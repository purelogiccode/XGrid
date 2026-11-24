using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using XGrid.Memory;
using XGrid.Nv2a;

namespace XGrid.GpuGl;

/// <summary>
/// Slice-1 OpenGL PGRAPH backend (design doc §3/§8 step 4): observation-only
/// consumer of the <see cref="IPgraphBackend"/> seam. The software path
/// still executes unconditionally; this backend renders the same resolved
/// state into an offscreen FBO on the emulation thread (synchronous, no
/// async) and serves the presented frame via readback at flip. Vertex path
/// stays on CPU <c>VshProgram</c> (screen-space buffer at FlushDraw); GLSL
/// fragment generation from <see cref="PshGlslGenerator"/>; blend/depth/
/// stencil/mask via <see cref="GlStateMapper"/>; probe-gated init in
/// <see cref="GlBackendFactory"/>.
/// </summary>
public sealed class GlPgraphBackend : IPgraphBackend, IDisposable
{
    private static readonly string[] UniformNames =
    [
        "uConsts[0]",
        "uFogColor",
        "uFogEnable",
        "uAlphaTestEnable",
        "uAlphaFunc",
        "uAlphaRef",
        "uZclipActive",
        "uZclipCull",
        "uZclipMin",
        "uZclipMax",
        "uZetaRange",
        "uZetaBytes",
        "uTexOk[0]",
        "uTexSize[0]",
        "uTexLinear[0]",
        "uTexWrapX[0]",
        "uTexWrapY[0]",
        "uTexCube[0]",
        "uTexDim[0]",
        "uTexBilinear[0]",
        "uTexConvolution[0]",
        "uTexBorder[0]",
        "uTexCubeOk[0]",
        "uTexCubeW[0]",
        "uTexCubeH[0]",
        "uDotMap[0]",
        "uGuestWord[0]",
        "uTexPoolBase[0]",
        "uEyePos",
        "uBumpMatrix[0]",
        "uBumpLumeScale[0]",
        "uBumpLumeOffset[0]",
        "uShaderClip",
        "uTri[0]",
        "uTex0",
        "uTex1",
        "uTex2",
        "uTex3",
        "uCube0",
        "uCube1",
        "uCube2",
        "uCube3",
    ];

    private readonly WglContext _context;
    private readonly INativeContext _glContext;
    private readonly GL _gl;
    private readonly IGpuMemory _memory;

    // Present-upload staleness watch (§132 (a*)): non-null when the
    // injected IGpuMemory is one of the machine adapters that expose the
    // PhysicalMemory dirty bit (the real CLI wiring); null for seam fakes,
    // which keep the pre-§135 serve-whenever-FBO-non-empty contract.
    private readonly IPresentDirtyWatch? _dirtyWatch;

    /// <summary>
    /// Guest write stamp channel (XFP4 texture identity): the backend hashes a
    /// texture's whole guest range only when the range's page-generation sum
    /// moved since the last hash. Null on a bare seam fake, where every bind
    /// hashes (still exact, just without the dirty short-circuit).
    /// </summary>
    private readonly IGuestWriteStamp? _writeStamp;

    private readonly uint _vertexShader;
    private readonly uint _vao;
    private readonly Dictionary<string, GlProgram> _programs = new(StringComparer.Ordinal);
    private readonly Dictionary<TextureKey, GlTextureEntry> _textures = [];

    // Packed per-draw uniform block (uTri[25], see PshGlslGenerator) so a draw
    // is ONE uniform upload instead of thirteen. Layout constants are the
    // shader contract; the fragment shader reads the same slots.
    private const int TriBlockFloats = 25 * 4; // one triangle's packed uniform block
    private readonly float[] _uTri = new float[TriBlockFloats];
    private const int TriVaryingBase = 12; // floats 0..11 are the barycentric/scalar preamble
    private const int TriVaryingStride = 12; // floats per per-vertex channel (3 vertices x vec4)
    private const int TriClipSizeBase = 24 * 4; // uTri[24] = (clipW, clipH, 0, 0) for the vertex shader

    // Draw-command batching: one PGRAPH primitive flush (BeginDrawBatch ..
    // EndDrawBatch) shares program, texture, GL state and uniforms, so its
    // triangles are staged in _batchData and submitted with one instanced
    // draw. Capacity is the number of blocks that fit the uniform budget.
    private readonly int _batchCapacity;
    private readonly float[] _batchData;
    private int _batchCount;
    private bool _batchOpen;

    // XFP5 swizzled render target: the batch's target record, used to download
    // the drawn clip region to guest RAM in Morton order after submission.
    private bool _rtSwizzlePending;
    private uint _rtAddress;
    private uint _rtColorBytes;
    private int _rtSwizzleWidth;
    private int _rtSwizzleHeight;
    private uint _rtClipX;
    private uint _rtClipY;
    private uint _rtClipW;
    private uint _rtClipH;
    private uint _rtDownloads;
    private uint _rtSwizzleSeen;
    private bool _batchStateReady;
    private GlProgram? _batchProgram;

    // Shader-structure cache: draws in a run share the combiner structure, so
    // the key string is only built when the 37 structure words change (the
    // per-draw BuildKey allocation was a measured submission cost).
    private readonly uint[] _lastStructure = new uint[ProgramStructureWords];
    private GlProgram? _lastProgram;
    private bool _lastLegacy;
    private bool _lastStructureValid;

    // Per-slot texture lookup cache (key struct, no per-draw string alloc).
    private readonly TextureKey[] _lastTextureKey = new TextureKey[PshCombiner.TextureSlots];
    private readonly GlTextureEntry?[] _lastTextureEntry = new GlTextureEntry?[PshCombiner.TextureSlots];
    private readonly bool[] _lastTextureValid = new bool[PshCombiner.TextureSlots];
    private readonly uint[] _boundTexture = new uint[PshCombiner.TextureSlots];
    private readonly uint[] _boundCubeTexture = new uint[PshCombiner.TextureSlots];
    private int _activeTextureUnit = -1;

    // Direct RAM word reads for the texture hash and the HILO guest-word pool
    // fall back to the adapter for non-RAM/out-of-range addresses.
    private readonly byte[]? _ramBytes;
    private readonly uint _ramSize;
    private readonly float[] _constFlat = new float[18 * 4];

    // GL binding shadows: redundant per-draw binds are driver-side validation
    // on a 2.76M-draw stream.
    private uint _boundFbo;
    private uint _currentProgram;
    private uint _viewportW;
    private uint _viewportH;
    private readonly int[] _texOk = new int[4];
    private readonly float[] _texSize = new float[4 * 2];
    private readonly int[] _texLinear = new int[4];
    private readonly int[] _texWrapX = new int[4];
    private readonly int[] _texWrapY = new int[4];
    private readonly int[] _texCube = new int[4];
    private readonly int[] _texDim = new int[4];
    private readonly int[] _texBilinear = new int[4];
    private readonly int[] _texConvolution = new int[4];
    private readonly float[] _texBorder = new float[4 * 4];
    // Cube maps and the dot texture modes (B3 slice 1): whether the stage
    // resolves to a face texture, its extent, and the decoded dot maps - all
    // uniforms so the program cache key never splits on them. The faces
    // themselves are a host 2D-array texture (see UploadCubeTexture).
    private readonly int[] _texCubeOk = new int[4];
    private readonly int[] _texCubeW = new int[4];
    private readonly int[] _texCubeH = new int[4];
    private readonly int[] _dotMap = new int[4];
    private readonly float[] _eyePos = new float[3];
    private readonly float[] _bumpMatrix = new float[3 * 4];
    private readonly float[] _bumpLumeScale = new float[3];
    private readonly float[] _bumpLumeOffset = new float[3];

    // The BUMPENVMAP per-stage matrix and luminance pair as the seam publishes
    // them (the software sampler's own values - Nv2AMmio.BumpState).
    private PgraphBumpState _bump;
    private readonly float[] _scalarScratch = new float[14];
    private readonly float[] _texScratch = new float[56];

    // GL-state shadow for ApplyState: consecutive draws usually share the
    // same resolved ROP state, so the redundant Enable/Disable/Func calls are
    // skipped. The shadow always reflects what was last uploaded to GL;
    // _stateValid starts false so the first draw applies everything.
    private bool _stateValid;
    private bool _glBlendEnabled;
    private float _glBlendR;
    private float _glBlendG;
    private float _glBlendB;
    private float _glBlendA;
    private uint _glBlendSrc;
    private uint _glBlendDst;
    private uint _glBlendEquation;
    private uint _glColorMask;
    private bool _glDepthEnabled;
    private uint _glDepthFunc;
    private bool _glDepthMask;
    private bool _glStencilEnabled;
    private uint _glStencilFunc;
    private uint _glStencilRef;
    private uint _glStencilReadMask;
    private uint _glStencilOpFail;
    private uint _glStencilOpZfail;
    private uint _glStencilOpZpass;
    private uint _glStencilWriteMask;

    private uint _fbo;
    private uint _colorRenderbuffer;
    private uint _depthRenderbuffer;
    private int _fboWidth;
    private int _fboHeight;
    private uint _fboZetaBytes;
    private byte[]? _readback;
    private uint _readbackWidth;
    private uint _readbackHeight;
    private string? _fatalError;
    private bool _disposed;
    private long _unsupportedColorBytes;
    private long _unsupportedZFormat;
    private long _unsupportedModes;
    private long _unsupportedCubeVolume;
    private long _unsupportedFbo;
    private long _unsupportedFlip;
    private long _fatalCount;

    // Which texture modes the declined draws asked for (0x00-0x1F, the
    // combiner's 5-bit tex-mode field). `unsup[modes=]` says how many draws were
    // declined; this says what to implement next (Track B3 method coverage needs
    // the mode, not the total). Incremented only on a decline, never per draw.
    private readonly long[] _unsupportedTexModeCounts = new long[0x20];

    // Slice-2 evidence diagnostics (informational, never BackendUnsupported):
    // measure exposure of the §113 open risks so their disposition is backed
    // by run counters instead of assumption. _fboReallocs counts FBO
    // reallocations with prior content (grow / zeta change — content is
    // preserved by blit except depth on a zeta format change, counted in
    // _fboZetaDrops); _clipXYNonZero counts draws/clears whose clip window
    // origin is not (0,0) (the readback origin assumption); _flipEmpty
    // counts flips with no backend-rendered content yet (no draw/clear has
    // created the FBO — e.g. CPU-blitted FMV presents, which the backend
    // must not mix software pixels into, so no frame is served);
    // _presentUploads counts flips where the FBO was filled by the
    // present-upload path (guest-RAM surface full-frame replace — either
    // the empty-FBO case or, since §135 (a*), a dirty non-empty FBO);
    // _flipStaleUploads counts the latter (dirty live-FBO uploads), the
    // §135 fail-closed stale-present diagnostic.
    private long _fboReallocs;
    private long _fboZetaDrops;
    private long _clipXyNonZero;
    private long _flipEmpty;
    private long _presentUploads;
    private long _flipStaleUploads;

    // Compatibility gap 8 (replace mode): a frame whose draw the backend
    // declined is composed from the FBO into guest RAM and handed to the
    // software rasterizer; the flip then uploads that RAM image instead of
    // serving the FBO. The flag lives for one frame.
    private bool _frameComposed;
    private long _composedFrames;
    private long _composedPixels;
    private long _composedDraws;
    private GlBackendTimings _timings;
    private readonly bool _windowRequested;
    private bool _windowShown;

    private sealed class GlProgram(uint id)
    {
        public uint Id { get; } = id;

        public Dictionary<string, int> Locations { get; } = new(StringComparer.Ordinal);

        // Per-program uniform upload caches. GL uniform state lives on the
        // program object, so unchanged values can be skipped entirely; the
        // *Valid flags force a full upload on first use.
        public float[] ScalarCache { get; } = new float[14];

        /// <summary>Raw combiner constant registers captured with <see cref="ConstCache"/> (cheap change stamp: FillConsts costs 40 unpack conversions + 72 float compares per draw).</summary>
        public uint[] ConstStamp { get; } = new uint[18];

        // Resolved once at link: the per-draw packed block location (a
        // per-draw dictionary lookup was measurable on 2.76M draws).
        public int TriLocation { get; set; } = -1;

        public float[] TexCache { get; } = new float[56];

        /// <summary>Change stamp of the cube/dot/decode uniforms (not covered by <see cref="TexCache"/>).</summary>
        public uint CubeModeStamp { get; set; }

        public float[] ConstCache { get; } = new float[72];

        public uint ScalarClipWord { get; set; }

        public bool ScalarValid { get; set; }

        public bool TexValid { get; set; }

        public bool ConstValid { get; set; }

        public int Location(string name)
        {
            if (!Locations.TryGetValue(name, out int location))
            {
                location = -2;
                Locations[name] = location;
            }

            return location;
        }
    }

    private sealed class GlTextureEntry(uint texture, int width, int height)
    {
        public uint Texture { get; set; } = texture;

        /// <summary>
        /// The stage's six swizzled guest faces decoded into a GL 2D-array
        /// texture (layer = face), 0 when the stage is not a sampleable cube.
        /// A cube face is not a 2D image row, so it cannot ride the linear
        /// upload; this one is decoded on the host through
        /// <see cref="Nv2AMmio.DecodeSwizzledTexelWord"/> - the software
        /// sampler's own decoder.
        /// </summary>
        public uint CubeTexture { get; set; }

        /// <summary>
        /// Whole-range content hash of the guest bytes the texture was last
        /// produced from (FNV-1a over <c>[offset, offset + length)</c>), taken
        /// at <see cref="DataStamp"/>.
        /// </summary>
        public uint DataHash { get; set; }

        /// <summary>
        /// Guest write stamp when <see cref="DataHash"/> was taken
        /// (<see cref="IGuestWriteStamp"/>): an unchanged stamp means the bytes
        /// cannot have moved, so the hash is not recomputed. This is the
        /// reference's "only hash when possibly dirty" rule with a
        /// page-generation sum instead of a page dirty bitmap.
        /// </summary>
        public ulong DataStamp { get; set; }

