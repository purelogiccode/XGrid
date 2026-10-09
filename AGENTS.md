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

- Real MCPX ROM -> 1BL -> 2BL executes to its scheduler idle loop; all 2BL threads blocked.
- Timer IRQ0 works; RTC is read but PIE never enabled; IDE is never accessed (IDE is nForce 10DE:01BC, as in xemu).
- PCI stubs present with xemu IDs: OHCI x2 (0:2.0/0:3.0), nvnet 0:4.0, APU 0:5.0, ACI 0:6.0, NV2A on AGP bus 1
  (AGP bridge at 0:30.0). Config space only; BAR MMIO is not routed yet. Secondary-bus routing in `PciBus`.
- These did NOT unblock the 2BL. Port histogram (`Port accesses:` in CLI): PCI enumeration, SMBus, PM, PIC/PIT/RTC
  and Super I/O are touched; IDE ports and the PM timer are not.
- SMC matches xemu's register map; PCI stubs are all enumerated by the 2BL (incl. NV2A on bus 1,
  which the 2BL reaches by programming the AGP secondary bus itself). No MMIO polling observed
  (NV2A MMIO: 30 one-time probes). IDE PCI is probed but legacy ports never touched.
- SMC handshake completes (challenge R1C-R1F + W20/W21, version, AV pack); tray/INTSTATUS never read.
- Drivers are active (USB/nvnet/APU/NV2A cmd=0007 with BARs assigned) but MMIO never read back;
  IDE is legacy cmd=0005 with ports untouched; LPC routing still default (all device IRQs masked).
- **Reframe (XboxWiki Boot Process, page 51): the kernel at 0x80010000 has a valid MZ header —
  the 2BL finished (flash copy + RC4 + LZX + MZ/PE check + entry call). We are in the KERNEL's
  idle loop, not the 2BL's. All device activity completes before 50M instructions (50M and 100M
  histograms are identical except tick/PIC-EOI counts). Kernel cold-boot init (PCI setup, SMC,
  Super-I/O debugger, RTC) matches the wiki's list; IDE and video-progress never start.
- Timer ISR is 0x80030E4C (EOI helper 0x80014F10, tick+quantum accounting, IRET); DPC branch
  never fires. Exactly one thread exists (0x80035A90, Running); the wait is KiIdleLoop on an
  empty DPC queue with no waiter object.
- NV2A MMIO region at 0xFD000000 is online (`XGrid.Nv2a/Nv2aMmio`, xemu block map/defaults):
  FD0/FD1 unmapped buckets gone, kernel probes hit real regs (PMC/PBUS/PSTRAPS), counts frozen
  before 50M; 50M→100M otherwise identical to baseline (IRQ 36→108, same idle loop, IDE=0).
  Presence alone did not unblock the kernel.
- Next (kernel phase): USB OHCI MMIO + SOF interrupt, or IDE+FATX+XBE dashboard path.
