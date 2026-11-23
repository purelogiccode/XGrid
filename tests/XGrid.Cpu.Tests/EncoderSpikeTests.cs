using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Iced.Intel;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.Cpu.Tests;

/// <summary>
/// The C7 encoder spike (decisions 234): a size for the gap between the encoder
/// the raw emitter uses today (<c>Iced.Intel.Assembler.Assemble</c>, which runs
/// Iced's multi-pass <c>BlockEncoder</c>) and a single-pass encode of the same
/// resolved instruction list (<c>Iced.Intel.Encoder</c>), which is what a
/// purpose-built <c>FastAssembler</c> with fixed-size rel32 branches would cost.
/// </summary>
/// <remarks>
/// <para>
/// The numbers this prints are the input to the C7 decision, and the assertion
/// is the invariant such a rewrite depends on: both paths must produce the same
/// bytes for the same instruction list. The list is shaped like one emitted block
/// (prologue, 40 guest-member expansions from the measured mix, epilogue), not
/// like the whole 1.9M-instruction 1B population, so the ratio - not the
/// absolute - is what a decision uses. The single-pass leg re-encodes an
/// instruction list that has already been resolved, which is exactly the work a
/// <c>FastAssembler</c> would still have to do (it would additionally resolve
/// one rel32 label per branch, a dictionary lookup).
/// </para>
/// <para>
/// Measured on H1 (2026-10-22, Release, x64): assembler 390 ns/instruction,
/// single pass 120 ns/instruction (108 with a discarding writer), Iced's decoder
/// 34 ns/instruction, ratio 3.2x. The product-side price of `Assemble` in a real
/// 1B run is §233a's 359-377 ns per assembled instruction, so the list is
/// representative. Projection from §233a's stage table: `Assemble` is 682 ms of
/// the triangle's 1,026 ms formation (10.2% of the 6.66 s wall) and becomes
/// ~213 ms, i.e. **a ~7% triangle wall / ~3% menu wall lever** - below the C
/// stage's 10% bar and nowhere near the 5.5x gap, which is why C7 owns the
/// engine shape instead of the encoder.
/// </para>
/// </remarks>
public sealed class EncoderSpikeTests(ITestOutputHelper output)
{
    private const ulong BaseIp = 0x00007FF6_00000000;

    private static AssemblerRegister64 R64(Register register) => new(register);

    private static AssemblerRegister32 R32(Register register) => new(register);

    private static AssemblerMemoryOperand Mem(Register baseRegister, Register indexRegister, int scale, long displacement)
    {
        AssemblerMemoryOperand operand = R64(baseRegister) + (R64(indexRegister) * scale);
        return displacement == 0 ? operand : operand + displacement;
    }

    private static AssemblerMemoryOperand Mem(Register baseRegister, long displacement)
    {
        AssemblerMemoryOperand operand = R64(baseRegister) + 0L;
        return displacement == 0 ? operand : operand + displacement;
    }

    /// <summary>
    /// One block-shaped instruction list: prologue, 40 member expansions and the
    /// epilogue, using the member mix the 1B profile measured (§199j).
    /// </summary>
    private static Assembler BuildBlockLikeList()
    {
        var a = new Assembler(64);
        Label exit = a.CreateLabel(string.Empty);
        Label chainIn = a.CreateLabel(string.Empty);
        Label body = a.CreateLabel(string.Empty);

        // Prologue: save the frame, load the slots the members use.
        a.push(R64(Register.RBX));
        a.push(R64(Register.RBP));
        a.push(R64(Register.R12));
        a.sub(R64(Register.RSP), 0x20);
        a.mov(R64(Register.RBP), R64(Register.RCX));
        a.mov(R64(Register.RBX), R64(Register.RDX));
        a.jmp(body);
        a.Label(ref chainIn);
        a.mov(R32(Register.EAX), Mem(Register.RSP, 0x1C));
        a.Label(ref body);

        for (int i = 0; i < 40; i++)
        {
            a.mov(R32(Register.EAX), Mem(Register.RBP, Register.RDI, 1, 0));
            a.mov(R32(Register.ECX), Mem(Register.RBP, Register.RDI, 1, 4));
            a.add(R32(Register.EAX), R32(Register.ECX));
            a.cmp(R32(Register.EAX), 0x1234);
            a.je(exit);
            a.mov(Mem(Register.RBP, Register.RDI, 1, 0), R32(Register.EAX));
            a.inc(R32(Register.EDX));
            a.dec(R32(Register.EDI));
            a.test(R32(Register.EDX), R32(Register.EDX));
            a.jne(exit);
            a.and(R32(Register.ESI), 0x0FFF);
            a.shl(R32(Register.ESI), 4);
            a.movzx(R32(Register.EAX), new AssemblerRegister8(Register.R8L));
            a.mov(R32(Register.EDX), R32(Register.ESI));
            a.xor(R32(Register.EDX), R32(Register.EAX));
            a.cmp(R32(Register.EDX), Mem(Register.RBP, Register.RDI, 1, 12));
            a.jb(exit);
            a.or(R32(Register.EAX), R32(Register.EDX));
            a.shr(R32(Register.EAX), 2);
            a.mov(Mem(Register.RBP, Register.RDI, 1, 0x1C), R32(Register.EAX));
            a.mov(R32(Register.EAX), 0x20);
            a.add(R64(Register.RDI), R64(Register.RAX));
            a.mov(R32(Register.EAX), Mem(Register.RSP, 0x18));
            a.cmp(R64(Register.RDI), R64(Register.RAX));
            a.ja(exit);
        }

        // Epilogue: restore and return.
        a.Label(ref exit);
        a.add(R64(Register.RSP), 0x20);
        a.pop(R64(Register.R12));
        a.pop(R64(Register.RBP));
        a.pop(R64(Register.RBX));
        a.ret();
        return a;
    }

