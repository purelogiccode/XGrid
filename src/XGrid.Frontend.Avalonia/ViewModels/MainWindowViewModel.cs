using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Input;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Application shell: owns settings, the xemu core session, navigation, and live status.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly XemuCoreSession _core;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly Lock _coreFrameLock = new();
    private byte[]? _coreFrameBuffer;
    private int _coreFrameWidth;
    private int _coreFrameHeight;
    private bool _coreFramePending;
    private bool _coreFrameLogged;
    private readonly string _coreLabel = "xemu 0.8.139";
    private GamepadMappingOptions _gamepadOptions = GamepadMappingOptions.Default;
    private int _coreDisplayWidth = 640;
    private int _coreDisplayHeight = 480;
    private bool _lastFrameSinkActive;
    private bool _shuttingDown;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusDetail = "No console powered on";

    [ObservableProperty]
    private WriteableBitmap? _frameBitmap;

    [ObservableProperty]
    private bool _hasFrame;

    /// <summary>The shell window, kept for window screenshots.</summary>
    private Window? _window;

    [ObservableProperty]
    private GameEntry? _currentGame;

    [ObservableProperty]
    private string? _toastMessage;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _showLogDock;

    /// <summary>Initializes the shell with the default per-user settings store.</summary>
    public MainWindowViewModel()
        : this(new SettingsStore())
    {
    }

    /// <summary>Initializes the shell with an explicit settings store (tests and tools).</summary>
    private MainWindowViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Config = settingsStore.Load();
        _gamepadOptions = InputMapping.Create(Config.Input);
        _core = new XemuCoreSession();
        _core.Log += OnCoreLog;
        _core.FrameReady += OnCoreFrameReady;
        _core.Stopped += OnCoreStopped;

        Home = new HomeViewModel(this);
        Settings = new SettingsViewModel(this);
        Library = new LibraryViewModel(this);
        Logs = new LogsViewModel(this);
        About = new AboutViewModel(this);

        NavigationItems =
        [
            new NavigationItem(PageKind.Home, "Home", AppIcons.Home, Home),
            new NavigationItem(PageKind.Library, "Library", AppIcons.Library, Library),
            new NavigationItem(PageKind.Settings, "Settings", AppIcons.Settings, Settings),
            new NavigationItem(PageKind.Logs, "Activity", AppIcons.Logs, Logs),
            new NavigationItem(PageKind.About, "About", AppIcons.Info, About),
        ];
        SelectedNavigationItem = NavigationItems[0];

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += OnUiTick;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
        _toastTimer.Tick += OnToastTick;
    }

    /// <summary>Gets the persisted configuration.</summary>
    public EmulatorSettings Config { get; private set; }

    /// <summary>Gets the home page.</summary>
    public HomeViewModel Home { get; }

    /// <summary>Gets the library page.</summary>
    public LibraryViewModel Library { get; }

    /// <summary>Gets the settings page.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Gets the activity log page.</summary>
    public LogsViewModel Logs { get; }

    /// <summary>Gets the about page.</summary>
    public AboutViewModel About { get; }

    /// <summary>Gets the sidebar navigation items.</summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>Gets all activity log entries (newest last).</summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    /// <summary>Gets the file dialog service.</summary>
    public FileDialogService Dialogs { get; } = new();

    /// <summary>Gets the settings file path shown in the UI.</summary>
    public string SettingsPath => _settingsStore.FilePath;

    /// <summary>Gets a value indicating whether a BIOS image is configured.</summary>
    public bool IsBiosReady => Config.IsBiosReady;

    /// <summary>Gets a value indicating whether the native core display should be shown.</summary>
    public bool ShowCoreDisplay => IsRunning && !_core.IsFrameSinkActive;

    /// <summary>Gets a value indicating whether the console page is selected.</summary>
    public bool IsHomePage => SelectedNavigationItem?.Kind == PageKind.Home;

    /// <summary>Gets a value indicating whether the library page is selected.</summary>
    public bool IsLibraryPage => SelectedNavigationItem?.Kind == PageKind.Library;

    /// <summary>Gets a value indicating whether the settings page is selected.</summary>
    public bool IsSettingsPage => SelectedNavigationItem?.Kind == PageKind.Settings;

    /// <summary>Gets a value indicating whether the activity page is selected.</summary>
    public bool IsLogsPage => SelectedNavigationItem?.Kind == PageKind.Logs;

    /// <summary>Gets a value indicating whether the about page is selected.</summary>
    public bool IsAboutPage => SelectedNavigationItem?.Kind == PageKind.About;

    /// <summary>Gets a value indicating whether firmware setup is needed.</summary>
    public bool NeedsSetup => !Config.IsBiosReady;

    /// <summary>Gets the supporting copy shown on the home page.</summary>
    public string HomeDetail
    {
        get
        {
            if (!Config.IsBiosReady)
            {
                return "Add your Xbox BIOS image to bring the emulated console to life.";
            }

            return IsRunning
                ? CurrentGame is not null
                    ? CurrentGame.Title + " is powered on."
                    : "The xemu core is running the Xbox dashboard below."
                : CurrentGame is not null
                ? "Power on again or choose another disc image."
                : "Insert a disc image, or power on to reach the dashboard.";
        }
    }

    /// <summary>Gets the placeholder shown before the first frame is presented.</summary>
    public string FramePlaceholder => IsRunning
        ? "Starting the xemu core."
        : "Power on the console to see output here.";

    /// <summary>Gets the caption under the display.</summary>
    public string DisplayCaption => "640 x 480 - xemu 0.8.139 (" + RendererName + ")";

    /// <summary>Gets the power button label.</summary>
    public string PowerButtonText => IsRunning ? "Power off" : "Power on";

    /// <summary>Gets the current game title (or a console default).</summary>
    public string CurrentGameTitle => CurrentGame?.Title ?? "Xbox dashboard";

    /// <summary>Gets a value indicating whether a disc is selected.</summary>
    public bool HasCurrentGame => CurrentGame is not null;

    /// <summary>Gets the current game format label.</summary>
    public string CurrentGameFormat => CurrentGame?.FormatText ?? "No disc";

    /// <summary>Gets the emulation core label for the status bar.</summary>
    public string CoreLabel => _coreLabel;

    /// <summary>Gets the renderer label for the status bar.</summary>
    public string RendererLabel => RendererName + " - xemu";

    private string RendererName => Config.Graphics.Renderer switch
    {
        GraphicsSettings.RendererVulkan => "Vulkan",
        GraphicsSettings.RendererNull => "Null",
        _ => "OpenGL",
    };

    /// <summary>Gets the short run state shown in the status bar.</summary>
    public string RunStateLabel => IsRunning ? "Running" : "Ready";

    /// <summary>Gets a value indicating whether the frame image should be scaled smoothly.</summary>
    public bool SmoothFrame => FrameFilters.UsesBilinearSampling(Config.Filter);

    /// <summary>Gets the gamepad options for the current configuration.</summary>
    public GamepadMappingOptions GamepadOptions => _gamepadOptions;

    /// <summary>Maps a keyboard key to the configured player-1 action.</summary>
    /// <param name="key">Pressed key.</param>
    /// <param name="action">Mapped action.</param>
    public bool TryMapKey(Key key, out PadAction action)
    {
        return InputMapping.TryMapKey(Config.Input, key, out action);
    }

    /// <summary>Overlays the pressed keyboard keys on the gamepad report.</summary>
    /// <param name="gamepad">Gamepad report.</param>
    /// <param name="pressed">Pressed keys.</param>
    public HostPad MergeKeyboard(HostPad gamepad, IEnumerable<Key> pressed)
    {
        return InputMapping.MergeKeyboard(gamepad, Config.Input, pressed);
    }

    /// <summary>Attaches the shell to the main window and performs first-run initialization.</summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        Dialogs.Window = window;
        window.Closing += OnWindowClosing;

        AccentService.Apply(AccentPalette.Find(Config.Accent));
        Settings.LoadFrom(Config);
        AddLog(LogLevel.Info, "XGrid frontend ready - " + RuntimeInformation.FrameworkDescription);
        AddLog(
            Config.IsBiosReady ? LogLevel.Info : LogLevel.Warning,
            Config.IsBiosReady
                ? "Firmware configured: " + Config.BiosPath
                : "Firmware is not configured yet. Open Settings and choose a BIOS image.");

        _uiTimer.Start();
        _ = Library.RefreshAsync();
    }

    /// <summary>Stops the emulator and releases resources.</summary>
    private void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _uiTimer.Stop();
        _toastTimer.Stop();
        _core.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Shutdown();
    }

    /// <summary>Navigates to a destination.</summary>
    public void NavigateTo(PageKind kind)
    {
        foreach (NavigationItem item in NavigationItems)
        {
            if (item.Kind == kind)
            {
                SelectedNavigationItem = item;
                return;
            }
        }
    }

    /// <summary>Appends a log line and mirrors it to the activity page.</summary>
    public void AddLog(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        LogEntries.Add(entry);
        while (LogEntries.Count > 2000)
        {
            LogEntries.RemoveAt(0);
        }

        Logs.NotifyEntryAdded(entry);
    }

    /// <summary>Shows a transient toast message.</summary>
    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Saves the supplied settings, applying them immediately.</summary>
    public void SaveSettings(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Config = settings;
        _gamepadOptions = InputMapping.Create(Config.Input);
        try
        {
            _settingsStore.Save(Config);
            Settings.LoadFrom(Config);
            AccentService.Apply(AccentPalette.Find(Config.Accent));
            NotifyConfigChanged();
            AddLog(LogLevel.Info, "Settings saved to " + _settingsStore.FilePath);
            ShowToast("Settings saved");
            _ = Library.RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Error, "Could not save settings: " + exception.Message);
            ShowToast("Could not save settings");
        }
    }

    /// <summary>Applies an accent palette without persisting it (live preview).</summary>
    public static void PreviewAccent(string key)
    {
        AccentService.Apply(AccentPalette.Find(key));
    }

    /// <summary>Adds a library folder, persists it, and rescans.</summary>
    public void AddLibraryFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)
            || Config.LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Config.LibraryFolders.Add(folder);
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the library folder: " + exception.Message);
        }

        Settings.LoadFrom(Config);
        AddLog(LogLevel.Info, "Library folder added: " + folder);
        _ = Library.RefreshAsync();
    }

    /// <summary>Public command: shows or hides the log dock.</summary>
    [RelayCommand]
    private void ToggleLogDock()
    {
        ShowLogDock = !ShowLogDock;
    }

    /// <summary>Navigates to a destination (menu and toolbar entry point).</summary>
    /// <param name="kind">Destination page.</param>
    [RelayCommand]
    private void Navigate(PageKind kind)
    {
        NavigateTo(kind);
    }

    /// <summary>Forwards the current keyboard/controller pad state to the core.</summary>
    public void SetPad(ushort buttons, ReadOnlySpan<byte> analog, ReadOnlySpan<short> sticks)
    {
        ForwardCorePad(buttons, analog, sticks);
    }

    /// <summary>Maps the shell pad onto xemu's XID mask and publishes it to the core.</summary>
    private void ForwardCorePad(ushort buttons, ReadOnlySpan<byte> analog, ReadOnlySpan<short> sticks)
    {
        if (!IsRunning || analog.Length < 8 || sticks.Length < 4)
        {
            return;
        }

        uint mask = 0;
        if (analog[0] > 64) { mask |= 1u << 0; }  // A
        if (analog[1] > 64) { mask |= 1u << 1; }  // B
        if (analog[2] > 64) { mask |= 1u << 2; }  // X
        if (analog[3] > 64) { mask |= 1u << 3; }  // Y
        if ((buttons & 0x0004) != 0) { mask |= 1u << 4; } // LEFT
        if ((buttons & 0x0001) != 0) { mask |= 1u << 5; } // UP
        if ((buttons & 0x0008) != 0) { mask |= 1u << 6; } // RIGHT
        if ((buttons & 0x0002) != 0) { mask |= 1u << 7; } // DOWN
        if ((buttons & 0x0020) != 0) { mask |= 1u << 8; } // BACK
        if ((buttons & 0x0010) != 0) { mask |= 1u << 9; } // START
        if (analog[5] > 64) { mask |= 1u << 10; } // WHITE
        if (analog[4] > 64) { mask |= 1u << 11; } // BLACK
        if ((buttons & 0x0040) != 0) { mask |= 1u << 12; } // LTHUMB
        if ((buttons & 0x0080) != 0) { mask |= 1u << 13; } // RTHUMB

        int lt = analog[6] * 32767 / 255;
        int rt = analog[7] * 32767 / 255;
        _core.SetPad(mask, sticks[0], sticks[1], sticks[2], sticks[3], lt, rt);
    }

    /// <summary>
    /// Reports the native display surface the core should be embedded in.
    /// Called by the home view when the surface is created or resized.
    /// </summary>
    /// <param name="handle">Host child window handle.</param>
    /// <param name="width">Width in physical pixels.</param>
    /// <param name="height">Height in physical pixels.</param>
    public void AttachCoreDisplay(IntPtr handle, int width, int height)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _coreDisplayWidth = Math.Max(1, width);
        _coreDisplayHeight = Math.Max(1, height);
        _core.AttachDisplay(handle, _coreDisplayWidth, _coreDisplayHeight);
    }

    /// <summary>Reports that the native display surface was destroyed.</summary>
    public void DetachCoreDisplay()
    {
        _core.DetachDisplay();
    }

    /// <summary>Builds and starts the core for a title, logging why it cannot.</summary>
    private void StartCore(GameEntry? game)
    {
        string workingDirectory = Path.Combine(SettingsStore.ResolveDirectory(), "xemu");
        EmulatorSettings effective = game is null
            ? Config
            : Config.WithTitleOverride(game.Path, game.TitleId == 0 ? null : game.TitleId);
        string? problem = XemuLaunchPlan.DescribeProblem(effective, game?.Path, workingDirectory);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            effective,
            game?.Path,
            _coreDisplayWidth,
            _coreDisplayHeight,
            workingDirectory);
        FrontendTrace.Write($"sink='{launch.DisplaySinkName}' setting={Config.DisplaySink}");
        _core.Start(launch);
        StatusText = "Running";
        StatusDetail = "xemu core - " + (game?.Title ?? "Xbox dashboard");
        AddLog(
            LogLevel.Info,
            $"xemu core {Path.GetFileName(launch.CorePath)} - {launch.Renderer} - {launch.RamMb} MB - "
            + (string.IsNullOrEmpty(launch.DvdPath) ? "no disc" : Path.GetFileName(launch.DvdPath))
            + (string.Equals(launch.AudioBackend, "wav", StringComparison.Ordinal)
                ? " - WAV audio: " + Path.GetFileName(launch.AudioWavPath)
                : string.Empty));
    }

    /// <summary>Public command: powers the console on, or off when already running.</summary>
    [RelayCommand]
    private void TogglePower()
    {
        if (IsRunning)
        {
            StopSession();
        }
        else
        {
            StartGame(null);
        }
    }

    /// <summary>
    /// Public command: writes the whole shell window to the <c>screenshots</c>
    /// folder as a PNG, whether or not the console is powered on.
    /// </summary>
    [RelayCommand]
    private void SaveScreenshot()
    {
        if (_window is null)
        {
            ShowToast("The window is not ready yet.");
            return;
        }

        try
        {
            string directory = ScreenshotNaming.Directory(SettingsStore.ResolveDirectory());
            _ = Directory.CreateDirectory(directory);
            string path = ScreenshotNaming.NextPath(directory, DateTimeOffset.Now);

            // Render the window's own visual tree, so the capture does not depend
            // on the window being unobstructed and matches the display scaling.
            double scaling = _window.RenderScaling > 0 ? _window.RenderScaling : 1.0;
            var size = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(_window.ClientSize.Width * scaling)),
                Math.Max(1, (int)Math.Ceiling(_window.ClientSize.Height * scaling)));
            using (var target = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling)))
            {
                target.Render(_window);
                target.Save(path, PngBitmapEncoderOptions.Default);
            }

            AddLog(LogLevel.Info, "Screenshot saved to " + path);
            ShowToast("Screenshot saved: " + Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException
                                              or InvalidOperationException)
        {
            AddLog(LogLevel.Error, "Could not save the screenshot: " + exception.Message);
            ShowToast("Could not save the screenshot.");
        }
    }

    /// <summary>
    /// Public command: opens the folder that holds the shell's configuration
    /// (<c>settings.json</c> and the generated core configuration).
    /// </summary>
    [RelayCommand]
    private void OpenConfigFolder()
    {
        OpenFolder(SettingsStore.ResolveDirectory(), "configuration");
    }

    /// <summary>Public command: opens the folder that holds the screenshots.</summary>
    [RelayCommand]
    private void OpenScreenshotsFolder()
    {
        OpenFolder(ScreenshotNaming.Directory(SettingsStore.ResolveDirectory()), "screenshots");
    }

    private void OpenFolder(string path, string description)
    {
        try
        {
            FolderLauncher.Open(path);
            AddLog(LogLevel.Info, "Opened the " + description + " folder: " + path);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            AddLog(LogLevel.Error, "Could not open the " + description + " folder: " + exception.Message);
            ShowToast("Could not open the folder.");
        }
    }

    /// <summary>Public command: starts a specific disc entry (or the configured disc).</summary>
    [RelayCommand]
    private void StartGame(GameEntry? game)
    {
        if (_core.IsRunning)
        {
            ShowToast("Power off the console before launching another disc.");
            return;
        }

        if (!Config.IsBiosReady)
        {
            AddLog(LogLevel.Warning, "Power-on refused: no BIOS image is configured.");
            ShowToast("Choose a BIOS image first.");
            NavigateTo(PageKind.Settings);
            return;
        }

        if (game is not null && !game.IsLaunchable)
        {
            ShowToast("Loose XBE files must be wrapped in an ISO or CHD image.");
            return;
        }

        try
        {
            CurrentGame = game;
            StartCore(game);

            OnPropertyChanged(nameof(CurrentGameTitle));
            OnPropertyChanged(nameof(CurrentGameFormat));
            OnPropertyChanged(nameof(HomeDetail));
            IsRunning = true;
            StatusText = "Running";
            StatusDetail = game?.Title ?? "Xbox dashboard";
            FrameBitmap = null;
            HasFrame = false;
            AddLog(LogLevel.Info, game is null
                ? "Powering on the console."
                : "Launching " + game.Title + ".");

            NavigateTo(PageKind.Home);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or XemuConfigurationException)
        {
            IsRunning = false;
            HasFrame = false;
            FrameBitmap = null;
            StatusText = "Idle";
            StatusDetail = "Power-on failed";
            AddLog(LogLevel.Error, "Power-on failed: " + exception.Message);
            ShowToast("Power-on failed: " + exception.Message);
        }
    }

    /// <summary>Public command: stops the running console.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopSession()
    {
        if (!IsRunning)
        {
            return;
        }

        AddLog(LogLevel.Info, "Stopping the console.");
        _core.Stop();
        StatusText = "Stopping";
    }

    /// <summary>Public command: stays on the home page.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        NavigateTo(PageKind.Settings);
    }

    /// <summary>Public command: picks a disc image and launches it.</summary>
    [RelayCommand]
    private async Task OpenDiscAsync()
    {
        string? path = await Dialogs.PickFileAsync("Choose a disc image", FileDialogService.DiscTypes);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!GameLibraryService.IsGameCandidate(path))
        {
            ShowToast("Choose an ISO, CHD, or XBE file.");
            return;
        }

        GameEntry? entry = GameEntry.FromFile(path);
        if (entry is null)
        {
            ShowToast("That file could not be read as a disc image.");
            return;
        }

        StartGame(entry);
    }

    private bool CanStop()
    {
        return IsRunning;
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(FramePlaceholder));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        OnPropertyChanged(nameof(RunStateLabel));
        StopSessionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        foreach (NavigationItem item in NavigationItems)
        {
            bool active = ReferenceEquals(item, value);
            item.IsSelected = active;
            item.Page.IsActive = active;
        }

        // A native child window is not clipped by the shell's visual tree; hide
        // it while another page is on top so it cannot cover the settings tabs.
        _core.SetDisplayVisible(value?.Kind == PageKind.Home);

        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsLibraryPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsLogsPage));
        OnPropertyChanged(nameof(IsAboutPage));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnStatusDetailChanged(string value)
    {
        OnPropertyChanged(nameof(HomeDetail));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnCurrentGameChanged(GameEntry? value)
    {
        OnPropertyChanged(nameof(CurrentGameTitle));
        OnPropertyChanged(nameof(CurrentGameFormat));
        OnPropertyChanged(nameof(HasCurrentGame));
        OnPropertyChanged(nameof(HomeDetail));
    }

    private void OnCoreFrameReady(object? sender, XemuDisplayFrame frame)
    {
        int length = frame.Width * frame.Height * 4;
        lock (_coreFrameLock)
        {
            // Latest wins: a slow UI must never queue frames.
            if (_coreFramePending)
            {
                return;
            }

            if (_coreFrameBuffer is null || _coreFrameBuffer.Length < length)
            {
                _coreFrameBuffer = new byte[length];
            }

            Buffer.BlockCopy(frame.Pixels, 0, _coreFrameBuffer, 0, length);
            _coreFrameWidth = frame.Width;
            _coreFrameHeight = frame.Height;
            _coreFramePending = true;
        }

        if (!_coreFrameLogged)
        {
            _coreFrameLogged = true;
            FrontendTrace.Write($"core frame {frame.Width}x{frame.Height} #{frame.Counter}");
        }
    }

    private bool TryTakeCoreFrame(out FramePacket? packet)
    {
        lock (_coreFrameLock)
        {
            if (!_coreFramePending || _coreFrameBuffer is null)
            {
                packet = null;
                return false;
            }

            _coreFramePending = false;
            packet = new FramePacket((uint)_coreFrameWidth, (uint)_coreFrameHeight, _coreFrameBuffer);
            return true;
        }
    }

    private void OnCoreLog(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => AddLog(LogLevel.Info, message));
    }

    private void OnCoreStopped(object? sender, XemuCoreStoppedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            HasFrame = false;
            FrameBitmap = null;
            StatusText = e.Faulted ? "Faulted" : "Off";
            StatusDetail = e.Message;
            AddLog(e.Faulted ? LogLevel.Error : LogLevel.Info, e.Message);
        });
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (TryTakeCoreFrame(out FramePacket? coreFrame))
        {
            PresentFrame(coreFrame!);
            HasFrame = true;
        }

        // The native host is only shown while the core paints into it. When the
        // frame sink takes over it must not sit above the frame image.
        bool sinkActive = _core.IsFrameSinkActive;
        if (sinkActive != _lastFrameSinkActive)
        {
            _lastFrameSinkActive = sinkActive;
            OnPropertyChanged(nameof(ShowCoreDisplay));
        }

        // The core owns the picture, its pacing and its input; the shell only
        // mirrors liveness so a crash is never silent.
        if (IsRunning && !_core.IsRunning)
        {
            IsRunning = false;
            HasFrame = false;
            return;
        }

        if (IsRunning)
        {
            HasFrame = _core.IsEmbedded || HasFrame;
        }
    }

    private void PresentFrame(FramePacket packet)
    {
        FrameFilters.Apply(Config.Filter, packet.Bgra, (int)packet.Width, (int)packet.Height);
        int width = (int)packet.Width;
        int height = (int)packet.Height;
        int rowBytes = width * 4;
        for (int index = 0; index < packet.Bgra.Length; index += 4)
        {
            packet.Bgra[index + 3] = 255;
        }

        if (FrameBitmap is null
            || FrameBitmap.PixelSize.Width != width
            || FrameBitmap.PixelSize.Height != height)
        {
            FrameBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        using (ILockedFramebuffer buffer = FrameBitmap.Lock())
        {
            if (buffer.RowBytes == rowBytes)
            {
                Marshal.Copy(packet.Bgra, 0, buffer.Address, rowBytes * height);
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(packet.Bgra, y * rowBytes, IntPtr.Add(buffer.Address, y * buffer.RowBytes), rowBytes);
                }
            }
        }

        if (!HasFrame)
        {
            HasFrame = true;
            OnPropertyChanged(nameof(FramePlaceholder));
        }
    }

    private void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(IsBiosReady));
        OnPropertyChanged(nameof(NeedsSetup));
        OnPropertyChanged(nameof(SettingsPath));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(FramePlaceholder));
        OnPropertyChanged(nameof(DisplayCaption));
        OnPropertyChanged(nameof(RendererLabel));
        OnPropertyChanged(nameof(SmoothFrame));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        StopSessionCommand.NotifyCanExecuteChanged();
    }

    private void OnToastTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        Shutdown();
    }
}
