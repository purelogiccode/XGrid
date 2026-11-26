using System;
using System.Globalization;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>Local and online cover-art options.</summary>
public sealed class CoverSettings
{
    /// <summary>Gets or sets the local covers folder, or empty for the default.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the online catalog may be used.</summary>
    public bool UseOnline { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether missing covers are downloaded automatically.</summary>
    public bool AutoDownload { get; set; } = true;

    /// <summary>Gets or sets the online catalog URL.</summary>
    public string CatalogUrl { get; set; } = CoverDefaults.CatalogUrl;

    /// <summary>Gets or sets a value indicating whether fuzzy title matching is enabled.</summary>
    public bool FuzzyMatching { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether annotation groups are stripped.</summary>
    public bool StripAnnotations { get; set; } = true;

    /// <summary>Gets or sets the Jaro-Winkler fuzzy-match threshold.</summary>
    public double FuzzyThreshold { get; set; } = CoverDefaults.DefaultFuzzyThreshold;

    /// <summary>Creates an independent copy.</summary>
    /// <returns>A copy with the same values.</returns>
    public CoverSettings Clone()
    {
        return new CoverSettings
        {
            Directory = Directory,
            UseOnline = UseOnline,
            AutoDownload = AutoDownload,
            CatalogUrl = CatalogUrl,
            FuzzyMatching = FuzzyMatching,
            StripAnnotations = StripAnnotations,
            FuzzyThreshold = FuzzyThreshold,
        };
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        Directory = Directory?.Trim() ?? string.Empty;
        CatalogUrl = CoverDefaults.NormalizeCatalogUrl(CatalogUrl);
        FuzzyThreshold = CoverDefaults.ClampThreshold(FuzzyThreshold);
    }
}

/// <summary>Accumulated play state for one library title.</summary>
public sealed class LibraryPlayRecord
{
    /// <summary>Gets or sets the accumulated play time in seconds.</summary>
    public long Seconds { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last launch.</summary>
    public DateTime? LastPlayedUtc { get; set; }

    /// <summary>Gets or sets the number of times the title was launched.</summary>
    public int LaunchCount { get; set; }

    /// <summary>Creates an independent copy.</summary>
    /// <returns>A copy with the same values.</returns>
    public LibraryPlayRecord Clone()
    {
        return new LibraryPlayRecord
        {
            Seconds = Seconds,
            LastPlayedUtc = LastPlayedUtc,
            LaunchCount = LaunchCount,
        };
    }

    /// <summary>Formats the accumulated time for the library views.</summary>
    /// <returns>A compact label such as <c>2h 05m</c>, or an empty string.</returns>
    public string FormatPlaytime()
    {
        if (Seconds <= 0)
        {
            return string.Empty;
        }

        if (Seconds >= 3600)
        {
            return (Seconds / 3600).ToString(CultureInfo.InvariantCulture)
                   + "h " + (Seconds % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "m";
        }

        return Seconds >= 60
            ? (Seconds / 60).ToString(CultureInfo.InvariantCulture) + "m"
            : Seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }
}
