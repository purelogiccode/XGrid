# xemu 0.8.136 — Hardware Facts and Architecture Notes

Reference: `References/xemu-0.8.136` (QEMU fork, `--target-list=i386-softmmu`, `-DXBOX=1`).
License: GPL-2.0/LGPL-2+/MIT per file — facts extracted here, no code copied.

Legend: **[HW]** Xbox hardware fact, **[XEMU]** xemu-specific workaround/implementation, **[QEMU]**
framework plumbing not needed by XGrid.

---

## 1. Xbox machine

- Machine type `xbox`, CPU default `pentium3`, `max_cpus = 1`, no floppy/CDROM, default NIC `nvnet`.
- Machine properties: `bootrom`, `avpack` (default `hdtv`), `short-animation`, `smc-version`
  (default `P01`), `video-encoder` (default `conexant`).
- RAM mapped at physical 0; default 64 MB (`-m` selects 64/128 MB). GPU VRAM is system RAM (UMA).

### Device list (devfn → model → IDs)

| Bus | Dev:Fn | Model | Xbox device | Vendor:Device rev |
|---|---|---|---|---|
| PCI0 | 0:0.0 | `xbox-pci` host bridge (multifunction) | NV2A host bridge | 10DE:02A5 A1 |
| PCI0 | 1:0.0 | `xbox-lpc` ISA bridge | MCPX LPC | 10DE:01B2 B2 |
| PCI0 | 1:1.0 | `xbox-smbus` | MCPX SMBus (AMD756) | 10DE:01B4 B1 |
| PCI0 | 2:0.0 | `pci-ohci` (4 ports) | USB0 OHCI | 10DE:01C2 B1 |
| PCI0 | 3:0.0 | `pci-ohci` (4 ports) | USB1 OHCI | 10DE:01C2 B1 |
| PCI0 | 4:0.0 | `nvnet` | MCPX Ethernet | 10DE:01C3 B1 |
| PCI0 | 5:0.0 | `mcpx-apu` | APU | 10DE:01B0 B1 |
| PCI0 | 6:0.0 | `mcpx-aci` | AC'97 codec interface | 10DE:01B1 B1 |
| PCI0 | 9:0.0 | `piix3-ide` (stand-in) | NVIDIA IDE | 10DE:01BC B1 |
| AGP | 0:0.0 | `nv2a` | GPU | 10DE:02A0 A1 |
| PCI0 | 30:0.0 | `xbox-agp` bridge | AGP bridge | 10DE:01B7 A1 |

### Physical memory map

- **RAM**: `0x00000000` .. `ram_size` (64 MB default).
- **PCI hole alias**: system memory `[ram_size, 0x1_0000_0000)` aliases PCI address space
  (`xbox_pci.c`). With 64 MB RAM: `0x04000000`–`0xFFFFFFFF`.
- **BIOS/flash**: base `0xFF000000`, size multiple of 64 KiB (default 256 KiB fallback, typical
  256 KiB or 1 MiB). Mirrored upward in `bios_size` steps while `map_loc >= 0xFF000000`
  (a 256 KiB BIOS is aliased 256× to fill top 64 MB). Read-only.
- **MCPX boot ROM**: 512-byte file overlaid over the *last 512 bytes* of the BIOS image at
  `0x1_0000_0000 - bios_size` (i.e. `0xFFFFFE00` for a 256 KiB BIOS). **[HW]** enabled at reset,
  disabled when guest writes LPC PCI config offset `0x80` with bit 1 set. **[XEMU]** implemented
  as writable RAM to satisfy a retail-1.1+ kernel cache-dependent write at `0xFFFFD52C`.

### MMIO windows (guest-assigned via PCI BARs)

