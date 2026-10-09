using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Storage;
using Xunit;

namespace XGrid.XboxFs.Tests;

public sealed class FatxFileSystemTests
{
    private const uint Base = 0x40;
    private const uint ClusterSectors = 4;
    private const uint PartSectors = 0x400;
    private const uint DataStart = Base + 8 + 1;

    [Fact]
    public void ListDirectory_ReturnsRootEntries()
    {
        var fs = OpenFixture(out _);

        var entries = fs.ListDirectory("\\");

        Assert.Equal(3, entries.Count);
        Assert.Equal("HELLO.TXT", entries[0].Name);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal(100u, entries[0].Size);
        Assert.Equal("SUB", entries[1].Name);
        Assert.True(entries[1].IsDirectory);
        Assert.Equal("BIG.BIN", entries[2].Name);
    }

    [Fact]
    public void ReadFile_ReturnsSingleClusterContent()
    {
        var fs = OpenFixture(out _);

        byte[] data = fs.ReadFile("\\HELLO.TXT");

        Assert.Equal(100, data.Length);
        Assert.Equal((byte)'H', data[0]);
        Assert.Equal((byte)'X', data[99]);
    }

    [Fact]
    public void ReadFile_FollowsClusterChain()
    {
        var fs = OpenFixture(out _);
        uint clusterBytes = ClusterSectors * 512;

        byte[] data = fs.ReadFile("\\BIG.BIN");

        Assert.Equal((int)(2 * clusterBytes), data.Length);
        Assert.Equal((byte)'A', data[0]);
        Assert.Equal((byte)'B', data[(int)clusterBytes]);
    }

    [Fact]
    public void ListDirectory_ResolvesSubdirectory()
    {
        var fs = OpenFixture(out _);

        var entries = fs.ListDirectory("\\SUB");

        Assert.Single(entries);
        Assert.Equal("NESTED.BIN", entries[0].Name);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var fs = OpenFixture(out _);

        byte[] data = fs.ReadFile("\\sub\\nested.bin");

        Assert.Equal(10, data.Length);
    }

    [Fact]
    public void MissingFile_ThrowsFileNotFound()
    {
        var fs = OpenFixture(out _);

        Assert.Throws<FileNotFoundException>(() => fs.ReadFile("\\NOPE.TXT"));
    }

    [Fact]
    public void MissingDirectory_ThrowsDirectoryNotFound()
    {
        var fs = OpenFixture(out _);

        Assert.Throws<DirectoryNotFoundException>(() => fs.ListDirectory("\\NOPE"));
    }

    [Fact]
    public void ReadDirectory_AsFile_Throws()
    {
        var fs = OpenFixture(out _);

        Assert.Throws<InvalidOperationException>(() => fs.ReadFile("\\SUB"));
    }

    [Fact]
    public void BadMagic_ThrowsInvalidData()
    {
        var disk = new FakeDisk(PartSectors);
        disk.WriteSector(Base, new byte[512]);

        Assert.Throws<InvalidDataException>(() => FatxFileSystem.Open(disk, Base, PartSectors));
    }

    [Fact]
    public void TruncatedChain_ThrowsInvalidData()
    {
        var fs = OpenFixture(out FakeDisk disk);
        disk.WriteFatEntry(Base, 4, 0, wide: false);

        Assert.Throws<InvalidDataException>(() => fs.ReadFile("\\BIG.BIN"));
    }

    [Fact]
    public void WideEntries_Parse32BitChains()
    {
        const uint wideBase = 0x80;
        const uint wideSectors = 2097824u;
        const uint wideClusters = (wideSectors - 8u) / 32u;
        uint wideData = wideBase + 8u + ((wideClusters * 4u + 511u) / 512u);
        var disk = new FakeDisk(wideSectors);
        WriteSuperblock(disk, wideBase, 32);
        disk.WriteFatEntry(wideBase, 0, 0xFFFFFFF8u, wide: true);
        disk.WriteFatEntry(wideBase, 1, 0xFFFFFFFFu, wide: true);
        disk.WriteFatEntry(wideBase, 2, 0xFFFFFFFFu, wide: true);
        WriteEntry(disk, wideData, 0, "F.BIN", 0x20, 2, 50);
        WriteEnd(disk, wideData, 64);
        Span<byte> content = stackalloc byte[50];
        content.Fill((byte)'Z');
        disk.WritePartial(wideData + 32u, 0, content);

        var fs = FatxFileSystem.Open(disk, wideBase, wideSectors);

        Assert.Single(fs.ListDirectory("\\"));
        byte[] data = fs.ReadFile("\\F.BIN");
        Assert.Equal(50, data.Length);
        Assert.All(data, b => Assert.Equal((byte)'Z', b));
    }

