using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Covers the NV2A leftovers real streams exercise (§124): the PROJECT2D
/// non-cube projective divide, the TEXADDRESS V-field decode plus BORDER
/// color, and the 0x30 depth (shadow-map) NEVER/ALWAYS texels.
/// Reference facts from xemu <c>hw/xbox/nv2a/pgraph/glsl/psh.c</c>
/// (<c>textureProj</c> for PROJECT2D/PROJECT3D, <c>psh_append_shadowmap</c>
/// NEVER/ALWAYS, <c>GL_CLAMP_TO_BORDER</c> with
/// <c>NV097_SET_TEXTURE_BORDER_COLOR</c>) and <c>nv2a_regs.h</c>
/// (TEXADDRESS ADDRU bits 0-2 / ADDRV bits 8-10 / ADDRP bits 16-18).
/// </summary>
public sealed class Nv2ARemainingModesTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const int MacMov = 1;
    private const int MuxR = 1;
    private const int MuxV = 2;

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

        return [0u, t1, t2, t3];
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
            memory.WritePhysical(address + (i * 16u), F(x));
            memory.WritePhysical(address + (i * 16u) + 4u, F(y));
            memory.WritePhysical(address + (i * 16u) + 8u, F(z));
            memory.WritePhysical(address + (i * 16u) + 12u, F(w));
        }
    }

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

    private const uint Swizzled2X2Format =
        (0x06u << 8) | (1u << 16) | (1u << 20) | (1u << 24) | (2u << 6);

    private static void WriteSwizzled2X2(ArrayMemory memory)
    {
        memory.WritePhysical(0x4000u, 0xFF111111u);
        memory.WritePhysical(0x4004u, 0xFF222222u);
        memory.WritePhysical(0x4008u, 0xFF333333u);
        memory.WritePhysical(0x400Cu, 0xFF444444u);
    }

    [Fact]
    public void AddressHelpers_MatchNv2aRegsBitLayout()
    {
        Assert.Equal(1, Nv2AMmio.DecodeTextureAddressU(0x00010101));
        Assert.Equal(1, Nv2AMmio.DecodeTextureAddressV(0x00010101));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressU(0x00010303));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressV(0x00010303));
        Assert.Equal(4, Nv2AMmio.DecodeTextureAddressU(0x00010404));
        Assert.Equal(4, Nv2AMmio.DecodeTextureAddressV(0x00010404));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressP(0x00030303));
        Assert.Equal(0, Nv2AMmio.ToWrapMode(1));
        Assert.Equal(1, Nv2AMmio.ToWrapMode(2));
        Assert.Equal(2, Nv2AMmio.ToWrapMode(3));
        Assert.Equal(0, Nv2AMmio.ToWrapMode(0));
        Assert.Equal(2, Nv2AMmio.ToWrapMode(7));
    }

    [Fact]
    public void ReservedAddressModes_DecodeToClampOnBothAxes()
    {
        // Modes 6-7 are reserved (nv2a_regs.h names only 1-5; xemu's
        // address-mode map covers 0-5 and asserts on anything larger, so
        // the reference defines no behavior for them). The standing safe
        // choice is CLAMP (3 = clamp-to-edge) at decode time, so no
        // downstream consumer ever sees a reserved mode.
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressU(0x00010606));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressV(0x00010606));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressU(0x00010707));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressV(0x00010707));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressP(0x00060606));
        Assert.Equal(3, Nv2AMmio.DecodeTextureAddressP(0x00070707));

        // Decoded reserved modes wrap as clamp downstream.
        Assert.Equal(2, Nv2AMmio.ToWrapMode(Nv2AMmio.DecodeTextureAddressU(0x00010606)));
        Assert.Equal(2, Nv2AMmio.ToWrapMode(Nv2AMmio.DecodeTextureAddressV(0x00010707)));

        // Defined modes still pass through unchanged on both axes.
        Assert.Equal(1, Nv2AMmio.DecodeTextureAddressU(0x00010101));
        Assert.Equal(2, Nv2AMmio.DecodeTextureAddressV(0x00020202));
        Assert.Equal(4, Nv2AMmio.DecodeTextureAddressU(0x00010404));
        Assert.Equal(5, Nv2AMmio.DecodeTextureAddressV(0x00050505));
        Assert.Equal(0, Nv2AMmio.DecodeTextureAddressU(0x00000000));
    }

    [Fact]
    public void BorderColor_DecodesArgbPacking()
    {
        Nv2AMmio.DecodeBorderColor(0xFFFF0000u, out float r, out float g, out float b, out float a);
        Assert.Equal(1f, r);
        Assert.Equal(0f, g);
        Assert.Equal(0f, b);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void DepthFormats_AreRecognized()
    {
        Assert.True(Nv2AMmio.IsDepthTextureFormat(0x30u));
        Assert.True(Nv2AMmio.IsDepthTextureFormat(0x2Cu));
        Assert.True(Nv2AMmio.IsDepthTextureFormat(0x2Eu));
        Assert.True(Nv2AMmio.IsDepthTextureFormat(0x2Fu));
        Assert.True(Nv2AMmio.IsDepthTextureFormat(0x31u));
        Assert.False(Nv2AMmio.IsDepthTextureFormat(0x06u));
        Assert.False(Nv2AMmio.IsDepthTextureFormat(0x12u));
    }

    private static void PushProjectDraw(
        Nv2AMmio nv2A,
        ArrayMemory memory,
        uint stageMode,
        uint format,
        uint address,
        float u,
        float v,
        float w)
    {
        WriteTriangle(memory);
        WriteSwizzled2X2(memory);
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, u, v, 0f, w);
        PushFormatTextureSlot(pushes, 0, 0x4000u, format, address, 2u, 2u);
        PushStagePassTexel(pushes, 0, stageMode);
        PushDraw(pushes);
        PushMethods(nv2A, memory, [.. pushes]);
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.CombinerDraws);
    }

    [Fact]
    public void Draw_Project2DDividesByW()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushProjectDraw(nv2A, memory, 0x01u, Swizzled2X2Format, 0u, 1.0f, 0.25f, 2.0f);
        Assert.Equal(0xFF222222u, ReadPixel(memory, 2, 2));
        Assert.True(nv2A.TexturedPixels > 0);
    }

    [Fact]
    public void Draw_Project2DUnitWMatchesUndivided()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushProjectDraw(nv2A, memory, 0x01u, Swizzled2X2Format, 0u, 0.5f, 0.25f, 1.0f);
        Assert.Equal(0xFF222222u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_AddressUWrapMatchesReferenceBit0()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushProjectDraw(nv2A, memory, 0x01u, Swizzled2X2Format, 0x00000001u, -0.25f, 0.25f, 1.0f);
        Assert.Equal(0xFF222222u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_AddressVClampUsesReferenceBit8()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushProjectDraw(nv2A, memory, 0x01u, Swizzled2X2Format, 0x00000300u, 0.25f, -0.5f, 1.0f);
        Assert.Equal(0xFF111111u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_BorderAxisReturnsBorderColor()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);
        WriteSwizzled2X2(memory);
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 1.5f, 0.25f, 0f, 1.0f);
        PushFormatTextureSlot(pushes, 0, 0x4000u, Swizzled2X2Format, 0x00000004u, 2u, 2u);
        pushes.Add((0x1B24u, 0xFFFF0000u));
        PushStagePassTexel(pushes, 0, 0x01u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, [.. pushes]);
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 2, 2));
    }

    [Fact]
    public void Draw_BorderAxisInBoundsSamplesTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);
        WriteSwizzled2X2(memory);
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1.0f);
        PushFormatTextureSlot(pushes, 0, 0x4000u, Swizzled2X2Format, 0x00000004u, 2u, 2u);
        pushes.Add((0x1B24u, 0xFFFF0000u));
        PushStagePassTexel(pushes, 0, 0x01u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, [.. pushes]);
        Assert.Equal(0xFF111111u, ReadPixel(memory, 2, 2));
    }

    private const uint DepthProjectFormat =
        (0x30u << 8) | (1u << 16) | (2u << 6);

    private const uint Depth24ProjectFormat =
        (0x2Eu << 8) | (1u << 16) | (2u << 6);

    private static void PushShadowDraw(
        Nv2AMmio nv2A,
        ArrayMemory memory,
        uint shadowFunc,
        uint depthFormat = DepthProjectFormat,
        uint depthWord = 0u)
    {
        WriteTriangle(memory);
        memory.WritePhysical(0x5000u, depthWord);
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1.0f);
        PushFormatTextureSlot(pushes, 3, 0x5000u, depthFormat, 0x00010101u, 512u, 512u);
        pushes.Add((0x1E6Cu, shadowFunc));
        PushStagePassTexel(pushes, 3, 0x02u);
        pushes.Add((0x026Cu, 0x1B200000u));
        pushes.Add((0x0AACu, 0x000000C0u));
        pushes.Add((0x028Cu, 0x00001C00u));
        PushDraw(pushes);
        PushMethods(nv2A, memory, [.. pushes]);
        Assert.Equal(1uL, nv2A.DrawsExecuted);
    }

    [Fact]
    public void Draw_ShadowNeverYieldsZeroTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushShadowDraw(nv2A, memory, 0u);
        Assert.Equal(0x00000000u, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_ShadowAlwaysYieldsOneTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushShadowDraw(nv2A, memory, 7u);
        Assert.Equal(0xFFFFFFFFu, ReadPixel(memory, 2, 2));
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_ShadowCompareFuncResolvesTheDepthTexel()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushShadowDraw(nv2A, memory, 3u); // LEQUAL
        // The stored depth is zero and the projected z is zero, so
        // 0 <= 0 passes and the texel is vec4(1.0). The old placeholder
        // counted a format miss and produced vec4(0,0,0,1).
        Assert.Equal(0xFFFFFFFFu, ReadPixel(memory, 2, 2));
        Assert.False(nv2A.TextureFormatMisses.TryGetValue(0x30u, out long misses) && misses > 0);
        Assert.Equal(0uL, nv2A.TexModeFallbacks);
    }

    [Fact]
    public void Draw_ShadowCompareFuncReadsTheStoredDepth()
    {
        // The stored depth is the far plane (0xFFFF = 65535 device units) and
        // the projected z is zero: LEQUAL fails, GEQUAL passes. This can only
        // distinguish the two if the depth word is actually sampled.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        PushShadowDraw(nv2A, memory, 3u, DepthProjectFormat, 0x0000FFFFu);
        Assert.Equal(0x00000000u, ReadPixel(memory, 2, 2));

        (Nv2AMmio gequal, ArrayMemory gequalMemory) = CreateChannel();
        PushShadowDraw(gequal, gequalMemory, 6u, DepthProjectFormat, 0x0000FFFFu);
        Assert.Equal(0xFFFFFFFFu, ReadPixel(gequalMemory, 2, 2));
    }

    [Fact]
    public void Draw_ShadowCompareFuncUsesTheTwentyFourBitDepthHighBits()
    {
        // LU_IMAGE_DEPTH_X8_Y24_FIXED: the depth is the high 24 bits and the
        // low byte is the stencil. A stencil byte alone must not fail the
        // compare, and the far depth must fail LEQUAL.
        (Nv2AMmio stencilOnly, ArrayMemory stencilMemory) = CreateChannel();
        PushShadowDraw(stencilOnly, stencilMemory, 3u, Depth24ProjectFormat, 0x000000FFu);
        Assert.Equal(0xFFFFFFFFu, ReadPixel(stencilMemory, 2, 2));

        (Nv2AMmio far, ArrayMemory farMemory) = CreateChannel();
        PushShadowDraw(far, farMemory, 3u, Depth24ProjectFormat, 0x00FFFFFFu);
        Assert.Equal(0x00000000u, ReadPixel(farMemory, 2, 2));
    }

    [Fact]
    public void Snapshot_RoundTripsBorderAndShadowState()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory);
        WriteSwizzled2X2(memory);
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>();
        PushSurface(pushes);
        PushTexcoordProgram(pushes);
        PushTexcoordArray(pushes, memory, 1, 0x2200u, 0.25f, 0.25f, 0f, 1.0f);
        PushFormatTextureSlot(pushes, 0, 0x4000u, Swizzled2X2Format, 0x00000004u, 2u, 2u);
        pushes.Add((0x1B24u, 0xFFFF0000u));
        pushes.Add((0x1E6Cu, 7u));
        PushStagePassTexel(pushes, 0, 0x01u);
        PushDraw(pushes);
        PushMethods(nv2A, memory, [.. pushes]);

        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(stream))
        {
            nv2A.SaveState(writer);
            writer.Flush();
        }

        byte[] baseline = stream.ToArray();
        var second = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(baseline)))
        {
            second.LoadState(reader);
        }

        using var again = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(again))
        {
            second.SaveState(writer);
            writer.Flush();
        }

        Assert.Equal(baseline, again.ToArray());
    }
}
