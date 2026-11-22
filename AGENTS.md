# XGrid — Agent Notes

## Reference-first process (required)

Before implementing or debugging hardware behavior, **consult the reference projects first**
to confirm layout, IDs, registers, and semantics. **XGrid is GPL-2.0**: extract facts and
port code from GPL-compatible references where it speeds development, preserving every
original copyright/license notice. The binding rules are `docs/porting-policy.md`; the
provenance log is `THIRD_PARTY_NOTICES.md`; the **full-port ledger** is
`docs/xemu-port-manifest.md` and the open-source status is `docs/open-source.md`.
The project stays **full managed C#** - no QEMU/xemu native library, no C/C++ emulator
code in the build. **C# first:** P/Invoke is allowed only for host APIs or OS facilities
(OpenGL, XInput, executable memory for the raw JIT, host audio) and every edge is
inventoried in `docs/porting-policy.md` section 2. **The program is a full source port
of xemu 0.8.139** (XFP: `ImplementationPlan.md` E1), playable titles first.

- `References2/xemu-0.8.139` — LLE reference (MCPX, PCI, IDE, SMBus, NV2A, APU/DSP). **Primary
  source, adopted 2026-10-23** (0.8.137 and 0.8.136 stay vendored as prior snapshots). Key paths:
  `hw/xbox/nv2a/` (GPU pipeline/registers), `hw/xbox/mcpx/` (APU/ACI/nvnet),
  `hw/xbox/mcpx/apu/dsp/` (GP/EP DSP interpreter + optional JIT),
  `hw/xbox/smbus_xbox_smc.c` (SMC protocol), `hw/xbox/xbox_pci.c` (PCI topology/IDs).
  The QEMU 10.2.0 base is vendored inside the same tree; QEMU algorithms may be
  ported to C# but QEMU is never linked.
- `References2/xemu-0.8.137` / `xemu-0.8.136` — prior snapshots; use 0.8.139 unless a
  regression needs the older behavior.
