using System;
using System.IO;
using System.Text;
using Xunit;

namespace XGrid.Xbe.Tests;

public sealed class XbeImageTests
{
    private const uint Base = 0x00010000u;

    [Fact]
    public void Load_ParsesRetailHeadersSectionsAndCert()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out uint entry, out uint thunk);

        var xbe = XbeImage.Load(image);

        Assert.Equal(XbeImage.ImageType.Retail, xbe.Type);
        Assert.Equal(Base, xbe.BaseAddress);
        Assert.Equal(entry, xbe.EntryPoint);
        Assert.Equal(thunk, xbe.KernelThunk);
        Assert.Equal(0x12345678u, xbe.TitleId);
        Assert.Equal("TEST", xbe.TitleName);
        Assert.Equal(0x1u, xbe.AllowedMedia);
        Assert.Single(xbe.Sections);
        Assert.Equal(".text", xbe.Sections[0].Name);
        Assert.Equal(0x11000u, xbe.Sections[0].VirtualAddress);
        Assert.Equal(0x200u, xbe.Sections[0].RawSize);
        Assert.Equal(0x4u, xbe.Sections[0].Flags);
        Assert.Single(xbe.Libraries);
        Assert.Equal("XAPILIB", xbe.Libraries[0].Name);
        Assert.NotNull(xbe.Tls);
    }

    [Fact]
    public void Load_DecodesDebugEntryPoint()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Debug, out uint entry, out _);

        var xbe = XbeImage.Load(image);

        Assert.Equal(XbeImage.ImageType.Debug, xbe.Type);
        Assert.Equal(entry, xbe.EntryPoint);
    }

    [Fact]
    public void Load_DetectsChihiroByEntryRange()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x128, 0x40001000u);

        var xbe = XbeImage.Load(image);

        Assert.Equal(XbeImage.ImageType.Chihiro, xbe.Type);
    }

    [Fact]
    public void Load_BadMagic_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        image[0] = (byte)'Y';

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Fact]
    public void Load_TruncatedHeaders_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image.AsSpan(0, 0x100)));
    }

    [Fact]
    public void Load_SectionPastEnd_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x300 + 0x0C, 0x00FFFFFFu);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(257u)]
    public void Load_SectionCountOutOfRange_Throws(uint count)
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x11C, count);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Fact]
    public void Load_CertificatePastEnd_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x118, Base + 0xFF0u);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Fact]
    public void Load_TlsPastEnd_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x12C, Base + 0xFF0u);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Fact]
    public void Load_LibraryTablePastEnd_Throws()
    {
        byte[] image = BuildImage(XbeImage.ImageType.Retail, out _, out _);
        WriteU32(image, 0x164, Base + 0xFF8u);

        Assert.Throws<InvalidDataException>(() => XbeImage.Load(image));
    }

    [Fact]
    public void Load_RejectsAnEmptyImage()
    {
        Assert.Throws<InvalidDataException>(() => XbeImage.Load([]));
    }

    private static byte[] BuildImage(XbeImage.ImageType type, out uint entry, out uint thunk)
    {
        const int headersSize = 0x688;
        const int sectionCount = 1;
        byte[] image = new byte[0x1000];
        WriteAscii(image, 0x00, "XBEH");
        WriteU32(image, 0x104, Base);
        WriteU32(image, 0x108, headersSize);
        WriteU32(image, 0x110, 0x178u);
        WriteU32(image, 0x10C, 0u);
        WriteU32(image, 0x118, (Base + 0x200));
        WriteU32(image, 0x11C, sectionCount);
        WriteU32(image, 0x120, (Base + 0x300));
        entry = 0x11000u;
        WriteU32(image, 0x128, entry ^ EntryXor(type));
        WriteU32(image, 0x12C, (Base + 0x380));
        thunk = type == XbeImage.ImageType.Debug ? 0x3FA1F152u : 0x00011000u;
        uint storedThunk = type == XbeImage.ImageType.Debug
            ? 0x80100000u
            : thunk ^ ThunkXor(type);
        WriteU32(image, 0x158, storedThunk);
        WriteU32(image, 0x160, 1u);
        WriteU32(image, 0x164, (Base + 0x340));

        const int cert = 0x200;
        WriteU32(image, cert + 0x08, 0x12345678u);
        WriteUnicode(image, cert + 0x0C, "TEST");
        WriteU32(image, cert + 0x9C, 0x1u);

        const int table = 0x300;
        WriteU32(image, table + 0x00, 0x4u);
        WriteU32(image, table + 0x04, 0x11000u);
        WriteU32(image, table + 0x08, 0x200u);
        WriteU32(image, table + 0x0C, 0x400u);
        WriteU32(image, table + 0x10, 0x200u);
        WriteU32(image, table + 0x14, (Base + 0x360));
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

        for (int i = 0; i < 0x200; i++)
        {
            image[0x400 + i] = (byte)i;
        }

        return image;
    }

    private static uint EntryXor(XbeImage.ImageType type)
    {
        return type switch
        {
            XbeImage.ImageType.Chihiro => 0x40B5C16Eu,
            XbeImage.ImageType.Debug => 0x94859D4Bu,
            _ => 0xA8FC57ABu,
        };
    }

    private static uint ThunkXor(XbeImage.ImageType type)
    {
        return type switch
        {
            XbeImage.ImageType.Chihiro => 0x2290059Du,
            XbeImage.ImageType.Debug => 0xEFB1F152u,
            _ => 0x5B6D40B6u,
        };
    }

    private static void WriteU32(byte[] image, int offset, uint value)
    {
        image[offset] = (byte)value;
        image[offset + 1] = (byte)(value >> 8);
        image[offset + 2] = (byte)(value >> 16);
        image[offset + 3] = (byte)(value >> 24);
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
}
