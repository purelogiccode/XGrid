using System;

namespace XGrid.Cpu.Tcg;

/// <summary>The guest-memory view the IR interpreter uses (the real engine passes the MMU/TLB).</summary>
public interface ITcgMemory
{
    /// <summary>Reads one byte.</summary>
    /// <param name="address">Linear address.</param>
    /// <returns>The byte.</returns>
    byte Read8(uint address);

    /// <summary>Reads one 16-bit little-endian value.</summary>
    /// <param name="address">Linear address.</param>
    /// <returns>The value.</returns>
    ushort Read16(uint address);

    /// <summary>Reads one 32-bit little-endian value.</summary>
    /// <param name="address">Linear address.</param>
    /// <returns>The value.</returns>
    uint Read32(uint address);

    /// <summary>Writes one byte.</summary>
    /// <param name="address">Linear address.</param>
    /// <param name="value">The value.</param>
    void Write8(uint address, byte value);

    /// <summary>Writes one 16-bit little-endian value.</summary>
    /// <param name="address">Linear address.</param>
    /// <param name="value">The value.</param>
    void Write16(uint address, ushort value);

    /// <summary>Writes one 32-bit little-endian value.</summary>
    /// <param name="address">Linear address.</param>
    /// <param name="value">The value.</param>
    void Write32(uint address, uint value);
}

/// <summary>The result of executing a managed TCG program on the reference interpreter.</summary>
public sealed class TcgResult
{
    internal TcgResult(uint[] registers, uint eflags, uint eip, FpuState? fpu)
    {
        Registers = registers;
        Eflags = eflags;
        Eip = eip;
        Fpu = fpu;
    }

    /// <summary>Gets the eight general-purpose registers after execution.</summary>
    public uint[] Registers { get; }

    /// <summary>Gets EFLAGS after execution.</summary>
    public uint Eflags { get; }

    /// <summary>Gets EIP after execution.</summary>
    public uint Eip { get; }

    /// <summary>Gets the MMX/x87 register file when the program used one (null otherwise).</summary>
    public FpuState? Fpu { get; }
}

/// <summary>
/// Reference interpreter for the managed TCG IR (P1S3). It proves the translator,
/// the operand sizes and the flag semantics before host code generation (P1S4);
/// the real <see cref="XboxCpu"/> interpreter remains the exact fallback.
/// </summary>
public static class TcgInterpreter
{
    /// <summary>Executes a register/immediate-only program.</summary>
    /// <param name="program">The TCG program.</param>
    /// <param name="registers">The eight guest GPRs.</param>
    /// <param name="eflags">The initial EFLAGS.</param>
    /// <returns>The final register state.</returns>
    public static TcgResult Execute(TcgProgram program, ReadOnlySpan<uint> registers, uint eflags)
    {
        return Execute(program, registers, eflags, ReadOnlySpan<uint>.Empty, memory: null, fpu: null, context: null);
    }

