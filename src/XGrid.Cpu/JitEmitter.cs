using System;
using System.Reflection;
using System.Reflection.Emit;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>
/// Fast-mode block emitter (J1b slice): builds one DynamicMethod per block
/// that keeps all eight GPRs, EIP, EFLAGS and TSC in IL locals across the
/// whole block and serves each member with baked native code.
///
/// Data members run either fully in the locals (register/immediate forms) or
/// through an inlined translate-cache probe + RAM-direct access; every
/// non-fast case (cache miss, non-RAM, cross-page, watched store,
/// non-writable store under WP) takes the exact interpreter access path
/// (ReadMemory/WriteMemory/Push/Pop) after syncing the locals back to the
/// context, so faults stay precise and every side effect keeps its legacy
/// order. Stack members (push/pop/call/ret) mirror the interpreter's
/// Push/Pop helpers including their ESP-before-write order.
///
/// The only boundary relaxed relative to the interpreter is the IRQ poll
/// position: the caller polls once per block (RunJitBlock), which the
/// dual-mode contract permits; TSC, EIP, faults and stores all stay
/// per-instruction exact.
/// </summary>
internal static class JitEmitter
{
    /// <summary>Maximum members per emitted block.</summary>
    public const int MaxMembers = 32;

    private static readonly MethodInfo GetContext =
        typeof(XboxCpu).GetProperty("Context")!.GetMethod!;

