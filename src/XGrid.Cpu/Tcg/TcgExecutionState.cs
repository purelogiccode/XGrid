namespace XGrid.Cpu.Tcg;

/// <summary>
/// The reusable execution state for <see cref="TcgInterpreter"/> (P1S4): the
/// guest register file, the segment bases, EFLAGS/EIP and the IR temp arrays
/// live across calls so the engine allocates nothing per instruction.
/// </summary>
public sealed class TcgExecutionState
{
    /// <summary>Gets the eight guest GPRs (seeded by the caller, updated in place).</summary>
    public uint[] Registers { get; } = new uint[8];

    /// <summary>Gets the six segment bases in <see cref="SegmentRegister"/> order.</summary>
    public uint[] Segments { get; } = new uint[6];

    /// <summary>Gets or sets EFLAGS.</summary>
    public uint Eflags { get; set; }

    /// <summary>Gets or sets EIP.</summary>
    public uint Eip { get; set; }

    internal uint[] Temps = [];

    internal ulong[] Wide = [];
}
