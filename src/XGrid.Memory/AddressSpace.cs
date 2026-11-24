using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Events;

namespace XGrid.Memory;

/// <summary>
/// Routes physical addresses to RAM, aliases, and memory-mapped devices. RAM accesses take a
/// direct fast path; everything else goes through the region table.
/// </summary>
public sealed class AddressSpace : IMemoryBus
{
    private readonly List<AddressRegion> _regions = [];
    private readonly Dictionary<uint, int> _unmappedCounts = [];
    private readonly Dictionary<uint, int> _unmappedPageReads = [];
    private readonly Dictionary<uint, int> _unmappedPageWrites = [];

    /// <summary>Creates an address space over the given RAM.</summary>
    public AddressSpace(PhysicalMemory ram, UnmappedAccessPolicy unmappedPolicy = UnmappedAccessPolicy.Sentinel)
    {
        ArgumentNullException.ThrowIfNull(ram);
        Ram = ram;
        UnmappedPolicy = unmappedPolicy;
    }

    /// <summary>Gets the backing RAM.</summary>
    public PhysicalMemory Ram { get; }

    /// <summary>Gets or sets how accesses outside all regions behave.</summary>
    public UnmappedAccessPolicy UnmappedPolicy { get; set; }

    /// <summary>Gets unmapped access counts keyed by 1 MB bucket (address >> 20). Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> UnmappedAccessCounts => _unmappedCounts;

    /// <summary>Gets unmapped read counts keyed by 4 KB page (address >> 12). Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> UnmappedPageReads => _unmappedPageReads;

    /// <summary>Gets unmapped write counts keyed by 4 KB page (address >> 12). Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> UnmappedPageWrites => _unmappedPageWrites;

    /// <summary>Gets the total number of unmapped accesses (reads and writes). Diagnostics.</summary>
    public long UnmappedAccessTotal { get; private set; }

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
        return address < Ram.Size;
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

    /// <summary>
    /// Returns whether the address is served by an <see cref="IImmutableImageDevice"/>
    /// window and reports that window's bounds. The decode cache uses this to
    /// cache instruction fetches from ROM/flash images (§237): those bytes only
    /// change on an explicit host action (the MCPX overlay being disabled, a
    /// snapshot load, or a new image), each of which flushes the cache, so no
    /// per-store invalidation is needed. Reads must be side-effect free — the
    /// marker interface guarantees that.
    /// </summary>
    /// <param name="address">The physical address to classify.</param>
    /// <param name="windowBase">Receives the window's first address.</param>
    /// <param name="windowSize">Receives the window's size in bytes.</param>
    /// <returns>True when the address lies in an immutable image window.</returns>
    public bool TryGetImmutableImageWindow(uint address, out uint windowBase, out uint windowSize)
    {
        for (int i = 0; i < _regions.Count; i++)
        {
            AddressRegion candidate = _regions[i];
            if (candidate.Device is not IImmutableImageDevice || !candidate.Contains(address))
            {
                continue;
            }

            if (!TryGetRegion(address, out AddressRegion? winner) || !ReferenceEquals(winner, candidate))
            {
                // A higher-priority window owns this address (e.g. the MCPX
                // overlay over the flash alias): not an image read.
                continue;
            }

            windowBase = candidate.BaseAddress;
            windowSize = candidate.Size;
            return true;
        }

        windowBase = 0;
        windowSize = 0;
        return false;
    }

    /// <summary>Reads one byte.</summary>
    public byte Read8(uint address)
    {
        return address < Ram.Size
            ? Ram.Read8(address)
            : TryGetRegion(address, out AddressRegion? region) ? (byte)region!.Device.Read(address, 1) : (byte)UnmappedRead(address, 1);
    }

    /// <summary>Reads a 16-bit little-endian value.</summary>
    public ushort Read16(uint address)
    {
        return (ulong)address + 2 <= Ram.Size
            ? Ram.Read16(address)
            : TryGetRegion(address, out AddressRegion? region) ? (ushort)region!.Device.Read(address, 2) : (ushort)UnmappedRead(address, 2);
    }

    /// <summary>Reads a 32-bit little-endian value.</summary>
    public uint Read32(uint address)
    {
        return (ulong)address + 4 <= Ram.Size
            ? Ram.Read32(address)
            : TryGetRegion(address, out AddressRegion? region) ? region!.Device.Read(address, 4) : UnmappedRead(address, 4);
    }

    /// <summary>Reads a 64-bit little-endian value as two 32-bit accesses.</summary>
    public ulong Read64(uint address)
    {
        if ((ulong)address + 8 <= Ram.Size)
        {
            return Ram.Read64(address);
        }

        uint low = Read32(address);
        uint high = Read32(address + 4);
        return low | ((ulong)high << 32);
    }

    /// <summary>Writes one byte.</summary>
    public void Write8(uint address, byte value)
    {
        CheckWatch(address, 1, value);
        if (address < Ram.Size)
        {
            Ram.Write8(address, value);
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
        if ((ulong)address + 2 <= Ram.Size)
        {
            Ram.Write16(address, value);
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
        if ((ulong)address + 4 <= Ram.Size)
        {
            Ram.Write32(address, value);
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
        if ((ulong)address + 8 <= Ram.Size)
        {
            Ram.Write64(address, value);
            return;
        }

        Write32(address, (uint)value);
        Write32(address + 4, (uint)(value >> 32));
    }

    /// <summary>Returns a writable span over RAM. Only valid for RAM addresses.</summary>
    public Span<byte> GetSpan(uint address, int length)
    {
        return address + (ulong)length > Ram.Size
            ? throw new MemoryAccessException(
                $"Span request outside RAM: address 0x{address:X8}, length {length}.")
            : Ram.GetSpan(address, length);
    }

    private void CheckWatch(uint address, int size, uint value)
    {
        if (WatchEnd > WatchStart && address >= WatchStart && address < WatchEnd)
        {
            WatchHit?.Invoke(address, size, value);
        }
    }

    private void CountUnmapped(uint address, bool isWrite)
    {
        uint bucket = address >> 20;
        _unmappedCounts.TryGetValue(bucket, out int count);
        _unmappedCounts[bucket] = count + 1;
        UnmappedAccessTotal++;

        Dictionary<uint, int> pages = isWrite ? _unmappedPageWrites : _unmappedPageReads;
        uint page = address >> 12;
        pages.TryGetValue(page, out int pageCount);
        pages[page] = pageCount + 1;
    }

    private uint UnmappedRead(uint address, int size)
    {
        CountUnmapped(address, isWrite: false);
        if (UnmappedPolicy == UnmappedAccessPolicy.Throw)
        {
            throw new MemoryAccessException($"Unmapped memory read at 0x{address:X8} ({size} bytes).");
        }

        if (Log.IsEnabled(LogEventLevel.Debug))
        {
            Log.Debug("Unmapped read at 0x{Address:X8} ({Size} bytes)", address, size);
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
        CountUnmapped(address, isWrite: true);
        if (UnmappedPolicy == UnmappedAccessPolicy.Throw)
        {
            throw new MemoryAccessException(
                $"Unmapped memory write at 0x{address:X8} ({size} bytes) = 0x{value:X}.");
        }

        if (Log.IsEnabled(LogEventLevel.Debug))
        {
            Log.Debug(
                "Unmapped write at 0x{Address:X8} ({Size} bytes) = 0x{Value:X}",
                address,
                size,
                value);
        }
    }
}
