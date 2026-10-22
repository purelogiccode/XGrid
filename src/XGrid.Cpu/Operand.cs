namespace XGrid.Cpu;

/// <summary>A decoded instruction operand.</summary>
public readonly struct Operand
{
    /// <summary>Gets the operand kind.</summary>
    public OperandKind Kind { get; private init; }

    /// <summary>Gets the register index (0-7) for register operands.</summary>
    public int RegisterIndex { get; private init; }

    /// <summary>Gets the operand size in bits (8, 16, or 32).</summary>
    public int Size { get; private init; }

    /// <summary>Gets the immediate value.</summary>
    public uint Immediate { get; private init; }

    /// <summary>Gets the memory operand description.</summary>
    public MemoryOperand Memory { get; private init; }

    /// <summary>Gets an empty operand.</summary>
    public static Operand None => default;

    /// <summary>Creates a register operand.</summary>
    public static Operand RegisterOperand(int registerIndex, int size)
    {
        return new Operand { Kind = OperandKind.Register, RegisterIndex = registerIndex, Size = size };
    }

    /// <summary>Creates an immediate operand.</summary>
    public static Operand ImmediateOperand(uint value, int size)
    {
        return new Operand { Kind = OperandKind.Immediate, Immediate = value, Size = size };
    }

    /// <summary>Creates a memory operand.</summary>
    public static Operand MemoryOperandValue(MemoryOperand memory, int size)
    {
        return new Operand { Kind = OperandKind.Memory, Memory = memory, Size = size };
    }
}
