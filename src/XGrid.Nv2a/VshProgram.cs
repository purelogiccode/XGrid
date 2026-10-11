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
    /// <param name="outputs">Output registers o0..o15 (four floats each), initialized to (0,0,0,1) before execution; o0 receives the screen-space position.</param>
    public static void Execute(
        uint[][] program,
        int length,
        float[][] constants,
        float[][] inputs,
        float[][] outputs)
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
        int count = Math.Min(length, MaxInstructions);
        for (int slot = 0; slot < count; slot++)
        {
            uint[] token = program[slot];
            bool final = Field(token, 3, 0, 1) != 0;
            int mac = Field(token, 1, 21, 4);
            int ilu = Field(token, 1, 25, 3);
            if (mac != 0 || ilu != 0)
            {
                ExecuteToken(token, mac, ilu, constants, inputs, outputs, registers, ref address);
            }

            if (final)
            {
                break;
            }
        }
    }

    private static void ExecuteToken(
        uint[] token,
        int mac,
        int ilu,
        float[][] constants,
        float[][] inputs,
        float[][] outputs,
        float[][] registers,
        ref int address)
    {
        int outR = Field(token, 3, 20, 4);
        int outMux = Field(token, 3, 2, 1);
        int outMask = Field(token, 3, 12, 4);
        int outOrb = Field(token, 3, 11, 1);
        int outAddress = Field(token, 3, 3, 8) & 0xF;
        bool paired = mac != 0 && ilu != 0;

        float[] a = ReadInput(token, Field(token, 2, 26, 2), Field(token, 2, 28, 4), 1, 6, 8, forceScalar: false, constants, inputs, registers, outputs, address);
        float[] b = ReadInput(token, Field(token, 2, 11, 2), Field(token, 2, 13, 4), 2, 23, 25, forceScalar: false, constants, inputs, registers, outputs, address);
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
                int macMask = Field(token, 3, 24, 4);
                if (outMux == 0 && outR == 1)
                {
                    macMask = 0;
                }

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
                WriteMasked(outputs[outAddress], result, outMask);
                if (outAddress == AddressOutput)
                {
                    address = (int)outputs[outAddress][0];
                }
            }
        }
    }

    private static float[]? ComputeMac(int mac, float[] a, float[] b, float[] c)
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
                    result[i] = Math.Min(a[i], b[i]);
                }

                break;
            case Mac.Max:
                for (int i = 0; i < 4; i++)
                {
                    result[i] = Math.Max(a[i], b[i]);
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

    private static float[] ComputeIlu(int ilu, float[] c)
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
                    : float.IsInfinity(c[0]) ? 0.0f : 1.0f / MathF.Sqrt(MathF.Abs(c[0])));
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
                    float w = Math.Clamp(c[3], -(128.0f - epsilon), 128.0f - epsilon);
                    float x = MathF.Max(c[0], 0.0f);
                    float y = MathF.Max(c[1], 0.0f);
                    result[0] = 1.0f;
                    result[1] = x;
                    result[2] = x > 0.0f ? MathF.Pow(2.0f, w * MathF.Log2(y)) : 0.0f;
                    result[3] = 1.0f;
                    break;
                }

            default:
                break;
        }

        return result;
    }

    private static float[] ReadInput(
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
        Span<int> swizzle = stackalloc int[4] { x, y, z, w };
        for (int i = 0; i < 4; i++)
        {
            float value = source[swizzle[i]];
            result[i] = negate ? -value : value;
        }

        return result;
    }

    private static int ConstantIndex(uint[] token, int address)
    {
        int raw = Field(token, 1, 13, 8);
        int index = ((((raw >> 5) & 7) - 3) * 32) + (raw & 31) + D3DScmCorrection;
        if (Field(token, 3, 1, 1) != 0)
        {
            index += address;
        }

        return Math.Clamp(index, 0, ConstantCount - 1);
    }

    private static float[] ReadRegister(float[][] registers, float[][] outputs, int index)
    {
        if (index == 12)
        {
            return outputs[0];
        }

        return registers[index];
    }

    private static void WriteMasked(float[] destination, float[] value, int mask)
    {
        for (int i = 0; i < 4; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                destination[i] = value[i];
            }
        }
    }

    private static void Multiply(float[] a, float[] b, float[] result)
    {
        for (int i = 0; i < 4; i++)
        {
            result[i] = a[i] * b[i];
        }
    }

    private static void Dot(float[] a, float[] b, int count, float[] result)
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

    private static void Fill(float[] destination, float value)
    {
        destination[0] = value;
        destination[1] = value;
        destination[2] = value;
        destination[3] = value;
    }

    private static float ClampAwayZeroInf(float value)
    {
        if (value > 0.0f || BitConverter.SingleToUInt32Bits(value) == 0)
        {
            return Math.Clamp(value, BitConverter.UInt32BitsToSingle(0x1F800000u), BitConverter.UInt32BitsToSingle(0x5F800000u));
        }

        return Math.Clamp(value, BitConverter.UInt32BitsToSingle(0xDF800000u), BitConverter.UInt32BitsToSingle(0x9F800000u));
    }

    private static bool IsScalarIlu(int ilu)
    {
        return ilu is (int)Ilu.Rcp or (int)Ilu.Rcc or (int)Ilu.Rsq or (int)Ilu.Exp or (int)Ilu.Log;
    }

    private static int Field(uint[] token, int subtoken, int startBit, int bitLength)
    {
        return (int)((token[subtoken] >> startBit) & ~(0xFFFFFFFFu << bitLength));
    }
}
