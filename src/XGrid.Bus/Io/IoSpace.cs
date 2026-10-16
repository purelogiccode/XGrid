using System;
using System.Collections.Generic;
using XGrid.Memory;

namespace XGrid.Bus.Io;

/// <summary>A device reachable through an I/O port window.</summary>
public interface IIoPortDevice
{
    /// <summary>Reads a value at an offset inside the device window.</summary>
    uint Read(ushort offset, int size);

    /// <summary>Writes a value at an offset inside the device window.</summary>
    void Write(ushort offset, int size, uint value);
}

/// <summary>Flat 64 KiB I/O port space with dynamic device windows (last registration wins).</summary>
public sealed class IoSpace : IIoBus
{
    private readonly List<Region> _regions = new();
    private readonly Dictionary<ushort, int> _portAccesses = new();

    /// <summary>Gets the number of accesses seen per port (diagnostics; includes unmapped ports).</summary>
    public IReadOnlyDictionary<ushort, int> PortAccessCounts => _portAccesses;

    /// <summary>Registers a device window. Returns a handle that removes it when disposed.</summary>
    public IDisposable Register(ushort start, ushort length, IIoPortDevice device)
    {
        if (length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "I/O window length must be non-zero.");
        }

        if ((uint)start + length > 0x10000u)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "I/O window exceeds the 64 KiB port space.");
        }

        var region = new Region(start, (ushort)(start + length - 1), device);
        _regions.Add(region);
        return new Registration(this, region);
    }

    /// <summary>Reads one byte from an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/> and returns 0xFF when no registered window covers the port.</summary>
    /// <param name="port">The port number to read.</param>
    public byte Read8(ushort port)
    {
        return (byte)Read(port, 8);
    }

    /// <summary>Reads a 16-bit little-endian value from an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/> and returns 0xFFFF when no registered window covers the port.</summary>
    /// <param name="port">The port number to read.</param>
    public ushort Read16(ushort port)
    {
        return (ushort)Read(port, 16);
    }

    /// <summary>Reads a 32-bit little-endian value from an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/> and returns 0xFFFFFFFF when no registered window covers the port.</summary>
    /// <param name="port">The port number to read.</param>
    public uint Read32(ushort port)
    {
        return Read(port, 32);
    }

    /// <summary>Writes one byte to an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/>; writes to ports no window covers are discarded.</summary>
    /// <param name="port">The port number to write.</param>
    /// <param name="value">The byte to write.</param>
    public void Write8(ushort port, byte value)
    {
        Write(port, 8, value);
    }

    /// <summary>Writes a 16-bit value to an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/>; writes to ports no window covers are discarded.</summary>
    /// <param name="port">The port number to write.</param>
    /// <param name="value">The 16-bit value to write.</param>
    public void Write16(ushort port, ushort value)
    {
        Write(port, 16, value);
    }

    /// <summary>Writes a 32-bit value to an I/O port: counts the access for
    /// <see cref="PortAccessCounts"/>; writes to ports no window covers are discarded.</summary>
    /// <param name="port">The port number to write.</param>
    /// <param name="value">The 32-bit value to write.</param>
    public void Write32(ushort port, uint value)
    {
        Write(port, 32, value);
    }

    private uint Read(ushort port, int size)
    {
        CountAccess(port);
        Region? region = Find(port);
        return region is null ? 0xFFFFFFFFu & Mask(size) : region.Device.Read((ushort)(port - region.Start), size);
    }

    private void CountAccess(ushort port)
    {
        _portAccesses.TryGetValue(port, out int count);
        _portAccesses[port] = count + 1;
    }

    private void Write(ushort port, int size, uint value)
    {
        CountAccess(port);
        Region? region = Find(port);
        region?.Device.Write((ushort)(port - region.Start), size, value);
    }

    private Region? Find(ushort port)
    {
        for (int i = _regions.Count - 1; i >= 0; i--)
        {
            if (port >= _regions[i].Start && port <= _regions[i].End)
            {
                return _regions[i];
            }
        }

        return null;
    }

    private static uint Mask(int size)
    {
        return size switch
        {
            8 => 0xFFu,
            16 => 0xFFFFu,
            _ => 0xFFFFFFFFu,
        };
    }

    private sealed record Region(ushort Start, ushort End, IIoPortDevice Device);

    private sealed class Registration : IDisposable
    {
        private readonly IoSpace _space;
        private Region? _region;

        public Registration(IoSpace space, Region region)
        {
            _space = space;
            _region = region;
        }

        public void Dispose()
        {
            if (_region is not null)
            {
                _space._regions.Remove(_region);
                _region = null;
            }
        }
    }
}