    // C2 slice 1: Eip/Eflags/Tsc are plain auto-properties with no side
    // effects, so the emitted code addresses their backing fields directly.
    // The six property callvirts per block (plus one per memory member for
    // the fault-precise Eip store) were pure call overhead.
    private static readonly FieldInfo ContextEip =
        typeof(XboxContext).GetField("_eip", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo ContextEflags =
        typeof(XboxContext).GetField("_eflags", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo ContextTsc =
        typeof(XboxContext).GetField("_tsc", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo GetEsp =
        typeof(XboxContext).GetProperty("Esp")!.GetMethod!;

    private static readonly MethodInfo GetCr0 =
        typeof(XboxContext).GetProperty("Cr0")!.GetMethod!;

    private static readonly MethodInfo GetSegmentBase =
        typeof(XboxContext).GetMethod("GetSegmentBase")!;

    /// <summary>Slow-member mnemonic tally enabled by XGRID_SLOW=1 (diagnostic).</summary>
    private static readonly bool TallySlowMembers =
        string.Equals(Environment.GetEnvironmentVariable("XGRID_SLOW"), "1", StringComparison.Ordinal);

    private static readonly FieldInfo SlowMemberCountsField =
        typeof(XboxCpu).GetField("<SlowMemberCounts>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo ContextRegisters =
        typeof(XboxContext).GetField("_registers", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatTag =
        typeof(XboxCpu).GetField("_dataXlatTag", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatPhys =
        typeof(XboxCpu).GetField("_dataXlatPhys", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo DataXlatWritable =
        typeof(XboxCpu).GetField("_dataXlatWritable", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo RamDirect =
        typeof(XboxCpu).GetField("_ramDirect", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo RamSize =
        typeof(XboxCpu).GetField("_ramSize", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo JitProgress =
        typeof(XboxCpu).GetField("_jitProgress", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo JitSlowMembers =
        typeof(XboxCpu).GetField("_jitSlowMembers", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo JitPending =
        typeof(XboxCpu).GetField("_jitPending", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo EntryInstruction =
        typeof(DecodeCacheEntry).GetField("Instruction")!;

    private static readonly MethodInfo GetInterruptSource =
        typeof(XboxCpu).GetProperty("InterruptSource")!.GetMethod!;

    private static readonly MethodInfo Acknowledge =
        typeof(IInterruptSource).GetMethod("Acknowledge")!;

    private static readonly MethodInfo GetMmu =
        typeof(XboxCpu).GetProperty("Mmu")!.GetMethod!;

    private static readonly MethodInfo FlushPage =
        typeof(Mmu).GetMethod("FlushPage")!;

    private static readonly MethodInfo IsWatched =
        typeof(XboxCpu).GetMethod("IsWatched", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo NoteStorePhysical =
        typeof(XboxCpu).GetMethod("NoteStorePhysical", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ReadMemory =
        typeof(XboxCpu).GetMethod("ReadMemory", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo WriteMemory =
        typeof(XboxCpu).GetMethod("WriteMemory", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ReadPhysicalJit =
        typeof(XboxCpu).GetMethod("ReadPhysicalJit", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo JitFpuPushSingle =
        typeof(XboxCpu).GetMethod("JitFpuPushSingle", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo JitFpuSingleBits =
        typeof(XboxCpu).GetMethod("JitFpuSingleBits", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo JitFpuPop =
        typeof(XboxCpu).GetMethod("JitFpuPop", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo WritePhysicalJit =
        typeof(XboxCpu).GetMethod("WritePhysicalJit", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo Push =
        typeof(XboxCpu).GetMethod("Push", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo Pop =
        typeof(XboxCpu).GetMethod("Pop", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RamRead8 =
        typeof(PhysicalMemory).GetMethod("Read8")!;

    private static readonly MethodInfo RamRead16 =
        typeof(PhysicalMemory).GetMethod("Read16")!;

    private static readonly MethodInfo RamRead32 =
        typeof(PhysicalMemory).GetMethod("Read32")!;

    private static readonly MethodInfo RamWrite8 =
        typeof(PhysicalMemory).GetMethod("Write8")!;

    private static readonly MethodInfo RamWrite16 =
        typeof(PhysicalMemory).GetMethod("Write16")!;

    private static readonly MethodInfo RamWrite32 =
        typeof(PhysicalMemory).GetMethod("Write32")!;

    private static readonly MethodInfo AluAdd =
        typeof(AluOps).GetMethod("Add")!;

    private static readonly MethodInfo AluSub =
        typeof(AluOps).GetMethod("Sub")!;

    private static readonly MethodInfo AluLogical =
        typeof(AluOps).GetMethod("Logical")!;

    private static readonly MethodInfo AluInc =
        typeof(AluOps).GetMethod("Inc")!;

    private static readonly MethodInfo AluDec =
        typeof(AluOps).GetMethod("Dec")!;

    private static readonly MethodInfo ShiftShl = typeof(ShiftOps).GetMethod("Shl")!;
    private static readonly MethodInfo ShiftShr = typeof(ShiftOps).GetMethod("Shr")!;
    private static readonly MethodInfo ShiftSar = typeof(ShiftOps).GetMethod("Sar")!;
    private static readonly MethodInfo ShiftRol = typeof(ShiftOps).GetMethod("Rol")!;
    private static readonly MethodInfo ShiftRor = typeof(ShiftOps).GetMethod("Ror")!;
    private static readonly MethodInfo ShiftRcl = typeof(ShiftOps).GetMethod("Rcl")!;
    private static readonly MethodInfo ShiftRcr = typeof(ShiftOps).GetMethod("Rcr")!;

    /// <summary>
    /// Builds a runner for the given planned members, or null when the plan
    /// set is empty. The runner contract: budget &gt; 0; serves
    /// <c>min(budget, plans.Length)</c> members; syncs the full guest context
    /// on every exit and immediately before any interpreter-path access (so a
    /// thrown <c>PageFaultSignal</c> leaves the context at the faulting
    /// member's boundary).
    /// </summary>
    public static JitRun? Build(uint headLinear, ReadOnlySpan<JitMemberPlan> plans, bool exactBoundaries,
        bool mmioFast)
    {
        if (plans.Length == 0)
        {
            return null;
        }

        // C2 slice 1 (lazy state sync): load the union of every member's read
        // and write masks, store only the block's write mask. Registers the
        // block never touches cost nothing; over-stores at mid-block sync
        // points write back unchanged values (every write-mask register is
        // loaded at the prologue).
        uint loadMask = 0;
        uint storeMask = 0;
        for (int i = 0; i < plans.Length; i++)
        {
            uint read = JitStateMask.ReadMask(plans[i]);
            uint write = JitStateMask.WriteMask(plans[i]);
            loadMask |= read | write;
            storeMask |= write;
        }

        var method = new DynamicMethod(
            $"JitBlock_{headLinear:X8}_{plans.Length}",
            typeof(int),
            new[] { typeof(XboxCpu), typeof(DecodeCacheEntry[]), typeof(int) },
            typeof(XboxCpu),
            skipVisibility: true);
        ILGenerator il = method.GetILGenerator();

        LocalBuilder context = il.DeclareLocal(typeof(XboxContext));
        var gprs = new LocalBuilder[8];
        for (int i = 0; i < gprs.Length; i++)
        {
            gprs[i] = il.DeclareLocal(typeof(uint));
        }

        LocalBuilder eip = il.DeclareLocal(typeof(uint));
        LocalBuilder flags = il.DeclareLocal(typeof(uint));
        LocalBuilder tsc = il.DeclareLocal(typeof(ulong));
        LocalBuilder served = il.DeclareLocal(typeof(int));
        LocalBuilder a1 = il.DeclareLocal(typeof(uint));
        LocalBuilder a2 = il.DeclareLocal(typeof(uint));
        LocalBuilder res = il.DeclareLocal(typeof(uint));
        LocalBuilder addr = il.DeclareLocal(typeof(uint));
        LocalBuilder vpn = il.DeclareLocal(typeof(uint));
        LocalBuilder phys = il.DeclareLocal(typeof(uint));
        LocalBuilder slot = il.DeclareLocal(typeof(int));
        LocalBuilder ram = il.DeclareLocal(typeof(PhysicalMemory));
        LocalBuilder interruptSource = il.DeclareLocal(typeof(IInterruptSource));
        LocalBuilder vector = il.DeclareLocal(typeof(int));
        Label exit = il.DefineLabel();

        // Prologue: hoist the context and load the cached register state the
        // block actually reads or writes.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, GetContext);
        il.Emit(OpCodes.Stloc, context);
        EmitLoadState(il, context, gprs, eip, flags, tsc, loadMask);

        for (int m = 0; m < plans.Length; m++)
        {
            JitMemberPlan plan = plans[m];

            // Budget cap: served >= budget -> exit.
            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Bge, exit);

            // TSC++ precedes the member body, exactly as the member path.
            il.Emit(OpCodes.Ldloc, tsc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, tsc);

            if (exactBoundaries)
            {
                EmitExactPoll(il, context, gprs, eip, flags, tsc, served, interruptSource, vector,
                    plan.BlockWriteMask);
            }

            EmitMember(il, plan, context, gprs, eip, flags, tsc, served,
                a1, a2, res, addr, vpn, phys, slot, ram, mmioFast);

            if (!exactBoundaries && (plan.Kind == JitKind.Sti || plan.PollAfter))
            {
                // STI (and POPF and IN/OUT) can leave the block with interrupts
                // enabled: the per-block entry poll cannot see that window when
                // a block's entry runs with IF cleared (the kernel idle
                // thread's sti/nop/nop/cli spin is exactly this shape), so poll
                // at the member's boundary — the interpreter would deliver at
                // the very next instruction boundary. The exit advances EIP
                // for every member that does not own it: spec handlers
                // (CallSpec) already advanced Context.Eip; native members and
                // interpreter-call members have not.
                EmitPostPoll(il, context, gprs, eip, flags, tsc, served,
                    interruptSource, vector,
                    advanceEip: !JitPlanner.HandlerOwnsEip(plan.Kind),
                    length: plan.Length,
                    plan.BlockWriteMask);
            }

            if (!plan.IsTerminator && !JitPlanner.HandlerOwnsEip(plan.Kind))
            {
                // Non-terminator members advance EIP after completion
                // (handlers and the emitted bodies advance only on success);
                // CallSpec handlers already advanced Context.Eip themselves.
                il.Emit(OpCodes.Ldloc, eip);
                il.Emit(OpCodes.Ldc_I4, plan.Length);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, eip);
            }

            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, served);
            if (plan.IsTerminator)
            {
                il.Emit(OpCodes.Br, exit);
            }
        }

        il.MarkLabel(exit);
        EmitStoreState(il, context, gprs, eip, flags, tsc, storeMask);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Ret);
        return (JitRun)method.CreateDelegate(typeof(JitRun));
    }

    /// <summary>
    /// Exact mode's per-member IRQ poll: with IF set, acknowledge once; a
    /// pending vector is recorded and the runner exits with the full state
    /// synced at this member's boundary (TSC already incremented). Mirrors
    /// <c>Step</c>/<c>RunSpecBlock</c> order bit for bit.
    /// </summary>
    private static void EmitExactPoll(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder interruptSource,
        LocalBuilder vector,
        uint storeMask)
    {
        Label body = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, GetInterruptSource);
        il.Emit(OpCodes.Stloc, interruptSource);
        il.Emit(OpCodes.Ldloc, interruptSource);
        il.Emit(OpCodes.Brfalse, body);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldc_I4, (int)FlagBits.If);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, body);
        il.Emit(OpCodes.Ldloc, interruptSource);
        il.Emit(OpCodes.Callvirt, Acknowledge);
        il.Emit(OpCodes.Stloc, vector);
        il.Emit(OpCodes.Ldloc, vector);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, body);

        // Pending: record the consumed vector and exit at this boundary.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, vector);
        il.Emit(OpCodes.Stfld, JitPending);
        EmitSync(il, context, gprs, eip, flags, tsc, served, storeMask);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(body);
    }

    /// <summary>
    /// Fast mode's post-member IRQ poll (STI and POPF). Both can leave the
    /// member with interrupts enabled; the interpreter's per-instruction poll
    /// would deliver a pending vector at the very next instruction boundary,
    /// and a block whose only IF=1 window sits inside it would otherwise
    /// starve every wakeup. This poll runs at the member's boundary and exits
    /// like the exact mode's pending path: the member counts as served, the
    /// delivery boundary counts one extra instruction (the caller adds it),
    /// TSC gets the boundary increment, and the saved return address is the
    /// next instruction (<paramref name="advanceEip"/> advances it here for
    /// native members that do not own EIP).
    /// </summary>
    private static void EmitPostPoll(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder interruptSource,
        LocalBuilder vector,
        bool advanceEip,
        int length,
        uint storeMask)
    {
        Label done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, GetInterruptSource);
        il.Emit(OpCodes.Stloc, interruptSource);
        il.Emit(OpCodes.Ldloc, interruptSource);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldc_I4, (int)FlagBits.If);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, interruptSource);
        il.Emit(OpCodes.Callvirt, Acknowledge);
        il.Emit(OpCodes.Stloc, vector);
        il.Emit(OpCodes.Ldloc, vector);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, done);

        // Pending: record the vector, complete the member (EIP advance when
        // native, boundary TSC++, served++), sync the full state and exit.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, vector);
        il.Emit(OpCodes.Stfld, JitPending);
        if (advanceEip)
        {
            il.Emit(OpCodes.Ldloc, eip);
            il.Emit(OpCodes.Ldc_I4, length);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, eip);
        }

        il.Emit(OpCodes.Ldloc, tsc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, tsc);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, served);
        EmitSync(il, context, gprs, eip, flags, tsc, served, storeMask);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(done);
    }

    private static void EmitMember(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram,
        bool mmioFast)
    {
        switch (plan.Kind)
        {
            case JitKind.MovRR:
                EmitReadReg(il, gprs, plan.Size, plan.SecondRegIndex);
                EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, res);
                break;

            case JitKind.MovRI:
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, res);
                break;

            case JitKind.MovLoad:
                EmitLoadMember(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram, mmioFast,
                    loadValue: () =>
                    {
                        EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, res);
                    },
                    writeScratch: res);
                break;

            case JitKind.MovStore:
                EmitStoreMember(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram, mmioFast,
                    emitValue: () => EmitReadReg(il, gprs, plan.Size, plan.RegIndex));
                break;

            case JitKind.FpuLoadReal:
                // C2 slice 3: Fld m32 through the same RAM probe as an integer
                // load; the FPU push is one helper call mirroring ReadFpuReal.
                EmitLoadMember(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram, mmioFast,
                    loadValue: () =>
                    {
                        // The probe leaves the loaded value on the stack;
                        // consume it into the scratch local, then push it
                        // onto the x87 stack.
                        il.Emit(OpCodes.Stloc, res);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldloc, res);
                        il.Emit(OpCodes.Call, JitFpuPushSingle);
                    },
                    writeScratch: res);
                break;

            case JitKind.FpuStoreReal:
            case JitKind.FpuStoreRealPop:
                EmitStoreMember(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram, mmioFast,
                    emitValue: () =>
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Call, JitFpuSingleBits);
                    });
                if (plan.Kind == JitKind.FpuStoreRealPop)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, JitFpuPop);
                }

                break;

            case JitKind.MovStoreImm:
                EmitStoreMember(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram, mmioFast,
                    emitValue: () => il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate)));
                break;

            case JitKind.AluRR:
            case JitKind.AluRI:
                {
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Stloc, a1);
                    if (plan.Kind == JitKind.AluRR)
                    {
                        EmitReadReg(il, gprs, plan.Size, plan.SecondRegIndex);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    }

                    il.Emit(OpCodes.Stloc, a2);
                    EmitAluCompute(il, plan, flags, a1, a2, res);
                    if (plan.AluOp != JitAluOp.Cmp)
                    {
                        il.Emit(OpCodes.Ldloc, res);
                        EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                    }

                    break;
                }

            case JitKind.AluRM:
                {
                    // r op= [m]: load the memory value, then compute in the locals.
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Stloc, a1);
                    Label done = il.DefineLabel();
                    Label slow = il.DefineLabel();
                    Label device = il.DefineLabel();
                    EmitEipStore(il, context, eip);
                    EmitAddress(il, plan, context, gprs, addr);
                    EmitProbe(il, context, isStore: false, plan.Size / 8, addr, vpn, slot, phys, ram, slow,
                        mmioFast ? device : null);
                    EmitRamRead(il, plan.Size, ram, phys);
                    il.Emit(OpCodes.Stloc, a2);
                    il.Emit(OpCodes.Br, done);
                    if (mmioFast)
                    {
                        il.MarkLabel(device);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldloc, phys);
                        il.Emit(OpCodes.Ldc_I4, plan.Size);
                        il.Emit(OpCodes.Call, ReadPhysicalJit);
                        il.Emit(OpCodes.Stloc, a2);
                        il.Emit(OpCodes.Br, done);
                    }