| Device | Window |
|---|---|
| NV2A BAR0 | 16 MiB register block |
| NV2A BAR1 | VRAM alias (prefetchable; system RAM) |
| APU BAR0 | 512 KiB: main `0x00000`, VP `0x20000`, GP `0x30000`, EP `0x50000` |
| ACI BAR2 | 4 KiB: NAM `0x000` (0x100), NABM `0x100` (0x80) |
| SMBus BAR1 | 32 bytes I/O |
| LPC BAR0 | 256 bytes I/O (PM base); GPE at +0x20, GPIO at +0xC0 |
| nvnet | MMIO 0x400 + I/O 8 bytes |
| IDE/OHCI | QEMU-standard PIIX3/OHCI BAR layouts |

### Reset / boot

- CPU reset: `EIP = 0xFFF0`, `CS` selector `0xF000`, base `0xFFFF0000` → first fetch at
  physical `0xFFFFFFF0` (inside MCPX ROM overlay).
- The real 512-byte MCPX ROM executes first (xemu does not emulate MCPX logic in C); it performs
  memory init and loads the 1BL. Writing LPC config `0x80` bit 1 removes the overlay.
- Launch wiring: HDD `-drive index=0,media=disk,locked=on` → IDE primary master; DVD
  `-drive index=1,media=cdrom` → primary slave; EEPROM `-device smbus-storage`; BIOS `-bios`;
  USB hub `usb-hub,port=1,ports=4` on OHCI0 root port 1.

---

## 2. CPU (QEMU TCG + XBOX patches)

- Pentium III model: family 6, **model 8, stepping 10** (Coppermine class).
- Features: `PENTIUM2_FEATURES | SSE` where PENTIUM2 adds PAE, SEP, MTRR, PGE, MCA, CMOV, PAT,
  PSE36, FXSR. `xlevel = 0` (no extended CPUID leaves).
- **CPUID quirks [HW]**:
  - Leaf 1: `EBX = 0` (brand index / CLFLUSH size / APIC id fields zero).
  - Leaf 2: hard-coded `EAX=0x03020101, EBX=0, ECX=0, EDX=0x0C040841` (Xbox 1.0 cache info).
  - Leaves 4+ compiled out; unsupported leaves clamp to `cpuid_level = 2`.
  - No hypervisor bit, no extended cache leaves, no 0x80000002-4 brand string.
- **MSR quirk [HW]**: `MSR_IA32_EBL_CR_POWERON` (0x2A) reads `0xC5040000` (FSB/clock strapping).
- No Xbox-specific MTRR/cache-disable emulation; only the MCPX overlay write workaround above.
- **[XEMU]** optional "hard FPU" host-float fast path — not hardware behavior.

---

## 3. PCI bus

- Config mechanism: standard `CONFIG_ADDR=0xCF8` / `CONFIG_DATA=0xCFC` I/O ports.
- IRQ infrastructure: `XBOX_NUM_INT_IRQS = 8`, `XBOX_NUM_PIRQS = 4`, `XBOX_NUM_PIC_IRQS = 16`.
- LPC config registers: `0x64` ACPI/SCI routing (2×8-bit), `0x68` PIRQ routing (4 bytes),
  `0x6C` INT routing (8×4-bit nibbles).
- Slot → routing index: slot 2 (USB0)=0, slot 3 (USB1)=1, slot 4 (NIC)=2, slot 5 (APU)=3,
  slot 6 (ACI)=4, slot 9 (IDE)=6, slot 30 (AGP)=PIRQ C (index 8+2); host/LPC = none.
- Canonical values: `INT_IRQ_ROUT = 0x0E065491` → nibbles LSB-first `1,9,4,5,6,0,E,0`
  (USB0=IRQ1, USB1=IRQ9, NIC=IRQ4, APU=IRQ5, ACI=IRQ6, IDE=IRQ14); `PIRQ_ROUT = 0x00031000`
  → AGP=IRQ3.
- PIC only (standard 8259 pair); **no IOAPIC instantiated** on Xbox.

---

## 4. MCPX subsystem

