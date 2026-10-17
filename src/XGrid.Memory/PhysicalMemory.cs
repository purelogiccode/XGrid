using System;
using System.IO;
using XGrid.Core.Snapshots;

namespace XGrid.Memory;

/// <summary>Flat physical RAM. All accesses are little-endian and bounds-checked.</summary>
public sealed class PhysicalMemory : ISnapshotable, IPresentDirtyWatch
{
    /// <summary>Default retail Xbox RAM size (64 MB).</summary>
    public const uint DefaultSize = 64u * 1024 * 1024;

    private const uint RamStateVersion = 1;

    // Present-upload staleness (§132 (a*)): watched presented-surface spans
    // armed by the GL backend while its FBO is live, plus the dirty bit at
    // the single write choke that also covers the 0x80000000 alias, the RAM
    // mirror, and the 0xF0000000 write-combined view (an AddressSpace-level
    // watch misses those - §132 finding ii). The GPU-writer-identity
    // suppression counter exempts the NV2A software twin's own surface
    // writes (§132 finding i). Host-side only, never serialized.
    private const int PresentWatchMaxSpans = 4;

    private readonly byte[] _bytes;
    private readonly uint[] _presentWatchBase = new uint[PresentWatchMaxSpans];
    private readonly uint[] _presentWatchLength = new uint[PresentWatchMaxSpans];
    private int _presentWatchCount;
    private int _presentWatchNext;
    private int _gpuWriteSuppression;
    private bool _presentDirty;

    /// <summary>Allocates RAM of the given size in bytes.</summary>
    public PhysicalMemory(uint size = DefaultSize)
    {
        if (size == 0 || size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "RAM size must be between 1 byte and 2 GB.");
        }

