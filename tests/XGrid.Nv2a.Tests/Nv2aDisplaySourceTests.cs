using System.IO;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the display source with a GPU backend attached: the backend's readback
/// is the picture on screen only while the display controller scans a surface a
/// flip presented (a double- or triple-buffered chain whose PCRTC start trails
/// the last flip by one buffer). A program that writes the framebuffer directly
/// switches PCRTC to a buffer no flip ever presented - nxdk's
/// <c>XVideoGetFB</c>, <c>debugPrint</c> - and its pixels exist only in guest
/// RAM, so the display source must fall back to the scanout instead of serving
/// the backend's last frame (a picture of a buffer that is no longer on screen).
/// </summary>
/// <remarks>
/// Measured on the corpus (decisions 234): <c>gamma</c> under
/// <c>--gpu gl --gpu-replace</c> presented the boot animation's last frame
/// (<c>18C7CC83</c>) while the software leg presented its gradient
/// (<c>D3EBF420</c>); with this rule the GL leg reproduces the software frame.
/// </remarks>
public sealed class Nv2aDisplaySourceTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const uint PcrtcStart = 0x600800u;
    private const uint Prmcio = 0x601000u;

    private const uint FirstSurface = 0x4000u;
    private const uint SecondSurface = 0x8000u;
    private const uint DirectSurface = 0xC000u;
    private const uint Width = 64u;
    private const uint Height = 32u;
    private const uint Pitch = Width * 4u;

    // The backend's readback marker: BGRA bytes 0x11/0x22/0x33/0xFF.
    private const byte MarkerBlue = 0x11;
    private const byte MarkerRed = 0x33;

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

    /// <summary>A backend whose readback is a constant marker frame, so the source is unambiguous.</summary>
    private sealed class MarkerBackend : IPgraphBackend
    {
        public long BackendUnsupported => 0;

        public string DebugSummary => "marker";

        public long Flips { get; private set; }

        public void Method(uint subchannel, uint method, uint parameter)
        {
        }

        public void Draw(in PgraphDrawInfo info)
        {
        }

        public void Clear(in PgraphClearInfo info)
        {
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
            Flips++;
        }

        public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
        {
            width = Width;
            height = Height;
            bgra = new byte[Width * Height * 4];
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = MarkerBlue;
                bgra[i + 1] = 0x22;
                bgra[i + 2] = MarkerRed;
                bgra[i + 3] = 0xFF;
            }

            return true;
        }

        public void ResetState()
        {
            Flips = 0;
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

    /// <summary>
    /// Appends one pushbuffer command sequence at the running offset and advances
    /// PUT, so a test can flip more than once: the pusher only consumes words
    /// between GET and PUT, and reusing one offset would leave GET == PUT. The
    /// terminator is the guest's jump-to-PUT word (its low bits carry the buffer
    /// end), so GET lands exactly on PUT and the pusher stops cleanly.
    /// </summary>
    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory, ref uint address, params (uint Method, uint Parameter)[] methods)
    {
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, (address + 4u) | 1u);
        address += 4;
        nv2A.Write(Base + 0x800040u, 4, address);

        // The NO_OPERATION terminator stalls the pusher until the guest acks the
        // PGRAPH notification, and the engine resumes on the next tick - the
        // real kernel's handshake, so a second flip needs it too.
        nv2A.Write(Base + 0x400100u, 4, 0x00100000u);
        nv2A.Advance(1);
    }

    /// <summary>Presents a <paramref name="surface"/>-based frame via one flip.</summary>
    private static void Present(Nv2AMmio nv2A, ArrayMemory memory, ref uint push, uint surface)
    {
        PushMethods(
            nv2A,
            memory,
            ref push,
            (0x0208u, 0x00000104u), // pitch-type surface, 4-byte colour
            (0x020Cu, Pitch),
            (0x0200u, Width << 16),
            (0x0204u, Height << 16),
            (0x0210u, surface),
            (0x012Cu, 0u)); // flip increment write
    }

    private static void WriteCrtc(Nv2AMmio nv2A, byte index, byte value)
    {
        nv2A.Write(Base + Prmcio + 0x3D4u, 1, index);
        nv2A.Write(Base + Prmcio + 0x3D5u, 1, value);
    }

    /// <summary>Programs the CRTC for a <paramref name="pixelWidth"/>x<paramref name="pixelHeight"/> 32 bpp mode.</summary>
    private static void ProgramMode(Nv2AMmio nv2A, uint pixelWidth = Width, uint pixelHeight = Height, uint pitch = Pitch)
    {
        uint offset = pitch / 8;
        WriteCrtc(nv2A, 0x01, (byte)((pixelWidth / 8) - 1));
        WriteCrtc(nv2A, 0x07, 0x00);
        WriteCrtc(nv2A, 0x12, (byte)(pixelHeight - 1));
        WriteCrtc(nv2A, 0x13, (byte)(offset & 0xFF));
        WriteCrtc(nv2A, 0x19, (byte)(((offset >> 8) & 0x07) << 5));
        WriteCrtc(nv2A, 0x25, 0x00);
        WriteCrtc(nv2A, 0x28, 3); // 32 bpp
    }

    private static void Fill(ArrayMemory memory, uint surface, uint pixel, uint pixelWidth = Width, uint pixelHeight = Height, uint pitch = Pitch)
    {
        for (uint y = 0; y < pixelHeight; y++)
        {
            for (uint x = 0; x < pixelWidth; x++)
            {
                memory.WritePhysical(surface + (y * pitch) + (x * 4u), pixel);
            }
        }
    }

    [Fact]
    public void DisplayFrame_WithBackend_ServesTheBackendForTheScanoutItRendered()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);
        Present(nv2A, memory, ref push, FirstSurface);
        nv2A.Write(Base + PcrtcStart, 4, FirstSurface);

        // The RAM copy of the same surface is a different colour: serving it
        // instead of the backend's readback is observable.
        Fill(memory, FirstSurface, 0xFF00FF00u);

        Assert.Equal("backend", nv2A.DisplaySourceDebug);
        Assert.True(nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(Width, width);
        Assert.Equal(Height, height);
        Assert.Equal(MarkerBlue, bgra[0]);
        Assert.Equal(MarkerRed, bgra[2]);
    }

    [Fact]
    public void DisplayFrame_WithBackend_ServesTheBackendForThePreviousFlipSurface()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);

        // A flip chain: the display controller scans the buffer the *previous*
        // flip presented (measured on the triangle: PCRTC trails the last flip
        // by one buffer), so membership in the recent-presented ring decides.
        Present(nv2A, memory, ref push, FirstSurface);
        Present(nv2A, memory, ref push, SecondSurface);
        nv2A.Write(Base + PcrtcStart, 4, FirstSurface);

        Assert.Equal("backend", nv2A.DisplaySourceDebug);
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] bgra));
        Assert.Equal(MarkerBlue, bgra[0]);
    }

    [Fact]
    public void DisplayFrame_WithBackend_ServesTheScanoutForABufferNoFlipPresented()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);
        Present(nv2A, memory, ref push, FirstSurface);

        // The program wrote its own framebuffer and pointed PCRTC at it (nxdk's
        // XVideoGetFB): the backend's frame is a picture of FirstSurface, which
        // is no longer on screen.
        nv2A.Write(Base + PcrtcStart, 4, DirectSurface);
        Fill(memory, DirectSurface, 0xFF0000AAu);

        Assert.Equal("memory-scanout", nv2A.DisplaySourceDebug);
        Assert.True(nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra));
        Assert.Equal(Width, width);
        Assert.Equal(Height, height);
        Assert.Equal(0xAA, bgra[0]);
        Assert.Equal(0x00, bgra[2]);
    }

    [Fact]
    public void DisplayFrame_WithBackend_ServesTheBackendBeforeAModeIsProgrammed()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        Present(nv2A, memory, ref push, FirstSurface);

        // No CRTC mode yet: there is no scanout to compare, so the backend's
        // presented image stays the display source.
        Assert.Equal("backend", nv2A.DisplaySourceDebug);
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] bgra));
        Assert.Equal(MarkerBlue, bgra[0]);
    }

    [Fact]
    public void DisplayFrame_WithBackend_ServesTheScanoutWhenTheModeChangedAtTheSameAddress()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);
        Present(nv2A, memory, ref push, FirstSurface);
        nv2A.Write(Base + PcrtcStart, 4, FirstSurface);
        Assert.Equal("backend", nv2A.DisplaySourceDebug);

        // Same buffer address, different layout (a wider pitch): the backend
        // rendered the old geometry, so its frame is not this scanout's picture.
        ProgramMode(nv2A, pitch: 512);
        Fill(memory, FirstSurface, 0xFF0000AAu, pitch: 512);
        Assert.Equal("memory-scanout", nv2A.DisplaySourceDebug);
        Assert.True(nv2A.TryCopyDisplayFrame(out _, out _, out byte[] bgra));
        Assert.Equal(0xAA, bgra[0]);
    }

    [Fact]
    public void DisplayFrame_WithBackend_ForgetsSurfacesBeyondTheRecentRing()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);

        // The ring keeps the most recent four surfaces; a buffer presented
        // before that is no longer "recently presented".
        const int ringSize = 4;
        uint first = 0x20000u;
        for (int i = 0; i < ringSize + 1; i++)
        {
            Present(nv2A, memory, ref push, first + ((uint)i * 0x4000u));
        }

        Assert.Equal((ulong)(ringSize + 1), nv2A.FramesPresented);
        nv2A.Write(Base + PcrtcStart, 4, first);
        Assert.Equal("memory-scanout", nv2A.DisplaySourceDebug);

        nv2A.Write(Base + PcrtcStart, 4, first + ((uint)ringSize * 0x4000u));
        Assert.Equal("backend", nv2A.DisplaySourceDebug);
    }

    [Fact]
    public void DisplaySource_DecisionSurvivesASnapshot()
    {
        var memory = new ArrayMemory();
        Nv2AMmio nv2A = CreateChannel(memory);
        nv2A.Backend = new MarkerBackend();
        uint push = 0;
        ProgramMode(nv2A);
        Present(nv2A, memory, ref push, FirstSurface);
        Present(nv2A, memory, ref push, SecondSurface);
        nv2A.Write(Base + PcrtcStart, 4, FirstSurface);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        stream.Position = 0;
        var restored = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        restored.Backend = new MarkerBackend();
        using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        // The ring is device state: a load must not turn a backend frame into a
        // black scanout (or the other way around).
        Assert.Equal("backend", restored.DisplaySourceDebug);
        Assert.True(restored.TryCopyDisplayFrame(out _, out _, out byte[] bgra));
        Assert.Equal(MarkerBlue, bgra[0]);
    }
}
