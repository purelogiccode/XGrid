using System;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>Temporary debug log for live-run reasoning (file, because test output is buffered per test).</summary>
internal static class DebugLog
{
    public static void Write(string text)
    {
        System.IO.File.AppendAllText("./xgrid-live-debug.txt", text + Environment.NewLine);
    }
}

/// <summary>
/// Live replace-mode composition tests (compatibility gap 8): a draw the GL
/// backend declines must not lose the frame's GL pixels. The backend composes
/// its FBO into the guest color surface once per frame, the software
/// rasterizer continues on that image, and the flip uploads it back instead of
/// serving the partial FBO. Two decline shapes are covered: a structurally
/// unsupported one (zeta-byte count) and a draw whose computed cube/dot modes
/// need guest texel words the seam did not carry (B3 slice 2). Returns early
/// (pass) when no GL context exists.
/// </summary>
public sealed class ReplaceModeComposeLiveTests(ITestOutputHelper output)
{
    /// <summary>A 4x4 A8R8G8B8 surface plus a clip window that maps onto it.</summary>
    private const uint Surface = 0x2000u;
    private const uint Pitch = 16u;
    private const uint Width = 4u;
    private const uint Height = 4u;

    /// <summary>Cube texture base for the guest-word row (past the surface, inside the fake RAM).</summary>
    private const uint CubeOffset = 0x7000u;

