using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// B3 slice-1 seam contract: the resolved state a GPU backend needs to honor
/// the dot/reflection texture modes and cube sampling reaches
/// <see cref="PgraphTextureSlot"/> / <see cref="PgraphDrawInfo"/> instead of
/// being re-derived (the §209a extent rule and the swizzle masks are exactly
/// what a backend got wrong when it guessed).
/// </summary>
public sealed class PgraphBackendCubeDotSeamTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data = new byte[0x10000];

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

    private sealed class RecordingBackend : IPgraphBackend
    {
        public long BackendUnsupported => 0;

        public string DebugSummary => "recording";

        public PgraphDrawInfo? Last { get; private set; }

        public void Method(uint subchannel, uint method, uint parameter)
        {
        }

        public void Draw(in PgraphDrawInfo info)
        {
            Last = info;
        }

        public void Clear(in PgraphClearInfo info)
        {
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
        }

        public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = [];
            return false;
        }

        public void ResetState()
        {
            Last = null;
        }
    }

    private static (Nv2AMmio Nv2A, ArrayMemory Memory) CreateChannel()
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

    private static void WriteVertices(IGpuMemory memory)
    {
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
    }

    private static void Push(Nv2AMmio nv2A, ArrayMemory memory, IEnumerable<(uint Method, uint Parameter)> stream)
    {
        const uint streamBase = 0x8000u;
        uint address = streamBase;
        foreach ((uint method, uint parameter) in stream)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, (address + 4u) | 1u);
        nv2A.Write(Base + 0x800044u, 4, streamBase);
        nv2A.Write(Base + 0x800040u, 4, address + 4u);
    }

    /// <summary>
    /// The baseline element-draw stream with the texture and eye registers the
    /// cube/dot modes read inserted before the first draw, exactly the shape
    /// the TEXFILTER seam test uses.
    /// </summary>
    private static (uint Method, uint Parameter)[] StreamWithDamage(
        uint format, uint enable, uint eyeX, uint eyeY, uint eyeZ)
    {
        var stream = new List<(uint Method, uint Parameter)>
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
            (0x0B84u, 0x3F000000u),
            (0x0B88u, 0x3E800000u),
            (0x0B8Cu, 0x3F800000u),
            // NV097_SET_EYE_POSITION (0x0A50..0x0A5C), the reflection camera.
            (0x0A50u, eyeX),
            (0x0A54u, eyeY),
            (0x0A58u, eyeZ),
            (0x0A5Cu, 0x3F800000u),
            // Slot 0: swizzled A8R8G8B8 16x16 at offset 0x100. The format word
            // carries the color id (bits 8-15), BASE_SIZE_U/V (bits 20-23 /
            // 24-27, both 4 = 16 texels) and CUBEMAPENABLE (bit 2).
            (0x1B00u, 0x00000100u),
            (0x1B04u, format),
            (0x1B08u, 0x00010101u),
            (0x1B0Cu, enable),
            (0x1B10u, 0u),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        return [.. stream];
    }

    /// <summary>A16R16F? No: A8R8G8B8 with BASE_SIZE 16x16 and CUBEMAPENABLE (bit 2).</summary>
    private const uint CubeFormat = (0x06u << 8) | (4u << 20) | (4u << 24) | 4u;

    private const uint FlatFormat = (0x06u << 8) | (4u << 20) | (4u << 24);

    [Fact]
    public void CubeSlot_CarriesFaceExtentStrideAndSwizzleMasks()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        Push(nv2A, memory, StreamWithDamage(CubeFormat, 0x40000000u, 0u, 0u, 0u));

        Assert.NotNull(backend.Last);
        PgraphTextureSlot slot = backend.Last!.Value.Texture0;
        Assert.True(slot.CubeSamplingReady);
        Assert.True(slot.MortonMasks);
        Assert.Equal(16, slot.CubeWidth);
        Assert.Equal(16, slot.CubeHeight);
        Assert.Equal(16 * 16 * 4u, slot.CubeFaceStride);
        Assert.NotEqual(0u, slot.SwizzleMaskX);
        Assert.NotEqual(0u, slot.SwizzleMaskY);
    }

    [Fact]
    public void NonCubeSlot_ReportsNoCubeLayout()
    {
        // The same texture without CUBEMAPENABLE: the seam must not claim a
        // face layout, so a backend cannot cube-sample a 2D texture.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        Push(nv2A, memory, StreamWithDamage(FlatFormat, 0x40000000u, 0u, 0u, 0u));

        Assert.NotNull(backend.Last);
        Assert.False(backend.Last!.Value.Texture0.CubeSamplingReady);
        Assert.Equal(0, backend.Last!.Value.Texture0.CubeWidth);
        Assert.Equal(0u, backend.Last!.Value.Texture0.CubeFaceStride);
    }

    [Fact]
    public void EyePosition_ReachesTheDrawRecord()
    {
        // The specular reflection mode reflects the eye vector; the camera
        // arrives as floats through NV097_SET_EYE_POSITION.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        Push(nv2A, memory, StreamWithDamage(
            FlatFormat, 0x40000000u,
            BitConverter.SingleToUInt32Bits(1.5f),
            BitConverter.SingleToUInt32Bits(-2.25f),
            BitConverter.SingleToUInt32Bits(3f)));

        Assert.NotNull(backend.Last);
        Assert.Equal(1.5f, backend.Last!.Value.EyeX);
        Assert.Equal(-2.25f, backend.Last!.Value.EyeY);
        Assert.Equal(3f, backend.Last!.Value.EyeZ);
        Assert.Equal((1.5f, -2.25f, 3f), nv2A.EyePosition);
    }

    [Fact]
    public void EyePosition_DefaultsToZeroWithNoRegisterWrite()
    {
        (Nv2AMmio nv2A, _) = CreateChannel();

        Assert.Equal((0f, 0f, 0f), nv2A.EyePosition);
    }

    /// <summary>
    /// The eye register is XF constant 56 (0x38) through the aliased writer, so
    /// a backend and the software sampler read one storage location.
    /// </summary>
    [Fact]
    public void EyeRegister_AliasesXfConstant56()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        Push(nv2A, memory, [(0x0A50u, 0x3FC00000u), (0x0A54u, 0xC0100000u)]);

        Assert.Equal((1.5f, -2.25f, 0f), nv2A.EyePosition);
        Assert.Equal(0x3FC00000u, nv2A.ReadTransformConstantWord(56, 0));
        Assert.Equal(0xC0100000u, nv2A.ReadTransformConstantWord(56, 1));
    }

    /// <summary>
    /// A cube face decodes through one decoder - <see
    /// cref="Nv2AMmio.DecodeSwizzledTexelWord"/>, the software sampler's - for
    /// every format the cube path backs, and the two byte orders stay
    /// distinct. This is the invariant the GL 2D-array cube decode relies on
    /// (it calls exactly this member per face texel).
    /// </summary>
    [Theory]
    [InlineData(0x05u)]
    [InlineData(0x06u)]
    [InlineData(0x3Au)]
    [InlineData(0x3Bu)]
    [InlineData(0x3Cu)]
    public void CubeFaceTexelWord_DecodesThroughTheSoftwareDecoder(uint format)
    {
        uint[] words = [0x00000000u, 0xFFFFFFFFu, 0x12345678u, 0x80000001u, 0x00FF00FFu];
        foreach (uint word in words)
        {
            Assert.True(Nv2AMmio.DecodeSwizzledTexelWord(format, 4u, word, out float r, out float g, out float b, out float a));
            Nv2AMmio.DecodeTextureTexel(format, word, out float er, out float eg, out float eb, out float ea);
            Assert.Equal((er, eg, eb, ea), (r, g, b, a));
        }

        // A 16-bit stage masks the upper half of the read word, so the same
        // face texel decodes identically whatever the neighbour bytes hold.
        Assert.True(Nv2AMmio.DecodeSwizzledTexelWord(0x05u, 2u, 0xDEAD1234u, out float r16, out float g16, out float b16, out float a16));
        Nv2AMmio.DecodeTextureTexel(0x05u, 0x1234u, out float er16, out float eg16, out float eb16, out float ea16);
        Assert.Equal((er16, eg16, eb16, ea16), (r16, g16, b16, a16));
    }

    /// <summary>
    /// The cube gate is the software sampler's, not the cubemap-enable bit: a
    /// cube-flagged TEXFMT whose color format
    /// <c>Nv2AMmio.TrySampleCubeDirection</c> does not back publishes no face
    /// layout, so a backend cannot decode faces the software refuses. The GL
    /// path's cube array texture is decoded from exactly these seam fields.
    /// </summary>
    /// <param name="color">Texture color-format id (TEXFMT bits 8-15).</param>
    [Theory]
    [InlineData(0x00u)]
    [InlineData(0x02u)]
    [InlineData(0x1Eu)]
    public void CubeFlaggedUnsupportedFormat_ReportsNoCubeLayout(uint color)
    {
        uint format = (color << 8) | (4u << 20) | (4u << 24) | 4u;
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        Push(nv2A, memory, StreamWithDamage(format, 0x40000000u, 0u, 0u, 0u));

        Assert.NotNull(backend.Last);
        Assert.False(backend.Last!.Value.Texture0.CubeSamplingReady);
        Assert.Equal(0u, backend.Last!.Value.Texture0.CubeFaceStride);
    }
}
