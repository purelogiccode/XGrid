using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Differential gate for <see cref="VshProgramPlan"/>: the compiled transform
/// program must reproduce the <see cref="VshProgram"/> interpreter's per-vertex
/// outputs bit-for-bit, including every opcode, operand mux, swizzle, negate,
/// write mask, ARL-relative constant, the oA0/oFog ORB writes and the
/// paired-MAC/ILU register-1 rule.
/// </summary>
public sealed class VshProgramPlanTests
{
    private const int MacMov = 1;
    private const int MacMul = 2;
    private const int MacAdd = 3;
    private const int MacMad = 4;
    private const int MacDp3 = 5;
    private const int MacArl = 13;
    private const int IluMov = 1;
    private const int IluRcp = 2;
    private const int IluRsq = 4;

    private const int MuxR = 1;
    private const int MuxV = 2;
    private const int MuxC = 3;

    private static readonly float[] EdgeValues =
    [
        0.0f,
        -0.0f,
        1.0f,
        -1.0f,
        0.5f,
        -2.25f,
        1.0f / 3.0f,
        1e30f,
        -1e30f,
        float.NaN,
        float.PositiveInfinity,
        float.NegativeInfinity,
        float.Epsilon,
        -float.Epsilon,
    ];


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
        bool relative = false,
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
        if (relative)
        {
            t3 |= 1u << 1;
        }

        if (final)
        {
            t3 |= 1u;
        }

