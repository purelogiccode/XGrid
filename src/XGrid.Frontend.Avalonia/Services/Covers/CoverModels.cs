using System;
using System.Collections.Generic;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Options for one local cover lookup.</summary>
public sealed record CoverMatchOptions
{
    /// <summary>Gets a value indicating whether fuzzy Jaro-Winkler matching is enabled.</summary>
    public bool Fuzzy { get; init; } = true;

    /// <summary>Gets a value indicating whether annotation groups are removed.</summary>
    public bool StripAnnotations { get; init; } = true;

    /// <summary>Gets the fuzzy-match threshold.</summary>
    public double Threshold { get; init; } = CoverDefaults.DefaultFuzzyThreshold;

    /// <summary>Gets the game's region codes, used for tie-breaking.</summary>
    public string Regions { get; init; } = string.Empty;

    /// <summary>Gets the game's release year, used for tie-breaking.</summary>
    public string Year { get; init; } = string.Empty;
}

/// <summary>One resolved local cover.</summary>
/// <param name="Path">Cover file path.</param>
/// <param name="MatchedName">Cover file name or alias that matched.</param>
/// <param name="Score">Match confidence from 0.0 to 1.0.</param>
/// <param name="Source">Resolution source label.</param>
/// <param name="Regions">Regions parsed from the cover name.</param>
/// <param name="Year">Release year parsed from the cover name.</param>
public sealed record CoverMatch(
    string Path,
    string MatchedName,
    double Score,
    string Source,
    string Regions,
    string Year);

/// <summary>One resolved cover for a library entry.</summary>
/// <param name="EntryPath">Disc path that was resolved.</param>
/// <param name="CoverPath">Local cover path, including a not-yet-downloaded cache path.</param>
/// <param name="CoverSource">Resolution source label.</param>
/// <param name="Score">Match confidence from 0.0 to 1.0.</param>
/// <param name="MatchedName">Cover alias or catalog name that matched.</param>
/// <param name="CoverId">Catalog image identifier, or empty for local files.</param>
/// <param name="NeedsDownload">Whether the local cache file still needs to be downloaded.</param>
/// <param name="Regions">Regions from the matched catalog or cover name.</param>
/// <param name="Year">Release year from the matched catalog or cover name.</param>
/// <param name="Publisher">Publisher metadata from the catalog, when present.</param>
/// <param name="Developer">Developer metadata from the catalog, when present.</param>
/// <param name="Genre">Genre metadata from the catalog, when present.</param>
/// <param name="Players">Player count metadata from the catalog, when present.</param>
/// <param name="Description">Description metadata from the catalog, when present.</param>
public sealed record CoverResolution(
    string EntryPath,
    string? CoverPath,
    string CoverSource,
    double Score,
    string MatchedName,
    string CoverId = "",
    bool NeedsDownload = false,
    string Regions = "",
    string Year = "",
    string Publisher = "",
    string Developer = "",
    string Genre = "",
    int Players = 0,
    string Description = "")
{
    /// <summary>Gets a value indicating whether a cover path was resolved.</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);
}

/// <summary>Progress for a batch cover download.</summary>
/// <param name="Completed">Completed items.</param>
/// <param name="Total">Total items.</param>
/// <param name="Bytes">Bytes written.</param>
/// <param name="CurrentName">Current cover name.</param>
public sealed record CoverDownloadProgress(
    int Completed,
    int Total,
    long Bytes,
    string CurrentName);

/// <summary>One failed cover download.</summary>
/// <param name="EntryPath">Disc path, or empty for a catalog-wide download.</param>
/// <param name="CoverId">Catalog image identifier.</param>
/// <param name="Message">Failure detail.</param>
public sealed record CoverDownloadFailure(string EntryPath, string CoverId, string Message);

/// <summary>Result of a batch cover download.</summary>
/// <param name="Downloaded">Downloaded items.</param>
/// <param name="Skipped">Already-cached items.</param>
/// <param name="Failed">Failed items.</param>
/// <param name="Bytes">Bytes written.</param>
/// <param name="Failures">Failed items.</param>
/// <param name="CoverPaths">Resolved cover paths keyed by disc path.</param>
public sealed record CoverDownloadResult(
    int Downloaded,
    int Skipped,
    int Failed,
    long Bytes,
    IReadOnlyList<CoverDownloadFailure> Failures,
    IReadOnlyDictionary<string, string> CoverPaths);

