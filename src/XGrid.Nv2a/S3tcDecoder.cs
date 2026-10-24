// SPDX-License-Identifier: MIT
// Ported from xemu 0.8.139 hw/xbox/nv2a/pgraph/s3tc.c
/*
 * S3TC Texture Decompression
 *
 * Copyright (c) 2020 Wilhelm Kovatch
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Buffers.Binary;

namespace XGrid.Nv2a;

/// <summary>
/// S3TC (DXT1/DXT3/DXT5) block decompression, transcribed from xemu
/// <c>s3tc.c</c>. The NV2A stores compressed textures as a linear block array
/// (no Morton swizzle): block <c>(x/4, y/4)</c> sits at
/// <c>offset + ((y/4) * blocksPerRow + x/4) * blockBytes</c>, which is what the
/// reference GL uploader consumes level by level.
/// </summary>
public static class S3tcDecoder
{
    /// <summary>NV097 color format <c>L_DXT1_A1R5G5B5</c> (8-byte blocks, 1-bit alpha).</summary>
    public const uint Dxt1ColorFormat = 0x0Cu;

    /// <summary>NV097 color format <c>L_DXT23_A8R8G8B8</c> (16-byte blocks, explicit 4-bit alpha).</summary>
    public const uint Dxt3ColorFormat = 0x0Eu;

    /// <summary>NV097 color format <c>L_DXT45_A8R8G8B8</c> (16-byte blocks, interpolated alpha).</summary>
    public const uint Dxt5ColorFormat = 0x0Fu;

    /// <summary>Gets whether a color format is one of the three S3TC formats.</summary>
    /// <param name="color">Texture color-format id (FORMAT bits 8-15).</param>
    /// <returns>True for DXT1/DXT3/DXT5.</returns>
    public static bool IsDxtFormat(uint color)
    {
        return color is Dxt1ColorFormat or Dxt3ColorFormat or Dxt5ColorFormat;
    }

    /// <summary>Gets the compressed block size in bytes (8 for DXT1, 16 otherwise).</summary>
    /// <param name="color">Texture color-format id.</param>
    /// <returns>8 or 16, or 0 when the format is not S3TC.</returns>
    public static int BlockBytes(uint color)
    {
        return color switch
        {
            Dxt1ColorFormat => 8,
            Dxt3ColorFormat or Dxt5ColorFormat => 16,
            _ => 0,
        };
    }

    /// <summary>
    /// Decodes one 4x4 S3TC block to 16 row-major RGBA8 texels, matching
    /// xemu <c>decompress_dxt1_block</c>/<c>decompress_dxt3_block</c>/
    /// <c>decompress_dxt5_block</c> byte for byte (the C integer divisions are
    /// kept as integer divisions here).
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="block">The 8- or 16-byte compressed block.</param>
    /// <param name="rgba">Destination, at least 64 bytes (16 texels x RGBA).</param>
    /// <returns>True when the format is S3TC and the spans are long enough.</returns>
    public static bool TryDecodeBlock(uint color, ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        int blockBytes = BlockBytes(color);
        if (blockBytes == 0 || block.Length < blockBytes || rgba.Length < 64)
        {
            return false;
        }

        Span<byte> r = stackalloc byte[4];
        Span<byte> g = stackalloc byte[4];
        Span<byte> b = stackalloc byte[4];
        Span<byte> a = stackalloc byte[16];

        uint indices;
        if (color == Dxt1ColorFormat)
        {
            ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
            ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
            DecodeBc1Colors(c0, c1, r, g, b, a, transparent: c0 <= c1);
            indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
            WriteBlock(rgba, indices, r, g, b, a, separateAlpha: false);
            return true;
        }

        ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(block[8..]);
        ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[10..]);
        DecodeBc1Colors(color0, color1, r, g, b, a, transparent: false);
        indices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);

        if (color == Dxt3ColorFormat)
        {
            ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(block);
            for (int i = 0; i < 16; i++)
            {
                a[i] = (byte)((((alpha >> (4 * i)) & 0x0Fu) << 4) * 0xFFu / 0xF0u);
            }
        }
        else
        {
            // DXT5: two endpoint alphas and a 3-bit index per texel.
            ulong alpha = BinaryPrimitives.ReadUInt64LittleEndian(block);
            byte a0 = block[0];
            byte a1 = block[1];
            Span<byte> palette = stackalloc byte[8];
            palette[0] = a0;
            palette[1] = a1;
            if (a0 > a1)
            {
                palette[2] = (byte)(((6 * a0) + a1) / 7);
                palette[3] = (byte)(((5 * a0) + (2 * a1)) / 7);
                palette[4] = (byte)(((4 * a0) + (3 * a1)) / 7);
                palette[5] = (byte)(((3 * a0) + (4 * a1)) / 7);
                palette[6] = (byte)(((2 * a0) + (5 * a1)) / 7);
                palette[7] = (byte)((a0 + (6 * a1)) / 7);
            }
            else
            {
                palette[2] = (byte)(((4 * a0) + a1) / 5);
                palette[3] = (byte)(((3 * a0) + (2 * a1)) / 5);
                palette[4] = (byte)(((2 * a0) + (3 * a1)) / 5);
                palette[5] = (byte)((a0 + (4 * a1)) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }

            for (int i = 0; i < 16; i++)
            {
                a[i] = palette[(int)((alpha >> (16 + (3 * i))) & 0x07u)];
            }
        }

        WriteBlock(rgba, indices, r, g, b, a, separateAlpha: true);
        return true;
    }

    /// <summary>
    /// Decodes a single texel of a block (the block is fully decoded and the
    /// requested lane returned, so the result is exactly
    /// <see cref="TryDecodeBlock"/>'s).
    /// </summary>
    /// <param name="color">Texture color-format id.</param>
    /// <param name="block">The compressed block.</param>
    /// <param name="texelIndex">Row-major texel index inside the block (0-15).</param>
    /// <param name="r">Decoded red byte.</param>
    /// <param name="g">Decoded green byte.</param>
    /// <param name="b">Decoded blue byte.</param>
    /// <param name="a">Decoded alpha byte.</param>
    /// <returns>True when the block decoded.</returns>
    public static bool TryDecodeTexel(uint color, ReadOnlySpan<byte> block, int texelIndex, out byte r, out byte g,
        out byte b, out byte a)
    {
        Span<byte> rgba = stackalloc byte[64];
        r = 0;
        g = 0;
        b = 0;
        a = 0;
        if (!TryDecodeBlock(color, block, rgba))
        {
            return false;
        }

        int index = Math.Clamp(texelIndex, 0, 15) * 4;
        r = rgba[index];
        g = rgba[index + 1];
        b = rgba[index + 2];
        a = rgba[index + 3];
        return true;
    }

    /// <summary>
    /// The BC1 endpoint palette (<c>decode_bc1_colors</c>): four RGB colours and
    /// four alphas. The transparent variant is the c0 &lt;= c1 DXT1 mode
    /// (3-colour + transparent black).
    /// </summary>
    private static void DecodeBc1Colors(ushort c0, ushort c1, Span<byte> r, Span<byte> g, Span<byte> b,
        Span<byte> a, bool transparent)
    {
        r[0] = (byte)(((c0 & 0xF800u) >> 8) * 0xFFu / 0xF8u);
        g[0] = (byte)(((c0 & 0x07E0u) >> 3) * 0xFFu / 0xFCu);
        b[0] = (byte)(((c0 & 0x001Fu) << 3) * 0xFFu / 0xF8u);
        a[0] = 255;

        r[1] = (byte)(((c1 & 0xF800u) >> 8) * 0xFFu / 0xF8u);
        g[1] = (byte)(((c1 & 0x07E0u) >> 3) * 0xFFu / 0xFCu);
        b[1] = (byte)(((c1 & 0x001Fu) << 3) * 0xFFu / 0xF8u);
        a[1] = 255;

        if (transparent)
        {
            r[2] = (byte)((r[0] + r[1]) / 2);
            g[2] = (byte)((g[0] + g[1]) / 2);
            b[2] = (byte)((b[0] + b[1]) / 2);
            a[2] = 255;

            r[3] = 0;
            g[3] = 0;
            b[3] = 0;
            a[3] = 0;
        }
        else
        {
            r[2] = (byte)(((2 * r[0]) + r[1]) / 3);
            g[2] = (byte)(((2 * g[0]) + g[1]) / 3);
            b[2] = (byte)(((2 * b[0]) + b[1]) / 3);
            a[2] = 255;

            r[3] = (byte)((r[0] + (2 * r[1])) / 3);
            g[3] = (byte)((g[0] + (2 * g[1])) / 3);
            b[3] = (byte)((b[0] + (2 * b[1])) / 3);
            a[3] = 255;
        }
    }

    /// <summary>
    /// The shared <c>write_block_to_texture</c> tail: one 2-bit colour index per
    /// texel, with the alpha lane either the same index (DXT1) or the separate
    /// alpha palette (DXT3/DXT5).
    /// </summary>
    private static void WriteBlock(Span<byte> rgba, uint indices, ReadOnlySpan<byte> r, ReadOnlySpan<byte> g,
        ReadOnlySpan<byte> b, ReadOnlySpan<byte> a, bool separateAlpha)
    {
        for (int i = 0; i < 16; i++)
        {
            int index = (int)((indices >> (2 * i)) & 0x03u);
            int alphaIndex = separateAlpha ? i : index;
            rgba[(i * 4) + 0] = r[index];
            rgba[(i * 4) + 1] = g[index];
            rgba[(i * 4) + 2] = b[index];
            rgba[(i * 4) + 3] = a[alphaIndex];
        }
    }
}