    private static FatxFileSystem OpenFixture(out FakeDisk disk)
    {
        disk = new FakeDisk(PartSectors);
        WriteSuperblock(disk, Base, ClusterSectors);
        disk.WriteFatEntry(Base, 0, 0xFFF8, wide: false);
        disk.WriteFatEntry(Base, 1, 0xFFFF, wide: false);
        disk.WriteFatEntry(Base, 2, 0xFFFF, wide: false);
        disk.WriteFatEntry(Base, 3, 0xFFFF, wide: false);
        disk.WriteFatEntry(Base, 4, 5, wide: false);
        disk.WriteFatEntry(Base, 5, 0xFFFF, wide: false);
        disk.WriteFatEntry(Base, 6, 0xFFFF, wide: false);
        WriteEntry(disk, DataStart, 0, "HELLO.TXT", 0x00, 2, 100);
        WriteEntry(disk, DataStart, 64, "SUB", 0x10, 3, 0);
        WriteEntry(disk, DataStart, 128, "BIG.BIN", 0x00, 4, 2 * ClusterSectors * 512);
        WriteEnd(disk, DataStart, 192);
        uint sub = DataStart + ((3u - 1u) * ClusterSectors);
        WriteEntry(disk, sub, 0, "NESTED.BIN", 0x00, 6, 10);
        WriteEnd(disk, sub, 64);
        FillCluster(disk, 2, (byte)'H', (byte)'X');
        FillCluster(disk, 4, (byte)'A', 0);
        FillCluster(disk, 5, (byte)'B', 0);
        FillCluster(disk, 6, (byte)'N', 0);
        return FatxFileSystem.Open(disk, Base, PartSectors);

        static void FillCluster(FakeDisk d, uint cluster, byte fill, byte last)
        {
            uint lba = DataStart + ((cluster - 1u) * ClusterSectors);
            Span<byte> sector = stackalloc byte[512];
            sector.Fill(fill);
            for (uint i = 0; i < ClusterSectors; i++)
            {
                d.WritePartial(lba + i, 0, sector);
            }

            if (last != 0)
            {
                sector.Clear();
                sector[0] = last;
                d.WritePartial(lba, 99, sector.Slice(0, 1));
            }
        }
    }

    private static void WriteSuperblock(FakeDisk disk, uint baseLba, uint clusterSectors)
    {
        Span<byte> superblock = stackalloc byte[16];
        superblock[0] = (byte)'F';
        superblock[1] = (byte)'A';
        superblock[2] = (byte)'T';
        superblock[3] = (byte)'X';
        superblock[8] = (byte)clusterSectors;
        disk.WritePartial(baseLba, 0, superblock);
    }

    private static void WriteEntry(
        FakeDisk disk, uint lba, int offset, string name, byte attributes, uint cluster, uint size)
    {
        Span<byte> entry = stackalloc byte[64];
        entry[0] = (byte)name.Length;
        entry[1] = attributes;
        for (int i = 0; i < name.Length; i++)
        {
            entry[2 + i] = (byte)name[i];
        }

        entry[44] = (byte)cluster;
        entry[45] = (byte)(cluster >> 8);
        entry[46] = (byte)(cluster >> 16);
        entry[47] = (byte)(cluster >> 24);
        entry[48] = (byte)size;
        entry[49] = (byte)(size >> 8);
        entry[50] = (byte)(size >> 16);
        entry[51] = (byte)(size >> 24);
        disk.WritePartial(lba, offset, entry);
    }

    private static void WriteEnd(FakeDisk disk, uint lba, int offset)
    {
        Span<byte> end = stackalloc byte[1];
        end[0] = 0xFF;
        disk.WritePartial(lba, offset, end);
    }

    private sealed class FakeDisk : IDiskImage
    {
        private readonly Dictionary<uint, byte[]> _sectors = new();

        public FakeDisk(uint sectors)
        {
            SectorCount = sectors;
        }

        public uint SectorCount { get; }

        public void WriteFatEntry(uint baseLba, uint index, uint value, bool wide)
        {
            uint offset = index * (wide ? 4u : 2u);
            int size = wide ? 4 : 2;
            Span<byte> bytes = stackalloc byte[4];
            bytes[0] = (byte)value;
            bytes[1] = (byte)(value >> 8);
            bytes[2] = (byte)(value >> 16);
            bytes[3] = (byte)(value >> 24);
            WritePartial(baseLba + 8 + (offset / 512), (int)(offset % 512), bytes.Slice(0, size));
        }

        public void WritePartial(uint lba, int offset, ReadOnlySpan<byte> data)
        {
            if (!_sectors.TryGetValue(lba, out byte[]? sector))
            {
                sector = new byte[512];
                _sectors[lba] = sector;
            }

            data.CopyTo(sector.AsSpan(offset));
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            if (!_sectors.TryGetValue(lba, out byte[]? sector))
            {
                destination[..512].Clear();
                return;
            }

            sector.CopyTo(destination);
        }

        public void WriteSector(uint lba, ReadOnlySpan<byte> source)
        {
            WritePartial(lba, 0, source.Slice(0, 512));
        }

        public void Dispose()
        {
        }
    }
}
