using System;
using System.Runtime.InteropServices;

namespace XGrid.Nv2a;

/// <summary>
/// Optional second consumer of the PGRAPH dispatch (GPU backend seam).
/// The software path executes exactly as today, unconditionally; the backend
/// observes the same resolved state at the flush sites (observation-only
/// mirror of <c>RecordMethod</c> plus draw/clear/flip callbacks carrying the
/// state the software path uses). When no backend is attached
/// (<c>Nv2AMmio.Backend == null</c>, the default) nothing changes.
/// Slice-1 rule: every callback is consumed synchronously on the emulation
/// thread at the existing flush sites; the backend never runs async.
/// </summary>
public interface IPgraphBackend
{
    /// <summary>Gets the number of in-subset states the backend could not honor. Gate requires 0.</summary>
    long BackendUnsupported { get; }

    /// <summary>Gets a one-line diagnostics summary for the CLI.</summary>
    string DebugSummary { get; }

    /// <summary>Observes one (subchannel, method, parameter) triple at the top of RecordMethod.</summary>
    void Method(uint subchannel, uint method, uint parameter);

    /// <summary>
    /// Gets whether this backend consumes the per-method observation callback.
    /// The software rasterizer's caller already counts every method
    /// (<c>Nv2aMmio.MethodTotal</c>), so a backend that only wants a total can
    /// decline the callback and save a virtual call per PGRAPH method on
    /// method-heavy workloads (18M methods per 1B menu steps).
    /// </summary>
    bool ObserveMethods => true;

    /// <summary>Submits one transformed triangle with the resolved state the software rasterizer uses.</summary>
    void Draw(in PgraphDrawInfo info);

    /// <summary>Mirrors a surface clear the software path actually performed.</summary>
    void Clear(in PgraphClearInfo info);

    /// <summary>Notifies the present event (FLIP_INCREMENT_WRITE) with the resolved surface record.</summary>
    void Flip(in PgraphSurfaceRecord record);

    /// <summary>Returns the backend-rendered presented frame as BGRA bytes.</summary>
    /// <returns>False when the backend has no valid frame; the caller must not mix in software pixels.</returns>
    bool ReadbackPresented(out uint width, out uint height, out byte[] bgra);

    /// <summary>Drops host-side backend state (snapshot load / machine reset). Guest state is unaffected.</summary>
    void ResetState();

    /// <summary>
    /// Optional host-side pump hook: called by the CLI between emulation
    /// batches so a live window stays responsive. The default implementation
    /// does nothing; only the window-mode GL backend acts on it, and only
    /// when the opt-in window is enabled.
    /// </summary>
    void Pump()
    {
    }

    /// <summary>
    /// Gets whether this backend replaces the software rasterizer for the
    /// draws and clears it honors (replacement mode) instead of mirroring it.
    /// Mirror mode (the default) keeps the software path authoritative: the
    /// backend observes the same resolved state and the software pixels are
    /// the guest-visible source. In replacement mode the backend becomes the
    /// pixel source: the caller skips the software fill when
    /// <see cref="TryDraw"/> succeeds, so the backends must never be mixed
    /// within a run. Replacement mode is a GPU-accelerated (fast-mode
    /// playability) contract; the software path stays the accurate-mode
    /// source of truth.
    /// </summary>
    bool ReplaceMode => false;

    /// <summary>
    /// Attempts to render one triangle in replacement mode. Returns true when
    /// the backend honored the draw and the caller must skip the software
    /// rasterizer; false when the caller must fall back to the software path
    /// for this draw (the backend counted it as unsupported).
    /// </summary>
    bool TryDraw(in PgraphDrawInfo info) => false;
}

