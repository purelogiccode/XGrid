using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The SSE executor behind <see cref="TcgOpKind.Sse"/> (P1S3 slice 12). The
/// semantics are transcribed from the exact interpreter's <c>XboxCpu.ExecuteSse</c>
/// and its arithmetic/comparison/shuffle helpers, operating on the machine's
/// <see cref="XboxContext"/> XMM/MXCSR state.
/// </summary>
public static class TcgSse
{
    /// <summary>Gets whether the translator can hand this mnemonic to <see cref="Execute"/>.</summary>
    /// <param name="mnemonic">The SSE mnemonic.</param>
    /// <returns>False for FXSAVE/FXRSTOR (interpreter fallback).</returns>
    public static bool IsSupported(Mnemonic mnemonic)
    {
        return IsSseMnemonic(mnemonic) && mnemonic is not (Mnemonic.Fxsave or Mnemonic.Fxrstor);
    }

    /// <summary>Gets whether the mnemonic is in the interpreter's SSE dispatch range.</summary>
    /// <param name="mnemonic">The mnemonic.</param>
    /// <returns>True for Movaps..Movntps.</returns>
    public static bool IsSseMnemonic(Mnemonic mnemonic)
    {
        return mnemonic >= Mnemonic.Movaps && mnemonic <= Mnemonic.Movntps;
    }

    /// <summary>Executes one SSE instruction.</summary>
    /// <param name="instruction">The decoded instruction.</param>
    /// <param name="context">The machine context (XMM registers and MXCSR).</param>
    /// <param name="gprs">The eight guest GPRs (memory addressing).</param>
    /// <param name="memory">The guest memory.</param>
    /// <param name="segmentBases">The six segment bases in <see cref="SegmentRegister"/> order.</param>
    /// <param name="eflags">EFLAGS (COMISS/UCOMISS update it).</param>
    /// <returns>The updated EFLAGS.</returns>
    public static uint Execute(
        in DecodedInstruction instruction,
        XboxContext context,
        Span<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases,
        uint eflags)
    {
        ArgumentNullException.ThrowIfNull(context);
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Prefetch:
            case Mnemonic.Sfence:
            case Mnemonic.Lfence:
            case Mnemonic.Mfence:
                break;

            case Mnemonic.Movaps:
            case Mnemonic.Movups:
                {
                    (ulong low, ulong high) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
                    WriteXmm(instruction.Operand1, low, high, context, gprs, memory, segmentBases);
                    break;
                }

            case Mnemonic.Movss:
                {
                    if (instruction.Operand1.Kind == OperandKind.Register)
                    {
                        (ulong low, ulong high) = ReadXmm(
                            instruction.Operand2, context, gprs, memory, segmentBases);
                        context.WriteXmmElement(instruction.Operand1.RegisterIndex, 0, ElementBits(low, high, 0));
                    }
                    else
                    {
                        uint source = context.ReadXmmElement(instruction.Operand2.RegisterIndex, 0);
                        WriteMemory(TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 32, source, memory);
                    }

                    break;
                }

            case Mnemonic.Movlps:
                {
                    if (instruction.Operand1.Kind == OperandKind.Memory)
                    {
                        WriteMemory64(
                            TcgAddress.Of(instruction.Operand1, gprs, segmentBases),
                            ReadXmm64(instruction.Operand2.RegisterIndex, 0, context),
                            memory);
                    }
                    else if (instruction.Operand2.Kind == OperandKind.Memory)
                    {
                        ulong value = ReadMemory64(
                            TcgAddress.Of(instruction.Operand2, gprs, segmentBases), memory);
                        WriteXmm64(instruction.Operand1.RegisterIndex, 0, value, context);
                    }

                    break;
                }

            case Mnemonic.Movhps:
                {
                    if (instruction.Operand1.Kind == OperandKind.Memory)
                    {
                        WriteMemory64(
                            TcgAddress.Of(instruction.Operand1, gprs, segmentBases),
                            ReadXmm64(instruction.Operand2.RegisterIndex, 1, context),
                            memory);
                    }
                    else if (instruction.Operand2.Kind == OperandKind.Memory)
                    {
                        ulong value = ReadMemory64(
                            TcgAddress.Of(instruction.Operand2, gprs, segmentBases), memory);
                        WriteXmm64(instruction.Operand1.RegisterIndex, 1, value, context);
                    }

                    break;
                }

            case Mnemonic.Movhlps:
                WriteXmm64(
                    instruction.Operand1.RegisterIndex,
                    0,
                    ReadXmm64(instruction.Operand2.RegisterIndex, 1, context),
                    context);
                break;

            case Mnemonic.Movlhps:
                WriteXmm64(
                    instruction.Operand1.RegisterIndex,
                    1,
                    ReadXmm64(instruction.Operand2.RegisterIndex, 0, context),
                    context);
                break;

            case Mnemonic.Movmskps:
                {
                    uint result = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        if ((context.ReadXmmElement(instruction.Operand2.RegisterIndex, i) & 0x80000000u) != 0)
                        {
                            result |= 1u << i;
                        }
                    }

                    gprs[instruction.Operand1.RegisterIndex] = result;
                    break;
                }

