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

## Development direction (required, 2026-10-16)

Canonical order: `docs/development-direction.md`. Summary of hard rules:

1. **JIT throughput first** — CPU is 97–100% of wall; swapchain/GPU is 0–2.9%.
   Specialize semantics; switch dispatch is not dominant; DynamicMethod stays
   rejected; boundary-preserving only.
2. **Headless stays default** (`--gpu sw`); never mix, no async. No window/Vulkan
   and no NV2A fidelity polish until a `data/test-samples/` histogram proves need.
3. **No real Xbox games** — `data/test-samples/` only (operator rule).
4. **Perf flags stay opt-in** until median-of-3 + exact SHAs/counters +
   `verifyFailures=0` + flag-off byte-identical. ktest smoke 7/7 is the standing gate.
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
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
   once; conflicts were purged 2026-10-09). Baseline: 1350 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — slices 9–11 landed (2026-10-16 evening); wave runner retired.

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
   **1356/1356 tests after slices 12-17 below.** Flag stays opt-in
   (saturation + no median-of-3 per slice; soak binaries must come
   from a FULL solution build — §145 lesson).
2. **SLICES 12–17 DONE (§§149, §151–155, same evening).** Scalar SSE (Addss/Subss/
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
3. **Standing operator rule: NO real Xbox games.** All title work uses
   `data/test-samples/`. 007/real-game probes deferred.
4. **Operator decision: swapchain/windowing** — brief filed at
   `docs/research-notes/gpu-swapchain-brief.md` (O2 = opt-in `--window`,
   pure user32/gdi32 interop on the existing WGL context, vsync off,
   ~400–600 LOC; O1 headless stays the default and gate currency; O3
   needs an operator override of the single-dependency rule; triggers
   listed in the brief).
5. **Next JIT step:** slices 1+2 and 5-17 are in (`--spec-exec`,
   `--block-exec`, `--decode-cache` default-on per §137 with
   `--no-decode-cache` opt-out; `--fetch-fast`/`--decode-verify` stay
   default-off). Slices 13-17 cut the menu fallback total 101.8M→35.7M
   (x87 fully absorbed on both titles); median-of-3 still required
   before any wall claim. The next lever after shapes is the
   memory-path design (`ReadMemory`/`WriteMemory` → translate + bus),
   a daylight scoping task. Remaining shape tail: packed-SSE moves
   (Movss/Movups/Movntps/Movhlps) + Rsqrtss, Rcl-imm, Sbb/Adc-imm
   (carry-live, out), Cdq, Pushf, Movs (string, out), Cli/Sti
   (untouchable). DynamicMethod stays rejected unless new numbers say
   otherwise. NXDK toolchain (operator/host) still the mesh blocker.
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

