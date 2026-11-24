using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>
/// The first-run wizard: point XGrid at the firmware and hard-disk images it
/// does not ship, optionally add a game folder, then hand the choices to the
/// shell. Closing the window without finishing saves the current state so the
/// wizard does not appear on every launch.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly FileDialogService _dialogs;

    /// <summary>Creates the wizard for one shell window.</summary>
    /// <param name="shell">The shell that persists the choices.</param>
    /// <param name="owner">The wizard window, used as the picker owner.</param>
    public FirstRunViewModel(MainWindowViewModel shell, Window owner)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(owner);
        _shell = shell;
        _dialogs = new FileDialogService { Window = owner };
        _biosPath = shell.Config.BiosPath;
        _eepromPath = shell.Config.EepromPath;
        _hddPath = shell.Config.HddPath;
        _libraryFolder = shell.Config.LibraryFolders.Count > 0 ? shell.Config.LibraryFolders[0] : string.Empty;
    }

    [ObservableProperty]
    private string _biosPath;

    [ObservableProperty]
    private string _eepromPath;

    [ObservableProperty]
    private string _hddPath;

    [ObservableProperty]
    private string _libraryFolder;

    [ObservableProperty]
    private string _problem = string.Empty;

    /// <summary>Gets a value indicating whether a problem message should be shown.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Gets a value indicating whether a BIOS image was chosen.</summary>
    public bool HasBios => !string.IsNullOrWhiteSpace(BiosPath);

    /// <summary>Raised when the wizard should close.</summary>
    public event EventHandler? Completed;

    /// <summary>Public command: chooses the BIOS image.</summary>
    [RelayCommand]
    private async Task BrowseBiosAsync()
    {
        string? path = await _dialogs.PickFileAsync(
            "Choose your Xbox BIOS image", FileDialogService.FirmwareTypes);
        if (!string.IsNullOrWhiteSpace(path))
        {
            BiosPath = path;
        }
    }

    /// <summary>Public command: chooses the EEPROM image.</summary>
    [RelayCommand]
    private async Task BrowseEepromAsync()
    {
        string? path = await _dialogs.PickFileAsync(
            "Choose your Xbox EEPROM image", FileDialogService.FirmwareTypes);
        if (!string.IsNullOrWhiteSpace(path))
        {
            EepromPath = path;
        }
    }

    /// <summary>Public command: chooses the hard-disk image.</summary>
    [RelayCommand]
    private async Task BrowseHddAsync()
    {
        string? path = await _dialogs.PickFileAsync(
            "Choose your Xbox hard-disk image", FileDialogService.DiskTypes);
        if (!string.IsNullOrWhiteSpace(path))
        {
            HddPath = path;
        }
    }

    /// <summary>Public command: chooses a game folder.</summary>
    [RelayCommand]
    private async Task BrowseLibraryAsync()
    {
        string? path = await _dialogs.PickFolderAsync("Choose a folder containing Xbox games");
        if (!string.IsNullOrWhiteSpace(path))
        {
            LibraryFolder = path;
        }
    }

    /// <summary>Public command: validates the choices and finishes setup.</summary>
    [RelayCommand]
    private void Finish()
    {
        string? problem = FirstRunSetup.Validate(BiosPath, EepromPath, HddPath);
        if (problem is not null)
        {
            Problem = problem;
            return;
        }

        Complete();
    }

    /// <summary>Public command: finishes setup with what is already configured.</summary>
    [RelayCommand]
    private void SetUpLater()
    {
        Complete();
    }

    private void Complete()
    {
        _shell.CompleteFirstRun(BiosPath, EepromPath, HddPath, LibraryFolder);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnBiosPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasBios));
        ClearProblem();
    }

    partial void OnEepromPathChanged(string value)
    {
        ClearProblem();
    }

    partial void OnHddPathChanged(string value)
    {
        ClearProblem();
    }

    partial void OnProblemChanged(string value)
    {
        OnPropertyChanged(nameof(HasProblem));
    }

    private void ClearProblem()
    {
        if (Problem.Length > 0)
        {
            Problem = string.Empty;
        }
    }
}
