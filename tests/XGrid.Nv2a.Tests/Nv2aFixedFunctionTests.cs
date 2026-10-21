using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the fixed-function (transform execution mode FIXED) vertex stage: the
/// transform epilogue, texgen, skinning, texture matrix, color selection and the
/// eye-space fog distance, plus the opt-in device path that runs it.
/// </summary>
/// <remarks>
/// The reference is xemu 0.8.137 <c>hw/xbox/nv2a/pgraph/glsl/vsh-ff.c</c>:
/// <c>oPos = tPosition * compositeMat</c> (the composite already contains the
/// model-view transform when skinning is off), <c>xy /= w</c>, <c>+= VPOFF</c>,
/// truncate to 1/16, <c>z</c> in depth-buffer units; identity texgen passes the
/// attribute through, eye/object linear dot the texgen plane, sphere map uses
/// <c>reflect(normalize(tPosition), tNormal)</c>; and only eye-space values feed
/// lighting, texgen and the fog distance.
/// </remarks>
public sealed class Nv2aFixedFunctionTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private const int CompositeMatrix = 0x00;
    private const int ModelViewMatrix = 0x08;
    private const int InverseModelViewMatrix = 0x0C;
    private const int FogPlane = 0x39;
    private const int ViewportOffset = 0x3B;
    private const int TexGenPlane = 0x40;
    private const int TextureMatrix = 0x44;

    private const int Position = 0;
    private const int Weight = 1;
    private const int Normal = 2;
    private const int Diffuse = 3;
    private const int Specular = 4;
    private const int FogCoord = 5;
    private const int BackDiffuse = 7;
    private const int BackSpecular = 8;
    private const int TexCoord0 = 9;

    private const uint SkinModeOff = 0u;
    private const uint SkinModeWeights1 = 1u;
    private const uint SkinModeWeights2Matrices2 = 2u;

    private const uint FogGenRadial = 1u;

    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data;

        public ArrayMemory(int size = 0x20000)
        {
            _data = new byte[size];
        }

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
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Base + 0x3250u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory, params (uint Method, uint Parameter)[] methods)
    {
        uint address = 0;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Base + 0x800040u, 4, address + 4);
    }

    private static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    /// <summary>Fresh constant context: every matrix is the identity, everything else zero.</summary>
    private static float[][] Constants()
    {
        var constants = new float[VshProgram.ConstantCount][];
        for (int i = 0; i < constants.Length; i++)
        {
            constants[i] = new float[4];
        }

        SetIdentity(constants, CompositeMatrix);
        SetIdentity(constants, ModelViewMatrix);
        SetIdentity(constants, InverseModelViewMatrix);
        SetIdentity(constants, TextureMatrix);
        SetIdentity(constants, TextureMatrix + 4);
        SetIdentity(constants, TextureMatrix + 8);
        SetIdentity(constants, TextureMatrix + 12);
        return constants;
    }

    private static void SetIdentity(float[][] constants, int baseIndex)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                constants[baseIndex + row][column] = row == column ? 1f : 0f;
            }
        }
    }

    private static float[][] Inputs()
    {
        var inputs = new float[VshProgram.RegisterCount][];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new float[4];
        }

        // Attribute defaults: opaque white diffuse and specular like the
        // device's inline values, unit texcoords, unit position.
        for (int slot = 0; slot < inputs.Length; slot++)
        {
            inputs[slot][3] = 1f;
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

    private static (float[][] Constants, float[][] Inputs, float[][] Outputs) Run(
        FixedFunctionVertexState state,
        bool fogEnable = false,
        uint fogGenMode = FogGenRadial)
    {
        float[][] constants = Constants();
        float[][] inputs = Inputs();
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable, (int)fogGenMode);
        return (constants, inputs, outputs);
    }

    [Fact]
    public void Execute_ScreenPositionComesFromTheCompositeMatrixAndViewportOffset()
    {
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        constants[CompositeMatrix][0] = 4f;
        constants[CompositeMatrix + 1][1] = 8f;
        constants[ViewportOffset][0] = 10f;
        constants[ViewportOffset][1] = 20f;

        // A non-identity model-view matrix must not reach the position when
        // skinning is off: the composite matrix already contains it.
        constants[ModelViewMatrix][0] = 99f;

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Position][1] = 3f;
        inputs[Position][2] = 0.5f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(18f, outputs[0][0]);
        Assert.Equal(44f, outputs[0][1]);
        Assert.Equal(0.5f, outputs[0][2]);
        Assert.Equal(1f, outputs[0][3]);
    }

    [Fact]
    public void Execute_TruncatesScreenCoordinatesToSixteenths()
    {
        // xemu roundScreenCoords: trunc(pos * 16) / 16 (hardware has four
        // fractional bits and truncates).
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        constants[CompositeMatrix][0] = 1f;
        constants[CompositeMatrix + 1][1] = 1f;
        constants[ViewportOffset][0] = 2.3f;
        constants[ViewportOffset][1] = -4.9f;

        float[][] inputs = Inputs();
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        // trunc(2.3 * 16) = 36 -> 2.25; trunc(-4.9 * 16) = -78 -> -4.875.
        Assert.Equal(2.25f, outputs[0][0]);
        Assert.Equal(-4.875f, outputs[0][1]);
    }

    [Fact]
    public void Execute_ZeroWIsClampedAwayFromZeroInsteadOfDividingByZero()
    {
        // clampAwayZeroInf(0) == 2^-64, so a zero w cannot produce NaN/inf.
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        constants[CompositeMatrix][0] = 1f;
        constants[CompositeMatrix + 1][1] = 1f;
        constants[CompositeMatrix + 3][3] = 0f;

        float[][] inputs = Inputs();
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(BitConverter.Int32BitsToSingle(0x1F800000), outputs[0][3]);
        Assert.Equal(0f, outputs[0][0]);
    }

    [Fact]
    public void Execute_PerspectiveDivideUsesTheCompositeW()
    {
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        constants[CompositeMatrix][0] = 640f;
        constants[CompositeMatrix + 3][3] = 2f;
        constants[ViewportOffset][0] = 320f;

        float[][] inputs = Inputs();
        inputs[Position][0] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(2f, outputs[0][3]);
        Assert.Equal(640f, outputs[0][0]);
    }

    [Fact]
    public void Execute_DisabledTexGenPassesTheAttributeThrough()
    {
        float[][] constants = Constants();
        float[][] inputs = Inputs();
        inputs[TexCoord0][0] = 0.125f;
        inputs[TexCoord0][1] = 0.25f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(new FixedFunctionVertexState(), constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(0.125f, outputs[9][0]);
        Assert.Equal(0.25f, outputs[9][1]);
    }

    [Fact]
    public void Execute_EyeLinearTexGenDotsThePlaneWithTheEyePosition()
    {
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 0, FixedFunctionVertex.TexGenEyeLinear);
        float[][] constants = Constants();
        constants[ModelViewMatrix][0] = 2f;
        constants[TexGenPlane][0] = 1f;

        // Eye position x = 2 * object x = 6; the plane (1,0,0,0) dots to 6.
        float[][] inputs = Inputs();
        inputs[Position][0] = 3f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(6f, outputs[9][0]);
    }

    [Fact]
    public void Execute_ObjectLinearTexGenDotsThePlaneWithTheObjectPosition()
    {
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 1, FixedFunctionVertex.TexGenObjectLinear);
        float[][] constants = Constants();
        constants[ModelViewMatrix][0] = 2f;
        constants[ModelViewMatrix + 1][1] = 2f;
        constants[TexGenPlane + 1][1] = 1f;

        float[][] inputs = Inputs();
        inputs[Position][1] = 5f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(5f, outputs[9][1]);
    }

    [Fact]
    public void Execute_SphereMapUsesTheReflectedEyeDirection()
    {
        // Eye position (0,0,1), normal (1,0,0): normalize(tPosition) = (0,0,1),
        // reflect gives (0,0,1), r + (0,0,1) has length 2, so
        // invM = 1/4 and oT = r.xy * 0.25 + 0.5 = (0.5, 0.5).
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 0, FixedFunctionVertex.TexGenSphereMap);
        state.SetTexGen(0, 1, FixedFunctionVertex.TexGenSphereMap);
        float[][] constants = Constants();

        float[][] inputs = Inputs();
        inputs[Position][2] = 1f;
        inputs[Normal][0] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(0.5f, outputs[9][0]);
        Assert.Equal(0.5f, outputs[9][1]);
    }

    [Fact]
    public void Execute_SphereMapShiftsWithTheReflectedDirection()
    {
        // Eye position (2,0,0), normal (0,1,0): u = (1,0,0),
        // r = u - 2*dot(normal, u)*normal = (1,0,0); r + (0,0,1) has length
        // sqrt(2), so invM = 1/(2*sqrt2) and oT.s = sqrt(0.5)/2 + 0.5.
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 0, FixedFunctionVertex.TexGenSphereMap);
        float[][] constants = Constants();

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Normal][1] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal((MathF.Sqrt(0.5f) / 2f) + 0.5f, outputs[9][0]);
    }

    [Fact]
    public void Execute_ReflectionMapPassesTheReflectedDirection()
    {
        // Eye (0,0,1) with normal (1,0,0) reflects to (0,0,1).
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 0, FixedFunctionVertex.TexGenReflectionMap);
        state.SetTexGen(0, 2, FixedFunctionVertex.TexGenReflectionMap);
        float[][] constants = Constants();

        float[][] inputs = Inputs();
        inputs[Position][2] = 1f;
        inputs[Normal][0] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(0f, outputs[9][0]);
        Assert.Equal(1f, outputs[9][2]);
    }

    [Fact]
    public void Execute_NormalMapPassesTheEyeSpaceNormal()
    {
        FixedFunctionVertexState state = new();
        state.SetTexGen(0, 0, FixedFunctionVertex.TexGenNormalMap);
        state.SetTexGen(0, 1, FixedFunctionVertex.TexGenNormalMap);
        state.Normalization = true;
        float[][] constants = Constants();
        constants[InverseModelViewMatrix][0] = 2f;
        constants[InverseModelViewMatrix + 1][1] = 2f;

        float[][] inputs = Inputs();
        inputs[Normal][0] = 1f;
        inputs[Normal][1] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        // (2, 2, 0) normalizes to (1/sqrt2, 1/sqrt2, 0).
        Assert.Equal(MathF.Sqrt(0.5f), outputs[9][0]);
        Assert.Equal(MathF.Sqrt(0.5f), outputs[9][1]);
    }

    [Fact]
    public void Execute_TextureMatrixMultipliesTheGeneratedCoordinate()
    {
        FixedFunctionVertexState state = new();
        state.SetTextureMatrixEnable(0, 1u);
        float[][] constants = Constants();
        constants[TextureMatrix][0] = 2f;
        constants[TextureMatrix + 1][1] = 3f;

        float[][] inputs = Inputs();
        inputs[TexCoord0][0] = 0.25f;
        inputs[TexCoord0][1] = 0.5f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(0.5f, outputs[9][0]);
        Assert.Equal(1.5f, outputs[9][1]);
    }

    [Fact]
    public void Execute_UnlitPathCopiesTheDiffuseAttributeThrough()
    {
        (_, float[][] inputs, float[][] outputs) = Run(new FixedFunctionVertexState());

        Assert.Equal(inputs[Diffuse][0], outputs[Diffuse][0]);
        Assert.Equal(inputs[BackDiffuse][3], outputs[BackDiffuse][3]);
        Assert.Equal(inputs[BackSpecular][3], outputs[BackSpecular][3]);
    }

    [Fact]
    public void Execute_SpecularDisabledWritesOpaqueBlack()
    {
        (_, _, float[][] outputs) = Run(new FixedFunctionVertexState());

        Assert.Equal(0f, outputs[Specular][0]);
        Assert.Equal(0f, outputs[Specular][1]);
        Assert.Equal(0f, outputs[Specular][2]);
        Assert.Equal(1f, outputs[Specular][3]);
    }

    [Fact]
    public void Execute_SpecularWithoutSeparateAddsIntoDiffuse()
    {
        FixedFunctionVertexState state = new()
        {
            SpecularEnable = true,
            SeparateSpecular = false,
        };

        (_, float[][] inputs, float[][] outputs) = Run(state);

        Assert.Equal(inputs[Diffuse][0] + inputs[Specular][0], outputs[Diffuse][0]);
        Assert.Equal(inputs[Specular][2], outputs[Specular][2]);
    }

    [Fact]
    public void Execute_SpecularDarkensAlphaWhenTheMaterialAlphaIsSelected()
    {
        FixedFunctionVertexState state = new()
        {
            SpecularEnable = true,
            SeparateSpecular = true,
            AlphaFromMaterialSpecular = false,
        };

        (_, _, float[][] outputs) = Run(state);

        Assert.Equal(1f, outputs[Specular][3]);
    }

    [Fact]
    public void Execute_RadialFogUsesTheEyeSpaceDistance()
    {
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        float[][] inputs = Inputs();
        inputs[Position][0] = 3f;
        inputs[Position][1] = 4f;
        float[][] outputs = Outputs();

        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: true, (int)FogGenRadial);

        Assert.Equal(5f, outputs[FogCoord][0]);
    }

    [Fact]
    public void Execute_PlanarFogUsesTheEyeSpaceDotProduct()
    {
        FixedFunctionVertexState state = new();
        float[][] constants = Constants();
        constants[FogPlane][0] = 2f;
        constants[FogPlane][3] = -1f;
        float[][] inputs = Inputs();
        inputs[Position][0] = 3f;
        float[][] outputs = Outputs();

        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: true, fogGenMode: 2);

        Assert.Equal(5f, outputs[FogCoord][0]);
    }

    [Fact]
    public void Execute_DisabledFogWritesZeroDistance()
    {
        (_, _, float[][] outputs) = Run(new FixedFunctionVertexState());

        Assert.Equal(0f, outputs[FogCoord][0]);
    }

    [Fact]
    public void Execute_OneWeightSkinningDerivesTheSecondWeight()
    {
        // SET_SKIN_MODE_2G: tPosition = position * MMat0 * w.x +
        // position * MMat1 * (1 - w.x).
        FixedFunctionVertexState state = new() { Skinning = (int)SkinModeWeights1 };
        float[][] constants = Constants();
        SetIdentity(constants, ModelViewMatrix);
        constants[ModelViewMatrix][0] = 1f;
        SetIdentity(constants, ModelViewMatrix + 4);
        constants[ModelViewMatrix + 4][0] = 3f;

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Weight][0] = 0.25f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: true, (int)FogGenRadial);

        // (2 * 0.25) + (6 * 0.75) = 5 along x; the radial fog distance is the
        // eye-space length, so 5.
        Assert.Equal(5f, outputs[FogCoord][0]);
    }

    [Fact]
    public void Execute_IndividualWeightSkinningUsesTheSecondWeight()
    {
        FixedFunctionVertexState state = new() { Skinning = (int)SkinModeWeights2Matrices2 };
        float[][] constants = Constants();
        SetIdentity(constants, ModelViewMatrix);
        constants[ModelViewMatrix][0] = 1f;
        SetIdentity(constants, ModelViewMatrix + 4);
        constants[ModelViewMatrix + 4][0] = 3f;

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Weight][0] = 0.5f;
        inputs[Weight][1] = 0.5f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: true, (int)FogGenRadial);

        // (2 * 0.5) + (6 * 0.5) = 4.
        Assert.Equal(4f, outputs[FogCoord][0]);
    }

    [Fact]
    public void Execute_SkinnedPositionGoesThroughTheCompositeMatrix()
    {
        // With skinning on, the composite multiply uses the skinned position
        // (the object-space shortcut only applies when skinning is off).
        FixedFunctionVertexState state = new() { Skinning = (int)SkinModeWeights2Matrices2 };
        float[][] constants = Constants();
        SetIdentity(constants, ModelViewMatrix);
        SetIdentity(constants, ModelViewMatrix + 4);
        constants[ModelViewMatrix + 4][0] = 3f;

        float[][] inputs = Inputs();
        inputs[Position][0] = 2f;
        inputs[Weight][0] = 0f;
        inputs[Weight][1] = 1f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        Assert.Equal(6f, outputs[0][0]);
    }

    [Fact]
    public void State_DecodesTheReferenceRegisterFields()
    {
        FixedFunctionVertexState state = new();
        state.SetLightControl(1u | (1u << 16) | (1u << 17));
        state.SetColorMaterial(0xE4u);

        Assert.True(state.SeparateSpecular);
        Assert.True(state.LocalEye);
        Assert.True(state.AlphaFromMaterialSpecular);
        Assert.Equal(0, state.EmissionSource);
        Assert.Equal(1, state.AmbientSource);
        Assert.Equal(2, state.DiffuseSource);
        Assert.Equal(3, state.SpecularSource);
    }

    [Fact]
    public void Execute_LitMaterialChainUsesTheAmbientSourceEmissionAndMaterialAlpha()
    {
        // The reference (vsh-ff.c) lit path with every light off:
        // oD0.rgb = ambientSource * materialEmission + emissionSource,
        // oD0.a = material alpha when the diffuse source is MATERIAL,
        // oD1 = (0,0,0,specular.a).
        FixedFunctionVertexState state = new()
        {
            Lighting = true,
            DiffuseSource = FixedFunctionVertex.MaterialColorSourceMaterial,
            AmbientSource = FixedFunctionVertex.MaterialColorSourceMaterial,
            EmissionSource = FixedFunctionVertex.MaterialColorSourceMaterial,
        };
        state.SceneAmbient[0] = 0.5f;
        state.SceneAmbient[1] = 0.25f;
        state.SceneAmbient[2] = 0.125f;
        state.MaterialEmission[0] = 2f;
        state.MaterialEmission[1] = 1f;
        state.MaterialEmission[2] = 0.5f;
        state.MaterialAlpha = 0.75f;

        (_, float[][] inputs, float[][] outputs) = Run(state);

        // sceneAmbient * materialEmission + sceneAmbient
        Assert.Equal(1.5f, outputs[Diffuse][0]);
        Assert.Equal(0.5f, outputs[Diffuse][1]);
        Assert.Equal(0.1875f, outputs[Diffuse][2]);
        Assert.Equal(0.75f, outputs[Diffuse][3]);
        Assert.Equal(0f, outputs[Specular][0]);
        Assert.Equal(inputs[Specular][3], outputs[Specular][3]);
    }

    [Fact]
    public void Execute_LitMaterialChainSelectsTheDiffuseAndSpecularSources()
    {
        FixedFunctionVertexState state = new()
        {
            Lighting = true,
            DiffuseSource = FixedFunctionVertex.MaterialColorSourceSpecular,
            AmbientSource = FixedFunctionVertex.MaterialColorSourceDiffuse,
            EmissionSource = FixedFunctionVertex.MaterialColorSourceSpecular,
        };
        state.MaterialEmission[0] = 1f;
        state.MaterialEmission[1] = 1f;
        state.MaterialEmission[2] = 1f;

        float[][] constants = Constants();
        float[][] inputs = Inputs();
        inputs[Diffuse][0] = 0.5f;
        inputs[Diffuse][1] = 0.25f;
        inputs[Diffuse][2] = 0.125f;
        float[][] outputs = Outputs();
        FixedFunctionVertex.Execute(state, constants, inputs, outputs, fogEnable: false, fogGenMode: 0);

        // oD0.rgb = diffuse * 1 + specular, alpha from the specular attribute.
        Assert.Equal(0.75f, outputs[Diffuse][0]);
        Assert.Equal(0.75f, outputs[Diffuse][1]);
        Assert.Equal(0.875f, outputs[Diffuse][2]);
        Assert.Equal(inputs[Specular][3], outputs[Diffuse][3]);
    }

    [Fact]
    public void Execute_LitSpecularDisabledStillWritesOpaqueBlack()
    {
        FixedFunctionVertexState state = new() { Lighting = true };

        (_, _, float[][] outputs) = Run(state);

        Assert.Equal(0f, outputs[Specular][0]);
        Assert.Equal(1f, outputs[Specular][3]);
    }

    [Fact]
    public void Draw_LitChainWritesTheMaterialAmbientAndEmissionColor()
    {
        // Lit draw, every material source = MATERIAL, scene ambient
        // (0.5,0.5,0.5), material emission (1,1,1), material alpha 1:
        // oD0 = ambient * emission + ambient = (1,1,1,1) - white, where the
        // unlit path writes the red diffuse attribute instead.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.FixedFunctionTransform = true;
        WriteAttribute(memory, 0x2000u, [[0f, 0f, 0f, 1f], [2f, 0f, 0f, 1f], [0f, 2f, 0f, 1f]]);
        WriteAttribute(memory, 0x2100u, [[1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f]]);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00001042u),
            (0x1E94u, 0x00000004u),
            (0x0314u, 1u),      // lighting enable
            (0x0298u, 0u),      // color material: all sources MATERIAL
            (0x0A10u, F(0.5f)), // scene ambient r
            (0x0A14u, F(0.5f)), // scene ambient g
            (0x0A18u, F(0.5f)), // scene ambient b
            (0x03A8u, F(1f)),   // material emission r
            (0x03ACu, F(1f)),   // material emission g
            (0x03B0u, F(1f)),   // material emission b
            (0x03B4u, F(1f)),   // material alpha
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
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.FixedFunctionDraws);
        Assert.Equal(1uL, nv2A.FixedFunctionLitDraws);
        Assert.Equal(0uL, nv2A.FixedFunctionLightedDraws);
        Assert.Equal(0xFFFFFFFFu, ReadPixel(memory, 5, 5));
    }

    [Fact]
    public void Draw_LitDrawWithAnEnabledLightIsCountedSeparately()
    {
        // An enabled light runs the reference's light loop; every light color
        // defaults to zero here, so the surface stays black but the counter
        // keeps light usage measurable (fixedFnLights= on the CLI line).
        (Nv2AMmio nv2A, ArrayMemory memory) = DrawFixedFunction(lightingEnable: 1u, lightMask: 0x00000001u);

        Assert.Equal(1uL, nv2A.FixedFunctionDraws);
        Assert.Equal(1uL, nv2A.FixedFunctionLitDraws);
        Assert.Equal(1uL, nv2A.FixedFunctionLightedDraws);
    }

    [Fact]
    public void State_RoundTripsThroughASnapshot()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.FixedFunctionTransform = true;
        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            (0x0314u, 1u),
            (0x03B8u, 1u),
            (0x0294u, 0x00020001u),
            (0x0298u, 0x000000E4u),
            (0x0328u, SkinModeWeights2Matrices2),
            (0x03A4u, 1u),
            (0x03BCu, 0x00000003u),
            (0x03C0u, FixedFunctionVertex.TexGenSphereMap),
            (0x03C4u, FixedFunctionVertex.TexGenSphereMap),
            (0x0430u, FixedFunctionVertex.TexGenReflectionMap),
            (0x0420u, 1u),
            (0x0424u, 1u),
        };
        PushMethods(nv2A, memory, pushes.ToArray());

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            nv2A.SaveState(writer);
        }

        buffer.Position = 0;
        (Nv2AMmio restored, ArrayMemory _) = CreateChannel();
        using (var reader = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        using var second = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(second, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.SaveState(writer);
        }

        Assert.Equal(Convert.ToHexString(buffer.ToArray()), Convert.ToHexString(second.ToArray()));
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

    /// <summary>
    /// One fixed-function draw: 16x16 surface, composite = x4 with a (2,2)
    /// viewport offset, object-space triangle (0,0)-(2,0)-(0,2) which lands on
    /// screen as (2,2)-(10,2)-(2,10), and the vertex diffuse as the color (no
    /// combiner, so the legacy fill path writes oD0 directly).
    /// </summary>
    private static (Nv2AMmio Nv2a, ArrayMemory Memory) DrawFixedFunction(
        bool optIn = true,
        uint lightingEnable = 0u,
        uint lightMask = 0u)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.FixedFunctionTransform = optIn;

        WriteAttribute(
            memory,
            0x2000u,
            [[0f, 0f, 0f, 1f], [2f, 0f, 0f, 1f], [0f, 2f, 0f, 1f]]);
        WriteAttribute(
            memory,
            0x2100u,
            [[1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f]]);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            // 16x16 A8R8G8B8 surface at 0x3000 (pitch 0x40 = 16 pixels).
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            // Position (slot 0) and diffuse (slot 3) as F32x4 arrays.
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00001042u),
            // FIXED transform (0) instead of a vertex program.
            (0x1E94u, 0x00000004u),
            (0x0314u, lightingEnable),
            (0x03BCu, lightMask),
        };

        // Composite = scale 4 on x/y, identity z/w; viewport offset (2,2).
        pushes.Add((0x0680u, F(4f)));
        pushes.Add((0x0684u, F(0f)));
        pushes.Add((0x0688u, F(0f)));
        pushes.Add((0x068Cu, F(0f)));
        pushes.Add((0x0690u, F(0f)));
        pushes.Add((0x0694u, F(4f)));
        pushes.Add((0x0698u, F(0f)));
        pushes.Add((0x069Cu, F(0f)));
        pushes.Add((0x06A0u, F(0f)));
        pushes.Add((0x06A4u, F(0f)));
        pushes.Add((0x06A8u, F(1f)));
        pushes.Add((0x06ACu, F(0f)));
        pushes.Add((0x06B0u, F(0f)));
        pushes.Add((0x06B4u, F(0f)));
        pushes.Add((0x06B8u, F(0f)));
        pushes.Add((0x06BCu, F(1f)));

        pushes.Add((0x0A20u, F(2f)));
        pushes.Add((0x0A24u, F(2f)));
        pushes.Add((0x0A28u, F(0f)));
        pushes.Add((0x0A2Cu, F(0f)));

        // Element 0 is implied by BEGIN_END; the two pushes supply 1 and 2.
        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));

        PushMethods(nv2A, memory, pushes.ToArray());
        return (nv2A, memory);
    }

    [Fact]
    public void Draw_FixedFunctionModeRendersTheObjectSpaceTriangle()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = DrawFixedFunction();

        Assert.Equal(1uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.FixedFunctionDraws);
        Assert.Equal(0uL, nv2A.FixedFunctionLitDraws);
        Assert.True(nv2A.DrawnPixels > 20, $"expected the triangle's ~32 pixels, got {nv2A.DrawnPixels}");

        // The diffuse is red: the composite transform put the triangle at
        // (2,2)-(10,2)-(2,10), so (5,5) is inside and (12,12) is outside.
        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 5, 5));
        Assert.Equal(0u, ReadPixel(memory, 12, 12));
    }

    [Fact]
    public void Draw_FixedFunctionModeIsOptIn()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = DrawFixedFunction(optIn: false);

        // No vertex program is resident, so without the opt-in the draw is
        // skipped exactly as before the fixed-function stage existed.
        Assert.Equal(0uL, nv2A.FixedFunctionDraws);
        Assert.Equal(0uL, nv2A.DrawsExecuted);
        Assert.Equal(1uL, nv2A.SkippedDraws);
        Assert.Equal(0u, ReadPixel(memory, 5, 5));
    }

    [Fact]
    public void Draw_LitDrawUsesTheMaterialChainNotTheDiffuseAttribute()
    {
        // Lighting on with every default zero: the material chain writes
        // (0,0,0, material alpha 0), so the red diffuse attribute does NOT reach
        // the surface - the difference from the unlit path.
        (Nv2AMmio nv2A, ArrayMemory memory) = DrawFixedFunction(lightingEnable: 1u);

        Assert.Equal(1uL, nv2A.FixedFunctionDraws);
        Assert.Equal(1uL, nv2A.FixedFunctionLitDraws);
        Assert.Equal(0uL, nv2A.FixedFunctionLightedDraws);
        Assert.Equal(0u, ReadPixel(memory, 5, 5));
    }

    [Fact]
    public void Draw_ViewportOffsetMovesTheWholeTriangle()
    {
        // Same geometry, offset by (5,3): the triangle becomes (5,3)-(13,3)-(5,11),
        // so (7,5) is inside while (3,3) - inside the unshifted triangle - is not.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.FixedFunctionTransform = true;
        WriteAttribute(memory, 0x2000u, [[0f, 0f, 0f, 1f], [2f, 0f, 0f, 1f], [0f, 2f, 0f, 1f]]);
        WriteAttribute(memory, 0x2100u, [[1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f], [1f, 0f, 0f, 1f]]);

        var pushes = new System.Collections.Generic.List<(uint Method, uint Parameter)>
        {
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            (0x172Cu, 0x00002100u),
            (0x176Cu, 0x00001042u),
            (0x1E94u, 0x00000004u),
            (0x0680u, F(4f)),
            (0x0694u, F(4f)),
            (0x06A8u, F(1f)),
            (0x06BCu, F(1f)),
            (0x0A20u, F(5f)),
            (0x0A24u, F(3f)),
            (0x0A28u, F(0f)),
            (0x0A2Cu, F(0f)),
            (0x17FCu, 5u),
            (0x1800u, 0x00010000u),
            (0x1808u, 0x00000002u),
            (0x17FCu, 0u),
        };
        PushMethods(nv2A, memory, pushes.ToArray());

        Assert.Equal(1uL, nv2A.FixedFunctionDraws);
        Assert.Equal(0xFFFF0000u, ReadPixel(memory, 7, 5));
        Assert.Equal(0u, ReadPixel(memory, 3, 3));
    }
}
