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
- Interrupts: PIT/PIC/PIT-EOI fine, NV2A IRQ path wired (PMC INTR pending but
  EN `0x01` keeps the line down, matching the driver). IDE IRQ14 works.
- **Next gate (XBE launch):** the kernel idles at `0x800426D8` after the
  dashboard load. Verify/relocate/entry-call of the XBE, then the dashboard
  menu draws + USB input. Widen M5–M8 (transforms/textures/zeta/blits) and
  Track C (CPU JIT) as needed; five remaining M9 gaps are tracked in
  `ImplementationPlan.md`.
- Build note: keep the repo out of Dropbox sync (parallel sync corrupted files
  once; conflicts were purged 2026-10-09). Baseline: 653 tests green,
  `dotnet build`/`format --verify-no-changes` clean.

## Next session — XBE launch gate

1. Reproduce the idle: `xgrid boot` (700M steps) and confirm `EIP=0x800426D8`,
   `lastCmd=0xC8`, `XBEH` at phys `0xBF000` (all verified 2026-10-09).
2. Find what the kernel waits on after the load (trace from `0x2EE98`; watch
   the thread/wait state, any object insert, and whether xemu's kernel
   proceeds to XBE verify/relocate/entry-call from the same point).
3. Then the dashboard runs: expect XBE section mapping, TLS, kernel-thunk
   imports (needs Phase 10 kernel-HLE surface for dashboard ordinals only if
   we choose HLE for its imports — LLE-first remains the rule).
4. Parallel tracks when blocked: NV2A M5–M8 (textures/transforms/zeta/flip)
   and Track C JIT for interactivity.
5. Reference oracles: `xemu -trace` + gdbstub (`-s`) for guest-time compares;
   `References/Cxbx-Reloaded-master/src/core/kernel/common/types.h` for
   KTHREAD/KWAIT offsets if thread-state forensics are needed.
