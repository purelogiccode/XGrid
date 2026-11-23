using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// XFP5: a SWIZZLE-type render target drawn by the GL backend is downloaded
/// to guest RAM after the draw batch in <b>Morton</b> order (the reference's
/// <c>surface_download</c> + <c>swizzle_rect</c>), so the guest's later sample
/// of that surface reads the rendered content. The discriminator: the
/// triangle covers pixel (2,0), whose Morton word is 4 while its pitch word is
/// 2; pixel (0,3) is covered too and its pitch word is 12 - an index that is
/// NOT the Morton word of any covered pixel - so a pitch walk would write it
/// and a Morton walk must leave it zero.
/// </summary>
public sealed class SwizzledTargetLiveTests(ITestOutputHelper output)
{
    private const uint TargetBase = 0x4000u;
    // 4x4 A8R8G8B8 SWIZZLE (log2 2x2): the surface the download writes.

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

    private static PgraphVertex Vertex(float x, float y)
    {
        return new PgraphVertex(
            x, y, 0.5f, 1f,
            1f, 1f, 1f, 1f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            1f, 1f, 1f, 1f,
            0f);
    }

    private static PgraphFragmentState Fragment()
    {
        return new PgraphFragmentState(
            alphaTestEnable: false, alphaFunc: 0u, alphaRef: 0u,
            blendEnable: false, blendSrcFactor: 1u, blendDstFactor: 0u, blendColor: 0u, blendEquation: 0u,
            depthTestEnable: false, depthFunc: 1u, depthMask: false,
            colorMask: 0x01010101u,
            stencilTestEnable: false, stencilWriteMask: 0u, stencilFunc: 1u, stencilRef: 0u,
            stencilReadMask: 0u, stencilOpFail: 0x1E00u, stencilOpZfail: 0x1E00u, stencilOpZpass: 0x1E00u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
    }

    private static PgraphDrawInfo Draw()
    {
        var slot = new PgraphTextureSlot(0u, 0u, 0u, 0u, 0u, 0x00100010u);
        return new PgraphDrawInfo(
            Vertex(0f, 0f), Vertex(4f, 0f), Vertex(0f, 4f),
            0u, 0u, 4u, 4u,
            colorBytes: 4u, zetaBytes: 0u,
            combinerActive: false,
            Fragment(),
            slot, slot, slot, slot,
            new PshCombiner(),
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: false,
            colorSurfaceAddress: TargetBase,
            colorPitch: 16u,
            colorSwizzle: true,
            colorSwizzleWidth: 4,
            colorSwizzleHeight: 4);
    }

    [Fact]
    public void SwizzledTarget_IsDownloadedToGuestRamInMortonOrder()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is not GlPgraphBackend live)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            backend.BeginDrawBatch();
            Assert.True(backend.TryDraw(Draw()), "swizzled draw declined: " + backend.DebugSummary);
            backend.EndDrawBatch();

            Assert.Contains("rtDownloads=1", backend.DebugSummary, StringComparison.Ordinal);

            // Pixel (2,0) is covered and lands at Morton word 4.
            Assert.Equal(0xFFFFFFFFu, memory.ReadPhysical(TargetBase + (4u * 4u)));
            // Index 12 is pixel (0,3)'s PITCH word; it is not the Morton word of
            // any covered pixel, so a pitch walk would have written it here.
            Assert.Equal(0u, memory.ReadPhysical(TargetBase + (12u * 4u)));
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void PitchTarget_IsNotDownloadedByTheSwizzlePath()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is not GlPgraphBackend live)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            // Same draw with ColorSwizzle false: no RT download may run (the
            // pitch present/compose paths own that surface).
            var slot = new PgraphTextureSlot(0u, 0u, 0u, 0u, 0u, 0x00100010u);
            var info = new PgraphDrawInfo(
                Vertex(0f, 0f), Vertex(4f, 0f), Vertex(0f, 4f),
                0u, 0u, 4u, 4u,
                colorBytes: 4u, zetaBytes: 0u,
                combinerActive: false,
                Fragment(),
                slot, slot, slot, slot,
                new PshCombiner(),
                fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
                shaderClipMode: 0u,
                zFormatFloat: false,
                colorSurfaceAddress: TargetBase,
                colorPitch: 16u);
            backend.BeginDrawBatch();
            Assert.True(backend.TryDraw(info), "pitch draw declined: " + backend.DebugSummary);
            backend.EndDrawBatch();

            Assert.DoesNotContain("rtDownloads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0u, memory.ReadPhysical(TargetBase));
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }
}
