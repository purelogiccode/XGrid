namespace XGrid.Cpu;

/// <summary>Effective-address description for a memory operand.</summary>
public readonly struct MemoryOperand
{
    /// <summary>Gets the base register.</summary>
    public Gpr Base { get; init; }

    /// <summary>Gets whether a base register is present.</summary>
    public bool HasBase { get; init; }

    /// <summary>Gets the index register.</summary>
    public Gpr Index { get; init; }

    /// <summary>Gets whether an index register is present.</summary>
    public bool HasIndex { get; init; }

    /// <summary>Gets the index scale (1, 2, 4, or 8).</summary>
    public int Scale { get; init; }

    /// <summary>Gets the displacement.</summary>
    public uint Displacement { get; init; }

    /// <summary>Gets the segment override prefix, if any.</summary>
    public SegmentRegister? SegmentOverride { get; init; }

    /// <summary>Gets whether 16-bit addressing is used.</summary>
    public bool Is16Bit { get; init; }
}
