namespace XGrid.Cpu;

/// <summary>How a decoded operand is addressed.</summary>
public enum OperandKind
{
    /// <summary>No operand.</summary>
    None = 0,

    /// <summary>A general-purpose register.</summary>
    Register = 1,

    /// <summary>A memory reference.</summary>
    Memory = 2,

    /// <summary>An immediate value.</summary>
    Immediate = 3,
}
