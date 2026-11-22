using System;
using System.Diagnostics.CodeAnalysis;
using XGrid.Cpu.Tcg;

namespace XGrid.Cpu;

/// <summary>
/// P1S4: the managed TCG engine seam. The IR engine runs one translated
/// instruction at a time over the CPU's own linear-memory path (MMU, MMIO,
/// page-fault delivery), polls interrupts exactly like <see cref="Step"/>, and
/// falls back to the exact interpreter for any instruction the translator
/// declines. It is opt-in (<c>XGRID_TCG_ENGINE=1</c>) and never mixes with the
/// JIT or spec-block engines.
/// </summary>
public sealed partial class XboxCpu : ITcgMemory, ITcgPollObserver
{
    /// <summary>Gets whether the TCG IR engine is enabled (the environment variable is read once).</summary>
    public static bool TcgEngineEnabled { get; } = string.Equals(
        Environment.GetEnvironmentVariable("XGRID_TCG_ENGINE"),
        "1",
        StringComparison.Ordinal);

    /// <summary>Gets the instructions the IR engine translated and executed.</summary>
    public long TcgTranslatedInstructions { get; private set; }

    /// <summary>Gets the instructions the IR engine handed to the interpreter.</summary>
    public long TcgFallbackInstructions { get; private set; }

    private readonly TcgExecutionState _tcgState = new();
    private TcgBlockCache? _tcgBlocks;

    private TcgBlockCache TcgBlocks => _tcgBlocks ??= new TcgBlockCache();

    /// <summary>Gets the cached translated-block count.</summary>
    public int TcgBlockCount => _tcgBlocks?.Count ?? 0;

    /// <summary>Gets the translated-block cache hits.</summary>
    public long TcgBlockHits => _tcgBlocks?.Hits ?? 0;

    /// <summary>Gets the translated-block cache misses (formations).</summary>
    public long TcgBlockMisses => _tcgBlocks?.Misses ?? 0;

    byte ITcgMemory.Read8(uint address) => (byte)ReadMemory(address, 8);

    ushort ITcgMemory.Read16(uint address) => (ushort)ReadMemory(address, 16);

    uint ITcgMemory.Read32(uint address) => ReadMemory(address, 32);

    void ITcgMemory.Write8(uint address, byte value) => WriteMemory(address, 8, value);

    void ITcgMemory.Write16(uint address, ushort value) => WriteMemory(address, 16, value);

    void ITcgMemory.Write32(uint address, uint value) => WriteMemory(address, 32, value);

    /// <summary>Runs up to <paramref name="budget"/> instructions on the IR engine.</summary>
    /// <param name="budget">Instruction budget.</param>
    /// <returns>The instructions executed.</returns>
    private int RunTcg(int budget)
    {
        int executed = 0;
        Span<byte> buffer = stackalloc byte[Decoder.MaxInstructionLength];
        while (executed < budget && !Context.Halted)
        {
            uint eip = Context.Eip;
            Context.Tsc++;
            _tcgState.MembersDone = 0;
            _tcgState.Interrupted = false;
            _tcgState.LimitReached = false;
            _tcgState.MemberLimit = budget - executed;

            if (InterruptSource is not null && (Context.Eflags & FlagBits.If) != 0)
            {
                int vector = PollInterrupt();
                if (vector >= 0)
                {
                    Context.Halted = false;
                    if (TryTransferToInterrupt(vector, Context.Eip, false, 0))
                    {
                        InterruptCount++;
                        executed++;
                        continue;
                    }
                }
            }

            try
            {
                int operandSize = Context.DefaultOperandSize;
                int addressSize = Context.DefaultAddressSize;
                if (!TcgBlocks.TryGet(
                        eip, operandSize, addressSize, out TcgProgram? program, out _, out int members))
                {
                    Fetch(buffer);
                    if (!TryBuildTcgBlock(
                            eip, buffer, operandSize, addressSize, out program, out int length, out members))
                    {
                        TcgFallbackInstructions++;
                        StepWithoutAccounting();
                        executed++;
                        continue;
                    }

                    TcgBlocks.Put(eip, buffer, operandSize, addressSize, program!, length, members);
                }

                for (int i = 0; i < 8; i++)
                {
                    _tcgState.Registers[i] = Context.ReadRegister((Gpr)i);
                }

                for (int i = 0; i < _tcgState.Segments.Length; i++)
                {
                    _tcgState.Segments[i] = Context.GetSegmentBase((SegmentRegister)i);
                }

                _tcgState.Eflags = Context.Eflags;
                _tcgState.Poll = this;
                TcgInterpreter.Execute(program!, _tcgState, this, Context.Fpu, Context);
                for (int i = 0; i < 8; i++)
                {
                    Context.WriteRegister((Gpr)i, _tcgState.Registers[i]);
                }

                Context.Eflags = _tcgState.Eflags;
                Context.Eip = _tcgState.Eip;
                if (_tcgState.Interrupted)
                {
                    // Completed members plus the delivery itself (the same
                    // count Step's poll contributes).
                    executed += _tcgState.MembersDone + 1;
                    TcgTranslatedInstructions += _tcgState.MembersDone;
                }
                else if (_tcgState.LimitReached)
                {
                    executed += _tcgState.MembersDone;
                    TcgTranslatedInstructions += _tcgState.MembersDone;
                }
                else
                {
                    executed += members;
                    TcgTranslatedInstructions += members;
                }

                continue;
            }
            catch (PageFaultSignal fault)
            {
                Context.Cr2 = fault.LinearAddress;
                LastFaultVector = 14;
                LastFaultEip = eip;
                LastFaultErrorCode = fault.ErrorCode;
                if (!TryTransferToInterrupt(14, eip, true, fault.ErrorCode))
                {
                    throw new UnhandledGuestException(14, eip);
                }

                executed += _tcgState.MembersDone + 1;
                continue;
            }
        }

        return executed;
    }