/// <summary>One screen-space transformed vertex (outputs of the CPU VshProgram path).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphVertex
{
    /// <summary>Initializes a transformed vertex from its position, colors, texture coordinates and fog factor.</summary>
    /// <param name="x">Screen-space x.</param>
    /// <param name="y">Screen-space y.</param>
    /// <param name="z">Screen-space depth.</param>
    /// <param name="w">Screen-space w (perspective divide and projective texturing).</param>
    /// <param name="diffuseR">Diffuse red.</param>
    /// <param name="diffuseG">Diffuse green.</param>
    /// <param name="diffuseB">Diffuse blue.</param>
    /// <param name="diffuseA">Diffuse alpha.</param>
    /// <param name="specR">Specular red.</param>
    /// <param name="specG">Specular green.</param>
    /// <param name="specB">Specular blue.</param>
    /// <param name="specA">Specular alpha.</param>
    /// <param name="u0">Stage-0 texture u.</param>
    /// <param name="v0">Stage-0 texture v.</param>
    /// <param name="s0">Stage-0 texture third coordinate.</param>
    /// <param name="u1">Stage-1 texture u.</param>
    /// <param name="v1">Stage-1 texture v.</param>
    /// <param name="s1">Stage-1 texture third coordinate.</param>
    /// <param name="u2">Stage-2 texture u.</param>
    /// <param name="v2">Stage-2 texture v.</param>
    /// <param name="s2">Stage-2 texture third coordinate.</param>
    /// <param name="u3">Stage-3 texture u.</param>
    /// <param name="v3">Stage-3 texture v.</param>
    /// <param name="s3">Stage-3 texture third coordinate.</param>
    /// <param name="t0W">Stage-0 projective w divisor.</param>
    /// <param name="t1W">Stage-1 projective w divisor.</param>
    /// <param name="t2W">Stage-2 projective w divisor.</param>
    /// <param name="t3W">Stage-3 projective w divisor.</param>
    /// <param name="fog">Interpolated fog factor.</param>
    public PgraphVertex(
        float x, float y, float z, float w,
        float diffuseR, float diffuseG, float diffuseB, float diffuseA,
        float specR, float specG, float specB, float specA,
        float u0, float v0, float s0,
        float u1, float v1, float s1,
        float u2, float v2, float s2,
        float u3, float v3, float s3,
        float t0W, float t1W, float t2W, float t3W,
        float fog)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
        DiffuseR = diffuseR;
        DiffuseG = diffuseG;
        DiffuseB = diffuseB;
        DiffuseA = diffuseA;
        SpecR = specR;
        SpecG = specG;
        SpecB = specB;
        SpecA = specA;
        U0 = u0;
        V0 = v0;
        S0 = s0;
        U1 = u1;
        V1 = v1;
        S1 = s1;
        U2 = u2;
        V2 = v2;
        S2 = s2;
        U3 = u3;
        V3 = v3;
        S3 = s3;
        T0W = t0W;
        T1W = t1W;
        T2W = t2W;
        T3W = t3W;
        Fog = fog;
    }

    /// <summary>Gets the screen-space x-coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the screen-space y-coordinate.</summary>
    public float Y { get; }

    /// <summary>Gets the screen-space depth.</summary>
    public float Z { get; }

    /// <summary>Gets the screen-space w (perspective divide and projective texturing).</summary>
    public float W { get; }

    /// <summary>Gets the diffuse red component.</summary>
    public float DiffuseR { get; }

    /// <summary>Gets the diffuse green component.</summary>
    public float DiffuseG { get; }

    /// <summary>Gets the diffuse blue component.</summary>
    public float DiffuseB { get; }

    /// <summary>Gets the diffuse alpha component.</summary>
    public float DiffuseA { get; }

    /// <summary>Gets the specular red component.</summary>
    public float SpecR { get; }

    /// <summary>Gets the specular green component.</summary>
    public float SpecG { get; }

    /// <summary>Gets the specular blue component.</summary>
    public float SpecB { get; }

    /// <summary>Gets the specular alpha component.</summary>
    public float SpecA { get; }

    /// <summary>Gets the stage-0 texture u coordinate.</summary>
    public float U0 { get; }

    /// <summary>Gets the stage-0 texture v coordinate.</summary>
    public float V0 { get; }

    /// <summary>Gets the stage-0 texture third coordinate.</summary>
    public float S0 { get; }

    /// <summary>Gets the stage-1 texture u coordinate.</summary>
    public float U1 { get; }

    /// <summary>Gets the stage-1 texture v coordinate.</summary>
    public float V1 { get; }

    /// <summary>Gets the stage-1 texture third coordinate.</summary>
    public float S1 { get; }

    /// <summary>Gets the stage-2 texture u coordinate.</summary>
    public float U2 { get; }

    /// <summary>Gets the stage-2 texture v coordinate.</summary>
    public float V2 { get; }

    /// <summary>Gets the stage-2 texture third coordinate.</summary>
    public float S2 { get; }

    /// <summary>Gets the stage-3 texture u coordinate.</summary>
    public float U3 { get; }

    /// <summary>Gets the stage-3 texture v coordinate.</summary>
    public float V3 { get; }

    /// <summary>Gets the stage-3 texture third coordinate.</summary>
    public float S3 { get; }

    /// <summary>Gets the stage-0 projective w divisor.</summary>
    public float T0W { get; }

    /// <summary>Gets the stage-1 projective w divisor.</summary>
    public float T1W { get; }

    /// <summary>Gets the stage-2 projective w divisor.</summary>
    public float T2W { get; }

    /// <summary>Gets the stage-3 projective w divisor.</summary>
    public float T3W { get; }

    /// <summary>Gets the interpolated fog factor.</summary>
    public float Fog { get; }
}

