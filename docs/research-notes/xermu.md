# Xermu — Architecture Notes (Standalone Xbox Emulator)

Reference: `References/Xermu-main` (C++20, from-scratch, "guided executor" JIT).
License: **none — all rights reserved**. Facts and design lessons only; do not copy source or tests.

---

## 1. Overall architecture

```
src/
  main.cpp            SDL3/ImGui/Vulkan GUI; frame loop; NV2A render sync
  cpu/                JIT CPU ("guided executor")
    context.hpp       GuestContext, StopReason, offsets
    platform.hpp      OS memory: RWX slabs, 4 GB aliased fastmem window
    mmio.hpp          MmioMap region table (max 32 regions)
    code_cache.hpp    CodeCache: 32 MB RWX + 4 KB helper page + 64 KB thunk slab
    trace.hpp         Trace, TraceCache (2-level direct-mapped)
    emitter.hpp       Raw byte emitter, EA synthesis, fastmem helpers, epilogs
    trace_builder.*   TraceBuilder, TraceArena, SoftTlb, MmioHelpers, build()
    insn_dispatch.hpp Two-level mnemonic→handler dispatch
    executor.*        Executor, run loop, paging walk, IRQ delivery, block linking
    privileged.cpp    HLT/IN/OUT/CR/MSR/IDT/etc.
    jit_helpers.*     extern "C" slow-path helpers
    veh_handler.cpp   Windows VEH: MMIO hot-patch, #DE, #UD, SMC
  xbox/               Device emulation (header-only, flat register files)
    address_map.hpp, setup.hpp, emu_thread.hpp
    nv2a.hpp, pgraph.hpp, apu.hpp, ide.hpp, usb.hpp, smbus.hpp, flash.hpp,
    pci.hpp, pic.hpp, pit.hpp, ioapic.hpp, cmos.hpp, acpi.hpp, misc_io.hpp, ram_mirror.hpp
    pe_loader.hpp, nboxkrnl_boot.hpp, nboxkrnl_host.hpp, nboxkrnl_io.hpp,
    nboxkrnl_paths.hpp, nboxkrnl_keys.hpp
    hle/bootstrap.hpp, hle/xbe_loader.hpp, hle/hle_kernel.hpp
  gpu/
    nv2a_vk_renderer.*   Vulkan renderer, single device memory, compute push-buffer parser
    shaders/*.glsl       pushbuf_parse.comp, nv2a_draw.vert/frag, scanout.vert/frag
  tests/               ~50 NASM .asm suites + C++ unit tests + test_runner
```

- Emulation loop: `Executor::run(entry_eip, max_steps)`; per iteration: deliver pending IRQs,
  translate EIP if paging, look up/build trace, link forward edges, dispatch, handle stop reasons
  (PRIVILEGED / PAGE_FAULT / DIVIDE_ERROR / INVALID_OPCODE). Spin-loop heuristics + periodic
  device tick (`tick_period=1024` traces); HLT fast-forwards ticks.
- `EmuThread` runs 4,000,000 steps per iteration on a worker thread; GUI main thread syncs
  RAM/PFIFO and dispatches GPU work each frame.

---

## 2. CPU ("guided executor")

- **Execution model**: no IR, no register allocator. Guest EAX/ECX/EDX/EBX/EBP/ESI/EDI are pinned
  to host registers; guest ESP lives in `ctx->gp[GP_ESP]`; guest EFLAGS live in host RFLAGS.
  Clean instructions (no memory operand, not privileged, not terminator) are copied verbatim to
  the JIT buffer. INC/DEC `0x40-0x4F` are re-encoded as `FF /0`/`FF /1` (REX collision).
- Memory instructions: EA synthesized into R14D, rewritten to `OP reg,[R12+R14]` fastmem; under
  paging an inline `CALL translate_va_jit` precedes it. Terminators end the trace with a
  save-GP + write-`next_eip` + `RET` epilog plus a linkable `JMP rel32`.
