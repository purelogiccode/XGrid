using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the fixed-function lighting stage: the enabled-light loop
/// (infinite/local/spot with attenuation and the spot cone), the
/// material-source selection of the light terms and the reconstructed
/// specular exponent.
/// </summary>
/// <remarks>
/// Reference: xemu 0.8.137 <c>hw/xbox/nv2a/pgraph/glsl/vsh-ff.c</c> (the
/// <c>SET_LIGHT_CONTROL_LOCALEYE</c> view vector, the per-light block with
/// <c>nDotVP</c>/<c>nDotHV</c>/<c>pf</c> and the three material-source
/// switches) and <c>pgraph.c</c> for the register decode
/// (<c>SET_LIGHT_AMBIENT_COLOR</c> .. +8*32 words at a 0x80-byte light stride)
/// and <c>reconstruct_specular_power</c>.
/// </remarks>
public sealed class Nv2aFixedFunctionLightingTests
{
    private const int ModelViewMatrix = 0x08;
    private const int InverseModelViewMatrix = 0x0C;
    private const int EyePosition = 0x38;

    private const int Position = 0;
    private const int Normal = 2;
    private const int Diffuse = 3;
    private const int Specular = 4;

    private const uint LightInfinite = 1u;
    private const uint LightLocal = 2u;
    private const uint LightSpot = 3u;

    private static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    private static float[][] Constants()
    {
        var constants = new float[VshProgram.ConstantCount][];
        for (int i = 0; i < constants.Length; i++)
        {
            constants[i] = new float[4];
        }

        for (int row = 0; row < 4; row++)
        {
            constants[ModelViewMatrix + row][row] = 1f;
            constants[InverseModelViewMatrix + row][row] = 1f;
        }

        return constants;
    }

    private static float[][] Inputs()
    {
        var inputs = new float[VshProgram.RegisterCount][];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new float[4];
            inputs[i][3] = 1f;
        }

