using System;
using System.Globalization;
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
        return new[] { x, y, z, w };
    }

    private static float[][] Texels(float r, float g, float b, float a)
    {
        return new[] { Vec(r, g, b, a), Vec(0f, 0f, 0f, 1f), Vec(0f, 0f, 0f, 1f), Vec(0f, 0f, 0f, 1f) };
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

    private static float[] RandomVec(Random rng)
    {
        float[] result = new float[4];
        for (int i = 0; i < 4; i++)
        {
            result[i] = rng.Next(8) switch
            {
                0 => 0f,
                1 => -0f,
                2 => 1f,
                3 => -1f,
                4 => float.NaN,
                5 => float.PositiveInfinity,
                6 => (float)((rng.NextDouble() * 4.0) - 2.0),
                _ => (float)(rng.NextDouble() * 4.0),
            };
        }

        return result;
    }

    [Fact]
    public void ModulateFastPath_MatchesGenericEvaluate_BitExact()
    {
        var combiner = new PshCombiner
        {
            Control = 0x00000001u,
            Final0 = 0x0000000Cu,
            Final1 = 0x00001C80u,
        };
        combiner.RgbInputs[0] = 0x08040000u;
        combiner.AlphaInputs[0] = 0x18140000u;
        combiner.RgbOutputs[0] = 0x00000C00u;
        combiner.AlphaOutputs[0] = 0x00000C00u;
        Assert.True(combiner.IsModulateT0V0Passthrough());

        float[][] consts = PshCombiner.BuildConsts(combiner);
        var rng = new Random(20261017);
        for (int i = 0; i < 4000; i++)
        {
            float[] diffuse = RandomVec(rng);
            float[] specular = RandomVec(rng);
            float[] fog = RandomVec(rng);
            var texels = new[] { RandomVec(rng), RandomVec(rng), RandomVec(rng), RandomVec(rng) };
            combiner.Evaluate(diffuse, specular, fog, texels, consts,
                out float r1, out float g1, out float b1, out float a1);
            PshCombiner.EvaluateModulateT0V0(diffuse, texels,
                out float r2, out float g2, out float b2, out float a2);
            Assert.Equal(BitConverter.SingleToInt32Bits(r1), BitConverter.SingleToInt32Bits(r2));
            Assert.Equal(BitConverter.SingleToInt32Bits(g1), BitConverter.SingleToInt32Bits(g2));
            Assert.Equal(BitConverter.SingleToInt32Bits(b1), BitConverter.SingleToInt32Bits(b2));
            Assert.Equal(BitConverter.SingleToInt32Bits(a1), BitConverter.SingleToInt32Bits(a2));
        }
    }

    [Fact]
    public void ModulateFastPath_RecognizerRequiresExactConfig()
    {
        var combiner = new PshCombiner
        {
            Control = 0x00000001u,
            Final0 = 0x0000000Cu,
            Final1 = 0x00001C80u,
        };
        combiner.RgbInputs[0] = 0x08040000u;
        combiner.AlphaInputs[0] = 0x18140000u;
        combiner.RgbOutputs[0] = 0x00000C00u;
        combiner.AlphaOutputs[0] = 0x00000C00u;
        Assert.True(combiner.IsModulateT0V0Passthrough());

        combiner.RgbInputs[0] = 0x08040001u; // C0 instead of V0
        Assert.False(combiner.IsModulateT0V0Passthrough());
        combiner.RgbInputs[0] = 0x08040000u;

        combiner.Final1 = 0x00001C81u; // one settings bit different
        Assert.False(combiner.IsModulateT0V0Passthrough());
        combiner.Final1 = 0x00001C80u;

        combiner.Control = 0x00000002u; // two stages
        Assert.False(combiner.IsModulateT0V0Passthrough());
        combiner.Control = 0x00000001u;

        combiner.Final0 = 0x0000000Du; // D = R1
        Assert.False(combiner.IsModulateT0V0Passthrough());
    }

    [Fact]
    public void LightingFastPath_MatchesGenericEvaluate_BitExact()
    {
        // stages=8 config (c=00011108): stage 0 dots C0 against expand(T1/T2).
        var eight = new PshCombiner { Control = 0x00011108u, Final0 = 0x0C0B000Fu, Final1 = 0x0C011180u };
        eight.RgbInputs[0] = 0xC149C14Au;
        eight.AlphaInputs[0] = 0xD159D15Au;
        eight.RgbOutputs[0] = 0x000C30CDu;
        eight.RgbInputs[1] = 0x0D0D0000u;
        eight.RgbOutputs[1] = 0x000000D0u;
        for (int stage = 2; stage <= 5; stage++)
        {
            eight.RgbInputs[stage] = 0xCDCD0000u;
            eight.RgbOutputs[stage] = 0x000000D0u;
        }

        eight.RgbInputs[6] = 0xC10CC20Du;
        eight.RgbOutputs[6] = 0x000005CDu;
        eight.RgbInputs[7] = 0xC40520C1u;
        eight.RgbOutputs[7] = 0x00000C00u;
        Assert.Equal(2, eight.GetSpecializedKind());
        AssertLightingMatchesGeneric(eight, 2, new Random(2001), 250);

        // stages=7 config (c=00011107): stage 0 dots expand(T0) against expand(T1/T2).
        var seven = new PshCombiner { Control = 0x00011107u, Final0 = 0x0C0B000Fu, Final1 = 0x0C011180u };
        seven.RgbInputs[0] = 0x4849484Au;
        seven.AlphaInputs[0] = 0x5859585Au;
        seven.RgbOutputs[0] = 0x000C30CDu;
        seven.RgbInputs[1] = 0x0D0D0000u;
        seven.RgbOutputs[1] = 0x000000D0u;
        for (int stage = 2; stage <= 4; stage++)
        {
            seven.RgbInputs[stage] = 0xCDCD0000u;
            seven.RgbOutputs[stage] = 0x000000D0u;
        }

        seven.RgbInputs[5] = 0xC10CC20Du;
        seven.RgbOutputs[5] = 0x000005CDu;
        seven.RgbInputs[6] = 0xC40520C1u;
        seven.RgbOutputs[6] = 0x00000C00u;
        Assert.Equal(3, seven.GetSpecializedKind());
        AssertLightingMatchesGeneric(seven, 3, new Random(2002), 250);

        // Recognizer rejects a single changed stage word.
        eight.RgbInputs[3] = 0x0D0D0000u;
        Assert.Equal(0, eight.GetSpecializedKind());
        eight.RgbInputs[3] = 0xCDCD0000u;
        Assert.Equal(2, eight.GetSpecializedKind());
    }

    private static void AssertLightingMatchesGeneric(PshCombiner combiner, int kind, Random rng, int constSets)
    {
        for (int set = 0; set < constSets; set++)
        {
            for (int i = 0; i < PshCombiner.MaxStages; i++)
            {
                combiner.Factor0[i] = (uint)rng.Next();
                combiner.Factor1[i] = (uint)rng.Next();
            }

            combiner.SpecFogFactor[0] = (uint)rng.Next();
            combiner.SpecFogFactor[1] = (uint)rng.Next();
            float[][] consts = PshCombiner.BuildConsts(combiner);
            combiner.RebuildPlan(consts);
            for (int i = 0; i < 200; i++)
            {
                float[] diffuse = RandomVec(rng);
                float[] specular = RandomVec(rng);
                float[] fog = RandomVec(rng);
                var texels = new[] { RandomVec(rng), RandomVec(rng), RandomVec(rng), RandomVec(rng) };
                combiner.Evaluate(diffuse, specular, fog, texels, consts,
                    out float r1, out float g1, out float b1, out float a1);
                PshCombiner.EvaluateLightingSpecialized(kind, diffuse, specular, texels, consts,
                    out float r2, out float g2, out float b2, out float a2);
                int r1b = BitConverter.SingleToInt32Bits(r1);
                int g1b = BitConverter.SingleToInt32Bits(g1);
                int b1b = BitConverter.SingleToInt32Bits(b1);
                int a1b = BitConverter.SingleToInt32Bits(a1);
                int r2b = BitConverter.SingleToInt32Bits(r2);
                int g2b = BitConverter.SingleToInt32Bits(g2);
                int b2b = BitConverter.SingleToInt32Bits(b2);
                int a2b = BitConverter.SingleToInt32Bits(a2);
                if (r1b != r2b || g1b != g2b || b1b != b2b || a1b != a2b)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"lighting kind={kind} divergence set={set} sample={i}: " +
                        $"generic=({r1b:X8},{g1b:X8},{b1b:X8},{a1b:X8}) " +
                        $"specialized=({r2b:X8},{g2b:X8},{b2b:X8},{a2b:X8})");
                }
            }
        }
    }

    private static void AssertPlannedMatchesGeneric(PshCombiner combiner, Random rng, int samples)
    {
        float[][] consts = PshCombiner.BuildConsts(combiner);
        combiner.RebuildPlan(consts);
        for (int i = 0; i < samples; i++)
        {
            float[] diffuse = RandomVec(rng);
            float[] specular = RandomVec(rng);
            float[] fog = RandomVec(rng);
            var texels = new[] { RandomVec(rng), RandomVec(rng), RandomVec(rng), RandomVec(rng) };
            combiner.Evaluate(diffuse, specular, fog, texels, consts,
                out float r1, out float g1, out float b1, out float a1);
            combiner.EvaluatePlanned(diffuse, specular, fog, texels, consts,
                out float r2, out float g2, out float b2, out float a2);
            int r1b = BitConverter.SingleToInt32Bits(r1);
            int g1b = BitConverter.SingleToInt32Bits(g1);
            int b1b = BitConverter.SingleToInt32Bits(b1);
            int a1b = BitConverter.SingleToInt32Bits(a1);
            int r2b = BitConverter.SingleToInt32Bits(r2);
            int g2b = BitConverter.SingleToInt32Bits(g2);
            int b2b = BitConverter.SingleToInt32Bits(b2);
            int a2b = BitConverter.SingleToInt32Bits(a2);
            if (r1b != r2b || g1b != g2b || b1b != b2b || a1b != a2b)
            {
                var words = new System.Text.StringBuilder();
                words.Append(CultureInfo.InvariantCulture,
                    $"c={combiner.Control:X8} f0={combiner.Final0:X8} f1={combiner.Final1:X8}");
                for (int stage = 0; stage < combiner.StageCount; stage++)
                {
                    words.Append(CultureInfo.InvariantCulture,
                        $" ri{stage}={combiner.RgbInputs[stage]:X8} ai{stage}={combiner.AlphaInputs[stage]:X8}" +
                        $" ro{stage}={combiner.RgbOutputs[stage]:X8} ao{stage}={combiner.AlphaOutputs[stage]:X8}");
                }

                throw new Xunit.Sdk.XunitException(
                    $"planned/generic divergence at sample {i}: {words} " +
                    $"generic=({r1b:X8},{g1b:X8},{b1b:X8},{a1b:X8}) " +
                    $"planned=({r2b:X8},{g2b:X8},{b2b:X8},{a2b:X8})");
            }
        }
    }

    private static bool CheckDivergence(PshCombiner combiner, Random rng, int samples)
    {
        float[][] consts = PshCombiner.BuildConsts(combiner);
        combiner.RebuildPlan(consts);
        for (int i = 0; i < samples; i++)
        {
            float[] diffuse = RandomVec(rng);
            float[] specular = RandomVec(rng);
            float[] fog = RandomVec(rng);
            var texels = new[] { RandomVec(rng), RandomVec(rng), RandomVec(rng), RandomVec(rng) };
            combiner.Evaluate(diffuse, specular, fog, texels, consts,
                out float r1, out float g1, out float b1, out float a1);
            combiner.EvaluatePlanned(diffuse, specular, fog, texels, consts,
                out float r2, out float g2, out float b2, out float a2);
            if (BitConverter.SingleToInt32Bits(r1) != BitConverter.SingleToInt32Bits(r2) ||
                BitConverter.SingleToInt32Bits(g1) != BitConverter.SingleToInt32Bits(g2) ||
                BitConverter.SingleToInt32Bits(b1) != BitConverter.SingleToInt32Bits(b2) ||
                BitConverter.SingleToInt32Bits(a1) != BitConverter.SingleToInt32Bits(a2))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void PlannedPath_FinalEfProdMapping_RegressionPin()
    {
        // Regression pin: the first planned-path divergence found during
        // development was a missing input mapping on the final combiner's
        // EF_PROD reads (config below: D = EfProd with UnsignedInvert). The
        // four cases isolate stages vs final and the EfProd slot.
        uint[] ri = { 0x09D1C176u, 0x1E1B4EC3u, 0x62B9630Au, 0x1076D0F5u, 0x7062236Du, 0x7675F7F3u, 0x5DC0F139u, 0x063B5164u };
        uint[] ai = { 0x2EBA3D74u, 0x14F69F7Eu, 0x3279F03Au, 0x3FB6FF90u, 0x42C89934u, 0x49CF3202u, 0x07318715u, 0x4A6FDFCDu };
        uint[] ro = { 0x0000E301u, 0x0009781Du, 0x000D039Fu, 0x0004EB9Fu, 0x0005C1D6u, 0x000A5067u, 0x000722F8u, 0x000B6605u };
        uint[] ao = { 0x0000EB97u, 0x00097B85u, 0x000D0128u, 0x0004E7ADu, 0x0005C928u, 0x000A5196u, 0x00072E5Fu, 0x000B6350u };

        // A: no final combiner -> isolates the stages.
        var noFinal = BuildConfig(8, ri, ai, ro, ao, 0u, 0u);
        Assert.False(CheckDivergence(noFinal, new Random(7), 200), "A: stages only");

        // B: single stage 0.
        var stage0 = BuildConfig(1, ri, ai, ro, ao, 0x6135E63Fu, 0x1596C3B7u);
        Assert.False(CheckDivergence(stage0, new Random(7), 200), "B: stage 0 only + final");

        // C: stage 7 only (stage count 8 with the other stage words zeroed).
        uint[] zeroRi = new uint[8];
        uint[] zeroAi = new uint[8];
        uint[] zeroRo = new uint[8];
        uint[] zeroAo = new uint[8];
        zeroRi[7] = ri[7];
        zeroRo[7] = ro[7];
        zeroAi[7] = ai[7];
        zeroAo[7] = ao[7];
        var stage7 = BuildConfig(8, zeroRi, zeroAi, zeroRo, zeroAo, 0x6135E63Fu, 0x1596C3B7u);
        Assert.False(CheckDivergence(stage7, new Random(7), 200), "C: stage 7 only + final");

        // D: final only (stage count 0).
        var finalOnly = BuildConfig(0, ri, ai, ro, ao, 0x6135E63Fu, 0x1596C3B7u);
        Assert.False(CheckDivergence(finalOnly, new Random(7), 200), "D: final only");
    }

    private static PshCombiner BuildConfig(int stages, uint[] ri, uint[] ai, uint[] ro, uint[] ao, uint final0,
        uint final1)
    {
        var combiner = new PshCombiner { Control = (uint)stages, Final0 = final0, Final1 = final1 };
        for (int s = 0; s < PshCombiner.MaxStages; s++)
        {
            combiner.RgbInputs[s] = ri[s];
            combiner.AlphaInputs[s] = ai[s];
            combiner.RgbOutputs[s] = ro[s];
            combiner.AlphaOutputs[s] = ao[s];
        }

        return combiner;
    }

    [Fact]
    public void PlannedPath_MatchesGenericEvaluate_RealConfigs()
    {
        // stages=8 config (c=00011108) the menu/triangle actually run.
        var eight = new PshCombiner
        {
            Control = 0x00011108u,
            Final0 = 0x0C0B000Fu,
            Final1 = 0x0C011180u,
        };
        eight.RgbInputs[0] = 0xC149C14Au;
        eight.AlphaInputs[0] = 0xD159D15Au;
        eight.RgbOutputs[0] = 0x000C30CDu;
        eight.AlphaOutputs[0] = 0x00000000u;
        eight.RgbInputs[1] = 0x0D0D0000u;
        eight.RgbOutputs[1] = 0x000000D0u;
        for (int stage = 2; stage <= 5; stage++)
        {
            eight.RgbInputs[stage] = 0xCDCD0000u;
            eight.RgbOutputs[stage] = 0x000000D0u;
        }

        eight.RgbInputs[6] = 0xC10CC20Du;
        eight.RgbOutputs[6] = 0x000005CDu;
        eight.RgbInputs[7] = 0xC40520C1u;
        eight.RgbOutputs[7] = 0x00000C00u;
        AssertPlannedMatchesGeneric(eight, new Random(1001), 800);

        // stages=7 variant (c=00011107).
        var seven = new PshCombiner
        {
            Control = 0x00011107u,
            Final0 = 0x0C0B000Fu,
            Final1 = 0x0C011180u,
        };
        seven.RgbInputs[0] = 0x4849484Au;
        seven.AlphaInputs[0] = 0x5859585Au;
        seven.RgbOutputs[0] = 0x000C30CDu;
        seven.AlphaOutputs[0] = 0x00000000u;
        seven.RgbInputs[1] = 0x0D0D0000u;
        seven.RgbOutputs[1] = 0x000000D0u;
        for (int stage = 2; stage <= 4; stage++)
        {
            seven.RgbInputs[stage] = 0xCDCD0000u;
            seven.RgbOutputs[stage] = 0x000000D0u;
        }

        seven.RgbInputs[5] = 0xC10CC20Du;
        seven.RgbOutputs[5] = 0x000005CDu;
        seven.RgbInputs[6] = 0xC40520C1u;
        seven.RgbOutputs[6] = 0x00000C00u;
        AssertPlannedMatchesGeneric(seven, new Random(1002), 800);

        // Randomized configurations for breadth (dots, muxes, mappings,
        // unique-C0/C1 flags, final EfProd/V1R0Sum, disabled final). Output
        // mappings are restricted to the six valid Out* encodings (the
        // generic path throws on the reserved ones, which hardware cannot
        // produce).
        int[] validMappings = { 0x00, 0x08, 0x10, 0x18, 0x20, 0x30 };
        var rng = new Random(20261018);
        for (int n = 0; n < 40; n++)
        {
            var combiner = new PshCombiner
            {
                Control = (uint)(rng.Next(0, 9) | (rng.Next(2) == 0 ? 0x10 : 0) | (rng.Next(2) == 0 ? 0x100 : 0) |
                                  (rng.Next(2) == 0 ? 0x1 : 0)),
                Final0 = (uint)rng.Next(),
                Final1 = (uint)rng.Next(),
            };
            for (int stage = 0; stage < PshCombiner.MaxStages; stage++)
            {
                uint outFlags = (uint)(validMappings[rng.Next(validMappings.Length)] |
                                       (rng.Next(2) == 0 ? 0x1 : 0) | (rng.Next(2) == 0 ? 0x2 : 0) |
                                       (rng.Next(2) == 0 ? 0x4 : 0) | (rng.Next(2) == 0 ? 0x40 : 0) |
                                       (rng.Next(2) == 0 ? 0x80 : 0));
                combiner.RgbInputs[stage] = (uint)rng.Next();
                combiner.AlphaInputs[stage] = (uint)rng.Next();
                combiner.RgbOutputs[stage] = (uint)(rng.Next() & 0x0FFF) | (outFlags << 12);
                combiner.AlphaOutputs[stage] = (uint)(rng.Next() & 0x0FFF) | (outFlags << 12);
            }

            AssertPlannedMatchesGeneric(combiner, rng, 120);
        }
    }
}
