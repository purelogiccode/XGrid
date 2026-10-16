using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class PshCombinerTests
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
    private const int MuxR = 1;
    private const int MuxV = 2;

    private static uint[] Insn(
        int mac = 0,
        int outMask = 0,
        int outAddress = 0,
        int aMux = MuxR,
        int v = 0,
        bool final = false)
    {
        uint t1 = ((uint)mac << 21) | ((uint)v << 9);
        t1 |= 0x1Bu;

        uint t2 = (uint)aMux << 26;
        t2 |= 0x1Bu << 17;
        t2 |= 1 << 11;
        t2 |= 0x1Bu << 2;

        uint t3 = (uint)outMask << 12;
        t3 |= 1u << 11;
        t3 |= (uint)outAddress << 3;
        if (final)
        {
            t3 |= 1u;
        }

        return new[] { 0u, t1, t2, t3 };
    }

    private static float[] Vec(float x, float y, float z, float w)
    {
        return new float[] { x, y, z, w };
    }

    private static float[][] Texels(float r, float g, float b, float a)
    {
        return new float[][] { Vec(r, g, b, a), Vec(0f, 0f, 0f, 1f), Vec(0f, 0f, 0f, 1f), Vec(0f, 0f, 0f, 1f) };
    }

    private static (float R, float G, float B, float A) Run(PshCombiner combiner, float[] diffuse, float[][] texels)
    {
        float[][] consts = PshCombiner.BuildConsts(combiner);
        combiner.Evaluate(diffuse, Vec(0f, 0f, 1f, 1f), Vec(0f, 0f, 0f, 1f), texels, consts, out float r, out float g,
            out float b, out float a);
        return (r, g, b, a);
    }

    private static void PassR0(PshCombiner combiner)
    {
        combiner.Final0 = 0x000C0C00u;
        combiner.Final1 = 0x00002000u;
    }

    [Fact]
    public void InputMappings_CoverAllEightModes()
    {
        Assert.Equal(0.75f, PshCombiner.ApplyInputMapping(0.75f, 0x00));
        Assert.Equal(0.25f, PshCombiner.ApplyInputMapping(0.75f, 0x20));
        Assert.Equal(0.5f, PshCombiner.ApplyInputMapping(0.75f, 0x40));
        Assert.Equal(-0.5f, PshCombiner.ApplyInputMapping(0.75f, 0x60));
        Assert.Equal(0.25f, PshCombiner.ApplyInputMapping(0.75f, 0x80));
        Assert.Equal(-0.25f, PshCombiner.ApplyInputMapping(0.75f, 0xA0));
        Assert.Equal(0.75f, PshCombiner.ApplyInputMapping(0.75f, 0xC0));
        Assert.Equal(-0.75f, PshCombiner.ApplyInputMapping(0.75f, 0xE0));
    }

    [Fact]
    public void OutputMappings_CoverAllSixModes()
    {
        Assert.Equal(0.75f, PshCombiner.ApplyOutputMapping(0.75f, 0x00));
        Assert.Equal(0.25f, PshCombiner.ApplyOutputMapping(0.75f, 0x08));
        Assert.Equal(1.5f, PshCombiner.ApplyOutputMapping(0.75f, 0x10));
        Assert.Equal(0.5f, PshCombiner.ApplyOutputMapping(0.75f, 0x18));
        Assert.Equal(3f, PshCombiner.ApplyOutputMapping(0.75f, 0x20));
        Assert.Equal(0.375f, PshCombiner.ApplyOutputMapping(0.75f, 0x30));
    }

    [Fact]
    public void Stage_MultiplyWritesR0()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04010000u;
        combiner.RgbOutputs[0] = 0x000000C0u;
        combiner.Factor0[0] = 0xFF808080u;
        combiner.Control = 0x01u;
        PassR0(combiner);

        (float r, float g, float b, float a) = Run(combiner, Vec(0.5f, 0.5f, 0.5f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(0.5f * (128f / 255f), r, 5);
        Assert.Equal(0.5f * (128f / 255f), g, 5);
        Assert.Equal(0.5f * (128f / 255f), b, 5);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void Stage_DotProductClampsToOne()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04010000u;
        combiner.RgbOutputs[0] = 0x000020C0u;
        combiner.Factor0[0] = 0xFFFFFFFFu;
        combiner.Control = 0x01u;
        PassR0(combiner);

        (float r, _, _, _) = Run(combiner, Vec(1f, 0.5f, 0.25f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(1f, r);
    }

    [Fact]
    public void Stage_DotProductComputesSum()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04010000u;
        combiner.RgbOutputs[0] = 0x000020C0u;
        combiner.Factor0[0] = 0xFF808080u;
        combiner.Control = 0x01u;
        PassR0(combiner);

        (float r, _, _, _) = Run(combiner, Vec(1f, 0f, 0f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(128f / 255f, r, 5);
    }

    [Fact]
    public void Stage_MuxMsbSelectsCd()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04200520u;
        combiner.RgbOutputs[0] = 0x00004C00u;
        combiner.Control = 0x0101u;
        PassR0(combiner);

        (float r, float g, float b, float a) = Run(combiner, Vec(1f, 0f, 0f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(0f, r);
        Assert.Equal(0f, g);
        Assert.Equal(1f, b);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void Stage_MuxLsbSelectsAbOnEvenAlpha()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04200520u;
        combiner.RgbOutputs[0] = 0x00004C00u;
        combiner.Control = 0x01u;
        combiner.StageProgram = 0x01u;
        PassR0(combiner);

        (float r, _, float b, _) = Run(combiner, Vec(1f, 0f, 0f, 1f), Texels(0f, 0f, 0f, 0f));

        Assert.Equal(1f, r);
        Assert.Equal(0f, b);
    }

    [Fact]
    public void Stage_AlphaModulatesToR0Alpha()
    {
        var combiner = new PshCombiner();
        combiner.AlphaInputs[0] = 0x14110000u;
        combiner.AlphaOutputs[0] = 0x000000C0u;
        combiner.Factor0[0] = 0xFFFFFFFFu;
        combiner.Control = 0x01u;
        combiner.Final0 = 0u;
        combiner.Final1 = 0x00001C00u;

        (float r, _, _, float a) = Run(combiner, Vec(1f, 1f, 1f, 0.5f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(0f, r);
        Assert.Equal(0.5f, a);
    }

    [Fact]
    public void Consts_SameC0SharedAcrossStages()
    {
        var combiner = new PshCombiner();
        combiner.RgbInputs[0] = 0x04010000u;
        combiner.RgbOutputs[0] = 0x000000C0u;
        combiner.RgbInputs[1] = 0x0C010000u;
        combiner.RgbOutputs[1] = 0x000000D0u;
        combiner.Factor0[0] = 0xFFFFFFFFu;
        combiner.Factor0[1] = 0xFFFF0000u;
        combiner.Control = 0x02u;
        combiner.Final0 = 0x000D0D00u;
        combiner.Final1 = 0x00002000u;

        (float r, float g, float b, _) = Run(combiner, Vec(0.5f, 0.5f, 0.5f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.Equal(0.5f, r);
        Assert.Equal(0.5f, g);
        Assert.Equal(0.5f, b);
    }

    [Fact]
    public void Final_LerpMixesTowardB()
    {
        var combiner = new PshCombiner();
        combiner.SpecFogFactor[0] = 0xFF0000FFu;
        combiner.SpecFogFactor[1] = 0xFF808080u;
        combiner.Control = 0u;
        // A = C1.rgb (gray 0x80 -> ~0.5 lerp factor), B = C0 (blue),
        // C = V0 (red), D = ZERO, G = ONE.
        combiner.Final0 = 0x02010400u;
        combiner.Final1 = 0x00002000u;

        (float r, float g, float b, float a) = Run(combiner, Vec(1f, 0f, 0f, 1f), Texels(0f, 0f, 0f, 1f));

        Assert.InRange(r, 0.49, 0.51);
        Assert.Equal(0f, g);
        Assert.InRange(b, 0.49, 0.52);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void Final_EfProdMultipliesEAndF()
    {
        var combiner = new PshCombiner();
        combiner.SpecFogFactor[0] = 0xFF808080u;
        combiner.SpecFogFactor[1] = 0xFF808080u;
        combiner.Control = 0u;
        combiner.Final0 = 0x200F0F00u;
        combiner.Final1 = 0x01022000u;

        (float r, _, _, _) = Run(combiner, Vec(0f, 0f, 0f, 1f), Texels(0f, 0f, 0f, 1f));

        float expected = (128f / 255f) * (128f / 255f);
        Assert.Equal(expected, r, 4);
    }

    [Fact]
    public void CombinerMethods_ParseIntoState()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        PushMethods(
            nv2A,
            memory,
            (0x1E60u, 2u),
            (0x1E70u, 0x21u),
            (0x0AC0u, 0x08090000u),
            (0x0288u, 0x000C0C00u),
            (0x028Cu, 0x00002000u));

        Assert.Equal("stages=2 control=00000002 final=True draws=0 texmodes=1100", nv2A.CombinerStateDebug);
    }

    [Fact]
    public void Draw_CombinerDisabledMatchesLegacyGolden()
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

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] color = Insn(mac: MacMov, aMux: MuxV, v: 3, outMask: 0xF, outAddress: 3);
        uint[] texcoord = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9, final: true);

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
            (0x1E60u, 0u),
            (0x0288u, 0u),
            (0x028Cu, 0u),
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
        Assert.Equal(0uL, nv2A.CombinerDraws);
        Assert.Equal(0xFF445566u, memory.ReadPhysical(0x3000u + (4u * 0x40u) + (2u * 4u)));
    }

    [Fact]
    public void Draw_TwoTextureModulateCombines()
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
        memory.WritePhysical(0x202Cu, 0x3F800000u);

        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 8u), 0x3E800000u);
            memory.WritePhysical(0x2204u + (i * 8u), 0x3E800000u);
        }

        memory.WritePhysical(0x4000u, 0xFF808080u);
        memory.WritePhysical(0x4100u, 0xFF404040u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 10, final: true);

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
            (0x1E9Cu, 0u),
            (0x0B00u, position[0]),
            (0x0B04u, position[1]),
            (0x0B08u, position[2]),
            (0x0B0Cu, position[3]),
            (0x0B10u, tex0[0]),
            (0x0B14u, tex0[1]),
            (0x0B18u, tex0[2]),
            (0x0B1Cu, tex0[3]),
            (0x0B20u, tex1[0]),
            (0x0B24u, tex1[1]),
            (0x0B28u, tex1[2]),
            (0x0B2Cu, tex1[3]),
            (0x1B00u, 0x00004000u),
            (0x1B04u, 0x00001200u),
            (0x1B08u, 0x00000000u),
            (0x1B0Cu, 0x40000000u),
            (0x1B10u, 0x00080000u),
            (0x1B1Cu, 0x00020002u),
            (0x1B40u, 0x00004100u),
            (0x1B44u, 0x00001200u),
            (0x1B48u, 0x00000000u),
            (0x1B4Cu, 0x40000000u),
            (0x1B50u, 0x00080000u),
            (0x1B5Cu, 0x00020002u),
            (0x1E70u, 0x21u),
            (0x0AC0u, 0x08090000u),
            (0x1E40u, 0x000000C0u),
            (0x1E60u, 0x01u),
            (0x0288u, 0x000C0C00u),
            (0x028Cu, 0x00002000u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u));

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.True(nv2A.TexturedPixels > 0);
        Assert.Equal(0xFF202020u, memory.ReadPhysical(0x3000u + (2u * 0x40u) + (2u * 4u)));
        Assert.Equal(0xFF202020u, memory.ReadPhysical(0x3000u + (1u * 0x40u) + (1u * 4u)));
    }
}