        inputs[Diffuse][0] = 1f;
        inputs[Diffuse][1] = 1f;
        inputs[Diffuse][2] = 1f;
        inputs[Specular][0] = 0.25f;
        inputs[Specular][1] = 0.5f;
        inputs[Specular][2] = 0.75f;
        return inputs;
    }

    private static float[][] Outputs()
    {
        var outputs = new float[VshProgram.RegisterCount][];
        for (int i = 0; i < outputs.Length; i++)
        {
            outputs[i] = new float[4];
            outputs[i][3] = 1f;
        }

        return outputs;
    }

    /// <summary>
    /// Runs the stage on a lit state whose material chain is zero (scene
    /// ambient, material emission and every source selection produce nothing
    /// on their own), so the outputs show the light terms alone.
    /// </summary>
    private static float[][] RunLit(
        FixedFunctionVertexState state,
        float[][] inputs,
        float[][]? constants = null)
    {
        state.Lighting = true;
        constants ??= Constants();
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);
        return outputs;
    }

    private static void AssertVector(ReadOnlySpan<float> actual, float x, float y, float z)
    {
        Assert.Equal(3, actual.Length);
        Assert.Equal(x, actual[0]);
        Assert.Equal(y, actual[1]);
        Assert.Equal(z, actual[2]);
    }

    [Fact]
    public void Execute_InfiniteLightAddsItsAmbientAndDiffuseTerms()
    {
        // Ambient (0.1,0.2,0.3) is added unconditionally, the diffuse color is
        // scaled by attenuation 1 and nDotVP = 1 (light direction and normal
        // both +Z), and the material diffuse source is MATERIAL so the term is
        // added unmodified.
        FixedFunctionVertexState state = new() { LightEnableMask = LightInfinite };
        state.TrySetLightMethod(0x1000u, F(0.1f));
        state.TrySetLightMethod(0x1004u, F(0.2f));
        state.TrySetLightMethod(0x1008u, F(0.3f));
        for (int i = 0; i < 3; i++)
        {
            state.TrySetLightMethod(0x100Cu + ((uint)i * 4u), F(0.5f));
        }

        state.TrySetLightMethod(0x1034u, F(0f));
        state.TrySetLightMethod(0x1038u, F(0f));
        state.TrySetLightMethod(0x103Cu, F(1f));

        float[][] inputs = Inputs();
        inputs[Normal][2] = 1f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(0.6f, outputs[Diffuse][0]);
        Assert.Equal(0.7f, outputs[Diffuse][1]);
        Assert.Equal(0.8f, outputs[Diffuse][2]);
    }

    [Fact]
    public void Execute_InfiniteLightWithoutLocalEyeUsesTheUploadedHalfVectorUnnormalized()
    {
        // Normal +Y, light direction +Y, uploaded half vector (0.6,0.8,0):
        // nDotVP = 1, nDotHV = 0.8 (the reference does not normalize the
        // uploaded half vector), specular power 2 -> pf = 0.8^2.
        FixedFunctionVertexState state = new() { LightEnableMask = LightInfinite, SpecularEnable = true, SeparateSpecular = true };
        state.TrySetLightMethod(0x1034u, F(0f));
        state.TrySetLightMethod(0x1038u, F(1f));
        state.TrySetLightMethod(0x103Cu, F(0f));
        state.TrySetLightMethod(0x1028u, F(0.6f));
        state.TrySetLightMethod(0x102Cu, F(0.8f));
        state.TrySetLightMethod(0x1030u, F(0f));
        state.TrySetLightMethod(0x1018u, F(1f));
        state.TrySetLightMethod(0x101Cu, F(1f));
        state.TrySetLightMethod(0x1020u, F(1f));

        // The low-power shortcut: power = c2 - 2 * c0 = 0 - 2 * (-1) = 2.
        state.TrySetSpecularParams(0x09E0u, F(-1f));
        state.TrySetSpecularParams(0x09E4u, F(1f));
        for (uint method = 0x09E8u; method <= 0x09F4u; method += 4u)
        {
            state.TrySetSpecularParams(method, F(0f));
        }

        float[][] inputs = Inputs();
        inputs[Normal][1] = 1f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(2f, state.SpecularPower);
        Assert.Equal(MathF.Pow(0.8f, 2f), outputs[Specular][0]);
    }

    [Fact]
    public void Execute_LocalEyeComputedHalfVectorUsesTheEyePosition()
    {
        // Eye at (0,0,1), vertex at the origin, light at (1,0,0):
        // VP = (1,0,0), VPeye = (0,0,1), halfVector = normalize(VP + VPeye),
        // normal (1,0,0) -> nDotHV = 1/sqrt(2), nDotVP = 1, power 1 -> pf =
        // 1/sqrt(2).
        FixedFunctionVertexState state = new()
        {
            LightEnableMask = LightLocal << 2,
            SpecularEnable = true,
            SeparateSpecular = true,
            LocalEye = true,
        };
        state.TrySetLightMethod(0x1080u + 0x5Cu, F(1f));
        state.TrySetLightMethod(0x1080u + 0x60u, F(0f));
        state.TrySetLightMethod(0x1080u + 0x64u, F(0f));
        state.TrySetLightMethod(0x1080u + 0x24u, F(10f));
        state.TrySetLightMethod(0x1080u + 0x68u, F(1f));
        state.TrySetLightMethod(0x1080u + 0x18u, F(1f));
        state.TrySetLightMethod(0x1080u + 0x1Cu, F(1f));
        state.TrySetLightMethod(0x1080u + 0x20u, F(1f));

        float[][] constants = Constants();
        constants[EyePosition][2] = 1f;
        constants[EyePosition][3] = 1f;

        // power = c2 - 2*c0 = 0 - 2*(-0.5) = 1.
        state.TrySetSpecularParams(0x09E0u, F(-0.5f));
        state.TrySetSpecularParams(0x09E4u, F(1f));
        for (uint method = 0x09E8u; method <= 0x09F4u; method += 4u)
        {
            state.TrySetSpecularParams(method, F(0f));
        }

        float[][] inputs = Inputs();
        inputs[Normal][0] = 1f;
        float[][] outputs = RunLit(state, inputs, constants);

        Assert.Equal(1f, state.SpecularPower);
        Assert.Equal(MathF.Sqrt(0.5f), outputs[Specular][0], 6);
    }

    [Fact]
    public void Execute_LocalLightOutsideItsRangeContributesNothing()
    {
        // Local light at the origin with range 1; the vertex sits at x = 5, so
        // the reference skips the whole light block (ambient included).
        FixedFunctionVertexState state = new() { LightEnableMask = LightLocal };
        state.TrySetLightMethod(0x1080u + 0x5Cu, F(0f));
        state.TrySetLightMethod(0x1080u + 0x24u, F(1f));
        state.TrySetLightMethod(0x1000u, F(1f));
        state.TrySetLightMethod(0x1004u, F(1f));
        state.TrySetLightMethod(0x1008u, F(1f));

        float[][] inputs = Inputs();
        inputs[Position][0] = 5f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(0f, outputs[Diffuse][0]);
        Assert.Equal(0f, outputs[Diffuse][1]);
        Assert.Equal(0f, outputs[Diffuse][2]);
    }

    [Fact]
    public void Execute_LocalLightAttenuatesWithTheDistanceConstants()
    {
        // Light at the origin, vertex at x = 2, attenuation (2, 0.5, 0):
        // 1 / (2 + 0.5*2 + 0*4) = 1/3. The normal is +Z while VP is -X, so
        // nDotVP = 0 and only the ambient term survives.
        FixedFunctionVertexState state = new() { LightEnableMask = LightLocal };
        state.TrySetLightMethod(0x1000u + 0x5Cu, F(0f));
        state.TrySetLightMethod(0x1000u + 0x60u, F(0f));
        state.TrySetLightMethod(0x1000u + 0x64u, F(0f));
        state.TrySetLightMethod(0x1000u + 0x24u, F(10f));
        state.TrySetLightMethod(0x1000u + 0x68u, F(2f));
        state.TrySetLightMethod(0x1000u + 0x6Cu, F(0.5f));
        state.TrySetLightMethod(0x1000u + 0x70u, F(0f));
        state.TrySetLightMethod(0x1000u, F(0.3f));

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Normal][2] = 1f;
        float[][] outputs = RunLit(state, inputs);

        float attenuation = 1f / (2f + (0.5f * 2f));
        Assert.Equal(0.3f * attenuation, outputs[Diffuse][0]);
    }

    [Fact]
    public void Execute_LocalLightDiffuseUsesTheMaterialSource()
    {
        // With the diffuse source = the diffuse attribute, the light diffuse
        // term is multiplied by the attribute (reference vsh-ff.c switch).
        FixedFunctionVertexState state = new()
        {
            LightEnableMask = LightLocal,
            DiffuseSource = FixedFunctionVertex.MaterialColorSourceDiffuse,
        };
        state.TrySetLightMethod(0x1000u + 0x5Cu, F(0f));
        state.TrySetLightMethod(0x1000u + 0x24u, F(10f));
        state.TrySetLightMethod(0x1000u + 0x68u, F(1f));
        state.TrySetLightMethod(0x1000u + 0x0Cu, F(1f));
        state.TrySetLightMethod(0x1000u + 0x10u, F(1f));
        state.TrySetLightMethod(0x1000u + 0x14u, F(1f));

        // Vertex at x = 2, light at the origin: VP = -X, normal = -X.
        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Normal][0] = -1f;
        inputs[Diffuse][0] = 0.5f;
        inputs[Diffuse][1] = 0.25f;
        inputs[Diffuse][2] = 0.125f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(0.5f, outputs[Diffuse][0], 6);
        Assert.Equal(0.25f, outputs[Diffuse][1], 6);
        Assert.Equal(0.125f, outputs[Diffuse][2], 6);
    }

    [Theory]
    [InlineData(0.5f, 1f)] // w = 0.5: cosHalfTheta = 0.5, rho = 1 > it -> full
    [InlineData(-0.5f, 0.5f)] // partial: attenuation *= dot + w = 0.5
    [InlineData(-1f, 0f)] // w = -1: cosHalfPhi = 1, rho = 1 <= it -> 0
    public void Execute_SpotLightAppliesTheConeFalloff(float coneWord, float expectedScale)
    {
        // Spot light at the origin, vertex at z = 5 looking down -Z; the spot
        // direction is (0,0,-1) with the cone cosine in .w. The ambient term is
        // (0.4,0,0) so the attenuation scales it directly.
        FixedFunctionVertexState state = new() { LightEnableMask = LightSpot };
        state.TrySetLightMethod(0x1000u + 0x5Cu, F(0f));
        state.TrySetLightMethod(0x1000u + 0x60u, F(0f));
        state.TrySetLightMethod(0x1000u + 0x64u, F(0f));
        state.TrySetLightMethod(0x1000u + 0x24u, F(10f));
        state.TrySetLightMethod(0x1000u + 0x68u, F(1f));
        state.TrySetLightMethod(0x1000u + 0x4Cu, F(0f));
        state.TrySetLightMethod(0x1000u + 0x50u, F(0f));
        state.TrySetLightMethod(0x1000u + 0x54u, F(-1f));
        state.TrySetLightMethod(0x1000u + 0x58u, F(coneWord));
        state.TrySetLightMethod(0x1000u, F(0.4f));

        float[][] inputs = Inputs();
        inputs[Position][2] = 5f;
        inputs[Normal][0] = 1f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(0.4f * expectedScale, outputs[Diffuse][0], 6);
    }

    [Fact]
    public void Execute_SpecularDisabledDiscardsTheLightSpecularTerm()
    {
        // The light loop accumulates oD1, but the trailing specular rules
        // overwrite it with opaque black when SET_SPECULAR_ENABLE is off.
        FixedFunctionVertexState state = new() { LightEnableMask = LightInfinite };
        state.TrySetLightMethod(0x1034u, F(0f));
        state.TrySetLightMethod(0x1038u, F(0f));
        state.TrySetLightMethod(0x103Cu, F(1f));
        state.TrySetLightMethod(0x1018u, F(1f));
        state.TrySetLightMethod(0x101Cu, F(1f));
        state.TrySetLightMethod(0x1020u, F(1f));

        float[][] inputs = Inputs();
        inputs[Normal][2] = 1f;
        float[][] outputs = RunLit(state, inputs);

        Assert.Equal(0f, outputs[Specular][0]);
        Assert.Equal(1f, outputs[Specular][3]);
    }

    [Fact]
    public void State_LightMethodWindowDecodesEveryProperty()
    {
        FixedFunctionVertexState state = new();

        Assert.False(state.TrySetLightMethod(0x0FFCu, 0u));
        Assert.False(state.TrySetLightMethod(0x1400u, 0u));
        Assert.True(state.TrySetLightMethod(0x13FCu, 0u));

        const uint light = 3u * 0x80u;
        state.TrySetLightMethod(0x1000u + light, F(0.25f));
        state.TrySetLightMethod(0x1004u + light, F(0.5f));
        state.TrySetLightMethod(0x1008u + light, F(0.75f));
        state.TrySetLightMethod(0x100Cu + light, F(0.125f));
        state.TrySetLightMethod(0x1010u + light, F(0.25f));
        state.TrySetLightMethod(0x1014u + light, F(0.375f));
        state.TrySetLightMethod(0x1018u + light, F(0.5f));
        state.TrySetLightMethod(0x101Cu + light, F(0.625f));
        state.TrySetLightMethod(0x1020u + light, F(0.75f));
        state.TrySetLightMethod(0x1024u + light, F(12.5f));
        state.TrySetLightMethod(0x1028u + light, F(0.1f));
        state.TrySetLightMethod(0x102Cu + light, F(0.2f));
        state.TrySetLightMethod(0x1030u + light, F(0.3f));
        state.TrySetLightMethod(0x1034u + light, F(0.4f));
        state.TrySetLightMethod(0x1038u + light, F(0.5f));
        state.TrySetLightMethod(0x103Cu + light, F(0.6f));
        state.TrySetLightMethod(0x1040u + light, F(0.7f));
        state.TrySetLightMethod(0x1044u + light, F(0.8f));
        state.TrySetLightMethod(0x1048u + light, F(0.9f));
        state.TrySetLightMethod(0x104Cu + light, F(1.5f));
        state.TrySetLightMethod(0x1050u + light, F(2.5f));
        state.TrySetLightMethod(0x1054u + light, F(3.5f));
        state.TrySetLightMethod(0x1058u + light, F(4.5f));
        state.TrySetLightMethod(0x105Cu + light, F(5.5f));
        state.TrySetLightMethod(0x1060u + light, F(6.5f));
        state.TrySetLightMethod(0x1064u + light, F(7.5f));
        state.TrySetLightMethod(0x1068u + light, F(8.5f));
        state.TrySetLightMethod(0x106Cu + light, F(9.5f));
        state.TrySetLightMethod(0x1070u + light, F(10.5f));

        AssertVector(state.GetLightAmbient(3), 0.25f, 0.5f, 0.75f);
        AssertVector(state.GetLightDiffuse(3), 0.125f, 0.25f, 0.375f);
        AssertVector(state.GetLightSpecular(3), 0.5f, 0.625f, 0.75f);
        Assert.Equal(12.5f, state.GetLightLocalRange(3));
        AssertVector(state.GetLightInfiniteHalfVector(3), 0.1f, 0.2f, 0.3f);
        AssertVector(state.GetLightInfiniteDirection(3), 0.4f, 0.5f, 0.6f);
        AssertVector(state.GetLightSpotFalloff(3), 0.7f, 0.8f, 0.9f);
        Assert.Equal(4, state.GetLightSpotDirection(3).Length);
        Assert.Equal(1.5f, state.GetLightSpotDirection(3)[0]);
        Assert.Equal(2.5f, state.GetLightSpotDirection(3)[1]);
        Assert.Equal(3.5f, state.GetLightSpotDirection(3)[2]);
        Assert.Equal(4.5f, state.GetLightSpotDirection(3)[3]);
        AssertVector(state.GetLightLocalPosition(3), 5.5f, 6.5f, 7.5f);
        AssertVector(state.GetLightLocalAttenuation(3), 8.5f, 9.5f, 10.5f);

        // The block is per light: light 1 stayed at zero.
        Assert.Equal(0f, state.GetLightAmbient(1)[0]);
        Assert.Equal(0f, state.GetLightLocalPosition(1)[0]);

        // The padding words are accepted and ignored.
        Assert.True(state.TrySetLightMethod(0x1074u + light, F(99f)));
        Assert.Equal(10.5f, state.GetLightLocalAttenuation(3)[2]);
    }

    [Fact]
    public void State_LightTypeComesFromTheEnableMask()
    {
        // 0x1B = 0b11011: light 0 = spot (3), light 1 = local (2), light 2 =
        // infinite (1), light 3 = off (0).
        FixedFunctionVertexState state = new() { LightEnableMask = 0x0000_001B };
        Assert.Equal(FixedFunctionVertex.LightSpot, state.GetLightType(0));
        Assert.Equal(FixedFunctionVertex.LightLocal, state.GetLightType(1));
        Assert.Equal(FixedFunctionVertex.LightInfinite, state.GetLightType(2));
        Assert.Equal(FixedFunctionVertex.LightOff, state.GetLightType(3));
    }

    [Fact]
    public void State_SpecularParamsRecomputeThePowerOnTheSixthWord()
    {
        FixedFunctionVertexState state = new();
        state.TrySetSpecularParams(0x09E0u, F(-1f));
        state.TrySetSpecularParams(0x09E4u, F(1f));
        state.TrySetSpecularParams(0x09E8u, F(0f));
        state.TrySetSpecularParams(0x09ECu, F(0f));
        state.TrySetSpecularParams(0x09F0u, F(0f));

        // Five words are not enough: the reference only reconstructs at slot 5.
        Assert.Equal(0f, state.SpecularPower);
        Assert.False(state.TrySetSpecularParams(0x09F8u, 0u));

        state.TrySetSpecularParams(0x09F4u, F(0f));
        Assert.Equal(2f, state.SpecularPower);
        Assert.Equal([-1f, 1f, 0f, 0f, 0f, 0f], state.GetSpecularParams().ToArray());
    }

    [Fact]
    public void SpecularPowerCurve_UsesTheLowPowerShortcut()
    {
        // c1 > 0 and c2 < 1: power = c2 - 2*c0.
        Assert.Equal(3.25f, SpecularPowerCurve.Reconstruct([-1.25f, 0.5f, 0.75f, -0.5f]));
    }

    [Fact]
    public void SpecularPowerCurve_UsesTheQuadraticAndGrowthRateFits()
    {
        // Segment 2 (c0 in (-0.6787, -0.4215]) is quadratic, segment 3
        // (c0 in (-0.8389, -0.6787]) is the saturation-growth-rate fit.
        float quadratic = 8.653441252033f + (29.189473787191f * -0.5f) + (43.586027561823f * -0.5f * -0.5f);
        Assert.Equal(quadratic, SpecularPowerCurve.Reconstruct([-0.5f, 0f, 1f, 0f]), 4);

        float growth = (-531.307758450301f * -0.8f) / (117.398468683934f + (113.155490738338f * -0.8f));
        Assert.Equal(growth, SpecularPowerCurve.Reconstruct([-0.8f, 0f, 1f, -0.5f]), 3);
    }

    [Fact]
    public void SpecularPowerCurve_ExtendsTheRangeFromTheHalfPower()
    {
        // c3's reconstruction beats c0's and c0 < -0.1: the reference doubles
        // the half power.
        float power = SpecularPowerCurve.Reconstruct([-0.3f, 0f, 1f, -0.99f]);
        float expected = 2f * ((-124.435242105211f * -0.99f) / (35.401219563514f + (35.408114377045f * -0.99f)));
        Assert.Equal(expected, power, 1);
    }

    [Fact]
    public void SpecularPowerCurve_GuardsPositiveCoefficients()
    {
        // A positive c0 or c3 cannot come from the DirectX driver; the
        // reference returns a tiny finite power.
        Assert.Equal(0.0001f, SpecularPowerCurve.Reconstruct([0.5f, 0f, 1f, 0f]));
        Assert.Equal(0.0001f, SpecularPowerCurve.Reconstruct([-0.5f, 0f, 1f, 0.25f]));
    }

    [Fact]
    public void Draw_InfiniteLightContributesToTheSurface()
    {
        // Device-level: the lit draw with light 0 infinite, ambient (0,1,0)
        // and diffuse (1,0,0), the vertex normal +Z and the light direction +Z
        // writes (1,1,0,1) - the material chain itself is zero here.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.FixedFunctionTransform = true;
        WriteAttribute(
            memory,
            0x2000u,
            [[0f, 0f, 0f, 1f], [2f, 0f, 0f, 1f], [0f, 2f, 0f, 1f]]);
        WriteAttribute(
            memory,
            0x2100u,
            [[0f, 0f, 1f, 0f], [0f, 0f, 1f, 0f], [0f, 0f, 1f, 0f]]);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x1728u, 0x00002100u),
            (0x1768u, 0x00001042u),
            (0x1E94u, 0x00000004u),
            (0x0314u, 1u),          // lighting enable
            (0x03BCu, 1u),          // light 0 = infinite
            (0x03B4u, F(1f)),       // material alpha
            (0x1000u, F(0f)),       // light 0 ambient r
            (0x1004u, F(1f)),       // light 0 ambient g
            (0x1008u, F(0f)),       // light 0 ambient b
            (0x100Cu, F(1f)),       // light 0 diffuse r
            (0x1010u, F(0f)),       // light 0 diffuse g
            (0x1014u, F(0f)),       // light 0 diffuse b
            (0x1034u, F(0f)),       // light 0 direction r
            (0x1038u, F(0f)),       // light 0 direction g
            (0x103Cu, F(1f)),       // light 0 direction b
            (0x0580u, F(1f)),       // inverse model-view (the normal's matrix)
            (0x0594u, F(1f)),
            (0x05A8u, F(1f)),
            (0x05BCu, F(1f)),
            (0x0680u, F(4f)),
            (0x0694u, F(4f)),
            (0x06A8u, F(1f)),
            (0x06BCu, F(1f)),
            (0x0A20u, F(2f)),
            (0x0A24u, F(2f)),
            (0x0A28u, F(0f)),
            (0x0A2Cu, F(0f)),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.Equal(1uL, nv2A.FixedFunctionLitDraws);
        Assert.Equal(1uL, nv2A.FixedFunctionLightedDraws);
        Assert.Equal(0xFFFFFF00u, ReadPixel(memory, 5, 5));
    }

    private sealed class ArrayMemory(int size = 0x20000) : IGpuMemory
    {
        private readonly byte[] _data = new byte[size];

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

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x2504u, 4, 1u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x3200u, 4, 1u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x3204u, 4, 0x100u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x3220u, 4, 1u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x3250u, 4, 1u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x400720u, 4, 1u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    private static void PushMethods(
        Nv2AMmio nv2A,
        ArrayMemory memory,
        params (uint Method, uint Parameter)[] methods)
    {
        uint address = 0;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Nv2AMmio.BaseAddress + 0x800040u, 4, address + 4);
    }

    /// <summary>Writes an F32x4 attribute stream: three vertices at 0x2000.</summary>
    private static void WriteAttribute(ArrayMemory memory, uint offset, float[][] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            uint at = offset + ((uint)i * 16u);
            for (int component = 0; component < 4; component++)
            {
                memory.WritePhysical(at + ((uint)component * 4u), F(values[i][component]));
            }
        }
    }

    private static uint ReadPixel(ArrayMemory memory, uint x, uint y)
    {
        return memory.ReadPhysical(0x3000u + (y * 0x40u) + (x * 4u));
    }
}
