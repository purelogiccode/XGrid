using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the recently played list, its persistence rules and labels.</summary>
public sealed class RecentGamesTests
{
    [Fact]
    public void Record_PutsTheNewestLaunchFirst()
    {
        var entries = new List<RecentGame>();

        RecentGame.Record(entries, @"C:\Games\Halo.iso", "Halo", new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc));
        RecentGame.Record(entries, @"C:\Games\Fable.iso", "Fable", new DateTime(2026, 11, 24, 11, 0, 0, DateTimeKind.Utc));

        Assert.Equal("Fable|Halo", string.Join("|", entries.ConvertAll<string>(static entry => entry.Title)));
        Assert.Equal(new DateTime(2026, 11, 24, 11, 0, 0, DateTimeKind.Utc), entries[0].LastPlayedUtc);
    }

    [Fact]
    public void Record_MovesAnExistingPathAndIgnoresCase()
    {
        var entries = new List<RecentGame>
        {
            new() { Path = @"C:\Games\Halo.iso", Title = "Halo", LastPlayedUtc = DateTime.UtcNow },
            new() { Path = @"C:\Games\Fable.iso", Title = "Fable", LastPlayedUtc = DateTime.UtcNow },
        };

        RecentGame.Record(entries, @"c:\games\HALO.iso", "Halo", DateTime.UtcNow);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Halo", entries[0].Title);
        Assert.Equal(1, entries.Count(static entry => string.Equals(entry.Title, "Halo", StringComparison.Ordinal)));
    }

    [Fact]
    public void Record_KeepsOnlyTheConfiguredNumber()
    {
        var entries = new List<RecentGame>();

        for (int index = 0; index < RecentGame.MaxEntries + 5; index++)
        {
            RecentGame.Record(entries, $@"C:\Games\Game{index}.iso", $"Game {index}", DateTime.UtcNow);
        }

        Assert.Equal(RecentGame.MaxEntries, entries.Count);
        Assert.Equal("Game 14", entries[0].Title);
    }

    [Fact]
    public void Record_IgnoresABlankPath()
    {
        var entries = new List<RecentGame>();

        RecentGame.Record(entries, " ", "Nothing", DateTime.UtcNow);

        Assert.Empty(entries);
    }

    [Fact]
    public void Normalize_SortsDropsAndFillsTitles()
    {
        string oldPath = Path.Combine("Games", "Old.iso");
        string newPath = Path.Combine("Games", "New.iso");
        var settings = new EmulatorSettings
        {
            RecentGames = new List<RecentGame>
            {
                new RecentGame { Path = oldPath, Title = "Old", LastPlayedUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
                new RecentGame { Path = " ", Title = "Broken", LastPlayedUtc = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc) },
                new RecentGame { Path = newPath, Title = " ", LastPlayedUtc = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc) },
                new RecentGame { Path = oldPath, Title = "Old", LastPlayedUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) },
            },
        };

        settings.Normalize();

        Assert.Equal("New|Old", string.Join("|", settings.RecentGames.ConvertAll<string>(static entry => entry.Title)));
        Assert.Equal(newPath, settings.RecentGames[0].Path);
    }

    [Fact]
    public void Normalize_ToleratesANullRecentList()
    {
        var settings = new EmulatorSettings
        {
            RecentGames = null!,
        };

        settings.Normalize();

        Assert.Empty(settings.RecentGames);
    }

    [Fact]
    public void CloneCopiesRecentEntriesIndependently()
    {
        var settings = new EmulatorSettings
        {
            RecentGames = new List<RecentGame>
            {
                new RecentGame { Path = @"C:\Games\Halo.iso", Title = "Halo", LastPlayedUtc = DateTime.UtcNow },
            },
        };

        EmulatorSettings copy = settings.Clone();
        copy.RecentGames[0].Title = "Changed";

        Assert.Equal("Halo", settings.RecentGames[0].Title);
        Assert.NotSame(settings.RecentGames[0], copy.RecentGames[0]);
    }

    [Fact]
    public void SettingsPageCopy_PreservesRecentGames()
    {
        var live = new EmulatorSettings
        {
            RecentGames = new List<RecentGame>
            {
                new RecentGame { Path = @"C:\Games\Halo.iso", Title = "Halo", LastPlayedUtc = DateTime.UtcNow },
            },
        };
        var next = new EmulatorSettings();

        SettingsViewModel.CopyRecentGames(next, live);

        Assert.Single(next.RecentGames);
        Assert.Equal("Halo", next.RecentGames[0].Title);
        Assert.NotSame(live.RecentGames[0], next.RecentGames[0]);
    }

    [Fact]
    public void SettingsPageCopy_RejectsMissingSettings()
    {
        var settings = new EmulatorSettings();

        Assert.Throws<ArgumentNullException>(() => SettingsViewModel.CopyRecentGames(null!, settings));
        Assert.Throws<ArgumentNullException>(() => SettingsViewModel.CopyRecentGames(settings, null!));
    }

    [Fact]
    public void CloneKeepsTheFpsOverlaySetting()
    {
        var settings = new EmulatorSettings { ShowFpsOverlay = true };

        Assert.True(settings.Clone().ShowFpsOverlay);
    }

    [Fact]
    public void DescribeLastPlayed_UsesRelativeLabels()
    {
        DateTime now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Just now", RecentGameItem.DescribeLastPlayed(now, now));
        Assert.Equal("5 min ago", RecentGameItem.DescribeLastPlayed(now.AddMinutes(-5), now));
        Assert.Equal("3 h ago", RecentGameItem.DescribeLastPlayed(now.AddHours(-3), now));
        Assert.Equal("2 d ago", RecentGameItem.DescribeLastPlayed(now.AddDays(-2), now));
    }

    [Fact]
    public void DescribeLastPlayed_ClampsFutureTimestamps()
    {
        DateTime now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Just now", RecentGameItem.DescribeLastPlayed(now.AddMinutes(5), now));
    }
}