- **Decoder**: Zydis 4.1.0 legacy-32, `ZydisDecoderDecodeFull`; no hand-written opcode tables.
- **Dispatch**: `uint8_t MNEMONIC_CLASS[ZYDIS_MNEMONIC_MAX_VALUE+1]` (~1.8 KB) →
  `InsnClass INSN_CLASS_TABLE[IC_MAX]` with flags `ICF_HAS_DISPATCH/ICF_CLEAN_COPY/
  ICF_TERMINATOR/ICF_PRIVILEGED`. 24 classes (IC_CLEAN, IC_LEA, IC_MOV_MEM, IC_ALU_MEM,
  IC_TEST_MEM, IC_PUSH, IC_POP, IC_PUSH_IMM, IC_LEAVE, IC_MOVZX_MEM, IC_MOVSX_MEM, IC_FPU_MEM,
  IC_SSE_MEM, IC_PUSHFD, IC_POPFD, IC_STRING, IC_PUSHAD, IC_POPAD, IC_FLAGMEM, IC_ENTER,
  IC_XLATB, IC_TERMINATOR, IC_PRIVILEGED, IC_NONE).
- **Trace builder**: 3 phases (pre-scan, backward flag liveness, emit); `MAX_INSN_COUNT=512`,
  `MAX_TRACE_BYTES=65536`, max 256 mem sites. Liveness formula
  `live_before = (live & ~written) | tested`, `ARITH_FLAGS = CF|PF|AF|ZF|SF|OF`; dispatch sites
  bracket with `PUSHFQ/POPFQ` only when needed.
- **Fastmem encoding**: `[R12+R14]` via `REX=0x43`, `SIB=0x34`, ModRM mod=00; loads/stores
  0x8A/0x8B/0x88/0x89, MOVZX 0F B6/B7, MOVSX 0F BE/BF, immediate stores C6/C7. Patchable regions
  padded with NOPs to ≥5 bytes for VEH hot-patching.
- **Code cache**: single RWX allocation: 32 MB JIT + 4 KB helper page + 64 KB thunk slab,
  16-byte-aligned bump allocator. On exhaustion: flush everything and rebuild once (a weakness).
- **Trace cache**: two-level direct-mapped keyed on guest VA:
  `page_map[(eip>>12) & 0x7FFF][(eip & 0xFFF)>>1]` (L1 32768 pages = 128 MB, L2 2048/page),
  full EIP verified. Fixed `Trace pool[65536]`.
- **Linking**: forward static exits only; backward edges intentionally return to the run loop
  for device ticks/IRQs. `unlink_trace` zeroes inbound rel32s before invalidation.
- **SMC**: page-level write protection (`code_page_bitmap` + `VirtualProtect`/`mprotect`); VEH
  invalidates all traces on the page. Zero per-store cost.
- **MMIO**: MMIO pages are `PAGE_NOACCESS` in the 4 GB window; first fault hot-patches the
  instruction with `CALL rel32` to one of 48 shared helpers (24 read + 24 write × sizes 1/2/4)
  plus 3 write-imm tails. Non-patchable sites are software-emulated in the VEH. Windows-only.
- **Flags**: eager, in host RFLAGS; saved/restored at trace boundaries; `virtual_if` models IF;
  `IRETD` mask `0x003F7FD5 | 0x02`.
- **MMU**: 32-bit non-PAE walk (4 KB and 4 MB pages, `PDE_PS=0x80`, 4 MB base `0xFFC00000`);
  `SoftTlb` 256-entry direct-mapped read/write split; flush on CR3 write, full trace flush on
  CR0 write; INVLPG flushes one VA. Paging emits a C call per access (known weak point).
- **FPU/MMX/SSE**: FXSAVE areas for guest/host; FCW=0x037F, MXCSR=0x1F80; register-only ops run
  natively; TARGET_SSE=1 (SSE1+MMX only).
