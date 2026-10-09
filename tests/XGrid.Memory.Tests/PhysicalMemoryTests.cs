using System;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class PhysicalMemoryTests
{
    [Fact]
    public void DefaultSize_Is64Mb()
    {
        var memory = new PhysicalMemory();

        Assert.Equal(64u * 1024 * 1024, memory.Size);
    }

    [Fact]
    public void CustomSize_IsPreserved()
    {
        var memory = new PhysicalMemory(1024);

        Assert.Equal(1024u, memory.Size);
    }

    [Fact]
    public void ZeroSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalMemory(0));
    }

    [Fact]
    public void Write32_Then_Read8_IsLittleEndian()
    {
        var memory = new PhysicalMemory(16);

        memory.Write32(0, 0x11223344);

        Assert.Equal(0x44, memory.Read8(0));
        Assert.Equal(0x33, memory.Read8(1));
        Assert.Equal(0x22, memory.Read8(2));
        Assert.Equal(0x11, memory.Read8(3));
    }

    [Fact]
    public void Widths_RoundTrip()
    {
        var memory = new PhysicalMemory(64);

        memory.Write8(0, 0xAB);
        memory.Write16(2, 0xBEEF);
        memory.Write32(4, 0xDEADBEEF);
        memory.Write64(8, 0x0123456789ABCDEF);

        Assert.Equal(0xAB, memory.Read8(0));
        Assert.Equal(0xBEEF, memory.Read16(2));
        Assert.Equal(0xDEADBEEFu, memory.Read32(4));
        Assert.Equal(0x0123456789ABCDEFUL, memory.Read64(8));
    }

    [Fact]
    public void UnalignedAccess_IsAllowed()
    {
        var memory = new PhysicalMemory(16);

        memory.Write32(1, 0xCAFEBABE);

        Assert.Equal(0xCAFEBABEu, memory.Read32(1));
    }

    [Theory]
    [InlineData(16, 1)]
    [InlineData(15, 2)]
    [InlineData(13, 4)]
    [InlineData(9, 8)]
    public void Access_OutOfRange_Throws(uint address, int width)
    {
        var memory = new PhysicalMemory(16);

        MemoryAccessException exception = Assert.Throws<MemoryAccessException>(() =>
        {
            switch (width)
            {
                case 1:
                    memory.Read8(address);
                    break;
                case 2:
                    memory.Read16(address);
                    break;
                case 4:
                    memory.Read32(address);
                    break;
                default:
                    memory.Read64(address);
                    break;
            }
        });

        Assert.Contains("out of range", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSpan_ReflectsWrites()
    {
        var memory = new PhysicalMemory(32);

        Span<byte> span = memory.GetSpan(8, 4);
        span[0] = 0x11;
        span[3] = 0x44;

        Assert.Equal(0x11, memory.Read8(8));
        Assert.Equal(0x44, memory.Read8(11));
    }

    [Fact]
    public void GetSpan_OutOfRange_Throws()
    {
        var memory = new PhysicalMemory(16);

        Assert.Throws<MemoryAccessException>(() => memory.GetSpan(12, 8).Length.ToString());
    }

    [Fact]
    public void Clear_ZeroesAllBytes()
    {
        var memory = new PhysicalMemory(16);
        memory.Write64(0, ulong.MaxValue);

        memory.Clear();

        Assert.Equal(0UL, memory.Read64(0));
    }
}
