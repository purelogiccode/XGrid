using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Differential pins for the specialized sampler path (SamplerPlan.FastKind)
/// and the compiled modulate fill: every case renders the same draw twice, once
/// with the specialized path and once with <see cref="Nv2AMmio.DiagnosticGenericSampler"/>
/// / <see cref="Nv2AMmio.DiagnosticGenericFill"/> forcing the reference path,
/// and requires the framebuffer bytes and the whole sampler/texture-state
/// accounting to be identical.
/// </summary>
public sealed class Nv2aSamplerFastTests
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
    private const int MuxV = 2;

    private static uint[] Insn(int outAddress, bool final = false, int v = 0)
    {
        uint t1 = ((uint)MacMov << 21) | ((uint)v << 9);
        t1 |= 0x1Bu;
        uint t2 = (uint)MuxV << 26;
        t2 |= 0x1Bu << 17;
        t2 |= 1 << 11;
        t2 |= 0x1Bu << 2;
        uint t3 = 0xFu << 12;
        t3 |= 1u << 11;
        t3 |= (uint)outAddress << 3;
        if (final)
        {
            t3 |= 1u;
        }

        return new[] { 0u, t1, t2, t3 };
    }

    private static uint F(float value)
    {
        return BitConverter.SingleToUInt32Bits(value);
    }

    private static void MethodPush(List<(uint Method, uint Parameter)> pushes, uint[] instruction, uint loadBase)
    {
        for (int i = 0; i < 4; i++)
        {
            pushes.Add((loadBase + (uint)(i * 4), instruction[i]));
        }
    }

    private static void WriteTriangle(ArrayMemory memory)
    {
        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, F(0.5f));
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, F(0.5f));
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, F(0.5f));
        memory.WritePhysical(0x202Cu, 0x3F800000u);
    }

    private static void WriteUvs(ArrayMemory memory, float u, float v)
    {
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 8u), F(u));
            memory.WritePhysical(0x2204u + (i * 8u), F(v));
        }
    }

    private static void PushSurface(List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x0208u, 0x00000104u));
        pushes.Add((0x020Cu, 0x00000040u));
        pushes.Add((0x0210u, 0x00003000u));
        pushes.Add((0x0200u, 0x00100000u));
        pushes.Add((0x0204u, 0x00100000u));
        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));
    }

    private static void PushPassR0(List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x0288u, 0x000C0C00u));
        pushes.Add((0x028Cu, 0x00002000u));
    }

    private static void PushDraw(List<(uint Method, uint Parameter)> pushes)
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

    private sealed record SampleResult(
        uint Pixel, ulong Samples, ulong RgbZero, ulong Disabled, ulong BadSize, ulong Textured, ulong Drawn,
        string Formats, string State);

    /// <summary>
    /// Renders one textured draw (two Project2D stages modulate-combined, the
    /// shape <c>PshCombinerTests.Draw_TwoTextureModulateCombines</c> pins) that
    /// samples both textures at the same coordinate (u, v).
    /// </summary>
    private static SampleResult Render(uint color, int width, int height, uint addressWord, float u, float v,
        bool generic)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.DiagnosticGenericSampler = generic;
        nv2A.DiagnosticGenericFill = generic;
        WriteTriangle(memory);
        WriteUvs(memory, u, v);

        // Deterministic texel patterns; the modulate product of the two
        // samples identifies which texel each stage resolved.
        uint texels = (uint)(width * height);
        for (uint i = 0; i < texels; i++)
        {
            uint a = 0x20u + (i * 8u);
            memory.WritePhysical(0x4000u + (i * 4u), 0xFF000000u | (a << 16) | (0x80u << 8) | 0x40u);
            memory.WritePhysical(0x4100u + (i * 4u), 0xFF000000u | (0xFFu << 16) | ((0x20u + (i * 8u)) << 8) | 0x40u);
        }

        uint format = (color << 8) |
                      ((uint)System.Numerics.BitOperations.Log2((uint)width) << 20) |
                      ((uint)System.Numerics.BitOperations.Log2((uint)height) << 24);
        var pushes = new List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, Insn(0, v: 0), 0x0B00u);
        MethodPush(pushes, Insn(9, v: 1), 0x0B10u);
        MethodPush(pushes, Insn(10, final: true, v: 1), 0x0B20u);
        for (int slot = 0; slot < 2; slot++)
        {
            uint slotBase = 0x1B00u + ((uint)slot * 0x40u);
            pushes.Add((slotBase, slot == 0 ? 0x4000u : 0x4100u));
            pushes.Add((slotBase + 0x04u, format));
            pushes.Add((slotBase + 0x08u, addressWord));
            pushes.Add((slotBase + 0x0Cu, 0x40000000u));
            pushes.Add((slotBase + 0x10u, ((uint)width * 4u) << 16));
            pushes.Add((slotBase + 0x1Cu, ((uint)width << 16) | (uint)height));
        }

        pushes.Add((0x1E70u, 0x21u));
        pushes.Add((0x0AC0u, 0x08090000u));
        pushes.Add((0x1E40u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x01u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        string formats = string.Join(";", nv2A.TextureFormatAttempts);
        string state = string.Join(";", nv2A.TextureStateStats);
        return new SampleResult(
            ReadPixel(memory, 2, 2),
            nv2A.TextureSampleSuccesses,
            nv2A.TextureSampleRgbZero,
            nv2A.TextureSampleDisabled,
            nv2A.TextureSampleBadSize,
            nv2A.TexturedPixels,
            nv2A.DrawnPixels,
            formats,
            state);
    }

    [Theory]
    // Linear A8R8G8B8 (0x12), 4x4, clamp addressing: texel-unit coords 5.5 and
    // -1.5 clamp to the (3, 0) corner.
    [InlineData(0x12u, 4, 4, 0x00010303u, 5.5f, -1.5f)]
    // Swizzled A8R8G8B8 (0x06), 4x4, mirror: u = 1.25 wraps back to texel 2.
    [InlineData(0x06u, 4, 4, 0x00010101u, 1.25f, 0.0f)]
    // Swizzled A4R4G4B4 (0x04), 4x4, wrap-by-mask: u = -0.25 folds to texel 3.
    [InlineData(0x04u, 4, 4, 0x00010000u, -0.25f, 0.0f)]
    // Non-standard interleave masks (1-wide texture): the generic mask loop runs.
    [InlineData(0x04u, 1, 2, 0x00010303u, 0.0f, 0.75f)]
    [InlineData(0x12u, 2, 1, 0x00010303u, 0.75f, 0.0f)]
    public void FastSampler_MatchesGenericSampler(uint color, int width, int height, uint addressWord, float u,
        float v)
    {
        SampleResult fast = Render(color, width, height, addressWord, u, v, generic: false);
        SampleResult slow = Render(color, width, height, addressWord, u, v, generic: true);

        Assert.True(fast.Samples > 0,
            $"the draw must sample the texture (pixel={fast.Pixel:X8} samples={fast.Samples} " +
            $"disabled={fast.Disabled} badsize={fast.BadSize} textured={fast.Textured} drawn={fast.Drawn} " +
            $"formats={fast.Formats} state={fast.State})");
        Assert.True(slow.Pixel == fast.Pixel,
            $"pixel differs: generic={slow.Pixel:X8} fast={fast.Pixel:X8}\n  genericState={slow.State}\n  fastState={fast.State}");
        Assert.Equal(slow.Samples, fast.Samples);
        Assert.Equal(slow.RgbZero, fast.RgbZero);
        Assert.Equal(slow.Disabled, fast.Disabled);
        Assert.Equal(slow.BadSize, fast.BadSize);
        Assert.True(string.Equals(slow.Formats, fast.Formats, StringComparison.Ordinal),
            $"formats differ: generic={slow.Formats} fast={fast.Formats}");
        Assert.True(string.Equals(slow.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  generic={slow.State}\n  fast={fast.State}");
    }
}