    /// <summary>Executes a program with the guest memory and segment bases.</summary>
    /// <param name="program">The TCG program.</param>
    /// <param name="registers">The eight guest GPRs.</param>
    /// <param name="eflags">The initial EFLAGS.</param>
    /// <param name="segmentBases">The six segment bases in <see cref="SegmentRegister"/> order.</param>
    /// <param name="memory">The guest memory (null for register-only programs).</param>
    /// <param name="fpu">The x87/MMX register file (null unless the program uses MMX).</param>
    /// <param name="context">The machine context (required for SSE).</param>
    /// <returns>The final register state.</returns>
    public static TcgResult Execute(
        TcgProgram program,
        ReadOnlySpan<uint> registers,
        uint eflags,
        ReadOnlySpan<uint> segmentBases,
        ITcgMemory? memory,
        FpuState? fpu = null,
        XboxContext? context = null)
    {
        if (registers.Length < 8)
        {
            throw new ArgumentException("the TCG interpreter needs all eight GPRs", nameof(registers));
        }

        var gprs = new uint[8];
        registers[..8].CopyTo(gprs);
        var temps = new uint[program.TempCount];
        var wide = new ulong[program.TempCount];
        uint eip = program.StartEip;

        foreach (TcgOp op in program.Ops)
        {
            switch (op.Kind)
            {
                case TcgOpKind.MovImmediate:
                    temps[op.Dest] = op.Size switch
                    {
                        1 => op.Immediate & 0xFFu,
                        2 => op.Immediate & 0xFFFFu,
                        _ => op.Immediate,
                    };
                    break;
                case TcgOpKind.ReadRegister:
                    temps[op.Dest] = gprs[op.GuestRegister];
                    break;
                case TcgOpKind.ReadRegister8:
                    temps[op.Dest] = op.GuestRegister < 4
                        ? (byte)gprs[op.GuestRegister]
                        : (byte)(gprs[op.GuestRegister - 4] >> 8);
                    break;
                case TcgOpKind.ReadRegister16:
                    temps[op.Dest] = (ushort)gprs[op.GuestRegister];
                    break;
                case TcgOpKind.WriteRegister:
                    gprs[op.GuestRegister] = temps[op.Left];
                    break;
                case TcgOpKind.WriteRegister8:
                    WriteRegister8(gprs, op.GuestRegister, (byte)temps[op.Left]);
                    break;
                case TcgOpKind.WriteRegister16:
                    gprs[op.GuestRegister] = (gprs[op.GuestRegister] & 0xFFFF0000u) | (temps[op.Left] & 0xFFFFu);
                    break;
                case TcgOpKind.Add:
                    eflags = AluOps.Add(temps[op.Left], temps[op.Right], 0, op.Size * 8, eflags, out uint sum);
                    temps[op.Dest] = sum;
                    break;
                case TcgOpKind.Adc:
                    eflags = AluOps.Add(
                        temps[op.Left],
                        temps[op.Right],
                        (eflags & FlagBits.Cf) != 0 ? 1u : 0u,
                        op.Size * 8,
                        eflags,
                        out uint carried);
                    temps[op.Dest] = carried;
                    break;
                case TcgOpKind.Sbb:
                    eflags = AluOps.Sub(
                        temps[op.Left],
                        temps[op.Right],
                        (eflags & FlagBits.Cf) != 0 ? 1u : 0u,
                        op.Size * 8,
                        eflags,
                        out uint borrowed);
                    temps[op.Dest] = borrowed;
                    break;
                case TcgOpKind.Sub:
                    eflags = AluOps.Sub(temps[op.Left], temps[op.Right], 0, op.Size * 8, eflags, out uint difference);
                    temps[op.Dest] = difference;
                    break;
                case TcgOpKind.And:
                    temps[op.Dest] = Mask(temps[op.Left] & temps[op.Right], op.Size);
                    eflags = AluOps.Logical(temps[op.Dest], op.Size * 8, eflags);
                    break;
                case TcgOpKind.Or:
                    temps[op.Dest] = Mask(temps[op.Left] | temps[op.Right], op.Size);
                    eflags = AluOps.Logical(temps[op.Dest], op.Size * 8, eflags);
                    break;
                case TcgOpKind.Xor:
                    temps[op.Dest] = Mask(temps[op.Left] ^ temps[op.Right], op.Size);
                    eflags = AluOps.Logical(temps[op.Dest], op.Size * 8, eflags);
                    break;
                case TcgOpKind.Cmp:
                    eflags = AluOps.Sub(temps[op.Left], temps[op.Right], 0, op.Size * 8, eflags, out _);
                    break;
                case TcgOpKind.Inc:
                    eflags = AluOps.Inc(temps[op.Left], op.Size * 8, eflags, out uint incremented);
                    temps[op.Dest] = incremented;
                    break;
                case TcgOpKind.Dec:
                    eflags = AluOps.Dec(temps[op.Left], op.Size * 8, eflags, out uint decremented);
                    temps[op.Dest] = decremented;
                    break;
                case TcgOpKind.Shl:
                    eflags = ShiftOps.Shl(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint shl);
                    temps[op.Dest] = shl;
                    break;
                case TcgOpKind.Shr:
                    eflags = ShiftOps.Shr(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint shr);
                    temps[op.Dest] = shr;
                    break;
                case TcgOpKind.Sar:
                    eflags = ShiftOps.Sar(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint sar);
                    temps[op.Dest] = sar;
                    break;
                case TcgOpKind.Rol:
                    eflags = ShiftOps.Rol(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint rol);
                    temps[op.Dest] = rol;
                    break;
                case TcgOpKind.Ror:
                    eflags = ShiftOps.Ror(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint ror);
                    temps[op.Dest] = ror;
                    break;
                case TcgOpKind.Rcl:
                    eflags = ShiftOps.Rcl(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint rcl);
                    temps[op.Dest] = rcl;
                    break;
                case TcgOpKind.Rcr:
                    eflags = ShiftOps.Rcr(temps[op.Left], (int)temps[op.Right], op.Size * 8, eflags, out uint rcr);
                    temps[op.Dest] = rcr;
                    break;
                case TcgOpKind.Shld:
                    {
                        int count = op.On ? (int)(temps[op.GuestRegister2] & 0xFF) : (int)op.Immediate;
                        eflags = ShiftOps.Shld(
                            temps[op.Left], temps[op.Right], count, op.Size * 8, eflags, out uint shld);
                        temps[op.Dest] = shld;
                        break;
                    }

                case TcgOpKind.Shrd:
                    {
                        int count = op.On ? (int)(temps[op.GuestRegister2] & 0xFF) : (int)op.Immediate;
                        eflags = ShiftOps.Shrd(
                            temps[op.Left], temps[op.Right], count, op.Size * 8, eflags, out uint shrd);
                        temps[op.Dest] = shrd;
                        break;
                    }
                case TcgOpKind.Neg:
                    eflags = AluOps.Neg(temps[op.Left], op.Size * 8, eflags, out uint negated);
                    temps[op.Dest] = negated;
                    break;
                case TcgOpKind.Not:
                    temps[op.Dest] = ~temps[op.Left] & AluOps.Mask(op.Size * 8);
                    break;
                case TcgOpKind.Movzx8:
                    temps[op.Dest] = temps[op.Left] & 0xFFu;
                    break;
                case TcgOpKind.Movzx16:
                    temps[op.Dest] = temps[op.Left] & 0xFFFFu;
                    break;
                case TcgOpKind.Movsx8:
                    temps[op.Dest] = (uint)(int)(sbyte)temps[op.Left];
                    break;
                case TcgOpKind.Movsx16:
                    temps[op.Dest] = (uint)(int)(short)temps[op.Left];
                    break;
                case TcgOpKind.Sahf:
                    eflags = (eflags & ~FlagBits.ConditionMask) | ((gprs[0] >> 8) & FlagBits.ConditionMask);
                    break;
                case TcgOpKind.Lahf:
                    WriteRegister8(gprs, 4, (byte)((eflags & FlagBits.ConditionMask) | FlagBits.Reserved1));
                    break;
                case TcgOpKind.Mul:
                    eflags = MultiplyUnsigned(gprs, temps[op.Left], op.Size, eflags);
                    break;
                case TcgOpKind.Imul1:
                    eflags = MultiplySigned(gprs, temps[op.Left], op.Size, eflags);
                    break;
                case TcgOpKind.Imul2:
                    {
                        long product = SignedValue(temps[op.Left], op.Size * 8)
                                       * SignedValue(temps[op.Right], op.Size * 8);
                        uint result = (uint)product & AluOps.Mask(op.Size * 8);
                        eflags = SetMultiplyFlags(eflags, product != SignedValue(result, op.Size * 8));
                        temps[op.Dest] = result;
                        break;
                    }

                case TcgOpKind.Imul3:
                    {
                        long product = SignedValue(temps[op.Left], op.Size * 8)
                                       * SignedValue(op.Immediate, op.Size * 8);
                        uint result = (uint)product & AluOps.Mask(op.Size * 8);
                        eflags = SetMultiplyFlags(eflags, product != SignedValue(result, op.Size * 8));
                        temps[op.Dest] = result;
                        break;
                    }

                case TcgOpKind.Div:
                    DivideUnsigned(gprs, temps[op.Left], op.Size);
                    break;
                case TcgOpKind.Idiv:
                    DivideSigned(gprs, temps[op.Left], op.Size);
                    break;
                case TcgOpKind.Push:
                    {
                        uint esp = (gprs[4] - (uint)op.Size) & (op.Address16 ? 0xFFFFu : 0xFFFFFFFFu);
                        gprs[4] = op.Address16 ? (gprs[4] & 0xFFFF0000u) | esp : esp;
                        Store(memory, SegmentBase(segmentBases, SegmentRegister.Ss) + esp, temps[op.Left], op.Size);
                        break;
                    }

                case TcgOpKind.Pop:
                    {
                        uint esp = op.Address16 ? gprs[4] & 0xFFFFu : gprs[4];
                        temps[op.Dest] = Load(
                            memory, SegmentBase(segmentBases, SegmentRegister.Ss) + esp, op.Size);
                        uint advanced = (esp + (uint)op.Size) & (op.Address16 ? 0xFFFFu : 0xFFFFFFFFu);
                        gprs[4] = op.Address16 ? (gprs[4] & 0xFFFF0000u) | advanced : advanced;
                        break;
                    }

                case TcgOpKind.BitTest:
                    {
                        int bitWidth = op.Size * 8;
                        uint index = temps[op.Right];
                        if (op.IsMemory)
                        {
                            uint address = temps[op.Left] + (index / (uint)bitWidth) * (uint)op.Size;
                            uint value = Load(memory, address, op.Size);
                            uint mask = 1u << (int)(index % (uint)bitWidth);
                            eflags = (value & mask) != 0 ? eflags | FlagBits.Cf : eflags & ~FlagBits.Cf;
                            value = op.Immediate switch
                            {
                                1 => value | mask,
                                2 => value & ~mask,
                                3 => value ^ mask,
                                _ => value,
                            };
                            if (op.Immediate != 0)
                            {
                                Store(memory, address, value, op.Size);
                            }
                        }
                        else
                        {
                            uint value = temps[op.Left];
                            uint mask = 1u << (int)(index & (uint)(bitWidth - 1));
                            eflags = (value & mask) != 0 ? eflags | FlagBits.Cf : eflags & ~FlagBits.Cf;
                            temps[op.Dest] = op.Immediate switch
                            {
                                1 => value | mask,
                                2 => value & ~mask,
                                3 => value ^ mask,
                                _ => value,
                            };
                        }

                        break;
                    }

                case TcgOpKind.Cmpxchg:
                    {
                        uint destination = op.IsMemory
                            ? Load(memory, temps[op.Left], op.Size)
                            : ReadRegisterSized(gprs, op.GuestRegister, op.Size);
                        uint accumulator = ReadAccumulator(gprs, op.Size);
                        eflags = AluOps.Sub(accumulator, destination, 0, op.Size * 8, eflags, out _);
                        if (accumulator == destination)
                        {
                            uint source = temps[op.Right];
                            if (op.IsMemory)
                            {
                                Store(memory, temps[op.Left], source, op.Size);
                            }
                            else
                            {
                                WriteRegister(gprs, op.GuestRegister, source, op.Size);
                            }
                        }
                        else
                        {
                            WriteAccumulator(gprs, op.Size, destination);
                        }

                        break;
                    }

                case TcgOpKind.Cmpxchg8B:
                    {
                        uint address = temps[op.Left];
                        uint low = Load(memory, address, 4);
                        uint high = Load(memory, address + 4, 4);
                        if (low == gprs[0] && high == gprs[2])
                        {
                            Store(memory, address, gprs[3], 4);
                            Store(memory, address + 4, gprs[1], 4);
                            eflags |= FlagBits.Zf;
                        }
                        else
                        {
                            gprs[0] = low;
                            gprs[2] = high;
                            eflags &= ~FlagBits.Zf;
                        }

                        break;
                    }

                case TcgOpKind.Xadd:
                    {
                        uint destination = op.IsMemory ? Load(memory, temps[op.Left], op.Size) : temps[op.Left];
                        uint source = temps[op.Right];
                        eflags = AluOps.Add(destination, source, 0, op.Size * 8, eflags, out uint result);
                        if (op.IsMemory)
                        {
                            Store(memory, temps[op.Left], result, op.Size);
                        }
                        else
                        {
                            temps[op.Left] = result;
                        }

                        WriteRegister(gprs, op.GuestRegister, destination, op.Size);
                        break;
                    }

                case TcgOpKind.StringOp:
                    eflags = ExecuteString(op, gprs, eflags, segmentBases, memory);
                    break;
                case TcgOpKind.Fpu:
                    eflags = TcgFpu.Execute(
                        op.Instruction,
                        fpu ?? throw new InvalidOperationException("an x87 operation needs an FpuState"),
                        gprs,
                        memory,
                        segmentBases,
                        eflags);
                    break;
                case TcgOpKind.ReadFpuStatus:
                    temps[op.Dest] = (fpu ?? throw new InvalidOperationException("an x87 operation needs an FpuState")).StatusWord;
                    break;
                case TcgOpKind.Sse:
                    eflags = TcgSse.Execute(
                        op.Instruction,
                        context ?? throw new InvalidOperationException("an SSE operation needs an XboxContext"),
                        gprs,
                        memory,
                        segmentBases,
                        eflags);
                    break;
                case TcgOpKind.Mmx:
                    ExecuteMmx(op, temps, wide, fpu ?? throw new InvalidOperationException("an MMX operation needs an FpuState"), gprs, segmentBases, memory);
                    break;
                case TcgOpKind.Lea:
                    temps[op.Dest] = EffectiveAddress(op, gprs, segmentBases);
                    break;
                case TcgOpKind.Load:
                    temps[op.Dest] = Load(memory, temps[op.Left], op.Size);
                    break;
                case TcgOpKind.Store:
                    Store(memory, temps[op.Left], temps[op.Right], op.Size);
                    break;
                case TcgOpKind.SetCondition:
                    temps[op.Dest] = TcgConditions.Met(op.Condition, eflags) ? 1u : 0u;
                    break;
                case TcgOpKind.ConditionalMove:
                    if (TcgConditions.Met(op.Condition, eflags))
                    {
                        temps[op.Dest] = Mask(temps[op.Right], op.Size);
                    }

                    break;
                case TcgOpKind.Bswap:
                    {
                        uint value = temps[op.Left];
                        temps[op.Dest] = (value >> 24)
                            | ((value >> 8) & 0x0000FF00u)
                            | ((value << 8) & 0x00FF0000u)
                            | (value << 24);
                        break;
                    }

                case TcgOpKind.BitScan:
                    {
                        uint source = op.Size == 2 ? temps[op.Left] & 0xFFFFu : temps[op.Left];
                        if (source == 0)
                        {
                            eflags |= FlagBits.Zf;
                        }
                        else
                        {
                            eflags &= ~FlagBits.Zf;
                            temps[op.Dest] = (uint)(op.On
                                ? System.Numerics.BitOperations.Log2(source)
                                : System.Numerics.BitOperations.TrailingZeroCount(source));
                        }

                        break;
                    }

                case TcgOpKind.Arpl:
                    {
                        ushort destination = op.IsMemory
                            ? (ushort)Load(memory, temps[op.Left], 2)
                            : (ushort)temps[op.Dest];
                        ushort source = (ushort)temps[op.Right];
                        if ((destination & 3) < (source & 3))
                        {
                            eflags |= FlagBits.Zf;
                            ushort adjusted = (ushort)((destination & 0xFFFC) | (source & 3));
                            if (op.IsMemory)
                            {
                                Store(memory, temps[op.Left], adjusted, 2);
                            }
                            else
                            {
                                temps[op.Dest] = adjusted;
                            }
                        }
                        else
                        {
                            eflags &= ~FlagBits.Zf;
                        }

                        break;
                    }

                case TcgOpKind.SignExtend:
                    switch (op.Immediate)
                    {
                        case 0:
                            gprs[0] = (gprs[0] & 0xFFFF0000u) | (uint)(0xFFFF & (int)(sbyte)(byte)gprs[0]);
                            break;
                        case 1:
                            gprs[0] = (uint)(int)(short)(ushort)gprs[0];
                            break;
                        case 2:
                            {
                                bool negative = ((short)(ushort)gprs[0]) < 0;
                                gprs[2] = (gprs[2] & 0xFFFF0000u) | (negative ? 0xFFFFu : 0u);
                                break;
                            }

                        default:
                            gprs[2] = (int)gprs[0] < 0 ? 0xFFFFFFFFu : 0u;
                            break;
                    }

                    break;
                case TcgOpKind.Xlat:
                    {
                        uint baseIndex = op.Address16 ? gprs[3] & 0xFFFFu : gprs[3];
                        uint address = SegmentBase(segmentBases, (SegmentRegister)op.Segment)
                            + baseIndex
                            + (gprs[0] & 0xFFu);
                        gprs[0] = (gprs[0] & 0xFFFFFF00u) | Load(memory, address, 1);
                        break;
                    }

                case TcgOpKind.FlagOp:
                    eflags = op.Size == 2
                        ? eflags ^ op.Immediate
                        : op.On ? eflags | op.Immediate : eflags & ~op.Immediate;
                    break;
                case TcgOpKind.ReadFlags:
                    temps[op.Dest] = eflags | FlagBits.Reserved1;
                    break;
                case TcgOpKind.WriteFlags:
                    eflags = temps[op.Left] | FlagBits.Reserved1;
                    break;
                case TcgOpKind.Loop:
                    {
                        bool address16 = op.Address16;
                        int kind = op.GuestRegister;
                        bool take;
                        if (kind == 3)
                        {
                            take = ReadIndex(gprs, Gpr.Ecx, address16) == 0;
                        }
                        else
                        {
                            uint counter = (ReadIndex(gprs, Gpr.Ecx, address16) - 1u) & (address16 ? 0xFFFFu : 0xFFFFFFFFu);
                            WriteCounter(gprs, counter, address16);
                            bool zero = (eflags & FlagBits.Zf) != 0;
                            take = kind switch
                            {
                                0 => counter != 0,
                                1 => counter != 0 && zero,
                                _ => counter != 0 && !zero,
                            };
                        }

                        if (take)
                        {
                            eip = op.Immediate;
                        }

                        break;
                    }

                case TcgOpKind.SetEip:
                    eip = op.Immediate;
                    break;
                case TcgOpKind.ConditionalSetEip:
                    if (TcgConditions.Met(op.Condition, eflags))
                    {
                        return new TcgResult(gprs, eflags, op.Immediate, fpu);
                    }

                    break;
                case TcgOpKind.Exit:
                    return new TcgResult(gprs, eflags, eip, fpu);
                default:
                    throw new InvalidOperationException($"unknown TCG op {op.Kind}");
            }
        }

        return new TcgResult(gprs, eflags, eip, fpu);
    }

