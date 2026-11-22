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
public sealed partial class XboxCpu : ITcgMemory
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
                Fetch(buffer);
                int operandSize = Context.DefaultOperandSize;
                int addressSize = Context.DefaultAddressSize;
                if (!TcgBlocks.TryGet(eip, buffer, operandSize, addressSize, out TcgProgram? program, out _))
                {
                    if (!TryBuildTcgBlock(eip, buffer, operandSize, addressSize, out program, out int length))
                    {
                        TcgFallbackInstructions++;
                        StepWithoutAccounting();
                        executed++;
                        continue;
                    }

                    TcgBlocks.Put(eip, buffer, operandSize, addressSize, program!, length);
                }

                var gprs = new uint[8];
                for (int i = 0; i < 8; i++)
                {
                    gprs[i] = Context.ReadRegister((Gpr)i);
                }

                var segments = new uint[6];
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i] = Context.GetSegmentBase((SegmentRegister)i);
                }

                TcgResult result = TcgInterpreter.Execute(
                    program!,
                    gprs,
                    Context.Eflags,
                    segments,
                    this,
                    Context.Fpu,
                    Context);
                for (int i = 0; i < 8; i++)
                {
                    Context.WriteRegister((Gpr)i, result.Registers[i]);
                }

                Context.Eflags = result.Eflags;
                Context.Eip = result.Eip;
                TcgTranslatedInstructions++;
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
            }

            executed++;
        }

        return executed;
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
        out int length)
    {
        program = null;
        length = 0;
        var builder = new TcgBuilder(eip);
        Span<byte> source = stackalloc byte[Decoder.MaxInstructionLength];
        int offset = 0;
        // One guest instruction per TB keeps the per-instruction interrupt
        // poll and TSC accounting exact. Multi-member blocks need a per-member
        // poll contract (P1S4 next slice).
        for (int member = 0; member < 1; member++)
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
            if (!decoded.IsValid
                || !I386Translator.TryTranslate(decoded, memberEip, builder, out bool terminates, out _))
            {
                break;
            }

            offset += decoded.Length;
            length = offset;
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
