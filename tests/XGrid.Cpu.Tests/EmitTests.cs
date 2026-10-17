using System.IO;
using System.Text;
using XGrid.Core.Devices;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// First-codegen slice tests (§171): the emitted tail runner matches the
/// member path (and the flag-off interpreter) on straight chains, Jcc loops,
/// mid-tail faults, mid-tail IRQ delivery across an STI, self-modifying
/// stores, fallback shapes, and the observer/verify gates.
/// </summary>
public sealed class EmitTests
{
    private static readonly byte[] Chain =
    {
        0x89, 0xD8, // mov eax,ebx
        0x01, 0xC8, // add eax,ecx
        0x29, 0xD0, // sub eax,edx
        0x31, 0xF6, // xor esi,esi
        0x40, // inc eax
        0x4E, // dec esi
        0x8B, 0x3B, // mov edi,[ebx] (ebx=0x2000 preset)
        0x89, 0x05, 0x00, 0x50, 0x00, 0x00, // mov [0x5000],eax
        0x90, // nop
        0xEB, 0xEB, // jmp head (19+2 bytes back; hlt has no handler so chains loop)
    };

    private static readonly byte[] Loop = { 0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8 };

    [Fact]
    public void StraightChain_MatchesSpecAndPlain_ServesTails()
    {
        XboxCpu plain = Create(EmitMode.Off, Chain);
        XboxCpu spec = Create(EmitMode.Spec, Chain);
        XboxCpu emit = Create(EmitMode.Emit, Chain);
        Preset(plain);
        Preset(spec);
        Preset(emit);

        Assert.Equal(320, plain.Execute(320));
        Assert.Equal(320, spec.Execute(320));
        Assert.Equal(320, emit.Execute(320));
        AssertSameState(plain, spec);
        AssertSameState(plain, emit);
        AssertSameCounters(spec, emit);
        Assert.False(plain.Context.Halted);

        DecodedBlockCache? stats = emit.SpecStatistics;
        Assert.NotNull(stats);
        Assert.True(stats.EmitBuilds > 0);
        Assert.True(stats.EmitRan > 0);
    }

    [Fact]
    public void DecJnzLoop_MatchesSpec()
    {
        XboxCpu spec = Create(EmitMode.Spec, Loop);
        XboxCpu emit = Create(EmitMode.Emit, Loop);
        Preset(spec);
        Preset(emit);

        Assert.Equal(spec.Execute(200), emit.Execute(200));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.True(emit.SpecStatistics!.EmitRan > 0);
    }

    [Fact]
    public void FaultMidTail_MatchesSpec_PreciseCr2()
    {
        // mov ebx,[ecx] with ECX steered by register: phase A maps VPN2 and
        // warms the block fault-free; phase B points ECX at unmapped VPN9 and
        // rewinds, so the warmed tail faults mid-tail (a fault on a cold
        // block would halt before any tail forms). The vector-14 handler is
        // a bare HLT that stops the run.
        byte[] code = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0x90, 0x8B, 0x19, 0x90, 0x90, 0xEB, 0xF4 };

        XboxCpu spec = CreatePaged(EmitMode.Spec, code);
        XboxCpu emit = CreatePaged(EmitMode.Emit, code);
        spec.Context.Ecx = 0x2000;
        emit.Context.Ecx = 0x2000;

        Assert.Equal(spec.Execute(96), emit.Execute(96));
        AssertSameState(spec, emit);

        spec.Context.Ecx = 0x9000;
        emit.Context.Ecx = 0x9000;
        spec.Context.Eip = 0x1000;
        emit.Context.Eip = 0x1000;

        Assert.Equal(spec.Execute(16), emit.Execute(16));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(14, emit.LastFaultVector);
        Assert.Equal(0x1006u, emit.LastFaultEip);
        Assert.Equal(0x9000u, emit.Context.Cr2);
        Assert.Equal(1u, emit.Context.Eax);
        Assert.True(emit.SpecStatistics!.EmitRan > 0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void IrqMidTail_MatchesSpec(int fireAt)
    {
        byte[] code = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xF8 };

        XboxCpu spec = CreateIrq(EmitMode.Spec, code, fireAt);
        XboxCpu emit = CreateIrq(EmitMode.Emit, code, fireAt);

        Assert.Equal(spec.Execute(32), emit.Execute(32));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(1, emit.InterruptCount);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(73)]
    [InlineData(74)]
    [InlineData(75)]
    [InlineData(76)]
    [InlineData(77)]
    [InlineData(78)]
    public void IrqSteadyState_FiresMidTail_MatchesSpec(int fireAt)
    {
        // The source stays quiet through warm-up (blocks form, the tiering
        // threshold passes, tails emit), then fires at the given ack: across
        // the sweep delivery lands at every member of the 7-member block on
        // both sides (7 cold-step acks, then polls run member-0, tail-1, ...
        // per iteration, so acks 72-78 cover tail-1..6 plus member-0 at
        // threshold-crossed visits). Unlike faults, a pending exit earns no
        // decode hit (the poll precedes it on the member path), which
        // AssertSameCounters pins.
        byte[] code = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xF8 };

