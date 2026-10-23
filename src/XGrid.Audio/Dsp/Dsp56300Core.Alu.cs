// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc.
// Original copyright (c) 2015 espes.
// Adapted from Hatari DSP M56001 emulation (C) 2003-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// DSP56300 accumulator arithmetic: the <c>emu_add_*</c>, <c>emu_*_x0_*</c> and
/// <c>emu_mpy_*</c> handler families.
/// </summary>
/// <remarks>
/// The reference writes one C function per opcode variant.  The bodies are the
/// same arithmetic over a different operand pair, so this port keeps one method
/// per family and the reference's one-line wrappers on top.  The flag merges
/// (<c>SR &amp;= ...</c> / <c>SR |= newsr</c>) are transcribed literally because
/// several variants deliberately drop the carry.
/// </remarks>
public sealed partial class Dsp56300Core
{
    /// <summary>The 24-bit and 48-bit source selectors of the ALU opcodes.</summary>
    private enum AluSource
    {
        A,
        B,
        X,
        Y,
        X0,
        X1,
        Y0,
        Y1,
    }

    private const uint SignPlus = 0;
    private const uint SignMinus = 1;

    private void LoadAcc(uint reg, Span<uint> dest)
    {
        uint offset = reg & 1;
        dest[0] = R[Dsp56300State.RegA2 + offset];
        dest[1] = R[Dsp56300State.RegA1 + offset];
        dest[2] = R[Dsp56300State.RegA0 + offset];
    }

    private void StoreAcc(uint reg, ReadOnlySpan<uint> dest)
    {
        uint offset = reg & 1;
        R[Dsp56300State.RegA2 + offset] = dest[0];
        R[Dsp56300State.RegA1 + offset] = dest[1];
        R[Dsp56300State.RegA0 + offset] = dest[2];
    }

    private void LoadAluSource(AluSource source, Span<uint> value)
    {
        switch (source)
        {
            case AluSource.A:
                LoadAcc(Dsp56300State.RegA, value);
                break;
            case AluSource.B:
                LoadAcc(Dsp56300State.RegB, value);
                break;
            case AluSource.X:
                value[1] = R[Dsp56300State.RegX1];
                value[2] = R[Dsp56300State.RegX0];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
            case AluSource.Y:
                value[1] = R[Dsp56300State.RegY1];
                value[2] = R[Dsp56300State.RegY0];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
            case AluSource.X0:
                value[2] = 0;
                value[1] = R[Dsp56300State.RegX0];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
            case AluSource.X1:
                value[2] = 0;
                value[1] = R[Dsp56300State.RegX1];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
            case AluSource.Y0:
                value[2] = 0;
                value[1] = R[Dsp56300State.RegY0];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
            default:
                value[2] = 0;
                value[1] = R[Dsp56300State.RegY1];
                value[0] = Flag(value[1], 23) ? 0xFFu : 0u;
                break;
        }
    }

    private static uint SourceRegister(AluSource source) => source switch
    {
        AluSource.X0 => Dsp56300State.RegX0,
        AluSource.X1 => Dsp56300State.RegX1,
        AluSource.Y0 => Dsp56300State.RegY0,
        AluSource.Y1 => Dsp56300State.RegY1,
        _ => 0,
    };

    /// <summary>Reads a register as a 24-bit value (xemu <c>emu_pm_read_accu24</c>).</summary>
    /// <param name="numreg">The register index.</param>
    /// <param name="dest">The 24-bit value.</param>
    /// <returns>Non-zero when the value had to be limited.</returns>
    private int PmReadAccu24(uint numreg, out uint dest)
    {
        uint scaling = (R[Dsp56300State.RegSr] >> Dsp56300State.SrS0) & 3u;
        uint reg = numreg & 1;
        uint value = R[Dsp56300State.RegA2 + reg] << 24;
        value += R[Dsp56300State.RegA1 + reg];

        switch (scaling)
        {
            case 0:
                break;
            case 1:
                value >>= 1;
                break;
            case 2:
                value <<= 1;
                value |= (R[Dsp56300State.RegA0 + reg] >> 23) & 1;
                break;
            case 3:
                break;
        }

        value &= Mask(24);

        if (R[Dsp56300State.RegA2 + reg] == 0 && value <= 0x007FFFFF)
        {
            dest = value;
            return 0;
        }

        if (R[Dsp56300State.RegA2 + reg] == 0xFF && value >= 0x00800000)
        {
            dest = value;
            return 0;
        }

        if (Flag(R[Dsp56300State.RegA2 + reg], 7))
        {
            dest = 0x00800000;
        }
        else
        {
            dest = 0x007FFFFF;
        }

        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrL;
        return 1;
    }

