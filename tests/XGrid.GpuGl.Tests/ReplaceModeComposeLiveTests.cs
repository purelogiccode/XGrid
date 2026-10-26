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
    private static PgraphGuestTexelSet BuildCubeWords(IGpuMemory memory, out PgraphTextureSlot texture,
        uint color = 0x06u)
    {
        const int faceSize = 2;
        const uint stride = 128u;
        texture = new PgraphTextureSlot(
            offset: CubeOffset,
            format: (color << 8) | 0x4u | (2u << 20) | (2u << 24),
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
        uint bytes = Nv2AMmio.TextureFormatBytesPerTexel(color);
        for (uint face = 0; face < 6; face++)
        {
            for (uint y = 0; y < faceSize; y++)
            {
                for (uint x = 0; x < faceSize; x++)
                {
                    uint swizzled = x | (y << 1);
                    uint address = CubeOffset + (stride * face) + (swizzled * bytes);
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
    /// The draw under test. <paramref name="shape"/>: 0 = a depth/zeta shape
    /// the backend rejects (structural), 2 = the corpus row's panel program - a
    /// single CUBEMAP stage whose texel is the whole output (A = T0, B = 1,
    /// MUX -&gt; R0, final D = R0), laid over the whole clip window with one
    /// constant cube direction per vertex so every fragment samples one face
    /// texel.
    /// </summary>
    private static PgraphDrawInfo DrawInfo(PshCombiner combiner, int shape, in PgraphTextureSlot cube,
        PgraphGuestTexelSet words)
    {
        bool cubeRow = shape == 2;
        bool panelRow = shape is 2 or 3;
        combiner.Control = panelRow ? 1u : 2u;
        combiner.StageProgram = shape switch { 2 => 0x03u, 3 => 0x02u, _ => 0x09u };
        combiner.Final0 = panelRow ? 0x0000000Cu : 0x00000000u;
        combiner.Final1 = panelRow ? 0x00001C80u : 0x00000001u;
        if (panelRow)
        {
            combiner.RgbInputs[0] = 0x08200000u;
            combiner.AlphaInputs[0] = 0x18200000u;
            combiner.RgbOutputs[0] = 0x00000C00u;
            combiner.AlphaOutputs[0] = 0x00000C00u;
        }

        PgraphVertex origin = Vertex(0f, 0f, cubeRow);
        PgraphVertex across = Vertex(Width, 0f, cubeRow);
        PgraphVertex down = Vertex(0f, Height, cubeRow);
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
            origin, across, down,
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
            guestTexels: words);
    }

    /// <summary>One vertex of the drawn triangle; the cube direction is +Z so the face and texel are fixed.</summary>
    /// <param name="x">Screen-space x.</param>
    /// <param name="y">Screen-space y.</param>
    /// <param name="cubeDirection">Whether stage 0's coords carry the +Z direction.</param>
    private static PgraphVertex Vertex(float x, float y, bool cubeDirection)
    {
        float u = cubeDirection ? 0f : 0.5f;
        float v = cubeDirection ? 0f : 0.5f;
        return new PgraphVertex(
            x, y, 0.5f, 1f,
            1f, 1f, 1f, 1f,
            0f, 0f, 0f, 0f,
            u, v, 1f,
            u, v, 1f,
            u, v, 1f,
            u, v, 1f,
            1f, 1f, 1f, 1f,
            1f);
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
    /// A PROJECT3D stage over a volume texture is declined, not silently
    /// painted black: the software sampler reads a swizzled volume slice
    /// (`Nv2AMmio.SampleInto3D`) and the GL shader has no volume source. Losing
    /// the draw to software keeps the frame complete - the same contract as a
    /// structurally unsupported zeta shape.
    /// </summary>
    [Fact]
    public void VolumeTextureUnderProject3D_IsDeclinedNotPaintedBlack()
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
            _ = BuildCubeWords(memory, out _);

            // A 3D (volume) TEXFMT: dimensionality is bits 6-7 of the format
            // word, which is what the shader's uTexDim carries.
            var volume = new PgraphTextureSlot(
                offset: CubeOffset,
                format: 0x00000600u | (3u << 6) | (2u << 20) | (2u << 24),
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
                cubeFaceStride: 128u,
                cubeWidth: 2,
                cubeHeight: 2);

            backend.Clear(ClearInfo());
            Assert.False(backend.TryDraw(DrawInfo(combiner, shape: 3, volume, words: PgraphGuestTexelSet.Empty)));
            output.WriteLine(backend.DebugSummary);
            Assert.Contains("cubeVol=1", backend.DebugSummary, StringComparison.Ordinal);
            // Replacement mode hands the frame to software, never a black panel.
            Assert.Equal(Width * Height, (uint)CountComposedPixels(memory));
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
    /// A cube stage is served from the decoder's own view of the guest faces:
    /// the backend decodes all six swizzled faces into a host 2D-array texture
    /// (<c>Nv2AMmio.DecodeSwizzledTexelWord</c> per texel) and the fragment
    /// stage taps it with the face select. The panel is therefore painted with
    /// a decoded texel - not the clear colour and not black, which is what the
    /// word-pool path produced once a face outgrew its window (compatibility
    /// gap 12, `xgu bump_cubemap_reflect`).
    /// </summary>
    /// <param name="color">Texture color-format id: every format the cube path backs.</param>
    [Theory]
    [InlineData(0x05u)]
    [InlineData(0x06u)]
    [InlineData(0x3Au)]
    [InlineData(0x3Bu)]
    [InlineData(0x3Cu)]
    public void CubeStage_SamplesTheDecodedFaces_NotAWordWindow(uint color)
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
            _ = BuildCubeWords(memory, out PgraphTextureSlot cube, color);
            backend.Clear(ClearInfo());

            // An empty carried set is the shape the real guest produces: the
            // control-flow capture saw no words for this draw command. The cube
            // decode reads guest RAM, so the draw is still served.
            bool served = backend.TryDraw(DrawInfo(combiner, shape: 2, cube, PgraphGuestTexelSet.Empty));
            output.WriteLine(backend.DebugSummary);
            Assert.True(served, backend.DebugSummary);
            Assert.Contains("guestWordDraws=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("guestWordMisses=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.Contains("unsupported=0", backend.DebugSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("composedFrames=1", backend.DebugSummary, StringComparison.Ordinal);

            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4u, 1ul, Surface, Pitch));
            Assert.True(backend.ReadbackPresented(out uint width, out uint height, out byte[] bgra));
            Assert.Equal(Width, width);
            Assert.Equal(Height, height);

            // The triangle covers half the 4x4 surface, and the constant +Z
            // direction samples one face texel in every covered fragment. The
            // covered pixels must be that texel's colour decoded by the software
            // sampler's own decoder (face 4, texel 1,1 of the 2x2 face) - the
            // per-format check of the "one decoder, two executors" rule, in the
            // byte order the framebuffer readback uses (B,G,R,A).
            uint bytes = Nv2AMmio.TextureFormatBytesPerTexel(color);
            uint texelIndex = Nv2AMmio.SwizzleAddressNT(
                1, 1, cube.SwizzleMaskX, cube.SwizzleMaskY, cube.MortonMasks);
            uint word = memory.ReadPhysical(CubeOffset + (cube.CubeFaceStride * 4u) + (texelIndex * bytes));
            Assert.True(Nv2AMmio.DecodeSwizzledTexelWord(
                color, bytes, word, out float er, out float eg, out float eb, out float ea));
            byte expectedB = (byte)((eb * 255f) + 0.5f);
            byte expectedG = (byte)((eg * 255f) + 0.5f);
            byte expectedR = (byte)((er * 255f) + 0.5f);
            byte expectedA = (byte)((ea * 255f) + 0.5f);

            int covered = 0;
            for (int i = 0; i < bgra.Length; i += 4)
            {
                if (bgra[i] == 0x99 && bgra[i + 1] == 0x66 && bgra[i + 2] == 0x33)
                {
                    continue; // the clear colour of an uncovered pixel
                }

                Assert.Equal(expectedB, bgra[i + 0]);
                Assert.Equal(expectedG, bgra[i + 1]);
                Assert.Equal(expectedR, bgra[i + 2]);
                Assert.Equal(expectedA, bgra[i + 3]);
                covered++;
            }

            Assert.True(covered > 0, "the cube stage painted no pixel");
            Assert.False((expectedR | expectedG | expectedB) == 0, "the decoded texel is black");
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
    /// A cube stage whose face texels the seam carried decodes to exactly the
    /// same pixels as one that carries none: the guest word is read from RAM
    /// either way, and the carried set is a window over what the software
    /// sampler touched - never the authority. Before this the carried set
    /// decided whether a draw was declined at all.
    /// </summary>
    [Fact]
    public void CubeStage_CarriedWordsAndDirectRamPaintTheSamePixels()
    {
        byte[]? withWords = RenderCubeFrame(carriedWords: true);
        byte[]? withoutWords = RenderCubeFrame(carriedWords: false);
        if (withWords is null || withoutWords is null)
        {
            output.WriteLine("no GL context; live row not exercised.");
            return;
        }

        Assert.Equal(withWords, withoutWords);
    }

    /// <summary>Renders the cube-stage frame with or without the seam's carried word set.</summary>
    /// <param name="carriedWords">Whether the draw reports the words it sampled.</param>
    /// <returns>The presented BGRA pixels, or null when no GL context exists.</returns>
    private byte[]? RenderCubeFrame(bool carriedWords)
    {
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is null)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            return null;
        }

        try
        {
            var combiner = new PshCombiner();
            PgraphGuestTexelSet words = BuildCubeWords(memory, out PgraphTextureSlot cube);
            backend.Clear(ClearInfo());
            PgraphGuestTexelSet carried = carriedWords ? words : PgraphGuestTexelSet.Empty;
            Assert.True(backend.TryDraw(DrawInfo(combiner, shape: 2, cube, carried)), backend.DebugSummary);
            backend.Flip(new PgraphSurfaceRecord(Width, Height, 4u, 1ul, Surface, Pitch));
            Assert.True(backend.ReadbackPresented(out _, out _, out byte[] bgra));
            return bgra;
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
