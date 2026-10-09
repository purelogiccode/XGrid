using System;
using System.Collections.Generic;
using XGrid.Core.Diagnostics;

namespace XGrid.Memory;

/// <summary>
/// Routes physical addresses to RAM, aliases, and memory-mapped devices. RAM accesses take a
/// direct fast path; everything else goes through the region table.
/// </summary>
public sealed class AddressSpace : IMemoryBus
{
    private readonly PhysicalMemory _ram;
    private readonly List<AddressRegion> _regions = [];
    private readonly Dictionary<uint, int> _unmappedCounts = new();

    /// <summary>Creates an address space over the given RAM.</summary>
    public AddressSpace(PhysicalMemory ram, UnmappedAccessPolicy unmappedPolicy = UnmappedAccessPolicy.Sentinel)
    {
        ArgumentNullException.ThrowIfNull(ram);
        _ram = ram;
        UnmappedPolicy = unmappedPolicy;
    }

    /// <summary>Gets the backing RAM.</summary>
    public PhysicalMemory Ram => _ram;

    /// <summary>Gets or sets how accesses outside all regions behave.</summary>
    public UnmappedAccessPolicy UnmappedPolicy { get; set; }

    /// <summary>Gets unmapped access counts keyed by 1 MB bucket (address >> 20). Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> UnmappedAccessCounts => _unmappedCounts;

    /// <summary>Gets or sets the start of the watched physical range (diagnostics; WatchEnd exclusive).</summary>
    public uint WatchStart { get; set; }

    /// <summary>Gets or sets the end of the watched physical range (diagnostics).</summary>
    public uint WatchEnd { get; set; }

    /// <summary>Raised when a watched address is written: (address, size, value).</summary>
    public event Action<uint, int, uint>? WatchHit;

    /// <summary>Gets the registered regions in registration order.</summary>
    public IReadOnlyList<AddressRegion> Regions => _regions;

    /// <summary>Registers a device window. Among overlapping regions the highest priority wins.</summary>
    public void AddRegion(string name, uint baseAddress, uint size, IMmioDevice device, int priority = 0)
    {
        _regions.Add(new AddressRegion(name, baseAddress, size, device, priority));
    }

    /// <summary>Returns whether the address maps directly to RAM.</summary>
    public bool IsRam(uint address)
    {
        return address < _ram.Size;
    }

    /// <summary>Finds the highest-priority region containing the address.</summary>
    public bool TryGetRegion(uint address, out AddressRegion? region)
    {
        AddressRegion? best = null;

        for (int i = 0; i < _regions.Count; i++)
        {
            AddressRegion candidate = _regions[i];
            if (!candidate.Contains(address))
            {
                continue;
            }

            if (best is null || candidate.Priority >= best.Priority)
            {
                best = candidate;
            }
        }

        region = best;
        return best is not null;
    }

    /// <summary>Reads one byte.</summary>
    public byte Read8(uint address)
    {
        if (address < _ram.Size)
        {
            return _ram.Read8(address);
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            return (byte)region!.Device.Read(address, 1);
        }

        return (byte)UnmappedRead(address, 1);
    }

    /// <summary>Reads a 16-bit little-endian value.</summary>
    public ushort Read16(uint address)
    {
        if ((ulong)address + 2 <= _ram.Size)
        {
            return _ram.Read16(address);
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            return (ushort)region!.Device.Read(address, 2);
        }

        return (ushort)UnmappedRead(address, 2);
    }

    /// <summary>Reads a 32-bit little-endian value.</summary>
    public uint Read32(uint address)
    {
        if ((ulong)address + 4 <= _ram.Size)
        {
            return _ram.Read32(address);
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            return region!.Device.Read(address, 4);
        }

        return UnmappedRead(address, 4);
    }

