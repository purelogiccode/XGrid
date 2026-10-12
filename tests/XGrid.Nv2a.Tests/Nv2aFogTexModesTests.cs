using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class Nv2aFogTexModesTests
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
    private const int MuxC = 3;

    private static uint[] Insn(
        int mac = 0,
        int outMask = 0,
        int outAddress = 0,
        int aMux = MuxR,
        int v = 0,
        int constIndex = 0,
        bool final = false)
    {
        uint t1 = ((uint)mac << 21) | ((uint)constIndex << 13) | ((uint)v << 9);
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

    private static float[][] ZeroRegisters(int count)
    {
        var registers = new float[count][];
        for (int i = 0; i < count; i++)
        {
            registers[i] = new float[4];
        }

        return registers;
    }

    private static uint F(float value)
    {
        return BitConverter.SingleToUInt32Bits(value);
    }

    private static void MethodPush(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes, uint[] instruction, uint loadBase)
    {
        for (int i = 0; i < 4; i++)
        {
            pushes.Add((loadBase + (uint)(i * 4), instruction[i]));
        }
    }

    [Fact]
    public void FogOutput_MaskedWriteFillsSignificanceOrder()
    {
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0x4, outAddress: 5, final: true) };
        VshProgram.Execute(program, 1, constants, inputs, outputs);

        // Only z masked: the most significant masked component (z) lands in oFog.x.
        Assert.Equal(3.0f, outputs[5][0]);
    }

    [Fact]
    public void FogOutput_FullMaskCopiesAll()
    {
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 5, final: true) };
        VshProgram.Execute(program, 1, constants, inputs, outputs);

        Assert.Equal(1.0f, outputs[5][0]);
        Assert.Equal(2.0f, outputs[5][1]);
        Assert.Equal(3.0f, outputs[5][2]);
        Assert.Equal(4.0f, outputs[5][3]);
    }

    [Fact]
    public void FogFactor_MatchesReferenceEquations()
    {
        Assert.Equal(0.25f, VshProgram.FogFactor(0, 1.25f, 0f, 0.25f), 5);
        Assert.Equal(1.0f, VshProgram.FogFactor(1, 1.5f, 0f, 5f), 5);
        Assert.Equal(1.0f, VshProgram.FogFactor(3, 1.5f, 0f, 5f), 5);
        Assert.Equal(1.0f, VshProgram.FogFactor(4, 0f, 0f, 10f), 5);
        Assert.Equal(1.0f, VshProgram.FogFactor(0, 0f, 0f, float.PositiveInfinity));
        Assert.Equal(1.0f, VshProgram.FogFactor(0, float.NaN, 0f, 0f));
        float exp = 1.5f + (MathF.Pow(2f, 2f * 0.5f * 16f) - 1.5f);
        Assert.Equal(exp, VshProgram.FogFactor(1, 1.5f, 0.5f, 2f), 3);
    }

    [Fact]
    public void DotMaps_AndInputTex_DecodeFromShaderCtl()
    {
        var combiner = new PshCombiner();
        combiner.DotRgbMapping = 0x0CBAu;
        combiner.OtherStageInput = 0x00210000u;

        Assert.Equal(0, combiner.DecodeDotMap(0));
        Assert.Equal(0xA, combiner.DecodeDotMap(1));
        Assert.Equal(0xB, combiner.DecodeDotMap(2));
        Assert.Equal(0xC, combiner.DecodeDotMap(3));

        Assert.Equal(-1, combiner.DecodeInputTex(0));
        Assert.Equal(0, combiner.DecodeInputTex(1));
        Assert.Equal(1, combiner.DecodeInputTex(2));
        Assert.Equal(2, combiner.DecodeInputTex(3));
    }

    [Fact]
    public void SignFunctions_MatchReference()
    {
        Assert.Equal((191f - 256f) / 127f, PshCombiner.Sign3(191f / 255f), 5);
        Assert.Equal(64f / 127f, PshCombiner.Sign3(64f / 255f), 5);
        Assert.Equal(1.0f, PshCombiner.Sign1(1f), 5);
        Assert.Equal((255f - 255.5f) / 127.5f, PshCombiner.Sign2(1f), 5);

        PshCombiner.ApplyDotMap(0, 0.2f, 0.4f, 0.6f, out float x, out float y, out float z);
        Assert.Equal(0.2f, x);
        Assert.Equal(0.4f, y);
        Assert.Equal(0.6f, z);

        PshCombiner.ApplyDotMap(5, 0.2f, 0.4f, 0.6f, out x, out y, out z);
        Assert.Equal(0.2f, x);

        Assert.True(PshCombiner.IsSupportedDotMap(0));
        Assert.True(PshCombiner.IsSupportedDotMap(3));
        Assert.False(PshCombiner.IsSupportedDotMap(4));
        Assert.False(PshCombiner.IsSupportedDotMap(7));
    }

    [Fact]
    public void ModeStageValidity_MatchesSpecTable()
    {
        Assert.True(PshCombiner.IsModeValidForStage(0x01, 0));
        Assert.True(PshCombiner.IsModeValidForStage(0x04, 3));
        Assert.False(PshCombiner.IsModeValidForStage(0x06, 0));
        Assert.True(PshCombiner.IsModeValidForStage(0x06, 1));
        Assert.True(PshCombiner.IsModeValidForStage(0x07, 3));
        Assert.False(PshCombiner.IsModeValidForStage(0x09, 1));
        Assert.True(PshCombiner.IsModeValidForStage(0x09, 2));
        Assert.True(PshCombiner.IsModeValidForStage(0x0D, 3));
        Assert.False(PshCombiner.IsModeValidForStage(0x0D, 2));
        Assert.True(PshCombiner.IsModeValidForStage(0x0F, 1));
        Assert.True(PshCombiner.IsModeValidForStage(0x10, 3));
        Assert.False(PshCombiner.IsModeValidForStage(0x10, 0));
        Assert.True(PshCombiner.IsModeValidForStage(0x11, 2));
        Assert.False(PshCombiner.IsModeValidForStage(0x11, 3));
        Assert.False(PshCombiner.IsModeValidForStage(0x02, 0));
        Assert.False(PshCombiner.IsModeValidForStage(0x13, 0));
    }

    private static void WriteTriangle(ArrayMemory memory, float z)
    {
        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, F(z));
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, F(z));
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, F(z));
        memory.WritePhysical(0x202Cu, 0x3F800000u);
    }

    private static void PushSurface(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x0208u, 0x00000104u));
        pushes.Add((0x020Cu, 0x00000040u));
        pushes.Add((0x0210u, 0x00003000u));
        pushes.Add((0x0200u, 0x00100000u));
        pushes.Add((0x0204u, 0x00100000u));
        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));
    }

    private static void PushTextureSlot(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes, int slot, uint offset, uint width, uint height)
    {
        uint slotBase = 0x1B00u + ((uint)slot * 0x40u);
        pushes.Add((slotBase, offset));
        pushes.Add((slotBase + 0x04u, 0x00001200u));
        pushes.Add((slotBase + 0x08u, 0x00000000u));
        pushes.Add((slotBase + 0x0Cu, 0x40000000u));
        pushes.Add((slotBase + 0x10u, width * 4u << 16));
        pushes.Add((slotBase + 0x1Cu, (width << 16) | height));
    }

    private static void PushPassR0(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x0288u, 0x000C0C00u));
        pushes.Add((0x028Cu, 0x00002000u));
    }

    private static void PushDraw(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));
    }

    private static uint ReadPixel(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(0x3000u + (y * 0x40u) + (x * 4u));
    }

    [Fact]
    public void Draw_FogDisabledMatchesFixedInput()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0.25f);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] fogOut = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0x4, outAddress: 5, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, fogOut, 0x0B10u);
        pushes.Add((0x1E60u, 0x01u));
        pushes.Add((0x0AC0u, 0x03200000u));
        pushes.Add((0x1E40u, 0x000000C0u));
        PushPassR0(pushes);
        pushes.Add((0x029Cu, 0x2601u));
        pushes.Add((0x02A4u, 0u));
        pushes.Add((0x02A8u, 0xFF0000FFu));
        pushes.Add((0x09C0u, F(1.25f)));
        pushes.Add((0x09C4u, F(0f)));
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
        Assert.Contains("enable=False", nv2A.FogStateDebug);
    }

    [Fact]
    public void Draw_FogEnabledBlendsTowardFogColor()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0.25f);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] fogOut = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0x4, outAddress: 5, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, fogOut, 0x0B10u);
        pushes.Add((0x1E60u, 0x01u));
        pushes.Add((0x0AC0u, 0x03200000u));
        pushes.Add((0x1E40u, 0x000000C0u));
        PushPassR0(pushes);
        pushes.Add((0x029Cu, 0x2601u));
        pushes.Add((0x02A4u, 1u));
        pushes.Add((0x02A8u, 0xFF0000FFu));
        pushes.Add((0x09C0u, F(1.25f)));
        pushes.Add((0x09C4u, F(0f)));
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 2, 2));
        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 1, 1));
        Assert.Contains("enable=True mode=0", nv2A.FogStateDebug);
    }

    [Fact]
    public void Draw_DependentArSamplesAlphaRedCoords()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0x4040BF20u);
        memory.WritePhysical(0x4100u, 0xFF000000u);
        memory.WritePhysical(0x4104u, 0xFFFFFFFFu);

        PushDependentDraw(nv2A, memory, 0x0Fu);

        // uv = (a, r) = (0.251, 0.251): x = floor(0.251 * 2) = 0 -> black texel.
        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_DependentGbSamplesGreenBlueCoords()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0x4040BF20u);
        memory.WritePhysical(0x4100u, 0xFF000000u);
        memory.WritePhysical(0x4104u, 0xFFFFFFFFu);

        PushDependentDraw(nv2A, memory, 0x10u);

        // uv = (g, b) = (0.749, 0.125): x = floor(0.749 * 2) = 1 -> white texel.
        Assert.Equal(0xFFFFFFFFu, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    private static void WriteUvs(ArrayMemory memory, float u, float v)
    {
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 8u), F(u));
            memory.WritePhysical(0x2204u + (i * 8u), F(v));
        }
    }

    private static void PushDependentDraw(Nv2AMmio nv2A, ArrayMemory memory, uint mode)
    {
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 10, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        MethodPush(pushes, tex1, 0x0B20u);
        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        PushTextureSlot(pushes, 1, 0x4100u, 2, 1);
        pushes.Add((0x1E70u, 0x01u | (mode << 5)));
        pushes.Add((0x0AC4u, 0x09200000u));
        pushes.Add((0x1E44u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x02u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_DotProductYieldsZeroTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0xFF804020u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        pushes.Add((0x1E70u, 0x01u | (0x11u << 5)));
        pushes.Add((0x0AC4u, 0x09200000u));
        pushes.Add((0x1E44u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x02u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.Equal(0uL, nv2A.UnsupportedDotMaps);
        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_UnsupportedDotMapFallsBackToIdentity()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0xFF804020u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        pushes.Add((0x1E70u, 0x01u | (0x11u << 5)));
        pushes.Add((0x1E74u, 0x00000005u));
        pushes.Add((0x0AC4u, 0x09200000u));
        pushes.Add((0x1E44u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x02u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
        Assert.True(nv2A.UnsupportedDotMaps > 0);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_DotStSamplesPreviousAndCurrentDots()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0xFFFFFFFFu);
        memory.WritePhysical(0x4200u, 0xFF000000u);
        memory.WritePhysical(0x4204u, 0xFF00FF00u);

        PushDotChainDraw(nv2A, memory, withStr3D: false);

        // d1 = dot((0.75, 0, 0), white) = 0.75; d2 = 0 -> uv (0.75, 0) selects texel 1.
        Assert.Equal(0xFF00FF00u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_DotStr3DSamplesFirstTwoDots()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0xFFFFFFFFu);
        memory.WritePhysical(0x4200u, 0xFF000000u);
        memory.WritePhysical(0x4204u, 0xFF00FF00u);
        memory.WritePhysical(0x4300u, 0xFF000000u);
        memory.WritePhysical(0x4304u, 0xFF0000FFu);

        PushDotChainDraw(nv2A, memory, withStr3D: true);

        Assert.Equal(0xFF0000FFu, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    private static void PushDotChainDraw(Nv2AMmio nv2A, ArrayMemory memory, bool withStr3D)
    {
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxC, constIndex: 0, outMask: 0xF, outAddress: 10);
        uint[] tex2 = Insn(mac: MacMov, aMux: MuxC, constIndex: 1, outMask: 0xF, outAddress: 11, final: !withStr3D);
        uint[] tex3 = Insn(mac: MacMov, aMux: MuxC, constIndex: 2, outMask: 0xF, outAddress: 12, final: withStr3D);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        MethodPush(pushes, tex1, 0x0B20u);
        MethodPush(pushes, tex2, 0x0B30u);
        if (withStr3D)
        {
            MethodPush(pushes, tex3, 0x0B40u);
        }

        pushes.Add((0x0B80u, F(0.75f)));
        pushes.Add((0x0B84u, F(0f)));
        pushes.Add((0x0B88u, F(0f)));
        pushes.Add((0x0B8Cu, F(1f)));
        pushes.Add((0x0B90u, F(0f)));
        pushes.Add((0x0B94u, F(0f)));
        pushes.Add((0x0B98u, F(0f)));
        pushes.Add((0x0B9Cu, F(1f)));
        if (withStr3D)
        {
            pushes.Add((0x0BA0u, F(0f)));
            pushes.Add((0x0BA4u, F(0f)));
            pushes.Add((0x0BA8u, F(0.9f)));
            pushes.Add((0x0BACu, F(1f)));
        }

        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        PushTextureSlot(pushes, 2, 0x4200u, 2, 1);
        if (withStr3D)
        {
            PushTextureSlot(pushes, 3, 0x4300u, 2, 1);
        }

        uint stageProgram = 0x01u | (0x11u << 5) | (0x09u << 10);
        if (withStr3D)
        {
            stageProgram |= 0x0Du << 15;
            pushes.Add((0x0ACCu, 0x0B200000u));
            pushes.Add((0x1E4Cu, 0x000000C0u));
            pushes.Add((0x1E60u, 0x04u));
        }
        else
        {
            pushes.Add((0x1E60u, 0x03u));
        }

        pushes.Add((0x1E70u, stageProgram));
        pushes.Add((0x0AC8u, 0x0A200000u));
        pushes.Add((0x1E48u, 0x000000C0u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_BumpEnvMapOffsetsCoordsByMatrix()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.5f, 0.5f);
        memory.WritePhysical(0x4000u, 0xFF0040BFu);
        memory.WritePhysical(0x410Cu, 0xFFFF0000u);

        PushBumpDraw(nv2A, memory, luminance: false);

        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_BumpEnvMapLumScalesByLuminance()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.5f, 0.5f);
        memory.WritePhysical(0x4000u, 0xFF8040BFu);
        memory.WritePhysical(0x410Cu, 0xFFFFFFFFu);

        PushBumpDraw(nv2A, memory, luminance: true);

        // White marker scaled by (1.0 * 0.502 + 0); alpha stays r0.a (t0.a = 1).
        Assert.Equal(0xFF808080u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    private static void PushBumpDraw(Nv2AMmio nv2A, ArrayMemory memory, bool luminance)
    {
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 10, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        MethodPush(pushes, tex1, 0x0B20u);
        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        PushTextureSlot(pushes, 1, 0x4100u, 4, 4);
        pushes.Add((0x1B68u, F(1f)));
        pushes.Add((0x1B6Cu, F(0f)));
        pushes.Add((0x1B70u, F(1f)));
        pushes.Add((0x1B74u, F(0f)));
        if (luminance)
        {
            pushes.Add((0x1B78u, F(1f)));
            pushes.Add((0x1B7Cu, F(0f)));
        }

        pushes.Add((0x1E70u, 0x01u | ((luminance ? 0x07u : 0x06u) << 5)));
        pushes.Add((0x0AC4u, 0x09200000u));
        pushes.Add((0x1E44u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x02u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_UnknownTexModeDegradesToNone()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, 0f);
        WriteUvs(memory, 0.25f, 0.25f);
        memory.WritePhysical(0x4000u, 0xFF804020u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        PushTextureSlot(pushes, 0, 0x4000u, 1, 1);
        pushes.Add((0x1E70u, 0x01u | (0x13u << 5)));
        pushes.Add((0x0AC4u, 0x09200000u));
        pushes.Add((0x1E44u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x02u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
        Assert.True(nv2A.TexModeFallbacks > 0);
    }
}