        return [0u, t1, t2, t3];
    }

    private static float[][] Registers(int count, int seed, bool edge)
    {
        var random = new Random(seed);
        var registers = new float[count][];
        for (int i = 0; i < count; i++)
        {
            registers[i] = new float[4];
            for (int j = 0; j < 4; j++)
            {
                registers[i][j] = edge
                    ? EdgeValues[random.Next(EdgeValues.Length)]
                    : (float)((random.NextDouble() * 4.0) - 2.0);
            }
        }

        return registers;
    }

    private static float[][] Copy(float[][] source)
    {
        var copy = new float[source.Length][];
        for (int i = 0; i < source.Length; i++)
        {
            copy[i] = [source[i][0], source[i][1], source[i][2], source[i][3]];
        }

        return copy;
    }

    /// <summary>
    /// Builds the comparison program: the test's instructions followed by a dump
    /// tail that copies every internal register r0..r15 into o0..o15, so every
    /// write-path difference reaches the compared outputs. o5 (oFog) is skipped
    /// by the tail so a program's own fog behaviour is not masked.
    /// </summary>

    private static uint[][] BuildWithDumpTail(uint[][] program, int length)
    {
        var full = new uint[length + VshProgram.RegisterCount][];
        Array.Copy(program, full, length);
        for (int register = 0; register < VshProgram.RegisterCount; register++)
        {
            full[length + register] = Insn(
                mac: MacMov,
                aMux: MuxR,
                aR: register,
                outR: register,
                macMask: 0xF,
                outMux: 0,
                outMask: register == 5 ? 0 : 0xF,
                outAddress: register,
                final: register == VshProgram.RegisterCount - 1);
        }

        return full;
    }

    private static void Compare(uint[][] program, int length, float[][] constants, float[][] inputs)
    {
        float[][] interpreterOutputs = Registers(VshProgram.RegisterCount, 7, edge: false);
        VshProgram.Execute(program, length, constants, Copy(inputs), interpreterOutputs, out bool interpreterFog);

        VshProgramPlan plan = VshProgramPlan.Create(program, 0, length);
        float[][] planOutputs = Registers(VshProgram.RegisterCount, 7, edge: false);
        plan.Execute(constants, Copy(inputs), planOutputs, out bool planFog);

        Assert.Equal(interpreterFog, planFog);
        for (int register = 0; register < VshProgram.RegisterCount; register++)
        {
            for (int component = 0; component < 4; component++)
            {
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(interpreterOutputs[register][component]),
                    BitConverter.SingleToUInt32Bits(planOutputs[register][component]));
            }
        }
    }

    /// <summary>
    /// Runs the same program and vertex through the interpreter and the compiled
    /// plan, with the dump tail, and asserts every output register is
    /// bit-identical.
    /// </summary>
    private static void AssertSameAsInterpreter(
        uint[][] program,
        int length,
        float[][] constants,
        float[][] inputs)
    {
        uint[][] full = BuildWithDumpTail(program, length);
        Compare(full, full.Length, constants, inputs);
    }

    [Fact]
    public void Plan_MatchesInterpreter_ForEveryMacAndIluOpcode()
    {
        for (int mac = 0; mac <= 15; mac++)
        {
            for (int ilu = 0; ilu <= 7; ilu++)
            {
                uint[][] program =
                [
                    // Seed r1..r4 with distinct values from the vertices and constants.
                    Insn(mac: MacMov, aMux: MuxV, v: 1, outR: 1, macMask: 0xF),
                    Insn(mac: MacMov, aMux: MuxC, constIndex: 1, outR: 2, macMask: 0xF),
                    Insn(mac: MacMad, aMux: MuxR, aR: 1, bMux: MuxV, v: 2, cMux: MuxC, constIndex: 2, outR: 3,
                        macMask: 0xF),
                    // The opcode under test with three register operands and every write path.
                    Insn(mac: mac, ilu: ilu, aMux: MuxR, aR: 1, bMux: MuxR, bR: 2, cMux: MuxR, cR: 3, outR: 4,
                        macMask: 0x7, iluMask: 0xB, outMux: 1, outMask: 0x5, outAddress: 6),
                ];

                AssertSameAsInterpreter(
                    program,
                    4,
                    Registers(VshProgram.ConstantCount, 11, edge: true),
                    Registers(VshProgram.RegisterCount, 12, edge: true));
            }
        }
    }

    [Fact]
    public void Plan_MatchesInterpreter_ForEverySourceSwizzleAndNegate()
    {
        for (int swizzle = 0; swizzle < 256; swizzle++)
        {
            uint[][] program =
            [
                Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 1, macMask: 0xF),
                Insn(mac: MacMul, aMux: MuxR, aR: 1, bMux: MuxC, constIndex: 3, outR: 2, macMask: 0xF),
                Insn(
                    mac: MacAdd,
                    ilu: IluMov,
                    aMux: MuxR,
                    aR: 1,
                    aSwz: swizzle,
                    aNeg: (swizzle & 1) != 0,
                    bMux: MuxR,
                    bR: 2,
                    bSwz: 255 - swizzle,
                    cMux: MuxC,
                    constIndex: 4,
                    cSwz: swizzle,
                    cNeg: (swizzle & 2) != 0,
                    outR: 3,
                    macMask: 0xF,
                    iluMask: 0xF,
                    outMux: 0,
                    outMask: 0xF,
                    outAddress: 7),
            ];

            AssertSameAsInterpreter(
                program,
                3,
                Registers(VshProgram.ConstantCount, 21, edge: true),
                Registers(VshProgram.RegisterCount, 22, edge: true));
        }
    }

    [Fact]
    public void Plan_MatchesInterpreter_OnArlRelativeConstantsAndAddressOutput()
    {
        for (int address = -3; address <= 3; address++)
        {
            uint[][] program =
            [
                // A0.x = v0.x (ARL), including a fractional and a negative source value.
                Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 1, macMask: 0xF),
                Insn(mac: MacArl, aMux: MuxR, aR: 1),
                // oA0 = v1.x; the address register then feeds a relative constant read.
                Insn(mac: MacMov, aMux: MuxV, v: 1, outR: 2, macMask: 0xF),
                Insn(mac: MacMov, aMux: MuxR, aR: 2, outMux: 0, outMask: 0xF, outAddress: 15),
                Insn(mac: MacAdd, aMux: MuxC, constIndex: 32, relative: true, bMux: MuxR, bR: 1, outR: 3,
                    macMask: 0xF),
            ];

            float[][] inputs = Registers(VshProgram.RegisterCount, 31, edge: false);
            inputs[0] = [address + 0.4f, 0.0f, 0.0f, 1.0f];
            inputs[1] = [address, 0.0f, 0.0f, 1.0f];

            AssertSameAsInterpreter(
                program,
                5,
                Registers(VshProgram.ConstantCount, 32, edge: true),
                inputs);
        }
    }

    [Fact]
    public void Plan_MatchesInterpreter_OnFogWritesAndEveryOutputMask()
    {
        for (int mask = 1; mask <= 0xF; mask++)
        {
            for (int mux = 0; mux <= 1; mux++)
            {
                uint[][] program =
                [
                    Insn(mac: MacMov, aMux: MuxV, v: 2, outR: 1, macMask: 0xF),
                    Insn(
                        mac: MacDp3,
                        ilu: IluRcp,
                        aMux: MuxR,
                        aR: 1,
                        bMux: MuxC,
                        cMux: MuxC,
                        constIndex: 5,
                        outR: 3,
                        macMask: 0xF,
                        iluMask: 0xF,
                        outMux: mux,
                        outMask: mask,
                        outAddress: 5),
                ];

                Compare(BuildWithDumpTail(program, 2), 2 + VshProgram.RegisterCount,
                    Registers(VshProgram.ConstantCount, 41, edge: true),
                    Registers(VshProgram.RegisterCount, 42, edge: true));
            }
        }

        // No ORB mask write: the fog flag must stay clear in both.
        uint[][] plain = [Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 0, outMask: 0xF, outAddress: 4)];
        uint[][] full = BuildWithDumpTail(plain, 1);
        Compare(full, full.Length,
            Registers(VshProgram.ConstantCount, 43, edge: true),
            Registers(VshProgram.RegisterCount, 44, edge: true));
    }

    [Fact]
    public void Plan_MatchesInterpreter_OnPairedRegisterOneRule()
    {
        for (int outR = 0; outR <= 15; outR++)
        {
            uint[][] program =
            [
                Insn(mac: MacMov, aMux: MuxV, v: 3, outR: 1, macMask: 0xF),
                Insn(mac: MacMul, ilu: IluRsq, aMux: MuxR, aR: 1, bMux: MuxC, cMux: MuxC, constIndex: 7, outR: outR,
                    macMask: 0x6, iluMask: 0x9, outMux: 1, outMask: 0xF, outAddress: outR),
            ];

            AssertSameAsInterpreter(
                program,
                2,
                Registers(VshProgram.ConstantCount, 51, edge: true),
                Registers(VshProgram.RegisterCount, 52, edge: true));
        }
    }

    [Fact]
    public void Plan_StopsAtTheFirstFinalInstruction()
    {
        uint[][] program =
        [
            Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 0, outMask: 0xE, outAddress: 0),
            // A no-op token carrying the final bit: the plan must drop it but stop here.
            Insn(final: true),
            Insn(mac: MacMov, aMux: MuxV, v: 1, outR: 0, outMask: 0xF, outAddress: 0, final: true),
        ];

        VshProgramPlan plan = VshProgramPlan.Create(program, 0, 3);
        Assert.Equal(1, plan.InstructionCount);
        Compare(program, 3,
            Registers(VshProgram.ConstantCount, 61, edge: true),
            Registers(VshProgram.RegisterCount, 62, edge: true));
    }

    [Fact]
    public void Plan_ClampsLengthToTheHardwareMaximum()
    {
        uint[][] program = new uint[VshProgram.MaxInstructions + 8][];
        for (int i = 0; i < program.Length; i++)
        {
            program[i] = Insn(mac: MacMov, aMux: MuxV, v: i & 0xF, outR: i & 0xF, macMask: 0xF);
        }

        VshProgramPlan plan = VshProgramPlan.Create(program, 0, program.Length);
        Assert.Equal(VshProgram.MaxInstructions, plan.InstructionCount);
        Compare(program, program.Length,
            Registers(VshProgram.ConstantCount, 63, edge: true),
            Registers(VshProgram.RegisterCount, 64, edge: true));
    }

    /// <summary>
    /// The plan reports which input slots its sources mux from, which is what
    /// lets the device fetch only those slots (D5). The mask names exactly the
    /// slots the program reads: a program that reads v0, v4 and v11 reports
    /// those three and nothing else.
    /// </summary>
    [Fact]
    public void ReadInputMask_ReportsExactlyTheInputSlotsTheProgramReads()
    {
        uint[][] program =
        [
            Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMul, aMux: MuxR, aR: 1, bMux: MuxV, v: 4, outR: 2, macMask: 0xF),
            Insn(mac: MacMad, aMux: MuxR, aR: 2, bMux: MuxC, constIndex: 7, cMux: MuxV, v: 11, outR: 3,
                macMask: 0xF, final: true),
        ];

        VshProgramPlan plan = VshProgramPlan.Create(program, 0, program.Length);
        Assert.Equal((1 << 0) | (1 << 4) | (1 << 11), plan.ReadInputMask);

        // Register/constant-only sources read no input slot at all.
        uint[][] constantOnly =
        [
            Insn(mac: MacMov, aMux: MuxC, constIndex: 1, outR: 1, macMask: 0xF),
            Insn(mac: MacAdd, aMux: MuxR, aR: 1, bMux: MuxC, constIndex: 2, outR: 2, macMask: 0xF,
                final: true),
        ];
        Assert.Equal(0, VshProgramPlan.Create(constantOnly, 0, constantOnly.Length).ReadInputMask);
    }

    /// <summary>
    /// The mask is safe by construction: every input slot it leaves out can be
    /// overwritten with arbitrary garbage (including NaN and infinities) and the
    /// plan's outputs stay bit-identical, because no instruction source reads
    /// those slots. If the mask under-reported a read slot, this fails.
    /// </summary>
    [Fact]
    public void ReadInputMask_ScramblingUnreadSlotsCannotChangeTheOutputs()
    {
        uint[][] program =
        [
            Insn(mac: MacMov, aMux: MuxV, v: 0, outR: 1, macMask: 0xF),
            Insn(mac: MacMul, aMux: MuxR, aR: 1, bMux: MuxV, v: 7, outR: 2, macMask: 0xF),
            Insn(mac: MacAdd, aMux: MuxR, aR: 2, bMux: MuxC, constIndex: 3, outR: 3, macMask: 0xF,
                final: true),
        ];
        uint[][] full = BuildWithDumpTail(program, program.Length);
        VshProgramPlan plan = VshProgramPlan.Create(full, 0, full.Length);
        float[][] constants = Registers(VshProgram.ConstantCount, 71, edge: true);
        float[][] inputs = Registers(VshProgram.RegisterCount, 72, edge: true);

        float[][] baseline = Registers(VshProgram.RegisterCount, 7, edge: false);
        plan.Execute(constants, inputs, baseline, out bool baselineFog);

        float[][] scrambledInputs = Copy(inputs);
        for (int slot = 0; slot < VshProgram.RegisterCount; slot++)
        {
            if ((plan.ReadInputMask & (1 << slot)) != 0)
            {
                continue;
            }

            scrambledInputs[slot] = Registers(1, 100 + slot, edge: true)[0];
        }

        float[][] after = Registers(VshProgram.RegisterCount, 7, edge: false);
        plan.Execute(constants, scrambledInputs, after, out bool afterFog);

        Assert.Equal(baselineFog, afterFog);
        for (int register = 0; register < VshProgram.RegisterCount; register++)
        {
            for (int component = 0; component < 4; component++)
            {
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(baseline[register][component]),
                    BitConverter.SingleToUInt32Bits(after[register][component]));
            }
        }
    }

    [Fact]
    public void Plan_MatchesInterpreter_OnRandomPrograms()
    {
        var random = new Random(20261023);
        for (int iteration = 0; iteration < 400; iteration++)
        {
            int count = 1 + random.Next(6);
            var program = new uint[count][];
            for (int i = 0; i < count; i++)
            {
                uint token1 = (uint)random.Next(int.MinValue, int.MaxValue);
                uint token2 = (uint)random.Next(int.MinValue, int.MaxValue);
                // The MAC/ILU fields are free (their out-of-range values must behave
                // the same in both paths); the dump tail supplies the final bit.
                uint token3 = (uint)random.Next(int.MinValue, int.MaxValue) & ~1u;
                program[i] = [0u, token1, token2, token3];
            }

            float[][] constants = Registers(VshProgram.ConstantCount, 1000 + iteration, edge: true);
            for (int register = 0; register < VshProgram.ConstantCount; register++)
            {
                for (int component = 0; component < 4; component++)
                {
                    if (random.Next(4) == 0)
                    {
                        constants[register][component] = (float)((random.NextDouble() * 8.0) - 4.0);
                    }
                }
            }

            AssertSameAsInterpreter(
                program,
                count,
                constants,
                Registers(VshProgram.RegisterCount, 2000 + iteration, edge: true));
        }
    }
}
