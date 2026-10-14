namespace XGrid.Memory;

/// <summary>
/// Present-upload staleness channel (research notes §132 (a*)) between the
/// GL PGRAPH backend and the external dirty bit in <see cref="PhysicalMemory"/>.
/// The bit lives at the <c>PhysicalMemory.Write8/16/32/64</c> choke (the
/// single writer path that also covers the 0x80000000 alias, the RAM mirror,
/// and the 0xF0000000 write-combined view); the backend arms the watched
/// presented-surface spans when its FBO is live, reads <see cref="PresentDirty"/>
/// at flip (dirty → upload from guest RAM, clean → serve the FBO), and clears
/// it only after an upload made the FBO authoritative. Fail-closed: snapshot
/// load marks the bit dirty; <see cref="DisarmPresentWatch"/> never clears it.
/// The bit is host-side only and is never serialized (no .xgs change).
/// </summary>
public interface IPresentDirtyWatch
{
    /// <summary>Gets a value indicating whether a non-suppressed writer touched a watched presented-surface span since the last clear.</summary>
    bool PresentDirty { get; }

    /// <summary>Adds the presented-surface span [address, address + length) to the watched set. Idempotent; a zero length is a no-op.</summary>
    void ArmPresentWatch(uint address, uint length);

    /// <summary>Clears the watched set. Does not clear the dirty bit (fail-closed survives a disarm).</summary>
    void DisarmPresentWatch();

    /// <summary>Marks the presented surface dirty (fail-closed: snapshot load / reset while a backend FBO may still hold prior content).</summary>
    void MarkPresentDirty();

    /// <summary>Clears the dirty bit (only after the FBO was made authoritative from guest RAM by an upload).</summary>
    void ClearPresentDirty();
}
