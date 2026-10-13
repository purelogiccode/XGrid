using System;
using System.IO;
using XGrid.Core;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A register-combiner (pixel shader) stage for the software rasterizer.
/// Semantics follow xemu <c>hw/xbox/nv2a/pgraph/glsl/psh.c</c> (input/output
/// word decode, input mappings, AB/CD multiply vs dot-product, MUX select on
/// r0.a, output mappings, r0 init, V1R0_SUM / EF_PROD, final-combiner lerp,
/// texture-mode texel resolution, dot-map decoding, bump/time/dependent math)
/// with state layout from <c>nv2a_regs.h</c> (NV097_SET_COMBINER_* methods)
/// and <c>psh_regs.h</c> (PS_* enums). Facts only; no reference code is copied.
/// The FOG register value itself is supplied per fragment by the caller
/// (fog color + interpolated fog factor); see <c>Nv2aMmio</c> fog state.
/// Hardware texture modes not backed by the software rasterizer degrade to
/// NONE with a diagnostic count; the mode equations stay here with the rest
/// of the fragment math. BRDF and DOT_RFLCT_SPEC_CONST are xemu-equivalent
/// stubs (reference emits a zero texel plus UNIMPLEMENTED; no other reliable
/// reference exists). Fixed-function fog-coordinate generation lives in
/// <c>VshProgram.FogGenDistance</c>.
/// </summary>
public sealed class PshCombiner : ISnapshotable
{
    /// <summary>Maximum general combiner stages (8, per CONTROL low byte).</summary>
    public const int MaxStages = 8;

    /// <summary>Texture slots consumed by combiner stages.</summary>
    public const int TextureSlots = 4;

    // Register ids (psh_regs.h PS_REGISTER).
    public const int RegZero = 0x0;
    public const int RegC0 = 0x1;
    public const int RegC1 = 0x2;
    public const int RegFog = 0x3;
    public const int RegV0 = 0x4;
    public const int RegV1 = 0x5;
    public const int RegT0 = 0x8;
    public const int RegR0 = 0xC;
    public const int RegR1 = 0xD;
    public const int RegV1R0Sum = 0xE;
    public const int RegEfProd = 0xF;

    // Input mappings (psh_regs.h PS_INPUTMAPPING).
    public const int MapUnsignedIdentity = 0x00;
    public const int MapUnsignedInvert = 0x20;
    public const int MapExpandNormal = 0x40;
    public const int MapExpandNegate = 0x60;
    public const int MapHalfbiasNormal = 0x80;
    public const int MapHalfbiasNegate = 0xA0;
    public const int MapSignedIdentity = 0xC0;
    public const int MapSignedNegate = 0xE0;

    // Output mappings (psh_regs.h PS_COMBINEROUTPUT).
    public const int OutIdentity = 0x00;
    public const int OutBias = 0x08;
    public const int OutShiftLeft1 = 0x10;
    public const int OutShiftLeft1Bias = 0x18;
    public const int OutShiftLeft2 = 0x20;
    public const int OutShiftRight1 = 0x30;

    // Output flag bits within an OCW word.
    public const uint FlagCdDot = 1u;
    public const uint FlagAbDot = 2u;
    public const uint FlagMux = 4u;
    public const uint FlagCdBlueToAlpha = 0x40u;
    public const uint FlagAbBlueToAlpha = 0x80u;

    // CONTROL flag bits (psh_regs.h PS_COMBINERCOUNTFLAGS), above the low stage-count byte.
    public const uint ControlMuxMsb = 0x1u;
    public const uint ControlUniqueC0 = 0x10u;
    public const uint ControlUniqueC1 = 0x100u;

    // Final-combiner setting bits (low byte of final CW1).
    public const uint FinalClampSum = 0x80u;
    public const uint FinalComplementV1 = 0x40u;
    public const uint FinalComplementR0 = 0x20u;

    /// <summary>Texture mode: stage produces (0,0,0,1).</summary>
    public const int TexModeNone = 0x00;

    /// <summary>Texture mode: sample the bound texture with the interpolated coords.</summary>
    public const int TexModeProject2D = 0x01;

    /// <summary>Texture mode: projective sample (u/w, v/w; 3D adds s/w).</summary>
    public const int TexModeProject3D = 0x02;

    /// <summary>Texture mode: cube-map sample of the interpolated xyz direction.</summary>
    public const int TexModeCubemap = 0x03;

    /// <summary>Texture mode: pass the interpolated coords through as the texel.</summary>
    public const int TexModePassthru = 0x04;

    /// <summary>Texture mode: per-component clip discard; texel is zero.</summary>
    public const int TexModeClipPlane = 0x05;

    /// <summary>Texture mode: sample with the previous stage's texel as coords (AR).</summary>
    public const int TexModeDpndntAr = 0x0F;

    /// <summary>Texture mode: sample with the previous stage's texel as coords (GB).</summary>
    public const int TexModeDpndntGb = 0x10;

    /// <summary>Texture mode: dot of coords and mapped texel; texel is zero.</summary>
    public const int TexModeDotProduct = 0x11;

