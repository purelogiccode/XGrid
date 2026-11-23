using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Home page: console status, live display actions, and recent games.</summary>
public sealed partial class HomeViewModel : PageViewModel
{
    /// <summary>Creates the home page.</summary>
    public HomeViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        shell.RecentGames.CollectionChanged += OnRecentGamesChanged;
        shell.PropertyChanged += OnShellPropertyChanged;
        RebuildRecent();
    }

    /// <summary>Gets recent games with per-row launch commands.</summary>
    public ObservableCollection<GameListItem> Recent { get; } = [];

    /// <summary>Gets a value indicating whether any recent games exist.</summary>
    public bool HasRecent => Recent.Count > 0;

    /// <summary>Gets a value indicating whether the recent list is empty.</summary>
    public bool NoRecent => Recent.Count == 0;

    /// <summary>Gets a value indicating whether the firmware warning is shown.</summary>
    public bool ShowSetupWarning => Shell.NeedsSetup;

    /// <summary>Gets a value indicating whether a disc is selected.</summary>
    public bool HasCurrentGame => Shell.CurrentGame is not null;

    /// <summary>Gets a value indicating whether no disc is selected.</summary>
    public bool NoCurrentGame => Shell.CurrentGame is null;

    /// <summary>Public command: powers the console on or off.</summary>
    [RelayCommand]
    private void TogglePower()
    {
        Shell.TogglePowerCommand.Execute(null);
    }

    /// <summary>Public command: pauses or resumes the running console.</summary>
    [RelayCommand]
    private void TogglePause()
    {
        Shell.TogglePauseCommand.Execute(null);
    }

    /// <summary>Public command: stops the running console.</summary>
    [RelayCommand]
    private void Stop()
    {
        Shell.StopSessionCommand.Execute(null);
    }

    /// <summary>Public command: advances one frame while the console is paused.</summary>
    [RelayCommand]
    private void StepFrame()
    {
        Shell.StepFrameCommand.Execute(null);
    }

    /// <summary>Public command: advances a slice of guest time while paused.</summary>
    [RelayCommand]
    private void StepInstructions()
    {
        Shell.StepInstructionsCommand.Execute(null);
    }

    /// <summary>Public command: picks and launches a disc image.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ChooseDiscAsync()
    {
        await Shell.OpenDiscCommand.ExecuteAsync(null);
    }

    /// <summary>Public command: saves a snapshot of the running console.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task SaveStateAsync()
    {
        await Shell.SaveStateCommand.ExecuteAsync(null);
    }

    /// <summary>Public command: loads a snapshot into the running console.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task LoadStateAsync()
    {
        await Shell.LoadStateCommand.ExecuteAsync(null);
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
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentGame))
        {
            OnPropertyChanged(nameof(HasCurrentGame));
            OnPropertyChanged(nameof(NoCurrentGame));
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.NeedsSetup))
        {
            OnPropertyChanged(nameof(ShowSetupWarning));
        }
    }

    private void OnRecentGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRecent();
    }

    private void RebuildRecent()
    {
        Recent.Clear();
        foreach (GameEntry game in Shell.RecentGames)
        {
            Recent.Add(new GameListItem(game, entry => Shell.StartGameCommand.Execute(entry)));
        }

        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(NoRecent));
    }
}
