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
        var combiner = new PshCombiner();
        combiner.Control = 0x00000001u;
        combiner.StageProgram = 0u;
        combiner.Final0 = 0x00000000u;
        combiner.Final1 = 0x00000001u;
        combiner.RgbInputs[0] = 0x00000000u;
        combiner.RgbOutputs[0] = 0x00000000u;
        combiner.AlphaInputs[0] = 0x00000000u;
        combiner.AlphaOutputs[0] = 0x00000000u;
        return combiner;
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

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
    }
}
