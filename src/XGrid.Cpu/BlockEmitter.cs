using System.Reflection;
using System.Reflection.Emit;
using XGrid.Core.Devices;

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
    /// <param name="inlineMembers">Receives the number of members baked onto the inline path.</param>
    /// <returns>The runner, or null for non-emittable tails.</returns>
    internal static EmitTailRunner? Build(
        uint headLinear,
        DecodeCacheEntry[] entries,
        XboxCpu.SpecHandler?[] handlers,
        uint[] linears,
        bool inlineBodies,
        out int inlineMembers)
    {
        inlineMembers = 0;
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

        // E3-1: resolve the inline twin per member at build time (null keeps
        // the plain handler call). The twin is validated like the handler so
        // an unexpected signature falls back instead of baking a bad call.
        var inline = new MethodInfo?[entries.Length];
        if (inlineBodies)
        {
            for (int i = 1; i < entries.Length; i++)
            {
                XboxCpu.SpecInlineHandler? twin = XboxCpu.ResolveInlineHandler(entries[i].Instruction);
                if (twin is null)
                {
                    continue;
                }

                MethodInfo candidate = twin.Method;
                if (candidate.IsStatic && candidate.ReturnType == typeof(void)
                    && candidate.GetParameters().Length == 3)
                {
                    inline[i] = candidate;
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
            // inline twin (context hoisted into the runner).
            il.MarkLabel(body);
            if (inline[j] is { } inlineMethod)
            {
                il.Emit(OpCodes.Ldloc, context);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, j);
                il.Emit(OpCodes.Ldelema, typeof(DecodeCacheEntry));
                il.Emit(OpCodes.Ldflda, EntryInstruction);
                il.EmitCall(OpCodes.Call, inlineMethod, null);
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
}
