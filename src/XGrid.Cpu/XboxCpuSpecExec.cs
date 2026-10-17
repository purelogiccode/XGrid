using System;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// JIT semspec slice (§139): pre-specialized per-shape straight-line handlers
/// plus the form-time shape table. No emitter, no codegen: each handler is the
/// exact interpreter case body for one (mnemonic, operand kinds, size) shape
/// with the mnemonic/operand-kind/size switches resolved at block-formation
/// time. GPR indices, immediates, displacements, EFLAGS, segments and
/// addresses stay data read from the block's cached member, exactly as the
/// interpreter reads them. Privileged, mode-changing, REP/string,
/// I/O, FPU and invalid shapes never resolve: they keep the interpreter.
/// Near conditional jumps and near jump/call/return resolve (they always
/// terminate their block, so the handler only sets EIP); all other control
/// flow keeps the interpreter.
/// </summary>
public sealed partial class XboxCpu
{
    /// <summary>
    /// One pre-specialized straight-line body. Advances
    /// <see cref="XboxContext.Eip"/> only after successful completion, exactly
    /// like <see cref="ExecuteInstruction"/>: a fault leaves EIP at the member
    /// start for precise fault reporting. Never touches live state beyond what
    /// the mirrored interpreter body touches.
    /// </summary>
    /// <param name="cpu">The CPU to execute on.</param>
    /// <param name="instruction">The block's cached member (shared lifetime).</param>
    public delegate void SpecHandler(XboxCpu cpu, in DecodedInstruction instruction);

