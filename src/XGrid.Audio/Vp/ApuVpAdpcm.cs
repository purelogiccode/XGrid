// SPDX-License-Identifier: BSD-3-Clause
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/vp/adpcm.h.
// Original: ADPCM-XQ 4-bit ADPCM decoder (https://github.com/dbry/adpcm-xq),
// Copyright (c) David Bryant, All rights reserved.  BSD-3-Clause; see the
// notice block in the reference file.  Distributed here as part of a
// GPL-2.0 work (BSD-3-Clause is GPL-compatible).

using System;

namespace XGrid.Audio.Vp;

/// <summary>
/// The Xbox APU's 4-bit ADPCM block decoder (xemu <c>adpcm_decode_block</c>).
/// Blocks are independently decodable, so the decoder is stateless.
/// </summary>
internal static class ApuVpAdpcm
{
    /// <summary>Samples a decoded block carries per channel (xemu <c>ADPCM_SAMPLES_PER_BLOCK</c>).</summary>
    public const int SamplesPerBlock = 64;

    /// <summary>Bytes in one ADPCM block for one channel (36, i.e. 9 words).</summary>
    public const int BlockBytes = 36;

    private static readonly ushort[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14,
        16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66,
        73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307,
        337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411,
        1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
        32767,
    ];

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];

    /// <summary>
    /// Decodes one ADPCM block into interleaved 16-bit PCM. The output layout is
    /// the reference's: one header sample per channel, then eight samples per
    /// chunk, interleaved by channel.
    /// </summary>
    /// <param name="output">Destination for interleaved samples (at least 65 * channels entries).</param>
    /// <param name="input">The block bytes.</param>
    /// <param name="channels">1 or 2.</param>
    /// <returns>Converted composite samples, or 0 when the block is not decodable.</returns>
    public static int Decode(Span<short> output, ReadOnlySpan<byte> input, int channels)
    {
        if (channels is < 1 or > 2 || input.Length < channels * 4)
        {
            return 0;
        }

        int inbufsize = input.Length;
        int inbuf = 0;
        int o = 0;
        int samples = 1;

        Span<int> pcmdata = stackalloc int[2];
        Span<int> index = stackalloc int[2];

        for (int ch = 0; ch < channels; ch++)
        {
            int initial = (short)(input[inbuf] | (input[inbuf + 1] << 8));
            pcmdata[ch] = initial;
            output[o++] = (short)initial;
            index[ch] = (sbyte)input[inbuf + 2];

            if (index[ch] < 0 || index[ch] > 88 || input[inbuf + 3] != 0)
            {
                return 0;
            }

            inbufsize -= 4;
            inbuf += 4;
        }

        int chunks = inbufsize / (channels * 4);
        samples += chunks * 8;

        while (chunks-- > 0)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int step = StepTable[index[ch]];
                    int delta = step >> 3;

                    if ((input[inbuf] & 1) != 0)
                    {
                        delta += step >> 2;
                    }

                    if ((input[inbuf] & 2) != 0)
                    {
                        delta += step >> 1;
                    }

                    if ((input[inbuf] & 4) != 0)
                    {
                        delta += step;
                    }

                    if ((input[inbuf] & 8) != 0)
                    {
                        delta = -delta;
                    }

                    pcmdata[ch] += delta;
                    index[ch] += IndexTable[input[inbuf] & 0x7];
                    index[ch] = Math.Clamp(index[ch], 0, 88);
                    pcmdata[ch] = Math.Clamp(pcmdata[ch], -32768, 32767);
                    output[o + (i * 2 * channels)] = (short)pcmdata[ch];

                    step = StepTable[index[ch]];
                    delta = step >> 3;

                    if ((input[inbuf] & 0x10) != 0)
                    {
                        delta += step >> 2;
                    }

                    if ((input[inbuf] & 0x20) != 0)
                    {
                        delta += step >> 1;
                    }

                    if ((input[inbuf] & 0x40) != 0)
                    {
                        delta += step;
                    }

                    if ((input[inbuf] & 0x80) != 0)
                    {
                        delta = -delta;
                    }

                    pcmdata[ch] += delta;
                    index[ch] += IndexTable[(input[inbuf] >> 4) & 0x7];
                    index[ch] = Math.Clamp(index[ch], 0, 88);
                    pcmdata[ch] = Math.Clamp(pcmdata[ch], -32768, 32767);
                    output[o + ((i * 2 + 1) * channels)] = (short)pcmdata[ch];

                    inbuf++;
                }

                o++;
            }

            o += channels * 7;
        }

        return samples;
    }
}
