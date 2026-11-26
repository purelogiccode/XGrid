using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins local and catalog cover resolution and image downloads.</summary>
public sealed class CoverServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "xgrid-cover-service-" + Guid.NewGuid().ToString("N"));

    private readonly string _games;
    private readonly string _localCovers;
    private readonly string _cache;
    private readonly string _catalogRoot;

    public CoverServiceTests()
    {
        _games = Path.Combine(_root, "games");
        _localCovers = Path.Combine(_root, "covers");
        _cache = Path.Combine(_root, "cache");
        _catalogRoot = Path.Combine(_root, "catalog");
        _ = Directory.CreateDirectory(_games);
        _ = Directory.CreateDirectory(_localCovers);
        _ = Directory.CreateDirectory(_catalogRoot);
    }

    [Fact]
    public async Task ResolveAllAsync_UsesAnExactLocalCover()
    {
        string game = Touch(Path.Combine(_games, "Halo - Combat Evolved.iso"));
        string cover = Touch(Path.Combine(_localCovers, "Halo - Combat Evolved (USA) [!].png"));
        GameEntry entry = CreateEntry(game);
        using var service = new CoverService(Options(useOnline: false));

        IReadOnlyList<CoverResolution> resolutions = await service.ResolveAllAsync([entry]);

        Assert.Single(resolutions);
        Assert.Equal(cover, resolutions[0].CoverPath);
        Assert.Equal("local-exact", resolutions[0].CoverSource);
        Assert.False(resolutions[0].NeedsDownload);
    }

    [Fact]
    public async Task ResolveAllAsync_FindsACatalogCoverAndDownloadsIt()
    {
        string game = Touch(Path.Combine(_games, "Halo - Combat Evolved.iso"));
        GameEntry entry = CreateEntry(game);
        (string catalogPath, string id) = CreateCatalog("Halo - Combat Evolved");
        using var service = new CoverService(Options(true, catalogPath));

        CoverCatalogRefreshResult refresh = await service.RefreshCatalogAsync(force: true);
        Assert.True(refresh.Success);
        Assert.Equal(1, refresh.CoverCount);

        IReadOnlyList<CoverResolution> resolutions = await service.ResolveAllAsync([entry]);
        Assert.Single(resolutions);
        Assert.Equal(id, resolutions[0].CoverId);
        Assert.True(resolutions[0].NeedsDownload);

        CoverDownloadResult download = await service.DownloadMissingAsync(resolutions);
        Assert.Equal(1, download.Downloaded);
        Assert.True(File.Exists(service.Store.GetImagePath(id)));
        Assert.True(download.CoverPaths.TryGetValue(entry.Path, out string? coverPath));
        Assert.True(File.Exists(coverPath));

        IReadOnlyList<CoverResolution> again = await service.ResolveAllAsync([entry]);
        Assert.False(again[0].NeedsDownload);
    }

    [Fact]
    public async Task ResolveAllAsync_PrefersCatalogExactOverLocalFuzzy()
    {
        string game = Touch(Path.Combine(_games, "Halo - Combat Evolved.iso"));
        _ = Touch(Path.Combine(_localCovers, "Halo Combat Evolvd.png"));
        GameEntry entry = CreateEntry(game);
        (string catalogPath, string id) = CreateCatalog("Halo - Combat Evolved");
        using var service = new CoverService(Options(true, catalogPath));
        _ = await service.RefreshCatalogAsync(force: true);

        IReadOnlyList<CoverResolution> resolutions = await service.ResolveAllAsync([entry]);

        Assert.Single(resolutions);
        Assert.Equal("catalog-exact", resolutions[0].CoverSource);
        Assert.Equal(id, resolutions[0].CoverId);
    }

    [Fact]
    public async Task ResolveAllAsync_ReturnsNoCoverWhenDisabledAndMissing()
    {
        string game = Touch(Path.Combine(_games, "Halo - Combat Evolved.iso"));
        GameEntry entry = CreateEntry(game);
        using var service = new CoverService(Options(useOnline: false));

        IReadOnlyList<CoverResolution> resolutions = await service.ResolveAllAsync([entry]);

        Assert.Single(resolutions);
        Assert.Equal("none", resolutions[0].CoverSource);
        Assert.Null(resolutions[0].CoverPath);
    }

    [Fact]
    public async Task ResolveAllAsync_RejectsASequelMismatchInTheCatalog()
    {
        string game = Touch(Path.Combine(_games, "Halo.iso"));
        GameEntry entry = CreateEntry(game);
        _ = CreateCatalog("Halo 2");
        using var service = new CoverService(Options(useOnline: true, _catalogRoot + "/catalog.json"));
        _ = await service.RefreshCatalogAsync(force: true);

        IReadOnlyList<CoverResolution> resolutions = await service.ResolveAllAsync([entry]);

        Assert.Single(resolutions);
        Assert.Equal("none", resolutions[0].CoverSource);
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

    private CoverServiceOptions Options(bool useOnline, string? catalogPath = null)
    {
        return new CoverServiceOptions
        {
            CatalogUri = new Uri(catalogPath ?? Path.Combine(_catalogRoot, "catalog.json")),
            UseOnlineCovers = useOnline,
            AutoDownloadCovers = true,
            FuzzyMatching = true,
            AnnotationStripping = true,
            FuzzyThreshold = CoverDefaults.DefaultFuzzyThreshold,
            CoverDirectory = _localCovers,
            CacheDirectory = _cache,
        };
    }

    private (string CatalogPath, string Id) CreateCatalog(string name)
    {
        byte[] image = [9, 8, 7, 6];
        string id = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
        string images = Path.Combine(_catalogRoot, "images");
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, id + ".webp"), image);

        string catalogPath = Path.Combine(_catalogRoot, "catalog.json");
        File.WriteAllText(
            catalogPath,
            $$"""
              {
                "schemaVersion": 1,
                "packVersion": "test",
                "imagesBase": "images/",
                "covers": [
                  {
                    "id": "{{id}}",
                    "file": "{{id}}.webp",
                    "name": "{{name}}",
                    "regions": ["USA"],
                    "year": "2001"
                  }
                ]
              }
              """);
        return (catalogPath, id);
    }

    private static GameEntry CreateEntry(string game)
    {
        GameEntry? entry = GameEntry.FromFile(game, LocalCoverIndex.Empty, resolveTitleId: false);
        Assert.NotNull(entry);
        return entry;
    }

    private static string Touch(string path)
    {
        File.WriteAllBytes(path, []);
        return path;
    }
}
