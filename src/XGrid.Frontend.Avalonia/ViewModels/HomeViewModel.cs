using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Home page: console status, live display actions, and the transport strip.</summary>
public sealed partial class HomeViewModel : PageViewModel
{
    /// <summary>Creates the home page.</summary>
    public HomeViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        shell.PropertyChanged += OnShellPropertyChanged;
    }

    /// <summary>Gets a value indicating whether the firmware warning is shown.</summary>
    public bool ShowSetupWarning => Shell.NeedsSetup;

    /// <summary>Public command: powers the console on or off.</summary>
    [RelayCommand]
    private void TogglePower()
    {
        Shell.TogglePowerCommand.Execute(null);
    }

    /// <summary>Public command: stops the running console.</summary>
    [RelayCommand]
    private void Stop()
    {
        Shell.StopSessionCommand.Execute(null);
    }

    /// <summary>Public command: picks and launches a disc image.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ChooseDiscAsync()
    {
        await Shell.OpenDiscCommand.ExecuteAsync(null);
    }

    /// <summary>Public command: navigates to firmware settings.</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        Shell.NavigateTo(PageKind.Settings);
    }

    /// <summary>Refreshes computed setup flags after settings change.</summary>
    public void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(ShowSetupWarning));
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.NeedsSetup))
        {
            OnPropertyChanged(nameof(ShowSetupWarning));
        }
    }
}
