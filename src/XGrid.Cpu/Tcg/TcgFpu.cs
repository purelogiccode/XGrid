using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The x87 instruction executor behind <c>TcgOpKind.Fpu</c> (P1S3 slice 11).
/// The semantics are transcribed from the exact interpreter's
/// <c>XboxCpu.ExecuteFpu</c> and its helpers, over the same <see cref="FpuState"/>
/// and <see cref="Fpu80"/> arithmetic; the host code generator (P1S4) calls this
/// same helper for the shapes it does not encode natively.
/// </summary>
public static class TcgFpu
{
    /// <summary>Gets whether the translator can hand this mnemonic to <see cref="Execute"/>.</summary>
    /// <param name="mnemonic">The x87 mnemonic.</param>
    /// <returns>False for the environment save/restore shapes (interpreter fallback).</returns>
    public static bool IsSupported(Mnemonic mnemonic)
    {
        return IsFpuMnemonic(mnemonic) && mnemonic is not (
            Mnemonic.Fldenv or Mnemonic.Fnstenv or Mnemonic.Fnsave or Mnemonic.Frstor);
    }

    /// <summary>Gets whether the mnemonic is in the interpreter's x87 dispatch range.</summary>
    /// <param name="mnemonic">The mnemonic.</param>
    /// <returns>True for Fwait..Fsincos plus Ficom/Ficomp.</returns>
    public static bool IsFpuMnemonic(Mnemonic mnemonic)
    {
        return (mnemonic >= Mnemonic.Fwait && mnemonic <= Mnemonic.Fsincos)
            || mnemonic is Mnemonic.Ficom or Mnemonic.Ficomp;
    }

