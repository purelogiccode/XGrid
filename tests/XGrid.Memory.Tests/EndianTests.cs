using System;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class EndianTests
{
    [Fact]
    public void Read16_UsesLittleEndian()
    {
        byte[] bytes = [0x34, 0x12];

        Assert.Equal(0x1234, Endian.Read16(bytes));
    }

    [Fact]
    public void Read32_UsesLittleEndian()
    {
        byte[] bytes = [0x78, 0x56, 0x34, 0x12];

        Assert.Equal(0x12345678u, Endian.Read32(bytes));
    }

    [Fact]
    public void Read64_UsesLittleEndian()
    {
        byte[] bytes = [0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01];

        Assert.Equal(0x0123456789ABCDEFUL, Endian.Read64(bytes));
    }

    [Fact]
    public void Write16_ProducesLittleEndianBytes()
    {
        byte[] bytes = new byte[2];

        Endian.Write16(bytes, 0x1234);

        Assert.Equal(new byte[] { 0x34, 0x12 }, bytes);
    }

    [Fact]
    public void Write32_ProducesLittleEndianBytes()
    {
        byte[] bytes = new byte[4];

        Endian.Write32(bytes, 0x12345678);

        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, bytes);
    }

    [Fact]
    public void Write64_ProducesLittleEndianBytes()
    {
        byte[] bytes = new byte[8];

        Endian.Write64(bytes, 0x0123456789ABCDEF);

        Assert.Equal(new byte[] { 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01 }, bytes);
    }

    [Fact]
    public void Write_Then_Read_RoundTrips()
    {
        byte[] bytes = new byte[8];

        Endian.Write64(bytes, 0xDEADBEEFCAFEF00D);

        Assert.Equal(0xDEADBEEFCAFEF00DUL, Endian.Read64(bytes));
    }

    [Fact]
    public void Read_TooShort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Endian.Read32(new byte[2]));
    }
}
