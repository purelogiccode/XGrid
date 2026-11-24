using System;
using System.Collections.Generic;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>A game the user launched from the shell, remembered across sessions.</summary>
public sealed class RecentGame
{
    /// <summary>The number of recent games kept in settings.</summary>
    public const int MaxEntries = 10;

    /// <summary>Gets or sets the absolute path of the launched image.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the title shown in the recent list.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time of the most recent launch.</summary>
    public DateTime LastPlayedUtc { get; set; }

    /// <summary>Creates an independent copy.</summary>
    /// <returns>A copy of this entry.</returns>
    public RecentGame Clone()
    {
        return new RecentGame
        {
            Path = Path,
            Title = Title,
            LastPlayedUtc = LastPlayedUtc,
        };
    }

    /// <summary>
    /// Moves a launch to the front of the list, removes an older entry for the
    /// same path and keeps at most <see cref="MaxEntries"/> rows.
    /// </summary>
    /// <param name="entries">Recent list to update.</param>
    /// <param name="path">Absolute path of the launched image.</param>
    /// <param name="title">Display title.</param>
    /// <param name="playedAtUtc">UTC launch time.</param>
    public static void Record(
        List<RecentGame> entries,
        string path,
        string title,
        DateTime playedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        entries.RemoveAll(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentGame
        {
            Path = path,
            Title = string.IsNullOrWhiteSpace(title) ? path : title,
            LastPlayedUtc = playedAtUtc,
        });
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }
    }
}
