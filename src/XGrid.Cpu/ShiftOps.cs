namespace XGrid.Cpu;

/// <summary>Flag-accurate shift and rotate operations (Tier 4).</summary>
public static class ShiftOps
{
    private const int CountMask = 0x1F;

    /// <summary>Logical shift left. OF is defined only for count 1.</summary>
    public static uint Shl(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Pf | FlagBits.Zf | FlagBits.Sf | FlagBits.Of);
        if (count < size && ((value >> (size - count)) & 1u) != 0)
        {
            flags |= FlagBits.Cf;
        }

        result = count >= size ? 0u : (value << count) & mask;
        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.Pf;
        }

        if (result == 0)
        {
            flags |= FlagBits.Zf;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Sf;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.Cf) != 0)))
        {
            flags |= FlagBits.Of;
        }

        return flags;
    }

    /// <summary>Logical shift right. OF is defined only for count 1.</summary>
    public static uint Shr(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Pf | FlagBits.Zf | FlagBits.Sf | FlagBits.Of);
        if (count < size && ((value >> (count - 1)) & 1u) != 0)
        {
            flags |= FlagBits.Cf;
        }

        if (count == 1 && (value & sign) != 0)
        {
            flags |= FlagBits.Of;
        }

        result = count >= size ? 0u : (value >> count) & mask;
        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.Pf;
        }

        if (result == 0)
        {
            flags |= FlagBits.Zf;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Sf;
        }

        return flags;
    }

    /// <summary>Arithmetic shift right (sign fill). OF is always cleared.</summary>
    public static uint Sar(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Pf | FlagBits.Zf | FlagBits.Sf | FlagBits.Of);
        if (count < size && ((value >> (count - 1)) & 1u) != 0)
        {
            flags |= FlagBits.Cf;
        }

        int signedValue = size switch
        {
            8 => (sbyte)value,
            16 => (short)value,
            _ => (int)value,
        };

        result = count >= size
            ? (signedValue < 0 ? mask : 0u)
            : (uint)(signedValue >> count) & mask;

        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.Pf;
        }

        if (result == 0)
        {
            flags |= FlagBits.Zf;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Sf;
        }

        return flags;
    }

    /// <summary>Rotate left. OF is defined only for count 1.</summary>
    public static uint Rol(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        int effective = count % size;
        result = effective == 0
            ? value
            : ((value << effective) | (value >> (size - effective))) & mask;

        if ((result & 1u) != 0)
        {
            flags |= FlagBits.Cf;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.Cf) != 0)))
        {
            flags |= FlagBits.Of;
        }

        return flags;
    }

    /// <summary>Rotate right. OF is defined only for count 1.</summary>
    public static uint Ror(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        int effective = count % size;
        result = effective == 0
            ? value
            : ((value >> effective) | (value << (size - effective))) & mask;

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Cf;
        }

        if (count == 1)
        {
            bool top = (result & sign) != 0;
            bool next = (result & (sign >> 1)) != 0;
            if (top != next)
            {
                flags |= FlagBits.Of;
            }
        }

        return flags;
    }

    /// <summary>Rotate left through carry. OF is defined only for count 1.</summary>
    public static uint Rcl(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        bool carry = (eflags & FlagBits.Cf) != 0;

        for (int i = 0; i < count; i++)
        {
            bool outBit = (value & sign) != 0;
            value = ((value << 1) & mask) | (carry ? 1u : 0u);
            carry = outBit;
        }

        result = value;
        if (carry)
        {
            flags |= FlagBits.Cf;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.Cf) != 0)))
        {
            flags |= FlagBits.Of;
        }

        return flags;
    }

    /// <summary>Rotate right through carry. OF is defined only for count 1.</summary>
    public static uint Rcr(uint value, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        value &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = value;
            return eflags;
        }

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Of);
        bool carry = (eflags & FlagBits.Cf) != 0;

        for (int i = 0; i < count; i++)
        {
            bool outBit = (value & 1u) != 0;
            value = (value >> 1) | (carry ? sign : 0u);
            carry = outBit;
        }

        result = value;
        if (carry)
        {
            flags |= FlagBits.Cf;
        }

        if (count == 1)
        {
            bool top = (result & sign) != 0;
            bool next = (result & (sign >> 1)) != 0;
            if (top != next)
            {
                flags |= FlagBits.Of;
            }
        }

        return flags;
    }

    /// <summary>Double-precision shift left. Count is masked to 5 bits; 16-bit counts
    /// above 16 use Intel doubling. OF is defined only for count 1.</summary>
    public static uint Shld(uint destination, uint source, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        destination &= mask;
        source &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = destination;
            return eflags;
        }

        ulong wide = size == 16
            ? (ulong)destination | ((ulong)source << 16) | ((ulong)destination << 32)
            : ((ulong)destination << 32) | source;
        result = size == 16
            ? (uint)(((wide << count) >> 32) & mask)
            : (uint)((wide >> (32 - count)) & mask);

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Pf | FlagBits.Zf | FlagBits.Sf | FlagBits.Of);
        uint outBit = count <= size
            ? (destination >> (size - count)) & 1u
            : (uint)((wide >> (33 - count)) & 1u);
        if (outBit != 0)
        {
            flags |= FlagBits.Cf;
        }

        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.Pf;
        }

        if (result == 0)
        {
            flags |= FlagBits.Zf;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Sf;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.Cf) != 0)))
        {
            flags |= FlagBits.Of;
        }

        return flags;
    }

    /// <summary>Double-precision shift right. Count is masked to 5 bits; 16-bit counts
    /// above 16 use Intel doubling. OF is defined only for count 1.</summary>
    public static uint Shrd(uint destination, uint source, int count, int size, uint eflags, out uint result)
    {
        uint mask = AluOps.Mask(size);
        uint sign = AluOps.SignBit(size);
        destination &= mask;
        source &= mask;
        count &= CountMask;

        if (count == 0)
        {
            result = destination;
            return eflags;
        }

        ulong wide = size == 16
            ? (ulong)destination | ((ulong)source << 16) | ((ulong)destination << 32)
            : (ulong)destination | ((ulong)source << 32);
        result = (uint)((wide >> count) & mask);

        uint flags = eflags & ~(FlagBits.Cf | FlagBits.Pf | FlagBits.Zf | FlagBits.Sf | FlagBits.Of);
        uint outBit = count <= size
            ? (destination >> (count - 1)) & 1u
            : (uint)((wide >> (count - 1)) & 1u);
        if (outBit != 0)
        {
            flags |= FlagBits.Cf;
        }

        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.Pf;
        }

        if (result == 0)
        {
            flags |= FlagBits.Zf;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.Sf;
        }

        if (count == 1)
        {
            bool top = (result & sign) != 0;
            bool next = (result & (sign >> 1)) != 0;
            if (top != next)
            {
                flags |= FlagBits.Of;
            }
        }

        return flags;
    }
}
