using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private const int VertexFloats = 2;
    private const int VertexStrideBytes = VertexFloats * sizeof(float);

    private static readonly string[] UniformNames =
    {
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
        "uShaderClip",
        "uPx[0]",
        "uColorV[0]",
        "uSpecV[0]",
        "uT0V[0]",
        "uT1V[0]",
        "uT2V[0]",
        "uT3V[0]",
        "uVtx[0]",
        "uPerspective",
        "uInvAw",
        "uInvBw",
        "uInvCw",
        "uClipY",
        "uTex0",
        "uTex1",
        "uTex2",
        "uTex3",
    };

    private readonly WglContext _context;
    private readonly INativeContext _glContext;
    private readonly GL _gl;
    private readonly IGpuMemory _memory;

    // Present-upload staleness watch (§132 (a*)): non-null when the
    // injected IGpuMemory is one of the machine adapters that expose the
    // PhysicalMemory dirty bit (the real CLI wiring); null for seam fakes,
    // which keep the pre-§135 serve-whenever-FBO-non-empty contract.
    private readonly IPresentDirtyWatch? _dirtyWatch;

    private readonly uint _vertexShader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Dictionary<string, GlProgram> _programs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GlTextureEntry> _textures = new(StringComparer.Ordinal);
    private readonly float[] _vertexData = new float[VertexFloats * 3];
    private readonly float[] _uPx = new float[3 * 2];
    private readonly float[] _uColorV = new float[3 * 4];
    private readonly float[] _uSpecV = new float[3 * 4];
    private readonly float[] _uT0V = new float[3 * 4];
    private readonly float[] _uT1V = new float[3 * 4];
    private readonly float[] _uT2V = new float[3 * 4];
    private readonly float[] _uT3V = new float[3 * 4];
    private readonly float[] _uVtx = new float[3 * 4];
    private readonly float[] _constFlat = new float[18 * 4];
    private readonly int[] _texOk = new int[4];
    private readonly float[] _texSize = new float[4 * 2];
    private readonly int[] _texLinear = new int[4];
    private readonly int[] _texWrapX = new int[4];
    private readonly int[] _texWrapY = new int[4];
    private readonly int[] _texCube = new int[4];
    private readonly int[] _texDim = new int[4];

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
    private GlBackendTimings _timings;
    private int _perspective;
    private float _invAw;
    private float _invBw;
    private float _invCw;
    private float _clipY;

    private sealed class GlProgram
    {
        public GlProgram(uint id)
        {
            Id = id;
            Locations = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public uint Id { get; }

        public Dictionary<string, int> Locations { get; }

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

    private sealed class GlTextureEntry
    {
        public GlTextureEntry(uint texture, uint fingerprint, int width, int height)
        {
            Texture = texture;
            Fingerprint = fingerprint;
            Width = width;
            Height = height;
        }

        public uint Texture { get; }
        public uint Fingerprint { get; set; }
        public int Width { get; }
        public int Height { get; }
    }

    internal GlPgraphBackend(WglContext context, INativeContext glContext, GL gl, IGpuMemory memory)
    {
        _context = context;
        _glContext = glContext;
        _gl = gl;
        _memory = memory;
        _dirtyWatch = memory as IPresentDirtyWatch;

        _vertexShader = CompileVertexShader();
        _gl.GenVertexArrays(1, out _vao);
        _gl.GenBuffers(1, out _vbo);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        const uint stride = (uint)VertexStrideBytes;
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        _gl.BindVertexArray(0);

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

    /// <summary>Gets the number of methods observed through the seam.</summary>
    public long MethodsObserved { get; private set; }

    /// <summary>Gets the number of triangles submitted to GL.</summary>
    public long Draws { get; private set; }

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
                $"backend=gl draws={Draws} clears={Clears} flips={Flips} methods={MethodsObserved} " +
                $"programs={_programs.Count} uploads={TextureUploads} unsupported={BackendUnsupported}" +
                $" unsup[colorBytes={_unsupportedColorBytes} zformat={_unsupportedZFormat} modes={_unsupportedModes}" +
                $" cubeVol={_unsupportedCubeVolume} fbo={_unsupportedFbo} flip={_unsupportedFlip} fatal={_fatalCount}]" +
                $" diag[fboReallocs={_fboReallocs} fboZetaDrops={_fboZetaDrops} clipXY={_clipXyNonZero} flipEmpty={_flipEmpty} presentUploads={_presentUploads} flipStaleUploads={_flipStaleUploads}]");
            _timings.AppendTo(sb);
            if (_fatalError is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" fatal=\"{_fatalError}\"");
            }

            return sb.ToString();
        }
    }

    /// <summary>Observes one PGRAPH method triple at the top of RecordMethod, counting it in <see cref="MethodsObserved"/>; issues no GL calls.</summary>
    public void Method(uint subchannel, uint method, uint parameter)
    {
        _ = subchannel;
        _ = method;
        _ = parameter;
        MethodsObserved++;
    }

    /// <summary>Submits the triangle to GL via DrawCore, timing the call; a thrown GL exception increments <see cref="BackendUnsupported"/> and latches a fatal error that suppresses later work.</summary>
    public void Draw(in PgraphDrawInfo info)
    {
        if (_fatalError is not null)
        {
            return;
        }

        try
        {
            long start = Stopwatch.GetTimestamp();
            DrawCore(info);
            _timings.AddDraw(Stopwatch.GetElapsedTime(start));
        }
        catch (Exception exception)
        {
            // Fail loud and stop issuing GL calls rather than storming.
            BackendUnsupported++;
            _fatalCount++;
            _fatalError = exception.GetType().Name + ": " + exception.Message;
        }
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
        bgra = _readback ?? Array.Empty<byte>();
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

        DeleteFbo();
        foreach (KeyValuePair<string, GlTextureEntry> entry in _textures)
        {
            uint texture = entry.Value.Texture;
            _gl.DeleteTextures(1, ref texture);
        }

        _textures.Clear();
        _readback = null;
        _readbackWidth = 0;
        _readbackHeight = 0;
        _fatalError = null;
        MethodsObserved = 0;
        Draws = 0;
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
        _fatalCount = 0;
        _fboReallocs = 0;
        _fboZetaDrops = 0;
        _clipXyNonZero = 0;
        _flipEmpty = 0;
        _presentUploads = 0;
        _flipStaleUploads = 0;
        _timings.Reset();

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
        try
        {
            DeleteFbo();
            foreach (KeyValuePair<string, GlTextureEntry> entry in _textures)
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
            uint vbo = _vbo;
            if (vbo != 0)
            {
                _gl.DeleteBuffers(1, ref vbo);
            }

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

    private void DrawCore(in PgraphDrawInfo info)
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
            return;
        }

        if (info.ZFormatFloat && info.ZetaBytes != 0 && info.Fragment.DepthTestEnable)
        {
            BackendUnsupported++;
            _unsupportedZFormat++;
            return;
        }

        bool legacy = !info.CombinerActive;
        PshCombiner? combiner = info.Combiner;
        if (!legacy && combiner is null)
        {
            BackendUnsupported++;
            _unsupportedModes++;
            return;
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
        }

        if (!EnsureFbo((int)info.ClipW, (int)info.ClipH, info.ZetaBytes))
        {
            BackendUnsupported++;
            _unsupportedFbo++;
            return;
        }

        GlProgram program = EnsureProgram(legacy, combiner);
        ApplyTextures(program, info, combiner, legacy);
        ApplyState(in info);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, info.ClipW, info.ClipH);
        _gl.UseProgram(program.Id);
        ApplyUniforms(program, in info, combiner);

        PackVertices(in info, out int vertexCount);
        UploadTriangleUniforms(program);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* pVertices = _vertexData)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (UIntPtr)(vertexCount * VertexStrideBytes),
                    pVertices,
                    BufferUsageARB.StreamDraw);
            }
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    }

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

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Enable(EnableCap.ScissorTest);
        int x = unchecked((int)(info.XMin - info.ClipX));
        int y = unchecked((int)(info.YMin - info.ClipY));
        uint width = info.XMax - info.XMin + 1;
        uint height = info.YMax - info.YMin + 1;
        _gl.Scissor(x, y, width, height);
        if (info.ClearColor && info.ColorBytes == 4)
        {
            GlStateMapper.MapClearColor(info.ColorValue, out float r, out float g, out float b, out float a);
            _gl.ClearColor(r, g, b, a);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
        }

        if (info.ClearZeta && info.ZetaBytes != 0 && !info.ZFormatFloat)
        {
            GlStateMapper.MapZetaClear(info.ZetaValue, info.ZetaBytes, out double depth, out int stencil);
            _gl.ClearDepth(depth);
            if (info.ZetaBytes == 4)
            {
                _gl.ClearStencil(stencil);
                _gl.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            }
            else
            {
                _gl.Clear(ClearBufferMask.DepthBufferBit);
            }
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
        byte[] rgba = new byte[width * height * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
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
        if (_fbo != 0 && _dirtyWatch is not null)
        {
            ulong span = (ulong)record.Pitch * (ulong)record.Height;
            _dirtyWatch.ArmPresentWatch(
                record.SurfaceAddress,
                span > uint.MaxValue ? uint.MaxValue : (uint)span);
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

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        uint delFbo = srcFbo;
        _gl.DeleteFramebuffers(1, ref delFbo);
        uint delTexture = texture;
        _gl.DeleteTextures(1, ref delTexture);
        return complete;
    }

    /// <summary>Returns true when a valid-stage slice-1-unsupported texture mode is in use.</summary>
    private static bool CheckModesUnsupported(in PgraphDrawInfo info, PshCombiner combiner, out bool cubeVolume)
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
                case PshCombiner.TexModeDotProduct:
                case PshCombiner.TexModeDotZw:
                case PshCombiner.TexModeDotSt:
                case PshCombiner.TexModeDotRflctDiff:
                case PshCombiner.TexModeDotRflctSpec:
                case PshCombiner.TexModeDotStr3D:
                case PshCombiner.TexModeDotStrCube:
                case PshCombiner.TexModeBumpEnvMap:
                case PshCombiner.TexModeBumpEnvMapLum:
                    unsupported = true;
                    break;
                case PshCombiner.TexModeProject2D:
                case PshCombiner.TexModeCubemap:
                    if (CubeSuccessPath(info.GetTexture(slot)))
                    {
                        unsupported = true;
                        cubeVolume = true;
                    }

                    break;
                case PshCombiner.TexModeProject3D:
                    if (VolumeSuccessPath(info.GetTexture(slot)))
                    {
                        unsupported = true;
                        cubeVolume = true;
                    }

                    break;
            }
        }

        return unsupported;
    }

    private static bool CubeSuccessPath(in PgraphTextureSlot slot)
    {
        // Software cube-samples only for cubemap-flagged slots with a
        // supported swizzled format (05/06) and sampling configured.
        uint color = (slot.Format >> 8) & 0x7Fu;
        bool enabled = (slot.Control0 & (1u << 30)) != 0;
        bool cubemap = (slot.Format & TextureFormatCubemapEnable) != 0;
        return enabled && cubemap && color is 0x05u or 0x06u;
    }

    private static bool VolumeSuccessPath(in PgraphTextureSlot slot)
    {
        uint color = (slot.Format >> 8) & 0x7Fu;
        bool enabled = (slot.Control0 & (1u << 30)) != 0;
        int dim = (int)((slot.Format & 0xC0u) >> 6);
        return enabled && dim == 3 && color is 0x05u or 0x06u;
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
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
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

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
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

    private uint CompileVertexShader()
    {
        const string source =
            "#version 330 core\n" +
            "layout(location = 0) in vec2 aPos;\n" +
            "void main() {\n" +
            "  gl_Position = vec4(aPos, 0.0, 1.0);\n" +
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
        string key = PshGlslGenerator.BuildKey(legacy, combiner!);
        if (_programs.TryGetValue(key, out GlProgram? existing))
        {
            return existing;
        }

        string source = PshGlslGenerator.BuildFragmentSource(legacy, combiner!);
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

        _gl.UseProgram(program);
        _gl.Uniform1(glProgram.Location("uTex0"), 0);
        _gl.Uniform1(glProgram.Location("uTex1"), 1);
        _gl.Uniform1(glProgram.Location("uTex2"), 2);
        _gl.Uniform1(glProgram.Location("uTex3"), 3);
        _gl.UseProgram(0);
        _programs.Add(key, glProgram);
        ProgramsCompiled++;
        return glProgram;
    }

    private void ApplyTextures(GlProgram program, in PgraphDrawInfo info, PshCombiner? combiner, bool legacy)
    {
        _ = program;
        _ = combiner;
        _ = legacy;
        for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
        {
            PgraphTextureSlot texture = info.GetTexture(slot);
            uint color = (texture.Format >> 8) & 0x7Fu;
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
            _texWrapX[slot] = (int)(texture.Address & 0xFu);
            _texWrapY[slot] = (int)((texture.Address >> 4) & 0xFu);

            _texCube[slot] = (texture.Format & TextureFormatCubemapEnable) != 0 ? 1 : 0;
            _texDim[slot] = (int)((texture.Format & 0xC0u) >> 6);
            if (!sampleOk)
            {
                continue;
            }

            string key = string.Create(
                CultureInfo.InvariantCulture,
                $"{texture.Offset:X8}:{color:X2}:{width}x{height}:{pitch}:{(linear ? 1 : 0)}");
            uint fingerprint = FingerprintTexture(in texture, linear, bytes, width, height, pitch);
            if (!_textures.TryGetValue(key, out GlTextureEntry? entry))
            {
                uint textureId = UploadTexture(in texture, color, linear, bytes, width, height, pitch, fingerprint);
                entry = new GlTextureEntry(textureId, fingerprint, width, height);
                _textures.Add(key, entry);
            }
            else if (entry.Fingerprint != fingerprint)
            {
                UploadTexture(in texture, color, linear, bytes, width, height, pitch, fingerprint);
                entry.Fingerprint = fingerprint;
            }

            _gl.ActiveTexture((TextureUnit)(0x84C0u + (uint)slot));
            _gl.BindTexture(TextureTarget.Texture2D, entry.Texture);
        }
    }

    private uint FingerprintTexture(in PgraphTextureSlot slot, bool linear, uint bytes, int width, int height,
        int pitch)
    {
        // Cheap sampled content fingerprint (head/tail/strided texel words):
        // catches full-buffer re-uploads (the menu's real pattern) without a
        // per-draw scan of megabytes; validated by the golden-frame gate.
        uint hash = 2166136261u;
        for (int i = 0; i < 16; i++)
        {
            int x = (int)((ulong)((i * 97) + 13) % (uint)width);
            int y = (int)((ulong)((i * 389) + 7) % (uint)height);
            uint address = TexelAddress(in slot, linear, bytes, width, height, pitch, x, y);
            uint texel = bytes == 2
                ? _memory.ReadPhysical(address) & 0xFFFFu
                : _memory.ReadPhysical(address);
            hash ^= texel;
            hash *= 16777619u;
        }

        return hash;
    }

    private static uint TexelAddress(
        in PgraphTextureSlot slot, bool linear, uint bytes, int width, int height, int pitch, int x, int y)
    {
        if (linear)
        {
            return slot.Offset + (uint)(y * pitch) + (uint)(x * (int)bytes);
        }

        return slot.Offset + (Nv2AMmio.SwizzleOffset(x, y, width, height) * bytes);
    }

    private uint UploadTexture(
        in PgraphTextureSlot slot, uint color, bool linear, uint bytes,
        int width, int height, int pitch, uint fingerprint)
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

    private void ApplyState(in PgraphDrawInfo info)
    {
        PgraphFragmentState fragment = info.Fragment;
        if (fragment.BlendEnable)
        {
            _gl.Enable(EnableCap.Blend);
            GlStateMapper.MapBlendColor(fragment.BlendColor, out float br, out float bg, out float bb, out float ba);
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

        GlStateMapper.MapColorMask(
            fragment.ColorMask, out bool maskR, out bool maskG, out bool maskB, out bool maskA);
        _gl.ColorMask(maskR, maskG, maskB, maskA);

        if (fragment.DepthTestEnable && info.ZetaBytes != 0)
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

        if (fragment.StencilTestEnable && info.ZetaBytes == 4)
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

        float[][] consts = PshCombiner.BuildConsts(combiner);
        for (int i = 0; i < consts.Length; i++)
        {
            _constFlat[(i * 4) + 0] = consts[i][0];
            _constFlat[(i * 4) + 1] = consts[i][1];
            _constFlat[(i * 4) + 2] = consts[i][2];
            _constFlat[(i * 4) + 3] = consts[i][3];
        }

        _gl.Uniform4(program.Location("uConsts[0]"), 18u, ref _constFlat[0]);
        SetScalarUniforms(program, in info, in fragment);
        SetTextureUniforms(program);
    }

    private void SetScalarUniforms(GlProgram program, in PgraphDrawInfo info, in PgraphFragmentState fragment)
    {
        _gl.Uniform4(
            program.Location("uFogColor"), info.FogR, info.FogG, info.FogB, info.FogA);
        _gl.Uniform1(program.Location("uFogEnable"), info.FogEnable ? 1 : 0);
        _gl.Uniform1(program.Location("uAlphaTestEnable"), fragment.AlphaTestEnable ? 1 : 0);
        _gl.Uniform1(program.Location("uAlphaFunc"), (int)fragment.AlphaFunc);
        _gl.Uniform1(program.Location("uAlphaRef"), fragment.AlphaRef / 255f);
        _gl.Uniform1(program.Location("uZclipActive"), fragment.ZclipActive ? 1 : 0);
        _gl.Uniform1(program.Location("uZclipCull"), fragment.ZclipCull ? 1 : 0);
        _gl.Uniform1(program.Location("uZclipMin"), fragment.ZclipMin);
        _gl.Uniform1(program.Location("uZclipMax"), fragment.ZclipMax);
        uint zetaFormat = info.ZetaBytes switch { 2u => 1u, 4u => 2u, _ => 0u };
        float zetaRange = Nv2AMmio.ZetaDepthRange(zetaFormat, info.ZFormatFloat);
        _gl.Uniform1(program.Location("uZetaRange"), zetaRange);
        _gl.Uniform1(program.Location("uZetaBytes"), (int)info.ZetaBytes);
        _gl.Uniform1(program.Location("uShaderClip"), info.ShaderClipMode);
    }

    private void SetTextureUniforms(GlProgram program)
    {
        _gl.Uniform1(program.Location("uTexOk[0]"), 4u, ref _texOk[0]);
        _gl.Uniform2(program.Location("uTexSize[0]"), 4u, ref _texSize[0]);
        _gl.Uniform1(program.Location("uTexLinear[0]"), 4u, ref _texLinear[0]);
        _gl.Uniform1(program.Location("uTexWrapX[0]"), 4u, ref _texWrapX[0]);
        _gl.Uniform1(program.Location("uTexWrapY[0]"), 4u, ref _texWrapY[0]);
        _gl.Uniform1(program.Location("uTexCube[0]"), 4u, ref _texCube[0]);
        _gl.Uniform1(program.Location("uTexDim[0]"), 4u, ref _texDim[0]);
    }

    private void PackVertices(in PgraphDrawInfo info, out int vertexCount)
    {
        // NDC-only vertex buffer (attributes travel as per-draw uniforms so
        // the fragment shader can re-run the software barycentric math
        // exactly). Perspective rule mirrors Nv2AMmio.FillTriangle exactly.
        float invAw = info.A.W != 0f ? 1f / info.A.W : 0f;
        float invBw = info.B.W != 0f ? 1f / info.B.W : 0f;
        float invCw = info.C.W != 0f ? 1f / info.C.W : 0f;
        bool perspective = invAw != 0f && invBw != 0f && invCw != 0f &&
                           (info.A.W != info.B.W || info.B.W != info.C.W);
        _perspective = perspective ? 1 : 0;
        _invAw = invAw;
        _invBw = invBw;
        _invCw = invCw;
        _clipY = info.ClipY;
        float clipW = info.ClipW;
        float clipH = info.ClipH;
        float clipX = info.ClipX;
        float clipY = info.ClipY;

        PgraphVertex a = info.A;
        PgraphVertex b = info.B;
        PgraphVertex c = info.C;
        StoreVertex(in a, clipX, clipY, clipW, clipH, 0);
        StoreVertex(in b, clipX, clipY, clipW, clipH, 1);
        StoreVertex(in c, clipX, clipY, clipW, clipH, 2);

        // Coverage superset: inflate the NDC triangle slightly about its
        // centroid so GL rasterizes every pixel the software inclusive
        // (any-sign) edge test accepts — GL's top-left rule alone drops
        // shared-diagonal tie pixels that software covers TWICE (the
        // double-blend on quad diagonals). The fragment shader's
        // software-exact inside-test discards the over-coverage, leaving
        // exactly software's per-triangle pixel set, ties included.
        const float inflate = 1.02f;
        float ndcAx = _vertexData[0];
        float ndcAy = _vertexData[1];
        float ndcBx = _vertexData[2];
        float ndcBy = _vertexData[3];
        float ndcCx = _vertexData[4];
        float ndcCy = _vertexData[5];
        float centerX = (ndcAx + ndcBx + ndcCx) / 3f;
        float centerY = (ndcAy + ndcBy + ndcCy) / 3f;
        _vertexData[0] = centerX + ((ndcAx - centerX) * inflate);
        _vertexData[1] = centerY + ((ndcAy - centerY) * inflate);
        _vertexData[2] = centerX + ((ndcBx - centerX) * inflate);
        _vertexData[3] = centerY + ((ndcBy - centerY) * inflate);
        _vertexData[4] = centerX + ((ndcCx - centerX) * inflate);
        _vertexData[5] = centerY + ((ndcCy - centerY) * inflate);
        vertexCount = 3;
    }

    private void StoreVertex(in PgraphVertex v, float clipX, float clipY, float clipW, float clipH, int index)
    {
        _uPx[(index * 2) + 0] = v.X;
        _uPx[(index * 2) + 1] = v.Y;
        _uColorV[(index * 4) + 0] = v.DiffuseR;
        _uColorV[(index * 4) + 1] = v.DiffuseG;
        _uColorV[(index * 4) + 2] = v.DiffuseB;
        _uColorV[(index * 4) + 3] = v.DiffuseA;
        _uSpecV[(index * 4) + 0] = v.SpecR;
        _uSpecV[(index * 4) + 1] = v.SpecG;
        _uSpecV[(index * 4) + 2] = v.SpecB;
        _uSpecV[(index * 4) + 3] = v.SpecA;
        _uT0V[(index * 4) + 0] = v.U0;
        _uT0V[(index * 4) + 1] = v.V0;
        _uT0V[(index * 4) + 2] = v.S0;
        _uT0V[(index * 4) + 3] = v.T0W;
        _uT1V[(index * 4) + 0] = v.U1;
        _uT1V[(index * 4) + 1] = v.V1;
        _uT1V[(index * 4) + 2] = v.S1;
        _uT1V[(index * 4) + 3] = v.T1W;
        _uT2V[(index * 4) + 0] = v.U2;
        _uT2V[(index * 4) + 1] = v.V2;
        _uT2V[(index * 4) + 2] = v.S2;
        _uT2V[(index * 4) + 3] = v.T2W;
        _uT3V[(index * 4) + 0] = v.U3;
        _uT3V[(index * 4) + 1] = v.V3;
        _uT3V[(index * 4) + 2] = v.S3;
        _uT3V[(index * 4) + 3] = v.T3W;
        _uVtx[(index * 4) + 0] = v.Z;
        _uVtx[(index * 4) + 1] = v.W;
        _uVtx[(index * 4) + 2] = v.Fog;
        _uVtx[(index * 4) + 3] = 0f;

        _vertexData[(index * VertexFloats) + 0] = ((2f * (v.X - clipX)) / clipW) - 1f;
        _vertexData[(index * VertexFloats) + 1] = ((2f * (v.Y - clipY)) / clipH) - 1f;
    }

    private void UploadTriangleUniforms(GlProgram program)
    {
        _gl.Uniform2(program.Location("uPx[0]"), 3u, ref _uPx[0]);
        _gl.Uniform4(program.Location("uColorV[0]"), 3u, ref _uColorV[0]);
        _gl.Uniform4(program.Location("uSpecV[0]"), 3u, ref _uSpecV[0]);
        _gl.Uniform4(program.Location("uT0V[0]"), 3u, ref _uT0V[0]);
        _gl.Uniform4(program.Location("uT1V[0]"), 3u, ref _uT1V[0]);
        _gl.Uniform4(program.Location("uT2V[0]"), 3u, ref _uT2V[0]);
        _gl.Uniform4(program.Location("uT3V[0]"), 3u, ref _uT3V[0]);
        _gl.Uniform4(program.Location("uVtx[0]"), 3u, ref _uVtx[0]);
        _gl.Uniform1(program.Location("uPerspective"), _perspective);
        _gl.Uniform1(program.Location("uInvAw"), _invAw);
        _gl.Uniform1(program.Location("uInvBw"), _invBw);
        _gl.Uniform1(program.Location("uInvCw"), _invCw);
        _gl.Uniform1(program.Location("uClipY"), _clipY);
    }
}
