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

    [Fact]
    public void ElementDraw_RunsVertexProgramAndRasterizes()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x40000000u);
        memory.WritePhysical(0x2004u, 0x40000000u);
        memory.WritePhysical(0x2008u, 0x00000000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41400000u);
        memory.WritePhysical(0x2014u, 0x40000000u);
        memory.WritePhysical(0x2018u, 0x00000000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x40000000u);
        memory.WritePhysical(0x2024u, 0x41400000u);
        memory.WritePhysical(0x2028u, 0x00000000u);
        memory.WritePhysical(0x202Cu, 0x3F800000u);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00000042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0436006Cu),
            (0x0B0Cu, 0x0000F000u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0836006Cu),
            (0x0B1Cu, 0x0000F019u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F000000u),
            (0x0B88u, 0x3E800000u),
            (0x0B8Cu, 0x3F800000u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFF8040u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void ElementDraw_SamplesSwizzledTexture()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, 0x00000000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x40800000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, 0x00000000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x40800000u);
        memory.WritePhysical(0x2028u, 0x00000000u);
        memory.WritePhysical(0x202Cu, 0x3F800000u);

        memory.WritePhysical(0x2100u, 0x00000000u);
        memory.WritePhysical(0x2104u, 0x00000000u);
        memory.WritePhysical(0x2108u, 0x3F800000u);
        memory.WritePhysical(0x210Cu, 0x00000000u);
        memory.WritePhysical(0x2110u, 0x00000000u);
        memory.WritePhysical(0x2114u, 0x3F800000u);

        memory.WritePhysical(0x4000u, 0xFFFF0000u);
        memory.WritePhysical(0x4004u, 0xFF00FF00u);
        memory.WritePhysical(0x4008u, 0xFF0000FFu);
        memory.WritePhysical(0x400Cu, 0xFFFFFFFFu);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000010u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00040000u),
            (0x0204u, 0x00040000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00000042u),
            (0x1724u, 0x00002100u),
            (0x1764u, 0x00000022u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0436006Cu),
            (0x0B0Cu, 0x0000F000u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0836006Cu),
            (0x0B1Cu, 0x0000F018u),
            (0x0B20u, 0u),
            (0x0B24u, 0x0020021Bu),
            (0x0B28u, 0x0436006Cu),
            (0x0B2Cu, 0x00003049u), (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F800000u),
            (0x0B88u, 0x3F800000u),
            (0x0B8Cu, 0x3F800000u),
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00000622u),
            (0x1B0Cu, 0x40000000u),
            (0x1B1Cu, 0x00020002u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u));
        Assert.Equal(0xFF00FF00u, memory.ReadPhysical(0x3000u + 8u));
        Assert.Equal(0xFF0000FFu, memory.ReadPhysical(0x3000u + (2u * 0x10u)));
    }
}