    /// <summary>Reads a 64-bit little-endian value as two 32-bit accesses.</summary>
    public ulong Read64(uint address)
    {
        if ((ulong)address + 8 <= _ram.Size)
        {
            return _ram.Read64(address);
        }

        uint low = Read32(address);
        uint high = Read32(address + 4);
        return low | ((ulong)high << 32);
    }

    /// <summary>Writes one byte.</summary>
    public void Write8(uint address, byte value)
    {
        CheckWatch(address, 1, value);
        if (address < _ram.Size)
        {
            _ram.Write8(address, value);
            return;
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            region!.Device.Write(address, 1, value);
            return;
        }

        UnmappedWrite(address, 1, value);
    }

    /// <summary>Writes a 16-bit little-endian value.</summary>
    public void Write16(uint address, ushort value)
    {
        CheckWatch(address, 2, value);
        if ((ulong)address + 2 <= _ram.Size)
        {
            _ram.Write16(address, value);
            return;
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            region!.Device.Write(address, 2, value);
            return;
        }

        UnmappedWrite(address, 2, value);
    }

    /// <summary>Writes a 32-bit little-endian value.</summary>
    public void Write32(uint address, uint value)
    {
        CheckWatch(address, 4, value);
        if ((ulong)address + 4 <= _ram.Size)
        {
            _ram.Write32(address, value);
            return;
        }

        if (TryGetRegion(address, out AddressRegion? region))
        {
            region!.Device.Write(address, 4, value);
            return;
        }

        UnmappedWrite(address, 4, value);
    }

    /// <summary>Writes a 64-bit little-endian value as two 32-bit accesses.</summary>
    public void Write64(uint address, ulong value)
    {
        if ((ulong)address + 8 <= _ram.Size)
        {
            _ram.Write64(address, value);
            return;
        }

        Write32(address, (uint)value);
        Write32(address + 4, (uint)(value >> 32));
    }

    /// <summary>Returns a writable span over RAM. Only valid for RAM addresses.</summary>
    public Span<byte> GetSpan(uint address, int length)
    {
        if ((ulong)address + (ulong)length > _ram.Size)
        {
            throw new MemoryAccessException(
                $"Span request outside RAM: address 0x{address:X8}, length {length}.");
        }

        return _ram.GetSpan(address, length);
    }

    private void CheckWatch(uint address, int size, uint value)
    {
        if (WatchEnd > WatchStart && address >= WatchStart && address < WatchEnd)
        {
            WatchHit?.Invoke(address, size, value);
        }
    }

    private void CountUnmapped(uint address)
    {
        uint bucket = address >> 20;
        _unmappedCounts.TryGetValue(bucket, out int count);
        _unmappedCounts[bucket] = count + 1;
    }

    private uint UnmappedRead(uint address, int size)
    {
        CountUnmapped(address);
        if (UnmappedPolicy == UnmappedAccessPolicy.Throw)
        {
            throw new MemoryAccessException($"Unmapped memory read at 0x{address:X8} ({size} bytes).");
        }

        if (XGridLog.IsEnabled(LogCategory.Mmio, LogLevel.Debug))
        {
            XGridLog.Write(LogCategory.Mmio, LogLevel.Debug, $"Unmapped read at 0x{address:X8} ({size} bytes)");
        }

        return size switch
        {
            1 => 0xFFu,
            2 => 0xFFFFu,
            _ => 0xFFFFFFFFu,
        };
    }

    private void UnmappedWrite(uint address, int size, uint value)
    {
        CountUnmapped(address);
        if (UnmappedPolicy == UnmappedAccessPolicy.Throw)
        {
            throw new MemoryAccessException(
                $"Unmapped memory write at 0x{address:X8} ({size} bytes) = 0x{value:X}.");
        }

        if (XGridLog.IsEnabled(LogCategory.Mmio, LogLevel.Debug))
        {
            XGridLog.Write(
                LogCategory.Mmio,
                LogLevel.Debug,
                $"Unmapped write at 0x{address:X8} ({size} bytes) = 0x{value:X}");
        }
    }
}
