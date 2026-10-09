namespace XGrid.Cpu;

/// <summary>EFLAGS bit positions (plan section 8.5).</summary>
public static class FlagBits
{
    /// <summary>Carry flag.</summary>
    public const uint CF = 1u << 0;

    /// <summary>Parity flag.</summary>
    public const uint PF = 1u << 2;

    /// <summary>Auxiliary carry flag.</summary>
    public const uint AF = 1u << 4;

    /// <summary>Zero flag.</summary>
    public const uint ZF = 1u << 6;

    /// <summary>Sign flag.</summary>
    public const uint SF = 1u << 7;

    /// <summary>Trap flag.</summary>
    public const uint TF = 1u << 8;

    /// <summary>Interrupt enable flag.</summary>
    public const uint IF = 1u << 9;

    /// <summary>Direction flag.</summary>
    public const uint DF = 1u << 10;

    /// <summary>Overflow flag.</summary>
    public const uint OF = 1u << 11;

    /// <summary>Always-one reserved bit.</summary>
    public const uint Reserved1 = 1u << 1;

    /// <summary>Flags affected by arithmetic and logic instructions.</summary>
    public const uint ArithmeticMask = CF | PF | AF | ZF | SF | OF;
}