- `References2/xemu-Playtest-v2` — **in-development xemu fork for Sega Chihiro (arcade)**
  on a QEMU 10.2.0 base. Its NV2A is the *tuned GPU* (`pgraph/cost.c`, `seed.c`,
  `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the un-tuned baseline at
  `hw/xbox/nv2a-stock/` — `diff -ru` isolates the whole perf delta (GPU-backend
  performance-track facts). Full `chihiro/` subsystem, `xid-light-gun.c`, new SMBus
  devices, 5 newer NV097 methods. Cross-check and port source; 0.8.139 stays the primary
  (see `docs/research-notes/xemu-playtest-v2.md`).
- `References2/xqemu-master` — older QEMU-based LLE reference (useful for simpler code paths);
  GPL-2.0, port source allowed with notices.
- `References2/Cxbx-Reloaded-master` — HLE, but its kernel headers are the NT-layout reference:
  `src/core/kernel/common/types.h` (KTHREAD/KWAIT_BLOCK/KAPC offsets, e.g. KTHREAD+0x2C = State).
  Cxbx is also the shader reference (`nv2a_psh.cpp` / `nv2a_vsh.cpp`) — `xemu` `glsl/psh.c` +
  `glsl/vsh-prog.c` are GPL-2+ and are the more complete oracle for the fragment/vertex stages.
  GPL-2.0; port source allowed with notices.
- ~~`References2/Xermu-main`~~ — **WITHDRAWN.** An earlier draft listed a standalone C#
  emulator that was never in the workspace. Operator confirmed 2026-10-13 the primary LLE
  reference is `References2/xemu-0.8.137`. `docs/research-notes/xermu.md` was deleted; historical
  citations to it (early plan text, `xgrid-architecture-decisions.md`) are unverified — do not
  use them as facts (see `sources.md` "Xermu - WITHDRAWN").
- `CSharp_XISOSharp` — MIT XDVDFS library: sibling repo (`C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp`,
  docs + source used for API/on-disk facts) + NuGet `XISOSharp` 1.4.2 referenced by `XGrid.XboxFs`
  (`XdvdfsFileSystem` adapter + `xgrid xdvdfs ls/cat`, §64). `CSharp_CHDSharp` sibling covers CHD later.
- `References2/XboxDevWiki` — XboxDevWiki vendored locally (full offline dump
  captured 2026-10-12: `html/<Title>.html` rendered pages with offline
  links/images, `pages/<Title>.wiki` raw wikitext, `images/`, `manifest.json`,
  `index.html`; identify pages by `<title>`). Key pages: Boot Process, BIOS,
  Kernel, MCPX, PCI, USB, NV2A, SMC, MCPX ROM, Hidden Boot Code, Memory.
  Old numeric citations (`51` Boot Process, `53` FATX, ...) translate per
  `References2/XboxDevWiki/README.md`.
- `References2/envytools-master` — **MIT** NVIDIA register/method database + GPU docs
  (notes: `docs/research-notes/envytools.md`). First cross-check for NV2A method
  offsets/enums: `rnndb/graph/nv20_3d.xml` (+ `nv10_3d.xml`) = Xbox class `0x0097`
  (`0x17FC` begin/end, `0x1800/0x1810/0x1818` elements/arrays/inline, `0x0B00/0x0B80`
  VP upload); pusher/puller semantics in `docs/hw/fifo/dma-pusher.rst` + `puller.rst`;
  vertex-program/XF semantics in `docs/hw/graph/xf/` (`isa.rst`, `mode.rst`). Caveat:
  envytools' own "NV2A" is the nForce IGP, not the Xbox GPU.
- `References2/nv2a-trace-master` — **MIT** apitrace-for-real-hardware tracer
  (notes: `docs/research-notes/nv2a-trace.md`). Hardware ground truth for PGRAPH if a box
  + `xboxpy` are available (per-method pre/post state, surface/texture PNGs, `debug.html`);
  without hardware its `XboxHelper.parse_command` pushbuffer decoder, NV2A/CACHE1 register
  constants and `Texture.py` format table are usable facts.
- `References2/CopettiDocumentation` — CC BY 4.0 (attribution required if reused) high-level
  console architecture essay (notes: `docs/research-notes/copetti.md`). Orientation /
  onboarding / design cross-check only — never a register-level oracle.

**Porting policy (2026-10-23).** XGrid is GPL-2.0 and may port code from xemu,
QEMU, Cxbx, xqemu, envytools and nv2a-trace. The project stays **full managed
C#**: port algorithms, do not embed QEMU/xemu or add C/C++ emulator code. A
ported file keeps its original copyright/license notice, gets an SPDX line and a
`Ported from <project> <version> <path>` line, and is recorded in
`THIRD_PARTY_NOTICES.md` + `docs/research-notes/sources.md`. The full workflow
is `docs/porting-policy.md`. **There is no license wall**: xemu and QEMU are
GPL-2.0, the same license family as XGrid, so any bounded module may be ported
as full managed C# with the provenance rules. The DSP port is the model; a CPU
TCG port is *allowed* but is a sized option (D8), not a default - the raw emitter
is already bit-exact and faster than xemu on the CPU-bound ktest.

**Never delete, move, or rewrite anything inside `References2/` — no file, no subfolder,
ever.** It is the vendored oracle set every wave depends on. It has been emptied twice:
2026-10-12, and 2026-10-14 18:05–18:07 (a Dropbox **remote-origin** deletion from another
device/session applied locally — the repo sits inside the Dropbox sync root, so anything
deleted "in the cloud" lands here as local file loss; no local script ran). Do not run
`Remove-Item`, `rm -rf`, `git clean`, `robocopy /MIR`, `dotnet clean`, or any
cleanup/grooming script against `References2/` or a path that resolves to it; wave
worktrees junction `References2/` read-only and worktree teardown must never descend
through that junction. **The canonical and only store is this folder**
(`C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator\References2`). Do not create,
promote or rely on an out-of-tree reference backup (`D:\xgrid-refs-restore\` is retired
as of 2026-10-20 by operator decision — do not reintroduce it). Integrity is checked,
not assumed: `pwsh -File scripts/refs-manifest.ps1 -Verify` compares every set against
`docs/refs-manifest.json` (regenerate with `-Update` after adding or updating a set; the
manifest is a checksum list (one entry per reference set), tracked by git, **not** a
copy of any reference).
If a reference is missing, recover from the other development machine's `References2/`
(the Dropbox account is the only cross-machine carrier) or, for sets that have one, the
upstream URL in `docs/research-notes/sources.md` — `xemu-Playtest-v2` has no upstream
URL and exists only here, so it is the one set whose loss is unrecoverable. Two
development machines share this tree (home machine = `H1` in `docs/hosts.md`); always
run `scripts/host-doctor.ps1` at session start rather than assuming the previous
session's environment.
Renamed `References/` → `References2/` 2026-10-16 by the operator (repeated
remote-origin deletions of `References/`); both names stay git-ignored.

Record extracted facts in `docs/research-notes/xgrid-architecture-decisions.md` (with source
file references) and update `ImplementationPlan.md` status when a device or phase changes.

## How to work (required; 2026-10-22 routine)

The full routine is `ImplementationPlan.md` -> "Execution plan (current)".
`docs/status.json` is the single source of truth; `scripts/status.ps1` validates
it and regenerates the status blocks and the completion graph.

1. **One active task (WIP=1).** `pwsh -File scripts/status.ps1 -Check -Show`;
   work only on the active task. No parallel agents on the same area.
2. **Price before build.** A measured ceiling first; <10% wall or <20% engine
   -> record a negative and stop.
3. **Acceptance test first.** Write the differential/golden before the code.
4. **Definition of done.** The per-type checklist in `ImplementationPlan.md`
   E8; "landed" without it is not done.
5. **No redo without a new measurement.** `docs/status.json` negatives are
   binding.
6. **One status file.** Edit `docs/status.json`; run
   `pwsh -File scripts/status.ps1 -Check -Sync`; never write a second status
   narrative.
7. **One task per commit.** Code + tests + status + graph together; no generic
   messages.
8. **Row-scoped corpus.** Full sweep only at milestone close.
9. **Stop after two failures.** Reassess; do not grind a third variation.
10. **History is append-only.** Mark old status blocks superseded; do not
    rewrite them.

**RepoFleet (remote):** the task command is static:
`pwsh -File scripts/status.ps1 -Next` prints the active task and the exact
close commands; `-Complete`/`-Negative` advance the queue. A parked conditional
task is closed with `-Negative -Task <id> -Evidence ... -Reopen ...`, and
`-Next` refuses a dirty tree. Queue the same command once per task; RepoFleet
serializes one active task per repository. The full contract is
`docs/repofleet-worker.md`.

## Development direction (required; course-corrected 2026-11-22 - full xemu port, playable first)

<!-- STATUS:BEGIN (generated from docs/status.json by scripts/status.ps1; do not edit) -->
**Current status (2026-11-22):** P1 / **XFP3 - PGRAPH shader pipeline (VSH, PSH, fixed-function, GLSL generators)** (active, attempt 0).
**Gameplay (P1):** H:\Tests\XBOXReady (7 optimized ISOs; 007 1.60 GB, Halo 3.38 GB, 50 Cent, Bad Boys II, Destroy All Humans!, Tak, Ninja Gaiden Black) - P1 sprint (2026-11-22): the objective is at least one game playable; no title is playable yet. P1S0 measured 007 at 0.100x (30B gameplay 413.254 s; runner 53.5%, non-JIT 20.1%, b4 35.7%, GL 0.42%; N15: shape fixes top out at ~0.20x). P1S1 is done: the live WASAPI sink plays the EP FIFO PCM (state=Playing; WAV SHA 813CB8DE... = TM8). The operator chose the full xemu/QEMU CPU engine port; P1S2 is done (QEMU CPUTLBEntry as QemuTlb, 9 exact differential tests; probe 0.84 vs 0.64 ns, so the TLB is not the lever - N16). P1S3 (TCG IR + i386 translator) is done: slices 1-5 landed (managed TCG IR + IR interpreter + 77 exact translator differentials: MOV/ALU/INC/DEC/NOP/JMP/Jcc, the reference memory shape Lea/Load/Store with 1/2/4-byte accesses and segment bases, 8/16-bit and partial registers, shifts/rotates, SHLD/SHRD, NEG/NOT, MOVZX/MOVSX, LEA, XCHG, SAHF/LAHF, MUL/IMUL/DIV/IDIV, PUSH/POP, BT/BTS/BTR/BTC, CMPXCHG/CMPXCHG8B, XADD, 16-bit addressing, REP string ops (MOVS/CMPS/STOS/LODS/SCAS), the MMX register file and the packed integer family (padd/psub saturation, pmul*, pcmp/pack/unpck, shifts, moves, maskmovq, pinsrw/pextrw/pmovmskb/pshufw, EMMS); 828 CPU tests green; the MMX file is the real FpuState and the 641-case cpu-oracle corpus runs through the IR engine with counted fallback - exact against the independent oracle, translated=2501/fallback=2344 after the integer batch: ADC/SBB, TEST, SETcc, CMOVcc, BSWAP, BSF/BSR, ARPL, sign-extends, XLAT, flag ops, PUSHF/POPF, PUSHA/POPA, LEAVE, LOOP/JECXZ), x87 (TcgFpu: full arithmetic/compare/unary/BCD/stack set over the shared FpuState/Fpu80) and SSE (TcgSse: moves, packed/scalar arithmetic, logical, compares, shuffles, conversions, MXCSR) - corpus 4202 translated / 643 fallback (641 harness FXSAVE + 2 ENTER), 892 CPU tests); P1S3 (TCG IR + i386 translator) is done; the IR interpreter measures 14.80 ns/member vs the raw runner 6.30 (2.3x) on a hot loop, and at real scale it pays ~132 ns/instruction, so the engine is integrated first (block runner, ITcgMemory over the CPU MMU, interrupt poll, fallback); P1S4 slice 1 lands the engine seam and TB cache: XboxCpu implements ITcgMemory over its own MMU/MMIO path, RunTcg polls interrupts and TSC per instruction with the exact interpreter as fallback, and TcgBlockCache caches one translated instruction per EIP (15-byte head check, clears on MMU flush and decoded-code stores) - exact on the triangle leg's first 10M steps (RAM 97CA74AA, CPU FA4E4131) at 5.24 vs 11.70 Msteps/s (1.06 without cache); slice 2 adds TcgExecutionState (no per-instruction allocations) and skips Fetch on TB hits -> still exact at 7.59 Msteps/s (2.7x the uncached interpreter, 0.65x the default cached one); the corrected real-scale cost is ~132 ns/instruction, so replacing the non-JIT share saves ~10% wall, and the switch gate now needs multi-member TBs with a per-member poll contract plus the codegen; slice 3 adds the per-member poll contract (TcgOpKind.Poll advances TSC and checks the PIC between members; a pending interrupt publishes the live state, delivers with the next member EIP and abandons the block) and the member limit that makes the step budget exact - multi-member TBs (up to 16) are now exact at 100k/1M/10M and at both 1B accurate anchors (triangle E58AE5D7/C43D5B49, menu 5C0CF968/E2B4D327) at 15.51 msps (10M) / 11.06 (triangle 1B) / 10.34 (menu 1B); the engine is not switched because it is not yet faster than the fast raw engine, 895 CPU tests). P1S4 is done and P1S5 (parity and switchover) is done: the parity bundle is exact with the ported engine (four accurate 1B goldens incl. both frames, ktest log CA5D3867 byte-identical, ktest 100M workload, cpu-oracle), the product is NOT switched because the engine is 5-7x slower, and the speed ceiling is recorded - the product is at 0.100x (13.7 ns/step), 0.5x needs ~2.7 ns/step, and the ported engine reaches 66-90 ns/step, so the phase stops with 0% of the required speedup from this port. The 0.5x P1 gate still needs xemu-class ~1.2 ns/step. XFP2 is active: the attribute-decode differential pins all six array formats (UB_D3D/UB_OGL/S1/F/S32K/CMP), the (0,0,0,1) defaults, count 0/2 and stride 0/advance through the real fetch path (10 differentials, 490 Nv2a tests); the cull/degenerate/clip-window rejection predicates are pinned too (8 cull tests), and primitive assembly is pinned (DRAW_ARRAYS packed start/count, range coalescing vs separation, one draw command per BEGIN/END; 496 Nv2a tests), triangle 1B anchor unchanged; the recorded title rows reproduce - 007 134,004 draws/1,005,989,482 px/6,478 frames and NGB 4,251/187,315,817/238,137 with EIP 0x8001B047, and the fast engine's one-frame sensitivity to the host flag set is documented. XFP3 is active with its baseline measured: NGB's XGrid frame has 13,266 colours vs xemu's 1,356 at matching means (within 11/255), the method stream alternates surface formats 0x08080228/0x08080128, and the next step is a matched-moment pair plus the surface-format/dither decode. P1S6 is closed negative: 7 of the nine parts pass (own render, live input, save/load, xemu frame policy, live audio, product defaults, gates) and the speed floor fails (0.100x, and 10 minutes derives from it), so the repository stays private; reopen with an engine at <=2.7 ns/step or an operator re-scope. The full-port program continues at XFP0 (done) and XFP1: the NV097 method surface is now machine-verified (215 defines / 201 rows; generated 1031 handled + 280 declined offsets) and an opt-in unknown-method counter measures 361 unknown writes in 18.6M methods on the triangle 1B leg with identical anchors; the reference-surface expansion is exact (1042 handled + 280 declined offsets; case4 strides fixed), and the residual 226 writes are seven offsets outside the reference NV097 surface (0x17C4, 0x1D84, 0x1710, 0x380, 0x16BC, 0x1D80, 0x9F8) that xemu also ignores - valid-surface coverage gap zero; the pgraph state-type switch has its first section (PgraphTextureState owns the four texture slots and bump state; behavior and snapshot order unchanged, triangle F9945E16/8836C7EB and menu 5C0CF968/E2B4D327 exact, 480 Nv2a tests); PgraphState now also owns the transform-program rows, transform-constant contexts and the PshCombiner (same anchors/tests); the six surface/clip scalars now also live on the state (ref properties, zero call-site churn; same anchors/tests), so the state type is the storage and no --pgraph-port toggle is needed - the differential is the gate; the seven-title method differential and XFP2 are next. The reference map is docs/research-notes/cpu-engine-port.md. The repository stays private until P1 (docs/open-source.md). Oracle: data/xemu/xemu.exe (xemu 0.8.139), same optimized ISO + firmware/EEPROM; scripts/xemu-oracle.ps1 selects the disc with -config_path and captures a DPI-correct client area; a capture is a window screenshot, so the policy is mean/colour/coverage, never pixel-exact.
**Perf reference (M-D, deferred; H1, 1B, fast):** triangle gl+replace 6.40 s / sw 11.10 s; menu gl+replace 17.63 s / sw 27.40 s; gap ~4.9x / ~11.7x.
**Phase score:** 79.65% (93.99 of 118). **Next:** XFP4 (PGRAPH surface, texture and cache semantics), XFP5 (Ported GL renderer (pgraph/gl) behind IPgraphBackend), XFP6 (AC'97 codec and APU front end (full)), XFP7 (IDE/ATAPI and block-layer port).
**Do not retry without a new measurement:** N1 C4 host-pointer translate probe, N2 Read-only-ROM decode caching alone, N3 Full trace/superblock formation, N4 Own the encoder (Assembler rewrite), N5 Remove the raw emitter's per-member EIP/TSC/served, N6 VshProgram.Execute allocations / scratch reuse. See docs/status.json for all 23.
<!-- STATUS:END -->

**Operator decision 2026-11-22 - full xemu port, playable first.**

1. **The program is a P1 sprint first, then a full source port of xemu 0.8.139
   to managed C#.** The sprint uses the existing engine and NuGet host packages
   to reach one playable game; the port follows for breadth. It is not native
   embedding: no QEMU/xemu library and no C/C++ emulator code in the build
   (`docs/porting-policy.md`). The module ledger is
   `docs/xemu-port-manifest.md`; the task plan is `ImplementationPlan.md` E1
   (P1S0-P1S3, XFP0-XFP14).
2. **The objective is at least one game playable (P1), and no title is playable
   yet.** M1 (007's own render/input milestone) is met, but 007 runs at 0.11x
   guest time (12.85 s per 1B steps) and its audio is an offline WAV. P1 is the
   first title that meets all nine conditions in E1 section 1 (own render, live
   input, >= 0.5x guest time, 10 minutes, save/load, xemu frame policy, **live
   host audio**, product defaults, gates exact); P2/P3 are breadth after P1, P4
   is seven of seven (XFP10), P5 is the ledger complete (XFP14). The repository
   stays **private until at least one game is playable**.
3. **The critical path is the P1 sprint** (measure 007, live NuGet audio, speed
   to >= 0.5x, the nine-part gate). The full-port order below starts **after
   P1**: NV2A PGRAPH + GL renderer (XFP1-XFP5), AC'97/APU (XFP6), IDE/block
   (XFP7), SMBus/SMC/nvnet (XFP8), USB/OHCI/XID (XFP9), machine/chipset
   (XFP10), CPU/memory (XFP11), host backends (XFP12), snapshots (XFP13), parity
   close (XFP14). A full-port module is promoted into the sprint only when the
   P1S0 measurement proves it is the fastest fix.
4. **The CPU is in scope but sequenced last,** gated on "not slower" than the
   current bit-exact raw engine on the 1B/ktest workloads. A slower port keeps
   the raw engine and records the exception.
5. **Media.** The acceptance set is `H:\Tests\XBOXReady` (seven optimized ISOs,
   xemu-oracle-grade); `E:\Microsoft Xbox` (945 retail CHDs, 1.44 TB) is the
   broad compatibility library for title triage and the CHD path, never an
   oracle row or a gate; `H:\Tests\XBOXTest` (37 Redump ISOs) is XGrid-only
   compatibility media; `data/test-samples/` stays gate currency.

The live queue is `docs/status.json`; the task detail is
`ImplementationPlan.md` E1. The ledger is `docs/xemu-port-manifest.md`; the
open-source/publication status is `docs/open-source.md`.

Canonical order: `docs/development-direction.md`; port ledger:
`docs/xemu-port-manifest.md`; open-source status: `docs/open-source.md`;
fast-mode charter: `docs/research-notes/jit-track-charter.md`; corpus plan:
`docs/research-notes/compatibility-corpus.md`; sample matrix:
`docs/compatibility.md`. **The long-run objective is a playable emulator
(xemu-class performance later), reached through the full port.** Hard rules:

1. **Playable titles lead.** Every task must advance a ledger row and, where
   applicable, a title row (own render, gameplay, 10 minutes, audible).
   Performance is reported, never claimed, until P3.
2. **Port the reference, don't reinvent it.** Translate xemu 0.8.139 state,
   dispatch and behavior; a fresh design needs a recorded reason.
3. **Differential first, old path as fallback.** No "ported" without a test that
   fails before the port; keep the old implementation until the differential is
   exact; one engine per run.
4. **Provenance is part of the code.** SPDX + `Ported from` + notices + sources
   in the same commit; `scripts/port-manifest.ps1 -Check` (XFP0) and
   `scripts/package-verify.ps1` enforce it.
5. **The gates are unchanged.** Four accurate 1B goldens + ktest log
   byte-identical (or a measured re-baseline), `build`/`test`/`format` clean,
   package gate on release changes. Retail rows are acceptance, never gates.
6. **One status file.** `docs/status.json` is the only current status;
   `scripts/status.ps1 -Check -Sync` regenerates the blocks. History is
   append-only.
7. **Direct development** - wave runner retired, no worktrees/detached servers.

**Position (2026-10-22, 1B fast mode, H1) - history, not the current target:**
triangle `gl+replace` 5.72-6.15 s / sw 10.21 s; menu `gl+replace` 15.6-17.5 s /
sw 27.01 s; gap ~4.9x / ~11.7x. ktest 2B is already ahead of xemu (9-10 s vs
15 s). Performance is reported in the matrix context, never claimed, until the
playability gate is met.

## Build / test / format

- `dotnet restore XGrid.sln` (after project-graph changes)
- `dotnet build XGrid.sln -c Release --nologo --no-restore`
- `dotnet test XGrid.sln -c Release --nologo --no-build`
- `dotnet format XGrid.sln --no-restore` then `--verify-no-changes`
- **GitHub Actions is DISABLED** (2026-10-22; private repository on a GitHub Free
  account): every workflow in `.github/workflows/` is `workflow_dispatch`-only, so
  nothing runs on push, PR or tag. Nothing was deleted — re-enabling is one
  commented-in trigger block per workflow. Run the gates above locally; the full
  runbook (InspectCode, package audit, coverage floors, releases) is `docs/ci.md`.

Warnings are errors (`TreatWarningsAsErrors`); nullable enabled; implicit usings disabled
(add explicit `using` directives). Analyzer rules: CA1822 requires `Name`-style properties to
touch instance state (use a backing field), CA1720 forbids names like `Pointer`.

## Velocity — how we go fast (required reading for wave agents)

1. **Scope to the acceptance test.** Never build the general case when the
   next gate needs a subset (GPU backend for the menu's method stream, not
   full NV2A; idle-skip before full JIT). Every task states its acceptance
   test up front; an agent that cannot meet it commits diagnosis, never a
   fake fix.
2. **Snapshots first for title work.** Probing a gate past ~1B steps must
   start AT the gate (save/restore CPU+RAM+device state), not replay
   billions of steps per probe. Until snapshot infra (plan V1) exists,
   reuse `--ramdump` + break-world steps and record exact step counts.
3. **Differential-first diagnosis.** When behavior diverges: (a) two-boot
   diff (neutral vs perturbed, RGB and alpha separated); (b) compare
   against xemu only where it is a valid oracle (it deadlocks at the
   animation wait — useless past that point); (c) per-draw/per-register
   instrumentation with counters before theorizing.
4. **Waves: partition, don't parallelize one brain.** One device/area per
   agent; read-only probe agents vs worktree build agents on `wave/*`
   branches; coordinator merges sequentially (`--no-ff`) and renumbers
   research notes. Never parallelize a single root-cause hunt or a
   sequential gate chain — that multiplies cost without cutting latency.
5. **Iteration budget.** 1B steps ≈ 70–90 s wall clock at ~14M insn/s.
   Batch runs, keep them deterministic (fixed scripts/steps) so results
   compare across runs, and prefer the cheapest run that reaches the
   gate. More cores → more parallel validation runs; emulation speed is
   the binding constraint, not agent count.
6. **Wave-agent liveness:** BOSS polls every running task ~10 min; stuck/
   failed tasks are restarted immediately (or closed if needed) — see
   Wave operations — BOSS protocol below.

## Wave operations — BOSS protocol (2026-10-14)

The coordinator acts as BOSS for every wave (proven in waves 7–8: a frozen
session relaunched per this rule closed two gates and turned scaffolding
into the CMC fix; four agents relaunched after a host power loss all went
green with zero work lost).

1. **Fan-out (max 8 simultaneous tasks).** One device/area per task; never
   parallelize a single root-cause hunt or a sequential gate chain. Each
   task is handled by one agent, which may spawn sub-agents for
   parallelizable sub-work. One todo per task; build tasks get a `wave/*`
   branch + worktree (with `References` junction + private media copy),
   probe tasks stay read-only. Every task prompt states its acceptance
   test and starting state up front.
2. **Poll every ~10 min** while a wave is out: check each running task's
   state (progress output, tool activity, elapsed wall clock vs iteration
   budget). Classify: on-track / stuck (frozen session, no progress, same
   failure repeating) / failed / done.
3. **Stuck/failed → restart; close if needed.** Relaunch IMMEDIATELY with
   an inventory of the task's partial state (untracked scripts,
   uncommitted diffs — keep-vs-revert in the relaunch prompt), never cold.
   Close (cancel) a task only if it is blocked on a dependency, its
   hypothesis was invalidated, or it was superseded — and record the
   diagnosis in the notes, never a fake fix.
4. **Aggregate when all tasks finish.** Merge sequentially (`--no-ff`),
   renumber research notes (§-union on conflict), run the full suite +
   format, update `AGENTS.md` (current state + next session) and
   `ImplementationPlan.md`, commit + push, report. Closed-task diagnoses
   go into the notes as known-no-bug / deferred items for the next wave.
5. **Poll discipline (2026-10-14, from wave-9 T5).** No task may span work
   without a checkpoint return inside ~10-min windows: scope long probes
   as checkpointed legs (save snapshot + return evidence per leg) so BOSS
   can poll, restart, or close them. A task with no checkpoint return past
   every review point is stuck — close it, record the diagnosis.

**Automation — `tools/wave` (2026-10-14).** The loop above is now also a
program (Node 24 + opencode v2 client; no tmux/WSL): a pull-scheduled worker pool
keeps N slots busy — an idle slot takes the next ready task *the moment* one
settles (deps satisfied, `serial` tasks run alone), so fast tasks do not
wait for slow ones. Each task gets a worktree + `wave/*` branch; the worker
contract is `wt-data/wave/<id>.result.json` (status/branch/headCommit/
tests/notes), verified against real git state; budget watchdog + retries
continue in-session from a continuation inventory. The aggregator merges in
topo order (`--no-ff`), optionally spawns a resolver session for conflicts,
runs `finalGate`, and writes `runs/<wave>/report.md`. Usage:
`npm --prefix tools/wave run wave -- run <plan.json> [--resume]`; `wave dashboard <plan.json>`
serves a live task/log view at 127.0.0.1:4181;
see `tools/wave/README.md` and `tools/wave/examples/wave.plan.json`
(end-to-end smoke-tested: worktree → real session → verified commit → clean).

## Current state

> Historical (2026-10-09 onwards). The current state is the generated block at
> the top of this file and the XFP program in `ImplementationPlan.md` E1; the
> bullets below record how the machine reached the dashboard and the first
> playable title.

- Real MCPX ROM -> 1BL -> 2BL -> kernel executes (LLE). Kernel cold-boot init,
  SMC handshake, PCI setup, RTC, NV2A driver all complete; timer IRQ0 + PIT flow.
- **Gate 1 DONE (2026-10-09): the kernel loads the dashboard off the HDD.**
  Guest sequence matches an xemu `-trace` oracle through HDD SECURITY UNLOCK,
  the full ATAPI DVD probe (TUR/SENSE/MODE SENSE/READ CAPACITY with identical
  sense bytes), then 27 READ DMA (`0xC8`) commands (~828 KB). RAM holds the
  real EvoX XBE (`XBEH` at phys `0xBF000`, loader `CMP [ESI],"XBEH"` at
  `0x2EE98`, `EvoX` cert string). See research-notes §31.
- IDE engine (all xemu-faithful, tested): 8/16/32-bit data port, dual-drive
  taskfile (HDD master + ATAPI DVD slave with `0x14/0xEB` signature), ATAPI
  DRQ/reason-bit/error protocol, REQUEST SENSE, synchronous BM-DMA (BAR4
  window via `OnBarWritten`, PRDT walk through `IGuestMemory` wired to RAM,
  ATA + ATAPI DMA, IRQ ack). CLI prints `IDE cmds`, `ATAPI pkts`, `IDE BMIBA`.
  DVD media serving (§65): `IDvdImage`/`IsoDvdImage` (2048 B) + `boot --dvd`;
  TUR/capacity/READ(10/12) with latched sense; gate72 served the kernel's
  real sector probe (PVD + XDVDFS-magic LBAs) with no auth traffic.
  Compressed CHDs (§66): `ChdDvdImage` (CHDSharp 1.4.3, magic dispatch
  in `OpenDvdImage`); 007 USA boots its XBE off CHD (SHA-identical
  `default.xbe`) and parks in a timing wait (gate74: 47% udiv, frozen
  DVD tail, 62→83 slow flips).
- Host-side dashboard tooling (parallel track, merged): `XGrid.XboxFs`
  FATX reader + `XGrid.Xbe` parser + `xgrid fatx ls/cat` / `xgrid xbe info` /
  `xgrid sector` (raw LBA dump); identified the boot dashboard as softmod
  `xboxdash.xbe` (770048 B, `EvoX`, entry `0x12A17`) in partition at `0x465400`.
- **Avalonia desktop frontend (2026-10-20, `ea8b38f`):** `XGrid.Frontend.Avalonia`
  is a runnable cross-platform dark-theme GUI (Windows/Linux/macOS) over the public
  `XGrid.Host` API: Home with live frames, Library (ISO/CHD/XBE scan), Settings
  (firmware/storage/RAM/renderer/JIT/NV2A fidelity/network/library/accent),
  Activity log, and About. In-process emulation on a background thread; frames via
  `Nv2aMmio.TryCopyDisplayFrame` into a `WriteableBitmap`. Verified live on the EvoX
  dashboard plus pause/resume/power cycles and settings persistence. Design note:
  `docs/research-notes/avalonia-frontend.md`; run
  `dotnet run --project src/XGrid.Frontend.Avalonia`.
  **Renderer switch (2026-10-22, §221):** Settings → Renderer offers the portable
  software rasterizer (default) or the host-GPU OpenGL backend (headless WGL,
  replacement mode + per-flip readback, created/disposed on the emulation thread;
  unavailable paths log a reason and stay on software). Measured on gate media at
  1B through the real session: 34.8 s software vs 24.7 s OpenGL (-29%) with the
  same picture.
- NV2A at `0xFD000000`: PFIFO pusher + PGRAPH software rasterizer (M4–M8
  done: inline draws, vertex-program interpreter, textures, zeta/blend
  fragment pipeline, flip present path). `XGrid.Graphics/DisplayFrame`
  exports the presented frame as BMP (`xgrid boot --frame out.bmp`); CLI
  also prints draw/blend/depth/flip counters and saves `--fb`/`--pgraph`/
  `--ramdump`. Dashboard renders 2670 draws / 10.1M px / 33 flips at 1.7B
  steps (EIP `0x14C72`); 3.2B steps reach file-save code (EIP `0x67BFD`).
- **Animation wait passed (2026-10-10, research-notes §32):** the `0x800426D8`
  park was the boot animation waiting on the NV2A notifies page. Implemented
  `NV097_BACK_END_WRITE_SEMAPHORE_RELEASE` (`0x1D70`) with the semaphore DMA
  context resolved as a RAMHT handle (xemu's raw-PRAMIN-offset handling is a
  TODO bug and deadlocks there — XGrid is now ahead of xemu on this path) and
  `NV097_NO_OPERATION` (`0x100`) PGRAPH notifications; `UpdateIrq` now matches
  xemu's `pending && enabled` assert. The loader runs the full animation and
  reaches the video-mode-set path.
- **AV-device gates passed (2026-10-10, research-notes §33):** the video-device
  open needed two MCPX devices — **ACI (AC'97) at `0xFEC00000`** (`McpxAciDevice`,
  xemu `ac97.c`-faithful; the kernel polls CAS bit 0 with a 1000×
  `StallExecution` timeout) and **APU at `0xFE800000`** (`XGrid.Audio/
  McpxApuDevice`; voice-off completion = `SE2FE_IDLE_VOICE` FE message, which
  must be delivered on a VP frame boundary via `Advance`, not synchronously, or
  the stop method deadlocks on its own `[dev+0x1C4]` operation counter). Both
  PCI IRQs wired (APU=PIC IRQ5, ACI=IRQ6 via the LPC INT routing nibbles). The
  boot now runs the animation + video mode set.
- Interrupts: PIT/PIC/PIT-EOI fine, NV2A IRQ path live (PMC/PGRAPH/PCRTC
  pending → PIC IRQ3; the kernel toggles `PMC_INTR_EN_0` around its DPC). IDE
  IRQ14 works; APU IRQ5 now handled by the kernel (ISR acks ISTS).
- **FCOMP bug fixed → animation runs + XBE executes (2026-10-10, §34):** the
  §33 NULL-surface crash was a **CPU bug**, not an NV2A gap. `fcomp mem`
  (`D8`/`DC` forms) was decoded as `(ST0, memory)` but the executor compared
  against `Operand1` (=ST0), so `fcomp` always compared ST0 with itself
  ("equal"), the intro-animation tick loop (`0x800579B8` → `0x80057949` →
  `0x800571AD`) never ran, and the AV teardown released the never-created D3D
  surface. Fix: compare against `Operand2` when present. Now the animation
  ticks (32 scene updates, 137.4M→488.9M), the teardown no longer faults, and
  `xgrid boot --steps 700000000` completes **all 700M steps with no fault**,
  executing the dashboard XBE image (loaded phys `0x5A000`, base VA `0x10000`,
  entry VA `0x22A17`).
- **XBE launch gate PASSED (2026-10-10, §34):** the XBE header entry field
  (VA `base+0x128` = `0x10128`) is an **absolute VA** (`0x12A17`), XOR-encrypted
  in the file; the kernel decrypts it in place (`xor [0x10128],0xA8FC57AB` at
  `0x8002EFE6`) and calls it directly (`call [0x10128]` at `0x800158DA`). The
  dashboard entry executes at phys `0x5CA17`, resolves its kernel-thunk imports
  (`[0x12C4B]=0x8001EEAC`), and runs its init: `0x5C7F0` → `0x5C390` → the
  bit-stream decompressor at `0x5C4A0` (RVA `0x24A0`; 16 KB ring buffer
  `[ctx+0x34]` masked `0x3FFF`, bit reader). At 700M the CPU is inside that
  decompressor (`srcIndex=0x19174`, state=12).
- **USB/OHCI gate passed (2026-10-10, §36):** `OhciUsbController`
  (`src/XGrid.Bus/Usb/`, xemu `hcd-ohci.c`-faithful: 256-byte region, 4 root
  ports, HcRevision `0x10`, HcRhDescriptorA `0x204`, port/hub status semantics,
  soft reset on HCR, `MIE && status&enable` IRQ gating, 1 ms frames via
  `Advance` → SOF + HCCA writeback) is mapped at USB0 `0xFED00000` / USB1
  `0xFED08000` (PCI 0:2.0/0:3.0, IRQ1/IRQ9 via `introute`). The
  `FED=375651421` unmapped poll is gone; `xgrid boot --steps 1600000000`
  completes with no fault at EIP `0x22DE2` (phys `0x10FDE2` — a **newly loaded
   dashboard module**, real code calling module functions). Only tiny unmapped
   buckets remain (`045=4 065=8 FEF=3970`, the nvnet stub — FEF eliminated
   by the nvnet device in §53).
- **Next gate — NV2A M5 (programmable vertex pipeline).** A 1.7B-step run with
  `--pgraph` shows the dashboard in its **rendering phase** (EIP `0x14C72`,
  186844 PGRAPH methods: `ARRAY_ELEMENT16 0x1800`×58577,
  `SET_TRANSFORM_CONSTANT_LOAD 0x1EA4`×8301 + transform constants `0xB80`+,
  `SET_BEGIN_END 0x17FC`×5340 draws, `INLINE_ARRAY 0x1818`×396, `DRAW_ARRAYS
  0x1810`×32, surface/clear/flip). **The dashboard uses NV2A vertex programs**
  (`SET_TRANSFORM_PROGRAM 0xB00`.. — D3D fixed-function is driver-generated
  programs), so M5 needs a **transform-program interpreter** (xemu
  `vsh-prog.c`), not just fixed-function. Foundation done (§37):
  transform-program/constant storage with PROG_LD_PTR/CONST_LD_PTR advance
  semantics, inline element/array semantics (`0x1800`/`0x1808`/`0x1810`/
  `0x1818`),   `SET_BEGIN_END` buffer resets, vertex array format/offset regs; CLI prints
  `programLoad`/`constLoad`/`inlineElements`. **Interpreter done (§38):**
  `VshProgram` decodes/executes NV2A vertex programs (MAC/ILU opcodes,
  R/V/C input muxes with shared V/const fields, swizzles/negates, write masks,
  paired MAC+ILU, R12=oPos alias, A0-relative constants, FINAL; screen-space
  oPos). **Attribute fetch + element draw done (§39):** vertex arrays decoded
  per format (UB_D3D BGRA/UB_OGL/S1/F/S32K/CMP, count/stride), elements
  (`0x1800`/`0x1808`) → `VshProgram` per vertex → screen-space `oPos` + `oD0`
   color → shared barycentric `FillTriangle`. **Ran on the real
   dashboard:** `xgrid boot --steps 1700000000` reports `draws executed=2670
   skipped=0 pixels=10137600` with no fault (§42 later showed all pixels come
   from the 33 legacy inline draws; program draws are degenerate at this
   stage). **M6 textures done (§40):** per-slot texture
  state, swizzled/linear A8R8G8B8 + R5G6B5 sampling with wrap/mirror/clamp,
  modulate by `oD0`, texcoords from `oT0`; all 10.1M pixels now sample
  (`textured=10137600`). **M7 fragment pipeline done (§41):** alpha test,
  stencil (fail/zfail/zpass ops), depth test/write (Z16/Z24S8), full blend
  factors/equations, color mask, nop-draw skip. **Interpreter fixes (§42):**
  R/V/C mux was rotated (now 1=R/2=V/3=C per xemu) and ORB polarity inverted
  (now ORB==1 writes oPos/oD0/oT0/A0.x); unpaired temp writes confirmed
  correct (R3/R4/R6/R7/R8 chain). **Pixel accounting (§42):** all 10137600 px
  come from 33 legacy fullscreen 640×480 inline draws; the ~2637 program
  draws are degenerate (their programs read never-loaded zero constants
  c58/c59 — no-ops on xemu too), so `blended=0 depth=0` is correct at
  this stage. Program draws validated live later (§§57–58: steady-state
  shaders read loaded C0, zero pixels proven guest-side clip). **M8 present done (§43):**
  flip read/write/modulo + `FLIP_INCREMENT_WRITE` (present event, captures
  the at-flip surface) + `FLIP_STALL` counting + vblank read-advance;
  `XGrid.Graphics/DisplayFrame` (BGRA frame store, 32-bit BMP export); CLI
  `--frame` saves the presented frame (`--fb` raw dump unchanged). Validated:
   `frames=33 stalls=33` (exact oracle match), final frame 640×480 from
   `0x32A4000` — opaque black (the flip chain presents the dashboard surface,
   not the `0x1000` region). Surface ops scoped in §44 (no missing blit op);
   inline `SET_VERTEX_DATA*` and `DRAW_ARRAYS` ranges remain.
- **USB hub topology + interactivity (2026-10-11, §§69–70):** the guest
  expects the Xbox daughterboard hub chain (root port 1 -> 4-port hub ->
  port 1.3 -> 3-port hub -> 1.3.1 -> XID); `UsbHub.cs` + OHCI RHSC/
  done-head/UsbReset fixes made the dashboard's XPP stack go live — it
  opens the pad and polls `GetState` every frame, D-pad levels move the
  menu selection (`obj+0x270`), and a step-timed A edge (`--pad-step`)
  fires the activation writer `0x1859D` (state 14 -> handler `0x1E66A`).
  A-press is no longer byte-identical (23,786-byte RAM diff). 007 also
  fetches the XID descriptor now.
- **PIT frequency fixed (2026-10-11, §68):** 1,193,182 -> 1,125,001
  (xemu PIT_FREQ); the game's 2 Hz tick pump is now exactly 10 guest-ms
  (median 7,332,029 = 10 x 733,333 steps) — its wait is guest-side
  pacing, not an emulation gap.
- **NV2A fidelity wave (§71):** inline `SET_VERTEX_DATA*` + `DRAW_ARRAYS`
  ranges (M5 gap closed), all 11 XF-context constant writers (c56-c59,
  c64-95; fixes §42's wrong "no-ops on xemu" claim), stride-0/bit31/
  program-start/output-defaults fetch fixes, MUL zero rule + GLSL
  min/max/LIT + paired-ILU + oPos epilogue, perspective-correct varyings
  + oD0 NaN->1. Fog + dependent/bump/dot modes (§78), fixed-function
  foggen + HILO_1 (§91), ZCLIP + float depth (§90), ZCLIP CULL +
  exact-1.0 (§97), exotic modes + HILO 5-7 (§96), textured-RGB-zero
  fix (§95: LU texel-unit coords, SZ POT base size, A4R4G4B4 decode
  — menu renders in color: 7,389 RGB-diff px vs neutral, 32,789 colors).
- **Sample-XBE + APU gates (2026-10-13, §§106–108):** `triangle.xbe` (nxdk)
  boots through the real kernel's DVD path and **renders** (colored Gouraud;
  §104's crash was three NV2A gaps: `NO_OPERATION` pusher stall until
  `PGRAPH_INTR` ack, `0x1D8C/0x1D90`→`0x1A88/0x186C` aliases, surface
  offsets = DMA-context base + offset; a pbkit depth clear had been wiping
  physical `0..0x12C000`). Kernel-test-suite sample boots clean. APU voice
  processor (§107): real position advance + notifiers — 007's PlayMPC
  streams complete via the advance callback/node removal (`0x20FC1` drain
  gate PASSED). V2 ROM-B/C green (§108). 1113 tests. Snapshot format v2
  (old `.xgs` invalid); pusher timing is now faithful so NV2A counter
  baselines shifted (re-record).
- **Wave 5b (2026-10-13, §§109–112):** triangle hatch FIXED (§109 — depth
  compare in zeta field space: the 3 coplanar demo triangles encode to the
  same field `0x10000` but raw interpolated floats straddled the stored
  decode ±1 ULP → LEQUAL pass/fail interleave; xemu `psh.c:1491-1516` floors
  depth before the test; frame `A1740DE0…` hatch-free, dashboard frame
  `4EF1FBF6…` unchanged; +2 tests fail pre-fix; test-harness PUT/barrier
  kick bug fixed). GPU-backend design + method inventory (§110 →
  `docs/research-notes/gpu-backend-design.md`: menu-subset slice 1, GLSL
  from own `PshCombiner`/`VshProgram`, golden-frame gates, `--gpu sw`
  retained; triangle stream 223,956 methods / 471 unique pairs). Opt-in
  `--idle-skip` (§111): spin-park fast-forward (0 HLTs in 1B boots; kernel
  idle thread `0x8001B02E`), flag-off byte-identical, flag-on 1.63×/1.90×
  wall clock, +11 tests. 007 post-drain probe (§112): gate-free to 18.1B
  (5.0B past §107), intro FMV live ~22 fps, stream end ~52.5B total next;
  probe origin `pd181.xgs`. Baseline **1126 tests**.
- **Wave 6 (2026-10-13, §§113–116):** **GPU-backend slice 1 LANDED (§113):**
  `IPgraphBackend` seam + `XGrid.GpuGl` (Silk.NET.OpenGL 2.23.0) + `--gpu sw|gl`
  (default `sw`); live golden gate PASSED on NVIDIA GL 4.6 — sw frame
  `4EF1FBF6…` (= recorded baseline), gl within policy (BMP `violations=0
  maxDelta=1`), `BackendUnsupported=0`, probe self-test gates init with sw
  fallback (`scripts/gpu-slice1-golden.ps1`). **007 stream END + new gate
  (§114):** G1 natural EOS node-removal @52,495,059,473 (§112 estimate
  spot-on), then new STRM + one drain, then **G2 park `{0x1C3028,0x1C302C}`
  (`[0x83924800+0x10]==3` never drains)** stable to 56.8B — snapshots
  `pd4915.xgs`/`pd546.xgs` (repros in §114). **nvnet first real frame (§115):**
  dashboard sends a real 342-byte DHCP DISCOVER (field-validated vs xemu); host
  `TcpProxy` + 5 tests (unwired by design). **ktest recon (§116):** suite is
  gated behind `D:\kernel_tests.log` CreateFile which fails every launch —
  0/378 tests ever ran (thunks fine; `tools/ktest-recon/` injector; symlink
  shellcode KeBugCheck open). **1205 tests** (1126 + 74 + 5).
- **Wave 7 (2026-10-13, §§117–120):** GPU slice-2 triangle golden PASSED on the
  **unmodified** slice-1 backend (§117 — zero `unsup` rows trip; FBO-grow
  content-preserving blit lands, fires 14×/boot with bit-identical frames; all
  §113 tripwires evidence-disposed via per-run counters). **007 G2 decoded
  (§118):** the park is 007's XDK DirectSound mixer submit spinning on the
  shared `[0x83924800+0x10]` state enum; the only re-opener is the **GP/EP DSP
  restart handshake delivered via APU ISTS bit3** — XGrid never raises it
  because the GP/EP DSPs started at init (+48.8M) are never executed (the §107
  open item); park onset pinned @53,657,687,344; force-raise proved the whole
  IRQ5→trampoline→ISR→worker chain. idle-skip V2 (§119): MMIO/PIO parks
  measured **0.038%** (negligible — §111 hypothesis disproven); read-model +
  deadline skip landed (+10 tests); corrected flag-on speedups 1.10×/1.66×
  (V1 ratios were contention-inflated). **kernel-test-suite UNBLOCKED (§120):
  378/378 tests execute, 53.7 MB log captured** — root causes: guest shellcode
  was ANSI-vs-UNICODE (its bug) + a real XGrid `f5 cmc` #UD gap in the kernel
   64-bit math chain (fixed, +1 test); xemu differential: XGrid AHEAD on
   NtResume/NtSuspendThread, 3 faithful failures. **1216 tests.**
- **Wave 8 (2026-10-14, §§121–124; survived a host power-loss mid-run).**
  APU GP/EP DSP bridge (§121): bit3 raises on the real wake (debounced
  ≤1/240 frames), **G2 park drains, 007 proceeds past 56.8B no fault**
  (new PlayMPC channel via first-ever `0x20BD0` HITs, frames 2017→2125,
  worker countdown 8→0, DVD streams a new 3 MB region); dashboard neutral
  byte-identical; +13 tests; Audio snapshot v3 with v2 fallback. GPU slice 3
  (§122): 007 transition + FMV goldens PASS (sw SHAs reproduce §114; gl
  byte-identical / correctly absent per the no-mixing rule), zero `unsup`
  rows; **fps table: GL submission+readback = 0–2.9% of wall** → swapchain is
  latency, not throughput; Track C JIT stays the lever; +6 tests. NV2A
  remaining (§124): measured-exercised rows only — 0x30 compare, PROJECT2D
  divide, TEXADDRESS V-decode + border; post-menu/tri byte-identical; +13
  tests (fail-pre-fix proven via stash); DXT/BRDF/DOT_STR_3D/mipmaps
  documented unused by menu+tri; **Nv2a snapshot v2→v3 — old `.xgs` must be
  regenerated** (new `_textureBorderColor[4]` + `_shadowDepthFunc`).
  ktest smoke (§123): **PASS 7/7 rows green ×2 cycles, ~5 min**, zero product
  changes. **1248 tests.** Recovery worked exactly per the liveness rule:
  branches intact, partial work inventoried (keep-vs-revert), all four agents
   relaunched to green.
- **Wave 9 (2026-10-14, §§125–127; five tasks, four green, one closed).**
  T1 dashboard golden PASS (1B menu ×2 runs, vec=0, SHA `4EF1FBF6…`
  bit-exact, counters run-identical); T2 ktest smoke PASS 7/7 in 04:36
  (steps bit-reproducible 792,800,535, zero deviations). T3
  present-upload plumbing MERGED (§125: `PgraphSurfaceRecord` carries the
  guest-physical surface address, plumbing only, +2 tests, menu golden
  unchanged). T4 JIT scoping FILED (§126: decode cache + fetch fast path
  as the next slice, no codegen). T5 007 long probe CLOSED-unrun — stuck
  past every review point with no checkpoint return, then closed by
  **standing operator rule: no real Xbox games at this stage; all title
  work uses `data/test-samples/`** (superseded 2026-10-24: retail gameplay is
  M-G; the optimized ISOs at `H:\Tests\XBOXReady` are the lead acceptance) (`triangle.xbe`, `nxdk-hello/led/
  mesh/triangle/`, `kernel-test-suite-default.xbe`); 007 probes deferred.
  **1250 tests.** Poll-discipline lesson folded into the BOSS protocol.
- **Wave 10 (2026-10-14, §§128–130; five tasks, all green, three merges).**
  T1 JIT profiler MERGED (§128: nullable `IHotspotObserver` at `Step()`
  line 106, mnemonic/PC/TLB counters, `--hotspot` switch; flag-off AND
  flag-on byte-identical on menu+triangle; hook cost +5.5% — the §126
  "under 3%" estimate disproven, batching noted for slice 2; +5 tests).
  T2 present-upload MERGED (§129: `_fbo == 0` branch builds the FBO from
  guest RAM via `IGpuMemory`, full-frame replace, never mix; goldens
  unchanged; `presentUploads=0` on menu/tri is correct — the live shape
  needs 007, forbidden; +12 tests). BOSS fixes at merge: §128/§129
  §-union, and T2's new `Xunit.SkippableFact` dependency REJECTED per
  the sources.md rule (plain `[Fact]` + WGL-guard return instead). T3
  sample matrix set (triangle SHA exact, smoke PASS; **nxdk sources
  BLOCKED — no toolchain on host**, the binding missing piece for mesh).
  T4 smoke PASS 7/7 in 03:54. T5 clamp-6/7 MERGED (safe choice, zero
  head movement, +1 test). BOSS reclaimed 84.5 GB scratch qcow2s (C:
  4.8 → 89.3 GB free). **1268 tests.**
- **Wave 11 (2026-10-14, §§131–133; four tasks, all green, one merge).**
  T1 JIT slice 2 MERGED (§131: batched TLB tallies, windowed top-64
  heads — overflow 23%→0.3%, `--fetch-fast` default-off; **triangle
  1.35× / menu 1.30× Msteps/s**, both SHAs + counters exact, flag-off
  byte-identical; +9 tests). Leg (d) decode cache DEFERRED with full
  handoff in §131 (est. 1.5–2× the (a–c) budget — next wave's lead).
  T2 staleness design FILED (§132: recommendation (a*) dirty-bit in
  `PhysicalMemory` + synthetic-harness gate). T3 smoke PASS 7/7 in
   03:51. T4 merged-master goldens PASS both legs (post-wave-10 gap
   closed). **1277 tests.**
- **Wave 12 (2026-10-14, §§134–135 + swapchain brief; four tasks, all
  green, two merges + docs).** T1 decode cache MERGED (§134: `DecodeCache`
  linear-key / physical-reverse-map block cache, conservative invalidation
  at the §131 inventory sites (five `XboxMachine` shims page-drop — no
  device changes), `--decode-cache` implies fetch-fast, `--decode-verify`
  memcmp tripwire; **menu 2.12–2.20× / triangle 2.97–3.07×** vs flag-off,
  hit share 99.53%/99.80%, decode 0.47/0.20 per insn, verifyFailures=0
  @1B; flag-off byte-identical, both SHAs + counters exact on both flags;
  +14 tests). T2 dirty-bit (a*) MERGED (§135: `IPresentDirtyWatch` choke
  in `PhysicalMemory.Write8/16/32/64` + suppression on the two
  `IGpuMemory` adapters, FlipCore dirty→upload / clean→serve / guard→
  flipEmpty, 4-slot round-robin span watch for the menu double-buffered
  presents; menu/tri goldens unchanged with `presentUploads=0
  flipStaleUploads=0`, A/B hook cost 0.949 within noise; +25 tests). BOSS
  merge conflicts: `PhysicalGpuMemory` combined both ways (suppression +
  page-drop) + one stray brace caught by the post-merge build; notes
  §-union 134/135. T3 base gates PASS (smoke 7/7 known deltas; menu/tri
  SHAs + counters exact). T4 swapchain brief FILED
  (`docs/research-notes/gpu-swapchain-brief.md`: O2 pure-interop
  `--window` recommended, O1 headless stays default, vsync off, O3 needs
  a single-dependency override; xemu's Vulkan renderer also presents via
  SDL GL). Also landed: `tools/wave` automation (parallel session, 26
  tests, committed separately per operator). **1316 tests; post-merge
  gates green: smoke 7/7, menu SW SHA anchor + GL SHA bit-identical to
  base, triangle PASS, `presentUploads=0` both, format clean; CI green
  on all three OSes (first green since §127 — WGL guard + test de-flake
  landed pre-wave).** Wave worktrees on `D:\wave-worktrees\` (C: has
  only ~5 GB free — keep media copies on D:, never on C:).
- **Wave 13 (2026-10-15, §§136–138; three tasks, all green, two merges +
  resolver).** T1 first-codegen scope FILED (§138 + `docs/research-notes/
  jit-codegen-scope.md`; probe, unmerged by design): measured remaining-cost
  breakdown (switch dispatch NOT dominant — data-path helpers are), §20.6
  emitter benchmark with the honest negative result (no managed emitter beats
  a plain C# helper loop — semantic specialization required; DynamicMethod
  provisional), boundary-safe block design + next-slice recipe. T2 block
  executor MERGED (§136: `DecodedBlockCache`, `--block-exec`/`--block-verify`
  opt-in, boundary-preserving; suite 1324 (+8); menu/triangle 1B SHAs + all
  counters exact in off/decode/block/verify/profile; flag-off byte-identical;
  dispatch share 30.9–36.6%; uncontended pairs 1.04–1.20× over decode-cache).
  T3 decode default-on soak MERGED (§137: default flip + `--no-decode-cache`
  opt-out + `scripts/decode-cache-soak.ps1`; menu 2.56× / triangle 3.04×
  default vs off; verifyFailures=0). §136 collision resolved by the wave
  resolver (decode → §137). Final gate on merged master: format + build +
  test ALL PASS; **ktest smoke PASS 7/7** (792,800,535 steps
  bit-reproducible). Also landed during the wave: `tools/wave` fixes
  (terminal-finish turn detection, state-save retry on Windows file locks)
  and a `wave dashboard` command (127.0.0.1:4181 live task/log view).
- **C3 raw emitter (2026-10-20, §§199e/199f + correctness campaigns §199g/§199h; opt-in
  `--jit-raw`).** One native x86-64 function per block (Iced 1.21.0, MIT): guest GPRs in fixed host
  registers, arithmetic flags in the host FLAGS register, memory members running the data
  translate-cache probe inline against pinned pointers, and a reverse-P/Invoke trampoline to the
  existing spec handlers for every unencoded shape. **Two correctness campaigns closed the
  JitTests gate (12 → 0 failures with `JitRawBlocks` forced on; suite 1495 green)** and fixed
  sixteen latent bugs (decisions §199g/§199h) — the second campaign's two headline items:
  **the store guard's note path clobbered R9 (the linear address), so every page-table store
  took the slow path with a garbage address whenever a watch was armed (the CLI arms one by
  default) — the triangle's 25.05M #PF at CR2=0x0D**; and **the epilogue materialized the frame
  flags with the emitter's compile-time liveness, corrupting the EFLAGS pushed by the poll's
  pending delivery (0x202 instead of 0x246) — the triangle's 30.92M divergence**. Both fixed;
  the guard now runs the exact IsWatched range test before the note path (which also restored
  inline stores: previously every store went slow while any watch was armed). **Differential
  status: BOTH titles are now bit-exact at 1B** — the §199h menu gap was closed by the third
  campaign (§199i): the four split fast/slow members (RMW destination, cmp-memory, test-memory,
  inc/dec-memory) skipped the fast path's EIP advance because the slow path's `EmitHelperMember`
  sets the emitter's compile-time `_memberHelper` flag, so a block whose LAST member was one of
  them exited on the member's first byte and the member path re-executed it (one-instruction
  drift, visible as the MMX-loop byte). Found with the new block-level trace
  (`XGRID_BLOCK_TRACE`): first differing invocation head=0x1E010, 32 members, exit 0x1E08B (IL)
  vs 0x1E088 (raw), M31=`or [eax+0x2C],edx`. Menu 1B now matches the IL anchor exactly
  (RAM `660CA11B…`, CPU `F5D61C94…`, every counter identical); triangle 1B RAM `A2DCA55F…`, CPU
  `7DAB63EB…` on both engines. Regression test
  `BlockEndingInMemoryRmw_AdvancesEipOnce` (fail-pre-fix: TSC 71 vs 70, double inc). Suite
  **1499** green (1497 at that session close), ktest XGrid leg at 792,800,535 steps with a
  byte-identical suite log. Performance: the raw path is now
  **ahead of IL on CPU serving time** after the §199k state hoist (`BuildRawState` once per
  `RunJitBlock` call instead of once per block invocation; the state lives in an unmanaged
  per-CPU block): 100M A/B 9.0% menu / 2.4% triangle faster, 1B `outerMs` -3.7% menu / -2.3%
  triangle with both title differentials bit-exact. **Direct raw block chaining landed in the
  sixth session (§199l, after the §199m fault-contract fix): 1B medians of 3 interleaved pairs
  triangle wall -12.0% / `outerMs` -28.6%, menu wall -13.1% / `outerMs` -14.2%**, SHAs and every
  counter identical to the IL control. Quiet-host calibration this session:
  triangle IL 1B **10.20 s**, menu IL 1B **~40 s** (gl+replace+spin) — both faster than the
  §199d baselines (10.63 / 45.62).
- **Track B present path (§204, 2026-10-20):** the GL path's ~7.9 s menu overhead
  is ~1.3 s. The per-flip CPU readback is opt-in (`--frame`/`--frame-first`; the
  window present blits the FBO), each draw is one `uTri[25]` uniform upload + one
  `glDrawArrays` (2 GL calls, was 13 uniforms + a 24-byte `BufferData` + 2 binds),
  texture keys/fingerprints no longer format strings or divide per texel, and the
  per-method seam call + method histogram are gone from the hot path
  (menu sw -6.2%). Same-window medians vs `4f0bd47`: menu `gl+replace`
  63.09 → 45.25 s (-20.5%, 3/3, `drawMs` 12.55 → 2.41 s, readback → 0), triangle
  10.26 → 9.89 s (-0.9%, 3/3). 1B goldens pass (menu gl `6BE6325F…`, triangle gl
  `3FC4B419…`, sw anchors unchanged, `BackendUnsupported=0`); the automated
  acceptance run projects menu **24.8 s** (0.8 over the 24 s target) and triangle
  **6.1 s** (0.1 over) on a loaded host, so M-A is not fully closed.
- **Track B draw-command batching (§205, 2026-10-20):** a PGRAPH primitive flush
  averages **82 triangles**, so the GL backend now stages a flush and submits it
  as **one** `glDrawArraysInstanced` with one `uTri[capacity*25]` upload (the
  per-triangle block is a uniform-array slot selected by `gl_InstanceID` through a
  flat varying; capacity 64 from the uniform budget; `XGRID_GL_NO_BATCH=1` forces
  the old path). Same-binary interleaved A/B at 1B menu `gl+replace`: `drawMs`
  1971 → **1045 ms (-47%, 3/3)**, wall median 42.20 → **40.43 s (-4.2%, 3/3)**,
  59,437 instanced draws replace 2.76M single draws, guest SHAs identical, both
  1B goldens **byte-identical** under the §204 policy (menu `6BE6325F…`, triangle
  `3FC4B419…`), ktest log byte-identical. Projected onto §204's 24.8 s menu
  projection: ~23.8 s against the 24 s M-A target. What is left of submission is
  ~0.8 s of block packing, not GL calls.
- **GL sampler parity + replace-mode composition (219, 2026-10-21):** the GL
  backend now runs the software sampler - TEXFILTER tent and the nine-tap 1-2-1
  convolution (MIN = CONVOLUTION_2D_LOD0 only), the *decoded* wrap modes with
  clamp-to-border (the seam carries `SamplerWrapU/V`/`BorderColor`/`Bilinear`/
  `Convolution`; the backend had read the raw word, where 1 is wrap and 2 is
  mirror), the PROJECT2D divide by w, and the clip origin on both fragment axes.
  In `--gpu-replace` a declined draw composes the FBO into guest RAM and the
  frame continues in software, so a mixed frame presents the complete image
  (gap 8 closed: `bump_cubemap_reflect` 3B presents `DA0750EE` = the software
  frame; depth/stencil are not composed - documented). Both 1B GL goldens stay
  byte-identical (`B6116DAE` / `7A860ED7`), `BackendUnsupported=0`,
  `composedFrames=0`, fast anchors unchanged, ktest PASS 7/7.

- **Cubemap reflection + TEXFILTER (§209, 2026-10-21):** `xgu
  bump_cubemap_reflect` renders its reflection (2 colours / 36% black → 21,041
  colours / 0% black, `|dmean|` (5.3, 2.4, 1.3) at 3.6B). The dominant bug was
  the cube face extent read from `TEXIMAGERECT` instead of TEXFMT `BASE_SIZE`
  (xemu's swizzled-texture rule); the three format gates (`0x3A`-`0x3C`
  whitelist, `CubeBytes`, shared `DecodeTextureTexel`) were widened together,
  and `TryDecodeSwizzledTexel` now shares the 2D decoder. The direction chain
  needed no change. Also landed: **TEXFILTER is now honored** - tent at LOD 0
  (per-tap wrap, face-local clamp for cubes) and the 9-tap quincunx/gaussian
  convolution on PROJECT2D - **opt-in** as `--tex-filter registers` (default
  `legacy` keeps every anchor byte-identical; the flag-on menu changes 8.7% of
  pixels with an identical CPU SHA, at 2.4x sw wall because compiled fills
  fall back to the generic loop). 1614 tests.
- **TEXFILTER convolution gate + compiled tent fill (§210, 2026-10-21):** the
  2.4x/8.7%-pixels §209 attributed to the tent was a **misapplied convolution**.
  A TEXFILTER kernel field only convolves when MIN is `CONVOLUTION_2D_LOD0` (7)
  (`xemu psh.c:197-210`); the dashboard and `bump_cubemap_reflect` program
  `2/2/1` = tent, no blur. `EffectiveConvolutionKernel` now decodes it once for
  the sampler and the `filters=` diagnostic (which prints the *effective*
  kernel). Both compiled fills serve the tent (a shared exact `ReadTapFast` tap
  reader), so 1B menu `registers` went **79.87 → 31.90 s** (legacy 32.22 s) with
  `compiled=` identical to the legacy leg, and triangle 10.53 → 11.66 s. The
  flag now moves **no gate anchor**: triangle RAM/CPU/frame identical, menu
  flipped frame byte-identical to `4EF1FBF6…` (only the back surface differs).
- **Fixed-function vertex stage (§213, 2026-10-21, opt-in `--ffp`):** the NV2A
  "cheops FF" vertex stage (transform, 1-4-matrix skinning, texgen, texture
  matrix, color selection, eye-space fog, screen-space epilogue) transcribed from
  `xemu 0.8.137 vsh-ff.c`, with the register decodes and
  `FixedFunctionDraws`/`FixedFunctionLitDraws` counters (snapshot **v6**). The
  two `xgu *_ffp` corpus rows render their scenes (2 → 7,358 and 26 → 7,601
  colours, CPU SHAs unchanged); the triangle's 1B RAM/CPU/frame anchors are
  byte-identical with the flag on, the dashboard's menu anchors move (22,207 of
  its draws are fixed-function). Lit draws run the material/ambient/emission
  chain; the enabled-light loop is filed and counted in `fixedFnLights=`.
- **Fixed-function lighting + PTIMER 0.8.137 (215-216, 2026-10-21):** the
  fixed-function vertex stage is now complete - the enabled-light loop
  (infinite/local/spot, `1/(a0 + a1 d + a2 d^2)` attenuation, the spot cone,
  the `LOCALEYE` view vector) and the reconstructed specular exponent, behind
  the same opt-in `--ffp`; no measured leg enables a light (`fixedFnLights=0`).
  PTIMER now models the 0.8.137 alarm: writable `TIME_0/1` offset, `ALARM_0`
  with the 2^32 re-arm, the alarm `INTR_0` bit and `NV_PMC_INTR_0_PTIMER`
  (bit 20). NV2A snapshot v6 -> **v8**. Every gate anchor stayed byte-identical.
- **Track P (217, 2026-10-21):** the frontend exposes `--ffp` and The APU sample counter
  `NV_PAPU_XGSCNT` (0x200C) is now derived from the EP frame divider too,
  which moved the menu RAM anchors (218; see the session note).
  configured firmware image stays a read-only seed; CLI `--eeprom-out`).
  `tests/XGrid.Frontend.Tests` (15 tests) is the frontend test project.
- **GL TEXFILTER parity (219a, 2026-10-21):** `Nv2AMmio.DecodeTextureFilter`
  is the single TEXFILTER decode (LOD-0 tent + the effective PROJECT2D
  convolution), `PgraphTextureSlot.FilterBilinear`/`FilterConvolution` carry it
  to the backend, and `PshGlslGenerator` emits `SampleTent` (`SampleBilinear`
  transcribed) / `SampleConvolved` (nine-tap 1-2-1) behind per-slot uniforms
  `uTexFilter`/`uTexConv`. Gap 7 closed; both GL goldens byte-identical.
- **GL replace-mode composition (219b, 2026-10-21):** a draw the backend
  declines is painted in software, so the flip drops the FBO and the present
  upload rebuilds the whole frame from guest RAM (`composeUploads=`). The
  cubemap corpus row's replace leg now presents `DA0750EE…` = the software
  anchor (gap 8).
- **A clear ignores the draw write masks (219c, 2026-10-21):** `ClearCore`
  lifts `glColorMask`/`glDepthMask`/`glStencilMask` for the clear and
  invalidates the state shadow; pinned by `ClearMaskLiveTests` (fails pre-fix)
  and two `Nv2aZetaClearLayoutTests` cases.
- **Per-title overrides V2 (219d, 2026-10-21):** `XGrid.Host.DiscTitleId` reads
  the XBE certificate title id from a loose `.xbe` or an ISO's root
  `default.xbe`; overrides can be keyed `title:XXXXXXXX` and the session logs
  the resolved title. A CHD has no XDVDFS reader yet and falls back to the path
  key. CLI `xgrid disc title <path>` prints the id/key. Caveat: homebrew images
  that never set a certificate id share the placeholder (`0xFFFF0002`).

- **Track P/B: the frontend on the host GPU backend (§221/§223, 2026-10-21):** the
  Avalonia frontend has a **Renderer** setting — `Software rasterizer` (default,
  portable) or `OpenGL backend` (`--gpu gl --gpu-replace`, Windows only).
  `Nv2AMmio.GpuMemory` exposes the machine's guest-memory adapter, the new
  frontend `GraphicsBackendHost` attaches `GlBackendFactory` on the emulation
  thread (`replaceSoftware`, `frameReadback`) and detaches it in the session's
  `finally`; frames still arrive through `TryCopyDisplayFrame`. Snapshot
  save/load is now marshalled onto the emulation thread (a load resets backend
  state and a GL context belongs to its thread). Measured on the dashboard (40 s
  interleaved pairs): **software 1.014B / 1.021B steps vs OpenGL 1.121B / 1.106B
  (+9.8% / +8.2%)**, same non-black pixel count, working 72 MB save/load round
  trip; the CLI's 1B menu leg is the ceiling at **33.95 s -> 23.46 s (-30.9%)**.
  The software default is byte-identical everywhere (all four 1B anchors exact,
  ktest log byte-identical).
- **Release tree (R1.1, 2026-10-23):** `scripts/package.ps1` publishes the CLI
  and the frontend self-contained for `win-x64`/`linux-x64`/`osx-x64`/`osx-arm64`,
  ships `COPYING` + `LICENSE` + `THIRD_PARTY_NOTICES.md` + the version's release
  notes + `VERSION`, and writes per-RID and combined SHA-256 lists plus
  `package-manifest.json`; `scripts/package-verify.ps1` is the acceptance gate
  (license set, notices coverage of every ported file, archive contents, per-RID
  executable magic, checksums/manifest, packaged CLI `--version`). The product
  version has one source (`Directory.Build.props`, `<Version>0.1.0</Version>`) and
  the CLI prints its assembly version. The GPL-2.0 review is
  `docs/release-licensing-review.md`, notes are `docs/release-notes/`, and no OS
  installer is produced (portable archives; code signing/notarization deferred).
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
  once; conflicts were purged 2026-10-09). Baseline: **1922 tests green**,
  `dotnet build`/`format --verify-no-changes` clean (2026-10-23).
- **§237 + §238 (2026-10-22 late): the boot ROM is a cached, blocked image
  window, and the non-JIT run slice is 64.** `IImmutableImageDevice` +
  `AddressSpace.TryGetImmutableImageWindow` classify flash/BIOS/mirror/overlay
  fetches; the decode cache holds them in a key set with no store path and drops
  them wholesale on the host actions that move an image; blocks form over them
  (and re-form when the decode defaults move with the CPU mode).
  `JitSpecSlice` then went 256 -> 64 (never swept before; `nonJitMs` falls to 4
  but formation eats it). Measured together: **triangle `gl+replace`
  6.672 -> 6.081 s (-8.9%)**, menu `gl+replace` 20.456 -> 19.762 s (-3.4%),
  100M boot phase 2.276 -> 1.813 s (-20.3%), `nonRamSkips` 359,476 -> 0,
  `nonJitInstructions` 5.48M -> 2.62M, every 1B SHA/counter/frame and every
  corpus row byte-identical. §237 also **repriced the "superblock for the boot"
  lever out** (the ROM share of the non-JIT population is 9%, not 100%).
- **TM3 (2026-10-24, decisions §254): SAHF/LAHF were the second shared
  blocker.** The three fast-mode `#UD` rows (Halo/Tak/Ninja Gaiden Black) were
  not a JIT bug: the decoder had no one-byte `0x9E`/`0x9F`, so the retail x87
  tail `fprem; fnstsw ax; sahf; jp` raised `#UD` in **every** engine and the
  kernel halted. Both instructions now decode and execute with the reference
  `gen_SAHF`/`gen_LAHF` semantics (12 new QEMU-TCG oracle cases, corpus 641);
  all three titles run 1B steps with `LastFault vec=0`, the 44-row matrix is
  **2 own-render / 42 dashboard / 0 fault / 0 timeout** (Tak is a new
  own-render row), and **no gate anchor moved**. The remaining one-byte decoder
  holes are filed in `real-game-probes.md`.

## Session notes - newest first (wave runner retired 2026-10-15).

**Session 2026-10-26 (fifty-ninth; TM8 ported the xemu APU voice processor - the
APU produces real audio, and the frame cadence was 8x slow - decisions 259).**

- **The blocker, in one line.** `McpxApuDevice` ran the guest DSP microcode with
  an **empty mix-bin frame**: the GP's 32x32 mix buffer was written as silence
  every frame, so the voices the guest programmed were never mixed and
  `--audio-out` wrote 877,568 bytes of **all-zero** PCM (TM4's measurement).
  xemu's `se_frame` is `mcpx_apu_vp_frame` (fill the bins) then
  `mcpx_apu_dsp_frame` (write them into the GP mix buffer, run the GP).
- **The port (four files, all behind `ApuDspCore`).** `McpxApuDevice.Vp.cs` is
  `vp.c`'s per-frame path: the 2D/3D/MP voice-list walk with the idle-voice trap,
  both envelopes (`voice_step_envelope`: delay/attack/hold/decay/sustain/release/
  force release, EA and EF), `voice_get_samples` (PCM and ADPCM, buffer and
  stream, loop/LBO/EBO, the SSL segment list, the SGE page walk),
  `voice_resample`, `voice_process` (bins, the eight volumes, submix and HRTF
  headroom, the LPF gate, HRTF, multipass) and the mix-bin accumulation.
  `Vp/ApuVpAdpcm.cs` is `adpcm_decode_block` (ADPCM-XQ, BSD-3),
  `Vp/ApuVpFilters.cs` is `svf.h` + `hrtf.h`, `Vp/ApuVpFrame.cs` is the frame
  geometry and `fpconv.h`. The front end gained the PIO methods the mix path
  needs and the old code did not handle (`SET_CURRENT_HRTF_ENTRY`,
  `SET_HRIR..SET_HRIR_X`, `SET_HRTF_SUBMIXES`, `SET_HRTF_HEADROOM`,
  `SET_SUBMIX_HEADROOM`), `VOICE_ON` now stores the ENVA/ENVF start phase into
  `PAR_STATE` and resets the voice filters, and the SSL segment window ends at
  `0x803` (it stopped at `0x7FF`, losing the last entry).
- **The cadence was 8x slow, and that is part of the port.** xemu's APU frame is
  32 samples (`EP_FRAME_US`), so its GP runs 1500 times a second and its EP every
  eighth frame. XGrid's APU frame is 256 samples, so the old
  `McpxApuDspPair.Frame` ran the GP once per 256 samples - eight times too slow
  in guest time - and the EP once per 2048. `AdvancePortedFrame` runs the voice
  processor and the GP **eight times** per XGrid frame and the EP **once**. The
  legacy bridge path keeps its old cadence, so nothing outside `ApuDspCore`
  moved.
- **Audio is no longer silent (007).** Before: 877,568 bytes of PCM, **0
  non-zero samples**. Now, 2B steps from boot: 377,856 bytes / 94,464 frames,
  **80,148 non-zero (42.4%)**, peak 7,500, RMS 1,023
  (`wt-data/tm8/007-audio-boot-2b.wav`); from the 26B anchor + 2B: 381,952 bytes
  / 95,488 frames, 90,925 non-zero (47.6%), peak 5,547, RMS 767
  (`007-audio-26b-2b.wav`). `gpFrames=3200 epFrames=371 gpUndefined=0
  gpLimitHits=0 LastFault vec=0` on both. It is audio, not noise: the RMS
  envelope has quiet and loud sections (to 3,157 at 1.6 s), `mean|dL|/mean|L| =
  0.126`, the channels differ, and a DFT of the gameplay leg peaks at
  **105.5/152.3/199.2/316.4/503.9 Hz** (a bass note and its harmonics).
- **The TM4/TM5/TM6 anchors needed a bootstrap path.** Those `.xgs` files were
  captured **without** `--apu-dsp`, so their payload says "no DSP state" and the
  engines come up with no program - and the guest wrote `GPRST`/`EPRST` before
  the anchor and never writes them again, so a resumed run stayed silent (the
  first attempt measured `gpFrames=0` and 0 PCM frames). `LoadState` now derives
  the run state from the restored reset registers and bootstraps the GP/EP from
  the restored scratch window on the next frame (RAM is not ready during the
  load, so it is deferred); it writes the run bits through the DSP
  sub-window so the core's own IsRunning is true too. APU snapshot **v6**
  appends the mixer state
  (headroom, HRTF submixes, 128 HRIR entries, per-voice SVF/HRTF history,
  resampler position); v2-v5 still load, so no `.xgs` is invalidated.
- **Tests.** `tests/XGrid.Audio.Tests/ApuVpMixerTests.cs` (12 cases) pins the
  ADPCM vector (`0x1000` then 4103, 4104), the invalid step index, a buffer voice
  landing in the GP mix buffer at exactly `0x400000`, zero volume, submix
  headroom (2 bits = 1/4), the pitch path (one octave up consumes two source
  frames per output frame plus two lookahead frames), the attack envelope (level
  239 at counter 15, full scale and hold at 17), the HRTF submix redirect, a
  stream voice walking its SSL segment and notifying, the SSL segment register
  window, a paused voice and a mixer-state snapshot round trip.
  `XGrid.Audio` gained `InternalsVisibleTo` for the test project. Suite 1996 ->
  **2008**, build 0 warnings, format clean.
- **Gates: not one anchor moved.** Accurate menu sw `4EF1FBF6` / gl `B6116DAE`
  (counters 34,333/686/686, `diff=2602 violations=0 maxDelta=1`) and accurate
  triangle sw `5B50373D` / gl `79142DE4` (2,707/69/34, `diff=27849
  violations=0`) both PASS byte-identical; ktest parks at `EIP=0x0004E03A` after
  **792,800,535** steps with `LastFault vec=0`. `gpu-slice2-triangle-golden.ps1`
  still carried the pre-TM2 anchors (`A1740DE0`, 2706/68/34) and now records the
  TM2 re-baseline; both gate scripts also need `-Bios/-Mcpx/-Eeprom data\xemu\...`
  on H1 because their default `data\xemu-0.8.136-windows-x86_64` is absent. The
  ktest **suite-log SHA could not be re-extracted** this session (the prepared
  HDD's C: root has no `kernel_tests.log` and the image mtime is unchanged), so
  that leg is reported unverified; the park EIP and step count are exact.
- **Not an oracle on this host:** xemu's `-audiodev wav` never opens (its APU
  monitor needs an SDL device: "SDL_OpenAudioDeviceStream failed: No default
  audio device available") and the EP sink bypasses QEMU's audio subsystem, so
  there is no xemu audio capture. `libsamplerate` is replaced by a linear
  interpolator that is bit-exact at rate 1.0 (pitch 0, the common case), which
  the reference itself says the hardware may well be.
- **Score:** P12 APU 85 -> 95, P24 Retail 55 -> 60 = **79.65%** (93.99 of 118).
- **Next: TM9** - the Ninja Gaiden Black colour count (12,913 against xemu's
  1,351 with every channel mean within 11/255), i.e. the fragment-path dither.

**Session 2026-10-25 (fifty-eighth; TM7 published the seven-title table with one
command per row, and the xemu oracle came back - decisions 258).**

- **The harness.** `scripts/retail-game-probe.ps1 <iso>` wraps
  `title-probe.ps1` (XGrid leg), `xemu-oracle.ps1` (oracle leg) and
  `frame-compare.py` (policy), writes `<slug>.bmp`/`.png`, the xemu frame, the
  row JSON, the Markdown report and the raw XGrid probe into one directory, and
  prints one `PROBE` line. `title-probe.ps1` gained `-LoadState`, `-FramePath`
  and `-StatePath` (backward-compatible). It **fails soft**: `xemu: blocked -
  <reason>`, exit 0, no fabricated comparison.
- **Two traps encoded.** A `.xgs` records its HDD path and the loader compares it
  **as a string** - `IDE HDD path mismatch on snapshot load (snapshot='wt-data\chain-199l\hdd.qcow2',
  live='C:\...\hdd-clean.qcow2')` - so the harness reads the image out of the
  snapshot (8 MB chunks from the tail) and passes it unchanged; resolving it to an
  absolute path fails the same way. And `--load-state` resets the step counter, so
  `-Steps` is "steps after the anchor" and pad offsets restart at 0.
- **The capture guard was earned.** The first NGB attempt at 45 s "captured" a
  **Chrome window**: `PrintWindow` answered black while the SDL/GL surface was
  starting and the screen-copy fallback photographed the browser (TM6's failure
  mode, reproduced). `xemu-oracle.ps1` now retries `PrintWindow` five times
  (800 ms, re-asserting topmost/foreground) and uses a screen copy **only** when
  `GetForegroundWindow()` is xemu, else throws `capture contended`; the harness
  also rejects a blank capture (<=2 colours or >=99% black). The bad file was
  deleted and the row re-captured.
- **The seven rows (all re-run from their anchors, `LastFault vec=0` everywhere):**
  007 **PASS** `dmean=(18.0, 22.6, 14.6)` - the TM4 numbers exactly (134,004
  draws / 1,005,989,482 px / 6,478 frames, EIP `0x0001FC9E`); Halo **FAIL**
  `(101.6, 100.4, 113.1)` - TM5's fail reproduced (3,247 / 191,496,512 / 516,
  `0x00063487`); **Ninja Gaiden Black matches xemu's stormy-sea intro within
  11/255 on the means and fails only on colour count (12,913 vs 1,351)** - the
  dither pattern is now the next named rendering defect (4,251 / 187,315,817 /
  238,137, `0x8001B047`); Bad Boys II **FAIL** 100% vs 20.5% black - the
  documented wait quantified against xemu's own cutscene (2,695 / 49,061,482 /
  57, `0x001A3DEA`); 50 Cent `n/a` different cinematic shot (`dmean=(28.4, 20.7,
  18.7)`, 3,534 / 299,139,810 / 721, `0x00132FEF`); Tak `n/a` xemu on the title
  screen (13,691 / 42,516,749 / 14,503, `0x800198AE`); Destroy All Humans! `n/a`
  title screen vs the Pandemic logo card (6,785 / 81,580,491 / 651, `0x00218674`).
- **xemu cannot be driven on this host** (SDL-gamepad input only), so it sits on
  title screens for Tak/DAH and on a different shot for 50 Cent: those rows are
  `n/a`, never a pass and never a fail. A capture is a window screenshot, so the
  policy is mean / colour count / coverage, never pixel-exact.
- **The step anchor is part of the row:** 007 at 102B (`+2B`) reads
  `dmean=(33.0, 32.6, 20.2)` - the same scene at a different moment - while the
  same anchor plus 1M reads 18.0/22.6/14.6.
- **Gates: not one anchor moved** (scripts + docs only, no `src/` change; the
  four accurate legs and ktest were still re-run): accurate menu
  `4EF1FBF6`/`B6116DAE` (`5C0CF968`/`E2B4D327`, vec=0), accurate triangle
  `5B50373D`/`79142DE4` (`E58AE5D7`/`C43D5B49`, vec=0), ktest park `4E03A` at
  **792,800,535** steps with the **40,797 B `CA5D3867`** log byte-identical.
  Suite count unchanged (no product code). Score 78.4 -> **78.89%** (93.09 of
  118: P20 82->85, P24 50->55).
- **Next: TM8** - the first targeted xemu port (default `hw/xbox/mcpx/apu/vp.c`
  + AC'97), since audio is the one silent part of the seven-part definition.
  Resume: `wt-data/tm7/<slug>/` (frames, rows, reports) and the `.xgs` anchors.

**Session 2026-10-25 (fifty-seventh; TM6 probed the second-tier five and found the
shared blocker in our own NV2A - `NV097_INLINE_ARRAY` was a screen-space
shortcut, so Ninja Gaiden Black rendered nothing - decisions 257).**

- **The blocker, in one line.** `FlushInlineDraw` required float attributes, read
  the first attribute as the screen position and the second as the colour, and
  never ran the resident vertex program; the reference
  (`pgraph_gl_bind_inline_array`) lays the word stream out as a packed vertex
  buffer decoded by the array formats and transformed by the program. Ninja
  Gaiden Black submits D3D FVF vertices (`F32x4` position + `UB_D3D` colour +
  `F32x2` texcoord, 28 bytes), so it hit the `(format & 0xF) != 2` check:
  **1,073,213 skipped draws** at 10B and a black display.
- **How it was found.** The new `skipReasons=` counters on the NV2A line split
  the skip sites; a 5M-step probe from the title's 10B snapshot read
  `0/0/0/0/0/184/0/0` - the legacy-inline vertex-format check.
- **The fix.** `BuildInlineArrayLayout` (the reference's per-attribute alignment
  and stride), `FetchInlineArrayAttribute` decoding through the shared
  `DecodeVertexAttribute` (one decode, reader selected by an `inlineSource`
  flag), `VertexFetchMode.InlineArray` running the program path, and
  `CanRunProgramDraw` keeping the legacy screen-space path as the fallback when
  no program can run.
- **Ninja Gaiden Black renders its own content.** 720x480 stormy-sea intro
  (13,789 colours, 0.5% black), 4,252 draws / 187,381,609 px / 238,138 frames at
  10B+2B steps, `LastFault vec=0`, kernel idle `0x8001B02E` between frames, a
  held A changes the scene. At 1B the matrix row went 2,672 -> **12,800** draws
  and 41,995,882 -> **166,119,138** px at the same 6,475 frames.
- **The other four.** 50 Cent renders its own 3D scene (18,638 colours, 3,534
  draws / 299,139,810 px, EIP `0x1455BA`, PIC `req=0x08`); Tak renders its own
  scene and then a ~910-draw/frame one where the software rasterizer is the wall
  (300M steps = 362 s, 347 s in 3,228,625 fills, 1.27e9 z-clipped fragments);
  Bad Boys II is a documented wait (own-code resource-handle scan `0x1A3C80` ->
  `0x1A2250`, handles 0x300C5+, no device traffic, black display); Destroy All
  Humans! renders its own Pandemic logo card and then spins in a kernel stall
  loop (`KeStallExecutionProcessor` `0x800153D0`, 25% of samples).
- **Gates: not one anchor moved.** Accurate menu `4EF1FBF6`/`B6116DAE`
  (`5C0CF968`/`E2B4D327`), accurate triangle `5B50373D`/`79142DE4`
  (`E58AE5D7`/`C43D5B49`), fast triangle sw `457719AC`/`AB39AEE9` 58,123,882 px,
  fast menu sw `FD12563B`/`D726C97A`, ktest `CA5D3867` (40,797 B) byte-identical
  at 792,800,535 steps. The menu's draw counters move 33,638/1,402 ->
  34,333/701 for the same pixels (the 701 format-skipped inline draws now run the
  program, 701 legacy-path draws are now skipped as non-triangle primitives), so
  the frame and the RAM/CPU SHAs are byte-identical. Suite 1993 -> **1996**
  (3 new `Nv2aInlineArrayTests`, all failing pre-fix), build 0 warnings, format
  clean.
- **The xemu oracle could not be captured this session** and that is recorded,
  not papered over: another agent owns the desktop, so PrintWindow on xemu's
  SDL/GL surface answers black, the screen copy catches the editor, `screendump`
  is not a command in this build and F12 (`ActionScreenshot`) needs focus. No
  xemu frame is claimed for TM6.
- **Score:** P11 NV2A 93 -> 94, P24 Retail 40 -> 50 = **78.4%** (92.5 of 118).
- **Next: TM7** - `scripts/retail-game-probe.ps1` + the compatibility rows, with
  Bad Boys II's resource scan and Destroy All Humans!' stall loop first. Resume
  points: `wt-data/tm6/*-10b.xgs`.

**Session 2026-10-25 (fifty-sixth; TM5 found Halo's blocker in our own APU bridge -
the DirectSound mixer block was hard-coded to 007's page, so Halo's submission was
never consumed - and the title now launches and renders its own main menu; gameplay
is a documented wait in a guest D3D resource loop - decisions 256).**

- **The blocker, in one line.** `McpxApuDevice` absorbed the XDK DirectSound mixer
  state word at the **fixed physical address `0x03924810`** (`DspStateWordPhys`) -
  007's block. Halo posts the *same* six-dword descriptor (page + 0x800, state word
  at + 0x810: 2 = submit posted, 3 = parameters loaded) at `0x02034800`, so the
  bridge never saw it: the guest set 3 and spun at `EIP 0x195EC0`
  (`cmp dword [eax+0x10],0; jne`) from ~1.5B steps with no fault and no new frames.
- **The fix.** `TryGetMixerStateWord` scans the guest's own GP scratch (`GPSADDR`)
  SGE table (16 entries) and reads page + 0x810 for each mapped page, absorbing a
  2/3. The DSP can only touch guest RAM through that mapping (xemu
  `gp_scratch_rw`), so the mapping is the authority; the per-title constant is gone.
  From `wt-data/tm5/halo-10b.xgs` the pre-fix build spins forever (2B steps,
  99.99% of samples); the fixed build logs `dsp-absorb 3->0 / 2->0 / 3->0` and
  leaves the spin at once (frames 35 -> 56 in 500M steps). Two of three new tests
  fail pre-fix.
- **Halo is a launched title.** Loading screen at 15B (251 frames), **main menu at
  35B** (`wt-data/tm5/halo-35b-display.png`: HALO / CAMPAIGN / MULTIPLAYER /
  SETTINGS / GAME DEMOS, 97,235 colours, 0% black, 516 frames / 3,247 draws /
  191,496,512 px), and **100B steps with `LastFault vec=0`** (fast engine,
  `--gpu sw`; 314 msps on the wait).
- **The 1B matrix moved: 6 own-render / 38 dashboard / 0 fault / 0 timeout** (was
  2/42). Ninja Gaiden Black 35 -> **6,475** frames (EIP `0x21A97D`), Tak 42 ->
  **1,068** frames / 3,701 draws, 50 Cent 98 frames / **100,987,370** px, Bad Boys
  II **42,303,082** px, Destroy All Humans! off the dashboard menu. The pre-fix
  build reproduces NGB's old row exactly (2,672 / 41,995,882 / 35, EIP
  `0x37DC15`), so the advance is the fix. Caveat: `own-render` is a tuple rule, so
  Destroy All Humans! (34 frames vs 35) counts although it only changed stage.
- **Gameplay is a documented wait.** After the menu the guest busy-waits in a D3D
  resource lookup: `0x1810F0` (from `0x6348B`) reads `[0x192390] = 0x18F800`,
  buckets at `+0x48C`, bucket 0 = `0x80060000`, slot field `-1`, handle **0** on
  every call (`--break-log arg=00000000`) -> `D3DERR_NOTFOUND` (0x88760828)
  retried forever; 99.99% of 280M samples are its 23 EIPs. Last device I/O: IDE
  READ DMA `0xC8` (32,216 accesses), APU reads/writes 14.17M, NV2A 538
  notifications; 165,903 interrupts by 100B, so the guest is waiting, not wedged.
- **xemu is the oracle and the wait is ours:** the same menu at 70 s
  (`wt-data/tm5/xemu-halo-70s.png`) and in-game gameplay at 150 s
  (`wt-data/tm5/xemu-halo-150s.png`). `scripts/xemu-oracle.ps1` now captures
  through `PrintWindow` (client-only) with a screen-copy fallback - a plain screen
  copy had caught the editor behind xemu. **The screenshot policy is not met at
  the menu** (`dmean=(101.8, 100.6, 113.3)`: XGrid's menu background renders far
  brighter), so no frame-policy claim is made.
- **Audio:** silent, measured - `--apu-dsp --audio-out` writes a 44-byte WAV and
  **0 PCM frames** (`gpFrames=0 epFrames=0`). Same TM8 gap as 007, one stage
  earlier.
- **Gates: not one anchor moved** (the gate titles post no mixer block the new
  rule absorbs differently): accurate menu `4EF1FBF6`/`B6116DAE`
  (`5C0CF968`/`E2B4D327`, 33,632/686/686), accurate triangle `5B50373D`/
  `79142DE4` (`E58AE5D7`/`C43D5B49`, 2,707/69/34), ktest `CA5D3867` (40,797 B)
  byte-identical at 792,800,535 steps. Suite 1990 -> **1993**, build 0 warnings,
  format clean. Also: the PIC state line prints `elcr=` now.
- **Next: TM6** - the next 5 titles. The D3D resource loop is the first shared
  candidate (Halo plus Ninja Gaiden Black / 50 Cent / Bad Boys II, all past their
  XBE entry and still dashboard rows at 1B); probe those at 10-40B before choosing.

**Session 2026-10-25 (fifty-fifth; TM4 closed the 007 #PF - the APU delivered the
idle-voice trap twice per voice, the guest freed the voice on the first one and
dereferenced null on the second, and 007 Agent Under Fire now runs 100B steps and
presents its first-level driving scene - decisions 255).**

- **The blocker, in one line.** `McpxApuDevice.VoiceOff` cleared the voice's
  active bit, wrote the notifier **and queued a `SE2FE_IDLE_VOICE` trap** that
  `ProcessFrame` delivered on the next frame; the VP frame walk then delivered the
  same voice again because it was still linked. xemu 0.8.139's `voice_off`
  (`vp.c:118-132`) sends **no** trap - `mcpx_apu_vp_frame` (`vp.c:1833-1835`) is
  the only sender - so the guest got two completions for one voice, freed the
  voice object on the first, and the second dereferenced
  `[this->voices[0x2E]] == null` at `EIP=0x1C386E` (`CR2=0xE4`).
- **The evidence chain** (all from a 26B snapshot, ~5 s per probe): the
  `--break-log 0x1C3BF1`/`0x1C386E` pair shows the caller `0x1C3C22` with arg
  `0x2E` and a live voice object on every visit except the last, where `ECX=0`;
  `--dumpv` shows the guest's `DSND` device voice table (256 entries at
  `this+0x2C4`) fully populated at the first call and **all zero** at the fault;
  the APU state at the fault reads `fedec=00008000/0000002E`
  (`SE2FE_IDLE_VOICE`, voice 46) and the event log shows `voice-off 46` x2, then
  `idle-voice 46` **x2** (the queued pair), then the walk's `idle-voice 47`,
  `idle-voice 46`.
- **The fix.** The pending queue is gone; `CompleteVoiceOff` has no `queueIdle`
  parameter and the frame walk is the only trap sender. APU snapshot **v5** (v2-v4
  still load; the old list is read and dropped, and the DSP payload gate moved to
  `SnapshotVersionV4`). Three new tests fail on the pre-fix source; the old
  `VoiceOff_DeliversIdleVoiceMessageOnNextFrame` pinned the defect and became
  `VoiceOff_UnlinkedVoiceSendsNoIdleVoiceTrap`.
- **007 is playable.** **100,000,000,000 steps, `LastFault vec=0`**, 134,004 draws
  / 1,005,989,482 px / 6,478 frames, EIP `0x000214C2`, wall 1,284.8 s; the display
  is the Z8 on the docks (`wt-data/tm4/007-100b-display.png`, 48,208 colours, 6.8%
  black, mean `(78.7, 65.5, 45.3)`). The 20B no-pad frame is the Harrier intro
  (`wt-data/tm4/007-20b-nopad-display.png`, 38,350 colours) and the 26B+ pad frame
  is gameplay, so the scripted A edge moves the guest a whole stage.
- **The xemu oracle had never been captured.** `scripts/xemu-oracle.ps1` passed
  `-cdrom` (not an xemu option - the disc is `sys.files.dvd_path`, now selected
  with `-config_path`), lost any ISO path containing spaces to unquoted argv
  splitting (`xemu.exe: -: invalid option`), and captured a DPI-virtualized rect
  so every shot was shifted into the window chrome. Fixed all three; xemu 0.8.139
  on the same ISO reaches **the same driving scene at 150 s**
  (`wt-data/tm4/xemu-007-150s.png`), and `frame-compare --crop-top 0.25` reads
  mean `(94.6, 87.6, 63.3)` vs `(76.6, 64.9, 48.7)`, colours 43,921 vs 39,912,
  black 4.1% vs 4.2%, `dmean=(18.0, 22.6, 14.6)`. xemu cannot be driven on this
  host (pad input is SDL-gamepad only), so it sits in the attract/intro loop.
- **Gates: not one anchor moved** (the gate titles never take the voice-off path
  at 1B): accurate menu `4EF1FBF6`/`B6116DAE` (`5C0CF968`/`E2B4D327`,
  33,632/686/686), accurate triangle `5B50373D`/`79142DE4`
  (`E58AE5D7`/`C43D5B49`, 2,707/69/34), ktest `CA5D3867` (40,797 B) byte-identical
  at 792,800,535 steps. Suite 1988 -> **1990**, build 0 warnings, format clean.
- **Also:** `--break-log` now prints EBX/ECX/EDX/EBP and the APU state line prints
  `fedec=<method>/<param>` - the null-pointer hunt needed ECX and the trap decode.
- **Scripted input** (from the 100B snapshot, same state, only the pad differs):
  50M steps gives RAM `68E22299` (none) / `0FFAF650` (stick right) / `946C590A`
  (stick left); at 300M the display is the plane-crash cinematic (13,754 colours)
  with no input or a held D-pad and **black** with a held A (cutscene skipped).
  Across the boot the pad edge is the stage switch: 20B with no pad is the Harrier
  intro (38,350 colours), with the A press it is gameplay.
- **Audio is measured silent:** `--apu-dsp --audio-out` at 28B steps writes
  877,568 bytes / 219,392 frames of PCM, every sample zero (`gpFrames=6914
  gpUndefined=0 gpLimitHits=0 epFrames=863`): the voices are never mixed into the
  EP input (the VP mixer `vp.c` is not modelled; the AC'97 codec has no PCM path).
  Part 6 is a documented silence, and that is TM8's default scope.

**Session 2026-10-24 (fifty-fourth; TM3 found the second shared blocker in the
decoder - SAHF/LAHF were missing, so the retail `fprem; fnstsw ax; sahf; jp`
tail raised #UD in every engine - and three oracle titles now run 1B steps with
no fault, Tak becomes a second own-render row, and no gate anchor moved -
decisions 254).**

- **The blocker, in one line.** Halo's deterministic `#UD` (`vec=6`,
  `EIP=0x00180AEC`, 604,260,882 steps) reproduces with `--jit-raw`, without it
  (IL), with `--jit-exact` **and in accurate mode** - so it was never a JIT bug.
  `--break`/`--break-log` show one visit to `0x180AEC`; `--dumpv` shows the byte
  is **`0x9E` = SAHF**, the tail of the x87 loop `fprem; fnstsw ax; sahf; jp`
  (`0x180AE6` FPREM -> `0x180AE9` FNSTSW AX -> `0x180AEC` SAHF -> `0x180AED`
  `jp 0x180AE6`). The decoder had no case for `0x9E`/`0x9F`, so the guest got a
  `#UD`, the kernel's dispatcher (`EAX=0x1E`, `ECX=0xC000001D`) halted at
  `0x800151EF` - the "shared last EIP" of the three matrix rows.
- **The fix** (xemu 0.8.139 `target/i386/tcg/emit.c.inc` `gen_SAHF`/`gen_LAHF`):
  `Decoder` `0x9E`->`Sahf`, `0x9F`->`Lahf`; `XboxCpu` implements
  `Eflags = (Eflags & ~ConditionMask) | (AH & ConditionMask)` with
  `ConditionMask = CF|PF|AF|ZF|SF` (`0xD5` - OF and every non-condition bit
  stay) and `AH = (Eflags & ConditionMask) | Reserved1`; `SpecSahf`/`SpecLahf`
  keep the hot FPREM loop JIT-served; `DecodedBlockCache.Terminates` continues
  through both.
- **Evidence.** 15 new tests (`FlagTransferTests`, `JitTests.SahfLahfLoop`,
  `SpecExecutionTests` shapes) - the pre-fix build throws
  `Unsupported instruction 0x9E`; the independent QEMU-TCG oracle gained 12
  cases (corpus 629 -> **641**, all green, a mask mutation fails it); the three
  titles run **1B steps, `LastFault vec=0`** (Halo `0xAC3AC` fast / `0xAC543`
  accurate, Tak `0x39CE8B`, NGB `0x37DC15`).
- **The matrix re-run (44 rows, 1B): 2 own-render, 42 dashboard, 0 fault,
  0 timeout** (was 2/39/3/0). **Tak is the second own-render row** (unique
  2,675 draws / 41,995,882 px / 42 frames - seven more frames than the boot
  animation), Halo/NGB execute their own XBE code.
- **Gates: not one anchor moved** (the gate titles execute neither opcode) -
  four accurate 1B goldens byte-identical (`4EF1FBF6`/`B6116DAE`,
  `5B50373D`/`79142DE4`, same RAM/CPU and counters), four fast anchors exact,
  ktest `CA5D3867` (40,797 B) byte-identical at 792,800,535 steps. Suite
  1973 -> **1988**, build 0 warnings, format clean, no snapshot change.
- **Filed:** the remaining one-byte decoder holes (`27 2F 37 3F` DAA/DAS/AAA/AAS,
  `62` BOUND, `82` the 32-bit group-1 alias, `C4 C5` LES/LDS, `D6` SALC,
  `F1` INT1) - the reference accepts them all in 32-bit mode; no measured row
  executes one. **Next: TM4** - 007's `#PF` at 26.5B (`EIP 0x1C386E`,
  CR2=`0xE4`) inside the APU ISR is untouched by this fix and is the title's
  blocker.

**Session 2026-10-24 (fifty-third; TM1 named the blocker - the guest programs the
8259 edge/level register and XGrid discarded the writes - and TM2 landed the
level-line model: 007 now reaches its own title screen, the ktest log stays
byte-identical, and the accurate anchors are re-baselined by one triangle frame -
decisions 253).**

- **TM1 (probe, its own commit).** The top 3 are 007, Halo and the 36-row Redump
  group. 007 stalls at 30B/50B in a guest spin at EIP `0x1C4787` (79,581 draws /
  603,250,282 px / 2,594 frames, black display, APU ISTS `0x79` asserted while
  PIC `req=0x00`); the Redump rows park at `0x800422B0` with the NV2A line
  asserted and no PIC request; Halo's fast-mode `#UD` at 604M is a JIT item
  (accurate mode clean). xemu 0.8.139 reaches 007 gameplay in 15 s and Halo's
  beach gameplay in 50 s (`wt-data/xemu-007-test.png`,
  `wt-data/xemu-halo-test.png`). Report: `docs/research-notes/real-game-probes.md`.
- **The blocker, in one line.** A temporary unmapped-port trace showed the guest
  writing the **ELCR**: `4D0<=68` (IRQ3 NV2A / IRQ5 APU / IRQ6 ACI level) and
  `4D1<=18` (IRQ11 USB0 / IRQ12 USB1 level). XGrid had no ELCR and the PIC was
  edge-only, so a held PCI line was lost after its first acknowledge.
- **TM2 landed** (`src/XGrid.Bus/Interrupts/Pic8259.cs`): the ELCR ports
  0x4D0/0x4D1 (masks 0xF8/0xDE), the xemu 0.8.139 `hw/intc/i8259.c` level
  semantics (an acknowledge does not clear a level line's request, so the EOI
  re-presents it), the slave INT re-driven into IR2 on every state change, and
  snapshot v2 (v1 states still load, as edge-only). `Pic8259LevelTests` (8 tests,
  3 fail with the pre-fix `IsLevel`), new `XGRID_PIC_TRACE=1` diagnostic. Suite
  1965 -> **1973**.
- **007 advanced two stages.** At 50B with the dashboard pad script it presents
  **its own 007 Agent Under Fire title screen** (`wt-data/007-50b-fixed-display.png`:
  21,186 colours, 49.7% black, EIP `0x1FD0E` in the XBE `.text`, 1,914 flips, no
  fault) where TM1 saw a frozen black transition; with the 20B A press it reaches
  the game's own render (79,581 draws) and then faults at **26.5B** at EIP
  `0x1C386E` (`#PF`, CR2=`0xE4`, err=0) inside the APU ISR - the TM3 blocker.
  Interrupts delivered +5 (menu) / +10 (triangle), no storm.
- **Gates.** ktest **byte-identical** (`CA5D3867`, 40,797 B, 792,800,535 steps,
  7/7). The accurate goldens re-baselined with the measured reason: the menu
  frame and its 33,632/686/686 counters are **unchanged** (`4EF1FBF6` /
  `B6116DAE`, RAM `5C0CF968`/`E2B4D327`), the triangle moves `A1740DE0` ->
  **`5B50373D`** (2,706/68/34 -> 2,707/69/34) and gl `7A860ED7` ->
  **`79142DE4`** (RAM `E58AE5D7`/`C43D5B49`); the pre-fix build reproduces the
  old values exactly. Fast anchors re-recorded (triangle gl
  `6CBBFE75`/`AB39AEE9` frame `79142DE4`, menu gl `EF1BF776`/`D726C97A` frame
  `B6116DAE`, triangle sw `457719AC`/`AB39AEE9` 58,123,882 px frame `5B50373D`,
  menu sw `FD12563B`/`D726C97A` 253,998,498 px frame `4EF1FBF6`). Build 0
  warnings, format clean.
- **Media change:** `H:\Tests\XBOXReady` now carries **7** optimized ISOs (the
  operator added 50 Cent, Bad Boys II, Destroy All Humans!, Tak and Ninja Gaiden
  Black), so the matrix re-run is **44 rows**, not 39. The matrix result:
  **2 own-render, 39 dashboard, 3 fault, 0 timeout**. The Redump group advanced
  too - GTA: San Andreas at 5B now presents the disc's own Xbox boot screen
  (`wt-data/gta-5b-fixed-display.png`, 27,543 colours, 0.1% black, EIP `0x2DC4F`,
  17,410 flips, no fault) where the pre-fix 5B sweep showed the dashboard. The
  three faults are one fast-mode `#UD` on **3 of the 7 oracle titles**
  (Halo `0x180AEC`, Tak `0x33C810`, Ninja Gaiden Black `0x30D63D`) - the TM3
  lead candidate by the matrix's ">=3 titles" rule.
- **Score:** P5 Bus & IRQ 85 -> 90, P24 Retail 0 -> 10 = **74.3%** (87.64 of 118).

**Session 2026-10-24 (fifty-second; G3 ran the consolidated re-baseline - every
gate, all 22 corpus rows, the ktest smoke and the package gate verified in one
commit, and six stale anchors replaced by measurement - decisions §252).**

- **The milestone's single re-baseline, not a code change.** One full solution
  Release build (0 warnings), one test run (**1965** green), then every leg from
  those binaries (no mixed engine). The four fast 1B anchors are byte-identical:
  triangle gl `4F1FCBC0`/`7DAB63EB` (6.311 s), menu gl `B06BA6E5`/`F5D61C94`
  (14.706 s), triangle sw `B45254CA`/`7DAB63EB` (57,663,082 px, 10.808 s), menu
  sw `34973510`/`F5D61C94` (253,998,498 px, 25.630 s).
- **All four accurate goldens PASS**: menu sw `4EF1FBF6` / gl `B6116DAE`
  (`diff=2602 violations=0 outsideBand=0 maxDelta=1`, counters 33,632/686/686),
  triangle sw `A1740DE0` / gl `7A860ED7` (`diff=27849`, 2,706/68/34); accurate
  RAM/CPU also exact (menu `6AB39AB0`/`3D2740B8`, triangle
  `812EA159`/`2F632483`). `BackendUnsupported=0 composedFrames=0` everywhere.
- **All 22 corpus rows PASS at their documented anchors.** The exceptions are
  six stale values, each re-baselined with its measurement: the
  `xgu bump_cubemap_reflect` 3B GL frame `5174B1EB` -> **`673477F7`** (now
  **1 px** of 307,200 from the software frame; the old value was the pre-§236
  two-colour image), its 4.4B colour count/mean 18,980 -> **19,042 /
  `(57.1, 89.0, 34.4)`** (pair 2 px maxDelta 3), its 3.6B pair **1 px maxDelta
  4** (21,041 colours both), its 3B software colour count **13,925** (21,041 is
  the 3.6B anchor), `xgu alpha_blending` 4.6B mean **`(22.7, 18.7, 230.4)`**
  (within 2.8/255 of `(20.8, 17.2, 233.2)`; 3B draws 2,952 at the same
  RAM/CPU), the dashboard 2B throughput **16.94 msps** (same 217,622 draws /
  `535C48FA`) and the ktest BTFF RAM rows (`F708B07D`/`7D667575`).
- **ktest smoke**: park `4E03A` at **792,800,535** steps, the 40,797-byte suite
  log `CA5D3867` byte-identical, `ktest_diff.py` **SMOKE PASS 7/7** (071/319/344
  fail on both, 224/231 XGrid-ahead). Driven with the script's own command on
  the prepared media - the sibling `CSharp_XISOSharp` and `xemu.exe` are absent
  here, so the diff reads the stored §120 oracle log.
- **Package gate**: `scripts/package.ps1` + `scripts/package-verify.ps1` ->
  **PACKAGE PASS: 8 archives, version 0.1.0, 4 RIDs, licenses complete** (19
  ported files; win-x64 CLI smoke `XGrid 0.1.0`).
- **Close**: G3 **done**; the E5c port milestone is complete (G1 done, G2
  negative, G3 done) and **the queue is empty** (`-Next` prints SEQUENCE
  COMPLETE). No code and no snapshot change (Nv2a v10, audio v3); phase score
  unchanged at 79.9% (86.34 of 108) - verification, not landed capability.
  Docs: decisions §252, `docs/compatibility.md` refreshed (status line, six
  rows, the measured-rows table), `ImplementationPlan.md` E5c/G3 checkboxes,
  this note and `next-session.md`.

**Session 2026-10-24 (fifty-first; G2 priced by site - the GL renderer port's
removable share is the CPU vertex stage, 4.0-4.2% of the triangle wall, and the
reference design's per-draw submission costs 3x the menu's gross saving - the
port closes negative - decisions §251).**

- **The price, measured not assumed.** The plan's G2 figure ("menu device 6.57 s
  pre-D5, triangle 0.62 s") is the *whole* device stage, which no port can
  remove. Temporary probes (stripped before the commit, sampled 1-in-16 flushes)
  split it on the post-D5 build at 1B `gl+replace`, H1: **triangle wall
  5.786/5.866 s, device 0.576/0.594 s (9.9-10.1%)**, of which flush 0.589/0.628
  = fetch 0.031 + **transform 0.225/0.238** + epilogue 0.008 + fill 0.036
  (backend 0.029) + **other 0.289/0.310** (assembly/setup/batch), clears 0.012.
  **Menu wall 14.817/14.621 s, device 5.261/5.202 s (35.5%)**, of which flush
  5.417/5.410 = fetch 0.650 + **transform 2.317/2.332** + epilogue 0.311 + fill
  1.073 (backend 0.770) + **other 1.066/1.050**, clears 0.043. Every anchor
  exact on every probed run.
- **The removable ceiling.** A GPU vertex program (xemu `glsl/vsh-prog.c`)
  removes transform + epilogue only: **triangle 0.233-0.246 s = 4.0-4.2% wall /
  5.0-5.3% engine; menu 2.63-2.64 s = 17.7-18.1% wall / 20.3% engine**. The
  triangle - the leg the charter's "both titles" rule binds - needs 0.58 s
  (2.4x) to reach 10%, and the whole device stage is only 10.0% of its wall.
- **The submission is the killer.** xemu's GL path submits per draw command from
  CPU-built vertex buffers and has no instancing; XGrid's backend submits one
  instanced draw per command (§205). The repo's own A/B for giving that up,
  `XGRID_GL_NO_BATCH=1` on the same build: **menu 14.817 -> 22.660 s (+7.843 s,
  +52.9%)**, triangle 5.786 -> 6.242 s (+0.456 s, +7.9%), `drawMs` 8,623 ms,
  anchors exact. Per-triangle rather than per-draw-command, so it overstates -
  but it is the only measured shape and it is 3x the menu's gross saving.
- **Compatibility does not rescue it:** the corpus's GL rows already match
  software (cubemap 2 px, `vp_lighting`, the display-source rows,
  `composedFrames=0`), gap 10 is a guest-state answer in both renderers, and
  G1's audit already dispositioned the 73 recorded methods. A port would add
  exact surface/texture-cache semantics that no row exercises.
- **Close:** G2 **negative** on the stage gate. Reopen only with (a) a bounded
  GPU vertex-program slice that keeps the instanced per-draw-command submission
  and removes >=10% of the *triangle* wall (>=0.58 s) with the anchors and both
  accurate goldens exact, or (b) an operator re-scope of G2 as a compatibility
  task (acceptance = the 22-row matrix, not the 10% wall bar), G3 as the
  vehicle. **Do not re-open on the "6.57 s menu device" figure.**
- **Gates (tree byte-identical to `da1bf43`):** fast 1B `gl+replace` triangle
  `4F1FCBC0`/`7DAB63EB` (5.948 s), menu `B06BA6E5`/`F5D61C94` (14.727 s); fast
  1B `sw` triangle `B45254CA`/`7DAB63EB` 57,663,082 px, menu
  `34973510`/`F5D61C94` 253,998,498 px; ktest XGrid leg park `4E03A` after
  **792,800,535** steps, 40,797 B log `CA5D3867…` byte-identical (the smoke
  *script* needs the sibling `CSharp_XISOSharp` and an `xemu.exe` absent here, so
  the leg ran with the script's own command on the prepared media); suite
  **1965** green, build 0 warnings, format clean. No code/snapshot change.

**Session 2026-10-24 (forty-ninth; D7 priced the boot cell by cell - the unbuilt
heads are 99.6% RAM code and building them is 15x negative, so the boot item
closes negative with the largest unretried lever at 2.5% - decisions §249).**

- **The plan's box, measured.** The unbuilt-head population is now a permanent
  `--jit-timing` line: `JitBlockCache` records each head's fetch source at first
  sight, `FormatJitAttribution` prints `pending heads=… bySource[ram/image/
  other/unknown] visits[…] formMsPerBlock=… buildAllMs=…`, and
  `JitAttributionTests` pins it. On the gate titles: **triangle 100M 34,197
  heads (34,046 RAM / 151 image) / 1B 55,005 (54,854 / 151); menu 31,720 /
  31,257**. The image share is the MCPX ROM, whose blocks already form, so the
  "prebuilt/plan cache for immutable image code" is worth 0.9-1.4%.
- **Building them all is negative by construction:** 19.4 s triangle / 5.1 s
  menu at the measured 0.16-0.39 ms per block, against non-JIT cells of 1.26 s /
  1.71 s (15x / 3x). The heads are 1-3-visit cold code.
- **The boot split (0-100M):** 1.87-1.91 s triangle (31% of a 6.1 s wall) =
  runner 41-45% + formation 25% (Iced `Assemble` 18-21%) + non-JIT 26% +
  lookup/loop 4%; menu 1.62 s. The non-JIT cell splits 34% `Step` (3.2 us each,
  1.1 us of it the byte-at-a-time 15-byte fetch) / 66% spec body (270-358
  ns/member: the interpreter handler plus an 88-106 ns cursor loop). `Form` is
  0.14 us a call, not the cost.
- **Ceiling:** Step fast fetch 2.5% of the wall, spec loop 1.8%, image plan
  cache 0.9-1.4% -> **5.7% of new levers**; the encoder (N4) is 9.4% and below
  its own >10% reopen bar, trace (N3) 3-5%, the runner (D8) uniform. The plan's
  combined shape is 10.8% on paper and 87% N4, so D7 closes **negative** and
  hands the target to D8 option (e) / G1-G3. The three sized candidates are in
  §249 for a future engine task.
- **Gates:** all four fast 1B anchors byte-identical (triangle gl
  `4F1FCBC0`/`7DAB63EB`, menu gl `B06BA6E5`/`F5D61C94`, triangle sw
  `B45254CA`/`7DAB63EB` 57,663,082 px, menu sw `34973510`/`F5D61C94`
  253,998,498 px); ktest XGrid leg parks at `4E03A` after **792,800,535**
  steps with the **40,797 B `CA5D3867` log byte-identical** and `ktest_diff.py`
  reads **SMOKE PASS 7/7** against the stored xemu oracle log (the smoke script
  still needs the absent sibling `CSharp_XISOSharp` to rebuild the ISO, so the
  leg ran with the script's own command on the prepared media). Suite 1922 ->
  **1925**, build 0 warnings, format clean. No snapshot change.

**Session 2026-10-24 (forty-eighth; D6 priced by building it - the compiled
transform evaluator is exact but 3.2x slower than the interpreter, so the
device-remainder item closes negative - decisions §248).**

- **Why build it.** §243 priced "a compiled transform evaluator" at 1.5-1.7 s of
  capture = **9.5-10.7% menu wall**, flagged high risk, and §244 called it
  at-or-below the bar on its own. A price that straddles the bar cannot be
  settled by another estimate, so D6's first checkbox (the price measurement)
  was answered by building the evaluator.
- **The stage, measured first (pre-change build, sampled 1/64).** Menu 1B
  `gl+replace`: **11,439,040 vertices / 138,269,607 VS instructions (12.1 each)
  at 325 ns/vertex = 3.72 s = 19.3% of a 19.24 s wall**. Triangle: 63,524
  vertices at 3,609 ns (27.7 instructions, transcendental-heavy) = 229 ms = 3.2%.
  So this is a menu item and its whole ceiling is 19.3% of the wall.
- **What was built (reverted).** One `DynamicMethod` per plan, emitted from the
  plan's **own decoded instruction list** (never a second decoder), with flat
  four-component files for constants/inputs/outputs; the reference's zero rule
  (`a == 0 || b == 0 -> +0.0f` as compute-then-overwrite, so `0 * inf` cannot
  leak a NaN), the `0.0f`-seeded left-to-right `dp3`/`dp4`/`dph` accumulation,
  `dst`, `min`/`max`/`slt`/`sge`/`arl` through the interpreter's own helpers,
  the ILU through one shared `ComputeIluAt` body, constant-folded masked stores
  with the bit-reversed hardware mask order, the oFog destination-prefix rule,
  the ORB/oA0 paths and the paired-register-1 rule. `Execute(float[][])` stays
  the public contract as a pack/unpack adapter and `ExecuteFlat` is the device
  entry; the interpreter stays the fallback.
- **Exactness was never the problem.** `VshProgramPlanTests` compared three
  executors per case (`VshProgram`, the kernel, the interpreter) and 10/10
  passed, including the 400-program fuzz, the 256-swizzle sweep, ARL-relative
  constants with the address output, every oFog mask and the paired r1 rule.
  All three emitter bugs were caught by that differential (swapped
  `ComputeIluAt` arguments, a missing swizzle on relative constants, ARL
  emitted for the undefined MAC ops 14/15).
- **The measurement.** **1,028 ns per vertex** (11,762 ms / 11,439,040 calls,
  unconditional) against the interpreter's **330 ns** - **3.2x slower**. Menu 1B
  `gl+replace` **19.237 -> 31.242 s (+62.4%)** and 19.585 -> 29.548/30.938 s in
  same-window pairs; device writes 7.39 -> 18.60 s, `runnerMs` 15,395 ->
  25,287, GL `drawMs` 999 -> 941 ms (unchanged); triangle 7.084 -> 6.778 s (no
  regression). Every anchor stayed exact throughout (menu `B06BA6E5` /
  `F5D61C94`, triangle `4F1FCBC0` / `7DAB63EB`, counters
  33,638/1,402/696/686/686, culled 41,035, GL 2,733,736 tris,
  `BackendUnsupported=0 composedFrames=0`).
- **Cause.** The per-instruction unrolled emission costs ~150-250 host
  instructions per VS instruction (bounds-checked flat-array loads/stores plus
  the per-component zero-rule branches) = **7-15 KB of straight-line code per
  plan**, so the body cannot stay uop-cache resident: IPC ~0.6 against the
  interpreter's ~2.5. The interpreter wins *because* its code is small - the
  mirror image of §239's "latency-bound, not instruction-bound".
- **Ceiling and reopen.** The bar needs an evaluator **>=2.4x the interpreter
  (<=135 ns/vertex)**; this design is 3.2x slower, so its ceiling is <=0% and
  §243's 9.5-10.7% is repriced to a negative. Reopen only with a *different*
  emission priced by building it: a 4-component `Vector128` emission (~12 host
  instructions per VS instruction) with the dots and the zero rule scalar,
  because the reference's accumulation order is not associative.
- **Gates.** Tree reverted to HEAD byte-identical; re-verified in this window -
  menu 1B `gl+replace` `B06BA6E5`/`F5D61C94` (16.316 s), triangle
  `4F1FCBC0`/`7DAB63EB` (6.186 s), no fault; build 0 warnings, suite **1922**
  green, format clean. No snapshot change.

**Session 2026-10-23 (forty-sixth; the GP/EP frame glue runs the guest's real
DSP microcode and the EP output FIFO feeds a host PCM sink - P1.3 and P1.4 land,
every gate anchor byte-identical).**

- **P1.3 landed (`McpxApuDsp`/`McpxApuDspPair`, `ApuDspCore` option).** The
  `gp_ep.c` glue is ported: the GP/EP 64 KB sub-windows (X/mixbuf/Y/P + the
  reset register), `proc_rst_write` (reset on a cleared run bit, bootstrap on the
  rising edge of both), `scatter_gather_rw`/`circular_scatter_gather_rw` over the
  guest SGE tables, the FIFO base/end/cur rings (global registers 0x3024/0x4024),
  the peripheral space (0xFFFFC4 halt, 0xFFFFC5 interrupt word, 0xFFFFD4-D7 DMA)
  and the frame loop (START_FRAME, halt clear, `run(1000)` until halt, a 1M-cycle
  safety ceiling). `ProcessDspFrame` passes the pre-increment `ep_frame_div` so
  the EP runs on frames 0, 8, 16... like `se_frame`. APU snapshot **v4**
  appends a length-prefixed DSP payload; v2/v3 still load, so no `.xgs` is
  invalidated.
- **P1.4 landed (`IAudioBackend`, `WaveAudioBackend`, CLI `--audio-out`).** The
  EP output FIFO 0 is submitted to the host backend and silence is written to
  the guest ring (`ep_sink_samples`); the WAVE backend records 48 kHz/16-bit
  stereo PCM with a patched RIFF header. `--audio-out` implies `--apu-dsp`.
- **The real microcode found a real bug.** The first `--apu-dsp` boot ran 29 GP
  frames with **274,495 undefined instructions** and 28 cycle-limit hits: the
  first un-decoded word was `nop` at PC 0x3FA. `InvalidateOpcodeCache` (every
  bootstrap/P write) cleared the tag cache to zeros, and 0 is `nop`, so the
  first fetch looked like a cache hit on a null entry and the PC never advanced.
  Fixed by re-filling the 0xFFFFFFFF sentinel; `InvalidateOpcodeCache_KeepsThe
  NopSentinel` fails pre-fix. After the fix: **0 undefined, 0 ceiling hits, PC
  back at 0** on every frame.
- **Evidence.** The opt-in path is guest-identical on both gate titles at 1B:
  menu `gl+replace` `B06BA6E5`/`F5D61C94` (29 GP frames), triangle
  `4F1FCBC0`/`7DAB63EB` (26 GP frames), same NV2A counters, no fault. Default
  path unchanged: fast 1B triangle gl `4F1FCBC0`/`7DAB63EB`, menu gl
  `B06BA6E5`/`F5D61C94`, triangle sw `B45254CA`/`7DAB63EB` 57,663,082 px, menu
  sw `34973510`/`F5D61C94` 253,998,498 px; accurate goldens `A1740DE0`/
  `3FC4B419` (2706/68/34) and `4EF1FBF6`/`6BE6325F` (33632/686/686) PASS,
  violations 0; ktest log `CA5D3867` (40,797 B) byte-identical at 792,800,535
  steps, SMOKE PASS 7/7. Suite 1893 -> **1922**, build 0 warnings, format clean.
  Phase 12 (APU) 55% -> 75%; score 78.9% (85.23 of 108).
- **Filed:** the VP mixer (`vp.c`) is what would make the gates audible (the
  dashboard starts the GP for 29 boot frames and never the EP, so `--audio-out`
  writes an empty-but-valid WAV on the gate media); the sink path itself is
  proven by a 16-sample stereo voice through the real EP FIFO/DMA ring. The
  AC97 codec path is not modeled.

**Session 2026-10-23 (forty-fifth; the DSP56300 interpreter is ported - the
decoder, the parallel moves and all 388 handler bodies - and it is
differentially exact against the xemu 0.8.139 reference on 929 vectors; P1.2
lands, every gate anchor unchanged).**

- **The oracle came first (`tools/dsp-oracle`).** The reference interpreter is
  self-contained, so the session compiled it standalone (host gcc, `References2/`
  read-only, six out-of-range guards added to a build copy) and used it as the
  differential: `gen_vectors.py` synthesizes three words per decode-table entry,
  two per defined ALU opcode, 240 random eight-word programs and 24
  interrupt-pipeline cases (1086 cases, 929 kept - the rest leave the
  reference's addressable memory), and the 121 KB golden file
  `tests/XGrid.Audio.Tests/TestData/dsp-vectors.txt` replays them through the
  port.  `build.ps1 -Regenerate` rebuilds both.
- **The port.** `Dsp56300Core` is partial now: `Execution` (fetch/decode, PC,
  DO/REP, stack, interrupt pipeline, effective addresses, condition codes),
  `Alu` (add/sub/cmp/cmpm/and/or/eor/tfr/adc/sbc, shifts/rotates,
  `mpy`/`mpyr`/`mac`/`macr`, the 24-bit read/limit), `Instructions` (104
  non-parallel handlers) and `ParallelMove` (`pm_0`..`pm_8`), plus
  `Dsp56300Opcodes` (187-row decode table + 256-entry ALU dispatch +
  `MatchMMMRRR`).  `Step`/`Run` execute; the APU bridge is untouched until P1.3.
- **Two reference quirks the vectors forced, both documented in the port:**
  `emu_pm_1`'s `registers[numreg1] = save_1` sits *outside* its `else`, so a
  parallel move also writes the virtual A/B register; and
  `emu_macr_p_x1_y0_b` rounds before loading the accumulator, so it behaves as a
  plain `mac`.  The vectors also caught an XGrid bug: the 256-entry decode cache
  used `0` as "empty" and `0` is `nop`, so every zero word decoded as undefined.
- **Evidence.** `XGrid.Audio.Tests` 42 -> **51** (9 differential facts), suite
  **1884 -> 1893**, build 0 warnings, format clean.  No gate moved (the core is
  not on the machine path): 1B `gl+replace` fast anchors reproduced exactly -
  triangle `4F1FCBC0`/`7DAB63EB`, menu `B06BA6E5`/`F5D61C94`,
  `BackendUnsupported=0`.  ktest was not re-run for the same reason (the
  CPU/device path is untouched; the sibling XISOSharp repo is also absent on
  this host).  The BTFF triangle RAM SHA (`8AE82781`) is the `--frame*` leg, not
  the plain `4F1FCBC0` one.
- **Next.** P1.3: wire the core into `McpxApuDevice` through the `gp_ep.c` frame
  glue (`proc_rst_write`, scratch scatter/gather, FIFO windows, the GP/EP frame
  loop, DMA) behind an `ApuDspCore` option; flip `ExceptionDebugging` off there
  so a guest `ill` raises the interrupt instead of throwing.  Then P1.4 (PCM
  sink).  Decisions §245.

**Session 2026-10-23 (forty-fourth; XGrid relicensed to GPL-2.0, xemu 0.8.139
adopted as the primary port source, and the APU DSP state/DMA foundation
lands).** Operator decision: the project may port xemu/QEMU code, but stays
**full managed C#**; the CPU TCG is explicitly not a target.

- **License and policy.** `COPYING` is the GPL-2.0 text; `LICENSE` is the
  project notice; `THIRD_PARTY_NOTICES.md` records every reference and ported
  module; `docs/porting-policy.md` is the binding workflow. `README.md`,
  `sources.md`, `AGENTS.md` and `development-direction.md` now say GPL-2.0 and
  allow porting with notices. The old "license stays non-GPL" and
  "facts only / never copy" rules are superseded.
- **xemu 0.8.139 is primary.** `References2/xemu-0.8.139` (commit `aa54c34`,
  2026-10-21) is vendored; the APU/DSP subtree is byte-identical to 0.8.137, and
  the NV2A delta is catalogued in `docs/research-notes/xemu-0.8.139.md` (37
  `hw/xbox` files, concentrated in `nv2a/`, plus new `debug.c`/`debug_gl.h`/
  `null/renderer.h`).
- **xemu menu oracle is real.** xemu 0.8.139 boots the XBMC dashboard from
  `data/xemu` with `Complex_4627.bin`, `mcpx_1.0.bin`, `eeprom.bin` and
  `xbox_hdd.qcow2`; the screenshot is `data/xemu/xemu-0.8.139-oracle.png`.
  `docs/research-notes/xemu-menu-oracle.md` records the config and the
  correction: the old "xemu deadlocks at the animation wait" note was specific
  to the 0.8.136/0.8.137 media, not xemu generally. `scripts/xemu-oracle.ps1`
  reproduces the capture.
- **DSP port slice 1.** `src/XGrid.Audio/Dsp/` now has `Dsp56300State`
  (registers/memory/interrupt state), `Dsp56300Core` (reset, X/Y/P memory map,
  bootstrap, interrupts), `Dsp56300Arithmetic` (56-bit add/sub/abs/shift/mul/
  round/sign-extend) and `DspDma` (the xemu 0.8.139 DMA list walker). The
  instruction interpreter (`dsp_cpu.c` + `dsp_emu.c.inc`) and the GP/EP frame
  glue are the next P1 slices; the existing APU bridge stays the default until
  the ported path passes.
- **Evidence.** `XGrid.Audio` build 0 warnings; `XGrid.Audio.Tests` **42**
  green (6 new `Dsp56300PortTests`: reset defaults, memory map/mix-buffer alias,
  bootstrap high-byte strip, interrupt pending/counter, arithmetic, DMA FIFO
  write). No gate anchor moved: the ported types are not on the machine path.
  `docs/status.json` records `P1.0` done and keeps P1 active.
- **Next.** Port the decoder/handlers (`P1.2`), then `gp_ep.c` (`P1.3`), then
  the PCM sink (`P1.4`). Do not start a QEMU TCG port: the raw emitter is
  already bit-exact and faster than xemu on the CPU-bound ktest.

**Session 2026-10-23 (forty-third; the combined draw-flush slice lands at
-10.33/-17.20/-10.29% menu wall - decisions §244). Track D/C, one slice landed,
every gate anchor byte-identical.**

- **What landed (D5).** §243 had priced its fill-path hoist at -8.0% (below the
  10% bar) and noted that the same slice plus the per-draw `PgraphDrawInfo`
  cache prices at ~-10.5%; that combined slice is D5's first step and it is now
  in: `Nv2AMmio.RejectTriangle` (degenerate/cull/clip-window predicates before
  the fill's setup, same counters), per-draw vertex scratch (`_vsConstants`,
  `_vsInputs`/`_vsOutputs`, `_vsTransformed` - 224 arrays per flush removed),
  `VshProgramPlan.ReadInputMask` (the fetch loop skips slots the program never
  muxes; the fixed-function stage keeps all 16 and the fog fallback's v0/v4/v5
  are folded in), `VshProgramPlan.Execute` operand locals (a 16-byte
  `readonly struct Operand` instead of `_a`/`_b`/`_c`, arithmetic transcribed
  literally and still pinned bit-for-bit by `VshProgramPlanTests`), and
  `BuildCachedDrawInfo` + `PgraphDrawInfo.WithTriangle` (fragment state, four
  texture slots, eye and bump state built once per draw command, stamped by
  `_drawCommandId`; `BumpState` alone was three arrays per triangle).
- **Measurement (1B menu `gl+replace`, interleaved pairs against the stashed
  `520c9d2` CLI).** Acceptance triple **-10.33 / -17.20 / -10.29%** (median
  -10.33%, 3/3 above the bar), `outerMs` -10.6 / -17.0 / -11.4%; three more
  pairs -11.21 / -8.52 / -10.20% (six-pair median -10.31%, 6/6 negative). The
  host was loaded (menu walls 24-31 s vs the recorded 15.6-17.5 band), so only
  paired ratios are read. Triangle `gl+replace` -0.70 / -2.27 / -2.02% (no
  regression, no claim - it is submission/pacing-bound, §241).
- **Gates.** Fast 1B anchors byte-identical on all four legs (menu gl
  `B06BA6E5`/`F5D61C94`, triangle gl `4F1FCBC0`/`7DAB63EB`, menu sw
  `34973510`/`F5D61C94` 253,998,498 px, triangle sw `B45254CA`/`7DAB63EB`
  57,663,082 px; menu counters 33,638/1,402/696/686/686, culled 41,035). All
  four accurate 1B goldens are byte-identical to the stashed base build (menu
  sw `4EF1FBF6`, menu gl `6BE6325F`, triangle sw `A1740DE0`, triangle gl
  `3FC4B419`) with identical counters, and the golden policy passes
  (`diff=1852 violations=0 maxDelta=1`). ktest: 4E03A park at **792,800,535**
  steps, suite log `CA5D3867` (40,797 B) byte-identical, `ktest_diff.py` SMOKE
  PASS 7/7 (the smoke *script* needs the sibling `CSharp_XISOSharp` tool, which
  this worker may not read, so the last run's ISO was booted with the script's
  own XGrid command). Suite 1874 -> **1878**, build 0 warnings, format clean.
  No snapshot change (host-side only; Nv2a v9, audio v3).
- **Next.** The menu's transform plan (2.2-2.5 s, 11.4M vertices at 12.1 VS
  instructions) is the only draw-flush item left; a compiled evaluator was
  priced at 9.5-10.7% menu wall and "high risk" (§243) and is not owned. The
  queue moves to **P1** (host audio).

**Session 2026-10-23 (forty-second; the menu-only wall is decomposed with stage
timers for the first time - 57% of it is the device stage - and the top item is
priced by building it: the draw-flush hoist measures **-8.03 / -9.90 / -7.87%**
(3/3) and is **reverted** as below the 10% bar - decisions §243). Track D/C, no
code landed, one measured negative (`D3`).**

- **The decomposition (D3.1).** Temporary host-only `Stopwatch` probes (removed
  before the commit, the §240 pattern) on both 1B `gl+replace` legs. Menu wall
  **15.0-15.2 s this window** (recorded band 15.6-17.5) = **device 6.57 s
  (41.6%)** + engine non-device 9.4 s (59%); software raster **0** (replace
  mode), GL readback **0**, boot 1.6-1.9 s (10-12%) shared with the triangle.
  The menu's device writes: **flushes 6.15 s** (35,040), other register writes
  0.42 s, clears 0.07 s. Inside the flush: **transform plan 2.2-2.5 s**
  (11.4M unique vertices), **attribute fetch 0.9-1.2 s** (16 slots/vertex),
  epilogue ~0.3 s, per-draw setup 0.26 s, **fill path 2.7-3.1 s** - 10,727,820
  `FillTrianglePsh` calls for 2,733,736 drawn triangles, so **8.0M of 10.7M
  calls produce no pixels**. Triangle for contrast: wall 5.67 s, device 619 ms
  (10.9%), flush 570 ms, transform 204, fill 373, fetch 38.
- **The menu-only mass is device-first** (+9.3 s = 57% device / 43% engine),
  the opposite of §242's arithmetic (it assumed the device stage was >=2.6 s;
  it is 6.57 s). The engine's non-device runner (9.4 s) stays unpriced: every
  transformation in §242's table is measured below the bar (N1/N5/N8/N9).
- **D3.3: the fill-path item was priced by building it**, four exact parts
  (reject-before-setup in `Triangle`, per-draw register-file/map reuse, a
  `VshProgramPlan.ReadInputMask` read-slot fetch mask, and `Execute` loading
  its operands into a 16-byte struct instead of the `_a/_b/_c` arrays). Three
  interleaved pairs: **14.996 -> 13.786 (-8.03%), 15.238 -> 13.728 (-9.90%),
  14.899 -> 13.729 (-7.87%)**; `executeMs` -7.6%, `runnerMs` 12,604 -> 10,617
  (**-15.8%**); every 1B anchor exact (menu `B06BA6E5`/`F5D61C94`,
  33,638/1,402/696/686/686, culled 41,035, GL 2,733,736 tris,
  `BackendUnsupported=0`; `VshProgramPlanTests` 8/8 including the 400-program
  fuzz). **Below 10% wall and 20% engine -> reverted**, recorded as the negative
  `D3` with the price; the combined slice (+ per-draw `PgraphDrawInfo` cache,
  ~0.3-0.4 s) prices at ~-10.5% and is D5's first step.
- **Gates:** no code changed (tree byte-identical to `f469218`). Fast 1B
  anchors re-verified: menu `B06BA6E5`/`F5D61C94`, triangle
  `4F1FCBC0`/`7DAB63EB` (2,706/6,455/68/34) at 5.669 s; build 0 warnings, suite
  green, format clean. A 100M-step boot leg reads 1.62 s (menu) / 1.93 s
  (triangle) with 39 ms of device writes and one flush - that is the shared
  boot share, not title work.
- **Note for the next session:** `runnerMs` is not printed unless
  `--jit-timing` is passed; the A/B legs must carry it to read the engine bar.

**Session 2026-10-22 (thirty-ninth; the device stage was measured for the first
time, and a clear now fills through spans - **-10.6% triangle / -10.6% / -9.4%
menu** on the headline `gl+replace` legs with every anchor byte-identical -
decisions §§239-240). Track C/B, two slices, one measured null.**

- **The measurement came first, and it repriced the wall.** A temporary probe
  around `Nv2aMmio.Write` read the triangle 1B `gl+replace` leg as **1.31 s of
  6.1 s inside 44,030 device writes**: 766 ms in the 2,706 draw flushes (of which
  the vertex-program interpreter is **413-435 ms** for 63,524 vertices at 27.7
  VS instructions each, `FillTrianglePsh`/GL 173-181 ms, setup 61-66, attribute
  fetch 34-36, epilogue ~70) and **~540 ms in the 6,455 surface clears**.
- **§240 landed: a 32-bit clear fills rows through `Span<uint>.Fill`.** Inside a
  `GpuWriteBatch` the fast adapter's write *is* a 4-byte RAM store and
  `IGpuMemoryFast.TryGetRamBytes` hands out the same array, so the exactly
  in-range, 4-byte aligned case no longer pays one interface call plus a bounds
  check per pixel (~20 ns/px). Everything else - 16/8-bit merge, unaligned
  offset/pitch, out-of-range partial fill - keeps the per-pixel loop.
  `SurfaceClear_FastFillLane_MatchesPerPixelFill` compares a fast-adapter clear
  against a plain one over the whole RAM image (and pins the `pitch=0x42`
  decline). Device-write time **1,306 -> 816 ms**; triangle `gl+replace`
  **6.31/6.43/6.34 -> 5.61/5.74/5.73 s (-10.6%, 3/3)**, menu
  **22.43/20.76 -> 19.98/18.82 s (-10.6%/-9.4%)**, SHAs and frames exact.
- **§239 landed: the raw emitter's EIP/TSC/served are static per edge.** A block
  is straight-line, so 11 emitted instructions per member (EIP set+advance, TSC
  tick, served increment, two store-forwarding chains) became one
  `EmitPublishAccounting` per edge; `F_TSC` is the block's entry base and
  `F_TSC_NOW` the published value. Measured **engine -3%** (`runnerMs` 3,401 ->
  3,310, ns/member 6.3 -> 6.1) and **wall flat** (3 pairs: 0.93/0.99/1.00) - the
  runner is **latency-bound, not instruction-bound**, which is the session's
  most useful negative for Track C. The trap: a delivered in-block poll ticks TSC
  *twice*, so the pending path publishes `_membersDone + 2`; with `+1` the first
  28M steps lose one tick per pending delivery (109 of them) and the CPU SHA
  moves while RAM stays identical. Found by byte-diffing 68 MB `--save-state`
  images of the stashed base and the new build (2 bytes at the TSC field).
- **Measured nulls (reverted):** making `VshProgram.Execute` allocation-free
  (per-thread register file + temporary pool: ~3.5M allocations per 1B leg
  removed, 413 -> 426 ms) and reusing the per-draw constant/input/output scratch
  (~606k) both changed nothing. The VS interpreter's cost is its own work
  (~245 ns per instruction), not GC.
- **Gates.** Accurate menu golden PASS (sw `4EF1FBF6` / gl `B6116DAE`, diff 2602,
  violations 0, counters 33632/686/686) and triangle PASS (sw `A1740DE0` / gl
  `7A860ED7`, diff 27849, 2706/68/34); fast 1B anchors exact on all four legs
  (triangle sw `B45254CA`/`7DAB63EB` 57,663,082 px, triangle gl
  `4F1FCBC0`/`7DAB63EB`, menu sw `34973510`/`F5D61C94` 253,998,498 px, menu gl
  `B06BA6E5`/`F5D61C94`); ktest smoke PASS 7/7 at **792,800,535** steps, log
  `CA5D3867` (40,797 B) byte-identical. Suite **1863 -> 1865**. No snapshot
  change (host-side only; Nv2a v9, audio v3).
- **Next:** the draw-flush half of the same stage is the largest unowned CPU-side
  item left (766 ms, of which the interpreted vertex program is 413-435 ms);
  `next-session.md` §2 Option A. A 100M-step mid-boot RAM flake (`08B3760F` vs
  `A52EBE41`, two NaN-sign bytes) is pre-existing and block-population sensitive
  - the base build reproduces it with `--jit-threshold 8`.

**Session 2026-10-22 (thirty-eighth; the non-JIT run slice went 256 -> 64 after a
sweep that had never been done - another -3.7% / -2.2% with every fast anchor
byte-identical - decisions §238). Track C, one constant, exact.**

- **The sweep.** `JitSpecSlice` bounds how many instructions one non-JIT run
  serves before the engine gets another chance at the head; its 256 came from the
  pre-raw engine (§194). At 100M (`--gpu gl --gpu-replace --jit-timing`):
  slice 256/64/16/4 -> wall 2.048/1.813/1.840/1.912 s, `nonJitMs`
  814/481/411/368, blocks 1122/1203/1399/1751. `nonJitMs` keeps falling to 4 but
  formation eats it: **64 is the wall minimum.**
- **Measured at 1B (interleaved pairs).** triangle `gl+replace` 6.312 -> **6.081 s
  (-3.7%, 3/3)**, menu `gl+replace` 20.199 -> **19.762 s (-2.2%, 3/3)**. Engine
  shape: `nonJitInstructions` 5,478,026 -> **2,618,986 (-52%)**, `nonJitMs`
  2190 -> **1650 (-25%)**, `nonJitCalls` 169,430 -> 182,142 (shorter runs, more
  calls), blocks 2,603 -> 2,812 / `outerMs` 3981 -> 4297 (formation pays for it).
  Per-member cost of what remains rose ~375 -> ~590 ns: the short runs left are
  device-heavy, matching §233a's GPU-inside-the-interpreter finding.
- **Both defaults now agree (64)** - `XboxCpu.JitSpecSlice` and the CLI's
  `jitSlice`, so a gate leg without `--jit-slice` runs what was measured. Do not
  push it to 16 on the `nonJitMs` number alone.
- **Gates.** All four fast 1B anchors byte-identical with the new default
  (triangle sw `B45254CA`/`7DAB63EB` 57,663,082 px, triangle gl
  `4F1FCBC0`/`7DAB63EB`, menu sw `34973510`/`F5D61C94` 253,998,498 px, menu gl
  `B06BA6E5`/`F5D61C94`), four corpus rows reproduce their documented pairs
  (`usb` 1B `ACDB5720`/`1590C697`, `vp_lighting` 3B `162DAFF1`/`0380C502`,
  `bump_cubemap_reflect` 3.6B `5ACD06FF`/`C848610E` and 4.4B
  `D90376B9`/`EBAC6B99`), suite green, format clean. The accurate goldens do not
  involve the JIT and passed after §237.
- **Combined session position (base -> now):** triangle `gl+replace`
  **6.672 -> 6.081 s (-8.9%)**, menu `gl+replace` **20.456 -> 19.762 s (-3.4%)**,
  100M boot phase **2.276 -> 1.813 s (-20.3%, both legs with `--jit-timing`)**.
  **The `TryMemo`-handoff was probed in the same session and rejected:** only
  4.2-4.9% of the non-JIT members sit at a head that already has a block
  (50,550/1,190,276 at 100M, 127,173/2,618,986 at 1B) = ~1.2% of the wall, so the
  remaining mass is the 55k *unbuilt* heads (a formation price). What is left for
  Track C is the engine shape, not another slice.

**Session 2026-10-22 (thirty-seventh; the boot ROM is now a cached, blocked image
window - the §2 "superblock for the boot" lever measured at -6.4% and repriced -
decisions §237). Track C; one slice, every gate anchor byte-identical.**

- **What was missing.** `DecodeCache` refused every physical above
  `MaxTrackedPhysical` (0x20000000) because its store-invalidation bitset only
  indexes base RAM, so the MCPX boot ROM at `0xFFFFFFxx` was never cached
  (`nonRamSkips=359,476` at 1B, all in the first 100M steps) and `FormJitBlock`
  could not walk one member there - every ROM instruction paid a `Step` (~2 us).
- **Landed.** A new `IImmutableImageDevice` marker on `RomDevice`/`RomAliasDevice`/
  `RomOverlayDevice`; `AddressSpace.TryGetImmutableImageWindow` accepts a window
  only when the winning region (priority-aware, so the MCPX overlay over the flash
  alias resolves correctly) is such a device; the cache holds those decodes in a
  key set with **no store path** (`FlushImmutableImages`), a store into a recorded
  window drops them conservatively, the overlay-disable event calls the narrow
  `InvalidateDecodeCacheImageWindow`, `Reset`/snapshot load reach it through
  `Mmu.Flush`, and `RunJitBlock` **drops a block whose decode defaults no longer
  match** the current mode (`JitBlockCache.NoteStale` also clears the memo slot)
  so a 16-bit real-mode block at a head later fetched in 32-bit re-forms instead
  of yielding forever.
- **Measured (interleaved 1B A/B, gate media).** triangle `gl+replace` 6.672 ->
  **6.242 s (-6.4%, 3/3)**, menu `gl+replace` 20.456 -> **20.158 s (-1.5%, 3/3)**,
  triangle `sw` -0.7% (2/3, rasterizer-bound), 100M boot phase 2.276 -> **2.035 s
  (-10.6%)**. `nonRamSkips` 359,476 -> 0, one-instruction non-JIT runs 434,309 ->
  136,149, `nonJitCalls` 523,062 -> 169,430, `nonJitMs` 2291 -> 2078, decode
  `hitShare` 0.9143 -> 0.9747, `imageFills=157`. **The repricing:** §2's ceiling
  assumed all 3.65M non-JIT instructions were ROM fetches; 317k are (9%), so the
  lever was worth 6.4%, not 8-13% - the boot's remainder is 55,215 RAM heads
  below the 16-visit build threshold (an Iced formation price, §234e).
- **Gates.** Accurate 1B menu slice-1 golden PASS (sw `4EF1FBF6`, gl `B6116DAE`,
  diff 2602, violations 0, counters 33632/686/686) and triangle slice-2 PASS
  (sw `A1740DE0`, gl `7A860ED7`, diff 27849, 2706/68/34). **All four fast 1B
  anchors byte-identical** (triangle sw `B45254CA`/`7DAB63EB`, triangle gl
  `4F1FCBC0`/`7DAB63EB`, menu sw `34973510`/`F5D61C94`, menu gl
  `B06BA6E5`/`F5D61C94`; pixels 57,663,082 / 253,998,498). ktest smoke PASS 7/7
  at **792,800,535** steps, log `CA5D3867` (40,797 B) byte-identical. Corpus spot
  checks reproduce `nxdk/usb` 1B `ACDB5720`/`1590C697`, `xgu-vp_lighting` 3B
  `162DAFF1`/`0380C502`, `xgu-bump_cubemap_reflect` 3.6B `5ACD06FF`/`C848610E`.
  The strongest exactness leg: 1B `--decode-cache --decode-verify` (interpreter
  plus the cache, the JIT bails under verify) reproduces the **accurate anchors**
  `812EA159`/`2F632483` with `verifyFailures=0`. Suite **1857 -> 1863** (5 of the
  6 new tests fail on the pre-change source). Format clean. Host-side only: no
  snapshot version change (Nv2a v9, audio v3 unchanged).
- **Trap recorded (§237 §4).** A fast-mode RAM SHA can move mid-boot with no
  semantic change: `--jit-spin` counts store-free STI *blocks*, so a different
  block population moves where device time is lumped. This session's change moves
  the triangle's 99.5M-step value and heals by 1B; the base build reproduces it by
  changing `--jit-threshold` alone. Diff with `--jit-spin` off before diagnosing.
- **Next.** `RunSpecBlock`'s ~375 ns/member (~2.0 s of the 6.24 s triangle wall) is
  the largest non-JIT line and the only remaining C item that is neither measured
  null nor priced out - `next-session.md` §2.

**Session 2026-10-22 (thirty-sixth; the GL backend computes cube maps by decoding
the guest's faces into a host array texture, and compatibility gap 12 is closed -
decisions §236). Track B/E, correctness; every gate anchor byte-identical.**

- **The defect chain (236a).** `xgu bump_cubemap_reflect`'s reflection panel was
  black on the GL path. Four defects, each hidden by the first: (1)
  `ResolveGuestWordPool` returned early on a **null** carried word set, so a draw
  command whose software fill captured no words never built a pool and the cube
  tap read pool entry 0 of a zero uniform; (2) the shader's cube decoder read
  `uTexFieldOff/W/C` - uniforms the backend **never uploaded** - and then
  returned the wrong channels anyway; (3) the pool was 48 entries against the
  shader's 24-entry array, and a 256x256 face is 393,216 texels (no window fits);
  (4) `GuestTexelsFor` handed a **wrapping ring** out as a contiguous
  `ArraySegment`, which crashed the 4.4B leg (`ArgumentException`) once a cube
  stage captured more than 64 words.
- **The design (236b).** A cube stage now samples a host **`sampler2DArray`**
  (`uCube0..3`, units 4-7) that the backend decodes from the six swizzled faces
  with `Nv2AMmio.SwizzleAddressNT` + `DecodeSwizzledTexelWord` - the software
  sampler's own two calls - so every face texel is addressable and the two
  executables share one decoder. Deleted: `AppendCubeWords`,
  `CubeTexelAddress`, `DescribeTextureDecode`, `DecodedChannel`, the GLSL
  descriptor decode, the swizzle bit-spread helpers and nine dead uniforms. An
  unresolved cube stage returns the software's `SetTexelNone`, never white.
- **HILO is gated on the dot map (236c).** `ResolveGuestWordPool` required a pool
  for *every* `DOT_PRODUCT` stage; only maps 4-7 consume the raw pair, and the
  colour maps read the already-sampled texel. With the gate the row's eight-stage
  program is served with `guestWordDraws=0 guestWordMisses=0` and
  `composedFrames=0` (was 122 of 156 frames painted in software).
- **Evidence (236d, H1).** GL replace vs software, same flags: 4.4B **2 pixels of
  307,200 differ (maxDelta 3)**, 19,042 colours both; 3.6B **2 pixels
  (maxDelta 3)**, 21,041 both. Before: 4 and 2 colours against 18,980 / 20,843.
  `unsupported=0 fatal=0`, `draws=27451`. `xgu vp_lighting` 3B still `E68070A3`
  (GL) / `72B2F490` (sw).
- **Gates.** Menu accurate 1B GL golden PASS (sw `4EF1FBF6` / gl `B6116DAE`,
  `diff=2602 violations=0 maxDelta=1`, counters 33,632/686/686); triangle
  slice-2 PASS (`A1740DE0` / `7A860ED7`, `diff=27849`); fast 1B exact on all four
  legs (tri sw `B45254CA`/`7DAB63EB`, tri gl `4F1FCBC0`/`7DAB63EB`, menu sw
  `34973510`/`F5D61C94`, menu gl `B06BA6E5`/`F5D61C94`); ktest smoke PASS 7/7 at
  792,800,535 steps, log byte-identical. Suite **1845 -> 1857**.
- **New diagnostics:** the `--gpu-time` combiner row carries `ts<n>=` (each
  referenced slot's `TEXFMT:dim[:c][:x][:s]`), which is what made "slot 3 is a
  cube-flagged `0x3A`" visible; and the row's `guestWordDraws/Misses` now mean
  only "a HILO input word was needed".
- **Filed, not fixed:** a HILO dot input is one word per stage (`PoolWord(inSlot,
  0)`), i.e. a per-stage constant; no measured row uses a HILO map. The exact
  shape is §236's: decode the input stage's raw words into a texture. The
  MUX-lsb/final-lerp "leads" in §230 were **not** the cause - both programs match
  software now - so do not change them without a failing differential.
- **Priced for the next session (Track C, option ii):** the boot's non-JIT path
  is `nonJitMs=995` of `outerMs=1221` at 100M steps over `nonJitCalls=449,772`
  serving 3,654,056 instructions (272 ns each). Forming blocks over **ROM pages**
  (the decode cache refuses `physical >= 0x20000000` today) is the only remaining
  lever with an over-the-bar ceiling: ~0.85 s = ~8% of the triangle `sw` wall /
  ~13% of `gl+replace`. Constraints and the write path are in
  `next-session.md` §2.

**Session 2026-10-22 (thirty-fourth; the display source under a GPU backend is now
decided by what the display controller scans, and the C7 encoder is priced out -
decisions §234). Track P/B product fix, exact; no gate anchor moved.**

- **The bug (234a).** With the OpenGL renderer, the nxdk samples that write the
  framebuffer themselves (`gamma`, `hello`, `usb`) showed the boot animation's
  last frame: `-Sample gamma -Gpu gl -GpuReplace` presented `18C7CC83` while the
  software leg presented its gradient `D3EBF420`. `TryCopyDisplayFrame` returned
  the backend readback whenever a backend was attached, and that readback is a
  picture of the *3D colour surface of the last flip* - a different buffer from
  the one PCRTC scans once the XBE takes over the display.
- **The rule (234b).** `Nv2AMmio` keeps a 4-entry ring of **recently presented
  surfaces** (address/extent/pitch/depth, recorded at `CapturePresentedFrame`) and
  `TryCopyDisplayFrame` serves the backend frame only when the CRTC geometry
  matches a ring entry, the RAM scanout otherwise. Measured first with a
  temporary `XGRID_TRACE_PRESENT` trace: the triangle is a three-buffer chain
  whose PCRTC trails the last flip by one buffer (`surface=3D00000
  pcrtc=3BD4000`), and `gamma`'s XBE ends by pointing PCRTC at `0x39E0040`, which
  no flip ever presented. `DisplaySourceDebug` prints `backend` or
  `memory-scanout`. **Nv2a snapshot v8 -> v9** (the ring is device state).
- **Evidence (234c).** GL replace at 1B: `gamma` `D3EBF420`, `hello` `72D33DC2`,
  `usb` `119AFA48` - all now equal to their software rows; `led` `18C7CC83` is
  the negative control (its scanout *is* a presented surface); `mesh`
  `A7442AB1` and the triangle's `--frame-display` (= `--frame` `7A860ED7`,
  `source=backend`) are the regression proof that a flipping title keeps the GPU
  frame. Guest `ram`/`cpu` SHAs unchanged on every sample.
- **Frontend (234d).** `EmulatorSession`'s private `_backendFrameSeen` fallback
  (a second copy of the same decision, blind to later mode changes) is gone;
  both renderers call `TryCopyDisplayFrame`. Verified through the real session
  with `tools/frontend-smoke` (dashboard, fast mode, 20 s): **software** 894.9M
  steps / 528 frames / 306,817 non-black px and **OpenGL replace** 858.3M steps
  / 287 frames / the same 306,817 px, both with a 72 MB `.xgs` round trip and
  `faulted=False`. That harness did not compile before this session
  (`RendererPolicy.IsOpenGl` is private since §217, and the tool was outside the
  solution so nothing caught it): fixed, and **added to `XGrid.sln`**.
- **C7 encoder, priced and rejected (234e).** New
  `tests/XGrid.Cpu.Tests/EncoderSpikeTests` measures a block-shaped list:
  `Assembler.Assemble` **390 ns/instruction**, single-pass `Encoder.Encode`
  **120 ns**, Iced's decoder 34 ns - **ratio 3.2x**, and it pins that the
  single-pass path reproduces the assembler's bytes exactly. On §233a's stage
  table `Assemble` is 682 ms of the triangle's 1,026 ms formation, so a 3.2x
  encoder is **~7% of the triangle wall / ~3% of the menu wall**: below the C
  stage's 10% bar and unrelated to the 5.5x gap. **Do not own the encoder**; C7's
  lever is the engine shape. §235 derives the same for a trace emitter: the
  block-boundary population it removes is ~350 ms of the runner = ~3-5% of the
  wall, so the engine-shape decision (option iii) is the only one left.
- **Gates (234f).** Accurate 1B menu golden PASS (sw `4EF1FBF6` / gl `B6116DAE`,
  diff 2602, violations 0, counters 33632/686/686) and triangle PASS (sw
  `A1740DE0` / gl `7A860ED7`, diff 27849, 2706/68/34); fast 1B anchors exact
  (triangle `B45254CA`/`7DAB63EB` 57,663,082 px, menu `34973510`/`F5D61C94`
  253,998,498 px); suite **1837 -> 1845**, the two scanout-serving tests proven
  fail-pre-fix; ktest smoke PASS 7/7 at **792,800,535** steps, log `CA5D3867`
  (40,797 B) byte-identical. Format clean.

**Session 2026-10-22 (thirty-third; C5 slice 1 landed - the IL runner is built on
demand and a chain jump keeps the source's frame - and the whole wall priced per
stage for the first time - decisions §233). Track C: -3 to -4% wall, exact.**

- **Priced before building (233a).** A temporary call-level probe split
  `Cpu.Execute` on the 1B triangle `gl+replace` leg: native runner **3393 ms**
  (6.25 ns/member), **formation 1026 ms** (682 of it Iced `Assemble` at 377 ns
  per emitted instruction; 2,585 blocks / 19,175 members / 1.9M instructions),
  boot `Step` path **1012 ms** (every step a decode miss), **GL submission
  ~1000 ms executed inside the guest's NV2A FIFO stores** (405k stores at
  `0xFD800000`, 2.4 us each - the sw leg shows the same region at 1.76 s with
  `rasterPsh=4.46 s`), non-GPU interpreter ~400 ms, and cold `RunJitBlock`
  entries only **68 ms** (523k x 132 ns). Two older "next levers" died on that
  number: the one-instruction round trip is 68 ms, not ~0.75 s, and caching ROM
  decodes has a **250 ms ceiling** (the fetch+decode is 345 ms of which 359k of
  498k decodes are non-RAM) - consistent with 232f's measured null.
- **Landed (a): lazy IL runner.** Every block built a DynamicMethod even when a
  raw runner existed; the IL runner is only reachable when the step budget
  cannot cover the block. `JitBlock.Plans` is retained and the build moved to
  the invoke path. `ilBuildMs` 72 -> 1, `formMs` 1026 -> 846. Raw-less blocks
  (exact mode, non-raw hosts, the ~12 unencodable heads) still build eagerly,
  so rejection semantics are unchanged.
- **Landed (b): the in-frame chain entry (233c).** A chain jump used to publish
  the state, probe, tear the frame down and re-enter the target's prologue
  (~82 instructions per 4.4-member edge). Each raw block now emits `_chainIn`
  after its prologue; the edge writes `F_BUDGET`/`F_ENTRIES`, zeroes `F_SERVED`
  and jumps there, and the target continues in the same frame (reloading flags
  and guest registers). `EmitChainAttempt` was rewritten to touch host scratch
  registers only. The entry is resolved with
  `BlockEncoderOptions.ReturnNewInstructionOffsets` + `GetLabelRIP`, range
  checked, and published in the chain table instead of the function pointer.
  `outerMs` 4477 -> 4353; **interleaved 1B A/B: triangle 6.951 -> 6.660 s
  (-4.2%, 3/3), menu 20.991 -> 20.356 s (-3.0%, 3/3)**, all four fast anchors
  and both accurate goldens byte-identical.
- **Measured negative (233d):** keeping the guest registers live across the
  edge (also skipping the state publish) **diverges** - 1B RAM `EED915B5` vs
  `4F1FCBC0` (CPU exact), first difference at 30.5M steps in a kernel structure
  written by a block that chains through port I/O. With `XGRID_RAW_CHAIN=0` both
  builds agree, so the frame sharing is fine and the register-liveness
  assumption is not. Priced negatives (233e): per-block shared cold stubs
  -60 ms, global -160 ms.
- **Gates:** accurate menu golden PASS (sw `4EF1FBF6` / gl `B6116DAE`, diff
  2602, violations 0, counters 33632/686/686) and triangle PASS (sw `A1740DE0` /
  gl `7A860ED7`, diff 27849, 2706/68/34); fast 1B exact on four legs (triangle
  sw `B45254CA`/`7DAB63EB` 57,663,082 px, triangle gl `4F1FCBC0`/`7DAB63EB`,
  menu sw `34973510`/`F5D61C94` 253,998,498 px, menu gl `B06BA6E5`/`F5D61C94`);
  ktest smoke PASS 7/7 at **792,800,535** steps, log `CA5D3867` (40,797 B)
  byte-identical. Suite **1836 -> 1837** green (one transient Nv2a GL flake on
  the first run, green on re-run); the new
  `ChainedBlocks_CarryFlagsAcrossTheChainEdge` pins the chain entry's flag
  reload (a `Jcc`-first chained target) and fails pre-fix. Format clean. No
  snapshot change (host-side only).
- **Where it leaves C:** the charter's own kill criterion is met (C4 null, C5
  -3/-4%); the remaining gap to xemu is ~5.5x and the only material levers are
  the encoder (377 ns/instruction), the per-member code volume (99 instructions
  per guest instruction) or a different engine shape. `next-session.md` §2
  Option A makes that a decision, with the prices attached.

**Session 2026-10-22 (thirty-second; C4 priced by implementing it and reverted
as a measured null, the non-JIT wall decomposed to the boot ROM decode, the
interrupt poll unified, and a lost store translate slot fixed - decisions
§232). Track C stage C4 measured, not landed.**

- **C4 slice 1 was implemented, measured and reverted.** The probe now has a
  host-pointer page table (per-slot RAM host base, 0 for non-RAM, physical
  path kept for `--jit-mmio`, RAM bound dropped because a slot is only filled
  for a page that is entirely RAM). Interleaved 1B A/B: menu wall 21.01 ->
  21.05 s (`runnerMs` 15,872 -> 16,026), triangle 6.89 -> 6.77 s
  (`runnerMs` 3,407 -> 3,382) - **noise**, with every SHA/counter exact. The
  probe's three dependent L1 loads are hidden by the out-of-order engine, so
  the raw path's ~6.3 ns/served member is not the probe. Reverted per the
  no-polish-pass rule; recorded as a measured negative.
- **Where the triangle's 6.9 s actually goes** (`--jit-timing` + `Host
  timing`): `RunJitBlock` 4.53 s (native 3.42 s + 1.1 s head lookup /
  formation / chain) + `RunNonJit` 2.25 s (523k `RunSpecBlock` calls serving
  5.31M members at 277 ns each, and 489k `Step`s at 1.6 us each) + device
  `Advance` 7 ms. **Both priced candidates for that non-JIT time measured
  null:** read-only-ROM decode caching (implemented in full - ROM windows
  classified, no store tracking, overlay flush - `nonRamSkips` 359,476 -> 0,
  decode misses -359k, wall and `nonJitMs` flat, SHAs identical; reverted) and
  a per-mnemonic `Step` tally that reads ordinary Mov/Add/Inc/Jcc. The cost is
  **per-call dispatch of a large cold-code population** (55,221 heads below
  the build threshold, 523k non-JIT runs at ~10 members each) plus the 1.1 s of
  outer bookkeeping; the `--jit-threshold` sweep (1/4/8/16/64 ->
  9.27/7.86/7.32/6.99/6.79 s) is already at its optimum. **C5 trace formation
  measured against `nonJitMs` (without growing `outerMs - runnerMs`) is the
  only structural lever left in C** - decisions 232f/232g. The menu shows the
  same shape: 20.8 s = `RunJitBlock` 17.50 s (runner 15.89) + `RunNonJit`
  3.23 s over 818k non-JIT calls (2.7x the triangle's count).
- **C6 slice 1 landed: one interrupt-poll contract.** `Step` and
  `RunSpecBlock` called the device model's `Acknowledge()` per instruction;
  new `XboxCpu.PollInterrupt()` (partial `XboxCpuInterrupts.cs`) loads the
  PIC's pinned pending word and calls the controller only when a vector may be
  pending. Exact by construction; wall delta below the 2% resolution, landed
  as architecture (the emitted blocks already polled this way).
- **Correctness fix (§232d).** The raw store probe's CR0.WP check clobbered
  R10D - the translate-cache slot the physical-page load indexes - so a store
  into a **read-only page with CR0.WP clear** wrote slot 0's frame (physical 0
  in the new test) instead of the mapped page. The kernel runs with WP set,
  which is why no gate ever hit it. Fixed + `ReadOnlyPageStoreWithWriteProtect
  Clear_WritesTheTranslatedPage` (fails pre-fix: `Read32(0) == 0xDEADBEEF`).
- **Methodology recorded (§232e):** env-gated emitter ablations are worthless
  when they change guest behavior (the flat-probe ablation ran "34% faster"
  on a diverged guest; a flags ablation fell back to the IL engine). Price a
  stage with interleaved same-window A/B legs on a stashed build instead.
- **Gates:** accurate menu `4EF1FBF6`/`B6116DAE` + counters 33632/686/686,
  accurate triangle `A1740DE0`/`7A860ED7` + 2706/68/34 (both GOLDEN PASS,
  violations 0); fast 1B anchors exact on four legs (menu `34973510`/
  `B06BA6E5`, triangle `B45254CA`/`4F1FCBC0`, CPUs `F5D61C94`/`7DAB63EB`);
  ktest smoke PASS 7/7 at **792,800,535** steps, log `CA5D3867` byte-identical.
  Suite 1835 -> **1836**; format clean.

**Session 2026-10-22 (thirty-first; course correction - xemu-class performance
is the long-run objective, Track C re-opened). Diagnostic/planning only; no code
change.** The operator set the long-run goal to xemu-class or better. The
measured position meets the freeze's own re-open condition: the CPU engine is
~17 s of the 24.3 s menu `gl+replace` wall (~70%) and the dominant part of the
7.5-7.9 s triangle wall; the gap is ~6.6× (triangle vs xemu's 1.2 s at 1B) and
~17.8× (menu vs the 1.36 s real-time band; xemu has no menu oracle). New
program: **C4** flat memory model/soft TLB, **C5** trace/superblock formation,
**C6** device/MMIO fast path, each gated on a same-window A/B (≥10% wall or
≥20% engine), exact 1B SHAs/counters, flag-off byte-identical and a
byte-identical ktest log. B3 (gap 12) is the only bounded B item; E steers with
performance rows; P's host audio runs parallel. Docs updated:
`docs/development-direction.md` §0/§1/§2/§3/§4/§5/§6,
`ImplementationPlan.md` (top priority list, performance campaign, phases
15/16/18, result), `docs/research-notes/next-session.md`,
`docs/research-notes/jit-track-charter.md` (Phase 3b), decisions §231.
Completion score stays **77.7%**
(no landed work; the rule is the score only moves with landed work).

**Session 2026-10-22 (thirtieth; `reach[...]` names the stage that carries each
combiner output flag - decisions §230). Diagnostic only; no gate anchor moved.**

- `PshCombiner.DescribeReach()` gained `flags@=<first>[-<last>]:<flags>,…`:
  runs of consecutive **writing** stages that share an output-flag word, so a
  MUX / BLUE_TO_ALPHA bit carried by a stage whose write nothing reads is no
  longer indistinguishable from one the program actually exercises. Three
  things the unit test pins: `FlagBits` needs a bit in 12-15 *and* a non-zero
  masked nibble; the alpha word is OR-ed unmasked (an alpha MUX write adds
  `0x40`); and the run sentinel must be -1 (`stackalloc` left "no run open"
  reading as "flags 0").
- **Measured on the B3 row** (`xgu bump_cubemap_reflect` 3.6B, `--gpu-time`):
  both dot-family records report `flags=C0 flags@=0:C0,1-7:00` / `1-6:00`, the
  panel program `flags=00`. The flag union is stage 0's alone, which disposes of
  gap 12's "carried in alpha through BLUE_TO_ALPHA on several stages" reading -
  the open leads there are texture content, the dot chain and the final-lerp
  negation (documented, still unimplemented). Per-fragment GL probes are not
  reached by this CLI (no save-at/continue adjacency, no in-draw hook).
- Gates: suite 1836 -> **1837**, build + format clean; the diagnostic cannot
  change guest RAM/CPU SHAs, frames or counters (the corpus row's BTFF record
  reproduces: 3.6B software last frame `BFC41A59…`, `cpu=C848610E`). No snapshot
  or GLSL-golden change.

**Session 2026-10-22 (twenty-ninth; the B3 corpus row runs its dot programs at
4.4B, the GL path renders that frame as 4 colours, and the read-set diagnostic
that makes the difference visible - decisions §228). No gate anchor moved.**

- **§227's anchor was wrong, not its finding.** `xgu bump_cubemap_reflect` is
  animated: at **4.4B** it spends the frame in `stages=8 c=00011108 tm=0332`
  (19.9M px) and `stages=7 c=00011107 tm=1332` (12.4M px) - PROJECT2D on
  slot 0, DOT_PRODUCT/CUBEMAP/REFLECT_SPECULAR on slots 1-3 - i.e. exactly the
  B3 family. The dot program is absent at 0.2-1.4B and at 3B. Phase table:
  `docs/research-notes/computed-mode-coverage.md`.
- **`reach[...]` on the `--gpu-time` combiner rows.** Each config row now also
  prints `tm=`/`sf=` and `PshCombiner.DescribeReach()` (`tx` slots the input
  words read plus a projector's own slot, `stg` writing stages, `src` sockets,
  `consts`, `flags` decoded, `lerp` final selects). A stage that writes no
  register was previously indistinguishable from a one-stage program. Tests:
  `PshCombinerReachTests` + `PshCombinerTests.DescribeReach_ReportsTheWritingStages`.
- **The cube word lane reads RAM, not the carried window.**
  `AppendCubeWords` declined every cube stage whose face texels were outside the
  seam's 32-entry window; it now falls back to the same direct RAM lane the HILO
  inputs use (plus `FindPooledWord` for a second cube slot in one command).
  `CubeStage_ReadsFaceTexelsFromGuestRam_WithoutCarriedWords` replaces §227's
  decline test (served, `guestWordDraws=1`); `ComputedModeShaderLiveTests` also
  compiles the two measured 8-stage programs on the driver. The row's own output
  is unchanged (`guestWordDraws=0`).
- **Corpus breadth: `nxdk/usb` is a row, and H1 builds nxdk rows after all
  (§229).** `host-doctor.ps1` probed PATH only, so it reported clang/make absent
  while MSYS2 at `C:\msys64` has the whole toolchain (nxdk `D:\nxdk`, `a6c31ee`);
  it now checks the MSYS2 default install ("BUILDABLE via MSYS2") and
  `docs/hosts.md` is corrected. `nxdk/usb` at 1B enumerates the daughterboard
  hub with its own USB stack and prints `Device connected on USB hub port 1
  (VID: 0409 PID: 55aa)` on the display export (rows: `nxdk-usb-sample.md`).
- **Open: the panel renders black.** GL replace presents **4 colours** at 4.4B
  and **2** at 3.6B against software's 18,980 / 20,843, with `unsupported=0
  fatal=0 composedFrames=0` - the FBO clear plus a black panel, i.e. a
  fragment-expression gap (the 3.6B shape predates the dot program). Gap 12
  carries the leads (BLUE_TO_ALPHA into R0 + the MUX-lsb select and the final
  lerp negation; a 3.6B per-fragment differential first).
- **Gates (this session, H1):** fast 1B triangle `B45254CA`/`7DAB63EB`/frame
  `A1740DE0` (68/34), menu `34973510`/`F5D61C94`/frame `4EF1FBF6` (686/686);
  GL fast triangle `4F1FCBC0`/frame `7A860ED7`, menu `B06BA6E5`/frame
  `B6116DAE`, both `BackendUnsupported=0 fatal=0 composedFrames=0`; accurate 1B
  triangle `812EA159`/`2F632483`/`A1740DE0` and menu `6AB39AB0`/`3D2740B8`/
  `4EF1FBF6`. Suite 1821 -> **1836**, format clean.

**Session 2026-10-22 (twenty-eighth; the computed-mode GL shaders never
compiled - a dot-variable redeclaration, an unarmed texel capture, and the live
compile gate that now covers both - decisions §227). No gate anchor moved; the
changes are host-side plus one `Nv2AMmio` setter.**

- **The row's own program failed to compile (§227).** `PshGlslGenerator` emitted
  `float dotVal = 0.0; bool dotOkA = StageDot(...)` per dot stage and then
  guarded the texel with an undeclared `dotOk`; a combiner with two
  `DOT_PRODUCT` stages (`xgu bump_cubemap_reflect`'s own program) also
  redeclared `dotVal`/`dotOkA`. `EnsureProgram` throws on a compile failure, so
  the backend counted a fatal and `--gpu-replace` painted the **whole frame** in
  software. §226 reported that row as a passing slice on a frame that was never
  GL's. Dot variables/flags are now named per stage
  (`dotVal{slot}Ok`, `dotA{slot}Ok`, …), `AppendDotTripleInto` replaced
  `AppendDotPair` (no collision between two cube-direction modes), and
  `DOT_RFLCT_DIFF` reads `(slot-2, slot-1, slot)` like `DOT_RFLCT_SPEC`.
- **The gate that catches it.** `ComputedModeShaderLiveTests` builds every
  dot/reflection/str mode in every legal stage, the adjacent two-dot pairs and
  the cubemap row's program at block capacities 1/8/32 and **compiles+links each
  on the driver** through the shared `GlProbe.TryBuildProgram` (the probe's own
  helper; `InternalsVisibleTo XGrid.GpuGl.Tests`). 99 programs, all accepted on
  NVIDIA 4.6. A string golden cannot see a redeclaration - the session's first
  draft passed every string assertion and still failed the driver.
- **The seam's texel capture was never armed.** `EnableGuestTexelCapture` had no
  caller, so `GuestTexels` was always null and every computed draw would have
  been declined for want of words. `Nv2AMmio.Backend`'s setter now arms it only
  while a backend is attached (a software-only run is unchanged).
- **What the row actually exercises (measured, and negative):** the 3B
  `-Gpu gl -GpuReplace` leg now reports `fatal=0 unsupported=0 programs=9
  composedFrames=0` and presents the GL frame (`5174B1EB…`, wall 10.7-12.2 s),
  but its *executed* combiner is `stages=1 control=00000001 texmodes=0000`
  (`PROJECT2D` plus mode `0x03` only) - the `DOT_PRODUCT`/`DOT_REFLECT_SPECULAR`
  modes never run, so the GL frame is a two-colour image by construction (not a
  regression: before the fix the same frame came from a software fallback that
  produced the richer `DA0750EE…` image, and the software leg is unchanged).
  Gap 12 has **no corpus coverage**; the synthetic seam tests are its only path.
- **Gates:** 1B accurate menu golden PASS byte-identical (sw `4EF1FBF6…`, gl
  `B6116DAE…`, diff 2602 px / violations 0 / maxDelta 1, `BackendUnsupported=0`,
  counters 33632/686/686); triangle slice-2 PASS (sw `A1740DE0…`, gl
  `7A860ED7…`); ktest smoke PASS 7/7 at 792,800,535 steps; suite 1821 -> **1830**;
  format clean. No snapshot or GLSL-golden change.
- **Also fixed:** the live replace-mode fixture expected a decline for a draw
  whose slot-0 mode is 1 (not `PROJECT2D`); that shape reads no guest word and
  is served - the decline path is now its own case (now a real cube-stage draw
  with an empty carried set, `guestWordMisses=1`, `composedFrames=1`).

**Session 2026-10-22 (twenty-seventh; B3 slice 1 - the computed cube/dot texel
state reaches a GPU backend and the shader carries the math - decisions §226).
No gate anchor moved; every change is host-side plus seam fields.**

- **Ceiling measured first (§226).** `xgu-bump_cubemap_reflect` at 3B
  `-Gpu gl -GpuReplace` declined 7 draws (`unsupTex[m0C=7 m11=14]`): the sample
  computes its colour from DOT_PRODUCT on stages 1-2 and DOT_REFLECT_SPECULAR on
  stage 3, so the GL backend could not sample 3 of its 4 stages.
- **Landed.** `PgraphTextureSlot` carries the cube face extent (**TEXFMT
  `BASE_SIZE`**, never TEXIMAGERECT - the §209a rule), the face stride and the
  swizzle masks; `PgraphDrawInfo` carries the eye (`EyeX/Y/Z`, XF constant
  `0x38`, `Nv2AMmio.EyePosition`); the generated shader transcribes the dot
  chain, the `Sign1/2/3` + HILO dot maps, `CubeFaceSelect`, the tent-clamped
  cube sample and `SpecularReflect` - all per-draw uniforms, so `BuildKey` never
  splits and a mode switch never recompiles. The shader's texel decode is
  **table-driven from `DescribeTextureDecode`**, proven equal to
  `DecodeTextureTexel` for all 20 supported formats
  (`TextureDecodeDescriptor_ReproducesTheSoftwareDecode`).
- **The fetch is NOT landed, and the modes stay declined.** A cube face / HILO
  word is a swizzled guest-RAM read, not a 2D row; `TexelWord()` is the one hole.
  A guest-word uniform buffer was attempted and **reverted unvalidated** rather
  than shipped: `CheckModesUnsupported` still declines and counts every dot/
  bump mode, replacement mode still paints them in software (§220d), so a wrong
  texel can never present. Next slice = that buffer.
- **Evidence.** 1B accurate goldens PASS byte-identical (menu sw `4EF1FBF6…` /
  gl `B6116DAE…`, diff 2602, counters 33632/686/686; triangle sw `A1740DE0…` /
  gl `7A860ED7…`, diff 27849); cubemap 3B gl-replace byte-identical to baseline
  (`DA0750EE…`, `19D576A8…`, `D5D38C64…`, `unsupported=7`); same-window 3-run
  A/B wall 12.08/11.56/11.49 -> 11.62/11.64/11.51 s; ktest smoke PASS 7/7 at
  792,800,535 steps, log byte-identical. Suite 1812 -> **1821**.
- **Lesson recorded:** the live GL tests caught two real defects the unit diffs
  could not (a malformed decode ternary, `SpecularReflect` reading `c1..c3` from
  outside its scope). Shader changes need a real `CompileShader`, not a string
  golden.
**Session 2026-10-21 (twenty-second; the GL sampler decodes the texture
address word, a composed replace-mode frame keeps both writers, and the
frontend can step frames - decisions §220). Every gate anchor stayed
byte-identical; all changes are host-side plus seam fields.**

- **Gap 7 closed on the GL side (§220b/§220c).** The seam now carries resolved sampler
  state (`SamplerWrapU/V` - 0 wrap, 1 mirror, 2 clamp, 3 clamp-to-border -,
  `BorderColor`, plus §219a's `FilterBilinear`/`FilterConvolution`), filled by the same
  `Nv2AMmio.ResolveTextureFilter` helper the software `SamplerPlan` uses (§219a's
  static decode folded into it). The
  GL backend had read the *raw* address word, so its wrap/mirror were swapped
  and border clamped; the menu programs slot 1 wrap 3,950x and slot 3 border
  80x (usage histogram). `PshGlslGenerator` now emits `SampleTap` (border +
  wrap), the four-tap tent (`SampleBilinear` arithmetic), the nine-tap 1-2-1
  `SampleConvolved` (PROJECT2D only, MIN = CONVOLUTION_2D_LOD0), the PROJECT2D
  divide by w, and the clip origin on **both** fragment axes (`px += T(2).w`;
  only `ClipY` was added, so `ClipX != 0` compared absolute vertices against
  clip-relative `gl_FragCoord`). Measured on `xgu vp_lighting` 3B (the only
  fractional-texcoord row with zero declined draws): gl vs sw agree within one
  8-bit step in **both** filter states (legacy `E68070A3` vs `72B2F490`,
  registers `A9E09139` vs `CF698444`, dmean <=0.01, maxDelta 1), and the flag
  moves both by the same ~6.5%/maxDelta 80. The menu cannot discriminate
  (texel-centre sampling) and its registers leg is the same `B6116DAE`.
- **Gap 8 closed (§220d, supersedes §219b): one writer per frame.** In `--gpu-replace`, the first
  declined draw now composes the FBO's clip window into the guest color
  surface (new seam fields `PgraphDrawInfo.ColorSurfaceAddress`/`ColorPitch`,
  the software fill's own address expression), every later draw of that frame
  is declined too, and the flip drops the FBO so the present-upload path
  rebuilds it from RAM (`composeUploads` is gone; the counters are
  `composedFrames`/`composedPixels`/`composedDraws`). Depth/stencil are not composed (GL depth is host-side)
  - documented. `xgu bump_cubemap_reflect` 3B `-Gpu gl -GpuReplace` now presents
  **`DA0750EE` = the software frame** (was the clear only, `3553B0DF`), with
  `composedFrames=7`, `composedPixels=2,150,400`, `BackendUnsupported=7`, CPU
  SHA unchanged (`D5D38C64`).
- **Track P: frame stepping (§220e).** Home has "Frame step" and "Step"; `F7` advances
  one presented frame (8M-instruction budget so an idle guest cannot hang the
  UI), `Shift+F7` advances 1,000,000 instructions. `EmulatorSession.StepFrame`/
  `StepInstructions` set a budget and release the pause gate; the loop consumes
  it in batches and re-closes the gate (compare-exchange budget so a second
  click keeps its own step). About page + README list the keys.
- **Gates:** menu 1B accurate golden PASS (sw `4EF1FBF6`, gl `B6116DAE`,
  counters 33,632/686/686, `BackendUnsupported=0`, `composedFrames=0`);
  triangle slice-2 golden PASS (sw `A1740DE0`, gl `7A860ED7`, 2,706/68/34);
  menu `--tex-filter registers` sw-vs-gl policy PASS; Nv2a 391 green, GpuGl 92
  green; format clean. New: `scripts/gpu-slice1-golden.ps1
  -TexFilter legacy|registers` (+ a generic `-Extra`), `diag[composedFrames=
  composedPixels= composedDraws=]`. Decisions §219 + §220; `compatibility.md` gaps 7/8.
**Session 2026-10-22 (twenty-third; the GPU renderer is a frontend setting + the
CHD title id). Two slices, gate currency untouched, suite 1738 -> 1757.**

- **The app can render on the host GPU (§221).** Settings → *Renderer*: software
  (default, portable) or OpenGL. `EmulatorSession` creates the headless WGL
  context on the emulation thread, attaches it in replacement mode with the
  per-flip readback on, and logs the probe line; any failure (non-Windows,
  probe, GL version) logs the reason and the session stays on software - never a
  mix, never a silent downgrade. `DetachRenderer` clears `Nv2A.Backend` and
  disposes the context in the session's `finally`.
  The display source follows the renderer: software keeps the RAM scanout,
  OpenGL shows the backend readback **only while the display controller scans a
  surface a flip presented** (a 4-entry recently-presented ring; §234), and a
  direct-framebuffer guest's own buffer is read from RAM in both modes. The
  pre-§234 `TryCopyScanoutFrameFromMemory` "until the first backend frame" latch
  is no longer the decision point.
  **Measured through the real `EmulatorSession` on gate media, 1B:** software
  34.8 s / 687 flips / 306,817 non-black px; OpenGL 24.7 s / 688 flips / the same
  pixel count, |dmean| ≈ 0.005 (2,385 of 307,200 px differ, mostly the one-flip
  offset). The app's own window was not re-verified live this session (this agent
  environment has no interactive desktop - the process starts and exits cleanly
  with no crash event); the seam that owns the feature, `EmulatorSession`, was
  driven for real.
- **One guest-memory adapter (§221a).** `Nv2AMmio.GpuMemory` exposes the
  `IGpuMemory` the device was built with, and the CLI now passes it to
  `GlBackendFactory` instead of its own duplicate `MachineGpuMemory` (deleted).
  The 1B menu GL golden re-ran byte-identically (sw `4EF1FBF6…`, gl
  `B6116DAE…`, diff 2602 px / maxDelta 1 / violations 0, counters 33632/686/686,
  `BackendUnsupported=0`).
- **CHD title id (§222).** Per-title override V2 could not resolve a CHD, so CHD
  libraries fell back to path keys. `XGrid.XboxFs.XdvdfsSectorReader` walks the
  XDVDFS volume over any `IDvdImage` (descriptor at partition sector 32, magic at
  +0x00/+0x7EC, root table at +20/+24, `14 + nameLen` entries, the `0xFFFF`
  sector-pad sentinel, the XGD disc-offset probe list), bounded so a corrupt
  table reports "no file". `DiscTitleId.TryRead` resolves `.chd` via
  `ChdDvdImage.Open` + the new `DiscTitleId.TryReadImage(IDvdImage, …)` seam.
  **Differential check on real images** (scratch, not committed): six ISOs
  (`wt-data/xemu-tri/triangle.iso` + five corpus samples) return exactly the id
  the XISOSharp path returns (`FFFF0002`). No CHD exists on this host, so the
  container hand-off itself is covered by an in-memory `IDvdImage` test, not by
  a real CHD run - keep that in mind before claiming CHD support generally.
- **Doc correction:** `docs/research-notes/xisosharp.md` said the descriptor's
  trailing magic sits at `+0x7F4` (8 bytes past the sector end); it is `+0x7EC`.
- **New diagnostic `unsupTex[...]`** on the GL line: which texture modes the
  declined replacement-mode draws asked for (slot counts). The cubemap row at 3B
  reads `unsupTex[m0C=14 m11=28]` - DOT_REFLECT_SPECULAR plus DOT_PRODUCT on two
  earlier stages - i.e. B3 method coverage needs both families, not just the
  reflection. Counters, frames and SHAs are unchanged by the diagnostic
  (cubemap frame still `DA0750EE…`).
- **Gates:** 1B accurate menu frame `4EF1FBF6…` and triangle frame `A1740DE0…`
  anchors unchanged (neither slice touches guest-visible behavior),
  `format --verify-no-changes` clean, ktest smoke PASS 7/7 at **792,800,535**
  steps with the 40,797-byte suite log `CA5D3867…` byte-identical.
  Suite 1738 -> **1757** (Nv2a +4, Frontend +3, XboxFs +9, Integration +3).

**Session 2026-10-21 (twenty-fourth; renderer policy seam, snapshot requests on
the emulation thread, frontend smoke harness - decisions §223).**

- **The renderer decision is pure and testable.** `RendererPolicy.Decide`/
  `Describe`/`Normalize` (frontend `Services/`) owns the platform policy (OpenGL
  only on Windows) and `GraphicsBackendHost.TryAttach`/`Detach` perform the side
  effects; `EmulatorSession` calls the host and logs its one-line detail. The
  setting's key space stays `EmulatorSettings.Renderer` (`software`/`opengl`),
  so there is one persisted vocabulary.
- **Snapshot save/load moved onto the emulation thread.** A load calls
  `Nv2AMmio.LoadState -> Backend?.ResetState()` (GL calls), which is illegal off
  the context's thread; requests are queued and served by the loop between
  batches (and during a pause, whose gate wait is now a 50 ms timed wait), with a
  60 s ceiling and explicit failure of requests still queued at exit.
- **Measured in-app (dashboard, fast mode, interleaved 40 s pairs):** software
  1.014B / 1.021B steps vs OpenGL 1.121B / 1.106B = **+9.8% / +8.2% guest
  throughput**, identical 306,817 non-black pixels, and a 72 MB `.xgs` round trip
  with GL attached. The CLI's 1B menu leg remains the ceiling (-30.9%).
- **`tools/frontend-smoke`** (new) drives a real `EmulatorSession` headlessly and
  prints steps/wall/frame SHA per renderer - the harness behind those numbers.
- **Gates unchanged** (host-side only); suite grows by the frontend rows.

**Session 2026-10-22 (twenty-fifth; per-title renderer, one GPU-memory adapter,
`KeyedOption` collapse - decisions §224).**

- **Per-title renderer.** `TitleOverride.Renderer` (same `software`/`opengl`
  vocabulary as the global setting) pins one title to the other renderer; the
  settings row offers `inherit` / `software` / `OpenGL`, and
  `EmulatorSettings.WithTitleOverride` merges it into the boot snapshot, so a
  title can opt into the GPU path without changing the global default.
- **`XboxMachine.GpuMemory`.** The machine publishes the `PhysicalGpuMemory` the
  NV2A itself uses; the CLI passes it to `GlBackendFactory` instead of wrapping
  `Memory` again, so the device and every backend share one GPU-writer
  suppression and present-dirty watch.
- **One option type.** `NetworkOption`/`TextureFilterOption`/`RendererOption`
  collapsed into `KeyedOption` (key + label + optional hint); the Settings XAML
  template binds `KeyedOption`.
- **Also:** the six Avalonia `AVLN5001` obsolete-`Watermark` warnings are fixed
  (0 warnings now), and the frontend tests cover the per-title renderer merge
  (`RendererSettingTests`) plus the one-adapter rule (`MachineGpuMemoryTests`).

**Session 2026-10-21 (twenty-sixth; CPU correctness campaign - five instruction
bugs, one decoder bug, three missing/mismatched shapes; gate titles unchanged).**

- **The differential was widened, and it paid off.** `tools/cpu-oracle/` went
  from 333 to **629 cases** (sign-sensitive patterns for every MMX/SSE1 packed
  op, every saturating shift count, the x87 transcendentals/environment, 16-bit
  addressing, segment overrides, `LOCK`, and string/stack edges; the test's
  memory size is now derived from the case count, `CpuOracleTests.MemorySizeFor`).
  Nine defects fell out, each fixed and pinned:
  1. **`PMINSW`/`PMAXSW` were unsigned.** `0F EA`/`0F EE` were decoded as invented
     `Pminuw`/`Pmaxuw` and compared with the *unsigned* word rule, so every
     negative lane took the wrong side. Renamed to `Pminsw`/`Pmaxsw` with a
     signed compare.
  2. **`PSRAW`/`PSRAD` count saturation** sign-filled from the low element only
     (one fill for the whole register) instead of per element.
  3. **`XLAT` (`D7`) was not decoded at all.** Added (decode + execute; the
     offset is `(E)BX + AL` with the 16-bit wrap, DS or a segment override).
  4. **16-bit addressing `[SI]`/`[DI]` ate a bogus disp16** (`mod=00 rm=4/5`),
     desynchronizing the instruction stream - only `mod=00 rm=6` is the disp16
     form. This one was a decoder bug, not a missing shape.
  5. **x87 NaN stores flipped the sign and dropped the payload** because the
     conversion went through .NET's `double.NaN` (the *negative* quiet NaN).
     `Fpu80.FromSingleBits`/`FromDoubleBits`/`ToSingleBits`/`ToDoubleBits` carry
     sign + payload; a signaling NaN survives `FLD`/`FSTP` unquieted.
  6. **`FUCOM`/`FUCOMP`/`FUCOMPP`/`FUCOMI`/`FUCOMIP` latched IE for a quiet
     NaN**; only a signaling NaN may signal on a quiet compare. Propagated to the
     spec handlers and the raw-emitter descriptor (bits 1/2).
  7. **`FSQRT` of a negative value** now sets C2 (SDM 8.3.2) with the invalid flag.
  8. **`MINPS`/`MAXPS`/`MINSS`/`MAXSS` returned the destination on equal
     operands**; the ISA returns the source, which decides the sign of a
     `+0.0`/`-0.0` pair.
  9. **Corpus hygiene:** three cases read an absolute low address another case
     had written (the CI harness reuses one image, the oracle maps a fresh one).
     Every case is page-relative now, and the rule is in the oracle README.
- **Nothing changed on the gate titles.** Both 1B accurate goldens PASS
  byte-identical (menu sw `4EF1FBF6…` / gl `B6116DAE…`, violations 0, counters
  33632/686/686; triangle sw `A1740DE0…` / gl `7A860ED7…`, counters 2706/68/34),
  the fast anchors are exact (triangle `B45254CA`/`7DAB63EB` 10.9 s, menu
  `34973510`/`F5D61C94` 32.0 s), and the ktest smoke is PASS 7/7 at
  **792,800,535** steps with the 40,797-byte log byte-identical (`CA5D3867…`).
  No snapshot version change (no device state).
- **Suite 1738 -> 1762** (CPU project 643 -> 667; 18 of the 23 new behavior
  tests fail on the pre-fix source, proven by stashing `src/XGrid.Cpu`).
  Deliberately out of scope and documented: no SSE2 (the CPU is a Pentium III,
  so the 128-bit packed-int forms must stay rejected), `FSTENV`/`FSAVE` pointer
  metadata, the reference's `0/0` ZE-vs-IE class, and `FSQRT`-negative IE.
  Details: decisions §225; caveats: `tools/cpu-oracle/README.md`.

**Session 2026-10-21 (twenty-first; GL TEXFILTER parity + replace-mode
composition + clear masks + per-title overrides V2). Four slices, every gate
anchor byte-identical.**

- **GL TEXFILTER parity (§219a, gap 7 closed).** The GL backend was nearest-only.
  `Nv2AMmio.DecodeTextureFilter` is now the *single* decode of a TEXFILTER word
  (LOD-0 tent for MIN/MAG 2/4/6/7; the effective convolution only when MIN is
  `CONVOLUTION_2D_LOD0 (7)`, §210a) used by both `SamplerPlan` and the new
  `PgraphTextureSlot.FilterBilinear`/`FilterConvolution`. The shader gained
  `SampleNearest`/`SampleTent`/`SampleConvolved` (transcriptions of
  `SampleBilinear`/`SampleConvolved`, same tap step, per-tap wrap), dispatched
  by per-slot uniforms `uTexFilter`/`uTexConv` - not the program key, so nothing
  re-compiles and the default (0) path is the same `texelFetch`. The GLSL string
  goldens were regenerated. `filters=2/2/0` on the triangle means the tent path
  runs there under `--tex-filter registers`, and `BackendUnsupported=0`.
- **GL replace-mode composition (§219b, gap 8 closed).** `TryDraw` returning
  false in replacement mode now sets "software painted since the last flip"; the
  flip drops the FBO and the existing present-upload path rebuilds the whole
  presented surface from guest RAM (all-or-nothing, never a mix). New diagnostic
  `composeUploads=`. Measured on `xgu-bump_cubemap_reflect` 3B
  `-Gpu gl -GpuReplace`: `unsupported=14` (unchanged), `composeUploads=7`,
  `presentUploads=7`, presented frame **`DA0750EE…` = the software anchor**
  (was clear-only), 10.4 s.
- **A clear ignores the draw write masks (§219c).** `Nv2AMmio.FillSurface` writes
  the clear value verbatim, but GL's `glClear` honors `glColorMask`/
  `glDepthMask`/`glStencilMask`. `ClearCore` now lifts all three for the clear
  and invalidates the state shadow so the next draw re-uploads them.
  `ClearMaskLiveTests` (live GL) **fails on the pre-fix build** (masked clear
  wrote nothing) and also proves the masks are restored; two
  `Nv2aZetaClearLayoutTests` cases pin the software rule.
- **Per-title overrides V2 (§219d).** `XGrid.Host.DiscTitleId` reads the XBE
  certificate title id from a loose `.xbe` or an ISO's root `default.xbe` (a CHD
  reports false - no XDVDFS reader for it yet - and the caller keys by path).
  `EmulatorSettings` gained `TitleIdKey`/`TryParseTitleIdKey`/
  `CanonicalTitleKey`, `FindTitleOverride(path, titleId)` (title key wins, path
  falls back), canonicalization in `Normalize()` (unparsable title keys are
  dropped), the settings row text (`NAME (Title XXXXXXXX)` / "applies wherever
  this title id is launched"), a power-on log line
  `Title XXXXXXXX · NAME (per-title override applied)`.
  CLI `xgrid disc title <path>` prints the id and the `title:XXXXXXXX` key.
  Caveat: homebrew images that never set a certificate id share the
  placeholder `0xFFFF0002` (every nxdk/xgu sample), so a path-keyed row is
  still the way to tell two such images apart.
- **Gates:** accurate 1B menu slice-1 golden PASS (sw `4EF1FBF6…`, gl
  `B6116DAE…`, diff 2602 px / maxDelta 1 / violations 0, counters
  33632/686/686) and triangle slice-2 golden PASS (sw `A1740DE0…`, gl
  `7A860ED7…`, diff 27849 / violations 0, counters 2706/68/34), both with
  `BackendUnsupported=0` and `composeUploads=0` (mirror mode never composes).
  Suite 1717 -> **1738**; format clean.
**Session 2026-10-21 (twentieth; gap 6 lighting + PTIMER 0.8.137 + APU XGSCNT +
two Track P features). Four landed slices; frames, CPU SHAs and every
counter stayed exact, and the two menu **RAM** anchors were re-recorded.**

- **Fixed-function lighting (215).** The enabled-light loop is in:
  `FixedFunctionVertex.ApplyLight` runs infinite/local/spot with range
  rejection, `1/(a0 + a1*d + a2*d^2)` attenuation, the spot cone bands and the
  `LOCALEYE` view vector; `SpecularPowerCurve` transcribes
  `reconstruct_specular_power` (low-power shortcut, nine-segment fit with the
  growth-rate variant, half-power extension, positive-coefficient guard);
  `FixedFunctionVertexState` latches all eight lights (`SET_LIGHT_*` window,
  0x80-byte stride, four-word spot direction) and the six
  `SET_SPECULAR_PARAMS` words. Snapshot v6 -> v7. **No measured leg enables a
  light** (`fixedFnLights=0` everywhere), so every gate is byte-identical:
  triangle 1B `B45254CA`/`7DAB63EB` (57,663,082 px; `--ffp` 78,792,545 px),
  menu `51B56687`/`F5D61C94` (253,998,498 px; `--ffp` `87BF40DA`, 375,569,329).
  +18 tests (`Nv2aFixedFunctionLightingTests`).
- **PTIMER (216).** The 0.8.137 alarm model: 61-bit register time with a
  writable offset (`TIME_0`/`TIME_1` writes), numerator/denominator reset 1/1,
  `ALARM_0` (0x420) armed with `get_next_alarm_time` (one 2^32 roll when not
  strictly future), the alarm's own `INTR_0` bit with re-arm, the unconditional
  read-latch, the immediate raise on enable, and `NV_PMC_INTR_0_PTIMER`
  (bit 20) in the aggregate. Polled from `Advance(coreTicks)` (the core clock is
  the clock source). Snapshot v7 -> v8. Both 1B gates byte-identical again.
  +14 tests (`Nv2aPtimerTests`).
- **Track P (217).** `--ffp` and `--tex-filter registers` are now reachable from
  the Avalonia Settings page (CLI defaults preserved), per-title overrides
  (V1 keyed by image path, `WithTitleOverride` merge + a Settings list with
  `ffp`/`filter`/`inherit` toggles) and **persistent EEPROM state**: the guest's
  user settings live in `<data dir>/eeprom.bin` while the configured firmware
  image stays a read-only seed; `--eeprom-out <path>` exports the live image
  from the CLI. New `tests/XGrid.Frontend.Tests` project (15 tests) covers the
  settings model and the EEPROM store; `MachineEepromTests` (+3) and
  `SmbusTests` (+2) cover the host/device copy path.
- **APU `NV_PAPU_XGSCNT` (218).** Offset 0x200C now returns
  `_epFrameDiv * 32` instead of the guest-written word (reference
  `apu.c` + `apu_regs.h`: a 48 kHz sample counter, read-only). +3 tests
  (`ApuFrameCounterTests`). **Anchor effect measured:** the 1B dashboard
  reads it during audio init and stores the value, so its RAM image moves
  while nothing else does - accurate menu RAM `01BE5D64` -> `6AB39AB0`
  (CPU `3D2740B8`, frame `4EF1FBF6`, counters 33632/686/686 all identical),
  fast menu RAM `51B56687` -> `34973510` (CPU `F5D61C94`), triangle
  unchanged (`812EA159`/`2F632483`/`A1740DE0`), ktest suite log
  byte-identical (`CA5D3867`, 792,800,535 steps). Precedent for a RAM
  re-record: 206g/109; frames, CPU SHAs and counters are the invariants.
- **Suite 1662 -> 1717 tests** (218 adds 3), `boot-to-first-frame.ps1` gained
  `-TexFilter legacy|registers`. Gate evidence this session: menu slice-1
  accurate golden PASS (sw `4EF1FBF6.`, gl `B6116DAE.`, violations 0,
  `BackendUnsupported=0`, counters 33632/686/686), triangle slice-2 golden
  PASS (`A1740DE0.` / `7A860ED7.`), ktest smoke PASS 7/7, fast 1B anchors
  exact on both titles (with and without `--ffp`); the menu RAM numbers in
  the 215 bullet are pre-218.

**Session 2026-10-21 (nineteenth; compatibility gap 6 closed behind `--ffp`, §213).
The fixed-function vertex stage exists, both `xgu *_ffp` corpus rows render their
scenes, and the gate titles stay byte-identical.**

- **What was missing, measured.** `xgu distance_fog_ffp` and `xgu spheremap_ffp`
  set `SET_TRANSFORM_EXECUTION_MODE` to FIXED (0) and upload matrices into the XF
  constant context instead of loading a program; XGrid ran whatever program was
  resident, so both rendered flat backgrounds. The same mode is used by the EvoX
  dashboard: **22,207** of its 33,632 draws per 1B steps are fixed-function (the
  triangle: 39, all lit).
- **Landed:** `FixedFunctionVertex` + `FixedFunctionVertexState` (transform,
  1..4-matrix skinning with the summed-weight modes, normalize, all six texgen
  modes per stage/channel, texture matrix, texture-matrix enable, color/specular
  selection, eye-space fog distance, the `clampAwayZeroInf`/1-16-truncation
  position epilogue) transcribed from `xemu 0.8.137 vsh-ff.c`; the register
  decodes (`SET_LIGHT_CONTROL`, `SET_COLOR_MATERIAL`, `SET_LIGHTING_ENABLE`,
  `SET_SKIN_MODE`, `SET_NORMALIZATION_ENABLE`, `SET_SPECULAR_ENABLE`,
  `SET_LIGHT_ENABLE_MASK`, `SET_TEXGEN_S/T/R/Q`, `SET_TEXTURE_MATRIX_ENABLE`);
  `FixedFunctionDraws`/`FixedFunctionLitDraws` diagnostics; snapshot v5 -> **v6**;
  CLI `--ffp`; `boot-to-first-frame.ps1 -Ffp`.
- **It is opt-in on purpose.** Mode-0 draws used to run the resident program, so
  the flag changes the dashboard's picture (its menu background *is* that layer -
  the scanout leg shows the clean EvoX background). Measured 1B H1: menu flag-off
  RAM `51B56687` / CPU `F5D61C94`, 253,998,498 px, 30.9 s; flag-on RAM
  `87BF40DA` / **CPU `F5D61C94`**, 375,569,329 px, `fixedFn=22208`, 59.0 s.
  Triangle flag-off and flag-on **both** RAM `B45254CA` / CPU `7DAB63EB` with the
  gate frame `A1740DE0` byte-identical (only host counters move). No oracle frame
  exists for the dashboard's fixed-function layer, so the default stays off.
- **Blast radius measured on the whole corpus** (13 rows, 1B, `-Corpus` vs
  `-Corpus -Ffp`): every row's RAM moves (the boot animation is FFP-drawn) and
  pixels go 42.0M -> 63.1M at +3.5-4 s, while **every CPU SHA and every
  frame/scanout/display SHA stays identical**; the flag-off sweep reproduces
  every documented row anchor (`gamma` `4EE18F78`, `hello` `4650454F`, `led`
  `3D1F72DB`, `mesh` `D6A41BAC`, `xaudio` `B0C863C5`).
- **Corpus rows (3B, BTFF):** `spheremap_ffp` 2 colours -> **7,358** (mean
  (27.8, 16.3, 163.1), 4.9% black), RAM `B5747761` -> `8062CFB5`, CPU unchanged
  `F0075C2F`; `distance_fog_ffp` 26 -> **7,601** colours (mean (129.4, 50.2,
  37.4)), RAM `AFF08085` -> `12BAD517`, CPU unchanged `571363B4`.
- **Lighting was the remaining slice and is now half landed, counted not faked:**
  every lit draw seen so far runs with all lights off and specular off, so the
  material/ambient/emission chain is implemented (below) and the enabled-light
  loop (three light types, `reconstruct_specular_power`) stays filed in §213b,
  counted in `fixedFnLights=`.
- **Lighting: the material chain landed, the light loop did not.** Lit draws now
  run the reference's ambient/emission chain (`SET_SCENE_AMBIENT_COLOR`,
  `SET_MATERIAL_EMISSION`, `SET_MATERIAL_ALPHA`, the three-way source selections
  and the alpha rule); a lit draw with an *enabled* light keeps the chain without
  the light terms and increments `fixedFnLights=`. Every lit draw measured so far
  has all lights off (`fixedFnLights=0`), so the landing is behavior-neutral on
  every gate and corpus leg - verified: triangle 1B `--ffp` RAM/CPU/frame still
  `B45254CA`/`7DAB63EB`/`A1740DE0`, menu `--ffp` still `87BF40DA`/`F5D61C94`
  (frame `C5B734A2`), both `xgu *_ffp` rows identical.
- **Gates:** accurate 1B menu `01BE5D64`/`3D2740B8` + frame `4EF1FBF6`, triangle
  `812EA159`/`2F632483` + frame `A1740DE0`; fast menu `51B56687`/`F5D61C94`
  (rasterPsh 10.75 s), triangle `B45254CA`/`7DAB63EB`; GL slice-1 `PASS`
  (gl `B6116DAE`, violations 0, `BackendUnsupported=0`) and slice-2 `PASS`
  (gl `7A860ED7`); ktest smoke **PASS 7/7** at **792,800,535** steps with the
  40,797-byte log `CA5D3867` byte-identical; suite 1625 -> **1662** (+33 FFP,
  +4 zeta); format clean.
- **§214: the Z24S8 clear-word layout is a verified no-bug**, not the §211f
  suspect: every consumer reads depth = bits 8-31 / stencil = bits 0-7, now
  pinned by `Nv2aZetaClearLayoutTests` (synthetic stencil differential: ref
  `0x74` draws, ref `0x00` rejected, zpass REPLACE writes the low byte and keeps
  the depth field). The gate titles' `0xFFFFFF00` clear agrees on depth under
  either reading, which is why only a synthetic test could settle it.

**Session 2026-10-21 (eighteenth; compatibility gap 10 diagnosed, §211). The
`bumpenv_ripple` black frame is a **state** answer, not a rasterizer bug - and the
one change landed is a convolution fix on linear texture stages.**

- **Diagnosis first, and it exonerates the renderer.** Replayed from the presented
  surface back to the guest's method stream with temporary flip/draw/fragment
  probes (all removed before committing; `git status` shows only the two intended
  files). Chain: all **216** presented surfaces are uniformly `FF000000` (the
  colour clear); each frame issues exactly **two** 4-vertex `Arrays` quads with
  vertex program `0+18` that writes only oPos and oT0/oT1 (**no oD0**), under a
  one-stage combiner that sets **`V0 = T1`** with final **`D = V0`** - so the
  visible colour *is* the stage-1 texel. Draw A (`m0100`) never resolves stage 1
  (mode `NONE`), so `T1` keeps `(0,0,0,1)`: black by construction, as in the
  reference. Draw B (`m1600`) is BUMPENVMAP and its math is faithful
  (`dsdt = (sign3(t0.b), sign3(t0.g))` off the `0x28` map, matrix
  `(0.062,0,0.062,0)`), but the perturbed coordinate at the quad's right edge
  (`1.0054, 0.9938`) clamps onto the logo texture's `FF000000` edge texel.
- **The zeta clear is what hides the geometry.** The app clears zeta to
  `0x00711174` (depth ~0.44 low-24-bit, ~0.0017 under the reference's `>> 8`
  layout) with `LEQUAL` while every fragment is at z ~ 0.5, so nothing passes.
  Forcing the clear far **or** disabling the depth test yields the identical
  frame - **406 colours, 96.9% black, mean (5.5, 4.2, 2.3)** - i.e. the plane's
  screen footprint (a wedge), not the reference's full-screen reflection. Culling
  additionally drops one triangle of each quad (pixels 42.0M -> 73.5M with culling
  off, frame still black).
- **Filed, each needing its own differential:** the Z24S8 clear-word layout
  (`pgraph_get_clear_depth_stencil_value` puts the stencil in the **low** byte,
  XGrid in the high byte; the gates clear `0xFFFFFF00` where the stored depth
  agrees and the stencil does not), the cull winding on those quads, and the app
  state the reflection needs (gap 9's second-emulator frame).
- **Landed: §212, the convolution tap step on linear (LU_IMAGE) stages.**
  `SampleConvolved` added `1/Width` in normalized units on every stage, i.e.
  `1/Width` of a texel where a linear stage's coordinate is a texel index, so all
  nine taps read one texel. One texel per tap now (`psh.c
  apply_convolution_filter` normalizes a rect coordinate first). Test
  `TextureFilterRegisters_ConvolutionOffsetsOneTexelOnLinearTexture` plus a new
  linear harness: an impulse centre reads **195 pre-fix -> 64 post-fix** (255 *
  4/16), and a kernel field next to MIN 1 still reads sharp. Only reachable under
  `--tex-filter registers` with MIN 7, so no anchor can move.
- **Gates (landing commit):** accurate 1B menu `01BE5D64…`/`3D2740B8…` + frame
  `4EF1FBF6…`, triangle `812EA159…`/`2F632483…` + frame `A1740DE0…`; GL policy
  frames `B6116DAE…`/`7A860ED7…` with `violations=0 maxDelta=1
  BackendUnsupported=0` and identical counters (`33632/686/686`, `2706/68/34`);
  fast 1B menu `51B56687…`/`F5D61C94…` (31.16 s, `rasterPsh` 10.84 s) and triangle
  `B45254CA…`/`7DAB63EB…` (10.49 s); ktest smoke **PASS 7/7** at **792,800,535**
  steps with the 40,797-byte log `CA5D3867…` byte-identical; suite 1624 ->
  **1625**; format clean. `gpu-slice1/2-golden.ps1` still default to
  `wt-data\xbox_hdd.qcow2`, which does not exist on H1 - pass
  `-Hdd wt-data\chain-199l\hdd.qcow2`.

**Session 2026-10-21 (seventeenth; the TEXFILTER convolution gate + compiled tent
fill, §210). The "2.4x filter cost" was a misapplied blur, and fixing it made the
flag free and invisible on both gate titles.**

- **Measured before building.** Option A was "teach the compiled fills the
  bilinear tap". A 1B menu leg with `--tex-filter registers` printed
  `filters=2/2/1` - MIN = TENT_LOD0 **and** a kernel field - and the reference
  only convolves when MIN is `CONVOLUTION_2D_LOD0` (7) (`xemu
  hw/xbox/nv2a/pgraph/glsl/psh.c:197-210`, filter map `gl/constants.h:26-36` +
  `gl/texture.c:126-141`). The dashboard and `xgu bump_cubemap_reflect` both
  program that pairing, i.e. the reference samples them as a **plain tent**. The
  pre-fix build convolved them (36 taps/fragment): menu 1B wall **79.87 s**,
  `rasterPsh` 57.84 s, 261.3M px, frame `AF991769…`. With the gate:
  **33.34 s / 11.76 s / 253,998,498 px / frame `4EF1FBF6…` = the legacy anchor**.
  So §209's "8.7% of pixels change" and "2.4x" were the blur, not the tent.
- **Both compiled fills now serve the tent** (§210b): the predicate drops
  `!Bilinear` (a convolution still declines), the pixel loop branches once per
  fragment, and the tent branch is `SampleBilinear` transcribed through one
  shared `ReadTapFast` tap reader (per-tap fold, linear/Morton/interleave
  address, the per-fill direct-RAM lane and `DecodeKind`, the same weights, the
  raw-coordinate `TrackTextureCoords`, one `CountSample` on the base tap).
  Evidence it is the same arithmetic: the `registers` leg reaches
  `compiled=429350/277015865` - identical to the legacy leg - and its RAM SHA
  equals the gate-fix-only build that served those draws through the generic
  loop. 1B menu `registers` **32.18 s** vs legacy **32.22 s**; triangle
  10.53 → **11.66 s** (four taps on the k2/k3 shape, `rasterPsh` 4.28 → 5.05 s),
  RAM/CPU identical to legacy.
- **The flag now moves no gate anchor.** Triangle: RAM `B45254CA…`, CPU
  `7DAB63EB…`, frame unchanged. Menu: flipped frame byte-identical to
  `4EF1FBF6…` (`19FEA0EA…`/`F5D61C94…` RAM/CPU; only the double-buffered back
  surface differs) because both titles sample at texel centers, where a tent is
  a nearest tap. The corpus rows with fractional texcoords are where the flag
  does change pixels, and both gain colour depth at an unchanged mean:
  `bump_cubemap_reflect` at 3.6B (57.0, 97.7, 30.2), colours **20,843 → 26,347**
  vs the reference's 30,806, and `vp_lighting` at 3B (33.9, 28.1, 226.4),
  **1,592 → 2,405** (its legacy leg also re-verifies that row's anchors
  `162DAFF1…`/`0380C502…`, unchanged).
- **Tooling:** `scripts/raster-leg.ps1 -TexFilter legacy|registers` (the leg
  previously required a hand-built CLI line and silently mis-measured when the
  flag was passed as one string) and a METRIC `filters=` field;
  `scripts/ktest-smoke.ps1` now reuses the stored §120 xemu oracle log under
  `-SkipXemu` instead of failing on a fresh work dir (and the new parameter had
  to avoid the name `$XemuLog`, because PowerShell variables are
  case-insensitive - the same trap §207 recorded).
- **Gates:** accurate 1B menu `01BE5D64…`/`3D2740B8…` + frame `4EF1FBF6…`,
  triangle `812EA159…`/`2F632483…` + frame `A1740DE0…` (all exact), fast legs
  exact, `vec=0`; ktest smoke PASS 7/7 at **792,800,535** steps with a
  byte-identical 40,797 B suite log; tests 1621 → **1624** (+3, two of them
  fail on the pre-change source).
- **Open for the operator:** the flag's default. It is now cheap and invisible
  on the gates, and the cubemap row's colour depth moves toward its reference,
  but the menu still has no oracle (`next-session.md` Option B).

**Session 2026-10-21 (sixteenth; cubemap reflection closed, TEXFILTER landed, §209).**
**The last xgu oracle failure now renders its reflection, and the sampler reads
the filter registers - the previous session's diagnosis was one gate short.**

- **The bug was the face extent, not the direction chain.** `TrySampleTextureCube`
  took the face size from `TEXIMAGERECT`; xemu takes a **swizzled** texture's
  extent from TEXFMT `BASE_SIZE_U/V` and reads TEXIMAGERECT only for linear
  formats. This guest never programs the rect, so the stale 512x512 default made
  the sampler walk 1 MB face strides over 256 KB faces. With that fixed plus the
  three format gates, the row goes from **2 colours / 36% black** to **21,041
  colours / 0% black / `|dmean|` (5.3, 2.4, 1.3)** at its best phase - on par
  with `vp_lighting`.
- **Probe set (temporary, removed before commit):** forcing `n = +Z`, dumping
  the normal map, `TEX1`, `n` and `rv` all produce smooth images, so the
  `DOT_RFLCT_SPEC` chain (`xemu psh.c:1285-1304`) is faithful and §208g's
  "striped = direction chain" reading is disproven. The residual hatch in a
  3.6B frame is the environment map at a grazing view.
- **The oracle needs a phase sweep here too.** Scores at 3.0/3.2/3.4/3.6/3.8/
  4.0/4.2/4.4B: 106.5/81.5/47.5/**27.5**/66.4/81.7/69.1/44.9. 4.4B is the best
  *structural* match (the reference's frontal plate).
- **TEXFILTER was stored and ignored.** `filters=` shows `2/2/1` (tent +
  quincunx) on the menu's stage 0/1 (stage 0 alone = 211M of 267M texel
  samples/1B) and on all four stages of the bump sample. New opt-in
  `--tex-filter registers`: `SamplerPlan.Bilinear` (MIN/MAG tent at LOD 0,
  per-tap wrap, border taps) and `SamplerPlan.Convolution` (nine textureProj
  taps, 1-2-1 gaussian, offsets / `w`), cube lookup face-local clamped,
  compiled fills fall back to the generic loop when filtering is active.
  Measured on the menu: 8.7% of pixels change (mean |delta| 2.3/2.1/2.0,
  colours 32,360 → 39,254), **CPU SHA unchanged** (`F5D61C94…`), wall 34.9 →
  85.6 s (perf follow-up: teach the compiled fills the bilinear tap). Default
  `legacy` is byte-identical: fast menu `51B56687`/`F5D61C94`, accurate goldens
  menu `4EF1FBF6`/`B6116DAE`, triangle `A1740DE0`/`7A860ED7`, ktest 7/7.
- **Tests 1605 → 1615** (3 byte-order cube cases + BASE_SIZE extent + 5 filter
  tests + a compiled-modulate colour guard; 4 of the cube ones fail on the
  pre-change source). The plain triangle leg (no `--frame-first`) reads
  `CB95C006…`/`B8EF1021…` both pre- and post-change on this host - it is a
  different leg from the documented `B45254CA…` row, not a regression.
- **Corpus widened to 18 rows over 14 samples:** `D:\xgu\swizzle_texture`
  builds with the §207 recipe (remember `$NXDK_DIR/bin` on PATH, or `make`
  fails with `nxdk-cc: No such file or directory`) and renders a textured XGU
  cube; it samples `0x3A` 3.14M times with zero misses, so it is a second
  regression row for the byte-order/swizzled path. The xgu repo ships no
  screenshot for it, so it has no oracle verdict.
- **`0x28`/`0x29`/`0x17` (G8B8/R8B8) decoded (§209g).** `bumpenv_ripple`
  sampled `0x28` 3.92M times and every sample missed; the decode now matches
  the reference (`GL_RG8` + the `(R, G, R, G)` swizzle, `0x29` is
  `(G, R, R, G)`). **That row still renders a black frame** at 5.0-5.6B, and its
  documented oracle delta turned out to be measured on the black frame - it is
  compatibility gap 10 now, not a match. Tests 1615 -> 1621.

**Session 2026-10-21 (fifteenth; display model + a measurement correction, §208).**
**The console now shows what the display controller scans out, and one
"triangle" performance leg turned out to be the boot animation.**

- **`NV_PRMCIO` is decoded.** XGrid stored the VGA I/O window the guest programs
  the video mode through and ignored it. It now models the CRTC index/data
  latch (with the CR0-7 write protection from CR11 bit 7), plus the sequencer,
  graphics and attribute latches, and derives the scanout geometry exactly like
  the reference: base = PCRTC START, pitch = `(CR13 | CR19[5-7]<<3 |
  CR25[5]<<6) << 3` (xemu `nv2a_get_params`), size `(CR01+1)*8` by
  `(CR12|CR07 bits)+1` (QEMU `vga_get_resolution`), depth from CR28 with 15-bit
  vs RGB565 from PRAMDAC general control bit 12.
- **The trap is worth remembering:** the first version inferred which port a
  write addressed by diffing the stored word, and silently swallowed
  `WriteCrtc(0x11,0)` after `WriteCrtc(0x07,0)` - two registers can share a data
  byte. The bus knows the exact lanes; `Nv2AMmio.Write` now passes them.
- **`TryCopyDisplayFrame`** returns the scanout (backend readback when a GPU
  backend is attached, flipped-surface fallback before a mode exists). CLI
  `--frame-display`; the Avalonia frontend samples it at ~30 Hz, so a guest that
  never flips (nxdk `gamma`, `hello`/`debugPrint`) finally shows its output.
  **`--frame` still exports the last flipped surface**, so every frame anchor
  stays valid. Snapshot v4 -> v5.
- **Evidence.** Accurate 1B gates exact (menu `01BE5D64`/`3D2740B8`/`4EF1FBF6`,
  triangle `812EA159`/`2F632483`/`A1740DE0`), fast menu `51B56687`/`F5D61C94`
  exact, `vec=0`, ktest smoke PASS 7/7 at 792,800,535 steps with the 40,797-byte
  log byte-identical. The CRTC export reproduces the old surface-geometry
  scanout bytes on six legs (`gamma` `D3EBF420`, `hello` `72D33DC2`, `led`
  `18C7CC83`, `mesh` `7328A99F`, menu `51658B3D`, triangle `AB50EF68`), and the
  flip/display buffers differ by exactly one 640x480x4 page (`0x12C000`).
  Suite 1596 -> **1605** (nine `Nv2aDisplayFrameTests`).
- **Correction (§208d):** `raster-leg.ps1 -Workload tri` and
  `present-path-acceptance.ps1` used **DVD only**, which per §207b never launches
  the disc's XBE - they measured the boot animation (2,673 draws / 35 frames /
  42.3M px, RAM `DD1D516E`) under a triangle's name. Gate media is HDD+DVD:
  2,706 / 68 / 57.7M, RAM `B45254CA`, CPU `7DAB63EB`. A stashed pre-change build
  reproduces both pairs, so this is media, not a regression. Corrected walls on
  H1 at 1B fast: **triangle 11.3 s sw / 7.5 s `gl+replace`** (was "8.0 / 6.4"),
  menu unchanged. The old triangle 6 s M-A target is re-based on a workload that
  was never being measured - operator decision before anyone chases it.
