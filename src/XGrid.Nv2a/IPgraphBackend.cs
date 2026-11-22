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
    // ReSharper disable once UnusedMember.Global
    void Method(uint subchannel, uint method, uint parameter);

    /// <summary>
    /// Gets whether this backend consumes the per-method observation callback.
    /// The software rasterizer's caller already counts every method
    /// (<c>Nv2aMmio.MethodTotal</c>), so a backend that only wants a total can
    /// decline the callback and save a virtual call per PGRAPH method on
    /// method-heavy workloads (18M methods per 1B menu steps).
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    bool ObserveMethods => true;

    /// <summary>Submits one transformed triangle with the resolved state the software rasterizer uses.</summary>
    // ReSharper disable once UnusedMember.Global
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
    /// Opens one PGRAPH draw-command batch: every <see cref="TryDraw"/> between
    /// this call and <see cref="EndDrawBatch"/> belongs to the same primitive
    /// flush, so the guest cannot change PGRAPH state or guest RAM between
    /// them (the flush loop is host-side and runs to completion before the next
    /// method is processed). A backend may therefore resolve program, texture,
    /// state and uniform values once and submit the whole run with one host
    /// call. Draws outside a batch carry no such guarantee and must be handled
    /// one at a time. The default implementation ignores the boundary.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    void BeginDrawBatch()
    {
    }

    /// <summary>
    /// Closes the batch opened by <see cref="BeginDrawBatch"/>: the backend
    /// must submit anything it buffered before returning, because the caller
    /// may clear, flip, read back or reset immediately after.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    void EndDrawBatch()
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
/// <remarks>Initializes a transformed vertex from its position, colors, texture coordinates and fog factor.</remarks>
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
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphVertex(
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

    /// <summary>Gets the screen-space x-coordinate.</summary>
    public float X { get; } = x;

    /// <summary>Gets the screen-space y-coordinate.</summary>
    public float Y { get; } = y;

    /// <summary>Gets the screen-space depth.</summary>
    public float Z { get; } = z;

    /// <summary>Gets the screen-space w (perspective divide and projective texturing).</summary>
    public float W { get; } = w;

    /// <summary>Gets the diffuse red component.</summary>
    public float DiffuseR { get; } = diffuseR;

    /// <summary>Gets the diffuse green component.</summary>
    public float DiffuseG { get; } = diffuseG;

    /// <summary>Gets the diffuse blue component.</summary>
    public float DiffuseB { get; } = diffuseB;

    /// <summary>Gets the diffuse alpha component.</summary>
    public float DiffuseA { get; } = diffuseA;

    /// <summary>Gets the specular red component.</summary>
    public float SpecR { get; } = specR;

    /// <summary>Gets the specular green component.</summary>
    public float SpecG { get; } = specG;

    /// <summary>Gets the specular blue component.</summary>
    public float SpecB { get; } = specB;

    /// <summary>Gets the specular alpha component.</summary>
    public float SpecA { get; } = specA;

    /// <summary>Gets the stage-0 texture u coordinate.</summary>
    public float U0 { get; } = u0;

    /// <summary>Gets the stage-0 texture v coordinate.</summary>
    public float V0 { get; } = v0;

    /// <summary>Gets the stage-0 texture third coordinate.</summary>
    public float S0 { get; } = s0;

    /// <summary>Gets the stage-1 texture u coordinate.</summary>
    public float U1 { get; } = u1;

    /// <summary>Gets the stage-1 texture v coordinate.</summary>
    public float V1 { get; } = v1;

    /// <summary>Gets the stage-1 texture third coordinate.</summary>
    public float S1 { get; } = s1;

    /// <summary>Gets the stage-2 texture u coordinate.</summary>
    public float U2 { get; } = u2;

    /// <summary>Gets the stage-2 texture v coordinate.</summary>
    public float V2 { get; } = v2;

    /// <summary>Gets the stage-2 texture third coordinate.</summary>
    public float S2 { get; } = s2;

    /// <summary>Gets the stage-3 texture u coordinate.</summary>
    public float U3 { get; } = u3;

    /// <summary>Gets the stage-3 texture v coordinate.</summary>
    public float V3 { get; } = v3;

    /// <summary>Gets the stage-3 texture third coordinate.</summary>
    public float S3 { get; } = s3;

    /// <summary>Gets the stage-0 projective w divisor.</summary>
    public float T0W { get; } = t0W;

    /// <summary>Gets the stage-1 projective w divisor.</summary>
    public float T1W { get; } = t1W;

    /// <summary>Gets the stage-2 projective w divisor.</summary>
    public float T2W { get; } = t2W;

    /// <summary>Gets the stage-3 projective w divisor.</summary>
    public float T3W { get; } = t3W;

    /// <summary>Gets the interpolated fog factor.</summary>
    public float Fog { get; } = fog;
}

