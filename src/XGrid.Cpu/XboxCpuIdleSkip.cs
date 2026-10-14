using System;
using System.Collections.Generic;

namespace XGrid.Cpu;

/// <summary>
/// Opt-in idle-skip (fast-forward) support: configuration, counters, and the
/// deadline-bounded bulk-advance loop that runs when the detector arms a
/// trigger at a verified idle-loop head. Default off: with no detector
/// constructed the interpreter path is byte-identical to the plain core.
/// </summary>
public sealed partial class XboxCpu
{
    private IdleSkipDetector? _idle;
    private int _idleSkipMinIterations = IdleSkipDetector.DefaultMinIterations;
    private int _idleSkipChunkSteps = IdleSkipDefaultChunkSteps;
    private int _idleAdvanced;

    /// <summary>Default device-advance quantum (CPU cycles) per skip chunk.</summary>
    public const int IdleSkipDefaultChunkSteps = 4096;

    /// <summary>Gets or sets the host that supplies bulk device advance and stop conditions.</summary>
    public IIdleSkipHost? IdleSkipHost { get; set; }

    /// <summary>
    /// Gets or sets the optional device read-effect model consulted for non-RAM
    /// reads in candidate bodies (§119): registers it classifies may stay in a
    /// skippable body, clamped to their change deadlines. Null = V1 semantics
    /// (any non-RAM read rejects the body).
    /// </summary>
    public IIdleSkipReadModel? IdleSkipReadModel { get; set; }

