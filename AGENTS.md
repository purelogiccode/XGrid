# XGrid — Agent Notes

## Reference-first process (required)

Before implementing or debugging hardware behavior, **consult the reference projects first**
to confirm layout, IDs, registers, and semantics. Extract facts, never copy code.

- `References2/xemu-0.8.136` — LLE reference (MCPX, PCI, IDE, SMBus, NV2A). Primary source.
  Key paths: `hw/xbox/nv2a/` (GPU pipeline/registers), `hw/xbox/mcpx/` (APU/ACI/nvnet),
  `hw/xbox/smbus_xbox_smc.c` (SMC protocol), `hw/xbox/xbox_pci.c` (PCI topology/IDs).
- `References2/xemu-Playtest-v2` — **in-development xemu fork for Sega Chihiro (arcade)**
  on a QEMU 10.2.0 base. Its NV2A is the *tuned GPU* (`pgraph/cost.c`, `seed.c`,
  `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the un-tuned baseline at
  `hw/xbox/nv2a-stock/` — `diff -ru` isolates the whole perf delta (GPU-backend
  performance-track facts). Full `chihiro/` subsystem, `xid-light-gun.c`, new SMBus
  devices, 5 newer NV097 methods. Cross-check only; 0.8.136 stays the primary
  (see `docs/research-notes/xemu-playtest-v2.md`).
- `References2/xqemu-master` — older QEMU-based LLE reference (useful for simpler code paths).
- `References2/Cxbx-Reloaded-master` — HLE, but its kernel headers are the NT-layout reference:
  `src/core/kernel/common/types.h` (KTHREAD/KWAIT_BLOCK/KAPC offsets, e.g. KTHREAD+0x2C = State).
  Cxbx is also the shader reference (`nv2a_psh.cpp` / `nv2a_vsh.cpp`) — `xemu` `glsl/psh.c` +
  `glsl/vsh-prog.c` are GPL-2+ and are the more complete oracle for the fragment/vertex stages.
- ~~`References2/Xermu-main`~~ — **WITHDRAWN.** An earlier draft listed a standalone C#
  emulator that was never in the workspace. Operator confirmed 2026-10-13 the primary LLE
  reference is `References2/xemu-0.8.136`. `docs/research-notes/xermu.md` was deleted; historical
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
through that junction. If a reference is missing, restore in this order — never work
around a missing reference: (1) `D:\xgrid-refs-restore\` (verified full backup
2026-10-14: 31,801 files / 513 MB, all 8 sets); (2) a client-accessible Windows shadow
copy of C: (VSS); (3) the upstream URLs in `docs/research-notes/sources.md`
(`xemu-Playtest-v2` has no upstream URL — operator-supplied only).
Renamed `References/` → `References2/` 2026-10-16 by the operator (repeated
remote-origin deletions of `References/`); both names stay git-ignored.

Record extracted facts in `docs/research-notes/xgrid-architecture-decisions.md` (with source
file references) and update `ImplementationPlan.md` status when a device or phase changes.

## Development direction (required, 2026-10-19)

Canonical order: `docs/development-direction.md`; fast-mode JIT track:
`docs/research-notes/jit-track-charter.md`. Summary of hard rules:

1. **Track C (CPU engine) leads the xemu-class campaign again (re-aimed
   2026-10-19 evening, §199).** Day result (1B, fast mode): triangle 27.9 →
   **20.28 s sw / 10.68 s gl+replace**; menu 64.0 → **56.82 s sw / 41.39 s
   gl+replace** — but the *structure* inverted: the CPU engine is again the
   largest line item (triangle runner 6.1 s of 10.68 s = 57%; menu CPU ≈17–19 s
   of 41.39 s ≈45%), and the `DynamicMethod` block JIT is at its structural
   ceiling (~18 ns fixed per block × 125M invocations + ~7 ns per member ×
   543M). The charter's Phase-3 entry condition is **met** (plateau >3× short
   of xemu-class; xemu ≈830 Msteps/s on the triangle vs our 94M/24M).
   Order: **C2 trace emitter** (multi-block traces, state in locals across the
   trace, inline RAM fast paths, same boundary polls; ≥2× CPU) → **C3 raw
   emitter** (Iced now unblocked: register allocation, direct block chaining,
   inline TLB; another ≥2×). Cheap Track-D wins first (D3b page-end
   wait/residue 1.9 s/3.6 s; bookkeeping 2.2 s/4.8 s profiling). Track B
   follows: **B2c draw batching** (2 µs/draw × 53M draws on the real dashboard
   = 106 s/4B steps) and the **window present path** (a redundant 1.6 ms
   readback per flip in window mode). Accurate-mode rules unchanged
   (specialize semantics; boundary-preserving; `DynamicMethod` stays opt-in
   via `--jit`; the raw emitter is now authorized by charter §4 Phase 3).
2. **Headless stays default** (`--gpu sw`); never mix, no async. No window/Vulkan
   and no NV2A fidelity polish until a `data/test-samples/` histogram proves need
   (fast-mode Phase-4 playability is chartered separately; gate runs unaffected).
3. **Real-content policy (2026-10-19, operator-delegated).** `data/test-samples/`
   stays gate currency (goldens, ktest smoke, acceptance). Real Xbox ISOs at
   `H:\Tests\XBOXTest` are authorized for fast-mode **benchmark-only** use
   (wall/throughput, xemu-class comparisons) — never gates, never accurate-mode
   acceptance, never fidelity work.
4. **Two-tier perf validation.** Accurate-mode promotion + every default flip:
   median-of-3 + exact SHAs/counters + `verifyFailures=0` + flag-off
   byte-identical. Fast-mode exploration: bench + unit differentials
   (charter §5). ktest smoke 7/7 is the standing gate.
5. **Direct development** — wave runner retired, no worktrees/detached servers.

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
- **C3 raw emitter (2026-10-20, §§199e/199f; correctness campaign 2026-10-19→20, §199g; opt-in
  `--jit-raw`).** One native x86-64 function per block (Iced 1.21.0, MIT): guest GPRs in fixed host
  registers, arithmetic flags in the host FLAGS register, memory members running the data
  translate-cache probe inline against pinned pointers, and a reverse-P/Invoke trampoline to the
  existing spec handlers for every unencoded shape. **The correctness campaign closed the JitTests
  gate: 12 → 0 failures with `JitRawBlocks` forced on (56/56 JitTests+RawJitTests, suite 1492
  green)** and fixed fourteen latent bugs (decisions §199g): missing non-terminator EIP advance,
  helpers seeing a stale EIP, CLI/STI clobbering guest EAX, the missing fast-mode post-STI/POPF
  poll, the translate-cache probe indexing by raw VPN (out-of-bounds ≥ 1 MB), unguarded inline
  stores (NoteStore/watch), the poll's shadow-space overlap, host DF left set across helper calls
  (Win64 ABI — runtime `rep movs` corruption), flag-liveness leaking from slow paths into merge
  points, the store guard's helper call clobbering guest EAX/ECX/EDX, GC-stale CPU/entries
  pointers (weak GCHandle + pinned entries array), memory-source MovExtend emitted as a register
  move, disp32 ≥ 0x80000000 failing to encode, and WP/A-D parity with the IL probe. **Open: the
  IL-fast vs raw-fast differential matches bit-exactly through 25,053,067 triangle steps, then
  diverges inside a raw block at `0x8001F9EB` (16-bit store to a page-table page; raw takes a #PF
  at EIP=0x8001F9F3 CR2=0x0D that the IL run does not) — fix, then 1B menu+triangle + quiet-host
  measurements before any default flip.** No performance claim yet (loaded-host single runs show
  ~2-5× msps informational only).
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
   once; conflicts were purged 2026-10-09). Baseline: **1492 tests green**,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — slices 14–20 + median-of-3 landed (2026-10-17 early morning); wave runner retired.

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
RawJitTests pass with the raw path forced on; suite 1492 green; build and
format clean.** The raw path stays **opt-in**; the remaining gate is the
IL-fast vs raw-fast title differential — it matches bit-exactly through
**25,053,067 triangle steps**, then diverges inside a raw block at
`0x8001F9EB` (16-bit store to a page-table page; raw takes a #PF at
EIP=0x8001F9F3 CR2=0x0D the IL run does not) — fix that, then 1B menu+triangle
plus quiet-host measurements before any default flip.

Recipe for the open divergence: `boot … --jit --jit-threshold 16 --jit-spin
--jit-raw --steps 25053130 --gpu sw` vs the same without `--jit-raw`; RAM SHA
bisect bounds it to steps [25,053,067, 25,053,130]; the raw block at head
`0x8001F9EB` is `movzx edi,di; shl edi,4; add edi,ecx;
mov [0x83FF0002+edi*4],bx; jmp` and its dump was read end-to-end (address
computation, guard and note path all match the IL semantics) — the fault is
`err=2 CR2=0x0D`, so the next step is comparing the faulting store's linear
address and the pre-fault registers (a `--save-state` at 25,053,124 does not
land mid-raw-block, so instrument the emitter or the helper).

Diagnostics to keep: XGRID_SLOW=1 (slow-member mnemonic mix), XGRID_NONRAM=1
(non-cacheable fetch pages), --jit-timing (runner vs chain split), Host timing
(execute vs advance), `XGRID_RAW_DUMP=1` (per-block Iced disassembly to
`%TEMP%\xgrid-raw-<head>.txt`), **XGRID_RAW_TRACE=1** (per-invocation
enter/exit + spec-handler trace to `%TEMP%\xgrid-raw-trace.txt`; file-based on
purpose — a hard crash loses buffered stderr), XGRID_RAW_THRESHOLD (test-side
build-threshold override for bisecting), **XGRID_RAW_SKIP_HEADS=hex[,hex…]**
(forces the listed raw heads to the IL runner — used to confirm the
`0x8001F9EB` block as the source of the 25.05M divergence).

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