/// <summary>Resolved fragment (ROP) state snapshot for one draw or clear.</summary>
/// <remarks>Initializes a resolved fragment-state snapshot from the live PGRAPH registers.</remarks>
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
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphFragmentState(
    bool alphaTestEnable, uint alphaFunc, uint alphaRef,
    bool blendEnable, uint blendSrcFactor, uint blendDstFactor, uint blendColor, uint blendEquation,
    bool depthTestEnable, uint depthFunc, bool depthMask,
    uint colorMask,
    bool stencilTestEnable, uint stencilWriteMask, uint stencilFunc, uint stencilRef,
    uint stencilReadMask, uint stencilOpFail, uint stencilOpZfail, uint stencilOpZpass,
    bool zclipActive, float zclipMin, float zclipMax, bool zclipCull)
{

    /// <summary>Gets whether the alpha test runs.</summary>
    public bool AlphaTestEnable { get; } = alphaTestEnable;

    /// <summary>Gets the alpha compare function.</summary>
    public uint AlphaFunc { get; } = alphaFunc;

    /// <summary>Gets the alpha reference value.</summary>
    public uint AlphaRef { get; } = alphaRef;

    /// <summary>Gets whether blending runs.</summary>
    public bool BlendEnable { get; } = blendEnable;

    /// <summary>Gets the source blend factor.</summary>
    public uint BlendSrcFactor { get; } = blendSrcFactor;

    /// <summary>Gets the destination blend factor.</summary>
    public uint BlendDstFactor { get; } = blendDstFactor;

    /// <summary>Gets the constant blend color.</summary>
    public uint BlendColor { get; } = blendColor;

    /// <summary>Gets the blend equation.</summary>
    public uint BlendEquation { get; } = blendEquation;

    /// <summary>Gets whether the depth test runs.</summary>
    public bool DepthTestEnable { get; } = depthTestEnable;

    /// <summary>Gets the depth compare function.</summary>
    public uint DepthFunc { get; } = depthFunc;

    /// <summary>Gets whether passing depth writes the zeta buffer.</summary>
    public bool DepthMask { get; } = depthMask;

    /// <summary>Gets the per-channel color write mask.</summary>
    public uint ColorMask { get; } = colorMask;

    /// <summary>Gets whether the stencil test runs.</summary>
    public bool StencilTestEnable { get; } = stencilTestEnable;

    /// <summary>Gets the stencil write mask.</summary>
    public uint StencilWriteMask { get; } = stencilWriteMask;

    /// <summary>Gets the stencil compare function.</summary>
    public uint StencilFunc { get; } = stencilFunc;

    /// <summary>Gets the stencil reference value.</summary>
    public uint StencilRef { get; } = stencilRef;

    /// <summary>Gets the stencil read mask.</summary>
    public uint StencilReadMask { get; } = stencilReadMask;

    /// <summary>Gets the stencil action on stencil-test failure.</summary>
    public uint StencilOpFail { get; } = stencilOpFail;

    /// <summary>Gets the stencil action on depth-test failure.</summary>
    public uint StencilOpZfail { get; } = stencilOpZfail;

    /// <summary>Gets the stencil action on depth-test pass.</summary>
    public uint StencilOpZpass { get; } = stencilOpZpass;

    /// <summary>Gets whether shader clip planes are active.</summary>
    public bool ZclipActive { get; } = zclipActive;

    /// <summary>Gets the minimum clip depth.</summary>
    public float ZclipMin { get; } = zclipMin;

    /// <summary>Gets the maximum clip depth.</summary>
    public float ZclipMax { get; } = zclipMax;

    /// <summary>Gets whether out-of-range fragments are culled.</summary>
    public bool ZclipCull { get; } = zclipCull;
}

