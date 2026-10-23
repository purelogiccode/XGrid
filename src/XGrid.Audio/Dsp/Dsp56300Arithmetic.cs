// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c.
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.
// Adapted from Hatari DSP M56001 emulation (C) 2001-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// 56-bit DSP56300 arithmetic helpers, transcribed from the xemu C
/// interpreter.  A 56-bit value is three limbs: [0] bits 55:48, [1] bits
/// 47:24, [2] bits 23:00.
/// </summary>
public static class Dsp56300Arithmetic
{
    private const uint Mask24 = 0x00FFFFFF;
    private const uint Mask8 = 0x000000FF;

    /// <summary>Computes D = |D| and returns the status bits to merge.</summary>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <returns>Status bits (L/V/C positions).</returns>
    public static ushort Abs56(Span<uint> dest)
    {
        if ((dest[0] & (1u << 7)) == 0)
        {
            return 0;
        }

        Span<uint> zero = stackalloc uint[3];
        zero[0] = 0;
        zero[1] = 0;
        zero[2] = 0;
        ushort newsr = Sub56(dest, zero);
        dest[0] = zero[0];
        dest[1] = zero[1];
        dest[2] = zero[2];
        return newsr;
    }

    /// <summary>Computes D = D &lt;&lt; n and returns the status bits.</summary>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <param name="n">The shift count.</param>
    /// <returns>Status bits (L/V/C positions).</returns>
    public static ushort Asl56(Span<uint> dest, int n)
    {
        ulong destValue = dest[2] | ((ulong)dest[1] << 24) | ((ulong)dest[0] << 48);
        uint carry = (uint)((destValue >> (56 - n)) & 1);
        ulong shifted = destValue << n;

        dest[2] = (uint)shifted & Mask24;
        dest[1] = (uint)(shifted >> 24) & Mask24;
        dest[0] = (uint)(shifted >> 48) & Mask8;

        uint overflow = (destValue >> (56 - n)) != 0 ? 1u : 0u;
        uint overflowBit = ((destValue >> 55) & 1) != ((shifted >> 55) & 1) ? 1u : 0u;
        return (ushort)((overflow << Dsp56300State.SrL)
            | (overflowBit << Dsp56300State.SrV)
            | (carry << Dsp56300State.SrC));
    }

    /// <summary>Computes D = D &gt;&gt; n and returns the carry bit.</summary>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <param name="n">The shift count.</param>
    /// <returns>Status bits (C position).</returns>
    public static ushort Asr56(Span<uint> dest, int n)
    {
        ulong destValue = dest[2] | ((ulong)dest[1] << 24) | ((ulong)dest[0] << 48);
        uint carry = (uint)((destValue >> (n - 1)) & 1);
        destValue >>= n;

        dest[2] = (uint)destValue & Mask24;
        dest[1] = (uint)(destValue >> 24) & Mask24;
        dest[0] = (uint)(destValue >> 48) & Mask8;
        return (ushort)(carry << Dsp56300State.SrC);
    }

    /// <summary>Computes D = D + S and returns the status bits.</summary>
    /// <param name="source">The three-limb source.</param>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <returns>Status bits (L/V/C positions).</returns>
    public static ushort Add56(ReadOnlySpan<uint> source, Span<uint> dest)
    {
        uint sourceSign = (source[0] >> 7) & 1;
        uint destSign = (dest[0] >> 7) & 1;

        dest[2] += source[2];
        dest[1] += source[1] + ((dest[2] >> 24) & 1);
        dest[0] += source[0] + ((dest[1] >> 24) & 1);

        uint carry = (dest[0] >> 8) & 1;
        dest[2] &= Mask24;
        dest[1] &= Mask24;
        dest[0] &= Mask8;

        uint resultSign = (dest[0] >> 7) & 1;
        uint overflow = (sourceSign ^ resultSign) & (destSign ^ resultSign);
        return (ushort)((overflow << Dsp56300State.SrL)
            | (overflow << Dsp56300State.SrV)
            | (carry << Dsp56300State.SrC));
    }

