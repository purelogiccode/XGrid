using System;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins local cover resolution: only the user's covers folder is consulted,
/// first by title id and then by an annotation-insensitive title match. Images
/// beside the disc must not be picked up.
/// </summary>
public sealed class CoverArtIndexTests : IDisposable
{
    private readonly string _root;
    private readonly string _covers;

    public CoverArtIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-covers-" + Guid.NewGuid().ToString("N"));
        _covers = Path.Combine(_root, "covers");
        _ = Directory.CreateDirectory(_covers);
    }

    [Fact]
    public void Find_IgnoresImagesBesideTheDisc()
    {
        string game = Touch(Path.Combine(_root, "Halo.iso"));
        _ = Touch(Path.Combine(_root, "Halo.png"));
        _ = Touch(Path.Combine(_root, "Halo.cover.jpg"));

        Assert.Null(CoverArtIndex.Create(null).Find(game, "Halo", 0));
        Assert.Null(CoverArtIndex.Create(_covers).Find(game, "Halo", 0));
    }

    [Fact]
    public void Find_MatchesTheTitleIdInTheCoversFolder()
    {
        string game = Touch(Path.Combine(_root, "Halo.iso"));
        string cover = Touch(Path.Combine(_covers, "4D530002.png"));
        _ = Touch(Path.Combine(_covers, "Halo.jpg"));

        Assert.Equal(cover, CoverArtIndex.Create(_covers).Find(game, "Halo", 0x4D530002u));
    }

    [Fact]
    public void Find_StripsAnnotationsFromTheTitle()
    {
        string game = Touch(Path.Combine(_root, "Halo.iso"));
        string cover = Touch(Path.Combine(_covers, "Halo - Combat Evolved (USA) [!].jpg"));

        Assert.Equal(
            cover,
            CoverArtIndex.Create(_covers).Find(game, "Halo - Combat Evolved", 0));
    }

    [Fact]
    public void Find_ReadsAmpersandAsAnd()
    {
        string game = Touch(Path.Combine(_root, "Tom.iso"));
        string cover = Touch(Path.Combine(_covers, "Tom and Jerry.png"));

        Assert.Equal(cover, CoverArtIndex.Create(_covers).Find(game, "Tom & Jerry", 0));
    }

    [Fact]
    public void Find_ReturnsNullWhenNothingMatches()
    {
        string game = Touch(Path.Combine(_root, "Halo.iso"));
        _ = Touch(Path.Combine(_covers, "Halo 2.png"));

        Assert.Null(CoverArtIndex.Create(_covers).Find(game, "Halo", 0));
    }

    [Theory]
    [InlineData("Halo (USA) [!]", "halo")]
    [InlineData("Tom & Jerry", "tomandjerry")]
    [InlineData("Halo 2 [Disc 1]", "halo2")]
    [InlineData("  ...  ", "")]
    public void Normalize_KeepsOnlyTheComparableTitle(string value, string expected)
    {
        Assert.Equal(expected, CoverArtIndex.Normalize(value));
    }

    [Fact]
    public void GameEntry_AttachesACoverFromTheCoversFolder()
    {
        string game = Touch(Path.Combine(_root, "My Game.iso"));
        string cover = Touch(Path.Combine(_covers, "My Game.png"));

        GameEntry? entry = GameEntry.FromFile(game, CoverArtIndex.Create(_covers));

        Assert.NotNull(entry);
        Assert.Equal(cover, entry.CoverPath);
    }

    [Fact]
    public void GameEntry_DoesNotUseAnImageBesideTheDisc()
    {
        string game = Touch(Path.Combine(_root, "My Game.iso"));
        _ = Touch(Path.Combine(_root, "My Game.cover.jpg"));

        GameEntry? entry = GameEntry.FromFile(game);

        Assert.NotNull(entry);
        Assert.Null(entry.CoverPath);
    }

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

    private static string Touch(string path)
    {
        File.WriteAllBytes(path, []);
        return path;
    }
}
