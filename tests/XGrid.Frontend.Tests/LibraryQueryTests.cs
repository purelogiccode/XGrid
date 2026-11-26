using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Frontend.Avalonia.ViewModels;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins library search, collection filters and sort orders.</summary>
public sealed class LibraryQueryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "xgrid-library-query-" + Guid.NewGuid().ToString("N"));

    private readonly CoverArtCache _covers = new();
    private readonly List<GameListItem> _items = [];

    public LibraryQueryTests()
    {
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Apply_FiltersBySearchTextAndFormat()
    {
        GameListItem halo = Item("Halo - Combat Evolved (USA).iso");
        Item("Panzer Dragoon Orta (Europe).chd");

        Assert.Single(LibraryQuery.Apply(_items, "halo", LibraryFilter.All, LibrarySort.Title));
        Assert.Single(LibraryQuery.Apply(_items, "USA", LibraryFilter.All, LibrarySort.Title));
        Assert.Single(LibraryQuery.Apply(_items, "CHD", LibraryFilter.All, LibrarySort.Title));
        Assert.Equal(halo.Title, LibraryQuery.Apply(_items, "halo", LibraryFilter.All, LibrarySort.Title)[0].Title);
    }

    [Fact]
    public void Apply_FiltersFavorites()
    {
        GameListItem halo = Item("Halo.iso");
        _ = Item("Fable.iso");
        halo.SetFavorite(true);

        List<GameListItem> result = LibraryQuery.Apply(
            _items,
            query: null,
            LibraryFilter.Favorites,
            LibrarySort.Title);

        Assert.Single(result);
        Assert.Same(halo, result[0]);
    }

    [Fact]
    public void Apply_FiltersPlayedAndUnplayed()
    {
        GameListItem halo = Item("Halo.iso");
        _ = Item("Fable.iso");
        halo.SetPlayState(new LibraryPlayRecord { Seconds = 60, LastPlayedUtc = DateTime.UtcNow });

        Assert.Single(LibraryQuery.Apply(_items, null, LibraryFilter.Played, LibrarySort.Title));
        Assert.Single(LibraryQuery.Apply(_items, null, LibraryFilter.Unplayed, LibrarySort.Title));
    }

    [Fact]
    public void Apply_SortsByMostPlayedThenTitle()
    {
        GameListItem halo = Item("Halo.iso");
        GameListItem fable = Item("Fable.iso");
        halo.SetPlayState(new LibraryPlayRecord { Seconds = 10 });
        fable.SetPlayState(new LibraryPlayRecord { Seconds = 30 });

        List<GameListItem> result = LibraryQuery.Apply(
            _items,
            null,
            LibraryFilter.All,
            LibrarySort.MostPlayed);

        Assert.Same(fable, result[0]);
        Assert.Same(halo, result[1]);
    }

    [Fact]
    public void Apply_SortsByRecentlyPlayedWithNullsLast()
    {
        GameListItem halo = Item("Halo.iso");
        GameListItem fable = Item("Fable.iso");
        _ = Item("Psychonauts.iso");
        halo.SetPlayState(new LibraryPlayRecord { LastPlayedUtc = DateTime.UtcNow.AddHours(-2) });
        fable.SetPlayState(new LibraryPlayRecord { LastPlayedUtc = DateTime.UtcNow.AddHours(-1) });

        List<GameListItem> result = LibraryQuery.Apply(
            _items,
            null,
            LibraryFilter.All,
            LibrarySort.RecentlyPlayed);

        Assert.Same(fable, result[0]);
        Assert.Same(halo, result[1]);
        Assert.Equal("Psychonauts", result[2].Title);
    }

    [Fact]
    public void Apply_SortsByYearAndPublisher()
    {
        GameListItem halo = Item("Halo.iso");
        GameListItem fable = Item("Fable.iso");
        halo.ApplyCoverResolution(new CoverResolution(
            halo.Path,
            null,
            "catalog",
            1.0,
            "Halo",
            Year: "2001",
            Publisher: "Microsoft Game Studios"));
        fable.ApplyCoverResolution(new CoverResolution(
            fable.Path,
            null,
            "catalog",
            1.0,
            "Fable",
            Year: "2004",
            Publisher: "Bungie"));

        Assert.Same(fable, LibraryQuery.Apply(_items, null, LibraryFilter.All, LibrarySort.ReleaseYear)[0]);
        Assert.Equal("Fable", LibraryQuery.Apply(_items, null, LibraryFilter.All, LibrarySort.Publisher)[0].Title);
    }

    [Fact]
    public void Title_StripsAnnotations()
    {
        GameListItem item = Item("Halo - Combat Evolved (USA) [!].iso");

        Assert.Equal("Halo - Combat Evolved", item.Title);
    }

    public void Dispose()
    {
        _covers.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail a test run.
        }
    }

    private GameListItem Item(string name)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        GameEntry? entry = GameEntry.FromFile(path, LocalCoverIndex.Empty, resolveTitleId: false);
        Assert.NotNull(entry);
        var item = new GameListItem(entry, _ => { }, _ => { }, _covers);
        _items.Add(item);
        return item;
    }
}
