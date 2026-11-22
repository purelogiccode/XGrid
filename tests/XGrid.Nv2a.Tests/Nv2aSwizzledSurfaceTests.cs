using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// XFP4: a <c>SURFACE_FORMAT_TYPE_SWIZZLE</c> surface stores its texels in the
/// log-dimension Morton order (xemu <c>surface_get_dimensions</c> +
/// <c>swizzle_rect</c>), not the pitch walk. For a 4x4 surface the reference's
/// <c>generate_swizzle_masks</c> yields <c>mask_x = 0b0101</c> and
/// <c>mask_y = 0b1010</c>, i.e. the Z-order table
/// <code>
///     0  1  4  5
///     2  3  6  7
///     8  9 12 13
///    10 11 14 15
/// </code>
/// The clears and the software fill must use those offsets; the tests seed the
/// linear-map word that a pitch walk would write and prove it stays untouched.
/// </summary>
public sealed class Nv2aSwizzledSurfaceTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const uint ColorBase = 0x3000u;
    private const uint ZetaBase = 0x5000u;
    private const uint Pitch = 0x10u; // 4 texels * 4 bytes

    // 4x4 A8R8G8B8, SWIZZLE type, log2 2x2 (no zeta).
    private const uint ColorOnlyFormat = 0x00220208u;

    // 4x4 A8R8G8B8 + Z24S8, SWIZZLE type, log2 2x2.
    private const uint ColorZetaFormat = 0x00220228u;

    private static readonly uint[] Morton4X4 =
    [
        0, 1, 4, 5,
        2, 3, 6, 7,
        8, 9, 12, 13,
        10, 11, 14, 15,
    ];

    private sealed class ArrayMemory(int size = 0x20000) : IGpuMemory
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

    private static System.Collections.Generic.List<(uint Method, uint Parameter)> SurfaceState(uint format)
    {
        return
        [
            (0x0208u, format),
            (0x020Cu, (Pitch << 16) | Pitch), // color and zeta pitch
            (0x0210u, ColorBase),
            (0x0214u, ZetaBase),
            (0x0200u, 0x00040000u), // clip H: x 0, width 4
            (0x0204u, 0x00040000u), // clip V: y 0, height 4
        ];
    }

    private static void PushTriangle(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        ArrayMemory memory)
    {
        float[][] vertices = [[2f, 2f], [12f, 2f], [2f, 12f]];
        for (int i = 0; i < 3; i++)
        {
            uint at = 0x2000u + ((uint)i * 16u);
            memory.WritePhysical(at, BitConverter.SingleToUInt32Bits(vertices[i][0]));
            memory.WritePhysical(at + 4u, BitConverter.SingleToUInt32Bits(vertices[i][1]));
            memory.WritePhysical(at + 8u, BitConverter.SingleToUInt32Bits(0.5f));
            memory.WritePhysical(at + 12u, BitConverter.SingleToUInt32Bits(1f));
        }

        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));

        // oPos = v0 (the position passthrough program).
        pushes.Add((0x1E9Cu, 0u));
        pushes.Add((0x0B00u, 0u));
        pushes.Add((0x0B04u, 0x0020001Bu));
        pushes.Add((0x0B08u, 0x0836006Cu));
        pushes.Add((0x0B0Cu, 0x0000F800u));
        pushes.Add((0x0B10u, 0u));
        pushes.Add((0x0B14u, 0x0020001Bu));
        pushes.Add((0x0B18u, 0x0C36006Cu));
        pushes.Add((0x0B1Cu, 0x0000F819u));
        pushes.Add((0x1EA4u, 0u));
        pushes.Add((0x0B80u, 0x3F800000u));
        pushes.Add((0x0B84u, 0x3F000000u));
        pushes.Add((0x0B88u, 0x3E800000u));
        pushes.Add((0x0B8Cu, 0x3F800000u));

        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));
    }

    private static uint Morton(uint x, uint y)
    {
        return Morton4X4[(y * 4u) + x];
    }

    private static uint ReadColor(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(ColorBase + (Morton(x, y) * 4u));
    }

    private static uint ReadZeta(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(ZetaBase + (Morton(x, y) * 4u));
    }

    private static uint ReadLinear(ArrayMemory memory, uint word)
    {
        return memory.ReadPhysical(ColorBase + (word * 4u));
    }

    [Fact]
    public void SwizzledColorClear_WritesTheMortonOffset()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState(ColorOnlyFormat);
        pushes.Add((0x1D90u, 0xFF112233u));
        pushes.Add((0x1D98u, 0x00020002u)); // clear xmin=xmax=2
        pushes.Add((0x1D9Cu, 0x00000000u)); // clear ymin=ymax=0
        pushes.Add((0x1D94u, 0x000000F0u));
        PushMethods(nv2A, memory, [.. pushes]);

        // (2,0) is Morton word 4, not the pitch walk's word 2.
        Assert.Equal(0xFF112233u, ReadColor(memory, 2, 0));
        Assert.Equal(0u, ReadLinear(memory, 2));
    }

    [Fact]
    public void SwizzledColorClear_MergeKeepsTheUnmaskedChannelsAtTheMortonOffset()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        for (uint word = 0; word < 16u; word++)
        {
            memory.WritePhysical(ColorBase + (word * 4u), 0x11223344u);
        }

        var pushes = SurfaceState(ColorOnlyFormat);
        pushes.Add((0x1D90u, 0xAA556677u));
        pushes.Add((0x1D98u, 0x00020002u));
        pushes.Add((0x1D9Cu, 0x00000000u));
        pushes.Add((0x1D94u, 0x00000010u)); // R only
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0x11553344u, ReadColor(memory, 2, 0));
        Assert.Equal(0x11223344u, ReadLinear(memory, 2));
    }

    [Fact]
    public void SwizzledZetaClear_WritesTheMortonOffset()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState(ColorZetaFormat);
        pushes.Add((0x1D8Cu, 0xFFFFFF00u));
        pushes.Add((0x1D98u, 0x00020002u));
        pushes.Add((0x1D9Cu, 0x00000000u));
        pushes.Add((0x1D94u, 0x00000001u)); // depth only
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0xFFFFFF00u, ReadZeta(memory, 2, 0));
        Assert.Equal(0u, memory.ReadPhysical(ZetaBase + 8u));
    }

    [Fact]
    public void SwizzledSurfaceDraw_WritesColorThroughTheMortonLayout()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState(ColorOnlyFormat);
        PushTriangle(pushes, memory);
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        // The triangle covers the 2x2 block at (2,2)-(3,3): Morton words
        // 12-15. The pitch walk's word for (2,2) is 10 and must stay zero.
        Assert.NotEqual(0u, ReadColor(memory, 2, 2));
        Assert.NotEqual(0u, ReadColor(memory, 3, 3));
        Assert.Equal(0u, ReadLinear(memory, 10));
    }

    [Fact]
    public void SwizzledSurfaceDraw_WritesZetaThroughTheMortonLayout()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState(ColorZetaFormat);
        pushes.Add((0x1D90u, 0x00000000u)); // color clear value (black)
        pushes.Add((0x1D8Cu, 0x00000000u)); // zeta clear value
        pushes.Add((0x1D98u, 0x00030000u));
        pushes.Add((0x1D9Cu, 0x00030000u));
        pushes.Add((0x1D94u, 0x000000F3u)); // clear color + zeta
        pushes.Add((0x32Cu, 0u)); // stencil off
        pushes.Add((0x30Cu, 1u)); // depth test enable
        pushes.Add((0x354u, 0x207u)); // depth func always (D3D-encoded)
        pushes.Add((0x35Cu, 1u)); // depth write enable
        PushTriangle(pushes, memory);
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        // The cleared zeta is zero; the draw's depth write must land on the
        // Morton words of the covered pixels (12-15), never the pitch word 10.
        Assert.NotEqual(0u, ReadZeta(memory, 2, 2));
        Assert.Equal(0u, memory.ReadPhysical(ZetaBase + (10u * 4u)));
    }
}
