using System;
using XGrid.Nv2a;
using Xunit;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live present-upload tests: drive the real <see cref="IPgraphBackend"/>
/// (via <see cref="GlBackendFactory"/>) through Flip with an empty FBO and
/// prove the address-to-FBO-content mapping plus the non-empty-FBO rule.
/// Skips (never fails) when no GL context is available.
/// </summary>
public sealed class PresentUploadLiveTests
{
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

    [SkippableFact]
    public void Flip_EmptyFbo_UploadsGuestSurface_And_NonEmptyFbo_StaysUntouched()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(memory, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            Skip.If(true, "no GL context: " + why);
            throw new InvalidOperationException("Skip.If did not skip.");
        }

        try
        {
            // 3x2 A8R8G8B8 surface at 0x1000, tight pitch 12.
            const uint first = 0x1000u;
            memory.WritePhysical(first + 0u, 0xFF0A141Eu);
            memory.WritePhysical(first + 4u, 0xE1282832u);
            memory.WritePhysical(first + 8u, 0xC8505254u);
            memory.WritePhysical(first + 12u, 0xA0787A7Cu);
            memory.WritePhysical(first + 16u, 0xFF0000FFu);
            memory.WritePhysical(first + 20u, 0xFF00FF00u);

            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 1, first, 12u));
            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(3u, width);
            Assert.Equal(2u, height);
            byte[] expected =
            {
                0x1E, 0x14, 0x0A, 0xFF, 0x32, 0x28, 0x28, 0xE1, 0x54, 0x52, 0x50, 0xC8,
                0x7C, 0x7A, 0x78, 0xA0, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
            };
            Assert.Equal(expected, bgra);
            Assert.Equal(0, backend.BackendUnsupported);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);

            // Second flip at a different surface with the FBO now non-empty:
            // the upload path must not touch it (staleness detection stays
            // open — the first frame is served again, provably stale).
            const uint second = 0x2000u;
            for (uint i = 0; i < 6; i++)
            {
                memory.WritePhysical(second + (i * 4u), 0xFFFF0000u);
            }

            backend.Flip(new PgraphSurfaceRecord(3, 2, 4, 2, second, 12u));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra2));
            Assert.Equal(bgra, bgra2);
            Assert.Equal(0, backend.BackendUnsupported);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
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