/// <summary>
/// One guest texel word read the computed cube/dot texture modes need
/// (B3 slice 2): a swizzled cube face texel or a 16-bit HILO word is a guest
/// RAM read at an address no host texture object can address, so the seam
/// carries the words themselves - the product's one guest-memory reader
/// (<see cref="Nv2AMmio"/>, through <c>IGpuMemory</c>) resolves each address
/// and the backend compares the top of its stack against the address it needs.
/// The set stays small because consecutive draws read the same cube faces
/// (the reflection direction changes per fragment, the faces do not).
/// </summary>
/// <remarks>Initializes one carried guest texel word.</remarks>
/// <param name="address">Guest-physical byte address the software sampler reads.</param>
/// <param name="word">The 32-bit word at that address (a 16-bit format masks on use).</param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphGuestTexel(uint address, uint word)
{
    /// <summary>Gets the guest-physical byte address the software sampler reads.</summary>
    public uint Address { get; } = address;

    /// <summary>Gets the 32-bit little-endian word at <see cref="Address"/>.</summary>
    public uint Word { get; } = word;
}

/// <summary>
/// The guest texel words one draw command's computed texture modes read. The
/// backend resolves the same addresses it would sample (cube face swizzle, HILO
/// input texel) against the top of this record at draw time; a miss means the
/// set was sized too small and the draw is declined rather than sampled wrong.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphGuestTexelSet
{
    /// <summary>Gets the constant pool size: the most recent entries a draw may reference.</summary>
    public const int PoolSize = 32;

    /// <summary>Initializes the carried set from a contiguous address/word run.</summary>
    /// <param name="addresses">The addresses, most recently added last.</param>
    /// <param name="words">The word at each address, same order.</param>
    public PgraphGuestTexelSet(ArraySegment<uint> addresses, ArraySegment<uint> words)
    {
        Addresses = addresses;
        Words = words;
    }

    /// <summary>Gets the number of carried words.</summary>
    public int Count => Addresses.Count;

    /// <summary>
    /// Gets an empty set: a draw command whose software fill sampled no guest
    /// word. It is not a decline - a cube face is decoded from guest RAM by the
    /// backend, and only the pool-backed HILO inputs need carried words.
    /// </summary>
    public static PgraphGuestTexelSet Empty => default;

    /// <summary>Gets the carried addresses.</summary>
    public ArraySegment<uint> Addresses { get; }

    /// <summary>Gets the carried words, matching <see cref="Addresses"/>.</summary>
    public ArraySegment<uint> Words { get; }
}

/// <summary>
/// The BUMPENVMAP per-stage state the software sampler's <c>ResolveBump</c>
/// reads: stages 1-3 each carry a 2x2 matrix (x,y = row 0/1 against the
/// dependent input's blue channel; z,w = row 0/1 against green) and the
/// luminance variant's scale/offset pair (<c>texel *= scale * input.r + offset</c>).
/// A GPU backend gets the same values through the seam instead of re-reading
/// the PGRAPH registers.
/// </summary>
/// <remarks>Initializes the bump state.</remarks>
/// <param name="matrix">Twelve coefficients: stage 1-3, four each (x, y, z, w).</param>
/// <param name="luminanceScale">Three luminance scales (stages 1-3).</param>
/// <param name="luminanceOffset">Three luminance offsets (stages 1-3).</param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphBumpState(
    float[] matrix, float[] luminanceScale, float[] luminanceOffset)
{
    /// <summary>Gets the matrix coefficients: stage 1-3, four per stage.</summary>
    public float[] Matrix { get; } = matrix;

    /// <summary>Gets the luminance scales (stages 1-3).</summary>
    public float[] LuminanceScale { get; } = luminanceScale;

    /// <summary>Gets the luminance offsets (stages 1-3).</summary>
    public float[] LuminanceOffset { get; } = luminanceOffset;
}