    private uint ReadReg24(uint numreg)
    {
        if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
        {
            _ = PmReadAccu24(numreg, out uint value);
            return value;
        }

        return R[numreg];
    }

    private void AluAdd(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);

        ushort newsr = Dsp56300Arithmetic.Add56(sourceValue, dest);
        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluSub(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluCmp(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluCmpm(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        _ = Dsp56300Arithmetic.Abs56(dest);
        LoadAluSource(source, sourceValue);
        _ = Dsp56300Arithmetic.Abs56(sourceValue);

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluAdc(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        uint curcarry = (R[Dsp56300State.RegSr] >> Dsp56300State.SrC) & 1;

        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);
        ushort newsr = Dsp56300Arithmetic.Add56(sourceValue, dest);

        if (curcarry != 0)
        {
            sourceValue[0] = 0;
            sourceValue[1] = 0;
            sourceValue[2] = 1;
            newsr |= Dsp56300Arithmetic.Add56(sourceValue, dest);
        }

        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluSbc(AluSource source, uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        uint curcarry = (R[Dsp56300State.RegSr] >> Dsp56300State.SrC) & 1;

        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);
        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);

        if (curcarry != 0)
        {
            sourceValue[0] = 0;
            sourceValue[1] = 0;
            sourceValue[2] = 1;
            newsr |= Dsp56300Arithmetic.Sub56(sourceValue, dest);
        }

        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AluAnd(AluSource source, uint destReg)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        R[dest] &= R[SourceRegister(source)];

        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dest] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void AluOr(AluSource source, uint destReg)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        R[dest] |= R[SourceRegister(source)];
        R[dest] &= Mask(24);

        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dest] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void AluEor(AluSource source, uint destReg)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        R[dest] ^= R[SourceRegister(source)];
        R[dest] &= Mask(24);

        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dest] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void AluTfr(AluSource source, uint destReg)
    {
        uint value = R[SourceRegister(source)];
        uint offset = destReg & 1;
        R[Dsp56300State.RegA0 + offset] = 0;
        R[Dsp56300State.RegA1 + offset] = value;
        R[Dsp56300State.RegA2 + offset] = Flag(value, 23) ? 0xFFu : 0u;
    }

    // The reference's one-line opcode wrappers (dsp_emu.c.inc).
    private void AddBA() => AluAdd(AluSource.B, Dsp56300State.RegA);

    private void AddAB() => AluAdd(AluSource.A, Dsp56300State.RegB);

    private void AddXA() => AluAdd(AluSource.X, Dsp56300State.RegA);

    private void AddXB() => AluAdd(AluSource.X, Dsp56300State.RegB);

    private void AddYA() => AluAdd(AluSource.Y, Dsp56300State.RegA);

    private void AddYB() => AluAdd(AluSource.Y, Dsp56300State.RegB);

    private void AddX0A() => AluAdd(AluSource.X0, Dsp56300State.RegA);

    private void AddX0B() => AluAdd(AluSource.X0, Dsp56300State.RegB);

    private void AddY0A() => AluAdd(AluSource.Y0, Dsp56300State.RegA);

    private void AddY0B() => AluAdd(AluSource.Y0, Dsp56300State.RegB);

    private void AddX1A() => AluAdd(AluSource.X1, Dsp56300State.RegA);

    private void AddX1B() => AluAdd(AluSource.X1, Dsp56300State.RegB);

    private void AddY1A() => AluAdd(AluSource.Y1, Dsp56300State.RegA);

    private void AddY1B() => AluAdd(AluSource.Y1, Dsp56300State.RegB);

    private void SubBA() => AluSub(AluSource.B, Dsp56300State.RegA);

    private void SubAB() => AluSub(AluSource.A, Dsp56300State.RegB);

    private void SubXA() => AluSub(AluSource.X, Dsp56300State.RegA);

    private void SubXB() => AluSub(AluSource.X, Dsp56300State.RegB);

    private void SubYA() => AluSub(AluSource.Y, Dsp56300State.RegA);

    private void SubYB() => AluSub(AluSource.Y, Dsp56300State.RegB);

    private void SubX0A() => AluSub(AluSource.X0, Dsp56300State.RegA);

    private void SubX0B() => AluSub(AluSource.X0, Dsp56300State.RegB);

    private void SubY0A() => AluSub(AluSource.Y0, Dsp56300State.RegA);

    private void SubY0B() => AluSub(AluSource.Y0, Dsp56300State.RegB);

    private void SubX1A() => AluSub(AluSource.X1, Dsp56300State.RegA);

    private void SubX1B() => AluSub(AluSource.X1, Dsp56300State.RegB);

    private void SubY1A() => AluSub(AluSource.Y1, Dsp56300State.RegA);

    private void SubY1B() => AluSub(AluSource.Y1, Dsp56300State.RegB);

    private void CmpBA() => AluCmp(AluSource.B, Dsp56300State.RegA);

    private void CmpAB() => AluCmp(AluSource.A, Dsp56300State.RegB);

    private void CmpX0A() => AluCmp(AluSource.X0, Dsp56300State.RegA);

    private void CmpX0B() => AluCmp(AluSource.X0, Dsp56300State.RegB);

    private void CmpY0A() => AluCmp(AluSource.Y0, Dsp56300State.RegA);

    private void CmpY0B() => AluCmp(AluSource.Y0, Dsp56300State.RegB);

    private void CmpX1A() => AluCmp(AluSource.X1, Dsp56300State.RegA);

    private void CmpX1B() => AluCmp(AluSource.X1, Dsp56300State.RegB);

    private void CmpY1A() => AluCmp(AluSource.Y1, Dsp56300State.RegA);

    private void CmpY1B() => AluCmp(AluSource.Y1, Dsp56300State.RegB);

    private void CmpmBA() => AluCmpm(AluSource.B, Dsp56300State.RegA);

    private void CmpmAB() => AluCmpm(AluSource.A, Dsp56300State.RegB);

    private void CmpmX0A() => AluCmpm(AluSource.X0, Dsp56300State.RegA);

    private void CmpmX0B() => AluCmpm(AluSource.X0, Dsp56300State.RegB);

    private void CmpmY0A() => AluCmpm(AluSource.Y0, Dsp56300State.RegA);

    private void CmpmY0B() => AluCmpm(AluSource.Y0, Dsp56300State.RegB);

    private void CmpmX1A() => AluCmpm(AluSource.X1, Dsp56300State.RegA);

    private void CmpmX1B() => AluCmpm(AluSource.X1, Dsp56300State.RegB);

    private void CmpmY1A() => AluCmpm(AluSource.Y1, Dsp56300State.RegA);

    private void CmpmY1B() => AluCmpm(AluSource.Y1, Dsp56300State.RegB);

    private void AdcXA() => AluAdc(AluSource.X, Dsp56300State.RegA);

    private void AdcXB() => AluAdc(AluSource.X, Dsp56300State.RegB);

    private void AdcYA() => AluAdc(AluSource.Y, Dsp56300State.RegA);

    private void AdcYB() => AluAdc(AluSource.Y, Dsp56300State.RegB);

    private void SbcXA() => AluSbc(AluSource.X, Dsp56300State.RegA);

    private void SbcXB() => AluSbc(AluSource.X, Dsp56300State.RegB);

    private void SbcYA() => AluSbc(AluSource.Y, Dsp56300State.RegA);

    private void SbcYB() => AluSbc(AluSource.Y, Dsp56300State.RegB);

    private void AndX0A() => AluAnd(AluSource.X0, Dsp56300State.RegA);

    private void AndX0B() => AluAnd(AluSource.X0, Dsp56300State.RegB);

    private void AndY0A() => AluAnd(AluSource.Y0, Dsp56300State.RegA);

    private void AndY0B() => AluAnd(AluSource.Y0, Dsp56300State.RegB);

    private void AndX1A() => AluAnd(AluSource.X1, Dsp56300State.RegA);

    private void AndX1B() => AluAnd(AluSource.X1, Dsp56300State.RegB);

    private void AndY1A() => AluAnd(AluSource.Y1, Dsp56300State.RegA);

    private void AndY1B() => AluAnd(AluSource.Y1, Dsp56300State.RegB);

    private void OrX0A() => AluOr(AluSource.X0, Dsp56300State.RegA);

    private void OrX0B() => AluOr(AluSource.X0, Dsp56300State.RegB);

    private void OrY0A() => AluOr(AluSource.Y0, Dsp56300State.RegA);

    private void OrY0B() => AluOr(AluSource.Y0, Dsp56300State.RegB);

    private void OrX1A() => AluOr(AluSource.X1, Dsp56300State.RegA);

    private void OrX1B() => AluOr(AluSource.X1, Dsp56300State.RegB);

    private void OrY1A() => AluOr(AluSource.Y1, Dsp56300State.RegA);

    private void OrY1B() => AluOr(AluSource.Y1, Dsp56300State.RegB);

    private void EorX0A() => AluEor(AluSource.X0, Dsp56300State.RegA);

    private void EorX0B() => AluEor(AluSource.X0, Dsp56300State.RegB);

    private void EorY0A() => AluEor(AluSource.Y0, Dsp56300State.RegA);

    private void EorY0B() => AluEor(AluSource.Y0, Dsp56300State.RegB);

    private void EorX1A() => AluEor(AluSource.X1, Dsp56300State.RegA);

    private void EorX1B() => AluEor(AluSource.X1, Dsp56300State.RegB);

    private void EorY1A() => AluEor(AluSource.Y1, Dsp56300State.RegA);

    private void EorY1B() => AluEor(AluSource.Y1, Dsp56300State.RegB);

    private void TfrX0A() => AluTfr(AluSource.X0, Dsp56300State.RegA);

    private void TfrX0B() => AluTfr(AluSource.X0, Dsp56300State.RegB);

    private void TfrY0A() => AluTfr(AluSource.Y0, Dsp56300State.RegA);

    private void TfrY0B() => AluTfr(AluSource.Y0, Dsp56300State.RegB);

    private void TfrX1A() => AluTfr(AluSource.X1, Dsp56300State.RegA);

    private void TfrX1B() => AluTfr(AluSource.X1, Dsp56300State.RegB);

    private void TfrY1A() => AluTfr(AluSource.Y1, Dsp56300State.RegA);

    private void TfrY1B() => AluTfr(AluSource.Y1, Dsp56300State.RegB);

    private void TfrBA()
    {
        R[Dsp56300State.RegA0] = R[Dsp56300State.RegB0];
        R[Dsp56300State.RegA1] = R[Dsp56300State.RegB1];
        R[Dsp56300State.RegA2] = R[Dsp56300State.RegB2];
    }

    private void TfrAB()
    {
        R[Dsp56300State.RegB0] = R[Dsp56300State.RegA0];
        R[Dsp56300State.RegB1] = R[Dsp56300State.RegA1];
        R[Dsp56300State.RegB2] = R[Dsp56300State.RegA2];
    }

    private void AddlBA() => AccumulateShifted(AluSource.B, Dsp56300State.RegA, shiftLeft: true, subtract: false);

    private void AddlAB() => AccumulateShifted(AluSource.A, Dsp56300State.RegB, shiftLeft: true, subtract: false);

    private void SublA() => AccumulateShifted(AluSource.B, Dsp56300State.RegA, shiftLeft: true, subtract: true);

    private void SublB() => AccumulateShifted(AluSource.A, Dsp56300State.RegB, shiftLeft: true, subtract: true);

    private void AddrBA() => AccumulateShifted(AluSource.B, Dsp56300State.RegA, shiftLeft: false, subtract: false);

    private void AddrAB() => AccumulateShifted(AluSource.A, Dsp56300State.RegB, shiftLeft: false, subtract: false);

    private void SubrA() => AccumulateShifted(AluSource.B, Dsp56300State.RegA, shiftLeft: false, subtract: true);

    private void SubrB() => AccumulateShifted(AluSource.A, Dsp56300State.RegB, shiftLeft: false, subtract: true);

    private void AccumulateShifted(AluSource source, uint destReg, bool shiftLeft, bool subtract)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        LoadAluSource(source, sourceValue);

        ushort newsr = shiftLeft
            ? Dsp56300Arithmetic.Asl56(dest, 1)
            : Dsp56300Arithmetic.Asr56(dest, 1);
        newsr |= subtract
            ? Dsp56300Arithmetic.Sub56(sourceValue, dest)
            : Dsp56300Arithmetic.Add56(sourceValue, dest);

        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AbsA() => AbsAcc(Dsp56300State.RegA);

    private void AbsB() => AbsAcc(Dsp56300State.RegB);

    private void AbsAcc(uint destReg)
    {
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);

        uint overflowed = (dest[2] == 0 && dest[1] == 0 && dest[0] == 0x80) ? 1u : 0u;
        _ = Dsp56300Arithmetic.Abs56(dest);
        StoreAcc(destReg, dest);

        ClearSr(1u << Dsp56300State.SrV);
        R[Dsp56300State.RegSr] |= (overflowed << Dsp56300State.SrL) | (overflowed << Dsp56300State.SrV);
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void NegA() => NegAcc(Dsp56300State.RegA);

    private void NegB() => NegAcc(Dsp56300State.RegB);

    private void NegAcc(uint destReg)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, sourceValue);

        uint overflowed = (sourceValue[2] == 0 && sourceValue[1] == 0 && sourceValue[0] == 0x80) ? 1u : 0u;
        dest[0] = 0;
        dest[1] = 0;
        dest[2] = 0;
        _ = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        StoreAcc(destReg, dest);

        ClearSr(1u << Dsp56300State.SrV);
        R[Dsp56300State.RegSr] |= (overflowed << Dsp56300State.SrL) | (overflowed << Dsp56300State.SrV);
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void NotA() => NotAcc(Dsp56300State.RegA);

    private void NotB() => NotAcc(Dsp56300State.RegB);

    private void NotAcc(uint destReg)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        R[dest] = ~R[dest];
        R[dest] &= Mask(24);

        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dest] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void RndA() => RoundAcc(Dsp56300State.RegA);

    private void RndB() => RoundAcc(Dsp56300State.RegB);

    private void RoundAcc(uint destReg)
    {
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        Dsp56300Arithmetic.Round56(State, dest);
        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void ClrA() => ClearAcc(Dsp56300State.RegA);

    private void ClrB() => ClearAcc(Dsp56300State.RegB);

    private void ClearAcc(uint destReg)
    {
        uint offset = destReg & 1;
        R[Dsp56300State.RegA2 + offset] = 0;
        R[Dsp56300State.RegA1 + offset] = 0;
        R[Dsp56300State.RegA0 + offset] = 0;

        ClearSr((1u << Dsp56300State.SrE) | (1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= (1u << Dsp56300State.SrU) | (1u << Dsp56300State.SrZ);
    }

    private void TstA() => TestAcc(Dsp56300State.RegA);

    private void TstB() => TestAcc(Dsp56300State.RegB);

    private void TestAcc(uint destReg)
    {
        uint offset = destReg & 1;
        UpdateEunz(R[Dsp56300State.RegA2 + offset], R[Dsp56300State.RegA1 + offset], R[Dsp56300State.RegA0 + offset]);
        ClearSr(1u << Dsp56300State.SrV);
    }

    private void AslA() => ShiftAcc56(Dsp56300State.RegA, left: true);

    private void AslB() => ShiftAcc56(Dsp56300State.RegB, left: true);

    private void AsrA() => ShiftAcc56(Dsp56300State.RegA, left: false);

    private void AsrB() => ShiftAcc56(Dsp56300State.RegB, left: false);

    private void ShiftAcc56(uint destReg, bool left)
    {
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);
        ushort newsr = left
            ? Dsp56300Arithmetic.Asl56(dest, 1)
            : Dsp56300Arithmetic.Asr56(dest, 1);
        StoreAcc(destReg, dest);

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= newsr;
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void LslA() => ShiftLogical(Dsp56300State.RegA, left: true);

    private void LslB() => ShiftLogical(Dsp56300State.RegB, left: true);

    private void LsrA() => ShiftLogical(Dsp56300State.RegA, left: false);

    private void LsrB() => ShiftLogical(Dsp56300State.RegB, left: false);

    private void ShiftLogical(uint destReg, bool left)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        uint newcarry = left ? (R[dest] >> 23) & 1 : R[dest] & 1;

        R[dest] = left ? (R[dest] << 1) & Mask(24) : R[dest] >> 1;

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrN)
            | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= newcarry;
        if (left)
        {
            R[Dsp56300State.RegSr] |= ((R[dest] >> 23) & 1) << Dsp56300State.SrN;
        }

        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void RolA() => RotateAcc(Dsp56300State.RegA, left: true);

    private void RolB() => RotateAcc(Dsp56300State.RegB, left: true);

    private void RorA() => RotateAcc(Dsp56300State.RegA, left: false);

    private void RorB() => RotateAcc(Dsp56300State.RegB, left: false);

    private void RotateAcc(uint destReg, bool left)
    {
        uint dest = destReg == Dsp56300State.RegA ? (uint)Dsp56300State.RegA1 : Dsp56300State.RegB1;
        uint newcarry = left ? (R[dest] >> 23) & 1 : R[dest] & 1;

        if (left)
        {
            R[dest] <<= 1;
            R[dest] |= newcarry;
            R[dest] &= Mask(24);
        }
        else
        {
            R[dest] >>= 1;
            R[dest] |= newcarry << 23;
        }

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrN)
            | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= newcarry;
        R[Dsp56300State.RegSr] |= (left ? ((R[dest] >> 23) & 1) : newcarry) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dest] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void Max()
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(Dsp56300State.RegA, dest);
        LoadAcc(Dsp56300State.RegB, sourceValue);

        _ = Dsp56300Arithmetic.Sub56(sourceValue, dest);

        bool pass = Flag(dest[0], 7) || (dest[0] == 0 && dest[1] == 0 && dest[2] == 0);
        if (pass)
        {
            // The reference stores A2:A1:A0 into B0:B1:B2; keep it verbatim.
            R[Dsp56300State.RegB0] = R[Dsp56300State.RegA2];
            R[Dsp56300State.RegB1] = R[Dsp56300State.RegA1];
            R[Dsp56300State.RegB2] = R[Dsp56300State.RegA0];
        }

        ClearSr(1u << Dsp56300State.SrC);
        R[Dsp56300State.RegSr] |= (pass ? 1u : 0u) << Dsp56300State.SrC;
    }

    private void Move()
    {
        // The reference's ALU 0x00 entry is an empty handler.
    }

    private void MpyMac(uint source1, uint source2, uint sign, uint destReg, bool round, bool accumulate)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];

        Dsp56300Arithmetic.Mul56(R[source1], R[source2], sourceValue, (byte)sign);

        if (accumulate)
        {
            LoadAcc(destReg, dest);
            ushort newsr = Dsp56300Arithmetic.Add56(sourceValue, dest);
            if (round)
            {
                Dsp56300Arithmetic.Round56(State, dest);
            }

            StoreAcc(destReg, dest);
            UpdateEunz(dest[0], dest[1], dest[2]);

            ClearSr(1u << Dsp56300State.SrV);
            R[Dsp56300State.RegSr] |= newsr & 0xFEu;
        }
        else
        {
            if (round)
            {
                Dsp56300Arithmetic.Round56(State, sourceValue);
            }

            StoreAcc(destReg, sourceValue);
            UpdateEunz(sourceValue[0], sourceValue[1], sourceValue[2]);
            ClearSr(1u << Dsp56300State.SrV);
        }
    }
}
