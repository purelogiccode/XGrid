using System;
using System.IO;
using XGrid.Core;

namespace XGrid.Memory;

/// <summary>Flat physical RAM. All accesses are little-endian and bounds-checked.</summary>
public sealed class PhysicalMemory : ISnapshotable
{
    /// <summary>Default retail Xbox RAM size (64 MB).</summary>
    public const uint DefaultSize = 64u * 1024 * 1024;

    private const uint RamStateVersion = 1;

    private readonly byte[] _bytes;

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
    }

    /// <summary>Writes a 16-bit little-endian value.</summary>
    public void Write16(uint address, ushort value)
    {
        Endian.Write16(_bytes.AsSpan(CheckedIndex(address, 2), 2), value);
    }

    /// <summary>Writes a 32-bit little-endian value.</summary>
    public void Write32(uint address, uint value)
    {
        Endian.Write32(_bytes.AsSpan(CheckedIndex(address, 4), 4), value);
    }

    /// <summary>Writes a 64-bit little-endian value.</summary>
    public void Write64(uint address, ulong value)
    {
        Endian.Write64(_bytes.AsSpan(CheckedIndex(address, 8), 8), value);
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