    private static byte[] Assemble(Assembler assembler, int repetitions, out long ticks)
    {
        var buffer = new byte[256 * 1024];
        var writer = new CountingCodeWriter(buffer);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            writer = new CountingCodeWriter(buffer);
            assembler.Assemble(writer, BaseIp, BlockEncoderOptions.ReturnNewInstructionOffsets);
        }

        ticks = Stopwatch.GetTimestamp() - start;
        byte[] bytes = new byte[writer.Position];
        Array.Copy(buffer, bytes, bytes.Length);
        return bytes;
    }

    private static byte[] EncodeSinglePass(Instruction[] instructions, int repetitions, out long ticks)
    {
        var writer = new CountingCodeWriter(new byte[256 * 1024]);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            writer = new CountingCodeWriter(writer.Buffer);
            var encoder = Encoder.Create(64, writer);
            ulong ip = BaseIp;
            foreach (Instruction instruction in instructions)
            {
                encoder.Encode(instruction, ip);
                ip += (ulong)instruction.Length;
            }
        }

        ticks = Stopwatch.GetTimestamp() - start;
        var bytes = new byte[writer.Position];
        Array.Copy(writer.Buffer, bytes, bytes.Length);
        return bytes;
    }

    /// <summary>The same encode with a discarding writer: isolates the encode cost from the byte writes.</summary>
    private static void EncodeSinglePassDiscarding(Instruction[] instructions, int repetitions, out long ticks)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            var writer = new NullCodeWriter();
            var encoder = Encoder.Create(64, writer);
            ulong ip = BaseIp;
            foreach (Instruction instruction in instructions)
            {
                encoder.Encode(instruction, ip);
                ip += (ulong)instruction.Length;
            }
        }

        ticks = Stopwatch.GetTimestamp() - start;
    }

    /// <summary>Encodes one instruction in a tight loop: the per-instruction floor of the same API.</summary>
    private static double MeasureOneInstruction(Instruction instruction, int repetitions)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            var writer = new NullCodeWriter();
            var encoder = Encoder.Create(64, writer);
            encoder.Encode(instruction, BaseIp);
        }

        long ticks = Stopwatch.GetTimestamp() - start;
        return 1e9 * ticks / Stopwatch.Frequency / repetitions;
    }

    /// <summary>Decodes the list back: a reference cost for the same per-instruction work.</summary>
    private static double MeasureDecode(Instruction[] instructions, int repetitions)
    {
        int bytes = 0;
        foreach (Instruction instruction in instructions)
        {
            bytes += instruction.Length;
        }

        var buffer = new byte[bytes];
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++)
        {
            var reader = new ByteArrayCodeReader(buffer);
            Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(64, reader);
            while (reader.CanReadByte)
            {
                decoder.Decode(out _);
            }
        }

        long ticks = Stopwatch.GetTimestamp() - start;
        return 1e9 * ticks / Stopwatch.Frequency / ((double)instructions.Length * repetitions);
    }

    /// <summary>Decodes assembled bytes back into an instruction list.</summary>
    private static Instruction[] Decode(byte[] code)
    {
        var reader = new ByteArrayCodeReader(code);
        Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(64, reader);
        decoder.IP = BaseIp;
        var list = new List<Instruction>();
        while (reader.CanReadByte)
        {
            decoder.Decode(out Instruction instruction);
            list.Add(instruction);
        }

        return list.ToArray();
    }

    [Fact]
    public void SinglePassEncoder_ReproducesTheAssemblerBytes()
    {
        Assembler assembler = BuildBlockLikeList();
        Instruction[] assembled = assembler.Instructions.ToArray();
        Assert.True(assembled.Length > 400, $"instruction list too small: {assembled.Length}");

        const int repetitions = 300;
        byte[] assemblerBytes = Assemble(assembler, repetitions, out long assemblerTicks);
        Instruction[] resolved = Decode(assemblerBytes);
        byte[] singleBytes = EncodeSinglePass(resolved, repetitions, out long singleTicks);
        EncodeSinglePassDiscarding(resolved, repetitions, out long nullTicks);
        double perOne = MeasureOneInstruction(resolved[8], 200_000);
        double perDecode = MeasureDecode(resolved, repetitions);

        double totalInstructions = (double)assembled.Length * repetitions;
        double perAssembler = 1e9 * assemblerTicks / Stopwatch.Frequency / totalInstructions;
        double perSingle = 1e9 * singleTicks / Stopwatch.Frequency / (resolved.Length * repetitions);
        double perNull = 1e9 * nullTicks / Stopwatch.Frequency / (resolved.Length * repetitions);
        output.WriteLine(
            $"instructions={assembled.Length} bytes={assemblerBytes.Length} decoded={resolved.Length} " +
            $"assemble={perAssembler:F1}ns singlePass={perSingle:F1}ns singlePassNull={perNull:F1}ns " +
            $"oneInstruction={perOne:F1}ns decode={perDecode:F1}ns ratio={perAssembler / perSingle:F1}x");

        Assert.Equal(assemblerBytes, singleBytes);
    }

    /// <summary>A code writer that records how many bytes the encoder produced.</summary>
    private sealed class CountingCodeWriter(byte[] buffer) : CodeWriter
    {
        public byte[] Buffer { get; } = buffer;

        public int Position { get; private set; }

        public override void WriteByte(byte value)
        {
            Buffer[Position++] = value;
        }
    }

    /// <summary>A code writer that discards bytes, so the encode cost is isolated from the memory writes.</summary>
    private sealed class NullCodeWriter : CodeWriter
    {
        public int Position { get; private set; }

        public override void WriteByte(byte value)
        {
            Position++;
        }
    }
}



