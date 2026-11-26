using System;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins local cover indexing, annotation handling and fuzzy selection.</summary>
public sealed class LocalCoverIndexTests : IDisposable
{
    private readonly string _root;
    private readonly string _covers;

    public LocalCoverIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-local-covers-" + Guid.NewGuid().ToString("N"));
        _covers = Path.Combine(_root, "covers");
        _ = Directory.CreateDirectory(_covers);
    }

    [Fact]
    public void Find_MatchesTheTitleIdFile()
    {
        string cover = Touch("4D530002.png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        CoverMatch? match = index.Find("Halo", "Halo", 0x4D530002u);

        Assert.NotNull(match);
        Assert.Equal(cover, match.Path);
        Assert.Equal("local-title-id", match.Source);
    }

    [Fact]
    public void Find_StripsAnnotationsFromTheCover()
    {
        string cover = Touch("Halo - Combat Evolved (USA) [!].png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        CoverMatch? match = index.Find("Halo - Combat Evolved", "Halo - Combat Evolved", 0);

        Assert.NotNull(match);
        Assert.Equal(cover, match.Path);
        Assert.Equal(1.0, match.Score);
    }

    [Fact]
    public void Find_ReadsAmpersandAsAnd()
    {
        string cover = Touch("Tom and Jerry.png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        CoverMatch? match = index.Find("Tom & Jerry", "Tom & Jerry", 0);

        Assert.NotNull(match);
        Assert.Equal(cover, match.Path);
    }

    [Fact]
    public void Find_UsesFuzzyMatchingForTypos()
    {
        string cover = Touch("Panzer Dragoon Orta (USA).png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        CoverMatch? match = index.Find("Panzer Dragoon Ort", null, 0);

        Assert.NotNull(match);
        Assert.Equal(cover, match.Path);
        Assert.Equal("local-fuzzy", match.Source);
    }

    [Fact]
    public void Find_PrefersTheRegionOnATie()
    {
        string usa = Touch("Halo 2 (USA).png");
        _ = Touch("Halo 2 (Europe).png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        CoverMatch? match = index.Find(
            "Halo 2",
            null,
            0,
            new CoverMatchOptions { Regions = "USA" });

        Assert.NotNull(match);
        Assert.Equal(usa, match.Path);
        Assert.Equal("USA", match.Regions);
    }

    [Fact]
    public void Find_RejectsASequelMismatch()
    {
        _ = Touch("Halo 2 (USA).png");
        LocalCoverIndex index = LocalCoverIndex.Create(_covers);

        Assert.Null(index.Find("Halo", null, 0));
    }

    [Fact]
    public void Find_ReturnsNullForAnEmptyIndex()
    {
        Assert.Null(LocalCoverIndex.Empty.Find("Halo", "Halo", 0));
    }

    [Fact]
    public void Create_IgnoresUnsupportedFiles()
    {
        _ = Touch("Halo.txt");
        _ = Touch("Halo.png");

        Assert.Equal(1, LocalCoverIndex.Create(_covers).Count);
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

    private string Touch(string name)
    {
        string path = Path.Combine(_covers, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
