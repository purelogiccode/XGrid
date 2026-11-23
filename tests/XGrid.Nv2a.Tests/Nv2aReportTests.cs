using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// XFP5: the ZPASS_PIXEL_CNT occlusion report (xemu
/// <c>pgraph/gl/reports.c</c> + <c>pgraph_write_zpass_pixel_cnt_report</c>).
/// The software rasterizer counts fragments that pass the depth test instead
/// of wrapping draws in GL_SAMPLES_PASSED queries; GET_REPORT adds the count
/// since the last GET to the cumulative result and writes the 16-byte report
/// block (timestamp, result, done) at the parameter's 24-bit offset in the
/// report DMA context. CLEAR_REPORT_VALUE resets both the pending count and
/// the result.
/// </summary>
public sealed class Nv2aReportTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const uint ColorBase = 0x3000u;
    private const uint ZetaBase = 0x5000u;
    private const uint Pitch = 0x40u;

    private const uint ReportHandle = 0x42u;
    private const uint ReportOffset = 0x9000u;
    private const uint ReportParameter = ReportOffset | (1u << 24); // type ZPASS

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

        // DMA object at PRAMIN 0x111C0: in-memory class, limit 0xFFFF,
        // base 0 (the same object the surface tests use).
        nv2A.Write(Base + 0x700000u + 0x111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x700000u + 0x111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x700000u + 0x111C8u, 4, 0x00000000u);

        // RAMHT entry (base 0, size 4096, channel 0): handle -> instance
        // 0x111C0 with the valid bit.
        uint ramht = ReportHandle * 8u;
        nv2A.Write(Base + 0x700000u + ramht, 4, ReportHandle);
        nv2A.Write(Base + 0x700000u + ramht + 4u, 4, 0x80000000u | (0x111C0u >> 4));
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

    private static System.Collections.Generic.List<(uint Method, uint Parameter)> SurfaceState()
    {
        return
        [
            (0x0208u, 0x00000124u), // pitch type, color 4-byte, zeta Z24S8
            (0x020Cu, 0x00400040u), // color pitch 0x40, zeta pitch 0x40
            (0x0210u, ColorBase),
            (0x0214u, ZetaBase),
            (0x0200u, 0x00100000u), // clip H: x 0, width 16
            (0x0204u, 0x00100000u), // clip V
        ];
    }

    private static void PushZetaClear(System.Collections.Generic.List<(uint Method, uint Parameter)> pushes,
        uint clearWord)
    {
        pushes.Add((0x1D8Cu, clearWord));
        pushes.Add((0x1D98u, 0x000F0000u));
        pushes.Add((0x1D9Cu, 0x000F0000u));
        pushes.Add((0x1D94u, 0x00000003u));
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
            memory.WritePhysical(at + 8u, 0u);
            memory.WritePhysical(at + 12u, BitConverter.SingleToUInt32Bits(1f));
        }

        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));

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

    private static uint ReadReport(ArrayMemory memory, uint offset)
    {
        return memory.ReadPhysical(ReportOffset + offset);
    }

    [Fact]
    public void GetReport_WritesTheTimestampResultAndDoneBlock_AndAccumulates()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        pushes.Add((0x1A8u, ReportHandle)); // SET_CONTEXT_DMA_REPORT
        pushes.Add((0x17CCu, 1u)); // SET_ZPASS_PIXEL_COUNT_ENABLE
        PushTriangle(pushes, memory);
        pushes.Add((0x17D0u, ReportParameter)); // GET #1: result = X
        pushes.Add((0x17D0u, ReportParameter)); // GET #2: no draws, still X
        PushTriangle(pushes, memory);
        pushes.Add((0x17D0u, ReportParameter)); // GET #3: 2X
        pushes.Add((0x17C8u, 1u)); // CLEAR: result 0
        pushes.Add((0x17D0u, ReportParameter)); // GET #4: 0
        pushes.Add((0x17C8u, 1u)); // CLEAR again
        PushTriangle(pushes, memory);
        pushes.Add((0x17D0u, ReportParameter)); // GET #5: X since the clear
        PushMethods(nv2A, memory, [.. pushes]);

        // Three identical triangles; the report count per draw is X.
        uint drawn = (uint)nv2A.DrawnPixels;
        Assert.True(drawn > 0 && drawn % 3u == 0, $"drawn={drawn}");
        uint perDraw = drawn / 3u;
        Assert.Equal(0x44556677u, ReadReport(memory, 0)); // timestamp low
        Assert.Equal(0x00112233u, ReadReport(memory, 4)); // timestamp high
        Assert.Equal(perDraw, ReadReport(memory, 8)); // GET #5 (last write)
        Assert.Equal(0u, ReadReport(memory, 12)); // done
        Assert.Equal(5L, nv2A.ZpassReports);
    }
    [Fact]
    public void Zpass_CountsOnlyFragmentsThatPassTheDepthTest()
    {
        // Depth test LESS against the far clear (1.0): every fragment passes.
        uint passing = CountWithDepthTest(0xFFFFFF00u);
        Assert.True(passing > 0, "far clear should pass the LESS test");

        // Depth test LESS against the near clear (0.0): no fragment passes.
        Assert.Equal(0u, CountWithDepthTest(0x00000000u));

        static uint CountWithDepthTest(uint zetaClear)
        {
            (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
            var pushes = SurfaceState();
            pushes.Add((0x1A8u, ReportHandle));
            pushes.Add((0x17CCu, 1u));
            pushes.Add((0x30Cu, 1u)); // depth test enable
            pushes.Add((0x354u, 0x201u)); // depth func LESS
            pushes.Add((0x35Cu, 1u)); // depth write enable
            PushZetaClear(pushes, zetaClear);
            PushTriangle(pushes, memory);
            pushes.Add((0x17D0u, ReportParameter));
            PushMethods(nv2A, memory, [.. pushes]);
            Assert.Equal(1uL, nv2A.DrawsExecuted);
            return ReadReport(memory, 8);
        }
    }

    [Fact]
    public void Zpass_DisabledCountsZero_ButStillWritesTheReport()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        pushes.Add((0x1A8u, ReportHandle));
        PushTriangle(pushes, memory);
        pushes.Add((0x17D0u, ReportParameter));
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0u, ReadReport(memory, 8));
        Assert.Equal(0x44556677u, ReadReport(memory, 0));
        Assert.Equal(1L, nv2A.ZpassReports);
    }

    [Fact]
    public void GetReport_UnknownTypeIsIgnored()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var pushes = SurfaceState();
        pushes.Add((0x1A8u, ReportHandle));
        pushes.Add((0x17CCu, 1u));
        PushTriangle(pushes, memory);

        // type 2 is not ZPASS_PIXEL_CNT (the reference asserts).
        pushes.Add((0x17D0u, ReportOffset | (2u << 24)));
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(0u, ReadReport(memory, 0));
        Assert.Equal(0L, nv2A.ZpassReports);
        Assert.Equal(1L, nv2A.ZpassReportTypeMisses);
    }
}
