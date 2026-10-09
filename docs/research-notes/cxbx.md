# Cxbx-Reloaded — Architecture Notes (HLE Emulator)

Reference: `References/Cxbx-Reloaded-master`. License: GPL-2.0 (some files LGPL-2) — facts only.
Cxbx is a Windows HLE emulator that runs Xbox executables natively; XGrid will not use its
process/HLE model, but its format and register knowledge is the best available.

---

## 1. Process model (context only — not for XGrid)

```
cxbx.exe (GUI, separate process)
  → EmuShared shared-memory IPC
cxbxr-ldr.exe (32-bit loader, /BASE:0x10000)
  → reserves Xbox VA ranges, LoadLibrary("cxbxr-emu.dll")
cxbxr-emu.dll
  → memory manager → load XBE → CxbxrKrnlInit
  → XBE machine code runs NATIVELY in-process (no interpreter by default;
    experimental interpreter/JIT at src/devices/x86/EmuX86.cpp gated by bLLE_JIT)
  → HLE kernel functions patched into the XBE thunk table
  → LLE devices (NV2A, OHCI, NVNet) are C++ models with their own threads
```

---

## 2. XBE format (exact layouts as implemented)

All structs `#pragma pack(1)`; file offset = VA − `dwBaseAddr`. Magic `XBEH` (LE dword
`0x48454258`). `XBE_IMAGE_BASE = 0x00010000`, `KSEG0_BASE = 0x80000000`,
`XBOX_WRITE_COMBINED_BASE = 0xF0000000`, `SEGABOOT_EP_XOR = 0x40000000`.

### 2.1 Image header — 0x178 (376) bytes

| Offset | Size | Field |
|---|---|---|
| 0x000 | 4 | dwMagic "XBEH" |
| 0x004 | 256 | pbDigitalSignature (RSA-encrypted SHA-1 of rest of header) |
| 0x104 | 4 | dwBaseAddr (usually 0x00010000) |
| 0x108 | 4 | dwSizeofHeaders |
| 0x10C | 4 | dwSizeofImage |
| 0x110 | 4 | dwSizeofImageHeader (usually 0x178) |
| 0x114 | 4 | dwTimeDate |
| 0x118 | 4 | dwCertificateAddr |
| 0x11C | 4 | dwSections |
| 0x120 | 4 | dwSectionHeadersAddr |
| 0x124 | 4 | dwInitFlags (bMountUtilityDrive:1, bFormatUtilityDrive:1, bLimit64MB:1, bDontSetupHarddisk:1, unused) |
| 0x128 | 4 | dwEntryAddr (XOR-encoded) |
| 0x12C | 4 | dwTLSAddr |
| 0x130 | 4 | dwPeStackCommit |
| 0x134 | 4 | dwPeHeapReserve |
| 0x138 | 4 | dwPeHeapCommit |
| 0x13C | 4 | dwPeBaseAddr |
| 0x140 | 4 | dwPeSizeofImage |
| 0x144 | 4 | dwPeChecksum |
| 0x148 | 4 | dwPeTimeDate |
| 0x14C | 4 | dwDebugPathnameAddr |
| 0x150 | 4 | dwDebugFilenameAddr |
| 0x154 | 4 | dwDebugUnicodeFilenameAddr |
| 0x158 | 4 | dwKernelImageThunkAddr (XOR-encoded) |
| 0x15C | 4 | dwNonKernelImportDirAddr |
| 0x160 | 4 | dwLibraryVersions (count) |
| 0x164 | 4 | dwLibraryVersionsAddr |
| 0x168 | 4 | dwKernelLibraryVersionAddr |
| 0x16C | 4 | dwXAPILibraryVersionAddr |
| 0x170 | 4 | dwLogoBitmapAddr |
| 0x174 | 4 | dwSizeofLogoBitmap |

Header extras: bytes `[0x178, dwSizeofHeaders)` opaque. Logo is 4-bit grayscale RLE 100×17
(8-bit token {Type1=1, Len:3, Data:4} or 16-bit token {Type1=0, Type2=1, Len:10, Data:4}).

### 2.2 Certificate

