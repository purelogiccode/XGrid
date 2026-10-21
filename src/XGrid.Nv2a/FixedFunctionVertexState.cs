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

    private readonly int[] _texGen = new int[TextureSlots * TexGenChannels];
    private readonly bool[] _textureMatrixEnable = new bool[TextureSlots];

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
    public int SpecularSource { get; set; }

    /// <summary>Scene ambient color (<c>SET_SCENE_AMBIENT_COLOR</c>), the reference's <c>ltctxa[FR_AMB].xyz</c>.</summary>
    public float[] SceneAmbient { get; } = new float[3];

    /// <summary>Material emission color (<c>SET_MATERIAL_EMISSION</c>), the reference's <c>ltctxa[CM_COL].xyz</c>.</summary>
    public float[] MaterialEmission { get; } = new float[3];

    /// <summary>Material alpha (<c>SET_MATERIAL_ALPHA</c>): the oD0 alpha when the diffuse source is MATERIAL.</summary>
    public float MaterialAlpha { get; set; }

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
    }

    /// <summary>Writes the state to a snapshot stream.</summary>
    /// <param name="writer">Destination stream.</param>
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
    }

    /// <summary>Reads the state from a snapshot stream.</summary>
    /// <param name="reader">Source stream.</param>
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
    }
}
