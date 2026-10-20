namespace XGrid.Nv2a;

/// <summary>
/// Optional fast lane for the software rasterizer's per-pixel RAM traffic
/// (rasterizer inventory O2). The plain <see cref="IGpuMemory"/> adapter path
/// pays an interface dispatch, two address-space bounds layers, a region scan
/// fallback and a per-write GPU-writer suppression pair; a draw can instead
/// open one batch and use the direct word accessors for RAM addresses.
///
/// <para><b>Contract.</b> <see cref="TryReadPhysical"/> and
/// <see cref="TryWritePhysical"/> return false for any address the adapter
/// cannot serve exactly (non-RAM, a diagnostic bus-watch overlap, a write
/// outside an open batch); the caller must then fall back to
/// <see cref="IGpuMemory"/>. A batch opens the GPU-writer suppression scope
/// and resets the decode-page memo: inside a draw the CPU does not run, so a
/// page needs at most one decode-cache drop, and the suppression counter must
/// not be re-balanced per pixel.</para>
/// </summary>
public interface IGpuMemoryFast
{
    /// <summary>Opens a draw-wide GPU-write batch (suppression + page memo).</summary>
    void BeginGpuBatch();

    /// <summary>Closes the batch opened by <see cref="BeginGpuBatch"/>.</summary>
    void EndGpuBatch();

    /// <summary>Tries a direct 32-bit RAM read; false means use the legacy adapter.</summary>
    bool TryReadPhysical(uint address, out uint value);

    /// <summary>
    /// Exposes the RAM backing store so a fill can index it directly
    /// (<c>MemoryMarshal.GetArrayDataReference</c>) after one bounds check per
    /// access instead of one interface dispatch. <paramref name="size"/> is the
    /// same RAM size <see cref="TryReadPhysical"/> bounds-checks against, so a
    /// caller can reproduce that check exactly. False means no direct view.
    /// </summary>
    bool TryGetRamBytes(out byte[] bytes, out uint size);

    /// <summary>Tries a direct 32-bit RAM write inside a batch; false means use the legacy adapter.</summary>
    bool TryWritePhysical(uint address, uint value);
}
