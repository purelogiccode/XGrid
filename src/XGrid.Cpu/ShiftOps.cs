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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.PF | FlagBits.ZF | FlagBits.SF | FlagBits.OF);
        if (count < size && ((value >> (size - count)) & 1u) != 0)
        {
            flags |= FlagBits.CF;
        }

        result = count >= size ? 0u : (value << count) & mask;
        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.PF;
        }

        if (result == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.SF;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.CF) != 0)))
        {
            flags |= FlagBits.OF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.PF | FlagBits.ZF | FlagBits.SF | FlagBits.OF);
        if (count < size && ((value >> (count - 1)) & 1u) != 0)
        {
            flags |= FlagBits.CF;
        }

        if (count == 1 && (value & sign) != 0)
        {
            flags |= FlagBits.OF;
        }

        result = count >= size ? 0u : (value >> count) & mask;
        if (AluOps.ParityEven(result))
        {
            flags |= FlagBits.PF;
        }

        if (result == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.SF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.PF | FlagBits.ZF | FlagBits.SF | FlagBits.OF);
        if (count < size && ((value >> (count - 1)) & 1u) != 0)
        {
            flags |= FlagBits.CF;
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
            flags |= FlagBits.PF;
        }

        if (result == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.SF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.OF);
        int effective = count % size;
        result = effective == 0
            ? value
            : ((value << effective) | (value >> (size - effective))) & mask;

        if ((result & 1u) != 0)
        {
            flags |= FlagBits.CF;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.CF) != 0)))
        {
            flags |= FlagBits.OF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.OF);
        int effective = count % size;
        result = effective == 0
            ? value
            : ((value >> effective) | (value << (size - effective))) & mask;

        if ((result & sign) != 0)
        {
            flags |= FlagBits.CF;
        }

        if (count == 1)
        {
            bool top = (result & sign) != 0;
            bool next = (result & (sign >> 1)) != 0;
            if (top != next)
            {
                flags |= FlagBits.OF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.OF);
        bool carry = (eflags & FlagBits.CF) != 0;

        for (int i = 0; i < count; i++)
        {
            bool outBit = (value & sign) != 0;
            value = ((value << 1) & mask) | (carry ? 1u : 0u);
            carry = outBit;
        }

        result = value;
        if (carry)
        {
            flags |= FlagBits.CF;
        }

        if (count == 1 && (((result & sign) != 0) != ((flags & FlagBits.CF) != 0)))
        {
            flags |= FlagBits.OF;
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

        uint flags = eflags & ~(FlagBits.CF | FlagBits.OF);
        bool carry = (eflags & FlagBits.CF) != 0;

        for (int i = 0; i < count; i++)
        {
            bool outBit = (value & 1u) != 0;
            value = (value >> 1) | (carry ? sign : 0u);
            carry = outBit;
        }

        result = value;
        if (carry)
        {
            flags |= FlagBits.CF;
        }

        if (count == 1)
        {
            bool top = (result & sign) != 0;
            bool next = (result & (sign >> 1)) != 0;
            if (top != next)
            {
                flags |= FlagBits.OF;
            }
        }

        return flags;
    }
}