    private static void WriteRegister8(uint[] gprs, int registerIndex, byte value)
    {
        if (registerIndex < 4)
        {
            gprs[registerIndex] = (gprs[registerIndex] & 0xFFFFFF00u) | value;
        }
        else
        {
            int register = registerIndex - 4;
            gprs[register] = (gprs[register] & 0xFFFF00FFu) | ((uint)value << 8);
        }
    }

    private static void WriteRegister(uint[] gprs, int registerIndex, uint value, int size)
    {
        switch (size)
        {
            case 1:
                WriteRegister8(gprs, registerIndex, (byte)value);
                break;
            case 2:
                gprs[registerIndex] = (gprs[registerIndex] & 0xFFFF0000u) | (value & 0xFFFFu);
                break;
            default:
                gprs[registerIndex] = value;
                break;
        }
    }

    private static uint ReadAccumulator(uint[] gprs, int size)
    {
        return size switch
        {
            1 => gprs[0] & 0xFFu,
            2 => gprs[0] & 0xFFFFu,
            _ => gprs[0],
        };
    }

    private static uint ReadRegisterSized(uint[] gprs, int registerIndex, int size)
    {
        return size switch
        {
            1 => registerIndex < 4
                ? (byte)gprs[registerIndex]
                : (byte)(gprs[registerIndex - 4] >> 8),
            2 => gprs[registerIndex] & 0xFFFFu,
            _ => gprs[registerIndex],
        };
    }