/// <summary>Resolved fragment (ROP) state snapshot for one draw or clear.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphFragmentState
{
    /// <summary>Initializes a resolved fragment-state snapshot from the live PGRAPH registers.</summary>
    /// <param name="alphaTestEnable">Whether the alpha test runs.</param>
    /// <param name="alphaFunc">Alpha compare function.</param>
    /// <param name="alphaRef">Alpha reference value.</param>
    /// <param name="blendEnable">Whether blending runs.</param>
    /// <param name="blendSrcFactor">Source blend factor.</param>
    /// <param name="blendDstFactor">Destination blend factor.</param>
    /// <param name="blendColor">Constant blend color.</param>
    /// <param name="blendEquation">Blend equation.</param>
    /// <param name="depthTestEnable">Whether the depth test runs.</param>
    /// <param name="depthFunc">Depth compare function.</param>
    /// <param name="depthMask">Whether passing depth writes the zeta buffer.</param>
    /// <param name="colorMask">Per-channel color write mask.</param>
    /// <param name="stencilTestEnable">Whether the stencil test runs.</param>
    /// <param name="stencilWriteMask">Stencil write mask.</param>
    /// <param name="stencilFunc">Stencil compare function.</param>
    /// <param name="stencilRef">Stencil reference value.</param>
    /// <param name="stencilReadMask">Stencil read mask.</param>
    /// <param name="stencilOpFail">Stencil action on stencil-test failure.</param>
    /// <param name="stencilOpZfail">Stencil action on depth-test failure.</param>
    /// <param name="stencilOpZpass">Stencil action on depth-test pass.</param>
    /// <param name="zclipActive">Whether shader clip planes are active.</param>
    /// <param name="zclipMin">Minimum clip depth.</param>
    /// <param name="zclipMax">Maximum clip depth.</param>
    /// <param name="zclipCull">Whether out-of-range fragments are culled.</param>
    public PgraphFragmentState(
        bool alphaTestEnable, uint alphaFunc, uint alphaRef,
        bool blendEnable, uint blendSrcFactor, uint blendDstFactor, uint blendColor, uint blendEquation,
        bool depthTestEnable, uint depthFunc, bool depthMask,
        uint colorMask,
        bool stencilTestEnable, uint stencilWriteMask, uint stencilFunc, uint stencilRef,
        uint stencilReadMask, uint stencilOpFail, uint stencilOpZfail, uint stencilOpZpass,
        bool zclipActive, float zclipMin, float zclipMax, bool zclipCull)
    {
        AlphaTestEnable = alphaTestEnable;
        AlphaFunc = alphaFunc;
        AlphaRef = alphaRef;
        BlendEnable = blendEnable;
        BlendSrcFactor = blendSrcFactor;
        BlendDstFactor = blendDstFactor;
        BlendColor = blendColor;
        BlendEquation = blendEquation;
        DepthTestEnable = depthTestEnable;
        DepthFunc = depthFunc;
        DepthMask = depthMask;
        ColorMask = colorMask;
        StencilTestEnable = stencilTestEnable;
        StencilWriteMask = stencilWriteMask;
        StencilFunc = stencilFunc;
        StencilRef = stencilRef;
        StencilReadMask = stencilReadMask;
        StencilOpFail = stencilOpFail;
        StencilOpZfail = stencilOpZfail;
        StencilOpZpass = stencilOpZpass;
        ZclipActive = zclipActive;
        ZclipMin = zclipMin;
        ZclipMax = zclipMax;
        ZclipCull = zclipCull;
    }

    /// <summary>Gets whether the alpha test runs.</summary>
    public bool AlphaTestEnable { get; }

    /// <summary>Gets the alpha compare function.</summary>
    public uint AlphaFunc { get; }

    /// <summary>Gets the alpha reference value.</summary>
    public uint AlphaRef { get; }

    /// <summary>Gets whether blending runs.</summary>
    public bool BlendEnable { get; }

    /// <summary>Gets the source blend factor.</summary>
    public uint BlendSrcFactor { get; }

    /// <summary>Gets the destination blend factor.</summary>
    public uint BlendDstFactor { get; }

    /// <summary>Gets the constant blend color.</summary>
    public uint BlendColor { get; }

    /// <summary>Gets the blend equation.</summary>
    public uint BlendEquation { get; }

    /// <summary>Gets whether the depth test runs.</summary>
    public bool DepthTestEnable { get; }

    /// <summary>Gets the depth compare function.</summary>
    public uint DepthFunc { get; }

    /// <summary>Gets whether passing depth writes the zeta buffer.</summary>
    public bool DepthMask { get; }

    /// <summary>Gets the per-channel color write mask.</summary>
    public uint ColorMask { get; }

    /// <summary>Gets whether the stencil test runs.</summary>
    public bool StencilTestEnable { get; }

    /// <summary>Gets the stencil write mask.</summary>
    public uint StencilWriteMask { get; }

    /// <summary>Gets the stencil compare function.</summary>
    public uint StencilFunc { get; }

    /// <summary>Gets the stencil reference value.</summary>
    public uint StencilRef { get; }

    /// <summary>Gets the stencil read mask.</summary>
    public uint StencilReadMask { get; }

    /// <summary>Gets the stencil action on stencil-test failure.</summary>
    public uint StencilOpFail { get; }

    /// <summary>Gets the stencil action on depth-test failure.</summary>
    public uint StencilOpZfail { get; }

    /// <summary>Gets the stencil action on depth-test pass.</summary>
    public uint StencilOpZpass { get; }

    /// <summary>Gets whether shader clip planes are active.</summary>
    public bool ZclipActive { get; }

    /// <summary>Gets the minimum clip depth.</summary>
    public float ZclipMin { get; }

    /// <summary>Gets the maximum clip depth.</summary>
    public float ZclipMax { get; }

    /// <summary>Gets whether out-of-range fragments are culled.</summary>
    public bool ZclipCull { get; }
}

