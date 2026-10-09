# xqemu — Architecture Notes (Older QEMU Fork)

Reference: `References/xqemu-master` (QEMU 4.0.0 base). License: GPL-2.0 (mixed per-file) — facts
only. xqemu is the predecessor of xemu; it has less coverage but a few unique artifacts.

---

## 1. Provenance / scope

- QEMU 4.0.0 base (`VERSION`), `--target-list=i386-softmmu`, `-DXBOX=1`, `--without-default-devices`.
- Machines: `xbox` and `chihiro` (Sega arcade).
- Xbox devices: NV2A, MCPX APU + ACI (AC'97), MCPX LPC/PCI/AGP/SMBus, SMC, CX25871 + ADM1032,
  LPC47M157 Super I/O, 2× OHCI USB, nvnet NIC, PIIX3 IDE, i8259/i8254/RTC/PIT, XID gamepad,
  Chihiro mediaboard.
- NV2A directory is ~576 KB vs xemu's ~1.25 MB; OpenGL-only renderer (no Vulkan, no S3TC
  decompression, no RenderDoc).

## 2. Machine / memory map

- RAM at 0, PCI hole alias `ram_size`..0xFFFFFFFF.
- BIOS mirrored 0xFF000000–0xFFFFFFFF (multiple of 64 KiB); MCPX bootrom exactly 512 bytes
  overlaid on the last 512 bytes of BIOS.
- RTC MC146818 base 2000; registers 0x10–0x6F and 0x80–0xFF pre-filled with alternating
  0x55/0xAA ("xbox bios wants this bit pattern set to mark the data as valid").
- Default EEPROM = "bunnie's eeprom" 256-byte array at SMBus 0x54. SMC 0x10, CX25871 0x45,
  ADM1032 0x4C.
- PCI/IRQ layout mirrors xemu (same devfn/IDs), with `0x64`/`0x68`/`0x6C` routing registers.

## 3. Key finding: no XDVDFS/FATX/XISO code

- Full-tree search: **zero** real hits for xdvdfs/xiso/fatx. Discs attach as raw ISO via stock
  QEMU PIIX3 IDE and the guest kernel parses XDVDFS.
- NV2A has `ENGINE_DVD = 2` / `NV_RAMHT_ENGINE_DVD 0x00020000` but the FIFO puller asserts
  `engine == ENGINE_GRAPHICS` — the DVD sub-engine is unimplemented.
- xemu is the same (no host XDVDFS parser); the only XDVDFS implementation in the workspace is
  Cxbx's (GPL-2), and XISOSharp (MIT) is the reusable C# one.

## 4. Unique artifacts vs xemu

1. `hw/xbox/xid-sdl.c` — SDL2 joystick + haptic XID device; documents the Xbox→SDL rumble range
   conversion (Xbox 0x0000–0xFFFF → SDL 0x0000–0x7FFF, shift right by 1).
2. `hw/xbox/adpcm.h` + `adpcm_block.h` — IMA ADPCM step/index tables (xemu has them under
   `mcpx/apu/vp/adpcm.h`).
3. `hw/xbox/mcpx_apu.c/h` — monolithic, heavily commented APU model with the complete
   `NV_PAPU_*`/`NV_PAVS_*`/`NV1BA0_PIO_*` register map in one file (more readable than xemu's
   refactored tree).
4. Original single-pass GLSL translators (`nv2a_vsh.c/h`, `nv2a_psh.c/h`, `nv2a_shaders.c/h`) —
   GPL-2, reference only.
5. `nv2a/xxhash.c/h` and `lru.c/h` bundled in-tree; old `gloffscreen_{wgl,glx,cgl,common}.c`.
6. Accelerator quirks: `-DXBOX=1` TSC hack (see §5), HAX/HVF dirty-tracking disabled under XBOX.
7. Boot model details: bunnie's EEPROM bytes, RTC 0x55/0xAA pattern, 512-byte bootrom overlay.
8. Hardware documentation pointers in comments: AMD/nForce chipset PDFs (docs 24494, 24416,
   24467, 24462), PIC16LC/xbox-linux PIC wiki, xpad.c, MSDN D3D VS docs,
   `NV_register_combiners` spec, `NV_fog_distance` spec, MS vs nvidia vs nouveau register naming.
9. Chihiro chip revision (0xF0) / ASIC (0x0100) / DIMM size (0xF4) registers.

## 5. CPU quirks (`-DXBOX=1`)

- Default CPU `pentium3`; TCG (KVM optional Linux, HAXM Windows).
- **TSC hack**: `cpu_get_tsc()` returns `muldiv64(qemu_clock_get_ns(VIRTUAL), 733333333,
  NANOSECONDS_PER_SECOND)` — fixed 733.333333 MHz TSC regardless of icount/host.
- HAX dirty bitmap workaround: skip `memory_region_set_dirty()` for RAM under XBOX (bad
  consequences for NV2A); HVF dirty tracking disabled under XBOX.

## 6. MCPX/APU/SMC facts (cross-checked with xemu)