- **Corpus: 17 rows over 13 samples (§208h).** Built three more xgu examples
  (`bumpenv_ripple`, `distance_fog_ffp`, `spheremap_ffp`). `bumpenv_ripple` runs
  its own scene from ~5B; the two `*_ffp` samples render flat because
  **`XGU_FIXED` (fixed-function transform) is not implemented** - compatibility
  gap 6, a large feature upstream of both renderers. `alpha_blending` is NOT a
  gap: its shader transparency ping-pongs, and at the right phase (4.6B) XGrid
  is within 4.8/255 of the reference (§208f). New `scripts/frame-stat.py` and
  `scripts/frame-oracle.py` make animated samples measurable.
- **Cube maps: three gates, not one (§208g).** Sampling needs
  `IsSupportedSwizzledFormat`, `SamplerPlan.CubeBytes` *and*
  `TryDecodeSwizzledTexel`; §207h moved only the first, which is why it looked
  like "no pixels changed". With all three the cube samples (frame |dmean|
  59.6/43.3/11.5 -> 47.2/14.1/7.2) but the reflection is striped, so the partial
  path was reverted - the remaining error is the DOT_REFLECT_SPECULAR direction
  chain.
- **Build carry-over (not mine, not committed):** the frontend csproj is on
  Avalonia 12.1.4 from a parallel session; `FileDialogService.CopyTextAsync` was
  migrated to `DataTransfer`/`SetDataAsync` locally so the solution builds. Leave
  both uncommitted unless that session lands its own version.

