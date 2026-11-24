using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
using Serilog;
using XGrid.Core.Xemu;
using XGrid.Diagnostics.Donations;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Frontend.Avalonia.Views;
using XGrid.Input;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Application shell: owns settings, the xemu core session, navigation, and live status.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly LaunchArguments _launchOptions;
    private readonly XemuCoreSession _core;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly Lock _coreFrameLock = new();
    private byte[]? _coreFrameBuffer;
    private byte[]? _coreFrameSpare;
    private int _coreFrameWidth;
    private int _coreFrameHeight;
    private bool _coreFramePending;
    private bool _coreFrameLogged;
    private int _framesArrived;
    private int _framesPresented;
    private long _presentTicks;
    private long _lastPresentTimestamp;
    private long _frameWindowStart;
    private const int FrameReportPeriodMs = 5000;
    private readonly FrameRateMeter _fpsMeter = new();

    /// <summary>The pace Space holds the console at (four times real time).</summary>
    public const int FastForwardMultiplier = 4;
    private GamepadMappingOptions _gamepadOptions;
    private readonly XInputGamepadReader?[] _gamepads = new XInputGamepadReader?[XemuInputSink.PadCount];
    private readonly HostPad[] _gamepadPads = CreateNeutralPads();
    private readonly bool[] _gamepadLogged = new bool[XemuInputSink.PadCount];
    private IReadOnlyList<Key> _pressedKeys = [];
    private int _coreDisplayWidth = 640;
    private int _coreDisplayHeight = 480;
    private int _frameWidth;
    private int _frameHeight;
    private bool _lastFrameSinkActive;
    private bool _shuttingDown;
    private SaveStateRunner? _saveStates;
    private PlaybackController? _playback;
    private DiscChangeRunner? _discs;
    private string? _sessionFilter;
    private bool _firstRunPending;
    private bool _saveStatesNeedRefresh;
    private CoreCapabilities? _coreCapabilities;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isFastForward;

    [ObservableProperty]
    private int _selectedSaveSlot = 1;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private string _statusDetail = "No console powered on";

    [ObservableProperty]
    private string _fpsText = "-- FPS";

    [ObservableProperty]
    private string _frameTimeText = string.Empty;

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

    [ObservableProperty]
    private bool _isDonationCheckInProgress;

    [ObservableProperty]
    private string _donationStatus =
        "XGrid is free donationware. If it helps you, please consider supporting development.";

    /// <summary>Initializes the shell with the default per-user settings store.</summary>
    public MainWindowViewModel()
        : this(new SettingsStore())
    {
    }

    /// <summary>Initializes the shell with an explicit settings store (tests and tools).</summary>
    private MainWindowViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _launchOptions = App.LaunchOptions;
        Config = settingsStore.Load();
        _firstRunPending = !settingsStore.ExistedAtLoad && !App.Autostart;
        _gamepadOptions = InputMapping.Create(Config.Input);
        ConfigureGamepads();
        _core = new XemuCoreSession();
        _core.Log += OnCoreLog;
        _core.FrameReady += OnCoreFrameReady;
        _core.Stopped += OnCoreStopped;

        Home = new HomeViewModel(this);
        Settings = new SettingsViewModel(this);
        Library = new LibraryViewModel(this);
        Library.RefreshRecentGames();
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
        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FramePacing.PollIntervalMilliseconds) };
        _frameTimer.Tick += OnFrameTick;
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

    /// <summary>Gets a value indicating whether a BIOS image is available for this session.</summary>
    public bool IsBiosReady => IsBiosReadyForSession;

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
    public bool NeedsSetup => !IsBiosReadyForSession;

    /// <summary>
    /// Gets a value indicating whether the optional donation banner is shown.
    /// It is hidden only after the service confirms a completed donation, and
    /// while fullscreen gameplay hides the shell chrome.
    /// </summary>
    public bool ShowDonationBanner => !Config.HasDonated && !IsFullscreen;

    /// <summary>
    /// Gets or sets a value indicating whether the frame-rate overlay is
    /// enabled. The choice is persisted in <c>settings.json</c>.
    /// </summary>
    public bool ShowFpsOverlay
    {
        get => Config.ShowFpsOverlay;
        set
        {
            if (Config.ShowFpsOverlay == value)
            {
                return;
            }

            Config.ShowFpsOverlay = value;
            try
            {
                _settingsStore.Save(Config);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AddLog(LogLevel.Warning, "Could not save the FPS overlay setting: " + exception.Message);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFpsOverlayVisible));
            if (value && IsRunning && !_core.IsFrameSinkActive)
            {
                ShowToast("The FPS overlay needs 'Present core frames in the shell'.");
            }
        }
    }

    /// <summary>Gets a value indicating whether the overlay is drawn over a running console.</summary>
    /// <remarks>
    /// The meter counts frames the shell presents. With the embedded core window
    /// the shell never sees a frame, so the overlay stays hidden instead of
    /// showing a permanent placeholder.
    /// </remarks>
    public bool IsFpsOverlayVisible => ShowFpsOverlay && IsRunning && _core.IsFrameSinkActive;

    /// <summary>Gets the supporting copy shown on the home page.</summary>
    public string HomeDetail
    {
        get
        {
            if (!IsBiosReadyForSession)
            {
                return "Add your Xbox BIOS image to bring the emulated console to life.";
            }

            return IsRunning
                ? CurrentGame is not null
                    ? CurrentGame.Title + " is powered on."
                    : "The emulator is running the Xbox dashboard below."
                : CurrentGame is not null
                ? "Power on again or choose another disc image."
                : "Insert a disc image, or power on to reach the dashboard.";
        }
    }

    /// <summary>Gets the placeholder shown before the first frame is presented.</summary>
    public string FramePlaceholder => IsRunning
        ? "Starting the console."
        : "Power on the console to see output here.";

    /// <summary>Gets the caption under the display, read from the presented frame.</summary>
    public string DisplayCaption => HasFrame
        ? DisplayCaptionText.Describe(_frameWidth, _frameHeight, RendererName)
        : string.Empty;

    /// <summary>Gets a value indicating whether there is a caption to show.</summary>
    public bool HasDisplayCaption => DisplayCaption.Length > 0;

    /// <summary>Gets the power button label.</summary>
    public string PowerButtonText => IsRunning ? "Power off" : "Power on";

    /// <summary>Gets the current game title (or a console default).</summary>
    public string CurrentGameTitle => CurrentGame?.Title ?? "Xbox dashboard";

    /// <summary>Gets a value indicating whether a disc is selected.</summary>
    public bool HasCurrentGame => CurrentGame is not null;

    /// <summary>Gets the current game format label.</summary>
    public string CurrentGameFormat => CurrentGame?.FormatText ?? "No disc";

    /// <summary>Gets the product label for the title bar badge and the status bar.</summary>
    public string AppLabel { get; } =
        "XGrid v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    /// <summary>Gets the renderer label for the status bar.</summary>
    public string RendererLabel => RendererName;

    /// <summary>Gets a value indicating whether the running core can save states.</summary>
    public bool HasSaveStates => _saveStates?.IsSupported == true;

    /// <summary>Gets a value indicating whether the running core can pause and step.</summary>
    public bool HasPlayback => _playback?.IsSupported == true;

    /// <summary>Gets a value indicating whether the running core can change discs.</summary>
    public bool HasDiscChange => _discs?.IsSupported == true;

    /// <summary>Gets the disc button hint for the current state.</summary>
    public string OpenDiscHint => IsRunning
        ? HasDiscChange
            ? "Change the inserted disc (Ctrl+O)"
            : "Choose a disc image; changing discs needs in-process hosting"
        : "Choose the disc image to boot";

    /// <summary>Gets the eject hint for the current state.</summary>
    public string EjectDiscHint => HasDiscChange
        ? "Eject the inserted disc (Ctrl+E)"
        : "Ejecting needs the in-process emulator core";

    /// <summary>Gets the pause button label for the current state.</summary>
    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    /// <summary>Gets the pause toolbar hint for the current state.</summary>
    public string PauseHint => IsPaused ? "Resume the console (P)" : "Pause the console (P)";

    /// <summary>Gets the frame-step toolbar hint.</summary>
    public string StepFrameHint => IsPaused
        ? "Advance one frame (F10)"
        : "Pause the console to step one frame at a time (F10)";

    /// <summary>Gets the quick-save menu label for the selected slot.</summary>
    public string QuickSaveHeader => "Quick Save (Slot " + SelectedSaveSlot.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Gets the quick-load menu label for the selected slot.</summary>
    public string QuickLoadHeader => "Quick Load (Slot " + SelectedSaveSlot.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Gets a hint for the console page's save button.</summary>
    public string SaveStateHint =>
        "Save the machine to slot " + SelectedSaveSlot.ToString(CultureInfo.InvariantCulture) + " (F6)";

    /// <summary>Gets a hint for the console page's load button.</summary>
    public string LoadStateHint =>
        "Restore slot " + SelectedSaveSlot.ToString(CultureInfo.InvariantCulture) + " (F7)";

    private string RendererName => XemuLaunchPlan.ResolveRenderer(Config.Graphics.Renderer, _coreCapabilities) switch
    {
        GraphicsSettings.RendererVulkan => "Vulkan",
        GraphicsSettings.RendererNull => "Null",
        _ => "OpenGL",
    };

    /// <summary>Gets the short run state shown in the status bar.</summary>
    public string RunStateLabel => IsRunning
        ? IsPaused
            ? "Paused"
            : IsFastForward
                ? $"Running {FastForwardMultiplier}x"
                : "Running"
        : "Ready";

    /// <summary>Gets a value indicating whether the frame image should be scaled smoothly.</summary>
    public bool SmoothFrame => FrameFilters.UsesBilinearSampling(ActiveFilter);

    /// <summary>
    /// Gets the presentation filter for the session: the per-title value the
    /// launch resolved, or the global setting when nothing is running.
    /// </summary>
    private string ActiveFilter => _sessionFilter ?? Config.Filter;

    /// <summary>
    /// Raised after a core frame has been copied into <see cref="FrameBitmap"/>.
    /// A bitmap write is invisible to Avalonia on its own (the image caches a
    /// snapshot until its visual is invalidated), so the view re-renders the
    /// picture on this event.
    /// </summary>
    public event EventHandler? FramePresented;

    /// <summary>Maps a keyboard key to the configured player-1 action.</summary>
    /// <param name="key">Pressed key.</param>
    /// <param name="action">Mapped action.</param>
    public bool TryMapKey(Key key, out PadAction action)
    {
        return InputMapping.TryMapKey(Config.Input, key, out action);
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
        RefreshCoreCapabilities();
        AddLog(LogLevel.Info, "XGrid frontend ready - " + RuntimeInformation.FrameworkDescription);
        AddLog(
            IsBiosReadyForSession ? LogLevel.Info : LogLevel.Warning,
            IsBiosReadyForSession
                ? "Firmware configured: " + (_launchOptions.BiosPath ?? Config.BiosPath)
                : "Firmware is not configured yet. Open Settings and choose a BIOS image.");

        _uiTimer.Start();
        _ = Library.RefreshAsync();

        if (_firstRunPending)
        {
            if (window.IsVisible)
            {
                _ = ShowFirstRunAsync(window);
            }
            else
            {
                window.Opened += OnMainWindowOpened;
            }
        }

        // A saved donation email is refreshed silently so a donation completed
        // after the last launch hides the banner without user action.
        if (!Config.HasDonated && !string.IsNullOrWhiteSpace(Config.DonationEmail))
        {
            if (window.IsVisible)
            {
                _ = RefreshDonationStatusAsync(showMessages: false);
            }
            else
            {
                window.Opened += OnDonationWindowOpened;
            }
        }
    }

    private void OnDonationWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnDonationWindowOpened;
        }

        _ = RefreshDonationStatusAsync(showMessages: false);
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnMainWindowOpened;
            _ = ShowFirstRunAsync(window);
        }
    }

    /// <summary>Shows the first-run wizard once, on a fresh settings folder.</summary>
    /// <param name="owner">The main window.</param>
    private async Task ShowFirstRunAsync(Window owner)
    {
        try
        {
            var wizard = new FirstRunWindow();
            var model = new FirstRunViewModel(this, wizard);
            model.Completed += (_, _) => wizard.Close();
            wizard.DataContext = model;
            await wizard.ShowDialog(owner);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            AddLog(LogLevel.Warning, "Could not show the first-run setup: " + exception.Message);
        }
        finally
        {
            // Closing the window with the title-bar button is "set up later";
            // saving now keeps the wizard from returning on every launch.
            if (_firstRunPending)
            {
                CompleteFirstRun(null, null, null, null);
            }
        }
    }

    /// <summary>
    /// Applies the first-run choices, saves the settings and refreshes the
    /// pages that read firmware and library state.
    /// </summary>
    /// <param name="biosPath">Chosen BIOS image, or null to keep the current one.</param>
    /// <param name="eepromPath">Chosen EEPROM image, or null to keep the current one.</param>
    /// <param name="hddPath">Chosen hard-disk image, or null to keep the current one.</param>
    /// <param name="libraryFolder">Game folder to add, or null.</param>
    public void CompleteFirstRun(
        string? biosPath, string? eepromPath, string? hddPath, string? libraryFolder)
    {
        _firstRunPending = false;
        FirstRunSetup.Apply(Config, biosPath, eepromPath, hddPath, libraryFolder);
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the first-run settings: " + exception.Message);
        }

        RefreshCoreCapabilities();
        Settings.LoadFrom(Config);
        NotifyConfigChanged();
        AddLog(LogLevel.Info, IsBiosReadyForSession
            ? "First-run setup finished; firmware configured: " + (_launchOptions.BiosPath ?? Config.BiosPath)
            : "First-run setup finished; add a BIOS image in Settings to power on.");
        if (Config.LibraryFolders.Count > 0)
        {
            _ = Library.RefreshAsync();
        }

        ShowToast("Welcome to XGrid");
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
        _frameTimer.Stop();
        _toastTimer.Stop();
        _core.Dispose();
        Library.Dispose();
        foreach (XInputGamepadReader? reader in _gamepads)
        {
            reader?.Dispose();
        }
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

    /// <summary>
    /// Appends a line to the activity page and routes it through Serilog so the
    /// rolling file, the debugger and warnings or errors in the bug-report
    /// pipeline all see the same event.
    /// </summary>
    /// <param name="level">The severity shown in the activity page.</param>
    /// <param name="message">The message text.</param>
    public void AddLog(LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Warning:
                Log.Warning("{Message}", message);
                break;
            case LogLevel.Error:
                Log.Error("{Message}", message);
                break;
            case LogLevel.Debug:
                Log.Debug("{Message}", message);
                break;
            default:
                Log.Information("{Message}", message);
                break;
        }

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

    /// <summary>
    /// Reads what the emulator build reports about itself. The renderer options
    /// and the labels follow this, so the shell never offers Vulkan for a core
    /// that was built without it.
    /// </summary>
    private void RefreshCoreCapabilities()
    {
        _coreCapabilities = CoreCapabilities.Read(XemuPaths.FindCore(Config.XemuCorePath));
        string requested = Config.Graphics.Renderer;
        string effective = XemuLaunchPlan.ResolveRenderer(requested, _coreCapabilities);
        if (!string.Equals(requested, effective, StringComparison.Ordinal))
        {
            AddLog(
                LogLevel.Warning,
                requested + " is not compiled into this emulator build; " + effective + " is used.");
        }

        OnPropertyChanged(nameof(RendererLabel));
        OnPropertyChanged(nameof(DisplayCaption));
    }

    /// <summary>Saves the supplied settings, applying them immediately.</summary>
    public void SaveSettings(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Config = settings;
        RefreshCoreCapabilities();
        _gamepadOptions = InputMapping.Create(Config.Input);
        ConfigureGamepads();
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

    private void RememberRecentGame(GameEntry game)
    {
        RecentGame.Record(Config.RecentGames, game.Path, game.Title, DateTime.UtcNow);
        PersistRecentGames();
    }

    /// <summary>Removes one game from the recently played list and persists it.</summary>
    /// <param name="path">Image path to forget.</param>
    public void ForgetRecentGame(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Config.RecentGames.RemoveAll(
            entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        PersistRecentGames();
    }

    /// <summary>Removes every recently played entry and persists the change.</summary>
    public void ClearRecentGames()
    {
        Config.RecentGames.Clear();
        PersistRecentGames();
    }

    private void PersistRecentGames()
    {
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the recently played list: " + exception.Message);
        }

        Library.RefreshRecentGames();
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

    /// <summary>
    /// Polls the gamepad assigned to each Xbox port and publishes all four pads.
    /// Player 1 also gets the keyboard overlay.
    /// </summary>
    /// <param name="active">True while the shell window is focused.</param>
    public void PollGamepads(bool active)
    {
        if (!IsRunning)
        {
            return;
        }

        for (int port = 0; port < _gamepads.Length; port++)
        {
            HostPad pad = HostPad.Neutral;
            XInputGamepadReader? reader = _gamepads[port];
            if (active && reader is not null && reader.TryRead(out HostPad read, _gamepadOptions))
            {
                pad = read;
                if (!_gamepadLogged[port])
                {
                    _gamepadLogged[port] = true;
                    AddLog(
                        LogLevel.Info,
                        $"XInput slot {reader.AssignedSlot + 1} drives player {port + 1}.");
                }
            }
            else
            {
                _gamepadLogged[port] = false;
            }

            _gamepadPads[port] = pad;
            PublishPad(port);
        }
    }

    /// <summary>Stores the pressed keyboard keys and republishes player 1.</summary>
    /// <param name="pressed">Currently pressed shell keys.</param>
    public void SetKeyboardKeys(IEnumerable<Key> pressed)
    {
        _pressedKeys = [.. pressed];
        if (IsRunning)
        {
            PublishPad(0);
        }
    }

    private void PublishPad(int port)
    {
        HostPad pad = port == 0
            ? InputMapping.MergeKeyboard(_gamepadPads[0], Config.Input, _pressedKeys)
            : _gamepadPads[port];
        ForwardCorePad(port, pad);
    }

    /// <summary>Creates one reader per assigned slot; unassigned ports stay neutral.</summary>
    private void ConfigureGamepads()
    {
        for (int port = 0; port < _gamepads.Length; port++)
        {
            _gamepads[port]?.Dispose();
            _gamepads[port] = null;
            _gamepadPads[port] = HostPad.Neutral;
            _gamepadLogged[port] = false;
        }

        int[] slots = Config.Input.PlayerGamepads;
        for (int port = 0; port < _gamepads.Length && port < slots.Length; port++)
        {
            if (slots[port] is >= 0 and < XInputGamepadReader.MaxUsers)
            {
                _gamepads[port] = new XInputGamepadReader(slots[port]);
            }
        }
    }

    private static HostPad[] CreateNeutralPads()
    {
        var pads = new HostPad[XemuInputSink.PadCount];
        Array.Fill(pads, HostPad.Neutral);
        return pads;
    }

    /// <summary>Maps the shell pad onto xemu's XID mask and publishes it to the core.</summary>
    private void ForwardCorePad(int port, HostPad pad)
    {
        if (!IsRunning)
        {
            return;
        }

        ushort buttons = pad.Buttons;
        ReadOnlySpan<byte> analog = pad.Analog;
        ReadOnlySpan<short> sticks = pad.Sticks;
        if (analog.Length < 8 || sticks.Length < 4)
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
        _core.SetPad(port, mask, sticks[0], sticks[1], sticks[2], sticks[3], lt, rt);
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
            ? Config.Clone()
            : Config.WithTitleOverride(game.Path, game.TitleId == 0 ? null : game.TitleId);
        // Command-line overrides are session-only and win over per-title values.
        _launchOptions.ApplyTo(effective);
        string? problem = XemuLaunchPlan.DescribeProblem(effective, game?.Path, workingDirectory);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        _sessionFilter = effective.Filter;
        OnPropertyChanged(nameof(SmoothFrame));

        XemuLaunchSettings launch = XemuLaunchPlan.Create(
            effective,
            game?.Path,
            _coreDisplayWidth,
            _coreDisplayHeight,
            workingDirectory);
        FrontendTrace.Write($"sink='{launch.DisplaySinkName}' setting={Config.DisplaySink}");
        _core.Start(launch);
        _saveStates = new SaveStateRunner(_core.SaveStates);
        _saveStatesNeedRefresh = _saveStates.IsSupported;
        OnPropertyChanged(nameof(HasSaveStates));
        _playback = new PlaybackController(_core.Playback);
        IsPaused = false;
        IsFastForward = false;
        OnPropertyChanged(nameof(HasPlayback));
        TogglePauseCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
        _discs = new DiscChangeRunner(_core.Discs);
        OnPropertyChanged(nameof(HasDiscChange));
        OnPropertyChanged(nameof(OpenDiscHint));
        OnPropertyChanged(nameof(EjectDiscHint));
        EjectDiscCommand.NotifyCanExecuteChanged();

        if (!string.Equals(launch.Renderer, effective.Graphics.Renderer, StringComparison.Ordinal))
        {
            AddLog(
                LogLevel.Warning,
                effective.Graphics.Renderer + " is not compiled into this emulator build; starting with "
                + launch.Renderer + ".");
        }

        StatusText = "Running";
        StatusDetail = "Emulator - " + (game?.Title ?? "Xbox dashboard");
        AddLog(
            LogLevel.Info,
            $"Emulator - {launch.Renderer} - {launch.RamMb} MB - "
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

    /// <summary>Public command: pauses a running console or resumes a paused one (P).</summary>
    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private void TogglePause()
    {
        PlaybackController? playback = _playback;
        if (playback is null)
        {
            ShowToast("Pausing needs the in-process emulator core.");
            return;
        }

        if (!playback.Toggle(out string problem))
        {
            AddLog(LogLevel.Warning, "Pause refused: " + problem);
            ShowToast(problem);
            return;
        }

        // Pausing while Space is held must return the core to real time; the
        // key handler releases the flag separately.
        if (playback.IsPaused && IsFastForward)
        {
            if (playback.TrySetSpeed(1, out string speedProblem))
            {
                IsFastForward = false;
            }
            else
            {
                AddLog(LogLevel.Warning, "Could not leave fast-forward: " + speedProblem);
            }
        }

        IsPaused = playback.IsPaused;
        StatusText = IsPaused ? "Paused" : "Running";
        StatusDetail = (CurrentGame?.Title ?? "Xbox dashboard") + (IsPaused ? " - paused" : string.Empty);
        AddLog(LogLevel.Info, IsPaused ? "Emulator paused." : "Emulator resumed.");
    }

    private bool CanTogglePause()
    {
        return IsRunning && _playback?.IsSupported == true;
    }

    /// <summary>Toggles 4x fast-forward on or off (Space).</summary>
    public void ToggleFastForward()
    {
        SetFastForward(!IsFastForward);
    }

    /// <summary>
    /// Turns 4x fast-forward on or off. The status bar shows the active pace so
    /// the state is never silent.
    /// </summary>
    /// <param name="enabled">True to run at 4x, false to return to real time.</param>
    public void SetFastForward(bool enabled)
    {
        if (enabled == IsFastForward)
        {
            return;
        }

        if (enabled && (!IsRunning || IsPaused))
        {
            return;
        }

        PlaybackController? playback = _playback;
        if (playback is null || !playback.IsSupported)
        {
            if (enabled)
            {
                ShowToast("Fast forward needs the in-process emulator core.");
            }

            return;
        }

        int multiplier = enabled ? FastForwardMultiplier : 1;
        if (!playback.TrySetSpeed(multiplier, out string problem))
        {
            if (enabled)
            {
                AddLog(LogLevel.Warning, "Fast forward refused: " + problem);
                ShowToast(problem);
            }

            return;
        }

        IsFastForward = enabled;
        StatusText = IsPaused ? "Paused" : "Running";
        StatusDetail = (CurrentGame?.Title ?? "Xbox dashboard")
            + (IsPaused ? " - paused" : enabled ? $" - {FastForwardMultiplier}x fast-forward" : string.Empty);
        AddLog(LogLevel.Info, enabled ? $"Fast forward {FastForwardMultiplier}x." : "Fast forward off.");
    }

    /// <summary>Public command: runs one frame while paused (F10).</summary>
    [RelayCommand(CanExecute = nameof(CanStepFrame))]
    private void StepFrame()
    {
        PlaybackController? playback = _playback;
        if (playback is null)
        {
            ShowToast("Pausing needs the in-process emulator core.");
            return;
        }

        if (!playback.TryStepFrame(out string problem))
        {
            ShowToast(problem);
        }
    }

    private bool CanStepFrame()
    {
        return IsRunning && IsPaused && _playback?.IsSupported == true;
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
            Library.RefreshScreenshots();
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

    /// <summary>Public command: opens the folder that holds game cover art.</summary>
    [RelayCommand]
    private void OpenCoversFolder()
    {
        OpenFolder(CoverArtIndex.DefaultDirectory(), "covers");
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

    /// <summary>Public command: opens the XGrid donation page in the browser.</summary>
    [RelayCommand]
    private async Task OpenDonationPageAsync()
    {
        try
        {
            Uri uri = DonationVerificationClient.CreateDonationPageUri(Config.DonationEmail);
            await Dialogs.OpenUriAsync(uri);
            DonationStatus = string.IsNullOrWhiteSpace(Config.DonationEmail)
                ? "After donating, click 'I already donated' and enter the email you used."
                : "After donating, click 'I already donated' to check your support.";
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or FormatException)
        {
            AddLog(LogLevel.Warning, "Could not open the donation page: " + exception.Message);
            ShowToast("Could not open the donation page.");
        }
    }

    /// <summary>
    /// Public command: asks for the donation email and verifies whether the
    /// service has a completed donation for it.
    /// </summary>
    [RelayCommand]
    private async Task VerifyDonationAsync()
    {
        Window? window = _window;
        if (window is null)
        {
            ShowToast("The main window is not ready yet.");
            return;
        }

        string? email;
        try
        {
            email = await DonationEmailWindow
                .ShowAsync(window, Config.DonationEmail)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            AddLog(LogLevel.Warning, "Could not show the donation dialog: " + exception.Message);
            ShowToast("Could not open the donation dialog.");
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        Config.DonationEmail = email.Trim();
        Config.HasDonated = false;
        SaveDonationSettings();
        await RefreshDonationStatusAsync(showMessages: true).ConfigureAwait(true);
    }

    private async Task RefreshDonationStatusAsync(bool showMessages)
    {
        if (IsDonationCheckInProgress || Config.HasDonated)
        {
            return;
        }

        string email = Config.DonationEmail;
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        IsDonationCheckInProgress = true;
        if (showMessages)
        {
            DonationStatus = "Checking donation status...";
        }

        try
        {
            DonationVerificationResult result = await DonationVerificationClient
                .VerifyAsync(email)
                .ConfigureAwait(true);
            switch (result.Status)
            {
                case DonationVerificationStatus.Donated:
                    Config.HasDonated = true;
                    Config.DonationLastCheckedUtc = DateTime.UtcNow;
                    SaveDonationSettings();
                    DonationStatus = "Thank you for supporting XGrid!";
                    OnPropertyChanged(nameof(ShowDonationBanner));
                    AddLog(LogLevel.Info, "Donation confirmed for the saved email.");
                    ShowToast("Thank you for supporting XGrid!");
                    break;

                case DonationVerificationStatus.NotDonated:
                    Config.DonationLastCheckedUtc = DateTime.UtcNow;
                    SaveDonationSettings();
                    if (showMessages)
                    {
                        DonationStatus =
                            "No donation was found for that email yet. If you just donated, try again in a moment.";
                    }

                    break;

                case DonationVerificationStatus.Unavailable:
                    AddLog(
                        LogLevel.Warning,
                        "Donation verification is unavailable: " + result.Reason + ".");
                    if (showMessages)
                    {
                        DonationStatus = DescribeDonationFailure(result.Reason);
                    }

                    break;

                default:
                    if (showMessages)
                    {
                        DonationStatus = "Could not check donation status. Please try again later.";
                    }

                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AddLog(LogLevel.Warning, "Unexpected donation verification failure: " + exception.Message);
            if (showMessages)
            {
                DonationStatus = "Could not check donation status. Please try again later.";
            }
        }
        finally
        {
            IsDonationCheckInProgress = false;
        }
    }

    internal static string DescribeDonationFailure(DonationVerificationUnavailableReason reason)
    {
        if (reason == DonationVerificationUnavailableReason.ApplicationNotRegistered)
        {
            return "Donation checks are not available for XGrid yet. Every feature stays free; please try again later.";
        }

        if (reason == DonationVerificationUnavailableReason.AuthenticationRejected)
        {
            return "The donation service rejected this copy of XGrid's credentials. Please report this so it can be fixed.";
        }

        if (reason == DonationVerificationUnavailableReason.ConnectionFailed)
        {
            return "Could not reach the donation service. Check your internet connection and try again.";
        }

        if (reason == DonationVerificationUnavailableReason.InvalidResponse)
        {
            return "The donation service returned an unexpected response. Please try again later.";
        }

        return "The donation service is temporarily unavailable. Please try again later.";
    }

    private void SaveDonationSettings()
    {
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the donation status: " + exception.Message);
        }
    }

    /// <summary>
    /// Starts the session the command line asked for: the requested disc, or
    /// the dashboard when only <c>--autostart</c> was given.
    /// </summary>
    public void StartFromCommandLine()
    {
        if (_launchOptions.DiscPath is { Length: > 0 } path)
        {
            GameEntry? entry = GameEntry.FromFile(path);
            if (entry is null)
            {
                AddLog(LogLevel.Error, "The disc image could not be read: " + path);
                ShowToast("The disc image could not be read.");
                return;
            }

            StartGame(entry);
            return;
        }

        TogglePowerCommand.Execute(null);
    }

    /// <summary>Gets a value indicating whether the session has a usable BIOS image.</summary>
    private bool IsBiosReadyForSession => _launchOptions.BiosPath is { Length: > 0 } path
        ? File.Exists(path)
        : Config.IsBiosReady;

    /// <summary>Public command: starts a specific disc entry (or the configured disc).</summary>
    [RelayCommand]
    private void StartGame(GameEntry? game)
    {
        if (game is null && _launchOptions.DiscPath is { Length: > 0 } requested)
        {
            game = GameEntry.FromFile(requested);
            if (game is null)
            {
                AddLog(LogLevel.Error, "The disc image could not be read: " + requested);
                ShowToast("The disc image could not be read.");
                return;
            }
        }

        if (game is not null && !game.IsLaunchable)
        {
            ShowToast("Loose XBE files must be wrapped in an ISO or CHD image.");
            return;
        }

        if (_core.IsRunning)
        {
            // Clicking another game in the Library is a disc swap, not a launch.
            if (game is not null && _discs?.IsSupported == true)
            {
                InsertDisc(game.Path);
                return;
            }

            ShowToast("Power off the console before changing discs.");
            return;
        }

        if (!IsBiosReadyForSession)
        {
            AddLog(LogLevel.Warning, "Power-on refused: no BIOS image is configured.");
            ShowToast("Choose a BIOS image first.");
            NavigateTo(PageKind.Settings);
            return;
        }

        try
        {
            CurrentGame = game;
            StartCore(game);
            if (game is not null)
            {
                RememberRecentGame(game);
            }

            OnPropertyChanged(nameof(CurrentGameTitle));
            OnPropertyChanged(nameof(CurrentGameFormat));
            OnPropertyChanged(nameof(HomeDetail));
            IsRunning = true;
            StatusText = "Running";
            StatusDetail = game?.Title ?? "Xbox dashboard";
            ResetFrame();
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
            ResetFrame();
            _playback?.Reset();
            _playback = null;
            IsPaused = false;
            _sessionFilter = null;
            _discs = null;
            OnPropertyChanged(nameof(HasPlayback));
            OnPropertyChanged(nameof(HasDiscChange));
            OnPropertyChanged(nameof(OpenDiscHint));
            OnPropertyChanged(nameof(EjectDiscHint));
            OnPropertyChanged(nameof(SmoothFrame));
            EjectDiscCommand.NotifyCanExecuteChanged();
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

    /// <summary>Public command: saves the machine to the selected slot (F6).</summary>
    [RelayCommand]
    private void QuickSave()
    {
        SaveStateToSlot(SelectedSaveSlot);
    }

    /// <summary>Public command: restores the selected slot (F7).</summary>
    [RelayCommand]
    private void QuickLoad()
    {
        LoadStateFromSlot(SelectedSaveSlot);
    }

    /// <summary>Saves the machine to one quick slot and selects it for F6/F7.</summary>
    /// <param name="slot">Slot number, 1 to 4.</param>
    public void SaveStateToSlot(int slot)
    {
        BeginSaveState(slot, static (runner, number, out problem) =>
            runner.TrySave(number, out problem));
    }

    /// <summary>Restores one quick slot and selects it for F6/F7.</summary>
    /// <param name="slot">Slot number, 1 to 4.</param>
    public void LoadStateFromSlot(int slot)
    {
        BeginSaveState(slot, static (runner, number, out problem) =>
            runner.TryLoad(number, out problem));
    }

    /// <summary>Deletes the state in one quick slot.</summary>
    /// <param name="slot">Slot number, 1 to 4.</param>
    public void DeleteStateInSlot(int slot)
    {
        BeginSaveState(slot, static (runner, number, out problem) =>
            runner.TryDelete(number, out problem));
    }

    private void BeginSaveState(
        int slot,
        SlotRequest request)
    {
        if (!IsRunning)
        {
            ShowToast("Power on the console before using save states.");
            return;
        }

        SaveStateRunner? runner = _saveStates;
        if (runner is null)
        {
            ShowToast("Save states need the in-process emulator core.");
            return;
        }

        if (!SaveStateRunner.IsValidSlot(slot))
        {
            ShowToast("Save states use slots 1 to " + SaveStateRunner.SlotCount.ToString(CultureInfo.InvariantCulture) + ".");
            return;
        }

        if (!request(runner, slot, out string problem))
        {
            AddLog(LogLevel.Warning, "Save state refused: " + problem);
            ShowToast(problem);
            return;
        }

        SelectedSaveSlot = slot;
        AddLog(LogLevel.Info, runner.Operation switch
        {
            XemuSaveStateOperation.Save => $"Saving state to slot {slot}...",
            XemuSaveStateOperation.Load => $"Loading state from slot {slot}...",
            _ => $"Deleting the state in slot {slot}...",
        });
    }

    private delegate bool SlotRequest(SaveStateRunner runner, int slot, out string problem);

    /// <summary>Public command: picks a disc image and launches it.</summary>
    [RelayCommand]
    private async Task OpenDiscAsync()
    {
        string? path = await Dialogs.PickFileAsync(
            IsRunning ? "Choose a disc image to insert" : "Choose a disc image",
            FileDialogService.DiscTypes);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!GameLibraryService.IsGameCandidate(path))
        {
            ShowToast("Choose an ISO/XISO, CHD, ZAR, or XBE file.");
            return;
        }

        if (IsRunning)
        {
            InsertDisc(path);
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

    /// <summary>Public command: ejects the inserted disc while the console runs.</summary>
    [RelayCommand(CanExecute = nameof(CanEjectDisc))]
    private void EjectDisc()
    {
        DiscChangeRunner? discs = _discs;
        if (discs is null || !discs.IsSupported)
        {
            ShowToast("Changing discs needs the in-process emulator core.");
            return;
        }

        if (!discs.TryEject(out string problem))
        {
            AddLog(LogLevel.Warning, "Eject refused: " + problem);
            ShowToast(problem);
            return;
        }

        StatusText = "Ejecting";
        StatusDetail = "Opening the disc tray";
        AddLog(LogLevel.Info, "Ejecting the disc...");
    }

    private bool CanEjectDisc()
    {
        return IsRunning && _discs?.IsSupported == true;
    }

    /// <summary>Inserts a disc into the running console through the core ABI.</summary>
    /// <param name="path">ISO/XISO, CHD or ZArchive image.</param>
    private void InsertDisc(string path)
    {
        DiscChangeRunner? discs = _discs;
        if (discs is null || !discs.IsSupported)
        {
            ShowToast("Changing discs needs the in-process emulator core.");
            return;
        }

        if (!discs.TryLoad(path, out string problem))
        {
            AddLog(LogLevel.Warning, "Disc change refused: " + problem);
            ShowToast(problem);
            return;
        }

        StatusText = "Changing disc";
        StatusDetail = "Inserting " + Path.GetFileName(path);
        AddLog(LogLevel.Info, "Changing disc to " + Path.GetFileName(path) + "...");
        NavigateTo(PageKind.Home);
    }

    /// <summary>
    /// Reports the finished disc request, if one arrived. The core changes the
    /// block backend on its own loop, so the shell only polls the status.
    /// </summary>
    private void PollDiscChange()
    {
        DiscChangeRunner? discs = _discs;
        if (discs is null)
        {
            return;
        }

        DiscChangeResult? result = discs.Poll();
        if (result is null)
        {
            return;
        }

        AddLog(result.Succeeded ? LogLevel.Info : LogLevel.Error, result.Message);
        ShowToast(result.Message);

        string detail = result.Succeeded
            ? result.Operation == DiscChangeOperation.Load
                ? (CurrentGame?.Title ?? "Xbox dashboard") + " - disc changed"
                : "No disc inserted"
            : CurrentGame?.Title ?? "Xbox dashboard";
        if (result.Succeeded && result.Operation == DiscChangeOperation.Load)
        {
            CurrentGame = GameEntry.FromFile(result.Path);
            detail = (CurrentGame?.Title ?? "Xbox dashboard") + " - disc changed";
        }
        else if (result.Succeeded)
        {
            CurrentGame = null;
        }

        StatusText = IsPaused ? "Paused" : "Running";
        StatusDetail = IsPaused ? detail + " - paused" : detail;
    }

    private bool CanStop()
    {
        return IsRunning;
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsRunningChanged(bool value)
    {
        if (!value)
        {
            IsFastForward = false;
        }

        _fpsMeter.Reset();
        FpsText = "-- FPS";
        FrameTimeText = string.Empty;
        OnPropertyChanged(nameof(IsFpsOverlayVisible));

        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(FramePlaceholder));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        OnPropertyChanged(nameof(RunStateLabel));
        OnPropertyChanged(nameof(OpenDiscHint));
        StopSessionCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
        EjectDiscCommand.NotifyCanExecuteChanged();
        if (value)
        {
            _frameTimer.Start();
        }
        else
        {
            _frameTimer.Stop();
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(RunStateLabel));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseHint));
        OnPropertyChanged(nameof(StepFrameHint));
        TogglePauseCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsFastForwardChanged(bool value)
    {
        OnPropertyChanged(nameof(RunStateLabel));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDonationBanner));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnHasFrameChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayCaption));
        OnPropertyChanged(nameof(HasDisplayCaption));
    }

    /// <summary>
    /// Forgets the presented frame, including its size, so the next power-on
    /// never shows the previous session's caption.
    /// </summary>
    private void ResetFrame()
    {
        lock (_coreFrameLock)
        {
            // A frame left pending by the previous session must not be the first
            // picture of the next one.
            _coreFramePending = false;
            _coreFrameBuffer = null;
            _coreFrameSpare = null;
        }

        FrameBitmap = null;
        HasFrame = false;
        if (_frameWidth != 0 || _frameHeight != 0)
        {
            _frameWidth = 0;
            _frameHeight = 0;
            OnPropertyChanged(nameof(DisplayCaption));
            OnPropertyChanged(nameof(HasDisplayCaption));
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSelectedSaveSlotChanged(int value)
    {
        OnPropertyChanged(nameof(QuickSaveHeader));
        OnPropertyChanged(nameof(QuickLoadHeader));
        OnPropertyChanged(nameof(SaveStateHint));
        OnPropertyChanged(nameof(LoadStateHint));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
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

        // Captures can appear while the console page is open; refresh when the
        // Library is shown. Unchanged files reuse their decoded thumbnails.
        if (value?.Kind == PageKind.Library)
        {
            Library.RefreshScreenshots();
        }

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
        Interlocked.Increment(ref _framesArrived);
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

            // Hand the UI the buffer the sink just filled and give the sink the
            // previous one to write next. A single buffer let the next frame
            // overwrite the pixels while PresentFrame was still reading them.
            byte[] present = _coreFrameBuffer;
            _coreFrameBuffer = _coreFrameSpare;
            _coreFrameSpare = present;
            packet = new FramePacket((uint)_coreFrameWidth, (uint)_coreFrameHeight, present);
            return true;
        }
    }

    private void OnCoreLog(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddLog(LogLevel.Info, message);
            if (message.Contains("Starting a separate core process", StringComparison.Ordinal))
            {
                // In-process QEMU state is single-use; the fallback is normal
                // after the first session, but the user should know why save
                // states and pause are no longer available.
                ShowToast("In-process core already ran; this game uses a separate process.");
            }
        });
    }

    private void OnCoreStopped(object? sender, XemuCoreStoppedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            ResetFrame();
            _saveStates = null;
            _saveStatesNeedRefresh = false;
            _playback?.Reset();
            _playback = null;
            IsPaused = false;
            _sessionFilter = null;
            _discs = null;
            OnPropertyChanged(nameof(HasSaveStates));
            OnPropertyChanged(nameof(HasPlayback));
            OnPropertyChanged(nameof(HasDiscChange));
            OnPropertyChanged(nameof(OpenDiscHint));
            OnPropertyChanged(nameof(EjectDiscHint));
            OnPropertyChanged(nameof(SmoothFrame));
            EjectDiscCommand.NotifyCanExecuteChanged();
            StatusText = e.Faulted ? "Faulted" : "Off";
            StatusDetail = e.Message;
            AddLog(e.Faulted ? LogLevel.Error : LogLevel.Info, e.Message);
        });
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        // The core can publish far faster than the screen refreshes (the sink
        // poll runs every 8 ms), so a present is paced by wall clock rather than
        // by the timer's own cadence.
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!FramePacing.ShouldPresent(now, _lastPresentTimestamp, System.Diagnostics.Stopwatch.Frequency))
        {
            return;
        }

        if (!TryTakeCoreFrame(out FramePacket? coreFrame))
        {
            return;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        PresentFrame(coreFrame!);
        Interlocked.Add(
            ref _presentTicks,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        _lastPresentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _framesPresented);
        _fpsMeter.AddFrame();
        HasFrame = true;
        FramePresented?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFps(long now)
    {
        if (!IsRunning)
        {
            _fpsMeter.Reset();
            return;
        }

        if (!_fpsMeter.TryUpdate(now, out double fps, out double frameTimeMs))
        {
            return;
        }

        FpsText = fps.ToString("F0", CultureInfo.InvariantCulture) + " FPS";
        FrameTimeText = frameTimeMs > 0
            ? frameTimeMs.ToString("F1", CultureInfo.InvariantCulture) + " ms"
            : string.Empty;
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        PollSaveStates();
        PollDiscChange();

        long now = Environment.TickCount64;
        UpdateFps(now);
        if (_frameWindowStart == 0)
        {
            _frameWindowStart = now;
        }
        else if (now - _frameWindowStart >= FrameReportPeriodMs)
        {
            double seconds = (now - _frameWindowStart) / 1000.0;
            int arrived = Interlocked.Exchange(ref _framesArrived, 0);
            int presented = Interlocked.Exchange(ref _framesPresented, 0);
            long ticks = Interlocked.Exchange(ref _presentTicks, 0);
            FrontendTrace.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"frames core={arrived / seconds:F1}/s shell={presented / seconds:F1}/s present={(presented == 0 ? 0 : (double)ticks / presented):F2}ms"));
            _frameWindowStart = now;
        }

        // The native host is only shown while the core paints into it. When the
        // frame sink takes over it must not sit above the frame image.
        bool sinkActive = _core.IsFrameSinkActive;
        if (sinkActive != _lastFrameSinkActive)
        {
            _lastFrameSinkActive = sinkActive;
            OnPropertyChanged(nameof(ShowCoreDisplay));
            OnPropertyChanged(nameof(IsFpsOverlayVisible));
        }

        // The core owns the picture, its pacing and its input; the shell only
        // mirrors liveness so a crash is never silent.
        if (IsRunning && !_core.IsRunning)
        {
            IsRunning = false;
            ResetFrame();
            return;
        }

        if (IsRunning)
        {
            HasFrame = _core.IsEmbedded || HasFrame;
        }
        else if (HasFrame)
        {
            ResetFrame();
        }
    }

    /// <summary>
    /// Reports the finished save-state request, if one arrived. The core runs
    /// the work on its own loop, so the shell only reads the status.
    /// </summary>
    private void PollSaveStates()
    {
        SaveStateRunner? runner = _saveStates;
        if (runner is null)
        {
            return;
        }

        // The core sets its main loop up on its own thread a moment after it
        // starts, so the first list request is retried until it is accepted.
        if (_saveStatesNeedRefresh)
        {
            _saveStatesNeedRefresh = !runner.TryRefresh(out _);
        }

        SaveStateResult? result = runner.Poll();
        if (result is null)
        {
            return;
        }

        XemuSaveStateOperation operation = runner.Operation;
        AddLog(result.Succeeded ? LogLevel.Info : LogLevel.Error, result.Message);
        FrontendTrace.Write(
            $"save state {operation} slot {runner.Slot}: {(result.Succeeded ? "ok" : "failed")} - {result.Message}");

        // A refresh is bookkeeping; the player only asked for the slot change.
        if (operation != XemuSaveStateOperation.Refresh)
        {
            ShowToast(result.Message);
        }

        if (result.Succeeded
            && operation is XemuSaveStateOperation.Save or XemuSaveStateOperation.Delete)
        {
            _ = runner.TryRefresh(out _);
        }
    }

    private void PresentFrame(FramePacket packet)
    {
        FrameFilters.Apply(ActiveFilter, packet.Bgra, (int)packet.Width, (int)packet.Height);
        int width = (int)packet.Width;
        int height = (int)packet.Height;
        int rowBytes = width * 4;
        if (_frameWidth != width || _frameHeight != height)
        {
            _frameWidth = width;
            _frameHeight = height;
            OnPropertyChanged(nameof(DisplayCaption));
            OnPropertyChanged(nameof(HasDisplayCaption));
        }
        // Opaque presentation: one word per pixel rather than four byte writes,
        // because this runs at the screen's refresh rate on the UI thread.
        Span<uint> pixels = MemoryMarshal.Cast<byte, uint>(packet.Bgra.AsSpan(0, width * height * 4));
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] |= 0xFF000000u;
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
        OnPropertyChanged(nameof(ShowDonationBanner));
        OnPropertyChanged(nameof(ShowFpsOverlay));
        OnPropertyChanged(nameof(IsFpsOverlayVisible));
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
