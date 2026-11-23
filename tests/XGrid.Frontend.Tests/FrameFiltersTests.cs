using System;
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
        byte[] pixels = Frame4x2(255);

        FrameFilters.Apply(filter, pixels, 4, 2);

        Assert.All(pixels, value => Assert.Equal(255, value));
    }

    [Fact]
    public void ScanlineDarkensEveryOtherRowOnly()
    {
        byte[] pixels = Frame4x4(255);

        FrameFilters.Apply(FrameFilters.Scanline, pixels, 4, 4);

        Assert.Equal(255, pixels[0]);            // row 0 untouched
        Assert.Equal(153, pixels[(1 * 4 + 0) * 4]); // row 1 darkened (255 * 3/5)
        Assert.Equal(255, pixels[(2 * 4 + 0) * 4]); // row 2 untouched
        Assert.Equal(153, pixels[(3 * 4 + 0) * 4]); // row 3 darkened
    }

    [Fact]
    public void CrtAddsThePhosphorColumnMask()
    {
        byte[] pixels = Frame3x2(255);

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

    private static byte[] Frame4x2(byte value)
    {
        return Frame(4, 2, value);
    }

    private static byte[] Frame4x4(byte value)
    {
        return Frame(4, 4, value);
    }

    private static byte[] Frame3x2(byte value)
    {
        return Frame(3, 2, value);
    }

    private static byte[] Frame(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, value);
        return pixels;
    }
}