| Offset | Size | Field |
|---|---|---|
| 0x000 | 4 | dwSize |
| 0x004 | 4 | dwTimeDate |
| 0x008 | 4 | dwTitleId |
| 0x00C | 80 | wsTitleName[40] UTF-16 |
| 0x05C | 64 | dwAlternateTitleId[0x10] |
| 0x09C | 4 | dwAllowedMedia |
| 0x0A0 | 4 | dwGameRegion |
| 0x0A4 | 4 | dwGameRatings |
| 0x0A8 | 4 | dwDiscNumber |
| 0x0AC | 4 | dwVersion (disc = &0xFF, patch = >>8) |
| 0x0B0 | 16 | bzLanKey |
| 0x0C0 | 16 | bzSignatureKey |
| 0x0D0 | 256 | bzTitleAlternateSignatureKey[16][16] |
| 0x1D0 | 4 | dwOriginalCertificateSize |
| 0x1D4 | 4 | dwOnlineService |
| 0x1D8 | 4 | dwSecurityFlags |
| 0x1DC | 16 | bzCodeEncKey |

Region: NA=0x1, JAPAN=0x2, RESTOFWORLD=0x4, MANUFACTURING=0x80000000.
Media: HARD_DISK=0x1, DVD_X2=0x2, DVD_CD=0x4, CD=0x8, DVD_5_RO=0x10, DVD_9_RO=0x20,
DVD_5_RW=0x40, DVD_9_RW=0x80, DONGLE=0x100, MEDIA_BOARD=0x200, NONSECURE_HARD_DISK=0x40000000,
NONSECURE_MODE=0x80000000, MASK=0x00FFFFFF.

### 2.3 Section header — 0x38 (56) bytes

| Offset | Size | Field |
|---|---|---|
| 0x00 | 4 | dwFlags (bWritable:1, bPreload:1, bExecutable:1, bInsertedFile:1, bHeadPageRO:1, bTailPageRO:1, unused) |
| 0x04 | 4 | dwVirtualAddr |
| 0x08 | 4 | dwVirtualSize |
| 0x0C | 4 | dwRawAddr (file offset) |
| 0x10 | 4 | dwSizeofRaw |
| 0x14 | 4 | dwSectionNameAddr |
| 0x18 | 4 | dwSectionRefCount |
| 0x1C | 4 | dwHeadSharedRefCountAddr |
| 0x20 | 4 | dwTailSharedRefCountAddr |
| 0x24 | 20 | bzSectionDigest (SHA-1) |

Flags: WRITEABLE=0x1, PRELOAD=0x2, EXECUTABLE=0x4, INSERTFILE=0x8, HEAD_PAGE_READONLY=0x10,
TAIL_PAGE_READONLY=0x20. **No section encryption**: sections are raw bytes.

### 2.4 Library version (16 bytes) / TLS (24 bytes)

- Library: szName[8], wMajorVersion u16, wMinorVersion u16, wBuildVersion u16, flags u16
  (QFEVersion:13, Approved:2, bDebugBuild:1).
- TLS: dwDataStartAddr, dwDataEndAddr, dwTLSIndexAddr, dwTLSCallbackAddr, dwSizeofZeroFill,
  dwCharacteristics.

### 2.5 XOR-obfuscated pointers (per type)

| Key | Retail | Debug | Chihiro |
|---|---|---|---|
| Entry point | 0xA8FC57AB | 0x94859D4B | 0x40B5C16E |
| Kernel thunk table | 0x5B6D40B6 | 0xEFB1F152 | 0x2290059D |

- Xbe type detection: Chihiro if `(dwEntryAddr & 0xF0000000) == 0x40000000` or `boot.id` exists;
  Debug if `dwKernelImageThunkAddr >= 0x80000000`; else Retail.
- Signature: SHA-1 of `[0x104, dwSizeofHeaders)` vs RSA public-decrypt with retail/debug/Chihiro
  keys (284-byte `RSA1`, 2048-bit modulus, e=65537). Unsigned XBEs still run.
- `CheckSectionIntegrity`: SHA-1 of raw section bytes vs digest.

### 2.6 Runtime section mapping (`XeLoadSection`, ordinal 327)

- On first reference: allocate virtual memory at the section VA (COMMIT, EXECUTE_READWRITE),
  zero it, copy cached raw section data, increment head/tail shared-page refcounts.
- Sections smaller than a page can share a page with the next section (e.g. DOA3 "XGRPH") —
  head/tail refcount logic handles this.
- `XeUnloadSection` reverses, decommitting only the interior when head/tail refcounts are nonzero.

### 2.7 Disc location