    private static void WriteAccumulator(uint[] gprs, int size, uint value)
    {
        WriteRegister(gprs, 0, value, size);
    }

    private static uint ExecuteString(
        TcgOp op,
        uint[] gprs,
        uint eflags,
        ReadOnlySpan<uint> segmentBases,
        ITcgMemory? memory)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("a string operation needs an ITcgMemory");
        }

        bool address16 = op.Address16;
        bool decrement = (eflags & FlagBits.Df) != 0;
        int size = op.Size;
        uint ds = SegmentBase(segmentBases, SegmentRegister.Ds);
        uint es = SegmentBase(segmentBases, SegmentRegister.Es);
        int operation = (int)op.Immediate;
        int rep = op.GuestRegister;
        uint count = address16 ? gprs[1] & 0xFFFFu : gprs[1];

        uint Iteration(uint flags)
        {
            uint source = ds + ReadIndex(gprs, Gpr.Esi, address16);
            uint destination = es + ReadIndex(gprs, Gpr.Edi, address16);
            switch (operation)
            {
                case 0:
                    Store(memory, destination, Load(memory, source, size), size);
                    AdvanceIndex(gprs, Gpr.Esi, decrement, size, address16);
                    AdvanceIndex(gprs, Gpr.Edi, decrement, size, address16);
                    break;
                case 1:
                    flags = AluOps.Sub(
                        Load(memory, source, size), Load(memory, destination, size), 0, size * 8, flags, out _);
                    AdvanceIndex(gprs, Gpr.Esi, decrement, size, address16);
                    AdvanceIndex(gprs, Gpr.Edi, decrement, size, address16);
                    break;
                case 2:
                    Store(memory, destination, ReadAccumulator(gprs, size), size);
                    AdvanceIndex(gprs, Gpr.Edi, decrement, size, address16);
                    break;
                case 3:
                    WriteAccumulator(gprs, size, Load(memory, source, size));
                    AdvanceIndex(gprs, Gpr.Esi, decrement, size, address16);
                    break;
                default:
                    flags = AluOps.Sub(
                        ReadAccumulator(gprs, size), Load(memory, destination, size), 0, size * 8, flags, out _);
                    AdvanceIndex(gprs, Gpr.Edi, decrement, size, address16);
                    break;
            }

            return flags;
        }

        if (rep == 0)
        {
            return Iteration(eflags);
        }

        while (count != 0)
        {
            eflags = Iteration(eflags);
            count--;
            WriteCounter(gprs, count, address16);
            if (operation is 1 or 4)
            {
                bool zero = (eflags & FlagBits.Zf) != 0;
                if (rep == 1 && !zero)
                {
                    break;
                }

                if (rep == 2 && zero)
                {
                    break;
                }
            }
        }

        return eflags;
    }

    private static uint ReadIndex(uint[] gprs, Gpr register, bool address16)
    {
        return address16 ? gprs[(int)register] & 0xFFFFu : gprs[(int)register];
    }

    private static void AdvanceIndex(uint[] gprs, Gpr register, bool decrement, int delta, bool address16)
    {
        int index = (int)register;
        uint value = address16 ? gprs[index] & 0xFFFFu : gprs[index];
        value = decrement ? value - (uint)delta : value + (uint)delta;
        gprs[index] = address16 ? (gprs[index] & 0xFFFF0000u) | (value & 0xFFFFu) : value;
    }

    private static void WriteCounter(uint[] gprs, uint value, bool address16)
    {
        gprs[1] = address16 ? (gprs[1] & 0xFFFF0000u) | (value & 0xFFFFu) : value;
    }

    private static void ExecuteMmx(
        TcgOp op,
        uint[] temps,
        ulong[] wide,
        FpuState fpu,
        uint[] gprs,
        ReadOnlySpan<uint> segmentBases,
        ITcgMemory? memory)
    {
        switch (op.MmxOp)
        {
            case TcgMmxOp.ToTemp:
                {
                    ulong value = fpu.GetMmxRegister(op.GuestRegister);
                    if (op.Size == 4)
                    {
                        value &= 0xFFFFFFFF;
                    }

                    wide[op.Dest] = value;
                    temps[op.Dest] = (uint)value;
                    break;
                }

            case TcgMmxOp.FromTemp:
                fpu.SetMmxRegister(op.GuestRegister, op.Size == 4 ? temps[op.Left] : wide[op.Left]);
                break;
            case TcgMmxOp.Load:
                {
                    if (memory is null)
                    {
                        throw new InvalidOperationException("a memory access needs an ITcgMemory");
                    }

                    uint address = temps[op.Left];
                    ulong value = op.Size == 4
                        ? memory.Read32(address)
                        : memory.Read32(address) | ((ulong)memory.Read32(address + 4) << 32);
                    wide[op.Dest] = value;
                    temps[op.Dest] = (uint)value;
                    break;
                }

            case TcgMmxOp.Store:
                {
                    if (memory is null)
                    {
                        throw new InvalidOperationException("a memory access needs an ITcgMemory");
                    }

                    uint address = temps[op.Left];
                    ulong value = wide[op.Right];
                    if (op.Size == 4)
                    {
                        memory.Write32(address, (uint)value);
                    }
                    else
                    {
                        memory.Write32(address, (uint)value);
                        memory.Write32(address + 4, (uint)(value >> 32));
                    }

                    break;
                }

            case TcgMmxOp.Binary:
                fpu.SetMmxRegister(op.GuestRegister, ComputeMmx(op.Mnemonic, fpu.GetMmxRegister(op.GuestRegister), wide[op.Left]));
                break;
            case TcgMmxOp.Shift:
                fpu.SetMmxRegister(op.GuestRegister, ShiftMmx(op.Mnemonic, fpu.GetMmxRegister(op.GuestRegister), (int)(temps[op.Left] & 0xFF)));
                break;
            case TcgMmxOp.Maskmovq:
                {
                    if (memory is null)
                    {
                        throw new InvalidOperationException("a memory access needs an ITcgMemory");
                    }

                    ulong data = fpu.GetMmxRegister(op.GuestRegister);
                    ulong mask = fpu.GetMmxRegister(op.Left);
                    uint address = SegmentBase(segmentBases, SegmentRegister.Es) + gprs[7];
                    for (int i = 0; i < 8; i++)
                    {
                        if (((mask >> (8 * i)) & 0x80) != 0)
                        {
                            memory.Write8(address + (uint)i, (byte)(data >> (8 * i)));
                        }
                    }

                    break;
                }

            case TcgMmxOp.Pinsrw:
                {
                    ulong destination = fpu.GetMmxRegister(op.GuestRegister);
                    int lane = (int)(op.Control & 3);
                    ulong mask = 0xFFFFUL << (16 * lane);
                    destination = (destination & ~mask) | (((ulong)temps[op.Left] & 0xFFFF) << (16 * lane));
                    fpu.SetMmxRegister(op.GuestRegister, destination);
                    break;
                }

            case TcgMmxOp.Pextrw:
                temps[op.Dest] = (uint)((fpu.GetMmxRegister(op.GuestRegister) >> (16 * (int)(op.Control & 3))) & 0xFFFF);
                break;
            case TcgMmxOp.Pmovmskb:
                {
                    ulong source = fpu.GetMmxRegister(op.GuestRegister);
                    uint result = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (((source >> (8 * i)) & 0x80) != 0)
                        {
                            result |= 1u << i;
                        }
                    }

                    temps[op.Dest] = result;
                    break;
                }

            case TcgMmxOp.Pshufw:
                {
                    ulong source = wide[op.Left];
                    ulong result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int lane = (int)((op.Control >> (2 * i)) & 3);
                        result |= (ulong)(ushort)(source >> (16 * lane)) << (16 * i);
                    }

                    fpu.SetMmxRegister(op.GuestRegister, result);
                    break;
                }

            case TcgMmxOp.Emms:
                fpu.EmptyAll();
                break;
            default:
                throw new InvalidOperationException($"unknown MMX op {op.MmxOp}");
        }
    }

    private static ulong ComputeMmx(Mnemonic mnemonic, ulong left, ulong right)
    {
        switch (mnemonic)
        {
            case Mnemonic.Paddb:
                return MapBytes(left, right, static (x, y) => (byte)(x + y));
            case Mnemonic.Paddw:
                return MapWords(left, right, static (x, y) => (ushort)(x + y));
            case Mnemonic.Paddd:
                return MapDwords(left, right, static (x, y) => x + y);
            case Mnemonic.Paddsb:
                return MapBytes(left, right, static (x, y) => SaturateSByte((sbyte)x + (sbyte)y));
            case Mnemonic.Paddsw:
                return MapWords(left, right, static (x, y) => SaturateInt16((short)x + (short)y));
            case Mnemonic.Paddusb:
                return MapBytes(left, right, static (x, y) => SaturateByte(x + y));
            case Mnemonic.Paddusw:
                return MapWords(left, right, static (x, y) => SaturateUInt16(x + y));
            case Mnemonic.Psubb:
                return MapBytes(left, right, static (x, y) => (byte)(x - y));
            case Mnemonic.Psubw:
                return MapWords(left, right, static (x, y) => (ushort)(x - y));
            case Mnemonic.Psubd:
                return MapDwords(left, right, static (x, y) => x - y);
            case Mnemonic.Psubsb:
                return MapBytes(left, right, static (x, y) => SaturateSByte((sbyte)x - (sbyte)y));
            case Mnemonic.Psubsw:
                return MapWords(left, right, static (x, y) => SaturateInt16((short)x - (short)y));
            case Mnemonic.Psubusb:
                return MapBytes(left, right, static (x, y) => SaturateByte(x - y));
            case Mnemonic.Psubusw:
                return MapWords(left, right, static (x, y) => SaturateUInt16(x - y));
            case Mnemonic.Pmullw:
                return MapWords(left, right, static (x, y) => (ushort)(x * y));
            case Mnemonic.Pmulhw:
                return MapWords(left, right, static (x, y) => (ushort)(((short)x * (short)y) >> 16));
            case Mnemonic.Pmulhuw:
                return MapWords(left, right, static (x, y) => (ushort)(((uint)x * y) >> 16));
            case Mnemonic.Pmaddwd:
                {
                    ulong result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        int p0 = (short)(left >> (16 * (2 * i))) * (short)(right >> (16 * (2 * i)));
                        int p1 = (short)(left >> (16 * ((2 * i) + 1))) * (short)(right >> (16 * ((2 * i) + 1)));
                        result |= (ulong)(uint)(p0 + p1) << (32 * i);
                    }

                    return result;
                }

            case Mnemonic.Pcmpeqb:
                return MapBytes(left, right, static (x, y) => x == y ? (byte)0xFF : (byte)0);
            case Mnemonic.Pcmpeqw:
                return MapWords(left, right, static (x, y) => x == y ? (ushort)0xFFFF : (ushort)0);
            case Mnemonic.Pcmpeqd:
                return MapDwords(left, right, static (x, y) => x == y ? 0xFFFFFFFFu : 0u);
            case Mnemonic.Pcmpgtb:
                return MapBytes(left, right, static (x, y) => (sbyte)x > (sbyte)y ? (byte)0xFF : (byte)0);
            case Mnemonic.Pcmpgtw:
                return MapWords(left, right, static (x, y) => (short)x > (short)y ? (ushort)0xFFFF : (ushort)0);
            case Mnemonic.Pcmpgtd:
                return MapDwords(left, right, static (x, y) => (int)x > (int)y ? 0xFFFFFFFFu : 0u);
            case Mnemonic.Pand:
                return left & right;
            case Mnemonic.Pandn:
                return ~left & right;
            case Mnemonic.Por:
                return left | right;
            case Mnemonic.Pxor:
                return left ^ right;
            case Mnemonic.Pavgb:
                return MapBytes(left, right, static (x, y) => (byte)((x + y + 1) >> 1));
            case Mnemonic.Pavgw:
                return MapWords(left, right, static (x, y) => (ushort)((x + y + 1) >> 1));
            case Mnemonic.Pminub:
                return MapBytes(left, right, static (x, y) => Math.Min(x, y));
            case Mnemonic.Pmaxub:
                return MapBytes(left, right, static (x, y) => Math.Max(x, y));
            case Mnemonic.Pminsw:
                return MapWords(left, right, static (x, y) => ((short)x < (short)y ? x : y));
            case Mnemonic.Pmaxsw:
                return MapWords(left, right, static (x, y) => ((short)x > (short)y ? x : y));
            case Mnemonic.Psadbw:
                {
                    int sum = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        sum += Math.Abs((int)((left >> (8 * i)) & 0xFF) - (int)((right >> (8 * i)) & 0xFF));
                    }

                    return (ushort)sum;
                }

            case Mnemonic.Packsswb:
                return PackWordsToBytes(left, right, static v => SaturateSByte(v));
            case Mnemonic.Packuswb:
                return PackWordsToBytes(left, right, static v => SaturateByte(v));
            case Mnemonic.Packssdw:
                return PackDwordsToWords(left, right);
            case Mnemonic.Punpcklbw:
                return InterleaveBytes(left, right, 0);
            case Mnemonic.Punpckhbw:
                return InterleaveBytes(left, right, 4);
            case Mnemonic.Punpcklwd:
                return InterleaveWords(left, right, 0);
            case Mnemonic.Punpckhwd:
                return InterleaveWords(left, right, 2);
            case Mnemonic.Punpckldq:
                return (left & 0xFFFFFFFFUL) | ((right & 0xFFFFFFFFUL) << 32);
            case Mnemonic.Punpckhdq:
                return ((left >> 32) & 0xFFFFFFFFUL) | ((right >> 32) << 32);
            default:
                throw new InvalidOperationException($"Unsupported MMX operation {mnemonic}.");
        }
    }

    private static ulong ShiftMmx(Mnemonic mnemonic, ulong value, int count)
    {
        switch (mnemonic)
        {
            case Mnemonic.Psllw:
                if (count > 15)
                {
                    return 0;
                }

                return MapWords(value, 0, (x, _) => (ushort)(x << count));

            case Mnemonic.Psrlw:
                if (count > 15)
                {
                    return 0;
                }

                return MapWords(value, 0, (x, _) => (ushort)(x >> count));

            case Mnemonic.Psraw:
                if (count > 15)
                {
                    return MapWords(value, 0, static (x, _) => (ushort)((short)x < 0 ? 0xFFFF : 0));
                }

                return MapWords(value, 0, (x, _) => (ushort)((short)x >> count));

            case Mnemonic.Pslld:
                if (count > 31)
                {
                    return 0;
                }

                return MapDwords(value, 0, (x, _) => x << count);

            case Mnemonic.Psrld:
                if (count > 31)
                {
                    return 0;
                }

                return MapDwords(value, 0, (x, _) => x >> count);

            case Mnemonic.Psrad:
                if (count > 31)
                {
                    return MapDwords(value, 0, static (x, _) => (int)x < 0 ? 0xFFFFFFFFu : 0u);
                }

                return MapDwords(value, 0, (x, _) => (uint)((int)x >> count));

            case Mnemonic.Psllq:
                return count > 63 ? 0 : value << count;

            default:
                return count > 63 ? 0 : value >> count;
        }
    }

    private static ulong MapBytes(ulong left, ulong right, Func<byte, byte, byte> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 8; i++)
        {
            result |= (ulong)operation((byte)(left >> (8 * i)), (byte)(right >> (8 * i))) << (8 * i);
        }

        return result;
    }

    private static ulong MapWords(ulong left, ulong right, Func<ushort, ushort, ushort> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)operation((ushort)(left >> (16 * i)), (ushort)(right >> (16 * i))) << (16 * i);
        }

        return result;
    }

    private static ulong MapDwords(ulong left, ulong right, Func<uint, uint, uint> operation)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)operation((uint)(left >> (32 * i)), (uint)(right >> (32 * i))) << (32 * i);
        }

        return result;
    }

    private static byte SaturateSByte(int value)
    {
        return value < sbyte.MinValue ? (byte)0x80 : value > sbyte.MaxValue ? (byte)0x7F : (byte)value;
    }

    private static byte SaturateByte(int value)
    {
        return value < 0 ? (byte)0 : value > 0xFF ? (byte)0xFF : (byte)value;
    }

    private static ushort SaturateInt16(int value)
    {
        return value < short.MinValue ? (ushort)0x8000 : value > short.MaxValue ? (ushort)0x7FFF : (ushort)value;
    }

    private static ushort SaturateUInt16(int value)
    {
        return value < 0 ? (ushort)0 : value > 0xFFFF ? (ushort)0xFFFF : (ushort)value;
    }

    private static ulong PackWordsToBytes(ulong left, ulong right, Func<short, byte> saturate)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)saturate((short)(left >> (16 * i))) << (8 * i);
            result |= (ulong)saturate((short)(right >> (16 * i))) << (8 * (i + 4));
        }

        return result;
    }

    private static ulong PackDwordsToWords(ulong left, ulong right)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)SaturateInt16((int)(uint)(left >> (32 * i))) << (16 * i);
            result |= (ulong)SaturateInt16((int)(uint)(right >> (32 * i))) << (16 * (i + 2));
        }

        return result;
    }

    private static ulong InterleaveBytes(ulong left, ulong right, int start)
    {
        ulong result = 0;
        for (int i = 0; i < 4; i++)
        {
            result |= (ulong)(byte)(left >> (8 * (start + i))) << (16 * i);
            result |= (ulong)(byte)(right >> (8 * (start + i))) << ((16 * i) + 8);
        }

        return result;
    }

    private static ulong InterleaveWords(ulong left, ulong right, int start)
    {
        ulong result = 0;
        for (int i = 0; i < 2; i++)
        {
            result |= (ulong)(ushort)(left >> (16 * (start + i))) << (32 * i);
            result |= (ulong)(ushort)(right >> (16 * (start + i))) << ((32 * i) + 16);
        }

        return result;
    }

    private static uint Mask(uint value, int size)
    {
        return size switch
        {
            1 => value & 0xFFu,
            2 => value & 0xFFFFu,
            _ => value,
        };
    }

    private static uint SegmentBase(ReadOnlySpan<uint> bases, SegmentRegister segment)
    {
        int index = (int)segment;
        return index >= 0 && index < bases.Length ? bases[index] : 0;
    }

    private static long SignedValue(uint value, int bits)
    {
        return bits switch
        {
            8 => (sbyte)value,
            16 => (short)value,
            _ => (int)value,
        };
    }

    private static uint SetMultiplyFlags(uint eflags, bool overflow)
    {
        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        if (overflow)
        {
            flags |= FlagBits.Cf | FlagBits.Of;
        }

        return flags;
    }

    private static uint MultiplyUnsigned(uint[] gprs, uint source, int size, uint eflags)
    {
        bool overflow;
        switch (size)
        {
            case 1:
                {
                    uint product = (byte)gprs[0] * (source & 0xFFu);
                    gprs[0] = (gprs[0] & 0xFFFF0000u) | (product & 0xFFFFu);
                    overflow = (product & 0xFF00u) != 0;
                    break;
                }

            case 2:
                {
                    uint product = (ushort)gprs[0] * (source & 0xFFFFu);
                    gprs[0] = (gprs[0] & 0xFFFF0000u) | (product & 0xFFFFu);
                    gprs[2] = (gprs[2] & 0xFFFF0000u) | ((product >> 16) & 0xFFFFu);
                    overflow = (product & 0xFFFF0000u) != 0;
                    break;
                }

            default:
                {
                    ulong product = (ulong)gprs[0] * source;
                    gprs[0] = (uint)product;
                    gprs[2] = (uint)(product >> 32);
                    overflow = (product >> 32) != 0;
                    break;
                }
        }

        return SetMultiplyFlags(eflags, overflow);
    }

    private static uint MultiplySigned(uint[] gprs, uint source, int size, uint eflags)
    {
        bool overflow;
        switch (size)
        {
            case 1:
                {
                    int product = (sbyte)gprs[0] * (sbyte)source;
                    gprs[0] = (gprs[0] & 0xFFFF0000u) | (uint)(product & 0xFFFF);
                    overflow = product != (sbyte)product;
                    break;
                }

            case 2:
                {
                    int product = (short)gprs[0] * (short)source;
                    gprs[0] = (gprs[0] & 0xFFFF0000u) | (uint)(product & 0xFFFF);
                    gprs[2] = (gprs[2] & 0xFFFF0000u) | ((uint)(product >> 16) & 0xFFFFu);
                    overflow = product != (short)product;
                    break;
                }

            default:
                {
                    long product = (long)(int)gprs[0] * (int)source;
                    gprs[0] = (uint)product;
                    gprs[2] = (uint)(product >> 32);
                    overflow = product != (int)product;
                    break;
                }
        }

        return SetMultiplyFlags(eflags, overflow);
    }

    private static void DivideUnsigned(uint[] gprs, uint source, int size)
    {
        switch (size)
        {
            case 1:
                {
                    uint divisor = source & 0xFFu;
                    if (divisor == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    uint dividend = (ushort)gprs[0];
                    uint quotient = dividend / divisor;
                    if (quotient > 0xFFu)
                    {
                        throw new TcgDivideErrorException();
                    }

                    WriteRegister8(gprs, 0, (byte)quotient);
                    WriteRegister8(gprs, 4, (byte)(dividend % divisor));
                    break;
                }

            case 2:
                {
                    uint divisor = source & 0xFFFFu;
                    if (divisor == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    uint dividend = ((uint)(ushort)gprs[2] << 16) | (ushort)gprs[0];
                    uint quotient = dividend / divisor;
                    if (quotient > 0xFFFFu)
                    {
                        throw new TcgDivideErrorException();
                    }

                    gprs[0] = (gprs[0] & 0xFFFF0000u) | (quotient & 0xFFFFu);
                    gprs[2] = (gprs[2] & 0xFFFF0000u) | ((dividend % divisor) & 0xFFFFu);
                    break;
                }

            default:
                {
                    if (source == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    ulong dividend = ((ulong)gprs[2] << 32) | gprs[0];
                    ulong quotient = dividend / source;
                    if (quotient > 0xFFFFFFFFu)
                    {
                        throw new TcgDivideErrorException();
                    }

                    gprs[0] = (uint)quotient;
                    gprs[2] = (uint)(dividend % source);
                    break;
                }
        }
    }

    private static void DivideSigned(uint[] gprs, uint source, int size)
    {
        switch (size)
        {
            case 1:
                {
                    int divisor = (sbyte)source;
                    if (divisor == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    int dividend = (short)gprs[0];
                    int quotient = dividend / divisor;
                    if (quotient is < sbyte.MinValue or > sbyte.MaxValue)
                    {
                        throw new TcgDivideErrorException();
                    }

                    WriteRegister8(gprs, 0, (byte)quotient);
                    WriteRegister8(gprs, 4, (byte)(dividend % divisor));
                    break;
                }

            case 2:
                {
                    int divisor = (short)source;
                    if (divisor == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    int dividend = (int)(((uint)(ushort)gprs[2] << 16) | (ushort)gprs[0]);
                    if (dividend == int.MinValue && divisor == -1)
                    {
                        throw new TcgDivideErrorException();
                    }

                    int quotient = dividend / divisor;
                    if (quotient is < short.MinValue or > short.MaxValue)
                    {
                        throw new TcgDivideErrorException();
                    }

                    gprs[0] = (gprs[0] & 0xFFFF0000u) | ((uint)quotient & 0xFFFFu);
                    gprs[2] = (gprs[2] & 0xFFFF0000u) | ((uint)(dividend % divisor) & 0xFFFFu);
                    break;
                }

            default:
                {
                    int divisor = (int)source;
                    if (divisor == 0)
                    {
                        throw new TcgDivideErrorException();
                    }

                    long dividend = (long)(((ulong)gprs[2] << 32) | gprs[0]);
                    if (dividend == long.MinValue && divisor == -1)
                    {
                        throw new TcgDivideErrorException();
                    }

                    long quotient = dividend / divisor;
                    if (quotient is < int.MinValue or > int.MaxValue)
                    {
                        throw new TcgDivideErrorException();
                    }

                    gprs[0] = (uint)quotient;
                    gprs[2] = (uint)(dividend % divisor);
                    break;
                }
        }
    }

    private static uint EffectiveAddress(TcgOp op, uint[] gprs, ReadOnlySpan<uint> segmentBases)
    {
        uint address = op.Immediate;
        if (op.HasBase)
        {
            address += op.Address16 ? gprs[op.GuestRegister] & 0xFFFFu : gprs[op.GuestRegister];
        }

        if (op.HasIndex)
        {
            uint index = op.Address16 ? gprs[op.GuestRegister2] & 0xFFFFu : gprs[op.GuestRegister2];
            address += index * (uint)op.Scale;
        }

        if (op.Address16)
        {
            address &= 0xFFFFu;
        }

        if (op.Segment >= 0 && op.Segment < segmentBases.Length)
        {
            address += segmentBases[op.Segment];
        }

        return address;
    }

    private static uint Load(ITcgMemory? memory, uint address, int size)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("a memory access needs an ITcgMemory");
        }

        return size switch
        {
            1 => memory.Read8(address),
            2 => memory.Read16(address),
            _ => memory.Read32(address),
        };
    }

    private static void Store(ITcgMemory? memory, uint address, uint value, int size)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("a memory access needs an ITcgMemory");
        }

        switch (size)
        {
            case 1:
                memory.Write8(address, (byte)value);
                break;
            case 2:
                memory.Write16(address, (ushort)value);
                break;
            default:
                memory.Write32(address, value);
                break;
        }
    }
}

