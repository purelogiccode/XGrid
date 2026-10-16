namespace XGrid.Cpu;

/// <summary>Condition codes used by Jcc and (later) SETcc/CMOVcc.</summary>
public enum ConditionCode
{
    /// <summary>Taken when the overflow flag is set.</summary>
    Overflow = 0,

    /// <summary>Taken when the overflow flag is clear.</summary>
    NotOverflow = 1,

    /// <summary>Taken when below (carry set).</summary>
    Below = 2,

    /// <summary>Taken when above or equal (carry clear).</summary>
    AboveOrEqual = 3,

    /// <summary>Taken when equal (zero flag set).</summary>
    Equal = 4,

    /// <summary>Taken when not equal (zero flag clear).</summary>
    NotEqual = 5,

    /// <summary>Taken when below or equal (carry or zero flag set).</summary>
    BelowOrEqual = 6,

    /// <summary>Taken when above (carry and zero flags clear).</summary>
    Above = 7,

    /// <summary>Taken when the sign flag is set.</summary>
    Sign = 8,

    /// <summary>Taken when the sign flag is clear.</summary>
    NotSign = 9,

    /// <summary>Taken when the parity flag is set.</summary>
    Parity = 10,

    /// <summary>Taken when the parity flag is clear.</summary>
    NotParity = 11,

    /// <summary>Taken when signed less-than (sign differs from overflow).</summary>
    Less = 12,

    /// <summary>Taken when signed greater-than-or-equal (sign equals overflow).</summary>
    GreaterOrEqual = 13,

    /// <summary>Taken when signed less-than-or-equal.</summary>
    LessOrEqual = 14,

    /// <summary>Taken when signed greater-than.</summary>
    Greater = 15,
}
