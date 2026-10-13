namespace XGrid.GpuGl;

/// <summary>
/// Pure mapping from the software rasterizer's compact fragment-state codes
/// to OpenGL enum values and GLSL snippets (state -> GL-state mapping under
/// unit test; no GL context required). Semantics mirror
/// <c>Nv2AMmio</c>'s <c>MapBlendFactor</c>/<c>MapBlendEquation</c>/
/// <c>MapStencilOp</c>/<c>CompareFunc</c>/<c>BlendFactor</c> behavior
/// (facts from the software path + OpenGL core enum values).
/// </summary>
public static class GlStateMapper
{
    // OpenGL core enum values (numeric, so tests stay context-free).
    public const uint GlZero = 0x0000;
    public const uint GlOne = 0x0001;
    public const uint GlSrcColor = 0x0300;
    public const uint GlOneMinusSrcColor = 0x0301;
    public const uint GlSrcAlpha = 0x0302;
    public const uint GlOneMinusSrcAlpha = 0x0303;
    public const uint GlDstAlpha = 0x0304;
    public const uint GlOneMinusDstAlpha = 0x0305;
    public const uint GlDstColor = 0x0306;
    public const uint GlOneMinusDstColor = 0x0307;
    public const uint GlSrcAlphaSaturate = 0x0308;
    public const uint GlConstantColor = 0x8001;
    public const uint GlOneMinusConstantColor = 0x8002;
    public const uint GlConstantAlpha = 0x8003;
    public const uint GlOneMinusConstantAlpha = 0x8004;
    public const uint GlFuncAdd = 0x8006;
    public const uint GlMin = 0x8007;
    public const uint GlMax = 0x8008;
    public const uint GlFuncSubtract = 0x800A;
    public const uint GlFuncReverseSubtract = 0x800D;
    public const uint GlKeep = 0x1E00;
    public const uint GlReplace = 0x1E01;
    public const uint GlIncr = 0x1E02;
    public const uint GlDecr = 0x1E03;
    public const uint GlInvert = 0x150A;
    public const uint GlIncrWrap = 0x8507;
    public const uint GlDecrWrap = 0x8508;
    public const uint GlNever = 0x0200;
    public const uint GlLess = 0x0201;
    public const uint GlEqual = 0x0202;
    public const uint GlLequal = 0x0203;
    public const uint GlGreater = 0x0204;
    public const uint GlNotequal = 0x0205;
    public const uint GlGequal = 0x0206;
    public const uint GlAlways = 0x0207;

    /// <summary>
    /// Maps the post-<c>MapBlendFactor</c> compact factor index (the software
    /// path's <c>BlendFactor</c> switch selector) to the GL blend-factor enum
    /// with identical per-channel semantics (index 10 matches
    /// SRC_ALPHA_SATURATE's rgb=min(srcA,1-dstA), a=1 exactly).
    /// </summary>
    public static uint MapBlendFactor(uint index)
    {
        return index switch
        {
            0u => GlZero,
            2u => GlSrcColor,
            3u => GlOneMinusSrcColor,
            4u => GlSrcAlpha,
            5u => GlOneMinusSrcAlpha,
            6u => GlDstAlpha,
            7u => GlOneMinusDstAlpha,
            8u => GlDstColor,
            9u => GlOneMinusDstColor,
            10u => GlSrcAlphaSaturate,
            12u => GlConstantColor,
            13u => GlOneMinusConstantColor,
            14u => GlConstantAlpha,
            15u => GlOneMinusConstantAlpha,
            _ => GlOne,
        };
    }

    /// <summary>
    /// Maps the software blend-equation code to the GL equation enum. The
    /// software codes: 0=src*fs-dst*fd (SUBTRACT), 1=dst*fd-src*fs
    /// (REVERSE_SUBTRACT), 2/other=ADD, 3=MIN, 4=MAX.
    /// </summary>
    public static uint MapBlendEquation(uint equation)
    {
        return equation switch
        {
            0u => GlFuncSubtract,
            1u => GlFuncReverseSubtract,
            3u => GlMin,
            4u => GlMax,
            _ => GlFuncAdd,
        };
    }