/// <summary>One texture sampler slot's resolved registers for one draw.</summary>
/// <remarks>Initializes a texture-slot snapshot from the live per-stage texture registers.</remarks>
/// <param name="offset">Guest-physical texture base offset.</param>
/// <param name="format">Raw format word (color id in bits 8-15, cubemap enable in bit 2).</param>
/// <param name="address">Raw address word decoded by the U/V address-mode helpers.</param>
/// <param name="control0">Raw control word 0 (enable in bit 30).</param>
/// <param name="control1">Raw control word 1 (linear pitch in the high 16 bits).</param>
/// <param name="imageRect">Packed extent: width in the high 16 bits, height in the low 16.</param>
/// <param name="filterBilinear">Whether the sampler honors a TEXFILTER tent at LOD 0 for this slot (false when the sampler is nearest-only).</param>
/// <param name="filterConvolution">Effective TEXFILTER convolution kernel for PROJECT2D (0 none, 1 quincunx, 2 gaussian-3).</param>
/// <param name="samplerWrapU">Resolved U address mode: 0 wrap, 1 mirror, 2 clamp, 3 clamp-to-border.</param>
/// <param name="samplerWrapV">Resolved V address mode: 0 wrap, 1 mirror, 2 clamp, 3 clamp-to-border.</param>
/// <param name="borderColor">Raw NV097_SET_TEXTURE_BORDER_COLOR word (ARGB, xemu argb-pack order).</param>
/// <param name="swizzleMaskX">Morton bit-spread mask for the texel x index (swizzled formats; the same mask the software sampler uses).</param>
/// <param name="swizzleMaskY">Morton bit-spread mask for the texel y index.</param>
/// <param name="mortonMasks">Whether the masks are the standard alternating Morton pair (branchless spread) rather than the generic interleave loop.</param>
/// <param name="cubeFaceStride">Cube-face byte stride (faces contiguous, full mip chain rounded to 128 bytes; 0 when not a cube-sampling slot).</param>
/// <param name="cubeWidth">Cube face width in texels (TEXFMT BASE_SIZE_U), 0 when not a cube-sampling slot.</param>
/// <param name="cubeHeight">Cube face height in texels (TEXFMT BASE_SIZE_V).</param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphTextureSlot(
    uint offset, uint format, uint address, uint control0, uint control1, uint imageRect,
    bool filterBilinear = false, int filterConvolution = 0,
    int samplerWrapU = 0, int samplerWrapV = 0, uint borderColor = 0,
    uint swizzleMaskX = 0, uint swizzleMaskY = 0, bool mortonMasks = false,
    uint cubeFaceStride = 0, int cubeWidth = 0, int cubeHeight = 0)
{

    /// <summary>Gets the guest-physical texture base offset.</summary>
    public uint Offset { get; } = offset;

    /// <summary>Gets the raw format word (color id in bits 8-15, cubemap enable in bit 2).</summary>
    public uint Format { get; } = format;

    /// <summary>Gets the raw address word decoded by the U/V address-mode helpers.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    // ReSharper disable once UnusedMember.Global
    public uint Address { get; } = address;

    /// <summary>Gets the raw control word 0 (enable in bit 30).</summary>
    public uint Control0 { get; } = control0;

    /// <summary>Gets the raw control word 1 (linear pitch in the high 16 bits).</summary>
    public uint Control1 { get; } = control1;

    /// <summary>Gets the packed extent: width in the high 16 bits, height in the low 16.</summary>
    public uint ImageRect { get; } = imageRect;

    /// <summary>
    /// Gets whether the sampler honors a TEXFILTER tent filter at LOD 0 for
    /// this slot (the software <c>SamplerPlan.Bilinear</c> rule). Always false
    /// unless the caller opted into the TEXFILTER registers, so a backend that
    /// keys on it keeps the legacy nearest sampler by default.
    /// </summary>
    public bool FilterBilinear { get; } = filterBilinear;

    /// <summary>
    /// Gets the effective TEXFILTER convolution kernel for PROJECT2D: 0 none,
    /// 1 quincunx, 2 gaussian-3 (the software <c>SamplerPlan.Convolution</c>
    /// rule, which only applies when MIN selects CONVOLUTION_2D_LOD0).
    /// </summary>
    public int FilterConvolution { get; } = filterConvolution;

    /// <summary>
    /// Gets the resolved U address mode in the sampler's own encoding: 0 wrap,
    /// 1 mirror, 2 clamp-to-edge, 3 clamp-to-border. This is the decoded
    /// NV097_SET_TEXTURE_ADDRESS value (1 wrap, 2 mirror, 3 clamp, 4 border),
    /// not the raw word, so a backend cannot repeat the raw-word mapping.
    /// </summary>
    public int SamplerWrapU { get; } = samplerWrapU;

    /// <summary>Gets the resolved V address mode (same encoding as <see cref="SamplerWrapU"/>).</summary>
    public int SamplerWrapV { get; } = samplerWrapV;

    /// <summary>Gets the raw NV097_SET_TEXTURE_BORDER_COLOR word (ARGB, xemu argb-pack order).</summary>
    public uint BorderColor { get; } = borderColor;

    /// <summary>
    /// Gets the Morton bit-spread mask for the texel x index of a swizzled
    /// texture (the software sampler's <c>InterleaveBits(x, maskX)</c>).
    /// A backend that decodes the guest's swizzled texels itself needs it to
    /// reproduce the exact texel address, so the mask never has to be
    /// recomputed from the extent (which the software path derives from TEXFMT
    /// BASE_SIZE for swizzled formats, not from TEXIMAGERECT).
    /// </summary>
    public uint SwizzleMaskX { get; } = swizzleMaskX;

    /// <summary>Gets the Morton bit-spread mask for the texel y index of a swizzled texture.</summary>
    public uint SwizzleMaskY { get; } = swizzleMaskY;

    /// <summary>
    /// Gets whether <see cref="SwizzleMaskX"/>/<see cref="SwizzleMaskY"/> are the
    /// standard alternating Morton pair. When true the spread is branchless;
    /// when false the generic bit-interleave loop applies (same result, slower).
    /// </summary>
    public bool MortonMasks { get; } = mortonMasks;

    /// <summary>
    /// Gets the cube-face byte stride: faces are stored contiguously in the
    /// guest's +X,-X,+Y,-Y,+Z,-Z order, each face a full mip chain rounded up
    /// to the 128-byte face alignment. 0 when the slot is not cube-sampling
    /// (a 0 cube extent means the same).
    /// </summary>
    public uint CubeFaceStride { get; } = cubeFaceStride;

    /// <summary>Gets the cube face width in texels (TEXFMT BASE_SIZE_U); 0 when the slot is not cube-sampling.</summary>
    public int CubeWidth { get; } = cubeWidth;

    /// <summary>Gets the cube face height in texels (TEXFMT BASE_SIZE_V); 0 when the slot is not cube-sampling.</summary>
    public int CubeHeight { get; } = cubeHeight;

    /// <summary>
    /// Gets whether this slot carries a complete cube-face description (a
    /// non-zero face extent and stride). A backend that acts on this never has
    /// to guess the software sampler's cube rules (swizzled formats only,
    /// extent from TEXFMT BASE_SIZE rather than TEXIMAGERECT).
    /// </summary>
    public bool CubeSamplingReady => CubeWidth > 0 && CubeHeight > 0 && CubeFaceStride != 0;
}

