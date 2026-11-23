// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/nv2a/pgraph/glsl/vsh-prog.c and vsh_regs.h.
// Original copyright (c) 2012 espes; (c) 2014 Jannik Vogel; (c) 2025 Matt Borgerson;
// the reference itself derives from Cxbx VertexShader.cpp (c) 2004 Aaron Robinson,
// Kingofc and Dxbx uPushBuffer.pas (c) 2007 Shadow_tj, PatrickvL.

using System;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A vertex (transform) program interpreter. Instruction decoding follows
/// xemu's field mapping (<c>hw/xbox/nv2a/pgraph/glsl/vsh-prog.c</c>,
/// <c>vsh_regs.h</c>); execution mirrors the semantics of xemu's generated
/// shader. A program runs once per vertex and leaves the screen-space position
/// in output register 0 (oPos).
/// </summary>
public static class VshProgram
{

    /// <summary>Maximum instruction count (NV2A_MAX_TRANSFORM_PROGRAM_LENGTH).</summary>
    public const int MaxInstructions = 136;

    /// <summary>Number of constant registers (NV2A_VERTEXSHADER_CONSTANTS).</summary>
    public const int ConstantCount = 192;

    /// <summary>Number of input/output registers.</summary>
    public const int RegisterCount = 16;

    /// <summary>D3D scene constant correction (VSH_D3DSCM_CORRECTION).</summary>
    private const int D3DScmCorrection = 96;

    private const int AddressOutput = 15;
    private const int FogOutput = 5;

    /// <summary>Fixed-function fog distance: clamped specular alpha (SET_FOG_GEN_MODE 0).</summary>
    public const int FogGenSpecAlpha = 0;

    /// <summary>Fixed-function fog distance: eye-space radial length (SET_FOG_GEN_MODE 1).</summary>
    public const int FogGenRadial = 1;

    /// <summary>Fixed-function fog distance: plane distance (SET_FOG_GEN_MODE 2).</summary>
    public const int FogGenPlanar = 2;

    /// <summary>Fixed-function fog distance: absolute plane distance (SET_FOG_GEN_MODE 3).</summary>
    public const int FogGenAbsPlanar = 3;

    /// <summary>Fixed-function fog distance: fog-coordinate attribute (SET_FOG_GEN_MODE 6).</summary>
    public const int FogGenFogX = 6;

    private enum Mac
    {
        // ReSharper disable once UnusedMember.Local
        Nop = 0,
        Mov,
        Mul,
        Add,
        Mad,
        Dp3,
        Dph,
        Dp4,
        Dst,
        Min,
        Max,
        Slt,
        Sge,
        Arl,
    }

    private enum Ilu
    {
        // ReSharper disable once UnusedMember.Local
        Nop = 0,
        Mov,
        Rcp,
        Rcc,
        Rsq,
        Exp,
        Log,
        Lit,
    }

    /// <summary>Runs the program for one vertex.</summary>
    /// <param name="program">Program tokens (four dwords per instruction).</param>
    /// <param name="length">Number of valid instructions.</param>
    /// <param name="constants">Constant register file (192 entries of four floats).</param>
    /// <param name="inputs">Input registers v0..v15 (four floats each).</param>
    /// <param name="outputs">Output registers o0..o15 (four floats each), initialized to (0,0,0,1) before execution; o0 receives the screen-space position, with xy truncated to 1/16 units and w clamped away from zero/infinity (shader epilogue). o5 carries the fog distance (oFog.x significant; masked components fill x,y,z,w in significance order per the reference fog_mask_str table).</param>
    public static void Execute(
        uint[][] program,
        int length,
        float[][] constants,
        float[][] inputs,
        float[][] outputs)
    {
        Execute(program, length, constants, inputs, outputs, out _);
    }

