using System;
using System.IO;
using System.Text;
using XGrid.Host;
using XGrid.Storage;
using Xunit;

namespace XGrid.Integration.Tests;

/// <summary>
/// Pins the disc title-id resolver used by the per-title override V2 keying:
/// the id comes from the XBE certificate (not the file name), and an image
/// that cannot be read has no id (the caller then keys by path).
/// </summary>
public sealed class DiscTitleIdTests
{
    private const uint Base = 0x10000u;
    private const uint TestTitleId = 0x4D530002u;

    [Fact]
    public void ReadsTheCertificateTitleIdAndName()
    {
        byte[] image = BuildXbe(TestTitleId, "HALO");

        Assert.True(DiscTitleId.TryReadExecutable(image, out uint titleId, out string titleName));
        Assert.Equal(TestTitleId, titleId);
        Assert.Equal("HALO", titleName);
    }

    [Fact]
    public void RejectsANonXbeImage()
    {
        byte[] image = Encoding.ASCII.GetBytes("this is not an executable at all, just bytes");

        Assert.False(DiscTitleId.TryReadExecutable(image, out uint titleId, out string titleName));
        Assert.Equal(0u, titleId);
        Assert.Empty(titleName);
    }

    [Fact]
    public void RejectsAZeroTitleId()
    {
        // A zero certificate id is "unknown", not a usable key: keying every
        // such image under title:00000000 would merge unrelated titles.
        byte[] image = BuildXbe(0u, "NONE");

        Assert.False(DiscTitleId.TryReadExecutable(image, out uint titleId, out _));
        Assert.Equal(0u, titleId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPathHasNoTitle(string? path)
    {
        Assert.False(DiscTitleId.TryRead(path, out uint titleId, out _));
        Assert.Equal(0u, titleId);
    }

    [Fact]
    public void UnsupportedContainersAndMissingFilesHaveNoTitle()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xgrid-disc-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // A missing CHD and a missing ISO cannot be opened at all.
            Assert.False(DiscTitleId.TryRead(Path.Combine(directory, "game.chd"), out _, out _));
            Assert.False(DiscTitleId.TryRead(Path.Combine(directory, "missing.iso"), out _, out _));

            // A garbage .xbe is rejected without throwing.
            string garbage = Path.Combine(directory, "garbage.xbe");
            File.WriteAllBytes(garbage, Encoding.ASCII.GetBytes("not an xbe"));
            Assert.False(DiscTitleId.TryRead(garbage, out _, out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadsTheTitleThroughACompressedContainerSectorStream()
    {
        // The CHD path: the reader walks the XDVDFS volume through the image's
        // sector interface, which is exactly what a compressed CHD exposes.
        var image = new MemoryDvdImage();

        Assert.True(DiscTitleId.TryReadImage(image, out uint titleId, out string titleName));
        Assert.Equal(TestTitleId, titleId);
        Assert.Equal("HALO", titleName);
    }

    [Fact]
    public void AnImageWithoutTheRootExecutableHasNoTitle()
    {
        var image = new MemoryDvdImage(rootFile: "eeprom.bin");

        Assert.False(DiscTitleId.TryReadImage(image, out uint titleId, out _));
        Assert.Equal(0u, titleId);
    }

    [Fact]
    public void ANullImageIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => DiscTitleId.TryReadImage(null!, out _, out _));
    }

    [Fact]
    public void ReadsALooseXbeFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "xgrid-title-" + Guid.NewGuid().ToString("N") + ".xbe");
        File.WriteAllBytes(path, BuildXbe(TestTitleId, "TEST GAME"));
        try
        {
            Assert.True(DiscTitleId.TryRead(path, out uint titleId, out string titleName));
            Assert.Equal(TestTitleId, titleId);
            Assert.Equal("TEST GAME", titleName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Builds the minimal XBE header + certificate the parser needs (shape copied from the XbeImage tests).</summary>
    private static byte[] BuildXbe(uint titleId, string titleName)
    {
        const int headersSize = 0x688;
        byte[] image = new byte[0x1000];
        WriteAscii(image, 0x00, "XBEH");
        WriteU32(image, 0x104, Base);
        WriteU32(image, 0x108, headersSize);
        WriteU32(image, 0x10C, 0u);
        WriteU32(image, 0x110, 0x178u);
        WriteU32(image, 0x118, Base + 0x200u);
        WriteU32(image, 0x11C, 1u);
        WriteU32(image, 0x120, Base + 0x300u);
        WriteU32(image, 0x128, 0x11000u ^ 0xA8FC57ABu);
        WriteU32(image, 0x12C, Base + 0x380u);
        WriteU32(image, 0x158, 0x00011000u ^ 0x5B6D40B6u);
        WriteU32(image, 0x160, 1u);
        WriteU32(image, 0x164, Base + 0x340u);
        WriteU32(image, 0x200 + 0x08, titleId);
        WriteUnicode(image, 0x200 + 0x0C, titleName);
        WriteU32(image, 0x200 + 0x9C, 0x1u);
        WriteU32(image, 0x300 + 0x00, 0x4u);
        WriteU32(image, 0x300 + 0x04, 0x11000u);
        WriteU32(image, 0x300 + 0x08, 0x200u);
        WriteU32(image, 0x300 + 0x0C, 0x400u);
        WriteU32(image, 0x300 + 0x10, 0x200u);
        WriteU32(image, 0x300 + 0x14, Base + 0x360u);
        WriteAscii(image, 0x360, ".text");
        WriteAscii(image, 0x340, "XAPILIB");
        WriteU16(image, 0x348, 1);
        WriteU16(image, 0x34A, 0);
        WriteU16(image, 0x34C, 4361);
        WriteU32(image, 0x380, 0x12000u);
        WriteU32(image, 0x384, 0x12000u);
        WriteU32(image, 0x388, 0x13000u);
        WriteU32(image, 0x38C, 0u);
        WriteU32(image, 0x390, 0x1000u);
        WriteU32(image, 0x394, 0u);
        return image;
    }

    private static void WriteU16(byte[] image, int offset, ushort value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteAscii(byte[] image, int offset, string text)
    {
        Encoding.ASCII.GetBytes(text).CopyTo(image, offset);
    }

    private static void WriteUnicode(byte[] image, int offset, string text)
    {
        Encoding.Unicode.GetBytes(text).CopyTo(image, offset);
    }

    private static void WriteU32(byte[] image, int offset, uint value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
        image[offset + 2] = (byte)(value >> 16);
        image[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// An in-memory disc image with one XDVDFS volume at sector 0 whose root
    /// directory holds a single entry, standing in for the compressed
    /// container the CHD path reads.
    /// </summary>
    private sealed class MemoryDvdImage : IDvdImage
    {
        private const int SectorSize = 2048;
        private const int TotalSectors = 256;
        private const uint RootSector = 0x30;
        private const uint FileSector = 0x40;

        private readonly byte[] _data = new byte[TotalSectors * SectorSize];

        public MemoryDvdImage(string rootFile = "default.xbe")
        {
            byte[] xbe = BuildXbe(TestTitleId, "HALO");
            int fileSectors = (xbe.Length + SectorSize - 1) / SectorSize;

            Span<byte> descriptor = Sector(32);
            Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA").CopyTo(descriptor);
            Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA").CopyTo(descriptor[0x7EC..]);
            WriteU32(descriptor, 20, RootSector);
            WriteU32(descriptor, 24, SectorSize);

            Span<byte> table = Sector(RootSector);
            table[13] = (byte)rootFile.Length;
            Encoding.ASCII.GetBytes(rootFile).CopyTo(table[14..]);
            WriteU32(table, 4, FileSector);
            WriteU32(table, 8, (uint)xbe.Length);

            xbe.CopyTo(Sectors(FileSector, fileSectors));
            _ = fileSectors;
        }

        public uint SectorCount => (uint)(_data.Length / SectorSize)!;

        public void ReadSector(uint lba, Span<byte> destination)
        {
            Sector(lba).CopyTo(destination);
        }

        public void Dispose()
        {
        }

        private Span<byte> Sector(uint lba)
        {
            return _data.AsSpan((int)(lba * SectorSize), SectorSize);
        }

        private Span<byte> Sectors(uint lba, int count)
        {
            return _data.AsSpan((int)(lba * SectorSize), count * SectorSize);
        }

        private static void WriteU32(Span<byte> data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
