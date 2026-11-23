namespace XGrid.Memory;

/// <summary>
/// Guest-write stamping for content-identity caches (XFP4 texture cache):
/// a page-granular generation sum over a guest range detects whether its
/// bytes changed without rescanning them. The generations live at the
/// <see cref="PhysicalMemory"/> write choke (every guest store, device DMA,
/// and the IL/JIT store path), are bumped inline by the raw emitter's store
/// guard, and are bumped once per software surface write or clear by the
/// NV2A, so a cache that hashed a texture only re-hashes it when a writer
/// actually touched its pages. Host-side only; never serialized.
/// </summary>
public interface IGuestWriteStamp
{
    /// <summary>
    /// Gets the generation sum for the guest range [address, address + length).
    /// Stable while the range is unchanged; any bump of one of its pages
    /// changes it.
    /// </summary>
    /// <param name="address">Guest-physical range start.</param>
    /// <param name="length">Range length in bytes.</param>
    /// <returns>The range's page-generation sum.</returns>
    ulong GetWriteStamp(uint address, uint length);

    /// <summary>
    /// Bumps the generations covering a range written outside the ordinary
    /// guest-store choke (a software draw's surface, a clear rectangle, or a
    /// direct RAM lane). Content caches only need one bump per write event.
    /// </summary>
    /// <param name="address">Guest-physical range start.</param>
    /// <param name="length">Range length in bytes.</param>
    void NoteDirectWrite(uint address, uint length);
}
