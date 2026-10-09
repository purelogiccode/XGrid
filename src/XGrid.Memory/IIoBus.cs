namespace XGrid.Memory;

/// <summary>x86 I/O port space interface (IN/OUT instructions).</summary>
public interface IIoBus
{
    /// <summary>Reads one byte from an I/O port.</summary>
    byte Read8(ushort port);

    /// <summary>Reads a 16-bit little-endian value from an I/O port.</summary>
    ushort Read16(ushort port);

    /// <summary>Reads a 32-bit little-endian value from an I/O port.</summary>
    uint Read32(ushort port);

    /// <summary>Writes one byte to an I/O port.</summary>
    void Write8(ushort port, byte value);

    /// <summary>Writes a 16-bit little-endian value to an I/O port.</summary>
    void Write16(ushort port, ushort value);

    /// <summary>Writes a 32-bit little-endian value to an I/O port.</summary>
    void Write32(ushort port, uint value);
}