### 4.1 SMBus controller (AMD756) — `amd_smbus.c` (MIT)
- I/O window 16 bytes, registers at `addr & 0x3F`; hardware base offset `0xE0`.
- Registers: GLOBAL_STATUS 0x0, GLOBAL_ENABLE 0x2, HOST_ADDRESS 0x4, HOST_DATA 0x6/0x7,
  HOST_COMMAND 0x8, HOST_BLOCK_DATA 0x9, HAS_DATA 0xA, HAS_DEVICE_ADDRESS 0xC,
  HAS_HOST_ADDRESS 0xE, SNOOP_ADDRESS 0xF.
- Status bits ABRT/COL/PRERR/HST/HCYC/TO (0-5), SMB_STS bit 11. Enable bits CYC_TYPE(0-2),
  HOST_STC bit3, HCYC_EN bit4, ABORT bit5. Protocols QUICK/BYTE/BYTE_DATA/WORD_DATA/
  PROCESS_CALL/BLOCK_DATA = 0..5. Transaction triggers on GLOBAL_ENABLE write with HOST_STC.

### 4.2 SMC — `smbus_xbox_smc.c` (MIT)
- PIC16LC at SMBus address **0x10**. Registers:
  - `0x01` version string (3 bytes, default "P01"; index write resets pointer)
  - `0x02` power: bit0 RESET, bit6 CYCLE, bit7 SHUTDOWN
  - `0x03` tray: 0x10 open, 0x40 no media, 0x60 media
  - `0x04` AV pack: 0=SCART,1=HDTV,2=VGA,3=RFU,4=SVIDEO,6=COMPOSITE,7=NONE
  - `0x05` fan mode, `0x06` fan speed, `0x07` LED mode, `0x08` LED sequence,
    `0x09` CPU temp, `0x0A` board temp
  - `0x0C` tray eject, `0x0D` interrupt ack, `0x0E` write error, `0x0F` read error
  - `0x11` interrupt status (power/tray closed/opening/avpack plug/unplug/eject button/closing),
    cleared on read
  - `0x19` reset-on-eject, `0x1A` interrupt enable, `0x1B` scratch (bit2 short animation)
- Challenge/response: writes to 0x20/0x21 ignored; reads 0x1C→0x52, 0x1D→0x72, 0x1E→0xEA,
  0x1F→0x46 (must be non-zero). **[HW]** required by the kernel SMC handshake.
- Buttons raise `ACPI_EXTSMI_STATUS`; tray state follows the ATAPI tray/media state.

### 4.3 EEPROM — `smbus_storage.c`, `eeprom_generation.c` (LGPL)
- I2C storage device: address **0x54**, size **256**, persist=true. Write sets offset then
  sequential writes; reads sequential, wrap at size.
- Image layout: `hash[20]`, `confounder[8]`, `hdd_key[16]`, `region`, `checksum`, `serial[12]`,
  `mac[6]`, `pad`, `online_key[16]`, `video_standard`, `pad2`, `user_checksum`, `user_section[156]`.
- Generation: random confounder/HDD/online keys/MAC (`00:50:F2` prefix)/serial; region=1;
  video_standard=0x00400100 (NTSC-M); checksum = `~(high+low)` over 32-bit LE words; security
  section encrypted with SHA1-then-RC4 using per-version SHA1 IVs (EEPROM versions D/R1/R2/R3).
- **[HW]** exact layout/crypto required for kernel compatibility.

### 4.4 Other SMBus devices
- Conexant CX25871 video encoder @ **0x45** (default), Focus FS454 @ 0x6A, Xcalibur @ 0x70,
  ADM1032 temp sensor @ 0x4C (returns 50 °C for cmd 0/1). Simple register stubs.

### 4.5 ACPI / power management — `acpi_xbox.c` (LGPL)
- LPC BAR0 (I/O, 256 bytes): GPE block at +0x20 (4 bytes), GPIO at +0xC0 (26 bytes).
- GPIO offset 0 returns alternating "field pin" bit 5; GPIO offset 0x16 write value 5 enables
  widescreen in xemu **[XEMU]**.
