using System;
using System.Collections.Generic;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// The presentation filters the shell can apply to a core frame. The set is
/// data: a filter is a key, a label and a pixel policy, so more can be added
/// without touching the core.
/// </summary>
public static class FrameFilters
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

    /// <summary>Returns true when the filter needs bilinear sampling at presentation.</summary>
    /// <param name="key">Filter key.</param>
    public static bool UsesBilinearSampling(string? key)
    {
        return string.Equals(Normalize(key), Bilinear, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies the filter's pixel policy in place. Nearest and bilinear are
    /// sampling policies (the control does the scaling); the masks are applied
    /// here, at the source resolution, so they stay stable at every window size.
    /// </summary>
    /// <param name="key">Filter key.</param>
    /// <param name="bgra">Top-down BGRA pixels, <c>width * height * 4</c> bytes.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    public static void Apply(string? key, byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        string filter = Normalize(key);
        if (string.Equals(filter, Nearest, StringComparison.Ordinal)
            || string.Equals(filter, Bilinear, StringComparison.Ordinal)
            || width <= 0
            || height <= 0)
        {
            return;
        }

        bool phosphor = string.Equals(filter, Crt, StringComparison.Ordinal);
        for (int y = 1; y < height; y += 2)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int i = row + (x * 4);
                if (i + 2 >= bgra.Length)
                {
                    return;
                }

                // Scanline: every other row keeps about half its light.
                bgra[i] = (byte)(bgra[i] * 3 / 5);
                bgra[i + 1] = (byte)(bgra[i + 1] * 3 / 5);
                bgra[i + 2] = (byte)(bgra[i + 2] * 3 / 5);
            }
        }

        if (!phosphor)
        {
            return;
        }

        // CRT phosphor mask: a repeating R/G/B column triad, subtle enough to
        // read as an aperture grille rather than a colour cast.
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int i = row + (x * 4);
                if (i + 2 >= bgra.Length)
                {
                    return;
                }

                switch (x % 3)
                {
                    case 0:
                        bgra[i] = (byte)(bgra[i] * 4 / 5);
                        break;
                    case 1:
                        bgra[i + 1] = (byte)(bgra[i + 1] * 4 / 5);
                        break;
                    default:
                        bgra[i + 2] = (byte)(bgra[i + 2] * 4 / 5);
                        break;
                }
            }
        }
    }
}
