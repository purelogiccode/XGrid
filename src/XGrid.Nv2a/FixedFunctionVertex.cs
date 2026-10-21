using System;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A fixed-function ("cheops FF") vertex stage: skinning, texture
/// coordinate generation, texture matrix, color/specular selection and the
/// screen-space position epilogue, transcribed from the reference GLSL
/// generator (xemu 0.8.137 <c>hw/xbox/nv2a/pgraph/glsl/vsh-ff.c</c>).
/// </summary>
/// <remarks>
/// <para>
/// Runs instead of a vertex program when <c>SET_TRANSFORM_EXECUTION_MODE</c>
/// selects FIXED (0): the guest then uploads its matrices into the XF constant
/// context (<c>NV097_SET_COMPOSITE_MATRIX</c>, <c>SET_MODEL_VIEW_MATRIX</c>,
/// <c>SET_INVERSE_MODEL_VIEW_MATRIX</c>, <c>SET_PROJECTION_MATRIX</c>,
/// <c>SET_TEXTURE_MATRIX</c>, <c>SET_TEXGEN_PLANE_*</c>,
/// <c>SET_VIEWPORT_OFFSET</c>) instead of loading a program.
/// </para>
/// <para>
/// Matrix convention: constants are rows of a row-vector matrix, matching the
/// reference's <c>v * mat4(c[0], c[1], c[2], c[3])</c>, so
/// <c>result[j] = dot(v, c[j])</c>.
/// </para>
/// <para>
/// Lighting (<c>SET_LIGHTING_ENABLE</c>): the material chain is implemented
/// (ambient source + material emission product + emission source + the
/// <c>oD1</c> alpha rule), which is exactly what the reference reduces to when
/// no light is enabled. The enabled-light loop (infinite/local/spot,
/// attenuation, spot cone, specular power) is not: a lit draw with any enabled
/// light keeps the chain without the light terms and is counted in
/// <see cref="Nv2AMmio.FixedFunctionLightedDraws"/> so the gap stays measured.
/// </para>
/// </remarks>
public static class FixedFunctionVertex
{
    /// <summary>Fixed-function texture stages.</summary>
    public const int TextureSlots = FixedFunctionVertexState.TextureSlots;

    /// <summary>Texgen channels per stage (S, T, R, Q).</summary>
    public const int TexGenChannels = FixedFunctionVertexState.TexGenChannels;

    /// <summary><c>NV097_SET_TEXGEN_*_DISABLE</c>: pass the input attribute through.</summary>
    public const uint TexGenDisable = 0x0000u;

    /// <summary><c>NV097_SET_TEXGEN_*_EYE_LINEAR</c>.</summary>
    public const uint TexGenEyeLinear = 0x2400u;

    /// <summary><c>NV097_SET_TEXGEN_*_OBJECT_LINEAR</c>.</summary>
    public const uint TexGenObjectLinear = 0x2401u;

    /// <summary><c>NV097_SET_TEXGEN_*_SPHERE_MAP</c>.</summary>
    public const uint TexGenSphereMap = 0x2402u;

    /// <summary><c>NV097_SET_TEXGEN_*_REFLECTION_MAP</c>.</summary>
    public const uint TexGenReflectionMap = 0x8512u;

    /// <summary><c>NV097_SET_TEXGEN_*_NORMAL_MAP</c>.</summary>
    public const uint TexGenNormalMap = 0x8511u;

    // SET_COLOR_MATERIAL source selectors (xemu vsh_regs.h MaterialColorSource).
    /// <summary>Material color source: the material color registers.</summary>
    public const int MaterialColorSourceMaterial = 0;

    /// <summary>Material color source: the diffuse vertex attribute.</summary>
    public const int MaterialColorSourceDiffuse = 1;

    /// <summary>Material color source: the specular vertex attribute.</summary>
    public const int MaterialColorSourceSpecular = 2;

    // XF constant context layout (xemu nv2a_regs.h NV_IGRAPH_XF_XFCTX_*).
    private const int CompositeMatrix = 0x00;
    private const int ModelViewMatrix = 0x08;
    private const int InverseModelViewMatrix = 0x0C;
    private const int FogPlane = 0x39;
    private const int ViewportOffset = 0x3B;
    private const int TexGenPlane = 0x40;
    private const int TextureMatrix = 0x44;