- XBE at `\default.xbe` in XDVDFS root; multi-title paths use `;` (e.g. `...\title;subdir\
  default.xbe`) to select a mount path / auto-mount `D:`.
- Chihiro: `SEGABOOT.XBE` from a 2 MB Media Board ROM if `boot.id` exists.

---

## 3. Kernel exports / HLE

- `CxbxKrnl_KernelThunkTable[379]` (ordinals 0–378): host pointers per ordinal. Real
  `xboxkrnl.exe` exports are ordinal-only; Cxbx names follow NT/ReactOS (`Av*`, `Dbg*`, `Ex*`,
  `Hal*`, `Io*`, `Ke*`, `Kf*`, `Ki*`, `Mm*`, `Nt*`, `Ob*`, `Ps*`, `Rtl*`, `Xc*`, `Xe*`, `Xbox*`).
- Thunk patching: XBE thunk array at `dwKernelImageThunkAddr ^ key`; each entry bit31 set with
  ordinal in low bits; replaced with host pointer. Data exports patched to host variable
  addresses. (HLE technique — XGrid instead implements a kernel export table.)
- XAPI (xapilib) handled by symbol scanning + trampoline patches: D3D8 (largest), DSound,
  XInput/XAPI, OHCI/USB, JVS, XACT.

### Export categories (counts)

| File | Exports | Ordinal range | Content |
|---|---|---|---|
| EmuKrnlKe.cpp | 67 | 92–163 | threads, timers, DPCs, IRQL, events, semaphores, mutants, queues, waits |
| EmuKrnlRtl.cpp | 62 | 260–320, 352, 361–364 | strings, critical sections, memory, time fields, sprintf, RtlRip |
| EmuKrnlNt.cpp | 55 | 184–238 | NtCreateFile/ReadFile/WriteFile/DeviceIoControlFile, VM, events, timers |
| EmuKrnlIo.cpp | 32 | 59–91, 359 | IRP/device objects, IoCreateDevice, IofCallDriver, IoMarkIrpMustComplete |
| EmuKrnlMm.cpp | 26 | 165–183 | contiguous/system memory, physical address, protect, statistics |
| EmuKrnlHal.cpp | 25 | 38–50, 329–334, 356–366 | interrupts, SMBus, PCI space, port I/O buffers, tray/shutdown |
| EmuKrnl.cpp | 24 | 51–58, 160–163, 357, 361–373 | Interlocked*, KfRaise/LowerIrql, KiBugCheckData |
| EmuKrnlEx.cpp | 23 | 12–34 | pool allocation, RWLocks, non-volatile settings, object types |
| EmuKrnlXc.cpp | 17 | 335–351 | crypto: SHA, RC4, HMAC, RSA, DES |
| EmuKrnlOb.cpp | 13 | 239–251 | object manager, handle tables, symbolic links |
| EmuKrnlXbox.cpp | 7 | 321–326, 353–355 | keys, hardware info, kernel version, XeImageFileName |
| EmuKrnlDbg.cpp | 6 | 5–11 | debug prints/breakpoints |
| EmuKrnlPs.cpp | 6 | 254–259 | system threads, statistics, notify routines |
| EmuKrnlAv.cpp | 4 | 1–4 | saved framebuffer address, TV encoder, display mode |
| EmuKrnlXe.cpp | 4 | 326–328, 355 | XeImageFileName, XeLoadSection, XeUnloadSection, XePublicKeyData |
| EmuKrnlFs.cpp | 3 | 35–37 | FSC cache controls |
| EmuKrnlKd.cpp | 2 | 88–89 | debugger present flags |
| EmuKrnlPhy.cpp | 2 | 252–253 | Ethernet PHY link state/init |

- DevKit-only MmDbg* at 374–378; unknown stubs 367–369; profiling 370–373.
- Representative: AvSetDisplayMode(3), ExAllocatePool(14), HalReturnToFirmware(49),
  KeDelayExecutionThread(99), KeQueryPerformanceCounter(126), KeSetTimer(149),
  MmAllocateContiguousMemory(165), MmGetPhysicalAddress(173), NtAllocateVirtualMemory(184),
  NtCreateFile(190), NtWaitForSingleObject(233), PsCreateSystemThreadEx(255), XeLoadSection(327),
  XcSHAInit(335), XboxHDKey(323), XboxKrnlVersion(324).
