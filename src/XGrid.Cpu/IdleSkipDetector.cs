using System.Collections.Generic;
using XGrid.Memory;

namespace XGrid.Cpu;

/// <summary>
/// Observes the instruction stream for stable idle basic blocks: a straight-line
/// body that ends in a single backward branch, reads only RAM, writes no memory
/// and performs no I/O. After <see cref="MinIterations"/> validated iterations
/// it arms a trigger the execute loop consumes to bulk-advance time up to the
/// next observable deadline (IRQ, watched-RAM change, or host deadline).
/// </summary>
internal sealed class IdleSkipDetector
{
    internal const int DefaultMinIterations = 128;
    internal const int MaxBodyInstructions = 32;
    internal const int MaxBodyBytes = 128;
    internal const int MaxWatchAddresses = 32;
    internal const int MaxAccountedHeads = 128;

    /// <summary>Sentinel stored as a head's offending address when a PIO port is not an immediate.</summary>
    internal const uint UnknownPortAddress = 0xFFFFFFFFu;

    private readonly XboxCpu _cpu;
    private readonly List<uint> _iterationWatch = new();
    private readonly List<uint> _validatedWatch = new();
    private readonly List<uint> _watchBaseline = new();
    private readonly List<DecodedInstruction> _iterationStream = new();
    private readonly List<uint> _iterationMmio = new();
    private readonly List<uint> _validatedMmio = new();
    private readonly Dictionary<uint, HeadTrack> _rejectedHeads = new();
    private readonly HashSet<uint> _armableHeads = new();
    private readonly ulong[] _rejectSteps = new ulong[7];

    private bool _spanOpen;
    private uint _spanHead;
    private ulong _spanLastVisitTsc;
    private ulong _spanTotalAtStart;
    private ulong _totalAttributed;

    private bool _measuring;
    private bool _poisoned;
    private uint _poisonedHead;
    private uint _head;
    private uint _nextEip;
    private uint _espAtHead;
    private int _bodyInstructions;
    private int _hitCount;
    private bool _sawWrite;
    private bool _sawNonRam;
    private bool _sawPio;
    private uint _pioPort;
    private uint? _firstNonRamAddress;
    private bool _watchOverflow;
    private bool _validatedRaisesIf;

    public IdleSkipDetector(XboxCpu cpu)
    {
        _cpu = cpu;
    }

    public int MinIterations { get; set; } = DefaultMinIterations;

    public bool TriggerPending { get; private set; }

    public uint Head => _head;

    public uint LastTriggerEip { get; private set; }

    /// <summary>
    /// Gets whether the validated body executes STI: an IRQ becomes deliverable
    /// mid-body regardless of the IF flag at the loop head, so the skip loop's
    /// IRQ hard-stop must not gate on IF for such bodies.
    /// </summary>
    public bool ValidatedRaisesIf => _validatedRaisesIf;

    public ulong IterationsCompleted { get; set; }

    public ulong StaticRejects { get; set; }

    public ulong AccessAborts { get; set; }

    public ulong StructuralAborts { get; set; }

    public ulong Triggers { get; set; }

    public ulong SkippedSteps { get; set; }

    public ulong StopIrq { get; set; }

    public ulong StopWatch { get; set; }

    public ulong StopDeadline { get; set; }

    public ulong StopBudget { get; set; }

    public ulong StopReadDeadline { get; set; }

    public ulong StopModelLost { get; set; }

    public ulong HltExecuted { get; set; }

    /// <summary>Steps attributed to heads that reached a skip trigger (V1-skippable parks).</summary>
    public ulong ArmableSteps { get; private set; }

    /// <summary>Rejected heads seen after the accounting capacity was exhausted.</summary>
    public ulong HeadsUntracked { get; set; }

    /// <summary>Triggers dropped because control left the loop before the skip ran.</summary>
    public ulong TriggerDrops { get; set; }

    /// <summary>RunIdleSkip invocations that reached the skip loop (probe excluded).</summary>
    public ulong SkipInvocations { get; set; }

    /// <summary>Skip attempts that bailed because no idle-skip host was wired.</summary>
    public ulong SkipHostNull { get; set; }

