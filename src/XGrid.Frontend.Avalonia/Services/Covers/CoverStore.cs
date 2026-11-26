using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Content-addressed cache for the online cover catalog. Catalog metadata and
/// images live under the shell data directory and are safe to delete.
/// </summary>
public sealed class CoverStore
{
    private const int BufferSize = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _root;

    /// <summary>Creates a store.</summary>
    /// <param name="root">Cache root, or null for <see cref="CoverDefaults.CacheDirectory"/>.</param>
    public CoverStore(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root)
            ? CoverDefaults.CacheDirectory
            : Path.GetFullPath(root);
    }

    /// <summary>Gets the cache root.</summary>
    public string Root => _root;

    /// <summary>Gets the catalog cache path.</summary>
    public string CatalogPath => Path.Combine(_root, "catalog.json");

    /// <summary>Gets the catalog metadata sidecar path.</summary>
    public string CatalogMetadataPath => Path.Combine(_root, "catalog.meta.json");

    /// <summary>Gets the image cache directory.</summary>
    public string ImagesDirectory => Path.Combine(_root, "images");

    private string TemporaryDirectory => Path.Combine(_root, "tmp");

    /// <summary>Gets the cache path for one content-addressed image.</summary>
    /// <param name="id">SHA-256 image id.</param>
    /// <returns>The expected cache path.</returns>
    public string GetImagePath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Path.Combine(ImagesDirectory, id + ".webp");
    }

    /// <summary>Returns true when an image is already cached.</summary>
    /// <param name="id">SHA-256 image id.</param>
    /// <returns>True when the cached file exists and is not empty.</returns>
    public bool HasImage(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(GetImagePath(id));
            return file.Exists && file.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Saves one image, verifying its SHA-256 id before publishing it.</summary>
    /// <param name="id">Expected SHA-256 image id.</param>
    /// <param name="source">Image bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Optional byte progress.</param>
    /// <returns>The cached image path.</returns>
    public async Task<string> SaveImageAsync(
        string id,
        Stream source,
        CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateId(id);

        string destination = GetImagePath(id);
        if (HasImage(id))
        {
            return destination;
        }

        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
        string temporary = Path.Combine(TemporaryDirectory, id + "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            long bytes = 0;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[BufferSize];
                await using (var output = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 BufferSize,
                                 useAsync: true))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        bytes += read;
                        if (bytes > CoverDefaults.MaxImageBytes)
                        {
                            throw new InvalidDataException("The cover image exceeds the maximum allowed size.");
                        }

                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        progress?.Report(bytes);
                    }
                }

                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The cover image hash {actual} does not match the catalog id {id}.");
                }
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Tries to read the cached catalog and its sidecar metadata.</summary>
    /// <param name="json">Cached catalog JSON.</param>
    /// <param name="metadata">Cached metadata.</param>
    /// <returns>True when a readable catalog exists.</returns>
    public bool TryLoadCatalog(out string json, out CoverCatalogCacheMetadata metadata)
    {
        json = string.Empty;
        metadata = new CoverCatalogCacheMetadata();
        try
        {
            if (!File.Exists(CatalogPath))
            {
                return false;
            }

            var info = new FileInfo(CatalogPath);
            if (info.Length > CoverDefaults.MaxCatalogBytes)
            {
                return false;
            }

            json = File.ReadAllText(CatalogPath);
            if (File.Exists(CatalogMetadataPath))
            {
                string metaJson = File.ReadAllText(CatalogMetadataPath);
                metadata = JsonSerializer.Deserialize<CoverCatalogCacheMetadata>(metaJson, JsonOptions)
                           ?? new CoverCatalogCacheMetadata();
            }

            return json.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or NotSupportedException)
        {
            Log.Debug(exception, "The cached cover catalog could not be read");
            return false;
        }
    }

    /// <summary>Saves the catalog and its sidecar metadata atomically.</summary>
    /// <param name="json">Catalog JSON.</param>
    /// <param name="source">Source catalog URI.</param>
    /// <param name="etag">Response ETag, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the catalog is written.</returns>
    public async Task SaveCatalogAsync(
        string json,
        Uri source,
        string? etag,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(source);

        Directory.CreateDirectory(_root);
        string temporary = CatalogPath + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, CatalogPath, overwrite: true);

            var metadata = new CoverCatalogCacheMetadata
            {
                SourceUrl = source.AbsoluteUri,
                ETag = etag ?? string.Empty,
                FetchedUtc = DateTimeOffset.UtcNow,
            };
            string metaTemporary = CatalogMetadataPath + "." + Guid.NewGuid().ToString("N") + ".part";
            await File.WriteAllTextAsync(
                    metaTemporary,
                    JsonSerializer.Serialize(metadata, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(metaTemporary, CatalogMetadataPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Deletes every cached catalog and image.</summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "The cover cache could not be cleared");
        }
    }

    /// <summary>Gets the current cache statistics.</summary>
    /// <returns>Image count, image bytes and whether a catalog is cached.</returns>
    public CoverCacheStats GetStats()
    {
        int count = 0;
        long bytes = 0;
        try
        {
            if (Directory.Exists(ImagesDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(ImagesDirectory, "*.webp"))
                {
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 0)
                    {
                        count++;
                        bytes += info.Length;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "The cover cache statistics could not be read");
        }

        return new CoverCacheStats(count, bytes, File.Exists(CatalogPath));
    }

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length != 64)
        {
            throw new InvalidDataException("The cover id is not a SHA-256 hash.");
        }

        foreach (char character in id)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                throw new InvalidDataException("The cover id is not a SHA-256 hash.");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "Temporary cover file {Path} could not be deleted", path);
        }
    }
}