/// <summary>One texture sampler slot's resolved registers for one draw.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphTextureSlot
{
    /// <summary>Initializes a texture-slot snapshot from the live per-stage texture registers.</summary>
    /// <param name="offset">Guest-physical texture base offset.</param>
    /// <param name="format">Raw format word (color id in bits 8-15, cubemap enable in bit 2).</param>
    /// <param name="address">Raw address word decoded by the U/V address-mode helpers.</param>
    /// <param name="control0">Raw control word 0 (enable in bit 30).</param>
    /// <param name="control1">Raw control word 1 (linear pitch in the high 16 bits).</param>
    /// <param name="imageRect">Packed extent: width in the high 16 bits, height in the low 16.</param>
    public PgraphTextureSlot(uint offset, uint format, uint address, uint control0, uint control1, uint imageRect)
    {
        Offset = offset;
        Format = format;
        Address = address;
        Control0 = control0;
        Control1 = control1;
        ImageRect = imageRect;
    }

    /// <summary>Gets the guest-physical texture base offset.</summary>
    public uint Offset { get; }

    /// <summary>Gets the raw format word (color id in bits 8-15, cubemap enable in bit 2).</summary>
    public uint Format { get; }

    /// <summary>Gets the raw address word decoded by the U/V address-mode helpers.</summary>
    public uint Address { get; }

    /// <summary>Gets the raw control word 0 (enable in bit 30).</summary>
    public uint Control0 { get; }

    /// <summary>Gets the raw control word 1 (linear pitch in the high 16 bits).</summary>
    public uint Control1 { get; }

    /// <summary>Gets the packed extent: width in the high 16 bits, height in the low 16.</summary>
    public uint ImageRect { get; }
}

