using System;
using System.Buffers.Binary;
using System.IO;

namespace XGrid.Storage;

/// <summary>Read-only qcow2 (version 2 or 3) image reader.</summary>
public sealed class Qcow2DiskImage : IDiskImage
{
    private const ulong OffsetMask = 0x00FFFFFFFFFFFE00ul;
    private const ulong ZeroFlag = 0x01ul;

    private readonly FileStream _stream;
    private readonly int _clusterBits;
    private readonly uint _l2Entries;
    private readonly ulong[] _l1;

    /// <summary>Opens a qcow2 image for reading.</summary>
    public Qcow2DiskImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> header = stackalloc byte[104];
        _stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0x514649FB)
        {
            throw new InvalidDataException("Not a qcow2 image.");
        }

        int version = (int)BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (version is not (2 or 3))
        {
            throw new InvalidDataException($"Unsupported qcow2 version {version}.");
        }

        _clusterBits = (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
        if (_clusterBits is < 9 or > 21)
        {
            throw new InvalidDataException($"Unsupported qcow2 cluster bits {_clusterBits}.");
        }

        ulong virtualSize = BinaryPrimitives.ReadUInt64BigEndian(header[24..]);
        uint l1Size = BinaryPrimitives.ReadUInt32BigEndian(header[36..]);
        ulong l1Offset = BinaryPrimitives.ReadUInt64BigEndian(header[40..]);
        SectorCount = (uint)(virtualSize / 512);
        _l2Entries = 1u << (_clusterBits - 3);
        _l1 = new ulong[l1Size];

        _stream.Position = (long)l1Offset;
        Span<byte> entry = stackalloc byte[8];
        for (uint i = 0; i < l1Size; i++)
        {
            _stream.ReadExactly(entry);
            _l1[i] = BinaryPrimitives.ReadUInt64BigEndian(entry);
        }
    }

    /// <summary>Gets the number of 512-byte sectors from the qcow2 virtual size in the header.</summary>
    public uint SectorCount { get; }

    /// <summary>Walks the qcow2 L1/L2 tables for the cluster holding the LBA and reads one 512-byte sector; unallocated or zero-flagged clusters are reported as zeroes.</summary>
    /// <param name="lba">Logical block address, less than <see cref="SectorCount"/>.</param>
    /// <param name="destination">Destination holding at least one sector.</param>
    public void ReadSector(uint lba, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (destination.Length < 512)
        {
            throw new ArgumentException("Destination must hold one sector.", nameof(destination));
        }

        ulong offset = (ulong)lba * 512;
        ulong clusterIndex = offset >> _clusterBits;
        uint l1Index = (uint)(clusterIndex / _l2Entries);
        uint l2Index = (uint)(clusterIndex % _l2Entries);
        ulong l2Offset = _l1[l1Index] & OffsetMask;
        if (l2Offset == 0)
        {
            destination[..512].Clear();
            return;
        }

        Span<byte> entry = stackalloc byte[8];
        _stream.Position = (long)(l2Offset + (l2Index * 8));
        _stream.ReadExactly(entry);
        ulong l2Entry = BinaryPrimitives.ReadUInt64BigEndian(entry);
        if ((l2Entry & ZeroFlag) != 0)
        {
            destination[..512].Clear();
            return;
        }

        ulong clusterOffset = l2Entry & OffsetMask;
        if (clusterOffset == 0)
        {
            destination[..512].Clear();
            return;
        }

        _stream.Position = (long)(clusterOffset + (offset & ((1ul << _clusterBits) - 1)));
        _stream.ReadExactly(destination[..512]);
    }

    /// <summary>Always throws <see cref="NotSupportedException"/>; the qcow2 reader is read-only.</summary>
    public void WriteSector(uint lba, ReadOnlySpan<byte> source)
    {
        throw new NotSupportedException("The qcow2 reader is read-only.");
    }

    /// <summary>Closes the backing file stream.</summary>
    public void Dispose()
    {
        _stream.Dispose();
    }
}
