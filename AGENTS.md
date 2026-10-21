# XGrid — Agent Notes

## Reference-first process (required)

Before implementing or debugging hardware behavior, **consult the reference projects first**
to confirm layout, IDs, registers, and semantics. Extract facts, never copy code.

- `References2/xemu-0.8.137` — LLE reference (MCPX, PCI, IDE, SMBus, NV2A, APU/DSP). **Primary
  source, adopted 2026-10-20** (0.8.136 stays vendored as the prior snapshot). Key paths:
  `hw/xbox/nv2a/` (GPU pipeline/registers), `hw/xbox/mcpx/` (APU/ACI/nvnet),
  `hw/xbox/mcpx/apu/dsp/` (GP/EP DSP interpreter + optional JIT),
  `hw/xbox/smbus_xbox_smc.c` (SMC protocol), `hw/xbox/xbox_pci.c` (PCI topology/IDs).
  Delta vs 0.8.136: `docs/research-notes/xemu-0.8.137.md`.
- `References2/xemu-Playtest-v2` — **in-development xemu fork for Sega Chihiro (arcade)**
  on a QEMU 10.2.0 base. Its NV2A is the *tuned GPU* (`pgraph/cost.c`, `seed.c`,
  `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the un-tuned baseline at
  `hw/xbox/nv2a-stock/` — `diff -ru` isolates the whole perf delta (GPU-backend
  performance-track facts). Full `chihiro/` subsystem, `xid-light-gun.c`, new SMBus
  devices, 5 newer NV097 methods. Cross-check only; 0.8.137 stays the primary
  (see `docs/research-notes/xemu-playtest-v2.md`).
- `References2/xqemu-master` — older QEMU-based LLE reference (useful for simpler code paths).
- `References2/Cxbx-Reloaded-master` — HLE, but its kernel headers are the NT-layout reference:
  `src/core/kernel/common/types.h` (KTHREAD/KWAIT_BLOCK/KAPC offsets, e.g. KTHREAD+0x2C = State).
  Cxbx is also the shader reference (`nv2a_psh.cpp` / `nv2a_vsh.cpp`) — `xemu` `glsl/psh.c` +
  `glsl/vsh-prog.c` are GPL-2+ and are the more complete oracle for the fragment/vertex stages.
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

## Development direction (required; course-corrected 2026-10-20)

Canonical order: `docs/development-direction.md`; fast-mode charter:
`docs/research-notes/jit-track-charter.md`; corpus plan:
`docs/research-notes/compatibility-corpus.md`; sample matrix:
`docs/compatibility.md`. Hard rules:

1. **Track B (host GPU present path) leads.** The product frontend
   (`XGrid.Frontend.Avalonia`, `ea8b38f`) renders on the software rasterizer and
   is GPU-bound. `--gpu gl --gpu-replace` exists but nets only 2.2 s on the menu
   (31.1 vs 33.3 s): ~9.9 s of GL draw/readback/present. The per-flip readback is
   1.6 ms (≈1.1 s/1B), so ~8.8 s is unexplained. **First slice: measure the split
   (submission / readback / present), then remove the readback from
   fast-mode/window present** (keep it for gates, screenshots, `--frame`).
   Acceptance: menu fast+gl ≤ 24 s, triangle ≤ 6 s, `BackendUnsupported=0`,
   guest SHAs/counters exact. The 2-pixel k1 interleave is demoted (≥2%
   acceptance, bounded ceiling).
2. **Track E (corpus) is the fidelity steering signal.** Gate currency stays
   `data/test-samples/`, but fidelity work needs observed usage: maintain
   `docs/compatibility.md` (boot stage, first frame, known gap, step anchor) and
   a boot-to-first-frame harness; widen the sample set with legal
   homebrew/demos (nxdk samples — toolchain is the host blocker, xgu examples,
   nxdk_vsh_tests, OpenXDK). Real ISOs stay benchmark-only. **No NV2A/APU
   fidelity polish beyond the matrix + a sample histogram.**
3. **Track P (product completeness) runs in parallel:** host audio sink,
   physical gamepad binding, save-state V1, per-title config. The frontend is
   currently sw-only, keyboard-only, silent, stateless.
4. **Track C (CPU engine) is frozen.** C3-x hit its target; C3-y/xlat measured
   neutral and were reverted; C3-z is deferred (2–3%). Only action: the
   operator's `--jit`/`--jit-raw` default-flip decision (the frontend already
   defaults both on). Report ns/served member + wall per presented frame, not
   1B-step walls (530M triangle steps are idle-spin).
5. **Track D only with a measured >2% ceiling** (page-end wait, bookkeeping).
6. **Gates unchanged.** Accurate mode is currency: median-of-3, exact
   SHAs/counters, `verifyFailures=0`, flag-off byte-identical; ktest smoke 7/7.
7. **Direct development** — wave runner retired, no worktrees/detached servers.

**Position (2026-10-20, 1B fast mode, quiet host):** triangle 8.69 s sw /
6.49 s gl+replace, menu 33.3 s sw / 31.1 s gl+replace; xemu ≈830 Msteps/s on
the triangle (~7× gap), the menu is ~27× real time. 1 guest-second ≈ 733M
steps. The software rasterizer is 12.0 s of the menu wall; the GL path replaces
it but still has ~9.9 s of draw/readback/present overhead (readback alone is
~1.6 ms/flip) — measure the split, then remove the readback. That is the lead
lever.

## Build / test / format

- `dotnet restore XGrid.sln` (after project-graph changes)
- `dotnet build XGrid.sln -c Release --nologo --no-restore`
- `dotnet test XGrid.sln -c Release --nologo --no-build`
- `dotnet format XGrid.sln --no-restore` then `--verify-no-changes`

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
  `XGrid.Host` API: Home with live software-rasterizer frames, Library
  (ISO/CHD/XBE scan), Settings (firmware/storage/RAM/JIT/network/library/accent),
  Activity log, and About. In-process emulation on a background thread; frames via
  `Nv2aMmio.TryCopyPresentedFrame` into a `WriteableBitmap`; no WGL dependency.
  Verified live on the EvoX dashboard plus pause/resume/power cycles and settings
  persistence. UI-only: no core/CLI/test changes. Design note:
  `docs/research-notes/avalonia-frontend.md`; run
  `dotnet run --project src/XGrid.Frontend.Avalonia`.
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
  work uses `data/test-samples/`** (`triangle.xbe`, `nxdk-hello/led/
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
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
   once; conflicts were purged 2026-10-09). Baseline: **1538 tests green**,
  `dotnet build`/`format --verify-no-changes` clean.

## Session notes — newest first (wave runner retired 2026-10-15).

**Session 2026-10-20 (fourteenth; Track E corpus unblocked, §207).**
**M-B is met: 11 rows over 7 samples, every row reproducible.** The blocker was
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
evidence; kill criteria; two-tier validation; license stays non-GPL). The
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
   `data/test-samples/`. 007/real-game probes deferred.
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

