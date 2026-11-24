using System;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>A recently played game with a launch command bound to the library.</summary>
public sealed class RecentGameItem
{
    /// <summary>Creates a recent-game card.</summary>
    /// <param name="game">Persisted recent entry.</param>
    /// <param name="launch">Launch callback.</param>
    /// <param name="nowUtc">Current UTC time used for the relative label.</param>
    public RecentGameItem(RecentGame game, Action<RecentGame> launch, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(launch);
        Game = game;
        LastPlayedText = DescribeLastPlayed(game.LastPlayedUtc, nowUtc);
        LaunchCommand = new RelayCommand(() => launch(Game));
    }

    /// <summary>Gets the underlying entry.</summary>
    private RecentGame Game { get; }

    /// <summary>Gets the launch command.</summary>
    public IRelayCommand LaunchCommand { get; }

    /// <summary>Gets the display title.</summary>
    public string Title => Game.Title;

    /// <summary>Gets the full image path (tooltip).</summary>
    public string Path => Game.Path;

    /// <summary>Gets the relative or absolute last-played label.</summary>
    public string LastPlayedText { get; }

    /// <summary>Formats a last-played timestamp for the recent list.</summary>
    /// <param name="lastPlayedUtc">UTC launch time.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns>A short relative label, or a date after a week.</returns>
    internal static string DescribeLastPlayed(DateTime lastPlayedUtc, DateTime nowUtc)
    {
        TimeSpan age = nowUtc - lastPlayedUtc;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Math.Max(1, (int)age.TotalMinutes).ToString(CultureInfo.CurrentCulture) + " min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return Math.Max(1, (int)age.TotalHours).ToString(CultureInfo.CurrentCulture) + " h ago";
        }

        if (age < TimeSpan.FromDays(7))
        {
            return Math.Max(1, (int)age.TotalDays).ToString(CultureInfo.CurrentCulture) + " d ago";
        }

        return lastPlayedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