- Kernel objects are Xbox-layout structs in emulated memory (KTHREAD, KEVENT, KSEMAPHORE,
  KMUTANT, KPCR, KINTERRUPT, DISPATCHER_HEADER) with wait/APC/DPC semantics and a timer wheel.
- Memory manager: PFN database, PTE page tables, `PHYSICAL_MAP_BASE = 0x80000000`; virtual
  regions User/System/Devkit/Contiguous; `ClaimGpuMemory` reserves contiguous memory for GPU.

---

## 4. NV2A

### 4.1 Register block table (BAR0 = 0xFD000000, 16 MB; BAR1 = 0xF0000000 system RAM)

| Offset | Size | Block | Notes |
|---|---|---|---|
| 0x000000 | 0x1000 | PMC | BOOT_0 = 0x02A000A2; INTR_0/EN_0 |
| 0x001000 | 0x1000 | PBUS | vendor 0x10DE, class display-3D, rev 0xA1 |
| 0x002000 | 0x2000 | PFIFO | pushbuffer FIFO + DMA |
| 0x007000 | 0x1000 | PRMA | stub |
| 0x008000 | 0x1000 | PVIDEO | overlay |
| 0x009000 | 0x1000 | PTIMER | numerator/denominator, TIME_0/1, alarm |
| 0x00A000 | 0x1000 | PCOUNTER | stub |
| 0x00B000 | 0x1000 | PVPE | stub (MPEG2) |
| 0x00D000 | 0x1000 | PTV | stub (TV encoder) |
| 0x0A0000 | 0x20000 | PRMFB | VGA memory alias |
| 0x0C0000 | 0x8000 | PRMVIO | VGA sequencer/GC alias |
| 0x100000 | 0x1000 | PFB | CFG0=3, CSTATUS=VRAM size, WBC=0 |
| 0x101000 | 0x1000 | PSTRAPS | stub |
| 0x400000 | 0x2000 | PGRAPH | 2D/3D engine |
| 0x600000 | 0x1000 | PCRTC | START, RASTER, VBlank IRQ |
| 0x601000 | 0x1000 | PRMCIO | VGA CRTC alias |
| 0x680000 | 0x1000 | PRAMDAC | NVPLL/MPLL/VPLL, PLL lock bits |
| 0x681000 | 0x1000 | PRMDIO | VGA palette alias |
| 0x700000 | 0x100000 | PRAMIN | 1 MB instance RAM |
| 0x800000 | 0x400000 | USER | per-channel DMA PUT/GET/REF kick |
| 0xC00000 | 0x400000 | UREMAP | mirror of USER |

- PRAMIN physical pointer 0xFD700000, committed in 64 KB chunks; GPU IRQ = Xbox IRQ 3.

### 4.2 Pushbuffer encoding (`nv2a.h` macros)

- `PUSH_TYPE_MASK 0x2`, `PUSH_METHOD_MASK 0x1FFC`, `PUSH_SUBCH_MASK 0xE000` (>>13),
  `PUSH_COUNT_MASK 0x1FFC0000` (>>18), `PUSH_INSTR_MASK 0xE0000000` (>>29: 0=imm-incr,
  1=jmp-near, 2=imm-noinc), `PUSH_ADDR_FAR_MASK 0xFFFFFFFC`, `PUSH_ADDR_NEAR_MASK 0x1FFFFFFC`.
- Pusher decodes: old jump `(w & 0xE0000003) == 0x20000000`; jump `(w&3)==1`;
  call `(w&3)==2`; return `w == 0x00020000`; increasing `(w & 0xE0030003)==0`;
  non-increasing `(w & 0xE0030003)==0x40000000`. 128 CACHE1 slots at `(put+4) & 0x1FC`.
- Puller: `method = entry & 0x1FFC`, `subchannel = (entry>>13)&7`; method 0 binds RAMHT handle;
  methods ≥0x100 forwarded; 0x180..0x1FF resolve parameter via RAMHT.
- RAMHT hash: XOR-fold handle in `bits = ffs(size)-2` chunks, XOR `channel_id << (bits-4)`;
  entry 8 bytes {handle, context} at `(NV_PFIFO_RAMHT & 0xFFFFF000) + hash*8`.
- DMA object: `nv_dma_map` returns `vram_ptr + (frame & 0x07FFFFFF)`.

### 4.3 PGRAPH / NV097 method groups