**Session 2026-10-20 (fourteenth; Track E corpus unblocked, §207).**
**M-B is met: 14 rows over 10 samples, every row reproducible.** The blocker was
a toolchain that had never been installed, not the emulator.

- **Toolchain landed** (`scripts/build-nxdk-samples.ps1`): MSYS2 + clang/lld/
  **llvm**/bison/flex/cmake, nxdk `a6c31ee`. Two traps worth knowing:
  `mingw-w64-x86_64-llvm` is **mandatory** (nxdk links with `llvm-dlltool`,
  which clang does not ship, and pacman lists llvm only as optional), and
  msys2's own git cannot reach the network here — clone with the Windows PATH
  git, build inside MSYS2.
- **Harness generalized**: `-Corpus` (every staged ISO), `-IsoPath`, 1B corpus
  budget. Two measured corrections: 400M steps leaves every sample in the boot
  animation (identical counters look like a harness bug), and **corpus rows need
  HDD + DVD** — DVD-only runs never launch the disc's XBE.
- **Rows**: `mesh` renders its own content (66 flips, 2,864 draws, 48,627
  triangles culled, 44 programs); `gamma`/`gamma-fade`/`hello`/`led`/`xaudio`
  boot but export the boot animation; `xaudio` proves the APU path is live
  (`voiceOff=1`, 135 reads / 203 writes).
