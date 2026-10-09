namespace XGrid.Cpu;

/// <summary>Exact flag computation for the implemented ALU operations.</summary>
public static class AluOps
{
    /// <summary>Returns the value mask for an operand size (8, 16, or 32).</summary>
    public static uint Mask(int size)
    {
        return size == 32 ? 0xFFFFFFFFu : (1u << size) - 1u;
    }

    /// <summary>Returns the sign bit for an operand size.</summary>
    public static uint SignBit(int size)
    {
        return 1u << (size - 1);
    }

    /// <summary>Returns whether the low byte has an even number of set bits.</summary>
    public static bool ParityEven(uint value)
    {
        uint v = value & 0xFFu;
        v ^= v >> 4;
        v ^= v >> 2;
        v ^= v >> 1;
        return (v & 1u) == 0;
    }

    /// <summary>Adds with carry and computes CF/PF/AF/ZF/SF/OF.</summary>
    public static uint Add(uint a, uint b, uint carryIn, int size, uint eflags, out uint result)
    {
        uint mask = Mask(size);
        uint sign = SignBit(size);
        a &= mask;
        b &= mask;
        carryIn &= 1u;

        ulong sum = (ulong)a + b + carryIn;
        result = (uint)sum & mask;

        uint flags = eflags & ~FlagBits.ArithmeticMask;
        if (sum > mask)
        {
            flags |= FlagBits.CF;
        }

        if (ParityEven(result))
        {
            flags |= FlagBits.PF;
        }

        if (((a ^ b ^ result) & 0x10u) != 0)
        {
            flags |= FlagBits.AF;
        }

        if (result == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.SF;
        }

        if (((a ^ result) & (b ^ result) & sign) != 0)
        {
            flags |= FlagBits.OF;
        }

        return flags;
    }

    /// <summary>Subtracts with borrow and computes CF/PF/AF/ZF/SF/OF.</summary>
    public static uint Sub(uint a, uint b, uint borrowIn, int size, uint eflags, out uint result)
    {
        uint mask = Mask(size);
        uint sign = SignBit(size);
        a &= mask;
        b &= mask;
        borrowIn &= 1u;

        long difference = (long)a - b - borrowIn;
        result = (uint)difference & mask;

        uint flags = eflags & ~FlagBits.ArithmeticMask;
        if (difference < 0)
        {
            flags |= FlagBits.CF;
        }

        if (ParityEven(result))
        {
            flags |= FlagBits.PF;
        }

        if (((a ^ b ^ result) & 0x10u) != 0)
        {
            flags |= FlagBits.AF;
        }

        if (result == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((result & sign) != 0)
        {
            flags |= FlagBits.SF;
        }

        if (((a ^ b) & (a ^ result) & sign) != 0)
        {
            flags |= FlagBits.OF;
        }

        return flags;
    }

    /// <summary>Computes flags for AND/OR/XOR/TEST: CF=OF=AF=0, PF/ZF/SF from the result.</summary>
    public static uint Logical(uint value, int size, uint eflags)
    {
        uint flags = eflags & ~(FlagBits.CF | FlagBits.PF | FlagBits.AF | FlagBits.ZF | FlagBits.SF | FlagBits.OF);
        if (ParityEven(value))
        {
            flags |= FlagBits.PF;
        }

        if (value == 0)
        {
            flags |= FlagBits.ZF;
        }

        if ((value & SignBit(size)) != 0)
        {
            flags |= FlagBits.SF;
        }

        return flags;
    }

    /// <summary>Increments, preserving CF.</summary>
    public static uint Inc(uint a, int size, uint eflags, out uint result)
    {
        uint flags = Add(a, 1, 0, size, eflags, out result);
        return (flags & ~FlagBits.CF) | (eflags & FlagBits.CF);
    }

    /// <summary>Decrements, preserving CF.</summary>
    public static uint Dec(uint a, int size, uint eflags, out uint result)
    {
        uint flags = Sub(a, 1, 0, size, eflags, out result);
        return (flags & ~FlagBits.CF) | (eflags & FlagBits.CF);
    }

    /// <summary>Negates (two's complement); CF is set when the operand is non-zero.</summary>
    public static uint Neg(uint a, int size, uint eflags, out uint result)
    {
        return Sub(0, a, 0, size, eflags, out result);
    }
}
