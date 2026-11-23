using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using XGrid.Storage;

namespace XGrid.XboxFs;

/// <summary>
/// Minimal read-only XDVDFS lookup over a raw 2048-byte DVD sector stream, for
/// containers that cannot be handed to <see cref="XdvdfsFileSystem"/> as a file
/// path - a compressed CHD is the one that matters (a per-title configuration
/// wants the disc's title id, and the XISOSharp explorer only opens ISO/CSO
/// paths).
/// </summary>
/// <remarks>
/// The layout facts are the ones recorded in
/// <see cref="XdvdfsFileSystem"/>'s remarks and <c>docs/research-notes/xisosharp.md</c>
/// (XDVDFS format section, cross-checked against XboxDevWiki):
/// <list type="bullet">
/// <item>the volume descriptor is sector 32 of the game partition (byte
/// <c>0x10000</c>) and is exactly one sector: magic
/// <c>MICROSOFT*XBOX*MEDIA</c> at 0x00, root table sector at +20 and root table
/// size at +24 (both little-endian u32, partition-relative), the same magic
/// repeated at +0x7F4;</item>
/// <item>retail discs keep a video partition in front of the game area, so the
/// descriptor is probed at the known disc offsets (0, XGD2 0x0FD90000, XGD3
/// 0x02080000, XGD2-hybrid 0x89D80000, XGD1 0x18300000), and every sector
/// number in the volume is relative to the matching offset;</item>
/// <item>a directory entry is left child u16 at +0 (in dwords, 0 = none), right
/// child u16 at +2, start sector u32 at +4, size u32 at +8, attributes u8 at
/// +12 (0x10 = directory), name length u8 at +13, Latin-1 name at +14, entry
/// length <c>14 + nameLength</c> rounded up to 4 bytes;</item>
/// <item><c>0xFFFF</c> in the first field is the sector-pad sentinel: the walk
/// resumes at the next 2048-byte boundary. An all-zero 14-byte header is the
/// empty-directory sentinel (no entries).</item>
/// </list>
/// Only a root-level file is resolved, and a table that does not parse simply
/// reports "no file" - a wrong lookup must never invent an identity.
/// </remarks>
public static class XdvdfsSectorReader
{
    /// <summary>DVD sector size in bytes.</summary>
    private const int SectorSize = 2048;

    /// <summary>Sector that holds the XDVDFS volume descriptor inside the game partition.</summary>
    public const int VolumeDescriptorSector = 32;

    private const int MagicLength = 20;
    private const int TrailingMagicOffset = 0x7EC;
    private const int DirectoryEntryHeaderBytes = 14;
    private const uint NoChildOffset = 0xFFFFu;
    private const byte AttributeDirectory = 0x10;
    private const int MaxDirectoryDepth = 32;
    private const uint MaxRootTableBytes = 1u << 20;
    private const int MaxEntriesPerTable = 8192;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

    /// <summary>
    /// Known game-partition offsets, in the probe order the reference uses:
    /// a plain image first, then the XGD variants.
    /// </summary>
    private static readonly long[] DiscSeeks =
    [
        0L,
        0x0FD90000L, // XGD2 / global lseek
        0x02080000L, // XGD3
        0x89D80000L, // XGD2 hybrid
        0x18300000L, // XGD1
    ];

    /// <summary>
    /// Reads a root-level file out of an XDVDFS volume on a sector stream.
    /// </summary>
    /// <param name="image">Sector source (ISO, CHD, or any <see cref="IDvdImage"/>).</param>
    /// <param name="fileName">Root-level file name to find (case-insensitive, e.g. <c>default.xbe</c>).</param>
    /// <param name="maxBytes">Largest file the caller will read; larger entries report false.</param>
    /// <param name="contents">File bytes on success; empty on failure.</param>
    /// <returns>True when the volume parsed and the file was read.</returns>
    public static bool TryReadRootFile(IDvdImage image, string fileName, long maxBytes, out byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1L);