/// <summary>The #DE divide-error condition raised by DIV/IDIV.</summary>
public sealed class TcgDivideErrorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TcgDivideErrorException"/> class.</summary>
    public TcgDivideErrorException()
        : this("divide error")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TcgDivideErrorException"/> class.</summary>
    /// <param name="message">The message.</param>
    public TcgDivideErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TcgDivideErrorException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TcgDivideErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The x86 condition-code evaluation (mirrors <c>XboxCpu.EvaluateCondition</c>).</summary>
internal static class TcgConditions
{
    /// <summary>Returns whether the condition holds for the given EFLAGS.</summary>
    /// <param name="condition">The condition code.</param>
    /// <param name="eflags">The flags.</param>
    /// <returns>True when the branch is taken.</returns>
    public static bool Met(ConditionCode condition, uint eflags)
    {
        bool cf = (eflags & FlagBits.Cf) != 0;
        bool zf = (eflags & FlagBits.Zf) != 0;
        bool sf = (eflags & FlagBits.Sf) != 0;
        bool of = (eflags & FlagBits.Of) != 0;
        bool pf = (eflags & FlagBits.Pf) != 0;

        return condition switch
        {
            ConditionCode.Overflow => of,
            ConditionCode.NotOverflow => !of,
            ConditionCode.Below => cf,
            ConditionCode.AboveOrEqual => !cf,
            ConditionCode.Equal => zf,
            ConditionCode.NotEqual => !zf,
            ConditionCode.BelowOrEqual => cf || zf,
            ConditionCode.Above => !cf && !zf,
            ConditionCode.Sign => sf,
            ConditionCode.NotSign => !sf,
            ConditionCode.Parity => pf,
            ConditionCode.NotParity => !pf,
            ConditionCode.Less => sf != of,
            ConditionCode.GreaterOrEqual => sf == of,
            ConditionCode.LessOrEqual => zf || sf != of,
            _ => !zf && sf == of,
        };
    }
}
