using System;
using System.Collections.Generic;
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

    private const int MacMov = 1;
    private const int MacAdd = 3;
    private const int MuxR = 1;
    private const int MuxV = 2;
    private const int MuxC = 3;

    private static uint[] Insn(
        int mac = 0,
        int ilu = 0,
        int outR = 0,
        int outMux = 0,
        int outMask = 0,
        int outAddress = 0,
        int aMux = MuxR,
        int aR = 0,
        int bMux = MuxR,
        int bR = 0,
        int cMux = MuxR,
        int cR = 0,
        int macMask = 0,
        int iluMask = 0,
        int v = 0,
        int constIndex = 0,
        int aSwz = 0x1B,
        int bSwz = 0x1B,
        int cSwz = 0x1B,
        bool aNeg = false,
        bool bNeg = false,
        bool cNeg = false,
        bool a0X = false,
        bool final = false)
    {
        uint t1 = ((uint)ilu << 25) | ((uint)mac << 21) | ((uint)constIndex << 13) | ((uint)v << 9);
        if (aNeg)
        {
            t1 |= 1u << 8;
        }

        t1 |= (uint)(aSwz & 0xFF);

        uint t2 = ((uint)aR << 28) | ((uint)aMux << 26);
        if (bNeg)
        {
            t2 |= 1u << 25;
        }

        t2 |= (uint)((bSwz & 0xFF) << 17);
        t2 |= (uint)bR << 13;
        t2 |= (uint)bMux << 11;
        if (cNeg)
        {
            t2 |= 1u << 10;
        }

        t2 |= (uint)((cSwz & 0xFF) << 2);
        t2 |= (uint)((cR >> 2) & 3);

        uint t3 = (uint)((cR & 3) << 30);
        t3 |= (uint)cMux << 28;
        t3 |= (uint)macMask << 24;
        t3 |= (uint)outR << 20;
        t3 |= (uint)iluMask << 16;
        t3 |= (uint)outMask << 12;
        t3 |= 1u << 11;
        t3 |= (uint)outAddress << 3;
        t3 |= (uint)outMux << 2;
        if (a0X)
        {
            t3 |= 1u << 1;
        }

        if (final)
        {
            t3 |= 1u;
        }

        return new[] { 0u, t1, t2, t3 };
    }

    private static float[][] ZeroRegisters(int count)
    {
        var registers = new float[count][];
        for (int i = 0; i < count; i++)
        {
            registers[i] = new float[4];
        }

        return registers;
    }

    private static float[][] ReadConstants(Nv2AMmio nv2A)
    {
        var constants = new float[VshProgram.ConstantCount][];
        for (int i = 0; i < constants.Length; i++)
        {
            constants[i] = new float[4];
            for (int j = 0; j < 4; j++)
            {
                constants[i][j] = BitConverter.UInt32BitsToSingle(nv2A.ReadTransformConstantWord((uint)i, j));
            }
        }

        return constants;
    }

    private static void AssertPosition(float[][] outputs, float x, float y, float z, float w)
    {
        Assert.Equal(x, outputs[0][0]);
        Assert.Equal(y, outputs[0][1]);
        Assert.Equal(z, outputs[0][2]);
        Assert.Equal(w, outputs[0][3]);
    }

    private static void WriteTriangleVertices(ArrayMemory memory, uint zBits)
    {
        memory.WritePhysical(0x2000u, 0x40000000u);
        memory.WritePhysical(0x2004u, 0x40000000u);
        memory.WritePhysical(0x2008u, zBits);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41400000u);
        memory.WritePhysical(0x2014u, 0x40000000u);
        memory.WritePhysical(0x2018u, zBits);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x40000000u);
        memory.WritePhysical(0x2024u, 0x41400000u);
        memory.WritePhysical(0x2028u, zBits);
        memory.WritePhysical(0x202Cu, 0x3F800000u);
    }

    private static void PushProgramDraw(Nv2AMmio nv2A, ArrayMemory memory,
        uint c0X, uint c0Y, uint c0Z, uint c0W,
        params (uint Method, uint Parameter)[] state)
    {
        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000124u),
            (0x020Cu, 0x00400040u),
            (0x0210u, 0x00003000u),
            (0x0214u, 0x00005000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, c0X),
            (0x0B84u, c0Y),
            (0x0B88u, c0Z),
            (0x0B8Cu, c0W),
        };
        methods.AddRange(state);
        methods.Add((0x17FCu, 5u));
        methods.Add((0x1800u, 0x00010000u));
        methods.Add((0x1808u, 0x00000002u));
        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, methods.ToArray());
    }

    private static List<(uint Method, uint Parameter)> ProgramSurfaceState(bool vertexColor)
    {
        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
        };
        if (vertexColor)
        {
            methods.Add((0x0B10u, 0u));
            methods.Add((0x0B14u, 0x0020021Bu));
            methods.Add((0x0B18u, 0x0836006Cu));
            methods.Add((0x0B1Cu, 0x0000F819u));
        }

        return methods;
    }

    private static void WriteStripVertices(ArrayMemory memory)
    {
        (uint X, uint Y)[] vertices =
        {
            (0u, 0u),
            (8u, 0u),
            (0u, 8u),
            (8u, 8u),
            (15u, 8u),
            (8u, 15u),
            (15u, 15u),
        };
        for (int i = 0; i < vertices.Length; i++)
        {
            uint address = 0x2000u + ((uint)i * 16u);
            memory.WritePhysical(address, BitConverter.SingleToUInt32Bits(vertices[i].X));
            memory.WritePhysical(address + 4, BitConverter.SingleToUInt32Bits(vertices[i].Y));
            memory.WritePhysical(address + 8, 0u);
            memory.WritePhysical(address + 12, 0x3F800000u);
        }
    }

    private static List<(uint Method, uint Parameter)> StripProgramState()
    {
        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F800000u),
            (0x0B88u, 0x3F800000u),
            (0x0B8Cu, 0x3F800000u),
        };
        return methods;
    }

    private static void AddInlineVertex(List<(uint Method, uint Parameter)> methods, uint x, uint y)
    {
        methods.Add((0x1A00u, x));
        methods.Add((0x1A04u, y));
        methods.Add((0x1A08u, 0u));
        methods.Add((0x1A0Cu, 0x3F800000u));
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
    public void DrawArrays_CollectsPendingRange()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x1810u, (3u << 24) | 0u));

        Assert.Equal(1, nv2A.DrawArrayRangeCount);
        Assert.Equal(0, nv2A.InlineElementCount);
    }

    [Fact]
    public void DrawArrays_ExpandsSingleRangeOnArrayElement()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x17FCu, 6u),
            (0x1810u, (3u << 24) | 0u),
            (0x1808u, 9u));

        Assert.Equal(0, nv2A.DrawArrayRangeCount);
        Assert.Equal(5, nv2A.InlineElementCount);
        Assert.Equal(0uL, nv2A.DrawsExecuted);
    }

    [Fact]
    public void DrawArrays_ExpandsPendingRangesOnArrayElement()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        var methods = ProgramSurfaceState(vertexColor: false);
        methods.Add((0x17FCu, 6u));
        methods.Add((0x1810u, (2u << 24) | 0u));
        methods.Add((0x1810u, (2u << 24) | 4u));
        methods.Add((0x1808u, 1u));
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0, nv2A.DrawArrayRangeCount);
        Assert.Equal(4, nv2A.InlineElementCount);
    }

    [Fact]
    public void VertexData2F_WritesXYAndFinishesVertex()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x1880u, 0x3F800000u),
            (0x1884u, 0x40000000u));

        Assert.Equal(0x3F800000u, nv2A.ReadInlineValueWord(0, 0));
        Assert.Equal(0x40000000u, nv2A.ReadInlineValueWord(0, 1));
        Assert.Equal(0u, nv2A.ReadInlineValueWord(0, 2));
        Assert.Equal(0x3F800000u, nv2A.ReadInlineValueWord(0, 3));
        Assert.Equal(1, nv2A.InlineVertexCount);
    }

    [Fact]
    public void VertexData2S_WritesSignedComponentsAndFinishesVertex()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x1900u, 0xFFFE0005u));

        Assert.Equal(BitConverter.SingleToUInt32Bits(5f), nv2A.ReadInlineValueWord(0, 0));
        Assert.Equal(BitConverter.SingleToUInt32Bits(-2f), nv2A.ReadInlineValueWord(0, 1));
        Assert.Equal(0u, nv2A.ReadInlineValueWord(0, 2));
        Assert.Equal(0x3F800000u, nv2A.ReadInlineValueWord(0, 3));
        Assert.Equal(1, nv2A.InlineVertexCount);
    }

    [Fact]
    public void VertexData4Ub_WritesNormalizedBytes()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x194Cu, 0xFF8040C0u));

        Assert.Equal(BitConverter.SingleToUInt32Bits(0xC0 / 255f), nv2A.ReadInlineValueWord(3, 0));
        Assert.Equal(BitConverter.SingleToUInt32Bits(0x40 / 255f), nv2A.ReadInlineValueWord(3, 1));
        Assert.Equal(BitConverter.SingleToUInt32Bits(0x80 / 255f), nv2A.ReadInlineValueWord(3, 2));
        Assert.Equal(BitConverter.SingleToUInt32Bits(0xFF / 255f), nv2A.ReadInlineValueWord(3, 3));
        Assert.Equal(0, nv2A.InlineVertexCount);
    }

    [Fact]
    public void VertexData4S_WritesRawInt16Pairs()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x1990u, 0x0004FFFCu),
            (0x1994u, 0x00080006u));

        Assert.Equal(BitConverter.SingleToUInt32Bits(-4f), nv2A.ReadInlineValueWord(2, 0));
        Assert.Equal(BitConverter.SingleToUInt32Bits(4f), nv2A.ReadInlineValueWord(2, 1));
        Assert.Equal(BitConverter.SingleToUInt32Bits(6f), nv2A.ReadInlineValueWord(2, 2));
        Assert.Equal(BitConverter.SingleToUInt32Bits(8f), nv2A.ReadInlineValueWord(2, 3));
        Assert.Equal(0, nv2A.InlineVertexCount);
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
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
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
            (0x1760u, 0x00001042u),
            (0x1724u, 0x00002100u),
            (0x1764u, 0x00000822u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F818u),
            (0x0B20u, 0u),
            (0x0B24u, 0x0020021Bu),
            (0x0B28u, 0x0836006Cu),

            // oT0 mask 0xC selects x, y under the hardware bit order (bit 0 = w).
            (0x0B2Cu, 0x0000C849u), (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F800000u),
            (0x0B88u, 0x3F800000u),
            (0x0B8Cu, 0x3F800000u),
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x01100622u),
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

    /// <summary>
    /// Pushes a legacy (no register-combiner) texture draw: oPos from v0, a
    /// white oD0 from constant c0, and oT0 from the v1 attribute array sampled
    /// at a uniform coordinate. The texture state methods are appended by the
    /// caller.
    /// </summary>
    private static void PushTexturedLegacyDraw(
        Nv2AMmio nv2A, ArrayMemory memory, float u, float v,
        params (uint Method, uint Parameter)[] textureState)
    {
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

        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2100u + (i * 8u), BitConverter.SingleToUInt32Bits(u));
            memory.WritePhysical(0x2104u + (i * 8u), BitConverter.SingleToUInt32Bits(v));
        }

        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000010u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00040000u),
            (0x0204u, 0x00040000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1724u, 0x00002100u),
            (0x1764u, 0x00000822u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F818u),
            (0x0B20u, 0u),
            (0x0B24u, 0x0020021Bu),
            (0x0B28u, 0x0836006Cu),
            (0x0B2Cu, 0x0000C849u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F800000u),
            (0x0B88u, 0x3F800000u),
            (0x0B8Cu, 0x3F800000u),
        };
        methods.AddRange(textureState);
        methods.Add((0x17FCu, 5u));
        methods.Add((0x1800u, 0x00010000u));
        methods.Add((0x1808u, 0x00000002u));
        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, methods.ToArray());
    }

    [Fact]
    public void ElementDraw_LinearTextureSamplesTexelUnits()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x4000u, 0xFF102030u);
        memory.WritePhysical(0x4004u, 0xFF405060u);
        memory.WritePhysical(0x4008u, 0xFF708090u);
        memory.WritePhysical(0x400Cu, 0xFFA0B0C0u);

        PushTexturedLegacyDraw(
            nv2A,
            memory,
            2.5f,
            0.5f,
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00001200u),
            (0x1B08u, 0x00000002u),
            (0x1B0Cu, 0x40000000u),
            (0x1B10u, 0x00100000u),
            (0x1B1Cu, 0x00040001u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF708090u, memory.ReadPhysical(0x3018u));
    }

    [Fact]
    public void ElementDraw_SwizzledTextureUsesBaseSizeNotImageRect()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x4000u, 0xFF000001u);
        memory.WritePhysical(0x4004u, 0xFF000002u);
        memory.WritePhysical(0x4008u, 0xFF000003u);
        memory.WritePhysical(0x400Cu, 0xFF000004u);

        PushTexturedLegacyDraw(
            nv2A,
            memory,
            0.5f,
            0.25f,
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x01100600u),
            (0x1B0Cu, 0x40000000u),
            (0x1B1Cu, 0x00040004u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF000002u, memory.ReadPhysical(0x3018u));
    }

    [Fact]
    public void ElementDraw_SwizzledA4R4G4B4DecodesNibbles()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x4000u, 0x0000F8A5u);

        PushTexturedLegacyDraw(
            nv2A,
            memory,
            0.25f,
            0.25f,
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00100400u),
            (0x1B0Cu, 0x40000000u),
            (0x1B1Cu, 0x00020001u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF88AA55u, memory.ReadPhysical(0x3018u));
    }

    [Fact]
    public void ElementDraw_SwizzledX8R8G8B8ForcesOpaqueAlpha()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x4000u, 0x00112233u);

        PushTexturedLegacyDraw(
            nv2A,
            memory,
            0.5f,
            0.5f,
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00000700u),
            (0x1B0Cu, 0x40000000u),
            (0x1B1Cu, 0x00010001u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x3018u));
    }

    [Fact]
    public void ElementDraw_PerspectiveCorrectInterpolatesColor()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, 0x00000000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, 0x00000000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, 0x00000000u);
        memory.WritePhysical(0x202Cu, 0x40000000u);

        memory.WritePhysical(0x2100u, 0xFFFF0000u);
        memory.WritePhysical(0x2110u, 0xFFFF0000u);
        memory.WritePhysical(0x2120u, 0xFF0000FFu);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00001040u),
            (0x1E9Cu, 0u),
            (0x0B00u, position[0]),
            (0x0B04u, position[1]),
            (0x0B08u, position[2]),
            (0x0B0Cu, position[3]),
            (0x0B10u, color[0]),
            (0x0B14u, color[1]),
            (0x0B18u, color[2]),
            (0x0B1Cu, color[3]),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF9B0064u, memory.ReadPhysical(0x3000u + (4u * 0x40u) + (2u * 4u)));
    }

    [Fact]
    public void ElementDraw_PerspectiveCorrectSamplesTexture()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, 0x00000000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, 0x00000000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, 0x00000000u);
        memory.WritePhysical(0x202Cu, 0x40000000u);

        memory.WritePhysical(0x2200u, 0x00000000u);
        memory.WritePhysical(0x2204u, 0x00000000u);
        memory.WritePhysical(0x2208u, 0x41000000u);
        memory.WritePhysical(0x220Cu, 0x00000000u);
        memory.WritePhysical(0x2210u, 0x00000000u);
        memory.WritePhysical(0x2214u, 0x00000000u);

        memory.WritePhysical(0x2300u, 0xFFFFFFFFu);
        memory.WritePhysical(0x2310u, 0xFFFFFFFFu);
        memory.WritePhysical(0x2320u, 0xFFFFFFFFu);

        memory.WritePhysical(0x4000u, 0xFF000000u);
        memory.WritePhysical(0x4004u, 0xFF000000u);
        memory.WritePhysical(0x4008u, 0xFF112233u);
        memory.WritePhysical(0x400Cu, 0xFF445566u);
        memory.WritePhysical(0x4010u, 0xFF000000u);
        memory.WritePhysical(0x4014u, 0xFF000000u);
        memory.WritePhysical(0x4018u, 0xFF000000u);
        memory.WritePhysical(0x401Cu, 0xFF000000u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3);
        uint[] texcoord = Insn(mac: MacMov, aMux: MuxV, v: 1, outMux: 0, outMask: 0xF, outAddress: 9, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1724u, 0x00002200u),
            (0x1764u, 0x00000822u),
            (0x172Cu, 0x00002300u),
            (0x176Cu, 0x00001040u),
            (0x1E9Cu, 0u),
            (0x0B00u, position[0]),
            (0x0B04u, position[1]),
            (0x0B08u, position[2]),
            (0x0B0Cu, position[3]),
            (0x0B10u, color[0]),
            (0x0B14u, color[1]),
            (0x0B18u, color[2]),
            (0x0B1Cu, color[3]),
            (0x0B20u, texcoord[0]),
            (0x0B24u, texcoord[1]),
            (0x0B28u, texcoord[2]),
            (0x0B2Cu, texcoord[3]),
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00001200u),
            (0x1B08u, 0x00000000u),
            (0x1B0Cu, 0x40000000u),
            (0x1B10u, 0x00200000u),
            (0x1B1Cu, 0x00080001u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFF445566u, memory.ReadPhysical(0x3000u + (4u * 0x40u) + (2u * 4u)));
    }

    [Fact]
    public void ElementDraw_NaNColorBecomesOne()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        memory.WritePhysical(0x2100u, 0x7FC00000u);
        memory.WritePhysical(0x2104u, 0x00000000u);
        memory.WritePhysical(0x2108u, 0x00000000u);
        memory.WritePhysical(0x210Cu, 0x3F800000u);
        memory.WritePhysical(0x2110u, 0x7FC00000u);
        memory.WritePhysical(0x2114u, 0x00000000u);
        memory.WritePhysical(0x2118u, 0x00000000u);
        memory.WritePhysical(0x211Cu, 0x3F800000u);
        memory.WritePhysical(0x2120u, 0x7FC00000u);
        memory.WritePhysical(0x2124u, 0x00000000u);
        memory.WritePhysical(0x2128u, 0x00000000u);
        memory.WritePhysical(0x212Cu, 0x3F800000u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, position[0]),
            (0x0B04u, position[1]),
            (0x0B08u, position[2]),
            (0x0B0Cu, position[3]),
            (0x0B10u, color[0]),
            (0x0B14u, color[1]),
            (0x0B18u, color[2]),
            (0x0B1Cu, color[3]),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void DrawArrays_SeparateRangesDrawSeparatePrimitives()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteStripVertices(memory);

        var methods = StripProgramState();
        methods.Add((0x17FCu, 6u));
        methods.Add((0x1810u, (2u << 24) | 0u));
        methods.Add((0x1810u, (2u << 24) | 4u));
        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFFFFFFu, memory.ReadPhysical(0x3000u + (2u * 0x40u) + (2u * 4u)));
        Assert.Equal(0u, memory.ReadPhysical(0x3000u + (4u * 0x40u) + (4u * 4u)));
    }

    [Fact]
    public void DrawArrays_ContiguousRangesMergeIntoOnePrimitive()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteStripVertices(memory);

        var methods = StripProgramState();
        methods.Add((0x17FCu, 6u));
        methods.Add((0x1810u, (2u << 24) | 0u));
        methods.Add((0x1810u, (2u << 24) | 3u));
        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFFFFFFu, memory.ReadPhysical(0x3000u + (4u * 0x40u) + (4u * 4u)));
    }

    [Fact]
    public void InlineVertexDraw_RunsVertexProgramAndPersistsAttributes()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        var methods = ProgramSurfaceState(vertexColor: true);
        methods.Add((0x17FCu, 5u));
        methods.Add((0x1944u, 0xFF8040C0u));
        AddInlineVertex(methods, 0x3F800000u, 0x3F800000u);
        AddInlineVertex(methods, 0x41100000u, 0x3F800000u);
        AddInlineVertex(methods, 0x3F800000u, 0x41100000u);
        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFC04080u, memory.ReadPhysical(0x3000u + (2u * 0x40u) + (2u * 4u)));
    }

    [Fact]
    public void DepthTest_KillsFartherFragments()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // Z is in depth-buffer units on a Z24S8 surface (XDK viewport bake),
        // so far = 16777215.0, not a normalized 0.9.
        WriteTriangleVertices(memory, 0x4B7FFFFFu);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0x19999900u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0uL, nv2A.DrawnPixels);
        Assert.True(nv2A.DepthTestedPixels > 0);
        Assert.Equal(0x19999900u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void DepthTest_PassesNearerAndWrites()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // Z is in depth-buffer units on a Z24S8 surface (XDK viewport bake),
        // so near = 0.5 * 16777215 = 8388608.0, which stores 0x800000.
        WriteTriangleVertices(memory, 0x4B000000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0x80000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void DepthMask_DisablesDepthWrite()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x3E800000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x035Cu, 0u));

        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFFFF00u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Theory]
    [InlineData(0x4B7FFFFFu)]
    [InlineData(0x7F800000u)]
    public void DeviceDepth_Z24SaturatedShaderUnitsAreKilled(uint zBits)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // Far-plane (16777215.0) and +infinity Z both normalize to 1.0, so
        // LEQUAL against a nearer stored depth fails (documents saturation).
        WriteTriangleVertices(memory, zBits);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0x19999900u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0uL, nv2A.DrawnPixels);
        Assert.Equal(0x19999900u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void DeviceDepth_Z16NormalizesBy65535()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x47000000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                uint address = 0x5000u + (y * 0x40u) + (x * 2u);
                uint wordAddress = address & ~3u;
                int shift = (int)((address & 2u) * 8);
                uint current = memory.ReadPhysical(wordAddress);
                uint halfMask = 0xFFFFu << shift;
                memory.WritePhysical(wordAddress, (current & ~halfMask) | (0xFFFFu << shift));
            }
        }

        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000114u),
            (0x020Cu, 0x00400040u),
            (0x0210u, 0x00003000u),
            (0x0214u, 0x00005000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0u),
            (0x0B88u, 0u),
            (0x0B8Cu, 0x3F800000u),
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.DepthTestedPixels > 0);
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (4u * 4u)));
        // Row 5 of the (2,2)/(12,2)/(2,12) triangle covers x=2..8, so both
        // half-words at x=4..5 hold the normalized 32768/65535 depth 0x8000.
        Assert.Equal(0x80008000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 8u));
        Assert.Equal(0x80008000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 12u));
    }

    [Fact]
    public void ViewportBake_PassthroughShaderDepthsAreSane()
    {
        // Dashboard-shaped regression: the XDK passthrough program with the
        // driver-baked constants C0=(1,1,16777215,1) / C1=(0.53125,0.53125,0,0)
        // must yield a sane normalized depth, not a saturation kill.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x40000000u);
        memory.WritePhysical(0x2004u, 0x40000000u);
        memory.WritePhysical(0x2008u, 0x3E800000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41400000u);
        memory.WritePhysical(0x2014u, 0x40000000u);
        memory.WritePhysical(0x2018u, 0x3E800000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x40000000u);
        memory.WritePhysical(0x2024u, 0x41400000u);
        memory.WritePhysical(0x2028u, 0x3E800000u);
        memory.WritePhysical(0x202Cu, 0x3F800000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000124u),
            (0x020Cu, 0x00400040u),
            (0x0210u, 0x00003000u),
            (0x0214u, 0x00005000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836106Cu),
            (0x0B0Cu, 0x2F100FF8u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0420061Bu),
            (0x0B18u, 0x083613FCu),
            (0x0B1Cu, 0x5011F818u),
            (0x0B20u, 0u),
            (0x0B24u, 0x002008FFu),
            (0x0B28u, 0x0836106Cu),
            (0x0B2Cu, 0x2070F828u),
            (0x0B30u, 0u),
            (0x0B34u, 0x0240081Bu),
            (0x0B38u, 0x1436186Cu),
            (0x0B3Cu, 0x2F20F824u),
            (0x0B40u, 0u),
            (0x0B44u, 0x0060201Bu),
            (0x0B48u, 0x2436106Cu),
            (0x0B4Cu, 0x3070F800u),
            (0x0B50u, 0u),
            (0x0B54u, 0x00200200u),
            (0x0B58u, 0x0836106Cu),
            (0x0B5Cu, 0x2070F830u),
            (0x0B60u, 0u),
            (0x0B64u, 0x00200E1Bu),
            (0x0B68u, 0x0836106Cu),
            (0x0B6Cu, 0x2070F838u),
            (0x0B70u, 0u),
            (0x0B74u, 0x0020101Bu),
            (0x0B78u, 0x0836106Cu),
            (0x0B7Cu, 0x2070F840u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020121Bu),
            (0x0B08u, 0x0836106Cu),
            (0x0B0Cu, 0x2070F848u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020141Bu),
            (0x0B18u, 0x0836106Cu),
            (0x0B1Cu, 0x2070F850u),
            (0x0B20u, 0u),
            (0x0B24u, 0x0020161Bu),
            (0x0B28u, 0x0836106Cu),
            (0x0B2Cu, 0x2070F858u),
            (0x0B30u, 0u),
            (0x0B34u, 0x0020181Bu),
            (0x0B38u, 0x0836106Cu),
            (0x0B3Cu, 0x2070F861u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F800000u),
            (0x0B88u, 0x4B7FFFFFu),
            (0x0B8Cu, 0x3F800000u),
            (0x0B90u, 0x3F080000u),
            (0x0B94u, 0x3F080000u),
            (0x0B98u, 0u),
            (0x0B9Cu, 0u),
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.DepthTestedPixels > 0);
        Assert.Equal(0x40000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));

        // No diffuse-color assertion: this harness binds only the position
        // array, so the program's oD0 path reads unbound attributes and the
        // resulting color is a harness artifact, not guest truth.
    }

    [Fact]
    public void ViewportBake_MinimalVertDiffuseDraws()
    {
        // Bisection probe: 2-instruction program piping v0 to oPos and oD0.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x2000u, 0x40000000u);
        memory.WritePhysical(0x2004u, 0x40000000u);
        memory.WritePhysical(0x2008u, 0x3E800000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41400000u);
        memory.WritePhysical(0x2014u, 0x40000000u);
        memory.WritePhysical(0x2018u, 0x3E800000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x40000000u);
        memory.WritePhysical(0x2024u, 0x41400000u);
        memory.WritePhysical(0x2028u, 0x3E800000u);
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
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836106Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0836106Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(0xFFFFFF40u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void Blend_AlphaBlendsOverDestination()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x3000u + (y * 0x40u) + (x * 4u), 0xFF0000FFu);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F000000u,
            (0x0304u, 1u),
            (0x0344u, 0x0302u),
            (0x0348u, 0x0303u),
            (0x0350u, 0x8006u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.BlendedPixels > 0);
        Assert.Equal(0xBF800080u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void ColorMask_PreservesMaskedChannels()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x3000u + (y * 0x40u) + (x * 4u), 0xFFFFFFFFu);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x0358u, 0x00000001u));

        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFFFF00u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void AlphaTest_NeverKillsFragments()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x0300u, 1u),
            (0x033Cu, 0x0200u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0uL, nv2A.DrawnPixels);
    }

    [Fact]
    public void Stencil_ReplaceUpdatesStencilByte()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x032Cu, 1u),
            (0x0364u, 0x0207u),
            (0x0368u, 0xABu),
            (0x036Cu, 0xFFu),
            (0x0360u, 0xFFu),
            (0x0370u, 0x1E01u),
            (0x0374u, 0x1E01u),
            (0x0378u, 0x1E01u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xABu, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void FlipIncrementWrite_WrapsWriteIndex()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0128u, 2u),
            (0x0124u, 1u),
            (0x012Cu, 0u),
            (0x012Cu, 0u));

        Assert.Equal(2uL, nv2A.FramesPresented);
        Assert.Equal(1u, nv2A.FlipWriteDebug);
        Assert.Equal(2u, nv2A.FlipModuloDebug);
    }

    [Fact]
    public void FlipStall_CountsWaits()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x0130u, 0u));

        Assert.Equal(1uL, nv2A.FlipStalls);
    }

    [Fact]
    public void FlipRead_AdvancesTowardWriteOnVblank()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0128u, 2u),
            (0x0124u, 1u));
        nv2A.Advance(1_000_000_000uL);

        Assert.Equal(1u, nv2A.FlipReadDebug);
    }

    [Fact]
    public void PresentedFrame_CapturesSurfaceAtFlip()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x3000u, 0xFF112233u);
        memory.WritePhysical(0x3004u, 0x80404040u);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000008u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00020000u),
            (0x0204u, 0x00010000u),
            (0x012Cu, 0u));

        Assert.Equal(1uL, nv2A.FramesPresented);
        Assert.True(nv2A.TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(2u, width);
        Assert.Equal(1u, height);
        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0xFF, 0x40, 0x40, 0x40, 0x80 }, bgra);
    }

    [Fact]
    public void PresentedFrame_ConvertsR5G6B5()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        memory.WritePhysical(0x3000u, 0x0000F800u);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000103u),
            (0x020Cu, 0x00000002u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00010000u),
            (0x0204u, 0x00010000u),
            (0x012Cu, 0u));

        Assert.True(nv2A.TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(1u, width);
        Assert.Equal(1u, height);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, bgra);
    }

    [Fact]
    public void PresentedFrame_InvalidWithoutSurface()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(nv2A, memory, (0x012Cu, 0u));

        Assert.Equal(1uL, nv2A.FramesPresented);
        Assert.False(nv2A.TryCopyPresentedFrame(out _, out _, out _));
    }

    [Fact]
    public void ViewportScaleAndOffset_ProgramReadsAliasedConstants()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0AF0u, 0x40000000u),
            (0x0AF4u, 0x40400000u),
            (0x0AF8u, 0x40800000u),
            (0x0AFCu, 0x3F800000u),
            (0x0A20u, 0x41200000u),
            (0x0A24u, 0x41A00000u),
            (0x0A28u, 0x41F00000u),
            (0x0A2Cu, 0x42200000u));

        uint[][] program =
        {
            Insn(mac: MacMov, aMux: MuxC, constIndex: 58, outR: 0, macMask: 0xF),
            Insn(mac: MacAdd, aMux: MuxR, aR: 0, cMux: MuxC, constIndex: 59, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ReadConstants(nv2A);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 12.0f, 23.0f, 34.0f, 41.0f);
    }

    [Fact]
    public void EyePosition_WritesConstant56()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0A50u, 0x3F800000u),
            (0x0A54u, 0x40000000u),
            (0x0A58u, 0x40400000u),
            (0x0A5Cu, 0x40800000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(56, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(56, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(56, 2));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(56, 3));
    }

    [Fact]
    public void FogPlane_WritesConstant57()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x09D0u, 0x3F800000u),
            (0x09D4u, 0x40000000u),
            (0x09D8u, 0x40400000u),
            (0x09DCu, 0x40800000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(57, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(57, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(57, 2));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(57, 3));
    }

    [Fact]
    public void TexGenPlanes_WriteConstants64To91()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0840u, 0x3F800000u),
            (0x0844u, 0x40000000u),
            (0x0850u, 0x40400000u),
            (0x0860u, 0x40800000u),
            (0x0870u, 0x40A00000u),
            (0x0880u, 0x40C00000u),
            (0x0930u, 0x40E00000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(64, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(64, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(65, 0));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(66, 0));
        Assert.Equal(0x40A00000u, nv2A.ReadTransformConstantWord(67, 0));
        Assert.Equal(0x40C00000u, nv2A.ReadTransformConstantWord(72, 0));
        Assert.Equal(0x40E00000u, nv2A.ReadTransformConstantWord(91, 0));
    }

    [Fact]
    public void CompositeMatrix_WritesConstants0To3()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0680u, 0x3F800000u),
            (0x0684u, 0x40000000u),
            (0x06BCu, 0x40400000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(0, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(0, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(3, 3));
    }

    [Fact]
    public void ProjectionMatrix_WritesConstants4To7()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0440u, 0x3F800000u),
            (0x0444u, 0x40000000u),
            (0x047Cu, 0x40400000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(4, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(4, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(7, 3));
    }

    [Fact]
    public void ModelViewMatrix_WritesConstants8To39()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0480u, 0x3F800000u),
            (0x0484u, 0x40000000u),
            (0x04C0u, 0x40400000u),
            (0x057Cu, 0x40800000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(8, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(8, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(16, 0));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(35, 3));
    }

    [Fact]
    public void InverseModelViewMatrix_WritesConstants12To39()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x0580u, 0x3F800000u),
            (0x067Cu, 0x40000000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(12, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(39, 3));
    }

    [Fact]
    public void TextureMatrix_WritesConstants68To95()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x06C0u, 0x3F800000u),
            (0x06C4u, 0x40000000u),
            (0x0700u, 0x40400000u),
            (0x07BCu, 0x40800000u));

        Assert.Equal(0x3F800000u, nv2A.ReadTransformConstantWord(68, 0));
        Assert.Equal(0x40000000u, nv2A.ReadTransformConstantWord(68, 1));
        Assert.Equal(0x40400000u, nv2A.ReadTransformConstantWord(76, 0));
        Assert.Equal(0x40800000u, nv2A.ReadTransformConstantWord(95, 3));
    }

    [Fact]
    public void ElementDraw_StrideZeroUsesFirstElement()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);
        memory.WritePhysical(0x2100u, 0xFFFF0000u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00000040u),
            (0x1E9Cu, 0u),
            (0x0B00u, position[0]),
            (0x0B04u, position[1]),
            (0x0B08u, position[2]),
            (0x0B0Cu, position[3]),
            (0x0B10u, color[0]),
            (0x0B14u, color[1]),
            (0x0B18u, color[2]),
            (0x0B1Cu, color[3]),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void ElementDraw_MasksDmaSelectBitInArrayOffset()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x80002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0u),
            (0x0B88u, 0u),
            (0x0B8Cu, 0x3F800000u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFF0000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void TransformProgramStart_SelectsProgramFromStartSlot()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        uint[] firstPosition = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] firstColor = Insn(mac: MacMov, aMux: MuxC, constIndex: 0, outMux: 0, outMask: 0xF, outAddress: 3, final: true);
        uint[] secondPosition = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0);
        uint[] secondColor = Insn(mac: MacMov, aMux: MuxC, constIndex: 1, outMux: 0, outMask: 0xF, outAddress: 3, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, firstPosition[0]),
            (0x0B04u, firstPosition[1]),
            (0x0B08u, firstPosition[2]),
            (0x0B0Cu, firstPosition[3]),
            (0x0B10u, firstColor[0]),
            (0x0B14u, firstColor[1]),
            (0x0B18u, firstColor[2]),
            (0x0B1Cu, firstColor[3]),
            (0x1E9Cu, 2u),
            (0x0B00u, secondPosition[0]),
            (0x0B04u, secondPosition[1]),
            (0x0B08u, secondPosition[2]),
            (0x0B0Cu, secondPosition[3]),
            (0x0B10u, secondColor[0]),
            (0x0B14u, secondColor[1]),
            (0x0B18u, secondColor[2]),
            (0x0B1Cu, secondColor[3]),
            (0x1EA0u, 2u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0u),
            (0x0B88u, 0u),
            (0x0B8Cu, 0x3F800000u),
            (0x0B90u, 0u),
            (0x0B94u, 0x3F800000u),
            (0x0B98u, 0u),
            (0x0B9Cu, 0x3F800000u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFF00FF00u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void ElementDraw_UnwrittenColorDefaultsToOpaqueBlack()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        uint[] program = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, program[0]),
            (0x0B04u, program[1]),
            (0x0B08u, program[2]),
            (0x0B0Cu, program[3]),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFF000000u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void TransformExecutionMode_NonProgrammableModeDrawsCounted()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0u);

        uint[] program = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true);

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, program[0]),
            (0x0B04u, program[1]),
            (0x0B08u, program[2]),
            (0x0B0Cu, program[3]),
            (0x1E94u, 0u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
            (0x1E94u, 2u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(2uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.NonProgrammableModeDraws);
        Assert.True(nv2A.DrawnPixels > 0);
    }

    [Fact]
    public void Zclip_InsideBoundsDrawsWithExactDepth()
    {
        // Z24S8, vertex Z = 8388608 device units with ZCLIP [0, 16777215]:
        // no clamping engages, so the stored word matches the no-ZCLIP golden.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x4B000000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x0394u, 0x00000000u),
            (0x0398u, 0x4B7FFFFFu));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0uL, nv2A.ZclipClampedPixels);
        Assert.Equal(0x80000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void Zclip_AboveMaxClampsToMax()
    {
        // Vertex Z = 16777215 device units with ZCLIP max = 8388608: the
        // fragment depth clamps to the bound, so the stored word is 0x800000
        // (legacy saturation would have stored 0xFFFFFF).
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x4B7FFFFFu);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x0394u, 0x00000000u),
            (0x0398u, 0x4B000000u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.ZclipClampedPixels > 0);
        Assert.Equal(0x80000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void Zclip_BelowMinClampsToMin()
    {
        // Vertex Z = 0 device units with ZCLIP min = 8388608: the fragment
        // depth clamps up to the bound (legacy saturation would store 0).
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x00000000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x0394u, 0x4B000000u),
            (0x0398u, 0x4B7FFFFFu));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.ZclipClampedPixels > 0);
        Assert.Equal(0x80000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void Zclip_BoundaryMaxIsInclusive()
    {
        // Vertex Z exactly equals ZCLIP max: the bound is inclusive, so the
        // triangle draws at the bound depth with no clamp counted.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x4B000000u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x0394u, 0x00000000u),
            (0x0398u, 0x4B000000u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0uL, nv2A.ZclipClampedPixels);
        Assert.Equal(0x80000000u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void Zclip_DisabledPassthroughPreservesSaturation()
    {
        // No ZCLIP methods written: shader Z at 2x the zeta maximum keeps the
        // legacy [0,1] saturation (clamps to 1.0, LEQUAL against far passes,
        // so the triangle draws). The stored word is 0 by the pre-existing
        // exact-1.0 overflow in the fixed-depth encode
        // ((uint)(1.0 * 16777215 + 0.5) = 0x1000000, shifted out by the << 8
        // zeta pack; xemu stores 1.0 there) — pinned as-is, out of scope.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x4BFFFFFFu);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xFFFFFF00u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x030Cu, 1u),
            (0x0354u, 0x0203u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0uL, nv2A.ZclipClampedPixels);
        Assert.Equal(0u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void FloatDepth_Z16RoundTrip()
    {
        // Z16 surface with CONTROL0 Z_FORMAT: vertex Z = 255.96875 device
        // units (0.5 of F16_MAX 511.9375) LEQUAL-passes against a 1.0
        // prefill and stores the exact F16 encoding 0xEFFF per pixel pair.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x437FF800u);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                uint address = 0x5000u + (y * 0x40u) + (x * 2u);
                uint wordAddress = address & ~3u;
                int shift = (int)((address & 2u) * 8);
                uint current = memory.ReadPhysical(wordAddress);
                uint halfMask = 0xFFFFu << shift;
                memory.WritePhysical(wordAddress, (current & ~halfMask) | (0xFFF8u << shift));
            }
        }

        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000114u),
            (0x020Cu, 0x00400040u),
            (0x0210u, 0x00003000u),
            (0x0214u, 0x00005000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0u),
            (0x0B88u, 0u),
            (0x0B8Cu, 0x3F800000u),
            (0x0290u, 0x00001000u),
            (0x030Cu, 1u),
            (0x0354u, 0x0203u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, methods.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.DepthTestedPixels > 0);
        Assert.Equal(0xEFFFEFFFu, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 8u));
        Assert.Equal(0xEFFFEFFFu, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 12u));
    }

    [Fact]
    public void FloatDepth_Z24RoundTrip()
    {
        // Z24S8 surface with CONTROL0 Z_FORMAT: vertex Z = 5e29 device units
        // (0.5 of F24_MAX 1e30) LEQUAL-passes against a 1.0 prefill and
        // stores the exact F24 encoding 0xE193E5 (stencil byte preserved).
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangleVertices(memory, 0x70C9F2CAu);

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                memory.WritePhysical(0x5000u + (y * 0x40u) + (x * 4u), 0xE293E500u);
            }
        }

        PushProgramDraw(
            nv2A,
            memory,
            0x3F800000u,
            0u,
            0u,
            0x3F800000u,
            (0x0290u, 0x00001000u),
            (0x030Cu, 1u),
            (0x0354u, 0x0203u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.True(nv2A.DepthTestedPixels > 0);
        Assert.Equal(0xE193E500u, memory.ReadPhysical(0x5000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void FloatDepth_OrderingMatchesFixed()
    {
        // Float-depth LESS ordering mirrors fixed depth: with a 0.5 prefill,
        // a 0.25 draw passes and writes its exact F16 encoding, and a later
        // 0.75 draw is killed, leaving the stored word untouched.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                uint address = 0x5000u + (y * 0x40u) + (x * 2u);
                uint wordAddress = address & ~3u;
                int shift = (int)((address & 2u) * 8);
                uint current = memory.ReadPhysical(wordAddress);
                uint halfMask = 0xFFFFu << shift;
                memory.WritePhysical(wordAddress, (current & ~halfMask) | (0xEFFFu << shift));
            }
        }

        WriteTriangleVertices(memory, 0x42FFF800u);
        PushFloatDepthDraw(nv2A, memory);
        Assert.Equal(0xDFFFDFFFu, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 8u));
        ulong drawnAfterNear = nv2A.DrawnPixels;
        Assert.True(drawnAfterNear > 0);

        WriteTriangleVertices(memory, 0x43BFFA00u);
        PushFloatDepthDraw(nv2A, memory);
        Assert.Equal(0xDFFFDFFFu, memory.ReadPhysical(0x5000u + (5u * 0x40u) + 8u));
        Assert.Equal(drawnAfterNear, nv2A.DrawnPixels);
    }

    private static void PushFloatDepthDraw(Nv2AMmio nv2A, ArrayMemory memory)
    {
        var methods = new List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000114u),
            (0x020Cu, 0x00400040u),
            (0x0210u, 0x00003000u),
            (0x0214u, 0x00005000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0u),
            (0x0B88u, 0u),
            (0x0B8Cu, 0x3F800000u),
            (0x0290u, 0x00001000u),
            (0x030Cu, 1u),
            (0x0354u, 0x0201u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, methods.ToArray());
    }
}