- **Privileged**: HLT, CLI/STI, CPUID (leaf 0 "GenuineIntel"; leaf 1 EAX `0x00000683` family 6/
  model 8/stepping 3, EDX `0x0383F9FF`), RDTSC, RDMSR/WRMSR (SYSENTER, TSC, MTRR), GDT/IDT/LDT/
  TSS, MOV CR/DR, INVLPG, RDPMC, WBINVD/INVD/CLTS/LMSW, SYSENTER/SYSEXIT, INT/INT3/INTO, IN/OUT,
  REP INS/OUTS, LES/LDS, BCD adjust, SALC, PUSHA/POPA, BOUND, ARPL.
- `deliver_interrupt(vector, return_eip, has_error, error_code)` reads 8-byte IDT gate, pushes
  EFLAGS/CS/EIP(+error), merges `virtual_if`, clears IF for interrupt gates, sets EIP.
- Mini 16-bit interpreter `interpret_real_mode_boot()` handles the BIOS reset-vector prologue.

---

## 3. Memory system

- `GUEST_RAM_SIZE = 128 MB` (retail 64 MB; extra covers devkits and kernel page tables).

### Address map (`src/xbox/address_map.hpp`)

| Region | Base | Size |
|---|---|---|
| Main RAM | 0x00000000 | 64/128 MB |
| RAM mirror (NV2A tiling alias) | 0x0C000000 | 0x08000000 (128 MB) |
| Flash ROM | 0xF0000000 | 1 MB |
| NV2A GPU | 0xFD000000 | 16 MB |
| APU | 0xFE800000 | 4 MB |
| I/O APIC | 0xFEC00000 | 4 KB |
| USB0 / USB1 | 0xFED00000 / 0xFED08000 | 4 KB each |
| BIOS shadow (flash alias) | 0xFF000000 | 16 MB |

- `alias_pa()`: PA 0x80000000-0x87FFFFFF → 0 (contiguous alias); PA 0xF0000000-0xF7FFFFFF → 0
  (write-combined alias; latent conflict with flash window — avoid in XGrid).
- **4 GB fastmem window** (`platform.hpp`): reserves whole 32-bit PA space; 128 MB RAM backed by
  a file mapping; mirror aliases mapped to the same backing; placeholder/no-access for the rest.
  Windows: `CreateFileMapping` + `VirtualAlloc2` placeholders + `MapViewOfFile3`. Unix: memfd +
  `mmap MAP_SHARED|MAP_FIXED`. NV2A 16 MB range committed RW for fast register polling.
- **MMIO dispatch**: flat `MmioRegion{base,size,read,write,user}` array (max 32), linear scan;
  unmapped reads = `0xFFFFFFFF` (BUS_FLOAT), writes dropped. I/O ports in `io_ports[128]`.
- Devices registered in `xbox_setup()`: RAM mirror, flash, NV2A, APU, IOAPIC, USB0, USB1,
  BIOS shadow.
- NV2A fastmem sync (`hw_tick_callback`) polls committed NV2A memory and mirrors USER PUT/GET,
  RAMFC, PRAMIN, CACHE1_REF, PCRTC into device state — fragile; do not copy.

---

## 4. Devices (flat register files, namespace `xbox`)

- **PCI** (`pci.hpp`): ports 0xCF8/0xCFC; 2 buses × 32 devices × 8 functions × 256 bytes. Device
  table: host 10DE:02A5, LPC 10DE:01B2, SMBus 10DE:01B4 (I/O BAR 0xC001, IRQ11), NV2A 10DE:02A0
  (BAR0 0xFD000000, IRQ3), APU 10DE:01B0 (0xFE800000, IRQ5), USB0 10DE:01C2 (0xFED00000, IRQ1),
  USB1 (IRQ9), IDE 10DE:01BC (BAR4 0xFF61, IRQ14), AGP bridge dev30 → NV2A on bus 1.
  Nonexistent devices read 0xFFFFFFFF.