    // clampAwayZeroInf bounds (xemu glsl/vsh.c): 2^-64 .. 2^64.
    private static readonly float ClampLowPositive = BitConverter.Int32BitsToSingle(0x1F800000);
    private static readonly float ClampHighPositive = BitConverter.Int32BitsToSingle(0x5F800000);

    /// <summary>
    /// Runs the fixed-function vertex stage for one vertex.
    /// </summary>
    /// <param name="state">Latched fixed-function vertex state.</param>
    /// <param name="constants">XF constant context (192 entries of four floats).</param>
    /// <param name="inputs">Input registers v0..v15 (four floats each).</param>
    /// <param name="outputs">Output registers o0..o15, laid out as the programmable epilogue leaves them: o0 = screen-space position, o3 = oD0, o4 = oD1, o5.x = oFog distance, o7 = oB0, o8 = oB1, o9..o12 = oT0..oT3.</param>
    /// <param name="fogEnable">True when <c>SET_FOG_ENABLE</c> is set.</param>
    /// <param name="fogGenMode">Raw <c>SET_FOG_GEN_MODE</c> value.</param>
    public static void Execute(
        FixedFunctionVertexState state,
        float[][] constants,
        float[][] inputs,
        float[][] outputs,
        bool fogEnable,
        int fogGenMode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        ReadOnlySpan<float> position = inputs[0];
        ReadOnlySpan<float> weight = inputs[1];
        ReadOnlySpan<float> normal = inputs[2];

        Span<float> normalSource = stackalloc float[4];
        normalSource[0] = normal[0];
        normalSource[1] = normal[1];
        normalSource[2] = normal[2];
        normalSource[3] = 0f;

        // tPosition (eye space) and tNormal (normal through the inverse
        // model-view matrix), skinned when SET_SKIN_MODE blends matrices.
        Span<float> eye = stackalloc float[4];
        Span<float> eyeNormal = stackalloc float[3];
        Span<float> transformed = stackalloc float[4];
        int matrices = state.SkinningMatrixCount;
        if (matrices == 0)
        {
            Multiply(position, constants, ModelViewMatrix, eye);
            Multiply(normalSource, constants, InverseModelViewMatrix, transformed);
            eyeNormal[0] = transformed[0];
            eyeNormal[1] = transformed[1];
            eyeNormal[2] = transformed[2];
        }
        else
        {
            Skin(state, weight, position, matrices, constants, ModelViewMatrix, eye);
            Skin(state, weight, normalSource, matrices, constants, InverseModelViewMatrix, transformed);
            eyeNormal[0] = transformed[0];
            eyeNormal[1] = transformed[1];
            eyeNormal[2] = transformed[2];
        }

        if (state.Normalization)
        {
            Normalize(eyeNormal);
        }

        TexGen(state, constants, inputs, outputs, position, eye, eyeNormal);

        Span<float> texel = stackalloc float[4];
        for (int stage = 0; stage < TextureSlots; stage++)
        {
            if (!state.GetTextureMatrixEnable(stage))
            {
                continue;
            }

            Multiply(outputs[9 + stage], constants, TextureMatrix + (4 * stage), texel);
            texel.CopyTo(outputs[9 + stage]);
        }

        // Colors. Unlit: oD0/oD1 are the attributes. Lit: the reference's
        // material chain (xemu vsh-ff.c) - the ambient source selects the base
        // color, oD0.rgb is multiplied by the material emission color and then
        // the emission source is added, and oD1 starts as (0,0,0,specular.a).
        // The enabled-light loop is not implemented (see the class remarks):
        // with every light off the reference reduces to exactly this chain, and
        // a draw with an enabled light is counted so the gap stays measurable.
        if (!state.Lighting)
        {
            for (int i = 0; i < 4; i++)
            {
                outputs[3][i] = inputs[3][i];
                outputs[4][i] = inputs[4][i];
                outputs[7][i] = inputs[7][i];
                outputs[8][i] = inputs[8][i];
            }
        }
        else
        {
            float alpha = state.DiffuseSource switch
            {
                MaterialColorSourceSpecular => inputs[4][3],
                MaterialColorSourceDiffuse => inputs[3][3],
                _ => state.MaterialAlpha,
            };

            ReadOnlySpan<float> ambient = state.AmbientSource switch
            {
                MaterialColorSourceDiffuse => inputs[3],
                MaterialColorSourceSpecular => inputs[4],
                _ => state.SceneAmbient,
            };

            ReadOnlySpan<float> emission = state.EmissionSource switch
            {
                MaterialColorSourceDiffuse => inputs[3],
                MaterialColorSourceSpecular => inputs[4],
                _ => state.SceneAmbient,
            };

            for (int i = 0; i < 3; i++)
            {
                outputs[3][i] = (ambient[i] * state.MaterialEmission[i]) + emission[i];
            }

            outputs[3][3] = alpha;
            Set(outputs[4], 0f, 0f, 0f, inputs[4][3]);
            for (int i = 0; i < 4; i++)
            {
                outputs[7][i] = inputs[7][i];
                outputs[8][i] = inputs[8][i];
            }
        }

        if (!state.SpecularEnable)
        {
            Set(outputs[4], 0f, 0f, 0f, 1f);
            Set(outputs[8], 0f, 0f, 0f, 1f);
        }
        else
        {
            if (!state.SeparateSpecular)
            {
                for (int i = 0; i < 3; i++)
                {
                    outputs[3][i] += outputs[4][i];
                    outputs[7][i] += outputs[8][i];
                }

                for (int i = 0; i < 4; i++)
                {
                    outputs[4][i] = inputs[4][i];
                    outputs[8][i] = inputs[8][i];
                }
            }

            if (!state.AlphaFromMaterialSpecular)
            {
                outputs[4][3] = 1f;
                outputs[8][3] = 1f;
            }
        }

        // Fog distance from the eye-space position (the reference generates
        // the same expressions from tPosition).
        outputs[5][0] = fogEnable
            ? VshProgram.FogGenDistance(
                fogGenMode,
                inputs[5][0],
                eye[0],
                eye[1],
                eye[2],
                inputs[4][3],
                constants[FogPlane])
            : 0f;

        // Position epilogue: with skinning off the composite matrix already
        // contains the model-view transform, so the raw object-space position
        // goes through it (xemu vsh-ff.c: "If skinning is off the composite
        // matrix already includes the MV matrix").
        Multiply(matrices == 0 ? position : eye, constants, CompositeMatrix, texel);
        float w = ClampAwayZeroInf(texel[3]);
        float x = (texel[0] / w) + constants[ViewportOffset][0];
        float y = (texel[1] / w) + constants[ViewportOffset][1];
        outputs[0][0] = (float)Math.Truncate(x * 16f) / 16f;
        outputs[0][1] = (float)Math.Truncate(y * 16f) / 16f;
        outputs[0][2] = texel[2] / w;
        outputs[0][3] = w;
    }

