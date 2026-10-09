namespace XGrid.Memory;

/// <summary>Minimal CPU-facing memory interface (byte/word/dword access).</summary>
public interface IMemoryBus
{
    /// <summary>Reads one byte.</summary>
    byte Read8(uint address);

    /// <summary>Reads a 16-bit little-endian value.</summary>
    ushort Read16(uint address);

    /// <summary>Reads a 32-bit little-endian value.</summary>
    uint Read32(uint address);

    /// <summary>Writes one byte.</summary>
    void Write8(uint address, byte value);

    /// <summary>Writes a 16-bit little-endian value.</summary>
    void Write16(uint address, ushort value);

    /// <summary>Writes a 32-bit little-endian value.</summary>
    void Write32(uint address, uint value);
}