        /// <summary>
        /// Guest addresses of a 16-texel spread grid, resolved once per cache
        /// key. The HILO dot stage carries the raw words at these addresses
        /// into the shader's guest-word pool (no decoded texel carries the raw
        /// 16-bit pair).
        /// </summary>
        public uint[] SampleAddresses { get; set; } = [];

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        // ReSharper disable once UnusedMember.Local
        public int Width { get; } = width;
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        // ReSharper disable once UnusedMember.Local
        public int Height { get; } = height;
    }

    /// <summary>
    /// Combiner structure words that change the generated GLSL (factors, fog,
    /// alpha, ZCLIP and texture enable/scale are uniforms and must not split
    /// the key). Kept in step with <see cref="PshGlslGenerator.BuildKey"/>.
    /// </summary>
    private const int ProgramStructureWords = 5 + (PshCombiner.MaxStages * 4);

    /// <summary>
    /// Content-addressed texture cache key (guest offset + format + extent).
    /// A value struct: the pre-Track-B path formatted a string per slot per
    /// draw, which showed up as submission cost on the menu's 2.76M draws.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct TextureKey(in PgraphTextureSlot slot, uint color, int width, int height, int pitch, bool linear) : IEquatable<TextureKey>
    {
        private readonly uint _offset = slot.Offset;
        private readonly uint _color = color;
        private readonly int _width = width;
        private readonly int _height = height;
        private readonly int _pitch = pitch;
        private readonly int _linear = linear ? 1 : 0;

        // The reference's TextureKey is the whole TextureShape; these are the
        // shape fields that change what a binding contains but are not in the
        // extent (mip count, volume dimensionality, the cube flag), so two
        // textures sharing offset/format/dims cannot alias.
        private readonly int _levels = (int)((slot.Format >> 16) & 0xFu);
        private readonly int _dim = (int)((slot.Format & 0xC0u) >> 6);
        private readonly int _cube = (slot.Format & 0x04u) != 0 ? 1 : 0;

        public bool Equals(TextureKey other)
        {
            return _offset == other._offset && _color == other._color &&
                   _width == other._width && _height == other._height &&
                   _pitch == other._pitch && _linear == other._linear &&
                   _levels == other._levels && _dim == other._dim && _cube == other._cube;
        }

        public override bool Equals(object? obj)
        {
            return obj is TextureKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_offset);
            hash.Add(_color);
            hash.Add(_width);
            hash.Add(_height);
            hash.Add(_pitch);
            hash.Add(_linear);
            hash.Add(_levels);
            hash.Add(_dim);
            hash.Add(_cube);
            return hash.ToHashCode();
        }
    }

    internal GlPgraphBackend(WglContext context, INativeContext glContext, GL gl, IGpuMemory memory,
        bool windowEnabled = false, bool replaceSoftware = false, bool frameReadback = true)
    {
        _context = context;
        _glContext = glContext;
        _gl = gl;
        _memory = memory;
        _dirtyWatch = memory as IPresentDirtyWatch;
        _writeStamp = memory as IGuestWriteStamp;
        _windowRequested = windowEnabled;
        ReplaceMode = replaceSoftware;
        FlipReadbackEnabled = frameReadback;
        WindowStatus = windowEnabled ? "pending first frame" : "off";
        if (memory is IGpuMemoryRamView ramView && ramView.TryGetRamBytes(out byte[] ramBytes, out uint ramSize))
        {
            _ramBytes = ramBytes;
            _ramSize = ramSize;
        }

        // Uniform-budget batch capacity: the packed block is 25 vec4 per
        // triangle and both stages declare the array, so the smaller stage
        // limit applies. The reserve covers every other uniform the program
        // declares: the combiner table, fog/alpha/ZCLIP scalars, ~40 vec4 of
        // per-slot texture descriptors, the B3 guest-word pool (96 uints = 24
        // vec4) and the bump state. It is MEASURED: with the pool added and an
        // untouched reserve NVIDIA failed the fragment LINK ("constant
        // register limit exceeded" - that compiler fits an arbitrary-capacity
        // uniform array into the constant bank as if it were up to 1024
        // registers), and every larger reserve still overflowed, so the
        // reserve is 192 vec4 = 768 registers + 25 x capacity <= 1024 minus a
        // safety margin. Cost: the menu's 82 triangles/draw command become
        // 32 instanced draws instead of 3 for the same triangles.
        bool batchingDisabled = string.Equals(
            Environment.GetEnvironmentVariable("XGRID_GL_NO_BATCH"), "1", StringComparison.Ordinal);
        int maxVectors = Math.Min(
            QueryInteger(GetPName.MaxFragmentUniformVectors, 224),
            QueryInteger(GetPName.MaxVertexUniformVectors, 224));
        int capacity = batchingDisabled ? 1 : Math.Clamp((maxVectors - 192) / 25, 1, MaxBatchTriangles);
        _batchCapacity = capacity;
        _batchData = capacity > 1 ? new float[capacity * TriBlockFloats] : [];

        _vertexShader = CompileVertexShader(capacity);
        // Empty VAO only: every per-draw value travels in the uTri uniform
        // block (positions are selected by gl_VertexID), so there is no vertex
        // buffer and no per-draw upload.
        _gl.GenVertexArrays(1, out _vao);
        _gl.BindVertexArray(_vao);

        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.DepthRange(0.0, 1.0);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        _gl.ClearColor(0f, 0f, 0f, 0f);
    }

    /// <summary>Gets the number of draws, clears and flips that failed with a fatal GL error or an unsupported in-subset state; the gate requires 0.</summary>
    public long BackendUnsupported { get; private set; }

    /// <summary>
    /// Compiles and links one shader pair on this backend's context, returning
    /// the driver log on failure. Test seam for the generated computed-mode
    /// shaders: a string golden cannot prove a program is accepted by a real
    /// driver (a redeclaration compiled fine as text and failed here).
    /// </summary>
    /// <param name="vertexSource">Vertex shader source.</param>
    /// <param name="fragmentSource">Fragment shader source.</param>
    /// <returns>The failure log, or null on success.</returns>
    internal string? BuildProgramForTest(string vertexSource, string fragmentSource)
    {
        return GlProbe.TryBuildProgram(_gl, vertexSource, fragmentSource);
    }

    /// <summary>
    /// Gets or sets whether a flip reads the FBO back to CPU memory. The
    /// readback is a gate/screenshot facility (CLI <c>--frame</c>, the
    /// frontend's display copy), not a per-frame cost: it forces a full GPU
    /// sync and a per-pixel R/B swizzle. Window presentation blits the FBO
    /// directly and never needs it.
    /// </summary>
    public bool FlipReadbackEnabled { get; set; }


    /// <summary>Upper bound on batched triangles per instanced draw (uniform budget permitting).</summary>
    private const int MaxBatchTriangles = 64;

    /// <summary>Reads one GL integer limit, falling back when the driver refuses the query (a 0 would disable batching).</summary>
    private int QueryInteger(GetPName name, int fallback)
    {
        int value = _gl.GetInteger(name);
        return value > 0 ? value : fallback;
    }

    /// <summary>Gets the number of triangles submitted to GL.</summary>
    public long Draws { get; private set; }

    /// <summary>Gets the number of instanced draws issued for batched draw commands.</summary>
    public long BatchCalls { get; private set; }

    /// <summary>Gets the number of triangles submitted through batched instanced draws.</summary>
    public long BatchTriangles { get; private set; }

    /// <summary>Gets the number of clears mirrored to the FBO.</summary>
    public long Clears { get; private set; }

    /// <summary>Gets the number of flips read back from the FBO.</summary>
    public long Flips { get; private set; }

    /// <summary>Gets the guest-physical surface address of the last flip. Observation only (hook for the present-upload path); never changes GL behavior.</summary>
    public uint LastFlipSurfaceAddress { get; private set; }

    /// <summary>Gets the number of texture uploads performed.</summary>
    public long TextureUploads { get; private set; }

    /// <summary>Gets the number of fragment programs compiled.</summary>
    public long ProgramsCompiled { get; private set; }

    /// <summary>Gets a one-line diagnostics summary of the GL backend: counters, unsupported buckets, diagnostics, timings, and any latched fatal error.</summary>
    public string DebugSummary
    {
        get
        {
            var sb = new StringBuilder(384);
            sb.Append(CultureInfo.InvariantCulture,
                $"backend=gl draws={Draws} clears={Clears} flips={Flips} " +
                $"programs={_programs.Count} uploads={TextureUploads} unsupported={BackendUnsupported}" +
                $" unsup[colorBytes={_unsupportedColorBytes} zformat={_unsupportedZFormat} modes={_unsupportedModes}" +
                $" cubeVol={_unsupportedCubeVolume} fbo={_unsupportedFbo} flip={_unsupportedFlip} fatal={_fatalCount}]" +
                $" unsupTex[{DescribeUnsupportedTexModes()}]" +
                $" diag[fboReallocs={_fboReallocs} fboZetaDrops={_fboZetaDrops} clipXY={_clipXyNonZero} flipEmpty={_flipEmpty} presentUploads={_presentUploads} flipStaleUploads={_flipStaleUploads} rtDownloads={_rtDownloads} rtSwizzleSeen={_rtSwizzleSeen} " +
                $"composedFrames={_composedFrames} composedPixels={_composedPixels} composedDraws={_composedDraws} " +
                $"batchCalls={BatchCalls} batchTris={BatchTriangles} batchCap={_batchCapacity} " +
                $"guestWordDraws={GuestWordDraws} guestWordMisses={GuestWordMisses} guestWordDeclined={_guestWordDeclinedDraws}]");
            _timings.AppendTo(sb);
            if (_probeEnabled)
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                sb.Append(CultureInfo.InvariantCulture,
                    $" probe[samples={_probeSamples} prepMs={_probePrepTicks * toMs:F0} " +
                    $"texMs={_probeTextureTicks * toMs:F0} stateMs={_probeStateTicks * toMs:F0} " +
                    $"uniMs={_probeUniformTicks * toMs:F0} vtxMs={_probeVertexTicks * toMs:F0} " +
                    $"glMs={_probeGlTicks * toMs:F0} fpRam={_probeFpRam} fpAdapter={_probeFpAdapter}]");
            }
            if (_fatalError is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" fatal=\"{_fatalError}\"");
            }

            sb.Append(CultureInfo.InvariantCulture, $" uniforms=[{string.Join(",", UniformNames)}]");

            return sb.ToString();
        }
    }

    /// <summary>The software path owns the method total (<c>Nv2aMmio.MethodTotal</c>): declining the per-method callback removes a virtual call per PGRAPH method (18M per 1B menu steps).</summary>
    public bool ObserveMethods => false;

    /// <summary>Observes one PGRAPH method triple; never called because <see cref="ObserveMethods"/> is false.</summary>
    public void Method(uint subchannel, uint method, uint parameter)
    {
        _ = subchannel;
        _ = method;
        _ = parameter;
    }

    /// <summary>Gets whether this backend replaces the software rasterizer for the draws it honors (opt-in replacement mode).</summary>
    public bool ReplaceMode { get; }

    /// <summary>Mirrors one triangle into the FBO (mirror mode; the software rasterizer stays authoritative and runs as today).</summary>
    public void Draw(in PgraphDrawInfo info)
    {
        _ = TryDraw(info);
    }

    /// <summary>Attempts to render one triangle; true when the replacement-mode caller must skip the software rasterizer.</summary>
    public bool TryDraw(in PgraphDrawInfo info)
    {
        if (_fatalError is not null)
        {
            return false;
        }

        if (info.ColorSwizzle)
        {
            _rtSwizzleSeen++;
            _rtSwizzlePending = true;
            _rtAddress = info.ColorSurfaceAddress;
            _rtColorBytes = info.ColorBytes;
            _rtSwizzleWidth = info.ColorSwizzleWidth;
            _rtSwizzleHeight = info.ColorSwizzleHeight;
            _rtClipX = info.ClipX;
            _rtClipY = info.ClipY;
            _rtClipW = info.ClipW;
            _rtClipH = info.ClipH;
        }

        if (ReplaceMode && _frameComposed)
        {
            // The frame was already handed back to the software rasterizer
            // (compatibility gap 8): every later draw must take the same
            // writer, or the two images diverge and the flip loses one.
            _composedDraws++;
            return false;
        }

        try
        {
            long start = Stopwatch.GetTimestamp();
            _drawDeclinedByGuestWords = false;
            bool drawn = TryDrawCore(info);
            if (!drawn)
            {
                // The caller will paint this triangle in software; submit the
                // buffered host triangles first so the two writers keep the
                // pre-batching draw order.
                FlushBatch();
                if (ReplaceMode)
                {
                    // Declining a draw in replacement mode would otherwise
                    // lose every GL pixel of this frame (the flip serves the
                    // FBO while the fallback pixels go to guest RAM). Compose
                    // the FBO into guest RAM once, then let the software path
                    // own the rest of the frame.
                    ComposeToGuestSurface(in info);
                }

                if (_drawDeclinedByGuestWords && ReplaceMode)
                {
                    // Counted, not silent (BackendUnsupported would trip the
                    // gate for a mode the shader does implement): the draw's
                    // guest words were not in the carried set, so it went to
                    // the software path.
                    _guestWordDeclinedDraws++;
                }
            }

            _timings.AddDrawTicks(Stopwatch.GetTimestamp() - start);
            return drawn;
        }
        catch (Exception exception)
        {
            // Fail loud and stop issuing GL calls rather than storming.
            BackendUnsupported++;
            _fatalCount++;
            _fatalError = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    private bool _drawDeclinedByGuestWords;
    private long _guestWordDeclinedDraws;

    /// <summary>
    /// Copies the FBO's clip window into the guest color surface so the
    /// software rasterizer can continue the same frame (compatibility gap 8).
    /// The FBO is the clip window with guest row 0 at its bottom, so FBO pixel
    /// (x, y) is guest pixel (ClipX + x, ClipY + y) at the seam's
    /// <see cref="PgraphDrawInfo.ColorSurfaceAddress"/>. Depth/stencil are not
    /// composed (the GL depth buffer is host-side): a frame that mixes the two
    /// writers keeps color continuity, which is what the presented image needs.
    /// </summary>
    private void ComposeToGuestSurface(in PgraphDrawInfo info)
    {
        if (_frameComposed || _fbo == 0)
        {
            return;
        }

        int width = (int)info.ClipW;
        int height = (int)info.ClipH;
        if (width <= 0 || height <= 0 || info.ColorBytes != 4 ||
            info.ColorPitch < (uint)(width * 4) ||
            _fboWidth < width || _fboHeight < height)
        {
            return;
        }

        FlushBatch();
        byte[] rgba = new byte[width * height * 4];
        long start = Stopwatch.GetTimestamp();
        BindFbo(_fbo);
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        _timings.AddReadback(Stopwatch.GetElapsedTime(start));
        uint baseAddress = info.ColorSurfaceAddress;
        uint pitch = info.ColorPitch;
        for (int y = 0; y < height; y++)
        {
            uint row = baseAddress + ((info.ClipY + (uint)y) * pitch);
            for (int x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;
                uint pixel = (uint)((rgba[at] << 16) | (rgba[at + 1] << 8) | rgba[at + 2] | (rgba[at + 3] << 24));
                _memory.WritePhysical(row + ((info.ClipX + (uint)x) * 4u), pixel);
            }
        }

        _frameComposed = true;
        _composedFrames++;
        _composedPixels += (long)width * height;
    }

    /// <summary>Opens one draw-command batch: from here to <see cref="EndDrawBatch"/> the guest state and RAM are frozen, so triangles are staged and submitted together.</summary>
    public void BeginDrawBatch()
    {
        if (_fatalError is not null || _batchCapacity <= 1)
        {
            return;
        }

        _batchOpen = true;
        _batchCount = 0;
        _batchStateReady = false;
    }

    /// <summary>Closes the batch opened by <see cref="BeginDrawBatch"/> and submits its remaining triangles.</summary>
    public void EndDrawBatch()
    {
        if (!_batchOpen)
        {
            return;
        }

        try
        {
            FlushBatch();
        }
        catch (Exception exception)
        {
            BackendUnsupported++;
            _fatalCount++;
            _fatalError = exception.GetType().Name + ": " + exception.Message;
        }

        _batchOpen = false;
        _batchStateReady = false;
        _batchCount = 0;
        _batchProgram = null;
    }

    /// <summary>Mirrors a clear the software path performed into the FBO via ClearCore, timing the call; a thrown GL exception increments <see cref="BackendUnsupported"/> and latches a fatal error.</summary>
    public void Clear(in PgraphClearInfo info)
    {
        if (_fatalError is not null)
        {
            return;
        }

        try
        {
            // A clear must never be reordered before buffered triangles.
            FlushBatch();
            long start = Stopwatch.GetTimestamp();
            ClearCore(info);
            _timings.AddClear(Stopwatch.GetElapsedTime(start));
        }
        catch (Exception exception)
        {
            BackendUnsupported++;
            _fatalError = exception.GetType().Name + ": " + exception.Message;
        }
    }

    /// <summary>Handles the present event by reading the presented frame back from the FBO via FlipCore; a thrown GL exception increments <see cref="BackendUnsupported"/> and latches a fatal error.</summary>
    public void Flip(in PgraphSurfaceRecord record)
    {
        if (_fatalError is not null)
        {
            return;
        }

        try
        {
            // The presented frame must include every triangle submitted so far.
            FlushBatch();
            FlipCore(record);
        }
        catch (Exception exception)
        {
            BackendUnsupported++;
            _fatalError = exception.GetType().Name + ": " + exception.Message;
        }
    }

    /// <summary>Returns the last flip's readback frame as BGRA bytes.</summary>
    /// <returns>False when no frame has been read back; the caller must not mix in software pixels.</returns>
    public bool ReadbackPresented(out uint width, out uint height, out byte[] bgra)
    {
        width = _readbackWidth;
        height = _readbackHeight;
        bgra = _readback ?? [];
        return _readback is not null;
    }

    /// <summary>Drops host-side GL state (FBO, textures, readback, counters) for a snapshot load or machine reset, and marks the present dirty so the next flip re-uploads instead of serving leftovers.</summary>
    public void ResetState()
    {
        // Host-side only: drop surfaces/textures/readback. Programs are
        // content-addressed by combiner structure and stay valid.
        if (_disposed)
        {
            return;
        }

        // Buffered triangles belong to the FBO that is about to be deleted.
        _batchOpen = false;
        _batchCount = 0;
        _batchStateReady = false;
        _batchProgram = null;
        DeleteFbo();
        foreach (KeyValuePair<TextureKey, GlTextureEntry> entry in _textures)
        {
            uint texture = entry.Value.Texture;
            _gl.DeleteTextures(1, ref texture);
        }

        _textures.Clear();
        _readback = null;
        _readbackWidth = 0;
        _readbackHeight = 0;
        InvalidateTextureBinding();
        _fatalError = null;
        Draws = 0;
        BatchCalls = 0;
        BatchTriangles = 0;
        Clears = 0;
        Flips = 0;
        LastFlipSurfaceAddress = 0;
        TextureUploads = 0;
        BackendUnsupported = 0;
        _unsupportedColorBytes = 0;
        _unsupportedZFormat = 0;
        _unsupportedModes = 0;
        _unsupportedCubeVolume = 0;
        _unsupportedFbo = 0;
        _unsupportedFlip = 0;
        Array.Clear(_unsupportedTexModeCounts);
        _fatalCount = 0;
        _fboReallocs = 0;
        _fboZetaDrops = 0;
        _clipXyNonZero = 0;
        _flipEmpty = 0;
        _presentUploads = 0;
        _flipStaleUploads = 0;
        _frameComposed = false;
        _composedFrames = 0;
        _composedPixels = 0;
        _composedDraws = 0;
        _timings.Reset();
        _probeDraws = 0;
        _probeSamples = 0;
        _probePrepTicks = 0;
        _probeTextureTicks = 0;
        _probeStateTicks = 0;
        _probeUniformTicks = 0;
        _probeVertexTicks = 0;
        _probeGlTicks = 0;

        // Fail-closed (§132 (a*)): reset/load replaced guest state under a
        // backend whose FBO (just deleted above) may have been the presented
        // content — the next flip must upload, never serve leftovers. The
        // dirty bit survives the disarm; only a successful upload clears it.
        _dirtyWatch?.MarkPresentDirty();
        _dirtyWatch?.DisarmPresentWatch();
    }

    /// <summary>Deletes the FBO, textures, programs, buffers and vertex shader, disarms the present watch, and disposes the GL and WGL contexts.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _batchOpen = false;
        _batchCount = 0;
        try
        {
            DeleteFbo();
            foreach (KeyValuePair<TextureKey, GlTextureEntry> entry in _textures)
            {
                uint texture = entry.Value.Texture;
                _gl.DeleteTextures(1, ref texture);
            }

            _textures.Clear();
            foreach (KeyValuePair<string, GlProgram> entry in _programs)
            {
                _gl.DeleteProgram(entry.Value.Id);
            }

            _programs.Clear();
            uint vao = _vao;
            if (vao != 0)
            {
                _gl.DeleteVertexArrays(1, ref vao);
            }

            if (_vertexShader != 0)
            {
                _gl.DeleteShader(_vertexShader);
            }
        }
        catch
        {
            // Disposal is best-effort at process teardown.
        }

        _dirtyWatch?.DisarmPresentWatch();
        _glContext.Dispose();
        _context.Dispose();
    }

    private bool TryDrawCore(in PgraphDrawInfo info)
    {
        Draws++;
        if (info.ClipX != 0 || info.ClipY != 0)
        {
            _clipXyNonZero++;
        }

        if (info.ColorBytes != 4)
        {
            BackendUnsupported++;
            _unsupportedColorBytes++;
            return false;
        }

        if (info.ZFormatFloat && info.ZetaBytes != 0 && info.Fragment.DepthTestEnable)
        {
            BackendUnsupported++;
            _unsupportedZFormat++;
            return false;
        }

        bool legacy = !info.CombinerActive;
        PshCombiner? combiner = info.Combiner;
        if (!legacy && combiner is null)
        {
            BackendUnsupported++;
            _unsupportedModes++;
            return false;
        }

        if (!legacy && CheckModesUnsupported(info, combiner!, out bool cubeVolume))
        {
            BackendUnsupported++;
            if (cubeVolume)
            {
                _unsupportedCubeVolume++;
            }
            else
            {
                _unsupportedModes++;
            }

            if (ReplaceMode)
            {
                // Replacement mode: an unhonored draw must come from the
                // software path, never from a partly-mapped state.
                return false;
            }
        }

        if (!EnsureFbo((int)info.ClipW, (int)info.ClipH, info.ZetaBytes))
        {
            BackendUnsupported++;
            _unsupportedFbo++;
            return false;
        }

        bool probe = _probeEnabled && ((_probeDraws++ & 63L) == 0L);
        if (_batchOpen && _batchCapacity > 1)
        {
            // Inside one draw command the guest cannot change PGRAPH state or
            // RAM, so program/texture/state resolution runs once per batch and
            // the whole run is one instanced draw (Track B: at 82 triangles
            // per command this removes ~98% of the submission calls).
            if (!_batchStateReady)
            {
                if (!ApplyDrawState(in info, legacy, combiner, probe))
                {
                    _drawDeclinedByGuestWords = true;
                    return false;
                }

                _batchStateReady = true;
                _batchProgram = _lastProgram;
            }

            long vtxStart = probe ? Stopwatch.GetTimestamp() : 0L;
            PackVerticesInto(in info, _batchData, _batchCount * TriBlockFloats);
            if (probe)
            {
                _probeVertexTicks += Stopwatch.GetTimestamp() - vtxStart;
                _probeSamples++;
            }

            _batchCount++;
            BatchTriangles++;
            if (_batchCount == _batchCapacity)
            {
                FlushBatch();
            }

            return true;
        }

        if (!ApplyDrawState(in info, legacy, combiner, probe))
        {
            _drawDeclinedByGuestWords = true;
            return false;
        }

        long t2 = probe ? Stopwatch.GetTimestamp() : 0L;
        PackVertices(in info, out int vertexCount);
        UploadTriangleUniforms(_lastProgram!);
        long t3 = probe ? Stopwatch.GetTimestamp() : 0L;
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
        if (probe)
        {
            long t4 = Stopwatch.GetTimestamp();
            // 1-in-64 sampling keeps the probe's own cost off the measuring
            // path (per-draw probes distorted the submission bill ~2x).
            _probeUniformTicks += t3 - t2;
            _probeGlTicks += t4 - t3;
            _probeSamples++;
        }

        return true;
    }

    /// <summary>
    /// Resolves and applies everything that is constant for one draw command:
    /// program, textures, GL state and the per-draw uniforms. Runs once per
    /// batch instead of once per triangle. Returns false when a computed
    /// cube/dot texture mode needs a guest word the seam did not carry — the
    /// caller declines that draw instead of sampling a guess.
    /// </summary>
    private bool ApplyDrawState(in PgraphDrawInfo info, bool legacy, PshCombiner? combiner, bool probe)
    {
        long t0 = probe ? Stopwatch.GetTimestamp() : 0L;
        GlProgram program = EnsureProgram(legacy, combiner);
        long t1 = probe ? Stopwatch.GetTimestamp() : 0L;
        if (!ApplyTextures(program, info, combiner, legacy, _batchStateReady))
        {
            return false;
        }

        long t1b = probe ? Stopwatch.GetTimestamp() : 0L;
        ApplyState(in info);
        long t1c = probe ? Stopwatch.GetTimestamp() : 0L;

        BindFbo(_fbo);
        if (_viewportW != info.ClipW || _viewportH != info.ClipH)
        {
            _gl.Viewport(0, 0, info.ClipW, info.ClipH);
            _viewportW = info.ClipW;
            _viewportH = info.ClipH;
        }

        if (_currentProgram != program.Id)
        {
            _gl.UseProgram(program.Id);
            _currentProgram = program.Id;
        }

        ApplyUniforms(program, in info, combiner);
        long t2 = probe ? Stopwatch.GetTimestamp() : 0L;

        if (probe)
        {
            _probePrepTicks += t1 - t0;
            _probeTextureTicks += t1b - t1;
            _probeStateTicks += t1c - t1b;
            _probeUniformTicks += t2 - t1c;
        }

        return true;
    }

    /// <summary>Submits every triangle buffered for the current batch as one instanced draw.</summary>
    /// <summary>
    /// XFP5: downloads a swizzled render target's drawn clip region from the
    /// FBO into guest RAM in Morton order (the reference's
    /// <c>surface_download</c> + <c>swizzle_rect</c>). The batch's vertices
    /// address the surface at ClipX/ClipY, so the FBO pixel (x, y) maps to
    /// the surface texel (ClipX + x, ClipY + y) and its swizzled offset; the
    /// write goes through the GPU-memory adapter, which bumps the guest write
    /// stamp so the texture cache re-hashes the surface when the guest samples
    /// it. Zeta is not downloaded (the FBO keeps it for the frame's own depth
    /// tests; a title that samples a swizzled render target's depth would need
    /// the same walk over the depth attachment - none measured).
    /// </summary>
    private void DownloadSwizzledTarget()
    {
        _rtSwizzlePending = false;
        if (!ReplaceMode || _rtColorBytes != 4 || _fbo == 0 ||
            _rtSwizzleWidth <= 0 || _rtSwizzleHeight <= 0)
        {
            return;
        }

        int width = (int)Math.Min(_rtClipW, (uint)_fboWidth);
        int height = (int)Math.Min(_rtClipH, (uint)_fboHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        byte[] rgba = new byte[width * height * 4];
        long start = Stopwatch.GetTimestamp();
        BindFbo(_fbo);
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        _timings.AddReadback(Stopwatch.GetElapsedTime(start));
        for (int y = 0; y < height; y++)
        {
            uint surfaceY = _rtClipY + (uint)y;
            for (int x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;
                uint pixel = (uint)((rgba[at] << 16) | (rgba[at + 1] << 8) | rgba[at + 2] |
                                    (rgba[at + 3] << 24));
                uint texel = Nv2AMmio.SwizzleOffset(
                    (int)(_rtClipX + (uint)x), (int)surfaceY, _rtSwizzleWidth, _rtSwizzleHeight);
                _memory.WritePhysical(_rtAddress + (texel * 4u), pixel);
            }
        }

        _rtDownloads++;
    }

    private void FlushBatch()
    {
        if (_batchCount == 0)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        GlProgram program = _batchProgram!;
        _gl.Uniform4(program.TriLocation, (uint)(_batchCount * 25), ref _batchData[0]);
        _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 3u, (uint)_batchCount);
        _timings.AddBatchFlushTicks(Stopwatch.GetTimestamp() - start);
        BatchCalls++;
        _batchCount = 0;

        if (_rtSwizzlePending && ReplaceMode)
        {
            DownloadSwizzledTarget();
        }
    }

    // XFP5 diagnostic: XGRID_GL_DUMP_FLIP=<dir> dumps the FBO and the
    // present-upload source for each stale-FBO flip (the only frames where the
    // two writers can lose each other), after the batch flush so the FBO holds
    // the submitted frame.
    private readonly string? _dumpFlipDir = Environment.GetEnvironmentVariable("XGRID_GL_DUMP_FLIP");

    private readonly bool _probeEnabled = string.Equals(
        Environment.GetEnvironmentVariable("XGRID_GL_PROBE"), "1", StringComparison.Ordinal);
    private long _probeDraws;
    private long _probeSamples;
    private long _probePrepTicks;
    private long _probeTextureTicks;
    private long _probeStateTicks;
    private long _probeUniformTicks;
    private long _probeVertexTicks;
    private long _probeGlTicks;

    private void ClearCore(in PgraphClearInfo info)
    {
        Clears++;
        if (info.ClipX != 0 || info.ClipY != 0)
        {
            _clipXyNonZero++;
        }

        if (info.ClearColor && info.ColorBytes != 4)
        {
            BackendUnsupported++;
            _unsupportedColorBytes++;
        }

        if (info.ClearZeta && info.ZetaBytes != 0 && info.ZFormatFloat)
        {
            BackendUnsupported++;
            _unsupportedZFormat++;
        }

        if (info.ClipW == 0 || info.ClipH == 0)
        {
            return;
        }

        if (!EnsureFbo((int)info.ClipW, (int)info.ClipH, info.ZetaBytes))
        {
            BackendUnsupported++;
            _unsupportedFbo++;
            return;
        }

        BindFbo(_fbo);
        _gl.Enable(EnableCap.ScissorTest);
        int x = unchecked((int)(info.XMin - info.ClipX));
        int y = unchecked((int)(info.YMin - info.ClipY));
        uint width = info.XMax - info.XMin + 1;
        uint height = info.YMax - info.YMin + 1;
        _gl.Scissor(x, y, width, height);

        // A clear follows the clear's OWN write masks (CLEAR_SURFACE_R/G/B/A
        // and _Z/_STENCIL), not the draw's: a channel the clear does not
        // select keeps the surface's previous value, and a masked-out
        // stencil/color draw state must not suppress the clear. GL's glClear
        // honors glColorMask / glDepthMask / glStencilMask, so program them
        // from the clear selection and force the next draw to re-upload the
        // draw state.
        uint channels = info.ColorChannels & 0xFu;
        _gl.ColorMask(
            (channels & 0x1u) != 0,
            (channels & 0x2u) != 0,
            (channels & 0x4u) != 0,
            (channels & 0x8u) != 0);
        bool clearDepthField = (info.ZetaFields & 0x1u) != 0;
        bool clearStencilField = info.ZetaBytes == 4 && (info.ZetaFields & 0x2u) != 0;
        _gl.DepthMask(clearDepthField);
        _gl.StencilMask(clearStencilField ? 0xFFu : 0x00u);
        _stateValid = false;

        if (info.ClearColor && info.ColorBytes == 4)
        {
            GlStateMapper.MapClearColor(info.ColorValue, out float r, out float g, out float b, out float a);
            _gl.ClearColor(r, g, b, a);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
        }

        if (info.ClearZeta && info.ZetaBytes != 0 && !info.ZFormatFloat &&
            (clearDepthField || clearStencilField))
        {
            GlStateMapper.MapZetaClear(info.ZetaValue, info.ZetaBytes, out double depth, out int stencil);
            _gl.ClearDepth(depth);
            _gl.ClearStencil(stencil);
            ClearBufferMask mask = ClearBufferMask.None;
            if (clearDepthField)
            {
                mask |= ClearBufferMask.DepthBufferBit;
            }

            if (clearStencilField)
            {
                mask |= ClearBufferMask.StencilBufferBit;
            }

            _gl.Clear(mask);
        }

        _gl.Disable(EnableCap.ScissorTest);
    }

    private void FlipCore(in PgraphSurfaceRecord record)
    {
        Flips++;
        // Carry the resolved guest-physical surface address for the
        // present-upload path (no upload yet: stored, never acted on).
        // Recorded before every early return so the empty-FBO path — the
        // CPU-blitted case the upload will serve — carries it too.
        LastFlipSurfaceAddress = record.SurfaceAddress;
        if (record.ColorBytes != 4)
        {
            BackendUnsupported++;
            _unsupportedColorBytes++;
            _readback = null;
            _readbackWidth = 0;
            _readbackHeight = 0;
            return;
        }

        // Present-upload staleness (§132 (a*)): a non-suppressed guest
        // write touched a watched presented-surface span since this FBO was
        // last made authoritative — drop the FBO and fall through to the
        // existing upload path below (which rebuilds it at record size from
        // guest RAM). Wrong-serve is the high-severity error, so on any
        // uncertainty the flip uploads; a guard failure below takes the
        // flipEmpty abstain path and keeps the honest gap.
        bool droppedStaleFbo = false;
        if (_frameComposed)
        {
            // The software rasterizer owns this frame (gap 8): its image is
            // in guest RAM, the FBO is a stale partial copy. Drop it so the
            // upload path below rebuilds the presented surface from RAM.
            if (_fbo != 0)
            {
                DeleteFbo();
            }

            droppedStaleFbo = true;
            _frameComposed = false;
        }

        if (_dumpFlipDir is not null && _fbo != 0 && record.ColorBytes == 4 &&
            _dirtyWatch?.PresentDirty == true)
        {
            FlushBatch();
            int dw = (int)Math.Min(record.Width, (uint)_fboWidth);
            int dh = (int)Math.Min(record.Height, (uint)_fboHeight);
            if (dw > 0 && dh > 0)
            {
                byte[] fboPixels = new byte[dw * dh * 4];
                BindFbo(_fbo);
                unsafe
                {
                    fixed (byte* p = fboPixels)
                    {
                        _gl.ReadPixels(0, 0, (uint)dw, (uint)dh, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                    }
                }

                Directory.CreateDirectory(_dumpFlipDir);
                string stem = Path.Combine(_dumpFlipDir, $"flip-{Flips}");
                File.WriteAllBytes(stem + "-fbo.raw", fboPixels);
                File.WriteAllText(stem + ".txt", $"{dw}x{dh}");
            }
        }

        if (_fbo != 0 && _dirtyWatch?.PresentDirty == true)
        {
            DeleteFbo();
            droppedStaleFbo = true;
        }

        if (_fbo == 0)
        {
            // Present-upload slice: either no draw/clear has created the
            // FBO (the presented frame, if any, is a CPU-blitted guest-RAM
            // surface) or the dirty watch just dropped a stale one. Upload
            // the surface wholesale into a fresh FBO (full-frame replace —
            // never mixed with software pixels) and fall through to the
            // readback below. Any failure below restores _fbo == 0 so the
            // session keeps the pre-upload honest gap (no frame served).
            if (PresentUpload.TryBuildUploadPixels(
                    _memory, LastFlipSurfaceAddress, record.Width, record.Height,
                    record.Pitch, record.ColorBytes, out byte[]? upload) &&
                upload is not null &&
                EnsureFbo((int)record.Width, (int)record.Height, 0) &&
                UploadFullFrame(upload, record.Width, record.Height))
            {
                _presentUploads++;
                if (droppedStaleFbo)
                {
                    _flipStaleUploads++;
                }

                if (_dumpFlipDir is not null)
                {
                    Directory.CreateDirectory(_dumpFlipDir);
                    string stem = Path.Combine(_dumpFlipDir, $"flip-{Flips}");
                    File.WriteAllBytes(stem + "-ram.raw", upload);
                    File.WriteAllText(stem + "-ram.txt", $"{record.Width}x{record.Height}");
                }

                // The upload made the FBO authoritative from guest RAM at
                // this instant: clean until a new guest write trips the bit.
                _dirtyWatch?.ClearPresentDirty();
            }
            else
            {
                DeleteFbo();
                _flipEmpty++;
                _readback = null;
                _readbackWidth = 0;
                _readbackHeight = 0;
                return;
            }
        }

        if (_fboWidth < record.Width || _fboHeight < record.Height)
        {
            BackendUnsupported++;
            _unsupportedFlip++;
            _readback = null;
            _readbackWidth = 0;
            _readbackHeight = 0;
            return;
        }

        uint width = record.Width;
        uint height = record.Height;
        if (!FlipReadbackEnabled)
        {
            // Fast-mode present path (Track B): no CPU readback. The frame is
            // never materialized host-side here; consumers that need pixels
            // (--frame, the frontend display copy) opt in and keep the exact
            // pre-Track-B behavior above. The FBO keeps the pixels for the
            // window blit.
            _readback = null;
            _readbackWidth = 0;
            _readbackHeight = 0;
            ArmPresentWatch(in record);
            if (_windowRequested)
            {
                PresentToWindow(width, height);
            }

            return;
        }

        byte[] rgba = new byte[width * height * 4];
        BindFbo(_fbo);
        long readbackStart = Stopwatch.GetTimestamp();
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        _timings.AddReadback(Stopwatch.GetElapsedTime(readbackStart));

        // GL rows come back bottom-up = guest top-down (our y-flip puts
        // guest y=0 at the FBO bottom), so only R/B swap is needed.
        byte[] bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }

        _readback = bgra;
        _readbackWidth = width;
        _readbackHeight = height;

        // Arm the present-upload dirty watch over the presented surface
        // (§132 (a*)): a live FBO is the arm condition; the span is the
        // resolved record's pitch × height. The NEXT flip decides serve vs
        // upload off non-suppressed writes into the watched spans (span
        // slots accumulate so a double-buffered app's surfaces stay
        // watched; a pre-first-flip CPU blit is the empty-FBO upload path).
        ArmPresentWatch(in record);

        if (_windowRequested)
        {
            PresentToWindow(width, height);
        }
    }

    private void ArmPresentWatch(in PgraphSurfaceRecord record)
    {
        if (_fbo != 0 && _dirtyWatch is not null)
        {
            ulong span = (ulong)record.Pitch * record.Height;
            _dirtyWatch.ArmPresentWatch(
                record.SurfaceAddress,
                span > uint.MaxValue ? uint.MaxValue : (uint)span);
        }
    }

    /// <summary>Gets the number of frames presented to the live window. Diagnostics.</summary>
    public long WindowPresents { get; private set; }

    /// <summary>Gets the window-mode status summary. Diagnostics.</summary>
    public string WindowStatus { get; private set; }

    /// <summary>
    /// Presents the just-read-back frame to the visible window (opt-in
    /// <c>--window</c>): shows/sizes the window on the first frame, blits the
    /// FBO into the default framebuffer (NEAREST, scaled to the client area),
    /// swaps, pumps messages and updates the title counters. Any failure
    /// degrades to headless (status recorded, presentation disabled) — the
    /// golden path and the readback are untouched.
    /// </summary>
    private void PresentToWindow(uint width, uint height)
    {
        if (!_windowShown)
        {
            _windowShown = true;
            if (!_context.TryShowWindow(width, height, out string why))
            {
                WindowStatus = $"window unavailable: {why}";
                return;
            }

            WindowStatus = $"window {width}x{height} vsync=off";
        }

        if (_fatalError is not null)
        {
            return;
        }

        uint clientWidth = width;
        uint clientHeight = height;
        if (_context.TryGetClientSize(out uint cw, out uint ch) && cw > 0 && ch > 0)
        {
            clientWidth = cw;
            clientHeight = ch;
        }

        // The FBO holds the guest image with guest y=0 at the GL bottom (the
        // readback depends on it: ReadPixels' bottom-up rows come out
        // guest-top-down and the BMP export is upright). The window's GL
        // bottom must therefore receive the guest's LAST row, so the
        // destination rect is flipped vertically — with a same-origin rect
        // the live window shows every frame upside down.
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        _gl.BlitFramebuffer(
            0, 0, (int)width, (int)height,
            0, (int)clientHeight, (int)clientWidth, 0,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _context.SwapBuffers();
        WindowPresents++;
        _context.PumpMessages();
        _context.SetTitle($"XGrid — flips={Flips} presented={WindowPresents} {width}x{height}");
    }

    /// <summary>Drains window messages between emulation batches (window mode only).</summary>
    public void Pump()
    {
        if (_windowRequested && _windowShown)
        {
            _context.PumpMessages();
        }
    }

    /// <summary>
    /// Full-frame replace of the (fresh, empty) FBO from guest-order RGBA
    /// bytes: a throwaway texture carries the pixels and a NEAREST blit
    /// lands them 1:1 (same-size FBO, same-origin rect — the convention the
    /// content-preserving grow blit already uses). Synchronous, headless;
    /// no partial updates, no async. Returns false when the throwaway
    /// source FBO is incomplete (the caller restores _fbo == 0).
    /// </summary>
    private bool UploadFullFrame(byte[] rgba, uint width, uint height)
    {
        _gl.GenTextures(1, out uint texture);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                    width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        _gl.GenFramebuffers(1, out uint srcFbo);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, srcFbo);
        _gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, texture, 0);
        bool complete = _gl.CheckFramebufferStatus(FramebufferTarget.ReadFramebuffer) ==
                        GLEnum.FramebufferComplete;
        if (complete)
        {
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _fbo);
            _gl.Disable(EnableCap.ScissorTest);
            _gl.BlitFramebuffer(
                0, 0, (int)width, (int)height,
                0, 0, (int)width, (int)height,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }

        BindFbo(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        uint delFbo = srcFbo;
        _gl.DeleteFramebuffers(1, ref delFbo);
        uint delTexture = texture;
        _gl.DeleteTextures(1, ref delTexture);
        InvalidateTextureBinding();
        return complete;
    }

    /// <summary>
    /// Returns true when a valid-stage texture mode is still unsupported by the
    /// GL path. The computed cube/dot modes (DOT_*/DOT_RFLCT_*/DOT_STR_*, and
    /// PROJECT2D/CUBEMAP on a cube-flagged supported format) are served: the
    /// shader transcribes their math and a cube stage samples the host
    /// 2D-array texture the backend decodes from the guest's faces (§236).
    /// BUMPENVMAP is served from its per-stage matrix/luminance uniforms.
    /// A PROJECT3D stage over a **volume** texture is declined: the software
    /// sampler reads a swizzled volume slice and the GL path has no volume
    /// source, so it would silently paint black where the software paints a
    /// texel - the draw goes to software instead (a composed frame, never a
    /// guessed one).
    /// </summary>
    private bool CheckModesUnsupported(in PgraphDrawInfo info, PshCombiner combiner, out bool cubeVolume)
    {
        bool unsupported = false;
        cubeVolume = false;
        for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
        {
            int mode = combiner.DecodeTexMode(slot);
            if (!PshCombiner.IsModeValidForStage(mode, slot))
            {
                continue;
            }

            switch (mode)
            {
                case PshCombiner.TexModeProject3D:
                    if (((info.GetTexture(slot).Format & 0xC0u) >> 6) == 3u)
                    {
                        _unsupportedTexModeCounts[mode & 0x1F]++;
                        cubeVolume = true;
                        unsupported = true;
                    }

                    break;
                case PshCombiner.TexModeBumpEnvMap:
                case PshCombiner.TexModeBumpEnvMapLum:
                case PshCombiner.TexModeDotProduct:
                case PshCombiner.TexModeDotZw:
                case PshCombiner.TexModeDotSt:
                case PshCombiner.TexModeDotRflctDiff:
                case PshCombiner.TexModeDotRflctSpec:
                case PshCombiner.TexModeDotStr3D:
                case PshCombiner.TexModeDotStrCube:
                case PshCombiner.TexModeProject2D:
                case PshCombiner.TexModeCubemap:
                    // Served: the computed cube/dot modes read the decoded cube
                    // array texture and the sampled input texel, and the bump
                    // modes read the per-stage matrix/luminance uniforms. A
                    // draw whose HILO word did not fit the pool is declined by
                    // ApplyDrawState (never sampled from a guess).
                    _ = info;
                    break;
            }
        }

        return unsupported;
    }

    /// <summary>Formats the declined-draw texture-mode histogram ("none" when empty).</summary>
    private string DescribeUnsupportedTexModes()
    {
        var sb = new StringBuilder(64);
        for (int mode = 0; mode < _unsupportedTexModeCounts.Length; mode++)
        {
            long count = _unsupportedTexModeCounts[mode];
            if (count == 0)
            {
                continue;
            }

            if (sb.Length != 0)
            {
                sb.Append(' ');
            }

            sb.Append(CultureInfo.InvariantCulture, $"m{mode:X2}={count}");
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private const uint TextureFormatCubemapEnable = 1u << 2;

    private bool EnsureFbo(int width, int height, uint zetaBytes)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        // Grow-only surface model: the FBO stands for the color/zeta
        // SURFACE (software keeps surface pixels across clip-window
        // changes), so a clip shrink or a flip at an older clip size reads
        // the same window software reads. Only a larger extent or a zeta
        // format change reallocates; the realloc PRESERVES prior content by
        // blitting color (always) and depth/stencil (same zeta format)
        // from the old FBO — software's surfaces live in guest RAM and are
        // never dropped, so losing them here was a fidelity gap (§113
        // FBO-grow risk; exposure measured in diag[fboReallocs /
        // fboZetaDrops], which the golden gates re-validate on every run).
        if (_fbo != 0 && _fboWidth >= width && _fboHeight >= height && _fboZetaBytes == zetaBytes)
        {
            return true;
        }

        uint oldFbo = _fbo;
        uint oldColor = _colorRenderbuffer;
        uint oldDepth = _depthRenderbuffer;
        int oldWidth = _fboWidth;
        int oldHeight = _fboHeight;
        uint oldZeta = _fboZetaBytes;

        _fbo = 0;
        _colorRenderbuffer = 0;
        _depthRenderbuffer = 0;
        _fboWidth = 0;
        _fboHeight = 0;
        _fboZetaBytes = 0;

        _gl.GenFramebuffers(1, out _fbo);
        BindFbo(_fbo);
        _gl.GenRenderbuffers(1, out _colorRenderbuffer);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorRenderbuffer);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _colorRenderbuffer);

        if (zetaBytes == 4)
        {
            _gl.GenRenderbuffers(1, out _depthRenderbuffer);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            _gl.RenderbufferStorage(
                RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(
                FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
        }
        else if (zetaBytes == 2)
        {
            _gl.GenRenderbuffers(1, out _depthRenderbuffer);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            _gl.RenderbufferStorage(
                RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent16, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(
                FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
        }

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            // Keep the previous FBO alive on failure: the caller counts the
            // draw unsupported, but later draws at the old size still work.
            DeleteFbo();
            _fbo = oldFbo;
            _colorRenderbuffer = oldColor;
            _depthRenderbuffer = oldDepth;
            _fboWidth = oldWidth;
            _fboHeight = oldHeight;
            _fboZetaBytes = oldZeta;
            return false;
        }

        if (oldFbo != 0)
        {
            _fboReallocs++;
            if (oldZeta != 0 && oldZeta != zetaBytes)
            {
                _fboZetaDrops++;
            }

            // Content-preserving blit: same-origin rect (both FBOs share the
            // guest-row-0-at-bottom convention), NEAREST, no filtering. Depth
            // only when the zeta format is unchanged (different depth formats
            // are not blit-compatible); a zeta format change drops depth
            // content and is counted so the gap stays visible.
            _gl.Disable(EnableCap.ScissorTest);
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, oldFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _fbo);
            _gl.BlitFramebuffer(
                0, 0, oldWidth, oldHeight,
                0, 0, oldWidth, oldHeight,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            if (oldDepth != 0 && oldZeta == zetaBytes)
            {
                ClearBufferMask mask = zetaBytes == 4
                    ? ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit
                    : ClearBufferMask.DepthBufferBit;
                _gl.BlitFramebuffer(
                    0, 0, oldWidth, oldHeight,
                    0, 0, oldWidth, oldHeight,
                    mask, BlitFramebufferFilter.Nearest);
            }

            BindFbo(0);
            uint delFbo = oldFbo;
            _gl.DeleteFramebuffers(1, ref delFbo);
            if (oldColor != 0)
            {
                uint del = oldColor;
                _gl.DeleteRenderbuffers(1, ref del);
            }

            if (oldDepth != 0)
            {
                uint del = oldDepth;
                _gl.DeleteRenderbuffers(1, ref del);
            }
        }

        _fboWidth = width;
        _fboHeight = height;
        _fboZetaBytes = zetaBytes;
        return true;
    }

    private void DeleteFbo()
    {
        if (_fbo != 0)
        {
            uint fbo = _fbo;
            _gl.DeleteFramebuffers(1, ref fbo);
            _fbo = 0;
            _boundFbo = 0;
        }

        if (_colorRenderbuffer != 0)
        {
            uint rb = _colorRenderbuffer;
            _gl.DeleteRenderbuffers(1, ref rb);
            _colorRenderbuffer = 0;
        }

        if (_depthRenderbuffer != 0)
        {
            uint rb = _depthRenderbuffer;
            _gl.DeleteRenderbuffers(1, ref rb);
            _depthRenderbuffer = 0;
        }

        _fboWidth = 0;
        _fboHeight = 0;
        _fboZetaBytes = 0;
    }

    private uint CompileVertexShader(int triangleCapacity)
    {
        // Positions come from the packed per-draw uniform block, selected by
        // gl_VertexID; the instance index selects the block of a batched draw.
        // The draw needs no per-draw vertex-buffer upload (Track B
        // present-path measurement: BufferData was one of the three remaining
        // GL calls per draw). The NDC mapping and the centroid inflate mirror
        // the previous C# StoreVertex math operation-for-operation; the inflate
        // only widens GL coverage, and the fragment shader's software-exact
        // inside-test decides every pixel.
        string source =
            "#version 330 core\n" +
            string.Create(CultureInfo.InvariantCulture, $"uniform vec4 uTri[{triangleCapacity * 25}];\n") +
            "flat out int vTri;\n" +
            "#define T(i) uTri[((gl_InstanceID) * 25) + (i)]\n" +
            "void main() {\n" +
            "  vTri = gl_InstanceID;\n" +
            "  vec2 vA = T(0).xy;\n" +
            "  vec2 vB = T(0).zw;\n" +
            "  vec2 vC = T(1).xy;\n" +
            "  vec2 clip = vec2(T(2).w, T(2).z);\n" +
            "  vec2 size = vec2(T(24).x, T(24).y);\n" +
            "  vec2 ndcA = ((2.0 * (vA - clip)) / size) - 1.0;\n" +
            "  vec2 ndcB = ((2.0 * (vB - clip)) / size) - 1.0;\n" +
            "  vec2 ndcC = ((2.0 * (vC - clip)) / size) - 1.0;\n" +
            "  vec2 ndc = (gl_VertexID == 0) ? ndcA : ((gl_VertexID == 1) ? ndcB : ndcC);\n" +
            "  vec2 center = (ndcA + ndcB + ndcC) / 3.0;\n" +
            "  gl_Position = vec4(center + ((ndc - center) * 1.02), 0.0, 1.0);\n" +
            "}\n";
        uint shader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException("vertex shader compile: " + log);
        }

        return shader;
    }

    private GlProgram EnsureProgram(bool legacy, PshCombiner? combiner)
    {
        // Fast path: the last draw's structure (draws arrive in runs, so the
        // 37-word compare replaces a per-draw key-string allocation).
        if (_lastProgram is not null && _lastLegacy == legacy &&
            (legacy || StructureMatches(combiner!)))
        {
            return _lastProgram;
        }

        string key = PshGlslGenerator.BuildKey(legacy, combiner!);
        if (_programs.TryGetValue(key, out GlProgram? existing))
        {
            RememberProgram(legacy, combiner, existing);
            return existing;
        }

        string source = PshGlslGenerator.BuildFragmentSource(legacy, combiner!, _batchCapacity);
        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, source);
        _gl.CompileShader(fragmentShader);
        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(fragmentShader).Replace('\n', ' ');
            _gl.DeleteShader(fragmentShader);
            throw new InvalidOperationException("fragment shader compile: " + log);
        }

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, _vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);
        _gl.DeleteShader(fragmentShader);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(program).Replace('\n', ' ');
            _gl.DeleteProgram(program);
            throw new InvalidOperationException("fragment program link: " + log);
        }

        var glProgram = new GlProgram(program);

        foreach (string name in UniformNames)
        {
            int location = _gl.GetUniformLocation(program, name);
            glProgram.Locations[name] = location;
        }

        glProgram.TriLocation = glProgram.Location("uTri[0]");

        _gl.UseProgram(program);
        _currentProgram = program;
        _gl.Uniform1(glProgram.Location("uTex0"), 0);
        _gl.Uniform1(glProgram.Location("uTex1"), 1);
        _gl.Uniform1(glProgram.Location("uTex2"), 2);
        _gl.Uniform1(glProgram.Location("uTex3"), 3);
        // Cube faces ride their own 2D-array samplers on units 4-7 so a stage
        // can be sampled as a cube and as a 2D image in the same program.
        _gl.Uniform1(glProgram.Location("uCube0"), 4);
        _gl.Uniform1(glProgram.Location("uCube1"), 5);
        _gl.Uniform1(glProgram.Location("uCube2"), 6);
        _gl.Uniform1(glProgram.Location("uCube3"), 7);
        _gl.UseProgram(0);
        _currentProgram = 0;
        _programs.Add(key, glProgram);
        ProgramsCompiled++;
        RememberProgram(legacy, combiner, glProgram);
        return glProgram;
    }

    private void RememberProgram(bool legacy, PshCombiner? combiner, GlProgram program)
    {
        _lastProgram = program;
        _lastLegacy = legacy;
        _lastStructureValid = !legacy;
        if (legacy || combiner is null)
        {
            return;
        }

        int w = 0;
        _lastStructure[w++] = combiner.Control;
        _lastStructure[w++] = combiner.StageProgram;
        _lastStructure[w++] = combiner.OtherStageInput;
        _lastStructure[w++] = combiner.Final0;
        _lastStructure[w++] = combiner.Final1;
        for (int i = 0; i < PshCombiner.MaxStages; i++)
        {
            _lastStructure[w++] = combiner.RgbInputs[i];
            _lastStructure[w++] = combiner.AlphaInputs[i];
            _lastStructure[w++] = combiner.RgbOutputs[i];
            _lastStructure[w++] = combiner.AlphaOutputs[i];
        }
    }

    private bool StructureMatches(PshCombiner combiner)
    {
        if (!_lastStructureValid)
        {
            return false;
        }

        int w = 0;
        if (_lastStructure[w++] != combiner.Control ||
            _lastStructure[w++] != combiner.StageProgram ||
            _lastStructure[w++] != combiner.OtherStageInput ||
            _lastStructure[w++] != combiner.Final0 ||
            _lastStructure[w++] != combiner.Final1)
        {
            return false;
        }

        for (int i = 0; i < PshCombiner.MaxStages; i++)
        {
            if (_lastStructure[w++] != combiner.RgbInputs[i] ||
                _lastStructure[w++] != combiner.AlphaInputs[i] ||
                _lastStructure[w++] != combiner.RgbOutputs[i] ||
                _lastStructure[w++] != combiner.AlphaOutputs[i])
            {
                return false;
            }
        }

        return true;
    }

    private bool ApplyTextures(GlProgram program, in PgraphDrawInfo info, PshCombiner? combiner, bool legacy,
        bool clampStages)
    {
        _ = program;
        _ = legacy;
        // The specular reflection dot mode reads the eye position; it stays
        // uniform state so the program key never splits on it.
        _eyePos[0] = info.EyeX;
        _eyePos[1] = info.EyeY;
        _eyePos[2] = info.EyeZ;
        _bump = info.Bump;
        // B3 computed modes: the shader resolves the swizzled cube/HILO guest
        // word through the carried set's constant pool. Slots the draw has no
        // pool entry for are clamped to pool 0 so a pool index can never read
        // past the buffer; clampStages says that is happening, and the pool
        // resolution below then declines the draw (nothing reaches the shader
        // from a clamped index).


        _texPoolClamped = false;
        for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
        {
            PgraphTextureSlot texture = info.GetTexture(slot);
            uint color = (texture.Format >> 8) & 0x7Fu;
            if (slot >= 1 && combiner is not null)
            {
                // BUMPENVMAP's per-stage matrix and luminance pair (the values
                // Nv2AMmio.ResolveBump applies), always uploaded so a mode
                // switch never has to recompile the program.
                // The bump matrix rows and the luminance pair for this stage,
                // read from the seam's copy of the software values.
                _bumpMatrix[((slot - 1) * 4) + 0] = _bump.Matrix[(slot - 1) * 4 + 0];
                _bumpMatrix[((slot - 1) * 4) + 1] = _bump.Matrix[(slot - 1) * 4 + 1];
                _bumpMatrix[((slot - 1) * 4) + 2] = _bump.Matrix[(slot - 1) * 4 + 2];
                _bumpMatrix[((slot - 1) * 4) + 3] = _bump.Matrix[(slot - 1) * 4 + 3];
                _bumpLumeScale[slot - 1] = _bump.LuminanceScale[slot - 1];
                _bumpLumeOffset[slot - 1] = _bump.LuminanceOffset[slot - 1];
            }
            bool linear = Nv2AMmio.IsLinearTextureFormat(color);
            uint bytes = Nv2AMmio.TextureFormatBytesPerTexel(color);
            bool enabled = (texture.Control0 & (1u << 30)) != 0;
            int width;
            int height;
            int pitch;
            if (linear)
            {
                width = (int)(texture.ImageRect >> 16);
                height = (int)(texture.ImageRect & 0xFFFFu);
                pitch = (int)(texture.Control1 >> 16);
                if (pitch <= 0 && width > 0)
                {
                    pitch = width * (int)bytes;
                }
            }
            else
            {
                width = 1 << (int)((texture.Format >> 20) & 0xFu);
                height = 1 << (int)((texture.Format >> 24) & 0xFu);
                pitch = width * (int)bytes;
            }

            bool sizeOk = width > 0 && height > 0 && (!linear || pitch > 0);
            bool sampleOk = enabled && bytes != 0 && sizeOk;
            _texOk[slot] = sampleOk ? 1 : 0;
            _texSize[(slot * 2) + 0] = width;
            _texSize[(slot * 2) + 1] = height;
            _texLinear[slot] = linear ? 1 : 0;
            // The seam already decoded the address word: 0 wrap, 1 mirror,
            // 2 clamp, 3 border. Reading the raw word here swapped wrap and
            // mirror (raw 1 = wrap, raw 2 = mirror) and had no border mode.
            _texWrapX[slot] = texture.SamplerWrapU;
            _texWrapY[slot] = texture.SamplerWrapV;
            _texBilinear[slot] = texture.FilterBilinear ? 1 : 0;
            _texConvolution[slot] = texture.FilterConvolution;
            _texCubeW[slot] = texture.CubeWidth;
            _texCubeH[slot] = texture.CubeHeight;
            ResolveGuestWordPool(in info, slot, combiner, clampStages);
            _dotMap[slot] = combiner is null ? 0 : combiner.DecodeDotMap(slot);
            Nv2AMmio.DecodeBorderColor(
                texture.BorderColor,
                out _texBorder[(slot * 4) + 0], out _texBorder[(slot * 4) + 1],
                out _texBorder[(slot * 4) + 2], out _texBorder[(slot * 4) + 3]);

            _texCube[slot] = (texture.Format & TextureFormatCubemapEnable) != 0 ? 1 : 0;
            _texDim[slot] = (int)((texture.Format & 0xC0u) >> 6);
            // A cube stage samples a host 2D-array texture, so it needs a
            // sampleable extent and the enable bit - never a word pool. The
            // software's gate is TrySampleCubeDirection (supported swizzled
            // format) + TrySampleTextureCube (enable, extent, bytes), both
            // already folded into CubeSamplingReady/bytes here.
            bool cubeOk = texture.CubeSamplingReady && enabled && bytes != 0 &&
                          (texture.Format & TextureFormatCubemapEnable) != 0;
            _texCubeOk[slot] = cubeOk ? 1 : 0;
            if (!sampleOk)
            {
                _lastTextureValid[slot] = false;
                continue;
            }

            // Content identity (XFP4): the reference hashes the whole texture
            // range but only when the NV2A dirty bitmap says the range may have
            // moved. The page-generation stamp is that dirty test: an unchanged
            // stamp reuses the binding without touching the bytes; a moved stamp
            // re-hashes the whole range and re-uploads only when it differs.
            int levels = Math.Max(1, (int)((texture.Format >> 16) & 0xFu));
            ulong chain = linear
                ? (ulong)Math.Max(height * pitch, width * (int)Math.Max(bytes, 1u))
                : (ulong)TextureFormats.ChainLength(color, width, height, levels);
            uint textureLength = (uint)Math.Min(chain, uint.MaxValue);
            if (_ramBytes is not null && texture.Offset < _ramSize)
            {
                textureLength = Math.Min(textureLength, _ramSize - texture.Offset);
            }

            if (textureLength == 0)
            {
                textureLength = 1;
            }

            bool linearFlag = linear;
            var key = new TextureKey(in texture, color, width, height, pitch, linearFlag);
            ulong stamp = _writeStamp is null ? 0UL : _writeStamp.GetWriteStamp(texture.Offset, textureLength);
            GlTextureEntry? entry;
            bool created = false;
            if (_lastTextureValid[slot] && _lastTextureKey[slot].Equals(key))
            {
                entry = _lastTextureEntry[slot]!;
            }
            else if (!_textures.TryGetValue(key, out entry))
            {
                entry = new GlTextureEntry(0, width, height);
                BuildSampleAddresses(entry, in texture, linear, bytes, width, height, pitch);
                entry.Texture = UploadTexture(in texture, color, linear, bytes, width, height, pitch);
                entry.DataHash = HashTextureRange(texture.Offset, textureLength);
                entry.DataStamp = stamp;
                _textures.Add(key, entry);
                created = true;
            }

            bool contentChanged = created;
            if (!created && entry.DataStamp != stamp)
            {
                // The range may have changed: hash it and re-upload into the
                // SAME entry only when the bytes actually differ (the menu's
                // atlas reuse), dropping the old id so later draws bind the
                // fresh texture, not a leaked one.
                uint hash = HashTextureRange(texture.Offset, textureLength);
                if (hash != entry.DataHash)
                {
                    uint oldId = entry.Texture;
                    entry.Texture = UploadTexture(in texture, color, linear, bytes, width, height, pitch);
                    _gl.DeleteTextures(1, ref oldId);
                    InvalidateTextureBinding();
                    entry.DataHash = hash;
                    contentChanged = true;
                }

                entry.DataStamp = stamp;
            }

            if (cubeOk)
            {
                // Same change discipline as the 2D upload: decode the six
                // swizzled faces once and re-decode only when the guest
                // content moved.
                if (entry.CubeTexture == 0 || contentChanged)
                {
                    uint oldCube = entry.CubeTexture;
                    entry.CubeTexture = UploadCubeTexture(in texture, color, bytes);
                    if (oldCube != 0)
                    {
                        _gl.DeleteTextures(1, ref oldCube);
                    }

                    InvalidateTextureBinding();
                }
            }

            _lastTextureKey[slot] = key;
            _lastTextureEntry[slot] = entry;
            _lastTextureValid[slot] = true;

            // Binding shadow: consecutive draws usually bind the same texture
            // on the same unit, and each redundant ActiveTexture/BindTexture
            // pair is driver validation work on the 2.76M-draw menu stream.
            if (_activeTextureUnit != slot)
            {
                _gl.ActiveTexture((TextureUnit)(0x84C0u + (uint)slot));
                _activeTextureUnit = slot;
            }

            if (_boundTexture[slot] != entry.Texture)
            {
                _gl.BindTexture(TextureTarget.Texture2D, entry.Texture);
                _boundTexture[slot] = entry.Texture;
            }

            if (cubeOk)
            {
                int unit = 4 + slot;
                if (_activeTextureUnit != unit)
                {
                    _gl.ActiveTexture((TextureUnit)(0x84C0u + (uint)unit));
                    _activeTextureUnit = unit;
                }

                if (_boundCubeTexture[slot] != entry.CubeTexture)
                {
                    _gl.BindTexture(TextureTarget.Texture2DArray, entry.CubeTexture);
                    _boundCubeTexture[slot] = entry.CubeTexture;
                }
            }
        }

        return !_texPoolClamped;
    }

    /// <summary>
    /// How many guest texel words one draw's computed HILO dot pool can carry.
    /// The fragment shader's guest-word array is sized once per program from
    /// <see cref="PshGlslGenerator.GuestWordPoolSize"/> (the one source), so a
    /// draw whose modes need more words than fit is declined (never sampled
    /// wrong). Cube faces do not use the pool any more - they ride a host
    /// 2D-array texture - so this only ever holds a 16-bit HILO input word.
    /// </summary>
    internal const int GuestWordPoolSize = PshGlslGenerator.GuestWordPoolSize;

    private readonly uint[] _guestWordValues = new uint[GuestWordPoolSize * 4];
    private int _guestWordCount;
    private bool _texPoolClamped;
    private readonly uint[] _guestWordScratch = new uint[GuestWordPoolSize * 4];
    private readonly int[] _texPoolBase = new int[4];

    /// <summary>Gets how many draws were declined because a computed mode needed a guest word outside the carried set.</summary>
    public long GuestWordMisses { get; private set; }

    /// <summary>Gets the number of draws whose computed cube/dot texture modes resolved guest words from the seam's set.</summary>
    public long GuestWordDraws { get; private set; }

    /// <summary>
    /// Resolves the guest texel words one stage's HILO dot mode samples into
    /// this draw's pool and returns the stage's pool base. The shader reads
    /// pool entry k of slot s at <c>uGuestWord[((uTexPoolBase[s] + k) * 4) + c]</c>.
    /// Marks the draw's stages clamped (the caller declines it) when the words
    /// do not fit the pool.
    /// </summary>
    /// <remarks>
    /// Cube faces are NOT resolved here: a face is a host 2D-array texture
    /// (see <see cref="UploadCubeTexture"/>), so every texel of every face is
    /// sampleable instead of the handful a word window could carry. The pool
    /// therefore only ever holds a 16-bit HILO input word, which is raw bytes
    /// and has no decoded-colour form.
    /// </remarks>
    private void ResolveGuestWordPool(in PgraphDrawInfo info, int slot,
        PshCombiner? combiner, bool clampStages)
    {
        if (slot == 0)
        {
            _guestWordCount = 0;
            _texPoolClamped = false;
        }

        _texPoolBase[slot] = 0;
        if (combiner is null)
        {
            return;
        }

        if (clampStages)
        {
            int already = combiner.DecodeTexMode(slot);
            if (PshCombiner.IsModeValidForStage(already, slot) && IsHiloDotMode(already))
            {
                // A later triangle of the same draw command sampled inside a
                // texture-bound program: its computed modes need words this
                // draw's pool does not carry.
                _texPoolClamped = true;
            }

            return;
        }

        int mode = combiner.DecodeTexMode(slot);
        if (!PshCombiner.IsModeValidForStage(mode, slot) || mode != PshCombiner.TexModeDotProduct)
        {
            return;
        }

        // Only the HILO dot maps (4-7) consume the input's raw 16-bit pair.
        // The colour maps (1-3, 8-15) read the input stage's *sampled texel*,
        // which the sampler already produced, so a DOT_PRODUCT stage over a
        // colour map needs no guest word at all - requiring one declined every
        // eight-stage dot program of the corpus row and painted the frame in
        // software.
        if (combiner.DecodeDotMap(slot) is < 4 or > 7)
        {
            return;
        }

        // An absent carried set is an empty one: the words the guest cannot
        // re-sample are read through the direct RAM lane (the software
        // sampler touched them when it ran this draw command), so a draw is
        // declined only when the pool cannot hold them.
        PgraphGuestTexelSet carried = info.GuestTexels ?? default;
        int start = _guestWordCount;
        if (!AppendDependentInputWords(in carried, slot, combiner))
        {
            _guestWordCount = start;
            _texPoolClamped = true;
            GuestWordMisses++;
            return;
        }

        _texPoolBase[slot] = start;
        GuestWordDraws++;
    }

    private static bool IsHiloDotMode(int mode)
    {
        return mode == PshCombiner.TexModeDotProduct;
    }

    /// <summary>
    /// Appends the dependent input words of a HILO dot stage: the 16-bit pair
    /// the dot map consumes is the input stage's raw guest word, which no
    /// decoded texel carries. The input stage's uploaded entry has the sample
    /// addresses the fingerprint walks, so those words (and their aligned
    /// neighbours) are carried.
    /// </summary>
    private bool AppendDependentInputWords(in PgraphGuestTexelSet carried, int slot, PshCombiner combiner)
    {
        int input = combiner.DecodeInputTex(slot);
        if (input < 0 || input >= PshCombiner.TextureSlots || input == slot)
        {
            return false;
        }

        GlTextureEntry? entry = _lastTextureValid[input] ? _lastTextureEntry[input] : null;
        if (entry?.SampleAddresses is null)
        {
            return false;
        }

        uint[] addresses = entry.SampleAddresses;
        for (int i = 0; i < addresses.Length; i++)
        {
            for (uint offset = 0; offset <= 4u; offset += 4u)
            {
                uint address = addresses[i] + offset;
                if (_guestWordCount >= GuestWordPoolSize)
                {
                    return false;
                }

                uint word;
                if (!FindCarriedWord(in carried, address, out word) && !TryReadDirectWord(address, out word))
                {
                    return false;
                }

                PushGuestWord(word);
            }
        }

        return true;
    }

    /// <summary>
    /// Reads one guest word through the direct RAM lane (the same lane the
    /// texture fingerprint uses), falling back to the memory adapter when the
    /// address is not plain RAM.
    /// </summary>
    private bool TryReadDirectWord(uint address, out uint word)
    {
        word = ReadGuestWord(address);
        return true;
    }

    private static bool FindCarriedWord(in PgraphGuestTexelSet carried, uint address, out uint word)
    {
        ArraySegment<uint> addresses = carried.Addresses;
        for (int i = 0; i < addresses.Count; i++)
        {
            if (addresses[i] == address)
            {
                word = carried.Words[i];
                return true;
            }
        }

        word = 0u;
        return false;
    }

    private void PushGuestWord(uint word)
    {
        int at = _guestWordCount * 4;
        _guestWordValues[at + 0] = word;
        _guestWordValues[at + 1] = 0u;
        _guestWordValues[at + 2] = 0u;
        _guestWordValues[at + 3] = 0u;
        _guestWordCount++;
    }

    /// <summary>Binds a framebuffer through the binding shadow (the default framebuffer is 0 at context creation).</summary>
    private void BindFbo(uint fbo)
    {
        if (_boundFbo != fbo)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            _boundFbo = fbo;
        }
    }

    /// <summary>Drops the texture-unit binding shadow (any out-of-band Gen/Delete can reuse ids).</summary>
    private void InvalidateTextureBinding()
    {
        _activeTextureUnit = -1;
        Array.Clear(_boundTexture);
        Array.Clear(_boundCubeTexture);
        Array.Clear(_lastTextureValid);
    }

    /// <summary>
    /// Hashes a texture's whole guest range (FNV-1a), the reference's
    /// <c>fast_hash(texture_data, length)</c>: the content identity behind a
    /// cache entry, recomputed only when the range's write stamp moved. Uses
    /// the direct RAM view when the range is RAM and the adapter otherwise
    /// (the adapter stays the authority for non-RAM mappings).
    /// </summary>
    /// <summary>Reads one 32-bit guest word through the direct RAM lane when the address is RAM (the adapter stays the authority for everything else).</summary>
    private uint ReadGuestWord(uint address)
    {
        byte[]? ram = _ramBytes;
        if (ram is not null && address <= _ramSize - 4u)
        {
            _probeFpRam++;
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                ram.AsSpan((int)address, 4));
        }

        _probeFpAdapter++;
        return _memory.ReadPhysical(address);
    }

    private long _probeFpRam;
    private long _probeFpAdapter;

    /// <summary>
    /// Resolves a 16-texel spread grid of guest addresses once per texture
    /// cache key (the HILO dot word pool walks it; the grid depends only on
    /// offset/layout/extents).
    /// </summary>
    private static void BuildSampleAddresses(GlTextureEntry entry, in PgraphTextureSlot slot,
        bool linear, uint bytes, int width, int height, int pitch)
    {
        uint[] addresses = new uint[16];
        for (int i = 0; i < 16; i++)
        {
            int x = (int)((ulong)((i * 97) + 13) % (uint)width);
            int y = (int)((ulong)((i * 389) + 7) % (uint)height);
            addresses[i] = TexelAddress(in slot, linear, bytes, width, height, pitch, x, y);
        }

        entry.SampleAddresses = addresses;
    }

    /// <summary>
    /// Hashes a texture's whole guest range (FNV-1a), the reference's
    /// <c>fast_hash(texture_data, length)</c>: the content identity behind a
    /// cache entry, recomputed only when the range's write stamp moved. Uses
    /// the direct RAM view when the range is RAM and the adapter otherwise.
    /// </summary>
    private uint HashTextureRange(uint offset, uint length)
    {
        uint hash = 2166136261u;
        byte[]? ram = _ramBytes;
        if (ram is not null && offset < _ramSize)
        {
            int count = (int)Math.Min(length, _ramSize - offset);
            _probeFpRam += count / 4;
            ReadOnlySpan<byte> span = ram.AsSpan((int)offset, count);
            for (int i = 0; i < span.Length; i++)
            {
                hash = (hash ^ span[i]) * 16777619u;
            }

            return hash;
        }

        for (uint at = 0; at < length; at += 4)
        {
            hash = (hash ^ _memory.ReadPhysical(offset + at)) * 16777619u;
            _probeFpAdapter++;
        }

        return hash;
    }
    private static uint TexelAddress(
        in PgraphTextureSlot slot, bool linear, uint bytes, int width, int height, int pitch, int x, int y)
    {
        return linear
            ? slot.Offset + (uint)(y * pitch) + (uint)(x * (int)bytes)
            : slot.Offset + (Nv2AMmio.SwizzleOffset(x, y, width, height) * bytes);
    }

    private uint UploadTexture(
        in PgraphTextureSlot slot, uint color, bool linear, uint bytes,
        int width, int height, int pitch)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint address = TexelAddress(in slot, linear, bytes, width, height, pitch, x, y);
                uint texel = bytes == 2
                    ? _memory.ReadPhysical(address) & 0xFFFFu
                    : _memory.ReadPhysical(address);
                Nv2AMmio.DecodeTextureTexel(color, texel, out float r, out float g, out float b, out float a);
                int at = ((y * width) + x) * 4;
                rgba[at] = (byte)((Math.Clamp(r, 0f, 1f) * 255f) + 0.5f);
                rgba[at + 1] = (byte)((Math.Clamp(g, 0f, 1f) * 255f) + 0.5f);
                rgba[at + 2] = (byte)((Math.Clamp(b, 0f, 1f) * 255f) + 0.5f);
                rgba[at + 3] = (byte)((Math.Clamp(a, 0f, 1f) * 255f) + 0.5f);
            }
        }

        _gl.GenTextures(1, out uint texture);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                    (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        TextureUploads++;
        return texture;
    }

    /// <summary>
    /// Decodes one cube stage into a GL 2D-array texture: layer = face, texel
    /// (x, y) = the swizzled guest word at the address the software sampler
    /// reads for that face texel (<see cref="Nv2AMmio.SwizzleAddressNT"/> +
    /// <see cref="Nv2AMmio.DecodeSwizzledTexelWord"/> - the same two calls
    /// <c>Nv2AMmio.TryReadCubeTexel</c> makes), so the shader's cube tap and
    /// the software tap are one decode. A word pool could only carry a window
    /// of a face; this carries every texel of all six, which is what a 256x256
    /// reflection cube needs.
    /// </summary>
    private uint UploadCubeTexture(in PgraphTextureSlot slot, uint color, uint bytes)
    {
        int width = slot.CubeWidth;
        int height = slot.CubeHeight;
        const int Faces = 6;
        byte[] rgba = new byte[width * height * Faces * 4];
        for (int face = 0; face < Faces; face++)
        {
            uint faceBase = slot.Offset + (slot.CubeFaceStride * (uint)face);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint texelIndex = Nv2AMmio.SwizzleAddressNT(
                        x, y, slot.SwizzleMaskX, slot.SwizzleMaskY, slot.MortonMasks);
                    uint address = faceBase + (texelIndex * bytes);
                    uint word = _memory.ReadPhysical(address);
                    int at = ((((face * height) + y) * width) + x) * 4;
                    if (!Nv2AMmio.DecodeSwizzledTexelWord(color, bytes, word, out float r, out float g,
                            out float b, out float a))
                    {
                        continue;
                    }

                    rgba[at] = (byte)((Math.Clamp(r, 0f, 1f) * 255f) + 0.5f);
                    rgba[at + 1] = (byte)((Math.Clamp(g, 0f, 1f) * 255f) + 0.5f);
                    rgba[at + 2] = (byte)((Math.Clamp(b, 0f, 1f) * 255f) + 0.5f);
                    rgba[at + 3] = (byte)((Math.Clamp(a, 0f, 1f) * 255f) + 0.5f);
                }
            }
        }

        _gl.GenTextures(1, out uint texture);
        _gl.BindTexture(TextureTarget.Texture2DArray, texture);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        unsafe
        {
            fixed (byte* pRgba = rgba)
            {
                _gl.TexImage3D(
                    TextureTarget.Texture2DArray, 0, (int)InternalFormat.Rgba8,
                    (uint)width, (uint)height, Faces, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pRgba);
            }
        }

        TextureUploads++;
        return texture;
    }

    private void ApplyState(in PgraphDrawInfo info)
    {
        PgraphFragmentState fragment = info.Fragment;
        GlStateMapper.MapBlendColor(fragment.BlendColor, out float br, out float bg, out float bb, out float ba);
        GlStateMapper.MapColorMask(
            fragment.ColorMask, out bool maskR, out bool maskG, out bool maskB, out bool maskA);
        uint colorMask = (maskR ? 1u : 0u) | (maskG ? 2u : 0u) | (maskB ? 4u : 0u) | (maskA ? 8u : 0u);
        uint blendSrc = fragment.BlendEnable ? fragment.BlendSrcFactor : 0u;
        uint blendDst = fragment.BlendEnable ? fragment.BlendDstFactor : 0u;
        uint blendEquation = fragment.BlendEnable ? fragment.BlendEquation : 0u;
        bool depthEnabled = fragment.DepthTestEnable && info.ZetaBytes != 0;
        uint depthFunc = depthEnabled ? fragment.DepthFunc : 0u;
        bool depthMask = depthEnabled && fragment.DepthMask;
        bool stencilEnabled = fragment.StencilTestEnable && info.ZetaBytes == 4;
        uint stencilFunc = stencilEnabled ? fragment.StencilFunc : 0u;
        uint stencilRef = stencilEnabled ? fragment.StencilRef & 0xFFu : 0u;
        uint stencilReadMask = stencilEnabled ? fragment.StencilReadMask & 0xFFu : 0u;
        uint stencilOpFail = stencilEnabled ? fragment.StencilOpFail : 0u;
        uint stencilOpZfail = stencilEnabled ? fragment.StencilOpZfail : 0u;
        uint stencilOpZpass = stencilEnabled ? fragment.StencilOpZpass : 0u;
        uint stencilWriteMask = stencilEnabled ? fragment.StencilWriteMask & 0xFFu : 0u;

        if (!_stateValid ||
            _glBlendEnabled != fragment.BlendEnable ||
            _glBlendR != br || _glBlendG != bg || _glBlendB != bb || _glBlendA != ba ||
            _glBlendSrc != blendSrc || _glBlendDst != blendDst || _glBlendEquation != blendEquation)
        {
            if (fragment.BlendEnable)
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendColor(br, bg, bb, ba);
                _gl.BlendFunc(
                    (GLEnum)GlStateMapper.MapBlendFactor(fragment.BlendSrcFactor),
                    (GLEnum)GlStateMapper.MapBlendFactor(fragment.BlendDstFactor));
                _gl.BlendEquation((GLEnum)GlStateMapper.MapBlendEquation(fragment.BlendEquation));
            }
            else
            {
                _gl.Disable(EnableCap.Blend);
            }

            _glBlendEnabled = fragment.BlendEnable;
            _glBlendR = br;
            _glBlendG = bg;
            _glBlendB = bb;
            _glBlendA = ba;
            _glBlendSrc = blendSrc;
            _glBlendDst = blendDst;
            _glBlendEquation = blendEquation;
        }

        if (!_stateValid || _glColorMask != colorMask)
        {
            _gl.ColorMask(maskR, maskG, maskB, maskA);
            _glColorMask = colorMask;
        }

        if (!_stateValid || _glDepthEnabled != depthEnabled || _glDepthFunc != depthFunc || _glDepthMask != depthMask)
        {
            if (depthEnabled)
            {
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc((GLEnum)GlStateMapper.MapCompareFunc(fragment.DepthFunc));
                _gl.DepthMask(fragment.DepthMask);
            }
            else
            {
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(false);
            }

            _glDepthEnabled = depthEnabled;
            _glDepthFunc = depthFunc;
            _glDepthMask = depthMask;
        }

        if (!_stateValid ||
            _glStencilEnabled != stencilEnabled || _glStencilFunc != stencilFunc ||
            _glStencilRef != stencilRef || _glStencilReadMask != stencilReadMask ||
            _glStencilOpFail != stencilOpFail || _glStencilOpZfail != stencilOpZfail ||
            _glStencilOpZpass != stencilOpZpass || _glStencilWriteMask != stencilWriteMask)
        {
            if (stencilEnabled)
            {
                _gl.Enable(EnableCap.StencilTest);
                _gl.StencilFunc(
                    (GLEnum)GlStateMapper.MapCompareFunc(fragment.StencilFunc),
                    (int)(fragment.StencilRef & 0xFFu),
                    fragment.StencilReadMask & 0xFFu);
                _gl.StencilOp(
                    (GLEnum)GlStateMapper.MapStencilOp(fragment.StencilOpFail),
                    (GLEnum)GlStateMapper.MapStencilOp(fragment.StencilOpZfail),
                    (GLEnum)GlStateMapper.MapStencilOp(fragment.StencilOpZpass));
                _gl.StencilMask(fragment.StencilWriteMask & 0xFFu);
            }
            else
            {
                _gl.Disable(EnableCap.StencilTest);
            }

            _glStencilEnabled = stencilEnabled;
            _glStencilFunc = stencilFunc;
            _glStencilRef = stencilRef;
            _glStencilReadMask = stencilReadMask;
            _glStencilOpFail = stencilOpFail;
            _glStencilOpZfail = stencilOpZfail;
            _glStencilOpZpass = stencilOpZpass;
            _glStencilWriteMask = stencilWriteMask;
        }

        _stateValid = true;
    }

    /// <summary>Cheap combiner-constant change stamp: the 18 raw registers <see cref="PshCombiner.FillConsts"/> reads.</summary>
    private static bool ConstRegistersMatch(GlProgram program, PshCombiner combiner)
    {
        uint[] stamp = program.ConstStamp;
        for (int i = 0; i < 8; i++)
        {
            if (stamp[i] != combiner.Factor0[i] || stamp[8 + i] != combiner.Factor1[i])
            {
                return false;
            }
        }

        return stamp[16] == combiner.SpecFogFactor[0] && stamp[17] == combiner.SpecFogFactor[1];
    }

    private static bool SameValues(float[] a, float[] b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyUniforms(GlProgram program, in PgraphDrawInfo info, PshCombiner? combiner)
    {
        PgraphFragmentState fragment = info.Fragment;
        if (!info.CombinerActive || combiner is null)
        {
            // Legacy path still needs the shared preamble uniforms.
            SetScalarUniforms(program, in info, in fragment);
            SetTextureUniforms(program);
            return;
        }

        if (!program.ConstValid || !ConstRegistersMatch(program, combiner))
        {
            PshCombiner.FillConsts(combiner, _constFlat);
            _gl.Uniform4(program.Location("uConsts[0]"), 18u, ref _constFlat[0]);
            Array.Copy(_constFlat, program.ConstCache, 72);
            Array.Copy(combiner.Factor0, 0, program.ConstStamp, 0, 8);
            Array.Copy(combiner.Factor1, 0, program.ConstStamp, 8, 8);
            program.ConstStamp[16] = combiner.SpecFogFactor[0];
            program.ConstStamp[17] = combiner.SpecFogFactor[1];
            program.ConstValid = true;
        }

        SetScalarUniforms(program, in info, in fragment);
        SetTextureUniforms(program);
    }

    private void SetScalarUniforms(GlProgram program, in PgraphDrawInfo info, in PgraphFragmentState fragment)
    {
        float[] values = _scalarScratch;
        values[0] = info.FogR;
        values[1] = info.FogG;
        values[2] = info.FogB;
        values[3] = info.FogA;
        values[4] = info.FogEnable ? 1 : 0;
        values[5] = fragment.AlphaTestEnable ? 1 : 0;
        values[6] = fragment.AlphaFunc;
        values[7] = fragment.AlphaRef / 255f;
        values[8] = fragment.ZclipActive ? 1 : 0;
        values[9] = fragment.ZclipCull ? 1 : 0;
        values[10] = fragment.ZclipMin;
        values[11] = fragment.ZclipMax;
        uint zetaFormat = info.ZetaBytes switch { 2u => 1u, 4u => 2u, _ => 0u };
        values[12] = Nv2AMmio.ZetaDepthRange(zetaFormat, info.ZFormatFloat);
        values[13] = info.ZetaBytes;

        if (program.ScalarValid &&
            program.ScalarClipWord == info.ShaderClipMode &&
            SameValues(values, program.ScalarCache, 14))
        {
            return;
        }

        _gl.Uniform4(program.Location("uFogColor"), values[0], values[1], values[2], values[3]);
        _gl.Uniform1(program.Location("uFogEnable"), values[4]);
        _gl.Uniform1(program.Location("uAlphaTestEnable"), values[5]);
        _gl.Uniform1(program.Location("uAlphaFunc"), (int)values[6]);
        _gl.Uniform1(program.Location("uAlphaRef"), values[7]);
        _gl.Uniform1(program.Location("uZclipActive"), values[8]);
        _gl.Uniform1(program.Location("uZclipCull"), values[9]);
        _gl.Uniform1(program.Location("uZclipMin"), values[10]);
        _gl.Uniform1(program.Location("uZclipMax"), values[11]);
        _gl.Uniform1(program.Location("uZetaRange"), values[12]);
        _gl.Uniform1(program.Location("uZetaBytes"), (int)values[13]);
        _gl.Uniform1(program.Location("uShaderClip"), info.ShaderClipMode);
        Array.Copy(values, program.ScalarCache, 14);
        program.ScalarClipWord = info.ShaderClipMode;
        program.ScalarValid = true;
    }

    private void SetTextureUniforms(GlProgram program)
    {
        // The values that affect the 2D sampler share one change check (the
        // 56-float cache). The cube/dot/decode uniforms ride on a stamp of
        // their own, because the same 2D sampler re-used under a different
        // texture mode is a different draw.
        float[] values = _texScratch;
        for (int i = 0; i < 4; i++)
        {
            values[i] = _texOk[i];
            values[4 + (i * 2)] = _texSize[i * 2];
            values[5 + (i * 2)] = _texSize[(i * 2) + 1];
            values[12 + i] = _texLinear[i];
            values[16 + i] = _texWrapX[i];
            values[20 + i] = _texWrapY[i];
            values[24 + i] = _texCube[i];
            values[28 + i] = _texDim[i];
            values[32 + i] = _texBilinear[i];
            values[36 + i] = _texConvolution[i];
            values[40 + (i * 4)] = _texBorder[(i * 4) + 0];
            values[41 + (i * 4)] = _texBorder[(i * 4) + 1];
            values[42 + (i * 4)] = _texBorder[(i * 4) + 2];
            values[43 + (i * 4)] = _texBorder[(i * 4) + 3];
        }

        // The cube/dot state must follow the draw even when the 2D state did
        // not change, so it has its own change stamp.
        uint modeStamp = CubeModeStamp();
        bool texChanged = !program.TexValid || !SameValues(values, program.TexCache, 56) ||
                          program.CubeModeStamp != modeStamp;
        if (!texChanged)
        {
            return;
        }

        _gl.Uniform1(program.Location("uTexOk[0]"), 4u, ref _texOk[0]);
        _gl.Uniform2(program.Location("uTexSize[0]"), 4u, ref _texSize[0]);
        _gl.Uniform1(program.Location("uTexLinear[0]"), 4u, ref _texLinear[0]);
        _gl.Uniform1(program.Location("uTexWrapX[0]"), 4u, ref _texWrapX[0]);
        _gl.Uniform1(program.Location("uTexWrapY[0]"), 4u, ref _texWrapY[0]);
        _gl.Uniform1(program.Location("uTexCube[0]"), 4u, ref _texCube[0]);
        _gl.Uniform1(program.Location("uTexDim[0]"), 4u, ref _texDim[0]);
        _gl.Uniform1(program.Location("uTexBilinear[0]"), 4u, ref _texBilinear[0]);
        _gl.Uniform1(program.Location("uTexConvolution[0]"), 4u, ref _texConvolution[0]);
        _gl.Uniform4(program.Location("uTexBorder[0]"), 4u, ref _texBorder[0]);
        // Cube uniforms ride on their own stamp (the 2D change check does not
        // cover them). They are only reached by a program whose combiner
        // carries a cube texture mode, so a presentable 2D workload never pays
        // these calls.
        _gl.Uniform1(program.Location("uTexCubeOk[0]"), 4u, ref _texCubeOk[0]);
        _gl.Uniform1(program.Location("uTexCubeW[0]"), 4u, ref _texCubeW[0]);
        _gl.Uniform1(program.Location("uTexCubeH[0]"), 4u, ref _texCubeH[0]);
        _gl.Uniform1(program.Location("uDotMap[0]"), 4u, ref _dotMap[0]);
        _gl.Uniform3(program.Location("uEyePos"), _eyePos[0], _eyePos[1], _eyePos[2]);
        _gl.Uniform4(program.Location("uBumpMatrix[0]"), 3u, ref _bumpMatrix[0]);
        _gl.Uniform1(program.Location("uBumpLumeScale[0]"), 3u, ref _bumpLumeScale[0]);
        _gl.Uniform1(program.Location("uBumpLumeOffset[0]"), 3u, ref _bumpLumeOffset[0]);
        SetGuestWordUniforms(program);
        Array.Copy(values, program.TexCache, 56);
        program.CubeModeStamp = modeStamp;
        program.TexValid = true;
    }

    /// <summary>
    /// Uploads the draw's guest texel words (B3 slice 2). The pool is exactly
    /// <see cref="GuestWordPoolSize"/> entries wide, so only the first
    /// <c>count</c> entries are sent: the slots' pool bases index it, and a
    /// draw with no computed mode uploads nothing (no stage then reads a word).
    /// The cube extents ride along because the face clamp reads them (the cube
    /// texels themselves are the host array texture).
    /// </summary>
    private void SetGuestWordUniforms(GlProgram program)
    {
        int count = _guestWordCount;
        if (count == 0)
        {
            return;
        }

        uint[] pool = _guestWordScratch;
        Array.Copy(_guestWordValues, pool, count * 4);
        _gl.Uniform4(program.Location("uGuestWord[0]"), (uint)(count * 4), ref pool[0]);
        _gl.Uniform1(program.Location("uTexPoolBase[0]"), 4u, ref _texPoolBase[0]);
    }

    /// <summary>Cheap stamp of every uniform the cube/dot sampler path reads (the 2D change check does not cover them).</summary>
    private uint CubeModeStamp()
    {
        uint stamp = 2166136261u;
        for (int i = 0; i < 4; i++)
        {
            stamp = (stamp ^ (uint)_texCubeOk[i]) * 16777619u;
            stamp = (stamp ^ (uint)_texCubeW[i]) * 16777619u;
            stamp = (stamp ^ (uint)_texCubeH[i]) * 16777619u;
            stamp = (stamp ^ (uint)_dotMap[i]) * 16777619u;
        }

        stamp = (stamp ^ BitConverter.SingleToUInt32Bits(_eyePos[0])) * 16777619u;
        stamp = (stamp ^ BitConverter.SingleToUInt32Bits(_eyePos[1])) * 16777619u;
        stamp = (stamp ^ BitConverter.SingleToUInt32Bits(_eyePos[2])) * 16777619u;
        return stamp;
    }

    private void PackVertices(in PgraphDrawInfo info, out int vertexCount)
    {
        // Per-draw values travel as uniforms so the fragment shader can re-run
        // the software barycentric math exactly. Perspective rule mirrors
        // Nv2AMmio.FillTriangle exactly. Positions go to uTri[0]/uTri[1] and
        // the clip window to uTri[2].zw + uTri[24].xy (the vertex shader
        // derives gl_Position from them by gl_VertexID).
        PackVerticesInto(in info, _uTri, 0);
        vertexCount = 3;
    }

    /// <summary>Packs one triangle's 25-vec4 block into <paramref name="target"/> at <paramref name="baseIndex"/> (floats).</summary>
    private static void PackVerticesInto(in PgraphDrawInfo info, float[] target, int baseIndex)
    {
        float invAw = info.A.W != 0f ? 1f / info.A.W : 0f;
        float invBw = info.B.W != 0f ? 1f / info.B.W : 0f;
        float invCw = info.C.W != 0f ? 1f / info.C.W : 0f;
        bool perspective = invAw != 0f && invBw != 0f && invCw != 0f &&
                           (info.A.W != info.B.W || info.B.W != info.C.W);

        PgraphVertex a = info.A;
        PgraphVertex b = info.B;
        PgraphVertex c = info.C;
        StoreVertex(in a, 0, target, baseIndex);
        StoreVertex(in b, 1, target, baseIndex);
        StoreVertex(in c, 2, target, baseIndex);
        target[baseIndex + 6] = perspective ? 1f : 0f;
        target[baseIndex + 7] = invAw;
        target[baseIndex + 8] = invBw;
        target[baseIndex + 9] = invCw;
        target[baseIndex + 10] = info.ClipY;
        target[baseIndex + 11] = info.ClipX;
        target[baseIndex + TriClipSizeBase] = info.ClipW;
        target[baseIndex + TriClipSizeBase + 1] = info.ClipH;
    }

    private static void StoreVertex(in PgraphVertex v, int index, float[] target, int baseIndex)
    {
        // Preamble pixels: uTri[0] = (vA.x, vA.y, vB.x, vB.y), uTri[1].xy = vC.
        target[baseIndex + (index * 2)] = v.X;
        target[baseIndex + (index * 2) + 1] = v.Y;
        int at = baseIndex + TriVaryingBase + (index * 4);
        target[at + 0] = v.DiffuseR;
        target[at + 1] = v.DiffuseG;
        target[at + 2] = v.DiffuseB;
        target[at + 3] = v.DiffuseA;
        at += TriVaryingStride;
        target[at + 0] = v.SpecR;
        target[at + 1] = v.SpecG;
        target[at + 2] = v.SpecB;
        target[at + 3] = v.SpecA;
        at += TriVaryingStride;
        target[at + 0] = v.U0;
        target[at + 1] = v.V0;
        target[at + 2] = v.S0;
        target[at + 3] = v.T0W;
        at += TriVaryingStride;
        target[at + 0] = v.U1;
        target[at + 1] = v.V1;
        target[at + 2] = v.S1;
        target[at + 3] = v.T1W;
        at += TriVaryingStride;
        target[at + 0] = v.U2;
        target[at + 1] = v.V2;
        target[at + 2] = v.S2;
        target[at + 3] = v.T2W;
        at += TriVaryingStride;
        target[at + 0] = v.U3;
        target[at + 1] = v.V3;
        target[at + 2] = v.S3;
        target[at + 3] = v.T3W;
        at += TriVaryingStride;
        target[at + 0] = v.Z;
        target[at + 1] = v.W;
        target[at + 2] = v.Fog;
        target[at + 3] = 0f;
    }

    private void UploadTriangleUniforms(GlProgram program)
    {
        // One upload for the whole per-draw block (was thirteen calls: the
        // per-draw uniform bill dominated GL submission on the menu).
        _gl.Uniform4(program.TriLocation, 25u, ref _uTri[0]);
    }
}
