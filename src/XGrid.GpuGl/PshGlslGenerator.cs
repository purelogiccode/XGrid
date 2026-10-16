using System;
using System.Globalization;
using System.Text;
using XGrid.Nv2a;

namespace XGrid.GpuGl;

/// <summary>
/// GLSL fragment-shader generator written from <see cref="PshCombiner"/>'s
/// C# semantics (design doc §3.4 option (a): one semantic source —
/// <c>PshCombiner.Evaluate</c> + <c>Nv2AMmio.ResolveTexel</c> — two
/// executors; nothing is ported from xemu/Cxbx, which are GPL/LGPL).
/// Pure string generation, no GL context (string goldens under unit test).
/// Generated body order mirrors the software path: perspective divide,
/// clip-plane discard, per-slot texture-mode resolution, general combiner
/// stages (RGB then alpha; write order AB/CD/MUX), final combiner (E/F
/// first, then A-D/G with EF_PROD), ZCLIP discard/clamp (§97), alpha-test
/// discard, zeta-field-quantized gl_FragDepth (§109 encode). Shader key =
/// combiner structure words + texture modes + legacy flag; all per-draw
/// values (factors, fog, alpha, ZCLIP, texture enable/scale/cube/dim,
/// clip bits) are uniforms.
/// </summary>
public static class PshGlslGenerator
{
    /// <summary>Builds the shader-cache key for a combiner snapshot + path.</summary>
    public static string BuildKey(bool legacy, PshCombiner state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sb = new StringBuilder(512);
        sb.Append(legacy ? "L" : "C");
        sb.Append(CultureInfo.InvariantCulture,
            $"|{state.Control:X8}|{state.StageProgram:X8}|{state.OtherStageInput:X8}|{state.Final0:X8}|{state.Final1:X8}");
        for (int i = 0; i < PshCombiner.MaxStages; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"|{state.RgbInputs[i]:X8},{state.AlphaInputs[i]:X8},{state.RgbOutputs[i]:X8},{state.AlphaOutputs[i]:X8}");
        }

