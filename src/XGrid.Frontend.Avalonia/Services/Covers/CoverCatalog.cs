using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Result of loading and validating a cover catalog.</summary>
/// <param name="Catalog">Loaded catalog, or null when the document is invalid.</param>
/// <param name="Index">Lookup index, or null when the document is invalid.</param>
/// <param name="Error">Validation failure detail, or null on success.</param>
public sealed record CoverCatalogLoadResult(
    CoverCatalog? Catalog,
    CoverCatalogIndex? Index,
    string? Error);

/// <summary>One catalog cover matched to a game.</summary>
/// <param name="Entry">Matched catalog entry.</param>
/// <param name="Score">Match confidence from 0.0 to 1.0.</param>
/// <param name="MatchedName">Catalog name or alias that matched.</param>
public sealed record CoverCatalogMatch(CoverCatalogEntry Entry, double Score, string MatchedName);

/// <summary>
/// Validates a cover catalog and builds exact/fuzzy lookup indexes. The catalog
/// is content-addressed: every image id is the SHA-256 of the encoded WebP file.
/// </summary>
public sealed class CoverCatalogIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, CoverCatalogEntry> _byGameId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<CatalogCandidate>> _byNormalized = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CatalogCandidate>> _byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CatalogCandidate>> _byNoSpace = new(StringComparer.Ordinal);
    private readonly List<CatalogCandidate> _candidates = [];

    private CoverCatalogIndex(CoverCatalog catalog, bool stripAnnotations)
    {
        Catalog = catalog;
        StripAnnotations = stripAnnotations;
    }

    /// <summary>Gets the catalog this index was built from.</summary>
    public CoverCatalog Catalog { get; }

    /// <summary>Gets a value indicating whether annotation groups are stripped.</summary>
    public bool StripAnnotations { get; }

    /// <summary>Gets the number of indexed catalog entries.</summary>
    public int Count => _candidates.Count;

    /// <summary>Loads a catalog document and builds its index.</summary>
    /// <param name="json">Catalog JSON.</param>
    /// <param name="stripAnnotations">Whether annotations are removed before matching.</param>
    /// <returns>The validated catalog and index, or an error.</returns>
    public static CoverCatalogLoadResult LoadJson(string json, bool stripAnnotations = true)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("The cover catalog is empty.");
        }

        CoverCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<CoverCatalog>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            Log.Debug(exception, "The cover catalog JSON is invalid");
            return Failure("The cover catalog JSON is invalid: " + exception.Message);
        }

        if (catalog is null)
        {
            return Failure("The cover catalog is empty.");
        }

        if (catalog.SchemaVersion != CoverDefaults.SchemaVersion)
        {
            return Failure($"The cover catalog schema {catalog.SchemaVersion} is not supported.");
        }

        catalog.Covers ??= [];
        if (catalog.Covers.Count == 0)
        {
            return Failure("The cover catalog contains no covers.");
        }

        var index = new CoverCatalogIndex(catalog, stripAnnotations);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CoverCatalogEntry? entry in catalog.Covers)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
            {
                return Failure("The cover catalog contains an entry without an id.");
            }

            entry.Aliases ??= [];
            entry.GameIds ??= [];
            entry.Regions ??= [];

            string id = entry.Id.Trim();
            if (id.Length != 64 || !IsHex(id))
            {
                return Failure($"The cover catalog id '{id}' is not a SHA-256 hash.");
            }

            if (!ids.Add(id))
            {
                return Failure($"The cover catalog contains the image id '{id}' more than once.");
            }

            string file = string.IsNullOrWhiteSpace(entry.File)
                ? id + ".webp"
                : entry.File.Trim();
            if (file.Contains('/') || file.Contains('\\')
                || !string.Equals(Path.GetFileNameWithoutExtension(file), id, StringComparison.OrdinalIgnoreCase))
            {
                return Failure($"The cover catalog file name '{file}' does not match its id.");
            }

            entry.Id = id;
            entry.File = file;
            index.Add(entry);
        }

        if (index.Count == 0)
        {
            return Failure("The cover catalog contains no covers.");
        }

        return new CoverCatalogLoadResult(catalog, index, null);
    }

    /// <summary>Finds a cover by XBE certificate title id.</summary>
    /// <param name="gameId">Eight-character hexadecimal title id, or null.</param>
    /// <returns>The matched entry, or null.</returns>
    public CoverCatalogEntry? FindByGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        return _byGameId.TryGetValue(gameId.Trim(), out CoverCatalogEntry? entry) ? entry : null;
    }

    /// <summary>Finds the best cover by title and disc file name.</summary>
    /// <param name="title">Display title.</param>
    /// <param name="fileName">Disc file name without extension, when available.</param>
    /// <param name="threshold">Fuzzy-match threshold.</param>
    /// <param name="fuzzy">Whether fuzzy matching is enabled.</param>
    /// <param name="regions">Game region codes for tie-breaking.</param>
    /// <param name="year">Game release year for tie-breaking.</param>
    /// <returns>The best match, or null when no cover is confident enough.</returns>
    public CoverCatalogMatch? FindByTitle(
        string title,
        string? fileName,
        double threshold,
        bool fuzzy,
        string? regions,
        string? year)
    {
        List<string> queryAliases = BuildQueryAliases(title, fileName);
        if (queryAliases.Count == 0)
        {
            return null;
        }

        List<CatalogCandidate>? exact = FindExactCandidates(queryAliases);
        if (exact is not null)
        {
            CatalogCandidate? chosen = Choose(exact, regions, year, allowAmbiguous: false);
            return chosen is null
                ? null
                : new CoverCatalogMatch(chosen.Entry, 1.0, chosen.Entry.Name);
        }

        if (!fuzzy)
        {
            return null;
        }

        double bestScore = 0.0;
        var best = new List<CatalogCandidate>(4);
        double thresholdValue = CoverDefaults.ClampThreshold(threshold);
        foreach (CatalogCandidate candidate in _candidates)
        {
            double score = ScoreCandidate(queryAliases, candidate);
            if (score < thresholdValue)
            {
                continue;
            }

            if (score > bestScore + 0.0001)
            {
                bestScore = score;
                best.Clear();
                best.Add(candidate);
            }
            else if (Math.Abs(score - bestScore) <= 0.0001)
            {
                best.Add(candidate);
            }
        }

        if (best.Count == 0)
        {
            return null;
        }

        CatalogCandidate? winner = Choose(best, regions, year, allowAmbiguous: bestScore < 0.995);
        return winner is null
            ? null
            : new CoverCatalogMatch(winner.Entry, bestScore, winner.Entry.Name);
    }

    /// <summary>Resolves an image URI relative to the catalog URL.</summary>
    /// <param name="catalogUri">Catalog URL.</param>
    /// <param name="catalog">Loaded catalog.</param>
    /// <param name="entry">Cover entry.</param>
    /// <returns>The absolute image URI.</returns>
    public static Uri ResolveImageUri(Uri catalogUri, CoverCatalog catalog, CoverCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(entry);

        string basePath = string.IsNullOrWhiteSpace(catalog.ImagesBase) ? "images/" : catalog.ImagesBase;
        string file = string.IsNullOrWhiteSpace(entry.File) ? entry.Id + ".webp" : entry.File;
        return ResolveRelativeUri(catalogUri, basePath + file);
    }

    /// <summary>Resolves a path relative to a catalog URI.</summary>
    /// <param name="catalogUri">Catalog URL.</param>
    /// <param name="relative">Relative path.</param>
    /// <returns>The absolute URI.</returns>
    public static Uri ResolveRelativeUri(Uri catalogUri, string relative)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);

        string normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The cover catalog path escapes the catalog folder.");
        }

        if (catalogUri.IsFile)
        {
            string directory = Path.GetDirectoryName(catalogUri.LocalPath) ?? string.Empty;
            return new Uri(Path.GetFullPath(Path.Combine(directory, normalized)));
        }

        return new Uri(catalogUri, normalized);
    }

    private void Add(CoverCatalogEntry entry)
    {
        foreach (string? rawId in entry.GameIds)
        {
            if (!string.IsNullOrWhiteSpace(rawId))
            {
                _byGameId.TryAdd(rawId.Trim(), entry);
            }
        }

        var aliases = new List<string>(entry.Aliases.Count + 2);
        var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAlias(entry.Name);
        foreach (string? alias in entry.Aliases)
        {
            AddAlias(alias);
        }

        void AddAlias(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && aliasSet.Add(value.Trim()))
            {
                aliases.Add(value.Trim());
            }
        }

        var keys = new List<CoverNameKeys>(aliases.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string alias in aliases)
        {
            CoverNameKeys key = CoverMatcher.BuildKeys(alias, StripAnnotations);
            if (key.Normalized.Length > 0 && seen.Add(key.Normalized))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            return;
        }

        var candidate = new CatalogCandidate(entry, keys);
        _candidates.Add(candidate);
        foreach (CoverNameKeys key in keys)
        {
            AddTo(_byNormalized, key.Normalized, candidate);
            AddTo(_byToken, key.TokenKey, candidate);
            if (key.NoSpaceKey.Length >= 6)
            {
                AddTo(_byNoSpace, key.NoSpaceKey, candidate);
            }
        }
    }

    private static void AddTo(
        Dictionary<string, List<CatalogCandidate>> index,
        string key,
        CatalogCandidate candidate)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!index.TryGetValue(key, out List<CatalogCandidate>? values))
        {
            values = [];
            index[key] = values;
        }

        if (!values.Contains(candidate))
        {
            values.Add(candidate);
        }
    }

    private List<string> BuildQueryAliases(string? title, string? fileName)
    {
        var result = new List<string>(4);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddQueryAlias(title);
        AddQueryAlias(fileName);
        if (!string.IsNullOrWhiteSpace(title))
        {
            foreach (string alias in CoverMatcher.ExpandAliases(title, StripAnnotations))
            {
                AddQueryAlias(alias);
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            foreach (string alias in CoverMatcher.ExpandAliases(fileName, StripAnnotations))
            {
                AddQueryAlias(alias);
            }
        }

        return result;

        void AddQueryAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = CoverMatcher.Normalize(value, StripAnnotations);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }
    }

    private List<CatalogCandidate>? FindExactCandidates(List<string> queryAliases)
    {
        foreach (string alias in queryAliases)
        {
            if (_byNormalized.TryGetValue(alias, out List<CatalogCandidate>? values))
            {
                return values;
            }
        }

        foreach (string alias in queryAliases)
        {
            if (_byToken.TryGetValue(CoverMatcher.BuildTokenKey(alias), out List<CatalogCandidate>? values))
            {
                return values;
            }
        }

        foreach (string alias in queryAliases)
        {
            string noSpace = CoverMatcher.BuildNoSpaceKey(alias);
            if (noSpace.Length >= 6
                && _byNoSpace.TryGetValue(noSpace, out List<CatalogCandidate>? values))
            {
                return values;
            }
        }

        return null;
    }

    private static double ScoreCandidate(List<string> queryAliases, CatalogCandidate candidate)
    {
        double best = 0.0;
        foreach (CoverNameKeys key in candidate.Keys)
        {
            foreach (string alias in key.NormalizedAliases)
            {
                foreach (string query in queryAliases)
                {
                    if (string.Equals(query, alias, StringComparison.Ordinal))
                    {
                        return 1.0;
                    }

                    if (string.Equals(
                            CoverMatcher.BuildTokenKey(query),
                            CoverMatcher.BuildTokenKey(alias),
                            StringComparison.Ordinal))
                    {
                        best = Math.Max(best, 0.99);
                        continue;
                    }

                    if (query.Length >= 6
                        && string.Equals(
                            CoverMatcher.BuildNoSpaceKey(query),
                            CoverMatcher.BuildNoSpaceKey(alias),
                            StringComparison.Ordinal))
                    {
                        best = Math.Max(best, 0.98);
                        continue;
                    }

                    int shorter = Math.Min(query.Length, alias.Length);
                    int longer = Math.Max(query.Length, alias.Length);
                    if (shorter * 2 < longer)
                    {
                        continue;
                    }

                    double similarity = CoverMatcher.CalculateJaroWinklerSimilarity(query, alias);
                    if (CoverMatcher.HasNumberConflict(query, alias))
                    {
                        similarity = Math.Min(similarity, 0.84);
                    }

                    best = Math.Max(best, similarity);
                }
            }
        }

        return best;
    }

    private static CatalogCandidate? Choose(
        List<CatalogCandidate> candidates,
        string? gameRegions,
        string? gameYear,
        bool allowAmbiguous)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        HashSet<string> regions = ParseRegions(gameRegions);
        string year = gameYear?.Trim() ?? string.Empty;
        CatalogCandidate? best = null;
        int bestRegion = -1;
        int bestYear = -1;
        int bestRank = int.MaxValue;
        int bestLength = int.MaxValue;
        foreach (CatalogCandidate candidate in candidates)
        {
            int regionScore = RegionScore(candidate.Entry, regions);
            int yearScore = string.Equals(candidate.Entry.Year, year, StringComparison.Ordinal) ? 1 : 0;
            if (best is null
                || regionScore > bestRegion
                || (regionScore == bestRegion
                    && (yearScore > bestYear
                        || (yearScore == bestYear
                            && (candidate.Entry.Rank < bestRank
                                || (candidate.Entry.Rank == bestRank
                                    && candidate.Entry.Name.Length < bestLength))))))
            {
                best = candidate;
                bestRegion = regionScore;
                bestYear = yearScore;
                bestRank = candidate.Entry.Rank;
                bestLength = candidate.Entry.Name.Length;
            }
        }

        if (!allowAmbiguous || best is null)
        {
            return best;
        }

        foreach (CatalogCandidate candidate in candidates)
        {
            if (ReferenceEquals(candidate, best))
            {
                continue;
            }

            bool tied = RegionScore(candidate.Entry, regions) == bestRegion
                        && string.Equals(candidate.Entry.Year, best.Entry.Year, StringComparison.Ordinal)
                        && candidate.Entry.Rank == best.Entry.Rank;
            if (tied)
            {
                return null;
            }
        }

        return best;
    }

    private static int RegionScore(CoverCatalogEntry entry, HashSet<string> regions)
    {
        if (regions.Count == 0 || entry.Regions.Count == 0)
        {
            return 0;
        }

        foreach (string? raw in entry.Regions)
        {
            if (!string.IsNullOrWhiteSpace(raw) && regions.Contains(raw.Trim()))
            {
                return 1;
            }
        }

        return 0;
    }

    private static HashSet<string> ParseRegions(string? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (string token in value.Split(
                     ['-', ',', '/', ' ', ';'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(token.ToUpperInvariant());
        }

        return result;
    }

    private static bool IsHex(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static CoverCatalogLoadResult Failure(string message)
    {
        return new CoverCatalogLoadResult(null, null, message);
    }

    private sealed record CatalogCandidate(CoverCatalogEntry Entry, IReadOnlyList<CoverNameKeys> Keys);
}