- APU: PCI 05:00, 10DE:01B0, rev 210, BAR0 0x80000; VP 0x20000, GP 0x30000, EP 0x50000 (64 KiB
  each). Constants: 32 samples/frame, 32 mixbins, 256 voices, voice struct 0x80, mixbuf 0x1400,
  GP 4 output/2 input FIFOs, EP 4 output/2 input FIFOs. Register map matches xemu (ISTS 0x1000,
  IEN 0x1004, FECTL 0x1100, FECV 0x1110, FEAV 0x1118, FEDECMETH 0x1300, FEMEMADDR 0x1324,
  FEMEMDATA 0x1334, SECTL 0x2000, TVL/CVL/NVL2D 0x2054/58/5C, 3D 0x2060/64/68, MP 0x206C/70/74,
  GPSADDR 0x2040, EPSADDR 0x2048, GPOFBASE0 0x3024, EPOFBASE0 0x4024, GPRST 0xFFFC with
  GPRST=1/GPDSPRST=2/GPNMI=4/GPABORT=8). Voice front-end method IDs `NV1BA0_PIO_*`
  (VOICE_ON 0x124, VOICE_OFF 0x128, SET_VOICE_CFG_* 0x300–0x318, TAR_PITCH 0x37C, BUF_BASE
  0x3A0, SGE 0x804/0x1800, SE2FE_IDLE_VOICE 0x8000). Envelope state machine
  (delay/attack/hold/decay/sustain/release) + SGE scatter-gather page tables.
- DSP56300 interpreter adapted from Hatari/ARAnyM (GPL-2+); X 4096, Y 2048, P 4096 words;
  mix buffer 1024 at 0x1400; peripheral interrupts at 0xFFFFC5, DMA 0xFFFFD4–D7; interrupt bits
  ABORT_FRAME 1, START_FRAME 2, DMA_EOL 0x80.
- ACI: PCI 06:00, 10DE:01B1, rev 210; BAR2 0x1000 with NAM 0x0 (0x100), NABM 0x100 (0x80).
- SMBus: PCI 01:01, 10DE:01B4, rev 161; BAR1 I/O 32 bytes; AMD756 window at base offset 0xE0,
  16 bytes (same register map as xemu).
- SMC: same register map as xemu; version "P01"; challenge reads 0x1C–0x1F = 0x52/0x72/0xEA/0x46.
- LPC47M157: config 0x2E (0x4E alt), enter 0x55, exit 0xAA, devices FDD 0, parallel 3, serial1 4,
  serial2 5, keyboard 7, game 9, PME 0xA, MPU 0xB.
- nvnet: 10DE:01C3, rev 210; I/O 8 + MMIO 0x400; Linux nvnet register names (IrqStatus 0x000,
  IrqMask 0x004, MacAddrA 0xA8, TxRingPhysAddr 0x100, LinkSpeed 0x110).
- ACPI: PM I/O BAR0 256 bytes; GPIO at 0xC0 length 26; field pin bit 5.
- XID: same protocol as xemu/Cxbx; rumble range conversion note in `xid-sdl.c`.

## 7. NV2A block table (identical offsets to xemu/cxbx)

PMC 0x000000, PBUS 0x001000, PFIFO 0x002000, PRMA 0x007000, PVIDEO 0x008000, PTIMER 0x009000,
PCOUNTER 0x00A000, PVPE 0x00B000, PTV 0x00D000, PRMFB 0x0A0000, PRMVIO 0x0C0000, PFB 0x100000,
PSTRAPS 0x101000, PGRAPH 0x400000, PCRTC 0x600000, PRMCIO 0x601000, PRAMDAC 0x680000,
PRMDIO 0x681000; PRAMIN as separate 1 MiB region at MMIO 0x700000; USER 0x800000.
- BAR1 prefetchable alias of system RAM ("xbox is UMA - vram *is* ram"), `vram_size_mb = 64`.
- PGRAPH register-combiner → GLSL, VSH versions (`VSH_VERSION_VS 0xF078`, `XVS 0x2078`,
  `XVSS 0x7378`, `XVSW 0x7778`, `VSH_TOKEN_SIZE 4`), 86 FIXME markers.
- Registers header notes: `NV2A_CRYSTAL_FREQ 16666666`, `NV2A_NUM_CHANNELS 32`,
  `NV2A_NUM_SUBCHANNELS 8`, `NV2A_CACHE1_SIZE 128`, `NV2A_MAX_BATCH_LENGTH 0x1FFFF`,
  MS vs nvidia/nouveau RDI naming differences (RDI_INDEX_VTX_CONSTANTS0 0x17, 1 0xCC).
- Clocks: core coeff 0x00011C01, video 0x0003C20D, core freq 189000000.

## 8. Value for XGrid

- Compact hardware documentation: NV2A register header, single-file APU register map, SMC
  register/challenge table, default EEPROM/RTC/bootrom behavior, AMD/nForce and xpad references,
  TSC/accelerator quirks.
- Nothing to port for storage/XBE/kernel (xqemu has no XBE/kernel code).
- Treat shader translators (GPL) and DSP (GPL) as behavioral reference only.
