using System;

namespace XGrid.Cpu.Tcg;

/// <summary>The result of executing a managed TCG program on the reference interpreter.</summary>
public sealed class TcgResult
{
    internal TcgResult(uint[] registers, uint eflags, uint eip)
    {
        Registers = registers;
        Eflags = eflags;
        Eip = eip;
    }

    /// <summary>Gets the eight general-purpose registers after execution.</summary>
    public uint[] Registers { get; }

    /// <summary>Gets EFLAGS after execution.</summary>
    public uint Eflags { get; }

    /// <summary>Gets EIP after execution.</summary>
    public uint Eip { get; }
}

/// <summary>
/// Reference interpreter for the managed TCG IR (P1S3). It proves the translator
/// and the flag semantics before host code generation (P1S4); the real
/// <see cref="XboxCpu"/> interpreter remains the exact fallback.
/// </summary>
public static class TcgInterpreter
{
    /// <summary>Executes a program against a copy of the given register state.</summary>
    /// <param name="program">The TCG program.</param>
    /// <param name="registers">The eight guest GPRs.</param>
    /// <param name="eflags">The initial EFLAGS.</param>
    /// <returns>The final register state.</returns>
    public static TcgResult Execute(TcgProgram program, ReadOnlySpan<uint> registers, uint eflags)
    {
        if (registers.Length < 8)
        {
            throw new ArgumentException("the TCG interpreter needs all eight GPRs", nameof(registers));
        }

        var gprs = new uint[8];
        registers[..8].CopyTo(gprs);
        var temps = new uint[program.TempCount];
        uint eip = program.StartEip;

        foreach (TcgOp op in program.Ops)
        {
            switch (op.Kind)
            {
                case TcgOpKind.MovImmediate:
                    temps[op.Dest] = op.Immediate;
                    break;
                case TcgOpKind.ReadRegister:
                    temps[op.Dest] = gprs[op.GuestRegister];
                    break;
                case TcgOpKind.WriteRegister:
                    gprs[op.GuestRegister] = temps[op.Left];
                    break;
                case TcgOpKind.Add:
                    eflags = AluOps.Add(temps[op.Left], temps[op.Right], 0, 32, eflags, out uint sum);
                    temps[op.Dest] = sum;
                    break;
                case TcgOpKind.Sub:
                    eflags = AluOps.Sub(temps[op.Left], temps[op.Right], 0, 32, eflags, out uint difference);
                    temps[op.Dest] = difference;
                    break;
                case TcgOpKind.And:
                    temps[op.Dest] = temps[op.Left] & temps[op.Right];
                    eflags = AluOps.Logical(temps[op.Dest], 32, eflags);
                    break;
                case TcgOpKind.Or:
                    temps[op.Dest] = temps[op.Left] | temps[op.Right];
                    eflags = AluOps.Logical(temps[op.Dest], 32, eflags);
                    break;
                case TcgOpKind.Xor:
                    temps[op.Dest] = temps[op.Left] ^ temps[op.Right];
                    eflags = AluOps.Logical(temps[op.Dest], 32, eflags);
                    break;
                case TcgOpKind.Cmp:
                    eflags = AluOps.Sub(temps[op.Left], temps[op.Right], 0, 32, eflags, out _);
                    break;
                case TcgOpKind.Inc:
                    eflags = AluOps.Inc(temps[op.Left], 32, eflags, out uint incremented);
                    temps[op.Dest] = incremented;
                    break;
                case TcgOpKind.Dec:
                    eflags = AluOps.Dec(temps[op.Left], 32, eflags, out uint decremented);
                    temps[op.Dest] = decremented;
                    break;
                case TcgOpKind.SetEip:
                    eip = op.Immediate;
                    break;
                case TcgOpKind.ConditionalSetEip:
                    if (TcgConditions.Met(op.Condition, eflags))
                    {
                        return new TcgResult(gprs, eflags, op.Immediate);
                    }

                    break;
                case TcgOpKind.Exit:
                    return new TcgResult(gprs, eflags, eip);
                default:
                    throw new InvalidOperationException($"unknown TCG op {op.Kind}");
            }
        }

        return new TcgResult(gprs, eflags, eip);
    }
}

/// <summary>The x86 condition-code evaluation (mirrors <c>XboxCpu.EvaluateCondition</c>).</summary>
internal static class TcgConditions
{
    /// <summary>Returns whether the condition holds for the given EFLAGS.</summary>
    /// <param name="condition">The condition code.</param>
    /// <param name="eflags">The flags.</param>
    /// <returns>True when the branch is taken.</returns>
    public static bool Met(ConditionCode condition, uint eflags)
    {
        bool cf = (eflags & FlagBits.Cf) != 0;
        bool zf = (eflags & FlagBits.Zf) != 0;
        bool sf = (eflags & FlagBits.Sf) != 0;
        bool of = (eflags & FlagBits.Of) != 0;
        bool pf = (eflags & FlagBits.Pf) != 0;

        return condition switch
        {
            ConditionCode.Overflow => of,
            ConditionCode.NotOverflow => !of,
            ConditionCode.Below => cf,
            ConditionCode.AboveOrEqual => !cf,
            ConditionCode.Equal => zf,
            ConditionCode.NotEqual => !zf,
            ConditionCode.BelowOrEqual => cf || zf,
            ConditionCode.Above => !cf && !zf,
            ConditionCode.Sign => sf,
            ConditionCode.NotSign => !sf,
            ConditionCode.Parity => pf,
            ConditionCode.NotParity => !pf,
            ConditionCode.Less => sf != of,
            ConditionCode.GreaterOrEqual => sf == of,
            ConditionCode.LessOrEqual => zf || sf != of,
            _ => !zf && sf == of,
        };
    }
}
