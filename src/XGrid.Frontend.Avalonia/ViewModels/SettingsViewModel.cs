using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>One selectable NVNET backend.</summary>
public sealed class NetworkOption
{
    /// <summary>Creates a backend option.</summary>
    public NetworkOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Gets the persisted key.</summary>
    public string Key { get; }

    /// <summary>Gets the display label.</summary>
    public string Label { get; }
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
    private bool _rawBlocks = true;

    [ObservableProperty]
    private bool _spinSkip = true;

    [ObservableProperty]
    private bool _decodeCache = true;

    [ObservableProperty]
    private decimal _jitThreshold = 8;

    [ObservableProperty]
    private bool _eosQuirk = true;

    [ObservableProperty]
    private bool _networkLinkUp = true;

    [ObservableProperty]
    private NetworkOption? _selectedNetwork;

    [ObservableProperty]
    private AccentOption? _selectedAccent;

    /// <summary>Creates the settings page with a draft loaded from the shell configuration.</summary>
    public SettingsViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        NetworkOptions = new ObservableCollection<NetworkOption>
        {
            new("loopback", "Loopback (offline)"),
            new("socket", "User-mode sockets"),
        };
        foreach (AccentPalette palette in AccentPalette.All)
        {
            AccentOptions.Add(new AccentOption(palette));
        }

        LoadFrom(shell.Config);
    }

    /// <summary>Gets the guest RAM choices.</summary>
    public IReadOnlyList<int> RamOptions { get; } = new[] { 64, 128 };

    /// <summary>Gets the selectable network backends.</summary>
    public ObservableCollection<NetworkOption> NetworkOptions { get; }

    /// <summary>Gets the accent swatches.</summary>
    public ObservableCollection<AccentOption> AccentOptions { get; } = new();

    /// <summary>Gets the library folders being edited.</summary>
    public ObservableCollection<LibraryFolderItem> LibraryFolders { get; } = new();

    /// <summary>Gets the BIOS status label.</summary>
    public string BiosStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BiosPath))
            {
                return "No BIOS image selected — power-on is disabled.";
            }

            return File.Exists(BiosPath) ? "BIOS image found." : "BIOS file not found at this path.";
        }
    }

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
        FastMode = settings.FastMode;
        RawBlocks = settings.RawBlocks;
        SpinSkip = settings.SpinSkip;
        DecodeCache = settings.DecodeCache;
        JitThreshold = settings.JitThreshold;
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
            RawBlocks = RawBlocks,
            SpinSkip = SpinSkip,
            DecodeCache = DecodeCache,
            JitThreshold = (int)JitThreshold,
            EosQuirk = EosQuirk,
            NetworkBackend = SelectedNetwork?.Key ?? "loopback",
            NetworkLinkUp = NetworkLinkUp,
            Accent = SelectedAccent?.Key ?? "Emerald",
            LibraryFolders = LibraryFolders.Select(item => item.Path).ToList(),
            RecentGames = new List<string>(Shell.Config.RecentGames),
        };
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
            default:
                break;
        }
    }

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