    /// <summary>
    /// Shape key baked per member at formation time (never live state):
    /// mnemonic, operand kinds, operand size, Jcc condition and the
    /// segment-override-or-none. GPR indices, immediates, displacements,
    /// EFLAGS, segments and addresses stay data. The segment override
    /// participates in key identity; the initial handlers consume it as data
    /// through <see cref="ComputeAddress"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct SpecShape(
        Mnemonic Mnemonic,
        OperandKind Op1Kind,
        OperandKind Op2Kind,
        int Size,
        ConditionCode Condition,
        SegmentRegister? SegmentOverride)
    {
        /// <summary>Builds the shape key for one decoded member.</summary>
        /// <param name="instruction">The member to key.</param>
        /// <returns>The form-time shape key.</returns>
        public static SpecShape From(in DecodedInstruction instruction)
        {
            return new SpecShape(
                instruction.Mnemonic,
                instruction.Operand1.Kind,
                instruction.Operand2.Kind,
                instruction.Operand1.Size,
                instruction.Condition,
                instruction.Operand1.Memory.SegmentOverride
                ?? instruction.Operand2.Memory.SegmentOverride);
        }
    }

    /// <summary>
    /// Resolves a member to its pre-specialized handler, or null when the
    /// shape is outside the initial cover (the member keeps the interpreter).
    /// Allocation-free: returns cached delegate instances.
    /// </summary>
    /// <param name="instruction">The member to resolve.</param>
    /// <returns>The handler, or null for fallback shapes.</returns>
    public static SpecHandler? ResolveSpec(in DecodedInstruction instruction)
    {
        // Slice 12: the scalar-SSE shapes carry Rep==Rep (their 0xF3 byte
        // is a mandatory opcode prefix, never a repeat), so they are
        // exempt from the REP reject below. Every other Rep-prefixed
        // instruction keeps the interpreter.
        if (!instruction.IsValid
            || (instruction.Rep != RepPrefix.None && !IsMandatoryF3Scalar(instruction.Mnemonic)))
        {
            return null;
        }

        OperandKind first = instruction.Operand1.Kind;
        OperandKind second = instruction.Operand2.Kind;
        int size = instruction.Operand1.Size;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Nop:
                return SpecNopHandler;
            case Mnemonic.Mov:
                return ResolveSpecMov(first, second, size);
            case Mnemonic.Add:
            case Mnemonic.Sub:
            case Mnemonic.And:
            case Mnemonic.Or:
            case Mnemonic.Xor:
                return ResolveSpecAlu(instruction.Mnemonic, first, second, size)
                    ?? ResolveSpecAluMem(instruction.Mnemonic, first, second, size);
            case Mnemonic.Cmp:
            case Mnemonic.Test:
                return ResolveSpecTest(instruction.Mnemonic, first, second, size)
                    ?? ResolveSpecAluMem(instruction.Mnemonic, first, second, size);
            case Mnemonic.Inc:
                return ResolveSpecIncDec(isInc: true, first, size);
            case Mnemonic.Dec:
                return ResolveSpecIncDec(isInc: false, first, size);
            case Mnemonic.Lea:
                return first == OperandKind.Register && second == OperandKind.Memory
                    ? ResolveSpecLea(size)
                    : null;
            case Mnemonic.Push:
                return second == OperandKind.None
                    ? ResolveSpecPush(first, size)
                    : null;
            case Mnemonic.Mul:
                // Slice 15: single-operand unsigned multiply (reg/mem).
                // The shared body calls the same `ExecuteMulDiv` dispatch
                // the interpreter uses, so flags and the EDX:EAX writes
                // match exactly. Slice 16 extends the same body to Div/Idiv
                // (a #DE raise transfers EIP mid-block; the serve-time
                // `_expectedLinear` guard redirects exactly like the
                // control-flow handlers, so whitelisting is safe).
                return first != OperandKind.Immediate && second == OperandKind.None
                    ? SpecMulHandler
                    : null;
            case Mnemonic.Div:
            case Mnemonic.Idiv:
                // Slice 16: single-operand divide through the shared body.
                return first != OperandKind.Immediate && second == OperandKind.None
                    ? SpecDivHandler
                    : null;
            case Mnemonic.Imul:
                // Slice 15: all signed-multiply forms through the same
                // `ExecuteImul` helper (1-op r/m, 2-op reg,reg/reg,mem,
                // 3-op reg,reg/mem,imm). Reads precede writes in every
                // form, so EIP stays precise exactly like the interpreter.
                return first == OperandKind.Register
                    ? SpecImulHandler
                    : null;
            case Mnemonic.Neg:
                // Slice 16: two's-complement negate, Inc/Dec-pattern
                // mirror (read, `AluOps.Neg`, write, EIP after).
                return second == OperandKind.None
                    && (first == OperandKind.Register || first == OperandKind.Memory)
                    ? SpecNegHandler
                    : null;
            case Mnemonic.Not:
                // Slice 16: bitwise complement (no flags touched).
                return second == OperandKind.None
                    && (first == OperandKind.Register || first == OperandKind.Memory)
                    ? SpecNotHandler
                    : null;
            case Mnemonic.Bswap:
                // Slice 16: byte-swap, register-only, no flags.
                return first == OperandKind.Register && second == OperandKind.None
                    ? SpecBswapHandler
                    : null;
            case Mnemonic.Xchg:
                // Slice 16: exchange via the verbatim read-read-write-write
                // sequence (fault order identical since the code is shared
                // shape-for-shape with the interpreter's inline case).
                return (first == OperandKind.Register || first == OperandKind.Memory)
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecXchgHandler
                    : null;
            case Mnemonic.Leave:
                // Slice 16: frame teardown through the same `ExecuteLeave`
                // helper (16/32-bit ESP/EBP handling inside).
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecLeaveHandler
                    : null;
            case Mnemonic.Fiadd:
            case Mnemonic.Fimul:
            case Mnemonic.Fisub:
            case Mnemonic.Fisubr:
            case Mnemonic.Fidiv:
            case Mnemonic.Fidivr:
                // Slice 16: x87 integer arithmetic (mem-only) through one
                // shared body: integer read faults before ST(0) updates.
                return first == OperandKind.Memory && second == OperandKind.None
                    ? ResolveSpecFpuInteger(instruction.Mnemonic)
                    : null;
            case Mnemonic.Fnstsw:
                // Slice 16: status word to AX or m16, verbatim mirror.
                return second == OperandKind.None
                    && (first == OperandKind.None || first == OperandKind.Memory)
                    ? SpecFnstswHandler
                    : null;
            case Mnemonic.Fcom:
            case Mnemonic.Fcomp:
            case Mnemonic.Fucom:
            case Mnemonic.Fucomp:
                // Slice 17: x87 compares (ST(i) or ST(0),mem) through one
                // shared body: the compared operand is read into a temp
                // before `SetFpuCompare` touches any status bit, and the
                // conditional pop runs only after a successful read.
                return first == OperandKind.Register
                    && (second == OperandKind.None || second == OperandKind.Memory)
                    ? ResolveSpecFpuCompare(instruction.Mnemonic)
                    : null;
            case Mnemonic.Ficom:
            case Mnemonic.Ficomp:
                // Slice 17: integer compares (mem-only) through the same
                // shared body with `ReadFpuInteger` as the source.
                return first == OperandKind.Memory && second == OperandKind.None
                    ? ResolveSpecFpuIntegerCompare(instruction.Mnemonic)
                    : null;
            case Mnemonic.Fchs:
            case Mnemonic.Fabs:
            case Mnemonic.Ftst:
                // Slice 17: ST(0) sign/test ops, pure register/state.
                return first == OperandKind.None && second == OperandKind.None
                    ? ResolveSpecFpuUnary(instruction.Mnemonic)
                    : null;
            case Mnemonic.Ffree:
            case Mnemonic.Ffreep:
            case Mnemonic.Fdecstp:
            case Mnemonic.Fincstp:
            case Mnemonic.Fnclex:
            case Mnemonic.Fninit:
                // Slice 17: tag/TOP/exception-control ops, verbatim
                // one-liners with no operands beyond an optional ST(i).
                return ResolveSpecFpuControl(instruction.Mnemonic, first, second);
            case Mnemonic.Movaps:
            case Mnemonic.Movups:
                // Slice 18: packed 128-bit moves through the same
                // `ReadXmmOperand`/`WriteXmmOperand` helpers (reg,reg /
                // reg,mem / mem,reg — reads precede writes, widths ride
                // the cached member exactly like the interpreter).
                return (first == OperandKind.Register || first == OperandKind.Memory)
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? ResolveSpecSseMove(instruction.Mnemonic)
                    : null;
            case Mnemonic.Movss:
                // Slice 18: scalar low-element move (F3 mandatory prefix —
                // covered by the `IsMandatoryF3Scalar` gate like slice 12).
                return (first == OperandKind.Register || first == OperandKind.Memory)
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecMovssHandler
                    : null;
            case Mnemonic.Movlps:
            case Mnemonic.Movhps:
                // Slice 18: low/high 64-bit moves, verbatim mirrors
                // (either direction; reg,reg falls through untouched on
                // both paths exactly like the interpreter).
                return (first == OperandKind.Register || first == OperandKind.Memory)
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? ResolveSpecSseMove64(instruction.Mnemonic)
                    : null;
            case Mnemonic.Movhlps:
            case Mnemonic.Movlhps:
            case Mnemonic.Movmskps:
                // Slice 18: reg,reg shuffles/mask — pure register data.
                return first == OperandKind.Register && second == OperandKind.Register
                    ? ResolveSpecSseShuffle(instruction.Mnemonic)
                    : null;
            case Mnemonic.Movntps:
                // Slice 18: non-temporal store (the interpreter ignores
                // the hint — plain 128-bit store through the same helper).
                return first == OperandKind.Memory && second == OperandKind.Register
                    ? SpecMovntpsHandler
                    : null;
            case Mnemonic.Sqrtps:
            case Mnemonic.Sqrtss:
            case Mnemonic.Rsqrtps:
            case Mnemonic.Rsqrtss:
            case Mnemonic.Rcpps:
            case Mnemonic.Rcpss:
                // Slice 19: SSE square-root/reciprocal (packed + scalar)
                // through the shared `ExecuteSseUnary` helper — plain
                // host-float ops, no MXCSR/exception behavior anywhere in
                // the interpreter. Scalar ss forms carry the F3 mandatory
                // prefix (gate below, like slices 12/18).
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecSseUnaryHandler
                    : null;
            case Mnemonic.Addps:
            case Mnemonic.Subps:
            case Mnemonic.Mulps:
            case Mnemonic.Divps:
            case Mnemonic.Minps:
            case Mnemonic.Maxps:
                // Slice 19: packed SSE arithmetic through the shared
                // `ExecuteSseArithmetic` helper (the scalar sisters were
                // slice 12; the helper's packed loop is equally verbatim).
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecSsePackedArithmeticHandler
                    : null;
            case Mnemonic.Andps:
            case Mnemonic.Andnps:
            case Mnemonic.Orps:
            case Mnemonic.Xorps:
                // Slice 19: packed SSE logicals through `ExecuteSseLogical`.
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecSseLogicalHandler
                    : null;
            case Mnemonic.Cmpps:
            case Mnemonic.Cmpss:
                // Slice 19: packed/scalar SSE compares (Cmpss carries F3).
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecSseCompareHandler
                    : null;
            case Mnemonic.Comiss:
            case Mnemonic.Ucomiss:
                // Slice 19: scalar ordered/unordered compares into EFLAGS
                // through `ExecuteSseCompareScalar` (flags only, no control
                // flow — the mirror is exact by shared code).
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecSseCompareScalarHandler
                    : null;
            case Mnemonic.Cbw:
            case Mnemonic.Cdq:
                // Slice 20: sign-extension, no operands — the operand-size
                // branch lives in the shared body, read from the cached
                // member exactly like the interpreter.
                return first == OperandKind.None && second == OperandKind.None
                    ? ResolveSpecSignExtend(instruction.Mnemonic)
                    : null;
            case Mnemonic.Pushf:
                // Slice 20: flag push (Reserved1 forced, like the
                // interpreter); operand size rides the member.
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecPushfHandler
                    : null;
            case Mnemonic.Popf:
                // Slice 20: flag pop (IF changes take effect at the
                // existing IRQ-check boundaries on both paths — the mirror
                // is exact by shared code shape).
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecPopfHandler
                    : null;
            case Mnemonic.Setcc:
                // Slice 20: condition to byte — the condition rides the
                // cached member, evaluated by the shared helper.
                return (first == OperandKind.Register || first == OperandKind.Memory)
                    && second == OperandKind.None
                    ? SpecSetccHandler
                    : null;
            case Mnemonic.Cmovcc:
                // Slice 20: conditional move through the shared condition
                // helper (read-before-write, flags only).
                return first == OperandKind.Register
                    && (second == OperandKind.Register || second == OperandKind.Memory)
                    ? SpecCmovccHandler
                    : null;
            case Mnemonic.Rdtsc:
                // Slice 20: timestamp read — both sides advance TSC
                // identically per executed step, so the mirror is exact.
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecRdtscHandler
                    : null;
            case Mnemonic.Cli:
                // Slice 6 (§168): IF clear — operandless by decode; the
                // block keeps flowing (slice 5) and the body is the
                // verbatim interpreter case.
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecCliHandler
                    : null;
            case Mnemonic.Sti:
                // Slice 6 (§168): IF set — same shape as Cli.
                return first == OperandKind.None && second == OperandKind.None
                    ? SpecStiHandler
                    : null;
            case Mnemonic.Pop:
                return first == OperandKind.Register && second == OperandKind.None
                    ? ResolveSpecPop(size)
                    : null;
            case Mnemonic.Jcc:
                return first == OperandKind.Immediate && second == OperandKind.None
                    ? ResolveSpecJcc(instruction.Condition)
                    : null;
            case Mnemonic.Jmp:
                return second == OperandKind.None
                    ? ResolveSpecJmp(first)
                    : null;
            case Mnemonic.Call:
                return second == OperandKind.None
                    ? ResolveSpecCall(first)
                    : null;
            case Mnemonic.Ret:
                return ResolveSpecRet(first, second);
            case Mnemonic.Shl:
            case Mnemonic.Shr:
            case Mnemonic.Sar:
            case Mnemonic.Rol:
            case Mnemonic.Ror:
            case Mnemonic.Rcl:
            case Mnemonic.Rcr:
                return ResolveSpecShift(instruction.Mnemonic, first, second, size);
            case Mnemonic.Movzx:
            case Mnemonic.Movsx:
                return ResolveSpecMoveExtend(
                    instruction.Mnemonic, first, second, size, instruction.Operand2.Size);
            case Mnemonic.Movq:
                return ResolveSpecMovq(first, second);
            case Mnemonic.Movntq:
                return first == OperandKind.Memory && second == OperandKind.Register
                    ? SpecMovntqMemRegHandler
                    : null;
            case Mnemonic.Prefetch:
                // Slice 11: the prefetch hint decodes with a memory operand
                // it never touches (the interpreter breaks without reading
                // it), so the handler is EIP-advance only — and like the
                // fences it may continue its block.
                return first == OperandKind.Memory && second == OperandKind.None
                    ? SpecPrefetchHandler
                    : null;
            case Mnemonic.Sfence:
            case Mnemonic.Lfence:
            case Mnemonic.Mfence:
                // Slice 11: ordering fences are interpreter nops
                // (`SseTests.Prefetch_IsNoOp` pins the family); the handler
                // only advances EIP and the shapes continue their block.
                return first == OperandKind.None && second == OperandKind.None
                    ? ResolveSpecFence(instruction.Mnemonic)
                    : null;
            case Mnemonic.Addss:
            case Mnemonic.Subss:
            case Mnemonic.Mulss:
            case Mnemonic.Divss:
            case Mnemonic.Minss:
            case Mnemonic.Maxss:
                // Slice 12: scalar SSE arithmetic is one verbatim helper
                // call (`ExecuteSseArithmetic` — plain host-float ops, no
                // MXCSR/exception behavior in the interpreter); the handler
                // only avoids the dispatch switch and the shapes continue
                // their block. Packed forms keep the interpreter.
                return ResolveSpecSseScalar(instruction.Mnemonic, first, second);
            case Mnemonic.Cvtss2Si:
            case Mnemonic.Cvttss2Si:
                // Slice 12: scalar float-to-int through the same
                // `ConvertFloatToInt32` helper (truncate flag baked per
                // mnemonic exactly like the interpreter).
                return ResolveSpecSseCvt(first, second, instruction.Mnemonic);
            case Mnemonic.Fld:
                // Slice 13: x87 load/store/integer-load/store + ST(0)
                // arithmetic. All bodies below mirror their interpreter
                // case verbatim (read before any FPU mutation, EIP only
                // after completion); widths ride the operand Size exactly
                // like `ReadFpuReal`/`WriteFpuReal`/`ReadFpuInteger`/
                // `WriteFpuInteger`. Slice 14 adds the register form
                // (Fld ST(i) pushes a copy — no fault possible).
                return second == OperandKind.None
                    && (first == OperandKind.Memory || first == OperandKind.Register)
                    ? (first == OperandKind.Memory ? SpecFldMemHandler : SpecFldRegHandler)
                    : null;
            case Mnemonic.Fst:
                return second == OperandKind.None
                    && (first == OperandKind.Memory || first == OperandKind.Register)
                    ? SpecFstHandler
                    : null;
            case Mnemonic.Fstp:
                return second == OperandKind.None
                    ? SpecFstpHandler
                    : null;
            case Mnemonic.Fild:
                return first == OperandKind.Memory && second == OperandKind.None
                    ? SpecFildMemHandler
                    : null;
            case Mnemonic.Fistp:
                return first == OperandKind.Memory && second == OperandKind.None
                    ? SpecFistpMemHandler
                    : null;
            case Mnemonic.Fadd:
                return ResolveSpecFpuAlu(Mnemonic.Fadd, first, second);
            case Mnemonic.Fmul:
                return ResolveSpecFpuAlu(Mnemonic.Fmul, first, second);
            case Mnemonic.Fsub:
                return ResolveSpecFpuAlu(Mnemonic.Fsub, first, second);
            case Mnemonic.Fsubr:
                return ResolveSpecFpuAlu(Mnemonic.Fsubr, first, second);
            case Mnemonic.Fdiv:
                return ResolveSpecFpuAlu(Mnemonic.Fdiv, first, second);
            case Mnemonic.Fdivr:
                return ResolveSpecFpuAlu(Mnemonic.Fdivr, first, second);
            case Mnemonic.Faddp:
            case Mnemonic.Fmulp:
            case Mnemonic.Fsubp:
            case Mnemonic.Fsubrp:
            case Mnemonic.Fdivp:
            case Mnemonic.Fdivrp:
                // Slice 14: x87 pop-arithmetic (ST(i),ST(0) -> ST(i), pop).
                // Verbatim interpreter pop-form body; no memory, no fault.
                return first == OperandKind.Register && second == OperandKind.Register
                    ? ResolveSpecFpuPop(instruction.Mnemonic)
                    : null;
            case Mnemonic.Fxch:
                // Slice 14: register exchange, no flags, no fault.
                return first == OperandKind.Register && second == OperandKind.None
                    ? SpecFxchHandler
                    : null;
            case Mnemonic.Fld1:
            case Mnemonic.Fldl2T:
            case Mnemonic.Fldl2E:
            case Mnemonic.Fldpi:
            case Mnemonic.Fldlg2:
            case Mnemonic.Fldln2:
            case Mnemonic.Fldz:
                // Slice 14: constant loads push a fixed Fpu80 — verbatim
                // interpreter cases, no operand, no fault.
                return first == OperandKind.None && second == OperandKind.None
                    ? ResolveSpecFldConst(instruction.Mnemonic)
                    : null;
            default:
                return null;
        }
    }

    private static SpecHandler? ResolveSpecMov(OperandKind first, OperandKind second, int size)
    {
        if (first == OperandKind.Register && second == OperandKind.Register)
        {
            return size switch
            {
                8 => SpecMovRegReg8Handler,
                16 => SpecMovRegReg16Handler,
                _ => size == 32 ? SpecMovRegReg32Handler : null,
            };
        }

        if (first == OperandKind.Register && second == OperandKind.Immediate)
        {
            return size switch
            {
                8 => SpecMovRegImm8Handler,
                16 => SpecMovRegImm16Handler,
                _ => size == 32 ? SpecMovRegImm32Handler : null,
            };
        }

        if (first == OperandKind.Register && second == OperandKind.Memory)
        {
            return size switch
            {
                8 => SpecMovRegMem8Handler,
                16 => SpecMovRegMem16Handler,
                _ => size == 32 ? SpecMovRegMem32Handler : null,
            };
        }

        if (first == OperandKind.Memory && second == OperandKind.Register)
        {
            return size switch
            {
                8 => SpecMovMemReg8Handler,
                16 => SpecMovMemReg16Handler,
                _ => size == 32 ? SpecMovMemReg32Handler : null,
            };
        }

        if (first == OperandKind.Memory && second == OperandKind.Immediate)
        {
            return size switch
            {
                8 => SpecMovMemImm8Handler,
                16 => SpecMovMemImm16Handler,
                _ => size == 32 ? SpecMovMemImm32Handler : null,
            };
        }

        return null;
    }

    private static SpecHandler? ResolveSpecAlu(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        bool regReg = first == OperandKind.Register && second == OperandKind.Register;
        bool regImm = first == OperandKind.Register && second == OperandKind.Immediate;
        if (!regReg && !regImm)
        {
            return null;
        }

        return (mnemonic, regReg, size) switch
        {
            (Mnemonic.Add, true, 8) => SpecAddRegReg8Handler,
            (Mnemonic.Add, true, 16) => SpecAddRegReg16Handler,
            (Mnemonic.Add, true, 32) => SpecAddRegReg32Handler,
            (Mnemonic.Add, false, 8) => SpecAddRegImm8Handler,
            (Mnemonic.Add, false, 16) => SpecAddRegImm16Handler,
            (Mnemonic.Add, false, 32) => SpecAddRegImm32Handler,
            (Mnemonic.Sub, true, 8) => SpecSubRegReg8Handler,
            (Mnemonic.Sub, true, 16) => SpecSubRegReg16Handler,
            (Mnemonic.Sub, true, 32) => SpecSubRegReg32Handler,
            (Mnemonic.Sub, false, 8) => SpecSubRegImm8Handler,
            (Mnemonic.Sub, false, 16) => SpecSubRegImm16Handler,
            (Mnemonic.Sub, false, 32) => SpecSubRegImm32Handler,
            (Mnemonic.And, true, 8) => SpecAndRegReg8Handler,
            (Mnemonic.And, true, 16) => SpecAndRegReg16Handler,
            (Mnemonic.And, true, 32) => SpecAndRegReg32Handler,
            (Mnemonic.And, false, 8) => SpecAndRegImm8Handler,
            (Mnemonic.And, false, 16) => SpecAndRegImm16Handler,
            (Mnemonic.And, false, 32) => SpecAndRegImm32Handler,
            (Mnemonic.Or, true, 8) => SpecOrRegReg8Handler,
            (Mnemonic.Or, true, 16) => SpecOrRegReg16Handler,
            (Mnemonic.Or, true, 32) => SpecOrRegReg32Handler,
            (Mnemonic.Or, false, 8) => SpecOrRegImm8Handler,
            (Mnemonic.Or, false, 16) => SpecOrRegImm16Handler,
            (Mnemonic.Or, false, 32) => SpecOrRegImm32Handler,
            (Mnemonic.Xor, true, 8) => SpecXorRegReg8Handler,
            (Mnemonic.Xor, true, 16) => SpecXorRegReg16Handler,
            (Mnemonic.Xor, true, 32) => SpecXorRegReg32Handler,
            (Mnemonic.Xor, false, 8) => SpecXorRegImm8Handler,
            (Mnemonic.Xor, false, 16) => SpecXorRegImm16Handler,
            (Mnemonic.Xor, false, 32) => SpecXorRegImm32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecTest(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        bool regReg = first == OperandKind.Register && second == OperandKind.Register;
        bool regImm = first == OperandKind.Register && second == OperandKind.Immediate;
        if (!regReg && !regImm)
        {
            return null;
        }

        return (mnemonic, regReg, size) switch
        {
            (Mnemonic.Cmp, true, 8) => SpecCmpRegReg8Handler,
            (Mnemonic.Cmp, true, 16) => SpecCmpRegReg16Handler,
            (Mnemonic.Cmp, true, 32) => SpecCmpRegReg32Handler,
            (Mnemonic.Cmp, false, 8) => SpecCmpRegImm8Handler,
            (Mnemonic.Cmp, false, 16) => SpecCmpRegImm16Handler,
            (Mnemonic.Cmp, false, 32) => SpecCmpRegImm32Handler,
            (Mnemonic.Test, true, 8) => SpecTestRegReg8Handler,
            (Mnemonic.Test, true, 16) => SpecTestRegReg16Handler,
            (Mnemonic.Test, true, 32) => SpecTestRegReg32Handler,
            (Mnemonic.Test, false, 8) => SpecTestRegImm8Handler,
            (Mnemonic.Test, false, 16) => SpecTestRegImm16Handler,
            (Mnemonic.Test, false, 32) => SpecTestRegImm32Handler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves ALU shapes with a memory operand (slice 3): register/memory,
    /// memory/register and memory/immediate for the carry-free ALU and
    /// compare/test mnemonics. Adc/Sbb stay unresolved (carry live).
    /// </summary>
    private static SpecHandler? ResolveSpecAluMem(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        int pair;
        if (first == OperandKind.Register && second == OperandKind.Memory)
        {
            pair = 0;
        }
        else if (first == OperandKind.Memory && second == OperandKind.Register)
        {
            pair = 1;
        }
        else if (first == OperandKind.Memory && second == OperandKind.Immediate)
        {
            pair = 2;
        }
        else
        {
            return null;
        }

        return (mnemonic, pair, size) switch
        {
            (Mnemonic.Add, 0, 8) => SpecAddRegMem8Handler,
            (Mnemonic.Add, 0, 16) => SpecAddRegMem16Handler,
            (Mnemonic.Add, 0, 32) => SpecAddRegMem32Handler,
            (Mnemonic.Add, 1, 8) => SpecAddMemReg8Handler,
            (Mnemonic.Add, 1, 16) => SpecAddMemReg16Handler,
            (Mnemonic.Add, 1, 32) => SpecAddMemReg32Handler,
            (Mnemonic.Add, 2, 8) => SpecAddMemImm8Handler,
            (Mnemonic.Add, 2, 16) => SpecAddMemImm16Handler,
            (Mnemonic.Add, 2, 32) => SpecAddMemImm32Handler,
            (Mnemonic.Sub, 0, 8) => SpecSubRegMem8Handler,
            (Mnemonic.Sub, 0, 16) => SpecSubRegMem16Handler,
            (Mnemonic.Sub, 0, 32) => SpecSubRegMem32Handler,
            (Mnemonic.Sub, 1, 8) => SpecSubMemReg8Handler,
            (Mnemonic.Sub, 1, 16) => SpecSubMemReg16Handler,
            (Mnemonic.Sub, 1, 32) => SpecSubMemReg32Handler,
            (Mnemonic.Sub, 2, 8) => SpecSubMemImm8Handler,
            (Mnemonic.Sub, 2, 16) => SpecSubMemImm16Handler,
            (Mnemonic.Sub, 2, 32) => SpecSubMemImm32Handler,
            (Mnemonic.And, 0, 8) => SpecAndRegMem8Handler,
            (Mnemonic.And, 0, 16) => SpecAndRegMem16Handler,
            (Mnemonic.And, 0, 32) => SpecAndRegMem32Handler,
            (Mnemonic.And, 1, 8) => SpecAndMemReg8Handler,
            (Mnemonic.And, 1, 16) => SpecAndMemReg16Handler,
            (Mnemonic.And, 1, 32) => SpecAndMemReg32Handler,
            (Mnemonic.And, 2, 8) => SpecAndMemImm8Handler,
            (Mnemonic.And, 2, 16) => SpecAndMemImm16Handler,
            (Mnemonic.And, 2, 32) => SpecAndMemImm32Handler,
            (Mnemonic.Or, 0, 8) => SpecOrRegMem8Handler,
            (Mnemonic.Or, 0, 16) => SpecOrRegMem16Handler,
            (Mnemonic.Or, 0, 32) => SpecOrRegMem32Handler,
            (Mnemonic.Or, 1, 8) => SpecOrMemReg8Handler,
            (Mnemonic.Or, 1, 16) => SpecOrMemReg16Handler,
            (Mnemonic.Or, 1, 32) => SpecOrMemReg32Handler,
            (Mnemonic.Or, 2, 8) => SpecOrMemImm8Handler,
            (Mnemonic.Or, 2, 16) => SpecOrMemImm16Handler,
            (Mnemonic.Or, 2, 32) => SpecOrMemImm32Handler,
            (Mnemonic.Xor, 0, 8) => SpecXorRegMem8Handler,
            (Mnemonic.Xor, 0, 16) => SpecXorRegMem16Handler,
            (Mnemonic.Xor, 0, 32) => SpecXorRegMem32Handler,
            (Mnemonic.Xor, 1, 8) => SpecXorMemReg8Handler,
            (Mnemonic.Xor, 1, 16) => SpecXorMemReg16Handler,
            (Mnemonic.Xor, 1, 32) => SpecXorMemReg32Handler,
            (Mnemonic.Xor, 2, 8) => SpecXorMemImm8Handler,
            (Mnemonic.Xor, 2, 16) => SpecXorMemImm16Handler,
            (Mnemonic.Xor, 2, 32) => SpecXorMemImm32Handler,
            (Mnemonic.Cmp, 0, 8) => SpecCmpRegMem8Handler,
            (Mnemonic.Cmp, 0, 16) => SpecCmpRegMem16Handler,
            (Mnemonic.Cmp, 0, 32) => SpecCmpRegMem32Handler,
            (Mnemonic.Cmp, 1, 8) => SpecCmpMemReg8Handler,
            (Mnemonic.Cmp, 1, 16) => SpecCmpMemReg16Handler,
            (Mnemonic.Cmp, 1, 32) => SpecCmpMemReg32Handler,
            (Mnemonic.Cmp, 2, 8) => SpecCmpMemImm8Handler,
            (Mnemonic.Cmp, 2, 16) => SpecCmpMemImm16Handler,
            (Mnemonic.Cmp, 2, 32) => SpecCmpMemImm32Handler,
            (Mnemonic.Test, 0, 8) => SpecTestRegMem8Handler,
            (Mnemonic.Test, 0, 16) => SpecTestRegMem16Handler,
            (Mnemonic.Test, 0, 32) => SpecTestRegMem32Handler,
            (Mnemonic.Test, 1, 8) => SpecTestMemReg8Handler,
            (Mnemonic.Test, 1, 16) => SpecTestMemReg16Handler,
            (Mnemonic.Test, 1, 32) => SpecTestMemReg32Handler,
            (Mnemonic.Test, 2, 8) => SpecTestMemImm8Handler,
            (Mnemonic.Test, 2, 16) => SpecTestMemImm16Handler,
            (Mnemonic.Test, 2, 32) => SpecTestMemImm32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecIncDec(bool isInc, OperandKind first, int size)
    {
        return (isInc, first, size) switch
        {
            (true, OperandKind.Register, 8) => SpecIncReg8Handler,
            (true, OperandKind.Register, 16) => SpecIncReg16Handler,
            (true, OperandKind.Register, 32) => SpecIncReg32Handler,
            (true, OperandKind.Memory, 8) => SpecIncMem8Handler,
            (true, OperandKind.Memory, 16) => SpecIncMem16Handler,
            (true, OperandKind.Memory, 32) => SpecIncMem32Handler,
            (false, OperandKind.Register, 8) => SpecDecReg8Handler,
            (false, OperandKind.Register, 16) => SpecDecReg16Handler,
            (false, OperandKind.Register, 32) => SpecDecReg32Handler,
            (false, OperandKind.Memory, 8) => SpecDecMem8Handler,
            (false, OperandKind.Memory, 16) => SpecDecMem16Handler,
            (false, OperandKind.Memory, 32) => SpecDecMem32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecLea(int size)
    {
        return size switch
        {
            16 => SpecLeaRegMem16Handler,
            32 => SpecLeaRegMem32Handler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecPush(OperandKind first, int size)
    {
        return (first, size) switch
        {
            (OperandKind.Register, 16) => SpecPushReg16Handler,
            (OperandKind.Register, 32) => SpecPushReg32Handler,
            (OperandKind.Immediate, 16) => SpecPushImm16Handler,
            (OperandKind.Immediate, 32) => SpecPushImm32Handler,
            (OperandKind.Memory, 16) => SpecPushMemHandler,
            (OperandKind.Memory, 32) => SpecPushMemHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecPop(int size)
    {
        return size switch
        {
            16 => SpecPopReg16Handler,
            32 => SpecPopReg32Handler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves near jumps, calls and returns (slice 7). Like Jcc they
    /// always terminate their block, so the handlers mirror the interpreter
    /// bodies verbatim (EIP assignment, return-address push, stack adjust)
    /// and the cursor completion ends the block. Far forms keep the
    /// interpreter. A faulting indirect target throws before any EIP write,
    /// preserving precise fault EIP exactly like the interpreter.
    /// </summary>
    private static SpecHandler? ResolveSpecJmp(OperandKind first)
    {
        return first switch
        {
            OperandKind.Immediate => SpecJmpImmHandler,
            OperandKind.Register => SpecJmpRegHandler,
            OperandKind.Memory => SpecJmpMemHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecCall(OperandKind first)
    {
        return first switch
        {
            OperandKind.Immediate => SpecCallImmHandler,
            OperandKind.Register => SpecCallRegHandler,
            OperandKind.Memory => SpecCallMemHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecRet(OperandKind first, OperandKind second)
    {
        return (first, second) switch
        {
            (OperandKind.None, OperandKind.None) => SpecRetHandler,
            (OperandKind.Immediate, OperandKind.None) => SpecRetImmHandler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves shift/rotate shapes (slices 8 + 15): Shl/Shr/Sar/Rol/Ror
    /// with register or memory destinations, counted by immediate
    /// (slice 8) or CL (slice 15 — the decoder stamps D2/D3 counts as
    /// Register index 1, verified in `DecodeShiftRm`). CL-count bodies
    /// call the same `ExecuteShift` helper the interpreter uses, so
    /// count masking and flag behavior match exactly. Rcl/Rcr forms keep
    /// the interpreter.
    /// </summary>
    private static SpecHandler? ResolveSpecShift(Mnemonic mnemonic, OperandKind first, OperandKind second, int size)
    {
        // Slice 20: Rcl/Rcr (carry-through rotates, excluded from slice 8
        // for scope, not safety) take a shared `ExecuteShift` body for
        // every count kind — the helper threads CF exactly like the
        // interpreter on both paths.
        if (mnemonic is Mnemonic.Rcl or Mnemonic.Rcr)
        {
            if ((first != OperandKind.Register && first != OperandKind.Memory)
                || (second != OperandKind.Immediate && second != OperandKind.Register))
            {
                return null;
            }

            return mnemonic == Mnemonic.Rcl ? SpecRclHandler : SpecRcrHandler;
        }

        if (second == OperandKind.Register)
        {
            // Slice 15: r,CL and m,CL. One shared body per mnemonic reads
            // the size from the cached member at serve time, exactly like
            // the interpreter — no per-size split needed.
            if (first != OperandKind.Register && first != OperandKind.Memory)
            {
                return null;
            }

            return mnemonic switch
            {
                Mnemonic.Shl => SpecShlCountClHandler,
                Mnemonic.Shr => SpecShrCountClHandler,
                Mnemonic.Sar => SpecSarCountClHandler,
                Mnemonic.Rol => SpecRolCountClHandler,
                Mnemonic.Ror => SpecRorCountClHandler,
                _ => null,
            };
        }

        int pair;
        if (first == OperandKind.Register && second == OperandKind.Immediate)
        {
            pair = 0;
        }
        else if (first == OperandKind.Memory && second == OperandKind.Immediate)
        {
            pair = 1;
        }
        else
        {
            return null;
        }

        return (mnemonic, pair, size) switch
        {
            (Mnemonic.Shl, 0, 8) => SpecShlRegImm8Handler,
            (Mnemonic.Shl, 0, 16) => SpecShlRegImm16Handler,
            (Mnemonic.Shl, 0, 32) => SpecShlRegImm32Handler,
            (Mnemonic.Shl, 1, 8) => SpecShlMemImm8Handler,
            (Mnemonic.Shl, 1, 16) => SpecShlMemImm16Handler,
            (Mnemonic.Shl, 1, 32) => SpecShlMemImm32Handler,
            (Mnemonic.Shr, 0, 8) => SpecShrRegImm8Handler,
            (Mnemonic.Shr, 0, 16) => SpecShrRegImm16Handler,
            (Mnemonic.Shr, 0, 32) => SpecShrRegImm32Handler,
            (Mnemonic.Shr, 1, 8) => SpecShrMemImm8Handler,
            (Mnemonic.Shr, 1, 16) => SpecShrMemImm16Handler,
            (Mnemonic.Shr, 1, 32) => SpecShrMemImm32Handler,
            (Mnemonic.Sar, 0, 8) => SpecSarRegImm8Handler,
            (Mnemonic.Sar, 0, 16) => SpecSarRegImm16Handler,
            (Mnemonic.Sar, 0, 32) => SpecSarRegImm32Handler,
            (Mnemonic.Sar, 1, 8) => SpecSarMemImm8Handler,
            (Mnemonic.Sar, 1, 16) => SpecSarMemImm16Handler,
            (Mnemonic.Sar, 1, 32) => SpecSarMemImm32Handler,
            (Mnemonic.Rol, 0, 8) => SpecRolRegImm8Handler,
            (Mnemonic.Rol, 0, 16) => SpecRolRegImm16Handler,
            (Mnemonic.Rol, 0, 32) => SpecRolRegImm32Handler,
            (Mnemonic.Rol, 1, 8) => SpecRolMemImm8Handler,
            (Mnemonic.Rol, 1, 16) => SpecRolMemImm16Handler,
            (Mnemonic.Rol, 1, 32) => SpecRolMemImm32Handler,
            (Mnemonic.Ror, 0, 8) => SpecRorRegImm8Handler,
            (Mnemonic.Ror, 0, 16) => SpecRorRegImm16Handler,
            (Mnemonic.Ror, 0, 32) => SpecRorRegImm32Handler,
            (Mnemonic.Ror, 1, 8) => SpecRorMemImm8Handler,
            (Mnemonic.Ror, 1, 16) => SpecRorMemImm16Handler,
            (Mnemonic.Ror, 1, 32) => SpecRorMemImm32Handler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves zero/sign-extension shapes (slice 8): Movzx/Movsx with
    /// register or memory sources into 16/32-bit registers. The source
    /// width selects the mask/sign rule exactly like
    /// `ExecuteMoveExtend`.
    /// </summary>
    private static SpecHandler? ResolveSpecMoveExtend(
        Mnemonic mnemonic, OperandKind first, OperandKind second, int size, int sourceSize)
    {
        int pair;
        if (first == OperandKind.Register && second == OperandKind.Register)
        {
            pair = 0;
        }
        else if (first == OperandKind.Register && second == OperandKind.Memory)
        {
            pair = 1;
        }
        else
        {
            return null;
        }

        return (mnemonic, pair, size, sourceSize) switch
        {
            (Mnemonic.Movzx, 0, 16, 8) => SpecMovzxRegReg168Handler,
            (Mnemonic.Movzx, 0, 32, 8) => SpecMovzxRegReg328Handler,
            (Mnemonic.Movzx, 0, 16, 16) => SpecMovzxRegReg1616Handler,
            (Mnemonic.Movzx, 0, 32, 16) => SpecMovzxRegReg3216Handler,
            (Mnemonic.Movzx, 1, 16, 8) => SpecMovzxRegMem168Handler,
            (Mnemonic.Movzx, 1, 32, 8) => SpecMovzxRegMem328Handler,
            (Mnemonic.Movzx, 1, 16, 16) => SpecMovzxRegMem1616Handler,
            (Mnemonic.Movzx, 1, 32, 16) => SpecMovzxRegMem3216Handler,
            (Mnemonic.Movsx, 0, 16, 8) => SpecMovsxRegReg168Handler,
            (Mnemonic.Movsx, 0, 32, 8) => SpecMovsxRegReg328Handler,
            (Mnemonic.Movsx, 0, 16, 16) => SpecMovsxRegReg1616Handler,
            (Mnemonic.Movsx, 0, 32, 16) => SpecMovsxRegReg3216Handler,
            (Mnemonic.Movsx, 1, 16, 8) => SpecMovsxRegMem168Handler,
            (Mnemonic.Movsx, 1, 32, 8) => SpecMovsxRegMem328Handler,
            (Mnemonic.Movsx, 1, 16, 16) => SpecMovsxRegMem1616Handler,
            (Mnemonic.Movsx, 1, 32, 16) => SpecMovsxRegMem3216Handler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves MMX 64-bit move shapes (slice 10): Movq reg,reg/reg,mem/
    /// mem,reg and Movntq mem,reg. Pure data moves through the x87-aliased
    /// MMX file (no flags, no control flow), so unlike Jcc/Jmp they may
    /// continue their block — <see cref="DecodedBlockCache.Terminates"/>
    /// whitelists both mnemonics. Bodies mirror the interpreter through
    /// the same <c>ReadMmxOperand</c>/<c>WriteMmxOperand</c>/
    /// <c>WriteMemory64</c> helpers with EIP advanced only after completion.
    /// </summary>
    private static SpecHandler? ResolveSpecMovq(OperandKind first, OperandKind second)
    {
        return (first, second) switch
        {
            (OperandKind.Register, OperandKind.Register) => SpecMovqRegRegHandler,
            (OperandKind.Register, OperandKind.Memory) => SpecMovqRegMemHandler,
            (OperandKind.Memory, OperandKind.Register) => SpecMovqMemRegHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFence(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Sfence => SpecSfenceHandler,
            Mnemonic.Lfence => SpecLfenceHandler,
            Mnemonic.Mfence => SpecMfenceHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSseScalar(Mnemonic mnemonic, OperandKind first, OperandKind second)
    {
        if (first != OperandKind.Register)
        {
            return null;
        }

        return (mnemonic, second) switch
        {
            (Mnemonic.Addss, OperandKind.Register) => SpecAddssRegRegHandler,
            (Mnemonic.Addss, OperandKind.Memory) => SpecAddssRegMemHandler,
            (Mnemonic.Subss, OperandKind.Register) => SpecSubssRegRegHandler,
            (Mnemonic.Subss, OperandKind.Memory) => SpecSubssRegMemHandler,
            (Mnemonic.Mulss, OperandKind.Register) => SpecMulssRegRegHandler,
            (Mnemonic.Mulss, OperandKind.Memory) => SpecMulssRegMemHandler,
            (Mnemonic.Divss, OperandKind.Register) => SpecDivssRegRegHandler,
            (Mnemonic.Divss, OperandKind.Memory) => SpecDivssRegMemHandler,
            (Mnemonic.Minss, OperandKind.Register) => SpecMinssRegRegHandler,
            (Mnemonic.Minss, OperandKind.Memory) => SpecMinssRegMemHandler,
            (Mnemonic.Maxss, OperandKind.Register) => SpecMaxssRegRegHandler,
            (Mnemonic.Maxss, OperandKind.Memory) => SpecMaxssRegMemHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSseCvt(OperandKind first, OperandKind second, Mnemonic mnemonic)
    {
        return (mnemonic, first, second) switch
        {
            (Mnemonic.Cvtss2Si, OperandKind.Register, OperandKind.Register) => SpecCvtss2SiRegRegHandler,
            (Mnemonic.Cvtss2Si, OperandKind.Register, OperandKind.Memory) => SpecCvtss2SiRegMemHandler,
            (Mnemonic.Cvttss2Si, OperandKind.Register, OperandKind.Register) => SpecCvttss2SiRegRegHandler,
            (Mnemonic.Cvttss2Si, OperandKind.Register, OperandKind.Memory) => SpecCvttss2SiRegMemHandler,
            _ => null,
        };
    }

    /// <summary>
    /// Slice-12 gate helper: the shapes whose 0xF3 byte is a mandatory
    /// opcode prefix (so they decode with Rep==Rep). Shared by
    /// <see cref="ResolveSpec"/> and
    /// <see cref="DecodedBlockCache.Terminates"/>. Slice 18 added Movss;
    /// slice 19 adds the scalar sqrt/rcp/compare (Sqrtss/Rsqrtss/Rcpss/
    /// Cmpss). Packed SSE, string/REP and every other mnemonic excluded.
    /// </summary>
    /// <param name="mnemonic">The mnemonic to test.</param>
    /// <returns>True for the F3-mandatory SSE shapes.</returns>
    internal static bool IsMandatoryF3Scalar(Mnemonic mnemonic)
    {
        return mnemonic is Mnemonic.Addss or Mnemonic.Subss or Mnemonic.Mulss or Mnemonic.Divss
            or Mnemonic.Minss or Mnemonic.Maxss or Mnemonic.Cvtss2Si or Mnemonic.Cvttss2Si
            or Mnemonic.Movss or Mnemonic.Sqrtss or Mnemonic.Rsqrtss or Mnemonic.Rcpss
            or Mnemonic.Cmpss;
    }

    /// <summary>
    /// Resolves x87 ST(0) arithmetic (slice 13): Fadd/Fmul with a register
    /// or memory source. One shared body per mnemonic mirrors the
    /// interpreter's read-read-write sequence for every operand combo, so
    /// both (reg,reg) and (reg,mem) resolve.
    /// </summary>
    private static SpecHandler? ResolveSpecFpuAlu(Mnemonic mnemonic, OperandKind first, OperandKind second)
    {
        if (first != OperandKind.Register
            || (second != OperandKind.Register && second != OperandKind.Memory))
        {
            return null;
        }

        return mnemonic switch
        {
            Mnemonic.Fadd => SpecFaddHandler,
            Mnemonic.Fmul => SpecFmulHandler,
            Mnemonic.Fsub => SpecFsubHandler,
            Mnemonic.Fsubr => SpecFsubrHandler,
            Mnemonic.Fdiv => SpecFdivHandler,
            Mnemonic.Fdivr => SpecFdivrHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuPop(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Faddp => SpecFaddpHandler,
            Mnemonic.Fmulp => SpecFmulpHandler,
            Mnemonic.Fsubp => SpecFsubpHandler,
            Mnemonic.Fsubrp => SpecFsubrpHandler,
            Mnemonic.Fdivp => SpecFdivpHandler,
            Mnemonic.Fdivrp => SpecFdivrpHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFldConst(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Fld1 => SpecFld1Handler,
            Mnemonic.Fldl2T => SpecFldl2THandler,
            Mnemonic.Fldl2E => SpecFldl2EHandler,
            Mnemonic.Fldpi => SpecFldpiHandler,
            Mnemonic.Fldlg2 => SpecFldlg2Handler,
            Mnemonic.Fldln2 => SpecFldln2Handler,
            Mnemonic.Fldz => SpecFldzHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuInteger(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Fiadd => SpecFiaddHandler,
            Mnemonic.Fimul => SpecFimulHandler,
            Mnemonic.Fisub => SpecFisubHandler,
            Mnemonic.Fisubr => SpecFisubrHandler,
            Mnemonic.Fidiv => SpecFidivHandler,
            Mnemonic.Fidivr => SpecFidivrHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuCompare(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Fcom => SpecFcomHandler,
            Mnemonic.Fcomp => SpecFcompHandler,
            Mnemonic.Fucom => SpecFucomHandler,
            Mnemonic.Fucomp => SpecFucompHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuIntegerCompare(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Ficom => SpecFicomHandler,
            Mnemonic.Ficomp => SpecFicompHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuUnary(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Fchs => SpecFchsHandler,
            Mnemonic.Fabs => SpecFabsHandler,
            Mnemonic.Ftst => SpecFtstHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecFpuControl(Mnemonic mnemonic, OperandKind first, OperandKind second)
    {
        return (mnemonic, first, second) switch
        {
            (Mnemonic.Ffree, OperandKind.Register, OperandKind.None) => SpecFfreeHandler,
            (Mnemonic.Ffreep, OperandKind.Register, OperandKind.None) => SpecFfreepHandler,
            (Mnemonic.Fdecstp, OperandKind.None, OperandKind.None) => SpecFdecstpHandler,
            (Mnemonic.Fincstp, OperandKind.None, OperandKind.None) => SpecFincstpHandler,
            (Mnemonic.Fnclex, OperandKind.None, OperandKind.None) => SpecFnclexHandler,
            (Mnemonic.Fninit, OperandKind.None, OperandKind.None) => SpecFninitHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSseMove(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Movaps => SpecMovapsHandler,
            Mnemonic.Movups => SpecMovupsHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSseMove64(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Movlps => SpecMovlpsHandler,
            Mnemonic.Movhps => SpecMovhpsHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSseShuffle(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Movhlps => SpecMovhlpsHandler,
            Mnemonic.Movlhps => SpecMovlhpsHandler,
            Mnemonic.Movmskps => SpecMovmskpsHandler,
            _ => null,
        };
    }

    private static SpecHandler? ResolveSpecSignExtend(Mnemonic mnemonic)
    {
        return mnemonic switch
        {
            Mnemonic.Cbw => SpecCbwHandler,
            Mnemonic.Cdq => SpecCdqHandler,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves near conditional jumps (slice 6). Jcc always terminates its
    /// block, so the handler only sets EIP (taken target or fallthrough) and
    /// the existing cursor completion ends the block; IRQ polling, fault and
    /// idle behavior are untouched. Size does not participate: the target
    /// arithmetic is identical for every operand size.
    /// </summary>
    private static SpecHandler? ResolveSpecJcc(ConditionCode condition)
    {
        return condition switch
        {
            ConditionCode.Overflow => SpecJccOverflowHandler,
            ConditionCode.NotOverflow => SpecJccNotOverflowHandler,
            ConditionCode.Below => SpecJccBelowHandler,
            ConditionCode.AboveOrEqual => SpecJccAboveOrEqualHandler,
            ConditionCode.Equal => SpecJccEqualHandler,
            ConditionCode.NotEqual => SpecJccNotEqualHandler,
            ConditionCode.BelowOrEqual => SpecJccBelowOrEqualHandler,
            ConditionCode.Above => SpecJccAboveHandler,
            ConditionCode.Sign => SpecJccSignHandler,
            ConditionCode.NotSign => SpecJccNotSignHandler,
            ConditionCode.Parity => SpecJccParityHandler,
            ConditionCode.NotParity => SpecJccNotParityHandler,
            ConditionCode.Less => SpecJccLessHandler,
            ConditionCode.GreaterOrEqual => SpecJccGreaterOrEqualHandler,
            ConditionCode.LessOrEqual => SpecJccLessOrEqualHandler,
            ConditionCode.Greater => SpecJccGreaterHandler,
            _ => null,
        };
    }

    private static readonly SpecHandler SpecNopHandler = SpecNop;
    private static readonly SpecHandler SpecMovRegReg8Handler = SpecMovRegReg8;
    private static readonly SpecHandler SpecMovRegReg16Handler = SpecMovRegReg16;
    private static readonly SpecHandler SpecMovRegReg32Handler = SpecMovRegReg32;
    private static readonly SpecHandler SpecMovRegImm8Handler = SpecMovRegImm8;
    private static readonly SpecHandler SpecMovRegImm16Handler = SpecMovRegImm16;
    private static readonly SpecHandler SpecMovRegImm32Handler = SpecMovRegImm32;
    private static readonly SpecHandler SpecMovRegMem8Handler = SpecMovRegMem8;
    private static readonly SpecHandler SpecMovRegMem16Handler = SpecMovRegMem16;
    private static readonly SpecHandler SpecMovRegMem32Handler = SpecMovRegMem32;
    private static readonly SpecHandler SpecMovMemReg8Handler = SpecMovMemReg8;
    private static readonly SpecHandler SpecMovMemReg16Handler = SpecMovMemReg16;
    private static readonly SpecHandler SpecMovMemReg32Handler = SpecMovMemReg32;
    private static readonly SpecHandler SpecAddRegReg8Handler = SpecAddRegReg8;
    private static readonly SpecHandler SpecAddRegReg16Handler = SpecAddRegReg16;
    private static readonly SpecHandler SpecAddRegReg32Handler = SpecAddRegReg32;
    private static readonly SpecHandler SpecAddRegImm8Handler = SpecAddRegImm8;
    private static readonly SpecHandler SpecAddRegImm16Handler = SpecAddRegImm16;
    private static readonly SpecHandler SpecAddRegImm32Handler = SpecAddRegImm32;
    private static readonly SpecHandler SpecSubRegReg8Handler = SpecSubRegReg8;
    private static readonly SpecHandler SpecSubRegReg16Handler = SpecSubRegReg16;
    private static readonly SpecHandler SpecSubRegReg32Handler = SpecSubRegReg32;
    private static readonly SpecHandler SpecSubRegImm8Handler = SpecSubRegImm8;
    private static readonly SpecHandler SpecSubRegImm16Handler = SpecSubRegImm16;
    private static readonly SpecHandler SpecSubRegImm32Handler = SpecSubRegImm32;
    private static readonly SpecHandler SpecAndRegReg8Handler = SpecAndRegReg8;
    private static readonly SpecHandler SpecAndRegReg16Handler = SpecAndRegReg16;
    private static readonly SpecHandler SpecAndRegReg32Handler = SpecAndRegReg32;
    private static readonly SpecHandler SpecAndRegImm8Handler = SpecAndRegImm8;
    private static readonly SpecHandler SpecAndRegImm16Handler = SpecAndRegImm16;
    private static readonly SpecHandler SpecAndRegImm32Handler = SpecAndRegImm32;
    private static readonly SpecHandler SpecOrRegReg8Handler = SpecOrRegReg8;
    private static readonly SpecHandler SpecOrRegReg16Handler = SpecOrRegReg16;
    private static readonly SpecHandler SpecOrRegReg32Handler = SpecOrRegReg32;
    private static readonly SpecHandler SpecOrRegImm8Handler = SpecOrRegImm8;
    private static readonly SpecHandler SpecOrRegImm16Handler = SpecOrRegImm16;
    private static readonly SpecHandler SpecOrRegImm32Handler = SpecOrRegImm32;
    private static readonly SpecHandler SpecXorRegReg8Handler = SpecXorRegReg8;
    private static readonly SpecHandler SpecXorRegReg16Handler = SpecXorRegReg16;
    private static readonly SpecHandler SpecXorRegReg32Handler = SpecXorRegReg32;
    private static readonly SpecHandler SpecXorRegImm8Handler = SpecXorRegImm8;
    private static readonly SpecHandler SpecXorRegImm16Handler = SpecXorRegImm16;
    private static readonly SpecHandler SpecXorRegImm32Handler = SpecXorRegImm32;
    private static readonly SpecHandler SpecCmpRegReg8Handler = SpecCmpRegReg8;
    private static readonly SpecHandler SpecCmpRegReg16Handler = SpecCmpRegReg16;
    private static readonly SpecHandler SpecCmpRegReg32Handler = SpecCmpRegReg32;
    private static readonly SpecHandler SpecCmpRegImm8Handler = SpecCmpRegImm8;
    private static readonly SpecHandler SpecCmpRegImm16Handler = SpecCmpRegImm16;
    private static readonly SpecHandler SpecCmpRegImm32Handler = SpecCmpRegImm32;
    private static readonly SpecHandler SpecTestRegReg8Handler = SpecTestRegReg8;
    private static readonly SpecHandler SpecTestRegReg16Handler = SpecTestRegReg16;
    private static readonly SpecHandler SpecTestRegReg32Handler = SpecTestRegReg32;
    private static readonly SpecHandler SpecTestRegImm8Handler = SpecTestRegImm8;
    private static readonly SpecHandler SpecTestRegImm16Handler = SpecTestRegImm16;
    private static readonly SpecHandler SpecTestRegImm32Handler = SpecTestRegImm32;
    private static readonly SpecHandler SpecIncReg8Handler = SpecIncReg8;
    private static readonly SpecHandler SpecIncReg16Handler = SpecIncReg16;
    private static readonly SpecHandler SpecIncReg32Handler = SpecIncReg32;
    private static readonly SpecHandler SpecDecReg8Handler = SpecDecReg8;
    private static readonly SpecHandler SpecDecReg16Handler = SpecDecReg16;
    private static readonly SpecHandler SpecDecReg32Handler = SpecDecReg32;
    private static readonly SpecHandler SpecIncMem8Handler = SpecIncMem8;
    private static readonly SpecHandler SpecIncMem16Handler = SpecIncMem16;
    private static readonly SpecHandler SpecIncMem32Handler = SpecIncMem32;
    private static readonly SpecHandler SpecDecMem8Handler = SpecDecMem8;
    private static readonly SpecHandler SpecDecMem16Handler = SpecDecMem16;
    private static readonly SpecHandler SpecDecMem32Handler = SpecDecMem32;
    private static readonly SpecHandler SpecLeaRegMem16Handler = SpecLeaRegMem16;
    private static readonly SpecHandler SpecLeaRegMem32Handler = SpecLeaRegMem32;
    private static readonly SpecHandler SpecPushReg16Handler = SpecPushReg16;
    private static readonly SpecHandler SpecPushReg32Handler = SpecPushReg32;
    private static readonly SpecHandler SpecPushImm16Handler = SpecPushImm16;
    private static readonly SpecHandler SpecPushImm32Handler = SpecPushImm32;
    private static readonly SpecHandler SpecPopReg16Handler = SpecPopReg16;
    private static readonly SpecHandler SpecPopReg32Handler = SpecPopReg32;
    private static readonly SpecHandler SpecJccOverflowHandler = SpecJccOverflow;
    private static readonly SpecHandler SpecJccNotOverflowHandler = SpecJccNotOverflow;
    private static readonly SpecHandler SpecJccBelowHandler = SpecJccBelow;
    private static readonly SpecHandler SpecJccAboveOrEqualHandler = SpecJccAboveOrEqual;
    private static readonly SpecHandler SpecJccEqualHandler = SpecJccEqual;
    private static readonly SpecHandler SpecJccNotEqualHandler = SpecJccNotEqual;
    private static readonly SpecHandler SpecJccBelowOrEqualHandler = SpecJccBelowOrEqual;
    private static readonly SpecHandler SpecJccAboveHandler = SpecJccAbove;
    private static readonly SpecHandler SpecJccSignHandler = SpecJccSign;
    private static readonly SpecHandler SpecJccNotSignHandler = SpecJccNotSign;
    private static readonly SpecHandler SpecJccParityHandler = SpecJccParity;
    private static readonly SpecHandler SpecJccNotParityHandler = SpecJccNotParity;
    private static readonly SpecHandler SpecJccLessHandler = SpecJccLess;
    private static readonly SpecHandler SpecJccGreaterOrEqualHandler = SpecJccGreaterOrEqual;
    private static readonly SpecHandler SpecJccLessOrEqualHandler = SpecJccLessOrEqual;
    private static readonly SpecHandler SpecJccGreaterHandler = SpecJccGreater;
    private static readonly SpecHandler SpecJmpImmHandler = SpecJmpImm;
    private static readonly SpecHandler SpecJmpRegHandler = SpecJmpReg;
    private static readonly SpecHandler SpecJmpMemHandler = SpecJmpMem;
    private static readonly SpecHandler SpecCallImmHandler = SpecCallImm;
    private static readonly SpecHandler SpecCallRegHandler = SpecCallReg;
    private static readonly SpecHandler SpecCallMemHandler = SpecCallMem;
    private static readonly SpecHandler SpecRetHandler = SpecRet;
    private static readonly SpecHandler SpecRetImmHandler = SpecRetImm;
    private static readonly SpecHandler SpecShlRegImm8Handler = SpecShlRegImm8;
    private static readonly SpecHandler SpecShlRegImm16Handler = SpecShlRegImm16;
    private static readonly SpecHandler SpecShlRegImm32Handler = SpecShlRegImm32;
    private static readonly SpecHandler SpecShlMemImm8Handler = SpecShlMemImm8;
    private static readonly SpecHandler SpecShlMemImm16Handler = SpecShlMemImm16;
    private static readonly SpecHandler SpecShlMemImm32Handler = SpecShlMemImm32;
    private static readonly SpecHandler SpecShrRegImm8Handler = SpecShrRegImm8;
    private static readonly SpecHandler SpecShrRegImm16Handler = SpecShrRegImm16;
    private static readonly SpecHandler SpecShrRegImm32Handler = SpecShrRegImm32;
    private static readonly SpecHandler SpecShrMemImm8Handler = SpecShrMemImm8;
    private static readonly SpecHandler SpecShrMemImm16Handler = SpecShrMemImm16;
    private static readonly SpecHandler SpecShrMemImm32Handler = SpecShrMemImm32;
    private static readonly SpecHandler SpecSarRegImm8Handler = SpecSarRegImm8;
    private static readonly SpecHandler SpecSarRegImm16Handler = SpecSarRegImm16;
    private static readonly SpecHandler SpecSarRegImm32Handler = SpecSarRegImm32;
    private static readonly SpecHandler SpecSarMemImm8Handler = SpecSarMemImm8;
    private static readonly SpecHandler SpecSarMemImm16Handler = SpecSarMemImm16;
    private static readonly SpecHandler SpecSarMemImm32Handler = SpecSarMemImm32;
    private static readonly SpecHandler SpecRolRegImm8Handler = SpecRolRegImm8;
    private static readonly SpecHandler SpecRolRegImm16Handler = SpecRolRegImm16;
    private static readonly SpecHandler SpecRolRegImm32Handler = SpecRolRegImm32;
    private static readonly SpecHandler SpecRolMemImm8Handler = SpecRolMemImm8;
    private static readonly SpecHandler SpecRolMemImm16Handler = SpecRolMemImm16;
    private static readonly SpecHandler SpecRolMemImm32Handler = SpecRolMemImm32;
    private static readonly SpecHandler SpecRorRegImm8Handler = SpecRorRegImm8;
    private static readonly SpecHandler SpecRorRegImm16Handler = SpecRorRegImm16;
    private static readonly SpecHandler SpecRorRegImm32Handler = SpecRorRegImm32;
    private static readonly SpecHandler SpecRorMemImm8Handler = SpecRorMemImm8;
    private static readonly SpecHandler SpecRorMemImm16Handler = SpecRorMemImm16;
    private static readonly SpecHandler SpecRorMemImm32Handler = SpecRorMemImm32;
    private static readonly SpecHandler SpecMovzxRegReg168Handler = SpecMovzxRegReg168;
    private static readonly SpecHandler SpecMovzxRegReg328Handler = SpecMovzxRegReg328;
    private static readonly SpecHandler SpecMovzxRegReg1616Handler = SpecMovzxRegReg1616;
    private static readonly SpecHandler SpecMovzxRegReg3216Handler = SpecMovzxRegReg3216;
    private static readonly SpecHandler SpecMovzxRegMem168Handler = SpecMovzxRegMem168;
    private static readonly SpecHandler SpecMovzxRegMem328Handler = SpecMovzxRegMem328;
    private static readonly SpecHandler SpecMovzxRegMem1616Handler = SpecMovzxRegMem1616;
    private static readonly SpecHandler SpecMovzxRegMem3216Handler = SpecMovzxRegMem3216;
    private static readonly SpecHandler SpecMovsxRegReg168Handler = SpecMovsxRegReg168;
    private static readonly SpecHandler SpecMovsxRegReg328Handler = SpecMovsxRegReg328;
    private static readonly SpecHandler SpecMovsxRegReg1616Handler = SpecMovsxRegReg1616;
    private static readonly SpecHandler SpecMovsxRegReg3216Handler = SpecMovsxRegReg3216;
    private static readonly SpecHandler SpecMovsxRegMem168Handler = SpecMovsxRegMem168;
    private static readonly SpecHandler SpecMovsxRegMem328Handler = SpecMovsxRegMem328;
    private static readonly SpecHandler SpecMovsxRegMem1616Handler = SpecMovsxRegMem1616;
    private static readonly SpecHandler SpecMovsxRegMem3216Handler = SpecMovsxRegMem3216;
    private static readonly SpecHandler SpecMovqRegRegHandler = SpecMovqRegReg;
    private static readonly SpecHandler SpecMovqRegMemHandler = SpecMovqRegMem;
    private static readonly SpecHandler SpecMovqMemRegHandler = SpecMovqMemReg;
    private static readonly SpecHandler SpecMovntqMemRegHandler = SpecMovntqMemReg;
    private static readonly SpecHandler SpecPrefetchHandler = SpecHintNop;
    private static readonly SpecHandler SpecSfenceHandler = SpecHintNop;
    private static readonly SpecHandler SpecLfenceHandler = SpecHintNop;
    private static readonly SpecHandler SpecMfenceHandler = SpecHintNop;
    private static readonly SpecHandler SpecAddssRegRegHandler = SpecAddssRegReg;
    private static readonly SpecHandler SpecAddssRegMemHandler = SpecAddssRegMem;
    private static readonly SpecHandler SpecSubssRegRegHandler = SpecSubssRegReg;
    private static readonly SpecHandler SpecSubssRegMemHandler = SpecSubssRegMem;
    private static readonly SpecHandler SpecMulssRegRegHandler = SpecMulssRegReg;
    private static readonly SpecHandler SpecMulssRegMemHandler = SpecMulssRegMem;
    private static readonly SpecHandler SpecDivssRegRegHandler = SpecDivssRegReg;
    private static readonly SpecHandler SpecDivssRegMemHandler = SpecDivssRegMem;
    private static readonly SpecHandler SpecMinssRegRegHandler = SpecMinssRegReg;
    private static readonly SpecHandler SpecMinssRegMemHandler = SpecMinssRegMem;
    private static readonly SpecHandler SpecMaxssRegRegHandler = SpecMaxssRegReg;
    private static readonly SpecHandler SpecMaxssRegMemHandler = SpecMaxssRegMem;
    private static readonly SpecHandler SpecCvtss2SiRegRegHandler = SpecCvtss2SiRegReg;
    private static readonly SpecHandler SpecCvtss2SiRegMemHandler = SpecCvtss2SiRegMem;
    private static readonly SpecHandler SpecCvttss2SiRegRegHandler = SpecCvttss2SiRegReg;
    private static readonly SpecHandler SpecCvttss2SiRegMemHandler = SpecCvttss2SiRegMem;
    private static readonly SpecHandler SpecFldMemHandler = SpecFldMem;
    private static readonly SpecHandler SpecMulHandler = SpecMulDivSingle;
    private static readonly SpecHandler SpecDivHandler = SpecMulDivSingle;
    private static readonly SpecHandler SpecImulHandler = SpecImul;
    private static readonly SpecHandler SpecNegHandler = SpecNeg;
    private static readonly SpecHandler SpecNotHandler = SpecNot;
    private static readonly SpecHandler SpecBswapHandler = SpecBswap;
    private static readonly SpecHandler SpecXchgHandler = SpecXchg;
    private static readonly SpecHandler SpecLeaveHandler = SpecLeave;
    private static readonly SpecHandler SpecFiaddHandler = SpecFiadd;
    private static readonly SpecHandler SpecFimulHandler = SpecFimul;
    private static readonly SpecHandler SpecFisubHandler = SpecFisub;
    private static readonly SpecHandler SpecFisubrHandler = SpecFisubr;
    private static readonly SpecHandler SpecFidivHandler = SpecFidiv;
    private static readonly SpecHandler SpecFidivrHandler = SpecFidivr;
    private static readonly SpecHandler SpecFnstswHandler = SpecFnstsw;
    private static readonly SpecHandler SpecFcomHandler = SpecFcom;
    private static readonly SpecHandler SpecFcompHandler = SpecFcomp;
    private static readonly SpecHandler SpecFucomHandler = SpecFucom;
    private static readonly SpecHandler SpecFucompHandler = SpecFucomp;
    private static readonly SpecHandler SpecFicomHandler = SpecFicom;
    private static readonly SpecHandler SpecFicompHandler = SpecFicomp;
    private static readonly SpecHandler SpecFchsHandler = SpecFchs;
    private static readonly SpecHandler SpecFabsHandler = SpecFabs;
    private static readonly SpecHandler SpecFtstHandler = SpecFtst;
    private static readonly SpecHandler SpecFfreeHandler = SpecFfree;
    private static readonly SpecHandler SpecFfreepHandler = SpecFfreep;
    private static readonly SpecHandler SpecFdecstpHandler = SpecFdecstp;
    private static readonly SpecHandler SpecFincstpHandler = SpecFincstp;
    private static readonly SpecHandler SpecFnclexHandler = SpecFnclex;
    private static readonly SpecHandler SpecFninitHandler = SpecFninit;
    private static readonly SpecHandler SpecMovapsHandler = SpecMovaps;
    private static readonly SpecHandler SpecMovupsHandler = SpecMovups;
    private static readonly SpecHandler SpecMovssHandler = SpecMovss;
    private static readonly SpecHandler SpecMovlpsHandler = SpecMovlps;
    private static readonly SpecHandler SpecMovhpsHandler = SpecMovhps;
    private static readonly SpecHandler SpecMovhlpsHandler = SpecMovhlps;
    private static readonly SpecHandler SpecMovlhpsHandler = SpecMovlhps;
    private static readonly SpecHandler SpecMovntpsHandler = SpecMovntps;
    private static readonly SpecHandler SpecMovmskpsHandler = SpecMovmskps;
    private static readonly SpecHandler SpecSseUnaryHandler = SpecSseUnary;
    private static readonly SpecHandler SpecSsePackedArithmeticHandler = SpecSsePackedArithmetic;
    private static readonly SpecHandler SpecSseLogicalHandler = SpecSseLogical;
    private static readonly SpecHandler SpecSseCompareHandler = SpecSseCompare;
    private static readonly SpecHandler SpecSseCompareScalarHandler = SpecSseCompareScalar;
    private static readonly SpecHandler SpecRclHandler = SpecRcl;
    private static readonly SpecHandler SpecRcrHandler = SpecRcr;
    private static readonly SpecHandler SpecCbwHandler = SpecCbw;
    private static readonly SpecHandler SpecCdqHandler = SpecCdq;
    private static readonly SpecHandler SpecPushfHandler = SpecPushf;
    private static readonly SpecHandler SpecPopfHandler = SpecPopf;
    private static readonly SpecHandler SpecSetccHandler = SpecSetcc;
    private static readonly SpecHandler SpecCmovccHandler = SpecCmovcc;
    private static readonly SpecHandler SpecRdtscHandler = SpecRdtsc;
    private static readonly SpecHandler SpecCliHandler = SpecCli;
    private static readonly SpecHandler SpecStiHandler = SpecSti;
    private static readonly SpecHandler SpecPushMemHandler = SpecPushMem;
    private static readonly SpecHandler SpecShlCountClHandler = SpecShlCountCl;
    private static readonly SpecHandler SpecShrCountClHandler = SpecShrCountCl;
    private static readonly SpecHandler SpecSarCountClHandler = SpecSarCountCl;
    private static readonly SpecHandler SpecRolCountClHandler = SpecRolCountCl;
    private static readonly SpecHandler SpecRorCountClHandler = SpecRorCountCl;
    private static readonly SpecHandler SpecFldRegHandler = SpecFldReg;
    private static readonly SpecHandler SpecFstMemHandler = SpecFstMem;
    private static readonly SpecHandler SpecFstHandler = SpecFst;
    private static readonly SpecHandler SpecFstpHandler = SpecFstp;
    private static readonly SpecHandler SpecFildMemHandler = SpecFildMem;
    private static readonly SpecHandler SpecFistpMemHandler = SpecFistpMem;
    private static readonly SpecHandler SpecFaddHandler = SpecFadd;
    private static readonly SpecHandler SpecFmulHandler = SpecFmul;
    private static readonly SpecHandler SpecFsubHandler = SpecFsub;
    private static readonly SpecHandler SpecFsubrHandler = SpecFsubr;
    private static readonly SpecHandler SpecFdivHandler = SpecFdiv;
    private static readonly SpecHandler SpecFdivrHandler = SpecFdivr;
    private static readonly SpecHandler SpecFaddpHandler = SpecFaddp;
    private static readonly SpecHandler SpecFmulpHandler = SpecFmulp;
    private static readonly SpecHandler SpecFsubpHandler = SpecFsubp;
    private static readonly SpecHandler SpecFsubrpHandler = SpecFsubrp;
    private static readonly SpecHandler SpecFdivpHandler = SpecFdivp;
    private static readonly SpecHandler SpecFdivrpHandler = SpecFdivrp;
    private static readonly SpecHandler SpecFxchHandler = SpecFxch;
    private static readonly SpecHandler SpecFld1Handler = SpecFld1;
    private static readonly SpecHandler SpecFldl2THandler = SpecFldl2T;
    private static readonly SpecHandler SpecFldl2EHandler = SpecFldl2E;
    private static readonly SpecHandler SpecFldpiHandler = SpecFldpi;
    private static readonly SpecHandler SpecFldlg2Handler = SpecFldlg2;
    private static readonly SpecHandler SpecFldln2Handler = SpecFldln2;
    private static readonly SpecHandler SpecFldzHandler = SpecFldz;
    private static readonly SpecHandler SpecMovMemImm8Handler = SpecMovMemImm8;
    private static readonly SpecHandler SpecMovMemImm16Handler = SpecMovMemImm16;
    private static readonly SpecHandler SpecMovMemImm32Handler = SpecMovMemImm32;
    private static readonly SpecHandler SpecAddRegMem8Handler = SpecAddRegMem8;
    private static readonly SpecHandler SpecAddRegMem16Handler = SpecAddRegMem16;
    private static readonly SpecHandler SpecAddRegMem32Handler = SpecAddRegMem32;
    private static readonly SpecHandler SpecAddMemReg8Handler = SpecAddMemReg8;
    private static readonly SpecHandler SpecAddMemReg16Handler = SpecAddMemReg16;
    private static readonly SpecHandler SpecAddMemReg32Handler = SpecAddMemReg32;
    private static readonly SpecHandler SpecAddMemImm8Handler = SpecAddMemImm8;
    private static readonly SpecHandler SpecAddMemImm16Handler = SpecAddMemImm16;
    private static readonly SpecHandler SpecAddMemImm32Handler = SpecAddMemImm32;
    private static readonly SpecHandler SpecSubRegMem8Handler = SpecSubRegMem8;
    private static readonly SpecHandler SpecSubRegMem16Handler = SpecSubRegMem16;
    private static readonly SpecHandler SpecSubRegMem32Handler = SpecSubRegMem32;
    private static readonly SpecHandler SpecSubMemReg8Handler = SpecSubMemReg8;
    private static readonly SpecHandler SpecSubMemReg16Handler = SpecSubMemReg16;
    private static readonly SpecHandler SpecSubMemReg32Handler = SpecSubMemReg32;
    private static readonly SpecHandler SpecSubMemImm8Handler = SpecSubMemImm8;
    private static readonly SpecHandler SpecSubMemImm16Handler = SpecSubMemImm16;
    private static readonly SpecHandler SpecSubMemImm32Handler = SpecSubMemImm32;
    private static readonly SpecHandler SpecAndRegMem8Handler = SpecAndRegMem8;
    private static readonly SpecHandler SpecAndRegMem16Handler = SpecAndRegMem16;
    private static readonly SpecHandler SpecAndRegMem32Handler = SpecAndRegMem32;
    private static readonly SpecHandler SpecAndMemReg8Handler = SpecAndMemReg8;
    private static readonly SpecHandler SpecAndMemReg16Handler = SpecAndMemReg16;
    private static readonly SpecHandler SpecAndMemReg32Handler = SpecAndMemReg32;
    private static readonly SpecHandler SpecAndMemImm8Handler = SpecAndMemImm8;
    private static readonly SpecHandler SpecAndMemImm16Handler = SpecAndMemImm16;
    private static readonly SpecHandler SpecAndMemImm32Handler = SpecAndMemImm32;
    private static readonly SpecHandler SpecOrRegMem8Handler = SpecOrRegMem8;
    private static readonly SpecHandler SpecOrRegMem16Handler = SpecOrRegMem16;
    private static readonly SpecHandler SpecOrRegMem32Handler = SpecOrRegMem32;
    private static readonly SpecHandler SpecOrMemReg8Handler = SpecOrMemReg8;
    private static readonly SpecHandler SpecOrMemReg16Handler = SpecOrMemReg16;
    private static readonly SpecHandler SpecOrMemReg32Handler = SpecOrMemReg32;
    private static readonly SpecHandler SpecOrMemImm8Handler = SpecOrMemImm8;
    private static readonly SpecHandler SpecOrMemImm16Handler = SpecOrMemImm16;
    private static readonly SpecHandler SpecOrMemImm32Handler = SpecOrMemImm32;
    private static readonly SpecHandler SpecXorRegMem8Handler = SpecXorRegMem8;
    private static readonly SpecHandler SpecXorRegMem16Handler = SpecXorRegMem16;
    private static readonly SpecHandler SpecXorRegMem32Handler = SpecXorRegMem32;
    private static readonly SpecHandler SpecXorMemReg8Handler = SpecXorMemReg8;
    private static readonly SpecHandler SpecXorMemReg16Handler = SpecXorMemReg16;
    private static readonly SpecHandler SpecXorMemReg32Handler = SpecXorMemReg32;
    private static readonly SpecHandler SpecXorMemImm8Handler = SpecXorMemImm8;
    private static readonly SpecHandler SpecXorMemImm16Handler = SpecXorMemImm16;
    private static readonly SpecHandler SpecXorMemImm32Handler = SpecXorMemImm32;
    private static readonly SpecHandler SpecCmpRegMem8Handler = SpecCmpRegMem8;
    private static readonly SpecHandler SpecCmpRegMem16Handler = SpecCmpRegMem16;
    private static readonly SpecHandler SpecCmpRegMem32Handler = SpecCmpRegMem32;
    private static readonly SpecHandler SpecCmpMemReg8Handler = SpecCmpMemReg8;
    private static readonly SpecHandler SpecCmpMemReg16Handler = SpecCmpMemReg16;
    private static readonly SpecHandler SpecCmpMemReg32Handler = SpecCmpMemReg32;
    private static readonly SpecHandler SpecCmpMemImm8Handler = SpecCmpMemImm8;
    private static readonly SpecHandler SpecCmpMemImm16Handler = SpecCmpMemImm16;
    private static readonly SpecHandler SpecCmpMemImm32Handler = SpecCmpMemImm32;
    private static readonly SpecHandler SpecTestRegMem8Handler = SpecTestRegMem8;
    private static readonly SpecHandler SpecTestRegMem16Handler = SpecTestRegMem16;
    private static readonly SpecHandler SpecTestRegMem32Handler = SpecTestRegMem32;
    private static readonly SpecHandler SpecTestMemReg8Handler = SpecTestMemReg8;
    private static readonly SpecHandler SpecTestMemReg16Handler = SpecTestMemReg16;
    private static readonly SpecHandler SpecTestMemReg32Handler = SpecTestMemReg32;
    private static readonly SpecHandler SpecTestMemImm8Handler = SpecTestMemImm8;
    private static readonly SpecHandler SpecTestMemImm16Handler = SpecTestMemImm16;
    private static readonly SpecHandler SpecTestMemImm32Handler = SpecTestMemImm32;

    private static void SpecNop(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(
            instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister8(
            instruction.Operand1.RegisterIndex,
            (byte)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            8,
            cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            16,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            32,
            cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = first & second & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = first & second & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                      & cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = first & instruction.Operand2.Immediate & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = first & instruction.Operand2.Immediate & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                      & instruction.Operand2.Immediate & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = (first | second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = (first | second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       | cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = (first | instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = (first | instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       | instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint result = (first ^ second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint result = (first ^ second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       ^ cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint result = (first ^ instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint result = (first ^ instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       ^ instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint value = first & second;
        value &= AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint value = first & second;
        value &= AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                     & cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        value &= AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint value = first & instruction.Operand2.Immediate;
        value &= AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint value = first & instruction.Operand2.Immediate;
        value &= AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex) & instruction.Operand2.Immediate;
        value &= AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Inc(value, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = AluOps.Dec(value, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Inc(cpu.ReadMemory(address, 8), 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Inc(cpu.ReadMemory(address, 16), 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecIncMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Inc(cpu.ReadMemory(address, 32), 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Dec(cpu.ReadMemory(address, 8), 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Dec(cpu.ReadMemory(address, 16), 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDecMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = AluOps.Dec(cpu.ReadMemory(address, 32), 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovqRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteMmx(instruction.Operand1.RegisterIndex, cpu.ReadMmxOperand(instruction.Operand2));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovqRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteMmx(instruction.Operand1.RegisterIndex, cpu.ReadMmxOperand(instruction.Operand2));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovqMemReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMmxOperand(instruction.Operand1, cpu.Context.ReadMmx(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovntqMemReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory64(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            cpu.Context.ReadMmx(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecHintNop(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 11: Prefetch/Sfence/Lfence/Mfence are interpreter nops
        // (`ExecuteSse` breaks without touching any operand or flag), so
        // the handler only advances EIP. Shared by all four shapes; the
        // per-mnemonic cached fields above keep the form-time table exact.
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMulssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMulssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDivssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecDivssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMinssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMinssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMaxssRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMaxssRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCvtss2SiRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecConvertScalarToInt(cpu, in instruction, truncate: false);
    }

    private static void SpecCvtss2SiRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecConvertScalarToInt(cpu, in instruction, truncate: false);
    }

    private static void SpecCvttss2SiRegReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecConvertScalarToInt(cpu, in instruction, truncate: true);
    }

    private static void SpecCvttss2SiRegMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecConvertScalarToInt(cpu, in instruction, truncate: true);
    }

    private static void SpecConvertScalarToInt(XboxCpu cpu, in DecodedInstruction instruction, bool truncate)
    {
        // Slice-12 convert body: verbatim interpreter `Cvtss2Si`/`Cvttss2Si`
        // case with the truncate flag baked per mnemonic. A faulting memory
        // source throws before any register write or EIP advance, exactly
        // like the interpreter.
        (ulong low, ulong high) = cpu.ReadXmmOperand(instruction.Operand2);
        float value = BitConverter.UInt32BitsToSingle(ElementBits((low, high), 0));
        int result = cpu.ConvertFloatToInt32(value, truncate);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, (uint)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 13: verbatim `Fld` case. The bus read faults before the
        // push mutates TOP/tags, so EIP stays precise exactly like the
        // interpreter; width rides Operand1.Size (32/64/80).
        cpu.Fpu.Push(cpu.ReadFpuReal(instruction.Operand1));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFstMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 13: verbatim `Fst` case (store, no pop, no stack change).
        cpu.WriteFpuReal(instruction.Operand1, cpu.Fpu.GetRegister(0));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFst(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 14: same verbatim `Fst` body — `WriteFpuReal` handles both
        // memory and ST(i) destinations, so one body serves both shapes.
        cpu.WriteFpuReal(instruction.Operand1, cpu.Fpu.GetRegister(0));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 14: verbatim `Fld ST(i)` — pushes a copy, no fault possible.
        cpu.Fpu.Push(cpu.Fpu.GetRegister(instruction.Operand1.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFstp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 13: verbatim `Fstp` case (store then pop). The pop runs
        // only after the write succeeds, for both memory and ST(i) forms.
        cpu.WriteFpuReal(instruction.Operand1, cpu.Fpu.GetRegister(0));
        cpu.Fpu.Pop();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFildMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 13: verbatim `Fild` case (width 16/32/64 via Operand1.Size).
        cpu.Fpu.Push(cpu.ReadFpuInteger(instruction.Operand1));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFistpMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 13: verbatim `Fistp` case. RoundingMode and the
        // overflow-to-indefinite + IE flag live inside `WriteFpuInteger`,
        // shared with the interpreter; the pop runs only after the write.
        cpu.WriteFpuInteger(instruction.Operand1, cpu.Fpu.GetRegister(0));
        cpu.Fpu.Pop();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFadd(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fadd);
    }

    private static void SpecFmul(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fmul);
    }

    private static void SpecFsub(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fsub);
    }

    private static void SpecFsubr(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fsubr);
    }

    private static void SpecFdiv(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fdiv);
    }

    private static void SpecFdivr(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuArithmetic(cpu, in instruction, Mnemonic.Fdivr);
    }

    private static void SpecFaddp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Faddp);
    }

    private static void SpecFmulp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Fmulp);
    }

    private static void SpecFsubp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Fsubp);
    }

    private static void SpecFsubrp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Fsubrp);
    }

    private static void SpecFdivp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Fdivp);
    }

    private static void SpecFdivrp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuPopArithmetic(cpu, in instruction, Mnemonic.Fdivrp);
    }

    private static void SpecFpuPopArithmetic(XboxCpu cpu, in DecodedInstruction instruction, Mnemonic mnemonic)
    {
        // Slice-14 shared pop-arithmetic: verbatim interpreter
        // `Faddp/Fmulp/...` case. Both registers read before the write;
        // no memory, no fault; pop runs after the write.
        Fpu80 left = cpu.Fpu.GetRegister(instruction.Operand1.RegisterIndex);
        Fpu80 right = cpu.Fpu.GetRegister(0);
        cpu.Fpu.SetRegister(
            instruction.Operand1.RegisterIndex, ApplyFpuArithmetic(mnemonic, left, right));
        cpu.Fpu.Pop();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFxch(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 14: verbatim `Fxch` — pure register exchange.
        cpu.Fpu.Exchange(instruction.Operand1.RegisterIndex);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFld1(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.One);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldl2T(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.Log2Ten);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldl2E(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.Log2E);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldpi(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.Pi);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldlg2(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.Log10Two);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldln2(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.LnTwo);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFldz(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Push(Fpu80.Zero);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFpuArithmetic(XboxCpu cpu, in DecodedInstruction instruction, Mnemonic mnemonic)
    {
        // Slice-13 shared ST(0) arithmetic: verbatim interpreter
        // read-read-write sequence. Both sources are read into temps
        // before the destination register is written, so a faulting
        // memory source throws with no FPU mutation and precise EIP.
        Fpu80 left = cpu.ReadFpuReal(instruction.Operand1);
        Fpu80 right = cpu.ReadFpuReal(instruction.Operand2);
        cpu.WriteFpuReal(instruction.Operand1, ApplyFpuArithmetic(mnemonic, left, right));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecLeaRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.ComputeAddress(instruction.Operand2.Memory));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecLeaRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ComputeAddress(instruction.Operand2.Memory));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(
            cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex),
            16,
            instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(
            cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex),
            32,
            instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(instruction.Operand1.Immediate, 16, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Push(instruction.Operand1.Immediate, 32, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 15: verbatim interpreter `Push` case. The source read
        // faults before ESP moves; a faulting stack write throws with the
        // same ESP state as the interpreter since the `Push` helper runs
        // the identical sequence on both paths.
        cpu.Push(cpu.ReadOperand(instruction.Operand1), instruction.Operand1.Size, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlCountCl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrCountCl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarCountCl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolCountCl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorCountCl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMulDivSingle(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 15 (Mul) + slice 16 (Div/Idiv): verbatim interpreter
        // Mul/Div path. `ExecuteMulDiv` dispatches on the mnemonic exactly
        // like `ExecuteInstruction`; a Div/Idiv #DE raise transfers EIP
        // and returns true (no advance), otherwise EIP advances — the
        // serve-time `_expectedLinear` guard redirects mid-block raises
        // exactly like the control-flow handlers.
        if (cpu.ExecuteMulDiv(instruction))
        {
            return;
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecImul(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 15: verbatim interpreter Imul path (all three operand
        // forms). `ExecuteImul` reads before writing in every form.
        cpu.ExecuteImul(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecNeg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Neg` case (Inc/Dec pattern).
        uint value = cpu.ReadOperand(instruction.Operand1);
        cpu.Context.Eflags = AluOps.Neg(value, instruction.Operand1.Size, cpu.Context.Eflags, out uint result);
        cpu.WriteOperand(instruction.Operand1, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecNot(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Not` case (no flags touched).
        cpu.WriteOperand(instruction.Operand1, ~cpu.ReadOperand(instruction.Operand1));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecBswap(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Bswap` case (reg-only, no flags).
        Gpr register = (Gpr)instruction.Operand1.RegisterIndex;
        uint value = cpu.Context.ReadRegister(register);
        cpu.Context.WriteRegister(
            register,
            (value >> 24) | ((value >> 8) & 0x0000FF00u) | ((value << 8) & 0x00FF0000u) | (value << 24));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXchg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Xchg` case. The read-read-
        // write-write order is shared shape-for-shape, so a faulting
        // memory side throws at the identical point with identical EIP.
        uint first = cpu.ReadOperand(instruction.Operand1);
        uint second = cpu.ReadOperand(instruction.Operand2);
        cpu.WriteOperand(instruction.Operand1, second);
        cpu.WriteOperand(instruction.Operand2, first);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecLeave(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Leave` case (16/32-bit
        // ESP/EBP handling lives in the shared helper; a faulting stack
        // pop throws with the same ESP/EBP state on both paths).
        cpu.ExecuteLeave(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFiadd(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fiadd);
    }

    private static void SpecFimul(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fimul);
    }

    private static void SpecFisub(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fisub);
    }

    private static void SpecFisubr(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fisubr);
    }

    private static void SpecFidiv(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fidiv);
    }

    private static void SpecFidivr(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerArithmetic(cpu, in instruction, Mnemonic.Fidivr);
    }

    private static void SpecFpuIntegerArithmetic(XboxCpu cpu, in DecodedInstruction instruction, Mnemonic mnemonic)
    {
        // Slice-16 shared x87 integer arithmetic: verbatim interpreter
        // `Fiadd/...` case. The integer read faults before ST(0) updates,
        // so EIP stays precise exactly like the interpreter.
        cpu.Fpu.SetRegister(
            0, ApplyFpuArithmetic(mnemonic, cpu.Fpu.GetRegister(0), cpu.ReadFpuInteger(instruction.Operand1)));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFnstsw(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 16: verbatim interpreter `Fnstsw` case (AX or m16).
        // A faulting m16 write throws before any state change.
        if (instruction.Operand1.Kind == OperandKind.None)
        {
            cpu.Context.WriteRegister16(Gpr.Eax, cpu.Fpu.StatusWord);
        }
        else
        {
            cpu.WriteMemory(
                cpu.ComputeAddress(instruction.Operand1.Memory), 16, cpu.Fpu.StatusWord);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFcom(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuCompare(cpu, in instruction, pop: false);
    }

    private static void SpecFcomp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuCompare(cpu, in instruction, pop: true);
    }

    private static void SpecFucom(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuCompare(cpu, in instruction, pop: false);
    }

    private static void SpecFucomp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuCompare(cpu, in instruction, pop: true);
    }

    private static void SpecFpuCompare(XboxCpu cpu, in DecodedInstruction instruction, bool pop)
    {
        // Slice-17 shared compare: verbatim interpreter
        // `Fcom/Fcomp/Fucom/Fucomp` case. The compared operand is read
        // into a temp before `SetFpuCompare` touches any status bit, so
        // a faulting memory source throws with no FPU mutation and
        // precise EIP; the conditional pop runs only after success.
        Operand compared = instruction.Operand2.Kind == OperandKind.None
            ? instruction.Operand1
            : instruction.Operand2;
        cpu.SetFpuCompare(cpu.Fpu.GetRegister(0), cpu.ReadFpuReal(compared));
        if (pop)
        {
            cpu.Fpu.Pop();
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFicom(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerCompare(cpu, in instruction, pop: false);
    }

    private static void SpecFicomp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        SpecFpuIntegerCompare(cpu, in instruction, pop: true);
    }

    private static void SpecFpuIntegerCompare(XboxCpu cpu, in DecodedInstruction instruction, bool pop)
    {
        // Slice-17 shared integer compare: verbatim interpreter
        // `Ficom/Ficomp` case with the same read-before-compare order.
        cpu.SetFpuCompare(cpu.Fpu.GetRegister(0), cpu.ReadFpuInteger(instruction.Operand1));
        if (pop)
        {
            cpu.Fpu.Pop();
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFchs(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.SetRegister(0, cpu.Fpu.GetRegister(0).Negate());
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFabs(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.SetRegister(0, cpu.Fpu.GetRegister(0).Abs());
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFtst(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.SetFpuCompare(cpu.Fpu.GetRegister(0), Fpu80.Zero);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFfree(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Free(instruction.Operand1.RegisterIndex);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFfreep(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Free(instruction.Operand1.RegisterIndex);
        cpu.Fpu.Pop();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFdecstp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Top = (cpu.Fpu.Top - 1) & 7;
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFincstp(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Top = (cpu.Fpu.Top + 1) & 7;
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFnclex(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.ClearExceptions();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecFninit(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Fpu.Init();
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovaps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movaps` case.
        (ulong low, ulong high) = cpu.ReadXmmOperand(instruction.Operand2);
        cpu.WriteXmmOperand(instruction.Operand1, low, high);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovups(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movups` case (same helpers —
        // alignment is the interpreter's business, identical on both).
        (ulong low, ulong high) = cpu.ReadXmmOperand(instruction.Operand2);
        cpu.WriteXmmOperand(instruction.Operand1, low, high);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovss(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movss` case (all three forms).
        // The mem-store form reads the XMM element (no fault) before the
        // m32 write, so EIP stays precise exactly like the interpreter.
        if (instruction.Operand1.Kind == OperandKind.Register)
        {
            (ulong low, ulong high) = cpu.ReadXmmOperand(instruction.Operand2);
            cpu.Context.WriteXmmElement(
                instruction.Operand1.RegisterIndex, 0, ElementBits((low, high), 0));
        }
        else
        {
            uint source = cpu.Context.ReadXmmElement(instruction.Operand2.RegisterIndex, 0);
            cpu.WriteMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 32, source);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovlps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movlps` case, including the
        // reg,reg fall-through (untouched on both paths).
        if (instruction.Operand1.Kind == OperandKind.Memory)
        {
            cpu.WriteMemory64(
                cpu.ComputeAddress(instruction.Operand1.Memory),
                cpu.Context.ReadXmm64(instruction.Operand2.RegisterIndex, 0));
        }
        else if (instruction.Operand2.Kind == OperandKind.Memory)
        {
            ulong value = cpu.ReadMemory64(cpu.ComputeAddress(instruction.Operand2.Memory));
            cpu.Context.WriteXmm64(instruction.Operand1.RegisterIndex, 0, value);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovhps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movhps` case (same fall-through
        // discipline as Movlps).
        if (instruction.Operand1.Kind == OperandKind.Memory)
        {
            cpu.WriteMemory64(
                cpu.ComputeAddress(instruction.Operand1.Memory),
                cpu.Context.ReadXmm64(instruction.Operand2.RegisterIndex, 1));
        }
        else if (instruction.Operand2.Kind == OperandKind.Memory)
        {
            ulong value = cpu.ReadMemory64(cpu.ComputeAddress(instruction.Operand2.Memory));
            cpu.Context.WriteXmm64(instruction.Operand1.RegisterIndex, 1, value);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovhlps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movhlps` case.
        ulong source = cpu.Context.ReadXmm64(instruction.Operand2.RegisterIndex, 1);
        cpu.Context.WriteXmm64(instruction.Operand1.RegisterIndex, 0, source);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovlhps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movlhps` case.
        ulong source = cpu.Context.ReadXmm64(instruction.Operand2.RegisterIndex, 0);
        cpu.Context.WriteXmm64(instruction.Operand1.RegisterIndex, 1, source);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovntps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movntps` case (hint ignored on
        // both paths — plain 128-bit store).
        (ulong low, ulong high) = cpu.Context.ReadXmm128(instruction.Operand2.RegisterIndex);
        cpu.WriteXmmOperand(instruction.Operand1, low, high);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovmskps(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 18: verbatim interpreter `Movmskps` case.
        uint result = 0;
        for (int i = 0; i < 4; i++)
        {
            if ((cpu.Context.ReadXmmElement(instruction.Operand2.RegisterIndex, i) & 0x80000000u) != 0)
            {
                result |= 1u << i;
            }
        }

        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSseUnary(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 19: verbatim interpreter sqrt/rcp case (packed + scalar
        // distinguished inside the shared helper, exactly as dispatched).
        cpu.ExecuteSseUnary(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSsePackedArithmetic(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 19: verbatim interpreter packed-arithmetic case.
        cpu.ExecuteSseArithmetic(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSseLogical(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 19: verbatim interpreter packed-logical case.
        cpu.ExecuteSseLogical(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSseCompare(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 19: verbatim interpreter packed/scalar compare case.
        cpu.ExecuteSseCompare(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSseCompareScalar(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 19: verbatim interpreter Comiss/Ucomiss case (EFLAGS
        // only, no control flow).
        cpu.ExecuteSseCompareScalar(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRcl(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter carry-rotate (CF threaded
        // inside the shared helper on both paths).
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRcr(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.ExecuteShift(instruction);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCbw(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Cbw` case (operand-size
        // branch read from the cached member on both paths).
        if (instruction.OperandSize == 16)
        {
            sbyte value = (sbyte)cpu.Context.ReadRegister8(0);
            cpu.Context.WriteRegister16(Gpr.Eax, (ushort)value);
        }
        else
        {
            short value = (short)cpu.Context.ReadRegister16(Gpr.Eax);
            cpu.Context.WriteRegister(Gpr.Eax, (uint)value);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCdq(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Cdq` case.
        if (instruction.OperandSize == 16)
        {
            short value = (short)cpu.Context.ReadRegister16(Gpr.Eax);
            cpu.Context.WriteRegister16(Gpr.Edx, value < 0 ? (ushort)0xFFFF : (ushort)0);
        }
        else
        {
            int value = (int)cpu.Context.ReadRegister(Gpr.Eax);
            cpu.Context.WriteRegister(Gpr.Edx, value < 0 ? 0xFFFFFFFFu : 0u);
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPushf(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Pushf` case.
        cpu.Push(cpu.Context.Eflags | FlagBits.Reserved1, instruction.OperandSize, instruction.AddressSize);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPopf(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Popf` case (a faulting stack
        // read throws before EFLAGS changes; IF effects land at the
        // existing IRQ-check boundaries on both paths).
        cpu.Context.Eflags = cpu.Pop(instruction.OperandSize, instruction.AddressSize) | FlagBits.Reserved1;
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSetcc(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Setcc` case (condition rides
        // the cached member through the shared evaluator).
        cpu.WriteOperand(instruction.Operand1, cpu.EvaluateCondition(instruction.Condition) ? 1u : 0u);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmovcc(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Cmovcc` case (flags only, no
        // control flow; the unread side stays untouched on both paths).
        if (cpu.EvaluateCondition(instruction.Condition))
        {
            cpu.WriteOperand(instruction.Operand1, cpu.ReadOperand(instruction.Operand2));
        }

        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRdtsc(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 20: verbatim interpreter `Rdtsc` case (TSC advances per
        // executed step identically on both sides of a differential).
        cpu.Context.Eax = (uint)cpu.Context.Tsc;
        cpu.Context.Edx = (uint)(cpu.Context.Tsc >> 32);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCli(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 6 (§168): verbatim interpreter `Cli` case (IF effects land
        // at the existing per-instruction IRQ-check boundaries on both
        // paths — the mirror is exact by shared shape).
        cpu.Context.Eflags &= ~FlagBits.If;
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSti(XboxCpu cpu, in DecodedInstruction instruction)
    {
        // Slice 6 (§168): verbatim interpreter `Sti` case (same
        // boundary argument as Cli; the 1-instruction STI shadow was
        // never modeled — delivery fires at the next poll either way).
        cpu.Context.Eflags |= FlagBits.If;
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPopReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.Pop(16, instruction.AddressSize));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecPopReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Pop(32, instruction.AddressSize));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            8,
            instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            16,
            instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.WriteMemory(
            cpu.ComputeAddress(instruction.Operand1.Memory),
            32,
            instruction.Operand2.Immediate);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 8);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 16);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 32);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Add(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 8);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 16);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAddMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 32);
        cpu.Context.Eflags =
            AluOps.Add(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 8);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 16);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 32);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 8);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 16);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSubMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint first = cpu.ReadMemory(address, 32);
        cpu.Context.Eflags =
            AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        uint result = first & second & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        uint result = first & second & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                      & cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 8)
                      & cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 16)
                      & cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 32)
                      & cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 8) & instruction.Operand2.Immediate & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 16) & instruction.Operand2.Immediate & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecAndMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = cpu.ReadMemory(address, 32) & instruction.Operand2.Immediate & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        uint result = (first | second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        uint result = (first | second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       | cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 8)
                       | cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex)) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 16)
                       | cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 32)
                       | cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 8) | instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 16) | instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecOrMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 32) | instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        uint result = (first ^ second) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        uint result = (first ^ second) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint result = (cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex)
                       ^ cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 8)
                       ^ cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex)) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 16)
                       ^ cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 32)
                       ^ cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex)) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 8) ^ instruction.Operand2.Immediate) & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(result, 8, cpu.Context.Eflags);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 16) ^ instruction.Operand2.Immediate) & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(result, 16, cpu.Context.Eflags);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecXorMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint result = (cpu.ReadMemory(address, 32) ^ instruction.Operand2.Immediate) & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(result, 32, cpu.Context.Eflags);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 8);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 16);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        cpu.Context.Eflags = AluOps.Sub(first, second, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 8);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 8, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 16);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 16, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecCmpMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        cpu.Context.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, cpu.Context.Eflags, out _);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegMem8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8);
        uint value = first & second & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegMem16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16);
        uint value = first & second & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestRegMem32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 32);
        uint value = first & second & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemReg8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 8);
        uint second = cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex);
        uint value = first & second & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemReg16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 16);
        uint second = cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex);
        uint value = first & second & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemReg32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        uint second = cpu.Context.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        uint value = first & second & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 8);
        uint value = first & instruction.Operand2.Immediate & AluOps.Mask(8);
        cpu.Context.Eflags = AluOps.Logical(value, 8, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 16);
        uint value = first & instruction.Operand2.Immediate & AluOps.Mask(16);
        cpu.Context.Eflags = AluOps.Logical(value, 16, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecTestMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint first = cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        uint value = first & instruction.Operand2.Immediate & AluOps.Mask(32);
        cpu.Context.Eflags = AluOps.Logical(value, 32, cpu.Context.Eflags);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecJccOverflow(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Of) != 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccNotOverflow(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Of) == 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccBelow(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Cf) != 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccAboveOrEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Cf) == 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Zf) != 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccNotEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Zf) == 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccBelowOrEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = ((flags & FlagBits.Cf) != 0 || (flags & FlagBits.Zf) != 0)
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccAbove(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = ((flags & FlagBits.Cf) == 0 && (flags & FlagBits.Zf) == 0)
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccSign(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Sf) != 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccNotSign(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Sf) == 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccParity(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Pf) != 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccNotParity(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = (cpu.Context.Eflags & FlagBits.Pf) == 0
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccLess(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = (((flags & FlagBits.Sf) != 0) != ((flags & FlagBits.Of) != 0))
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccGreaterOrEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = (((flags & FlagBits.Sf) != 0) == ((flags & FlagBits.Of) != 0))
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccLessOrEqual(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = ((flags & FlagBits.Zf) != 0
                           || ((flags & FlagBits.Sf) != 0) != ((flags & FlagBits.Of) != 0))
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJccGreater(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint flags = cpu.Context.Eflags;
        cpu.Context.Eip = ((flags & FlagBits.Zf) == 0
                           && ((flags & FlagBits.Sf) != 0) == ((flags & FlagBits.Of) != 0))
            ? nextEip + instruction.Operand1.Immediate
            : nextEip;
    }

    private static void SpecJmpImm(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        cpu.Context.Eip = nextEip + instruction.Operand1.Immediate;
    }

    private static void SpecJmpReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip = cpu.ReadOperand(instruction.Operand1);
    }

    private static void SpecJmpMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip = cpu.ReadOperand(instruction.Operand1);
    }

    private static void SpecCallImm(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint target = nextEip + instruction.Operand1.Immediate;
        cpu.Push(nextEip, instruction.OperandSize, instruction.AddressSize);
        cpu.Context.Eip = target;
    }

    private static void SpecCallReg(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint target = cpu.ReadOperand(instruction.Operand1);
        cpu.Push(nextEip, instruction.OperandSize, instruction.AddressSize);
        cpu.Context.Eip = target;
    }

    private static void SpecCallMem(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint nextEip = cpu.Context.Eip + (uint)instruction.Length;
        uint target = cpu.ReadOperand(instruction.Operand1);
        cpu.Push(nextEip, instruction.OperandSize, instruction.AddressSize);
        cpu.Context.Eip = target;
    }

    private static void SpecRet(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip = cpu.Pop(instruction.OperandSize, instruction.AddressSize);
    }

    private static void SpecRetImm(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.Eip = cpu.Pop(instruction.OperandSize, instruction.AddressSize);
        cpu.AdjustStackPointer(instruction.Operand1.Immediate, instruction.AddressSize);
    }

    private static void SpecShlRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shl(value, (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shl(value, (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shl(value, (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shl(cpu.ReadMemory(address, 8), (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shl(cpu.ReadMemory(address, 16), (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShlMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shl(cpu.ReadMemory(address, 32), (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shr(value, (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shr(value, (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Shr(value, (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shr(cpu.ReadMemory(address, 8), (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shr(cpu.ReadMemory(address, 16), (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecShrMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Shr(cpu.ReadMemory(address, 32), (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Sar(value, (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Sar(value, (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Sar(value, (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Sar(cpu.ReadMemory(address, 8), (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Sar(cpu.ReadMemory(address, 16), (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecSarMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Sar(cpu.ReadMemory(address, 32), (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Rol(value, (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Rol(value, (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Rol(value, (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Rol(cpu.ReadMemory(address, 8), (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Rol(cpu.ReadMemory(address, 16), (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRolMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Rol(cpu.ReadMemory(address, 32), (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorRegImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister8(instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Ror(value, (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorRegImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister16((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Ror(value, (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister16((Gpr)instruction.Operand1.RegisterIndex, (ushort)result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorRegImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint value = cpu.Context.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        cpu.Context.Eflags = ShiftOps.Ror(value, (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.Context.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorMemImm8(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Ror(cpu.ReadMemory(address, 8), (int)instruction.Operand2.Immediate, 8, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 8, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorMemImm16(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Ror(cpu.ReadMemory(address, 16), (int)instruction.Operand2.Immediate, 16, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 16, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecRorMemImm32(XboxCpu cpu, in DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        cpu.Context.Eflags = ShiftOps.Ror(cpu.ReadMemory(address, 32), (int)instruction.Operand2.Immediate, 32, cpu.Context.Eflags, out uint result);
        cpu.WriteMemory(address, 32, result);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegReg168(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex) & 0xFFu));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegReg328(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex) & 0xFFu);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegReg1616(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegReg3216(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex) & 0xFFFFu);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegMem168(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8) & 0xFFu));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegMem328(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8) & 0xFFu);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegMem1616(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovzxRegMem3216(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16) & 0xFFFFu);
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegReg168(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(uint)(int)(sbyte)cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegReg328(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            (uint)(int)(sbyte)cpu.Context.ReadRegister8(instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegReg1616(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(uint)(int)(short)cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegReg3216(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            (uint)(int)(short)cpu.Context.ReadRegister16((Gpr)instruction.Operand2.RegisterIndex));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegMem168(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(uint)(int)(sbyte)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegMem328(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            (uint)(int)(sbyte)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 8));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegMem1616(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister16(
            (Gpr)instruction.Operand1.RegisterIndex,
            (ushort)(uint)(int)(short)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16));
        cpu.Context.Eip += (uint)instruction.Length;
    }

    private static void SpecMovsxRegMem3216(XboxCpu cpu, in DecodedInstruction instruction)
    {
        cpu.Context.WriteRegister(
            (Gpr)instruction.Operand1.RegisterIndex,
            (uint)(int)(short)cpu.ReadMemory(cpu.ComputeAddress(instruction.Operand2.Memory), 16));
        cpu.Context.Eip += (uint)instruction.Length;
    }
}
