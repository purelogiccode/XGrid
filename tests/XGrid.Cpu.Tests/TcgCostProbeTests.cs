using System;
using System.Diagnostics;
using XGrid.Cpu.Tcg;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// P1S4 price probe: the TCG IR interpreter's member cost, measured on the
/// shapes the engine actually executes, against the documented raw runner cost
/// (6.3 ns/member, decisions section 239). A code generator can at best reach the
/// raw runner's own quality (the raw emitter already pins guest GPRs, keeps flags
/// in FLAGS and chains blocks), so this ratio bounds what P1S4 can buy over the
/// engines the product already has.
/// </summary>
public sealed class TcgCostProbeTests
{
    [Fact]
    public void InterpreterMemberCost_IsRecorded()
    {
        // The hot integer shape: a loop body with mov, add, a RAM load/store and
        // a cmp/jcc pair (the corpus's dominant classes).
        byte[] code =
        [
            0x8B, 0x03,             // mov eax, [ebx]
            0x05, 0x01, 0x00, 0x00, 0x00, // add eax, 1
            0x89, 0x43, 0x04,       // mov [ebx+4], eax
            0x3D, 0x00, 0x01, 0x00, 0x00, // cmp eax, 0x100
            0x75, 0xF2,             // jne -14
        ];

        var builder = new TcgBuilder(0x1000);
        int offset = 0;
        while (offset < code.Length)
        {
            DecodedInstruction decoded = Decoder.Decode(code.AsSpan(offset));
            Assert.True(
                I386Translator.TryTranslate(decoded, (uint)(0x1000 + offset), builder, out bool terminates, out string? reason),
                reason);
            offset += decoded.Length;
            if (terminates)
            {
                break;
            }
        }

        TcgProgram program = builder.Build();
        var registers = new uint[8];
        registers[3] = 0x2000;
        var memory = new ConstantMemory();
        var initialFlags = 0x202u;

        // Warm up the JIT and the memory, then measure.
        for (int i = 0; i < 1000; i++)
        {
            TcgInterpreter.Execute(program, registers, initialFlags, default, memory);
        }

        const int iterations = 2_000_000;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            TcgInterpreter.Execute(program, registers, initialFlags, default, memory);
        }

        stopwatch.Stop();
        double nanosecondPerMember = stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / (iterations * program.Ops.Count);
        Assert.True(nanosecondPerMember > 0);
        Console.WriteLine(
            $"tcg-interpreter: ops={program.Ops.Count} iterations={iterations} " +
            $"nsPerMember={nanosecondPerMember:F2} rawRunner=6.30 ratio={nanosecondPerMember / 6.30:F1}x");
    }

    /// <summary>A RAM-like memory with no bus dispatch (the comparison is the interpreter's own cost).</summary>
    private sealed class ConstantMemory : ITcgMemory
    {
        public byte Read8(uint address)
        {
            return 0x11;
        }

        public ushort Read16(uint address)
        {
            return 0x1122;
        }

        public uint Read32(uint address)
        {
            return 0x11223344;
        }

        public void Write8(uint address, byte value)
        {
        }

        public void Write16(uint address, ushort value)
        {
        }

        public void Write32(uint address, uint value)
        {
        }
    }
}
