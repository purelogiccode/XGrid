using System;
using System.Reflection;
using System.Reflection.Emit;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>
/// First-codegen slice (§171, DynamicMethod per §138): builds an unrolled
/// per-block tail runner for fully-specialized blocks. The emitted code serves
/// members 1..N-1 of one block (member 0 always runs through the existing
/// member path, preserving head lookup and counting). Every original boundary
/// is retained per member: generation check (self-modification), EIP check
/// (redirects), TSC increment, IRQ poll at the Step site (a pending IRQ bails
/// to the C# transfer path with the consumed vector), and exact fault EIP
/// (handlers advance EIP only after completion; faults propagate through the
/// emitted frames to the wrapper's catch, which replays the Step catch body).
/// The wrapper skips emission whenever observers, idle-skip, or either verify
/// mode is active, so those paths stay byte-identical by construction.
/// </summary>
internal delegate void EmitTailRunner(
    XboxCpu cpu,
    DecodeCacheEntry[] entries,
    int[] progress,
    int max,
    ulong generation);

/// <summary>Builds <see cref="EmitTailRunner"/> delegates for emittable block tails.</summary>
internal static class BlockEmitter
{
    /// <summary>
    /// Progress slots: [0] members served, [1] pending IRQ vector (-1 none),
    /// [2] redirect flag (EIP mismatch).
    /// </summary>
    internal const int ProgressServed = 0;

    /// <summary>Progress slot for the consumed pending IRQ vector.</summary>
    internal const int ProgressPending = 1;

    /// <summary>Progress slot for the EIP-redirect flag.</summary>
    internal const int ProgressRedirect = 2;

    /// <summary>Progress array length.</summary>
    internal const int ProgressLength = 3;

