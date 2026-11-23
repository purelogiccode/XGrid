// SPDX-License-Identifier: LGPL-2.0-or-later
// Ported from xemu 0.8.137 hw/xbox/nv2a/pgraph/pgraph.c and
// hw/xbox/nv2a/pgraph/glsl/vsh.c (the 0.8.139 files have no semantic delta;
// see docs/research-notes/xemu-0.8.139.md).
// Original copyright (c) 2012 espes; (c) 2015 Jannik Vogel; (c) 2018-2025 Matt Borgerson.

using System;
using System.IO;

namespace XGrid.Nv2a;

/// <summary>
/// Latched NV2A fixed-function ("cheops FF") vertex state: the companion
/// registers of the transform execution mode (lighting/material/specular/
/// texgen/skinning), decoded from the <c>NV097_SET_*</c> methods exactly as the
/// reference does (xemu 0.8.137 <c>pgraph.c</c> + <c>glsl/vsh.c</c>).
/// </summary>
/// <remarks>
/// The matrices, texgen planes, texture matrices, eye position, fog plane and
/// viewport offset/scale are not duplicated here: they live in the shared XF
/// constant context that <see cref="Nv2AMmio"/> already latches from the
/// aliased matrix methods and the transform-constant window, using the
/// reference layout (<c>NV_IGRAPH_XF_XFCTX_*</c>).
/// </remarks>
public sealed class FixedFunctionVertexState
{
    /// <summary>Number of fixed-function texture stages.</summary>
    public const int TextureSlots = 4;

    /// <summary>Texgen channels per stage (S, T, R, Q).</summary>
    public const int TexGenChannels = 4;

    /// <summary>Number of fixed-function light sources.</summary>
    public const int LightCount = 8;

    /// <summary>Byte distance between two light blocks in the <c>SET_LIGHT_*</c> window.</summary>
    private const uint LightStride = 0x80u;

    /// <summary>First method of the <c>SET_LIGHT_AMBIENT_COLOR</c> window (8 lights x 32 words).</summary>
    private const uint LightConstantBase = 0x1000u;

    /// <summary>First method of the <c>SET_SPECULAR_PARAMS</c> range (6 words).</summary>
    private const uint SpecularParamsBase = 0x09E0u;

    private readonly int[] _texGen = new int[TextureSlots * TexGenChannels];
    private readonly bool[] _textureMatrixEnable = new bool[TextureSlots];

    // Per-light registers (xemu pgraph.c NV097_SET_LIGHT_* handlers): the
    // ambient/diffuse/specular colors live in ltctxb, the spot falloff and
    // direction in ltctxa, the local range in ltc1; the infinite half vector,
    // infinite direction, local position and local attenuation are plain
    // per-light arrays in the reference.
    private readonly float[] _lightAmbient = new float[LightCount * 3];
    private readonly float[] _lightDiffuse = new float[LightCount * 3];
    private readonly float[] _lightSpecular = new float[LightCount * 3];
    private readonly float[] _lightLocalRange = new float[LightCount];
    private readonly float[] _lightInfiniteHalfVector = new float[LightCount * 3];
    private readonly float[] _lightInfiniteDirection = new float[LightCount * 3];
    private readonly float[] _lightSpotFalloff = new float[LightCount * 3];
    private readonly float[] _lightSpotDirection = new float[LightCount * 4];
    private readonly float[] _lightLocalPosition = new float[LightCount * 3];
    private readonly float[] _lightLocalAttenuation = new float[LightCount * 3];
    private readonly float[] _specularParams = new float[6];

    /// <summary>True when <c>SET_LIGHTING_ENABLE</c> selects the lighting path.</summary>
    public bool Lighting { get; set; }

    /// <summary>True when <c>SET_SPECULAR_ENABLE</c> is set.</summary>
    public bool SpecularEnable { get; set; }

    /// <summary>True when the reference's separate-specular output is enabled (oD1 carries specular).</summary>
    public bool SeparateSpecular { get; set; }

    /// <summary>True when the eye vector is computed per vertex (enables specular on infinite lights).</summary>
    public bool LocalEye { get; set; }

    /// <summary>
    /// True when oD1 alpha comes from the material specular alpha. The
    /// reference stores the inverse flag (<c>ignore_specular_alpha</c>), so
    /// the default is true.
    /// </summary>
    public bool AlphaFromMaterialSpecular { get; set; } = true;

