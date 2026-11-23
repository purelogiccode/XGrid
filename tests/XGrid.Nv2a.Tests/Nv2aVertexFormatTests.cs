using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// XFP2: the array-attribute decode differential. Every NV097 vertex-array
/// format is fetched through the real <see cref="Nv2AMmio"/> path (defaults,
/// count, stride and the type decode) and compared with xemu 0.8.139
/// <c>pgraph/vertex.c</c>'s rules.
/// </summary>
public sealed class Nv2aVertexFormatTests
{

    [Fact]
    public void UbD3dSwizzlesBgraToRgba()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        // D3D packs a colour as BGRA; the decode yields (R,G,B,A)/255.
        WriteBytes(memory, 0x2000, 0x10, 0x20, 0x30, 0x40);
        nv2a.SetVertexArrayForTest(0, Format(type: 0, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(0x30 / 255f, value[0]);
        Assert.Equal(0x20 / 255f, value[1]);
        Assert.Equal(0x10 / 255f, value[2]);
        Assert.Equal(0x40 / 255f, value[3]);
    }

    [Fact]
    public void UbOglKeepsByteOrder()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteBytes(memory, 0x2000, 0x10, 0x20, 0x30, 0x40);
        nv2a.SetVertexArrayForTest(0, Format(type: 4, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(0x10 / 255f, value[0]);
        Assert.Equal(0x20 / 255f, value[1]);
        Assert.Equal(0x30 / 255f, value[2]);
        Assert.Equal(0x40 / 255f, value[3]);
    }

    [Fact]
    public void ShortOneNormalizesAndClampsAtMinusOne()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, 0x7FFF);
        WriteWord(memory, 0x2002, 0x8000);
        WriteWord(memory, 0x2004, 0xFFFF);
        nv2a.SetVertexArrayForTest(0, Format(type: 1, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(1f, value[0]);
        Assert.Equal(-1f, value[1]);
        Assert.Equal(-1f / 32767f, value[2]);
    }

    [Fact]
    public void FloatPassesThrough()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, BitConverter.SingleToUInt32Bits(1.5f));
        WriteWord(memory, 0x2004, BitConverter.SingleToUInt32Bits(-2.25f));
        nv2a.SetVertexArrayForTest(0, Format(type: 2, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(1.5f, value[0]);
        Assert.Equal(-2.25f, value[1]);
    }

    [Fact]
    public void Short32KIsRawNotNormalized()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, 0x8000);
        WriteWord(memory, 0x2002, 0x7FFF);
        nv2a.SetVertexArrayForTest(0, Format(type: 5, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(-32768f, value[0]);
        Assert.Equal(32767f, value[1]);
    }

    [Fact]
    public void CmpSignExtendsElevenElevenTen()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        // x = -1023 (11 bits), y = 1023, z = -512 (10 bits).
        uint packed = 0x401u | (0x3FFu << 11) | (0x200u << 22);
        WriteWord(memory, 0x2000, packed);
        nv2a.SetVertexArrayForTest(0, Format(type: 6, count: 4, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(-1f, value[0]);
        Assert.Equal(1f, value[1]);
        Assert.Equal(-1f, value[2]);
        Assert.Equal(1f, value[3]);
    }

    [Fact]
    public void CountTwoLeavesZAndWAtDefaults()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, BitConverter.SingleToUInt32Bits(3f));
        WriteWord(memory, 0x2004, BitConverter.SingleToUInt32Bits(4f));
        nv2a.SetVertexArrayForTest(0, Format(type: 2, count: 2, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 0);
        Assert.Equal(3f, value[0]);
        Assert.Equal(4f, value[1]);
        Assert.Equal(0f, value[2]);
        Assert.Equal(1f, value[3]);
    }

    [Fact]
    public void CountZeroKeepsTheDefaultAttribute()
    {
        (Nv2AMmio nv2a, _) = CreateChannel();
        nv2a.SetVertexArrayForTest(0, Format(type: 2, count: 0, stride: 0), 0x2000);

        float[] value = Fetch(nv2a, 0, 7);
        Assert.Equal(0f, value[0]);
        Assert.Equal(0f, value[1]);
        Assert.Equal(0f, value[2]);
        Assert.Equal(1f, value[3]);
    }

    [Fact]
    public void StrideZeroRepeatsTheFirstVertex()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, BitConverter.SingleToUInt32Bits(9f));
        WriteWord(memory, 0x2010, BitConverter.SingleToUInt32Bits(1f));
        nv2a.SetVertexArrayForTest(0, Format(type: 2, count: 1, stride: 0), 0x2000);

        Assert.Equal(9f, Fetch(nv2a, 0, 0)[0]);
        Assert.Equal(9f, Fetch(nv2a, 0, 5)[0]);
    }

    [Fact]
    public void StrideAdvancesByVertex()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteWord(memory, 0x2000, BitConverter.SingleToUInt32Bits(1f));
        WriteWord(memory, 0x200C, BitConverter.SingleToUInt32Bits(2f));
        WriteWord(memory, 0x2018, BitConverter.SingleToUInt32Bits(3f));
        nv2a.SetVertexArrayForTest(0, Format(type: 2, count: 1, stride: 12), 0x2000);

        Assert.Equal(1f, Fetch(nv2a, 0, 0)[0]);
        Assert.Equal(2f, Fetch(nv2a, 0, 1)[0]);
        Assert.Equal(3f, Fetch(nv2a, 0, 2)[0]);
    }

    private static uint Format(int type, int count, int stride)
    {
        return (uint)((type & 0xF) | ((count & 0xF) << 4) | (stride << 8));
    }

    private static float[] Fetch(Nv2AMmio nv2a, int slot, int index)
    {
        var value = new float[4];
        nv2a.FetchVertexAttributeForTest(slot, index, value);
        return value;
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2a = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        return (nv2a, memory);
    }

    private static void WriteBytes(ArrayMemory memory, uint address, params byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            memory.WriteByte(address + (uint)i, bytes[i]);
        }
    }

    private static void WriteWord(ArrayMemory memory, uint address, uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            memory.WritePhysical(address + (uint)i, (byte)(value >> (8 * i)));
        }
    }

    private sealed class ArrayMemory(int size = 0x10000) : IGpuMemory
    {
        private byte[] Bytes { get; } = new byte[size];

        public void WritePhysical(uint address, uint value)
        {
            Bytes[address] = (byte)value;
            Bytes[address + 1] = (byte)(value >> 8);
            Bytes[address + 2] = (byte)(value >> 16);
            Bytes[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(Bytes[address]
                | (Bytes[address + 1] << 8)
                | (Bytes[address + 2] << 16)
                | (Bytes[address + 3] << 24));
        }

        public void WriteByte(uint address, byte value)
        {
            Bytes[address] = value;
        }

        public void WritePhysicalBlock(uint address, ReadOnlySpan<byte> data)
        {
            data.CopyTo(Bytes.AsSpan((int)address));
        }

        public void ReadPhysicalBlock(uint address, Span<byte> destination)
        {
            Bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
        }
    }
}
