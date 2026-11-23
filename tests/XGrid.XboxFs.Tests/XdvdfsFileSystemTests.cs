using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace XGrid.XboxFs.Tests;

public sealed class XdvdfsFileSystemTests : IDisposable
{
    private const int SectorSize = 2048;
    private const uint DescriptorSector = 32;
    private const uint RootSector = 33;
    private const uint HelloSector = 34;
    private const uint SubTableSector = 35;
    private const uint NestedSector = 36;

    private readonly List<string> _tempFiles = [];
    private bool _disposed;

    [Fact]
    public void ListDirectory_ReturnsRootEntries()
    {
        using var fs = OpenFixture();

        var entries = fs.ListDirectory("\\");

        Assert.Equal(2, entries.Count);
        Assert.Equal("HELLO.TXT", entries[0].Name);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal(100, entries[0].Size);
        Assert.Equal(HelloSector, entries[0].StartSector);
        Assert.Equal("SUB", entries[1].Name);
        Assert.True(entries[1].IsDirectory);
    }

    [Fact]
    public void ListDirectory_ResolvesSubdirectory()
    {
        using var fs = OpenFixture();

        var entries = fs.ListDirectory("\\SUB");

        Assert.Single(entries);
        Assert.Equal("NESTED.BIN", entries[0].Name);
        Assert.Equal(16, entries[0].Size);
    }

    [Fact]
    public void ReadFile_ReturnsExactContent()
    {
        using var fs = OpenFixture();

        byte[] data = fs.ReadFile("\\HELLO.TXT");

        Assert.Equal(100, data.Length);
        Assert.Equal((byte)'H', data[0]);
        Assert.Equal((byte)'X', data[99]);
    }

    [Fact]
    public void ReadFile_ByEntryReadsSubdirectoryFile()
    {
        using var fs = OpenFixture();
        var sub = fs.ListDirectory("\\")[1];

        byte[] data = fs.ReadFile(fs.ListDirectory("\\SUB")[0]);

        Assert.Equal(16, data.Length);
        Assert.Equal((byte)'N', data[0]);
        Assert.Equal((byte)'D', data[15]);
        Assert.Equal("SUB", sub.Name);
    }

    [Fact]
    public void ReadFile_MissingPathThrows()
    {
        using var fs = OpenFixture();

        Assert.Throws<FileNotFoundException>(() => fs.ReadFile("\\NOPE.TXT"));
    }

    [Fact]
    public void ReadFile_DirectoryThrows()
    {
        using var fs = OpenFixture();

        Assert.Throws<InvalidOperationException>(() => fs.ReadFile("\\SUB"));
    }

    [Fact]
    public void Open_GarbageThrowsInvalidData()
    {
        string path = WriteTemp([1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => XdvdfsFileSystem.Open(path));
    }

    [Fact]
    public void UseAfterDispose_Throws()
    {
        var fs = OpenFixture();
        fs.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fs.ListDirectory("\\"));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (string path in _tempFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }

            _disposed = true;
        }
    }

    private XdvdfsFileSystem OpenFixture()
    {
        byte[] image = new byte[(NestedSector + 1) * SectorSize];
        WriteDescriptor(image);
        var root = new List<byte>();
        WriteEntry(root, left: 0, right: 0, start: HelloSector, size: 100, attributes: 0x20, name: "HELLO.TXT");
        int subOffset = Pad(root);
        SetRight(root, 0, (ushort)(subOffset / 4));
        WriteEntry(root, left: 0, right: 0xFFFF, start: SubTableSector, size: SectorSize, attributes: 0x10,
            name: "SUB");
        Pad(root);
        Buffer.BlockCopy(root.ToArray(), 0, image, (int)RootSector * SectorSize, root.Count);
        for (int i = 0; i < 100; i++)
        {
            image[(HelloSector * SectorSize) + i] = i == 0 ? (byte)'H' : i == 99 ? (byte)'X' : (byte)'A';
        }

        var sub = new List<byte>();
        WriteEntry(sub, left: 0, right: 0xFFFF, start: NestedSector, size: 16, attributes: 0x20, name: "NESTED.BIN");
        Pad(sub);
        Buffer.BlockCopy(sub.ToArray(), 0, image, (int)SubTableSector * SectorSize, sub.Count);
        for (int i = 0; i < 16; i++)
        {
            image[(NestedSector * SectorSize) + i] = i == 0 ? (byte)'N' : i == 15 ? (byte)'D' : (byte)'B';
        }

        return XdvdfsFileSystem.Open(WriteTemp(image));
    }

    private static void WriteDescriptor(byte[] image)
    {
        byte[] magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
        Buffer.BlockCopy(magic, 0, image, (int)DescriptorSector * SectorSize, magic.Length);
        Buffer.BlockCopy(magic, 0, image, ((int)DescriptorSector * SectorSize) + 0x7EC, magic.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            new Span<byte>(image, ((int)DescriptorSector * SectorSize) + 0x14, 4), RootSector);
        BinaryPrimitives.WriteUInt32LittleEndian(
            new Span<byte>(image, ((int)DescriptorSector * SectorSize) + 0x18, 4), SectorSize);
    }

    private static void WriteEntry(List<byte> table, ushort left, ushort right, uint start, uint size, byte attributes,
        string name)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] header = new byte[14];
        BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(header, 0, 2), left);
        BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(header, 2, 2), right);
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(header, 4, 4), start);
        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(header, 8, 4), size);
        header[12] = attributes;
        header[13] = (byte)nameBytes.Length;
        table.AddRange(header);
        table.AddRange(nameBytes);
    }

    private static int Pad(List<byte> table)
    {
        while (table.Count % 4 != 0)
        {
            table.Add(0);
        }

        return table.Count;
    }

    private static void SetRight(List<byte> table, int entryOffset, ushort right)
    {
        table[entryOffset + 2] = (byte)(right & 0xFF);
        table[entryOffset + 3] = (byte)(right >> 8);
    }

    private string WriteTemp(byte[] content)
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-xdvdfs-" + Guid.NewGuid().ToString("N") + ".iso");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
