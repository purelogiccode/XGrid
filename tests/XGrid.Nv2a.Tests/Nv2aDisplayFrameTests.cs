using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the display-controller model: the guest programs the video mode through
/// the VGA register file the NV2A mirrors at NV_PRMCIO (xemu forwards those
/// accesses to <c>vga_ioport_write</c>), and the display frame is PCRTC START
/// read with the CRTC pitch and size - not the last flipped 3D surface.
/// </summary>
/// <remarks>
/// The distinction is what makes direct-framebuffer programs visible (nxdk
/// <c>XVideoGetFB</c>, <c>debugPrint</c>: no 3D draws, no flips) and what a TV
/// attached to the console actually shows. Measured on the corpus (decisions
/// 207d/207f/208): <c>gamma</c> exports 1 colour through the flip path and 768
/// through the display path, <c>hello</c> 1 vs 2 (its text).
/// </remarks>
public sealed class Nv2aDisplayFrameTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const uint PcrtcStart = 0x600800u;
    private const uint Prmcio = 0x601000u;

    private const uint FlippedSurface = 0x4000u;
    private const uint DisplaySurface = 0x8000u;

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

    private static Nv2AMmio CreateChannel(ArrayMemory memory)
    {
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
        return nv2A;
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

    private static void Present(Nv2AMmio nv2A, ArrayMemory memory, uint surface, uint width, uint height)
    {
        uint pitch = width * 4u;
        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u), // pitch-type surface, 4-byte colour
            (0x020Cu, pitch),
            (0x0200u, width << 16),
            (0x0204u, height << 16),
            (0x0210u, surface),
            (0x012Cu, 0u)); // flip increment write
    }

    private static void WriteCrtc(Nv2AMmio nv2A, byte index, byte value)
    {
        nv2A.Write(Base + Prmcio + 0x3D4u, 1, index);
        nv2A.Write(Base + Prmcio + 0x3D5u, 1, value);
    }

    private static void WriteSequencer(Nv2AMmio nv2A, byte index, byte value)
    {
        nv2A.Write(Base + Prmcio + 0x3C4u, 1, index);
        nv2A.Write(Base + Prmcio + 0x3C5u, 1, value);
    }

    /// <summary>Programs the CRTC for a <paramref name="width"/>x<paramref name="height"/> mode.</summary>
    private static void ProgramMode(Nv2AMmio nv2A, uint width, uint height, uint pitch, byte depth = 3)
    {
        uint vDisplayEnd = height - 1;
        uint offset = pitch / 8;
        WriteCrtc(nv2A, 0x01, (byte)((width / 8) - 1));
        WriteCrtc(nv2A, 0x07, (byte)(((vDisplayEnd >> 8) & 0x01) << 1 | ((vDisplayEnd >> 9) & 0x01) << 6));
        WriteCrtc(nv2A, 0x12, (byte)(vDisplayEnd & 0xFF));
        WriteCrtc(nv2A, 0x13, (byte)(offset & 0xFF));
        WriteCrtc(nv2A, 0x19, (byte)(((offset >> 8) & 0x07) << 5));
        WriteCrtc(nv2A, 0x25, (byte)(((offset >> 10) & 0x01) << 5));
        WriteCrtc(nv2A, 0x28, depth);
    }

    private static void FillRow(ArrayMemory memory, uint surface, uint pitch, uint width, uint y, uint pixel)
    {
        for (uint x = 0; x < width; x++)
        {
            memory.WritePhysical(surface + (y * pitch) + (x * 4u), pixel);
        }
    }

    [Fact]
    public void CrtcRegisters_DecodeWidthHeightPitchAndDepth()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        ProgramMode(nv2A, 640, 480, 2560);

        Assert.Equal(640u, nv2A.DisplayWidth);
        Assert.Equal(480u, nv2A.DisplayHeight);
        Assert.Equal(2560u, nv2A.DisplayLineOffset);
        Assert.Equal(4u, nv2A.DisplayBytesPerPixel);
    }

    [Fact]
    public void CrtcExtendedOffset_UsesCr19AndCr25ForTenBits()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);

        // The CRTC offset field is 12 bits: 0-7 from CR13, 8-10 from CR19 bits
        // 5-7 and 11 from CR25 bit 5 (xemu nv2a_get_params).
        WriteCrtc(nv2A, 0x13, 0x80);
        Assert.Equal(0x80u << 3, nv2A.DisplayLineOffset);

        WriteCrtc(nv2A, 0x13, 0x00);
        WriteCrtc(nv2A, 0x19, 0x40); // bit 6 -> offset bit 9
        Assert.Equal(0x200u << 3, nv2A.DisplayLineOffset);

        WriteCrtc(nv2A, 0x19, 0x00);
        WriteCrtc(nv2A, 0x25, 0x20); // bit 5 -> offset bit 11
        Assert.Equal(0x800u << 3, nv2A.DisplayLineOffset);

        // 1280 px * 4 B = 5120 B/line = 640 offset units (0x280).
        WriteCrtc(nv2A, 0x13, 0x80);
        WriteCrtc(nv2A, 0x19, 0x40);
        WriteCrtc(nv2A, 0x25, 0x00);
        Assert.True(nv2A.DisplayLineOffset == 5120u, nv2A.VgaCrtcDebug);
    }

    [Fact]
    public void DisplayFrame_ReadsPcrtcStartWithTheCrtcPitch()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);

        // 64x32 display mode, pitch 256 (= 64 px * 4 B).
        ProgramMode(nv2A, 64, 32, 256);
        nv2A.Write(Base + PcrtcStart, 4, DisplaySurface);

        // Every display row gets its own blue value; the flipped surface holds
        // a different picture entirely, so reading the wrong buffer or the
        // wrong pitch cannot pass.
        for (uint y = 0; y < 32; y++)
        {
            FillRow(memory, DisplaySurface, 256, 64, y, 0xFF000000u | y);
        }

        Present(nv2A, memory, FlippedSurface, 64, 32);
        for (uint y = 0; y < 32; y++)
        {
            FillRow(memory, FlippedSurface, 256, 64, y, 0xFF0000AAu);
        }

        Assert.True(nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(64u, width);
        Assert.Equal(32u, height);
        Assert.Equal(64 * 32 * 4, bgra.Length);
        for (uint y = 0; y < 32; y++)
        {
            int at = (int)(y * 64 * 4);
            Assert.Equal((byte)y, bgra[at]);
            Assert.Equal(0x00, bgra[at + 2]);
        }
    }

    [Fact]
    public void DisplayFrame_FallsBackToTheFlippedSurfaceBeforeAModeIsProgrammed()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        Present(nv2A, memory, FlippedSurface, 64, 32);
        FillRow(memory, FlippedSurface, 256, 64, 0, 0xFF0000AAu);

        // No CRTC registers written yet: the display model has no geometry, so
        // the flipped surface is what the console shows.
        Assert.False(nv2A.TryGetDisplayGeometry(out _, out _, out _, out _, out _));
        Assert.True(nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(64u, width);
        Assert.Equal(32u, height);
        Assert.Equal(0xAA, bgra[0]);
    }

    [Fact]
    public void DisplayFrame_DecodesFifteenBitAndRgb565FromGeneralControl()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        ProgramMode(nv2A, 64, 32, 128, depth: 2); // 16 bpp, pitch 64 px * 2 B
        nv2A.Write(Base + PcrtcStart, 4, DisplaySurface);

        // 0x7C00 is saturated red in X1R5G5B5 and red+green in RGB565, so the
        // decode branch is observable.
        memory.WritePhysical(DisplaySurface, 0x00007C00u);

        Assert.True(nv2A.DisplayFifteenBit);
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] fifteen));
        Assert.Equal(0xFF, fifteen[2]);
        Assert.Equal(0x00, fifteen[1]);

        // PRAMDAC general control bit 12 (ALT_MODE_SEL) selects RGB565.
        nv2A.Write(Base + 0x680600u, 4, 1u << 12);
        Assert.False(nv2A.DisplayFifteenBit);
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] sixteen));
        Assert.Equal(0x7B, sixteen[2]);
        Assert.Equal(0x81, sixteen[1]);
    }

    [Fact]
    public void DisplayFrame_IsBlankedWhenTheSequencerTurnsTheScreenOff()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        ProgramMode(nv2A, 64, 32, 256);
        nv2A.Write(Base + PcrtcStart, 4, DisplaySurface);
        for (uint y = 0; y < 32; y++)
        {
            FillRow(memory, DisplaySurface, 256, 64, y, 0xFF000000u | y);
        }

        Present(nv2A, memory, FlippedSurface, 64, 32);
        FillRow(memory, FlippedSurface, 256, 64, 0, 0xFF0000AAu);

        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] before));
        Assert.Equal(0x00, before[0]);

        WriteSequencer(nv2A, 1, 0x20); // SR01 bit 5 = screen off
        Assert.False(nv2A.TryGetDisplayGeometry(out _, out _, out _, out _, out _));
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] after));
        Assert.Equal(0xAA, after[0]); // the flipped surface, not the blank screen

        WriteSequencer(nv2A, 1, 0x00);
        Assert.True(nv2A.TryGetDisplayGeometry(out _, out _, out _, out _, out _));
    }

    [Fact]
    public void CrtcWriteProtection_LocksCr0ToCr7WhileCr11Bit7IsSet()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);

        WriteCrtc(nv2A, 0x01, 0x4F); // width 640
        WriteCrtc(nv2A, 0x12, 0xDF); // height 224
        WriteCrtc(nv2A, 0x07, 0x02); // height bit 8 -> 480
        Assert.Equal(640u, nv2A.DisplayWidth);
        Assert.Equal(480u, nv2A.DisplayHeight);

        WriteCrtc(nv2A, 0x11, 0x80); // protect CR0-CR7
        WriteCrtc(nv2A, 0x01, 0x27); // ignored
        WriteCrtc(nv2A, 0x07, 0x00); // ignored (only bit 4 is writable)
        Assert.Equal(640u, nv2A.DisplayWidth);
        Assert.Equal(480u, nv2A.DisplayHeight);

        WriteCrtc(nv2A, 0x11, 0x00); // unlock
        WriteCrtc(nv2A, 0x01, 0x27);
        Assert.Equal(320u, nv2A.DisplayWidth);
    }

    [Fact]
    public void CrtcIndexAndData_CanArriveInOneWord()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);

        // A 32-bit write carries the index port (0x3D4) in lane 0 and the data
        // port (0x3D5) in lane 1 - the bus turns narrower writes into the same
        // read-modify-write, so both lanes must be applied.
        nv2A.Write(Base + Prmcio + 0x3D4u, 4, 0x4F01u);
        Assert.Equal(640u, nv2A.DisplayWidth);

        nv2A.Write(Base + Prmcio + 0x3D4u, 4, 0xDF12u); // CR12 = 0xDF
        WriteCrtc(nv2A, 0x07, 0x02); // CR7 bit 8 of the visible height
        Assert.Equal(480u, nv2A.DisplayHeight);
    }

    [Fact]
    public void DisplayGeometry_AndCrtcState_SurviveASnapshot()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        ProgramMode(nv2A, 64, 32, 256);
        nv2A.Write(Base + PcrtcStart, 4, DisplaySurface);
        for (uint y = 0; y < 32; y++)
        {
            FillRow(memory, DisplaySurface, 256, 64, y, 0xFF000000u | y);
        }

        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        stream.Position = 0;
        var restored = new Nv2AMmio(64u * 1024 * 1024, "NV2A", new ArrayMemory());
        using (var reader = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        Assert.Equal(64u, restored.DisplayWidth);
        Assert.Equal(32u, restored.DisplayHeight);
        Assert.Equal(256u, restored.DisplayLineOffset);
        Assert.True(restored.TryGetDisplayGeometry(out uint start, out _, out _, out _, out _));
        Assert.Equal(DisplaySurface, start);
    }
}