    /// <summary>Executes one x87 instruction.</summary>
    /// <param name="instruction">The decoded instruction.</param>
    /// <param name="fpu">The FPU state.</param>
    /// <param name="gprs">The eight guest GPRs (x87 memory addressing).</param>
    /// <param name="memory">The guest memory.</param>
    /// <param name="segmentBases">The six segment bases in <see cref="SegmentRegister"/> order.</param>
    /// <param name="eflags">EFLAGS (FCOMI updates it).</param>
    /// <returns>The updated EFLAGS.</returns>
    public static uint Execute(
        in DecodedInstruction instruction,
        FpuState fpu,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases,
        uint eflags)
    {
        ArgumentNullException.ThrowIfNull(fpu);
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Fwait:
            case Mnemonic.Fnop:
                break;

            case Mnemonic.Fld:
                fpu.Push(ReadReal(instruction.Operand1, fpu, gprs, memory, segmentBases));
                break;

            case Mnemonic.Fst:
                WriteReal(instruction.Operand1, fpu.GetRegister(0), fpu, gprs, memory, segmentBases);
                break;

            case Mnemonic.Fstp:
                WriteReal(instruction.Operand1, fpu.GetRegister(0), fpu, gprs, memory, segmentBases);
                fpu.Pop();
                break;

            case Mnemonic.Fild:
                fpu.Push(ReadInteger(instruction.Operand1, gprs, memory, segmentBases));
                break;

            case Mnemonic.Fist:
                WriteInteger(instruction.Operand1, fpu.GetRegister(0), fpu, gprs, memory, segmentBases);
                break;

            case Mnemonic.Fistp:
                WriteInteger(instruction.Operand1, fpu.GetRegister(0), fpu, gprs, memory, segmentBases);
                fpu.Pop();
                break;

            case Mnemonic.Fbld:
                fpu.Push(ReadBcd(TcgAddress.Of(instruction.Operand1, gprs, segmentBases), fpu, memory));
                break;

            case Mnemonic.Fbstp:
                WriteBcd(TcgAddress.Of(instruction.Operand1, gprs, segmentBases), fpu.GetRegister(0), fpu, memory);
                fpu.Pop();
                break;

            case Mnemonic.Fxch:
                fpu.Exchange(instruction.Operand1.RegisterIndex);
                break;

            case Mnemonic.Fadd:
            case Mnemonic.Fmul:
            case Mnemonic.Fsub:
            case Mnemonic.Fsubr:
            case Mnemonic.Fdiv:
            case Mnemonic.Fdivr:
                {
                    Fpu80 left = ReadReal(instruction.Operand1, fpu, gprs, memory, segmentBases);
                    Fpu80 right = ReadReal(instruction.Operand2, fpu, gprs, memory, segmentBases);
                    WriteReal(
                        instruction.Operand1,
                        ApplyArithmetic(instruction.Mnemonic, left, right, fpu),
                        fpu,
                        gprs,
                        memory,
                        segmentBases);
                    break;
                }

            case Mnemonic.Faddp:
            case Mnemonic.Fmulp:
            case Mnemonic.Fsubp:
            case Mnemonic.Fsubrp:
            case Mnemonic.Fdivp:
            case Mnemonic.Fdivrp:
                {
                    Fpu80 left = fpu.GetRegister(instruction.Operand1.RegisterIndex);
                    Fpu80 right = fpu.GetRegister(0);
                    fpu.SetRegister(
                        instruction.Operand1.RegisterIndex,
                        ApplyArithmetic(instruction.Mnemonic, left, right, fpu));
                    fpu.Pop();
                    break;
                }

            case Mnemonic.Fiadd:
            case Mnemonic.Fimul:
            case Mnemonic.Fisub:
            case Mnemonic.Fisubr:
            case Mnemonic.Fidiv:
            case Mnemonic.Fidivr:
                fpu.SetRegister(
                    0,
                    ApplyArithmetic(
                        instruction.Mnemonic,
                        fpu.GetRegister(0),
                        ReadInteger(instruction.Operand1, gprs, memory, segmentBases),
                        fpu));
                break;

            case Mnemonic.Fcom:
            case Mnemonic.Fcomp:
            case Mnemonic.Fucom:
            case Mnemonic.Fucomp:
                {
                    Operand compared = instruction.Operand2.Kind == OperandKind.None
                        ? instruction.Operand1
                        : instruction.Operand2;
                    bool quiet = instruction.Mnemonic is Mnemonic.Fucom or Mnemonic.Fucomp;
                    SetCompare(fpu, fpu.GetRegister(0), ReadReal(compared, fpu, gprs, memory, segmentBases), quiet);
                    if (instruction.Mnemonic is Mnemonic.Fcomp or Mnemonic.Fucomp)
                    {
                        fpu.Pop();
                    }

                    break;
                }

            case Mnemonic.Ficom:
            case Mnemonic.Ficomp:
                SetCompare(
                    fpu,
                    fpu.GetRegister(0),
                    ReadInteger(instruction.Operand1, gprs, memory, segmentBases));
                if (instruction.Mnemonic == Mnemonic.Ficomp)
                {
                    fpu.Pop();
                }

                break;

            case Mnemonic.Fcompp:
            case Mnemonic.Fucompp:
                SetCompare(
                    fpu,
                    fpu.GetRegister(0),
                    fpu.GetRegister(1),
                    instruction.Mnemonic == Mnemonic.Fucompp);
                fpu.Pop();
                fpu.Pop();
                break;

            case Mnemonic.Fcomi:
            case Mnemonic.Fcomip:
            case Mnemonic.Fucomi:
            case Mnemonic.Fucomip:
                eflags = FlagsCompare(
                    fpu,
                    eflags,
                    fpu.GetRegister(0),
                    fpu.GetRegister(instruction.Operand2.RegisterIndex),
                    instruction.Mnemonic is Mnemonic.Fucomi or Mnemonic.Fucomip);
                if (instruction.Mnemonic is Mnemonic.Fcomip or Mnemonic.Fucomip)
                {
                    fpu.Pop();
                }

                break;

            case Mnemonic.Ftst:
                SetCompare(fpu, fpu.GetRegister(0), Fpu80.Zero);
                break;

            case Mnemonic.Fxam:
                ExecuteFxam(fpu);
                break;

            case Mnemonic.Fchs:
                fpu.SetRegister(0, fpu.GetRegister(0).Negate());
                break;

            case Mnemonic.Fabs:
                fpu.SetRegister(0, fpu.GetRegister(0).Abs());
                break;

            case Mnemonic.Fsqrt:
                {
                    Fpu80 source = fpu.GetRegister(0);
                    if (source.IsNegative && !source.IsZero && !source.IsNaN)
                    {
                        fpu.SetConditionCodes(false, true, false);
                        fpu.SetException(0);
                    }

                    fpu.SetRegister(0, Fpu80.SquareRoot(source));
                    break;
                }

            case Mnemonic.Frndint:
                fpu.SetRegister(0, Fpu80.FromDouble(RoundToInteger(fpu.GetRegister(0).ToDouble(), fpu.RoundingMode)));
                break;

            case Mnemonic.Fscale:
                {
                    double scale = Math.Truncate(fpu.GetRegister(1).ToDouble());
                    int exponent = (int)Math.Clamp(scale, -32768, 32767);
                    fpu.SetRegister(0, Fpu80.FromDouble(Math.ScaleB(fpu.GetRegister(0).ToDouble(), exponent)));
                    break;
                }

            case Mnemonic.Fxtract:
                ExecuteFxtract(fpu);
                break;

            case Mnemonic.Fprem:
            case Mnemonic.Fprem1:
                ExecuteFprem(fpu, instruction.Mnemonic == Mnemonic.Fprem1);
                break;

            case Mnemonic.Fdecstp:
                fpu.Top = (fpu.Top - 1) & 7;
                break;

            case Mnemonic.Fincstp:
                fpu.Top = (fpu.Top + 1) & 7;
                break;

            case Mnemonic.Ffree:
                fpu.Free(instruction.Operand1.RegisterIndex);
                break;

            case Mnemonic.Ffreep:
                fpu.Free(instruction.Operand1.RegisterIndex);
                fpu.Pop();
                break;

            case Mnemonic.Fninit:
                fpu.Init();
                break;

            case Mnemonic.Fnclex:
                fpu.ClearExceptions();
                break;

            case Mnemonic.Fldcw:
                fpu.ControlWord = (ushort)ReadMemory(
                    TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 16, memory);
                break;

            case Mnemonic.Fnstcw:
                WriteMemory(
                    TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 16, fpu.ControlWord, memory);
                break;

            case Mnemonic.Fnstsw:
                if (instruction.Operand1.Kind == OperandKind.None)
                {
                    throw new InvalidOperationException("FNSTSW AX is a register form");
                }

                WriteMemory(
                    TcgAddress.Of(instruction.Operand1, gprs, segmentBases), 16, fpu.StatusWord, memory);
                break;

            case Mnemonic.Fcmovb:
            case Mnemonic.Fcmove:
            case Mnemonic.Fcmovbe:
            case Mnemonic.Fcmovu:
            case Mnemonic.Fcmovnb:
            case Mnemonic.Fcmovne:
            case Mnemonic.Fcmovnbe:
            case Mnemonic.Fcmovnu:
                if (EvaluateFcmov(instruction.Mnemonic, eflags))
                {
                    fpu.SetRegister(0, fpu.GetRegister(instruction.Operand2.RegisterIndex));
                }

                break;

            case Mnemonic.Fld1:
                fpu.Push(Fpu80.One);
                break;
            case Mnemonic.Fldl2T:
                fpu.Push(Fpu80.Log2Ten);
                break;
            case Mnemonic.Fldl2E:
                fpu.Push(Fpu80.Log2E);
                break;
            case Mnemonic.Fldpi:
                fpu.Push(Fpu80.Pi);
                break;
            case Mnemonic.Fldlg2:
                fpu.Push(Fpu80.Log10Two);
                break;
            case Mnemonic.Fldln2:
                fpu.Push(Fpu80.LnTwo);
                break;
            case Mnemonic.Fldz:
                fpu.Push(Fpu80.Zero);
                break;

            case Mnemonic.F2Xm1:
                fpu.SetRegister(0, Fpu80.FromDouble(Math.Pow(2.0, fpu.GetRegister(0).ToDouble()) - 1.0));
                break;

            case Mnemonic.Fyl2X:
                {
                    double result = fpu.GetRegister(1).ToDouble() * Math.Log2(fpu.GetRegister(0).ToDouble());
                    fpu.SetRegister(1, Fpu80.FromDouble(result));
                    fpu.Pop();
                    break;
                }

            case Mnemonic.Fyl2Xp1:
                {
                    double result = fpu.GetRegister(1).ToDouble() * Math.Log2(fpu.GetRegister(0).ToDouble() + 1.0);
                    fpu.SetRegister(1, Fpu80.FromDouble(result));
                    fpu.Pop();
                    break;
                }

            case Mnemonic.Fptan:
                fpu.SetRegister(0, Fpu80.FromDouble(Math.Tan(fpu.GetRegister(0).ToDouble())));
                fpu.Push(Fpu80.One);
                break;

            case Mnemonic.Fpatan:
                {
                    double result = Math.Atan2(fpu.GetRegister(1).ToDouble(), fpu.GetRegister(0).ToDouble());
                    fpu.SetRegister(1, Fpu80.FromDouble(result));
                    fpu.Pop();
                    break;
                }

            case Mnemonic.Fsin:
                fpu.SetRegister(0, Fpu80.FromDouble(Math.Sin(fpu.GetRegister(0).ToDouble())));
                break;

            case Mnemonic.Fcos:
                fpu.SetRegister(0, Fpu80.FromDouble(Math.Cos(fpu.GetRegister(0).ToDouble())));
                break;

            case Mnemonic.Fsincos:
                {
                    double angle = fpu.GetRegister(0).ToDouble();
                    fpu.SetRegister(0, Fpu80.FromDouble(Math.Sin(angle)));
                    fpu.Push(Fpu80.FromDouble(Math.Cos(angle)));
                    break;
                }

            default:
                throw new InvalidOperationException($"Unsupported x87 instruction {instruction.Mnemonic}.");
        }