        _bytes = new byte[size];
        Size = size;
    }

    /// <summary>Gets the RAM size in bytes.</summary>
    public uint Size { get; }

    /// <summary>Reads one byte.</summary>
    public byte Read8(uint address)
    {
        return _bytes[CheckedIndex(address, 1)];
    }

    /// <summary>Reads a 16-bit little-endian value.</summary>
    public ushort Read16(uint address)
    {
        return Endian.Read16(_bytes.AsSpan(CheckedIndex(address, 2), 2));
    }

    /// <summary>Reads a 32-bit little-endian value.</summary>
    public uint Read32(uint address)
    {
        return Endian.Read32(_bytes.AsSpan(CheckedIndex(address, 4), 4));
    }

    /// <summary>Reads a 64-bit little-endian value.</summary>
    public ulong Read64(uint address)
    {
        return Endian.Read64(_bytes.AsSpan(CheckedIndex(address, 8), 8));
    }

    /// <summary>Writes one byte.</summary>
    public void Write8(uint address, byte value)
    {
        _bytes[CheckedIndex(address, 1)] = value;
        NoteGuestWrite(address, 1);
    }

    /// <summary>Writes a 16-bit little-endian value.</summary>
    public void Write16(uint address, ushort value)
    {
        Endian.Write16(_bytes.AsSpan(CheckedIndex(address, 2), 2), value);
        NoteGuestWrite(address, 2);
    }

    /// <summary>Writes a 32-bit little-endian value.</summary>
    public void Write32(uint address, uint value)
    {
        Endian.Write32(_bytes.AsSpan(CheckedIndex(address, 4), 4), value);
        NoteGuestWrite(address, 4);
    }

    /// <summary>Writes a 64-bit little-endian value.</summary>
    public void Write64(uint address, ulong value)
    {
        Endian.Write64(_bytes.AsSpan(CheckedIndex(address, 8), 8), value);
        NoteGuestWrite(address, 8);
    }

    /// <summary>
    /// Bulk byte write (memory-path slice 2, §162): span copy plus the single
    /// present-dirty choke the N per-byte <see cref="Write8"/> calls would
    /// hit. The choke is range-based so one call covers the whole span
    /// exactly; callers still run the decode-cache page drop per page.
    /// </summary>
    public void WriteSpan(uint address, ReadOnlySpan<byte> source)
    {
        source.CopyTo(GetSpan(address, source.Length));
        NoteGuestWrite(address, source.Length);
    }

    /// <summary>Returns a writable span over RAM.</summary>
    public Span<byte> GetSpan(uint address, int length)
    {
        return _bytes.AsSpan(CheckedRange(address, length), length);
    }

    /// <summary>Returns a read-only span over RAM.</summary>
    public ReadOnlySpan<byte> GetReadOnlySpan(uint address, int length)
    {
        return _bytes.AsSpan(CheckedRange(address, length), length);
    }

    /// <summary>Zeroes all of RAM.</summary>
    public void Clear()
    {
        Array.Clear(_bytes);
        if (_presentWatchCount > 0)
        {
            // Fail-closed: wholesale RAM replacement while a backend FBO is
            // armed leaves the FBO stale until the next upload.
            _presentDirty = true;
        }
    }

    /// <summary>Gets a value indicating whether a non-suppressed writer touched a watched presented-surface span since the last clear (present-upload staleness, §132 (a*)).</summary>
    public bool PresentDirty => _presentDirty;

    /// <summary>Enters the GPU-writer-identity suppression scope: writes issued inside it never set the present dirty bit (NV2A software-twin surface writes, §132 finding i). Single-threaded by design; pair with <see cref="EndGpuWriteSuppression"/> in a finally.</summary>
    public void BeginGpuWriteSuppression()
    {
        _gpuWriteSuppression++;
    }

    /// <summary>Leaves the innermost GPU-writer-identity suppression scope.</summary>
    public void EndGpuWriteSuppression()
    {
        _gpuWriteSuppression--;
    }

    /// <summary>Adds the presented-surface span to the watched set; zero lengths are no-ops, duplicates are ignored, and once four spans are armed the oldest is evicted round-robin.</summary>
    public void ArmPresentWatch(uint address, uint length)
    {
        if (length == 0)
        {
            return;
        }

        for (int i = 0; i < _presentWatchCount; i++)
        {
            if (_presentWatchBase[i] == address && _presentWatchLength[i] == length)
            {
                return;
            }
        }

        if (_presentWatchCount < PresentWatchMaxSpans)
        {
            _presentWatchBase[_presentWatchCount] = address;
            _presentWatchLength[_presentWatchCount] = length;
            _presentWatchCount++;
            return;
        }

        // More than four presented surfaces in flight: round-robin eviction
        // keeps the most recent spans watched (double-buffered apps fit in
        // two; beyond four spans a CPU writer to an evaded span is the
        // documented §135 under-watch corner - fail-closed elsewhere).
        _presentWatchBase[_presentWatchNext] = address;
        _presentWatchLength[_presentWatchNext] = length;
        _presentWatchNext = (_presentWatchNext + 1) % PresentWatchMaxSpans;
    }

    /// <summary>Clears the watched-span set and the round-robin cursor; the present dirty bit is not cleared (fail-closed survives a disarm).</summary>
    public void DisarmPresentWatch()
    {
        _presentWatchCount = 0;
        _presentWatchNext = 0;
    }

    /// <summary>Marks the presented surface dirty (fail-closed: snapshot load or reset while a backend FBO may still hold prior content).</summary>
    public void MarkPresentDirty()
    {
        _presentDirty = true;
    }

    /// <summary>Clears the present dirty bit, to be called only after an upload made the backend FBO authoritative from guest RAM.</summary>
    public void ClearPresentDirty()
    {
        _presentDirty = false;
    }

    /// <summary>Saves the full RAM contents (V1 snapshot).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(RamStateVersion);
        writer.Write(Size);
        writer.Write(_bytes);
    }

    /// <summary>Restores the full RAM contents (V1 snapshot).</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        uint version = reader.ReadUInt32();
        if (version != RamStateVersion)
        {
            throw new InvalidDataException($"Unsupported RAM state version {version} (expected {RamStateVersion}).");
        }

        uint size = reader.ReadUInt32();
        if (size != Size)
        {
            throw new InvalidDataException($"Snapshot RAM size {size} does not match machine RAM size {Size}.");
        }

        byte[] bytes = reader.ReadBytes((int)size);
        if (bytes.Length != Size)
        {
            throw new InvalidDataException($"Snapshot RAM payload truncated ({bytes.Length} of {Size} bytes).");
        }

        Buffer.BlockCopy(bytes, 0, _bytes, 0, (int)Size);
        if (_presentWatchCount > 0)
        {
            // Fail-closed (§132 (a*)): snapshot load replaces RAM wholesale;
            // a backend FBO armed over the pre-load surfaces is stale until
            // the next upload. The bit itself is not serialized (backend is
            // excluded from snapshots - §101).
            _presentDirty = true;
        }
    }

    private void NoteGuestWrite(uint address, int length)
    {
        // Present-upload staleness choke (§132 (a*)): a write inside a
        // watched presented-surface span dirties the frame unless it came
        // from the suppressed NV2A-twin adapters. Disarmed (count == 0, the
        // sw default and pre-first-flip case) this is one predicted branch.
        if (_presentWatchCount == 0 || _gpuWriteSuppression != 0)
        {
            return;
        }

        ulong start = address;
        ulong end = start + (ulong)length;
        for (int i = 0; i < _presentWatchCount; i++)
        {
            ulong watchedStart = _presentWatchBase[i];
            ulong watchedEnd = watchedStart + _presentWatchLength[i];
            if (start < watchedEnd && watchedStart < end)
            {
                _presentDirty = true;
                return;
            }
        }
    }

    private int CheckedIndex(uint address, int width)
    {
        return CheckedRange(address, width);
    }

    private int CheckedRange(uint address, int length)
    {
        if (length < 0 || (ulong)address + (ulong)length > Size)
        {
            throw new MemoryAccessException(
                $"RAM access out of range: address 0x{address:X8}, length {length}, size 0x{Size:X8}.");
        }

        return (int)address;
    }
}