                    il.MarkLabel(slow);
                    EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldloc, addr);
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Call, ReadMemory);
                    il.Emit(OpCodes.Stloc, a2);
                    il.MarkLabel(done);
                    EmitAluCompute(il, plan, flags, a1, a2, res);
                    if (plan.AluOp != JitAluOp.Cmp)
                    {
                        il.Emit(OpCodes.Ldloc, res);
                        EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                    }

                    break;
                }

            case JitKind.AluMR:
            case JitKind.AluMI:
                EmitReadModifyWrite(il, plan, context, gprs, eip, flags, tsc, served, a1, a2, res, addr, vpn, phys,
                    slot, ram, mmioFast);
                break;

            case JitKind.TestRR:
            case JitKind.TestRI:
                {
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Stloc, a1);
                    if (plan.Kind == JitKind.TestRR)
                    {
                        EmitReadReg(il, gprs, plan.Size, plan.SecondRegIndex);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    }

                    il.Emit(OpCodes.Stloc, a2);
                    EmitTestCompute(il, plan.Size, flags, a1, a2);
                    break;
                }

            case JitKind.TestMI:
                {
                    Label done = il.DefineLabel();
                    Label slow = il.DefineLabel();
                    EmitEipStore(il, context, eip);
                    EmitAddress(il, plan, context, gprs, addr);
                    EmitProbe(il, context, isStore: false, plan.Size / 8, addr, vpn, slot, phys, ram, slow);
                    EmitRamRead(il, plan.Size, ram, phys);
                    il.Emit(OpCodes.Stloc, a1);
                    il.Emit(OpCodes.Br, done);
                    il.MarkLabel(slow);
                    EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldloc, addr);
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Call, ReadMemory);
                    il.Emit(OpCodes.Stloc, a1);
                    il.MarkLabel(done);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    il.Emit(OpCodes.Stloc, a2);
                    EmitTestCompute(il, plan.Size, flags, a1, a2);
                    break;
                }

            case JitKind.IncR:
            case JitKind.DecR:
                {
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Ldloc, flags);
                    il.Emit(OpCodes.Ldloca, res);
                    il.Emit(OpCodes.Call, plan.Kind == JitKind.IncR ? AluInc : AluDec);
                    il.Emit(OpCodes.Stloc, flags);
                    il.Emit(OpCodes.Ldloc, res);
                    EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                    break;
                }

            case JitKind.IncM:
            case JitKind.DecM:
                EmitReadModifyWrite(il, plan, context, gprs, eip, flags, tsc, served, a1, a2, res, addr, vpn, phys,
                    slot, ram, mmioFast);
                break;

            case JitKind.Lea:
                // Cannot fault: no EIP store needed.
                EmitAddress(il, plan, context, gprs, addr);
                il.Emit(OpCodes.Ldloc, addr);
                EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                break;

            case JitKind.MovExtend:
                EmitExtend(il, plan, context, gprs, eip, flags, tsc, served, addr, vpn, phys, slot, ram);
                break;

            case JitKind.ShiftRI:
                {
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Stloc, a1);
                    il.Emit(OpCodes.Ldloc, a1);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Ldloc, flags);
                    il.Emit(OpCodes.Ldloca, res);
                    il.Emit(OpCodes.Call, ShiftMethod(plan.ShiftOp));
                    il.Emit(OpCodes.Stloc, flags);
                    il.Emit(OpCodes.Ldloc, res);
                    EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                    break;
                }

            case JitKind.ShiftRC:
                {
                    EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                    il.Emit(OpCodes.Stloc, a1);
                    il.Emit(OpCodes.Ldloc, a1);
                    EmitReadReg(il, gprs, 8, (int)Gpr.Ecx);
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Ldloc, flags);
                    il.Emit(OpCodes.Ldloca, res);
                    il.Emit(OpCodes.Call, ShiftMethod(plan.ShiftOp));
                    il.Emit(OpCodes.Stloc, flags);
                    il.Emit(OpCodes.Ldloc, res);
                    EmitWriteRegFromStack(il, gprs, plan.Size, plan.RegIndex, a1);
                    break;
                }

            case JitKind.PushR:
            case JitKind.PushI:
                EmitPush(il, plan, context, gprs, eip, flags, tsc, served, a1, addr, vpn, phys, slot, ram);
                break;

            case JitKind.PopR:
                EmitPop(il, plan, context, gprs, eip, flags, tsc, served, a1, addr, vpn, phys, slot, ram);
                break;

            case JitKind.CallReg:
            case JitKind.CallMem:
                EmitCallIndirect(il, plan, context, gprs, eip, flags, tsc, served, a1, a2, addr, vpn, phys, slot, ram);
                break;

            case JitKind.CallRel:
                EmitCallRel(il, plan, context, gprs, eip, flags, tsc, served, a2, addr, vpn, phys, slot, ram);
                break;

            case JitKind.JmpMem:
                EmitJmpMem(il, plan, context, gprs, eip, flags, tsc, served, a2, addr, vpn, phys, slot, ram);
                break;

            case JitKind.RetNear:
                EmitRet(il, plan, context, gprs, eip, flags, tsc, served, a1, addr, vpn, phys, slot, ram);
                break;

            case JitKind.JmpReg:
                il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
                il.Emit(OpCodes.Stloc, eip);
                break;

            case JitKind.JmpRel:
                il.Emit(OpCodes.Ldloc, eip);
                il.Emit(OpCodes.Ldc_I4, plan.Length);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, eip);
                break;

            case JitKind.JccRel:
                {
                    Label notTaken = il.DefineLabel();
                    Label done = il.DefineLabel();
                    EmitCondition(il, flags, plan.Condition);
                    il.Emit(OpCodes.Brfalse, notTaken);
                    il.Emit(OpCodes.Ldloc, eip);
                    il.Emit(OpCodes.Ldc_I4, plan.Length);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, eip);
                    il.Emit(OpCodes.Br, done);
                    il.MarkLabel(notTaken);
                    il.Emit(OpCodes.Ldloc, eip);
                    il.Emit(OpCodes.Ldc_I4, plan.Length);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, eip);
                    il.MarkLabel(done);
                    break;
                }

            case JitKind.CallSpec:
            case JitKind.CallInterpreter:
            case JitKind.FpuLoadRegister:
            case JitKind.FpuStoreRegister:
            case JitKind.FpuStoreRegisterPop:
            case JitKind.FpuLoadInteger:
            case JitKind.FpuStoreInteger:
            case JitKind.FpuStoreIntegerPop:
            case JitKind.FpuArithReg:
            case JitKind.FpuArithPop:
            case JitKind.FpuArithMem:
            case JitKind.FpuExchange:
            case JitKind.FpuPushOne:
            case JitKind.FpuChangeSign:
            case JitKind.FpuCompareReg:
            case JitKind.FpuCompareMem:
            case JitKind.PushM:
            case JitKind.Cdq:
            case JitKind.Setcc:
            case JitKind.MmxLoad:
            case JitKind.MmxStore:
            case JitKind.SseArithReg:
            case JitKind.SseArithMem:
            case JitKind.SseUnaryReg:
            case JitKind.SseMovssReg:
            case JitKind.SseMovssMem:
            case JitKind.SseMovssStore:
            case JitKind.SseCvtReg:
            case JitKind.SseCvtMem:
            case JitKind.NegR:
            case JitKind.NotR:
            case JitKind.Bswap:
            case JitKind.XchgRR:
            case JitKind.Leave:
                {
                    // Spec-handler / interpreter fallback: sync the full
                    // context, run the exact handler (the same body the member
                    // path calls), then reload the locals. Faults unwind with
                    // the context already at this member's boundary. CallSpec
                    // handlers own EIP (they advance it); interpreter entry
                    // points do not (the outer loop advances after the
                    // member).
                    EmitEipStore(il, context, eip);
                    if (TallySlowMembers)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, SlowMemberCountsField);
                        il.Emit(OpCodes.Ldc_I4, (int)plan.Mnemonic);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, SlowMemberCountsField);
                        il.Emit(OpCodes.Ldc_I4, (int)plan.Mnemonic);
                        il.Emit(OpCodes.Ldelem_I8);
                        il.Emit(OpCodes.Ldc_I4_1);
                        il.Emit(OpCodes.Conv_I8);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Stelem_I8);
                    }

                    EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, plan.EntryIndex);
                    il.Emit(OpCodes.Ldelema, typeof(DecodeCacheEntry));
                    il.Emit(OpCodes.Ldflda, EntryInstruction);
                    il.Emit(OpCodes.Call, plan.SpecHandler!);
                    EmitLoadState(il, context, gprs, eip, flags, tsc, JitStateMask.All);
                    break;
                }

            case JitKind.Invlpg:
                {
                    // Compute the operand address, sync, drive the interpreter
                    // page flush, reload. The flush only clears caches; the
                    // block stays valid and simply advances.
                    EmitAddress(il, plan, context, gprs, addr);
                    EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Callvirt, GetMmu);
                    il.Emit(OpCodes.Ldloc, addr);
                    il.Emit(OpCodes.Callvirt, FlushPage);
                    EmitLoadState(il, context, gprs, eip, flags, tsc, JitStateMask.All);
                    break;
                }

            case JitKind.Cld:
                il.Emit(OpCodes.Ldloc, flags);
                il.Emit(OpCodes.Ldc_I4, ~(int)FlagBits.Df);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, flags);
                break;

            case JitKind.Std:
                il.Emit(OpCodes.Ldloc, flags);
                il.Emit(OpCodes.Ldc_I4, (int)FlagBits.Df);
                il.Emit(OpCodes.Or);
                il.Emit(OpCodes.Stloc, flags);
                break;

            case JitKind.Cli:
                il.Emit(OpCodes.Ldloc, flags);
                il.Emit(OpCodes.Ldc_I4, ~(int)FlagBits.If);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, flags);
                break;

            case JitKind.Sti:
                il.Emit(OpCodes.Ldloc, flags);
                il.Emit(OpCodes.Ldc_I4, (int)FlagBits.If);
                il.Emit(OpCodes.Or);
                il.Emit(OpCodes.Stloc, flags);
                break;

            case JitKind.Nop:
                break;

            default:
                throw new InvalidOperationException($"No emitter for kind {plan.Kind}.");
        }
    }

    /// <summary>Load-only member: probe + RAM read, converging on the value in <paramref name="writeScratch"/>.</summary>
    private static void EmitLoadMember(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram,
        bool mmioFast,
        Action loadValue,
        LocalBuilder writeScratch)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        Label device = il.DefineLabel();
        EmitEipStore(il, context, eip);
        EmitAddress(il, plan, context, gprs, addr);
        EmitProbe(il, context, isStore: false, plan.Size / 8, addr, vpn, slot, phys, ram, slow,
            mmioFast ? device : null);
        EmitRamRead(il, plan.Size, ram, phys);
        il.Emit(OpCodes.Stloc, writeScratch);
        il.Emit(OpCodes.Ldloc, writeScratch);
        loadValue();
        il.Emit(OpCodes.Br, done);
        if (mmioFast)
        {
            // Device fast path (--jit-mmio): the probe translated this
            // single-page access and holds the physical address, so the
            // interpreter's re-translate is skipped and the access goes
            // straight to the same RawRead the interpreter would reach. No
            // fault is possible (the probe checked the translation), so no
            // context sync is needed.
            il.MarkLabel(device);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, plan.Size);
            il.Emit(OpCodes.Call, ReadPhysicalJit);
            il.Emit(OpCodes.Stloc, writeScratch);
            il.Emit(OpCodes.Ldloc, writeScratch);
            loadValue();
            il.Emit(OpCodes.Br, done);
        }

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, plan.Size);
        il.Emit(OpCodes.Call, ReadMemory);
        loadValue();
        il.MarkLabel(done);
    }

    /// <summary>Store member: probe + RAM write with the watch guard, converging on no value.</summary>
    private static void EmitStoreMember(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram,
        bool mmioFast,
        Action emitValue)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        Label device = il.DefineLabel();
        EmitEipStore(il, context, eip);
        EmitAddress(il, plan, context, gprs, addr);
        EmitProbe(il, context, isStore: true, plan.Size / 8, addr, vpn, slot, phys, ram, slow,
            mmioFast ? device : null);
        EmitWatchGuard(il, plan.Size / 8, phys, slow);
        EmitNoteStore(il, plan.Size, phys);
        EmitRamWrite(il, plan.Size, ram, phys, emitValue);
        il.Emit(OpCodes.Br, done);
        if (mmioFast)
        {
            // Device fast path (--jit-mmio): the probe checked writability and
            // translated the access, so the interpreter's re-translate is
            // skipped and the store goes straight to RawWrite — which keeps
            // the bus watch check (AddressSpace.Write* -> CheckWatch) and the
            // NoteStorePhysical choke exactly as the interpreter would run them.
            il.MarkLabel(device);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, plan.Size);
            emitValue();
            il.Emit(OpCodes.Call, WritePhysicalJit);
            il.Emit(OpCodes.Br, done);
        }

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, plan.Size);
        emitValue();
        il.Emit(OpCodes.Call, WriteMemory);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Read-modify-write member (AluMR/AluMI/IncM/DecM): probe once (the fast
    /// path is only taken when the page is writable and in RAM), read, compute,
    /// NoteStorePhysical and write; the slow path is the exact interpreter
    /// ReadMemory + compute + WriteMemory sequence.
    /// </summary>
    private static void EmitReadModifyWrite(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram,
        bool mmioFast)
    {
        bool isCmp = plan.AluOp == JitAluOp.Cmp;
        bool stores = !isCmp;
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        Label device = il.DefineLabel();
        EmitEipStore(il, context, eip);
        EmitAddress(il, plan, context, gprs, addr);
        EmitProbe(il, context, isStore: stores, plan.Size / 8, addr, vpn, slot, phys, ram, slow,
            mmioFast ? device : null);
        EmitRamRead(il, plan.Size, ram, phys);
        il.Emit(OpCodes.Stloc, a1);
        EmitSecondOperand(il, plan, gprs, a2);
        ComputeRmw(il, plan, flags, a1, a2, res);
        if (stores)
        {
            EmitWatchGuard(il, plan.Size / 8, phys, slow);
            EmitNoteStore(il, plan.Size, phys);
            EmitRamWrite(il, plan.Size, ram, phys, () => il.Emit(OpCodes.Ldloc, res));
        }

        il.Emit(OpCodes.Br, done);
        if (mmioFast)
        {
            // Device fast path (--jit-mmio): read and write through the
            // already-translated physical address (RawRead/RawWrite keep the
            // interpreter's watch and NoteStore semantics); the probe checked
            // writability, so no fault can be skipped.
            il.MarkLabel(device);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, plan.Size);
            il.Emit(OpCodes.Call, ReadPhysicalJit);
            il.Emit(OpCodes.Stloc, a1);
            EmitSecondOperand(il, plan, gprs, a2);
            ComputeRmw(il, plan, flags, a1, a2, res);
            if (stores)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Ldc_I4, plan.Size);
                il.Emit(OpCodes.Ldloc, res);
                il.Emit(OpCodes.Call, WritePhysicalJit);
            }

            il.Emit(OpCodes.Br, done);
        }

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, plan.Size);
        il.Emit(OpCodes.Call, ReadMemory);
        il.Emit(OpCodes.Stloc, a1);
        EmitSecondOperand(il, plan, gprs, a2);
        ComputeRmw(il, plan, flags, a1, a2, res);
        if (stores)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, plan.Size);
            il.Emit(OpCodes.Ldloc, res);
            il.Emit(OpCodes.Call, WriteMemory);
        }

        il.MarkLabel(done);
    }

    /// <summary>Second operand for the RMW compute (register source or baked immediate).</summary>
    private static void EmitSecondOperand(ILGenerator il, JitMemberPlan plan, LocalBuilder[] gprs, LocalBuilder a2)
    {
        switch (plan.Kind)
        {
            case JitKind.AluMR:
                EmitReadReg(il, gprs, plan.Size, plan.RegIndex);
                il.Emit(OpCodes.Stloc, a2);
                break;
            case JitKind.AluMI:
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.Stloc, a2);
                break;
            default:
                break;
        }
    }

    private static void ComputeRmw(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder flags,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res)
    {
        switch (plan.Kind)
        {
            case JitKind.IncM:
            case JitKind.DecM:
                il.Emit(OpCodes.Ldloc, a1);
                il.Emit(OpCodes.Ldc_I4, plan.Size);
                il.Emit(OpCodes.Ldloc, flags);
                il.Emit(OpCodes.Ldloca, res);
                il.Emit(OpCodes.Call, plan.Kind == JitKind.IncM ? AluInc : AluDec);
                il.Emit(OpCodes.Stloc, flags);
                break;
            default:
                EmitAluCompute(il, plan, flags, a1, a2, res);
                break;
        }
    }

    private static void EmitExtend(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        if (plan.HasMemory)
        {
            Label done = il.DefineLabel();
            Label slow = il.DefineLabel();
            EmitEipStore(il, context, eip);
            EmitAddress(il, plan, context, gprs, addr);
            EmitProbe(il, context, isStore: false, plan.SourceSize / 8, addr, vpn, slot, phys, ram, slow);
            EmitRamRead(il, plan.SourceSize, ram, phys);
            EmitExtendValue(il, plan);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(slow);
            EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, plan.SourceSize);
            il.Emit(OpCodes.Call, ReadMemory);
            EmitExtendValue(il, plan);
            il.MarkLabel(done);
        }
        else
        {
            EmitReadReg(il, gprs, plan.SourceSize, plan.SecondRegIndex);
            EmitExtendValue(il, plan);
        }

        EmitWriteRegFromStack(il, gprs, 32, plan.RegIndex, phys);
    }

    private static void EmitExtendValue(ILGenerator il, JitMemberPlan plan)
    {
        // The value is on the stack, masked to its source width by the read;
        // sign-extend via conv.i1/i2, zero-extend via conv.u1/u2.
        il.Emit(plan.SourceSize == 8
            ? plan.SignExtend ? OpCodes.Conv_I1 : OpCodes.Conv_U1
            : plan.SignExtend ? OpCodes.Conv_I2 : OpCodes.Conv_U2);
    }

    private static void EmitPush(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder scratch,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        EmitEipStore(il, context, eip);

        // newEsp = ESP - 4; addr = SS.base + newEsp.
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, scratch);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)SegmentRegister.Ss);
        il.Emit(OpCodes.Call, GetSegmentBase);
        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, addr);

        EmitProbe(il, context, isStore: true, 4, addr, vpn, slot, phys, ram, slow);
        EmitWatchGuard(il, 4, phys, slow);
        EmitNoteStore(il, 32, phys);
        if (plan.Kind == JitKind.PushR)
        {
            EmitRamWrite(il, 32, ram, phys, () => il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]));
        }
        else
        {
            EmitRamWrite(il, 32, ram, phys, () => il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate)));
        }

        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        if (plan.Kind == JitKind.PushR)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
        }

        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, Push);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEsp);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.MarkLabel(done);
    }

    private static void EmitPop(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        EmitEipStore(il, context, eip);

        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)SegmentRegister.Ss);
        il.Emit(OpCodes.Call, GetSegmentBase);
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, addr);

        EmitProbe(il, context, isStore: false, 4, addr, vpn, slot, phys, ram, slow);
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Call, RamRead32);
        il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldloc, a1);
        EmitWriteRegFromStack(il, gprs, 32, plan.RegIndex, a1);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, Pop);
        il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEsp);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldloc, a1);
        EmitWriteRegFromStack(il, gprs, 32, plan.RegIndex, a1);
        il.MarkLabel(done);
    }

    private static void EmitCallRel(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder scratch,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        EmitEipStore(il, context, eip);

        // newEsp = ESP - 4; addr = SS.base + newEsp.
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, scratch);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)SegmentRegister.Ss);
        il.Emit(OpCodes.Call, GetSegmentBase);
        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, addr);

        EmitProbe(il, context, isStore: true, 4, addr, vpn, slot, phys, ram, slow);
        EmitWatchGuard(il, 4, phys, slow);
        EmitNoteStore(il, 32, phys);
        EmitRamWrite(il, 32, ram, phys, () =>
        {
            il.Emit(OpCodes.Ldloc, eip);
            il.Emit(OpCodes.Ldc_I4, plan.Length);
            il.Emit(OpCodes.Add);
        });
        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        EmitJumpTarget(il, plan, eip);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Ldc_I4, plan.Length);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, Push);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEsp);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        EmitJumpTarget(il, plan, eip);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Indirect call (register or memory target): reads the target first
    /// (the interpreter reads the operand before pushing, so a read fault
    /// leaves ESP untouched), then pushes the return address, then sets EIP
    /// — each stage with its own fast/slow pair and exact fault ordering.
    /// </summary>
    private static void EmitCallIndirect(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder scratch,
        LocalBuilder target,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label pushDone = il.DefineLabel();
        Label slowPush = il.DefineLabel();
        EmitEipStore(il, context, eip);

        if (plan.Kind == JitKind.CallMem)
        {
            Label loadDone = il.DefineLabel();
            Label slowLoad = il.DefineLabel();
            EmitAddress(il, plan, context, gprs, addr);
            EmitProbe(il, context, isStore: false, 4, addr, vpn, slot, phys, ram, slowLoad);
            EmitRamRead(il, 32, ram, phys);
            il.Emit(OpCodes.Stloc, target);
            il.Emit(OpCodes.Br, loadDone);
            il.MarkLabel(slowLoad);
            EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Call, ReadMemory);
            il.Emit(OpCodes.Stloc, target);
            il.MarkLabel(loadDone);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.RegIndex]);
            il.Emit(OpCodes.Stloc, target);
        }

        // newEsp = ESP - 4; addr = SS.base + newEsp (CallRel's push sequence).
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, scratch);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)SegmentRegister.Ss);
        il.Emit(OpCodes.Call, GetSegmentBase);
        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, addr);

        EmitProbe(il, context, isStore: true, 4, addr, vpn, slot, phys, ram, slowPush);
        EmitWatchGuard(il, 4, phys, slowPush);
        EmitNoteStore(il, 32, phys);
        EmitRamWrite(il, 32, ram, phys, () =>
        {
            il.Emit(OpCodes.Ldloc, eip);
            il.Emit(OpCodes.Ldc_I4, plan.Length);
            il.Emit(OpCodes.Add);
        });
        il.Emit(OpCodes.Ldloc, scratch);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Br, pushDone);

        il.MarkLabel(slowPush);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Ldc_I4, plan.Length);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, Push);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEsp);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.MarkLabel(pushDone);

        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Stloc, eip);
    }

    /// <summary>Indirect jump through memory (`jmp m32`): load the target, then set EIP.</summary>
    private static void EmitJmpMem(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder target,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        EmitEipStore(il, context, eip);
        EmitAddress(il, plan, context, gprs, addr);
        EmitProbe(il, context, isStore: false, 4, addr, vpn, slot, phys, ram, slow);
        EmitRamRead(il, 32, ram, phys);
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, ReadMemory);
        il.Emit(OpCodes.Stloc, target);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Stloc, eip);
    }

    private static void EmitJumpTarget(ILGenerator il, JitMemberPlan plan, LocalBuilder eip)
    {
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Ldc_I4, plan.Length);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, eip);
    }

    private static void EmitRet(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        LocalBuilder a1,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder phys,
        LocalBuilder slot,
        LocalBuilder ram)
    {
        Label done = il.DefineLabel();
        Label slow = il.DefineLabel();
        EmitEipStore(il, context, eip);

        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)SegmentRegister.Ss);
        il.Emit(OpCodes.Call, GetSegmentBase);
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, addr);

        EmitProbe(il, context, isStore: false, 4, addr, vpn, slot, phys, ram, slow);
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Call, RamRead32);
        il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        if (plan.Immediate != 0)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
            il.Emit(OpCodes.Add);
        }

        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        il.Emit(OpCodes.Ldloc, a1);
        il.Emit(OpCodes.Stloc, eip);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(slow);
        EmitSync(il, context, gprs, eip, flags, tsc, served, plan.BlockWriteMask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Call, Pop);
        il.Emit(OpCodes.Stloc, a1);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Callvirt, GetEsp);
        il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        if (plan.Immediate != 0)
        {
            il.Emit(OpCodes.Ldloc, gprs[(int)Gpr.Esp]);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, gprs[(int)Gpr.Esp]);
        }

        il.Emit(OpCodes.Ldloc, a1);
        il.Emit(OpCodes.Stloc, eip);
        il.MarkLabel(done);
    }

    /// <summary>ALU compute over a1 (first operand) and a2 (second operand), result and flags in locals.</summary>
    private static void EmitAluCompute(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder flags,
        LocalBuilder a1,
        LocalBuilder a2,
        LocalBuilder res)
    {
        switch (plan.AluOp)
        {
            case JitAluOp.Add:
            case JitAluOp.Adc:
            case JitAluOp.Sub:
            case JitAluOp.Sbb:
            case JitAluOp.Cmp:
                {
                    bool add = plan.AluOp is JitAluOp.Add or JitAluOp.Adc;
                    bool carry = plan.AluOp is JitAluOp.Adc or JitAluOp.Sbb;
                    il.Emit(OpCodes.Ldloc, a1);
                    il.Emit(OpCodes.Ldloc, a2);
                    if (carry)
                    {
                        il.Emit(OpCodes.Ldloc, flags);
                        il.Emit(OpCodes.Ldc_I4, (int)FlagBits.Cf);
                        il.Emit(OpCodes.And);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Cgt_Un);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_I4_0);
                    }

                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Ldloc, flags);
                    il.Emit(OpCodes.Ldloca, res);
                    il.Emit(OpCodes.Call, add ? AluAdd : AluSub);
                    il.Emit(OpCodes.Stloc, flags);
                    break;
                }

            default:
                {
                    // And/Or/Xor: compute the masked value, then AluOps.Logical.
                    il.Emit(OpCodes.Ldloc, a1);
                    il.Emit(OpCodes.Ldloc, a2);
                    il.Emit(plan.AluOp switch
                    {
                        JitAluOp.And => OpCodes.And,
                        JitAluOp.Or => OpCodes.Or,
                        _ => OpCodes.Xor,
                    });
                    EmitMaskValue(il, plan.Size);
                    il.Emit(OpCodes.Stloc, res);
                    il.Emit(OpCodes.Ldloc, res);
                    il.Emit(OpCodes.Ldc_I4, plan.Size);
                    il.Emit(OpCodes.Ldloc, flags);
                    il.Emit(OpCodes.Call, AluLogical);
                    il.Emit(OpCodes.Stloc, flags);
                    break;
                }
        }
    }

    private static void EmitTestCompute(
        ILGenerator il,
        int size,
        LocalBuilder flags,
        LocalBuilder a1,
        LocalBuilder a2)
    {
        il.Emit(OpCodes.Ldloc, a1);
        il.Emit(OpCodes.Ldloc, a2);
        il.Emit(OpCodes.And);
        EmitMaskValue(il, size);
        il.Emit(OpCodes.Ldc_I4, size);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Call, AluLogical);
        il.Emit(OpCodes.Stloc, flags);
    }

    private static void EmitMaskValue(ILGenerator il, int size)
    {
        if (size == 8)
        {
            il.Emit(OpCodes.Ldc_I4, 0xFF);
            il.Emit(OpCodes.And);
        }
        else if (size == 16)
        {
            il.Emit(OpCodes.Ldc_I4, 0xFFFF);
            il.Emit(OpCodes.And);
        }
    }

    /// <summary>Emits the effective address of the member's memory operand into <paramref name="addr"/>.</summary>
    private static void EmitAddress(
        ILGenerator il,
        JitMemberPlan plan,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder addr)
    {
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)plan.Segment);
        il.Emit(OpCodes.Call, GetSegmentBase);
        if (plan.HasBase)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.BaseRegister]);
            il.Emit(OpCodes.Add);
        }

        if (plan.HasIndex)
        {
            il.Emit(OpCodes.Ldloc, gprs[plan.IndexRegister]);
            if (plan.Scale != 1)
            {
                il.Emit(OpCodes.Ldc_I4, plan.Scale);
                il.Emit(OpCodes.Mul);
            }

            il.Emit(OpCodes.Add);
        }

        if (plan.Displacement != 0)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Displacement));
            il.Emit(OpCodes.Add);
        }

        il.Emit(OpCodes.Stloc, addr);
    }

    /// <summary>
    /// Emits the inlined fast-access guards: cross-page (when applicable),
    /// translate-cache tag, WP-writability for stores, RAM-direct presence
    /// and bounds. Any failure branches to <paramref name="slow"/>.
    /// </summary>
    private static void EmitProbe(
        ILGenerator il,
        LocalBuilder context,
        bool isStore,
        int sizeBytes,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder slot,
        LocalBuilder phys,
        LocalBuilder ram,
        Label slow,
        Label? device = null,
        bool skipCrossPage = false)
    {
        if (!skipCrossPage && sizeBytes > 1)
        {
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, 0xFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4, 0x1000 - sizeBytes);
            il.Emit(OpCodes.Bgt_Un, slow);
        }

        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Shr_Un);
        il.Emit(OpCodes.Stloc, vpn);
        il.Emit(OpCodes.Ldloc, vpn);
        il.Emit(OpCodes.Ldc_I4, (int)XboxCpu.DataXlatMask);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, slot);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DataXlatTag);
        il.Emit(OpCodes.Ldloc, slot);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, vpn);
        il.Emit(OpCodes.Bne_Un, slow);

        if (isStore)
        {
            Label writable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, DataXlatWritable);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Brtrue, writable);
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Callvirt, GetCr0);
            il.Emit(OpCodes.Ldc_I4, (int)ControlRegisterBits.Cr0WriteProtect);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, slow);
            il.MarkLabel(writable);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DataXlatPhys);
        il.Emit(OpCodes.Ldloc, slot);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 0xFFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, phys);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamDirect);
        il.Emit(OpCodes.Stloc, ram);
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Brfalse, slow);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldc_I4, sizeBytes);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamSize);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Bgt_Un, device ?? slow);
    }

    private static void EmitRamRead(ILGenerator il, int size, LocalBuilder ram, LocalBuilder phys)
    {
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Call, size switch
        {
            8 => RamRead8,
            16 => RamRead16,
            _ => RamRead32,
        });
    }

    private static void EmitRamWrite(
        ILGenerator il,
        int size,
        LocalBuilder ram,
        LocalBuilder phys,
        Action emitValue)
    {
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Ldloc, phys);
        emitValue();
        if (size == 8)
        {
            il.Emit(OpCodes.Conv_U1);
        }
        else if (size == 16)
        {
            il.Emit(OpCodes.Conv_U2);
        }

        il.Emit(OpCodes.Call, size switch
        {
            8 => RamWrite8,
            16 => RamWrite16,
            _ => RamWrite32,
        });
    }

    private static void EmitWatchGuard(ILGenerator il, int sizeBytes, LocalBuilder phys, Label slow)
    {
        // Watched ranges keep the legacy bus path (CheckWatch + WatchHit).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Ldc_I4, sizeBytes);
        il.Emit(OpCodes.Call, IsWatched);
        il.Emit(OpCodes.Brtrue, slow);
    }

    private static void EmitNoteStore(ILGenerator il, int sizeBits, LocalBuilder phys)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Ldc_I4, sizeBits);
        il.Emit(OpCodes.Call, NoteStorePhysical);
    }

    private static void EmitEipStore(ILGenerator il, LocalBuilder context, LocalBuilder eip)
    {
        // Fault precision: the context carries the member EIP while its access
        // runs (handlers advance EIP only after completion).
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Stfld, ContextEip);
    }

    private static void EmitSync(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        LocalBuilder served,
        uint storeMask)
    {
        // The fault path reads the members completed before the fault.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, served);
        il.Emit(OpCodes.Stfld, JitProgress);

        // Diagnostic: count members that took the interpreter fallback.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldfld, JitSlowMembers);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, JitSlowMembers);
        EmitStoreState(il, context, gprs, eip, flags, tsc, storeMask);
    }

    private static void EmitLoadState(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        uint loadMask)
    {
        if (loadMask != 0)
        {
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Ldfld, ContextRegisters);
            for (int i = 0; i < gprs.Length; i++)
            {
                if ((loadMask & (1u << i)) == 0)
                {
                    continue;
                }

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_I4);
                il.Emit(OpCodes.Stloc, gprs[i]);
            }

            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextEip);
        il.Emit(OpCodes.Stloc, eip);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextEflags);
        il.Emit(OpCodes.Stloc, flags);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextTsc);
        il.Emit(OpCodes.Stloc, tsc);
    }

    private static void EmitStoreState(
        ILGenerator il,
        LocalBuilder context,
        LocalBuilder[] gprs,
        LocalBuilder eip,
        LocalBuilder flags,
        LocalBuilder tsc,
        uint storeMask)
    {
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, eip);
        il.Emit(OpCodes.Stfld, ContextEip);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Stfld, ContextEflags);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, tsc);
        il.Emit(OpCodes.Stfld, ContextTsc);
        if (storeMask == 0)
        {
            return;
        }

        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldfld, ContextRegisters);
        for (int i = 0; i < gprs.Length; i++)
        {
            if ((storeMask & (1u << i)) == 0)
            {
                continue;
            }

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldloc, gprs[i]);
            il.Emit(OpCodes.Stelem_I4);
        }

        il.Emit(OpCodes.Pop);
    }

    /// <summary>Pushes the value of one register (size-masked) onto the IL stack.</summary>
    private static void EmitReadReg(ILGenerator il, LocalBuilder[] gprs, int size, int index)
    {
        switch (size)
        {
            case 8:
                if (index < 4)
                {
                    il.Emit(OpCodes.Ldloc, gprs[index]);
                    il.Emit(OpCodes.Ldc_I4, 0xFF);
                    il.Emit(OpCodes.And);
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, gprs[index & 3]);
                    il.Emit(OpCodes.Ldc_I4, 8);
                    il.Emit(OpCodes.Shr_Un);
                    il.Emit(OpCodes.Ldc_I4, 0xFF);
                    il.Emit(OpCodes.And);
                }

                break;
            case 16:
                il.Emit(OpCodes.Ldloc, gprs[index]);
                il.Emit(OpCodes.Ldc_I4, 0xFFFF);
                il.Emit(OpCodes.And);
                break;
            default:
                il.Emit(OpCodes.Ldloc, gprs[index]);
                break;
        }
    }

    /// <summary>
    /// Pops a value and writes it into one register with partial-write
    /// semantics identical to <c>XboxContext.WriteRegister8/16/32</c>.
    /// </summary>
    private static void EmitWriteRegFromStack(
        ILGenerator il,
        LocalBuilder[] gprs,
        int size,
        int index,
        LocalBuilder scratch)
    {
        switch (size)
        {
            case 8:
                {
                    il.Emit(OpCodes.Stloc, scratch);
                    int target = index & 3;
                    il.Emit(OpCodes.Ldloc, gprs[target]);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(index < 4 ? 0xFFFFFF00u : 0xFFFF00FFu)));
                    il.Emit(OpCodes.And);
                    il.Emit(OpCodes.Ldloc, scratch);
                    il.Emit(OpCodes.Ldc_I4, 0xFF);
                    il.Emit(OpCodes.And);
                    if (index >= 4)
                    {
                        il.Emit(OpCodes.Ldc_I4, 8);
                        il.Emit(OpCodes.Shl);
                    }

                    il.Emit(OpCodes.Or);
                    il.Emit(OpCodes.Stloc, gprs[target]);
                    break;
                }

            case 16:
                il.Emit(OpCodes.Stloc, scratch);
                il.Emit(OpCodes.Ldloc, gprs[index]);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)0xFFFF0000u));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Ldloc, scratch);
                il.Emit(OpCodes.Ldc_I4, 0xFFFF);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Or);
                il.Emit(OpCodes.Stloc, gprs[index]);
                break;
            default:
                il.Emit(OpCodes.Stloc, gprs[index]);
                break;
        }
    }

    private static MethodInfo ShiftMethod(JitShiftOp op)
    {
        return op switch
        {
            JitShiftOp.Shl => ShiftShl,
            JitShiftOp.Shr => ShiftShr,
            JitShiftOp.Sar => ShiftSar,
            JitShiftOp.Rol => ShiftRol,
            JitShiftOp.Ror => ShiftRor,
            JitShiftOp.Rcl => ShiftRcl,
            _ => ShiftRcr,
        };
    }

    /// <summary>
    /// Emits the flag test for one Jcc condition as an int (0/1) on the
    /// stack, mirroring <c>XboxCpu.EvaluateCondition</c> bit for bit.
    /// </summary>
    private static void EmitCondition(ILGenerator il, LocalBuilder flags, ConditionCode condition)
    {
        switch (condition)
        {
            case ConditionCode.Overflow:
                EmitFlag(il, flags, FlagBits.Of);
                break;
            case ConditionCode.NotOverflow:
                EmitFlag(il, flags, FlagBits.Of);
                EmitNot(il);
                break;
            case ConditionCode.Below:
                EmitFlag(il, flags, FlagBits.Cf);
                break;
            case ConditionCode.AboveOrEqual:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitNot(il);
                break;
            case ConditionCode.Equal:
                EmitFlag(il, flags, FlagBits.Zf);
                break;
            case ConditionCode.NotEqual:
                EmitFlag(il, flags, FlagBits.Zf);
                EmitNot(il);
                break;
            case ConditionCode.BelowOrEqual:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                break;
            case ConditionCode.Above:
                EmitFlag(il, flags, FlagBits.Cf);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                EmitNot(il);
                break;
            case ConditionCode.Sign:
                EmitFlag(il, flags, FlagBits.Sf);
                break;
            case ConditionCode.NotSign:
                EmitFlag(il, flags, FlagBits.Sf);
                EmitNot(il);
                break;
            case ConditionCode.Parity:
                EmitFlag(il, flags, FlagBits.Pf);
                break;
            case ConditionCode.NotParity:
                EmitFlag(il, flags, FlagBits.Pf);
                EmitNot(il);
                break;
            case ConditionCode.Less:
                EmitSignXorOverflow(il, flags);
                break;
            case ConditionCode.GreaterOrEqual:
                EmitSignXorOverflow(il, flags);
                EmitNot(il);
                break;
            case ConditionCode.LessOrEqual:
                EmitSignXorOverflow(il, flags);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                break;
            default:
                // Greater: !zf && sf == of.
                EmitSignXorOverflow(il, flags);
                EmitFlag(il, flags, FlagBits.Zf);
                il.Emit(OpCodes.Or);
                EmitNot(il);
                break;
        }
    }

    private static void EmitSignXorOverflow(ILGenerator il, LocalBuilder flags)
    {
        EmitFlag(il, flags, FlagBits.Sf);
        EmitFlag(il, flags, FlagBits.Of);
        il.Emit(OpCodes.Xor);
    }

    private static void EmitFlag(ILGenerator il, LocalBuilder flags, uint bit)
    {
        il.Emit(OpCodes.Ldloc, flags);
        il.Emit(OpCodes.Ldc_I4, (int)bit);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
    }

    private static void EmitNot(ILGenerator il)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Xor);
    }
}