    /// <summary>True when <c>SET_NORMALIZATION_ENABLE</c> normalizes the transformed normal.</summary>
    public bool Normalization { get; set; }

    /// <summary>Skinning mode (<c>SET_SKIN_MODE</c>, 0 = off, 1..6 = weighted blends).</summary>
    public int Skinning { get; set; }

    /// <summary>Per-light enable mask (<c>SET_LIGHT_ENABLE_MASK</c>, two bits per light).</summary>
    public uint LightEnableMask { get; set; }

    /// <summary>Material emission color source (<c>SET_COLOR_MATERIAL</c> bits 0-1).</summary>
    public int EmissionSource { get; set; }

    /// <summary>Material ambient color source (<c>SET_COLOR_MATERIAL</c> bits 2-3).</summary>
    public int AmbientSource { get; set; }

    /// <summary>Material diffuse color source (<c>SET_COLOR_MATERIAL</c> bits 4-5).</summary>
    public int DiffuseSource { get; set; }

    /// <summary>Material specular color source (<c>SET_COLOR_MATERIAL</c> bits 6-7).</summary>
    public int SpecularSource { get; private set; }

    /// <summary>Scene ambient color (<c>SET_SCENE_AMBIENT_COLOR</c>), the reference's <c>ltctxa[FR_AMB].xyz</c>.</summary>
    public float[] SceneAmbient { get; } = new float[3];

    /// <summary>Material emission color (<c>SET_MATERIAL_EMISSION</c>), the reference's <c>ltctxa[CM_COL].xyz</c>.</summary>
    public float[] MaterialEmission { get; } = new float[3];

    /// <summary>Material alpha (<c>SET_MATERIAL_ALPHA</c>): the oD0 alpha when the diffuse source is MATERIAL.</summary>
    public float MaterialAlpha { get; set; }

    /// <summary>
    /// Reconstructed specular exponent from <c>SET_SPECULAR_PARAMS</c>
    /// (xemu <c>reconstruct_specular_power</c>, recomputed when the sixth word
    /// arrives). Zero until the guest uploads the curve.
    /// </summary>
    public float SpecularPower { get; private set; }

