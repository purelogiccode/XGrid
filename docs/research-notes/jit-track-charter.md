# Fast-mode execution engine (JIT track) - charter (2026-10-18, operator-approved)

> **STATUS 2026-10-22 LATE (thirty-second session, 232) - C4 slice 1 measured
> NULL and reverted; the measured levers are now the boot ROM decode and the
> non-JIT share.** The host-pointer translate probe was implemented (per-slot
> RAM host base, exact non-RAM fallback) and priced on interleaved 1B A/B:
> menu wall 21.01 -> 21.05 s (`runnerMs` 15,872 -> 16,026), triangle 6.89 ->
> 6.77 s (`runnerMs` 3,407 -> 3,382) - noise, SHAs exact. The probe is NOT the
> raw path's wall. The triangle's 6.9 s is now decomposed (232b):
> `RunJitBlock` 4.53 s (native 3.42 s + 1.1 s head lookup/formation/chain) +
> `RunNonJit` 2.25 s, and the non-JIT cost is **decode**, not the mnemonics -
> `nonRamSkips=359,476` of 497,806 misses, top pages `0xFFFFFFxx` (boot ROM),
> all inside the first 100M steps, priced at **~0.54 s/run** (7.8% of the
> triangle wall). Landed instead: the unified interrupt poll (232c) and the
> store probe's lost translate slot (232d). **C4 as specified is closed**; the
> next Track C candidates are the read-only-ROM decode path and C5 trace
> formation measured against `nonJitMs` (see `next-session.md` §2). Method
> note (232e): price stages with stashed interleaved A/B legs, never with
> env-gated ablations that change guest behavior.

> **STATUS 2026-10-22 (thirty-first session, course correction) - Track C is
> RE-OPENED by operator decision: the long-run objective is xemu-class
> performance or better.** The freeze's own re-open condition is met: the CPU
> engine is ~17 s of the 24.3 s menu `gl+replace` wall (~70%) and the dominant
> part of the 7.5-7.9 s triangle wall. Measured gap: triangle ~6.6x to xemu's
> 1.20 s at 1B (830 Msteps/s); menu ~17.8x to the 1.36 s real-time band (xemu
> has no menu oracle); ktest 2B is already ahead (9-10 s vs 15 s). The new
> program is **C4** flat memory model/soft TLB, **C5** trace/superblock
> formation, **C6** device/MMIO fast path (Phase 3b below). C3-x/y/z is closed;
> incremental sub-2% slices remain banned. Each stage: same-window A/B >=10%
> wall or >=20% engine, exact 1B SHAs/counters, flag-off byte-identical, ktest
> log byte-identical. Kill/reassess if C4+C5 miss 3x engine after two
> structural iterations. Canonical order: `docs/development-direction.md` §0.

> **STATUS 2026-10-21 (twenty-second session, 220) - Track C stays frozen; the
> session was CPU *correctness*, not throughput.** The independent x86
> differential (`tools/cpu-oracle/`) widened from 333 to 629 cases and found nine
> interpreter defects (unsigned `PMINSW`/`PMAXSW`, `PSRAW`/`PSRAD` count
> saturation, missing `XLAT`, a 16-bit `[SI]`/`[DI]` disp16 decoder bug, x87 NaN
> sign/payload loss, quiet-NaN compare exceptions, `FSQRT`-negative C2, SSE
> min/max operand selection). Every fix is shared code, so fast mode inherits it;
> the raw emitter's FPU-compare descriptor gained the quiet bit (bit 1/2) to stay
> aligned with the interpreter. **No measured change on either title** - both 1B
> accurate goldens are byte-identical and the fast anchors are exact (triangle
> `B45254CA`/`7DAB63EB` 10.9 s, menu `34973510`/`F5D61C94` 32.0 s), so the engine
> and rasterizer budget in 211 is unchanged (~17 s CPU engine on the
> `gl+replace` menu wall, `sw` menu 32.0 s with `rasterPsh` 11.0 s). The freeze
> covers codegen/throughput only; re-opening Track C remains an operator decision
> (C3-z, 2-3%).

