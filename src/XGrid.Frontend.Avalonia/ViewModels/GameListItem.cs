using System;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>A game row/card with a launch command bound to the shell.</summary>
public sealed class GameListItem
{
    /// <summary>Creates a list item for a library entry.</summary>
    public GameListItem(GameEntry game, Action<GameEntry> launch)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(launch);
        Game = game;
        LaunchCommand = new RelayCommand(() => launch(Game));
    }

    /// <summary>Gets the underlying entry.</summary>
    public GameEntry Game { get; }

    /// <summary>Gets the launch command.</summary>
    public IRelayCommand LaunchCommand { get; }

    /// <summary>Gets the display title.</summary>
    public string Title => Game.Title;

    /// <summary>Gets the format label.</summary>
    public string FormatText => Game.FormatText;

    /// <summary>Gets the formatted size.</summary>
    public string SizeText => Game.SizeText;

    /// <summary>Gets the containing folder.</summary>
    public string Folder => Game.Folder;

    /// <summary>Gets a value indicating whether the disc can be launched.</summary>
    public bool IsLaunchable => Game.IsLaunchable;

    /// <summary>Gets a value indicating whether the entry is a loose executable.</summary>
    public bool IsExecutable => Game.Format == GameFormat.Xbe;
}
