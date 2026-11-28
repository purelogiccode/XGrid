using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
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
    private readonly DispatcherTimer _toastTimer;

    /// <summary>The pace Space holds the console at (four times real time).</summary>
    public const int FastForwardMultiplier = 4;

    /// <summary>
    /// How many refresh ticks to wait for the core to open its HDD and report
    /// the session snapshot. The in-process machine is still initialising when
    /// the first refresh is accepted.
    /// </summary>
    private const int MaxSessionLoadAttempts = 50;

    /// <summary>
    /// Wait before restoring a session snapshot. The guest must have initialized
    /// the APU/DSP before the VM state can be loaded; loading during the first
    /// seconds of the retail boot trips a DSP state assertion.
    /// </summary>
    private const int SessionResumeDelayMs = 12_000;

    private GamepadMappingOptions _gamepadOptions;
    private readonly XInputGamepadReader?[] _gamepads = new XInputGamepadReader?[XemuInputSink.PadCount];
    private readonly HostPad[] _gamepadPads = CreateNeutralPads();
    private readonly bool[] _gamepadLogged = new bool[XemuInputSink.PadCount];
    private IReadOnlyList<Key> _pressedKeys = [];
    private int _coreDisplayWidth = 640;
    private int _coreDisplayHeight = 480;
    private bool _shuttingDown;
    private bool _closeWhenStopped;
    private bool _stopRequested;
    private bool _returnToLibraryWhenStopped;
    private bool _sessionSavePending;
    private bool _loadSessionWhenReady;
    private bool _sessionResumeFound;
    private long _sessionResumeNotBefore;
    private int _sessionLoadAttempts;
    private bool _windowReady;
    private SaveStateRunner? _saveStates;
    private PlaybackController? _playback;
    private DiscChangeRunner? _discs;
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
    private string _statusText = "Powered off";

    [ObservableProperty]
    private string _statusDetail = "No console powered on";

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
        SelectedNavigationItem = NavigationItems[1];

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

    /// <summary>Gets a value indicating whether a BIOS image is available for this session.</summary>
    public bool IsBiosReady => IsBiosReadyForSession;

    /// <summary>Gets a value indicating whether the native core display should be shown.</summary>
    public bool ShowCoreDisplay => IsRunning;

    /// <summary>Gets a value indicating whether the powered-off console panel should be shown.</summary>
    public bool ShowPoweredOffPanel => SessionPolicy.ShouldShowPoweredOffScreen(Config, IsRunning);

    /// <summary>
    /// Gets a value indicating whether the console page should explain that the
    /// core window is outside XGrid on this host.
    /// </summary>
    public bool ShowExternalDisplayHint => IsRunning && !IsEmbeddedDisplaySupported;

    /// <summary>
    /// Gets a value indicating whether the native core window can be embedded in
    /// the console page on this host.
    /// </summary>
    public bool IsEmbeddedDisplaySupported { get; } = XemuWindowEmbedder.IsSupported;

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

    /// <summary>Gets the page padding; fullscreen removes every shell margin.</summary>
    public Thickness PageMargin => IsFullscreen ? default : new Thickness(16, 14, 16, 14);

    /// <summary>Gets a value indicating whether the log dock is visible (never in fullscreen).</summary>
    public bool IsLogDockVisible => ShowLogDock && !IsFullscreen;

    /// <summary>Gets the picture border thickness; fullscreen is edge to edge.</summary>
    public Thickness PictureBorderThickness => IsFullscreen ? default : new Thickness(1);

    /// <summary>Gets the picture corner radius; fullscreen has square corners.</summary>
    public CornerRadius PictureCornerRadius => IsFullscreen ? default : new CornerRadius(4);

    /// <summary>Gets the console page row spacing; fullscreen removes it.</summary>
    public double HomeRowSpacing => IsFullscreen ? 0 : 10;

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
            if (value && IsRunning && !_core.TrySetFpsOverlay(true))
            {
                ShowToast("The FPS overlay takes effect on the next launch for a separate core process.");
            }
            else if (IsRunning && !value && !_core.TrySetFpsOverlay(false))
            {
                ShowToast("The FPS overlay takes effect on the next launch for a separate core process.");
            }
        }
    }

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

    private string RendererName => XemuLaunchPlan.ResolveRenderer(
        _launchOptions.Renderer ?? Config.Graphics.Renderer, _coreCapabilities) switch
    {
        GraphicsSettings.RendererVulkan => "Vulkan",
        GraphicsSettings.RendererDirect3D11 => "Direct3D 11",
        GraphicsSettings.RendererNull => "Null",
        _ => "OpenGL",
    };

    /// <summary>Gets the short run state shown in the status bar.</summary>
    public string RunStateLabel => _sessionSavePending
        ? "Saving state"
        : _stopRequested
            ? "Stopping"
            : IsRunning
                ? IsPaused
                    ? "Paused"
                    : IsFastForward
                        ? $"Running {FastForwardMultiplier}x"
                        : "Running"
                : "Ready";

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

        if (window.IsVisible)
        {
            _ = OnWindowReadyAsync(window);
        }
        else
        {
            window.Opened += OnMainWindowOpened;
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

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Opened -= OnMainWindowOpened;
            await OnWindowReadyAsync(window);
        }
    }

    /// <summary>
    /// Runs the once-per-window startup work: the first-run wizard, then the
    /// saved power-on choice. A command-line autostart is posted by the app and
    /// wins over <see cref="EmulatorSettings.PowerOnAtStartup"/>.
    /// </summary>
    private async Task OnWindowReadyAsync(Window window)
    {
        if (_windowReady)
        {
            return;
        }

        _windowReady = true;
        try
        {
            if (_firstRunPending)
            {
                await ShowFirstRunAsync(window);
            }

            if (SessionPolicy.ShouldPowerOnAtStartup(
                    Config,
                    commandLineAutostart: App.Autostart,
                    firstRunPending: _firstRunPending,
                    isRunning: IsRunning))
            {
                Dispatcher.UIThread.Post(StartFromCommandLine, DispatcherPriority.Background);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "The startup session work failed");
            AddLog(LogLevel.Error, "The startup session work failed: " + exception.Message);
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
        _closeWhenStopped = false;
        _stopRequested = false;
        _returnToLibraryWhenStopped = false;
        _sessionSavePending = false;
        _loadSessionWhenReady = false;
        _sessionResumeFound = false;
        _sessionLoadAttempts = 0;
        _uiTimer.Stop();
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
        string requested = _launchOptions.Renderer ?? Config.Graphics.Renderer;
        string effective = XemuLaunchPlan.ResolveRenderer(requested, _coreCapabilities);
        if (!string.Equals(requested, effective, StringComparison.Ordinal))
        {
            AddLog(
                LogLevel.Warning,
                requested + " is not compiled into this emulator build; " + effective + " is used.");
        }

        OnPropertyChanged(nameof(RendererLabel));
    }

    /// <summary>Saves the supplied settings, applying them immediately.</summary>
    public void SaveSettings(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string previousFilter = Config.Filter;
        bool previousOverlay = Config.ShowFpsOverlay;
        int previousFrameRateLimit = Config.Graphics.FrameRateLimit;
        bool previousFrameSkip = Config.Graphics.FrameSkip;
        settings.Normalize();
        Config = settings;
        RefreshCoreCapabilities();
        _gamepadOptions = InputMapping.Create(Config.Input);
        ConfigureGamepads();
        if (IsRunning && !string.Equals(previousFilter, Config.Filter, StringComparison.Ordinal)
            && !_core.TrySetPresentationFilter(Config.Filter))
        {
            AddLog(LogLevel.Info, "The presentation filter applies on the next launch for a separate core process.");
        }

        if (IsRunning && previousOverlay != Config.ShowFpsOverlay
            && !_core.TrySetFpsOverlay(Config.ShowFpsOverlay))
        {
            AddLog(LogLevel.Info, "The FPS overlay applies on the next launch for a separate core process.");
        }

        if (IsRunning && previousFrameRateLimit != Config.Graphics.FrameRateLimit
            && !_core.TrySetFrameRateLimit(Config.Graphics.FrameRateLimit))
        {
            AddLog(LogLevel.Info, "The frame-rate limit applies on the next launch for a separate core process.");
        }

        if (IsRunning && previousFrameSkip != Config.Graphics.FrameSkip
            && !_core.TrySetFrameSkip(Config.Graphics.FrameSkip))
        {
            AddLog(LogLevel.Info, "Frame skipping applies on the next launch for a separate core process.");
        }
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
    }

    /// <summary>Persists favorites, play time and the library tile size.</summary>
    public void SaveLibraryState()
    {
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the library state: " + exception.Message);
        }
    }

    /// <summary>Persists the library's tile or row presentation.</summary>
    /// <param name="gridView">True for thumbnail tiles, false for the detailed rows.</param>
    public void SetLibraryView(bool gridView)
    {
        if (Config.LibraryGridView == gridView)
        {
            return;
        }

        Config.LibraryGridView = gridView;
        try
        {
            _settingsStore.Save(Config);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddLog(LogLevel.Warning, "Could not save the library view: " + exception.Message);
        }
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

        // The control publishes before its first layout pass (1x1); keep the
        // last meaningful size until the real one arrives, so the core is not
        // launched into a 64x64 window.
        _coreDisplayWidth = width >= 64 ? width : _coreDisplayWidth;
        _coreDisplayHeight = height >= 64 ? height : _coreDisplayHeight;
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
        // The library scan is metadata-only; resolve this one disc now so a
        // title-id override still applies to the launch.
        game?.ResolveTitleId();

        string workingDirectory = Path.Combine(SettingsStore.ResolveDirectory(), "xemu");
        EmulatorSettings effective = game is null
            ? Config.Clone()
            : Config.WithTitleOverride(game.Path, game.TitleId == 0 ? null : game.TitleId);
        // Command-line overrides are session-only and win over per-title values.
        _launchOptions.ApplyTo(effective);
        if (string.Equals(effective.Audio.Backend, AudioBackends.DirectSound, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(effective.Audio.Device)
            && !AudioDeviceService.Enumerate().Any(
                device => string.Equals(device.Id, effective.Audio.Device, StringComparison.OrdinalIgnoreCase)))
        {
            AddLog(
                LogLevel.Warning,
                "The selected DirectSound device is not connected; using the system default.");
            effective.Audio.Device = string.Empty;
        }

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
        FrontendTrace.Write($"filter={launch.PresentationFilter} overlay={launch.FpsOverlay}");
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
        ToggleFastForwardCommand.NotifyCanExecuteChanged();
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
    [RelayCommand(CanExecute = nameof(CanToggleFastForward))]
    public void ToggleFastForward()
    {
        SetFastForward(!IsFastForward);
    }

    private bool CanToggleFastForward()
    {
        // Keep the control live even when the hosting mode cannot change the
        // pace: a press then explains why instead of the button silently doing
        // nothing (the old in-process-only state was easy to miss).
        return IsRunning && !IsPaused;
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

        int multiplier = enabled ? FastForwardMultiplier : 1;
        PlaybackController? playback = _playback;
        bool accepted = playback is not null
                        && playback.IsSupported
                        && playback.TrySetSpeed(multiplier, out _);
        if (!accepted)
        {
            // A separate core process exposes no pause/step ABI, but it does
            // accept the speed message on its own window. This keeps Space and
            // the 4x button working in both hosting modes.
            accepted = _core.TrySetSpeed(multiplier);
        }

        if (!accepted)
        {
            if (enabled)
            {
                const string problem = "Fast forward needs the in-process emulator core, or the core window must be visible.";
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

        // A running in-process core can capture its own picture, which is the
        // only path that includes the embedded GL child window.
        if (IsRunning)
        {
            _ = Directory.CreateDirectory(ScreenshotNaming.Directory(SettingsStore.ResolveDirectory()));
            if (_core.TryRequestScreenshot())
            {
                AddLog(LogLevel.Info, "Screenshot requested from the core.");
                ShowToast("Saving screenshot...");
                _ = RefreshScreenshotsSoonAsync();
                return;
            }
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

            // A native child window (the embedded core) is invisible to the
            // visual-tree renderer, so read the composed desktop instead while
            // a console is running.
            IntPtr handle = WindowCapture.IsSupported
                ? _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
                : IntPtr.Zero;
            using WriteableBitmap? captured = IsRunning && handle != IntPtr.Zero
                ? WindowCapture.CaptureClient(handle, size)
                : null;
            if (captured is not null)
            {
                captured.Save(path, PngBitmapEncoderOptions.Default);
            }
            else
            {
                using var target = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
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

    private async Task RefreshScreenshotsSoonAsync()
    {
        await Task.Delay(500).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            ShowToast("Screenshot saved to the screenshots folder.");
        });
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
        string directory = string.IsNullOrWhiteSpace(Config.Covers.Directory)
            ? CoverDefaults.LocalDirectory
            : Config.Covers.Directory;
        OpenFolder(directory, "covers");
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

        if (_stopRequested)
        {
            ShowToast("The console is still stopping.");
            return;
        }

        _returnToLibraryWhenStopped = false;

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
                Library.RecordLaunch(game);
            }

            OnPropertyChanged(nameof(CurrentGameTitle));
            OnPropertyChanged(nameof(CurrentGameFormat));
            OnPropertyChanged(nameof(HomeDetail));
            IsRunning = true;
            StatusText = "Running";
            StatusDetail = game?.Title ?? "Xbox dashboard";
            AddLog(LogLevel.Info, game is null
                ? "Powering on the console."
                : "Launching " + game.Title + ".");

            NavigateTo(PageKind.Home);

            if (SessionPolicy.ShouldResumeAfterStart(
                    Config,
                    hasGame: game is not null,
                    saveStatesSupported: _saveStates?.IsSupported == true))
            {
                _loadSessionWhenReady = true;
                _sessionResumeFound = false;
                _sessionLoadAttempts = 0;
                _sessionResumeNotBefore = Environment.TickCount64 + SessionResumeDelayMs;
                AddLog(LogLevel.Info, "Will resume the saved session when the core is ready.");
            }
            else if (Config.ResumeStateOnStart && game is null && _saveStates?.IsSupported != true)
            {
                AddLog(LogLevel.Info, "Resume needs the in-process emulator core; starting a fresh dashboard.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or XemuConfigurationException)
        {
            IsRunning = false;
            _playback?.Reset();
            _playback = null;
            IsPaused = false;
            _discs = null;
            OnPropertyChanged(nameof(HasPlayback));
            OnPropertyChanged(nameof(HasDiscChange));
            OnPropertyChanged(nameof(OpenDiscHint));
            OnPropertyChanged(nameof(EjectDiscHint));
            EjectDiscCommand.NotifyCanExecuteChanged();
            StatusText = "Idle";
            StatusDetail = "Power-on failed";
            AddLog(LogLevel.Error, "Power-on failed: " + exception.Message);
            ShowToast("Power-on failed: " + exception.Message);
        }
    }

    /// <summary>Stops the console and shows the library at the next safe point.</summary>
    public void StopSessionAndShowLibrary()
    {
        if (!IsRunning)
        {
            NavigateTo(PageKind.Library);
            return;
        }

        _returnToLibraryWhenStopped = true;
        StopSessionCommand.Execute(null);
        NavigateTo(PageKind.Library);
    }

    /// <summary>Public command: stops the running console.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopSession()
    {
        if (!IsRunning || _stopRequested)
        {
            return;
        }

        _stopRequested = true;
        OnPropertyChanged(nameof(RunStateLabel));

        if (SessionPolicy.ShouldSaveBeforeStop(
                Config,
                saveStatesSupported: _saveStates?.IsSupported == true,
                isRunning: IsRunning))
        {
            SaveStateRunner? runner = _saveStates;
            string problem = "The save-state service is not available.";
            if (runner is not null && runner.TrySaveSession(out problem))
            {
                _sessionSavePending = true;
                StatusText = "Saving state";
                StatusDetail = "Saving the console state before power off...";
                OnPropertyChanged(nameof(RunStateLabel));
                AddLog(LogLevel.Info, "Saving the session state before power off...");
                return;
            }

            AddLog(LogLevel.Warning, "Could not save the session state: " + problem);
        }
        else if (Config.SaveStateOnExit && _saveStates?.IsSupported != true)
        {
            AddLog(
                LogLevel.Info,
                "Save-on-exit needs the in-process emulator core; powering off without a snapshot.");
        }

        AddLog(LogLevel.Info, "Stopping the console.");
        _core.Stop();
        StatusText = "Stopping";
        OnPropertyChanged(nameof(RunStateLabel));
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
        CancelPendingSessionResume();
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
        CancelPendingSessionResume();
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
        CancelPendingSessionResume();
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
            if (CurrentGame is not null)
            {
                Library.RecordLaunch(CurrentGame);
            }

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

        OnPropertyChanged(nameof(PowerButtonText));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        OnPropertyChanged(nameof(ShowPoweredOffPanel));
        OnPropertyChanged(nameof(ShowExternalDisplayHint));
        OnPropertyChanged(nameof(RunStateLabel));
        OnPropertyChanged(nameof(OpenDiscHint));
        StopSessionCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
        ToggleFastForwardCommand.NotifyCanExecuteChanged();
        EjectDiscCommand.NotifyCanExecuteChanged();
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
        ToggleFastForwardCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(PageMargin));
        OnPropertyChanged(nameof(IsLogDockVisible));
        OnPropertyChanged(nameof(PictureBorderThickness));
        OnPropertyChanged(nameof(PictureCornerRadius));
        OnPropertyChanged(nameof(HomeRowSpacing));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnShowLogDockChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLogDockVisible));
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
            Library.CompletePlaySession();
            IsRunning = false;
            _stopRequested = false;
            _sessionSavePending = false;
            _loadSessionWhenReady = false;
            _sessionResumeFound = false;
            _sessionLoadAttempts = 0;
            _saveStates = null;
            _saveStatesNeedRefresh = false;
            _playback?.Reset();
            _playback = null;
            IsPaused = false;
            _discs = null;
            OnPropertyChanged(nameof(HasSaveStates));
            OnPropertyChanged(nameof(HasPlayback));
            OnPropertyChanged(nameof(HasDiscChange));
            OnPropertyChanged(nameof(ShowPoweredOffPanel));
            OnPropertyChanged(nameof(ShowExternalDisplayHint));
            OnPropertyChanged(nameof(OpenDiscHint));
            OnPropertyChanged(nameof(EjectDiscHint));
            OnPropertyChanged(nameof(RunStateLabel));
            EjectDiscCommand.NotifyCanExecuteChanged();
            StatusText = e.Faulted ? "Faulted" : "Powered off";
            StatusDetail = e.Message;
            AddLog(e.Faulted ? LogLevel.Error : LogLevel.Info, e.Message);

            if (_returnToLibraryWhenStopped)
            {
                _returnToLibraryWhenStopped = false;
                NavigateTo(PageKind.Library);
            }
            else if (_closeWhenStopped)
            {
                _closeWhenStopped = false;
                // Re-enter Closing with the core fully stopped, so SDL can
                // destroy its window without waiting on this thread.
                _window?.Close();
            }
            else if (SessionPolicy.ShouldCloseAfterStop(Config, e.Faulted))
            {
                _closeWhenStopped = true;
                _window?.Close();
            }
        });
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        PollSaveStates();
        ResumeSessionWhenReady();
        PollDiscChange();

        // The core owns the picture, its pacing, filtering and overlay; the
        // shell only mirrors liveness so a crash is never silent.
        if (IsRunning && !_core.IsRunning)
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Restores the session snapshot once the guest has finished the early boot
    /// and the save-state channel is idle. Loading during the first seconds of
    /// the retail boot trips a DSP state assertion in the core.
    /// </summary>
    private void ResumeSessionWhenReady()
    {
        if (!_sessionResumeFound
            || Environment.TickCount64 < _sessionResumeNotBefore
            || _saveStates is not { IsSupported: true } runner
            || runner.IsBusy)
        {
            return;
        }

        _sessionResumeFound = false;
        if (runner.TryLoadSession(out string problem))
        {
            StatusDetail = "Resuming the saved session...";
            AddLog(LogLevel.Info, "Resuming the saved session...");
        }
        else
        {
            AddLog(LogLevel.Warning, "Could not resume the saved session: " + problem);
        }
    }

    /// <summary>Cancels an automatic resume that has not run yet after a manual machine action.</summary>
    private void CancelPendingSessionResume()
    {
        _loadSessionWhenReady = false;
        _sessionResumeFound = false;
        _sessionLoadAttempts = 0;
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
        bool session = runner.IsSession;
        AddLog(result.Succeeded ? LogLevel.Info : LogLevel.Error, result.Message);
        FrontendTrace.Write(
            $"save state {operation} {(session ? runner.Name : "slot " + runner.Slot.ToString(CultureInfo.InvariantCulture))}: "
            + $"{(result.Succeeded ? "ok" : "failed")} - {result.Message}");

        // A refresh is bookkeeping; the player only asked for the slot change.
        if (operation != XemuSaveStateOperation.Refresh)
        {
            ShowToast(result.Message);
        }

        if (session
            && operation == XemuSaveStateOperation.Save
            && _sessionSavePending)
        {
            _sessionSavePending = false;
            StatusText = "Stopping";
            StatusDetail = result.Succeeded
                ? "Session state saved. Powering off..."
                : "Powering off without a saved session.";
            OnPropertyChanged(nameof(RunStateLabel));
            AddLog(LogLevel.Info, "Stopping the console.");
            _core.Stop();
            return;
        }

        if (operation == XemuSaveStateOperation.Refresh && _loadSessionWhenReady)
        {
            if (runner.Names.Contains(SaveStateRunner.SessionName, StringComparer.Ordinal))
            {
                _loadSessionWhenReady = false;
                _sessionLoadAttempts = 0;
                _sessionResumeFound = true;
                AddLog(LogLevel.Info, "Saved session found; it will resume after the console boots.");
            }
            else if (_sessionLoadAttempts++ < MaxSessionLoadAttempts)
            {
                // The machine may still be opening the HDD; ask again next tick.
                _saveStatesNeedRefresh = true;
                return;
            }
            else
            {
                _loadSessionWhenReady = false;
                _sessionLoadAttempts = 0;
                AddLog(LogLevel.Info, "No saved session to resume.");
            }
        }

        if (result.Succeeded
            && operation is XemuSaveStateOperation.Save or XemuSaveStateOperation.Delete)
        {
            _ = runner.TryRefresh(out _);
        }
    }

    private void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(IsBiosReady));
        OnPropertyChanged(nameof(NeedsSetup));
        OnPropertyChanged(nameof(SettingsPath));
        OnPropertyChanged(nameof(HomeDetail));
        OnPropertyChanged(nameof(RendererLabel));
        OnPropertyChanged(nameof(ShowCoreDisplay));
        OnPropertyChanged(nameof(ShowPoweredOffPanel));
        OnPropertyChanged(nameof(ShowExternalDisplayHint));
        OnPropertyChanged(nameof(ShowDonationBanner));
        OnPropertyChanged(nameof(ShowFpsOverlay));
        StopSessionCommand.NotifyCanExecuteChanged();
    }

    private void OnToastTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // The core destroys its own GL window on its thread. That thread sends
        // window messages to the shell's UI thread, so blocking here to wait
        // for the core would deadlock until the stop timeout. Let the window
        // close once the core has reported that it stopped.
        if (_core.IsRunning)
        {
            if (!_closeWhenStopped)
            {
                e.Cancel = true;
                _closeWhenStopped = true;
                StopSession();
                return;
            }

            // A second close request while the core is still stopping is the
            // user's escape hatch: Dispose applies its bounded timeout and the
            // window closes regardless.
            Shutdown();
            return;
        }

        Shutdown();
    }
}