- **PIC** (`pic.hpp`): dual 8259A master 0x20/0x21 slave 0xA0/0xA1, IRR/ISR/IMR, ICW1-4, OCW2
  EOI, OCW3, cascade IRQ2, auto-EOI.
- **PIT** (`pit.hpp`): ports 0x40-0x43, 3 channels, modes 0/2/3, latch; ch0→IRQ0.
- **IOAPIC** (`ioapic.hpp`): PA 0xFEC00000, IOREGSEL 0x00/IOWIN 0x10, 24 redirections, version
  0x00170011, entries default masked (bit16).
- **ACPI** (`acpi.hpp`): I/O 0x8000/0x8001 PM1a STS/EN, 0x8008 32-bit PM timer at 3.579545 MHz,
  0x80C0/0x80C1 GPE0 STS/EN.
- **CMOS** (`cmos.hpp`): ports 0x70/0x71, 128 regs, A=0x26, B=0x02 (24h BCD), D=0x80; periodic
  interrupt → IRQ8; reg C clears flags; ELCR 0x4D0/0x4D1.
- **misc I/O** (`misc_io.hpp`): 0x61 sysctl, 0x80 POST, 0xE9 debug console.
- **IDE** (`ide.hpp`): primary 0x1F0-0x1F7 + 0x3F6 (HDD), secondary 0x170-0x177 + 0x376 (DVD);
  bus-master DMA BAR4 0xFF60. Commands: IDENTIFY DEVICE 0xEC, IDENTIFY PACKET 0xA1, PACKET 0xA0,
  READ 0x20, WRITE 0x30, SET FEATURES 0xEF, INIT DEV PARAMS 0x91, FLUSH 0xE7; status 0x50/0x58/
  0x51, error ABRT 0x04. ATAPI: TEST UNIT READY 0x00, INQUIRY 0x12 ("XBOX DVD"), MODE SENSE 10
  0x5A; signature LBA_MID=0x14, LBA_HIGH=0xEB. DMA is register-level only (PRDT not walked).
- **SMBus** (`smbus.hpp`): ports 0xC000-0xC00F; STATUS 0x00 (W1C, bit4 done), CONTROL 0x02
  (protocol bits0-2, start bit3, int enable bit4), ADDRESS 0x04, DATA 0x06, COMMAND 0x08,
  FIFO 0x09; IRQ11. Addresses: SMC 0x10, EEPROM 0x54, TV encoder 0x45, temp 0x4C, Xcalibur 0x6A.
- **EEPROM defaults**: 0x2C region=1 (NTSC-NA), 0x34 serial "000000000000",
  0x40 MAC 00:50:F2:00:00:01, 0x4C audio stereo, 0x54 DVD region 1, 0x58 NTSC-M, 0xE8 English,
  0xFC checksum.
- **SMC commands**: VERSION 0x01→0xD0; TRAY_STATE 0x03→0x60; AV_PACK 0x04→0x07; FAN_SPEED 0x06;
  CPU_TEMP 0x09→40C; MB_TEMP 0x0A→35C; SCRATCH 0x0E; AV_TYPE 0x0F→0x05; challenge 0x1C-0x1F;
  writes FAN_MODE 0x05, LED 0x07/0x08, TRAY_EJECT 0x0C, RESET_ON_EJECT 0x19, POWER_OFF 0x02.
- **Flash** (`flash.hpp`): 1 MB at 0xF0000000 aliased at 0xFF000000; load 256 KB (mirrored 4×) or
  1 MB. CFI: READ_ARRAY 0xFF, READ_ID 0x90, READ_STATUS 0x70, CLEAR_STATUS 0x50, BYTE_PROGRAM
  0x40, ERASE_SETUP 0x20, ERASE_CONFIRM 0xD0; SR_READY 0x80, SR_ERASE_ERR 0x20, SR_PROG_ERR 0x10;
  SST IDs 0xBF/0x52. Byte program only clears bits; 4 KB sector erase.
