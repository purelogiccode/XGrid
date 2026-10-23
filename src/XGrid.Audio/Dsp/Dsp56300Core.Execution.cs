// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c.
// Original copyright (c) 2015 espes; (c) 2020-2025 Matt Borgerson.
// Adapted from Hatari DSP M56001 emulation (C) 2001-2008 ARAnyM developer team.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>
/// DSP56300 execution kernel: instruction fetch/decode, the program counter,
/// the DO/REP loops, the stack and the interrupt pipeline.
/// </summary>
/// <remarks>
/// The structure follows the xemu C interpreter one-to-one so a handler can be
/// checked against the reference source directly: <c>dsp56k_execute_instruction</c>
/// is <see cref="ExecuteInstruction"/>, <c>dsp_postexecute_update_pc</c> is
/// <see cref="PostExecuteUpdatePc"/> and <c>dsp_postexecute_interrupts</c> is
/// <see cref="PostExecuteInterrupts"/>.
/// </remarks>
public sealed partial class Dsp56300Core
{
    private const uint Mask4 = 0x0F;
    private const uint Mask6 = 0x3F;
    private const uint Mask16 = 0xFFFF;

    private const int SpSe = 0x04;
    private const int SpUf = 0x05;

    private static readonly uint[] RegisterMask =
    [
        0, 0, 0, 0,
        24, 24, 24, 24,
        24, 24, 8, 8,
        24, 24, 24, 24,

        16, 16, 16, 16,
        16, 16, 16, 16,
        16, 16, 16, 16,
        16, 16, 16, 16,

        16, 16, 16, 16,
        16, 16, 16, 16,
        0, 0, 0, 0,
        0, 0, 0, 0,

        0, 0, 0, 0,
        0, 0, 0, 0,
        0, 16, 8, 6,
        16, 16, 16, 16,
    ];

    private readonly short[] _pramOpcode = new short[Dsp56300State.PramSize];
    private readonly uint[] _opcodeLookupInstruction = new uint[256];
    private readonly DspOpcode?[] _opcodeLookup = new DspOpcode?[256];
    private int _saveCycles;

    private static uint Mask(int bits) => (1u << bits) - 1;

    private uint[] R => State.Registers;

    private static bool Flag(uint value, int bit) => (value & (1u << bit)) != 0;

    private void ClearSr(uint mask) => R[Dsp56300State.RegSr] &= Mask(16) - mask;

    /// <summary>Executes one instruction (xemu <c>dsp56k_execute_instruction</c>).</summary>
    public void ExecuteInstruction()
    {
        State.CurrentInstruction = ReadMemoryP(State.Pc);
        State.CurrentInstructionLength = 1;
        State.InstrCycle = 2;

        uint instruction = State.CurrentInstruction;
        if (instruction < 0x100000)
        {
            DspOpcode? opcode = LookupPc(State.Pc, instruction);
            if (opcode?.Handler is { } handler)
            {
                handler();
            }
            else
            {
                Undefined();
            }
        }
        else
        {
            // Parallel move: the move handler runs the ALU part itself.
            ParallelMoveInstructions++;
            ParallelMove((int)((instruction >> 20) & Mask4));
        }

        PostExecuteUpdatePc();
        PostExecuteInterrupts();

        State.NumInstructions += State.InstrCycle;
    }

    /// <summary>Runs for at most <paramref name="cycles"/> cycles.</summary>
    /// <param name="cycles">The cycle budget.</param>
    public void Run(int cycles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycles);

        _saveCycles += cycles;
        if (_saveCycles <= 0)
        {
            return;
        }

