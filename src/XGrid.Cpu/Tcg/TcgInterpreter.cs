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
    internal TcgResult(uint[] registers, uint eflags, uint eip)
    {
        Registers = registers;
        Eflags = eflags;
        Eip = eip;
    }

    /// <summary>Gets the eight general-purpose registers after execution.</summary>
    public uint[] Registers { get; }

    /// <summary>Gets EFLAGS after execution.</summary>
    public uint Eflags { get; }

    /// <summary>Gets EIP after execution.</summary>
    public uint Eip { get; }
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
        return Execute(program, registers, eflags, ReadOnlySpan<uint>.Empty, memory: null);
    }

    /// <summary>Executes a program with the guest memory and segment bases.</summary>
    /// <param name="program">The TCG program.</param>
    /// <param name="registers">The eight guest GPRs.</param>
    /// <param name="eflags">The initial EFLAGS.</param>
    /// <param name="segmentBases">The six segment bases in <see cref="SegmentRegister"/> order.</param>
    /// <param name="memory">The guest memory (null for register-only programs).</param>
    /// <returns>The final register state.</returns>
    public static TcgResult Execute(
        TcgProgram program,
        ReadOnlySpan<uint> registers,
        uint eflags,
        ReadOnlySpan<uint> segmentBases,
        ITcgMemory? memory)
    {
        if (registers.Length < 8)
        {
            throw new ArgumentException("the TCG interpreter needs all eight GPRs", nameof(registers));
        }

        var gprs = new uint[8];
        registers[..8].CopyTo(gprs);
        var temps = new uint[program.TempCount];
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
                    eflags = ShiftOps.Shld(
                        temps[op.Left], temps[op.Right], (int)op.Immediate, op.Size * 8, eflags, out uint shld);
                    temps[op.Dest] = shld;
                    break;
                case TcgOpKind.Shrd:
                    eflags = ShiftOps.Shrd(
                        temps[op.Left], temps[op.Right], (int)op.Immediate, op.Size * 8, eflags, out uint shrd);
                    temps[op.Dest] = shrd;
                    break;
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
                        uint esp = gprs[4] - (uint)op.Size;
                        gprs[4] = esp;
                        Store(memory, SegmentBase(segmentBases, SegmentRegister.Ss) + esp, temps[op.Left], op.Size);
                        break;
                    }

                case TcgOpKind.Pop:
                    {
                        uint esp = gprs[4];
                        temps[op.Dest] = Load(
                            memory, SegmentBase(segmentBases, SegmentRegister.Ss) + esp, op.Size);
                        gprs[4] = esp + (uint)op.Size;
                        break;
                    }
                case TcgOpKind.Lea:
                    temps[op.Dest] = EffectiveAddress(op, gprs, segmentBases);
                    break;
                case TcgOpKind.Load:
                    temps[op.Dest] = Load(memory, temps[op.Left], op.Size);
                    break;
                case TcgOpKind.Store:
                    Store(memory, temps[op.Left], temps[op.Right], op.Size);
                    break;
                case TcgOpKind.SetEip:
                    eip = op.Immediate;
                    break;
                case TcgOpKind.ConditionalSetEip:
                    if (TcgConditions.Met(op.Condition, eflags))
                    {
                        return new TcgResult(gprs, eflags, op.Immediate);
                    }

                    break;
                case TcgOpKind.Exit:
                    return new TcgResult(gprs, eflags, eip);
                default:
                    throw new InvalidOperationException($"unknown TCG op {op.Kind}");
            }
        }

        return new TcgResult(gprs, eflags, eip);
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
            address += gprs[op.GuestRegister];
        }

        if (op.HasIndex)
        {
            address += gprs[op.GuestRegister2] * (uint)op.Scale;
        }

        if (op.Segment >= 0 && op.Segment < segmentBases.Length)
        {
            address += segmentBases[op.Segment];
        }

        return op.Address16 ? address & 0xFFFFu : address;
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
