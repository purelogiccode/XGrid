using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Storage;
using Xunit;

namespace XGrid.XboxFs.Tests;

public sealed class ChihiroNetbootBuilderTests : IDisposable
{
    private const int SectorSize = 2048;
    private const uint DescriptorSector = 32;
    private const uint RootSector = 33;
    private const uint MediaSector = 34;
    private const uint BootIdSector = 35;
    private const uint OriginalXbeSector = 36;
    private const uint MediaFileSector = 37;

    private readonly string _root;

    public ChihiroNetbootBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-chihiro-builder-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Build_WritesABootableFatxTreeAndAliasesTheRenamedExecutable()
    {
        string iso = Path.Combine(_root, "game.iso");
        string bin = Path.Combine(_root, "game.bin");
        File.WriteAllBytes(iso, BuildXiso());

        ChihiroNetbootBuilder.Result result = ChihiroNetbootBuilder.Build(iso, bin);

        Assert.Equal("TEST", result.GameId);
        Assert.Equal("game.xbe", result.Executable);
        Assert.Equal(128L * 1024 * 1024, result.DimmBytes);
        Assert.Equal(result.DimmBytes - (16L * 1024 * 1024), result.ImageBytes);
        Assert.True(File.Exists(bin));
        Assert.Equal(result.ImageBytes, new FileInfo(bin).Length);

        using var disk = new RawDiskImage(bin, writable: false);
        FatxFileSystem fs = FatxFileSystem.Open(disk, 0, disk.SectorCount);

        IReadOnlyList<FatxFileSystem.Entry> root = fs.ListDirectory("\\");
        Assert.Contains(root, entry => string.Equals(entry.Name, "boot.id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root, entry => string.Equals(entry.Name, "game.xbe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root, entry => string.Equals(entry.Name, "game.xbe.bak", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root, entry => string.Equals(entry.Name, "media", StringComparison.OrdinalIgnoreCase)
                                       && entry.IsDirectory);

        Assert.Equal("BTID", Encoding.ASCII.GetString(fs.ReadFile("\\boot.id"), 0, 4));
        Assert.Equal("XBEH", Encoding.ASCII.GetString(fs.ReadFile("\\game.xbe"), 0, 4));
        Assert.Equal(
            fs.ReadFile("\\game.xbe"),
            fs.ReadFile("\\game.xbe.bak"));
        Assert.Equal("HELLO", Encoding.ASCII.GetString(fs.ReadFile("\\media\\data.bin")));
    }

    [Fact]
    public void Build_RejectsAnImageWithoutBootId()
    {
        string iso = Path.Combine(_root, "plain.iso");
        string bin = Path.Combine(_root, "plain.bin");
        File.WriteAllBytes(iso, BuildXiso(includeBootId: false));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => ChihiroNetbootBuilder.Build(iso, bin));

        Assert.Contains("boot.id", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(bin));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static byte[] BuildXiso(bool includeBootId = true)
    {
        byte[] image = new byte[40 * SectorSize];
        WriteDescriptor(image);

        var root = new List<byte>();
        List<int> offsets = [];
        if (includeBootId)
        {
            offsets.Add(AddEntry(root, "boot.id", 0x20, BootIdSector, 0xC0));
        }

        offsets.Add(AddEntry(root, "game.xbe.bak", 0x20, OriginalXbeSector, 0x100));
        offsets.Add(AddEntry(root, "media", 0x10, MediaSector, SectorSize));
        LinkRightChain(root, offsets);
        Buffer.BlockCopy(root.ToArray(), 0, image, (int)RootSector * SectorSize, root.Count);

        var media = new List<byte>();
        offsets = [AddEntry(media, "data.bin", 0x20, MediaFileSector, 5)];
        LinkRightChain(media, offsets);
        Buffer.BlockCopy(media.ToArray(), 0, image, (int)MediaSector * SectorSize, media.Count);

        if (includeBootId)
        {
            byte[] boot = new byte[0xC0];
            Encoding.ASCII.GetBytes("BTID").CopyTo(boot, 0);
            Encoding.ASCII.GetBytes("XBAM").CopyTo(boot, 0x20);
            Encoding.ASCII.GetBytes("TEST").CopyTo(boot, 0x30);
            Encoding.ASCII.GetBytes("TEST GAME").CopyTo(boot, 0x80);
            Encoding.ASCII.GetBytes("\\game.xbe").CopyTo(boot, 0xA0);
            Buffer.BlockCopy(boot, 0, image, (int)BootIdSector * SectorSize, boot.Length);
        }

        byte[] xbe = new byte[0x100];
        Encoding.ASCII.GetBytes("XBEH").CopyTo(xbe, 0);
        Buffer.BlockCopy(xbe, 0, image, (int)OriginalXbeSector * SectorSize, xbe.Length);
        Encoding.ASCII.GetBytes("HELLO").CopyTo(image, (int)MediaFileSector * SectorSize);
        return image;
    }

    private static void WriteDescriptor(byte[] image)
    {
        byte[] magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        int offset = (int)DescriptorSector * SectorSize;
        Buffer.BlockCopy(magic, 0, image, offset, magic.Length);
        Buffer.BlockCopy(magic, 0, image, offset + 0x7EC, magic.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            new Span<byte>(image, offset + 0x14, 4),
            RootSector);
        BinaryPrimitives.WriteUInt32LittleEndian(
            new Span<byte>(image, offset + 0x18, 4),
            SectorSize);
    }

    private static int AddEntry(List<byte> table, string name, byte attributes, uint start, uint size)
    {
        int offset = table.Count;
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] header = new byte[14];
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(header, 4, 4), start);
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(header, 8, 4), size);
        header[12] = attributes;
        header[13] = checked((byte)nameBytes.Length);
        table.AddRange(header);
        table.AddRange(nameBytes);
        while (table.Count % 4 != 0)
        {
            table.Add(0);
        }

        return offset;
    }

    private static void LinkRightChain(List<byte> table, List<int> offsets)
    {
        for (int i = 0; i < offsets.Count; i++)
        {
            ushort right = i + 1 < offsets.Count
                ? checked((ushort)(offsets[i + 1] / 4))
                : (ushort)0xFFFF;
            int at = offsets[i] + 2;
            table[at] = (byte)(right & 0xFF);
            table[at + 1] = (byte)(right >> 8);
        }
    }
}
