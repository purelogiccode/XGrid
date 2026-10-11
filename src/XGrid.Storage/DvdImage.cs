using System;
using System.IO;

namespace XGrid.Storage;

/// <summary>A random-access 2048-byte-sector DVD image (Xbox game disc).</summary>
public interface IDvdImage : IDisposable
{
    /// <summary>Gets the number of 2048-byte sectors.</summary>
    uint SectorCount { get; }

    /// <summary>Reads one 2048-byte sector at the given LBA.</summary>
    void ReadSector(uint lba, Span<byte> destination);
}

/// <summary>A read-only DVD image backed by a raw disc file (ISO/XISO).</summary>
public sealed class IsoDvdImage : IDvdImage
{
    /// <summary>DVD sector size in bytes.</summary>
    public const int SectorSize = 2048;

    private readonly FileStream _stream;

    /// <summary>Opens a raw disc image file (read-only, shared reads allowed).</summary>
    public IsoDvdImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (_stream.Length < SectorSize)
        {
            _stream.Dispose();
            throw new InvalidDataException($"Not a DVD image (too small): '{path}'.");
        }

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
            throw new ArgumentException("Destination must hold one DVD sector.", nameof(destination));
        }

        _stream.Position = (long)lba * SectorSize;
        _stream.ReadExactly(destination[..SectorSize]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
    }
}
