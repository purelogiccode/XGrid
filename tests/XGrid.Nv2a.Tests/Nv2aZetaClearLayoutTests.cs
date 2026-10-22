using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the Z24S8 clear-word layout: depth in bits 8-31, stencil in bits 0-7,
/// the reference's <c>pgraph_get_clear_depth_stencil_value</c> reading
/// (<c>stencil = word &amp; 0xFF</c>, <c>depth = word &gt;&gt; 8</c>).
/// </summary>
/// <remarks>
/// Filed as a suspected defect (§211f): the gate titles clear with
/// <c>0xFFFFFF00</c>, where the stored depth is the same under either reading
/// and only the stencil differs (<c>0xFF</c> vs <c>0x00</c>), so the store and
/// the compare are pinned here explicitly instead of being inferred from a
/// gate anchor.
/// </remarks>
public sealed class Nv2aZetaClearLayoutTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const uint ColorBase = 0x3000u;
    private const uint ZetaBase = 0x5000u;
    private const uint Pitch = 0x40u;

    private const uint StencilFuncEqual = 2u;
    private const uint StencilAll = 0xFFu;
    private const uint StencilOpKeep = 0x1E00u;
    private const uint StencilOpReplace = 0x1E01u;

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

    /// <summary>
    /// Surface: 16x16 A8R8G8B8 color at 0x3000, Z24S8 zeta at 0x5000 (both
    /// pitches 0x40). Returns the pushed state so a caller can append a draw.
    /// </summary>
    private static System.Collections.Generic.List<(uint Method, uint Parameter)> SurfaceState()
    {
        return
        [
            (0x0208u, 0x00000124u), // pitch type, color 4-byte, zeta Z24S8
            (0x020Cu, 0x00400040u), // color pitch 0x40, zeta pitch 0x40
            (0x0210u, ColorBase),
            (0x0214u, ZetaBase),
            (0x0200u, 0x00100000u), // clip H: width 16
            (0x0204u, 0x00100000u), // clip V: height 16
        ];
    }

    private static void PushZetaClear(
        System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        uint clearWord)
    {
        pushes.Add((0x1D8Cu, clearWord));
        pushes.Add((0x1D98u, 0x000F0000u));
        pushes.Add((0x1D9Cu, 0x000F0000u));
        pushes.Add((0x1D94u, 0x00000003u)); // clear zeta
    }

    /// <summary>Triangle (2,2)-(12,2)-(2,12) in screen space via a passthrough program.</summary>
    private static void PushTriangle(
        System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        ArrayMemory memory)
    {
        float[][] vertices = [[2f, 2f], [12f, 2f], [2f, 12f]];
        for (int i = 0; i < 3; i++)
        {
            uint at = 0x2000u + ((uint)i * 16u);
            memory.WritePhysical(at, BitConverter.SingleToUInt32Bits(vertices[i][0]));
            memory.WritePhysical(at + 4u, BitConverter.SingleToUInt32Bits(vertices[i][1]));
            memory.WritePhysical(at + 8u, 0u);
            memory.WritePhysical(at + 12u, BitConverter.SingleToUInt32Bits(1f));
        }

        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));

        // oPos = v0 (the position passthrough program every NV2A test uses).
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

    private static uint ReadZeta(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(ZetaBase + (y * Pitch) + (x * 4u));
    }

    private static uint ReadPixel(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(ColorBase + (y * Pitch) + (x * 4u));
    }

    [Fact]
    public void ZetaClear_StoresTheWordVerbatim()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        PushZetaClear(pushes, 0x00711174u);
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0x00711174u, ReadZeta(memory, 0, 0));
        Assert.Equal(0x00711174u, ReadZeta(memory, 15, 15));
    }

    [Fact]
    public void StencilCompare_ReadsTheLowByteOfTheZetaClearWord()
    {
        // The cleared stencil is the low byte (0x74). A stencil test against
        // ref 0x74 must pass (the triangle writes color) and the same draw
        // against ref 0x00 must be rejected (the color surface stays zero); a
        // high-byte reading (0x00 in this word) would invert both.
        Assert.NotEqual(0u, DrawWithStencil(0x74u));
        Assert.Equal(0u, DrawWithStencil(0x00u));

        static uint DrawWithStencil(uint reference)
        {
            (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
            var pushes = SurfaceState();
            PushZetaClear(pushes, 0x00711174u);
            pushes.Add((0x32Cu, 1u)); // stencil test enable
            pushes.Add((0x364u, StencilFuncEqual));
            pushes.Add((0x368u, reference));
            pushes.Add((0x36Cu, StencilAll)); // read mask
            pushes.Add((0x370u, StencilOpKeep));
            pushes.Add((0x374u, StencilOpKeep));
            pushes.Add((0x378u, StencilOpKeep));
            PushTriangle(pushes, memory);
            PushMethods(nv2A, memory, [.. pushes]);

            Assert.Equal(1uL, nv2A.DrawsExecuted);
            return ReadPixel(memory, 5, 5);
        }
    }

    [Fact]
    public void StencilZpass_ReplaceWritesTheLowByteAndKeepsTheDepthField()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        PushZetaClear(pushes, 0xFF000000u); // depth field 0xFF0000, stencil 0
        pushes.Add((0x32Cu, 1u));
        pushes.Add((0x364u, 7u)); // always pass
        pushes.Add((0x368u, 0x5Au));
        pushes.Add((0x36Cu, StencilAll));
        pushes.Add((0x370u, StencilOpKeep));
        pushes.Add((0x374u, StencilOpKeep));
        pushes.Add((0x378u, StencilOpReplace));
        pushes.Add((0x35Cu, 0u)); // depth mask off: zeta depth must not move
        pushes.Add((0x1D90u, 0xFF00FF00u));
        PushTriangle(pushes, memory);
        PushMethods(nv2A, memory, [.. pushes]);

        // Stencil replaced inside the triangle, depth field untouched.
        Assert.Equal(0xFF00005Au, ReadZeta(memory, 5, 5));
        // Outside the triangle the clear word survives.
        Assert.Equal(0xFF000000u, ReadZeta(memory, 15, 0));
    }

    [Fact]
    public void GateClearWords_AgreeOnDepthAndDifferOnlyInTheStencil()
    {
        // 0xFFFFFF00 (the gate clears): depth 1.0 under either reading, and the
        // stencil is 0x00 - which is why only a synthetic stencil test can tell
        // the layouts apart (§211f).
        Assert.Equal(0xFFFFFFu, (0xFFFFFF00u >> 8) & 0xFFFFFFu);
        Assert.Equal(0x00u, 0xFFFFFF00u & 0xFFu);
        Assert.Equal(0x74u, 0x00711174u & 0xFFu);
    }

    [Fact]
    public void ZetaClear_IgnoresTheStencilWriteMask()
    {
        // A clear is a raw surface fill: the stencil write mask is draw state
        // (xemu's surface clear writes the word, it does not run the stencil
        // op path), so a clear with the stencil masked off still stores the
        // stencil byte.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        pushes.Add((0x358u, 0u)); // color write mask: nothing
        pushes.Add((0x32Cu, 1u)); // stencil test enabled
        pushes.Add((0x360u, 0u)); // stencil write mask: nothing
        PushZetaClear(pushes, 0x00711174u);
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0x00711174u, ReadZeta(memory, 0, 0));
        Assert.Equal(0x00711174u, ReadZeta(memory, 15, 15));
    }

    [Fact]
    public void ColorClear_IgnoresTheColorWriteMask()
    {
        // Same rule for the color surface: the clear writes every channel even
        // with the color mask off (the mask only gates draw writes).
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        pushes.Add((0x358u, 0u)); // color write mask: nothing
        pushes.Add((0x1D90u, 0xFF3366AAu)); // color clear value
        pushes.Add((0x1D98u, 0x000F0000u));
        pushes.Add((0x1D9Cu, 0x000F0000u));
        pushes.Add((0x1D94u, 0x000000F0u)); // clear color
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0xFF3366AAu, ReadPixel(memory, 0, 0));
        Assert.Equal(0xFF3366AAu, ReadPixel(memory, 15, 15));
    }
}
