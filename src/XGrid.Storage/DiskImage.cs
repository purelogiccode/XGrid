using System;
using System.IO;

namespace XGrid.Storage;

/// <summary>A random-access 512-byte-sector block device.</summary>
public interface IDiskImage : IDisposable
{
    /// <summary>Gets the number of 512-byte sectors.</summary>
    uint SectorCount { get; }

    /// <summary>Reads one 512-byte sector at the given LBA.</summary>
    void ReadSector(uint lba, Span<byte> destination);

    /// <summary>Writes one 512-byte sector at the given LBA.</summary>
    void WriteSector(uint lba, ReadOnlySpan<byte> source);
}

/// <summary>A raw sector image backed by a file.</summary>
public sealed class RawDiskImage : IDiskImage
{
    /// <summary>Sector size in bytes.</summary>
    public const int SectorSize = 512;

    private readonly FileStream _stream;

    /// <summary>Opens a raw image file.</summary>
    public RawDiskImage(string path, bool writable = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _stream = new FileStream(
            path,
            FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.Read);
        SectorCount = (uint)(_stream.Length / SectorSize);
    }

    /// <inheritdoc />
    public uint SectorCount { get; }

    /// <inheritdoc />
    public void ReadSector(uint lba, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (destination.Length < SectorSize)
        {
            throw new ArgumentException("Destination must hold one sector.", nameof(destination));
        }

        _stream.Position = (long)lba * SectorSize;
        _stream.ReadExactly(destination[..SectorSize]);
    }

    /// <inheritdoc />
    public void WriteSector(uint lba, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (source.Length < SectorSize)
        {
            throw new ArgumentException("Source must hold one sector.", nameof(source));
        }

        _stream.Position = (long)lba * SectorSize;
        _stream.Write(source[..SectorSize]);
        _stream.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
    }
}
