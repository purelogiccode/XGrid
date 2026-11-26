using System;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Shared defaults for local and online cover art. The online catalog uses the
/// same static, content-addressed layout as the 5DO catalog: a JSON index plus
/// immutable WebP images on a public Cloudflare R2 custom domain.
/// </summary>
public static class CoverDefaults
{
    /// <summary>The public base URL that hosts the XGrid cover catalog and images.</summary>
    public const string BaseUrl = "https://assets.purelogiccode.com/xgrid/v1";

    /// <summary>The default catalog URL.</summary>
    public const string CatalogUrl = BaseUrl + "/catalog.json";

    /// <summary>The catalog schema version understood by this build.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The default Jaro-Winkler threshold for fuzzy cover matching.</summary>
    public const double DefaultFuzzyThreshold = 0.85;

    /// <summary>The smallest selectable fuzzy threshold.</summary>
    public const double MinFuzzyThreshold = 0.70;

    /// <summary>The largest selectable fuzzy threshold.</summary>
    public const double MaxFuzzyThreshold = 0.95;

    /// <summary>The maximum accepted catalog size.</summary>
    public const long MaxCatalogBytes = 5L * 1024 * 1024;

    /// <summary>The maximum accepted image size.</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>The maximum accepted bulk-pack size.</summary>
    public const long MaxPackBytes = 512L * 1024 * 1024;

    /// <summary>Gets the default local covers folder under the shell data directory.</summary>
    public static string LocalDirectory => Path.Combine(SettingsStore.ResolveDirectory(), "covers");

    /// <summary>Gets the default online-cover cache under the shell data directory.</summary>
    public static string CacheDirectory => Path.Combine(SettingsStore.ResolveDirectory(), "covers-cache");

    /// <summary>Clamps a fuzzy threshold to the supported range.</summary>
    /// <param name="value">Requested threshold.</param>
    /// <returns>A finite value between the supported bounds.</returns>
    public static double ClampThreshold(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, MinFuzzyThreshold, MaxFuzzyThreshold)
            : DefaultFuzzyThreshold;
    }

    /// <summary>Normalizes a catalog URL or base URL.</summary>
    /// <param name="value">Configured URL or base URL.</param>
    /// <returns>The absolute catalog URL to use.</returns>
    public static string NormalizeCatalogUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CatalogUrl;
        }

        string text = value.Trim();
        if (!text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.TrimEnd('/') + "/catalog.json";
        }

        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
               && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   || uri.IsFile)
            ? uri.AbsoluteUri
            : CatalogUrl;
    }
}
