# XGrid — Development Direction (canonical, revised 2026-11-22)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 0. Operator direction (2026-11-22): full xemu port, playable first

<!-- STATUS:BEGIN (generated from docs/status.json by scripts/status.ps1; do not edit) -->
**Current status (2026-11-22):** P1 / **XFP4 - PGRAPH surface, texture and cache semantics** (active, attempt 0).
**Gameplay (P1):** H:\Tests\XBOXReady (7 optimized ISOs; 007 1.60 GB, Halo 3.38 GB, 50 Cent, Bad Boys II, Destroy All Humans!, Tak, Ninja Gaiden Black) - P1 sprint (2026-11-22): the objective is at least one game playable; no title is playable yet. P1S0 measured 007 at 0.100x (30B gameplay 413.254 s; runner 53.5%, non-JIT 20.1%, b4 35.7%, GL 0.42%; N15: shape fixes top out at ~0.20x). P1S1 is done: the live WASAPI sink plays the EP FIFO PCM (state=Playing; WAV SHA 813CB8DE... = TM8). The operator chose the full xemu/QEMU CPU engine port; P1S2 is done (QEMU CPUTLBEntry as QemuTlb, 9 exact differential tests; probe 0.84 vs 0.64 ns, so the TLB is not the lever - N16). P1S3 (TCG IR + i386 translator) is done: slices 1-5 landed (managed TCG IR + IR interpreter + 77 exact translator differentials: MOV/ALU/INC/DEC/NOP/JMP/Jcc, the reference memory shape Lea/Load/Store with 1/2/4-byte accesses and segment bases, 8/16-bit and partial registers, shifts/rotates, SHLD/SHRD, NEG/NOT, MOVZX/MOVSX, LEA, XCHG, SAHF/LAHF, MUL/IMUL/DIV/IDIV, PUSH/POP, BT/BTS/BTR/BTC, CMPXCHG/CMPXCHG8B, XADD, 16-bit addressing, REP string ops (MOVS/CMPS/STOS/LODS/SCAS), the MMX register file and the packed integer family (padd/psub saturation, pmul*, pcmp/pack/unpck, shifts, moves, maskmovq, pinsrw/pextrw/pmovmskb/pshufw, EMMS); 828 CPU tests green; the MMX file is the real FpuState and the 641-case cpu-oracle corpus runs through the IR engine with counted fallback - exact against the independent oracle, translated=2501/fallback=2344 after the integer batch: ADC/SBB, TEST, SETcc, CMOVcc, BSWAP, BSF/BSR, ARPL, sign-extends, XLAT, flag ops, PUSHF/POPF, PUSHA/POPA, LEAVE, LOOP/JECXZ), x87 (TcgFpu: full arithmetic/compare/unary/BCD/stack set over the shared FpuState/Fpu80) and SSE (TcgSse: moves, packed/scalar arithmetic, logical, compares, shuffles, conversions, MXCSR) - corpus 4202 translated / 643 fallback (641 harness FXSAVE + 2 ENTER), 892 CPU tests); P1S3 (TCG IR + i386 translator) is done; the IR interpreter measures 14.80 ns/member vs the raw runner 6.30 (2.3x) on a hot loop, and at real scale it pays ~132 ns/instruction, so the engine is integrated first (block runner, ITcgMemory over the CPU MMU, interrupt poll, fallback); P1S4 slice 1 lands the engine seam and TB cache: XboxCpu implements ITcgMemory over its own MMU/MMIO path, RunTcg polls interrupts and TSC per instruction with the exact interpreter as fallback, and TcgBlockCache caches one translated instruction per EIP (15-byte head check, clears on MMU flush and decoded-code stores) - exact on the triangle leg's first 10M steps (RAM 97CA74AA, CPU FA4E4131) at 5.24 vs 11.70 Msteps/s (1.06 without cache); slice 2 adds TcgExecutionState (no per-instruction allocations) and skips Fetch on TB hits -> still exact at 7.59 Msteps/s (2.7x the uncached interpreter, 0.65x the default cached one); the corrected real-scale cost is ~132 ns/instruction, so replacing the non-JIT share saves ~10% wall, and the switch gate now needs multi-member TBs with a per-member poll contract plus the codegen; slice 3 adds the per-member poll contract (TcgOpKind.Poll advances TSC and checks the PIC between members; a pending interrupt publishes the live state, delivers with the next member EIP and abandons the block) and the member limit that makes the step budget exact - multi-member TBs (up to 16) are now exact at 100k/1M/10M and at both 1B accurate anchors (triangle E58AE5D7/C43D5B49, menu 5C0CF968/E2B4D327) at 15.51 msps (10M) / 11.06 (triangle 1B) / 10.34 (menu 1B); the engine is not switched because it is not yet faster than the fast raw engine, 895 CPU tests). P1S4 is done and P1S5 (parity and switchover) is done: the parity bundle is exact with the ported engine (four accurate 1B goldens incl. both frames, ktest log CA5D3867 byte-identical, ktest 100M workload, cpu-oracle), the product is NOT switched because the engine is 5-7x slower, and the speed ceiling is recorded - the product is at 0.100x (13.7 ns/step), 0.5x needs ~2.7 ns/step, and the ported engine reaches 66-90 ns/step, so the phase stops with 0% of the required speedup from this port. The 0.5x P1 gate still needs xemu-class ~1.2 ns/step. XFP2 is active: the attribute-decode differential pins all six array formats (UB_D3D/UB_OGL/S1/F/S32K/CMP), the (0,0,0,1) defaults, count 0/2 and stride 0/advance through the real fetch path (10 differentials, 490 Nv2a tests); the cull/degenerate/clip-window rejection predicates are pinned too (8 cull tests), and primitive assembly is pinned (DRAW_ARRAYS packed start/count, range coalescing vs separation, one draw command per BEGIN/END; 496 Nv2a tests), triangle 1B anchor unchanged; the recorded title rows reproduce - 007 134,004 draws/1,005,989,482 px/6,478 frames and NGB 4,251/187,315,817/238,137 with EIP 0x8001B047, and the fast engine's one-frame sensitivity to the host flag set is documented. XFP3 is active: the NGB colour-count differential was withdrawn after measurement - the stored XGrid/xemu pair differs in 89.1% of its luminance distribution (different storm moments; mean delta 12.3/14.7/16.2), both surfaces are A8R8G8B8+Z24S8 (0x08080228 swizzle / 0x08080128 pitch), and xemu cannot be driven to the same guest step - so XFP3 rests on the exact VshProgramPlan/corpus differentials and the live compile gate, which now also compiles the five unique gate-title combiner configurations at capacities 1/8/32 (the menu leg's four are a subset), 109 GpuGl tests. XFP4 is active and the texture-format surface is done: all 43 reference ids are classified and backed, and the five depth ids now run the reference shadow-map compare (TryDecodeDepthTexel/CompareShadowDepth for the six depth functions, sampled through the stage wrap/border/linear/swizzle state) instead of resolving only NEVER/ALWAYS; the clear rules now carry the reference per-channel and per-field masks (CLEAR_SURFACE_R/G/B/A and _Z/_STENCIL, format-forced alpha lanes, the GL backend programming the clear masks), the remaining XFP4 work is swizzled render targets (NGB programs 0x08080228 SWIZZLE surfaces that the fill/clear still address as pitch), the texture-cache content identity and the 22-row corpus differential. P1S6 is closed negative: 7 of the nine parts pass (own render, live input, save/load, xemu frame policy, live audio, product defaults, gates) and the speed floor fails (0.100x, and 10 minutes derives from it), so the repository stays private; reopen with an engine at <=2.7 ns/step or an operator re-scope. The full-port program continues at XFP0 (done) and XFP1: the NV097 method surface is now machine-verified (215 defines / 201 rows; generated 1031 handled + 280 declined offsets) and an opt-in unknown-method counter measures 361 unknown writes in 18.6M methods on the triangle 1B leg with identical anchors; the reference-surface expansion is exact (1042 handled + 280 declined offsets; case4 strides fixed), and the residual 226 writes are seven offsets outside the reference NV097 surface (0x17C4, 0x1D84, 0x1710, 0x380, 0x16BC, 0x1D80, 0x9F8) that xemu also ignores - valid-surface coverage gap zero; the pgraph state-type switch has its first section (PgraphTextureState owns the four texture slots and bump state; behavior and snapshot order unchanged, triangle F9945E16/8836C7EB and menu 5C0CF968/E2B4D327 exact, 480 Nv2a tests); PgraphState now also owns the transform-program rows, transform-constant contexts and the PshCombiner (same anchors/tests); the six surface/clip scalars now also live on the state (ref properties, zero call-site churn; same anchors/tests), so the state type is the storage and no --pgraph-port toggle is needed - the differential is the gate; the seven-title method differential and XFP2 are next. The reference map is docs/research-notes/cpu-engine-port.md. The repository stays private until P1 (docs/open-source.md). Oracle: data/xemu/xemu.exe (xemu 0.8.139), same optimized ISO + firmware/EEPROM; scripts/xemu-oracle.ps1 selects the disc with -config_path and captures a DPI-correct client area; a capture is a window screenshot, so the policy is mean/colour/coverage, never pixel-exact.
**Perf reference (M-D, deferred; H1, 1B, fast):** triangle gl+replace 6.40 s / sw 11.10 s; menu gl+replace 17.63 s / sw 27.40 s; gap ~4.9x / ~11.7x.
**Phase score:** 79.65% (93.99 of 118). **Next:** XFP5 (Ported GL renderer (pgraph/gl) behind IPgraphBackend), XFP6 (AC'97 codec and APU front end (full)), XFP7 (IDE/ATAPI and block-layer port), XFP8 (SMBus/SMC/EEPROM and nvnet port).
**Do not retry without a new measurement:** N1 C4 host-pointer translate probe, N2 Read-only-ROM decode caching alone, N3 Full trace/superblock formation, N4 Own the encoder (Assembler rewrite), N5 Remove the raw emitter's per-member EIP/TSC/served, N6 VshProgram.Execute allocations / scratch reuse. See docs/status.json for all 23.
<!-- STATUS:END -->

**Decision.** XGrid's engineering program is a **P1 sprint first, then a full
source port of xemu 0.8.139 to managed C#**. The 2026-10-24 rule ("port a module
only when the title matrix proves it blocks >=3 titles or >=20% of the matrix")
is **superseded**: the port is the breadth program, the P1 sprint is the fast
path, and the title matrix is the acceptance test. The previous decision is kept
as history in section 0a.

**Objective.** **At least one game playable** (P1), as fast as possible. A game
is playable by the nine-part definition in `ImplementationPlan.md` E1 section 1:
own render, live input, **>= 0.5x guest time**, 10 minutes, save/load, the xemu
frame policy, **live host audio** (not an offline WAV), product defaults, and the
gates exact. **No title is playable yet**: 007 reaches its own scene and takes
scripted input but runs at 0.11x guest time (12.85 s per 1B steps) and its audio
is an offline WAV. The gates are **M1** 007's own render/input milestone (met
2026-10-25), **P1** the objective, **P2/P3** breadth after it, **P4** seven of
seven (XFP10) and **P5** ledger complete (XFP14).

**Means (2026-11-22, fast path).** First, a short **P1 sprint** on the existing
engine: measure 007 (P1S0), add a live NuGet audio sink and the AC'97 codec
(P1S1), fix the measured speed blockers to >= 0.5x (P1S2), and run the P1 gate
(P1S3). That is the fastest path to at least one playable game; **NuGet host
packages are allowed** (C# first, native edges inventoried in
`docs/porting-policy.md`). After P1, the full xemu 0.8.139 port (XFP0-XFP14)
continues for breadth: PGRAPH + the GL renderer (XFP1-XFP5), AC'97/APU (XFP6),
IDE/block (XFP7), SMBus/SMC/nvnet (XFP8), USB/OHCI/XID (XFP9), machine/chipset
(XFP10), the CPU/memory core (XFP11), host backends (XFP12), snapshots (XFP13)
and parity close (XFP14). The order, the module ledger and the explicit
out-of-scope list are `docs/xemu-port-manifest.md`.

**Why the order.** The remaining blockers are device behavior the reference
already implements: Halo's D3D resource wait, Ninja Gaiden Black's fragment
dither, the missing AC'97 codec, Bad Boys II's resource scan. The P1 sprint fixes
the first playable title with the existing GL backend and a live audio package;
the full port follows for breadth. In the full port the CPU is sequenced last
because the interpreter/raw JIT is bit-exact and faster than xemu on the
CPU-bound ktest - but the P1 sprint promotes XFP11 if the measurement says the
CPU is the wall to 0.5x.

**What the port is not.** It is not native embedding: no QEMU/xemu library and
no C/C++ emulator code in the build (`docs/porting-policy.md`). It is not a
product rewrite: the CLI, **Avalonia frontend (kept by operator decision
2026-11-22)**, `XGrid.Host`, the snapshot format, the test harness and the gates
stay (`ImplementationPlan.md` E1 section 5). Vulkan, Chihiro and QEMU
user-mode/packaging infrastructure stay out of scope with reasons.

**Media.** `H:\Tests\XBOXReady` (seven optimized ISOs) is the acceptance set and
xemu-oracle media. `E:\Microsoft Xbox` (945 retail CHDs, 1.44 TB) is the broad
compatibility library for title triage and the CHD path - never an oracle row or
a gate. `H:\Tests\XBOXTest` (37 Redump ISOs) is XGrid-only compatibility media.
`data/test-samples/` stays gate currency.

**Open source.** XGrid is GPL-2.0 (`COPYING`, `LICENSE`,
`THIRD_PARTY_NOTICES.md`). It is **open source by licence**; the repository is
**deliberately private because no game is playable yet** (operator decision
2026-11-22). Publication is triggered by the first title meeting the strict bar
and is a checklist in `docs/open-source.md`. The full port increases the notices
surface, not the licence.

**Exit.** P1 (at least one game playable) is the objective; P2/P3 are breadth
and P4 (seven of seven) and P5 (ledger 100% verified/out-of-scope) close the
program. Guest speed and presented fps are reported for every playability row; a
step-budget run is not a playability claim. The plan follows the speed
checkpoint: if the GPU/device stage is not the majority of the wall, XFP11 (CPU)
is promoted.

## 0a. Superseded (2026-10-24, second): a playable emulator, not a wholesale port

> Superseded 2026-11-22 by section 0. The title-matrix loop, the seven-part
> playability definition and the media policy remain valid and are carried into
> E1; the "targeted port only" rule is what changed.


**Operator decision.** The goal is a **playable Xbox emulator**: at least three
retail titles reach gameplay with input, audio and save/load. The previous
order (M-G -> M-P -> M-D) is replaced because it committed 40-60 sessions to a
136k-LOC xemu port before measuring how broad the gap was. The new order is
**M-T: title matrix -> blocker probe -> fix the shared blocker -> playability
gate -> grow the matrix**.

**The port rule.** xemu 0.8.139 is the oracle and the source for specific
modules, not the base to rewrite. Port a module only when the title matrix
shows the same xemu source file blocks **>=3 titles or >=20% of the matrix**,
and keep the old path as the fallback until the differential is exact. Never
port `tcg/` or `softmmu/` unless the matrix proves the CPU is the blocker -
today it is not (ktest 2B is bit-exact and faster than xemu).

**Why.** The current engine already boots the real firmware to the dashboard,
runs 007's XBE, enumerates USB, streams DVD/HDD and executes the guest DSP
microcode. The gap is device behaviour (the APU VP mixer, a handful of NV2A
PGRAPH behaviours, timing), not the QEMU core. A wholesale rewrite produces no
user-visible product until late; the matrix loop produces a playable title
every few sessions.

**Media.** `H:\Tests\XBOXReady` has the two optimized ISOs (007 1.60 GB, Halo
3.38 GB) and is oracle-grade. `H:\Tests\XBOXTest` has 37 full-disc Redump ISOs
that xemu cannot boot; they are **XGrid-only compatibility media** that may
prove a blocker but are never oracle rows or gates. `data/test-samples/` stays
gate currency.

**Exit (M-T):** three retail titles playable by the seven-part definition in
`ImplementationPlan.md` E1 (own render, scripted input, 10 minutes, save/load,
xemu frame match, audible output, gates exact). Speed is reported, never
claimed, until then.

## 0b. Superseded (2026-10-22): xemu-class performance is the long-run objective


> The generated block above is the current status; the narrative below is
> history. The execution plan and the task queue are in `ImplementationPlan.md`
> -> "Execution plan (current)".

**2026-10-23 operator decision: GPL-2.0, port xemu/QEMU, full C#.** XGrid is
now licensed under **GPL-2.0** (`COPYING`) and may port code from xemu, QEMU and
the other GPL-compatible references, preserving every notice. The project stays
**full managed C#** - no QEMU/xemu native library and no C/C++ emulator code in
the build. `References2/xemu-0.8.139` is the primary source. The first port, the
**APU GP/EP DSP** (`hw/xbox/mcpx/apu/dsp/`), is **landed** (P1.2-P1.4: the
interpreter, the GP/EP frame glue and a 48 kHz PCM sink); the VP mixer
(`apu/vp/`) and the AC'97 codec path are what make it audible next. The CPU TCG
is **not** a port target: the raw emitter is already bit-exact and faster than
xemu on the CPU-bound ktest; the gap is the boot/device/GPU path. The working
xemu 0.8.139 menu oracle is `docs/research-notes/xemu-menu-oracle.md` (the old
"xemu deadlocks" note was specific to the 0.8.136/0.8.137 media). Binding policy:
`docs/porting-policy.md`; provenance: `THIRD_PARTY_NOTICES.md`.

**Status 2026-10-22 very late (39th session, §§239-240): the device stage was
measured for the first time and one half of it landed.** A temporary probe priced
the triangle 1B `gl+replace` leg as **1.31 s of 6.1 s inside 44,030 device
writes** - 766 ms in the 2,706 draw flushes (interpreted vertex program 413-435
ms, GL submission 173-181 ms, setup 61-66, attribute fetch 34-36, epilogue ~70)
and ~540 ms in the 6,455 surface clears. (1) §240: a 32-bit clear now fills rows
through `Span<uint>.Fill` instead of one `WriteGpu` per pixel - device time
**1,306 -> 816 ms**, **triangle `gl+replace` 6.31/6.43/6.34 -> 5.61/5.74/5.73 s
(-10.6%, 3/3)**, menu 22.43/20.76 -> 19.98/18.82 s (-10.6%/-9.4%). (2) §239: the
raw emitter's EIP/TSC/served became static per edge (11 emitted instructions per
member removed) - **-3% engine, 0% wall**, which is the session's decisive
negative: the raw runner is **latency-bound, not instruction-bound**, so C's next
lever must remove *edges* (the chain edge is ~60 host instructions per 4-member
loop iteration), not instructions. Every 1B anchor, both accurate goldens, both
GL frames and the ktest log are byte-identical. **The largest unowned CPU-side
item left is the interpreted vertex program inside the draw flush (413-435 ms,
next-session.md §2 Option A).**

