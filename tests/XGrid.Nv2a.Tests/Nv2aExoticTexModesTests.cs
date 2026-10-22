using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Covers the NV2A "exotic" texture modes implemented alongside the HILO
/// hemisphere dot maps: cubemap sampling (CUBEMAPENABLE and the unfolded
/// remap2DToCube path), projective 3D volume sampling, CLIPPLANE discard,
/// the reflection dot-chain modes, and the xemu-equivalent BRDF stub.
/// Mode equations follow xemu hw/xbox/nv2a/pgraph/glsl/psh.c; the hemisphere
/// dot decode follows the Cxbx 16-bit sign1/sign2/sign3 extensions.
/// </summary>
public sealed class Nv2AExoticTexModesTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const int MacMov = 1;
    private const int MuxR = 1;
    private const int MuxV = 2;
    private const int MuxC = 3;

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

    private static uint F(float value)
    {
        return BitConverter.SingleToUInt32Bits(value);
    }

    private static void MethodPush(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        uint[] instruction, uint loadBase)
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

    private static void PushDraw(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));
    }

    private static void PushPassR0(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        pushes.Add((0x0288u, 0x000C0C00u));
        pushes.Add((0x028Cu, 0x00002000u));
    }

    private static uint ReadPixel(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(0x3000u + (y * 0x40u) + (x * 4u));
    }

    /// <summary>One-texcoord vertex program: oT0 = v1, position = v0.</summary>
    private static void PushTexcoordProgram(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes)
    {
        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9, final: true);
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
    }

    private static void PushTexcoordArray(
        System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        ArrayMemory memory,
        int attribute,
        uint address,
        float x,
        float y,
        float z,
        float w)
    {
        pushes.Add((0x1720u + (uint)(attribute * 4), address));
        pushes.Add((0x1760u + (uint)(attribute * 4), 0x00001042u));
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(address + (i * 16u), BitConverter.SingleToUInt32Bits(x));
            memory.WritePhysical(address + (i * 16u) + 4u, BitConverter.SingleToUInt32Bits(y));
            memory.WritePhysical(address + (i * 16u) + 8u, BitConverter.SingleToUInt32Bits(z));
            memory.WritePhysical(address + (i * 16u) + 12u, BitConverter.SingleToUInt32Bits(w));
        }
    }

    /// <summary>Swizzled A8R8G8B8 slot with the given TEXFMT value and extent.</summary>
    private static void PushFormatTextureSlot(
        System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        int slot,
        uint offset,
        uint format,
        uint address,
        uint width,
        uint height)
    {
        uint slotBase = 0x1B00u + ((uint)slot * 0x40u);
        pushes.Add((slotBase, offset));
        pushes.Add((slotBase + 0x04u, format));
        pushes.Add((slotBase + 0x08u, address));
        pushes.Add((slotBase + 0x0Cu, 0x40000000u));
        pushes.Add((slotBase + 0x1Cu, (width << 16) | height));
    }


    /// <summary>
    /// Builds a swizzled cube TEXFMT word the way xgu/nxdk programs it:
    /// mip levels, dimensionality 2, cubemap enable, color format and the
    /// BASE_SIZE_U/V log2 extents (xemu reads a swizzled texture's extent
    /// from these, never from TEXIMAGERECT).
    /// </summary>
    private static uint CubeFormat(uint color, int logU, int logV, uint levels = 1u)
    {
        return Swizzled2DFormat(color, logU, logV, levels) | 4u;
    }

    /// <summary>Swizzled 2D TEXFMT word (no cubemap bit).</summary>
    private static uint Swizzled2DFormat(uint color, int logU, int logV, uint levels = 1u)
    {
        return (levels << 16) | (2u << 6) | (color << 8) | ((uint)logU << 20) | ((uint)logV << 24);
    }

    /// <summary>
    /// Combiner that writes stage N's texel to R0 (A = TN, B = 1), final
    /// combiner passes R0. Stage modes below N are left at NONE.
    /// </summary>
    private static void PushStagePassTexel(
        System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        int stage,
        uint mode)
    {
        pushes.Add((0x1E70u, mode << (stage * 5)));
        pushes.Add((0x0AC0u + (uint)(stage * 4), ((uint)(0x08 + stage) << 24) | 0x00200000u));
        pushes.Add((0x1E40u + (uint)(stage * 4), 0x000000C0u));
        pushes.Add((0x1E60u, (uint)(stage + 1)));
        PushPassR0(pushes);
    }

    [Fact]
    public void CubeHelpers_MatchReferenceConventions()
    {
        Assert.Equal(0, PshCombiner.SelectCubeFace(1f, 0f, 0f, out float s, out float t));
        Assert.Equal(0.5f, s);
        Assert.Equal(0.5f, t);

        Assert.Equal(1, PshCombiner.SelectCubeFace(-1f, 0f, 0f, out s, out t));
        Assert.Equal(2, PshCombiner.SelectCubeFace(0f, 1f, 0f, out s, out t));
        Assert.Equal(3, PshCombiner.SelectCubeFace(0f, -1f, 0f, out s, out t));
        Assert.Equal(4, PshCombiner.SelectCubeFace(0f, 0f, 1f, out s, out t));
        Assert.Equal(5, PshCombiner.SelectCubeFace(0f, 0f, -1f, out s, out t));

        // +Y face: s = (x / |y| + 1)/2, t = (z / |y| + 1)/2; direction
        // (0.5, 1, 0) lands at (0.75, 0.5).
        Assert.Equal(2, PshCombiner.SelectCubeFace(0.5f, 1f, 0f, out s, out t));
        Assert.Equal(0.75f, s);
        Assert.Equal(0.5f, t);

        PshCombiner.CubeRemapTo2D(1f, 0.5f, 0f, out float u, out float v);
        Assert.Equal(0f, u);
        Assert.Equal(0.5f, v);

        PshCombiner.CubeRemapTo2D(0f, 0f, 1f, out u, out v);
        Assert.Equal(0f, u);
        Assert.Equal(0f, v);

        // -X direction still divides by |x| (no sign flip on the divisor).
        PshCombiner.CubeRemapTo2D(-1f, 0.25f, -0.5f, out u, out v);
        Assert.Equal(-0.5f, u);
        Assert.Equal(0.25f, v);
    }

    private static void PushCubeDraw(
        Nv2AMmio nv2A,
        ArrayMemory memory,
        float dx,
        float dy,
        float dz,
        uint format,
        uint imageRectWidth,
        uint imageRectHeight)
    {
        WriteTriangle(memory);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, dx, dy, dz, 1f);
        PushFormatTextureSlot(pushes, 0, 0x4000u, format, 0u, imageRectWidth, imageRectHeight);
        PushStagePassTexel(pushes, 0, 0x03u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
    }

    [Fact]
    public void Draw_CubeMapSamplesPositiveXFace()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x4000u + 12u, 0xFF123456u);

        PushCubeDraw(nv2A, memory, 1f, 0f, 0f, CubeFormat(0x06u, 1, 1), 2u, 2u);

        Assert.Equal(0xFF123456u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_CubeMapSamplesPositiveZFace()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x4000u + (4u * 128u) + 12u, 0xFF123456u);

        PushCubeDraw(nv2A, memory, 0f, 0f, 1f, CubeFormat(0x06u, 1, 1), 2u, 2u);

        Assert.Equal(0xFF123456u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Theory]
    // The SZ byte-order twins are the same 4-byte swizzled words with the
    // channels permuted; a cube sample must decode them exactly like the 2D
    // sampler does (0x3A is what xgu bump_cubemap_reflect binds). The
    // pass-through combiner in this harness forces output alpha to 1, so the
    // expectation pins the RGB channel order (the word's own alpha byte is
    // covered by the 2D byte-order suite).
    [InlineData(0x3Au, 0xFF563412u)]
    [InlineData(0x3Bu, 0xFF3412FFu)]
    [InlineData(0x3Cu, 0xFFFF1234u)]
    public void Draw_CubeMapSamplesByteOrderFace(uint color, uint expected)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x4000u + 12u, 0xFF123456u);

        PushCubeDraw(nv2A, memory, 1f, 0f, 0f, CubeFormat(color, 1, 1), 2u, 2u);

        Assert.Equal(expected, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_CubeMapExtentComesFromBaseSizeNotImageRect()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        // A 2x2 face (BASE_SIZE 1) samples (s,t) = (1,1) at the face texel
        // with swizzled offset 3 (byte 12). A 1x1 TEXIMAGERECT would sample
        // offset 0 instead, which holds a different texel.
        memory.WritePhysical(0x4000u, 0xFF112233u);
        memory.WritePhysical(0x4000u + 12u, 0xFF123456u);

        PushCubeDraw(nv2A, memory, 1f, 0f, 0f, CubeFormat(0x06u, 1, 1), 1u, 1u);

        Assert.Equal(0xFF123456u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    /// <summary>Morton/tiled texel offset for a 4x4 swizzled texture.</summary>
    private static uint Swizzle2D(uint x, uint y)
    {
        uint result = 0u;
        for (int bit = 0; bit < 8; bit++)
        {
            result |= ((x >> bit) & 1u) << (bit * 2);
            result |= ((y >> bit) & 1u) << ((bit * 2) + 1);
        }

        return result;
    }

    private const uint LinearFilter = 0x02020000u;

    /// <summary>
    /// Renders one constant-UV Project2D sample of a swizzled 4x4 A8R8G8B8
    /// texture with the given TEXFILTER word and returns the shaded pixel.
    /// <see cref="Nv2AMmio.TextureFilterRegisters"/> is the opt-in that makes
    /// the sampler honor MIN/MAG and the convolution kernel (TEXFILTER is
    /// otherwise stored and ignored - the sampler is nearest-only).
    /// </summary>
    private static uint RenderFilteredSample(
        uint filterWord,
        float u,
        float v,
        bool registers,
        Func<uint, uint, uint> texel)
    {
        var (nv2A, memory) = CreateChannel();
        nv2A.TextureFilterRegisters = registers;
        WriteTriangle(memory);
        for (uint y = 0; y < 4; y++)
        {
            for (uint x = 0; x < 4; x++)
            {
                memory.WritePhysical(0x4000u + (Swizzle2D(x, y) * 4u), texel(x, y));
            }
        }

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, u, v, 0f, 1f);
        // Swizzled A8R8G8B8, dimensionality 2, BASE_SIZE 2 (4x4), one level.
        PushFormatTextureSlot(pushes, 0, 0x4000u, Swizzled2DFormat(0x06u, 2, 2), 0x00010101u, 4u, 4u);
        pushes.Add((0x1B14u, filterWord));
        PushStagePassTexel(pushes, 0, 0x01u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.TexturedPixels > 0, "the draw must sample the texture");
        return ReadPixel(memory, 1, 1);
    }

    private static uint GreyRamp(uint x, uint y) =>
        0xFF000000u | ((x * 60u) << 16) | ((y * 60u) << 8) | 0x11u;

    private static uint RedChannel(uint pixel) => (pixel >> 16) & 0xFFu;

    [Fact]
    public void TextureFilterRegisters_IsOffByDefault()
    {
        var (nv2A, _) = CreateChannel();
        Assert.False(nv2A.TextureFilterRegisters);
    }

    /// <summary>
    /// Off is the legacy nearest sampler: a sample at the texel grid boundary
    /// (0.5 of a 4x4) is the upper texel, and the nearest texel center reads
    /// the same value.
    /// </summary>
    [Fact]
    public void TextureFilterRegisters_Off_StaysNearest()
    {
        uint boundary = RenderFilteredSample(LinearFilter, 0.5f, 0.5f, registers: false, GreyRamp);
        uint center = RenderFilteredSample(LinearFilter, 0.625f, 0.625f, registers: false, GreyRamp);
        uint lower = RenderFilteredSample(LinearFilter, 0.375f, 0.375f, registers: false, GreyRamp);
        Assert.Equal(center, boundary);
        Assert.NotEqual(lower, boundary);
    }

    /// <summary>
    /// On, MIN/MAG 2 is a tent filter at LOD 0: the sample at the grid
    /// boundary blends the four surrounding texels, so it differs from the
    /// nearest result and lies between the two texel centres per channel.
    /// </summary>
    [Fact]
    public void TextureFilterRegisters_On_BlendsBetweenTexels()
    {
        uint upper = RenderFilteredSample(LinearFilter, 0.625f, 0.625f, registers: true, GreyRamp);
        uint lower = RenderFilteredSample(LinearFilter, 0.375f, 0.375f, registers: true, GreyRamp);
        uint blended = RenderFilteredSample(LinearFilter, 0.5f, 0.5f, registers: true, GreyRamp);
        uint nearest = RenderFilteredSample(LinearFilter, 0.5f, 0.5f, registers: false, GreyRamp);
        Assert.NotEqual(nearest, blended);
        int low = (int)RedChannel(lower);
        int mid = (int)RedChannel(blended);
        int high = (int)RedChannel(upper);
        Assert.True(mid > low && mid < high, $"blended red {mid} not between {low} and {high}");
        // GreyRamp puts 60 at texel 1 and 120 at texel 2; the grid-boundary
        // sample is an even four-texel blend, so red is 90 (8-bit rounding).
        Assert.InRange(mid, 88, 92);
    }

    /// <summary>
    /// A filter word that asks for nearest (1/1) must keep the exact legacy
    /// result even with the registers enabled - the opt-in only changes what
    /// the guest asked to change.
    /// </summary>
    [Fact]
    public void TextureFilterRegisters_NearestWord_MatchesLegacy()
    {
        uint legacy = RenderFilteredSample(0x01010000u, 0.5f, 0.5f, registers: false, GreyRamp);
        uint enabled = RenderFilteredSample(0x01010000u, 0.5f, 0.5f, registers: true, GreyRamp);
        Assert.Equal(legacy, enabled);
    }

    /// <summary>
    /// QUINCUNX/Gaussian convolution is xemu's nine-tap 1-2-1 blur - applied
    /// only when MIN is CONVOLUTION_2D_LOD0 (7), which is the one pairing the
    /// reference convolves (xemu <c>psh.c:197-210</c>): a single bright texel
    /// in a black 4x4 spreads over its neighbours, so the center sample drops
    /// to roughly the center weight (4/16) of the impulse. The same kernel
    /// field next to an ordinary tent MIN is not a convolution (the dashboard's
    /// and the corpus cubemap's shape), so it must read the sharp texel.
    /// </summary>
    [Fact]
    public void TextureFilterRegisters_ConvolutionBlursImpulse()
    {
        static uint Impulse(uint x, uint y) => x == 1 && y == 1 ? 0xFFFFFFFFu : 0xFF000000u;
        const uint kernelOnly = 0x02012000u;   // MIN 1 (nearest), MAG 2, kernel QUINCUNX
        const uint convolution = 0x02072000u;  // MIN 7 (CONVOLUTION_2D_LOD0), MAG 2, kernel QUINCUNX
        uint sharp = RenderFilteredSample(0x01010000u, 0.375f, 0.375f, registers: false, Impulse);
        uint gated = RenderFilteredSample(kernelOnly, 0.375f, 0.375f, registers: true, Impulse);
        uint blurred = RenderFilteredSample(convolution, 0.375f, 0.375f, registers: true, Impulse);
        int sharpRed = (int)RedChannel(sharp);
        int gatedRed = (int)RedChannel(gated);
        int blurredRed = (int)RedChannel(blurred);
        Assert.True(sharpRed > 200, $"impulse texel should read bright, got {sharpRed}");
        Assert.Equal(sharpRed, gatedRed);
        // The centre tap has weight 4/16, so 255 * 0.25 = 63.75 lands in the
        // six-bit neighbourhood of 64.
        Assert.InRange(blurredRed, 58, 70);
    }

    /// <summary>
    /// The nine convolution taps are one texel apart on a linear (LU_IMAGE_*)
    /// stage too. A rect texture takes texel-unit coordinates, so a delta of
    /// 1/textureSize in normalized units would move a quarter of a texel and
    /// every tap would read the same texel: the same impulse that blurs to
    /// ~4/16 on a swizzled stage would stay at full intensity. xemu normalizes
    /// the rect coordinate first and offsets by 1/textureSize
    /// (<c>apply_convolution_filter</c>: <c>norm%d</c> + <c>texCoordDelta</c>),
    /// i.e. exactly one texel in texel-unit space.
    /// </summary>
    [Fact]
    public void TextureFilterRegisters_ConvolutionOffsetsOneTexelOnLinearTexture()
    {
        static uint Impulse(uint x, uint y) => x == 1 && y == 1 ? 0xFFFFFFFFu : 0xFF000000u;
        const uint kernelOnly = 0x02012000u;   // MIN 1 (nearest), MAG 2, kernel QUINCUNX
        const uint convolution = 0x02072000u;  // MIN 7 (CONVOLUTION_2D_LOD0), MAG 2, kernel QUINCUNX

        // Texel-unit coordinates: (1.5, 1.5) is the centre of texel (1,1).
        uint sharp = RenderFilteredLinearSample(0x01010000u, 1.5f, 1.5f, registers: false, Impulse);
        uint gated = RenderFilteredLinearSample(kernelOnly, 1.5f, 1.5f, registers: true, Impulse);
        uint blurred = RenderFilteredLinearSample(convolution, 1.5f, 1.5f, registers: true, Impulse);
        int sharpRed = (int)RedChannel(sharp);
        int gatedRed = (int)RedChannel(gated);
        int blurredRed = (int)RedChannel(blurred);
        Assert.True(sharpRed > 200, $"impulse texel should read bright, got {sharpRed}");
        Assert.Equal(sharpRed, gatedRed);
        // One texel per tap: the centre weight is 4/16, 255 * 0.25 = 63.75.
        Assert.InRange(blurredRed, 58, 70);
    }

    /// <summary>
    /// Renders one constant-UV Project2D sample of a linear (LU_IMAGE_A8R8G8B8)
    /// 4x4 texture laid out row-major at 0x4000. Linear formats address texels
    /// directly, so the extent comes from TEXIMAGERECT and the coordinates are
    /// texel units (research-notes §95).
    /// </summary>
    private static uint RenderFilteredLinearSample(
        uint filterWord,
        float u,
        float v,
        bool registers,
        Func<uint, uint, uint> texel)
    {
        var (nv2A, memory) = CreateChannel();
        nv2A.TextureFilterRegisters = registers;
        WriteTriangle(memory);
        for (uint y = 0; y < 4; y++)
        {
            for (uint x = 0; x < 4; x++)
            {
                memory.WritePhysical(0x4000u + (((y * 4u) + x) * 4u), texel(x, y));
            }
        }

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, u, v, 0f, 1f);
        // LU_IMAGE_A8R8G8B8: linear layout, dimensionality 2, one level; a
        // swizzled word here would take its extent from BASE_SIZE instead.
        const uint linearFormat = (1u << 16) | (2u << 6) | (0x1Eu << 8);
        PushFormatTextureSlot(pushes, 0, 0x4000u, linearFormat, 0x00010101u, 4u, 4u);
        pushes.Add((0x1B14u, filterWord));
        PushStagePassTexel(pushes, 0, 0x01u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.TexturedPixels > 0, "the draw must sample the texture");
        return ReadPixel(memory, 1, 1);
    }

    [Fact]
    public void Draw_CubemapModeOnFlatTextureUsesCubeRemap()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);

        // 4x2 linear A8R8G8B8 flat texture; direction (1, 0.5, 0) remaps to
        // (u, v) = (0, 0.5) in texel units -> texel (0, 0) (LU_IMAGE formats
        // take texel-unit coordinates; see research-notes §95).
        memory.WritePhysical(0x4000u, 0xFF0F0F0Fu);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 1f, 0.5f, 0f, 1f);
        PushFormatTextureSlot(pushes, 0, 0x4000u, 0x00001200u, 0u, 4u, 2u);
        pushes.Add((0x1B10u, (4u * 4u) << 16));
        PushStagePassTexel(pushes, 0, 0x03u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(0xFF0F0F0Fu, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_Project3DSamplesVolumeTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);

        // 2x2x2 swizzled volume: (1,1,1) sits at offset 7.
        memory.WritePhysical(0x4000u + 28u, 0xFF336699u);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 2f, 2f, 2f, 2f);
        const uint project3DFormat = (1u << 28) | (1u << 16) | (3u << 6) | (0x06u << 8);
        // Clamp on U/V/P in reference TEXADDRESS bits (ADDRU 0-2, ADDRV 8-10,
        // ADDRP 16-18); the out-of-bounds (2,2,2) coord must clamp to (1,1,1).
        PushFormatTextureSlot(pushes, 0, 0x4000u, project3DFormat, 0x00030303u, 2u, 2u);
        PushStagePassTexel(pushes, 0, 0x02u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(0xFF336699u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    private static void PushClipPlaneDraw(Nv2AMmio nv2A, ArrayMemory memory, uint clipMode)
    {
        WriteTriangle(memory);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1f);
        pushes.Add((0x17F8u, clipMode));
        PushStagePassTexel(pushes, 0, 0x05u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_ClipPlaneDiscardsWhenCompareBitSet()
    {
        // Stage 0, component x: bit set means discard when pT.x >= 0.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushClipPlaneDraw(nv2A, memory, 0x1u);

        Assert.Equal(0uL, nv2A.DrawnPixels);
    }

    [Fact]
    public void Draw_ClipPlaneKeepsFragmentsWhenNoComponentMatches()
    {
        // Bit set means discard when pT.x >= 0; with pT.x = 0.25 every
        // fragment dies. Clearing the bit flips the test to pT.x < 0, which
        // no fragment satisfies, so all fragments survive.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushClipPlaneDraw(nv2A, memory, 0x0u);

        Assert.True(nv2A.DrawnPixels > 0);
    }

    [Fact]
    public void Draw_ReflectionDiffuseSamplesRemappedTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);

        // Stage 0 samples a white 1x1 texel (input for the dots), stage 2 is
        // DOT_RFLCT_DIFF on a 4x4 linear texture. dot1 = 0.5 (oT1 . white),
        // dot2 = 0.25, dot2_n = 0.25 (oT3 . white) -> n = (0.5, 0.25, 0.25)
        // -> remap (u, v) = (-0.5, 0.5); LU_IMAGE texel-unit coords wrap
        // x = floor(-0.5) -> 3, y = floor(0.5) -> 0, pitch 16 -> texel (3, 0) at +12.
        memory.WritePhysical(0x4000u, 0xFFFFFFFFu);
        memory.WritePhysical(0x4200u + 12u, 0xFF123456u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxV, v: 2, outMask: 0xF, outAddress: 10);
        uint[] tex2 = Insn(mac: MacMov, aMux: MuxV, v: 3, outMask: 0xF, outAddress: 11);
        uint[] tex3 = Insn(mac: MacMov, aMux: MuxV, v: 4, outMask: 0xF, outAddress: 12, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1f);
        PushTexcoordArray(pushes, memory, 2, 0x2300u, 0.5f, 0f, 0f, 1f);
        PushTexcoordArray(pushes, memory, 3, 0x2400u, 0.25f, 0f, 0f, 1f);
        PushTexcoordArray(pushes, memory, 4, 0x2500u, 0.25f, 0f, 0f, 1f);
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        MethodPush(pushes, tex1, 0x0B20u);
        MethodPush(pushes, tex2, 0x0B30u);
        MethodPush(pushes, tex3, 0x0B40u);
        PushFormatTextureSlot(pushes, 0, 0x4000u, 0x00001200u, 0u, 1u, 1u);
        pushes.Add((0x1B10u, 4u << 16));
        PushFormatTextureSlot(pushes, 2, 0x4200u, 0x00001200u, 0u, 4u, 4u);
        pushes.Add((0x1B50u, 16u << 16));
        pushes.Add((0x1E70u, 0x01u | (0x0Bu << 10)));
        pushes.Add((0x0AC8u, 0x0A200000u));
        pushes.Add((0x1E48u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x03u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0xFF123456u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.Equal(0uL, nv2A.UnsupportedDotMaps);
    }

    [Fact]
    public void Draw_BrdfMatchesXemuZeroTexelStub()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1f);
        // BRDF (valid at stages 2-3) writes the xemu zero texel. Route the
        // stage-2 texel alpha through the alpha combiner to R0.a and the
        // final G input, so the zero texel is observable (a NONE fallback
        // would produce alpha 1 = 0xFF000000).
        pushes.Add((0x1E70u, 0x08u << 10));
        pushes.Add((0x0AC8u, 0x0A200000u));
        pushes.Add((0x1E48u, 0x000000C0u));
        pushes.Add((0x0268u, 0x1A200000u));
        pushes.Add((0x0AA8u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x03u));
        pushes.Add((0x0288u, 0x000C0C00u));
        pushes.Add((0x028Cu, 0x00001C00u));
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.CombinerDraws);
        Assert.Equal(0x00000000u, ReadPixel(memory, 2, 2));
        Assert.True(nv2A.TexModeFallbacks > 0);
    }

    private static void PushHiloDotDraw(Nv2AMmio nv2A, ArrayMemory memory, uint dotMap)
    {
        // Stage 0 samples the 1x1 HILO source texel 0xFF000000 (a=255, rgb=0),
        // stage 1 DOTPRODUCT dots (0,0,2) against the mapped texel (c1.z = 2 so
        // the texel-unit dot reaches the second texel), stage 2 DOT_ST samples
        // the 2-wide texture at (dot1, 0).
        WriteTriangle(memory);
        memory.WritePhysical(0x4000u, 0xFF000000u);
        memory.WritePhysical(0x4200u, 0xFF000000u);
        memory.WritePhysical(0x4204u, 0xFF00FF00u);

        uint[] position = Insn(mac: MacMov, aMux: MuxV, v: 0, outMask: 0xF, outAddress: 0);
        uint[] tex0 = Insn(mac: MacMov, aMux: MuxV, v: 1, outMask: 0xF, outAddress: 9);
        uint[] tex1 = Insn(mac: MacMov, aMux: MuxC, constIndex: 1, outMask: 0xF, outAddress: 10);
        uint[] tex2 = Insn(mac: MacMov, aMux: MuxC, constIndex: 2, outMask: 0xF, outAddress: 11, final: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00001042u));
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 16u), BitConverter.SingleToUInt32Bits(0.25f));
            memory.WritePhysical(0x2204u + (i * 16u), BitConverter.SingleToUInt32Bits(0.25f));
            memory.WritePhysical(0x2208u + (i * 16u), BitConverter.SingleToUInt32Bits(0f));
            memory.WritePhysical(0x220Cu + (i * 16u), BitConverter.SingleToUInt32Bits(1f));
        }

        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, position, 0x0B00u);
        MethodPush(pushes, tex0, 0x0B10u);
        MethodPush(pushes, tex1, 0x0B20u);
        MethodPush(pushes, tex2, 0x0B30u);
        pushes.Add((0x0B80u, F(0f)));
        pushes.Add((0x0B84u, F(0f)));
        pushes.Add((0x0B88u, F(0f)));
        pushes.Add((0x0B8Cu, F(1f)));
        pushes.Add((0x0B90u, F(0f)));
        pushes.Add((0x0B94u, F(0f)));
        pushes.Add((0x0B98u, F(2f)));
        pushes.Add((0x0B9Cu, F(1f)));
        pushes.Add((0x0BA0u, F(0f)));
        pushes.Add((0x0BA4u, F(0f)));
        pushes.Add((0x0BA8u, F(0f)));
        pushes.Add((0x0BACu, F(1f)));
        PushFormatTextureSlot(pushes, 0, 0x4000u, 0x00001200u, 0u, 1u, 1u);
        PushFormatTextureSlot(pushes, 2, 0x4200u, 0x00001200u, 0u, 2u, 1u);
        pushes.Add((0x1B50u, 8u << 16));
        pushes.Add((0x1E70u, 0x01u | (0x11u << 5) | (0x09u << 10)));
        pushes.Add((0x1E74u, dotMap));
        pushes.Add((0x0AC8u, 0x0A200000u));
        pushes.Add((0x1E48u, 0x000000C0u));
        pushes.Add((0x1E60u, 0x03u));
        PushPassR0(pushes);
        PushDraw(pushes);
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
        Assert.Equal(0uL, nv2A.UnsupportedDotMaps);
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_HiloHemisphereGenericSelectsSecondTexel()
    {
        // texel 0xFF000000: hi = 0xFF00 -> -0.0078, lo = 0 -> z ~= 1.
        // dot((0,0,2), (-0.0078, 0, 1)) ~= 2 -> texel 1 (green). Map 5 (D3D)
        // decodes hi to +0.992 with z = 0 instead, giving dot 0 -> texel 0.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushHiloDotDraw(nv2A, memory, 0x07u);

        Assert.Equal(0xFF00FF00u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_HiloHemisphereD3dSelectsFirstTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushHiloDotDraw(nv2A, memory, 0x05u);

        Assert.Equal(0xFF000000u, ReadPixel(memory, 2, 2));
    }
}
