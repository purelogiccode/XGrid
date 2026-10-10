using System;
using System.Collections.Generic;
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

/// <summary>
/// Wraps a (possibly read-only) disk image with an in-memory copy-on-write
/// overlay: writes are retained in RAM and reads consult the overlay first, so
/// the backing image is never modified. Used for the HDD so guest filesystem
/// writes cannot corrupt the shared reference image.
/// </summary>
public sealed class OverlayDiskImage : IDiskImage
{
    private readonly IDiskImage _backing;
    private readonly Dictionary<uint, byte[]> _overlay = new();

    /// <summary>Creates an overlay over the given backing image (which it owns).</summary>
    public OverlayDiskImage(IDiskImage backing)
    {
        ArgumentNullException.ThrowIfNull(backing);
        _backing = backing;
    }

    /// <inheritdoc />
    public uint SectorCount => _backing.SectorCount;

    /// <summary>Gets the number of sectors modified in the overlay (diagnostics).</summary>
    public int ModifiedSectorCount => _overlay.Count;

    /// <inheritdoc />
    public void ReadSector(uint lba, Span<byte> destination)
    {
        if (_overlay.TryGetValue(lba, out byte[]? sector))
        {
            sector.AsSpan(0, 512).CopyTo(destination);
            return;
        }

        _backing.ReadSector(lba, destination);
    }

    /// <inheritdoc />
    public void WriteSector(uint lba, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (source.Length < 512)
        {
            throw new ArgumentException("Source must hold one sector.", nameof(source));
        }

        _overlay[lba] = source[..512].ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _backing.Dispose();
    }
}
