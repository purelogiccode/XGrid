using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Input;
using XGrid.XboxFs;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>One selectable option in a settings drop-down: a persisted key and a label.</summary>
/// <remarks>Creates an option.</remarks>
/// <param name="key">Value persisted in the settings file.</param>
/// <param name="label">Text shown in the drop-down.</param>
public sealed class KeyedOption(string key, string label)
{
    /// <summary>Gets the persisted key.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the display label.</summary>
    public string Label { get; } = label;
}

/// <summary>One per-title override row in the settings list.</summary>
public sealed class TitleOverrideItem : ObservableObject
{
    private static readonly KeyedOption[] RamChoices =
    [
        new(string.Empty, "inherit"),
        new("64", "64 MB"),
        new("128", "128 MB"),
    ];

    private KeyedOption _selectedRam;
    private KeyedOption _selectedRenderer;
    private KeyedOption _selectedFiltering;
    private KeyedOption _selectedFilter;

    /// <summary>Creates a row for a launched image path or an XBE title id.</summary>
    /// <param name="key">Override dictionary key (image path or <c>title:XXXXXXXX</c>).</param>
    /// <param name="value">Persisted override values.</param>
    /// <param name="remove">Removal callback.</param>
    /// <param name="rendererKeys">Renderers the selected core reports.</param>
    /// <param name="titleName">Certificate title name when the key is a title id.</param>
    public TitleOverrideItem(
        string key,
        TitleOverride value,
        Action<TitleOverrideItem> remove,
        IReadOnlyList<string> rendererKeys,
        string? titleName = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(rendererKeys);
        Path = key;
        TitleName = titleName ?? string.Empty;
        _selectedRam = RamChoices.FirstOrDefault(
            option => option.Key.Length > 0
                && int.TryParse(option.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ram)
                && ram == value.RamMb)
            ?? RamChoices[0];
        RendererOptions =
        [
            new KeyedOption(string.Empty, "inherit"),
            .. rendererKeys.Select(renderer => new KeyedOption(renderer, ShortRendererLabel(renderer))),
        ];
        FilteringOptions =
        [
            new KeyedOption(string.Empty, "inherit"),
            new KeyedOption(GraphicsSettings.FilterLinear, "Linear"),
            new KeyedOption(GraphicsSettings.FilterNearest, "Nearest"),
        ];
        FilterOptions =
        [
            new KeyedOption(string.Empty, "inherit"),
            .. FrameFilters.All.Select(filter => new KeyedOption(filter.Key, filter.Label)),
        ];
        _selectedRenderer = Pick(RendererOptions, value.Renderer);
        _selectedFiltering = Pick(FilteringOptions, value.Filtering);
        _selectedFilter = Pick(FilterOptions, value.Filter);
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>Gets the override key (an image path, or <c>title:XXXXXXXX</c> for a title-id key).</summary>
    public string Path { get; }

    /// <summary>Gets the certificate title name when this row is keyed by title id (may be empty).</summary>
    private string TitleName { get; }

    /// <summary>Gets the label shown in the list: the title name, the title id, or the file name.</summary>
    public string DisplayName
    {
        get
        {
            if (EmulatorSettings.TryParseTitleIdKey(Path, out uint titleId))
            {
                string id = "Title " + titleId.ToString("X8", CultureInfo.InvariantCulture);
                return TitleName.Length == 0 ? id : TitleName + " (" + id + ")";
            }

            return System.IO.Path.GetFileName(Path);
        }
    }

    /// <summary>Gets the muted second line: the image path, or the title-key rule.</summary>
    public string PathText => EmulatorSettings.IsTitleIdKey(Path) ? "applies wherever this title id is launched" : Path;

    /// <summary>Gets the command that removes this override.</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>Gets the guest-memory choices this row can pick from ("inherit", "64", "128").</summary>
    public IReadOnlyList<KeyedOption> RamOptions { get; } = RamChoices;

    /// <summary>Gets the renderer choices this row can pick from ("inherit" plus the core's renderers).</summary>
    public IReadOnlyList<KeyedOption> RendererOptions { get; }

    /// <summary>Gets the core texture-filter choices this row can pick from ("inherit", linear, nearest).</summary>
    public IReadOnlyList<KeyedOption> FilteringOptions { get; }

    /// <summary>Gets the presentation-filter choices this row can pick from ("inherit" plus the shell filters).</summary>
    public IReadOnlyList<KeyedOption> FilterOptions { get; }

    /// <summary>Gets or sets the guest memory this title uses (the "inherit" row keeps the global one).</summary>
    public KeyedOption SelectedRam
    {
        get => _selectedRam;
        set => SetProperty(ref _selectedRam, value);
    }

    /// <summary>Gets or sets the renderer this title uses (the "inherit" row keeps the global one).</summary>
    public KeyedOption SelectedRenderer
    {
        get => _selectedRenderer;
        set => SetProperty(ref _selectedRenderer, value);
    }

    /// <summary>Gets or sets the core texture filter this title uses.</summary>
    public KeyedOption SelectedFiltering
    {
        get => _selectedFiltering;
        set => SetProperty(ref _selectedFiltering, value);
    }

    /// <summary>Gets or sets the presentation filter this title uses.</summary>
    public KeyedOption SelectedFilter
    {
        get => _selectedFilter;
        set => SetProperty(ref _selectedFilter, value);
    }

    /// <summary>Projects the row back into the persisted model.</summary>
    public TitleOverride ToOverride()
    {
        return new TitleOverride
        {
            RamMb = int.TryParse(_selectedRam.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ram)
                ? ram
                : null,
            Renderer = OptionalKey(_selectedRenderer),
            Filtering = OptionalKey(_selectedFiltering),
            Filter = OptionalKey(_selectedFilter),
        };
    }

    private static KeyedOption Pick(IReadOnlyList<KeyedOption> options, string? key)
    {
        return options.FirstOrDefault(
            option => option.Key.Length > 0
                && string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private static string? OptionalKey(KeyedOption option)
    {
        return option.Key.Length == 0 ? null : option.Key;
    }

    private static string ShortRendererLabel(string key)
    {
        return key switch
        {
            GraphicsSettings.RendererOpenGl => "OpenGL",
            GraphicsSettings.RendererVulkan => "Vulkan",
            GraphicsSettings.RendererNull => "Null",
            _ => key,
        };
    }
}

/// <summary>One keyboard binding row: an action and its comma-separated keys.</summary>
public sealed class KeyboardBindingItem : ObservableObject
{
    private string _keys;
    private bool _isCapturing;

    /// <summary>Creates a binding row.</summary>
    /// <param name="action">Pad action.</param>
    /// <param name="keys">Stored key list.</param>
    /// <param name="capture">Callback that starts key capture for this row.</param>
    public KeyboardBindingItem(PadAction action, string keys, Action<KeyboardBindingItem> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Action = action;
        ActionName = PadActions.DisplayName(action);
        _keys = keys;
        CaptureCommand = new RelayCommand(() => capture(this));
        ClearCommand = new RelayCommand(() =>
        {
            Keys = string.Empty;
            IsCapturing = false;
        });
    }

    /// <summary>Gets the pad action.</summary>
    public PadAction Action { get; }

    /// <summary>Gets the display label for the action.</summary>
    public string ActionName { get; }

    /// <summary>Gets or sets the stored key list.</summary>
    public string Keys
    {
        get => _keys;
        set
        {
            if (SetProperty(ref _keys, value))
            {
                OnPropertyChanged(nameof(KeysDisplay));
            }
        }
    }

    /// <summary>Gets the display text for the binding.</summary>
    public string KeysDisplay => _isCapturing ? "Press a key..." : KeyboardKeys.Describe(_keys);

    /// <summary>Gets or sets a value indicating whether the next key press binds this row.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(KeysDisplay));
            }
        }
    }

