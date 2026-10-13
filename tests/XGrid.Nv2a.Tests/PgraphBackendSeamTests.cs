using System;
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
            Draws++;
        }

        public void Clear(in PgraphClearInfo info)
        {
            _ = info;
            Clears++;
        }

        public void Flip(in PgraphSurfaceRecord record)
        {
            _ = record;
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
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    /// <summary>
    /// Writes the method stream at 0x8000 (clear rects in these tests fill
    /// physical 0..0x27FF, which would wipe a stream parked at 0) and kicks
    /// the pusher with GET at the same base.
    /// </summary>
    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory, params (uint Method, uint Parameter)[] methods)
    {
        const uint streamBase = 0x8000u;
        uint address = streamBase;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Base + 0x800044u, 4, streamBase);
        nv2A.Write(Base + 0x800040u, 4, address);
    }

    /// <summary>The ElementDraw_RunsVertexProgramAndRasterizes stream (existing golden).</summary>
    private static (uint Method, uint Parameter)[] ElementDrawStream() => new[]
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

    private static void WriteVertices(ArrayMemory memory)
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
            (0x012Cu, 0u));         // flip increment write

        Assert.Equal(1, backend.Clears);
        Assert.Equal(1, backend.Flips);
        Assert.True(backend.Methods >= 9);
        Assert.Equal(1uL, nv2A.ClearsExecuted);
        Assert.Equal(1uL, nv2A.FramesPresented);
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
}
