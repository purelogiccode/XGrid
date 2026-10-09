namespace XGrid.Cpu;

/// <summary>A decoded x86 instruction (plan section 8.3).</summary>
public readonly struct DecodedInstruction
{
    /// <summary>Gets the mnemonic.</summary>
    public Mnemonic Mnemonic { get; init; }

    /// <summary>Gets the first operand.</summary>
    public Operand Operand1 { get; init; }

    /// <summary>Gets the second operand.</summary>
    public Operand Operand2 { get; init; }

    /// <summary>Gets the third operand.</summary>
    public Operand Operand3 { get; init; }

    /// <summary>Gets the instruction length in bytes.</summary>
    public int Length { get; init; }

    /// <summary>Gets the effective operand size in bits.</summary>
    public int OperandSize { get; init; }

    /// <summary>Gets the effective address size in bits.</summary>
    public int AddressSize { get; init; }

    /// <summary>Gets the condition code for Jcc.</summary>
    public ConditionCode Condition { get; init; }

    /// <summary>Gets whether the instruction was decoded successfully.</summary>
    public bool IsValid => Mnemonic is not (Mnemonic.None or Mnemonic.Unsupported);

    /// <summary>Gets the first opcode byte.</summary>
    public byte Opcode { get; init; }

    /// <summary>Gets the REP-family prefix for string instructions.</summary>
    public RepPrefix Rep { get; init; }
}
