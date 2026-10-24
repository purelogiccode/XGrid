using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace XGrid.Cpu;

/// <summary>
/// Static per-block profile captured at formation (diagnostic, D8): the host
/// instruction count of each emitted region and the member class composition.
/// A block is straight-line, so these are compile-time facts about every
/// execution of it; multiplying by the invocation/edge counts turns them into
/// the engine's host-instruction census without a Stopwatch or a counter in
/// the emitted code.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct JitBlockStatics
{
    /// <summary>Gets the emitted host instructions of the external entry (per entry).</summary>
    public int PrologueInstructions { get; init; }

    /// <summary>Gets the emitted host instructions of the in-frame chain entry (per chained edge).</summary>
    public int ChainEntryInstructions { get; init; }

    /// <summary>Gets the emitted host instructions of the member bodies (per block execution).</summary>
    public int MemberInstructions { get; init; }

    /// <summary>Gets the emitted host instructions of the exit edge (publish + chain attempt + return).</summary>
    public int ExitInstructions { get; init; }

    /// <summary>Gets the members that read guest memory.</summary>
    public int LoadMembers { get; init; }

    /// <summary>Gets the members that write guest memory.</summary>
    public int StoreMembers { get; init; }

    /// <summary>Gets the register-only ALU/shift/move members.</summary>
    public int AluMembers { get; init; }

    /// <summary>Gets the control-flow members (jcc/jmp/call/ret).</summary>
    public int BranchMembers { get; init; }

    /// <summary>Gets the members of every other class (x87/MMX/SSE/string/system).</summary>
    public int OtherMembers { get; init; }

    /// <summary>Gets the class index holding the most members (0 load, 1 store, 2 alu, 3 branch, 4 other).</summary>
    public byte DominantClass { get; init; }
}

/// <summary>
/// D8 engine attribution (<c>--jit-timing</c> only): where the fast engine's
/// host time goes, in the cells the M-D engine decision needs.
/// <para>
/// Four tables: (1) the runner split by block member count and by member class,
/// (2) the edge census - chained edges vs entries, members per edge and the
/// static host instruction count of every emitted region, (3) the formation
/// split (Iced <c>Assemble</c> vs emitter body vs IL build vs head lookup) and
/// the heads it forms by fetch source, (4) the head-lookup share that never
/// forms (heads below the build threshold).
/// </para>
/// <para>
/// Every counter is written from the existing once-per-invocation accounting
/// site, so enabling it costs two timestamp calls per entry and a switch per
/// invocation - never a Stopwatch per member, and never a counter in the
/// emitted code. The member-class <em>time</em> split attributes a whole
/// invocation to the class holding most of its members (a documented
/// estimator: a block executes all of its members); the member counts
/// themselves are exact. Nothing here is guest-visible.
/// </para>
/// </summary>
internal sealed class JitAttribution
{
    private static readonly double NanosecondsPerTick = 1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Runner ticks by block member count (1-4, 5-8, 9+).</summary>
    private readonly long[] _lengthTicks = new long[3];

    /// <summary>Runner invocations by block member count.</summary>
    private readonly long[] _lengthInvocations = new long[3];

    /// <summary>Members served by block member count.</summary>
    private readonly long[] _lengthMembers = new long[3];

    /// <summary>Runner ticks by dominant member class (0 load, 1 store, 2 alu, 3 branch, 4 other).</summary>
    private readonly long[] _classTicks = new long[5];

    /// <summary>Members served by blocks whose dominant class is this one (measured ns per member).</summary>
    private readonly long[] _classMembers = new long[5];

    /// <summary>Runner invocations by dominant class.</summary>
    private readonly long[] _classInvocations = new long[5];

    /// <summary>The exact static member mix of every block executed (a member may be in one class only).</summary>
    private readonly long[] _classNameMembers = new long[5];

    /// <summary>Formation ticks by fetch source (0 RAM, 1 immutable image, 2 uncacheable).</summary>
    private readonly long[] _sourceFormTicks = new long[3];

    /// <summary>Formed blocks by fetch source.</summary>
    private readonly long[] _sourceFormCount = new long[3];

    /// <summary>Gets the number of runner entries (external invocations, not chain jumps).</summary>
    public long Entries { get; private set; }

    /// <summary>Gets the number of successful in-frame chain jumps.</summary>
    public long ChainEdges { get; private set; }

    /// <summary>Gets the members served behind a chain edge (served minus the entry block's own members).</summary>
    public long ChainMembers { get; private set; }