- **MCPX ROM** (`flash.hpp`): 512 bytes at PA 0xFFFFFE00 served when `mcpx_loaded`.
- **USB** (`usb.hpp`): USB0 0xFED00000, USB1 0xFED08000; 64 dwords; HcRevision 0x0110,
  HcFmInterval 0x27782EDF, HcRhDescriptorA NDP=2. No transfers/XID in this snapshot.
- **APU** (`apu.hpp`): PA 0xFE800000, 4 MB; ISTS 0x1000 (W1C), IEN 0x1004, FECTL 0x1100,
  FECV 0x1110, FESTATE 0x1108, FESTATUS 0x110C, FETFORCE1 0x1504, SECTL 0x2000, XGSCNT 0x200C,
  VPVADDR 0x202C, GPSADDR 0x2040, GPISTS 0x3004, EPISTS 0x4004. No audio output.
- **NV2A** (`nv2a.hpp`): PMC BOOT_0 0x02A000A1; PBUS FBIO_RAM 0x218=3; PFIFO CACHE1_PUSH0
  0x1200, PUSH1 0x1204, STATUS 0x1214 (0x10 idle), DMA_PUSH 0x1220, DMA_STATE 0x1228,
  DMA_INSTANCE 0x122C, PUT/GET/REF 0x1240/44/48, RAMHT 0x0210, RAMFC 0x0214, RAMRO 0x0218;
  PVIDEO 0x008000; PTIMER 0x009000 (NUM 0x200, DEN 0x210, TIME_0 0x400, ALARM_0 0x420;
  800 ns/tick); PFB CFG0 0x200=0x03070103 (64 MB); PGRAPH 0x400000 (INTR 0x100, INTR_EN 0x140,
  CTX_STATUS 0x170, FIFO 0x720=1, REF_CNT 0xB10); PCRTC 0x600000 (START 0x800, CONFIG 0x804,
  RASTER 0x808, NTSC 525 lines); PRAMDAC 0x680000 (NVPLL 0x500=0x00011C01, MPLL 0x504=0x00011801,
  VPLL 0x508=0x00031801, FP_DEBUG0 0x600=0x101); PRAMIN 0x700000 (1 MB + guest backing at
  0x03FE0000); USER 0x800000 (0x40 PUT, 0x44 GET, 0x48 REF per 64 KB channel).
- **PFIFO pusher** (`tick_fifo`): resolves pushbuffer base from the DMA context object in PRAMIN
  (frame[31:12] | flags[31:20]<<12, masked to 128 MB); parses NOP/OLD_JUMP/JUMP/CALL/RETURN/
  INCREASING/NON_INCREASING; intercepts SET_REFERENCE 0x14, NV097_SET_REFERENCE 0x75C,
  SET_CONTEXT_DMA_NOTIFIES 0x60, NOTIFY 0x41, surfaces 0x80/0x82/0x83; dispatches others to the
  PGRAPH shadow and updates RAMFC GET.
- **PGRAPH** (`pgraph.hpp`): `regs[2048]` indexed by method/4; vs_program[136*4],
  vs_constants[192][4]; constants: BEGIN_END 0x17FC, DRAW_ARRAYS 0x1810, SURFACE_FORMAT 0x0208,
  PITCH 0x020C, COLOR_OFFSET 0x0210, ZETA_OFFSET 0x0214, CLIP_H/V 0x0200/0x0204, viewport
  0x0A20/0x0AF0, blend 0x0304-0x0350, depth 0x0354-0x035C, stencil 0x1D0C/0x0364-0x0378, cull
  0x039C/0x03A0/0x03A4, texture stage block 0x1B00+stage*64, transform program 0x1E9C/0x1EA0,
  constants 0x1EA4/0x0B80, combiner control 0x0288, CLEAR_SURFACE 0x1D94.