    /// <summary>Gets the command that starts key capture.</summary>
    public IRelayCommand CaptureCommand { get; }

    /// <summary>Gets the command that clears the binding.</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>Binds one captured key.</summary>
    /// <param name="key">Captured key.</param>
    public void Capture(Key key)
    {
        Keys = KeyboardKeys.Format(key);
        IsCapturing = false;
    }
}

/// <summary>One gamepad binding row: an action and its physical source.</summary>
public sealed class GamepadBindingItem : ObservableObject
{
    private KeyedOption _selectedSource;

    /// <summary>Creates a gamepad binding row.</summary>
    /// <param name="action">Pad action.</param>
    /// <param name="source">Bound source.</param>
    /// <param name="sources">Selectable sources.</param>
    public GamepadBindingItem(PadAction action, GamepadSource source, IReadOnlyList<KeyedOption> sources)
    {
        Action = action;
        ActionName = PadActions.DisplayName(action);
        Sources = sources;
        _selectedSource = sources.FirstOrDefault(option => string.Equals(option.Key, source.ToString(), StringComparison.Ordinal))
            ?? sources[0];
    }

    /// <summary>Gets the pad action.</summary>
    public PadAction Action { get; }

    /// <summary>Gets the display label for the action.</summary>
    public string ActionName { get; }