        contents = [];
        foreach (long discSeek in DiscSeeks)
        {
            if (!TryReadVolumeHeader(image, discSeek, out uint rootSector, out uint rootSize))
            {
                continue;
            }

            if (!TryReadTable(image, discSeek, rootSector, rootSize, out byte[] table))
            {
                continue;
            }

            var visited = new HashSet<long>();
            var budget = new EntryBudget(MaxEntriesPerTable);
            if (!TryFindEntry(table, 0, fileName, visited, budget, depth: 0, out DirectoryEntry entry))
            {
                continue;
            }

            if (entry.IsDirectory || entry.Size == 0 || entry.Size > maxBytes)
            {
                continue;
            }

            if (TryReadData(image, discSeek, entry.StartSector, entry.Size, out contents))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the volume descriptor at <paramref name="discSeek"/> and extracts the root table geometry.</summary>
    private static bool TryReadVolumeHeader(IDvdImage image, long discSeek, out uint rootSector, out uint rootSize)
    {
        rootSector = 0;
        rootSize = 0;
        if (!TryReadSectors(image, (discSeek / SectorSize) + VolumeDescriptorSector, 1, out byte[] sector))
        {
            return false;
        }

        ReadOnlySpan<byte> descriptor = sector;
        if (!descriptor.Slice(0, MagicLength).SequenceEqual(Magic) ||
            !descriptor.Slice(TrailingMagicOffset, MagicLength).SequenceEqual(Magic))
        {
            return false;
        }

        rootSector = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(20, 4));
        rootSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(24, 4));
        return rootSector != 0 && rootSize != 0 && rootSize <= MaxRootTableBytes;
    }

    /// <summary>Reads a directory table into memory (sector-padded to the table size).</summary>
    private static bool TryReadTable(IDvdImage image, long discSeek, uint tableSector, uint tableSize, out byte[] table)
    {
        int sectors = (int)((tableSize + SectorSize - 1u) / SectorSize);
        if (!TryReadSectors(image, (discSeek / SectorSize) + tableSector, sectors, out table))
        {
            table = [];
            return false;
        }

        return true;
    }

    /// <summary>AVL-walks a directory table for one name (left child, entry, right child).</summary>
    private static bool TryFindEntry(
        ReadOnlySpan<byte> table, long offset, string fileName, HashSet<long> visited, EntryBudget budget, int depth,
        out DirectoryEntry entry)
    {
        entry = default;
        if (depth > MaxDirectoryDepth || offset < 0 || offset > table.Length - DirectoryEntryHeaderBytes)
        {
            return false;
        }

        if (!visited.Add(offset) || !budget.Take())
        {
            return false;
        }

        ushort left = BinaryPrimitives.ReadUInt16LittleEndian(table.Slice((int)offset, 2));
        if (left == NoChildOffset)
        {
            // Sector-pad sentinel: the walk continues at the next sector boundary.
            long padded = offset + (SectorSize - (offset % SectorSize));
            return TryFindEntry(table, padded, fileName, visited, budget, depth + 1, out entry);
        }

        ushort right = BinaryPrimitives.ReadUInt16LittleEndian(table.Slice((int)(offset + 2), 2));
        uint startSector = BinaryPrimitives.ReadUInt32LittleEndian(table.Slice((int)(offset + 4), 4));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(table.Slice((int)(offset + 8), 4));
        byte attributes = table[(int)(offset + 12)];
        int nameLength = table[(int)(offset + 13)];

        // The all-zero header is the empty-directory sentinel: nothing to walk.
        if (left == 0 && right == 0 && startSector == 0 && size == 0 && attributes == 0 && nameLength == 0)
        {
            return false;
        }

        if (offset + DirectoryEntryHeaderBytes + nameLength > table.Length)
        {
            return false;
        }

        if (left != 0 &&
            TryFindEntry(table, left * 4L, fileName, visited, budget, depth + 1, out entry))
        {
            return true;
        }

        if (nameLength > 0)
        {
            string name = Encoding.Latin1.GetString(
                table.Slice((int)(offset + DirectoryEntryHeaderBytes), nameLength));
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                entry = new DirectoryEntry(startSector, size, (attributes & AttributeDirectory) != 0);
                return true;
            }
        }

        return right != 0 &&
            TryFindEntry(table, right * 4L, fileName, visited, budget, depth + 1, out entry);
    }

    /// <summary>Reads a file's contiguous data sectors.</summary>
    private static bool TryReadData(IDvdImage image, long discSeek, uint startSector, uint size, out byte[] data)
    {
        data = [];
        long first = (discSeek / SectorSize) + startSector;
        int sectors = (int)((size + SectorSize - 1u) / SectorSize);
        if (!TryReadSectors(image, first, sectors, out byte[] raw))
        {
            return false;
        }

        data = sectors * SectorSize == size ? raw : raw.AsSpan(0, (int)size).ToArray();
        return true;
    }

    /// <summary>Reads <paramref name="count"/> contiguous sectors, refusing anything outside the image.</summary>
    private static bool TryReadSectors(IDvdImage image, long startSector, int count, out byte[] data)
    {
        data = [];
        if (startSector < 0 || count <= 0)
        {
            return false;
        }

        long end = startSector + count;
        if (end > image.SectorCount)
        {
            return false;
        }

        var buffer = new byte[count * SectorSize];
        for (int i = 0; i < count; i++)
        {
            image.ReadSector(
                checked((uint)(startSector + i)),
                buffer.AsSpan(i * SectorSize, SectorSize));
        }

        data = buffer;
        return true;
    }

    /// <summary>One parsed directory entry.</summary>
    private readonly struct DirectoryEntry(uint startSector, uint size, bool isDirectory)
    {
        public uint StartSector { get; } = startSector;

        public uint Size { get; } = size;

        public bool IsDirectory { get; } = isDirectory;
    }

    /// <summary>Bounds the number of entries a walk may visit on a corrupt table.</summary>
    private sealed class EntryBudget(int remaining)
    {
        private int _remaining = remaining;

        public bool Take()
        {
            if (_remaining <= 0)
            {
                return false;
            }

            _remaining--;
            return true;
        }
    }
}