- PM1 event/control from QEMU ACPI core; SCI = PM1 status & enable (RTC/power button/global
  lock/timer). `ACPI_EXTSMI` events delivered by the SMC.

### 4.6 Super I/O — `lpc47m157.c`
- SMSC LPC47M157: config ports 0x2E (0x4E if `sysopt`), index/data 0/1, enter 0x55, exit 0xAA.
- Logical devices: FDD=0, Parallel=3, Serial1=4, Serial2=5, Keyboard=7, Game=9, PME=0xA,
  MPU=0xB. Config regs: device number 0x07, port low/high 0x26/0x27, activate 0x30,
  base 0x60/0x61, IRQ 0x70. Not instantiated by the xbox machine in 0.8.136.

### 4.7 Flash
- Treated as flat read-only ROM + writable MCPX overlay; **no flash command set** in xemu.
- **[HW]** real Xbox flash is AMD/SST command-set; Xermu documents the CFI model (see xermu.md).

### 4.8 RTC
- Standard MC146818 at I/O 0x70/0x71, IRQ 8, base year 2000.

### 4.9 ACI (AC'97) — `mcpx/aci.c` (LGPL)
- NVIDIA 01B1; BAR2 4 KiB: NAM 0x100 bytes @0, NABM 0x80 bytes @0x100. QEMU ac97 core used.

---

## 5. NV2A

### 5.1 Device and memory
- PCI VGA device; BAR0 = 16 MiB MMIO; BAR1 = alias of system RAM (UMA), prefetchable.
- **RAMIN**: 1 MiB separate host RAM mapped at BAR0 offset `0x700000` (xemu keeps it separate
  from VRAM; real HW aliases instance memory inside VRAM).
- Reset clocks: core PLL coeff `0x00011C01` (~189 MHz), video PLL `0x0003C20D` (~25.182 MHz),
  memory clock coeff 0; crystal `NV2A_CRYSTAL_FREQ = 16,666,666`.
- PFIFO cache1 status init LOW_MARK; DAC palette initialized to identity ramp.

### 5.2 Register block map (BAR0 offsets)

| Block | Offset | Size | File |
|---|---|---|---|
| PMC | 0x000000 | 0x1000 | `pmc.c` |
| PBUS | 0x001000 | 0x1000 | `pbus.c` |
| PFIFO | 0x002000 | 0x2000 | `pfifo.c` |
| PRMA | 0x007000 | 0x1000 | stubs |
| PVIDEO | 0x008000 | 0x1000 | `pvideo.c` |
| PTIMER | 0x009000 | 0x1000 | `ptimer.c` |
| PCOUNTER | 0x00A000 | 0x1000 | stubs |
| PVPE | 0x00B000 | 0x1000 | stubs |
| PTV | 0x00D000 | 0x1000 | stubs |
| PRMFB | 0x0A0000 | 0x20000 | stubs |
| PRMVIO | 0x0C0000 | 0x1000 | `prmvio.c` |
| PFB | 0x100000 | 0x1000 | `pfb.c` |
| PSTRAPS | 0x101000 | 0x1000 | stubs |
| PGRAPH | 0x400000 | 0x2000 | `pgraph/pgraph.c` |
| PCRTC | 0x600000 | 0x1000 | `pcrtc.c` |
| PRMCIO | 0x601000 | 0x1000 | `prmcio.c` |
| PRAMDAC | 0x680000 | 0x1000 | `pramdac.c` |
| PRMDIO | 0x681000 | 0x1000 | `prmdio.c` |
| PRAMIN | 0x700000 | 0x100000 | separate RAM |
| USER | 0x800000 | 0x800000 | `user.c` |

### 5.3 PMC / PBUS
- `NV_PMC_BOOT_0` (0x000000) = `0x02A000A3` (NV2A A03, Rev 0).
- `NV_PMC_INTR_0` 0x100 / `NV_PMC_INTR_EN_0` 0x104; per-unit bits (PFIFO/PCRTC/PGRAPH).
- PBUS 0x800/0x804/0x808 mirror PCI vendor/command/class-revision.

