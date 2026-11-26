using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins recursive library discovery, filtering and ordering.</summary>
public sealed class GameLibraryServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "xgrid-library-tests", Guid.NewGuid().ToString("N"));

    public GameLibraryServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("game.iso", true)]
    [InlineData("game.ISO", true)]
    [InlineData("game.xiso", true)]
    [InlineData("game.chd", true)]
    [InlineData("game.cci", true)]
    [InlineData("game.1.cci", true)]
    [InlineData("game.2.cci", false)]
    [InlineData("game.12.cci", false)]
    [InlineData("game.01.cci", true)]
    [InlineData("game.zar", true)]
    [InlineData("game.xbe", true)]
    [InlineData("game.zip", false)]
    [InlineData("game.txt", false)]
    [InlineData("game", false)]
    public void IsGameCandidate_UsesTheSupportedExtensionList(string name, bool expected)
    {
        Assert.Equal(expected, GameLibraryService.IsGameCandidate(name));
    }

    [Fact]
    public void Scan_FindsSupportedFilesRecursivelyAndPrettifiesTitles()
    {
        string games = Path.Combine(_root, "games");
        Directory.CreateDirectory(Path.Combine(games, "nested"));
        File.WriteAllBytes(Path.Combine(games, "halo_2.iso"), [0]);
        File.WriteAllBytes(Path.Combine(games, "nested", "panzer.drag.oon.chd"), [0]);
        File.WriteAllText(Path.Combine(games, "readme.txt"), "not a game");

        List<GameEntry> entries = GameLibraryService.Scan([games]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(["halo 2", "panzer drag oon"], entries.ConvertAll(entry => entry.Title));
        Assert.Equal(GameFormat.Iso, entries[0].Format);
        Assert.Equal(GameFormat.Chd, entries[1].Format);
    }

    [Fact]
    public void Scan_DeduplicatesTheSameImageReachedThroughOverlappingFolders()
    {
        File.WriteAllBytes(Path.Combine(_root, "game.iso"), [0]);

        List<GameEntry> entries = GameLibraryService.Scan([_root, Path.Combine(_root, ".")]);

        Assert.Single(entries);
    }

    [Fact]
    public void Scan_ListsASplitCciSetOnceUnderItsBaseName()
    {
        File.WriteAllBytes(Path.Combine(_root, "halo.1.cci"), [0]);
        File.WriteAllBytes(Path.Combine(_root, "halo.2.cci"), [0]);

        List<GameEntry> entries = GameLibraryService.Scan([_root]);

        GameEntry entry = Assert.Single(entries);
        Assert.Equal(GameFormat.Cci, entry.Format);
        Assert.Equal("halo", entry.Title);
    }

    [Fact]
    public void Scan_SkipsMissingBlankAndNullFolders()
    {
        List<GameEntry> entries = GameLibraryService.Scan(
            [null!, string.Empty, "   ", Path.Combine(_root, "missing")]);

        Assert.Empty(entries);
    }

    [Fact]
    public void Scan_SortsByTitleIgnoringCase()
    {
        File.WriteAllBytes(Path.Combine(_root, "zeta.iso"), [0]);
        File.WriteAllBytes(Path.Combine(_root, "Alpha.iso"), [0]);
        File.WriteAllBytes(Path.Combine(_root, "beta.iso"), [0]);

        List<GameEntry> entries = GameLibraryService.Scan([_root]);

        Assert.Equal(["Alpha", "beta", "zeta"], entries.ConvertAll(entry => entry.Title));
    }

    [Fact]
    public void Scan_WhenNoFolderExists_ReturnsAnEmptyList()
    {
        Assert.Empty(GameLibraryService.Scan([]));
    }

    [Fact]
    public void Scan_RejectsANullFolderSequence()
    {
        Assert.Throws<ArgumentNullException>(() => GameLibraryService.Scan(null!));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail a test run.
        }
    }
}