**Status 2026-10-22 late (38th session, §237 + §238): two exact Track C slices,
-8.9% / -3.4% on the headline legs.** (1) The boot ROM is a cached, blocked image
window: instruction fetches from flash/BIOS/mirror/overlay windows are a
decode-cache class of their own with no store path (wholesale flush on the host
actions that move an image), and the JIT forms blocks over them -
`nonRamSkips` 359,476 -> 0, one-instruction non-JIT runs 434k -> 136k. (2) The
non-JIT run slice `JitSpecSlice` went 256 -> 64 after its first-ever sweep
(`nonJitInstructions` 5.48M -> 2.62M; `nonJitMs` keeps falling to slice 4 but
formation eats it). Measured together: **triangle `gl+replace` 6.672 -> 6.081 s
(-8.9%)**, menu `gl+replace` 20.456 -> 19.762 s (-3.4%), 100M boot phase
2.276 -> 1.813 s (-20.3%), every 1B SHA/counter/frame and every corpus row
byte-identical. §237 also **reprized the "superblock for the boot" lever out**:
the ROM share of the non-JIT population is 9%, not the 100% §2 assumed, so its
ceiling was 6.4%; the remaining boot mass is 55k RAM heads below the build
threshold (an encoder price, §234e). Track B stays closed (gap 12 closed in §236).