### 5.4 PFIFO
- Engines: SOFTWARE=0, GRAPHICS=1, DVD=2. 32 channels.
- Channel regs: `NV_PFIFO_MODE` 0x504; CACHE1_PUSH0 0x1200 (ACCESS bit0); CACHE1_PUSH1 0x1204
  (CHID 0-4, MODE bit8 PIO/DMA); CACHE1_DMA_PUSH 0x1220; CACHE1_DMA_STATE 0x1228;
  CACHE1_DMA_INSTANCE 0x122C; PUT 0x1240 / GET 0x1244 / REF 0x1248 / SUBROUTINE 0x124C;
  PULL0 0x1250; PULL1 0x1254; DMA_DCOUNT 0x12A0; GET_JMP_SHADOW 0x12A4; RSVD_SHADOW 0x12A8;
  DATA_SHADOW 0x12AC; CACHE1_METHOD 0x1800; CACHE1_DATA 0x1804; CACHE1_STATUS 0x1214
  (LOW_MARK bit4, HIGH_MARK bit8).
- DMA object (3 dwords): flags (class 0-11, target 16-17: NVM=0/NVM_TILED=1/PCI=2/AGP=3,
  adjust 20-31), limit, frame (address bits 12-31). `nv_dma_map` masks address with `0x07FFFFFF`.
- RAMHT: base = `NV_PFIFO_RAMHT` (0x210) bits 4-8 << 12; size 1 << (size_field+12) (4/8/16/32 KiB);
  hash = XOR-fold of handle plus `channel_id << (bits-4)`. Entry (8 bytes): handle, context
  (instance 0-15 <<4, engine 16-17, channel 24-28, valid bit31).
- Method dispatch: method word `0x0000` binds object handle → subchannel (engine must be GRAPHICS);
  methods `>= 0x100` forwarded to PGRAPH; methods 0x180..0x1FF resolve parameter through RAMHT.
- Pusher command encodings:
  - old jump `(w & 0xE0000003) == 0x20000000` → addr `w & 0x1FFFFFFF`
  - jump `(w & 3) == 1` → addr `w & 0xFFFFFFFC`
  - call `(w & 3) == 2` (error if nested); return `w == 0x00020000`
  - increasing methods `(w & 0xE0030003) == 0`: method=(w&0x1FFF)>>2, subchannel=(w>>13)&7,
    count=(w>>18)&0x7FF
  - non-increasing `(w & 0xE0030003) == 0x40000000`, same fields
  - error codes in CACHE1_DMA_STATE_ERROR (CALL/NON_CACHE/RETURN/RESERVED_CMD/PROTECTION)
- `NV_USER` per-channel windows at `channel_id << 16`: DMA_PUT 0x40, DMA_GET 0x44, REF 0x48.

### 5.5 PGRAPH
- Graphics classes: NV_BETA 0x12, NV_CONTEXT_PATTERN 0x44, NV_CONTEXT_SURFACES_2D 0x62,
  NV_IMAGE_BLIT 0x9F, NV_KELVIN_PRIMITIVE 0x97.
- Kelvin method table 0x800 entries (see `methods.h.inc`); key methods: SET_OBJECT, NO_OPERATION,
  WAIT_FOR_IDLE, flip registers, context DMAs, surface format/pitch/offset, combiners, textures,
  matrices, vertex arrays, SET_BEGIN_END, DRAW_ARRAYS, INLINE_ARRAY, ARRAY_ELEMENT16/32,
  CLEAR_SURFACE, GET_REPORT, FLIP_STALL.
- SET_OBJECT loads 4 context dwords from RAMIN into `NV_PGRAPH_CTX_CACHE1..4` per subchannel.
- Context switch: CHID mismatch → set TRAPPED_ADDR.CHID, raise `NV_PGRAPH_INTR_CONTEXT_SWITCH`,
  set `waiting_for_context_switch` (software context switching; HW_CONTEXT_SWITCH off).
