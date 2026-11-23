namespace XGrid.Cpu;

/// <summary>EFLAGS bit positions (plan section 8.5).</summary>
public static class FlagBits
{
    /// <summary>Carry flag.</summary>
    public const uint Cf = 1u << 0;

    /// <summary>Parity flag.</summary>
    public const uint Pf = 1u << 2;

    /// <summary>Auxiliary carry flag.</summary>
    public const uint Af = 1u << 4;

    /// <summary>Zero flag.</summary>
    public const uint Zf = 1u << 6;

    /// <summary>Sign flag.</summary>
    public const uint Sf = 1u << 7;

    /// <summary>Trap flag.</summary>
    public const uint Tf = 1u << 8;

    /// <summary>Interrupt enable flag.</summary>
    public const uint If = 1u << 9;

    /// <summary>Direction flag.</summary>
    public const uint Df = 1u << 10;

    /// <summary>Overflow flag.</summary>
    public const uint Of = 1u << 11;

    /// <summary>Always-one reserved bit.</summary>
    public const uint Reserved1 = 1u << 1;

    /// <summary>Flags affected by arithmetic and logic instructions.</summary>
    public const uint ArithmeticMask = Cf | Pf | Af | Zf | Sf | Of;

    /// <summary>
    /// The condition-code bits SAHF loads from AH and LAHF stores into AH
    /// (the reference's CC_S|CC_Z|CC_A|CC_P|CC_C). OF and every non-condition
    /// bit are not part of the transfer.
    /// </summary>
    public const uint ConditionMask = Cf | Pf | Af | Zf | Sf;
}
