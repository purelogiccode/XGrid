using System;
using System.Buffers.Binary;
using System.Text;
using XGrid.Storage;
using Xunit;

namespace XGrid.XboxFs.Tests;

/// <summary>
/// Pins <see cref="XdvdfsSectorReader"/>: the sector-level XDVDFS lookup that
/// gives a per-title configuration a title id for containers the XISOSharp
/// explorer cannot open (a compressed CHD).
/// </summary>
public sealed class XdvdfsSectorReaderTests
{
    private const int SectorSize = 2048;
    private const int SectorCountMinimum = 512;
    private const uint RootSector = 0x108;
    private const uint ExecutableSector = 0x140;
    private const string ExecutableName = "default.xbe";

    /// <summary>In-memory disc image, one byte array of 2048-byte sectors.</summary>
    private sealed class MemoryDvdImage : IDvdImage
    {
        private readonly byte[] _data;

        public MemoryDvdImage(int sectors = SectorCountMinimum)
        {
            _data = new byte[sectors * SectorSize];
        }

        public uint SectorCount => (uint)(_data.Length / SectorSize);

        public Span<byte> Sector(uint lba)
        {
            return _data.AsSpan((int)(lba * SectorSize), SectorSize);
        }

        /// <summary>Returns <paramref name="count"/> contiguous sectors as one span.</summary>
        public Span<byte> Sectors(uint lba, int count)
        {
            return _data.AsSpan((int)(lba * SectorSize), count * SectorSize);
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            Sector(lba).CopyTo(destination);
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void ReadsARootFileThroughTheSectorStream()
    {
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false);

        Assert.True(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out byte[] contents));
        Assert.Equal(0x400, contents.Length);
        Assert.Equal("XBEH", Encoding.ASCII.GetString(contents, 0, 4));
    }

    [Fact]
    public void FindsTheVolumeBehindAnXgdPartitionOffset()
    {
        // A retail disc keeps a video partition in front of the game area: the
        // descriptor is probed at the XGD offsets, and every sector number in
        // the volume is relative to the matching offset.
        MemoryDvdImage image = BuildImage(discSeek: 0x02080000, children: false);

        Assert.True(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out byte[] contents));
        Assert.Equal("XBEH", Encoding.ASCII.GetString(contents, 0, 4));
    }

    [Fact]
    public void WalksTheAvlChildrenToReachTheEntry()
    {
        // The root table starts with an unrelated entry whose right child is the
        // executable: a reader that only looks at offset 0 must fail here.
        MemoryDvdImage image = BuildImage(discSeek: 0, children: true);

        Assert.True(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out byte[] contents));
        Assert.Equal("XBEH", Encoding.ASCII.GetString(contents, 0, 4));
    }

    [Fact]
    public void NameMatchIsCaseInsensitive()
    {
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false);

        Assert.True(XdvdfsSectorReader.TryReadRootFile(image, "DEFAULT.XBE", 1 << 20, out _));
    }

    [Fact]
    public void MissingFileReportsFalse()
    {
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false);

        Assert.False(XdvdfsSectorReader.TryReadRootFile(image, "other.xbe", 1 << 20, out byte[] contents));
        Assert.Empty(contents);
    }

    [Fact]
    public void AnImageWithoutTheMagicIsRejected()
    {
        // Nothing written: no descriptor anywhere, so no volume and no file.
        var image = new MemoryDvdImage();

        Assert.False(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out _));
    }

    [Fact]
    public void AnOversizedFileIsNotRead()
    {
        // The caller's cap protects the host from a bogus size field.
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false);

        Assert.False(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, maxBytes: 0x100, out _));
    }

    [Fact]
    public void ADirectoryEntryIsNotAFile()
    {
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false, executableIsDirectory: true);

        Assert.False(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out _));
    }

    [Fact]
    public void ASectorPaddedTableIsWalkedToTheNextSector()
    {
        // 0xFFFF in the first field is the sector-pad sentinel: the walk resumes
        // at the next sector boundary, where the real entry lives.
        MemoryDvdImage image = BuildImage(discSeek: 0, children: false, padFirstSector: true);

        Assert.True(XdvdfsSectorReader.TryReadRootFile(image, ExecutableName, 1 << 20, out byte[] contents));
        Assert.Equal("XBEH", Encoding.ASCII.GetString(contents, 0, 4));
    }

    /// <summary>Builds a one-file XDVDFS volume at <paramref name="discSeek"/>.</summary>
    private static MemoryDvdImage BuildImage(
        long discSeek, bool children, bool executableIsDirectory = false, bool padFirstSector = false)
    {
        var image = new MemoryDvdImage((int)((discSeek / SectorSize) + SectorCountMinimum));
        long baseSector = discSeek / SectorSize;
        uint rootTableSize = padFirstSector ? 2u * SectorSize : SectorSize;

        // Volume descriptor: magic at 0x00 and at +0x7EC, root geometry at +20/+24.
        Span<byte> descriptor = image.Sector((uint)(baseSector + XdvdfsSectorReader.VolumeDescriptorSector));
        WriteAscii(descriptor, 0, "MICROSOFT*XBOX*MEDIA");
        WriteAscii(descriptor, 0x7EC, "MICROSOFT*XBOX*MEDIA");
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(20, 4), RootSector);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(24, 4), rootTableSize);

        // Root table (one or two sectors, addressed as one span).
        Span<byte> table = image.Sectors((uint)(baseSector + RootSector), (int)(rootTableSize / SectorSize));
        int executableEntryOffset = 0;
        if (padFirstSector)
        {
            // Fill the first table sector with the pad sentinel.
            table.Slice(0, SectorSize).Fill(0xFF);
            executableEntryOffset = SectorSize;
        }

        if (children)
        {
            // Entry 0 holds another name and points its right child at the
            // executable's entry.
            const int otherOffset = 0;
            executableEntryOffset = children ? 64 : executableEntryOffset;
            WriteEntry(table, otherOffset, left: 0, right: (ushort)(executableEntryOffset / 4), "readme.txt",
                startSector: ExecutableSector + 4, size: 0x10, isDirectory: false);
        }

        WriteEntry(
            table,
            executableEntryOffset,
            left: 0,
            right: 0,
            ExecutableName,
            ExecutableSector,
            0x400,
            executableIsDirectory);

        // File data.
        Span<byte> data = image.Sector((uint)(baseSector + ExecutableSector)).Slice(0, 0x400);
        WriteAscii(data, 0, "XBEH");
        return image;
    }

    private static void WriteEntry(
        Span<byte> table, int offset, ushort left, ushort right, string name, uint startSector, uint size, bool isDirectory)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(table.Slice(offset, 2), left);
        BinaryPrimitives.WriteUInt16LittleEndian(table.Slice(offset + 2, 2), right);
        BinaryPrimitives.WriteUInt32LittleEndian(table.Slice(offset + 4, 4), startSector);
        BinaryPrimitives.WriteUInt32LittleEndian(table.Slice(offset + 8, 4), size);
        table[offset + 12] = isDirectory ? (byte)0x10 : (byte)0x00;
        table[offset + 13] = (byte)name.Length;
        WriteAscii(table, offset + 14, name);
    }

    private static void WriteAscii(Span<byte> destination, int offset, string text)
    {
        Encoding.ASCII.GetBytes(text, destination.Slice(offset, text.Length));
    }
}