- NO_OPERATION with non-zero parameter raises `NV_PGRAPH_INTR_ERROR` + TRAPPED_ADDR fields.
- Flips: `NV_PGRAPH_SURFACE` read/write/modulo; FLIP_INCREMENT_WRITE bumps frame time;
  FLIP_STALL waits read==write.
- Surfaces: `SurfaceShape {z_format, color_format, zeta_format, log_width, log_height,
  clip_x/y/w/h, anti_aliasing}`; AA center1 / center-corner2 / square4 double dimensions.
- PGRAPH state: ~0x2000 dword register file + dirty bitmap; object state structs for
  ContextSurfaces2D, ImageBlit, Kelvin, Beta; inline/draw arrays max `0x7FFFF` words;
  16 vertex attributes, 192 VS constants, 136-instruction transform program, 8 lights,
  4 textures.

### 5.6 Renderer abstraction and shader IR
- `PGRAPHRenderer` ops: `early_context_init, init, finalize, clear_report_value, clear_surface,
  draw_begin/end, flip_stall, flush_draw, get_report, image_blit, process_pending,
  process_pending_reports, surface_flush, surface_update, get/set_surface_scale_factor,
  get_framebuffer_surface, get_gpu_properties`. Types: OPENGL, VULKAN, NULL.
- Intermediate representation: `ShaderState { VshState vsh; GeomState geom; PshState psh; }`.
  - `VshState`: fixed-function fields or programmable `program_data[136][4]`; fog/specular/point
    params, attribute bitmasks, surface scale.
  - `PshState`: combiner control/stage programs/final inputs, per-stage rgb/alpha inputs+outputs
    (8 stages), texture flags (point sprite, rect, snorm, compare, alphakill, colorkey,
    convolution, x8y24, dimensionality, cubemap), border, shadow map, alpha test, depth format.
  - `GeomState`: primitive/back-face/winding.
- Shader translation: `pgraph_glsl_gen_vsh()` (fixed-function or XVS token translation),
  `pgraph_glsl_gen_psh()` (texture-stage/register-combiner → GLSL), `geom.c` for geometry
  shaders (Vulkan). Uniform sets documented in `vsh.h`/`psh.h`.

### 5.7 Clocks/display
- PCRTC `NV_PCRTC_START` (0x800) & `0x07FFFFFF` = framebuffer base; INTR_0/INTR_EN_0;
  RASTER returns incrementing counter.
- PRAMDAC PLLs report locked; core clock = crystal * N / (2^P * M).
- PTIMER TIME_0/TIME_1 scaled by core clock; alarm register.
- PFB CSTATUS (0x20C) reads VRAM size; WBC reads 0.
- NV2A embeds a VGA core for display; VBLANK raises `NV_PCRTC_INTR_0_VBLANK`.

### 5.8 Texture swizzle (algorithm fact)
```
generate_swizzle_masks(w,h,d, &mx,&my,&mz):
  bit=1; mask_bit=1
  do { done=true
       if (bit<w) { mx|=mask_bit; mask_bit<<=1; done=false }
       if (bit<h) { my|=mask_bit; mask_bit<<=1; done=false }
       if (bit<d) { mz|=mask_bit; mask_bit<<=1; done=false }
       bit<<=1 } while(!done)
fill_pattern(pattern, value): expand value bits into pattern bit positions
offset(x,y,z) = bpp * (fill(mx,x) | fill(my,y) | fill(mz,z))
```
- Texture formats: `kelvin_color_format_info_map[66]`; DXT1/23/45 compressed; S3TC decode in
  `s3tc.c` (MIT).

---

## 6. APU (`mcpx/apu/**`)