- **Fidelity item chosen by the corpus (§207h): byte-order texture formats.**
  `xgu vp_lighting` rendered 3 colours while issuing 3.4M fragments; the texture
  line said `3F=3420787/3420787` — format `0x3F` (`LU_IMAGE_A8B8G8R8`, defined
  only in the newer xemu fork) sampled 3.42M times with **every** sample missing.
  `0x3A`–`0x3C` and `0x3F`–`0x41` are now decoded as the same 32-bit words with
  permuted channels; the non-obvious part was forcing `DecodeKind` to the generic
  path, since its default 0 means "A8R8G8B8" in the compiled fills and would have
  swapped red/blue silently. Result: misses `3.42M -> 0`, textured `+3.42M`, frame
  **3 colours -> 1,592, full coverage**. Both gate frames byte-identical.
  **1596 tests** (+20). Corpus rows now 14 over 10 samples, including three
  `xgu` examples (`vp_lighting`, `alpha_blending`, `bump_cubemap_reflect`).
- **Reverted, not shipped:** enabling `0x3A` cube maps removed 896k mode
  fallbacks and changed **zero pixels**, so it went back out until there is an
  oracle for cube direction mapping.
- **Top corpus-driven gap found by measuring:** *direct-framebuffer output is
  not what `--frame`/the frontend present*. `CapturePresentedFrame` snapshots the
  3D color surface at flip time, so `XVideoGetFB`/`debugPrint` programs produce no
  captured frame — four samples with four different RAM SHAs exported the
  identical frame. The pixels were never missing: `TryCopyScanoutFrame` +
  `--frame-scanout` (new) read PCRTC START and recover them — `gamma` goes from
  1 colour to **768 colours / 298,744 px**, `hello` shows its text (302 px), and
  `led` is the correct negative control (scanout == flipped frame). Every corpus
  row now carries a discriminating `scanoutSha`. **Not made the default:**
  flipping titles differ between the two buffers (`mesh` 8,695 vs 8,548
  colours), so switching needs xemu's display model (`pcrtc.start/4` + VGA CRTC
  line offset) and a deliberate gate re-record. Both gate frames re-verified
  byte-identical after the refactor (`A1740DE0`, `4EF1FBF6`).