    /// <summary>Texture mode: offset coords by the bump matrix, then sample.</summary>
    public const int TexModeBumpEnvMap = 0x06;

    /// <summary>Texture mode: bump-offset sample, then scale by luminance.</summary>
    public const int TexModeBumpEnvMapLum = 0x07;

    /// <summary>Texture mode: sample at (previous dot, this dot).</summary>
    public const int TexModeDotSt = 0x09;

    /// <summary>Texture mode: dot only; texel is zero (depth write in hardware).</summary>
    public const int TexModeDotZw = 0x0A;

    /// <summary>Texture mode: stage-2 reflect-diffuse cube/2D sample of the dot chain.</summary>
    public const int TexModeDotRflctDiff = 0x0B;

    /// <summary>Texture mode: stage-3 reflect-specular cube/2D sample of the dot chain.</summary>
    public const int TexModeDotRflctSpec = 0x0C;

    /// <summary>Texture mode: sample at (dot1, dot2) of the dot chain.</summary>
    public const int TexModeDotStr3D = 0x0D;

    /// <summary>Texture mode: stage-3 cube/2D sample of the dot chain.</summary>
    public const int TexModeDotStrCube = 0x0E;

    /// <summary>Texture mode: xemu-equivalent zero-texel stub (reference UNIMPLEMENTED).</summary>
    public const int TexModeBrdf = 0x08;

    /// <summary>Texture mode: xemu-equivalent zero-texel stub (reference UNIMPLEMENTED).</summary>
    public const int TexModeDotRflctSpecConst = 0x12;

    // Reserved modes 0x13-0x1F degrade to NONE with a diagnostic count.

    /// <summary>Per-stage RGB input words (SET_COMBINER_COLOR_ICW).</summary>
    public uint[] RgbInputs { get; } = new uint[MaxStages];

    /// <summary>Per-stage alpha input words (SET_COMBINER_ALPHA_ICW).</summary>
    public uint[] AlphaInputs { get; } = new uint[MaxStages];

    /// <summary>Per-stage RGB output words (SET_COMBINER_COLOR_OCW).</summary>
    public uint[] RgbOutputs { get; } = new uint[MaxStages];

    /// <summary>Per-stage alpha output words (SET_COMBINER_ALPHA_OCW).</summary>
    public uint[] AlphaOutputs { get; } = new uint[MaxStages];

    /// <summary>Per-stage C0 constant words, ARGB packed (SET_COMBINER_FACTOR0).</summary>
    public uint[] Factor0 { get; } = new uint[MaxStages];

    /// <summary>Per-stage C1 constant words, ARGB packed (SET_COMBINER_FACTOR1).</summary>
    public uint[] Factor1 { get; } = new uint[MaxStages];

    /// <summary>Final-combiner constant words, ARGB packed (SET_SPECULAR_FOG_FACTOR).</summary>
    public uint[] SpecFogFactor { get; } = new uint[2];

    /// <summary>Stage count + flags (SET_COMBINER_CONTROL).</summary>
    public uint Control { get; set; }

    /// <summary>Packed per-stage texture modes, 5 bits each (SET_SHADER_STAGE_PROGRAM).</summary>
    public uint StageProgram { get; set; }

    /// <summary>Dot-map low 12 bits of SHADERCTL (SET_DOT_RGBMAPPING).</summary>
    public uint DotRgbMapping { get; set; }

    /// <summary>Dependent-texture select bits 12-27 of SHADERCTL (SET_SHADER_OTHER_STAGE_INPUT).</summary>
    public uint OtherStageInput { get; set; }

    /// <summary>Final-combiner RGB inputs A/B/C/D (SET_COMBINER_SPECULAR_FOG_CW0).</summary>
    public uint Final0 { get; set; }

    /// <summary>Final-combiner inputs E/F/G + settings (SET_COMBINER_SPECULAR_FOG_CW1).</summary>
    public uint Final1 { get; set; }

    /// <summary>Resets all combiner state to power-on defaults (disabled).</summary>
    public void Reset()
    {
        Array.Clear(RgbInputs);
        Array.Clear(AlphaInputs);
        Array.Clear(RgbOutputs);
        Array.Clear(AlphaOutputs);
        Array.Clear(Factor0);
        Array.Clear(Factor1);
        Array.Clear(SpecFogFactor);
        Control = 0;
        StageProgram = 0;
        DotRgbMapping = 0;
        OtherStageInput = 0;
        Final0 = 0;
        Final1 = 0;
    }