    /// <summary>Gets or sets whether idle-loop detection and skipping are enabled (default off).</summary>
    public bool IdleSkipEnabled
    {
        get => _idle is not null;
        set
        {
            if (value)
            {
                _idle ??= new IdleSkipDetector(this);
            }
            else
            {
                _idle = null;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether triggers only measure (probe mode: counters update,
    /// no time is skipped). Used by the CLI's <c>--idle-skip-probe</c>.
    /// </summary>
    public bool IdleSkipMeasureOnly { get; set; }

    /// <summary>Gets or sets the consecutive validated iterations required before a skip trigger.</summary>
    public int IdleSkipMinIterations
    {
        get => _idleSkipMinIterations;
        set => _idleSkipMinIterations = Math.Max(1, value);
    }

    /// <summary>Gets or sets the maximum cycles advanced per skip chunk between stop-condition checks.</summary>
    public int IdleSkipChunkSteps
    {
        get => _idleSkipChunkSteps;
        set => _idleSkipChunkSteps = Math.Max(1, value);
    }

    /// <summary>Gets the number of skip triggers fired.</summary>
    public ulong IdleSkipTriggers => _idle?.Triggers ?? 0;

    /// <summary>Gets the number of steps bulk-advanced by idle skips.</summary>
    public ulong IdleSkipSkippedSteps => _idle?.SkippedSteps ?? 0;

    /// <summary>Gets the number of validated loop iterations observed.</summary>
    public ulong IdleSkipIterations => _idle?.IterationsCompleted ?? 0;

    /// <summary>Gets the number of structurally clean bodies rejected by the data-flow rules.</summary>
    public ulong IdleSkipStaticRejects => _idle?.StaticRejects ?? 0;

    /// <summary>Gets the number of measured bodies rejected for MMIO/PIO/memory-write access.</summary>
    public ulong IdleSkipAccessAborts => _idle?.AccessAborts ?? 0;

    /// <summary>Gets the number of measured bodies that broke structure mid-iteration.</summary>
    public ulong IdleSkipStructuralAborts => _idle?.StructuralAborts ?? 0;

    /// <summary>Gets the number of skips stopped by a pending+enabled IRQ boundary.</summary>
    public ulong IdleSkipStopIrq => _idle?.StopIrq ?? 0;

    /// <summary>Gets the number of skips stopped by a watched-RAM change (poll exit).</summary>
    public ulong IdleSkipStopWatch => _idle?.StopWatch ?? 0;

    /// <summary>Gets the number of skips stopped exactly on a host deadline.</summary>
    public ulong IdleSkipStopDeadline => _idle?.StopDeadline ?? 0;

    /// <summary>Gets the number of skips stopped by the instruction budget.</summary>
    public ulong IdleSkipStopBudget => _idle?.StopBudget ?? 0;

    /// <summary>Gets the number of skips stopped exactly on a watched device register's change deadline.</summary>
    public ulong IdleSkipStopReadDeadline => _idle?.StopReadDeadline ?? 0;

    /// <summary>Gets the number of skips stopped because a watched device register could no longer be classified.</summary>
    public ulong IdleSkipStopModelLost => _idle?.StopModelLost ?? 0;

    /// <summary>Gets the steps attributed to heads that reached a skip trigger (V1-skippable parks).</summary>
    public ulong IdleSkipArmableSteps => _idle?.ArmableSteps ?? 0;

    /// <summary>Gets the number of rejected heads seen after the accounting capacity was exhausted.</summary>
    public ulong IdleSkipHeadsUntracked => _idle?.HeadsUntracked ?? 0;

    /// <summary>Gets the number of triggers dropped because control left the loop before the skip ran.</summary>
    public ulong IdleSkipTriggerDrops => _idle?.TriggerDrops ?? 0;

    /// <summary>Gets the number of skip-loop invocations (probe mode excluded).</summary>
    public ulong IdleSkipInvocations => _idle?.SkipInvocations ?? 0;

    /// <summary>Gets the number of skip attempts that bailed because no idle-skip host was wired.</summary>
    public ulong IdleSkipHostNull => _idle?.SkipHostNull ?? 0;

    /// <summary>Gets the steps attributed to heads rejected for a non-RAM (device-register) body read.</summary>
    public ulong IdleSkipRejectedStepsMmioRead => _idle?.RejectedSteps(IdleSkipRejectReason.MmioRead) ?? 0;

    /// <summary>Gets the steps attributed to heads rejected for PIO in the body.</summary>
    public ulong IdleSkipRejectedStepsPio => _idle?.RejectedSteps(IdleSkipRejectReason.Pio) ?? 0;

    /// <summary>Gets the steps attributed to heads rejected for a memory write in the body.</summary>
    public ulong IdleSkipRejectedStepsMemWrite => _idle?.RejectedSteps(IdleSkipRejectReason.MemWrite) ?? 0;

    /// <summary>Gets the steps attributed to heads rejected for watch-list overflow.</summary>
    public ulong IdleSkipRejectedStepsWatchOverflow => _idle?.RejectedSteps(IdleSkipRejectReason.WatchOverflow) ?? 0;

    /// <summary>Gets the steps attributed to heads rejected by the fixed-point data-flow rules.</summary>
    public ulong IdleSkipRejectedStepsStaticRule => _idle?.RejectedSteps(IdleSkipRejectReason.StaticRule) ?? 0;

    /// <summary>Gets the steps attributed to heads rejected for structure/mnemonic violations.</summary>
    public ulong IdleSkipRejectedStepsStructural => _idle?.RejectedSteps(IdleSkipRejectReason.Structural) ?? 0;

    /// <summary>Gets the top rejected loop heads by attributed steps (measurement table).</summary>
    public List<IdleSkipHeadStat> IdleSkipTopRejectedHeads(int maxCount)
    {
        return _idle is null ? new List<IdleSkipHeadStat>() : _idle.TopRejectedHeads(maxCount);
    }

    /// <summary>Gets the number of HLT instructions executed (probe diagnostic; guests are spin-park machines).</summary>
    public ulong IdleSkipHltExecuted => _idle?.HltExecuted ?? 0;

    /// <summary>Gets the loop-head EIP of the most recent skip trigger.</summary>
    public uint IdleSkipLastTriggerEip => _idle?.LastTriggerEip ?? 0;

    /// <summary>
    /// Takes the cycle count already advanced on the host during skips inside the
    /// last <see cref="Execute"/> call, so the machine does not advance twice.
    /// </summary>
    public int TakeIdleAdvanced()
    {
        int advanced = _idleAdvanced;
        _idleAdvanced = 0;
        return advanced;
    }

    private int RunIdleSkip(int budget)
    {
        IdleSkipDetector? detector = _idle;
        if (detector is null || budget <= 0)
        {
            return 0;
        }

        detector.NoteSkipInvocation();
        detector.ClearTrigger();
        if (IdleSkipMeasureOnly || IdleSkipHost is null)
        {
            if (IdleSkipHost is null)
            {
                detector.SkipHostNull++;
            }

            detector.ResetAfterSkip();
            return 0;
        }

        IIdleSkipHost host = IdleSkipHost;
        bool irqCanWake = detector.ValidatedRaisesIf || (Context.Eflags & FlagBits.If) != 0;
        detector.CaptureWatchBaseline();
        int skipped = 0;
        while (true)
        {
            // IRQ boundary: hard stop. Bodies with STI deliver on the first IF=1
            // boundary mid-body, so they wake on pending regardless of head-time IF.
            if (irqCanWake && host.IrqPendingAndEnabled)
            {
                detector.StopIrq++;
                break;
            }

            if (detector.WatchChanged())
            {
                detector.StopWatch++;
                break;
            }

            if (skipped >= budget)
            {
                detector.StopBudget++;
                break;
            }

            int chunk = Math.Min(_idleSkipChunkSteps, budget - skipped);
            if (host.StepsUntilNextDeadline is long deadline)
            {
                if (deadline <= 0)
                {
                    detector.StopDeadline++;
                    break;
                }

                chunk = (int)Math.Min(chunk, deadline);
            }

            // §119: clamp by the watched device registers' change deadlines so a
            // classified MMIO poll exits exactly at its value transition.
            if (!detector.TryClampChunkByReadModel(ref chunk))
            {
                break;
            }

            Context.Tsc += (ulong)chunk;
            host.AdvanceIdle((ulong)chunk);
            _idleAdvanced += chunk;
            skipped += chunk;
            detector.SkippedSteps += (ulong)chunk;
        }

        detector.ResetAfterSkip();
        return skipped;
    }
}
