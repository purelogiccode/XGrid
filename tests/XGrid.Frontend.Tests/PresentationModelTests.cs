using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media;
using XGrid.Diagnostics;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins small presentation models that do not need a window.</summary>
public sealed class PresentationModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "xgrid-models", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LogEntry_FormatsTheClockAndSeverity()
    {
        var timestamp = new DateTimeOffset(2026, 11, 24, 15, 4, 5, TimeSpan.FromHours(-3));

        Assert.Equal("INFO", new LogEntry(timestamp, LogLevel.Info, "a").LevelText);
        Assert.Equal("WARN", new LogEntry(timestamp, LogLevel.Warning, "a").LevelText);
        Assert.Equal("ERROR", new LogEntry(timestamp, LogLevel.Error, "a").LevelText);
        Assert.Equal("DEBUG", new LogEntry(timestamp, LogLevel.Debug, "a").LevelText);
        Assert.Equal(
            timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            new LogEntry(timestamp, LogLevel.Info, "a").TimeText);
        Assert.Equal("message", new LogEntry(timestamp, LogLevel.Info, "message").Message);
    }

    [Fact]
    public void UpdatePrompt_BuildsTheVersionLinesAndFallbackText()
    {
        var update = new GitHubUpdateInfo
        {
            CurrentVersion = "v1.2.3",
            LatestVersion = "v1.4.0",
        };

        var model = new UpdatePromptViewModel(update);

        Assert.Equal("You are running v1.2.3.", model.CurrentVersion);
        Assert.Equal("The latest release is v1.4.0.", model.LatestVersion);
        Assert.Equal("Open the project releases page to download the new build.", model.ReleaseName);
        Assert.Contains("new version", model.Heading, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatePrompt_UsesTheReleaseNameWhenPresent()
    {
        var model = new UpdatePromptViewModel(new GitHubUpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "1.1.0",
            ReleaseName = "Faster rendering",
        });

        Assert.Equal("Faster rendering", model.ReleaseName);
    }

    [Fact]
    public void UpdatePrompt_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdatePromptViewModel(null!));
    }

    [Fact]
    public void GameListItem_ExposesTheEntryAndLaunchesThroughTheCommand()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "my_game.iso");
        File.WriteAllBytes(path, [0]);
        GameEntry game = Assert.IsType<GameEntry>(GameEntry.FromFile(path));
        GameEntry? launched = null;
        using var covers = new CoverArtCache();
        var item = new GameListItem(game, entry => launched = entry, _ => { }, covers);

        Assert.Equal("my game", item.Title);
        Assert.Equal("ISO", item.FormatText);
        Assert.True(item.IsLaunchable);
        Assert.False(item.IsExecutable);
        Assert.False(item.HasCover);

        item.LaunchCommand.Execute(null);

        Assert.Same(game, launched);
    }

    [Fact]
    public void GameListItem_RejectsMissingArguments()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "game.iso");
        File.WriteAllBytes(path, [0]);
        GameEntry game = Assert.IsType<GameEntry>(GameEntry.FromFile(path));

        using var covers = new CoverArtCache();
        Assert.Throws<ArgumentNullException>(() => new GameListItem(null!, _ => { }, _ => { }, covers));
        Assert.Throws<ArgumentNullException>(() => new GameListItem(game, null!, _ => { }, covers));
        Assert.Throws<ArgumentNullException>(() => new GameListItem(game, _ => { }, null!, covers));
        Assert.Throws<ArgumentNullException>(() => new GameListItem(game, _ => { }, _ => { }, null!));
    }

    [Fact]
    public void AccentPalette_FindsByKeyCaseInsensitivelyAndFallsBackToBlue()
    {
        Assert.Equal("Emerald", AccentPalette.Find("emerald").Label);
        Assert.Equal("Blue", AccentPalette.Find(null).Label);
        Assert.Equal("Blue", AccentPalette.Find("chartreuse").Label);
        Assert.Same(AccentPalette.All[0], AccentPalette.Find("unknown"));
    }

    [Fact]
    public void AccentPalette_ExposesUniqueKeysAndDerivedShades()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AccentPalette palette in AccentPalette.All)
        {
            Assert.True(keys.Add(palette.Key), "duplicate palette key: " + palette.Key);
            Assert.False(string.IsNullOrWhiteSpace(palette.Label));
            Assert.True(palette.Hover.R > palette.Color.R || palette.Hover.G > palette.Color.G);
            Assert.Equal(38, palette.Soft.A);
            Assert.IsType<SolidColorBrush>(palette.Brush);
        }
    }

    [Fact]
    public void DiagnosticsOptions_ExposeTheProductUrls()
    {
        var options = new DiagnosticsOptions
        {
            RepositoryOwner = "owner",
            RepositoryName = "repo",
        };

        Assert.Equal("https://github.com/owner/repo", options.RepositoryUrl.ToString());
        Assert.Equal(
            "https://api.github.com/repos/owner/repo/releases/latest",
            options.UpdateApiUrl.ToString());
        Assert.Equal(
            "https://api.github.com/repos/owner/repo/tags",
            options.TagsApiUrl.ToString());
        Assert.False(string.IsNullOrWhiteSpace(DiagnosticsOptions.DefaultLogDirectory));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
