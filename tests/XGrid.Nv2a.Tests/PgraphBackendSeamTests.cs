using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// IPgraphBackend seam tests: with a backend attached the software path
/// executes exactly as today (same RAM result / counters as the null case)
/// and the backend observes Method/Draw/Clear/Flip at the flush sites.
/// </summary>
public sealed class PgraphBackendSeamTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

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

    private sealed class RecordingBackend : IPgraphBackend
    {
        public long BackendUnsupported { get; private set; }

        public string DebugSummary => $"recording methods={Methods} draws={Draws} clears={Clears} flips={Flips}";

        public long Methods { get; private set; }

        public long Draws { get; private set; }

        public long Clears { get; private set; }

        public long Flips { get; private set; }

        public long ResetCount { get; private set; }

        public uint LastClipW { get; private set; }

        public bool HasLastTexture { get; private set; }

        public PgraphTextureSlot LastTexture0 { get; private set; }

        public PgraphTextureSlot LastTexture2 { get; private set; }

        public uint LastFlipSurfaceAddress { get; private set; }

        public List<uint> FlipSurfaceAddresses { get; } = new();

        public void Method(uint subchannel, uint method, uint parameter)
        {
            _ = subchannel;
            _ = method;
            _ = parameter;
            Methods++;
        }

        public void Draw(in PgraphDrawInfo info)
        {
            LastClipW = info.ClipW;
            LastTexture0 = info.Texture0;
            LastTexture2 = info.Texture2;
            HasLastTexture = true;
            Draws++;
        }

        public void Clear(in PgraphClearInfo info)
        {
            _ = info;
            Clears++;
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
            LastFlipSurfaceAddress = record.SurfaceAddress;
            FlipSurfaceAddresses.Add(record.SurfaceAddress);
            Flips++;
        }

        public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = Array.Empty<byte>();
            return false;
        }

        public void ResetState()
        {
            Methods = 0;
            Draws = 0;
            Clears = 0;
            Flips = 0;
            BackendUnsupported = 0;
            ResetCount++;
        }
    }

    private sealed class ReplacingBackend : IPgraphBackend
    {
        public bool ReplaceMode => true;

        public long BackendUnsupported => 0;

        public string DebugSummary => $"replacing draws={Draws} mirrorDraws={MirrorDrawCalls}";

        public long Draws { get; private set; }

        public long MirrorDrawCalls { get; private set; }

        public void Method(uint subchannel, uint method, uint parameter)
        {
            _ = subchannel;
            _ = method;
            _ = parameter;
        }

        public bool TryDraw(in PgraphDrawInfo info)
        {
            _ = info;
            Draws++;
            return true;
        }

        public void Draw(in PgraphDrawInfo info)
        {
            // Replacement mode must never mirror: the seam contract routes
            // every draw through TryDraw.
            _ = info;
            MirrorDrawCalls++;
        }

        public void Clear(in PgraphClearInfo info)
        {
            _ = info;
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
            _ = record;
        }

        public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = Array.Empty<byte>();
            return false;
        }

        public void ResetState()
        {
        }
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        return (CreateChannelWith(memory), memory);
    }

    private static Nv2AMmio CreateChannelWith(IGpuMemory memory)
    {
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
        return nv2A;
    }

    /// <summary>
    /// Writes the method stream at 0x8000 (clear rects in these tests fill
    /// physical 0..0x27FF, which would wipe a stream parked at 0) and kicks
    /// the pusher with GET at the same base. The stream ends with a
    /// jump-to-PUT word so the stream itself executes exactly once (a jump
    /// elsewhere would walk whatever bytes lie past the stream and
    /// re-execute it until the word cap). Residual artifact, documented not
    /// fixed: the GET kick runs while PUT is still zero, so after the jump
    /// the pusher walks to the DMA limit and suspends with Protection (the
    /// PUT kick is then a no-op) — per-stream counts are exact, but
    /// PusherError is nonzero and must not be asserted here.
    /// </summary>
    private static void PushMethods(Nv2AMmio nv2A, IGpuMemory memory, params (uint Method, uint Parameter)[] methods)
    {
        const uint streamBase = 0x8000u;
        uint address = streamBase;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, (address + 4u) | 1u);
        nv2A.Write(Base + 0x800044u, 4, streamBase);
        nv2A.Write(Base + 0x800040u, 4, address + 4u);
    }

    /// <summary>The ElementDraw_RunsVertexProgramAndRasterizes stream (existing golden).</summary>
    private static (uint Method, uint Parameter)[] ElementDrawStream()
    {
        return new[]
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
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
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
    }

    private static void WriteVertices(IGpuMemory memory)
    {
        memory.WritePhysical(0x2000u, 0x40000000u);
        memory.WritePhysical(0x2004u, 0x40000000u);
        memory.WritePhysical(0x2008u, 0x00000000u);
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41400000u);
        memory.WritePhysical(0x2014u, 0x40000000u);
        memory.WritePhysical(0x2018u, 0x00000000u);
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x40000000u);
        memory.WritePhysical(0x2024u, 0x41400000u);
        memory.WritePhysical(0x2028u, 0x00000000u);
        memory.WritePhysical(0x202Cu, 0x3F800000u);
    }

    private sealed class FastArrayMemory : IGpuMemory, IGpuMemoryFast
    {
        private readonly byte[] _data = new byte[0x10000];
        private bool _batch;

        public long BeginBatches { get; private set; }

        public long EndBatches { get; private set; }

        public long FastWrites { get; private set; }

        public long FastReads { get; private set; }

        public uint ReadPhysical(uint address) => Read32(address);

        public void WritePhysical(uint address, uint value) => Write32(address, value);

        public void BeginGpuBatch()
        {
            BeginBatches++;
            _batch = true;
        }

        public void EndGpuBatch()
        {
            EndBatches++;
            _batch = false;
        }

        public bool TryReadPhysical(uint address, out uint value)
        {
            if (address <= _data.Length - 4)
            {
                value = Read32(address);
                FastReads++;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWritePhysical(uint address, uint value)
        {
            if (!_batch || address > _data.Length - 4)
            {
                return false;
            }

            Write32(address, value);
            FastWrites++;
            return true;
        }

        public bool TryGetRamBytes(out byte[] bytes, out uint size)
        {
            bytes = _data;
            size = (uint)_data.Length;
            return true;
        }

        private uint Read32(uint address) =>
            (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) | (_data[address + 3] << 24));

        private void Write32(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }
    }

    [Fact]
    public void GpuMemoryFastLane_MatchesLegacyRasterizer_AndIsUsed()
    {
        (Nv2AMmio legacy, ArrayMemory legacyMemory) = CreateChannel();
        WriteVertices(legacyMemory);
        PushMethods(legacy, legacyMemory, ElementDrawStream());

        var fastMemory = new FastArrayMemory();
        Nv2AMmio fast = CreateChannelWith(fastMemory);
        WriteVertices(fastMemory);
        PushMethods(fast, fastMemory, ElementDrawStream());

        Assert.Equal(legacy.DrawsExecuted, fast.DrawsExecuted);
        Assert.Equal(legacy.DrawnPixels, fast.DrawnPixels);
        for (uint address = 0x3000u; address < 0x3400u; address += 4u)
        {
            Assert.Equal(legacyMemory.ReadPhysical(address), fastMemory.ReadPhysical(address));
        }

        Assert.True(fastMemory.BeginBatches > 0, "no GPU write batch opened");
        Assert.Equal(fastMemory.BeginBatches, fastMemory.EndBatches);
        Assert.True(fastMemory.FastWrites > 0, "fast write lane not used");
        Assert.True(fastMemory.FastReads > 0, "fast read lane not used");
    }

    [Fact]
    public void BackendNull_ElementDrawSoftwareUnchanged()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        Assert.Null(nv2A.Backend);
        WriteVertices(memory);
        PushMethods(nv2A, memory, ElementDrawStream());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFF8040u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    [Fact]
    public void RasterTiming_DefaultOffThenAccountsOneFill()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        Assert.False(nv2A.RasterTimingEnabled);
        Assert.Equal(0L, nv2A.RasterLegacyCalls + nv2A.RasterPshCalls);
        Assert.Equal(0.0, nv2A.RasterLegacySeconds + nv2A.RasterPshSeconds);

        WriteVertices(memory);
        nv2A.RasterTimingEnabled = true;
        PushMethods(nv2A, memory, ElementDrawStream());

        Assert.Equal(1L, nv2A.RasterLegacyCalls + nv2A.RasterPshCalls);
        Assert.True(nv2A.RasterLegacySeconds + nv2A.RasterPshSeconds > 0.0);
    }

    [Fact]
    public void BackendAttached_SoftwarePathIdenticalAndBackendObserves()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        PushMethods(nv2A, memory, ElementDrawStream());

        // Software path byte-identical to the null-backend case.
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.True(nv2A.DrawnPixels > 0);
        Assert.Equal(0xFFFF8040u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));

        // Backend observed the dispatch.
        Assert.True(backend.Methods > 0);
        Assert.Equal(1, backend.Draws);
        Assert.Equal(0x00100000u >> 16, backend.LastClipW);
        Assert.Equal(0, backend.Clears);
        Assert.Equal(0, backend.Flips);
        Assert.Equal(0, backend.BackendUnsupported);
    }

    [Fact]
    public void ReplaceMode_SkipsSoftwareRasterizerAndNeverMirrors()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new ReplacingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        PushMethods(nv2A, memory, ElementDrawStream());

        // The backend drew it; the software rasterizer was skipped entirely.
        Assert.Equal(1, backend.Draws);
        Assert.Equal(0, backend.MirrorDrawCalls);
        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(0uL, nv2A.DrawnPixels);
        Assert.Equal(0u, memory.ReadPhysical(0x3000u + (5u * 0x40u) + (5u * 4u)));
    }

    /// <summary>
    /// Records the draw-command batch bracket so the seam's ordering contract
    /// can be asserted: every draw must arrive between one BeginDrawBatch and
    /// its matching EndDrawBatch, batches must never nest, and no clear/flip
    /// may arrive inside one (they change FBO content, so the backend must have
    /// been flushed first).
    /// </summary>
    private sealed class BatchRecordingBackend : IPgraphBackend
    {
        public long BackendUnsupported => 0;

        public string DebugSummary => $"batches={Begins} draws={Draws}";

        public int Begins { get; private set; }

        public int Ends { get; private set; }

        public int Draws { get; private set; }

        public int Clears { get; private set; }

        public int Flips { get; private set; }

        public int OrphanEnds { get; private set; }

        public int NestedBegins { get; private set; }

        public int DrawsOutsideBatch { get; private set; }

        public int ClearInsideBatch { get; private set; }

        public int FlipInsideBatch { get; private set; }

        public List<int> RunLengths { get; } = new();

        public int MaxRun { get; private set; }

        private int _depth;
        private int _run;

        public void Method(uint subchannel, uint method, uint parameter)
        {
            _ = subchannel;
            _ = method;
            _ = parameter;
        }

        public void BeginDrawBatch()
        {
            Begins++;
            _depth++;
            if (_depth > 1)
            {
                NestedBegins++;
            }
        }

        public void EndDrawBatch()
        {
            Ends++;
            if (_depth == 0)
            {
                OrphanEnds++;
                return;
            }

            _depth--;
            if (_depth == 0)
            {
                RunLengths.Add(_run);
                MaxRun = Math.Max(MaxRun, _run);
                _run = 0;
            }
        }

        public void Draw(in PgraphDrawInfo info)
        {
            _ = info;
            Draws++;
            if (_depth == 0)
            {
                DrawsOutsideBatch++;
            }

            _run++;
        }

        public void Clear(in PgraphClearInfo info)
        {
            _ = info;
            Clears++;
            if (_depth != 0)
            {
                ClearInsideBatch++;
            }
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
            _ = record;
            Flips++;
            if (_depth != 0)
            {
                FlipInsideBatch++;
            }
        }

        public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
        {
            width = 0;
            height = 0;
            bgra = Array.Empty<byte>();
            return false;
        }

        public void ResetState()
        {
        }
    }

    /// <summary>An element-mode stream whose single draw command carries two triangles.</summary>
    private static (uint Method, uint Parameter)[] TwoTriangleElementDrawStream()
    {
        var stream = new List<(uint Method, uint Parameter)>();
        foreach ((uint method, uint parameter) in ElementDrawStream())
        {
            if (method == 0x17FCu && parameter == 0u)
            {
                // A second element inside the same SET_BEGIN_END block: one
                // primitive flush, two triangles.
                stream.Add((0x1800u, 0x00010000u));
                stream.Add((0x1808u, 0x00000002u));
            }

            stream.Add((method, parameter));
        }

        return stream.ToArray();
    }

    [Fact]
    public void DrawBatch_BracketsOneDrawCommandWithAllItsTriangles()
    {
        // Track B batching contract (205): one PGRAPH primitive flush is one
        // batch, every triangle of it is submitted inside the bracket, and the
        // bracket never nests. The GL backend collapses exactly this run into
        // one instanced draw, so a broken bracket silently groups state across
        // draw commands.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new BatchRecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        PushMethods(nv2A, memory, TwoTriangleElementDrawStream());

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(2, backend.Draws);
        Assert.Equal(1, backend.Begins);
        Assert.Equal(1, backend.Ends);
        Assert.Equal(0, backend.NestedBegins);
        Assert.Equal(0, backend.OrphanEnds);
        Assert.Equal(0, backend.DrawsOutsideBatch);
        Assert.Equal(2, backend.MaxRun);
        Assert.Single(backend.RunLengths);
        Assert.Equal(2, backend.RunLengths[0]);
    }

    [Fact]
    public void DrawBatch_TwoDrawCommandsAreTwoBatches()
    {
        // Two SET_BEGIN_END blocks are two commands and must not merge: the
        // guest can change any PGRAPH state between them.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new BatchRecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        (uint Method, uint Parameter)[] first = ElementDrawStream();
        var twice = new List<(uint Method, uint Parameter)>(first.Length * 2);
        twice.AddRange(first);
        twice.AddRange(first);
        PushMethods(nv2A, memory, twice.ToArray());

        Assert.Equal(2uL, nv2A.DrawsExecuted);
        Assert.Equal(2, backend.Draws);
        Assert.Equal(2, backend.Begins);
        Assert.Equal(2, backend.Ends);
        Assert.Equal(2, backend.RunLengths.Count);
        Assert.Equal(1, backend.RunLengths[0]);
        Assert.Equal(1, backend.RunLengths[1]);
        Assert.Equal(0, backend.DrawsOutsideBatch);
    }

    [Fact]
    public void DrawBatch_SkippedCommandStillClosesItsBatch()
    {
        // The flush early-returns for an unsupported primitive mode; the
        // bracket must still close (the seam uses try/finally), or the next
        // command's triangles would be grouped with stale state.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new BatchRecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x17FCu, 1u), // points: not a triangle mode
            (0x1800u, 0x00010000u),
            (0x1808u, 0u),
            (0x17FCu, 0u));

        Assert.Equal(0, backend.Draws);
        Assert.Equal(1, backend.Begins);
        Assert.Equal(1, backend.Ends);
        Assert.Equal(0, backend.OrphanEnds);
        Assert.True(nv2A.SkippedDraws > 0);
    }

    [Fact]
    public void DrawBatch_ClearAndFlipArriveOutsideBatches()
    {
        // A clear or a flip changes/reports FBO content; the backend flushes
        // its pending batch on them, so the seam must never deliver one inside
        // an open bracket.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new BatchRecordingBackend();
        nv2A.Backend = backend;
        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000100u),
            (0x0200u, 0x00400000u),
            (0x0204u, 0x00280000u),
            (0x1D90u, 0xFF112233u),
            (0x1D98u, 0x03FF0000u),
            (0x1D9Cu, 0x03FF0000u),
            (0x1D94u, 0x000000F0u),
            (0x012Cu, 0u));

        Assert.Equal(1, backend.Clears);
        Assert.Equal(1, backend.Flips);
        Assert.Equal(0, backend.ClearInsideBatch);
        Assert.Equal(0, backend.FlipInsideBatch);
        Assert.Equal(0, backend.DrawsOutsideBatch);
    }

    [Fact]
    public void BackendAttached_ObservesFlipAndClear()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        PushMethods(
            nv2A,
            memory,
            (0x0208u, 0x00000104u), // surface format: pitch type, color 4-byte
            (0x020Cu, 0x00000100u), // pitch 0x100
            (0x0200u, 0x00400000u), // clip H width=64
            (0x0204u, 0x00280000u), // clip V height=40
            (0x1D90u, 0xFF112233u), // color clear value
            (0x1D98u, 0x03FF0000u), // clear rect H 0..1023
            (0x1D9Cu, 0x03FF0000u), // clear rect V 0..1023
            (0x1D94u, 0x000000F0u), // clear color
            (0x012Cu, 0u)); // flip increment write

        Assert.Equal(1, backend.Clears);
        Assert.Equal(1, backend.Flips);
        Assert.True(backend.Methods >= 9);
        Assert.Equal(1uL, nv2A.ClearsExecuted);
        Assert.Equal(1uL, nv2A.FramesPresented);
    }

    [Fact]
    public void Flip_CarriesResolvedSurfaceAddress()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;

        // RAMHT at RAMIN 0x10000; color context handle 9 -> instance 0x11150
        // with base 0x8000 (same recipe as the pusher DMA-context test).
        nv2A.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2A.Write(Base + 0x711150u, 4, 0x0000003Du);
        nv2A.Write(Base + 0x711154u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x711158u, 4, 0x00008003u);
        nv2A.Write(Base + 0x710000u + 9u * 8u, 4, 0x00000009u);
        nv2A.Write(Base + 0x710000u + 9u * 8u + 4u, 4, 0x80001115u);

        PushMethods(
            nv2A,
            memory,
            (0x0194u, 0x00000009u), // color context -> DMA base 0x8000
            (0x0210u, 0x00001000u), // color offset
            (0x012Cu, 0u)); // flip increment write

        Assert.Equal(1, backend.Flips);
        Assert.Equal(0x9000u, backend.LastFlipSurfaceAddress);
        Assert.Equal(1uL, nv2A.FramesPresented);
        Assert.False(nv2A.PusherCapped);
    }

    [Fact]
    public void Flip_SurfaceAddressTracksOffsetAcrossFlips()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;

        // No DMA context (base resolves to 0): the record carries the live
        // color offset at each flip.
        PushMethods(
            nv2A,
            memory,
            (0x0210u, 0x00003000u),
            (0x012Cu, 0u),
            (0x0210u, 0x00003040u),
            (0x012Cu, 0u));

        Assert.Equal(2, backend.Flips);
        Assert.Equal(2, backend.FlipSurfaceAddresses.Count);
        Assert.Equal(0x3000u, backend.FlipSurfaceAddresses[0]);
        Assert.Equal(0x3040u, backend.FlipSurfaceAddresses[1]);
        Assert.Equal(2uL, nv2A.FramesPresented);
        Assert.False(nv2A.PusherCapped);
    }

    [Fact]
    public void BackendResetState_CalledOnMachineResetPath()
    {
        (Nv2AMmio nv2A, _) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        nv2A.Reset();
        Assert.Equal(1, backend.ResetCount);
        Assert.Same(backend, nv2A.Backend);
        Assert.Equal(0, backend.Methods);
    }

    /// <summary>Texture methods (SET_TEXTURE_ADDRESS/FILTER/BORDER_COLOR) followed by one draw.</summary>
    private static (uint Method, uint Parameter)[] TexturedDrawStream()
    {
        var stream = new List<(uint Method, uint Parameter)>();
        foreach ((uint method, uint parameter) in ElementDrawStream())
        {
            if (method == 0x17FCu && parameter == 5u)
            {
                stream.Add((0x1B08u, 0x00030404u)); // slot 0: U border, V border
                stream.Add((0x1B14u, 0x02022000u)); // slot 0: MIN 2 MAG 2, kernel 1
                stream.Add((0x1B24u, 0xFF000000u)); // slot 0: border colour
                stream.Add((0x1B48u, 0x00020202u)); // slot 1: U mirror, V mirror
                stream.Add((0x1B88u, 0x00020202u)); // slot 2: U mirror, V mirror
                stream.Add((0x1B94u, 0x07074000u)); // slot 2: MIN 7 MAG 7, gaussian-3
            }

            stream.Add((method, parameter));
        }

        return stream.ToArray();
    }

    [Fact]
    public void BackendSeesDecodedSamplerWrapModes()
    {
        // The seam carries the *decoded* address mode (0 wrap, 1 mirror,
        // 2 clamp, 3 border): the raw word assigns 1 to wrap and 2 to mirror,
        // and the GL backend read it directly (swapping them and clamping
        // border axes) before this contract existed.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        var backend = new RecordingBackend();
        nv2A.Backend = backend;
        WriteVertices(memory);
        PushMethods(nv2A, memory, TexturedDrawStream());

        Assert.True(backend.HasLastTexture);
        Assert.Equal(3, backend.LastTexture0.SamplerWrapU);
        Assert.Equal(3, backend.LastTexture0.SamplerWrapV);
        Assert.Equal(0xFF000000u, backend.LastTexture0.BorderColor);
        Assert.Equal(1, backend.LastTexture2.SamplerWrapU);
        Assert.Equal(1, backend.LastTexture2.SamplerWrapV);
    }

    [Fact]
    public void BackendSeesResolvedTexFilterOnlyWhenRegistersAreEnabled()
    {
        // TEXFILTER is opt-in: with the flag off every stage is nearest and
        // no backend can filter, so the default path is unchanged by
        // construction. With it on the seam carries the same resolution the
        // software SamplerPlan uses (tent for MIN/MAG 2 or 4/6/7; the kernel
        // only counts when MIN selects the convolution).
        (Nv2AMmio off, ArrayMemory offMemory) = CreateChannel();
        var offBackend = new RecordingBackend();
        off.Backend = offBackend;
        WriteVertices(offMemory);
        PushMethods(off, offMemory, TexturedDrawStream());
        Assert.False(offBackend.LastTexture0.Bilinear);
        Assert.Equal(0, offBackend.LastTexture0.Convolution);
        Assert.False(offBackend.LastTexture2.Bilinear);
        Assert.Equal(0, offBackend.LastTexture2.Convolution);

        (Nv2AMmio on, ArrayMemory onMemory) = CreateChannel();
        on.TextureFilterRegisters = true;
        var onBackend = new RecordingBackend();
        on.Backend = onBackend;
        WriteVertices(onMemory);
        PushMethods(on, onMemory, TexturedDrawStream());
        Assert.True(onBackend.LastTexture0.Bilinear);
        Assert.Equal(0, onBackend.LastTexture0.Convolution); // kernel next to a tent MIN is ignored
        Assert.True(onBackend.LastTexture2.Bilinear);
        Assert.Equal(2, onBackend.LastTexture2.Convolution);
    }
}
