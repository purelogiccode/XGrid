# XGrid — Agent Notes

## Reference-first process (required)

Before implementing or debugging hardware behavior, **consult the reference projects first**
to confirm layout, IDs, registers, and semantics. Extract facts, never copy code.

- `References/xemu-0.8.136` — LLE reference (MCPX, PCI, IDE, SMBus, NV2A). Primary source.
  Key paths: `hw/xbox/nv2a/` (GPU pipeline/registers), `hw/xbox/mcpx/` (APU/ACI/nvnet),
  `hw/xbox/smbus_xbox_smc.c` (SMC protocol), `hw/xbox/xbox_pci.c` (PCI topology/IDs).
- `References/xemu-Playtest-v2` — **in-development xemu fork for Sega Chihiro (arcade)**
  on a QEMU 10.2.0 base. Its NV2A is the *tuned GPU* (`pgraph/cost.c`, `seed.c`,
  `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the un-tuned baseline at
  `hw/xbox/nv2a-stock/` — `diff -ru` isolates the whole perf delta (GPU-backend
  performance-track facts). Full `chihiro/` subsystem, `xid-light-gun.c`, new SMBus
  devices, 5 newer NV097 methods. Cross-check only; 0.8.136 stays the primary
  (see `docs/research-notes/xemu-playtest-v2.md`).
- `References/xqemu-master` — older QEMU-based LLE reference (useful for simpler code paths).
- `References/Cxbx-Reloaded-master` — HLE, but its kernel headers are the NT-layout reference:
  `src/core/kernel/common/types.h` (KTHREAD/KWAIT_BLOCK/KAPC offsets, e.g. KTHREAD+0x2C = State).
  Cxbx is also the shader reference (`nv2a_psh.cpp` / `nv2a_vsh.cpp`) — `xemu` `glsl/psh.c` +
  `glsl/vsh-prog.c` are GPL-2+ and are the more complete oracle for the fragment/vertex stages.
- ~~`References/Xermu-main`~~ — **WITHDRAWN.** An earlier draft listed a standalone C#
  emulator that was never in the workspace. Operator confirmed 2026-10-13 the primary LLE
  reference is `References/xemu-0.8.136`. `docs/research-notes/xermu.md` was deleted; historical
  citations to it (early plan text, `xgrid-architecture-decisions.md`) are unverified — do not
  use them as facts (see `sources.md` "Xermu - WITHDRAWN").
- `CSharp_XISOSharp` — MIT XDVDFS library: sibling repo (`C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp`,
  docs + source used for API/on-disk facts) + NuGet `XISOSharp` 1.4.2 referenced by `XGrid.XboxFs`
  (`XdvdfsFileSystem` adapter + `xgrid xdvdfs ls/cat`, §64). `CSharp_CHDSharp` sibling covers CHD later.
- `References/XboxDevWiki` — XboxDevWiki vendored locally (full offline dump
  captured 2026-10-12: `html/<Title>.html` rendered pages with offline
  links/images, `pages/<Title>.wiki` raw wikitext, `images/`, `manifest.json`,
  `index.html`; identify pages by `<title>`). Key pages: Boot Process, BIOS,
  Kernel, MCPX, PCI, USB, NV2A, SMC, MCPX ROM, Hidden Boot Code, Memory.
  Old numeric citations (`51` Boot Process, `53` FATX, ...) translate per
  `References/XboxDevWiki/README.md`.
- `References/envytools-master` — **MIT** NVIDIA register/method database + GPU docs
  (notes: `docs/research-notes/envytools.md`). First cross-check for NV2A method
  offsets/enums: `rnndb/graph/nv20_3d.xml` (+ `nv10_3d.xml`) = Xbox class `0x0097`
  (`0x17FC` begin/end, `0x1800/0x1810/0x1818` elements/arrays/inline, `0x0B00/0x0B80`
  VP upload); pusher/puller semantics in `docs/hw/fifo/dma-pusher.rst` + `puller.rst`;
  vertex-program/XF semantics in `docs/hw/graph/xf/` (`isa.rst`, `mode.rst`). Caveat:
  envytools' own "NV2A" is the nForce IGP, not the Xbox GPU.
- `References/nv2a-trace-master` — **MIT** apitrace-for-real-hardware tracer
  (notes: `docs/research-notes/nv2a-trace.md`). Hardware ground truth for PGRAPH if a box
  + `xboxpy` are available (per-method pre/post state, surface/texture PNGs, `debug.html`);
  without hardware its `XboxHelper.parse_command` pushbuffer decoder, NV2A/CACHE1 register
  constants and `Texture.py` format table are usable facts.
- `References/CopettiDocumentation` — CC BY 4.0 (attribution required if reused) high-level
  console architecture essay (notes: `docs/research-notes/copetti.md`). Orientation /
  onboarding / design cross-check only — never a register-level oracle.

**Never delete, move, or rewrite anything under `References/`.** It is the vendored
oracle set every wave depends on — it was emptied once (2026-10-12) and blocked a wave.
If a reference is missing, restore it from the upstream URLs in
`docs/research-notes/sources.md` before proceeding; never work around a missing reference.

Record extracted facts in `docs/research-notes/xgrid-architecture-decisions.md` (with source
file references) and update `ImplementationPlan.md` status when a device or phase changes.

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
6. **Wave-agent liveness (2026-10-13):** poll each running agent's task
   state periodically (~10 min cadence while a wave is out); a
   cancelled/frozen agent gets relaunched IMMEDIATELY with an inventory
   of its partial worktree state (untracked scripts, uncommitted diffs —
   classify keep-vs-revert in the relaunch prompt) rather than waiting or
   restarting cold. The first such relaunch (ktest-logsink, after a
   frozen session) closed two gates the frozen run had left open and
   turned scaffolding into the actual CMC fix.

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
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
   once; conflicts were purged 2026-10-09). Baseline: 1216 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — 007 onward probe + swapchain decision + JIT scoping.

1. **WAVE DONE (§§121–124).** APU DSP bridge drains G2 (§121, past 56.8B);
   GPU slice 3 goldens + fps table (§122 — GL 0–2.9% of wall; JIT is the
   lever); NV2A measured rows (§124, post-menu/tri identical); ktest smoke
   PASS ×2 (§123). **1248 tests.** Snapshot format v3: Nv2a v2→v3 drops old
   saves (Audio has v2 fallback) — **regenerate the 007 probe chain**.
2. **007 onward:** verify the merged DSP fix live (dashboard golden + 007 via
   a fresh snapshot to ~57.8B), save a new post-G2 probe origin, continue up
   the buffer-drain ladder / next content.
3. **Swapchain/windowing decision (operator)** for the present path;
   present-upload follow-up needs `PgraphSurfaceRecord` to carry the surface
   address (Nv2a-seam follow-up, per §122).
4. Track C JIT scoping (the lever); 007 texture census (DXT/mipmaps/shadows)
   when a later-stage probe can reach them; ktest smoke stays the standing
   gate; NV2A reserve rows (6–7 clamp is a safe choice, not reference).
6. `F36A2`/LBA 8 CLOSED as no-bug (§73): LBA 8 is the Xbox config
   sector (Partition0 network settings; `PCBX`/`55 AA` = its format, not
   MBR) under kernel RMW at step ~656M (EIP `0x8002430E`, IDENTIFY +
   READ/WRITE DMA, faithful serving, no fault); `F36A2` is the §49–51
   WC-fill page artifact, unrelated to disk I/O. Track C JIT for
   interactivity still the throughput bottleneck.
7. Parallel tracks when blocked: Track C JIT for interactivity (5B-step
   batch runs take ~6 min at ~14M insn/s Release; true interactive
   rates still need JIT).
8. Reference oracles: `xemu -trace` + QEMU monitor (`xp` physical reads; xemu
   deadlocks at the animation wait, so it is no longer an oracle past that
   point); xemu `hw/xbox/nv2a/pgraph/` (`methods.h.inc`, `draw.c`, `surface.c`,
   `vsh-prog.c`), `nv2a_regs.h`; for the APU gate xemu `hw/xbox/mcpx/`
   (`vp/`, `dsp/`, `apu.c`);
   `References/Cxbx-Reloaded-master/src/core/kernel/common/types.h` for
   KTHREAD/KWAIT offsets if needed.

Earlier AV-device gate details (research-notes §33, for reference):