/// <summary>
/// One draw: three transformed vertices plus the resolved surface, fragment,
/// fog, texture and combiner state the software path uses at this flush site.
/// The <see cref="Combiner"/> reference is the live instance and is only valid
/// for the duration of the synchronous callback.
/// </summary>
/// <remarks>Initializes a draw record from the transformed triangle and the resolved state the software path uses.</remarks>
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
/// <param name="colorSurfaceAddress">Guest-physical color-surface pixel (0,0): surface base + presented color offset.</param>
/// <param name="colorPitch">Color-surface row pitch in bytes (surface pitch low 16 bits).</param>
/// <param name="eyex">Eye-space camera x (NV097_SET_EYE_POSITION), read by the specular reflection texture modes.</param>
/// <param name="eyey">Eye-space camera y.</param>
/// <param name="eyez">Eye-space camera z.</param>
/// <param name="guestTexels">Guest texel words the computed cube/dot texture modes read (null when none apply).</param>
/// <param name="bump">The BUMPENVMAP per-stage matrices and luminance pairs.</param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphDrawInfo(
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
    bool zFormatFloat,
    uint colorSurfaceAddress,
    uint colorPitch,
    float eyex = 0f,
    float eyey = 0f,
    float eyez = 0f,
    PgraphGuestTexelSet? guestTexels = null,
    PgraphBumpState bump = default)
{

    /// <summary>Gets the first triangle vertex.</summary>
    public PgraphVertex A { get; } = a;

    /// <summary>Gets the second triangle vertex.</summary>
    public PgraphVertex B { get; } = b;

    /// <summary>Gets the third triangle vertex.</summary>
    public PgraphVertex C { get; } = c;

    /// <summary>Gets the clip rectangle origin x (surface clip low 16 bits).</summary>
    public uint ClipX { get; } = clipX;

    /// <summary>Gets the clip rectangle origin y (surface clip low 16 bits).</summary>
    public uint ClipY { get; } = clipY;

    /// <summary>Gets the clip rectangle width (surface clip high 16 bits).</summary>
    public uint ClipW { get; } = clipW;

    /// <summary>Gets the clip rectangle height (surface clip high 16 bits).</summary>
    public uint ClipH { get; } = clipH;

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; } = colorBytes;

    /// <summary>Gets the zeta bytes per pixel.</summary>
    public uint ZetaBytes { get; } = zetaBytes;

    /// <summary>Gets whether the final combiner is enabled for this draw.</summary>
    public bool CombinerActive { get; } = combinerActive;

    /// <summary>Gets the resolved fragment state.</summary>
    public PgraphFragmentState Fragment { get; } = fragment;

    /// <summary>Gets the stage-0 texture slot.</summary>
    public PgraphTextureSlot Texture0 { get; } = texture0;

    /// <summary>Gets the stage-1 texture slot.</summary>
    private PgraphTextureSlot Texture1 { get; } = texture1;

    /// <summary>Gets the stage-2 texture slot.</summary>
    public PgraphTextureSlot Texture2 { get; } = texture2;

    /// <summary>Gets the stage-3 texture slot.</summary>
    private PgraphTextureSlot Texture3 { get; } = texture3;

    /// <summary>Gets the live combiner instance (valid only for the synchronous callback).</summary>
    public PshCombiner? Combiner { get; } = combiner;

    /// <summary>Gets whether fog blending runs.</summary>
    public bool FogEnable { get; } = fogEnable;

    /// <summary>Gets the fog color red.</summary>
    public float FogR { get; } = fogR;

    /// <summary>Gets the fog color green.</summary>
    public float FogG { get; } = fogG;

    /// <summary>Gets the fog color blue.</summary>
    public float FogB { get; } = fogB;

    /// <summary>Gets the fog color alpha.</summary>
    public float FogA { get; } = fogA;

    /// <summary>Gets the raw shader clip-mode word (per-stage discard polarity).</summary>
    public uint ShaderClipMode { get; } = shaderClipMode;

    /// <summary>Gets whether zeta values use the float format.</summary>
    public bool ZFormatFloat { get; } = zFormatFloat;

    /// <summary>
    /// Gets the guest-physical address of the color surface's pixel (0,0)
    /// (surface base + presented color offset). A clip-window pixel (x, y) is
    /// at <c>ColorSurfaceAddress + ((ClipY + y) * ColorPitch) + (x * 4)</c>:
    /// the same expression the software fill uses. A GPU backend that has to
    /// hand a frame back to the software rasterizer composes its image through
    /// this address.
    /// </summary>
    public uint ColorSurfaceAddress { get; } = colorSurfaceAddress;

    /// <summary>Gets the color-surface row pitch in bytes (surface pitch low 16 bits).</summary>
    public uint ColorPitch { get; } = colorPitch;

    /// <summary>
    /// Gets the eye-space camera x (NV097_SET_EYE_POSITION). The specular
    /// reflection texture mode reflects the eye vector `p - eye` about the
    /// stage normal, so a backend that honors that mode needs the same camera
    /// the software sampler reads.
    /// </summary>
    public float EyeX { get; } = eyex;

    /// <summary>Gets the eye-space camera y.</summary>
    public float EyeY { get; } = eyey;

    /// <summary>Gets the eye-space camera z.</summary>
    public float EyeZ { get; } = eyez;

    /// <summary>
    /// Gets the guest texel words the computed cube/dot texture modes read for
    /// this draw, or null when the combiner needs none. The backend may only
    /// reference an address whose final guest state it has been handed: the set
    /// is built from the draw command that is about to run (see
    /// <see cref="Nv2AMmio.GuestTexelsFor"/>) or from this draw command's own
    /// tail once the flush resolved its texcoords. A draw that samples anything
    /// outside the set must decline, never read a stale entry.
    /// </summary>
    public PgraphGuestTexelSet? GuestTexels { get; } = guestTexels;

    /// <summary>
    /// Gets the BUMPENVMAP per-stage state (matrices + the luminance scale and
    /// offset pairs) the software <c>ResolveBump</c> applies, so a backend
    /// perturbs the coordinate and scales the texel with the same numbers.
    /// <see cref="PgraphBumpState.Matrix"/> is null when the source did not
    /// carry it; readers treat that as all zeros.
    /// </summary>
    public PgraphBumpState Bump { get; } = bump.Matrix is null ? EmptyBump : bump;

    private static readonly float[] EmptyBump12 = new float[12];
    private static readonly float[] EmptyBump3 = new float[3];
    private static readonly PgraphBumpState EmptyBump = new(EmptyBump12, EmptyBump3, EmptyBump3);

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

    /// <summary>
    /// Returns this draw command's state with a different triangle: the
    /// fragment state, texture slots, combiner, fog, shader clip mode, zeta
    /// format, color surface, eye and bump state all carry over, so a backend
    /// consuming the successive triangles of one draw command does not have to
    /// re-derive them. Only the vertices, the clip rectangle and the
    /// per-command guest texel set are replaced.
    /// </summary>
    /// <param name="a">The first triangle vertex.</param>
    /// <param name="b">The second triangle vertex.</param>
    /// <param name="c">The third triangle vertex.</param>
    /// <param name="clipX">The clip rectangle origin x.</param>
    /// <param name="clipY">The clip rectangle origin y.</param>
    /// <param name="clipW">The clip rectangle width.</param>
    /// <param name="clipH">The clip rectangle height.</param>
    /// <param name="colorBytes">The color bytes per pixel.</param>
    /// <param name="zetaBytes">The zeta bytes per pixel.</param>
    /// <param name="combinerActive">Whether the final combiner is enabled.</param>
    /// <param name="guestTexels">The guest texel words this draw may reference.</param>
    /// <returns>The draw record for the given triangle.</returns>
    public PgraphDrawInfo WithTriangle(
        in PgraphVertex a, in PgraphVertex b, in PgraphVertex c,
        uint clipX, uint clipY, uint clipW, uint clipH,
        uint colorBytes, uint zetaBytes, bool combinerActive, PgraphGuestTexelSet? guestTexels)
    {
        return new PgraphDrawInfo(
            in a, in b, in c,
            clipX, clipY, clipW, clipH,
            colorBytes, zetaBytes, combinerActive,
            Fragment,
            Texture0, Texture1, Texture2, Texture3,
            Combiner,
            FogEnable, FogR, FogG, FogB, FogA,
            ShaderClipMode,
            ZFormatFloat,
            ColorSurfaceAddress,
            ColorPitch,
            EyeX,
            EyeY,
            EyeZ,
            guestTexels,
            Bump);
    }
}