    /// <summary>Runs the program for one vertex, also reporting whether the oFog register was written.</summary>
    /// <param name="program">Program tokens (four dwords per instruction).</param>
    /// <param name="length">Number of valid instructions.</param>
    /// <param name="constants">Constant register file (192 entries of four floats).</param>
    /// <param name="inputs">Input registers v0..v15 (four floats each).</param>
    /// <param name="outputs">Output registers o0..o15 (four floats each), initialized to (0,0,0,1) before execution; o0 receives the screen-space position, with xy truncated to 1/16 units and w clamped away from zero/infinity (shader epilogue). o5 carries the fog distance (oFog.x significant; masked components fill x,y,z,w in significance order per the reference fog_mask_str table).</param>
    /// <param name="fogWritten">True when at least one instruction wrote the oFog register.</param>
    public static void Execute(
        uint[][] program,
        int length,
        float[][] constants,
        float[][] inputs,
        float[][] outputs,
        out bool fogWritten)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);


        var registers = new float[RegisterCount][];
        for (int i = 0; i < RegisterCount; i++)
        {
            registers[i] = new float[4];
            outputs[i][0] = 0.0f;
            outputs[i][1] = 0.0f;
            outputs[i][2] = 0.0f;
            outputs[i][3] = 1.0f;
        }

        int address = 0;
        fogWritten = false;
        int count = Math.Min(length, MaxInstructions);
        for (int slot = 0; slot < count; slot++)
        {
            uint[] token = program[slot];
            bool final = Field(token, 3, 0, 1) != 0;
            int mac = Field(token, 1, 21, 4);
            int ilu = Field(token, 1, 25, 3);
            if (mac != 0 || ilu != 0)
            {

                ExecuteToken(token, mac, ilu, constants, inputs, outputs, registers, ref address, ref fogWritten);
            }

            if (final)
            {
                break;
            }
        }

        RoundScreenCoords(outputs[0]);
        outputs[0][3] = ClampAwayZeroInf(outputs[0][3]);
    }

    internal static void ExecuteToken(
        uint[] token,
        int mac,
        int ilu,
        float[][] constants,
        float[][] inputs,
        float[][] outputs,
        float[][] registers,
        ref int address,
        ref bool fogWritten)
    {
        int outR = Field(token, 3, 20, 4);
        int outMux = Field(token, 3, 2, 1);
        int outMask = Field(token, 3, 12, 4);
        int outOrb = Field(token, 3, 11, 1);
        int outAddress = Field(token, 3, 3, 8) & 0xF;
        bool paired = mac != 0 && ilu != 0;

        float[] a = ReadInput(token, Field(token, 2, 26, 2), Field(token, 2, 28, 4), 1, 6, 8, forceScalar: false,
            constants, inputs, registers, outputs, address);
        float[] b = ReadInput(token, Field(token, 2, 11, 2), Field(token, 2, 13, 4), 2, 23, 25, forceScalar: false,
            constants, inputs, registers, outputs, address);
        float[] c = ReadInput(
            token,
            Field(token, 3, 28, 2),
            (Field(token, 2, 0, 2) << 2) | Field(token, 3, 30, 2),
            2,
            8,
            10,
            IsScalarIlu(ilu),
            constants,
            inputs,
            registers,
            outputs,
            address);

        float[]? macResult = mac != 0 ? ComputeMac(mac, a, b, c) : null;
        float[]? iluResult = ilu != 0 ? ComputeIlu(ilu, c) : null;

        if (paired)
        {
            if (iluResult is not null)
            {
                WriteMasked(ReadRegister(registers, outputs, 1), iluResult, Field(token, 3, 16, 4));
            }

            if (macResult is not null)
            {
                int macMask = outR == 1 ? 0 : Field(token, 3, 24, 4);
                WriteMasked(ReadRegister(registers, outputs, outR), macResult, macMask);
            }
            else if (mac == (int)Mac.Arl)
            {
                address = (int)Math.Floor(a[0] + 0.001f);
            }
        }
        else
        {
            if (mac == (int)Mac.Arl)
            {
                address = (int)Math.Floor(a[0] + 0.001f);
            }
            else if (macResult is not null)
            {
                WriteMasked(ReadRegister(registers, outputs, outR), macResult, Field(token, 3, 24, 4));
            }

            if (iluResult is not null)
            {
                WriteMasked(ReadRegister(registers, outputs, outR), iluResult, Field(token, 3, 16, 4));
            }
        }

        if (outMask != 0 && outOrb == 1)
        {
            float[]? result = outMux == 0 ? macResult : iluResult;
            if (result is not null)
            {
                if (outAddress == FogOutput)
                {
                    WriteFogMasked(outputs[FogOutput], result, outMask);
                    fogWritten = true;
                }
                else
                {
                    WriteMasked(outputs[outAddress], result, outMask);
                }

                if (outAddress == AddressOutput)
                {
                    address = (int)outputs[outAddress][0];
                }
            }
        }
    }

    internal static float[]? ComputeMac(int mac, float[] a, float[] b, float[] c)
    {
        var result = new float[4];
        switch ((Mac)mac)
        {
            case Mac.Mov:
                Array.Copy(a, result, 4);
                break;
            case Mac.Mul:
                Multiply(a, b, result);
                break;
            case Mac.Add:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = a[i] + c[i];
                }

                break;
            case Mac.Mad:
                Multiply(a, b, result);
                for (int i = 0; i < 4; i++)
                {
                    result[i] += c[i];
                }

                break;
            case Mac.Dp3:
                Dot(a, b, 3, result);
                break;
            case Mac.Dph:
                result[0] = (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]) + b[3];
                result[1] = result[0];
                result[2] = result[0];
                result[3] = result[0];
                break;
            case Mac.Dp4:
                Dot(a, b, 4, result);
                break;
            case Mac.Dst:
                result[0] = 1.0f;
                result[1] = a[1] * b[1];
                result[2] = a[2];
                result[3] = b[3];
                break;
            case Mac.Min:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = Min(a[i], b[i]);
                }

                break;
            case Mac.Max:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = Max(a[i], b[i]);
                }

                break;
            case Mac.Slt:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = a[i] < b[i] ? 1.0f : 0.0f;
                }

                break;
            case Mac.Sge:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = a[i] >= b[i] ? 1.0f : 0.0f;
                }

                break;
            default:
                return null;
        }

        return result;
    }

    internal static float[] ComputeIlu(int ilu, float[] c)
    {
        var result = new float[4];
        switch ((Ilu)ilu)
        {
            case Ilu.Mov:
                Array.Copy(c, result, 4);
                break;
            case Ilu.Rcp:
                Fill(result, 1.0f / c[0]);
                break;
            case Ilu.Rcc:
                Fill(result, ClampAwayZeroInf(1.0f / c[0]));
                break;
            case Ilu.Rsq:
                Fill(result, c[0] == 0.0f
                    ? float.PositiveInfinity
                    : float.IsInfinity(c[0])
                        ? 0.0f
                        : 1.0f / MathF.Sqrt(MathF.Abs(c[0])));
                break;
            case Ilu.Exp:
                result[0] = MathF.Pow(2.0f, MathF.Floor(c[0]));
                result[1] = c[0] - MathF.Floor(c[0]);
                result[2] = MathF.Pow(2.0f, c[0]);
                result[3] = 1.0f;
                break;
            case Ilu.Log:
                {
                    float magnitude = MathF.Abs(c[0]);
                    if (magnitude == 0.0f)
                    {
                        result[0] = float.NegativeInfinity;
                        result[1] = 1.0f;
                        result[2] = float.NegativeInfinity;
                        result[3] = 1.0f;
                    }
                    else
                    {
                        float floorLog = MathF.Floor(MathF.Log2(magnitude));
                        result[0] = floorLog;
                        result[1] = magnitude / MathF.Pow(2.0f, floorLog);
                        result[2] = MathF.Log2(magnitude);
                        result[3] = 1.0f;
                    }

                    break;
                }

            case Ilu.Lit:
                {
                    const float epsilon = 1.0f / 256.0f;
                    float w = Clamp(c[3], -(128.0f - epsilon), 128.0f - epsilon);
                    float x = Max(c[0], 0.0f);
                    float y = Max(c[1], 0.0f);
                    result[0] = 1.0f;
                    result[1] = x;
                    result[2] = x > 0.0f ? MathF.Pow(2.0f, w * MathF.Log2(y)) : 0.0f;
                    result[3] = 1.0f;
                    break;
                }

        }

        return result;
    }

    internal static float[] ReadInput(
        uint[] token,
        int mux,
        int registerIndex,
        int swizzleSubtoken,
        int swizzleXBit,
        int negateBit,
        bool forceScalar,
        float[][] constants,
        float[][] inputs,
        float[][] registers,
        float[][] outputs,
        int address)
    {
        float[] source = mux switch
        {
            2 => inputs[Field(token, 1, 9, 4)],
            3 => constants[ConstantIndex(token, address)],
            _ => ReadRegister(registers, outputs, registerIndex),
        };

        int x, y, z, w;
        if (forceScalar)
        {
            x = y = z = w = Field(token, swizzleSubtoken, swizzleXBit, 2);
        }
        else
        {
            x = Field(token, swizzleSubtoken, swizzleXBit, 2);
            y = Field(token, swizzleSubtoken, swizzleXBit - 2, 2);
            z = Field(token, swizzleSubtoken, swizzleXBit - 4, 2);
            w = Field(token, swizzleSubtoken, swizzleXBit - 6, 2);
        }

        bool negate = Field(token, swizzleSubtoken, negateBit, 1) != 0;
        var result = new float[4];
        Span<int> swizzle = [x, y, z, w];
        for (int i = 0; i < 4; i++)
        {
            float value = source[swizzle[i]];
            result[i] = negate ? -value : value;
        }

        return result;
    }

    internal static int ConstantIndex(uint[] token, int address)
    {
        int raw = Field(token, 1, 13, 8);
        int index = ((((raw >> 5) & 7) - 3) * 32) + (raw & 31) + D3DScmCorrection;
        if (Field(token, 3, 1, 1) != 0)
        {
            index += address;
        }

        return Math.Clamp(index, 0, ConstantCount - 1);
    }

    internal static float[] ReadRegister(float[][] registers, float[][] outputs, int index)
    {
        return index == 12 ? outputs[0] : registers[index];
    }

    internal static void WriteMasked(float[] destination, float[] value, int mask)
    {
        // Hardware mask order is bit-reversed against the component index:
        // bit 3 writes x, bit 2 writes y, bit 1 writes z, bit 0 writes w
        // (xemu mask_str table; Cxbx MASK_X=0x008..MASK_W=0x001).
        for (int i = 0; i < 4; i++)
        {
            if ((mask & (1 << (3 - i))) != 0)
            {
                destination[i] = value[i];
            }
        }
    }

    /// <summary>
    /// Writes a fog-register (oFog) result. The reference always starts the
    /// destination at oFog.x and fills the first N components from the
    /// result's x-first components, where N is the number of set mask bits
    /// (xemu fog_mask_str destination-prefix selection with the opcode-macro
    /// source swizzle; Cxbx redirects non-x oFog masks to x the same way).
    /// Only oFog.x feeds the fog-distance equation.
    /// </summary>
    internal static void WriteFogMasked(float[] destination, float[] value, int mask)
    {
        int count = 0;
        for (int bit = 0; bit < 4; bit++)
        {
            if ((mask & (1 << bit)) != 0)
            {
                count++;
            }
        }

        for (int i = 0; i < count && i < 4; i++)
        {
            destination[i] = value[i];
        }
    }

    /// <summary>
    /// Computes the fixed-function fog distance for the given SET_FOG_GEN_MODE
    /// value, following the reference fixed-function vertex path (xemu
    /// <c>vsh-ff.c</c>, from the NV_fog_distance spec): SPEC_ALPHA clamps the
    /// specular alpha to [0,1]; RADIAL takes the position length; PLANAR dots
    /// the fog plane (xyz) with the position plus the plane w; ABS_PLANAR
    /// takes the absolute value; FOG_X passes the fog-coordinate attribute
    /// through. The reference consumes the eye-space position (position times
    /// the model-view matrix); this rasterizer has no fixed-function
    /// transform unit, so the object-space position attribute (v0.xyz) is
    /// fed instead. Unknown modes fall back to SPEC_ALPHA (hardware asserts).
    /// </summary>
    public static float FogGenDistance(
        int genMode,
        float fogCoord,
        float posX,
        float posY,
        float posZ,
        float specAlpha,
        float[] fogPlane)
    {
        ArgumentNullException.ThrowIfNull(fogPlane);

        return genMode switch
        {
            FogGenRadial => MathF.Sqrt((posX * posX) + (posY * posY) + (posZ * posZ)),
            FogGenPlanar => (fogPlane[0] * posX) + (fogPlane[1] * posY) + (fogPlane[2] * posZ) + fogPlane[3],
            FogGenAbsPlanar => MathF.Abs((fogPlane[0] * posX) + (fogPlane[1] * posY) + (fogPlane[2] * posZ) +
                                         fogPlane[3]),
            FogGenFogX => fogCoord,
            _ => Math.Clamp(specAlpha, 0f, 1f),
        };
    }

    /// <summary>
    /// Computes the per-vertex fog factor from the fog distance (oFog.x) and
    /// the FOGPARAM pair, following the reference vertex-shader fog equations.
    /// Mode ids match the CONTROL_3 encoding: 0 LINEAR, 1 EXP, 3 EXP2, 4
    /// LINEAR_ABS, 5 EXP_ABS, 7 EXP2_ABS. Infinite distance yields 1, as does a
    /// NaN result; unknown modes fall back to the linear equation.
    /// </summary>
    public static float FogFactor(int mode, float paramX, float paramY, float distance)
    {
        if (float.IsInfinity(distance))
        {
            return 1f;
        }

        float factor = mode switch
        {
            1 or 5 => paramX + (MathF.Pow(2f, distance * paramY * 16f) - 1.5f),
            3 or 7 => paramX + (MathF.Pow(2f, -distance * distance * paramY * paramY * 32f) - 1.5f),
            _ => paramX + (distance * paramY) - 1f,
        };

        if (mode is 4 or 5 or 7)
        {
            factor = MathF.Abs(factor);
        }

        return float.IsNaN(factor) ? 1f : factor;
    }

    internal static void Multiply(float[] a, float[] b, float[] result)
    {
        for (int i = 0; i < 4; i++)
        {
            result[i] = a[i] == 0.0f || b[i] == 0.0f ? 0.0f : a[i] * b[i];
        }
    }

    internal static void Dot(float[] a, float[] b, int count, float[] result)
    {
        float sum = 0.0f;
        for (int i = 0; i < count; i++)
        {
            sum += a[i] * b[i];
        }

        result[0] = sum;
        result[1] = sum;
        result[2] = sum;
        result[3] = sum;
    }

    internal static void Fill(float[] destination, float value)
    {
        destination[0] = value;
        destination[1] = value;
        destination[2] = value;
        destination[3] = value;
    }

    internal static float ClampAwayZeroInf(float value)
    {
        return value > 0.0f || BitConverter.SingleToUInt32Bits(value) == 0
            ? Math.Clamp(value, BitConverter.UInt32BitsToSingle(0x1F800000u),
                BitConverter.UInt32BitsToSingle(0x5F800000u))
            : Math.Clamp(value, BitConverter.UInt32BitsToSingle(0xDF800000u),
            BitConverter.UInt32BitsToSingle(0x9F800000u));
    }

    internal static float Min(float x, float y)
    {
        return y < x ? y : x;
    }

    internal static float Max(float x, float y)
    {
        return y > x ? y : x;
    }

    internal static float Clamp(float value, float minimum, float maximum)
    {
        return Min(Max(value, minimum), maximum);
    }

    internal static void RoundScreenCoords(float[] position)
    {
        position[0] = MathF.Truncate(position[0] * 16.0f) / 16.0f;
        position[1] = MathF.Truncate(position[1] * 16.0f) / 16.0f;
    }

    internal static bool IsScalarIlu(int ilu)
    {
        return ilu is (int)Ilu.Rcp or (int)Ilu.Rcc or (int)Ilu.Rsq or (int)Ilu.Exp or (int)Ilu.Log;
    }

    internal static int Field(uint[] token, int subtoken, int startBit, int bitLength)
    {
        return (int)((token[subtoken] >> startBit) & ~(0xFFFFFFFFu << bitLength));
    }
}
