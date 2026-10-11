using System;
using System.IO;
using CHDSharp;
using CHDSharp.Models;

namespace XGrid.Storage;

/// <summary>
/// A read-only DVD image backed by a MAME CHD file (compressed disc image),
/// via the MIT-licensed CHDSharp library. The CHD holds a disc layout of its
/// own (verified: rebuilt XISO with the descriptor at sector 32); guest LBAs
/// map to CHD byte offsets directly (<c>lba * 2048</c>). Single-threaded use
/// only (the underlying reader is not thread-safe).
/// </summary>
public sealed class ChdDvdImage : IDvdImage
{
    /// <summary>DVD sector size in bytes.</summary>
    public const int SectorSize = 2048;

    private readonly ChdFile _chd;
    private readonly byte[] _scratch = new byte[SectorSize];
    private bool _disposed;

    private ChdDvdImage(ChdFile chd, uint sectors)
    {
        _chd = chd;
        SectorCount = sectors;
    }

    /// <summary>Opens a CHD disc image file.</summary>
    public static ChdDvdImage Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ChdError error = ChdFile.Open(path, out ChdFile? chd);
        if (error != ChdError.Chderrnone || chd is null)
        {
            throw new InvalidDataException($"Not a CHD image ({error.GetMessage()}): '{path}'.");
        }

        try
        {
            uint sectors = (uint)(chd.TotalBytes / SectorSize);
            if (sectors == 0)
            {
                throw new InvalidDataException($"CHD image is too small: '{path}'.");
            }

            return new ChdDvdImage(chd, sectors);
        }
        catch
        {
            chd.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public uint SectorCount { get; }

    /// <inheritdoc />
    public void ReadSector(uint lba, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lba, SectorCount);
        if (destination.Length < SectorSize)
        {
            throw new ArgumentException("Destination must hold one DVD sector.", nameof(destination));
        }

        ChdError error = _chd.Read((ulong)lba * SectorSize, _scratch, 0, SectorSize);
        if (error != ChdError.Chderrnone)
        {
            throw new IOException($"CHD read failed ({error.GetMessage()}) at LBA {lba}.");
        }

        _scratch.AsSpan().CopyTo(destination);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _chd.Dispose();
            _disposed = true;
        }
    }
}