    /// <summary>
    /// The light type of one source, from the two bits
    /// <c>SET_LIGHT_ENABLE_MASK</c> holds for it (0 = off, 1 = infinite,
    /// 2 = local, 3 = spot).
    /// </summary>
    /// <param name="light">Light index (0..7).</param>
    public int GetLightType(int light)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(light);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(light, LightCount);
        return (int)((LightEnableMask >> (light * 2)) & 3u);
    }

    /// <summary>One light's ambient color (<c>SET_LIGHT_AMBIENT_COLOR</c>), three floats.</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightAmbient(int light) =>
        _lightAmbient.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's diffuse color (<c>SET_LIGHT_DIFFUSE_COLOR</c>), three floats.</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightDiffuse(int light) =>
        _lightDiffuse.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's specular color (<c>SET_LIGHT_SPECULAR_COLOR</c>), three floats.</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightSpecular(int light) =>
        _lightSpecular.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's local range (<c>SET_LIGHT_LOCAL_RANGE</c>).</summary>
    /// <param name="light">Light index (0..7).</param>
    public float GetLightLocalRange(int light)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(light);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(light, LightCount);
        return _lightLocalRange[light];
    }

    /// <summary>One light's infinite half vector (<c>SET_LIGHT_INFINITE_HALF_VECTOR</c>).</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightInfiniteHalfVector(int light) =>
        _lightInfiniteHalfVector.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's infinite direction (<c>SET_LIGHT_INFINITE_DIRECTION</c>).</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightInfiniteDirection(int light) =>
        _lightInfiniteDirection.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's spot falloff (<c>SET_LIGHT_SPOT_FALLOFF</c>), the reference's unused <c>ltctxa[L0_K]</c>.</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightSpotFalloff(int light) =>
        _lightSpotFalloff.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's spot direction (<c>SET_LIGHT_SPOT_DIRECTION</c>): xyz + the cone cosine word.</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightSpotDirection(int light) =>
        _lightSpotDirection.AsSpan(CheckedLightBase(light, 4), 4);

    /// <summary>One light's local position (<c>SET_LIGHT_LOCAL_POSITION</c>).</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightLocalPosition(int light) =>
        _lightLocalPosition.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>One light's local attenuation constants (<c>SET_LIGHT_LOCAL_ATTENUATION</c>).</summary>
    /// <param name="light">Light index (0..7).</param>
    public ReadOnlySpan<float> GetLightLocalAttenuation(int light) =>
        _lightLocalAttenuation.AsSpan(CheckedLightBase(light, 3), 3);

    /// <summary>The raw <c>SET_SPECULAR_PARAMS</c> curve words (6 floats).</summary>
    public ReadOnlySpan<float> GetSpecularParams() => _specularParams;

    private static int CheckedLightBase(int light, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(light);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(light, LightCount);
        return light * width;
    }

    /// <summary>
    /// Latches one word of the <c>SET_LIGHT_*</c> window
    /// (<c>SET_LIGHT_AMBIENT_COLOR</c> .. +8*32 words), decoding the light index
    /// and the within-light offset exactly like the reference handler.
    /// </summary>
    /// <param name="method">Method offset.</param>
    /// <param name="parameter">Raw method parameter.</param>
    /// <returns>True when the method belongs to the light window.</returns>
    public bool TrySetLightMethod(uint method, uint parameter)
    {
        if (method < LightConstantBase || method >= LightConstantBase + (LightCount * LightStride))
        {
            return false;
        }

        uint relative = method - LightConstantBase;
        int light = (int)(relative / LightStride);
        uint offset = relative % LightStride;
        float value = BitConverter.UInt32BitsToSingle(parameter);

        // The within-light layout mirrors the reference method window
        // (NV097_SET_LIGHT_* offsets): three-word colors, then the single-word
        // range, the three-word vectors, the four-word spot direction and the
        // 0x74..0x7C padding.
        if (offset <= 0x08)
        {
            _lightAmbient[(light * 3) + (int)(offset / 4u)] = value;
        }
        else if (offset >= 0x0C && offset <= 0x14)
        {
            _lightDiffuse[(light * 3) + (int)((offset - 0x0C) / 4u)] = value;
        }
        else if (offset >= 0x18 && offset <= 0x20)
        {
            _lightSpecular[(light * 3) + (int)((offset - 0x18) / 4u)] = value;
        }
        else if (offset == 0x24)
        {
            _lightLocalRange[light] = value;
        }
        else if (offset >= 0x28 && offset <= 0x30)
        {
            _lightInfiniteHalfVector[(light * 3) + (int)((offset - 0x28) / 4u)] = value;
        }
        else if (offset >= 0x34 && offset <= 0x3C)
        {
            _lightInfiniteDirection[(light * 3) + (int)((offset - 0x34) / 4u)] = value;
        }
        else if (offset >= 0x40 && offset <= 0x48)
        {
            _lightSpotFalloff[(light * 3) + (int)((offset - 0x40) / 4u)] = value;
        }
        else if (offset >= 0x4C && offset <= 0x58)
        {
            _lightSpotDirection[(light * 4) + (int)((offset - 0x4C) / 4u)] = value;
        }
        else if (offset >= 0x5C && offset <= 0x64)
        {
            _lightLocalPosition[(light * 3) + (int)((offset - 0x5C) / 4u)] = value;
        }
        else if (offset >= 0x68 && offset <= 0x70)
        {
            _lightLocalAttenuation[(light * 3) + (int)((offset - 0x68) / 4u)] = value;
        }

        // 0x74..0x7C are padding/no light property: ignored like the
        // reference's default case (which asserts in debug builds).
        return true;
    }

    /// <summary>
    /// Latches one word of <c>SET_SPECULAR_PARAMS</c> and recomputes
    /// <see cref="SpecularPower"/> when the sixth word arrives (reference
    /// <c>SET_SPECULAR_PARAMS</c> handler).
    /// </summary>
    /// <param name="method">Method offset.</param>
    /// <param name="parameter">Raw method parameter.</param>
    /// <returns>True when the method belongs to the parameter range.</returns>
    public bool TrySetSpecularParams(uint method, uint parameter)
    {
        if (method < SpecularParamsBase || method >= SpecularParamsBase + 24u)
        {
            return false;
        }

        int slot = (int)((method - SpecularParamsBase) / 4u);
        _specularParams[slot] = BitConverter.UInt32BitsToSingle(parameter);
        if (slot == 5)
        {
            SpecularPower = SpecularPowerCurve.Reconstruct(_specularParams);
        }

        return true;
    }

    /// <summary>
    /// Number of model-view matrices the skinning mode blends (0 for
    /// <c>SET_SKIN_MODE_OFF</c>).
    /// </summary>
    public int SkinningMatrixCount => Skinning switch
    {
        1 or 2 => 2,
        3 or 4 => 3,
        5 or 6 => 4,
        _ => 0,
    };

    /// <summary>
    /// True for the modes whose last weight is derived (GL_WEIGHT_SUM_UNITY):
    /// 1WEIGHTS (1), 2WEIGHTS (3) and 3WEIGHTS (5).
    /// </summary>
    public bool SkinningSumsWeights => Skinning is 1 or 3 or 5;

    /// <summary>Returns the texgen mode of one stage channel (0..3 = S,T,R,Q).</summary>
    /// <param name="stage">Texture stage (0..3).</param>
    /// <param name="channel">Channel: 0 = S, 1 = T, 2 = R, 3 = Q.</param>
    public int GetTexGen(int stage, int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stage, TextureSlots);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, TexGenChannels);
        return _texGen[(stage * TexGenChannels) + channel];
    }

    /// <summary>Latches one texgen mode (<c>SET_TEXGEN_S/T/R/Q</c>).</summary>
    /// <param name="stage">Texture stage (0..3).</param>
    /// <param name="channel">Channel: 0 = S, 1 = T, 2 = R, 3 = Q.</param>
    /// <param name="mode">Raw <c>NV097_SET_TEXGEN_*</c> parameter.</param>
    public void SetTexGen(int stage, int channel, uint mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stage, TextureSlots);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, TexGenChannels);
        _texGen[(stage * TexGenChannels) + channel] = (int)mode;
    }

    /// <summary>True when the stage applies its texture matrix (<c>SET_TEXTURE_MATRIX_ENABLE</c>).</summary>
    /// <param name="stage">Texture stage (0..3).</param>
    public bool GetTextureMatrixEnable(int stage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stage, TextureSlots);
        return _textureMatrixEnable[stage];
    }

    /// <summary>Latches one <c>SET_TEXTURE_MATRIX_ENABLE</c> value.</summary>
    /// <param name="stage">Texture stage (0..3).</param>
    /// <param name="enable">Raw method parameter (0 disables).</param>
    public void SetTextureMatrixEnable(int stage, uint enable)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stage, TextureSlots);
        _textureMatrixEnable[stage] = enable != 0;
    }

    /// <summary>Decodes <c>SET_LIGHT_CONTROL</c>.</summary>
    /// <param name="parameter">Raw method parameter.</param>
    public void SetLightControl(uint parameter)
    {
        SeparateSpecular = (parameter & 1u) != 0;
        LocalEye = (parameter & (1u << 16)) != 0;
        AlphaFromMaterialSpecular = (parameter & (1u << 17)) != 0;
    }

    /// <summary>Decodes <c>SET_COLOR_MATERIAL</c> (four two-bit source selects).</summary>
    /// <param name="parameter">Raw method parameter.</param>
    public void SetColorMaterial(uint parameter)
    {
        EmissionSource = (int)(parameter & 3u);
        AmbientSource = (int)((parameter >> 2) & 3u);
        DiffuseSource = (int)((parameter >> 4) & 3u);
        SpecularSource = (int)((parameter >> 6) & 3u);
    }

    /// <summary>Restores the reset values.</summary>
    // ReSharper disable once UnusedMember.Global
    public void Reset()
    {
        Lighting = false;
        SpecularEnable = false;
        SeparateSpecular = false;
        LocalEye = false;
        AlphaFromMaterialSpecular = true;
        Normalization = false;
        Skinning = 0;
        LightEnableMask = 0;
        EmissionSource = 0;
        AmbientSource = 0;
        DiffuseSource = 0;
        SpecularSource = 0;
        Array.Clear(SceneAmbient);
        Array.Clear(MaterialEmission);
        MaterialAlpha = 0f;
        Array.Clear(_texGen);
        Array.Clear(_textureMatrixEnable);
        Array.Clear(_lightAmbient);
        Array.Clear(_lightDiffuse);
        Array.Clear(_lightSpecular);
        Array.Clear(_lightLocalRange);
        Array.Clear(_lightInfiniteHalfVector);
        Array.Clear(_lightInfiniteDirection);
        Array.Clear(_lightSpotFalloff);
        Array.Clear(_lightSpotDirection);
        Array.Clear(_lightLocalPosition);
        Array.Clear(_lightLocalAttenuation);
        Array.Clear(_specularParams);
        SpecularPower = 0f;
    }

    /// <summary>Writes the state to a snapshot stream.</summary>
    /// <param name="writer">Destination stream.</param>
    // ReSharper disable once UnusedMember.Global
    public void Write(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Lighting);
        writer.Write(SpecularEnable);
        writer.Write(SeparateSpecular);
        writer.Write(LocalEye);
        writer.Write(AlphaFromMaterialSpecular);
        writer.Write(Normalization);
        writer.Write(Skinning);
        writer.Write(LightEnableMask);
        writer.Write(EmissionSource);
        writer.Write(AmbientSource);
        writer.Write(DiffuseSource);
        writer.Write(SpecularSource);
        for (int i = 0; i < 3; i++)
        {
            writer.Write(SceneAmbient[i]);
        }

        for (int i = 0; i < 3; i++)
        {
            writer.Write(MaterialEmission[i]);
        }

        writer.Write(MaterialAlpha);
        for (int i = 0; i < _texGen.Length; i++)
        {
            writer.Write(_texGen[i]);
        }

        for (int i = 0; i < _textureMatrixEnable.Length; i++)
        {
            writer.Write(_textureMatrixEnable[i]);
        }

        WriteFloats(writer, _lightAmbient);
        WriteFloats(writer, _lightDiffuse);
        WriteFloats(writer, _lightSpecular);
        WriteFloats(writer, _lightLocalRange);
        WriteFloats(writer, _lightInfiniteHalfVector);
        WriteFloats(writer, _lightInfiniteDirection);
        WriteFloats(writer, _lightSpotFalloff);
        WriteFloats(writer, _lightSpotDirection);
        WriteFloats(writer, _lightLocalPosition);
        WriteFloats(writer, _lightLocalAttenuation);
        WriteFloats(writer, _specularParams);
        writer.Write(SpecularPower);
    }

    /// <summary>Reads the state from a snapshot stream.</summary>
    /// <param name="reader">Source stream.</param>
    // ReSharper disable once UnusedMember.Global
    public void Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Lighting = reader.ReadBoolean();
        SpecularEnable = reader.ReadBoolean();
        SeparateSpecular = reader.ReadBoolean();
        LocalEye = reader.ReadBoolean();
        AlphaFromMaterialSpecular = reader.ReadBoolean();
        Normalization = reader.ReadBoolean();
        Skinning = reader.ReadInt32();
        LightEnableMask = reader.ReadUInt32();
        EmissionSource = reader.ReadInt32();
        AmbientSource = reader.ReadInt32();
        DiffuseSource = reader.ReadInt32();
        SpecularSource = reader.ReadInt32();
        for (int i = 0; i < 3; i++)
        {
            SceneAmbient[i] = reader.ReadSingle();
        }

        for (int i = 0; i < 3; i++)
        {
            MaterialEmission[i] = reader.ReadSingle();
        }

        MaterialAlpha = reader.ReadSingle();
        for (int i = 0; i < _texGen.Length; i++)
        {
            _texGen[i] = reader.ReadInt32();
        }

        for (int i = 0; i < _textureMatrixEnable.Length; i++)
        {
            _textureMatrixEnable[i] = reader.ReadBoolean();
        }

        ReadFloats(reader, _lightAmbient);
        ReadFloats(reader, _lightDiffuse);
        ReadFloats(reader, _lightSpecular);
        ReadFloats(reader, _lightLocalRange);
        ReadFloats(reader, _lightInfiniteHalfVector);
        ReadFloats(reader, _lightInfiniteDirection);
        ReadFloats(reader, _lightSpotFalloff);
        ReadFloats(reader, _lightSpotDirection);
        ReadFloats(reader, _lightLocalPosition);
        ReadFloats(reader, _lightLocalAttenuation);
        ReadFloats(reader, _specularParams);
        SpecularPower = reader.ReadSingle();
    }

    private static void WriteFloats(BinaryWriter writer, float[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void ReadFloats(BinaryReader reader, float[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadSingle();
        }
    }
}
