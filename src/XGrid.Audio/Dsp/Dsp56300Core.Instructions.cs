// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc.
// Original copyright (c) 2015 espes.
// Adapted from Hatari DSP M56001 emulation (C) 2003-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// The DSP56300 non-parallel instruction handlers (xemu <c>emu_*</c> bodies).
/// </summary>
public sealed partial class Dsp56300Core
{
    private static readonly int[,] RegistersTcc =
    {
        { Dsp56300State.RegB, Dsp56300State.RegA },
        { Dsp56300State.RegA, Dsp56300State.RegB },
        { 0, 0 },
        { 0, 0 },

        { 0, 0 },
        { 0, 0 },
        { 0, 0 },
        { 0, 0 },

        { Dsp56300State.RegX0, Dsp56300State.RegA },
        { Dsp56300State.RegX0, Dsp56300State.RegB },
        { Dsp56300State.RegY0, Dsp56300State.RegA },
        { Dsp56300State.RegY0, Dsp56300State.RegB },

        { Dsp56300State.RegX1, Dsp56300State.RegA },
        { Dsp56300State.RegX1, Dsp56300State.RegB },
        { Dsp56300State.RegY1, Dsp56300State.RegA },
        { Dsp56300State.RegY1, Dsp56300State.RegB },
    };

    /// <summary>Gets or sets the diagnostic log sink (STOP/WAIT notices).</summary>
    public Action<string>? Log { get; set; }

    private void Nop()
    {
    }

    /// <summary>Reads a word with an unsigned space id (the opcode fields are unsigned).</summary>
    /// <param name="space">The memory space.</param>
    /// <param name="address">The word address.</param>
    /// <returns>The word.</returns>
    private uint ReadMemory(uint space, uint address) => ReadMemory((int)space, address);

    /// <summary>Writes a word with an unsigned space id.</summary>
    /// <param name="space">The memory space.</param>
    /// <param name="address">The word address.</param>
    /// <param name="value">The word.</param>
    private void WriteMemory(uint space, uint address, uint value) => WriteMemory((int)space, address, value);

    private void AddX(uint x, uint d)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        uint destReg = d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA;
        LoadAcc(destReg, dest);

        sourceValue[2] = 0;
        sourceValue[1] = x;
        sourceValue[0] = Flag(sourceValue[1], 23) ? 0xFFu : 0u;

        ushort newsr = Dsp56300Arithmetic.Add56(sourceValue, dest);
        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void AddImm()
    {
        uint xx = (State.CurrentInstruction >> 8) & Mask(6);
        uint d = (State.CurrentInstruction >> 3) & 1;
        AddX(xx, d);
    }

