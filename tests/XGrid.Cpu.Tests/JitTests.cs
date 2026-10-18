using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Fast-mode block JIT (J1 slice) differential tests: every program runs on
/// the plain interpreter and on the JIT-enabled engine over identical code
/// and initial state, then the full architectural state (including the
/// snapshot bytes and all of RAM) must match exactly. Programs are wrapped in
/// short loops wherever the JIT should demonstrably serve blocks, because the
/// first pass over fresh code is always a transient formation bail (the
/// member path fills the decode cache) — the second pass is JIT-served.
/// </summary>
public sealed class JitTests
{
    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    [Fact]
    public void AluLoop_MatchesInterpreter_AndServesBlocks()
    {
        // mov ecx,5; mov eax,1; mov ebx,0x10
        // loop: add eax,ebx; xor edx,edx; sub eax,2; nop; dec ecx; jne loop; hlt
        byte[] code =
        [
            0xB9, 0x05, 0x00, 0x00, 0x00,
            0xB8, 0x01, 0x00, 0x00, 0x00,
            0xBB, 0x10, 0x00, 0x00, 0x00,
            0x01, 0xD8,
            0x31, 0xD2,
            0x2D, 0x02, 0x00, 0x00, 0x00,
            0x90,
            0x49,
            0x75, 0xF3,
            0xF4,
        ];

        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        Preset(plain);
        Preset(jit);

        plain.Execute(100_000);
        jit.Execute(100_000);

        AssertSameState(plain, jit);
        Assert.True(jit.Context.Halted);
        Assert.True(jit.JitServedTotal > 0, $"JIT served {jit.JitServedTotal} instructions.");
        Assert.True(jit.JitBlocksBuilt > 0);
    }

    [Fact]
    public void MemoryShapes_MatchesInterpreter_AndServesBlocks()
    {
        // eax=&0x2000; ebx=0xAABBCCDD; [eax]=ebx; ecx=[eax]; inc ecx;
        // edx=0x2004; [edx]=ecx; esi=[edx]; dec esi; [edx+4]=esi; edi=[edx+4];
        // ecx=0x2FFE; [ecx]=ebx (cross-page); ebp=[ecx]; jmp +0 (loop-free, hlt next)
        byte[] code =
        [
            0xB8, 0x00, 0x20, 0x00, 0x00,
            0xBB, 0xDD, 0xCC, 0xBB, 0xAA,
            0x89, 0x18,
            0x8B, 0x08,
            0x41,
            0xBA, 0x04, 0x20, 0x00, 0x00,
            0x89, 0x0A,
            0x8B, 0x32,
            0x4E,
            0x89, 0x72, 0x04,
            0x8B, 0x7A, 0x04,
            0xB9, 0xFE, 0x2F, 0x00, 0x00,
            0x89, 0x19,
            0x8B, 0x29,
            0xF4,
        ];

        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        Preset(plain);
        Preset(jit);

        // Two passes: run to the HLT, rewind both to the head, run again.
        plain.Execute(1000);
        jit.Execute(1000);
        AssertSameState(plain, jit);
        plain.Context.Halted = false;
        jit.Context.Halted = false;
        plain.Context.Eip = CpuHarness.CodeAddress;
        jit.Context.Eip = CpuHarness.CodeAddress;
        plain.Execute(1000);
        jit.Execute(1000);

        AssertSameState(plain, jit);
        Assert.Equal(0xAABBCCDDu, jit.Ram().Read32(0x2000));
        Assert.Equal(0xAABBCCDDu, jit.Ram().Read32(0x2FFE));
        Assert.True(jit.JitServedTotal > 0, $"JIT served {jit.JitServedTotal} instructions.");
    }

    [Fact]
    public void JccConditions_Sweep_MatchesInterpreter()
    {
        byte[] code = ConditionSweepProgram();
        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        Preset(plain);
        Preset(jit);

        // Two passes: the first fills the decode cache (transient JIT bails),
        // the second is served by formed blocks.
        plain.Execute(100_000);
        jit.Execute(100_000);
        AssertSameState(plain, jit);
        Assert.True(jit.Context.Halted);
        plain.Context.Halted = false;
        jit.Context.Halted = false;
        plain.Context.Eip = CpuHarness.CodeAddress;
        jit.Context.Eip = CpuHarness.CodeAddress;
        plain.Execute(100_000);
        jit.Execute(100_000);

        AssertSameState(plain, jit);
        Assert.True(jit.Context.Halted);
        Assert.True(jit.JitServedTotal > 0, $"JIT served {jit.JitServedTotal} instructions.");
    }

