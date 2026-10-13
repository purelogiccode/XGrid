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

    private readonly XboxCpu _cpu;
    private readonly List<uint> _iterationWatch = new();
    private readonly List<uint> _validatedWatch = new();
    private readonly List<uint> _watchBaseline = new();
    private readonly List<DecodedInstruction> _iterationStream = new();

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

    public ulong HltExecuted { get; set; }

    /// <summary>Records one executed instruction for the candidate-loop state machine.</summary>
    public void Observe(in DecodedInstruction instruction, uint eipBefore, uint eipAfter)
    {
        if (instruction.Mnemonic == Mnemonic.Hlt)
        {
            HltExecuted++;
        }

        bool isBranch = instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Jcc;
        uint fallThrough = eipBefore + (uint)instruction.Length;

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
            PoisonAndAbort();
            return;
        }

        if (!IdleSkipBodyRules.IsAllowed(instruction.Mnemonic))
        {
            Abort(structural: true);
            return;
        }

        if (_sawWrite || _sawNonRam || _watchOverflow)
        {
            AccessAborts++;
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
                _sawNonRam = true;
            }
            else
            {
                _sawWrite = true;
            }

            return;
        }

        if (!host.IsRam(physicalAddress))
        {
            _sawNonRam = true;
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
        _watchOverflow = false;
        _validatedRaisesIf = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
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
        _watchOverflow = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        TriggerPending = false;
    }

    private bool CountBodyInstruction(in DecodedInstruction instruction, uint nextEip)
    {
        if (_bodyInstructions >= MaxBodyInstructions)
        {
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
        if (_sawWrite || _sawNonRam || _watchOverflow)
        {
            AccessAborts++;
            PoisonAndAbort();
            return;
        }

        if (_cpu.Context.Esp != _espAtHead)
        {
            Abort(structural: true);
            return;
        }

        string? reject = IdleSkipBodyRules.Validate(_iterationStream, out bool raisesIf);
        if (reject is not null)
        {
            StaticRejects++;
            PoisonAndAbort();
            return;
        }

        _validatedWatch.Clear();
        _validatedWatch.AddRange(_iterationWatch);
        _validatedRaisesIf = raisesIf;
        IterationsCompleted++;
        _hitCount++;

        _espAtHead = _cpu.Context.Esp;
        _bodyInstructions = 0;
        _sawWrite = false;
        _sawNonRam = false;
        _watchOverflow = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
        _nextEip = _head;

        if (_hitCount >= MinIterations && !TriggerPending)
        {
            TriggerPending = true;
            Triggers++;
            LastTriggerEip = _head;
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
        _watchOverflow = false;
        _validatedRaisesIf = false;
        _iterationWatch.Clear();
        _iterationStream.Clear();
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