            case Mnemonic.Addps:
            case Mnemonic.Addss:
            case Mnemonic.Subps:
            case Mnemonic.Subss:
            case Mnemonic.Mulps:
            case Mnemonic.Mulss:
            case Mnemonic.Divps:
            case Mnemonic.Divss:
            case Mnemonic.Minps:
            case Mnemonic.Minss:
            case Mnemonic.Maxps:
            case Mnemonic.Maxss:
                ExecuteArithmetic(instruction, context, gprs, memory, segmentBases);
                break;

            case Mnemonic.Sqrtps:
            case Mnemonic.Sqrtss:
            case Mnemonic.Rsqrtps:
            case Mnemonic.Rsqrtss:
            case Mnemonic.Rcpps:
            case Mnemonic.Rcpss:
                ExecuteUnary(instruction, context, gprs, memory, segmentBases);
                break;

            case Mnemonic.Andps:
            case Mnemonic.Andnps:
            case Mnemonic.Orps:
            case Mnemonic.Xorps:
                ExecuteLogical(instruction, context, gprs, memory, segmentBases);
                break;

            case Mnemonic.Cmpps:
            case Mnemonic.Cmpss:
                ExecuteCompare(instruction, context, gprs, memory, segmentBases);
                break;

            case Mnemonic.Comiss:
            case Mnemonic.Ucomiss:
                eflags = CompareScalar(instruction, context, gprs, memory, segmentBases, eflags);
                break;

            case Mnemonic.Shufps:
                ExecuteShufps(instruction, context, gprs, memory, segmentBases);
                break;

            case Mnemonic.Unpcklps:
            case Mnemonic.Unpckhps:
                {
                    (ulong dlow, ulong dhigh) = context.ReadXmm128(instruction.Operand1.RegisterIndex);
                    (ulong slow, ulong shigh) = ReadXmm(
                        instruction.Operand2, context, gprs, memory, segmentBases);
                    ulong rlow;
                    ulong rhigh;
                    if (instruction.Mnemonic == Mnemonic.Unpcklps)
                    {
                        rlow = (dlow & 0xFFFFFFFFUL) | ((slow & 0xFFFFFFFFUL) << 32);
                        rhigh = ((dlow >> 32) & 0xFFFFFFFFUL) | ((slow >> 32) << 32);
                    }
                    else
                    {
                        rlow = (dhigh & 0xFFFFFFFFUL) | ((shigh & 0xFFFFFFFFUL) << 32);
                        rhigh = ((dhigh >> 32) & 0xFFFFFFFFUL) | ((shigh >> 32) << 32);
                    }

                    context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
                    break;
                }

