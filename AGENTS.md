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
- Interrupts: PIT/PIC/PIT-EOI fine, NV2A IRQ path live (PMC/PGRAPH/PCRTC
  pending → PIC IRQ3; the kernel toggles `PMC_INTR_EN_0` around its DPC). IDE
  IRQ14 works.
- **Next gate (XBE launch):** the kernel faults on a null video-device object:
  `#PF` at `0x8004E147` (`mov ecx,[eax]`, EAX=0) from `0x80057A33`; the object
  came from `0x80053302` (video-device open/create) and stayed NULL →
  BugCheck `0x1E` (`0xC0000005`). Then XBE verify/relocate/entry-call, menu
  draws + USB input. Widen M5–M8 (transforms/textures/zeta/blits) and Track C
  (CPU JIT) as needed; remaining M9 gaps tracked in `ImplementationPlan.md`.
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
  once; conflicts were purged 2026-10-09). Baseline: 655 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — XBE launch gate

1. Run `xgrid boot` (700M steps, `data/xemu-0.8.136-windows-x86_64/` set); the
   old park is gone; expect the null-object fault (BugCheck 0x1E) at ~157.5M
   instructions (verified 2026-10-10).
2. Trace `0x80053302` (first hit ~25.3M; the faulting call is later) to find
   why the video-device object is NULL (candidates: a device object registered
   by an earlier driver init, encoder/AV-pack path, missing NV2A interface).
   `--ramdump`/`--ramin` snapshots + `objdump -b binary -m i386` (kernel image
   at phys `0x10000`, VA = phys + `0x80000000`) were the working tools.
3. Then the dashboard runs: expect XBE section mapping, TLS, kernel-thunk
   imports (needs Phase 10 kernel-HLE surface for dashboard ordinals only if
   we choose HLE for its imports — LLE-first remains the rule).
4. Parallel tracks when blocked: NV2A M5–M8 (textures/transforms/zeta/flip)
   and Track C JIT for interactivity.
5. Reference oracles: `xemu -trace` + QEMU monitor (`-monitor tcp:...`,
   `xp` physical reads; xemu deadlocks at the animation wait, so it is no
   longer an oracle past that point); `References/Cxbx-Reloaded-master/src/
   core/kernel/common/types.h` for KTHREAD/KWAIT offsets if needed.