/// <summary>
/// One draw: three transformed vertices plus the resolved surface, fragment,
/// fog, texture and combiner state the software path uses at this flush site.
/// The <see cref="Combiner"/> reference is the live instance and is only valid
/// for the duration of the synchronous callback.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphDrawInfo
{
    /// <summary>Initializes a draw record from the transformed triangle and the resolved state the software path uses.</summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="clipX">Clip rectangle origin x (surface clip low 16 bits).</param>
    /// <param name="clipY">Clip rectangle origin y (surface clip low 16 bits).</param>
    /// <param name="clipW">Clip rectangle width (surface clip high 16 bits).</param>
    /// <param name="clipH">Clip rectangle height (surface clip high 16 bits).</param>
    /// <param name="colorBytes">Color bytes per pixel.</param>
    /// <param name="zetaBytes">Zeta bytes per pixel.</param>
    /// <param name="combinerActive">Whether the final combiner is enabled for this draw.</param>
    /// <param name="fragment">Resolved fragment state.</param>
    /// <param name="texture0">Stage-0 texture slot.</param>
    /// <param name="texture1">Stage-1 texture slot.</param>
    /// <param name="texture2">Stage-2 texture slot.</param>
    /// <param name="texture3">Stage-3 texture slot.</param>
    /// <param name="combiner">Live combiner instance (valid only for the synchronous callback).</param>
    /// <param name="fogEnable">Whether fog blending runs.</param>
    /// <param name="fogR">Fog color red.</param>
    /// <param name="fogG">Fog color green.</param>
    /// <param name="fogB">Fog color blue.</param>
    /// <param name="fogA">Fog color alpha.</param>
    /// <param name="shaderClipMode">Raw shader clip-mode word (per-stage discard polarity).</param>
    /// <param name="zFormatFloat">Whether zeta values use the float format.</param>
    public PgraphDrawInfo(
        in PgraphVertex a, in PgraphVertex b, in PgraphVertex c,
        uint clipX, uint clipY, uint clipW, uint clipH,
        uint colorBytes, uint zetaBytes,
        bool combinerActive,
        in PgraphFragmentState fragment,
        in PgraphTextureSlot texture0, in PgraphTextureSlot texture1,
        in PgraphTextureSlot texture2, in PgraphTextureSlot texture3,
        PshCombiner? combiner,
        bool fogEnable, float fogR, float fogG, float fogB, float fogA,
        uint shaderClipMode,
        bool zFormatFloat)
    {
        A = a;
        B = b;
        C = c;
        ClipX = clipX;
        ClipY = clipY;
        ClipW = clipW;
        ClipH = clipH;
        ColorBytes = colorBytes;
        ZetaBytes = zetaBytes;
        CombinerActive = combinerActive;
        Fragment = fragment;
        Texture0 = texture0;
        Texture1 = texture1;
        Texture2 = texture2;
        Texture3 = texture3;
        Combiner = combiner;
        FogEnable = fogEnable;
        FogR = fogR;
        FogG = fogG;
        FogB = fogB;
        FogA = fogA;
        ShaderClipMode = shaderClipMode;
        ZFormatFloat = zFormatFloat;
    }

    /// <summary>Gets the first triangle vertex.</summary>
    public PgraphVertex A { get; }

    /// <summary>Gets the second triangle vertex.</summary>
    public PgraphVertex B { get; }

    /// <summary>Gets the third triangle vertex.</summary>
    public PgraphVertex C { get; }

    /// <summary>Gets the clip rectangle origin x (surface clip low 16 bits).</summary>
    public uint ClipX { get; }

    /// <summary>Gets the clip rectangle origin y (surface clip low 16 bits).</summary>
    public uint ClipY { get; }

    /// <summary>Gets the clip rectangle width (surface clip high 16 bits).</summary>
    public uint ClipW { get; }

    /// <summary>Gets the clip rectangle height (surface clip high 16 bits).</summary>
    public uint ClipH { get; }

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; }

    /// <summary>Gets the zeta bytes per pixel.</summary>
    public uint ZetaBytes { get; }

    /// <summary>Gets whether the final combiner is enabled for this draw.</summary>
    public bool CombinerActive { get; }

    /// <summary>Gets the resolved fragment state.</summary>
    public PgraphFragmentState Fragment { get; }

    /// <summary>Gets the stage-0 texture slot.</summary>
    public PgraphTextureSlot Texture0 { get; }

    /// <summary>Gets the stage-1 texture slot.</summary>
    public PgraphTextureSlot Texture1 { get; }

    /// <summary>Gets the stage-2 texture slot.</summary>
    public PgraphTextureSlot Texture2 { get; }

    /// <summary>Gets the stage-3 texture slot.</summary>
    public PgraphTextureSlot Texture3 { get; }

    /// <summary>Gets the live combiner instance (valid only for the synchronous callback).</summary>
    public PshCombiner? Combiner { get; }

    /// <summary>Gets whether fog blending runs.</summary>
    public bool FogEnable { get; }

    /// <summary>Gets the fog color red.</summary>
    public float FogR { get; }

    /// <summary>Gets the fog color green.</summary>
    public float FogG { get; }

    /// <summary>Gets the fog color blue.</summary>
    public float FogB { get; }

    /// <summary>Gets the fog color alpha.</summary>
    public float FogA { get; }

    /// <summary>Gets the raw shader clip-mode word (per-stage discard polarity).</summary>
    public uint ShaderClipMode { get; }

    /// <summary>Gets whether zeta values use the float format.</summary>
    public bool ZFormatFloat { get; }

    /// <summary>Gets the texture slot by index (0-3).</summary>
    public PgraphTextureSlot GetTexture(int slot)
    {
        return slot switch
        {
            0 => Texture0,
            1 => Texture1,
            2 => Texture2,
            3 => Texture3,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }
}

