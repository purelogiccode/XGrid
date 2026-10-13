using System;
using Xunit;

namespace XGrid.GpuGl.Tests;

/// <summary>State → GL-state mapping goldens (pure C#, no GL context).</summary>
public sealed class GlStateMapperTests
{
    [Theory]
    [InlineData(0u, GlStateMapper.GlZero)]
    [InlineData(1u, GlStateMapper.GlOne)]
    [InlineData(2u, GlStateMapper.GlSrcColor)]
    [InlineData(3u, GlStateMapper.GlOneMinusSrcColor)]
    [InlineData(4u, GlStateMapper.GlSrcAlpha)]
    [InlineData(5u, GlStateMapper.GlOneMinusSrcAlpha)]
    [InlineData(6u, GlStateMapper.GlDstAlpha)]
    [InlineData(7u, GlStateMapper.GlOneMinusDstAlpha)]
    [InlineData(8u, GlStateMapper.GlDstColor)]
    [InlineData(9u, GlStateMapper.GlOneMinusDstColor)]
    [InlineData(10u, GlStateMapper.GlSrcAlphaSaturate)]
    [InlineData(11u, GlStateMapper.GlOne)]
    [InlineData(12u, GlStateMapper.GlConstantColor)]
    [InlineData(13u, GlStateMapper.GlOneMinusConstantColor)]
    [InlineData(14u, GlStateMapper.GlConstantAlpha)]
    [InlineData(15u, GlStateMapper.GlOneMinusConstantAlpha)]
    [InlineData(99u, GlStateMapper.GlOne)]
    public void BlendFactor_MapsSoftwareIndexToGlEnum(uint index, uint expected)
    {
        Assert.Equal(expected, GlStateMapper.MapBlendFactor(index));
    }

    [Theory]
    [InlineData(0u, GlStateMapper.GlFuncSubtract)]
    [InlineData(1u, GlStateMapper.GlFuncReverseSubtract)]
    [InlineData(2u, GlStateMapper.GlFuncAdd)]
    [InlineData(3u, GlStateMapper.GlMin)]
    [InlineData(4u, GlStateMapper.GlMax)]
    [InlineData(9u, GlStateMapper.GlFuncAdd)]
    public void BlendEquation_MapsSoftwareCodeToGlEnum(uint equation, uint expected)
    {
        Assert.Equal(expected, GlStateMapper.MapBlendEquation(equation));
    }

    [Theory]
    [InlineData(1u, GlStateMapper.GlKeep)]
    [InlineData(2u, GlStateMapper.GlZero)]
    [InlineData(3u, GlStateMapper.GlReplace)]
    [InlineData(4u, GlStateMapper.GlIncr)]
    [InlineData(5u, GlStateMapper.GlDecr)]
    [InlineData(6u, GlStateMapper.GlInvert)]
    [InlineData(7u, GlStateMapper.GlIncrWrap)]
    [InlineData(8u, GlStateMapper.GlDecrWrap)]
    [InlineData(0u, GlStateMapper.GlKeep)]
    [InlineData(42u, GlStateMapper.GlKeep)]
    public void StencilOp_MapsSoftwareCodeToGlEnum(uint op, uint expected)
    {
        Assert.Equal(expected, GlStateMapper.MapStencilOp(op));
    }

    [Theory]
    [InlineData(0u, GlStateMapper.GlNever)]
    [InlineData(1u, GlStateMapper.GlLess)]
    [InlineData(2u, GlStateMapper.GlEqual)]
    [InlineData(3u, GlStateMapper.GlLequal)]
    [InlineData(4u, GlStateMapper.GlGreater)]
    [InlineData(5u, GlStateMapper.GlNotequal)]
    [InlineData(6u, GlStateMapper.GlGequal)]
    [InlineData(7u, GlStateMapper.GlAlways)]
    [InlineData(8u, GlStateMapper.GlAlways)]
    public void CompareFunc_MapsSoftwareCodeToGlEnum(uint func, uint expected)
    {
        Assert.Equal(expected, GlStateMapper.MapCompareFunc(func));
    }

    [Theory]
    [InlineData(0u, "false")]
    [InlineData(1u, "a < uAlphaRef")]
    [InlineData(2u, "a == uAlphaRef")]
    [InlineData(3u, "a <= uAlphaRef")]
    [InlineData(4u, "a > uAlphaRef")]
    [InlineData(5u, "a != uAlphaRef")]
    [InlineData(6u, "a >= uAlphaRef")]
    [InlineData(7u, "true")]
    public void AlphaOperator_MapsSoftwareCompareFunc(uint func, string expected)
    {
        Assert.Equal(expected, GlStateMapper.MapAlphaOperator(func));
    }

    [Fact]
    public void ColorMask_MapsSoftwareChannelBits()
    {
        // All channels off.
        GlStateMapper.MapColorMask(0u, out bool r, out bool g, out bool b, out bool a);
        Assert.False(r);
        Assert.False(g);
        Assert.False(b);
        Assert.False(a);

        // R=bit16, G=bit8, B=bit0, A=bit24 (software ProcessFragment layout);
        // 0x01010101 is ColorMaskAll — every channel on.
        GlStateMapper.MapColorMask(0x01010101u, out r, out g, out b, out a);
        Assert.True(r);
        Assert.True(g);
        Assert.True(b);
        Assert.True(a);

        GlStateMapper.MapColorMask(0x00010000u, out r, out g, out b, out a);
        Assert.True(r);
        Assert.False(g);
        Assert.False(b);
        Assert.False(a);

        GlStateMapper.MapColorMask(0xFF000000u, out r, out g, out b, out a);
        Assert.False(r);
        Assert.False(g);
        Assert.False(b);
        Assert.True(a);
    }

    [Fact]
    public void BlendColor_MatchesSoftwareArgbUnpack()
    {
        // Software: (bits16-23=R, 8-15=G, 0-7=B, 24-31=A).
        GlStateMapper.MapBlendColor(0x80402010u, out float r, out float g, out float b, out float a);
        Assert.Equal(0x40 / 255f, r);
        Assert.Equal(0x20 / 255f, g);
        Assert.Equal(0x10 / 255f, b);
        Assert.Equal(0x80 / 255f, a);
    }

    [Fact]
    public void ZetaClear_Z24S8_RoundTripsDepthAndStencil()
    {
        // depth field k -> normalized k/16777215 must round-trip through the
        // GL fixed-point conversion (the exact store the software path does).
        uint zetaValue = (123456u << 8) | 0xABu;
        GlStateMapper.MapZetaClear(zetaValue, 4u, out double depth, out int stencil);
        Assert.Equal(0xAB, stencil);
        Assert.Equal(123456u / 16777215.0, depth);
        Assert.Equal(123456L, (long)Math.Round(depth * 16777215.0));
    }

    [Fact]
    public void ZetaClear_Z16_MapsLowHalfToDepth()
    {
        GlStateMapper.MapZetaClear(0x0000F00Au, 2u, out double depth, out int stencil);
        Assert.Equal(0, stencil);
        Assert.Equal(0xF00Au / 65535.0, depth);
    }

    [Fact]
    public void ClearColor_MatchesSoftwareWordLayout()
    {
        // A8R8G8B8 clear word: A in bits 24-31, R 16-23, G 8-15, B 0-7.
        GlStateMapper.MapClearColor(0xFF112233u, out float r, out float g, out float b, out float a);
        Assert.Equal(0x11 / 255f, r);
        Assert.Equal(0x22 / 255f, g);
        Assert.Equal(0x33 / 255f, b);
        Assert.Equal(1f, a);
    }
}
