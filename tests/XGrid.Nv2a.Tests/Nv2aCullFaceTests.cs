using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the cull-face translation: which screen-space winding counts as front
/// facing, and which winding <c>SET_CULL_FACE</c> discards.
/// </summary>
/// <remarks>
/// Screen space is Y-down, so a visually clockwise triangle has a positive
/// signed area under <c>((bx-ax)*(cy-ay)) - ((cx-ax)*(by-ay))</c>. The method
/// values are xemu's <c>NV097_SET_CULL_FACE_V_*</c> (0x0404 front, 0x0405 back,
/// 0x0406 both) and <c>NV097_SET_FRONT_FACE_V_*</c> (0x0900 CW, 0x0901 CCW).
/// </remarks>
public sealed class Nv2aCullFaceTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const uint CullFaceFront = 0x0404u;
    private const uint CullFaceBack = 0x0405u;
    private const uint CullFaceFrontAndBack = 0x0406u;
    private const uint FrontFaceCw = 0x0900u;
    private const uint FrontFaceCcw = 0x0901u;

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
        var memory = new ArrayMemory(0x20000);
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

    /// <summary>Writes three F32x4 screen-space vertices at 0x2000.</summary>
    private static void WriteTriangle(ArrayMemory memory, bool clockwise)
    {
        // A clockwise triangle in Y-down screen space has positive signed area.
        WriteVertices(memory, clockwise
            ? [[2f, 2f], [12f, 2f], [2f, 12f]]
            : [[2f, 2f], [2f, 12f], [12f, 2f]]);
    }

    private static void WriteVertices(ArrayMemory memory, float[][] vertices)
    {
        for (int i = 0; i < 3; i++)
        {
            uint at = 0x2000u + ((uint)i * 16u);
            memory.WritePhysical(at, BitConverter.SingleToUInt32Bits(vertices[i][0]));
            memory.WritePhysical(at + 4u, BitConverter.SingleToUInt32Bits(vertices[i][1]));
            memory.WritePhysical(at + 8u, 0u);
            memory.WritePhysical(at + 12u, BitConverter.SingleToUInt32Bits(1f));
        }
    }

    /// <summary>Draws an arbitrary triangle with the common state.</summary>
    private static (long Culled, ulong Pixels) DrawVertices(
        float[][] vertices,
        uint cullEnable,
        uint cullFace,
        uint frontFace)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteVertices(memory, vertices);
        PushDrawState(nv2A, memory, cullEnable, cullFace, frontFace);
        return (nv2A.TrianglesCulled, nv2A.DrawnPixels);
    }

    private static (long Culled, ulong Pixels) Draw(
        bool clockwise,
        uint cullEnable,
        uint cullFace,
        uint frontFace)
    {
        return DrawVertices(
            clockwise
                ? [[2f, 2f], [12f, 2f], [2f, 12f]]
                : [[2f, 2f], [2f, 12f], [12f, 2f]],
            cullEnable,
            cullFace,
            frontFace);
    }

    private static void PushDrawState(
        Nv2AMmio nv2A,
        ArrayMemory memory,
        uint cullEnable,
        uint cullFace,
        uint frontFace)
    {
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            // 16x16 A8R8G8B8 surface at 0x3000 (pitch 0x40 = 16 pixels).
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x0308u, cullEnable),
            (0x039Cu, cullFace),
            (0x03A0u, frontFace),
            // Position program: oPos = v0 (the same words PgraphBackendSeamTests uses
            // for its software-draw oracle).
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

            // Element 0 is implied by BEGIN_END; the two pushes supply 1 and 2.
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u)
        };

        PushMethods(nv2A, memory, [.. pushes]);
    }

    [Fact]
    public void CullingDisabled_DrawsBothWindings()
    {
        (long cwCulled, ulong cwPixels) = Draw(clockwise: true, cullEnable: 0u, CullFaceBack, FrontFaceCw);
        (long ccwCulled, ulong ccwPixels) = Draw(clockwise: false, cullEnable: 0u, CullFaceBack, FrontFaceCw);

        Assert.Equal(0, cwCulled);
        Assert.Equal(0, ccwCulled);
        Assert.True(cwPixels > 0);
        Assert.True(ccwPixels > 0);
    }

    [Fact]
    public void CullBack_KeepsFrontWindingAndDiscardsTheOther()
    {
        // Front face = CW, cull = BACK: the clockwise triangle survives.
        (long cwCulled, ulong cwPixels) = Draw(clockwise: true, cullEnable: 1u, CullFaceBack, FrontFaceCw);
        (long ccwCulled, ulong ccwPixels) = Draw(clockwise: false, cullEnable: 1u, CullFaceBack, FrontFaceCw);

        Assert.Equal(0, cwCulled);
        Assert.True(cwPixels > 0);
        Assert.Equal(1, ccwCulled);
        Assert.Equal(0UL, ccwPixels);
    }

    [Fact]
    public void CullFront_KeepsBackWindingAndDiscardsTheOther()
    {
        (long cwCulled, ulong cwPixels) = Draw(clockwise: true, cullEnable: 1u, CullFaceFront, FrontFaceCw);
        (long ccwCulled, ulong ccwPixels) = Draw(clockwise: false, cullEnable: 1u, CullFaceFront, FrontFaceCw);

        Assert.Equal(1, cwCulled);
        Assert.Equal(0UL, cwPixels);
        Assert.Equal(0, ccwCulled);
        Assert.True(ccwPixels > 0);
    }

    [Fact]
    public void FrontFaceCcwSwapsWhichWindingSurvives()
    {
        // Same geometry, opposite front-face convention: the survivor flips.
        (long cwCulled, _) = Draw(clockwise: true, cullEnable: 1u, CullFaceBack, FrontFaceCcw);
        (long ccwCulled, _) = Draw(clockwise: false, cullEnable: 1u, CullFaceBack, FrontFaceCcw);

        Assert.Equal(1, cwCulled);
        Assert.Equal(0, ccwCulled);
    }

    [Fact]
    public void CullFrontAndBack_DiscardsEverything()
    {
        (long cwCulled, ulong cwPixels) = Draw(clockwise: true, cullEnable: 1u, CullFaceFrontAndBack, FrontFaceCw);
        (long ccwCulled, ulong ccwPixels) = Draw(
            clockwise: false,
            cullEnable: 1u,
            CullFaceFrontAndBack,
            FrontFaceCw);

        Assert.Equal(1, cwCulled);
        Assert.Equal(1, ccwCulled);
        Assert.Equal(0UL, cwPixels);
        Assert.Equal(0UL, ccwPixels);
    }

    [Fact]
    public void DegenerateTriangle_IsRejectedWithoutACullCount()
    {
        // Two vertices coincide: area == 0, rejected before culling.
        (long culled, ulong pixels) = DrawVertices(
            [[2f, 2f], [2f, 2f], [12f, 12f]], cullEnable: 1u, CullFaceBack, FrontFaceCw);

        Assert.Equal(0, culled);
        Assert.Equal(0UL, pixels);
    }

    [Fact]
    public void TriangleOutsideTheClipWindow_IsRejected()
    {
        // The 16x16 surface clip (0x200/0x204) excludes a triangle at 100..112.
        (long culled, ulong pixels) = DrawVertices(
            [[100f, 100f], [112f, 100f], [100f, 112f]], cullEnable: 0u, CullFaceBack, FrontFaceCw);

        Assert.Equal(0, culled);
        Assert.Equal(0UL, pixels);
    }

    [Fact]
    public void CullStateRoundTripsThroughASnapshot()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        WriteTriangle(memory, clockwise: true);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            (0x0308u, 1u),
            (0x039Cu, CullFaceFront),
            (0x03A0u, FrontFaceCcw),
        };
        PushMethods(nv2A, memory, [.. pushes]);

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        buffer.Position = 0;
        (Nv2AMmio restored, ArrayMemory _) = CreateChannel();
        using (var reader = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        using var second = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(second, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        Assert.Equal(Convert.ToHexString(buffer.ToArray()), Convert.ToHexString(second.ToArray()));
    }
}
