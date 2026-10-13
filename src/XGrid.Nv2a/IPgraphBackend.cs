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
}

/// <summary>One screen-space transformed vertex (outputs of the CPU VshProgram path).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphVertex
{
    public PgraphVertex(
        float x, float y, float z, float w,
        float diffuseR, float diffuseG, float diffuseB, float diffuseA,
        float specR, float specG, float specB, float specA,
        float u0, float v0, float s0,
        float u1, float v1, float s1,
        float u2, float v2, float s2,
        float u3, float v3, float s3,
        float t0w, float t1w, float t2w, float t3w,
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
        T0W = t0w;
        T1W = t1w;
        T2W = t2w;
        T3W = t3w;
        Fog = fog;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
    public float DiffuseR { get; }
    public float DiffuseG { get; }
    public float DiffuseB { get; }
    public float DiffuseA { get; }
    public float SpecR { get; }
    public float SpecG { get; }
    public float SpecB { get; }
    public float SpecA { get; }
    public float U0 { get; }
    public float V0 { get; }
    public float S0 { get; }
    public float U1 { get; }
    public float V1 { get; }
    public float S1 { get; }
    public float U2 { get; }
    public float V2 { get; }
    public float S2 { get; }
    public float U3 { get; }
    public float V3 { get; }
    public float S3 { get; }
    public float T0W { get; }
    public float T1W { get; }
    public float T2W { get; }
    public float T3W { get; }
    public float Fog { get; }
}

/// <summary>Resolved fragment (ROP) state snapshot for one draw or clear.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphFragmentState
{
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

    public bool AlphaTestEnable { get; }
    public uint AlphaFunc { get; }
    public uint AlphaRef { get; }
    public bool BlendEnable { get; }
    public uint BlendSrcFactor { get; }
    public uint BlendDstFactor { get; }
    public uint BlendColor { get; }
    public uint BlendEquation { get; }
    public bool DepthTestEnable { get; }
    public uint DepthFunc { get; }
    public bool DepthMask { get; }
    public uint ColorMask { get; }
    public bool StencilTestEnable { get; }
    public uint StencilWriteMask { get; }
    public uint StencilFunc { get; }
    public uint StencilRef { get; }
    public uint StencilReadMask { get; }
    public uint StencilOpFail { get; }
    public uint StencilOpZfail { get; }
    public uint StencilOpZpass { get; }
    public bool ZclipActive { get; }
    public float ZclipMin { get; }
    public float ZclipMax { get; }
    public bool ZclipCull { get; }
}

/// <summary>One texture sampler slot's resolved registers for one draw.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphTextureSlot
{
    public PgraphTextureSlot(uint offset, uint format, uint address, uint control0, uint control1, uint imageRect)
    {
        Offset = offset;
        Format = format;
        Address = address;
        Control0 = control0;
        Control1 = control1;
        ImageRect = imageRect;
    }

    public uint Offset { get; }
    public uint Format { get; }
    public uint Address { get; }
    public uint Control0 { get; }
    public uint Control1 { get; }
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

    public PgraphVertex A { get; }
    public PgraphVertex B { get; }
    public PgraphVertex C { get; }
    public uint ClipX { get; }
    public uint ClipY { get; }
    public uint ClipW { get; }
    public uint ClipH { get; }
    public uint ColorBytes { get; }
    public uint ZetaBytes { get; }
    public bool CombinerActive { get; }
    public PgraphFragmentState Fragment { get; }
    public PgraphTextureSlot Texture0 { get; }
    public PgraphTextureSlot Texture1 { get; }
    public PgraphTextureSlot Texture2 { get; }
    public PgraphTextureSlot Texture3 { get; }
    public PshCombiner? Combiner { get; }
    public bool FogEnable { get; }
    public float FogR { get; }
    public float FogG { get; }
    public float FogB { get; }
    public float FogA { get; }
    public uint ShaderClipMode { get; }
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

    public bool ClearColor { get; }
    public bool ClearZeta { get; }
    public uint XMin { get; }
    public uint YMin { get; }
    public uint XMax { get; }
    public uint YMax { get; }
    public uint ClipX { get; }
    public uint ClipY { get; }
    public uint ClipW { get; }
    public uint ClipH { get; }
    public uint ColorValue { get; }
    public uint ZetaValue { get; }
    public uint ColorBytes { get; }
    public uint ZetaBytes { get; }
    public bool ZFormatFloat { get; }
}

/// <summary>Resolved at-flip surface record (same fields CapturePresentedFrame records).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PgraphSurfaceRecord
{
    public PgraphSurfaceRecord(uint width, uint height, uint colorBytes, ulong frame)
    {
        Width = width;
        Height = height;
        ColorBytes = colorBytes;
        Frame = frame;
    }

    public uint Width { get; }
    public uint Height { get; }
    public uint ColorBytes { get; }
    public ulong Frame { get; }
}
