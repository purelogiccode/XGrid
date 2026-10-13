using System;
using System.Runtime.InteropServices;

namespace XGrid.Graphics;

/// <summary>
/// Golden-frame comparison under the design doc §3.4 diff policy: flat
/// regions (both frames locally constant) must be byte-exact; differences
/// are allowed only within a ±1 px band of a software-detected edge and
/// only with per-channel tolerance ≤ 2 (the same ±2 tolerance xemu's GL
/// driver self-test uses for a known texel).
/// </summary>
public static class FrameDiff
{
    /// <summary>Result of a golden-frame comparison.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Result
    {
        public Result(int width, int height, int diffPixels, int edgeBandPixels, int violations, int maxChannelDelta, int rgbDiffPixels)
        {
            Width = width;
            Height = height;
            DiffPixels = diffPixels;
            EdgeBandPixels = edgeBandPixels;
            Violations = violations;
            MaxChannelDelta = maxChannelDelta;
            RgbDiffPixels = rgbDiffPixels;
        }

        /// <summary>Gets the frame width.</summary>
        public int Width { get; }

        /// <summary>Gets the frame height.</summary>
        public int Height { get; }

        /// <summary>Gets the number of pixels that differ at all.</summary>
        public int DiffPixels { get; }

        /// <summary>Gets the number of differing pixels inside the ±1 px edge band.</summary>
        public int EdgeBandPixels { get; }

        /// <summary>Gets the number of policy violations (must be 0 to pass).</summary>
        public int Violations { get; }

        /// <summary>Gets the maximum per-channel delta seen anywhere.</summary>
        public int MaxChannelDelta { get; }

        /// <summary>Gets the number of differing pixels whose RGB (ignoring alpha) changed.</summary>
        public int RgbDiffPixels { get; }

        /// <summary>Gets whether the pair satisfies the §3.4 policy.</summary>
        public bool Passed => Violations == 0;

        /// <inheritdoc />
        public override string ToString() =>
            $"diff={DiffPixels} rgbDiff={RgbDiffPixels} edgeBand={EdgeBandPixels} violations={Violations} maxDelta={MaxChannelDelta} {Width}x{Height}";
    }

    /// <summary>
    /// Compares two BGRA frames of equal size under the §3.4 policy.
    /// The reference frame (software) defines the edge map: an edge is a
    /// pixel with a 4-neighbor whose bytes differ at all; the band is the
    /// edge plus its Moore neighborhood.
    /// </summary>
    public static Result Compare(ReadOnlySpan<byte> reference, ReadOnlySpan<byte> test, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        int pixelBytes = checked(width * height * 4);
        if (reference.Length != pixelBytes || test.Length != pixelBytes)
        {
            throw new ArgumentException("Frame sizes do not match dimensions.", nameof(test));
        }

        int diff = 0;
        int rgbDiff = 0;
        int bandDiff = 0;
        int violations = 0;
        int maxDelta = 0;
        var edge = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = ((y * width) + x) * 4;
                bool isEdge = false;
                if (x + 1 < width && !SamePixel(reference, p, p + 4))
                {
                    isEdge = true;
                }

                if (!isEdge && x > 0 && !SamePixel(reference, p, p - 4))
                {
                    isEdge = true;
                }

                if (!isEdge && y + 1 < height && !SamePixel(reference, p, p + (width * 4)))
                {
                    isEdge = true;
                }

                if (!isEdge && y > 0 && !SamePixel(reference, p, p - (width * 4)))
                {
                    isEdge = true;
                }

                edge[(y * width) + x] = isEdge;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = ((y * width) + x) * 4;
                bool same = reference[p] == test[p] &&
                    reference[p + 1] == test[p + 1] &&
                    reference[p + 2] == test[p + 2] &&
                    reference[p + 3] == test[p + 3];
                if (same)
                {
                    continue;
                }

                diff++;
                if (reference[p] != test[p] ||
                    reference[p + 1] != test[p + 1] ||
                    reference[p + 2] != test[p + 2])
                {
                    rgbDiff++;
                }

                int pixelMax = Math.Max(
                    Math.Max(
                        Math.Abs(reference[p] - test[p]),
                        Math.Abs(reference[p + 1] - test[p + 1])),
                    Math.Abs(reference[p + 2] - test[p + 2]));
                pixelMax = Math.Max(pixelMax, Math.Abs(reference[p + 3] - test[p + 3]));
                maxDelta = Math.Max(maxDelta, pixelMax);

                bool inBand = InEdgeBand(edge, width, height, x, y);
                if (inBand)
                {
                    bandDiff++;
                    if (pixelMax > 2)
                    {
                        violations++;
                    }
                }
                else
                {
                    violations++;
                }
            }
        }

        return new Result(width, height, diff, bandDiff, violations, maxDelta, rgbDiff);
    }

    private static bool SamePixel(ReadOnlySpan<byte> frame, int a, int b)
    {
        return frame[a] == frame[b] &&
            frame[a + 1] == frame[b + 1] &&
            frame[a + 2] == frame[b + 2] &&
            frame[a + 3] == frame[b + 3];
    }

    private static bool InEdgeBand(bool[] edge, int width, int height, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= height)
            {
                continue;
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                if (nx >= 0 && nx < width && edge[(ny * width) + nx])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
