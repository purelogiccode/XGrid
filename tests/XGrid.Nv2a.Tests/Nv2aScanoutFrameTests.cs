using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the distinction between the two exportable surfaces: the last flipped
/// 3D colour surface (<see cref="Nv2AMmio.TryCopyPresentedFrame"/>) and the
/// scanout buffer the display controller is reading
/// (<see cref="Nv2AMmio.TryCopyScanoutFrame"/>).
/// </summary>
/// <remarks>
/// The distinction matters because programs that render straight into the
/// framebuffer - nxdk's <c>XVideoGetFB</c> and <c>debugPrint</c> - never flip and
/// never draw, so only the scanout carries their output. Measured on the corpus:
/// <c>gamma</c> exports 1 colour through the flip path and 768 through the
/// scanout (decisions 207d).
/// </remarks>
public sealed class Nv2aScanoutFrameTests
{
    private const uint Base = Nv2AMmio.BaseAddress;
    private const uint PcrtcStart = 0x600800u;

    private const uint FlippedSurface = 0xC000u;
    private const uint ScanoutSurface = 0xE000u;
    private const uint Pitch = 8u; // 2 px * 4 B
    private const uint Width = 2u;
    private const uint Height = 2u;

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

    private static void Fill(ArrayMemory memory, uint surface, uint pixel)
    {
        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                memory.WritePhysical(surface + (y * Pitch) + (x * 4u), pixel);
            }
        }
    }

    /// <summary>Presents a 2x2 surface at <paramref name="surface"/> via one flip.</summary>
    private static void Present(Nv2AMmio nv2A, ArrayMemory memory, uint surface)
    {
        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u), // pitch-type surface, 4-byte colour
            (0x020Cu, Pitch),
            (0x0200u, Width << 16),
            (0x0204u, Height << 16),
            (0x0210u, surface),
            (0x012Cu, 0u)); // flip increment write
    }

    private const uint Blue = 0xFFFF0000u;  // BGRA: B=00 G=00 R=FF
    private const uint Green = 0xFF00FF00u; // BGRA: B=00 G=FF R=00

    [Fact]
    public void ScanoutAndFlippedSurface_AreReadFromDifferentBuffers()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        Fill(memory, FlippedSurface, Green);
        Fill(memory, ScanoutSurface, Blue);
        Present(nv2A, memory, FlippedSurface);
        nv2A.Write(Base + PcrtcStart, 4, ScanoutSurface);

        Assert.True(nv2A.TryCopyPresentedFrame(out uint flipWidth, out uint flipHeight, out byte[] flipBgra));
        Assert.True(nv2A.TryCopyScanoutFrame(out uint scanWidth, out uint scanHeight, out byte[] scanBgra));

        Assert.Equal(Width, flipWidth);
        Assert.Equal(Height, flipHeight);
        Assert.Equal(Width, scanWidth);
        Assert.Equal(Height, scanHeight);
        Assert.Equal((int)(Width * Height * 4), flipBgra.Length);
        Assert.Equal((int)(Width * Height * 4), scanBgra.Length);

        // The flipped surface is green (G=0xFF), the scanout is blue (R=0xFF):
        // the two exports must not be the same bytes.
        Assert.Equal(0xFF, flipBgra[1]);
        Assert.Equal(0x00, flipBgra[2]);
        Assert.Equal(0x00, scanBgra[1]);
        Assert.Equal(0xFF, scanBgra[2]);
        Assert.NotEqual(Convert.ToHexString(flipBgra), Convert.ToHexString(scanBgra), StringComparer.Ordinal);
    }

    [Fact]
    public void ScanoutExport_RequiresAScanoutAndAPresentedGeometry()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // No flip yet: no geometry to read either buffer with.
        Assert.False(nv2A.TryCopyScanoutFrame(out _, out _, out _));
        Assert.False(nv2A.TryCopyPresentedFrame(out _, out _, out _));

        Fill(memory, FlippedSurface, Green);
        Present(nv2A, memory, FlippedSurface);

        // Geometry exists, but PCRTC START is still zero: no scanout to read.
        Assert.False(nv2A.TryCopyScanoutFrame(out _, out _, out _));
        Assert.True(nv2A.TryCopyPresentedFrame(out _, out _, out _));
    }

    [Fact]
    public void ScanoutExport_MatchesTheGuestBytesAtTheScanoutBase()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        Fill(memory, FlippedSurface, Green);

        // Distinct per-pixel values so a row/pitch mistake cannot pass.
        memory.WritePhysical(ScanoutSurface + 0u, 0xFF000001u);
        memory.WritePhysical(ScanoutSurface + 4u, 0xFF000002u);
        memory.WritePhysical(ScanoutSurface + Pitch + 0u, 0xFF000003u);
        memory.WritePhysical(ScanoutSurface + Pitch + 4u, 0xFF000004u);

        Present(nv2A, memory, FlippedSurface);
        nv2A.Write(Base + PcrtcStart, 4, ScanoutSurface);

        Assert.True(nv2A.TryCopyScanoutFrame(out _, out _, out byte[] bgra));
        Assert.Equal(0x01, bgra[0]);
        Assert.Equal(0x02, bgra[4]);
        Assert.Equal(0x03, bgra[8]);
        Assert.Equal(0x04, bgra[12]);
    }
}
