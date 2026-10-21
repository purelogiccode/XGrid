using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live replace-mode composition tests (compatibility gap 8): a draw the GL
/// backend declines must not lose the frame's GL pixels. The backend composes
/// its FBO into the guest color surface once per frame, the software
/// rasterizer continues on that image, and the flip uploads it back instead of
/// serving the partial FBO. Returns early (pass) when no GL context exists.
/// </summary>
public sealed class ReplaceModeComposeLiveTests
{
    /// <summary>A 4x4 A8R8G8B8 surface plus a clip window that maps onto it.</summary>
    private const uint Surface = 0x2000u;
    private const uint Pitch = 16u;
    private const uint Width = 4u;
    private const uint Height = 4u;

    private readonly ITestOutputHelper _output;

    public ReplaceModeComposeLiveTests(ITestOutputHelper output)
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

    private static PgraphDrawInfo DrawInfo(PshCombiner combiner, bool unsupported)
    {
        // A combiner whose slot 1 is BUMPENVMAP is a slice-1-unsupported mode:
        // TryDraw declines it and the Nv2a would paint it in software.
        combiner.Control = 2u;
        combiner.StageProgram = unsupported ? (0x01u | (0x06u << 5)) : 0x01u;
        combiner.Final0 = 0x00000000u;
        combiner.Final1 = 0x00000001u;
        var vertex = new PgraphVertex(
            0f, 0f, 0.5f, 1f,
            1f, 1f, 1f, 1f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            1f, 1f, 1f, 1f,
            1f);
        var fragment = new PgraphFragmentState(
            alphaTestEnable: false, alphaFunc: 0u, alphaRef: 0u,
            blendEnable: false, blendSrcFactor: 0u, blendDstFactor: 0u, blendColor: 0u, blendEquation: 0u,
            depthTestEnable: false, depthFunc: 0u, depthMask: false,
            colorMask: 0x01010101u,
            stencilTestEnable: false, stencilWriteMask: 0u, stencilFunc: 0u, stencilRef: 0u,
            stencilReadMask: 0u, stencilOpFail: 0u, stencilOpZfail: 0u, stencilOpZpass: 0u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
        var texture = new PgraphTextureSlot(
            0u, 0u, 0u, 0u, 0u, 0u, filterBilinear: false, filterConvolution: 0);
        return new PgraphDrawInfo(
            vertex, vertex, vertex,
            clipX: 0u, clipY: 0u, clipW: Width, clipH: Height,
            colorBytes: 4u, zetaBytes: 0u,
            combinerActive: true,
            fragment,
            texture, texture, texture, texture,
            combiner,
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: false,
            colorSurfaceAddress: Surface,
            colorPitch: Pitch);
    }

    private static PgraphClearInfo ClearInfo()
    {
        return new PgraphClearInfo(
            clearColor: true, clearZeta: false,
            xMin: 0u, yMin: 0u, xMax: Width - 1u, yMax: Height - 1u,
            clipX: 0u, clipY: 0u, clipW: Width, clipH: Height,
            colorValue: 0xFF336699u, zetaValue: 0u,
            colorBytes: 4u, zetaBytes: 0u,
            zFormatFloat: false);
    }

    private static int CountComposedPixels(IGpuMemory memory)
    {
        int count = 0;
        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                if (memory.ReadPhysical(Surface + (y * Pitch) + (x * 4u)) == 0xFF336699u)
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void DeclinedDraw_ComposesFboIntoGuestRam_AndFlipServesIt()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            _output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        Assert.True(backend.ReplaceMode);
        try
        {
            var combiner = new PshCombiner();

            // 1. A clear fills the FBO with the frame's background colour.
            backend.Clear(ClearInfo());

            // 2. A draw the backend cannot honor hands the frame back: the
            //    FBO (the clear) must land in guest RAM before the software
            //    fill runs.
            Assert.False(backend.TryDraw(DrawInfo(combiner, unsupported: true)));
            Assert.Equal(Width * Height, (uint)CountComposedPixels(memory));
            Assert.Contains("composedFrames=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains($"composedPixels={Width * Height}", backend.DebugSummary, StringComparison.Ordinal);

            // 3. Every later draw of the same frame takes the software writer,
            //    even a shape the backend could have rendered.
            Assert.False(backend.TryDraw(DrawInfo(combiner, unsupported: false)));
            Assert.Contains("composedDraws=1", backend.DebugSummary, StringComparison.Ordinal);

            // 4. The flip re-uploads the composed guest image and serves it.
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4u, 1ul, Surface, Pitch));
            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(Width, width);
            Assert.Equal(Height, height);

            // The declined draw is still counted as unsupported (the GL
            // backend did not render it); the composition only makes the
            // presented frame complete, it does not claim fidelity.
            Assert.Equal(1, backend.BackendUnsupported);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            for (int i = 0; i < bgra.Length; i += 4)
            {
                Assert.Equal(0x99, bgra[i]);     // B
                Assert.Equal(0x66, bgra[i + 1]); // G
                Assert.Equal(0x33, bgra[i + 2]); // R
                Assert.Equal(0xFF, bgra[i + 3]); // A
            }
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