> **STATUS 2026-10-21 (eighteenth session, §211) - Track C stays frozen; the
> session was a Track E diagnosis with no engine or rasterizer change.** The
> target was compatibility gap 10 (`xgu bumpenv_ripple` renders black, §209g).
> It is now diagnosed as a *state* answer, not a renderer bug: every presented
> surface is the colour clear, the two per-frame quads resolve their colour from
> `T1` (`V0 = T1`, final `D = V0`; stage 1 is `NONE` in one draw, the bump draw
> samples the logo's black edge), and the zeta clear (`0x00711174`, `LEQUAL`) is
> nearer than every fragment, so nothing passes (§211a-211e). The only landed
> change is the convolution tap step on linear (LU_IMAGE) stages inside the
> opt-in `--tex-filter` path (§211b), which no gate title reaches. Engine and
> rasterizer numbers are unchanged from §210 (menu `gl+replace` ~17 s CPU engine,
> menu `sw` 31.2 s with `rasterPsh` 10.84 s). Re-opening Track C remains an
> operator decision (C3-z, 2-3%).

> **STATUS 2026-10-21 (seventeenth session, §210) - Track C stays frozen; the
> session was Track B/D + fidelity, no engine change.** The lead item was
> `next-session.md` Option A (teach the compiled fills the bilinear tap), and
> pricing it found that the "2.4x cost of TEXFILTER" was a misapplied
> convolution (`psh.c:204`), not the tent. Both landed: the flag is now 31.90 s
> vs the 32.22 s legacy menu wall at 1B and no gate anchor moves (§210). The CPU
> engine is unchanged and still ~17 s of the `gl+replace` menu wall; the sw
> product path is 32.2 s, of which the rasterizer is 11.0 s. Re-opening Track C
> remains an operator decision (C3-z, 2-3%); nothing in this session changes its
> calculus.

> **STATUS 2026-10-20 (twelfth session, §205) - Track B's two scoped slices are
> in; Track C stays frozen.** Draw-command batching landed on top of §204
> (`drawMs` 1971 -> 1045 ms, -47% 3/3; wall -4.2% 3/3; both 1B frames
> byte-identical), so the GL path now costs ~1.05 s of a ~23.8 s projected menu
> wall. Track B has no scoped slice left that is worth a session (the remainder
> is ~0.8 s of block packing and the ~0.5-0.9 s texture fingerprint, both
> documented as rejected/deferred). Next per `docs/development-direction.md` §0:
> Track E (corpus) and Track P (product). The engine is still >50% of the
> measured walls, but the redirect criterion is "after C3-x/y/z" and C3-x
> landed; re-opening Track C remains an operator decision (C3-z, 2-3%).

> **STATUS 2026-10-20 (course correction, §203) - Track C is FROZEN.** The CPU
> engine is no longer the lead: the product frontend (`ea8b38f`) made the
> software rasterizer the product renderer, so Track B leads with the
> `gl+replace` present path (remove the per-flip readback; ~9.9 s / 686 menu
> frames), then Track E (corpus) and Track P (product: audio/pad/save states).
> No new codegen slices; only the operator's `--jit`/`--jit-raw` default-flip
> decision remains. Re-open only if Track B lands and the engine is again >50%
> of a measured wall. Canonical order: `docs/development-direction.md` §0.

> **STATUS 2026-10-20 (ninth session - Track B compiled-loop fast lanes, 202).**
> The rasterizer is no longer just "compiled loops": both fills now carry per-fill
> exploit lanes priced by ablation. `IGpuMemoryFast.TryGetRamBytes` plus
> `SamplerAddressBound` hoist the texel read's interface call behind one per-fill
> bounds proof (direct `ReadUInt32LittleEndian` over the RAM span, adapter fallback
> unchanged), `SamplerPlan.DecodeKind` folds the per-sample decode switch into the
> fill's exact branch, the blend destination read uses the same direct word, and the
> lighting loop drops the clamps its own algebra proves are no-ops (R1 stays in
> [0,1]; the combine multipliers are gated by a per-fill `|c0c|,|c1c| <= 1` predicate
> that all 53,574 fills pass) plus per-row hoists. **1B fast-mode A/B vs `65e136e`,
> 3 interleaved pairs, SHAs and every counter identical: triangle `rasterPsh`
> 3.39 -> 2.87 s (-14.2%) and wall 9.15 -> 8.69 s (-4.0%); menu `rasterPsh`
> 13.47 -> 12.49 s (-5.3%) and wall 35.45 -> 34.64 s (-2.3%).** Accurate mode is
> byte-identical to the 200 anchors. Two measured negatives are recorded and must
> not be re-tried: the k2 constant fold (2-3% slower - the hoisted constants do not
> stay in registers) and the alpha-1 blend skip (neutral; the destination read is
> only 2.4 ns/px). CPU-side levers were priced and dropped: the non-JIT time is
> mostly the software raster inside an MMIO member (gl+replace: 5.35 -> 2.5 s), and
> block builds cost ~200 us raw + 20 us IL, with the threshold sweep showing 16 is
> already optimal. Track B still owns the end-to-end gap; the sampler's remaining
> ~4.5 s is load latency and the dependent chain, so the next lever there is
> structural (2-pixel interleave), not more hoisting.

> **STATUS 2026-10-20 (seventh session - C3-x hot-shape native encoding
> LANDED, §199o).** The §199n re-weight's first item is done: the raw emitter
> now encodes the shapes the new `--raw-shapes` tally showed, so helper-routed
> members at 1B fell **24.65M -> 1.81M (triangle)** and **122.65M -> 5.57M
> (menu)** with every SHA/counter exact against the IL anchor (triangle
> `F57934BF.../CF79FC17...`, menu `660CA11B.../F5D61C94...`). Coverage: stack/
> call/ret/leave, memory movzx/movsx, neg/not/bswap/xchg/cdq/setcc, shifts and
> rotates (including by CL with the ShiftOps OF rule), x87 load/store/
> arithmetic/compare/constants, MMX movq/movntq, scalar SSE (arith/movss/cvt),
> and prefetch-as-nop. Two bugs were fixed: register-form FPU members advanced
> EIP twice (block exits landed mid-instruction), and the IL runner advanced
> EIP for the new handler-backed kinds (`JitPlanner.HandlerOwnsEip` now owns
> that decision). Validation: suite 1510 green, ktest smoke PASS 7/7, format
> clean. **Quiet-host 1B A/B vs the pre-C3-x commit (3 interleaved pairs,
> old/new SHAs identical):** `--gpu sw` triangle 14.74 -> 13.14 s (-10.8%),
> menu 52.20 -> 48.43 s (-7.2%); `--gpu gl --gpu-replace` triangle
> 7.56 -> 6.49 s (-14.1%) at **11.9 -> 8.8 ns/served member**, menu
> 34.29 -> 31.56 s (-8.0%) at **36.7 -> 31.6 ns** - the §199n C3-x target
> (menu 61 -> ~30 ns) is met. **C3-y was implemented bit-exact, measured
> NEUTRAL and reverted** (5 interleaved triangle pairs: wall 7.048 -> 7.015 s,
> `outerMs` 4326 -> 4314); a 1024-slot data translate cache was likewise
> neutral and reverted. The raw-kind profile (`XGRID_RAW_PROFILE=1`) shows the
> 1B menu is ~50% load/store + 14% loop dec/inc, so its premium is the memory
> probe's dependent-load chain, not helpers. The flip-pacing probe is closed
> as no-bug (one vblank + render per frame, reference-matching stall
> semantics). Remaining CPU work is structural (memory probe latency, device
> mix); **Track B/D owns the end-to-end gap** per the redirect criterion. The
> remaining helper-shape tail (`mul/imul`, string `movs`, high-byte `mov`,
> `fnstsw`, carry-rotates) is small and low-value.

> **STATUS 2026-10-20 (sixth session - C3 chaining landed behind the §199l/§199m
> fault contract; measured position re-aimed).** C3 is complete for the two
> goldens: raw emitter + direct block chaining, both 1B bit-exact against the
> IL control (menu `660CA11B…/F5D61C94…`, triangle IL == raw on this host),
> 1B medians of 3 interleaved pairs -12.0% (triangle) / -13.1% (menu) wall,
> -28.6% / -14.2% `outerMs`. **The breakdown now says the engine is no longer
> the binding constraint on either golden** (§199n; same host/window, 1B,
> `--jit-raw --jit-threshold 16 --jit-spin`; gl legs `--gpu gl --gpu-replace`):
>
> | 1B, raw+chain | menu | triangle |
> |---|---:|---:|
> | wall `--gpu sw` | 112.5 s | 22.2 s |
> | wall `--gpu gl --gpu-replace` | 58.9 s | 13.5 s |
> | software-rasterizer cost (sw-gl) | 53.6 s (48%) | 8.7 s (39%) |
> | raw members served | 828.8M | 452.5M |
> | raw runner time (gl leg) | 50.5 s | 8.35 s |
> | ns per served member (gl leg) | 61 ns | 18.5 ns |
> | chain jumps / invocations | 157.9M / 4.63M | 109.0M / 1.62M |
> | idle-skipped steps | 128.6M | 530.2M |
>
> Block mechanics are cheap now (1.6-4.6M invocations and 109-158M chain jumps
> over 450-830M members), so further block/trace work is **not** the lever.
> The remaining CPU-side cost is concentrated in the **helper-routed shapes**
> (a managed `ExecuteSpec` call per unencoded member - the dashboard's MMX
> copy/string/x87 loops; rank them first with a raw-leg shape tally) and in
> **per-member bookkeeping** (EIP store, TSC
> tick, store-guard tests). Against xemu's measured triangle ~830 Msteps/s we
> are at 74 Msteps/s with a hardware backend (~11x) and 18.5 ns/member
> (~2-4x off TCG-class on simple code; the dashboard's 61 ns is ~6-12x but
> includes its NV2A/device traffic).
> **Re-weighted order (supersedes the Phase 2.5/3 sequence below):**
> (1) C3-x hot-shape native encoding (MMX `movq`/`movntq`, non-faulting
> `movs`/`stos`, dashboard x87) - target menu average 61 -> ~30 ns/member;
> (2) C3-y per-member bookkeeping trim (EIP per block exit, hoisted store
> guard) - ~10-20% of the CPU path; (3) C3-z inline TLB/RAM page pair - the
> last softmmu-style item; then **Track B leads**: the software rasterizer is
> 48% (menu) / 39% (triangle) of the sw wall, and present pacing gates
> playability (686 flips/1B = 11.7 fps with GL today). **Metric change:**
> report ns per served member, the `gl+replace` 1B wall and wall per rendered
> frame - not 1B-step walls, which mix in 530M idle-spin steps on the
> triangle. **Redirect criterion:** if after C3-x/y/z the dashboard is still
> >15x from real time with GL, the remainder is Track B/D (rasterizer
> structure, device paths, present pacing) and further codegen work stops.
> One fidelity probe queued: we emit 35 triangle flips per 1B steps where
> xemu's rate implies ~84 - the guest may be waiting on vblank/flip semantics
> we delay (§199n).

> **STATUS 2026-10-20 (fifth session - C3 third correctness campaign, §199i):
> BOTH TITLES 1B BIT-EXACT; THE OPEN MENU GAP IS CLOSED.** The §199h menu
> divergence was not a store helper: the four split fast/slow members (RMW
> destination, cmp-memory, test-memory, inc/dec-memory) skipped the **fast
> path's EIP advance** because the slow path's `EmitHelperMember` sets the
> emitter's compile-time `_memberHelper` flag for the whole member. Mid-block
> the next member's `EmitSetEip` hides it; when such a member is the block's
> LAST member the block exits on the member's first byte, the member path
> re-executes the instruction, and the guest step/TSC accounting drifts by
> one (the recorded MMX-loop byte was the symptom). A new block-level trace
> (`XGRID_BLOCK_TRACE`, plus retargetable `XGRID_WATCH_START/END` +
> `XGRID_WATCH_TRACE`) localized the first differing invocation to head
> 0x1E010: 32 members, exit 0x1E08B (IL) vs 0x1E088 (raw), last member
> `or [eax+0x2C],edx`. Both title 1B differentials now match exactly (menu
> `660CA11B…/F5D61C94…` with every JIT counter identical, triangle
> `A2DCA55F…/7DAB63EB…`); regression test
> `BlockEndingInMemoryRmw_AdvancesEipOnce` fails pre-fix (TSC 71 vs 70,
> doubled inc). Suite **1497** green; ktest smoke PASS 7/7 at the
> bit-reproducible 792,800,535 steps. The §199k state hoist (`BuildRawState`
> once per `RunJitBlock` call; unmanaged per-CPU state block) then put the raw
> path **ahead of IL on CPU serving time**: 100M A/B 9.0% menu / 2.4%
> triangle, 1B `outerMs` -3.7% menu / -2.3% triangle, differentials still
> bit-exact; end-to-end walls were host-noise bound, so no end-to-end claim
> yet. **Direct chaining was then implemented (§199l) and is now LANDED**
> (sixth session, commit `21b0f8a`) after the fault contract was fixed
> (§199m): a managed exception may not unwind out of a raw helper through the
> hand-emitted frame (no unwind data, so CoreCLR cannot walk it - delivery was
> stack-layout luck, which the `AluLoop`→`PagedCrossVisitFault` pair exposed).
> Helpers now catch, record (`RawState.Fault`) and return; the emitted block
> tests the word after every helper call and exits with a sentinel that
> `RunJitBlock` rethrows in managed code after `RawExit`, so fault delivery and
> `_jitProgress` are exactly the IL runner's. Chaining design as measured:
> per-CPU tail-jump table, chained served/budget accounting, live-generation
> validation and a cheap PIC pending word; STI blocks never chain. **1B medians
> of 3 interleaved pairs: triangle wall 26.97 → 23.72 s (-12.0%, 3/3) /
> `outerMs` -28.6%; menu wall 109.39 → 95.06 s (-13.1%, 3/3) / `outerMs`
> -14.2%**, menu SHAs the §199i anchor exactly (`660CA11B…/F5D61C94…`) with
> every counter identical, triangle IL == raw on this host
> (`F57934BF…/CF79FC17…`; the older `A2DCA55F…` anchor no longer reproduces
> even on pre-patch HEAD). Pair reproducer clean 10/10, CPU suite 625/625 x10,
> full suite 1499 green. The raw path stays opt-in. Quiet-host calibration:
> triangle IL 1B **10.20 s**, menu IL 1B **~40 s** (gl+replace+spin).
>
> **STATUS 2026-10-20 (fourth session — C3 second correctness campaign, §199h):
> TRIANGLE 1B BIT-EXACT; MENU OPEN AT 968.38M.** Two more raw-emitter bugs
> closed, both found by the title differentials: the **store guard's note path
> clobbered R9** (the linear address) so page-table stores took the slow path
> with a garbage address whenever a bus watch was armed (the CLI arms one by
> default — the triangle's 25.05M #PF at CR2=0x0D, and every store went slow
> under an armed watch); the guard now runs the exact `IsWatched` range test
> before the note path. And the **exit-edge flag merge**: the epilogue
> materialized the frame flags with the emitter's compile-time liveness while
> `_exit` is also the target of the poll's pending path / Jcc arms / terminator
> jumps, so the kernel idle delivery pushed EFLAGS 0x202 instead of 0x246 (the
> triangle's 30.92M divergence). **The triangle now matches IL bit-exactly at
> 1B steps** (RAM SHA + CPU SHA + every NV2A counter identical). The menu is
> exact through step 968,380,639 and then differs by one byte in the
> dashboard's MMX copy loop (`movq`/`movntq`) — open, full evidence trail in
> §199h. Three regression tests added (delivered-EFLAGS parity, page-table
> store under a covering watch, exact watch-range hits); suite 1495 green.
> The raw path is now at **parity** with IL after the per-invocation overhead
> cuts (guarded trace interpolations, RawState invariant template) — it serves
> 76% of triangle members; the next levers are structural (chaining /
> register-resident state) and constrained by the per-block IRQ poll contract
> that keeps the IL/raw differential meaningful. Quiet-host calibration:
> triangle IL 1B **10.20 s**, menu IL 1B **~40 s** (gl+replace+spin).
>
> **STATUS 2026-10-20 (third session — C3 correctness campaign, §199g):
> GATE 1 CLOSED, GATE 2 AT 25.05M/1B.** `JitRawBlocks` forced on now runs the
> whole `JitTests` suite green — **12 → 0 failures, 56/56 JitTests +
> RawJitTests, suite 1492 green** — after fourteen latent raw-emitter fixes
> (non-terminator EIP advance, stale EIP for helpers, CLI/STI EAX clobber, the
> missing post-STI/POPF poll, the VPN-vs-slot xlat probe bug, unguarded inline
> stores, the poll's shadow-space overlap, host DF across helper calls — a
> Win64 ABI violation, flag-liveness leaking across merge points, the store
> guard clobbering guest EAX/ECX/EDX, GC-stale CPU/entries pointers, memory
> MovExtend, disp32 ≥ 0x80000000 encoding, WP/A-D parity). The **IL-fast vs
> raw-fast title differential matches bit-exactly through 25,053,067 triangle
> steps**, then diverges inside a raw block at `0x8001F9EB` (a 16-bit store to
> a page-table page; raw takes a #PF the IL run does not) — that is the gate
> before any measurement or default flip. The JitTests harness now forces
> `JitRawBlocks` on Windows so the native runner stays under the differential
> net; other hosts fall back to the IL runner automatically.
>
> **STATUS 2026-10-20 (second 12-hour window): PHASE 3 STARTED — the raw
> emitter executes real blocks.** C2 (DynamicMethod, state sync + native
> Fld/Fst/Fstp m32) and D3b landed first and took the fast-mode 1B goldens to
> **triangle 10.63 s / menu 45.62 s** (gl+replace+spin, SHAs exact; §199d).
> **C3 (`--jit-raw`, opt-in) now emits one native x86-64 function per block**
> — guest GPRs in fixed host registers, arithmetic flags in the host FLAGS
> register, memory members running the data translate-cache probe inline
> against pinned pointers, and a reverse-P/Invoke trampoline to the existing
> spec handlers for every unencoded shape, so every block is emittable
> (§§199e/199f; the bring-up blockers and the TSC gap are recorded there).
> Validation at that point: 11/11 raw differentials green, suite 1492 green;
> the Phase-3 exit criterion was *not* met (a dozen `JitTests` failed with the
> path forced on and no 1B measurement existed).
>
> **STATUS 2026-10-19 (evening, quiet host): PHASE 0 + 1 LANDED; PHASE 3
> ENTRY CONDITION MET — CPU ENGINE IS THE LEAD TRACK AGAIN (§199).**
> J1a–J1d + the coverage campaign + the day's fast-mode work (`--jit-spin`
> idle-wait skip, `--jit-timing` gate, `--jit-mmio`, 256-slot data-translate
> cache, cross-page decode entries) brought the 1B goldens to **triangle
> 20.28 s sw / 10.68 s gl+replace, menu 56.82 s / 41.39 s**. The structure
> inverted again: the **CPU engine is 57% of the triangle wall and ≈45% of the
> menu wall**, and the `DynamicMethod` block JIT has hit its structural
> ceiling — ~18 ns fixed per block (context load/store + dispatch + memo) ×
> 125M invocations plus ~7 ns per member × 543M, with the remaining Phase-2
> leftovers worth only ~1.5–2×. Measured xemu: **triangle ≈830 Msteps/s
> (1.13× real-time)** vs XGrid 94M (triangle) / 24M (menu) — a **~9× / ~30×**
> plateau, i.e. >3× short, which is exactly this charter's Phase-3 entry
> condition. **Decision (§199): C2 trace emitter next (DynamicMethod,
> multi-block traces, ≥2× CPU), then C3 raw emitter (Iced, now unblocked).**
> Fast mode is never gate currency; runs are deterministic per binary+flags.
> Cheap Track-D wins (D3b, bookkeeping) and Track B (B2c draw batching, the
> window present path) run behind the CPU track.

> **STATUS: AUTHORIZED 2026-10-18 by the operator.** Long-run goal: *at least
> xemu-class performance*. This file is the governing document for the track —
> dual-mode contract (§2), correctness methodology (§3), phases + kill criteria
> (§4), validation gates (§5), the approved constraint deltas (§6), and the
> invariants that must not change (§7). `docs/development-direction.md` and
> `AGENTS.md` summarize it; `ImplementationPlan.md` Phase 15 / Track C point
> here. Nothing in this charter weakens the accurate mode or the standing
> gates. `§N` refs = `docs/research-notes/xgrid-architecture-decisions.md`.

---

## 1. Goal, and why the interpreter track cannot reach it

**Goal:** reach the xemu-class execution band (QEMU TCG class) — end-to-end
**10²–10³ Msteps/s**, i.e. the 733 MHz Pentium III executed in the real-time
band — on top of the existing LLE core (real MCPX ROM → kernel → XBE).

**Measured position (2026-10-19 evening, §199; 1B, fast mode, single runs —
the pre-§198a figures in parentheses):**

| Workload | 1B-step wall (sw) | 1B-step wall (gl+replace+spin) | End-to-end |
|---|---:|---:|---:|
| menu (dashboard) | 56.8 s (64.0) | 41.4 s (48.3) | ≈24 Msteps/s |
| triangle | 20.3 s (27.9) | 10.7 s (17.8) | ≈94 Msteps/s |

xemu on the triangle: ≈830 Msteps/s (1.13× real-time, 70 fps) → the triangle
gap is ~9× and the menu ~30× from the real-time band (733 Msteps/s). The menu
has no xemu oracle (xemu deadlocks at the dashboard animation wait, §32). The
structural remainder is the CPU engine — runner 57% of the triangle wall,
≈45% of the menu — see the status header and Phase 2.5/3.

**Why the current track has asymptoted:**

- The decode-cache frontier is captured: default-on 2.56×/3.04× (§137) plus
  fetch-fast 1.30–1.35× (§131) are already in the baseline.
- Shape specialization cut fallbacks 101.8M → 34.1M but moved walls only
  1.07–1.19× (§§149–169; §160, §164, §166, §169) — §138: the 354-case switch
  is *not* the remaining cost; per-instruction execute bodies (76% data-path
  helpers on menu) and the fixed ~105 ns/step instrumented overhead are.
- Relay-style emission is **measured wall-neutral**: E1 −1.93×/−6.50×
  (§171), E2 parity (§§172–174), E3 quiet-host triangle 1.021× vs emit /
  1.137× vs spec, menu parity (§§175–179b; §178 hitRate 0.9161/0.9831), and
  the §20.6 microbenchmark (in `jit-codegen-scope.md`): no managed emitter
  beats a plain C# helper loop (14.188 vs 14.285 ns; construction
  124.6–319.3 µs + 6–30 KB per block).

Only a codegen tier that removes per-instruction helper dispatch and
context load/store can move the wall (≈3–45× per title). That tier is this
track.

**Decision record — licensing (why not "port TCG"):** the project **stays
non-GPL** and does **not** transliterate QEMU/xemu code. QEMU TCG is
entangled with QEMU (softmmu, TB cache, `CPUArchState`, signal-based
invalidation); a C# port is a GPL derivative work *and* ≈ equal effort to an
own emitter. Legitimate references (techniques/design only unless a license
permits more): QEMU TCG (GPL-2.0 — **design reference, never code**),
Ryujinx ARMeilleure (**MIT** — C# runtime machine-code JIT infrastructure),
Iced (**MIT** — encoding, Phase 3, approved not yet referenced), Intel SDM
(x86-64 encodings). Any code reuse gets its license notice + a `sources.md`
entry.

## 2. The dual-mode contract (the load-bearing change)

**Accurate mode (existing interpreter; today's default):** unchanged in every
way — boundary-preserving `Step()`, exact TSC/IRQ/step observability, golden
SHAs/counters, byte-identical flag contracts. Gate currency forever.

**Fast mode (`--jit`, opt-in):** a new execution engine under a *precisely
defined* weaker contract.

- **Preserved (must hold in fast mode):** guest memory semantics and program
  order; fault precision (exact faulting instruction + architectural state at
  the fault); MMIO/device side effects in instruction order (non-RAM access
  exits to the exact helper at the access point); IF semantics (blocks
  bounded/structured at IF changes); deterministic output for the same
  binary + input script (verifiable via a bench state hash).
- **Relaxed (fast mode only):** TSC per-instruction values; step-boundary
  observability and step-count reproducibility; IRQ delivery point (block
  granularity); intermediate-state and frame SHAs are *informational*, never
  gates.
- **`--jit-exact` (debug variant):** the same JIT code with per-instruction
  serialization → bit-exact architectural state vs accurate mode at every
  boundary. Slow by design; used for differential debugging (QEMU singlestep
  precedent).
- **One engine per run** — a run is accurate or fast; no per-region mixing.

## 3. Correctness methodology for fast mode

- **Sync-point differential** (the fast mode's correctness contract): run
  both engines over the same deterministic input and compare architectural
  state at every sync point — MMIO access, guest fault, IRQ delivery, device
  callback. Harness: `--jit-verify`; first divergence prints full
  register/memory context and exits nonzero.
- **Unit differentials per emitted shape**, in the existing test style
  (differential vs interpreter, fault-precision pins, observed-store cases).
- **Title acceptance per milestone (§4):** triangle frame SHA target
  bit-exact where timing-independent (static scene); menu acceptance is
  structural/classified (the animation is timing-driven and fast mode runs
  faster than guest pacing); ktest *outcome* (pass/fail) must match from
  Phase 2 on.
- **Shared-code rule:** any change to code both engines use (decoder, MMU,
  `PhysicalMemory`, devices) still passes the full accurate-mode golden
  ceremony. Fast mode never substitutes for accurate gates.

## 4. Phases and kill criteria

**Phase 0 — bench + verify harness (days; no product change).**
Deterministic bench workloads ≤10 s wall each (hot-loop streams from the
§138 heads; menu/triangle trace replays), reporting Msteps/s + state hash;
`--jit-verify` scaffold (accurate-vs-accurate self-check green first).
Optional calibration: capture xemu throughput on this host if a permitted
oracle run exists; otherwise the 10²–10³ Msteps/s band stands.
*Accept:* reproduces current rates within ±10%, deterministic, adds no
product behavior.

**Phase 1 — block JIT v1 (DynamicMethod; opt-in `--jit`; weeks).**
Differ in kind from E1–E3 (why those were neutral): whole-block straight-line
code; guest GPR/EFLAGS **cached in locals across the block**; **inlined RAM
fast paths** (translate-probe + direct read/write) with MMIO/observe exits
inline-checked; block-local flag materialization; fast-mode block-granular
boundaries.
*Targets:* bench CPU-side **≥2.5–3×** vs current (triangle ≥ ~90–110
Msteps/s CPU-side, menu ≥ ~2×); integration **triangle 1B ≤ 22 s / menu
≤ 50 s** (vs 36.6/70.8).
*Kill:* <1.5× bench CPU-side after 4 focused weeks or 3 structural
iterations → stop polishing, reassess (Phase 3 design or re-scope). Do not
repeat the E-series pattern of landing wall-neutral slices for weeks.

**Phase 2 — lazy flags, block chaining, tiering, SMC invalidation (weeks).**
Reuse the §131/§134 invalidation inventory; tiering thresholds informed by
§§172–174. *Target:* cumulative CPU-side **5–10×** (≥100–370 Msteps/s);
integration triangle 1B ≤ 12 s / menu ≤ 30 s.
*Status 2026-10-19 evening (§199):* partially landed (outer-loop chaining +
32-entry memo + lazy revalidation + fill-on-demand formation); the measured
plateau is ~94 Msteps/s (triangle) / ~24 Msteps/s (menu), and the remaining
items are worth ~1.5–2×. The fixed per-block cost (~18 ns: context
load/store + dispatch + memo) is the structural remainder — hence Phase 2.5.

**Phase 2.5 — trace emitter (C2; the immediate next slice, §199).**
Design: docs/research-notes/jit-trace-emitter-design.md (formation rules,
runner contract, lazy-sync mask table, validation order, kill criteria).
Emit *multi-block traces* in one `DynamicMethod`: follow the planned
successor at build time (unconditional jumps first, then hot conditional
paths), keep guest GPR/EFLAGS/EIP/TSC in locals **across the whole trace**
(no per-block context load/store), inline the RAM fast paths already proven
in `--emit-inline`, keep the interrupt poll at every block boundary (the
§193 wakeup contract), and exit to the existing block JIT on any trace miss
(state stored once per exit). *Target:* CPU-side ≥2× — triangle runner
6.1 s → ≤3 s, menu CPU ≈18 s → ≤11 s; end-to-end triangle ≤6 s / menu ≤20 s
with D3b + bookkeeping. *Kill:* <1.4× CPU-side after 3 structural iterations
→ skip straight to C3.

**Phase 3 — raw emitter (entry condition: MET, §199).**
Iced (MIT; approved, now authorized) or own encoding; guest context pinned in
a host register (or a register-allocated subset); direct block chaining
(patch-on-link); inline softmmu-style TLB probes; helper calls only for
complex ops; reuses the Phase-1/2.5 planner, decode cache and boundary
contract. *Entry:* Phase 1–2 plateau >3× short of the xemu-class target —
**met** (measured ~9× triangle / ~30× menu vs the real-time band; §199).
*Target:* another ≥2× CPU-side — triangle 1B ≤3 s end-to-end, menu ≤12 s
(headless gl+replace+spin), live window ≥60 guest-fps-equivalent on the menu
animation.
*Status 2026-10-20 (§§199e-199n):* **landed and validated** — per-block native
x86-64 with pinned guest GPRs/host FLAGS, inline translate-cache probe,
spec-handler trampoline, then direct block chaining (§199l) behind the raw
fault contract (§199m: helpers catch/record/report, `RunJitBlock` rethrows in
managed code). Both 1B title differentials are bit-exact against IL; the raw
path is ahead of IL on CPU serving time and 12–13% ahead end-to-end. Phase 2.5
(trace emitter) was **absorbed** by C3: chaining already removes the per-block
state reload without a second emitter. The measured remainder (§199n) is the
helper-routed shape mix, per-member bookkeeping and the data-translate probe —
see the re-weighted order in the status block. The Phase-3 *target* is
re-stated as **ns per served member** (triangle 18.5 → ~8–10 ns, menu 61 →
~30 ns on `gl+replace`) rather than 1B walls. **2026-10-22:** the target was
met (8.8 ns triangle / 31.6 ns menu) but is not xemu-class (~1.2 ns/step); the
Phase-3 program is closed and the continuation is **Phase 3b (C4-C6)** below.

**Phase 3b — structural performance, C4-C6 (re-opened 2026-10-22).**
The engine is the wall again (~17 s of the 24.3 s menu `gl+replace` wall,
dominant on the 7.5-7.9 s triangle wall). The remaining cost is the
data-translate probe, per-block entry/exit and the menu's helper/device mix.
Stages:

| Stage | Work | Target |
|---|---|---|
| **C4** | ~~Flat memory model / soft TLB: direct host-pointer RAM pages, exact non-RAM/MMIO fallback; remove the per-access probe~~ **MEASURED NULL 2026-10-22 late (232a), reverted** | ~~≥2× menu engine, ≥1.5× triangle~~ |
| **C4b** | Read-only-ROM decode path: classify read-only physical windows as cacheable code, skip the tracked-page bitset for them, flush on overlay-disable/reset (priced 0.54 s/run, 232b) | ≥0.45 s off the 1B triangle / 100M boot |
| **C5** | Trace/superblock formation: hot successors in one native trace, state across blocks, poll at every boundary (the §193 wakeup contract); re-measure the §199d block-fusion negative first; keep `outerMs - runnerMs` flat | ≥1.3× engine |
| **C6** | Device/MMIO fast path: hot NV2A method dispatch and the menu's device mix (slice 1, the unified interrupt poll, landed 232c as architecture) | ≥10% menu wall |
| **C7** | Reassess if C4b+C5 miss 3× after two structural iterations (including the native-core option) | measured decision |

Each stage: same-window A/B (≥10% wall or ≥20% engine), exact 1B
SHAs/counters, flag-off byte-identical, ktest log byte-identical. Report
ns/served member + wall per presented frame. The target band is triangle
≤1.2 s / menu ≤1.36 s at 1B.

**Phase 4 — fast-mode playability (parallel-capable after Phase 2 passes).**
GPU offload coverage, presentation pacing, audio sync (APU mixer/host
output), input. Gate runs and the accurate mode are unaffected (no-mix binds
all gate runs). Scope detail filed when Phase 2 meets its bar.
*Status 2026-10-20 (§199n):* the GPU side is now the **largest** single item on
the sw wall (menu 48% / triangle 39%), and present pacing is the playability
gate (686 flips/1B = 11.7 fps with GL). So Phase 4's first slice is the
`--gpu gl --gpu-replace` per-frame path (submission batching, present/readback
pacing) plus the rasterizer's structural per-pixel cost — not more CPU
codegen. **2026-10-22:** the GL present path landed (~1.3 s of the menu wall,
readback opt-in); the CPU engine is the largest item again, so this note is
superseded by Phase 3b.

## 5. Validation and gates (two-tier)

- **Exploration slice (fast mode only):** build + format clean, unit
  differentials green, bench numbers recorded with fixed commands. No golden
  obligation — fast-mode outputs are not gate material.
- **Promotion (default-on, product-visible, or shared-code effect):** the
  full accurate-mode ceremony unchanged (development-direction §2:
  median-of-3 + exact SHAs/counters + `verifyFailures=0` + flag-off
  byte-identical) **plus** fast-mode gates: sync-point differential clean on
  menu + triangle 1B, ktest outcome match, title acceptance per phase.
- **ktest smoke 7/7 stays the standing gate** (accurate mode).

## 6. Constraint deltas (operator-approved 2026-10-18)

| # | Rule (before) | Delta (now) |
|---|---|---|
| 1 | "Boundary-preserving only" | Accurate mode only; fast mode per §2 |
| 2 | "`DynamicMethod` stays rejected" | Rejected for accurate-mode relay slices (E1–E3 unchanged); **authorized for the fast-mode block JIT** (Phase 1) |
| 3 | "No machine-code emitter without beating the unrolled-C# baseline" | Accurate mode unchanged; Phase 3 authorized under §2/§4 entry condition |
| 4 | "No new NuGet without operator approval" | Iced (MIT) approved for Phase 3 (not yet referenced); Ryujinx/ARMeilleure (MIT) design reference; `sources.md` entry on any reuse |
| 5 | Perf-slice ceremony every slice | Two-tier (§5): exploration vs promotion |
| 6 | "No real Xbox games" | Unchanged; fast-mode **benchmark-only** use requires an explicit operator decision (none as of 2026-10-18) |
| 7 | License undecided | Stays non-GPL; GPL/QEMU-translation vehicle explicitly rejected (§1) |

## 7. Invariants (must not change)

- Accurate-mode determinism, goldens, byte-identical contracts, flag
  discipline — forever the gate currency.
- MMIO program-order semantics and fault precision in **all** modes.
- The dual-mode separation: never mix engines within a run; fast mode never
  becomes gate currency; fast ↔ accurate equivalence is defined by §3 sync
  points, not byte equality.
- `References2/` protection rules; ktest smoke; direct development
  (wave runner retired); **single-brain rule for codegen work** — no
  parallel agents inside the JIT.

## 8. Open items

- Flag naming (`--jit`; `--jit-exact` is fixed) and CLI wiring.
- Tier policy and thresholds (evidence base §§172–174).
- SMC invalidation design (reuse `DecodeCache` inventory; validate with a
  synthetic self-modifying harness).
- xemu throughput calibration (Phase 0, optional).
- Whether fast mode ever becomes the default — operator decision, not
  proposed.
- Phase 4 scope detail (GPU coverage, pacing, audio, input).

---

**Provenance.** Measured evidence: `xgrid-architecture-decisions.md` §§131,
134, 137–138, 149–179b, 186a, 187–189; `jit-codegen-scope.md` (§20.6
microbenchmark, §138 decomposition); `gpu-swapchain-brief.md` §5 (gap math);
`development-direction.md`. External (facts/design only): QEMU TCG (GPL-2.0),
Ryujinx ARMeilleure (MIT), Iced (MIT), Intel SDM. Operator decision
2026-10-18, recorded in decisions §190, `AGENTS.md`, `ImplementationPlan.md`.