    bool ITcgPollObserver.BeforeMember(TcgExecutionState state, uint eip)
    {
        Context.Tsc++;
        if (InterruptSource is null || (Context.Eflags & FlagBits.If) == 0)
        {
            return false;
        }

        int vector = PollInterrupt();
        if (vector < 0)
        {
            return false;
        }

        // Publish the live block state before the delivery: the handler's stack
        // push and segment loads must see the block's registers, not the
        // block-entry snapshot.
        for (int i = 0; i < 8; i++)
        {
            Context.WriteRegister((Gpr)i, state.Registers[i]);
        }

        Context.Eflags = state.Eflags;
        Context.Halted = false;
        if (!TryTransferToInterrupt(vector, eip, false, 0))
        {
            return false;
        }

        InterruptCount++;
        for (int i = 0; i < 8; i++)
        {
            state.Registers[i] = Context.ReadRegister((Gpr)i);
        }

        state.Eflags = Context.Eflags;
        state.Eip = Context.Eip;
        return true;
    }

    /// <summary>
    /// Builds one straight-line translated block (up to 16 instructions) from
    /// the guest EIP. The head bytes are already fetched by the caller.
    /// </summary>
    private bool TryBuildTcgBlock(
        uint eip,
        ReadOnlySpan<byte> head,
        int operandSize,
        int addressSize,
        out TcgProgram? program,
        out int length,
        out int members)
    {
        program = null;
        length = 0;
        members = 0;
        var builder = new TcgBuilder(eip);
        Span<byte> source = stackalloc byte[Decoder.MaxInstructionLength];
        int offset = 0;
        for (int member = 0; member < 16; member++)
        {
            uint memberEip = eip + (uint)offset;
            if (member == 0)
            {
                head.CopyTo(source);
            }
            else
            {
                uint saved = Context.Eip;
                Context.Eip = memberEip;
                try
                {
                    Fetch(source);
                }
                finally
                {
                    Context.Eip = saved;
                }
            }

            DecodedInstruction decoded = Decoder.Decode(source, operandSize, addressSize);
            if (!decoded.IsValid)
            {
                break;
            }

            // Probe on a throwaway builder so a declined member never leaves a
            // half-emitted program (and never emits its poll).
            var probe = new TcgBuilder(memberEip);
            if (!I386Translator.TryTranslate(decoded, memberEip, probe, out _, out _))
            {
                break;
            }

            if (member != 0)
            {
                builder.Poll(memberEip);
            }

            I386Translator.TryTranslate(
                decoded, memberEip, builder, out bool terminates, out _);
            offset += decoded.Length;
            length = offset;
            members++;
            if (terminates)
            {
                program = builder.Build();
                return true;
            }
        }

        if (length == 0)
        {
            return false;
        }

        builder.SetEip(eip + (uint)length);
        builder.Exit();
        program = builder.Build();
        return true;
    }

    /// <summary>
    /// Executes one instruction with the interpreter without the step/TSC and
    /// interrupt accounting <see cref="RunTcg"/> already performed.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    private void StepWithoutAccounting()
    {
        Span<byte> buffer = stackalloc byte[Decoder.MaxInstructionLength];
        Fetch(buffer);
        DecodedInstruction instruction = Decoder.Decode(
            buffer, Context.DefaultOperandSize, Context.DefaultAddressSize);
        DecodeCount++;
        if (!instruction.IsValid)
        {
            LastFaultVector = 6;
            LastFaultEip = Context.Eip;
            LastFaultErrorCode = 0;
            if (!TryTransferToInterrupt(6, Context.Eip, false, 0))
            {
                throw new UnsupportedInstructionException(Context.Eip, instruction.Opcode);
            }

            return;
        }

        uint eipBefore = Context.Eip;
        ExecuteInstruction(instruction);
        _idle?.Observe(instruction, eipBefore, Context.Eip);
        HotspotObserver?.Observe(instruction, eipBefore, Context.Eip);
    }
}