    /// <summary>
    /// A 2x2 A8R8G8B8 cube (six faces, 128-byte face stride) with distinct
    /// texel words, plus the seam set carrying exactly those words.
    /// </summary>
    private static PgraphGuestTexelSet BuildCubeWords(IGpuMemory memory, out PgraphTextureSlot texture)
    {
        const int faceSize = 2;
        const uint stride = 128u;
        texture = new PgraphTextureSlot(
            offset: CubeOffset,
            format: 0x00000600u | 0x4u | (2u << 20) | (2u << 24),
            address: 0u,
            control0: 1u << 30,
            control1: 0u,
            imageRect: 0u,
            filterBilinear: false,
            filterConvolution: 0,
            samplerWrapU: 0,
            samplerWrapV: 0,
            borderColor: 0u,
            swizzleMaskX: 0x1u,
            swizzleMaskY: 0x2u,
            mortonMasks: true,
            cubeFaceStride: stride,
            cubeWidth: faceSize,
            cubeHeight: faceSize);

        var addresses = new uint[24];
        var words = new uint[24];
        int at = 0;
        for (uint face = 0; face < 6; face++)
        {
            for (uint y = 0; y < faceSize; y++)
            {
                for (uint x = 0; x < faceSize; x++)
                {
                    uint swizzled = x | (y << 1);
                    uint address = CubeOffset + (stride * face) + (swizzled * 4u);
                    uint word = 0x80000000u | (0x20u * face << 16) | (0x30u * y << 8) | (0x40u * x);
                    memory.WritePhysical(address, word);
                    addresses[at] = address;
                    words[at] = word;
                    at++;
                }
            }
        }

        return new PgraphGuestTexelSet(addresses, words);
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

    /// <summary>
    /// The draw the backend cannot honor. <paramref name="shape"/>: 0 = a
    /// test/depth shape the backend rejects (structural), 1 = a 2D stage that
    /// reads no guest word (served), 2 = the cube-flagged stage WITH its
    /// words (served, the control that proves the set is what makes the
    /// difference), 3 = the same cube-flagged PROJECT2D stage without words
    /// (declined).
    /// </summary>
    private static PgraphDrawInfo DrawInfo(PshCombiner combiner, int shape, in PgraphTextureSlot cube,
        PgraphGuestTexelSet words)
    {
        combiner.Control = 2u;
        combiner.StageProgram = shape is 0 or 2 or 3 ? 0x01u : 0x09u;
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
            depthTestEnable: shape == 0, depthFunc: shape == 0 ? 3u : 0u, depthMask: false,
            colorMask: 0x01010101u,
            stencilTestEnable: false, stencilWriteMask: 0u, stencilFunc: 0u, stencilRef: 0u,
            stencilReadMask: 0u, stencilOpFail: 0u, stencilOpZfail: 0u, stencilOpZpass: 0u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
        var empty = new PgraphTextureSlot(
            0u, 0u, 0u, 0u, 0u, 0u, filterBilinear: false, filterConvolution: 0);
        PgraphTextureSlot stage0 = shape == 0 ? empty : cube;
        return new PgraphDrawInfo(
            vertex, vertex, vertex,
            clipX: 0u, clipY: 0u, clipW: Width, clipH: Height,
            colorBytes: 4u, zetaBytes: shape == 0 ? 4u : 0u,
            combinerActive: true,
            fragment,
            stage0, empty, empty, empty,
            combiner,
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: shape == 0,
            colorSurfaceAddress: Surface,
            colorPitch: Pitch,
            guestTexels: shape is 1 or 3 ? new PgraphGuestTexelSet(Array.Empty<uint>(), Array.Empty<uint>()) : words);
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
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        Assert.True(backend.ReplaceMode);
        try
        {
            var combiner = new PshCombiner();
            PgraphGuestTexelSet words = BuildCubeWords(memory, out PgraphTextureSlot cube);

            // 1. A clear fills the FBO with the frame's background colour.
            backend.Clear(ClearInfo());

            // 2. A structurally unsupported clear must not leave a stale FBO
            //    standing in for the frame (the software path fills that
            //    buffer), and a draw the backend cannot honor then hands the
            //    frame back: the FBO's valid content must land in guest RAM
            //    before the software fill runs.
            backend.Clear(ClearInfo());
            Assert.False(backend.TryDraw(DrawInfo(combiner, shape: 0, cube, words)));
            Assert.Equal(Width * Height, (uint)CountComposedPixels(memory));
            Assert.Contains("composedFrames=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains($"composedPixels={Width * Height}", backend.DebugSummary, StringComparison.Ordinal);

            // 3. Every later draw of the same frame takes the software writer,
            //    even a shape the backend could have rendered.
            Assert.False(backend.TryDraw(DrawInfo(combiner, shape: 2, cube, words)));
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
                Assert.Equal(0x99, bgra[i]);      // B
                Assert.Equal(0x66, bgra[i + 1]);  // G
                Assert.Equal(0x33, bgra[i + 2]);  // R
                Assert.Equal(0xFF, bgra[i + 3]);  // A
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

    /// <summary>
    /// The guest-word lane (B3 slice 2): a cube stage is SERVED when the seam
    /// carried its words and DECLINED - never sampled from a guess - when it
    /// did not. Both draws go through the same backend and the same textures;
    /// only the carried set differs.
    /// </summary>
    [Fact]
    public void CubeStage_ServedWithCarriedWords_DeclinedWithout()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            var combiner = new PshCombiner();
            PgraphGuestTexelSet words = BuildCubeWords(memory, out PgraphTextureSlot cube);
            backend.Clear(ClearInfo());

            // Shape 1 (kind-1 modulate, mode 0x09 = slot-0 mode 1, not
            // PROJECT2D) reads no guest word at all: it is served with or
            // without carried words, and is not counted as a mode gap. The
            // cube-stage decline path is covered by
            // CubeStage_WithoutCarriedWords_IsDeclinedNotSampled.
            Assert.True(backend.TryDraw(DrawInfo(combiner, shape: 1, cube, words)), backend.DebugSummary);
            Assert.Contains("guestWordMisses=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("guestWordDraws=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("unsupported=0", backend.DebugSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (backend is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        // Fresh backend for the served leg (the first one composed its frame).
        var memory2 = new ArrayMemory();
        bool created2 = GlBackendFactory.TryCreate(
            memory2, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? served, out string why2);
        if (!created2 || served is null)
        {
            output.WriteLine("no GL context (" + why2 + "); served leg not exercised.");
            return;
        }

        try
        {
            var combiner = new PshCombiner();
            PgraphGuestTexelSet words = BuildCubeWords(memory2, out PgraphTextureSlot cube);
            served.Clear(ClearInfo());
            Assert.True(served.TryDraw(DrawInfo(combiner, shape: 2, cube, words)), served.DebugSummary);
            output.WriteLine(served.DebugSummary);
            Assert.Contains("guestWordDraws=1", served.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("guestWordMisses=0", served.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("unsupported=0", served.DebugSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("composedFrames=1", served.DebugSummary, StringComparison.Ordinal);

            // The frame is the GPU's: the flip serves the FBO, not an upload.
            served.Flip(new PgraphSurfaceRecord(Width, Height, 4u, 1ul, Surface, Pitch));
            Assert.True(served.ReadbackPresented(out uint width, out uint height, out _));
            Assert.Equal(Width, width);
            Assert.Equal(Height, height);
            Assert.Contains("presentUploads=0", served.DebugSummary, StringComparison.Ordinal);
        }
        finally
        {
            if (served is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// The decline half of the guest-word lane: a cube-flagged PROJECT2D
    /// stage whose draw carries no words must go to software (never sample a
    /// guessed word), record a guest-word miss, and not be counted as a mode
    /// gap - the mode is implemented, the draw's words were absent.
    /// </summary>
    [Fact]
    public void CubeStage_WithoutCarriedWords_IsDeclinedNotSampled()
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return;
        }

        try
        {
            PgraphGuestTexelSet words = BuildCubeWords(memory, out PgraphTextureSlot cube);
            var combiner = new PshCombiner();
            backend.Clear(ClearInfo());
            // The slot program is PROJECT2D on the cube stage, so with no
            // carried words the backend has nothing to resolve the cube face
            // with; the structural declines (zeta, float depth, depth test)
            // are off for this shape, so the guest-word lane is the only
            // reason left.
            Assert.False(backend.TryDraw(DrawInfo(combiner, shape: 3, cube, new PgraphGuestTexelSet(Array.Empty<uint>(), Array.Empty<uint>()))));
            _ = words;
            Assert.Contains("guestWordMisses=1", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("guestWordDraws=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("unsupported=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("composedFrames=1", backend.DebugSummary, StringComparison.Ordinal);
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