    private static void TexGen(
        FixedFunctionVertexState state,
        float[][] constants,
        float[][] inputs,
        float[][] outputs,
        ReadOnlySpan<float> position,
        ReadOnlySpan<float> eye,
        ReadOnlySpan<float> normal)
    {
        Span<float> reflected = stackalloc float[3];
        Span<float> sphere = stackalloc float[2];
        for (int stage = 0; stage < TextureSlots; stage++)
        {
            Span<float> texel = outputs[9 + stage];
            ReadOnlySpan<float> attribute = inputs[9 + stage];
            int planeBase = TexGenPlane + (4 * stage);
            bool sphereComputed = false;
            bool reflectionComputed = false;
            for (int channel = 0; channel < TexGenChannels; channel++)
            {
                switch ((uint)state.GetTexGen(stage, channel))
                {
                    case TexGenEyeLinear:
                        texel[channel] = Dot(constants[planeBase + channel], eye);
                        break;
                    case TexGenObjectLinear:
                        texel[channel] = Dot(constants[planeBase + channel], position);
                        break;
                    case TexGenSphereMap:
                        if (channel >= 2)
                        {
                            // Hardware asserts on S/T only: keep the input.
                            texel[channel] = attribute[channel];
                            break;
                        }

                        if (!sphereComputed)
                        {
                            ComputeSphereMap(eye, normal, sphere);
                            sphereComputed = true;
                        }

                        texel[channel] = sphere[channel];
                        break;
                    case TexGenReflectionMap:
                        if (channel >= 3)
                        {
                            texel[channel] = attribute[channel];
                            break;
                        }

                        if (!reflectionComputed)
                        {
                            ComputeReflection(eye, normal, reflected);
                            reflectionComputed = true;
                        }

                        texel[channel] = reflected[channel];
                        break;
                    case TexGenNormalMap:
                        texel[channel] = channel < 3 ? normal[channel] : attribute[channel];
                        break;
                    default:
                        texel[channel] = attribute[channel];
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Sphere-map S/T for the eye-space position and normal (xemu vsh-ff.c):
    /// <c>u = normalize(tPosition.xyz)</c>, <c>r = reflect(u, tNormal)</c>,
    /// <c>oT = r.xy / (2 * length(r + (0,0,1))) + 0.5</c>.
    /// </summary>
    private static void ComputeSphereMap(ReadOnlySpan<float> eye, ReadOnlySpan<float> normal, Span<float> result)
    {
        Span<float> reflected = stackalloc float[3];
        ComputeReflection(eye, normal, reflected);
        float dz = reflected[2] + 1f;
        float length = MathF.Sqrt(
            (reflected[0] * reflected[0]) + (reflected[1] * reflected[1]) + (dz * dz));
        float invM = 1f / (2f * length);
        result[0] = (reflected[0] * invM) + 0.5f;
        result[1] = (reflected[1] * invM) + 0.5f;
    }

    private static void ComputeReflection(ReadOnlySpan<float> eye, ReadOnlySpan<float> normal, Span<float> result)
    {
        float length = MathF.Sqrt((eye[0] * eye[0]) + (eye[1] * eye[1]) + (eye[2] * eye[2]));
        Span<float> incident = stackalloc float[3];
        incident[0] = eye[0] / length;
        incident[1] = eye[1] / length;
        incident[2] = eye[2] / length;
        float dot = (normal[0] * incident[0]) + (normal[1] * incident[1]) + (normal[2] * incident[2]);
        result[0] = incident[0] - (2f * dot * normal[0]);
        result[1] = incident[1] - (2f * dot * normal[1]);
        result[2] = incident[2] - (2f * dot * normal[2]);
    }

    private static void Normalize(Span<float> value)
    {
        float length = MathF.Sqrt(
            (value[0] * value[0]) + (value[1] * value[1]) + (value[2] * value[2]));
        value[0] /= length;
        value[1] /= length;
        value[2] /= length;
    }

    private static void Skin(
        FixedFunctionVertexState state,
        ReadOnlySpan<float> weight,
        ReadOnlySpan<float> source,
        int matrices,
        float[][] constants,
        int baseIndex,
        Span<float> result)
    {
        Span<float> transformed = stackalloc float[4];
        result.Clear();
        float remaining = 1f;
        for (int i = 0; i < matrices; i++)
        {
            float blend;
            if (i == matrices - 1)
            {
                blend = state.SkinningSumsWeights ? remaining : weight[i];
            }
            else
            {
                blend = weight[i];
                remaining -= blend;
            }

            if (blend == 0f)
            {
                continue;
            }

            Multiply(source, constants, baseIndex + (4 * i), transformed);
            for (int j = 0; j < 4; j++)
            {
                result[j] += transformed[j] * blend;
            }
        }
    }

    /// <summary>Row-vector matrix product: <c>result[j] = dot(source, constants[base + j])</c>.</summary>
    private static void Multiply(
        ReadOnlySpan<float> source,
        float[][] constants,
        int baseIndex,
        Span<float> result)
    {
        for (int j = 0; j < 4; j++)
        {
            float[] row = constants[baseIndex + j];
            result[j] = (source[0] * row[0]) + (source[1] * row[1]) + (source[2] * row[2]) + (source[3] * row[3]);
        }
    }

    private static float Dot(float[] row, ReadOnlySpan<float> value) =>
        (row[0] * value[0]) + (row[1] * value[1]) + (row[2] * value[2]) + (row[3] * value[3]);

    private static void Set(float[] destination, float x, float y, float z, float w)
    {
        destination[0] = x;
        destination[1] = y;
        destination[2] = z;
        destination[3] = w;
    }

    /// <summary>
    /// Clamps away from zero and infinity exactly like the reference
    /// <c>clampAwayZeroInf</c>: positive values (and +0) stay in
    /// [2^-64, 2^64], negative values in [-2^64, -2^-64].
    /// </summary>
    private static float ClampAwayZeroInf(float value)
    {
        if (value > 0f || BitConverter.SingleToUInt32Bits(value) == 0u)
        {
            return Math.Clamp(value, ClampLowPositive, ClampHighPositive);
        }

        return Math.Clamp(value, -ClampHighPositive, -ClampLowPositive);
    }
}
