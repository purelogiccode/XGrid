using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Present-upload pixel-builder tests: the address-to-content mapping must
/// match <see cref="Nv2AMmio.TryCopyPresentedFrame"/> exactly (same
/// pitch/offset walk, same channel order), including padded-pitch repack,
/// and every non-uploadable record must return false without throwing.
/// </summary>
public sealed class PresentUploadTests
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
        const uint streamBase = 0x8000u;
        uint address = streamBase;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, (address + 4u) | 1u);
        nv2A.Write(Base + 0x800044u, 4, streamBase);
        nv2A.Write(Base + 0x800040u, 4, address + 4u);
    }

    [Fact]
    public void BuildUploadPixels_MapsAddressToRgba_InGuestRowOrder()
    {
        var memory = new ArrayMemory();
        const uint surface = 0x4000u;
        const uint pitch = 12u; // tight: 3 px * 4 B.
        memory.WritePhysical(surface + (0u * pitch) + 0u, 0xFF0A141Eu);
        memory.WritePhysical(surface + (0u * pitch) + 4u, 0xE1282832u);
        memory.WritePhysical(surface + (0u * pitch) + 8u, 0xC8505254u);
        memory.WritePhysical(surface + (1u * pitch) + 0u, 0xA0787A7Cu);
        memory.WritePhysical(surface + (1u * pitch) + 4u, 0xFF0000FFu);
        memory.WritePhysical(surface + (1u * pitch) + 8u, 0xFF00FF00u);

        Assert.True(PresentUpload.TryBuildUploadPixels(memory, surface, 3, 2, pitch, 4, out byte[]? rgba));
        Assert.NotNull(rgba);
        byte[] expected =
        [
            0x0A, 0x14, 0x1E, 0xFF, 0x28, 0x28, 0x32, 0xE1, 0x50, 0x52, 0x54, 0xC8,
            0x78, 0x7A, 0x7C, 0xA0, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        ];
        Assert.Equal(expected, rgba);
    }

    [Fact]
    public void BuildUploadPixels_MatchesSoftwareCopy_WithPaddedPitch()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // 8x4 surface at 0xC000 (clear of the 0x8000 method stream) with a
        // padded pitch (64 vs 32 row bytes) to pin the repack rule.
        const uint surface = 0xC000u;
        const uint pitch = 64u;
        for (uint y = 0; y < 4; y++)
        {
            for (uint x = 0; x < 8; x++)
            {
                uint pixel = 0xFF000000u | (x * 0x00100000u) | (y * 0x00001000u) | ((x + y) * 0x00000010u);
                memory.WritePhysical(surface + (y * pitch) + (x * 4u), pixel);
            }
        }

        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u), // surface format: pitch type, color 4-byte
            (0x020Cu, pitch), // color pitch
            (0x0200u, 0x00080000u), // clip H width=8
            (0x0204u, 0x00040000u), // clip V height=4
            (0x0210u, surface), // color offset
            (0x012Cu, 0u)); // flip increment write

        Assert.Equal(1uL, nv2A.FramesPresented);
        Assert.False(nv2A.PusherCapped);
        Assert.True(nv2A.TryCopyPresentedFrame(out uint swWidth, out uint swHeight, out byte[] swBgra));
        Assert.Equal(8u, swWidth);
        Assert.Equal(4u, swHeight);

        Assert.True(PresentUpload.TryBuildUploadPixels(memory, surface, 8, 4, pitch, 4, out byte[]? rgba));
        Assert.NotNull(rgba);

        // The FlipCore readback conversion (guest order preserved, R/B swap)
        // must reproduce the software copy byte-for-byte.
        byte[] viaReadback = new byte[rgba.Length];
        for (int i = 0; i < viaReadback.Length; i += 4)
        {
            viaReadback[i] = rgba[i + 2];
            viaReadback[i + 1] = rgba[i + 1];
            viaReadback[i + 2] = rgba[i];
            viaReadback[i + 3] = rgba[i + 3];
        }

        Assert.Equal(swBgra, viaReadback);
    }

    [Theory]
    [InlineData(0u, 2u, 8u, 4u)] // zero width
    [InlineData(3u, 0u, 12u, 4u)] // zero height
    [InlineData(4097u, 2u, 16388u, 4u)] // width over the dimension cap
    [InlineData(3u, 4097u, 12u, 4u)] // height over the dimension cap
    [InlineData(3u, 2u, 0u, 4u)] // zero pitch
    [InlineData(4u, 2u, 15u, 4u)] // pitch narrower than a row
    [InlineData(3u, 2u, 12u, 2u)] // non-4-byte format
    public void BuildUploadPixels_InvalidRecord_ReturnsFalse(
        uint width, uint height, uint pitch, uint colorBytes)
    {
        var memory = new ArrayMemory();
        Assert.False(PresentUpload.TryBuildUploadPixels(memory, 0x1000u, width, height, pitch, colorBytes,
            out byte[]? rgba));
        Assert.Null(rgba);
    }

    [Fact]
    public void BuildUploadPixels_SpanWrappingAddressSpace_ReturnsFalse()
    {
        var memory = new ArrayMemory();
        const uint address = 0xFFFFF000u;
        Assert.False(PresentUpload.TryBuildUploadPixels(memory, address, 64, 64, 256, 4, out byte[]? rgba));
        Assert.Null(rgba);
    }

    [Fact]
    public void BuildUploadPixels_NullMemory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PresentUpload.TryBuildUploadPixels(null!, 0x1000u, 3, 2, 12, 4, out byte[]? _));
    }
}