    private void AddLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint d = (State.CurrentInstruction >> 3) & 1;
        AddX(xxxx, d);
    }

    private void AndX(uint x, uint d)
    {
        uint dstreg = d != 0 ? (uint)Dsp56300State.RegB1 : Dsp56300State.RegA1;
        R[dstreg] &= x;

        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dstreg] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dstreg] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void AndImm()
    {
        uint xx = (State.CurrentInstruction >> 8) & Mask(6);
        uint d = (State.CurrentInstruction >> 3) & 1;
        AndX(xx, d);
    }

    private void AndLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint d = (State.CurrentInstruction >> 3) & 1;
        AndX(xxxx, d);
    }

    private void Andi()
    {
        uint value = (State.CurrentInstruction >> 8) & Mask(8);
        uint regnum = State.CurrentInstruction & Mask(2);
        switch (regnum)
        {
            case 0:
                R[Dsp56300State.RegSr] &= (value << 8) | Mask(8);
                break;
            case 1:
                R[Dsp56300State.RegSr] &= (Mask(8) << 8) | value;
                break;
            case 2:
                R[Dsp56300State.RegOmr] &= value;
                break;
        }
    }

    private void AslImm()
    {
        uint s = (State.CurrentInstruction >> 7) & 1;
        uint d = State.CurrentInstruction & 1;
        uint ii = (State.CurrentInstruction >> 1) & Mask(6);
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(s != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        ushort newsr = Dsp56300Arithmetic.Asl56(dest, (int)ii);
        StoreAcc(d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= newsr;
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void AsrImm()
    {
        uint s = (State.CurrentInstruction >> 7) & 1;
        uint d = State.CurrentInstruction & 1;
        uint ii = (State.CurrentInstruction >> 1) & Mask(6);
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(s != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        ushort newsr = Dsp56300Arithmetic.Asr56(dest, (int)ii);
        StoreAcc(d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= newsr;
        UpdateEunz(dest[0], dest[1], dest[2]);
    }

    private void LslImm()
    {
        uint d = State.CurrentInstruction & 1;
        uint ii = (State.CurrentInstruction >> 1) & Mask(5);
        while (ii-- != 0)
        {
            if (d != 0)
            {
                LslB();
            }
            else
            {
                LslA();
            }
        }
    }

    private void BccLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint ccCode = State.CurrentInstruction & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            State.Pc += xxxx;
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BccImm()
    {
        uint xxx = (State.CurrentInstruction & Mask(5))
            + ((State.CurrentInstruction & (Mask(4) << 6)) >> 1);
        uint ccCode = (State.CurrentInstruction >> 12) & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            State.Pc += Dsp56300Arithmetic.SignExtend(9, xxx);
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BchgAa() => BitChangeAa();

    private void BchgEa() => BitChangeEa();

    private void BchgPp() => BitChangePp();

    private void BchgReg() => BitChangeReg();

    private void BclrAa() => BitClearAa();

    private void BclrEa() => BitClearEa();

    private void BclrPp() => BitClearPp();

    private void BclrReg() => BitClearReg();

    private void BsetAa() => BitSetAa();

    private void BsetEa() => BitSetEa();

    private void BsetPp() => BitSetPp();

    private void BsetReg() => BitSetReg();

    private void BtstAa() => BitTestAa();

    private void BtstEa() => BitTestEa();

    private void BtstPp() => BitTestPp();

    private void BtstReg() => BitTestReg();

    private uint BitAddressAa() => (State.CurrentInstruction >> 8) & Mask(6);

    private uint BitAddressPp() => 0xFFFFC0 + ((State.CurrentInstruction >> 8) & Mask(6));

    private uint BitNumber() => State.CurrentInstruction & Mask(5);

    private uint BitMemorySpace() => (State.CurrentInstruction >> 6) & 1;

    private uint BitRegister() => (State.CurrentInstruction >> 8) & Mask(6);

    private void SetCarry(uint value)
    {
        ClearSr(1u << Dsp56300State.SrC);
        R[Dsp56300State.RegSr] |= value << Dsp56300State.SrC;
    }

    private void BitChangeAa()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressAa();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value = newcarry != 0 ? value - (1u << (int)numbit) : value + (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitChangeEa()
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value = newcarry != 0 ? value - (1u << (int)numbit) : value + (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitChangePp()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value = newcarry != 0 ? value - (1u << (int)numbit) : value + (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitChangeReg()
    {
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        uint newcarry = (value >> (int)numbit) & 1;

        value = newcarry != 0 ? value - (1u << (int)numbit) : value + (1u << (int)numbit);
        WriteReg(numreg, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitClearAa()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressAa();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value &= 0xFFFFFFFFu - (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitClearEa()
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value &= 0xFFFFFFFFu - (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitClearPp()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value &= 0xFFFFFFFFu - (1u << (int)numbit);
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitClearReg()
    {
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        uint newcarry = (value >> (int)numbit) & 1;

        value &= 0xFFFFFFFFu - (1u << (int)numbit);
        WriteReg(numreg, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitSetAa()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressAa();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value |= 1u << (int)numbit;
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitSetEa()
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value |= 1u << (int)numbit;
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitSetPp()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        value |= 1u << (int)numbit;
        WriteMemory(space, addr, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitSetReg()
    {
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        uint newcarry = (value >> (int)numbit) & 1;

        value |= 1u << (int)numbit;
        WriteReg(numreg, value);
        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitTestAa()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressAa();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitTestEa()
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitTestPp()
    {
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        uint newcarry = (value >> (int)numbit) & 1;

        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BitTestReg()
    {
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        uint newcarry = (value >> (int)numbit) & 1;

        SetCarry(newcarry);
        State.InstrCycle += 2;
    }

    private void BraLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        State.Pc += xxxx;
        State.Pc &= Mask(24);
        State.CurrentInstructionLength = 0;
    }

    private void BraImm()
    {
        uint xxx = (State.CurrentInstruction & Mask(5))
            + ((State.CurrentInstruction & (Mask(4) << 6)) >> 1);
        State.Pc += Dsp56300Arithmetic.SignExtend(9, xxx);
        State.Pc &= Mask(24);
        State.CurrentInstructionLength = 0;
    }

    private void BrclrPp()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) == 0)
        {
            State.Pc += xxxx;
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BrclrReg()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) == 0)
        {
            State.Pc += xxxx;
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BrsetPp()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint space = BitMemorySpace();
        uint addr = BitAddressPp();
        uint numbit = BitNumber();
        uint value = ReadMemory(space, addr);
        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) != 0)
        {
            State.Pc += xxxx;
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BrsetReg()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint numreg = BitRegister();
        uint numbit = BitNumber();
        uint value = ReadReg24(numreg);
        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) != 0)
        {
            State.Pc += xxxx;
            State.Pc &= Mask(24);
            State.CurrentInstructionLength = 0;
        }
    }

    private void BsrLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        if (State.InterruptState != Dsp56300State.InterruptLong)
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        }
        else
        {
            State.InterruptState = Dsp56300State.InterruptDisabled;
        }

        State.Pc += xxxx;
        State.Pc &= Mask(24);
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 4;
    }

    private void BsrImm()
    {
        uint xxx = (State.CurrentInstruction & Mask(5))
            + ((State.CurrentInstruction & (Mask(4) << 6)) >> 1);
        if (State.InterruptState != Dsp56300State.InterruptLong)
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        }
        else
        {
            State.InterruptState = Dsp56300State.InterruptDisabled;
        }

        State.Pc += Dsp56300Arithmetic.SignExtend(9, xxx);
        State.Pc &= Mask(24);
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 2;
    }

    private void CmpImm()
    {
        uint xx = (State.CurrentInstruction >> 8) & Mask(6);
        uint d = (State.CurrentInstruction >> 3) & 1;
        CompareImmediate(xx, d);
    }

    private void CmpLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint d = (State.CurrentInstruction >> 3) & 1;
        CompareImmediate(xxxx, d);
    }

    private void CompareImmediate(uint value, uint d)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        sourceValue[2] = 0;
        sourceValue[1] = value;
        sourceValue[0] = 0;

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void Cmpu()
    {
        uint ggg = (State.CurrentInstruction >> 1) & Mask(3);
        uint d = State.CurrentInstruction & 1;
        uint srcreg = 0;

        switch (ggg)
        {
            case 0:
                srcreg = d != 0 ? (uint)Dsp56300State.RegA : Dsp56300State.RegB;
                break;
            case 4:
                srcreg = Dsp56300State.RegX0;
                break;
            case 5:
                srcreg = Dsp56300State.RegY0;
                break;
            case 6:
                srcreg = Dsp56300State.RegX1;
                break;
            case 7:
                srcreg = Dsp56300State.RegY1;
                break;
        }

        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, dest);

        uint value = ReadReg24(srcreg);
        sourceValue[2] = 0;
        sourceValue[1] = value;
        sourceValue[0] = Flag(sourceValue[1], 23) ? 0xFFu : 0u;

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC)
            | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrN));
        R[Dsp56300State.RegSr] |= newsr & (1u << Dsp56300State.SrC);

        if (dest[0] == 0 && dest[2] == 0 && dest[1] == 0)
        {
            R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrZ;
        }

        R[Dsp56300State.RegSr] |= (dest[0] >> 4) & 0x8;
    }

    private void Dec() => IncrementAccumulator(-1);

    private void Inc() => IncrementAccumulator(1);

    private void IncrementAccumulator(int delta)
    {
        uint destreg = State.CurrentInstruction & 1;
        uint destReg = Dsp56300State.RegA + destreg;
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];

        LoadAcc(destReg, dest);
        sourceValue[2] = 1;
        sourceValue[1] = 0;
        sourceValue[0] = 0;

        ushort newsr = delta > 0
            ? Dsp56300Arithmetic.Add56(sourceValue, dest)
            : Dsp56300Arithmetic.Sub56(sourceValue, dest);

        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void Div()
    {
        uint srcreg = (uint)((State.CurrentInstruction >> 4) & Mask(2)) switch
        {
            0 => Dsp56300State.RegX0,
            1 => Dsp56300State.RegY0,
            2 => Dsp56300State.RegX1,
            _ => Dsp56300State.RegY1,
        };

        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];

        sourceValue[2] = 0;
        sourceValue[1] = R[srcreg];
        sourceValue[0] = Flag(sourceValue[1], 23) ? 0xFFu : 0u;

        uint destreg = (State.CurrentInstruction >> 3) & 1;
        uint destReg = Dsp56300State.RegA + destreg;
        LoadAcc(destReg, dest);

        ushort newsr;
        if ((((dest[0] >> 7) & 1) ^ ((sourceValue[1] >> 23) & 1)) != 0)
        {
            newsr = Dsp56300Arithmetic.Asl56(dest, 1);
            _ = Dsp56300Arithmetic.Add56(sourceValue, dest);
        }
        else
        {
            newsr = Dsp56300Arithmetic.Asl56(dest, 1);
            _ = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        }

        dest[2] |= (R[Dsp56300State.RegSr] >> Dsp56300State.SrC) & 1;
        StoreAcc(destReg, dest);

        ClearSr((1u << Dsp56300State.SrC) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= (1 - ((dest[0] >> 7) & 1)) << Dsp56300State.SrC;
        R[Dsp56300State.RegSr] |= newsr & (1u << Dsp56300State.SrL);
        R[Dsp56300State.RegSr] |= newsr & (1u << Dsp56300State.SrV);
    }

    private void DoAa()
    {
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = ReadMemoryP(State.Pc + 1) & Mask(16);
        State.CurrentInstructionLength++;
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;

        uint space = (State.CurrentInstruction >> 6) & 1;
        uint addr = (State.CurrentInstruction >> 8) & Mask(6);
        R[Dsp56300State.RegLc] = ReadMemory(space, addr) & Mask(16);
        State.InstrCycle += 4;
    }

    private void DoImm()
    {
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = ReadMemoryP(State.Pc + 1) & Mask(16);
        State.CurrentInstructionLength++;
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;

        R[Dsp56300State.RegLc] = ((State.CurrentInstruction >> 8) & Mask(8))
            + ((State.CurrentInstruction & Mask(4)) << 8);
        State.InstrCycle += 4;
    }

    private void DoEa()
    {
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = ReadMemoryP(State.Pc + 1) & Mask(16);
        State.CurrentInstructionLength++;
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;

        uint space = (State.CurrentInstruction >> 6) & 1;
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
        R[Dsp56300State.RegLc] = ReadMemory(space, addr) & Mask(16);
        State.InstrCycle += 4;
    }

    private void DoReg()
    {
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = ReadMemoryP(State.Pc + 1) & Mask(16);
        State.CurrentInstructionLength++;

        uint numreg = (State.CurrentInstruction >> 8) & Mask(6);
        if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
        {
            _ = PmReadAccu24(numreg, out R[Dsp56300State.RegLc]);
        }
        else
        {
            R[Dsp56300State.RegLc] = R[numreg];
        }

        R[Dsp56300State.RegLc] &= Mask(16);
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;
        State.InstrCycle += 4;
    }

    private void DorImm()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = (State.Pc + xxxx) & Mask(16);
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;

        R[Dsp56300State.RegLc] = ((State.CurrentInstruction >> 8) & Mask(8))
            + ((State.CurrentInstruction & Mask(4)) << 8);
        State.InstrCycle += 4;
    }

    private void DorReg()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        StackPush(R[Dsp56300State.RegLa], R[Dsp56300State.RegLc], sshOnly: false);
        R[Dsp56300State.RegLa] = (State.Pc + xxxx) & Mask(16);
        StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrLf;

        uint numreg = (State.CurrentInstruction >> 8) & Mask(6);
        if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
        {
            _ = PmReadAccu24(numreg, out R[Dsp56300State.RegLc]);
        }
        else
        {
            R[Dsp56300State.RegLc] = R[numreg];
        }

        R[Dsp56300State.RegLc] &= Mask(16);
        State.InstrCycle += 4;
    }

    private void Enddo()
    {
        StackPop(out _, out uint savedSr);
        R[Dsp56300State.RegSr] &= 0x7F;
        R[Dsp56300State.RegSr] |= savedSr & (1u << Dsp56300State.SrLf);
        StackPop(out R[Dsp56300State.RegLa], out R[Dsp56300State.RegLc]);
    }

    private void Illegal()
    {
        AddInterrupt(Dsp56300State.InterIllegal);
        if (State.ExceptionDebugging)
        {
            throw new DspIllegalInstructionException(State.Pc, State.CurrentInstruction);
        }
    }

    private void JccImm()
    {
        uint newpc = State.CurrentInstruction & Mask(12);
        uint ccCode = (State.CurrentInstruction >> 12) & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            State.Pc = newpc;
            State.CurrentInstructionLength = 0;
        }

        State.InstrCycle += 2;
    }

    private void JccEa()
    {
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint newpc);
        uint ccCode = State.CurrentInstruction & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            State.Pc = newpc;
            State.CurrentInstructionLength = 0;
        }

        State.InstrCycle += 2;
    }

    private void JmpEa()
    {
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint newpc);
        State.CurrentInstructionLength = 0;
        State.Pc = newpc;
        State.InstrCycle += 2;
    }

    private void JmpImm()
    {
        uint newpc = State.CurrentInstruction & Mask(12);
        State.CurrentInstructionLength = 0;
        State.Pc = newpc;
        State.InstrCycle += 2;
    }

    private void JsccEa()
    {
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint newpc);
        uint ccCode = State.CurrentInstruction & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
            State.Pc = newpc;
            State.CurrentInstructionLength = 0;
        }

        State.InstrCycle += 2;
    }

    private void JsccImm()
    {
        uint newpc = State.CurrentInstruction & Mask(12);
        uint ccCode = (State.CurrentInstruction >> 12) & Mask(4);
        if (CalcConditionCode(ccCode))
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
            State.Pc = newpc;
            State.CurrentInstructionLength = 0;
        }

        State.InstrCycle += 2;
    }

    private void JsrImm()
    {
        uint newpc = State.CurrentInstruction & Mask(12);
        if (State.InterruptState != Dsp56300State.InterruptLong)
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        }
        else
        {
            State.InterruptState = Dsp56300State.InterruptDisabled;
        }

        State.Pc = newpc;
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 2;
    }

    private void JsrEa()
    {
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint newpc);
        if (State.InterruptState != Dsp56300State.InterruptLong)
        {
            StackPush(State.Pc + State.CurrentInstructionLength, R[Dsp56300State.RegSr], sshOnly: false);
        }
        else
        {
            State.InterruptState = Dsp56300State.InterruptDisabled;
        }

        State.Pc = newpc;
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 2;
    }

    private void JclrAa() => JumpOnBit(clear: true, effectiveAddress: false, port: false, register: false);

    private void JclrEa() => JumpOnBit(clear: true, effectiveAddress: true, port: false, register: false);

    private void JclrPp() => JumpOnBit(clear: true, effectiveAddress: false, port: true, register: false);

    private void JclrReg() => JumpOnBit(clear: true, effectiveAddress: false, port: false, register: true);

    private void JsetAa() => JumpOnBit(clear: false, effectiveAddress: false, port: false, register: false);

    private void JsetEa() => JumpOnBit(clear: false, effectiveAddress: true, port: false, register: false);

    private void JsetPp() => JumpOnBit(clear: false, effectiveAddress: false, port: true, register: false);

    private void JsetReg() => JumpOnBit(clear: false, effectiveAddress: false, port: false, register: true);

    private void JsclrAa() => JumpSubroutineOnBit(clear: true, effectiveAddress: false, port: false, register: false);

    private void JsclrEa() => JumpSubroutineOnBit(clear: true, effectiveAddress: true, port: false, register: false);

    private void JsclrPp() => JumpSubroutineOnBit(clear: true, effectiveAddress: false, port: true, register: false);

    private void JsclrReg() => JumpSubroutineOnBit(clear: true, effectiveAddress: false, port: false, register: true);

    private void JssetAa() => JumpSubroutineOnBit(clear: false, effectiveAddress: false, port: false, register: false);

    private void JssetEa() => JumpSubroutineOnBit(clear: false, effectiveAddress: true, port: false, register: false);

    private void JssetPp() => JumpSubroutineOnBit(clear: false, effectiveAddress: false, port: true, register: false);

    private void JssetReg() => JumpSubroutineOnBit(clear: false, effectiveAddress: false, port: false, register: true);

    private void JumpOnBit(bool clear, bool effectiveAddress, bool port, bool register)
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        uint newaddr = ReadMemoryP(State.Pc + 1);
        uint value;

        if (register)
        {
            value = ReadReg24(BitRegister());
        }
        else if (effectiveAddress)
        {
            _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
            value = ReadMemory(space, addr);
        }
        else
        {
            value = ReadMemory(space, port ? BitAddressPp() : BitAddressAa());
        }

        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) == (clear ? 0u : 1u << (int)numbit))
        {
            State.Pc = newaddr;
            State.CurrentInstructionLength = 0;
            return;
        }

        State.CurrentInstructionLength++;
    }

    private void JumpSubroutineOnBit(bool clear, bool effectiveAddress, bool port, bool register)
    {
        uint space = BitMemorySpace();
        uint numbit = BitNumber();
        uint newaddr = ReadMemoryP(State.Pc + 1);
        uint value;

        if (register)
        {
            value = ReadReg24(BitRegister());
        }
        else if (effectiveAddress)
        {
            _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);
            value = ReadMemory(space, addr);
        }
        else
        {
            value = ReadMemory(space, port ? BitAddressPp() : BitAddressAa());
        }

        State.InstrCycle += 4;
        if ((value & (1u << (int)numbit)) == (clear ? 0u : 1u << (int)numbit))
        {
            StackPush(State.Pc + 2, R[Dsp56300State.RegSr], sshOnly: false);
            State.Pc = newaddr;
            State.CurrentInstructionLength = 0;
            return;
        }

        State.CurrentInstructionLength++;
    }

    private void Lua()
    {
        uint srcreg = (State.CurrentInstruction >> 8) & Mask(3);
        uint srcsave = R[Dsp56300State.RegR0 + srcreg];
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(5), out uint value);
        uint srcnew = R[Dsp56300State.RegR0 + srcreg];
        R[Dsp56300State.RegR0 + srcreg] = srcsave;

        uint dstreg = State.CurrentInstruction & Mask(3);
        if ((State.CurrentInstruction & (1u << 3)) != 0)
        {
            R[Dsp56300State.RegN0 + dstreg] = srcnew;
        }
        else
        {
            R[Dsp56300State.RegR0 + dstreg] = srcnew;
        }

        State.InstrCycle += 2;
    }

    private void LuaRel()
    {
        uint aa = ((State.CurrentInstruction >> 4) & Mask(4))
            + (((State.CurrentInstruction >> 11) & Mask(3)) << 4);
        uint addrreg = (State.CurrentInstruction >> 8) & Mask(3);
        uint dstreg = State.CurrentInstruction & Mask(3);
        uint value = (R[Dsp56300State.RegR0 + addrreg] + Dsp56300Arithmetic.SignExtend(7, aa)) & Mask(24);

        if ((State.CurrentInstruction & (1u << 3)) != 0)
        {
            R[Dsp56300State.RegN0 + dstreg] = value;
        }
        else
        {
            R[Dsp56300State.RegR0 + dstreg] = value;
        }

        State.InstrCycle += 2;
    }

    private void MovecReg()
    {
        uint numreg2 = (State.CurrentInstruction >> 8) & Mask(6);
        uint numreg1 = State.CurrentInstruction & Mask(6);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            uint value = ReadReg24(numreg2);
            value &= Mask((int)RegisterMask[numreg1]);
            WriteReg(numreg1, value);
        }
        else
        {
            uint value;
            if (numreg1 == Dsp56300State.RegSsh)
            {
                StackPop(out value, out _);
            }
            else
            {
                value = R[numreg1];
            }

            WriteRegOrAccumulator(numreg2, value);
        }
    }

    private void MovecAa()
    {
        uint numreg = State.CurrentInstruction & Mask(6);
        uint addr = (State.CurrentInstruction >> 8) & Mask(6);
        uint space = (State.CurrentInstruction >> 6) & 1;

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            uint value = ReadMemory(space, addr);
            value &= Mask((int)RegisterMask[numreg]);
            WriteReg(numreg, value);
        }
        else
        {
            uint value;
            if (numreg == Dsp56300State.RegSsh)
            {
                StackPop(out value, out _);
            }
            else
            {
                value = R[numreg];
            }

            WriteMemory(space, addr, value);
        }
    }

    private void MovecImm()
    {
        uint numreg = State.CurrentInstruction & Mask(6);
        uint value = (State.CurrentInstruction >> 8) & Mask(8);
        value &= Mask((int)RegisterMask[numreg]);
        WriteReg(numreg, value);
    }

    private void MovecEa()
    {
        uint numreg = State.CurrentInstruction & Mask(6);
        uint eaMode = (State.CurrentInstruction >> 8) & Mask(6);
        uint space = (State.CurrentInstruction >> 6) & 1;

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            int retour = CalcEffectiveAddress(eaMode, out uint addr);
            uint value = retour != 0 ? addr : ReadMemory(space, addr);
            value &= Mask((int)RegisterMask[numreg]);
            WriteReg(numreg, value);
        }
        else
        {
            _ = CalcEffectiveAddress(eaMode, out uint addr);
            uint value;
            if (numreg == Dsp56300State.RegSsh)
            {
                StackPop(out value, out _);
            }
            else
            {
                value = R[numreg];
            }

            WriteMemory(space, addr, value);
        }
    }

    private void WriteRegOrAccumulator(uint numreg, uint value)
    {
        if (numreg == Dsp56300State.RegA)
        {
            R[Dsp56300State.RegA0] = 0;
            R[Dsp56300State.RegA1] = value & Mask(24);
            R[Dsp56300State.RegA2] = Flag(value, 23) ? 0xFFu : 0u;
        }
        else if (numreg == Dsp56300State.RegB)
        {
            R[Dsp56300State.RegB0] = 0;
            R[Dsp56300State.RegB1] = value & Mask(24);
            R[Dsp56300State.RegB2] = Flag(value, 23) ? 0xFFu : 0u;
        }
        else
        {
            R[numreg] = value & Mask((int)RegisterMask[numreg]);
        }
    }

    private void MovemAa()
    {
        uint numreg = State.CurrentInstruction & Mask(6);
        uint addr = (State.CurrentInstruction >> 8) & Mask(6);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            uint value = ReadMemoryP(addr);
            value &= Mask((int)RegisterMask[numreg]);
            WriteReg(numreg, value);
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceP, addr, ReadMovemSource(numreg));
        }

        State.InstrCycle += 4;
    }

    private void MovemEa()
    {
        uint numreg = State.CurrentInstruction & Mask(6);
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint addr);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            uint value = ReadMemoryP(addr);
            value &= Mask((int)RegisterMask[numreg]);
            WriteReg(numreg, value);
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceP, addr, ReadMovemSource(numreg));
        }

        State.InstrCycle += 4;
    }

    private uint ReadMovemSource(uint numreg)
    {
        if (numreg == Dsp56300State.RegSsh)
        {
            StackPop(out uint value, out _);
            return value;
        }

        if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
        {
            _ = PmReadAccu24(numreg, out uint value);
            return value;
        }

        return R[numreg];
    }

    private void Movep0()
    {
        uint addr = 0xFFFFC0 + (State.CurrentInstruction & Mask(6));
        uint space = (State.CurrentInstruction >> 16) & 1;
        uint numreg = (State.CurrentInstruction >> 8) & Mask(6);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            uint value;
            if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
            {
                _ = PmReadAccu24(numreg, out value);
            }
            else if (numreg == Dsp56300State.RegSsh)
            {
                StackPop(out value, out _);
            }
            else
            {
                value = R[numreg];
            }

            WriteMemory(space, addr, value);
        }
        else
        {
            uint value = ReadMemory(space, addr);
            value &= Mask((int)RegisterMask[numreg]);
            WriteReg(numreg, value);
        }

        State.InstrCycle += 2;
    }

    private void Movep1()
    {
        uint xyaddr = 0xFFFFC0 + (State.CurrentInstruction & Mask(6));
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint paddr);
        uint space = (State.CurrentInstruction >> 16) & 1;

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            WriteMemory(space, xyaddr, ReadMemoryP(paddr));
        }
        else
        {
            WriteMemory(Dsp56300State.SpaceP, paddr, ReadMemory(space, xyaddr));
        }

        State.InstrCycle += 4;
    }

    private void Movep23()
    {
        uint peraddr = 0xFFFFC0 + (State.CurrentInstruction & Mask(6));
        uint perspace = (State.CurrentInstruction >> 16) & 1;
        uint eaMode = (State.CurrentInstruction >> 8) & Mask(6);
        uint easpace = (State.CurrentInstruction >> 6) & 1;
        int retour = CalcEffectiveAddress(eaMode, out uint addr);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            if (retour != 0)
            {
                WriteMemory(perspace, peraddr, addr);
            }
            else
            {
                WriteMemory(perspace, peraddr, ReadMemory(easpace, addr));
            }
        }
        else
        {
            WriteMemory(easpace, addr, ReadMemory(perspace, peraddr));
        }

        State.InstrCycle += 2;
    }

    private void MovepXQq()
    {
        uint xAddr = 0xFFFF80 + (State.CurrentInstruction & Mask(6));
        uint eaMode = (State.CurrentInstruction >> 8) & Mask(6);
        uint eaSpace = (State.CurrentInstruction >> 6) & 1;
        int retour = CalcEffectiveAddress(eaMode, out uint eaAddr);

        if ((State.CurrentInstruction & (1u << 15)) != 0)
        {
            if (retour != 0)
            {
                WriteMemory(Dsp56300State.SpaceX, xAddr, eaAddr);
            }
            else
            {
                WriteMemory(Dsp56300State.SpaceX, xAddr, ReadMemory(eaSpace, eaAddr));
            }
        }
        else
        {
            WriteMemory(eaSpace, eaAddr, ReadMemory(Dsp56300State.SpaceX, xAddr));
        }

        State.InstrCycle += 2;
    }

    private void MoveXLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint w = (State.CurrentInstruction >> 6) & 1;
        uint offreg = Dsp56300State.RegR0 + ((State.CurrentInstruction >> 8) & Mask(3));
        uint numreg = State.CurrentInstruction & Mask(6);
        uint xAddr = (R[offreg] + xxxx) & Mask(24);

        if (w == 0)
        {
            WriteMemory(Dsp56300State.SpaceX, xAddr, ReadReg24(numreg));
        }
        else
        {
            WriteReg(numreg, ReadMemory(Dsp56300State.SpaceX, xAddr));
        }
    }

    private void MoveXImm() => MoveXyImm(Dsp56300State.SpaceX);

    private void MoveYImm() => MoveXyImm(Dsp56300State.SpaceY);

    private void MoveXyImm(int space)
    {
        uint xxx = (((State.CurrentInstruction >> 11) & Mask(6)) << 1)
            + ((State.CurrentInstruction >> 6) & 1);
        uint w = (State.CurrentInstruction >> 4) & 1;
        uint offreg = Dsp56300State.RegR0 + ((State.CurrentInstruction >> 8) & Mask(3));
        uint numreg = State.CurrentInstruction & Mask(4);
        uint addr = (R[offreg] + Dsp56300Arithmetic.SignExtend(7, xxx)) & Mask(24);

        if (w == 0)
        {
            WriteMemory(space, addr, ReadReg24(numreg));
        }
        else
        {
            WriteReg(numreg, ReadMemory(space, addr));
        }
    }

    private void Mpyi()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint k = (State.CurrentInstruction >> 2) & 1;
        uint d = (State.CurrentInstruction >> 3) & 1;
        uint qq = (State.CurrentInstruction >> 4) & Mask(2);

        uint srcreg = qq switch
        {
            0 => Dsp56300State.RegX0,
            1 => Dsp56300State.RegY0,
            2 => Dsp56300State.RegX1,
            _ => Dsp56300State.RegY1,
        };

        Span<uint> sourceValue = stackalloc uint[3];
        Dsp56300Arithmetic.Mul56(xxxx, R[srcreg], sourceValue, (byte)(k != 0 ? SignMinus : SignPlus));
        StoreAcc(d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA, sourceValue);

        UpdateEunz(sourceValue[0], sourceValue[1], sourceValue[2]);
        ClearSr(1u << Dsp56300State.SrV);
    }

    private void Norm()
    {
        uint cursr = R[Dsp56300State.RegSr];
        uint curE = (cursr >> Dsp56300State.SrE) & 1;
        uint curEuz = ~curE;
        curEuz &= (cursr >> Dsp56300State.SrU) & 1;
        curEuz &= ~((cursr >> Dsp56300State.SrZ) & 1);
        curEuz &= 1;

        uint numreg = (State.CurrentInstruction >> 3) & 1;
        uint destReg = Dsp56300State.RegA + numreg;
        Span<uint> dest = stackalloc uint[3];
        LoadAcc(destReg, dest);

        uint rreg = Dsp56300State.RegR0 + ((State.CurrentInstruction >> 8) & Mask(3));
        ushort newsr;
        if (curEuz != 0)
        {
            newsr = Dsp56300Arithmetic.Asl56(dest, 1);
            R[rreg]--;
            R[rreg] &= Mask(16);
        }
        else if (curE != 0)
        {
            newsr = Dsp56300Arithmetic.Asr56(dest, 1);
            R[rreg]++;
            R[rreg] &= Mask(16);
        }
        else
        {
            newsr = 0;
        }

        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void OrLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint dstreg = ((State.CurrentInstruction >> 3) & 1) != 0 ? (uint)Dsp56300State.RegB1 : Dsp56300State.RegA1;

        R[dstreg] |= xxxx;
        ClearSr((1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ) | (1u << Dsp56300State.SrV));
        R[Dsp56300State.RegSr] |= ((R[dstreg] >> 23) & 1) << Dsp56300State.SrN;
        R[Dsp56300State.RegSr] |= (R[dstreg] == 0 ? 1u : 0u) << Dsp56300State.SrZ;
    }

    private void Ori()
    {
        uint value = (State.CurrentInstruction >> 8) & Mask(8);
        uint regnum = State.CurrentInstruction & Mask(2);
        switch (regnum)
        {
            case 0:
                R[Dsp56300State.RegSr] |= value << 8;
                break;
            case 1:
                R[Dsp56300State.RegSr] |= value;
                break;
            case 2:
                R[Dsp56300State.RegOmr] |= value;
                break;
        }
    }

    private void RepAa()
    {
        R[Dsp56300State.RegLcSave] = R[Dsp56300State.RegLc];
        State.PcOnRep = true;
        State.LoopRep = 1;
        R[Dsp56300State.RegLc] = ReadMemory((State.CurrentInstruction >> 6) & 1, (State.CurrentInstruction >> 8) & Mask(6));
        State.InstrCycle += 2;
    }

    private void RepImm()
    {
        R[Dsp56300State.RegLcSave] = R[Dsp56300State.RegLc];
        State.PcOnRep = true;
        State.LoopRep = 1;
        R[Dsp56300State.RegLc] = ((State.CurrentInstruction >> 8) & Mask(8))
            + ((State.CurrentInstruction & Mask(4)) << 8);
        State.InstrCycle += 2;
    }

    private void RepEa()
    {
        R[Dsp56300State.RegLcSave] = R[Dsp56300State.RegLc];
        State.PcOnRep = true;
        State.LoopRep = 1;
        _ = CalcEffectiveAddress((State.CurrentInstruction >> 8) & Mask(6), out uint value);
        R[Dsp56300State.RegLc] = ReadMemory((State.CurrentInstruction >> 6) & 1, value);
        State.InstrCycle += 2;
    }

    private void RepReg()
    {
        R[Dsp56300State.RegLcSave] = R[Dsp56300State.RegLc];
        State.PcOnRep = true;
        State.LoopRep = 1;

        uint numreg = (State.CurrentInstruction >> 8) & Mask(6);
        if (numreg == Dsp56300State.RegA || numreg == Dsp56300State.RegB)
        {
            _ = PmReadAccu24(numreg, out R[Dsp56300State.RegLc]);
        }
        else
        {
            R[Dsp56300State.RegLc] = R[numreg];
        }

        R[Dsp56300State.RegLc] &= Mask(16);
        State.InstrCycle += 2;
    }

    private void ResetInstruction()
    {
        State.InstrCycle += 2;
    }

    private void Rti()
    {
        StackPop(out uint newpc, out uint newsr);
        State.Pc = newpc;
        R[Dsp56300State.RegSr] = newsr;
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 2;
    }

    private void Rts()
    {
        StackPop(out uint newpc, out _);
        State.Pc = newpc;
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 2;
    }

    private void Stop()
    {
        Log?.Invoke("DSP: STOP instruction");
    }

    private void Wait()
    {
        Log?.Invoke("DSP: WAIT instruction");
    }

    private void SubX(uint x, uint d)
    {
        Span<uint> sourceValue = stackalloc uint[3];
        Span<uint> dest = stackalloc uint[3];
        uint destReg = d != 0 ? (uint)Dsp56300State.RegB : Dsp56300State.RegA;
        LoadAcc(destReg, dest);

        sourceValue[2] = 0;
        sourceValue[1] = x;
        sourceValue[0] = Flag(sourceValue[1], 23) ? 0xFFu : 0u;

        ushort newsr = Dsp56300Arithmetic.Sub56(sourceValue, dest);
        StoreAcc(destReg, dest);
        UpdateEunz(dest[0], dest[1], dest[2]);

        ClearSr((1u << Dsp56300State.SrV) | (1u << Dsp56300State.SrC));
        R[Dsp56300State.RegSr] |= newsr;
    }

    private void SubImm()
    {
        uint xx = (State.CurrentInstruction >> 8) & Mask(6);
        uint d = (State.CurrentInstruction >> 3) & 1;
        SubX(xx, d);
    }

    private void SubLong()
    {
        uint xxxx = ReadMemoryP(State.Pc + 1);
        State.CurrentInstructionLength++;
        uint d = (State.CurrentInstruction >> 3) & 1;
        SubX(xxxx, d);
    }

    private void Tcc()
    {
        uint ccCode = (State.CurrentInstruction >> 12) & Mask(4);
        if (!CalcConditionCode(ccCode))
        {
            return;
        }

        int index = (int)((State.CurrentInstruction >> 3) & Mask(4));
        uint regsrc1 = (uint)RegistersTcc[index, 0];
        uint regdest1 = (uint)RegistersTcc[index, 1];

        uint val0;
        uint val1;
        uint val2;
        if (regsrc1 == Dsp56300State.RegA)
        {
            val0 = R[Dsp56300State.RegA0];
            val1 = R[Dsp56300State.RegA1];
            val2 = R[Dsp56300State.RegA2];
        }
        else if (regsrc1 == Dsp56300State.RegB)
        {
            val0 = R[Dsp56300State.RegB0];
            val1 = R[Dsp56300State.RegB1];
            val2 = R[Dsp56300State.RegB2];
        }
        else
        {
            val0 = 0;
            val1 = R[regsrc1];
            val2 = Flag(val1, 23) ? 0xFFu : 0u;
        }

        if (regdest1 == Dsp56300State.RegA)
        {
            R[Dsp56300State.RegA2] = val2;
            R[Dsp56300State.RegA1] = val1;
            R[Dsp56300State.RegA0] = val0;
        }
        else
        {
            R[Dsp56300State.RegB2] = val2;
            R[Dsp56300State.RegB1] = val1;
            R[Dsp56300State.RegB0] = val0;
        }

        if ((State.CurrentInstruction & (1u << 16)) != 0)
        {
            uint regsrc2 = Dsp56300State.RegR0 + ((State.CurrentInstruction >> 8) & Mask(3));
            uint regdest2 = Dsp56300State.RegR0 + (State.CurrentInstruction & Mask(3));
            R[regdest2] = R[regsrc2];
        }
    }
}
