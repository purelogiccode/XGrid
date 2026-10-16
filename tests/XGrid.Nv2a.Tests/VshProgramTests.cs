using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class VshProgramTests
{
    private const int MacMov = 1;
    private const int MacMul = 2;
    private const int MacMad = 4;
    private const int MacDp3 = 5;
    private const int MacDp4 = 7;
    private const int MacMin = 9;
    private const int MacMax = 10;
    private const int MacSlt = 11;
    private const int MacSge = 12;
    private const int MacArl = 13;
    private const int IluMov = 1;
    private const int IluRcp = 2;
    private const int IluLog = 6;
    private const int IluLit = 7;

    private const int MuxR = 1;
    private const int MuxV = 2;
    private const int MuxC = 3;

    private static int Swizzle(int x, int y, int z, int w)
    {
        return (x << 6) | (y << 4) | (z << 2) | w;
    }

    private static uint[] Insn(
        int mac = 0,
        int ilu = 0,
        int outR = 0,
        int outMux = 0,
        int outMask = 0,
        int outAddress = 0,
        int aMux = MuxR,
        int aR = 0,
        int bMux = MuxR,
        int bR = 0,
        int cMux = MuxR,
        int cR = 0,
        int macMask = 0,
        int iluMask = 0,
        int v = 0,
        int constIndex = 0,
        int aSwz = 0x1B,
        int bSwz = 0x1B,
        int cSwz = 0x1B,
        bool aNeg = false,
        bool bNeg = false,
        bool cNeg = false,
        bool a0X = false,
        bool final = false)
    {
        uint t1 = ((uint)ilu << 25) | ((uint)mac << 21) | ((uint)constIndex << 13) | ((uint)v << 9);
        if (aNeg)
        {
            t1 |= 1u << 8;
        }

        t1 |= (uint)(aSwz & 0xFF);

        uint t2 = ((uint)aR << 28) | ((uint)aMux << 26);
        if (bNeg)
        {
            t2 |= 1u << 25;
        }

        t2 |= (uint)((bSwz & 0xFF) << 17);
        t2 |= (uint)bR << 13;
        t2 |= (uint)bMux << 11;
        if (cNeg)
        {
            t2 |= 1u << 10;
        }

        t2 |= (uint)((cSwz & 0xFF) << 2);
        t2 |= (uint)((cR >> 2) & 3);

        uint t3 = (uint)((cR & 3) << 30);
        t3 |= (uint)cMux << 28;
        t3 |= (uint)macMask << 24;
        t3 |= (uint)outR << 20;
        t3 |= (uint)iluMask << 16;
        t3 |= (uint)outMask << 12;
        t3 |= 1u << 11;
        t3 |= (uint)outAddress << 3;
        t3 |= (uint)outMux << 2;
        if (a0X)
        {
            t3 |= 1u << 1;
        }

        if (final)
        {
            t3 |= 1u;
        }

        return new[] { 0u, t1, t2, t3 };
    }

    private static float[][] ZeroRegisters(int count)
    {
        var registers = new float[count][];
        for (int i = 0; i < count; i++)
        {
            registers[i] = new float[4];
        }

        return registers;
    }

    private static void AssertPosition(float[][] outputs, float x, float y, float z, float w)
    {
        Assert.Equal(x, outputs[0][0]);
        Assert.Equal(y, outputs[0][1]);
        Assert.Equal(z, outputs[0][2]);
        Assert.Equal(w, outputs[0][3]);
    }

    [Fact]
    public void Mov_ToPosition_WritesInput()
    {
        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true) };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        AssertPosition(outputs, 1.0f, 2.0f, 3.0f, 4.0f);
    }

    [Fact]
    public void Mad_WithConstant_MultipliesAndAdds()
    {
        uint[][] program =
        {
            Insn(mac: MacMad, aMux: MuxV, v: 0, bMux: MuxC, cMux: MuxC, constIndex: 0, outR: 0, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        constants[0] = new[] { 2.0f, 2.0f, 2.0f, 2.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 4.0f, 6.0f, 8.0f, 10.0f);
    }

    [Fact]
    public void Dp4_BroadcastsDotProduct()
    {
        uint[][] program =
        {
            Insn(mac: MacDp4, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 0, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        constants[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 10.0f, 10.0f, 10.0f, 10.0f);
    }

    [Fact]
    public void Dp3_IgnoresW()
    {
        uint[][] program =
        {
            Insn(mac: MacDp3, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 0, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 100.0f };
        constants[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 6.0f, 6.0f, 6.0f, 6.0f);
    }

    [Fact]
    public void SwizzleAndNegate_AreApplied()
    {
        uint[][] program =
        {
            Insn(mac: MacMov, aMux: MuxV, v: 0, aSwz: Swizzle(1, 0, 3, 2), aNeg: true, outR: 0, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, -2.0f, -1.0f, -4.0f, -3.0f);
    }

    [Fact]
    public void WriteMask_PreservesUntouchedComponents()
    {
        uint[][] program =
        {
            Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0x3, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxC, constIndex: 0, outMux: 0, outMask: 0xC, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        constants[0] = new[] { 9.0f, 9.0f, 9.0f, 9.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        // Hardware mask order is bit-reversed (bit 0 = w): 0x3 writes z,w
        // from v0 and 0xC writes x,y from c0.
        AssertPosition(outputs, 9.0f, 9.0f, 3.0f, 4.0f);
    }

    [Fact]
    public void PairedMacAndIlu_BothExecute()
    {
        uint[][] program =
        {
            Insn(
                mac: MacMul,
                ilu: IluRcp,
                aMux: MuxV,
                v: 0,
                bMux: MuxC,
                cMux: MuxC,
                constIndex: 0,
                outR: 0,
                outMux: 0,
                outMask: 0xF,
                outAddress: 0,
                macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        constants[0] = new[] { 4.0f, 4.0f, 4.0f, 4.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 4.0f, 8.0f, 12.0f, 16.0f);
    }

    [Fact]
    public void FinalBit_TerminatesProgram()
    {
        uint[][] program =
        {
            Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
            Insn(mac: MacMov, aMux: MuxC, constIndex: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 5.0f, 6.0f, 7.0f, 8.0f };
        constants[0] = new[] { 9.0f, 9.0f, 9.0f, 9.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 5.0f, 6.0f, 7.0f, 8.0f);
    }

    [Fact]
    public void ArlAndRelativeConstant_AddressesConstants()
    {
        uint[][] program =
        {
            Insn(mac: MacArl, aMux: MuxV, v: 0),
            Insn(mac: MacMov, aMux: MuxC, constIndex: 0, a0X: true, outR: 0, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 2.0f, 0.0f, 0.0f, 0.0f };
        constants[2] = new[] { 7.0f, 7.0f, 7.0f, 7.0f };

        VshProgram.Execute(program, 3, constants, inputs, outputs);

        AssertPosition(outputs, 7.0f, 7.0f, 7.0f, 7.0f);
    }

    [Fact]
    public void IluMov_WritesTempRegister()
    {
        uint[][] program =
        {
            Insn(ilu: IluMov, cMux: MuxC, constIndex: 0, outR: 0, iluMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { 1.5f, 2.5f, 3.5f, 4.5f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 1.5f, 2.5f, 3.5f, 4.5f);
    }

    [Fact]
    public void UnwrittenOutputs_DefaultToZeroWithUnitAlpha()
    {
        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true) };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        AssertPosition(outputs, 1.0f, 2.0f, 3.0f, 4.0f);
        for (int i = 1; i < VshProgram.RegisterCount; i++)
        {
            Assert.Equal(0.0f, outputs[i][0]);
            Assert.Equal(0.0f, outputs[i][1]);
            Assert.Equal(0.0f, outputs[i][2]);
            Assert.Equal(1.0f, outputs[i][3]);
        }
    }

    private static float[][] ExecuteIlu(int ilu, float x, float y, float z, float w)
    {
        uint[][] program =
        {
            Insn(ilu: ilu, cMux: MuxC, constIndex: 0, outR: 1, iluMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { x, y, z, w };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        return outputs;
    }

    [Theory]
    [InlineData(0x7F800000u)]
    [InlineData(0xFF800000u)]
    [InlineData(0x7FC00000u)]
    public void Mul_NonFiniteTimesZero_ReturnsZero(uint operandBits)
    {
        float operand = BitConverter.UInt32BitsToSingle(operandBits);
        uint[][] program =
        {
            Insn(
                mac: MacMul,
                aMux: MuxC,
                constIndex: 0,
                bMux: MuxR,
                bR: 0,
                outR: 1,
                macMask: 0xF,
                outMux: 0,
                outMask: 0x7,
                outAddress: 0,
                final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { operand, operand, operand, operand };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        // Mask 0x7 selects y, z, w under the hardware bit order. The w lane
        // passes through the shader-epilogue clamp-away-from-zero (xemu),
        // so 0.0 arrives as +2^-64, not +0.0.
        AssertPosition(outputs, 0.0f, 0.0f, 0.0f, BitConverter.UInt32BitsToSingle(0x1F800000u));
    }

    [Fact]
    public void Mad_NonFiniteTimesZero_AddsConstant()
    {
        uint[][] program =
        {
            Insn(
                mac: MacMad,
                aMux: MuxC,
                constIndex: 0,
                bMux: MuxR,
                bR: 0,
                cMux: MuxV,
                v: 0,
                outR: 1,
                macMask: 0xF,
                outMux: 0,
                outMask: 0xF,
                outAddress: 0,
                final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[]
            { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        inputs[0] = new[] { 5.0f, 6.0f, 7.0f, 8.0f };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        AssertPosition(outputs, 5.0f, 6.0f, 7.0f, 8.0f);
    }

    [Fact]
    public void Min_WithNaN_MatchesGLSLComparisons()
    {
        uint[][] program =
        {
            Insn(mac: MacMin, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMin, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0xE, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { float.NaN, float.NaN, float.NaN, float.NaN };
        inputs[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Mask 0x1 selects w and 0xE selects x, y, z under the hardware bit order.
        Assert.Equal(1.0f, outputs[0][0]);
        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(1.0f, outputs[0][2]);
        Assert.True(float.IsNaN(outputs[0][3]));
    }

    [Fact]
    public void Max_WithNaN_MatchesGLSLComparisons()
    {
        uint[][] program =
        {
            Insn(mac: MacMax, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMax, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0xE, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { float.NaN, float.NaN, float.NaN, float.NaN };
        inputs[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Mask 0x1 selects w and 0xE selects x, y, z under the hardware bit order.
        Assert.Equal(1.0f, outputs[0][0]);
        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(1.0f, outputs[0][2]);
        Assert.True(float.IsNaN(outputs[0][3]));
    }

    [Fact]
    public void Min_WithSignedZeros_ReturnsFirstOperandWhenEqual()
    {
        uint[][] program =
        {
            Insn(mac: MacMin, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMin, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x6, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { -0.0f, -0.0f, -0.0f, -0.0f };
        inputs[0] = new[] { 0.0f, 0.0f, 0.0f, 0.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Mask 0x1 selects w and 0x6 selects y, z under the hardware bit order.
        // The -0.0 in w passes through the epilogue clamp-away-from-zero as -2^-64.
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][0]));
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][1]));
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][2]));
        Assert.Equal(0x9F800000u, BitConverter.SingleToUInt32Bits(outputs[0][3]));
    }

    [Fact]
    public void Max_WithSignedZeros_ReturnsFirstOperandWhenEqual()
    {
        uint[][] program =
        {
            Insn(mac: MacMax, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMax, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x6, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { -0.0f, -0.0f, -0.0f, -0.0f };
        inputs[0] = new[] { 0.0f, 0.0f, 0.0f, 0.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Mask 0x1 selects w and 0x6 selects y, z under the hardware bit order.
        // The -0.0 in w passes through the epilogue clamp-away-from-zero as -2^-64.
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][0]));
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][1]));
        Assert.Equal(0x00000000u, BitConverter.SingleToUInt32Bits(outputs[0][2]));
        Assert.Equal(0x9F800000u, BitConverter.SingleToUInt32Bits(outputs[0][3]));
    }

    [Fact]
    public void Slt_WithNaN_ReturnsZero()
    {
        uint[][] program =
        {
            Insn(mac: MacSlt, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacSlt, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacSlt, aMux: MuxR, aR: 0, bMux: MuxV, v: 0, outR: 3, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x2, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 3, outMux: 0, outMask: 0x4, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { float.NaN, float.NaN, float.NaN, float.NaN };
        inputs[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 6, constants, inputs, outputs);

        // Masks 0x1/0x2/0x4 select w, z, y under the hardware bit order.
        // The 0.0 in w passes through the epilogue clamp-away-from-zero as +2^-64.
        AssertPosition(outputs, 0.0f, 1.0f, 0.0f, BitConverter.UInt32BitsToSingle(0x1F800000u));
    }

    [Fact]
    public void Slt_WithSignedZeros_ReturnsZero()
    {
        uint[][] program =
        {
            Insn(mac: MacSlt, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacSlt, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x2, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { -0.0f, -0.0f, -0.0f, -0.0f };
        inputs[0] = new[] { 0.0f, 0.0f, 0.0f, 0.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Masks 0x1/0x2 select w, z under the hardware bit order.
        // The 0.0 in w passes through the epilogue clamp-away-from-zero as +2^-64.
        AssertPosition(outputs, 0.0f, 0.0f, 0.0f, BitConverter.UInt32BitsToSingle(0x1F800000u));
    }

    [Fact]
    public void Sge_WithNaN_ReturnsZero()
    {
        uint[][] program =
        {
            Insn(mac: MacSge, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacSge, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacSge, aMux: MuxV, bMux: MuxV, v: 0, outR: 3, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x2, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 3, outMux: 0, outMask: 0x4, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { float.NaN, float.NaN, float.NaN, float.NaN };
        inputs[0] = new[] { 1.0f, 1.0f, 1.0f, 1.0f };

        VshProgram.Execute(program, 6, constants, inputs, outputs);

        // Masks 0x1/0x2/0x4 select w, z, y under the hardware bit order.
        // The 0.0 in w passes through the epilogue clamp-away-from-zero as +2^-64.
        AssertPosition(outputs, 0.0f, 1.0f, 0.0f, BitConverter.UInt32BitsToSingle(0x1F800000u));
    }

    [Fact]
    public void Sge_WithSignedZeros_ReturnsOne()
    {
        uint[][] program =
        {
            Insn(mac: MacSge, aMux: MuxC, constIndex: 0, bMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacSge, aMux: MuxV, v: 0, bMux: MuxC, constIndex: 0, outR: 2, macMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0x1, outAddress: 0),
            Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0x2, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { -0.0f, -0.0f, -0.0f, -0.0f };
        inputs[0] = new[] { 0.0f, 0.0f, 0.0f, 0.0f };

        VshProgram.Execute(program, 4, constants, inputs, outputs);

        // Masks 0x1/0x2 select w, z under the hardware bit order.
        AssertPosition(outputs, 0.0f, 0.0f, 1.0f, 1.0f);
    }

    [Fact]
    public void Log_Zero_ReturnsNegativeInfinityExponentAndUnitMantissa()
    {
        float[][] outputs = ExecuteIlu(IluLog, 0.0f, 0.0f, 0.0f, 0.0f);

        Assert.Equal(float.NegativeInfinity, outputs[0][0]);
        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(float.NegativeInfinity, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Log_NegativeZero_ReturnsNegativeInfinityExponentAndUnitMantissa()
    {
        float[][] outputs = ExecuteIlu(IluLog, -0.0f, -0.0f, -0.0f, -0.0f);

        Assert.Equal(float.NegativeInfinity, outputs[0][0]);
        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(float.NegativeInfinity, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Log_Infinity_ReturnsInfiniteExponentAndNaNMantissa()
    {
        float[][] outputs = ExecuteIlu(IluLog, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity,
            float.PositiveInfinity);

        Assert.Equal(float.PositiveInfinity, outputs[0][0]);
        Assert.True(float.IsNaN(outputs[0][1]));
        Assert.Equal(float.PositiveInfinity, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Log_DecomposesExponentAndMantissa()
    {
        float[][] outputs = ExecuteIlu(IluLog, 12.0f, 12.0f, 12.0f, 12.0f);

        Assert.Equal(3.0f, outputs[0][0]);
        Assert.Equal(1.5f, outputs[0][1]);
        Assert.Equal(MathF.Log2(12.0f), outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);

        outputs = ExecuteIlu(IluLog, 0.75f, 0.75f, 0.75f, 0.75f);

        Assert.Equal(-1.0f, outputs[0][0]);
        Assert.Equal(1.5f, outputs[0][1]);
        Assert.Equal(MathF.Log2(0.75f), outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Lit_ComputesPowerFromZeroAndPositiveInputs()
    {
        float[][] outputs = ExecuteIlu(IluLit, 1.0f, 2.0f, 0.0f, 3.0f);

        Assert.Equal(1.0f, outputs[0][0]);
        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(8.0f, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Lit_ZeroX_ZeroesPower()
    {
        float[][] outputs = ExecuteIlu(IluLit, 0.0f, 2.0f, 0.0f, 3.0f);

        Assert.Equal(1.0f, outputs[0][0]);
        Assert.Equal(0.0f, outputs[0][1]);
        Assert.Equal(0.0f, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void Lit_ZeroY_WithPositiveW_ZeroesPower()
    {
        float[][] outputs = ExecuteIlu(IluLit, 1.0f, 0.0f, 0.0f, 3.0f);

        Assert.Equal(1.0f, outputs[0][1]);
        Assert.Equal(0.0f, outputs[0][2]);
    }

    [Fact]
    public void Lit_ZeroY_WithZeroW_ProducesNaNPower()
    {
        float[][] outputs = ExecuteIlu(IluLit, 1.0f, 0.0f, 0.0f, 0.0f);

        Assert.Equal(1.0f, outputs[0][1]);
        Assert.True(float.IsNaN(outputs[0][2]));
    }

    [Fact]
    public void Lit_ClampsWTo128MinusEpsilon()
    {
        float limit = 128.0f - (1.0f / 256.0f);

        float[][] positive = ExecuteIlu(IluLit, 1.0f, 2.0f, 0.0f, 200.0f);
        float[][] positiveAtLimit = ExecuteIlu(IluLit, 1.0f, 2.0f, 0.0f, limit);
        Assert.Equal(positiveAtLimit[0][2], positive[0][2]);
        Assert.False(float.IsInfinity(positive[0][2]));

        float[][] negative = ExecuteIlu(IluLit, 1.0f, 2.0f, 0.0f, -200.0f);
        float[][] negativeAtLimit = ExecuteIlu(IluLit, 1.0f, 2.0f, 0.0f, -limit);
        Assert.Equal(negativeAtLimit[0][2], negative[0][2]);
        Assert.NotEqual(0.0f, negative[0][2]);
    }

    [Fact]
    public void Lit_NegativeZeroX_PreservesNegativeZeroY()
    {
        float[][] outputs = ExecuteIlu(IluLit, -0.0f, 2.0f, 0.0f, 3.0f);

        Assert.Equal(1.0f, outputs[0][0]);
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(outputs[0][1]));
        Assert.Equal(0.0f, outputs[0][2]);
        Assert.Equal(1.0f, outputs[0][3]);
    }

    [Fact]
    public void PairedMacAndIlu_OutMuxIluWithOutR1_IluValueWins()
    {
        uint[][] program =
        {
            Insn(
                mac: MacMov,
                ilu: IluMov,
                aMux: MuxV,
                v: 0,
                cMux: MuxC,
                constIndex: 0,
                outR: 1,
                outMux: 1,
                outMask: 0,
                macMask: 0xF,
                iluMask: 0xF),
            Insn(mac: MacMov, aMux: MuxR, aR: 1, outMux: 0, outMask: 0xF, outAddress: 0, final: true),
        };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        constants[0] = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        inputs[0] = new[] { 5.0f, 6.0f, 7.0f, 8.0f };

        VshProgram.Execute(program, 2, constants, inputs, outputs);

        AssertPosition(outputs, 1.0f, 2.0f, 3.0f, 4.0f);
    }

    [Fact]
    public void Position_TruncatesXyTowardZeroToSixteenthUnits()
    {
        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true) };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 0.099f, -1.01f, 2.5f, 3.5f };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        Assert.Equal(0.0625f, outputs[0][0]);
        Assert.Equal(-1.0f, outputs[0][1]);
        Assert.Equal(2.5f, outputs[0][2]);
        Assert.Equal(3.5f, outputs[0][3]);
    }

    [Theory]
    [InlineData(0x00000000u, 0x1F800000u)]
    [InlineData(0x80000000u, 0x9F800000u)]
    [InlineData(0x7F800000u, 0x5F800000u)]
    [InlineData(0xFF800000u, 0xDF800000u)]
    [InlineData(0x00000001u, 0x1F800000u)]
    [InlineData(0x7FC00000u, 0x7FC00000u)]
    [InlineData(0x3F800000u, 0x3F800000u)]
    public void Position_ClampsWAwayFromZeroAndInfinity(uint wBits, uint expectedBits)
    {
        uint[][] program = { Insn(mac: MacMov, aMux: MuxV, v: 0, outMux: 0, outMask: 0xF, outAddress: 0, final: true) };
        float[][] constants = ZeroRegisters(VshProgram.ConstantCount);
        float[][] inputs = ZeroRegisters(VshProgram.RegisterCount);
        float[][] outputs = ZeroRegisters(VshProgram.RegisterCount);
        inputs[0] = new[] { 0.0f, 0.0f, 0.0f, BitConverter.UInt32BitsToSingle(wBits) };

        VshProgram.Execute(program, 1, constants, inputs, outputs);

        Assert.Equal(expectedBits, BitConverter.SingleToUInt32Bits(outputs[0][3]));
    }
}