    /// <summary>Steps attributed to rejected heads, indexed by <see cref="IdleSkipRejectReason"/>.</summary>
    public ulong RejectedSteps(IdleSkipRejectReason reason)
    {
        return _rejectSteps[(int)reason];
    }

    /// <summary>Returns the top rejected heads by attributed steps (measurement table).</summary>
    public List<IdleSkipHeadStat> TopRejectedHeads(int maxCount)
    {
        var stats = new List<IdleSkipHeadStat>(_rejectedHeads.Count);
        foreach ((uint head, HeadTrack track) in _rejectedHeads)
        {
            if (track.Steps == 0)
            {
                continue;
            }

            stats.Add(new IdleSkipHeadStat
            {
                Head = head,
                Steps = track.Steps,
                Reason = track.Reason,
                Address = track.Address,
            });
        }

        stats.Sort((a, b) => b.Steps.CompareTo(a.Steps));
        if (stats.Count > maxCount)
        {
            stats.RemoveRange(maxCount, stats.Count - maxCount);
        }

        return stats;
    }

    /// <summary>Records one executed instruction for the candidate-loop state machine.</summary>
    public void Observe(in DecodedInstruction instruction, uint eipBefore, uint eipAfter)
    {
        if (instruction.Mnemonic == Mnemonic.Hlt)
        {
            HltExecuted++;
        }

        bool isBranch = instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Jcc;
        uint fallThrough = eipBefore + (uint)instruction.Length;

        if (isBranch && eipAfter < eipBefore)
        {
            AccountVisit(eipAfter);
        }

        if (!_measuring)
        {
            if (isBranch && eipAfter < eipBefore)
            {
                StartCandidate(eipAfter);
            }

            return;
        }

        if (eipBefore != _nextEip)
        {
            Abort(structural: true);
            return;
        }

        if (isBranch)
        {
            if (eipAfter == fallThrough)
            {
                // Not taken: stays in the body.
                CountBodyInstruction(instruction, fallThrough);
                return;
            }

            if (eipAfter < eipBefore && eipAfter == _head)
            {
                // Completing backward branch: record it, then validate the stream.
                if (CountBodyInstruction(instruction, eipAfter))
                {
                    CompleteIteration();
                }

                return;
            }

            if (eipAfter < eipBefore)
            {
                // A different backward edge: restart measurement anchored there.
                Abort(structural: true);
                StartCandidate(eipAfter);
                return;
            }

            if (instruction.Mnemonic != Mnemonic.Jcc)
            {
                // Unconditional forward jump leaves through an unknown path.
                Abort(structural: true);
                return;
            }

            // Forward conditional taken (idle-loop sub-path skip): part of the body.
            CountBodyInstruction(instruction, eipAfter);
            return;
        }

        if (eipAfter != fallThrough)
        {
            Abort(structural: true);
            return;
        }

        if (instruction.Mnemonic is Mnemonic.In or Mnemonic.Out or Mnemonic.Ins or Mnemonic.Outs)
        {
            AccessAborts++;
            _sawPio = true;
            _pioPort = instruction.Operand1.Kind == OperandKind.Immediate
                ? instruction.Operand1.Immediate
                : UnknownPortAddress;
            RegisterRejectedHead(IdleSkipRejectReason.Pio, _pioPort);
            PoisonAndAbort();
            return;
        }

        if (!IdleSkipBodyRules.IsAllowed(instruction.Mnemonic))
        {
            RegisterRejectedHead(IdleSkipRejectReason.Structural);
            Abort(structural: true);
            return;
        }

        if (_sawWrite || _sawNonRam || _watchOverflow)
        {
            AccessAborts++;
            RegisterAccessVerdictHead();
            PoisonAndAbort();
            return;
        }

        CountBodyInstruction(instruction, fallThrough);
    }

