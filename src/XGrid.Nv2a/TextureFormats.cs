// SPDX-License-Identifier: LGPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/nv2a/pgraph/texture.c,
//   hw/xbox/nv2a/pgraph/gl/constants.h and hw/xbox/nv2a/pgraph/util.h.
/*
 * QEMU Geforce NV2A implementation
 *
 * Copyright (c) 2012 espes
 * Copyright (c) 2015 Jannik Vogel
 * Copyright (c) 2018-2025 Matt Borgerson
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, see <http://www.gnu.org/licenses/>.
 */

using System;

namespace XGrid.Nv2a;

/// <summary>
/// The NV097 texture color-format table: bytes per texel, layout (linear vs
/// swizzled), the depth flag and the decoders the software sampler needs for
/// formats the 2026-10 corpus does not exercise. Transcribed from xemu
/// <c>kelvin_color_format_info_map</c> (texture.c), <c>kelvin_color_format_gl_map</c>
/// (gl/constants.h) and the <c>pgraph_convert_texture_data</c> converters.
/// </summary>
/// <remarks>
/// The reference's <c>bytes_per_pixel</c> for a compressed format is 4 (the
/// decompressed texel), but the compressed block array is addressed by block,
/// not by texel: <see cref="BytesPerTexel"/> keeps the reference value for
/// callers that ask about the decoded layout, while <see cref="IsCompressed"/>
/// tells the sampler to take the block path.
/// </remarks>
public static class TextureFormats
{
    /// <summary>NV097 <c>SZ_I8_A8R8G8B8</c>: one index byte, palette lookup.</summary>
    public const uint PalettedI8 = 0x0Bu;

    /// <summary>NV097 <c>LC_IMAGE_CR8YB8CB8YA8</c> (YUY2).</summary>
    public const uint Yuy2 = 0x24u;

    /// <summary>NV097 <c>LC_IMAGE_YB8CR8YA8CB8</c> (UYVY).</summary>
    public const uint Uyvy = 0x25u;

    /// <summary>Gets whether a color format is the paletted I8 format.</summary>
    /// <param name="color">Texture color-format id (FORMAT bits 8-15).</param>
    /// <returns>True for <c>SZ_I8_A8R8G8B8</c>.</returns>
    public static bool IsPaletted(uint color)
    {
        return color == PalettedI8;
    }

    /// <summary>Gets whether a color format is one of the two linear YUV formats.</summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>True for YUY2/UYVY.</returns>
    public static bool IsYuv(uint color)
    {
        return color is Yuy2 or Uyvy;
    }

    /// <summary>Gets whether a color format is an S3TC block format.</summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>True for DXT1/DXT3/DXT5.</returns>
    public static bool IsCompressed(uint color)
    {
        return S3tcDecoder.IsDxtFormat(color);
    }

    /// <summary>
    /// Gets whether a texel is decoded from the 1/2/4-byte word at its address
    /// (the generic sampler's word lane) rather than a block, palette or YUV
    /// neighbour read.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>True when the word lane can decode the format.</returns>
    public static bool IsWordDecodable(uint color)
    {
        return !IsPaletted(color) && !IsCompressed(color) && !IsYuv(color);
    }

    /// <summary>
    /// Bytes per texel for a color format, matching xemu's
    /// <c>kelvin_color_format_info_map</c>: 1, 2 or 4 for a backed format, 0
    /// for an id the reference table does not define (the sampler counts a
    /// format miss) and for the depth formats, which sample through the shadow
    /// path (<see cref="Nv2AMmio.IsDepthTextureFormat"/>).
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>1, 2, 4 or 0.</returns>
    public static uint BytesPerTexel(uint color)
    {
        return color switch
        {
            0x00u or 0x01u or 0x0Bu or 0x13u or 0x19u or 0x1Bu or 0x1Fu => 1u,
            0x02u or 0x03u or 0x04u or 0x05u or 0x10u or 0x11u or 0x17u or 0x1Au or 0x1Cu or 0x1Du
                or 0x20u or 0x24u or 0x25u or 0x27u or 0x28u or 0x29u or 0x32u or 0x35u => 2u,
            0x06u or 0x07u or 0x0Cu or 0x0Eu or 0x0Fu or 0x12u or 0x1Eu or 0x3Au or 0x3Bu or 0x3Cu
                or 0x3Fu or 0x40u or 0x41u => 4u,
            _ => 0u,
        };
    }