/// <summary>Result of one cover-catalog refresh.</summary>
/// <param name="Success">Whether a usable catalog is available.</param>
/// <param name="FromCache">Whether the cached catalog was used.</param>
/// <param name="NotModified">Whether the server returned HTTP 304.</param>
/// <param name="PackVersion">Catalog pack version.</param>
/// <param name="CoverCount">Number of cover entries.</param>
/// <param name="Error">Optional failure detail.</param>
public sealed record CoverCatalogRefreshResult(
    bool Success,
    bool FromCache,
    bool NotModified,
    string PackVersion,
    int CoverCount,
    string? Error);

/// <summary>Result of a conditional catalog request.</summary>
/// <param name="NotModified">Whether the server returned HTTP 304.</param>
/// <param name="Json">Catalog JSON.</param>
/// <param name="ETag">Response ETag, or an empty string.</param>
public sealed record CoverCatalogHttpResult(bool NotModified, string Json, string ETag);

/// <summary>Cache statistics for the online cover store.</summary>
/// <param name="ImageCount">Number of cached images.</param>
/// <param name="Bytes">Total cached image bytes.</param>
/// <param name="CatalogPresent">Whether a cached catalog exists.</param>
public sealed record CoverCacheStats(int ImageCount, long Bytes, bool CatalogPresent);

/// <summary>Sidecar metadata for the cached catalog.</summary>
public sealed class CoverCatalogCacheMetadata
{
    /// <summary>Gets or sets the catalog schema version.</summary>
    public int SchemaVersion { get; set; } = CoverDefaults.SchemaVersion;

    /// <summary>Gets or sets the source catalog URL.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the HTTP ETag returned by the server.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC fetch timestamp.</summary>
    public DateTimeOffset FetchedUtc { get; set; }
}

/// <summary>
/// The online cover catalog. It is published as <c>catalog.json</c> and
/// references content-addressed WebP images.
/// </summary>
public sealed class CoverCatalog
{
    /// <summary>Gets or sets the catalog schema version.</summary>
    public int SchemaVersion { get; set; } = CoverDefaults.SchemaVersion;

    /// <summary>Gets or sets the cover-pack version label.</summary>
    public string PackVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC generation timestamp.</summary>
    public DateTimeOffset? GeneratedUtc { get; set; }

    /// <summary>Gets or sets the optional rights/takedown notice URL.</summary>
    public string NoticeUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the image path prefix, relative to the catalog URL.</summary>
    public string ImagesBase { get; set; } = "images/";

    /// <summary>Gets or sets the optional bulk-pack path, relative to the catalog URL.</summary>
    public string PackFile { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional bulk-pack size in bytes.</summary>
    public long PackSize { get; set; }

    /// <summary>Gets or sets the optional bulk-pack SHA-256 hash.</summary>
    public string PackSha256 { get; set; } = string.Empty;

    /// <summary>Gets the cover entries.</summary>
    public IList<CoverCatalogEntry> Covers { get; set; } = [];
}

/// <summary>One cover image in the online catalog.</summary>
public sealed class CoverCatalogEntry
{
    /// <summary>Gets or sets the SHA-256 identifier of the encoded WebP image.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the image file name, relative to <see cref="CoverCatalog.ImagesBase"/>.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the original cover-file names that resolve to this image.</summary>
    public IList<string> Aliases { get; set; } = [];

    /// <summary>Gets the XBE certificate title ids mapped to this cover.</summary>
    public IList<string> GameIds { get; set; } = [];

    /// <summary>Gets or sets the encoded image width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the encoded image height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the encoded image size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Gets the region codes parsed from the source file names.</summary>
    public IList<string> Regions { get; set; } = [];

    /// <summary>Gets or sets the release year parsed from the source file names.</summary>
    public string Year { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release rank: 0 retail, 1 demo/sample, 2 beta/proto,
    /// 3 unknown.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>Gets or sets the optional publisher metadata.</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional developer metadata.</summary>
    public string Developer { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional genre metadata.</summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional player-count metadata.</summary>
    public int Players { get; set; }

    /// <summary>Gets or sets the optional description metadata.</summary>
    public string Description { get; set; } = string.Empty;
}
