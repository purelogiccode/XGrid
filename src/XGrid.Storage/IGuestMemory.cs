namespace XGrid.Storage;

/// <summary>Minimal guest-physical memory access for bus-master DMA (PRDT walk and data).</summary>
public interface IGuestMemory
{
    /// <summary>Reads one byte at a guest physical address.</summary>
    byte ReadPhysical(uint address);

    /// <summary>Writes one byte at a guest physical address.</summary>
    void WritePhysical(uint address, byte value);
}
