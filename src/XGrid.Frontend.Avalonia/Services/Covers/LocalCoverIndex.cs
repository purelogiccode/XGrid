using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Indexes a local cover directory for exact and fuzzy name matching. Only file
/// names are read while indexing; images are decoded lazily when a card is
/// shown. The index is read-only after construction and can be reused for every
/// entry in one library scan.
/// </summary>
public sealed class LocalCoverIndex
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly Dictionary<string, Candidate> _byTitleId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Candidate> _byBaseName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Candidate>> _byNormalized = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Candidate>> _byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Candidate>> _byNoSpace = new(StringComparer.Ordinal);
    private readonly List<Candidate> _candidates = [];
    private readonly bool _stripAnnotations;

    private LocalCoverIndex(string directory, bool stripAnnotations)
    {
        DirectoryPath = directory;
        _stripAnnotations = stripAnnotations;
    }

    /// <summary>An index with no directory; every lookup returns null.</summary>
    public static LocalCoverIndex Empty { get; } = new(string.Empty, stripAnnotations: true);

    /// <summary>Gets the indexed directory, or an empty string for <see cref="Empty"/>.</summary>
    public string DirectoryPath { get; }

    /// <summary>Gets the number of indexed images.</summary>
    public int Count => _candidates.Count;

    /// <summary>Builds an index for one directory.</summary>
    /// <param name="directory">Directory to index, or null/empty for no covers.</param>
    /// <param name="stripAnnotations">Whether annotations are removed before matching.</param>
    /// <returns>The index, which may be empty.</returns>
    public static LocalCoverIndex Create(string? directory, bool stripAnnotations = true)
    {
        if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
        {
            return Empty;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(exception, "The cover directory is not valid: {Directory}", directory);
            return Empty;
        }

        var index = new LocalCoverIndex(fullPath, stripAnnotations);
        try
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(fullPath))
            {
                if (IsImage(file))
                {
                    index.Add(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            Log.Debug(exception, "Could not index the cover directory {Directory}", directory);
        }

        return index;
    }

    /// <summary>Finds the best cover for one game.</summary>
    /// <param name="title">Display title from the library scanner.</param>
    /// <param name="fileName">Disc file name without extension, when available.</param>
    /// <param name="titleId">XBE certificate title id, or 0 when unknown.</param>
    /// <param name="options">Matching options, or null for the defaults.</param>
    /// <returns>The best match, or null when no cover is confident enough.</returns>
    public CoverMatch? Find(
        string title,
        string? fileName,
        uint titleId,
        CoverMatchOptions? options = null)
    {
        options ??= new CoverMatchOptions();
        if (_candidates.Count == 0)
        {
            return null;
        }

        if (titleId != 0)
        {
            string id = titleId.ToString("X8", CultureInfo.InvariantCulture);
            if (_byTitleId.TryGetValue(id, out Candidate? byId))
            {
                return ToMatch(byId, 1.0, "local-title-id");
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName)
            && _byBaseName.TryGetValue(fileName, out Candidate? exactFile))
        {
            return ToMatch(exactFile, 1.0, "local-exact");
        }

        List<string> queryAliases = BuildQueryAliases(title, fileName, options.StripAnnotations);
        if (queryAliases.Count == 0)
        {
            return null;
        }

        List<Candidate>? exact = FindExactCandidates(queryAliases);
        if (exact is not null)
        {
            Candidate? chosen = Choose(exact, options, allowAmbiguous: false);
            return chosen is null ? null : ToMatch(chosen, 1.0, "local-exact");
        }

        if (!options.Fuzzy)
        {
            return null;
        }

        double threshold = CoverDefaults.ClampThreshold(options.Threshold);
        double bestScore = 0.0;
        var best = new List<Candidate>(4);
        foreach (Candidate candidate in _candidates)
        {
            double score = ScoreCandidate(queryAliases, candidate);
            if (score < threshold)
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

        Candidate? winner = Choose(best, options, allowAmbiguous: bestScore < 0.995);
        return winner is null ? null : ToMatch(winner, bestScore, "local-fuzzy");
    }

    /// <summary>Returns true when a path has a supported cover extension.</summary>
    /// <param name="path">Candidate image path.</param>
    /// <returns>True for PNG, JPEG, WebP and BMP.</returns>
    public static bool IsImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string extension = Path.GetExtension(path);
        foreach (string candidate in Extensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void Add(string file)
    {
        string baseName = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return;
        }

        CoverAnnotations metadata = CoverMatcher.ParseAnnotations(baseName);
        var aliases = new List<string>(4);
        var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAlias(baseName);
        if (metadata.Title.Length > 0)
        {
            AddAlias(metadata.Title);
        }

        foreach (string alias in CoverMatcher.ExpandAliases(baseName, _stripAnnotations))
        {
            AddAlias(alias);
        }

        void AddAlias(string value)
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
            CoverNameKeys key = CoverMatcher.BuildKeys(alias, _stripAnnotations);
            if (key.Normalized.Length > 0 && seen.Add(key.Normalized))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            return;
        }

        var candidate = new Candidate(
            file,
            baseName,
            keys,
            string.Join(", ", metadata.Regions),
            metadata.Year,
            metadata.Rank,
            aliases);
        _candidates.Add(candidate);
        _byBaseName.TryAdd(baseName, candidate);

        if (baseName.Length == 8
            && uint.TryParse(baseName, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            _byTitleId.TryAdd(baseName, candidate);
        }

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
        Dictionary<string, List<Candidate>> index,
        string key,
        Candidate candidate)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (!index.TryGetValue(key, out List<Candidate>? values))
        {
            values = [];
            index[key] = values;
        }

        if (!values.Contains(candidate))
        {
            values.Add(candidate);
        }
    }

    private static List<string> BuildQueryAliases(
        string? title,
        string? fileName,
        bool stripAnnotations)
    {
        var result = new List<string>(4);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddQueryAlias(title);
        AddQueryAlias(fileName);
        if (!string.IsNullOrWhiteSpace(title))
        {
            foreach (string alias in CoverMatcher.ExpandAliases(title, stripAnnotations))
            {
                AddQueryAlias(alias);
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            foreach (string alias in CoverMatcher.ExpandAliases(fileName, stripAnnotations))
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

            string normalized = CoverMatcher.Normalize(value, stripAnnotations);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }
    }

    private List<Candidate>? FindExactCandidates(List<string> queryAliases)
    {
        foreach (string alias in queryAliases)
        {
            if (_byNormalized.TryGetValue(alias, out List<Candidate>? values))
            {
                return values;
            }
        }

        foreach (string alias in queryAliases)
        {
            if (_byToken.TryGetValue(CoverMatcher.BuildTokenKey(alias), out List<Candidate>? values))
            {
                return values;
            }
        }

        foreach (string alias in queryAliases)
        {
            string noSpace = CoverMatcher.BuildNoSpaceKey(alias);
            if (noSpace.Length >= 6
                && _byNoSpace.TryGetValue(noSpace, out List<Candidate>? values))
            {
                return values;
            }
        }

        return null;
    }

    private static double ScoreCandidate(List<string> queryAliases, Candidate candidate)
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

    private static Candidate? Choose(
        List<Candidate> candidates,
        CoverMatchOptions options,
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

        HashSet<string> regions = ParseRegions(options.Regions);
        string year = options.Year.Trim();
        Candidate? best = null;
        int bestRegion = -1;
        int bestYear = -1;
        int bestRank = int.MaxValue;
        int bestLength = int.MaxValue;

        foreach (Candidate candidate in candidates)
        {
            int regionScore = RegionScore(candidate, regions);
            int yearScore = string.Equals(candidate.Year, year, StringComparison.Ordinal) ? 1 : 0;
            if (best is null
                || regionScore > bestRegion
                || (regionScore == bestRegion
                    && (yearScore > bestYear
                        || (yearScore == bestYear
                            && (candidate.Rank < bestRank
                                || (candidate.Rank == bestRank
                                    && candidate.Name.Length < bestLength))))))
            {
                best = candidate;
                bestRegion = regionScore;
                bestYear = yearScore;
                bestRank = candidate.Rank;
                bestLength = candidate.Name.Length;
            }
        }

        if (!allowAmbiguous || best is null)
        {
            return best;
        }

        Candidate? second = null;
        foreach (Candidate candidate in candidates)
        {
            if (ReferenceEquals(candidate, best))
            {
                continue;
            }

            if (second is null
                || RegionScore(candidate, regions) > RegionScore(second, regions)
                || (RegionScore(candidate, regions) == RegionScore(second, regions)
                    && string.Equals(candidate.Year, year, StringComparison.Ordinal)
                       && !string.Equals(second.Year, year, StringComparison.Ordinal)))
            {
                second = candidate;
            }
        }

        if (second is null)
        {
            return best;
        }

        bool tied = RegionScore(best, regions) == RegionScore(second, regions)
                    && string.Equals(best.Year, second.Year, StringComparison.Ordinal)
                    && best.Rank == second.Rank;
        return tied ? null : best;
    }

    private static CoverMatch ToMatch(Candidate candidate, double score, string source)
    {
        return new CoverMatch(
            candidate.Path,
            candidate.Name,
            score,
            source,
            candidate.Regions,
            candidate.Year);
    }

    private static int RegionScore(Candidate candidate, HashSet<string> regions)
    {
        if (regions.Count == 0 || candidate.Regions.Length == 0)
        {
            return 0;
        }

        HashSet<string> candidateRegions = ParseRegions(candidate.Regions);
        return candidateRegions.Overlaps(regions) ? 1 : 0;
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

    private sealed record Candidate(
        string Path,
        string Name,
        IReadOnlyList<CoverNameKeys> Keys,
        string Regions,
        string Year,
        int Rank,
        IReadOnlyList<string> Aliases);

}
