using System;
using System.Collections.Generic;
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
    [
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
    ];

    private static readonly byte[] Loop = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8];

    [Fact]
    public void StraightChain_MatchesSpecAndPlain_ServesTails()
    {
        XboxCpu plain = Create(EmitMode.Off, Chain);
        XboxCpu spec = Create(EmitMode.Spec, Chain);
        XboxCpu emit = Create(EmitMode.Emit, Chain);
        Preset(plain);
        Preset(spec);
        Preset(emit);

        Assert.Equal(12800, plain.Execute(12800));
        Assert.Equal(12800, spec.Execute(12800));
        Assert.Equal(12800, emit.Execute(12800));
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

        Assert.Equal(spec.Execute(10240), emit.Execute(10240));
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
        byte[] code = [0xB8, 0x01, 0x00, 0x00, 0x00, 0x90, 0x8B, 0x19, 0x90, 0x90, 0xEB, 0xF4];

        XboxCpu spec = CreatePaged(EmitMode.Spec, code);
        XboxCpu emit = CreatePaged(EmitMode.Emit, code);
        spec.Context.Ecx = 0x2000;
        emit.Context.Ecx = 0x2000;

        Assert.Equal(spec.Execute(6400), emit.Execute(6400));
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
        byte[] code = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xF8];

        XboxCpu spec = CreateIrq(EmitMode.Spec, code, fireAt);
        XboxCpu emit = CreateIrq(EmitMode.Emit, code, fireAt);

        Assert.Equal(spec.Execute(32), emit.Execute(32));
        AssertSameState(spec, emit);
        AssertSameCounters(spec, emit);
        Assert.Equal(1, emit.InterruptCount);
    }

    [Theory]
    [InlineData(7208)]
    [InlineData(7209)]
    [InlineData(7210)]
    [InlineData(7211)]
    [InlineData(7212)]
    [InlineData(7213)]
    [InlineData(7214)]
    public void IrqSteadyState_FiresMidTail_MatchesSpec(int fireAt)
    {
        // The source stays quiet through warm-up (blocks form, the tiering
        // threshold passes, tails emit), then fires at the given ack: across
        // the sweep delivery lands at every member of the 7-member block on
        // both sides (7 cold-step acks, then polls run member-0, tail-1, ...
        // per iteration, so acks 7208-7214 cover all 7 positions at well
        // past-threshold visits). Unlike faults, a pending
        // exit earns no decode hit (the poll precedes it on the member
        // path), which AssertSameCounters pins.
        byte[] code = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xF8];

        XboxCpu spec = CreateIrq(EmitMode.Spec, code, fireAt);
        XboxCpu emit = CreateIrq(EmitMode.Emit, code, fireAt);

        Assert.Equal(spec.Execute(8000), emit.Execute(8000));
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
        byte[] code = [0x90, 0xFB, 0xEB, 0x02, 0x90, 0x90, 0x90, 0x90, 0x90, 0xEB, 0xFB];

        XboxCpu spec = CreateSti(EmitMode.Spec, code);
        XboxCpu emit = CreateSti(EmitMode.Emit, code);

        Assert.Equal(spec.Execute(5120), emit.Execute(5120));
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
        byte[] code = [0x90, 0xA2, 0x06, 0x10, 0x00, 0x00, 0x90, 0x90, 0xEB, 0xF6];

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
        byte[] code = [0x90, 0x0F, 0xA2, 0x90, 0x90, 0x90, 0xEB, 0xF8];

        XboxCpu spec = Create(EmitMode.Spec, code);
        XboxCpu emit = Create(EmitMode.Emit, code);
        Preset(spec);
        Preset(emit);

        Assert.Equal(spec.Execute(5120), emit.Execute(5120));
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

    private static readonly byte[] MemoryShapes =
    [
        0x8B, 0x03, // mov eax,[ebx]           (load32)
        0x89, 0x43, 0x04, // mov [ebx+4],eax  (store32)
        0x8A, 0x0B, // mov cl,[ebx]            (load8)
        0x88, 0x4B, 0x05, // mov [ebx+5],cl   (store8)
        0x3B, 0x43, 0x08, // cmp eax,[ebx+8]  (cmp reg,mem32)
        0x39, 0x4B, 0x08, // cmp [ebx+8],ecx  (cmp mem,reg32)
        0x83, 0x7B, 0x0C, 0x55, // cmp dword [ebx+12],0x55 (cmp mem,imm32)
        0xF7, 0x43, 0x10, 0x0F, 0x00, 0x00, 0x00, // test dword [ebx+16],0xF (test mem,imm32)
        0x64, 0x8B, 0x0B, // mov ecx, fs:[ebx]    (segment override load)
        0x65, 0x89, 0x4B, 0x14, // mov gs:[ebx+0x14],ecx (segment override store)
        0xEB, 0xDC, // jmp head
    ];

    [Fact]
    public void InlineBodies_MemoryShapes_MatchSpecAndEmit()
    {
        // E3-1: every covered memory shape (loads/stores at 8 and 32, both
        // Cmp directions, Cmp/Test against immediates, FS/GS overrides)
        // served through the inline twin must match the spec handlers and
        // the plain emit path exactly, and the inline path must actually be
        // exercised.
        XboxCpu spec = Create(EmitMode.Spec, MemoryShapes);
        XboxCpu emit = Create(EmitMode.Emit, MemoryShapes);
        XboxCpu inline = Create(EmitMode.EmitInline, MemoryShapes);
        Preset(spec);
        Preset(emit);
        Preset(inline);
        spec.Context.SetSegment(SegmentRegister.Fs, 0x20, 0x1000);
        spec.Context.SetSegment(SegmentRegister.Gs, 0x28, 0x3000);
        emit.Context.SetSegment(SegmentRegister.Fs, 0x20, 0x1000);
        emit.Context.SetSegment(SegmentRegister.Gs, 0x28, 0x3000);
        inline.Context.SetSegment(SegmentRegister.Fs, 0x20, 0x1000);
        inline.Context.SetSegment(SegmentRegister.Gs, 0x28, 0x3000);

        Assert.Equal(40960, spec.Execute(40960));
        Assert.Equal(40960, emit.Execute(40960));
        Assert.Equal(40960, inline.Execute(40960));
        AssertSameState(spec, inline);
        AssertSameState(emit, inline);
        AssertSameCounters(spec, inline);
        AssertSameCounters(emit, inline);

        DecodedBlockCache stats = inline.SpecStatistics!;
        Assert.True(stats.EmitRan > 0);
        Assert.True(stats.EmitInlineMembers > 0);
        Assert.True(stats.EmitFastMembers > 0);
    }

    [Fact]
    public void InlineBodies_16BitAddressing_MatchesSpec()
    {
        // 67-prefixed load: address = BX+SI (16-bit, wrapped) + DS base.
        // EBX=0x2000 makes BX=0x2000 and ESI=0 makes SI=0, so the load
        // reads 0x2000 through the same shared ComputeAddress path. The
        // load sits at member 1 so a warmed tail serves it.
        byte[] code = [0x90, 0x67, 0x8B, 0x00, 0x90, 0xEB, 0xF9]; // nop; mov eax,[bx+si]; nop; jmp head

        XboxCpu spec = Create(EmitMode.Spec, code);
        XboxCpu inline = Create(EmitMode.EmitInline, code);
        Preset(spec);
        Preset(inline);
        spec.Context.Esi = 0;
        inline.Context.Esi = 0;

        Assert.Equal(20480, spec.Execute(20480));
        Assert.Equal(20480, inline.Execute(20480));
        AssertSameState(spec, inline);
        AssertSameCounters(spec, inline);
        Assert.True(inline.SpecStatistics!.EmitInlineMembers > 0);

        // 16-bit addressing stays on the twin call (no baked fast path).
        Assert.Equal(0UL, inline.SpecStatistics!.EmitFastMembers);
    }

    [Fact]
    public void InlineBodies_WatchedStore_MatchesSpec_WatchHitsExact()
    {
        // A watched physical range steers RawWrite onto the legacy bus path
        // (CheckWatch + WatchHit). The inline body must take the same path
        // with identical hit counts and values.
        byte[] code = [0xB8, 0x01, 0x00, 0x00, 0x00, 0x90, 0x89, 0x19, 0x90, 0x90, 0xEB, 0xF4];

        XboxCpu spec = Create(EmitMode.Spec, code);
        XboxCpu inline = Create(EmitMode.EmitInline, code);
        spec.Context.Ecx = 0x2000;
        inline.Context.Ecx = 0x2000;
        spec.Bus().WatchStart = 0x2000;
        spec.Bus().WatchEnd = 0x2010;
        inline.Bus().WatchStart = 0x2000;
        inline.Bus().WatchEnd = 0x2010;
        int specHits = 0;
        int inlineHits = 0;
        uint lastSpecValue = 0;
        uint lastInlineValue = 0;
        spec.Bus().WatchHit += (_, _, value) => { specHits++; lastSpecValue = value; };
        inline.Bus().WatchHit += (_, _, value) => { inlineHits++; lastInlineValue = value; };

        Assert.Equal(spec.Execute(6400), inline.Execute(6400));
        AssertSameState(spec, inline);
        AssertSameCounters(spec, inline);
        Assert.True(specHits > 0);
        Assert.Equal(specHits, inlineHits);
        Assert.Equal(lastSpecValue, lastInlineValue);
        Assert.True(inline.SpecStatistics!.EmitRan > 0);

        // The store is baked onto the fast path; the watch check routes it
        // to the twin at runtime (same hits, same values).
        Assert.True(inline.SpecStatistics!.EmitFastMembers > 0);
    }

    [Fact]
    public void InlineBodies_LoadFaultMidTail_MatchesSpec_PreciseCr2()
    {
        // Same shape as the emit fault test but with a load that has an
        // inline twin: phase A warms the block against a mapped page; phase
        // B steers ECX at an unmapped VPN so the warmed tail faults mid-tail
        // through the inline body's translate call.
        byte[] code = [0xB8, 0x01, 0x00, 0x00, 0x00, 0x90, 0x8B, 0x01, 0x90, 0x90, 0xEB, 0xF4];

        XboxCpu spec = CreatePaged(EmitMode.Spec, code);
        XboxCpu inline = CreatePaged(EmitMode.EmitInline, code);
        spec.Context.Ecx = 0x2000;
        inline.Context.Ecx = 0x2000;

        Assert.Equal(spec.Execute(6400), inline.Execute(6400));
        AssertSameState(spec, inline);

        spec.Context.Ecx = 0x9000;
        inline.Context.Ecx = 0x9000;
        spec.Context.Eip = 0x1000;
        inline.Context.Eip = 0x1000;

        Assert.Equal(spec.Execute(16), inline.Execute(16));
        AssertSameState(spec, inline);
        AssertSameCounters(spec, inline);
        Assert.Equal(14, inline.LastFaultVector);
        Assert.Equal(0x1006u, inline.LastFaultEip);
        Assert.Equal(0x9000u, inline.Context.Cr2);
        Assert.Equal(1u, inline.Context.Eax);
        Assert.True(inline.SpecStatistics!.EmitRan > 0);
    }

    [Fact]
    public void InlineBodies_StoreCrossPage_MatchesSpec_PreciseCr2()
    {
        // mov [ecx],ebx sits at member 2 so a warmed tail serves it. Phase A
        // stores inside VPN2 (mapped); phase B steers ECX to the last two
        // bytes of VPN2 so the store crosses into unmapped VPN3 and faults
        // on the third byte through the inline body's per-byte translate
        // loop, with the store's start EIP.
        byte[] code = [0xB8, 0x01, 0x00, 0x00, 0x00, 0x90, 0x89, 0x19, 0x90, 0x90, 0xEB, 0xF4];

        XboxCpu spec = CreatePaged(EmitMode.Spec, code);
        XboxCpu inline = CreatePaged(EmitMode.EmitInline, code);
        spec.Context.Ecx = 0x2000;
        inline.Context.Ecx = 0x2000;

        Assert.Equal(spec.Execute(6400), inline.Execute(6400));
        AssertSameState(spec, inline);

        spec.Context.Ecx = 0x2FFE;
        inline.Context.Ecx = 0x2FFE;
        spec.Context.Eip = 0x1000;
        inline.Context.Eip = 0x1000;

        Assert.Equal(spec.Execute(16), inline.Execute(16));
        AssertSameState(spec, inline);
        AssertSameCounters(spec, inline);
        Assert.Equal(14, inline.LastFaultVector);
        Assert.Equal(0x1006u, inline.LastFaultEip);
        Assert.Equal(0x3000u, inline.Context.Cr2);
        Assert.True(inline.SpecStatistics!.EmitRan > 0);
    }

    [Fact]
    public void InlineBodies_NonRamPhysical_MatchesSpec()
    {
        // A page-table entry can map a linear page outside RAM: the fast
        // path's RAM-bounds check must fall back to the twin, which takes
        // the same bus read/write path (counters included).
        byte[] code = [0x90, 0x8B, 0x03, 0x89, 0x43, 0x04, 0xEB, 0xF8]; // nop; mov eax,[ebx]; mov [ebx+4],eax; jmp head

        XboxCpu spec = CreatePaged(EmitMode.Spec, code);
        XboxCpu inline = CreatePaged(EmitMode.EmitInline, code);

        // VPN3 -> phys 0x10000000 (far above the 64 KB test RAM), present+writable.
        spec.Bus().Write32(PageTable + (3 * 4), 0x10000000u | 3u);
        inline.Bus().Write32(PageTable + (3 * 4), 0x10000000u | 3u);
        spec.Context.Ebx = 0x3000;
        inline.Context.Ebx = 0x3000;

        // The harness bus throws on unmapped access by default; sentinel
        // mode lets both modes take the identical bus fallback path.
        spec.Bus().UnmappedPolicy = UnmappedAccessPolicy.Sentinel;
        inline.Bus().UnmappedPolicy = UnmappedAccessPolicy.Sentinel;

        Assert.Equal(spec.Execute(6400), inline.Execute(6400));
        AssertSameState(spec, inline);
        AssertSameCounters(spec, inline);
        Assert.True(inline.SpecStatistics!.EmitFastMembers > 0);
    }

    [Fact]
    public void InlineBodies_DiagCounters_CountHitsAndTagMisses()
    {
        // Paged harness: the data-translate cache is only populated with
        // paging on, so only paged runs can complete the fast path (unpaged
        // data accesses never consult the cache and always fall back to the
        // twin, which is exact). The loop carries one member of every fast
        // kind: Mov load/store 32 and 8, Cmp reg/mem + mem/reg + mem/imm,
        // Test mem/imm.
        byte[] code =
        [
            0x90, // nop (head)
            0x8B, 0x03, // mov eax,[ebx]
            0x89, 0x43, 0x04, // mov [ebx+4],eax
            0x8A, 0x0B, // mov cl,[ebx]
            0x88, 0x4B, 0x05, // mov [ebx+5],cl
            0x3B, 0x43, 0x08, // cmp eax,[ebx+8]
            0x39, 0x4B, 0x08, // cmp [ebx+8],ecx
            0x83, 0x7B, 0x0C, 0x55, // cmp dword [ebx+12],0x55
            0xF7, 0x43, 0x10, 0x0F, 0x00, 0x00, 0x00, // test dword [ebx+16],0xF
            0xEB, 0xE2, // jmp head
        ];

        XboxCpu off = CreatePaged(EmitMode.EmitInline, code);
        off.Context.Ebx = 0x2000;
        Assert.Equal(40960, off.Execute(40960));
        Assert.Equal(0L, off.EmitInlineFastHits);
        Assert.Equal(0L, off.EmitInlineTagMisses);

        XboxCpu diag = CreatePaged(EmitMode.EmitInline, code);
        diag.Context.Ebx = 0x2000;
        diag.EmitInlineCountersEnabled = true;
        Assert.Equal(40960, diag.Execute(40960));

        Assert.True(diag.EmitInlineFastHits > 0);
        Assert.Equal(0L, diag.EmitInlineTwinCalls); // warm-up populated every tag before tails ran
        Assert.Equal(0L, diag.EmitInlineTagMisses);

        // Phase B: steer EBX to another mapped VPN (7, identity-mapped RAM
        // away from the page tables) so the warmed tail takes a tag-miss
        // fallback once, then hits again.
        long hitsBefore = diag.EmitInlineFastHits;
        diag.Context.Ebx = 0x7000;
        diag.Context.Eip = 0x1000;
        Assert.Equal(40960, diag.Execute(40960));
        Assert.True(diag.EmitInlineTwinCalls > 0);
        Assert.True(diag.EmitInlineTagMisses > 0);
        Assert.True(diag.EmitInlineTagMisses <= diag.EmitInlineTwinCalls);
        Assert.True(diag.EmitInlineFastHits > hitsBefore);
        Assert.True(diag.EmitInlineFastHits + diag.EmitInlineTwinCalls <= (long)diag.SpecStatistics!.EmitRan);
    }

    [Fact]
    public void InlineBodies_RandomPrograms_MatchAcrossAllModes()
    {
        // Randomized differential net for the serve layer and the E3-1 fast
        // paths: seeded programs mixing every covered memory shape (random
        // registers, displacements, prefixes, immediates) with inc/dec/add/
        // jcc(rel 0)/cli/sti/nop filler must evolve identically under plain,
        // spec, emit and emit-inline (paged, so the fast path actually
        // completes; unpaged, so the twins carry the same shapes).
        for (int program = 0; program < 40; program++)
        {
            var rng = new Random(0xE31A + program);
            byte[] code = BuildRandomProgram(rng);

            foreach (bool paged in new[] { false, true })
            {
                XboxCpu plain = paged ? CreatePaged(EmitMode.Off, code) : Create(EmitMode.Off, code);
                XboxCpu spec = paged ? CreatePaged(EmitMode.Spec, code) : Create(EmitMode.Spec, code);
                XboxCpu emit = paged ? CreatePaged(EmitMode.Emit, code) : Create(EmitMode.Emit, code);
                XboxCpu inline = paged ? CreatePaged(EmitMode.EmitInline, code) : Create(EmitMode.EmitInline, code);

                foreach (XboxCpu cpu in new[] { plain, spec, emit, inline })
                {
                    SeedRandomState(cpu, new Random((program * 7) + 1));
                }

                int steps = 65536;
                Assert.Equal(steps, plain.Execute(steps));
                Assert.Equal(steps, spec.Execute(steps));
                Assert.Equal(steps, emit.Execute(steps));
                Assert.Equal(steps, inline.Execute(steps));
                AssertSameState(plain, spec);
                AssertSameState(spec, emit);
                AssertSameState(emit, inline);
                AssertSameCounters(spec, emit);
                AssertSameCounters(emit, inline);
            }
        }
    }

    /// <summary>
    /// Builds one seeded program from the covered memory-shape templates
    /// with random registers (ebx/ebp/esi/edi bases), disp8, optional FS/GS
    /// prefix and immediates, looping via a jmp. Data bases sit in the
    /// identity-mapped VPN2 window (0x2000..0x202C), away from the paged
    /// harness's page tables.
    /// </summary>
    private static byte[] BuildRandomProgram(Random rng)
    {
        var code = new List<byte>();
        for (int i = 0; i < 12; i++)
        {
            int baseReg = new[] { 3, 5, 6, 7 }[rng.Next(4)];
            int reg = rng.Next(8);

            // Load destinations must not be an addressing register (or ESP):
            // a random value loaded into EBX/EBP/ESI/EDI would turn the next
            // member's base into a wild address. 8-bit destinations are
            // limited to AL/CL/DL for the same reason (BL is EBX's low byte).
            int loadDest = new[] { 0, 1, 2 }[rng.Next(3)];
            int disp = new[] { 0, 4, 8, 0x10 }[rng.Next(4)];
            byte modrm = (byte)(0x40 | (reg << 3) | baseReg);
            switch (rng.Next(14))
            {
                case 0:
                    EmitPrefix(code, rng);
                    code.Add(0x8B);
                    code.Add((byte)(0x40 | (loadDest << 3) | baseReg));
                    code.Add((byte)disp);
                    break;
                case 1:
                    EmitPrefix(code, rng);
                    code.Add(0x89);
                    code.Add(modrm);
                    code.Add((byte)disp);
                    break;
                case 2:
                    EmitPrefix(code, rng);
                    code.Add(0x8A);
                    code.Add((byte)(0x40 | (loadDest << 3) | baseReg));
                    code.Add((byte)disp);
                    break;
                case 3:
                    EmitPrefix(code, rng);
                    code.Add(0x88);
                    code.Add(modrm);
                    code.Add((byte)disp);
                    break;
                case 4:
                    EmitPrefix(code, rng);
                    code.Add(0x3B);
                    code.Add(modrm);
                    code.Add((byte)disp);
                    break;
                case 5:
                    EmitPrefix(code, rng);
                    code.Add(0x39);
                    code.Add(modrm);
                    code.Add((byte)disp);
                    break;
                case 6:
                    EmitPrefix(code, rng);
                    code.Add(0x83);
                    code.Add((byte)(0x78 | baseReg));
                    code.Add((byte)disp);
                    code.Add((byte)rng.Next(256));
                    break;
                case 7:
                    EmitPrefix(code, rng);
                    code.Add(0xF7);
                    code.Add((byte)(0x40 | baseReg));
                    code.Add((byte)disp);
                    for (int b = 0; b < 4; b++)
                    {
                        code.Add((byte)rng.Next(256));
                    }

                    break;
                case 8:
                    code.Add((byte)(0x40 + loadDest)); // inc r32
                    break;
                case 9:
                    code.Add((byte)(0x48 + loadDest)); // dec r32
                    break;
                case 10:
                    code.Add(0x83); // add r32, imm8
                    code.Add((byte)(0xC0 | loadDest));
                    code.Add((byte)rng.Next(256));
                    break;
                case 11:
                    // jcc rel8=0: evaluates a random condition without
                    // changing control flow.
                    code.Add((byte)(0x70 | rng.Next(16)));
                    code.Add(0x00);
                    break;
                case 12:
                    code.Add(0x90); // nop
                    break;
                default:
                    code.Add(rng.Next(2) == 0 ? (byte)0xFA : (byte)0xFB); // cli/sti
                    break;
            }
        }

        // Loop back to the head (rel8 covers the 12-instruction body).
        int rel = -(code.Count + 2);
        code.Add(0xEB);
        code.Add(unchecked((byte)rel));
        return [.. code];
    }

    private static void EmitPrefix(List<byte> code, Random rng)
    {
        switch (rng.Next(6))
        {
            case 0:
                code.Add(0x64); // fs:
                break;
            case 1:
                code.Add(0x65); // gs:
                break;
        }
    }

    private static void SeedRandomState(XboxCpu cpu, Random rng)
    {
        cpu.Context.Eax = (uint)rng.Next();
        cpu.Context.Ecx = (uint)rng.Next();
        cpu.Context.Edx = (uint)rng.Next();
        cpu.Context.Eflags = FlagBits.Reserved1 | (uint)rng.Next();
        cpu.Context.Ebx = 0x2000;
        cpu.Context.Ebp = 0x2004;
        cpu.Context.Esi = 0x2008;
        cpu.Context.Edi = 0x200C;
        for (uint address = 0x2000; address <= 0x202C; address += 4)
        {
            cpu.Ram().Write32(address, (uint)rng.Next());
        }
    }

    [Fact]
    public void InlineBodies_GatesSkipInlineEmit()
    {
        XboxCpu verified = Create(EmitMode.EmitInline, Chain);
        Preset(verified);
        verified.BlockVerify = true;
        XboxCpu spec = Create(EmitMode.Spec, Chain);
        Preset(spec);

        Assert.Equal(spec.Execute(32), verified.Execute(32));
        AssertSameState(spec, verified);
        Assert.Equal(0u, verified.SpecStatistics!.EmitRan);

        XboxCpu observed = Create(EmitMode.EmitInline, Chain);
        Preset(observed);
        observed.HotspotObserver = new HotspotProfiler();
        XboxCpu specObserved = Create(EmitMode.Spec, Chain);
        Preset(specObserved);
        specObserved.HotspotObserver = new HotspotProfiler();

        Assert.Equal(specObserved.Execute(32), observed.Execute(32));
        AssertSameState(specObserved, observed);
        Assert.Equal(0u, observed.SpecStatistics!.EmitRan);
    }

    [Fact]
    public void InlineResolver_TwinsFollowHandlerCover()
    {
        // The twin resolver must return a body exactly when the shape has a
        // handler and that handler has a twin; uncovered shapes and
        // covered-but-twinless shapes return null.
        Span<byte> bytes = stackalloc byte[15];
        bytes.Clear();
        bytes[0] = 0x8B;
        bytes[1] = 0x03; // mov eax,[ebx]
        DecodedInstruction load = Decoder.Decode(bytes);
        Assert.NotNull(XboxCpu.ResolveInlineHandler(load));

        bytes.Clear();
        bytes[0] = 0x01;
        bytes[1] = 0xD8; // add eax,ebx (handler yes, twin no)
        DecodedInstruction alu = Decoder.Decode(bytes);
        Assert.NotNull(XboxCpu.ResolveSpec(alu));
        Assert.Null(XboxCpu.ResolveInlineHandler(alu));

        bytes.Clear();
        bytes[0] = 0xF3;
        bytes[1] = 0xAB; // rep stosd (no handler, no twin)
        DecodedInstruction rep = Decoder.Decode(bytes);
        Assert.Null(XboxCpu.ResolveSpec(rep));
        Assert.Null(XboxCpu.ResolveInlineHandler(rep));
    }

    private enum EmitMode
    {
        Off,
        Spec,
        Emit,
        EmitInline,
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

        if (mode == EmitMode.EmitInline)
        {
            cpu.EmitInlineEnabled = true;
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
    private sealed class CountedInterruptSource(int vector, int fireAt) : IInterruptSource
    {
        private int _acks;

        public int Acknowledge()
        {
            _acks++;
            return _acks == fireAt ? vector : -1;
        }
    }
}