        XboxCpu spec = CreateIrq(EmitMode.Spec, code, fireAt);
        XboxCpu emit = CreateIrq(EmitMode.Emit, code, fireAt);

        Assert.Equal(spec.Execute(200), emit.Execute(200));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(1, emit.InterruptCount);
        Assert.True(emit.SpecStatistics!.EmitRan > 0);
    }

    [Fact]
    public void StiFlip_DeliveryBeforeEmittedBlocks()
    {
        // IF starts clear with an always-firing source. STI has no handler
        // (fallback) so its block runs the member path; the following block
        // is fully specialized with IF set, so delivery lands with live IF
        // while emitted tails serve the steady state. An IF flip cannot
        // happen inside an emitted tail (every IF writer is a fallback or a
        // terminator), which is exactly why the flip lives in the preceding
        // block here.
        // 0x1000 nop; 0x1001 sti; 0x1002 jmp +2 -> 0x1006;
        // 0x1004/0x1005 skipped; 0x1006..0x1008 nops; 0x1009 jmp 0x1006.
        byte[] code = { 0x90, 0xFB, 0xEB, 0x02, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xFB };

        XboxCpu spec = CreateSti(EmitMode.Spec, code);
        XboxCpu emit = CreateSti(EmitMode.Emit, code);

        Assert.Equal(spec.Execute(320), emit.Execute(320));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(1, emit.InterruptCount);
        Assert.True(emit.SpecStatistics!.EmitRan > 0);
    }

    [Fact]
    public void SelfModifyingStore_MidTailBail_MatchesSpec()
    {
        // nop; mov [0x1006],al; nops; jmp head. The store drops the code
        // page on every iteration, so entries never accumulate and no block
        // ever forms across the store (formation poisoning): every iteration
        // runs the miss path on both sides. EmitRan stays 0 by construction
        // here; the mid-tail generation check exists for device writes, which
        // CPU stores cannot produce mid-tail. What this pins is exactness of
        // the perpetual-miss SMC path plus patch coherence (the last store
        // wrote the then-current AL).
        byte[] code = { 0x90, 0xA2, 0x06, 0x10, 0x00, 0x00, 0x90, 0x90, 0xEB, 0xF6 };

        XboxCpu spec = Create(EmitMode.Spec, code);
        XboxCpu emit = Create(EmitMode.Emit, code);
        Preset(spec);
        Preset(emit);
        spec.Context.Eax = 0x40404040;
        emit.Context.Eax = 0x40404040;

        Assert.Equal(spec.Execute(32), emit.Execute(32));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal((byte)(emit.Context.Eax & 0xFF), emit.Ram().Read8(0x1006));
    }

    [Fact]
    public void FallbackShape_FallsBackExact()
    {
        // CPUID terminates its block with no handler, so every head
        // iteration counts one fallback while the nops block emits.
        byte[] code = { 0x90, 0x0F, 0xA2, 0x90, 0x90, 0x90, 0xEB, 0xF8 };

        XboxCpu spec = Create(EmitMode.Spec, code);
        XboxCpu emit = Create(EmitMode.Emit, code);
        Preset(spec);
        Preset(emit);

        Assert.Equal(spec.Execute(320), emit.Execute(320));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(spec.SpecStatistics!.ShapeFallbacks, emit.SpecStatistics!.ShapeFallbacks);
        Assert.True(emit.SpecStatistics!.ShapeFallbacks > 0);
    }

    [Fact]
    public void Gates_VerifyAndObserversSkipEmit()
    {
        XboxCpu verified = Create(EmitMode.Emit, Chain);
        Preset(verified);
        verified.BlockVerify = true;
        XboxCpu spec = Create(EmitMode.Spec, Chain);
        Preset(spec);

        Assert.Equal(spec.Execute(32), verified.Execute(32));
        AssertSameState(spec, verified);
        Assert.Equal(0u, verified.SpecStatistics!.EmitRan);

        XboxCpu observed = Create(EmitMode.Emit, Chain);
        Preset(observed);
        var profile = new HotspotProfiler();
        observed.HotspotObserver = profile;
        XboxCpu specObserved = Create(EmitMode.Spec, Chain);
        Preset(specObserved);
        var specProfile = new HotspotProfiler();
        specObserved.HotspotObserver = specProfile;

        Assert.Equal(specObserved.Execute(32), observed.Execute(32));
        AssertSameState(specObserved, observed);
        Assert.Equal(0u, observed.SpecStatistics!.EmitRan);
        Assert.Equal(specProfile.TotalInstructions, profile.TotalInstructions);
    }

    private enum EmitMode
    {
        Off,
        Spec,
        Emit,
    }

    private static XboxCpu Create(EmitMode mode, byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        if (mode != EmitMode.Off)
        {
            cpu.DecodeCacheEnabled = true;
            cpu.SpecExecutionEnabled = true;
        }

        if (mode == EmitMode.Emit)
        {
            cpu.EmitEnabled = true;
        }

        return cpu;
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
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf;
        cpu.Ram().Write32(0x2000, 0xAABBCCDD);
        cpu.Ram().Write32(0x5000, 0x11223344);
    }

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    private static XboxCpu CreatePaged(EmitMode mode, byte[] code)
    {
        XboxCpu cpu = Create(mode, code);
        MapIdentity(cpu.Bus(), 1, 1);
        MapIdentity(cpu.Bus(), 2, 1);
        MapIdentity(cpu.Bus(), 4, 1);
        MapIdentity(cpu.Bus(), 7, 2);
        EnablePaging(cpu);
        cpu.Ram().Write32(0x2000, 0xAABBCCDD);
        cpu.Ram().Write8(0x1500, 0xF4);
        InstallIdt(cpu, 14, 0x1500);
        return cpu;
    }

    private static XboxCpu CreateIrq(EmitMode mode, byte[] code, int fireAt)
    {
        XboxCpu cpu = Create(mode, code);
        cpu.Ram().Write8(0x3000, 0xCF);
        InstallIdt(cpu, 0x21, 0x3000);
        cpu.Context.Eflags |= FlagBits.If;
        cpu.InterruptSource = new CountedInterruptSource(0x21, fireAt);
        return cpu;
    }

    private static XboxCpu CreateSti(EmitMode mode, byte[] code)
    {
        XboxCpu cpu = Create(mode, code);
        cpu.Ram().Write8(0x3000, 0xCF);
        InstallIdt(cpu, 0x21, 0x3000);
        cpu.Context.Eflags &= ~FlagBits.If;
        cpu.InterruptSource = new CountedInterruptSource(0x21, 1);
        return cpu;
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

        PhysicalMemory plainRam = expected.Ram();
        PhysicalMemory specRam = actual.Ram();
        for (uint address = 0; address < 0x10000; address += 4)
        {
            Assert.Equal(plainRam.Read32(address), specRam.Read32(address));
        }
    }

    /// <summary>
    /// Host-serve counters must match exactly across serve vehicles: the
    /// emit path replicates every member-path counting point, including the
    /// in-flight member of fault/pending exits.
    /// </summary>
    private static void AssertSameCounters(XboxCpu expected, XboxCpu actual)
    {
        Assert.Equal(expected.DecodeCacheHits, actual.DecodeCacheHits);
        Assert.Equal(expected.DecodeCacheMisses, actual.DecodeCacheMisses);
        Assert.Equal(expected.DecodeCount, actual.DecodeCount);
        DecodedBlockCache? expectedBlocks = expected.SpecStatistics;
        DecodedBlockCache? actualBlocks = actual.SpecStatistics;
        Assert.NotNull(expectedBlocks);
        Assert.NotNull(actualBlocks);
        Assert.Equal(expectedBlocks.Dispatches, actualBlocks.Dispatches);
        Assert.Equal(expectedBlocks.Instructions, actualBlocks.Instructions);
        Assert.Equal(expectedBlocks.Builds, actualBlocks.Builds);
        Assert.Equal(expectedBlocks.Hits, actualBlocks.Hits);
        Assert.Equal(expectedBlocks.Completed, actualBlocks.Completed);
        Assert.Equal(expectedBlocks.Redirects, actualBlocks.Redirects);
        Assert.Equal(expectedBlocks.BoundaryExits, actualBlocks.BoundaryExits);
        Assert.Equal(expectedBlocks.ShapeHits, actualBlocks.ShapeHits);
        Assert.Equal(expectedBlocks.ShapeFallbacks, actualBlocks.ShapeFallbacks);
        Assert.Equal(expectedBlocks.SpecRan, actualBlocks.SpecRan);
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

    /// <summary>Fires one vector at the Nth acknowledge.</summary>
    private sealed class CountedInterruptSource : IInterruptSource
    {
        private readonly int _vector;
        private readonly int _fireAt;
        private int _acks;

        public CountedInterruptSource(int vector, int fireAt)
        {
            _vector = vector;
            _fireAt = fireAt;
        }

        public int Acknowledge()
        {
            _acks++;
            return _acks == _fireAt ? _vector : -1;
        }
    }
}