- Classes: NV_CONTEXT_PATTERN 0x44, NV_CONTEXT_SURFACES_2D 0x62, NV_IMAGE_BLIT 0x9F,
  NV_KELVIN_PRIMITIVE 0x97.
- Object/context 0x000–0x1A8: SET_OBJECT 0x0, NO_OPERATION 0x100, WAIT_FOR_IDLE 0x110,
  flips 0x120–0x130, SET_CONTEXT_DMA_* 0x180–0x1A8.
- Surface 0x200–0x214: CLIP_H/V 0x200/0x204, FORMAT 0x208 (color 0x0F: X1R5G5B5..A8R8G8B8=8,
  B8=9, G8B8=0xA; zeta 0xF0: Z16=1, Z24S8=2; type 0xF00: PITCH=1, SWIZZLE=2; AA 0xF000;
  width 0x00FF0000, height 0xFF000000), PITCH 0x20C, COLOR_OFFSET 0x210, ZETA_OFFSET 0x214.
- Register combiners 0x260–0x2A8, 0xA60–0xAF0, 0x1E20–0x1E68: alpha ICW 0x260, specular/fog
  CW0/CW1 0x288/0x28C, factor0/1 0xA60/0xA80, alpha OCW 0xAA0, color ICW 0xAC0, color OCW
  0x1E40, combiner control 0x1E60.
- Control/fog 0x290–0x2E0: CONTROL0 0x290, fog mode/gen/enable/color 0x29C–0x2A8, window clip
  0x2B4–0x2E0.
- State enables/functions 0x300–0x3CC: ALPHA_FUNC 0x33C, ALPHA_REF 0x340, blend factors
  0x344/0x348, blend color/equation 0x34C/0x350, depth func/mask 0x354/0x35C, stencil
  0x364–0x378, polygon offset 0x384/0x388, polygon mode 0x38C/0x390, clip min/max 0x394/0x398,
  cull/front face 0x39C/0x3A0, normalization 0x3A4, light enable 0x3BC, texgen 0x3C0–0x3CC.
- Matrices 0x420–0x6C0: tex matrix enable 0x420, projection 0x440, model-view 0x480 (×4),
  inverse 0x580, composite 0x680, texture matrices 0x6C0; texgen planes 0x840–0x870; fog
  0x9C0/0x9D0.
- Lighting 0xC00–0x1068.
- Vertex submission 0x1500–0x181C: SET_VERTEX3F 0x1500, SET_VERTEX4F 0x1518,
  DATA_ARRAY_OFFSET 0x1720, DATA_ARRAY_FORMAT 0x1760 (type 0xF, size 0xF0, stride 0xFFFFFF00),
  CLEAR_REPORT_VALUE 0x17C8, ZPASS_PIXEL_COUNT_ENABLE 0x17CC, GET_REPORT 0x17D0,
  SET_BEGIN_END 0x17FC, ARRAY_ELEMENT16 0x1800, ARRAY_ELEMENT32 0x1808, DRAW_ARRAYS 0x1810
  (start 0x00FFFFFF, count 0xFF000000), INLINE_ARRAY 0x1818, DATA2F_M 0x1880, 2S 0x1900,
  4UB 0x1940, 4S_M 0x1980, 4F_M 0x1A00.
- Textures 0x1B00–0x1B3C: offset 0x1B00, format 0x1B04 (context DMA 0x3, dimensionality 0xF0,
  color 0xFF00), address 0x1B08, control0/1 0x1B0C/0x1B10, filter 0x1B14, image rect 0x1B1C,
  palette 0x1B20, border 0x1B24, bump-env 0x1B28–0x1B3C.
- Sync/clear 0x1D6C–0x1D9C: semaphore offset 0x1D6C, release 0x1D70, zmin/zmax 0x1D78,
  compress zbuffer 0x1D80, zstencil/color clear 0x1D8C/0x1D90, CLEAR_SURFACE 0x1D94 (color
  mask 0xF0), clear rects 0x1D98/0x1D9C.
- Transform program 0x1E80–0x1EA4: transform data 0x1E80, launch 0x1E90, execution mode 0x1E94,
  ctx write enable 0x1E98, program load/start 0x1E9C/0x1EA0, constant load 0x1EA4;
  SET_TRANSFORM_PROGRAM 0xB00, SET_TRANSFORM_CONSTANT 0xB80.

### 4.4 Shaders

