using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Application shell: owns settings, the emulation session, navigation, and live status.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly EmulatorSession _session;
    private readonly XemuCoreSession _core;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly Lock _frameLock = new();
    private FramePacket? _pendingFrame;
    private long _lastSampleTicks;
    private long _lastSampleSteps;
    private long _lastSampleFrames;
    private int _coreDisplayWidth = 640;
    private int _coreDisplayHeight = 480;
    private bool _shuttingDown;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusDetail = "No console powered on";

    [ObservableProperty]
    private ulong _steps;

    [ObservableProperty]
    private ulong _frames;

    [ObservableProperty]
    private double _mips;

    [ObservableProperty]
    private double _fps;

    [ObservableProperty]
    private string _eip = "—";

    [ObservableProperty]
    private WriteableBitmap? _frameBitmap;

    [ObservableProperty]
    private bool _hasFrame;

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
        _session = new EmulatorSession();
        _session.Log += OnSessionLog;
        _session.FrameReady += OnFrameReady;
        _session.Stopped += OnSessionStopped;
        _core = new XemuCoreSession();
        _core.Log += OnCoreLog;
        _core.Stopped += OnCoreStopped;

        Home = new HomeViewModel(this);
        Settings = new SettingsViewModel(this);
        Library = new LibraryViewModel(this);
        Logs = new LogsViewModel(this);
        About = new AboutViewModel(this);

        NavigationItems =
        [
            new NavigationItem(PageKind.Home, "Home", "Console status", AppIcons.Home, Home),
            new NavigationItem(PageKind.Library, "Library", "Disc images", AppIcons.Library, Library),
            new NavigationItem(PageKind.Settings, "Settings", "Configure XGrid", AppIcons.Settings, Settings),
            new NavigationItem(PageKind.Logs, "Activity", "Emulator log", AppIcons.Logs, Logs),
            new NavigationItem(PageKind.About, "About", "Build information", AppIcons.Info, About),
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
    private LibraryViewModel Library { get; }

    /// <summary>Gets the settings page.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Gets the activity log page.</summary>
    private LogsViewModel Logs { get; }

    /// <summary>Gets the about page.</summary>
    private AboutViewModel About { get; }

    /// <summary>Gets the sidebar navigation items.</summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>Gets all activity log entries (newest last).</summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    /// <summary>Gets recently launched games (newest first).</summary>
    public ObservableCollection<GameEntry> RecentGames { get; } = [];

    /// <summary>Gets the file dialog service.</summary>
    public FileDialogService Dialogs { get; } = new();

    /// <summary>Gets the settings file path shown in the UI.</summary>
    public string SettingsPath => _settingsStore.FilePath;

    /// <summary>Gets a value indicating whether a BIOS image is configured.</summary>
    public bool IsBiosReady => Config.IsBiosReady;

    /// <summary>Gets a value indicating whether the selected core is the xemu core.</summary>
    public bool IsXemuCore => Config.UsesXemuCore;

    /// <summary>Gets a value indicating whether the native core display should be shown.</summary>
    public bool ShowCoreDisplay => IsRunning && IsXemuCore;

    /// <summary>Gets a value indicating whether firmware setup is needed.</summary>
    public bool NeedsSetup => !Config.IsBiosReady;

    /// <summary>Gets the current page title.</summary>
    public string PageTitle => SelectedNavigationItem?.Title ?? "Home";

    /// <summary>Gets the current page caption.</summary>
    public string PageCaption => SelectedNavigationItem?.Subtitle ?? string.Empty;

    /// <summary>Gets the headline shown on the home page.</summary>
    public string HomeHeadline
    {
        get
        {
            if (!Config.IsBiosReady)
            {
                return "Firmware required";
            }

            if (IsRunning && CurrentGame is not null)
            {
                return "Now playing";
            }

            return IsRunning ? "Console running" : CurrentGame is not null ? "Ready to play" : "Ready when you are";
        }
    }

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
                    : Config.UsesXemuCore
                        ? "The xemu core is running the Xbox dashboard below."
                        : "The Xbox dashboard is live below."
                : CurrentGame is not null
                ? "Power on again or choose another disc image."
                : "Insert a disc image, or power on to reach the dashboard.";
        }
    }

    /// <summary>Gets the placeholder shown before the first frame is presented.</summary>
    public string FramePlaceholder => IsRunning
        ? IsXemuCore
            ? "Starting the xemu core…"
            : "Waiting for the first presented frame…"
        : "Power on the console to see output here.";

    /// <summary>
    /// Gets the caption under the display: the picture is a 640x480 console
    /// scanout, rendered by the configured NV2A renderer.
    /// </summary>
    public string DisplayCaption => IsXemuCore
        ? "640 × 480 · xemu 0.8.139 core (OpenGL)"
        : "640 × 480 · " + RendererPolicy.Describe(Config.Renderer);

    /// <summary>Gets the power button label.</summary>
    public string PowerButtonText => IsRunning ? "Power off" : "Power on";

    /// <summary>Gets the pause button label.</summary>
    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    /// <summary>Gets the current game title (or a console default).</summary>
    public string CurrentGameTitle => CurrentGame?.Title ?? "Xbox dashboard";

    /// <summary>Gets the current game format label.</summary>
    public string CurrentGameFormat => CurrentGame?.FormatText ?? "No disc";

    /// <summary>Gets the current game size label.</summary>
    public string CurrentGameSize => CurrentGame?.SizeText ?? string.Empty;

    /// <summary>Attaches the shell to the main window and performs first-run initialization.</summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Dialogs.Window = window;
        window.Closing += OnWindowClosing;

        AccentService.Apply(AccentPalette.Find(Config.Accent));
        Settings.LoadFrom(Config);
        RefreshRecentGames();
        AddLog(LogLevel.Info, "XGrid frontend ready · " + RuntimeInformation.FrameworkDescription);
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
        _session.Dispose();
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
        try
        {
            _settingsStore.Save(Config);
            Settings.LoadFrom(Config);
            AccentService.Apply(AccentPalette.Find(Config.Accent));
            NotifyConfigChanged();
            RefreshRecentGames();
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

    /// <summary>Gets a value indicating whether the log dock is expanded.</summary>
    public bool IsLogDockOpen => ShowLogDock;

    /// <summary>Gets the emulation core label for the status bar.</summary>
    public string CoreLabel => Config.UsesXemuCore ? "xemu core" : "managed C# core";

    /// <summary>Gets the renderer label for the status bar.</summary>
    public string RendererLabel => Config.UsesXemuCore
        ? "OpenGL · xemu"
        : RendererPolicy.Describe(Config.Renderer);

    /// <summary>Gets the short run state shown in the status bar.</summary>
    public string RunStateLabel => !IsRunning ? "Ready" : IsPaused ? "Paused" : "Running";

    /// <summary>Gets the frame-rate text for the status bar.</summary>
    public string FpsText => IsRunning && !IsXemuCore
        ? Fps.ToString("0.0", CultureInfo.InvariantCulture) + " fps"
        : "—";

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

    /// <summary>Forwards the current keyboard pad state to the emulation thread.</summary>
    public void SetPad(ushort buttons, ReadOnlySpan<byte> analog, ReadOnlySpan<short> sticks)
    {
        if (Config.UsesXemuCore)
        {
            // The core window owns the keyboard and any XInput pad directly.
            return;
        }

        if (_session.IsRunning)
        {
            _session.SetPad(buttons, analog, sticks);
        }
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
        string? problem = XemuLaunchPlan.DescribeProblem(Config, game?.Path, workingDirectory);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            Config,
            game?.Path,
            _coreDisplayWidth,
            _coreDisplayHeight,
            workingDirectory);
        _core.Start(launch);
        StatusText = "Running";
        StatusDetail = "xemu core · " + (game?.Title ?? "Xbox dashboard");
        AddLog(
            LogLevel.Info,
            $"xemu core {Path.GetFileName(launch.CorePath)} · {launch.RamMb} MB · "
            + (string.IsNullOrEmpty(launch.DvdPath) ? "no disc" : Path.GetFileName(launch.DvdPath)));
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

    /// <summary>Public command: starts a specific disc entry (or the configured disc).</summary>
    [RelayCommand]
    private void StartGame(GameEntry? game)
    {
        if (_session.IsRunning)
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
            if (Config.UsesXemuCore)
            {
                StartCore(game);
            }
            else
            {
                _session.Start(Config, game?.Path);
            }

            OnPropertyChanged(nameof(CurrentGameTitle));
            OnPropertyChanged(nameof(CurrentGameFormat));
            OnPropertyChanged(nameof(CurrentGameSize));
            OnPropertyChanged(nameof(HomeHeadline));
            OnPropertyChanged(nameof(HomeDetail));
            IsRunning = true;
            IsPaused = false;
            StatusText = Config.UsesXemuCore ? "Running" : "Booting";
            StatusDetail = game?.Title ?? "Xbox dashboard";
            FrameBitmap = null;
            HasFrame = false;
            Steps = 0;
            Frames = 0;
            Mips = 0;
            Fps = 0;
            _lastSampleTicks = 0;
            AddLog(LogLevel.Info, game is null
                ? "Powering on the console…"
                : "Launching " + game.Title + "…");
            if (game is not null)
            {
                RememberGame(game.Path);
            }

            NavigateTo(PageKind.Home);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or XemuConfigurationException)
        {
            IsRunning = false;
            IsPaused = false;
            HasFrame = false;
            FrameBitmap = null;
            StatusText = "Idle";
            StatusDetail = "Power-on failed";
            AddLog(LogLevel.Error, "Power-on failed: " + exception.Message);
            ShowToast("Power-on failed: " + exception.Message);
        }
    }

    /// <summary>Public command: pauses or resumes the running console.</summary>
    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private void TogglePause()
    {
        if (IsXemuCore)
        {
            ShowToast("The xemu core manages its own pause (its window has focus).");
            return;
        }

        if (_session.IsPaused)
        {
            _session.Resume();
            IsPaused = false;
            StatusText = "Running";
            AddLog(LogLevel.Info, "Emulation resumed.");
        }
        else
        {
            _session.Pause();
            IsPaused = true;
            StatusText = "Paused";
            AddLog(LogLevel.Info, "Emulation paused.");
        }
    }

    /// <summary>Public command: advances one frame while the console is paused.</summary>
    [RelayCommand(CanExecute = nameof(CanStepFrame))]
    private void StepFrame()
    {
        if (_session.StepFrame())
        {
            AddLog(LogLevel.Info, "Stepping one frame.");
        }
    }

    /// <summary>Public command: advances a slice of guest time while paused.</summary>
    [RelayCommand(CanExecute = nameof(CanStepFrame))]
    private void StepInstructions()
    {
        if (_session.StepInstructions(1_000_000))
        {
            AddLog(LogLevel.Info, "Stepping 1,000,000 instructions.");
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

        AddLog(LogLevel.Info, "Stopping the console…");
        if (IsXemuCore)
        {
            _core.Stop();
            StatusText = "Stopping";
            return;
        }

        _session.Stop();
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

    private bool CanTogglePause()
    {
        return IsRunning && !IsXemuCore;
    }

    private bool CanStepFrame()
    {
        return IsRunning && IsPaused && !IsXemuCore;
    }

    private bool CanStop()
    {
        return IsRunning;
    }

    /// <summary>Public command: saves a snapshot of the running console.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveState))]
    private async Task SaveStateAsync()
    {
        if (!_session.IsRunning)
        {
            return;
        }

        string? path = await Dialogs.PickSaveFileAsync(
            "Save state",
            SuggestedStateName(),
            FileDialogService.StateTypes);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _session.SaveState(path);
            AddLog(LogLevel.Info, "State saved: " + path);
            ShowToast("State saved.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Error, "Save state failed: " + exception.Message);
            ShowToast("Save state failed: " + exception.Message);
        }
    }

    /// <summary>Public command: loads a snapshot into the running console.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveState))]
    private async Task LoadStateAsync()
    {
        if (!_session.IsRunning)
        {
            return;
        }

        string? path = await Dialogs.PickFileAsync("Load state", FileDialogService.StateTypes);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _session.LoadState(path);
            AddLog(LogLevel.Info, "State loaded: " + path);
            ShowToast("State loaded.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Error, "Load state failed: " + exception.Message);
            ShowToast("Load state failed: " + exception.Message);
        }
    }

    private bool CanSaveState()
    {
        return IsRunning;
    }

    private string SuggestedStateName()
    {
        string name = CurrentGame is { } game && !string.IsNullOrWhiteSpace(game.Title)
            ? game.Title
            : "xgrid";

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageCaption));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(HomeHeadline));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(FramePlaceholder));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        OnPropertyChanged(nameof(RunStateLabel));
        OnPropertyChanged(nameof(FpsText));
        TogglePauseCommand.NotifyCanExecuteChanged();
        StopSessionCommand.NotifyCanExecuteChanged();
        SaveStateCommand.NotifyCanExecuteChanged();
        LoadStateCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
        StepInstructionsCommand.NotifyCanExecuteChanged();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(RunStateLabel));
        StepFrameCommand.NotifyCanExecuteChanged();
        StepInstructionsCommand.NotifyCanExecuteChanged();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnShowLogDockChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLogDockOpen));
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
        OnPropertyChanged(nameof(CurrentGameSize));
        OnPropertyChanged(nameof(HomeHeadline));
        OnPropertyChanged(nameof(HomeDetail));
    }

    private void OnSessionLog(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => AddLog(LogLevel.Info, message));
    }

    private void OnFrameReady(object? sender, FramePacket packet)
    {
        lock (_frameLock)
        {
            _pendingFrame = packet;
        }
    }

    private void OnSessionStopped(object? sender, SessionStoppedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => HandleSessionStopped(e));
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
            IsPaused = false;
            HasFrame = false;
            FrameBitmap = null;
            Mips = 0;
            Fps = 0;
            StatusText = e.Faulted ? "Faulted" : "Off";
            StatusDetail = e.Message;
            AddLog(e.Faulted ? LogLevel.Error : LogLevel.Info, e.Message);
        });
    }

    private void HandleSessionStopped(SessionStoppedEventArgs e)
    {
        IsRunning = false;
        IsPaused = false;
        StatusText = e.Faulted ? "Faulted" : "Stopped";
        StatusDetail = e.Message;
        Mips = 0;
        Fps = 0;
        if (e.Faulted)
        {
            AddLog(LogLevel.Error, "Emulation stopped: " + e.Message);
            ShowToast("Emulation stopped: " + e.Message);
        }
        else
        {
            AddLog(LogLevel.Info, $"Emulation stopped after {e.Steps:N0} steps.");
        }
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        FramePacket? frame;
        lock (_frameLock)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }

        if (frame is not null)
        {
            PresentFrame(frame);
        }

        if (IsXemuCore)
        {
            // The core owns the picture, its pacing and its input; the shell
            // only mirrors liveness so a crash is never silent.
            if (IsRunning && !_core.IsRunning)
            {
                IsRunning = false;
                HasFrame = false;
            }

            if (IsRunning)
            {
                HasFrame = _core.IsEmbedded;
                Steps = 0;
                Frames = 0;
                Mips = 0;
                Fps = 0;
            }

            return;
        }

        if (!_session.IsRunning)
        {
            if (IsRunning)
            {
                IsRunning = false;
            }

            return;
        }

        long steps = _session.Steps;
        long frames = _session.Frames;
        long now = Stopwatch.GetTimestamp();
        if (_lastSampleTicks == 0)
        {
            _lastSampleTicks = now;
            _lastSampleSteps = steps;
            _lastSampleFrames = frames;
        }
        else
        {
            double seconds = (now - _lastSampleTicks) / (double)Stopwatch.Frequency;
            if (seconds >= 0.25)
            {
                double instantMips = (steps - _lastSampleSteps) / seconds / 1_000_000.0;
                double instantFps = (frames - _lastSampleFrames) / seconds;
                Mips = Mips <= 0 ? instantMips : (Mips * 0.65) + (instantMips * 0.35);
                Fps = Fps <= 0 ? instantFps : (Fps * 0.65) + (instantFps * 0.35);
                _lastSampleTicks = now;
                _lastSampleSteps = steps;
                _lastSampleFrames = frames;
            }
        }

        Steps = (ulong)Math.Max(0, steps);
        Frames = (ulong)Math.Max(0, frames);
        OnPropertyChanged(nameof(FpsText));
        Eip = string.Format(CultureInfo.InvariantCulture, "0x{0:X8}", _session.Eip);
        if (IsPaused != _session.IsPaused)
        {
            IsPaused = _session.IsPaused;
        }
    }

    private void PresentFrame(FramePacket packet)
    {
        int width = (int)packet.Width;
        int height = (int)packet.Height;
        int rowBytes = width * 4;
        bool hasVisibleColor = false;
        for (int index = 0; index < packet.Bgra.Length; index += 4)
        {
            packet.Bgra[index + 3] = 255;
            if ((packet.Bgra[index] | packet.Bgra[index + 1] | packet.Bgra[index + 2]) != 0)
            {
                hasVisibleColor = true;
            }
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

        if (hasVisibleColor && string.Equals(StatusText, "Booting", StringComparison.Ordinal))
        {
            StatusText = "Running";
        }
    }

    private void RememberGame(string path)
    {
        Config.RecentGames.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        Config.RecentGames.Insert(0, path);
        while (Config.RecentGames.Count > 8)
        {
            Config.RecentGames.RemoveAt(Config.RecentGames.Count - 1);
        }

        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save recent games: " + exception.Message);
        }

        RefreshRecentGames();
    }

    private void RefreshRecentGames()
    {
        RecentGames.Clear();
        foreach (string path in Config.RecentGames)
        {
            GameEntry? entry = GameEntry.FromFile(path);
            if (entry is not null)
            {
                RecentGames.Add(entry);
            }
        }
    }

    private void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(IsBiosReady));
        OnPropertyChanged(nameof(NeedsSetup));
        OnPropertyChanged(nameof(SettingsPath));
        OnPropertyChanged(nameof(HomeHeadline));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(FramePlaceholder));
        OnPropertyChanged(nameof(DisplayCaption));
        OnPropertyChanged(nameof(IsXemuCore));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        TogglePauseCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
        StepInstructionsCommand.NotifyCanExecuteChanged();
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
