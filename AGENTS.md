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
- **Next gate:** the XBE entry `0x22A17` is not yet reached; execution is in a
  bit-stream decompressor in the XBE's early `.text` (RVA ~0x24A0-0x27E0: 16 KB
  ring buffer `[ctx+0x34]` masked `0x3FFF`, bit reader `shl/or`). Decide if it
  is a legitimate slow decompression or a second emulation bug; find which
  section RVA 0x24A3 is and why it runs before the entry.
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
  once; conflicts were purged 2026-10-09). Baseline: 678 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — XBE launch gate

1. The animation/AV-teardown crash is fixed (CPU `fcomp`/`fcom` memory-operand
   bug, §34). `xgrid boot --steps 700000000` (700M steps,
   `data/xemu-0.8.136-windows-x86_64/` set) now completes without faulting and
   ends executing the dashboard XBE image.
2. Trace the XBE `.text` decompressor at phys `0x5C4A0` (VA `0x124A0`):
   disassemble from the RAM dump (`objdump -D -b binary -m i386 -M intel`),
   then `--break-log` its inner loops and `--watch` the 16 KB ring buffer /
   source pointer `[ctx+0x2C]` to see whether progress is made. The XBE header
   is at phys `0x5A000` (base `0x10000`, entry RVA `0x12A17`); the entry
   `0x22A17` never ran in a 560M-step run.
3. Then the dashboard entry: XBE section mapping, TLS, kernel-thunk imports
   (Phase 10 kernel-HLE surface only if we choose HLE for dashboard ordinals;
   LLE-first remains the rule). Menu rendering needs NV2A M5-M8 + present.
4. Parallel tracks when blocked: NV2A M5-M8 (textures/transforms/zeta/flip)
   and Track C JIT for interactivity.
5. Reference oracles: `xemu -trace` + QEMU monitor (`xp` physical reads; xemu
   deadlocks at the animation wait, so it is no longer an oracle past that
   point); `References/Cxbx-Reloaded-master/src/core/kernel/common/types.h`
   for KTHREAD/KWAIT offsets if needed.

Earlier AV-device gate details (research-notes §33, for reference):

