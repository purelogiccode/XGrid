using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace XGrid.Core.Xemu;

/// <summary>One file entry in a ZArchive.</summary>
/// <param name="Name">Entry name (Latin-1 decoded).</param>
/// <param name="UncompressedOffset">Offset in the archive's uncompressed space.</param>
/// <param name="Size">Entry size in bytes.</param>
public readonly record struct ZarEntry(string Name, ulong UncompressedOffset, ulong Size);

/// <summary>
/// Reads the directory of a ZArchive (<c>.zar</c>) without decoding any block,
/// so the shell can tell whether an archive carries a disc image as one entry
/// or stores the game as a file tree.
/// </summary>
/// <remarks>
/// The format facts are in <c>docs/research-notes/zarchive-format.md</c> (from
/// the operator's ZArchiveSharp port of ZArchive 0.1.2). Everything is
/// big-endian on disk; the footer is the last 144 bytes.
/// </remarks>
public sealed class ZarArchive
{
    private const uint Magic = 0x169F52D6;
    private const uint Version = 0x61BF3A01;
    private const int FooterSize = 144;
    private const int TreeEntrySize = 16;

    private ZarArchive(IReadOnlyList<ZarEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Gets the file entries in the archive, in tree order.</summary>
    public IReadOnlyList<ZarEntry> Entries { get; }

    /// <summary>Gets a value indicating whether the archive holds an .iso entry.</summary>
    public bool HasDiscImage => FindDiscImage() is not null;

    /// <summary>Returns the largest .iso entry, or null.</summary>
    public ZarEntry? FindDiscImage()
    {
        ZarEntry? best = null;
        foreach (ZarEntry entry in Entries)
        {
            if (!entry.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null || entry.Size > best.Value.Size)
            {
                best = entry;
            }
        }

        return best;
    }

    /// <summary>Opens an archive and reads its directory.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="archive">Parsed directory, or null when the file is not a valid archive.</param>
    /// <returns>True when the file was read as a ZArchive.</returns>
    public static bool TryOpen(string path, out ZarArchive? archive)
    {
        archive = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length < FooterSize)
            {
                return false;
            }

            Span<byte> footer = stackalloc byte[FooterSize];
            stream.Seek(length - FooterSize, SeekOrigin.Begin);
            if (stream.Read(footer) != FooterSize)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(footer[140..]) != Magic
                || BinaryPrimitives.ReadUInt32BigEndian(footer[136..]) != Version
                || BinaryPrimitives.ReadUInt64BigEndian(footer[128..]) != (ulong)length)
            {
                return false;
            }

            ulong namesOffset = BinaryPrimitives.ReadUInt64BigEndian(footer[32..]);
            int namesSize = (int)BinaryPrimitives.ReadUInt64BigEndian(footer[40..]);
            ulong treeOffset = BinaryPrimitives.ReadUInt64BigEndian(footer[48..]);
            int treeSize = (int)BinaryPrimitives.ReadUInt64BigEndian(footer[56..]);
            if (namesSize <= 0 || treeSize < TreeEntrySize || treeSize % TreeEntrySize != 0)
            {
                return false;
            }

            byte[] names = ReadAt(stream, namesOffset, namesSize);
            byte[] tree = ReadAt(stream, treeOffset, treeSize);
            var entries = new List<ZarEntry>(treeSize / TreeEntrySize);
            for (int i = 0; i < tree.Length; i += TreeEntrySize)
            {
                ReadOnlySpan<byte> entry = tree.AsSpan(i, TreeEntrySize);
                uint nameAndType = BinaryPrimitives.ReadUInt32BigEndian(entry);
                if ((nameAndType & 0x8000_0000u) == 0)
                {
                    continue; // directory
                }

                string name = ReadName(names, nameAndType & 0x7FFF_FFFFu);
                if (name.Length == 0)
                {
                    continue;
                }

                ulong offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..])
                               | ((ulong)(BinaryPrimitives.ReadUInt32BigEndian(entry[12..]) & 0xFFFF) << 32);
                ulong size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..])
                             | ((ulong)(BinaryPrimitives.ReadUInt32BigEndian(entry[12..]) >> 16) << 32);
                entries.Add(new ZarEntry(name, offset, size));
            }

            archive = new ZarArchive(entries);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] ReadAt(FileStream stream, ulong offset, int size)
    {
        var buffer = new byte[size];
        stream.Seek((long)offset, SeekOrigin.Begin);
        if (stream.Read(buffer, 0, size) != size)
        {
            throw new IOException("Short read in the archive directory.");
        }

        return buffer;
    }

    private static string ReadName(byte[] names, uint offset)
    {
        int position = (int)offset;
        if (position < 0 || position >= names.Length)
        {
            return string.Empty;
        }

        // One header byte carries a 7-bit length; the high bit selects the
        // two-byte form (ZArchiveSharp keeps the upstream quirk that the second
        // byte is read from the header's own byte again).
        int length = names[position] & 0x7F;
        if ((names[position] & 0x80) != 0)
        {
            if (position + 1 >= names.Length)
            {
                return string.Empty;
            }

            length |= names[position] << 7;
            position += 2;
        }
        else
        {
            position++;
        }

        length = Math.Min(length, names.Length - position);
        return length <= 0 ? string.Empty : System.Text.Encoding.Latin1.GetString(names, position, length);
    }
}
