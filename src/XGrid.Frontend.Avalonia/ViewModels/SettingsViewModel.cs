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
        _selectedRam = RamChoices.FirstOrDefault(
            option => option.Key.Length > 0
                && int.TryParse(option.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ram)
                && ram == value.RamMb)
            ?? RamChoices[0];
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

    /// <summary>Gets or sets the guest memory this title uses (the "inherit" row keeps the global one).</summary>
    public KeyedOption SelectedRam
    {
        get => _selectedRam;
        set => SetProperty(ref _selectedRam, value);
    }

    /// <summary>Projects the row back into the persisted model.</summary>
    public TitleOverride ToOverride()
    {
        return new TitleOverride
        {
            RamMb = int.TryParse(_selectedRam.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ram)
                ? ram
                : null,
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
    private KeyedOption? _selectedFilter;

    [ObservableProperty]
    private string _xemuCorePath = string.Empty;

    [ObservableProperty]
    private AccentOption? _selectedAccent;

    /// <summary>Creates the settings page with a draft loaded from the shell configuration.</summary>
    public SettingsViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        FilterOptions = [.. FrameFilters.All.Select(item => new KeyedOption(item.Key, item.Label))];
        foreach (AccentPalette palette in AccentPalette.All)
        {
            AccentOptions.Add(new AccentOption(palette));
        }

        LoadFrom(shell.Config);
        TitleOverrides.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTitleOverrides));
    }

    /// <summary>Gets the guest RAM choices.</summary>
    public IReadOnlyList<int> RamOptions { get; } = [64, 128];

    /// <summary>Gets the selectable presentation filters.</summary>
    public IReadOnlyList<KeyedOption> FilterOptions { get; }

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
            XemuCorePath = XemuCorePath.Trim(),
            DisplaySink = DisplaySink,
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