    private static readonly MethodInfo GetBlockGeneration =
        typeof(XboxCpu).GetProperty(
            "BlockGeneration",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetMethod!;

    private static readonly MethodInfo GetContext =
        typeof(XboxCpu).GetProperty("Context")!.GetMethod!;

    private static readonly MethodInfo GetEip =
        typeof(XboxContext).GetProperty("Eip")!.GetMethod!;

    private static readonly MethodInfo SetEip =
        typeof(XboxContext).GetProperty("Eip")!.SetMethod!;

    private static readonly MethodInfo GetMmu =
        typeof(XboxCpu).GetProperty("Mmu")!.GetMethod!;

    private static readonly MethodInfo GetMmuObserver =
        typeof(Mmu).GetProperty("Observer")!.GetMethod!;

    private static readonly MethodInfo GetCr0 =
        typeof(XboxContext).GetProperty("Cr0")!.GetMethod!;

    private static readonly MethodInfo SetEflags =
        typeof(XboxContext).GetProperty("Eflags")!.SetMethod!;

    private static readonly MethodInfo AluSub =
        typeof(AluOps).GetMethod("Sub")!;

    private static readonly MethodInfo AluLogical =
        typeof(AluOps).GetMethod("Logical")!;

    private static readonly MethodInfo ReadRegister8 =
        typeof(XboxContext).GetMethod("ReadRegister8")!;

    private static readonly MethodInfo WriteRegister8 =
        typeof(XboxContext).GetMethod("WriteRegister8")!;

    private static readonly MethodInfo GetSegmentBase =
        typeof(XboxContext).GetMethod("GetSegmentBase")!;

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

    private static readonly MethodInfo IsWatched =
        typeof(XboxCpu).GetMethod("IsWatched", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo NoteStorePhysical =
        typeof(XboxCpu).GetMethod("NoteStorePhysical", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo RamRead32 =
        typeof(PhysicalMemory).GetMethod("Read32")!;

    private static readonly MethodInfo RamWrite32 =
        typeof(PhysicalMemory).GetMethod("Write32")!;

    private static readonly MethodInfo RamRead8 =
        typeof(PhysicalMemory).GetMethod("Read8")!;

    private static readonly MethodInfo RamWrite8 =
        typeof(PhysicalMemory).GetMethod("Write8")!;

    private static readonly FieldInfo InlineFastHitsField =
        typeof(XboxCpu).GetField("InlineFastHits", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo InlineTagMissesField =
        typeof(XboxCpu).GetField("InlineTagMisses", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo GetTsc =
        typeof(XboxContext).GetProperty("Tsc")!.GetMethod!;

    private static readonly MethodInfo SetTsc =
        typeof(XboxContext).GetProperty("Tsc")!.SetMethod!;

    private static readonly MethodInfo GetEflags =
        typeof(XboxContext).GetProperty("Eflags")!.GetMethod!;

    private static readonly MethodInfo GetInterruptSource =
        typeof(XboxCpu).GetProperty("InterruptSource")!.GetMethod!;

    private static readonly MethodInfo Acknowledge =
        typeof(IInterruptSource).GetMethod("Acknowledge")!;

    private static readonly FieldInfo EntryInstruction =
        typeof(DecodeCacheEntry).GetField("Instruction")!;

    /// <summary>
    /// Builds a tail runner for members 1..N-1 of one fully-specialized block,
    /// or null when any tail member lacks a resolved static handler.
    /// </summary>
    /// <param name="headLinear">Block head linear address (diagnostic name only).</param>
    /// <param name="entries">The block's live entry array (shared lifetime).</param>
    /// <param name="handlers">The block's resolved handlers (all non-null).</param>
    /// <param name="linears">Expected linear PC per member.</param>
    /// <param name="inlineBodies">True to serve members with a resolved E3-1 inline twin (context-hoisted lean body) where one exists.</param>
    /// <param name="inlineCounters">True to bake E3-1d diagnostic counters into the fast paths.</param>
    /// <param name="inlineMembers">Receives the number of members baked onto the inline path.</param>
    /// <param name="fastMembers">Receives the number of members baked onto the E3-1b IL fast path.</param>
    /// <returns>The runner, or null for non-emittable tails.</returns>
    internal static EmitTailRunner? Build(
        uint headLinear,
        DecodeCacheEntry[] entries,
        XboxCpu.SpecHandler?[] handlers,
        uint[] linears,
        bool inlineBodies,
        bool inlineCounters,
        out int inlineMembers,
        out int fastMembers)
    {
        inlineMembers = 0;
        fastMembers = 0;
        if (entries.Length < 2 || entries.Length != linears.Length || entries.Length != handlers.Length)
        {
            return null;
        }

        for (int i = 1; i < handlers.Length; i++)
        {
            if (handlers[i] is null || handlers[i]!.Target is not null)
            {
                // Null shapes keep the interpreter; a closed delegate (bound
                // target) can never be a direct static call, so it also falls
                // back instead of baking a wrong call. Both are exact.
                return null;
            }

            MethodInfo target = handlers[i]!.Method;
            if (!target.IsStatic || target.ReturnType != typeof(void)
                || target.GetParameters().Length != 2)
            {
                return null;
            }
        }

        // E3-1: resolve the inline plan per member at build time (null keeps
        // the plain handler call). Each plan carries the exact twin body plus
        // the baked fast-path descriptor; a twin whose signature is not a
        // direct static call falls back instead of baking a bad call.
        var plans = new XboxCpu.InlineFastPlan?[entries.Length];
        bool anyFast = false;
        if (inlineBodies)
        {
            for (int i = 1; i < entries.Length; i++)
            {
                XboxCpu.InlineFastPlan? plan = XboxCpu.ResolveInlinePlan(entries[i].Instruction);
                if (plan is null)
                {
                    continue;
                }

                MethodInfo candidate = plan.Value.Body.Method;
                if (candidate.IsStatic && candidate.ReturnType == typeof(void)
                    && candidate.GetParameters().Length == 3)
                {
                    plans[i] = plan;
                    if (plan.Value.Kind != XboxCpu.InlineFastKind.None)
                    {
                        anyFast = true;
                    }
                }
            }
        }

        var method = new DynamicMethod(
            $"EmitTail_{headLinear:X8}_{entries.Length}",
            typeof(void),
            new[] { typeof(XboxCpu), typeof(DecodeCacheEntry[]), typeof(int[]), typeof(int), typeof(ulong) },
            typeof(XboxCpu),
            skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder served = il.DeclareLocal(typeof(int));
        LocalBuilder source = il.DeclareLocal(typeof(IInterruptSource));
        LocalBuilder vector = il.DeclareLocal(typeof(int));
        LocalBuilder context = il.DeclareLocal(typeof(XboxContext));
        Label end = il.DefineLabel();
        Label redirect = il.DefineLabel();

        // Per-invocation hoists: the context never changes mid-block
        // (segment reloads and mode changes flush). InterruptSource stays a
        // live per-member read.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, GetContext);
        il.Emit(OpCodes.Stloc, context);

        // E3-1b fast-path locals and the per-invocation observer gate: the
        // data-translate cache is only consulted while Mmu.Observer is null
        // (TranslateLinear's FastWideAllowed; its hotspot/idle halves are
        // guaranteed by the RunEmitTail guard). Hoisting per invocation is
        // sound: no specialized member can attach an observer, and a tail
        // runs to completion synchronously.
        LocalBuilder? fastAddr = null;
        LocalBuilder? fastVpn = null;
        LocalBuilder? fastSlot = null;
        LocalBuilder? fastPhys = null;
        LocalBuilder? fastValue = null;
        LocalBuilder? fastAux = null;
        LocalBuilder? fastRam = null;
        LocalBuilder? fastOk = null;
        if (anyFast)
        {
            fastAddr = il.DeclareLocal(typeof(uint));
            fastVpn = il.DeclareLocal(typeof(uint));
            fastSlot = il.DeclareLocal(typeof(int));
            fastPhys = il.DeclareLocal(typeof(uint));
            fastValue = il.DeclareLocal(typeof(uint));
            fastAux = il.DeclareLocal(typeof(uint));
            fastRam = il.DeclareLocal(typeof(PhysicalMemory));
            fastOk = il.DeclareLocal(typeof(bool));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, GetMmu);
            il.Emit(OpCodes.Callvirt, GetMmuObserver);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Stloc, fastOk);
        }

        for (int j = 1; j < entries.Length; j++)
        {
            Label body = il.DefineLabel();

            // Budget cap: served >= max -> end.
            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Bge, end);

            // Self-modification: live generation != build generation -> end.
            // (Invalidation already dropped the cursor and counted; just stop.)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, GetBlockGeneration);
            il.Emit(OpCodes.Ldarg_S, (byte)4);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, end);

            // Redirect: EIP != baked member linear -> redirect exit.
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Callvirt, GetEip);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)linears[j]));
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, redirect);

            // TSC++ before the poll, exactly as the member path.
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Callvirt, GetTsc);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, SetTsc);

            // IRQ poll at the Step site.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, GetInterruptSource);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Brfalse, body);
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Callvirt, GetEflags);
            il.Emit(OpCodes.Ldc_I4, (int)FlagBits.If);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, body);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Callvirt, Acknowledge);
            il.Emit(OpCodes.Stloc, vector);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, body);

            // Pending: report the consumed vector; served count is current.
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Stelem_I4);
            il.Emit(OpCodes.Ret);

            // Semantic body: direct static call on the member instruction in
            // place (entry address + instruction field address, no copies),
            // with no delegate indirection. E3-1 members take the resolved
            // inline twin (context hoisted into the runner); E3-1b members
            // first try the baked fast path and fall back to the twin on any
            // non-fast case (16-bit addressing, cross-page, cache miss,
            // non-RAM, watched or unwritable stores).
            il.MarkLabel(body);
            if (plans[j] is { } plan)
            {
                if (plan.Kind != XboxCpu.InlineFastKind.None && fastOk is not null)
                {
                    Label slow = il.DefineLabel();
                    Label done = il.DefineLabel();
                    EmitFastMemoryPath(il, plan, context, fastOk, slow, inlineCounters,
                        fastAddr!, fastVpn!, fastSlot!, fastPhys!, fastValue!, fastAux!, fastRam!);
                    il.Emit(OpCodes.Br, done);
                    il.MarkLabel(slow);
                    EmitTwinCall(il, plan.Body, context, j);
                    il.MarkLabel(done);
                    fastMembers++;
                }
                else
                {
                    EmitTwinCall(il, plan.Body, context, j);
                }

                inlineMembers++;
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, j);
                il.Emit(OpCodes.Ldelema, typeof(DecodeCacheEntry));
                il.Emit(OpCodes.Ldflda, EntryInstruction);
                il.EmitCall(OpCodes.Call, (MethodInfo)handlers[j]!.Method, null);
            }

            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, served);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, served);
            il.Emit(OpCodes.Stelem_I4);
        }

        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(redirect);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stelem_I4);
        il.Emit(OpCodes.Ret);

        return (EmitTailRunner)method.CreateDelegate(typeof(EmitTailRunner));
    }

    private static void EmitTwinCall(ILGenerator il, XboxCpu.SpecInlineHandler body, LocalBuilder context, int member)
    {
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, member);
        il.Emit(OpCodes.Ldelema, typeof(DecodeCacheEntry));
        il.Emit(OpCodes.Ldflda, EntryInstruction);
        il.EmitCall(OpCodes.Call, body.Method, null);
    }

    /// <summary>
    /// E3-1b fast path for a baked 32-bit Mov register/memory member: inline
    /// effective-address compute, data-translate cache probe, RAM-direct
    /// read/write and the register/EIP updates. Every non-fast case branches
    /// to <paramref name="slow"/>, which runs the exact twin body. The fast
    /// path performs no observable side effect the twin would not perform
    /// for the same case: a translate-cache hit has no translation side
    /// effects in the legacy path either, and stores keep IsWatched,
    /// NoteStorePhysical and the RAM write in RawWrite's order.
    /// </summary>
    private static void EmitFastMemoryPath(
        ILGenerator il,
        XboxCpu.InlineFastPlan plan,
        LocalBuilder context,
        LocalBuilder fastOk,
        Label slow,
        bool inlineCounters,
        LocalBuilder addr,
        LocalBuilder vpn,
        LocalBuilder slot,
        LocalBuilder phys,
        LocalBuilder value,
        LocalBuilder aux,
        LocalBuilder ram)
    {
        il.Emit(OpCodes.Ldloc, fastOk);
        il.Emit(OpCodes.Brfalse, slow);

        // addr = segBase(segment) + base + index*scale + disp
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldc_I4, (int)plan.Segment);
        il.Emit(OpCodes.Call, GetSegmentBase);
        if (plan.BaseRegister >= 0)
        {
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Ldfld, ContextRegisters);
            il.Emit(OpCodes.Ldc_I4, plan.BaseRegister);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Add);
        }

        if (plan.IndexRegister >= 0)
        {
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Ldfld, ContextRegisters);
            il.Emit(OpCodes.Ldc_I4, plan.IndexRegister);
            il.Emit(OpCodes.Ldelem_I4);
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

        // Cross-page accesses keep the interpreter's per-byte order via the
        // twin. Only 32-bit accesses can cross a page boundary.
        if (plan.Size == 32)
        {
            il.Emit(OpCodes.Ldloc, addr);
            il.Emit(OpCodes.Ldc_I4, 0xFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4, 0xFFC);
            il.Emit(OpCodes.Bgt_Un, slow);
        }

        // vpn = addr >> 12; slot = vpn & DataXlatMask; cache tag must match.
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
        if (inlineCounters)
        {
            Label tagOk = il.DefineLabel();
            il.Emit(OpCodes.Beq, tagOk);
            EmitCounterIncrement(il, InlineTagMissesField);
            il.Emit(OpCodes.Br, slow);
            il.MarkLabel(tagOk);
        }
        else
        {
            il.Emit(OpCodes.Bne_Un, slow);
        }

        // Stores: the WP-gated unwritable case must fault through the twin
        // (TranslateLinear's cached-hit replay), so it takes the slow path.
        if (plan.IsStore)
        {
            Label writable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, DataXlatWritable);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Brtrue, writable);
            il.Emit(OpCodes.Ldloc, context);
            il.Emit(OpCodes.Call, GetCr0);
            il.Emit(OpCodes.Ldc_I4, (int)ControlRegisterBits.Cr0WriteProtect);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, slow);
            il.MarkLabel(writable);
        }

        // phys = cached page base + page offset.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, DataXlatPhys);
        il.Emit(OpCodes.Ldloc, slot);
        il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc, addr);
        il.Emit(OpCodes.Ldc_I4, 0xFFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, phys);

        // RAM-direct only: non-RAM or out-of-bounds goes through the twin.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamDirect);
        il.Emit(OpCodes.Stloc, ram);
        il.Emit(OpCodes.Ldloc, ram);
        il.Emit(OpCodes.Brfalse, slow);
        il.Emit(OpCodes.Ldloc, phys);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldc_I4, plan.Size / 8);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, RamSize);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Bgt_Un, slow);

        if (plan.IsStore)
        {
            // Watched ranges keep the legacy bus path (CheckWatch + WatchHit).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, phys);
            il.Emit(OpCodes.Ldc_I4, plan.Size / 8);
            il.Emit(OpCodes.Call, IsWatched);
            il.Emit(OpCodes.Brtrue, slow);
        }

        EmitFastTail(il, plan, context, value, aux, ram, phys);

        if (inlineCounters)
        {
            EmitCounterIncrement(il, InlineFastHitsField);
        }

        // EIP advances only after completion, exactly as the handler.
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Call, GetEip);
        il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Length));
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, SetEip);
    }

    /// <summary>
    /// Kind-specific tail of the E3-1b/E3-1c fast path, after the address,
    /// translate-cache, RAM and store-guard checks passed. Each case mirrors
    /// its spec handler's body with the operand facts baked.
    /// </summary>
    private static void EmitFastTail(
        ILGenerator il,
        XboxCpu.InlineFastPlan plan,
        LocalBuilder context,
        LocalBuilder value,
        LocalBuilder aux,
        LocalBuilder ram,
        LocalBuilder phys)
    {
        switch (plan.Kind)
        {
            case XboxCpu.InlineFastKind.MovLoad32:
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead32);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldfld, ContextRegisters);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Stelem_I4);
                break;

            case XboxCpu.InlineFastKind.MovLoad8:
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead8);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Call, WriteRegister8);
                break;

            case XboxCpu.InlineFastKind.MovStore32:
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldfld, ContextRegisters);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Ldelem_I4);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Ldc_I4, 32);
                il.Emit(OpCodes.Call, NoteStorePhysical);
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Call, RamWrite32);
                break;

            case XboxCpu.InlineFastKind.MovStore8:
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Call, ReadRegister8);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Ldc_I4, 8);
                il.Emit(OpCodes.Call, NoteStorePhysical);
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Call, RamWrite8);
                break;

            case XboxCpu.InlineFastKind.CmpRegMem32:
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldfld, ContextRegisters);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Ldelem_I4);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead32);
                il.Emit(OpCodes.Stloc, aux);
                EmitFastCmpFlags(il, context, value, aux, aux);
                break;

            case XboxCpu.InlineFastKind.CmpMemReg32:
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead32);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldfld, ContextRegisters);
                il.Emit(OpCodes.Ldc_I4, plan.RegisterIndex);
                il.Emit(OpCodes.Ldelem_I4);
                il.Emit(OpCodes.Stloc, aux);
                EmitFastCmpFlags(il, context, value, aux, aux);
                break;

            case XboxCpu.InlineFastKind.CmpMemImm32:
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead32);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_I4, 32);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Call, GetEflags);
                il.Emit(OpCodes.Ldloca, aux);
                il.Emit(OpCodes.Call, AluSub);
                il.Emit(OpCodes.Call, SetEflags);
                break;

            case XboxCpu.InlineFastKind.TestMemImm32:
                il.Emit(OpCodes.Ldloc, ram);
                il.Emit(OpCodes.Ldloc, phys);
                il.Emit(OpCodes.Call, RamRead32);
                il.Emit(OpCodes.Stloc, value);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)plan.Immediate));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Ldc_I4, 32);
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Call, GetEflags);
                il.Emit(OpCodes.Call, AluLogical);
                il.Emit(OpCodes.Call, SetEflags);
                break;

            default:
                throw new InvalidOperationException($"No fast tail for kind {plan.Kind}.");
        }
    }

    /// <summary>
    /// Emits <c>cpu.field++</c> for an E3-1d diagnostic counter (int64
    /// instance field on the CPU).
    /// </summary>
    private static void EmitCounterIncrement(ILGenerator il, FieldInfo field)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, field);
    }

    /// <summary>
    /// Emits <c>eflags = AluOps.Sub(first, second, 0, 32, eflags, out _)</c>
    /// with <paramref name="scratch"/> as the discarded result local.
    /// </summary>
    private static void EmitFastCmpFlags(
        ILGenerator il, LocalBuilder context, LocalBuilder first, LocalBuilder second, LocalBuilder scratch)
    {
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Ldloc, first);
        il.Emit(OpCodes.Ldloc, second);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldloc, context);
        il.Emit(OpCodes.Call, GetEflags);
        il.Emit(OpCodes.Ldloca, scratch);
        il.Emit(OpCodes.Call, AluSub);
        il.Emit(OpCodes.Call, SetEflags);
    }
}