    /// <summary>Maps the software stencil-op code (1-8) to the GL stencil-op enum.</summary>
    public static uint MapStencilOp(uint op)
    {
        return op switch
        {
            2u => GlZero,
            3u => GlReplace,
            4u => GlIncr,
            5u => GlDecr,
            6u => GlInvert,
            7u => GlIncrWrap,
            8u => GlDecrWrap,
            _ => GlKeep,
        };
    }

    /// <summary>
    /// Maps a software compare func (0-7, matching
    /// <c>Nv2AMmio.CompareFunc</c>) to the GL compare enum; values above 7
    /// behave as ALWAYS just like the software default.
    /// </summary>
    public static uint MapCompareFunc(uint func)
    {
        return func <= 7u ? GlNever + func : GlAlways;
    }

    /// <summary>
    /// Maps a compare func to the GLSL boolean expression used by the
    /// in-shader alpha test (<c>a OP ref</c>), mirroring
    /// <c>Nv2AMmio.CompareFunc</c>.
    /// </summary>
    public static string MapAlphaOperator(uint func)
    {
        return func switch
        {
            1u => "a < uAlphaRef",
            2u => "a == uAlphaRef",
            3u => "a <= uAlphaRef",
            4u => "a > uAlphaRef",
            5u => "a != uAlphaRef",
            6u => "a >= uAlphaRef",
            7u => "true",
            _ => "false",
        };
    }

    /// <summary>
    /// Maps the software color mask to per-channel GL writes. Software bits:
    /// 0x00010000=R, 0x00000100=G, 0x00000001=B, 0x01000000=A.
    /// </summary>
    public static void MapColorMask(uint mask, out bool red, out bool green, out bool blue, out bool alpha)
    {
        red = (mask & (1u << 16)) != 0;
        green = (mask & (1u << 8)) != 0;
        blue = (mask & 1u) != 0;
        alpha = (mask & (1u << 24)) != 0;
    }

    /// <summary>
    /// Decodes the software blend-constant word to the GL constant channels
    /// (the same ARGB unpack the software <c>BlendFactor</c> path uses).
    /// </summary>
    public static void MapBlendColor(uint blendColor, out float r, out float g, out float b, out float a)
    {
        r = ((blendColor >> 16) & 0xFFu) / 255f;
        g = ((blendColor >> 8) & 0xFFu) / 255f;
        b = (blendColor & 0xFFu) / 255f;
        a = ((blendColor >> 24) & 0xFFu) / 255f;
    }

    /// <summary>Decodes the clear-color word (AARRGGBB) to GL clear color.</summary>
    public static void MapClearColor(uint colorValue, out float r, out float g, out float b, out float a)
    {
        r = ((colorValue >> 16) & 0xFFu) / 255f;
        g = ((colorValue >> 8) & 0xFFu) / 255f;
        b = (colorValue & 0xFFu) / 255f;
        a = ((colorValue >> 24) & 0xFFu) / 255f;
    }

    /// <summary>
    /// Decodes the zeta clear word to the GL depth/stencil clear values with
    /// the exact field round-trip the software FillSurface store performs
    /// (Z24S8: depth=bits 8-31 / 16777215, stencil=bits 0-7; Z16: depth=
    /// bits 0-15 / 65535).
    /// </summary>
    public static void MapZetaClear(uint zetaValue, uint zetaBytes, out double depth, out int stencil)
    {
        if (zetaBytes == 4u)
        {
            uint field = (zetaValue >> 8) & 0xFFFFFFu;
            depth = field / 16777215.0;
            stencil = (int)(zetaValue & 0xFFu);
            return;
        }

        if (zetaBytes == 2u)
        {
            depth = (zetaValue & 0xFFFFu) / 65535.0;
            stencil = 0;
            return;
        }

        depth = 0.0;
        stencil = 0;
    }
}
