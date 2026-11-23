using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>One selectable option in a settings drop-down: a persisted key, a label and an optional hint.</summary>
/// <remarks>Creates an option.</remarks>
/// <param name="key">Value persisted in the settings file.</param>
/// <param name="label">Text shown in the drop-down.</param>
/// <param name="hint">Optional availability/behavior hint shown next to the label.</param>
public sealed class KeyedOption(string key, string label, string hint = "")
{

    /// <summary>Gets the persisted key.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the display label.</summary>
    public string Label { get; } = label;

    /// <summary>Gets the availability/behavior hint (may be empty).</summary>
    public string Hint { get; } = hint;
}
/// <summary>One per-title override row in the settings list.</summary>
public sealed class TitleOverrideItem : ObservableObject
{
    private static readonly KeyedOption[] RendererChoices =
    [
        new(string.Empty, "inherit"),
        new(EmulatorSettings.RendererSoftware, "software"),
        new(EmulatorSettings.RendererOpenGl, "OpenGL"),
    ];

    private bool _fixedFunctionVertex;
    private bool _texFilterRegisters;
    private bool _inheritTextureFilter;
    private KeyedOption _selectedRenderer;

    /// <summary>Creates a row for a launched image path or an XBE title id.</summary>
    /// <param name="key">Override dictionary key (image path or <c>title:XXXXXXXX</c>).</param>
    /// <param name="value">Persisted override values.</param>
    /// <param name="remove">Removal callback.</param>
    /// <param name="titleName">Certificate title name when the key is a title id.</param>
    public TitleOverrideItem(string key, TitleOverride value, Action<TitleOverrideItem> remove, string? titleName = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(remove);
        Path = key;
        TitleName = titleName ?? string.Empty;
        _fixedFunctionVertex = value.FixedFunctionVertex == true;
        _texFilterRegisters = string.Equals(value.TextureFilter, "registers", StringComparison.Ordinal);
        _inheritTextureFilter = string.IsNullOrEmpty(value.TextureFilter);
        _selectedRenderer = RendererChoices.FirstOrDefault(
            option => option.Key.Length > 0 && string.Equals(option.Key, value.Renderer, StringComparison.Ordinal))
            ?? RendererChoices[0];
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

    /// <summary>Gets or sets a value indicating whether the title runs the fixed-function vertex stage.</summary>
    public bool FixedFunctionVertex
    {
        get => _fixedFunctionVertex;
        set => SetProperty(ref _fixedFunctionVertex, value);
    }

    /// <summary>Gets or sets a value indicating whether the title maps TEXFILTER onto the tent sampler.</summary>
    public bool TexFilterRegisters
    {
        get => _texFilterRegisters;
        set => SetProperty(ref _texFilterRegisters, value);
    }

    /// <summary>Gets or sets a value indicating whether the override inherits the global texture filter.</summary>
    public bool InheritTextureFilter
    {
        get => _inheritTextureFilter;
        set => SetProperty(ref _inheritTextureFilter, value);
    }

    /// <summary>Gets the renderer choices this row can pick from ("inherit", "software", "OpenGL").</summary>
    public IReadOnlyList<KeyedOption> Renderers { get; } = RendererChoices;

    /// <summary>Gets or sets the renderer this title uses (the "inherit" row keeps the global one).</summary>
    public KeyedOption SelectedRenderer
    {
        get => _selectedRenderer;
        set => SetProperty(ref _selectedRenderer, value);
    }

    /// <summary>Projects the row back into the persisted model.</summary>
    public TitleOverride ToOverride()
    {
        return new TitleOverride
        {
            FixedFunctionVertex = _fixedFunctionVertex,
            TextureFilter = _inheritTextureFilter ? null : (_texFilterRegisters ? "registers" : "legacy"),
            Renderer = _selectedRenderer.Key.Length == 0 ? null : _selectedRenderer.Key,
        };
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

/// <summary>Settings page: firmware, storage, performance, network, appearance, and library.</summary>
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
    private bool _fastMode = true;

    [ObservableProperty]
    private bool _apuDsp = true;

    [ObservableProperty]
    private bool _liveAudio = true;

    [ObservableProperty]
    private bool _rawBlocks = true;

    [ObservableProperty]
    private bool _spinSkip = true;

    [ObservableProperty]
    private bool _decodeCache = true;

    [ObservableProperty]
    private decimal _jitThreshold = 8;

    [ObservableProperty]
    private bool _fixedFunctionVertex;

    [ObservableProperty]
    private KeyedOption? _selectedTextureFilter;

    [ObservableProperty]
    private KeyedOption? _selectedRenderer;

    [ObservableProperty]
    private bool _displaySink;

    [ObservableProperty]
    private KeyedOption? _selectedFilter;

    [ObservableProperty]
    private KeyedOption? _selectedCore;

    [ObservableProperty]
    private string _xemuCorePath = string.Empty;

    [ObservableProperty]
    private bool _eosQuirk = true;

    [ObservableProperty]
    private bool _networkLinkUp = true;

    [ObservableProperty]
    private KeyedOption? _selectedNetwork;

    [ObservableProperty]
    private AccentOption? _selectedAccent;

    /// <summary>Creates the settings page with a draft loaded from the shell configuration.</summary>
    public SettingsViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        NetworkOptions =
        [
            new KeyedOption("loopback", "Loopback (offline)"),
            new KeyedOption("socket", "User-mode sockets"),
        ];
        TextureFilterOptions =
        [
            new KeyedOption("legacy", "Legacy (nearest)"),
            new KeyedOption("registers", "TEXFILTER (tent, LOD 0)"),
        ];
        RendererOptions =
        [
            new KeyedOption(EmulatorSettings.RendererSoftware, "Software rasterizer", "portable"),
            new KeyedOption(EmulatorSettings.RendererOpenGl, "OpenGL backend", OperatingSystem.IsWindows() ? "Windows" : "Windows only"),
        ];
        FilterOptions = [.. FrameFilters.All.Select(item => new KeyedOption(item.Key, item.Label))];
        CoreOptions =
        [
            new KeyedOption(EmulatorSettings.CoreManaged, "Managed C# core", "oracle, slower"),
            new KeyedOption(EmulatorSettings.CoreXemu, "xemu 0.8.139 core", "fastest"),
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

    /// <summary>Gets the selectable network backends.</summary>
    public ObservableCollection<KeyedOption> NetworkOptions { get; }

    /// <summary>Gets the selectable presentation filters.</summary>
    public IReadOnlyList<KeyedOption> FilterOptions { get; }

    /// <summary>Gets the selectable emulation cores.</summary>
    public IReadOnlyList<KeyedOption> CoreOptions { get; }

    /// <summary>Gets the selectable texture filter modes.</summary>
    public ObservableCollection<KeyedOption> TextureFilterOptions { get; }

    /// <summary>Gets the selectable NV2A renderers.</summary>
    public ObservableCollection<KeyedOption> RendererOptions { get; }

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

    /// <summary>Gets the persisted EEPROM state path.</summary>
    public string EepromStatePath => new EepromStateStore(SettingsFolder).FilePath;

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
        FastMode = settings.FastMode;
        ApuDsp = settings.ApuDsp;
        LiveAudio = !string.Equals(settings.AudioOutput, EmulatorSettings.AudioOutputOff, StringComparison.Ordinal);
        RawBlocks = settings.RawBlocks;
        SpinSkip = settings.SpinSkip;
        DecodeCache = settings.DecodeCache;
        JitThreshold = settings.JitThreshold;
        FixedFunctionVertex = settings.FixedFunctionVertex;
        SelectedTextureFilter = TextureFilterOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.TextureFilter, StringComparison.Ordinal))
            ?? TextureFilterOptions[0];
        SelectedRenderer = RendererOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Renderer, StringComparison.Ordinal))
            ?? RendererOptions[0];
        SelectedCore = CoreOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Core, StringComparison.Ordinal))
            ?? CoreOptions[0];
        XemuCorePath = settings.XemuCorePath;
        DisplaySink = settings.DisplaySink;
        SelectedFilter = FilterOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.Filter, StringComparison.Ordinal))
            ?? FilterOptions[0];
        TitleOverrides.Clear();
        foreach (KeyValuePair<string, TitleOverride> pair in settings.TitleOverrides)
        {
            TitleOverrides.Add(new TitleOverrideItem(pair.Key, pair.Value, RemoveTitleOverride));
        }

        EosQuirk = settings.EosQuirk;
        NetworkLinkUp = settings.NetworkLinkUp;
        SelectedNetwork = NetworkOptions.FirstOrDefault(
            option => string.Equals(option.Key, settings.NetworkBackend, StringComparison.Ordinal))
            ?? NetworkOptions[0];

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
            FastMode = FastMode,
            ApuDsp = ApuDsp,
            AudioOutput = LiveAudio ? EmulatorSettings.AudioOutputLive : EmulatorSettings.AudioOutputOff,
            RawBlocks = RawBlocks,
            SpinSkip = SpinSkip,
            DecodeCache = DecodeCache,
            JitThreshold = (int)JitThreshold,
            FixedFunctionVertex = FixedFunctionVertex,
            TextureFilter = SelectedTextureFilter?.Key ?? "legacy",
            Renderer = SelectedRenderer?.Key ?? EmulatorSettings.RendererSoftware,
            Core = SelectedCore?.Key ?? EmulatorSettings.CoreManaged,
            XemuCorePath = XemuCorePath.Trim(),
            DisplaySink = DisplaySink,
            Filter = SelectedFilter?.Key ?? FrameFilters.Nearest,
            EosQuirk = EosQuirk,
            NetworkBackend = SelectedNetwork?.Key ?? "loopback",
            NetworkLinkUp = NetworkLinkUp,
            Accent = SelectedAccent?.Key ?? "Emerald",
            LibraryFolders = [.. LibraryFolders.Select(item => item.Path)],
            RecentGames = [.. Shell.Config.RecentGames],
        };
        foreach (TitleOverrideItem item in TitleOverrides)
        {
            next.TitleOverrides[EmulatorSettings.CanonicalTitleKey(item.Path)] = item.ToOverride();
        }

        Shell.SaveSettings(next);
        Shell.Home.NotifySettingsChanged();
    }

    /// <summary>Public command: restores the recommended performance options without saving.</summary>
    [RelayCommand]
    private void ResetPerformance()
    {
        FastMode = true;
        RawBlocks = true;
        SpinSkip = true;
        DecodeCache = true;
        JitThreshold = 8;
        Shell.ShowToast("Recommended performance options restored — save to apply.");
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

        // V2: key the override by the XBE title id when it can be read, so it
        // follows the title across renamed, moved or re-wrapped images. An
        // unreadable image (e.g. a CHD, which has no XDVDFS reader yet) keeps
        // the V1 path key.
        if (EmulatorSession.TryResolveTitle(path, out uint titleId, out string resolvedName))
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
            new TitleOverride { FixedFunctionVertex = FixedFunctionVertex },
            RemoveTitleOverride,
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

    /// <summary>Public command: clears the persisted EEPROM state so the next boot re-seeds from the configured image.</summary>
    [RelayCommand]
    private void ResetEepromState()
    {
        var store = new EepromStateStore(SettingsFolder);
        Shell.ShowToast(store.TryReset()
            ? "EEPROM state cleared — the next boot reseeds from the configured image."
            : "Could not clear the EEPROM state file.");
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