    /// <summary>
    /// Gets whether a color format uses the linear (pitched) layout instead of
    /// the swizzled one. From xemu's <c>kelvin_color_format_info_map</c>
    /// <c>linear</c> flag; the compressed formats are block-linear but are
    /// handled by the block path, not the pitch walk.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>True for the LU/LC linear formats.</returns>
    public static bool IsLinear(uint color)
    {
        return color is 0x10u or 0x11u or 0x12u or 0x13u or 0x17u or 0x1Bu or 0x1Cu or 0x1Du or 0x1Eu
            or 0x1Fu or 0x20u or 0x24u or 0x25u or 0x2Eu or 0x2Fu or 0x30u or 0x31u or 0x35u or 0x3Fu
            or 0x40u or 0x41u;
    }

    /// <summary>
    /// Decodes the formats the reference's GL map defines that the original
    /// A8R8G8B8/R5G6B5 switch did not cover: Y8, AY8, A8, A8Y8, Y16 and the
    /// signed R6G5B5. The returned channels are the reference GL sample:
    /// the channel swizzle in <c>kelvin_color_format_gl_map</c> is applied
    /// literally (e.g. Y8 is <c>(R, R, R, 1)</c>, A8 is <c>(1, 1, 1, R)</c>).
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="texel">Raw 1- or 2-byte texel word (low bits).</param>
    /// <param name="r">Decoded red.</param>
    /// <param name="g">Decoded green.</param>
    /// <param name="b">Decoded blue.</param>
    /// <param name="a">Decoded alpha.</param>
    /// <returns>True when this table decodes the format.</returns>
    public static bool TryDecodeWord(uint color, uint texel, out float r, out float g, out float b, out float a)
    {
        switch (color)
        {
            case 0x00u: // SZ_Y8
            case 0x13u: // LU_IMAGE_Y8
                r = g = b = (texel & 0xFFu) / 255f;
                a = 1f;
                return true;
            case 0x01u: // SZ_AY8
            case 0x1Bu: // LU_IMAGE_AY8
                r = g = b = a = (texel & 0xFFu) / 255f;
                return true;
            case 0x19u: // SZ_A8
            case 0x1Fu: // LU_IMAGE_A8
                r = 1f;
                g = 1f;
                b = 1f;
                a = (texel & 0xFFu) / 255f;
                return true;
            case 0x1Au: // SZ_A8Y8
            case 0x20u: // LU_IMAGE_A8Y8
                r = g = b = (texel & 0xFFu) / 255f;
                a = ((texel >> 8) & 0xFFu) / 255f;
                return true;
            case 0x32u: // SZ_Y16
            case 0x35u: // LU_IMAGE_Y16
                // GL_RG8 with swizzle (1, G, R, 1): the high byte is green,
                // the low byte is blue, red and alpha are one.
                r = 1f;
                g = ((texel >> 8) & 0xFFu) / 255f;
                b = (texel & 0xFFu) / 255f;
                a = 1f;
                return true;
            case 0x27u: // SZ_R6G5B5
                {
                    // pgraph_convert_texture_data's signed conversion, then the
                    // GL_RGB8_SNORM sample: the byte is a signed normalized value
                    // divided by 127, and alpha is 1.
                    uint rgb655 = (texel & 0xFFFFu) ^ ((1u << 9) | (1u << 4));
                    int red = (int)((rgb655 & 0xFC00u) >> 10) * 0x7F / 0x3F;
                    int green = ((int)((rgb655 & 0x03E0u) >> 5) * 0xFF / 0x1F) - 0x80;
                    int blue = ((int)(rgb655 & 0x001Fu) * 0xFF / 0x1F) - 0x80;
                    r = red / 127f;
                    g = green / 127f;
                    b = blue / 127f;
                    a = 1f;
                    return true;
                }
            default:
                r = 0f;
                g = 0f;
                b = 0f;
                a = 0f;
                return false;
        }
    }

    /// <summary>
    /// Decodes a paletted texel: the index byte selects a 32-bit A8R8G8B8
    /// palette entry (<c>pgraph_convert_texture_data</c> reads
    /// <c>palette_data + index * 4</c> with no length clamp, and the GL upload
    /// is the same A8R8G8B8 word the default decoder reads).
    /// </summary>
    /// <param name="index">Palette index byte.</param>
    /// <param name="paletteWord">The palette entry.</param>
    /// <param name="r">Decoded red.</param>
    /// <param name="g">Decoded green.</param>
    /// <param name="b">Decoded blue.</param>
    /// <param name="a">Decoded alpha.</param>
    public static void DecodePaletteEntry(byte index, uint paletteWord, out float r, out float g, out float b,
        out float a)
    {
        _ = index;
        Nv2AMmio.DecodeTextureTexel(0x06u, paletteWord, out r, out g, out b, out a);
    }

