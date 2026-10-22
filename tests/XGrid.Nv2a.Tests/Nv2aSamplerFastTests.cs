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

    private class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data;

        public ArrayMemory(int size = 0x10000)
        {
            _data = new byte[size];
        }

        public byte[] Bytes => _data;

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

    /// <summary>
    /// The O2/O7 fast-lane shape the production <c>PhysicalGpuMemory</c> adapter
    /// exposes (direct RAM words inside a draw batch plus the RAM array view the
    /// compiled fills index). Counts the calls each lane takes so a test can
    /// prove which lane served the pixels.
    /// </summary>
    private sealed class FastArrayMemory : ArrayMemory, IGpuMemoryFast
    {
        private bool _batch;

        public long FastReads { get; private set; }

        public long FastWrites { get; private set; }

        public long Batches { get; private set; }

        public bool RamViewAvailable { get; set; } = true;

        public void BeginGpuBatch()
        {
            _batch = true;
            Batches++;
        }

        public void EndGpuBatch() => _batch = false;

        public bool TryReadPhysical(uint address, out uint value)
        {
            if (address < (uint)Bytes.Length && address <= (uint)Bytes.Length - 4u)
            {
                value = ReadPhysical(address);
                FastReads++;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWritePhysical(uint address, uint value)
        {
            if (!_batch || address > (uint)Bytes.Length - 4u)
            {
                return false;
            }

            WritePhysical(address, value);
            FastWrites++;
            return true;
        }

        public bool TryGetRamBytes(out byte[] bytes, out uint size)
        {
            if (!RamViewAvailable)
            {
                bytes = Array.Empty<byte>();
                size = 0;
                return false;
            }

            bytes = Bytes;
            size = (uint)Bytes.Length;
            return true;
        }
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel(ArrayMemory? memory = null)
    {
        memory ??= new ArrayMemory();
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
    private const int MuxC = 3;

    private static uint[] Insn(int outAddress, bool final = false, int v = 0, int aMux = MuxV, int constIndex = 0)
    {
        uint t1 = ((uint)MacMov << 21) | ((uint)constIndex << 13) | ((uint)v << 9);
        t1 |= 0x1Bu;
        uint t2 = (uint)aMux << 26;
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

    private sealed record FillResult(
        uint[] Frame, ulong Drawn, ulong Blended, ulong Textured, ulong Samples, ulong RgbZero, long CombinerRgbZero,
        string Formats, string State, bool Compiled, string Filters);

    /// <summary>
    /// Renders the kind-1 modulate shape (single stage-0 Project2D texture,
    /// SrcAlpha/InvSrcAlpha/Add blend, opaque mask, no zeta traffic) - the
    /// compiled fill's shape - with the generic loop available as a reference.
    /// <paramref name="filter"/> programs TEXFILTER slot 0 and
    /// <paramref name="filterRegisters"/> turns the register decode on; the
    /// defaults keep the legacy nearest sampler. <paramref name="swizzled"/>
    /// binds a swizzled (normalized-coordinate) 4x4 texture, whose per-sample
    /// address runs through the Morton path instead of the linear one.
    /// </summary>
    private static FillResult RenderModulateFill(bool generic, ArrayMemory? backing = null, uint filter = 0u,
        bool filterRegisters = false, bool swizzled = false)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel(backing);
        nv2A.DiagnosticGenericFill = generic;
        nv2A.TextureFilterRegisters = filterRegisters;
        WriteTriangle(memory);
        WriteUvs(memory, 0.5f, 0.5f);

        // White diffuse through the transform constant c0.
        for (uint i = 0; i < 4; i++)
        {
            memory.WritePhysical(0xB80u + (i * 4u), 0x3F800000u);
        }

        if (swizzled)
        {
            for (uint y = 0; y < 4; y++)
            {
                for (uint x = 0; x < 4; x++)
                {
                    uint swizzledAddress = SpreadMortonBits(x) | (SpreadMortonBits(y) << 1);
                    uint a = 0x20u + (((y * 4u) + x) * 0x10u);
                    memory.WritePhysical(0x4000u + (swizzledAddress * 4u), 0xFF000000u | (a << 16) | (a << 8) | a);
                }
            }
        }
        else
        {
            for (uint i = 0; i < 4; i++)
            {
                uint a = 0x20u + (i * 0x30u);
                memory.WritePhysical(0x4000u + (i * 4u), 0xFF000000u | (a << 16) | (a << 8) | a);
            }
        }

        var pushes = new List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, Insn(0, v: 0), 0x0B00u);
        MethodPush(pushes, Insn(3, aMux: MuxC, constIndex: 0), 0x0B10u);
        MethodPush(pushes, Insn(9, final: true, v: 1), 0x0B20u);
        pushes.Add((0x1B00u, 0x4000u));
        pushes.Add((0x1B04u, swizzled ? 0x00000600u | (2u << 20) | (2u << 24) : 0x00001200u));
        pushes.Add((0x1B08u, swizzled ? 0x00010101u : 0x00010303u));
        pushes.Add((0x1B0Cu, 0x40000000u));
        pushes.Add((0x1B10u, 0x00000010u << 16));
        if (filter != 0u)
        {
            pushes.Add((0x1B14u, filter));
        }

        pushes.Add((0x1B1Cu, 0x00040004u));
        pushes.Add((0x1E70u, 0x01u));
        pushes.Add((0x1E60u, 0x01u));
        pushes.Add((0x0AC0u, 0x08040000u));
        pushes.Add((0x0260u, 0x18140000u));
        pushes.Add((0x1E40u, 0x00000C00u));
        pushes.Add((0x0AA0u, 0x00000C00u));
        pushes.Add((0x0288u, 0x0000000Cu));
        pushes.Add((0x028Cu, 0x00001C80u));
        pushes.Add((0x0304u, 1u));
        pushes.Add((0x0344u, 0x0302u));
        pushes.Add((0x0348u, 0x0303u));
        pushes.Add((0x0350u, 0x8006u));
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        var frame = new uint[0x40 * 0x40];
        for (uint y = 0; y < 0x40; y++)
        {
            for (uint x = 0; x < 0x40; x++)
            {
                frame[(y * 0x40) + x] = ReadPixel(memory, x, y);
            }
        }

        return new FillResult(
            frame,
            nv2A.DrawnPixels,
            nv2A.BlendedPixels,
            nv2A.TexturedPixels,
            nv2A.TextureSampleSuccesses,
            nv2A.TextureSampleRgbZero,
            (long)nv2A.CombinerRgbZeroFragments,
            string.Join(";", nv2A.TextureFormatAttempts),
            string.Join(";", nv2A.TextureStateStats),
            nv2A.FastKindDebug.Contains("compiled=1/", StringComparison.Ordinal),
            nv2A.TextureFilterDebug);
    }

    /// <summary>
    /// The compiled fill's direct-RAM lanes (texel read, blend destination read)
    /// must produce exactly what the adapter lane produces: same framebuffer,
    /// same counters, same sampler state. The fast memory proves the lane
    /// engaged (<see cref="FastArrayMemory.FastReads"/> stays at zero when the
    /// RAM view is withheld, which is the adapter fallback).
    /// </summary>
    [Fact]
    public void CompiledModulateFill_DirectRamLane_MatchesAdapterLane()
    {
        var direct = new FastArrayMemory();
        FillResult fast = RenderModulateFill(generic: false, direct);
        FillResult adapter = RenderModulateFill(generic: false);

        Assert.True(fast.Compiled, "the compiled modulate fill must engage");
        Assert.True(direct.FastReads > 0, "the direct RAM texel lane must serve reads");
        Assert.True(fast.Drawn > 0, "the draw must write pixels");
        Assert.True(fast.Frame.AsSpan().SequenceEqual(adapter.Frame), "framebuffer differs");
        Assert.Equal(adapter.Drawn, fast.Drawn);
        Assert.Equal(adapter.Blended, fast.Blended);
        Assert.Equal(adapter.Textured, fast.Textured);
        Assert.Equal(adapter.Samples, fast.Samples);
        Assert.Equal(adapter.RgbZero, fast.RgbZero);
        Assert.Equal(adapter.CombinerRgbZero, fast.CombinerRgbZero);
        Assert.True(string.Equals(adapter.Formats, fast.Formats, StringComparison.Ordinal),
            $"formats differ: adapter={adapter.Formats} direct={fast.Formats}");
        Assert.True(string.Equals(adapter.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  adapter={adapter.State}\n  direct={fast.State}");
    }

    /// <summary>
    /// Withholding the RAM view must fall back to the adapter lane and still
    /// produce the reference result (the compiled fill has to work on any
    /// IGpuMemory, including test fakes that never expose RAM).
    /// </summary>
    [Fact]
    public void CompiledModulateFill_NoRamView_FallsBackToAdapter()
    {
        var noView = new FastArrayMemory { RamViewAvailable = false };
        FillResult fast = RenderModulateFill(generic: false, noView);
        FillResult reference = RenderModulateFill(generic: true);

        Assert.True(fast.Compiled, "the compiled modulate fill must engage");

        // Without the RAM view the loop keeps the per-sample TryReadPhysical
        // lane (which increments FastReads) instead of the direct array read.
        Assert.True(noView.FastReads > 0, "the adapter word lane must serve the texels");
        Assert.True(noView.FastWrites > 0, "the batched write lane must still serve stores");
        Assert.True(fast.Frame.AsSpan().SequenceEqual(reference.Frame), "framebuffer differs");
        Assert.Equal(reference.Drawn, fast.Drawn);
        Assert.Equal(reference.Blended, fast.Blended);
        Assert.Equal(reference.Textured, fast.Textured);
        Assert.Equal(reference.Samples, fast.Samples);
        Assert.True(string.Equals(reference.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  reference={reference.State}\n  fallback={fast.State}");
    }


    /// <summary>
    /// The compiled modulate fill must render the texture's colours, not just
    /// write pixels: the harness's four gray texels (0x20/0x50/0x80/0xB0)
    /// modulate white diffuse and land in the frame byte-exact. (The frames
    /// compared by the tests above are only meaningful if they are not all
    /// black; this pins that.)
    /// </summary>
    [Fact]
    public void CompiledModulateFill_RendersTextureColors()
    {
        FillResult fast = RenderModulateFill(generic: false);
        var colors = new HashSet<uint>(fast.Frame);
        colors.Remove(0u);
        string[] values = new string[colors.Count];
        int index = 0;
        foreach (uint value in colors)
        {
            values[index++] = value.ToString("X8");
        }

        Array.Sort(values, StringComparer.Ordinal);
        Assert.Equal(DiagnosticModulateColors, values);
        Assert.True(fast.Compiled, "the compiled modulate fill must engage");
    }

    private static readonly string[] DiagnosticModulateColors =
    {
        "FF202020", "FF505050", "FF808080", "FFB0B0B0",
    };

    [Fact]
    public void CompiledModulateFill_MatchesGenericLoop()
    {
        FillResult fast = RenderModulateFill(generic: false);
        FillResult slow = RenderModulateFill(generic: true);

        Assert.True(slow.Drawn > 0, "the draw must write pixels");
        Assert.True(fast.Compiled, "the compiled modulate fill must engage");
        Assert.False(slow.Compiled);
        Assert.True(fast.Frame.AsSpan().SequenceEqual(slow.Frame), "framebuffer differs");
        Assert.Equal(slow.Drawn, fast.Drawn);
        Assert.Equal(slow.Blended, fast.Blended);
        Assert.Equal(slow.Textured, fast.Textured);
        Assert.Equal(slow.Samples, fast.Samples);
        Assert.Equal(slow.RgbZero, fast.RgbZero);
        Assert.Equal(slow.CombinerRgbZero, fast.CombinerRgbZero);
        Assert.True(string.Equals(slow.Formats, fast.Formats, StringComparison.Ordinal),
            $"formats differ: generic={slow.Formats} fast={fast.Formats}");
        Assert.True(string.Equals(slow.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  generic={slow.State}\n  fast={fast.State}");
    }

    /// <summary>
    /// Renders one of the two "squared lighting" shapes (kind 2 with stage 0
    /// unbound, or kind 3 with a Project2D stage 0) as the titles program it:
    /// eight-stage combiner, cube-mode stages 1/2 on unconfigured 0x07
    /// textures, a Project3D depth-format stage 3 whose shadow function is the
    /// constant-zero one, no blend/alpha test and no zeta traffic.
    /// </summary>
    private static FillResult RenderLightingFill(int kind, bool generic)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.DiagnosticGenericFill = generic;
        WriteTriangle(memory);
        WriteUvs(memory, 0.5f, 0.5f);

        // Shape constants: c0 (stage-0 A), c12/c13 (combine), c14 (add),
        // c16 (specular fog factor).
        WriteConstant(memory, 0, 0.5f, 0.25f, 0.75f, 1f);
        WriteConstant(memory, 12, 1f, 1f, 1f, 0f);
        WriteConstant(memory, 13, 0.5f, 0.5f, 0.5f, 0f);
        WriteConstant(memory, 14, 0.25f, 0.5f, 0.75f, 0f);
        WriteConstant(memory, 16, 1f, 1f, 1f, 1f);

        // Stage-0 texture for kind 3: swizzled A8R8G8B8 4x4 in Morton order.
        for (uint y = 0; y < 4; y++)
        {
            for (uint x = 0; x < 4; x++)
            {
                uint swizzled = SpreadMortonBits(x) | (SpreadMortonBits(y) << 1);
                uint a = 0x20u + ((y * 4u + x) * 0x10u);
                memory.WritePhysical(0x4000u + (swizzled * 4u), 0xFF000000u | (a << 16) | (a << 8) | a);
            }
        }

        var pushes = new List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, Insn(0, v: 0), 0x0B00u);
        MethodPush(pushes, Insn(3, aMux: MuxC, constIndex: 0), 0x0B10u);
        MethodPush(pushes, Insn(9, final: true, v: 1), 0x0B20u);

        pushes.Add((0x1B00u, 0x4000u));
        pushes.Add((0x1B04u, 0x00000600u | (2u << 20) | (2u << 24)));
        pushes.Add((0x1B08u, 0x00010101u));
        pushes.Add((0x1B0Cu, 0x40000000u));
        pushes.Add((0x1B10u, 0x00000010u << 16));
        pushes.Add((0x1B1Cu, 0x00040004u));
        for (int slot = 1; slot <= 2; slot++)
        {
            uint slotBase = 0x1B00u + ((uint)slot * 0x40u);
            pushes.Add((slotBase, 0x4100u));
            pushes.Add((slotBase + 0x04u, (0x07u << 8) | (1u << 2) | (1u << 20) | (1u << 24)));
            pushes.Add((slotBase + 0x08u, 0x00010101u));
            pushes.Add((slotBase + 0x0Cu, 0x40000000u));
            pushes.Add((slotBase + 0x10u, 0x00000004u << 16));
            pushes.Add((slotBase + 0x1Cu, 0u));
        }

        const uint slot3 = 0x1B00u + (3u * 0x40u);
        pushes.Add((slot3, 0x4200u));
        pushes.Add((slot3 + 0x04u, (0x30u << 8) | (3u << 6) | (1u << 20) | (1u << 24)));
        pushes.Add((slot3 + 0x08u, 0x00030303u));
        pushes.Add((slot3 + 0x0Cu, 0x40000000u));
        pushes.Add((slot3 + 0x10u, 0x00000200u << 16));
        pushes.Add((slot3 + 0x1Cu, 0x02000200u));

        pushes.Add((0x1E70u, kind == 2 ? 0x00010C60u : 0x00010C61u));
        pushes.Add((0x1E60u, 0x00011108u));
        pushes.Add((0x0288u, 0x0C0B000Fu));
        pushes.Add((0x028Cu, 0x0C011180u));
        uint[] rgbInputs =
        {
            0xC149C14Au, 0x0D0D0000u, 0xCDCD0000u, 0xCDCD0000u,
            0xCDCD0000u, 0xCDCD0000u, 0xC10CC20Du, 0xC40520C1u,
        };
        uint[] rgbOutputs =
        {
            0x000C30CDu, 0x000000D0u, 0x000000D0u, 0x000000D0u,
            0x000000D0u, 0x000000D0u, 0x000005CDu, 0x00000C00u,
        };
        for (uint stage = 0; stage < 8; stage++)
        {
            pushes.Add((0x0AC0u + (stage * 4u), rgbInputs[stage]));
            pushes.Add((0x1E40u + (stage * 4u), rgbOutputs[stage]));
            pushes.Add((0x0260u + (stage * 4u), stage == 0 ? 0xD159D15Au : 0u));
            pushes.Add((0x0AA0u + (stage * 4u), 0u));
        }

        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        var frame = new uint[0x40 * 0x40];
        for (uint y = 0; y < 0x40; y++)
        {
            for (uint x = 0; x < 0x40; x++)
            {
                frame[(y * 0x40) + x] = ReadPixel(memory, x, y);
            }
        }

        return new FillResult(
            frame,
            nv2A.DrawnPixels,
            nv2A.BlendedPixels,
            nv2A.TexturedPixels,
            nv2A.TextureSampleSuccesses,
            nv2A.TextureSampleRgbZero,
            (long)nv2A.CombinerRgbZeroFragments,
            string.Join(";", nv2A.TextureFormatAttempts),
            string.Join(";", nv2A.TextureStateStats),
            nv2A.FastKindDebug.Contains("compiled=1/", StringComparison.Ordinal),
            nv2A.TextureFilterDebug);
    }

    private static void WriteConstant(ArrayMemory memory, int index, float x, float y, float z, float w)
    {
        uint baseAddress = 0xB80u + ((uint)index * 0x10u);
        memory.WritePhysical(baseAddress, F(x));
        memory.WritePhysical(baseAddress + 4u, F(y));
        memory.WritePhysical(baseAddress + 8u, F(z));
        memory.WritePhysical(baseAddress + 0xCu, F(w));
    }

    private static uint SpreadMortonBits(uint value)
    {
        uint result = 0u;
        for (int bit = 0; bit < 8; bit++)
        {
            result |= ((value >> bit) & 1u) << (bit * 2);
        }

        return result;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void CompiledLightingFill_MatchesGenericLoop(int kind)
    {
        FillResult fast = RenderLightingFill(kind, generic: false);
        FillResult slow = RenderLightingFill(kind, generic: true);

        Assert.True(slow.Drawn > 0, "the draw must write pixels");
        Assert.True(fast.Compiled, "the compiled lighting fill must engage");
        Assert.False(slow.Compiled);
        Assert.True(fast.Frame.AsSpan().SequenceEqual(slow.Frame), "framebuffer differs");
        Assert.Equal(slow.Drawn, fast.Drawn);
        Assert.Equal(slow.Blended, fast.Blended);
        Assert.Equal(slow.Textured, fast.Textured);
        Assert.Equal(slow.Samples, fast.Samples);
        Assert.Equal(slow.RgbZero, fast.RgbZero);
        Assert.Equal(slow.CombinerRgbZero, fast.CombinerRgbZero);
        Assert.True(string.Equals(slow.Formats, fast.Formats, StringComparison.Ordinal),
            $"formats differ: generic={slow.Formats} fast={fast.Formats}");
        Assert.True(string.Equals(slow.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  generic={slow.State}\n  fast={fast.State}");
    }

    /// <summary>
    /// TEXFILTER MIN/MAG = TENT_LOD0 (2/2), the pairing the dashboard programs
    /// on all four slots. The compiled modulate fill must serve it - same
    /// framebuffer and same sampler accounting as the generic loop - instead of
    /// dropping to the generic loop (the pre-§210 predicate declined every
    /// bilinear plan, which is most of the 2.4x the flag used to cost).
    /// </summary>
    [Fact]
    public void CompiledModulateFill_ServesTentFilter()
    {
        const uint tent = (2u << 16) | (2u << 24);
        FillResult fast = RenderModulateFill(generic: false, filter: tent, filterRegisters: true, swizzled: true);
        FillResult slow = RenderModulateFill(generic: true, filter: tent, filterRegisters: true, swizzled: true);

        Assert.True(fast.Drawn > 0, "the draw must write pixels");
        Assert.True(fast.Compiled, "the compiled modulate fill must serve the tent filter");
        Assert.True(string.Equals(fast.Filters, "2/2/0,0/0/0,0/0/0,0/0/0", StringComparison.Ordinal),
            $"filter decode: {fast.Filters}");
        Assert.True(fast.Frame.AsSpan().SequenceEqual(slow.Frame), "framebuffer differs from the generic loop");
        Assert.Equal(slow.Drawn, fast.Drawn);
        Assert.Equal(slow.Blended, fast.Blended);
        Assert.Equal(slow.Textured, fast.Textured);
        Assert.Equal(slow.Samples, fast.Samples);
        Assert.Equal(slow.RgbZero, fast.RgbZero);
        Assert.Equal(slow.CombinerRgbZero, fast.CombinerRgbZero);
        Assert.True(string.Equals(slow.Formats, fast.Formats, StringComparison.Ordinal),
            $"formats differ: generic={slow.Formats} fast={fast.Formats}");
        Assert.True(string.Equals(slow.State, fast.State, StringComparison.Ordinal),
            $"state differs:\n  generic={slow.State}\n  fast={fast.State}");
    }

    /// <summary>
    /// A convolution kernel programmed next to an ordinary tent MIN is not a
    /// convolution: the reference only applies the kernel when MIN is
    /// CONVOLUTION_2D_LOD0 (7) (xemu <c>psh.c</c>). The dashboard's four slots
    /// are exactly this shape (2/2 + kernel 1), so the pin is that the kernel
    /// bits are ignored - same frame as the tent-only word, still compiled.
    /// </summary>
    [Fact]
    public void TextureFilter_KernelWithoutConvolutionMinIsIgnored()
    {
        const uint tent = (2u << 16) | (2u << 24);
        const uint tentPlusKernel = tent | (1u << 13);
        FillResult plain = RenderModulateFill(generic: false, filter: tent, filterRegisters: true, swizzled: true);
        FillResult kernel = RenderModulateFill(generic: false, filter: tentPlusKernel, filterRegisters: true,
            swizzled: true);

        Assert.True(kernel.Compiled, "the compiled modulate fill must still engage");
        Assert.True(string.Equals(kernel.Filters, "2/2/0,0/0/0,0/0/0,0/0/0", StringComparison.Ordinal),
            $"the kernel field must decode as inactive: {kernel.Filters}");
        Assert.True(kernel.Frame.AsSpan().SequenceEqual(plain.Frame),
            "a kernel field without CONVOLUTION_2D_LOD0 must not convolve");
        Assert.Equal(plain.Samples, kernel.Samples);
    }

    /// <summary>
    /// MIN = CONVOLUTION_2D_LOD0 (7) + QUINCUNX is the one pairing the
    /// reference convolves: nine taps per fragment instead of one, so the
    /// sampler accounting is nine times the tent's and the compiled fill
    /// declines (its transcription only knows the single-tap shape).
    /// </summary>
    [Fact]
    public void TextureFilter_ConvolutionMinAppliesKernel()
    {
        const uint tent = (2u << 16) | (2u << 24);
        const uint convolution = (7u << 16) | (2u << 24) | (1u << 13);
        FillResult plain = RenderModulateFill(generic: false, filter: tent, filterRegisters: true, swizzled: true);
        FillResult convolved = RenderModulateFill(generic: false, filter: convolution, filterRegisters: true,
            swizzled: true);
        FillResult convolvedGeneric = RenderModulateFill(generic: true, filter: convolution, filterRegisters: true,
            swizzled: true);

        Assert.False(convolved.Compiled, "a convolution must stay on the generic loop");
        Assert.True(string.Equals(convolved.Filters, "7/2/1,0/0/0,0/0/0,0/0/0", StringComparison.Ordinal),
            $"filter decode: {convolved.Filters}");
        Assert.Equal(plain.Samples * 9, convolved.Samples);
        Assert.True(convolved.Frame.AsSpan().SequenceEqual(convolvedGeneric.Frame), "framebuffer differs");
        Assert.Equal(convolvedGeneric.Samples, convolved.Samples);
    }
}

