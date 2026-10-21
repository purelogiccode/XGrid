using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// IEEE 754 80-bit extended-precision value in the x87 register format.
/// Arithmetic is performed in double precision and rounded back (documented limitation).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Fpu80 : IEquatable<Fpu80>
{
    /// <summary>Exponent bias of the extended format.</summary>
    public const int ExponentBias = 16383;

    /// <summary>Explicit-integer-bit mask of the significand.</summary>
    public const ulong IntegerBit = 0x8000000000000000UL;

    /// <summary>Creates a value from its raw fields.</summary>
    public Fpu80(ushort signExponent, ulong significand)
    {
        SignExponent = signExponent;
        Significand = significand;
    }

    /// <summary>Gets the sign bit (15) and biased exponent (14-0).</summary>
    public ushort SignExponent { get; }

    /// <summary>Gets the 64-bit significand (explicit integer bit at bit 63).</summary>
    public ulong Significand { get; }

    /// <summary>Gets whether the sign bit is set.</summary>
    public bool IsNegative => (SignExponent & 0x8000) != 0;

    /// <summary>Gets the biased exponent.</summary>
    public int Exponent => SignExponent & 0x7FFF;

    /// <summary>Gets a value indicating whether this is +0 or -0.</summary>
    public bool IsZero => Exponent == 0 && Significand == 0;

    /// <summary>Gets a value indicating whether this is a denormal (integer bit clear).</summary>
    public bool IsDenormal => Exponent == 0 && Significand != 0 && (Significand & IntegerBit) == 0;

    /// <summary>Gets a value indicating whether this is a pseudo-denormal (integer bit set).</summary>
    public bool IsPseudoDenormal => Exponent == 0 && Significand != 0 && (Significand & IntegerBit) != 0;

    /// <summary>Gets a value indicating whether this is an unnormal (integer bit clear).</summary>
    public bool IsUnnormal => Exponent is > 0 and < 0x7FFF && (Significand & IntegerBit) == 0;

    /// <summary>Gets a value indicating whether this is an infinity.</summary>
    public bool IsInfinity => Exponent == 0x7FFF && Significand == IntegerBit;

    /// <summary>Gets a value indicating whether this is a NaN (quiet or signaling).</summary>
    public bool IsNaN => Exponent == 0x7FFF && Significand != IntegerBit;

    /// <summary>Gets a value indicating whether this is a signaling NaN (integer bit set, quiet bit clear).</summary>
    public bool IsSignalingNaN => Exponent == 0x7FFF && Significand != IntegerBit
                                  && (Significand & 0x4000000000000000UL) == 0;

    /// <summary>Gets a value indicating whether this is finite (not infinity or NaN).</summary>
    public bool IsFinite => Exponent != 0x7FFF;

    /// <summary>Gets positive zero.</summary>
    public static Fpu80 Zero => new(0, 0);

    /// <summary>Gets negative zero.</summary>
    public static Fpu80 NegativeZero => new(0x8000, 0);

    /// <summary>Gets one.</summary>
    public static Fpu80 One => new(0x3FFF, IntegerBit);

    /// <summary>Gets positive infinity.</summary>
    public static Fpu80 PositiveInfinity => new(0x7FFF, IntegerBit);

    /// <summary>Gets the default indefinite NaN.</summary>
    public static Fpu80 Indefinite => new(0xFFFF, 0xC000000000000000UL);

    /// <summary>Gets log2(10).</summary>
    public static Fpu80 Log2Ten => FromDouble(3.3219280948873623478703194294894);

    /// <summary>Gets log2(e).</summary>
    public static Fpu80 Log2E => FromDouble(1.4426950408889634073599246810019);

    /// <summary>Gets pi.</summary>
    public static Fpu80 Pi => FromDouble(Math.PI);

    /// <summary>Gets log10(2).</summary>
    public static Fpu80 Log10Two => FromDouble(0.30102999566398119521373889472449);

    /// <summary>Gets ln(2).</summary>
    public static Fpu80 LnTwo => FromDouble(0.69314718055994530941723212145818);

    /// <summary>Returns the value with the sign bit flipped.</summary>
    public Fpu80 Negate()
    {
        return new Fpu80((ushort)(SignExponent ^ 0x8000), Significand);
    }

    /// <summary>Returns the absolute value.</summary>
    public Fpu80 Abs()
    {
        return new Fpu80((ushort)(SignExponent & 0x7FFF), Significand);
    }

    /// <summary>Converts to a double (rounds to 53 bits; extended denormals underflow to zero).</summary>
    public double ToDouble()
    {
        if (IsNaN)
        {
            // Preserve the sign (and the top payload bits): .NET's NaN constant
            // is the *negative* quiet NaN, which would flip the sign of every
            // stored NaN.
            return BitConverter.Int64BitsToDouble(unchecked((long)ToDoubleBits()));
        }

        if (IsInfinity)
        {
            return IsNegative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (Significand == 0)
        {
            return IsNegative ? -0.0 : 0.0;
        }

        if (Exponent == 0)
        {
            // Extended denormals are far below the smallest double.
            return IsNegative ? -0.0 : 0.0;
        }

        double magnitude = Math.ScaleB((double)Significand, Exponent - ExponentBias - 63);
        return IsNegative ? -magnitude : magnitude;
    }

    /// <summary>Converts to IEEE 754 single-precision bits, preserving a NaN's sign and payload.</summary>
    public uint ToSingleBits()
    {
        if (IsNaN)
        {
            // A store is not an arithmetic operation: keep the payload (and the
            // signaling bit) exactly as the register holds it.
            return (IsNegative ? 0x80000000u : 0u) | 0x7F800000u
                   | (uint)((Significand >> 40) & 0x007FFFFFUL);
        }

        return BitConverter.SingleToUInt32Bits((float)ToDouble());
    }

    /// <summary>Converts to IEEE 754 double-precision bits, preserving a NaN's sign and payload.</summary>
    public ulong ToDoubleBits()
    {
        if (IsNaN)
        {
            return (IsNegative ? 0x8000000000000000UL : 0UL) | 0x7FF0000000000000UL
                   | ((Significand >> 11) & 0x000FFFFFFFFFFFFFUL);
        }

        return (ulong)BitConverter.DoubleToInt64Bits(ToDouble());
    }

    /// <summary>
    /// Creates a value from IEEE 754 single-precision bits. Unlike the
    /// double-precision conversion path this preserves a signaling NaN (FLD is
    /// not an arithmetic operation, so it must not quiet a NaN).
    /// </summary>
    public static Fpu80 FromSingleBits(uint bits)
    {
        if ((bits & 0x7F800000u) == 0x7F800000u)
        {
            ushort sign = (ushort)((bits >> 16) & 0x8000);
            uint fraction = bits & 0x007FFFFFu;
            return fraction == 0
                ? new Fpu80((ushort)(sign | 0x7FFF), IntegerBit)
                : new Fpu80((ushort)(sign | 0x7FFF), IntegerBit | ((ulong)fraction << 40));
        }

        return FromDouble(BitConverter.UInt32BitsToSingle(bits));
    }

    /// <summary>
    /// Creates a value from IEEE 754 double-precision bits, preserving a
    /// signaling NaN for the same reason as <see cref="FromSingleBits"/>.
    /// </summary>
    public static Fpu80 FromDoubleBits(ulong bits)
    {
        if ((bits & 0x7FF0000000000000UL) == 0x7FF0000000000000UL)
        {
            ushort sign = (ushort)((bits >> 48) & 0x8000);
            ulong fraction = bits & 0x000FFFFFFFFFFFFFUL;
            return fraction == 0
                ? new Fpu80((ushort)(sign | 0x7FFF), IntegerBit)
                : new Fpu80((ushort)(sign | 0x7FFF), IntegerBit | (fraction << 11));
        }

        return FromDouble(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
    }

    /// <summary>Creates a value from a double.</summary>
    public static Fpu80 FromDouble(double value)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        ushort sign = (ushort)((bits >> 48) & 0x8000);
        int exponent = (int)((bits >> 52) & 0x7FF);
        ulong fraction = bits & 0xFFFFFFFFFFFFFUL;

        if (exponent == 0x7FF)
        {
            return fraction == 0
                ? new Fpu80((ushort)(sign | 0x7FFF), IntegerBit)
                : new Fpu80((ushort)(sign | 0x7FFF), 0xC000000000000000UL | (fraction << 11));
        }

        if (exponent == 0)
        {
            if (fraction == 0)
            {
                return new Fpu80(sign, 0);
            }

            int highestBit = 63 - BitOperations.LeadingZeroCount(fraction);
            ulong significand = fraction << (63 - highestBit);
            int extendedExponent = ExponentBias - 1074 + highestBit;
            return new Fpu80((ushort)(sign | extendedExponent), significand);
        }

        ulong extendedSignificand = (fraction | 0x10000000000000UL) << 11;
        int exponentValue = exponent - 1023 + ExponentBias;
        return new Fpu80((ushort)(sign | exponentValue), extendedSignificand);
    }

    /// <summary>Creates a value from a signed 32-bit integer.</summary>
    public static Fpu80 FromInt32(int value)
    {
        return FromInt64(value);
    }

    /// <summary>Creates a value from an unsigned 32-bit integer.</summary>
    public static Fpu80 FromUInt32(uint value)
    {
        return FromInt64(value);
    }

    /// <summary>Creates a value from a signed 64-bit integer.</summary>
    public static Fpu80 FromInt64(long value)
    {
        if (value == 0)
        {
            return Zero;
        }

        bool negative = value < 0;
        ulong magnitude = negative
            ? (ulong)(-(value + 1)) + 1UL
            : (ulong)value;

        ushort sign = negative ? (ushort)0x8000 : (ushort)0;
        return new Fpu80((ushort)(sign | (ExponentBias + 63)), magnitude);
    }

    /// <summary>Converts to a signed 64-bit integer using the given x87 rounding mode.</summary>
    /// <returns>False when the value is invalid or out of range.</returns>
    public bool TryToInt64(int roundingMode, out long value)
    {
        value = unchecked((long)0x8000000000000000UL);

        if (IsNaN || IsInfinity)
        {
            return false;
        }

        if (Significand == 0)
        {
            value = 0;
            return true;
        }

        int shift = Exponent - ExponentBias - 63;

        ulong magnitude;
        if (shift >= 0)
        {
            if (shift >= 64 || (shift > 0 && (Significand >> (64 - shift)) != 0))
            {
                return false;
            }

            magnitude = Significand << shift;
        }
        else
        {
            int right = -shift;
            if (right >= 64)
            {
                magnitude = 0;
            }
            else
            {
                magnitude = Significand >> right;
                bool hasRemainder = (Significand & ((1UL << right) - 1)) != 0;
                bool roundUp = roundingMode switch
                {
                    1 => IsNegative && hasRemainder,
                    2 => !IsNegative && hasRemainder,
                    3 => false,
                    _ => RoundNearestUp(Significand, right),
                };

                if (roundUp)
                {
                    magnitude++;
                }
            }
        }

        if (IsNegative)
        {
            if (magnitude > 0x8000000000000000UL)
            {
                return false;
            }

            value = magnitude == 0x8000000000000000UL
                ? long.MinValue
                : -(long)magnitude;
            return true;
        }

        if (magnitude > long.MaxValue)
        {
            return false;
        }

        value = (long)magnitude;
        return true;
    }

    /// <summary>Converts to a signed 32-bit integer using the given x87 rounding mode.</summary>
    /// <returns>False when the value is invalid or out of range.</returns>
    public bool TryToInt32(int roundingMode, out int value)
    {
        value = unchecked((int)0x80000000u);
        if (!TryToInt64(roundingMode, out long wide))
        {
            return false;
        }

        if (wide is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)wide;
        return true;
    }

    /// <summary>Converts to a signed 16-bit integer using the given x87 rounding mode.</summary>
    /// <returns>False when the value is invalid or out of range.</returns>
    public bool TryToInt16(int roundingMode, out short value)
    {
        value = unchecked((short)0x8000);
        if (!TryToInt64(roundingMode, out long wide))
        {
            return false;
        }

        if (wide is < short.MinValue or > short.MaxValue)
        {
            return false;
        }

        value = (short)wide;
        return true;
    }

    /// <summary>Adds two values.</summary>
    public static Fpu80 Add(Fpu80 left, Fpu80 right)
    {
        return FromDouble(left.ToDouble() + right.ToDouble());
    }

    /// <summary>Subtracts two values.</summary>
    public static Fpu80 Subtract(Fpu80 left, Fpu80 right)
    {
        return FromDouble(left.ToDouble() - right.ToDouble());
    }

    /// <summary>Multiplies two values.</summary>
    public static Fpu80 Multiply(Fpu80 left, Fpu80 right)
    {
        return FromDouble(left.ToDouble() * right.ToDouble());
    }

    /// <summary>Divides two values.</summary>
    public static Fpu80 Divide(Fpu80 left, Fpu80 right)
    {
        return FromDouble(left.ToDouble() / right.ToDouble());
    }

    /// <summary>Computes the square root.</summary>
    public static Fpu80 SquareRoot(Fpu80 value)
    {
        return FromDouble(Math.Sqrt(value.ToDouble()));
    }

    /// <summary>Compares two values for raw equality.</summary>
    public static bool operator ==(Fpu80 left, Fpu80 right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares two values for raw inequality.</summary>
    public static bool operator !=(Fpu80 left, Fpu80 right)
    {
        return !left.Equals(right);
    }

    /// <summary>Determines whether this value's raw <see cref="SignExponent"/> and <see cref="Significand"/> bits equal those of <paramref name="other"/>.</summary>
    public bool Equals(Fpu80 other)
    {
        return SignExponent == other.SignExponent && Significand == other.Significand;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Fpu80 other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(SignExponent, Significand);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"0x{SignExponent:X4}:0x{Significand:X16} ({ToDouble()})";
    }

    private static bool RoundNearestUp(ulong significand, int right)
    {
        ulong guard = (significand >> (right - 1)) & 1UL;
        ulong lower = right >= 2 ? significand & ((1UL << (right - 1)) - 1) : 0;
        if (guard == 0)
        {
            return false;
        }

        return lower != 0 || ((significand >> right) & 1UL) != 0;
    }
}