        while (_saveCycles > 0)
        {
            ExecuteInstruction();
            _saveCycles -= State.InstrCycle;
            State.CycleCount += State.InstrCycle;

            if (State.IsIdle)
            {
                break;
            }
        }
    }

    private DspOpcode? LookupPc(uint pc, uint instruction)
    {
        if (pc >= (uint)_pramOpcode.Length)
        {
            return LookupOpcode(instruction);
        }

        short index = _pramOpcode[pc];
        if (index >= 0)
        {
            return _nonParallel[index];
        }

        DspOpcode? opcode = LookupOpcode(instruction);
        _pramOpcode[pc] = opcode is null ? (short)-1 : (short)opcode.Index;
        return opcode;
    }

    private DspOpcode? LookupOpcode(uint instruction)
    {
        byte tag = (byte)((instruction >> 24) ^ (instruction >> 16) ^ (instruction >> 8) ^ instruction);
        if (_opcodeLookupInstruction[tag] == instruction)
        {
            return _opcodeLookup[tag];
        }

        DspOpcode? found = null;
        foreach (DspOpcode opcode in _nonParallel)
        {
            if ((instruction & opcode.Mask) != opcode.Match)
            {
                continue;
            }

            if (opcode.Matcher is { } matcher && !matcher(instruction))
            {
                continue;
            }

            found = opcode;
            break;
        }

        _opcodeLookupInstruction[tag] = instruction;
        _opcodeLookup[tag] = found;
        return found;
    }

    /// <summary>Gets the number of instructions the decoder could not resolve.</summary>
    public long UndefinedInstructions { get; private set; }

    /// <summary>Gets the first instruction word the decoder could not resolve.</summary>
    public uint FirstUndefinedInstruction { get; private set; }

    /// <summary>Gets the program counter of the first unresolved instruction.</summary>
    public uint FirstUndefinedPc { get; private set; }

    /// <summary>Gets the number of parallel-move instructions executed.</summary>
    public long ParallelMoveInstructions { get; private set; }

    private void Undefined()
    {
        if (UndefinedInstructions == 0)
        {
            FirstUndefinedInstruction = State.CurrentInstruction;
            FirstUndefinedPc = State.Pc;
        }

        UndefinedInstructions++;
        State.CurrentInstructionLength = 0;
        State.InstrCycle += 100;
        if (State.ExceptionDebugging)
        {
            throw new DspUndefinedInstructionException(State.Pc, State.CurrentInstruction);
        }
    }

    private void PostExecuteUpdatePc()
    {
        // When running a REP, the PC must stay on the current instruction.
        if (State.LoopRep != 0)
        {
            if (!State.PcOnRep)
            {
                R[Dsp56300State.RegLc]--;
                R[Dsp56300State.RegLc] &= Mask16;

                if (R[Dsp56300State.RegLc] > 0)
                {
                    State.CurrentInstructionLength = 0;
                }
                else
                {
                    State.LoopRep = 0;
                    R[Dsp56300State.RegLc] = R[Dsp56300State.RegLcSave];
                }
            }
            else
            {
                if (R[Dsp56300State.RegLc] == 0)
                {
                    R[Dsp56300State.RegLc] = 0x010000;
                }

                State.PcOnRep = false;
            }
        }

        State.Pc += State.CurrentInstructionLength;

        // When running a DO loop the end is tested with the updated PC.
        if (Flag(R[Dsp56300State.RegSr], Dsp56300State.SrLf))
        {
            if (State.Pc == R[Dsp56300State.RegLa] + 1)
            {
                R[Dsp56300State.RegLc]--;
                R[Dsp56300State.RegLc] &= Mask16;

                if (R[Dsp56300State.RegLc] == 0)
                {
                    StackPop(out _, out uint savedSr);
                    R[Dsp56300State.RegSr] &= 0x7F;
                    R[Dsp56300State.RegSr] |= savedSr & (1u << Dsp56300State.SrLf);
                    StackPop(out R[Dsp56300State.RegLa], out R[Dsp56300State.RegLc]);
                }
                else
                {
                    State.Pc = R[Dsp56300State.RegSsh];
                }
            }
        }
    }

    private void PostExecuteInterrupts()
    {
        // REP is not interruptible.
        if (State.LoopRep != 0)
        {
            return;
        }

        // A fast interrupt cannot be interrupted.
        if (State.InterruptState == Dsp56300State.InterruptDisabled)
        {
            switch (State.InterruptPipelineCount)
            {
                case 5:
                    State.InterruptPipelineCount--;
                    return;
                case 4:
                    {
                        // Prefetch interrupt instruction 1.
                        State.InterruptSavePc = (ushort)State.Pc;
                        State.Pc = State.InterruptInstrFetch;

                        uint instr = ReadMemoryP(State.InterruptInstrFetch);
                        if (IsLongInterrupt(instr))
                        {
                            State.InterruptState = Dsp56300State.InterruptLong;
                            StackPush(State.InterruptSavePc, R[Dsp56300State.RegSr], sshOnly: false);
                            ClearSr((1u << Dsp56300State.SrLf) | (1u << Dsp56300State.SrFv)
                                | (1u << Dsp56300State.SrS1) | (1u << Dsp56300State.SrS0)
                                | (1u << Dsp56300State.SrI0) | (1u << Dsp56300State.SrI1));
                            R[Dsp56300State.RegSr] |= (uint)State.InterruptIplToRaise << Dsp56300State.SrI0;
                        }

                        State.InterruptPipelineCount--;
                        return;
                    }

                case 3:
                    {
                        // Prefetch interrupt instruction 2.
                        if (State.Pc == State.InterruptInstrFetch + 1)
                        {
                            uint instr = ReadMemoryP(State.Pc);
                            if (IsLongInterrupt(instr))
                            {
                                State.InterruptState = Dsp56300State.InterruptLong;
                                StackPush(State.InterruptSavePc, R[Dsp56300State.RegSr], sshOnly: false);
                                ClearSr((1u << Dsp56300State.SrLf) | (1u << Dsp56300State.SrFv)
                                    | (1u << Dsp56300State.SrS1) | (1u << Dsp56300State.SrS0)
                                    | (1u << Dsp56300State.SrI0) | (1u << Dsp56300State.SrI1));
                                R[Dsp56300State.RegSr] |= (uint)State.InterruptIplToRaise << Dsp56300State.SrI0;
                            }
                        }

                        State.InterruptPipelineCount--;
                        return;
                    }

                case 2:
                    // One instruction executed after the interrupt.
                    if (State.Pc == State.InterruptInstrFetch + 2)
                    {
                        State.Pc = State.InterruptSavePc;
                    }

                    State.InterruptPipelineCount--;
                    return;
                case 1:
                    // Last instruction executed after the interrupt.
                    State.InterruptPipelineCount--;
                    return;
                case 0:
                    // All six instructions are done; interrupts can be enabled.
                    State.InterruptSavePc = 0xFFFF;
                    State.InterruptInstrFetch = 0xFFFF;
                    State.InterruptState = Dsp56300State.InterruptNone;
                    break;
            }
        }

        if (State.InterruptCounter == 0)
        {
            return;
        }

        uint iplSr = (R[Dsp56300State.RegSr] >> Dsp56300State.SrI0) & 3u;
        uint index = 0xFFFF;
        int iplToRaise = -1;

        for (int i = 0; i < 4; i++)
        {
            if (State.InterruptIsPending[i] != 1)
            {
                continue;
            }

            if (State.InterruptIpl[i] == 3)
            {
                index = (uint)i;
                break;
            }

            if (State.InterruptIpl[i] < iplSr)
            {
                continue;
            }

            if (State.InterruptIpl[i] <= iplToRaise)
            {
                continue;
            }

            index = (uint)i;
            iplToRaise = State.InterruptIpl[i];
        }

        if (index == 0xFFFF)
        {
            return;
        }

        State.InterruptIsPending[index] = 0;
        State.InterruptCounter--;

        iplToRaise = State.InterruptIpl[index] + 1;
        if (iplToRaise > 3)
        {
            iplToRaise = 3;
        }

        State.InterruptInstrFetch = InterruptVector[index];
        State.InterruptPipelineCount = 5;
        State.InterruptState = Dsp56300State.InterruptDisabled;
        State.InterruptIplToRaise = (ushort)iplToRaise;
    }

    private static bool IsLongInterrupt(uint instr)
    {
        return ((instr & 0xFFF000) == 0x0D0000) || ((instr & 0xFFC0FF) == 0x0BC080);
    }

    /// <summary>Reads a word from program memory (xemu <c>read_memory_p</c>).</summary>
    /// <param name="address">The word address.</param>
    /// <returns>The word.</returns>
    internal uint ReadMemoryP(uint address)
    {
        if (address >= (uint)Dsp56300State.PramSize)
        {
            return 0;
        }

        return State.Pram[address];
    }

    /// <summary>Writes a word to program memory and invalidates the decode cache.</summary>
    /// <param name="address">The word address.</param>
    /// <param name="value">The word.</param>
    internal void WriteMemoryP(uint address, uint value)
    {
        if (address >= (uint)Dsp56300State.PramSize)
        {
            return;
        }

        State.Pram[address] = value;
        _pramOpcode[address] = -1;
    }

    /// <summary>Clears the decoded-opcode caches.</summary>
    public void InvalidateOpcodeCache()
    {
        Array.Fill(_pramOpcode, (short)-1);
        Array.Clear(_opcodeLookup);

        // Zero is a valid instruction word (nop), so the tag cache needs a
        // sentinel that no 24-bit instruction can equal; clearing it would make
        // the first fetch of 0x000000 look like a cache hit on a null entry.
        Array.Fill(_opcodeLookupInstruction, 0xFFFFFFFFu);
    }

    private void WriteReg(uint numreg, uint value)
    {
        switch (numreg)
        {
            case Dsp56300State.RegA:
                R[Dsp56300State.RegA0] = 0;
                R[Dsp56300State.RegA1] = value;
                R[Dsp56300State.RegA2] = Flag(value, 23) ? 0xFFu : 0u;
                break;
            case Dsp56300State.RegB:
                R[Dsp56300State.RegB0] = 0;
                R[Dsp56300State.RegB1] = value;
                R[Dsp56300State.RegB2] = Flag(value, 23) ? 0xFFu : 0u;
                break;
            case Dsp56300State.RegOmr:
                R[Dsp56300State.RegOmr] = value & 0xC7;
                break;
            case Dsp56300State.RegSr:
                R[Dsp56300State.RegSr] = value & 0xAF7F;
                break;
            case Dsp56300State.RegSp:
                {
                    uint stackError = R[Dsp56300State.RegSp] & (3u << SpSe);
                    if (stackError == 0 && (value & (3u << SpSe)) != 0)
                    {
                        AddInterrupt(Dsp56300State.InterStackError);
                        R[Dsp56300State.RegSp] = value & (3u << SpSe);
                        if (State.ExceptionDebugging)
                        {
                            throw new DspStackException("stack overflow or underflow");
                        }
                    }
                    else
                    {
                        R[Dsp56300State.RegSp] = value & Mask6;
                    }

                    ComputeSshSsl();
                    break;
                }

            case Dsp56300State.RegSsh:
                StackPush(value, 0, sshOnly: true);
                break;
            case Dsp56300State.RegSsl:
                {
                    uint slot = R[Dsp56300State.RegSp] & Mask4;
                    if (slot == 0)
                    {
                        value = 0;
                    }

                    State.Stack[1][slot] = value & Mask16;
                    R[Dsp56300State.RegSsl] = value & Mask16;
                    break;
                }

            default:
                R[numreg] = value;
                R[numreg] &= Mask((int)RegisterMask[numreg]);
                break;
        }
    }

    private void StackPush(uint curpc, uint cursr, bool sshOnly)
    {
        uint stackError = R[Dsp56300State.RegSp] & (1u << SpSe);
        uint underflow = R[Dsp56300State.RegSp] & (1u << SpUf);
        uint stack = (R[Dsp56300State.RegSp] & Mask4) + 1;

        if (stackError == 0 && (stack & (1u << SpSe)) != 0)
        {
            AddInterrupt(Dsp56300State.InterStackError);
            if (State.ExceptionDebugging)
            {
                throw new DspStackException("stack overflow");
            }
        }

        R[Dsp56300State.RegSp] = (underflow | stackError | stack) & Mask6;
        stack &= Mask4;

        if (stack != 0)
        {
            State.Stack[0][stack] = curpc & Mask16;
            if (!sshOnly)
            {
                State.Stack[1][stack] = cursr & Mask16;
            }
        }
        else
        {
            State.Stack[0][0] = 0;
            State.Stack[1][0] = 0;
        }

        R[Dsp56300State.RegSsh] = State.Stack[0][stack];
        R[Dsp56300State.RegSsl] = State.Stack[1][stack];
    }

    private void StackPop(out uint newpc, out uint newsr)
    {
        uint stackError = R[Dsp56300State.RegSp] & (1u << SpSe);
        uint underflow = R[Dsp56300State.RegSp] & (1u << SpUf);
        uint stack = (R[Dsp56300State.RegSp] & Mask4) - 1;

        if (stackError == 0 && (stack & (1u << SpSe)) != 0)
        {
            AddInterrupt(Dsp56300State.InterStackError);
            if (State.ExceptionDebugging)
            {
                throw new DspStackException("stack underflow");
            }
        }

        R[Dsp56300State.RegSp] = (underflow | stackError | stack) & Mask6;
        stack &= Mask4;
        newpc = R[Dsp56300State.RegSsh];
        newsr = R[Dsp56300State.RegSsl];

        R[Dsp56300State.RegSsh] = State.Stack[0][stack];
        R[Dsp56300State.RegSsl] = State.Stack[1][stack];
    }

    /// <summary>Recomputes SSH/SSL from the stack pointer (xemu <c>dsp_compute_ssh_ssl</c>).</summary>
    public void ComputeSshSsl()
    {
        uint stack = R[Dsp56300State.RegSp] & Mask4;
        R[Dsp56300State.RegSsh] = State.Stack[0][stack];
        R[Dsp56300State.RegSsl] = State.Stack[1][stack];
    }

    private int CalcEffectiveAddress(uint eaMode, out uint address)
    {
        uint value = (eaMode >> 3) & 7u;
        uint numreg = eaMode & 7u;
        address = 0;

        switch (value)
        {
            case 0:
                address = R[Dsp56300State.RegR0 + numreg];
                UpdateRn(numreg, (short)(0u - R[Dsp56300State.RegN0 + numreg]));
                break;
            case 1:
                address = R[Dsp56300State.RegR0 + numreg];
                UpdateRn(numreg, (short)R[Dsp56300State.RegN0 + numreg]);
                break;
            case 2:
                address = R[Dsp56300State.RegR0 + numreg];
                UpdateRn(numreg, -1);
                break;
            case 3:
                address = R[Dsp56300State.RegR0 + numreg];
                UpdateRn(numreg, +1);
                break;
            case 4:
                address = R[Dsp56300State.RegR0 + numreg];
                break;
            case 5:
                {
                    State.InstrCycle += 2;
                    uint curreg = R[Dsp56300State.RegR0 + numreg];
                    UpdateRn(numreg, (short)R[Dsp56300State.RegN0 + numreg]);
                    address = R[Dsp56300State.RegR0 + numreg];
                    R[Dsp56300State.RegR0 + numreg] = curreg;
                    break;
                }

            case 6:
                State.InstrCycle += 2;
                address = ReadMemoryP(State.Pc + 1);
                State.CurrentInstructionLength++;
                if (numreg != 0)
                {
                    return 1;
                }

                break;
            case 7:
                State.InstrCycle += 2;
                UpdateRn(numreg, -1);
                address = R[Dsp56300State.RegR0 + numreg];
                break;
        }

        return 0;
    }

    private bool CalcConditionCode(uint ccCode)
    {
        uint sr = R[Dsp56300State.RegSr];
        switch (ccCode)
        {
            case 0:
                return (sr & (1u << Dsp56300State.SrC)) == 0;
            case 1:
                return (((sr >> Dsp56300State.SrN) ^ (sr >> Dsp56300State.SrV)) & 1) == 0;
            case 2:
                return (sr & (1u << Dsp56300State.SrZ)) == 0;
            case 3:
                return (sr & (1u << Dsp56300State.SrN)) == 0;
            case 4:
                {
                    uint value1 = (sr >> Dsp56300State.SrZ) & 1;
                    uint value2 = (~(sr >> Dsp56300State.SrU)) & 1;
                    uint value3 = (~(sr >> Dsp56300State.SrE)) & 1;
                    return (value1 | (value2 & value3)) == 0;
                }

            case 5:
                return (sr & (1u << Dsp56300State.SrE)) == 0;
            case 6:
                return (sr & (1u << Dsp56300State.SrL)) == 0;
            case 7:
                {
                    uint value1 = (sr >> Dsp56300State.SrN) & 1;
                    uint value2 = (sr >> Dsp56300State.SrV) & 1;
                    uint value3 = (sr >> Dsp56300State.SrZ) & 1;
                    return (value3 | (value1 ^ value2)) == 0;
                }

            case 8:
                return (sr & (1u << Dsp56300State.SrC)) != 0;
            case 9:
                return (((sr >> Dsp56300State.SrN) ^ (sr >> Dsp56300State.SrV)) & 1) != 0;
            case 10:
                return ((sr >> Dsp56300State.SrZ) & 1) != 0;
            case 11:
                return ((sr >> Dsp56300State.SrN) & 1) != 0;
            case 12:
                {
                    uint value1 = (sr >> Dsp56300State.SrZ) & 1;
                    uint value2 = (~(sr >> Dsp56300State.SrU)) & 1;
                    uint value3 = (~(sr >> Dsp56300State.SrE)) & 1;
                    return (value1 | (value2 & value3)) != 0;
                }

            case 13:
                return ((sr >> Dsp56300State.SrE) & 1) != 0;
            case 14:
                return ((sr >> Dsp56300State.SrL) & 1) != 0;
            case 15:
                {
                    uint value1 = (sr >> Dsp56300State.SrN) & 1;
                    uint value2 = (sr >> Dsp56300State.SrV) & 1;
                    uint value3 = (sr >> Dsp56300State.SrZ) & 1;
                    return (value3 | (value1 ^ value2)) != 0;
                }

            default:
                return false;
        }
    }

    private void UpdateEunz(uint reg0, uint reg1, uint reg2)
    {
        ClearSr((1u << Dsp56300State.SrE) | (1u << Dsp56300State.SrU)
            | (1u << Dsp56300State.SrN) | (1u << Dsp56300State.SrZ));

        uint scaling = (R[Dsp56300State.RegSr] >> Dsp56300State.SrS0) & 3u;
        switch (scaling)
        {
            case 0:
                {
                    uint valueE = (reg0 << 1) + (reg1 >> 23);
                    if (valueE != 0 && valueE != Mask(9))
                    {
                        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrE;
                    }

                    if ((reg1 & 0xC00000) == 0 || (reg1 & 0xC00000) == 0xC00000)
                    {
                        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrU;
                    }

                    break;
                }

            case 1:
                if (reg0 != 0 && reg0 != Mask(8))
                {
                    R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrE;
                }

                uint valueU = ((reg0 << 1) + (reg1 >> 23)) & 3;
                if (valueU == 0 || valueU == 3)
                {
                    R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrU;
                }

                break;
            case 2:
                {
                    uint valueE = (reg0 << 2) + (reg1 >> 22);
                    if (valueE != 0 && valueE != Mask(10))
                    {
                        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrE;
                    }

                    if ((reg1 & 0x600000) == 0 || (reg1 & 0x600000) == 0x600000)
                    {
                        R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrU;
                    }

                    break;
                }

            default:
                return;
        }

        if (reg0 == 0 && reg1 == 0 && reg2 == 0)
        {
            R[Dsp56300State.RegSr] |= 1u << Dsp56300State.SrZ;
        }

        R[Dsp56300State.RegSr] |= (reg0 >> 4) & 0x8;
    }

    private void UpdateRnBitReverse(uint numreg)
    {
        uint value = R[Dsp56300State.RegN0 + numreg];
        int revbits;
        for (revbits = 0; revbits < 16; revbits++)
        {
            if ((value & (1u << revbits)) != 0)
            {
                break;
            }
        }

        revbits++;

        uint rReg = R[Dsp56300State.RegR0 + numreg];
        value = rReg & (Mask(16) - Mask(revbits));
        for (int i = 0; i < revbits; i++)
        {
            if ((rReg & (1u << i)) != 0)
            {
                value |= 1u << (revbits - i - 1);
            }
        }

        value++;
        value &= Mask(revbits);

        rReg &= Mask(16) - Mask(revbits);
        rReg |= value;

        value = rReg & (Mask(16) - Mask(revbits));
        for (int i = 0; i < revbits; i++)
        {
            if ((rReg & (1u << i)) != 0)
            {
                value |= 1u << (revbits - i - 1);
            }
        }

        R[Dsp56300State.RegR0 + numreg] = value;
    }

    private void UpdateRnModulo(uint numreg, short modifier)
    {
        ushort modulo = (ushort)(R[Dsp56300State.RegM0 + numreg] + 1);
        ushort bufsize = 1;
        ushort bufmask = (ushort)Mask(16);
        while (bufsize < modulo)
        {
            bufsize <<= 1;
            bufmask <<= 1;
        }

        ushort lobound = (ushort)(R[Dsp56300State.RegR0 + numreg] & bufmask);
        ushort hibound = (ushort)(lobound + modulo - 1);
        short rReg = (short)R[Dsp56300State.RegR0 + numreg];
        short origModifier = modifier;

        if (origModifier > modulo)
        {
            while (modifier > bufsize)
            {
                rReg += (short)bufsize;
                modifier -= (short)bufsize;
            }

            while (modifier < -bufsize)
            {
                rReg -= (short)bufsize;
                modifier += (short)bufsize;
            }
        }

        rReg += modifier;

        if (origModifier != modulo)
        {
            if (rReg > hibound)
            {
                rReg -= (short)modulo;
            }
            else if (rReg < lobound)
            {
                rReg += (short)modulo;
            }
        }

        R[Dsp56300State.RegR0 + numreg] = (uint)(ushort)rReg & Mask16;
    }

    private void UpdateRn(uint numreg, short modifier)
    {
        ushort mReg = (ushort)R[Dsp56300State.RegM0 + numreg];
        if (mReg == 65535)
        {
            short value = (short)R[Dsp56300State.RegR0 + numreg];
            value += modifier;
            R[Dsp56300State.RegR0 + numreg] = (uint)(ushort)value & Mask16;
        }
        else if (mReg == 0)
        {
            UpdateRnBitReverse(numreg);
        }
        else if (mReg <= 32767)
        {
            UpdateRnModulo(numreg, modifier);
        }
    }
}