/// <summary>One surface clear the software path actually performed.</summary>
/// <remarks>Initializes a clear record from the rectangle and values the software path filled.</remarks>
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
/// <param name="colorChannels">
/// Color channels the clear writes (bit 0 R, 1 G, 2 B, 3 A, from
/// <c>NV097_CLEAR_SURFACE_R/G/B/A</c>). A channel whose bit is clear keeps the
/// surface's previous value.
/// </param>
/// <param name="zetaFields">
/// Zeta fields the clear writes (bit 0 depth, bit 1 stencil, from
/// <c>NV097_CLEAR_SURFACE_Z/STENCIL</c>). A field whose bit is clear keeps the
/// surface's previous value.
/// </param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphClearInfo(
    bool clearColor, bool clearZeta,
    uint xMin, uint yMin, uint xMax, uint yMax,
    uint clipX, uint clipY, uint clipW, uint clipH,
    uint colorValue, uint zetaValue,
    uint colorBytes, uint zetaBytes,
    bool zFormatFloat,
    uint colorChannels = 0xFu, uint zetaFields = 0x3u)
{

    /// <summary>Gets whether the color surface was filled.</summary>
    public bool ClearColor { get; } = clearColor;

    /// <summary>Gets whether the zeta surface was filled.</summary>
    public bool ClearZeta { get; } = clearZeta;

    /// <summary>Gets the clear rectangle minimum x (inclusive).</summary>
    public uint XMin { get; } = xMin;

    /// <summary>Gets the clear rectangle minimum y (inclusive).</summary>
    public uint YMin { get; } = yMin;

    /// <summary>Gets the clear rectangle maximum x (inclusive).</summary>
    public uint XMax { get; } = xMax;

    /// <summary>Gets the clear rectangle maximum y (inclusive).</summary>
    public uint YMax { get; } = yMax;

    /// <summary>Gets the clip rectangle origin x (surface clip low 16 bits).</summary>
    public uint ClipX { get; } = clipX;

    /// <summary>Gets the clip rectangle origin y (surface clip low 16 bits).</summary>
    public uint ClipY { get; } = clipY;

    /// <summary>Gets the clip rectangle width (surface clip high 16 bits).</summary>
    public uint ClipW { get; } = clipW;

    /// <summary>Gets the clip rectangle height (surface clip high 16 bits).</summary>
    public uint ClipH { get; } = clipH;

    /// <summary>Gets the raw color clear value.</summary>
    public uint ColorValue { get; } = colorValue;

    /// <summary>Gets the raw zeta clear value.</summary>
    public uint ZetaValue { get; } = zetaValue;

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; } = colorBytes;

    /// <summary>Gets the zeta bytes per pixel (2 for Z16, 4 for Z24S8).</summary>
    public uint ZetaBytes { get; } = zetaBytes;

    /// <summary>Gets whether zeta values use the float format.</summary>
    public bool ZFormatFloat { get; } = zFormatFloat;

    /// <summary>
    /// Gets the color channels the clear writes (bit 0 R, 1 G, 2 B, 3 A). A
    /// clear channel is written; an unmasked channel keeps its previous value.
    /// </summary>
    public uint ColorChannels { get; } = colorChannels;

    /// <summary>
    /// Gets the zeta fields the clear writes (bit 0 depth, bit 1 stencil). A
    /// cleared field is written; an unmasked field keeps its previous value.
    /// </summary>
    public uint ZetaFields { get; } = zetaFields;
}

