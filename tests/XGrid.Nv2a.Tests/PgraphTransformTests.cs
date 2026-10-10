using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class PgraphTransformTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data;

        public ArrayMemory(int size = 0x10000)
        {
            _data = new byte[size];
        }

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Base + 0x3250u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory, params (uint Method, uint Parameter)[] methods)
    {
        uint address = 0;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Base + 0x800040u, 4, address + 4);
    }

    [Fact]
    public void TransformConstantLoad_StoresConstantsAndAdvances()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x1EA4u, 5u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x40000000u),
            (0x0B88u, 0x40400000u),
            (0x0B8Cu, 0x40800000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(5, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(5, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(5, 2));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(5, 3));
        Assert.Equal(6u, nv2A.TransformConstantLoadDebug);
    }

    [Fact]
    public void TransformProgramLoad_StoresProgramAndAdvances()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x1E9Cu, 2u),
            (0x0B00u, 0xAAAA0001u),
            (0x0B04u, 0xAAAA0002u),
            (0x0B08u, 0xAAAA0003u),
            (0x0B0Cu, 0xAAAA0004u));

        Assert.Equal(0xAAAA0001u, nv2A.ReadTransformProgramWord(2, 0));
        Assert.Equal(0xAAAA0004u, nv2A.ReadTransformProgramWord(2, 3));
        Assert.Equal(3u, nv2A.TransformProgramLoadDebug);
    }

    [Fact]
    public void ArrayElement16_AppendsTwoElements()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x1800u, 0x11223344u));

        Assert.Equal(2, nv2A.InlineElementCount);
    }

    [Fact]
    public void ArrayElement32_AppendsOneElement()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x1808u, 0x11223344u));

        Assert.Equal(1, nv2A.InlineElementCount);
    }

    [Fact]
    public void DrawArrays_AppendsCountPlusOneElements()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x1810u, (3u << 24) | 0u));

        Assert.Equal(4, nv2A.InlineElementCount);
    }

    [Fact]
    public void BeginEnd_ResetsInlineBuffers()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x17FCu, 5u),
            (0x1800u, 0x00010002u),
            (0x1808u, 0x00000003u),
            (0x17FCu, 0u));

        Assert.Equal(0, nv2A.InlineElementCount);
    }
}
