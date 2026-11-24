using System;
using System.Collections.Generic;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the presentation filters: the sampling-only filters never change pixels,
/// the masks darken whole rows and the normalization repairs persisted keys.
/// </summary>
public sealed class FrameFiltersTests
{
    [Theory]
    [InlineData(FrameFilters.Nearest)]
    [InlineData(FrameFilters.Bilinear)]
    public void SamplingFiltersLeaveTheFrameUntouched(string filter)
    {
        byte[] pixels = Frame4X2(255);

        FrameFilters.Apply(filter, pixels, 4, 2);

        Assert.All(pixels, value => Assert.Equal(255, value));
    }

    [Fact]
    public void ScanlineDarkensEveryOtherRowOnly()
    {
        byte[] pixels = Frame4X4(255);

        FrameFilters.Apply(FrameFilters.Scanline, pixels, 4, 4);

        Assert.Equal(255, pixels[0]);            // row 0 untouched
        Assert.Equal(153, pixels[(1 * 4 + 0) * 4]); // row 1 darkened (255 * 3/5)
        Assert.Equal(255, pixels[(2 * 4 + 0) * 4]); // row 2 untouched
        Assert.Equal(153, pixels[(3 * 4 + 0) * 4]); // row 3 darkened
    }

    [Fact]
    public void CrtAddsThePhosphorColumnMask()
    {
        byte[] pixels = Frame3X2(255);

        FrameFilters.Apply(FrameFilters.Crt, pixels, 3, 2);

        // BGRA order. Row 0 is even, so only the column triad applies:
        // x=0 darkens blue, x=1 green, x=2 red.
        Assert.Equal(204, pixels[0]);            // x=0 blue
        Assert.Equal(255, pixels[1]);            // x=0 green
        Assert.Equal(204, pixels[(1 * 4) + 1]);  // x=1 green
        Assert.Equal(204, pixels[(2 * 4) + 2]);  // x=2 red
    }

    [Fact]
    public void NormalizeRepairsAnUnknownFilter()
    {
        Assert.Equal(FrameFilters.Nearest, FrameFilters.Normalize("vhs"));
        Assert.Equal(FrameFilters.Nearest, FrameFilters.Normalize(null));
        Assert.Equal(FrameFilters.Crt, FrameFilters.Normalize(FrameFilters.Crt));
        Assert.True(FrameFilters.UsesBilinearSampling(FrameFilters.Bilinear));
        Assert.False(FrameFilters.UsesBilinearSampling(FrameFilters.Scanline));
    }

    [Fact]
    public void EverySelectableFilterNormalizesToItself()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string key, string label) in FrameFilters.All)
        {
            Assert.True(keys.Add(key), "Duplicate filter key: " + key);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.Equal(key, FrameFilters.Normalize(key));
        }
    }

    [Fact]
    public void OnlyBilinearSelectsSmoothSampling()
    {
        foreach ((string key, _) in FrameFilters.All)
        {
            Assert.Equal(
                string.Equals(key, FrameFilters.Bilinear, StringComparison.Ordinal),
                FrameFilters.UsesBilinearSampling(key));
        }
    }

    [Fact]
    public void ScanlineMaskPreservesAlpha()
    {
        byte[] pixels = Frame(4, 4, 255);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 17;
        }

        FrameFilters.Apply(FrameFilters.Scanline, pixels, 4, 4);

        for (int i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(17, pixels[i]);
        }
    }

    [Fact]
    public void CrtAppliesTheScanlineAndPhosphorMasksTogether()
    {
        byte[] pixels = Frame3X3(255);

        FrameFilters.Apply(FrameFilters.Crt, pixels, 3, 3);

        // Row 1 gets the scanline reduction (153) and then the column triad:
        // x=0 darkens blue, x=1 green, x=2 red.
        Assert.Equal(122, pixels[(1 * 3 + 0) * 4]);
        Assert.Equal(153, pixels[((1 * 3 + 0) * 4) + 1]);
        Assert.Equal(153, pixels[((1 * 3 + 0) * 4) + 2]);
        Assert.Equal(153, pixels[((1 * 3 + 1) * 4) + 0]);
        Assert.Equal(122, pixels[((1 * 3 + 1) * 4) + 1]);
        Assert.Equal(122, pixels[((1 * 3 + 2) * 4) + 2]);

        // Row 2 is even, so it only receives the column triad.
        Assert.Equal(204, pixels[(2 * 3) * 4]);
    }

    [Fact]
    public void MalformedFrameIsLeftUnfiltered()
    {
        byte[] pixels = Frame(4, 4, 255);
        byte[] tooShort = pixels[..10];

        FrameFilters.Apply(FrameFilters.Crt, tooShort, 4, 4);

        Assert.All(tooShort, value => Assert.Equal(255, value));
    }

    [Fact]
    public void ZeroOrNegativeGeometryIsSafe()
    {
        byte[] pixels = Frame4X2(255);

        FrameFilters.Apply(FrameFilters.Crt, pixels, 0, 2);
        FrameFilters.Apply(FrameFilters.Crt, pixels, 4, 0);
        FrameFilters.Apply(FrameFilters.Crt, pixels, -1, 2);

        Assert.All(pixels, value => Assert.Equal(255, value));
    }

    private static byte[] Frame4X2(byte value)
    {
        return Frame(4, 2, value);
    }

    private static byte[] Frame4X4(byte value)
    {
        return Frame(4, 4, value);
    }

    private static byte[] Frame3X2(byte value)
    {
        return Frame(3, 2, value);
    }

    private static byte[] Frame3X3(byte value)
    {
        return Frame(3, 3, value);
    }

    private static byte[] Frame(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }
}
