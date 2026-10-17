namespace XGrid.Cpu;

/// <summary>
/// E3-1 (§175): lean per-shape bodies served directly from emitted tails.
/// These are call-based twins of the memory spec handlers with the
/// emit-mode invariants folded in: an emitted tail only runs while
/// <see cref="XboxCpu.HotspotObserver"/> and the idle-skip detector are
/// null (the RunEmitTail guard), so the <c>_hotspot</c> branches and notes
/// of <see cref="XboxCpu.ReadMemory"/>/<see cref="XboxCpu.WriteMemory"/>
/// are unreachable and the lean helpers skip them. Everything else — MMU
/// gate, cross-page byte loop, translate side effects, RAM-direct fallback,
/// store-watch handling and exact fault order — mirrors the interpreter
/// path statement-for-statement. The context arrives hoisted from the
/// emitted runner. Bodies are private: the only caller is the emitted
/// runner (via <see cref="ResolveInlineHandler"/>).
/// </summary>
public sealed partial class XboxCpu
{
    /// <summary>
    /// Context-hoisted shape body for emitted tails. Same semantics as the
    /// matching <see cref="SpecHandler"/>, with the context passed in.
    /// </summary>
    public delegate void SpecInlineHandler(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction);

    /// <summary>
    /// Data read mirroring <see cref="ReadMemory"/> minus the hotspot
    /// observer (null by the emit-mode guard). Faults, per-byte cross-page
    /// order, MMU gate and RAM-direct behavior are identical.
    /// </summary>
    private static uint ReadDataFast(XboxCpu cpu, uint address, int size)
    {
        if (!cpu.Mmu.IsEnabled)
        {
            return cpu.RawRead(address, size);
        }

        if (CrossesPage(address, size))
        {
            int bytes = size / 8;
            uint value = 0;
            for (int i = 0; i < bytes; i++)
            {
                uint physical = cpu.TranslateLinear(address + (uint)i, isWrite: false, isInstruction: false);
                value |= (uint)cpu.Memory.Read8(physical) << (8 * i);
            }

            return value;
        }

        return cpu.RawRead(cpu.TranslateLinear(address, isWrite: false, isInstruction: false), size);
    }

    /// <summary>
    /// Data write mirroring <see cref="WriteMemory"/> minus the hotspot
    /// observer (null by the emit-mode guard). The per-byte cross-page path
    /// keeps the per-byte <see cref="NoteStorePhysical"/> call exactly like
    /// the interpreter.
    /// </summary>
    private static void WriteDataFast(XboxCpu cpu, uint address, int size, uint value)
    {
        if (!cpu.Mmu.IsEnabled)
        {
            cpu.RawWrite(address, size, value);
            return;
        }

        if (CrossesPage(address, size))
        {
            int bytes = size / 8;
            for (int i = 0; i < bytes; i++)
            {
                uint physical = cpu.TranslateLinear(address + (uint)i, isWrite: true, isInstruction: false);
                cpu.Memory.Write8(physical, (byte)(value >> (8 * i)));
                cpu.NoteStorePhysical(physical, 8);
            }

            return;
        }

        cpu.RawWrite(cpu.TranslateLinear(address, isWrite: true, isInstruction: false), size, value);
    }

    private static void MovRegMem8Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand2.Memory);
        uint value = ReadDataFast(cpu, address, 8);
        ctx.WriteRegister8(instruction.Operand1.RegisterIndex, (byte)value);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void MovRegMem32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand2.Memory);
        uint value = ReadDataFast(cpu, address, 32);
        ctx.WriteRegister((Gpr)instruction.Operand1.RegisterIndex, value);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void MovMemReg8Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint value = ctx.ReadRegister8(instruction.Operand2.RegisterIndex);
        WriteDataFast(cpu, address, 8, value);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void MovMemReg32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint address = cpu.ComputeAddress(instruction.Operand1.Memory);
        uint value = ctx.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        WriteDataFast(cpu, address, 32, value);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void CmpRegMem32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint first = ctx.ReadRegister((Gpr)instruction.Operand1.RegisterIndex);
        uint second = ReadDataFast(cpu, cpu.ComputeAddress(instruction.Operand2.Memory), 32);
        ctx.Eflags = AluOps.Sub(first, second, 0, 32, ctx.Eflags, out _);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void CmpMemReg32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint first = ReadDataFast(cpu, cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        uint second = ctx.ReadRegister((Gpr)instruction.Operand2.RegisterIndex);
        ctx.Eflags = AluOps.Sub(first, second, 0, 32, ctx.Eflags, out _);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void CmpMemImm32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint first = ReadDataFast(cpu, cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        ctx.Eflags = AluOps.Sub(first, instruction.Operand2.Immediate, 0, 32, ctx.Eflags, out _);
        ctx.Eip += (uint)instruction.Length;
    }

    private static void TestMemImm32Fast(XboxContext ctx, XboxCpu cpu, ref DecodedInstruction instruction)
    {
        uint first = ReadDataFast(cpu, cpu.ComputeAddress(instruction.Operand1.Memory), 32);
        uint value = first & instruction.Operand2.Immediate & AluOps.Mask(32);
        ctx.Eflags = AluOps.Logical(value, 32, ctx.Eflags);
        ctx.Eip += (uint)instruction.Length;
    }

    private static readonly SpecInlineHandler MovRegMem8FastHandler = MovRegMem8Fast;
    private static readonly SpecInlineHandler MovRegMem32FastHandler = MovRegMem32Fast;
    private static readonly SpecInlineHandler MovMemReg8FastHandler = MovMemReg8Fast;
    private static readonly SpecInlineHandler MovMemReg32FastHandler = MovMemReg32Fast;
    private static readonly SpecInlineHandler CmpRegMem32FastHandler = CmpRegMem32Fast;
    private static readonly SpecInlineHandler CmpMemReg32FastHandler = CmpMemReg32Fast;
    private static readonly SpecInlineHandler CmpMemImm32FastHandler = CmpMemImm32Fast;
    private static readonly SpecInlineHandler TestMemImm32FastHandler = TestMemImm32Fast;

    /// <summary>
    /// Resolves a member to its inline body twin, or null when the shape has
    /// none. The twin is derived from <see cref="ResolveSpec"/>'s handler so
    /// the two can never disagree about which shapes are covered; only the
    /// vehicle differs. Build-time only (called from BlockEmitter.Build).
    /// </summary>
    public static SpecInlineHandler? ResolveInlineHandler(in DecodedInstruction instruction)
    {
        SpecHandler? handler = ResolveSpec(instruction);
        if (handler is null)
        {
            return null;
        }

        if (handler == SpecMovRegMem8Handler)
        {
            return MovRegMem8FastHandler;
        }

        if (handler == SpecMovRegMem32Handler)
        {
            return MovRegMem32FastHandler;
        }

        if (handler == SpecMovMemReg8Handler)
        {
            return MovMemReg8FastHandler;
        }

        if (handler == SpecMovMemReg32Handler)
        {
            return MovMemReg32FastHandler;
        }

        if (handler == SpecCmpRegMem32Handler)
        {
            return CmpRegMem32FastHandler;
        }

        if (handler == SpecCmpMemReg32Handler)
        {
            return CmpMemReg32FastHandler;
        }

        if (handler == SpecCmpMemImm32Handler)
        {
            return CmpMemImm32FastHandler;
        }

        if (handler == SpecTestMemImm32Handler)
        {
            return TestMemImm32FastHandler;
        }

        return null;
    }
}