/// <summary>One surface clear the software path actually performed.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphClearInfo
{
    /// <summary>Initializes a clear record from the rectangle and values the software path filled.</summary>
    /// <param name="clearColor">Whether the color surface was filled.</param>
    /// <param name="clearZeta">Whether the zeta surface was filled.</param>
    /// <param name="xMin">Clear rectangle minimum x (inclusive).</param>
    /// <param name="yMin">Clear rectangle minimum y (inclusive).</param>
    /// <param name="xMax">Clear rectangle maximum x (inclusive).</param>
    /// <param name="yMax">Clear rectangle maximum y (inclusive).</param>
    /// <param name="clipX">Clip rectangle origin x (surface clip low 16 bits).</param>
    /// <param name="clipY">Clip rectangle origin y (surface clip low 16 bits).</param>
    /// <param name="clipW">Clip rectangle width (surface clip high 16 bits).</param>
    /// <param name="clipH">Clip rectangle height (surface clip high 16 bits).</param>
    /// <param name="colorValue">Raw color clear value.</param>
    /// <param name="zetaValue">Raw zeta clear value.</param>
    /// <param name="colorBytes">Color bytes per pixel.</param>
    /// <param name="zetaBytes">Zeta bytes per pixel (2 for Z16, 4 for Z24S8).</param>
    /// <param name="zFormatFloat">Whether zeta values use the float format.</param>
    public PgraphClearInfo(
        bool clearColor, bool clearZeta,
        uint xMin, uint yMin, uint xMax, uint yMax,
        uint clipX, uint clipY, uint clipW, uint clipH,
        uint colorValue, uint zetaValue,
        uint colorBytes, uint zetaBytes,
        bool zFormatFloat)
    {
        ClearColor = clearColor;
        ClearZeta = clearZeta;
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
        ClipX = clipX;
        ClipY = clipY;
        ClipW = clipW;
        ClipH = clipH;
        ColorValue = colorValue;
        ZetaValue = zetaValue;
        ColorBytes = colorBytes;
        ZetaBytes = zetaBytes;
        ZFormatFloat = zFormatFloat;
    }

    /// <summary>Gets whether the color surface was filled.</summary>
    public bool ClearColor { get; }

    /// <summary>Gets whether the zeta surface was filled.</summary>
    public bool ClearZeta { get; }

    /// <summary>Gets the clear rectangle minimum x (inclusive).</summary>
    public uint XMin { get; }

    /// <summary>Gets the clear rectangle minimum y (inclusive).</summary>
    public uint YMin { get; }

    /// <summary>Gets the clear rectangle maximum x (inclusive).</summary>
    public uint XMax { get; }

    /// <summary>Gets the clear rectangle maximum y (inclusive).</summary>
    public uint YMax { get; }

    /// <summary>Gets the clip rectangle origin x (surface clip low 16 bits).</summary>
    public uint ClipX { get; }

    /// <summary>Gets the clip rectangle origin y (surface clip low 16 bits).</summary>
    public uint ClipY { get; }

    /// <summary>Gets the clip rectangle width (surface clip high 16 bits).</summary>
    public uint ClipW { get; }

    /// <summary>Gets the clip rectangle height (surface clip high 16 bits).</summary>
    public uint ClipH { get; }

    /// <summary>Gets the raw color clear value.</summary>
    public uint ColorValue { get; }

    /// <summary>Gets the raw zeta clear value.</summary>
    public uint ZetaValue { get; }

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; }

    /// <summary>Gets the zeta bytes per pixel (2 for Z16, 4 for Z24S8).</summary>
    public uint ZetaBytes { get; }

    /// <summary>Gets whether zeta values use the float format.</summary>
    public bool ZFormatFloat { get; }
}

