using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Explicit options for one cover service instance.</summary>
public sealed class CoverServiceOptions
{
    /// <summary>Gets the catalog URI.</summary>
    public Uri CatalogUri { get; init; } = new(CoverDefaults.CatalogUrl);

    /// <summary>Gets a value indicating whether the online catalog may be used.</summary>
    public bool UseOnlineCovers { get; init; } = true;

    /// <summary>Gets a value indicating whether missing covers are downloaded automatically.</summary>
    public bool AutoDownloadCovers { get; init; } = true;

    /// <summary>Gets a value indicating whether fuzzy title matching is enabled.</summary>
    public bool FuzzyMatching { get; init; } = true;

    /// <summary>Gets a value indicating whether annotations are stripped before matching.</summary>
    public bool AnnotationStripping { get; init; } = true;

    /// <summary>Gets the fuzzy-match threshold.</summary>
    public double FuzzyThreshold { get; init; } = CoverDefaults.DefaultFuzzyThreshold;

    /// <summary>Gets the local cover directory, or an empty string to use the default.</summary>
    public string CoverDirectory { get; init; } = string.Empty;

    /// <summary>Gets the cache directory override, or an empty string to use the default.</summary>
    public string CacheDirectory { get; init; } = string.Empty;

    /// <summary>Gets how long a cached catalog is considered fresh for matching.</summary>
    public TimeSpan CatalogCacheDuration { get; init; } = TimeSpan.FromHours(6);
}

/// <summary>
/// Resolves cover art from local files and the online catalog, and downloads
/// missing content-addressed images. Local covers keep working when the network
/// is unavailable.
/// </summary>
public sealed class CoverService : IDisposable
{
    private const int DownloadConcurrency = 4;

    private readonly CoverServiceOptions _options;
    private readonly CoverStore _store;
    private readonly CoverDownloader _downloader;
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly Lock _localGate = new();
    private LocalCoverIndex? _localIndex;
    private CoverCatalog? _catalog;
    private CoverCatalogIndex? _index;
    private CoverCatalogCacheMetadata _metadata = new();
    private bool _disposed;

    /// <summary>Creates a cover service.</summary>
    /// <param name="options">Service options.</param>
    /// <param name="httpClient">Optional HTTP client for tests or a shared connection pool.</param>
    /// <param name="store">Optional cache store for tests.</param>
    public CoverService(
        CoverServiceOptions options,
        HttpClient? httpClient = null,
        CoverStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _store = store ?? new CoverStore(
            string.IsNullOrWhiteSpace(options.CacheDirectory) ? null : options.CacheDirectory);
        _downloader = new CoverDownloader(httpClient);
    }

    /// <summary>Gets the service options.</summary>
    public CoverServiceOptions Options => _options;

    /// <summary>Gets the cover cache store.</summary>
    public CoverStore Store => _store;

    /// <summary>Gets the loaded catalog, if any.</summary>
    public CoverCatalog? Catalog => _catalog;

    /// <summary>Gets the loaded catalog index, if any.</summary>
    public CoverCatalogIndex? Index => _index;

    /// <summary>Gets the current catalog cache metadata.</summary>
    public CoverCatalogCacheMetadata CatalogMetadata => _metadata;