    /// <summary>Gets the emitted host instructions of the entry prologues.</summary>
    public long PrologueInstructions { get; private set; }

    /// <summary>Gets the emitted host instructions of the in-frame chain entries.</summary>
    public long ChainEntryInstructions { get; private set; }

    /// <summary>Gets the emitted host instructions of the member bodies.</summary>
    public long MemberInstructions { get; private set; }

    /// <summary>Gets the emitted host instructions of the exit edges.</summary>
    public long ExitInstructions { get; private set; }

    /// <summary>Gets the ticks spent in head lookup on the memo-miss path (pending visits + formation).</summary>
    public long LookupTicks { get; private set; }

    /// <summary>Gets the number of memo-miss head lookups.</summary>
    public long LookupCalls { get; private set; }

    /// <summary>Gets the ticks spent in block formation (planning + emitter + assemble + IL build).</summary>
    public long FormTicks { get; private set; }

    /// <summary>Gets the number of block formation attempts.</summary>
    public long FormAttempts { get; private set; }

    /// <summary>Gets the ticks spent inside Iced's <c>Assembler.Assemble</c>.</summary>
    public long AssembleTicks { get; private set; }

    /// <summary>Gets the ticks spent in the raw emitter's own code generation (outside <c>Assemble</c>).</summary>
    public long EmitTicks { get; private set; }

    /// <summary>Gets the ticks spent building IL runners (DynamicMethod).</summary>
    public long IlBuildTicks { get; private set; }

    /// <summary>Gets the ticks spent revalidating stale blocks.</summary>
    public long RevalidateTicks { get; private set; }

    /// <summary>Gets the number of blocks revalidated (kept without a rebuild).</summary>
    public long Revalidations { get; private set; }

    /// <summary>Gets the members served through the runner, summed over every bucket.</summary>
    public long RunnerMembers
    {
        get
        {
            long total = 0;
            foreach (long value in _lengthMembers)
            {
                total += value;
            }

            return total;
        }
    }

    /// <summary>Gets the runner ticks, summed over every bucket.</summary>
    public long RunnerTicks
    {
        get
        {
            long total = 0;
            foreach (long value in _lengthTicks)
            {
                total += value;
            }

            return total;
        }
    }

    /// <summary>Records one runner entry (block invocation) with its served members and chain edges.</summary>
    public void NoteInvocation(int blockMembers, int served, int chainEdges, in JitBlockStatics statics, long ticks)
    {
        Entries++;
        ChainEdges += chainEdges;
        int chained = served - blockMembers;
        if (chained > 0)
        {
            ChainMembers += chained;
        }

        int bucket = blockMembers <= 4 ? 0 : blockMembers <= 8 ? 1 : 2;
        _lengthTicks[bucket] += ticks;
        _lengthInvocations[bucket]++;
        _lengthMembers[bucket] += served;

        // A block executes all of its members once per entry and once per
        // chained target in the same frame; the static composition times that
        // execution count is the exact member census for the class.
        long executions = 1 + chainEdges;
        _classNameMembers[0] += statics.LoadMembers * executions;
        _classNameMembers[1] += statics.StoreMembers * executions;
        _classNameMembers[2] += statics.AluMembers * executions;
        _classNameMembers[3] += statics.BranchMembers * executions;
        _classNameMembers[4] += statics.OtherMembers * executions;
        int dominant = statics.DominantClass;
        if ((uint)dominant < 5u)
        {
            // Time is a whole-invocation measurement, so it is attributed to
            // the class holding most of the entry block's members and divided
            // by the members that invocation served - a measured ns/member for
            // "blocks dominated by X", not an assumed split.
            _classTicks[dominant] += ticks;
            _classInvocations[dominant]++;
            _classMembers[dominant] += served;
        }

        PrologueInstructions += statics.PrologueInstructions;
        ExitInstructions += statics.ExitInstructions * executions;
        MemberInstructions += (long)statics.MemberInstructions * executions;
        ChainEntryInstructions += (long)statics.ChainEntryInstructions * chainEdges;
    }

    /// <summary>Records one memo-miss head lookup (pending visit or formation attempt).</summary>
    public void NoteLookup(long ticks)
    {
        LookupCalls++;
        LookupTicks += ticks;
    }

    /// <summary>Records one formation attempt and its time by fetch source.</summary>
    public void NoteFormation(long ticks, int source)
    {
        FormAttempts++;
        FormTicks += ticks;
        if ((uint)source < 3u)
        {
            _sourceFormTicks[source] += ticks;
            _sourceFormCount[source]++;
        }
    }