- PCI 10DE:01B0 B1, BAR0 512 KiB. Key registers:
  - `ISTS` 0x1000 (GINTSTS bit0, FETINTSTS bit4, FENINTSTS bit5, FEVINTSTS bit6), `IEN` 0x1004
  - `FECTL` 0x1100 (method 5-7, trap reason 8-11), FECV 0x1110, FEAV 0x1118, FENADDR 0x115C,
    FEDECMETH 0x1300, FEDECPARAM 0x1304, FEMEMADDR 0x1324, FEMEMDATA 0x1334, FETFORCE0/1 0x1500/04
  - `SECTL` 0x2000 (XCNTMODE 3-4), XGSCNT 0x200C, VPVADDR 0x202C, VPSGEADDR 0x2030,
    VPSSLADDR 0x2034, GPSADDR 0x2040, GPFADDR 0x2044, EPSADDR 0x2048, EPFADDR 0x204C
  - GP output FIFOs 4× {BASE 0x3024+0x10i, END +4, CUR +8}; GP input 2× @0x3064;
    EP FIFOs @0x4024/0x4064
  - DSP windows: GP `GPXMEM` 0x0, `GPMIXBUF` 0x5000, `GPYMEM` 0x6000, `GPPMEM` 0xA000,
    `GPRST` 0xFFFC (GPRST/GPDSPRST/GPNMI/GPABORT); EP analogous
  - Front-end PIO methods `NV1BA0_PIO_*` 0x10..0x1800: VOICE_ON/OFF/RELEASE/PAUSE/
    GET_POSITION, per-voice config, HRTF, SSL, SUBMIX/HRTF headroom, OUTBUF, SGE handles
  - Voice struct `NV_PAVS_*` (0x80 bytes): VBIN/FMT, envelopes, HRTF target, PSL/PSH, ECNT,
    PAR_STATE (PAUSED bit18, NEW bit20, ACTIVE bit21), PAR_OFFSET/NEXT, VOLA/B/C,
    LFO_ENV, FCA/B, PITCH_LINK
- Constants: 256 max voices, 64 max 3D voices, 32 samples/frame, 32 mixbins, ADPCM 64
  samples/block, SSL 16 PRDs × 2 SSLs/voice, HRTF 128 entries, EP frame 5333 µs
  (256/48000), 192 bytes/ms at 48 kHz stereo S16.
- Frame loop: dedicated thread at ~5.33 ms cadence; VP mixes voices into 32×32 float mixbins,
  GP DSP consumes them, EP runs every 8th frame, monitor emits 256 stereo frames (0x400 bytes).
- DSP: Motorola DSP56300 core (Hatari-derived, GPL); XRAM 4096 / YRAM 2048 / PRAM 4096 words;
  128-word peripheral space at 0xFFFF80; 1024-word mixbuffer at 0x1400; 64 registers, 2×16 stack,
  12 interrupt levels. GP is realtime; EP every 8th frame.
- Voice processor: per-voice resampler (libsamplerate SRC_STATE), 2 state-variable filters,
  HRTF convolution + ITD, ADPCM decode, SSL scatter-gather, 4×64-bit voice lock bitmaps.

---

## 7. Storage

- IDE: QEMU PIIX3 core stand-in with NVIDIA ID 10DE:01BC B1. **[HW]** real controller is nForce
  IDE; XGrid should model the NVIDIA controller if aiming for accuracy.
- HDD security: `locked=on` makes IDENTIFY word 128 report security supported|enabled|locked;
  ATA SECURITY UNLOCK accepts any 512-byte password (no key checking). Xbox kernel derives the
  password from the EEPROM HDD key.
- FATX: only a host-side image creator exists (`ui/thirdparty/fatx/fatx.c`). Superblock:
  signature `0x58544146` ("FATX" LE), volume_id, sectors_per_cluster=4, root_cluster=1,
  4078 bytes padding; FAT starts at offset 0x1000, free entry `0xFFFFFFF8`.