    /// <summary>Refreshes the catalog from its source, falling back to the cache.</summary>
    /// <param name="force">Whether a fresh cache is ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refresh result.</returns>
    public async Task<CoverCatalogRefreshResult> RefreshCatalogAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalog is null)
            {
                TryLoadCachedCatalog();
            }

            if (!_options.UseOnlineCovers)
            {
                return _catalog is not null
                    ? Success(fromCache: true, notModified: false, error: null)
                    : new CoverCatalogRefreshResult(
                        false,
                        false,
                        false,
                        string.Empty,
                        0,
                        "Online covers are disabled.");
            }

            if (!force
                && _catalog is not null
                && _metadata.FetchedUtc != default
                && DateTimeOffset.UtcNow - _metadata.FetchedUtc < _options.CatalogCacheDuration)
            {
                return Success(fromCache: true, notModified: false, error: null);
            }

            try
            {
                CoverCatalogHttpResult response = await _downloader.GetCatalogAsync(
                        _options.CatalogUri,
                        _metadata.ETag,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (response.NotModified && _catalog is null)
                {
                    response = await _downloader.GetCatalogAsync(
                            _options.CatalogUri,
                            etag: null,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (response.NotModified && _catalog is not null)
                {
                    _metadata.FetchedUtc = DateTimeOffset.UtcNow;
                    return Success(fromCache: true, notModified: true, error: null);
                }

                if (!TrySetCatalog(response.Json, response.ETag, out string? error))
                {
                    return _catalog is not null
                        ? Success(fromCache: true, notModified: false, error: error)
                        : new CoverCatalogRefreshResult(
                            false,
                            false,
                            false,
                            string.Empty,
                            0,
                            error);
                }

                await _store.SaveCatalogAsync(
                        response.Json,
                        _options.CatalogUri,
                        response.ETag,
                        cancellationToken)
                    .ConfigureAwait(false);
                Log.Information(
                    "Cover catalog loaded: {PackVersion}, {CoverCount} covers",
                    _catalog?.PackVersion,
                    _catalog?.Covers.Count);
                return Success(fromCache: false, notModified: false, error: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is CoverHttpException
                                                  or HttpRequestException
                                                  or TaskCanceledException
                                                  or IOException
                                                  or UnauthorizedAccessException
                                                  or InvalidDataException
                                                  or NotSupportedException)
            {
                Log.Debug(exception, "Cover catalog refresh failed; using the cached catalog when available");
                return _catalog is not null
                    ? Success(fromCache: true, notModified: false, error: exception.Message)
                    : new CoverCatalogRefreshResult(
                        false,
                        false,
                        false,
                        string.Empty,
                        0,
                        exception.Message);
            }
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    /// <summary>Resolves local and catalog covers for every library entry.</summary>
    /// <param name="entries">Library entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One resolution per entry, in the same order.</returns>
    public async Task<IReadOnlyList<CoverResolution>> ResolveAllAsync(
        IReadOnlyList<GameEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ThrowIfDisposed();
        if (_index is null && _options.UseOnlineCovers)
        {
            await RefreshCatalogAsync(force: false, cancellationToken).ConfigureAwait(false);
        }

        return await Task.Run<IReadOnlyList<CoverResolution>>(
                () =>
                {
                    LocalCoverIndex local = GetLocalIndex();
                    var results = new List<CoverResolution>(entries.Count);
                    foreach (GameEntry entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        results.Add(ResolveEntry(entry, local));
                    }

                    return results;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Downloads every missing cover referenced by a resolution set.</summary>
    /// <param name="resolutions">Resolved covers.</param>
    /// <param name="progress">Optional progress receiver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The download result.</returns>
    public async Task<CoverDownloadResult> DownloadMissingAsync(
        IReadOnlyList<CoverResolution> resolutions,
        IProgress<CoverDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        ThrowIfDisposed();
        if (_index is null || _catalog is null)
        {
            return EmptyDownloadResult();
        }

        var byId = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        foreach (CoverResolution resolution in resolutions)
        {
            if (!resolution.NeedsDownload
                || resolution.CoverId.Length == 0
                || string.IsNullOrWhiteSpace(resolution.CoverPath))
            {
                continue;
            }

            if (!byId.TryGetValue(resolution.CoverId, out Target? target))
            {
                target = new Target(resolution.CoverId, resolution.CoverPath, resolution.MatchedName);
                byId[resolution.CoverId] = target;
            }

            target.EntryPaths.Add(resolution.EntryPath);
        }

        if (byId.Count == 0)
        {
            return EmptyDownloadResult();
        }

        var downloaded = 0;
        var skipped = 0;
        var failed = 0;
        long bytes = 0;
        var completed = 0;
        var coverPaths = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<CoverDownloadFailure>();
        var failureGate = new Lock();
        var catalogById = new Dictionary<string, CoverCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (CoverCatalogEntry entry in _catalog.Covers)
        {
            catalogById[entry.Id] = entry;
        }

        await Parallel.ForEachAsync(
                byId.Values,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = DownloadConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (target, token) =>
                {
                    try
                    {
                        if (_store.HasImage(target.Id))
                        {
                            Interlocked.Increment(ref skipped);
                            AddPaths(coverPaths, target, target.CoverPath);
                            ReportProgress(target.Name);
                            return;
                        }

                        if (!catalogById.TryGetValue(target.Id, out CoverCatalogEntry? catalogEntry))
                        {
                            throw new InvalidDataException("The cover is missing from the loaded catalog.");
                        }

                        Uri imageUri = CoverCatalogIndex.ResolveImageUri(
                            _options.CatalogUri,
                            _catalog,
                            catalogEntry);
                        string path = await _downloader.DownloadImageAsync(
                                imageUri,
                                target.Id,
                                _store,
                                progress: null,
                                cancellationToken: token)
                            .ConfigureAwait(false);
                        Interlocked.Increment(ref downloaded);
                        Interlocked.Add(ref bytes, GetLength(path));
                        AddPaths(coverPaths, target, path);
                        ReportProgress(target.Name);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is CoverHttpException
                                                          or HttpRequestException
                                                          or TaskCanceledException
                                                          or IOException
                                                          or UnauthorizedAccessException
                                                          or InvalidDataException
                                                          or NotSupportedException)
                    {
                        Interlocked.Increment(ref failed);
                        lock (failureGate)
                        {
                            failures.Add(new CoverDownloadFailure(
                                target.EntryPaths.Count > 0 ? target.EntryPaths[0] : string.Empty,
                                target.Id,
                                exception.Message));
                        }

                        Log.Debug(exception, "Cover download failed for {CoverId}", target.Id);
                        ReportProgress(target.Name);
                    }
                })
            .ConfigureAwait(false);

        progress?.Report(new CoverDownloadProgress(completed, byId.Count, bytes, string.Empty));
        return new CoverDownloadResult(
            downloaded,
            skipped,
            failed,
            bytes,
            failures,
            coverPaths);

        void AddPaths(ConcurrentDictionary<string, string> paths, Target target, string path)
        {
            foreach (string entryPath in target.EntryPaths)
            {
                paths[entryPath] = path;
            }
        }

        void ReportProgress(string currentName)
        {
            int value = Interlocked.Increment(ref completed);
            progress?.Report(new CoverDownloadProgress(value, byId.Count, Interlocked.Read(ref bytes), currentName));
        }
    }

    /// <summary>Gets current cache statistics.</summary>
    /// <returns>Cache statistics.</returns>
    public CoverCacheStats GetCacheStats()
    {
        ThrowIfDisposed();
        return _store.GetStats();
    }

    /// <summary>Deletes the online cover cache and reloads the local index.</summary>
    public void ClearCache()
    {
        ThrowIfDisposed();
        _store.Clear();
        lock (_localGate)
        {
            _localIndex = null;
        }
    }

    /// <summary>Clears the cached local-cover index so the next resolution rescans the directory.</summary>
    public void ReloadLocalCovers()
    {
        ThrowIfDisposed();
        lock (_localGate)
        {
            _localIndex = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _downloader.Dispose();
        _catalogGate.Dispose();
    }

    private CoverResolution ResolveEntry(GameEntry entry, LocalCoverIndex local)
    {
        string fileName = Path.GetFileNameWithoutExtension(entry.Path);
        string title = entry.Title;
        CoverAnnotations annotations = CoverMatcher.ParseAnnotations(fileName);
        var matchOptions = new CoverMatchOptions
        {
            Fuzzy = _options.FuzzyMatching,
            StripAnnotations = _options.AnnotationStripping,
            Threshold = _options.FuzzyThreshold,
            Regions = string.Join(", ", annotations.Regions),
            Year = annotations.Year,
        };
        CoverMatchOptions exactOptions = matchOptions with { Fuzzy = false };

        CoverMatch? localExact = local.Find(title, fileName, entry.TitleId, exactOptions);
        if (localExact is not null)
        {
            return LocalResolution(entry, localExact);
        }

        if (_index is not null)
        {
            CoverCatalogEntry? byGameId = entry.TitleId == 0
                ? null
                : _index.FindByGameId(
                    entry.TitleId.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            if (byGameId is not null)
            {
                return CatalogResolution(entry, byGameId, 1.0, "catalog-title-id", byGameId.Name);
            }

            CoverCatalogMatch? exact = _index.FindByTitle(
                title,
                fileName,
                matchOptions.Threshold,
                fuzzy: false,
                regions: matchOptions.Regions,
                year: matchOptions.Year);
            if (exact is not null)
            {
                return CatalogResolution(entry, exact, "catalog-exact");
            }
        }

        CoverMatch? localFuzzy = local.Find(title, fileName, entry.TitleId, matchOptions);
        if (localFuzzy is not null)
        {
            return LocalResolution(entry, localFuzzy);
        }

        if (_index is not null)
        {
            CoverCatalogMatch? fuzzy = _index.FindByTitle(
                title,
                fileName,
                matchOptions.Threshold,
                fuzzy: _options.FuzzyMatching,
                regions: matchOptions.Regions,
                year: matchOptions.Year);
            if (fuzzy is not null)
            {
                return CatalogResolution(entry, fuzzy, "catalog-fuzzy");
            }
        }

        return new CoverResolution(entry.Path, null, "none", 0.0, string.Empty);
    }

    private static CoverResolution LocalResolution(GameEntry entry, CoverMatch match)
    {
        return new CoverResolution(
            entry.Path,
            match.Path,
            match.Source,
            match.Score,
            match.MatchedName,
            Regions: match.Regions,
            Year: match.Year);
    }

    private CoverResolution CatalogResolution(
        GameEntry entry,
        CoverCatalogEntry catalogEntry,
        double score,
        string source,
        string matchedName)
    {
        string path = _store.GetImagePath(catalogEntry.Id);
        return new CoverResolution(
            entry.Path,
            path,
            source,
            score,
            matchedName,
            catalogEntry.Id,
            NeedsDownload: !_store.HasImage(catalogEntry.Id),
            Regions: string.Join(", ", catalogEntry.Regions),
            Year: catalogEntry.Year,
            Publisher: catalogEntry.Publisher,
            Developer: catalogEntry.Developer,
            Genre: catalogEntry.Genre,
            Players: catalogEntry.Players,
            Description: catalogEntry.Description);
    }

    private CoverResolution CatalogResolution(GameEntry entry, CoverCatalogMatch match, string source)
    {
        return CatalogResolution(entry, match.Entry, match.Score, source, match.MatchedName);
    }

    private LocalCoverIndex GetLocalIndex()
    {
        lock (_localGate)
        {
            if (_localIndex is not null)
            {
                return _localIndex;
            }

            string directory = string.IsNullOrWhiteSpace(_options.CoverDirectory)
                ? CoverDefaults.LocalDirectory
                : _options.CoverDirectory;
            _localIndex = LocalCoverIndex.Create(directory, _options.AnnotationStripping);
            return _localIndex;
        }
    }

    private void TryLoadCachedCatalog()
    {
        if (!_store.TryLoadCatalog(out string json, out CoverCatalogCacheMetadata metadata))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(metadata.SourceUrl)
            && !string.Equals(
                metadata.SourceUrl,
                _options.CatalogUri.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("Ignoring a cached cover catalog from another source");
            return;
        }

        if (TrySetCatalog(json, metadata.ETag, out string? error))
        {
            _metadata = metadata;
            return;
        }

        Log.Debug("Cached cover catalog is invalid: {Error}", error);
    }

    private bool TrySetCatalog(string json, string? etag, out string? error)
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson(json, _options.AnnotationStripping);
        if (result.Catalog is null || result.Index is null)
        {
            error = result.Error;
            return false;
        }

        _catalog = result.Catalog;
        _index = result.Index;
        _metadata = new CoverCatalogCacheMetadata
        {
            SourceUrl = _options.CatalogUri.AbsoluteUri,
            ETag = etag ?? string.Empty,
            FetchedUtc = DateTimeOffset.UtcNow,
        };
        error = null;
        return true;
    }

    private CoverCatalogRefreshResult Success(bool fromCache, bool notModified, string? error)
    {
        return new CoverCatalogRefreshResult(
            true,
            fromCache,
            notModified,
            _catalog?.PackVersion ?? string.Empty,
            _catalog?.Covers.Count ?? 0,
            error);
    }

    private static CoverDownloadResult EmptyDownloadResult()
    {
        return new CoverDownloadResult(
            0,
            0,
            0,
            0,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static long GetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Debug(exception, "The cover length for {Path} could not be read", path);
            return 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class Target
    {
        internal Target(string id, string coverPath, string name)
        {
            Id = id;
            CoverPath = coverPath;
            Name = name;
        }

        internal string Id { get; }

        internal string CoverPath { get; }

        internal string Name { get; }

        internal List<string> EntryPaths { get; } = [];
    }
}