    /// <summary>Gets the selectable sources.</summary>
    public IReadOnlyList<KeyedOption> Sources { get; }

    /// <summary>Gets or sets the selected source.</summary>
    public KeyedOption SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }
}

/// <summary>A library folder row with a remove command.</summary>
public sealed class LibraryFolderItem
{
    /// <summary>Creates a folder row.</summary>
    public LibraryFolderItem(string path, Action<string> remove)
    {
        Path = path;
        RemoveCommand = new RelayCommand(() => remove(Path));
    }

    /// <summary>Gets the folder path.</summary>
    public string Path { get; }

    /// <summary>Gets the remove command.</summary>
    public IRelayCommand RemoveCommand { get; }
}

/// <summary>Settings page: firmware, storage, xemu core options, appearance, and library.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    [ObservableProperty]
    private string _biosPath = string.Empty;

    [ObservableProperty]
    private string _mcpxRomPath = string.Empty;

    [ObservableProperty]
    private string _eepromPath = string.Empty;

    [ObservableProperty]
    private string _hddPath = string.Empty;

    [ObservableProperty]
    private string _dvdPath = string.Empty;

    [ObservableProperty]
    private int _ramMb = 64;

    [ObservableProperty]
    private bool _displaySink;

    [ObservableProperty]
    private KeyedOption? _selectedCoreHosting;

    [ObservableProperty]
    private KeyedOption? _selectedFilter;

    [ObservableProperty]
    private string _xemuCorePath = string.Empty;

    [ObservableProperty]
    private AccentOption? _selectedAccent;

    [ObservableProperty]
    private double _leftDeadZone = GamepadMappingOptions.DefaultLeftDeadZonePercent;

    [ObservableProperty]
    private double _rightDeadZone = GamepadMappingOptions.DefaultRightDeadZonePercent;

    [ObservableProperty]
    private double _triggerThreshold = GamepadMappingOptions.DefaultTriggerThresholdPercent;

    [ObservableProperty]
    private bool _invertLeftY;

    [ObservableProperty]
    private bool _invertRightY;

    [ObservableProperty]
    private bool _swapSticks;

    [ObservableProperty]
    private KeyedOption? _selectedRenderer;

    private IReadOnlyList<KeyedOption> _rendererOptions = [];
    private CoreCapabilities? _coreCapabilities;

    [ObservableProperty]
    private int _surfaceScale = 1;

    [ObservableProperty]
    private KeyedOption? _selectedFiltering;

    [ObservableProperty]
    private KeyedOption? _selectedStartupSize;

    [ObservableProperty]
    private bool _graphicsVSync;

    [ObservableProperty]
    private bool _fullscreenOnStartup;

    [ObservableProperty]
    private bool _fullscreenExclusive;

    [ObservableProperty]
    private KeyedOption? _selectedAspectRatio;

    [ObservableProperty]
    private KeyedOption? _selectedFit;

    [ObservableProperty]
    private KeyedOption? _selectedAvPack;

    [ObservableProperty]
    private bool _useDsp = true;

    [ObservableProperty]
    private bool _useDspJit;

    [ObservableProperty]
    private bool _hrtf = true;

    [ObservableProperty]
    private double _volumeLimit = 1.0;

    [ObservableProperty]
    private int _voiceWorkers;

    [ObservableProperty]
    private KeyedOption? _selectedAudioBackend;

    /// <summary>Creates the settings page with a draft loaded from the shell configuration.</summary>
    public SettingsViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        FilterOptions = [.. FrameFilters.All.Select(item => new KeyedOption(item.Key, item.Label))];
        CoreHostingOptions =
        [
            new KeyedOption(EmulatorSettings.CoreHostingInProcess, "In-process (recommended)"),
            new KeyedOption(EmulatorSettings.CoreHostingProcess, "Separate process"),
        ];
        GamepadSourceOptions =
        [
            .. GamepadSources.All.Select(source => new KeyedOption(source.ToString(), GamepadSources.DisplayName(source))),
        ];
        RendererOptions = [.. RendererChoices.Keys(null).Select(key => new KeyedOption(key, RendererChoices.Label(key)))];
        FilteringOptions =
        [
            new KeyedOption(GraphicsSettings.FilterLinear, "Linear"),
            new KeyedOption(GraphicsSettings.FilterNearest, "Nearest"),
        ];
        StartupSizeOptions =
        [
            .. GraphicsSettings.StartupSizes.Select(size => new KeyedOption(size, size)),
        ];
        AspectRatioOptions =
        [
            new KeyedOption("auto", "Auto"),
            new KeyedOption("native", "Native"),
            new KeyedOption("4x3", "4:3"),
            new KeyedOption("16x9", "16:9"),
        ];
        FitOptions =
        [
            new KeyedOption("scale", "Scale"),
            new KeyedOption("center", "Center"),
            new KeyedOption("stretch", "Stretch"),
        ];
        AvPackOptions =
        [
            .. GraphicsSettings.AvPacks.Select(pack => new KeyedOption(pack, pack)),
        ];
        AudioBackendOptions = OperatingSystem.IsWindows()
            ?
            [
                new KeyedOption(AudioSettings.BackendAuto, "Auto"),
                new KeyedOption(AudioSettings.BackendDirectSound, "DirectSound"),
                new KeyedOption(AudioSettings.BackendNone, "None"),
                new KeyedOption(AudioSettings.BackendWav, "WAV capture"),
            ]
            :
            [
                new KeyedOption(AudioSettings.BackendAuto, "Auto"),
                new KeyedOption(AudioSettings.BackendNone, "None"),
                new KeyedOption(AudioSettings.BackendWav, "WAV capture"),
            ];
        foreach (AccentPalette palette in AccentPalette.All)
        {
            AccentOptions.Add(new AccentOption(palette));
        }

        LoadFrom(shell.Config);
        TitleOverrides.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTitleOverrides));
    }

    /// <summary>Gets the guest RAM choices.</summary>
    public IReadOnlyList<int> RamOptions { get; } = [64, 128];

    /// <summary>Gets the selectable core hosting modes.</summary>
    public IReadOnlyList<KeyedOption> CoreHostingOptions { get; }

    /// <summary>Gets the selectable presentation filters.</summary>
    public IReadOnlyList<KeyedOption> FilterOptions { get; }

    /// <summary>Gets the selectable gamepad sources.</summary>
    public IReadOnlyList<KeyedOption> GamepadSourceOptions { get; }

    /// <summary>Gets the selectable core renderers.</summary>
    public IReadOnlyList<KeyedOption> RendererOptions
    {
        get => _rendererOptions;
        private set => SetProperty(ref _rendererOptions, value);
    }

    /// <summary>Gets the hint under the renderer row, describing the core's build.</summary>
    public string RendererHint => RendererChoices.Hint(_coreCapabilities);

    /// <summary>Gets the selectable internal surface scales.</summary>
    public IReadOnlyList<int> SurfaceScaleOptions { get; } = [1, 2, 3, 4, 5, 6, 8];

    /// <summary>Gets the selectable core texture filters.</summary>
    public IReadOnlyList<KeyedOption> FilteringOptions { get; }

    /// <summary>Gets the selectable core window sizes.</summary>
    public IReadOnlyList<KeyedOption> StartupSizeOptions { get; }

    /// <summary>Gets the selectable aspect-ratio modes.</summary>
    public IReadOnlyList<KeyedOption> AspectRatioOptions { get; }

    /// <summary>Gets the selectable fit modes.</summary>
    public IReadOnlyList<KeyedOption> FitOptions { get; }

    /// <summary>Gets the selectable AV packs.</summary>
    public IReadOnlyList<KeyedOption> AvPackOptions { get; }

    /// <summary>Gets the selectable host audio backends.</summary>
    public IReadOnlyList<KeyedOption> AudioBackendOptions { get; }

    /// <summary>Gets the selectable voice-worker counts (0 = auto).</summary>
    public IReadOnlyList<int> VoiceWorkerOptions { get; } = [0, 1, 2, 4, 8];

    /// <summary>Gets the keyboard binding rows.</summary>
    public ObservableCollection<KeyboardBindingItem> KeyboardBindings { get; } = [];

    /// <summary>Gets the gamepad binding rows.</summary>
    public ObservableCollection<GamepadBindingItem> GamepadBindings { get; } = [];

    /// <summary>Gets the row currently waiting for a key, or null.</summary>
    public KeyboardBindingItem? CapturingBinding => KeyboardBindings.FirstOrDefault(item => item.IsCapturing);

    /// <summary>Gets a value indicating whether a key capture is in progress.</summary>
    public bool IsCapturingKey => CapturingBinding is not null;

    /// <summary>Gets the per-title overrides being edited.</summary>
    public ObservableCollection<TitleOverrideItem> TitleOverrides { get; } = [];

    /// <summary>Gets a value indicating whether any per-title override exists.</summary>
    public bool HasTitleOverrides => TitleOverrides.Count > 0;

    /// <summary>Gets the accent swatches.</summary>
    public ObservableCollection<AccentOption> AccentOptions { get; } = [];

    /// <summary>Gets the library folders being edited.</summary>
    public ObservableCollection<LibraryFolderItem> LibraryFolders { get; } = [];

    /// <summary>Gets the BIOS status label.</summary>
    public string BiosStatusText =>
        string.IsNullOrWhiteSpace(BiosPath)
            ? "No BIOS image selected - power-on is disabled."
            : File.Exists(BiosPath) ? "BIOS image found." : "BIOS file not found at this path.";

    /// <summary>Gets a value indicating whether the BIOS path is usable.</summary>
    public bool BiosPathValid => !string.IsNullOrWhiteSpace(BiosPath) && File.Exists(BiosPath);

    /// <summary>Gets a value indicating whether the BIOS warning should be shown.</summary>
    public bool ShowBiosWarning => !BiosPathValid;

    /// <summary>Gets the settings storage folder.</summary>
    public string SettingsFolder => Path.GetDirectoryName(Shell.SettingsPath) ?? string.Empty;

    /// <summary>Gets a value indicating whether any library folders are configured.</summary>
    public bool HasLibraryFolders => LibraryFolders.Count > 0;

    /// <summary>Loads a settings snapshot into the edit surface.</summary>
    public void LoadFrom(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        BiosPath = settings.BiosPath;
        McpxRomPath = settings.McpxRomPath;
        EepromPath = settings.EepromPath;
        HddPath = settings.HddPath;
        DvdPath = settings.DvdPath;
        RamMb = settings.RamMb;
        LoadInput(settings.Input);

        // The core binary decides which renderers can be offered, so capabilities
        // are read before the saved renderer is matched against the list.
        XemuCorePath = settings.XemuCorePath;
        RefreshRenderers(settings.Graphics.Renderer);
        SelectedRenderer = RendererOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.Renderer, StringComparison.Ordinal))
            ?? RendererOptions[0];
        SurfaceScale = settings.Graphics.SurfaceScale;
        SelectedFiltering = FilteringOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.Filtering, StringComparison.Ordinal))
            ?? FilteringOptions[0];
        SelectedStartupSize = StartupSizeOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.StartupSize, StringComparison.Ordinal))
            ?? StartupSizeOptions.FirstOrDefault(option => string.Equals(option.Key, "1280x960", StringComparison.Ordinal))
            ?? StartupSizeOptions[0];
        GraphicsVSync = settings.Graphics.VSync;
        FullscreenOnStartup = settings.Graphics.FullscreenOnStartup;
        FullscreenExclusive = settings.Graphics.FullscreenExclusive;
        SelectedAspectRatio = AspectRatioOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.AspectRatio, StringComparison.Ordinal))
            ?? AspectRatioOptions[0];
        SelectedFit = FitOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.Fit, StringComparison.Ordinal))
            ?? FitOptions[0];
        SelectedAvPack = AvPackOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Graphics.AvPack, StringComparison.Ordinal))
            ?? AvPackOptions[0];
        UseDsp = settings.Audio.UseDsp;
        UseDspJit = settings.Audio.UseDspJit;
        Hrtf = settings.Audio.Hrtf;
        VolumeLimit = settings.Audio.VolumeLimit;
        VoiceWorkers = settings.Audio.VoiceWorkers;
        SelectedAudioBackend = AudioBackendOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Audio.Backend, StringComparison.Ordinal))
            ?? AudioBackendOptions[0];
        DisplaySink = settings.DisplaySink;
        SelectedCoreHosting = CoreHostingOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.CoreHosting, StringComparison.Ordinal))
            ?? CoreHostingOptions[0];
        SelectedFilter = FilterOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Filter, StringComparison.Ordinal))
            ?? FilterOptions[0];
        TitleOverrides.Clear();
        foreach (KeyValuePair<string, TitleOverride> pair in settings.TitleOverrides)
        {
            TitleOverrides.Add(new TitleOverrideItem(
                pair.Key,
                pair.Value,
                RemoveTitleOverride,
                OfferableRenderers()));
        }

        string accentKey = settings.Accent;
        foreach (AccentOption option in AccentOptions)
        {
            option.IsSelected = string.Equals(option.Key, accentKey, StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected)
            {
                SelectedAccent = option;
            }
        }

        LibraryFolders.Clear();
        foreach (string folder in settings.LibraryFolders)
        {
            LibraryFolders.Add(new LibraryFolderItem(folder, RemoveFolder));
        }

        OnPropertyChanged(nameof(HasLibraryFolders));
        OnPropertyChanged(nameof(BiosStatusText));
        OnPropertyChanged(nameof(BiosPathValid));
        OnPropertyChanged(nameof(ShowBiosWarning));
    }

    /// <summary>Public command: saves the draft to disk and applies it.</summary>
    [RelayCommand]
    private void Save()
    {
        var next = new EmulatorSettings
        {
            BiosPath = BiosPath.Trim(),
            McpxRomPath = McpxRomPath.Trim(),
            EepromPath = EepromPath.Trim(),
            HddPath = HddPath.Trim(),
            DvdPath = DvdPath.Trim(),
            RamMb = RamMb,
            Input = BuildInputSettings(),
            Graphics = BuildGraphicsSettings(),
            Audio = BuildAudioSettings(),
            XemuCorePath = XemuCorePath.Trim(),
            DisplaySink = DisplaySink,
            CoreHosting = SelectedCoreHosting?.Key ?? EmulatorSettings.CoreHostingInProcess,
            Filter = SelectedFilter?.Key ?? FrameFilters.Nearest,
            Accent = SelectedAccent?.Key ?? "Emerald",
            LibraryFolders = [.. LibraryFolders.Select(item => item.Path)],
        };
        foreach (TitleOverrideItem item in TitleOverrides)
        {
            next.TitleOverrides[EmulatorSettings.CanonicalTitleKey(item.Path)] = item.ToOverride();
        }

        Shell.SaveSettings(next);
        Shell.Home.NotifySettingsChanged();
    }

    private GraphicsSettings BuildGraphicsSettings()
    {
        var graphics = new GraphicsSettings
        {
            Renderer = SelectedRenderer?.Key ?? GraphicsSettings.RendererOpenGl,
            SurfaceScale = SurfaceScale,
            Filtering = SelectedFiltering?.Key ?? GraphicsSettings.FilterLinear,
            StartupSize = SelectedStartupSize?.Key ?? "1280x960",
            VSync = GraphicsVSync,
            FullscreenOnStartup = FullscreenOnStartup,
            FullscreenExclusive = FullscreenExclusive,
            AspectRatio = SelectedAspectRatio?.Key ?? "auto",
            Fit = SelectedFit?.Key ?? "scale",
            AvPack = SelectedAvPack?.Key ?? "hdtv",
        };
        graphics.Normalize();
        return graphics;
    }

    private AudioSettings BuildAudioSettings()
    {
        var audio = new AudioSettings
        {
            UseDsp = UseDsp,
            UseDspJit = UseDspJit,
            Hrtf = Hrtf,
            VolumeLimit = VolumeLimit,
            VoiceWorkers = VoiceWorkers,
            Backend = SelectedAudioBackend?.Key ?? AudioSettings.BackendAuto,
        };
        audio.Normalize();
        return audio;
    }

    /// <summary>Public command: restores the shipped keyboard and gamepad defaults.</summary>
    [RelayCommand]
    private void ResetInputDefaults()
    {
        LoadInput(new InputSettings());
        Shell.ShowToast("Input defaults restored - save to apply.");
    }

    /// <summary>Loads one input configuration into the edit surface.</summary>
    /// <param name="input">Input settings.</param>
    private void LoadInput(InputSettings input)
    {
        ArgumentNullException.ThrowIfNull(input);
        KeyboardBindings.Clear();
        foreach (PadAction action in PadActions.All)
        {
            string keys = input.Keyboard.TryGetValue(action.ToString(), out string? stored)
                ? stored
                : InputDefaults.DefaultKeyboard(action);
            KeyboardBindings.Add(new KeyboardBindingItem(action, keys, BeginCapture));
        }

        GamepadBindings.Clear();
        foreach (PadAction action in PadActions.All)
        {
            string sourceName = input.Gamepad.TryGetValue(action.ToString(), out string? stored)
                ? stored
                : InputDefaults.DefaultGamepad(action);
            _ = GamepadSources.TryParse(sourceName, out GamepadSource source);
            GamepadBindings.Add(new GamepadBindingItem(action, source, GamepadSourceOptions));
        }

        LeftDeadZone = input.LeftDeadZonePercent;
        RightDeadZone = input.RightDeadZonePercent;
        TriggerThreshold = input.TriggerThresholdPercent;
        InvertLeftY = input.InvertLeftY;
        InvertRightY = input.InvertRightY;
        SwapSticks = input.SwapSticks;
        OnPropertyChanged(nameof(CapturingBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
    }

    /// <summary>Projects the edit surface back into persisted input settings.</summary>
    private InputSettings BuildInputSettings()
    {
        var input = new InputSettings
        {
            Keyboard = KeyboardBindings.ToDictionary(
                item => item.Action.ToString(),
                item => item.Keys,
                StringComparer.OrdinalIgnoreCase),
            Gamepad = GamepadBindings.ToDictionary(
                item => item.Action.ToString(),
                item => item.SelectedSource.Key,
                StringComparer.OrdinalIgnoreCase),
            LeftDeadZonePercent = (int)Math.Round(LeftDeadZone),
            RightDeadZonePercent = (int)Math.Round(RightDeadZone),
            TriggerThresholdPercent = (int)Math.Round(TriggerThreshold),
            InvertLeftY = InvertLeftY,
            InvertRightY = InvertRightY,
            SwapSticks = SwapSticks,
        };
        input.Normalize();
        return input;
    }

    private void BeginCapture(KeyboardBindingItem item)
    {
        foreach (KeyboardBindingItem binding in KeyboardBindings)
        {
            binding.IsCapturing = false;
        }

        item.IsCapturing = true;
        OnPropertyChanged(nameof(CapturingBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
        Shell.ShowToast("Press a key for " + item.ActionName + ". Esc cancels.");
    }

    /// <summary>Captures the next key while a binding row is armed.</summary>
    /// <param name="key">Pressed key.</param>
    /// <returns>True when the key was consumed by the capture.</returns>
    public bool CaptureKey(Key key)
    {
        KeyboardBindingItem? item = CapturingBinding;
        if (item is null)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            item.IsCapturing = false;
        }
        else
        {
            item.Capture(key);
        }

        OnPropertyChanged(nameof(CapturingBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
        return true;
    }

    /// <summary>Public command: adds a per-title override for a disc or XBE image.</summary>
    [RelayCommand]
    private async Task AddTitleOverrideAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync(
            "Choose the disc or XBE this override applies to", FileDialogService.DiscTypes);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string key = EmulatorSettings.NormalizeTitleKey(path);
        string? titleName = null;

        // Key the override by the XBE title id when it can be read, so it
        // follows the title across renamed, moved or re-wrapped images. An
        // unreadable image keeps the path key.
        if (DiscTitleId.TryRead(path, out uint titleId, out string resolvedName))
        {
            key = EmulatorSettings.TitleIdKey(titleId);
            titleName = resolvedName;
        }

        if (TitleOverrides.Any(item => string.Equals(
                EmulatorSettings.CanonicalTitleKey(item.Path), key, StringComparison.OrdinalIgnoreCase)))
        {
            Shell.ShowToast("That title already has an override.");
            return;
        }

        TitleOverrides.Add(new TitleOverrideItem(
            key,
            new TitleOverride { RamMb = RamMb },
            RemoveTitleOverride,
            OfferableRenderers(),
            titleName));
        Shell.ShowToast(titleName is null
            ? "Override added for this image (no XBE title id could be read)."
            : "Override added for title " + key);
    }

    /// <summary>Public command: removes a per-title override row.</summary>
    [RelayCommand]
    private void RemoveTitleOverride(TitleOverrideItem? item)
    {
        if (item is null)
        {
            return;
        }

        TitleOverrides.Remove(item);
    }

    /// <summary>Returns the renderer keys a new or loaded override row can offer.</summary>
    private IReadOnlyList<string> OfferableRenderers()
    {
        return [.. RendererOptions.Select(option => option.Key)];
    }

    /// <summary>Public command: browses for the BIOS image.</summary>
    [RelayCommand]
    private async Task BrowseBiosAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose the Xbox BIOS image", FileDialogService.FirmwareTypes);
        if (path is not null)
        {
            BiosPath = path;
        }
    }

    /// <summary>Public command: browses for the MCPX boot ROM.</summary>
    [RelayCommand]
    private async Task BrowseMcpxAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose the 512-byte MCPX ROM", FileDialogService.FirmwareTypes);
        if (path is not null)
        {
            McpxRomPath = path;
        }
    }

    /// <summary>Public command: browses for the EEPROM image.</summary>
    [RelayCommand]
    private async Task BrowseEepromAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose the 256-byte EEPROM image", FileDialogService.FirmwareTypes);
        if (path is not null)
        {
            EepromPath = path;
        }
    }

    /// <summary>Public command: browses for a hard-disk image.</summary>
    [RelayCommand]
    private async Task BrowseHddAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose a hard-disk image", FileDialogService.DiskTypes);
        if (path is not null)
        {
            HddPath = path;
        }
    }

    /// <summary>Public command: browses for a disc image.</summary>
    [RelayCommand]
    private async Task BrowseDvdAsync()
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose a disc image", FileDialogService.DiscTypes);
        if (path is not null)
        {
            DvdPath = path;
        }
    }

    /// <summary>Public command: adds a library folder to the draft.</summary>
    [RelayCommand]
    private async Task AddLibraryFolderAsync()
    {
        string? folder = await Shell.Dialogs.PickFolderAsync("Choose a folder containing Xbox games");
        if (string.IsNullOrWhiteSpace(folder)
            || LibraryFolders.Any(item => string.Equals(item.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        LibraryFolders.Add(new LibraryFolderItem(folder, RemoveFolder));
        OnPropertyChanged(nameof(HasLibraryFolders));
    }

    /// <summary>Public command: removes a folder from the draft.</summary>
    [RelayCommand]
    private void RemoveLibraryFolder(LibraryFolderItem? folder)
    {
        if (folder is null)
        {
            return;
        }

        RemoveFolder(folder.Path);
    }

    private void RemoveFolder(string folder)
    {
        LibraryFolderItem? item = LibraryFolders.FirstOrDefault(
            candidate => string.Equals(candidate.Path, folder, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            LibraryFolders.Remove(item);
        }

        OnPropertyChanged(nameof(HasLibraryFolders));
    }

    /// <summary>Public command: opens the settings folder in the platform file manager.</summary>
    [RelayCommand]
    private Task OpenSettingsFolderAsync()
    {
        return Shell.Dialogs.OpenFolderAsync(SettingsFolder);
    }

    /// <summary>Public command: clears a configured path.</summary>
    [RelayCommand]
    private void ClearField(string? field)
    {
        switch (field)
        {
            case "bios":
                BiosPath = string.Empty;
                break;
            case "mcpx":
                McpxRomPath = string.Empty;
                break;
            case "eeprom":
                EepromPath = string.Empty;
                break;
            case "hdd":
                HddPath = string.Empty;
                break;
            case "dvd":
                DvdPath = string.Empty;
                break;
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnBiosPathChanged(string value)
    {
        OnPropertyChanged(nameof(BiosStatusText));
        OnPropertyChanged(nameof(BiosPathValid));
        OnPropertyChanged(nameof(ShowBiosWarning));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnXemuCorePathChanged(string value)
    {
        RefreshRenderers();
    }

    /// <summary>
    /// Rebuilds the renderer list from what the selected core reports, keeping
    /// the current choice when it is still offered.
    /// </summary>
    /// <param name="preferredKey">Renderer key to select instead of the current choice.</param>
    private void RefreshRenderers(string? preferredKey = null)
    {
        _coreCapabilities = CoreCapabilities.Read(XemuPaths.FindCore(XemuCorePath));
        string selected = preferredKey ?? SelectedRenderer?.Key ?? CoreCapabilities.RendererOpenGl;
        RendererOptions = [.. RendererChoices.Keys(_coreCapabilities).Select(key => new KeyedOption(key, RendererChoices.Label(key)))];
        SelectedRenderer = RendererOptions.FirstOrDefault(
            option => string.Equals(option.Key, selected, StringComparison.Ordinal))
            ?? RendererOptions[0];
        OnPropertyChanged(nameof(RendererHint));
    }

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (AccentOption option in AccentOptions)
        {
            option.IsSelected = ReferenceEquals(option, value);
        }

        MainWindowViewModel.PreviewAccent(value.Key);
    }
}