- Renderer is OpenGL 3.3 core (D3D9 path belongs to the HLE D3D8 layer).
- Vertex program: 4 dwords/slot, token field map (`FLD_ILU` token1 25..27, `FLD_MAC` 21..24,
  `FLD_A_R` token2 28..31, `FLD_OUT_MAC_MASK` token3 24..27, `FLD_FINAL` token3 bit0).
  Opcodes ILU {NOP,MOV,RCP,RCC,RSQ,EXP,LOG,LIT} and MAC {NOP,MOV,MUL,ADD,MAD,DP3,DPH,DP4,DST,
  MIN,MAX,SLT,SGE,ARL}; swizzles; `VSH_D3DSCM_CORRECTION 96`. Outputs oPos, oD0, oD1, oB0, oB1,
  oFog, oT0..oT3; converts screen-space to clip-space and handles z_perspective/clip-range.
- Pixel shader: register-combiner translation. Enums: PS_TEXTUREMODES_* (NONE, PROJECT2D/3D,
  CUBEMAP, PASSTHRU, CLIPPLANE, BUMPENVMAP[_LUM], BRDF, DOT_*, DEPENDENT_*, DOT_PRODUCT),
  PS_INPUTMAPPING_*, PS_REGISTER_* (ZERO/DISCARD, C0/C1, FOG, V0/V1, T0–T3, R0/R1, V1R0_SUM,
  EF_PROD), PS_COMBINEROUTPUT_*, PS_FINALCOMBINERSETTING_*. 8 stages, samplers texSamp0..3.
- `generate_shaders()` binds attributes v0..v15; cache keyed by `ShaderState`.
- Texture swizzle algorithm (same Morton-style interleave as xemu; see xemu.md §5.8).
- Texture formats: linear, swizzled (SZ_*), compressed DXT1/3/5; paletted formats converted on
  CPU before upload. 4 texture stages. Surface formats via `kelvin_surface_color_format_map[16]`.
- VBlank simulated at 60 Hz → GPU IRQ; PCRTC RASTER returns a fake 0/mid/end rotation.

---

## 5. USB / XID

- Full OHCI controller, 4 root ports. Register indices (addr>>2): 0 HcRevision, 1 HcControl,
  2 CommandStatus, 3 InterruptStatus, 4/5 InterruptEnable/Disable, 6 HcHCCA, 7 PeriodCurrentED,
  8/9 ControlHead/CurrentED, 10/11 BulkHead/CurrentED, 12 DoneHead, 13 FmInterval, 14 FmRemaining,
  15 FmNumber, 16 PeriodicStart, 17 LSThreshold, 18/19 RhDescriptorA/B, 20 RhStatus, 21–24 RhPort.
- Memory structs: `OHCI_ED {Flags,TailP,HeadP,NextED}` 16 B; `OHCI_TD {Flags,CurrentBufferPointer,
  NextTD,BufferEnd}` 16 B; `OHCI_ISO_TD` 24 B; `OHCI_HCCA {InterruptTable[32],FrameNumber,Pad,
  DoneHead}` 128 B.
- ED bits: FA 0..6, EN 7..10, D 11..12, S bit13, K bit14, F bit15, MPS 16..26. TD bits:
  R bit18, DP 19..20 (0=SETUP,1=OUT,2=IN), DI 21..23, T0/T1 24/25, EC 26..27, CC 28..31
  (NOERROR=0, CRC=1, BITSTUFFING=2, DATATOGGLE=3, STALL=4).
- HcControl: CBSR 0..1, PLE bit2, IE bit3, CLE bit4, BLE bit5, HCFS 6..7, IR bit8, RWC bit9,
  RWE bit10. Port bits: CCS=1, PES=2, PSS=4, POCI=8, PRS=16, PPS=0x100, LSDA=0x200, CSC=0x10000,
  PESC=0x20000, PSSC=0x40000, OCIC=0x80000, PRSC=0x100000.
- USB frame = 1 ms, USB_HZ = 12 MHz; HCCA table indexed by `HcFmNumber & 0x1F`.
- XID gamepad: VID 0x045E, PID 0x0202, bcdDevice 0x0100, bcdUSB 0x0110, EP0 0x40. Interface
  class 0x58, subclass 0x42, protocol 0; interrupt IN 0x82 / OUT 0x02, max packet 0x20,
  interval 4. XID descriptor type 0x42: {bLength 0x10, bcdXid 0x100, bType 1, bSubType 1,
  max input 20, max output 6, wAlternateProductIds[4] = 0xFFFF}.
