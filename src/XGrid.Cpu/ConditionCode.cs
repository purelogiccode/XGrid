namespace XGrid.Cpu;

/// <summary>Condition codes used by Jcc and (later) SETcc/CMOVcc.</summary>
public enum ConditionCode
{
    Overflow = 0,
    NotOverflow = 1,
    Below = 2,
    AboveOrEqual = 3,
    Equal = 4,
    NotEqual = 5,
    BelowOrEqual = 6,
    Above = 7,
    Sign = 8,
    NotSign = 9,
    Parity = 10,
    NotParity = 11,
    Less = 12,
    GreaterOrEqual = 13,
    LessOrEqual = 14,
    Greater = 15,
}