- Also: `data/test-samples/nxdk-mesh` was missing `verts.h`/`texture.h` and could
  never build (`data/` is gitignored, so nothing noticed); the corpus now builds
  from nxdk upstream. New diagnostic: the `Frame` line prints `pcrtc=`.
- PowerShell gotcha recorded: variable names are case-insensitive, so a
  `foreach ($iso …)` loop variable collided with a `[string]$Iso` parameter and
  silently coerced to a string (empty sample names, same media re-run).

**Session 2026-10-20 (thirteenth; environment governance + Track P product slice, §206).**
**Two machines became a first-class concept, and the frontend stopped being
keyboard-only and stateless.** Operator decision: `References2/` is the canonical
**and only** reference store — `D:\xgrid-refs-restore\` is retired, no out-of-tree
backup is promoted. Detection replaces duplication: `scripts/refs-manifest.ps1`
+ `docs/refs-manifest.json` (9 sets, 43,153 files, PASS) checks integrity from a
checksum list that lives in git, so it survives a wipe of the folder it describes.

- **Host dimension added** (`docs/hosts.md`, `scripts/host-doctor.ps1`): semantic
  anchors (SHAs/counters) must match on every host — a mismatch is a determinism
  bug, never an "environment moved" note; performance anchors (walls) are
  host-local and only same-window A/B travels. **H1 verified as the reference
  host**: all four 1B anchors reproduce exactly (`F57934BF…`/`CF79FC17…`
  triangle 9.56 s, `660CA11B…`/`F5D61C94…` menu 39.08 s, `vec=0`, no fault), so
  §199l's SHA drift was not a live bug here. Walls read +10%/+17% vs the recorded
  quiet references — read ceilings as ratios on this host. The §204-close
  62.6 s / 14.0 s reading did not reproduce; treat it as a transient.
- **Track P landed (XInput + save states).** `XGrid.Input` (previously orphaned)
  now holds `HostPad`, the **pure** `XInputPadMapping.Map` and a
  `NativeLibrary`-loaded `XInputGamepadReader` (xinput1_4 -> 1_3 -> 9_1_0, no
  `DllImport`, never throws when absent). Digital bits pass through (XInput's low
  byte is the XID mask); A/B/X/Y and LB/RB map to the analog bytes; triggers use
  the 30/255 threshold; sticks use **radial** dead zones so diagonals survive. The
  frontend polls at 60 Hz, merges with the keyboard (OR digital / max analog) and
  is gated on window activation. Save/load: `EmulatorSession` publishes the live
  machine and takes `_machineLock` around each batch, so `.xgs` save/load observe
  a quiescent machine; Home has buttons, F8/F9 and a save picker. No new
  dependency. **1567 tests** (1538 + 29), format clean, app launches.
- **Host audio is NOT last-mile wiring** — `McpxApuDevice` has no PCM sink ("its
  FIFO drain needs an S/PDIF sink"), so it needs real output emulation and has no
  oracle. Scoped as a fidelity feature, not a Track P item.
- **Environment hazard, third strike:** frontend builds failed with
  `CreateAppHost`/`BadImageFormatException: Image is too small` and a locked
  `.pdb`, both cleared only by deleting the project's `obj`/`bin`. That is
  Dropbox touching build outputs mid-build. Strongest argument yet for moving
  this tree out of the sync root.
- **NV097 backface culling landed (§206g) — the first fidelity item chosen from
  measured usage.** A Halo v1.09 benchmark probe produced the missing method
  histogram (188,435 methods, 447 distinct); the top of the stream is the path
  XGrid already implements, and the largest *missing* state was culling. The
  same probe on `triangle.xbe` proved the **gate sample itself** uses it
  (72 enables / 52 cull-face / 1 front-face) — so this was correctness, not a
  title quirk. `SET_CULL_FACE_ENABLE`/`SET_CULL_FACE`/`SET_FRONT_FACE` now latch
  and `ShouldCull(area)` runs in both fill paths (screen space is Y-down, so CW
  has positive signed area). Controlled pre/post A/B on both gate legs: **RAM
  moved, every CPU SHA and both gate frames byte-identical** (triangle
  `A1740DE0`, menu `4EF1FBF6`). Raster effect on the same runs: triangle pixels
  **89.1M → 57.7M (-35.3%)**, menu 285.5M → 253.7M (-11.1%), 41,035 triangles
  culled per 1B. Fast RAM anchors re-recorded (triangle `DD1D516E`, menu
  `51B56687`); snapshot version 3 → **4**; `culled=` added to the NV2A
  diagnostic line; six new tests. **1573 tests.**

> **Next session starts here:** `docs/research-notes/next-session.md` — verified
> start state + the H1 anchor table, what §206 landed (references as a single
> verified store, the host dimension, XInput + save states), what §205/§204 landed
> before it, where the remaining wall is, the all-loaded order, measured
> negatives, validation ceremony, commands and diagnostics. **Run
> `scripts/host-doctor.ps1` first on every machine.**
>
> Frontend follow-ups (Track P): `docs/research-notes/avalonia-frontend.md`.
> GPU reference orientation: `docs/research-notes/gpu-reference-map.md`; NV097
> method coverage: `docs/research-notes/nv097-coverage.md`; **measured usage:
> `docs/research-notes/usage-histogram.md`** (pick the next fidelity item there).

**Session 2026-10-20 (twelfth; Track B draw-command batching, §205).**
**One measured question, one structural slice: the GL backend no longer submits
a draw per triangle.** Guest behavior is byte-identical on every gate; every
change is host-side except the batch-boundary seam in `Nv2aMmio.FlushDraw`.

- **Measured before building.** One 1B menu `--gpu gl --gpu-replace` run prints
  both halves of the ratio: `NV2A draws executed=33638` (primitive flushes) and
  `GPU backend=gl draws=2761019` (triangles) = **82 triangles per draw command**.
  Without that number the slice would have been worthless: the dashboard's
  `SET_BEGIN_END` history suggested one triangle per command. The 1-in-64 probe
  split the 786 ns/triangle submission: texture work ~53%, uniform upload ~23%,
  `EnsureProgram` ~12%, `glDrawArrays` ~7%, state ~4%.
- **Design changed from the §204 sketch.** A TBO read by `gl_InstanceID` would
  move the 25-vec4 block into the *fragment* stage (~19-25 lookups/fragment on a
  210M-fragment leg, i.e. a per-pixel regression). Instead the block stays a
  **uniform array** of `capacity * 25` vec4, and a `flat out/in int vTri` carries
  `gl_InstanceID` to the fragment stage (`#define T(i) uTri[((vTri)*25)+(i)]` is
  a constant-bank read, not a fetch). `gl_InstanceID` is 0 when not instanced, so
  one program serves both paths. Capacity comes from the queried uniform budget
  (64 here, 3 minimum-spec), `XGRID_GL_NO_BATCH=1` forces the old path.
