using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class VshProgramTests
{
    private const int MacMov = 1;
    private const int MacMul = 2;
    private const int MacMad = 4;
    private const int MacDp3 = 5;
    private const int MacDp4 = 7;
    private const int MacArl = 13;
    private const int IluMov = 1;
    private const int IluRcp = 2;

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
        bool a0x = false,
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
        if (a0x)
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

        AssertPosition(outputs, 1.0f, 2.0f, 9.0f, 9.0f);
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
            Insn(mac: MacMov, aMux: MuxC, constIndex: 0, a0x: true, outR: 0, macMask: 0xF),
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
}
