using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live clear-state tests: a PGRAPH clear is a raw surface fill, so the draw
/// write masks (color / depth / stencil) must not suppress it. GL's glClear
/// honors those masks, so the backend lifts them for the clear and restores
/// them for the next draw. Returns early (pass) when no GL context is
/// available, like the other live rows.
/// </summary>
public sealed class ClearMaskLiveTests
{
    private readonly ITestOutputHelper _output;

    public ClearMaskLiveTests(ITestOutputHelper output)
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

    private static PgraphVertex Vertex(float x, float y, float r)
    {
        return new PgraphVertex(
            x, y, 0.5f, 1f,
            r, r, r, 1f,
            0f, 0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            1f, 1f, 1f, 1f,
            0f);
    }

    private static PgraphFragmentState Fragment(uint colorMask)
    {
        return new PgraphFragmentState(
            alphaTestEnable: false, alphaFunc: 0u, alphaRef: 0u,
            blendEnable: false, blendSrcFactor: 1u, blendDstFactor: 0u, blendColor: 0u, blendEquation: 0u,
            depthTestEnable: false, depthFunc: 1u, depthMask: false,
            colorMask,
            stencilTestEnable: false, stencilWriteMask: 0u, stencilFunc: 1u, stencilRef: 0u,
            stencilReadMask: 0u, stencilOpFail: 0x1E00u, stencilOpZfail: 0x1E00u, stencilOpZpass: 0x1E00u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
    }

    private static PgraphDrawInfo Draw(uint colorMask)
    {
        var slot = new PgraphTextureSlot(0u, 0u, 0u, 0u, 0u, 0x00100010u);
        return new PgraphDrawInfo(
            Vertex(0f, 0f, 1f), Vertex(16f, 0f, 0f), Vertex(0f, 16f, 0f),
            0u, 0u, 16u, 16u,
            colorBytes: 4u, zetaBytes: 0u,
            combinerActive: false,
            Fragment(colorMask),
            slot, slot, slot, slot,
            new PshCombiner(),
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: false,
            colorSurfaceAddress: 0u,
            colorPitch: 0u);
    }

    private static PgraphClearInfo ColorClear(uint value)
    {
        return new PgraphClearInfo(
            clearColor: true, clearZeta: false,
            xMin: 0u, yMin: 0u, xMax: 15u, yMax: 15u,
            clipX: 0u, clipY: 0u, clipW: 16u, clipH: 16u,
            colorValue: value, zetaValue: 0u,
            colorBytes: 4u, zetaBytes: 0u,
            zFormatFloat: false);
    }

    [Fact]
    public void ColorClear_IgnoresTheColorWriteMask()
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
            // A draw whose write mask blocks every channel loads that state.
            Assert.True(backend.TryDraw(Draw(colorMask: 0u)), "draw declined");

            // The clear must still fill the surface (software FillSurface writes
            // the value verbatim), even though the last draw masked color off.
            backend.Clear(ColorClear(0xFF336699u));
            backend.Flip(new PgraphSurfaceRecord(16, 16, 4, 1, 0x1000u, 64u));

            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(16u, width);
            Assert.Equal(16u, height);
            for (int i = 0; i < bgra.Length; i += 4)
            {
                Assert.Equal(0x99, bgra[i]);
                Assert.Equal(0x66, bgra[i + 1]);
                Assert.Equal(0x33, bgra[i + 2]);
                Assert.Equal(0xFF, bgra[i + 3]);
            }

            Assert.Equal(0, backend.BackendUnsupported);

            // The lifted masks are not left behind: a masked draw AFTER the
            // clear leaves the cleared color untouched.
            backend.Clear(ColorClear(0xFF0000FFu));
            Assert.True(backend.TryDraw(Draw(colorMask: 0u)), "draw declined");
            backend.Flip(new PgraphSurfaceRecord(16, 16, 4, 2, 0x1000u, 64u));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] second));
            for (int i = 0; i < second.Length; i += 4)
            {
                Assert.Equal(0xFF, second[i]);
                Assert.Equal(0x00, second[i + 1]);
                Assert.Equal(0x00, second[i + 2]);
                Assert.Equal(0xFF, second[i + 3]);
            }

            // ...and an unmasked draw still writes (the clear did not break the
            // draw-side mask path).
            Assert.True(backend.TryDraw(Draw(colorMask: 0x01010101u)), "draw declined");
            backend.Flip(new PgraphSurfaceRecord(16, 16, 4, 3, 0x1000u, 64u));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] third));
            bool changed = false;
            for (int i = 0; i < third.Length; i += 4)
            {
                if (third[i] != 0xFF || third[i + 1] != 0x00 || third[i + 2] != 0x00)
                {
                    changed = true;
                    break;
                }
            }

            Assert.True(changed, "unmasked draw wrote nothing");
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
