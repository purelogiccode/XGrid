namespace XGrid.Cpu.Tcg;

/// <summary>The engine callback that runs between translated-block members (P1S4).</summary>
public interface ITcgPollObserver
{
    /// <summary>Runs the per-instruction TSC tick and pending-interrupt check.</summary>
    /// <param name="state">The live execution state (the observer may publish it before a delivery).</param>
    /// <param name="eip">The next member's guest EIP (the interrupt return address).</param>
    /// <returns>True when an interrupt was delivered and the block must stop.</returns>
    bool BeforeMember(TcgExecutionState state, uint eip);
}

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

    /// <summary>Gets or sets the mid-block poll observer (null for single-instruction programs).</summary>
    public ITcgPollObserver? Poll { get; set; }

    /// <summary>Gets the number of members completed when a poll stopped the block.</summary>
    public int MembersDone { get; internal set; }

    /// <summary>Gets whether a mid-block poll stopped the block.</summary>
    public bool Interrupted { get; internal set; }

    /// <summary>Gets or sets the maximum members this block may execute (the caller's budget).</summary>
    public int MemberLimit { get; set; } = int.MaxValue;

    /// <summary>Gets whether the member limit ended the block before its terminator.</summary>
    public bool LimitReached { get; internal set; }

    internal uint[] Temps = [];

    internal ulong[] Wide = [];
}