    /// <summary>Records a translated physical access made by the candidate body or its fetch.</summary>
    public void NoteAccess(uint physicalAddress, bool isWrite, bool isInstruction)
    {
        if (!_measuring)
        {
            return;
        }

        IIdleSkipHost? host = _cpu.IdleSkipHost;
        if (host is null)
        {
            _sawNonRam = true;
            return;
        }

        if (isWrite)
        {
            if (!host.IsRam(physicalAddress))
            {
                // A device write is still a write-body hard stop (V1 rule); flag
                // both so the §119 attribution labels it MemWrite, not MmioRead.
                _sawNonRam = true;
                _sawWrite = true;
                _firstNonRamAddress ??= physicalAddress;
            }
            else
            {
                _sawWrite = true;
            }

            return;
        }

        if (!host.IsRam(physicalAddress))
        {
            // V2 (§119): a data read of a device register the read-effect model
            // classifies (side-effect free, value changes only with device time)
            // may stay in the body; the skip loop clamps chunks to its deadline.
            if (!isInstruction
                && _cpu.IdleSkipReadModel is { } model
                && model.TryGetReadEffect(physicalAddress, out _))
            {
                if (!_iterationMmio.Contains(physicalAddress))
                {
                    if (_iterationMmio.Count >= MaxWatchAddresses)
                    {
                        _watchOverflow = true;
                        return;
                    }

                    _iterationMmio.Add(physicalAddress);
                }

                return;
            }

            _sawNonRam = true;
            _firstNonRamAddress ??= physicalAddress;
            return;
        }

        if (isInstruction)
        {
            return;
        }

        // Watch a whole dword so the skip loop can compare cheaply; an access
        // straddling the RAM boundary is rejected instead of half-watched.
        if (!host.IsRam(physicalAddress + 1) || !host.IsRam(physicalAddress + 2)
                                             || !host.IsRam(physicalAddress + 3))
        {
            _watchOverflow = true;
            return;
        }

        if (!_iterationWatch.Contains(physicalAddress))
        {
            if (_iterationWatch.Count >= MaxWatchAddresses)
            {
                _watchOverflow = true;
                return;
            }

            _iterationWatch.Add(physicalAddress);
        }
    }

    /// <summary>Clears a pending trigger (consumed or stale).</summary>
    public void ClearTrigger()
    {
        TriggerPending = false;
    }

    /// <summary>Records a trigger that was dropped before the skip could run.</summary>
    public void NoteTriggerDropped()
    {
        TriggerDrops++;
    }

    /// <summary>Records that the skip loop was entered.</summary>
    public void NoteSkipInvocation()
    {
        SkipInvocations++;
    }

    /// <summary>Snapshots the watched dwords so later changes are detectable.</summary>
    public void CaptureWatchBaseline()
    {
        _watchBaseline.Clear();
        for (int i = 0; i < _validatedWatch.Count; i++)
        {
            _watchBaseline.Add(ReadWatchDword(_validatedWatch[i]));
        }
    }

