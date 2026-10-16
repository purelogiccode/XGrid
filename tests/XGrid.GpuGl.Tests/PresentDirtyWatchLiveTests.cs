using System;
using System.IO;
using System.Text;
using XGrid.Memory;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live present-upload dirty-watch tests (research notes §132 (a*) /
/// wave-12 T2 acceptance): drive the real <see cref="GlPgraphBackend"/>
/// (via <see cref="GlBackendFactory"/>) through the four harness legs —
/// GL-rendered flip serves (clean), guest CPU write uploads (dirty), the
/// same write via the 0x80000000 alias uploads (choke-coverage regression),
/// and snapshot load / ResetState fail closed — plus the writer-identity
/// leg over the full NV2A pusher path (twin surface writes must not dirty).
/// Returns early (pass) when no GL context is available — same pattern as
/// <see cref="PresentUploadLiveTests"/>.
/// </summary>
public sealed class PresentDirtyWatchLiveTests
{
    private const uint SurfaceBase = 0x1000u;
    private const uint Width = 16;
    private const uint Height = 8;
    private const uint Pitch = Width * 4;

    // GL clear value (AARRGGBB) and its served BGRA bytes (FlipCore swaps
    // the readback R/B; MapClearColor maps r=(>>16) g=(>>8) b=(low)).
    private const uint GpuClearColor = 0xFF336699u;

    private static readonly byte[] GpuClearBgra = { 0x99, 0x66, 0x33, 0xFF };

    private readonly ITestOutputHelper _output;

    public PresentDirtyWatchLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Product-adapter mirror: IGpuMemory + IPresentDirtyWatch over the
    /// machine's AddressSpace (same shape as XboxMachine.PhysicalGpuMemory /
    /// Program.MachineGpuMemory — suppression-scoped WritePhysical so the
    /// NV2A twin identity leg exercises the real writer path).
    /// </summary>
    private sealed class WatchGpuMemory : IGpuMemory, IPresentDirtyWatch
    {
        private readonly AddressSpace _memory;

        public WatchGpuMemory(PhysicalMemory ram)
        {
            _memory = new AddressSpace(ram);
        }

        public PhysicalMemory Ram => _memory.Ram;

        public bool PresentDirty => _memory.Ram.PresentDirty;

        public uint ReadPhysical(uint address)
        {
            return _memory.Read32(address);
        }

        public void WritePhysical(uint address, uint value)
        {
            _memory.Ram.BeginGpuWriteSuppression();
            try
            {
                _memory.Write32(address, value);
            }
            finally
            {
                _memory.Ram.EndGpuWriteSuppression();
            }
        }

        public void ArmPresentWatch(uint address, uint length)
        {
            _memory.Ram.ArmPresentWatch(address, length);
        }

        public void DisarmPresentWatch()
        {
            _memory.Ram.DisarmPresentWatch();
        }

        public void MarkPresentDirty()
        {
            _memory.Ram.MarkPresentDirty();
        }

        public void ClearPresentDirty()
        {
            _memory.Ram.ClearPresentDirty();
        }
    }

    [Fact]
    public void GlClear_Flip_ServesGpuContent_NotGuestBytes()
    {
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        FillSurface(ram, GuestPattern(0x10));
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            backend.Clear(new PgraphClearInfo(
                clearColor: true, clearZeta: false,
                xMin: 0, yMin: 0, xMax: Width - 1, yMax: Height - 1,
                clipX: 0, clipY: 0, clipW: Width, clipH: Height,
                colorValue: GpuClearColor, zetaValue: 0,
                colorBytes: 4, zetaBytes: 0, zFormatFloat: false));
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 1, SurfaceBase, Pitch));

            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(Width, width);
            Assert.Equal(Height, height);
            Assert.Equal(Repeat(GpuClearBgra, (int)(Width * Height)), bgra);

            // The guest bytes at the surface are a DIFFERENT pattern: the
            // readback is provably the GL-rendered content (flipServed),
            // not a guest upload.
            Assert.NotEqual(Repeat(GuestPattern(0x10), Width * Height), bgra);
            Assert.Equal(1, ((GlPgraphBackend)backend).Flips);
            Assert.Contains("flipEmpty=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("presentUploads=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0, backend.BackendUnsupported);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void GuestCpuWrite_AfterServe_Flip_UploadsGuestBytes()
    {
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            // Leg (a): GL render + clean flip serves GPU content.
            backend.Clear(new PgraphClearInfo(
                clearColor: true, clearZeta: false,
                xMin: 0, yMin: 0, xMax: Width - 1, yMax: Height - 1,
                clipX: 0, clipY: 0, clipW: Width, clipH: Height,
                colorValue: GpuClearColor, zetaValue: 0,
                colorBytes: 4, zetaBytes: 0, zFormatFloat: false));
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 1, SurfaceBase, Pitch));
            Assert.Contains("presentUploads=0", backend.DebugSummary, StringComparison.Ordinal);

            // Leg (b): a guest CPU blit rewrites the presented surface;
            // the FBO is now stale and the next flip must upload.
            FillSurface(ram, GuestPattern(0x22));
            Assert.True(memory.PresentDirty);
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 2, SurfaceBase, Pitch));

            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            Assert.Equal(Repeat(GuestPattern(0x22), Width * Height), bgra);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0, backend.BackendUnsupported);

            // The upload made the FBO authoritative: a repeat flip with no
            // new guest write serves (no second upload).
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 3, SurfaceBase, Pitch));
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra2));
            Assert.Equal(Repeat(GuestPattern(0x22), Width * Height), bgra2);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void GuestCpuWrite_Via0x80000000Alias_Flip_Uploads()
    {
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        var alias = new RamAliasDevice(ram, 0x80000000u);
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            backend.Clear(new PgraphClearInfo(
                clearColor: true, clearZeta: false,
                xMin: 0, yMin: 0, xMax: Width - 1, yMax: Height - 1,
                clipX: 0, clipY: 0, clipW: Width, clipH: Height,
                colorValue: GpuClearColor, zetaValue: 0,
                colorBytes: 4, zetaBytes: 0, zFormatFloat: false));
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 1, SurfaceBase, Pitch));
            Assert.Contains("presentUploads=0", backend.DebugSummary, StringComparison.Ordinal);

            // §132 finding ii regression: the blit lands through the
            // 0x80000000 alias window, which resolves to the same flat
            // PhysicalMemory offset — the choke must see it.
            FillSurfaceViaAlias(alias, GuestPattern(0x33));
            Assert.True(memory.PresentDirty);
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 2, SurfaceBase, Pitch));

            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            Assert.Equal(Repeat(GuestPattern(0x33), Width * Height), bgra);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0, backend.BackendUnsupported);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SaveLoadMidSequence_Flip_UploadsLoadedBytes()
    {
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            // GL render + clean flip; the surface carries T1 bytes.
            FillSurface(ram, GuestPattern(0x41));
            backend.Clear(new PgraphClearInfo(
                clearColor: true, clearZeta: false,
                xMin: 0, yMin: 0, xMax: Width - 1, yMax: Height - 1,
                clipX: 0, clipY: 0, clipW: Width, clipH: Height,
                colorValue: GpuClearColor, zetaValue: 0,
                colorBytes: 4, zetaBytes: 0, zFormatFloat: false));
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 1, SurfaceBase, Pitch));
            Assert.Contains("presentUploads=0", backend.DebugSummary, StringComparison.Ordinal);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                ram.SaveState(writer);
            }

            // Post-save guest writes (dirty), then a snapshot load restores
            // the T1 bytes and must fail closed (dirty survives).
            FillSurface(ram, GuestPattern(0x55));
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            ram.LoadState(reader);
            Assert.True(memory.PresentDirty);

            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 2, SurfaceBase, Pitch));

            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            Assert.Equal(Repeat(GuestPattern(0x41), Width * Height), bgra);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0, backend.BackendUnsupported);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ResetState_Flip_UploadsRatherThanServesLeftovers()
    {
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            FillSurface(ram, GuestPattern(0x61));
            backend.Clear(new PgraphClearInfo(
                clearColor: true, clearZeta: false,
                xMin: 0, yMin: 0, xMax: Width - 1, yMax: Height - 1,
                clipX: 0, clipY: 0, clipW: Width, clipH: Height,
                colorValue: GpuClearColor, zetaValue: 0,
                colorBytes: 4, zetaBytes: 0, zFormatFloat: false));
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 1, SurfaceBase, Pitch));

            backend.ResetState();
            Assert.True(memory.PresentDirty);

            // No draw has rebuilt the FBO after the reset: the flip uploads
            // the guest surface instead of serving deleted-FBO leftovers.
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4, 2, SurfaceBase, Pitch));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            Assert.Equal(Repeat(GuestPattern(0x61), Width * Height), bgra);
            Assert.Contains("presentUploads=1", backend.DebugSummary, StringComparison.Ordinal);
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void TwinSurfaceWritesThroughAdapter_Flip_Serves_NoUpload()
    {
        // Writer identity (§132 finding i) over the full NV2A path: the
        // software twin's surface fills go through the suppression-scoped
        // IGpuMemory adapter and must not dirty the presented frame. Two
        // clear+flip iterations prove the SECOND flip (first one arms the
        // watched span) still serves GL content with presentUploads=0.
        var ram = new PhysicalMemory(0x10000);
        var memory = new WatchGpuMemory(ram);
        Nv2AMmio nv2A = CreateChannel(memory);
        IPgraphBackend? created = CreateBackend(memory);
        if (created is null)
        {
            return;
        }

        IPgraphBackend backend = created;

        try
        {
            // One stream carries both iterations (the pusher suspends with
            // Protection after a stream's jump word, so a second kick on the
            // same channel would be a no-op — PgraphBackendSeamTests
            // artifact): clear+flip, then clear+flip again.
            nv2A.Backend = backend;
            PushClearFlipTwice(nv2A, ram, firstColor: 0xFF336699u, secondColor: 0xFF993366u);
            Assert.Equal(2uL, nv2A.ClearsExecuted);
            Assert.Equal(2uL, nv2A.FramesPresented);
            Assert.Contains("presentUploads=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("flipStaleUploads=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Equal(0, backend.BackendUnsupported);
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] second));

            // The second twin fill wrote the new clear color into the
            // watched surface (suppressed -> stayed clean): the flip served
            // the GL-rendered content, never uploaded.
            byte[] secondExpected = { 0x66, 0x33, 0x99, 0xFF };
            Assert.Equal(Repeat(secondExpected, (int)second.Length / 4), second);
        }
        finally
        {
            nv2A.Backend = null;
            (backend as IDisposable)?.Dispose();
        }
    }

    private IPgraphBackend? CreateBackend(WatchGpuMemory memory)
    {
        if (!GlBackendFactory.TryCreate(memory, out IPgraphBackend? backend, out string why))
        {
            _output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return null;
        }

        return backend;
    }

    private static Nv2AMmio CreateChannel(WatchGpuMemory memory)
    {
        const uint nv2ABase = 0xFD000000u;
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(nv2ABase + 0x2504u, 4, 1u);
        nv2A.Write(nv2ABase + 0x3200u, 4, 1u);
        nv2A.Write(nv2ABase + 0x3204u, 4, 0x100u);
        nv2A.Write(nv2ABase + 0x3220u, 4, 1u);
        nv2A.Write(nv2ABase + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(nv2ABase + 0x3250u, 4, 1u);
        nv2A.Write(nv2ABase + 0x400720u, 4, 1u);
        nv2A.Write(nv2ABase + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(nv2ABase + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(nv2ABase + 0x7111C8u, 4, 0x00000000u);
        return nv2A;
    }

    private static void PushClearFlipTwice(Nv2AMmio nv2A, PhysicalMemory ram, uint firstColor, uint secondColor)
    {
        const uint streamBase = 0x8000u;
        (uint Method, uint Parameter)[] methods =
        {
            (0x0208u, 0x00000104u), // surface format: pitch type, color 4-byte
            (0x020Cu, 0x00000100u), // pitch 0x100
            (0x0200u, 0x00400000u), // clip H width=64
            (0x0204u, 0x00280000u), // clip V height=40
            (0x0210u, 0x00001000u), // color offset -> surface base 0x1000
            (0x1D90u, firstColor), // color clear value
            (0x1D98u, 0x03FF0000u), // clear rect H 0..1023
            (0x1D9Cu, 0x03FF0000u), // clear rect V 0..1023
            (0x1D94u, 0x000000F0u), // clear color enable
            (0x012Cu, 0u), // flip increment write (arms the watch)
            (0x1D90u, secondColor), // second clear value into the same surface
            (0x1D94u, 0x000000F0u),
            (0x012Cu, 0u), // second flip (must still serve)
        };

        uint address = streamBase;
        foreach ((uint method, uint parameter) in methods)
        {
            ram.Write32(address, (1u << 18) | method);
            ram.Write32(address + 4, parameter);
            address += 8;
        }

        ram.Write32(address, (address + 4u) | 1u);
        nv2A.Write(0xFD000000u + 0x800044u, 4, streamBase);
        nv2A.Write(0xFD000000u + 0x800040u, 4, address + 4u);
    }

    private static uint GuestPattern(uint seed)
    {
        return 0xFF000000u | ((seed * 0x10203u) & 0x00FFFFFFu);
    }

    private static void FillSurface(PhysicalMemory ram, uint pattern)
    {
        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                ram.Write32(SurfaceBase + (y * Pitch) + (x * 4), pattern);
            }
        }
    }

    private static void FillSurfaceViaAlias(RamAliasDevice alias, uint pattern)
    {
        for (uint y = 0; y < Height; y++)
        {
            for (uint x = 0; x < Width; x++)
            {
                alias.Write(0x80000000u + SurfaceBase + (y * Pitch) + (x * 4), 4, pattern);
            }
        }
    }

    /// <summary>Builds the served-BGRA expectation for a uniform guest dword pattern.</summary>
    private static byte[] Repeat(uint pattern, uint pixels)
    {
        int count = (int)pixels;
        byte[] bgra = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            bgra[(i * 4) + 0] = (byte)pattern;
            bgra[(i * 4) + 1] = (byte)(pattern >> 8);
            bgra[(i * 4) + 2] = (byte)(pattern >> 16);
            bgra[(i * 4) + 3] = (byte)(pattern >> 24);
        }

        return bgra;
    }

    private static byte[] Repeat(byte[] pixel, int pixels)
    {
        byte[] bgra = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            bgra[(i * 4) + 0] = pixel[0];
            bgra[(i * 4) + 1] = pixel[1];
            bgra[(i * 4) + 2] = pixel[2];
            bgra[(i * 4) + 3] = pixel[3];
        }

        return bgra;
    }
}
