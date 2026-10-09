using System;

namespace XGrid.Memory;

/// <summary>Maps a window of addresses onto RAM, wrapping within RAM (Xbox RAM mirroring).</summary>
public sealed class RamAliasDevice : IMmioDevice
{
    private readonly PhysicalMemory _ram;
    private readonly uint _baseAddress;
    private readonly uint _mask;

    /// <summary>Creates an alias over the given window. RAM size must be a power of two.</summary>
    public RamAliasDevice(PhysicalMemory ram, uint baseAddress)
    {
        ArgumentNullException.ThrowIfNull(ram);

        _ram = ram;
        _baseAddress = baseAddress;
        _mask = ram.Size - 1;

        if ((ram.Size & _mask) != 0)
        {
            throw new ArgumentException("RAM size must be a power of two for mirroring.", nameof(ram));
        }
    }

    /// <inheritdoc />
    public uint Read(uint address, int size)
    {
        uint mapped = Map(address);
        return size switch
        {
            1 => _ram.Read8(mapped),
            2 => _ram.Read16(mapped),
            4 => _ram.Read32(mapped),
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported access size."),
        };
    }

    /// <inheritdoc />
    public void Write(uint address, int size, uint value)
    {
        uint mapped = Map(address);
        switch (size)
        {
            case 1:
                _ram.Write8(mapped, (byte)value);
                break;
            case 2:
                _ram.Write16(mapped, (ushort)value);
                break;
            case 4:
                _ram.Write32(mapped, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported access size.");
        }
    }

    private uint Map(uint address)
    {
        return (address - _baseAddress) & _mask;
    }
}
