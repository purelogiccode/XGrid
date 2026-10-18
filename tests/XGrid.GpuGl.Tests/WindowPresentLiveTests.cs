using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live window-present tests (opt-in <c>--window</c>): with the window
/// enabled the flip path shows/presents to a visible window and counts
/// presents, while the readback bytes and the headless contract stay
/// untouched; with the window disabled nothing changes. Returns early (pass)
/// when no GL context is available — the row is exercised on any GL-capable
/// host.
/// </summary>
public sealed class WindowPresentLiveTests
{
    private readonly ITestOutputHelper _output;

    public WindowPresentLiveTests(ITestOutputHelper output)
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
    public void WindowDisabled_ReportsOff_AndPresentsNothing()
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
            var gl = Assert.IsType<GlPgraphBackend>(backend);
            Assert.Equal("off", gl.WindowStatus);
            memory.WritePhysical(0x1000u, 0xFF203040u);
            gl.Flip(new PgraphSurfaceRecord(1, 1, 4, 1, 0x1000u, 4u));
            Assert.Equal(0, gl.WindowPresents);
            Assert.Equal("off", gl.WindowStatus);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void WindowEnabled_PresentsOnFlip_AndReadbackUnchanged()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(memory, windowEnabled: true, out IPgraphBackend? backend,
            out string why);
        if (!created || backend is null)
        {
            _output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            var gl = Assert.IsType<GlPgraphBackend>(backend);
            Assert.Equal("pending first frame", gl.WindowStatus);
            memory.WritePhysical(0x1000u, 0xFF203040u);
            gl.Flip(new PgraphSurfaceRecord(1, 1, 4, 1, 0x1000u, 4u));

            // The readback is untouched by the window path (never mixed).
            Assert.True(gl.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(1u, width);
            Assert.Equal(1u, height);
            Assert.Equal(new byte[] { 0x40, 0x30, 0x20, 0xFF }, bgra);

            if (gl.WindowStatus.StartsWith("window ", StringComparison.Ordinal))
            {
                Assert.Equal(1, gl.WindowPresents);
            }
            else
            {
                // Headless/session-0: the window degrades like a probe failure
                // and the session continues headless.
                _output.WriteLine("window unavailable in this session: " + gl.WindowStatus);
                Assert.Contains("window unavailable", gl.WindowStatus);
                Assert.Equal(0, gl.WindowPresents);
            }
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }
}