- **The boundary is guest-side and exact.** `IPgraphBackend.BeginDrawBatch()` /
  `EndDrawBatch()` (default no-ops) bracket `Nv2aMmio.FlushDraw()` — the only
  place where "PGRAPH state and guest RAM cannot change between these triangles"
  is provable. No state fingerprint, and `TryDraw` outside a batch behaves
  exactly as before. `Clear`/`Flip`/`ResetState`/`Dispose`/software-fallback
  flush first, so write order matches the pre-batching behavior, and `drawMs`
  now includes the flush (`flushMs` is the batch share).
- **Evidence (same binary, interleaved, 3 pairs, 1B menu `gl+replace`):** `drawMs`
  **1971 → 1045 ms (-47%, 3/3)**, of which 242 ms is the instanced draws;
  `batchCalls=59437` for 2,761,019 triangles; wall median **42.20 → 40.43 s
  (-4.2%, 3/3)**; RAM/CPU SHAs `DD023100`/`F5D61C94` on every leg. Triangle:
  `batchCalls=2423`/54,492 triangles, `drawMs` 251.8 ms (flush 12.7 ms), wall
  CPU-bound so no claim.
- **Gates:** 1538 tests green; format clean; 1B menu golden sw `4EF1FBF6…` vs gl
  **`6BE6325F…`** — diff 1852 px, maxDelta 1, 0 violations (the batched fragment
  path is bit-exact); triangle sw `A1740DE0…` vs gl **`3FC4B419…`** — diff 16744,
  0 violations; counters identical (`draws=33632 frames=686 stalls=686`,
  `BackendUnsupported=0`); fast-mode sw anchors exact; ktest XGrid leg
  792,800,535 steps with the 40,797-byte suite log byte-identical
  (`CA5D3867…`). The mirror-mode menu run (no `--gpu-replace`) is covered by the
  golden; the A/B covers replacement mode.
- **New diagnostics/scripts:** `XGRID_GL_NO_BATCH=1`, `diag[... batchCalls=
  batchTris= batchCap=]`, `timed[flushMs=]`, `scripts/batch-205-ab.ps1`.
  Remaining menu submission is ~0.8 s of block packing, not GL calls. Four new
  `PgraphBackendSeamTests` pin the bracket contract (one batch per
  `SET_BEGIN_END`, no nesting, no clear/flip inside, skipped commands still
  close); removing the bracket fails 3 of the 4.

**Session 2026-10-20 (eleventh; Track B present path + Track E corpus opening).**
**The GL present path is measured and fixed, and the compatibility corpus has its
first harness and measured rows (§204).** All host-side: guest behavior is
byte-identical on every gate.

- **The measurement first.** `GlBackendTimings` + a 1-in-64 sampled probe
  (`XGRID_GL_PROBE=1`) split the menu's ~9.9 s GL cost: **draw submission 6.87 s
  (2.49 µs × 2,760,841 draws)**, clear 0.01 s, **flip readback 0.71 s** (686 ×
  1.04 ms), BGRA swizzle+alloc ~0.3 s. The readback was ~1 s, not 8.8 — the
  per-draw submission was the lever.
- **Nine slices landed:** readback opt-in (`--frame`/`--frame-first` only; window
  present blits the FBO), one packed `uTri[25]` uniform upload per draw (was 13
  `glUniform` calls), program-structure cache (no per-draw key string),
  `TextureKey` struct + per-entry sample addresses (removed 32 integer
  divisions/draw) + prefetched 16-load fingerprint, GL binding shadows
  (FBO/program/viewport/texture unit/texture), vertex positions by `gl_VertexID`
  (no per-draw VBO upload: 2 GL calls/draw), raw-tick draw timing, a latent
  texture-reupload bug fix (stale id kept, new texture leaked), and no
  double-upload on texture creation (25 → 13 uploads).
- **Two shared-path wins (Track D, measured >2%):**
  `Nv2aMmio.CollectMethodHistogram` off in the CLI (2 dictionary updates ×
  18.1M methods) = **menu sw -6.2% (64.2 → 60.3 s)**; `IPgraphBackend
  .ObserveMethods` lets the GL backend decline the per-method callback = **-5.8 s
  on the loaded menu gl wall**. The CLI now prints `methods=<MethodTotal>`.
- **Evidence (same-window, 3 interleaved pairs vs `4f0bd47`, SHAs identical):**
  menu `gl+replace` **63.09 → 45.25 s median (-20.5%, 3/3)**, `drawMs` 12.55 →
  2.41 s, readback → 0; triangle `gl+replace` 10.26 → 9.89 s (-0.9%, 3/3).
  `scripts/present-path-acceptance.ps1` (new) automates the ceremony. **M-A is
  not decidable on this host:** at session close the *unmodified* baseline
  build measured menu sw 62.6 s / triangle sw 14.0 s against the recorded
  33.3 / 8.69 (idle CPU, base clock; raster inflated 1.67x), so today's absolute
  walls do not convert to the reference session's. The durable evidence is the
  same-window A/B (-20.5% / -0.9%, `drawMs` 12.55 → 2.1 s, readback → 0) plus
  the exact goldens. Re-run the acceptance script on the reference state
  (menu sw ~33 s) before recording M-A; per-triangle batching is the structural
  step if the menu is still over.
- **Gates:** 1532 tests green, format clean, 1B menu golden sw `4EF1FBF6…` vs gl
  `6BE6325F…` (diff 1852 px, maxDelta 1, 0 violations), 1B triangle golden sw
  `A1740DE0…` vs gl `3FC4B419…` (diff 16744, maxDelta 1, 0 violations),
  `BackendUnsupported=0`, counters identical, **ktest smoke 7/7 rows green**
  (XGrid leg 792,800,535 steps, suite log byte-identical). Accurate mode
  untouched.
- **Track E:** `scripts/boot-to-first-frame.ps1` (new harness; prints one `BTFF`
  line + JSON per row), `docs/compatibility.md` rewritten with reproducible
  commands and four measured rows (six after the §205 refresh: every sample on
  both backends), the new `--frame-first <bmp>` CLI facility
  (first presented frame + step anchor). The nxdk-toolchain blocker is recorded
  precisely (MSYS2 base has no clang/make; C: 247 MB free → install on D:).
  Two reference notes from a parallel session are indexed:
  `gpu-reference-map.md` and `nv097-coverage.md` (201-row method table:
  108 handled / 93 recorded).
- **Diagnostics added:** `XGRID_GL_PROBE=1` (`probe[samples prep tex state uni
  vtx gl fpRam fpAdapter]`), `--gpu gl` prints `readback=on|off`, `--frame-first`.

**Session 2026-10-20 (course correction; docs only).** The canonical direction
now leads with the product path. The Avalonia frontend made the software
rasterizer the product renderer, and `--gpu gl --gpu-replace` still has ~9.9 s of
GL draw/readback/present overhead on the menu (31.1 vs 33.3 s); the known
per-flip readback is only ~1.1 s/1B, so the split must be measured before the
slice is scoped. Track C is frozen after C3-x hit its target
(8.8 ns/served member triangle, 31.6 ns menu); C3-y and the 1024-slot xlat cache
measured neutral and were reverted; C3-z is deferred (2–3%). New order: **Track B
(GL present path) → Track E (corpus + `docs/compatibility.md`) → Track P
(audio/pad/save states, parallel) → Track D (measured >2% only)**. No code
changed; `docs/development-direction.md`, `ImplementationPlan.md`,
`next-session.md`, the new `docs/compatibility.md` and
`docs/research-notes/compatibility-corpus.md` carry it. See decisions §203.

**Session 2026-10-20 (parallel frontend track).** **The Avalonia desktop frontend is
LANDED (`ea8b38f`; only `src/XGrid.Frontend.Avalonia/` touched).** A separate session
built the GUI while core work continued, so no emulator file, CLI path, test project,
solution entry, or shared package file was modified.

- **Shell:** Avalonia 11.3.22 (Desktop/Fluent/Inter) + CommunityToolkit.Mvvm 8.4.2,
  all MIT, versions supplied via `VersionOverride` so `Directory.Packages.props` is
  unchanged. `net10.0`; compiled bindings on with `x:DataType` everywhere; analyzers
  and warnings-as-errors stay on (`NoWarn=MA0004` is project-local because UI
  continuations must resume on the Avalonia dispatcher).
- **Pages:** Home (power/pause/stop, disc picker, live 4:3 display, speed/steps/
  frames/FPS/EIP, recent games, keyboard pad), Library (recursive ISO/CHD/XBE scan,
  XBE title parsing, search, launch), Settings (BIOS/MCPX/EEPROM/HDD/default disc,
  64/128 MB RAM, fast mode + raw emitter + spin skip + decode cache + JIT threshold,
  EOS quirk, NVNET backend/link, library folders, accent), Activity (level filter,
  auto-scroll, copy/clear), About (versions, runtime/OS, links, shortcuts).
- **Emulator integration:** in-process `XboxMachine` on a dedicated background thread
  in `XboxMachine.Run(65536)` batches; pause gate, cancellation-token stop, device
  disposal, and UI-boundary fault capture. Presented frames are read on the emulation
  thread only (`Nv2a.FramesPresented` change -> `TryCopyPresentedFrame`) and copied
  into a `WriteableBitmap` by a 100 ms dispatcher timer. Portable software rasterizer
  only: no WGL/GL backend reference and no window mixing.
- **Input/settings:** keyboard-to-XID pad state is applied at batch boundaries; F5/F6/
  F11/Esc/Ctrl+O/Ctrl+S shortcuts. JSON settings at `%APPDATA%\XGrid`,
  `$XDG_CONFIG_HOME/XGrid`, or `~/Library/Application Support/XGrid` (override with
  `XGRID_FRONTEND_DATA_DIR`); corrupt files are backed up and defaults restored.
  Five runtime accent palettes update all custom styles through `DynamicResource`.
- **Validation (Windows 11, repo test media):** Release build 0 warnings/0 errors and
  `dotnet format --verify-no-changes` clean. The EvoX HDD dashboard booted through the
  real firmware path and rendered in the app (861,995,008 steps / 138 frames in the
  smoke run under concurrent test load); pause froze steps, resume, power-off and
  restart all behaved; the library found the two sample XBEs and parsed titles; the
  Aqua accent saved and restored across launches. No core/CLI/test file was touched;
  the 1519-test baseline was not re-run by this UI-only track.
- **Limitations:** software renderer only, keyboard pad only, no host audio sink, no
  save states/debugger panes, loose XBE needs an ISO/CHD wrapper. The design note and
  next-step list are in `docs/research-notes/avalonia-frontend.md`; run
  `dotnet run --project src/XGrid.Frontend.Avalonia`. Project usage:
  `src/XGrid.Frontend.Avalonia/README.md`.

**Session 2026-10-20 (tenth, oracle/0.8.137 re-base). Two deliverables: the 0.8.137
reference delta is now verified, and XGrid has an independent x86 differential.**

**1. 0.8.137 re-base (`docs/research-notes/xemu-0.8.137.md` rewritten).** The 88/97
differing NV2A files are **clang-format only** except four: `ptimer.c` (rewritten),
`nv2a.c`/`nv2a_int.h`/`nv2a_regs.h` (PTIMER IRQ), `pgraph/vk/surface.c` (nearest
filter) and `gl/shaders.c` (GL version in the shader-cache key). No new NV097
methods, no PSH/VSH/vertex/texture semantic change; the 0.8.136 transcriptions stay
valid. The real functional delta is **PTIMER**: 61-bit register time with a writable
offset (TIME_0/TIME_1 are writable), ALARM_0 is a 27-bit-masked compare re-armed on
each 2^32 wrap, the alarm is a QEMU timer, `INTR_0` read latches a reached alarm, and
`NV_PMC_INTR_0_PTIMER` (bit 20) now feeds the PMC IRQ aggregate. `Nv2aMmio` still has
the 0.8.136 counter-only model - landing the 0.8.137 model is the next fidelity item.
The APU/DSP restructure is documented in the same note: the JIT is an external Rust
crate (so `interp/` stays our semantics oracle), `dsp_emu.c.inc` is byte-identical
after whitespace removal, and the real corrections are the 4-entry interrupt table
(RESET 0x00, STACK_ERROR 0x02, ILLEGAL **0x04** not 0x3E, TRAP 0x08), SR bit names
(SC/DM/LF/FV) and interrupt entry clearing LF+FV, DMA format 0 (8-bit) and the
`DMA_CONTROL` **3-read -> STOPPED** completion protocol, and `NV_PAPU_XGSCNT` =
`ep_frame_div * 32`.

**2. CPU differential oracle (`tools/cpu-oracle/`, note `cpu-oracle.md`).** A
333-case corpus (ALU/shift/mul/div/mov/lea/stack/bit/string/x87/MMX/SSE, defined-flag
masks per instruction) runs identically on XGrid and on QEMU's TCG core (Unicorn
2.1.4); both sides produce the same FXSAVE image plus a data-page dump. Found and
fixed three real gaps: `0F BA` (BT/BTS/BTR/BTC r/m, imm8) was **not decoded at all**;
`FPREM` mapped the quotient bits as C3=Q2/C0=Q0 instead of C0=Q2/C1=Q0/C3=Q1; and
`FDIV`/`FDIVR` never latched ZE (`x/0`) or IE (`0/0`) in the FPU status word. 1B
fast mode is bit-exact on both titles (anchors `660CA11B`/`F5D61C94` and
`F57934BF`/`CF79FC17`); accurate-mode RAM SHAs are unchanged and the triangle CPU
SHA moved `98039867 -> 9B5AFC2A` because a byte diff of the two 71 MB `.xgs` saves
shows exactly one changed byte - the FSW ZE bit. Suite 1519 green (CPU project 643).
`ktest-smoke` XGrid leg is bit-reproducible at **792,800,535** steps with only the
known deltas (319 RtlWalkFrameChain, 344 XcVerifyPKCS1Signature); the xemu leg could
not bind its monitor socket on this host (`Failed to bind socket: Input/output error`
on ports 4455 and 4457), so the 7/7 pairwise diff did not run. The system-QEMU path
(`boot.s` + `harness.s`) is staged but
not validated end-to-end (the 197 MB installer download is incomplete on this host);
Unicorn is the same TCG core.

**Session 2026-10-20 (ninth).** **Track B compiled-loop fast lanes: the sampler's
descriptor work is gone (§202).** Priced by ablation on the 1B legs first (a probe
switch that elided one block at a time: all sampler work 1.6 s, decode 1.0 s,
destination read 0.5 s, pack 0.3 s, write 0.2 s, `TrackTextureCoords` ~0), then landed
two exact slices:

1. **Per-fill direct-RAM sampler + decode branch** (both compiled fills):
   `IGpuMemoryFast.TryGetRamBytes` + `SamplerAddressBound` prove the highest address a
   fill can sample, so the per-sample interface call and its span construction are
   replaced by one direct little-endian word read (`MemoryMarshal.CreateReadOnlySpan`
   over the RAM array; the adapter path stays as the fallback and still owns every
   non-RAM or out-of-bound case). `SamplerPlan.DecodeKind` selects the fill's exact
   `DecodeTextureTexel` branch (A8R8G8B8 default / A4R4G4B4 / generic). The k1 blend
   destination read uses the same direct word when the fill's framebuffer span is in
   RAM.
2. **Lighting-loop provable clamp drops**: the `R1` power chain stays in [0,1], so its
   -1..1 clamps are no-ops; the combine-stage products are no-ops when
   `|c0c|,|c1c| <= 1` (a per-fill predicate that all 53,574 lighting fills pass, with
   the generic loop as fallback); `addCd` is hoisted; both fills hoist the per-row
   barycentric terms.

**1B fast-mode A/B vs `65e136e` (3 interleaved pairs, SHAs + every counter
identical):** triangle `rasterPsh` **3.39 -> 2.87 s (-14.2%, 3/3)**, wall 9.15 ->
8.69 s (-4.0%, 3/3); menu **13.47 -> 12.49 s (-5.3%, 3/3)**, wall 35.45 -> 34.64 s
(-0.3/-2.4/-4.2%). **Accurate mode is unchanged**: 1B SHAs reproduce the §200 anchors
(menu `D9C99A58…`/`3D2740B8…`, triangle `F57934BF…`/`98039867…`) with every counter
identical. Suite **1519**, ktest smoke **PASS 7/7**, format clean. **Measured
negatives, do not re-try:** the k2 constant fold (-2-3% - the hoisted constants do not
stay in registers), the alpha-1 blend skip (neutral; 93.5% of menu k1 fragments have
`fa == 1` but the destination read is only 2.4 ns/px), `AggressiveInlining` on
`SampleFast` (neutral). **CPU-side levers priced and dropped:** the non-JIT time is
mostly the software raster inside an MMIO member (gl+replace takes the triangle's
`nonJitMs` 5.35 -> 2.5 s), and block builds cost ~200 us raw + 20 us IL, with the
16/64/256 threshold sweep trading build cost for interpreter time one-for-one.
**Lesson:** probe instrumentation left in the candidate leg biases the wall by ~0.35 s
- strip it before any A/B (that is what disguised the raster win as a wall regression).

**Also this session: `--jit-mmio` was broken and is fixed (§202 addendum).** The
opt-in device fast path halted the 1B menu leg at 25,146,493 steps (pre-existing, also
on the session-start build). Bisection to one raw block, then to the emitter's
compile-time `_flagsLive`: the device block is emitted *before* the member's `slow`
block and ends in `EmitReloadAfterHelper()` (which sets `_flagsLive = true`), so the
`slow` code materialized the probe's own comparison flags into `F_EFLAGS` — a
never-executed branch corrupting a reachable one. Fix: reset `_flagsLive = false`
before `slow` in both `EmitLoad`/`EmitStore` plus
`EmitSyncGuestStateForHelper()` (no flag re-materialization) for the device paths.
Regression test `MmioFast_FlagsAcrossSlowMemoryMember_MatchInterpreter` (fail-pre-fix:
EFLAGS 0x70 vs 0x130); both 1B goldens now pass with the flag on, exact SHAs. Not a
perf win (`mmioMembers=0` — the probe already routes device pages to the slow path),
so it stays opt-in. Suite **1522**.

**Session 2026-10-20 (eighth).** **Track B rasterizer campaign: the software
rasterizer is ~44% cheaper and is no longer the dominant wall (§200).** Three
measured steps, each gated on both 1B SW goldens with exact SHAs and every
counter byte-identical:

1. **O5 counter batching was measured NEGATIVE before implementing** (a probe
   elided every per-pixel diagnostic: 24.00 -> 23.34 s menu raster, no triangle
   change), the implementation measured neutral, and it was reverted. The
   inventory's 5-12 ns/fragment estimate for the diagnostics is disproven.
2. **O3b per-fill fast sampler** (`SamplerPlan.FastKind`): linear/Morton 2- and
   4-byte texels with clamp/mirror/wrap folds skip the per-sample descriptor
   tests; `DecodeTextureTexel`/`TryReadPixelFloat` use exact unit tables built
   by the same `i / 255f` expression (bit-identical, removes 3-4 float divides
   per sample/blend read).
