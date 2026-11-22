# XGrid — Detailed Implementation Plan

<!-- STATUS:BEGIN (generated from docs/status.json by scripts/status.ps1; do not edit) -->
**Current status (2026-11-22):** P1 / **P1S3 - CPU engine port B: TCG IR and the i386 translator** (active, attempt 0).
**Gameplay (P1):** H:\Tests\XBOXReady (7 optimized ISOs; 007 1.60 GB, Halo 3.38 GB, 50 Cent, Bad Boys II, Destroy All Humans!, Tak, Ninja Gaiden Black) - P1 sprint (2026-11-22): the objective is at least one game playable; no title is playable yet. P1S0 measured 007 at 0.100x (30B gameplay 413.254 s; runner 53.5%, non-JIT 20.1%, b4 35.7%, GL 0.42%; N15: shape fixes top out at ~0.20x). P1S1 is done: the live WASAPI sink plays the EP FIFO PCM (state=Playing; WAV SHA 813CB8DE... = TM8). The operator chose the full xemu/QEMU CPU engine port; P1S2 is done (QEMU CPUTLBEntry as QemuTlb, 9 exact differential tests; probe 0.84 vs 0.64 ns, so the TLB is not the lever - N16). P1S3 (TCG IR + i386 translator) is active: slices 1-5 landed (managed TCG IR + IR interpreter + 77 exact translator differentials: MOV/ALU/INC/DEC/NOP/JMP/Jcc, the reference memory shape Lea/Load/Store with 1/2/4-byte accesses and segment bases, 8/16-bit and partial registers, shifts/rotates, SHLD/SHRD, NEG/NOT, MOVZX/MOVSX, LEA, XCHG, SAHF/LAHF, MUL/IMUL/DIV/IDIV, PUSH/POP, BT/BTS/BTR/BTC, CMPXCHG/CMPXCHG8B, XADD, 16-bit addressing, REP string ops (MOVS/CMPS/STOS/LODS/SCAS), the MMX register file and the packed integer family (padd/psub saturation, pmul*, pcmp/pack/unpck, shifts, moves, maskmovq, pinsrw/pextrw/pmovmskb/pshufw, EMMS); 828 CPU tests green; the MMX file is the real FpuState and the 641-case cpu-oracle corpus runs through the IR engine with counted fallback - exact against the independent oracle, translated=2501/fallback=2344 after the integer batch: ADC/SBB, TEST, SETcc, CMOVcc, BSWAP, BSF/BSR, ARPL, sign-extends, XLAT, flag ops, PUSHF/POPF, PUSHA/POPA, LEAVE, LOOP/JECXZ), x87 (TcgFpu: full arithmetic/compare/unary/BCD/stack set over the shared FpuState/Fpu80) and SSE (TcgSse: moves, packed/scalar arithmetic, logical, compares, shuffles, conversions, MXCSR) - corpus 4202 translated / 643 fallback (641 harness FXSAVE + 2 ENTER), 892 CPU tests); P1S4 host codegen is next. Then P1S4 host codegen, P1S5 parity/switchover, P1S6 the P1 gate. The reference map is docs/research-notes/cpu-engine-port.md. The repository stays private until P1 (docs/open-source.md). Oracle: data/xemu/xemu.exe (xemu 0.8.139), same optimized ISO + firmware/EEPROM; scripts/xemu-oracle.ps1 selects the disc with -config_path and captures a DPI-correct client area; a capture is a window screenshot, so the policy is mean/colour/coverage, never pixel-exact.
**Perf reference (M-D, deferred; H1, 1B, fast):** triangle gl+replace 6.40 s / sw 11.10 s; menu gl+replace 17.63 s / sw 27.40 s; gap ~4.9x / ~11.7x.
**Phase score:** 79.65% (93.99 of 118). **Next:** P1S4 (CPU engine port C: host code generation), P1S5 (CPU engine port D: parity and switchover), P1S6 (P1 gate: at least one game playable), XFP0 (Port ledger, module map and differential harness for the xemu full port).
**Do not retry without a new measurement:** N1 C4 host-pointer translate probe, N2 Read-only-ROM decode caching alone, N3 Full trace/superblock formation, N4 Own the encoder (Assembler rewrite), N5 Remove the raw emitter's per-member EIP/TSC/served, N6 VshProgram.Execute allocations / scratch reuse. See docs/status.json for all 22.
<!-- STATUS:END -->

# Execution plan (current) — the checklist

> **A session starts here.** The generated block above is the current status.
> `docs/status.json` is the single source of truth; `scripts/status.ps1` validates
> it and regenerates the status blocks and the completion graph. The detailed
> narrative below is history, not the current task list.

## E0. How we work (binding)

**The one rule:** one active task at a time. The active task is the only thing a
session works on. A session may fix a build break caused by the active task, but
it may not start a second task, a "quick" refactor, or a docs refresh.

**The task lifecycle:**

```text
queued -> priced -> active -> validating -> done
                         \-> negative (recorded, not retried)
```

- `queued`: has an id, a title, a dependency list, and a price placeholder.
- `priced`: a measured ceiling exists (a 100M-step ablation, a stage timer, or a
  counted usage). If the ceiling is <10% wall or <20% engine, the task goes to
  `negative` instead of `active`.
- `active`: the acceptance test is written or the exact differential is defined.
  Exactly one task is active.
- `validating`: the implementation is in; the definition-of-done checklist (E8)
  is being run.
- `done`: the evidence bundle is in `docs/status.json`, the graph is regenerated,
  and the commit contains code + tests + status + graph.
- `negative`: the measured result is in `docs/status.json` under `negatives`,
  with a reopen condition. It is not retried without a new measurement.

**The ten rules:**

1. **One active task.** WIP = 1. No parallel agents on the same area, no
   "while I'm here" work.
2. **Price before build.** A task needs a measured ceiling before it becomes
   active. If the ceiling is below the bar, stop and record a negative. This is
   the rule that would have prevented the C4/C4b double implementation.
   **Port tasks are correctness tasks:** their price is the module's reference
   size and the session budget, and their acceptance is the module differential
   plus the title/corpus row. The 10% wall / 20% engine bar applies only to
   performance tasks and to XFP11's "not slower" acceptance.
3. **Acceptance test first.** Write the differential, golden, or exact counter
   check before the implementation.
4. **Definition of done.** A task is done only when its DoD checklist (E8) is
   complete. "Landed" without the evidence is not done.
5. **No redo without a new measurement.** The negatives registry (E7) is
   binding. A retry must cite a new measurement that invalidates the old
   evidence.
6. **One status file.** `docs/status.json` is the only place current status is
   edited. The three docs' status blocks are generated. No docs-only status
   commits.
7. **One task per commit.** Code, tests, status and graph land together. No
   mixed commits, no generic messages.
8. **Row-scoped corpus.** Re-verify only the rows the change can affect; a full
   sweep is a milestone-close event, not a per-slice ritual.
9. **Stop after two failures.** If a task fails its acceptance test twice, stop
   and reassess the approach; do not grind a third variation.
10. **History is append-only.** Do not rewrite old status blocks. Mark them
    superseded and put the current state in `status.json` + the generated block.

**The forbidden patterns (the ones that caused the 2026-10-22 churn):**

- Re-implementing a measured negative (C4 probe, ROM cache alone, word pool).
- Declaring a GPU/shader slice "landed" without a live driver compile.
- Rewriting the same status in `next-session.md`, `AGENTS.md`, the decisions
  file, the plan, the charter and the direction doc.
- Refreshing the completion graph separately from the assessment.
- Running the full 22-row corpus after every slice.
- A generic commit message (`Task (RepoFleet task #37)`) with no task id.

## E1. P1 sprint + XFP full-port program (2026-11-22 rewrite)

**Operator direction (2026-11-22).** The objective is **at least one game
playable, fast**. The immediate means is a short **P1 sprint on the existing
engine** (measure 007, live NuGet audio, measured speed fixes, the nine-part
gate). The long-run means is a **full source port of xemu 0.8.139 to managed
C#**, ordered so breadth follows P1. This supersedes the 2026-10-24 rule that
ported a module only when the title matrix proved it blocked three titles: the
port is the breadth program, and the matrix is the acceptance test that tells us
whether each ported module actually moved a title.

**The first-playable sprint is the critical path.** Until P1 is met, the binding
order is **P1S0 -> P1S1 -> P1S2 -> P1S3 -> P1S4 -> P1S5 -> P1S6**. It uses the
existing engine for audio (P1S1) and then **ports the xemu/QEMU CPU engine**
(P1S2-P1S5: softmmu/TLB, TCG IR + translator, host codegen, parity). The
full-port program (XFP0-XFP14) starts after P1; a full-port module is promoted
into the sprint only when the measurement proves it is the fastest fix.

**Why the port is the fast path, not the slow one.** The current engine already
boots the real MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard and runs 007's XBE,
but every remaining blocker is a *device behavior* the reference already
implements completely: Halo waits on a D3D resource lookup, Ninja Gaiden Black
renders a dither pattern the reference does not, Bad Boys II scans resource
handles, Destroy All Humans! stalls in the kernel, and the AC'97 codec is
missing. Fixing those one probe at a time has **not produced a title that meets
the operator's playability bar in seven sessions**; porting the reference's
PGRAPH draw/shader path and its AC'97 path addresses the whole class at once.

**The ledger is the definition of done.** `docs/xemu-port-manifest.md` (written
2026-11-22) lists every in-scope module, its target C# file, its status and its
differential, plus the explicit out-of-scope list with reasons.
`scripts/port-manifest.ps1 -Check` (XFP0) makes the ledger a gate. The program
is complete when the ledger is 100% `verified` or `out-of-scope` with a reason,
not when a feeling says the port is done.

**Scope in one line.** All of `hw/xbox/**`; the QEMU modules the Xbox machine
uses (`hw/ide`, OHCI USB, AC'97, `hw/display`, i8259/i8254/RTC, i2c, PCI, core,
input, `softmmu`/`system`, `accel/tcg` + `tcg` + `target/i386`, `migration`,
`block`, `net`, and the host `ui`/`audio`/`input` backends); source-level,
managed C#, no native emulator code. **C# first:** P/Invoke is allowed only for
host APIs or OS facilities (OpenGL, XInput, executable memory for the raw JIT,
host audio) and every edge is inventoried in `docs/porting-policy.md` §2.
Vulkan (`pgraph/vk`), Chihiro, QEMU user-mode, packaging and test infrastructure
are out of scope with the reasons in the manifest.

**C#, NuGet and native edges (2026-11-22).** C# is the default and the bulk of
the project. **NuGet packages are allowed** for host facilities and pure-managed
libraries - that is the fastest path to a playable title - subject to:

- The package is recorded in `Directory.Packages.props`, `THIRD_PARTY_NOTICES.md`
  and the release licence review in the same commit.
- No package may embed an emulator core or native emulator code. Host API
  bindings (OpenGL, audio, input, UI) are allowed.
- C# first: a new P/Invoke or native edge needs a recorded reason and a row in
  `docs/porting-policy.md` section 2.
- Current packages: `Silk.NET.OpenGL` (GPU), Avalonia (UI, **kept by operator
  decision**), CommunityToolkit.Mvvm, Iced, XISOSharp, CHDSharp. The P1 sprint
  candidate is **NAudio** (Windows WASAPI live sink); an OpenAL/SDL2 backend is
  the cross-platform follow-up.

### 1. The playability bar (corrected 2026-11-22)

The 2026-10-25 definition counted a title "playable" when it rendered its own
scene, took scripted input, saved and matched xemu. **The operator's bar is
stricter: no title is playable yet.** A title is **playable** only when all
nine hold:

1. The frame comes from the title's own render path (not the boot animation).
2. **Interactive input:** a live host pad and a scripted edge both advance guest
   state and change the frame; the loop polls at a playable rate.
3. **Near real time:** the measured scene runs at **>= 0.5x guest time** (wall
   per 1B steps <= 2.73 s; guest time = steps / 733M per second). The target is
   **1.0x** (xemu measures 1.14x on the gate triangle); 0.5x is the floor, not
   the goal. Report the guest speed and the presented fps, not a step budget.
4. It runs 10 minutes (or 100B steps, whichever comes first) with no fault and
   no hang.
5. Save/load round-trips in the frontend.
6. The xemu 0.8.139 frame at a comparable scene matches within the screenshot
   policy (mean / colour count / coverage; never pixel-exact).
7. **Live audio** through the host sink (the AC'97 path), not an offline WAV and
   not a documented silence, with no desync.
8. It runs from the product defaults (frontend settings), not a hand-built CLI
   flag set.
9. The four accurate goldens, the ktest log and the package gate stay
   byte-identical.

**Where the current engine stands (measured).**

| workload | wall per 1B steps | guest speed | verdict |
|---|---:|---:|---|
| 007, product scene (TM4: 100B in 1,284.8 s) | 12.85 s | **0.11x** | not playable |
| gate triangle, `gl+replace` | 6.40 s | 0.21x | not playable |
| xemu 0.8.139, gate triangle | 1.20 s | 1.14x | reference |
| real time | 1.36 s | 1.00x | target |

Audio is equally short of the bar: TM8 proved the VP mixer with an
`--audio-out` WAV; the live sink and the AC'97 codec are XFP6. The repository
stays **private** until a title meets this bar (`docs/open-source.md`).

| Gate | Meaning | Expected after |
|---|---|---|
| **M1** | 007 own render + scripted input + save/load + xemu frame policy | **met 2026-10-25 (TM4); a milestone, not playability** |
| **P1** | the first title meets all nine - **truly playable: THE OBJECTIVE** | P1S6, after the CPU engine port (P1S2-P1S5) |
| **P2** | a second title playable (breadth, after the objective) | after P1 |
| **P3** | three titles playable (breadth) | after P2 |
| **P4** | seven of seven oracle titles playable | XFP10 |
| **P5** | ledger 100% verified/out-of-scope; parity close | XFP14 |

**P1 is the objective.** P2-P5 are follow-on breadth and coverage; they do not
start before P1. "The project has a playable game" means P1 is met, not M1.

**The speed checkpoint (binding).** P1S0 measured **0.100x** and N15 priced shape
fixes out (~0.20x ceiling). The operator chose the **full CPU engine port**
(2026-11-22), phased as **P1S2-P1S5**: softmmu/TLB, TCG IR + i386 translator,
host codegen, parity. P1S5 must measure >= 0.5x or record the ceiling; no third
engine variation. In the full-port phase, re-measure after XFP5 (ported GL
renderer) and XFP6 (live audio): if the device/GPU stage is not the majority of
the wall, XFP11 is already covered by the sprint port; if it is, finish the
device ports first. The plan follows the measurement, not the list order.

The screenshot policy is unchanged: `scripts/frame-compare.py` reports
per-channel mean, distinct colours and black coverage; a row passes when black
coverage is within 2 percentage points, the colour count within 25% and
`|dmean| <= 25/255` on every channel. Retail rows are acceptance, never merge
gates.

### 2. The loop

**P1 sprint (until one game is playable).** This is the fast loop and the
current critical path:

1. **Measure.** Run the target title (default 007) in the product configuration
   (`--gpu gl --gpu-replace`, raw JIT, live sink) from a fixed anchor; record
   wall per 1B steps, guest speed, presented fps and the stage split.
2. **Fix the measured blocker.** One change at a time (GL coverage/submission,
   CPU hot shapes, pacing, audio); price it by ablation before building.
3. **Verify.** Re-run the same anchor. A change that does not move the nine-part
   bar is reverted or recorded as a negative.
4. **Gate.** The four accurate 1B goldens and the ktest log stay exact; the title
   row is re-run.
5. **Stop at P1.** When the nine conditions hold, P1 is met; breadth (P2-P5) and
   the full port follow.

**Port tasks (after P1).** Port state and helpers first, the dispatcher second,
the behavior paths third (the DSP is the model); keep the old implementation
behind a flag until the differential passes; one engine per run; update the
ledger row and the notices in the same commit.

**Measure every change.** No "it should be faster" work: the P1 sprint prices
each candidate by ablation on the same anchor (the section 1 measured-gap
table is the baseline). Sub-2% changes are noise on this host.

### 3. The queue

| id | task | status | depends | acceptance |
|---|---|---|---|---|
| P1S0 | Measure the playable gap on 007 | **done** | - | a measured table (wall per 1B, guest speed, fps, stage split) and the ordered fix list to 0.5x |
| P1S1 | Live host audio sink (NAudio) for 007 | **done** | P1S0 | 007 live audio (`state=Playing`) and the WAV capture byte-identical to the TM8 PCM; four accurate golden legs PASS |
| P1S2 | CPU engine port A: softmmu/TLB and memory dispatch | **done** | P1S1 | exact TLB differential; the probe measured 0.84 vs 0.64 ns (N16: not the lever) |
| P1S3 | CPU engine port B: TCG IR and the i386 translator | active | P1S2 | cpu-oracle exact on the IR interpreter; fallback count recorded |
| P1S4 | CPU engine port C: host code generation | queued | P1S3 | exact 1B/ktest differentials; member cost below the current raw runner |
| P1S5 | CPU engine port D: parity and switchover | queued | P1S4 | every anchor exact; product engine switched only on parity; 0.5x measured |
| P1S6 | P1 gate (the nine conditions) | queued | P1S5 | **at least one game playable = the objective** |
| XFP0 | Port ledger, module map and differential harness | queued | P1S6 | manifest covers every in-scope module; checker green; no emulator source change |
| XFP1 | PGRAPH method surface and state | queued | XFP0 | every method a title writes is handled or declined with a counter; gates exact |
| XFP2 | PGRAPH vertex fetch, primitive assembly and draw | queued | XFP1 | draw/pixel counters reproduce the software path; gates exact |
| XFP3 | PGRAPH shader pipeline (VSH/PSH/FF/GLSL) | queued | XFP2 | NGB row passes the screenshot policy; corpus + gates exact; live driver compile |
| XFP4 | PGRAPH surface, texture and cache semantics | queued | XFP3 | 22-row corpus pairs reproduce; gates exact |
| XFP5 | Ported GL renderer (`pgraph/gl`) | queued | XFP4 | `--gpu gl` is the ported renderer, no software composition on supported rows, three titles render their own frames, and the speed checkpoint is measured |
| XFP6 | AC'97 codec and APU front end (full) | queued | XFP0 | live host audio for 007 and Halo through the full codec; APU snapshot round-trips; gates exact |
| XFP7 | IDE/ATAPI and block layer | queued | XFP0 | kernel probe, ktest media and DVD streaming reproduce; waits narrowed |
| XFP8 | SMBus/SMC/EEPROM and nvnet | queued | XFP7 | device vectors + dashboard + network frames; gates exact |
| XFP9 | USB OHCI, hub and XID | queued | XFP7 | enumeration + scripted pad on every title; gates exact |
| XFP10 | Machine and chipset assembly | queued | XFP5, XFP6, XFP8, XFP9 | one machine from ported devices; 1B differential; seven-title assessment |
| XFP11 | CPU core: softmmu + TCG + target/i386 | queued | XFP10 | cpu-oracle + ktest byte-identical + 1B anchors exact, not slower than the current engine |
| XFP12 | Host integration (ui, audio, input backends) | queued | XFP10 | frontend smoke, live window, audio device and pad unchanged or better |
| XFP13 | Snapshot/migration parity | queued | XFP12 | round trip at 1B and a title anchor reproduces the next-step SHA |
| XFP14 | Parity close: ledger, seven titles, release | queued | XFP11, XFP13 | ledger 100%, seven rows playable, `PACKAGE PASS` |
| TM9 | Ninja Gaiden Black colour depth (superseded) | superseded | TM7 | folded into **XFP3** - the ported fragment path is what fixes the dither |

**The queue is a default, not a lock.** The P1 sprint is the critical path:
P1S1 (audio) and P1S2 (speed) both depend only on P1S0, so the audio and speed
work can be taken in either order. After P1S3, XFP0 opens the full-port program;
if the sprint proves a full-port module is the fastest fix, that module is
promoted into the sprint with its ledger row.

### 4. Task detail

#### P1S0 - Measure the playable gap on 007

- [ ] Build Release and run 007 from the `wt-data/tm4/007-100b.xgs` anchor (+1B
  steps) in the **product configuration** (`--gpu gl --gpu-replace`, raw JIT,
  live sink when P1S1 lands) and with `--gpu sw` as the control.
- [ ] Record: wall per 1B steps, guest speed (`steps / 733M / wall`), presented
  fps, and the stage split (CPU runner, device/GPU, raster, audio). The target is
  **<= 2.73 s per 1B** (0.5x); the stretch target is 1.36 s (1.0x).
- [ ] Name the top five measured blockers with file/line and the tool or package
  that addresses each (GL coverage, draw batching, CPU shape, pacing, audio).
- [ ] No emulator source change; the deliverable is the P1 sprint price table.

**Acceptance:** the measured table, the delta to 0.5x, and the ordered fix list.

#### P1S1 - Live audio sink for 007 (AC'97 codec stays in XFP6)

- [ ] Add a live host audio sink behind `IAudioBackend` via a NuGet package:
  **NAudio** (Windows WASAPI, managed) as the first backend; keep
  `WaveAudioBackend`; leave an OpenAL/SDL2 slot for Linux and macOS.
- [ ] The audible source is the **EP output FIFO** (the VP mixer's PCM, already
  verified by TM8). The AC'97 codec register/file surface is a guest audio-device
  detail and is ported in XFP6; it is not required for the EP PCM.
- [ ] Wire the EP output FIFO to the live sink through a bounded buffer; never
  block the emulation thread; a missing device logs and stays silent.
- [ ] Acceptance: 007 produces live host audio (`state=Playing`) and the WAV
  capture is byte-identical to the verified TM8 PCM; the four accurate golden
  legs PASS; the solution tests and format are clean.

#### P1S2 - CPU engine port A: softmmu/TLB and memory dispatch

**The operator chose the full CPU engine port (2026-11-22) after N15:** shape
fixes top out at ~0.20x, so the 5x lever is the engine. Phase A ports QEMU/xemu's
soft TLB and memory dispatch first, because the measured per-member cost is the
memory probe, not instruction encoding.

Reference: `accel/tcg/cputlb.c` (`tlb_fill`, `probe_read`/`probe_write`, MMIO
slow path), `include/exec/exec-all.h` (`CPUTLBEntry`, `TARGET_PAGE_BITS`),
`include/exec/cpu-defs.h`, `softmmu/physmem.c` (flatview/address-space
dispatch), `include/exec/memory.h`.

- [ ] Write the reference map in `docs/research-notes/cpu-engine-port.md`:
  QEMU file/function to C# type/method, including the `CPUTLBEntry` fields
  (`addr_read`/`addr_write`/`addr_code` + addend), the 4 KB page size, the
  per-vCPU TLB and the MMIO slow path.
- [ ] Port the TLB state and fast probe into `XGrid.Memory` as a new `SoftTlb`
  (1-2 dependent loads, addend host address, per-page permissions) with the
  existing `Mmu.Translate*` as the exact fallback.
- [ ] Port the miss path and invalidation: page-table walk, permission faults,
  page-boundary/cross-page accesses, MMIO classification, flush on
  CR3/CR0/INVLPG/page-table stores.
- [ ] Differential: the existing `Mmu` tests plus a TLB vector suite (read,
  write, code, user/supervisor, RW/RO, page boundary, cross-page, MMIO, stale
  entry after invalidation).
- [ ] Micro-benchmark the translate throughput vs the current 4-entry cache and
  the raw emitter's inline probe; record ns per translation.
- [ ] Acceptance: the differential is exact and the probe measures at least
  **2x** the current per-member memory cost, or the measurement is recorded as
  a negative and P1S2 stops (no third variation).
- [x] **Result (2026-11-22):** QemuTlb ported with 9 exact differential tests;
  probe **0.84 ns vs the current SoftTlb 0.64 ns** - the TLB is not the 2x
  lever (N16). P1S3 (TCG IR + translator) is the next lever; the QemuTlb stays
  as the reference shape for the TCG port.

**Price:** 4-6 sessions. **Bar:** correctness + >=2x memory-probe ceiling.

#### P1S3 - CPU engine port B: TCG IR and the i386 translator

Reference: `tcg/tcg.c`, `tcg/tcg-op.c`, `include/tcg/tcg.h`,
`tcg/i386/tcg-target.c.inc` (host), `target/i386/tcg/translate.c`,
`decode.h`, `emit.c.inc` (guest).

- [ ] Port the TCG IR (opcodes, temps, globals, labels, basic blocks) into
  `XGrid.Cpu` as a managed IR builder.
- [ ] Port the i386 decoder/translator for the shapes the gates and titles use:
  start from the 641-case cpu-oracle corpus and the ktest shapes, then extend
  by measured fallback gaps.
- [ ] Keep the existing interpreter as the exact fallback for unported opcodes;
  count every fallback.
- [ ] Differential: `tools/cpu-oracle` exact on the IR interpreter before any
  host codegen.
- [x] **Slices 1-4 (2026-11-22):** managed TCG IR, `TcgInterpreter` (exact
  `AluOps`/`ShiftOps` flags), 32-bit register/immediate forms, the reference
  memory shape (`Lea`/`Load`/`Store`, 1/2/4 bytes, segment bases), 8/16-bit and
  partial-register forms, shifts/rotates, SHLD/SHRD, NEG/NOT, MOVZX/MOVSX, LEA,
  XCHG, SAHF/LAHF, MUL/IMUL (1/2/3-operand)/DIV/IDIV, PUSH/POP, BT/BTS/BTR/BTC,
  CMPXCHG/CMPXCHG8B, XADD, 16-bit addressing and REP string ops (MOVS/CMPS/
  STOS/LODS/SCAS, REP/REPNE, DF, 16/32-bit address size), the MMX register file
  and the packed integer family (padd/psub with saturation, pmul*, pcmp/pack/
  punpck, logical, avg/min/max/sadbw, shifts, moves, maskmovq, pinsrw/pextrw/
  pmovmskb/pshufw, EMMS); **122 differentials** exact (registers, EFLAGS, EIP,
  memory, MMX registers, tags), 825 CPU tests green, format clean. The MMX file
  is now the machine's real `FpuState`, and the **corpus gate** runs all 641
  `tools/cpu-oracle` cases through the IR engine (translated instruction ->
  IR, otherwise exact-interpreter fallback, counted): exact against the
  independent oracle (`translated=2430 fallback=2415`, the harness
  `fxsave`/`ldmxcsr` being 1,282 of them), 828 CPU tests green. Slice 10 adds
  ADC/SBB, TEST, SETcc, CMOVcc, BSWAP, BSF/BSR, ARPL, the sign-extends, XLAT,
  the flag ops, PUSHF/POPF, PUSHA/POPA, LEAVE and LOOP/JECXZ (16/32 address
  size, 16-bit stack wrap); 159 differentials, 863 CPU tests, corpus fallback
  2,415 -> 2,344. Slice 11-12 add x87 (`TcgFpu`, full arithmetic/compare/unary/
  BCD/stack set over the shared `FpuState`/`Fpu80`) and SSE (`TcgSse`, moves,
  packed/scalar arithmetic, logical, compares, shuffles, conversions, MXCSR):
  **188 differentials**, 892 CPU tests, corpus **4,202 translated / 643
  fallback** (641 harness FXSAVE + 2 ENTER). Next: P1S4 host codegen.
- [ ] Acceptance: the corpus is exact and the fallback count on the 007/ktest
  legs is recorded.

**Price:** 8-12 sessions. **Bar:** correctness.

#### P1S4 - CPU engine port C: host code generation

Reference: `tcg/i386/tcg-target.c.inc`; the existing `RawBlockEmitter` (Iced).

- [ ] Map the TCG IR to the existing raw emitter (one native function per
  translated block) or a new IR executor; keep the IL/raw engine as the
  fallback behind a flag.
- [ ] Port the host helpers for memory, FPU/SSE, string ops and CALL/RET.
- [ ] Differential: the 1B anchors, the ktest log and the cpu-oracle exact with
  the new engine; one engine per run, no mixing.
- [ ] Performance: measure ns/member and the 30B 007 leg; the new engine must
  beat the current 8.3 s/1B runner or it is not switched.
- [ ] Acceptance: exact differentials and a measured member cost below the
  current raw runner.

**Price:** 6-10 sessions. **Bar:** correctness + not-slower.

#### P1S5 - CPU engine port D: parity and switchover

- [ ] Run the four accurate 1B goldens, the ktest log, the 22-row corpus and
  the 30B/100B 007 legs with the ported engine; every anchor exact.
- [ ] Switch the product default only after the parity bundle; keep the raw
  engine as the fallback and a documented flag.
- [ ] Update the ledger (XFP11), the notices and the plan.
- [ ] Acceptance: the nine-part P1 gate is re-run with the ported engine and
  the speed floor; if it is still below 0.5x, record the measured ceiling and
  stop (no third engine variation).

**Price:** 4-6 sessions. **Bar:** correctness + speed floor.

#### P1S6 - P1 gate

- [ ] Run the nine-part check on 007: own render, live input, >= 0.5x, 10
  minutes, save/load, xemu frame policy, live audio, product defaults, gates.
- [ ] Evidence bundle in `docs/status.json` and `docs/compatibility.md`; the
  repository stays private until the operator confirms (publication gate).
- [ ] Acceptance: **P1 met** (at least one game playable) or a documented
  blocker with the measurement.

**Price:** 1 session. **Bar:** correctness.

#### XFP0 - Port ledger, module map and differential harness

- [ ] Review every row of `docs/xemu-port-manifest.md` against
  `References2/xemu-0.8.139`; add the per-file list for `hw/xbox/**` (149
  files) and correct any status that is not backed by a test.
- [ ] Write `scripts/port-manifest.ps1 -Check`: parse `src/**` for
  `Ported from`/`Derived from`, cross-check `THIRD_PARTY_NOTICES.md`, require a
  row and a status for every in-scope module, print
  `verified|ported|in-progress|not-started|out-of-scope` coverage, and exit
  non-zero on a gap or an orphan port header.
- [ ] Record the differential class per module type in the manifest section 2:
  reference vector table (DSP model), register/method pre-post table, or
  title/corpus row.
- [ ] Add `docs/open-source.md` to the docs index; confirm the licence and
  publication status are stated there (this plan's port increases the notices
  surface, not the licence).
- [ ] `pwsh -File scripts/port-manifest.ps1 -Check` and
  `pwsh -File scripts/status.ps1 -Check` green; no emulator source change.

**Price:** 1 session. **Bar:** correctness.

#### XFP1 - PGRAPH method surface and state

Reference: `hw/xbox/nv2a/pgraph/pgraph.c`, `pgraph.h`, `psh_regs.h`,
`vsh_regs.h`, `nv2a_regs.h`, `methods.h.inc`, `pgraph_state` accessors.

- [ ] Port the method table: every NV097 method with its latch and behavior -
  `SET_*` state registers, transform constants/programs, `SET_VERTEX_DATA*`,
  `SET_BEGIN_END`, `DRAW_ARRAYS`, `INLINE_ARRAY`, surface/clear/flip, semaphore
  and notification paths.
- [ ] Port the PGRAPH state (four texture slots, combiner words, transform
  constant contexts, viewport/clip, surface pitch/offset) into a state type the
  existing `Nv2aMmio` owns.
- [ ] Route methods behind a `--pgraph-port` flag (default off until verified);
  an unhandled method increments an `unknownMethod` counter instead of NOPing.
- [ ] Differential: a method/pre-post-state table generated from the reference
  source plus the 1B method histogram; every method the seven titles write is
  handled or explicitly declined.
- [ ] Gates: four accurate 1B goldens + ktest byte-identical (or the measured
  re-baseline recorded in `docs/status.json`).

**Price:** 3-5 sessions. **Bar:** correctness.

#### XFP2 - PGRAPH vertex fetch, primitive assembly and draw

Reference: `pgraph/vertex.c`, `pgraph/draw.c`, `pgraph_gl_bind_inline_array`,
`pgraph.c` draw/clear paths.

- [ ] Port attribute decode for every array format (UB_D3D, UB_OGL, S1, F,
  S32K, CMP), stride-0/bit-31 rules and the inline-array layout.
- [ ] Port primitive assembly (`SET_BEGIN_END`, arrays, elements, ranges), the
  cull/clip-window predicates, degenerate rejection and the draw-command
  boundary.
- [ ] Keep the existing software fill as the exact oracle until the draw path
  is verified; one engine per run.
- [ ] Differential: per-draw vertex/pre-post state captured from a title method
  stream; draw/pixel counters reproduce the current build (or the port fixes a
  measured gap - NGB's inline array is such a case).
- [ ] Gates exact; 007 and NGB rows re-run.

**Price:** 4-6 sessions. **Bar:** correctness.

#### XFP3 - PGRAPH shader pipeline

Reference: `pgraph/glsl/vsh-prog.c`, `vsh.c`, `psh.c`, `vsh-ff.c`, `vsh.h`,
`psh_regs.h`, `vsh_regs.h`.

- [ ] Replace the partial hand transcription of the vertex-program interpreter and
  combiner with the reference's complete program decode and evaluation, keeping
  `VshProgramPlan` as the fast form and the interpreter as the fallback.
- [ ] Port the reference's GLSL generation for VSH, PSH and fixed-function: the
  final combiner, fog, texgen, texture matrices, lighting.
- [ ] Keep the live-driver compile gate (N12): a shader is "done" only when it
  compiles and links.
- [ ] Differential: `VshProgramPlanTests`, the `xgu` corpus rows, and the
  **Ninja Gaiden Black colour-count row** (the TM9 finding: 12,913 colours vs
  xemu's 1,351; the fragment path is the named defect and this port is the fix).
- [ ] Acceptance: NGB passes the screenshot policy; the corpus and the four
  accurate goldens are exact.

**Price:** 5-8 sessions. **Bar:** correctness.

#### XFP4 - PGRAPH surface, texture and cache semantics

Reference: `pgraph/texture.c`/`.h`, `surface.h`, `swizzle.c`/`.h`, `s3tc.c`
(already ported), `psh.c` sampler paths.

- [ ] Port the surface geometry and clear rules (pitch/offset/depth/zeta layout,
  color/depth write masks) and the texture cache's content identity.
- [ ] Port every texture format and the mip-chain/cube layout; keep the existing
  `TextureFormats` table where it already matches and delete the duplicate
  paths.
- [ ] Differential: the format vector table (16 S3TC blocks + the colour
  formats) and the 22-row corpus pairs.
- [ ] Gates exact; no new quality knob.

**Price:** 4-6 sessions. **Bar:** correctness.

#### XFP5 - Ported GL renderer

Reference: `pgraph/gl/**` (14 files) - renderer, draw, texture, shaders,
program cache, buffer objects.

- [ ] Port the renderer behind the existing `IPgraphBackend` seam; keep
  `GlBackendFactory`/`GlPgraphBackend` names so the CLI/frontend do not change.
- [ ] Port the shader program cache, texture upload, state shadow, draw
  submission and surface binding.
- [ ] Keep `--gpu sw` as the exact gate currency and the differential oracle;
  `--gpu gl --gpu-replace` is the product renderer.
- [ ] Differential: software-vs-GL policy on every corpus row, `BackendUnsupported=0`,
  no software composition on rows the port supports, and the four accurate
  goldens.
- [ ] Acceptance: the first strict-playability assessment (P1): the ported
  renderer is the product path, and the speed checkpoint (>= 0.5x guest time or
  a promotion of XFP11) is measured and recorded.

**Price:** 6-10 sessions. **Bar:** correctness.

#### XFP6 - AC'97 codec and APU front end

Reference: `hw/audio/ac97.c`, `hw/xbox/mcpx/aci.c`, `mcpx/apu/apu.c`,
`apu_regs.h`, `apu_int.h` (the VP and DSP are already ported).

- [ ] Port the AC'97 codec register file and the PCM out path; port the ACI
  bridge.
- [ ] Complete the APU front end: the full register surface, interrupts, the
  mix-buffer protocol and the real-time cadence (one XGrid frame = eight
  32-sample reference frames is already fixed).
- [ ] Differential: codec register/file vectors; the PCM capture policy
  (non-zero sample share, RMS envelope, stereo difference, DFT peaks).
- [ ] Acceptance: **live host audio** for 007 and Halo (not `--audio-out`), in
  sync; the P1 playability assessment with the speed check; gates exact.

**Price:** 3-5 sessions. **Bar:** correctness.

#### XFP7 - IDE/ATAPI and block layer

Reference: `hw/ide/{core,pci,atapi,cdrom}.c`, `block/qcow2.c`,
`block/raw-format.c`.

- [ ] Port the IDE controller/taskfile, ATAPI packet protocol and the block
  layer operations the machine uses; keep XGrid's ISO/CHD adapters behind the
  same seam.
- [ ] Differential: the kernel's ATAPI probe, 27 READ DMA, the ktest media and
  DVD streaming; RAM/CPU SHAs identical.
- [ ] Acceptance: the Bad Boys II resource scan and Halo's D3D resource wait are
  re-probed with evidence; if the wait is not storage, the task closes with the
  measurement and XFP5/XFP1 own it.

**Price:** 4-6 sessions. **Bar:** correctness.

#### XFP8 - SMBus/SMC/EEPROM and nvnet

Reference: `hw/xbox/smbus_xbox_smc.c`, `amd_smbus.c`, `eeprom_generation.c`,
`mcpx/nvnet/nvnet.c`.

- [ ] Port the SMBus controller/transactions, the SMC command/reply state
  machine, the EEPROM image generation and the nvnet register/frame path.
- [ ] Differential: SMC command vectors, the EEPROM image bytes, DHCP/ARP frame
  vectors, the dashboard and ktest.
- [ ] Gates exact.

**Price:** 3-5 sessions. **Bar:** correctness.

#### XFP9 - USB OHCI, hub and XID

Reference: `hw/usb/hcd-ohci.c`, `hw/usb/core.c`, `dev-hub.c`,
`hw/xbox/xid.c`, `xid-gamepad.c`.

- [ ] Port the OHCI host controller, the hub topology and the XID device state.
- [ ] Differential: enumeration/port-status vectors, pad reports, and the
  scripted-input rows (the pad script must change guest state on every title).
- [ ] Gates exact.

**Price:** 3-5 sessions. **Bar:** correctness.

#### XFP10 - Machine and chipset assembly

Reference: `hw/xbox/xbox.c`, `xbox_pci.c`, `mcpx/mcpx.c`, `lpc47m157.c`,
`acpi_xbox.c`, `xblc.c`, `hw/intc/i8259.c`, `hw/timer/i8254.c`,
`hw/timer/mc146818rtc.c`.

- [ ] Assemble the machine from the ported devices; port the chipset/Super-I/O/
  ACPI paths the machine touches.
- [ ] Differential: a full-machine 1B trace (RAM/CPU/frame/counters) against the
  pre-port build, plus the ktest log.
- [ ] Acceptance: **P4** (seven of seven oracle titles playable) is assessed
  here; any title still waiting gets a named blocker and a follow-up task.

**Price:** 4-6 sessions. **Bar:** correctness.

#### XFP11 - CPU core: softmmu, TCG and target/i386

Reference: `softmmu/**`, `system/memory.c`, `accel/tcg/**`, `tcg/**`,
`target/i386/tcg/**` (decode, translate, helpers, TB/TLB).

- [ ] Port the memory dispatch/TLB and the TCG translator/TB model; map the host
  code generation onto the existing raw emitter (preferred) or a new IR
  executor, keeping the interpreter as the exact fallback.
- [ ] Differential: `tools/cpu-oracle` (641+ cases), the ktest log
  byte-identical, the four 1B anchors exact, and **not slower** than the current
  engine on the 1B gate.
- [ ] Acceptance: parity or better on the CPU-bound ktest; all titles still
  playable. If the port is slower, keep the raw engine and port only
  `softmmu`/the decoder, and record the scope exception in the manifest with the
  measurement.

**Price:** 20-40 sessions. **Bar:** correctness + not-slower.

#### XFP12 - Host integration

Reference: `ui/**`, `audio/**`, `input/**` (the paths xemu actually uses on
Windows/Linux/macOS).

- [ ] Port the display/present, audio-device and input-routing semantics into
  the Avalonia/CLI seams; no QEMU UI framework is embedded.
- [ ] Differential: `tools/frontend-smoke` (steps/frames/pixels/save-load), the
  live window and the pad.
- [ ] Acceptance: the product surface is unchanged or better; settings,
  renderer choice and save states still work.

**Price:** 4-6 sessions. **Bar:** correctness.

#### XFP13 - Snapshot/migration parity

Reference: `migration/**` (the device state field sets).

- [ ] Map the ported devices' state to the `.xgs` schema (or port the field
  sets); version the snapshot and keep old files loadable or document the break.
- [ ] Differential: round trip at 1B and at a title anchor; a resumed run
  reproduces the same next-step SHA.
- [ ] Acceptance: save/load across the ported machine.

**Price:** 3-5 sessions. **Bar:** correctness.

#### XFP14 - Parity close

- [ ] Ledger: 100% `verified` or `out-of-scope` with reasons; coverage report
  committed.
- [ ] **P4/P5:** seven oracle titles playable (own render, input, 10 minutes,
  save/load, xemu frame policy, audible), ledger complete.
- [ ] Full corpus sweep, four accurate goldens, ktest, `scripts/package.ps1` +
  `scripts/package-verify.ps1` (`PACKAGE PASS`).
- [ ] Docs: README, `docs/compatibility.md`, release notes,
  `docs/open-source.md` publication checklist status.

**Price:** 2-4 sessions. **Bar:** correctness.

### 5. Architecture: keep vs port

| Keep (product shell and gates) | Port (the emulated machine) |
|---|---|
| `XGrid.sln`, test projects, golden/ktest/package gates | NV2A PGRAPH + GL renderer |
| `XGrid.Cli`, `XGrid.Host`, Avalonia frontend, settings, save UI | APU front end + AC'97 codec |
| `IPgraphBackend`, `IGpuMemory`, `AddressSpace`, `IMmioDevice`, `IAudioBackend` seams | IDE/ATAPI/block, SMBus/SMC/EEPROM, nvnet, USB/XID |
| the software rasterizer as the exact oracle | machine/chipset assembly |
| decode cache + raw JIT until XFP11 replaces them | CPU/memory core (XFP11) |
| ISO/CHD container adapters (XISOSharp, CHDSharp) | host display/audio/input backends, migration |

A ported module keeps the seam it replaces; no product code is rewritten to fit
the reference's object model. That is what keeps the gates runnable during the
port. **The P1 sprint deliberately uses the existing pieces** - the
`--gpu gl --gpu-replace` backend, the raw JIT, NuGet host packages for audio -
instead of waiting for their ported replacements; the port then raises fidelity
and breadth behind the same seams.

### 6. Media and reporting

- `H:\Tests\XBOXReady` (seven optimized ISOs) is the acceptance set; xemu
  0.8.139 on the same ISO + firmware/EEPROM is the oracle. `H:\Tests\XBOXTest`
  (37 Redump ISOs) is XGrid-only compatibility media - never gates.
- `E:\Microsoft Xbox` (945 retail CHDs, 1.44 TB) is the **broad compatibility
  library**: use it to choose and triage titles and to exercise the CHD path.
  It is never an oracle row (no xemu capture is taken there) and never a merge
  gate; the seven optimized ISOs remain the P1 acceptance set.
- `data/test-samples/` (triangle, ktest, dashboard, nxdk/xgu corpus) stays gate
  currency.
- Every task reports: the ledger row(s) moved, the differential result, the
  four accurate 1B anchors, the ktest log, and the retail row it moved (or
  "no row moved", with the reason). 1B walls are continuity anchors; they are
  not the objective until P3.
- The size estimate is deliberately explicit: ~350-450k lines of in-scope C to
  translate, **70-120 sessions** for the full ledger, with **P2 expected at
  XFP3-XFP5 and P3 at XFP5-XFP7** (roughly 15-40 sessions). A task that cannot
  state which ledger row it advances is not started.

### 7. Standing gates (unchanged)

- Four accurate 1B goldens byte-identical (menu sw `4EF1FBF6` / gl `B6116DAE`,
  triangle sw `5B50373D` / gl `79142DE4` as of TM7) or the measured re-baseline.
- ktest park `EIP=0x0004E03A` at **792,800,535** steps with the 40,797-byte
  suite log byte-identical (`CA5D3867`); `ktest_diff.py` SMOKE PASS 7/7.
- `dotnet build XGrid.sln -c Release` 0 warnings, `dotnet test` green,
  `dotnet format --verify-no-changes` clean.
- `scripts/package.ps1` + `scripts/package-verify.ps1` on any release-affecting
  change; notices and sources updated with every port.
- `pwsh -File scripts/port-manifest.ps1 -Check` green once XFP0 lands.
- No new external dependency without the NuGet rule (precedent:
  `Xunit.SkippableFact` rejected).

## E2. D1 — C6 draw-flush half (DONE 2026-10-23; §241)

**Why this is the active task:** the device stage is 1.31 s of the 6.1 s
triangle wall; 766 ms of that is draw flushes, and 413-435 ms is the interpreted
vertex program. It is the largest measured unowned item. The clear half already
landed (§240).

**The measured facts (do not re-derive):**

- 2,706 draw flushes at 1B; 63,524 vertices; 27.7 VS instructions per vertex.
- The VS interpreter costs ~245 ns per instruction; allocation removal was
  measured null (§240).
- The row's own transform program is 28 instructions; a per-program compiled
  evaluator is a bounded item.
- The draw-flush split: VS interpreter 413-435 ms, GL submission 173-181 ms,
  setup 61-66 ms, attribute fetch 34-36 ms, epilogue ~70 ms.

**Steps:**

- [x] **D1.1 Price the draw-flush half.** Run a 100M-step ablation that removes
  the VS interpreter from the flush (or time it with the existing stage probe).
  Record the ceiling in `docs/status.json` (D1.price). Acceptance: the ceiling
  is ≥10% wall or ≥20% engine. If not, stop and record a new negative.
- [x] **D1.2 Freeze the baseline.** Capture the triangle and menu 1B anchors, the
  draw/flip counters, and a draw-flush trace (the 2,706 flushes, vertex counts,
  program ids). This is the differential baseline.
- [x] **D1.3 Write the differential first.** A test that runs the same draw
  stream through the interpreter and the new path and compares every vertex
  output bit-for-bit; a frame golden (`A1740DE0`, `B6116DAE`, `7A860ED7`); a
  corpus row (`xgu vp_lighting` 3B, `xgu bump_cubemap_reflect` 3.6B).
- [x] **D1.4 Implement the smallest compiled/cached form.** One host delegate
  per token or a hoisted operand predecode; no float operation change; keep the
  interpreter as the fallback. Do not rewrite the program loader.
- [x] **D1.5 Validate.** Build + tests + format; triangle and menu 1B
  SHAs/counters exact; both accurate goldens; ktest log byte-identical;
  `BackendUnsupported=0`; both GL frames.
- [x] **D1.6 Measure.** At least 3 interleaved same-window pairs; report
  draw-flush ms, ns/vertex, wall, and the 3/3 sign. A single run is not a claim.
- [x] **D1.7 Close.** Add the done item to `docs/status.json` with the evidence
  and commit hashes; run `pwsh -File scripts/status.ps1 -Check -Sync`; commit
  code + tests + status + graph in one commit. Then promote D2.

**Result (§241):** the 100M-step leg cannot price this stage (the triangle has
one draw there), so the stage probe at 1B is the measurement: the interpreted
transform program is **422-506 ms of the triangle's 5.8-6.1 s** (7.3-8.2%) and
**7,134-7,336 ms of the menu's 20.9-21.9 s** (33.8-34.9%). `VshProgramPlan`
(predecoded instructions, plan-owned scratch, the interpreter's own float
helpers) takes it to **202 ms / 2,600 ms**; the menu wall A/B is **-24.8% /
-30.4% / -32.8% (3/3)** with `outerMs` **-25% / -31% / -34%**, the triangle wall
is -4.02% median with 5/10 signs (noise floor; `nonJitMs` -11/-23/-12%) while its
stage time halves. Every 1B SHA/counter, both accurate goldens and the ktest log
are byte-identical. Numbers and caveats: §241.

**Stop conditions:** the ceiling is <10% wall or <20% engine; the differential
cannot be made bit-exact; two implementation attempts fail the acceptance test.
Record the negative and stop.

## E3. D2 — C7 engine-shape decision (DONE 2026-10-23; §242)

**The decision inputs (already measured):**

- Encoder: `Assemble` 390 ns vs a single-pass 120 ns per instruction = ~7%
  triangle / ~3% menu (§234e). Below the bar.
- Code volume: 11 instructions per member removed = -3% engine, 0% wall; the
  runner is latency-bound (§239). Closed.
- Boundary population: ~350 ms = 3-5% of the wall (§235). Below the bar.
- Device stage: 766 ms of draw flushes (D1) and 816 ms total after the clear
  fill (§240).

**Steps:**

- [x] **D2.1 Re-read the prices.** Re-measured at 1B `gl+replace` on the post-D1
  build: triangle 5.839 s (`runnerMs` 3,425 = 58.7%, 6.26 ns/member, formation+
  chain 991, `nonJitMs` 1,400), menu 16.448 s (`runnerMs` 12,801 = 77.8%,
  14.77 ns/member, formation+chain 1,716, `nonJitMs` 1,882); both fast anchors
  exact. Prices confirmed current: encoder 415.4 vs 122.2 ns/instruction (3.4x,
  re-spiked) = 8.0% triangle / 4.9% menu; trace boundary population
  352.6 / 716.2 ms = 6.04% / 4.35%; code volume 0% wall; device stage ~596 ms
  triangle (10.2%) and >=2,600 ms menu (>=15.8%).
- [x] **D2.2 Write the decision table.** (a)/(b)/(c) with measured ceiling, cost,
  risk and kill criterion — §242 §3.
- [x] **D2.3 Decide.** (a) keep the current engine and take the device/draw-flush
  path; C's engine-shape program is closed on its own kill criterion. D3 is
  promoted; D4 stays conditional on a per-member probe (>=20% engine, two
  iterations). Rationale — §242 §4.
- [x] **D2.4 Record.** §242; `docs/status.json` (done + queue + position);
  generated block and graph.

**Result (§242):** the decision is **option (a)**. Re-measured at 1B: triangle
5.839 s (runner 58.7%, 6.26 ns/member, formation+chain 991 ms) and menu 16.448 s
(runner 77.8%, 14.77 ns/member, formation+chain 1,716 ms), both anchors exact,
gap 4.87x / 12.1x. The only ceiling at or above the bar is the remaining device
stage (~596 ms = 10.2% triangle; the menu's transform stage alone >=2,600 ms =
>=15.8%, >=22% with the measured ~1.0 s GL submission). Every engine-shape
option is measured below it: encoder 8.0% / 4.9%, trace <=6.0% / <=4.4%, code
volume 0% wall, the C4 memory probe null, the register-liveness edge inexact —
so a native core has no price and cannot start under rule 2. The menu-only mass
is 88% raw runner, which is why D3 is promoted and D4 keeps the per-member probe
as its reopen condition. No code changed; suite 1874 green (1,864 in the
solution-level run plus `XGrid.Graphics.Tests`' 10), build and format clean.

## E4. D3 — menu-only wall (DONE 2026-10-23 as a NEGATIVE; §243)

- [x] **D3.1 Decompose.** Menu 18.8-20.0 s vs triangle 5.61-5.74 s: about 13 s
  is menu-only. Split it into CPU engine, device, GL, raster and boot; use the
  stage timers, not a guess.
- [x] **D3.2 Rank and price.** Take the top menu-only item; write the ceiling.
- [x] **D3.3 Land or negative.** Only with the gate; otherwise record the
  negative.

**Result (§243):** the decomposition is measured with temporary stage probes
(removed before the commit): the menu's 1B `gl+replace` wall is **15.0-15.2 s
in this window** (recorded band 15.6-17.5) = **device 6.57 s (41.6%)** + engine
non-device 9.4 s (59%), raster **0** (replace mode), GL readback **0**, and the
shared boot 1.6-1.9 s (10-12%) inside both. The menu-only mass (+9.3 s) is
**57% device** (+5.95 s: flushes 5.58, other writes 0.37, clears 0.06) and 43%
engine (+4.0 s). Inside the menu's 6.15 s draw flush: transform plan 2.2-2.5 s,
attribute fetch 0.9-1.2 s, vertex epilogue ~0.3 s, per-draw setup 0.26 s, and
the fill path 2.7-3.1 s - 10,727,820 fills for 2,733,736 drawn triangles, i.e.
**8.0M of 10.7M calls produce no pixels**. The top item is the draw flush; its
fill-path piece was priced by **building it**: reject-before-setup, per-draw
scratch reuse, a read-slot fetch mask and operand locals in the plan measure
**-8.03 / -9.90 / -7.87% wall (3/3)** and -7.6% `executeMs` (-15.8%
`runnerMs`) with every 1B anchor exact - **below the 10% wall and 20% engine
bars**, so it was reverted and recorded as the negative `D3`. Adding the
per-draw `PgraphDrawInfo` cache (~0.3-0.4 s, 2.5%) prices the combined slice at
~-10.5%, which is D5's first step. Build clean, suite green, format clean; no
code changed.

## E5. D5 — remaining device/MMIO (DONE 2026-10-23; §244)

- [x] **D5.1 Price the hot NV2A method dispatch and the menu's device mix.** The
  draw-flush half is D1; this is the rest. The price is §243's: its measured
  -8.0% (3/3) plus the per-draw `PgraphDrawInfo` cache = ~-10.5% as a combined
  slice, so the combined slice was built and measured rather than re-derived.
- [x] **D5.2 Land or negative.** ≥10% menu wall; exact counters.

**Result (§244):** the combined draw-flush slice landed - reject-before-setup,
per-draw vertex scratch, the plan's read-slot fetch mask, operand locals in
`VshProgramPlan.Execute`, and a per-draw-command backend record rebuilt through
`PgraphDrawInfo.WithTriangle`. Menu `gl+replace` 1B interleaved pairs:
**-10.33 / -17.20 / -10.29%** on the acceptance triple (median -10.33%, 3/3
above the bar); six pairs read a -10.31% median, 6/6 negative. Triangle
`gl+replace` -0.70 / -2.27 / -2.02% (no regression, no claim - it is
pacing-bound). Every fast anchor byte-identical (menu gl `B06BA6E5`/`F5D61C94`,
triangle gl `4F1FCBC0`/`7DAB63EB`, menu sw `34973510`, triangle sw `B45254CA`),
all four accurate goldens byte-identical to the stashed base build
(`4EF1FBF6` / `6BE6325F` / `A1740DE0` / `3FC4B419`), ktest log `CA5D3867`
(40,797 B) byte-identical at 792,800,535 steps and SMOKE PASS 7/7, suite
1874 -> 1878. No snapshot change.

## E5b. D6/D7/D8 - the M-D continuation (queued 2026-10-23)

**Why this section exists.** D5 closed the item D2 promoted, and the measured
budget in E1 says the current shape cannot reach the band: the triangle's native
runner alone is 3.43 s (2.9x the whole 1.20 s target), its boot alone is 1.93 s
(1.6x), and the menu's runner is 12.80 s (9.4x the 1.36 s target). Every engine
transformation measured so far is below the 10% wall / 20% engine bar, so the
next move is not another slice. It is (a) the two build items that are priced
today, and (b) a new measurement that lets the engine decision be reopened with
a number instead of a guess. D2 left exactly that hook open (D4 reopens on a
>=20% engine probe). Order: **D8 -> D6 -> D7**, WIP = 1.

### D8 - Engine-shape decision v2 (measurement + written decision)

**Result (2026-10-24, negative) - decisions §247.** The four tables are measured
on both gate titles at 100M and 1B with a permanent, free-when-off
instrumentation (`--jit-timing` + `JitAttribution`): runner by block length and
member class, the edge census with the static host instruction count of every
emitted region, the boot split (formation/`Assemble`/non-JIT/device/lookup) and
the steady-state delta. Headline: **8.47 ns per served member (triangle) /
17.51 ns (menu), uniform across load/store/ALU/branch classes (7.4-9.9 ns), 78
emitted host instructions per member, 4.4-5.3 members per chain edge, and an
in-frame chain edge saves 33.5/35.2 ns against a managed entry**. Option (a) was
**priced by building it** (self-loop fast edge, 76.8%/67.8% of edges are
self-loops): 16% slower runner on the one completing leg and two native crashes,
reverted -> ceiling <= 0%. (b) 3-5% (N3), (c) null (N1/N9), (d) is the
compatibility path (the engine already beats the reference's TCG on CPU-bound
code; the title's mass is device/formation/non-JIT). **No engine-shape option
clears >= 10% wall / >= 20% engine, so D8 selects option (e): the target is
restated** - ns per served member outside the device stage plus wall per
presented frame on the corpus, with the 1B band kept only as the real-time
reference. Reopen condition: one candidate at >= 20% engine on this attribution.

**Price:** a measurement task; it claims no wall. It exists because the priced
levers cannot close E1's arithmetic, so the next spend must be decided by data.
The working target is **steady-state <=2 ns per served member** (the 1.20 s band
implies ~1.1 ns/step on the post-boot 900M steps, device and non-JIT included),
and the boot must fall from 1.93 s to <=0.20 s for the triangle target to exist
at all.

**Measure**, on gate media, fast mode, `--jit-timing`, at 100M **and** 1B, for
both titles:

1. **Runner attribution** - ns per served member split by block length
   (1-4 / 5-8 / 9+ members), by entry vs chained, and by member class (memory
   load/store, ALU, branch). §242/§243 say ~55% of the menu mix is
   MovLoad/MovStore and ~14% DecR; the probe must attribute the 6.26 / 14.77 ns
   instead of assuming it.
2. **Edge census** - chained edges vs entries, members per edge, and the static
   host instruction count per edge (the emitter knows it; add a counter to the
   emission path, not a Stopwatch per member).
3. **Boot split (0-100M)** - formation (`Assemble` vs IL), non-JIT, runner,
   device, and the 55,215 heads below the build threshold with their per-head
   formation cost by source (RAM / ROM / immutable image).
4. **Steady state (100M-1B delta)** - the same cells, so the boot does not hide
   engine work.

**Acceptance:** the four tables for both titles, plus a written decision that
selects exactly one of:
 (a) **self-loop / edge elimination** - the hot 4-member loop pays ~60 host
     instructions per edge at ~6 ns/member (§239); price a self-loop fast path
     and its poll-contract cost;
 (b) **short-block trace or fusion** - N8 (-82% invocations, +40% wall) and N3
     (3-5%) are binding; a new design must name which of their costs it removes;
 (c) **memory-probe / data-path reshape** - N1 (host pointers) and N9 (1024-slot
     cache) are binding; a new design must not be either of them;
 (d) **port the reference's engine/device shape** - xemu/QEMU are GPL-2.0, so
     porting is legal and now the default when the reference implements the
     module (`docs/porting-policy.md`). Candidates, each priced separately:
     QEMU/xemu `tcg/` block translation for the CPU, and xemu's
     `hw/xbox/nv2a/pgraph/` draw path (the device stage is 6.57 s of the menu
     wall) or its GL renderer. A port is full managed C#, keeps the notices, and
     needs a bounded prototype before the full module;
 (e) **restate the target** - if no option clears the bar, write that down and
     replace the 1B band with the supported-corpus target (steady-state ns per
     presented frame).
The selected option carries a ceiling estimate and a stage plan; >=20% engine
(or >=10% wall) is the build bar. Option (d) is a port, not a performance slice:
its price is the module boundary + the prototype's measured cost, and it may be
chosen for correctness/completeness even when its wall share is below the bar.

**DoD:** temporary probes removed before the commit unless they cost nothing and
stay behind `--jit-timing`; no guest-visible change; both 1B fast anchors and
all four accurate goldens exact; the decision recorded in `docs/status.json` and
this plan.

### D6 - Device remainder: compiled transform execution and the fill path

**Result (2026-10-24, negative) - decisions §248.** The compiled transform
evaluator was **priced by building it**: one `DynamicMethod` per plan emitted
from the plan's own decoded instruction list, flat four-component files, the
reference's zero rule/dots/masks/oFog prefix/ARL-relative constants, with the
interpreter kept as the fallback. It is **differentially exact** (the
`VshProgramPlanTests` gate now compares `VshProgram`, the kernel and the
interpreter on every case including the 400-program fuzz) and **3.2x slower**:
**1,028 ns per vertex** against the interpreter's 330 ns, so the menu 1B
`gl+replace` wall goes 19.237 -> 31.242 s (+62.4%) and 19.585 -> 29.548/30.938 s
in same-window pairs, device writes 7.39 -> 18.60 s, `runnerMs` 15,395 ->
25,287, GL submission unchanged; the triangle leg is 7.084 -> 6.778 s (no
regression - the transform is 3% there). Every anchor stayed exact throughout.
Cause: the per-instruction unrolled emission costs ~150-250 host instructions
per VS instruction (bounds-checked flat-array loads/stores plus the
per-component zero-rule branches) = 7-15 KB of straight-line code per plan, so
the body cannot stay uop-cache resident (IPC ~0.6 against the interpreter's
~2.5). The menu transform is 3.72 s = 19.3% of the wall, so the bar needs an
evaluator **>= 2.4x faster than the interpreter (<= 135 ns/vertex)**; this
design's ceiling is **<= 0%**. Reverted.

**Reopen:** a different emission strategy priced by building it and measuring
<= 135 ns/vertex - the only one with a plausible ceiling is a 4-component
vector emission (`Vector128` per register: 3 vector loads, 2-3 vector ALU ops
and one blended store per VS instruction, ~12 host instructions instead of
~200), with the dots and the zero rule scalar because the reference's
accumulation order is not associative.

**Price:** the compiled transform evaluator was priced at **9.5-10.7% menu
wall** (§243, flagged high risk); that estimate is superseded by §248's
measurement (a negative). The fill-path hoist already landed in D5.
Post-D1 the menu transform is 2.2-3.7 s of the wall (measured 3.72 s this
session); the triangle transform is ~230 ms, so this is a menu item.

**Acceptance:** >=10% menu wall on 3/3 interleaved pairs (or >=20% `executeMs`
when the wall is host-noise bound), `--jit-timing` on every leg, exact anchors,
`BackendUnsupported=0 composedFrames=0`, both 1B GL frames byte-identical, and
the 400-program fuzz plus the per-vertex bit-identity against `VshProgramPlan`.
**Fallback:** keep `VshProgramPlan` as the executor; a compiled form is a
replacement, never a second semantic source.

### D7 - The boot: the first 100M steps are 33% of the triangle wall

**Result (2026-10-24, negative) - decisions §249.** The price measurement was
run as the plan asked: the boot was split by stage and the **unbuilt-head
population was priced by source class** with new permanent instrumentation
(`--jit-timing`; `JitBlockCache` records each head's fetch source at first
sight, `FormatJitAttribution` prints one `pending` line with the run's own
mean formation cost applied to it, pinned by `JitAttributionTests`). Headline:
the boot is **1.87-1.91 s triangle (31% of a 6.1 s wall) / 1.62 s menu (11%)**
= runner 41-45% + formation 25% (Iced `Assemble` 18-21%) + non-JIT 26% +
lookup/loop 4%. The pending heads are **99.6% RAM code** (34,046 of 34,197 at
100M; 54,854 of 55,005 at 1B; the 151 image heads are the MCPX ROM, whose
blocks already form), and **building them all costs 19.4 s (triangle) / 5.1 s
(menu)** to remove a non-JIT cell of 1.26 s / 1.71 s - **15x and 3x negative**.
The non-JIT cell itself splits 34% `Step` (3.2 us each, 1.1 us of it the
byte-at-a-time fetch) / 66% spec-run body (270-358 ns/member: 165-270 the
interpreter handler, 88-106 the cursor loop); `Form` is 0.14 us a call and not
the cost. **The largest unretried lever is the `Step` fast fetch at 2.5% of the
wall** (the JIT's own `TryFillDecodeEntry` fetch), then the spec-run loop at
1.8% and the image plan cache at 0.9-1.4%; the plan's combined shape (encoder
+ image plan cache) is 9.4% + 1.4% on paper, 87% of it N4, whose own reopen bar
(>10% from a new formation profile) this profile does not meet. No 10% wall, so
the target goes to D8's option (e) and the device half to G1-G3. The three
sized candidates are recorded for a future engine task.

**Price:** boot is **1.93 s triangle / 1.62 s menu** (D3.1), and 1.20 s cannot be
met with a 1.93 s boot. The known levers are the encoder (8.0% triangle / 4.9%
menu, N4) and trace formation (3-5%, N3); both are below the bar alone, so this
task measures first: split the boot by stage (item 3 of D8) and price the
unbuilt-head population by source class. A combined lever (encoder + a
prebuilt/plan cache for immutable image code) is the only shape that can clear
the bar on paper.

**Acceptance:** >=10% triangle wall on 3/3 interleaved pairs from whatever
combination the split justifies, exact anchors, byte-identical ktest log. If the
measured ceiling stays below 10%, record the negative and hand the target to
D8's option (d).

## E5c. G1-G3 - the optional wholesale NV2A/PGRAPH port (queued 2026-10-23, after D8)

**What it is.** A full port of xemu 0.8.139's NV2A path into managed C#: the
PGRAPH core (method dispatch, state, formats) followed by the GL renderer
(submission, surface/texture cache, shader pipeline). It is legal (GPL-2.0, same
family) and allowed by `docs/porting-policy.md`; the reason it is *queued* and
not *started* is measured value.

**Why it is not the performance fix.** The device stage is **41.6% of the menu
wall but 10.9% of the triangle's** (D3.1). An xemu-class device path would take
the menu from 15.6-17.5 s to roughly 11-12 s and the triangle from 5.72-6.15 s
to roughly 5.5 s - because the runners (12.8 s menu / 3.43 s triangle) and the
boot (1.93 s triangle) are untouched. The band still needs D8's engine decision.
What the port *does* buy is compatibility breadth: the 73 `recorded` NV097
methods (61 in vertex submission 30, raster modes/queries 24, context DMA 7),
DXT/paletted/mipmap/multistage formats, and exact surface/texture cache
semantics.

**Scope and price** (counted from `References2/xemu-0.8.139/hw/xbox/nv2a`,
34,480 C LOC / 92 files):

| part | C LOC | XGrid today | action |
|---|---:|---|---|
| top-level `nv2a.c`, `pfifo.c`, `pmc/pcrtc/prmcio/prmvio/pfb/pbus/ptimer`, `nv2a_regs.h` | ~4.4k | `Nv2aMmio.cs` has the pusher, blocks, PCRTC/PRMCIO, IRQ | adapt the missing register/IRQ semantics |
| PGRAPH core `pgraph.c`, `texture.c`, `s3tc.c`, `vertex.c`, `swizzle.c`, headers | ~4.7k | method switch + `PshCombiner` + `VshProgram(Plan)` + fixed-function | **G1** |
| GL renderer `pgraph/gl/` | 6,348 | `XGrid.GpuGl` 5.2k (our own backend) | **G2** |
| GLSL generators `pgraph/glsl/` | 4,895 | already transcribed into `PshCombiner`/`VshProgram`/`FixedFunctionVertex` | port only the parts G2 needs; do not duplicate semantics |
| Vulkan `pgraph/vk/` | 12,596 | none | **out of scope** |

**Total in scope: ~16k C LOC → ~20k C#, 13-20 sessions** including seam
adaptation and the consolidated re-baseline. Calibration: the DSP port was
~4.5k LOC in ~3 sessions because it had 929 differential vectors; this path has
frames and state only.

**Invariants (all three tasks):**
- The **software rasterizer stays**. xemu has no software path (`gl/`, `vk/`,
  `null/` only), and the software frames are the accurate-mode gate currency, so
  it remains the oracle and the default renderer.
- XGrid's seams stay: `IMmioDevice`, `IGpuMemory`, `IPgraphBackend`; the port
  adapts to them, it does not bring QEMU's `MemoryRegion`/thread model.
- Every ported file carries SPDX + `Ported from` + a `THIRD_PARTY_NOTICES.md`
  row (`scripts/package-verify.ps1` enforces it).
- Software 1B anchors, the four accurate goldens and the ktest log stay
  byte-identical; only GL frames may be re-baselined, and only with the policy
  diff (`violations=0`, `maxDelta<=1` where the software frame is the oracle).

### G1 - PGRAPH core: method dispatch, state and formats

**Price:** ~4.7k C LOC, 4-6 sessions. It is the compatibility half: it closes
the recorded method groups and the texture/vertex format gaps. It does not
change the draw/fill cost (that is G2).

**Acceptance:** the 73 `recorded` methods are either handled or explicitly
declined with a measured reason and a corpus/histogram citation; DXT, paletted,
mipmapped and multistage vertex/texture formats decode; the software rasterizer
still reproduces the 1B accurate goldens and the ktest log is byte-identical;
notices rows for every ported file. **Fallback:** keep the current switch for
any method whose port would change a gate frame; a method that no row exercises
is not worth a regression.

- [x] **Price / scope (2026-10-24).** The price is the module boundary and the
  acceptance, not a wall gate: G1 is the compatibility half and does not move
  the draw/fill cost (G2). The slice landed as a bounded audit + format port.
- [x] **Audit the 73 recorded methods.** `--pgraph` probes on the triangle 300M
  and the dashboard 1B windows; `docs/research-notes/nv097-coverage.md` "G1
  audit" records every entry with its measured writes and a decision (1
  handled, 72 declined; `SET_TEXTURE_PALETTE` now has a sink). The parameter
  table is `usage-histogram.md` "Recorded-method parameters".
- [x] **Port the texture formats.** `S3tcDecoder.cs` (MIT) + `TextureFormats.cs`
  (LGPL-2+): the 66-entry format table, DXT1/3/5 blocks, the paletted I8 lane,
  Y8/AY8/A8/A8Y8/Y16/R6G5B5, YUY2/UYVY and the mip-chain layout. The software
  sampler takes the new lanes; the compiled fills keep their existing fast
  formats. Differential vectors: `tools/texture-oracle` +
  `tests/XGrid.Nv2a.Tests/TestData/s3tc-vectors.txt` (16 blocks).
- [x] **Gates.** Fast 1B `gl+replace` triangle `4F1FCBC0`/`7DAB63EB` and menu
  `B06BA6E5`/`F5D61C94`; fast 1B `sw` triangle `B45254CA`/`7DAB63EB`
  (57,663,082 px) and menu `34973510`/`F5D61C94` (253,998,498 px); accurate
  goldens triangle `A1740DE0`/`7A860ED7` and menu `4EF1FBF6`/`B6116DAE`
  (`violations=0`); ktest 792,800,535 steps with the 40,797 B `CA5D3867` log
  byte-identical and `ktest_diff.py` SMOKE PASS 7/7; suite 1965 green.
- [x] **Provenance.** Notices + sources rows for both new files and the oracle;
  snapshot **v10** (the palette state).
- [ ] **Deferred to G2/G3:** the window-clip 8-rect per-fragment test (a GL
  `clipRegion[8]` port), the context-DMA map, the immediate-mode vertex path,
  and per-pixel mip LOD selection. None is exercised by the gate media; the
  audit is the measured reason.

### G2 - GL renderer: submission, surface/texture cache and shader pipeline

**Price:** ~11.2k C LOC (gl 6.3k + the glsl pieces G2 needs), 5-8 sessions. This
is the half that could move the menu device cost (6.57 s pre-D5) and it is where
the §205 batching, §219a/§220c sampler parity, §220d composition and §234
display-source rules live - all of which must survive.

**Acceptance:** the 22-row matrix re-verified or re-baselined with the policy
diff; `BackendUnsupported=0 fatal=0` and the expected `composedFrames`;
`--gpu gl --gpu-replace` still presents the software anchor where the row says
so; batching retained (or a measured reason it is slower); software anchors and
ktest log byte-identical. **Guard:** any row that regresses more than the policy
allows blocks the merge, not the row.

- [x] **Price / scope (2026-10-24) - measured, and it does not clear the gate
  (negative, decisions §251).** The device stage was split by site on the
  post-D5 build (temporary probes, 1-in-16 flushes): triangle 0.576-0.594 s
  (9.9-10.1% of 5.79-5.87 s) / menu 5.20-5.26 s (35.5-35.6% of 14.6-14.8 s),
  with the CPU vertex stage (transform + epilogue) at **0.233-0.246 s triangle
  (4.0-4.2% wall, 5.0-5.3% engine) / 2.63-2.64 s menu (17.7-18.1% wall,
  20.3% engine)**. A GL renderer that runs the vertex program on the GPU
  removes only that; the clears, attribute fetch, fill predicates, primitive
  assembly and submission stay. The reference design submits per draw command
  from CPU-built vertex buffers and has no instancing, and the repo's own
  proxy for giving up §205 batching (`XGRID_GL_NO_BATCH=1`) measured
  **+7.84 s menu (+52.9%) / +0.456 s triangle (+7.9%)**. The triangle cannot
  reach either bar even with a free submission (it would need 0.58 s, 2.4x),
  and the menu's gross saving is 3x smaller than the measured submission
  penalty. **Reopen** only with (a) a bounded GPU vertex-program slice that
  keeps the instanced per-draw-command submission and removes >=10% of the
  triangle wall (>=0.58 s) with the anchors and both accurate goldens exact, or
  (b) an operator re-scope of G2 as a compatibility task (acceptance = the
  22-row matrix, not the 10% wall bar), with G3 as the vehicle. Do not re-open
  on the "6.57 s menu device" figure - it is the whole device stage and ~80% of
  it is not renderer work.
- [ ] **Deferred to G3:** nothing from G2 (the port is not owned); G3's
  consolidated re-baseline still runs on the G1 state.


### G3 - Consolidated gate re-baseline and differential harness

**Result (2026-10-24, done) - decisions §252.** The whole acceptance bundle ran
in one session on one Release build (no mixed engine). Every gate is exact: the
four fast 1B anchors (triangle gl `4F1FCBC0`/`7DAB63EB`, menu gl
`B06BA6E5`/`F5D61C94`, triangle sw `B45254CA`/`7DAB63EB` 57,663,082 px, menu sw
`34973510`/`F5D61C94` 253,998,498 px), the four accurate goldens (menu
`4EF1FBF6`/`B6116DAE` `diff=2602 violations=0 maxDelta=1`, triangle
`A1740DE0`/`7A860ED7` `diff=27849 violations=0 maxDelta=1`, accurate RAM/CPU
`6AB39AB0`/`3D2740B8` and `812EA159`/`2F632483`), all 22 corpus rows PASS, the
ktest smoke (park `4E03A` at 792,800,535 steps, 40,797 B `CA5D3867` log
byte-identical, 7/7) and the package gate (**PACKAGE PASS: 8 archives, version
0.1.0, 4 RIDs, licenses complete**, 19 ported files). Six documented values were
stale and are re-baselined with the measurement that moved each (cubemap 3B GL
frame `5174B1EB` -> `673477F7` now 1 px from software; cubemap 4.4B colours
18,980 -> 19,042 and 3.6B pair 1 px/maxDelta 4; cubemap 3B software colours
13,925; alpha_blending 4.6B mean (22.7, 18.7, 230.4) within 2.8/255; dashboard
2B 16.94 msps with the same 217,622 draws / `535C48FA` frame; ktest BTFF RAM).
Nothing regressed: the G1 format port moved no gate and G2 landed nothing, so
this is the milestone's single re-baseline. Suite 1965, build 0 warnings,
format clean, no score change (verification, not landed capability).

**Price:** 2-3 sessions. G1/G2 each keep the software anchors exact, so this is
the one place where the GL frames, the 22 corpus rows and the documented anchors
may move together; doing it once avoids per-task re-baseline churn.

**Acceptance:** both 1B fast legs, all four accurate goldens, the 22 corpus
rows, the ktest smoke and `scripts/package-verify.ps1` (PACKAGE PASS) verified
in one commit; the new anchors recorded in `docs/status.json`, `docs/compatibility.md`
and the task evidence; no mixed engine and no stale GLSL goldens.

- [x] **Price / scope.** G3's price is a 2-3 session verification budget, not a
  wall gate: it implements nothing, so the >=10% wall / >=20% engine stage rule
  does not apply (the same shape as G1's module boundary). Measured outcome: one
  session.
- [x] **One build, no mixed engine.** Full solution Release build (0 warnings),
  one test run (**1965** green), then every leg from those binaries; the GpuGl
  suite (104 green) carries the GLSL string goldens and the 99-program live
  compile gate, so no stale GLSL golden survives.
- [x] **Both 1B fast legs (all four anchors).** triangle/menu, `gl+replace` and
  `sw`, exact RAM/CPU, pixels and counters (table in §252).
- [x] **All four accurate goldens.** menu sw/gl and triangle sw/gl, policy
  `violations=0 maxDelta=1`, counters identical, `BackendUnsupported=0
  composedFrames=0`; accurate-mode RAM/CPU exact too.
- [x] **The 22 corpus rows.** Every documented anchor reproduced except six
  stale values, each re-baselined with its measurement (§252's list): the
  cubemap 3B GL frame, the cubemap 4.4B colour count/mean and 3.6B pair, the
  cubemap 3B colour count, the alpha_blending 4.6B phase mean, the dashboard 2B
  throughput and the ktest BTFF RAM rows.
- [x] **ktest smoke.** Park `4E03A` at 792,800,535 steps, 40,797 B suite log
  `CA5D3867` byte-identical, `ktest_diff.py` SMOKE PASS 7/7 (071/319/344 fail on
  both, 224/231 XGrid-ahead - the §120 deltas). Driven with the script's own
  command on the prepared media: the sibling `CSharp_XISOSharp` and `xemu.exe`
  are absent here, so the diff reads the stored §120 oracle log.
- [x] **Package gate.** `scripts/package.ps1` + `scripts/package-verify.ps1`:
  PACKAGE PASS, 8 archives, version 0.1.0, 4 RIDs, licenses complete (19 ported
  files), win-x64 CLI smoke `XGrid 0.1.0`.
- [x] **Anchors recorded.** `docs/status.json` (G3 evidence), `docs/compatibility.md`
  (matrix status + the six moved rows + the refreshed measured-rows table),
  `AGENTS.md` session note and §252; no code, no snapshot change (Nv2a v10,
  audio v3); phase score unchanged at 79.9% (86.34 of 108).

## E6. P1 / R1 — parallel product and release

**P1 is a porting task, not a performance slice.** The 10% wall / 20% engine
price gate does **not** apply. The "price" is the module boundary and the test
surface; the acceptance is that the ported xemu 0.8.139 DSP executes the guest
microcode and a real voice stream produces PCM, with no gate regression.

**P1 is the xemu 0.8.139 DSP port** (operator decision 2026-10-23). The project
is GPL-2.0 and the port is full managed C#; the reference is
`References2/xemu-0.8.139/hw/xbox/mcpx/apu/dsp/`, with the provenance rules in
`docs/porting-policy.md`.

- [x] **P1.0 License + port foundation.** GPL-2.0 (`COPYING`/`LICENSE`),
  `THIRD_PARTY_NOTICES.md`, `docs/porting-policy.md`, sources inventory, and the
  xemu 0.8.139 menu oracle (`docs/research-notes/xemu-menu-oracle.md`).
- [x] **P1.1 Port the DSP state, memory map, bootstrap, 56-bit arithmetic and
  DMA.** `Dsp56300State`, `Dsp56300Core`, `Dsp56300Arithmetic`, `DspDma`; tests
  in `Dsp56300PortTests`; no gate path changed.
- [x] **P1.2 Port the instruction decoder and handlers.** `dsp_cpu.c` +
  `interp/dsp_emu.c.inc`: the opcode table, the parallel-move rules and the
  handler bodies. `Dsp56300Core.Execution` (fetch/decode, PC, DO/REP, stack,
  interrupts, effective addresses, condition codes), `.Alu`, `.Instructions`,
  `.ParallelMove` and `Dsp56300Opcodes` (187-row decode table + 256-entry ALU
  dispatch). `tools/dsp-oracle` compiles the reference interpreter from
  `References2/` and generates 929 differential vectors replayed by
  `Dsp56300DifferentialTests` (51 Audio tests green). The existing APU bridge is
  untouched until P1.3 wires the core.
- [x] **P1.3 Port the GP/EP frame glue.** `gp_ep.c`: `proc_rst_write`,
  scatter/gather scratch, FIFO windows, the GP/EP frame loop and the DMA
  wiring. `ApuDspCore` option (default stays the bridge); `McpxApuDsp` /
  `McpxApuDspPair`; APU snapshot v4. The opt-in path executes the dashboard's
  real microcode (29 GP frames, 0 undefined, 0 cycle-limit hits) and is
  guest-identical on both 1B gates; §246.
- [x] **P1.4 Add the PCM sink.** `IAudioBackend` + `WaveAudioBackend`; the EP
  output FIFO 0 is sunk and silence written to the guest ring; CLI `--audio-out`
  (implies `--apu-dsp`). The VP mixer is the remaining audible-output gap; §246.
- [x] **R1.1 Package the frontend and CLI** for Windows/Linux/macOS; license
  review (GPL-2.0 + third-party notices); release notes. `scripts/package.ps1`
  (publish → strip → license set → deterministic archive → checksums →
  manifest) and its acceptance gate `scripts/package-verify.ps1` (GPL-2.0 text,
  notices coverage of every `// Ported from` / `// Derived from` file, archive
  contents, per-RID executable magic, SHA-256, manifest, CLI `--version` smoke);
  `docs/release-licensing-review.md` (findings F1–F6); release notes in
  `docs/release-notes/v0.1.0.md`; `release.yml` and `docs/ci.md` aligned. The
  version now has one source (`Directory.Build.props`), and the CLI prints its
  assembly version.

The CPU TCG is **not** a P1 target, but it is **not excluded by licensing**: the
project is GPL-2.0, same as xemu/QEMU. It is a sized option in D8 because the raw
emitter is already bit-exact on both 1B gates and faster than xemu on the
CPU-bound ktest; the measured gap is the boot/short-block/device shape, and D8
prices a TCG-shaped port against the edge/formation options.

> **Superseded 2026-10-24:** the CPU TCG **is** now a port target (**MP1** in
> E1b). D8's attribution (78 emitted host instructions per served member,
> 8.47 ns/member, uniform across member classes) is the density gap the old plan
> never priced, and the operator re-scoped the port to the whole engine.

WIP = 1: the queue is **D8 → D6 → D7** (see E1/E5b); `docs/status.json` is the
authority.

**E6 result (2026-10-23, R1.1):** the release tree is real and verified.
Packaging: `scripts/package.ps1` publishes both applications self-contained
for `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64` (8 archives, 32.7-47.7 MB),
adds the complete GPL-2.0 set (`COPYING`, `LICENSE`,
`THIRD_PARTY_NOTICES.md`, the release notes, `README.md`, `VERSION`) and writes
`SHA256SUMS-<rid>.txt`, `SHA256SUMS.txt` and `package-manifest.json`; the recipe
reproduces all 8 archives byte-identically for the same commit. Acceptance:
`scripts/package-verify.ps1` (license text, notices coverage of every
`// Ported from`/`// Derived from` file, archive contents, per-RID executable
magic, checksum/manifest agreement, packaged CLI `--version`) fails the old
recipe with 22 problems and passes the tree. Licensing:
`docs/release-licensing-review.md` (findings F1-F6, operator actions for GPL-2
§3 source availability, store channel and signing). Notes:
`docs/release-notes/v0.1.0.md`. `release.yml` and `ci.yml` (its package job) run
the same recipe, and `docs/ci.md` documents it. **Remaining in the phase:** an OS
installer and code signing / notarization, both deferred by the review's F5/F6
(portable archives need no install-time state; signing needs operator
certificates).

## E7. Measured negatives (binding — do not retry)

| id | negative | evidence | reopen only if |
|---|---|---|---|
| N1 | C4 host-pointer translate probe | `runnerMs` 15,872 -> 16,026 menu; 3,407 -> 3,382 triangle; walls within noise | a new measurement shows the probe is the wall |
| N2 | read-only-ROM decode cache alone | `nonRamSkips` -> 0, wall/`nonJitMs` flat | a new cache question (e.g. store tracking) |
| N3 | full trace/superblock formation | ~350 ms = 3-5% of the wall | a new measurement shows a larger boundary population |
| N4 | own the encoder | 390 vs 120 ns = ~7% triangle | a new formation profile makes it >10% |
| N5 | per-member EIP/TSC/served removal | -3% engine, 0% wall | an edge-removal design |
| N6 | VshProgram allocations | 3.5M allocations removed, 413 -> 426 ms | a new allocation profile |
| N7 | B3 word pool (`uGuestWord[96]`) | superseded by the 2D-array; code deleted | never; use the 2D-array |
| N8 | block fusion | -82% invocations, +40% wall | a new design that keeps the spin skip |
| N9 | 1024-slot data translate cache | neutral | a new TLB design |
| N10 | `JitSpecSlice` below 64 | formation eats the gain | a cheaper formation path |
| N11 | k1 fold / alpha-1 skip / AggressiveInlining | neutral | a new profile |
| N12 | GPU "landed" without a live compile | §227: the shaders never compiled | never; the gate is DoD |
| N13 | full corpus after every slice | two sweeps in one day | milestone close or a shared sampler/decoder change |
| N14 | docs-only status rewrites | next-session 24 touches, AGENTS 23, decisions 22 | never; status.json + generated blocks |
| N15 | shape/bookkeeping fixes alone reach 0.5x on 007 | 30B gameplay 413.254 s / 0.100x; runner 53.5%, non-JIT 20.1%, b4 35.7%, GL 0.42%; halving every CPU stage = 0.20x; top shapes are the x87/SSE wait loop | a measured engine change (not a shape fix) reaches >= 0.5x, or the operator lowers the P1 floor |
| N16 | QEMU TLB shape alone is a 2x probe win | `QemuTlb` 0.84 ns/lookup vs the current `SoftTlb` 0.64 ns (9 exact differential tests); the current TLB is already the fast shape | only with a measurement where the TLB probe is the bottleneck (the current cost is block/translator shape) |
| D3 | menu draw-flush hoist (reject-before-setup + per-draw scratch + read-slot mask + plan operand locals) | 3 interleaved pairs **-8.03 / -9.90 / -7.87%** wall, `executeMs` -7.6% (`runnerMs` -15.8%); every 1B anchor exact; reverted | a combined slice with the per-draw draw-info cache measures >=10% wall |

## E8. Definition of done (per task type)

**Code/device:**

- [ ] `dotnet build XGrid.sln -c Release` clean (0 warnings).
- [ ] `dotnet test XGrid.sln -c Release` green.
- [ ] `dotnet format XGrid.sln --no-restore --verify-no-changes` clean.
- [ ] 1B triangle and menu SHAs/counters exact; flag-off byte-identical.
- [ ] ktest log byte-identical (when the task touches the CPU/device path).
- [ ] `docs/status.json` done item with evidence + commit hashes.

**GPU/shader:**

- [ ] The shader compiles and links on the driver (the §227 live gate), not just
  a string compare.
- [ ] The frame differential vs software (policy: violations 0, maxDelta ≤1) or
  the corpus pair.
- [ ] `BackendUnsupported=0`, `fatal=0`, the expected `composedFrames`.
- [ ] Both 1B GL frames byte-identical where the task is host-side only.

**Performance:**

- [ ] A measured ceiling before the build (100M-step ablation or stage timer).
- [ ] ≥3 interleaved same-window pairs, 3/3 sign.
- [ ] ≥10% wall or ≥20% engine; otherwise negative.
- [ ] The exact anchors and counters on both titles; no mixed engine.

**Docs/status:**

- [ ] `pwsh -File scripts/status.ps1 -Check` green.
- [ ] `-Sync` regenerated the three status blocks and the graph.
- [ ] No new duplicate status prose outside the generated block.
- [ ] The commit contains the code/test/status/graph, not a docs-only follow-up.

**Product:**

- [ ] Frontend build + manual smoke; no core/CLI/test file touched unless the
  task is core.
- [ ] Settings/save/load/input still work; a screenshot or a session log is the
  evidence.

**Release:**

- [ ] `scripts/package.ps1` produces every affected RID and the tree is clean
  (`package-manifest.json` `dirty=false`).
- [ ] `scripts/package-verify.ps1` reports `PACKAGE PASS` (license set, notices,
  archive contents, per-RID magic, checksums, CLI `--version`).
- [ ] The version's `docs/release-notes/v<version>.md` exists and is in every
  archive; the licensing review covers any new reference/package.

## E9. Session checklist

**Start:**

- **RepoFleet (remote):** the task command is static:
  `pwsh -File scripts/status.ps1 -Next` prints the active task and the exact
  close commands; `-Complete`/`-Negative` advance the queue. A parked
  conditional task is closed with `-Negative -Task <id> -Evidence ... -Reopen ...`;
  `-Next` refuses a dirty tree. Queue the same command once per task; RepoFleet
  serializes one active task per repository. The full contract is
  `docs/repofleet-worker.md`.
1. `pwsh -File scripts/host-doctor.ps1` — read the VERDICT.
2. `pwsh -File scripts/status.ps1 -Check -Show` - the active task and the queue;
   a red check stops the session.
3. Read the active task's steps and the negatives table; do not start anything
   else.
4. `dotnet build XGrid.sln -c Release --nologo --no-restore`.

**Work:**

5. Write the acceptance test/differential first.
6. Price the stage; if below the bar, record the negative and stop.
7. Implement the smallest change; keep the fallback.
8. Validate against the DoD.

**Close:**

9. Edit `docs/status.json` once: current, queue, done/negative, position.
10. `pwsh -File scripts/status.ps1 -Check -Sync` — green.
11. Run the gates (build, test, format, ktest when applicable).
12. Commit code + tests + status + graph in one commit. No generic message.
13. Do not write a second status narrative in `next-session.md`/`AGENTS.md`/the
    decisions file; the generated block is the status.

## E10. Commit protocol

- One task per commit. If the task needs a test-only prep commit, use the task
  id in both.
- Message: `area: imperative summary (task id)`.
  - Good: `nv2a: fill 32-bit clears through a span (D1)`.
  - Bad: `Task (RepoFleet task #37)`.
- The closing commit contains: code, tests, `docs/status.json`, the generated
  status blocks, the graph data and the artifacts.
- A docs-only status commit is a failure of the routine; fix the status file and
  regenerate instead.
- RepoFleet commits remaining changes as `<title> (RepoFleet task #N)`; commit
  yourself so the message follows this protocol, and the harness pushes.
- Use the close command printed by `-Next` (`-Complete`/`-Negative`) so the
  queue advances; a task left active is retried by the next slot.

## E11. Adding or reopening a task

- Add it to `docs/status.json` `queue` with an id, title, plan section, depends,
  acceptance and price.
- It cannot become active until the price is measured and the acceptance test is
  written.
- A negative is reopened only by adding a new measurement to its `reopen` field;
  the old evidence stays.
- Delete nothing from `done` or `negatives`; history is append-only.

## 0. Project Overview

**Project name:** XGrid  
**Target:** Native C#/.NET original Xbox emulator inspired by the hardware behavior and architecture of existing open-source Xbox emulators.

### Primary goal

Build a **playable** cross-platform original Xbox emulator in modern C#/.NET,
by **fully porting xemu 0.8.139 to managed C#**:

- **Playability first:** three retail titles playable, then seven
  (`ImplementationPlan.md` -> E1, gates P1-P5).
- **Full port:** every in-scope xemu/QEMU module is ported and verified; the
  ledger is `docs/xemu-port-manifest.md`
  (`scripts/port-manifest.ps1 -Check`, XFP0).
- C# 15 / .NET 11 (`Directory.Build.props`); NativeAOT-compatible core where
  practical.
- Windows, Linux, and macOS support.
- Portable software rasterizer as the exact oracle; the **ported** OpenGL
  renderer (`pgraph/gl`) as the product renderer; Vulkan is a later host
  backend, not a goal.
- CPU: the accurate interpreter + raw JIT today; the ported `softmmu`/TCG core
  (XFP11) is in scope, gated on "not slower than the current engine".
- Full port of the Xbox hardware: MCPX, NV2A, APU/AC'97, IDE, USB/OHCI/XID,
  SMBus/SMC/EEPROM, nvnet, PCI, timers.
- FATX and XDVDFS support; XBE loading.
- Debugger and tracing facilities; automated conformance/regression tests.
- GPL-2.0; **no QEMU/xemu native library** and no C/C++ emulator code in the
  build (`docs/porting-policy.md`); the open-source/publication status is
  `docs/open-source.md`.

### Reference projects

**xemu 0.8.139 is the port source** (`docs/porting-policy.md`): the emulated
machine is translated module by module into managed C#, with the reference's
state, dispatch and behavior preserved. The other projects are cross-checks and
fact sources, not the port base. Full extracted findings are in
`docs/research-notes/` (see the reference study deliverables below).

1. **xemu** - `References2/xemu-0.8.139` - **primary port source** - GPL-2.0
   (per-file LGPL-2+/MIT/GPL-2+)
   - The full-port base and behavioral oracle, adopted 2026-11-22 (0.8.137 and
     0.8.136 stay vendored as prior snapshots). Identity:
     `aa54c34cae1c09410d74c69a44210a9867fe2d16`, 2026-10-21, QEMU 10.2.0 base;
     the delta is catalogued in `docs/research-notes/xemu-0.8.139.md`.
   - Xbox machine/memory map, PCI IDs and IRQ routing, PIII CPUID/MSR quirks, MCPX
     (SMBus/SMC/EEPROM/flash/ACPI/ACI), NV2A (PFIFO/PGRAPH/renderer), APU, USB/XID,
     nvnet, and the QEMU modules the machine uses (IDE, OHCI, AC'97, timers,
     i8259, softmmu/TCG).
   - The port ledger is `docs/xemu-port-manifest.md`; the task order is E1 (XFP).
   - Newer in-development fork: `References2/xemu-Playtest-v2` (QEMU 10.2 base, Sega Chihiro
     arcade bring-up). Its NV2A carries host-GPU performance work (cost model, shader seeds,
     uniform cache, writeback heuristics, driver probe) with an un-tuned `nv2a-stock/`
     baseline to diff against — a fact source for GPU performance, not a port target. See
     `docs/research-notes/xemu-playtest-v2.md`.

3. **Cxbx-Reloaded** — `References2/Cxbx-Reloaded-master` — GPL-2.0
   - Reference for XBE format (exact struct offsets), kernel ordinal semantics, NV2A/NV097
     register and method maps, texture swizzle, OHCI/XID, XDVDFS layout.

4. **xqemu** — `References2/xqemu-master` — GPL-2.0
   - Older QEMU fork; compact single-file APU register map, SMC/EEPROM defaults, TSC and
     accelerator quirks, hardware documentation pointers. No XDVDFS/FATX/XBE code.

5. **XISOSharp** — `C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp` — **MIT**
   - Pure C# XDVDFS/XISO library (read/write, CISO, XBE metadata). The only reference project
     approved for direct integration as a library dependency, where architecture allows.

6. **envytools** - `References2/envytools-master` - **MIT**
   - NVIDIA register/method database (`rnndb/` XML) + GPU architecture docs. Cross-check for
     NV2A method offsets/enums (`rnndb/graph/nv20_3d.xml` + `nv10_3d.xml` = Xbox class
     `0x0097`: `0x17FC` begin/end, `0x1800/0x1810/0x1818` element/array/inline, `0x0B00/0x0B80`
     VP upload), FIFO pusher/puller semantics (`docs/hw/fifo/dma-pusher.rst`, `puller.rst`),
     XF/vertex-program semantics (`docs/hw/graph/xf/`), PMC/PTIMER. Note: envytools' own
     "NV2A" is the nForce IGP, not the Xbox GPU. See `docs/research-notes/envytools.md`.

7. **nv2a-trace** - `References2/nv2a-trace-master` - **MIT**
   - apitrace-style tracer for a real Xbox GPU (needs hardware + `xboxpy`): per-method
     pre/post state, surface/texture PNG dumps, `debug.html` log. Without hardware its
     pushbuffer command decoder (`XboxHelper.parse_command`), NV2A/CACHE1 register constants
     and `Texture.py` format table are reusable facts. See `docs/research-notes/nv2a-trace.md`.

8. **Copetti - Xbox Architecture** - `References2/CopettiDocumentation` - **CC BY 4.0**
   - High-level console architecture essay (`xbox-architecture.md`): CPU/memory/graphics/
     audio/IO/OS/boot/anti-piracy narrative. Orientation, onboarding and design cross-check
     only; attribution required if any text or figure is reused. Never a register-level
     oracle. See `docs/research-notes/copetti.md`.

**Withdrawn reference.** Earlier drafts listed a standalone C# emulator "Xermu"
(`References2/Xermu-main`). No such project exists in the workspace; the operator confirmed
2026-10-13 that the correct primary LLE reference is `References2/xemu-0.8.137` (0.8.136 was
the primary snapshot until the 0.8.137 adoption on 2026-10-20). Do not add
Xermu to any list, and do not treat historical citations to it (including
`xgrid-architecture-decisions.md`) as facts.

**Licensing rule (SUPERSEDED 2026-10-23 - see `docs/porting-policy.md`):** XGrid
is GPL-2.0 and may port code from xemu, QEMU, Cxbx and xqemu with their notices;
the earlier "no source code is copied / facts only" rule no longer applies. The
still-binding constraints are: full managed C# (no QEMU/xemu native code in the
build) and the provenance rules (SPDX + `Ported from` + `THIRD_PARTY_NOTICES.md`
+ `sources.md`). XISOSharp (MIT) remains a dependency with its notices; Copetti
is CC BY 4.0 and needs attribution if its content (not just facts) is reused.
Never commit copyrighted firmware, EEPROMs, BIOS images, or game data.

### Reference study deliverables

The first project activity was a completed reference study. Its outputs are committed under
`docs/research-notes/` and must be consulted during implementation:

```text
docs/research-notes/sources.md                      licensing/provenance inventory (plan section 46)
docs/research-notes/xemu.md                         xemu hardware facts
docs/research-notes/cxbx.md                         XBE, kernel ordinals, NV2A, USB, XDVDFS
docs/research-notes/xqemu.md                        xqemu unique artifacts
docs/research-notes/xisosharp.md                    reusable MIT library + XDVDFS format facts
docs/research-notes/envytools.md                    NV2A method/register DB + FIFO/XF docs (MIT)
docs/research-notes/nv2a-trace.md                   real-hardware PGRAPH tracer facts (MIT)
docs/research-notes/copetti.md                      high-level architecture essay (CC BY 4.0)
docs/research-notes/xgrid-architecture-decisions.md synthesis, constants appendix, open questions
```

**Part II** at the end of this document expands every phase into a concrete, file-level task plan.

### Guiding principle

xemu 0.8.139 is the reference implementation. Port it.

> Translate the reference's state, dispatch and behavior into managed C# behind
> the existing seams; keep the old path until the differential is exact; record
> the provenance. The product shell (CLI, frontend, gates) is not rewritten.

A fresh design is allowed only when the reference is wrong or its shape does not
fit the seam, and the reason is recorded in the ledger row.

### Development direction (history; superseded 2026-11-22)

> **Superseded as the current task list by the execution plan at the top of this
> file (E1, the XFP full-port program).** The section below is the plan's
> narrative history; `docs/status.json` and the generated block are the current
> state. The 2026-11-22 operator decision replaces the 2026-10-22
> "xemu-class performance after playability" aim with **playable first, via a
> full xemu 0.8.139 port** (`docs/development-direction.md` §0).

Canonical priority order: `docs/development-direction.md` (summarized in
`AGENTS.md` "Development direction"). The **2026-10-22 operator decision** makes
xemu-class performance or better the long-run objective and re-opens Track C as
the lead; the 2026-10-20 product-path-first order is retained as history (the
present path landed, the corpus is 22 rows / 17 samples, the product surface is
usable). Current order:

1. **Track C — performance (lead, re-opened 2026-10-22).** **C4 (host-pointer
   translate probe) was implemented, measured on an interleaved 1B A/B and
   REVERTED as a null result** (decisions §232a: menu `runnerMs` 15,872 →
   16,026, triangle 3,407 → 3,382, walls within noise, SHAs exact). The
   measured wall decomposition (§232b) puts the triangle's 6.9 s at
   `RunJitBlock` 4.53 s (native 3.42 + 1.1 bookkeeping) + `RunNonJit` 2.25 s.
   **§237/§238 LANDED two exact slices:** instruction fetches from flash/BIOS/
   mirror/MCPX-overlay windows are cached and blocked (a new
   `IImmutableImageDevice` class with no store path, wholesale flush on the host
   actions that move an image, and a mode-change re-formation of a block whose
   decode defaults moved), and the non-JIT run slice went 256 -> 64 after its
   first sweep. `nonRamSkips` 359,476 → 0, one-instruction non-JIT runs
   434k → 134k, `nonJitInstructions` 5.48M → 2.62M, `nonJitMs` 2190 → 1650,
   blocks 2,603 → 2,812. Measured together: **triangle `gl+replace`
   6.672 → 6.081 s (-8.9%)**, menu 20.456 → 19.762 s (-3.4%), 100M boot phase
   2.276 → 1.813 s (-20.3%), every 1B SHA/counter/frame byte-identical. §237 also
   **repriced the "superblock for the boot" lever out**: the ROM share of the
   non-JIT population was 9%, not 100%, so its ceiling was 6.4% and the remaining
   boot mass is 55k RAM heads below the build threshold (an encoder price, §234e).
   **C4 and C4b are closed.** The next candidate, the `RunSpecBlock` `TryMemo`-handoff
   probe, was **priced and rejected** (§238 §4: 4.2-4.9% of non-JIT members sit at
   a built head = ~1.2% of the wall), and **§239 closed the code-volume question**:
   removing the raw emitter's per-member EIP/TSC/served bookkeeping (11 emitted
   instructions per member, static per edge instead) moved the engine 6.3 → 6.1
   ns/member and the wall not at all, so the runner is **latency-bound, not
   instruction-bound**. **§240 then measured and won the device stage**: 1.31 s of
   the triangle 1B `gl+replace` wall was inside 44,030 device writes (766 ms draw
   flushes, ~540 ms surface clears); a 32-bit clear now fills rows through
   `Span<uint>.Fill` (device time **1,306 → 816 ms**), giving **triangle
   6.31/6.43/6.34 → 5.61/5.74/5.73 s (-10.6%, 3/3)** and **menu 22.43/20.76 →
   19.98/18.82 s (-10.6%/-9.4%)** with every anchor byte-identical. **C6 is the
   paying track now**; the largest unowned item is the interpreted vertex program
   inside the draw flush (413-435 ms). Each stage: same-window A/B ≥10% wall or
   ≥20% engine, exact 1B SHAs/counters, flag-off byte-identical, ktest log
   byte-identical.
2. **Track B — bounded B3 finish — CLOSED 2026-10-22 (§236).** The present path
   was already closed (readback opt-in, batching, sampler parity, replace-mode
   composition); the computed-mode panel (gap 12) is now closed too - a cube
   stage samples a host 2D-array texture decoded from the guest's swizzled faces
   (`sampler2DArray` units 4-7) through the software sampler's own decoder, and
   the corpus row agrees with software within 2 pixels of 307,200 at both
   anchors. The word pool now carries only a HILO dot input's raw pair, and only
   when the stage's dot map is 4-7. No new B fidelity rows until C lands.
3. **Track E — compatibility corpus.** 22 rows / 17 samples; add performance
   rows; no new breadth beyond the standing rows.
4. **Track P — product completeness (parallel).** Host audio remains the large
   gap; pad/save states/per-title config/renderer are landed. Do not displace C.
5. **Track D — measured >2% only.**
6. **Gates unchanged.**

**2026-10-20 detail (historical; the order above is current):**

1. **Track B — host GPU present path (was the lead; landed).** The Avalonia frontend renders on
   the software rasterizer; `--gpu gl --gpu-replace` netted only 2.2 s on the menu
   because of ~9.9 s of GL draw/readback/present overhead (31.1 vs 33.3 s).
   **Landed 2026-10-20 (§204):** the split was measured (submission 6.87 s =
   2.49 µs × 2.76M draws, readback 0.71 s, swizzle/alloc ~0.3 s); the readback is
   now opt-in (`--frame`/`--frame-first`), each draw is one packed `uTri[25]`
   uniform + one `glDrawArrays` (positions by `gl_VertexID`, no VBO upload), the
   per-draw key strings/divisions/binds are gone, and the per-method seam call +
   method histogram were removed from the hot path. Same-window medians vs
   `4f0bd47`: menu `gl+replace` **63.09 → 45.25 s (-20.5%, 3/3; `drawMs` 12.55 →
   2.41 s, readback → 0)**, triangle 10.26 → 9.89 s (-0.9%, 3/3); quiet-host
   projection menu ~24.7 s (boundary), triangle ~6.4 s (CPU-bound). 1B GL goldens
   pass (menu `6BE6325F…`, triangle `3FC4B419…`, sw anchors unchanged).
   **Landed 2026-10-20 (§205): draw-command batching** — a PGRAPH primitive flush
   averages 82 triangles, so the backend stages the whole flush and submits it as
   one `glDrawArraysInstanced` (per-triangle block = a uniform-array slot selected
   by `gl_InstanceID` via a flat varying; capacity 64 from the queried uniform
   budget; `XGRID_GL_NO_BATCH=1` forces the old path; boundary =
   `BeginDrawBatch`/`EndDrawBatch` around `Nv2aMmio.FlushDraw`). Same-binary A/B:
   `drawMs` **1971 → 1045 ms (-47%, 3/3)**, 59,437 instanced draws replace 2.76M
   single draws, wall **42.20 → 40.43 s (-4.2%, 3/3)**, both 1B GL frames
   byte-identical. Remaining menu submission is ~0.8 s of block packing (not GL
   calls) and the ~0.5-0.9 s texture fingerprint, both measured and deferred.
   Next: B3 method coverage on the corpus (zero `unsup` rows, guest counters
   identical) — the submission levers are spent.
   **Status 2026-10-21 (next session, §219):** the GL backend now samples like the
   software rasterizer - the TEXFILTER tent and the nine-tap 1-2-1 convolution
   (only for MIN = CONVOLUTION_2D_LOD0), the *decoded* wrap modes (the raw address
   word assigns 1 to wrap and 2 to mirror, which the backend had swapped) with
   clamp-to-border, the PROJECT2D divide by w, and the clip origin on both
   fragment axes - and `--gpu-replace` now composes a declined draw's frame into
   guest RAM (one writer per frame) instead of presenting the clear colour
   (compatibility gap 8 closed: `xgu bump_cubemap_reflect` 3B presents
   `DA0750EE...` = the software frame, CPU SHA unchanged; depth/stencil are not
   composed, documented). The 1B GL gate frames stay byte-identical (`B6116DAE`
   menu / `7A860ED7` triangle), `BackendUnsupported=0`, `composedFrames=0`. On
   `vp_lighting` the two renderers agree within one 8-bit step in both filter
   states. Suite 1725.
2. **Track E - compatibility corpus.** `docs/compatibility.md` + a
   boot-to-first-frame harness + ≥8 samples; no NV2A/APU fidelity polish without
   a matrix row and a usage histogram (`compatibility-corpus.md`). **M-B MET
   2026-10-20:** `scripts/boot-to-first-frame.ps1` (+ `--frame-first`,
   `--frame-scanout`, `-Corpus`, `-IsoPath`) and `scripts/build-nxdk-samples.ps1`
   (MSYS2 + clang/lld/llvm/bison/flex/cmake, nxdk `a6c31ee`) give **11 rows over
   7 samples**, every row reproducible. The corpus immediately found two measured
   gaps: direct-framebuffer output is not what `--frame`/the frontend present
   (`TryCopyScanoutFrame` + `--frame-scanout` recover it - **closed 2026-10-21, §208** (the VGA CRTC behind `NV_PRMCIO` is
    decoded, `TryCopyDisplayFrame` + `--frame-display` export the scanout and
    the frontend samples it at ~30 Hz; `--frame` keeps its gate anchor). Making it the default
   needs xemu's display model and a gate re-record), and the repo's `nxdk-mesh`
   snapshot was missing `verts.h`/`texture.h`. Steering inputs:
   `usage-histogram.md` (measured usage, now including TEXFILTER), `nv097-coverage.md` (111 handled / 90
   recorded), `gpu-reference-map.md`. One `mesh` mipmap question is still open.
   **Corpus-driven fidelity landed 2026-10-21 (§209):** the cubemap row renders
   (`xgu bump_cubemap_reflect`, 2 colours -> 21,041, `|dmean|` 5.3/2.4/1.3)
   after fixing the face extent (TEXIMAGERECT vs TEXFMT `BASE_SIZE`) plus the
   three format gates, and **TEXFILTER is honored** (tent at LOD 0, convolution
   only for MIN = `CONVOLUTION_2D_LOD0`) behind opt-in `--tex-filter registers` -
   the menu's stage 0 programs `2/2/1` on 211M texel samples/1B, so the
   default-flip decision is an operator item. **§210 (2026-10-21):** the 2.4x
   cost and the 8.7% pixel delta §209 attributed to the tent were a *misapplied
   convolution* (the reference gates the kernel on MIN 7); with that fixed and
   both compiled fills serving the tent, the flag costs 32.18 s vs 32.22 s at 1B
   menu and moves no gate anchor (triangle SHA/frame identical, menu frame
   `4EF1FBF6…`), while `bump_cubemap_reflect`'s colour depth goes 20,843 ->
   **§219 (2026-10-21):** the GL side of gap 7 is closed too - `xgu vp_lighting`
   at 3B (fractional texcoords, zero declined draws) matches the software frame
   within one 8-bit step *with and without* `--tex-filter registers`, and the
   flag moves both renderers by the same ~6.5% (max delta 80); the gate frames
   are unchanged. Gap 8 is closed (§219d). Open corpus items are gap 9
   (second-emulator frame oracle) and gap 10 (`bumpenv_ripple` app state).
   26,347 toward its reference's 30,806 at an unchanged mean. **§211
   (2026-10-21):** gap 10 (`xgu bumpenv_ripple` black) is **diagnosed, not
   fixed** - every presented surface is the colour clear, the two per-frame
   quads take their colour from `T1` (`V0 = T1`, final `D = V0`; stage 1 `NONE`
   in one draw, black texels in the bump draw) and the zeta clear
   (`0x00711174`, `LEQUAL`) is nearer than every fragment, so nothing passes;
   the renderer is exonerated and three items are filed (Z24S8 clear-word
   layout vs the reference, cull winding on those quads, the app state the
   reflection needs). The same session fixed the convolution tap step on linear
   (LU_IMAGE) stages (one texel per tap, `psh.c apply_convolution_filter`); the
   opt-in flag's default stays an operator item. 1625 tests green.
3. **Track P - product completeness (parallel).** Host audio, physical pad,
   save-state V1, per-title config.   **Status 2026-10-21 (20th session, §217):** physical pad and save states
   landed 2026-10-20 (§206); this session added the graphics-fidelity settings
   (`--ffp`, `--tex-filter`), per-title overrides (V1 keyed by image path),
   persistent EEPROM state (`<data dir>/eeprom.bin`, CLI `--eeprom-out`) and the
   frontend's first test project (15 tests). Host audio (the DSP execution it
   needs) is the remaining product gap.
   **Status 2026-10-21 (twenty-second session):** pause now has a step control -
   F7 steps one presented frame (8M-instruction budget so an idle guest cannot
   hang the UI), Shift+F7 steps 1,000,000 instructions; Home gained both
   buttons and the About page/README list the keys.
   **Status 2026-10-22 (§§221-223):** the frontend is no longer sw-only - a
   Settings > Renderer switch runs the host-GPU OpenGL backend headless with the
   per-flip readback (measured -29% wall at 1B through the real session), and
   per-title overrides V2 now key a CHD by its XBE title id. The policy seam
   (`RendererPolicy`/`GraphicsBackendHost`), thread-correct snapshot save/load
   and `tools/frontend-smoke` landed with it (measured +9.8%/+8.2% in-app
   throughput, every gate anchor unchanged).
   Host audio (APU DSP execution) is still the
   remaining product gap.

4. **Track C — re-opened and leads (2026-10-22).** The operator set the
   long-run objective to xemu-class performance or better; the engine is ~17 s
   of the 24.3 s menu `gl+replace` wall and dominant on the 7.5-7.9 s triangle
   wall. New program: C4 flat memory model/soft TLB, C5 trace/superblock
   formation, C6 device/MMIO fast path; each stage needs a same-window A/B
   (≥10% wall or ≥20% engine) and exact 1B differentials. C3-x/y/z is closed;
   the `--jit`/`--jit-raw` default-flip decision remains open. Incremental
   sub-2% slices stay banned; interpreter **correctness** work is still allowed
   (the §225 campaign is the precedent).
5. **Track D — measured >2% only** (page-end wait, bookkeeping).
6. **Environment preconditions (binding, 2026-10-20).** Two machines share this
   tree (`docs/hosts.md`); only the *reference host* may record M-A. Session start
   is `scripts/host-doctor.ps1` (fingerprint, free space, toolchain, gate media,
   `References2` manifest). Semantic anchors (1B RAM/CPU SHAs, frame SHAs, every
   counter) must be **identical on both machines** — a mismatch is a determinism
   bug, not an environment note; performance anchors (walls, `msps`, `rasterPsh`,
   `drawMs`) are host-local and only same-window A/B pairs travel between hosts.
   `References2/` is the canonical and only reference store, verified by
   `scripts/refs-manifest.ps1` against `docs/refs-manifest.json`; out-of-tree
   reference backups are retired by operator decision.

Headless `--gpu sw` stays the default and gate currency; `data/test-samples/`
stays gate currency and real content stays benchmark-only; accurate-mode perf
flags stay opt-in until median-of-3 walls plus exact golden SHAs/counters,
`verifyFailures=0`, and flag-off byte-identical runs. Fast-mode contract:
`docs/research-notes/jit-track-charter.md` (unchanged).

**Status 2026-10-21 (20th session, decisions §§215-218; 1717 tests green).**
Three landed slices, no existing anchor moved: **fixed-function lighting** (§215:
the enabled-light loop, spot cone, attenuation and the reconstructed specular
exponent behind opt-in `--ffp` - gap 6 is now complete, and no measured leg
enables a light), **PTIMER 0.8.137** (§216: 61-bit register time with writable
`TIME_0/1`, `ALARM_0` with the 2^32 re-arm, the alarm's `INTR_0` bit and
`NV_PMC_INTR_0_PTIMER`), and **Track P** (§217: `--ffp`/`--tex-filter` in the
frontend, per-title overrides V1, persistent EEPROM state, the frontend test
project). Evidence: 1B fast legs exact with and without `--ffp`
(triangle `B45254CA`/`7DAB63EB`, menu `51B56687`/`F5D61C94`), accurate goldens
`A1740DE0`/`4EF1FBF6` with GL policy frames `7A860ED7`/`B6116DAE`,
`BackendUnsupported=0`, ktest smoke 7/7 at 792,800,535 steps with the
byte-identical 40,797-byte log. The APU sample counter `NV_PAPU_XGSCNT` (§218) is now derived from the EP frame divider, which the dashboard reads and stores: the two menu RAM anchors were re-recorded (`01BE5D64 -> 6AB39AB0` accurate, `51B56687 -> 34973510` fast) while frames, CPU SHAs, counters and all 13 corpus rows stayed exact. NV2A snapshot v6 -> **v8** (old `.xgs` invalid).
Next: Track P host audio (needs DSP execution), the frontend on the GL backend
(the composition blocker is closed), or the gap-9 second-emulator oracle.

**Status 2026-10-21 (21st session, decisions §219; 1738 tests green).** Four
slices, no anchor moved: **GL TEXFILTER parity** (§219a - compatibility gap 7
closed; `DecodeTextureFilter` is the single decode, the shader runs the tent and
the nine-tap PROJECT2D convolution behind `uTexFilter`/`uTexConv` uniforms, so
the default is still the same nearest `texelFetch`), **GL replace-mode
composition** (§219b - gap 8 closed; a software-painted draw makes the flip drop
the FBO and rebuild the frame from guest RAM, `composeUploads=`; the cubemap
corpus row's replace leg now presents the software anchor `DA0750EE…`),
**clear write-mask parity** (§219c - `ClearCore` lifts the GL masks for a clear,
`ClearMaskLiveTests` fails pre-fix), and **per-title overrides V2** (§219d -
`XGrid.Host.DiscTitleId`, `title:XXXXXXXX` keys with title-over-path
precedence). Evidence: accurate 1B goldens PASS (sw `4EF1FBF6`/`A1740DE0`, GL
`B6116DAE`/`7A860ED7`, violations 0, counters identical), fast legs exact (menu
`34973510`/`F5D61C94`, triangle `B45254CA`/`7DAB63EB`), ktest smoke PASS 7/7 at
792,800,535 steps with the byte-identical log. Suite 1717 -> 1738.

**Status 2026-10-22 (22nd session, decisions §§220-221; 1757 tests green).** Two
slices, no guest-visible behavior change: **the GPU renderer is a frontend
setting** (§220 - Settings → Renderer picks software or OpenGL; the session
creates the headless WGL context on the emulation thread in replacement mode
with the per-flip readback, falls back to software with a logged reason on any
failure, and the display follows the renderer with the new
`TryCopyScanoutFrameFromMemory` lane covering direct-framebuffer guests before
the first backend frame; measured through the real session at 1B on gate media:
34.8 s software vs 24.7 s OpenGL, same picture) and **the CHD title id**
(§221 - `XGrid.XboxFs.XdvdfsSectorReader` walks the XDVDFS volume over any
`IDvdImage`, so `DiscTitleId` keys a CHD by XBE title id instead of falling back
to its path; cross-checked against the XISOSharp path on six real ISOs). Host
API: `Nv2AMmio.GpuMemory` exposes the device's guest-memory adapter (the CLI's
duplicate adapter is deleted). Evidence: 1B menu GL golden PASS byte-identical
(sw `4EF1FBF6`, gl `B6116DAE`, diff 2602 / maxDelta 1 / violations 0, counters
33632/686/686), accurate frames unchanged, ktest smoke PASS 7/7 at 792,800,535
steps with the byte-identical 40,797-byte log. Suite 1738 -> 1757.

**Remaining Track P gap: host audio output** (needs APU GP/EP DSP execution
first). Track B is closed end to end: the B3 computed-mode panel (gap 12) landed
in §236 - a cube stage is a host 2D-array texture decoded from the guest's
swizzled faces - so no B item remains open.---

*Status 2026-10-21 (22nd session, decisions 220; 1762 tests green).** CPU
correctness campaign: the independent x86 differential (`tools/cpu-oracle/`)
widened from 333 to **629 cases** (sign-sensitive patterns for every MMX/SSE1
packed op, saturating shift counts, x87 transcendentals/environment, 16-bit
addressing, segment overrides, `LOCK`, string/stack edges) and found **nine
defects**, all fixed and pinned: `PMINSW`/`PMAXSW` compared unsigned (they were
decoded as invented `Pminuw`/`Pmaxuw`), `PSRAW`/`PSRAD` count saturation,
`XLAT` undecoded, 16-bit `[SI]`/`[DI]` eating a bogus disp16, x87 NaN stores
flipping the sign/dropping the payload (`.NET double.NaN` is negative), the
FUCOM family latching IE for a quiet NaN, `FSQRT`-negative C2, and
`MINPS`/`MAXPS`/`MINSS`/`MAXSS` returning the destination on equal operands.
**No gate anchor moved** (accurate 1B goldens byte-identical, fast anchors exact,
ktest log byte-identical) - the gate titles execute none of the fixed shapes.
Deliberately out of scope: no SSE2 (the CPU is a Pentium III, so the 128-bit
packed-int forms stay `UnsupportedInstruction`), `FSTENV`/`FSAVE` pointer
metadata, the reference's `0/0` ZE-vs-IE class. Suite 1738 -> 1762.

---

# 1. Design Principles

## 1.1 Core must be independent of the UI

The emulator must not depend on WPF, Avalonia, WinForms, SDL UI code, or any frontend framework.

Required architecture:

```text
XGrid.Core
       |
       +-- CLI
       |
       +-- Avalonia frontend
       |
       +-- future WPF frontend
       |
       +-- automated test runner
```

## 1.2 Hardware emulation must be separated from host APIs

Xbox hardware code must not directly call:

- Vulkan
- OpenGL
- SDL
- Windows APIs
- Linux APIs
- macOS APIs

Use interfaces between Xbox hardware and host implementations.

Example:

```csharp
public interface IGraphicsBackend
{
    void Initialize(GraphicsConfiguration configuration);
    void BeginFrame();
    void EndFrame();
}
```

## 1.3 Correctness before optimization

Development order:

```text
Correctness
    ↓
Functional compatibility
    ↓
Profiling
    ↓
Optimization
    ↓
JIT
    ↓
Advanced optimization
```

Do not optimize unmeasured code.

## 1.4 Every hardware component needs tests

Each major device should have:

- unit tests
- register tests
- state transition tests
- integration tests
- regression tests

## 1.5 Build the debugger early

The debugger should not be an afterthought.

CPU state, memory inspection, breakpoints, traces, and device registers should be available before commercial games work.

---

# 2. Technology Stack

## 2.1 Required

- C#
- .NET 9 initially
- SDK-style projects
- nullable reference types
- implicit usings where appropriate
- analyzers
- xUnit or NUnit
- Git
- GitHub Actions

## 2.2 Recommended

- .NET 9/10 as the runtime evolves
- Avalonia for the desktop frontend
- Vulkan as primary graphics backend
- Silk.NET or another maintained C# Vulkan binding
- System.Numerics
- System.Runtime.Intrinsics
- System.Runtime.CompilerServices.Unsafe
- `Span<T>`
- `MemoryMarshal`
- function pointers
- NativeAOT for supported components

## 2.3 Avoid in the core

Avoid making the emulator dependent on:

- QEMU
- native xemu libraries
- proprietary Xbox SDKs
- Windows-only APIs
- WPF
- SDL-only APIs
- external emulation processes

External/native libraries can be used as optional host backends where justified.

---

# 3. Repository Structure

```text
XGrid/
|
+-- XGrid.sln
|
+-- Directory.Build.props
+-- Directory.Build.targets
+-- Directory.Packages.props
+-- global.json
+-- README.md
+-- LICENSE
+-- CONTRIBUTING.md
+-- SECURITY.md
|
+-- docs/
|   +-- architecture/
|   +-- cpu.md
|   +-- memory.md
|   +-- nv2a.md
|   +-- mcpx.md
|   +-- storage.md
|   +-- audio.md
|   +-- input.md
|   +-- boot.md
|   +-- debugging.md
|   +-- performance.md
|   +-- compatibility.md
|   +-- research-notes/
|
+-- src/
|   +-- XGrid.Core/
|   +-- XGrid.Cpu/
|   +-- XGrid.Memory/
|   +-- XGrid.Bus/
|   +-- XGrid.Mcpx/
|   +-- XGrid.Nv2a/
|   +-- XGrid.Audio/
|   +-- XGrid.Storage/
|   +-- XGrid.XboxFs/
|   +-- XGrid.Xbe/
|   +-- XGrid.Input/
|   +-- XGrid.Graphics/
|   +-- XGrid.Debug/
|   +-- XGrid.Host/
|   +-- XGrid.Frontend.Avalonia/
|   +-- XGrid.Cli/
|
+-- tests/
|   +-- XGrid.Core.Tests/
|   +-- XGrid.Cpu.Tests/
|   +-- XGrid.Memory.Tests/
|   +-- XGrid.Bus.Tests/
|   +-- XGrid.Mcpx.Tests/
|   +-- XGrid.Nv2a.Tests/
|   +-- XGrid.Audio.Tests/
|   +-- XGrid.Storage.Tests/
|   +-- XGrid.XboxFs.Tests/
|   +-- XGrid.Xbe.Tests/
|   +-- XGrid.Integration.Tests/
|   +-- XGrid.Compatibility.Tests/
|   +-- XGrid.Performance.Tests/
|
+-- tools/
|   +-- XGrid.CpuTestRunner/
|   +-- XGrid.XbeTool/
|   +-- XGrid.MemoryTool/
|   +-- XGrid.TraceTool/
|   +-- XGrid.Nv2aTool/
|
+-- benchmarks/
|   +-- CpuBenchmarks/
|   +-- MemoryBenchmarks/
|   +-- XdvdfsBenchmarks/
|   +-- Nv2aBenchmarks/
|   +-- EndToEndBenchmarks/
|
+-- test-data/
|   +-- cpu/
|   +-- xbe/
|   +-- xdvdfs/
|   +-- fatx/
|   +-- nv2a/
|   +-- audio/
|   +-- integration/
|
+-- scripts/
    +-- build.ps1
    +-- build.sh
    +-- test.ps1
    +-- test.sh
    +-- benchmark.ps1
    +-- format.ps1
```

---

# 4. Dependency Graph

The implementation should follow this dependency direction:

```text
                    Frontend
                       |
                      Host
                       |
                  Emulator Core
                       |
        +--------------+--------------+
        |              |              |
       CPU           Memory          Bus
        |              |              |
        +--------------+--------------+
                       |
          +------------+------------+
          |            |            |
        MCPX          NV2A         APU
          |            |            |
          +------------+------------+
                       |
                    Storage
                       |
               FATX / XDVDFS / XBE
```

Lower layers must never depend on higher layers.

---

# 5. Phase 0 — Project Bootstrap

## Objectives

Create a compiling solution and establish engineering standards.

## Tasks

- Create solution.
- Create all major projects.
- Enable nullable.
- Enable warnings.
- Add analyzers.
- Configure formatting.
- Add CI.
- Add unit test project.
- Add benchmark project.
- Establish logging.
- Establish versioning.
- Establish documentation structure.

## Acceptance criteria

```text
dotnet build
```

must succeed on supported platforms.

```text
dotnet test
```

must succeed with zero tests failing.

CI must execute:

- build
- test
- formatting validation
- analyzers

---

# 6. Phase 1 — Emulator Core

## 6.1 Emulator lifecycle

Create:

```csharp
public interface IEmulator
{
    EmulatorState State { get; }

    void Initialize();
    void Reset();
    void Start();
    void Pause();
    void Resume();
    void Stop();
}
```

## 6.2 Clock

Implement:

- emulated cycle accounting
- device scheduling
- deterministic test mode
- host-time mode

The first version should favor determinism.

## 6.3 Event scheduler

Create:

```csharp
public interface IEmulatedDevice
{
    void Reset();
    void Tick(ulong cycles);
}
```

Later replace coarse ticking with event-driven scheduling where useful.

## Acceptance criteria

- Emulator can initialize and reset deterministically.
- A test can execute a fixed number of cycles and obtain identical state.
- No frontend dependency exists.

---

# 7. Phase 2 — Memory System

## 7.1 Physical memory

Implement Xbox RAM representation.

Required operations:

```text
Read8
Read16
Read32
Read64
Write8
Write16
Write32
Write64
```

Also provide span access:

```csharp
Span<byte> GetSpan(uint address, int length);
```

## 7.2 Endianness

Create explicit little-endian helpers.

Never depend on accidental host behavior.

## 7.3 Memory map

Implement:

```text
Physical RAM
ROM/flash
MMIO
PCI regions
GPU memory
device windows
```

## 7.4 MMIO

Create:

```csharp
public interface IMmioDevice
{
    uint Read(uint address, int size);
    void Write(uint address, int size, uint value);
}
```

## 7.5 Fast memory path

Implement:

```text
Address
  ↓
range check
  ↓
RAM direct access
```

for normal RAM.

MMIO must take the slower device path.

## Acceptance criteria

- Memory read/write tests pass.
- MMIO routing works.
- RAM performance benchmark exists.
- Invalid addresses produce deterministic behavior.

---

# 8. Phase 3 — x86/Pentium III CPU

This is one of the highest-risk components.

## 8.1 CPU state

Implement:

```text
EAX
EBX
ECX
EDX
ESI
EDI
EBP
ESP
EIP
EFLAGS
CS
DS
ES
FS
GS
SS
CR0
CR2
CR3
CR4
DR0-DR7
```

and relevant descriptor/control state.

## 8.2 Decoder

Create:

```text
Prefix decoder
Opcode decoder
ModRM decoder
SIB decoder
Immediate decoder
Address-size handling
Operand-size handling
```

## 8.3 Instruction representation

Create an internal representation:

```csharp
public readonly struct DecodedInstruction
{
    public Opcode Opcode { get; init; }
    public Operand Operand1 { get; init; }
    public Operand Operand2 { get; init; }
    public Operand Operand3 { get; init; }
    public int Length { get; init; }
}
```

## 8.4 Interpreter

Implement instruction families in this order:

> **Status:** Tiers 1–10 are implemented and covered by tests (integer/logic, control flow,
> shifts/rotates, MUL/IMUL/DIV/IDIV, string ops with REP/REPE/REPNE, system instructions
> including CPUID/RDTSC/RDMSR/WRMSR/SETcc/CMOVcc, MOV CR/DR, LGDT/LIDT, INVLPG, x87/FPU,
> MMX, and SSE1). x87 arithmetic is double-rounded; FWAIT/#MF and SSE2 are intentionally absent.

### Tier 1

- NOP
- MOV
- PUSH
- POP
- XCHG
- LEA

### Tier 2

- ADD
- ADC
- SUB
- SBB
- INC
- DEC
- CMP
- TEST
- AND
- OR
- XOR
- NOT
- NEG

### Tier 3

- JMP
- CALL
- RET
- Jcc
- LOOP
- INT
- IRET

### Tier 4

- SHL
- SHR
- SAR
- ROL
- ROR
- RCL
- RCR

### Tier 5

- MUL
- IMUL
- DIV
- IDIV

### Tier 6

- string instructions

### Tier 7

- system instructions

### Tier 8

- x87/FPU

### Tier 9

- MMX

### Tier 10

- SSE

## 8.5 Flags

Implement exact behavior for:

```text
CF
PF
AF
ZF
SF
OF
DF
IF
TF
```

Avoid recomputing every flag eagerly if profiling later proves lazy flags worthwhile.

## 8.6 Exceptions

Implement:

- divide error
- invalid opcode
- general protection
- page fault
- breakpoint
- debug exception
- invalid TSS where needed
- device-not-available where needed

## 8.7 CPU test framework

Create a test format:

```json
{
  "name": "ADD_EAX_EBX",
  "registers": {
    "EAX": "0x00000005",
    "EBX": "0x00000007"
  },
  "code": "01D8",
  "expected": {
    "EAX": "0x0000000C"
  }
}
```

**Independent differential (2026-10-20).** `tools/cpu-oracle/` generates a
333-case corpus and runs it on an independent x86 implementation (QEMU's TCG
core through Unicorn; a system-QEMU harness is staged). The test
`CpuOracleTests.InstructionCorpus_MatchesIndependentOracle` runs the checked-in
`data/cpu-oracle/int.json` + `int.bin` in CI. It compares GPRs, defined EFLAGS
bits, EIP, the architectural FXSAVE words (FCW/FSW/MXCSR) and the memory each
case stores. It found the missing `0F BA` decode (BT/BTS/BTR/BTC r/m, imm8),
the swapped `FPREM` quotient condition codes, and the missing `FDIV` ZE/IE
latches. See `docs/research-notes/cpu-oracle.md`.

## Acceptance criteria

The interpreter must pass an extensive instruction-level regression suite before Xbox boot development proceeds.

---

# 9. Phase 4 — x86 MMU

Implement:

- segmentation where required
- paging
- page directory
- page tables
- CR0
- CR3
- CR4
- TLB
- page faults
- access permissions
- user/supervisor semantics where required

## Optimization plan

Start with correctness.

Then:

```text
Virtual address
     ↓
TLB hit
     ↓
physical address
     ↓
RAM
```

Only on TLB miss:

```text
page table walk
```

## Acceptance criteria

- Synthetic page-table tests pass.
- Page faults are correct.
- TLB invalidation is tested.
- CPU can execute code through virtual addresses.

---

# 10. Phase 5 — Xbox Bus and Interrupt Infrastructure

Implement:

- PCI bus
- device registration
- MMIO regions
- interrupt lines
- PIC-like behavior
- IOAPIC behavior
- timers
- DMA infrastructure

Create:

```csharp
public interface IXboxDevice
{
    string Name { get; }
    void Reset();
}
```

## Acceptance criteria

Devices can register themselves without modifying central switch statements.

---

# 11. Phase 6 — MCPX [PARTIAL]

Implement in stages:

1. SMBus
2. EEPROM
3. Flash
4. SMC-related functionality
5. USB host integration
6. interrupts
7. timers
8. remaining MCPX functions required by boot

Architecture:

```text
MCPX
 |
 +-- SMBus
 +-- EEPROM
 +-- Flash
 +-- SMC
 +-- USB
 +-- Interrupts
 +-- Timers
```

## Acceptance criteria

The Xbox boot firmware can initialize MCPX-dependent functionality.

---

# 12. Phase 7 — Firmware and Boot [PARTIAL]

Implement configuration for user-provided firmware files.

Required conceptual inputs:

```text
MCPX boot ROM
Xbox BIOS
Hard disk image
```

Do not distribute copyrighted firmware.

## Boot pipeline

```text
Reset
 ↓
CPU reset state
 ↓
MCPX
 ↓
BIOS mapping
 ↓
BIOS execution
 ↓
hardware initialization
 ↓
storage initialization
 ↓
kernel loading
```

## Debugging requirements

At every stage log:

- EIP
- CR0
- CR3
- EFLAGS
- interrupts
- MMIO accesses
- PCI accesses
- page faults

## Milestone

### M1 — BIOS execution

Success means the C# emulator executes real Xbox firmware far enough to demonstrate correct CPU/memory/basic hardware behavior.

---

# 13. Phase 8 — Storage

## 13.1 ATA/IDE

Implement:

```text
IDE controller
ATA command handling
PIO
DMA where required
device registers
status/error registers
interrupts
```

## 13.2 Hard disk

Support a virtual disk backed by a file.

Avoid loading the entire image into memory.

Use:

```text
random-access file
block cache
read-ahead
write-back where required
```

## 13.3 DVD

Implement a virtual DVD device.

## 13.4 FATX

Implement:

- partition parsing
- boot sector
- FAT
- directories
- files
- timestamps/metadata as needed
- allocation
- read-only mode initially

## 13.5 XDVDFS

Integrate the existing C# XDVDFS implementation where licensing and architecture allow.

Support:

```text
ISO
XISO
raw XDVDFS
```

Test media on this machine (verified 2026-10-11; local paths, not in repo):

```text
H:\Tests\XBOXTest      37 Redump retail XISO .iso files (~263 GB: NTSC-U/Europe/Japan/Australia)
E:\Microsoft Xbox      945 USA retail .chd files (~1.44 TB) + 1403 .dummy placeholders
```

Libraries for them (both pure C#, zero native deps, target
net8.0/net9.0/net10.0 - compatible with this repo's net11.0): NuGet
`XISOSharp` 1.4.2 (purelogiccode's C# extract-xiso implementation;
sibling repo `CSharp_XISOSharp` holds docs + source) for ISO/XISO -
**referenced since §64** (`XGrid.XboxFs/XdvdfsFileSystem` + `xgrid
xdvdfs ls/cat`; 007 validated: DVD_X2, TitleId `0x4541000D`), and NuGet
`CHDSharp` 1.4.3 (pure C# MAME CHD V1-V5 reader, all codecs) for CHD -
**referenced since §66** (`XGrid.Storage/ChdDvdImage`, `boot --dvd`
accepts `.chd` by magic; 007 USA validated, SHA-identical XBE).

## Milestone

### M2 — Xbox filesystem access

The emulator can access a real Xbox filesystem through emulated storage.

---

# 14. Phase 9 — XBE Loader

Implement:

- XBE header parser
- sections
- entry point
- image base
- certificates
- TLS information
- imports/exports as needed
- section mapping
- relocation behavior where applicable
- debug information when present

Create:

```csharp
public sealed class XbeImage
{
    public uint EntryPoint { get; }
    public uint BaseAddress { get; }
    public IReadOnlyList<XbeSection> Sections { get; }
}
```

Create a standalone tool:

```text
XGrid.XbeTool
```

Commands:

```text
xbe info game.xbe
xbe sections game.xbe
xbe imports game.xbe
xbe extract game.xbe
```

## Milestone

### M3 — XBE loading

An XBE can be parsed and mapped into emulated memory.

---

# 15. Phase 10 — Xbox Kernel Interface

This area must be implemented incrementally.

Start with the minimum services required to boot known test software.

Potential categories:

```text
memory management
threads
events
IRQs
filesystem
object manager
timers
device I/O
Xbox kernel exports
```

Do not implement the entire kernel API upfront.

Use tracing to discover missing functionality.

## Strategy

```text
Run
 ↓
unimplemented kernel call
 ↓
trace
 ↓
implement
 ↓
regression test
```

This is preferable to speculative implementation.

---

# 16. Phase 11 — NV2A

This is the second major high-risk subsystem.

## 16.1 Device hierarchy

```text
NV2A
 |
 +-- PBUS
 +-- PFIFO
 +-- PGRAPH
 +-- PFB
 +-- PCRTC
 +-- PVIDEO
 +-- PRAMDAC
 +-- memory interface
```

## 16.2 PFIFO

Implement:

- channels
- push buffers
- DMA objects
- method dispatch
- command parsing

## 16.3 PGRAPH

Implement:

- graphics state
- register state
- surfaces
- textures
- vertex state
- render state
- shader state
- primitive submission

## 16.4 Memory

Handle:

```text
GPU virtual/physical memory
surfaces
textures
framebuffers
depth buffers
```

## 16.5 Renderer abstraction

Do not expose Vulkan types to NV2A emulation.

Use an intermediate representation:

```csharp
public interface INv2aRenderer
{
    void SetState(Nv2aState state);
    void UploadTexture(Nv2aTexture texture);
    void Draw(Nv2aDrawCommand command);
}
```

## 16.6 Vulkan backend

Primary target.

Implement:

- device creation
- swapchain
- command buffers
- descriptor management
- textures
- render targets
- pipelines
- synchronization

## 16.7 OpenGL backend

Optional after Vulkan reaches stable rendering.

## Milestones

### M4 — Framebuffer output [DONE 2026-10-09]

A software rasterizer renders PGRAPH inline-vertex triangles (Gouraud fill,
scissor, A8R8G8B8/R5G6B5) through the full pushbuffer path; the dashboard's
boot triangle extracts as a black→red→green gradient (307K pixels, viewed).

### M5 — 2D graphics [DONE 2026-10-10]

Transform-program interpreter (`VshProgram`, §38), transform
program/constant storage with load-pointer semantics, inline element/array
semantics, vertex attribute fetch (all formats) and the element draw path
through the program to the shared barycentric rasterizer (§39). Ran on the
real dashboard without fault (2670 draws); §42 proved all 10.1M pixels
come from legacy inline draws while program draws are degenerate at this
boot stage — steady-state program draws later validated live (§§57–58:
real shaders execute, zero pixels proven guest-side clip).

### M6 — basic 3D [DONE 2026-10-10]

Per-slot texture state, swizzled (Morton via xemu's mask algorithm) and linear
A8R8G8B8/R5G6B5 sampling with wrap/mirror/clamp addressing, modulate by `oD0`,
texcoords from `oT0` (§40). All 10.1M dashboard pixels sample; §44 later
showed the textures are CPU-uploaded assets, dark by nature (only
`0x3628000` is a true render target).

### M7 — shaders [DONE 2026-10-10]

Fragment pipeline (`ProcessFragment`, §41): alpha test, stencil test with
fail/zfail/zpass ops, depth test/write (Z16/Z24S8, depth mask), full blend
factors/equations with `BLEND_COLOR`, color mask, nop-draw skip. 7 unit tests
with exact pixel/dword expectations. Validation found no live fragments at
this boot stage (`blended=0 depth=0` — the 66 draws using these paths execute
programs that read never-loaded zero constants, degenerate on xemu too, §42;
steady-state programs later validated live in §§57–58).

### M8 — game rendering [DONE 2026-10-10]

Flip tracking + present path (§43): `FLIP_INCREMENT_WRITE` rotates the write
buffer, counts a presented frame and captures the at-flip surface;
`FLIP_STALL` counting; vblank read-advance; `XGrid.Graphics/DisplayFrame`
(BGRA store, 32-bit BMP export); CLI `--frame` saves the presented frame.
Validated: `frames=33 stalls=33` (exact oracle match), 640×480 from
`0x32A4000`, opaque black — the flip chain presents the dashboard surface.

### M9 — Dashboard boot (target: same dashboard xemu boots from `xbox_hdd.qcow2`)

Status 2026-10-11: boot through **interactive menu** is SOLVED
end-to-end (5B steps, no fault): Gate 1, animation (§32), ACI/APU
(§33), fcomp (§34), M5–M8 (§37–43), content-discovery (§45),
file-save (§47–48), shadow alias (§50–51), NIC gates I+II (§52–53),
XID gates I–III (§54–56), D3D probe (§57), oPos/C0/zeta/viewport
verdicts (§§58–61), the NV2A fidelity wave (§71) and — the missing
piece — the **USB daughterboard hub chain** (§69): the guest's XPP
stack now opens the pad and polls `GetState` every frame, D-pad
levels move the menu selection (`obj+0x270` 1→5), and a step-timed
A edge (`--pad-step`) fires the activation writer `0x1859D`
(state 14 -> handler `0x1E66A`; §§69–70). The Xbox PIT frequency is
fixed to 1,125,001 (§68). Definition of done: the real kernel (LLE,
no HLE) loads the dashboard XBE off the HDD (DONE §31), the dashboard
renders its menu through our NV2A path (DONE §95 — textured-RGB-zero
fixed; colored menu, 7,389 RGB-diff px, 32,789 colors, no fault), and
controller input navigates it (DONE at the plumbing level — selection
moves, activation fires; scripted `--pad-script` injection merged §74,
control list decoded §76). Probes EXECUTED (§79): submenu state-100 is state-proven
(`0x1E420`@972M, `FC0→0x5E4560`, no load) but pixel-negative
(black-identical thru 1.6B) — next is the `--pgraph` method-stream
diff (submenu vs neutral) to decide guest-side clip vs rasterizer
gap; idx0 XBMC is D-pad-unreachable (clamps {1..5}); DVD auto-boots
past the menu to the EA splash (mechanism TBD).
007: limiter exited via temp `[AFE34]=2` (reverted); game parks in a
SECOND wait (parked STRM `remaining=0 state=2`, zero writes 1.8B→3B;
exit needs `MPCh` magic or `0x2A830` — §85 suspects user-mode
completion delivery, APC/producer wakeup);
nvnet P0+P1 MERGED (§84: ring latches, IRQ4, TX walker + loopback).
DVD auto-launch TRACED (§83) + media branch PINNED (§86: neither
`0x80030980` nor `0x80021693` is media-dependent — decision is
downstream file-path; decrypt writer is the SAME site `0x8002EFE6`
+ key `0xA8FC57AB`; PacketLog 16→256).
007: APC-suspect REFUTED (§89 — single-threaded guest pipeline
stall: final chunk memcpy'd but never linked, refill silent since
1.6B; no device/CPU fix indicated; unblock = guest-behavior EOS
decision, not correctness); EOS QUIRK DONE (§92: TitleId-gated
STRM 2->0, exits 0x20B50 (EBP handle->2) to the 0x138xx next
gate; 1034 tests);
nvnet P0–P3 COMPLETE (§§84, §88: rings, IRQ4, TX walker, RX
filter/DMA, link/autoneg, socket backend behind `--net-backend`,
`--net-inject`/`--net-link`; 993 tests).
**VIEWPORT BAKE CLOSED (§87) + scene-proof (§93): `C0.z=16777215`
was correct XDK device-depth output — the gap was emulator-side
(missing `oPos.z/clipRange.y` divide; §82's "guest-accurate" verdict
SUPERSEDED). Fix validated live: pixels 10.4M→118.8M, layout proven
RIGHT (frames diverge, different lists, glyph blocks). **MENU COLOR
DONE (§95):** the RGB-zero bug was three coupled texture-sample bugs
(LU_IMAGE texel-unit coords, SZ POT base size, `SZ_A4R4G4B4`
unsupported); live two-boot now shows 7,389 RGB-differing px with a
readable colored EvoX menu. ZCLIP + float depth (§90), foggen + HILO
(§91), exotic modes + HILO 5–7 (§96) and ZCLIP CULL + exact-1.0
(§97) merged with live-identical counters.**
Next gate: 007 audio-drain (`0x20FC1`) — implement the APU voice
processor + DSP (§98). Order matters; do not work on later gaps
before earlier gates pass.

Gap 1 — kernel→storage handoff [DONE 2026-10-09]. The kernel loads the
dashboard: guest sequence matches the xemu oracle through HDD unlock
(`MEF MEF MEC MF2 MEC M91`), the full DVD probe, then 27 `READ DMA (0xC8)`
commands (~828 KB); RAM holds the real EvoX XBE (`XBEH` at phys `0xBF000`,
loader `CMP [ESI],"XBEH"`, cert string). Engine: 8/16/32-bit data port,
dual-drive taskfile, ATAPI protocol details, synchronous BM-DMA engine
(BAR4/PRDT/IRQ). See research-notes §31.

Gap 1b — boot-animation wait [DONE 2026-10-10]. The `0x800426D8` park was the
animation/loading-screen frame pacing waiting on the NV2A notifies page; the
kernel's `NV097_BACK_END_WRITE_SEMAPHORE_RELEASE` (0x1D70) was never written
because the semaphore DMA context is a RAMHT handle, and PGRAPH notifications
(`NV097_NO_OPERATION`) were not raised. Both implemented (semaphore via RAMHT
resolution; notification INTR/NSOURCE/TRAPPED_*; `UpdateIrq` matches xemu's
`pending && enabled`); the loader now runs the full animation and proceeds to
video mode set. See research-notes §32.

Gap 1c — AV-device bring-up [DONE 2026-10-10]. Two more missing MCPX devices
blocked the video-device open after the animation: the **ACI (AC'97) at
0xFEC00000** (the kernel polls CAS bit 0 with a 1000×`StallExecution` timeout;
unmapped all-ones → E_FAIL → NULL video device → BugCheck 0x1E) and the **APU
at 0xFE800000** (a voice-off completion — `SE2FE_IDLE_VOICE` FE message — must
arrive on a VP frame boundary, not synchronously, or the stop method
deadlocks on its own operation counter). `McpxAciDevice` + `McpxApuDevice`
implement both register models. See research-notes §33.

Gap 1d — animation + XBE launch [DONE 2026-10-10]. The remaining NULL D3D
surface crash (`[AV+0x64C]` released by the AV teardown `0x8005BACE` →
`0x800409F5`) was **not** an NV2A gap but a **CPU bug**: `fcomp`/`fcom` memory
operands (`D8`/`DC` forms, decoded as `(ST0, memory)`) were executed comparing
`Operand1` (=ST0 itself), so every `fcomp mem` returned "equal". The
intro-animation tick loop (`0x800579B8` → `0x80057949` → `0x800571AD`) exited
immediately, the animation never ran, and the teardown released the
never-created surface. Fix: compare `Operand2` when present (XboxCpu.cs;
regression tests in `FpuTests`). Now the animation ticks (32 scene updates,
137.4M→488.9M), the teardown no longer faults, and `xgrid boot --steps
700000000` completes **all 700M steps with no fault**. **The dashboard XBE
entry executes**: the header entry field (VA `base+0x128` = `0x10128`) is an
absolute VA (`0x12A17`), XOR-encrypted in the file; the kernel decrypts it
(`xor [0x10128],0xA8FC57AB` at `0x8002EFE6`) and calls it (`call [0x10128]` at
`0x800158DA`); the entry runs at phys `0x5CA17`, resolves its kernel thunks
(`[0x12C4B]=0x8001EEAC`) and starts its init (bit-stream decompressor at
`0x5C4A0`). See research-notes §34. (Init long completed: the dashboard went
on to render (§37–43), discover content (§45) and save files (§46).)

Gap 2 — exact dashboard identity [DONE 2026-10-09]. Both sides agree: the host
FATX track identified softmod `xboxdash.xbe` (770048 B, `EvoX`, entry
`0x12A17`) at partition `0x465400`, and Gate 1's guest RAM forensics proved the
kernel itself DMA-loads that exact image (`XBEH` at phys `0xBF000`, loader
`CMP [ESI],"XBEH"` at `0x2EE98`, `EvoX` cert string). "Same dashboard" claim
is now evidence-backed; no further confirmation needed.

Gap 3 — CPU throughput. Dashboard boot + menu needs billions of guest
instructions; 5B-step batch runs take ~6 min (~14M insn/s Release), so
forensics runs are practical, but interactive rates need JIT (Phase
15) or a much faster interpreter first, benchmark-driven (Phase 20/29).

Gap 4 — NV2A 3D depth (M5–M8) [M5–M8 DONE; visible render DONE (§95)]. M5
(programmable vertex pipeline), M6 (textures), M7 (fragment pipeline)
and M8 (present) are done (§37–43); the §71 fidelity wave closed the M5
gap (inline `SET_VERTEX_DATA*` + `DRAW_ARRAYS`), implemented all 11
XF-context constant writers (c56–c59, c64–95 — correcting §42's wrong
"no-ops on xemu" claim; xemu feeds c58/c59 from the viewport methods),
fixed stride-0/bit31/program-start/output-defaults, and aligned the
interpreter with xemu's GLSL (MUL zero rule, min/max/LIT, paired-ILU,
oPos epilogue) plus perspective-correct varyings and oD0 NaN→1. §57–58
proved live program draws execute real shaders (`oPos=V0*C0+C0`) and
clip guest-side; §95 fixed the textured-RGB-zero bugs and the menu now
rasterizes in color (7,389 RGB-diff px vs neutral). **Register
combiner (psh) merged
(§75):** real combiner methods (ALPHA_ICW `0x260`, SPEC_FOG_CW
`0x288/0x28C`, FACTOR `0xA60/0xA80`, ALPHA_OCW `0xAA0`, COLOR_ICW
`0xAC0`, SPEC_FOG_FACTOR `0x1E20`, COLOR_OCW `0x1E40`, CONTROL
`0x1E60`, SHADER_STAGE_PROGRAM `0x1E70`, DOT_RGBMAPPING `0x1E74`,
OTHER_STAGE_INPUT `0x1E78` — correcting the brief's 0x1940 range,
which is inline vertex data), stage/final evaluation with
combiner-disabled draws pixel-identical. **Fog + dependent/bump/dot
modes done (§78, wave/nv2a-fog-tex):** SET_FOG_COLOR/MODE/GEN/ENABLE/
PARAMS, oFog varying with fog_mask order, per-vertex LINEAR/EXP/EXP2
(+ABS) factor into the FOG register (disabled stays `(0,0,0,1)`);
DPNDNT_AR/GB, DOTPRODUCT/DOT_ST/DOT_ZW/DOT_STR_3D, BUMPENVMAP[_LUM]
with dot-map/sign decode and bump matrix/scale/offset; unknown modes
degrade to NONE with `TexModeFallbacks`. **Viewport bake CLOSED (§87,
wave/viewport-bake):** `C0.z=16777215` reclassified as correct XDK
device-depth output (driver threads the viewport Z scale
`(MaxZ−MinZ)×0xFFFFFF` into its generated programs, exactly as xemu
feeds XFCTX slots 58/59 for fixed-function) — not poison. The gap was
emulator-side: the rasterizer never implemented xemu's `oPos.z /
clipRange.y` (vertex epilogue + fragment store), so device-unit Z
saturated to 1.0. `Nv2aMmio.Vertex` now divides by the surface zeta
maximum (Z16→65535, Z24S8→16777215; fixed-depth only); same-tree 1B
submenu validation: pixels 10444800→118798338, blended 0→45.3M,
frame black→57087 nonzero px, draws/skipped/frames unchanged
(deterministic guest stream). Same wave also fixed the `WriteMasked`
bit order (bit0=w per xemu `mask_str`/Cxbx `MASK_*`) and aligned
`WriteFogMasked` with both references' oFog.x rule. **ZCLIP + float depth DONE
(§90, wave/nv2a-zclip):** `SET_CLIP_MIN/MAX` (`0x394/0x398`) stored as
device-unit float bounds and applied as the fragment-stage clamp xemu
runs in psh (passthrough-until-programmed, so all goldens bit-identical);
`SET_CONTROL0` Z_FORMAT (bit 12) selects the F16_MAX/F24_MAX
(511.9375/1e30) device-depth range with custom-float zeta
decode/encode; CULL-discard and the exact-1.0 fixed-encode overflow
stay open as noted follow-ups. **Fixed-function foggen + HILO DONE
(§91, wave/nv2a-foggen-hilo):** GEN_MODE distances (SPEC_ALPHA/RADIAL/
PLANAR/ABS_PLANAR/FOG_X from v5/v4.a/v0.xyz + c57, used when the program
leaves oFog unwritten) and HILO_1 dot-map decode. **NV2A exotics DONE
(§95, wave/nv2a-exotics):** cubemap sampling (CUBEMAPENABLE faces +
remap2DToCube for flat textures), projective 3D volume sampling,
CLIPPLANE per-component discard (SET_SHADER_CLIP_PLANE_MODE), the
DOT_RFLCT_DIFF/SPEC and DOT_STR_CUBE reflection modes, HILO hemisphere
dot maps 5-7 (Cxbx 16-bit sign decode), and the xemu-equivalent
BRDF/DOT_RFLCT_SPEC_CONST zero-texel stubs; BRDF math remains reference
UNIMPLEMENTED. Remaining: ZCLIP-CULL, right-scene color proof.
**Scene-proof (§93): layout/content
proven RIGHT (diverging frames, different lists, glyph blocks) — color
broken: textured-draw RGB-zero is the new top item (RGB=0 + structured
alpha everywhere; one untextured blue column keeps color; suspect
texture-sample RGB, not global mask).** **§82 verdict
superseded:** the submenu black was emulator-side depth saturation,
not guest-accurate.

Gap 5 — USB/OHCI + controller input (Phase 13) [DONE 2026-10-11]. The
OHCI host controller (`OhciUsbController`, §36/§54) plus the
xemu-faithful USB hub chain (§69: root port 1 -> 4-port daughterboard
hub -> 1.3 -> 3-port controller hub -> 1.3.1 -> XID; RHSC/done-head/
UsbReset fixes) made the guest's XPP stack go live: it opens the pad
(`0x79E99`/`0x76A1F`/`0x76A81` @947–949M) and polls `GetState`
(`0x76C59`) every frame (§69); D-pad levels move the menu selection
(`obj+0x270` 1→5) and a step-timed A edge (`--pad-step`) fires the
activation writer `0x1859D` (§70; A-press now differs from neutral by
23,786 RAM bytes). Scripted `--pad-script` navigate+activate sequences
merged (§74). **007 input verdict (§77): never-reaches, not mismatch**
— no `XInputOpen`/`GetState`, no device fix; unblock its `0x20E40`
limiter first. **007 second wait (§§85, §89):** APC-suspect REFUTED — single-threaded
guest pipeline stall (final 0x2A00 B memcpy'd but never linked;
catch-22 on empty stream; refill silent since 1.6B; force-probe exits
permanently). No device/CPU fix indicated; unblock = guest-behavior
EOS decision. **nvnet P0–P3 COMPLETE (§§84, §88):** TX walker, RX
filter/DMA + RX/RX_NOBUF IRQs, link toggle + 250 ms autoneg +
MIIEVENT, socket user-mode backend (ARP/ICMP/UDP) behind
`--net-backend`, `--net-inject`/`--net-link`; 993 tests. **DVD
auto-launch (§83) + media branch (§86):** `call [0x10128]` handoff
at step 537,926,130 → entry `0x22E35`; file-path decision; `0xAD`
requested once (§66 correction); trace/break worlds diverge ~100M
steps (use break-world steps). **EOS QUIRK MERGED (§§92, §94):**
TitleId-gated STRM 2→0, fires once (opt-out flag); game exits
`0x20B50` with no fault over 1B steps but parks in the `0x138xx`
splash loop (`ESI/EDI=0`, no input, DVD frozen). **§95 DECODED:**
the `0x138xx` EIP is the `0x11210` stream-clock divide helper; the
park is PlayMPC's audio-drain loop (`0x20FC1`) waiting for stream
channel `0x100` to complete. Completion needs advance events from
the XDK audio driver, which need the APU voice-processor/DSP
position feedback that `McpxApuDevice` does not emulate (PIO writes
stored only; status page never updated) — emulator gap, no guest
stall, no quirk warranted. Next: APU VP/DSP streaming wave. Remaining:
memory-unit/storage class USB devices +
first-real-frame validation + TCP proxy.

Gap 6 — APU depth (Phase 12). The register model is implemented
(`McpxApuDevice`, §33) and the front-end handshake/voice-off flow works; DSP,
mixing and real audio output are pending. Non-blocking for boot; dashboard
sounds come later.

---

# 17. Phase 12 — APU

Implement:

1. device registers
2. voices
3. DMA/audio buffers
4. mixing
5. DSP behavior
6. resampling
7. host output

Architecture:

```text
Xbox APU
   ↓
voice processing
   ↓
mixer
   ↓
IAudioBackend
```

Host backend should support:

- Windows
- Linux
- macOS

## Acceptance criteria

- Audio initialization works.
- Known audio test produces expected waveform.
- Long-running audio does not drift excessively.

---

# 18. Phase 13 — USB and Controllers

Implement:

- OHCI host behavior where required
- USB device model
- Xbox controller protocol
- controller state
- rumble

Architecture:

```text
HostInput
   ↓
XboxControllerDevice
   ↓
USB
   ↓
MCPX
```

Support four controllers.

---

# 19. Phase 14 — Networking

Implement only after core gameplay works.

Potential architecture:

```text
Xbox network device
       ↓
emulated NIC
       ↓
INetworkBackend
       ↓
host socket/network API
```

Start with the minimum required for games.

---

# 20. Phase 15 — CPU JIT

This should begin only after the interpreter is stable.

> **Status 2026-10-18: LIVE — governed by
> `docs/research-notes/jit-track-charter.md`.** The interpreter is stable and
> relay-style emission measured wall-neutral (E1–E3, §§171–179b). The
> charter's Phase 1 differs in kind from those slices: whole-block
> straight-line code with register-cached guest state, inlined RAM fast
> paths, block-local flags — under the dual-mode contract (fast mode only;
> the accurate mode stays gate currency). Phases 0–4 + kill criteria live in
> the charter; the architecture sketch below remains the target.
>
> **J1a–J1d landed 2026-10-18 night (§§191–192):** Phase 0 bench
> (`boot --bench`) + a working whole-block engine behind `--jit`
> (`--jit-threshold`, `--jit-exact`). 1B exact-mode runs are bit-exact vs
> the accurate control on both titles and reproduce the golden frames;
> fast mode reaches ~76 Msteps/s at 1B (menu 13.07 s / triangle 13.14 s vs
> 88.05/44.65 s spec-controls; fast mode is never gate currency).
>
> **C3 (Phase 3, raw emitter) started 2026-10-20 (§§199e/199f); correctness
> campaigns closed 2026-10-19→20 (§§199g/§199h/§199i).** The `DynamicMethod` block
> JIT reached its structural floor (~14 ns/ordinary member, ~8 ns/block
> chain, ~15-20 ns/block state sync), so the charter's Phase-3 entry
> condition was met and Iced 1.21.0 (MIT) was added. `RawBlockEmitter` emits
> one native x86-64 function per block: guest GPRs in fixed host registers
> (EAX/ECX/EDX/EBX + R12D-R15D, keeping AH..BH addressable), arithmetic
> flags in the host FLAGS register with IF/DF/IOPL in a control slot, memory
> members running the data translate-cache probe inline against pinned
> pointers (`[rbp+rdi]`) with an exact-helper slow path, and a reverse-P/Invoke
> trampoline to the spec handlers for every unencoded shape — so every block
> is emittable. **Status: opt-in `--jit-raw`; all JitTests green with the
> path forced on (59/59; suite 1497 green); BOTH title 1B differentials are
> bit-exact (menu `660CA11B…/F5D61C94…`, triangle `A2DCA55F…/7DAB63EB…`, every
> JIT counter identical).** The §199h menu gap was the split-member fast-path
> EIP advance (§199i), not a store helper. Seventeen latent emitter bugs
> were fixed across the three campaigns (shadow space, marshalled delegate
> parameters, probe register reuse, stale helper EIP, the translate-cache
> VPN/slot mask, the post-STI/POPF poll, host DF across helper calls, the
> store guard's R9 clobber + exact watch-range test, the exit-edge flag
> merge, the split-member fast-path EIP advance, GC-stale CPU/entries
> pointers, and more — §199g/§199h/§199i). The raw path is now **ahead of IL
> on CPU serving time** after the §199k state hoist (`BuildRawState` once per
> `RunJitBlock` call instead of once per block invocation; the state lives in
> an unmanaged per-CPU block): 100M A/B 9.0% menu / 2.4% triangle faster and
> 1B `outerMs` -3.7% menu / -2.3% triangle, both title differentials still
> bit-exact. End-to-end walls were host-noise bound, so no end-to-end claim
> yet. **Direct block chaining was then implemented and measured (§199l): a
> per-CPU target table with tail-jumps into raw entries, chained
> served/budget accounting, live-generation validation, a cheap PIC pending
> word and an STI gate for `--jit-spin`. It is LANDED (2026-10-20, sixth
> session) after the fault contract was fixed (§199m): a managed exception
> may not unwind out of a raw helper through the hand-emitted frame (no
> unwind data, so CoreCLR cannot walk it - delivery was stack-layout luck,
> which is what the `AluLoop`+`PagedCrossVisitFault` crash exposed). Every
> helper now catches, records (`RawState.Fault`) and returns; the emitted
> block tests the word after each helper call and exits with a sentinel that
> `RunJitBlock` rethrows in managed code, so fault delivery and
> `_jitProgress` are exactly the IL runner's. **1B medians of 3 interleaved
> pairs: triangle wall 26.97 -> 23.72 s (-12.0%, 3/3) / `outerMs` -28.6%;
> menu wall 109.39 -> 95.06 s (-13.1%, 3/3) / `outerMs` -14.2%**, menu SHAs
> the §199i anchor exactly with every counter identical, triangle IL == raw
> on this host (the older `A2DCA55F...` anchor no longer reproduces even on
> pre-patch HEAD - §199l records it). Pair reproducer clean 10/10, CPU suite
> 625/625 x10, full suite 1499 green, ktest XGrid leg byte-identical to the
> accepted baseline at 792,800,535 steps. No default flip is proposed (the
> raw path stays opt-in).

## 20.1 JIT architecture

```text
x86 bytes
   ↓
decoder
   ↓
basic block
   ↓
IR
   ↓
optimizer
   ↓
host code emitter
   ↓
code cache
```

## 20.2 Intermediate representation

Example:

```text
LOAD r1, [EBX]
ADD r1, ECX
SHL r1, 2
STORE [EDX], r1
```

## 20.3 Basic blocks

Compile until:

- branch
- call
- return
- indirect jump
- exception
- page boundary where required

## 20.4 Block cache

Key by:

```text
guest virtual address
CPU mode
relevant execution state
```

Invalidate blocks when guest code changes.

## 20.5 Direct host execution

Investigate later:

- executable memory
- machine-code emitter
- host register allocation
- trampolines
- direct memory mapping

These techniques may require unsafe/native platform support.

## 20.6 Managed JIT options

Evaluate in this order:

1. Expression/IL-based compilation
2. `DynamicMethod`
3. `System.Reflection.Emit`
4. custom machine-code emitter
5. NativeAOT-assisted infrastructure
6. platform-specific executable memory

The final choice must be benchmark-driven.

> **Outcome (2026-10-20).** Option 1/2 is what the fast-mode block JIT uses
> (`--jit`, `DynamicMethod`); options 3 and 5 were not needed. Option 4 was
> taken for C3 with **Iced 1.21.0 (MIT)** for encoding plus a `VirtualAlloc`
> `PAGE_EXECUTE_READWRITE` region per block (option 6, Windows x64 only; every
> other platform keeps the IL runner). Measured negatives that decided this:
> the `DynamicMethod` tier plateaus at ~14 ns per ordinary member, and relay
> emission (E1-E3) was wall-neutral. See
> `docs/research-notes/xgrid-architecture-decisions.md` §§199d-199f.

---

# 21. Phase 16 — CPU Optimization

Optimize in this order:

1. instruction decode
2. memory access
3. branch handling
4. flags
5. block cache
6. register allocation
7. indirect branches
8. MMIO checks
9. TLB
10. synchronization

Avoid optimizing the interpreter after JIT performance becomes the primary path.

---

# 22. Phase 17 — Memory Optimization

Potential techniques:

- direct RAM array
- unsafe references
- spans
- `MemoryMarshal`
- fixed-size pages
- TLB cache
- read/write helpers
- branch reduction
- host virtual-memory mapping where justified

Benchmark every change.

---

# 23. Phase 18 — GPU Optimization
> **Status 2026-10-20 (§205): the phase's own checklist is closed; only measured
> leftovers remain.** Phase 18 asks for seven things to be *measured* and six
> techniques to be *used*. Against this phase's own list (evidence in brackets):
> CPU→GPU command generation [§204 per-draw -81%, §205 batching -47%], GPU
> submission [one instanced draw per primitive flush, 1.05 s per 1B menu steps],
> shader compilation [structure-keyed program cache, 11 programs per 1B],
> texture upload [content-addressed cache + content fingerprint, 13 uploads per
> 1B], render pass creation [single FBO with content-preserving grow blit, 14
> reallocs per 1B], synchronization [per-flip readback opt-in `--frame` only -
> the fast path never syncs] and frame pacing [FLIP_INCREMENT_WRITE /
> FLIP_STALL correct, flip-pacing probe closed as no-bug]; techniques: persistent
> resources, descriptor caching (binding shadows + pre-resolved sample
> addresses), pipeline caching (program cache), texture caching, command-buffer
> reuse (one VAO, per-draw data in uniforms, no per-draw VBO) and async upload
> (n/a on this path: 13 synchronous uploads per 1B, below noise). The score moved
> 48% → 72% because it had been derived from "Track B slices left" rather than
> from this checklist. **What remains (72% -> 100%):** per-draw layer bits
> hybrid/z, per-triangle block packing (~0.8 s/1B), the texture-content
> fingerprint (~0.5-0.9 s/1B), row spans (O6, demoted, ~0.5 s ceiling) and real
> product-path present pacing (the `--window` sink is vsync-off, no pacing) -
> every one measured and deferred, none of them a capability.

> **Status 2026-10-20 (§199p): first Track B slice landed - rasterizer O2
> memory fast lane.** `IGpuMemoryFast` adds a draw-wide GPU-writer batch and
> direct pinned-RAM word reads/writes with exact fallbacks (non-RAM, bus
> watch, unbatched writes); the per-fill decode-page memo drops each page at
> most once (the CPU cannot run mid-draw). New Nv2a differential pins
> fast-vs-legacy framebuffer equality and lane usage; SW goldens/counters
> exact. Quiet-host 3 interleaved pairs: menu 50.01 → 48.99 s (-2.0%, 3/3),
> triangle 12.95 → 12.69 s (-2.0%, 3/3). **O4 landed on top:** the swizzled
> sampler replaces the per-sample `InterleaveBits` loops with the branchless
> Morton spread when the plan masks are the standard alternating ones
> (non-standard masks keep the loop); triangle 12.51 → 11.76 s (-5.9%, 3/3),
> menu 47.90 → 47.33 s (-1.2%, 2/3). Next: inventory O1/O5 (per-pixel
> fragment/counter work); O6/O7 stay filed.

Measure:

```text
CPU → GPU command generation
GPU submission
shader compilation
texture upload
render pass creation
synchronization
frame pacing
```

Avoid excessive GPU/CPU synchronization.

Use:

- persistent resources
- descriptor caching
- pipeline caching
- texture caching
- command-buffer reuse
- asynchronous resource upload where appropriate

---

# 24. Phase 19 — Debugger

Create an integrated debugger.

## CPU

- registers
- flags
- instruction pointer
- disassembly
- step
- step over
- continue
- breakpoints
- watchpoints

## Memory

- hex viewer
- search
- read/write
- mapped regions

## GPU

- NV2A registers
- PGRAPH state
- texture inspection
- framebuffer inspection
- push-buffer trace

## Storage

- FATX browser
- XDVDFS browser
- mounted image information

## Trace

Support:

```text
CPU trace
MMIO trace
PCI trace
NV2A trace
kernel trace
filesystem trace
```

---

# 25. Phase 20 — Automated Testing

## 25.1 Unit tests

Every component gets isolated tests.

## 25.2 CPU tests

Test:

- every instruction family
- flags
- exceptions
- addressing modes
- prefixes
- FPU
- MMX
- SSE

## 25.3 Memory tests

Test:

- alignment
- paging
- TLB
- page faults
- MMIO
- cache behavior

## 25.4 Device tests

Test each device independently.

## 25.5 Golden traces

Create expected traces for known scenarios.

Example:

```text
EIP=00100000
EAX=00000000
...
MMIO WRITE 0xFED00000 = 0x00000001
...
```

Compare emulator output against the golden trace.

---

# 26. Differential Testing

Where legally and technically practical, compare behavior against reference emulators.

Run identical test scenarios:

```text
Test input
   |
   +---- xemu
   |
   +---- xqemu
   |
   +---- XGrid
```

Compare:

- CPU registers
- memory
- interrupts
- MMIO
- GPU state
- output framebuffer

Differences should be investigated, not automatically assumed to mean XGrid is wrong.

---

# 27. GPU Differential Testing

For deterministic test scenes:

```text
Xbox GPU commands
       ↓
xemu
       ↓
reference image
```

and:

```text
Xbox GPU commands
       ↓
XGrid
       ↓
test image
```

Compare:

- exact pixels where deterministic
- tolerance-based pixel differences
- geometry
- texture sampling
- depth behavior

Generate heatmaps for differences.

---

# 28. Compatibility Test Matrix

Create a database:

```text
Game
Title ID
Region
Version
Boots
Menus
2D
3D
Audio
Input
Gameplay
Performance
Known issues
```

Statuses:

```text
Not tested
Boots
Menu
Playable
Playable with issues
Playable
Perfect
```

Never claim compatibility from one successful launch.

---

# 29. Performance Benchmarking

Benchmark separately:

## CPU

```text
instructions/sec
guest MIPS
basic blocks/sec
JIT compile time
JIT cache hit rate
```

## Memory

```text
RAM reads/sec
RAM writes/sec
TLB hit rate
MMIO accesses/sec
```

## GPU

```text
command submission
draw calls/frame
frame time
GPU time
CPU render time
```

## Full emulator

Measure:

```text
FPS
frame time
CPU utilization
GPU utilization
RAM usage
startup time
JIT compilation time
```

---

# 30. Performance Targets

These are project targets, not guarantees.

## Milestone targets

### Interpreter

Target:

```text
10–20% of native/reference emulator performance
```

depending heavily on workload.

### Initial JIT

Target:

```text
50–70%
```

### Mature JIT

Target:

```text
80–100%+
```

The final result must be evaluated against the same:

- game
- resolution
- renderer
- host CPU
- host GPU
- synchronization settings

---

# 31. C# Performance Rules

## Avoid

```csharp
object
dynamic
LINQ
boxing
per-instruction allocations
virtual calls in hot loops
exceptions for normal control flow
```

## Prefer

```csharp
struct
readonly struct
ref struct
Span<T>
ReadOnlySpan<T>
ref
in
Unsafe
MemoryMarshal
ArrayPool<T>
Vector128<T>
Vector256<T>
function pointers
```

Do not blindly use unsafe code. Profile first.

---

# 32. Allocation Policy

The CPU execution loop should ideally perform:

```text
0 allocations/frame
0 allocations/basic block
0 allocations/instruction
```

Use object allocation primarily for:

- initialization
- configuration
- debugger
- logging
- device creation

---

# 33. Logging Architecture

Use structured categories:

```text
CPU
MEMORY
MMIO
PCI
MCPX
NV2A
APU
USB
IDE
FATX
XDVDFS
XBE
KERNEL
INPUT
AUDIO
VIDEO
JIT
```

Levels:

```text
Trace
Debug
Information
Warning
Error
Critical
```

Hot paths must not generate strings unnecessarily.

---

# 34. Configuration

Example:

```json
{
  "system": {
    "bios": "bios.bin",
    "mcpx": "mcpx.bin",
    "hardDisk": "xbox_hdd.img"
  },
  "graphics": {
    "backend": "Vulkan",
    "resolutionScale": 1,
    "vsync": true
  },
  "cpu": {
    "backend": "Jit",
    "jitEnabled": true
  },
  "audio": {
    "enabled": true
  },
  "input": {
    "backend": "Default"
  }
}
```

Do not store copyrighted firmware in the repository.

---

# 35. CLI

Create:

```text
xgrid
```

Commands:

```text
xgrid run game.iso
xgrid run game.xbe
xgrid pause
xgrid reset
xgrid info
xgrid verify
xgrid trace
xgrid benchmark
```

Useful developer commands:

```text
xgrid cpu-test
xgrid xbe-info game.xbe
xgrid xdvdfs-info game.iso
xgrid nv2a-test
```

---

# 36. Avalonia Frontend

The frontend should provide:

- game selection
- firmware configuration
- storage configuration
- graphics configuration
- controller configuration
- audio configuration
- debugger
- logs
- performance overlay
- save-state management if implemented

The frontend must communicate with the emulator through an API, not internal fields.

**Status (2026-10-20): LANDED** as `src/XGrid.Frontend.Avalonia` (commit `ea8b38f`),
an Avalonia 11.3.22 desktop application (Windows/Linux/macOS, `net10.0`, MIT
dependencies) that talks to the emulator only through the public `XGrid.Host`
`XboxMachine` API.

Delivered in this slice:

- dark modern shell with Home, Library, Settings, Activity, and About pages;
- power/pause/resume/stop with a live presented-frame display (`WriteableBitmap`;
  software rasterizer by default, opt-in hidden-window OpenGL backend since §220);
- game selection and recursive library scan (ISO/CHD/XBE, XBE title parsing);
- firmware, storage, RAM, JIT/performance, network, library-folder, and accent
  configuration persisted as JSON in the platform app-data folder;
- keyboard-to-XID pad mapping, activity log, runtime accent themes.

Deferred to later frontend slices: physical gamepad binding, host audio output,
debugger panes, per-title profiles (the renderer is global), save-state UI, and
the GL path for direct-framebuffer guests. Design and validation record:
`docs/research-notes/avalonia-frontend.md`; usage:
`src/XGrid.Frontend.Avalonia/README.md`.

---

# 37. Save States

Do not implement save states early.

When implemented, the emulator must serialize:

```text
CPU
RAM
MMU
device state
GPU state
APU state
timers
interrupt controllers
storage state
```

Save-state versioning is mandatory.

Example:

```text
XGrid Save State
Version 1
Architecture version
Device versions
Compressed state
Checksum
```

---

# 38. Snapshot/Replay System

A valuable debugging feature.

Record:

```text
initial state
input events
timing events
external events
```

Then replay deterministically.

This can make bugs reproducible.

---

# 39. Deterministic Mode

Add:

```text
--deterministic
```

This mode should:

- use fixed timing
- control random sources
- avoid nondeterministic thread scheduling
- serialize external input
- use deterministic event ordering

This is extremely useful for regression tests.

---

# 40. Threading Model

Start with a single emulation thread:

```text
Emulation Thread
       |
       +-- CPU
       +-- devices
       +-- scheduler
```

Host services can run separately:

```text
UI Thread
Audio Thread
GPU submission where safe
File I/O
```

Do not parallelize Xbox CPU/device execution prematurely.

The Xbox is fundamentally a single-core CPU target, and deterministic ordering is valuable.

---

# 41. GPU Threading

Keep Xbox GPU command generation logically synchronized with the emulated CPU.

Host GPU submission can be optimized later.

Avoid:

```text
CPU thread
    ↕
GPU thread
    ↕
blocking synchronization every draw
```

Prefer batching.

---

# 42. Error Handling

Create specific emulator exceptions only for unrecoverable developer/configuration conditions.

Examples:

```text
InvalidFirmwareException
InvalidXbeException
InvalidStorageImageException
UnsupportedInstructionException
GpuStateException
EmulationFatalException
```

Normal guest exceptions must remain part of emulated CPU behavior and must not become host exceptions.

---

# 43. Security

The emulator processes untrusted game images.

Treat all images as hostile input.

Requirements:

- bounds checking
- overflow checking where appropriate
- no arbitrary host path access through guest filesystem APIs
- no uncontrolled memory allocation
- no execution of guest-provided host code
- safe archive handling
- fuzz testing of parsers

Particularly fuzz:

```text
XBE
XDVDFS
FATX
ISO
NV2A command streams
```

---

# 44. Fuzzing

Create fuzz targets for:

```text
x86 decoder
XBE parser
XDVDFS parser
FATX parser
NV2A push-buffer parser
MMIO decoder
```

Requirements:

- no host crash
- no infinite loop
- no unbounded allocation
- no out-of-range memory access

---

# 45. Documentation

Every major subsystem needs:

```text
Purpose
Hardware behavior
Registers
Address ranges
Timing assumptions
Implementation status
Known deviations
Tests
Reference material
```

Example:

```text
docs/nv2a.md

# NV2A

## Overview
## Register map
## PFIFO
## PGRAPH
## Memory
## Texture formats
## Shaders
## Renderer mapping
## Known deviations
## Tests
```

---

# 46. Licensing and Clean-Room Process

Before copying code from any project:

1. Identify its license.
2. Confirm compatibility with the intended XGrid license.
3. Record the source.
4. Record whether code is copied, adapted, or independently reimplemented.
5. Preserve required notices.
6. Avoid copying code where the licensing situation is unclear.

Maintain:

```text
docs/research-notes/sources.md
```

with:

```text
Project
URL
License
Subsystem
Relevant information
Code copied?
Date reviewed
```

---

# 47. Milestone Plan

> **Authoritative ladder (2026-10-20 course correction):**
> `docs/development-direction.md` §3 — **M-A** host GPU present path,
> **M-B** compatibility corpus, **M-C** product playability (audio/pad/save),
> **M-D** xemu-class walls. The M0–M16 list below is the original build plan,
> kept as history; several entries are long since done.

## M0 [DONE] — Repository

- Solution
- CI
- tests
- analyzers
- documentation

## M1 — CPU Core [DONE]

- registers
- decoder
- interpreter
- flags
- exceptions

## M2 — Memory [DONE]

- RAM
- MMIO
- MMU
- TLB

## M3 — Xbox Machine [PARTIAL - bus/PIC/timers/SMBus/EEPROM/SMC/encoder done; GPE/Super I/O pending]

- bus
- interrupts
- timers
- MCPX skeleton

## M4 — BIOS [PARTIAL - MCPX ROM boots, 1BL runs, 2BL reaches its scheduler idle loop]

- firmware mapping
- boot
- BIOS execution

## M5 — Storage

- IDE
- HDD
- FATX
- DVD
- XDVDFS

## M6 — XBE

- parser
- loader
- memory mapping

## M7 — Kernel

- initial Xbox kernel compatibility
- system calls
- threads
- memory
- filesystem

## M8 — NV2A

- registers
- PFIFO
- PGRAPH
- framebuffer

## M9 — Vulkan

- renderer
- textures
- surfaces
- shaders
- 3D

## M10 — Input

- USB
- controllers
- rumble

## M11 — Audio

- APU
- DSP
- mixer

## M12 — JIT

- IR
- block compiler
- code cache
- optimized memory

## M13 — Debugger

- breakpoints
- memory viewer
- CPU viewer
- GPU viewer

## M14 — Compatibility

- game test matrix
- regression tests
- compatibility reports

## M15 — Optimization

- CPU
- memory
- GPU
- synchronization

## M16 — Release

- Windows
- Linux
- macOS
- installer/packages
- documentation

---

# 48. Definition of Done

The project should not be considered production-ready until:

- [x] CPU passes comprehensive instruction tests. (343 CPU tests: integer, control flow, shifts,
  MUL/DIV, strings, system, x87, MMX, SSE1, boot-path instructions)
- [x] MMU passes paging tests.
- [ ] BIOS boots.
- [ ] HDD works.
- [ ] DVD/XDVDFS works.
- [ ] XBE loader works.
- [ ] Xbox kernel compatibility is sufficient for target software.
- [ ] NV2A renders known test scenes.
- [ ] Vulkan backend is stable.
- [ ] Audio works.
- [ ] Controllers work.
- [ ] Debugger works.
- [ ] No known critical memory corruption.
- [ ] Fuzz tests are established.
- [ ] Regression suite runs automatically.
- [ ] Performance benchmarks are reproducible.
- [ ] Windows build works.
- [ ] Linux build works.
- [ ] macOS build works.
- [ ] User-supplied firmware workflow is documented.
- [ ] Licensing documentation is complete.

---

# 49. Recommended First 30 Implementation Tasks

1. [x] Create Git repository.
2. [x] Create .NET solution.
3. [x] Create Core project.
4. [x] Create CPU project.
5. [x] Create Memory project.
6. [x] Create test projects.
7. [x] Configure analyzers.
8. [x] Implement logging.
9. [x] Implement emulator lifecycle.
10. [x] Implement physical RAM.
11. [x] Implement memory read/write.
12. [x] Implement CPU register structure.
13. [x] Implement EFLAGS.
14. [x] Implement instruction decoder.
15. [x] Implement NOP/MOV.
16. [x] Implement stack instructions.
17. [x] Implement arithmetic.
18. [x] Implement branches.
19. [x] Implement CALL/RET.
20. [x] Implement CPU test runner.
21. [x] Implement MMIO abstraction.
22. [x] Implement memory map.
23. [x] Implement paging.
24. [x] Implement TLB.
25. [x] Implement interrupts. (CPU IDT delivery + dual 8259 PIC + PIT IRQ0 done)
26. [x] Implement PCI abstraction.
27. [x] Implement MCPX skeleton. (SMBus AMD756 + EEPROM/SMC/encoder, overlay, boot-script interpreter; real MCPX ROM runs end-to-end)
28. [x] Implement firmware mapping. (flash window/alias + MCPX overlay + reset vector)
29. [x] Implement CPU instruction tracing. (`xgrid boot --trace/--trace-regs/--trace-start/--dump` disassembly + register trace)
30. [~] Attempt first BIOS boot. (MCPX ROM -> 1BL -> 2BL reaches its scheduler idle loop; runs 300M instructions with interrupts enabled — timer/device event delivery under investigation)

Completed beyond the original list: shifts/rotates, MUL/IMUL/DIV/IDIV, string instructions
(REP/REPE/REPNE), system instructions (CPUID/RDTSC/RDMSR/WRMSR/SETcc/CMOVcc/MOV CR-DR/LGDT/LIDT/
INVLPG/ARPL/BSWAP/LEAVE/ENTER/WBINVD/INVD/0F 00 group/`FF /6`/`8F /0`), the full x87 FPU
(loads/stores/arithmetic/compare/transcendentals/env+state save), MMX, SSE1 (with FXSAVE/FXRSTOR/
LDMXCSR/STMXCSR), the full x86 MMU (4 KB/4 MB paging, software TLB, `#PF`, CR0/CR3/CR4 TLB flush,
paging-aware GDT/IDT access), the I/O port space with IN/OUT, PCI bus/config/BARs, LPC routing and
ROM overlay control, dual 8259 PIC with CPU interrupt injection, the 8254 PIT, and the AMD756
SMBus with EEPROM/SMC/encoder devices.

Do not begin NV2A before the CPU/memory/boot foundation is stable.

---

# 50. Recommended First Prototype

The first useful prototype should be extremely small:

```text
XGrid
 |
 +-- CPU
 |    +-- decoder
 |    +-- interpreter
 |
 +-- Memory
 |    +-- 64 MB RAM
 |
 +-- MMIO
 |
 +-- Interrupt controller
 |
 +-- BIOS loader
 |
 +-- CLI
```

Target:

```text
Reset
 ↓
CPU starts
 ↓
BIOS mapped
 ↓
x86 instructions execute
 ↓
MMIO accesses appear in trace
```

This is a much better first milestone than trying to boot a commercial game.

---

# 51. Long-Term Architecture

The intended final architecture is:

```text
                           XGrid
                              |
        +---------------------+---------------------+
        |                     |                     |
      Frontend              Debugger              CLI
        |                     |                     |
        +---------------------+---------------------+
                              |
                         Emulator Core
                              |
       +----------------------+----------------------+
       |                      |                      |
      CPU                   Memory                  Bus
       |                      |                      |
   +---+---+             +----+----+          +-----+-----+
   |       |             |         |          |           |
Interpreter JIT         RAM       MMU       PCI         IRQ
                                                |
                     +--------------------------+----------------+
                     |             |             |              |
                    MCPX          NV2A           APU          Storage
                     |             |             |              |
                   USB          Renderer         Audio      IDE/DVD
                                   |                             |
                            +------+-----+                  +----+----+
                            |            |                  |         |
                         Vulkan       OpenGL              FATX     XDVDFS
```

---

# 52. Final Development Strategy

The project should be approached as three parallel tracks.

## Track A — Compatibility

```text
CPU
 ↓
Memory
 ↓
MCPX
 ↓
BIOS
 ↓
Storage
 ↓
XBE
 ↓
Kernel
```

## Track B — Graphics

```text
NV2A
 ↓
PFIFO
 ↓
PGRAPH
 ↓
Vulkan
 ↓
Shaders
```

## Track C — Performance

> **Status 2026-10-22 very late (§239/§240): the code-volume question closed
> negative and the device stage paid -10.6% on both headline legs.** §239 turned
> the raw emitter's per-member EIP/TSC/served bookkeeping (11 emitted instructions
> per member) into one static publish per edge: the engine moved 6.3 → 6.1
> ns/member and the wall not at all, so the raw runner is **latency-bound, not
> instruction-bound** and C's CPU-side lever is *edge* removal. §240 measured the
> device stage for the first time (1.31 s of 6.1 s: 766 ms draw flushes incl.
> 413-435 ms of interpreted vertex program, ~540 ms surface clears) and cut the
> clear half by filling 32-bit rects through `Span<uint>.Fill`: device time
> **1,306 → 816 ms**, **triangle `gl+replace` 6.31/6.43/6.34 → 5.61/5.74/5.73 s
> (-10.6%, 3/3)**, **menu 22.43/20.76 → 19.98/18.82 s (-10.6%/-9.4%)**, every 1B
> SHA/counter/frame, both accurate goldens and the ktest log byte-identical.
>
> **Status 2026-10-22 late (§237/§238): two exact slices, -8.9% / -3.4%.** (1)
> Instruction fetches from immutable image windows (flash/BIOS, its mirror, the
> MCPX ROM overlay) are a decode-cache class of their own and are served by native
> blocks: `nonRamSkips` 359,476 → 0, one-instruction non-JIT runs 434k → 134k.
> (2) The non-JIT run slice `JitSpecSlice` went 256 → 64 after its first sweep
> (`nonJitInstructions` 5.48M → 2.62M; `nonJitMs` keeps falling to slice 4 but
> formation eats it). Measured together: **triangle `gl+replace` 6.672 → 6.081 s
> (-8.9%)**, menu 20.456 → 19.762 s (-3.4%), 100M boot phase 2.276 → 1.813 s
> (-20.3%), every 1B SHA/counter/frame byte-identical. §237 reprized the
> "superblock for the boot" lever out (the ROM share of the non-JIT population is
> 9%). The next C item at that point was a priced `RunSpecBlock` handoff probe;
> §238 rejected it and §240 then won the device stage (see the status above).
>
> **Status 2026-10-22 (course correction): Track C leads again.** Operator:
> xemu-class performance or better is the long-run objective. The present path
> is done and the engine is ~17 s of the 24.3 s menu `gl+replace` wall / dominant
> on the 7.5-7.9 s triangle wall. The 2026-10-20 "Track B/D owns the end-to-end
> gap" note is superseded; the new program is **C4** flat memory model/soft TLB,
> **C5** trace/superblock formation, **C6** device/MMIO fast path, with the C7
> reassessment. `docs/development-direction.md` §0 is canonical; the charter's
> Phase 3b has the stage table. The C3 status blocks below are history.
>
> **Status 2026-10-18: the JIT stage below is chartered as the fast-mode
> track** — `docs/research-notes/jit-track-charter.md` (dual-mode contract,
> phases 0–4, kill criteria). Interpreter-track wins are captured
> (decode-cache frontier §§131/134/137; shape slices §§139–170; rasterizer
> campaign §§180–187); the codegen tier is the authorized next step.
>
> **Status 2026-10-20: the codegen tier is live and the raw emitter is
> running.** `--jit` (whole-block `DynamicMethod`, Phase 1) is at its
> structural floor; C3 (Phase 3, native x86-64 via Iced) is implemented behind
> opt-in `--jit-raw` and validated: all JitTests green with the path forced
> on, **both 1B differentials bit-exact** (triangle and menu, §199i). Baseline
> to beat: fast-mode 1B **triangle 10.63 s / menu 45.62 s** (this session's
> quiet-host calibration: triangle 10.20 s, menu ~40 s). The raw path is now
> ahead of IL on CPU serving time (§199k state hoist: 9.0% menu / 2.4%
> triangle at 100M, 1B `outerMs` -3.7% / -2.3%, differentials bit-exact);
> end-to-end walls remain host-noise bound. **Direct raw chaining is LANDED
> (§199l) on top of the §199m fault-contract fix** (helpers never let a
> managed exception unwind through the hand-emitted frame): 1B medians of 3
> interleaved pairs are **triangle wall 26.97 -> 23.72 s (-12.0%, 3/3) /
> `outerMs` -28.6% and menu wall 109.39 -> 95.06 s (-13.1%, 3/3) /
> `outerMs` -14.2%** with both title SHAs and every counter identical to
> the IL control. **The post-landing breakdown (§199n) re-aimed the target:**
> the software rasterizer is now the largest single item (48% menu / 39%
> triangle of the sw wall; `gl+replace` legs 58.9 s / 13.5 s), block mechanics
> are cheap (1.6-4.6M invocations, 109-158M chain jumps over 450-830M
> members), and the CPU path costs 18.5 ns/member (triangle) / 61 ns
> (dashboard) - the dashboard premium is the helper-routed shape mix plus
> per-member bookkeeping. Remaining fast-mode order: **C3-x hot-shape native
> encoding (menu 61 -> ~30 ns/member) -> C3-y bookkeeping trim -> C3-z inline
> TLB/RAM page pair**, then Track B leads (rasterizer structure, `gl+replace`
> per-frame path, present pacing). Reporting moves to ns/served member +
> `gl+replace` 1B wall + wall per frame; the outstanding operator decision
> stays the `--jit`/`--jit-raw` default flip. Redirect criterion: dashboard
> still >15x from real time with GL after C3-x/y/z -> codegen stops.
>
> **Status 2026-10-20 (seventh session): C3-x hot-shape native encoding
> LANDED (§199o).** The new `--raw-shapes` tally ranked the raw mix and the
> emitter now natively encodes stack/call/ret, memory movzx/movsx, integer
> unaries/`cdq`/`setcc`, shifts/rotates (incl. CL with the ShiftOps OF rule),
> x87 load/store/arithmetic/compare/constants, MMX movq/movntq, scalar SSE
> (arith/movss/cvt) and prefetch-as-nop. **Helper-routed members at 1B fell
> 24.65M -> 1.81M (triangle) and 122.65M -> 5.57M (menu)**, with both title
> SHAs and every counter exact against the IL anchors. Two bugs fixed en
> route: register-form FPU members advanced EIP twice, and the IL runner
> advanced EIP for handler-backed kinds (`JitPlanner.HandlerOwnsEip`).
> Suite 1510 green, ktest smoke PASS 7/7, format clean. Quiet-host 1B A/B
> vs the pre-C3-x commit (3 interleaved pairs, identical SHAs):
> **`--gpu sw` triangle 14.74 -> 13.14 s (-10.8%), menu 52.20 -> 48.43 s
> (-7.2%); `--gpu gl --gpu-replace` triangle 7.56 -> 6.49 s (-14.1%) at
> 11.9 -> 8.8 ns/member, menu 34.29 -> 31.56 s (-8.0%) at 36.7 -> 31.6 ns.**
> **C3-y was implemented bit-exact, measured NEUTRAL and reverted** (5 pairs:
> wall 7.048 -> 7.015, `outerMs` 4326 -> 4314); a 1024-slot data translate
> cache was likewise neutral and reverted. The new `XGRID_RAW_PROFILE`
> tally shows the menu is ~50% load/store + 14% loop dec/inc, so its premium
> is the memory probe's dependent-load chain, not helper dispatch. The flip
> probe is closed as no-bug. **Track B/D owns the end-to-end gap now**; the
> remaining CPU helper tail (`mul/imul`, string `movs`, high-byte `mov`,
> `fnstsw`, carry-rotates) is small.

```text
Interpreter
 ↓
Profiling
 ↓
IR
 ↓
JIT
 ↓
Block cache
 ↓
Memory optimization
 ↓
GPU optimization
```

Do not let Track C block Track A.

A slow but correct emulator is useful.

A fast emulator that cannot boot is not.

---

# 53. Strategic Recommendation

The strongest implementation strategy is:

```text
xemu
    ↓
Xbox hardware knowledge

xqemu
    ↓
standalone CPU/JIT + simpler device models

Cxbx
    ↓
XBE/HLE/direct execution research

Existing C# Xbox tooling
    ↓
XDVDFS/XISO/storage

             ↓

        XGrid
             ↓
     Native C#/.NET Xbox
          emulator
```

The project should initially target **correctness and boot compatibility**, then introduce the JIT only after the interpreter has enough coverage to serve as a reliable reference implementation.

The interpreter should remain permanently available as a debugging/reference backend even after the JIT becomes the default.

---

# 54. First Major Success Criterion

The first major project goal should be:

> Boot a legally obtained/user-supplied Xbox BIOS and execute it correctly through the C# CPU, memory, MMU, interrupt, MCPX, and basic bus implementation.

The second major goal:

> Load an XBE from a user-supplied Xbox storage image and reach executable Xbox code.

The third:

> Render a deterministic NV2A test scene through Vulkan.

Only after these three goals should the project be considered ready for serious game compatibility work.

---

# 55. Suggested Project Identity

Working name:

**XGrid**

Possible alternatives:

- XboxSharp
- XSharpEmu
- XBoxEmu.NET
- Xemu.NET
- XemuCS
- XboxCore
- XCore

The project name should be reviewed for trademark/confusion concerns before public release.

---

# 56. Final Checklist

## Architecture

- [x] Core independent of frontend
- [ ] Hardware independent of host graphics API
- [ ] CPU backend abstraction
- [ ] Graphics backend abstraction
- [ ] Audio backend abstraction
- [ ] Input backend abstraction
- [ ] Storage backend abstraction

## CPU

- [x] x86 decoder
- [x] interpreter
- [x] MMU
- [x] TLB
- [x] FPU
- [x] MMX
- [x] SSE
- [ ] JIT
- [ ] block cache
- [ ] profiling

## Xbox hardware

- [x] MCPX
- [x] SMBus
- [x] EEPROM
- [~] Flash
- [x] PCI
- [x] interrupts
- [x] timers
- [ ] IDE
- [ ] USB
- [ ] APU
- [ ] NV2A

## Storage

- [ ] FATX
- [ ] XDVDFS
- [ ] XISO
- [ ] HDD
- [ ] DVD
- [ ] XBE

## Graphics

- [ ] PFIFO
- [ ] PGRAPH
- [ ] textures
- [ ] surfaces
- [ ] shaders
- [ ] Vulkan
- [ ] optional OpenGL

## Developer tools

- [ ] debugger
- [ ] trace viewer
- [ ] XBE tool
- [ ] XDVDFS tool
- [x] CPU test runner
- [ ] GPU test runner
- [~] benchmarks

## Quality

- [x] unit tests
- [x] integration tests
- [ ] differential tests
- [ ] fuzzing
- [ ] deterministic mode
- [ ] regression suite
- [ ] compatibility database

## Release

- [x] Windows
- [ ] Linux
- [ ] macOS
- [~] documentation
- [ ] licensing review
- [ ] firmware/user-data documentation
- [ ] performance documentation

---

# Conclusion

XGrid should not be treated as a direct port of xemu.

It should be a clean, modular C# implementation of the original Xbox hardware model, informed by xemu, Cxbx, public technical documentation, reverse-engineering research, and controlled differential testing.

The most important architectural decision is to keep:

```text
CPU
Memory
Xbox hardware
Storage
GPU emulation
Host graphics
Frontend
Debugger
```

as separate layers.

The most important performance decision is to implement:

```text
Interpreter → Reference implementation

JIT → Production CPU backend
```

rather than attempting to write the JIT first.

The most important compatibility decision is to implement the Xbox machine incrementally and use deterministic tests and differential traces throughout development.

The ultimate target is a genuinely independent:

```text
C# / .NET
        +
Pentium III emulation/JIT
        +
Xbox hardware emulation
        +
NV2A
        +
Vulkan
        +
XDVDFS/FATX
=
Native Xbox emulator
```

---

# PART II — DETAILED XGRID IMPLEMENTATION PLAN

This part turns the phases above into a concrete, file-level plan. It is the working checklist.
Every phase lists: goal, prerequisites, tasks, files/interfaces, tests, acceptance criteria, risks,
and the reference notes to consult. Phases are ordered; do not start a later phase on an unstable
earlier one except where explicitly parallel (Track B/C).

## Implementation status (last updated 2026-10-21; wave-15 concluded, tools/wave retired; XboxWiki findings integrated below)

| Phase | Status | Notes |
|---|---|---|
| 0 — Project Bootstrap | **Done** | `XGrid.sln`, 16 src + 5 test + benchmark projects, `Directory.*`, CI, scripts, docs |
| 1 — Emulator Core | **Done** | `Emulator` lifecycle, `EmulatedClock`, `Scheduler`, configuration + AOT-safe JSON loader |
| 2 — Memory System | **Done** | `PhysicalMemory`, `Endian`, `AddressSpace` (regions/priority/aliases), `IMmioDevice`, RAM benchmark; flash/MCPX/PCI-hole regions register in their phases |
| 3 — x86/Pentium III CPU | **Done** | Tiers 1–10 done (integer, control flow, shifts/rotates, MUL/DIV, strings, system incl. CPUID/RDTSC/MSR/SETcc/CMOVcc, x87, MMX, SSE1) plus boot-path instructions (WBINVD/INVD, ARPL, BSWAP, LEAVE, ENTER, 0F 00 group, `FF /6`, `8F /0`, INS/OUTS, XADD/CMPXCHG, BSF/BSR, BT/BTS/BTR/BTC, AAM/AAD with #DE on base 0 and QEMU-verified flag semantics); exceptions #DE/#UD/#PF/#BP; x87 arithmetic uses double-precision rounding (documented limitation). Fixed 2026-10-10: `FCOM`/`FCOMP` memory operands (`D8`/`DC`) compared ST0 with itself instead of memory (research-notes §34) |
| 4 — x86 MMU | **Done** | 4 KB/4 MB paging, software TLB (large pages always walk), A/D bits, WP, `#PF` error codes, MOV CR/DR, INVLPG, CR0/CR3/CR4 TLB flushes, paging-aware GDT/IDT access |
| 5 — Bus & Interrupts | **Partial** | I/O port space + CPU IN/OUT, PCI bus (0xCF8/0xCFC, BAR sizing, command, secondary-bus routing, interrupt raise + lower events), host/AGP bridges, LPC routing (INT/PIRQ/ACPI bytes) + ROM overlay + ACPI PM, dual 8259 PIC + CPU interrupt injection, PIT 8254 (verified firing at ~1060 Hz during real boot), MC146818 RTC (ports 0x70/0x71, periodic IRQ8); PCI devices with xemu IDs — OHCI USB0/USB1 (`OhciUsbController`, 0:2.0/0:3.0, `0xFED00000`/`0xFED08000`, IRQ1/IRQ9) with a synchronous schedule engine + the xemu-faithful USB daughterboard hub chain and `XidGamepad` behind it (§§54–55, §69) + latched and step-timed `--pad` injection (§56, `--pad-step` §70 - XPP; **TM2 (2026-10-24): the 8259 now models the edge/level control register (0x4D0/0x4D1, master 0xF8 / slave 0xDE masks) and the xemu 0.8.139 `i8259.c` level semantics - a level line's request survives the acknowledge and the EOI re-presents it; the guest programs ELCR 0x68/0x18 for the PCI lines (NV2A IRQ3, APU IRQ5, ACI IRQ6, USB IRQ11/12), which unblocked 007's APU ISR (decisions §253)**
           opens the pad and polls it live) + scripted `--pad-script` sequences (§74); nvnet MMIO (`McpxNvNetDevice`, `0xFEF00000`, idle/link-up/MDIO, §52–53); APU MMIO in `XGrid.Audio`, ACI in `XGrid.Bus/Aci`; bus-1 NV2A; IDE corrected to nForce 10DE:01BC; SMBus completion IRQ wired end-to-end (device raise/lower → ACPI byte 1 → PIC, xemu `amd_smbus.c` parity); CLI prints PIC/PIT/interrupt/IDE/PCI/MMIO/SMC/SMBus/NV2A/USB/NVNET diagnostics (USB `ctl/intr/addr/cfg` + transfer/request logs, NVNET `reads/writes`, 1MB unmapped buckets + top pages, EIP-tagged 250M progress); DMA helper pending |
| 6 — MCPX | **Partial** | AMD756 SMBus controller (exact status/protocol semantics + xemu-parity IRQ raise on completion with `HCYC_EN` / lower on status write + transaction diagnostics with 192-entry history), 24C02 EEPROM (user-supplied image), SMC (version/challenge/scratch, `TRAYCLOSED` set at realize and `SHORT_ANIMATION` scratch default per xemu), Conexant CX25871 encoder stub, **MCPX ACI (AC'97) register model at 0xFEC00000, xemu `ac97.c`-faithful NAM/NABM (`McpxAciDevice`)** + **APU at 0xFE800000 (`McpxApuDevice`, §33)**; the real MCPX 1.0 ROM executes end-to-end (boot-script PCI/SMBus/MMIO interpreter, decrypt loop); flash AMD/SST command set, GPE/GPIO, Super I/O pending |
| 7 — Firmware & Boot | **Partial** | `XboxMachine` (RAM/aliases, flash window + 16 MB alias, MCPX 512-byte overlay with disable/reset semantics), BIOS loader, `xgrid boot` CLI debugger (`--trace/--trace-regs/--trace-start/--dump/--dumpv/--scan/--break/--break-log/--watch/--ramdump/--ramin/--fb/--frame/--pgraph/--pgraph-max/--writedump/--pad-buttons/--pad-analog/--pad-sticks/--pad-step/--pad-script`, 250M-step progress with EIP trail, `ulong` steps; `xgrid sector` raw LBA dump); real MCPX 1.0 ROM -> 1BL -> 2BL -> kernel executes (LLE). **Kernel cold-boot init completes and the system runs menu-idle at 5B steps with no fault** (EIP `0x2D3B9`, live dashboard render loop, configured USB pad + link-up NIC, black degenerate flips; §§51–61): SMC handshake, PCI setup, RTC, NV2A driver, HDD unlock, DVD probe, 27 READ DMA (~828 KB), EvoX XBE in RAM. The animation wait (§32), ACI/APU gates (§33) and the CPU `fcomp` bug (§34) are resolved; the boot runs the full intro animation, **executes the dashboard XBE**, renders through NV2A (M5–M8, §37–43), discovers content (§45) and re-saves `C:\evox.ini` (§47) — then runs its menu loop. The Xbox PIT frequency is fixed to 1,125,001 (§68, pump = 10 guest-ms) and the full input path is live (§§69–70): the USB hub chain unblocks XPP, the dashboard opens the pad, polls `GetState` every frame, D-pad levels move the menu selection and a            step-timed A edge (`--pad-step`) fires the activation writer `0x1859D`. See M9 + research-notes §31-34, §51-61, §68-72, §74 |
| 8 — Storage | **Partial** | `XGrid.Storage`: `IDiskImage` + `RawDiskImage` + read-only `Qcow2DiskImage` (v2/v3, L1/L2, zero-flag clusters return zeros per QEMU `qcow2.h`) + `OverlayDiskImage` (in-memory copy-on-write; used by `AttachHdd` so guest WRITE DMA cannot modify the shared reference image); ATA/ATAPI `AtaDevice` (IDENTIFY, READ/WRITE SECTORS + DMA `0xC8`/`0xCA`, SET FEATURES, FLUSH, SECURITY UNLOCK, ATAPI PACKET + REQUEST SENSE, 8/16/32-bit data port), `IdeController` (nForce 10DE:01BC per xemu `#ifdef XBOX`, PCI 0:9.0, legacy ports 0x1F0/0x3F6, IRQ14 via LPC routing, dual-drive taskfile HDD master + ATAPI DVD slave, synchronous bus-master DMA: BAR4 window, PRDT walk through `IGuestMemory`, IRQ ack); `XGrid.XboxFs/FatxFileSystem` (read-only XATF) + `xgrid fatx ls/cat`; guest kernel loads the dashboard exclusively via READ DMA (research-notes §30–31) and the dashboard later issues WRITE DMA `0xCA` to LBA `0x80000` (§35); host-side XDVDFS reader done (`XGrid.XboxFs/XdvdfsFileSystem` over NuGet `XISOSharp` 1.4.2 + `xgrid xdvdfs ls/cat`, §64); guest DVD reads served (`IDvdImage`/`IsoDvdImage`, `boot --dvd`, TUR/capacity/READ10/12, §65, validated: kernel probes real sectors, no auth traffic); compressed CHDs (`ChdDvdImage` over CHDSharp 1.4.3, magic dispatch in `OpenDvdImage`, §66) |
| 9 — XBE Loader | **Partial** | `XGrid.Xbe/XbeImage` (host inspection parser: header/cert/sections/TLS/libraries from Cxbx offsets, per-type XOR decode, strict bounds validation) + `xgrid xbe info` (raw file or via FATX); verified against the real dashboard (`EvoX`, retail, entry `0x12A17`, 1 section); guest-side verify/relocate/section-map/entry-call pending (the XBE launch gate — the kernel maps the dashboard at base `0x10000`, image phys `0x5A000`, entry VA `0x22A17`; the guest runs the full dashboard lifetime — rendering, discovering, saving, menu-idle render loop at 5B steps, EIP `0x2D3B9`; see M9 Gap 1d, §§51–61) |
| 10 — Kernel Interface | Not started | LLE-first: only needed if the dashboard's kernel-thunk imports require host-side ordinals; guest kernel is real and running |
| 11 — NV2A | **Partial** | `XGrid.Nv2a/Nv2aMmio`: 16 MB BAR0 window at 0xFD000000 wired in `XboxMachine`; xemu-faithful blocks/defaults; PFIFO pusher (CACHE1 parse, DMA jump/call/return/inc, RAMHT, GET writeback, ERROR+suspend), PGRAPH recording sink + software rasterizer (Gouraud triangle renders through the full pushbuffer path, 307K px, M4 done); surface/clear/flip tracking, PRMCIO retrace, 60 Hz VBLANK; semaphore release via RAMHT DMA object (`0x1D70`) and `NV097_NO_OPERATION` PGRAPH notification (`0x100`) unblocked the boot-animation wait (§32); **M5 done (§37–39):** transform-program/constant storage, inline element semantics, `VshProgram` interpreter (MAC/ILU, muxes/swizzles/masks, paired ops, A0-relative constants), vertex attribute fetch (all formats) and the element draw path — ran on the dashboard without fault (2670 draws; §42: pixels all inline, program draws degenerate); **M6 done (§40):** per-slot texture state, swizzled (Morton) + linear A8R8G8B8/R5G6B5 sampling with wrap/mirror/clamp, modulate by `oD0`, texcoords from `oT0` (all 10.1M px sample; §44: CPU-uploaded assets, dark by nature). **M7 done (§41–42):** alpha/stencil/depth/blend/mask fragment pipeline; validation fixed two interpreter bugs (R/V/C mux rotation, ORB polarity) and proved exact pixel accounting (all 10.1M px from 33 legacy fullscreen inline draws; ~2637 program draws degenerate at this boot stage, 0 px — steady-state program draws validated live in §§57–58). **M8 done (§43):** flip present events with at-flip surface capture, BGRA snapshot, `XGrid.Graphics/DisplayFrame` BMP export, CLI `--frame` (`frames=33 stalls=33`, 640×480 `0x32A4000`, opaque black). Content discovery (§45) + file-save (§46–48) done; menu-idle render loop at 5B (EIP `0x2D3B9`, double-buffer `0x3D00000↔0x3E2C000`, colored since §95) with viewport-baked `C0.z` clip proven guest-side (§§57–61). **§71 fidelity wave:** inline `SET_VERTEX_DATA*` + `DRAW_ARRAYS` (M5 gap closed), all 11 XF-context constant writers (c56–c59, c64–95 — fixes §42's wrong "no-ops on xemu" claim), stride-0/bit31/program-start/output-defaults, MUL zero rule + GLSL min/max/LIT + paired-ILU + oPos epilogue, perspective-correct varyings + oD0 NaN→1. Register combiner (psh) merged (§75: real methods 0x260/0x288/0xA60/0xA80/0xAA0/0xAC0/0x1E20/0x1E40/0x1E60/0x1E70/0x1E74/0x1E78 — the brief's 0x1940 range was inline vertex data, corrected; combiner-disabled draws pixel-identical). Fog + dependent/bump/dot
modes merged (§78). ZCLIP + float depth (§90), foggen + HILO (§91),
exotic modes + HILO 5–7 (§96) and ZCLIP CULL + exact-1.0 (§97)
merged; **textured-RGB-zero FIXED (§95): LU texel-unit coords, SZ POT
base size, `SZ_A4R4G4B4` decode — menu renders in color (7,389
RGB-diff px vs neutral, 32,789 colors)**. Remaining: mipmap LOD + DXT
decode, PROJECT2D non-cube divide, DOT_STR_3D dim==3, border/mip/
filtering, TEXADDRESS V bit, BRDF detail, Vulkan/OpenGL backends
(16.6–16.7) |
| 12 — APU | **Partial** | `XGrid.Audio/McpxApuDevice`: 512 KB window at 0xFE800000, xemu `apu.c`/`vp.c`-faithful register model (ISTS/IEN + `update_irq` gating, FECTL traps, FEDECMETH/FEDECPARAM FE messages, FEMEMADDR/FEMEMDATA magic write, VP PIO_FREE=0x80, VOICE_ON/OFF, voice-list regs), notifier/voice-state writes to guest RAM, frame-quantized `SE2FE_IDLE_VOICE` delivery (`Advance`, 256 samples @ 48 kHz), PCI IRQ wired to PIC; VP voice processor DONE (§107: PIO methods, guest-RAM voice state, SSL advance, notifiers + idle voice; 007's PlayMPC streams complete through the real advance callback — the `0x20FC1` drain gate PASSED). Remaining: DSP cores (register-only; not yet needed), mixing/resampling, host output |
| 13 — USB & Controllers | **Partial** | OHCI schedule engine (control/bulk/periodic, done-queue/WDH, §54), xemu-faithful USB hub chain (4-port daughterboard hub -> 3-port controller hub -> XID, §69), `XidGamepad` with latched + step-timed `--pad` injection; **the guest's XPP stack now opens the pad and polls `GetState` every frame; D-pad navigates the menu, A-edge activation fires `0x1859D` (§§69–70)**; scripted `--pad-script` sequences (§74). Remaining: memory units/USB storage class, more device types |
| 14 — Networking | Partial (MMIO + engine) | nvnet MMIO + per-reg histograms (§52–53, §62) + P0/P1 packet engine (§84: ring latches, IRQ4, TX walker + loopback backend, CLI counters) + P2/P3 (§88: RX filter/DMA + RX/RX_NOBUF IRQs, link toggle + 250 ms autoneg + MIIEVENT, socket user-mode backend behind `--net-backend` with loopback default, `--net-inject`/`--net-link`, 30 new tests); guest init-only steady state preserved, 986 tests |
| 15–23 | Partial | JIT slices landed (profiler §128, fetch-fast §131, decode cache default-on §§134/137, block executor §136, semspec slices §§139–170, memory-path slices §§161–165, E1–E3 emission trial wall-neutral §§171–179b); **fast-mode JIT track §§190–199o** (`--jit` DynamicMethod engine 99% served, ktest 3.76×; C2/C3 raw emitter `--jit-raw` bit-exact on both titles at 1B, direct chaining, C3-x hot shapes — 8.8/31.6 ns per served member; C3-y/xlat/flip probes closed neutral); **rasterizer campaign §§180–187 and Track B §§199p/§200** (raster menu 24.0→13.3-15.1 s, triangle 6.19→3.31-3.45 s, no longer the dominant wall; all SHAs exact); V1 snapshot framework (§§99–101); GL backend slices 1–3 (§§113,117,122); `--window` present (§184); **Track B §204 present path + §205 draw-command batching** (GL submission 6.87 → 1.05 s, per-flip readback opt-in, 59,437 instanced draws replace 2.76M single draws, both 1B GL frames byte-identical); **post-§205 product/finish slices §§206–225** (XInput pad + save states §206, display model §208, cubemap/TEXFILTER §§209/210, fixed-function vertex + lighting §§213/215, PTIMER §216, per-title overrides + EEPROM §217/§219d, GL sampler parity + replace-mode composition §§219/220, the GPU renderer as a frontend setting §§221/223, CHD title id §222, per-title renderer §224, CPU correctness campaign §225); **B3/Track C §§226–240** (the computed-mode GPU seam §§226–230, the display-source rule §234, the closed gap 12 cube 2D-array decode §236, the image-window decode class §237, the 64-instruction non-JIT slice §238, static edge accounting §239 and the device-stage clear fill §240). Codegen IR, DSP mixing/host output, debugger GUI, release packaging pending |

Current test baseline: **1884 tests passing** (2026-10-23, `dotnet test XGrid.sln -c Release --no-build`; per project: Cpu 676, Nv2a 437, Bus 263, GpuGl 104, Memory 74, Integration 72, Storage 54, Frontend 50, Core 39, Audio 42, Input 29, XboxFs 28, Graphics 10, Xbe 6);
`dotnet build -c Release` clean (0 warnings); `dotnet format --verify-no-changes` clean (verified 2026-10-23).

## Project completion assessment (2026-10-19; refreshed 2026-10-22 (39th session) for the C4/C5/C6 performance slices §§237–240 - instruction fetches from immutable image windows, the non-JIT run slice at 64, the raw emitter's static per-edge accounting and the device-stage clear fill through `Span<uint>` (-10.6% on both headline legs, every anchor byte-identical) - and, in the 36th session, for the closed B3 computed-mode panel - gap 12, a cube stage sampled as a decoded host 2D-array texture - and, in the 34th session, §234 - the GPU display-source rule (a direct-framebuffer guest now shows its own buffer under the OpenGL renderer) and the C7 encoder price that closes "own the encoder" (~7% triangle wall, below the stage bar) - and earlier for the B3 computed-mode GPU slices §§226–228, the `reach[...]` combiner read-set diagnostics §230, the `nxdk/usb` corpus row §229 (the matrix is now 22 rows over 17 samples), the full CI/release pipeline (`81bc8b3`), the zero-warning/zero-inspectcode-findings tree (`8840073`/`2b7d144`) and the **2026-10-22 performance re-aim** (operator: xemu-class or better is the long-run objective; Track C re-opened, C4-C6 filed, no score change until a slice lands); the previous refresh covered the Track B GL slices §§219–220, the frontend GPU renderer + per-title renderer + threaded snapshots §§221–224, the CPU correctness campaign §225, the fixed-function vertex stage and its lighting §§213/215, PTIMER §216, the display model §208, the compatibility corpus at 20 rows over 16 samples §§207–210, Track P (XInput pad + save states §206, per-title overrides + persistent EEPROM §217/§219d) and the APU XGSCNT fix §218; earlier refreshes covered §§184–205)

> The phase table below is current and is checked against `docs/status.json` by
> `scripts/status.ps1 -Check`; the prose in this section is history. The
> completion graph is generated from `docs/status.json`.

Weighted estimate over the 24 plan phases (0–23). Phase fractions reflect
implemented-vs-planned surface (behavior, tests, reference parity), and the
weights approximate remaining effort (CPU interpreter, NV2A and JIT
dominate). This is an estimate, not a metric — treat it as ±5 points.

| Phase | Weight | Done | What remains |
|---|---|---|---|
| 0 Bootstrap | 1 | 100% | — |
| 1 Core | 2 | 100% | — |
| 2 Memory | 3 | 100% | — |
| 3 CPU | 11 | 97% | x87 double-rounding limitation; the §225 independent x86 differential (333 → 629 cases) closed the rare-op defects it found (PMINSW/PMAXSW sign, PSRAW/PSRAD saturation, XLAT, 16-bit `[SI]`/`[DI]`, x87 NaN payloads, FUCOM quiet-NaN IE, FSQRT-negative C2, MIN/MAX equal-operand source); **§254/TM3 added SAHF/LAHF (the retail `fprem; fnstsw ax; sahf; jp` tail) with 12 more oracle cases (corpus 641)**; the remaining one-byte holes are filed (DAA/DAS/AAA/AAS, BOUND, the `0x82` alias, LES/LDS, SALC, INT1); no SSE2 by design (the CPU is a Pentium III) |
| 4 MMU | 3 | 100% | — |
| 5 Bus & IRQ | 6 | 90% | DMA helper, ELCR (landed TM2: 0x4D0/0x4D1 + level delivery), Super-I/O, GPE |
| 6 MCPX | 6 | 80% | flash command set, GPE/GPIO, Super I/O |
| 7 Firmware & Boot | 5 | 90% | ATA-security edge cases; guest XBE-loader items |
| 8 Storage | 5 | 86% | writable host-side FATX; DVD media edge cases. §222 added `XdvdfsSectorReader` (XDVDFS over any `IDvdImage`, so a CHD resolves its XBE title id) |
| 9 XBE Loader | 3 | 90% | host-side relocation/launch paths |
| 10 Kernel Interface | 2 | 5% | whole phase (HLE ordinals) — deferred, LLE-first |
| 11 NV2A | 15 | 94% | mipmap LOD + DXT decode; DOT_STR_3D dim==3; BRDF detail and the remaining B3 method rows; Vulkan backend (16.6–16.7). Landed since the last refresh: the **fixed-function vertex stage + its lighting** (§§213/215, compatibility gap 6 — the dashboard runs 22,207 of its 33,632 draws/1B through it, opt-in `--ffp`), **PTIMER** 0.8.137 (§216: 61-bit time with writable offset, `ALARM_0` re-arm, `NV_PMC_INTR_0_PTIMER`), the **display-controller scanout model** (§208: `NV_PRMCIO` CRTC decode, `TryCopyDisplayFrame`), backface culling (§206g), the **cubemap face extent** fix + byte-order texture formats (§§207h/209g), **TEXFILTER** on both backends (§§209/210/219a/220c — the convolution runs only for MIN = `CONVOLUTION_2D_LOD0`), GL `PROJECT2D` divide + clip origin (§220c), one-writer replace-mode composition (§220d), and the Z24S8 clear-word layout verified (§214). **Since the last refresh (2026-10-22, §§226–230):** the B3 computed-mode family reached the GPU seam — cube face extent/stride/swizzle masks, the eye position and the dot/HILO/cube-face/specular-reflect math are per-draw uniform state, the generated shaders compile on the driver under a 99-program live gate (a redeclaration bug that had silently forced whole frames back to software is fixed), and cube stages fetch face texels through the direct RAM lane instead of the 32-entry carried window; the corpus row runs its dot programs at 4.4B. **236 (36th session) closed the last B item:** a cube stage is a host sampler2DArray decoded from the guest's six swizzled faces through Nv2AMmio.DecodeSwizzledTexelWord, so the computed-mode panel renders on the GPU and agrees with software within 2 pixels of 307,200 at both anchors; the word pool now carries only a HILO dot input's raw pair (maps 4-7), and nine dead uniforms plus the descriptor decode are gone. **§234 (34th session):** the display source under a GPU backend now follows the display controller - a 4-entry recently-presented-surface ring decides between the backend readback and the RAM scanout, so a direct-framebuffer guest (`gamma`/`hello`/`usb`) shows its own pixels in the OpenGL renderer and a flipping title keeps the GPU frame; Nv2a snapshot v8 -> v9 |
| 12 APU | 4 | 95% | GP/EP DSP execution, mixing/resampling, host output. **The 0.8.139 VP mixer is ported (TM8, 2026-10-26): McpxApuDevice.Vp.cs (the per-frame voice-list walk, both envelopes, PCM/ADPCM buffer and stream fetch through the SSL/SGE tables, resampling, the 8-tap volume/headroom mix into the 32 mix bins, multipass), Vp/ApuVpAdpcm.cs, Vp/ApuVpFilters.cs (svf/hrtf) and Vp/ApuVpFrame.cs; the GP/EP cadence is now the reference's (eight 32-sample frames per XGrid frame, the EP once), 007 writes 42.4%-non-zero PCM (peak 7,500, RMS 1,023) where TM4 measured all-zero, and APU snapshot v6 carries the mixer state. The AC'97 codec stays register-only, which matches xemu (its EP sink bypasses the codec).** **The 0.8.139 DSP is ported (P1.1-P1.4): `Dsp56300Core` (929 differential vectors), `DspDma`, the `gp_ep.c` frame glue (`McpxApuDsp`/`McpxApuDspPair`: sub-windows, scatter/gather scratch, FIFO rings, peripheral halt/interrupt/DMA, the GP/EP frame loop) and the PCM sink (`IAudioBackend`/`WaveAudioBackend`, EP output FIFO 0). The core path is opt-in (`--apu-dsp`) and executes the real dashboard microcode with 0 undefined instructions; the VP mixer (audible voices) is the remaining gap.** The VP voice processor and `NV_PAPU_XGSCNT` are correct (107, 218) **TM4 (2026-10-25): the idle-voice trap is the VP frame walk's alone** - `voice_off` queues none (xemu `vp.c:118-132`, the walk at `vp.c:1833-1835` is the only `SE2FE_IDLE_VOICE` sender); the pending queue that gave 007 two completions per voice is gone (APU snapshot v5, v2-v4 still load) and the gate titles are byte-identical. **TM5 (2026-10-25): the DirectSound mixer bridge is title-independent** - the block is discovered from the guest's own GP scratch (SGE) table (page + 0x810, state 2/3 absorbed, `TryGetMixerStateWord`) instead of 007's hard-coded `0x03924810`, which is what let Halo launch. Measured silence: `--apu-dsp --audio-out` on 007 writes 219,392 frames of zero PCM and Halo writes 0 PCM frames (the VP mixer is not modelled). |
| 13 USB & Controllers | 4 | 70% | memory units/storage class, more device types |
| 14 Networking | 4 | 70% | first-real-frame validation, TCP proxy |
| 15 CPU JIT | 11 | 90% | semspec slices 1–20+memory-path 1–4+dispatch 5–6+shape 7 opt-in `--spec-exec`; decode/TLB caches + block executor (§§131,134,136); codegen E1–E3 opt-in (§§171–179b); **fast-mode track §§190–194: `--jit` whole-block DynamicMethod engine (dual-mode contract), lost-wakeup fix, coverage campaign (99% served), menu 1B bit-exact, ktest 2B 3.76× CPU-bound, quiet-host fast medians triangle 10.63 s / menu 45.62 s after C2+D3b (§199d)**; **C3 raw emitter §§199e–§199i: `--jit-raw` opt-in, per-block native x86-64 (fixed host registers, flags in host FLAGS, inline translate-cache probe, spec-handler trampoline); 17 latent bugs fixed across three correctness campaigns; 59/59 JitTests with the raw path forced on; BOTH title 1B IL-vs-raw differentials bit-exact (menu `660CA11B…/F5D61C94…`, triangle `A2DCA55F…/7DAB63EB…`, every counter identical); ahead of IL on CPU serving time after the §199k state hoist; §199l direct chaining LANDED after the §199m fault-contract fix (helpers catch/record/report instead of unwinding through the hand-emitted frame): 1B medians of 3 interleaved pairs triangle wall -12.0% / `outerMs` -28.6%, menu wall -13.1% / `outerMs` -14.2%, SHAs and every counter identical to IL; 625/625 CPU tests x10, ktest XGrid leg byte-identical at 792,800,535 steps**; **§199o C3-x hot-shape native encoding LANDED (helper-routed members 24.65M -> 1.81M triangle, 122.65M -> 5.57M menu; 1B wall -10.8% triangle / -7.2% menu on sw, -14.1% / -8.0% on gl+replace; 8.8 ns/served member triangle / 31.6 ns menu — the §199n target hit); C3-y bookkeeping trim and the 1024-slot data xlat cache measured NEUTRAL and reverted, the flip-pacing probe closed as no-bug**; **C3 frozen 2026-10-20, then re-opened 2026-10-22 (operator: xemu-class or better is the long-run objective): the engine is ~17 s of the 24.3 s menu `gl+replace` wall and dominant on the 7.5-7.9 s triangle wall, so the new C4-C6 program (flat memory model/soft TLB, trace/superblock formation, device/MMIO fast path) leads; each stage needs a same-window A/B and exact 1B differentials. The `--jit`/`--jit-raw` default-flip decision is still open**; **C5 slice 1 landed (§233: the IL runner is built on demand and a chain jump keeps the source frame - 1B `gl+replace` triangle -4.2% / menu -3.0%, 3/3 pairs, all four fast anchors exact); §234e prices the one remaining encoder option out: `Assemble` 390 ns vs a single-pass 120 ns per instruction (3.2x) is ~7% of the triangle wall, so C7 owns the engine shape, not the assembler**; **§237–§239 (39th session) landed three exact engine slices:** instruction fetches from flash/BIOS/mirror/MCPX-overlay windows are a decode-cache class with no store path (`nonRamSkips` 359,476 → 0, one-instruction non-JIT runs 434k → 134k), the non-JIT run slice went 256 → 64 (`nonJitInstructions` 5.48M → 2.62M, `nonJitMs` 2190 → 1650) and the raw emitter's per-member EIP/TSC/served bookkeeping became one static publish per edge (engine 6.3 → 6.1 ns/member; the wall was flat, so the runner is latency-bound, not instruction-bound); measured together triangle `gl+replace` 6.672 → 6.081 s (-8.9%) and menu 20.456 → 19.762 s (-3.4%), every 1B SHA/counter/frame and the ktest log byte-identical; threshold auto-tune and E3-2 are deferred |
| 16 CPU Optimization | 3 | 35% | benchmark-driven tuning; `boot --bench` + `jit-bench.ps1` medians + `--jit-tally`/`--gpu-time` diagnostics and quiet-host accounting in place (§§191–194); **2026-10-22: the performance program is the lead — C4-C6 use these as stage gates and report ns/served member + wall per presented frame** |
| 17 Memory Optimization | 2 | 40% | RAM-direct/wide-access (§161), Bytes bulk (§162), xlat cache (§163), rep-string span (§165); remaining RMW paths |
| 18 GPU Optimization | 4 | 78% | rasterizer bit-exact specialization (§§180–187: fast combiner configs, planned evaluator, sampler plans, blend specialization, lean interpolation; menu 119→24.6 s, triangle 54.7→9.9 s); neutral experiments reverted (§183/§186); GL backend pairing measured parity-to-worse at 1B (§194 am.3 — sw stays default); measured at 48% (menu) / 39% (triangle) of the 1B sw wall after C3 (§199n), then **§199p: O2 `IGpuMemoryFast` GPU-memory fast lane landed (-2.0% both titles, 3/3), then O4 branchless Morton spread (-5.9% triangle 3/3, -1.2% menu 2/3); §200: O3b per-fill fast sampler + O1 compiled k1 modulate (245.8M px) and k2/k3 lighting fills, 1B rasterPsh menu 24.0 → 13.3-15.1 s (-44%), triangle 6.19 → 3.31-3.45 s (-45%), interleaved wall -21.9% / -23.5%, quiet host 36.01 s / 9.13 s; accurate-mode raster triangle 6.90 → 3.63 s (-47%) with RAM/CPU SHAs and every counter exact; O5 counter batching and the cube-stride hoist measured neutral; the rasterizer is no longer the dominant wall**; **§204 present path measured and fixed: the per-flip readback is opt-in (`--frame`/`--frame-first`; 0 in normal runs), each draw is one packed `uTri[25]` uniform + one `glDrawArrays` (positions by `gl_VertexID`, no VBO upload), and the per-draw key strings/divisions/binds plus the per-method seam call are gone — `drawMs` 12.55 → 2.41 s (-81%), menu `gl+replace` 63.09 → 45.25 s (-20.5% same-window, 3/3), triangle 10.26 → 9.89 s; 1B GL goldens exact (`6BE6325F…` / `3FC4B419…`)**; **§205 draw-command batching landed: a PGRAPH primitive flush averages 82 triangles, so the backend stages a whole command and submits it with one `glDrawArraysInstanced` (per-triangle block = a uniform-array slot selected by `gl_InstanceID` through a flat varying, capacity 64 from the queried uniform budget, `XGRID_GL_NO_BATCH=1` forces the old path) — `drawMs` 1971 → 1045 ms (-47%, 3/3 same-binary), 59,437 instanced draws replace 2.76M single draws, menu `gl+replace` wall 42.20 → 40.43 s (-4.2%, 3/3), both 1B GL frame goldens byte-identical; the remaining menu submission is ~0.8 s of block packing (not GL calls) and the ~0.5-0.9 s texture fingerprint, both deferred with measured ceilings**; **the phase's own checklist is now closed** (command generation, submission, shader compilation, texture upload, render-pass creation, synchronization, frame pacing + the six caching techniques - see the phase-18 status block for the per-item evidence); what remains is measured leftovers (per-draw layer bits hybrid/z, ~0.8 s block packing, ~0.5-0.9 s texture fingerprint, O6 row spans demoted, product-path present pacing) - no capability gap; Vulkan/GL backend work stays in P11. **§210–§224 (2026-10-21/22):** the TEXFILTER convolution gate made the opt-in flag free (1B menu 32.18 s vs 32.22 s legacy, `compiled=` identical) and both compiled fills serve the tent; the GL backend gained TEXFILTER parity (§219a), `ClearCore` lifts the draw write masks (§219c), the sampler decodes the address word and both fragment axes use the clip origin (§220b/§220c), and a declined draw composes the frame into guest RAM as one writer per frame (§220d); through the real frontend session the host-GPU path runs 1.121B/1.106B steps vs software's 1.014B/1.021B in a 40 s interleaved pair (+9.8%/+8.2%, §223), and the CLI's 1B menu leg is 33.95 → 23.46 s (-30.9%, §221); **§240 (39th session) measured the device stage for the first time and cut its clear half:** 1.31 s of the triangle 1B `gl+replace` wall was inside 44,030 device writes (766 ms draw flushes, ~540 ms surface clears); a 32-bit clear now fills rows through `Span<uint>.Fill` when the fast adapter's write is a plain 4-byte RAM store, so device time fell **1,306 → 816 ms** and the wall **triangle 6.31/6.43/6.34 → 5.61/5.74/5.73 s (-10.6%, 3/3)**, **menu 22.43/20.76 → 19.98/18.82 s (-10.6%/-9.4%)**, every anchor byte-identical |
| 19 Debugger | 3 | 30% | symbols, GUI/trace viewer (save states are phase 22's and landed) |
| 20 Automated Testing | 3 | 85% | 1884 tests (Cpu 676, Nv2a 437, Bus 263, GpuGl 104, Memory 74, Integration 72, Storage 54, Frontend 50, Core 39, Audio 42, XboxFs 28, Input 29, Graphics 10, Xbe 6) + goldens/median harnesses + randomized emit-inline differential net + 44 JIT differentials (fuzz/IRQ sweeps/faults/paging) + raw-emitter differentials (`RawJitTests` + the §199h delivered-EFLAGS parity, page-table-store-under-watch, exact watch-range-hit and split-member block-exit tests, plus 11 §199o C3-x shape differentials; 59/59 with the raw path forced on) + §199p fast-vs-legacy GPU-memory-lane differential + 8 §200 fast-vs-generic sampler/compiled-fill differentials + the §204 readback-policy live tests + 4 §205 batch-bracket seam tests (fail-pre-fix proven) + ktest smoke; the present-path acceptance script (`scripts/present-path-acceptance.ps1`), the batching A/B (`scripts/batch-205-ab.ps1`) and the boot-to-first-frame harness (`scripts/boot-to-first-frame.ps1`, `-Corpus`/`-IsoPath`, both backends) plus the frontend smoke harness (`tools/frontend-smoke`) automate the ceremony; **the independent x86 oracle (`tools/cpu-oracle/`, 629 cases against Unicorn/TCG) landed in §225 and is the model for new differentials**; **Track E's matrix is `docs/compatibility.md` — 22 measured rows over 17 samples (plus 3 blocked samples), M-B met**; **since the last refresh the §227 live driver compile gate (99 generated computed-mode programs compiled and linked on NVIDIA 4.6, not string-compared) and the §228/§230 `reach[...]`/`flags@` combiner read-set tests joined the net, and §229's `nxdk/usb` row is a guest-side assertion of the OHCI/hub model**; **§237–§240 added `ImageWindowFetchTests` (six cases: classification, one fetch per instruction, exact across a host flush, a guest store drops the decodes, JIT blocks match the interpreter, and a mode change re-forms instead of yielding) and `SurfaceClear_FastFillLane_MatchesPerPixelFill` (a fast-adapter span fill vs the per-pixel loop over the whole RAM image, plus the `pitch=0x42` decline)**; remaining: fuzzing (decoder/XBE/XDVDFS/FATX/pushbuffer), a real compatibility database |
| 21 CLI & Frontend | 3 | 75% | opt-in `--window` live present sink (§184); **Avalonia frontend landed `ea8b38f`** and is now a product shell: Settings → Renderer picks software or the host-GPU OpenGL backend (§§221/223 — headless WGL created on the emulation thread, logged fallback, thread-correct snapshot save/load), per-title overrides V2 (`title:XXXXXXXX`, a CHD resolved through the §222 XDVDFS reader) offer `inherit`/`software`/`OpenGL` per title (§§217/219d/224), persistent EEPROM state (`--eeprom-out`), `--ffp`/`--tex-filter` settings, XInput pad merged with the keyboard (§206), save/load UI (F8/F9 + picker), frame stepping (F7/Shift+F7, §220e), and the display follows the display controller rather than the renderer (`TryCopyDisplayFrame` + a recently-presented-surface ring, §208/§234: a direct-framebuffer guest shows its own buffer under OpenGL, a flipping title shows the GPU frame),  `tests/XGrid.Frontend.Tests` (50 tests) + `tools/frontend-smoke` cover it; §204 CLI observability (`--frame-first`, `readback=on\|off`, `methods=`); **§234 (34th session): the display source follows the display controller under a GPU backend** (a recently-presented-surface ring: a direct-framebuffer guest shows RAM, a flipping title shows the GPU frame; the session-level latch is gone and both renderers verify at the same 306,817-px dashboard picture), and the `tools/frontend-smoke` harness (broken since §217) is fixed and now in `XGrid.sln`; since the previous refresh the Library shows each XBE's title id (§228) and `--gpu-time`'s combiner rows print `tm=`/`sf=`/`reach[...]` (§§228/230) and `ts&lt;n&gt;=` per texture slot (§236)**; the OpenGL product renderer now samples cube maps exactly (gap 12 closed, §236: 2 pixels of 307,200 from the software frame at both corpus anchors); remaining: **host audio output** (needs APU GP/EP DSP execution), debugger panes, performance-overlay polish |
| 22 Save States & Replay | 2 | 40% | snapshot/replay — **V1 save states landed and productized**: `.xgs` covers CPU/RAM/machine scalars plus every registered device, per-device versions (NV2A **v9**, APU **v3** with a v2 fallback, machine section v1), unknown sections skipped on load, and the frontend marshals save/load onto the emulation thread (a load resets backend state; 72 MB round trip with GL attached, §223). Remaining: replay recording (initial state + inputs + timing events), a whole-file checksum, and version migration. **TM4 (2026-10-25): a live 007 state round-trips through the real `EmulatorSession`** (72,110,280 B at 1.6B steps, `tools/frontend-smoke`, `faulted=False`), and the 100B gameplay snapshot (72,214,829 B) resumes and runs 1.2B steps; the APU payload is v5 with a v2-v4 reader. |
| 23 Release | 3 | 55% | packaging, installer, licensing review; **compatibility list exists — `docs/compatibility.md` with 22 measured rows over 17 samples (both backends, flag variants, phase anchors), reproducible commands, the `scripts/boot-to-first-frame.ps1` harness and the `scripts/build-nxdk-samples.ps1` toolchain (Track E; M-B met: ≥8 rows, ≥2 beyond dashboard/triangle/ktest)**. **Since the last refresh:** a full CI pipeline (`.github/workflows/ci.yml` + `release.yml`, `81bc8b3`: Release build and the full suite on Windows/GNU-Linux/macOS, coverage/inspectcode/vulnerability gates, CodeQL, dependency review and Dependabot), a zero-warning / zero-inspectcode-finding tree (`8840073`/`2b7d144`) and generated golden images dropped from git (`6fbf46a`); packaging and the licensing review are done (R1.1: scripts/package.ps1 + scripts/package-verify.ps1, docs/release-licensing-review.md, docs/release-notes/v0.1.0.md); an OS installer and code signing/notarization remain and are deferred by scope decision |
| 24 Retail gameplay & xemu full port | 10 | 60% | the title matrix (TM0), the shared-blocker probes and fixes (TM1-TM3), the 007/Halo playability gates (TM4-TM5), the next 5 titles and the probe harness (TM6-TM7), and the first targeted xemu port the matrix proves (TM8); port a module only at >=3 titles or >=20% of the matrix. **Landed: TM0's first 39-title matrix and TM2's 44-title re-run, TM1's blocker probe (the guest programs ELCR 0x68/0x18 and the writes were discarded) and TM2's PIC level-interrupt fix; 007 now reaches its own title screen at 50B and the game's render path before faulting in the APU ISR (CR2=0xE4); TM3 fixed the missing SAHF/LAHF opcodes, so the 44-row matrix has 0 faults, Tak is a second own-render row, and Halo/Ninja Gaiden Black run their own XBE code to 1B** **TM4 (2026-10-25): 007 Agent Under Fire is PLAYABLE** - 100,000,000,000 steps, `LastFault vec=0`, 134,004 draws / 1,005,989,482 px / 6,478 frames, the first-level Z8-on-the-docks frame (48,208 colours), the xemu 0.8.139 oracle at the same scene (dmean 18.0/22.6/14.6, black 4.1% vs 4.2%), scripted input changing state and frame, and a frontend save/load round trip; the 44-row matrix is unchanged (2/42/0/0). Audio is a documented silence (TM8). **TM5 (2026-10-25): Halo launches and renders its own main menu** - the APU mixer bridge was hard-coded to 007's block, so Halo's DirectSound submission was never consumed and the guest spun from 1.5B; with the block discovered from the GP scratch table the title presents its loading screen at 15B and the HALO menu at 35B (516 frames / 3,247 draws / 191,496,512 px), runs 100B steps with `LastFault vec=0`, and then waits in a guest-side D3D resource retry loop (EIP 0x1810F0, D3DERR_NOTFOUND for handle 0) - a documented wait; xemu shows the same menu at 70 s and gameplay at 150 s, and the menu frame fails the screenshot policy (dmean 101.8/100.6/113.3). The same fix moved the 44-row 1B matrix to **6 own-render / 38 dashboard / 0 fault / 0 timeout** (Ninja Gaiden Black 6,475 frames, Tak 1,068 frames / 3,701 draws, 50 Cent 100,987,370 px, Bad Boys II 42,303,082 px, Destroy All Humans! off the dashboard; the pre-fix build reproduces the old NGB row exactly). **TM7 (2026-10-25): the seven-title table is published with one command per row.** `scripts/retail-game-probe.ps1 <iso>` runs both legs and writes the frames, the row JSON, the Markdown report and one `PROBE` line, and fails soft (`xemu: blocked - <reason>`, exit 0). All seven rows were re-run from their TM4/TM5/TM6 anchors with a live xemu 0.8.139 capture for six of them: 007 **PASS** (dmean 18.0/22.6/14.6, the TM4 numbers reproduced exactly), Halo **FAIL** on menu brightness (101.6/100.4/113.1), Ninja Gaiden Black **FAIL** on colour count only (dmean 7.7/8.5/11.4, 12,913 vs 1,351 colours - the dither pattern is now the next named rendering defect), Bad Boys II **FAIL** (100% vs 20.5% black, the documented wait quantified), and Tak / Destroy All Humans! / 50 Cent **n/a** because xemu cannot be driven past a title screen on this host or shows a different shot. The capture guard retries `PrintWindow` and refuses a screen copy when xemu is not the foreground window (TM7 caught a browser frame that way). **2026-11-22: the targeted-port rule is superseded by the XFP full-port program (E1); the remaining work is XFP0-XFP14 against the ledger `docs/xemu-port-manifest.md`, playable-first gates P1-P5.** |

**Result: 78.89% (93.09 of 118)** after TM7 (2026-10-25: P20 Automated Testing 82->85 for the one-command retail probe harness (two legs, the screenshot policy, soft-fail with a recorded block, the snapshot-HDD resolution and the capture-contention guard) and P24 Retail 50->55 because all seven optimized ISOs were re-run from their anchors and six now carry a live xemu 0.8.139 oracle frame, with Ninja Gaiden Black's dither pattern named as the next rendering defect and Bad Boys II's black display quantified; before it TM6 (2026-10-25: P11 NV2A 93->94 for the reference-faithful inline-array path and P24 Retail 40->50 because the second-tier sweep probed five titles, moved Ninja Gaiden Black from a black screen to its own animated intro, and classified the other four with EIP, wait object and device I/O; before it TM5's Halo gate (2026-10-25: P12 APU 80->85 for the title-independent DirectSound mixer bridge that let Halo launch, P24 Retail 30->40 because a second retail title now launches and renders its own content and runs 100B steps clean, with gameplay a documented wait; before it TM4's 007 playability gate (2026-10-25: P12 APU 75->80 for the reference-faithful idle-voice delivery, P22 Save States 30->40 for the live 007 round trip through the frontend session, P24 Retail 15->30 because one retail title is playable end to end; before it TM2's level-interrupt fix took it to 74.3% (87.64 of 118: P5 Bus & IRQ 85->90 for the ELCR/level model, P24 Retail 0->10 for the first retail advance; the 2026-10-24 scope change had added phase 24 (Retail gameplay & xemu port, weight 10, done 0; the 24-phase score was 79.9%, 86.34 of 108). The pre-change history reads: ~78% complete overall (weighted: 84.43 of 108 weight-points = 78.2%, recomputed 2026-10-22 (39th session) after §§237–240: P15 CPU JIT 88→90, P18 GPU Optimization 76→78 and P20 Automated Testing 81→82, with P11 NV2A 92→93 and P21 CLI & Frontend 73→75 carried over from the §234/§236 refreshes; was 84.10 = 77.9% after §236: P21 CLI & Frontend 74->75, the OpenGL renderer's computed-mode panel; was 84.07 = 77.8% after §234: P11 NV2A 92->93 and P21 CLI & Frontend 73->74 (the GPU display source follows the display controller; the C7 encoder is priced out and moves no phase); was 83.89 = 77.7% after the §226–§230 B3 slices and the CI/quality landing: P11 NV2A 91→92, P20 Automated Testing 80→81, P21 CLI & Frontend 72→73 and P23 Release 14→18; was 83.56 = 77.4% after the §206–§225 refresh: P3 CPU 95→96, P8 Storage 85→86, P11 NV2A 88→91, P18 GPU Optimization 72→76, P20 Automated Testing 73→80, P21 CLI & Frontend 63→72, P22 Save States 5→30 and P23 Release 11→14; was 81.72 = 75.7% after the §220 frontend renderer switch (phase 21 60→63); earlier, recomputed 2026-10-20 after the §204 Track B present path (phase 18 40→45), the §205 draw-command batching (phase 18 45→72 after re-deriving it from the phase's own checklist, phase 20 72→73), the Track E corpus harness (phase 20 71→72), the Avalonia frontend `ea8b38f` (phase 21 52→60) and the compatibility matrix (phase 23 10→11); was 80.02 before the frontend. The 2026-10-20 course correction changed the *order* — Track B/E/P before further Track C work — and the 2026-10-22 operator decision changes it again: xemu-class performance or better is the long-run objective, so Track C is re-opened and leads (C4-C6). The score only moves with landed work; §§237–§240 are the first landed performance slices, which is why it moves now).

Visual treemap of the table above: `tools/completion-graph/` (SVG + interactive HTML; regenerate with `pwsh -File tools/completion-graph/generate.ps1`).
By track:

- **LLE core (phases 0–14): ~85%.** The real console chain — MCPX ROM ->
  1BL -> 2BL -> kernel -> dashboard — boots, renders in color, discovers
  content, saves files, and navigates with a real controller through the
  daughterboard hub topology (§§68–70, §95); the 007 title runs to its
  audio-drain gate (§98, APU VP/DSP next). Since the last refresh the NV2A
  also runs the **fixed-function transform + lighting pipeline** (§§213/215,
  `--ffp`), the **PTIMER** alarm model (§216), the **display-controller scanout**
  (§208) and the **TEXFILTER** sampler on both backends (§§209/210/219a/220c).
  The B3 computed-mode family also reached the GPU seam (§§226–228), and
  §236 closed gap 12: a cube stage now samples a host 2D-array texture decoded
  from the guest's six swizzled faces, so the computed-mode panel renders on the
  GPU within 2 pixels of software.
- **Performance/finish track (phases 15-23): ~63%.** Dominated by the CPU
  JIT (90% — interpreter with default-on decode cache, opt-in spec/block/
  emit layers, and the chartered fast-mode engine: `--jit` whole-block
  DynamicMethod runners at 99% served, bit-exact on both titles at 1B, ktest
  2B 3.76× CPU-bound; the C3 raw emitter (`--jit-raw`, opt-in) is
  correctness-validated on both titles at 1B, the §199l direct-chaining slice
  is LANDED on the §199m fault contract (-12.0% triangle / -13.1% menu wall
  at 1B), and §199o C3-x hot-shape native encoding cut helper-routed members
  to 1.81M / 5.57M and per-member cost to 8.8 ns triangle / 31.6 ns menu —
  the §199n target hit; C3-y and the xlat cache measured neutral and were
  reverted; §§237–239 then landed the immutable-image decode class, the
  64-instruction non-JIT slice and static per-edge accounting — triangle
  `gl+replace` -8.9% / menu -3.4% with every anchor exact) and by GPU
  optimization (78% — the software rasterizer is 44-45%
  cheaper after §199p O2/O4 and the §200 compiled fills: menu 24.0 →
  13.3-15.1 s, triangle 6.19 → 3.31-3.45 s, no longer the dominant wall,
  with accurate-mode SHAs and every counter exact; §204 then measured and
  removed the `gl+replace` present-path overhead: readback is opt-in, one
  packed uniform + one `glDrawArrays` per triangle, `drawMs` 12.55 → 2.41 s
  (-81%), menu `gl+replace` 63.09 → 45.25 s same-window (-20.5%, 3/3); §205 then batched a whole draw command (82 triangles average) into one instanced draw: `drawMs` 1971 → 1045 ms (-47%, 3/3), 59,437 instanced draws replace 2.76M single draws, wall -4.2% (3/3), both 1B GL frames byte-identical); §240 then measured the device stage for the first time (1.31 s of the triangle wall: 766 ms draw flushes, ~540 ms clears) and cut the clear half through `Span<uint>.Fill` — device time 1,306 → 816 ms, triangle `gl+replace` 6.31/6.43/6.34 → 5.61/5.74/5.73 s (-10.6%, 3/3) and menu 22.43/20.76 → 19.98/18.82 s (-10.6%/-9.4%), every anchor exact).
  Both scoped Track B slices are in and the phase's checklist is closed; what remains is ~0.8 s of block packing
  and the ~0.5-0.9 s texture fingerprint (measured, deferred);
  row spans (O6) are demoted and the C3-z inline page pair is deferred
  (2-3% estimate, below its keep bar). §210–§224 then made the opt-in TEXFILTER
  free (the convolution gate), gave the GL backend sampler parity, clear-mask
  parity and one-writer replace-mode composition, and put the renderer behind
  the frontend setting: CLI 1B menu leg 33.95 → 23.46 s (-30.9%) and in-app
  throughput +9.8%/+8.2% steps with the same picture. The 2026-10-22
  §§226–230 landing is correctness/diagnostics (B3 shader math, the live
  compile gate, `reach[...]`), not wall-clock work; the release phase moved to
  18% on the CI/release pipeline. **The present path is closed as of
  2026-10-22; the remaining GPU work is B3 fidelity (phase 11) and the software
  rasterizer's structural per-pixel cost on the sw gate path (secondary — the
  product path is GL).**
- **Product track (2026-10-20, Track P):** the Avalonia frontend landed
  (`ea8b38f`) and Track P has since closed most of its gap: physical pad
  (XInput, §206), save-state V1 with save/load UI (§§206/223), per-title
  config V2 keyed by XBE title id (§219d/§224, CHD via §222), persistent
  EEPROM (§217), the renderer as a setting (§221/§223) and frame stepping
  (§220e). What remains is **host audio output** (it needs APU GP/EP DSP
  execution, so it is fidelity work with no sink oracle yet), debugger panes
  and release polish — the difference between "a core with a GUI" and an
  emulator a user can play is now mostly sound.
- **Flagship milestone M9 (dashboard boot): ~85%.** Boot, render pipeline
  (now colored, §95, and shown through the display-controller scanout since
  §208), and the full input path (XPP open -> `GetState`
  polling -> menu navigation -> activation) all work; a readable colored
  menu is live. The submenu state-100 pixel-negative and the NV2A method
  coverage tail are what remain.

Interpretation: the "does the console boot and respond" core is close; the
long tail is now **performance (xemu-class), audio output, release packaging
and the debugger**. The 2026-10-22 operator decision makes xemu-class
performance or better the long-run objective and re-opens Track C as the lead:
after §§237–240 the headline legs are **triangle `gl+replace` 5.61-5.74 s**
and **menu 18.8-20.0 s**, a ~4.7x gap to xemu's 1.2 s on the triangle and ~14x
to the menu's 1.36 s real-time band at 1B; the CPU engine remains the dominant
share. Track B is closed (§236: the computed-mode panel renders on the GPU
within 2 pixels of software), and host audio is the largest product gap.
`docs/development-direction.md` §0 is canonical.

### Performance campaign — xemu-class re-aim (2026-10-19; re-aimed 2026-10-22)

> **Re-aimed 2026-10-22 (operator decision): xemu-class performance or better
> is the long-run objective, and Track C is re-opened as the lead.** The
> 2026-10-20 correction did its job — the GL present path is ~1.3 s of the
> menu wall (was ~9.9 s), the corpus is 22 rows / 17 samples, and the frontend
> renders on the host GPU — but it left the CPU engine at ~17 s of the 24.3 s
> menu `gl+replace` wall (~70%) and dominant on the 7.5-7.9 s triangle wall,
> i.e. >50% of a measured wall, the stated re-open condition. Measured gap:
> ~6.6x (triangle vs xemu's 1.2 s at 1B) and ~17.8x (menu vs the 1.36 s
> real-time band; xemu has no menu oracle). Program: **C4** flat memory
> model/soft TLB, **C5** trace/superblock formation, **C6** device/MMIO fast
> path; each stage gated on a same-window A/B (>=10% wall or >=20% engine),
> exact 1B SHAs/counters, flag-off byte-identical and a byte-identical ktest
> log. C3-x/y/z is closed; this is not a resumption of 2-3% slices. The
> 2026-10-19 table and status below are retained as history. **Post-§237–240
> position:** triangle `gl+replace` 5.61-5.74 s and menu 18.8-20.0 s (see the
> Target definition below); C4 is closed null, C4b / C5 slice 1 / C6's clear
> fill landed, and the next C item is the draw-flush half.

**Why the plan changed.** The fast-mode JIT campaign (§§190–194) finished
its coverage/speed work: 99% served, menu 1B bit-exact, ktest 2B 3.76×
CPU-bound. The measured 1B decomposition then moved the bottleneck:
the **software rasterizer is 24.3 s of the 64.0 s menu wall (38%) and
10.1 s of the 27.9 s triangle wall (36%)**, MMIO slow-path device dispatch
(118.9M / 21.3M accesses) dominates the runner's non-raster time, and the
pure CPU-JIT portion is ~23 s / ~4 s. The GL backend's per-draw CPU cost
currently cancels its raster savings (menu +7–10 s, triangle ±0; §194
am.3). **"CPU JIT throughput first" is therefore retired as the lead track;
the GPU backend (Track B) and MMIO dispatch (Track D) are the primary
performance tracks, with JIT Phase-2 leftovers (Track C) after them.**
**2026-10-22: this conclusion is itself superseded — the GL path no longer
cancels its raster savings, so the CPU engine is the wall again; see the
re-aim note above.**

**Target definition (measured, not asserted).** 1 guest-second ≈ 733M steps
(PIT cross-check, §68). "xemu-class" = the same content runs at wall ≤
guest-time, and **or better** is the operator's 2026-10-22 bar. **Current
position (2026-10-22, after §§237–240):** menu `gl+replace` 18.8-20.0 s / `sw`
30.35 s, triangle `gl+replace` 5.61-5.74 s / `sw` 10.03 s at 1B; xemu is
1.20 s on the triangle (830 Msteps/s) and the menu's real-time band is 1.36 s,
so the gap is ~4.7x / ~14x (was ~6.6x / ~17.8x). ktest 2B is already ahead
(9-10 s vs 15 s). The lead milestone is M-D, not M-A. See
`docs/development-direction.md` §0/§3.
The baseline row is xemu on the same `data/test-samples/` workloads
(triangle ISO, ktest smoke): wall, frames, frames-per-wall-second. Perf
slices are measured in fast mode with verified guest-work equivalence
(SHAs/counters); fast mode is never correctness currency.

| Slice | Work | Acceptance test |
|---|---|---|
| **B1** | GL backend per-phase timers (`--gpu-time`: state, vertex, program, uniform, draw, present/readback); explain the menu +7–10 s and triangle parity | measured split delivered; no guest change |
| **B2** | Per-draw/per-method overhead: state-shadow caching, batched/UBO uploads, no redundant binds | menu 1B fast+gl ≤ fast+sw − 15 s (≤45 s); triangle ≤ sw − 8 s (≤20 s); golden policy violations=0 maxDelta≤1; SHAs unchanged |
| **B3** | Method coverage: menu subset → triangle's 471 unique pairs + ktest stream | zero `unsup` rows; guest counters identical |
| **B4** | Present/pacing polish only on playability need | Phase-4/swapchain rules |
| **D1** | MMIO fast dispatch (device-span guard + fast device entry) | −50% slow-member overhead; menu+triangle SHAs bit-exact; flag-off byte-identical |
| **C1** | JIT Phase-2 leftovers (IL chaining, threshold sweep) | ≥5% wall both goldens; bit-exact |

**2026-10-22 program (C4-C6) — the current lead.**

| Stage | Work | Acceptance target / status |
|---|---|---|
| **C4** | ~~Flat memory model / soft TLB: remove the per-access translate probe from the raw emitter's memory members; direct host-pointer RAM pages with an exact fallback for non-RAM/MMIO~~ | ~~≥2× engine on the menu, ≥1.5× triangle (same-window A/B)~~ **MEASURED NULL 2026-10-22 (§232a), reverted** |
| **C4b** | Read-only image windows: classify flash/BIOS/mirror/MCPX-overlay fetches as cacheable code, no store path, wholesale flush on a host image move; blocks form over them | **LANDED (§237): `nonRamSkips` 359,476 → 0, `nonJitCalls` 523k → 169k, triangle `gl+replace` -6.4%; exact 1B anchors** |
| **C5** | Trace/superblock formation: follow hot successors into one native trace, keep state across blocks, poll at every block boundary (the §193 wakeup contract) | **slice 1 landed (§233): -4.2% triangle / -3.0% menu; the full trace removes ~350 ms of boundary population = 3-5% of the wall (§235), below the stage bar** |
| **C6** | Device/MMIO fast path: hot NV2A method dispatch and the menu's device mix (slice 1, the unified interrupt poll, §232c) | **clear-fill slice landed (§240: device time 1,306 → 816 ms, triangle -10.6% / menu -10.6%/-9.4%), the draw-flush half landed (§241 / D1: the transform program is planned once per draw, 422 → 202 ms triangle and 7,134 → 2,600 ms menu, menu wall -24.8/-30.4/-32.8%), and the fill-path/record slice landed (§244 / D5: reject-before-setup, per-draw vertex scratch, read-slot fetch mask, operand locals, per-draw `PgraphDrawInfo` - menu wall -10.33/-17.20/-10.29%); the remainder is the menu's transform stage (2.2-2.5 s), priced at 9.5-10.7% for a compiled evaluator and not owned** |
| **C7** | Reassess: if C4+C5 miss 3× engine after two structural iterations, stop and reassess (including the native-core option) | **DECIDED 2026-10-23 (§242 / D2): option (a) - keep the raw engine and take the device/draw-flush path; C's engine-shape program is closed. Re-read on the post-D1 build: encoder 8.0% triangle / 4.9% menu, trace boundary population 6.04% / 4.35%, code volume 0% wall, C4 probe null, device stage 10.2% triangle / >=15.8% menu. D4 stays conditional on a per-member probe (>=20% engine, two iterations)** |

Gate rules unchanged: accurate mode is gate currency; headless `--gpu sw`
default; never mix; no async; quiet-host medians for wall claims (pause and
ask the operator when Fortnite is running); no real Xbox games; non-GPL.

**Status (2026-10-19).** B1 + B2a LANDED (§196): the backend was an
observation mirror; `--gpu-replace` adds replacement mode (software
rasterizer skips honored draws). Quiet-host 1B fast: **menu 64.02 → 48.29 s
(1.33× sw, 1.52× spec-ctl), triangle 27.91 → 17.82 s (1.57× / 2.08×)**,
CPU SHAs exact, frames within the maxDelta≤1 policy, BackendUnsupported=0,
RAM divergence confined to renderer-owned surface memory. B2b (state shadow
+ per-program uniform caches + non-allocating consts) landed: pixel-identical
and wall-neutral — the ~2.5 µs/draw is driver cost, so **B2c (draw batching:
per-draw uniforms → attributes/UBO, one call per state run) is the next
GPU-path slice**, alongside the RAM writeback for framebuffer-reading
content. D1 (MMIO fast dispatch) and C1 (JIT leftovers) remain queued.

**Update (2026-10-19, same day — D1/D2/D3a + fast-mode spin skip + timing
gate landed; §§196–198).** Track D delivered more than expected, plus a
CPU-side slice:
- **D1 `--jit-mmio`** (device fast path; opt-in): 352K/941K accesses served,
  bit-exact; device accesses are only 0.3%/4.6% of the slow class — neutral
  wall, but the diagnosis was decisive.
- **D2** data-translate cache 4 → 256 slots: slow members −25%/−19%, ~2%
  menu (4/4 paired).
- **D3a** cross-page decode-cache entries: crossPageSkips 761K → 0,
  `--decode-verify` clean; the triangle's non-JIT cost was a vblank spin at
  page-end addresses (§197).
- **`--jit-spin`** fast-mode idle-wait skip (§198): the kernel idle loop
  (0x8001B02E) fast-forwarded; triangle −17%, menu −4%; frames identical.
- **`--jit-timing`** (§198a): the per-invocation runner timing was costing
  ~15–25 ns × 125–237M invocations — gated off by default; triangle −24%,
  menu −8%, same SHAs.
**Best-ever 1B (fast mode): triangle 10.68 s gl+replace / 20.28 s sw
(1.67×/1.38× vs the 27.9 s baseline); menu 41.39 s / 56.82 s (1.17×/1.13×).**
The xemu-class gap on the triangle narrowed from 18–29× to **~11×** (xemu
~70 fps vs 6.4 fps). Next levers: B2c draw batching, D3b (blocks across
page-end heads), the runner's per-block state sync (~18 ns/invocation ×
125M), and the RAM writeback. Real-content policy: benchmark-only real ISOs
authorized (§197).

**Re-aim (2026-10-19 evening, §199) — Track C leads again.** *(Superseded
2026-10-20 by the course correction, then re-aimed 2026-10-22: Track C leads
again as the C4-C6 program. See the block at the top of this section and
`docs/development-direction.md` §0.)* The structure
inverted once more: with `--gpu-replace` the rasterizer is gone, and the CPU
engine is now 57% of the triangle wall (runner 6.1 s of 10.68 s) and ≈45% of
the menu wall. The `DynamicMethod` block JIT is at its measured structural
ceiling (~18 ns fixed per block × 125M + ~7 ns per member × 543M); the
charter's Phase-3 entry condition is met (xemu ≈830 Msteps/s on the triangle
vs 94M/24M → ~9×/30×). A 4B-step window-mode dashboard run (single, window
overhead included) profiled the *real* workload: **53.1M GL draws at ~2 µs
(drawMs 106 s, 22%)**, 12,675 flips each paying a **1.6 ms readback
(readbackMs 20.7 s)** that the window path does not need, 736M slow members,
349M NV2A methods. Execution order now:
1. **D3b + bookkeeping** (cheap, days): page-end wait/residue (1.9 s
   triangle / 3.6 s menu) and the machine/CLI advance path (2.2 s / 4.8 s).
2. **C2 trace emitter** (charter Phase 2.5): multi-block traces, state in
   locals across the trace, inline RAM fast paths, boundary polls preserved;
   target ≥2× CPU (triangle runner ≤3 s, menu CPU ≤11 s). **Absorbed by C3:**
   direct block chaining already removes the per-block state reload without a
   second emitter (decisions §199l/§199n).
3. **C3 raw emitter** (charter Phase 3, Iced authorized): register
   allocation, direct chaining, inline TLB; another ≥2× (triangle ≤3 s
   end-to-end, menu ≤12 s). **Landed (§§199e–§199n): both titles are bit-exact
   with IL at 1B, the §199k state hoist put the raw path ahead on CPU serving
   time, and direct chaining (§199l) behind the §199m fault contract adds
   -12.0% / -13.1% wall and -28.6% / -14.2% `outerMs` (1B medians of 3
   interleaved pairs).** The §199n breakdown then moved the remainder: the
   software rasterizer is the largest single item (48% menu / 39% triangle of
   the sw wall) and the CPU cost is 18.5 / 61 ns per served member, so the open
   structural work is **C3-x hot-shape native encoding (menu 61 → ~30 ns) →
   C3-y per-member bookkeeping trim → C3-z inline TLB/RAM page pair**, then
   Track B.
4. **B2c draw batching** (2 µs/draw → ≤0.5 µs) and the **window present
   path** (drop the redundant per-flip readback in window mode; batch the
   message pump).
Milestones: M1 triangle ≤6 s / menu ≤20 s; M2 triangle ≤3 s / menu ≤12 s;
M3 triangle ≤1.5 s / menu ≤4 s (xemu-class band).

**Decode-cache default-on soak complete (§137, 2026-10-15):** boot CLI now
uses cache by default; `--no-decode-cache` restores legacy fetch/decode.
Menu/triangle 1B exact SHAs and full counters on default/on/off/verify;
verifyFailures=0 both, log-diff discipline passes, 1316 tests green.
Default/off speedups 2.56x menu / 3.04x triangle (shared-host walls).
CPU defaults, snapshots, and block-executor code unchanged.

**Block executor + first-codegen scope complete (136/138, 2026-10-15):**
opt-in `--block-exec` (`DecodedBlockCache`, boundary-preserving block cursors,
`--block-verify` tripwire); suite 1324 green; menu/triangle 1B SHAs + all
counters exact in off/decode/block/verify/profile; flag-off byte-identical;
dispatch share 30.9-36.6% of steps per block dispatch; uncontended pairs
1.04-1.20x over decode-cache. Scope study (138) measured the remaining cost:
dispatch is not dominant (data-path helpers are); no managed emitter beats a
plain C# helper loop (14.285 ns/insn) - semantic specialization is required
before any codegen slice; recipe and acceptance gates in
`docs/research-notes/jit-codegen-scope.md`.

**CURRENT STATE (2026-10-16) — wave-15 concluded; wave runner retired.**
**WAVE 13 DONE (§§136–138; two merges + resolver).**
T1 decode cache MERGED (§134: `DecodeCache` linear-key/physical-reverse-map,
conservative invalidation at the §131 inventory sites, `--decode-cache`
implies fetch-fast, `--decode-verify`; **menu 2.12–2.20× / triangle 2.97–
3.07×**, hit share 99.5–99.8%, decode 0.20–0.47/insn, verifyFailures=0 @1B;
flag-off byte-identical, both SHAs + counters exact both flags; +14 tests).
T2 dirty-bit (a*) MERGED (§135: `IPresentDirtyWatch` choke in
`PhysicalMemory.Write8/16/32/64`, suppression on the two `IGpuMemory`
adapters, FlipCore dirty→upload / clean→serve / guard→flipEmpty; menu/tri
goldens unchanged with `presentUploads=0`, A/B hook cost within noise;
+25 tests). BOSS merge conflict: both sides of `PhysicalGpuMemory`
combined (suppression + page-drop) plus one stray-brace fix caught by the
post-merge build; §-union notes 134/135. T3 gates on base PASS (smoke
7/7 known deltas; menu/tri SHAs + counters exact). T4 swapchain brief
FILED (`gpu-swapchain-brief.md`: O2 window recommended, O1 stays default,
vsync off, O3 violates single-dependency rule). **1316 tests; post-merge
gates green: ktest smoke PASS 7/7, menu golden (SW SHA anchor + GL SHA
bit-identical to base, `presentUploads=0`), triangle golden PASS,
format clean; CI green on all three OSes.** Baseline for next wave:
green master.
**WAVE 13 DONE (§§136–138; two merges + resolver).** T2 block executor
MERGED (§136: `DecodedBlockCache`, `--block-exec`/`--block-verify` opt-in,
boundary-preserving); T3 decode default-on soak MERGED (§137: default flip +
`--no-decode-cache` opt-out, menu 2.56× / triangle 3.04× default vs off);
T1 first-codegen scope FILED (§138 + `docs/research-notes/jit-codegen-scope.md`,
probe, unmerged by design — no managed emitter beats a plain C# helper loop,
semantic specialization required). Final gate: format + build + test green,
ktest smoke 7/7 (792,800,535 steps bit-reproducible).
**WAVE 15 DONE (§§139–140; spike done clean, build finished by BOSS handoff).**
Spike filed §139 (V4R winner: pre-specialized per-shape handlers + unrolled C#
runner, 5.95×/4.40× over the helper loop; DynamicMethod rejected at ~10% over
unrolled C#). Build landed slices 1+2 behind opt-in `--spec-exec` (default off;
`XboxCpuSpecExec.cs` + 16 new `SpecExecutionTests`), merged as
`wave/jit-semspec`. The build worker was stopped by an opencode-go
`provider.invalid-output` hiccup with attempts exhausted and reported blocked
with an exact recipe instead of a fake done; BOSS ran the recipe verbatim: all
six 1B off/cache/spec pairs PASS (SHAs exact both titles, spec/cache 1.17×
triangle / 1.07× menu single-run) + two specverify legs PASS (verifyFailures=0,
vec=0) + off-vs-spec logs identical. Final gate green: format + build +
**1340/1340 tests**. Flag stays opt-in (menu margin thin, median-of-3 not done).
**§141 DONE (2026-10-16, direct):** spec-exec median-of-3 ran
(`scripts/spec-exec-soak.ps1`, 18/18 legs PASS, SHAs exact both titles,
`verifyFailures=0`, logs identical modulo mode lines): menu spec/cache
**1.035x** (inside host noise), triangle **1.130x**. Flag stays opt-in,
default off — no default-flip. Details in research-notes §141.
**§142 DONE (2026-10-16, direct):** JIT slice 5 (ALU-with-memory family,
66 handlers: Add/Sub/And/Or/Xor/Cmp/Test × reg,mem/mem,reg/mem,imm +
Mov mem,imm) behind `--spec-exec`; 1343/1343 tests, format clean, 18-leg
golden matrix PASS (SHAs exact, `verifyFailures=0`, logs identical);
fallbacks menu 392M→339M / triangle 494M→374M; within-matrix spec/cache
menu **1.046x** / triangle **1.086x**. Flag stays opt-in.
**§143 DONE (2026-10-16, direct):** JIT slice 6 (near Jcc, all 16
conditions, size-insensitive) behind `--spec-exec`; 1345/1345 tests,
format clean, 18-leg golden matrix PASS (SHAs exact, `verifyFailures=0`,
logs identical); fallbacks menu 339M→163M / triangle 374M→151M;
within-matrix spec/cache menu **1.067x** / triangle **1.156x**.
Flag stays opt-in.
**§144 DONE (2026-10-16, direct):** JIT slice 7 (near Jmp/Call/Ret)
behind `--spec-exec`; 1346/1346 tests, format clean, 18-leg golden
matrix PASS (SHAs exact, `verifyFailures=0`, logs identical);
fallbacks menu 163M→147M / triangle 151M→136M; within-matrix spec/cache
menu **1.058x** / triangle **1.158x**. Flag stays opt-in.
**§145 DONE (2026-10-16, direct):** JIT slice 8 (shifts/rotates by
immediate + Movzx/Movsx, 46 handlers) behind `--spec-exec`; 1347/1347
tests, format clean, 18-leg golden matrix PASS (SHAs exact,
`verifyFailures=0`, logs identical); fallbacks menu 147M→124M /
triangle 136M→131M; within-matrix spec/cache menu **1.077x** /
triangle **1.041x**. Specialization saturating (§138 confirmed);
next lever is the memory path (daylight design task).
Flag stays opt-in.
**§146 DONE (2026-10-16, direct):** JIT slice 9 (Inc/Dec with memory,
6 handlers: m8/m16/m32) behind `--spec-exec`; 1348/1348 tests, format
clean, 2-leg spec golden check PASS (`D:\spec-soak\slice9`: menu 197.0 s
SHA `4EF1FBF6…`, triangle 85.6 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, counter anchors exact); fallbacks menu 124M→120.8M /
triangle 131M→129M with Inc/Dec gone from the tail (remaining top:
Cli/Sti, x87, Movq/Movntq, SSE). Next: MMX Movq/Movntq (slice 10),
then x87. Flag stays opt-in.
**§147 DONE (2026-10-16, direct):** JIT slice 10 (MMX Movq reg,reg/
reg,mem/mem,reg + Movntq mem,reg, 4 handlers, both mnemonics now
continue blocks) behind `--spec-exec`; 1349/1349 tests, format clean,
2-leg spec golden check PASS (`D:\spec-soak\slice10`: menu 200.1 s SHA
`4EF1FBF6…`, triangle 87.3 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, counter anchors exact); fallbacks menu 120.8M→105.6M
(−15.2M Movq/Movntq absorbed) / triangle −90K; block dispatches menu
307M→292M (continuation confirmed). Walls within noise — no wall claim.
Next: x87 (daylight slice), SSE, Prefetch. Flag stays opt-in.
**§148 DONE (2026-10-16, direct):** JIT slice 11 (Prefetch + Sfence/
Lfence/Mfence hints, 1 shared EIP-advance handler, all 4 continue
blocks) behind `--spec-exec`; 1350/1350 tests, format clean, 2-leg spec
golden check PASS (`D:\spec-soak\slice11`: menu 223.2 s SHA `4EF1FBF6…`,
triangle 117.6 s SHA `A1740DE0…`, both `vec=0`, `verifyFailures=0`,
counter anchors exact; walls contention-inflated, no wall claim);
fallbacks menu 105.6M→103.7M (−1.9M Prefetch absorbed) / triangle −10K.
Remaining top: Cli/Sti (untouchable), x87 (daylight), SSE. Flag stays
opt-in.
**§149 DONE (2026-10-16, direct):** JIT slice 12 (scalar SSE Addss/
Subss/Mulss/Divss/Minss/Maxss + Cvtss2Si/Cvttss2Si, 16 handlers, all
continue blocks) behind `--spec-exec`; 1351/1351 tests, format clean,
2-leg spec golden check PASS (`D:\spec-soak\slice12`: menu 178.6 s SHA
`4EF1FBF6…`, triangle 99.7 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, counter anchors exact); fallbacks menu 103.7M→101.8M
/ triangle −1.8M with scalar SSE gone from both tails (remaining: Cli/
Sti + x87). En route: real F3-vs-REP discovery (`Decoder.cs:72-73,94`,
shared `IsMandatoryF3Scalar` gate + `rep movsb` null pin). Flag stays
opt-in; x87 stays daylight.
**§150 DONE (2026-10-16, direct):** x87 slice scope filed (read-only
probe: ~50.8M menu / ~9M triangle inventory, safety verified —
read-before-mutate, zero new invalidation hooks, no reordering).
**§151 DONE (2026-10-16, direct):** JIT slice 13 (`Fld/Fst/Fstp`
m32/m64/m80 + `Fstp ST(i)`, `Fild/Fistp` m16/m32/m64, `Fadd/Fmul`
reg,reg + reg,mem — 7 shared bodies, ~25 shapes) behind `--spec-exec`;
1352/1352 tests, format clean, 2-leg spec golden check PASS
(`D:\spec-soak\slice13`: menu 175.6 s SHA `4EF1FBF6…`, triangle 87.9 s
SHA `A1740DE0…`, both `vec=0`, `verifyFailures=0`, anchors exact);
fallbacks menu 101.8M→49.3M (−52%) / triangle −8M with all six mem
shapes gone from both tails; dispatches menu −18%. New tail: Cli/Sti,
Shl/Shr r,r, Fld-reg/Fxch/Fld1/Faddp, Movs, Push-m, Fsub r,m, Mul-m.
Flag stays opt-in.
**§152 DONE (2026-10-16, direct):** JIT slice 14 (x87 `Fld ST(i)`/
`Fst ST(i)`, `Fxch`, all 7 constant loads, 6 pop-arithmetic forms,
`Fsub/Fsubr/Fdiv/Fdivr` reg,reg + reg,mem — 19 handlers, all continue
blocks) behind `--spec-exec`; 1353/1353 tests, format clean, 2-leg spec
golden check PASS (`D:\\spec-soak\\slice14`: menu 204.1 s SHA
`4EF1FBF6…`, triangle 102.0 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, anchors exact); fallbacks menu 49.3M→43.5M (−12%)
/ triangle −2.2M with every slice-14 shape gone from both tails. New
tail: Shl/Shr r,r (CL forms), Push-m, Mul-m, Movs (string, out),
Leave/Neg/Fnstsw/Fiadd. Flag stays opt-in.
**§153 DONE (2026-10-16, direct):** JIT slice 15 (shifts/rotates by CL,
Push-m, Mul single + Imul all-forms — 8 shared bodies through the same
interpreter helpers, ~50 shapes, no `Terminates` change needed) behind
`--spec-exec`; 1354/1354 tests, format clean, 2-leg spec golden check
PASS (`D:\\spec-soak\\slice15`: menu 168.3 s SHA `4EF1FBF6…`, triangle
88.0 s SHA `A1740DE0…`, both `vec=0`, `verifyFailures=0`, anchors
exact) + `--block-exec` cross-check PASS; fallbacks menu 43.5M→38.4M
(−12%) / triangle −2.0M with every slice-15 shape gone from both tails.
New tail: Cli/Sti (untouchable), Movs (string, out), Leave, Neg,
Fnstsw, Fiadd, Div-r, Bswap, Not, Movss. Flag stays opt-in.
**§154 DONE (2026-10-16, direct):** JIT slice 16 (Neg/Not, Div/Idiv
single, Bswap, Xchg, Leave, x87 Fiadd-family + Fnstsw — 15 handlers,
all continue blocks) behind `--spec-exec`; 1355/1355 tests, format
clean, 2-leg spec golden check PASS (`D:\\spec-soak\\slice16`: menu
167.9 s SHA `4EF1FBF6…`, triangle 86.8 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, anchors exact) + `--block-exec` cross-check PASS;
fallbacks menu 38.4M→36.2M / triangle −0.5M with every slice-16 shape
gone from both tails. New tail: Movss-family/Rsqrtss (packed SSE),
Rcl-imm, Fcom/Fcomp/Fchs, Sbb-imm (carry-live, out), Movs (string,
out), Cli/Sti. Flag stays opt-in.
**§155 DONE (2026-10-16, direct):** JIT slice 17 (x87 Fcom/Fcomp/Fucom/
Fucomp + Ficom/Ficomp + Fchs/Fabs/Ftst + Ffree/Ffreep/Fdecstp/Fincstp/
Fnclex/Fninit — 16 handlers, all continue blocks) behind `--spec-exec`;
1356/1356 tests, format clean, 2-leg spec golden check PASS
(`D:\\spec-soak\\slice17`: menu 169.9 s SHA `4EF1FBF6…`, triangle 85.1 s
SHA `A1740DE0…`, both `vec=0`, `verifyFailures=0`, anchors exact) +
`--block-exec` cross-check PASS; fallbacks menu 36.2M→35.7M / triangle
−0.2M with x87 data fully absorbed on both titles (remaining x87:
transcendentals, Fcmovcc, Fcomi, CW/env/state, BCD — none in top-12).
New tail: Cli/Sti (untouchable), Movs (string, out), packed-SSE moves +
Rsqrtss, Rcl-imm, Sbb/Adc-imm (carry-live, out), Cdq, Pushf. Flag stays
opt-in.
**§156 DONE (2026-10-17, direct):** JIT slice 18 (packed-SSE moves
Movaps/Movups/Movss/Movlps/Movhps/Movhlps/Movlhps/Movntps/Movmskps — 9
handlers incl. the F3-gate ninth member, all continue blocks) behind
`--spec-exec`; 1357/1357 tests, format clean, 2-leg spec golden check
PASS (`D:\\spec-soak\\slice18`: menu 167.6 s SHA `4EF1FBF6…`, triangle
89.7 s SHA `A1740DE0…`, both `vec=0`, `verifyFailures=0`, anchors exact)
+ `--block-exec` cross-check PASS; fallbacks menu 35.7M→34.8M /
triangle −0.8M with every packed move gone from both tails. New tail:
Cli/Sti, Movs/Stos (string, out), Rsqrtss, Rcl-imm, Sbb/Adc
(carry-live, out), Cdq, Pushf/Popf. Flag stays opt-in.
**§158 DONE (2026-10-17, direct):** JIT slice 20 (Rcl/Rcr imm+CL,
Cbw/Cdq, Pushf/Popf, Setcc/Cmovcc, Rdtsc — 9 handlers, Popf/Rdtsc
whitelist additions) behind `--spec-exec`; 1359/1359 tests, format
clean, 2-leg spec golden check PASS (`D:\\spec-soak\\slice20`: menu
184.2 s SHA `4EF1FBF6…`, triangle 96.9 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, anchors exact) + `--block-exec` cross-check PASS;
fallbacks menu 34.6M→34.1M / triangle −0.2M with Rcl-imm/Cdq/Pushf/
Popf/Setcc gone from both tails. New tail: Cli/Sti, Movs/Stos (string,
out), Sbb/Adc (carry-live, out), Cmpxchg8B, Fldcw/Fnstcw/Fwait, Shufps.
Flag stays opt-in.
**§159 DONE (2026-10-17, direct):** memory-path scope filed (read-only
probe: common-case ~6-7 calls + translate + RAM touch per access; all
interpreter + 20 spec slices funnel through 4-6 helpers; three design
options with hazards — TLB-inline, identity fast path, span/burst).
**§160 DONE (2026-10-17, direct):** median-of-3 post-slices-13–20
(`D:\\spec-soak\\median20`, 12/12 legs PASS, SHAs + anchors +
`verifyFailures=0`): menu spec/cache **1.072x** (221.58/206.75),
triangle **1.185x** (118.49/99.99) vs §141's 1.035x/1.130x — first
measured wall movement since slice 8 (+3.7/+5.5 points tracking the
−91% fallback absorption). Flag stays opt-in (thin; default-flip needs
the §159 build first).
**§161 DONE (2026-10-17, direct):** memory-path slice 1 (RAM-direct
RawRead/RawWrite + 64-bit direct + XMM128/FPU80 single-translate,
observers-null gated, watch/MMIO/page-cross fallbacks) — no
resolver/whitelist change, active every mode; 8 new
`MemoryFastPathTests` (1367/1367 tests), format clean, 4-leg golden
gate PASS (`D:\\spec-soak\\slice21`: triangle spec 102.1 s + menu spec
203.8 s SHAs exact, `vec=0`, `verifyFailures=0`, anchors exact;
menu cache SHA exact = flag-off byte-identical; `--block-exec`
cross-check SHA exact). Walls single-run only, no claim. Flag stays
opt-in.
**§162 DONE (2026-10-17, direct):** memory-path slice 2
(Read/WriteMemoryBytes bulk path: MMU-off whole-span copy via new
`PhysicalMemory.WriteSpan` + MMU-on page-chunked one-translate-per-page
with per-chunk MMIO/watch/fault-exact fallbacks; Fxsave 512 B goes
512 translates → 1–2) — 5 new tests; suite 1372/1372 at that point.
**§163 DONE (2026-10-17, direct):** memory-path slice 3 (4-entry
direct-mapped data-translate cache in `TranslateLinear` via new
`Mmu.TranslateDataFast`; WP fault replayed exact; flush/INVLPG/
PTE-store invalidation; page-table tracking now always-on so the
signal survives `--no-decode-cache`; hit-implies-SoftTLB-hit coherence
proof; WP test fail-pre-fix proven) — 3 new tests; suite 1375/1375,
format clean, joint 4-leg golden gate PASS (`D:\\spec-soak\\slice22`:
triangle spec 98.9 s + menu spec 187.9 s SHAs exact, `vec=0`,
`verifyFailures=0`, anchors exact; menu cache 207.4 s SHA exact =
flag-off byte-identical; `--block-exec` SHA exact). Walls single-run
only, no claim; fresh median-of-3 (§164: menu 1.070x / triangle
1.164x — ratios flat vs §160, but ~7% menu / ~3–4% triangle ABSOLUTE
on both modes) → default-flip decision: flag stays opt-in. ktest
smoke PASS 7/7 alongside (02:12).
**§165 DONE (2026-10-17, direct):** memory-path slice 4 (rep movs/stos
span path: page-chunked one-translate-per-side + order-exact direct RAM
elements; ZF-dependent/IO strings stay legacy; 16-bit/64K-wrap and 4 GB
fallbacks) — caught a real ESI-on-stos bug via the golden gate (unit
tests all green, triangle halted at 6.6M; fixed + ESI pin + 10M-step
EIP-identical probe); 10 new tests, suite 1385/1385, format clean,
4-leg golden gate PASS (`D:\spec-soak\slice23`: triangle spec 82.2 s +
menu spec 154.7 s SHAs exact, menu cache 166.0 s byte-identical,
`--block-exec` SHA exact; single-run walls only, §164 median stands).
Flag stays opt-in.
**§167 DONE (2026-10-17, direct):** dispatch slice 5 (Cli/Sti continue
blocks — 2-token `Terminates` change, still fallback-interpreted;
behavior-neutral by construction since delivery polls per instruction;
dispatches triangle −32% / menu −14% with interrupts bit-identical
1883/3153) — 5 new `BlockFormationTests`, suite 1390/1390, format
clean, 4-leg golden gate PASS (`D:\spec-soak\slice24`: triangle spec
77.2 s + menu spec 145.4 s SHAs exact, menu cache 158.3 s control-leg
exact, `--block-exec` SHA exact; single-run walls only). Flag stays
opt-in.
**§168 DONE (2026-10-17, direct):** shape slice 6 (Cli/Sti spec
handlers — verbatim IF-bit bodies + resolver cases; fallbacks triangle
113.3M→185k / menu 34.1M→1.7M with Cli/Sti gone from both tails;
instructions + interrupts 1883/3153 identical) — differential + 2 pins,
suite 1391/1391, format clean, 4-leg golden gate PASS
(`D:\spec-soak\slice25`: triangle spec 73.0 s + menu spec 142.8 s SHAs
exact, menu cache 160.3 s control-leg exact, `--block-exec` SHA exact;
single-run walls only). Flag stays opt-in.
**§169 DONE (2026-10-17, direct):** median-of-3 post-slices-5+6
(`D:\\spec-soak\\median25`, 12/12 PASS): menu **1.084x** (156.28/144.16),
triangle **1.231x** (89.88/73.00) — reads as real (spec-only gains
−4.8%/−1.1% vs cache controls drifting the other way; magnitudes track
fallback volumes; tight triangle-spec legs). Specialization declared at
asymptote; emitter (§138) is the next lever. Flag stays opt-in.
**§170 DONE (2026-10-17, direct):** shape slice 7 (Adc/Sbb × all
forms/sizes with live carry + Fldcw/Fnstcw/Emms/Fwait one-liners; 34
handlers + rows; stale carry-live label retired) — 44-shape
differential (carry 0+1) + 34 pins, suite 1392/1392, format clean,
4-leg golden gate PASS (`D:\spec-soak\slice26`: triangle spec 74.4 s +
menu spec 144.3 s SHAs exact, menu cache 155.1 s control-leg exact,
`--block-exec` SHA exact; fallbacks −52%/−28%, interrupts identical;
single-run walls only). Flag stays opt-in.
**§171 DONE (2026-10-17, direct):** first codegen E1 (DynamicMethod
block-tail emitter behind opt-in `--emit`: per-head cached runners with
direct static calls, all boundaries retained, invalidation-gated;
DecodeCacheEntry.Instruction is now an addressable readonly field) — 11
emit differentials incl. host-counter exactness, suite 1403/1403,
format clean, 8-leg golden gate PASS (`D:\spec-soak\slice27`: SHAs
exact both titles, vec=0, anchors exact, interrupts identical
1883/3153). VERDICT wall-negative: emit 1.93x/6.50x slower than spec
(triangle/menu) — per-runner construction+first-JIT (~0.7 ms) cannot
amortize over short churned tails (menu builds 1.34M distinct heads);
§138 negative confirmed at title scale. Flag stays opt-in (zero impact);
E2 scoped as threshold-tiered emission. Post-fix triangle-emit 1B
re-run SHA exact with serve counters bit-identical; 50M counter-diff
fully identical incl. decode hits (pending exits earn no hit -- the poll
precedes it -- pinned by a 7-position sweep).
**§172 DONE (2026-10-17, direct):** tiered emission (lazy runner build
at 8th head visit + runner cache on the Block, killing the head-key
dict and silent cap + per-visit lookup + Context hoisted in the
emitted prologue) -- 18 emit differentials (incl. steady-state
pending sweep), suite 1410/1410, format clean, 8-leg golden gate PASS
(`D:\spec-soak\slice28`: SHAs exact both titles, vec=0, anchors exact,
interrupts identical 1883/3153; triangle emit 71.6 s BEATS spec
73.0 s -- first codegen config at parity-or-better; menu emit
376.2 s vs spec 147.2 s, was 984.1 s). Builds collapsed by tiering
(triangle 80k-->9k, menu 1.24M-->432k) with identical emission volume
and bit-identical tallies. Remaining menu excess fits 432k warm-head
constructions: threshold tuning is the next knob. ktest smoke PASS
7/7 (01:42). Flag stays opt-in.
**§173 DONE (2026-10-17, direct):** threshold T=8-->64 (single const;
unit budgets extended, 18/18 green) -- 8-leg golden gate PASS
(`D:\spec-soak\slice29`: SHAs exact, vec=0, anchors exact, interrupts
identical; triangle emit 71.0 s beats spec 73.6 s again; menu emit
179.2 s / spec 153.2 s = 1.17x, was 2.56x at T=8). Builds menu
432k-->70k (triangle 9k-->3.6k) with identical emission volume and
bit-identical tallies. ktest smoke PASS 7/7 (01:40). Suite 1410/1410,
format clean. Flag stays opt-in; E2 lands here unless the T=256
timebox (below) wins cleanly.
**§174 DONE (2026-10-17, direct):** threshold T=64-->256-->1024 (two
one-const steps; unit budgets extended each time, 18/18 green) --
8-leg golden gate PASS (`D:\spec-soak\slice30`: SHAs exact, vec=0,
anchors exact, interrupts identical 1883/3153; triangle emit 66.0 s
< spec 76.0 s; menu emit 180.3 s ~= spec 182.2 s = parity, was
6.50x at E1). Builds menu 70k-->5.8k (triangle 3.6k-->1k).
Per-member emit is at/cheaper than the C# unrolled path; the serve
layer is no longer the bottleneck (E3 body inlining is next).
ktest smoke PASS 7/7 (01:50). Suite 1410/1410, format clean.
Flag stays opt-in (zero default impact).
**§175 DONE (2026-10-17, direct):** E3 scoped (measurement-first body
specialization; non-goals listed) + first median-backed emitter claim
(`D:\spec-soak\median31`, 12 legs, all SHAs exact): triangle emit
1.089x spec (74.29 vs 80.91 medians); menu emit 0.944x spec
(155.71 vs 146.97 medians -- 6% deficit; emit legs descend 171-->145
across reps, consistent with warmup, tested by a second triplet:
148.0/150.3/154.1 confirm r1 outlier, steady state at parity).
E3 Step 0 measured (menu 1B hotspot, same sequence as emit): Mov 33.8%,
502M RAM data accesses; missing per-shape split --> E3-0 scoped
(flag-gated served-shape tally) to rank families for E3-1
(Mov-mem inline leads by volume x depth).
**§166 DONE (2026-10-17, direct):** median-of-3 post-slice-4
(`D:\\spec-soak\\median23`, 12/12 PASS): menu **1.088x** (158.89/146.10),
triangle **1.161x** (91.62/78.93) vs §164's 1.070x/1.164x — ratios the
robust signal (absolutes shifted ~20% on identical binaries = host
shift, no attribution claim). Soak-script gate hardening (delete BMP
pre-run, fail on `Frame unavailable`, require BMP) after the stale-BMP
SHA hole. Flag stays opt-in.
**§157 DONE (2026-10-17, direct):** JIT slice 19 (SSE sqrt/rcp + packed
arithmetic/logicals/compares — 5 shared bodies, 20 mnemonics, all
continue blocks) behind `--spec-exec`; 1358/1358 tests, format clean,
2-leg spec golden check PASS (`D:\\spec-soak\\slice19`: menu 165.2 s SHA
`4EF1FBF6…`, triangle 85.6 s SHA `A1740DE0…`, both `vec=0`,
`verifyFailures=0`, anchors exact) + `--block-exec` cross-check PASS;
fallbacks menu 34.8M→34.6M / triangle −0.2M with Rsqrtss gone from both
tails. New tail: Cli/Sti, Movs/Stos (string, out), Rcl-imm, Sbb/Adc
(carry-live, out), Cdq, Pushf/Popf, Setcc. Flag stays opt-in.
**ktest smoke re-run (2026-10-16, post-slices 12–13): PASS 7/7** in
02:13 — known deltas matching, park break at exactly 792,800,535 steps
(bit-reproducible 3rd run in a row); SSE+x87 disturb nothing.
**ktest smoke re-run (2026-10-16, post-slices 14–17): PASS 7/7** in
01:49 — known deltas matching, park break at exactly 792,800,535 steps
(bit-reproducible 4th run in a row); x87-reg/shift-CL/Mul/int-remainder/
x87-compare disturb nothing.
**ktest smoke re-run (2026-10-17, post-slices 18–20): PASS 7/7** in
02:12 — known deltas matching, park break at exactly 792,800,535 steps
(bit-reproducible 5th run in a row); packed-SSE/SSE-compute/flags
disturb nothing.
**ktest smoke re-run (2026-10-17, post-memory-path-slice-1): PASS 7/7**
in 02:12 — known deltas matching, park break at exactly 792,800,535
steps (bit-reproducible 6th run in a row); RAM-direct + wide-access
fusion disturb nothing.
**ktest smoke re-runs (2026-10-17, night slices 2–7): PASS 7/7** after
§§162–163 (02:12), §165 (01:45), §167 (01:37), §168 (01:37) and §170
(01:34) — known deltas matching every time, park break
bit-reproducible throughout; Bytes bulk, xlat cache, rep-string span,
Cli/Sti continuation+handlers and Adc/Sbb+FPU-control disturb
nothing.
**The wave runner is retired (operator decision 2026-10-15).** `tools/wave/`
deleted; no further waves. Development continues directly (no BOSS protocol, no
worktrees, no detached servers). Standing operator rule: NO real Xbox games —
all title work uses `data/test-samples/`. Next JIT step: median-of-3 wall
confirmation for the thin menu margin; DynamicMethod stays rejected unless new
numbers say otherwise. NXDK toolchain (operator/host) still the mesh blocker.
`F36A2`/LBA 8 CLOSED as no-bug (§73).
**`RemoteX`** dash (XBApp, static IP); streams + carved runner in temp.
M5 (`VshProgram` interpreter — mux mapping and ORB polarity corrected in §42,
transform constants, attribute fetch, element draws), M6 (per-slot texture
state, swizzled/linear sampling), M7 (alpha/stencil/depth/blend/mask
fragment pipeline) and M8 (flip present events with at-flip surface capture,
`XGrid.Graphics/DisplayFrame` BMP export, CLI `--frame`) are implemented and
unit-tested. **Pixel accounting:** all pixels still come from the legacy
fullscreen inline draws; program draws paint ~0 px in the black state-0 menu.
The §42 c58/c59 "no-ops on xemu too" claim was wrong — xemu feeds c58/c59
from the viewport methods; §71 implemented all 11 aliased constant writers.
Steady-state program draws validated live in §§57–58 (real shaders, clip
proven guest-side). The presented frame
(§43) is 640×480 from surface `0x32A4000`, opaque black — the flip chain
presents the dashboard surface, not the `0x1000` early-boot region.
The 2.5B content scan (§45) gave way to the evox.ini re-save at 3.2B
(§47), complete at 4.0B with post-save disk.bin reads, GPU stirrings and
the 2×2 MB Shadow-RAM surface upload (§48–51) — landing in RAM top via
the write-combined alias (§50), 59% BGRA pixels. Menu-idle since
§51 (5B, kernel delay-loop EIP `0x800153D3` pre-NIC; live render loop
EIP `0x2D3B9` post-NIC) with NIC link-up (§52–53) + enumerated USB pad
(§54–55) + latched `--pad` injection (§56, guest never polls); C0
viewport-baked clip proven guest-side (§57–61). Indexed draws are fully
traced through RAM arrays to oPos (§58). Remaining M5 gaps: inline
`SET_VERTEX_DATA*` immediate path and `DRAW_ARRAYS` ranges. Test media
for future DVD/XDVDFS work: `H:\Tests\XBOXTest` (37 Redump ISOs),
`E:\Microsoft Xbox` (945 CHDs) — read via `ChdDvdImage` (§66).
(see §13.5). Reference: xemu
`hw/xbox/nv2a/pgraph/` (`methods.h.inc`, `draw.c`, `surface.c`,
`vsh-prog.c`), `nv2a_regs.h`. Tools: `--break-log <addr>`, `--pgraph`/`--pgraph-max`, `--pad-*`, `--eip-hist N`,
`--ramdump`/`--ramin`, `--fb`/`--frame`, `--writedump`, `sector`, objdump. Evidence
trail: research-notes §30-61.

Phase 7 progress: the CPU gained far control transfers (JMP/CALL ptr16:16/32 and m16:16/32, RETF),
segment loads/stores (MOV Sreg, PUSH/POP Sreg incl. FS/GS), and CS.D-driven default operand size
(`XboxContext.CodeSegment32` set from the GDT descriptor on CS loads, cleared at reset). With the
real MCPX 1.0 ROM: reset vector -> LGDT/LIDT -> CR0.PE -> far jump `0x0008:0xFFFFFE00` -> flat
protected mode -> boot-script interpreter (9-byte opcodes in the BIOS: PCI config, SMBus, MMIO
writes) -> decrypt/copy loop -> MCPX enables paging -> jumps to the decrypted 1BL at 0x90000.
The 1BL sets up MTRRs/PAT, builds its own page tables (4 MB pages), enables caching + WP + NE,
disables the ROM overlay, copies itself to 0x400000, then runs from the high-RAM alias and sets up
its own GDT/IDT/TSS (`LTR`) before executing `HLT` (waiting on an interrupt source) at ~24M
instructions. `xgrid boot --bios <path> [--mcpx <path>] [--eeprom <path>] [--hdd <path>] [--dvd <path>] [--steps N] [--trace N]
[--trace-start N] [--trace-regs] [--dump addr[,len]] [--ramdump <path>] [--frame <path>] [--pgraph <path> [--pgraph-max N]]
[--writedump <dir>] [--pad-buttons <set> --pad-analog <hex> --pad-sticks <lx,ly,rx,ry> [--pad-step N] [--pad-script <set@step[+hold],...>]] [--eip-hist N]` runs user firmware and prints the final CPU/device state.
Remaining for M1: none — M1 (BIOS execution) is complete; the boot chain now runs
MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard DMA load. The paragraphs below are
historical (2BL-era forensics kept for reference).

Kernel-phase diagnosis (session 2026-10-09; all with Complex_4627 + mcpx_1.0 + user eeprom/hdd):
**RESOLVED — kept as history.** The CMPSB mismatch below was the SMBus word-read bug
(kernel got `0x00A8` instead of `0xC4A8`, built a decimated WCHAR table); the fix
(compose consecutive byte registers for multi-byte reads, matching HW/QEMU) let the
gate fall through, after which the IDE path ran and Gate 1 completed (kernel loads the
EvoX dashboard via DMA; see M9 + research-notes §23, §31). The `0x800426D8` park was
later diagnosed and passed (research-notes §32), as were the ACI/APU gates (§33);
the animation + XBE launch path is now unblocked (M9 Gap 1d, research-notes §34).
- Idle loop decoded (`0x8001B02E`: STI/NOP/NOP/CLI, empty-DPC check at `[EBP]`, wake flag at
  `[EBX+0x2C]` = `0x80035C08` = PRCB NextThread). Parks at instruction 25,062,717 with zero
  interrupts delivered; 50M/100M runs are frozen past that point (only tick counts grow).
- The parked thread (`0x80035A90`) is the idle thread itself (its stack is the live ESP,
  `WaitBlockList=NULL`). A second thread (`0xD0008BF8`, prio 8, own 12 KB stack) is dispatched
  right after parking via `KiFindReadyThread` → `SwapContext`; it runs ~140K instructions
  (encoder writes, SMC handshake/scratch/LED, full 256-byte EEPROM word sweep, 9 RTC reads,
  PIC setup, one `KeStallExecutionProcessor` delay) and exits with status 0 at ~25.2M through
  `PspExitThread`. Afterwards: no ready threads, empty DPC list, no timers — true stall.
- Exit cause: 5-byte `CMPSB` at `0x800551B6` (HMAC-SHA1 worker `0x55107`) mismatches on byte 0
  (computed `83 F7 8E BE…` vs expected `A8 00 F9 00 BD…`, the latter = EEPROM `hash[20]` even
  bytes as WCHAR, verified byte-exact against the image across all 128 table entries). Mismatch
  → cleanup → `PspExitThread(0)`; match path (`LEAVE/RET`) returns into the IDE-unlock path
  (`0x80055C30` consumer, IDE taskfile code at `0x80055C67+`), which never runs.
- Unblocked along the way (each verified by boot): NV2A MMIO region (FD unmapped buckets gone,
  kernel probes hit real regs); SMBus completion IRQ + ACPI-byte-1 routing (was the original
  idle cause — kernel is interrupt-driven here, 2BL-era code polls); AAM/AAD (fixes BugCheck
  `0x1E`/`STATUS_ILLEGAL_INSTRUCTION` in the RTC/BCD path).
- Ruled out as mismatch sources: EEPROM bytes (table verified 128/128 against the image),
  SMC challenge/IDs (fixed, xemu-identical), EEPROM image self-consistency (custom double-SHA1
  hash MATCHES under 1.0 IVs, region = 1, both CRCs OK, serial/MAC valid), RTC epoch (host-time
  experiment: identical suicide at the same instruction), AV pack (`hdtv = 0x01` both sides),
  SMC `INTSTATUS.TRAYCLOSED` (now set at init per xemu; suicide unchanged at the same
  instruction), scratch init (`SHORT_ANIMATION`, matching xemu `skip_boot_anim`; suicide
  unchanged), CPUID (now at full parity: leaves 0/1/2 incl. Xbox-faked cache bytes, model
  8/stepping 10, identical feature bits, extended leaf reports 0, out-of-range clamps to
  leaf 2 — all per QEMU `target/i386/cpu.c`).
- A/B proven mode-invariant (identical bytes in fast-run vs single-stepped boots despite
  different IRQ timing): all crypto inputs are static and verified, so the gate outcome is
  input-determined. Pre-wake window trace confirms no divergent live reads (no CPUID/RDTSC/
  SMBus/NV2A; only PCI/RTC-program/RTC-status/PIT/PM/Super-I/O writes and one Super-I/O
  config read). Conclusion: xemu reaches the same gate with the same outcome on this exact
  no-DVD retail-dashboard configuration; the user's working xemu boots must differ in
  effective configuration (DVD game path with `dvd_path` set, and/or custom BIOS per the
  user's own hypothesis). Next: verify with an xemu no-DVD dashboard boot, or proceed down
  the DVD/game path (needs ATAPI) and revisit dashboard with matched images.
- Incidental fixes this session: batching determinism (timer remainders accumulate, so totals
  no longer depend on advance granularity; CLI trace/break loops now share `XboxMachine.Advance`
  including NV2A PTIMER), `--watch start,end` CLI flag for physical watchpoint ranges.
- Dashboard-storage path, host side (session 2026-10-09): `XGrid.XboxFs` FATX reader verified
  against `xbox_hdd.qcow2` (5 superblocks at `0x400/0x177400/0x2EE400/0x465400/0x55F400`,
  cluster size 32; u16 FAT below 0xFFF5 clusters (`FFF8` reserved, `0000` free, `FFFF` end),
  u32 above (`FFFFFFF8` free, `FFFFFFFF` end); 64B entries, `0xFF` ends / `0x00` frees a slot,
  root = first data cluster auto-detected by entry density). The `0x465400` partition is a
  softmod dashboard (`xboxdash.xbe` 770048 B, `evox.ini`, HeXEn, BIOS/EEPROM/HDD-key backups);
  `0x55F400` holds `TDATA/UDATA/CACHE/Dash/SAVES`. `XGrid.Xbe` parses the real dashboard
  (retail `EvoX`, entry `0x12A17` after XOR decode, 1 section, no TLS/imports). Also fixed
`Qcow2DiskImage` to return zeros for zero-flagged clusters (none present here, per QEMU
`qcow2.h`). Remaining on this path: ATA SECURITY (drive reports unlocked, matching QEMU
default, so likely unneeded), guest-side FATX/XBE use, ATAPI DVD. (USB OHCI MMIO done §36.)
- xemu differential via GDB stub (decisive, 2026-10-09): with identical images, xemu's kernel
  reaches the same `CMPSB` at `0x800551B6` with the same thread/stack layout, but its A/B
  buffers both hold the raw EEPROM hash (`A8 C4 F9 56…`, match) while ours hold digest/WCHAR
  (mismatch). The HMAC wrapper (`0x2F641`) fires repeatedly on xemu (boot + steady state), and
  post-match it is called a third time keyed by the match product at `0x8003B008` — confirming
  the match feeds the IDE-unlock crypto (`0x80055C30` consumer). So the fill paths diverge
  upstream of the compare; the selector is still open (all static inputs verified identical).
- FATX ground truth (host-side, verified against `xbox_hdd.qcow2`): 5 superblocks
  (`0x400/0x177400/0x2EE400/0x465400/0x55F400`, cluster size 32, root field 1); u16 FAT below
  0xFFF5 clusters (`FFF8` reserved, `0000` free, `FFFF` end) and u32 above (`FFFFFFF8` free,
  `FFFFFFFF` end); 64B entries (len/attrs/42-char ASCII name/start/size; `0xFF` ends a dir,
  `0x00` frees a slot; case-insensitive lookup); root = first data cluster (auto-detected by
  entry density). The `0x465400` partition holds a softmod dashboard (`xboxdash.xbe` 770048 B,
  base `0x10000`, entry `0x10178`, `evox.ini`, HeXEn, `bios.bin`/`eeprom.bin`/`hddkey.bin`
  backups); `0x55F400` holds `TDATA/UDATA/CACHE/Dash/SAVES`. `xgrid fatx ls/cat` verified
  end-to-end (sizes match, XBEH parses). Next on this path: ATA SECURITY (unlock), XBE loader
  (entry `0x10178`, SHA1 `223a825c…` recorded, file itself stays out of the repo).
Phase 5 notes: `XGrid.Bus` now provides `IoSpace` (dynamic windows), `PciBus` (standard 0xCF8/0xCFC
config mechanism, BAR probe/assign, command register), `PciHostBridge` (0:0.0), `PciAgpBridge`
(30:0.0), `LpcController` (1:0.0: INT/PIRQ/ACPI routing with hardware defaults, MCPX ROM overlay
disable on config 0x80 bit 1, PM1 status/enable, 24-bit PM timer), `Pic8259` (master/slave,
ICW/OCW, cascade, poll, EOI), `Pit8254` (3 channels, modes 0/2/3, latch), and `XboxBus` wiring.
`XboxCpu` gained an `IIoBus`, IN/OUT (0xE4-0xE7, 0xEC-0xEF), and `IInterruptSource` injection
with IF-gated acknowledge. Deferred within Phase 5: DMA helper, ELCR,
Super-I/O UART fidelity (open-bus 0xFF today), GPE beyond PM1/timer.
(USB OHCI MMIO done §36 + schedule engine §54; APU/ACI MMIO done §33.)

Phase 3 notes: x87 values are stored as true 80-bit extended format, but arithmetic and
transcendental operations round through IEEE double (FSIN/FCOS/FPATAN/F2XM1/FYL2X/FPREM/FRNDINT/
FSCALE/FXTRACT) rather than 64-bit-significand soft-float; integer conversion, loads/stores,
constants, and comparison use the full extended format. MMX registers alias the x87 significand
as on hardware; SSE1 executes scalar loops with single-precision floats (no SSE2, per PIII).
FWAIT does not yet deliver #MF for unmasked x87 exceptions.

## Conventions for all phases

- **Names**: solution `XGrid.sln`; assemblies `XGrid.*`; test assemblies `XGrid.*.Tests`.
- **Target**: `.NET 11` (`net11.0`), SDK pinned in `global.json`; `XGrid.Core` must remain
  NativeAOT-compatible where practical (no reflection in hot paths, no `dynamic`).
- **Language**: C# 15 (`LangVersion 15`), nullable enabled, implicit usings off (explicit imports
  for clarity), file-scoped namespaces, `TreatWarningsAsErrors true`, deterministic builds.
- **Style**: no comments except where behavior is non-obvious; no `#region`; public API documented
  with XML docs. Hot paths follow plan section 31 (no LINQ/boxing/allocation).
- **Licensing** (superseded 2026-10-23): XGrid is GPL-2.0 and may port code from
  xemu/QEMU/Cxbx under `docs/porting-policy.md` (SPDX + `Ported from` + notices).
  XISOSharp (MIT) is a dependency behind an interface/adapter and keeps its
  notices; other third-party packages are listed in `THIRD_PARTY_NOTICES.md`.
- **Definition of done per phase**: builds clean; all tests pass; new public APIs documented;
  analyzers/format clean; acceptance criteria met; research note updated if a fact changed.
- **Test naming**: `Method_Scenario_Expected`. Devices get a register/state test; parsers get
  boundary/corruption tests; fuzz targets added per plan section 44.

## Phase 0 — Project Bootstrap (detailed) [DONE]

**Goal**: a compiling, tested, CI-gated solution with engineering standards and a minimal Core.

**Prerequisites**: none.

**Tasks**
1. `dotnet --info`; pin installed .NET 9 SDK in `global.json` (`rollForward: latestFeature`).
2. Create `Directory.Build.props` with shared properties (TargetFramework, nullable, implicit
   usings, langversion, deterministic, warnings-as-errors, analyzers, source-link optional).
3. Create `Directory.Build.targets` (shared test/coverage hooks if needed).
4. Create `Directory.Packages.props` (central package management; pin xUnit, analyzers, etc.).
5. Create `.editorconfig`, `.gitignore`, `README.md`, `LICENSE` (placeholder until decided),
   `CONTRIBUTING.md`, `SECURITY.md`.
6. Create `XGrid.sln` and the project skeleton:
   - `src/XGrid.Core`, `src/XGrid.Cpu`, `src/XGrid.Memory`, `src/XGrid.Bus`, `src/XGrid.Mcpx`,
     `src/XGrid.Nv2a`, `src/XGrid.Audio`, `src/XGrid.Storage`, `src/XGrid.XboxFs`, `src/XGrid.Xbe`,
     `src/XGrid.Input`, `src/XGrid.Graphics`, `src/XGrid.Debug`, `src/XGrid.Host`,
     `src/XGrid.Cli`.
   - `tests/XGrid.Core.Tests`, `tests/XGrid.Cpu.Tests`, `tests/XGrid.Memory.Tests`,
     `tests/XGrid.Integration.Tests`.
   - `benchmarks/XGrid.Benchmarks` (BenchmarkDotNet).
7. Implement the minimal Core surface: `EmulatorState` enum, `IEmulator` interface (plan 6.1),
   `IEmulatedDevice` (plan 6.3), `IXboxDevice` (plan 10), and a structured `XGridLog`/category
   skeleton (plan 33).
8. Create `.github/workflows/ci.yml`: restore, build (Release), test, `dotnet format
   --verify-no-changes`, analyzer enforcement, on Windows/Linux/macOS.
9. Create `scripts/build.ps1`, `scripts/test.ps1`, `scripts/format.ps1` (and `.sh` equivalents).
10. Add a first `XGrid.Core.Tests` test that fails if the lifecycle enum/contract changes.

**Interfaces (initial)**
```csharp
public enum EmulatorState { Created, Initialized, Running, Paused, Stopped, Faulted }
public interface IEmulator {
    EmulatorState State { get; }
    void Initialize(); void Reset(); void Start();
    void Pause(); void Resume(); void Stop();
}
public interface IEmulatedDevice { void Reset(); void Tick(ulong cycles); }
public interface IXboxDevice { string Name { get; } void Reset(); }
```

**Acceptance**
- `dotnet build -c Release` succeeds on Windows/Linux/macOS in CI.
- `dotnet test -c Release` succeeds with zero failures.
- `dotnet format --verify-no-changes` succeeds.
- No frontend/UI reference from Core/Cpu/Memory/Bus/devices.

**Risks**: installer/SDK availability; analyzer churn. Mitigation: pin SDK, start with a small
analyzer set and tighten later.

## Phase 1 — Emulator Core (detailed) [DONE]

**Goal**: deterministic lifecycle, cycle clock, device scheduler, no frontend dependency.

**Tasks**
1. `XGrid.Core`: `Emulator` implementing `IEmulator` state machine with explicit transitions and
   invalid-transition guards (throw `InvalidOperationException` only for developer errors).
2. `EmulatedClock` (plan 6.2): cycle counter, host-time mode, deterministic fixed-step mode;
   single source of emulated time used by all devices.
3. `Scheduler` / event queue (plan 6.3): device registration, coarse `Tick(cycles)` first; design
   for later event-driven scheduling; deterministic ordering by registration + event time.
4. `XGrid.Core.Diagnostics`: category logger (plan 33) with source-generated, allocation-free
   `LoggerMessage`-style methods; categories CPU/MEMORY/MMIO/PCI/MCPX/NV2A/APU/USB/IDE/FATX/
   XDVDFS/XBE/KERNEL/INPUT/AUDIO/VIDEO/JIT.
5. `Configuration` model + loader (plan 34): system/bios/mcpx/hdd, graphics backend/resolution,
   cpu backend, audio, input; validated with clear errors (`InvalidFirmwareException` etc.).
6. Deterministic mode flag end-to-end.

**Tests**: determinism (two runs identical state after N cycles); invalid transition matrix;
scheduler ordering; clock arithmetic; config parse round-trip.

**Acceptance (plan 6)**: init/reset deterministic; fixed-cycle state reproducible; no frontend
dependency.

## Phase 2 — Memory System (detailed) [DONE]

**Goal**: correct physical RAM, endianness, memory map, MMIO routing, RAM fast path.

**Tasks**
1. `XGrid.Memory`: `PhysicalMemory` (64/128 MB flat `byte[]`) with `Read8/16/32/64`,
   `Write8/16/32/64`, `Span<byte> GetSpan(uint, int)`; bounds-checked with deterministic behavior.
2. `Endian` helpers: explicit little-endian reads/writes in a `MemoryMarshal`-based static class.
3. `AddressSpace`: region table with `(base, size, read, write, priority)`; RAM fast path
   (range check → direct array); MMIO slow path; aliases/mirrors for RAM (0x0C000000), flash
   mirrors (0xFF000000), MCPX ROM overlay (0xFFFFFE00), PCI hole alias.
4. `IMmioDevice` (plan 7.4) + registration API.
5. Invalid-address policy: deterministic (configurable: fault/log/sentinel), documented.
6. `XGridMemoryTool` (plan tools) later; for now expose an inspection API used by the debugger.

**Facts**: use the consolidated address map in `docs/research-notes/xgrid-architecture-decisions.md`,
validated against XboxWiki `References2/XboxDevWiki/html/Memory.html` (Memory): 64 MB RAM 0x00000000-0x03FFFFFF,
NV2A regs 0xFD000000-0xFDFFFFFF, APU regs 0xFE800000-0xFE87FFFF (512 KiB; `ApuSize` corrected
2026-10-10), ACI regs 0xFEC00000-0xFEC00FFF (`IoApicBase` was a misnomer; renamed `AciBase`
2026-10-10 — the window is ACI MMIO), USB0 0xFED00000-0xFED00FFF, USB1 0xFED08000-0xFED08FFF, nvnet
0xFEF00000-0xFEF003FF, flash 0xFF000000-0xFFFFFFFF, MCPX ROM 0xFFFFFE00-0xFFFFFFFF.

**Tests**: read/write each width; unaligned access policy; endianness; MMIO routing; alias
correctness; invalid addresses; RAM benchmark harness.

**Acceptance (plan 7)**: memory tests pass; MMIO routing works; RAM benchmark exists; invalid
addresses deterministic.

## Phase 3 — x86/Pentium III CPU (detailed) [DONE]

**Goal**: table-driven interpreter passing an extensive instruction regression suite.

**Tasks**
1. `XGrid.Cpu`: `XboxContext` (all registers in plan 8.1), `Eflags`, segment/descriptor state,
   CR/DR, FPU/MMX/SSE state.
2. `Decoder`: prefix/opcode/ModRM/SIB/immediate decoders, address/operand-size handling;
   `DecodedInstruction` (plan 8.3); no external decoder dependency.
3. Interpreter tiers in order (plan 8.4): T1 NOP/MOV/PUSH/POP/XCHG/LEA; T2 arithmetic/logic;
   T3 JMP/CALL/RET/Jcc/LOOP/INT/IRET; T4 shifts/rotates; T5 MUL/DIV; T6 strings; T7 system;
   T8 x87; T9 MMX; T10 SSE.
4. Exact flags: CF/PF/AF/ZF/SF/OF/DF/IF/TF (plan 8.5). Eager first; lazy only if profiled.
5. Exceptions (plan 8.6) through a guest IDT model: #DE, #UD, #GP, #PF, #BP, #DB; host exceptions
   never leak.
6. CPU test framework (plan 8.7): JSON instruction tests + an assembly-based golden harness
   (write our own NASM files using the `harness.inc` convention).
7. `XGrid.CpuTestRunner` tool.

**Facts**: PIII CPUID/MSR quirks in `docs/research-notes/xemu.md` section 2.

**Tests**: per instruction family, flags, exceptions, addressing modes, prefixes, FPU/MMX/SSE.

**Acceptance (plan 8)**: interpreter passes the instruction regression suite before boot work.

## Phase 4 — x86 MMU (detailed) [DONE]

**Goal**: segmentation as required, paging, TLB, correct page faults.

**Tasks**: page directory/tables (4 KB and 4 MB), CR0/CR3/CR4 handling, A/D bits, permission
checks, TLB with invalidation on CR3/INVLPG, page-fault error codes, virtual-address execution.
Synthetic page-table tests and TLB invalidation tests; CPU executes code through virtual addresses.

**Acceptance**: page-table tests pass; faults correct; TLB invalidation tested.

## Phase 5 — Xbox Bus & Interrupts (detailed) [PARTIAL - I/O, PCI, LPC, PIC, PIT, RTC, USB/XID, nvnet MMIO done; DMA helper, ELCR, Super-I/O, GPE pending]

### PCI device topology (verified against xemu `hw/xbox/xbox.c`)

Process directive: **consult the reference projects first** (`References2/xemu-0.8.137`,
`References2/xemu-Playtest-v2` (newer NV2A/perf cross-check), `References2/xqemu-master`,
`References2/Cxbx-Reloaded-master`,
`References2/envytools-master` for NV2A method/register/XF facts, `References2/nv2a-trace-master`
for pushbuffer/PGRAPH tracing facts, `References2/XboxDevWiki`, `References2/CopettiDocumentation`
for high-level architecture) to
confirm hardware layout, IDs, and behavior before implementing or debugging a device. Xemu is
the LLE reference; Cxbx is HLE (kernel reimplementation, no NV2A emulation) and only
help for kernel-facing behavior. (Xermu was once listed here too — it never existed;
see the "Withdrawn reference" note in section 0.)

| Slot | Device (vendor:device, revision) | xemu source | XGrid status |
| --- | --- | --- | --- |
| 0:0.0 | Xbox PCI host bridge (10DE:02A5, rev A1, multi-function) | `xbox_pci.c` | done |
| 0:0.3 | Memory-controller stub (xemu: QEMU `pci-testdev` placeholder) | `xbox_pci.c` | not emulated |
| 0:1.0 | MCPX LPC (10DE:01B2, rev B2, multi-function) | `xbox_pci.c` | done |
| 0:1.1 | nForce SMBus (10DE:01B4, rev B1) | `xbox_pci.c` | done |
| 0:2.0, 0:3.0 | nForce OHCI USB (10DE:01C2, rev B1, class 0C0310, BAR0 0x100) | `hcd-ohci-pci.c` | config space + MMIO schedule engine/XID (§36, §54) |
| 0:4.0 | nvnet Ethernet (10DE:01C3, rev B1, BAR0 0x400 + IO 0x8) | `nvnet.c` | config space + MMIO (§52) |
| 0:5.0 | MCPX APU (10DE:01B0, rev B1, BAR0 0x80000) | `apu.c` | config space + MMIO (§33) |
| 0:6.0 | MCPX ACI (10DE:01B1, rev B1, BAR2 0x1000) | `aci.c` | config space + MMIO (§33) |
| 0:9.0 | Xbox IDE (10DE:01BC, rev B1, IRQ 14) | `piix.c` (`#ifdef XBOX`) | done |
| 0:30.0 | AGP bridge (10DE:01B7, rev A1); secondary bus 1 | `xbox_pci.c` | done |
| bus 1, 0:0.0 | NV2A (10DE:02A0, rev A1, BAR0 16 MB, BAR1 64 MB prefetchable) | `nv2a.c` | config space + full PFIFO/PGRAPH model (M5–M8) |

Bus routing: configuration cycles to bus N are forwarded only to the bridge whose programmed
secondary bus number (register 0x19) equals N. Unclaimed buses return all-ones (no aliasing).

Status after this step: the PCI stubs and secondary-bus routing are in place, and the IDE ID now
matches xemu's Xbox variant. The 2BL still idles at the same scheduler loop (100M instructions,
IDE accesses 0, interrupts 108). Diagnostics added: `AddressSpace.UnmappedAccessCounts` (1 MB
buckets, printed by CLI) and `PciBus.ConfigAccessCounts` (per bus+devfn, printed by CLI).

SMC comparison (`hw/xbox/smbus_xbox_smc.c` vs our `SmcDevice`): register maps match (version,
tray, AV pack, error, INTSTATUS clear-on-read, non-zero challenge bytes 0x1C-0x1F, ignored
0x20/0x21 handshake writes). Two minor deltas, no code change: POWER register (0x02) writes
have no effect in ours (no reset plumbing), and xemu sets TRAYCLOSED intstatus + asserts SCI at
realize while ours starts with intstatus 0 (IRQ9 is masked by the 2BL anyway).

Measured at 100M instructions: the 2BL enumerates every PCI function — host 10, memctl-slot 5
(all-ones, not emulated), LPC 20, SMBus 3, USB0/USB1 139 each, nvnet 128, APU 128, ACI 130,
IDE 128, AGP 134, bus-1 NV2A 133 (so the 2BL programs the AGP secondary bus itself). NV2A MMIO
gets 30 one-time probes (FD0=28, FD1=2); USB/APU/nvnet MMIO is never touched, so no driver polls
a status bit. Above-RAM probes (045=4, 065=8) look like one-time memory-size detection. The 2BL
sees all devices and then parks: the wait is for a runtime event, not a missing function.
SMC transaction log (`SmcDevice.ReadCounts/WriteCounts`, printed by CLI) at 100M: R01=1, R1C-R1F=1
(challenge request), R04=1 (AV pack), W01=2, W12=1, W13=1, W20=1, W21=1 (challenge handshake
writes, ignored as in xemu). No reads of tray state (0x03), error (0x0F), or INTSTATUS (0x11):
the 2BL is not waiting on the SMC either. Total: 529 tests pass.
PCI state line (CLI) at 100M: USB0/USB1/nvnet/APU/NV2A all cmd=0007 (IO+MEM+bus master) with
BARs assigned (USB 0xFED00000/0xFED08000, nvnet 0xFEF00000, APU 0xFE800000, NV2A 0xFD000000);
ACI BAR0 shows the 2BL's IO probe write (0xD001) since xemu registers no BAR0/1 there. IDE is
cmd=0005 (legacy IO + bus master, no MEM) with BARs unassigned — yet the legacy ports are never
touched, so storage init never starts. LPC routing registers are untouched defaults
(introute=0E065491, pirq=00031000), so every device IRQ is effectively masked. Drivers are
active but their MMIO is never read back and no interrupt can arrive: the boot thread is parked
before storage init, waiting on an event nothing posts.
Next: trace the timer ISR (vector 0x30 -> 0x8001B180 -> dispatcher 0x8001B9F0 -> C handler
0x8001CA64) to see what it does with each tick.
Done (1.5M-instruction trace + `--dump` thread decode): the running ISR is at **0x80030E4C**
(0x8001B180 never executes): EOI helper 0x80014F10, +1 ms tick accumulation, per-thread
KernelTime/Quantum accounting, IRET. DPC/timer-expiry branch never fires. Exactly one thread
exists (0x80035A90, Type=6, State=Running, KernelTime≈ticks, WaitBlockList=NULL); the
IdleThread/NextThread PRCB slots hold zeros/garbage. The wait is KiIdleLoop-on-empty-DPC-queue
with no waiter object: only a future device event unblocks it. Total: 530 tests pass.
Reframe from XboxWiki `References2/XboxDevWiki/html/Boot Process.html` (Boot Process): `--dump 0x80010000` shows
a valid MZ header, so the 2BL completed its whole program (MCPX ROM disable via LPC 0x80=0x02,
SMC challenge 0x1C-0x1F + response 0x20/0x21, kernel flash copy + RC4 + LZX to 0x80010000,
MZ/PE check, entry call) and we are in the KERNEL's idle loop. A 50M-instruction run shows
byte-identical device histograms to 100M (only tick/PIC-EOI counts differ): kernel cold-boot
init (PCI setup, SMC, Super-I/O debugger, RTC, driver BAR assignment) finishes before 50M and
the kernel parks with IDE and video-progress never started, matching the wiki's cold-boot list
minus its IDE/video items.
Next (kernel phase): map an NV2A MMIO region at 0xFD000000 so display init can proceed (the
wiki's PDE maps it cache-disabled, flags 0xFB; video likely precedes disk), then the
IDE+FATX+XBE dashboard path (Phases 8/10/11).

**Goal**: PCI enumeration, MMIO/IRQ infrastructure, timers, DMA, discoverable devices.

**Tasks**: PCI bus with config ports 0xCF8/0xCFC, BAR allocator, host bridge, AGP bridge; device
registry (`IXboxDevice`) with no central switch; IRQ lines + dual 8259 PIC with LPC routing
registers 0x64/0x68/0x6C; PIT 8254; ACPI PM timer (3.579545 MHz); RTC; DMA helper that reads and
writes guest RAM through `AddressSpace`.

**Facts**: IDs/routing in `docs/research-notes/xemu.md` sections 1 and 3.

**Acceptance (plan 10)**: devices register without modifying central switch statements.

## Phase 6 — MCPX (detailed) [PARTIAL - SMBus AMD756/EEPROM/SMC/encoder/ACI/APU + real MCPX ROM boot done; flash command set, GPE/GPIO, Super I/O pending]

**Goal**: boot firmware can initialize MCPX-dependent functionality.

**Order**: SMBus (AMD756) → EEPROM → Flash → SMC → USB integration → interrupts → timers.

**Tasks/facts**: SMBus register map (`xemu.md` 4.1); EEPROM layout + SHA1/RC4 security section and
generation (`xemu.md` 4.3); flash AMD/SST command set (open gap — xemu has no command model
and no other in-repo reference covers CFI; implement clean-room from datasheets); SMC register
and challenge/response map (`xemu.md` 4.2); ACPI PM/GPE/GPIO (`xemu.md` 4.5); Super I/O programming
model (`xemu.md` 4.6); ACI AC'97 (`xemu.md` 4.9).

**Tests**: per-device register/state tests (SMBus transaction, EEPROM read/write/wrap, flash
program/erase, SMC challenge, ACPI timer).

**Acceptance (plan 11)**: boot firmware initializes MCPX functionality.

## Phase 7 — Firmware & Boot (detailed) [PARTIAL - machine/overlay/CLI done; real boot through menu-idle done (§§31–61); ATA SECURITY/XBE-loader guest-side items pending]

**Goal**: execute real user-supplied firmware far enough to prove CPU/memory/basic hardware.

**Tasks**: BIOS loader (256 KiB/1 MiB, mirroring), MCPX 512-byte ROM overlay + disable semantics,
reset vector 0xFFFFFFF0, boot pipeline (plan 12), stage logging (EIP/CR0/CR3/EFLAGS/interrupts/
MMIO/PCI/page faults), `--deterministic`, milestone M1 trace. Keep nboxkrnl-style guest-kernel
path and HLE as later fallbacks.

**Acceptance**: M1 — C# CPU executes real firmware with correct basic hardware behavior.

**Findings (XboxWiki `References2/XboxDevWiki/html/Boot Process.html`, Boot Process)**: the 2BL program is fully
documented (MTRR, self-copy, PDE incl. GPU window flags 0xFB, MCPX ROM disable, SMC challenge,
IDE+NIC enable, LDT config, kernel RC4+LZX to 0x80010000, MZ/PE check, entry call) and our boot
matches it end to end — kernel MZ verified in RAM, so the paragraphs below about the 2BL
fatal path are superseded. We are in the kernel idle loop (research note 18); M1 holds through
the kernel handoff, and further progress is kernel-phase work (NV2A MMIO, IDE+FATX+XBE).

## Phase 8 — Storage (detailed)

**Goal**: access a real Xbox filesystem through emulated storage.

**Tasks**: IDE controller (model NVIDIA nForce if feasible; PIIX3-compatible acceptable first),
ATA commands (IDENTIFY, READ/WRITE, SET FEATURES, FLUSH, security IDENTIFY), PIO then bus-master
DMA; virtual HDD backed by random-access file with block cache/read-ahead; ATAPI DVD with
2048-byte sectors for XDVDFS; FATX read-only first (superblock/partition/FAT/dirs/files); host
tooling via XISOSharp adapter behind `IDiscImage`.

**Facts**: `cxbx.md` 6, `xisosharp.md` 2, `xemu.md` 7; XboxWiki `References2/XboxDevWiki/html/FATX.html`
(FATX) for the filesystem stage. Note: the boot kernel loads from flash (XboxWiki Boot Process,
page `51`), so disk I/O belongs to the dashboard phase, not firmware boot.

**Acceptance**: M2 — emulator accesses a real Xbox filesystem through emulated storage.

## Phase 9 — XBE Loader (detailed)

**Goal**: parse and map an XBE into emulated memory.

**Tasks**: header/certificate/section/library/TLS parsing (offsets in `cxbx.md` 2), XOR entry
point/kernel thunk decode per type, section mapping with shared-page head/tail semantics, image
base, entry point; `XbeImage` type (plan 14); `XGrid.XbeTool` with `info/sections/imports/extract`.

**Acceptance**: M3 — XBE parsed and mapped into emulated memory.

## Phase 10 — Xbox Kernel Interface (detailed)

**Goal**: minimum kernel services to boot known test software; trace-driven.

**Tasks**: declarative ordinal table (name, arg count, calling convention, data/function) derived
from `cxbx.md` 3; implement categories incrementally (memory, threads, events, IRQs, filesystem,
object manager, timers, device IO); strategy: Run → unimplemented call → trace → implement →
regression test. Real-kernel boot preferred; HLE fallback.

**Acceptance**: sufficient kernel compatibility for target test software.

## Phase 11 — NV2A (detailed)

**Goal**: render known test scenes; second major high-risk subsystem.

**Tasks**: device + block table (`xemu.md` 5.2 / `cxbx.md` 4.1); PFIFO channels/pushbuffers/DMA
objects/RAMHT/method dispatch; PGRAPH state shadow + classes + NV097 methods; surfaces/textures/
vertex/render state; memory handling (surfaces, textures, framebuffers, depth); `INv2aRenderer`
intermediate representation carrying `ShaderState`; Vulkan backend primary, OpenGL optional;
precompiled interpreter ubershaders.

**Acceptance**: M4 framebuffer → M5 2D → M6 3D → M7 shaders → M8 game rendering.

**Facts (XboxWiki)**: `References2/XboxDevWiki/html/NV2A.html` (NV2A MMIO/pipeline) plus
`NV2A_Vertex attributes`/`NV2A_Fixed Function Pipeline`/`NV2A_Vertex Shader`/
`NV2A_Pixel Combiner`/`NV2A_Surface Formats` (all under `References2/XboxDevWiki/html/`).
Guest page tables map the 16 MB control window cache-disabled (Boot Process `51`); BAR0 base
observed 0xFD000000 during real boot.

## Phase 12 — APU (detailed)

**Goal**: audio initialization and correct waveform.

**Tasks**: device registers, voices, DMA/audio buffers, mixing (32 float mixbins), DSP behavior
(GP/EP), resampling, host output behind `IAudioBackend`; implement DSP semantics independently
(reference implementations are GPL). Long-run drift test.

**Acceptance (plan 17)**: init works; known test waveform correct; no excessive drift.

## Phase 13 — USB & Controllers (detailed)

**Goal**: four controllers with rumble.

**Status**: OHCI host (SOF/HCCA/IRQ, §36), USB schedule engine
(control/bulk/periodic, done-queue + WDH, STALL/NAK, §54) and XID
controller protocol (descriptors/reports/capabilities per Cxbx facts)
DONE — guest enumerates first try (§55); `--pad` injection latched
(§56). Remaining: host input behind `IInputBackend`, rumble output,
pads 2–4, isochronous transfers.

**Facts (XboxWiki `References2/XboxDevWiki/html/USB.html`, USB)**: two MCPX OHCI controllers, MMIO per
standard OHCI spec Table 7-1; USB0 = PCI 0:2.0 IRQ1 (retail game-controller ports), USB1 =
PCI 0:3.0 IRQ9 (not enabled in retail titles). Firmware values (retail v1.6 PAL):
HcRevision=0x10 both; USB0 HcRhDescriptorA=0x01001204 (4 ports), USB1 =0x01000202 (2 ports);
HcRhDescriptorB DeviceRemovable=0, PortPowerControlMask=0. Front-panel ports 1,2,3,4 map to
USB hardware ports 3,4,1,2. Our stub slots/IRQs/BARs already match; MMIO + SOF interrupt is
the unblock candidate for the kernel idle wait.
**Implementation deltas (2026-10-11, §§54–56):** both controllers report
4 root ports (xemu `xbox.c` `num-ports=4`) with NPS (no power switching:
attached function reads connected without guest power writes); gamepad
on USB0 port 0 only; USB1 never touched by the guest (`acc=0` all runs).
OPEN: wiki says USB1 has 2 ports (`0x01000202`) — our 4-port report is
unvalidated against real hardware; front-panel mapping untested.

## Phase 14 — Networking (detailed)

**Goal**: minimum networking for games, only after core gameplay works.

**Status**: nvnet register/MMIO model DONE (`McpxNvNetDevice` at
`0xFEF00000`: idle TX/RX, link-up, instant MDIO/PHY, qemu-default MAC;
xemu `nvnet_regs.h`-faithful; §52) and validated live (FEF unmapped
collapse, init succeeds first try, live render loop; §53). gate67
proved init-only (§62: 44/350 per-reg, zero ring/KICK/poll) — the
packet engine stays scoped-not-built per the trace-driven rule.
Remaining (when a title drives it): descriptor/DMA packet engine
(`RingDesc` `{u32 buf; u16 len; u16 flags}`, rings `0x100`–`0x138` per
xemu `nvnet.c`), RX/TX interrupts, link-state/DHCP behavior, all behind
`INetworkBackend`. P0/P1 packet engine MERGED (§84: ring latches, IRQ4,
TX walker + loopback backend; guest init-only steady state preserved).
P2 RX + P3 backend MERGED (§88: RX filter + DMA-write + RX/RX_NOBUF
IRQs with pending-slot hold-and-retry, link toggle + 250 ms autoneg via
`Advance` with MIIEVENT, socket user-mode backend — ARP/ICMP/UDP relay —
behind `--net-backend`, loopback default; CLI `--net-inject`/
`--net-link` + rx/link counters; 30 new tests, full suite 986 green;
guest init-only behavior unchanged).

## Phase 15 — CPU JIT (detailed)

**Goal**: production CPU backend after the interpreter is stable and remains the reference.

**Tasks**: decoder → basic block → IR (plan 20.2) → optimizer → host emitter → code cache;
block keying and invalidation; evaluate managed options in plan 20.6 order; benchmark-driven.
Adopt proven JIT design concepts (trace cache keyed on guest VA, forward-only linking,
page-protection SMC, per-block invalidation.

## Phase 16–18 — Optimization (detailed)

- CPU (16): decode, memory, branches, flags, block cache, register allocation, indirect branches,
  MMIO checks, TLB, synchronization — in that order, benchmarked.
- Memory (17): direct RAM array, unsafe refs, spans, `MemoryMarshal`, fixed pages, TLB cache,
  read/write helpers, branch reduction, host virtual mapping where justified.
- GPU (18): measure command generation, submission, shader compilation, texture upload, render
  pass creation, synchronization, frame pacing; persistent resources, descriptor/pipeline/texture
  caches, command-buffer reuse, async upload.

## Phase 19 — Debugger (detailed)

CPU (registers/flags/IP/disassembly/step/step-over/continue/breakpoints/watchpoints), memory
(hex/search/read-write/mapped regions), GPU (NV2A registers/PGRAPH state/texture/framebuffer/
pushbuffer trace), storage (FATX/XDVDFS browsers), trace (CPU/MMIO/PCI/NV2A/kernel/filesystem).

## Phase 20 — Automated Testing & Fuzzing (detailed)

Unit tests per component; CPU instruction tests; memory alignment/paging/TLB/page-fault/MMIO/cache;
device tests; golden traces (plan 25.5); differential testing vs xemu where legal (plan 26); GPU
differential images/heatmaps (plan 27); compatibility matrix (plan 28); fuzz targets for x86
decoder, XBE, XDVDFS, FATX, ISO, NV2A push-buffer, MMIO decoder (plan 44).

## Phase 21 — CLI & Frontend (detailed)

`XGrid.Cli` commands (plan 35); Avalonia frontend (plan 36) communicating only through a
public API; performance overlay, logs, debugger, configuration.

## Phase 22 — Save States & Replay (detailed)

Deferred (plan 37/38). When implemented: serialize CPU/RAM/MMU/device/GPU/APU/timers/interrupts/
storage with mandatory versioning and checksums; snapshot/replay records initial state + inputs +
timing events.

## Phase 23 — Release (detailed)

Windows/Linux/macOS builds, packages, documentation, licensing review, firmware/user-data
documentation, performance documentation (plan 47 M16, section 48).

## Detailed repository tree (target)

```text
XGrid/
|-- XGrid.sln
|-- Directory.Build.props / Directory.Build.targets / Directory.Packages.props / global.json
|-- .editorconfig / .gitignore / README.md / LICENSE / CONTRIBUTING.md / SECURITY.md
|-- .github/workflows/ci.yml
|-- docs/            (architecture/, cpu.md, memory.md, nv2a.md, mcpx.md, storage.md, audio.md,
|                     input.md, boot.md, debugging.md, performance.md, compatibility.md,
|                     research-notes/)
|-- src/
|   |-- XGrid.Core/ XGrid.Cpu/ XGrid.Memory/ XGrid.Bus/ XGrid.Mcpx/ XGrid.Nv2a/ XGrid.Audio/
|   |-- XGrid.Storage/ XGrid.XboxFs/ XGrid.Xbe/ XGrid.Input/ XGrid.Graphics/ XGrid.Debug/
|   |-- XGrid.Host/ XGrid.Frontend.Avalonia/ XGrid.Cli/
|-- tests/
|   |-- XGrid.Core.Tests/ XGrid.Cpu.Tests/ XGrid.Memory.Tests/ XGrid.Bus.Tests/
|   |-- XGrid.Mcpx.Tests/ XGrid.Nv2a.Tests/ XGrid.Audio.Tests/ XGrid.Storage.Tests/
|   |-- XGrid.XboxFs.Tests/ XGrid.Xbe.Tests/ XGrid.Integration.Tests/
|   |-- XGrid.Compatibility.Tests/ XGrid.Performance.Tests/
|-- tools/           (XGrid.CpuTestRunner/ XGrid.XbeTool/ XGrid.MemoryTool/ XGrid.TraceTool/
|                     XGrid.Nv2aTool/)
|-- benchmarks/      (CpuBenchmarks/ MemoryBenchmarks/ XdvdfsBenchmarks/ Nv2aBenchmarks/
|                     EndToEndBenchmarks/)
|-- test-data/       (cpu/ xbe/ xdvdfs/ fatx/ nv2a/ audio/ integration/)
|-- scripts/         (build/test/benchmark/format .ps1 and .sh)
```

## Development velocity program (parallel track, unweighted)

Tooling that pays for itself. These are NOT numbered phases (the 0–23
weighting is untouched); each item ships when its acceptance test passes,
on the same wave machinery as device work. Build them in order — each
one multiplies every wave after it.

**V1 — Execution snapshots & restore (highest ROI).** Start any probe AT
a gate instead of replaying billions of steps to reach it. Acceptance:
save/restore CPU+RAM+device state at an arbitrary step; a restore
reproduces EIP/registers/flip counters exactly; a 007-gate probe runs in
seconds, not minutes. Note: phases 19/22 cover product save states; V1
is the internal forensic fast path — land V1 first, productize later.
Status DONE 2026-10-12 (waves snapshot-core + snapshot-devices,
§§99–100): `ISnapshotable` contract; CPU (all fields incl. FPU/SSE/
SYSENTER) + RAM + machine-scalar save/load; tagged-container
orchestration (`CPU`/`RAM`/`BUS`/`IDE`/`NV2A`/`ACI`/`APU`/`NET`/`USB0`/
`USB1`/`MACH`, order-independent, skip-unknown); per-device state on
all peripherals (NV2A+PRAMIN, IDE+overlay dirty sectors, USB+hubs+pad,
APU/ACI, PIT/PIC, SMBus/SMC/RTC/PCI, nvnet with socket-backend gate);
CLI `--save-state [--save-at]` / `--load-state`; 39 new tests.
**Acceptance met live (§101):** submenu save@1B → restore +50M ≡
uninterrupted 1.05B — EIP/regs/NV2A counters identical, frame SHA +
64 MB RAM SHA both match; probes now cost ~20 s instead of ~6 min.
Exclusions: diagnostic counters (core), unrestorable host handles
(re-attach by path), socket-backend snapshots gated to loopback.

**V2 — Minimal test-ROM suite + sample XBEs.** 10-second iterations per
device. §102's "overlay-XBE blocked" verdict assumed a stock retail
kernel — **superseded:** the operator's BIOS is a custom build that
permits unsigned code (the unsigned EvoX dashboard runs), so unsigned
XBEs go through the real kernel path (§104; `wave/xbe-bringup`).
Harness: `xgrid testrom --bin` flat-binary (no kernel/BIOS, `OUT 0xE9`
console, `EAX=0+HLT`=pass) + NASM ROMs. **Samples (local,
`data/test-samples/`, gitignored):** `triangle.xbe` (nxdk sample — first
GPU bring-up target; minimal XISO via `XISOSharp -y -m -c` + `boot
--dvd`), `kernel-test-suite-default.xbe` (kernel API coverage), nxdk
`hello`/`led`/`mesh`/`triangle` sources (see folder README). Acceptance:
ROM-A NV2A textured quad (DONE), ROM-B IDE READ DMA, ROM-C pad GetState
poll — each <100M steps with TESTROM PASS/FAIL + existing counters;
triangle renders through the kernel.
Status PARTIAL 2026-10-12 (wave/testrom-harness, §103): harness DONE +
ROM-A DONE — `xgrid testrom --bin` (no kernel/BIOS, `OUT 0xE9` console,
`EAX=0+HLT`=pass) + `TestRomRunner` + `tools/testroms/nv2a_quad.asm/.bin`
+ 4 tests (1096 green). ROM-A live: `TESTROM PASS nv2a_quad draws=1
textured=2080 frames=1 nonzero=1056` in 68,491 steps. ROM-B (IDE READ
DMA) and ROM-C (pad GetState poll) remain as specified §102 follow-ups.
Status UPDATE 2026-10-12 (wave/testrom-roms, §108): **ROM-B + ROM-C DONE,
V2 suite complete.** `tools/testroms/ide_dma.asm/.bin` — test-generated
deterministic 8 MB pattern HDD (not the dashboard qcow2), PCI BAR4
(register 0x20) + PRDT + READ DMA 0xC8 LBA 0x100 x8, BM-status poll,
guest pattern memcmp; live `TESTROM PASS ide-dma lba=0x100 sectors=8
mismatch=0` in 53,836 steps (`BMIBA=0xC000 status=0x04 starts=1`),
zero-image control fails data-mismatch. `tools/testroms/pad_poll.asm/.bin`
— OHCI HCCA + chained 5-ED hub-chain enumeration + periodic ED/TD XID EP
0x82, DoneHead/data-toggle poll across 1 ms frames; live `TESTROM PASS
pad-poll idle=0x0 pressed=0x2` in 2,949,419 steps, neutral-pad control
fails bad-report. +4 tests (1100 green). Remaining V2 item: triangle XBE
through the kernel (`wave/xbe-bringup`, §104).

**V3 — Differential harness vs xemu.** Turn search into bisection.
Acceptance: scripted early-boot state comparison at fixed steps (valid
only pre-animation-wait, where xemu is a live oracle) + a checked-in
frame-compare utility (RGB/alpha separation per `wave-visible/compare.py`).

**Operating rules (mirrored in AGENTS.md, binding on wave agents):**
scope every task to its acceptance test (subset beats general case);
partition waves into independent streams (one device/area per agent);
probe (read-only) vs build (worktree branch) agents; coordinator merges
sequentially (`--no-ff`) and renumbers research notes; never parallelize
a single root-cause hunt or a sequential gate chain; never fake a fix to
meet a wave deadline. Compute note: emulation speed is the binding
constraint — a faster machine / more cores beats more agents for
validation latency.