    /// <summary>Saves all combiner stage words, factors, and control state.</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        WriteUints(writer, RgbInputs);
        WriteUints(writer, AlphaInputs);
        WriteUints(writer, RgbOutputs);
        WriteUints(writer, AlphaOutputs);
        WriteUints(writer, Factor0);
        WriteUints(writer, Factor1);
        WriteUints(writer, SpecFogFactor);
        writer.Write(Control);
        writer.Write(StageProgram);
        writer.Write(DotRgbMapping);
        writer.Write(OtherStageInput);
        writer.Write(Final0);
        writer.Write(Final1);
    }

    /// <summary>Restores all combiner stage words, factors, and control state.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported combiner snapshot version {version}.");
        }

        ReadUints(reader, RgbInputs);
        ReadUints(reader, AlphaInputs);
        ReadUints(reader, RgbOutputs);
        ReadUints(reader, AlphaOutputs);
        ReadUints(reader, Factor0);
        ReadUints(reader, Factor1);
        ReadUints(reader, SpecFogFactor);
        Control = reader.ReadUInt32();
        StageProgram = reader.ReadUInt32();
        DotRgbMapping = reader.ReadUInt32();
        OtherStageInput = reader.ReadUInt32();
        Final0 = reader.ReadUInt32();
        Final1 = reader.ReadUInt32();
    }

    private const int SnapshotVersion = 1;

    private static void WriteUints(BinaryWriter writer, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void ReadUints(BinaryReader reader, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadUInt32();
        }
    }

    /// <summary>Gets the active general-combiner stage count, clamped to 8.</summary>
    public int StageCount => (int)Math.Min(Control & 0xFFu, (uint)MaxStages);

    /// <summary>Gets whether the final combiner is programmed (either CW word nonzero).</summary>
    public bool FinalEnabled => (Final0 | Final1) != 0;

    /// <summary>Decodes the texture mode for the given stage (5-bit field).</summary>
    public int DecodeTexMode(int stage)
    {
        return (int)((StageProgram >> (stage * 5)) & 0x1Fu);
    }

    /// <summary>
    /// Decodes the dot-map for the given stage. Stage 0 is fixed to
    /// ZERO_TO_ONE; stages 1-3 come from the low 12 bits of SHADERCTL
    /// (4 bits each), matching xemu's <c>ps.dot_map</c> setup.
    /// </summary>
    public int DecodeDotMap(int stage)
    {
        return stage switch
        {
            1 => (int)(DotRgbMapping & 0xFu),
            2 => (int)((DotRgbMapping >> 4) & 0xFu),
            3 => (int)((DotRgbMapping >> 8) & 0xFu),
            _ => 0,
        };
    }

    /// <summary>
    /// Decodes the dependent-texture input stage: stage 1 always reads
    /// stage 0, stages 2-3 read SHADERCTL bits 16-19 / 20-23, matching
    /// xemu's <c>ps.input_tex</c> setup. Stage 0 has no input (-1).
    /// </summary>
    public int DecodeInputTex(int stage)
    {
        return stage switch
        {
            1 => 0,
            2 => (int)((OtherStageInput >> 16) & 0xFu),
            3 => (int)((OtherStageInput >> 20) & 0xFu),
            _ => -1,
        };
    }

    /// <summary>
    /// Gets whether the dot map is implemented (0-7). Maps 8-15 are not
    /// defined by the reference; they resolve as identity and the caller
    /// counts the fallback.
    /// </summary>
    public static bool IsSupportedDotMap(int map)
    {
        return map is >= 0 and <= 7;
    }

    /// <summary>
    /// Applies a dot-map to an unsigned [0,1] texel, producing the vec3 the
    /// dot modes dot against the interpolated coords. Maps 0-3 follow the
    /// reference sign functions; map 4 (HILO_1) packs the texel bytes as
    /// hi = (a:r) / 65535, lo = (g:b) / 65535 with z = 1; maps 5-7 (HILO
    /// hemisphere D3D/GL/generic) decode the same hi/lo words as signed
    /// H/L and return (H, L, sqrt(1-H*H-L*L)), the 16-bit extensions of the
    /// 8-bit sign1/sign2/sign3 functions (xemu still resolves these as
    /// identity FIXMEs; Cxbx supplies the piecewise signed decode).
    /// Maps 8-15 fall back to identity.
    /// </summary>
    public static void ApplyDotMap(int map, float r, float g, float b, float a, out float x, out float y, out float z)
    {
        switch (map)
        {
            case 1:
                x = Sign1(r);
                y = Sign1(g);
                z = Sign1(b);
                break;
            case 2:
                x = Sign2(r);
                y = Sign2(g);
                z = Sign2(b);
                break;
            case 3:
                x = Sign3(r);
                y = Sign3(g);
                z = Sign3(b);
                break;
            case 4:
                DecodeHilo(r, g, b, a, out float hi, out float lo);
                x = hi / 65535f;
                y = lo / 65535f;
                z = 1f;
                break;
            case 5:
                DecodeHilo(r, g, b, a, out hi, out lo);
                x = DecodeHemisphereD3D(hi);
                y = DecodeHemisphereD3D(lo);
                z = HemisphereZ(x, y);
                break;
            case 6:
                DecodeHilo(r, g, b, a, out hi, out lo);
                x = DecodeHemisphereGl(hi);
                y = DecodeHemisphereGl(lo);
                z = HemisphereZ(x, y);
                break;
            case 7:
                DecodeHilo(r, g, b, a, out hi, out lo);
                x = DecodeHemisphere(hi);
                y = DecodeHemisphere(lo);
                z = HemisphereZ(x, y);
                break;
            default:
                x = r;
                y = g;
                z = b;
                break;
        }
    }

    /// <summary>
    /// Packs an unsigned texel to the two 16-bit HILO words used by the
    /// HILO dot maps: hi = (a &lt;&lt; 8) | r, lo = (g &lt;&lt; 8) | b, each from
    /// the reference byte quantization <c>uint(component * 0xFF)</c>.
    /// </summary>
    public static void DecodeHilo(float r, float g, float b, float a, out float hi, out float lo)
    {
        hi = (float)(((uint)(a * 255f) << 8) | (uint)(r * 255f));
        lo = (float)(((uint)(g * 255f) << 8) | (uint)(b * 255f));
    }

    /// <summary>D3D signed 16-bit decode (xemu sign1 extended): (h - 0x8000)/0x7FFF.</summary>
    public static float DecodeHemisphereD3D(float h)
    {
        return (h - 32768f) / 32767f;
    }

    /// <summary>OpenGL signed 16-bit decode (xemu sign2 extended) with the +0.5 bias.</summary>
    public static float DecodeHemisphereGl(float h)
    {
        return h >= 32768f ? (h - 65535.5f) / 32767.5f : (h + 0.5f) / 32767.5f;
    }

    /// <summary>Plain signed 16-bit decode (xemu sign3 extended), two's complement / 0x7FFF.</summary>
    public static float DecodeHemisphere(float h)
    {
        return h >= 32768f ? (h - 65536f) / 32767f : h / 32767f;
    }

    /// <summary>Hemisphere z = sqrt(max(0, 1 - H*H - L*L)).</summary>
    public static float HemisphereZ(float h, float l)
    {
        return MathF.Sqrt(Math.Max(0f, 1f - (h * h) - (l * l)));
    }

    /// <summary>D3D signed decode (xemu <c>sign1</c>): (x*255-128)/127.</summary>
    public static float Sign1(float x)
    {
        return ((x * 255f) - 128f) / 127f;
    }

    /// <summary>OpenGL signed decode (xemu <c>sign2</c>).</summary>
    public static float Sign2(float x)
    {
        float scaled = x * 255f;
        return scaled >= 128f ? (scaled - 255.5f) / 127.5f : (scaled + 0.5f) / 127.5f;
    }

    /// <summary>Plain signed decode (xemu <c>sign3</c>), also used for bump offsets.</summary>
    public static float Sign3(float x)
    {
        float scaled = x * 255f;
        return scaled >= 128f ? (scaled - 256f) / 127f : scaled / 127f;
    }

    /// <summary>Luminance companion of <see cref="Sign3"/> (xemu <c>sign3_to_0_to_1</c>).</summary>
    public static float Sign3To01(float x)
    {
        return x >= 0f ? x / 2f : 1f + (x / 2f);
    }

    /// <summary>
    /// xemu <c>remapCubeTo2D</c>: maps a cube direction to 2D coordinates for
    /// a cube map unfolded into a 2D texture. The branch order and tie
    /// handling match the reference exactly (strict &gt; comparisons; the z
    /// branch also catches zero along z).
    /// </summary>
    public static void CubeRemapTo2D(float x, float y, float z, out float u, out float v)
    {
        float ax = Math.Abs(x);
        float ay = Math.Abs(y);
        float az = Math.Abs(z);
        if (ax > ay && ax > az)
        {
            u = x > 0f ? -z : z;
            v = y;
            u /= ax;
            v /= ax;
        }
        else if (ay > ax && ay > az)
        {
            u = x;
            v = y > 0f ? -z : z;
            u /= ay;
            v /= ay;
        }
        else
        {
            u = z > 0f ? x : -x;
            v = y;
            u /= az;
            v /= az;
        }
    }

    /// <summary>
    /// Selects the GL/D3D cube face and per-face [0,1] texel coordinates for
    /// a direction vector: face 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z. The
    /// major axis is chosen by &gt;= comparisons (Mesa-style tie breaking).
    /// The Xbox cube layout is uploaded in that same order, one face after
    /// another (xemu <c>generate_texture</c>).
    /// </summary>
    public static int SelectCubeFace(float x, float y, float z, out float s, out float t)
    {
        float ax = Math.Abs(x);
        float ay = Math.Abs(y);
        float az = Math.Abs(z);
        if (ax >= ay && ax >= az)
        {
            if (x > 0f)
            {
                s = ((-z / ax) + 1f) * 0.5f;
                t = ((-y / ax) + 1f) * 0.5f;
                return 0;
            }

            s = ((z / ax) + 1f) * 0.5f;
            t = ((-y / ax) + 1f) * 0.5f;
            return 1;
        }

        if (ay >= ax && ay >= az)
        {
            if (y > 0f)
            {
                s = ((x / ay) + 1f) * 0.5f;
                t = ((z / ay) + 1f) * 0.5f;
                return 2;
            }

            s = ((x / ay) + 1f) * 0.5f;
            t = ((-z / ay) + 1f) * 0.5f;
            return 3;
        }

        if (z > 0f)
        {
            s = ((x / az) + 1f) * 0.5f;
            t = ((-y / az) + 1f) * 0.5f;
            return 4;
        }

        s = ((-x / az) + 1f) * 0.5f;
        t = ((-y / az) + 1f) * 0.5f;
        return 5;
    }

    /// <summary>
    /// Gets whether a texture mode is valid in the given stage, per the
    /// stage column of the reference PS_TEXTUREMODES table. Invalid
    /// stage/mode pairs degrade to NONE with a diagnostic count.
    /// </summary>
    public static bool IsModeValidForStage(int mode, int stage)
    {
        return mode switch
        {
            TexModeNone => true,
            TexModeProject2D => true,
            TexModeProject3D => true,
            TexModeCubemap => true,
            TexModePassthru => true,
            TexModeClipPlane => true,
            TexModeBumpEnvMap => stage is >= 1 and <= 3,
            TexModeBumpEnvMapLum => stage is >= 1 and <= 3,
            TexModeBrdf => stage is 2 or 3,
            TexModeDotSt => stage is 2 or 3,
            TexModeDotZw => stage is 2 or 3,
            TexModeDotRflctDiff => stage == 2,
            TexModeDotRflctSpec => stage == 3,
            TexModeDotStr3D => stage == 3,
            TexModeDotStrCube => stage == 3,
            TexModeDpndntAr => stage is >= 1 and <= 3,
            TexModeDpndntGb => stage is >= 1 and <= 3,
            TexModeDotProduct => stage is 1 or 2,
            TexModeDotRflctSpecConst => stage == 3,
            _ => false,
        };
    }

    /// <summary>
    /// Builds the per-draw constant table: pairs of RGBA floats for stages 0-7
    /// (FACTOR0/1, ARGB unpacked) plus the final pair (SPECFOGFACTOR0/1).
    /// Layout matches xemu's <c>consts[18]</c> uniform (<c>c0_i</c>/<c>c1_i</c> defines).
    /// </summary>
    public static float[][] BuildConsts(PshCombiner state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var consts = new float[18][];
        for (int i = 0; i < MaxStages; i++)
        {
            consts[i * 2] = UnpackArgb(state.Factor0[i]);
            consts[i * 2 + 1] = UnpackArgb(state.Factor1[i]);
        }

        consts[16] = UnpackArgb(state.SpecFogFactor[0]);
        consts[17] = UnpackArgb(state.SpecFogFactor[1]);
        return consts;
    }

    /// <summary>Unpacks an ARGB-packed constant word to RGBA floats.</summary>
    public static float[] UnpackArgb(uint argb)
    {
        return new float[]
        {
            ((argb >> 16) & 0xFFu) / 255f,
            ((argb >> 8) & 0xFFu) / 255f,
            (argb & 0xFFu) / 255f,
            ((argb >> 24) & 0xFFu) / 255f,
        };
    }

    /// <summary>Applies an input mapping to a single component value.</summary>
    public static float ApplyInputMapping(float x, int mapping)
    {
        return mapping switch
        {
            MapUnsignedIdentity => Math.Max(x, 0f),
            MapUnsignedInvert => 1f - Math.Clamp(x, 0f, 1f),
            MapExpandNormal => (2f * Math.Max(x, 0f)) - 1f,
            MapExpandNegate => (-2f * Math.Max(x, 0f)) + 1f,
            MapHalfbiasNormal => Math.Max(x, 0f) - 0.5f,
            MapHalfbiasNegate => -Math.Max(x, 0f) + 0.5f,
            MapSignedIdentity => x,
            MapSignedNegate => -x,
            _ => throw new ArgumentOutOfRangeException(nameof(mapping)),
        };
    }

    /// <summary>Applies an output mapping to a single component value.</summary>
    public static float ApplyOutputMapping(float x, int mapping)
    {
        return mapping switch
        {
            OutIdentity => x,
            OutBias => x - 0.5f,
            OutShiftLeft1 => x * 2f,
            OutShiftLeft1Bias => (x - 0.5f) * 2f,
            OutShiftLeft2 => x * 4f,
            OutShiftRight1 => x / 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(mapping)),
        };
    }

    /// <summary>
    /// Evaluates the combiner for one fragment. Inputs are RGBA float vectors:
    /// diffuse (V0), specular (V1), fog (FOG), four texels (T0-T3, already
    /// resolved per texture mode by the caller), and the per-draw constant
    /// table from <see cref="BuildConsts"/>. General stages run first, then
    /// the final combiner; when the final combiner is disabled the r0
    /// temporary is returned as a documented deterministic fallback.
    /// </summary>
    public void Evaluate(
        float[] diffuse,
        float[] specular,
        float[] fog,
        float[][] texels,
        float[][] consts,
        out float r,
        out float g,
        out float b,
        out float a)
    {
        ArgumentNullException.ThrowIfNull(diffuse);
        ArgumentNullException.ThrowIfNull(specular);
        ArgumentNullException.ThrowIfNull(fog);
        ArgumentNullException.ThrowIfNull(texels);
        ArgumentNullException.ThrowIfNull(consts);

        // Mutable register file: V0, V1, T0-T3, R0, R1 (C0/C1/FOG read-only).
        Span<float> regs = stackalloc float[8 * 4];
        CopyVec(regs.Slice(0, 4), diffuse);
        CopyVec(regs.Slice(4, 4), specular);
        for (int i = 0; i < TextureSlots; i++)
        {
            CopyVec(regs.Slice((8 + (i * 4)), 4), texels[i]);
        }

        regs[24] = 0f;
        regs[25] = 0f;
        regs[26] = 0f;
        int texMode0 = DecodeTexMode(0);
        regs[27] = texMode0 != TexModeNone ? texels[0][3] : 1f;
        regs[28] = 0f;
        regs[29] = 0f;
        regs[30] = 0f;
        regs[31] = 0f;

        uint flags = Control >> 8;
        int stages = StageCount;
        for (int stage = 0; stage < stages; stage++)
        {
            EvalStage(stage, RgbInputs[stage], RgbOutputs[stage], isAlpha: false, regs, fog, consts);
            EvalStage(stage, AlphaInputs[stage], AlphaOutputs[stage], isAlpha: true, regs, fog, consts);
        }

        if (!FinalEnabled)
        {
            r = regs[24];
            g = regs[25];
            b = regs[26];
            a = regs[27];
            return;
        }

        // Final combiner: E/F first (A-D/G may read EF_PROD). E/F themselves
        // resolve C0/C1 to the final constant pair, matching cur_stage == 8.
        DecodeInput((Final1 >> 24) & 0xFFu, out int eReg, out int eChan, out int eMod);
        DecodeInput((Final1 >> 16) & 0xFFu, out int fReg, out int fChan, out int fMod);
        float ex = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, eReg, eChan, 0), eMod);
        float ey = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, eReg, eChan, 1), eMod);
        float ez = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, eReg, eChan, 2), eMod);
        float fx = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, fReg, fChan, 0), fMod);
        float fy = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, fReg, fChan, 1), fMod);
        float fz = MapComponent(ReadFinalComponent(regs, 0f, 0f, 0f, fog, consts, fReg, fChan, 2), fMod);
        float efx = ex * fx;
        float efy = ey * fy;
        float efz = ez * fz;

        DecodeInput((Final0 >> 24) & 0xFFu, out int aReg, out int aChan, out int aMod);
        DecodeInput((Final0 >> 16) & 0xFFu, out int bReg, out int bChan, out int bMod);
        DecodeInput((Final0 >> 8) & 0xFFu, out int cReg, out int cChan, out int cMod);
        DecodeInput(Final0 & 0xFFu, out int dReg, out int dChan, out int dMod);
        DecodeInput((Final1 >> 8) & 0xFFu, out int gReg, out int gChan, out int gMod);

        float ax = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, aReg, aChan, 0), aMod);
        float ay = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, aReg, aChan, 1), aMod);
        float az = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, aReg, aChan, 2), aMod);
        float bx = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, bReg, bChan, 0), bMod);
        float by = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, bReg, bChan, 1), bMod);
        float bz = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, bReg, bChan, 2), bMod);
        float cx = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, cReg, cChan, 0), cMod);
        float cy = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, cReg, cChan, 1), cMod);
        float cz = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, cReg, cChan, 2), cMod);
        float dx = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, dReg, dChan, 0), dMod);
        float dy = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, dReg, dChan, 1), dMod);
        float dz = MapComponent(ReadFinalComponent(regs, efx, efy, efz, fog, consts, dReg, dChan, 2), dMod);

        // fragColor.rgb = D + mix(C, B, A); fragColor.a = G.
        r = dx + (cx + ((bx - cx) * ax));
        g = dy + (cy + ((by - cy) * ay));
        b = dz + (cz + ((bz - cz) * az));
        a = MapComponent(ReadFinalAlpha(regs, efx, efy, efz, fog, consts, gReg, gChan), gMod);
    }

    private static void CopyVec(Span<float> destination, float[] source)
    {
        destination[0] = source[0];
        destination[1] = source[1];
        destination[2] = source[2];
        destination[3] = source[3];
    }

    private static void DecodeInput(uint value, out int reg, out int chan, out int mod)
    {
        reg = (int)(value & 0xFu);
        chan = (int)(value & 0x10u);
        mod = (int)(value & 0xE0u);
    }

    private static float MapComponent(float x, int mapping)
    {
        return ApplyInputMapping(x, mapping);
    }

    // Register-file offsets within the regs span: 0=V0 4=V1 8=T0 12=T1 16=T2 20=T3 24=R0 28=R1.
    private static int RegOffset(int reg)
    {
        return reg switch
        {
            RegV0 => 0,
            RegV1 => 4,
            RegT0 => 8,
            0x9 => 12,
            0xA => 16,
            0xB => 20,
            RegR0 => 24,
            RegR1 => 28,
            _ => -1,
        };
    }

    private float[] StageConst(float[][] consts, int stage, int reg)
    {
        uint flags = Control >> 8;
        if (reg == RegC0)
        {
            return ((flags & ControlUniqueC0) != 0) ? consts[stage * 2] : consts[0];
        }

        return ((flags & ControlUniqueC1) != 0) ? consts[(stage * 2) + 1] : consts[1];
    }

    private float ReadComponent(Span<float> regs, float[] fog, float[][] consts, int stage, int reg, int chan, int component)
    {
        if (reg == RegZero)
        {
            return 0f;
        }

        if (reg == RegC0 || reg == RegC1)
        {
            float[] c = StageConst(consts, stage, reg);
            return chan != 0 ? c[3] : c[component];
        }

        if (reg == RegFog)
        {
            return chan != 0 ? fog[3] : fog[component];
        }

        if (reg == RegV1R0Sum)
        {
            uint settings = Final1 & 0xFFu;
            float vx = ((settings & FinalComplementV1) != 0 ? 1f - regs[4 + component] : regs[4 + component]);
            float rx = ((settings & FinalComplementR0) != 0 ? 1f - regs[24 + component] : regs[24 + component]);
            float s = component < 3 ? vx + rx : 0f;
            return ((settings & FinalClampSum) != 0) ? Math.Clamp(s, 0f, 1f) : s;
        }

        if (reg == RegEfProd)
        {
            // E/F are final-combiner inputs; outside the final stage this reads zero.
            return 0f;
        }

        int offset = RegOffset(reg);
        if (offset < 0)
        {
            return 0f;
        }

        return regs[offset + component];
    }

    private float ReadFinalComponent(Span<float> regs, float efx, float efy, float efz, float[] fog, float[][] consts, int reg, int chan, int component)
    {
        if (reg == RegEfProd)
        {
            return component == 0 ? efx : component == 1 ? efy : efz;
        }

        if (reg == RegC0)
        {
            return chan != 0 ? consts[16][3] : consts[16][component];
        }

        if (reg == RegC1)
        {
            return chan != 0 ? consts[17][3] : consts[17][component];
        }

        return ReadComponent(regs, fog, consts, MaxStages, reg, chan, component);
    }

    private float ReadFinalAlpha(Span<float> regs, float efx, float efy, float efz, float[] fog, float[][] consts, int reg, int chan)
    {
        // Alpha channel select: BLUE reads .b, ALPHA reads .a.
        int component = chan != 0 ? 3 : 2;
        if (reg == RegEfProd)
        {
            return component == 3 ? 0f : efz;
        }

        if (reg == RegC0)
        {
            return consts[16][component];
        }

        if (reg == RegC1)
        {
            return consts[17][component];
        }

        return ReadComponent(regs, fog, consts, MaxStages, reg, chan, component);
    }

    private float ReadAlpha(Span<float> regs, float[] fog, float[][] consts, int stage, int reg, int chan)
    {
        // Alpha inputs: BLUE channel reads .b, ALPHA channel reads .a.
        int component = chan != 0 ? 3 : 2;
        return ReadComponent(regs, fog, consts, stage, reg, chan, component);
    }

    private void EvalStage(int stage, uint inputs, uint outputs, bool isAlpha, Span<float> regs, float[] fog, float[][] consts)
    {
        DecodeInput((inputs >> 24) & 0xFFu, out int aReg, out int aChan, out int aMod);
        DecodeInput((inputs >> 16) & 0xFFu, out int bReg, out int bChan, out int bMod);
        DecodeInput((inputs >> 8) & 0xFFu, out int cReg, out int cChan, out int cMod);
        DecodeInput(inputs & 0xFFu, out int dReg, out int dChan, out int dMod);

        int abDest = (int)((outputs >> 4) & 0xFu);
        int cdDest = (int)(outputs & 0xFu);
        int muxDest = (int)((outputs >> 8) & 0xFu);
        uint outFlags = outputs >> 12;
        int mapping = (int)(outFlags & 0x38u);

        if (isAlpha)
        {
            float a = MapComponent(ReadAlpha(regs, fog, consts, stage, aReg, aChan), aMod);
            float b = MapComponent(ReadAlpha(regs, fog, consts, stage, bReg, bChan), bMod);
            float c = MapComponent(ReadAlpha(regs, fog, consts, stage, cReg, cChan), cMod);
            float d = MapComponent(ReadAlpha(regs, fog, consts, stage, dReg, dChan), dMod);

            // Scalar dot-product equals multiply (GLSL dot(float, float)); no branch needed.
            float ab = a * b;
            float cd = c * d;
            ab = Math.Clamp(ApplyOutputMapping(ab, mapping), -1f, 1f);
            cd = Math.Clamp(ApplyOutputMapping(cd, mapping), -1f, 1f);

            float mux = ((outFlags & FlagMux) != 0) ? MuxSelect(regs[27], ab, cd) : ab + cd;
            mux = Math.Clamp(ApplyOutputMapping(mux, mapping), -1f, 1f);

            WriteAlpha(regs, abDest, ab);
            WriteAlpha(regs, cdDest, cd);
            WriteAlpha(regs, muxDest, mux);
            return;
        }

        ReadMappedVec(regs, fog, consts, stage, aReg, aChan, aMod, out float ax, out float ay, out float az);
        ReadMappedVec(regs, fog, consts, stage, bReg, bChan, bMod, out float bx, out float by, out float bz);
        ReadMappedVec(regs, fog, consts, stage, cReg, cChan, cMod, out float cx, out float cy, out float cz);
        ReadMappedVec(regs, fog, consts, stage, dReg, dChan, dMod, out float dx, out float dy, out float dz);

        float abx;
        float aby;
        float abz;
        if ((outFlags & FlagAbDot) != 0)
        {
            float dot = (ax * bx) + (ay * by) + (az * bz);
            abx = dot;
            aby = dot;
            abz = dot;
        }
        else
        {
            abx = ax * bx;
            aby = ay * by;
            abz = az * bz;
        }

        float cdx;
        float cdy;
        float cdz;
        if ((outFlags & FlagCdDot) != 0)
        {
            float dot = (cx * dx) + (cy * dy) + (cz * dz);
            cdx = dot;
            cdy = dot;
            cdz = dot;
        }
        else
        {
            cdx = cx * dx;
            cdy = cy * dy;
            cdz = cz * dz;
        }

        abx = Math.Clamp(ApplyOutputMapping(abx, mapping), -1f, 1f);
        aby = Math.Clamp(ApplyOutputMapping(aby, mapping), -1f, 1f);
        abz = Math.Clamp(ApplyOutputMapping(abz, mapping), -1f, 1f);
        cdx = Math.Clamp(ApplyOutputMapping(cdx, mapping), -1f, 1f);
        cdy = Math.Clamp(ApplyOutputMapping(cdy, mapping), -1f, 1f);
        cdz = Math.Clamp(ApplyOutputMapping(cdz, mapping), -1f, 1f);

        float muxx;
        float muxy;
        float muxz;
        if ((outFlags & FlagMux) != 0)
        {
            muxx = MuxSelect(regs[27], abx, cdx);
            muxy = MuxSelect(regs[27], aby, cdy);
            muxz = MuxSelect(regs[27], abz, cdz);
        }
        else
        {
            muxx = abx + cdx;
            muxy = aby + cdy;
            muxz = abz + cdz;
        }

        muxx = Math.Clamp(ApplyOutputMapping(muxx, mapping), -1f, 1f);
        muxy = Math.Clamp(ApplyOutputMapping(muxy, mapping), -1f, 1f);
        muxz = Math.Clamp(ApplyOutputMapping(muxz, mapping), -1f, 1f);

        WriteVec(regs, abDest, abx, aby, abz, (outFlags & FlagAbBlueToAlpha) != 0);
        WriteVec(regs, cdDest, cdx, cdy, cdz, (outFlags & FlagCdBlueToAlpha) != 0);
        WriteVec(regs, muxDest, muxx, muxy, muxz, blueToAlpha: false);
    }

    private void ReadMappedVec(Span<float> regs, float[] fog, float[][] consts, int stage, int reg, int chan, int mod, out float x, out float y, out float z)
    {
        if (chan != 0)
        {
            // ALPHA channel replicates .a across RGB.
            float w = MapComponent(ReadComponent(regs, fog, consts, stage, reg, chan, 3), mod);
            x = w;
            y = w;
            z = w;
            return;
        }

        x = MapComponent(ReadComponent(regs, fog, consts, stage, reg, chan, 0), mod);
        y = MapComponent(ReadComponent(regs, fog, consts, stage, reg, chan, 1), mod);
        z = MapComponent(ReadComponent(regs, fog, consts, stage, reg, chan, 2), mod);
    }

    private float MuxSelect(float r0a, float ab, float cd)
    {
        return MuxSelectsCd(r0a) ? cd : ab;
    }

    private bool MuxSelectsCd(float r0a)
    {
        if (((Control >> 8) & ControlMuxMsb) != 0)
        {
            return r0a >= 0.5f;
        }

        // LSB of the 8-bit quantized alpha; negative alphas quantize to 0
        // (matches x86 cvttss2si saturation practiced by the reference).
        uint bits = r0a < 0f ? 0u : (uint)(r0a * 255f);
        return (bits & 1u) == 1u;
    }

    private static void WriteVec(Span<float> regs, int dest, float x, float y, float z, bool blueToAlpha)
    {
        int offset = RegOffset(dest);
        if (offset < 0)
        {
            return;
        }

        regs[offset] = x;
        regs[offset + 1] = y;
        regs[offset + 2] = z;
        if (blueToAlpha)
        {
            regs[offset + 3] = z;
        }
    }

    private static void WriteAlpha(Span<float> regs, int dest, float value)
    {
        int offset = RegOffset(dest);
        if (offset < 0)
        {
            return;
        }

        regs[offset + 3] = value;
    }
}