3. **O1 compiled fills** for the two shapes the titles actually run: the k1
   modulate shape (**245.8M of the menu's 285M pixels**) and the k2/k3
   "squared lighting" shapes (62.4M px on **both** titles = the whole triangle
   raster). A per-fill predicate proves which stages can only produce a
   constant texel with no counter updates, then runs a transcribed pixel loop
   (same barycentric/interpolation/sampler/combiner/blend expressions) with the
   generic loop as the untouched fallback.

1B `rasterPsh`: menu **24.0 -> 13.3-15.1 s (-44%)**, triangle **6.19 -> 3.30-3.45 s
(-45%)**. Interleaved wall medians vs the session-start commit (3/3 pairs):
**triangle 12.44 -> 9.51 s (-23.5%), menu 53.78 -> 42.01 s (-21.9%)**; on a
quiet host the absolutes read **triangle 12.21 -> 9.13 s, menu 47.96 -> 36.01 s**. Suite
**1519** (8 new differentials: sampler shapes plus both compiled fills, fail-pre-fix proven),
ktest smoke PASS 7/7, format clean. Remaining wall is CPU engine + boot again
(charter redirect criterion: Track C leads next, starting with the scoped C3-z
inline page pair). *(Superseded 2026-10-20 by the course correction: Track C is
frozen; Track B's present path leads. See the newest session note.)*

**Session 2026-10-20 (seventh, cont.).** **Track B first slices: rasterizer O2
memory fast lane + O4 swizzle spread LANDED (§199p).** `IGpuMemoryFast`
(optional; production `PhysicalGpuMemory` only) opens one GPU-writer
suppression scope per fill and a decode-page memo, and gives `Nv2aMmio` direct
pinned-RAM word reads/writes with exact fallbacks (non-RAM, diagnostic bus
watch, unbatched write). O4 replaces the two per-sample `InterleaveBits` mask
loops with the branchless Morton bit-spread when the plan masks are the
standard alternating ones (non-standard masks keep the loop; exact by
construction). New Nv2a differential pins fast-vs-legacy framebuffer equality
and lane use. SW 1B goldens exact both titles, counters unchanged.
Quiet-host pairs: O2 **menu 50.01 -> 48.99 s (-2.0%, 3/3), triangle 12.95 ->
12.69 s (-2.0%, 3/3)**; O4 on top **triangle 12.51 -> 11.76 s (-5.9%, 3/3),
menu 47.90 -> 47.33 s (-1.2%, 2/3)**. Suite **1511**. Next Track B lever:
O1/O5 per-pixel fragment/counter work.

**Session 2026-10-20 (seventh).** **C3-x hot-shape native encoding is LANDED
(decisions §199o, opt-in `--jit-raw`).** A new `--raw-shapes` diagnostic
(`RawHelperShapeCounts`/`RawHelperShapeDetail`, plus `rawBuildFails` in the
Jit stats line) ranked every helper-routed raw member; the emitter then baked
the hot shapes natively: `push/pop/call/ret/leave/jmp [m]/call [m]`, memory
`movzx/movsx`, `neg/not/bswap/xchg/cdq/setcc`, `shl/shr/sar/rol/ror` by imm
and by CL (host instruction plus the ShiftOps OF rule for counts > 1),
`Fld/Fst/Fstp/Fild/Fist/Fistp`, the x87 arithmetic family (reg/m32/m64, the
`Faddp` pops, `Fchs`, compares, `Fld1`, `Fxch`, `Fiadd` m16/m32), MMX
`movq/movntq`, scalar SSE (`Addss/Subss/Mulss/Divss/Minss/Maxss`,
`Sqrtss/Rsqrtss/Rcpss`, `Movss`, `Cvttss2Si`) and `prefetch` as a Nop. **At 1B
the helper-routed members fell 24.65M -> 1.81M (triangle) and 122.65M ->
5.57M (menu)**, with both title SHAs and every NV2A/JIT counter identical to
the IL anchors (triangle `F57934BF.../CF79FC17...`, menu
`660CA11B.../F5D61C94...`). Two real bugs were fixed: register-form FPU
members advanced EIP twice (the block exited mid-instruction, then the guest
jumped through garbage - diagnosed with the block trace `served=6
exit=0x8005818D` and the raw dump), and the IL runner's generic EIP advance
did not treat the new handler-backed kinds as owning EIP
(`JitPlanner.HandlerOwnsEip` now centralizes it). Tiny helpers returning a
value must capture it in **R8**: `EmitFaultCheck` clobbers R9/R10/R11 (the
first version stored a fault-pointer fragment as the FPU result). Suite
**1510** green (11 new raw differentials), ktest smoke **PASS 7/7** (01:10),
format clean. **Quiet-host 1B A/B vs the pre-C3-x commit (3 interleaved pairs
per leg, old/new SHAs identical on every pair):** `--gpu sw` triangle
14.74 -> 13.14 s (-10.8%, 3/3), menu 52.20 -> 48.43 s (-7.2%, 3/3);
`--gpu gl --gpu-replace` triangle 7.56 -> 6.49 s (-14.1%, 3/3) with
**11.9 -> 8.8 ns/served member**, menu 34.29 -> 31.56 s (-8.0%, 3/3) with
**36.7 -> 31.6 ns** (the §199n target was menu 61 -> ~30 ns). The wall gain is
smaller than the engine gain because the sw rasterizer is 39-48% of those
walls. **C3-y (per-member EIP/served/TSC bookkeeping trim) was implemented
bit-exact and then measured NEUTRAL, so it was reverted** (triangle gl, 5
interleaved pairs: wall 7.048 -> 7.015 s, `outerMs` 4326 -> 4314): the
bookkeeping is off the critical path. A 1024-slot data translate cache was
also neutral (menu `outerMs` +0.1%, 3 pairs) and reverted. The new raw-kind
profiler (`XGRID_RAW_PROFILE=1`, run with `XGRID_RAW_CHAIN=0`) shows the 1B
menu mix is **~50% MovLoad/MovStore, 14% DecR, 14% AluRR/IncR/AluRI**, i.e., a
move/loop workload whose cost is the memory probe's dependent-load chain and
store guard - not helper dispatch. The queued flip-pacing probe is **closed as
no-bug**: `FLIP_STALL` matches the reference (`READ_3D != WRITE_3D` until
vblank), and the triangle's 35 flips/1B is render work + one vblank
(~39 ms/frame guest time), not an extra-frame delay. Remaining helpers are
dominated by `mul/imul` (~1.1M/1.5M),
string `movs` (1.0M menu), high-byte `mov` (0.8M menu), `fnstsw`,
carry-rotates and a few SSE moves. **Track B/D now owns the end-to-end gap**
per the charter's redirect criterion; further CPU work is structural
(memory-probe latency, device mix), not bookkeeping or helper coverage.

**Session close 2026-10-20 (sixth).** **Direct raw block chaining is LANDED
(`21b0f8a`, opt-in `--jit-raw`)** and the fault-path crash that forced its
revert is root-caused and fixed. The blocker was never the chain tail: **a
managed exception could not legally unwind out of a raw helper through the
hand-emitted native frame** (no unwind data, so CoreCLR cannot walk it) —
delivery depended on stack-layout luck, and the chaining diff changed the
layout. Chain-off crashed too, `XGRID_RAW_SKIP_HEADS=100F` hid it, and a
minimal RWX-frame harness reproduced the abort independently. Fix: **raw
helpers never throw across the native frame** — each entry catches, records the
exception (`RawCaptureFault`, which sets `RawState.Fault`) and returns; the
emitted block tests that word after every helper call and exits with the `-1`
sentinel; `RunJitBlock` rethrows the original exception (`ExceptionDispatchInfo`)
after `RawExit` published the context, so fault delivery and `_jitProgress`
accounting are byte-for-byte the IL runner's. Invariant recorded in decisions
§199m (new `EmitFaultCheck()` + try/catch for any new helper). Gates: the
`AluLoop`+`PagedCrossVisitFault` pair clean **10/10**, CPU suite **625/625
green ×10**, full solution suite green, format clean, ktest XGrid leg stops at
the 4E03A park after **792,800,535** steps with its 739-line suite log
**byte-identical** to the accepted baseline. 1B: menu IL == raw ==
`660CA11B…/F5D61C94…` with every counter identical (the §199i anchor);
triangle IL == raw `F57934BF…/CF79FC17…` on this host (the older `A2DCA55F…`
anchor no longer reproduces even on pre-patch `025d20d` — environment moved,
recorded in §199l). Medians of 3 interleaved pairs: **triangle wall 26.97 →
23.72 s (-12.0%, 3/3), `outerMs` -28.6%; menu wall 109.39 → 95.06 s (-13.1%,
3/3), `outerMs` -14.2%**; 100M A/B menu wall 0.9315 (5/5) / outer 0.9019,
triangle outer 0.9670 (wall noise). Fixed-section gates:
`scripts/chain-199l-gate.ps1`, `scripts/chain-199l-ab.ps1`. Baseline **1499
tests green**. Note: `Directory.Packages.props` still carries its pre-existing
uncommitted version bump (Meziantou 3.0.312, XISOSharp 1.4.3, CHDSharp 1.5.0)
and was left untouched.

**Perf position after the re-land (§199n, same session).** The breakdown moved
the target: with `--jit-timing --gpu-time` at 1B, the **software rasterizer is
the largest single item on both goldens** (menu 112.5 s sw vs 58.9 s gl →
48%; triangle 22.2 vs 13.5 → 39%), block mechanics are no longer material
(1.6–4.6M invocations and 109–158M chain jumps over 450–830M members), and the
CPU path costs **18.5 ns per served member (triangle) / 61 ns (dashboard)** on
the `gl+replace` legs. The dashboard premium is the **helper-routed shape
mix** (MMX `movq`/`movntq`, strings, x87 → one `ExecuteSpec` managed call per
member) plus per-member bookkeeping (EIP store, TSC tick, store-guard tests).
Against xemu's ~830 Msteps/s we are at 74 Msteps/s with GL (~11×),
i.e. the engine gap is a small multiple and the end-to-end gap is GPU/helper
bound. **Re-weighted fast-mode order (charter status block + §199n):**
(1) C3-x hot-shape native encoding (target menu 61 → ~30 ns/member),
(2) C3-y bookkeeping trim (EIP per block exit, hoisted store guard),
(3) C3-z inline TLB/RAM page pair, then **Track B leads**: rasterizer
structure, `gl+replace` per-frame path, present pacing. **Reporting changes**
to ns/served member + `gl+replace` 1B wall + wall per rendered frame; 1B-step
walls stay as continuity anchors only (530M of the triangle's steps are
idle-spin). Redirect criterion: if the dashboard is still >15× from real time
with GL after C3-x/y/z, further codegen stops and the remainder is Track B/D.
Queued probe: 35 triangle flips per 1B here vs ~84 implied by xemu's rate —
check the vblank/flip wait before any present-path work.

**Session close 2026-10-20 (fifth).** The C3 raw-emitter correctness story is
closed: the §199h menu divergence was the split-member fast-path EIP advance
(§199i), and **both titles are bit-exact at 1B** (menu
`660CA11B…/F5D61C94…`, triangle `A2DCA55F…/7DAB63EB…`, every counter
identical). Suite **1497** green; ktest smoke PASS 7/7 at 792,800,535 steps;
format clean. The §199k state hoist makes the raw path **ahead of IL on CPU
serving time** (100M A/B 9.0% menu / 2.4% triangle; 1B `outerMs` -3.7% /
-2.3%; end-to-end walls host-noise bound). Two negative results recorded:
mask-driven register sync (§199j) and static served/EIP bookkeeping (§199k) —
both ~1%, below the sub-2% resolution standard. **Direct raw block chaining
was implemented and produced the largest win yet but is NOT landed in that
session:** it triggered a deterministic CLR `ExecutionEngineException` in
`JitTests.PagedCrossVisitFault` when preceded by `AluLoop`; resolved by the
sixth session above (§199m).

**2026-10-20 status (direct development, two 12-hour windows).** Fast-mode
medians on the quiet host: **triangle 1B 10.63 s, menu 1B 45.62 s**
(gl+replace+spin, SHAs exact 4F1FCBC0/7DAB63EB, DD023100/F5D61C94). Landed:
D3b page-edge block formation (nonJitCalls -59%), C2 slice 1 lazy GPR state
sync + direct context backing-field access, C2 slice 3 native Fld/Fst/Fstp m32,
then **C3 slices 1-2b: the raw x86-64 block emitter** (`--jit-raw`, opt-in;
Iced 1.21.0, MIT). Measured cost structure (decisions §199d): chain ~1 s, state
sync ~1.5-2 s, ordinary members ~5-6 s (~14 ns each, memory probe ~25 ns), slow
members ~1 s, cold non-JIT ~1.9 s, GL ~0.5 s. **Two negative results recorded:**
block fusion (-82% invocations but +40% wall from codegen/I-cache and the
emitted boundary poll; it also silently disabled the spin skip) and a
1024-slot data translate cache (L1 pressure).

**C3 raw emitter — state after §§199e/199f.** One native function per block
with guest GPRs pinned in fixed host registers (EAX/ECX/EDX/EBX + R12D-R15D so
AH..BH stay addressable), arithmetic flags live in the host FLAGS register
(materialized to a frame slot only when a probe or helper needs them; IF/DF/IOPL
ride in a control slot), memory members running the data translate-cache probe
inline against pinned pointers with a `[rbp+rdi]` access and an exact-helper
slow path (state synced first, so fault precision is unchanged), and every
unencoded shape (x87/SSE, strings, calls, indirect jumps) routed through a
reverse-P/Invoke trampoline to the same spec handler the IL emitter uses — so
**every block is emittable**. Four bring-up blockers, all found by differential
testing and fixed: (1) the frame slots sat in the **Win64 shadow space**, so the
first helper call overwrote the saved cpu/entries/state pointers (frame now
starts at 0x20); (2) the entry delegate **marshalled** `XboxCpu` and
`DecodeCacheEntry[]` — the callee received a proxy, proven by comparing the
received pointer with the running CPU — so `RawRun` takes `IntPtr` for both;
(3) `EmitProbe` used **guest registers** (RAX/RCX/RDX hold eax/ecx/edx), so every
probe miss synced garbage; (4) the helper updates the **context** while the
block reads its own scratch copy of Eip/Eflags/Tsc, so `ExecuteSpec` now
publishes them back. Also fixed: `cmp [mem],reg/imm` is a read (it was routed
through the write path), CLI/STI/CLD/STD materialize the flags before their
`and`/`or`, ADC/SBB load CF when it is not live, addressing uses the 64-bit
register forms, the memory slow path parks its address in RDI, and any emit
failure falls back to the IL runner. **The 2026-10-19→20 correctness campaign
(decisions §199g) closed both correctness gaps:** the post-member IRQ poll for
STI/POPF/string/port members (the §193 wakeup contract), the data translate
cache probe indexing by raw VPN instead of `vpn & DataXlatMask` (anything
≥ 1 MB read out of the 256-entry tables — the original triangle crash), the
non-terminator EIP advance, forward-publishing Eip/Eflags/Tsc to helpers, the
unguarded inline stores (PT/active-page bitset guard calling
`NoteStorePhysical` inline like the IL path, plus the watch-armed flag), the
poll's parked registers landing in the callee's shadow space, host DF left set
across helper calls (a Win64 ABI violation that corrupted the runtime's
`rep movs`), flag liveness leaking from slow-path branches into merge points,
the store guard's helper call clobbering guest EAX/ECX/EDX, GC-stale CPU and
entries pointers (the CPU now travels as a weak `GCHandle`, the entries array
is pinned with `GC.AllocateArray(pinned: true)`), memory-source MovExtend
emitted as a register move, disp32 ≥ 0x80000000 failing to encode, WP parity
(`Cr0Wp` flag) and A/D parity with the IL probe. **All 56 JitTests +
RawJitTests pass with the raw path forced on; suite 1497 green; build and
format clean.** The second campaign (§199h) then closed the two title
differential bugs: **the store guard's note path clobbered R9 (the linear
address) — every page-table store took the slow path with a garbage address
while a watch was armed (the CLI arms one by default), the triangle's
25.05M #PF at CR2=0x0D**; the guard now runs the exact `IsWatched` range test
(`WatchStart/WatchEnd` in `RawState`) before the note path, which also
restored inline stores (previously *every* store went slow while any watch
was armed). **And the exit-edge flag merge: the epilogue materialized the
frame flags with the emitter's compile-time liveness, but `_exit` is also
the target of the poll's pending path, the `Jcc` arms and the terminator
jumps — the kernel idle delivery pushed EFLAGS 0x202 instead of 0x246, the
triangle's 30.92M divergence**; every exit edge now normalizes the frame
flags on its own path. **Differential status: BOTH titles are bit-exact at
1B** — §199h's menu gap was a split-member EIP advance bug, not a store
helper (decisions §199i): the four fast/slow members (RMW destination,
cmp-memory, test-memory, inc/dec-memory) lost the fast path's EIP advance
because the slow path's `EmitHelperMember` sets the compile-time
`_memberHelper` flag. A block ending on such a member exited on the member's
first byte and the member path re-executed it, leaving the engine one guest
instruction behind (visible at the trace level as the MMX-loop byte).
Regression test `BlockEndingInMemoryRmw_AdvancesEipOnce`; menu 1B matches the
IL anchor exactly (`660CA11B…/F5D61C94…`, every counter identical), triangle
1B `A2DCA55F…/7DAB63EB…` on both engines. The raw path stays **opt-in**;
after the §199k state hoist it is **ahead of IL on CPU serving time** (100M
A/B 9.0% menu / 2.4% triangle; 1B `outerMs` -3.7% / -2.3%), and **direct raw
chaining (§199l) is landed on top of the §199m fault contract** for another
-12% (triangle) / -13% (menu) wall and -29% / -14% `outerMs` at 1B, SHAs and
counters identical to IL. Suite **1499** green.

**Menu divergence recipe (historical, CLOSED by §199i).** `boot … --jit
--jit-threshold 16 --jit-spin --jit-raw --pad-script "down@950000000+hold,
none@962000000,a@970000000+hold,none@976000000" --steps 968380640 --gpu sw`
vs the same without `--jit-raw` used to differ by bytes at physical
0x7534B8/0x7534C8. The block-level trace (`XGRID_BLOCK_TRACE=1` +
`XGRID_BLOCK_TRACE_FROM/TO` + `XGRID_BLOCK_TRACE_HEAD`) localizes it to head
0x1E010: the raw block exited at 0x1E088 (M31 `or [eax+0x2C],edx`) instead of
0x1E08B and re-executed M31. Fixed by emitting the fast-path EIP advance for
the four split members (decisions §199i). Both titles are bit-exact at 1B now;
keep the recipe only as the differential harness for future raw changes.
Note for `--save-state` work: in the `.xgs` CPU block, the TSC is at byte
offset 146 (the §199h "offset 170" note was wrong) and equals the step count.

Differential harness for raw changes: `XGRID_WATCH_START`/`XGRID_WATCH_END`
(hex) retarget the bus watch; `XGRID_WATCH_TRACE=1` prints each hit as
`WHIT tsc=… eip=… addr=… size=… value=…`; `XGRID_BLOCK_TRACE=1` with
`XGRID_BLOCK_TRACE_FROM/TO` (TSC window) logs
`head/entry/members/served/exit/tsc/raw` per block invocation and
`XGRID_BLOCK_TRACE_HEAD=hex` dumps that block's member list
(EIP/length/mnemonic). The first differing invocation is the root cause; the
RAM SHA alone is only the symptom.

Diagnostics to keep: XGRID_SLOW=1 (slow-member mnemonic mix), XGRID_NONRAM=1
(non-cacheable fetch pages), --jit-timing (runner vs chain split), Host timing
(execute vs advance), `XGRID_RAW_DUMP=1` (per-block Iced disassembly to
`%TEMP%\xgrid-raw-<head>.txt`), **XGRID_RAW_TRACE=1** (per-invocation
enter/exit + spec-handler trace to `%TEMP%\xgrid-raw-trace.txt`; file-based on
purpose — a hard crash loses buffered stderr), XGRID_RAW_THRESHOLD (test-side
build-threshold override for bisecting), **XGRID_RAW_SKIP_HEADS=hex[,hex…]**
(forces the listed raw heads to the IL runner — used to confirm the
`0x8001F9EB` block as the source of the 25.05M divergence).
Also: `XGRID_RAW_CHAIN=0` (disables raw chaining for A/B), the `rawChained` /
`JitRawFaults` / `RawChainPublishes` counters in the JIT stats line, and the two
§199l/§199m gate scripts: `scripts/chain-199l-gate.ps1` (1B IL-vs-raw legs) and
`scripts/chain-199l-ab.ps1` (interleaved pair medians).

### Fast-mode JIT J1a–J1d LANDED (2026-10-18 night, direct; §§191–192) — the real-time band is reached

Charter Phase 0 + Phase 1 (J1 slice) done in one night: `boot --bench`
(BENCH line: wall/msps/RAM+CPU SHA); `--jit` whole-block engine (GPR/EIP/
EFLAGS/TSC in IL locals, inlined translate-cache + RAM fast paths, exact
interpreter fallback; head cache with visit-threshold builds,
`--jit-threshold` default 8; block-chained inner loop + 32-entry memo;
CallSpec fallback members for all static spec handlers; Cli/Sti flag
members); `--jit-exact` per-member poll — bit-exact.

Evidence: 29 JIT differential tests (fuzz, IRQ sweeps, faults, paging);
**1B exact-mode runs bit-exact vs the accurate control on BOTH titles,
golden frames reproduced** (menu `4EF1FBF6…` 686/686, triangle
`A1740DE0…` 68/34); fast mode's SHA divergence proven to be IRQ-position
timing only. Rates (1B, same host window, single runs; absolutes
host-shifted, ratios robust): **menu fast 13.07 s (76.5 Msteps/s, 97.1%
served) vs spec-ctl 88.05 s = 6.7×; triangle fast 13.14 s (76.1 Msteps/s,
97.6% served) vs spec-ctl 44.7 s = 3.4×** — the 10²–10³ Msteps/s band.
Fast mode is never gate currency; exact mode is the bit-exact variant.
Suite 1461 green, format clean. Next: block chaining (direct jumps),
shape tail, medians-on-quiet-host, 1B threshold re-tune.

### Fast-mode lost-wakeup fixed (§193, `3211788`) — the rates above were a wedged guest

The §192 fast-mode runs presented 1 frame vs the control's 686/68: the
kernel idle loop's `sti/nop/nop/cli` spin has IF=1 only inside one block,
so the once-per-block poll starved every wakeup (interrupt count froze at
267; guest wedged). STI members now poll at their own boundary; POPF left
the member set. **Post-fix, fast mode is bit-exact on triangle+menu 1B
(SHAs + golden frames) and ktest 2B cpuSha** (suite log differs only in
one 5 ms measured duration). Real-workload served share at 1B: 26.6%
(menu) / 35.5% (triangle) — the unserved tail is the next coverage lever,
with quiet-host medians still pending.

### Coverage campaign (§194, `0bc0a35` + `e4975e1`) — real workloads now run on the JIT

The 193-era fast numbers measured a spinning guest; the real workloads ran
73% through the member path. Fixed in one campaign: non-JIT runs are
sliced (`--jit-slice`, default 256) so the JIT probes a head every 256
instructions; blocks survive the guest's page-table churn via lazy byte
revalidation (`Verdict.Stale` → fresh translate + source-window compare)
with persistent visit counters and fill-on-demand formation; new members:
indirect call/jmp, Popf, all string ops incl. rep forms, In/Out, Cld/Std,
Invlpg. **Menu 1B fast: 99% served, fully bit-exact (SHAs + golden frame,
686/686)**; triangle fast keeps a 3-dword context-save residual (delivery
shift; exact mode bit-exact); `--jit-tally` diagnostics added. A
poll-exit bug (EIP not advanced for interpreter-call poll-after members →
instruction re-executed after the handler) was caught by the 100M SHA
gates and fixed. Suite 1476 green, format clean.

**Quiet-host results of this build (medians of 3 fast / single controls; all
SHAs verified; §194 amendment 2):** triangle 1B plain 51.28 s / spec-ctl
37.10 s / **fast 27.91 s (1.33× ctl, 1.84× plain)**; menu 1B plain 81.91 s
/ spec-ctl 73.61 s / **fast 64.02 s (1.15× ctl, 1.28× plain)**; ktest 2B
CPU-bound **3.76×**. Breakdown: the software rasterizer is 36–38% of both
walls (24.3 s menu / 10.1 s triangle) and MMIO slow-path members (118.9M /
21.3M) dominate the runner's non-raster time — the next levers are Track B
(GPU) and MMIO-path work, not CPU-side JIT tuning.

### Fast-mode JIT track chartered (2026-10-18) — operator decision

Long-run goal: at least xemu-class performance. Governing doc:
`docs/research-notes/jit-track-charter.md` (dual-mode contract; Phase 1
DynamicMethod block JIT authorized; Phase 3 raw emitter gated on Phase 1–2
evidence; kill criteria; two-tier validation; license is GPL-2.0 since
2026-10-23). The
accurate mode remains gate currency; fast mode (`--jit`) is never gate
currency and is validated by sync-point differentials + `--jit-exact`
bit-exact debugging. Next work item: Phase 0 bench + `--jit-verify` harness.
Recorded: decisions §190; `development-direction.md` §1/§2b; Phase 15/Track C.

### Rasterizer throughput campaign (2026-10-17 evening, direct; §§180–183) — DONE

The software rasterizer was the dominant wall on both 1B goldens (menu 68%,
triangle 66% — measured with the new opt-in `--gpu-time` diagnostic). Six
bit-exact slices landed; every gate: menu/triangle SHAs exact, every counter
exact, normalized stdout byte-identical, suite green:
1. F1: dashboard modulate-config fast path (§180) — raster 119.2 → 80.1 s.
2. R2: loop skips + per-fill fragment constants + scratch reuse (§181) → 76.2 s.
3. R3: planned combiner evaluator (§181) → 59.5 s.
4. R4: lighting-config fast paths (§182; recognizer + transcription pinned by
   differential tests over the REAL config words — the first recognizer had
   misread the histogram and silently never matched; the `fastfills=k1/k2/k3`
   counter exists so that can never hide again) + R5: per-draw sampler
   plans (§182) → **menu 30.2 s / triangle 9.9 s**; R6: SrcAlpha/InvSrcAlpha/
   ADD blend specialization (§186, 3/3 paired wins, −3.25 s) → **menu 26.9 s**;
   R7: lean interpolation for modulate fills (§187, 16 of 24 dead vectors
   skipped; `lean=402600` fills) → **menu 24.6 s**.
Two further experiments measured NEUTRAL and were reverted (§183: GPU write
scopes/page memo; byte→float decode tables) — the remaining raster cost is
fundamental per-pixel work (~100 ns/px), not hoistable decode/bookkeeping.
**Closing medians (median-of-3, interleaved, all SHAs exact, §186a/§187):
menu 70.8 s (2.03× vs the 143.7 s baseline), triangle 36.6 s (1.96× vs
71.6 s); rasterizer-only menu 119.2 → 24.6 s (4.9×), triangle 54.7 → 9.9 s
(5.5×).** ktest smoke PASS 7/7 (on final binaries).
Diagnostics to keep: `--gpu-time` (raster seconds per fill path, combiner
config histogram, fastfill k1/k2/k3 counts).
Next raster levers if ever needed: row-span iteration and structural
per-pixel work (inventory `docs/research-notes/rasterizer-inventory.md`
O6/O7-class); the CPU side is again the larger half of both walls. The
decode-flush narrowing candidates are filed in
`docs/research-notes/decode-flush-audit.md`.

### GL backend + opt-in window (2026-10-17 evening, direct; §184) — DONE

- **Latent regression FIXED:** commit `4b5af0d` renamed `wglGetProcAddress`
  to `WglGetProcAddress` without an `EntryPoint`, so `--gpu gl` had been
  silently falling back to software (`EntryPointNotFoundException`). Fixed;
  `GPU backend=gl probe ok; GL_VERSION=4.6.0 NVIDIA 610.88` and the slice-1
  golden script pass again.
- **Opt-in live window (`--window`, brief O2; T1 conditions met; no new
  dependency):** `WglContext.TryShowWindow`/`SwapBuffers`/`PumpMessages`
  (+swap interval 0), `GlPgraphBackend.PresentToWindow` after the readback
  (sink only — blit FBO -> default framebuffer, swap, pump, title counters),
  `IPgraphBackend.Pump()` default no-op pumped between batches, 2M-step
  batch cap in window mode. Live: `GL window presents=35 status=window
  640x480 vsync=off`, `BackendUnsupported=0`; guest counters identical to a
  `--gpu sw` control at the same steps; WGL-guarded `WindowPresentLiveTests`.
  Headless `--gpu sw` stays the default and gate currency; `--window` is an
  observation sink (no input, no vsync).

### Flag status at session close (2026-10-17; §185) — idle-skip is now a REGRESSION

`--idle-skip` measured on the current binary: menu 112.4 s vs 77.6 s (0.69x),
triangle 82.2 s vs 35.5 s (0.43x) with `--spec-exec`; triangle cache-only
56.5 s vs ~41.3 s. The skip machinery's fixed per-iteration cost did not
shrink while the JIT slices made base execution 2-3x faster, so the §119
1.10x/1.66x win inverted. SHA-exact every leg; flag stays functional.
Recommendation: retire or rework (operator decision) — do not use on the
goldens. SoftTlb 256->4096 entries also measured neutral (77.9 vs 77.6 s;
the boot's TLB misses are flush-driven, not capacity-driven) and was
reverted.

1. **SLICES 5–11 DONE (§§142–148, direct development).** After wave-15
   (slices 1+2, 1340 tests) came the §141 median-of-3 (flag stays
   opt-in: menu 1.035x, triangle 1.130x), then slices 5–8 (ALU-mem,
   Jcc, near Jmp/Call/Ret, shifts + Movzx/Movsx; §§142–145) and tonight
   slices 9–11: Inc/Dec-with-memory (§146, 6 handlers), MMX Movq/Movntq
   (§147, 4 handlers, both now continue blocks), Prefetch + fences
   (§148, 1 shared nop handler). Each slice: unit differential +
   resolver pins, full suite green, 2-leg 1B spec goldens (SHAs exact
   both titles, `vec=0`, `verifyFailures=0`, counter anchors exact).
   Fallbacks menu 392M→103.7M / triangle 494M→129M across slices 5–11;
   walls saturated (~1.04–1.16x — §138 confirmed, helpers dominate).
   **1367/1367 tests after slices 12-20 + memory-path slice 1 below.** Flag stays opt-in
   (median-of-3 post-slices-13–20: menu 1.072x / triangle 1.185x —
   real but thin; soak binaries must come from a FULL solution build
   — §145 lesson).
   Then memory-path slice 1 (§161): RAM-direct RawRead/RawWrite +
   64-bit direct + XMM128/FPU80 single-translate (observers-null
   gated) — no new shapes, active every mode; 4-leg golden gate PASS
   (spec SHAs exact both titles, flag-off byte-identical,
   `--block-exec` exact). **1367/1367 tests**, format clean.
   Then memory-path slices 2+3 (§§162–163, same night): Bytes bulk
   path (page-chunked one-translate-per-page + span copy via new
   `PhysicalMemory.WriteSpan`; Fxsave 512 B goes 512 translates →
   1–2) + 4-entry data-translate cache in `TranslateLinear` (new
   `Mmu.TranslateDataFast`; WP fault replayed exact; tracking
   always-on). Joint 4-leg golden gate PASS (`D:\spec-soak\slice22`:
   triangle spec 98.9 s + menu spec 187.9 s SHAs exact, menu cache
   207.4 s byte-identical, `--block-exec` SHA exact; all single-run,
   no claim). **1375/1375 tests**, format clean.
   Then memory-path slice 4 (§165, same night): rep movs/stos span
   path (page-chunked, order-exact; ZF/IO strings stay legacy). Caught
   a real ESI-on-stos bug via the gate (tests green, triangle halted
   at 6.6M; fixed + ESI pin). 4-leg gate PASS (`D:\spec-soak\slice23`:
   triangle spec 82.2 s + menu spec 154.7 s SHAs exact, menu cache
   166.0 s byte-identical, `--block-exec` SHA exact; single-run, no
   claim; §164 median stands). **1385/1385 tests**, format clean.
   Fresh median-3 with slice 4 (§166): menu 1.088x / tri 1.161x
   (12/12 PASS; absolutes host-shifted, ratios robust). Soak script
   hardened (stale-BMP SHA hole closed).
   Dispatch slice 5 (§167): Cli/Sti continue blocks (2-token change;
   dispatches tri −32% / menu −14%, interrupts bit-identical
   1883/3153; behavior-neutral by construction). 4-leg gate PASS
   (`D:\spec-soak\slice24`). **1390/1390 tests**.
   Shape slice 6 (§168): Cli/Sti spec handlers (fallbacks tri
   113.3M→185k / menu 34.1M→1.7M; interrupts identical). 4-leg gate
   PASS (`D:\spec-soak\slice25`). **1391/1391 tests**.
   Median-3 post-slices-5+6 (§169): menu 1.084x / tri 1.231x
   (12/12 PASS; reads as real — spec-only gains vs controls).
   Specialization at asymptote; emitter next. Flag stays opt-in.
   Shape slice 7 (§170): Adc/Sbb all forms + FPU-control/Emms/Fwait
   (fallbacks −52%/−28%; stale carry-live label retired). 4-leg gate
   PASS (`D:\spec-soak\slice26`). **1392/1392 tests**.

    Emitter E1 (§171, morning session): first DynamicMethod slice behind
    opt-in `--emit` (per-block tail runners, direct static calls, all
    boundaries retained, invalidation-gated). 8-leg golden gate PASS
    (`D:\spec-soak\slice27`): SHAs exact both titles, `vec=0`, anchors
    exact, interrupts identical 1883/3153. VERDICT wall-negative: emit
    1.93x/6.50x slower than spec (triangle/menu) -- per-runner
    construction+first-JIT (~0.7 ms) cannot amortize over short churned
    tails; §138 negative confirmed at title scale. Flag stays opt-in
    (zero impact). Serve/exit tallies bit-identical incl. a 7-position
    delivery sweep. ktest smoke PASS 7/7 (01:45). **1410/1410 tests**,
    format clean. E2 LANDED (§172-174): tiered emission (lazy builds + block-attached
    runners + Context hoisting), threshold tuned 8-->64-->256-->1024.
    Slice30 gate: SHAs exact both titles, triangle emit 66.0 s < spec
    76.0 s, menu emit 180.3 s ~= spec 182.2 s (parity, was 6.50x at E1);
    builds menu 1.24M-->5.8k. Serve layer no longer the bottleneck
    (E3 body inlining next, scoped §175). Median-of-3: triangle emit
    1.089x spec, menu emit 0.944x spec (first-triplet median; second
    triplet 148.0/150.3/154.1 confirms r1 warmup outlier, steady state
    at parity-with-slight-deficit). Smoke PASS 7/7. **1410/1410 tests**.
    E3-0/E3-1 LANDED (§§175-176, direct): `--shape-tally` served-shape
    histogram (default-off, suppresses emit; 1B rankings in §176 -- the
    twins' shapes are 34.9% of menu / 35.3% of triangle members); E3-1a
    `--emit-inline` context-hoisted lean twins for 8 memory shapes (twins
    derived from ResolveSpec handlers; hotspot branches folded out under
    the RunEmitTail guard); E3-1b baked IL fast paths for the two 32-bit
    Mov reg<->mem shapes (inline address + translate-cache probe +
    RAM-direct read/write; every non-fast case falls back to the exact
    twin; Mmu.Observer hoisted per invocation). +8 EmitTests (differentials
    incl. FS/GS, 16-bit addressing, watched store, non-RAM physical,
    mid-tail fault precision, gates) + 3 tally tests. Gate
    `scripts/emit-gate.ps1`; walls in §176: two interleaved median-of-3
    triplets (e31: menu emit 174.2 s vs emit-inline 171.4 s = 1.016x;
    e32: 146.4 vs 150.5, **sign flips**) -> **wall-neutral within host
    noise** (triangle 1.015-1.037x single-run, no claim). Flag stays
    opt-in. **1421/1421 tests**. E3-1c (§177, same day): fast kinds
    extended to Cmp reg/mem/imm + Test mem,imm + 8-bit Mov
    (fast-eligible shapes 35.1% of menu / 34.6% of triangle members;
    550/550 inline members baked fast on the menu smoke); walls in §177.
    E3-1d diag (§178): `--emit-diag` baked counters -> fast-path hitRate
    **0.9161 menu / 0.9831 triangle** (fallbacks ~98% tag misses, no
    thrash); wall-neutral is a scale fact (16-18% of all members
    fast-served at a few ns each; only ~50% of fast-kind members are
    tail-served at all). Further body work is low-value against the
    boundary-dominated remainder; flag stays opt-in.
    QUIET-HOST VERDICT (§§179-179b, supersedes the noisy §§176-177
    readings): 42-leg interleaved campaign, all SHAs exact. TRIANGLE
    (n=7): spec 71.6 / emit 64.3 / **emit-inline 63.0** s -- inline wins
    5/7 paired rounds, **1.021x vs emit, 1.137x vs spec**. MENU (n=7):
    emit 141.8 / spec 143.7 / emit-inline 144.4 s -- **parity across all
    three modes (+-2%)**, mixed per-round signs; menu deltas are below
    median-of-3 resolution even on a quiet host (n>=7 is the sub-2%
    standard; mixed signs = parity, never averaged into a win). Flag
    stays opt-in; a default-mode change is an operator decision.

2. **SLICES 12–20 DONE (§§149, §151–158, same evening).** Scalar SSE (Addss/Subss/
   Mulss/Divss/Minss/Maxss + Cvtss2Si/Cvttss2Si, 16 handlers, all
   continue blocks) behind opt-in `--spec-exec`; real F3-vs-REP
   discovery en route (`Decoder.cs:72-73,94` — 0xF3 is both mandatory
   prefix and stamped Rep; shared `IsMandatoryF3Scalar` gate exempts
   exactly the 8 shapes, `rep movsb` pinned null). Then slice 13 from
   the §150 scope: x87 loads/stores/add/multiply (7 shared bodies,
   ~25 shapes) — fallbacks menu 101.8M→**49.3M (−52%)**, dispatches
   −18%, fastest spec yet (single run, no claim). 2-leg 1B spec
   goldens PASS each slice (SHAs exact, `vec=0`, `verifyFailures=0`)
   + `--block-exec` cross-check leg PASS. Then slice 14 (§152): x87
   reg-forms + Fsub/Fdiv families (19 handlers) — fallbacks menu
   49.3M→**43.5M (−12%)**. 2-leg 1B spec goldens PASS each slice
   (SHAs exact, `vec=0`, `verifyFailures=0`). **1353/1353
   tests**, format clean, ktest smoke PASS 7/7 post-slices 9–11
   (792,800,535 steps bit-reproducible).
   Then slice 15 (§153): shifts-by-CL + Push-m + Mul/Imul (8 shared
   bodies through the interpreter helpers, ~50 shapes, no whitelist
   change) — fallbacks menu 43.5M→**38.4M (−12%)**, menu 168.3 s
   fastest spec yet (single run, no claim). 2-leg 1B spec goldens PASS
   + `--block-exec` cross-check PASS. **1354/1354 tests**, format clean.
   Then slice 16 (§154): integer remainder + x87 integer arithmetic
   (Neg/Not/Div/Idiv/Bswap/Xchg/Leave + Fiadd-family/Fnstsw, 15
   handlers) — fallbacks menu 38.4M→**36.2M**. 2-leg 1B spec goldens
   PASS + `--block-exec` cross-check PASS. **1355/1355 tests**,
   format clean.
   Then slice 17 (§155): x87 compares + sign/test + tag control
   (16 handlers — x87 data fully absorbed on both titles) —
   fallbacks menu 36.2M→**35.7M**. 2-leg 1B spec goldens PASS +
   `--block-exec` cross-check PASS. **1356/1356 tests**, format clean.
   Then slice 18 (§156): packed-SSE moves (9 handlers incl. the F3-gate
   ninth member) — fallbacks menu 35.7M→**34.8M**. 2-leg 1B spec
   goldens PASS + `--block-exec` cross-check PASS. **1357/1357
   tests**, format clean.
   Then slice 19 (§157): SSE sqrt/rcp + packed arithmetic/logicals/
   compares (5 shared bodies, 20 mnemonics) — fallbacks menu
   34.8M→**34.6M**. 2-leg 1B spec goldens PASS + `--block-exec`
   cross-check PASS. **1358/1358 tests**, format clean.
   Then slice 20 (§158): carry-rotates + sign-extension + flags +
   cond-move + TSC (9 handlers) — fallbacks menu 34.6M→**34.1M**.
   2-leg 1B spec goldens PASS + `--block-exec` cross-check PASS.
   **1359/1359 tests**, format clean.
3. **Standing operator rule: NO real Xbox games.** All title work uses
   `data/test-samples/`. 007/real-game probes deferred. **Superseded
   2026-10-24:** retail gameplay is M-G; the optimized ISOs at
   `H:\Tests\XBOXReady` are the lead acceptance (never merge gates), and the
   xemu port (M-P) follows.
4. **Operator decision: swapchain/windowing** — brief filed at
   `docs/research-notes/gpu-swapchain-brief.md` (O2 = opt-in `--window`,
   pure user32/gdi32 interop on the existing WGL context, vsync off,
   ~400–600 LOC; O1 headless stays the default and gate currency; O3
   needs an operator override of the single-dependency rule; triggers
   listed in the brief).
5. **Next JIT step:** slices 1+2 and 5-20 are in (`--spec-exec`,
   `--block-exec`, `--decode-cache` default-on per §137 with
   `--no-decode-cache` opt-out; `--fetch-fast`/`--decode-verify` stay
   default-off) plus memory-path slice 1 (§161: RAM-direct +
   wide-access single-translate, mode-independent). Slices 13-20 cut
   the menu fallback total 101.8M→34.1M (x87 fully absorbed on both
   titles); median-of-3 post-slices-13–20 stands at menu 1.072x /
   triangle 1.185x (§160). Memory-path build per §159 now complete:
   slice 1 (RAM-direct/wide-S1) + slices 2+3 (Bytes bulk, xlat cache)
   + slice 4 (rep-string span); RMW compute-once done by inspection
   (hot handlers hoist; interpreter keeps fault-priority semantics).
   Fresh median-3 post-slices-1–3 (§164): menu 1.070x / tri 1.164x →
   flag stays opt-in (flip calculus is the ratio). Remaining shape
   tail: Sbb/Adc (carry-live,
   out), Cmpxchg8B, Fldcw/Fnstcw/Fwait, Shufps + smalls, Movs/Stos
   (rep bulk landed §165 but shapes still interpreter-dispatched),
   Cli/Sti (untouchable) — all <100k or deliberate
   outs; shape work is done except daylight items. DynamicMethod tried
   in §171 (E1: wall-negative, construction-dominated) and stays opt-in;
   E2 (threshold-tiered emission) scoped next. NXDK toolchain
   (operator/host) still the mesh blocker.
6. **The wave runner is retired (operator decision 2026-10-15).**
   `tools/wave/` will be deleted; no further waves. Development continues
   directly (no BOSS protocol, no worktrees, no detached servers). Tool
   lessons recorded in §140 provenance (fresh session after
   `provider.invalid-output`; ENOENT save-retry already in `state.ts`).
   ktest smoke stays the standing gate.
7. `F36A2`/LBA 8 CLOSED as no-bug (§73): LBA 8 is the Xbox config
   sector (Partition0 network settings; `PCBX`/`55 AA` = its format, not
   MBR) under kernel RMW at step ~656M (EIP `0x8002430E`, IDENTIFY +
   READ/WRITE DMA, faithful serving, no fault); `F36A2` is the §49–51
   WC-fill page artifact, unrelated to disk I/O. Track C JIT for
   interactivity still the throughput bottleneck.
8. Parallel tracks when blocked: Track C JIT for interactivity (5B-step
   batch runs take ~6 min at ~14M insn/s Release; true interactive
   rates still need JIT).
9. Reference oracles: `xemu -trace` + QEMU monitor (`xp` physical reads; xemu
   deadlocks at the animation wait, so it is no longer an oracle past that
   point); xemu `hw/xbox/nv2a/pgraph/` (`methods.h.inc`, `draw.c`, `surface.c`,
   `vsh-prog.c`), `nv2a_regs.h`; for the APU gate xemu `hw/xbox/mcpx/`
   (`vp/`, `dsp/`, `apu.c`);
   `References2/Cxbx-Reloaded-master/src/core/kernel/common/types.h` for
   KTHREAD/KWAIT offsets if needed.

Earlier AV-device gate details (research-notes §33, for reference):