    /// <summary>Computes D = D - S and returns the status bits.</summary>
    /// <param name="source">The three-limb source.</param>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <returns>Status bits (L/V/C positions).</returns>
    public static ushort Sub56(ReadOnlySpan<uint> source, Span<uint> dest)
    {
        uint destSave = dest[0];

        dest[2] -= source[2];
        dest[1] -= source[1] + ((dest[2] >> 24) & 1);
        dest[0] -= source[0] + ((dest[1] >> 24) & 1);

        uint carry = (dest[0] >> 8) & 1;
        dest[2] &= Mask24;
        dest[1] &= Mask24;
        dest[0] &= Mask8;

        uint sourceSign = (source[0] >> 7) & 1;
        uint destSign = (destSave >> 7) & 1;
        uint resultSign = (dest[0] >> 7) & 1;
        uint overflow = (sourceSign ^ destSign) & (resultSign ^ destSign);
        return (ushort)((overflow << Dsp56300State.SrL)
            | (overflow << Dsp56300State.SrV)
            | (carry << Dsp56300State.SrC));
    }

    /// <summary>Computes D = S1 * S2 into the three-limb destination.</summary>
    /// <param name="source1">The first 24-bit source.</param>
    /// <param name="source2">The second 24-bit source.</param>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    /// <param name="signe">The initial sign flag.</param>
    public static void Mul56(uint source1, uint source2, Span<uint> dest, byte signe)
    {
        Span<uint> part = stackalloc uint[4];
        Span<uint> zero = stackalloc uint[3];
        zero[0] = 0;
        zero[1] = 0;
        zero[2] = 0;

        if ((source1 & (1u << 23)) != 0)
        {
            signe ^= 1;
            source1 = (1u << 24) - source1;
        }

        if ((source2 & (1u << 23)) != 0)
        {
            signe ^= 1;
            source2 = (1u << 24) - source2;
        }

        part[0] = (source1 & 0x0FFF) * (source2 & 0x0FFF);
        part[1] = ((source1 >> 12) & 0x0FFF) * (source2 & 0x0FFF);
        part[2] = (source1 & 0x0FFF) * ((source2 >> 12) & 0x0FFF);
        part[3] = ((source1 >> 12) & 0x0FFF) * ((source2 >> 12) & 0x0FFF);

        dest[2] = part[0];
        dest[2] += (part[1] & 0x0FFF) << 12;
        dest[2] += (part[2] & 0x0FFF) << 12;

        dest[1] = (part[1] >> 12) & 0x0FFF;
        dest[1] += (part[2] >> 12) & 0x0FFF;
        dest[1] += part[3];

        dest[0] = 0;

        uint value = (dest[2] >> 24) & Mask8;
        if (value != 0)
        {
            dest[1] += value;
            dest[2] &= Mask24;
        }

        value = (dest[1] >> 24) & Mask8;
        if (value != 0)
        {
            dest[0] += value;
            dest[1] &= Mask24;
        }

        _ = Asl56(dest, 1);

        if (signe != 0)
        {
            _ = Sub56(dest, zero);
            dest[0] = zero[0];
            dest[1] = zero[1];
            dest[2] = zero[2];
        }
    }

    /// <summary>Rounds the three-limb destination using the current scaling mode.</summary>
    /// <param name="state">The core state (reads SR).</param>
    /// <param name="dest">The three-limb destination, updated in place.</param>
    public static void Round56(Dsp56300State state, Span<uint> dest)
    {
        Span<uint> round = stackalloc uint[3];
        round[0] = 0;

        if ((state.Registers[Dsp56300State.RegSr] & (1u << Dsp56300State.SrS0)) != 0)
        {
            round[1] = 1;
            round[2] = 0;
            _ = Add56(round, dest);

            if (dest[2] == 0 && (dest[1] & 1) == 0)
            {
                dest[1] &= 0xFFFFFF - 0x3;
            }

            dest[1] &= 0xFFFFFE;
            dest[2] = 0;
        }
        else if ((state.Registers[Dsp56300State.RegSr] & (1u << Dsp56300State.SrS1)) != 0)
        {
            round[1] = 0;
            round[2] = 1u << 22;
            _ = Add56(round, dest);

            if ((dest[2] & 0x7FFFFF) == 0)
            {
                dest[2] = 0;
            }

            dest[2] &= 0x800000;
        }
        else
        {
            round[1] = 0;
            round[2] = 1u << 23;
            _ = Add56(round, dest);

            if (dest[2] == 0)
            {
                dest[1] &= 0xFFFFFE;
            }

            dest[2] = 0;
        }
    }

    /// <summary>Sign-extends the low <paramref name="bits"/> of a value.</summary>
    /// <param name="bits">The source width.</param>
    /// <param name="value">The value to extend.</param>
    /// <returns>The sign-extended value.</returns>
    public static uint SignExtend(int bits, uint value)
    {
        int shift = (sizeof(int) * 8) - bits;
        if (shift <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bits));
        }

        return (uint)((int)value << shift >> shift);
    }
}
