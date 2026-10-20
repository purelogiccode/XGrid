using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live readback-policy tests: the per-flip CPU readback is opt-in (Track B
/// present path). A flip with readback disabled must leave the backend with no
/// CPU frame (so the fast path pays no ReadPixels and no BGRA swizzle), and
/// enabling it must restore the exact pre-Track-B behavior. Returns early
/// (pass) when no GL context is available - no extra test dependency.
/// </summary>
public sealed class FlipReadbackPolicyLiveTests
{
    private readonly ITestOutputHelper _output;

    public FlipReadbackPolicyLiveTests(ITestOutputHelper output)
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

    [Fact]
    public void FlipReadbackDisabled_ServesNoCpuFrame_And_EnabledRestoresIt()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: false, frameReadback: false,
            out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            _output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            const uint surface = 0x1000u;
            for (uint i = 0; i < 6; i++)
            {
                memory.WritePhysical(surface + (i * 4u), 0xFFFF0000u);
            }

            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 1, surface, 12u));
            Assert.False(backend.ReadbackPresented(out _, out _, out _));

            // The flip still armed/completed (the FBO is live and the frame is
            // presentable to a window); only the CPU copy is absent.
            GlPgraphBackend gl = Assert.IsType<GlPgraphBackend>(backend);
            Assert.Equal(1, gl.Flips);
            Assert.Equal(0, backend.BackendUnsupported);
            Assert.Contains("readbacks=0", backend.DebugSummary, StringComparison.Ordinal);

            // Opting in restores the exact pre-Track-B behavior.
            gl.FlipReadbackEnabled = true;
            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 2, surface, 12u));
            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(3u, width);
            Assert.Equal(2u, height);
            Assert.Equal(24, bgra.Length);
            // BSV1555 is not involved: A8R8G8B8 red -> BGRA 00 00 FF FF.
            Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, bgra[..4]);
            Assert.Contains("readbacks=1", backend.DebugSummary, StringComparison.Ordinal);
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
    public void FactoryDefault_KeepsReadbackEnabled()
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
            const uint surface = 0x1000u;
            for (uint i = 0; i < 6; i++)
            {
                memory.WritePhysical(surface + (i * 4u), 0xFF00FF00u);
            }

            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 1, surface, 12u));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x00, 0xFF }, bgra[..4]);
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
