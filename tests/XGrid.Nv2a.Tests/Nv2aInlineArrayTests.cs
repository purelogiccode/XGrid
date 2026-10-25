using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// NV097_INLINE_ARRAY (0x1818) is a packed vertex buffer, not a stream of
/// pre-transformed screen-space quads: the reference decodes its words through
/// the array formats and runs the resident vertex program
/// (xemu pgraph_gl_bind_inline_array -&gt; pgraph_gl_bind_vertex_attributes with
/// inline_data=true). These tests pin the decode, the reference's per-attribute
/// alignment rule and that the program (not the raw words) decides where the
/// triangle lands.
/// </summary>
public sealed class Nv2aInlineArrayTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const int MacMov = 1;
    private const int MacAdd = 3;
    private const int MuxV = 2;
    private const int MuxC = 3;

    private sealed class ArrayMemory(int size = 0x10000) : IGpuMemory
    {
        private readonly byte[] _data = new byte[size];

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
        uint address = 0u;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Base + 0x800040u, 4, address);
    }

    private static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    /// <summary>
    /// A 16x16 A8R8G8B8 colour surface at 0x3000 (pitch 0x40) plus a resident
    /// two-instruction program: MOV oD0 = v3, MOV oPos = v0. The vertex formats
    /// are supplied by the caller because the inline array's layout is what they
    /// describe.
    /// </summary>
    private static List<(uint Method, uint Parameter)> DrawState(uint positionFormat, uint colorFormat)
    {
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3);
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true);
        return
        [
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1760u, positionFormat),
            (0x176Cu, colorFormat),
            (0x1E9Cu, 0u),
            (0x0B00u, color[0]),
            (0x0B04u, color[1]),
            (0x0B08u, color[2]),
            (0x0B0Cu, color[3]),
            (0x0B10u, position[0]),
            (0x0B14u, position[1]),
            (0x0B18u, position[2]),
            (0x0B1Cu, position[3]),
            (0x1EA4u, 0u),
        ];
    }

    private static uint ReadPixel(ArrayMemory memory, uint x, uint y) =>
        memory.ReadPhysical(0x3000u + (y * 0x40u) + (x * 4u));

    private static string Dump(ArrayMemory memory)
    {
        var text = new System.Text.StringBuilder();
        for (uint y = 0; y < 16; y++)
        {
            for (uint x = 0; x < 16; x++)
            {
                uint pixel = ReadPixel(memory, x, y);
                if (pixel != 0)
                {
                    text.Append($"({x},{y})={pixel:X8} ");
                }
            }
        }

        return text.ToString();
    }

    [Fact]
    public void InlineArray_DecodesPackedUbColorThroughTheProgram()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // slot 0 = F32x4 position, slot 3 = UB_D3D (4 packed bytes) diffuse.
        List<(uint Method, uint Parameter)> methods = DrawState(0x00000042u, 0x00000040u);
        methods.Add((0x17FCu, 5u));
        // Three vertices, 5 words each (16 bytes of position + 4 of colour).
        // 0xFFFF0000 is UB_D3D blue=00 green=00 red=FF alpha=FF, i.e. opaque red.
        (float X, float Y)[] positions = [(2f, 2f), (10f, 2f), (2f, 10f)];
        foreach ((float x, float y) in positions)
        {
            methods.Add((0x1818u, F(x)));
            methods.Add((0x1818u, F(y)));
            methods.Add((0x1818u, F(0f)));
            methods.Add((0x1818u, F(1f)));
            methods.Add((0x1818u, 0xFFFF0000u));
        }

        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, [.. methods]);

        // Before the inline-array path existed every one of these draws was
        // skipped (the format's type nibble is not F), so the screen stayed black.
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 20, $"expected the triangle's ~32 pixels, got {nv2A.DrawnPixels}; drawn: {Dump(memory)}");
        Assert.True(ReadPixel(memory, 5, 5) == 0xFFFF0000u, $"drawn: {Dump(memory)}");
        Assert.Equal(0u, ReadPixel(memory, 12, 12));
    }

    [Fact]
    public void InlineArray_StrideAlignsEachAttributeToItsComponentSize()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // slot 0 = F32x4 (16 B), slot 3 = UB_D3D (4 B), slot 9 = F32x2 (8 B).
        // The reference aligns the colour to its 1-byte size (offset 16) and the
        // texcoord to its 4-byte size (offset 20), so one vertex is 28 bytes -
        // seven words, not the five the colour-only layout uses.
        List<(uint Method, uint Parameter)> methods = DrawState(0x00000042u, 0x00000040u);
        methods.Add((0x1784u, 0x00000022u));
        methods.Add((0x17FCu, 5u));
        (float X, float Y)[] positions = [(2f, 2f), (10f, 2f), (2f, 10f)];
        foreach ((float x, float y) in positions)
        {
            methods.Add((0x1818u, F(x)));
            methods.Add((0x1818u, F(y)));
            methods.Add((0x1818u, F(0f)));
            methods.Add((0x1818u, F(1f)));
            methods.Add((0x1818u, 0xFFFF0000u));
            methods.Add((0x1818u, F(0f)));
            methods.Add((0x1818u, F(0f)));
        }

        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, [.. methods]);

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        // A 20-byte stride would read vertex 1's position out of the packed
        // colour word (0xFFFF0000 is a NaN float), so this pixel is the proof
        // the alignment rule is applied.
        Assert.True(ReadPixel(memory, 5, 5) == 0xFFFF0000u, $"drawn: {Dump(memory)}");
        Assert.Equal(0u, ReadPixel(memory, 12, 12));
    }

    [Fact]
    public void InlineArray_RunsTheResidentVertexProgramInsteadOfTheRawWords()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // slot 0 carries a degenerate triangle at the origin; the program writes
        // oPos from slot 1 instead, so the visible triangle can only appear where
        // slot 1 puts it. The old screen-space shortcut read slot 0 as the
        // position and ignored the program entirely.
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMux: 0, outMask: 0xF, outAddress: 3);
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 1, outMux: 0, outMask: 0xF, outAddress: 0, final: true);
        List<(uint Method, uint Parameter)> methods =
        [
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1760u, 0x00000042u),
            (0x1764u, 0x00000042u),
            (0x176Cu, 0x00000040u),
            (0x1E9Cu, 0u),
            (0x0B00u, color[0]),
            (0x0B04u, color[1]),
            (0x0B08u, color[2]),
            (0x0B0Cu, color[3]),
            (0x0B10u, position[0]),
            (0x0B14u, position[1]),
            (0x0B18u, position[2]),
            (0x0B1Cu, position[3]),
            (0x1EA4u, 0u),
            (0x17FCu, 5u),
        ];
        (float X, float Y)[] raw = [(0f, 0f), (1f, 0f), (0f, 1f)];
        (float X, float Y)[] program = [(2f, 2f), (10f, 2f), (2f, 10f)];
        for (int i = 0; i < 3; i++)
        {
            methods.Add((0x1818u, F(raw[i].X)));
            methods.Add((0x1818u, F(raw[i].Y)));
            methods.Add((0x1818u, F(0f)));
            methods.Add((0x1818u, F(1f)));
            methods.Add((0x1818u, F(program[i].X)));
            methods.Add((0x1818u, F(program[i].Y)));
            methods.Add((0x1818u, F(0f)));
            methods.Add((0x1818u, F(1f)));
            methods.Add((0x1818u, 0xFFFF0000u));
        }

        methods.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, [.. methods]);

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        // Inside the triangle the program's slot 1 defines, outside the raw one.
        Assert.True(ReadPixel(memory, 5, 5) == 0xFFFF0000u, $"drawn: {Dump(memory)}");
        Assert.Equal(0u, ReadPixel(memory, 12, 12));
    }

    private static uint[] Insn(
        int mac = 0,
        int ilu = 0,
        int outR = 0,
        int outMux = 0,
        int outMask = 0,
        int outAddress = 0,
        int aMux = 1,
        int aR = 0,
        int bMux = 1,
        int bR = 0,
        int cMux = 1,
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

        return [0u, t1, t2, t3];
    }
}