            case Mnemonic.Cvtsi2Ss:
                {
                    int value = (int)ReadGprOrMemory(instruction.Operand2, gprs, memory, segmentBases);
                    context.WriteXmmElement(
                        instruction.Operand1.RegisterIndex, 0, BitConverter.SingleToUInt32Bits(value));
                    break;
                }

            case Mnemonic.Cvtss2Si:
            case Mnemonic.Cvttss2Si:
                {
                    (ulong low, ulong high) = ReadXmm(
                        instruction.Operand2, context, gprs, memory, segmentBases);
                    float value = BitConverter.UInt32BitsToSingle(ElementBits(low, high, 0));
                    int result = ConvertFloatToInt32(
                        value, instruction.Mnemonic == Mnemonic.Cvttss2Si, context.Mxcsr);
                    gprs[instruction.Operand1.RegisterIndex] = (uint)result;
                    break;
                }

            case Mnemonic.Ldmxcsr:
                context.Mxcsr = ReadMemory(TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 32, memory);
                break;

            case Mnemonic.Stmxcsr:
                WriteMemory(
                    TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 32, context.Mxcsr, memory);
                break;

            case Mnemonic.Movntps:
                {
                    (ulong low, ulong high) = context.ReadXmm128(instruction.Operand2.RegisterIndex);
                    WriteXmm(instruction.Operand1, low, high, context, gprs, memory, segmentBases);
                    break;
                }