        return sb.ToString();
    }

    /// <summary>Generates the full GLSL 330 fragment shader for the request.</summary>
    public static string BuildFragmentSource(bool legacy, PshCombiner state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sb = new StringBuilder(8192);
        AppendHeader(sb);
        sb.Append("void main() {\n");
        AppendInterpolation(sb);
        if (legacy)
        {
            AppendLegacyBody(sb);
        }
        else
        {
            AppendCombinerBody(sb, state);
        }

        AppendDepthAndOutput(sb, legacy ? "outcColor" : "comb");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Software-exact barycentric preamble (Nv2AMmio.FillTriangle/
    /// FillTrianglePsh formulas, same operation order on float32 uniforms +
    /// gl_FragCoord): GL's own varying interpolation is replaced so texel
    /// selection at integer-boundary UVs is bit-identical to software
    /// (dual-rasterizer ULP drift was flipping atlas texels at glyph edges).
    /// Produces: color, spec, c0..c3, fogFactor, depthZ.
    /// </summary>
    private static void AppendInterpolation(StringBuilder sb)
    {
        sb.Append("  float px = gl_FragCoord.x;\n");
        sb.Append("  float py = gl_FragCoord.y + uClipY;\n");
        sb.Append("  vec2 vA = uPx[0];\n");
        sb.Append("  vec2 vB = uPx[1];\n");
        sb.Append("  vec2 vC = uPx[2];\n");
        sb.Append("  float area = ((vB.x - vA.x) * (vC.y - vA.y)) - ((vC.x - vA.x) * (vB.y - vA.y));\n");
        sb.Append("  float invArea = 1.0 / area;\n");
        sb.Append("  float w0 = (((vB.x - vA.x) * (py - vA.y)) - ((vB.y - vA.y) * (px - vA.x))) * invArea;\n");
        sb.Append("  float w1 = (((vC.x - vB.x) * (py - vB.y)) - ((vC.y - vB.y) * (px - vB.x))) * invArea;\n");
        sb.Append("  float w2 = 1.0 - w0 - w1;\n");
        sb.Append(
            "  if (!((w0 >= 0.0 && w1 >= 0.0 && w2 >= 0.0) || (w0 <= 0.0 && w1 <= 0.0 && w2 <= 0.0))) discard;\n");
        sb.Append("  float p0 = w0;\n");
        sb.Append("  float p1 = w1;\n");
        sb.Append("  float p2 = w2;\n");
        sb.Append("  if (uPerspective == 1) {\n");
        sb.Append("    float s0 = w0 * uInvCw;\n");
        sb.Append("    float s1 = w1 * uInvAw;\n");
        sb.Append("    float s2 = w2 * uInvBw;\n");
        sb.Append("    float sum = s0 + s1 + s2;\n");
        sb.Append("    if (!isnan(sum) && !isinf(sum) && sum != 0.0) {\n");
        sb.Append("      float invSum = 1.0 / sum;\n");
        sb.Append("      p0 = s0 * invSum;\n");
        sb.Append("      p1 = s1 * invSum;\n");
        sb.Append("      p2 = s2 * invSum;\n");
        sb.Append("    }\n");
        sb.Append("  }\n");
        sb.Append("  vec4 color = (p1 * uColorV[0]) + (p2 * uColorV[1]) + (p0 * uColorV[2]);\n");
        sb.Append("  vec4 spec = (p1 * uSpecV[0]) + (p2 * uSpecV[1]) + (p0 * uSpecV[2]);\n");
        sb.Append("  vec4 c0 = (p1 * uT0V[0]) + (p2 * uT0V[1]) + (p0 * uT0V[2]);\n");
        sb.Append("  vec4 c1 = (p1 * uT1V[0]) + (p2 * uT1V[1]) + (p0 * uT1V[2]);\n");
        sb.Append("  vec4 c2 = (p1 * uT2V[0]) + (p2 * uT2V[1]) + (p0 * uT2V[2]);\n");
        sb.Append("  vec4 c3 = (p1 * uT3V[0]) + (p2 * uT3V[1]) + (p0 * uT3V[2]);\n");
        sb.Append("  float fogFactor = (p1 * uVtx[0].z) + (p2 * uVtx[1].z) + (p0 * uVtx[2].z);\n");
        sb.Append("  float depthZ = (w1 * uVtx[0].x) + (w2 * uVtx[1].x) + (w0 * uVtx[2].x);\n");
        sb.Append('\n');
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.Append("#version 330 core\n");
        sb.Append("uniform vec4 uConsts[18];\n");
        sb.Append("uniform vec4 uFogColor;\n");
        sb.Append("uniform int uFogEnable;\n");
        sb.Append("uniform int uAlphaTestEnable;\n");
        sb.Append("uniform int uAlphaFunc;\n");
        sb.Append("uniform float uAlphaRef;\n");
        sb.Append("uniform int uZclipActive;\n");
        sb.Append("uniform int uZclipCull;\n");
        sb.Append("uniform float uZclipMin;\n");
        sb.Append("uniform float uZclipMax;\n");
        sb.Append("uniform float uZetaRange;\n");
        sb.Append("uniform int uZetaBytes;\n");
        sb.Append("uniform int uTexOk[4];\n");
        sb.Append("uniform vec2 uTexSize[4];\n");
        sb.Append("uniform int uTexLinear[4];\n");
        sb.Append("uniform int uTexWrapX[4];\n");
        sb.Append("uniform int uTexWrapY[4];\n");
        sb.Append("uniform int uTexCube[4];\n");
        sb.Append("uniform int uTexDim[4];\n");
        sb.Append("uniform uint uShaderClip;\n");
        sb.Append("uniform vec2 uPx[3];\n");
        sb.Append("uniform vec4 uColorV[3];\n");
        sb.Append("uniform vec4 uSpecV[3];\n");
        sb.Append("uniform vec4 uT0V[3];\n");
        sb.Append("uniform vec4 uT1V[3];\n");
        sb.Append("uniform vec4 uT2V[3];\n");
        sb.Append("uniform vec4 uT3V[3];\n");
        sb.Append("uniform vec4 uVtx[3];\n");
        sb.Append("uniform int uPerspective;\n");
        sb.Append("uniform float uInvAw;\n");
        sb.Append("uniform float uInvBw;\n");
        sb.Append("uniform float uInvCw;\n");
        sb.Append("uniform float uClipY;\n");
        sb.Append("uniform sampler2D uTex0;\n");
        sb.Append("uniform sampler2D uTex1;\n");
        sb.Append("uniform sampler2D uTex2;\n");
        sb.Append("uniform sampler2D uTex3;\n");
        sb.Append("out vec4 fragColor;\n\n");

        // PshCombiner.ApplyInputMapping / ApplyOutputMapping (C# semantics).
        sb.Append("float InMap(float x, int m) {\n");
        sb.Append("  if (m == 0) return max(x, 0.0);\n");
        sb.Append("  if (m == 32) return 1.0 - clamp(x, 0.0, 1.0);\n");
        sb.Append("  if (m == 64) return 2.0 * max(x, 0.0) - 1.0;\n");
        sb.Append("  if (m == 96) return -2.0 * max(x, 0.0) + 1.0;\n");
        sb.Append("  if (m == 128) return max(x, 0.0) - 0.5;\n");
        sb.Append("  if (m == 160) return -max(x, 0.0) + 0.5;\n");
        sb.Append("  if (m == 192) return x;\n");
        sb.Append("  return -x;\n");
        sb.Append("}\n");
        sb.Append("vec3 InMap3(vec3 x, int m) {\n");
        sb.Append("  return vec3(InMap(x.r, m), InMap(x.g, m), InMap(x.b, m));\n");
        sb.Append("}\n");
        sb.Append("float OutMap(float x, int m) {\n");
        sb.Append("  if (m == 8) return x - 0.5;\n");
        sb.Append("  if (m == 16) return x * 2.0;\n");
        sb.Append("  if (m == 24) return (x - 0.5) * 2.0;\n");
        sb.Append("  if (m == 32) return x * 4.0;\n");
        sb.Append("  if (m == 48) return x / 2.0;\n");
        sb.Append("  return x;\n");
        sb.Append("}\n");
        sb.Append("vec3 OutMap3(vec3 x, int m) {\n");
        sb.Append("  return vec3(OutMap(x.r, m), OutMap(x.g, m), OutMap(x.b, m));\n");
        sb.Append("}\n");

        // Nv2AMmio.CompareFunc for the alpha test (uniform-driven so alpha
        // state churn does not split the program cache).
        sb.Append("bool AlphaTest(float a) {\n");
        sb.Append("  int f = uAlphaFunc;\n");
        sb.Append("  if (f == 1) return a < uAlphaRef;\n");
        sb.Append("  if (f == 2) return a == uAlphaRef;\n");
        sb.Append("  if (f == 3) return a <= uAlphaRef;\n");
        sb.Append("  if (f == 4) return a > uAlphaRef;\n");
        sb.Append("  if (f == 5) return a != uAlphaRef;\n");
        sb.Append("  if (f == 6) return a >= uAlphaRef;\n");
        sb.Append("  if (f == 7) return true;\n");
        sb.Append("  return false;\n");
        sb.Append("}\n");

        // PshCombiner.CubeRemapTo2D semantics (CUBEMAP mode on a
        // non-cubemap-flagged 2D texture).
        sb.Append("vec2 CubeRemap2D(vec3 d) {\n");
        sb.Append("  float ax = abs(d.x); float ay = abs(d.y); float az = abs(d.z);\n");
        sb.Append("  if (ax > ay && ax > az) {\n");
        sb.Append("    float u = d.x > 0.0 ? -d.z : d.z;\n");
        sb.Append("    return vec2(u / ax, d.y / ax);\n");
        sb.Append("  }\n");
        sb.Append("  if (ay > ax && ay > az) {\n");
        sb.Append("    float v = d.y > 0.0 ? -d.z : d.z;\n");
        sb.Append("    return vec2(d.x / ay, v / ay);\n");
        sb.Append("  }\n");
        sb.Append("  float u = d.z > 0.0 ? d.x : -d.x;\n");
        sb.Append("  return vec2(u / az, d.y / az);\n");
        sb.Append("}\n");

        // Nv2AMmio.WrapCoordinate arithmetic in shader integers (floor then
        // wrap in texel space; GLSL int division truncates like C#), paired
        // with texelFetch so samples are bit-identical to the software
        // floor/wrap — a normalized texture() round-trip (u*(1/size)*size)
        // flips floor at texel boundaries. Failed sample (disabled /
        // unsupported format / bad size) -> SetTexelNone (0,0,0,1).
        sb.Append("int WrapI(int v, int s, int mode) {\n");
        sb.Append("  if (mode == 0) {\n");
        sb.Append("    int m = v - (s * (v / s));\n");
        sb.Append("    if (m < 0) m += s;\n");
        sb.Append("    return m;\n");
        sb.Append("  }\n");
        sb.Append("  if (mode == 1) {\n");
        sb.Append("    int p = s * 2;\n");
        sb.Append("    int m = v - (p * (v / p));\n");
        sb.Append("    if (m < 0) m += p;\n");
        sb.Append("    return m < s ? m : p - 1 - m;\n");
        sb.Append("  }\n");
        sb.Append("  return v < 0 ? 0 : (v >= s ? s - 1 : v);\n");
        sb.Append("}\n");
        sb.Append("vec4 SampleSlot(int slot, vec2 uv) {\n");
        sb.Append("  if (uTexOk[slot] != 1) return vec4(0.0, 0.0, 0.0, 1.0);\n");
        sb.Append("  vec2 c = (uTexLinear[slot] == 1) ? uv : uv * uTexSize[slot];\n");
        sb.Append("  ivec2 t;\n");
        sb.Append("  t.x = WrapI(int(floor(c.x)), int(uTexSize[slot].x), uTexWrapX[slot]);\n");
        sb.Append("  t.y = WrapI(int(floor(c.y)), int(uTexSize[slot].y), uTexWrapY[slot]);\n");
        sb.Append("  if (slot == 0) return texelFetch(uTex0, t, 0);\n");
        sb.Append("  if (slot == 1) return texelFetch(uTex1, t, 0);\n");
        sb.Append("  if (slot == 2) return texelFetch(uTex2, t, 0);\n");
        sb.Append("  return texelFetch(uTex3, t, 0);\n");
        sb.Append("}\n");
        sb.Append('\n');
    }

    private static void AppendLegacyBody(StringBuilder sb)
    {
        // Nv2AMmio.FillTriangle: diffuse modulated by texture slot 0 when
        // TrySampleTexture0 succeeds; failure leaves the color untouched.
        // color/c0 come from the software-exact interpolation preamble.
        sb.Append("  vec4 outcColor = color;\n");
        sb.Append("  if (uTexOk[0] == 1) outcColor *= SampleSlot(0, c0.xy);\n");
    }

    private static void AppendCombinerBody(StringBuilder sb, PshCombiner state)
    {
        // color/spec/c0..c3/fogFactor come from the interpolation preamble.
        sb.Append(
            "  vec4 fog = (uFogEnable == 1) ? vec4(uFogColor.rgb, clamp(fogFactor, 0.0, 1.0)) : vec4(0.0, 0.0, 0.0, 1.0);\n");
        sb.Append("  vec4 t0 = vec4(0.0, 0.0, 0.0, 1.0);\n");
        sb.Append("  vec4 t1 = vec4(0.0, 0.0, 0.0, 1.0);\n");
        sb.Append("  vec4 t2 = vec4(0.0, 0.0, 0.0, 1.0);\n");
        sb.Append("  vec4 t3 = vec4(0.0, 0.0, 0.0, 1.0);\n");
        AppendClipPlaneDiscards(sb, state);
        AppendTexelResolution(sb, state);

        // PshCombiner.Evaluate register-file init: R0 = (0,0,0, r0.a-init)
        // where r0.a = T0 alpha when the slot-0 mode is not NONE else 1;
        // R1 = 0. Evaluated after texel resolution, like the software loop.
        bool mode0None = state.DecodeTexMode(0) == PshCombiner.TexModeNone;
        sb.Append(mode0None
            ? "  vec4 r0 = vec4(0.0, 0.0, 0.0, 1.0);\n"
            : "  vec4 r0 = vec4(0.0, 0.0, 0.0, t0.a);\n");
        sb.Append("  vec4 r1 = vec4(0.0);\n");

        bool muxMsb = ((state.Control >> 8) & PshCombiner.ControlMuxMsb) != 0;
        for (int stage = 0; stage < state.StageCount; stage++)
        {
            AppendStage(sb, state, stage, muxMsb);
        }

        if (state.FinalEnabled)
        {
            AppendFinal(sb, state);
        }
        else
        {
            sb.Append("  vec4 comb = r0;\n");
        }
    }

    private static void AppendClipPlaneDiscards(StringBuilder sb, PshCombiner state)
    {
        for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
        {
            if (state.DecodeTexMode(slot) != PshCombiner.TexModeClipPlane)
            {
                continue;
            }

            string coord = $"c{slot}";
            for (int j = 0; j < 4; j++)
            {
                string comp = j switch { 0 => ".x", 1 => ".y", 2 => ".z", _ => ".w" };
                uint bit = (uint)((4 * slot) + j);
                sb.Append(CultureInfo.InvariantCulture,
                    $"  if ((((uShaderClip >> {bit}u) & 1u) != 0u) ? ({coord}{comp} >= 0.0) : ({coord}{comp} < 0.0)) discard;\n");
            }
        }
    }

    private static void AppendTexelResolution(StringBuilder sb, PshCombiner state)
    {
        for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
        {
            int mode = state.DecodeTexMode(slot);
            string t = $"t{slot}";
            string c = $"c{slot}";
            if (!PshCombiner.IsModeValidForStage(mode, slot))
            {
                // Software: SetTexelNone + CountTexModeFallback (parity; the
                // backend does not count this as unsupported).
                sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0, 0.0, 0.0, 1.0);\n");
                continue;
            }

            switch (mode)
            {
                case PshCombiner.TexModeNone:
                    sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0, 0.0, 0.0, 1.0);\n");
                    break;
                case PshCombiner.TexModePassthru:
                    sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4({c}.xy, 0.0, 1.0);\n");
                    break;
                case PshCombiner.TexModeClipPlane:
                    sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0);\n");
                    break;
                case PshCombiner.TexModeProject2D:
                    // Cubemap-flagged: software cube-samples when the format
                    // is 05/06 (slice-1 counts that as unsupported) or falls
                    // back to none; slice 1 emits none either way.
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  if (uTexCube[{slot}] == 1) {{ {t} = vec4(0.0, 0.0, 0.0, 1.0); }} else {{ {t} = SampleSlot({slot}, {c}.xy); }}\n");
                    break;
                case PshCombiner.TexModeProject3D:
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  if (uTexDim[{slot}] == 3) {{ {t} = vec4(0.0, 0.0, 0.0, 1.0); }} else {{ {t} = SampleSlot({slot}, {c}.xy / {c}.w); }}\n");
                    break;
                case PshCombiner.TexModeCubemap:
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  if (uTexCube[{slot}] == 1) {{ {t} = vec4(0.0, 0.0, 0.0, 1.0); }} else {{ {t} = SampleSlot({slot}, CubeRemap2D({c}.xyz)); }}\n");
                    break;
                case PshCombiner.TexModeDpndntAr:
                case PshCombiner.TexModeDpndntGb:
                    AppendDependent(sb, state, slot, mode, t);
                    break;
                case PshCombiner.TexModeBrdf:
                case PshCombiner.TexModeDotRflctSpecConst:
                    // Software stub: SetTexelZero.
                    sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0);\n");
                    break;
                default:
                    // Dot family / bump: slice-1 unsupported when valid for
                    // the stage (BackendUnsupported counts at draw time);
                    // deterministic none-texel here.
                    sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0, 0.0, 0.0, 1.0);\n");
                    break;
            }
        }
    }

    private static void AppendDependent(StringBuilder sb, PshCombiner state, int slot, int mode, string t)
    {
        int input = state.DecodeInputTex(slot);
        if (input is < 0 or >= PshCombiner.TextureSlots)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {t} = vec4(0.0, 0.0, 0.0, 1.0);\n");
            return;
        }

        string prev = $"t{input}";
        (string u, string v) = mode == PshCombiner.TexModeDpndntAr
            ? ($"{prev}.a", $"{prev}.r")
            : ($"{prev}.g", $"{prev}.b");
        sb.Append(CultureInfo.InvariantCulture, $"  {t} = SampleSlot({slot}, vec2({u}, {v}));\n");
    }

    private static void AppendStage(StringBuilder sb, PshCombiner state, int stage, bool muxMsb)
    {
        uint rgbIn = state.RgbInputs[stage];
        uint alphaIn = state.AlphaInputs[stage];
        uint rgbOut = state.RgbOutputs[stage];
        uint alphaOut = state.AlphaOutputs[stage];
        uint outFlags = rgbOut >> 12;
        int mapping = (int)(outFlags & 0x38u);
        bool abDot = (outFlags & PshCombiner.FlagAbDot) != 0;
        bool cdDot = (outFlags & PshCombiner.FlagCdDot) != 0;
        bool muxFlag = (outFlags & PshCombiner.FlagMux) != 0;
        bool abBlue = (outFlags & PshCombiner.FlagAbBlueToAlpha) != 0;
        bool cdBlue = (outFlags & PshCombiner.FlagCdBlueToAlpha) != 0;

        string aExpr = ReadVecExpr(state, stage, (rgbIn >> 24) & 0xFFu);
        string bExpr = ReadVecExpr(state, stage, (rgbIn >> 16) & 0xFFu);
        string cExpr = ReadVecExpr(state, stage, (rgbIn >> 8) & 0xFFu);
        string dExpr = ReadVecExpr(state, stage, rgbIn & 0xFFu);
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 s{stage}A = {aExpr};\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 s{stage}B = {bExpr};\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 s{stage}C = {cExpr};\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 s{stage}D = {dExpr};\n");
        string ab = abDot ? $"vec3(dot(s{stage}A, s{stage}B))" : $"(s{stage}A * s{stage}B)";
        string cd = cdDot ? $"vec3(dot(s{stage}C, s{stage}D))" : $"(s{stage}C * s{stage}D)";
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 ab{stage} = clamp(OutMap3({ab}, {mapping}), -1.0, 1.0);\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 cd{stage} = clamp(OutMap3({cd}, {mapping}), -1.0, 1.0);\n");
        string sel = MuxSelectExpr(muxMsb);
        string mux = muxFlag ? $"({sel} ? cd{stage} : ab{stage})" : $"(ab{stage} + cd{stage})";
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 mux{stage} = clamp(OutMap3({mux}, {mapping}), -1.0, 1.0);\n");
        AppendVecWrites(sb, rgbOut, abBlue, cdBlue, $"ab{stage}", $"cd{stage}", $"mux{stage}");

        uint alphaFlags = alphaOut >> 12;
        int alphaMapping = (int)(alphaFlags & 0x38u);
        bool alphaMux = (alphaFlags & PshCombiner.FlagMux) != 0;
        string aa = ReadAlphaExpr(state, stage, (alphaIn >> 24) & 0xFFu);
        string abv = ReadAlphaExpr(state, stage, (alphaIn >> 16) & 0xFFu);
        string ac = ReadAlphaExpr(state, stage, (alphaIn >> 8) & 0xFFu);
        string ad = ReadAlphaExpr(state, stage, alphaIn & 0xFFu);
        sb.Append(CultureInfo.InvariantCulture,
            $"  float s{stage}aA = InMap({aa}, {(int)((alphaIn >> 24) & 0xE0u)});\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  float s{stage}aB = InMap({abv}, {(int)((alphaIn >> 16) & 0xE0u)});\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  float s{stage}aC = InMap({ac}, {(int)((alphaIn >> 8) & 0xE0u)});\n");
        sb.Append(CultureInfo.InvariantCulture, $"  float s{stage}aD = InMap({ad}, {(int)(alphaIn & 0xE0u)});\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  float abA{stage} = clamp(OutMap(s{stage}aA * s{stage}aB, {alphaMapping}), -1.0, 1.0);\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  float cdA{stage} = clamp(OutMap(s{stage}aC * s{stage}aD, {alphaMapping}), -1.0, 1.0);\n");
        string alphaSel = MuxSelectExpr(muxMsb);
        string muxA = alphaMux
            ? $"({alphaSel} ? cdA{stage} : abA{stage})"
            : $"(abA{stage} + cdA{stage})";
        sb.Append(CultureInfo.InvariantCulture,
            $"  float muxA{stage} = clamp(OutMap({muxA}, {alphaMapping}), -1.0, 1.0);\n");
        AppendAlphaWrites(sb, alphaOut, $"abA{stage}", $"cdA{stage}", $"muxA{stage}");
    }

    private static void AppendVecWrites(StringBuilder sb, uint rgbOut, bool abBlue, bool cdBlue, string ab, string cd,
        string mux)
    {
        string? abName = RegName((int)((rgbOut >> 4) & 0xFu));
        string? cdName = RegName((int)(rgbOut & 0xFu));
        string? muxName = RegName((int)((rgbOut >> 8) & 0xFu));
        if (abName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {abName}.rgb = {ab};\n");
            if (abBlue)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  {abName}.a = {ab}.b;\n");
            }
        }

        if (cdName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {cdName}.rgb = {cd};\n");
            if (cdBlue)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  {cdName}.a = {cd}.b;\n");
            }
        }

        if (muxName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {muxName}.rgb = {mux};\n");
        }
    }

    private static void AppendAlphaWrites(StringBuilder sb, uint alphaOut, string ab, string cd, string mux)
    {
        string? abName = RegName((int)((alphaOut >> 4) & 0xFu));
        string? cdName = RegName((int)(alphaOut & 0xFu));
        string? muxName = RegName((int)((alphaOut >> 8) & 0xFu));
        if (abName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {abName}.a = {ab};\n");
        }

        if (cdName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {cdName}.a = {cd};\n");
        }

        if (muxName is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {muxName}.a = {mux};\n");
        }
    }

    private static string? RegName(int reg)
    {
        return reg switch
        {
            PshCombiner.RegV0 => "color",
            PshCombiner.RegV1 => "spec",
            0x8 => "t0",
            0x9 => "t1",
            0xA => "t2",
            0xB => "t3",
            PshCombiner.RegR0 => "r0",
            PshCombiner.RegR1 => "r1",
            _ => null,
        };
    }

    private static string MuxSelectExpr(bool muxMsb)
    {
        return muxMsb
            ? "r0.a >= 0.5"
            : "mod(floor(max(r0.a, 0.0) * 255.0), 2.0) >= 1.0";
    }

    private static void AppendFinal(StringBuilder sb, PshCombiner state)
    {
        uint final0 = state.Final0;
        uint final1 = state.Final1;
        int eMod = (int)((final1 >> 24) & 0xE0u);
        int fMod = (int)((final1 >> 16) & 0xE0u);
        int aMod = (int)((final0 >> 24) & 0xE0u);
        int bMod = (int)((final0 >> 16) & 0xE0u);
        int cMod = (int)((final0 >> 8) & 0xE0u);
        int dMod = (int)(final0 & 0xE0u);
        int gMod = (int)((final1 >> 8) & 0xE0u);

        string eRaw = ReadFinalVecExpr(state, (final1 >> 24) & 0xFFu, efAvailable: false, null);
        string fRaw = ReadFinalVecExpr(state, (final1 >> 16) & 0xFFu, efAvailable: false, null);
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 eF = InMap3({eRaw}, {eMod}) * InMap3({fRaw}, {fMod});\n");
        string aRaw = ReadFinalVecExpr(state, (final0 >> 24) & 0xFFu, efAvailable: true, "eF");
        string bRaw = ReadFinalVecExpr(state, (final0 >> 16) & 0xFFu, efAvailable: true, "eF");
        string cRaw = ReadFinalVecExpr(state, (final0 >> 8) & 0xFFu, efAvailable: true, "eF");
        string dRaw = ReadFinalVecExpr(state, final0 & 0xFFu, efAvailable: true, "eF");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 aF = InMap3({aRaw}, {aMod});\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 bF = InMap3({bRaw}, {bMod});\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 cF = InMap3({cRaw}, {cMod});\n");
        sb.Append(CultureInfo.InvariantCulture, $"  vec3 dF = InMap3({dRaw}, {dMod});\n");
        sb.Append("  vec4 comb = vec4(dF + (cF + ((bF - cF) * aF)), 0.0);\n");

        string gExpr = ReadFinalAlphaExpr(state, (final1 >> 8) & 0xFFu, "eF");
        sb.Append(CultureInfo.InvariantCulture, $"  comb.a = InMap({gExpr}, {gMod});\n");
    }

    private static void AppendDepthAndOutput(StringBuilder sb, string expression)
    {
        // ZCLIP before alpha (§97): device units = normalized * zeta range;
        // CULL discards, CLAMP pulls to the bound. Then alpha-test discard.
        // Then the §109 zeta-field-quantized depth store; Z_FORMAT float
        // zeta is counted unsupported at draw time (integer encode is the
        // deterministic fallback there). depthZ is produced by the
        // software-exact interpolation preamble.
        sb.Append("  if (uZclipActive == 1) {\n");
        sb.Append("    float zv = depthZ * uZetaRange;\n");
        sb.Append("    if (zv < uZclipMin || zv > uZclipMax) {\n");
        sb.Append("      if (uZclipCull == 1) discard;\n");
        sb.Append("      depthZ = clamp(zv, uZclipMin, uZclipMax) / uZetaRange;\n");
        sb.Append("    }\n");
        sb.Append("  }\n");
        sb.Append($"  vec4 outc = {expression};\n");
        sb.Append("  if (uAlphaTestEnable == 1 && !AlphaTest(outc.a)) discard;\n");
        sb.Append("  float dz = clamp(depthZ, 0.0, 1.0);\n");
        sb.Append("  if (uZetaBytes == 4) {\n");
        sb.Append("    gl_FragDepth = float(min(uint(dz * 16777215.0 + 0.5), 0xFFFFFFu)) / 16777215.0;\n");
        sb.Append("  } else if (uZetaBytes == 2) {\n");
        sb.Append("    gl_FragDepth = float(min(uint(dz * 65535.0 + 0.5), 0xFFFFu)) / 65535.0;\n");
        sb.Append("  } else {\n");
        sb.Append("    gl_FragDepth = dz;\n");
        sb.Append("  }\n");
        sb.Append("  fragColor = outc;\n");
    }

    // ---- register reads (mirror PshCombiner.ReadComponent /
    // ReadFinalComponent / ReadFinalAlpha with baked register ids) ----

    private static string StageConstExpr(PshCombiner state, int stage, bool isC0)
    {
        uint flags = state.Control >> 8;
        if (isC0)
        {
            return (flags & PshCombiner.ControlUniqueC0) != 0
                ? $"uConsts[{(stage * 2).ToString(CultureInfo.InvariantCulture)}]"
                : "uConsts[0]";
        }

        return (flags & PshCombiner.ControlUniqueC1) != 0
            ? $"uConsts[{((stage * 2) + 1).ToString(CultureInfo.InvariantCulture)}]"
            : "uConsts[1]";
    }

    private static string ReadVecExpr(PshCombiner state, int stage, uint word)
    {
        int reg = (int)(word & 0xFu);
        bool chan = (word & 0x10u) != 0;
        int mod = (int)(word & 0xE0u);
        if (chan)
        {
            string scalar = ReadScalarCore(state, stage, reg, chan: true, 3);
            return $"vec3(InMap({scalar}, {mod}))";
        }

        string x = ReadScalarCore(state, stage, reg, chan: false, 0);
        string y = ReadScalarCore(state, stage, reg, chan: false, 1);
        string z = ReadScalarCore(state, stage, reg, chan: false, 2);
        return $"vec3(InMap({x}, {mod}), InMap({y}, {mod}), InMap({z}, {mod}))";
    }

    private static string ReadAlphaExpr(PshCombiner state, int stage, uint word)
    {
        int reg = (int)(word & 0xFu);
        bool chan = (word & 0x10u) != 0;
        int component = chan ? 3 : 2;
        return ReadScalarCore(state, stage, reg, chan, component);
    }

    private static string ReadScalarCore(PshCombiner state, int stage, int reg, bool chan, int component)
    {
        switch (reg)
        {
            case PshCombiner.RegZero:
                return "0.0";
            case PshCombiner.RegC0:
            {
                string c = StageConstExpr(state, stage, isC0: true);
                return chan ? $"{c}.a" : SwizzleComponent(c, component);
            }

            case PshCombiner.RegC1:
            {
                string c = StageConstExpr(state, stage, isC0: false);
                return chan ? $"{c}.a" : SwizzleComponent(c, component);
            }

            case PshCombiner.RegFog:
                return chan ? "fog.a" : SwizzleComponent("fog", component);
            case PshCombiner.RegV0:
                return chan ? "color.a" : SwizzleComponent("color", component);
            case PshCombiner.RegV1:
                return chan ? "spec.a" : SwizzleComponent("spec", component);
            case 0x8:
                return chan ? "t0.a" : SwizzleComponent("t0", component);
            case 0x9:
                return chan ? "t1.a" : SwizzleComponent("t1", component);
            case 0xA:
                return chan ? "t2.a" : SwizzleComponent("t2", component);
            case 0xB:
                return chan ? "t3.a" : SwizzleComponent("t3", component);
            case PshCombiner.RegR0:
                return chan ? "r0.a" : SwizzleComponent("r0", component);
            case PshCombiner.RegR1:
                return chan ? "r1.a" : SwizzleComponent("r1", component);
            case PshCombiner.RegV1R0Sum:
                return V1R0SumExpr(state, component);
            case PshCombiner.RegEfProd:
            default:
                return "0.0";
        }
    }

    private static string SwizzleComponent(string name, int component)
    {
        return component switch
        {
            0 => $"{name}.x",
            1 => $"{name}.y",
            2 => $"{name}.z",
            _ => $"{name}.w",
        };
    }

    private static string V1R0SumExpr(PshCombiner state, int component)
    {
        if (component >= 3)
        {
            return "0.0";
        }

        uint settings = state.Final1 & 0xFFu;
        string sw = SwizzleComponent("spec", component);
        string rw = SwizzleComponent("r0", component);
        string vx = (settings & PshCombiner.FinalComplementV1) != 0 ? $"(1.0 - {sw})" : sw;
        string rx = (settings & PshCombiner.FinalComplementR0) != 0 ? $"(1.0 - {rw})" : rw;
        string sum = $"({vx} + {rx})";
        return (settings & PshCombiner.FinalClampSum) != 0 ? $"clamp({sum}, 0.0, 1.0)" : sum;
    }

    private static string ReadFinalVecExpr(PshCombiner state, uint word, bool efAvailable, string? ef)
    {
        int reg = (int)(word & 0xFu);
        bool chan = (word & 0x10u) != 0;
        if (efAvailable && reg == PshCombiner.RegEfProd)
        {
            return ef ?? "eF";
        }

        if (reg == PshCombiner.RegC0)
        {
            return chan ? "vec3(uConsts[16].a)" : "uConsts[16].rgb";
        }

        if (reg == PshCombiner.RegC1)
        {
            return chan ? "vec3(uConsts[17].a)" : "uConsts[17].rgb";
        }

        if (chan)
        {
            return $"vec3({ReadScalarCore(state, PshCombiner.MaxStages, reg, true, 3)})";
        }

        string x = ReadScalarCore(state, PshCombiner.MaxStages, reg, false, 0);
        string y = ReadScalarCore(state, PshCombiner.MaxStages, reg, false, 1);
        string z = ReadScalarCore(state, PshCombiner.MaxStages, reg, false, 2);
        return $"vec3({x}, {y}, {z})";
    }

    private static string ReadFinalAlphaExpr(PshCombiner state, uint word, string ef)
    {
        int reg = (int)(word & 0xFu);
        bool chan = (word & 0x10u) != 0;
        int component = chan ? 3 : 2;
        if (reg == PshCombiner.RegEfProd)
        {
            return component == 3 ? "0.0" : $"{ef}.z";
        }

        if (reg == PshCombiner.RegC0)
        {
            return SwizzleComponent("uConsts[16]", component);
        }

        if (reg == PshCombiner.RegC1)
        {
            return SwizzleComponent("uConsts[17]", component);
        }

        return ReadScalarCore(state, PshCombiner.MaxStages, reg, chan, component);
    }
}
