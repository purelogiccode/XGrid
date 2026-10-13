namespace XGrid.Cpu;

/// <summary>
/// Host services the interpreter needs to fast-forward a verified idle loop:
/// bulk device-time advance plus the stop conditions (IRQ assertion and the
/// host's next scheduled deadline). Physical-address classification comes from
/// the host; the CPU watches polled RAM itself through its memory bus.
/// </summary>
public interface IIdleSkipHost
{
    /// <summary>Gets whether the interrupt controller has an unmasked pending request (side-effect free).</summary>
    bool IrqPendingAndEnabled { get; }

    /// <summary>Gets the number of cycles until the host's next scheduled event, or null when unknown.</summary>
    long? StepsUntilNextDeadline { get; }

    /// <summary>Returns true when the physical address is RAM-backed (reads have no device side effects).</summary>
    bool IsRam(uint physicalAddress);

    /// <summary>Advances devices (and host-visible time) by the given number of CPU cycles.</summary>
    void AdvanceIdle(ulong steps);
}
