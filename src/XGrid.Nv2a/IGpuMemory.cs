namespace XGrid.Nv2a;

/// <summary>Guest physical memory visible to the GPU (DMA pushbuffers, surfaces).</summary>
public interface IGpuMemory
{
    /// <summary>Reads a 32-bit little-endian word from guest physical memory.</summary>
    uint ReadPhysical(uint address);

    /// <summary>Writes a 32-bit little-endian word to guest physical memory.</summary>
    void WritePhysical(uint address, uint value);
}