        return eflags;
    }

    private static Fpu80 ApplyArithmetic(Mnemonic mnemonic, Fpu80 left, Fpu80 right, FpuState fpu)
    {
        if (mnemonic is Mnemonic.Fdiv or Mnemonic.Fdivp or Mnemonic.Fidiv)
        {
            SetDivideExceptions(left, right, fpu);
        }
        else if (mnemonic is Mnemonic.Fdivr or Mnemonic.Fdivrp or Mnemonic.Fidivr)
        {
            SetDivideExceptions(right, left, fpu);
        }

        return mnemonic switch
        {
            Mnemonic.Fadd or Mnemonic.Faddp or Mnemonic.Fiadd => Fpu80.Add(left, right),
            Mnemonic.Fmul or Mnemonic.Fmulp or Mnemonic.Fimul => Fpu80.Multiply(left, right),
            Mnemonic.Fsub or Mnemonic.Fsubp or Mnemonic.Fisub => Fpu80.Subtract(left, right),
            Mnemonic.Fsubr or Mnemonic.Fsubrp or Mnemonic.Fisubr => Fpu80.Subtract(right, left),
            Mnemonic.Fdiv or Mnemonic.Fdivp or Mnemonic.Fidiv => Fpu80.Divide(left, right),
            _ => Fpu80.Divide(right, left),
        };
    }

    private static void SetDivideExceptions(Fpu80 dividend, Fpu80 divisor, FpuState fpu)
    {
        if (!divisor.IsZero)
        {
            if (divisor.IsInfinity && dividend.IsInfinity)
            {
                fpu.SetException(0);
            }

            return;
        }

        fpu.SetException(dividend.IsZero ? 0 : 2);
    }

    private static void SetCompare(FpuState fpu, Fpu80 left, Fpu80 right, bool quiet = false)
    {
        if (left.IsNaN || right.IsNaN)
        {
            fpu.SetConditionCodes(true, true, true);
            if (!quiet || left.IsSignalingNaN || right.IsSignalingNaN)
            {
                fpu.SetException(0);
            }

            return;
        }

        double a = left.ToDouble();
        double b = right.ToDouble();
        fpu.SetConditionCodes(a == b, false, a < b);
    }

    private static uint FlagsCompare(FpuState fpu, uint eflags, Fpu80 left, Fpu80 right, bool quiet)
    {
        uint flags = eflags & ~(FlagBits.Zf | FlagBits.Pf | FlagBits.Cf | FlagBits.Of | FlagBits.Sf | FlagBits.Af);
        if (left.IsNaN || right.IsNaN)
        {
            flags |= FlagBits.Zf | FlagBits.Pf | FlagBits.Cf;
            if (!quiet || left.IsSignalingNaN || right.IsSignalingNaN)
            {
                fpu.SetException(0);
            }
        }
        else
        {
            double a = left.ToDouble();
            double b = right.ToDouble();
            if (a < b)
            {
                flags |= FlagBits.Cf;
            }
            else if (a == b)
            {
                flags |= FlagBits.Zf;
            }
        }

        return flags;
    }

    private static void ExecuteFxam(FpuState fpu)
    {
        Fpu80 value = fpu.GetRegister(0);
        int tag = (fpu.TagWord >> (fpu.Top * 2)) & 3;
        bool c3;
        bool c2;
        bool c0;

        if (tag == FpuState.TagEmpty)
        {
            (c3, c2, c0) = (true, false, true);
        }
        else if (value.IsNaN)
        {
            (c3, c2, c0) = (false, false, true);
        }
        else if (value.IsInfinity)
        {
            (c3, c2, c0) = (false, true, true);
        }
        else if (value.IsZero)
        {
            (c3, c2, c0) = (true, false, false);
        }
        else if (value.Exponent == 0 || value.IsUnnormal)
        {
            (c3, c2, c0) = (true, true, false);
        }
        else
        {
            (c3, c2, c0) = (false, true, false);
        }

        fpu.SetConditionCodes(c3, c2, c0);
        fpu.SetC1(tag != FpuState.TagEmpty && value.IsNegative);
    }

    private static void ExecuteFxtract(FpuState fpu)
    {
        Fpu80 value = fpu.GetRegister(0);
        double magnitude = Math.Abs(value.ToDouble());
        if (magnitude == 0 || double.IsInfinity(magnitude) || double.IsNaN(magnitude))
        {
            double exponent = magnitude == 0 ? double.NegativeInfinity : double.PositiveInfinity;
            if (double.IsNaN(magnitude))
            {
                exponent = double.NaN;
            }

            fpu.SetRegister(0, Fpu80.FromDouble(exponent));
            fpu.Push(Fpu80.FromDouble(value.IsNegative ? -magnitude : magnitude));
            return;
        }

        int exponentValue = Math.ILogB(magnitude);
        double significand = Math.ScaleB(magnitude, -exponentValue);
        fpu.SetRegister(0, Fpu80.FromInt32(exponentValue));
        fpu.Push(Fpu80.FromDouble(value.IsNegative ? -significand : significand));
    }

    private static void ExecuteFprem(FpuState fpu, bool ieee)
    {
        double dividend = fpu.GetRegister(0).ToDouble();
        double divisor = fpu.GetRegister(1).ToDouble();
        if (double.IsNaN(dividend) || double.IsNaN(divisor) || double.IsInfinity(dividend) || divisor == 0)
        {
            fpu.SetRegister(0, Fpu80.FromDouble(double.NaN));
            fpu.SetConditionCodes(false, false, false);
            return;
        }

        double quotient;
        double remainder;
        if (ieee)
        {
            remainder = Math.IEEERemainder(dividend, divisor);
            quotient = Math.Round((dividend - remainder) / divisor);
        }
        else
        {
            quotient = Math.Truncate(dividend / divisor);
            remainder = dividend - (quotient * divisor);
        }

        fpu.SetRegister(0, Fpu80.FromDouble(remainder));
        long q = (long)quotient;
        fpu.SetConditionCodes((q & 2) != 0, false, (q & 4) != 0);
        fpu.SetC1((q & 1) != 0);
    }

    private static double RoundToInteger(double value, int roundingMode)
    {
        return roundingMode switch
        {
            1 => Math.Floor(value),
            2 => Math.Ceiling(value),
            3 => Math.Truncate(value),
            _ => Math.Round(value, MidpointRounding.ToEven),
        };
    }

    private static bool EvaluateFcmov(Mnemonic mnemonic, uint eflags)
    {
        bool cf = (eflags & FlagBits.Cf) != 0;
        bool zf = (eflags & FlagBits.Zf) != 0;
        bool pf = (eflags & FlagBits.Pf) != 0;
        return mnemonic switch
        {
            Mnemonic.Fcmovb => cf,
            Mnemonic.Fcmove => zf,
            Mnemonic.Fcmovbe => cf || zf,
            Mnemonic.Fcmovu => pf,
            Mnemonic.Fcmovnb => !cf,
            Mnemonic.Fcmovne => !zf,
            Mnemonic.Fcmovnbe => !cf && !zf,
            _ => !pf,
        };
    }

    private static Fpu80 ReadReal(
        Operand operand,
        FpuState fpu,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        if (operand.Kind == OperandKind.Register)
        {
            return fpu.GetRegister(operand.RegisterIndex);
        }

        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        return operand.Size switch
        {
            32 => Fpu80.FromSingleBits(ReadMemory(address, 32, memory)),
            64 => Fpu80.FromDoubleBits(ReadMemory64(address, memory)),
            _ => new Fpu80(
                (ushort)ReadMemory(address + 8, 16, memory),
                ReadMemory64(address, memory)),
        };
    }

    private static Fpu80 ReadInteger(
        Operand operand,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        return operand.Size switch
        {
            16 => Fpu80.FromInt64((short)ReadMemory(address, 16, memory)),
            32 => Fpu80.FromInt64((int)ReadMemory(address, 32, memory)),
            _ => Fpu80.FromInt64((long)ReadMemory64(address, memory)),
        };
    }

    private static void WriteReal(
        Operand operand,
        Fpu80 value,
        FpuState fpu,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        if (operand.Kind == OperandKind.Register)
        {
            fpu.SetRegister(operand.RegisterIndex, value);
            return;
        }

        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        switch (operand.Size)
        {
            case 32:
                WriteMemory(address, 32, value.ToSingleBits(), memory);
                break;
            case 64:
                WriteMemory64(address, value.ToDoubleBits(), memory);
                break;
            default:
                WriteMemory64(address, value.Significand, memory);
                WriteMemory(address + 8, 16, value.SignExponent, memory);
                break;
        }
    }

    private static void WriteInteger(
        Operand operand,
        Fpu80 value,
        FpuState fpu,
        ReadOnlySpan<uint> gprs,
        ITcgMemory? memory,
        ReadOnlySpan<uint> segmentBases)
    {
        uint address = TcgAddress.Of(operand, gprs, segmentBases);
        int mode = fpu.RoundingMode;
        switch (operand.Size)
        {
            case 16:
                if (!value.TryToInt16(mode, out short small))
                {
                    small = unchecked((short)0x8000);
                    fpu.SetException(0);
                }

                WriteMemory(address, 16, (ushort)small, memory);
                break;
            case 32:
                if (!value.TryToInt32(mode, out int word))
                {
                    word = unchecked((int)0x80000000u);
                    fpu.SetException(0);
                }

                WriteMemory(address, 32, (uint)word, memory);
                break;
            default:
                if (!value.TryToInt64(mode, out long wide))
                {
                    wide = long.MinValue;
                    fpu.SetException(0);
                }

                WriteMemory64(address, (ulong)wide, memory);
                break;
        }
    }

    private static Fpu80 ReadBcd(uint address, FpuState fpu, ITcgMemory? memory)
    {
        Span<byte> bytes = stackalloc byte[10];
        ReadBytes(address, bytes, memory);
        ulong magnitude = 0;
        ulong multiplier = 1;
        for (int i = 0; i < 9; i++)
        {
            magnitude += (ulong)(bytes[i] & 0xF) * multiplier;
            multiplier *= 10;
            magnitude += (ulong)(bytes[i] >> 4) * multiplier;
            multiplier *= 10;
        }

        Fpu80 value = Fpu80.FromInt64((long)magnitude);
        return (bytes[9] & 0x80) != 0 ? value.Negate() : value;
    }

    private static void WriteBcd(uint address, Fpu80 value, FpuState fpu, ITcgMemory? memory)
    {
        Span<byte> bytes = stackalloc byte[10];
        bytes.Clear();
        if (!value.TryToInt64(3, out long integer) || integer == long.MinValue)
        {
            fpu.SetException(0);
        }
        else
        {
            bool negative = integer < 0;
            ulong magnitude = negative ? (ulong)(-(integer + 1)) + 1UL : (ulong)integer;
            for (int i = 0; i < 9; i++)
            {
                int low = (int)(magnitude % 10);
                magnitude /= 10;
                int high = (int)(magnitude % 10);
                magnitude /= 10;
                bytes[i] = (byte)(low | (high << 4));
            }

            bytes[9] = negative ? (byte)0x80 : (byte)0;
        }

        WriteBytes(address, bytes, memory);
    }

    private static uint ReadMemory(uint address, int bits, ITcgMemory? memory)
    {
        if (memory is null)
        {
            throw new InvalidOperationException("an x87 memory access needs an ITcgMemory");
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
            throw new InvalidOperationException("an x87 memory access needs an ITcgMemory");
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

    private static void ReadBytes(uint address, Span<byte> destination, ITcgMemory? memory)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)ReadMemory(address + (uint)i, 8, memory);
        }
    }

    private static void WriteBytes(uint address, ReadOnlySpan<byte> source, ITcgMemory? memory)
    {
        for (int i = 0; i < source.Length; i++)
        {
            WriteMemory(address + (uint)i, 8, source[i], memory);
        }
    }
}
