using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            .. XemuPresentationFilter.All.Select(filter => new KeyedOption(filter.Key, filter.Label)),
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
            GraphicsSettings.RendererDirect3D11 => "Direct3D 11",
            GraphicsSettings.RendererNull => "Null",
            _ => key,
        };
    }
}

/// <summary>One player's XInput slot assignment in Settings → Input.</summary>
public sealed class PlayerPadItem : ObservableObject
{
    private static readonly KeyedOption[] Choices =
    [
        new("-1", "No gamepad"),
        new("0", "XInput slot 1"),
        new("1", "XInput slot 2"),
        new("2", "XInput slot 3"),
        new("3", "XInput slot 4"),
    ];

    private KeyedOption _selected;

    /// <summary>Creates an assignment row.</summary>
    /// <param name="player">Player number, 1 to 4.</param>
    /// <param name="slot">XInput user slot, or -1 for none.</param>
    public PlayerPadItem(int player, int slot)
    {
        Label = "Player " + player.ToString(CultureInfo.InvariantCulture);
        _selected = Choices.FirstOrDefault(
            option => int.TryParse(
                          option.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                      && value == slot)
            ?? Choices[0];
    }

    /// <summary>Gets the label shown in the list.</summary>
    public string Label { get; }

    /// <summary>Gets the selectable slots.</summary>
    public IReadOnlyList<KeyedOption> Options { get; } = Choices;

    /// <summary>Gets or sets the selected slot.</summary>
    public KeyedOption Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>Projects the row back into the persisted slot (-1 for none).</summary>
    public int ToSlot()
    {
        return int.TryParse(_selected.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
            ? slot
            : -1;
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

/// <summary>One Chihiro cabinet binding row: an action and its SDL scancode.</summary>
public sealed class ChihiroBindingItem : ObservableObject
{
    private readonly Func<int> _get;
    private readonly Action<int> _set;
    private bool _isCapturing;

    /// <summary>Creates a cabinet binding row.</summary>
    /// <param name="actionName">Display label.</param>
    /// <param name="get">Reads the current binding.</param>
    /// <param name="set">Stores a new binding.</param>
    /// <param name="capture">Starts key capture for this row.</param>
    public ChihiroBindingItem(
        string actionName,
        Func<int> get,
        Action<int> set,
        Action<ChihiroBindingItem> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ActionName = actionName;
        _get = get;
        _set = set;
        CaptureCommand = new RelayCommand(() => capture(this));
        ClearCommand = new RelayCommand(() => Reset(0));
    }

    /// <summary>Gets the display label.</summary>
    public string ActionName { get; }

    /// <summary>Gets the current binding.</summary>
    public int Scancode => _get();

    /// <summary>Gets the display text for the binding.</summary>
    public string KeysDisplay => _isCapturing ? "Press a key..." : ChihiroKeys.Describe(_get());

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

    /// <summary>Stores a captured physical key.</summary>
    /// <param name="key">Captured key.</param>
    public void Capture(PhysicalKey key)
    {
        int scancode = ChihiroKeys.ToScancode(key);
        if (scancode != 0)
        {
            _set(scancode);
            OnPropertyChanged(nameof(KeysDisplay));
        }

        IsCapturing = false;
    }

    /// <summary>Sets a binding directly.</summary>
    /// <param name="scancode">SDL scancode, mouse binding or 0.</param>
    public void Reset(int scancode)
    {
        _set(scancode);
        IsCapturing = false;
        OnPropertyChanged(nameof(KeysDisplay));
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

/// <summary>Settings page: firmware, storage, session, xemu core options, appearance, and library.</summary>
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
    private bool _chihiroEnabled;

    [ObservableProperty]
    private string _chihiroFirmwareDirectory = string.Empty;

    [ObservableProperty]
    private bool _chihiroLinkEnabled;

    [ObservableProperty]
    private int _chihiroLinkCabinets = 2;

    [ObservableProperty]
    private int _chihiroLinkCabinet = 1;

    [ObservableProperty]
    private string _chihiroLinkHost = string.Empty;

    [ObservableProperty]
    private int _chihiroLinkPort = 9100;

    [ObservableProperty]
    private bool _chihiroConverting;

    [ObservableProperty]
    private string _chihiroConvertStatus = string.Empty;

    [ObservableProperty]
    private int _ramMb = 64;

    [ObservableProperty]
    private KeyedOption? _selectedCoreHosting;

    [ObservableProperty]
    private bool _powerOnAtStartup;

    [ObservableProperty]
    private bool _stayOpenAfterPowerOff = true;

    [ObservableProperty]
    private bool _saveStateOnExit;

    [ObservableProperty]
    private bool _resumeStateOnStart;

    [ObservableProperty]
    private bool _showPoweredOffScreen = true;

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
    private IReadOnlyList<KeyedOption> _audioBackendOptions = [];
    private CoreCapabilities? _coreCapabilities;
    private bool _loadingAudioSettings;
    private string? _lastAudioBackend;

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
        FilterOptions = [.. XemuPresentationFilter.All.Select(item => new KeyedOption(item.Key, item.Label))];
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

    /// <summary>Gets the selectable Chihiro regions.</summary>
    public IReadOnlyList<KeyedOption> ChihiroRegionOptions { get; } =
    [
        new KeyedOption("auto", "Auto"),
        new KeyedOption("jp", "Japan"),
        new KeyedOption("us", "USA"),
        new KeyedOption("ex", "Export"),
    ];

    /// <summary>Gets the selectable Chihiro DIMM sizes.</summary>
    public IReadOnlyList<KeyedOption> ChihiroDimmOptions { get; } =
    [
        new KeyedOption("auto", "Auto"),
        new KeyedOption("128", "128 MB"),
        new KeyedOption("256", "256 MB"),
        new KeyedOption("512", "512 MB"),
        new KeyedOption("1024", "1024 MB"),
    ];

    /// <summary>Gets the selectable Chihiro media-board types.</summary>
    public IReadOnlyList<KeyedOption> ChihiroBoardTypeOptions { get; } =
    [
        new KeyedOption("auto", "Auto"),
        new KeyedOption("type1", "Type 1"),
        new KeyedOption("type3", "Type 3"),
    ];

    [ObservableProperty]
    private bool _chihiroFreePlay;

    [ObservableProperty]
    private bool _chihiroForceFeedback;

    [ObservableProperty]
    private int _chihiroFfbStrength = 100;

    [ObservableProperty]
    private bool _chihiroFfbInvert;

    [ObservableProperty]
    private int _chihiroWheelWeight = 40;

    [ObservableProperty]
    private bool _chihiroWheelAutocenter = true;

    [ObservableProperty]
    private int _chihiroWheelAutocenterStrength = 50;

    [ObservableProperty]
    private bool _chihiroPointerDevices;

    [ObservableProperty]
    private int _chihiroP2AimSpeed = 100;

    [ObservableProperty]
    private bool _chihiroCardsEnabled = true;

    [ObservableProperty]
    private string _chihiroCardSlot1 = string.Empty;

    [ObservableProperty]
    private string _chihiroCardSlot2 = string.Empty;

    [ObservableProperty]
    private string _chihiroCardGundam = string.Empty;

    [ObservableProperty]
    private string _chihiroCardMt1 = string.Empty;

    [ObservableProperty]
    private string _chihiroCardMt2 = string.Empty;

    private readonly string _chihiroCardsFolder = ChihiroCardPaths.ResolveCardsDirectory();

    /// <summary>Gets the xemu folder where blank cards are created.</summary>
    public string ChihiroCardsFolder => _chihiroCardsFolder;

    [ObservableProperty]
    private KeyedOption? _selectedChihiroRegion;

    [ObservableProperty]
    private KeyedOption? _selectedChihiroDimm;

    [ObservableProperty]
    private KeyedOption? _selectedChihiroBoardType;

    /// <summary>Gets the selectable audio backends reported by the core.</summary>
    public IReadOnlyList<KeyedOption> AudioBackendOptions
    {
        get => _audioBackendOptions;
        private set => SetProperty(ref _audioBackendOptions, value);
    }

    /// <summary>Gets the detected DirectSound devices plus the system default.</summary>
    public ObservableCollection<KeyedOption> AudioDeviceOptions { get; } = [];

    [ObservableProperty]
    private KeyedOption? _selectedAudioDevice;

    [ObservableProperty]
    private string _audioDevice = string.Empty;

    /// <summary>Gets a value indicating whether the device is a DirectSound selector.</summary>
    public bool ShowAudioDeviceList =>
        OperatingSystem.IsWindows()
        && string.Equals(SelectedAudioBackend?.Key, AudioBackends.DirectSound, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether the device is a free-form backend name.</summary>
    public bool ShowAudioDeviceText => SelectedAudioBackend?.Key is
        AudioBackends.PulseAudio or AudioBackends.Alsa or AudioBackends.Jack
        or AudioBackends.PipeWire or AudioBackends.Oss or AudioBackends.Sndio;

    /// <summary>Gets a value indicating whether the device row is informational only.</summary>
    public bool ShowAudioDeviceHint => !ShowAudioDeviceList && !ShowAudioDeviceText;

    /// <summary>Gets the hint under the backend row.</summary>
    public string AudioBackendHint => AudioChoices.Hint(_coreCapabilities);

    /// <summary>Gets the hint under the device row.</summary>
    public string AudioDeviceHint => AudioChoices.DeviceHint(SelectedAudioBackend?.Key);

    /// <summary>Gets the selectable voice-worker counts (0 = auto).</summary>
    public IReadOnlyList<int> VoiceWorkerOptions { get; } = [0, 1, 2, 4, 8];

    /// <summary>Gets the keyboard binding rows.</summary>
    public ObservableCollection<KeyboardBindingItem> KeyboardBindings { get; } = [];

    private ChihiroJvsSettings _chihiroJvs = new();

    private ChihiroJvsP2Settings _chihiroJvsP2 = new();

    /// <summary>Gets the Chihiro system-key rows.</summary>
    public ObservableCollection<ChihiroBindingItem> ChihiroSystemBindings { get; } = [];

    /// <summary>Gets the Chihiro driving-key rows.</summary>
    public ObservableCollection<ChihiroBindingItem> ChihiroDrivingBindings { get; } = [];

    /// <summary>Gets the Chihiro gun-key rows.</summary>
    public ObservableCollection<ChihiroBindingItem> ChihiroGunBindings { get; } = [];

    /// <summary>Gets player 2's gun and card binding rows.</summary>
    public ObservableCollection<ChihiroBindingItem> ChihiroPlayer2Bindings { get; } = [];

    /// <summary>Gets the Gundam cockpit binding rows.</summary>
    public ObservableCollection<ChihiroBindingItem> ChihiroGundamBindings { get; } = [];

    /// <summary>Gets the gamepad binding rows.</summary>
    public ObservableCollection<GamepadBindingItem> GamepadBindings { get; } = [];

    /// <summary>Gets the four player-to-XInput-slot assignments.</summary>
    public ObservableCollection<PlayerPadItem> PlayerPads { get; } = [];

    /// <summary>Gets the row currently waiting for a key, or null.</summary>
    public KeyboardBindingItem? CapturingBinding => KeyboardBindings.FirstOrDefault(item => item.IsCapturing);

    /// <summary>Gets the Chihiro row currently waiting for a key, or null.</summary>
    public ChihiroBindingItem? CapturingChihiroBinding => ChihiroSystemBindings
        .Concat(ChihiroDrivingBindings)
        .Concat(ChihiroGunBindings)
        .Concat(ChihiroPlayer2Bindings)
        .Concat(ChihiroGundamBindings)
        .FirstOrDefault(item => item.IsCapturing);

    /// <summary>Gets a value indicating whether a key capture is in progress.</summary>
    public bool IsCapturingKey => CapturingBinding is not null || CapturingChihiroBinding is not null;

    /// <summary>Gets the per-title overrides being edited.</summary>
    public ObservableCollection<TitleOverrideItem> TitleOverrides { get; } = [];

    /// <summary>Gets a value indicating whether any per-title override exists.</summary>
    public bool HasTitleOverrides => TitleOverrides.Count > 0;

    /// <summary>Gets the accent swatches.</summary>
    public ObservableCollection<AccentOption> AccentOptions { get; } = [];

    /// <summary>Gets the library folders being edited.</summary>
    public ObservableCollection<LibraryFolderItem> LibraryFolders { get; } = [];

    [ObservableProperty]
    private string _coverDirectory = string.Empty;

    [ObservableProperty]
    private bool _useOnlineCovers = true;

    [ObservableProperty]
    private bool _autoDownloadCovers = true;

    [ObservableProperty]
    private string _coverCatalogUrl = CoverDefaults.CatalogUrl;

    [ObservableProperty]
    private bool _coverFuzzyMatching = true;

    [ObservableProperty]
    private bool _coverStripAnnotations = true;

    [ObservableProperty]
    private double _coverFuzzyThreshold = CoverDefaults.DefaultFuzzyThreshold;

    [ObservableProperty]
    private string _coverCacheText = string.Empty;

    /// <summary>Gets the BIOS status label.</summary>
    public string BiosStatusText =>
        string.IsNullOrWhiteSpace(BiosPath)
            ? "No BIOS image selected - power-on is disabled."
            : File.Exists(BiosPath) ? "BIOS image found." : "BIOS file not found at this path.";

    /// <summary>Gets a value indicating whether the BIOS path is usable.</summary>
    public bool BiosPathValid => !string.IsNullOrWhiteSpace(BiosPath) && File.Exists(BiosPath);

    /// <summary>Gets a value indicating whether the BIOS warning should be shown.</summary>
    public bool ShowBiosWarning => !BiosPathValid;

    /// <summary>Gets a value indicating whether the Chihiro firmware set is complete.</summary>
    public bool ChihiroReady
    {
        get
        {
            string directory = ChihiroFirmwareFolder;
            return directory.Length > 0
                   && File.Exists(Path.Combine(directory, ChihiroSettings.BiosFileName))
                   && File.Exists(Path.Combine(directory, ChihiroSettings.MediaboardFileName))
                   && File.Exists(Path.Combine(directory, ChihiroSettings.Ic10FileName))
                   && File.Exists(Path.Combine(directory, ChihiroSettings.Ic11FileName))
                   && File.Exists(Path.Combine(directory, ChihiroSettings.Pc20FileName));
        }
    }

    private string ChihiroFirmwareFolder
    {
        get
        {
            string directory = ChihiroFirmwareDirectory.Trim();
            if (directory.Length == 0 && BiosPath.Length > 0)
            {
                directory = Path.GetDirectoryName(BiosPath) ?? string.Empty;
            }

            return directory;
        }
    }

    private bool ChihiroNetworkFirmwarePresent
    {
        get
        {
            string directory = ChihiroFirmwareFolder;
            return directory.Length > 0
                   && File.Exists(Path.Combine(directory, ChihiroSettings.NetFirmwareFileName));
        }
    }

    private string? ChihiroNetworkFirmwareVersion =>
        ChihiroNetworkFirmwarePresent
            ? ChihiroNetworkFirmware.ReadVersion(
                Path.Combine(ChihiroFirmwareFolder, ChihiroSettings.NetFirmwareFileName))
            : null;

    /// <summary>Gets the Chihiro firmware status label.</summary>
    public string ChihiroStatusText =>
        !ChihiroEnabled
            ? "Off. A .bin netboot image enables the Chihiro for that launch."
            : !ChihiroReady
                ? "Chihiro firmware is incomplete; choose the folder with chihiro_xbox_bios.bin and the media-board files."
                : ChihiroNetworkFirmwarePresent
                    ? ChihiroNetworkFirmwareVersion is { } version
                        ? $"Chihiro firmware and network firmware {version} found."
                        : "Chihiro firmware and an unversioned network firmware found."
                    : "Chihiro firmware found. Add ver1305.bin for MJ2, MJ3 and other network titles.";

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
        ChihiroEnabled = settings.Chihiro.Enabled;
        ChihiroFirmwareDirectory = settings.Chihiro.FirmwareDirectory;
        ChihiroLinkSettings link = settings.Chihiro.Link ?? new ChihiroLinkSettings();
        ChihiroLinkEnabled = link.Enabled;
        ChihiroLinkCabinets = link.Cabinets;
        ChihiroLinkCabinet = link.Cabinet;
        ChihiroLinkHost = link.Host;
        ChihiroLinkPort = link.Port;
        _chihiroJvs = (settings.Chihiro.Jvs ?? new ChihiroJvsSettings()).Clone();
        _chihiroJvsP2 = (settings.Chihiro.JvsP2 ?? new ChihiroJvsP2Settings()).Clone();
        ChihiroP2AimSpeed = _chihiroJvsP2.AimSpeed;
        ChihiroCardReaderSettings cards = settings.Chihiro.CardReader ?? new ChihiroCardReaderSettings();
        ChihiroCardsEnabled = cards.Enabled;
        ChihiroCardSlot1 = cards.Hw210Slot1;
        ChihiroCardSlot2 = cards.Hw210Slot2;
        ChihiroCardGundam = cards.Hw210Gundam;
        ChihiroCardMt1 = cards.Crp1231Mt1;
        ChihiroCardMt2 = cards.Crp1231Mt2;
        RebuildChihiroBindings();
        ChihiroFreePlay = settings.Chihiro.FreePlay;
        ChihiroForceFeedback = settings.Chihiro.ForceFeedback;
        ChihiroFfbStrength = settings.Chihiro.FfbStrength;
        ChihiroFfbInvert = settings.Chihiro.FfbInvert;
        ChihiroWheelWeight = settings.Chihiro.WheelWeight;
        ChihiroWheelAutocenter = settings.Chihiro.WheelAutocenter;
        ChihiroWheelAutocenterStrength = settings.Chihiro.WheelAutocenterStrength;
        ChihiroPointerDevices = settings.Chihiro.PointerDevices;
        SelectedChihiroRegion = ChihiroRegionOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Chihiro.Region, StringComparison.Ordinal))
            ?? ChihiroRegionOptions.First(option => string.Equals(option.Key, "ex", StringComparison.Ordinal));
        SelectedChihiroDimm = ChihiroDimmOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Chihiro.DimmSize, StringComparison.Ordinal))
            ?? ChihiroDimmOptions[0];
        SelectedChihiroBoardType = ChihiroBoardTypeOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Chihiro.BoardType, StringComparison.Ordinal))
            ?? ChihiroBoardTypeOptions[0];
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
        RefreshAudioBackends(settings.Audio.Backend);
        _loadingAudioSettings = true;
        SelectedAudioBackend = AudioBackendOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Audio.Backend, StringComparison.Ordinal))
            ?? AudioBackendOptions[0];
        _loadingAudioSettings = false;
        AudioDevice = settings.Audio.Device;
        RefreshAudioDevices();
        SelectedCoreHosting = CoreHostingOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.CoreHosting, StringComparison.Ordinal))
            ?? CoreHostingOptions[0];
        PowerOnAtStartup = settings.PowerOnAtStartup;
        StayOpenAfterPowerOff = settings.StayOpenAfterPowerOff;
        SaveStateOnExit = settings.SaveStateOnExit;
        ResumeStateOnStart = settings.ResumeStateOnStart;
        ShowPoweredOffScreen = settings.ShowPoweredOffScreen;
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

        CoverDirectory = settings.Covers.Directory;
        UseOnlineCovers = settings.Covers.UseOnline;
        AutoDownloadCovers = settings.Covers.AutoDownload;
        CoverCatalogUrl = settings.Covers.CatalogUrl;
        CoverFuzzyMatching = settings.Covers.FuzzyMatching;
        CoverStripAnnotations = settings.Covers.StripAnnotations;
        CoverFuzzyThreshold = settings.Covers.FuzzyThreshold;
        UpdateCoverCacheText();

        OnPropertyChanged(nameof(HasLibraryFolders));
        OnPropertyChanged(nameof(BiosStatusText));
        OnPropertyChanged(nameof(BiosPathValid));
        OnPropertyChanged(nameof(ShowBiosWarning));
        OnPropertyChanged(nameof(ChihiroReady));
        OnPropertyChanged(nameof(ChihiroStatusText));
    }

    /// <summary>Public command: saves the draft to disk and applies it.</summary>
    [RelayCommand]
    private void Save()
    {
        ChihiroP2AimSpeed = Math.Clamp(ChihiroP2AimSpeed, 0, 400);
        ChihiroWheelWeight = Math.Clamp(ChihiroWheelWeight, 0, 100);
        ChihiroWheelAutocenterStrength = Math.Clamp(ChihiroWheelAutocenterStrength, 0, 100);
        _chihiroJvsP2.AimSpeed = ChihiroP2AimSpeed;
        var next = new EmulatorSettings
        {
            BiosPath = BiosPath.Trim(),
            McpxRomPath = McpxRomPath.Trim(),
            EepromPath = EepromPath.Trim(),
            HddPath = HddPath.Trim(),
            DvdPath = DvdPath.Trim(),
            Chihiro = new ChihiroSettings
            {
                Enabled = ChihiroEnabled,
                FirmwareDirectory = ChihiroFirmwareDirectory.Trim(),
                Link = new ChihiroLinkSettings
                {
                    Enabled = ChihiroLinkEnabled,
                    Cabinets = ChihiroLinkCabinets,
                    Cabinet = ChihiroLinkCabinet,
                    Host = ChihiroLinkHost.Trim(),
                    Port = ChihiroLinkPort,
                },
                Jvs = _chihiroJvs.Clone(),
                JvsP2 = _chihiroJvsP2.Clone(),
                CardReader = new ChihiroCardReaderSettings
                {
                    Enabled = ChihiroCardsEnabled,
                    Hw210Slot1 = ChihiroCardSlot1.Trim(),
                    Hw210Slot2 = ChihiroCardSlot2.Trim(),
                    Hw210Gundam = ChihiroCardGundam.Trim(),
                    Crp1231Mt1 = ChihiroCardMt1.Trim(),
                    Crp1231Mt2 = ChihiroCardMt2.Trim(),
                },
                FreePlay = ChihiroFreePlay,
                Region = SelectedChihiroRegion?.Key ?? "ex",
                DimmSize = SelectedChihiroDimm?.Key ?? "auto",
                BoardType = SelectedChihiroBoardType?.Key ?? "auto",
                ForceFeedback = ChihiroForceFeedback,
                FfbStrength = ChihiroFfbStrength,
                FfbInvert = ChihiroFfbInvert,
                WheelWeight = ChihiroWheelWeight,
                WheelAutocenter = ChihiroWheelAutocenter,
                WheelAutocenterStrength = ChihiroWheelAutocenterStrength,
                PointerDevices = ChihiroPointerDevices,
            },
            RamMb = RamMb,
            Input = BuildInputSettings(),
            Graphics = BuildGraphicsSettings(),
            Audio = BuildAudioSettings(),
            XemuCorePath = XemuCorePath.Trim(),
            CoreHosting = SelectedCoreHosting?.Key ?? EmulatorSettings.CoreHostingInProcess,
            PowerOnAtStartup = PowerOnAtStartup,
            StayOpenAfterPowerOff = StayOpenAfterPowerOff,
            SaveStateOnExit = SaveStateOnExit,
            ResumeStateOnStart = ResumeStateOnStart,
            ShowPoweredOffScreen = ShowPoweredOffScreen,
            Filter = SelectedFilter?.Key ?? XemuPresentationFilter.Nearest,
            Accent = SelectedAccent?.Key ?? "Emerald",
            LibraryFolders = [.. LibraryFolders.Select(item => item.Path)],
            Covers = BuildCoverSettings(),
        };
        CopyDonationState(next, Shell.Config);
        CopyRecentGames(next, Shell.Config);
        next.ShowFpsOverlay = Shell.Config.ShowFpsOverlay;
        next.LibraryGridView = Shell.Config.LibraryGridView;
        next.LibraryIconSize = Shell.Config.LibraryIconSize;
        next.LibraryFavorites = [.. Shell.Config.LibraryFavorites];
        foreach (KeyValuePair<string, LibraryPlayRecord> pair in Shell.Config.LibraryPlayState)
        {
            if (pair.Value is not null)
            {
                next.LibraryPlayState[pair.Key] = pair.Value.Clone();
            }
        }

        foreach (KeyValuePair<string, uint> pair in Shell.Config.LibraryTitleIds)
        {
            next.LibraryTitleIds[pair.Key] = pair.Value;
        }
        foreach (TitleOverrideItem item in TitleOverrides)
        {
            next.TitleOverrides[EmulatorSettings.CanonicalTitleKey(item.Path)] = item.ToOverride();
        }

        Shell.SaveSettings(next);
        Shell.Home.NotifySettingsChanged();
    }

    /// <summary>Builds cover settings from the current controls.</summary>
    /// <returns>The normalized cover settings.</returns>
    private CoverSettings BuildCoverSettings()
    {
        return new CoverSettings
        {
            Directory = CoverDirectory.Trim(),
            UseOnline = UseOnlineCovers,
            AutoDownload = AutoDownloadCovers,
            CatalogUrl = CoverDefaults.NormalizeCatalogUrl(CoverCatalogUrl),
            FuzzyMatching = CoverFuzzyMatching,
            StripAnnotations = CoverStripAnnotations,
            FuzzyThreshold = CoverDefaults.ClampThreshold(CoverFuzzyThreshold),
        };
    }

    /// <summary>Public command: chooses the local cover folder.</summary>
    [RelayCommand]
    private async Task BrowseCoverDirectoryAsync()
    {
        string? folder = await Shell.Dialogs.PickFolderAsync("Choose the folder containing cover images");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            CoverDirectory = folder;
        }
    }

    /// <summary>Public command: restores the default local cover folder.</summary>
    [RelayCommand]
    private void ResetCoverDirectory()
    {
        CoverDirectory = string.Empty;
    }

    /// <summary>Public command: restores the default catalog URL.</summary>
    [RelayCommand]
    private void ResetCoverCatalogUrl()
    {
        CoverCatalogUrl = CoverDefaults.CatalogUrl;
    }

    /// <summary>Public command: refreshes the online cover catalog now.</summary>
    [RelayCommand]
    private async Task RefreshCoverCatalogAsync()
    {
        string directory = string.IsNullOrWhiteSpace(CoverDirectory)
            ? CoverDefaults.LocalDirectory
            : CoverDirectory.Trim();
        using var service = new CoverService(new CoverServiceOptions
        {
            CatalogUri = new Uri(CoverDefaults.NormalizeCatalogUrl(CoverCatalogUrl)),
            UseOnlineCovers = true,
            CoverDirectory = directory,
            CacheDirectory = CoverDefaults.CacheDirectory,
            FuzzyThreshold = CoverDefaults.ClampThreshold(CoverFuzzyThreshold),
            AnnotationStripping = CoverStripAnnotations,
            FuzzyMatching = CoverFuzzyMatching,
        });
        CoverCacheText = "Refreshing cover catalog…";
        CoverCatalogRefreshResult result = await service.RefreshCatalogAsync(force: true);
        if (result.Success)
        {
            CoverCacheText = "Catalog " + result.PackVersion + " · " + result.CoverCount + " covers";
            Shell.ShowToast("Cover catalog refreshed; save settings to apply changes.");
        }
        else
        {
            CoverCacheText = "Catalog refresh failed: " + (result.Error ?? "unknown error");
            Shell.ShowToast("Cover catalog refresh failed.");
        }

        UpdateCoverCacheText();
    }

    /// <summary>Public command: clears the online cover cache.</summary>
    [RelayCommand]
    private void ClearCoverCache()
    {
        new CoverStore(CoverDefaults.CacheDirectory).Clear();
        UpdateCoverCacheText();
        Shell.ShowToast("Cover cache cleared; rescan the library to download covers again.");
    }

    private void UpdateCoverCacheText()
    {
        CoverCacheStats stats = new CoverStore(CoverDefaults.CacheDirectory).GetStats();
        CoverCacheText = stats.ImageCount == 0
            ? "No online covers cached."
            : stats.ImageCount + " covers · " + FormatBytes(stats.Bytes) + " cached";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        return bytes >= 1024L * 1024
            ? (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
    }

    /// <summary>
    /// Copies the recently played list so saving the Settings page does not
    /// erase the Library page's recent entries.
    /// </summary>
    /// <param name="target">The settings being saved.</param>
    /// <param name="source">The live settings.</param>
    internal static void CopyRecentGames(EmulatorSettings target, EmulatorSettings source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.RecentGames = [.. source.RecentGames.Select(static entry => entry.Clone())];
    }

    /// <summary>
    /// Copies the fields the Settings page does not edit so saving the page
    /// cannot hide the donation banner or forget a confirmed donation.
    /// </summary>
    /// <param name="target">The settings being saved.</param>
    /// <param name="source">The live settings.</param>
    internal static void CopyDonationState(EmulatorSettings target, EmulatorSettings source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        target.DonationEmail = source.DonationEmail;
        target.HasDonated = source.HasDonated;
        target.DonationLastCheckedUtc = source.DonationLastCheckedUtc;
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
        string backend = SelectedAudioBackend?.Key ?? AudioBackends.Auto;
        string device = AudioBackends.SupportsDevice(backend)
            ? string.Equals(backend, AudioBackends.DirectSound, StringComparison.Ordinal)
                ? SelectedAudioDevice?.Key ?? string.Empty
                : AudioDevice.Trim()
            : string.Empty;
        var audio = new AudioSettings
        {
            UseDsp = UseDsp,
            UseDspJit = UseDspJit,
            Hrtf = Hrtf,
            VolumeLimit = VolumeLimit,
            VoiceWorkers = VoiceWorkers,
            Backend = backend,
            Device = device,
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

        PlayerPads.Clear();
        int[] slots = input.PlayerGamepads;
        for (int port = 0; port < 4; port++)
        {
            int slot = slots is not null && port < slots.Length ? slots[port] : port;
            PlayerPads.Add(new PlayerPadItem(port + 1, slot));
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
            PlayerGamepads = [.. PlayerPads.Select(item => item.ToSlot())],
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

    private void RebuildChihiroBindings()
    {
        ChihiroSystemBindings.Clear();
        ChihiroDrivingBindings.Clear();
        ChihiroGunBindings.Clear();
        ChihiroPlayer2Bindings.Clear();
        ChihiroGundamBindings.Clear();

        AddChihiroBinding(ChihiroSystemBindings, "Start",
            () => _chihiroJvs.Start, value => _chihiroJvs.Start = value);
        AddChihiroBinding(ChihiroSystemBindings, "Service",
            () => _chihiroJvs.Service, value => _chihiroJvs.Service = value);
        AddChihiroBinding(ChihiroSystemBindings, "Coin",
            () => _chihiroJvs.Coin, value => _chihiroJvs.Coin = value);
        AddChihiroBinding(ChihiroSystemBindings, "Test",
            () => _chihiroJvs.Test, value => _chihiroJvs.Test = value);
        AddChihiroBinding(ChihiroSystemBindings, "Card in",
            () => _chihiroJvs.CardIn, value => _chihiroJvs.CardIn = value);

        AddChihiroBinding(ChihiroDrivingBindings, "Steer left",
            () => _chihiroJvs.SteerLeft, value => _chihiroJvs.SteerLeft = value);
        AddChihiroBinding(ChihiroDrivingBindings, "Steer right",
            () => _chihiroJvs.SteerRight, value => _chihiroJvs.SteerRight = value);
        AddChihiroBinding(ChihiroDrivingBindings, "Gas",
            () => _chihiroJvs.Gas, value => _chihiroJvs.Gas = value);
        AddChihiroBinding(ChihiroDrivingBindings, "Brake",
            () => _chihiroJvs.Brake, value => _chihiroJvs.Brake = value);
        AddChihiroBinding(ChihiroDrivingBindings, "Gear up",
            () => _chihiroJvs.GearUp, value => _chihiroJvs.GearUp = value);
        AddChihiroBinding(ChihiroDrivingBindings, "Gear down",
            () => _chihiroJvs.GearDown, value => _chihiroJvs.GearDown = value);

        AddChihiroBinding(ChihiroGunBindings, "HotD3 trigger",
            () => _chihiroJvs.Hotd3Trigger, value => _chihiroJvs.Hotd3Trigger = value);
        AddChihiroBinding(ChihiroGunBindings, "HotD3 body",
            () => _chihiroJvs.Hotd3Body, value => _chihiroJvs.Hotd3Body = value);
        AddChihiroBinding(ChihiroGunBindings, "Virtua Cop 3 trigger",
            () => _chihiroJvs.Vc3Trigger, value => _chihiroJvs.Vc3Trigger = value);
        AddChihiroBinding(ChihiroGunBindings, "Virtua Cop 3 body",
            () => _chihiroJvs.Vc3Body, value => _chihiroJvs.Vc3Body = value);
        AddChihiroBinding(ChihiroGunBindings, "Virtua Cop 3 pedal",
            () => _chihiroJvs.Vc3Pedal, value => _chihiroJvs.Vc3Pedal = value);
        AddChihiroBinding(ChihiroGunBindings, "Ghost Squad trigger",
            () => _chihiroJvs.GsTrigger, value => _chihiroJvs.GsTrigger = value);
        AddChihiroBinding(ChihiroGunBindings, "Ghost Squad body",
            () => _chihiroJvs.GsBody, value => _chihiroJvs.GsBody = value);
        AddChihiroBinding(ChihiroGunBindings, "Ghost Squad change",
            () => _chihiroJvs.GsChange, value => _chihiroJvs.GsChange = value);

        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Start",
            () => _chihiroJvsP2.Start, value => _chihiroJvsP2.Start = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Coin",
            () => _chihiroJvsP2.Coin, value => _chihiroJvsP2.Coin = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Card in",
            () => _chihiroJvsP2.CardIn, value => _chihiroJvsP2.CardIn = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 aim left",
            () => _chihiroJvsP2.AimLeft, value => _chihiroJvsP2.AimLeft = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 aim right",
            () => _chihiroJvsP2.AimRight, value => _chihiroJvsP2.AimRight = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 aim up",
            () => _chihiroJvsP2.AimUp, value => _chihiroJvsP2.AimUp = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 aim down",
            () => _chihiroJvsP2.AimDown, value => _chihiroJvsP2.AimDown = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 HotD3 trigger",
            () => _chihiroJvsP2.Hotd3Trigger, value => _chihiroJvsP2.Hotd3Trigger = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 HotD3 body",
            () => _chihiroJvsP2.Hotd3Body, value => _chihiroJvsP2.Hotd3Body = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Virtua Cop 3 trigger",
            () => _chihiroJvsP2.Vc3Trigger, value => _chihiroJvsP2.Vc3Trigger = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Virtua Cop 3 body",
            () => _chihiroJvsP2.Vc3Body, value => _chihiroJvsP2.Vc3Body = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Virtua Cop 3 pedal",
            () => _chihiroJvsP2.Vc3Pedal, value => _chihiroJvsP2.Vc3Pedal = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Virtua Cop 3 reload",
            () => _chihiroJvsP2.Vc3Reload, value => _chihiroJvsP2.Vc3Reload = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Ghost Squad trigger",
            () => _chihiroJvsP2.GsTrigger, value => _chihiroJvsP2.GsTrigger = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Ghost Squad body",
            () => _chihiroJvsP2.GsBody, value => _chihiroJvsP2.GsBody = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Ghost Squad change",
            () => _chihiroJvsP2.GsChange, value => _chihiroJvsP2.GsChange = value);
        AddChihiroBinding(ChihiroPlayer2Bindings, "P2 Ghost Squad reload",
            () => _chihiroJvsP2.GsReload, value => _chihiroJvsP2.GsReload = value);

        AddChihiroBinding(ChihiroGundamBindings, "Left stick up",
            () => _chihiroJvs.GundamLUp, value => _chihiroJvs.GundamLUp = value);
        AddChihiroBinding(ChihiroGundamBindings, "Left stick down",
            () => _chihiroJvs.GundamLDown, value => _chihiroJvs.GundamLDown = value);
        AddChihiroBinding(ChihiroGundamBindings, "Left stick left",
            () => _chihiroJvs.GundamLLeft, value => _chihiroJvs.GundamLLeft = value);
        AddChihiroBinding(ChihiroGundamBindings, "Left stick right",
            () => _chihiroJvs.GundamLRight, value => _chihiroJvs.GundamLRight = value);
        AddChihiroBinding(ChihiroGundamBindings, "Left trigger",
            () => _chihiroJvs.GundamLTrigger, value => _chihiroJvs.GundamLTrigger = value);
        AddChihiroBinding(ChihiroGundamBindings, "Left button",
            () => _chihiroJvs.GundamLButton, value => _chihiroJvs.GundamLButton = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right stick up",
            () => _chihiroJvs.GundamRUp, value => _chihiroJvs.GundamRUp = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right stick down",
            () => _chihiroJvs.GundamRDown, value => _chihiroJvs.GundamRDown = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right stick left",
            () => _chihiroJvs.GundamRLeft, value => _chihiroJvs.GundamRLeft = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right stick right",
            () => _chihiroJvs.GundamRRight, value => _chihiroJvs.GundamRRight = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right trigger",
            () => _chihiroJvs.GundamRTrigger, value => _chihiroJvs.GundamRTrigger = value);
        AddChihiroBinding(ChihiroGundamBindings, "Right button",
            () => _chihiroJvs.GundamRButton, value => _chihiroJvs.GundamRButton = value);
        AddChihiroBinding(ChihiroGundamBindings, "Pedal",
            () => _chihiroJvs.GundamPedal, value => _chihiroJvs.GundamPedal = value);

        OnPropertyChanged(nameof(CapturingChihiroBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
    }

    private void AddChihiroBinding(
        ObservableCollection<ChihiroBindingItem> rows,
        string name,
        Func<int> get,
        Action<int> set)
    {
        rows.Add(new ChihiroBindingItem(name, get, set, BeginChihiroCapture));
    }

    private void BeginChihiroCapture(ChihiroBindingItem item)
    {
        foreach (KeyboardBindingItem binding in KeyboardBindings)
        {
            binding.IsCapturing = false;
        }

        foreach (ChihiroBindingItem binding in ChihiroSystemBindings
                     .Concat(ChihiroDrivingBindings)
                     .Concat(ChihiroGunBindings)
                     .Concat(ChihiroPlayer2Bindings)
                     .Concat(ChihiroGundamBindings))
        {
            binding.IsCapturing = false;
        }

        item.IsCapturing = true;
        OnPropertyChanged(nameof(CapturingBinding));
        OnPropertyChanged(nameof(CapturingChihiroBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
        Shell.ShowToast("Press a key for " + item.ActionName + ". Esc cancels.");
    }

    /// <summary>Public command: restores the cabinet keys to the core defaults.</summary>
    [RelayCommand]
    private void ResetChihiroKeys()
    {
        _chihiroJvs = new ChihiroJvsSettings();
        _chihiroJvsP2 = new ChihiroJvsP2Settings();
        ChihiroP2AimSpeed = 100;
        RebuildChihiroBindings();
        Shell.ShowToast("Chihiro cabinet keys reset.");
    }

    private void BeginCapture(KeyboardBindingItem item)
    {
        foreach (KeyboardBindingItem binding in KeyboardBindings)
        {
            binding.IsCapturing = false;
        }

        foreach (ChihiroBindingItem binding in ChihiroSystemBindings
                     .Concat(ChihiroDrivingBindings)
                     .Concat(ChihiroGunBindings)
                     .Concat(ChihiroPlayer2Bindings)
                     .Concat(ChihiroGundamBindings))
        {
            binding.IsCapturing = false;
        }

        item.IsCapturing = true;
        OnPropertyChanged(nameof(CapturingBinding));
        OnPropertyChanged(nameof(CapturingChihiroBinding));
        OnPropertyChanged(nameof(IsCapturingKey));
        Shell.ShowToast("Press a key for " + item.ActionName + ". Esc cancels.");
    }

    /// <summary>Captures the next key while a binding row is armed.</summary>
    /// <param name="key">Pressed key.</param>
    /// <param name="physicalKey">Physical key, used for cabinet scancodes.</param>
    /// <returns>True when the key was consumed by the capture.</returns>
    public bool CaptureKey(Key key, PhysicalKey physicalKey)
    {
        ChihiroBindingItem? cabinet = CapturingChihiroBinding;
        if (cabinet is not null)
        {
            if (key == Key.Escape)
            {
                cabinet.IsCapturing = false;
            }
            else
            {
                cabinet.Capture(physicalKey);
            }

            OnPropertyChanged(nameof(CapturingChihiroBinding));
            OnPropertyChanged(nameof(IsCapturingKey));
            return true;
        }

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

    /// <summary>
    /// Public command: converts an Xbox-format Chihiro ISO into the FATX
    /// netboot image the Chihiro machine boots. The game files in these
    /// homebrew conversions are already decrypted; a raw GD-ROM is not.
    /// </summary>
    [RelayCommand]
    private async Task ConvertChihiroIsoAsync()
    {
        string? iso = await Shell.Dialogs.PickFileAsync(
            "Choose a Chihiro Xbox-format ISO",
            FileDialogService.ChihiroIsoTypes);
        if (iso is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(iso) + ".bin";
        string? output = await Shell.Dialogs.PickSaveFileAsync(
            "Save the Chihiro netboot image",
            suggested,
            FileDialogService.NetbootTypes);
        if (output is null)
        {
            return;
        }

        ChihiroConverting = true;
        ChihiroConvertStatus = "Converting...";
        try
        {
            var progress = new Progress<double>(
                value => ChihiroConvertStatus = $"Converting... {value * 100:0}%");
            ChihiroNetbootBuilder.Result result = await Task.Run(
                () => ChihiroNetbootBuilder.Build(iso, output, progress));
            ChihiroConvertStatus =
                $"Wrote {Path.GetFileName(result.OutputPath)} " +
                $"({result.ImageBytes / (1024 * 1024)} MiB game area, {result.GameId}).";
            ChihiroEnabled = true;
            Shell.ShowToast("Chihiro netboot image created.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException)
        {
            ChihiroConvertStatus = "Conversion failed: " + exception.Message;
            Shell.ShowToast("Chihiro conversion failed: " + exception.Message);
        }
        finally
        {
            ChihiroConverting = false;
        }
    }

    /// <summary>
    /// Public command: converts a raw Redump GD-ROM cue/bin set into a netboot
    /// image. The game's small MAME key ZIP supplies the DES key and the
    /// loader; the high-density track is decrypted directly.
    /// </summary>
    [RelayCommand]
    private async Task ConvertChihiroGdRomAsync()
    {
        string? disc = await Shell.Dialogs.PickFileAsync(
            "Choose the game's cue sheet or CHD",
            FileDialogService.ChihiroDiscTypes);
        if (disc is null)
        {
            return;
        }

        string? key = await Shell.Dialogs.PickFileAsync(
            "Choose the game's MAME key ZIP",
            FileDialogService.KeyArchiveTypes);
        if (key is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(disc) + ".bin";
        string? output = await Shell.Dialogs.PickSaveFileAsync(
            "Save the Chihiro netboot image",
            suggested,
            FileDialogService.NetbootTypes);
        if (output is null)
        {
            return;
        }

        ChihiroConverting = true;
        ChihiroConvertStatus = "Converting...";
        try
        {
            var progress = new Progress<double>(
                value => ChihiroConvertStatus = $"Converting... {value * 100:0}%");
            ChihiroRedumpConverter.Result result = await Task.Run(
                () => ChihiroRedumpConverter.Convert(disc, key, output, progress));
            ChihiroConvertStatus =
                $"Wrote {Path.GetFileName(result.OutputPath)} " +
                $"({result.ImageBytes / (1024 * 1024)} MiB, {result.Loader} -> {result.FatxName}).";
            ChihiroEnabled = true;
            Shell.ShowToast("Chihiro netboot image created.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException
                                              or CryptographicException)
        {
            ChihiroConvertStatus = "Conversion failed: " + exception.Message;
            Shell.ShowToast("Chihiro conversion failed: " + exception.Message);
        }
        finally
        {
            ChihiroConverting = false;
        }
    }

    /// <summary>Public command: browses for the Chihiro firmware folder.</summary>
    [RelayCommand]
    private async Task BrowseChihiroFolderAsync()
    {
        string? folder = await Shell.Dialogs.PickFolderAsync("Choose the folder with the Chihiro firmware");
        if (folder is not null)
        {
            ChihiroFirmwareDirectory = folder;
        }
    }

    /// <summary>Public command: browses for one of the cabinet's card images.</summary>
    [RelayCommand]
    private async Task BrowseChihiroCardAsync(string? slot)
    {
        string? path = await Shell.Dialogs.PickFileAsync("Choose a Chihiro card image", FileDialogService.CardTypes);
        if (path is null)
        {
            return;
        }

        switch (slot)
        {
            case "hw210-slot1":
                ChihiroCardSlot1 = path;
                break;
            case "hw210-slot2":
                ChihiroCardSlot2 = path;
                break;
            case "hw210-gundam":
                ChihiroCardGundam = path;
                break;
            case "crp1231-mt1":
                ChihiroCardMt1 = path;
                break;
            case "crp1231-mt2":
                ChihiroCardMt2 = path;
                break;
        }
    }

    /// <summary>Public command: opens the folder where the core creates blank cards.</summary>
    [RelayCommand]
    private async Task OpenChihiroCardsFolderAsync()
    {
        string folder = ChihiroCardPaths.ResolveCardsDirectory();
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception)
        {
            Shell.ShowToast("Could not create the cards folder: " + exception.Message);
            return;
        }

        await Shell.Dialogs.OpenFolderAsync(folder);
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
            case "chihiro-firmware":
                ChihiroFirmwareDirectory = string.Empty;
                break;
            case "chihiro-card-hw210-slot1":
                ChihiroCardSlot1 = string.Empty;
                break;
            case "chihiro-card-hw210-slot2":
                ChihiroCardSlot2 = string.Empty;
                break;
            case "chihiro-card-hw210-gundam":
                ChihiroCardGundam = string.Empty;
                break;
            case "chihiro-card-crp1231-mt1":
                ChihiroCardMt1 = string.Empty;
                break;
            case "chihiro-card-crp1231-mt2":
                ChihiroCardMt2 = string.Empty;
                break;
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnBiosPathChanged(string value)
    {
        OnPropertyChanged(nameof(BiosStatusText));
        OnPropertyChanged(nameof(BiosPathValid));
        OnPropertyChanged(nameof(ShowBiosWarning));
        OnPropertyChanged(nameof(ChihiroReady));
        OnPropertyChanged(nameof(ChihiroStatusText));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnChihiroEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ChihiroReady));
        OnPropertyChanged(nameof(ChihiroStatusText));
    }

    partial void OnChihiroLinkCabinetsChanged(int value)
    {
        if (ChihiroLinkCabinet > value)
        {
            ChihiroLinkCabinet = value;
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnChihiroFirmwareDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(ChihiroReady));
        OnPropertyChanged(nameof(ChihiroStatusText));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnXemuCorePathChanged(string value)
    {
        RefreshRenderers();
        RefreshAudioBackends();
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

    /// <summary>
    /// Rebuilds the audio backend list from what the selected core reports.
    /// </summary>
    /// <param name="preferredKey">Backend key to select instead of the current choice.</param>
    private void RefreshAudioBackends(string? preferredKey = null)
    {
        string selected = preferredKey ?? SelectedAudioBackend?.Key ?? AudioBackends.Auto;
        AudioBackendOptions = [.. AudioChoices.Keys(_coreCapabilities)
            .Select(key => new KeyedOption(key, AudioChoices.Label(key)))];
        _loadingAudioSettings = true;
        SelectedAudioBackend = AudioBackendOptions.FirstOrDefault(
            option => string.Equals(option.Key, selected, StringComparison.Ordinal))
            ?? AudioBackendOptions[0];
        _loadingAudioSettings = false;
        _lastAudioBackend = SelectedAudioBackend?.Key;
        OnPropertyChanged(nameof(AudioBackendHint));
        OnPropertyChanged(nameof(AudioDeviceHint));
        OnPropertyChanged(nameof(ShowAudioDeviceList));
        OnPropertyChanged(nameof(ShowAudioDeviceText));
        OnPropertyChanged(nameof(ShowAudioDeviceHint));
    }

    /// <summary>Public command: re-reads the DirectSound output devices.</summary>
    [RelayCommand]
    private void RefreshAudioDevices()
    {
        string current = SelectedAudioDevice?.Key ?? AudioDevice;
        AudioDeviceOptions.Clear();
        AudioDeviceOptions.Add(new KeyedOption(string.Empty, "System default"));
        foreach (AudioDeviceInfo device in AudioDeviceService.Enumerate())
        {
            AudioDeviceOptions.Add(new KeyedOption(device.Id, device.Name));
        }

        KeyedOption? match = AudioDeviceOptions.FirstOrDefault(
            option => string.Equals(option.Key, current, StringComparison.OrdinalIgnoreCase));
        if (match is null
            && !string.IsNullOrWhiteSpace(current)
            && string.Equals(SelectedAudioBackend?.Key, AudioBackends.DirectSound, StringComparison.Ordinal))
        {
            // Keep a saved device visible when it was unplugged, so the user
            // can see why the launch will fall back to the system default.
            match = new KeyedOption(current, "Missing device (" + current + ")");
            AudioDeviceOptions.Add(match);
        }

        SelectedAudioDevice = match ?? AudioDeviceOptions[0];
    }

    partial void OnSelectedAudioBackendChanged(KeyedOption? value)
    {
        if (_loadingAudioSettings)
        {
            return;
        }

        string? backend = value?.Key;
        if (string.Equals(_lastAudioBackend, AudioBackends.DirectSound, StringComparison.Ordinal)
            && !string.Equals(backend, AudioBackends.DirectSound, StringComparison.Ordinal))
        {
            // A DirectSound device is a GUID; it is not a PulseAudio/ALSA name.
            AudioDevice = string.Empty;
        }

        if (string.Equals(backend, AudioBackends.DirectSound, StringComparison.Ordinal)
            && !string.Equals(_lastAudioBackend, AudioBackends.DirectSound, StringComparison.Ordinal))
        {
            // A backend name is not a DirectSound GUID; start on the default.
            AudioDevice = string.Empty;
        }

        _lastAudioBackend = backend;
        if (string.Equals(backend, AudioBackends.DirectSound, StringComparison.Ordinal))
        {
            RefreshAudioDevices();
        }

        OnPropertyChanged(nameof(AudioDeviceHint));
        OnPropertyChanged(nameof(ShowAudioDeviceList));
        OnPropertyChanged(nameof(ShowAudioDeviceText));
        OnPropertyChanged(nameof(ShowAudioDeviceHint));
    }

    partial void OnSelectedAudioDeviceChanged(KeyedOption? value)
    {
        if (value is not null)
        {
            AudioDevice = value.Key;
        }
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