/// <summary>
/// Resolved at-flip surface record (same fields CapturePresentedFrame records,
/// plus the resolved guest-physical surface address for the present-upload path).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphSurfaceRecord
{
    /// <summary>Initializes an at-flip surface record from the presented surface state.</summary>
    /// <param name="width">Presented frame width (surface clip extent).</param>
    /// <param name="height">Presented frame height (surface clip extent).</param>
    /// <param name="colorBytes">Color bytes per pixel.</param>
    /// <param name="frame">Presented-frame sequence number.</param>
    /// <param name="surfaceAddress">Resolved guest-physical byte address of the presented surface.</param>
    /// <param name="pitch">Color-surface pitch in bytes (guest-row stride).</param>
    public PgraphSurfaceRecord(uint width, uint height, uint colorBytes, ulong frame, uint surfaceAddress, uint pitch)
    {
        Width = width;
        Height = height;
        ColorBytes = colorBytes;
        Frame = frame;
        SurfaceAddress = surfaceAddress;
        Pitch = pitch;
    }

    /// <summary>Gets the presented frame width (surface clip extent).</summary>
    public uint Width { get; }

    /// <summary>Gets the presented frame height (surface clip extent).</summary>
    public uint Height { get; }

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; }

    /// <summary>Gets the presented-frame sequence number.</summary>
    public ulong Frame { get; }

    /// <summary>Gets the resolved guest-physical byte address of the presented surface (DMA color-context base + color offset). Carried for the present-upload path; consumers must not change behavior on it yet.</summary>
    public uint SurfaceAddress { get; }

    /// <summary>Gets the color-surface pitch in bytes (the low 16 bits of the surface-pitch register), i.e. the guest-row stride the present-upload walk uses.</summary>
    public uint Pitch { get; }
}