    [Fact]
    public void PagedCrossVisitFault_DeliveredFromJit_MatchesInterpreter()
    {
        // esi=0x2000; ecx=2; loop: eax=[esi]; esi+=0x7000; dec ecx; jne loop; hlt.
        // First visit loads the mapped 0x2000 page (this pass also warms the
        // decode cache); the second visit faults on the unmapped 0x9000 page
        // through the JIT-served block.
        byte[] code =
        [
            0xBE, 0x00, 0x20, 0x00, 0x00,
            0xB9, 0x02, 0x00, 0x00, 0x00,
            0x8B, 0x06,
            0x81, 0xC6, 0x00, 0x70, 0x00, 0x00,
            0x49,
            0x75, 0xF5,
            0xF4,
        ];

        XboxCpu plain = CreatePaged(code);
        XboxCpu jit = CreateJit(code);
        foreach (XboxCpu cpu in new[] { plain, jit })
        {
            MapIdentity(cpu.Bus(), 1, 1);
            MapIdentity(cpu.Bus(), 2, 1);
            MapIdentity(cpu.Bus(), 4, 1);
            EnablePaging(cpu);
            cpu.Ram().Write32(0x2000, 0xAABBCCDD);
            cpu.Ram().Write8(0x1500, 0xF4);
            InstallIdt(cpu, 14, 0x1500);
        }

        Preset(plain);
        Preset(jit);
        plain.Context.Esp = 0x2F00;
        jit.Context.Esp = 0x2F00;
        plain.Execute(10_000);
        jit.Execute(10_000);

        AssertSameState(plain, jit);
        Assert.Equal(14, jit.LastFaultVector);
        Assert.Equal(0x100Au, jit.LastFaultEip);
        Assert.Equal(0x9000u, jit.Context.Cr2);
        Assert.Equal(1ul, jit.JitFaults);
        Assert.True(jit.JitServedTotal > 0, $"JIT served {jit.JitServedTotal} instructions.");
    }

    [Fact]
    public void NonEmittableHead_RejectedAndFallsBack()
    {
        // push eax; nop; jmp $-2 (loops over the push head: the second visit
        // finds a cached entry and records a permanent rejection).
        byte[] code = [0x50, 0x90, 0xEB, 0xFC];

        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        Preset(plain);
        Preset(jit);

        plain.Execute(50);
        jit.Execute(50);

        AssertSameState(plain, jit);
        Assert.True(jit.JitRejectedHeads >= 1);
    }

