namespace XGrid.Memory;

/// <summary>A memory-mapped device window. Addresses are absolute; sizes are in bytes (1, 2, 4).</summary>
public interface IMmioDevice
{
    /// <summary>Reads from the given absolute address.</summary>
    uint Read(uint address, int size);

    /// <summary>Writes to the given absolute address.</summary>
    void Write(uint address, int size, uint value);
}