**Status 2026-10-22 late (36th session, §236): compatibility gap 12 is CLOSED -
the GL backend decodes a cube stage's six swizzled faces into a host 2D-array
texture (`sampler2DArray`), so `xgu bump_cubemap_reflect`'s reflection panel
renders on the GPU and agrees with software within 2 pixels of 307,200 at both of
its anchors. The word pool now carries only a HILO dot input's raw pair, and only
when the stage's dot map is 4-7. Track B's bounded item is done: B stops here
until C has a measured result.** Track order unchanged; the next C work is the
engine-shape decision (§0 kill/reassess, `next-session.md`).

**Operator decision.** The long-run objective is **xemu-class performance or
better** on the same content. The 2026-10-20 correction was right to put the
product surface and the corpus ahead of synthetic optimization, and that work
landed: the GL present path is ~1.3 s of the menu wall (was ~9.9 s), the corpus
is 22 rows over 17 samples, and the frontend renders on the host GPU. But it
froze Track C, and the measured position now fails the freeze's own re-open
condition: **the CPU engine is ~17 s of the 24.3 s menu `gl+replace` wall
(~70%) and the dominant part of the 7.5-7.9 s triangle wall**, so the engine is
again >50% of a measured wall. Performance is no longer the last engineering
track.

**The measured gap (1B, H1, gate media).**

