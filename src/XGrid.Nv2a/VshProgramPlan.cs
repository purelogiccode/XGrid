using System;

namespace XGrid.Nv2a;

/// <summary>
/// Compiled form of an NV2A transform (vertex) program. Every token is decoded
/// once, when the active program changes, into a predecoded instruction list;
/// execution then runs those instructions with a plan-owned register file and
/// operand scratch, so there is no per-vertex token parsing, field extraction
/// or allocation. The arithmetic is a transcription of the
/// <see cref="VshProgram"/> interpreter and must stay bit-identical to it
/// (pinned by <c>VshProgramPlanTests</c>); the interpreter remains the
/// fallback and the reference semantics.
/// </summary>
public sealed class VshProgramPlan
{
    private const int AddressOutput = 15;
    private const int FogOutput = 5;
    private const int ScmCorrection = 96;
    private const int AddressRegister = 12;

    private enum SourceKind : byte
    {
        Register,
        Input,
        Constant,
    }

    private enum MacOp : byte
    {
        None,
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

    private enum IluOp : byte
    {
        None,
        Mov,
        Rcp,
        Rcc,
        Rsq,
        Exp,
        Log,
        Lit,
    }

    private readonly struct Source
    {
        public Source(
            SourceKind kind,
            int index,
            int constantBase,
            bool relative,
            bool negate,
            int x,
            int y,
            int z,
            int w)
        {
            Kind = kind;
            Index = index;
            ConstantBase = constantBase;
            Relative = relative;
            Negate = negate;
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public readonly SourceKind Kind;

        /// <summary>Gets the register/input index, or the clamped constant index when not ARL-relative.</summary>
        public readonly int Index;

        /// <summary>Gets the unclamped constant base index for an ARL-relative source.</summary>
        public readonly int ConstantBase;

        /// <summary>Gets whether the constant source is ARL-relative.</summary>
        public readonly bool Relative;

        /// <summary>Gets whether the read value is negated.</summary>
        public readonly bool Negate;

        /// <summary>Gets the swizzle component index for x.</summary>
        public readonly int X;

        /// <summary>Gets the swizzle component index for y.</summary>
        public readonly int Y;

        /// <summary>Gets the swizzle component index for z.</summary>
        public readonly int Z;

        /// <summary>Gets the swizzle component index for w.</summary>
        public readonly int W;
    }

    private readonly struct Instruction
    {
        public Instruction(
            MacOp mac,
            IluOp ilu,
            bool paired,
            in Source a,
            in Source b,
            in Source c,
            int outR,
            int pairedMacMask,
            int macMask,
            int iluMask,
            int outMux,
            int outMask,
            bool orb,
            int outAddress)
        {
            Mac = mac;
            Ilu = ilu;
            Paired = paired;
            A = a;
            B = b;
            C = c;
            OutR = outR;
            PairedMacMask = pairedMacMask;
            MacMask = macMask;
            IluMask = iluMask;
            OutMux = outMux;
            OutMask = outMask;
            Orb = orb;
            OutAddress = outAddress;
        }

        public readonly MacOp Mac;

        public readonly IluOp Ilu;

        public readonly bool Paired;

        public readonly Source A;

        public readonly Source B;

        public readonly Source C;

        public readonly int OutR;

        /// <summary>Gets the MAC write mask in the paired path (zero when outR is r1, reference rule).</summary>
        public readonly int PairedMacMask;

        /// <summary>Gets the MAC write mask in the unpaired path.</summary>
        public readonly int MacMask;

        /// <summary>Gets the ILU write mask.</summary>
        public readonly int IluMask;

        /// <summary>Gets the ORB output mux (0 = MAC result, 1 = ILU result).</summary>
        public readonly int OutMux;

        /// <summary>Gets the ORB output write mask.</summary>
        public readonly int OutMask;

        /// <summary>Gets whether the ORB output write is enabled.</summary>
        public readonly bool Orb;

        /// <summary>Gets the ORB output register index.</summary>
        public readonly int OutAddress;
    }

    private readonly Instruction[] _instructions;
    private readonly float[][] _registers;
    private readonly float[] _mac = new float[4];
    private readonly float[] _ilu = new float[4];

    private VshProgramPlan(Instruction[] instructions, int readInputMask)
    {
        _instructions = instructions;
        ReadInputMask = readInputMask;
        _registers = new float[VshProgram.RegisterCount][];
        for (int i = 0; i < _registers.Length; i++)
        {
            _registers[i] = new float[4];
        }
    }

    /// <summary>Gets the number of predecoded instructions the plan executes.</summary>
    public int InstructionCount => _instructions.Length;

    /// <summary>
    /// Gets the input-register slots (bit <c>n</c> = v<c>n</c>) the program
    /// reads from, i.e. the slots its instruction sources mux from the input
    /// file. A fetcher may skip every other slot: the plan never loads a slot
    /// outside this mask, so an unread slot's value is never observed.
    /// </summary>
    public int ReadInputMask { get; }

    /// <summary>
    /// Decodes <paramref name="length"/> instructions of <paramref name="program"/>
    /// starting at <paramref name="start"/> into an executable plan. Tokens after
    /// the first one with the final bit set are not executed, matching the
    /// interpreter's stop rule.
    /// </summary>
    /// <param name="program">Program storage (four dwords per instruction).</param>
    /// <param name="start">Index of the program's first instruction.</param>
    /// <param name="length">Number of instructions to consider.</param>
    /// <returns>The compiled plan.</returns>
    public static VshProgramPlan Create(uint[][] program, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(program);

        int count = Math.Min(length, VshProgram.MaxInstructions);
        var instructions = new Instruction[count];
        int written = 0;
        int readInputMask = 0;
        for (int slot = 0; slot < count; slot++)
        {
            uint[] token = program[start + slot];
            bool final = VshProgram.Field(token, 3, 0, 1) != 0;
            int mac = VshProgram.Field(token, 1, 21, 4);
            int ilu = VshProgram.Field(token, 1, 25, 3);
            if (mac != 0 || ilu != 0)
            {
                Instruction instruction = Decode(token, mac, ilu);
                instructions[written++] = instruction;
                readInputMask |= InputBit(instruction.A) | InputBit(instruction.B) | InputBit(instruction.C);
            }

            if (final)
            {
                break;
            }
        }

        if (written != instructions.Length)
        {
            Array.Resize(ref instructions, written);
        }

        return new VshProgramPlan(instructions, readInputMask);
    }

    private static int InputBit(in Source source)
    {
        return source.Kind == SourceKind.Input ? 1 << source.Index : 0;
    }

    /// <summary>
    /// Runs the compiled program for one vertex. The contract is the
    /// interpreter's <see cref="VshProgram.Execute(uint[][], int, float[][], float[][], float[][], out bool)"/>:
    /// <paramref name="outputs"/> is initialized to (0,0,0,1) per register, o0
    /// receives the rounded screen-space position with the clamped w, and an
    /// oFog write is reported through <paramref name="fogWritten"/>.
    /// </summary>
    /// <param name="constants">Constant register file (192 entries of four floats).</param>
    /// <param name="inputs">Input registers v0..v15 (four floats each).</param>
    /// <param name="outputs">Output registers o0..o15 (four floats each).</param>
    /// <param name="fogWritten">True when at least one instruction wrote oFog.</param>
    public void Execute(float[][] constants, float[][] inputs, float[][] outputs, out bool fogWritten)
    {
        ArgumentNullException.ThrowIfNull(constants);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        float[][] registers = _registers;
        for (int i = 0; i < registers.Length; i++)
        {
            float[] register = registers[i];
            register[0] = 0.0f;
            register[1] = 0.0f;
            register[2] = 0.0f;
            register[3] = 0.0f;
            float[] output = outputs[i];
            output[0] = 0.0f;
            output[1] = 0.0f;
            output[2] = 0.0f;
            output[3] = 1.0f;
        }

        int address = 0;
        bool fog = false;
        Instruction[] instructions = _instructions;
        for (int slot = 0; slot < instructions.Length; slot++)
        {
            ref readonly Instruction instruction = ref instructions[slot];
            Operand a = LoadSource(in instruction.A, address, constants, inputs, outputs);
            Operand b = LoadSource(in instruction.B, address, constants, inputs, outputs);
            Operand c = LoadSource(in instruction.C, address, constants, inputs, outputs);

            bool hasMacResult = ComputeMac(instruction.Mac, in a, in b, in c, _mac);
            bool hasIluResult = instruction.Ilu != IluOp.None;
            if (hasIluResult)
            {
                ComputeIlu(instruction.Ilu, in c, _ilu);
            }

            if (instruction.Paired)
            {
                if (hasIluResult)
                {
                    VshProgram.WriteMasked(
                        ReadRegister(registers, outputs, 1), _ilu, instruction.IluMask);
                }

                if (hasMacResult)
                {
                    VshProgram.WriteMasked(
                        ReadRegister(registers, outputs, instruction.OutR), _mac, instruction.PairedMacMask);
                }
                else if (instruction.Mac == MacOp.Arl)
                {
                    address = (int)Math.Floor(a.X + 0.001f);
                }
            }
            else
            {
                if (instruction.Mac == MacOp.Arl)
                {
                    address = (int)Math.Floor(a.X + 0.001f);
                }
                else if (hasMacResult)
                {
                    VshProgram.WriteMasked(
                        ReadRegister(registers, outputs, instruction.OutR), _mac, instruction.MacMask);
                }

                if (hasIluResult)
                {
                    VshProgram.WriteMasked(
                        ReadRegister(registers, outputs, instruction.OutR), _ilu, instruction.IluMask);
                }
            }

            if (instruction.OutMask != 0 && instruction.Orb)
            {
                float[]? result = instruction.OutMux == 0 ? (hasMacResult ? _mac : null) : (hasIluResult ? _ilu : null);
                if (result is not null)
                {
                    if (instruction.OutAddress == FogOutput)
                    {
                        VshProgram.WriteFogMasked(outputs[FogOutput], result, instruction.OutMask);
                        fog = true;
                    }
                    else
                    {
                        VshProgram.WriteMasked(outputs[instruction.OutAddress], result, instruction.OutMask);
                    }

                    if (instruction.OutAddress == AddressOutput)
                    {
                        address = (int)outputs[AddressOutput][0];
                    }
                }
            }
        }

        VshProgram.RoundScreenCoords(outputs[0]);
        outputs[0][3] = VshProgram.ClampAwayZeroInf(outputs[0][3]);
        fogWritten = fog;
    }

    private static Instruction Decode(uint[] token, int mac, int ilu)
    {
        int outR = VshProgram.Field(token, 3, 20, 4);
        int outMux = VshProgram.Field(token, 3, 2, 1);
        int outMask = VshProgram.Field(token, 3, 12, 4);
        int outOrb = VshProgram.Field(token, 3, 11, 1);
        int outAddress = VshProgram.Field(token, 3, 3, 8) & 0xF;
        bool paired = mac != 0 && ilu != 0;

        return new Instruction(
            (MacOp)mac,
            (IluOp)ilu,
            paired,
            DecodeSource(
                token, VshProgram.Field(token, 2, 26, 2), VshProgram.Field(token, 2, 28, 4), 1, 6, 8, scalar: false),
            DecodeSource(
                token, VshProgram.Field(token, 2, 11, 2), VshProgram.Field(token, 2, 13, 4), 2, 23, 25,
                scalar: false),
            DecodeSource(
                token,
                VshProgram.Field(token, 3, 28, 2),
                (VshProgram.Field(token, 2, 0, 2) << 2) | VshProgram.Field(token, 3, 30, 2),
                2,
                8,
                10,
                scalar: IsScalarIlu(ilu)),
            outR,
            outR == 1 ? 0 : VshProgram.Field(token, 3, 24, 4),
            VshProgram.Field(token, 3, 24, 4),
            VshProgram.Field(token, 3, 16, 4),
            outMux,
            outMask,
            outOrb == 1,
            outAddress);
    }

    private static Source DecodeSource(
        uint[] token,
        int mux,
        int registerIndex,
        int swizzleSubtoken,
        int swizzleXBit,
        int negateBit,
        bool scalar)
    {
        int x, y, z, w;
        if (scalar)
        {
            x = y = z = w = VshProgram.Field(token, swizzleSubtoken, swizzleXBit, 2);
        }
        else
        {
            x = VshProgram.Field(token, swizzleSubtoken, swizzleXBit, 2);
            y = VshProgram.Field(token, swizzleSubtoken, swizzleXBit - 2, 2);
            z = VshProgram.Field(token, swizzleSubtoken, swizzleXBit - 4, 2);
            w = VshProgram.Field(token, swizzleSubtoken, swizzleXBit - 6, 2);
        }

        bool negate = VshProgram.Field(token, swizzleSubtoken, negateBit, 1) != 0;
        switch (mux)
        {
            case 2:
                return new Source(SourceKind.Input, VshProgram.Field(token, 1, 9, 4), 0, false, negate, x, y, z, w);
            case 3:
                {
                    int raw = VshProgram.Field(token, 1, 13, 8);
                    int constantBase = ((((raw >> 5) & 7) - 3) * 32) + (raw & 31) + ScmCorrection;
                    bool relative = VshProgram.Field(token, 3, 1, 1) != 0;
                    int index = relative ? 0 : Math.Clamp(constantBase, 0, VshProgram.ConstantCount - 1);
                    return new Source(SourceKind.Constant, index, constantBase, relative, negate, x, y, z, w);
                }

            default:
                return new Source(SourceKind.Register, registerIndex, 0, false, negate, x, y, z, w);
        }
    }

    private static bool IsScalarIlu(int ilu)
    {
        return (IluOp)ilu is IluOp.Rcp or IluOp.Rcc or IluOp.Rsq or IluOp.Exp or IluOp.Log;
    }

    private static float[] ReadRegister(float[][] registers, float[][] outputs, int index)
    {
        return index == AddressRegister ? outputs[0] : registers[index];
    }

    /// <summary>
    /// One loaded source operand. The plan keeps its three operands as a
    /// 16-byte value each instead of writing them into per-plan register-file
    /// arrays (12 stores and 12 loads per instruction removed); the arithmetic
    /// below transcribes the interpreter's expressions literally.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private readonly struct Operand
    {
        public Operand(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;
    }

    private Operand LoadSource(
        in Source source,
        int address,
        float[][] constants,
        float[][] inputs,
        float[][] outputs)
    {
        float[] values;
        switch (source.Kind)
        {
            case SourceKind.Input:
                values = inputs[source.Index];
                break;
            case SourceKind.Constant:
                int index = source.Relative
                    ? Math.Clamp(source.ConstantBase + address, 0, VshProgram.ConstantCount - 1)
                    : source.Index;
                values = constants[index];
                break;
            default:
                values = source.Index == AddressRegister ? outputs[0] : _registers[source.Index];
                break;
        }

        return new Operand(
            source.Negate ? -values[source.X] : values[source.X],
            source.Negate ? -values[source.Y] : values[source.Y],
            source.Negate ? -values[source.Z] : values[source.Z],
            source.Negate ? -values[source.W] : values[source.W]);
    }

    private static void Multiply(in Operand a, in Operand b, float[] result)
    {
        result[0] = a.X == 0.0f || b.X == 0.0f ? 0.0f : a.X * b.X;
        result[1] = a.Y == 0.0f || b.Y == 0.0f ? 0.0f : a.Y * b.Y;
        result[2] = a.Z == 0.0f || b.Z == 0.0f ? 0.0f : a.Z * b.Z;
        result[3] = a.W == 0.0f || b.W == 0.0f ? 0.0f : a.W * b.W;
    }

    private static void Dot(in Operand a, in Operand b, int count, float[] result)
    {
        float sum = 0.0f;
        sum += a.X * b.X;
        sum += a.Y * b.Y;
        sum += a.Z * b.Z;
        if (count == 4)
        {
            sum += a.W * b.W;
        }

        result[0] = sum;
        result[1] = sum;
        result[2] = sum;
        result[3] = sum;
    }

    private static bool ComputeMac(MacOp op, in Operand a, in Operand b, in Operand c, float[] result)
    {
        switch (op)
        {
            case MacOp.Mov:
                result[0] = a.X;
                result[1] = a.Y;
                result[2] = a.Z;
                result[3] = a.W;
                break;
            case MacOp.Mul:
                Multiply(in a, in b, result);
                break;
            case MacOp.Add:
                result[0] = a.X + c.X;
                result[1] = a.Y + c.Y;
                result[2] = a.Z + c.Z;
                result[3] = a.W + c.W;
                break;
            case MacOp.Mad:
                Multiply(in a, in b, result);
                result[0] += c.X;
                result[1] += c.Y;
                result[2] += c.Z;
                result[3] += c.W;
                break;
            case MacOp.Dp3:
                Dot(in a, in b, 3, result);
                break;
            case MacOp.Dph:
                {
                    float sum = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + b.W;
                    result[0] = sum;
                    result[1] = sum;
                    result[2] = sum;
                    result[3] = sum;
                    break;
                }

            case MacOp.Dp4:
                Dot(in a, in b, 4, result);
                break;
            case MacOp.Dst:
                result[0] = 1.0f;
                result[1] = a.Y * b.Y;
                result[2] = a.Z;
                result[3] = b.W;
                break;
            case MacOp.Min:
                result[0] = VshProgram.Min(a.X, b.X);
                result[1] = VshProgram.Min(a.Y, b.Y);
                result[2] = VshProgram.Min(a.Z, b.Z);
                result[3] = VshProgram.Min(a.W, b.W);
                break;
            case MacOp.Max:
                result[0] = VshProgram.Max(a.X, b.X);
                result[1] = VshProgram.Max(a.Y, b.Y);
                result[2] = VshProgram.Max(a.Z, b.Z);
                result[3] = VshProgram.Max(a.W, b.W);
                break;
            case MacOp.Slt:
                result[0] = a.X < b.X ? 1.0f : 0.0f;
                result[1] = a.Y < b.Y ? 1.0f : 0.0f;
                result[2] = a.Z < b.Z ? 1.0f : 0.0f;
                result[3] = a.W < b.W ? 1.0f : 0.0f;
                break;
            case MacOp.Sge:
                result[0] = a.X >= b.X ? 1.0f : 0.0f;
                result[1] = a.Y >= b.Y ? 1.0f : 0.0f;
                result[2] = a.Z >= b.Z ? 1.0f : 0.0f;
                result[3] = a.W >= b.W ? 1.0f : 0.0f;
                break;
            default:
                return false;
        }

        return true;
    }

    private static void ComputeIlu(IluOp op, in Operand c, float[] result)
    {
        switch (op)
        {
            case IluOp.Mov:
                result[0] = c.X;
                result[1] = c.Y;
                result[2] = c.Z;
                result[3] = c.W;
                break;
            case IluOp.Rcp:
                VshProgram.Fill(result, 1.0f / c.X);
                break;
            case IluOp.Rcc:
                VshProgram.Fill(result, VshProgram.ClampAwayZeroInf(1.0f / c.X));
                break;
            case IluOp.Rsq:
                VshProgram.Fill(
                    result,
                    c.X == 0.0f
                        ? float.PositiveInfinity
                        : float.IsInfinity(c.X)
                            ? 0.0f
                            : 1.0f / MathF.Sqrt(MathF.Abs(c.X)));
                break;
            case IluOp.Exp:
                result[0] = MathF.Pow(2.0f, MathF.Floor(c.X));
                result[1] = c.X - MathF.Floor(c.X);
                result[2] = MathF.Pow(2.0f, c.X);
                result[3] = 1.0f;
                break;
            case IluOp.Log:
                {
                    float magnitude = MathF.Abs(c.X);
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

            case IluOp.Lit:
                {
                    const float epsilon = 1.0f / 256.0f;
                    float w = VshProgram.Clamp(c.W, -(128.0f - epsilon), 128.0f - epsilon);
                    float x = VshProgram.Max(c.X, 0.0f);
                    float y = VshProgram.Max(c.Y, 0.0f);
                    result[0] = 1.0f;
                    result[1] = x;
                    result[2] = x > 0.0f ? MathF.Pow(2.0f, w * MathF.Log2(y)) : 0.0f;
                    result[3] = 1.0f;
                    break;
                }
        }
    }
}