- **Input report 20 bytes**: bReportId 0, bLength 20, wButtons (0=dpad up, 1=down, 2=left,
  3=right, 4=start, 5=back, 6=L-thumb click, 7=R-thumb click), bAnalogButtons[8]
  (A,B,X,Y,White,Black,LTrigger,RTrigger; 0x00/0xFF or scaled), sThumbLX/LY/RX/RY int16.
- **Output report 6 bytes**: report_id, length=6, left_actuator_strength, right_actuator_strength.
- Control requests: GET_REPORT 0x0100, SET_REPORT 0x0200, XID_GET_CAPABILITIES (vendor 0x01,
  wValue 0x0100/0x0200) all-0xFF, vendor GET_DESCRIPTOR wValue 0x4200 → XID descriptor.
- Cxbx's USB/XID path is compiled out (`#if 0`); default input is HLE XAPI. Use as protocol
  reference only.
- Input manager: 24-control layout (16 buttons + 4 axis pairs + motor); binding map into XID
  buffer documented in `UpdateInputXpad`.

---

## 6. Storage / disc

- XISO mount (`XisoMount.cpp`) via Dokany; requires `\default.xbe` at root. No DVD hardware
  model: `EmuCdRom.cpp` creates a kernel DEVICE_OBJECT with host backing; `CdRomReadOnly` /
  `CdRomDeviceControl` are unimplemented stubs.
- XDVDFS structures (also in xdvdfs-tools):
  - Volume descriptor at sector 32 (2048-byte sectors): magic `MICROSOFT*XBOX*MEDIA` at 0x00 and
    0x7EC; RootDirectory u32 @0x14; RootDirectorySize u32 @0x18; timestamp u64 @0x1C.
    Fallback for Xbox 360 XGD images: descriptor at `2048*32*6192 = 0x18300000`.
  - Directory entry: LeftSubTree u16 @0, RightSubTree u16 @2, FileStartSector u32 @4,
    FileSize u32 @8, FileAttributes u8 @0xC (0x10 dir), FilenameLength u8 @0xD, name from @0xE.
    Child lookup: sector = parent sector, offset = subtree*4; first child of a dir at
    FileStartSector offset 0.
- **No FATX implementation** — only a synthesized 4096-byte superblock (`XTAF` signature) and
  partition-to-host-directory mapping. FSCTL codes: dismount 0x00090020, read FATX metadata
  0x0009411C, write 0x00098120.
- Partition table (from `EmuFile.h`): Magic[16], Res0[32], TableEntries[14] each
  {Name[16], Flags u32, LBAStart u32, LBASize u32, Reserved u32}.
- FATX superblock (4096 bytes): Signature u32 'XTAF', VolumeID u32, ClusterSize u32,
  FatCopies u32, Name wchar[32], OnlineData[2048], Unused[1968].
- XPR resource bundle: magic 'XPR0'/'XPR1'; `X_D3DResourceLoc {Common, Data, Lock, Format, Size}`.

---

## 7. Audio / input

- Default audio is HLE DirectSound8 (`CxbxInitAudio`, all IDirectSound* patched); XACT engine
  mostly S_OK stubs.
- LLE APU device (`APUDevice.cpp`) is a stub not connected to the PCI bus: PCI 10DE:01B0,
  BAR0 I/O 0xD000, BAR1 I/O 0xD200, BAR2 memory; VP 0x20000/GP 0x30000/EP 0x50000; 0x200C
  returns APU time; VP 0x10 returns 0x80. AC97 device similarly stubbed. **No usable APU
  reference here.**
- Input backends: DInput keyboard/mouse, XInput, SDL2 joystick, libusb pass-through, emulated
  devices; profiles per port.

---

## 8. Highest-value artifacts for XGrid

1. XBE structures/offsets (§2).
2. Kernel ordinal→semantics map (`KernelThunk.cpp`, 379 entries) — single best kernel-compat
   reference; convert into XGrid's declarative ordinal/signature table.
3. NV2A register block map + NV097 method map (§4.1, §4.3).
4. Pushbuffer/method encoding (§4.2).
5. Texture swizzle algorithm.
6. Shader translation behavior (VSH tokens, register combiners).
7. XID report formats/descriptors (§5).
8. OHCI register/ED/TD layouts (§5).
9. XDVDFS/partition structures (§6).
