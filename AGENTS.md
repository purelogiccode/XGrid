# XGrid — Agent Notes

## Reference-first process (required)

Before implementing or debugging hardware behavior, **consult the reference projects first**
to confirm layout, IDs, registers, and semantics. Extract facts, never copy code.

- `References/xemu-0.8.136` — LLE reference (MCPX, PCI, IDE, SMBus, NV2A). Primary source.
  Key paths: `hw/xbox/nv2a/` (GPU pipeline/registers), `hw/xbox/mcpx/` (APU/ACI/nvnet),
  `hw/xbox/smbus_xbox_smc.c` (SMC protocol), `hw/xbox/xbox_pci.c` (PCI topology/IDs).
- `References/xqemu-master` — older QEMU-based LLE reference (useful for simpler code paths).
- `References/Cxbx-Reloaded-master` — HLE, but its kernel headers are the NT-layout reference:
  `src/core/kernel/common/types.h` (KTHREAD/KWAIT_BLOCK/KAPC offsets, e.g. KTHREAD+0x2C = State).
- `References/Xermu-main` — HLE, C# (kernel-facing behavior; not an LLE device model).
- `References/CSharp_XISOSharp` — approved MIT dependency for XDVDFS.
- `References/XboxWiki` — XboxDevWiki vendored locally (99 `.mhtml` pages, numeric file names;
  identify by `<title>`). Key pages: `51` Boot Process, `50` BIOS, `49` Kernel, `24` MCPX,
  `30` PCI, `41` USB, `16` NV2A, `33` SMC, `48` MCPX ROM, `90` Hidden Boot Code, `22` Memory.

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
- Host-side dashboard tooling (parallel track, merged): `XGrid.XboxFs`
  FATX reader + `XGrid.Xbe` parser + `xgrid fatx ls/cat` / `xgrid xbe info`;
  identified the boot dashboard as softmod `xboxdash.xbe` (770048 B, `EvoX`,
  entry `0x12A17`) in partition at `0x465400`.
- NV2A at `0xFD000000`: PFIFO pusher + PGRAPH inline-draw software rasterizer
  render one Gouraud triangle (307K px; M4 done). No textures/shaders/zeta/
  transforms yet; no present path (`XGrid.Graphics` empty; CLI dumps raw fb).
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
  buckets remain (`045=4 065=8 FEF=3970`, the nvnet stub).
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
  color → shared barycentric `FillTriangle`. **Validated on the real
  dashboard:** `xgrid boot --steps 1700000000` reports `draws executed=2670
  skipped=0 pixels=10137600` and the framebuffer at phys `0x1000` has rendered
  content (previously skipped). **M6 textures done (§40):** per-slot texture
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
  c58/c59 — no-ops on xemu too), so `blended=0 depth=0` is correct and live
  program-draw rasterization is still unvalidated. **M8 present done (§43):**
  flip read/write/modulo + `FLIP_INCREMENT_WRITE` (present event, captures
  the at-flip surface) + `FLIP_STALL` counting + vblank read-advance;
  `XGrid.Graphics/DisplayFrame` (BGRA frame store, 32-bit BMP export); CLI
  `--frame` saves the presented frame (`--fb` raw dump unchanged). Validated:
  `frames=33 stalls=33` (exact oracle match), final frame 640×480 from
  `0x32A4000` — opaque black (the flip chain presents the dashboard surface,
  not the `0x1000` region). Next: surface operations (render-to-texture/
  blits); inline `SET_VERTEX_DATA*` and `DRAW_ARRAYS` ranges remain.
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
  once; conflicts were purged 2026-10-09). Baseline: 725 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — surface operations + live program draws

1. The dashboard XBE entry executes (phys `0x5CA17`), decompresses its payload,
   writes the HDD via ATA WRITE DMA (`OverlayDiskImage`), passes the USB poll
   (OHCI implemented), and **renders**: `xgrid boot --steps 1700000000` (1.7B
   steps, `data/xemu-0.8.136-windows-x86_64/` set) completes with no fault and
   reports `draws executed=2670 skipped=0 pixels=10137600 textured=10137600
   clears=41 blended=0 depth=0 frames=33 stalls=33` at EIP `0x14C72`.
2. **M5** (transform-program interpreter, attribute fetch, element draws,
   §37–39, interpreter corrected §42) and **M6** (per-slot texture state,
   swizzled/linear A8R8G8B8 + R5G6B5 sampling, §40) and **M7** (fragment
   pipeline: alpha/stencil/depth/blend/mask, §41) are done and unit-tested.
   The dashboard's visible textures sample black because they are
   **GPU-generated (render-to-texture/blits not yet produced)** — the texture
   data at its offsets is mostly black (one colored texture at `0x321C000`).
3. **M8 present done (§43):** `frames=33 stalls=33` (exact oracle match);
   `xgrid boot --frame out.bmp` saves the flip-captured 640×480 frame
   (surface `0x32A4000`) — currently opaque black, which clarifies the
   display story (flip chain presents the dashboard surface, not the `0x1000`
   early-boot region). Next: the **surface operations** (render-to-texture,
   additional surface targets, 2D blits) that generate the dashboard's
   textures. Live program-draw rasterization is still unvalidated (pending
   non-degenerate program traffic). Remaining M5 gaps: inline
   `SET_VERTEX_DATA*` and `DRAW_ARRAYS` ranges.
4. Controller input: the OHCI root hub reports empty ports; an XID/gamepad
   device is future work. LLE-first remains the rule; Phase 10 kernel-HLE only
   if we choose HLE for dashboard ordinals.
5. Parallel tracks when blocked: Track C JIT for interactivity (dashboard boot
   + menu needs billions of instructions; the interpreter is 2.3 M insn/s).
6. Reference oracles: `xemu -trace` + QEMU monitor (`xp` physical reads; xemu
   deadlocks at the animation wait, so it is no longer an oracle past that
   point); xemu `hw/xbox/nv2a/pgraph/` (`methods.h.inc`, `draw.c`, `surface.c`,
   `vsh-prog.c`), `nv2a_regs.h`;
   `References/Cxbx-Reloaded-master/src/core/kernel/common/types.h` for
   KTHREAD/KWAIT offsets if needed.

Earlier AV-device gate details (research-notes §33, for reference):