    /// <summary>
    /// Decodes one YUY2/UYVY texel, transcribing
    /// <c>convert_yuy2_to_rgb</c>/<c>convert_uyvy_to_rgb</c> (util.h). The
    /// window is the four-byte pair group that contains the texel: for an even
    /// texel it starts at the texel address, for an odd texel two bytes earlier
    /// (the pair group is <c>U Y V Y</c> for both formats, with the texel at
    /// offset 0 for YUY2/even, 2 for YUY2/odd, 1 for UYVY/even and 3 for
    /// UYVY/odd).
    /// </summary>
    /// <param name="color">YUY2 or UYVY.</param>
    /// <param name="window">The four-byte pair group.</param>
    /// <param name="odd">True when the texel index in the row is odd.</param>
    /// <param name="r">Decoded red.</param>
    /// <param name="g">Decoded green.</param>
    /// <param name="b">Decoded blue.</param>
    /// <returns>True for a YUV format.</returns>
    public static bool TryDecodeYuv(uint color, ReadOnlySpan<byte> window, bool odd, out float r, out float g,
        out float b)
    {
        r = 0f;
        g = 0f;
        b = 0f;
        if (!IsYuv(color) || window.Length < 4)
        {
            return false;
        }

        int offset = odd ? 2 : 0;
        int c;
        int d;
        int e;
        if (color == Yuy2)
        {
            // U Y V Y: the texel's luma is at the group offset, the chroma
            // bytes are the U at 1 and the V at 3 for either parity.
            c = window[offset] - 16;
            d = window[1] - 128;
            e = window[3] - 128;
        }
        else
        {
            // U Y V Y: the texel's luma follows its group offset, the U is at
            // 0 and the V at 2.
            c = window[offset + 1] - 16;
            d = window[0] - 128;
            e = window[2] - 128;
        }

        r = ClipToByte(((298 * c) + (409 * e) + 128) >> 8) / 255f;
        g = ClipToByte(((298 * c) - (100 * d) - (208 * e) + 128) >> 8) / 255f;
        b = ClipToByte(((298 * c) + (516 * d) + 128) >> 8) / 255f;
        return true;
    }

    private static byte ClipToByte(int value)
    {
        return (byte)(value < 0 ? 0 : (value > 255 ? 255 : value));
    }

    /// <summary>
    /// Bytes occupied by one mip level of a swizzled (or compressed) texture,
    /// following xemu <c>pgraph_get_texture_length</c>: the uncompressed level
    /// is <c>w * h * bytes</c>, the compressed level is the 4x4-rounded block
    /// count times the block size.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="width">Level width.</param>
    /// <param name="height">Level height.</param>
    /// <returns>Level length in bytes.</returns>
    public static long LevelLength(uint color, int width, int height)
    {
        int w = Math.Max(width, 1);
        int h = Math.Max(height, 1);
        if (IsCompressed(color))
        {
            int physicalWidth = (w + 3) & ~3;
            int physicalHeight = (h + 3) & ~3;
            return (long)(physicalWidth / 4) * (physicalHeight / 4) * S3tcDecoder.BlockBytes(color);
        }

        return (long)w * h * BytesPerTexel(color);
    }

    /// <summary>
    /// Total bytes of a 2D mip chain (level 0 through
    /// <paramref name="levels"/> - 1), the reference's level-length loop.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="width">Level-0 width.</param>
    /// <param name="height">Level-0 height.</param>
    /// <param name="levels">Number of levels (clamped to at least one).</param>
    /// <returns>Chain length in bytes.</returns>
    public static long ChainLength(uint color, int width, int height, int levels)
    {
        long total = 0;
        int w = width;
        int h = height;
        for (int level = 0; level < Math.Max(levels, 1); level++)
        {
            total += LevelLength(color, w, h);
            w /= 2;
            h /= 2;
        }

        return total;
    }

    /// <summary>
    /// Byte offset of a mip level inside the chain: the sum of the preceding
    /// levels' <see cref="LevelLength"/>.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="width">Level-0 width.</param>
    /// <param name="height">Level-0 height.</param>
    /// <param name="level">Zero-based level.</param>
    /// <returns>Level offset in bytes.</returns>
    public static long LevelOffset(uint color, int width, int height, int level)
    {
        long offset = 0;
        int w = width;
        int h = height;
        for (int i = 0; i < level; i++)
        {
            offset += LevelLength(color, w, h);
            w /= 2;
            h /= 2;
        }

        return offset;
    }

    /// <summary>
    /// Bytes of one cube face: the full mip chain rounded up to the reference's
    /// 128-byte <c>NV2A_CUBEMAP_FACE_ALIGNMENT</c>. This is the same formula
    /// the cube sampler's face stride uses, now shared so a compressed face
    /// and an uncompressed face cannot disagree.
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="width">Face width.</param>
    /// <param name="height">Face height.</param>
    /// <param name="levels">Number of levels.</param>
    /// <returns>Face stride in bytes.</returns>
    public static long CubeFaceStride(uint color, int width, int height, int levels)
    {
        long length = ChainLength(color, width, height, levels);
        return (length + 127) & ~127L;
    }
}
