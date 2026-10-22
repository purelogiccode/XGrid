using System;
using System.IO;
using XGrid.Nv2a;
using Xunit;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// GLSL-generator string goldens (pure C#, headless). The golden sources
/// live under Goldens/ and are copied to the output directory.
/// </summary>
public sealed class PshGlslGeneratorTests
{
    private static PshCombiner MenuLikeCombiner()
    {
        // Observed menu shape (design §1.2/§3.1): stages=1,
        // control=00000001, final combiner enabled, texture modes NONE.
        var combiner = new PshCombiner
        {
            Control = 0x00000001u,
            StageProgram = 0u,
            Final0 = 0x00000000u,
            Final1 = 0x00000001u
        };
        combiner.RgbInputs[0] = 0x00000000u;
        combiner.RgbOutputs[0] = 0x00000000u;
        combiner.AlphaInputs[0] = 0x00000000u;
        combiner.AlphaOutputs[0] = 0x00000000u;
        return combiner;
    }

    [Fact]
    public void RegenerateGoldens_WhenRequested()
    {
        // One-shot tool: set XGRID_REGENERATE_GLSL_GOLDENS=1 after an
        // intentional shader change, run this test, then review the diff.
        if (!string.Equals(Environment.GetEnvironmentVariable("XGRID_REGENERATE_GLSL_GOLDENS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        string source = Path.Combine("..", "..", "..", "Goldens");
        string dir = Path.GetFullPath(source, AppContext.BaseDirectory);
        if (!Directory.Exists(dir))
        {
            dir = Path.Combine(AppContext.BaseDirectory, "Goldens");
        }

        var legacy = new PshCombiner();
        File.WriteAllText(Path.Combine(dir, "legacy.glsl"),
            PshGlslGenerator.BuildFragmentSource(legacy: true, legacy));
        File.WriteAllText(Path.Combine(dir, "menu-1s.glsl"),
            PshGlslGenerator.BuildFragmentSource(legacy: false, MenuLikeCombiner()));
    }
    private static string LoadGolden(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Goldens", name);
        return File.ReadAllText(path);
    }

    [Fact]
    public void BuildKey_IsDeterministicAndStateSensitive()
    {
        PshCombiner state = MenuLikeCombiner();
        string key1 = PshGlslGenerator.BuildKey(legacy: false, state);
        string key2 = PshGlslGenerator.BuildKey(legacy: false, state);
        Assert.Equal(key1, key2, StringComparer.Ordinal);

        state.Control = 0x00000002u;
        string key3 = PshGlslGenerator.BuildKey(legacy: false, state);
        Assert.NotEqual(key1, key3, StringComparer.Ordinal);

        // Factors are uniforms and must NOT split the key.
        state.Control = 0x00000001u;
        state.Factor0[0] = 0x11223344u;
        Assert.Equal(key1, PshGlslGenerator.BuildKey(legacy: false, state), StringComparer.Ordinal);

        // Legacy path keys separately.
        Assert.NotEqual(key1, PshGlslGenerator.BuildKey(legacy: true, state), StringComparer.Ordinal);
    }

    [Fact]
    public void LegacySource_MatchesGolden()
    {
        PshCombiner state = new();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: true, state);
        Assert.Equal(Normalize(LoadGolden("legacy.glsl")), Normalize(source), StringComparer.Ordinal);
    }

    [Fact]
    public void MenuOneStageFinalSource_MatchesGolden()
    {
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Equal(Normalize(LoadGolden("menu-1s.glsl")), Normalize(source), StringComparer.Ordinal);
    }

    [Fact]
    public void CombinerSource_ContainsSlice1Contract()
    {
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.StartsWith("#version 330 core", source, StringComparison.Ordinal);
        Assert.Contains("gl_FragDepth", source, StringComparison.Ordinal);
        Assert.Contains("uZclipCull", source, StringComparison.Ordinal);
        Assert.Contains("AlphaTest(", source, StringComparison.Ordinal);
        Assert.Contains("dF + (cF + ((bF - cF) * aF))", source, StringComparison.Ordinal);
        Assert.Contains("uTex0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipPlaneMode_EmitsDiscard()
    {
        PshCombiner state = MenuLikeCombiner();
        state.StageProgram = 5u; // slot 0 mode = CLIPPLANE (0x05)
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("uShaderClip >> 0u", source, StringComparison.Ordinal);
        Assert.Contains("discard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidStageMode_DegradesToNoneTexel()
    {
        // DOT_PRODUCT (0x11) is invalid in slot 0 -> none (software parity,
        // no unsupported count).
        PshCombiner state = MenuLikeCombiner();
        state.StageProgram = 0x11u;
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("t0 = vec4(0.0, 0.0, 0.0, 1.0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SampleSlot(0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PassthruMode_EmitsPassthruTexel()
    {
        PshCombiner state = MenuLikeCombiner();
        state.StageProgram = 0x04u; // slot 0 PASSTHRU
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("t0 = vec4(c0.xy, 0.0, 1.0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MuxMsbControlBit_SelectsHalfThreshold()
    {
        PshCombiner state = MenuLikeCombiner();
        state.Control = 0x00000101u; // stages=1, MUX_MSB set
        state.RgbOutputs[0] = 4u << 12; // FlagMux on the RGB output
        state.AlphaOutputs[0] = 4u << 12; // FlagMux on the alpha output
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("r0.a >= 0.5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mod(floor(max(r0.a", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MuxLsbControlBit_SelectsQuantizedBit()
    {
        PshCombiner state = MenuLikeCombiner();
        state.Control = 0x00000001u; // stages=1, MUX LSB mode
        state.RgbOutputs[0] = 4u << 12; // FlagMux on the RGB output
        state.AlphaOutputs[0] = 4u << 12; // FlagMux on the alpha output
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("mod(floor(max(r0.a, 0.0) * 255.0), 2.0) >= 1.0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PackedTriangleUniformBlock_ReplacesPerDrawUniformArrays()
    {
        // Track B: per-draw values travel as ONE packed vec4 block (uTri[24])
        // so a draw is a single uniform upload. Pins the layout contract the
        // backend's PackVertices/StoreVertex writes into and the per-draw
        // submission optimization that came out of the 2026-10-20 present-path
        // measurement (13 glUniform calls per draw -> 1).
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("uniform vec4 uTri[25];", source, StringComparison.Ordinal);
        Assert.Contains("vec2 vA = T(0).xy;", source, StringComparison.Ordinal);
        Assert.Contains("vec2 vB = T(0).zw;", source, StringComparison.Ordinal);
        Assert.Contains("vec2 vC = T(1).xy;", source, StringComparison.Ordinal);
        Assert.Contains("gl_FragCoord.y + T(2).z", source, StringComparison.Ordinal);
        Assert.Contains("if (T(1).z == 1.0)", source, StringComparison.Ordinal);
        Assert.Contains("(p1 * T(3)) + (p2 * T(4)) + (p0 * T(5))", source, StringComparison.Ordinal);
        Assert.Contains("(p1 * T(21).z) + (p2 * T(22).z) + (p0 * T(23).z)", source, StringComparison.Ordinal);
        foreach (string retired in new[]
                 {
                     "uniform vec2 uPx[3];", "uniform vec4 uColorV[3];", "uniform float uClipY;",
                     "uniform int uPerspective;",
                 })
        {
            Assert.DoesNotContain(retired, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BatchedTriangleBlocks_IndexTheBlockByInstance()
    {
        // Track B draw-command batching: one instanced draw covers a whole
        // PGRAPH primitive flush, so the program addresses `capacity` packed
        // blocks and both stages resolve the block with the flat instance
        // index (0 for a non-instanced draw, which keeps the single-triangle
        // path on block 0). Values are identical float32 reads either way.
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state, triangleCapacity: 8);
        Assert.Contains("uniform vec4 uTri[200];", source, StringComparison.Ordinal);
        Assert.Contains("flat in int vTri;", source, StringComparison.Ordinal);
        Assert.Contains("#define T(i) uTri[((vTri) * 25) + (i)]", source, StringComparison.Ordinal);
        Assert.Contains("vec2 vA = T(0).xy;", source, StringComparison.Ordinal);

        // Default capacity is the single-triangle program.
        string single = PshGlslGenerator.BuildFragmentSource(legacy: true, state);
        Assert.Contains("uniform vec4 uTri[25];", single, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PshGlslGenerator.BuildFragmentSource(legacy: false, state, triangleCapacity: 0));
    }

    [Fact]
    public void SamplerHonorsResolvedWrapModesAndTexFilterUniforms()
    {
        // GL TEXFILTER parity (compatibility gap 7): the shader reads the
        // seam's resolved sampler state as uniforms, so the program cache key
        // does not split on filter/address churn.
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("uniform int uTexBilinear[4];", source, StringComparison.Ordinal);
        Assert.Contains("uniform int uTexConvolution[4];", source, StringComparison.Ordinal);
        Assert.Contains("uniform vec4 uTexBorder[4];", source, StringComparison.Ordinal);

        // Nearest path: one tap through SampleTap, which owns the border rule.
        Assert.Contains("return SampleTap(slot, ivec2(int(floor(c.x)), int(floor(c.y))));", source,
            StringComparison.Ordinal);
        Assert.Contains("bool outU = wu == 3 && (t.x < 0 || t.x >= size.x);", source, StringComparison.Ordinal);
        Assert.Contains("if (outU || outV) return uTexBorder[slot];", source, StringComparison.Ordinal);

        // The filter registers are per-draw state, not shader structure: the
        // program cache key never carries them, so switching them on does not
        // recompile a program.
        Assert.DoesNotContain("uTexBilinear", PshGlslGenerator.BuildKey(legacy: false, state), StringComparison.Ordinal);
        Assert.DoesNotContain("uTexConvolution", PshGlslGenerator.BuildKey(legacy: false, state), StringComparison.Ordinal);
    }

    [Fact]
    public void TentFilter_UsesTexelCentreOffsetAndFourIndependentlyWrappedTaps()
    {
        // Same arithmetic as Nv2AMmio.SampleBilinear: fx = c - 0.5, four taps
        // wrapped per axis, the exact weight products.
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("if (uTexBilinear[slot] != 1) {", source, StringComparison.Ordinal);
        Assert.Contains("float fx = c.x - 0.5;", source, StringComparison.Ordinal);
        Assert.Contains("float fy = c.y - 0.5;", source, StringComparison.Ordinal);
        Assert.Contains("vec4 t00 = SampleTap(slot, ivec2(x0, y0));", source, StringComparison.Ordinal);
        Assert.Contains("vec4 t11 = SampleTap(slot, ivec2(x0 + 1, y0 + 1));", source, StringComparison.Ordinal);
        Assert.Contains("float w00 = (1.0 - wx) * (1.0 - wy);", source, StringComparison.Ordinal);
        Assert.Contains("return (t00 * w00) + (t10 * w10) + (t01 * w01) + (t11 * w11);", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Convolution_EmitsNineOneTwoOneTapsOnProject2D()
    {
        // xemu apply_convolution_filter: nine textureProj taps with the 1-2-1
        // kernel (weights sum to 16/16), one texel apart, only on PROJECT2D
        // and only when MIN is CONVOLUTION_2D_LOD0 (the backend resolves that
        // into uTexConvolution).
        PshCombiner state = MenuLikeCombiner();
        state.StageProgram = 0x01u; // slot 0 PROJECT2D
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("vec4 SampleConvolved(int slot, vec3 p) {", source, StringComparison.Ordinal);
        Assert.Contains("acc += SampleSlot(slot, base + vec2(-1.0 * du, -1.0 * dv)) * 0.0625;", source,
            StringComparison.Ordinal);
        Assert.Contains("acc += SampleSlot(slot, base + vec2(0.0 * du, 0.0 * dv)) * 0.2500;", source,
            StringComparison.Ordinal);
        int taps = CountOccurrences(source, "acc += SampleSlot(");
        Assert.Equal(9, taps);
        Assert.Contains("else if (uTexConvolution[0] != 0) { t0 = SampleConvolved(0, c0.xyw); }", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DotAndCubeModes_EmitTranscribedMath_NotAStubTexel()
    {
        // B3 slice 1: the dot/reflection/cube helpers carry the software
        // sampler's math (Nv2AMmio.TryComputeDot / ResolveDotReflection /
        // TrySampleTextureCube / DecodeTextureTexel) as real GLSL, so the
        // declined modes are no longer a stub texel. Rounding the float scale
        // fixes nothing; each assertion is an expression the software path
        // computes the same way.
        PshCombiner state = MenuLikeCombiner();
        // Stage 0: PROJECT2D, stage 1: DOT_PRODUCT, stage 2: DOT_PRODUCT,
        // stage 3: DOT_REFLECT_SPECULAR (the cubemap sample's own program).
        state.StageProgram = 0x01u | (0x11u << 5) | (0x11u << 10) | (0x0Cu << 15);
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);

        // The spliced-constant dot: dot(pT.xyz, dotmap(input texel)). A HILO
        // map (4-7) consumes the input's raw 16-bit pair, which is the guest
        // word the seam carried (B3 slice 2), not a decoded colour.
        Assert.Contains("bool StageDot(int stage, vec3 coord, vec4 inTexel, int inSlot, out float d)", source, StringComparison.Ordinal);
        Assert.Contains("d = dot(coord, DotMap(map, inTexel));", source, StringComparison.Ordinal);
        Assert.Contains("uint pair = PoolWord(inSlot, 0);", source, StringComparison.Ordinal);
        Assert.Contains($"uniform uint uGuestWord[{PshGlslGenerator.GuestWordPoolSize * 4}];", source, StringComparison.Ordinal);
        Assert.Contains("uniform int uTexPoolBase[4];", source, StringComparison.Ordinal);

        // Dot maps 0-7, decoded from the texel with the reference sign
        // functions (not a threshold on the float).
        Assert.Contains("float Sign1(float x) { return ((x * 255.0) - 128.0) / 127.0; }", source, StringComparison.Ordinal);
        Assert.Contains("float Sign3(float x) { float s = x * 255.0; return s >= 128.0 ? (s - 256.0) / 127.0 : s / 127.0; }", source, StringComparison.Ordinal);
        Assert.Contains("if (m == 4) return vec3(hi / 65535.0, lo / 65535.0, 1.0);", source, StringComparison.Ordinal);

        // DOT_PRODUCT keeps the zero texel but still computes the chain, and
        // every dot value/flag is named per stage: stage 1 and stage 2 both
        // compute a dot in the same function scope, and a shared name is a
        // redeclaration the driver rejects (this program failed to compile).
        Assert.Contains("if (!dotVal1Ok) { t1 = vec4(0.0, 0.0, 0.0, 1.0); } else { t1 = vec4(0.0); }", source,
            StringComparison.Ordinal);
        Assert.Contains("if (!dotVal2Ok) { t2 = vec4(0.0, 0.0, 0.0, 1.0); } else { t2 = vec4(0.0); }", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("bool dotOk", source, StringComparison.Ordinal);

        // The specular reflection: rv = 2*n*dot(n,e)/dot(n,n) - e with the
        // eye from the specular slots' w components.
        Assert.Contains("vec3 SpecularReflect(vec3 n, vec3 e)", source, StringComparison.Ordinal);
        Assert.Contains("return (scale * n) - e;", source, StringComparison.Ordinal);
        Assert.Contains("SpecularReflect(vec3(dotA3, dotB3, dotC3), vec3(c1.w, c2.w, c3.w))", source,
            StringComparison.Ordinal);

        // Cube sampling: the face select order, and one tap of the host
        // 2D-array texture the backend decoded from the guest's swizzled faces
        // (layer = face), never a stub zero or a carried-window word.
        Assert.Contains("int CubeFaceSelect(vec3 d, out vec2 st)", source, StringComparison.Ordinal);
        Assert.Contains("if (slot == 1) return texelFetch(uCube1, t, 0);", source, StringComparison.Ordinal);
        Assert.Contains("return FetchCubeTexel(slot, ivec3(tx, ty, face));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("uint TexelWord(int addr)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cube face is a host 2D-array texture the backend decodes with the
    /// software sampler's own decoder, so the shader must not re-implement the
    /// per-format channel math (that is how a dual-rasterizer decoder drifts -
    /// the removed descriptor path returned the wrong channels and never
    /// fired). An unresolved cube stage is the software's None texel, never a
    /// guessed white.
    /// </summary>
    [Fact]
    public void CubeTexelDecode_IsAHostArrayFetch_NotAShaderFormatDecode()
    {
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("uniform sampler2DArray uCube0;", source, StringComparison.Ordinal);
        Assert.Contains("uniform sampler2DArray uCube3;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("uTexFieldOff", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DecodeGuestTexel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DecodeField", source, StringComparison.Ordinal);
        Assert.Contains("if (uTexCubeOk[slot] != 1) return vec4(0.0, 0.0, 0.0, 1.0);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CubeDotState_StaysOutOfTheProgramCacheKey()
    {
        // Per-draw state (extents, masks, dot maps, eye) travels as uniforms:
        // switching a texture mode never recompiles.
        PshCombiner state = MenuLikeCombiner();
        string key = PshGlslGenerator.BuildKey(legacy: false, state);
        foreach (string uniform in new[] { "uTexCubeOk", "uTexSwzMaskX", "uCube0", "uDotMap", "uEyePos" })
        {
            Assert.DoesNotContain(uniform, key, StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    [Fact]
    public void PreambleAddsTheClipOriginToBothFragmentAxes()
    {
        // The software fill works in absolute surface coordinates and clips
        // against clipX/clipY; the GL viewport is clip-relative, so the
        // fragment shader must add the clip origin on BOTH axes (it added only
        // ClipY). diag[clipXY=] counts legs where this can matter.
        PshCombiner state = MenuLikeCombiner();
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("float px = gl_FragCoord.x + T(2).w;", source, StringComparison.Ordinal);
        Assert.Contains("float py = gl_FragCoord.y + T(2).z;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project2D_DividesProjectiveCoordinatesByW()
    {
        // Reference textureProj(norm(pT.xyw)): the 2D coords divide by w even
        // without a cube map; a zero w keeps the undivided pair (software
        // Nv2AMmio.ResolveTexel).
        PshCombiner state = MenuLikeCombiner();
        state.StageProgram = 0x01u; // slot 0 PROJECT2D
        string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state);
        Assert.Contains("t0 = SampleSlot(0, (c0.w != 0.0) ? (c0.xy / c0.w) : c0.xy);", source,
            StringComparison.Ordinal);

        // The legacy path samples oT0 without the projection (software
        // FillTriangle has no texture stage to project).
        string legacy = PshGlslGenerator.BuildFragmentSource(legacy: true, state);
        Assert.Contains("outcColor *= SampleSlot(0, c0.xy);", legacy, StringComparison.Ordinal);
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
    }
}