/// <summary>
/// Resolved at-flip surface record (same fields CapturePresentedFrame records,
/// plus the resolved guest-physical surface address for the present-upload path).
/// </summary>
/// <remarks>Initializes an at-flip surface record from the presented surface state.</remarks>
/// <param name="width">Presented frame width (surface clip extent).</param>
/// <param name="height">Presented frame height (surface clip extent).</param>
/// <param name="colorBytes">Color bytes per pixel.</param>
/// <param name="frame">Presented-frame sequence number.</param>
/// <param name="surfaceAddress">Resolved guest-physical byte address of the presented surface.</param>
/// <param name="pitch">Color-surface pitch in bytes (guest-row stride).</param>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphSurfaceRecord(uint width, uint height, uint colorBytes, ulong frame, uint surfaceAddress, uint pitch)
{

    /// <summary>Gets the presented frame width (surface clip extent).</summary>
    public uint Width { get; } = width;

    /// <summary>Gets the presented frame height (surface clip extent).</summary>
    public uint Height { get; } = height;

    /// <summary>Gets the color bytes per pixel.</summary>
    public uint ColorBytes { get; } = colorBytes;

    /// <summary>Gets the presented-frame sequence number.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    // ReSharper disable once UnusedMember.Global
    public ulong Frame { get; } = frame;

    /// <summary>Gets the resolved guest-physical byte address of the presented surface (DMA color-context base + color offset). Carried for the present-upload path; consumers must not change behavior on it yet.</summary>
    public uint SurfaceAddress { get; } = surfaceAddress;

    /// <summary>Gets the color-surface pitch in bytes (the low 16 bits of the surface-pitch register), i.e. the guest-row stride the present-upload walk uses.</summary>
    public uint Pitch { get; } = pitch;
}