| workload | xemu / real-time band | XGrid now | gap |
|---|---:|---:|---:|
| triangle `gl+replace` | 1.20 s (xemu 830 Msteps/s) | 5.72-6.15 s | ~4.9× |
| menu `gl+replace` | 1.36 s (real-time band; xemu has no menu oracle) | 15.6-17.5 s | ~12× |
| ktest 2B (CPU-bound) | 15 s (xemu) | 9-10 s | XGrid already ahead |

The triangle's wall is ~5.8 ns per guest step vs xemu's ~1.2 ns; the engine's
served-member cost is 6.26 ns (triangle) and 14.77 ns (menu, measured 2026-10-23
in §242), and the device stage after D1 is ~596 ms of the triangle wall (§240 +
§241), so the menu's gap is the helper/device mix plus its 2.3x member count. A
2-3% slice cannot close either; the program is structural.

**New order.**

1. **C — performance (lead, re-opened).** C4 flat memory model / soft TLB,
   C5 trace/superblock formation, C6 device/MMIO fast path. Each stage is
   gated on a same-window A/B (≥10% wall or ≥20% engine), exact 1B
   SHAs/counters on both titles, flag-off byte-identical, ktest log
   byte-identical. The old C3-x/C3-y/C3-z list is closed; this is a new
   program, not a resumption of 2-3% slices. **C6 has paid twice (§240 the clear
   fill, -10.6% on both headline legs; §241/D1 the transform program plan,
   -24.8/-30.4/-32.8% on the menu) and the rest of the device stage is priced
   (~596 ms of the triangle wall, >=2.6 s of the menu's transform stage alone);
   C4 measured null (§232a) and §239 proved the runner is latency- not
   instruction-bound.**
   **C7 was decided (2026-10-23, §242):** option (a) — keep the raw engine and
   take the device/draw-flush path. That path paid twice more (D1 §241, D5
   §244: the combined draw-flush slice -10.3% menu, 6/6 pairs), and the queue is
   now **D8 → D6 → D7** (ImplementationPlan E1/E5b, `docs/status.json`):
   **D8** re-measures the per-member/edge and boot attribution with a hard
   target (steady-state ≤2 ns/member) and selects one option — including, now
   that the license point is settled, **porting the reference's engine/device
   shape** (QEMU/xemu TCG, xemu PGRAPH draw path); **D6** is the priced device
   remainder (compiled transform evaluator, 9.5-10.7% menu); **D7** attacks the
   boot (1.93 s triangle = 33% of that wall, 1.6× the whole 1.20 s target).
   The arithmetic in E1 is the reason: zeroing every non-runner stage still
   leaves the triangle's 3.43 s runner 2.9× off the band, so the engine decision
   must be reopened with data, not closed by the negative table.
2. **B — bounded finish of the in-flight B3 slice — DONE 2026-10-22 (§236).**
   The GL product renderer's computed-mode panel was black because a cube stage
   had no word source; it now samples a host 2D-array texture decoded from the
   guest's swizzled faces and agrees with software within 2 pixels of 307,200 at
   both of the row's anchors. **B stops here**; no new B fidelity rows while C
   leads.
3. **E — corpus steers.** Keep the matrix; add the performance rows
   (Msteps/s, wall per presented frame) and the xemu comparison where it is a
   valid oracle. No new corpus breadth beyond the standing rows while C leads.
   **G1-G3 (E5c)** are the optional wholesale NV2A/PGRAPH port: a compatibility
   milestone (73 recorded methods, DXT/mipmap/multistage formats, exact cache
   semantics), not the path to the band. **G1 landed** (the method audit + the
   texture-format table); **G2 closed negative (§251)** because the removable
   CPU vertex stage is 4.0-4.2% of the triangle wall and the reference design's
   per-draw submission costs 3x the menu's gross saving; **G3** is the
   consolidated re-baseline. The software rasterizer stays as the oracle; only
   GL frames may be re-baselined.
4. **P — product (parallel, not blocking).** The **APU DSP port is landed**
   (P1.2-P1.4: interpreter, GP/EP glue, 48 kHz PCM sink). The largest remaining
   product gap is audible output: the **VP mixer** and the AC'97 codec path.
   Run it parallel to C; do not let it displace C.
5. **D — only with a measured >2% ceiling.** Unchanged.
6. **Gates — unchanged and non-negotiable.** Accurate mode is currency; the
   performance program never relaxes the ceremony.

**Price every stage first.** Each stage starts with a 100M-step ablation that
prices its ceiling (the §199n pattern: elide the mechanism, measure the wall).
If the ceiling is <10% wall, stop and re-scope rather than land a polish pass.
**The draw-flush half landed (D1 + D5); the next stage is D8's measurement, not
another slice** - the trace was priced at 3-5% (§235) and the engine's remaining
levers are all below the bar on their own measurements.

**Kill/reassess.** If the engine program does not reach ≥3× engine improvement
after two structural iterations, stop and reassess the approach (memory model,
trace formation, or a ported reference core) rather than polishing. The
xemu-class target is the aim, not a promise; the measured stage results decide.
**Status 2026-10-23:** C4 null and C5 slice 1 -3/-4% met the first criterion; C6
then paid -10.6%, D1 -25/-31% menu and D5 -10.3%. The reassessment is **D8**: a
per-member/edge/boot attribution with a hard steady-state target and a written
decision, where "port the reference's shape" (GPL-2.0, full managed C#) is a
first-class option.

**Why not B first?** B3 is a correctness item for one homebrew row; C is
~70-90% of every product wall and the stated long-run objective. B3 is bounded
and stays in flight only because leaving the GL renderer visibly wrong is
worse than finishing it.

## 0c. Superseded (2026-10-20) — retained as history

**Superseded by §0 (2026-10-22).** The 2026-10-20 rationale is kept because it
explains why the product/corpus work happened first.

**The goal, restated.** A "xemu-like emulator" is four things, in user-value
order: (1) **breadth** — a growing set of real titles boots, renders, and stays
up; (2) **fidelity** — hardware behavior matches the console where titles depend
on it; (3) **playability** — speed, audio, input, save states; (4) **performance**
— the same content in the real-time band. XGrid is strong on (2) as *methodology*
(LLE, exact gates, independent differentials) and on (4) as *engineering* (native
CPU blocks, direct chaining). But its fidelity is steered by three programs, and
its product renderer is a CPU rasterizer. The previous order — Track C leads,
then Track B optimizes the software rasterizer — optimizes two synthetic
workloads and leaves the corpus and product surface frozen. That is the classic
emulator plateau: a very accurate, very well-tested demo.

**What is right and must not change.**

- LLE, no HLE: MCPX ROM → 1BL → 2BL → kernel → XBE. This is the only
  architecture that scales to a compatibility list.
- Accurate mode is gate currency; fast mode is a separate engine validated by
  bit-exact 1B differentials (charter §5). Every default flip keeps the
  median-of-3 + exact SHA/counter ceremony.
- Reference-first: xemu 0.8.139 primary (0.8.137/0.8.136 prior); independent
  CPU oracle (`tools/cpu-oracle`, Unicorn/TCG); GPL-2.0 with ported-code notices.
- The device *set* is complete (MCPX, PCI, IDE/ATAPI, USB-OHCI + XID, SMC,
  SMBus, EEPROM, PIC/PIT/RTC, APU, nvnet, NV2A). Only depth remains.
- The CPU engine (raw emitter, chaining, fault contract) stays landed and
  opt-in. Do not spend more sessions on it until the tracks below clear.
  *Correctness* work is exempt from the freeze (it is not codegen): 220 landed
  nine interpreter/decoder fixes found by the independent oracle, with no
  throughput claim and no gate anchor moved.

**What changed (2026-10-20; historical — superseded by §0).**

1. **Track B leads; its first slice is the present path, not the fill loop.**
   The product surface (`XGrid.Frontend.Avalonia`) renders on `--gpu sw` and is
   therefore GPU-bound. `--gpu gl --gpu-replace` exists but nets only 2.2 s on
   the menu (31.1 vs 33.3 s): ~9.9 s of GL draw/readback/present. The
   window-mode 4B run measured 12,675 flips at 1.6 ms readback each (20.7 s/4B,
   ≈1.1 s/1B) — real, but not the whole 9.9 s. **Measure the split first**
   (submission / readback / present), then remove the readback from
   fast-mode/window present (keep it for gates, screenshots, `--frame`). The
   ceiling is ~10 s, larger than any remaining CPU slice; the unexplained part
   is why the measurement is the first deliverable.
   The 2-pixel k1 interleave is demoted (≥2% acceptance, bounded ceiling); take
   it only if the present path stalls.
2. **Track E opens the corpus — the exit from sample-only fidelity.** Gate
   currency stays `data/test-samples/` (triangle, ktest, dashboard), but
   fidelity work gets a steering signal: `docs/compatibility.md` (boot stage,
   first frame, known gap, step anchor) plus a boot-to-first-frame harness;
   widen the sample set with legal homebrew/demos (nxdk samples once the
   toolchain exists, xgu examples, nxdk_vsh_tests goldens, OpenXDK) so
   NV097/APU/XBE work is driven by observed usage. Real ISOs stay benchmark-only
   (no gates, no fidelity work) until the operator changes the policy.
3. **Track P makes it feel like an emulator.** The frontend landed (`ea8b38f`)
   but is keyboard-only, silent, and defaults to the software rasterizer (an
   opt-in host-OpenGL renderer landed in §220). In parallel: host
   audio sink (APU output), physical gamepad binding, save-state V1, per-title
   config. Cheap relative to their product value; they do not block Track B.
4. **Track C is frozen after the landed slices.** C3-x hit its target
   (8.8 ns/served member triangle, 31.6 ns menu); C3-y and the 1024-slot xlat
   cache measured neutral and were reverted; C3-z is deferred (2–3% estimate).
   The only remaining CPU action is the operator's `--jit`/`--jit-raw`
   default-flip decision (the frontend already defaults both on; the CLI does
   not). Report ns/served member and wall per presented frame, not 1B-step
   walls (530M of the triangle's steps are idle-spin).
5. **Track D only with a measured >2% ceiling.** D3b page-end wait and
   bookkeeping remain candidates, not commitments.

**Steering rule (2026-10-20; the current rule is §0).** Every proposal states
which track it serves and the measured ceiling behind its acceptance. A task
that serves none of B/E/P is rejected even if it is interesting.

## 1. Priority stack (in order)

**Status 2026-11-22: the order is XFP (section 0).** The live queue is
`docs/status.json`; the task detail is `ImplementationPlan.md` E1
(XFP0-XFP14). The performance stack below is retained as history and as the
M-D reference; it is not the current task order. A task that does not advance a
ledger row in `docs/xemu-port-manifest.md` is not started.

**Status 2026-10-22 late (§232): C4 measured NULL; the levers are now measured,
not assumed.** The host-pointer translate probe was implemented and priced on an
interleaved 1B A/B (menu `runnerMs` 15,872 → 16,026, triangle 3,407 → 3,382,
walls within noise, SHAs exact) and reverted. The triangle's 6.9 s is
`RunJitBlock` 4.53 s (native 3.42 + 1.1 bookkeeping) + `RunNonJit` 2.25 s, and
the non-JIT cost is *decode*: 359,476 boot-ROM fetches (`0xFFFFFFxx`, all inside
the first 100M steps) at ~1.5 µs each because the decode cache refuses non-RAM
pages — **priced 0.54 s/run, 7.8% of the triangle wall**. Landed: the unified
interrupt poll (C6 slice 1, architecture; wall delta below resolution) and a
correctness fix for a raw store that lost its translate slot when CR0.WP was
clear. **The plan at that point was C4b (read-only-ROM decode path), then C5
trace formation (measured against `nonJitMs`), then C6 - all three are now done;
see the 39th-session status below.** Price stages with stashed interleaved
A/B legs, never with env-gated ablations that change guest behavior (§232e).

**Status 2026-10-22 very late (39th session, §§237–240): the "next" list above
is done and the device stage is now the paying track.** C4b landed as the
image-window class (§237: `nonRamSkips` 359,476 → 0, `nonJitCalls` 523k → 169k,
triangle `gl+replace` -6.4%), the non-JIT run slice went 256 → 64 (§238:
`nonJitInstructions` 5.48M → 2.62M, `nonJitMs` 2190 → 1650, triangle -3.7%),
and the raw emitter's per-member EIP/TSC/served became one static publish per
edge (§239: engine 6.3 → 6.1 ns/member, wall flat — the runner is
**latency-bound, not instruction-bound**, so the CPU-side lever is *edge*
removal). The device stage was measured for the first time (1.31 s of the
triangle 1B `gl+replace` wall inside 44,030 device writes: 766 ms draw flushes,
~540 ms clears) and its clear half was won (§240: `Span<uint>.Fill`, device
time 1,306 → 816 ms, triangle -10.6% / menu -10.6%/-9.4%). **Next: the
draw-flush half** (766 ms, of which 413-435 ms is the interpreted vertex
program), then the **C7 engine-shape decision** — the encoder (~7%) and the
code-volume question (§239, 0% wall) are already priced out.

**Status 2026-10-23 (41st session, §§241–242): the draw-flush half landed and
C7 is decided — option (a), keep the raw engine and take the device path.**
D1 (§241) compiles the active transform program once per draw (`VshProgramPlan`):
the transform stage 422 → 202 ms triangle and 7,134 → 2,600 ms menu, menu wall
**-24.8/-30.4/-32.8%** (3/3) with every anchor byte-identical; the triangle wall
is -4.02% median (its wall is pacing-bound, not transform-bound). D2 (§242)
re-read every price on the post-D1 build — triangle 5.839 s (runner 58.7%,
6.26 ns/member), menu 16.448 s (runner 77.8%, 14.77 ns/member), anchors exact,
gap 4.87x/12.1x — and wrote the decision table: encoder 8.0%/4.9%, trace
boundary population 6.04%/4.35%, code volume 0% wall, C4 probe null, device
stage 10.2% triangle / ≥15.8% menu. **Only option (a) is above the bar; the
engine-shape program is closed, D3 (menu decomposition) is promoted and D4 (a
new engine shape) is conditional on a per-member probe ≥20% engine.** Price
stages with stashed interleaved A/B legs, never with env-gated ablations that
change guest behavior (§232e).

**Status 2026-10-22 (course correction): Track C is re-opened and leads; B3 is
a bounded finish; E steers; P runs parallel.** The engine is ~17 s of the
24.3 s menu `gl+replace` wall and dominant on the 7.5-7.9 s triangle wall; the
long-run target is xemu-class (triangle 1.2 s, menu 1.36 s at 1B). The
2026-10-20 status blocks below are retained as history.

**Status 2026-10-22 (22nd session, §§220-221):** the frontend's renderer is a
setting - software (default) or the headless host-GPU OpenGL backend, which
measures **-29% wall** at 1B through the real `EmulatorSession` (34.8 s -> 24.7 s
on gate media) with the same picture; and per-title override V2 resolves a CHD
through a sector-level XDVDFS lookup (§221), so a compressed library keys by
title id. Suite **1757**. Track order is otherwise unchanged; the largest
remaining product gap is host audio, which needs APU DSP execution first.**Status 2026-10-21 (20th session, 215-218):** gap 6 is **closed end to end** -
the fixed-function vertex stage (213) and its enabled-light loop (215) are
opt-in (`--ffp`), PTIMER now models the 0.8.137 alarm registers (216), the APU
sample counter `NV_PAPU_XGSCNT` is derived from the EP frame divider (218, which
re-recorded the two menu RAM anchors; frames/CPU/counters unchanged), and the
frontend exposes the graphics-fidelity switches, per-title overrides and a
persisted EEPROM (217). Suite 1717. Track order is otherwise unchanged; the
largest remaining product gap is host audio, which needs APU DSP execution
first.

**Status 2026-10-21 (21st session, 219):** the GL backend now samples like the
software rasterizer (TEXFILTER tent/convolution, decoded wrap modes + border
colour, the PROJECT2D w divide, the clip origin on both fragment axes) and
`--gpu-replace` composes a software-fallback frame into guest RAM instead of
presenting the clear colour - compatibility gaps 7 (GL side) and 8 are closed.
Both 1B GL gate frames are byte-identical (`B6116DAE` / `7A860ED7`),
`BackendUnsupported=0`, and `xgu vp_lighting` 3B agrees with the software frame
within one 8-bit step in both filter states. Track P gained a frame-step
control (F7 / Shift+F7). Suite 1725; the largest remaining product gap is still
host audio (APU DSP execution first).

1. **C — performance (lead, re-opened 2026-10-22).** C4 flat memory
   model/soft TLB, C5 trace/superblock formation, C6 device/MMIO fast path.
   Acceptance per stage: same-window A/B ≥10% wall or ≥20% engine, exact 1B
   SHAs/counters on both titles, flag-off byte-identical, ktest log
   byte-identical. Exit: the xemu-class band (triangle ≤1.2 s, menu ≤1.36 s at
   1B) or a measured reassessment after two structural iterations. The
   software rasterizer stays the exact-mode gate renderer and differential
   oracle; the GL path stays the fast-mode product renderer.
2. **B — bounded B3 finish — CLOSED 2026-10-22 (§236).** A cube stage samples a
   host 2D-array texture decoded from the guest's swizzled faces, so the GL
   product renderer's computed-mode panel renders and agrees with software within
   2 pixels of 307,200. No new B fidelity rows while C leads. The
   present path itself is closed (readback opt-in, batching, sampler parity,
   replace-mode composition).
3. **E — compatibility corpus.** `docs/compatibility.md` + the harness +
   performance rows (Msteps/s, wall per presented frame). Every row has a
   reproducible command and a step anchor; a fidelity landing cites the row it
   fixes and the histogram that proves usage. No new corpus breadth beyond the
   standing 22 rows / 17 samples while C leads.
4. **P — product completeness (parallel, not blocking).** Host audio output is
   the remaining large item; P1 is the **xemu 0.8.139 DSP port** (state/DMA in,
   interpreter + GP/EP glue + PCM sink next). Pad binding, save-state V1,
   per-title config, renderer choice, frame stepping and persistent EEPROM are
   landed. Do not let P displace C.
5. **D — cheap wins with a measured ceiling.** Page-end wait, bookkeeping.
6. **Gates — unchanged and non-negotiable.** Accurate mode is currency.

## 2. Measured position (1B fast-mode legs, reference host; 2026-10-20 table + 2026-10-22 very late current position)

1 guest-second ≈ 733M steps (§68), so 1B steps ≈ 1.364 guest-seconds.
**"xemu-class" means the same content runs at wall ≤ guest-time**, measured
against xemu where an oracle exists (triangle ≈830 Msteps/s; ktest smoke xemu
15 s vs XGrid fast 9–10 s) and against the real-time band otherwise (menu).

**Correction 2026-10-21 (§208d):** the triangle rows below were measured on a
DVD-only leg, which never launches the disc's XBE. On gate media (HDD+DVD) the
same legs cost **11.3 s sw / 7.5 s `gl+replace`** at 1B on H1 (2,706 draws /
68 frames / 57.7M px). Read the table as the boot-animation workload it actually
measured; M-A's triangle target needs re-basing before it is chased.

**Current position (2026-10-22 very late, after §§237–240):** the present path
is done and the CPU/device engine is the wall again. On H1 at 1B: menu
`gl+replace` **18.8-20.0 s** / `sw` 30.35 s (raster ~11.2 s), triangle
`gl+replace` **5.61-5.74 s** / `sw` 10.03 s. xemu-class is **1.20 s** on the
triangle (830 Msteps/s) and **1.36 s** on the menu (real-time band; xemu has no
menu oracle), so the gap is **~4.7x / ~14x** (was ~6.6x / ~17.8x before
§§237–240). ktest 2B is already ahead (9-10 s vs xemu 15 s). **M-D is the lead
milestone, not the last one.** The table below is the 2026-10-20 measurement
(pre-§237) and is retained as history.

| leg | wall | raster | xemu reference | gap |
|---|---:|---:|---|---:|
| triangle `--gpu sw` | 8.69 s (corrected: 11.3 s) | 2.8 s | 830 Msteps/s (1.13× real time) | ~7× |
| triangle `gl+replace` | 6.49 s → **~6.4 s** (corrected: 7.5 s) | — | same | ~5.6× |
| menu `--gpu sw` | 33.3 s → **~30 s** | 12.0 s | no oracle (xemu deadlocks) | ~25× real time |
| menu `gl+replace` | 31.1 s → **~23.7 s** | - | same | ~17.4× real time |

**Track B slice 1 (§204) landed:** the GL path's overhead fell from ~7.9 s to
~1.3 s (readback removed from fast-mode present, per-draw submission 2.49 →
~0.5 µs/draw, `BackendUnsupported=0`, guest SHAs/counters identical). Same-window
medians vs `4f0bd47`: menu `gl+replace` **63.09 → 45.25 s (-20.5%, 3/3)**,
triangle 10.26 → 9.89 s (-0.9%, 3/3). The quiet-host projections above are the
ratio method against the previous session's references; the menu is at the
M-A boundary (22-25 s across windows) and the triangle is CPU-bound
(`nonJitMs` 3.75 s of `outerMs` 6.3 s), so it cannot reach 6 s from Track B
alone. Remaining menu GL submission ~1.2-1.5 s, dominated by the texture-content
fingerprint; the exact fix is guest-write span tracking, not weaker sampling.
The shared method-histogram cost (-6.2% menu sw) was also removed (§204 item 8).

**Track B slice 2 (§205) landed — draw-command batching.** A PGRAPH primitive
flush averages **82 triangles**, so the backend now stages a whole flush and
submits it with one instanced draw (per-triangle block = a uniform-array slot,
selected by `gl_InstanceID` through a flat varying; capacity 64 from the uniform
budget; `XGRID_GL_NO_BATCH=1` forces the old path). Same-binary interleaved A/B
at 1B menu `gl+replace`: **`drawMs` 1971 → 1045 ms (-47%, 3/3)**, wall median
42.20 → 40.43 s (**-4.2%, 3/3**), 59,437 instanced draws replace 2.76M single
draws, RAM/CPU SHAs identical, both 1B goldens byte-identical under the §204
policy (menu `6BE6325F.`, triangle `3FC4B419.`). Projected onto the 24.8 s menu
projection that is ~23.8 s against the 24 s M-A target. What is left of
submission is ~803 ms of block packing (100 float stores × 2.76M), not GL calls.


Anchors (fast mode, H1, 2026-10-22): menu RAM `34973510…` / CPU `F5D61C94…`;
triangle RAM `B45254CA…` / CPU `7DAB63EB…`. Accurate mode: menu
`6AB39AB0…`/`3D2740B8…`, triangle `812EA159…`/`2F632483…`; frames
`4EF1FBF6…` / `A1740DE0…`. The full per-leg table is `next-session.md` §0;
1B walls are continuity anchors only.

**Two hosts, two anchor classes (2026-10-20).** Development runs on two machines
that share this Dropbox tree; `docs/hosts.md` holds their fingerprints and which
anchors reproduce on each. Every measured table must say which class it is:

- **Semantic anchors** — 1B RAM/CPU SHAs, frame SHAs, and every counter
  (`draws`, `frames`, `stalls`, `BackendUnsupported`, `verifyFailures`,
  `MethodTotal`). These are **host-independent**: a mismatch between machines on
  the same commit is a determinism bug to file and fix, never an "environment
  moved" note. Open item: the §199l fast-mode triangle SHA (`A2DCA55F…`) that
  stopped reproducing — diagnose it, do not document it.
- **Performance anchors** — walls, `msps`, `rasterPsh`, `drawMs`/`flushMs`.
  Host-local, not comparable across machines; only same-window A/B pairs count.

**Reference host** is whichever machine reproduces the quiet-host absolutes (menu
`--gpu sw` ~30-33 s, triangle `--gpu sw` ~10-11.3 s at 1B on the *unmodified
baseline* — the old ~8.7 s triangle figure was the DVD-only boot animation
(§208d). M-A can only be recorded there; on any other host
`present-path-acceptance.ps1`'s SUMMARY is a ratio, not a milestone.

## 3. Milestone ladder

- **XFP (2026-11-22) - the lead program.** Full xemu 0.8.139 port, playable
  first. The gates are:
  - **M1 - 007 own render/input milestone: MET 2026-10-25 (TM4).** 100B steps,
    own first-level frame, scripted input, save/load round trip, xemu frame
    policy. **Not playability:** 0.11x guest time and offline audio.
  - **P1 - at least one game playable: THE OBJECTIVE** (all nine conditions).
  - **P2 - a second title playable** (breadth, after P1).
  - **P3 - three titles playable** (breadth).
  - **P4 - seven of seven oracle titles playable** (XFP10).
  - **P5 - ledger 100% verified/out-of-scope; release close** (XFP14).
  Playability is the nine-part definition in `ImplementationPlan.md` E1 section
  1 (own render, live input, **>= 0.5x guest time**, 10 minutes, save/load, xemu
  frame policy, **live host audio**, product defaults, gates exact).
- **Port rule (2026-11-22):** xemu 0.8.139 is the **port source**. Port every
  in-scope module in the XFP order, with the old path as the fallback until the
  differential is exact. The ledger (`docs/xemu-port-manifest.md`) is the
  coverage record; `scripts/port-manifest.ps1 -Check` is the gate (XFP0).
  Vulkan, Chihiro and QEMU user-mode/packaging infrastructure are out of scope
  with reasons in the ledger.
- **M-T (playable titles, 2026-10-24) - history; folded into XFP.** The loop
  (title matrix -> blocker probe -> fix -> playability gate) is retained as the
  acceptance method. TM0-TM8 are done; TM9 (NGB colour depth) is folded into
  XFP3. **Status 2026-10-26 (TM8):** the APU is audible - the VP mixer is
  ported and 007 writes real PCM; the AC'97 codec and the APU front end are
  XFP6.
- **M-A (Track B, present path, 2026-10-20) - history.** menu fast+gl ≤ 24 s, triangle ≤ 6 s at 1B;
  `BackendUnsupported=0`; SHAs/counters exact; the Avalonia display is driven
  by presents, not a per-frame readback. **Status 2026-10-20 (§204):** the GL
  overhead (~7.9 s) is removed and measured (menu `gl+replace` -20.5% 3/3,
  triangle -0.9% 3/3 same-window; `drawMs` 12.55 → 2.1 s; readback 1.5 s → 0).
  **M-A itself was not decidable on the 2026-10-20 host:** the unmodified baseline build
  measures menu sw 62.6 s / triangle sw 14.0 s against the recorded 33.3 / 8.69
  with an idle CPU, so absolute walls are not comparable. Re-run
  `scripts/present-path-acceptance.ps1` on the reference host state (menu sw
  ~33 s) before recording M-A; the 39th-session status below records it on H1. If
  the menu is still over, per-triangle batching
  is the structural step. **The in-app GL display landed 2026-10-22 (§220):** the
  frontend's Renderer setting runs the GPU backend headless with the per-flip readback (-29% wall at 1B through the real session), opt-in and not the default. The in-app switch is +9.8%/+8.2% guest throughput on the dashboard with every gate anchor unchanged (§223).
  **Closed/re-based 2026-10-22:** the present path is done (readback opt-in,
  batching, sampler parity, replace-mode composition). **Status 2026-10-22 very
  late (§§237–240): the numeric targets are met on H1 on the corrected HDD+DVD
  media — triangle `gl+replace` 5.61-5.74 s (≤6 s) and menu 18.8-20.0 s (≤24 s),
  with `BackendUnsupported=0`, exact SHAs/counters and a present-driven display.
  M-A is MET; the lead is M-D.** The `sw` legs (10.03 s / 30.35 s) are not part
  of M-A.
- **Display model (Track E/P) - LANDED 2026-10-21 (§208).** The console now
  presents what the display controller scans out: the VGA CRTC behind
  `NV_PRMCIO` is decoded (pitch/size/depth), `TryCopyDisplayFrame` +
  `--frame-display` export it, and the Avalonia frontend samples it at ~30 Hz,
  so direct-framebuffer guests (`gamma`, `hello`) finally show their output.
  `--frame` still exports the last flipped surface, so no frame anchor moved.
  The same session corrected the triangle's media (§208d): every triangle leg
  that used DVD only was measuring the boot animation.
- **M-B (Track E, corpus) - MET 2026-10-20; now 22 rows over 17 samples
  (2026-10-22).** Every row reproducible: `scripts/build-nxdk-samples.ps1` builds the nxdk
  samples from a pinned upstream commit (`a6c31ee`) and
  `scripts/boot-to-first-frame.ps1 -Corpus` re-measures them. Beyond
  dashboard/triangle/ktest: `mesh` (textured 3D with VS/PS shaders - mipmap use
  not yet exercised), `gamma`/`gamma-fade`/`hello` (direct-framebuffer), `led`
  (SMC), `xaudio` (APU voice engine live). The corpus immediately produced two
  measured gaps: the direct-framebuffer present gap and the never-buildable
  `nxdk-mesh` snapshot (both in `docs/compatibility.md`).
- **Cubemap reflection (Track E) - LANDED 2026-10-21 (§209).**
  `xgu bump_cubemap_reflect` went from 2 colours / 36% black to 21,041 colours /
  0% black, `|dmean|` (5.3, 2.4, 1.3) against its reference screenshot. The
  dominant bug was the cube face extent read from `TEXIMAGERECT` instead of
  TEXFMT `BASE_SIZE` (xemu's swizzled-texture rule), plus the three format
  gates. The reflection math needed no change - the "striped" frame that 208g
  blamed on the direction chain was the wrong face stride.
  Same session: **TEXFILTER became a real sampler feature** (tent at LOD 0 +
  quincunx convolution), opt-in as `--tex-filter registers` because it changes
  every textured pixel of the menu and the menu has no oracle. See gap 7.

- **M-C (Track P, playability):** audio output, physical pad, save/load in the
  frontend; no CLI required.
- **Speed (after P3):** xemu-class is the later objective, but it is
  reported, never claimed, until the playability gate is met. The 1B reference
  stays (triangle ≤ 1.2 s, menu ≤ 1.36 s); ktest 2B already beats xemu (9-10 s
  vs 15 s). The old per-slice 10% bar is not the rule for M-T.

## 4. Rules

**Accurate mode (unchanged; §§136–140, `jit-codegen-scope.md`).**

- Specialize semantics (pre-specialized per-shape handlers + unrolled runner),
  not dispatch. The 354-case switch is NOT dominant — data-path helpers are.
- `DynamicMethod` stays rejected *for accurate-mode relay slices* (~10% over
  unrolled C#; E1–E3 measured wall-neutral). No machine-code emitter for the
  accurate path without beating the unrolled-C# baseline.
- Boundary-preserving only: `Step()` = one original boundary (TSC, IRQ poll,
  faults, idle replay unchanged). `--decode-cache` default-on (opt-out
  `--no-decode-cache`); `--block-exec` / `--spec-exec` / `--fetch-fast` /
  `--decode-verify` stay opt-in, default-off.
- Accurate-mode promotion + every default flip still needs: 1B off/cache/(new)
  pairs on menu + triangle with SHAs (`4EF1FBF6…`, `A1740DE0…`) + all counters
  exact, `verifyFailures=0`, vec=0, flag-off logs byte-identical, median-of-3
  walls before claiming a win.

**Fast mode.** Governing doc: `docs/research-notes/jit-track-charter.md`.
Fast mode (`--jit`, opt-in) is a separate engine: MMIO program order and fault
precision retained; TSC/step/IRQ observability relaxed to block granularity;
deterministic per binary+script; never gate currency; one engine per run (no
mixing). `--jit-exact` is the bit-exact debug variant. The raw emitter (Iced,
MIT) is authorized and landed; it is the base of the C4-C6 program re-opened by
§0. Incremental sub-2% slices remain banned; every C4-C6 stage needs the §0
gate (same-window A/B ≥10% wall or ≥20% engine, exact 1B differentials).

**GPU / present.**

- Headless FBO (`--gpu sw`) is the gate currency. Never mix (full-frame replace
  or no frame), no async (emulation-thread only).
- The GL backend is the fast-mode product renderer (Track B). Readback is a
  gate/screenshot facility, not a per-frame cost. The present path is closed, and
  so is the bounded B3 item (gap 12, §236): a cube stage is a decoded host
  2D-array texture, and a HILO dot input is the only remaining word-pool holdout
  (one word per stage; no measured row uses one).
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs an
  operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish beyond `docs/compatibility.md` + a sample histogram
  (Track E gates it). Exotics, DXT/BRDF/mipmaps, cost model, seeds, writeback
  stay gated on observed usage.

**Corpus.** `data/test-samples/` stays gate currency. The **optimized** retail
ISOs at `H:\Tests\XBOXReady` are the M-G acceptance and the xemu-oracle media
(never gates). The 37 full-disc Redump ISOs at `H:\Tests\XBOXTest` are
XGrid-only **benchmark** media (xemu cannot boot them) - never gates, never
accurate-mode acceptance, never fidelity work. A title quirk is allowed only
when isolated, documented, and not a substitute for the hardware behavior (the
007 EOS quirk is the historical precedent, not a pattern to extend).

## 5. Hard non-goals

- **License is GPL-2.0; porting is allowed.** XGrid may port code from
  xemu/QEMU/Cxbx/xqemu under their GPL-compatible licenses, preserving the
  notices. It must **not** embed QEMU/xemu as a native library or add C/C++
  emulator code to the build; port the algorithms to managed C#. See
  `docs/porting-policy.md`.
- No `References2/` deletes/moves/rewrites ever. Its single canonical store is
  `References2/` itself; verify with `scripts/refs-manifest.ps1 -Verify` against
  `docs/refs-manifest.json`. Never work around a missing reference; do not
  reintroduce an out-of-tree reference backup (`D:\xgrid-refs-restore\` is
  retired by operator decision 2026-10-20).
- No codegen work except the P1 sprint's measured speed fixes (P1S2) or XFP11.
  C4-C6 micro-slices and the raw-emitter experiments remain closed without a new
  measurement (the negatives are binding).
- No port without a ledger row, a differential and a provenance line. "Ported"
  without a test is not a status; the manifest's `verified` requires the
  evidence.
- No micro-optimization below a measured 2% ceiling.
- No native QEMU/xemu embedding; port source to managed C# and record the
  provenance. New dependencies still follow the NuGet rule (precedent:
  `Xunit.SkippableFact` rejected; plain `[Fact]` + WGL-guard instead).

## 6. Standing gates

- **P1 playability:** the nine-part definition (`ImplementationPlan.md` E1
  section 1) with the xemu 0.8.139 frame at a comparable scene, live input,
  >= 0.5x guest time, live host audio, and the four accurate goldens + ktest log
  byte-identical. Gameplay is acceptance, not a merge gate.
- **XFP port:** the module's differential is exact (a vector table, a
  register/method pre-post table, or the row matrix), the old path stays until
  it passes, the ledger row is updated, and `scripts/package-verify.ps1` passes
  with the new notices rows. A module is not "done" because it compiles.
- `pwsh -File scripts/port-manifest.ps1 -Check` green once XFP0 lands; an
  in-scope module without a ledger row fails the gate.
- `dotnet build XGrid.sln -c Release`, `dotnet test`, `dotnet format
  --verify-no-changes` clean; warnings are errors.
- Release changes: `scripts/package.ps1` on every affected RID and
  `scripts/package-verify.ps1` green (`PACKAGE PASS`); the tree must be clean so
  the manifest records the commit, and the version's release notes must exist.
- ktest smoke 7/7 (~5 min, 792,800,535 steps bit-reproducible) + menu/triangle
  1B golden pairs before any perf/default-flip merge.
- Frontend changes: `dotnet build src/XGrid.Frontend.Avalonia -c Release` +
  project format clean; UI-only work does not re-run core gates, but it must not
  touch core/CLI/test files.
- Every Track B/E/P/C landing updates `ImplementationPlan.md` status,
  `docs/compatibility.md` (when it changes a sample row), and the session notes.
- Every C4-C6 performance landing reports **ns/served member + wall per
  presented frame**, the same-window A/B, and the exact 1B differentials; 1B
  walls alone are continuity anchors, not a performance claim.
- Session start on any machine: `pwsh -File scripts/host-doctor.ps1` (fingerprint,
  free space, gate media, toolchain, `References2` integrity). A session that
  skips it owns whatever environment surprise it hits.

