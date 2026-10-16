using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Snapshots;

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

    /// <summary>Gets the number of 512-byte sectors derived from the backing file's length.</summary>
    public uint SectorCount { get; }

    /// <summary>Reads one 512-byte sector from the backing file at byte offset <c>lba * 512</c>.</summary>
    /// <param name="lba">Logical block address, less than <see cref="SectorCount"/>.</param>
    /// <param name="destination">Destination holding at least one sector.</param>
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

    /// <summary>Writes one 512-byte sector to the backing file at byte offset <c>lba * 512</c> and flushes the stream.</summary>
    /// <param name="lba">Logical block address, less than <see cref="SectorCount"/>.</param>
    /// <param name="source">Source holding at least one sector.</param>
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

    /// <summary>Closes the backing file stream.</summary>
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
public sealed class OverlayDiskImage : IDiskImage, ISnapshotable
{
    private readonly IDiskImage _backing;
    private readonly Dictionary<uint, byte[]> _overlay = new();
    private readonly List<uint> _writeLog = new();

    /// <summary>Creates an overlay over the given backing image (which it owns).</summary>
    public OverlayDiskImage(IDiskImage backing)
    {
        ArgumentNullException.ThrowIfNull(backing);
        _backing = backing;
    }

    /// <summary>Gets the number of 512-byte sectors from the backing image.</summary>
    public uint SectorCount => _backing.SectorCount;

    /// <summary>Gets the number of sectors modified in the overlay (diagnostics).</summary>
    public int ModifiedSectorCount => _overlay.Count;

    /// <summary>Gets the total number of sector writes issued (including repeats).</summary>
    public int WriteCount => _writeLog.Count;

    /// <summary>
    /// Gets every sector write LBA in issue order, including repeats of the same
    /// LBA. Used to reconstruct guest write patterns (e.g. journal updates).
    /// </summary>
    public IReadOnlyList<uint> WriteLog => _writeLog;

    /// <summary>Returns the modified sector LBAs in ascending order.</summary>
    public uint[] GetModifiedSectors()
    {
        uint[] sectors = new uint[_overlay.Count];
        _overlay.Keys.CopyTo(sectors, 0);
        Array.Sort(sectors);
        return sectors;
    }

    /// <summary>Reads one 512-byte sector, serving it from the in-RAM overlay when the LBA was written, otherwise from the backing image.</summary>
    /// <param name="lba">Logical block address, less than <see cref="SectorCount"/>.</param>
    /// <param name="destination">Destination holding at least one sector.</param>
    public void ReadSector(uint lba, Span<byte> destination)
    {
        if (_overlay.TryGetValue(lba, out byte[]? sector))
        {
            sector.AsSpan(0, 512).CopyTo(destination);
            return;
        }

        _backing.ReadSector(lba, destination);
    }

    /// <summary>Copies one 512-byte sector into the in-RAM overlay (the backing image is never modified) and appends the LBA to the write log.</summary>
    /// <param name="lba">Logical block address, less than <see cref="SectorCount"/>.</param>
    /// <param name="source">Source holding at least one sector.</param>
    public void WriteSector(uint lba, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (source.Length < 512)
        {
            throw new ArgumentException("Source must hold one sector.", nameof(source));
        }

        _overlay[lba] = source[..512].ToArray();
        _writeLog.Add(lba);
    }

    /// <summary>Disposes the owned backing image.</summary>
    public void Dispose()
    {
        _backing.Dispose();
    }

    /// <summary>Saves overlay dirty sectors plus the write log (backing handle is host state, not serialized).</summary>
    /// <remarks>Re-attach strategy: the backing image is reopened from its path on load
    /// (caller-owned); this snapshot stores only the sector count for validation plus the
    /// in-RAM overlay. Load fails loudly when the backing sector count differs.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(SectorCount);
        writer.Write(_overlay.Count);
        foreach (var entry in _overlay)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value, 0, 512);
        }

        writer.Write(_writeLog.Count);
        foreach (uint lba in _writeLog)
        {
            writer.Write(lba);
        }
    }

    /// <summary>Restores overlay dirty sectors plus the write log (backing must already be re-attached).</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported overlay snapshot version {version}.");
        }

        uint sectorCount = reader.ReadUInt32();
        if (sectorCount != SectorCount)
        {
            throw new InvalidDataException(
                $"HDD backing sector count mismatch on snapshot load (snapshot={sectorCount}, live={SectorCount}). Re-attach the same image.");
        }

        int dirty = reader.ReadInt32();
        if (dirty < 0)
        {
            throw new InvalidDataException("Negative overlay dirty-sector snapshot.");
        }

        _overlay.Clear();
        for (int i = 0; i < dirty; i++)
        {
            uint lba = reader.ReadUInt32();
            byte[] sector = reader.ReadBytes(512);
            if (sector.Length != 512)
            {
                throw new InvalidDataException("Truncated overlay sector snapshot.");
            }

            _overlay[lba] = sector;
        }

        int writes = reader.ReadInt32();
        if (writes < 0)
        {
            throw new InvalidDataException("Negative overlay write-log snapshot.");
        }

        _writeLog.Clear();
        for (int i = 0; i < writes; i++)
        {
            _writeLog.Add(reader.ReadUInt32());
        }
    }

    private const int SnapshotVersion = 1;
}