    [Fact]
    public void DecodeCacheOff_JitInert_MatchesInterpreter()
    {
        byte[] code =
        [
            0xB9, 0x03, 0x00, 0x00, 0x00,
            0x40,
            0x49,
            0x75, 0xFC,
            0xF4,
        ];

        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        jit.DecodeCacheEnabled = false;
        Preset(plain);
        Preset(jit);

        plain.Execute(1000);
        jit.Execute(1000);

        AssertSameState(plain, jit);
        Assert.Equal(0ul, jit.JitServedTotal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RandomPrograms_MatchInterpreter(int seed)
    {
        byte[] code = RandomProgram(seed, 48);
        XboxCpu plain = CpuHarness.Create(code);
        XboxCpu jit = CreateJit(code);
        Preset(plain);
        Preset(jit);

        plain.Execute(400);
        jit.Execute(400);

        AssertSameState(plain, jit);
    }

    private static XboxCpu CreateJit(byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        cpu.DecodeCacheEnabled = true;
        cpu.SpecExecutionEnabled = true;
        cpu.JitEnabled = true;
        return cpu;
    }

    private static XboxCpu CreatePaged(byte[] code)
    {
        return CpuHarness.Create(code);
    }

    private static void Preset(XboxCpu cpu)
    {
        cpu.Context.Eax = 0x11111111;
        cpu.Context.Ebx = 0x00002000;
        cpu.Context.Ecx = 0x33333333;
        cpu.Context.Edx = 0x44444444;
        cpu.Context.Esi = 0x55555555;
        cpu.Context.Edi = 0x66666666;
        cpu.Context.Ebp = 0x00007000;
        cpu.Context.Esp = CpuHarness.StackAddress;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf;
        cpu.Ram().Write32(0x2000, 0xAABBCCDD);
        cpu.Ram().Write32(0x5000, 0x11223344);
    }

    private static void MapIdentity(AddressSpace bus, uint firstVpn, uint count)
    {
        bus.Write32(PageDirectory, PageTable | 3u);
        for (uint vpn = firstVpn; vpn < firstVpn + count; vpn++)
        {
            bus.Write32(PageTable + vpn * 4, (vpn << 12) | 3u);
        }
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler)
    {
        const uint idt = 0x4000;
        uint gate = idt + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = idt;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    private static void AssertSameState(XboxCpu expected, XboxCpu actual)
    {
        Assert.Equal(expected.Context.Eip, actual.Context.Eip);
        Assert.Equal(expected.Context.Eflags, actual.Context.Eflags);
        Assert.Equal(expected.Context.Eax, actual.Context.Eax);
        Assert.Equal(expected.Context.Ebx, actual.Context.Ebx);
        Assert.Equal(expected.Context.Ecx, actual.Context.Ecx);
        Assert.Equal(expected.Context.Edx, actual.Context.Edx);
        Assert.Equal(expected.Context.Esi, actual.Context.Esi);
        Assert.Equal(expected.Context.Edi, actual.Context.Edi);
        Assert.Equal(expected.Context.Ebp, actual.Context.Ebp);
        Assert.Equal(expected.Context.Esp, actual.Context.Esp);
        Assert.Equal(expected.Context.Tsc, actual.Context.Tsc);
        Assert.Equal(expected.Context.Halted, actual.Context.Halted);
        Assert.Equal(expected.Context.Cr2, actual.Context.Cr2);
        Assert.Equal(expected.InterruptCount, actual.InterruptCount);
        Assert.Equal(expected.LastFaultVector, actual.LastFaultVector);
        Assert.Equal(expected.LastFaultEip, actual.LastFaultEip);
        Assert.Equal(expected.LastFaultErrorCode, actual.LastFaultErrorCode);
        Assert.Equal(StateBytes(expected), StateBytes(actual));

        PhysicalMemory expectedRam = expected.Ram();
        PhysicalMemory actualRam = actual.Ram();
        for (uint address = 0; address < 0x10000; address += 4)
        {
            uint expectedWord = expectedRam.Read32(address);
            uint actualWord = actualRam.Read32(address);
            if (expectedWord != actualWord)
            {
                Assert.Fail(
                    $"RAM 0x{address:X4}: expected 0x{expectedWord:X8} actual 0x{actualWord:X8} " +
                    $"(eip 0x{expected.Context.Eip:X8}/0x{actual.Context.Eip:X8})");
            }
        }
    }

    private static byte[] StateBytes(XboxCpu cpu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        return stream.ToArray();
    }

    /// <summary>Builds the 32-row Jcc sweep (every condition, taken and not-taken setups).</summary>
    private static byte[] ConditionSweepProgram()
    {
        (ConditionCode Cc, uint V1, uint V2)[] rows =
        [
            (ConditionCode.Overflow, 0x80000000u, 1u),
            (ConditionCode.Overflow, 1u, 1u),
            (ConditionCode.NotOverflow, 1u, 1u),
            (ConditionCode.NotOverflow, 0x80000000u, 1u),
            (ConditionCode.Below, 1u, 2u),
            (ConditionCode.Below, 2u, 1u),
            (ConditionCode.AboveOrEqual, 2u, 1u),
            (ConditionCode.AboveOrEqual, 1u, 2u),
            (ConditionCode.Equal, 5u, 5u),
            (ConditionCode.Equal, 5u, 6u),
            (ConditionCode.NotEqual, 5u, 6u),
            (ConditionCode.NotEqual, 5u, 5u),
            (ConditionCode.BelowOrEqual, 5u, 5u),
            (ConditionCode.BelowOrEqual, 2u, 1u),
            (ConditionCode.Above, 2u, 1u),
            (ConditionCode.Above, 5u, 5u),
            (ConditionCode.Sign, 1u, 2u),
            (ConditionCode.Sign, 2u, 1u),
            (ConditionCode.NotSign, 2u, 1u),
            (ConditionCode.NotSign, 1u, 2u),
            (ConditionCode.Parity, 3u, 0u),
            (ConditionCode.Parity, 3u, 1u),
            (ConditionCode.NotParity, 3u, 1u),
            (ConditionCode.NotParity, 3u, 0u),
            (ConditionCode.Less, 0x80000000u, 1u),
            (ConditionCode.Less, 2u, 1u),
            (ConditionCode.GreaterOrEqual, 1u, 1u),
            (ConditionCode.GreaterOrEqual, 0x80000000u, 1u),
            (ConditionCode.LessOrEqual, 5u, 5u),
            (ConditionCode.LessOrEqual, 6u, 5u),
            (ConditionCode.Greater, 6u, 5u),
            (ConditionCode.Greater, 5u, 5u),
        ];

        var code = new List<byte>();
        foreach ((ConditionCode cc, uint v1, uint v2) in rows)
        {
            code.Add(0xB8);
            AppendU32(code, v1);
            code.Add(0xBB);
            AppendU32(code, v2);
            code.Add(0x39);
            code.Add(0xD8);
            code.Add((byte)(0x70 + (int)cc));
            code.Add(0x05);
            code.Add(0xBA);
            AppendU32(code, 0x99);
        }

        code.Add(0xF4);
        return code.ToArray();
    }

    /// <summary>
    /// Random program over the J1 emittable shapes: register/immediate data
    /// ops, absolute-disp32 loads/stores, and backward jcc/jmp branches to
    /// earlier instruction boundaries (bounded by the execute cap). Every
    /// program terminates in HLT when reached.
    /// </summary>
    private static byte[] RandomProgram(int seed, int instructionCount)
    {
        var random = new Random(seed);
        var code = new List<byte>();
        var boundaries = new List<int>();
        for (int i = 0; i < instructionCount; i++)
        {
            boundaries.Add(code.Count);
            switch (random.Next(12))
            {
                case 0:
                    code.Add((byte)(0xB8 + random.Next(8)));
                    AppendU32(code, (uint)random.Next());
                    break;
                case 1:
                    code.Add(0x89);
                    code.Add((byte)(0xC0 | (random.Next(8) << 3) | random.Next(8)));
                    break;
                case 2:
                    code.Add((byte)new byte[] { 0x01, 0x29, 0x21, 0x09, 0x31, 0x39 }[random.Next(6)]);
                    code.Add((byte)(0xC0 | (random.Next(8) << 3) | random.Next(8)));
                    break;
                case 3:
                    {
                        byte[] groups = [0xC0, 0xC8, 0xE0, 0xE8, 0xF0, 0xF8];
                        code.Add(0x81);
                        code.Add((byte)(groups[random.Next(6)] | random.Next(8)));
                        AppendU32(code, (uint)random.Next());
                        break;
                    }
                case 4:
                    code.Add(0x85);
                    code.Add((byte)(0xC0 | (random.Next(8) << 3) | random.Next(8)));
                    break;
                case 5:
                    code.Add((byte)((random.Next(2) == 0 ? 0x40 : 0x48) + random.Next(8)));
                    break;
                case 6:
                    code.Add(0x90);
                    break;
                case 7:
                    code.Add(0x8B);
                    code.Add((byte)((random.Next(8) << 3) | 0x05));
                    AppendU32(code, (uint)(0x2000 + random.Next(0x1000) * 4));
                    break;
                case 8:
                    code.Add(0x89);
                    code.Add((byte)((random.Next(8) << 3) | 0x05));
                    AppendU32(code, (uint)(0x2000 + random.Next(0x1000) * 4));
                    break;
                case 9:
                    code.Add((byte)(0x70 + random.Next(16)));
                    code.Add((byte)PickBackwardRel(boundaries, code.Count, random));
                    break;
                case 10:
                    code.Add(0xEB);
                    code.Add((byte)PickBackwardRel(boundaries, code.Count, random));
                    break;
                default:
                    code.Add(0x90);
                    break;
            }
        }

        code.Add(0xF4);
        return code.ToArray();
    }

    /// <summary>
    /// Picks a rel8 for a branch whose rel byte will sit at index
    /// <paramref name="relIndex"/>: a backward jump to a previous instruction
    /// boundary when one fits, else 0 (fall through).
    /// </summary>
    private static int PickBackwardRel(List<int> boundaries, int relIndex, Random random)
    {
        var candidates = new List<int>();
        for (int i = boundaries.Count - 1; i >= 0; i--)
        {
            int rel = boundaries[i] - (relIndex + 1);
            if (rel < -120)
            {
                break;
            }

            if (rel <= -2)
            {
                candidates.Add(rel);
            }
        }

        return candidates.Count == 0 ? 0 : candidates[random.Next(candidates.Count)];
    }

    private static void AppendU32(List<byte> code, uint value)
    {
        code.Add((byte)value);
        code.Add((byte)(value >> 8));
        code.Add((byte)(value >> 16));
        code.Add((byte)(value >> 24));
    }
}
