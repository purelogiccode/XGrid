using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the byte-order texture formats: the SZ variants 0x3A-0x3C and their
/// linear (LU) twins 0x3F-0x41.
/// </summary>
/// <remarks>
/// These are the same 32-bit words as A8R8G8B8 with the channels permuted. They
/// are missing from the xemu 0.8.137 header but present in the newer fork
/// (0x3F = <c>NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8B8G8R8</c>). The corpus
/// found them the hard way: the xgu examples sample format 0x3F 3,420,787 times
/// and every sample missed, which renders their geometry untextured
/// (decisions 207h).
/// </remarks>
public sealed class Nv2aTextureByteOrderTests
{
    // One texel where every byte is distinct: A=0x11 B=0x22 G=0x33 R=0x44 in the
    // A8R8G8B8 reading, so any permutation is visible.
    private const uint Texel = 0x11223344u;

    private static float U(byte value) => value / 255f;

    [Theory]
    [InlineData(0x3Au)]
    [InlineData(0x3Bu)]
    [InlineData(0x3Cu)]
    [InlineData(0x3Fu)]
    [InlineData(0x40u)]
    [InlineData(0x41u)]
    public void ByteOrderFormats_AreFourBytesPerTexel(uint color)
    {
        Assert.Equal(4u, Nv2AMmio.TextureFormatBytesPerTexel(color));
    }

    [Theory]
    [InlineData(0x3Fu)]
    [InlineData(0x40u)]
    [InlineData(0x41u)]
    public void LinearTwinsAreReportedAsLinear(uint color)
    {
        Assert.True(Nv2AMmio.IsLinearTextureFormat(color));
    }

    [Theory]
    [InlineData(0x3Au)]
    [InlineData(0x3Bu)]
    [InlineData(0x3Cu)]
    public void SwizzledVariantsAreNotReportedAsLinear(uint color)
    {
        Assert.False(Nv2AMmio.IsLinearTextureFormat(color));
    }

    [Theory]
    [InlineData(0x17u, true)]
    [InlineData(0x28u, false)]
    [InlineData(0x29u, false)]
    public void G8B8R8B8_AreTwoByteFormats(uint color, bool linear)
    {
        Assert.Equal(2u, Nv2AMmio.TextureFormatBytesPerTexel(color));
        Assert.Equal(linear, Nv2AMmio.IsLinearTextureFormat(color));
    }

    /// <summary>
    /// G8B8/R8B8 are 16-bit two-channel words. xemu samples them as GL_RG8
    /// and swizzles (R, G, R, G) for G8B8 and (G, R, R, G) for R8B8, so the
    /// low byte is B (G8B8) / R (R8B8) and the high byte is the other one.
    /// `bumpenv_ripple` binds 0x28 for its derivative map and sampled it
    /// 3.9M times with every sample missing before this decode existed.
    /// </summary>
    [Theory]
    [InlineData(0x28u, 0x44, 0x33)]
    [InlineData(0x17u, 0x44, 0x33)]
    [InlineData(0x29u, 0x33, 0x44)]
    public void G8B8R8B8_SwizzleChannelsLikeTheReference(uint color, int expectedR, int expectedG)
    {
        // Texel = 0x11223344: low byte 0x44, high byte 0x33.
        Nv2AMmio.DecodeTextureTexel(color, Texel, out float r, out float g, out float b, out float a);

        Assert.Equal(U((byte)expectedR), r);
        Assert.Equal(U((byte)expectedG), g);
        Assert.Equal(r, b);
        Assert.Equal(g, a);
    }

    [Fact]
    public void A8B8G8R8_PutsRedInTheLowByte()
    {
        Nv2AMmio.DecodeTextureTexel(0x3Fu, Texel, out float r, out float g, out float b, out float a);

        Assert.Equal(U(0x44), r);
        Assert.Equal(U(0x33), g);
        Assert.Equal(U(0x22), b);
        Assert.Equal(U(0x11), a);
    }

    [Fact]
    public void B8G8R8A8_PutsAlphaInTheLowByte()
    {
        Nv2AMmio.DecodeTextureTexel(0x40u, Texel, out float r, out float g, out float b, out float a);

        Assert.Equal(U(0x33), r);
        Assert.Equal(U(0x22), g);
        Assert.Equal(U(0x11), b);
        Assert.Equal(U(0x44), a);
    }

    [Fact]
    public void R8G8B8A8_PutsRedInTheHighByte()
    {
        Nv2AMmio.DecodeTextureTexel(0x41u, Texel, out float r, out float g, out float b, out float a);

        Assert.Equal(U(0x11), r);
        Assert.Equal(U(0x22), g);
        Assert.Equal(U(0x33), b);
        Assert.Equal(U(0x44), a);
    }

    [Theory]
    [InlineData(0x3Au, 0x3Fu)]
    [InlineData(0x3Bu, 0x40u)]
    [InlineData(0x3Cu, 0x41u)]
    public void SwizzledAndLinearTwinsDecodeIdentically(uint swizzled, uint linear)
    {
        Nv2AMmio.DecodeTextureTexel(swizzled, Texel, out float sr, out float sg, out float sb, out float sa);
        Nv2AMmio.DecodeTextureTexel(linear, Texel, out float lr, out float lg, out float lb, out float la);

        Assert.Equal(sr, lr);
        Assert.Equal(sg, lg);
        Assert.Equal(sb, lb);
        Assert.Equal(sa, la);
    }

    [Fact]
    public void ByteOrderFormatsAreNotTheA8R8G8B8Layout()
    {
        // Guards the trap this slice fixed: routing one of these through the
        // fast path's A8R8G8B8 branch would silently swap red and blue.
        Nv2AMmio.DecodeTextureTexel(0x06u, Texel, out float br, out float bg, out float bb, out float ba);
        Nv2AMmio.DecodeTextureTexel(0x3Fu, Texel, out float r, out float g, out float b, out float a);

        Assert.Equal(U(0x22), br);
        Assert.Equal(U(0x44), bb);
        Assert.NotEqual(br, r);
        Assert.NotEqual(bb, b);

        // Green and alpha are in the same place for this pair.
        Assert.Equal(bg, g);
        Assert.Equal(ba, a);
    }

    [Fact]
    public void UnknownFormats_StillReportNoTexelWidth()
    {
        // The fix must not turn the sampler into a catch-all: an id with no
        // defined layout has to keep counting a format miss.
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x3Du));
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x2Fu));
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x7Fu));
    }
}