    /// <summary>Returns true when any watched RAM dword changed since the baseline snapshot.</summary>
    public bool WatchChanged()
    {
        for (int i = 0; i < _validatedWatch.Count; i++)
        {
            if (ReadWatchDword(_validatedWatch[i]) != _watchBaseline[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Disarms the trigger and restarts measurement from scratch after a skip (or probe hit).</summary>
    public void ResetAfterSkip()
    {
        TriggerPending = false;
        _measuring = false;
        _hitCount = 0;
        _bodyInstructions = 0;
        _sawWrite = false;
        _sawNonRam = false;
        _sawPio = false;
        _pioPort = 0;
        _firstNonRamAddress = null;
        _watchOverflow = false;
        _validatedRaisesIf = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        _iterationMmio.Clear();
    }

    /// <summary>Fully resets measurement state, poisoning and counters (CPU reset / snapshot load).</summary>
    public void HardReset()
    {
        ResetAfterSkip();
        _poisoned = false;
        _poisonedHead = 0;
        _head = 0;
        _nextEip = 0;
        _espAtHead = 0;
        _validatedWatch.Clear();
        _watchBaseline.Clear();
        _validatedMmio.Clear();
        _rejectedHeads.Clear();
        _armableHeads.Clear();
        for (int i = 0; i < _rejectSteps.Length; i++)
        {
            _rejectSteps[i] = 0;
        }

        _spanOpen = false;
        _spanHead = 0;
        _spanLastVisitTsc = 0;
        _spanTotalAtStart = 0;
        _totalAttributed = 0;
        ArmableSteps = 0;
        HeadsUntracked = 0;
        TriggerDrops = 0;
        SkipInvocations = 0;
        SkipHostNull = 0;
        IterationsCompleted = 0;
        StaticRejects = 0;
        AccessAborts = 0;
        StructuralAborts = 0;
        Triggers = 0;
        SkippedSteps = 0;
        StopIrq = 0;
        StopWatch = 0;
        StopDeadline = 0;
        StopBudget = 0;
        StopReadDeadline = 0;
        StopModelLost = 0;
        HltExecuted = 0;
        LastTriggerEip = 0;
    }

    private void StartCandidate(uint head)
    {
        if (_cpu.IdleSkipHost is null)
        {
            return;
        }

        if (_poisoned && head == _poisonedHead)
        {
            return;
        }

        _measuring = true;
        _head = head;
        _nextEip = head;
        _bodyInstructions = 0;
        _hitCount = 0;
        _espAtHead = _cpu.Context.Esp;
        _sawWrite = false;
        _sawNonRam = false;
        _sawPio = false;
        _pioPort = 0;
        _firstNonRamAddress = null;
        _watchOverflow = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        _iterationMmio.Clear();
        TriggerPending = false;
    }

    private bool CountBodyInstruction(in DecodedInstruction instruction, uint nextEip)
    {
        if (_bodyInstructions >= MaxBodyInstructions)
        {
            RegisterRejectedHead(IdleSkipRejectReason.Structural);
            Abort(structural: true);
            return false;
        }

        _bodyInstructions++;
        _nextEip = nextEip;
        _iterationStream.Add(instruction);
        return true;
    }

    private void CompleteIteration()
    {
        if (_sawWrite || _sawNonRam || _sawPio || _watchOverflow)
        {
            AccessAborts++;
            RegisterAccessVerdictHead();
            PoisonAndAbort();
            return;
        }

        if (_cpu.Context.Esp != _espAtHead)
        {
            RegisterRejectedHead(IdleSkipRejectReason.Structural);
            Abort(structural: true);
            return;
        }

        string? reject = IdleSkipBodyRules.Validate(_iterationStream, out bool raisesIf);
        if (reject is not null)
        {
            StaticRejects++;
            RegisterRejectedHead(IdleSkipRejectReason.StaticRule);
            PoisonAndAbort();
            return;
        }

        _validatedWatch.Clear();
        _validatedWatch.AddRange(_iterationWatch);
        _validatedMmio.Clear();
        _validatedMmio.AddRange(_iterationMmio);
        _validatedRaisesIf = raisesIf;
        IterationsCompleted++;
        _hitCount++;

        _espAtHead = _cpu.Context.Esp;
        _bodyInstructions = 0;
        _sawWrite = false;
        _sawNonRam = false;
        _sawPio = false;
        _pioPort = 0;
        _firstNonRamAddress = null;
        _watchOverflow = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        _iterationMmio.Clear();
        _nextEip = _head;

        if (_hitCount >= MinIterations && !TriggerPending)
        {
            TriggerPending = true;
            Triggers++;
            LastTriggerEip = _head;
            _rejectedHeads.Remove(_head);
            _armableHeads.Add(_head);
        }
    }

    private void PoisonAndAbort()
    {
        _poisoned = true;
        _poisonedHead = _head;
        Abort(structural: false);
    }

    private void Abort(bool structural)
    {
        if (structural && _bodyInstructions > 0)
        {
            StructuralAborts++;
        }

        _measuring = false;
        TriggerPending = false;
        _bodyInstructions = 0;
        _hitCount = 0;
        _sawWrite = false;
        _sawNonRam = false;
        _sawPio = false;
        _pioPort = 0;
        _firstNonRamAddress = null;
        _watchOverflow = false;
        _validatedRaisesIf = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        _iterationMmio.Clear();
    }

    /// <summary>
    /// Exclusive-time attribution for the §119 measurement: the inter-visit
    /// delta of a backward branch is credited to the currently open loop head,
    /// minus any steps already attributed to other tracked heads (inner or
    /// sibling loops) since that head's previous visit. This keeps nested and
    /// sequential loops from double-counting spans; time spent in untracked
    /// code between visits stays with the open head (documented approximation).
    /// </summary>
    private void AccountVisit(uint head)
    {
        ulong tsc = _cpu.Context.Tsc;
        if (_spanOpen)
        {
            ulong delta = tsc - _spanLastVisitTsc;
            if (delta > 0)
            {
                ulong other = _totalAttributed - _spanTotalAtStart;
                ulong net = delta > other ? delta - other : 0;
                if (net > 0)
                {
                    _totalAttributed += net;
                    if (_rejectedHeads.TryGetValue(_spanHead, out HeadTrack? track))
                    {
                        track.Steps += net;
                        _rejectSteps[(int)track.Reason] += net;
                    }
                    else if (_armableHeads.Contains(_spanHead))
                    {
                        ArmableSteps += net;
                    }
                }
            }
        }

        _spanOpen = true;
        _spanHead = head;
        _spanLastVisitTsc = tsc;
        _spanTotalAtStart = _totalAttributed;
    }

    /// <summary>Records the access-verdict reason (PIO / write / MMIO / watch overflow) for the current head.</summary>
    private void RegisterAccessVerdictHead()
    {
        if (_sawPio)
        {
            RegisterRejectedHead(IdleSkipRejectReason.Pio, _pioPort);
        }
        else if (_sawWrite)
        {
            RegisterRejectedHead(IdleSkipRejectReason.MemWrite);
        }
        else if (_sawNonRam)
        {
            RegisterRejectedHead(IdleSkipRejectReason.MmioRead, _firstNonRamAddress ?? 0);
        }
        else
        {
            RegisterRejectedHead(IdleSkipRejectReason.WatchOverflow);
        }
    }

    private void RegisterRejectedHead(IdleSkipRejectReason reason, uint address = 0)
    {
        if (_armableHeads.Contains(_head))
        {
            return;
        }

        if (!_rejectedHeads.TryGetValue(_head, out HeadTrack? track))
        {
            if (_rejectedHeads.Count >= MaxAccountedHeads)
            {
                HeadsUntracked++;
                return;
            }

            track = new HeadTrack();
            _rejectedHeads[_head] = track;
        }

        if (track.Reason == IdleSkipRejectReason.None)
        {
            track.Reason = reason;
            track.Address = address;
        }
    }

    /// <summary>
    /// Clamps the next skip chunk by every watched device register's change
    /// deadline. Returns false when the skip must stop now: a deadline reached
    /// (the register changes at this step — the interpreter must observe it)
    /// or the model can no longer classify a watched register (conservative).
    /// </summary>
    internal bool TryClampChunkByReadModel(ref int chunk)
    {
        if (_validatedMmio.Count == 0)
        {
            return true;
        }

        IIdleSkipReadModel? model = _cpu.IdleSkipReadModel;
        if (model is null)
        {
            StopModelLost++;
            return false;
        }

        for (int i = 0; i < _validatedMmio.Count; i++)
        {
            if (!model.TryGetReadEffect(_validatedMmio[i], out IdleSkipReadEffect effect))
            {
                StopModelLost++;
                return false;
            }

            if (effect.IsStable)
            {
                continue;
            }

            if (effect.StepsUntilChange <= 0)
            {
                StopReadDeadline++;
                return false;
            }

            if (effect.StepsUntilChange < chunk)
            {
                chunk = (int)effect.StepsUntilChange;
            }
        }

        return true;
    }

    private sealed class HeadTrack
    {
        public IdleSkipRejectReason Reason;
        public uint Address;
        public ulong Steps;
    }

    private uint ReadWatchDword(uint physicalAddress)
    {
        IMemoryBus memory = _cpu.Memory;
        return memory.Read8(physicalAddress)
               | ((uint)memory.Read8(physicalAddress + 1) << 8)
               | ((uint)memory.Read8(physicalAddress + 2) << 16)
               | ((uint)memory.Read8(physicalAddress + 3) << 24);
    }
}
