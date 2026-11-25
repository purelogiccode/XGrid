using System;
using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>
/// The presentation filters the core implements in its final blit shader. The
/// shell only selects one; it never processes picture pixels.
/// </summary>
public static class XemuPresentationFilter
{
    /// <summary>Filter key: nearest-neighbour, the sharpest and cheapest.</summary>
    public const string Nearest = "nearest";

    /// <summary>Filter key: bilinear upscale.</summary>
    public const string Bilinear = "bilinear";

    /// <summary>Filter key: nearest plus a scanline mask.</summary>
    public const string Scanline = "scanline";

    /// <summary>Filter key: scanline plus a CRT phosphor mask.</summary>
    public const string Crt = "crt";

    /// <summary>Gets the selectable filters, in display order.</summary>
    public static IReadOnlyList<(string Key, string Label)> All { get; } =
    [
        (Nearest, "Nearest (sharp)"),
        (Bilinear, "Bilinear (smooth)"),
        (Scanline, "Scanlines"),
        (Crt, "CRT mask"),
    ];

    /// <summary>Repairs a filter key loaded from settings.</summary>
    /// <param name="key">Persisted key.</param>
    /// <returns>A key this build implements.</returns>
    public static string Normalize(string? key)
    {
        foreach ((string candidate, _) in All)
        {
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return Nearest;
    }

    /// <summary>
    /// Maps a filter key to the value the core's ABI expects.
    /// </summary>
    /// <param name="key">Filter key.</param>
    /// <returns>The core value, or -1 for an unknown key.</returns>
    public static int ToCoreValue(string? key)
    {
        return NormalizeOrUnknown(key) switch
        {
            Nearest => 0,
            Bilinear => 1,
            Scanline => 2,
            Crt => 3,
            _ => -1,
        };
    }

    private static string NormalizeOrUnknown(string? key)
    {
        foreach ((string candidate, _) in All)
        {
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