            default:
                throw new InvalidOperationException($"Unsupported SSE instruction {instruction.Mnemonic}.");
        }

        return eflags;
    }

    private static void ExecuteArithmetic(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        bool scalar = instruction.Mnemonic is Mnemonic.Addss or Mnemonic.Subss or Mnemonic.Mulss
            or Mnemonic.Divss or Mnemonic.Minss or Mnemonic.Maxss;
        (ulong slow, ulong shigh) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
            float b = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, 0));
            context.WriteXmmElement(
                instruction.Operand1.RegisterIndex,
                0,
                BitConverter.SingleToUInt32Bits(ApplyArithmetic(instruction.Mnemonic, a, b)));
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(context.ReadXmmElement(instruction.Operand1.RegisterIndex, i));
            float b = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, i));
            uint bits = BitConverter.SingleToUInt32Bits(ApplyArithmetic(instruction.Mnemonic, a, b));
            if (i < 2)
            {
                rlow |= (ulong)bits << (32 * i);
            }
            else
            {
                rhigh |= (ulong)bits << (32 * (i - 2));
            }
        }

        context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static float ApplyArithmetic(Mnemonic mnemonic, float left, float right)
    {
        return mnemonic switch
        {
            Mnemonic.Addps or Mnemonic.Addss => left + right,
            Mnemonic.Subps or Mnemonic.Subss => left - right,
            Mnemonic.Mulps or Mnemonic.Mulss => left * right,
            Mnemonic.Divps or Mnemonic.Divss => left / right,
            Mnemonic.Minps or Mnemonic.Minss => left < right ? left : right,
            _ => left > right ? left : right,
        };
    }

    private static void ExecuteUnary(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        bool scalar = instruction.Mnemonic is Mnemonic.Sqrtss or Mnemonic.Rsqrtss or Mnemonic.Rcpss;
        (ulong slow, ulong shigh) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, 0));
            context.WriteXmmElement(
                instruction.Operand1.RegisterIndex,
                0,
                BitConverter.SingleToUInt32Bits(ApplyUnary(instruction.Mnemonic, a)));
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, i));
            uint bits = BitConverter.SingleToUInt32Bits(ApplyUnary(instruction.Mnemonic, a));
            if (i < 2)
            {
                rlow |= (ulong)bits << (32 * i);
            }
            else
            {
                rhigh |= (ulong)bits << (32 * (i - 2));
            }
        }

        context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static float ApplyUnary(Mnemonic mnemonic, float value)
    {
        return mnemonic switch
        {
            Mnemonic.Sqrtps or Mnemonic.Sqrtss => MathF.Sqrt(value),
            Mnemonic.Rsqrtps or Mnemonic.Rsqrtss => 1f / MathF.Sqrt(value),
            _ => 1f / value,
        };
    }

    private static void ExecuteLogical(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        (ulong alow, ulong ahigh) = context.ReadXmm128(instruction.Operand1.RegisterIndex);
        (ulong blow, ulong bhigh) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        (ulong low, ulong high) = instruction.Mnemonic switch
        {
            Mnemonic.Andps => (alow & blow, ahigh & bhigh),
            Mnemonic.Andnps => (~alow & blow, ~ahigh & bhigh),
            Mnemonic.Orps => (alow | blow, ahigh | bhigh),
            _ => (alow ^ blow, ahigh ^ bhigh),
        };
        context.WriteXmm128(instruction.Operand1.RegisterIndex, low, high);
    }

    private static void ExecuteCompare(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        bool scalar = instruction.Mnemonic == Mnemonic.Cmpss;
        int predicate = (int)(instruction.Operand3.Immediate & 7);
        (ulong slow, ulong shigh) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        if (scalar)
        {
            float a = BitConverter.UInt32BitsToSingle(context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
            float b = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, 0));
            context.WriteXmmElement(
                instruction.Operand1.RegisterIndex, 0, EvaluateCompare(predicate, a, b) ? 0xFFFFFFFFu : 0u);
            return;
        }

        ulong rlow = 0;
        ulong rhigh = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = BitConverter.UInt32BitsToSingle(context.ReadXmmElement(instruction.Operand1.RegisterIndex, i));
            float b = BitConverter.UInt32BitsToSingle(ElementBits(slow, shigh, i));
            uint mask = EvaluateCompare(predicate, a, b) ? 0xFFFFFFFFu : 0u;
            if (i < 2)
            {
                rlow |= (ulong)mask << (32 * i);
            }
            else
            {
                rhigh |= (ulong)mask << (32 * (i - 2));
            }
        }

        context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static bool EvaluateCompare(int predicate, float left, float right)
    {
        bool unordered = float.IsNaN(left) || float.IsNaN(right);
        return predicate switch
        {
            0 => left == right,
            1 => left < right,
            2 => left <= right,
            3 => unordered,
            4 => left != right,
            5 => !(left < right),
            6 => !(left <= right),
            _ => !unordered,
        };
    }

    private static uint CompareScalar(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases,
        uint eflags)
    {
        float left = BitConverter.UInt32BitsToSingle(context.ReadXmmElement(instruction.Operand1.RegisterIndex, 0));
        (ulong low, ulong high) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        float right = BitConverter.UInt32BitsToSingle(ElementBits(low, high, 0));
        uint flags = eflags & ~(FlagBits.Zf | FlagBits.Pf | FlagBits.Cf | FlagBits.Of | FlagBits.Sf | FlagBits.Af);
        if (float.IsNaN(left) || float.IsNaN(right))
        {
            flags |= FlagBits.Zf | FlagBits.Pf | FlagBits.Cf;
        }
        else if (left < right)
        {
            flags |= FlagBits.Cf;
        }
        else if (left == right)
        {
            flags |= FlagBits.Zf;
        }

        return flags;
    }

    private static void ExecuteShufps(
        in DecodedInstruction instruction,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        (ulong dlow, ulong dhigh) = context.ReadXmm128(instruction.Operand1.RegisterIndex);
        (ulong slow, ulong shigh) = ReadXmm(instruction.Operand2, context, gprs, memory, segmentBases);
        uint control = instruction.Operand3.Immediate;
        Span<uint> destination =
        [
            ElementBits(dlow, dhigh, 0),
            ElementBits(dlow, dhigh, 1),
            ElementBits(dlow, dhigh, 2),
            ElementBits(dlow, dhigh, 3),
        ];
        Span<uint> source =
        [
            ElementBits(slow, shigh, 0),
            ElementBits(slow, shigh, 1),
            ElementBits(slow, shigh, 2),
            ElementBits(slow, shigh, 3),
        ];

        ulong rlow = destination[(int)(control & 3)] | ((ulong)destination[(int)((control >> 2) & 3)] << 32);
        ulong rhigh = source[(int)((control >> 4) & 3)] | ((ulong)source[(int)((control >> 6) & 3)] << 32);
        context.WriteXmm128(instruction.Operand1.RegisterIndex, rlow, rhigh);
    }

    private static (ulong Low, ulong High) ReadXmm(
        Operand operand,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        if (operand.Kind == OperandKind.Register)
        {
            return context.ReadXmm128(operand.RegisterIndex);
        }

        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        return (ReadMemory64(address, memory), ReadMemory64(address + 8, memory));
    }

    private static void WriteXmm(
        Operand operand,
        ulong low,
        ulong high,
        XboxContext context,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        if (operand.Kind == OperandKind.Register)
        {
            context.WriteXmm128(operand.RegisterIndex, low, high);
            return;
        }

        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        WriteMemory64(address, low, memory);
        WriteMemory64(address + 8, high, memory);
    }

    private static ulong ReadXmm64(int register, int half, XboxContext context)
    {
        return half == 0 ? context.ReadXmm128(register).Low : context.ReadXmm128(register).High;
    }

    private static void WriteXmm64(int register, int half, ulong value, XboxContext context)
    {
        (ulong low, ulong high) = context.ReadXmm128(register);
        if (half == 0)
        {
            low = value;
        }
        else
        {
            high = value;
        }

        context.WriteXmm128(register, low, high);
    }

    private static uint ElementBits(ulong low, ulong high, int index)
    {
        return index < 2 ? (uint)(low >> (32 * index)) : (uint)(high >> (32 * (index - 2)));
    }

    private static int ConvertFloatToInt32(float value, bool truncate, uint mxcsr)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return unchecked((int)0x80000000u);
        }

        float rounded;
        if (truncate)
        {
            rounded = MathF.Truncate(value);
        }
        else
        {
            rounded = ((mxcsr >> 13) & 3) switch
            {
                1 => MathF.Floor(value),
                2 => MathF.Ceiling(value),
                3 => MathF.Truncate(value),
                _ => MathF.Round(value, MidpointRounding.ToEven),
            };
        }

        return rounded is < -2147483648f or >= 2147483648f
            ? unchecked((int)0x80000000u)
            : (int)rounded;
    }

    private static uint ReadGprOrMemory(
        Operand operand,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        return operand.Kind == OperandKind.Register
            ? operand.Size == 16 ? gprs[operand.RegisterIndex] & 0xFFFFu : gprs[operand.RegisterIndex]
            : ReadMemory(TcgAddress.Of(operand, gprs, segmentBases), operand.Size, memory);
    }

    private static uint ReadMemory(uint address, int bits, ITcgMemory? memory)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("an SSE memory access needs an ITcgMemory");
        }

        return bits switch
        {
            8 => memory.Read8(address),
            16 => memory.Read16(address),
            _ => memory.Read32(address),
        };
    }

    private static void WriteMemory(uint address, int bits, uint value, ITcgMemory? memory)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("an SSE memory access needs an ITcgMemory");
        }

        switch (bits)
        {
            case 8:
                memory.Write8(address, (byte)value);
                break;
            case 16:
                memory.Write16(address, (ushort)value);
                break;
            default:
                memory.Write32(address, value);
                break;
        }
    }

    private static ulong ReadMemory64(uint address, ITcgMemory? memory)
    {
        return ReadMemory(address, 32, memory) | ((ulong)ReadMemory(address + 4, 32, memory) << 32);
    }

    private static void WriteMemory64(uint address, ulong value, ITcgMemory? memory)
    {
        WriteMemory(address, 32, (uint)value, memory);
        WriteMemory(address + 4, 32, (uint)(value >> 32), memory);
    }
}