    /// <summary>Records the Iced assemble and raw-emitter shares of one formation.</summary>
    public void NoteRawBuild(long assembleTicks, long emitTicks)
    {
        AssembleTicks += assembleTicks;
        EmitTicks += emitTicks;
    }

    /// <summary>Records one IL runner build.</summary>
    public void NoteIlBuild(long ticks) => IlBuildTicks += ticks;

    /// <summary>Records one revalidation.</summary>
    public void NoteRevalidation(long ticks)
    {
        Revalidations++;
        RevalidateTicks += ticks;
    }

    /// <summary>Formats the four attribution tables as diagnostic lines.</summary>
    public string Format()
    {
        var builder = new StringBuilder();
        long members = RunnerMembers;
        double runnerNs = RunnerTicks * NanosecondsPerTick;

        builder.Append("Jit attribution runner members=").Append(members)
            .Append(" entries=").Append(Entries)
            .Append(" ms=").Append((runnerNs / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" nsPerMember=").Append(NsPerMember(runnerNs, members).ToString("F2", CultureInfo.InvariantCulture));
        string[] lengthNames = ["b1_4", "b5_8", "b9p"];
        for (int i = 0; i < lengthNames.Length; i++)
        {
            builder.Append(' ').Append(lengthNames[i]).Append("=inv:")
                .Append(_lengthInvocations[i]).Append("/mem:").Append(_lengthMembers[i])
                .Append("/ns:").Append(NsPerMember(_lengthTicks[i] * NanosecondsPerTick, _lengthMembers[i])
                    .ToString("F2", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
        string[] classNames = ["load", "store", "alu", "branch", "other"];
        builder.Append("Jit attribution class");
        for (int i = 0; i < classNames.Length; i++)
        {
            builder.Append(' ').Append(classNames[i]).Append("=inv:").Append(_classInvocations[i])
                .Append("/mem:").Append(_classMembers[i])
                .Append("/ns:").Append(NsPerMember(_classTicks[i] * NanosecondsPerTick, _classMembers[i])
                    .ToString("F2", CultureInfo.InvariantCulture));
        }

        long mixTotal = 0;
        foreach (long value in _classNameMembers)
        {
            mixTotal += value;
        }

        builder.Append(" mix[");
        for (int i = 0; i < classNames.Length; i++)
        {
            if (i != 0)
            {
                builder.Append(' ');
            }

            builder.Append(classNames[i]).Append(':')
                .Append(mixTotal == 0
                    ? "0"
                    : (100.0 * _classNameMembers[i] / mixTotal).ToString("F1", CultureInfo.InvariantCulture))
                .Append('%');
        }

        builder.Append(']');

        builder.AppendLine();
        double edges = ChainEdges;
        builder.Append("Jit attribution edges entries=").Append(Entries)
            .Append(" chainEdges=").Append(ChainEdges)
            .Append(" chainMembers=").Append(ChainMembers)
            .Append(" membersPerEdge=").Append(edges == 0 ? "0" : (members / (Entries + edges)).ToString("F2", CultureInfo.InvariantCulture))
            .Append(" membersPerEntry=").Append(Entries == 0 ? "0" : (members / (double)Entries).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" hostInstr[prologue=").Append(PrologueInstructions)
            .Append(" chainEntry=").Append(ChainEntryInstructions)
            .Append(" member=").Append(MemberInstructions)
            .Append(" exit=").Append(ExitInstructions).Append(']');
        long totalInstructions = PrologueInstructions + ChainEntryInstructions + MemberInstructions + ExitInstructions;
        builder.Append(" hostInstrPerMember=").Append(members == 0 ? "0" : (totalInstructions / (double)members).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" hostInstrPerEdge=")
            .Append(Entries + edges == 0
                ? "0"
                : (totalInstructions / (double)(Entries + edges)).ToString("F1", CultureInfo.InvariantCulture));

        builder.AppendLine();
        builder.Append("Jit attribution form attempts=").Append(FormAttempts)
            .Append(" ms=").Append((FormTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" assembleMs=").Append((AssembleTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" emitMs=").Append((EmitTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" ilMs=").Append((IlBuildTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append(" revalidateMs=").Append((RevalidateTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append("/n:").Append(Revalidations)
            .Append(" lookupMs=").Append((LookupTicks * NanosecondsPerTick / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture))
            .Append("/n:").Append(LookupCalls);
        string[] sourceNames = ["ram", "image", "other"];
        builder.Append(" bySource[");
        for (int i = 0; i < sourceNames.Length; i++)
        {
            if (i != 0)
            {
                builder.Append(' ');
            }

            builder.Append(sourceNames[i]).Append("=n:").Append(_sourceFormCount[i])
                .Append("/ms:").Append((_sourceFormTicks[i] * NanosecondsPerTick / 1_000_000.0)
                    .ToString("F1", CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static double NsPerMember(double nanoseconds, long members) => members == 0 ? 0 : nanoseconds / members;
}

/// <summary>
/// The engine's member census (D8): the emitted instruction counts of a raw
/// block's regions plus the member-class composition of its plans. Built once
/// at formation; the attribution multiplies it by the invocation/edge counts.
/// </summary>
public sealed unsafe partial class XboxCpu
{
    /// <summary>Builds a block's static profile from its plans and native emission.</summary>
    private static JitBlockStatics BuildStatics(ReadOnlySpan<JitMemberPlan> plans, in RawBuildStatics raw)
    {
        int load = 0;
        int store = 0;
        int alu = 0;
        int branch = 0;
        int other = 0;
        foreach (JitMemberPlan plan in plans)
        {
            switch (ClassifyMember(plan))
            {
                case 0:
                    load++;
                    break;
                case 1:
                    store++;
                    break;
                case 2:
                    alu++;
                    break;
                case 3:
                    branch++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        int dominant = 0;
        int best = load;
        if (store > best)
        {
            best = store;
            dominant = 1;
        }

        if (alu > best)
        {
            best = alu;
            dominant = 2;
        }

        if (branch > best)
        {
            best = branch;
            dominant = 3;
        }

        if (other > best)
        {
            dominant = 4;
        }

        return new JitBlockStatics
        {
            PrologueInstructions = raw.PrologueInstructions,
            ChainEntryInstructions = raw.ChainEntryInstructions,
            MemberInstructions = raw.MemberInstructions,
            ExitInstructions = raw.ExitInstructions,
            LoadMembers = load,
            StoreMembers = store,
            AluMembers = alu,
            BranchMembers = branch,
            OtherMembers = other,
            DominantClass = (byte)dominant,
        };
    }

    /// <summary>Classifies one planned member (0 load, 1 store, 2 alu, 3 branch, 4 other).</summary>
    private static int ClassifyMember(in JitMemberPlan plan)
    {
        switch (plan.Kind)
        {
            // Memory writes first: a read-modify-write member is a store.
            case JitKind.MovStore:
            case JitKind.MovStoreImm:
            case JitKind.IncM:
            case JitKind.DecM:
            case JitKind.FpuStoreReal:
            case JitKind.FpuStoreRealPop:
            case JitKind.FpuStoreInteger:
            case JitKind.FpuStoreIntegerPop:
            case JitKind.MmxStore:
            case JitKind.SseMovssStore:
            case JitKind.PushR:
            case JitKind.PushI:
            case JitKind.PushM:
                return 1;

            // Memory reads.
            case JitKind.MovLoad:
            case JitKind.FpuLoadReal:
            case JitKind.FpuLoadInteger:
            case JitKind.MmxLoad:
            case JitKind.SseMovssMem:
            case JitKind.SseCvtMem:
            case JitKind.SseArithMem:
            case JitKind.AluRM:
            case JitKind.TestMI:
            case JitKind.PopR:
                return 0;

            // Control flow.
            case JitKind.JccRel:
            case JitKind.JmpRel:
            case JitKind.JmpReg:
            case JitKind.JmpMem:
            case JitKind.CallRel:
            case JitKind.CallReg:
            case JitKind.CallMem:
            case JitKind.RetNear:
                return 3;

            // Read-modify-write memory: a store unless it is the compare form.
            case JitKind.AluMR:
            case JitKind.AluMI:
                return plan.AluOp == JitAluOp.Cmp ? 0 : 1;

            // Register-only arithmetic / moves / shifts.
            case JitKind.MovRR:
            case JitKind.MovRI:
            case JitKind.AluRR:
            case JitKind.AluRI:
            case JitKind.TestRR:
            case JitKind.TestRI:
            case JitKind.NegR:
            case JitKind.NotR:
            case JitKind.Bswap:
            case JitKind.XchgRR:
            case JitKind.Cdq:
            case JitKind.Setcc:
            case JitKind.IncR:
            case JitKind.DecR:
            case JitKind.Lea:
            case JitKind.MovExtend:
            case JitKind.ShiftRI:
            case JitKind.ShiftRC:
            case JitKind.Leave:
                return 2;

            default:
                return 4;
        }
    }
}
