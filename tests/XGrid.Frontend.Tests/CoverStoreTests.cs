using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins content-addressed image storage and catalog caching.</summary>
public sealed class CoverStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "xgrid-cover-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveImageAsync_VerifiesTheHashAndWritesTheFile()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        string id = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var store = new CoverStore(_root);
        using var stream = new MemoryStream(bytes);

        string path = await store.SaveImageAsync(id, stream, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.True(store.HasImage(id));
        Assert.Equal(1, store.GetStats().ImageCount);
    }

    [Fact]
    public async Task SaveImageAsync_RejectsABadHash()
    {
        var store = new CoverStore(_root);
        using var stream = new MemoryStream([1, 2, 3]);
        string id = new('a', 64);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveImageAsync(id, stream, CancellationToken.None));
        Assert.False(store.HasImage(id));
    }

    [Fact]
    public async Task Catalog_RoundTripsThroughTheServerCache()
    {
        var store = new CoverStore(_root);
        const string json = "{\"schemaVersion\":1,\"covers\":[]}";
        var source = new Uri("https://example.test/xgrid/v1/catalog.json");

        await store.SaveCatalogAsync(json, source, "\"etag\"", CancellationToken.None);

        Assert.True(store.TryLoadCatalog(out string cached, out CoverCatalogCacheMetadata metadata));
        Assert.Equal(json, cached);
        Assert.Equal(source.AbsoluteUri, metadata.SourceUrl);
        Assert.Equal("\"etag\"", metadata.ETag);
        Assert.True(store.GetStats().CatalogPresent);
    }

    [Fact]
    public async Task Clear_RemovesImagesAndCatalog()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("image");
        string id = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var store = new CoverStore(_root);
        using var stream = new MemoryStream(bytes);
        _ = await store.SaveImageAsync(id, stream, CancellationToken.None);
        await store.SaveCatalogAsync(
            "{}",
            new Uri("https://example.test/catalog.json"),
            null,
            CancellationToken.None);

        store.Clear();

        Assert.False(Directory.Exists(_root));
        Assert.False(store.HasImage(id));
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
}
