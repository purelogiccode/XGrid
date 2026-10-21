using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live replace-mode composition tests (compatibility gap 8): a draw the GL
/// backend declines is painted by the software rasterizer into guest RAM, so
/// the FBO is no longer the frame. The next flip must compose the whole
/// presented surface from guest RAM (full-frame replace, never a mix) instead
/// of serving an FBO with a hole. Returns early (pass) when no GL context is
/// available, like the other live rows.
/// </summary>
public sealed class FallbackCompositionLiveTests
{
    private readonly ITestOutputHelper _output;

    public FallbackCompositionLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
            stencilTestEnable: false, stencilWriteMask: 0xFFu, stencilFunc: 1u, stencilRef: 0u,
            stencilReadMask: 0xFFu, stencilOpFail: 0x1E00u, stencilOpZfail: 0x1E00u, stencilOpZpass: 0x1E00u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
    }

    private static PgraphDrawInfo Draw(PshCombiner? combiner, bool combinerActive)
    {
        var slot = new PgraphTextureSlot(0u, 0u, 0u, 0u, 0u, 0x00100010u);
        return new PgraphDrawInfo(
            Vertex(0f, 0f), Vertex(16f, 0f), Vertex(0f, 16f),
            0u, 0u, 16u, 16u,
            colorBytes: 4u, zetaBytes: 0u,
            combinerActive,
            Fragment(),
            slot, slot, slot, slot,
            combiner,
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: false);
    }

    /// <summary>A combiner whose stage 1 asks for DOT_PRODUCT - a valid mode for that stage that slice 1 does not implement.</summary>
    private static PshCombiner UnsupportedCombiner()
    {
        var combiner = new PshCombiner();
        combiner.Control = 0x00000002u; // stages = 2
        combiner.StageProgram = 0x11u << 5; // slot 1 = DOT_PRODUCT
        combiner.Final1 = 0x00000001u;
        return combiner;
    }

    [Fact]
    public void DeclinedDraw_ComposesPresentedFrameFromGuestRam()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            _output.WriteLine("no GL context (" + why + "; live row not exercised.");
            return;
        }

        try
        {
            // 1) A legacy draw the backend honors creates the FBO.
            Assert.True(backend.TryDraw(Draw(new PshCombiner(), combinerActive: false)), backend.DebugSummary);

            // 2) A draw it declines in replace mode: software paints it, so the
            //    FBO no longer holds the frame.
            Assert.False(backend.TryDraw(Draw(UnsupportedCombiner(), combinerActive: true)), backend.DebugSummary);
            Assert.True(backend.BackendUnsupported > 0, "the declined draw must be counted");

            // 3) The guest surface has the software pixels (distinct values).
            const uint first = 0x1000u;
            for (uint i = 0; i < 6; i++)
            {
                memory.WritePhysical(first + (i * 4u), 0xFF000000u | (i << 16) | (i << 8) | i);
            }

            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 1, first, 12u));
            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(3u, width);
            Assert.Equal(2u, height);

            // The served frame is the guest surface, i.e. the whole frame was
            // replaced from RAM - not the legacy-draw FBO with a missing hole.
            byte[] expected =
            {
                0x00, 0x00, 0x00, 0xFF, 0x01, 0x01, 0x01, 0xFF, 0x02, 0x02, 0x02, 0xFF,
                0x03, 0x03, 0x03, 0xFF, 0x04, 0x04, 0x04, 0xFF, 0x05, 0x05, 0x05, 0xFF,
            };
            Assert.Equal(expected, bgra);
            Assert.Contains("composeUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);

            // 4) A frame with no declined draw serves the FBO again: the
            //    counter is per flip, so composition does not stick.
            Assert.True(backend.TryDraw(Draw(new PshCombiner(), combinerActive: false)));
            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 2, first, 12u));
            Assert.Contains("composeUploads=1", backend.DebugSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (backend is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    [Fact]
    public void MirrorMode_DeclinedDrawDoesNotCompose()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(memory, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            _output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            // Mirror mode renders the draw degraded and the software path still
            // paints the real pixels, so the FBO is never short a draw: the
            // flip must not drop it (the pre-composition behavior verbatim).
            Assert.True(backend.TryDraw(Draw(UnsupportedCombiner(), combinerActive: true))); backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 1, 0x1000u, 12u));
            Assert.Contains("composeUploads=0", backend.DebugSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (backend is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