- **Vulkan renderer** (`gpu/nv2a_vk_renderer`): single host-visible persistently mapped
  VkDeviceMemory (~64.5 MB): Xbox RAM (the emulator's `ram` pointer IS this mapping), PGRAPH
  state, PFIFO control block, indirect draws, per-draw state snapshots, VS program+constants.
  `pushbuf_parse.comp.glsl` (local_size 1) parses pushbuffers on GPU, writes PGRAPH state,
  handles surface/VS/BEGIN_END/DRAW_ARRAYS/CLEAR_SURFACE, emits indirect draws + snapshots.
  `nv2a_draw.vert.glsl`: vertex fetch interpreter + NV2A VS ubershader (13 MAC + 8 ILU opcodes,
  12 temps, 16 inputs/outputs, 192 constants, address reg a0). `nv2a_draw.frag.glsl`: 8-stage
  register-combiner interpreter. `scanout.frag.glsl`: X8R8G8B8/A8R8G8B8/R5G6B5 unpack.
  Prototype quality; CPU shadow and GPU parser duplicate dispatch logic.

---

## 5. Boot modes and kernel strategy

`BootConfig`/`auto_boot` priority: **nboxkrnl → LLE BIOS → HLE**.

### 5.1 HLE mode
- Writes 379 stubs of 8 bytes: `MOV EAX, ordinal (B8 imm32); INT 0x20 (CD 20); RET (C3)` at
  `HLE_STUB_BASE=0x80000`, `KDATA_BASE=0x81000` (re-based above the XBE image, page-aligned).
- XBE load: parse `XBEH`; decode entry point and kernel thunk with XOR keys (retail EP
  `0xA8FC57AB`, debug `0x94859D4B`; retail KT `0x5B6D40B6`, debug `0xEFB1F152`); load sections
  (zero-fill + raw copy); TLS index 0; walk thunk table (bit31 = ordinal; data exports → KDATA
  field address, functions → stub).
- KDATA layout (0x1000): KeTickCount 0x000, XboxHardwareInfo 0x004, XboxKrnlVersion 0x00C,
  LaunchDataPage 0x014, XeImageFileName 0x018/0x020, HalDiskCachePartCount 0x060,
  KdDebuggerEnabled 0x0B0, KeInterruptTime 0x0B4, KeSystemTime 0x0C0, KeTimeIncrement 0x0CC,
  KiBugCheckData 0x0D0, HalBootSMCVideoMode 0x0E4, object types 0x188-0x2C8, keys 0x2E8-0x428,
  XePublicKeyData 0x428. Values: hardware flags 0x20 (internal USB hub), GPU rev 0xA1, MCP rev
  0xD4, kernel version 1.0.5838.1, time increment 10000 (1 ms in 100 ns), SMC video mode 1 (NTSC).
- `default_hle_handler` (~197 ordinal cases): memory (ExAllocatePool, MmAllocateContiguousMemory,
  MmClaimGpuInstanceMemory, NtAllocateVirtualMemory...), threads/sync (PsCreateSystemThread,
  KeInitializeEvent/Mutant/Semaphore/TimerEx, KeWaitFor*, Rtl*CriticalSection), timers/DPC
  (KeInitializeDpc, KeSetTimer, KeQueryPerformanceCounter 733,333,333 Hz), I/O (NtCreateFile/
  ReadFile/WriteFile/SetInformationFile/QueryInformationFile/QueryVolumeInformationFile/
  QueryFullAttributesFile/DeviceIoControlFile/Close), HAL (HalReadSMBusValue/Write,
  HalReadWritePCISpace, HalGetInterruptVector, HalReturnToFirmware, HalReadSMCTrayState),
  crypto (SHA-1, HMAC-SHA1, RC4; DES key parity only; signature verify stubbed TRUE).
- Mounts: D:=XBE dir, C:=Partition2, E:=Partition1/TDATA, T/Y/Z:=XBE dir, CDROM0.
- Cooperative threading: round-robin `ThreadSlot` contexts, pending DPCs, thread exit at
  EIP 0xFFFFFFFF or HalReturnToFirmware.
- Assessment: HLE is a behavioral fake; dies at GPU/display init. Use only as fallback.

### 5.2 nboxkrnl mode (replacement guest kernel — recommended long-term shape)
- Loads a native PE at VA 0x80400000 → PA 0x400000 (`KERNEL_VA_OFFSET=0x80000000`); page
  directory at PA 0xF000; PDE[0..31] identity-map 128 MB as 4 MB pages (`0xE3`);
  PDE[0x200..0x21F] mirror VA 0x80000000 → PA 0..128 MB; PDE[0x300] self-map (`0xF063`);
  CPU state `CR0=0x80000021`, `CR3=0xF000`, `CR4=0x610`; stack VA 0x80430000; CS=0x08,
  DS/SS/ES/GS=0x10, FS=0x18; IF disabled; EEPROM+certificate keys on stack.
- Host bridge I/O ports: 0x200 DBG_STR, 0x201 MACHINE_TYPE, 0x202 ABORT, 0x203/0x204
  CLOCK_INCREMENT_LOW/HIGH, 0x205 BOOT_TIME_MS, 0x206 IO_START, 0x207 IO_RETRY, 0x208 IO_QUERY,
  0x20A IO_CHECK_ENQUEUE, 0x20D XBE_PATH_LENGTH, 0x20E XBE_PATH_ADDR, 0x20F/0x210
  ACPI_TIME_LOW/HIGH (3.579545 MHz).
- Async file I/O: 44-byte `PackedRequest`, 36-byte `InfoBlockOc`; type word =
  RequestType[31:28] | DevType[27:23] | flags[22:3] | disposition[2:0]; REQ_OPEN/REMOVE/CLOSE/
  READ/WRITE; devices CDROM=0, Partition0..5=2..7; worker `std::jthread` with deque +
  completed_map. Path mapping `\Device\CdRom0` → XBE dir, `Partition0..5` → data/hdd dirs.
  Pass-through host filesystem, no FATX/XISO.
- KeyConfig: 32 bytes (16B EEPROM + 16B certificate); zero = unencrypted defaults.

### 5.3 LLE kernel mode
- Loads real `xboxkrnl.exe` PE at its preferred base, loads XBE, re-resolves each HLE stub thunk
  against the PE export directory, leaves HLE stubs for missing exports; starts at XBE entry.

### 5.4 LLE BIOS mode
- `load_bios`; optional MCPX ROM; RC4 key from key file or MCPX offset 0x1A5 (16 bytes); if BIOS
  looks pre-decrypted (contains "Microsoft" in first 24 KB) skip RC4; else copy 24 KB from flash
  to PA 0x90000 and RC4-decrypt. Can patch 2BL SHA-1 check at flash 0x3D7AC and inner key at
  0x3FF9C. Runs the reset vector via the mini real-mode interpreter; execution continues from the
  2BL. Incomplete but closest to a real boot path.

---

## 6. Test framework

- ~50 NASM `.asm` suites assembled to flat binaries; `harness.inc` macros: `ASSERT_EQ`,
  `ASSERT_EQ_MEM`, `ASSERT_NE`, `ASSERT_FLAGS`, `PASS` (`xor eax,eax; hlt`), `PUTC`/`PUTS`
  (port 0xE9). Convention: `[BITS 32]`, `[ORG 0x1000]`, EAX=0 + HLT = pass, EAX=N = assertion N
  failed; golden values embedded as immediates.
- `test_runner` modes: flat-bin (load PA 0x1000, stack 0x80000 with sentinel 0xFFFFFFFF, FS base
  0x70000), `--xbox` (real device stubs), `--bios`, `--kernel`, `--nboxkrnl`, auto-detect.
  Test-only I/O port 0xEB raises arbitrary PIC IRQs.
- Coverage: alu, memory, flow, fpu, sse, advanced, string, segment, indirect, privileged, misc,
  smc, interrupt, paging, xbox, hle, linking, pic, pit, nv2a_*, pcrtc, smbus, apu, ide, usb,
  mtrr, gdt_tss, flash, pfifo, ioapic, pci, pvideo, pbus, pramdac, ptimer_alarm, ide_dma,
  usb_ohci, apu_voice, div_error, mmio_alu, ud2.
- C++ unit tests: `test_basic` (5), `test_block_rw` (5), `test_pe_loader`, `test_pgraph`
  (58 assertions: 33-command push buffer → state checks), nboxkrnl suite.
- Value: the NASM harness pattern is architecture-neutral and portable; **write our own test
  files** (Xermu's are unlicensed) using the same conventions. The `test_pgraph` pattern is
  worth reproducing.

---

## 7. Adopt / avoid (lessons for XGrid)

### Adopt (concepts, re-implemented)
1. Explicit guest context with a documented, tested layout and named offsets.
2. Two-level O(1) opcode/mnemonic dispatch (table → class → handler).
3. Trace/block cache keyed on guest VA, two-level direct mapping, fixed object pool.
4. Forward-only block linking; backward edges re-enter the run loop for ticks/IRQs.
5. Page-protection SMC for self-modifying code (when a JIT exists).
6. Fastmem concept: flat guest-physical window with aliasing maps for mirrors.
7. Eager flags (natural for an interpreter); liveness gating only if JITting.
8. Software TLB (read/write split, direct-mapped) + page walk with A/D bits and #PF error codes.
9. Golden assembly CPU tests (write our own) and push-buffer state-snapshot tests.
10. Narrow, documented host bridge for a real guest kernel (I/O port protocol) with async file
    I/O worker.
11. XBE loader facts: XOR keys, section loading, kernel thunk patching, KDATA layout, KPCR setup.
12. Device constants: address map, PCI table, SMBus/EEPROM, IDE strings, APU/NV2A registers,
    PFIFO encoding, NV097 methods.
13. Vulkan strategy: precompiled interpreter ubershaders + per-draw state snapshots + indirect
    draws + scanout from guest RAM (no runtime shader compilation per state combination).

### Avoid
1. Copying source (unlicensed repo; GPL-adjacent reference shaders, nboxkrnl).
2. The verbatim-copy guided-executor model — impossible in managed C#; start with a table-driven
   interpreter.
3. Heuristic spin-loop fast-forward patching (mutating guest state from opcode scans; hardcoded
   diagnostic addresses).
4. Per-tick polling bridges for committed MMIO.
5. Monolithic header-only HLE with guessed stdcall arg counts; use declarative ordinal/signature
   metadata.
6. Global process-wide VEH / one-Executor limitation.
7. Platform asymmetry (Windows-only fault handling).
8. Unnamed magic constants and stale docs.
9. Native FPU/SSE execution without a managed fallback (determinism/portability).
10. Unbounded trace cache with global flush; plan per-block invalidation/recycling.
11. C-call-per-access VA translation when paging; inline the TLB if JITting.

### Highest-value constants (verified in Xermu)
- Address map (see §3), NV2A block offsets and defaults (PMC chip ID 0x02A000A1, PFB_CFG0
  0x03070103, PLL defaults 0x00011C01/0x00011801/0x00031801), PFIFO register set and command
  encodings, NV097 method numbers, PCI table, SMBus EEPROM/SMC bytes, IDE command/status values,
  PIT/PIC/IOAPIC/CMOS/ACPI semantics, XBE XOR keys and header/thunk layout, nboxkrnl page-table/
  CPU setup and host port protocol.