- XDVDFS: **no host implementation in xemu**; game ISOs attach as raw ATAPI CD media and the
  guest kernel parses XDVDFS. XGrid needs an ATAPI CD path (not necessarily a host parser).
- XBE: `xemu-xbe.c/h` is host-side introspection only (XBEH signature `0x48454258`, virtual→
  physical translate of 0x10000, cert validation). Structs `xbe_header`/`xbe_certificate` are
  fully documented (title ID, title name UTF-16[40], alt IDs[16], media/region/ratings,
  disk number, version, LAN/signature keys).

---

## 8. USB / XID

- Two generic OHCI controllers, 4 root ports each; Xbox 1.0 daughterboard is a hub on OHCI0
  root port 1. IDs patched to 10DE:01C2 B1.
- XID gamepad:
  - VID 0x045E; PID 0x0202 (gamepad) / 0x0289 (Controller S); bcdDevice 0x0100; EP0 max packet 0x40.
  - Interface class `0x58`, subclass 0x42; two interrupt endpoints both address 0x02 (IN/OUT),
    max packet 0x20, interval 4.
  - XID descriptor (16 bytes): bLength=0x10, bDescriptorType=0x42, bcdXid=0x100, bType=1,
    bSubType 1/2, max input report 20, max output report 6, four alt product IDs 0xFFFF.
  - **Input report (20 bytes)**: bReportId, bLength, wButtons (bit0=DPAD_UP, A=0..RTHUMB=15 per
    BUTTON_MASK), bAnalogButtons[8] (A/B/X/Y/BLACK/WHITE as 0x00/0xFF, triggers axis>>7),
    sThumbLX/LY/RX/RY (int16).
  - **Output report (6 bytes)**: report_id, length, left_actuator_strength, right_actuator_strength.
  - Control: HID GET_REPORT 0x0100, SET_REPORT 0x0200, vendor GET_DESCRIPTOR 0x4200,
    XID_GET_CAPABILITIES (0x01) returning all-0xFF.
- XBLC headset: 0x045E:0x0283, class 0x78, endpoints 0x04/0x05, 48-byte packets, 16-bit
  samples, 16 kHz default; SET_SAMPLE_RATE 0x00, SET_AGC 0x01.

---

## 9. Networking (`nvnet`)

- 10DE:01C3 B1; MMIO 0x400 + I/O 8 bytes; PHY address 1, autoneg 250 ms.
- Descriptor (8 bytes): `{u32 buffer_addr; u16 length; u16 flags}`.
- Rings: TX/RX base 0x100/0x104, sizes 0x108 (TX low16, RX high16), current/next 0x11C/0x120/
  0x134/0x138.
- Regs: IRQ status/mask 0x000/0x004, polling 0x00C, TX/RX control+status 0x084/0x088/0x094/0x098,
  packet filter 0x08C, MAC 0x0A8/0x0AC, multicast 0x0B0-0x0BC, MII status 0x180, adapter control
  0x188, MDIO 0x190/0x194, power 0x268/0x26C.
- Descriptor flags: TX_VALID 1<<15, TX_LASTPACKET 1<<0, TX_ERROR 1<<14; RX_AVAIL 1<<15,
  RX_DESCRIPTORVALID 1<<0, RX_ERROR 1<<14.

---

## 10. Data structures / interfaces worth mirroring in C#

- `XboxMachineState`: machine config (bootrom, avpack, short animation, SMC version, encoder).
- `NV2AState`: PCI + VGA core + VRAM/RAMIN + per-block state + `PGRAPHState`.
- `PGRAPHState`: locks, interrupts, surfaces, DMA handles, vertex state, 0x2000-word regs,
  renderer pointer.
- `MCPXAPUState`: VP/GP/EP substates, regs[0x20000], monitor/throttle.
- Device communication patterns to re-express: memory regions with read/write ops and overlap
  priority; IRQ lines with fan-in/out; PCI bus with config space + BAR allocation; DMA into
  pinned guest RAM buffers; timers; per-device VM state inventory (useful save-state checklist).
