# XGrid — Architecture Decisions from Reference Study

Project: XGrid (name replacing the plan's working name "XemuSharp").
Sources: `docs/research-notes/{xemu,cxbx,xqemu,xisosharp,sources}.md`.
This document converts the reference study into concrete decisions for the plan's phases.

**Note — "Xermu" citations below are unverified.** An earlier draft listed a standalone C#
emulator "Xermu" (`References2/Xermu-main`). It never existed in the workspace; the operator
confirmed 2026-10-13 the primary reference is `References2/xemu-0.8.137` (0.8.136 was the
primary snapshot until the 0.8.137 adoption on 2026-10-20). Mentions of Xermu
in this file are historical artifacts of that draft — do not treat them as facts or as a
pointer to any project. See `sources.md` "Xermu - WITHDRAWN".

---

## 1. Decision summary

| Area | Decision | Rationale |
|---|---|---|
| CPU backend | Managed table-driven interpreter first; JIT later behind the same abstractions | Verbatim-copy "guided executor" is impossible in managed C#; interpreter is portable/testable (xermu.md §7) |
| Memory | `AddressSpace` region table with flat RAM fast path + alias views; MMIO device registration | Mirrors xemu/xqemu region model without QEMU; Xermu's fastmem idea kept for later |
| Boot | LLE BIOS path primary (real MCPX ROM + BIOS); optional nboxkrnl-style guest-kernel path; HLE fallback with declarative ordinal table | Plan §54; xermu.md §5 |
| Kernel | Declarative ordinal/signature table derived from Cxbx `KernelThunk.cpp` semantics | Avoids Xermu's guessed-signature drift (xermu.md §7, cxbx.md §3) |
| Graphics | NV2A state shadow + renderer abstraction; Vulkan primary; precompiled interpreter ubershaders + per-draw state snapshots | xemu renderer split + Xermu GPU strategy |
| Storage | ATAPI DVD with 2048-byte sectors (guest parses XDVDFS); host tooling uses XISOSharp (MIT); FATX implemented clean-room | xemu/xqemu leave XDVDFS to the guest; XISOSharp MIT is reusable |
| XBE | Own loader from documented struct offsets | Cxbx/XISOSharp facts; XISOSharp's parser is private and image-bound |
| Testing | Own x86 golden test suite (NASM concept, own files), per-device register tests, push-buffer state tests | xermu.md §6; do not copy Xermu's unlicensed tests |
| Dependencies | XISOSharp (MIT) is the only approved third-party library | sources.md |

---

## 2. CPU (Phase 3/4/15/16)

- **Interpreter first**, structured like the plan's tiers. Recommended design:
  - One authoritative `XboxContext` (registers, EFLAGS, segment/descriptor state, CR/DR, FPU/MMX/
    SSE state) with a documented layout; unit tests assert offsets only if/when a JIT needs them.
  - Decoder: table-driven one-byte/two-byte opcode maps, ModRM/SIB, prefixes (segment, operand/
    address size, lock, rep), VEX not needed (P6/SSE1). No Zydis dependency in C#.
  - Instruction representation: compact `readonly struct`; dispatch via class tables, not
    per-instruction virtual calls (plan §31).
  - Eager flags for the interpreter; lazy-flag evaluation only if profiling demands.
  - Exceptions through a guest IDT model: #DE, #UD, #GP, #PF, #BP, #DB; never host exceptions.
- **CPU facts to implement** (xemu.md §2): PIII model 8/10; features PAE, SEP, MTRR, PGE, MCA,
  CMOV, PAT, PSE36, FXSR, SSE; CPUID leaf 1 EBX=0; leaf 2 `0x03020101/0/0/0x0C040841`; clamp
  unsupported leaves to level 2; MSR 0x2A = 0xC5040000. TSC behavior: xqemu uses a fixed
  733.333333 MHz TSC — treat as a configuration fact to verify against real BIOS behavior.
- **MMU** (plan §9): segmentation where required, paging (4 KB/4 MB), TLB, CR0/CR3/CR4, A/D
  bits, page-fault error codes, user/supervisor. Start with correctness; add soft TLB.
- **Segment/D-bit semantics (validated against the real MCPX ROM)**: default operand/address size
  must follow CS.D, not CR0.PE — the MCPX sets PE and then executes `66 EA` as a 32-bit far jump
  because the current CS is still a 16-bit segment. `LoadSegment` reads the GDT base and D bit
  (descriptor byte 6 bit 6) when protected, and `selector << 4` in real mode; CS loads update
  `XboxContext.CodeSegment32`. Far control transfers (`EA`/`9A`/`CB`/`CA`, `FF /3`/`FF /5`),
  `MOV Sreg` (`8C`/`8E`), and `PUSH/POP Sreg` (incl. `0F A0/A1/A8/A9`) are implemented.
- **JIT later** (plan §20): adopt Xermu's proven concepts — trace cache keyed on guest VA,
  forward-only block linking, page-protection SMC, per-block invalidation (not global flush) —
  but build the IR/emitter per plan §20.4/20.6 and benchmark-driven choice.
- **x87/MMX/SSE implementation decisions (Phase 3 complete)**:
  - `Fpu80`: true 80-bit extended storage (explicit integer bit, 15-bit exponent); loads/stores,
    constants, comparison, and integer conversion (`TryToInt16/32/64` with x87 rounding modes) are
    exact. Arithmetic and transcendentals round through IEEE double (documented limitation; a
    soft-float upgrade is a Phase 16 optimization candidate, not a correctness blocker).
  - `FpuState`: control/status/tag words, Top pointer, physical register file; tag classification
    (valid/zero/special/empty); FNSTENV/FLDENV (14/28-byte), FNSAVE/FRSTOR (94/108-byte).
  - MMX registers alias the low 64 bits of the x87 significand with exponent 0x403E; EMMS clears
    tags. SSE1 only (no SSE2): 66/F2-prefixed forms decode as #UD, matching PIII. XMM is
    8×128-bit; MXCSR default 0x1F80; FXSAVE/FXRSTOR use the 512-byte 32-bit layout (abridged FTW).
  - FWAIT does not yet deliver #MF for unmasked x87 exceptions (all masked by default FCW 0x037F);
    revisit if a title unmasks exceptions.

## 3. Memory (Phase 2)

- `PhysicalMemory`: flat 64/128 MB `byte[]` with `Read8..64/Write8..64`, little-endian helpers,
  `Span<byte>` access; no host-endian assumptions.
- `AddressSpace`: ordered region table (base, size, read, write, priority) with overlap for
  BIOS mirrors and MCPX ROM overlay; fast path for RAM (range check → direct array).
- **Address map to implement** (consolidated; xemu/xqemu/Xermu agree):
  - RAM 0x00000000 (64 MB retail / 128 MB devkit); mirror 0x0C000000 (NV2A tiling alias);
    contiguous alias 0x80000000 (kernel physical map); write-combined alias 0xF0000000.
  - Flash 0xF0000000 (1 MB) mirrored at 0xFF000000 (16 MB); MCPX ROM 512 bytes at 0xFFFFFE00.
  - PCI hole alias `[ram_size, 0x1_0000_0000)`.
  - NV2A 0xFD000000 (16 MB), APU 0xFE800000 (512 KB), ACI 0xFEC00000 (4 KB),
    USB0 0xFED00000, USB1 0xFED08000.
- MMIO interface per plan §7.4; invalid accesses deterministic and logged.

## 4. Xbox bus / interrupts (Phase 5)

- PCI: config ports 0xCF8/0xCFC; device table and IDs per xemu.md §1; BAR allocation.
- Interrupts: dual 8259 PIC only (no IOAPIC on Xbox; Xermu's IOAPIC is a devkit-era artifact —
  keep optional). LPC routing registers 0x64/0x68/0x6C with canonical values
  `INT_IRQ_ROUT=0x0E065491`, `PIRQ_ROUT=0x00031000` (xemu.md §3).
- Timers: PIT 8254, ACPI PM timer 3.579545 MHz, RTC MC146818 base 2000 (with xqemu's
  0x55/0xAA pattern note), PTIMER on NV2A.
- Device registration: `IXboxDevice { Name; Reset(); }` + `IMmioDevice`, no central switch.
- **Implemented (Phase 5 core)**: `IoSpace` (dynamic 64 KiB windows, last-wins, open-bus 0xFF),
  CPU IN/OUT via `IIoBus`; `PciBus` standard config address (register field = bits 7-2, byte offset
  = `address & 0xFC`), BAR probe/assign, command register; `LpcController` implements the exact
  xemu routing model: INT routing nibble at `0x6C >> (index*4)`, PIRQ bytes at `0x68+ (index-8)`,
  slot map 0→5, 1→7, 2→0, 3→1, 4→2, 5→3, 6→4, 9→6, 30→8+2 (PIRQ C); ROM overlay disabled on
  config write `0x80` bit 1 (re-enabled on reset); PM1 status/enable and 24-bit PM timer in the
  BAR0 I/O window (+0x00/+0x02/+0x08). `Pic8259` models slave INT→master IR2 cascade in software;
  `Pit8254` counts PIT input ticks directly (mode 0 one-shot, modes 2/3 rate generator); `XboxBus`
  converts CPU cycles (733.333 MHz) to PIT/PM ticks. CPU interrupt injection is IF-gated and
  destructive only when an interrupt is actually dispatched.

## 5. MCPX (Phase 6)

- **CPU fixes required by the real boot path** (all tested): CR0/CR3/CR4 writes flush the MMU/TLB;
  4 MB pages are never cached in the 4 KB software TLB (the TLB hit path only reconstructs 4 KB
  offsets); GDT/IDT/descriptor reads translate through paging (the 1BL maps its IDT at a linear
  address backed by a 4 MB page); absent IDT gates fail cleanly instead of jumping to a null
  selector; WBINVD/INVD, ARPL, BSWAP, LEAVE, ENTER, and the 0F 00 system group
  (SLDT/STR/LLDT/LTR/VERR/VERW) implemented; `FF /6` PUSH r/m32 added to Group 5; `8F /0` POP r/m32
  (Group 1A, incl. FS override — the 1BL's SEH unwind uses `POP FS:[0]`).
- **2BL running (milestone)**: after the 1BL's self-copy and page-table setup, the 2BL (compiled
  runtime) executes at virtual 0x80000000+ (physical 0x0xxxx through PDE[0x200]=0x000000E3, 4 MB
  page). Observed 2BL features: interrupt entry stub at 0x8001B226 ending in IRET, SEH frames via
  `FS:[0]` (`POP FS:[0]` unwind), fatal-error thunk `0x8001C785` (`MOV ESP,[EBP-0x18]; PUSH 0x78;
  CALL 0x800194B0`). The 2BL's dispatch call at 0x8002E6EB (`CALL [EDI+ECX*4+8]`, EDI=0x80010CD8,
  index 0, table entries `{0xFFFFFFFF, thunkA, thunkB, 0}`) stores 0xFFFFFFFF ("not found") and
  calls the throwing stub → fatal error 0x78. Next: identify the descriptor/object and the missing
  device.
- **2BL state-machine object (open)**: object at 0x800395D8 is `{+0=0xFFFFFFFF, +4=run fn
  0x8002E668, +8=table 0x80010CD8, +0xC=index (0, set to 0xFFFFFFFF on entry), +0x10=0,
  +0x14=0x800557A1}`. `run` reads index (ESI) and table, calls `table[i].prepare` (entry 0 =
  0x8001C781 = `XOR EAX,EAX; INC EAX; RET` → returns 1), then `table[i].process` (entry 0 =
  0x8001C785 = fatal 0x78 stub), and sets index = `table[i].next` (entry 0 = 0xFFFFFFFF = done).
  Table entries are 16 bytes `{next/0xFFFFFFFF, prepare, process, flags}`; entry 1 =
  `{0xFFFFFFFF, 0x8001CC5F, 0x8001CC63, 0xFF0000FF}`. The index should be set to a valid state by a
  device probe before `run`; it is 0 (the terminal/failed entry) in our boot.
- **Entry semantics resolved**: entry 0's prepare AND entry 1's prepare are both `XOR EAX,EAX; INC
  EAX; RET` (return 1), but entry 0's process (0x8001C785) is a not-implemented stub while entry 1's
  process (0x8001CC63: `MOV ESP,[EBP-0x18]; OR [EBP-4],-1; MOV EAX,[EBP-0x1C]; CALL 0x8002DA95;
  RET 4`) is real code. This is MSVC-style **virtual dispatch with a not-implemented/pure slot**:
  the object's class (vtable 0x80010CD8) does not implement method 0, so the call lands on the
  stub → fatal 0x78. The 2BL is compiled C++ with SEH exception frames (registries at 0x800391C0+,
  0x80038E40+ are exception/object lists referencing 0x800395D8). Next: find the constructor that
  installs the correct vtable/class for the object in the device/object registry, i.e. which
  hardware probe should have produced a derived class implementing method 0.
- **Vtable installer found**: static initializer at 0x8001C65D does `PUSH 8; PUSH 0x80010CD8;
  CALL 0x8002DA5C` (register the class/vtable, size 8) and initializes globals at 0x80035C2C+
  (`MOV [0x80035C30], 0x80035C2C; MOV [0x80035C2C], 0x80035C2C; MOV [0x80035C34], 0`), i.e. a
  global object/singleton list. The object 0x800395D8 is a singleton of this class; the failing
  call is method 0 (the pure/not-implemented slot). Nearby code: 0x8001C62A-0x8001C65C is a
  word-swap loop over a table at 0x80035E60..0x80036060 (stride 8) — likely a global object/class
  table being byte-swapped at init. Next: dump the initializer body (0x8001C67A+) and trace its
  caller to identify the class and which hardware-derived subclass should have been installed.
- **Initializer body (0x8001C669+)**: self-links the list head 0x80035C2C (empty list), calls
  0x80014368 / 0x8001CD34, initializes list/critical-section structs (0x8003A840/0x8003A860 via
  0x8001AD0B, counters 0x8003A874=0x7F, 0x8003A854=0x3C), creates a task via 0x80018E4B with
  (obj 0x80035A90, entry 0x80039840, stack 0x3000, zeros), then zeroes a 0x210-byte block at
  `[0x80035AAC]-0x210` (FXSAVE-style state: word[0]=0x027F x87 CW, dword[+0x18]=0x1F80 MXCSR) and
  links 0x80035A90 into the list at 0x80035C04/0x80035C0C. So the vtable-0x80010CD8 class is a
  **task/thread-like object** (FPU/SSE state + list links); the failing method 0 is its run/entry
  slot, left as the not-implemented stub because the task entry path differs from real hardware.
- **Implemented (SMBus core)**: `Amd756Smbus` (PCI 1:1.0, BAR1 I/O) reproduces xemu's exact
  register/status model: successful transaction sets `GS_HCYC_STS` (0x10), failures set
  `GS_PRERR_STS` (0x04), abort sets `GS_ABRT_STS` (0x01); GLOBAL_STATUS writes clear (0x37 mask)
  or set HCYC; GLOBAL_ENABLE bit 3 triggers the transaction with protocol = bits 0-2 and read =
  HOST_ADDRESS bit 0; block read returns the length in HOST_DATA. Slaves: `Eeprom24c02` (0x54,
  byte/word/block, pointer auto-increment), `SmcDevice` (0x10: version "P01" with index, tray,
  AV pack, error, interrupt status clear-on-read, scratch, challenge 0x1C-0x1F = 52/72/EA/46),
  `ConexantCx25871` (0x45, byte register file). The MCPX boot script polls GLOBAL_STATUS for
  exactly 0x10 after each transaction, so status fidelity is boot-critical.

Implement in xemu's staged order: SMBus → EEPROM → Flash → SMC → USB integration → interrupts →
timers. Facts:
- AMD756 SMBus register map (xemu.md §4.1); device addresses SMC 0x10, EEPROM 0x54, CX25871
  0x45, ADM1032 0x4C, FS454 0x6A, Xcalibur 0x70.
- SMC register/challenge map (xemu.md §4.2) — required for the kernel handshake.
- EEPROM layout + SHA1/RC4 security-section crypto (xemu.md §4.3) — implement generation and
  parsing; never ship copyrighted EEPROMs.
- Flash: model AMD/SST command set (Xermu CFI commands; xemu.md §4.7) rather than flat ROM.
- ACPI PM/GPE/GPIO (xemu.md §4.5); LPC47M157 Super I/O programming model (xemu.md §4.6).
- ACI AC'97 link (xemu.md §4.9) for capture; APU EP normally feeds playback.

## 6. NV2A (Phase 11, 18) — Track B

- Register block map and defaults (xemu.md §5.2, cxbx.md §4.1) are consistent across references:
  PMC 0x000000, PBUS 0x001000, PFIFO 0x002000, PVIDEO 0x008000, PTIMER 0x009000, PFB 0x100000,
  PGRAPH 0x400000, PCRTC 0x600000, PRAMDAC 0x680000, PRAMIN 0x700000, USER 0x800000.
  PMC BOOT_0 = 0x02A000A3 (xemu); PFB CFG0 = 0x03070103.
- PFIFO: channels, pushbuffer encoding, DMA objects, RAMHT, method dispatch (xemu.md §5.4,
  cxbx.md §4.2). Keep GPU command generation logically synchronized with the CPU (plan §41).
- PGRAPH: state shadow (`regs[0x2000]` + dirty tracking), classes (0x44/0x62/0x97/0x9F), NV097
  method groups (cxbx.md §4.3), surfaces/textures/vertex state. No Vulkan types in emulation.
- Renderer: `INv2aRenderer` intermediate representation carrying `ShaderState`
  {VshState, GeomState, PshState} (xemu.md §5.6). Vulkan primary (plan §16.6); OpenGL optional.
- Shaders: **precompiled interpreter ubershaders** — vertex fetch + NV2A VS interpreter (MAC/ILU
  opcodes) and 8-stage register-combiner interpreter — driven by per-draw state snapshots and
  indirect draws (Xermu GPU strategy; cxbx.md §4.4). Avoid per-state runtime shader compilation.
- Texture swizzle Morton algorithm (xemu.md §5.8); DXT1/3/5; paletted conversion on upload.
- Display: PCRTC START, VBLANK IRQ, PRAMDAC PLLs, VGA core integration, scanout from guest RAM.

## 7. APU (Phase 12)

- Register map and constants (xemu.md §6). DSP56300 semantics are the hard part; the xemu/xqemu
  DSP interpreters are GPL (Hatari-derived) — implement independently from documented DSP56300
  behavior, with the register/peripheral maps as facts.
- Architecture: VP (voice mixing → 32 float mixbins) → GP DSP → EP DSP every 8th frame →
  monitor 48 kHz stereo S16 (256 frames/5.33 ms).
- Host output behind `IAudioBackend` (plan §17); long-run drift test required.

## 8. Storage / XBE / kernel (Phases 8–10)

- IDE/ATA: primary HDD, secondary ATAPI DVD; commands per Xermu/xemu facts. Consider modeling
  the NVIDIA nForce IDE rather than PIIX3 for accuracy (xemu uses PIIX3 as a stand-in).
- DVD: 2048-byte sectors; guest kernel parses XDVDFS; provide raw ATAPI media. For host-side
  image handling and tooling, integrate **XISOSharp** (MIT) — see xisosharp.md.
- FATX: **no reference implementation in the workspace**. Implement clean-room (superblock facts:
  signature `XTAF`/`0x58544146`, cluster size, FAT copies, root cluster; partition table facts in
  cxbx.md §6). Read-only first (plan §13.4).
- XBE loader (Phase 9): implement from documented offsets (cxbx.md §2); parse header, cert,
  sections, TLS, entry point/image base (XOR keys per type); map sections into emulated memory;
  `XemuSharp.XbeTool`-equivalent CLI (`XGrid.XbeTool`).
- Kernel (Phase 10): declarative ordinal table with explicit name/arg count/calling convention/
  data-vs-function metadata, derived from Cxbx semantics (cxbx.md §3) and verified against a real
  kernel. Trace-driven implementation (plan §15 strategy). Keep HLE as a fallback; prefer real
  BIOS/nboxkrnl-style boot (xermu.md §5).

## 9. USB / input (Phase 13)

- OHCI controller (register/ED/TD layouts in cxbx.md §5) + internal hub + XID gamepad.
- XID descriptors/report formats/rumble (xemu.md §8, cxbx.md §5). Four controllers.
- Host input behind `IInputBackend`.

## 10. Networking (Phase 14)

- nvnet register/descriptor map (xemu.md §9) behind `INetworkBackend`; defer until core gameplay.

## 11. Testing strategy (Phases 20/25/26/44)

- **CPU golden tests**: write our own x86 test suite using the NASM harness conventions
  (EAX=0 + HLT = pass, EAX=N = fail, port 0xE9 console) — do not copy Xermu's unlicensed tests.
  Include the plan's JSON test format for higher-level instruction tests.
- **Device tests**: per-device register/state tests modeled on Xermu's per-device `.asm` concept
  (PIC/PIT/SMBus/IDE/APU/NV2A timers etc.).
- **Push-buffer state tests**: construct command streams, feed PFIFO/PGRAPH, assert state.
- **Deterministic mode** (`--deterministic`) per plan §39; golden traces per plan §25.5.
- **Differential testing** (plan §26): xemu as reference where legal; never assume xemu is right.
- **Fuzzing** (plan §44): XBE, XDVDFS, FATX, ISO, NV2A command streams, x86 decoder.

## 12. Licensing / clean-room process

- Follow `docs/research-notes/sources.md`: facts extracted, no code copied from xemu (GPL/LGPL),
  Xermu (unlicensed), Cxbx (GPL), xqemu (GPL).
- XISOSharp (MIT) is the only approved library dependency; preserve MIT + extract-xiso advertising
  notice if any source is adapted. Record the exact package version when integrated.
- Maintain `sources.md` when new references are studied (plan §46).
- Never commit copyrighted firmware, EEPROMs, BIOS images, game data, or test fixtures derived
  from them.

## 13. Consolidated key constants appendix

### Physical address map
```
RAM              0x00000000  64/128 MB
RAM mirror       0x0C000000  (NV2A tiling alias)
Contiguous alias 0x80000000  -> PA 0
WC alias         0xF0000000  -> PA 0
Flash            0xF0000000  1 MB, mirrored at 0xFF000000 (16 MB)
MCPX ROM         0xFFFFFE00  512 bytes (overlay, disabled by LPC cfg 0x80 bit1)
PCI hole alias   [ram_size, 0x1_0000_0000)
NV2A             0xFD000000  16 MB
APU              0xFE800000  512 KB (VP 0x20000, GP 0x30000, EP 0x50000)
ACI (AC'97)      0xFEC00000  4 KB (PCI 0:6.0 BAR2; no I/O APIC on Xbox)
USB0 / USB1      0xFED00000 / 0xFED08000  4 KB
```

### PCI IDs
```
Host bridge   10DE:02A5 A1        LPC/ISA       10DE:01B2 B2
SMBus         10DE:01B4 B1        USB0/USB1     10DE:01C2 B1
NIC (nvnet)   10DE:01C3 B1        APU           10DE:01B0 B1
ACI (AC'97)   10DE:01B1 B1        IDE           10DE:01BC B1
NV2A          10DE:02A0 A1        AGP bridge    10DE:01B7 A1
```

### IRQ routing
```
INT_IRQ_ROUT 0x0E065491 -> USB0=1, USB1=9, NIC=4, APU=5, ACI=6, IDE=14
PIRQ_ROUT    0x00031000 -> AGP=IRQ3
```

### NV2A blocks
```
PMC 0x000000  PBUS 0x001000  PFIFO 0x002000  PRMA 0x007000  PVIDEO 0x008000
PTIMER 0x009000  PCOUNTER 0x00A000  PVPE 0x00B000  PTV 0x00D000  PRMFB 0x0A0000
PRMVIO 0x0C0000  PFB 0x100000  PSTRAPS 0x101000  PGRAPH 0x400000  PCRTC 0x600000
PRMCIO 0x601000  PRAMDAC 0x680000  PRMDIO 0x681000  PRAMIN 0x700000 (1 MB)
USER 0x800000 (0x800000)
```
Defaults: PMC BOOT_0 0x02A000A3; PFB CFG0 0x03070103; PLLs 0x00011C01 / 0x00011801 / 0x00031801;
crystal 16,666,666 Hz.

### MCPX
```
SMBus ports 0xC000-0xC00F (AMD756 regs at +0xE0, 16 bytes)
SMC 0x10, EEPROM 0x54 (256 bytes), CX25871 0x45, ADM1032 0x4C, FS454 0x6A, Xcalibur 0x70
SMC challenge: 0x1C->0x52, 0x1D->0x72, 0x1E->0xEA, 0x1F->0x46
ACPI: PM1 0x8000/0x8001, PM timer 0x8008 (3.579545 MHz), GPE 0x80C0/0x80C1
RTC 0x70/0x71, IRQ8, base year 2000
```

### XBE
```
Magic "XBEH"; base 0x00010000; header 0x178; cert size 0x1D0; section header 0x38
EP XOR: retail 0xA8FC57AB, debug 0x94859D4B, chihiro 0x40B5C16E
KT XOR: retail 0x5B6D40B6, debug 0xEFB1F152, chihiro 0x2290059D
```

### XDVDFS
```
Descriptor sector 32 (0x10000); magic MICROSOFT*XBOX*MEDIA (0x00 and 0x7F4)
Entry: left u16, right u16, start sector u32, size u32, attrs u8, namelen u8, name
Sentinels: 0xFFFF end, 0x0000 empty dir; optimized tag "in!xiso" at 31337
FileModulus 0x10000; pad 0xFF; disc offsets 0xFD90000/0x2080000/0x89D80000/0x18300000
```

### XID
```
VID 0x045E, PID 0x0202 (S 0x0289); class 0x58 subclass 0x42; XID descriptor type 0x42
Input report 20 bytes (buttons u16, analog[8], thumbs int16 x4)
Output report 6 bytes (left/right actuator u16)
```

## 14. Open questions / decisions to confirm

1. **CPU TSC model**: real Coppermine TSC vs xqemu's fixed 733.333 MHz — verify against BIOS.
2. **Boot target order**: BIOS LLE vs nboxkrnl-style guest kernel first; plan §54 says BIOS first.
3. **IDE model**: NVIDIA nForce register-accurate vs PIIX3-compatible stand-in.
4. **JIT code generation strategy** (plan §20.6): Expression/IL vs DynamicMethod vs machine-code
   emitter — benchmark-driven after interpreter exists.
5. **XISOSharp consumption**: NuGet vs project reference; ZArchiveSharp dependency acceptable?
6. **FATX scope**: read-only first (plan §13.4); allocation/write later.
7. **Determinism baseline**: single-thread emulation, host services separated (plan §40).

## 15. Real-BIOS boot progress (MCPX -> 1BL -> 2BL)

- The 2BL's unhandled-exception path parks an `EXCEPTION_RECORD` on the thread stack
  (`{code, flags, record, address, params}`); reading it after a panic identifies the exact
  faulting instruction. Panics observed and fixed, in order: `OUTSB` (`6E`), `XADD`
  (`0F C1`), `BSR` (`0F BD`), `BTR` (`0F B3`). Implemented INS/OUTS (0x6C-0x6F) with REP,
  XADD/CMPXCHG (0F C0/B0/B1/C1), BSF/BSR (0F BC/BD), BT/BTS/BTR/BTC (0F A3/AB/B3/BB).
- The 2BL uses a `VC20XC00`-tagged scope-table exception dispatcher (`0x8002E668`) registered
  via an MSVC `__SEH_prolog4`-style helper (`0x8002DA5C`); scope records are 12 bytes
  `{enclosingLevel, filter, handler}` and the terminate record panics with code 0x78.
- A second dispatcher walks the SEH chain via TEB fields FS:[0]/[4]/[8] and falls back to a
  global handler at `[0x8003A8DC]`, panicking with the record's fields on failure.
- `XboxBus.Advance(cycles)` (PIT + PM timer) existed but was never called; `XboxMachine.Run`
  now executes in 8192-instruction batches and advances the bus, so IRQ0 can fire during long
  runs. With that, the 2BL reaches its scheduler/idle loop (`0x8001B02E`: `STI; NOP; NOP; CLI;
  CMP [EBP]; JZ; CMP [EBX+0x2C]; JZ`) and runs 300M instructions without panic.
- Idle loop waits on a task list at `0x80035C2C` and a flag at `0x80035C08`; which interrupt or
  device posts these (PIT vs RTC vs IDE/NV2A) is the next investigation. IDT vector 8 handler is
  `0x8001B680`; no IRQ0 handler hits were observed in a 200K-instruction window (PIT divisor
  65536 gives ~40M-instruction periods, so this is inconclusive).
- PIC diagnostics (now printed by the CLI): after 300M instructions the master is remapped to
  **base 0x30** (`mask=0xFA` so IRQ0 unmasked, `req=0`, `isr=0`). IDT gate 0x30 = `0x8001B180`
  (a trap-gate stub that saves context, pushes `[0x80035BDC]`, sets it to -1, and calls the
  common interrupt dispatcher `0x8001B9F0`). IDT gates 0x20-0x2F are absent; gates 0x30/0x31/0x33
  are software-interrupt APIs.
- PIT channel 0 **is** programmed (divisor=1125, mode 2, ~1060 Hz) by a boot stage not visible
  as `OUT 0x40-0x43` in the RAM dumps (likely a DX-based or table-driven write); the BIOS image
  on disk is encrypted so file-offset pattern searches are meaningless. The CLI now prints PIC
  base/mask/request/in-service, PIT ch0 divisor/mode, and the CPU's delivered-interrupt count.
- **388 interrupts are delivered over 300M instructions** (matches the ~691K-instruction IRQ0
  period from ~25M onward), so timer delivery and PIC EOI work. The scheduler idle loop is
  therefore waiting on a **device event** (most likely IDE/HDD — Phase 8 storage, which is not
  implemented yet), not the timer.
- Phase 8 foundation added (2026-10): `IDiskImage`/`RawDiskImage`/`Qcow2DiskImage` (read-only
  v2/v3), `AtaDevice` (PIO taskfile), `IdeController` (PIIX3 at PCI dev 9, ports 0x1F0/0x3F6,
  IRQ14 through `LpcController.RoutingIndexForSlot(9)=6`). `xgrid boot --hdd <path>` attaches a
  raw or qcow2 image. Attaching the real qcow2 HDD does **not** change the idle-loop state
  (same EIP/registers at 100M instructions), and the taskfile diagnostic reports
  **`IDE accesses=0`** — the 2BL never touches the IDE controller. The blocking wait is
  therefore a different device (RTC/USB/NV2A/APU) or a scheduling/DPC issue: the vector-0x30
  timer stub (`0x8001B180`, a trap gate) runs 100+ times, but no ready task ever appears in the
  idle loop's list at `0x80035C2C`/flag `0x80035C08`. Next: decode the common dispatcher
  `0x8001B9F0` (how it selects the vector/EOI and posts DPCs) and check whether the timer
  handler's wakeups land in the list the idle loop polls.
- Dispatcher `0x8001B9F0` decoded: it calls the C handler at `0x8001CA64` (the SEH-walking
  exception dispatcher seen in earlier panic traces) and then restores the interrupted context
  through the `0x8001B226` trampoline; a nonzero handler result takes an alternate stub path.
  Since 106+ IRQs are delivered across 100M instructions, the handler runs and EOIs the PIC —
  the missing piece is what it signals. All 2BL threads appear blocked with no I/O in flight
  (IDE accesses=0), so the wake source is likely a PCI/device interrupt we never raise
  (NV2A/USB/APU/RTC) or a DPC posted to a different list than the idle loop polls.
- MC146818 RTC added (ports 0x70/0x71, periodic IRQ8, BCD time from a fixed 2000-01-01 base;
  `XboxBus.Advance` ticks it at 32.768 kHz, `XboxMachine.Run` batches raised to 65536 so RTC/PIT
  ticks accumulate). The 2BL's slave PIC mask is `0xF6` (IRQ8 and IRQ11 unmasked), confirming it
  expects RTC interrupts, but the idle-loop state is unchanged and delivered interrupts stay at
  PIT levels — the RTC is **ruled out**: diagnostics show `RTC accesses=16, pie=False` (the 2BL
  reads the clock but never enables the periodic interrupt). IDE accesses remain 0. The wake
  source is therefore almost certainly a **PCI device interrupt we never raise** (NV2A, USB OHCI,
  APU) or a DPC posted by such a handler. Next: decode which wait object the boot thread blocks
  on, or register minimal NV2A/USB/APU PCI devices with interrupt capability.

## 16. Xbox PCI topology (from xemu `hw/xbox/xbox.c`) and reference-first process

Process directive: consult the reference projects first for hardware specs and behavior.
Xemu (`References2/xemu-0.8.137`) is the LLE reference for MCPX/PCI/IDE/NV2A; Cxbx-Reloaded and
Xermu are HLE (kernel reimplementation, no NV2A emulation) and only inform kernel-facing
behavior. Facts are extracted, never copied.

| Slot | Device (vendor:device, rev) | xemu source | XGrid status |
| --- | --- | --- | --- |
| 0:0.0 | Xbox PCI host bridge (10DE:02A5 `PCI_DEVICE_ID_NVIDIA_XBOX_PCHB`, rev A1, multi-function) | `xbox_pci.c:185,525` | done |
| 0:0.3 | Memory-controller stub (QEMU `pci-testdev` placeholder, "FIXME" in xemu) | `xbox.c:340` | not emulated |
| 0:1.0 | MCPX LPC (10DE:01B2, rev B2, multi-function) | `xbox_pci.c:205,444` | done |
| 0:1.1 | nForce SMBus (10DE:01B4, rev B1) | `xbox_pci.c:221,298` | done |
| 0:2.0, 0:3.0 | nForce OHCI USB (10DE:01C2, rev B1, class 0C0310, 256-byte BAR0) | `xbox.c:317,321`, `hcd-ohci-pci.c:136-144`, `hcd-ohci.c:1943` | config space only |
| 0:4.0 | nvnet Ethernet (10DE:01C3, rev B1, MMIO 0x400, IO 0x8) | `xbox.c:326`, `nvnet.c:910-916,1061` | config space only |
| 0:5.0 | MCPX APU (10DE:01B0, rev B1, MMIO 0x80000) | `xbox.c:331`, `apu.c:384-399,558` | config space only |
| 0:6.0 | MCPX ACI (10DE:01B1, rev B1, BAR2 0x1000) | `xbox.c:334`, `aci.c:48-67,87` | config space only |
| 0:9.0 | Xbox IDE (10DE:01BC, rev B1; PIIX3 class with `#ifdef XBOX` IDs) | `xbox.c:294`, `piix.c:190-193` | done (XGrid.Storage) |
| 0:30.0 | AGP bridge (10DE:01B7, rev A1); secondary bus carries NV2A | `xbox_pci.c:230,484-486` | done |
| bus 1, 0:0.0 | NV2A (10DE:02A0, rev A1, BAR0 16 MB, BAR1 64 MB prefetchable alias of RAM) | `xbox.c:337`, `nv2a.c:214-216,344,577-580` | config space only |
| 0:1.1 / 0:1.0 | MCPX SMBus / LPC | `xbox_pci.c` | done |

Bus routing: configuration cycles to bus N are forwarded only to the bridge whose programmed
secondary bus number (register 0x19) equals N, so unclaimed buses return all-ones instead of
aliasing bus-0 devices. Host bridge and LPC set the multi-function bit (header 0x0E bit 7), which
`PciDevice.Reset()` now preserves.

Result: with the stubs in place the 2BL still idles at the same loop (100M instructions, IDE
accesses 0, interrupts 108). Port histogram: 2BL enumerates PCI (0xCF8/0xCFC 1093 accesses), SMBus
(likely 0xC0xx), PM/GPIO (0x80xx), PIC/PIT/RTC, Super I/O config (0x2E/0x2F); it never reads the
IDE ports or the PM timer. Missing PCI functions are therefore not the wake source.

SMC comparison (`smbus_xbox_smc.c` vs `SmcDevice`): full register parity (tray/AV/error/INTSTATUS
clear-on-read/challenge bytes/handshake-write ignores). Deltas: POWER reg (0x02) is a no-op in
ours; xemu raises TRAYCLOSED+SCI at realize, ours starts intstatus 0 (IRQ9 masked by 2BL anyway).

Per-slot PCI histogram at 100M (bus+devfn: count): host 0000=10, memctl 0003=5 (all-ones),
LPC 0008=20, SMBus 0009=3, USB0 0010=139, USB1 0018=139, nvnet 0020=128, APU 0028=128,
ACI 0030=130, IDE 0048=128, AGP 00F0=134, NV2A on bus 1: 0100=133. The 2BL programs the AGP
secondary bus itself and sees every device. Unmapped-phys histogram: FD0=28, FD1=2 (one-time
NV2A probes, no polling), 045=4, 065=8 (likely memory-size probes). Conclusion: enumeration
completes; the 2BL waits for a runtime event.

SMC transaction log at 100M (`SMC cmds:` in CLI): R01=1, R1C=1, R1D=1, R1E=1, R1F=1, R04=1,
W01=2, W12=1, W13=1, W20=1, W21=1. The challenge handshake completes (reads 0x1C-0x1F get our
non-zero bytes, writes 0x20/0x21 ignored as in xemu); version and AV pack are read once. No
tray-state (0x03), error (0x0F), or INTSTATUS (0x11) reads, so the 2BL is not waiting on the SMC.

PCI state at 100M (`PCI state:` in CLI): USB0/USB1/nvnet/APU/NV2A cmd=0007 with BARs assigned
(FED00000/FED08000/FEF00000/FE800000/FD000000); ACI BAR0 reads back the 2BL's own IO probe
(0xD001) because xemu registers no BAR0/1 there. IDE cmd=0005 legacy with unassigned BARs, but
ports 0x1F0 never touched. LPC introute/pirq still defaults, so all device IRQs are masked.
Active drivers + no MMIO readback + no interrupts + parked thread = wait sits before storage
init. Next: trace the timer ISR path (0x8001B180 -> 0x8001B9F0 -> 0x8001CA64) per tick.

## 17. Timer ISR path and single-thread finding (1.5M-instruction trace + dumps)

Timer ISR correction: the running ISR is entered directly at **0x80030E4C**, not 0x8001B180
(0 hits in 1.5M traced instructions). Per-tick flow: EOI helper 0x80014F10 first
(`OUT 0x20,0x60` specific-EOI IRQ0, gated on byte [0x80035C00]), +0x2710 (1 ms) into 64-bit
counters at 0x8003A880/90, 32-entry ring at 0x8003A900, tick count [0x80035C14] (=108 at 100M,
matches InterruptCount), current-thread KernelTime++ ([EBX+0x18]) and Quantum-=3 ([EBX+0x6C]),
compare with 0x80035A90, restore [0x80035BDC], IRET. The DPC/timer-expiry branch
(JNZ at 0x800143DF) never fires: nothing is ever queued or expires.

Thread decode via `--dump` (Cxbx `types.h` KTHREAD/KPRCB as reference, 2BL layout differs slightly):
only one thread exists, 0x80035A90: Header.Type=6 (Thread), MutantListHead empty, KernelTime=0x6B
(~tick count), State=2 (Running) at +0x2C, WaitBlockList=NULL, Quantum=0xFFFFFF3E (idle, never
recharged). PRCB.CurrentThread slot [0x80035BDC] holds it during steady state. The
"IdleThread" slot (0x80035BE4) holds 0x80036840 = zeros; the "NextThread" slot (0x80035BE0)
holds 0x80039630 = stack garbage (Type byte 0x7F) — both unused/uninitialized by the 2BL.
(A 0x80035B54 object with Header.Type=2 is a KMUTANT, not a second thread.)

Conclusion: no thread waits on an object; the boot CPU sits in KiIdleLoop on an empty DPC queue
with an infinite wait. Only a device event (DPC via device ISR, or an expiring timer) unblocks
it. All device IRQs are masked and all device MMIO is hollow, so nothing can ever arrive.
Next: implement the first device event path the boot waits on. Best candidate is USB OHCI
(full BAR+bus-master setup on both controllers, zero follow-up) — needs MMIO + SOF interrupt;
alternatives are NV2A MMIO presence or IDE I/O completion (cmd=0005 legacy, never started).

## 18. Reframe: we are in the kernel, not the 2BL (XboxWiki Boot Process, page 51)

`References2/XboxWiki/51.mhtml` documents the 2BL program: MTRR setup, self-copy, PDE at 0xF000
(GPU regs at 0xFDxxxxxx with flags 0xFB = cache-disabled, flash, PDE self-map), MCPX ROM disable
(`out32 0xCF8,0x80000880` + `out8 0xCFC,0x02` — our LPC overlay handling), SMC watchdog
challenge (0x1C-0x1F must be non-zero, response to 0x20/0x21), IDE+NIC enable (LPC reg 0x8C),
LDT config, kernel flash copy + RC4 (key at offset 32) + LZX to 0x80010000, MZ/PE validation
(infinite loop if invalid), entry call with (cmdline@0x80400004, keys). The kernel comes from
flash, not disk — so zero IDE I/O during boot is CORRECT behavior, and our earlier "IDE
silence" worry was misplaced.

Verified: `--dump 0x80010000` = `4D 5A ...` (valid MZ). The 2BL finished; the idle loop is the
kernel's. A 50M run is histogram-identical to 100M (only interrupts 36->108 and PIC EOIs grow):
kernel cold-boot init matches the wiki list (PCI setup, SMC, Super-I/O debugger, RTC, BAR
assignment) and parks before its IDE and video-progress items. Next: NV2A MMIO region at
0xFD000000 (display init likely precedes disk), then IDE+FATX+XBE dashboard path.

## 19. NV2A MMIO region online (Phase 11 first slice)

`src/XGrid.Nv2a/Nv2aMmio.cs` implements the 16 MB BAR0 window at 0xFD000000 as an
`IMmioDevice`/`IXboxDevice`, wired in `XboxMachine` (region `NV2A`, reset + PTIMER advance in
`Run`). Block map and reset defaults are xemu facts (`nv2a.c` blocktable, `pmc.c` BOOT_0
0x02A000A3 + write-1-clear INTR, `pbus.c` PCI mirror IDs, `pfb.c` CSTATUS=ram size/WBC=0,
`pcrtc.c` START mask + raster increment, `pramdac.c` coeffs + all-locked PLL test,
`ptimer.c` numerator/denominator + TIME_0/1 bit layout, `user.c` DMA PUT/GET/REF channel
gating, `stubs.c` zero-read pattern); PFB CFG0 = 0x03070103 is the 64 MB hardware default
from the Xermu notes. Deferred: PFIFO/PGRAPH execution, IRQ assertion (pending/enabled
tracked only), PBUS↔PCI-config mirroring, VGA/PRAMIN-as-VRAM aliasing.

Boot result: FD0/FD1 unmapped buckets are gone; the kernel's early probes now hit real
registers (`NV2A regs: R101000=2 R001000=8 R000000=1 W001000=19` — PSTRAPS/PBUS/PMC reads,
PBUS command-mirror writes), all frozen before 50M. 50M→100M is otherwise identical to the
pre-MMIO baseline (interrupts 36→108, same idle EIP, IDE=0, RTC pie=False). Presence alone
does not unblock the kernel; next candidates are USB OHCI MMIO + SOF interrupt or the
IDE+FATX+XBE dashboard path.

## 20. Dashboard-path reference synthesis (2026-10-09; Cxbx/XboxWiki/xemu/Xermu/xqemu)

- XboxWiki Boot Process (`References2/XboxWiki/51.mhtml`): reset `0xFFFFFFF0`, flash
  top 16 MiB `0xFF000000-0xFFFFFFFF`, MCPX ROM overlay last 512 B `0xFFFFFE00`;
  xcodes 9 B (`02` PEEK, `03` POKE, `04/05` PCI, `06` AND/OR, `07` prefix, `08` BNE,
  `09` BRA, `10` ACC2, `11` OUTB, `12` INB, `EE` END); MCPX-disable
  `CF8=0x80000880,CFC=0x02`; 2BL MTRRs, self-copy `0x90000->0x400000`, PDE at
  `0xF000` (identity `0x0/0x800=0xE3`, self-map `0xC00=0xF063`, flash
  `0xFFC=0xFFC000E3`, GPU `0xFD0/4/8/C=0xFD0000FB...` = P|RW|A|D|PWT|PCD|PS);
  kernel RC4 (key at offset 32) + LZX to `0x80010000`, MZ/PE check, entry with
  (cmdline `0x80400004`, keys). Cold-boot order: HW init, boot animation, logo,
  HDD unlock, DVD check, else `xboxdash.xbe` from fixed partition; failures halt
  via Fatal Error. EEPROM 256 B @`0x54`: hash `0x00`, confounder `0x14`,
  HDD key `0x1C`, region `0x2C`, checksum `0x30`, serial `0x34`, MAC `0x40`,
  video `0x58`, user checksum `0x60`; HMAC-SHA1 + RC4; HDKey = first 16 B.
  IDE password = HMAC-SHA1(HDKey, model+serial 20 B) + 12 zeros; model 40 B /
  serial 20 B byte-swapped, `0x20`-trimmed. Fixed partitions, no MBR; FATX
  `FATX`, HDD cluster 32. NV2A MMIO `0xFD000000` 16 MiB, BAR0 `FD000000`.
  SMC PIC16LC @`0x10` (`33`, `91`): `01` version, `03` tray, `04` AV pack,
  `11` IRQ read-clear, `1B` scratch, `1C-1F` challenge non-zero, `20/21`
  response ignored; `02` power, `07/08` LED, `0C` tray, `1A` IRQ-enable.
- xemu LLE (`References2/xemu-0.8.136`): PCI all `10DE` (host `02A5`, LPC `01B2`,
  SMBus `01B4`, USB `01C2` x2, nvnet `01C3`, APU `01B0`, ACI `01B1`, IDE `01BC`
  when `XBOX`, AGP `01B7`, NV2A `02A0` on secondary bus); `0xCF8/0xCFC`;
  `INT_ROUT=0x0E065491` (USB0 1, USB1 9, nvnet 4, APU 5, ACI 6, IDE 14),
  `PIRQ=0x00031000` (AGP 3). SMBus AMD756: 16 B @BAR1+`0xE0`, protocols
  0-5, `HCYC 0x10`/`PRERR 0x04`/`ABRT 0x01`, IRQ on `HCYC_EN`. EEPROM
  `smbus-storage` @`0x54`, 256 B, offset + auto-increment wrap. SMC defaults
  `P01`, `hdtv=1`, `TRAYCLOSED` at realize, challenge `52/72/EA/46`. EEPROM
  crypto (`eeprom_generation.c`): CRC `~(high+low)`, double-SHA1 with version
  IVs `D/R1/R2/R3` (retail 1.0 default `R1`), `RC4(seed,20)` over `0x1C` from
  confounder. IDE (`piix.c`, `core.c`): legacy `0x1F0/0x3F6 IRQ14`,
  `0x170/0x376 IRQ15`; `locked=on` sets IDENTIFY word 128 `0b111`, unlock sets
  `0b011`; opcodes `F1/F2/F3/F4/F5/F6`, only `F2` implemented as 512 B PIO-out
  that never verifies. MCPX ROM: 512 B overlay, `0x80&2` disables. NV2A BAR0
  16 MiB + BAR1 VRAM alias; `BOOT_0 0x02A000A3`, IRQ on `pending&enabled`.
- Cxbx (`References2/Cxbx-Reloaded-master`): `types.h` KTHREAD `0x110`,
  State `0x2C` (Running 2), WaitBlockList `0x58`, Quantum `0x6C`; KPRCB
  Current/Next/IdleThread, DPC list; XBE `XBEH`, EP/KT XOR retail
  `A8FC57AB/5B6D40B6`, debug `94859D4B/EFB1F152`, Chihiro `40B5C16E/2290059D`;
  sections with head/tail sharing + SHA digests; ordinals `XeLoadSection 327`,
  `IdexChannelObject 357`, `Nt/Io` file set, `Xc` crypto `335-351`; NV2A
  classes `39/44/62/9F/97`; OHCI `Revision 0x10`, 4 ports, HCCA/DoneHead;
  XID `045E:0202`, type `0x42`, input 20 B / output 6 B; XDVDFS
  `MICROSOFT*XBOX*MEDIA`, descriptor sector 32, RootDir `u32` + size.
- Xermu/Xqemu: standalone JIT + fastmem + Vulkan vs QEMU machine; four boot
  modes (nboxkrnl/LLE-kernel/LLE-BIOS/HLE); DVD/game path is a host mapping
  (`CdRom0` to XBE parent/XISO dir, `C:` to Partition2) above the IDE security
  command, not an IDE emulation decision; TSC virtualized (733.333 MHz
  reference); SMC/EEPROM defaults match xemu; APU `0x80000` (VP `0x20000`,
  GP `0x30000`, EP `0x50000`).
- Consequence for XGrid: the `0x800551B6` 5-byte `CMPSB` gate is the EEPROM
  HMAC (version-IV dependent); the IDE-unlock path never runs because the
  password is never derived, not because the IDE model rejects it. Implement
  the xemu-parity accept-any `0xF2` unlock (word 128 `0x0007->0x0003`, 512 B
  PIO-out, IRQ) so the second gate is already open, then attack the HMAC
  inputs (EEPROM version IVs R1/R2/R3/D, region, CRCs) with a generation /
  validation tool; parallelize FATX/XDVDFS/XBE + NV2A-PFIFO/USB-OHCI.

## 21. Custom-firmware gate forensics (2026-10-09; Complex_4627 + mcpx_1.0 + user eeprom/hdd)

Effective configuration (`data/xemu-0.8.136-windows-x86_64/xemu.toml`, user-supplied,
never committed): bootrom `mcpx_1.0.bin` (512 B), flash `Complex_4627.bin` (1 MiB,
hacked BIOS that runs unsigned code), `eeprom.bin` (256 B), `xbox_hdd.qcow2`
(custom dashboard, no `dvd_path` so dashboard boots from HDD), `skip_boot_anim`
(implies SMC scratch `SHORT_ANIMATION`, which XGrid already defaults). XGrid
defaults already match xemu here (`SmcDevice`: version `P01`, AV `HDTV`, scratch
`0x04`, `TRAYCLOSED`; `LpcController` routing `0x0E065491/0x00031000`).

- 30M-instruction boot reproduces the known stall byte-identically (EIP
  `0x8001B032`, `ESI=0x80035A90`, `EDI=0xD0008BF8`, IDE=0, RTC `pie=False`,
  NV2A probes frozen, IRQ 142; `--break 0x800551B6` fires at 25,200,172 with
  `ECX=5`, `ESI=0xD001CD10`, `EDI=0xD001CD38`). The compare is `F3 A7`
  (`REPE CMPSD`, 5 dwords = 20 bytes); mismatch on the first dword, `JNZ` taken
  into the `PUSH 0xA0` / `CALL 0x80014E0A` error path, then `PspExitThread(0)`.
- Delivery proven exact: new `Amd756Smbus.Transaction` data capture
  (`src/XGrid.Bus/Smbus/Amd756Smbus.cs`, printed by `src/XGrid.Cli/Program.cs`)
  shows the full 128-word EEPROM sweep `0x00-0xFE` reconstructing the file
  256/256 with 0 mismatches; SMC challenge `52/72/EA/46`, version `P`,
  AV `01`, INTSTATUS `02`, scratch `04` all match xemu.
- Reference crypto nailed down (`References2/xemu-0.8.136/hw/xbox/
  eeprom_generation.c`, `util/sha1.c`, `util/sha1.h` — facts only):
  `length` counts bits with a 512-bit preset (pretends a 64-byte key block was
  hashed; "Middle Message"), LE words, BE length field; compute = SHA1(first
  IVs, data) then SHA1(second IVs, 20-byte digest); generate hashes the
  plaintext confounder region, derives the seed from the hash, then RC4s the
  region in place — so verification must decrypt first. Independent
  reimplementation confirms the user file is a valid R1 image (seed-derived
  RC4 decrypt yields region 1 and the recomputed hash equals the stored hash;
  both CRCs also match; R2/R3/D do not verify).
- Guest uses R1 IVs (trace shows `EAX=72127625` with standard-IV context init
  nearby), yet its ESI digest matches no R1 intermediate (seed/plain/check).
  The worker is multi-stage (~20K instructions, 8 compressions at `0x800320B0`
  callees, standard-SHA1 init `MOV [EDI],0x67452301` at `0x80031519`, HMAC
  `0x36` ipad block and `0x5C` opad in pool, RC4-shaped code, WCHAR expansion
  writer at `0x80031571`, digest stores at `0x800315B4/B8`, digest-fn arg block
  writing output `D001CD10`/len `0x14`/input `8003B140` from `0x8002F741`).
  Physical write-watch (`--watch`, no code change needed) and `--scan`
  corroborate buffer reuse across stages.
- Conclusion: same code + same bytes pass on xemu and fail here, so execution
  diverges inside the worker (CPU semantics or memory reads), not inputs or
  config. The 8-byte length-swap helper at `0x800320B0` and the WCHAR staging
  were verified structurally sound from the trace. Next: break at digest-fn
  `0x8002F6xx` entry (args identify message/key), or per-round trace-vs-model
  comparison of the first `0x800320B0`-family compression to find the first
   diverging instruction.

## 22. SHA1 compression verified bit-exact; xemu stalls identically (2026-10-09)

- Digest-framework map (all from `--break`+`--trace` runs, same firmware set):
  `0x8002F741`/`0x8002F6E2`/`0x8002F70D` = per-message digest call sites;
  `0x80031490` = SHA1-final (pad+length, len at `[ESI+0x30]`);
  `0x80031370` = SHA1-update buffering (`REP MOVSD/MOVSB`, calls compression when a
  64 B block fills); `0x800320B0` = dword byte-reverse copy helper
  (`dst[i]=rev32(src[i])`, NOT a compression — corrects note 21);
  `0x80031550` = single-block SHA1 compression (BSWAP `W[0..15]` load,
  standard `W[16..79]` expansion `ROL1(w[i-3]^w[i-8]^w[i-14]^w[i-16])`,
  80 rounds from `0x800315BD`, feedforward adds standard IVs).
- Decisive trace-vs-model: first `0x80031550` call hashes the HMAC inner-pad
  block (`1C0D9B1A 87A279A5 9CFBFB48 3CF4D86C` + `0x36`*48) from standard IVs;
  guest output `72127625 336472B9 BE609BEA F55E226B 99958DAC` EQUALS an
  independent C# reference SHA1 compression bit-exact. The interpreter runs
  SHA1-family code (ROL/ADD/XOR/AND/OR/NOT/BSWAP) flawlessly — no CPU bug here.
- Differential vs xemu CORRECTION (same session): the first headless sample
  (EIP `0x80014F2D`, frozen) was misread as a shared stall. `info blockstats`
  proves xemu passes the gate: `ide0-hd0 rd_bytes=847872` in 27 ops (FATX +
  dashboard XBE loaded), then EIP parks at `0x800426D4` = dashboard idle, zero
  further reads. The user is right: this exact triple boots the custom
  dashboard on xemu. XGrid's gate mismatch is a REAL divergence — forensics
  REOPENED (the "faithful stall" conclusion above is retracted). `ide0-cd1`
  shows 3 probe reads (54 B): the kernel also probes the DVD (ATAPI work will
  matter for the game path).

## 23. Gate passes; dashboard driver runs; parked at PFIFO (2026-10-09)

Supersedes the §22 "stop forensics" decision: xemu blockstats + gdbstub proved the
gate SHOULD pass (xemu reads 847 KB off disk, idles at `0x800426D4`), so forensics
reopened and the divergence was hunted to its root causes. Fixes this session
(all reference-first against xemu, all with regression tests):

- SMBus word-read (THE gate bug): `IoSpace.Read16` dispatches one
  `device.Read(offset, 16)`; our `Amd756Smbus` returned only `_data0`. xemu's
  `amd_smbus.c` exposes byte-only handlers, so QEMU splits word INs into two
  byte reads. The kernel driver reads EEPROM words with 16-bit INs (proven: port
  histogram shows `C006` reads but never `C007`), got `0x00A8` instead of `0xC4A8`
  (proven by xemu gdbstub register compare at filler `0x800134A9`), built a
  decimated WCHAR EEPROM table at `0x8003AEE8` (xemu: raw bytes, proven by `xp`),
  and failed the HMAC gate. Fix: compose consecutive byte registers for
  multi-byte accesses (matches HW bus + QEMU). Gate `REPE CMPSD` at `0x800551B6`
  now falls through to the match path.
- Missing instructions found by the new `XboxCpu.LastFaultVector/Eip/ErrorCode`
  + `CR2` diagnostics (printed by CLI; each was a kernel `BugCheck 0x1E`):
  CBW/CWDE/CWD/CDQ (`0x98/0x99`; CDQ faulted at `0x80024975` doing 64-bit disk
  math), SHLD/SHRD (`0F A4/A5/AC/AD`; SHRD faulted at `0x8001CD24`; result model
  follows QEMU `gen_shiftd_rm_T1` incl. 16-bit Intel doubling), CMPXCHG8B
  (`0F C7 /1`, faulted at `0x80012C9A` in a CAS retry loop), and the FPU ST(0)
  operand bug (D8/DC memory arithmetic decoded `(mem, None)`; `ReadFpuReal(None)`
  read linear 0, so `FDIV [0x80066760]` faulted with CR2=0 — fixed decode to
  `(ST(0), mem)`; DA/DE integer forms were already correct).
- PFB WBC offset: polled register is `NV_PFB_WBC = 0x100410` (xemu `nv2a_regs.h`),
  not `0x30C`; our stored-regs model kept the driver's flush request bit set
  forever (`TEST [FD000000+0x100410],1` spin, 39.8M reads). Fix: WBC reads 0.
- Status at 200M instructions: no faults, no halt, timer IRQs flowing (390),
  IDE IDENTIFY + 153 accesses done, SMC fan/LED writes done, NV2A driver active.
  Main thread parked in a countdown-retry loop at `0x800422B4` (`DEC EAX /
  MOV [ESP],EAX / JNZ`) while PFIFO USER-area reads grow (`R800000` 49K at 120M
  to 99K at 200M, `W800000=1`): the driver submits pushbuffer work and waits for
  completion that needs real PFIFO execution (registers-only today). Next:
  Phase 11 PFIFO (CACHE1 parse, DMA push, method dispatch per xemu `pfifo.c`).

## 24. PFIFO pusher runs; driver passes the GPU wait (2026-10-09)

Reference facts (xemu `hw/xbox/nv2a/`, no code copied): PFIFO pusher gates
(PUSH0_ACCESS, per-channel DMA MODE, PUSH1 DMA mode, DMA_PUSH ACCESS/not
suspended, DMA_STATE error NONE, `PGRAPH_FIFO_ACCESS`), DMA loop over
GET→PUT with old-jump/jump/call/return/increasing/non-increasing parsing,
puller (`PULL0_ACCESS`, LOW_MARK clear) dispatching method 0 (RAMHT bind),
`0x180-0x200` (RAMHT object substitution) and `>= 0x100` (engine methods) to
`pgraph_method`, RAMHT hash (folded handle XOR channel, 8-byte entries in
RAMIN), DMA objects (`flags/limit/frame` in PRAMIN, address =
`(frame & 0xFFFFF000) | (flags & 0xFFF00000)`, masked to 27 bits into VRAM),
USER PUT/GET/REF mirrors of CACHE1 regs per DMA channel, INTR_0 W1C /
INTR_EN_0 / RUNOUT_STATUS LOW_MARK, kick on PFIFO/USER/PGRAPH_FIFO writes.

- Observed on XGrid (120M boot, MMIO dumps): channel 0 DMA, PUSH/PULL access
  set, DMA_INSTANCE `0x111C` (object: class 2 FROM_MEMORY, limit `0x03FFAFFF`,
  base 0), PUT `0x03EDD000`, GET 0; pushbuffer word 0 is `JMP PUT` (idle ring).
  Driver polls USER DMA_GET (`R000044`, was 49K).
- Observed on xemu (method + reg traces, same boot): same DMA setup, 657
  handled PGRAPH methods + 13 unhandled (NV097 state: object/context-DMA
  binds, transform constants, texgen, eye position...), PUT/GET chase with
  `USER 0x40/0x44` traffic, PGRAPH `0x720` toggled 0/1 per submission.
- Implemented (`src/XGrid.Nv2a`, `IGpuMemory` fed by `XboxMachine` over
  `AddressSpace`, synchronous on kick for determinism): gate checks, DMA loop
  with jump/call/return/inc/non-inc, RAMHT lookup, puller with LOW_MARK
  clear/set, GET writeback per word, DCOUNT, ERROR+suspend (no xemu-style
  asserts), 4M-word cap with `PusherCapped` diagnostic, PGRAPH method sink
  (bind/instance tracking, per-method counts; engine check logged not asserted),
  PFIFO INTR W1C/EN/RUNOUT semantics, per-offset USER read counts. IRQ wiring
  deferred (driver polls; pending/enabled tracked).
- Result at 200M: USER GET polls collapsed 49K to 1, EIP moved `0x800422B4` →
  `0x800308EC` (display init, `EAX=0xFD6013DA`), timer IRQs flowing (479), no
  faults, no halt. 605 tests pass.
- Detour logged: a `--dumpv FD080000` red herring (missing-zero address;
  correct USER VA is `FD800000`) and unit-test hex-digit slips (`0x711C0` for
  `0x7111C0`, `0x4720` for `0x400720`) — all caught by the new tests.

## 25. Dashboard idle reached; VGA retrace unblocked it (2026-10-09)

- After the pusher landed, the driver parked polling PRMCIO (`R601000` 104M
  reads) at a `JZ` spin (`0x800308EC`) with `EAX=0xFD6013DA` (VGA Input
  Status 1). Our block map had no PRMCIO (`0x601000` fell through to 0).
  xemu's PRMCIO is QEMU VGA (`vga_ioport_read`); QEMU's dumb retrace returns
  `st01 ^ (V_RETRACE | DISP_ENABLE)` per read (toggles `0x00/0x09`), which
  terminates any set/clear spin within 2 reads.
- Implemented PRMCIO block (stored regs + retrace toggle on the `0x3D8` word,
  matching QEMU dumb behavior) with roundtrip/reset tests.
- Result: boot runs 400M instructions fault-free. EIP `0x800426D4/DC` —
  the SAME address xemu parks at with the dashboard up — same regs
  (`EAX=2 EBX=0x8004AB88 ECX=3 EDX=0x83FDF000 ESI=6 EDI=9`), IF=1, timer IRQs
  flowing (774), IDE quiet after 153 accesses. GET polls: 1. Pusher consumed
  1221 words; ~355 method keys recorded in xemu-identical order (object/context
  binds, semaphore, transform constants, texgen, eye position...). No new GPU
  work 300M→400M (pusher words frozen) — xemu's 80 s trace likewise shows only
  ~657 methods (on-demand UI, no continuous animation). The custom dashboard
  is up and idling, waiting for input.
- Next (M4): framebuffer extraction — read PGRAPH surface regs, dump the
  surface from RAM, untile/swizzle-decode to PPM offline (Cxbx swizzle facts).

## 26. Framebuffer path works; screen is black on both emulators (2026-10-09)

- Surface methods (xemu `nv2a_regs.h` facts): FORMAT `0x208` (color/zeta/type/
  AA/W/H), PITCH `0x20C`, COLOR/ZETA_OFFSET `0x210/0x214`, CLIP `0x200/0x204`;
  CLEAR values `0x1D8C/0x1D90`, CLEAR_RECT `0x1D98/0x1D9C` (inclusive min/max,
  12-bit fields), CLEAR_SURFACE `0x1D94` (bit0=Z, bit1=stencil, `0xF0`=RGBA),
  FLIP_READ/WRITE/MODULO `0x120/0x124/0x128`. Z24S8 clear: stencil low byte,
  depth upper 24 (pixel = value as-is); color clear = raw value (xemu: "works
  like memset"). Observed boot values: format `0x128` (A8R8G8B8/PITCH/Z24S8),
  pitch `0x14001400`, color `0x03C84000`, zeta `0x03A2C000`, clip 1280x480,
  clears `0x3` (Z+stencil only) — color never cleared/drawn.
- Implemented: surface/clear/flip state tracking in the sink, `CLEAR_SURFACE`
  execution (pitch-linear fills with bounds caps; swizzle deferred), `IGpuMemory
  .WritePhysical`, CLI `boot --fb <path>` dumping the presented surface as raw
  bytes (bounds-checked). Tests drive clears through the pushbuffer, incl. an
  unaligned 16-bit merge case.
- Result: `fb0.raw` 1280x480 A8R8G8B8 decodes to pure black — and xemu's own
  surface bytes at `0x03C84000` (`xp`) are zeros too. Pixel-faithful.
- Driver then arms PGRAPH+PCRTC+PFIFO interrupts (`INTR_EN` all set) and idles
  at `0x800426DC` with no MMIO polling — IRQ wait. xemu routes NV2A via AGP
  bridge to PIRQC (`xbox_pci.c`: `case 30`), default PIRQ routing sends PIRQC
  to PIC IRQ 3. Next: NV2A interrupt delivery (PMC aggregation → PIRQC → PIC →
  CPU) with VSYNC/frame event sources.

## 27. NV2A IRQs wired; dashboard idle is stable and black-faithful (2026-10-09)

- Reference facts: `nv2a_update_irq` (PFIFO/PCRTC/PGRAPH pending&enabled →
  PMC bits 8/24/12; line asserts iff PMC pending & enabled), VBLANK set by
  display refresh (`nv2a_vga_gfx_update`, raster reset), PGRAPH CONTEXT_SWITCH
  bit 12, PMC INTR_0 `0x100` / INTR_EN_0 `0x140`, UMA (`vram is ram`).
- Implemented: `Nv2aMmio.InterruptChanged` + `UpdateIrq` on every INTR/INTR_EN
  write, VBLANK heartbeat in `Advance` (60 Hz emulated, deterministic),
  CONTEXT_SWITCH pending on bind change, PMC INTR W1C incl. subordinate
  clear-down; `XboxBus.Nv2aPci` stub reference; `ResolveDeviceIrq` routes
  `Nv2aGpu` via slot 30 → PIRQC → IRQ 3 (xemu `case 30` parity, honors driver
  PIRQ reprogramming); `XboxMachine` subscribes line to stub raise/lower.
  Tests: vsync assert/silent/clear, bind-raises-IRQ.
- Boot truth at 400M: PMC INTR `0x01001000` (PGRAPH+PCRTC pending — vsync and
  binds DO fire) but PMC INTR_EN `0x01` (subordinate bits NOT enabled by the
  driver) → line correctly down (`irq=False`). No spurious IRQs; unit INTR_ENs
  armed as on xemu. IRQ path proven by tests, dormant by driver choice.
- Stability: 300M→700M identical idle (`0x800426D4/DC/DA`, same regs),
  pusher words frozen at 1221 after the init burst (12 BEGIN_END draws flowed
  300M→400M through the recording sink), interrupts PIT-only, screen black on
  both emulators (xemu surface bytes also zero). Dashboard waits for input/time;
  UI draws need PGRAPH rasterization (no pixels rasterized yet) and more
  emulated time (~1 s emulated so far).
- Next tracks: (1) PGRAPH draw execution — vertex fetch, shaders, raster,
  textures → non-black pixels (M5–M8); (2) CPU speed (JIT, Track C) — the menu
  likely appears after more emulated seconds than the interpreter can reach
  interactively.

## 28. First rendered frame: Gouraud triangle (M4, 2026-10-09)

- The dashboard submits one TRIANGLES draw (BEGIN 5, 12 INLINE_ARRAY words,
  END 0): slots 0+6 active (float2 each), 3 verts, into a 640x480 surface.
  Decoded per xemu `bind_inline_array` packing (count-0 slots skipped,
  4-byte-aligned).
- Implemented a minimal software rasterizer: inline vertex assembly, TRIANGLES/
  STRIP/FAN/QUADS, edge-function fill (double-sided v1 deviation), scissor to
  clip rect, Gouraud RGB lerp, A8R8G8B8 + R5G6B5 writes with bounds guards;
  no transforms/shaders/textures/zeta yet (documented). Position = first
  active slot (x,y), color = second (rgb clamped, a defaults 1).
- Result: 307,188 non-black pixels; the 640x480 extract shows a black→red→
  green Gouraud triangle. Pushbuffer command-encoding lessons logged: INC vs
  NON-INC is bit 30 (`0x40000000`), so BEGIN/END use `0x400417FC` (not
  `0x40417FC`, which parses as INC count 257 and desyncs the stream); inline
  data requires a NON-INC `0x1818` header command. Test scene renders through
  the full path (CACHE1 → pusher → puller → raster → RAM → PPM).
- Next: correct attribute roles/transforms (vertex shaders or fixed-function
  matrices), textures, zeta, flip/display (M5–M8).

## 29. Gate 1 reference facts: kernel-to-dashboard handoff (2026-10-09)

XboxWiki Boot Process (p.51): 2BL enables IDE+NIC via PCI (`0x8000088C <-
0x40000000`); kernel (stripped NT 5.0 + HAL + filesystems + HDD/DVD drivers,
base `0x80010000`; INIT section holds cold-boot init + boot animation and is
discarded after) inits hardware, plays the animation with direct register
access (no audio/video drivers in kernel), unlocks the HDD, checks DVD, then
loads `xboxdash.xbe` from partition 3 (dashboard) or starts the game; errors
(no/wrong HDD, bad signature) show the Fatal Error screen and halt. Reboot
re-init skips PCI setup, EEPROM decrypt, AV-pack check, video mode, "some IDE
stuff", "some SMC stuff", allocator init, and debugger init (Kernel p.49 notes
the IDEXPRDT PRDT section is allocation-only).

Gate 1 consequences: the guest kernel itself performs HDD unlock (ATA SECURITY
UNLOCK with the EEPROM key), FATX reads, and XBE load — no host FATX/XBE code
is on the boot path. Our `AtaDevice` already implements the Xbox-critical set
(IDENTIFY `0xEC`, READ `0x20`/`0x24`, SECURITY UNLOCK `0xF2`, init
`0x91`/`0xEF`/`0xE7`) and the IDE controller is PCI-registered with legacy
ports (`XboxMachine` wiring); the guest enumerates IDE on PCI but never issues
ATA commands. So Gate 1 is NOT a missing-command problem — the open question
is purely what the post-storage-init idle kernel waits on (candidates: DVD
check/tray, USB stack, SMC event, PM timer, VBLANK IRQ, or simply more
emulated time per §27).

Differential tool (confirmed in tree): xemu is QEMU-based, so `xemu -trace`
with `hw/ide/trace-events` (`ide_ioport_read/write`, `ide_bus_exec_cmd`,
`ide_sector_read`) yields the reference kernel IDE timeline for the same
BIOS/HDD; our port histogram is the symmetric probe. Experiment plan: (1) run
xemu with IDE tracing to dashboard, record first ATA command + guest-time;
(2) run XGrid longer-boot IDE histogram (late vs never); (3) diff PCI/SMBus/
USB activity just before first ATA command on xemu vs ours to find the missing
wake-up. Xermu corroborates direction only: its "Guided Executor" is a JIT
(host regs = guest regs, fastmem + VEH-trapped MMIO) targeting dashboard +
retail games — same Track C conclusion as our Gap 3.

## 30. xemu IDE differential capture: DMA-only dashboard load (2026-10-09)

Ran headless `xemu.exe -display none -trace` (`ide_ioport_read/write`,
`ide_bus_exec_cmd`, `ide_sector_read`, `ide_status_read`, `ide_ctrl_write`,
data-phase events excluded) with our exact set (Complex_4627, mcpx_1.0,
eeprom, xbox_hdd.qcow2), 110 s to dashboard idle. Whole-boot IDE traffic is
42 commands: `EF EF EC F2 EC 91 EF EF A1 A0 x5 C8 x27+`. Facts: SET FEATURES
init, IDENTIFY, SECURITY UNLOCK (`0xF2`, HDD unlock with EEPROM key) comes
4th — very early; second IDENTIFY, init `0x91`, more `EF`; DVD probe
(`0xA1` IDENTIFY PACKET + five `0xA0` PACKETs, empty DVD attached); then the
entire dashboard load is READ DMA (`0xC8`) — no PIO data reads at all.

Two consequences for XGrid. (1) Wake-up question stands: our guest never
issues even the first `EF`, so the stall precedes IDE init (§29 candidates
unchanged; longer-boot histogram still needed). (2) NEW second wall: our IDE
stack has zero DMA support (no BMIBA/PRDT engine, no `0xC8`/`0xC4`, no ATAPI
slave for `0xA1`/`0xA0`) — verified by grep. Once the kernel wakes, dashboard
load demands bus-master DMA with the kernel's IDEXPRDT PRDT plus a DVD-side
ATAPI model that at least NAKs probes like xemu's empty drive. Implementation
order: wake-up first (else DMA is untestable), then BM-DMA + ATAPI-probe
handling before any dashboard-load attempt.

## 31. Gate 1 DONE: kernel loads the dashboard via DMA (2026-10-09)

The whole §30 wall fell in one session, driven by guest-behavior evidence
(port/command/packet/BM diagnostics) plus a second xemu oracle capture
(`ide_atapi_cmd*` traces). Fixes, all reference-first against xemu
`hw/ide/{core,atapi}.c` + `ide-internal.h`, all with regression tests:

- ATA data-port width: NT moves IDENTIFY/sectors with 32-bit `REP INSD`;
  our port returned 2 bytes per 32-bit read, so IDENTIFY never completed
  (128 accesses, guest parked). Now 8/16/32 all advance correctly.
- Dual-drive taskfile: master HDD + ATAPI DVD slave, QEMU-style shared setup
  writes + drive-select routing; slave returns the ATAPI signature
  (`0x14/0xEB`) and IDENTIFY PACKET block (word 0 `0x85C0`).
- ATAPI protocol details the driver polls: DRQ for the packet phase,
  interrupt-reason bits (`CoD|IO`), READY|ERR (`0x41`, no SEEK) on packet
  errors, REQUEST SENSE with 18 B fixed-format data, per-cause sense
  (TUR/media `2/0x3A`, unknown opcode `5/0x20`, MODE SENSE page `5/0x24`).
- Bus-master DMA engine (synchronous v1): BAR4 window via `OnBarWritten`,
  PRDT walk through `IGuestMemory` (wired to RAM in `XboxMachine`), ATA
  READ/WRITE DMA, ATAPI packet-in + packet-data DMA, IRQ ack on status
  read/command write, BM INT/ERROR bits. Three evidence-driven corrections:
  DMA-pending status stays READY (BSY deadlocks the driver into DEVICE
  RESET), the packet arrives via DMA for F01 (zero PIO bytes, BM started)
  but the driver still requires DRQ after A0, and both START-then-command
  and command-then-START orders are kicked.
- Packet errors always IRQ (no data follows), and a BM START that finds an
  errored packet completes with INT so the driver reaches REQUEST SENSE.

Result at 700M steps: guest sequence matches the xemu oracle through the
entire DVD probe (`TUR→SENSE→MODE SENSE(0x3E)→SENSE→READ CAPACITY→SENSE`,
sense bytes identical), then **27 `READ DMA (0xC8)` commands** moving ~828 KB
(FATX metadata stride + 256-sector XBE bulk + tail). RAM forensics prove the
bytes are the real dashboard: `XBEH` at phys `0xBF000`, the kernel's
`CMP [ESI],"XBEH"` loader check at `0x2EE98`, and the `EvoX` cert string —
matching the parallel FATX track's identification (softmod `xboxdash.xbe` at
`0x465400`). Next gate: XBE launch (kernel idles at `0x800426D8` after the
load; verify/relocate/entry-call, then menu draws + USB input).

## 32. XBE-launch gate: animation wait solved via NV2A semaphore/notification LLE (2026-10-10)

The `0x800426D8` park was fully diagnosed and passed. Evidence trail, in order:

- **xemu reproduces the stall bit-for-bit** (0.8.136, same BIOS/HDD/EEPROM,
  both `short-animation` settings, 6 min + 7.8 h CPU observed): `EIP=0x800426D4`,
  `[P+0x1C]=9`, flag `[0x8004D878]=0`, notifies page < 7. So the park is
  kernel behavior for this configuration, not an XGrid gap. (xemu's notifies
  page read 3 or 5 depending on timing; ours always 3.)
- **The wait**: boot-animation/loading-screen frame pacing in INIT. `waitHelper`
  (`0x80042620`) spins until the notifies-page dword reaches `[P+0x1C]`; the
  page is the `NV097_SET_CONTEXT_DMA_NOTIFIES` (method `0x180`) target: RAMHT
  handle 2 → PRAMIN `0x11180` DMA object → phys page (frame `0x03FDF003`),
  seeded `3` by `0x800422C0` (T=5). The wait target in the observed frame was 7.
- **The missing writer**: the kernel's animation pushbuffer issues
  `NV097_BACK_END_WRITE_SEMAPHORE_RELEASE` (`0x1D70`) with increasing values
  (5, 7, 9, … up to 0x1F observed) using `SET_CONTEXT_DMA_SEMAPHORE`
  (`0x1A4`, value **8 = RAMHT handle**, resolved to PRAMIN `0x111B0`, whose
  object targets the same page). xemu's `pgraph.c` stores the parameter and
  `nv_dma_map`s it as a **raw PRAMIN offset** (its own `TODO` comment) → the
  write never reaches the page; XGrid now resolves it through the RAMHT
  (`TryRamhtLookup`) and writes `parameter` to `object.address + semaphore
  offset` through `IGpuMemory`. The loader then passes the wait.
- **IRQ gating**: xemu's `nv2a_update_irq` asserts the PCI line when
  `pmc.pending && pmc.enabled` (logical AND, `nv2a.c:48`); XGrid required a
  bitwise match, so the kernel's `PMC_INTR_EN_0 = 1` never let PGRAPH pending
  (bit 12) through. Matched xemu.
- **Notification**: `NV097_NO_OPERATION` (`0x100`, parameter != 0) raises the
  PGRAPH notification (xemu parity): `NV_PGRAPH_INTR` ERROR `1<<20`,
  `NV_PGRAPH_NSOURCE`=NOTIFICATION, `NV_PGRAPH_TRAPPED_ADDR` with method
  `0x100`, `NV_PGRAPH_TRAPPED_DATA_LOW` = parameter. The kernel's NV2A DPC
  (`0x80045F80` → `0x80045D80`) dispatches command codes `0x300`-`0x320`
  (observed `0x31C`, `0x320`, `0x304`) and acked through `PGRAPH_INTR`.
- **Result**: at 700M steps the kernel no longer parks; it runs the whole
  animation (14 semaphore releases to `0x1F`, 470 IRQs) and enters the
  video-mode-set path. New gate: a **null object release** faults at
  `0x8004E147` (`mov ecx,[eax]`, EAX=0) called from `0x80057A33`; the object
  came from `0x80053302` (video-device open/create) and stayed NULL, then
  BugCheck `0x1E` (KMODE_EXCEPTION_NOT_HANDLED) with `0xC0000005`. Next:
  trace why `0x80053302` returns NULL (device creation) — see plan "Next
  session".
- Tooling added this session: `xgrid boot --ramdump <path>` and `--ramin
  <path>` (RAM/PRAMIN snapshots), NV2A semaphore/notification/IRQ diagnostics
  in the CLI; workflow used `objdump -b binary -m i386` (kernel image at phys
  `0x10000`, VA = phys + `0x80000000`) and xemu's QEMU monitor (`xp`) as the
  oracle. Kernel exports (366, ordinal-only) parse from the PE at
  `0x80010000`; ordinal names come from Cxbx `kernel/common/kernel.h`.

## 33. XBE-launch gate progress: MCPX ACI and APU register models (2026-10-10)

Two more gates passed after the animation fix (§32), each a missing MCPX device.
Both devices are fixed-address MMIO windows (the guest uses the hardcoded
addresses, not BAR values):

**ACI = AC'97 at 0xFEC00000 (not an I/O APIC).** XboxDevWiki "Memory"/"PCI":
`0xFEC00000-0xFEC00FFF` is the ACI register window (PCI 0:6.0 BAR2, 4 KB).
`XboxAddressMap.IoApicBase` was a misnomer; renamed `AciBase`. Layout and
semantics follow xemu's QEMU AC'97 core (`hw/audio/ac97.c` via
`hw/xbox/mcpx/aci.c`): NAM codec register file at 0x000-0x0FF, NABM bus-master
block at 0x100-0x17F (PI/PO/MC at 0x00/0x10/0x20, SO at 0x70, GLOB_CNT 0x2C,
GLOB_STA 0x30, CAS 0x34). Key semantics: GLOB_STA reads with `GS_S0CR`
(bit 8) always set; CAS read returns `cas` then sets it, any NAM access
clears it; CR bit 1 (RR) resets the block (SR=DCH); GLOB_CNT writes with
COLD_RST/WARM_RST bits are ignored. The kernel's AV-device init
(`0x800531F7` → `0x800530F6` → `0x80053028` → `0x80050BA7` → `0x80052D01` →
`0x80052B2A`) writes codec reg 0x26 (powerdown) = 0, reg 0x02 = 0, reg 0x18 =
0x808 and resets PO_CR/SO_CR — but **first polls CAS bit 0 until clear with a
1000×`StallExecution(0x14)` timeout**. With the window unmapped (reads all
ones) the bit never cleared → E_FAIL (0x80004005) → video-device open
(`0x80053302`) returned NULL → the `0x8004E147` release fault (BugCheck 0x1E)
seen after the animation fix. `McpxAciDevice` implements the register model;
the boot now runs the full animation and proceeds.

**APU at 0xFE800000.** 512 KB window (PCI 0:5.0 BAR0). Registers follow
xemu `hw/xbox/mcpx/apu/apu.c` + `apu/vp/vp.c`: global regs below 0x20000
(ISTS 0x1000, IEN 0x1004, FECTL 0x1100, FEDECMETH 0x1300, FEDECPARAM 0x1304,
FEMEMADDR/FEMEMDATA 0x1324/0x1334 `magic write`, FETFORCE1 0x1504), VP
front-end PIO at 0x20000+ (`NV1BA0_PIO_FREE` 0x20010 reads 0x80 — xemu does
not model the queue; VOICE_ON 0x20124, VOICE_OFF 0x20128, voice-list regs
TVL/CVL/NVL 0x2054-0x2074). IRQ: ISTS write clears the written bits, then
`update_irq` sets `FETINTSTS` if FECTL is trapped and asserts `GINTSTS` +
PCI IRQ when `(IEN & GINTSTS) && (ISTS & ~GINTSTS & IEN)`; the kernel's IEN
is 0xAFA (bits 1,3-7,9,11) plus bit 0 toggled when audio starts
(`0x8004F428`). The kernel stops a voice by writing SET_CURRENT_VOICE +
VOICE_OFF in `0x80051B6C`, sets bit 28 of the stream object, and then
spins at its tail (`0x80051C93` → `0x8004EDCD`) until the completion clears
bit 28. The completion (`0x80051B0D` → `0x80051426` → `0x8005037D` →
`0x8004E6BE`) runs from the ISR's FETINTSTS path (`0x80052499`) and requires
`[dev+0x1C4] == 0` (the AV device operation counter held for the duration of
the FE command sequence). **Delivering the `SE2FE_IDLE_VOICE` message
(0x8000) synchronously in the VOICE_OFF write races it**: the ISR fires 72
instructions later while the counter is still 1, the completion is skipped,
and the stop method's tail wait deadlocks. xemu avoids this because its VP
frame loop sends the idle-voice message **asynchronously** (per ~5.33 ms
frame, `EP_FRAME_US` = 256 samples @ 48 kHz); `McpxApuDevice` queues the voice
on VOICE_OFF and delivers it from `Advance()` on frame boundaries. With that,
the completion runs, bit 28 clears, and the boot continues.

**New gate (current).** At ~137.5M instructions the video-mode function
(`0x800579F4`, called from the callback stub `0x80055F7F`) succeeds, then
calls the AV-device teardown `0x80056E03` → sub-object teardown `0x8005BACE`
(AV+0x57C) → releases handles at +0x00/+0x10/...+0xD0 via `0x800409F0`
(NULL-dereferencing release helper: `mov eax,[esi]`). `[AV+0x64C]` (= sub+0xD0)
is **NULL**; the initializer (`0x8005BBEE`) explicitly clears it and the
setter `0x8005C366` (creates a 0x400×0x400 surface through the D3D runtime
`0x8003E630`) never ran. Found with a new `--break-log` CLI option
(logs every hit of an address with ESI/EDI/ESP/caller/arg and continues):
the crash call is `0x8005BB37` with arg 0 at 137,529,024. Likely tied to the
XMV/AV playback path (D3D surface creation depends on NV2A operations we do
not implement) — i.e. NV2A M5-M8 depth. Crash: `#PF` at `0x800409F5`
(CR2=0), BugCheck 0x1E (`0xC0000005`).

Tooling added this session: `--break-log <addr>`; `McpxAciDevice`/`McpxApuDevice`
access counters and an APU `EventLog` ring (ISTS acks, FECTL writes, IRQ
raise/lower, voice-off/idle-voice) printed by the CLI.

## 34. FCOMP memory-operand bug fixed — animation runs, XBE launches (2026-10-10)

**Root cause of the §33 NULL-surface crash was a CPU bug, not an NV2A gap.**

The kernel's AV per-frame loop is `0x800579F4` (run intro to completion) →
`0x800579B8` (tick loop) → `0x80057949` (tick) → `0x800571AD` (scene update).
The tick loop condition is `fld [esi+0x10]; fcomp [0x80065E54]; fnstsw ax;
test ah,0x5; jnp` — loop while the AV media clock (`[AV+0x10]`) is *less than*
the duration (`[0x80065E54]=8.0`). At the loop entry the clock is `0x40F00000`
(7.5, set by the AV init at `0x80056B63`) and the duration is `0x41000000`
(8.0), so the loop must run. It never did: the animation "completed" with zero
ticks and the teardown then released the never-created surface `[AV+0x64C]`.

Diagnosis (all via `--break-log`, extended to print EAX/EFLAGS):
- `0x800571AD`/`0x80057949` had **zero hits** in a full run; `0x800579B8`
  (loop entry) ran once at 137,485,043.
- Halting at the loop test `0x800579D2` confirmed `[AV+0x10]=7.5` and
  `[0x80065E54]=8.0`, yet the fall-through after the `jnp` (`0x800579E2`)
  executed with `EAX=0x4001` — i.e. the FNSTSW status had **C3=1, C0=0**
  ("equal"), so `test ah,0x5`/`jnp` exited the loop.

**Bug**: `Decoder` builds the `D8`/`DC` memory forms of the x87 compare
instructions as `(ST(0), memory)` (Decoder.cs:1805), but the execution in
`XboxCpu.ExecuteFpu` compared against `instruction.Operand1` — which for these
forms is ST(0) itself — so `fcomp mem` always compared ST0 with ST0 (equal).
Register forms (`FCOM/FCOMP ST(i)`, decoded as `(ST(i), None)`) were correct.
Fix: compare against `Operand2` when present, else `Operand1`. `FICOM/FICOMP`
(`DA`/`DB` memory forms decode as `(memory, None)`) were already correct.
Regression tests: `FpuTests.Fcomp_MemoryOperand_ComparesAgainstMemory` and
`Fcomp_MemoryOperand_EqualSetsC3`.

**Result**: with the fix, the animation ticks (32 scene updates from
137.4M→488.9M instructions — one per frame, gated on the timer tick global
`0x8003A89C`), the animation completes, the AV teardown runs **without
faulting**, and `xgrid boot --steps 700000000` now completes all 700M steps
with `LastFault vec=0`. Execution ends in user space (VA ≈ `0x124A3`/
`0x127D9`) which page-walks (CR3=0xF000) to phys `0x5C4A3` — the dashboard
XBE image loaded at phys `0x5A000`.

**The XBE launch gate is PASSED (corrected analysis).** The XBE header's entry
field (file offset 0x128, VA `base+0x128` = `0x10128`) is an **absolute VA**,
not an RVA: the file stores it XOR-encrypted (`0xA8FD7DBC`); the kernel
decrypts it in place (`xor [0x10128],0xA8FC57AB` at `0x8002EFE6`) giving
`0x12A17`, then calls it directly (`call DWORD PTR ds:0x10128` at `0x800158DA`
— the XBE launch function; return address `0x800158E0` is on the guest stack).
The XBE (base `0x10000`, 1 section `EvoX` VA `0x11000` vsize `0xBA2F4`,
size_of_image `0xBB300`, `EvoX` cert) is mapped at VA `0x10000` ↔ phys
`0x5A000`, so the entry executes at phys `0x5CA17` (= VA `0x12A17`). The entry
resolves its kernel-thunk imports (`[0x12C4B] = 0x8001EEAC`) and runs its init:
`call 0x5C850`/`0x5C7F0` (VA `0x12850`/`0x127F0`) → `0x5C390` (VA `0x12390`)
→ the bit-stream decompressor at `0x5C4A0` (RVA `0x24A0`) — a 16 KB ring buffer
(`[ctx+0x34]`, mask `0x3FFF`) read bit-by-bit (`movzx`/`shl bl,cl`/`or`), state
at `[ctx+8]`/`[ctx+0x20..0x30]`, with saved state copies at `[ctx+0x4034..0x4040]`.
At 700M the decompressor context (`ESI` ≈ `0x7FDCB800` → phys `0x4DC800`) has
`srcIndex=0x19174` (102772) and state=12.

**Next gate**: decide whether the dashboard's init decompression is progressing
(it likely decompresses the EvoX dashboard payload) or looping (a second bug) —
watch the decompressor context fields across step counts, or trace the
`0x5C390`/`0x5C420` orchestrator. Then the dashboard's D3D/menu bring-up
(NV2A M5-M8 + present) and USB input.

## 35. Dashboard init progresses: decompression works, HDD writes, USB gate (2026-10-10)

**Decompression is real, not a loop.** The init decompressor's context
(`0x5C390` orchestrator called from the entry at `0x5C7F0`; ctx VA ≈
`0x7FDCB800` → phys `0x4DC800`, deterministic) shows `srcIndex` (`ctx+0x2C`)
advancing **monotonically 0 → 0x19174 with ~102781 writes** (≈ one write per
byte, no resets) and the 16 KB ring buffer receiving real data — a legitimate
LZX/Xpress-style decode. The orchestrator is called twice (525.9M, 690.9M
instructions) with output buffer `0x7FA00000` and size `0x3CB000`; the source
is fed in by the entry's kernel-thunk reads (`[0x12C4B] = 0x8001EEAC`). So the
dashboard entry runs, resolves its imports, and decompresses its payload.

**HDD-write gate.** After the decompression the dashboard issues an ATA
**WRITE DMA (`0xCA`)** to LBA `0x80000` (and reads `0xC8`/`0xEC`); the qcow2
reader threw `NotSupportedException: The qcow2 reader is read-only`
(`Qcow2DiskImage.WriteSector`) from `AtaDevice.CommitDmaSector`. Fix:
`OverlayDiskImage` — an in-memory copy-on-write wrapper (reads consult the
overlay first; writes are retained in RAM) — applied in `XboxMachine.AttachHdd`
so guest filesystem writes cannot corrupt the shared reference image. 3 tests
(`OverlayDiskImageTests`). With it, `xgrid boot --steps 1600000000` completes
**all 1.6B steps with no fault** (`LastFault vec=0`), ending at EIP `0x76192`
(still inside the dashboard image, base `0x10000`), with the IDE trace showing
the WRITE DMA (`0xCA`) succeeding.

**Next gate — USB/OHCI.** At 1.6B the CLI reports `Unmapped phys (1MB buckets):
FED=375651421` — the dashboard is polling **USB0 (`0xFED00000`)**, which is a
PCI stub (`NForceUsbController`, 0:2.0) whose MMIO window is **not registered**
in the memory map, so reads return the sentinel and the poll never satisfies.
Implement the OHCI USB controller (register model + ED/TD + root hub) and map
its window; references: xemu `hw/usb/hcd-ohci.c`, `hw/usb/`, and xqemu. Then
the dashboard's D3D/menu bring-up (NV2A M5-M8 + present).

## 36. OHCI USB implemented — USB gate passed (2026-10-10)

`OhciUsbController` (`src/XGrid.Bus/Usb/OhciUsbController.cs`) models the MCPX
OHCI 1.0 controller following xemu `hw/usb/hcd-ohci.c`:
- 256-byte register region (xemu registers a 256-byte OHCI memory region),
  four root ports (xemu `hw/xbox/xbox.c`: `num-ports=4`), base `0xFED00000`
  (USB0) / `0xFED08000` (USB1), PCI 0:2.0/0:3.0 (`NForceUsbController`).
- Register semantics: HcRevision `0x10`; HcRhDescriptorA = `NPS | num_ports` =
  `0x204`; HcRhDescriptorB/RhStatus; HcControl (HCFS state machine; operational
  starts frames, suspend stops and clears SF, reset re-inits the root hub);
  HcCommandStatus (HCR triggers a **soft** reset — status/intr cleared, root hub
  and descriptors kept — and SOC reads as `0`); HcInterruptStatus/Enable/Disable
  with xemu's `asserted = (intr & MIE) && (intr_status & intr)` gating;
  HcFmInterval (`fi=0x2EDF`, FSMPS/FIT bitfields); HcFmNumber; HcLSThreshold
  `0x628`; HCCA/ED pointer masks (`0xFFFFFF00`/`0xFFFFFFF0`); unaligned access
  returns `0xFFFFFFFF` and writes are ignored (xemu).
- Root hub: port status reads return `ctrl | PPS`; writes apply the
  write-1-to-clear WTC bits and the PES/PSS/PRS "set if connected" logic — with
  **no attached device** `CCS=0`, so an enable write sets `CSC` and raises RHSC;
  hub LPSC/LPS power the ports; `old != new` raises RHSC.
- Frame timing: `Advance(cycles)` runs 1 ms frames only while operational
  (`HCFS=Operational`), incrementing HcFmNumber, raising the SOF interrupt, and
  writing the HCCA frame/pad/done back to guest memory (`hcca+0x80/0x82/0x84`)
  via `IUsbMemory`. USB0/USB1 are wired to the machine (`XboxMachine.Usb0/1`,
  PCI IRQ via `introute` slot 2→IRQ1, slot 3→IRQ9) and advanced from
  `XboxMachine.Advance`. 9 tests (`OhciUsbTests`).

**Result**: the `FED=375651421` unmapped bucket is gone — the USB register
accesses now land in the OHCI model. `xgrid boot --steps 1600000000` completes
**with no fault** (`LastFault vec=0`) at EIP `0x22DE2` (page-walks to phys
`0x10FDE2`, a **newly mapped module** — the kernel remaps as the dashboard
loads code; the code is real x86 with calls into the module at
`0x113400`/`0x153910`/`0x119B24`). IDE activity continues (277 PRDT starts,
reads at LBA `0xF8F7xx`). Remaining unmapped buckets are tiny (`045=4 065=8
FEF=3970` — the nvnet stub is probed briefly).

**Next gate — NV2A fixed-function pipeline (M5-M8).** A 1.7B-step run
(`--pgraph`) shows the dashboard has reached its **rendering phase**: EIP
`0x14C72` (dashboard `.text`, page-mapped in pieces; 1.6B EIP was `0x22DE2`,
so the CPU is progressing), NV2A semaphores unchanged (52), and 186844 PGRAPH
methods submitted — dominated by `NV097_ARRAY_ELEMENT16` (`0x1800`, 58577),
`NV097_SET_TRANSFORM_CONSTANT_LOAD` (`0x1EA4`, 8301) + transform constants
(`0xB80`.., 8301/3387/3255 per dword), `NV097_SET_BEGIN_END` (`0x17FC`, 5340
draws), `NV097_DRAW_ARRAYS` (`0x1810`, 32), `NV097_INLINE_ARRAY` (`0x1818`,
396), surface format/color/zeta (`0x208`/`0x210`/`0x214`), `NV097_CLEAR_SURFACE`
(`0x1D94`, 41) and flips (`0x1D84`/`0x1D90`, 70/151). XGrid's NV2A handles
inline draws (`0x1818` + `0x17FC`), surface set/clear, and the semaphore/
notification path, but **ignores `0x1800` (ARRAY_ELEMENT16), transform
constants (`0xB80`/`0x1EA4`), vertex formats, textures, lighting, and zeta** —
so the dashboard's geometry is not transformed/rendered (screen effectively
black). Next: implement the fixed-function vertex pipeline (M5: transform
constants → vertex transform → clip/screen space, plus the inline-array
element semantics), then textures/lighting (M6), zeta/blend (M7), and present
(M8). The CLI now prints draw stats (`draws executed/skipped/pixels/clears`)
to quantify progress. Reference: xemu `hw/xbox/nv2a/pgraph/` (`methods.h.inc`,
`vertex.c`, `draw.c`, `vsh-ff.c`), `nv2a_regs.h`.

## 37. NV2A M5 started: transform program/constant + inline element foundation (2026-10-10)

**Discovery — the dashboard uses the programmable vertex pipeline.** Dumping
the PGRAPH method stream and reconstructing the writes shows the dashboard's
`0xB00`..`0xB7C` traffic is `NV097_SET_TRANSFORM_PROGRAM` (the NV2A vertex
shader program, 136 dwords max), not fixed-function T&L: it uploads real
NV2A vertex programs (e.g. first instruction
`0x00000000 0x0020001B 0x0836106C 0x2F100FF8`) and feeds them constants via
`NV097_SET_TRANSFORM_CONSTANT` (`0xB80`+)/`SET_TRANSFORM_CONSTANT_LOAD`
(`0x1EA4`). On the Xbox, Direct3D's fixed-function T&L is implemented by the
driver generating vertex programs, so M5 for this title requires a
**transform-program interpreter** (xemu `vsh-prog.c`), not only `vsh-ff.c`.
Vertex data is fetched from guest memory: the dashboard sets vertex array
formats (`0x1760`-`0x179C`) and offsets (`0x1720`-`0x175C`) and submits
indexed draws via `ARRAY_ELEMENT16` (`0x1800`) + `SET_BEGIN_END`.

**Implemented (foundation slice, xemu `pgraph.c`-faithful):**
- Transform program storage: `SET_TRANSFORM_PROGRAM` (`0xB00`..) writes
  `programData[PROG_LD_PTR][slot%4]`, advancing `PROG_LD_PTR` every 4 dwords;
  `SET_TRANSFORM_PROGRAM_LOAD` (`0x1E9C`), `SET_TRANSFORM_PROGRAM_START`
  (`0x1EA0`), `SET_TRANSFORM_EXECUTION_MODE` (`0x1E94`),
  `SET_TRANSFORM_PROGRAM_CXT_WRITE_EN` (`0x1E98`) stored.
- Transform constants: `SET_TRANSFORM_CONSTANT` (`0xB80`..) writes
  `vshConstants[CONST_LD_PTR][slot%4]`, advancing `CONST_LD_PTR` every 4
  dwords; `SET_TRANSFORM_CONSTANT_LOAD` (`0x1EA4`) selects the block. 136
  program slots and 192 constant registers, matching xemu.
- Inline element semantics: `ARRAY_ELEMENT16` (`0x1800`) appends two 16-bit
  elements, `ARRAY_ELEMENT32` (`0x1808`) appends one, `DRAW_ARRAYS` (`0x1810`)
  appends `start..start+count` (count = bits 31-24 + 1), `INLINE_ARRAY`
  (`0x1818`) keeps the raw word stream; `SET_BEGIN_END` (`0x17FC`) resets the
  inline buffers on begin/end (xemu `pgraph_reset_inline_buffers`).
- Vertex array format/offset registers (`0x1760`/`0x1720`) stored per attribute.
- Diagnostics: CLI prints `programLoad`, `constLoad`, `inlineElements`
  alongside the draw stats; `ReadTransformProgramWord`/`ReadTransformConstantWord`
  accessors for tests. 6 tests (`PgraphTransformTests`).

**Next (M5 continued):** implement the NV2A transform-program interpreter
(xemu `vsh-prog.c`: 4-dword instruction decode, input/output register files,
swizzles, write masks, the standard opcodes), vertex attribute fetching from
the vertex arrays (DMA context A/B, format decode per
`NV097_SET_VERTEX_DATA_ARRAY_FORMAT`, stride/offset), then the transform →
clip/screen-space step and feeding the resulting screen-space vertices into
the existing rasterizer. Then M6-M8.

## 38. NV2A transform-program interpreter (M5 continued, 2026-10-10)

`VshProgram` (`src/XGrid.Nv2a/VshProgram.cs`) interprets NV2A vertex programs,
following xemu's field mapping (`glsl/vsh-prog.c`) and register encoding
(`vsh_regs.h`):
- Instruction = four dwords; fields decoded exactly per xemu's
  `field_mapping` (ILU/MAC opcodes, shared V register index and constant index,
  per-input muxes/swizzles/negates, output register/mask/orb, A0X, FINAL).
- MAC opcodes MOV/MUL/ADD/MAD/DP3/DPH/DP4/DST/MIN/MAX/SLT/SGE/ARL and ILU
  opcodes MOV/RCP/RCC/RSQ/EXP/LOG/LIT with xemu's math (broadcast results,
  `ClampAwayZeroInf` for RCC, `floor(x+0.001)` for ARL).
- Register file: R0-R11 temporaries, **R12 aliases oPos** (xemu `#define R12
  oPos`), output registers o0..o15; input registers v0..v15; 192 constants.
- Input muxes PARAM_R/PARAM_V/PARAM_C with the shared `FLD_V`/`FLD_CONST`
  fields; constants mapped through `convert_c_register`
  (`((((c>>5)&7)-3)*32)+(c&31)+96`) with A0-relative addressing when A0X is
  set; the ILU force-scalar rule (RCP..LOG) applies to the shared C input.
- Paired MAC+ILU semantics per xemu: the ILU writes R1, the MAC writes
  R<OUT_R> (mask dropped when OUT_MUX=MAC and OUT_R=1), and only the op
  selected by OUT_MUX writes the output register with OUT_O_MASK.
- Programs leave **screen-space position** in oPos (xemu converts it back to
  clip space for OpenGL; our software rasterizer consumes screen space
  directly). 10 tests (`VshProgramTests`) with a token encoder covering MOV,
  MAD, DP3/DP4, swizzle/negate, write masks, paired MAC+ILU, FINAL, ARL +
  relative constants and ILU-only instructions.

**Next (M5 continued):** vertex attribute fetch (inline `SET_VERTEX_DATA*`
values and DMA vertex arrays with `NV097_SET_VERTEX_DATA_ARRAY_FORMAT` decode:
types UB_D3D/S1/F/UB_OGL/S32K/CMP, sizes, stride), feed inputs into
`VshProgram`, run per vertex for the collected inline elements/draw-arrays,
and rasterize the resulting screen-space triangles (clip/w-divide per xemu's
`roundScreenCoords`/`clampAwayZeroInf` conventions). Then M6-M8.

## 39. NV2A M5: vertex attribute fetch + element draw path (2026-10-10)

**Attribute fetch.** `Nv2aMmio.FetchVertexAttribute(slot, index, dest)` reads
the vertex array for attribute `slot` at element `index`, decoding the format
per xemu (`pgraph.c` `SET_VERTEX_DATA_ARRAY_FORMAT` + `vertex.c`
`pgraph_update_inline_value`):
- `count = (format >> 4) & 0xF`, `stride = format >> 8` (0 = tightly packed),
  `size` by type: UB_D3D/UB_OGL 1, S1/S32K 2, F/CMP 4.
- UB_D3D: four bytes normalized /255 in **BGRA** memory order (xemu binds
  GL_BGRA); UB_OGL: bytes in order; S1: `max(-1, int16/32767)`; F: floats;
  S32K: int16 as float; CMP: 11/11/10-bit signed normalized packed in a dword.
- Address = `vertexArrayOffset[slot] + index * step`; data read through
  `IGpuMemory` (physical RAM), matching the GPU's physical addressing.
- Attributes with `count == 0` are disabled and default to (0,0,0,1).

**Element draw path.** `MethodBeginEnd` end now dispatches through
`FlushDraw`: when `ARRAY_ELEMENT16`/`ARRAY_ELEMENT32`/`DRAW_ARRAYS` collected
elements, `FlushElementDraw` runs — for each element index it fetches the 16
attributes, runs `VshProgram` (program length = `PROG_LD_PTR` after upload,
constants converted from the 192 transform-constant registers), and takes the
screen-space `oPos` plus `oD0` color. Triangles are assembled per the begin
mode (triangles/strip/fan/quads) and filled by `FillTriangle`, the barycentric
rasterizer refactored out of the legacy inline path (which is kept as the
fallback when no elements are pending). Draws are counted; unsupported modes
increment `SkippedDraws`.

**Test:** `PgraphTransformTests.ElementDraw_RunsVertexProgramAndRasterizes` —
sets up a 4-byte pitch surface, an F-format vertex array (stride 0), a
two-instruction program (`MOV oPos, v0; MOV oD0, c0`), constants, elements
0/1/2 and `BEGIN_END` triangles; asserts `DrawsExecuted == 1`,
`DrawnPixels > 0`, and the centroid pixel equals `0xFFFF8040` (the constant
color). 53 NV2A tests total.

**Validation against the real dashboard (2026-10-10).** `xgrid boot --steps
1700000000` (1.7B steps) now reports **`draws executed=2670 skipped=0
pixels=10137600`** (previously the element draws were skipped) and the
framebuffer at physical `0x1000` holds rendered content (685/1024 non-zero
dwords). The run still completes with no fault at EIP `0x14C72`. So the
dashboard's real vertex programs, constants and vertex arrays now transform
and rasterize its geometry.

**Next:** validate the rendered image semantically (compare against xemu's
output or the expected dashboard frame once M6-M8 exist); then M6
(textures/lighting), M7 (zeta/blend), M8 (present); the inline
`SET_VERTEX_DATA*` immediate path and `DRAW_ARRAYS` ranges remain
unimplemented.

## 40. NV2A M6: texture state + sampling (2026-10-10)

**Texture state.** Per-slot (4 slots, 0x40 stride from `0x1B00`) storage of
offset (`0x1B00`), format (`0x1B04`), address/wrap (`0x1B08`), control0
(`0x1B0C`, enable = bit 30), control1 (`0x1B10`, pitch = bits 16-31), filter
(`0x1B14`) and image rect (`0x1B1C`, width = bits 16-31, height = bits 0-15).

**Sampling** (`TrySampleTexture0`): with texture 0 enabled, sample at
`(floor(u*w), floor(v*h))` with wrap/mirror/clamp addressing
(`HcTextureAddress` nibbles) and modulate the interpolated `oD0` by the texel.
Supported formats: `SZ_A8R8G8B8` (`0x06`, Morton-swizzled), `SZ_R5G6B5`
(`0x05`), `LU_IMAGE_A8R8G8B8` (`0x12`, linear with control1 pitch). The swizzle
offset is computed with xemu's mask-generation algorithm
(`swizzle.c` `generate_swizzle_masks` + bit interleave), handling
non-power-of-two dimensions. Texcoords come from output register `oT0`
(`o9`); the program draw path captures `(u,v)` per vertex and `FillTriangle`
interpolates them.

**Test:** `PgraphTransformTests.ElementDraw_SamplesSwizzledTexture` — 2×2
swizzled A8R8G8B8 texture (red/green/blue/white), a program writing
`oPos`/`oD0`/`oT0` from `v0`/`c0`/`v1`, position + texcoord vertex arrays;
asserts the red/green/blue texels appear at the expected framebuffer pixels.

**Validation on the real dashboard (1.7B steps, no fault).** The CLI now
reports `textured=10137600` — **every drawn pixel samples texture 0**. The
framebuffer at phys `0x1000` is byte-identical to the M5 run, and the texture
data at the dashboard's offsets is mostly black (`0x2053000`/`0x3628000` zero
or opaque black; one colored texture at `0x321C000`). This indicates the
dashboard's *visible* textures are **GPU-generated** (render-to-texture or
blits we do not yet produce), so M6 sampling is active but samples black —
consistent with the M6-M8 ordering: M7/M8 (zeta/blend/present and the
surface/blit operations) are needed before the image is meaningful.

**Next:** M7 (zeta test/write + alpha blending), M8 (present/flip path), and
the surface operations that generate the dashboard's textures (render-to-
texture, `NV097_SET_SURFACE_*` targets beyond the color buffer, blits).

## 41. NV2A M7: depth/stencil/alpha/blend fragment pipeline (2026-10-10)

**Method IDs** (from xemu `nv2a_regs.h`, all observed in dashboard traffic):
`0x300` alpha-test enable, `0x304` blend enable, `0x30C` depth-test enable,
`0x32C` stencil-test enable, `0x33C` alpha func (low nibble), `0x340` alpha ref,
`0x344/0x348` blend src/dst factors (raw GL-style codes mapped to indices;
unknown codes keep previous, as xemu discards), `0x34C` blend color (ARGB),
`0x350` blend equation (`0x800A` SUB, `0x800B` RSUB, `0x8006` ADD, `0x8007`
MIN, `0x8008` MAX, signed variants fold to RSUB/ADD per xemu's GL map),
`0x354` depth func (accepted `0x200-0x207`, low 3 bits), `0x358` color mask
(blue bit 0, green bit 8, red bit 16, alpha bit 24), `0x35C` depth mask,
`0x360/0x364/0x368/0x36C` stencil write mask/func/ref/read mask,
`0x370/0x374/0x378` stencil fail/zfail/zpass ops (mapped to internal 1-8),
`0x198` zeta DMA context (record-only; offsets are direct into flat RAM).

**Zeta layout** (xemu `pgraph.c` + `vk/surface-compute.c`): Z16 = 16-bit
normalized depth; Z24S8 = dword `(depth24 << 8) | stencil`. The existing
`NV097_CLEAR_SURFACE` zeta fill already matches this layout, so no clear-path
change was needed. Zeta pitch = high 16 bits of `SET_SURFACE_PITCH`.

**Fragment pipeline** (`ProcessFragment`, GL order): alpha test → nop-draw
early-out (no color write and no depth/stencil test, mirroring xemu
`gl/draw.c`) → stencil test (Z24S8 only; fail applies op-fail and kills) →
depth test (fail applies op-zfail and kills) → stencil zpass op → single
zeta read-modify-write (depth and/or stencil) → blend (all 13 factor codes
with per-channel semantics incl. `SRC_ALPHA_SATURATE` and `CONSTANT_*` via
`BLEND_COLOR`; MIN/MAX on raw src/dst) → color-mask merge → write.
Depth test without a bound zeta buffer (format not Z16/Z24S8, non-pitch type,
or zero zeta pitch) passes through with no access. oPos.z (interpolated) is
the depth value, clamped to [0,1].

**Tests (7, all in `PgraphTransformTests`):** depth kill (LEQUAL vs nearer
zeta, buffer unchanged), depth pass + write (exact `0x40000000` dword),
depth-mask-off (pass without write), alpha blend (`SRC_ALPHA` /
`ONE_MINUS_SRC_ALPHA` → exact `0xBF800080`), color mask (blue-only →
`0xFFFFFF00`), alpha-test NEVER (kills), stencil REPLACE (`0xAB` byte).

**Validation:** 1.7B-step run completes with no fault at EIP `0x14C72`;
`draws=2670 skipped=0 pixels=10137600 textured=10137600 clears=41` (identical
to M5/M6), but `blended=0 depth=0` — investigated in §42.

## 42. M7 validation: two VshProgram bugs fixed; program draws are degenerate (2026-10-10)

**Why `blended=0 depth=0`.** The method stream shows 34 element draws
flushed with blend on and 32 with depth on plus a bound zeta buffer — yet no
fragment ever reached either path. Probing the real 15-instruction program
(active at those draws) with the real constants/vertices from the RAM dump
showed oPos collapsing to a degenerate line (x≈300-302, y=z=0). Root cause
chain, all verified against xemu (`vsh_regs.h`, `glsl/vsh-prog.c`,
`gl/constants.h`):

1. **R/V/C input mux mapping was rotated.** XGrid mapped mux 1→vertex,
   2→constant, 0/3→temp; xemu's `VshParameterType` is `PARAM_UNKNOWN=0,
   PARAM_R=1, PARAM_V=2, PARAM_C=3` (1→temp, 2→vertex, 3→constant). Fixed
   `ReadInput`; fixed the test encoder (`MuxR/MuxV/MuxC` 0/1/2 → 1/2/3) and
   the hand-written draw-test tokens (position input mux 1→2, color input
   mux 2→3).
2. **Output-bank (ORB) polarity was inverted.** XGrid wrote the output
   namespace only when ORB==0; xemu writes `out_reg_name[]`
   (oPos/oD0/oT0/…/A0.x) when `ORB==OUTPUT_O (1)` and asserts on ORB==0
   (const-write TODO). Every real token uses ORB==1. Fixed the o-write gate
   and set bit 11 in all hand-written test tokens (including one missed
   `0x0000F018` → `0x0000F818` that broke the texture test until fixed).
   Also added A0.x write-back to the address register (real programs compute
   A0.x in o15; nothing reads it via A0X yet, but the feedback is
   hardware-faithful).
3. **Unpaired temp writes were already correct** (kept): the 60-word
   program's R3/R4/R6/R7/R8 chain (i7→i11, i8→i10, i10→i12, i11→i13) only
   works if unpaired MAC instructions write R[OUT_R] — confirmed against
   xemu's `decode_opcode` else-branch, which emits the R-write whenever the
   MAC mask is nonzero. Paired-ILU→R1 also kept (i14 reads R1; xemu's comment
   documents it).

**Degenerate draws are driver reality, not an XGrid gap.** After the fixes,
the probe yields the program's true output — still degenerate — because the
program scales by c58 and offsets by c59, and **constants 58/59 are never
loaded in the entire 186844-method run** (verified: indices ever written are
0-107 minus gaps; only c0/c1/c60-c62/c96-c104 are nonzero). Identical stream
+ identical semantics ⇒ identical (empty) result on xemu. All 34 blend draws
and all 32 zeta-buffered depth draws (60-word and 48-word programs, first and
last in the run) are no-ops everywhere.

**Pixel accounting correction (supersedes §39's validation claim).** Exact
analysis: all 10137600 drawn pixels come from **33 legacy-path fullscreen
640×480 inline draws** (33 × 307200; clip/pitch/offset verified per flush;
`textured=` matches because the shared rasterizer samples texel (0,0)).
The ~2637 program draws contribute **exactly 0 pixels** — their geometry is
degenerate at this boot stage. So the M5 element-draw path is implemented,
unit-tested, and runs real traffic without fault, but **live program-draw
rasterization is still unvalidated** — pending a workload whose programs
produce non-degenerate geometry. The M5/M6 framebuffer content stands (from
inline draws), but §39's "transform and rasterize its geometry" applies to
the synthetic tests only.

**Revalidation with fixed interpreter:** 1.7B steps, no fault, EIP
`0x14C72`, counters identical, **RAM byte-identical** (64 MB, hash-matched)
— expected, since program draws contribute 0 pixels and inline draws don't
touch the interpreter. 715 tests green (Nv2a 61).

## 43. NV2A M8: flip tracking + present path (2026-10-10)

**Flip semantics** (xemu `pfifo.c`/`pgraph.c`): `SET_FLIP_READ (0x120)` /
`SET_FLIP_WRITE (0x124)` select the displayed/rendered buffer index,
`SET_FLIP_MODULO (0x128)` the buffer count; `FLIP_INCREMENT_WRITE (0x12C)`
rotates `write = (write+1) % modulo` and bumps `frame_time` — this is the
**present event**; `FLIP_STALL (0x130)` parks the FIFO until `read != write`;
the display side advances read toward write each vsync (`INCREMENT_READ_3D`).

**Dashboard traffic:** read=0, write=1, modulo=2 (each written once), then
33× `FLIP_INCREMENT_WRITE` + 33× `FLIP_STALL` — one flip+vsync-wait per
frame, paired 1:1 with the 33 fullscreen inline draws (§42). All 33 frames
render to the **same** color offset (`0x32A4000`); no buffer ping-pong in
practice.

**Implementation:** `0x12C` rotates the write index (modulo 0 guarded),
counts `FramesPresented`, and captures the at-flip surface record (color
offset, clip w/h, pitch, bytes-per-pixel; valid only for 2/4-byte pitch
surfaces with sane dims); `0x130` counts `FlipStalls`; each 60 Hz vblank in
`Advance` steps read toward write. `TryCopyPresentedFrame` snapshots the
surface as BGRA bytes (A8R8G8B8 direct, R5G6B5 upconverted; fails cleanly on
OOB/unset state). `XGrid.Graphics` gained its first source file:
`DisplayFrame` (`Show` + 32-bit bottom-up BMP export + nonzero-pixel count).
CLI: `--frame <path>` saves the flip-captured frame as BMP; `--fb` (raw
current-surface dump) unchanged. Draw stats now print `frames=`/`stalls=`.

**Validation:** 1.7B steps, no fault, EIP `0x14C72`: `frames=33 stalls=33`
(exact stream-oracle match), final frame `640x480 pitch=2560` from surface
`0x32A4000`, saved BMP hash-verified structurally (BM magic, 1228854 bytes).
Content: every pixel `(0,0,0,255)` — **opaque black**. This clarifies the
display story: the flip chain presents `0x32A4000` (black at this boot
stage), not the `0x1000` region previously eyeballed (early-boot fb with
stale animation content). The all-black presented frame is consistent with
§40 (black textures) — the dashboard is pre-UI / fading in at EIP `0x14C72`.

**Tests:** 6 flip/present tests (`PgraphTransformTests`: wrap counting,
stall counting, vblank read-advance, exact BGRA snapshot incl. R5G6B5,
invalid-surface refusal) + 4 `DisplayFrame` tests (new
`XGrid.Graphics.Tests` project: dims, mismatch rejection, BMP byte layout
incl. bottom-up rows, no-frame error). 725 green (Nv2a 67, Graphics 4).

## 44. Surface-operation scoping + 2.5B-step run (2026-10-10)

**Texture provenance (no felled tree).** The dashboard samples 7 texture
offsets; RAM analysis shows all have plausible asset content — `0x321C000`
blue gradient, `0x3022000`/`0x3066000` near-black fills, `0x2053000`/
`0x3026000` zeros — i.e. **CPU-uploaded dashboard assets, dark by nature**,
not missing GPU output. The M6 sampler reads them correctly. Only
`0x3628000` is both a render target and a texture (true render-to-texture),
and it is black solely because the draws targeting it are degenerate (§42).
**There is no missing blit operation:** the 2D surfaces context (subs 3/4)
is bound once (~line 160824, offsets `0x3BA1000`/`0x2053000`) but no blit
command ever follows (15 non-3D methods total). So "surface operations" as a
work category is real future work (render-to-texture will matter once
programs go live) but blocks nothing today.

**CLI 64-bit steps.** `--steps` used `int.Parse`, overflowing past 2.1B
(`OverflowException` at 2.5B). Widened CLI counters to `ulong` with chunked
`RunBatch` dispatch (`XboxMachine.Run` stays `int`-batched); verified by the
run below reporting exactly `Executed 2500000000 instruction(s)`.

**2.5B-step run: genuine forward progress.** Completes with no fault, not
halted, at **new EIP `0x26402`** (a `ret` site amid thiscall prologues —
translated via CR3 page walk to phys `0x113402`, refined in §45 to a
dashboard-loaded module mapping, not the XBE image). GPU state frozen
(draws/pixels/frames/stalls identical to 1.7B; frame still #33/black) while
the CPU moved on: IDE accesses 5727→8292 with **new ATA READ DMA LBAs**,
interrupts 3110→4566, no new unmapped buckets. The dashboard is in an
**asset-loading phase** (HDD reads, module code), past the black-frame
render loop — the boot continues. Next: map the new READ DMA LBAs to HDD
content to name the phase; continue longer runs; XID input for the eventual
menu.

## 45. Naming the 2.5B load phase: blind strided disk probing (2026-10-10)

**New READ DMA LBAs** (2.5B tail vs 1.7B tail; all `0xC8` ×8 sectors,
master/HDD): three groups strided `0x80000` sectors (256 MB) —
`0x280400…0xC00400` (×20, spanning Y→Z→C→E), `0x7417…0x307417` (×7, X→Y→Z),
`0xE42E…0x20E42E` (×5, X→Y). Partition map confirmed empirically (`sector`
reads: LBA 0 zeros = no MBR; `FATX` superblock at `0x400` = X partition;
prior session: C partition at `0x465400`).

**Content sampled** (new `xgrid sector` command): group 1 all zeros
(unallocated/zeroed clusters, incl. C: `0x480400`); groups 2–3 mixed zeros
and stale high-entropy blobs (`0x7417`: entropy 7.76, no magic). C:\ root
listed via `fatx ls`: `xboxdash.xbe`, `xboxdash2.xbe`, `evox.ini`,
`disk.bin`, `Skin/`, `backup/`. The reads hit unallocated areas, so
**file-driven loading is ruled out for these LBAs** — a blind LBA scan.

**Code context:** EIP `0x26402` is a leaf getter (`mov eax,[ecx]; ret`,
ECX=`0x005E2A10` heap object) called from VA `0x14C48` (`call 0x26400`,
return address on stack confirms); page walks place the caller at phys
`0xE1C4D` and the helper at phys `0x113402` — dashboard-loaded module
mappings distinct from the XBE image (phys `0x5A000`). The caller sits in an
MSVC SEH function with a poll/retry loop (`cmp [0x591AA2],bl; jne`,
handle-or-`-1` wrappers). So the dashboard's main loop is **probing the
disk** (purpose undetermined — content/signature scan across cache
partitions is the leading hypothesis; exact API attribution needs full IDE
logging, future work).

**Also this turn:** CLI counters widened to `ulong` with chunked `RunBatch`
dispatch (`--steps` overflowed past 2.1B); new `xgrid sector` raw-sector
tool. 725 tests green, format clean.

## 45. Phase named: content-discovery scan for menu population (2026-10-10)

**Heap-string forensics settles it.** Diffing RAM 1.7B→2.5B (9 of 64 MB
buckets changed; all GPU regions bit-identical; ~80–270 KB of heap churn at
phys `0x33D000–0x3FC000`) exposes newly arrived strings: `unleashx.xbe`,
`evoxdash.xbe`, `evox.ini`, `disk.bin`, `config.xml`, `Screensavers`,
`Skin`, `backup`, `EvolutionX`, `UnleashX`, plus volume/content labels
`FATX(P)`, `TDATA`, `UDATA`, `CACHE`, `Dash`, `SAVES`. The dashboard is
**enumerating installed content** — alternate dashboards, screensavers,
configs, save/cache volumes — i.e. populating its menu (games/apps/saves
lists). Corroborated by `fatx ls`: C:\ holds `xboxdash.xbe`,
`xboxdash2.xbe`, `evox.ini`, `disk.bin`, `Skin/HeXEn`, `backup/`; X:\ holds
game-like data (`levels/`, `characters/`, `gui/`, …).

**The strided reads stay mechanistically open** but narrowed: blind
`0x80000`-spaced ×8-sector LBAs (not file-driven — unallocated areas),
concurrent with the enumeration, content zeros + stale blobs. Leading
hypotheses: sparse sampling of large content files, or a surface scan;
attribution needs full IDE logging (the `--ide-log` idea stands).

**Code context refined:** EIP `0x26402` is a vtable leaf getter called from
VA `0x14C48` (return address on stack); page walks place caller (phys
`0xE1C4D`) and helper (phys `0x113402`) in dashboard-loaded module mappings
distinct from the XBE image — the caller is an MSVC SEH poll/retry loop
issuing the probes.

## 46. Outrunning the scan: file-save phase at 3.2B (2026-10-10)

**3.2B-step run completes with no fault** (not halted) at **new EIP
`0x67BFD`** (phys `0xD4BFD`, another dashboard-loaded module mapping; VA
`0x10000` now walks to phys `0xDD000`). GPU still frozen (same
2670/10.1M/33/33; frame #33/black). New behavior: **`0xCA` WRITE DMAs** —
`lastCmd=0xCA`, tail shows a repeated 1-sector write at LBA `0x885446`
(E partition, +1.6 GB in) interleaved with ascending 1–3-sector writes
`0x897546`→`0xA07546`. That pattern (same sector rewritten per data chunk)
is a **growing file with directory-entry updates**. IDE 8292→10452
accesses, interrupts 4566→5799, no new unmapped buckets.

**Code context:** EIP sits in a function epilogue just past a file-open
wrapper — `call [0x7D688]` kernel thunk with object-attribute args,
`test eax,eax` error check, bitfield-dispatched custom statuses
(`0x80072749/26/4A/7B`, dashboard-internal, not in Cxbx headers). So the
dashboard moved from content discovery into **saving files** (config/cache/
content DB candidates) — still progressing, no stall.

**False alarm logged:** `OverlayEnabled=False` in every gate file is the
**ROM** overlay (LPC), unrelated to the HDD COW overlay — writes may be
landing in the discarded in-memory overlay. Capturing write content needs
overlay introspection (future tooling alongside full IDE logging).

**Also this turn:** boot prints `Progress …` every 250M instructions
(chunked `RunBatch` loop), so killed long runs still yield partial data;
validated on a 300M run. 725 tests green, format clean.

## 47. File-save identified: dashboard rewriting C:\evox.ini (2026-10-11)

**Tooling built:** `OverlayDiskImage` now journals every sector write
(`WriteLog` in issue order incl. repeats, `WriteCount`, sorted
`GetModifiedSectors()`; `XboxMachine.HddOverlay` exposes the overlay) and
`xgrid boot --writedump <dir>` dumps each dirtied sector as `<LBA8X>.bin`
plus a `manifest.txt` (first-16-bytes preview per sector), printing
coalesced LBA ranges and top repeated-write LBAs. Two unit tests cover
order/repeats/snapshot. 727 tests green (Storage 25→27), format clean;
1M-step smoke run prints `sectors=0 writes=0` correctly.

**3.2B run with capture: EIP `0x67BFD`, no fault, `WRITEDUMP sectors=93
writes=200`, ranges `000008 465409 465488 467548-4675A1`, repeats
`465488x56 465409x4 …`.** Every range decodes (C: partition base
`0x465400`, FATX superblock, 32-sector clusters, narrow u16 FAT of 128
sectors ⇒ ~512 MB = C:, root dir = cluster 1 = LBA `0x465488`):

- `0x465488` (56 writes) = **root-dir sector**; dirent #4 is `evox.ini`
  (start cluster 263), size 79868 (base image) → **45781** (final). All
  other dirents in the sector byte-identical — the 56 flushes track the
  shrinking/growing size field as the file streams.
- `0x465409` (4 writes) = FAT sector (base+9, clusters 256–511). Diff
  base→final: FAT[265] `0x010A`→`0xFFFF`, FAT[266] `0x010B`→`0x0000`,
  FAT[267] `0xFFFF`→`0x0000` — chain **truncated 263→264→265→END**,
  clusters 266–267 freed (neighbour chains `disk.bin` 231–262,
  `xboxdash.xbe` 268+ untouched).
- `0x467548–0x4675A1` (90 sectors) = clusters 263–265 partial:
  `0x465488+(263−1)*32 = 0x467548` exactly; 89 full sectors + 213 bytes.
- Payload is **all-ASCII `evox.ini`, byte-identical to the on-disk file's
  first 45781 bytes**, ending mid-line (`…0xB64C` + `0xFF` pad): `[Misc]`
  (`SkinName=HeXEn`, `ScreenSaver=5`, `DVDPlayer=…`), `[Network]`
  (`Ip=192.168.1.250`, `Subnetmask=255.255.255.0`,
  `Defaultgateway=192.168.1.254`), then the `ROM = "…" ,0x…` BIOS hash
  list (`Complex 4627`, `Cromwell`, `Xecuter2…`). Final dirent size 45781
  = valid byte count. **The dashboard is re-saving `C:\evox.ini` in place
  (truncate + streaming rewrite); the write is still in progress at 3.2B**
  (46 KB of 79,868). Why it re-saves with no input (first-boot settings
  commit vs dirty-flag save) is open.
- `LBA 8`: single early write (absent from the 32-entry `CommandLog`
  tail), 60 nonzero bytes, `55 AA` boot signature at 510–511 and
  `50 43 42 58` (`PCBX`) at `0x1F4` — MBR-signature-like disk system-area
  sector; attribution open.

**§46 correction (LBA radix):** `IdeController.LogCommand` prints
`l{LbaLow:X2}{LbaMid:X2}{LbaHigh:X2}` — raw taskfile bytes concatenated
(`IdeController.cs:85-86`). So tail entry `l885446` = bytes 88 54 46 =
true LBA **`0x465488`** (the root-dir sector above), and `897546…A07546`
= `0x467589…0x4675A1` (the data run). The "E: +1.6 GB" reading was
byte-reversed; all file-save I/O is C:-local (`+0x88` dirent, `+9` FAT,
`+0x2148` data).

## 48. Save completes; runner is a chain-loaded "RemoteX" dash; post-save poll storm (2026-10-11)

**Static detour (all offline, no guest steps).** `C:\xboxdash.xbe` (770048 B,
1 section) is **encrypted on disk**: section entropy 7.98, no ASCII/UTF-16
strings at all — only the entry stub is plaintext (`push 0x12C4B; push
0x80010000; call…`, matching the §34 thunk table). It decrypts itself in
RAM and chain-loads the real dashboard: the 0x5A000 image region is
zeroed by 3.2B and the running image is a **10-section `RemoteX` XBE at
phys `0xDD000`** (base VA `0x10000`, TitleId `0x586F7645`, sections
`.text/D3D/D3DX/XGRPH/XNET/XPP/.rdata`/5.3 MB `.data`), carrying the EvoX
`dash` debug strings (`e:\secure\xbox\source\dash\Release\dash.pdb` leak)
— the EvoX loader-to-dash handoff, not a module.

**Main flow, from its own debug strings (in order):** `Check
LaunchInfo` → `Check Launch msdash` → `Init Hdd` → **`Load q:\evox.ini`**
(Q: drive) → `dash root %s` → `Fill!` → `Start!` → … →
`OpenX_Dash.Create` → `OpenX_Dash.Run`. There is **no save string in the
main flow** and no absolute `imm32` reference to the `q:\evox.ini`
constant anywhere in the image — the path flows through runtime pointers.
So the re-save is triggered **post-`Start!`** (menu `Run` loop or `Fill!`),
not by init; narrowing further needs the save call site, which takes
dynamic work (12 KB chunk buffer at phys `0x401000` holds ROM-section
text — the rewrite streams in ~12 KB chunks; a 16-byte `SkinName=HeXEn`
parse fragment sits at `0x6B8230`).

**4.0B run: save COMPLETE, new phase begins.** Dirent size = **79868**
(full file), 156 data sectors with **zero byte-differences vs the base
ini**, FAT chain re-extended 263→264→265→266→267→END (the §47 "truncate"
was just progressive streaming). Final state: EIP `0x67BFF` (+2 bytes in
800M steps — parked), `lastCmd=0xC8`. Post-save novelties, all absent at
3.2B: **reads of the `disk.bin` region** (`0x4662C8–0x4670F0`, clusters
~243–262); **GPU stirrings** (+48 draws / 4 skipped / 0 px, +2 clears,
**frame #34**); and an **unmapped-MMIO poll storm: ~1.05M accesses in
buckets `F34–F38`** (`0xF3400000–0xF38FFFFF`, nothing mapped there in xemu
either). EIP disassembles as a **data pointer table** (XNET range) — the
sampled thread is parked in (or lost into) data while another thread spins
on the unmapped region. Next gate: instrument unmapped accesses with
(EIP, exact address) to identify the poller + stack forensics on the
parked thread (`--writedump`/`ram_40b.bin` artifacts in temp).

## 49. The "poll storm" is a 2×2 MB Shadow-RAM surface clear (2026-10-11)

**Tooling:** `AddressSpace` now counts unmapped accesses per 4 KB page
(`UnmappedPageReads`/`UnmappedPageWrites`), keeps a monotonic
`UnmappedAccessTotal`, and `PrintState` prints top/all pages (full list
when ≤2048) plus an **EIP trail** (every 250M-step `Progress` line now
carries `EIP=`). 3 tests, 730 green, format clean.

**Deterministic re-run (4.0B) + exact page list settles the shape.** The
storm is **writes-only, two contiguous 2 MB dword fills —
`[F34A2,F36A2)` + `[F36A3,F38A3)` — every one of the 1024 pages hit
exactly 1024 times** (1,048,576 writes; single skipped page `F36A2`).
Not a poll loop: no register is re-read, nothing is ever read back (F34+
reads: zero in 4.0B steps). Per XboxDevWiki Memory (vendored page 22),
`0xF0000000–0xF3FFFFFF` is **Shadow RAM** (64 MB retail); the fill sits
inside it at shadow+52–56 MB, i.e. the shadow of **RAM top, GPU surface
area** (flip surface `0x32A4000` neighborhood), issued right after flip
#34. Reads as: **post-present double-buffer clear, 2×2 MB, through the
uncached shadow alias** (or into the void — XGrid drops the writes).
Corroborating: EIP trail shows **no dominant loop** during the window
(render `0x14Cxx`, net `0x688xx`, save `0x678xx`, module `0x22Dxx`
interleave) — the fill is ~1%-duty background work, i.e. a clear call,
not a spin. No `0xF34…` literal exists in the RemoteX image — the address
is a computed surface pointer. Our NV2A semaphore path already
range-checks bogus targets (drops them); APU `FEMEMDATA` and USB HCCA
writes are guest-register-driven one-shots, not the filler.

**Open (next):** issuer EIP (needs value/size capture or EIP plumbing —
`UnmappedAccessTotal` + interval sampling exists as the hook); **Shadow
RAM semantics** (write-through mirror vs discard — if mirror, XGrid needs
a RAM-alias region at `0xF0000000` and the clear must land in RAM top;
note the map oddity that our `FlashBase = 0xF0000000` (1 MB) overlaps the
wiki's Shadow RAM range — xemu maps nothing there either, but xemu never
reaches this phase so it is no oracle); the `F36A2` gap (guard page vs
two back-to-back 2 MB clears vs scatter-list run boundary).

## 50. Shadow RAM verdict: tiled/write-combined mirror — alias implemented (2026-10-11)

**Offline evidence, no guest steps.** Cxbx `AddressRanges.h` (kernel-header
reference, vendored) is definitive: `TILED_MEMORY_BASE = 0xF0000000` (64
MB retail, matching the wiki's Shadow RAM column) with the comment
*"WC (The WC memory is another name of the tiled memory)"*; debug-size
128 MB (`XBOX_WRITE_COMBINED_*`) matches the wiki's Debug/Chihiro column.
xemu maps nothing at `0xF0000000` (and deadlocks long before this phase —
no oracle either way). Verdict: **mirror, not discard** — the 4 MB fill
lands in RAM top on hardware, and XGrid was diverging by dropping it.

**Implementation (small, fully tested).** `XboxAddressMap.WriteCombinedBase/
Size` (`0xF0000000`/`0x04000000`) + a `RamAliasDevice` region ("RAM
write-combined") registered in the `XboxMachine` constructor. Overlap
with the 1 MB flash window at the same base is resolved by registration
tie-order: the constructor alias registers first, `LoadBios` flash later,
so flash keeps precedence over `F00–F0F` (all existing flash tests pass
unchanged) while `F10–F3F` newly mirrors RAM. The 4.0B unmapped histograms
prove no pre-fill code reads `F10–F3F` (reads there: zero), so boot
behavior through the fill is bit-identical; 1M-step smoke confirms
(same EIP/buckets). 732 tests green (Integration 11→13). Next long run
will show the fill landing in RAM top (`0x34A2000+`) — watch whether
anything downstream reads it back.

**Still open:** exact hardware priority inside `F00–F0F` (flash-first is
our modeling choice, preserves 4.0B-validated boot); fill issuer EIP;
`F36A2` gap; LBA 8 attribution.

## 51. Alias verified live; menu-idle state with NIC traffic (2026-10-11)

**5.0B run (WC alias live), no fault.** Alias interception proven by the
dog that didn't bark: `F34–F38` unmapped counts drop to ~0 (only
`045=4 065=8 FEF` remain) while everything else stays deterministic
(IDE 12132, writedump 159/352 identical, no new disk I/O). The fill
landed in RAM top — and it is **surface DATA, not a clear**: the region
is now 59% BGRA pixels (`97512CFF`-style, alpha `FF`) vs 99% stale blobs
pre-alias, zeros clustered in bands (64 fully-zero pages incl. a 13-page
gutter straddling the `F36A2` boundary). Single-variable attribution
(alias on/off is the only delta). §49 "clear" framing corrected: a
4 MB surface/texture upload through the shadow alias.

**System looks menu-idle, waiting.** End EIP `0x800153D3` (stable
500M+ steps) disassembles as a kernel delay loop (`sub eax,1; jne` +
alignment NOPs, `rep ins` port-I/O helpers nearby — KeStall-style
busy-wait). The save thread revisited `0x67BFD` at 4.5B with no new
writes (parked wait loop). GPU: +384 draws (32 skipped, 0 px), +8 flips
(frame #42, still black, surface moved `0x32A4000`→`0x3D04000`).
**NIC traffic is new:** `FEF` reads 4652→14815, writes 0→704 in 4–5B —
the network stack configuring/polling a config-space-only stub
(`NvNetController`: PCI 0:4.0 BARs only, no MMIO), reads returning
sentinel `0xFF`. xemu reference exists for the future gate
(`mcpx/nvnet/nvnet.c` + `nvnet_regs.h`).

**Next:** NIC register identification + link-status design (what is it
polling — link? DHCP? — and does the wait gate UI?), XID gamepad input,
then a menu-response test (does the "idle menu" react?). The black
degenerate draws remain unvalidated as menu rendering vs no-content.

## 52. NIC gate I: driver init loops decoded, minimal nvnet device live (2026-10-11)

**Register ID (static, ram_50b.bin).** 137 `0xFEF00xxx` immediates cluster
in kernel driver code (0x5C3xx + copies at 0x9Cxxx/0xBBxxx); hottest:
`0x144` (×20), `0x000` (×15), `0x004`/`0x180`, `0x110`/`0x188`, `0x080…`.
Disassembly at `0x8005C39F` is the driver's start function: zero
`0x110/0x094/0x084`, then two bounded (500×) `KeStall(10)` retry loops —
wait TX/RX STATUS busy-clear (`0x088`/`0x098`), poke `0x144`=4, wait
`0x144` IDLE-bit-set — then clear `0x144`. Cross-ref with xemu
`nvnet_regs.h`: `0x084/0x088` TX control/status, `0x094/0x098` RX,
`0x110` LINKSPEED, `0x144` TX_RX_CONTROL (KICK/IDLE/RESET),
`0x180` MII_STATUS, `0x188` ADAPTER_CONTROL (LINKUP), `0x190/0x194`
MDIO. With sentinel `0xFF` the busy bits read SET, so loop 1 always burns
its 500 stalls (the init churn: ~30 re-inits in 4–5B, 14.8K reads).

**Minimal device (`XGrid.Bus/Net/McpxNvNetDevice`, wired at `0xFEF00000`
+ `XboxAddressMap.NvNetBase/Size`, CLI `NVNET` line).** xemu-faithful
power-on/link-up-steady-state defaults (as with xemu's default always-up
link — and sentinel already showed the driver LINKUP=1, so trajectory is
preserved, only the stall-burn is removed): TX/RX STATUS forced idle,
`0x144` IDLE-sticky + BIT1-clears-IRQ + write-0-sets-REG5-bit31 quirks,
IRQ/MII W1C (never raised — no backend), LINKUP set, instant MDIO with a
tiny PHY file (BMSR link-up per xemu `phy_reg_init`), latches elsewhere,
qemu-default MAC. No DMA/packet engine, no IRQ wiring yet. 10 unit tests
(Bus 110→120); 742 green, format clean, 1M-step smoke identical. **Not
yet behaviorally validated** (NIC traffic is post-4B): next long run
should show FEF unmapped collapse + faster init; then DHCP-without-link
timeouts are the following question, then XID input for the menu test.

## 53. NIC gate II: validation run — FEF collapse, dashboard goes live (2026-10-11)

**5.0B run with the §52 device live (~26 min, no fault, vec=0).**
Unmapped `FEF` collapses to zero (`045=4 065=8` remain); `NVNET
reads=44 writes=350` (was 14815/704 — busy-loops exit on first read,
init succeeds first try). CLI gained `USB0/USB1` lines (`acc`, `irq`,
raw port regs; +1 Bus test).

**The trajectory advanced past the menu-idle park.** With no 500-stall
burns the system enters a live dashboard render loop: draws 806958
(skipped 33610), flips/stalls 16789, clears 16798, semaphore releases
269574 — ~48 draws + clear + flip per frame — while pixels stay frozen
at 10137600 (every new draw is a zero-pixel degenerate program draw, as
§42 predicts). End EIP `0x2D42C` with zeroed data regs disassembles
clean (VA+0xEE000 → phys `0x11C42C`: a `cmp cl,0x0C/0x48` byte-dispatch
in the §36 dashboard module) — real running code, not a kernel park.
Frame #16789 is still black (surface `0x3E2C000`, nonzero=0).

**Rest of the system corroborates "alive and waiting".** Disk save
footprint identical (159 sectors, ranges through `4675E3`, writes
353 vs 352 — one extra rewrite). IDE 11060 (was 12132, faster init).
`USB0 acc=31` — first guest USB traffic past early boot — but ports
never powered (all port regs 0, USB1 silent). SMBus 1689 txns cycle
the `0x4C` sensor (cmds 01/10/08/00, all NAK — thermal-poll loop, new
observation with no baseline yet).

**Read:** the dashboard runs its main loop with no content and no
input — a waiting menu. The black degenerate draws are now confirmed
as the menu's steady state, not a park artifact. Next gate is XID:
the guest probes USB0 but our root hub has nothing to offer, so menu
response needs a gamepad device behind the OHCI (ED/TD transfer path,
currently unimplemented — SOF/HCCA only). Reference-first: xemu USB
passthrough + Cxbx XID to scope it.

## 54. XID gate I: OHCI schedule engine + gamepad function, unit-verified (2026-10-11)

**Reference facts extracted (no code copied).** Cxbx `XidGamepad.cpp`:
VID `0x045E`/PID `0x0202`, XID interface class `0x58`/subclass `0x42`,
IN+OUT ep2 interrupt (32 B, interval 4), 20-B input / 6-B output reports,
16-B XID descriptor (`42`, caps 20/6), request matrix (standard
descriptors/SET_ADDRESS/SET_CONFIGURATION/GET_STATUS/FEATURE/INTERFACE;
class GET_REPORT `0x0100` with oversize→STALL, SET_REPORT `0x0200`
exact-length; vendor XID descriptor `0x4200`, GET_CAPABILITIES
`0x0100/0x0200`; real-pad quirks noted). xemu `hcd-ohci.c`: ED/TD
layouts + bitfields, direction resolution (ED.D else TD.DP), page-split
lengths, CBP advance with `CBP=0`-on-complete, toggle carry, retire
(ED.HeadP:=TD.Next, done-queue link, DI minimum, error→0+H), NAK defer,
periodic via `HCCA.intr[frame&31]`, control iff CLE+CLF (clear on idle),
done writeback with WDH + toggle bit.

**Implementation (`XGrid.Bus/Usb/`, clean-room).** `XidGamepad`
(`IUsbDeviceFunction`: setup matrix, deferred SET_ADDRESS/CONFIG commit
on status stage, neutral report + `SetInput` for later injection, rumble
accepted/dropped) attached to USB0 port 0 (CCS on power, reset→enable);
`OhciUsbController` grew a synchronous schedule engine (control/bulk/
periodic walkers, link-limit 32, 512-B cap, general TDs only — no ISO;
STALL+halt on bad requests/addresses; NAK defers; done_count/WDH/HcDoneHead
per xemu). `IUsbMemory` gained reads (`PhysicalUsbMemory` passes
through); CLI USB lines gained `ctl/intr/addr`. 17 tests (Bus 121→138):
programmed ED/TD GET_DESCRIPTOR lands bytes with CC=0 + WDH, address
assign, STALL-halt, periodic IN delivers the report, port sequence.
760 green, format clean, 1M smoke identical. **Guest validation pending:**
USB traffic is post-4B, so a 5.0B run with the neutral pad attached is
queued — expect enumeration (address assigned, config set, interrupt
polling starts) in `ctl/intr/addr` + no-fault + same render loop. Button
injection (`SetInput` wired to CLI) and the menu-response read-out are
gate II, after seeing what the guest does with a pad.

## 61. Viewport writers: app sane, driver bakes junk — guest-side (2026-10-11)

**The fork is decided.** RAM holds exactly one 640×480 D3DVIEWPORT8
(phys `0x154670`) with **MinZ=0.0, MaxZ=1.0 — sane**. Yet the
pushbuffer carries viewport-Z 16.7M from the first viewport write, for
both GPU clients, all 5B steps. The corruption happens between the
app's SetViewport and the pushbuffer: inside the D3D runtime as
faithfully executed. App-level junk is out.

**Emulator channels exonerated (audit).** Surface/zeta/format state is
guest-write passthrough (nothing for the driver to misread — and
NV2A methods are guest-authored verbatim in the 463M-method stream);
there is no GPU readback path feeding driver math; PFIFO capture is
complete (no suspends/caps hit); the 16.7M repeats identically across
486K batches, runs and phases (corruption/DMA-misread would be chaotic,
and int math is exact in our CPU). The driver really emits Z24MAX from
MaxZ=1.0 on this configuration — deterministically, from boot.

**Position.** The guest AS OBSERVED draws nothing visible,
deterministically, from the first frame: real app viewport, junk
driver bake, total clip, black flips. No GPU-fidelity item remains on
this path. What is left is APP-INTENT: is the black idle scene the
correct waiting state (for input? network? content? timer?), or is the
driver bake itself symptom of an earlier missed trigger (XBApp flow:
does init complete; what gates content)? Next: XBApp-flow evidence
(EIP-loop structure at higher resolution than 250M samples? D3D-object
forensics? failure-string execution proof?) alongside the nvnet packet
engine (the other half of "waiting for what").

## 60. Zeta verdict: set at boot, never after; logo-vs-scene split (2026-10-11)

**Full-stream surface timeline** (single pass, 463M methods): `0x214`
zeta IS set — 78 writes, all within the first ~187K methods
(`0x33D0000`×36 with color `0x3628000`, `0`×33 with `0x32A4000`,
`0x2813000`×6 with `0x2853000/0x2893000`, `0x3BD4000` last) — then
never again; color `0x210` flips the `0x3D00000↔0x3E2C000` pair to the
last line (16.7K ≈ frame count); format/pitch/`0x290` also boot-only.
Program/const uploads run all file long at uniform cadence
(1E9C ~486K, 1EA4 ~494K, 1EA0 ~251K, all slot/start 0). So: genuine
device bring-up with depth at boot (several surface configs), then a
steady color-only present loop. Zeta absence can't explain the clip
(clip precedes depth), and M-fidelity stands.

**Logo-vs-scene split hypothesis.** The only pixels ever drawn (10.1M)
come from 33 legacy fullscreen TEXTURED inline draws — plausibly the
visible boot logo — while all 800K program draws (real scene @
`0x389C000`, real shaders) clip on `C0.z`. I.e. the animation you see
and the scene you don't are different draw populations; the menu may
be black because its scene-graph draws through the same poisoned
viewport, not because the app is stuck.

**Remaining fork, sharpened.** Both GPU clients emit viewport-Z 16.7M
from frame one — either the app sets it (both clients broken the same
way?) or the driver bakes it from emulated state (surface/zeta/caps
query we misreport). Next probes: (a) find the app's D3DVIEWPORT8 in
RAM and x-ref its writers (who stores 16.7M?); (b) audit OUR
surface/zeta/caps responses at bake time; (c) EIP-anchored SetViewport
trace. nvnet engine stays last.

## 59. C0 provenance: viewport-baked junk from the first upload (2026-10-11)

**Cap semantics corrected.** `MethodStream` keeps the FIRST 262144
methods, not a tail — gate61/62 files are byte-identical, and §57's
"menu-idle tail" was the boot window (the steady-frames finding stands,
re-attributed to the animation loop; menu-idle differs: denser inline
draws, slot-0-only constants, ongoing uploads). Added `--pgraph-max`
(0 = uncap) + streaming dump (the StringBuilder OOM'd on 463M methods).

**Full 5B stream (463M methods, 1.68M BEGIN_ENDs ≈ engine draw counts,
462 fixed methods):** `C0=(1,1,16777215,1)` from the first const batch
(line ~462) through the last (line ~463M), 486K batches sampled
head→tail identical — junk-from-start, zero drift, no sane phase ever.

**Source found one level up.** The init sequence writes viewport params
first: `0AF0=320.0, 0AF4=-240.0, 0AF8=4B7FFFFF(16777215.0)` +
`0398=4B7FFFFF`, i.e. the 16.7M enters through the VIEWPORT depth
entry, then surface setup (`0x210=0x32A4000`, zeta `0x214=0`), then the
const batches bake it into C0. Menu-idle re-emits the identical
viewport while double-buffering `0x3D00000↔0x3E2C000`. So C0 is
viewport-derived, and the viewport Z is insane from the first frame.

**Hottest lead (untested): zeta is never set** (`0x214=0` at init,
absent from the 6MB end window) — if D3D device creation failed
(XBApp string!), no depth buffer exists and the fixed-function
viewport bake may default Z scale to Z24MAX-as-float. Next: (a) does
any phase ever set a zeta surface (full-file `0x214` scan or CLI
line); (b) D3D device-creation probe (called? where fails?);
(c) app viewport-struct forensics. Only then the nvnet engine.

## 58. Indexed-draw trace: oPos verdict — proven total clip, guest-side (2026-10-11)

**Array state + buffers (all offline python over the §57 stream +
ram_60_5b.bin, no repo changes).** Steady-state arrays: one interleaved
f32 buffer at `0x389C000` (stride 32: pos×3 @0, v2×3 @12, tex×2 @24);
buffer holds a REAL scene (positions ±4–8, y∈{0,1}, 73% nonzero) —
indexed geometry is genuine, not degenerate.

**Full program decode (bit-exact vs our `VshProgram` layout).** The
re-uploaded 11–14-insn program: `R1=V0`; `R2=R1*C0` (B-mux is const,
raw 0); `oPos=R2+C0`; varying passthrough; dead SGE/LIT after FINAL.
So `oPos = V0*C0 + C0` with stable `C0=(1,1,16777215,1)` (195 identical
batches): z/w ≈ ±50M for every real vertex → **every triangle fails
depth clip; zero pixels proven independent of vertex inputs.** Our
engine computes the identical math (same clip) — M5–M7 fidelity fully
exonerated for this traffic; the 1818-inline draws collapse the same
way (zero verts → single point at C0).

**Open (narrowed to one word).** Why is `C0.z=16777215`? App
junk-state (waiting menu re-issuing stale blocks) vs driver miscompute
under emulation is undecided; skin content is present
(`Skin/HeXEn`: main/load.bmp + skin.ini), so not missing-skin; whether
boot-time C0 was ever sane needs an early-stream capture. Next: (a) C0
provenance, (b) XBApp flow evidence, (c) nvnet packet engine last.

## 57. D3D probe: steady no-op draws — real shaders, degenerate geometry (2026-10-11)

**Method stream captured** (`--pgraph`, 5B run; capped at 262144
entries so the file IS the boot window — **correction (§59): the cap
keeps the FIRST methods, not a tail; gate61/62 files are byte-identical.
The ~150 identical frames below are the animation loop, not menu-idle;
menu-idle differs (denser draws, slot-0-only constants)** — no bursts,
no surface creation, i.e. a steady loop, not init churn). Artifacts in
temp (`pgraph_61_5b.txt`, `vp_tail.json`), not the repo. Per frame: transform-program re-uploads
(7–11 instruction fixed-function-style shaders: `R1=V0`, perspective
RCP, `R2=R1*X`, `oPos=R2+C0`, varying passthrough, FINAL-terminated),
constant batches (slots 0 with `C0=[1,1,16777215,1]` + slots 96–104 via
the 1EA4 load pointer), texture state, indexed (ARRAY_ELEMENT16) +
inline-array geometry. START=0/LOAD=0 always; the 12 boot programs are
never selected in the tail.

**Zero pixels explained, no emulator gap found.** The in-stream
geometry (INLINE_ARRAY) is zeros/denormals: every vertex identical, so
oPos collapses to a point (`C0` offset) — zero-area, correctly zero
pixels. Our interpreter/rasterizer faithfully reproduces this (same math
→ same nothing). The indexed path references RAM arrays (16-bit indices
8–22, 380+ — normal-looking) and still needs RAM-backed tracing to
close fully, but nothing so far implicates M5–M7 fidelity: programs
decode cleanly, constants land, no unknown methods/opcodes in 462
distinct methods.

**Verdict matrix.** The flip producer is the dashboard's own D3D layer
issuing fully-formed but content-free draws (EIP samples span dash
.text + D3D/D3DX libs + kernel waits — a running-D3D-app profile).
Black frames + degenerate geometry = the menu genuinely has nothing to
show. Init-retry (XBApp `Could not create D3D device!`) vs waiting-menu
(network/pad/content-gated) stays open, but the evidence pool now
favors *waiting*: perfectly periodic identical frames with zero error
churn. Next: (a) RAM-backed trace of one indexed draw (array
format/offset methods + buffers → execute → oPos verdict); (b) XBApp
flow evidence (EIP-loop analysis or D3D-device-object forensics);
(c) only then the nvnet packet engine if the dash proves
network-gated. The string-anchored call-site forensics stalled on heap
residence (message VAs aren't image-relative — the 0xEE2E3 block sits
outside every loaded image).

## 56. XID gate III: injection works, guest ignores the pad (2026-10-11)

**Input path built end-to-end.** `--pad-buttons/--pad-analog/--pad-sticks`
CLI flags → `PadInput.TryParse` → `XidGamepad.SetInput`, latched across
port resets (enumeration resets no longer wipe held buttons). 4 tests
(Bus 144→148); 768 green; smoke confirms the Pad line + CCS from boot.

**A-press 5B run: byte-identical to neutral.** Same EIP `0x2D3B9`, same
draws 806958 / frames 16789, same USB (`acc=133 ctl=10 addr=1 cfg=True
intr=0`), same black frame from `0x3E2C000`. Our side delivers (device
configured, reports carry A=`FF` whenever polled); the guest never
polls — no periodic EDs, no GET_REPORT, no state delta. The blocker is
not delivery but the title never opening XInput.

**Triage for next (three forks, D3D-first favored).** (1) The dash also
prints `Could not create D3D device!`, and every observed draw is
degenerate with black flips — consistent with XBApp stuck in an
init-retry loop where graphics (not input) is the Blocker, the flips
coming from an orphaned context. Probe: `--pgraph` method mix (D3D-create
signature vs loader leftovers), identify who flips. (2) XInput-open
forensics: once-vs-loop for `CreateGamepads` (thunk/call-site analysis
in the dashboard image; note the strings are statically present, not
proof of execution). (3) Network pivot: RemoteX runs telnet/FTP/RDTOOLS
servers with a static IP — if the UI is remote, interaction needs the
nvnet packet engine (TX/RX rings + host networking), a much bigger gate.
Do (1) first: it decides whether input was ever the right gate.

## 55. XID gate II: enumeration completes — gamepad configured (2026-10-11)

**Three-iteration diagnosis, all offline-analyzed from CLI logs.** (1) The
guest looped a byte-identical re-enumeration (GET_DESC8 → SET_ADDRESS →
GET_CONFIG(80) → port reset) with `ctl` frozen and `cfg=False`. (2) TD
logging exposed the chunking: setup(80) + data#1(64, no Rounding) +
data#2(16, Rounding set) + status. data#1 shorted 32/64; strict OHCI
(DataUnderrun + halt, xemu-faithful) made the guest reset. A short-IN
leniency probe completed nothing — the guest still reset — killing the
underrun theory. (3) Frame stamps showed data#2 STALLing (`available=0`
→ STALL): with Rounding EXPLICITLY set the stack expects an empty short
packet, not a stall. Fixing that (zero-length success when Rounding is
set; still STALL without it) plus a port-reset→function-reset fix (bus
reset returns the device to address 0 — without it the guest's recovery
re-enumeration STALLed on a stale address) completed enumeration on the
next run: **one clean pass (11 TDs), `addr=1 cfg=True`, zero resets.**

**Strictness vindicated.** A follow-up run removed the leniency probe
AND added the one xemu line I had missed (CBP advances by transferred
bytes even on error retirements — the stack reads back CBP for the
actual length). Enumeration still completes (10 TDs: underrun noted,
guest un-halts, skips to status, configures). Final engine is
xemu-faithful with zero deviations; 764 green.

**Static bonus (ram_58_5b.bin):** the runner is an XBApp-framework dash
(EvoX skin strings, FTP/telnet/RDTOOLS servers, static IP 192.168.0.2)
that prints `Call to CreateGamepads() failed!` — the title wants the
pad. But `intr=0` at 5B: nothing programs interrupt EDs yet, so no
XInput polling. Gate III: `--pad` button injection + response test
(polling starts? XInput RAM state? EIP/draws change?) — or the dash may
be network-driven (RemoteX remote shell), in which case the nvnet packet
engine becomes the gate. The `CreateGamepads` retry behavior (once vs
loop) is the first thing to establish.

## 62. Menu-idle loop decoded: state machine + hot memcpy (2026-10-11)

**Runner carved from RAM (offline, ram_60_5b.bin).** Guest page walk
(CR3=`0xF000`, PDE[0]=`0x15D067` → PT @ phys `0x15D000`) translates the
menu-idle EIP trail: the running image sits at VA `0x10000` → phys
`0x13E000` (moved since §48's `0xDD000`; `XBEH` header present, base
`0x10000`, 10 sections, entry(enc) `0x29F53`, cert `0x10178`). All 10
sections carved from loaded VAs (raw addrs are file offsets): `.text`
`0x11000`/`0x24ACC`, `D3D` `0x35AE0`/`0x124A8`, `D3DX` `0x47FA0`/`0x1C778`,
`XGRPH`, `XNET` `0x66880`/`0xE858`, `XPP`, `.rdata`, `.data`
`0x836E0`/`0x5121B8` (5.3 MB ✓), `_text`, `text`. Artifacts in temp
(`runner/*.bin`, `text.asm`), not the repo.

**250M-trail decode (all real code).** Final EIP `0x2D3B9` = inside an
idiv helper (`0x2D3B0`: `push esi`, null-test, `div ecx`; 11 callers);
`0x2EA10` = UTF-16 tolower loop (`cmp A/Z`, `+0x20`; multi-sampled);
`0x2DE26` = wcslen (7 callers); `0x254C0` = COM-style accessor
(`[esi+0x48/0x38]` null-check + call; 7 callers); `0x320C7` = string
scan; `0x181EB` = FPU spill code; `0x3Fxxx`/`0x48CA7` = D3D/D3DX
sections. Profile = D3D + string processing + FPU: a working loop, not
a stall.

**D3D frame function (~`0x3E200`, state dispatch).** FPU prologue, a
div/mod global (`0x94B58`), flag checks (`0x47BC59`, `0xCE9F8`), then
`[esi+0x280]` compared against `0x64` → byte table `0x1EE98` → jump
table `0x1EE50`: ~19 distinct handlers + default `0x1E72B` (most states,
incl. 25–99). Handler anatomy: `[esi+0x280]` = state (ebx = next),
`[esi+0x284]` = sub-state, `[esi+0xFC0]` = mode fnptr (states
4/100/1/13/33 install `0x47BC60/A8/F0/CC…`). State 10 (`0x1E33A`) =
debug-dump path (file-op calls + `e:\dump.txt` via D3DX
`0x2CD56/0x2CD69`); state 9 = sub `0x19` + init call. No `int3` padding
in range — one large function.

**XBApp string block (heap).** Phys `0xEE000` → VA `0x80000` holds the
Creating…/failed! pairs (`CreateGamepads`, `D3D device`, `Direct3D`) +
menu/skin strings (`SkinName`, `MenuColor*`, `q:\skin`, `Skin_%s`,
`screendump.bmp`, `e:\dump.txt`, `d:\default.xbe`) + server strings
(`Start FTP/Debug`, `XBApp:`). 9 code xrefs (`push`/`mov reg,imm32`)
from D3D/XNET/XPP — live references, but execution of the failure
branches is unproven (heap-resident, no image-relative anchors).

**gate66 `--eip-hist` (new CLI probe, 5000 samples over 5B).**
`EipHistogram` (`XGrid.Core/Diagnostics`, 3 tests) samples EIP every N
instructions between batches; prints top-40. Result: kernel idle
`0x800422Bx` 7%, then a **D3D-section MMX streaming-memcpy loop
`0x39F90` ~10%** (`movq`/`movntq` 64 B blocks + `prefetchnta`, `dec
ecx/jne`, then `call 0x3C880`), wcslen cluster (`0x2DE2A+`), tolower
cluster (`0x2DC49+`). The loop copies heavily every frame while issuing
periodic identical draws/flips. IDE shows reads only (`0xC8`, no `0xCA`
writes) → no per-frame dump writes; memcpy targets RAM (doomed
surfaces? audio? scratch — open).

**Forensics limit hit.** Kernel exports are ordinal-only (no
`KeWaitForSingleObject`-style names in the image), so import-based wait
classification needs the Cxbx 4627 ordinal map — deferred. Current
state value needs the menu object (`esi`); handler-level attribution is
the next probe (`--break-log` on handler VAs or finer hist).

**gate67 verdict (NVNET regs, 5B, no fault).** Per-reg histograms
(`McpxNvNetDevice.RegReadCounts/RegWriteCounts` + CLI `NVNET regs:`,
2 tests): reads=44 (`190`=22 MDIO_ADDR, `194`=10 MDIO_DATA, the rest
2× status/IRQ/link), writes=350 (`144`=40 TxRxControl init dance,
`190`=12 MDIO, `188/184/00/110/94/84` ≤4 each). **Zero accesses to
ring regs `0x100`–`0x138`, zero KICK traffic, zero status polling.**
The guest initializes the NIC and never drives it — the packet engine
stays scoped-not-built per the trace-driven rule. There is no network
half to "waiting for what": init-only NIC, unpolled pad, present
content, advancing timer. What remains is the state machine itself —
current state value + wait primitive (Cxbx ordinal map deferred).

## 63. App-intent closed: init succeeds, menu parked in state 0 (2026-10-11)

**Method correction (glob dotfile trap).** Python `glob('*.bin')`
skipped the dotfile carves (`.text/.rdata/.data`) - the §62 "9 xrefs"
and "63 thunks" censuses covered only 7 sections. Redone with
`os.listdir`: the six XBApp creation/failure strings ARE referenced,
all from `.text` (`0x1D952`-`0x1DA29`). Lesson: never trust glob counts
on this dir; the §62 census numbers are superseded here.

**XBApp init decoded (`.text 0x1D920`, thiscall, SEH).** Print
"Creating Direct3D" -> `0x3AEF0(0)` (D3D object -> `[esi+0x48]`, NULL ->
"Unable to create Direct3D", E_FAIL) -> print "Creating D3D device" ->
`call 0x35AE0` (**D3D section entry = CreateDevice**) -> HRESULT<0 ->
"Could not create D3D device!" -> stores + `0x37D40/0x379C0/0x7698B` ->
print "Creating gamepads" -> `call 0x26490` -> <0 -> "Call to
CreateGamepads() failed!" -> success path: `0x23D70/0x23F70`,
`0x25250`, `0x374C0`, then `0x395D0(0x280,0x1E0,...)` = **640x480
window**, `0x396A0`, D3DX `0x483A6`, `[esi+0x1F4]=1`. Single caller
(`.text 0x24B4B`); single-shot, no retry. Thunk census (corrected, all
sections): 63+ kernel imports, XNET-heavy (`0x80014368`x66,
`0x80014348`x50 - string/mem routines by context).

**gate70: INIT SUCCEEDS (dynamic proof).** `--break-log 0x1DA49`
(gamepad-success continuation): `HIT 645040997 ESI=D003B500` - D3D
device created AND gamepads created at step ~645M. Both failure
theories dead. ESI=`0xD003B500` = the menu object (stable across runs:
gate65 final ESI identical).

**Current state = 0.** `[ESI+0x280]` in ram_66_5b.bin = `0` (sub-state
`+0x284` = 0, flag `+0x27C` = 1). State 0 -> handler `0x1E77F`
(object housekeeping). The menu never leaves state 0 through 5B.

**Dispatch twins resolved.** `.text 0x1E2F9` is the real dispatcher
(aligned function context, `je`-in at `0x1E2EE`); the D3D-section
`0x3E2F9` is a byte-identical dead copy - **gate69 (`--break-log
0x3E2FF`, full 5B single-step, ~15 min) recorded ZERO hits.**
(Side finding: single-step 5B is practical, ~15-20 min.)

**gate71: first dispatch @ step 816,778,581** (`--break 0x1E2FF`).
Init success (645M) -> dispatch live (817M) -> content scan (2.5B) ->
save (3.2B) -> menu-idle (5B, state 0). The loop has run ~16.7k
dispatches; handlers are thin vs the memcpy/string bulk, hence absent
from the top-40 hist.

**Verdict: no emulation blocker remains on this path.** D3D device:
created. Gamepads: created. Input polling: never attempted by the
title (idle screen reads nothing - A-press identical). Network: init-
only. Content: present. Failures: none executed. The black idle scene
is the CORRECT steady state of state 0 (renders nothing; what it does
draw clips on the guest-baked C0.z, §§57-61). State advance needs input
events the title never samples - title logic, not a device gap.

**Next.** (a) What moves 0->N (deeper input-event paths? submenu
states 200-299 via default handler?) - needs a title that consumes;
(b) visible pixels need state advance or the viewport-bake root cause
(guest driver behavior, out of emulator scope); (c) Track C JIT for
interactivity. Open micro-items unchanged: issuer EIP, `F36A2`, LBA 8.

## 64. Game media readable: XDVDFS via XISOSharp (2026-10-11)

**Retail layout (007 Agent Under Fire, Redump).** Sectors 0+ are an
ISO9660 DVD-video partition (`CD001` @16, `VIDEO_TS`, 6992 sectors),
then zeros; the game area starts at file offset `0x18310000`
(sector 198176), volume descriptor at +32 sectors with magic at 0x00
and 0x7EC (the XGD-family fallback pattern from cxbx.md §6, shifted
`+0x10000` vs the 360 value). XISOSharp auto-detects all of this
(`Volume.IsValid`, `DescriptorSector=32`, `DiscLseek=405798912`).

**Integration (no source copied).** NuGet `XISOSharp` 1.4.2, centrally
pinned, consumed by `XGrid.XboxFs` through a new `XdvdfsFileSystem`
adapter (Open/ListDirectory/ReadFile + Entry, mirroring
`FatxFileSystem`; `\`→`/` normalization; `XisoExplorer` keep-open
mode; `XisoFormatException`→`InvalidDataException`). CLI `xgrid xdvdfs
ls|cat --image <iso> [--path] [--out]`. 8 unit tests over a hand-built
37-sector synthetic image (descriptor + AVL tree + subdir + content),
which independently validates the on-disk facts (u16 l/r in dwords,
u32 sector/size, attrs, Latin-1 names, pad-to-4, 0xFFFF terminator).

**Validation.** `xdvdfs ls` on 007: `default.xbe` 696320 B, `bond.xbe`,
`Driving.xbe`, `Assets/`, `driving.dat`. Extracted `default.xbe`
parses via `xbe info`: Retail, base `0x10000`, TitleId `0x4541000D`,
Media `0x2` (DVD_X2, pressed), 9 sections, 5 libraries, TLS present.

**Correction.** `CSharp_XISOSharp` is NOT vendored under `References2/`
(AGENTS.md + plan §13.5 wording was wrong) - it lives as the sibling
repo `C:/Users/HomePC/Dropbox/source/repos/CSharp_XISOSharp` (docs/ +
source used for API + on-disk facts) and as the NuGet package. Same
situation for `CSharp_CHDSharp` (covers the 945-CHD set later).

**Next.** ATAPI DVD sector serving from ISO (guest LBA*2048 maps the
file directly - the ISO *is* the disc); DVD-auth scope (what the
kernel's ATAPI probe needs beyond TUR/SENSE/CAPACITY); CHDSharp for
`E:/Microsoft Xbox`.

## 65. DVD reads served: ATAPI media + guest probe (2026-10-11)

**Device work (`XGrid.Storage`).** New `IDvdImage`/`IsoDvdImage`
(2048 B sectors, read-only, shared reads) + `AtaDevice.AttachDvd`
(ATAPI-only) plumbed through `IdeController`/`XboxMachine` to CLI
`boot --dvd <iso>`. With media: TUR succeeds (`CompletePacketOk`),
READ CAPACITY returns BE last-LBA + 2048, READ(10/12) serve
sectors (bounds-checked 5/0x21, 128-sector cap 5/0x24, zero-length =
OK, single-DRQ staging shared by PIO + BM-DMA via `_dmaLarge`),
unimplemented-with-media (TOC/STRUCTURE/CD) fails 5/0x20.
REQUEST SENSE now returns latched sense (fresh empty drive still
2/0x3A via Reset init - no-media oracle unchanged). 17 new tests
(PIO packets, image file, PRDT DMA incl. a real-file path test).

**gate72 validation (`--dvd` 007, 5B, no fault).** Dashboard boot is
bit-identical (same HDD cmds, draws 806958 / frames 16789). The
guest, finding a ready DVD, probes it: TUR + READ CAPACITY + 16x
READ(10) at LBAs `0x10`-`0x13` (ISO9660 PVD check), `0x20` (XDVDFS
magic check), `0x100/0x101/0x102/0x112` follow-ups - all served live
from the ISO. No auth opcodes observed (no 0xAD/D5/vendor): the
kernel sector-probes, nothing more, on this path.

**Ops lesson.** `Start-Process` single-string `ArgumentList` splits
on spaces: ISO paths with spaces need embedded quotes (array form
failed the same way through the tool layer); foreground quoted runs
verify parsing.

**Next.** READ TOC/STRUCTURE if a title demands them; real game boot
needs title flow to `D:` (dashboard stays in state 0); CHDSharp for
the 945-CHD set.

## 66. CHD games boot: compressed DVD + 007 in a timing wait (2026-10-11)

**CHDSharp integration (no source copied).** NuGet `CHDSharp` 1.4.3
(MIT; transitive MS.Extensions.Logging/Hashing), consumed by
`XGrid.Storage` through `ChdDvdImage : IDvdImage` (`ChdFile.Open` /
byte-offset `Read` / `TotalBytes`; 2048 B scratch + copy; not
thread-safe, single-threaded use documented). `XboxMachine.OpenDvdImage`
sniffs magic (`MComprHD` -> CHD, else raw ISO); `boot --dvd` accepts
both. 4 tests (CHD error paths, magic dispatch both ways).

**Layout discovery.** The 007 USA CHD is a REBUILT XISO, not the raw
dump: 839520 sectors (1.7 GB), descriptor at CHD sector 32, root at
33, no video partition (`DVD` metadata tag). Cross-proof:
`default.xbe` extracted from the CHD is SHA-identical to the Redump
ISO's (696320 B) - same title, different container.

**gate73: the kernel boots the GAME off the CHD.** TUR + capacity +
probe, then sequential READ(10)s at `0x96E34+` step 2 (bulk streaming);
game XBE executes (base `0x10000`): hot code = software udiv loop
(`0x138C0`, 32-iteration) + QPF timing math (`0x112D2`: `imul 1000 /
div` - perf-counter-to-microseconds). NVNET 0/0, USB `intr=0`; draws
stay at boot-logo 2670, frames 62. No auth opcodes anywhere.

**gate74 (10B, +eip-hist): parked in the wait.** No fault; DVD tail
FROZEN (same 16 LBAs - zero new reads after ~5B); **47% of all 10B
instructions inside the udiv loop**, kernel idle 3.6%; flips crawl
62->83 (~240M steps/flip) with no new clears; EIP ends inside udiv.
SMBus port reads (`C000`=1375) are background (10x less than
dashboard runs), not the wait. PIT IRQs flow (16342).

**Position.** First real game boot: kernel -> DVD -> XBE executes ->
load streams -> timing wait with everything idle. The wait condition
(caller of the timing func? QPF source? flip gating?) is the next
probe. TOC/STRUCTURE still unneeded (never requested).

## 67. Game wait decoded: frame limiter + 2 Hz tick pump (2026-10-11)

**Wait loop (`0x20E40`).** `while (delta() < [AFE34]-2) { 0x26C60(ebx); }`
with a second driver looping back via `je 0x20E40`; exit falls into
object housekeeping. Delta comes from `0x207C0` (tick global minus
baseline through udiv-heavy scaling, float return).

**Wait body (`0x2654D`).** Builds a negative relative timeout
(`0x26400`: ms x 10000, negated) and calls kernel ordinal 99 =
**KeDelayExecutionThread**, retrying while the return is `0x101`
(ALERTED). A timed sleep, not a spin - so the 47% udiv burn is the
OUTER delta recompute, spinning because delta never reaches target.

**Tick pump (`0x26B60`).** `tick++; tick2++`, then fires up to 8
callbacks from table `0x1183A0` - which is EMPTY at 6B. No direct
E8/jmp callers anywhere in the image.

**gate76 (watch on tick phys `0x1991E0`, 5.5B): `n=720` writes.**
First = kernel zero-init (`0x8001FAF5`, value 0), last = pump
(`0x26B66`, value 718). The tick advances at ~2 Hz, steadily - a
slow trickle, not a freeze. Snapshot at 6B: tick 790, baseline 4,
target `[AFE34]` = 26, `[AFE30]`/`[AFE38]` = 0.

**gate77 (pump break-log, IN FLIGHT at session end): 251+ hits from
step ~550M, ALL from a single invoker `0x379CA`, object
`ESI=D005CB30` stable.** The invoker is an object-table iterator:
lock (RtlEnter 294) -> indirect `call [ebp-0xC]` (callback pointer
loaded from `obj+0x28` = the pump) -> unlock (RtlLeave 277). Sibling
flag bits dispatch to NtSetEvent (225) / NtPulseEvent (205) - an
event/object manager update. Game thunk census: 96 kernel ordinals
incl. PsCreateSystemThreadEx (255), timers/DPCs
(97/107/113/119/137/149/150), events, full file I/O.

**Position.** Leading theory is TIME DILATION, not deadlock: the
frame limiter waits on a tick pumped at ~2 Hz (vs the 60 Hz-ish rate
a frame-driven pump would give), so game-time crawls and the udiv
recompute dominates. Still open: what schedules the pump invoker
(worker thread? DPC?); full gate77 cadence distribution; whether the
wait expires with enough steps. Next session: finish gate77
(`gate77_pump.log`), identify the invoker's thread context, then
decide emulation-gap vs long-wait.

## 68. Gate77 complete + Xbox PIT frequency fix (2026-10-11)

**gate77 (pump break-log) finished: 718 hits, first 549,826,940, last
5,496,305,058.** Median interval **6,913,994 steps = exactly 10 PIT
pulses** (guest divisor 1125); payload identical on every hit
(`ESI=D005CB30`, `caller=000379CA`, `arg=1`), plus 22 pairs 376 steps
apart (same callback re-entered). The pump is a **10 ms periodic
KTIMER** (slot `+0x3C = FFFE7960` = -100000 x 100 ns, `+0x31` periodic,
`+0x28` callback `0x26B60`) registered by game code `0x26BA0`
(`1000/100 -> 10`), serviced by the game-created worker thread (stable
ESP/ESI - not a DPC). The `Watch:` tail (phys `0x35B00-0x35D00`) is
2BL/kernel BSS, not game state.

**Time-dilation verdict.** The guest's own `delta()` (`0x207C0`) scales
the pump tick by `0.001 x 29.97` - it treats the registered 10 ms tick
as 1 ms. That guest-side unit mismatch is the ~10.7x device-time
dilation; wall-clock ~540x = ~50x interpreter x ~10.7x pacing. The
KeDelay wrapper (`0x26C60 -> 0xD554D`, ord 99) builds a 0-timeout yield
by design (arg 0). Not a deadlock, no lost wakeups.

**Real defect found: `Pit8254.InputFrequency` was 1,193,182 (PC 8254).**
Xbox is **1,125,001** (xemu `i8254.h:33-38` PIT_FREQ; guest divisor 1125
-> 1000.0 Hz; KeTimeIncrement `0x2710` = 1 ms). Fixed. Validation
(probe_pump2, 2B steps): 198 hits, **median interval 7,332,029** ~= 10 x
733,333 = 7,333,330. Guest clock now tracks device time exactly.

## 69. XID root cause: missing USB daughterboard hub (2026-10-11)

**Pre-hub**: the guest enumerated the pad directly on USB0 root port 0
(GET_DESC8 -> SET_ADDRESS 1 -> GET_CONFIG -> SET_CONFIG, frames 104-111),
then issued zero USB traffic for 10B steps; the XPP device mask (VA
`0x751B4`) stayed 0; no XID class requests; A-press was byte-identical
(section 56).

**Root cause**: the guest kernel reports `XboxHardwareInfo = 0x41` (bit0
= internal USB daughterboard present) and expects a hub; xemu models
root port 1 -> 4-port daughterboard hub -> port 1.3 -> 3-port controller
hub -> port 1.3.1 -> XID (verified against the vendored xemu source and a
live `-trace` capture of the oracle binary). XGrid had no hub.

**Implemented** (`src/XGrid.Bus/Usb/UsbHub.cs` + `OhciUsbController`):
class-9 hub (descriptors, port status/feature handling, downstream
address routing, EP bitmap, NAK when clean) plus OHCI fixes: RHSC raised
on attach; `HcDoneHead` returns the live done queue; HCCA `done`
preserved when no writeback; `USB_RESET` stops the bus.

**Validation**: 007 now fetches the XID descriptor (absent pre-hub;
present at phys `0x19FC94` post-hub). The dashboard's XPP stack goes
live: OHCI ISR `0x7BA19` 93 hits, XID interface open `0x79E99` @947.66M,
XInputOpen `0x76A1F` @948.39M, GetCapabilities `0x76A81` @949.13M,
**GetState `0x76C59` 999x from 950.59M to run end**; XPP mask now points
at a device object (VA `0x751B4` = `a0633b00` x4); the hub interrupt EP
is polled every 8 frames (NAK when clean).

## 70. Interactivity: input reaches the menu; navigation + activation (2026-10-11)

**A-press (post-hub)**: neutral vs A RAM diff 23,786 bytes (pre-hub:
byte-identical). Classification: 98.3% mid-frame NV2A scratch; the rest
is pad mirrors - kernel XPP buffers (VA `0x801A0854/74`), XPP pad objects
(VA `0x591AB4/08/30`), menu input mirror (obj `+0xA8=00FF0000`,
`+0xAA=FF`, `+0xD0=1`). Menu state stayed 0.

**Activation needs the analog-A EDGE** (obj `+0xF4` = one-frame
just-pressed from padmgr `+0x4C`); boot-latched A fired before the menu
was live. The only writer of state `[+0x280]` is `0x1859D` (navigator
activation, control = `0x24E90([0x47BC84], [+0x270])`).

**D-pad levels are edge-free**: `--pad-buttons 0002` (DOWN) moved the
selection obj `+0x270` 1 -> 5; navigator `0x1840A/0x184BA/0x1854B` hit
1383x (844M-1.2B).

**New CLI `--pad-step N`** (step-timed pad injection). A-edge at
960,000,000 -> activation `0x1859D` fired once @960,493,586 (one frame
later), storing control[1] `+0x0C = 14` into `[+0x280]`. State 14 maps
(table `0x1EE98[14]=12` -> `jmp [0x1EE50+48]`) to handler **`0x1E66A`**,
which ran and cleared the state (watch: 4 writes, 3 from `0x1E66A`).
No visible frame change (draws 72139 vs 72152, frames/pixels equal) -
control[1] targets a logical transition. Next: decode other controls'
targets and sequence injection (navigate then activate).

## 71. NV2A fidelity wave: inline arrays, XF constants, fetch, semantics (2026-10-11)

- **Inline vertex data (M5 gap closed)**: `SET_VERTEX_DATA2F_M` 0x1880-0x18FC,
  `2S` 0x1900-0x193C, `4UB` 0x1940-0x197C, `4S_M` 0x1980-0x19FC, `4F_M`
  0x1A00-0x1AFC; inline-buffer draws now run through `VshProgram`;
  `DRAW_ARRAYS` 0x1810 ranges (start bits 0-23, count bits 31-24 + 1)
  with contiguity merge; ARRAY_ELEMENT range expansion (xemu `draw.c`/
  `pgraph.c`).
- **XF-context constant writers (all 11 xemu `vsh_constants` writers)**:
  CMAT c0-3, PMAT c4-7, MMAT c8-39, IMMAT c12-39, TEXGEN planes c64-91,
  T0MAT c68-95, FOG c57, EYE c56, VPOFF c59, VPSCL c58 (+ dynamic
  0x0B80). This corrects section 42's wrong claim that c58/c59 are
  no-ops on xemu.
- **Vertex fetch**: stride 0 = constant attribute (element 0); array
  offset bit31 masked (DMA context A only); program start + FINAL-
  terminated length scan; output defaults (0,0,0,1); execution-mode
  diagnostic counter.
- **Interpreter semantics**: MUL zero rule (0 x Inf/NaN -> 0), GLSL
  min/max/NaN/-0 forms, LIT, paired outMux=ILU last-writer order, oPos
  trunc-to-1/16 + w clamp.
- **Fragment**: perspective-correct varyings (w-weighted barycentrics,
  equal-w fast path bit-exact), oD0 NaN->1. Multi-texture STOPPED - it
  needs the psh register-combiner subsystem (COMBINECTL 0x1940 etc.);
  full register list in the agent report.
- Dashboard/007 counters unchanged or +1 fullscreen frame (pixels
  10,137,600 -> 10,444,800); program draws still ~0 px while the menu
  is black state 0.

## 72. Repo hygiene (2026-10-11)

- `src/XGrid.Xbe` was untracked (hidden by the `*.xbe` ignore rule);
  negation added + project committed - a clean clone now restores.
- CLI: `--break-log` repeatable (EIP in hit lines); `--pad-step`;
  `--watch` remains physical-only.
- Test baseline **804 -> 885** (Nv2a 67->94, Bus 150->172, Integration
  15->16).

## 73. F36A2/LBA 8 gap closed: config-sector RMW is guest-normal, no emulation bug (2026-10-11)

**LBA 8 identity (reference-first).** XboxDevWiki "Hard Drive" gives a
fixed partition layout with **Partition0 = Config Area** (byte `0x0`,
size `0x80000`, "Fixed Structure") — LBA 8 sits inside it. The vendored
wiki plus the live "Config Sector" page document network settings at
Partition0+`0x1020`+ (= LBA 8 offset `0x20`+: static/DHCP/Xbox-Live
IPs, masks, gateways, DNS). So `55 AA` at `0x1FE` and `PCBX` at `0x1F4`
are the **config-sector format** (validity marker + magic), not MBR
semantics — and the kernel's fixed layout means nothing ever parses
LBA 8 as a partition table. `XGrid.XboxFs` takes explicit partition
LBAs and never reads LBA 8 (verified by grep: no PCBX/55AA/LBA-8
handling anywhere in `src/`).

**Trace evidence (temp env-gated scan + single-step trap, both
reverted; logs/ram in temp).** A 5M-step overlay poll finds the first
LBA 8 write in `(640M,645M]`; an exact-step trap fires deterministically
at **step 656658455, EIP `0x8002430E`** (kernel ATA miniport, state's
`0x800240xx` machine with `0x8003adxx` dispatch; `out 0x1F7` FLUSH form
nearby). IDE tail at the trap: dashboard content reads, 1-sector
partition probes, `MEC` (IDENTIFY), then `MC8/F00/n01l080000` (READ DMA
LBA 8) + `MCA/F00/n01l080000` (WRITE DMA LBA 8) — a kernel-driven
**read-modify-write of the config sector during dashboard init**, the
overlay's first two writes overall. Kernel-stack forensics (page-table
walk from CR3 `0xF000`) shows a pure kernel chain (`0x800245E8`,
`0x80024B9C`, `0x800243FD`, `0x80012348`, `0x8001598A`, `0x80026770`,
`0x80012428`, …) with the request block at `ESI=0xD004C988`; the trap
EIP/registers/IDE tail reproduce bit-identically across runs.

**Serving fidelity (no read-path or taskfile/DMA bug).** Host-side
`xgrid sector` dump of base LBA 8 is byte-identical to the guest-written
sector in early runs; the RMW preserves every stable field (header,
`2VBX`/`PCBX`, `55 AA`), which is only possible if our READ DMA served
the stored bytes exactly. Later runs rewrite the digest/checksum fields
(`0x0C–0x1F`, `0x1F8–0x1FB`) with run-varying values (image `D94D…`,
53-family `69FA…`, trap run `C7F2…`; 53_5b == 60_5b at equal steps, so
tick-derived, not wall-clock) — plain-SHA1-over-sector hypotheses all
fail, so the field is guest-internal crypto (keyed/HMAC-style) with no
behavioral consequence: boot continues with no fault in every run.

**F36A2 decoded (unrelated to disk I/O).** `0x800F36A2` is unmapped in
the guest page tables; dashboard VA `0xF36A2` holds BGRA texture pixels
at trap time; HDD LBA `0xF36A2` reads all zeros; no `F36A2`-ish value
appears anywhere on the writer's kernel stack. The §§49–51 `F36A2`
skipped page is the WC-alias surface-upload artifact (resolved by the
§50 alias), a separate observation sharing only a hex coincidence.

**Verdict: NOT an emulation bug.** Guest-normal kernel config-sector
RMW, served faithfully, zero behavioral consequence. No fix required.

**Regression lock.** `BusMasterDmaTests.
ConfigSectorReadWriteDmaRoundTripPreservesSignatureBytes` replays the
exact guest sequence (0xC8/0xCA, n=1, taskfile `08 00 00`, incl. the
`MC8/F00/n01l080000` log detail and the guest's BM-stop-between-
transfers discipline) on PCBX/`55 AA`-seeded LBA 8: signatures survive a
digest-field RMW round trip. Storage 46→47 tests.
## 74. Pad-sequence injection: `--pad-script` (2026-10-12)

**Button facts (xemu `hw/xbox/xid.h`, read-only ref).** Analog indices
A0 B1 X2 Y3 BLACK4 WHITE5 LT6 RT7; digital `BUTTON_MASK(b) = 1<<(b-8)`:
UP 0x0001 DOWN 0x0002 LEFT 0x0004 RIGHT 0x0008 START 0x0010 BACK 0x0020
LTHUMB 0x0040 RTHUMB 0x0080. Report = wButtons + 8 analog + 4 sticks,
matching `XidGamepad` passthrough (no code copied).

**Design.** No name parser existed (`--pad-buttons` was hex-only), so
`PadInput.TryParseButtonSet` is new shared code: names (case-insensitive,
`_`/`-`/space ignored, `+`/`|` join) take precedence over hex, so `a`
means analog-A not mask 0x000A; analog names press 0xFF; `none`/`neutral`/
`release`/`0` = neutral. `--pad-buttons` now accepts the same form
(hex inputs parse identically). `PadScript` (`XGrid.Bus/Usb`) parses
`buttonset@step[+hold],...` (stable by step, sticks neutral), applies at
exact step boundaries in all three run loops with batch target-splitting
(same pattern as `--pad-step`, which is untouched); momentary presses
auto-release after 1M steps, `+hold` latches, later events supersede
pending releases. 23 new tests (Bus 172->195); 908 green.

**Validation (worktree build, main-checkout firmware read-only, logs in
temp).** Smoke `--pad-script down@1000000,a@2000000` (3M steps): DOWN
@1000000, A @2000000, neutral @3000000, no fault; `down@1000000+hold`
latches with no release line; bogus spec exits 1. §70 oracle reproduced
via the new mechanism: `--pad-script down@950000000,a@960000000
--break-log 0x1859D` (965M) fires DOWN @950M, release @951M, A @960M,
release @961M, and `HIT 960493586 EIP=0001859D ESI=D003B500` —
byte-identical to the old-flag regression run (`--pad-analog
FF00000000000000 --pad-step 960000000`), which still passes.
## 75. NV2A register-combiner (psh) stage (2026-10-12)

- **Register-map correction**: the task brief's "COMBINECTL 0x1940
  through 0x1940-0x197C" conflates the PGRAPH engine offset
  (`NV_PGRAPH_COMBINECTL` = 0x1940, xemu-internal) with channel method
  offsets, and 0x1940-0x197C is actually `SET_VERTEX_DATA4UB_M`
  (inline vertex data, section 71). The real combiner methods
  (`nv2a_regs.h` NV097_SET_* + `pgraph.c` handlers) are: ALPHA_ICW
  0x260 x8, SPECULAR_FOG_CW0/CW1 0x288/0x28C (final inputs),
  FACTOR0/1 0xA60/0xA80 x8 (C0/C1, ARGB packed), ALPHA_OCW 0xAA0 x8,
  COLOR_ICW 0xAC0 x8, SPECULAR_FOG_FACTOR 0x1E20 x2 (final consts),
  COLOR_OCW 0x1E40 x8, CONTROL 0x1E60, SHADER_STAGE_PROGRAM 0x1E70,
  DOT_RGBMAPPING 0x1E74, SHADER_OTHER_STAGE_INPUT 0x1E78. None collide
  with existing ranges (nearest: viewport scale ends 0xAFC).
- **Stage math** (`glsl/psh.c` facts, reimplemented scalar in
  `src/XGrid.Nv2a/PshCombiner.cs`): ICW byte = reg[3:0] | chan bit4 |
  mapping[7:5], word order A[31:24] B C D[7:0]; regs 0 ZERO/DISCARD,
  1 C0, 2 C1, 3 FOG, 4/5 V0/V1, 8-11 T0-T3, 12/13 R0/R1, 14 V1R0_SUM,
  15 EF_PROD (ZERO+mapping gives ONE etc.); RGB chan 0=.rgb/0x10=.aaa,
  alpha chan 0=.b/0x10=.a; 8 input mappings (unsigned identity/invert,
  expand normal/negate, halfbias normal/negate, signed identity/negate);
  AB = A*B or dot (RGB only; scalar dot = multiply), CD likewise;
  muxsum = AB+CD or MUX (MSB flag: r0.a>=0.5 -> CD; LSB: 8-bit
  quantized r0.a odd -> CD); 6 output mappings; all outputs clamped
  [-1,1]; blue-to-alpha flags; r0 init (0,0,0,t0.a or 1.0 when stage-0
  mode is NONE); V0=oD0, V1=oD1, T=oT0-oT3; EF_PROD = E*F (final only,
  zeros elsewhere); final `rgb = D + mix(C,B,A)`, `a = G`, with
  clamp_sum/inv_v1/inv_r0 on V1R0_SUM; C0/C1 same-vs-unique via CONTROL
  bits 0x10/0x100 (final always uses the SPECFOG pair, incl. E/F reads).
- **Activation rule**: combiner path runs iff the final combiner is
  programmed (CW0|CW1 != 0); otherwise the legacy slot-0/mul path runs
  bit-identically (disabled/idle golden test). Stages without a final
  fall back to legacy (xemu leaves fragColor undefined there).
- **Textures**: sampler generalized slot-0 -> per-slot (offset/format/
  address/control/filter/rect already per-slot); modes NONE (0,0,0,1),
  PROJECT2D (sample), PASSTHRU (coords, w=1); all other modes
  (dot/bump/dependent) sample as NONE - follow-up. Fog register reads
  fixed (0,0,0,1): SET_FOG_COLOR + oFog varying are the follow-up.
- **Validation**: 14 new tests (`PshCombinerTests`): 8 mappings, 6
  output mappings, multiply, dot clamp (1.75->1) + exact dot, MUX
  MSB/LSB, alpha modulate, shared-C0 across stages, final lerp,
  EF_PROD, method-parse state string, disabled golden
  (`0xFF445566`, CombinerDraws=0), two-texture modulate golden
   (`0xFF202020` = (0x80*0x40)/255^2 at two pixels, CombinerDraws=1).
   Full suite green (Nv2a 139); build/format clean.

## 76. Dashboard menu control list decoded (2026-10-12)

Decoded from `dash_act_ram.bin` (64 MB, `XBEH` at phys `0x13E000`).
VA walk (`CR3=0xF000`, `PDE[0]=0x15D067`): header `0x10000->0x13E000`,
`.text 0x11000` scattered (`0x1840A->0x16640A`, `0x1E2FF->0x14C2FF`,
`0x24E90->0x172E90`), global `0x47BC84->0xAEAC84`, menu
`0xD003B500->0x753500`. Disasm via `objdump -D -b binary -m i386
-M intel --adjust-vma=0x11000` on the carved text. Artifacts in
`Temp\opencode\menu_decode\` (outside repo).

**Manager** = global `0x47BC84` (also `[ESI+0xFBC]`/`[ESI+0xFC0]`,
`ESI=0xD003B500`): `+0x00` cached node VA, `+0x04` head VA, `+0x08`
tail VA, `+0x0C` cached index, `+0x1C` selection index (mirrors
`ESI+0x270`). `0x24E90` (thiscall, idx on stack) walks the cursor via
`[node+0x18]` next / `[node+0x14]` prev against `[mgr+0x0C]`, returns
the node VA (`0xCF208` fallback).

**Control node** (`0x30B`, doubly-linked): `+0x00` type (2=XBMC idx0,
1=idx1–5, sentinel `0x00FFFFFF`/1), `+0x04` flag byte tested by the
navigator (0=idx0/sentinels, 1=idx1–5), `+0x08` title VA (UTF-16),
`+0x0C` target state (`0xFFFFFFFF` = end-of-list, tested at `0x18454`),
`+0x10` path VA (ascii, idx0 only), `+0x1C` submenu-list VA (idx2/5),
`+0x20/+0x24/+0x28` display params.

**Enumerated controls** (head `0x5E4210` forward): idx0 XBMC → state 8,
path `e:\Shorcuts\XBMC Shortcut\xbmc.xbe`; idx1 Launch DVD → state 14;
idx2 Games → state 100, list `0x5E4380`; idx3 Trainers → state 33;
idx4 Skins → state 13; idx5 Xbox Admin → state 100, list `0x5E4560`;
idx6–7 sentinels (`+0x0C=-1`). State map (`0x1EE98[state]` → jump
`[0x1EE50+idx*4]`): 8→6→`0x1E54F`, 13→11→`0x1E460`, 14→12→`0x1E66A`,
33→14→`0x1E479`, 100→16→`0x1E420`, 0→0→`0x1E77F`.

**Ranked screen-changing candidates.** (1) idx0 XBMC / state 8
(`0x1E54F`) — the only unconditional loader call (`call 0x232A0`
with the `e:\…xbmc.xbe` path; `0x232A0` is drive-letter dispatch,
`0x23420` stages the path at `0x58FB10/0x590E70`). (2) idx2/5
Games/Admin / state 100 (`0x1E420`) — installs the submenu list as
the new `[ESI+0xFC0]` base, so the next frames render a different
list: cheapest visible-change test, no load. (3) idx3/idx4
Trainers/Skins — single mode-fnptr store (`[ESI+0xFC0]=imm`),
static page. (4) idx1 Launch DVD / state 14 (`0x1E66A`) — proven
no-op at `[0x95BBC]=3`: three `call 0x232A0` sites are gated on
`[0x95BBC]`==0/1/2-or-4 (`E:\APPS\DVDX2\default.xbe`,
`c:\xboxdash.xbe`, `d:\default.xbe`); `=3` falls through — this
explains the §70 identical draws/frames/pixels.

**State machine** (`0x1E2F9` dispatch at `0x1E2FF` region): pre-hook
`0x1E2C2` skips when `[ESI+0x280]==0`; dispatch reads
`[ESI+0x280]`, bounds-checks `0x64` (else `0x1E72B` default),
indexes `0x1EE98` → jumps `[0x1EE50+idx*4]`. Handlers set
`[ESI+0x284]` sub-state + `[ESI+0xFC0]` mode, then common tail
`0x1E779` clears `[ESI+0x280]` → `0x1E77F` housekeeping → sub-state
dispatch via `0x1EF00/0x1EF44`. Only writer of `[+0x280]` is
`0x1859D` (from `0x24E90(mgr,[ESI+0x270])+0x0C`). D-pad loops move
`[+0x270]` (skip on `+0x04==0` or `+0x0C==-1`); A-edge
(`[ESI+0xF4]` just-pressed) fires `0x1859D`. (`0x3E2F9` is a dead
copy — zero hits.)

**Next probes.** (1) `--break-log 0x1E54F --break-log 0x232A0` +
UP to idx0 + `--pad-script` A-edge: expect loader call with the
`e:\…` path, `0x47BC58/0x59647B` writes, IDE `e:\` reads, frame
delta. (2) `--break-log 0x1E420` + idx2 Games + A-edge, dump
`ESI+0xFC0`: expect base `0x47BC84→0x5E4380`, redrawn list, no
load. (3) `--dvd <007.iso> --break-log 0x1E66A`, dump `0x95BBC`:
expect 3→0/1/2/4 and `0x232A0(d:\default.xbe)` on activation.

## 77. 007 input verdict: never-reaches, not mismatch (2026-10-12)

`XInput*` are **not** kernel-thunk imports — they are statically
linked XAPILIB/XPP inside the title image (kernel census §67: 96
ordinals, no input). 007's XBE: 696320 B, base `0x10000`, TitleId
`0x4541000D`, 9 sections, 5 libs, `VA+0xAAF000=phys` (§64). No
`XInputOpen`/`GetState` hits in any 007 run; `USB intr=0`; XPP mask
zero pre-hub; A-press byte-identical. Its sole post-hub USB traffic
is the successful XID descriptor fetch (phys `0x19FC94`) — it has
never issued `GET_REPORT`/`GET_CAPABILITIES`/periodic IN, so no
device-side answer bug is observable.

**No format gap either.** Wire (`XidGamepad` 20 B report) vs XAPI
(`XINPUT_GAMEPAD` 18 B + `XINPUT_STATE` 22 B, `GetCapabilities`
memset `FF`, rumble 6 B) matches what we serve, and the dashboard's
999× `GetState` + D-pad/A-edge propagation proves the
wire→XPP→title path bit-correct. Analog order variance (Cxbx
`X,Y,A,B` comment vs xemu `A,B,X,Y` header) is absorbed by
passthrough + proven mapping.

**The game never gets there.** 007 sits in the `0x20E40` frame-limiter
wait (tick pump `0x26B60`, invoker `0x379CA`, KTIMER periodic;
§§67–68): PIT-exact 10 guest-ms ticks, but the guest treats 10 ms as
1 ms (~10.7× dilation) and the snapshot shows tick≫target with the
wait persisting — the expiry logic (float `delta`, `[AFE30/38]`) is
still undecoded. Dashboard `GetState` runs per-frame; 007's per-frame
phase is behind the limiter exit.

**Unblock plan (no device change indicated):** static-scan 007's
image for `XInitDevices`/`XInputOpen` prologues + `XID 0x4200`
xrefs, `--break-log` the candidates on a post-hub `--dvd` run to
prove zero-open vs the dashboard `0x76A1F` pattern; run past the
limiter (scale pump/tick or patch `[AFE34]`/delta at `0x207C0`)
and only then re-test `--pad` visibility. Do not build psh/nvnet
scope for this.

## 78. NV2A fog state + dependent/bump/dot texture modes (2026-10-12)

Reference facts (`References2/xemu-0.8.136`, `nv2a_regs.h` + `pgraph/`
`pgraph.c` + `glsl/psh.c` + `glsl/vsh.c` + `glsl/vsh-prog.c` +
`psh_regs.h`; reimplemented scalar, never copied):

- **Fog methods**: `SET_FOG_MODE 0x29C` (values `0x2601/0x800/0x801/`
  `0x802/0x803/0x804` -> LINEAR/EXP/EXP2/EXP_ABS/EXP2_ABS/LINEAR_ABS,
  i.e. CONTROL_3 ids 0/1/3/5/7/4), `SET_FOG_GEN_MODE 0x2A0` (stored;
  fixed-function distance is a follow-up), `SET_FOG_ENABLE 0x2A4`
  (bit 0), `SET_FOG_COLOR 0x2A8` (ABGR-packed: R bits 0-7), and
  `SET_FOG_PARAMS 0x9C0` x3 float words (first two = fogParam.x/y;
  third stored).
- **Fog equation** (`vsh.c`): distance = oFog.x (programmable path;
  disabled -> factor path unused). LINEAR: `px + d*py - 1`;
  EXP: `px + exp2(d*py*16) - 1.5`; EXP2: `px + exp2(-d*d*py*py*32)
  - 1.5`; ABS variants take `|f|`; infinite distance -> 1, NaN
  result -> 1. Per-vertex factor is interpolated, then the pixel
  shader feeds `pFog = (fogColor.rgb, clamp(factor,0,1))` into the
  FOG register (`psh.c:1074`). Fog-disabled draws keep the
  established fixed `(0,0,0,1)` input (deliberate deviation from
  xemu's `vec4(1.0)`, preserves the disabled-combiner invariant).
- **oFog varying**: output register 5 (`OUTPUT_REG_FOG`,
  `vsh-prog.c:212`). Masked components fill oFog.x,y,z,w in x,y,z,w
  significance order (the `fog_mask_str` table); only .x is consumed
  downstream (`vtxFog = oFog.x`).
- **Texture modes** (`psh_regs.h` enum, `psh.c` texel block):
  `0x0F DPNDNT_AR` (uv = prev.a,prev.r), `0x10 DPNDNT_GB` (uv =
  prev.g,prev.b) with stage-1 fixed to texel 0 and stages 2-3 from
  SHADERCTL bits 16-19/20-23; `0x11 DOTPRODUCT` / `0x0A DOT_ZW`
  (texel zero, dot stored for the chain); `0x09 DOT_ST` (sample at
  (prevDot, thisDot)); `0x0D DOT_STR_3D` stage 3 only (sample 2D at
  (dot1, dot2)); `0x06 BUMPENVMAP` (`dsdt = (sign3(b), sign3(g))`,
  offset by the 2x2 bump matrix, then sample); `0x07 BUMPENVMAP_LUM`
  (additionally `t *= scale*lum + offset`, lum = prev.r).
- **Dot maps** (`DOT_RGBMAPPING 0x1E74`, low 12 bits, 4 bits per
  stage 1-3): 0 identity, 1 D3D `sign1` ((x*255-128)/127), 2 GL
  `sign2`, 3 `sign3` ((x>=128 ? x-256 : x)/127 with x*255);
  HILO 4-7 fall back to identity with a count. Bump always takes
  the unsigned `sign3` path (no SNORM textures exist here).
- **Bump registers**: texture-slot offsets `0x28/0x2C/0x30/0x34`
  (matrix words in 00,01,11,10 method order per the reference
  swizzle, stored canonically m00,m01,m10,m11), `0x38` scale,
  `0x3C` offset; slot 0 writes discarded (matches the reference
  `slot < 16 / slot == 0` guards).
- **Degradation rule**: PROJECT3D/CUBEMAP/CLIPPLANE/BRDF/reflection
  modes, reserved `0x13-0x1F`, and valid modes in invalid stages all
  resolve to NONE `(0,0,0,1)` and count `TexModeFallbacks`
  (diagnostic property alongside `FogStateDebug`).
- **Validation**: 17 new tests (`Nv2aFogTexModesTests`): fog-mask
  order, factor equations (linear/exp/exp2/abs/inf/NaN), SHADERCTL
  decode, sign functions, stage-validity table, fog-disabled black
  (`0xFF000000`, identical to the pre-change fixed input),
  fog-enabled red (`0xFFFF0000`, LINEAR f = 1.25+0.25*0-1 = 0.25),
  DPNDNT_AR black vs DPNDNT_GB white (order-discriminating pair),
  DOTPRODUCT zero, HILO fallback count, DOT_ST green, DOT_STR_3D
  blue, BUMPENVMAP red, BUMPENVMAP_LUM `0xFF808080`, reserved-mode
  fallback count. Full suite 940 green (923 + 17); existing goldens
  (`0xFF445566`, `0xFF202020`) bit-identical.
- **Remains**: fixed-function foggen distances (SPEC_ALPHA/RADIAL/
  PLANAR/ABS_PLANAR/FOG_X + FOG_COORD attr), cubemap/reflection/
  project3d/clipplane/BRDF modes, HILO dot maps, clipRange, backends.

## 82. pgraph diff verdict: submenu black is guest-accurate, no rasterizer gap (2026-10-12)

Prebuilt Release CLI as-is; neutral vs submenu 1B runs (`--pgraph`,
`--pgraph-max 120M`, same flags + §79 pad script); artifacts in
`Temp\opencode\pgraph-diff\`; repo untouched. Both §79 HITs replicate
byte-identically. Neutral-vs-repeat streams are SHA256-identical
(18,094,707 methods each) → emulation bit-deterministic; neutral↔
submenu heads byte-identical for 13,046,714 lines, first diff = a
float-LSB in modelview `0x0488` — divergence is genuine post-950M
input-driven behavior (late frames are method-dense: 5.1M methods
over the last 50M steps).

**Deltas** (post-divergence; neutral→submenu): BE TRIANGLES +139,
BE QUADS −272, `0x1800` inline elements +98,628 (same arrays/formats;
42 program-word pairs in both, zero unique to either; element-index
vocabulary identical: 23,520 values, zero unique). Program/const
uploads −548 (all slot 0); flips/clears +4. CLI: draws 33760→33623,
skipped +8, pixels frozen 10444800 both, frames 681→685, frame SHA
identical black both.

**Clip point:** every const batch in the submenu region loads the
identical poisoned **C0=(1.0, 1.0, 16777215.0, 1.0)**, viewport-Z
`0AF8` is only `4B7FFFFF`, and all other kill-state is unchanged
(surface pair, no zeta, combiner/fog/blend/alpha/depth same, no clip
regs). Per the §58 proof (`oPos=V0·C0+C0`, |z/w|≈50M) every added
triangle dies in vertex clip independent of inputs — the +8 skipped
draws are degenerate, the rest execute and clip. **VERDICT:
guest-accurate black; no rasterizer gap. Stop probing the
submenu/rasterizer path — the fault is upstream in the viewport
bake** (driver emits Z=16.7M from sane RAM viewport MinZ=0/MaxZ=1):
next is auditing surface/zeta/caps responses at SetViewport-bake
time (§61 open items). Fixing the bake should light up scene +
submenu text in one move.

## 83. DVD auto-launch traced to the same `call [0x10128]` gate (2026-10-12)

`--dvd` 007-CHD runs (logs in `Temp\opencode\dvd-launch\`; repo
untouched; prebuilt binary). 007 XBE: base `0x10000`, entry VA
`0x22E35` (file-stored `0xA8FE799E`), 9 sections.

**Phase map:** 0–24.3M ROM→kernel init (zero IDE); at **step
24,312,685** kernel init calls boot-image decision fn `0x80015724`
(from `0x8001C72E`; flag `[0x8003a87c]=0` → `call 0x8006281f`
branch); ~30–40M HDD IDENTIFY/SET FEATURES + SECURITY UNLOCK
(master) + slave PACKET IDENTIFY + 27-packet ATAPI burst; 33–60M
DVD reads complete (tail fixed `0x604` by 60M), first NV2A frame
≤60M, kernel idle `0x800422B9/B4` 200–500M. **Handoff (exact):
step 537,926,130** — `0x800158DA: call dword ptr [0x10128]` →
**first game EIP `0x22E35`**, via thread-startup trampoline
`0x800218F4` → launch fn `0x800158xx` (XBE-prep calls) → `call
[0x10128]` (entry never returns; `push ebx; call 0x80021b78;
int3`). **Same gate the HDD boot uses for the dashboard (§34)** —
only the loaded image differs; no menu involvement (no
`0x232A0`/`0x1E66A`).

**Decision point:** no single media branch — the 24M init decision
runs pre-I/O on the init-zeroed flag, and the media-dependent part
is the file path (TUR→XDVDFS mount→`D:\default.xbe` open vs `C:\`
fallback). `[0x8003a87c]` is written exactly once in 560M (init
zeroing) — NOT a DVD-presence flag. HDD sees only IDENTIFY/UNLOCK
on DVD boot; all title I/O is DVD-side.

**DVD read order:** `TUR` → `MODE SENSE(10)` [5/0x24] → `REQ SENSE`
→ `READ CAPACITY` → `READ(10) 0x20` (XDVDFS magic) → `0x4B8` →
**`0xAD` (READ DVD STRUCTURE) [fails 5/0x20, tolerated]** →
`0x4BA→0x4FA→0x51A→0x51C-cluster…→0x604` (default.xbe extents) →
launch → `0x96E34+` streaming. **Correction to §66: `0xAD` IS
requested once** (not "never requested"); no `0xA3/0xA4/0xD5` auth
anywhere; no ISO9660 probe (rebuilt XISO has no PVD).

**Tooling caveat (load-bearing for future probes):** `--trace` runs
(65536-insn `Advance` chunks) diverge ~100M+ steps from
`--break`/`--break-log` runs (4096-chunk single-step) — same guest
logic, different pacing (suspect chunk-sensitive `Advance`
consumers: APU VP-frame delivery, PIT phasing). Rule:
`--break`-world steps for exactness; `--trace` windows are only
self-consistent with plain runs. Next: pin the media branch at
`0x80021693`/`0x80030980` (`--break` + ESP-window dumpv), `--watch`
the `0x10128` title-page frame for the XOR-decrypt writer (HDD
analog `0x8002EFE6`), raise the 16-entry `PacketLog` cap.

## 84. nvnet P0+P1 merged: ring latches, IRQ4, TX walker + loopback (2026-10-12)

`wave/nvnet-engine` merged (commit `496ff3c`; +932/−9). P0: TX/RX
BASE/SIZE/CURRENT/NEXT latches, CURRENT=NEXT=BASE on RESET,
IDLE/KICK plumbing, IRQ STATUS/MASK + W1C + `InterruptChanged`
→ `Bus.NvNetPci` raise/lower → PIC IRQ4 (existing slot-4
routing). P1: TX walker (≤TX-count descs to LASTPACKET,
`len+1` coalesce into 1628 B buffer, VALID+error-clear writeback
preserving length/LASTPACKET, TX IRQ `0x10` on completed frames),
`CanTransmit` gates (`TX_START && !BIT2 && LINK_ST` with reason
tracking), `INetworkBackend` seam + loopback/logger default
(bounded 64-frame queue, `TryReceive` P2 seam), CLI counters
(`tx_pkts/tx_bytes/kicks/cant_tx`, ring pointers, irq
asserts/deasserts). Oversize coalesce stops the walk (no xemu
assert). New: `INvNetMemory` (local to `Bus` — sharing IDE's
`IGuestMemory` would be a project cycle; mirrors
`IApuMemory`/`IUsbMemory`), 15 bus + 1 machine tests (machine test
drives a real guest-RAM descriptor → backend frame + PIC
master-request bit 4 → W1C deassert). All 12 existing nvnet tests
green unchanged; full suite 956/956. Guest is init-only (zero
KICKs on boot) — steady state preserved. P2 RX + P3 socket
backend remain (seams ready).

## 85. 007 second wait decoded: parked STRM, completion never arrives (2026-10-12)

Work in `Temp\opencode\worktrees\007-wait2` (analysis-only, clean —
both temp `Program.cs` forcings reverted; logs in
`Temp\opencode\007-wait2\`).

**Timer table** (VA `0xEA308`, 16×16 B, phys `0x199308`): `+0`
callback VA (0=empty), `+4` period, `+8` deadline, `+0xC` busy.
Live: entry0 `{0x30DE0,1,deadline}` = pad poller (XPP object
`0x5AEFC` — the game DID register input callbacks), entry1
`{0x12350,1,deadline}` = input dispatch over `0xF3FD4` (registered
by `0x12390` via `0x26970`); entries 2–15 empty. Pump `0x26B60`
ticks exact 10 ms. `0x26AE0` fires callbacks (fn≠0, tick≥deadline,
busy==0; `deadline=tick+period`) but `0x20B50` **discards the
return** (EAX overwritten) — timer callbacks cannot advance the
wait. Exit needs (a) `0x2A740` buffer magic `0x6843504D`
("MPCh", then `inc [AFE34]`), or (b) `0x2A830`≠0 (needs
`[handle+8]==0` AND `[STRM+0x44]==0`); `0x2ABE0` is the only 2→1
clearer but gated on non-empty buffer — catch-22 on empty stream.

**Parked STRM** (handle VA `0x80B6CBE8`→P `0xB6CBE8`, struct
`0x80B6C800`, stable): `remaining=0`, `state=2` (refill-pending,
set by `0x2AA65`); 153 lifetime writes, last writer `0x2AA65`,
**zero writes 1.8B→3B**. No device I/O outstanding, no producer
thread (EIPHIST: waiter + kernel idle + pump only). Longer steps:
only deadlines move; pad windows: zero effect; force probe
2→0 at 2.2B: exits permanently → 800M new-phase steps, no fault
(splash unchanged, DVD tail still frozen — next gate).

**Verdict:** waiting on the final chunk's async completion that
never arrives — prime suspect user-mode completion delivery
(APC/producer wakeup) for the refill read, not DVD serving.
**Next:** thread/APC observability for the pending op on
`0x80C6C800`; windowed break-log on `0x2AA65` (~1.9B) to capture
the refill issue; implement the missing completion delivery.
Longer runs ruled out (frozen 2→3B).

## 79. §76 probes executed: submenu state-proven/pixel-negative, idx0 unreachable, DVD auto-boots (2026-10-12)

Prebuilt Release CLI as-is, firmware/HDD read-only (in-memory COW),
logs/BMPs in `Temp\opencode\probes76\`; repo untouched. Baselines:
neutral 1B (EIP `0x259BF`, draws 33811/skip 1394/px 10444800/clr 692/
frames 682, black SHA `6f780a33…`, sel=1/state=0/`FC0=0x47BC84`/
`[0x95BBC]=3`) and neutral 1.6B (148928 draws/3081 frames, identical
black).

**Input-timing model (new):** menu frames run every ~250k steps;
D-pad auto-repeat ≈ initial + ~3M period. The 1M momentary press is
phase-unreliable (misses polls — the §74 oracle script re-fired idx1
3× with sel stuck at 1). Reliable recipe: D-pad `+hold` ≥6M, A
`+hold` ≥6M.

**Probe 2 (state 100) — STATE-PROVEN, PIXEL-NEGATIVE.**
`down@950M+hold,none@962M,a@970M+hold,none@976M`: DOWN walks 1→2→3→4→5,
`HIT 972246034 EIP=0001859D EAX=005E44F0` + `HIT 972349247
EIP=0001E420 EAX=0x64` → `FC0 0x47BC84→0x5E4560` (Admin list), sel 5→0,
state cleared, no XBE load; replicated bit-identically. Deltas:
−188 draws/+3 frames at 1B; −4944 draws/+113 frames/+113 clears at
1.6B — but pixels frozen at 10444800 and frames SHA-identical black
in every menu run. Bonus: `HIT 960596785 EIP=0001E66A EAX=0x0E`
observed directly — state-14 handler firing and no-op'ing.

**Probe 1 (state 8 loader) — UNREACHABLE via D-pad.** UP held 12M
(~48 frames) → sel stays 1 (replicated 2×); DOWN 40M → clamps at 5,
re-press stays 5 (wrap excluded). Navigable set = {1..5}; idx0 never
selected while input is live (sel reads 0 only during 836–844M, before
XPP/menu input is live — A-held there gives zero HITs). `0x1E54F`/
`0x232A0` never fired in 15 runs; zero IDE `e:\` reads.

**Probe 3 (DVD flip) — SUPERSEDED: game auto-boots.** `--dvd` 007 CHD:
no menu instantiates (zero state-machine HITs, menu VA holds non-menu
data); draws 2672/frames 45, ATAPI READ(10) streaming (`0x96E34`+),
EIP `0x138D3`, no fault — frame fully non-black (nonzero=307200):
**the EA GAMES splash** (white bg, blue logo, letterbox bars, 5465
colors, surface `0x890000`). Converged by ~760M; auto-launch precedes
the menu (not via the `0x1E66A` DVD branch — `0x232A0` never fired;
launch mechanism TBD). The literal `[0x95BBC]`-flip expectation is
inapplicable (owning module not resident).

**Next:** `--pgraph` method-stream diff (submenu vs neutral) to decide
guest-side clip (cf. §§57–58) vs rasterizer gap — that determines
whether pixel-negative is guest-accurate. In parallel: trace the DVD
auto-launch path (kernel XBE-load/reboot APIs, ≤760M `--dvd` boot).

## 80. 007 unblocked past the limiter — parks in a second wait, never-polls (2026-10-12)

Work in `Temp\opencode\worktrees\007-unblock` (no commit — nothing to
commit; temp patch fully reverted, worktree clean). Logs/scripts in
`Temp\opencode\007-unblock\`.

**XInput call-site map** (007 `default.xbe` facts confirmed): no
`XInput*`/`XInitDevices` strings, zero `push 0x4200` (XID hits are
float-table false positives). `0x602F7` (XPP section) = **XInputGetState**
— prologue byte-identical to the dashboard GetState, returns `0x48F`
(`ERROR_DEVICE_NOT_CONNECTED`) when absent. Break-log 2B run: **4 hits**
(626M/729M/829M/833M, stable thread stack — init-time 4-port probes,
not per-frame polling). `0x5E016`/`0x5E276` (XInputOpen-like) +
`0x5E815`: zero hits in 2B — open never called. Device path proven:
XID descriptor guest copy at phys `0x19FC94` (exact §69 address), USB
`addr=1 cfg=True`.

**Limiter exit (temporary `[AFE34]=2` forcing, reverted; Program.cs
zero diff).** Non-invasive exit unavailable (no PIT/pump CLI flag;
natural exit needs ~800 ticks / 6B+ steps). Decoded `0x20E40`:
`delta()` vs `[AFE34]−2` with `0x26C60` spin; `0x207C0` = raw-elapsed
+ `[AFE30/38]` drift filter + float scale. Corrections to §77: live
MMU mapping is **VA `0xAFE34` → phys `0x15EE34`** (not `+0xAAF000`);
the game rewrites target=`3` every ~10–20M steps; 007's `XBEH` is
never in RAM (sections-only mapping).

**Post-limiter verdict — NEGATIVE (never-polls).** 2B patched: new
regions (`0x1C/0x1E/0x21/0x222xx`), frames 49→99, limiter machinery
gone. 5B patched: parks in a **second wait** (table-scan spin
`0x26AE0–0x26B5B` + `0x20B50` exit-housekeeping + new `0x2A8xx`
code; udiv burn gone), frames stall at 101, logo unchanged, DVD tail
frozen (same 16 READs). USB `ctl` frozen at 94 (enumeration only),
zero `0x602F7` samples in 500 EIPHIST, all three `--pad-script`
windows (0.7/0.8/3/3.2/4/4.2B) with no effect. **No device fix
indicated** — dashboard 999× GetState precedent stands; the game just
never reaches steady-state polling by 5B. Next: decode the second
wait (`0x26AE0` table — what is it scanning for?).

## 81. nvnet packet-engine design (2026-10-12)

Read-only survey; no code. Current device
(`src/XGrid.Bus/Net/McpxNvNetDevice.cs`): MMIO `0xFEF00000`+
`0x400`, IRQ/STATUS/MAC/MDIO/link-up only, header states no
DMA/engine/interrupts; PCI 0:4.0 `10DE:01C3` (`XboxPciStubs`),
IRQ path resolves to PIC IRQ4 via LPC routing but the device never
raises. Guest is init-only (zero `0x100–0x138` ring, zero KICK).

**xemu oracle** (`hw/xbox/mcpx/nvnet/nvnet.c` + `nvnet_regs.h`,
facts only): rings live in **guest RAM as 8 B LE descriptor arrays**
`{buf,len,flags}` pointed by BASE regs `0x100` (TX)/`0x104` (RX),
SIZE `0x108` (count=mask+1), CURRENT/NEXT `0x11C/0x120/0x134/0x138`
with wrap+clamp; doorbell = `0x144` KICK write (walk ≤TX-count descs
to LASTPACKET); TX bytes=`len+1` coalesced, RX asserts `(len+1)>=size`;
IRQ = `status&mask` assert (TX `0x10`/RX `0x2`/RX_NOBUF `0x4`/TX_ERR
`0x8`/TIMER `0x20`/MIIEVENT `0x40`), W1C on `0x000/0x180`;
`can_transmit = TX_START && !BIT2 && LINK_ST`; RX filter =
broadcast-always + MYADDR/mcast-masked/ucast-MAC compare, oversize
(`>MTU+128`) dropped; backend = QEMU `-nic` pluggable, default
user-mode SLIRP. PHY init values match ours exactly
(`BMCR 0x1100/BMSR 0x2C/AN 0x3E0`). Cxbx has zero nvnet register
model — xemu is the oracle. Dashboard carries XNET + static IP
(`192.168.0.2`) + FTP/telnet servers: static-IP servers, no DHCP
evidence.

**Build plan (phased, for a future wave):** P0 skeleton (ring-pointer
latches, RESET wrap, IDLE/KICK plumbing, IRQ STATUS/MASK + W1C +
`InterruptChanged→IRQ4`); P1 TX (walker + coalesce + VALID-clear
writeback + TX IRQ + logger backend); P2 RX (filter + DMA-write +
RX/RX_NOBUF IRQs + CLI `nvnet inject`); P3 backend
(`INetworkBackend`, loopback/logger default, socket/SLIRP-equivalent
behind flag, 250 ms autoneg via `Advance`). Walker reuses the IDE
`IGuestMemory` pattern. CLI observability: named reg Rd/Wr, TX/RX
pkt/byte/drop counters, `cant_tx/cant_rx` reasons, ring pointers,
IRQ assert/deassert, link toggles. Open: BIT2 reliance, AN_COMP
timing, SIZE counts, filter programming, first real frame type
(needs a TX-logging run).

## 86. DVD-vs-HDD media branch pinned + title-page decrypt writer + PacketLog cap (2026-10-12)

Work in worktree `wave/dvd-media-branch` (main checkout untouched;
worktree-built Release CLI; logs in `Temp\opencode\dvd-media\`). Boot
args replicate the §83 handoff bit-exactly (`--break 0x22E35` fires at
step 537,926,130 in break world).

**Media-branch verdict: neither `0x80021693` nor `0x80030980` is
media-dependent.** Both fire at identical steps with identical state on
DVD (007 CHD) and HDD (dashboard) boots:

- `0x80030980` (first hit step 24,304,655, ESP=`0x800395E4`):
  `CMP byte [0x8003A87C],0` (the §83 init flag, still 0) then
  `TEST [0x8003B138],1` (also 0), then port I/O (`out dx,al` forms at
  `0x80030995+`). Caller `0x80018B7A` (call at `0x80018B75`);
  ESI=`0x0040008C` EDI=`0x80406000` EAX=`0x80036840` (zeros).
  `--break-log` through 600M shows ~20 hits from many callers
  (`0x80062828/43/4D/57/61/6D/77`, `0x8001573E`, `0x800309BE/E3/...`)
  -> a shared port-I/O helper, not a branch.
- `0x80021693` (single hit step 25,054,251, ESP=`0x800395BC`):
  `OR dword [0x8003ABC4],0xFF` + 26-entry table init at `0x8003ABB0`.
  Called from decision fn `0x80015724` (call at `0x80015750`, return
  `0x80015756`: `test al,al` then `push 0x5E` vs `call 0x80061C92`);
  decision caller is `0x8001C72E` (return `0x8001C733` on stack). Stack
  args `0x80406000`/`0x0040008C` are unmapped here (no path strings).

ESP-window dumps, flag bytes (`[0x8003A87C]=0`, `[0x8003B138]=0`) and
caller code are byte-identical across media. The media-dependent
decision is downstream and file-path based (`D:\default.xbe` vs `C:\`
fallback), confirming §83: no single media branch at these sites.

**Decrypt-writer verdict: SAME SITE `0x8002EFE6`, same key.** DVD:
`--break 0x8002EFE6` fires step 25,955,482, EAX=`0xA8FC57AB`, VA
`0x10128` = `0xA8FE799E` (encrypted); insn `31 05 28 01 01 00`
(`xor [0x10128],eax`) yields `0x00022E35` (007 entry). HDD analog fires
step 26,343,475 with same EIP/key, ciphertext `0xA8FD7DBC` ->
`0x00012A17` (dashboard entry). Physical watch (`--watch
0xBF128,0xBF130`; VA `0x10128` -> phys `0xBF128` via CR3=`0xF000` page
walk, image base VA `0x10000` -> phys `0xBF000`):
`W000BF128 n=3 first=8001FAF5:4=0 last=8002EFE6:4=22E35`
(zero-fill, load, decrypt) — the writer is the decrypt site.

**PacketLog cap 16 -> 256** (`AtaDevice.PacketLogCapacity`,
`src/XGrid.Storage/AtaDevice.cs`; 2 tests in
`tests/XGrid.Storage.Tests/AtapiTests.cs`: 30-packet full retention in
order, 266-packet cap evicting oldest). Single-run re-capture to the
handoff (27 packets): `00` TUR, `5A` MODE SENSE(10), `03` REQ SENSE,
`25` READ CAPACITY, `28@0x20`, `28@0x4B8`, `AD`, `03` REQ SENSE
(post-AD 5/0x20 sense fetch), then `28@0x4BA/4FA/51A/51C/52C/52E/546/
548/54A/54C/558/55A/564/566/5A6/5E6/5F6/5F8/604` — confirms the §83
order including the AD-failure sense fetch in one run (the old cap
showed only the trailing 16 READs).

Suite 958/958 (956 + 2 new); build + format clean.
## 87. Viewport bake decoded: C0.z=16.7M is correct XDK output — the missing piece was emulator-side /clipRange.y (2026-10-12)

**Verdict first.** There is no poisoned bake and no surface/zeta/caps
response to fix: the guest driver emits textbook-correct XDK
device-depth output (viewport-Z `0AF8=4B7FFFFF`, `C0=(1,1,16777215,1)`),
and the black scene was our rasterizer saturating device-unit Z to 1.0
because it never implemented xemu's `oPos.z / clipRange.y`
normalization. Fix merged on `wave/viewport-bake`; the 1B submenu
window goes from a black frame to a 57K-nonzero rendered scene
(pixels 10.4M → 118.8M). C0.z and viewport-Z are UNCHANGED by the fix
(still 16.7M — that is the correct value); sanity appears downstream
of the divide.

**Reference facts extracted (no code copied).** xemu
`hw/xbox/nv2a/nv2a_regs.h`: `SET_SURFACE_FORMAT` (`0x208`) ZETA field
is bits [4:7] with Z16=1/Z24S8=2; `SET_VIEWPORT_SCALE` = `0xAF0`,
`SET_VIEWPORT_OFFSET` = `0xA20`, `SET_CLIP_MAX` = `0x398`,
`SET_TRANSFORM_CONSTANT` = `0xB80`; XF `VPSCL`/`VPOFF` slots are
`0x3A`/`0x3B` (58/59). `pgraph/pgraph.c`: viewport scale/offset
methods only feed `vsh_constants[VPSCL/VPOFF]` (fixed-function
shaders); guest-program constants are never derived from the viewport
— the driver bakes them. `pgraph/glsl/vsh-prog.c`: `mask_str`
(bit0=w, bit1=z, bit2=y, bit3=x) is used for every masked write
including the paired temp path; the fog write expands to
`oFog.<fogmask> = result.<fogmask>`, i.e. oFog.x always comes from
result.x (only the set-bit COUNT selects y/z/w); the vertex epilogue
does `oPos.z = oPos.z / clipRange.y`. `pgraph/glsl/common.c`:
`clipRange.y` = zeta-format maximum (Z16: 0xFFFF, Z24S8: 0xFFFFFF;
float-depth variant via `CONTROL0/Z_FORMAT`:
`f16_max=511.9375`/`f24_max=1e30`). `pgraph/glsl/psh.c`: fragment
`zvalue` is interpolated screen-space (device units), clamped to
`[ZCLIPMIN,ZCLIPMAX]` (guest `0x398` → `ZCLIPMAX`), stored
`floor(z)/65535` (D16) or `floor(z)/16777216` (D24). `pgraph/glsl/vsh.c`:
fog distance is always `oFog.x`; `vsh_header` inits `oFog=(0,0,0,1)`.
Cxbx `XbVertexShader.h` (`MASK_X=0x008, _Y=0x004, _Z=0x002, _W=0x001`)
and `Direct3D9/VertexShader.cpp` (`DestMaskHlsl`: non-x oFog masks are
redirected to x with a `LOG_TEST_CASE`, seen once in Lego Star Wars II)
independently confirm both the bit order and the oFog.x rule.

**Evidence chain (guest is correct).** RAM viewport `MinZ=0/MaxZ=1`
(§61) with a Z24S8-default depth range gives device Z scale
`(1−0)×0xFFFFFF = 16777215`: the pushbuffer's `0AF0/2/4/8 =
(320,−240,16777215)` + `0398 CLIP_MAX = 16777215` is exactly that
(scaleX/Y = 640/2, −480/2 as expected). The §58 shader
(`oPos=V0·C0+C0`, `C0=(1,1,16777215,1)`) is the driver threading the
viewport Z scale through its generated program — same thing xemu's
fixed-function backend does via slots 58/59. §61's audit stands:
surface/zeta state is guest-write passthrough with no GPU readback
into driver math, so no emulator response could (or need) change what
the driver emits.

**Inherited-diff audit (uncommitted work from a vanished session).**
Kept: `WriteMasked` bit-reversal (both references) + its
`VshProgramTests` updates; device-depth divide in `Nv2aMmio.Vertex`
(xemu epilogue/fragment sites above; fixed-depth only, matching the
rest of our rasterizer — `Z_FORMAT` float is a noted gap) + the
`DepthTest` device-unit updates + the Z24-saturation theory.
Reworked: `WriteFogMasked` (inherited bit-position model matched
neither reference's executable behavior — replaced with the
count-based destination-prefix model both codegens implement; common
`oFog.x` writes are identical under all models). Reverted/removed:
`LastVertexProbe` (hot-path debug string), the `zeta16.txt`/`probe.txt`
file-writing tests (absolute paths; one carried a guaranteed-fail
`"reveal"` assert), the captured-shader color assert (single-array
harness artifact — depth asserts kept). Repaired (previous session
stopped mid-update): 6 mask tests whose w-lane expectations forgot the
pre-existing epilogue `ClampAwayZeroInf` (±0 → ±2^-64); the swizzle
texture test's oT0 mask `0x3→0xC` (same x,y intent under the corrected
order). Full suite 961/961 (956 baseline + 5 new: Z16/Z24-saturation/
passthrough-depth/minimal-diffuse).

**Validation (worktree Release; firmware read-only from main-repo
`data\xemu-0.8.136-windows-x86_64\`; logs in
`Temp\opencode\viewport-bake2\`; recipe `--pad-script
"down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"`,
1B steps, no fault, EIP `0x1DF11`).** Same-tree before→after (stash
run vs fixed run, guest stream bit-deterministic so draws/skipped/
frames/clears are identical): draws 33632, skipped 1402, clears 695,
frames 686 both; pixels 10444800 → 118798338, textured 10444800 →
233381688, blended 0 → 45302409, depth-tested 0 → 136; frame
nonzero 0 → 57087 (pre SHA
`6F780A33…`, post `CC9D82DC…`). Post-run pgraph (18.1M methods):
viewport-Z `0AF8` still only `4B7FFFFF` (9639×), `C0.z` (`0B88`)
still dominantly `4B7FFFFF` — correctly so (device units).
**Next:** the scene draws but nothing asserts it is the RIGHT scene
(compare against xemu pixels if it ever passes the animation wait, or
drive to a known screen); ZCLIP bounds + float-depth remain untracked;
fixed-function foggen still open (§78).
## 88. nvnet P2 RX + P3 backend merged: full packet engine (2026-10-12)

`wave/nvnet-p2p3` on top of the §84 P0/P1 engine. Files:
`src/XGrid.Bus/Net/McpxNvNetDevice.cs` (RX + link),
`src/XGrid.Bus/Net/SocketNetworkBackend.cs` (new, user-mode IP),
`src/XGrid.Bus/Net/INetworkBackend.cs` (`TryReceive` added to the
seam), `src/XGrid.Host/XboxMachine.cs` (`Net.Advance` wired into the
advance path), `src/XGrid.Cli/Program.cs` (`--net-backend`,
`--net-inject`, `--net-link`, extended NVNET line).

- **P2 RX** (xemu `dma_packet_to_guest` + `receive_filter` +
  `nvnet_can_receive`, facts only): gates RX_START && !BIT2 &&
  LINK_ST plus AVAIL (`CanReceive`/`CanReceiveReason` with
  `rx-start/bit2/link/no-buf` reasons); filter = broadcast-always +
  MYADDR-clear accept-all + masked-multicast compare + unicast MAC
  compare (xemu-literal, including the reset-zero-regs match-all
  quirk — the multicast path skips only on a programmed broadcast
  address); oversize (`>1500+128`) drop; DMA-write into desc.buf with
  `len=size` writeback and `BIT4|DESCRIPTORVALID` flags (AVAIL
  consumed); RX (`0x2`) IRQ; ring clamp/wrap mirroring the TX
  helpers; RX counts = high-16 mask + 1. Delivery enters via
  `PollReceive` (backend drain, called from `Advance`) or the
  one-shot `InjectFrame` (CLI `--net-inject <hex>`); a frame that
  meets no AVAIL buffer is held in a pending slot (QEMU
  hold-and-retry) for a later poll. Counters:
  `rx_pkts/rx_bytes/rx_drop_filter/rx_drop_nobuf/rx_oversize/cant_rx`.
- **Deliberate deviation (logged):** xemu never raises RX_NOBUF
  (`dma_packet_to_guest` returns -1 silently; verified by grep — no
  `NOBUF` raise exists in `nvnet.c`). XGrid raises the specified
  `0x4` IRQ on the no-buf miss instead: the forcedeth driver
  replenishes rings off that interrupt, so a silent drop would wedge
  it. Everything else is xemu-parity.
- **P3 backend:** `SocketNetworkBackend` (default stays
  loopback/logger; `--net-backend socket` selects it): virtual
  gateway `192.168.0.1` (locally administered MAC
  `52:55:0A:00:02:01`), ARP responder for the gateway, ICMP echo
  responder, UDP NAT relay via host sockets (unicast + broadcast,
  per-4-tuple mappings, 60 s sweep, computed IP/UDP checksums),
  guest IP/MAC learning from outbound frames; TCP and non-gateway
  protocols dropped + counted. TAP/bridge out of scope; TCP proxy /
  port-forward is the follow-up (dashboard FTP/telnet are inbound
  servers). All socket polling is non-blocking (`TryReceive`).
- **Link (P3):** `SetLinkUp` (`--net-link down|up`); down applies
  immediately (LINK_ST+AN_COMP+ANLPAR-ACK cleared, ADAPTER LINKUP
  cleared); up with AUTOEN && !AN_COMP pends a 250 ms autoneg timer
  in `Advance` (xemu `AUTONEG_DURATION_MS`), else completes
  instantly (LINK_ST only, no AN_COMP — xemu parity). Both
  transitions raise MII LINKCHANGE + MIIEVENT (`0x40`); W1C clears
  both status words. Reset restores instant link-up (AN_COMP set),
  matching the established steady state.
- **§81 open questions resolved:** BIT2 gates RX as well as TX with
  reason-string + counter logging; AN_COMP stays instant at reset
  (no driver wait ever observed — guest is init-only, zero
  ring/KICK); SIZE counts are mask+1 on both rings; filter regs are
  latched AND enforced (defaults accept-all, so init-only guests see
  zero change); first real frame type is still open (no guest TX on
  boot) — loopback round-trip proves the path with broadcast frames.
- **Validation:** 30 new tests (14 RX: AVAIL consume + writeback,
  2-desc wrap, filter match/mismatch matrix incl. masked multicast,
  no-buf + short-desc NOBUF IRQ, oversize, gate reasons, pending
  redelivery, Advance drain, loopback TX→RX round-trip; 8 link:
  down-blocks-TX/RX + MIIEVENT + W1C, 250 ms autoneg partial/full,
  instant-no-AUTOEN, idempotence, pending-cancel, reset, idle-Advance
  side-effect-free; 7 socket:
  ARP reply bytes, ARP miss, ICMP echo + checksums, UDP loopback
  relay round-trip with re-encapsulation, runts, TCP drop, disposed;
  1 machine: RX through guest RAM → PIC IRQ4 assert/W1C deassert).
  All 28 pre-existing nvnet tests green unchanged; guest
  init-only behavior unchanged (STATUS 0, IDLE-set, zero-KICK
   steadiness; `Advance` with an empty backend is a no-op).
   Full suite 986 green.

## 89. 007 APC-suspect refuted: single-threaded guest pipeline stall, not a delivery gap (2026-10-12)

Work in `Temp\opencode\worktrees\007-apc` (analysis-only, clean —
temp forcing/observability fully reverted; logs in
`Temp\opencode\007-apc\`, incl. 3.3 GB full log + ramdump).

**0x2AA65 capture** (break-world, forced `[AFE34]=2`, reverted):
2.3B-step break-log on 0x2AA65/0x2ABE0/0x2A740/0x2A830 — 0x2A740 =
4,402,438 hits, 0x2A830 = 4,402,334 hits (the wait poll, firing to
step 2,299,999,893), but **0x2AA65 fired 76× (last 1,608,143,961)
and 0x2ABE0 75× (last 1,608,143,784)** — the refill producer goes
permanently silent 700M steps before end. Bit-exact repro (pump
first-hit 562,606,460 = prior run to the step).

**APC/thread findings — prime suspect REFUTED.** 8.8M samples: ONE
thread (`0xD002F018`) owns waiter+refill end-to-end; alertable=0
always; no producer thread, no pending async op, IDE idle, DVD tail
frozen at 16 READs with zero errors — nothing for KiDeliverApc to
deliver; the wait is not APC-shaped. Disasm: `0x20B50` drains
chunks via 0x2A740, validates tag==`MPCh`, releases via 0x2ABE0
(which 2→1-clears AND calls refill `0x2A950`); `0x2AA65` is the
refill's starved-exit (`mov [esi+0x44],2`); `0x30820` is SSE memcpy
(movntps correctly emulated — verified), not a DVD read; DVD data
path byte-exact (RAM tag == file bytes). End-state catch-22: the
final 0x2A00 B WERE memcpy'd (tag landed, watch-proven writer
`0x30903`=MPCh) but never linked into the consumable queue
(linking only in 0x2A950, reachable only via 0x2ABE0, which the
waiter only calls on a found chunk); `[STRM+0x44]` stays 2. Both
waiter exits unreachable — deterministic guest-side single-threaded
pipeline stall, ~160/669 sectors of `EAG.MPC` unplayed. Consistent
with §85's force-probe exiting permanently (simulates the EOS the
guest never signals).

**Verdict:** no emulation layer is implicated — no device fix, no
CPU/APC fix indicated. Any unblock is a guest-behavior decision
(EOS semantics), not a correctness fix; longer runs ruled out.
Next (if ever): decide whether to emulate the missing EOS signal
as a compatibility quirk, with the force-probe as the reference
behavior.
## 90. ZCLIP bounds + Z_FORMAT float depth modeled in the software rasterizer (2026-10-12)

Work on `wave/nv2a-zclip`. Closes the two §87-listed remainders
("ZCLIP bounds + float-depth remain untracked"); fixed-function
foggen, exotic texture modes, and GL/Vulkan backends stay open.

**Reference facts extracted (no code copied).** xemu
`hw/xbox/nv2a/nv2a_regs.h`: Kelvin `SET_CLIP_MIN` = `0x394`,
`SET_CLIP_MAX` = `0x398`, `SET_CONTROL0` = `0x290` with `Z_FORMAT`
bit 12, `SETUPRASTER_Z_FORMAT` bit 29. `pgraph/pgraph.c`:
`SET_CONTROL0` feeds its Z_FORMAT bit into `SETUPRASTER_Z_FORMAT`;
`SET_CLIP_MIN/MAX` store the raw word into `NV_PGRAPH_ZCLIPMIN`
(`0x1A90`) / `ZCLIPMAX` (`0x1ABC`); all pgraph regs reset to zero
(`nv2a.c` memset). `pgraph/glsl/common.c`: the fragment uniform is
`clipRange = [0, zmax, *(float*)ZCLIPMIN, *(float*)ZCLIPMAX]` with
`zmax` = zeta-format maximum, or the float variant when
`surface_shape.z_format` is set (Z16: `f16_max`, Z24S8: `f24_max`).
`pgraph/util.h`: `f16_max = 511.9375`, `f24_max = 1e30`; the custom
float decodes are zero-special-cased bit shuffles
(F16: `(f16<<11)+0x3C000000` as float32; F24: `(f24&0xFFFFFF)<<7` as
float32). `pgraph/glsl/psh.c` `set_psh_state`: Z16+Z_FORMAT = F16,
Z24S8+Z_FORMAT = F24 (else D16/D24); per fragment, device-unit
`zvalue` is discarded outside `[clipRange.z, clipRange.w]` iff
`depth_clipping` (`ZCOMPRESSOCCLUDE.ZCLAMP_EN == CULL`), else clamped
into it; depth store is `floor(z)/65535` (D16),
`uintBitsToFloat(floatBitsToUint(floor(z)/16777216)+1)` (D24, the +1u
bit-trick reproduces /16777215 under GPU multiply rounding), or
`zvalue / clipRange.y` (float). `pgraph.c`
`get_clear_depth_stencil_value`: clear words decode the same way
(F16/F24 field + stencil byte for Z24S8), normalized by the float max.
Vertex clip planes and the guard band have no explicit regs — in xemu
they are GL-hardware clipping after the epilogue's
`oPos.xyz *= oPos.w` re-projection (`vsh-prog.c`); only the ZCLIP
fragment clamp/discard is conventional state.

**XGrid mapping (`src/XGrid.Nv2a/Nv2aMmio.cs`).** New methods
`0x290` (latches Z_FORMAT bit 12 into `_zFormatFloat`), `0x394/0x398`
(reinterpreted as float into `_zclipMin/_zclipMax`, setting
`_zclipActive`). `ZetaDepthRange` takes the float flag (F16/F24 maxima,
unknown formats still fall back to 1). The vertex divide is unchanged
in shape, now range-correct for float surfaces. The fragment stage —
exactly where xemu applies ZCLIP — gains `ApplyZclip` (device-unit
clamp + `ZclipClampedPixels` diagnostic) and float-aware zeta
decode/encode (`ConvertF16/F24ToFloat` = the util.h formulas;
`ConvertFloatToF16/F24` = their inverses for the normalized compare/
store path). Clear stays raw passthrough (the driver supplies
already-encoded clear words, as on hardware).

**Deliberate deviations (logged).** (1) Unprogrammed ZCLIP is
passthrough (`[0,+Inf]`, legacy `[0,1]` clamp bit-exact): xemu has no
enable and resets both regs to 0.0 (= kill-everything until written),
but guests always program the bounds before 3D (dashboard writes
`0x398 = 16777215`), so passthrough-until-written is observationally
identical and keeps every existing golden bit-exact by construction.
(2) Only the clamp path is modeled; the CULL/discard variant needs
`ZCOMPRESSOCCLUDE` state that no Kelvin method currently feeds —
follow-up if a guest ever uses occlusion cull. **[Corrected in §95:**
the Kelvin method exists — `NV097_SET_ZMIN_MAX_CONTROL` = `0x1D78`
feeds `ZCOMPRESSOCCLUDE.ZCLAMP_EN`, the dashboard programs it live
(`0x1D78 = 1` → CULL), and xemu's behavior is a **per-fragment
discard** (not primitive culling). §95 implements it; §90's live run
was clamp-only and therefore painted 21,526 far-plane fragments that
the CULL path now discards (final frame unchanged).**] (3) The F16 encoder
saturates at `0xFFF8`: the raw inverse reaches `0xFFFF` at 1.0, which
xemu's own decode maps back to a *negative* float (bit-add wraps past
`0x80000000`); `0xFFF8` is the largest field decoding sane (511.5),
monotonicity preserved. F24 needs no saturation (1e30 sits far below
its encoding's wrap). (4) Pinned, not fixed: incoming normalized depth
*exactly* 1.0 stores word 0 in the fixed path
(`(uint)(1.0*16777215+0.5) = 0x1000000`, shifted out by the `<< 8`
zeta pack; xemu stores 1.0) — pre-existing, out of scope, covered by
`Zclip_DisabledPassthroughPreservesSaturation`.

**Validation (in-worktree).** 8 new `PgraphTransformTests`: ZCLIP
inside (no-clamp, golden-identical word), above-max clamp, below-min
clamp, boundary-max inclusive with zero clamp count, disabled
passthrough, F16 round-trip (exact `0xEFFF` halves), F24 round-trip
(exact `0xE193E5` word, stencil preserved), float LESS ordering
(0.25 passes writing exact `0xDFFF`, 0.75 killed, word untouched).
Nv2a suite 169/169 (161 pre-existing unchanged, incl. the
`0xFF445566`/`0xFF202020`/fog/texture goldens); full suite 1001/1001;
`dotnet format --verify-no-changes` clean.
**Live boot (1B steps, §87 recipe + `--frame`, logs in
`Temp\opencode\nv2a-zclip\`):** no fault, and every rendering counter
is bit-identical to the §87 post-fix run — draws 33632, skipped 1402,
pixels 118798338, textured 233381688, blended 45302409, depth-tested
136, clears 695, frames/stalls 686, frame 640×480 nonzero 57087, frame
SHA `CC9D82DC…` identical. The `--pgraph` stream proves the new path
was live: guest writes `0394 = 0.0` (398×) and `0398 = 16777215.0`
(397×), so `_zclipActive` held through the 3D boot and every depth
fragment ran `ApplyZclip` — `zclip=0` (nothing out of bounds, correct
for containing bounds) via the bit-exact in-bounds fast path.
`0290` arrives with Z_FORMAT set 153× early in the stream, but no
float-mode draw exists in this window (pixel-identity with the fixed
run proves the flag is clear during all 33K draws). End EIP
`0x1E0F1` vs §87's `0x1DF11` is benign guest-side timing variance
(RTC wall-clock reads); rendering is unaffected.

## 91. NV2A fixed-function foggen + HILO dot maps (2026-10-12)

Work in `Temp\opencode\worktrees\nv2a-foggen-hilo` (wave/nv2a-foggen-hilo).
Closes the two §78 remainders. Reference facts (`References2/xemu-0.8.136`,
reimplemented scalar, never copied):

- **Gen values** (`nv2a_regs.h:917-922` -> `pgraph.c:1131-1150` CSV0_D):
  SPEC_ALPHA 0, RADIAL 1, PLANAR 2, ABS_PLANAR 3, FOG_X 6 (4/5/7+
  assert on hardware).
- **Distances** (`pgraph/glsl/vsh-ff.c:448-473`, from NV_fog_distance):
  SPEC_ALPHA = `clamp(specular.a, 0, 1)`; RADIAL =
  `length(tPosition.xyz)` with tPosition = position x modelView
  (eye space, skinning-aware; `vsh-ff.c:171-173`); PLANAR =
  `dot(fogPlane.xyz, tPosition.xyz) + fogPlane.w`; ABS_PLANAR =
  `abs(...)`; FOG_X = `fogCoord` = `v5.x` (`vsh-ff.c:78`).
- **fogPlane** = transform constant `NV_IGRAPH_XF_XFCTX_FOG` = `0x39`
  = c57 (`nv2a_regs.h:1343`, `vsh.c:183`; fed by `0x09D0-0x09DC`,
  `pgraph.c:1819`). **FOG attr** = vertex attribute 5
  (`nv2a_regs.h:1474`); `SET_FOG_COORD 0x1698` replicates the scalar
  inline value x4 without finishing a vertex (`pgraph.c:2155-2163`).
- **Flow** (`vsh.c:301-327,334-403`): fixed-function -> gen distance;
  programmable -> `oFog.x` unconditionally (with a FIXME noting
  foggen is ignored there — the RollerCoaster case writes `oFog=v0.z`
  itself); shared LINEAR/EXP/EXP2 (+ABS) equations; inf distance and
  NaN factor both yield the mode's fallback; `vtxFog = oFog.x`; psh
  feeds `(fogColor.rgb, clamp(factor, 0, 1))` as FOG (`psh.c:1074`).
  Specular alpha: unlit `oD1 = specular` (v4), lit `oD1.a =
  specular.a` (`vsh-ff.c:254-258,288`) — so SPEC_ALPHA == v4.a always.
- **HILO** (`psh_regs.h:143-153`, `psh.c:851-910,1246-1265`): applied
  as `dot(pT[i].xyz, dotmap(t[input_tex[i]]))`. Map 4 (`dotmap_hilo_1`)
  is a real decode: hi = (a:r bytes)/65535, lo = (g:b bytes)/65535,
  z = 1, with C-style truncation (`uint(x*255)`). Maps 5-7 are
  identity FIXMEs. Any map > 3 logs `NV2A_UNIMPLEMENTED`
  (`psh.c:1104-1106`).

XGrid wiring (`src/XGrid.Nv2a/`): `VshProgram.FogGenDistance` (new;
gen consts 0/1/2/3/6; unknown -> SPEC_ALPHA); `Execute` overload
reporting `fogWritten` (set on any oFog masked write); vertex path
uses `oFog.x` when written, else the gen distance from v5.x (FOG_X),
v4.a (SPEC_ALPHA), v0.xyz (RADIAL/PLANAR) against c57; `SET_FOG_COORD
0x1698` stages `_inlineValues[5]` x4; `PshCombiner.ApplyDotMap` takes
the texel alpha and decodes map 4 (maps 5-7 stay identity +
`UnsupportedDotMaps`, mirroring FIXME+UNIMPLEMENTED).

**Deliberate deviations:** no fixed-function transform unit exists
(the uploaded program always runs the transform), so RADIAL/PLANAR
consume the object-space position, not eye-space tPosition; the
gen fallback triggers on unwritten-oFog rather than on execution
mode (a fog-enabled program that never writes oFog gets the gen
distance instead of distance 0 — the reference FIXME admits this
corner is uncertain); fog-disabled keeps the established fixed
`(0,0,0,1)` input, not xemu's `vec4(1.0)` (§78 invariant kept).

**Validation:** 11 new tests (`Nv2aFogTexModesTests`): unit goldens
per gen mode (clamp/abs/unknown-fallback, RADIAL (3,4,0)->5,
PLANAR 2.5, FOG_X passthrough) + `fogWritten` true/false; draw
goldens via a factor-routing combiner (FOG alpha -> pixel, LINEAR
(1,1)): FOG_X 0/1/2/-1, SPEC_ALPHA 0/1/2/-0.5, RADIAL saturated
white at z=2, PLANAR 0/1/sloped-2z, ABS-vs-PLANAR pair on plane -1,
oFog-wins precedence, disabled-white invariant under staged
gen+coord; HILO DOT_ST chain (green second texel, zero fallback
count) with an identity-map black control. Full suite 1004 green
(993 + 11); all pre-existing goldens (`0xFF445566`, `0xFF202020`,
fog red `0xFFFF0000`, BUMP_LUM `0xFF808080`) bit-identical.
**Remains:** cubemap/reflection/project3d/clipplane/BRDF modes,
HILO hemisphere 5-7, ZCLIP + float depth, backends.

## 92. 007 EOS compatibility quirk: STRM 2->0 exits 0x20B50 to the next gate (2026-10-12)

**COMPATIBILITY QUIRK** (not a correctness fix; §89: no emulation
layer at fault). Reference: §85 force-probe (state 2->0 at 2.2B
exits permanently -> 800M new-phase steps, splash unchanged, DVD
tail frozen).

**Guest facts** (capstone over `Temp\opencode\007-wait2\text_*.bin`,
reimplemented reading, never copied): `0x20B50` waiter =
`0x2A740` (chunk or 0) -> `0x26AE0` pump -> 4 B tag build, `cmp
0x6843504D` (`MPCh`), `je 0x20BB4` (exit (a): `inc [AFE34]`,
return chunk), else `0x2ABE0` release + `0x2A830` check; empty
chunk -> `0x2A830` check -> `je` loop else return 0 (exit (b)).
`0x2A740` checks `STRM` magic + `[handle+8]` remaining + queue
walk. `0x2A830` returns 1 iff magic `0x4D525453` AND
`[STRM+0x44]==0` AND `[handle+8]==0` (EOS). `0x2ABE0` (`0x2AC20+`)
is the only 2->1 clearer + refill `0x2A990`, reachable only on a
found chunk (catch-22). `0x2AA65` is the refill starved-exit
(`mov [esi+0x44],2`). `0x30820` is SSE `movntps` memcpy (writer
`0x30903` lands the `MPCh` tag), not a DVD read.

**Parked signature** (bit-identical across `runE_2b5`, `007-apc`
`run1`, `runF_3b` + in-worktree `repro_23b` quirk-off 2.3B):
TitleId `0x4541000D` (cert `0x10178+8` via MMU walk of `0x10118`;
live-MMU lesson: walk from CR3, e.g. `0xAFE34->0x15EE34`, never
`+0xAAF000`); STRM VA `0x80B6C800` magic `STRM`, handle VA
`0x80B6CBE8->STRM`, `handle+8==0`, `STRM+0x44==2`,
`STRM+0x60=0x80B6FD30` with `MPCh`, `handle+0xC=0x80B6CC48` with
`0xFFFFFFFE` (tag present but unlinked). `repro_23b` (temp
`[AFE34]=2` forcing, reverted): EIP `0x26B44` (`0x26AE0` spin),
EBP=handle, frames 84 stall, STRM bytes above, `checks=0
fires=0` — confirms §89 in-worktree.

**Quirk design** (`src/XGrid.Host/SevenEosQuirk.cs`,
`XboxMachine.cs`, `src/XGrid.Cli/Program.cs`;
`tests/XGrid.Integration.Tests/SevenEosQuirkTests.cs`): gate =
TitleId + exact signature above via live MMU (`sideEffects:false`
reads, `sideEffects:true` write) + 10M-step stability (transient
starves resolve in <1M; park persists 700M+). Effect (minimal) =
single-dword `STRM+0x44` 2->0 (exactly the force-probe), letting
the waiter exit through its own (b) path (`0x2A830->1`,
`0x20B50` return 0); never touches EIP. Fires once (state breaks
to 0), bounded. Observable = `SevenEosQuirkChecks/Fires` +
`007 EOS quirk on/off` + `fired at step ...` + final
`enabled/checks/fires` line. Flag = default-on with
`--no-007-eos-quirk` opt-out (safe: exact gate + stability +
once + 1B-step no-fault reference; dashboard/other titles never
match TitleId; opt-out preserves A/B purity).

**Live validation** (worktree Release, limiter forcing to reach
the park quickly, reverted; logs `Temp\opencode\007-eos\`):
quirk-on 3B fired at step 2B (`EIP=0x138E4`, `checks=45775`,
`fires=1`), state 0 persisted to 3B with staged `MPCh` intact,
EBP `handle->2` (waiter exited `0x20B50`), EIP `0x13894`
(streaming, not waiter `0x26B44`), no fault over 1B post-fire
steps. Frames 84->85 stall (not past 101), EIPHIST no `0x602F7`
(no `GetState` polling), splash BMP full-white (307200 nonzero,
unchanged), DVD tail frozen — matches the force-probe reference
(exits to the next gate, splash/DVD frozen). A/B: quirk-off 2.3B
stays parked (state 2, EBP handle) vs quirk-on 3B exits (state 0,
EBP 2); no corruption, no spurious fire. Suite 1015/1015 (993 +
22 new); build + format clean.

**Next gate (precise):** after the (b) exit (return 0) the game
leaves the STRM wait (EBP proof) but parks in the `0x138xx`
splash/streaming loop (`EIP=0x13894`, `ESI/EDI=0`, `EBP=2`,
frames stall, no input polling, DVD frozen). `0x602F7` never
reached. Further unblock needs decoding the `0x138xx` wait, not
STRM.

## 93. Scene-proof verdict: layout is the RIGHT scene, color is broken (textured-RGB-zero bug) (2026-10-12)

Verdict mission in `Temp\opencode\worktrees\scene-proof`
(`wave/scene-proof`, clean, no commit — analysis only). Worktree
Release CLI, firmware read-only, HDD COW-verified read-only; logs/BMPs
in `Temp\opencode\scene-proof\`. Recipe confirmed live post-everything
(both HITs at the exact established steps; submenu SHA `CC9D82DC…`
reproduced exactly).

**Correction to §87:** `nonzero` counts any nonzero BGRA byte — 99%
of the 57K nonzero pixels are **RGB=(0,0,0) with nonzero alpha**, so
the frame is still visually black. "First non-black frame" meant
first alpha-bearing frame.

**Right-layout evidence:** neutral SHA `7db7b642…` vs submenu
`cc9d82dc…` — DIVERGE now (were identical pre-fix). Full-BGRA diff:
10,345 px (3.37%), ALL alpha-only (RGB-change = 0), confined to
y=320–459 (list area); above y=320 pixel-identical (stable header).
Alpha carries an antialiased image (65/39 distinct values, no
NaN/garbage); row-ink profile proves different lists (neutral narrow
strip y320–340 + block y364–404; submenu wide block y380–404);
ASCII render shows identical header text (y20–210) + different
centered panel/rows — glyph-like blocks, highlight-bar-scale rows,
coherent rects, no triangle storms. RGB palette: 4 colors total
(black + 1px steel-blue column at x=0, full height).

**Precise bug pointer (unfixed):** textured-draw RGB is dropped while
alpha/coverage survives — every UI quad (background, glyphs, panels)
lands RGB=0 + structured alpha, while ONE untextured draw (the x=0
blue column, RGB ~52,109,165) keeps full color. So the RGB write path
works and the fault is draw-dependent. Prime suspect: texture-sample
RGB returning 0 with alpha intact (M6 swizzle/decode or combiner
diffuse×texture modulation) — not a global color-mask/blend-zero
(those would kill the blue column too). Secondary: background
alpha=0 over 250K px suggests the clear color itself may be
(0,0,0,0). **Next:** fix textured-RGB-zero, re-run this exact two-boot
recipe — success = BMPs differ in RGB with readable colored UI. The
alpha channel is meanwhile a valid layout oracle: any RGB fix must
preserve these alpha structures pixel-for-pixel.

## 94. 007 EOS quirk merged; game reaches the 0x138xx next gate (2026-10-12)

`wave/007-eos-quirk` merged (§92: `SevenEosQuirk.cs` pure predicate —
TitleId `0x4541000D` + STRM magic + handle linkage + remaining 0 +
state 2 + staged MPCh + unlinked current + 10M-step stability, fires
once; effect = single dword `STRM+0x44` 2→0, exactly the §85
force-probe; waiter exits via its own (b) path, never touches EIP;
default-on with `--no-007-eos-quirk` opt-out; CLI counters/log;
22 tests). Gate is dashboard-safe (TitleId can never match).
Validated live: quirk-off repro confirms the park (EIP `0x26B44`,
frames stall at 84); quirk-on fires at 2B (`checks=45775 fires=1`),
state persists, EBP handle→2, EIP `0x13894`, **no fault over 1B
post-fire** — but frames 84→85 stall: the game parks in the
`0x138xx` splash/streaming loop (`ESI/EDI=0`, no input polling,
DVD frozen, splash full-white). The STRM gate is behind us; the
next gate is decoding the `0x138xx` wait. Full suite 1034/1034
(993 + 8 ZCLIP + 11 foggen/HILO + 22 EOS).

## 95. Textured-RGB-zero fixed: linear texel coords, POT size for swizzled, A4R4G4B4 (2026-10-12)

Worktree `Temp\opencode\worktrees\nv2a-rgb-zero` (`wave/nv2a-rgb-zero`);
firmware read-only; artifacts in `Temp\opencode\rgb-zero\`. Reference
facts from `References2/xemu-0.8.136/hw/xbox/nv2a/pgraph/` (`texture.c`
`pgraph_get_texture_shape`, `glsl/psh.c` sample sites + `norm`,
`gl/vk/constants.h` format tables). The §93 symptom (right layout,
RGB=0, structured alpha, one untextured blue column) had THREE
independent causes, all in the texture-sample stage; the combiner,
blend and color-mask paths were correct.

**Evidence (1B-step neutral boot, per-pixel instrumentation).**
- Per-format sample attempts: `12=209,817,600/0  04=44,942,064/44,942,064
  06=22,642,488/0` — every A4R4G4B4 (`0x04`) sample was a miss
  (unsupported -> `SetTexelNone` (0,0,0,1) -> RGB 0, alpha 1: the exact
  §93 signature), and 219.9M/232.2M successful samples decoded RGB zero.
- Per-texture-state UV bounds showed the smoking gun: the dominant
  state (`LU_IMAGE_A8R8G8B8`, offset `0x369C000`, rect 1024x512,
  pitch 0x1000, stage mode 1/PROJECT2D, 199.4M samples) had
  `u=[0.49995,639.50006] v=[0.49999,479.50003]` — **texel-unit**
  coordinates, not normalized. The 1024x512 allocation at that offset
  holds a 640x480 image in its top-left corner only (RAM scan: 307,200
  nonzero of 524,288); XGrid's `floor(u*1024)` scrambled the samples
  into the zeroed right/bottom region (first sample address `0x379CFFC`
  = (1023,256), word 0), giving 199.06M rgb0.
- A second state (`SZ_A4R4G4B4`, offset `0x3BC1000`) declared
  BASE_SIZE_U/V = 128x256 (`format 0x08710429`) while the image rect
  said 1024x512. Unswizzling with 1024x512 masks produced a garbled red
  gradient (the red text blocks in the broken frames); unswizzling with
  the 128x256 masks produced the clean font atlas (glyphs `!#$...`).
  The image rect does not apply to swizzled formats.
- Mode fallbacks `187.5M` (`0x03` CUBEMAP 125.1M + `0x02` PROJECT3D
  62.4M) are real but decorative at this stage (parks in stages 1-3 of
  `m0332`/`m1332` states); the readable-UI result does not need them.
- Combiner fragments with zero RGB: 294.4M before, of which 95.3M had
  nonzero alpha (the §93 image-in-alpha).

**Reference semantics (facts).**
- `pgraph_get_texture_shape`: for `f.linear` formats
  (`LU_IMAGE_*`) `width = rect_width, height = rect_height` (method
  `SET_TEXTURE_IMAGE_RECT 0x1B1C`, WIDTH bits 16-31 / HEIGHT bits 0-15)
  and pitch from `SET_TEXTURE_CONTROL1 0x1B10` bits 16-31; for swizzled
  (`SZ_*`) formats `width = 1 << BASE_SIZE_U`, `height = 1 <<
  BASE_SIZE_V` (format word bits 20-23 / 24-27), rect and pitch ignored.
- `glsl/psh.c` wraps every sample of a `rect_tex` (linear) texture in
  `norm(coord) = coord / textureSize`, i.e. the guest supplies
  **texel-unit** coordinates for LU formats; swizzled formats sample
  normalized coordinates directly. (`textureProj` also divides by w;
  not needed for the dashboard's oT0.)
- Format bit layouts (`gl/vk/constants.h`): SZ_A8R8G8B8/LU_A8R8G8B8 =
  B8G8R8A8 in memory; A4R4G4B4 = A bits 12-15, R 8-11, G 4-7, B 0-3;
  A1R5G5B5 = A bit 15, R 10-14, G 5-9, B 0-4; R5G6B5 = R 11-15, G 5-10,
  B 0-4; X8/X1 variants force alpha 1.

**Fix (`Nv2aMmio.TrySampleTexture`).** (a) Linear formats take
`x=floor(u)`, `y=floor(v)` (wrap/clamp in texel space) with
offset+pitch addressing; swizzled formats keep `x=floor(u*width)` with
Morton offsets. (b) `width`/`height` now come from the image rect for
linear formats and from BASE_SIZE_U/V for swizzled ones. (c) Decode
table extended to the SZ/LU pairs 02/10 (A1R5G5B5), 03/1C (X1R5G5B5),
04/1D (A4R4G4B4), 05/11 (R5G6B5), 06/12 (A8R8G8B8), 07/1E (X8R8G8B8)
with a shared per-texel decoder; the `0x04` font format was the
44.9M-miss hole. Unsupported formats still fall back to NONE and count.

**Instrumentation (kept in the CLI).** `NV2A tex ok/rgb0/disabled/
badsize formats=<fmt=attempts/misses> fallbacks=<n> byMode=<mode=n>
dotMaps=`, `NV2A combiner rgb0=/rgb0Alpha=`, and top-16
`NV2A texstate` lines: per (combiner/program) state with texture
enable/format/offset/rect/ctl1/address per slot, texture modes, stage
count, final-enable, sample/rgb0/fallback/miss counts, first
address/texel, and UV bounds. Tests updated for the corrected
encoding: swizzled tests now set BASE_SIZE bits; linear-texture tests
use texel-unit UVs; the fog/texmode helper uses swizzled A8R8G8B8
(per the reference `!rect_tex` assert for dependent/bump modes) with
the 4x4 data at its Morton offset.

**Live validation (final build, 1B steps, both boots).** Recipe
reproduced exactly: `HIT 972246034 EIP=0001859D` and
`HIT 972349247 EIP=0001E420`, no fault either run. Counters neutral:
draws 33811 skipped 1394 pixels 284,296,413 textured 277,402,152
blended 210,800,484 depth 136 zclip 0 frames 682; submenu: draws 33623
pixels 285,174,170 blended 211,678,241 frames 685 (all >= the §93
pre-fix run; no regression). Texture stats: `04` misses now 0;
rgb0 samples 44.2M (mostly genuinely black texels). Frames
(`final_neutral.bmp` / `final_submenu.bmp`, SHAs `df0feb86...` /
`4ef1fbf6...`): full-BGRA diff 7,390 px (2.41%), **RGB-change 7,389,
alpha-only 1**, confined to y=320-459 (list area), header pixel-identical;
306,817/307,200 nonzero-RGB pixels both (32.4K distinct colors).
The §93 alpha-only structures are **superseded** (texture alpha now
feeds the blend, so the layout oracle is the RGB diff in the same
region); the steel-blue x=0 column is gone as a separate artefact
because the untextured draw now sits inside a fully textured scene.
Visually the frame is the readable EvoX main menu (time/BIOS/free-space
header, XBMC/Launch DVD/Games/Trainers/Skins/Xbox Admin list, SELECT A /
B BACK bar) and the submenu shows Applications/Dashboards/Switch
Dashboards/System.

**Tests.** 4 new: `ElementDraw_LinearTextureSamplesTexelUnits`,
`ElementDraw_SwizzledTextureUsesBaseSizeNotImageRect`,
`ElementDraw_SwizzledA4R4G4B4DecodesNibbles`,
`ElementDraw_SwizzledX8R8G8B8ForcesOpaqueAlpha` (all fail pre-fix);
9 existing texture tests updated for the reference semantics. Full
suite **1038/1038** (was 1034), Nv2a 180 -> 184; `dotnet format
--verify-no-changes` clean.

**Remains.** PROJECT3D/CUBEMAP stage modes (parallel wave; 187.5M
falls here but they are decoration), HILO dot maps 5-7, reserved
formats, mipmap LOD selection (base level only), DXT decode.

## 96. NV2A exotic texture modes + HILO hemispheres implemented (2026-10-12)

Closes the §78/§91 remainder: cubemap, project3d, clipplane, the
reflection dot-chain modes, and HILO hemisphere dot maps 5-7. Primary
reference `References2/xemu-0.8.136/hw/xbox/nv2a/pgraph/glsl/psh.c`
(mode switch ~L1108-1369, sampler types L620-695, dotmap funcs
L851-910, remap helpers L936-976), `pgraph/texture.c`
(`pgraph_get_texture_length` L143-192, shape L194-326),
`pgraph/gl/texture.c` (`generate_texture` L642-758: cube faces
uploaded +X,-X,+Y,-Y,+Z,-Z, each face = full mip chain rounded to
`NV2A_CUBEMAP_FACE_ALIGNMENT` = 128, `nv2a_regs.h:1518`; volume upload
L564-635: per level `unswizzle_box`, level advance
`w*h*depth*bpp`), `pgraph/swizzle.c` (`generate_swizzle_masks`
L48-70: x/y/z mask bits woven in that order), `nv2a_regs.h`
(TEXFMT CUBEMAPENABLE bit 2 / DIMENSIONALITY 0xC0 / MIPMAP_LEVELS
0xF0000 / BASE_SIZE_P 0xF0000000, TEXADDRESS ADDRP 0x70000,
SHADERCLIPMODE 0x1994), `pgraph.c:2934` (NV097
SET_SHADER_CLIP_PLANE_MODE = `0x17F8` writes it).

**Equations implemented** (`PshCombiner.cs` static math + `Nv2aMmio`
ResolveTexel/samplers):
- PROJECT2D: non-cube unchanged (bit-identical); with CUBEMAPENABLE:
  `normalize(vec3(1, v/w, -u/w))` (`remap2DToCube`) then a cube sample.
- PROJECT3D: `(u/w, v/w[, s/w])`; 2D sampler for dim 2, swizzled
  volume sampler for dim 3 (`unswizzle_box` layout, level 0).
- CUBEMAP: cube sample of `(u,v,s)` when CUBEMAPENABLE, else
  `remapCubeTo2D` (`+/-X: (-z,y)/|x|`, `+/-Y: (x,-z)/|y|`,
  `+/-Z: (x,-y)/|z|` with xemu's strict-`>` branch order) then a 2D
  sample with the normal address modes.
- Face select + s/t from the GL/D3D convention (`|ma|` division,
  `(c/|ma|+1)/2`), Mesa-style `>=` tie break; face stride =
  `ALIGN128(sum over clamped mip levels of w*h*bpp)`.
- CLIPPLANE: for stage i component j, SHADERCLIPMODE bit `4i+j`
  selects discard when `pT[j] >= 0`, else when `pT[j] < 0`; texel is
  `(0,0,0,0)`; discard applied in the FillTrianglePsh pixel loop.
- DOT_RFLCT_DIFF (stage 2): `n = (dot1, dot2, dot_i_n)` — dot1 from
  stage 1, dot2 from stage 2, the third from stage 3's coords/dot
  map/input (xemu `dot%d_n`); cube or remap sample.
- DOT_RFLCT_SPEC (stage 3): `n = (dot1,dot2,dot3)`,
  `e = (oT1.w, oT2.w, oT3.w)`,
  `rv = 2*n*dot(n,e)/dot(n,n) - e`; cube or remap sample.
- DOT_STR_CUBE (stage 3): `(dot1,dot2,dot3)` cube/remap sample.
- Reflection dots are recomputed on demand (`TryComputeDot`, per-stage
  dot map + SHADERCTL input stage), so the stage chain does not
  require the earlier stage's texture mode to be a dot mode.
- HILO hemisphere 5/6/7: `hi = (a<<8)|r`, `lo = (g<<8)|b` (same word
  packing as HILO_1); decode each word with the 16-bit extension of
  sign1/sign2/sign3 — D3D `(h-0x8000)/0x7FFF`, GL
  `h>=0x8000 ? (h-65535.5)/32767.5 : (h+0.5)/32767.5`, generic
  `h>=0x8000 ? (h-65536)/32767 : h/32767` — then
  `(H, L, sqrt(max(0, 1-H*H-L*L)))`. `IsSupportedDotMap` now 0-7;
  maps 8-15 stay identity + `UnsupportedDotMaps`.

**Reference limits (not guessed):** xemu's `dotmap_hilo_hemisphere*`
are FIXME identity (psh.c L902-910) even in current master; the
16-bit decode comes from the Cxbx line (`XbPixelShader.h:127-130`
comments are partially garbled — D3D anchor line contradicts its own
"thus" formula — and the template's `hls()` applies the generic
formula to all three) plus Xermu's `doc/reference/
CxbxPixelShaderFunctions.hlsli` (`ln()`), which states the piecewise
per-flavor decode and mirrors xemu's sign1/sign2/sign3. Documented as
the best available secondary reference; the clamp at `1-H^2-L^2 < 0`
is explicit. BRDF (0x08) and DOT_RFLCT_SPEC_CONST (0x12) have no
implementation in xemu (`vec4(0.0)` + `NV2A_UNIMPLEMENTED`,
psh.c L1240-1245, L1359-1364) and Cxbx's variants are marked untested
(`CxbxPixelShaderTemplate.hlsl:391,402`, `Brdf()` "TODO: Complete 16
bit phi/sigma retrieval"), so they now emit exactly the xemu
observable zero texel `(0,0,0,0)` and count `TexModeFallbacks` — no
speculative math.

**Explicit non-goals kept:** non-cubemap PROJECT2D still ignores the
projective w divide (pre-existing; changing it would alter draws not
under this wave), DOT_STR_3D keeps its 2D sample for dim 3, and
border/mip/filtered sampling remain absent. Pre-existing quirk noted:
the 2D sampler reads TEXADDRESS V from bits 4-7 while `nv2a_regs.h`
puts ADDRV at bits 8-10 (U/V follow file convention in new samplers;
P uses bits 16-18); tests use explicit clamp modes.

**Tests:** `tests/XGrid.Nv2a.Tests/Nv2aExoticTexModesTests.cs` (11):
cube helper conventions (gl +X..-Z, remap, tie), cube face selection
for +X and +Z (2x2 faces at 128-byte stride), cube-flag-off remap on a
flat 4x2 texture, PROJECT3D volume texel selection, CLIPPLANE discard
vs keep, DOT_RFLCT_DIFF remap golden, BRDF zero-alpha golden (routed
through the alpha combiner), HILO map 7 vs map 5 texel selection.
Updated `Nv2aFogTexModesTests`: hemisphere assertions (were
identity/unsupported), mode-validity rows for 0x02/0x03/0x05/0x08/
0x0B/0x0C/0x0E/0x12, and the unsupported-dot-map golden moved to map 8.
Full suite 1045/1045 (1034 + 11); build + `format --verify-no-changes`
clean.

**Live validation:** worktree Release, standard firmware/HDD, dashboard
`boot --steps 1000000000`: EIP `0x000259E0`, no fault, and the full
CLI output is byte-identical to the pre-wave baseline (`Compare-Object`
diff count 0) — `draws executed=33811 skipped=1394
pixels=118927184 textured=232460088 clears=692 blended=45431255
depth=136 zclip=0 frames=682 stalls=682 programLoad=12 constLoad=2
inlineElements=0`, unmapped buckets unchanged. The dashboard exercises
none of the newly enabled paths (they are opt-in via SHADERPROG /
SHADERCLIPMODE / SHADERCTL), so the no-regression check is exact.

## 97. ZCLIP CULL (per-fragment discard) + exact-1.0 fixed zeta encode (2026-10-12)

Work in `Temp\opencode\worktrees\nv2a-zclip-cull` (`wave/nv2a-zclip-cull`).
Closes the two follow-ups left open by §90. **The §90 follow-up
phrasing was wrong on two counts** (record corrected in §90 deviation
(2)): (a) a Kelvin method *does* feed the CULL/CLAMP state —
`NV097_SET_ZMIN_MAX_CONTROL` = `0x1D78`; (b) xemu has **no
primitive-level ZCLIP culling at all** (no `clip_rect`, no draw.c
use) — the "cull" is a per-fragment `discard` in the fragment shader.
An exhaustive search of xemu for `zclip|clip_min|clip_max|CLIP_MIN|
clip_rect|depth_clipping` confirms psh.c is the only consumer.

**Reference facts extracted (no code copied).** `nv2a_regs.h:1270-1273`:
`NV097_SET_ZMIN_MAX_CONTROL` = `0x1D78`, `ZCLAMP_EN` mask `0xF0`,
CULL = 0, CLAMP = 1. `nv2a_regs.h:638-641`:
`NV_PGRAPH_ZCOMPRESSOCCLUDE` = `0x1A84`, `ZCLAMP_EN` = bit 4, CULL 0 /
CLAMP 1. `pgraph.c:2867-2885`: the method writes the masked field onto
`ZCOMPRESSOCCLUDE`; reserved values hit a debug `assert(!"Invalid
zclamp value")` (release: no-op). `nv2a.c:301` memsets all pgraph regs
to zero, so the reset mode is **CULL**. `glsl/psh.c:89-92`:
`depth_clipping = ZCLAMP_EN == CULL`; `psh.c:1058-1067`: CULL is
`if (zvalue < clipRange.z || clipRange.w < zvalue) discard;` and CLAMP
is `zvalue = clamp(zvalue, clipRange.z, clipRange.w);` — bounds are
inclusive, and the section runs at the top of the shader, before alpha
test and independent of depth-test enable (a discard kills color too).
`psh.c:1491-1499` is the D24 store comment: `floor(zvalue)` device
units are written via `uintBitsToFloat(floatBitsToUint(floor(z) /
16777216.0) + 1u)` ("plus one bit" reproduces `/16777215` under GPU
multiply rounding); at `z = clipRange.w` that stores 1.0 = field
`0xFFFFFF`. The float-depth branch carries the only nearby TODO
(`psh.c:1515`, already handled in §90).

**XGrid mapping (`src/XGrid.Nv2a/Nv2aMmio.cs`).** New method `0x1D78`
(`MethodZminMaxControl`): masked value 0 → `_zclipCull = true`,
`0x10` → false, anything else leaves the mode untouched (reference
debug-assert/release no-op). `_zclipCull` resets **true** (xemu's
zeroed `ZCOMPRESSOCCLUDE`). `ApplyZclip` becomes
`bool ApplyZclip(ref float depth, uint zetaBytes)`: out-of-bounds
CULL mode counts `ZclipCulledPixels` and returns false (caller
discards before any write); CLAMP mode is the §90 clamp path. It is
now called at the **top of `ProcessFragment`**, before alpha test and
regardless of depth-test enable, matching the shader's section order —
so CULL discards color-only draws exactly like the reference. Bounds
unprogrammed still means passthrough (§90 deviation (1) unchanged).

**Exact-1.0 fixed-encode overflow (fixed).** XGrid's pre-existing
fixed path rounds normalized depth: `(uint)(1.0 * 16777215 + 0.5)`;
the `+0.5` rounds 16777215.5 up to 16777216 (round-half-even at
ULP = 1), and the `<< 8` zeta pack shifts the overflow out, storing 0.
`EncodeDepth24`/`EncodeDepth16` now cap at `0xFFFFFF`/`0xFFFF`
(`ZetaFixedMax24/16`): 1.0 stores `0xFFFFFF00`, decodes back to
normalized 1.0, and equals xemu's D24 store for `z = clipRange.w`.
The D16 cap is a guard (65535.5 is exactly representable, so the D16
overflow is unreachable). The pre-existing round-to-nearest encode is
kept (not switched to xemu's floor) to preserve every golden; the fix
only removes the wrap.

**Deliberate deviations (logged).** (1) The mode stays CLAMP until
`0x1D78` is first written, whereas xemu resets to CULL; the dashboard
always writes it before drawing (`0x1D78 = 0x00000001`, ZCLAMP_EN
nibble 0, i.e. CULL) so this only affects guests that program bounds
but never the mode — same passthrough-until-programmed compromise as
§90. (2) Clamp/discard still run on the normalized-space interpolated
depth rather than xemu's device-space `vtxPos.z` (inherited ULP-level
difference, §90). (3) A NaN depth is neither culled nor clamped
(xemu's GLSL comparisons are also false for NaN).

**Validation (in-worktree).** 5 `PgraphTransformTests` touched:
`Zclip_AboveMaxClampsToMax`/`Zclip_BelowMinClampsToMin` now program
CLAMP explicitly; `Zclip_DisabledPassthroughPreservesSaturation`
renamed `Depth_ExactOneFixedEncodeStoresMaxWord` and now pins
`0xFFFFFF00` (was pinning the bug's 0). 4 new:
`Zclip_CullModeDiscardsAboveMax` (uses the dashboard's exact parameter
`0x1D78 = 1`; no pixels, no depth test, zeta untouched, cull > 0),
`Zclip_CullModeDiscardsBelowMin`, `Zclip_CullDiscardAppliesWithoutDepthTest`
(color-only draw killed), `Zclip_ReservedZclampValueKeepsProgrammedMode`
(reserved `0x20` is a no-op after CLAMP). Float-depth/Z16/Z24/ordering
goldens unchanged. Nv2a suite 184/184; full suite **1038/1038**
(1034 + 4); build + `dotnet format --verify-no-changes` clean.

**Live boot (1B steps, §87 pad recipe + `--frame`/`--pgraph`, logs in
`Temp\opencode\nv2a-zclip-cull-wave\`):** no fault, Halted=False,
EIP `0x1E0F1` (bit-identical to §90's `boot2.log`), draws 33632,
skipped 1402, textured 233381688, clears 695, blended 45302409,
depth 136, frames/stalls 686, programLoad 12, constLoad 2 — all
identical to §90. `zclip=0` (no clamps) as before; **`zclipCull=21526`**
proves the new path live (pgraph shows `0 1D78 00000001` ×39 in the
logged window, plus two `0394/0398 = 4B7FFFFF` degenerate
[max, max] windows). pixels 118798338 → **118776812**, delta exactly
−21526 = the newly discarded fragments §90's clamp-only model had
painted. The presented frame is **byte-identical** to §90's:
640×480, nonzero 57087, SHA256
`CC9D82DCB9C5D4D2466ABA507AEF57CD4F2AB005300C39D1968C5C0978CFC441`.
Frames/SHAs unchanged because the discarded far-plane fragments were
depth-rejected or overpainted anyway — they were never visible.

**Remains:** cubemap/reflection/project3d/clipplane/BRDF, HILO
hemisphere 5-7, ZCLIP-CULL exact-1.0 corner done (this section),
Vulkan/OpenGL backends.
## 98. 007 `0x138xx` wait decoded: PlayMPC audio-drain loop, blocked by the missing APU voice-processor/DSP position feedback (2026-10-12)

Work in `Temp\opencode\worktrees\007-splash-gate` (`wave/007-splash-gate`,
analysis-only, clean — the temp `--force-afe34` limiter forcing and the APU
write-offset histogram were fully reverted; logs + ramdumps in
`Temp\opencode\007-splash\`).

**The `0x138xx` EIP is a compiler helper, not the wait.** `0x13850` is a
32×32→64 multiply and `0x138C0` a 64/32 divide (`ESI` = divisor); the park
EIPs (`0x13894`, `0x138DB`, `0x138E5`, …) are always inside them. Both are
called from `0x11210` (`0x112B6`/`0x112BD`), plaympc's stream-clock query.
`0x11210` has exactly two callers: the streaming frame limiter `0x207C0`
(`0x207F8`) and the **audio-drain loop** `0x20FC1` (`0x20FCC`). Pre-fire and
post-fire EIPs therefore look alike; the helper is not the loop.

**Loop decode (guest, `plaympc.c`).** Strings at `0x62580-0x62644`
(`d:\Bond\ToolSrc\movXB\plaympc.c`, `PLAYMPC - no MPC chunks found`,
`ASSETS\SOUND\VIDEO\{ATT,HILITE,MGM,EAG}.MPC`, `streambuff`, `audiobuff`).
PlayMPC `0x20BD0`: streaming loop `0x20E40..0x20FA6` = frame limiter
`0x207C0() >= [0xAFE34]-2` (`0x20E45..0x20E6E`, `0x26C60` sleep), chunk
fetch `0x20B50` (`0x2A740` → tag `MPCh`; `0x2ABE0` release + refill
`0x2A950`; EOS check `0x2A830`), demux/submit `0x20780`, timer pump
`0x26AE0`, pad check `0x20A70`. On EOS (`0x2A740`→0 and `0x2A830`→1) the
loop falls through `0x20FAC` (teardown `0x21F70`) into the **drain loop**
`0x20FC1..0x20FF1`: `0x111D0` enter CS → `0x11210(handle,&state)` →
`0x111F0` leave CS → `0x26AE0` pump → `0x20A70`; it exits only when
`state[0]==3` (stream ended) or `[0xAFE2C]!=0` (input/menu), and the pad
check is gated off by `[0xE1E48]==0`, so effectively it waits on `state==3`.

**What state 3 needs.** `0x11210` looks up stream object
`[0xF3D40+(arg&0xff)*4]` (= `0x80DA7CB0` for arg `0x100`), then channel
node via `0x138F0` (walks `[obj+0x11C]`, matches `[node+0xC]==arg`) and
returns 3 when `0x138F0`→0 (channel gone), 2 when the head matches, plus
positions `[node+0x14]*1000/48000` etc. (`ESI=0xBB80`=48000). The node
`0x80DA7DE8` (`[+0xC]=0x100`) is removed only by `0x11340`, called from the
advance callback `0x11450` when `[node+0x14] >= [node+0x18]`. `0x11450` is
registered at stream creation (`0x11A6F` → `0x13A10`, stored `[timer+0x38]`)
and dispatched **only** by `0x14170` from event queue `[0xF4140]` (word0==0
→ `[timer+0x38]` with the sample delta; else `[timer+0x34]` = release
callback `0x11420`). Events are queued by the XDK audio driver's DSP/DMA
processing (`0x182E1`, `0x1865D`), fed by the APU DSP (handler `0x18290`
registered at `0x18440`; DMA callback `0x18610`).

**Live evidence (forced runs; logs `Temp\opencode\007-splash\forced_23b.log`
/ `forced_26b.log`; ramdumps kept).** `--force-afe34` (temp, reverted)
reset `[AFE34]` to 2 every 50M steps so the game consumed the 1MB stream
buffer quickly; the EOS quirk then fired at ~1.82B. Post-fire stacks are
`0x20FD1` (drain loop, after `call 0x11210`) and `0x21128` (PlayMPC caller
for `EAG.MPC`); EBP = `0x11210`'s out pointer, out[0]=2, EIP inside
`0x138C0`. Sound-object watch (`0xDA7CB0..0xDA7E20`): node `+0x14`
(phys `0xDA7DFC`) written exactly **once** (allocation = 0) — the advance
callback never ran; `+0x1C` fed 30× by `0x11718`, `+0x24` 31× by `0x1172C`.
`[0xF4140]` (event count) = 0; `[0xF4098]` (sound-thread wake counter) =
`0xC30`/`0xFE6` (~312/407 10 ms iterations, `[0xF409D]=1`) — the thread
runs but has nothing to dispatch. APU write histogram (temp, reverted):
`0x202FC` VOICE_LOCK ×932, `0x202F8` SET_CURRENT_VOICE ×714, voice config
`0x20300-0x2037C` ×245-509 each, `0x20124` VOICE_ON ×11, GP
`0x20804/0x20808` ×17, `0x3FFFC` GPRST ×8 — yet the EventLog still shows
only the 27 early-boot entries and `voiceOff=1`. The DSOUND voice-status
page `[0x49424]=0x80568000` (phys `0x568000`), read by `0x445C2`/`0x45106`
at entry `+0x58`, was memset once (`0x4288E`) and got voice IDs once
(`0x45F8B`) but **positions were never updated**.

**Verdict: (a) emulator fidelity gap — no guest-side stall.** The guest
configured and enabled 11 VP voices and set up the status page, but
`McpxApuDevice` only stores the PIO writes (its own doc: “The DSP and voice
processor are not emulated yet”), so no positions/notifiers/DMA messages
reach the XDK audio driver, no advance events reach the sound thread, the
stream channel never completes, and the drain loop spins forever. The
sound thread is alive and dispatching — the producer side is simply never
fed. This also explains “no input polling” (drain loop pad check gated by
`[0xE1E48]=0`) and “DVD frozen” (data was buffered; the wait is audio-side).

**Correction to the premise.** The natural (unforced) run at 3B is *not*
post-fire: it is still in PlayMPC's streaming limiter with `[AFE34]=12`,
`remaining=0xB8980`, `state=2` (refill-pending), frames 53 — the read-ahead
filled the 1MB ring (256× READ(10), §86 cap) and the game consumes it at
the paced rate; the EOS quirk never fires there (`fires=0`). The forced
runs simulate the natural end state (buffer drained → EOS quirk → drain
loop).

**Fix path (next wave).** APU voice-processor + DSP streaming emulation
from `References2/xemu-0.8.136/hw/xbox/mcpx/apu/`: `vp/vp.c` (`fe_method`
PIO dispatch, voice lists, SSL segment processing `0x600+`, notifier
statuses, position advance), `dsp/dsp.c` + `dsp/dsp_dma.c` + `dsp/gp_ep.c`
(GP FIFOs, position DMA, FE interrupt), `apu.c` FEVINTSTS/FENINTSTS +
`FE_TRAPPED` delivery (XGrid already models the trap channel for
`SE2FE_IDLE_VOICE`). No compatibility quirk: the emulation layer is the
fault, and faking position/notifier state would violate the no-fake rule.

**Status:** docs-only change; no source edits. Suite 1034/1034, build +
`format --verify-no-changes` clean.

**Mapping fact verified:** 007 module code is `phys = VA + 0xAAF000`
(the code page holding `0x18440` was found at phys `0xC7440`; the XBE
`.text` file offset is `VA - 0x10000`). Data pointers still need live
page-table walks from CR3 (`0xAFE34`→`0x15EE34`, §92).

## 99. V1 snapshot core: framework + CPU + RAM + orchestration + CLI (2026-10-12)

Branch `wave/snapshot-core`. The devices branch implements `ISnapshotable`
on every device and appends registrations at the `// V1-DEVICES-ANCHOR` in
`XboxMachine`; the coordinator merges sequentially. This note covers the
core half.

**Contract.** `src/XGrid.Core/Snapshots/ISnapshotable.cs` (`SaveState` /
`LoadState` over `BinaryWriter`/`BinaryReader`) is byte-identical to the
wave spec. `XGrid.Core` is already referenced by every device assembly
(Audio, Bus, Cpu, Debug, Graphics, Host, Input, Mcpx, Memory, Nv2a,
Storage, Xbe, XboxFs — verified via all `XGrid.*.csproj`), so no
relocation was needed.

**File format** (written by `XboxMachine.SaveState`, read by `LoadState`):

```text
"XGS1"                       4 bytes ASCII magic
uint32 formatVersion         1
int32  sectionCount
per section:
  string tag                 BinaryWriter length-prefixed UTF-8
  uint64 payloadLength
  byte[payloadLength]        opaque section payload
```

Sections are written in registration order — `CPU`, `RAM`, then device
sections (devices branch), then `MACH` — but loading is order-independent:
the loader reads all sections into a tag→bytes map, then dispatches each
registered section by tag. Unknown tags are skipped (so files written with
more device sections still load on this branch); a missing registered tag,
duplicate tag, bad magic, version mismatch, oversized section (>512 MB), or
truncated payload throws `InvalidDataException`. Duplicate registration
tags throw `ArgumentException` at `RegisterSnapshotSection` time. Each
section payload starts with its own `uint32` section version (CPU=1,
RAM=1, MACH=1) for independent evolution.

**Complete captured-field list.**

- CPU (`XboxCpu`, `src/XGrid.Cpu/XboxCpuSnapshots.cs`): 8 GPRs (Gpr 0–7);
  EIP; EFLAGS (full 32 bits); 6 segments × (selector + base)
  (Es/Cs/Ss/Ds/Fs/Gs); CodeSegment32; TR; LDTR; CR0/CR2/CR3/CR4; DR0–DR7;
  GDTR base+limit; IDTR base+limit; Halted; TSC (u64); MXCSR; FPU control /
  status (Top rides in status bits 11–13, no separate field) / tag words +
  8 raw x87 registers in physical order (sign+exponent u16, significand
  u64 — NaN payloads, unnormals, and MMX aliases all round-trip bit-exact);
  8 XMM registers (32 u32, element order); SYSENTER_CS/ESP/EIP MSRs
  (0x174–0x176; MSR 0x2A returns a constant and holds no state; MSR 0x10
  is the TSC itself); InterruptCount; LastFaultVector/Eip/ErrorCode.
  The interpreter holds NO other mutable state: verified by enumerating
  all private fields of `XboxCpu` (`_sysenterCs/Esp/Eip` only) — there is
  no RNG and no pending-IRQ latch (the PIC is polled inline each `Step`;
  `InterruptCount` is the only counter). The software TLB is a derived
  cache over guest page tables: it is NOT stored, `LoadState` calls
  `Mmu.Flush()` instead (a stale entry would be a silent divergence).
- RAM (`PhysicalMemory`): section version + size + full byte image.
  Size mismatch on load throws (RAM size is construction-time; the CLI
  must pass the same `--ram`). PRAMIN is NOT duplicated here: it lives in
  `Nv2aMmio` and is snapshotted by the devices branch via
  `Nv2aMmio.SaveState` (documented split).
- MACH (`XboxMachine.MachineScalarsSnapshot`, tag `"MACH"`, core-owned
  trailing section): NV2A PTIMER fractional accumulator
  (`_nv2ARemainder` — lives on the machine, not the device, so it is core
  state); 007 EOS quirk detector (`SevenEosQuirkEnabled/Checks/Fires`,
  `_sevenEosQuirkParkActive/SinceTsc`). The saved `Enabled` value wins over
  the CLI `--no-007-eos-quirk` flag on load (exactness first).

**Deliberately excluded (caller responsibility / other branches).**
Flash/BIOS bytes and the MCPX overlay enable flag (rebuilt by
`LoadBios`+`Reset` — the restore recipe requires identical
`--bios/--mcpx/--eeprom` inputs); HDD overlay dirty sectors and DVD image
identity (require identical `--hdd/--dvd`; same-process save→restore keeps
the overlay, cross-process restore loses it — storage-branch gap);
diagnostic counters (unmapped-access buckets, method-stream logs,
watch hits) — guest-invisible, intentionally not captured. Save/load must
happen at an instruction boundary (between `Run` calls); mid-instruction
state is not observable and is never captured.

**CLI.** `boot --save-state <path> [--save-at N]` + 
`boot --load-state <path> [--steps N]` (usage line updated). Semantics:
`--save-at N` clamps the standard run loop so the save lands at exactly N
steps, then saves and exits(0) before the post-run outputs; `--save-state`
alone saves after the full `--steps` run and continues normally (composes
with `--ramdump/--frame/--pgraph`); `--save-at` without `--save-state`
is a usage error. `--load-state` restores after `Reset()` (same firmware/
disk inputs required) and `--steps` counts post-restore steps; composes
with `--pad-script/--dvd/etc.` (pad boundaries are evaluated against the
post-restore step counter). `--save-at` applies exactly to the standard
run loop; combined with `--trace/--break` debug phases the save still
captures exact state but may land past N (documented, not silently
rounded). Exit code 6 on snapshot I/O or format errors.

**Validation (this branch).** New tests: `CpuSnapshotTests` (4 facts:
full-field round-trip incl. FPU NaN/Top/condition bits, XMM, SYSENTER via
RDMSR execution, segments, DRs, TSC, MXCSR; Halted+InterruptCount via a
single-shot `IInterruptSource`; LastFault via UD with/without IDT; bad
version throws), `PhysicalMemorySnapshotTests` (3 facts: full-image
round-trip, size-mismatch and bad-version throws), `MachineSnapshotTests`
(5 facts: save@1→restore→+1 is byte-identical to uninterrupted +2 —
proves orchestration + container determinism; bad magic / missing section
throw; unknown sections ignored; duplicate tag rejected). Live CLI smoke
(real firmware+HDD, worktree build): save@2M (`EIP=0x0040349F`, 67 MB
file), `--load-state` resumes at exactly `0x0040349F` and runs +500K
fault-free; `--save-at 100000` stops at exactly step 100000 and exits.
Dashboard 1B save/restore exactness is NOT claimed on this branch: with
only CPU/RAM/MACH restored, device state (NV2A/PIC/PIT/IDE/USB/APU…)
diverges immediately — that is precisely what the devices branch closes.
Coordinator recipe for the merge gate:

```text
xgrid boot --bios … --mcpx … --eeprom … --hdd … --pad-script "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000" --steps 1000000000 --save-state sub1b.xgs --save-at 1000000000
xgrid boot --bios … --mcpx … --eeprom … --hdd … --load-state sub1b.xgs --steps <M> --frame resumed.bmp   (> resumed.log)
xgrid boot --bios … --mcpx … --eeprom … --hdd … --pad-script "…" --steps <1000000000+M> --frame direct.bmp (> direct.log)
```

Pass = EIP/all-regs/flip+NV2A counters identical in both logs and
`resumed.bmp` SHA-equal to `direct.bmp`. Pre-wave reference counters for
the save point: draws executed=33632 skipped=1402 pixels=285171381
textured=278577344 frames=stalls=686, frame 640×480 fully nonzero.

**Stretch (007 EOS-quirk snapshot) not attempted:** correctly scoped out —
the quirk fires at ~1.8B steps on forced runs and the natural 3B park is
pre-fire; meaningful snapshot validation there needs the APU VP/DSP
streaming wave first (§98), plus the devices branch for exactness.

**Status:** core builds, new tests green, `format --verify-no-changes`
clean. V1 acceptance is met for CPU+RAM+orchestration; full V1
(save/restore CPU+RAM+device at an arbitrary step with exact EIP/
registers/flip counters) completes at the devices-merge.

## 100. V1 device snapshots: ISnapshotable on every stateful device (2026-10-12)

Branch `wave/snapshot-devices`. Contract file
`src/XGrid.Core/Snapshots/ISnapshotable.cs` is byte-identical to the wave
contract (`SaveState(BinaryWriter)` / `LoadState(BinaryReader)` in namespace
`XGrid.Core`). Every versioned payload starts with an `int` snapshot version;
loads throw `InvalidDataException` on version mismatch, truncation, count
out-of-range, topology change, or media mismatch -- never silently.

Per-device field lists (save order = load order):

- `PciDevice` (base, virtual): 256 config bytes, `InterruptLine`, per-BAR
  present flag + `Base` + `Probing`. Load re-fires `OnBarWritten` so IO
  windows re-sync. BAR Size/IsIo/Prefetchable are construction constants.
- `PciBus`: `ConfigAddress`, config-access histogram, all 256 slots
  (present flag + device cascade), secondary-bus count + cascades. Load
  resets slots whose device vanished and fails when the secondary count
  differs (topology is caller-owned).
- `LpcController`: base + `RomOverlayEnabled`, `Pm1Status`, `Pm1Enable`,
  `PmTimerCounter`.
- `Amd756Smbus`: base + `Status`, `Control`, address/data/command/block
  regs, 32 block bytes, `TransactionCount`, last-transaction diag regs, and
  the 192-entry transaction history.
- `SmcDevice`: version cursor, command, scratch, error, IRQ status,
  `TrayState`, `AvPack`, read/write histograms.
- `Eeprom24C02`: full 256-byte image + address pointer (content IS state;
  no path needed).
- `ConexantCx25871`: 256 register bytes + pointer.
- `XboxBus`: `_pitRemainder/_pmRemainder/_rtcRemainder`, then `Pic`, `Pit`,
  `Rtc`, `Pci` (cascades to LPC/SMBus/stubs), `Smc`, `Eeprom`, `Encoder`.
- `Pit8254`: control byte + 3 channels (counter, access, read/write phases,
  write latch, running, latch flag/value, divisor, mode).
- `Pic8259`: per-PIC mask, vector base, request + in-service latches, init
  step, auto-EOI, read-select, poll-pending.
- `Rtc146818`: 64 CMOS bytes, index, status A/B/C, subtick/second/period
  accumulators, access count.
- `McpxAciDevice`: 128 mixer bytes, 8 bus-master blocks
  (BdbAr/Civ/Lvi/Sr/Picb/Piv/Cr), `GlobCnt/GlobSta`, CAS semaphore,
  NAM/NABM access counters.
- `McpxApuDevice`: full 512 KB register file, pending idle-voice queue,
  frame remainder, read/write/voice-off counters, IRQ latch, 128-entry event
  log. VP/DSP do not exist yet -- this snapshots what exists; the guest
  memory adapter is host wiring.
- `IdeController`: base + drive select, bus-master command/status/PRDT/
  start count, both `AtaDevice` cascades, 32-entry command log, 64-entry
  access log, optional `HddPath`/`DvdPath` re-attach params (adopted when
  unset, validated when both set).
- `AtaDevice`: ATAPI personality flag, IDENTIFY page, packet + DMA/staging
  buffers, data/transfer/packet indices, remaining/LBA, phase enum,
  packet-DMA + packet-error flags, all taskfile regs, status, access/last-
  command counters, security lock, packet log + total packet bytes, media
  presence + sector counts, HDD overlay cascade when snapshotable. Load fails
  when DMA is in flight (save only when idle) and when media presence or
  sector counts differ.
- `OverlayDiskImage`: backing sector count (validation only), dirty-sector
  map (LBA + 512 bytes each), full write log.
- `OhciUsbController`: base-address check, all Hc regs, 4 root-port regs,
  bus-running, frame remainder, done counter, access/transfer counters,
  control/bulk heads, HCCA, IRQ latch, pending control-stage state
  (setup flag, IN flag, function address, payload, offset, OUT buffer),
  transfer log, reg histograms, attached-function cascade. The pending
  function is re-resolved by address on load and fails when missing.
- `UsbHub`: address/config, remote-wakeup, pending address/config, per-port
  status/change, per-port device cascade, 128-entry request log. Port count
  and per-port presence must match (topology caller-owned).
- `XidGamepad`: address/config, held buttons/analog/sticks, input/output/
  capability reports, pending address/config, 128-entry request log.
- `McpxNvNetDevice`: 1 KB register file, 32 PHY regs, reg histograms, 1628 B
  TX coalescing buffer + offset, pending RX frame, autoneg state, all IRQ
  and packet/link counters, loopback-backend cascade.
- `LoopbackNetworkBackend`: queued frames, sent log (16 cap), sent/byte/
  drop counters.
- `PshCombiner`: 8x RGB/alpha in/out words, 8x factor0/1, 2x spec-fog
  factor, control, stage program, dot-RGB mapping, other-stage input, final
  words.
- `Nv2aMmio`: bound engines/instances, PFIFO/PVIDEO/PFB/PGRAPH arrays,
  1 MB PRAMIN bytes, PRMCIO, retrace latch, read/write/user histograms, PMC
  log + pending/enabled, PBUS command, PTIMER/PCRTC/PLL state, PFIFO
  pending/enabled, vsync remainder, pusher/error/context counters, surface/
  clear/semaphore/flip/presented descriptors, vertex formats/offsets,
  136x4 transform program, 192x4 constants, load pointers, begin mode,
  inline elements/words/vertices, draw-array ranges, 16x4 inline defaults,
  4 texture slots, shader-clip mode, bump matrices/scales/offsets, fog
  state, combiner cascade, alpha/blend/depth/stencil/zclip state, all draw/
  pixel/sample/fallback counters, texture-state tables, semaphore/debug/
  notification/PRAMIN/IRQ latches, method-stream cap + counts + params +
  full method stream. Presented-frame pixels live in guest RAM and are
  re-read; only at-flip descriptors are stored. `RamSize` validated on load.

Re-attach strategies (handles never serialized): disk/DVD file handles stay
with their image objects in-process; cross-process restore reopens the same
HDD/DVD paths before load (`Ide.HddPath/DvdPath` record them; sector-count
checks fail loudly). EEPROM/encoder/PRAMIN/overlay-dirty bytes are value
state and ARE serialized. Guest-memory adapters (`IGuestMemory`,
`IApuMemory`, `IUsbMemory`, `IGpuMemory`, `INvNetMemory`) and IO-window
registrations are construction wiring rebuilt via BAR restore, not stored.
`SocketNetworkBackend` (host sockets + UDP NAT mappings) is NOT restorable:
nvnet save AND load both throw `InvalidDataException` unless the live backend
is loopback -- gated, never silent.

Quiesce contracts (where the CLI user may save): NV2A -- FIFO drained at a
flip boundary (synchronous pusher idle); IDE/ATAPI -- no DMA in flight on
either drive (load enforces); USB -- 1 ms frame boundary with no control
transfer mid-status-stage; SMBus/EEPROM/encoder -- no transaction in flight;
PCI -- BARs stable (post-enumeration); nvnet -- loopback backend only.
PIT/PIC/RTC/LPC/ACI/APU taskfile state is valid at any point.

Unrestorable exclusions (with reasons): host event delegates
(`InterruptChanged`, PCI raise/lower, RTC `Interrupt`) -- wiring, not state;
PIC latches are restored separately and already match, so load never fires
events. `IoSpace`/`AddressSpace` routing tables -- rebuilt at construction.
`DisplayFrame` bitmaps -- derived from guest RAM on next flip. Socket/NAT
state -- OS handles, no stable serialization. `XboxMachine._nv2ARemainder`
plus the SevenEosQuirk park state are core-owned (parallel branch) and must
be snapshotted there for bit-exact continuation; all device remainders
(PIT/PM/RTC accumulators, APU frame remainder, OHCI frame remainder, nvnet
autoneg remainder, NV2A vsync remainder) are covered here.

Registration: `XboxMachine.SnapshotDevices` (8 entries: Bus, Ide, Nv2A, Aci,
Apu, Net, Usb0, Usb1) with cascading saves; `// V1-DEVICES-ANCHOR` marks the
insertion point for the core branch merge. Open merge items for the
coordinator: `AttachHdd`/`AttachDvd` should assign `Ide.HddPath`/`DvdPath`
(one line each); end-to-end restore validation happens at merge.

**Status:** 27 new round-trip tests (Bus 17 incl. socket-gate negatives,
Storage 5 incl. sector-mismatch negative, Audio 1, Nv2a 2, Integration 2
registration/contract tests); save-mutate-load asserts byte-identical
re-saves. Suite 1080/1080, build + `format --verify-no-changes` clean.

## 101. V1 merged + acceptance met: dashboard save/restore is bit-exact (2026-10-12)

Coordinator merge of `wave/snapshot-core` (`6b2b73d`) +
`wave/snapshot-devices` (`68a32fc`) into master. Both branches created
`src/XGrid.Core/Snapshots/ISnapshotable.cs` byte-identical (clean
auto-merge). Reconciliation by coordinator: kept the core tagged-section
scheme (`CPU`/`RAM`/`BUS`/`IDE`/`NV2A`/`ACI`/`APU`/`NET`/`USB0`/`USB1`/
`MACH`, order-independent load, skip-unknown, duplicate rejection) and
translated the devices branch's 8-entry ordered list into tagged
registrations at the anchor; deleted the redundant `SnapshotDevices`
property and rewrote its test to assert section tags from a saved file;
fixed two analyzer errors in the rewritten test (CA1861 static-readonly
expected-tags array, MA0002 ordinal comparer) and one stale core test
(`UnknownSection_IsIgnored` now round-trips all 11 sections + the
unknown tag). Coordinator fix at merge: `AttachHdd`/`AttachDvd` now
record `Ide.HddPath`/`DvdPath` (devices open issue #1, cross-process
reopen params). Full suite **1092/1092** (1053 + 12 core + 27 devices).

**Acceptance, live on merged master** (logs/framebuffer dumps in
`Temp\opencode\v1-accept\`): submenu boot `--save-state S.xgs --save-at
1000000000` (72,105,569 B at EIP `0x1E0F1`), then `--load-state S.xgs
--steps 50000000` vs uninterrupted `--steps 1050000000` with the same
pad-script — **EIP `0x3A004`, all GPRs, NV2A counters (`draws=42833`,
`pixels=353422182`, `frames=895`, `zclipCull=21526`) identical; presented-
frame SHA-256 match; full 64 MB RAM SHA-256 match.** V1 acceptance
(save/restore CPU+RAM+device with exact EIP/registers/flip counters) is
MET. Probe economics: the gate costs one 1B run (~6 min at current
menu-workload speed); every probe after it costs only post-restore steps
(50M ≈ 20 s). Carried exclusions: core-uncaptured diagnostic counters,
host handles by re-attach (DVD/HDD paths), socket-backend gate
(loopback-only snapshots), `--save-at` exactness only for the standard
run loop, saved `SevenEosQuirkEnabled` overrides the CLI flag on load.

## 102. V2 recon verdict: overlay-XBE replacement BLOCKED; build the flat-binary harness (2026-10-12)

Read-only recon (no repo writes). Question: how can a minimal test
program execute without 10-minute full-boot runs (plan V2)?

**Signature enforcement (retail kernel, live + documented):** the kernel
loads the HDD dashboard via 27 READ DMA, stages it at phys `0xBF000`,
decrypts entry in place (`xor [0x10128],0xA8FC57AB` at `0x8002EFE6`)
and calls it (`0x800158DA`) — Gates 1/34, §§31/34/86. XBE format per
Cxbx: 256-B RSA signature over the header + per-section SHA-1 digests +
cert `AllowedMedia`; the retail kernel executes SHA-1/HMAC workers
bit-exact in XGrid LLE (§22). XboxWiki: kernel runs signed XBEs only;
bad signature → Fatal Error + halt. `NONSECURE_*` cert bits are honored
by debug kernels, NOT a retail bypass; debug-signed XBEs need a debug
BIOS/kernel (unavailable); the XISOSharp media-enable patch fixes a
DVD check inside the XBE, not kernel verification. **Verdict: replacing
`xboxdash.xbe` on an overlay image with an unsigned homebrew XBE cannot
run on the retail path (no MS private key).** Ranked alternatives:
(1) flat-binary harness, no kernel/XBE — RECOMMENDED, days;
(2) unsigned XBE + in-RAM verify patch — fragile, 1–2 weeks;
(3) debug BIOS/kernel — blocked on firmware;
(4) retail-sign homebrew — impossible;
(5) full nxdk bring-up — weeks, and still needs (2)/(3) to launch.

**Recommended harness (spec):** new `xgrid testrom --bin <flatbin>`
subcommand (do not overload `boot`): `new XboxMachine` with NO BIOS
(proven pattern: `XboxMachineTests` boots a fake BIOS; `CpuHarness` sets
flat GDT/stack/entry), copy `--bin` to phys base, ~100-line
`TestRomRunner`, `OUT 0xE9` PUTC/PUTS console device (+ optional `0xEB`
IRQ raiser later), `EAX=0+HLT`=pass / `EAX=N`=fail with `TESTROM
PASS/FAIL` first log line + existing counters. Test programs do
bare-metal pokes (NV2A MMIO, IDE PIO/BM-DMA, OHCI HCCA/ED/TD + 1 ms
`Advance` frames, port I/O); no kernel services, no paging, IRQs later.
**Toolchain on this machine:** NO nxdk/OpenXDK/xcc/MSVC in PATH or
common locations; YES `C:\mingw64\bin\gcc` 16.1.0 (x86_64, `-m32` link
unproven) and **NASM 3.01** at `C:\msys64\mingw64\bin\nasm.exe` (not in
PATH — absolute path; Xermu precedent: ~50 NASM `.asm` → flat binaries
with ASSERT macros). Python-emitted `.bin` is the fallback.

**First three ROMs (each <100M steps, `mov eax,CODE; hlt` exit):**
ROM-A `nv2a-textured-quad` (<2M): PGRAPH MMIO direct — surface/texture/
flip regs, 64×64 checker, one triangle, `FLIP_INCREMENT_WRITE`; ASSERT
`draws>=1 textured>0 frames>=1 nonzero>0` (+ `--frame`/`--pgraph`
cross-check). ROM-B `ide-read-dma` (<5M): synthetic 8 MB HDD (Python-made,
known pattern at LBA `0x100`), BAR4+PRDT, `READ DMA 0xC8` ×8 sectors,
`memcmp` vs golden; ASSERT `mismatch=0`. ROM-C `pad-getstate-poll`
(<10M): OHCI HCCA/ED/interrupt-IN TD in RAM, poll `DoneHead` across
`Advance` frames with `--pad-buttons down`; ASSERT `idle=0x0
pressed=0x2`. (APU voice-on/off is the 4th ROM.) Exact CLI shapes and
the full evidence trail are in the recon report; key file refs:
`XboxMachine.cs:16-108,261-391`, `CpuHarness.cs:10-42`,
`CpuTestRunner.cs:18-49`, `XboxMachineTests.cs:58-70`,
`Program.cs:911-1155` (counters), `XbeImage.cs:218-246` (XOR/type).

## 103. V2 flat-binary test harness + ROM-A nv2a-textured-quad DONE (2026-10-12)

Built the §102 spec as specified (wave/testrom-harness): `xgrid testrom
--bin <flatbin>` subcommand (separate from `boot`, no BIOS/HDD required),
~100-line `TestRomRunner` reusing `XboxMachine` as-is, 1-byte `0xE9`
console device (`0x0A` flushes `TESTROM LOG:` lines), `EAX=0+HLT`=pass.

**Files:** `src/XGrid.Bus/Io/TestRomConsole.cs` (new, `IIoPortDevice`,
line-buffered, `Flush()` for partial lines at HLT);
`src/XGrid.Host/TestRomRunner.cs` (new, `LoadImage` copies `--bin` to
phys base + flat GDT at 0x7000/selectors 0x08/0x10/`Esp`/`Eip` per
`CpuHarness`, `Run(ulong)` chunks through `machine.Run`,
`Passed` = halted && EAX==0, `IDisposable` for the port registration);
`src/XGrid.Cli/Program.cs` (`testrom` + `--base/--entry/--stack`
default 0x1000/0x1000/0x80000, `--steps` default 10M, `--ram`,
`--hdd/--dvd/--pad-buttons/--pad-script/--frame/--pgraph/--pgraph-max/
--dump/--break`; ASSERT verdict is the first stdout line, then deferred
`Break:`, `TESTROM LOG:` lines, existing `PrintState` counters;
exit 0/1 = pass/fail; crash paths also print the verdict before
`PrintState`); `tools/testroms/nv2a_quad.asm` (human-readable source) +
`nv2a_quad.bin` (1188 B, assembled once with
`C:\msys64\mingw64\bin\nasm.exe -f bin`, absolute path, not in PATH);
`tests/XGrid.Integration.Tests/TestRomTests.cs` (bytes embedded, no NASM
shell-out: ROM-A live test + pass/fail-convention + E9-console tests).

**ROM-A design (direct PGRAPH MMIO via PFIFO pusher, mirrors
`PgraphTransformTests.PushTexturedLegacyDraw` linear path + the
`CreateChannel` setup):** guest fills a 64x64 8x8-cell checker
(transparent-black 0x00000000 / white 0xFFFFFFFF) at 0x10000, screen-space
verts (0,0)/(64,0)/(0,64) at 0x8000, texel-unit texcoords at 0x8100, a
39-method pushbuffer at 0x9000 (surface 64x64 A8R8G8B8 at 0x20000/pitch
256, arrays 0x1720/0x1760 + 0x1724/0x1764, 3-insn program with
c0=(1,1,1,1), linear-texture state 0x1B00-0x1B1C with image rect
0x00400040, begin/element/end, `FLIP_INCREMENT_WRITE`), the 10-word
channel setup + explicit GET (`0x800044`, so the buffer need not sit at
physical 0), then PUT. Guest ASSERT reads two interior surface pixels
((16,8)==white cell (2,1), (16,16)==exact-zero cell (2,2)) — proving
draw + texture sample, not just "something rendered".

**Live evidence (first run, no iteration needed):**
`TESTROM PASS nv2a_quad draws=1 textured=2080 frames=1 nonzero=1056`
in 68,491 steps (<2M budget with 29x margin); `textured=2080` is exactly
the triangle pixel count (64*65/2); frame BMP is 64x64 with 1056 white +
3040 zero pixels (byte histogram); `--pgraph` holds all 39 methods.
Guest log lines (`fill+push`, `pushed, checking`, `surface matches`) and
`Port 00E9=76` accesses confirm the console path.

**Follow-ups left as specified (not half-built):** ROM-B `ide-read-dma`
(synthetic 8 MB HDD, BAR4+PRDT, `READ DMA 0xC8` x8, `mismatch=0`) and
ROM-C `pad-getstate-poll` (OHCI HCCA/ED/interrupt-IN TD, `DoneHead`
across `Advance` frames, `idle=0x0 pressed=0x2`) per §102. Both ride the
same harness/CLI with no further infra work. The checked-in `.bin` lets
other machines run the CLI evidence without NASM; if the `.asm` changes,
re-assemble with the absolute-path NASM and re-embed the bytes.

## 104. Test samples (data/test-samples) + first unsigned-XBE launch attempt (2026-10-12)

**Samples added (local only, gitignored under `data/`; provenance in the
folder README):** `triangle.xbe` (160 KB, nxdk `samples/triangle` build
from ghidra-xbe test files; `Type=Retail`, base `0x10000`, entry VA
`0x36720` stored `0xA8FF308B`, 7 sections incl. `.tls`, lib `CXBE0`,
signature field all-zero = unsigned). `kernel-test-suite-default.xbe`
(312 KB, Cxbx-Reloaded `xbox_kernel_test_suite` release; `Type=Retail`,
entry VA `0x4DF80` stored `0xA8F8882B`, 6 sections incl. TLS +
`sectest_`). `nxdk-{hello,led,mesh,triangle}/` sources (mesh needs
upstream `texture.h`/`verts.h`). Per the operator the boot BIOS
(`Complex_4627.bin`) is a **custom build that permits unsigned code** —
consistent with the unsigned EvoX dashboard running from the HDD; this
supersedes §102's stock-kernel "blocked" verdict for unsigned XBEs.

**Minimal XISO recipe (works):** sibling repo `CSharp_XISOSharp` CLI
(MIT): `XISOSharp.exe -y -m -c <dir> <out.iso>` where `<dir>` holds
`default.xbe` = triangle.xbe (`-m` disables the automatic XBE
media-enable patch; the kernel's DVD probe accepts the image normally).

**First boot (`--dvd triangle.iso --steps 700M`):** the kernel runs its
DVD path and the XBE launch call site `0x800158DA` HITs at step
**525,703,663** (`EAX=0 ESI=0x80000000 EDI=0`; 007's handoff was
537,926,130). Immediately after, the run dies with an **unhandled
`XboxCpu.PageFaultSignal` thrown from `TryTransferToInterrupt` →
`TranslateLinear`** — an interrupt/exception delivery whose IDT
translation faults (double-fault-class) is not handled by the CPU core.
Open: whether `[0x10128]` was decrypted (`xor 0xA8FC57AB` at
`0x8002EFE6`) and executed, and whether the first fault is an emulator
fault-delivery gap or a guest error path (e.g. a verification-failure
branch). Diagnosis continues in `wave/xbe-bringup` (snapshot at 525.6M
so probes run in seconds).

## 105. DXT recon: unused by the dashboard; the one real texture miss is 0x30 (2026-10-12)

Read-only probe on the prebuilt CLI (1B-step submenu boot; logs in
`Temp\opencode\dxt-recon\`). **DXT (0x0C/0x0E/0x0F) has ZERO usage in
the dashboard workload** — implementing DXT decode would change 0
pixels there; do not land it expecting a dashboard diff. The only
texture-format miss is **`0x30` LU_IMAGE_DEPTH_Y16_FIXED
(62,444,852 attempts / 62,444,852 misses, ~18%)**, all in decorative
multi-stage states (stage-3 PROJECT3D, 512x512 depth textures) that
currently degrade to `SetTexelNone`. Used formats: `12`=210.7M/0,
`04`=45.2M/0, `06`=22.6M/0; fallbacks 0. The probe also spec'd the
full DXT decode (s3tc bit math with integer truncation, block-linear
addressing without Morton, hand-computed test vectors) and the mipmap
LOD rule (TEXCTL0 min/max clamp, TEXFILTER 13-bit signed LOD bias,
MIN/MAG filter modes) for whenever a DXT/mipmap workload appears (e.g.
nxdk-mesh once built). Priority: **DXT decode deprioritized; `0x30`
depth-format semantics open** (needs per-draw combiner tracing to
decide shadow-map vs dead stage).

## 106. Unsigned-XBE bring-up: triangle.xbe renders — NO_OPERATION stall + surface DMA-context bases (2026-10-13)

Worktree `wave/xbe-bringup` (main checkout untouched; logs/frames in
`Temp\opencode\xbe-bringup\`). Acceptance MET: `triangle.xbe` executes
through the real kernel and renders; `--frame` BMP shows the pbkit
triangle (red/green/blue Gouraud foreground, dark-blue background pair,
black clear), 35,519 unique colors, no fault.

**§104 crash root cause (three coupled emulator gaps, not a guest
error and not a signature check).** The fault was NOT fault-delivery:
a pbkit `pb_erase_depth_stencil_buffer` clear (pushbuffer at phys
`0x3F5B0xx`) wrote `0xFFFFFF00` over physical `0..0x12C000` — wiping
the IDT, page directory (PDEs all `0xFFFFFF00`), kernel and XBE — then
the next fetch hit a `FF FF` fill byte (#UD at VA `0x1F16A`), the #UD
gate was already trashed (offset `0xFFFFFF00`, selector `0xFFFF`), the
handler fetch faulted, and the #PF frame push walked off the stack into
the unmapped page below `0xD003F000` → the unhandled double-fault-class
`PageFaultSignal`. RAM dump at the storm: fill runs `0x0..0x121A10` +
`0x121A1C..0x12C000` (+ two 2.4–4 MB pbkit buffer fills), `XBEH` gone.
The clear was legitimate: pbkit sets `SET_SURFACE_ZETA_OFFSET=0` and
`SET_SURFACE_COLOR_OFFSET=0` because its color/zeta surfaces are the
DMA-context bases, and it updates the RAMIN DMA objects (frame =
back-buffer/depth-buffer physical address) through its `PB_SETOUTER`
subprogram (`0x0100` fire-interrupt + `PARAMETER_A/B`). XGrid executed
all of that wrong:

1. **`NV097_NO_OPERATION` (0x100) must stall the pusher.** xemu sets
   `waiting_for_nop` and `pfifo_pusher_should_stall` returns until the
   guest writes `NV_PGRAPH_INTR` (which clears it and `pfifo_kick`s).
   XGrid raised the notification but processed the rest of the
   pushbuffer synchronously, so the clear ran before the guest DPC
   updated the DMA objects. Implemented `_waitingForNop` in
   `RunPusher` (early return + loop break after the data word) and the
   ack in `WritePgraph(0x100)`.
2. **Resume must be deferred one tick.** pbkit acks the notification
   *before* reading `PARAMETER_A/B` (`pb_gr_handler`:
   `VIDEOREG(NV_PGRAPH_INTR)=ERROR_RESET;` then `pb_subprog(...)`).
   Resuming the pusher synchronously inside the ack write would clobber
   the parameters with the *next* pair and drop the frame/base write.
   Implemented `_pusherKickPending`, consumed at the top of
   `Nv2aMmio.Advance` — the guest's handler always completes first
   (model of GPU-engine latency).
3. **`0x1D8C`/`0x1D90` are PARAMETER_A/B aliases.** pbkit reads the
   outer registers `NV_PGRAPH_ZSTENCILCLEARVALUE` (`0x1A88`) and
   `NV_PGRAPH_COLORCLEARVALUE` (`0x186C`) in `pb_subprog`; xemu mirrors
   `SET_ZSTENCIL_CLEAR_VALUE`/`SET_COLOR_CLEAR_VALUE` into them. XGrid
   only kept private fields, so the subprogram read zeros and wrote
   `0` to PMC. Both methods now also write `_pgraph[0x1A88/4]` and
   `_pgraph[0x186C/4]`.
4. **Surface address = DMA-context base + surface offset.** xemu
   `populate_surface_binding_entry_sized`: `vram_addr = dma.address +
   surface->offset` where the DMA object is loaded from the context
   handle. Added `MethodContextDmaColor` (`0x194`) + `_dmaColor`,
   `TryResolveDmaContext` (RAMHT handle → instance → flags/limit/frame,
   adjust shifted like xemu `GET_MASK`), `SurfaceBase` and
   `SurfaceFitsContext` (xemu's limit assert; unresolved handles fall
   back to base 0 so raw-offset titles like the dashboard are
   unchanged). Applied to `ExecuteClear` (color+zeta, with limit
   guard), the fragment color/zeta writes (bases resolved once per
   triangle in `FillTriangle`/`FillTrianglePsh`), and
   `CapturePresentedFrame` (`_presentedOffset = base + offset`).
   Evidence at 1.5256B: RAMIN handle 9 frame `0x3BD4003` limit
   `0x12BFFF` (back buffer), handle 0xA frame `0x3AA8003` (depth
   buffer) — the guest subprograms now land. Reference:
   `xemu hw/xbox/nv2a/pgraph/gl/surface.c:1042-1060`,
   `pgraph.c:823-857` (NO_OPERATION), `pfifo.c:490-509` (RAMHT hash),
   `nv2a.c:56-71` (`nv_dma_load`).

**Live evidence.** Snapshot `prelaunch.xgs` at 525.6M (plain-run world;
EIP `0x8001E361`; the old §104 525,703,663 launch figure was
break-log-world pacing). Probes:
`--load-state prelaunch.xgs --steps 1000000000 --frame f.bmp` completes
**1B steps, `LastFault vec=0`**, `draws=2749 pixels=108.5M clears=15477
frames=111`, presented surface `0x3E2C000` 640x480 pitch 2560; BMP
bottom row `(255,0,0)→(0,0,255)` red-to-blue Gouraud ramp, green vertex
at (320,100), dark-blue background pair. Fresh `boot --dvd triangle.iso
--steps 1000000000` (no snapshot): **no fault**, `draws=2706
pixels=88.9M frames=68`, same triangle frame. **Dashboard regression
check** (HDD boot, `--steps 1700000000`): **no fault**,
`zclipCull=21526` unchanged, frame is the blue EvoX menu (8,390 sampled
colors, ASCII downsample shows text rows). Note: the stall now paces
the pusher faithfully, so dashboard counters move (3480 flips vs the
§43 33-flip baseline) — the menu runs continuously instead of the
synchronous-pusher park; §43/§101 counter baselines need re-recording.

**Files:** `src/XGrid.Nv2a/Nv2aMmio.cs` (stall/kick/pending, parameter
mirrors, DMA context fields + helpers, clear/draw/present addressing,
`Nv2aSnapshotVersion 1→2`); `tests/XGrid.Nv2a.Tests/PfifoPusherTests.cs`
(`NoOperationWithParameter_StallsPusherUntilPgraphIntrAck`,
`ClearSurface_AppliesColorDmaContextBase`; the old
`IncreasingMethod_DispatchesToSink` now uses a zero parameter since
non-zero is a stall). Suite 1098/1098 (was 1096); build + format clean.

**Open:** the snapshot version bump invalidates pre-fix `.xgs` files
(regenerate); `pbkit` presents via `NV_PGRAPH_INCREMENT` (`0x71C`)
+ `PCRTC_START` rather than method `0x12C`, so the captured frame comes
from the dashboard-style flip path — a dedicated pbkit present hook is
optional follow-up; kernel-test-suite sample not yet tried.
## 107. APU voice processor: real position advance unblocks 007's PlayMPC streams (2026-10-13)

Branch `wave/apu-vp-dsp` (worktree `Temp\opencode\worktrees\apu-vp-dsp`). Fixes the
§98 verdict: the `0x138xx`/`0x20FC1` park was the PlayMPC audio-drain loop waiting
for stream channel `0x100` status 3, and status 3 needed the APU voice processor's
position/notifier feedback, which XGrid stored but never advanced.

**xemu facts extracted** (`References2/xemu-0.8.136/hw/xbox/mcpx/apu/`):
- Voice state lives in *guest RAM* at `VPVADDR + voice*0x80`; every VP PIO method
  reads/writes it via `voice_get_mask`/`voice_set_mask` (`vp/vp.c:100-116`).
  Registers: `VPVADDR 0x202C`, `VPSGEADDR 0x2030`, `VPSSLADDR 0x2034`,
  `FENADDR 0x115C` (`apu_regs.h:58-60,45`); voice offsets CFG_FMT `0x04`,
  CUR_PSL_START `0x20`, CUR_PSH_SAMPLE `0x24`, PAR_STATE `0x54`, PAR_OFFSET
  (CBO) `0x58`, PAR_NEXT (EBO) `0x5C`, TAR_PITCH_LINK `0x7C`.
- `fe_method` (`vp.c:164-576`): VOICE_ON links the voice to the top of the
  selected 2D/3D/MP list (`TVL2D 0x2054`/`TVL3D 0x2060`/`TVLMP 0x206C`) or after
  an antecedent, resets CBO/SSL index/segment and envelope counts, sets
  ACTIVE_VOICE (bit 21); CFG/TAR/SSL/SGE/segment methods store into the voice
  page; `SET_CURRENT_SSL` selects the SSL table page and methods `0x600..0x7FF`
  write 8-byte `{offset,length}` entries into `VPSSLADDR + page*8`.
- Frame loop `mcpx_apu_vp_frame` (`vp.c:1809-1858`): walks the 3 lists via
  `TAR_PITCH_LINK.NEXT_VOICE_HANDLE`; voices that are not active get
  `fe_method(SE2FE_IDLE_VOICE, v)` (gated by `FETFORCE1` bit 15, `vp.c:559-569`),
  active voices are processed.
- `voice_get_samples` (`vp.c:837-1121`): a streaming voice (`CFG_FMT` bit 24)
  takes its segment length from `VPSSLADDR` (low 16 bits = samples), advances
  CBO one sample per played sample, and on segment end advances `ssl_seg`; when
  the SSL's segment count is exhausted it flips `ssl_index` and writes notifier
  `SSLA_DONE + old_index` (`vp.c:1093-1106`). `count == 0` means stream end:
  CBO=0, `ssl_seg=0`; without PERSIST the SSL index resets and `voice_off` runs
  (`vp.c:941-955`). Buffer voices wrap to LBO when LOOP is set, else
  `voice_off` (`vp.c:1107-1115`).
- `set_notify_status` (`vp.c:33-52`): status byte `DONE_SUCCESS=1` at
  `FENADDR + 16*(2 + v*4 + notifier) + 15`, a `1` at `-1`, and
  `ISTS |= FEVINTSTS|FENINTSTS`. `voice_off` uses `SSLA_DONE + ssl_index` for
  stream voices (`vp.c:118-132`). FE trap delivery is `FEDECMETH/FEDECPARAM` +
  `FECTL` trapped/requested, as XGrid already modeled for idle voices.
- xemu runs 32 samples/frame (8 VP frames per 256-sample EP frame,
  `apu_regs.h:333-334,363`); the DSP (M56001) cores are *not* needed for this
  gate: the XDK driver's own DMA callback processes audio buffers and enqueues
  advance events (below), the VP notifiers are the producer.

**Design** (`src/XGrid.Audio/McpxApuDevice.cs`): the VP front-end now dispatches
every PIO method xemu implements (voice lock/current/on/off/release/pause, all
CFG/TAR/SSL/SGE/segment writers) against the guest-RAM voice page; `Advance`
runs one VP frame per existing 256-sample/48 kHz XGrid frame quantum, walking the
2D/3D/MP lists (cycle-guarded), advancing CBO through buffer/SSL data, writing
notifier statuses and raising `FEVINTSTS|FENINTSTS` on segment completion, and
sending `SE2FE_IDLE_VOICE` for inactive linked voices and on natural stream end.
Snapshot version bumped to 2 (SSL bases/counts/index/segment, voice locks, SGE
handles); v1 snapshots are rejected.

**Live evidence (007 USA CHD, firmware read-only, quirk default-on, no pad).**
Snapshot-driven: `snap007b.xgs` at 2.4B (EIP `0x21308`); every probe below is a
restore + continue (seconds), never a 2.4B replay.
- Pre-fix state at 2.4B: node `0x80DA7DE8` (`[+0xC]=0x100`) position `[+0x14]`=0,
  EOS quirk `fires=0`, game parked in the streaming limiter.
- Post-fix restore +1.05M: driver DMA callback `0x18610` (caller `0x1788B`),
  event dispatcher `0x14170` +1.064M, then **advance callback `0x11450`**
  (caller `0x141AD`) +1.064M — the real path is alive.
- Node `[+0x14]` advances at ~48 kHz real time: 109,536 (save) → 140,480
  (+400M) → 144,145 = `[+0x18]` (+~530M).
- Single-step break-log window (snapF, 450M after snap007b): **`0x11340` node
  removal hit once at +115,757,484, 10 steps after an `0x11450` event**, and the
  post-window RAM shows the acceptance state: stream object `0x80DA7CB0`
  `[+0x11C]` channel list empty, `[+0x124]` active count 0, node on the free
  list (`[+0x128]`), node `[+0x14]==[+0x18]==0x23311`. `0x138F0` walking that
  list now finds no `0x100` node, i.e. `0x11210` returns **status 3** — the exact
  condition `0x20FC1` waits for, reached with no forced write.
- The game then plays through the splash MPC sequence instead of parking:
  channel `0x100` (3 s) removed, `0x200` (8 s, position 17,952→384,384, removed),
  `0x300` (54 s) at 189,344 by 13.1B total; EIPs stay in PlayMPC
  (`0x1Exxx`/`0x2xxxx`, limiter helper `0x138xx`), DVD reads continue, **no fault
  through 13.1B steps** (`LastFault vec=0`).
- The `0x20FC1` drain loop itself was **never entered** in the natural run (two
  break-log windows, 0-800M after snap007b): with the channel now completing
  through the real path, the EOS path no longer leaves the drain with a pending
  channel, so PlayMPC advances straight to the next stream. The EOS quirk also
  stays `fires=0` (the read-ahead no longer starves); no quirk or forced write is
  involved.
- Dashboard regression: 1B boot with HDD only — all steps executed, no fault,
  NV2A draws=33811 pixels=284,274,887 frames=682, IDE/NV2A/APU counters sane
  (APU `ist=0x10 ien=0xAFA`).

**Next gate observed:** the game is inside the 007 splash MPC playback loop
(PlayMPC at EIP `0x1E32D`/`0x1FC92` at 13.1B), cycling streams; the next visible
gate is the end of the `0x300` (54 s) stream / subsequent MPCs, which at the
real-time pace needs ~40B more steps. Open: DSP cores remain register-only
(no GP/EP execution); voice pitch/resampling is not modeled (CBO advances by the
frame quantum; 007's stream voices have pitch 0 = 48 kHz, so 1:1); xemu's
32-sample VP frame vs XGrid's 256-sample frame only changes notifier granularity.

**Status:** suite **1107/1107** (1096 + 11 new `ApuVoiceProcessorTests`); build +
`dotnet format --verify-no-changes` clean.
## 108. V2 test-ROM suite complete: ROM-B ide-read-dma + ROM-C pad-getstate-poll (2026-10-12)

Completed the §102/§103 flat-binary suite on the merged harness
(`xgrid testrom --bin`): ROM-B `ide_dma` and ROM-C `pad_poll` both PASS
live with the guest as the pass/fail authority. **No emulator (`src/`)
changes were needed** — the IDE bus-master engine and the OHCI schedule
engine already satisfy their ROMs; this wave only adds ROMs, tests, and
docs.

**Files:** `tools/testroms/ide_dma.asm` + `.bin` (480 B),
`tools/testroms/pad_poll.asm` + `.bin` (1013 B) — both assembled once with
`C:\msys64\mingw64\bin\nasm.exe -f bin` (absolute path, not in PATH);
`tests/XGrid.Integration.Tests/TestRomTests.cs` (+4 tests: pass + fail for
each ROM; bytes embedded, tests never shell out to NASM; temp HDD
generated/disposed by the test).

**ROM-B `ide-read-dma` (<5M budget).** Host test generates a deterministic
8 MB raw HDD into a temp file (NOT the dashboard qcow2): the byte at linear
offset n = LBA*512 + i is `((n * 0x21) ^ 0x5A) & 0xFF`. Guest: PCI config
dev 9 fn 0 (IDE 10DE:01BC) — **BAR4 lives at config register 0x20, not
0x10** (BAR0 is 0x10; the first live run failed `bm-error-bit` with
`starts=0` because the BAR write landed on BAR0 — documented so later ROMs
do not repeat it), BAR4=0xC000, command 0x07 (IO+bus master); one-entry
PRDT at 0x30000 -> 4096 B at 0x40000 (end bit); taskfile 0x1F6=0xE0,
0x1F2=8, LBA 0x100, command 0xC8; BM command start; poll BM status bit 2
(INTERRUPT) with ERROR clear; guest recomputes the pattern and memcmps the
4096-byte buffer (mismatch counter), printing the exact assert line.
Evidence: `TESTROM PASS ide_dma` + `TESTROM LOG: TESTROM PASS ide-dma
lba=0x100 sectors=8 mismatch=0` in **53,836 steps**; `IDE BMIBA=0xC000
status=0x04 prdt=0x00030000 starts=1`; IDE trace
`BW0:0:8 BW2:6:8 BW4:30000:32 TMW7:C8:8 BW0:1:8 BR2:4`. Negative control
(all-zero 8 MB image) exits EAX=2 with `data-mismatch`, proving the guest
memcmp is the authority.

**ROM-C `pad-getstate-poll` (<10M budget).** Guest: USB0 OHCI at
0xFED00000 soft reset -> operational+CLE, HCCA 0x30000; enumerates the
daughterboard topology (root port 1 -> 4-port hub port 3 -> 3-port hub
port 1 -> XID pad, §69) with **one chained control ED list of five EDs**
(0x31000, each a setup TD + zero-length IN status TD) and a single CLF
doorbell: SET_ADDRESS(1), SET_FEATURE(PORT_RESET) port 3, SET_ADDRESS(2),
SET_FEATURE(PORT_RESET) port 1, SET_ADDRESS(3). OHCI clears CLF only one
frame *after* the list drains (`ServiceEdList` returns active while TDs
retire), so ringing per transfer costs 2 frames each; the chained list is
serviced wholly in frame 0 and clears CLF at frame 1 — this cut the ROM
from 9.57M steps (95% of budget, first working version) to **2,949,419
steps**. Then: clear WDH, cross a frame (done-queue writeback), assert
`HcDoneHead == 0` (the `idle=0x0` observation — with `--pad-buttons down`
the report itself is always 0x2); build the periodic ED (address 3,
endpoint 2 IN, 32-byte packets) + 20-byte TD at 0x32010 -> report 0x33000;
fill all 32 HCCA interrupt-table entries; clear WDH; enable PLE; poll
`HcDoneHead`/`HCCA+0x84` across 1 ms `Advance` frames until the TD
address appears; check TD condition == NoError and the data toggle
advanced (flags 0x03000000). Report bytes [0]=0, [1]=20, [2..3]=0x0002
(DOWN) -> `TESTROM PASS pad-poll idle=0x0 pressed=0x2`, EAX=0. Evidence:
CLI verdict `TESTROM PASS pad_poll`, USB0 log `setup 00/05 addr=1`,
`setup 23/03 idx=3`, xfer log shows all five control setup/status pairs at
`f=0`, `Gamepad.Address=3`, `InterruptTransfers>=1`. Negative control
(neutral pad) exits EAX=3 with `bad-report`, proving the guest check.

**Test accounting:** 4 new tests (IdeDma pass + zero-image fail, PadPoll
down pass + neutral fail); suite 1096 -> 1100 green. Both ROMs stay far
inside their step budgets with deterministic, fixed scripts (no
wall-clock dependence).

**Follow-ups unchanged:** triangle XBE bring-up (`wave/xbe-bringup`,
§104) and APU voice-on/off as the 4th ROM (§98) are separate waves.

## §109 — Triangle diagonal hatch fixed: depth compare quantized to zeta precision (2026-10-13)

Work in `Temp\opencode\worktrees\triangle-hatch` (`wave/triangle-hatch`).
Closes the §106 follow-up "diagonal hatch artifact inside the triangle".
Acceptance met on all four points (evidence below).

**What the hatch was (evidence chain, differential-first).** Baseline
unmodified `triangle.xbe` boot (1B steps, private media copies, `--frame`
SHA256 `72F880C75F18D8E8EABA867C47DA415E27DD654A2344DED8DFADB3F6A73248DF`)
shows diagonal stripes inside the rendered triangle. The stripes are **RGB,
not alpha** (frame pixels A=`FF` on both sides of every stripe): scanline
y=200 alternates between the foreground Gouraud ramp (`R=6A G=94 B=00`
progressing) and the background triangle's constant gradient color
(`0B0B40`) — i.e. foreground fragments randomly failing the depth test and
letting the already-drawn background show through. The demo
(`data/test-samples/nxdk-triangle/main.c`) draws **three triangles at the
same Z** in one `TRIANGLES` draw; `vs.vs.cg` computes
`position.xyz /= position.w` in-shader with w=1, so every vertex leaves the
program at device z = 65536 (viewport bake `1.0 * (zmax - zmin)`). The
pusher stream shows depth enable `030C=1`, depth func `0354=00000203`
(LEQUAL), depth mask on, stencil test off, `Z_FORMAT` **clear** (`0290`
values are `0x00100001`/`0x00110001` — bits 16/20, never bit 12), ZCLIP
`[0, 16777215]`, `1D78=CULL`, and the pbkit clear `1D8C=FFFFFF00`
(depth field 1.0).

**Root cause (XGrid diverged from hardware).** Reference fact, xemu
`pgraph/glsl/psh.c:1491-1516`: *"With integer depth buffers Xbox hardware
floors values"* — the reference fragment shader writes an already-quantized
`gl_FragDepth` (`floor(zvalue)` in device units for D16/D24), and the GL /
Vulkan / D3D pipelines all convert FragDepth to the attachment's precision
**before** the fixed-function depth test. XGrid instead compared the raw
float interpolated depth (`w1*az + w2*bz + w0*cz`, ULP noise because the
barycentric weights only sum to 1 approximately) against the stored depth
decoded from the quantized zeta field. All three coplanar triangles encode
to the same field (device 65536 -> field `0x10000`), but the unquantized
fragment floats straddle the stored decode by ~1 ULP: LEQUAL passes where
the interpolation rounds down and fails where it rounds up -> per-pixel
interleaving of pass/fail = the diagonal hatch (weights vary smoothly along
diagonals, so the rounding pattern does too). Exact single-precision
replication of `FillTriangle`'s formulas confirmed the mechanism: geometry
A = 12/64, geometry B = 7/64 sampled pixels with `interp > stored` (the
isolated unit test failed with exactly 12/64 undrawn pre-fix).

**Fix (`src/XGrid.Nv2a/Nv2aMmio.cs`, `ProcessFragment`).** When the depth
test runs, the fragment depth is encoded through the same conversion the
store uses (`EncodeDepth24`/`EncodeDepth16` -> fixed D24/D16 integer or
F24/F16 custom-float field) and the compare happens in **zeta field space**
against the raw stored field (monotonic with the stored value). NaN keeps
the old IEEE float-compare path (ordered relations false, as before). The
store path, ZCLIP order (cull/clamp first), stencil ops, and all counters
are unchanged.

**Test-harness bug found while building the repro (also fixed).**
`PgraphTransformTests.PushMethods` kicked with `PUT = address + 4`,
*including* the trailing `0x00001001` word. That word is an NV **jump to
0x1000** control word (`(word & 3) == 1`); PUT (~0xE4) != 0x1000, so the
pusher ran past the stream into scratch memory, misparsed vertex floats
(e.g. `0x3F800000` parses as jump-to-`0x1F800000`), and suspended with
`DmaErrorProtection` (err=6) after **every** single-draw test. A second
`PushMethods` kick in the same test was therefore always dead — notably
`FloatDepth_OrderingMatchesFixed`'s second draw never actually ran (its
assertions hold vacuously; unchanged expectations, now non-vacuous if
relied upon). `PushMethods` now sets `PUT = data-end` (the pusher stops
cleanly at `get == put` before the barrier) and a new
`PushMethodsContinue` appends a second stream at the current GET, the way
guests push contiguously. All 203 pre-existing Nv2a tests pass unchanged
with the new kick semantics.

**New tests (2, `tests/XGrid.Nv2a.Tests/PgraphTransformTests.cs`):**
- `DepthTest_CoplanarLEQUALOverlapDrawsSolidTopTriangle` — two overlapping
  coplanar triangles at device z 65536 (the demo's exact Z), pbkit clear
  `0xFFFFFF00`, depth LEQUAL: every interior pixel of the top triangle must
  be the top color. **Fails pre-fix** (bottom red `FFFF0000` bleeding
  through where blue `FF0000FF` must be = the hatch in miniature), passes
  post-fix.
- `DepthTest_QuantizedComparePassesSubQuantumLequal` — stored field
  `0x10000`, fragment device z 65536: LEQUAL must pass for all 64 sampled
  interior pixels. **Fails pre-fix with 12/64 undrawn** (matches the exact
  float simulation), passes post-fix.

**Acceptance evidence.**
- (i) Fresh `triangle.xbe` boot, 1B steps, private `wt-data` media: no
  fault (`LastFault vec=0`), and the hatch is **gone** — frame SHA256
  `A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`,
  zero neighbor-jump anomaly rows below the text region (baseline: 286
  rows), smooth scanlines; triangle counters draws=2706/depth=15667336/
  zclipCull=21526/frames=68/stalls=34 unchanged, **pixels
  88,944,649 -> 89,142,121 (+197,472 healed fragments** over 68 frames),
  PGRAPH stream byte-identical in length (223956 methods).
- (ii) Dashboard neutral pad-recipe boot, 1B steps: frame SHA256
  `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`
  **identical to the pre-change baseline**, and draws=33632 skipped=1402
  pixels=285171381 textured=278577344 clears=695 blended=211696978
  depth=136 zclip=0 zclipCull=21526 frames=686 stalls=686 all identical.
- (iii) `dotnet test -c Release`: **1115/1115** green (1113 baseline + 2
  new), zero changes to existing expectations.
- (iv) `dotnet format --verify-no-changes` clean.

**Deliberate deltas / risks.** (1) Compare quantization uses XGrid's
round-to-nearest encode (the §97-kept convention), not xemu's documented
hardware `floor`; self-consistent with XGrid's store, spec-shaped
(D3D/GL convert-to-attachment-precision), and the floor-vs-round store
deviation stays exactly as §97 logged it — switching either to floor would
flip existing goldens (`DepthTest_PassesNearerAndWrites` pins `0x80000000`,
which floor(z=8388608±) would not reproduce). (2) Dashboard `depth=136`
tested pixels happen to sit far from quantization boundaries, so the frame
is bit-identical; a future guest fighting depth at exact half-ULP
boundaries could see a different winner than pre-fix (that behavior was
the bug). (3) The `--fb` raw dump for this boot reads `addr=0x00000000`
(unresolved surface offset -> kernel-memory noise); the flip-captured
`--frame` is the real surface — pre-existing, out of scope.

**Files:** `src/XGrid.Nv2a/Nv2aMmio.cs` (field-space depth compare),
`tests/XGrid.Nv2a.Tests/PgraphTransformTests.cs` (PUT/barrier kick fix,
`PushMethodsContinue`, 2 new tests).

## §110 — GPU backend design

Design document for XGrid's first host-GPU backend slice:
`docs/research-notes/gpu-backend-design.md` (wave `wave/gpu-backend-scope`,
2026-10-13). Contents: method/state subset inventory (dashboard figures from the
M5-era notes + a fresh 1B-step triangle-ISO `--pgraph` capture: 223,956 methods,
471 unique pairs, ~98.9% handled by `RecordMethod`, named inert rows; 007 histogram
open), the `RecordMethod`→`FlushDraw`→`FillTriangle`→`DisplayFrame` seam map,
first-slice scope (menu subset; textures 02–07/10–12/1E only, no DXT per §105;
combiner observed `stages=1`), translation options with recommendation **(a)
translate XGrid's own `PshCombiner`/`VshProgram` semantics to GLSL** (CPU vertex
path kept in slice 1), Playtest-v2 applicability matrix (uniform dirty-sections +
probe-gated GL first; seeds after ship; writeback at eviction; cost model likely
never), DisplayFrame-parity present path preserving §43/§106 flip-stall semantics,
three slices each with a golden-frame acceptance gate (software path always
retained behind a flag), risks (stream growth + re-inventory procedure, GL/VK,
async-pusher/§106 stall, tolerance policy, threading), and a verbatim next-wave
work order. No `src/`/`tests/` changes in this wave.

## §111 — Idle-skip V1: opt-in fast-forward of verified spin-park loops (2026-10-13)

Branch `wave/idle-skip` (worktree `Temp\opencode\worktrees\idle-skip`). First
SAFE, opt-in CPU fast-forward increment per the velocity program ("idle-skip
before full JIT"). Scope: `src/XGrid.Cpu/` + `src/XGrid.Host/` + CLI flag +
`tests/XGrid.Cpu.Tests/`. Default OFF: with no flag the interpreter path is
byte-identical to master (proven below).

**Measured facts first (the park question).**
- Guests **never execute HLT** in 1B-step boots: `hlt=0` (probe counter) in
  both dashboard and triangle runs. Parks are SPIN LOOPS, confirming §§32/107.
- The dominant park in both boots is the **kernel idle thread at
  `0x8001B02E`** (from `--ramdump` + `objdump`):
  `sti; nop; nop; cli; cmp 0(%ebp),%ebp; je 0x8001B043; cmpl $0,0x2c(%ebx);
  je 0x8001B02E` — IRQ-wait spin over dispatcher state (`[ebp]` ready-queue
  at VA `0x80035C2C`, interrupt/DPC flag at `0x80035C08`), with a forward
  conditional sub-path that skips the dispatcher-call path when idle. In the
  triangle boot it consumes >=250M of 1B steps (progress EIPs
  `0x8001B031/0x8001B047` from 750M..1000M, flag-off).

**Design (candidate V1, chosen safest that meets acceptance).**
Runtime state machine (`IdleSkipDetector`) observes every executed instruction
(flag-off: detector is null, zero behavior change): candidate anchored on any
taken backward branch; body validated per iteration as straight-line with
(single) backward exit plus forward-conditional sub-paths, executed instruction
stream recorded (forward jumps make linear re-decode unsound), allow-listed
mnemonics only (no memory writes, no IO, no indirect control), runtime access
notes through `TranslateLinear` (all fetches + MMU-on data) reject any
non-RAM physical or write immediately, ESP stable across the iteration head.
Safety theorem: the body must be a **fixed point** of (register file, unchanged
memory, IF) — first-definition-from-memory/invariant rules reject register
countdowns (`dec ecx; jnz`) and pointer chases, so the exit decision cannot
change without an external write; therefore skipping until an external event
is exact. Events: (1) pending+enabled PIC request (hard stop each chunk; for
bodies containing `STI` the check ignores head-time IF — an IRQ is deliverable
mid-body, unit-tested); (2) watched-RAM dword change (union of the body's
data reads, baseline snapshotted at trigger); (3) host deadline
(`IIdleSkipHost.StepsUntilNextDeadline`) clamps the chunk so a fake device's
scheduled event lands EXACTLY on its cycle; (4) instruction budget. Advance in
chunks of 4096 cycles (`Tsc += chunk`, `host.AdvanceIdle(chunk)` — machine
`Advance`, all devices are remainder-linear per XboxBus.Advance docs), counting
skipped steps toward `--steps`; `XboxMachine.Run` subtracts
`Cpu.TakeIdleAdvanced()` so devices are never advanced twice. Threshold 128
validated iterations. MMIO/PIO/write inside a measured body poisons that head
(per-loop disable, counted). The real machine has no device-deadline API
without touching sibling-owned device projects, so it relies on the IRQ/watch
stops with chunk <=4096 (finer than the baseline's own 65536-step batch
quantization). `--idle-skip-probe` measures without skipping; the CLI prints
the counter line only when the flag is on.

**Acceptance evidence.**
- (1) Flag-off: `dotnet test` **1124/1124** green (1113 baseline + 11 new
  `IdleSkipTests`; zero existing expectations touched);
  `dotnet format --verify-no-changes` clean. Dashboard neutral recipe
  (pad-script + `--break-log 1859D/1E420`, 1B, fresh `wt-data` copies),
  pre-change baseline vs final build: frame SHA256 identical
  (`4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`),
  full logs byte-identical (only the `Frame <path>` output name differs):
  HIT `972246032 EIP=0001859D`, HIT `972349245 EIP=0001E420`,
  draws=33623 skipped=1400 pixels=285152644 frames=685 stalls=685,
  Interrupts=3152, EOS checks=244055 fires=0, LastFault=0, EIP=0x0002EA06.
- (2) Flag-on unit tests (synthetic loops + fake host with one scheduled
  event): exact deadline landing (`Advanced == 1000` for deadline 1000);
  bounded poll-exit overshoot (poll at 5000, chunk 4096 -> stop in
  (5000, 5000+4096], exact when deadline known); pending+enabled IRQ never
  skipped (0 advance when pending at trigger; exact-deadline stop when the
  event raises the IRQ); MMIO-classified body and PIO body disable skip
  (0 triggers, accessAborts>=1); register countdown and memory-write bodies
  rejected; STI-body IRQ wake with IF clear at head; measure-only probe;
  flag-off byte-identity (Tsc==steps, no host calls).
- (3) Flag-on integration, 1B steps each, sequential vs flag-off, machine
  shared with sibling agents: **triangle**: no fault, frames=68, draws/pixels
  counters and final frame SHA **identical to flag-off**
  (`72F880C7...`); `skippedSteps=447,784,409` (44.8% of budget),
  triggers=7561, lastTrigger=0x8001B02E; wall **230.7s vs 437.1s = 1.90x**.
  **dashboard**: no fault, frames=686, frame SHA identical to flag-off plain
  (`4EF1FBF6...`), pad-script events fired at their exact steps;
  `skippedSteps=129,424,394` (12.9%), triggers=2203, stopIrq=203;
  wall **317.3s vs 516.7s = 1.63x**. Effective rate during parks: skipped
  chunks cost ~ns/cycle (device remainder math only) vs ~70ns/instruction
  interpreted.
- (4) format verify exit 0.

**Recipe semantics (documented in the CLI banner).** Absolute-step recipes
(`@950000000`, `--break-log` HIT step numbers, `--save-at`, `--eip-hist`)
apply to **flag-off runs**: flag-on IRQ/watch delivery is chunk-quantized, so
step-numbered events may shift (dashboard flag-on drew 33638 vs 33632 — a few
vblank-timed draws differ; final frames matched). Pad-script/boundaries are
still honored under flag-on (batches clamp to event steps). Semantic
milestones that must hold flag-on: no fault at 1B, frames present, pad events
fire, dashboard/triangle counters within noise.

**Open risks / follow-ups.**
- MMIO/PIO parks (e.g. NV2A flip-stall register polls, PIT/RTC port waits)
  are rejected by design; capturing them needs per-device read-effect modeling
  (V2), not covered here.
- Per-head poisoning means self-modifying loop code is not re-detected.
- `stopWatch=0` in live runs: watched idle-loop RAM only changes via the ISR
  path, so the IRQ stop always fires first (watch remains the unit-tested
  correctness net for DMA-written polls).
- Wall-clocks were taken under 3-way agent contention; ratios are back-to-back
  but absolute insn/s varies. HLT remains "stops the run" (existing behavior,
  guests never hit it).
- Tests: 1124/1124; snapshot format untouched; V1 snapshot resets the
  transient detector on load.

## §112 — 007 post-drain

Branch `wave/007-postdrain` (worktree `Temp\opencode\worktrees\007-postdrain`).
PROBE-ONLY wave: no `src/` or `tests/` changes. Goal: map 007's phase after the
§107 PlayMPC drain gate and find the next gate. EOS quirk left default-ON per
§92/§94 (it never fired: `fires=0` through 18.1B). Private media copies in
`wt-data\` (BIOS/MCPX/EEPROM/qcow2); shared CHD on `E:` read-only.

**Probe origin (fresh v2 snapshot, cold run).** Master @692653c Release CLI:
`boot ... --steps 13100000000 --save-state postdrain.xgs --eip-hist 10000000
--watch 0xDA7CB0,0xDA7DE8 --frame pd13.bmp --dump 0xDA7CB0,0x150 --ramdump
pd13.bin` — EXIT=0, wall 4347.7 s (contended host, ~3.0M insn/s; sibling agents
running in parallel), saved at step 13,100,000,000 EIP `0x00021308`,
`LastFault vec=0`. Restore verified: `--load-state postdrain.xgs --steps
1000000` loads at EIP `0x21308`, continues to `0x213B1`, no fault, counters
continuous (EOS checks 200425->200441). Determinism cross-check: node position
at 13.1B = 189,344 — byte-identical to §107's recorded value.

**Phase timeline (every row = watch/counter/frame/log evidence from this
wave's runs; logs in worktree `logs\`).**

| Step window | EIP range | Identified activity | Evidence |
|---|---|---|---|
| 0 – 0.5B | `0x800422B9/B0` (kernel) | Kernel boot + XBE load + PlayMPC setup | run13 Progress @250M/500M (`logs\run13.utf8.log`) |
| 0.5 – 0.75B | `0x800422B0` -> `0x138D1` | Transition into PlayMPC streaming (stream-clock helper live by 750M) | Progress @750M; EIPHIST 0x138xx helpers 162/1310 samples over the run |
| 0.75 – 2.52B | `0x138xx`/`0x1Exxx`-`0x21xxx` | Splash MPC sequence: channel 0x100, then 0x200; drain completes naturally at ~2.516B (§107 node removal +115,757,484 from 2.4B) — no forced write, quirk `fires=0` | run13 watch: exactly 11 mutations of channel-list fields (`0xDA7DCC/DD0/DD4`), first-writer EIP `0x2999A`-`0x299B0` (create), last-writers `0x11C72/0x11C7F/0x11C85/0x114DE` (unlink/free); EOS `checks=200425 fires=0` |
| 2.52 – 13.1B | PlayMPC `0x1Exxx`-`0x22xxx`, kernel excursions `0x80019855`/`0x800231E1` | Channel 0x200 (8 s) completes, channel 0x300 (54.3 s) plays to position 189,344; intro FMV running: DVD READ(10) `0x28` stream, ~22 fps present flips, CPU-blit frames | run13 Progress every 250M; PrintState `frames=500 stalls=500 draws=2672` (draws frozen — video is written to the surface outside PGRAPH and presented by flip; surfaces alternate `9BC000`/`890000`); ATAPI packet ring strictly sequential LBAs (`0x9719E`+4 steps); node dump: ch `0x300` at `[+0x8]`, `[+0x14]=189344`, `[+0x18]=2607407` (54.32 s); watch final state head=`0x80DA7DE8`, active count=1; `pd13.bmp` = real FMV frame (mean luma 45.6, 65.75% lit, letterbox y73-407, 2634 sampled colors — NOT the §92 full-white pre-drain splash) |
| 13.1 – 15.1B | `0x1Exxx`-`0x21xxx` | Steady 0x300 playback continues, no phase change | 4 x 500M batches (`logs\pd136/pd141/pd146/pd151.log`): EXIT=0 each, `LastFault vec=0`; frames 500->519->538->557->576 (+19/500M = 22 fps); draws frozen 2672; node pos 189,344->220,544->251,136->281,312->312,640 (61-62k samples/B); BM starts +50/500M (video DMA); ZERO Watch writes (no channel churn); EOS `fires=0`; `voiceOff=1` unchanged (no voice restarts) |
| 15.1 – 17.1B | `0x1Exxx`-`0x22xxx` (end `0x222CA`) | Same steady playback; measured throughput 4.53M insn/s | 2B batch `logs\pd171.log`: EXIT=0 in 7:25.6, frames->653, node pos->434,720, EOS checks->261825 fires=0, no Watch writes; EIPHIST (200 samples @10M) shows only PlayMPC/clock EIPs, no `0x20FCx`/`0x602F7` |
| 17.1 – 18.1B | end `0x218BF` (PlayMPC) | Event-hunt window: NO drain re-entry, NO stream end, NO menu polling | 1B batch with `--break-log 0x20FC1 --break-log 0x11340 --break-log 0x20BD0 --break-log 0x602F7` (`logs\pd181.utf8.log`): **0 HIT lines**, EXIT=0 in 3:49.1 (4.37M/s — break-log costs ~3% vs main loop); frames->691; save `pd181.xgs` verified (load +1M, EIP `0x218BF`->`0x1E133`, no fault) |

Frame progression proof: `pd13.bmp` vs `pd171.bmp` differ in 273,055/307,200
RGB pixels — the FMV image advances across batches (present path = CPU-written
surface + flip; NV2A draw/texture counters stay frozen at 2672/33,087,288
throughout 13.1-18.1B, so no per-frame GPU work). By 13.1B the pre/early-stream
rendering had already accumulated **62,444,852 format-`0x30` texture-sample
misses out of 62,444,852 attempts** (all missed; formats `06`/`12` account for
all 33.09M OK samples) — the §105 open item is real and heavy on this title's
menu/splash rendering, though the video phase itself does no texture sampling.

**NEXT GATE: none found — gate-free progress 0 -> 18.1B steps.** The post-drain
ground is 5.0B steps beyond §107's 13.1B frontier: no fault (`LastFault vec=0`
every batch), no new EIP plateau (all EIPs stay inside the already-running
PlayMPC call: `0x1Exxx`-`0x22xxx` + `0x138xx` clock helpers), no device waits
video keeps flipping / DVD keeps streaming / APU keeps advancing / pad
interrupts keep NAK-ing, no channel churn (watch silent), and the drain loop,
node-removal, PlayMPC-entry and kernel-GetState addresses were never hit in a
dedicated 1B break-log window. **Honest statement:** the stream's natural end
remains the next candidate gate. Measured pace (61,344 samples/B over
13.1-17.1B; channel 0x300 total 2,607,407 samples; position ~495,760
extrapolated at 18.1B) puts channel 0x300's completion at **~52.5B total
steps** (consistent with §107's "~40B more" from 13.1B; the briefing's "~40B
total" is a lower bound). What happens at that end — drain-loop re-entry
(`0x20FC1`), next MPC file (`ASSETS\SOUND\VIDEO\{ATT,HILITE,MGM,EAG}.MPC` per
§98 strings), or a jump to menu/input polling (`0x602F7`) — is unverified past
18.1B. Reaching it costs ~34.5B steps (~2.2 h at the contended 4.4M/s; ~41 min
at the 14M/s idle-host rate).

**Recommended follow-up acceptance test (verbatim, next wave):**

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd181.xgs --steps 34500000000 --save-state pd526.xgs --break-log 0x20FC1 --break-log 0x11340 --break-log 0x20BD0 --break-log 0x602F7 --frame pd526.bmp
```

Acceptance: a `HIT` line (drain-loop entry / node removal / PlayMPC re-entry /
kernel GetState) with its step number, OR a fault line, OR a new EIP plateau in
the final state — each identifies the next gate exactly; re-snapshot (v2)
immediately at the HIT step for the decode wave. Chunk the 34.5B via
intermediate `--save-state` if wall-clock risk is high (500M-2B batches cost
2-8 min each here).

**Artifacts (worktree, NOT committed — paths only):** snapshots
`postdrain.xgs` (13.1B), `pd136/pd141/pd146/pd151/pd171/pd181.xgs` chain
(13.6/14.1/14.6/15.1/17.1/18.1B), `pd14.xgs` (orphan duplicate of the 14.1B
state from a duplicated launch, superseded by `pd141.xgs`); frames
`pd13/pd136/pd141/pd146/pd151/pd171/pd181.bmp`; 64 MB RAM dump `pd13.bin`;
batch logs `logs\pd136..pd171.log`; key logs `logs\run13.utf8.log` (full
13.1B run: Progress timeline + PrintState + watch + EIPHIST) and
`logs\pd181.utf8.log` (zero-HIT break-log window) — `logs/` is repo-ignored,
so these stay worktree-only with their decisive lines quoted above; helper
scripts `run13.ps1`, `run_pd14.ps1`.

**Infra incidents (open, not emulator bugs):** two process deaths were
observed and NOT reproduced — (1) a foreground 13.1B attempt exited code -1
right after its `Progress 4250000000` line with no stderr (the identical rerun
completed 13.1B clean through the same EIP sequence); (2) a detached
`--load-state` 1B attempt's wrapper died without its exit marker and its
`*>`-redirected log ended up 0 bytes (one completed copy of that same command
still produced `pd14.xgs`). No Windows application event-log entries were
found; all subsequent foreground 500M/1B/2B batches and both restore verifications
completed EXIT=0. Suspect external process interference on the shared host
(sibling wave agents run their own `dotnet` boots concurrently), not a runtime
crash — next long run should keep the foreground+`Tee` pattern or re-check
process liveness.

**Open questions:** (a) phase after channel 0x300 ends (~52.5B) — drain vs next
MPC vs menu, per the acceptance command above; (b) the present path for FMV —
CPU blit vs PVIDEO overlay (draws/textures frozen while pixels change; decode
only if a visual bug appears); (c) format-`0x30` texture miss (62.4M hits by
13.1B, §105) — impact on menu/splash visuals unassessed; (d) APU `voice list
cycle` log lines are benign empty-list probes (3 lists x repeated, no state
damage observed).

## §113 - GPU-backend slice 1: IPgraphBackend seam + Silk.NET OpenGL backend, golden gate PASSED (2026-10-13)

Work in `Temp\opencode\worktrees\gpu-slice1` (`wave/gpu-slice1`, branch from
master @459f742). Implements `gpu-backend-design.md` §8 steps 1-7 verbatim
with the operator checkpoint resolved to **Silk.NET.OpenGL 2.23.0** (pinned
in `Directory.Packages.props`; no other new dependency). Acceptance met on
all three clauses (evidence below). Test baseline at wave start **1126**;
after the wave **1200/1200 green** (+74 new tests) with
`dotnet format --verify-no-changes` clean.

### What was built

- **Seam (`src/XGrid.Nv2a/`, minimal additive):** `IPgraphBackend` +
  `PgraphVertex`/`PgraphFragmentState`/`PgraphTextureSlot`/`PgraphDrawInfo`/
  `PgraphClearInfo`/`PgraphSurfaceRecord` (new file `IPgraphBackend.cs`);
  `Nv2AMmio.Backend { get; set; }`; `Backend?.Method` at the top of
  `RecordMethod` (after the side-tap); `Backend?.Draw` at the post-bbox
  flush sites of `FillTriangle` (legacy/non-combiner) and `FillTrianglePsh`
  (combiner) with state packed by `BuildDrawInfo`/`BuildLegacyDrawInfo`
  (resolved clip/fragment/texture/fog/zclip/surface + live `PshCombiner`);
  `Backend?.Clear` once per `ExecuteClear` with only the fills the software
  path actually performed; `Backend?.Flip` in the `0x12C` case after
  `CapturePresentedFrame`; `TryCopyPresentedFrame` serves the backend
  exclusively when `Backend != null` (no software/GPU pixel mixing, ever);
  `Reset()`/`LoadState` call `Backend?.ResetState()` (snapshot format v2
  unchanged — backend is host-side only). Seven `private` texture/depth
  helpers widened to `public` for upload/parity reuse (`DecodeTextureTexel`,
  `SwizzleOffset`, `IsLinearTextureFormat`, `TextureFormatBytesPerTexel`,
  `WrapCoordinate`, `ZetaDepthRange`, `EncodeDepth16/24`, float-zeta
  converts); `PshCombiner` protocol constants (Reg*/Map*/Out*/Flag*/Control*/
  Final*) made `public` so the GLSL generator reads the same single source.
- **`src/XGrid.GpuGl/` (new project, referenced by `XGrid.Cli` only):**
  `GlPgraphBackend : IPgraphBackend` (FBO surface model, shader/texture
  caches, fixed-function state, flip readback), `PshGlslGenerator` (GLSL
  330 fragment generator written from `PshCombiner.Evaluate` +
  `Nv2AMmio.ResolveTexel` semantics — facts/structure only, zero xemu/Cxbx
  code), `GlStateMapper` (blend/depth/stencil/mask/alpha/clear mappings,
  pure), `WglContext` (hidden STATIC window + WGL context via user32/gdi32/
  opengl32 P/Invoke — no windowing package), `GlProbe` (probe.c pattern:
  tiny program compile, FBO complete, known-texel draw ±2), and
  `GlBackendFactory.TryCreate` (probe-gated; failure prints why and the CLI
  falls back to `sw` for the session).
- **CLI:** `boot --gpu sw|gl` (default `sw`); `--gpu gl` constructs the
  backend after `machine.Reset()`, prints `GPU backend=gl probe ok;
  GL_VERSION=...` or the probe-failure fallback line; end-of-run summary
  prints `GPU backend=gl draws= clears= flips= methods= programs= uploads=
  unsupported= unsup[colorBytes= zformat= modes= cubeVol= fbo= flip= fatal=]
  BackendUnsupported=<n>`.
- **`src/XGrid.Graphics/`:** `FrameDiff` (§3.4 policy comparator: reference
  edge map, ±1 px Moore band, per-channel ≤2, flat-exact requirement) used
  by the golden harness and 6 new unit tests.
- **Harness:** `scripts/gpu-slice1-golden.ps1` runs the §95 neutral 1B boot
  twice (`--gpu sw` / `--gpu gl`, verbatim pad-script
  `down@950000000+hold,none@962000000,a@970000000+hold,none@976000000`) and
  applies the policy + structural checks.
- **Tests (+74, zero changes to existing expectations):** 64 headless
  `XGrid.GpuGl.Tests` (GlStateMapper enums/behaviors, GLSL string goldens
  `Goldens/legacy.glsl` + `Goldens/menu-1s.glsl`, key stability, mux
  MSB/LSB, CLIPPLANE discard, invalid-stage none-texel, passthru), 6
  `FrameDiffTests`, 4 `PgraphBackendSeamTests` (recording backend; software
  RAM result byte-identical with backend attached; Method/Draw/Clear/Flip
  observation; ResetState on machine reset).

### Acceptance evidence (design §8 step 6, exact commands + outputs)

Media: private `wt-data\` copies (Complex_4627.bin, mcpx_1.0.bin,
eeprom.bin, xbox_hdd.qcow2); triangle ISO untouched read-only. Host:
Windows desktop, NVIDIA GL **4.6.0 driver 610.88** — probe
`probe ok` (real context; no honesty-gap needed).

Runs (worktree root, Release build):

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --steps 1000000000 --pad-script "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000" --gpu sw --frame golden-sw.bmp
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot ... --gpu gl --frame golden-gl.bmp
```

1. **Both runs `LastFault vec=0`** (exit 0).
2. **Structural counters identical sw↔gl** (sw counters are pusher/
   dispatch-level and run unconditionally, so equality is exact):
   `NV2A draws executed=33632 skipped=1402 pixels=285171381
   textured=278577344 clears=695 blended=211696978 depth=136 zclip=0
   zclipCull=21526 frames=686 stalls=686 ...` — byte-identical lines in
   both logs; `Frame ... 640x480 nonzero=307200 ... surface=3E2C000
   pitch=2560 valid=True frame=686`.
3. **`BackendUnsupported == 0`** with named sub-counters all zero:
   `GPU backend=gl draws=2760841 clears=695 flips=686 methods=18125062
   programs=11 uploads=13 unsupported=0 unsup[colorBytes=0 zformat=0
   modes=0 cubeVol=0 fbo=0 flip=0 fatal=0] BackendUnsupported=0`.
4. **BMP diff (§3.4 policy): `violations=0`, `maxDelta=1`,
   `outsideBand=0`** — 2602 differing pixels (0.85% of 307,200), every one
   inside the ±1 px edge band with per-channel delta ≤ 1; flat regions
   byte-exact. SHAs: sw `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`
   (= the §95/§109 recorded dashboard submenu frame — the sw leg reproduces
   the canonical oracle), gl
   `B1B6CDAE...`→ final `B6116DAE805822BF0D7C984516B9EC18E8D4AA56A148045BB32F0FCC7CFCF33D`
   (gl SHA changes across backend revisions; the sw SHA is the stable one).
5. **Default path unchanged:** full suite 1200/1200 green (1126 baseline +
   74 new; zero existing expectations touched) and
   `dotnet format XGrid.sln --no-restore --verify-no-changes` exit 0.

### Fidelity findings (the gate taught these; recorded as facts)

Three mechanisms were required to reach violations=0 — each diagnosed from
quantified diffs, never guessed:

1. **Integer texelFetch sampling.** Normalized `texture()` with a
   `u*(1/size)` scale round-trips through FP and flips `floor` at
   texel-boundary UVs (glyph columns landed on the wrong atlas texel: a
   2px vertical bar of ±116-delta pixels at x=468-469 in the first pair).
   The GLSL sampler now computes `WrapI(floor(coord), size, mode)` in
   shader integers — the exact `Nv2AMmio.WrapCoordinate` arithmetic (GLSL
   int division truncates toward zero like C#) — and fetches with
   `texelFetch`. Upload path reuses the software `DecodeTextureTexel` /
   `SwizzleOffset` tables (widened to public).
2. **Software-exact barycentric in the fragment shader.** GL's own
   varying interpolation evaluates the perspective-weight expression in a
   different FP order than `FillTrianglePsh` (ULP drift flips boundary
   texels again: 128 → 77 residual violations after (1)). The three
   transformed vertices now travel as per-draw uniforms (`uPx`, `uColorV`,
   `uSpecV`, `uT0V..uT3V`, `uVtx`, perspective/invs/clip) and the shader
   re-runs the software formulas verbatim (`area/invArea/w0/w1/w2`,
   inclusive any-sign inside-test, `s0/s1/s2/sum/invSum` perspective
   weights, `p1*a+p2*b+p0*c` attributes, `w1*aZ+w2*bZ+w0*cZ` depth) from
   `gl_FragCoord` — the design's "screen-space vertex buffer at FlushDraw"
   (§8 step 4). A `UseProgram`-before-uniforms ordering bug found on the
   way (per-draw uniforms were binding to no program) explains part of the
   first pair's diff as well.
3. **Coverage-superset inflation + shader inside-test.** The residual 77
   violations were software's *inclusive* edge test double-covering
   shared-quad-diagonal pixels (two triangles each accept a center lying
   exactly on their shared edge → the blend runs twice in software; GL's
   top-left rule runs it once). Fix: `gl_Position` triangles are inflated
   2% about their centroid so GL rasterizes a strict superset, and the
   shader's software-exact inside-discard trims it back to exactly
   software's per-triangle pixel set — tie pixels now execute the fragment
   (and the blend) twice, byte-matching the software double-blend.
   Result: 77 → 0 violations, maxDelta 128→1.

Other decisions: FBO is a **grow-only surface** (software keeps surface
pixels across clip-window changes; clip-shrink flips read the same window —
this turned the first pair's 34 `flip` unsupported counts into 0); flips
read back the `record.Width×record.Height` window when the FBO is ≥ it;
`Method()` is an observation counter only (the seam transfers all
slice-1-relevant state through Draw/Clear/Flip, so `BackendUnsupported` is
driven by draw/clear/flip-time checks with named sub-counters); texture
change detection is a cheap head/tail/strided fingerprint per draw
(documented risk — full-buffer CRC was measured too slow per draw).

### Open risks / slice-2 pointers

- **Exotic modes are counted, not implemented** in the GLSL emitter:
  valid-stage DOT family + BUMP (`unsup[modes]`), cubemap-flagged 05/06 +
  volume 05/06 success paths (`unsup[cubeVol]`), `Z_FORMAT` float depth
  with depth test (`unsup[zformat]`), non-4-byte color surfaces
  (`unsup[colorBytes]`), FBO grow/zeta reallocation (`unsup[fbo]`), flip
  window larger than the FBO (`unsup[flip]`), any GL exception
  (`unsup[fatal]`, backend then stops issuing GL calls). The menu trips
  none (all zero above); a title that does fails the gate loudly.
- Clip-origin assumption: readback treats the presented window as surface
  rows `0..clipH-1` (matches `CapturePresentedFrame`); draw y is
  translated by `clipY` and `px/py` reconstructed as
  `gl_FragCoord.x` / `gl_FragCoord.y + uClipY` — exact for the menu
  (`clipY=0`); a clipY>0 title should be re-validated by the golden pair.
- Texture fingerprint = 16 sampled texel words/draw (head/tail/strided);
  an interior-only re-upload between draws could be missed — the golden
  pair is the tripwire.
- GLSL `float` FMA contraction is driver-defined; this driver's output is
  bit-stable for the recorded frame (violations=0), but goldens are per-
  driver until a sw-vs-sw noise floor (design §7 risk 4) says otherwise.
- Swapchain/windowed present, Vulkan backend, shader disk cache, async
  submission: out of slice 1 (design §3/§5/§7 unchanged).
- Build note: `AllowUnsafeBlocks` enabled only in `XGrid.GpuGl` (pointer
  overloads for Silk tex uploads/readback); no other project changed its
  build flags.

## §114 — 007 stream end

Branch `wave/007-streamend` (worktree `Temp\opencode\worktrees\007-streamend`).
PROBE-ONLY wave: no `src/` or `tests/` changes. Master @459f742 Release CLI,
restore + build clean. Goal: run §112's verbatim follow-up (pd181 → 52.6B with
the four `--break-log` addresses), catch the stream-end event, characterize it.
EOS quirk left default-ON per §92/§94 — `fires=0` for the entire wave
(checks 505,966 at 18.1B → 9,905,390 at 56.6B). Media: private copies in
`wt-data\` (BIOS/MCPX/EEPROM/qcow2), shared CHD on `E:` read-only; `pd181.xgs`
copied from the `007-postdrain` worktree (read-only source). `--idle-skip` NOT
used (flag-off authoritative steps).

**CLI facts learned this wave (affect any rerun):** (1) `--break-log` runs the
WHOLE `--steps` budget in the single-step loop (`Program.cs:423-463`) — no
`Progress` lines, and a HIT only prints; it does **not** exit early (only
faults/`Halted` exit). (2) `--save-at` with `--break-log` never triggers
mid-run (the check lives in the batch loop only) and — worse — `--save-at` set
at all makes the post-loop save `return 0` **before** `PrintState`/`--frame`
(`Program.cs:602-605`), so it was deliberately omitted from every run here.
(3) `Advance` quantum differs by mode: every 4096 steps under `--break-log`
vs 65536 in `machine.Run` batch mode — `SevenEosQuirkChecks` counts
`Advance` calls, so its rate is ~244/M (break-log) vs ~15.4/M (batch); the
coarse guest state converges but near-deadline interleavings can diverge
(observed below). (4) Progress from a `--load-state` run is steps-since-load;
absolute = 18.1B + progress for the main chain.

**Method — chunked verbatim (per §112's own "chunk if wall-clock risk is
high").** §112's acceptance command run as 10 × 3.45B batches, each with the
four break-logs (`0x20FC1`/`0x11340`/`0x20BD0`/`0x602F7`), `--watch
0xDA7CB0,0xDA7DE8`, `--dump 0xDA7CB0,0x160`, per-batch frame + v2 snapshot
chain `pd181 → pd2155 → pd2500 → pd2845 → pd3190 → pd3535 → pd3880 → pd4225 →
pd4570 → pd4915 → pd526` (names = absolute 0.01B steps). 699-806 s per batch
(~4.3-4.9M insn/s, contended host). Template (only `--load-state`/
`--save-state`/`--frame` vary):

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd4915.xgs --steps 3450000000 --save-state pd526.xgs --break-log 0x20FC1 --break-log 0x11340 --break-log 0x20BD0 --break-log 0x602F7 --frame pd526.bmp --watch 0xDA7CB0,0xDA7DE8 --dump 0xDA7CB0,0x160
```

**Phase timeline (18.1B → 56.8B; every row = watch/counter/frame/log evidence;
logs in worktree `logs\`).**

| Step window (absolute) | EIP / event | Identified activity | Evidence |
|---|---|---|---|
| 18.1 – 52.495B | PlayMPC `0x1Exxx`-`0x22xxx`, clock `0x138xx` | Steady channel-0x300 intro FMV, unchanged from §112 — DVD READ(10) stream, ~22 fps CPU-blit presents, no device waits | 10 batches all EXIT=0, `LastFault vec=0`, **0 HIT lines** (batches 1-9), zero watch writes in `0xDA7CB0..0xDE8`; node `[+0x14]` position linear 708,640 → 919,840 → 1,130,656 → 1,343,232 → 1,555,200 → 1,767,040 → 1,978,112 → 2,188,288 → 2,401,632 (61.1-61.7k/B); frames 691→1,881→2,010 (+129-133/3.45B); draws frozen 2,672; BM starts +~352/batch (linear video DMA); EOS `fires=0` |
| **52,495,059,473** | **HIT `0x11340`** (batch-10 offset 3,345,059,473) | **Stream end**: node removal via the real advance-callback path — ch 0x300 played to completion | `HIT 3345059473 EIP=00011340 ESI=00000000 EDI=00000001 EAX=80DA7DE8 caller=00011496 arg=0` (EAX = node VA, caller = §107's `0x11496`); watch teardown by the removal routine: `0xDA7DCC` (list head `+0x11C`)→0 @`0x11397`, `0xDA7DD4` (active count `+0x124`)→0 @`0x113C7`, free-list pointers →`0x80DA7DE8` @`0x113EB/F7`; end dump: head=0, count=0, node on free list, node `[+0x14]==[+0x18]==0x27C92F` (2,607,407 = pos==total, natural EOS, no quirk involvement) |
| 52.495 – 52.6B | end `0x8001984F` (kernel) | Post-removal tail; endpoint of the acceptance run | pd526: `Saved state pd526.xgs at step 3450000000 EIP=0x8001984F`, `LastFault vec=0`, frames=2010, EOS `checks=8928826 fires=0`, channel list already empty |
| 52.6 – 53.0B | `0x8002xxxx`/`0x8001Cxxx`/`0x00026xxx`/`0x00011xxx` (diverse) | Post-FMV transition: DVD frozen first, execution live | probe A (batch +400M, `logs\pd526p400.log`): EIPHIST 200 samples spread over kernel+module EIPs (top `0x800231D9` n=5 — no plateau); ATAPI tail still `0x9B296`, BM starts 5485 (frozen); frames +2 only; end EIP `0x8001C51B` |
| ~53.0 – 53.6B | `0x309FE`/`0x29998-0x29A66` (create), `0x20FC1` | **New STRM object created; DVD reads resume in a new LBA region; 10 voice-offs (menu/transition SFX)**; drain loop entered once | watch: `0xDA7CB0`→`0x80B6C020` (new STRM VA) @`0x309FE`, object body re-init by create EIPs `0x29998/9A/9D/9A0`+; BM starts 5485→5879; tail leaves `0x9AFxx`/`0x9B296` (frozen at 53.0) for new region (`28:00000004B4` at 53.6B end); `voiceOff` 1→11 (voices 46-50,64-67 off); ACI nam/nabm 6/93→9/149 (AC'97 traffic starts); **HIT `0x20FC1` once**: `HIT 559655459 EIP=00020FC1 ESI=00000100 ...` → abs **53,159,655,459** (break-log chain; drain for ch 0x100, single pass, not a re-park) |
| ~53.6 – 54.6B | transient `0x16AExx` (string scan) → `0x13Axxx` clock → kernel `0x8001B0xx`/`0x800153xx` → **park `{0x1C3028,0x1C302C}`** | Screen transition completes (1 fullscreen draw +5 clears onto new surface `0x3BD8000`), then the guest **parks** | batch chain: pd536 (53.6B) still transient at `0x16AE37`, draws 2672/frames 2016/BM 5879; pd546b Progress @+250M (53.85B) already `0x1C302C` → onset ∈ (53.6, 53.85]B; pd546 (break-log, 54.6B) also `0x1C3028`. pd546 EIPHIST (1B window): 189/200 samples in `{0x1C3028,0x1C302C}`, brief kernel `0x8001B030-43` before it. Draw/flip counters land identical across both quanta (draws 2,673, clears 66, frames 2,017, BM 5,885, voiceOff 11) |
| 54.6 – 56.8B (park) | `cmp dword [EAX+0x10],0; jnz` spin | **Stable gate**: main thread spins waiting for pending-count → 0; nothing clears it | pd546 → pd566 (+2B) → pd566p200 (+200M): EIP stays `{0x1C3028,0x1C302C}` (EIPHIST 100/100 both), `Halted=False`, `LastFault vec=0`; ALL main-thread devices frozen byte-for-byte (draws/frames/BM/pktbytes/APU r/w/voiceOff/USB acc/channel dump identical; `USB0 acc=54332` never moves — no pad polling either); interrupts **keep firing** (`Interrupts delivered` 92,704@52.6 → 96,127@54.6 → 99,017@56.6 → 99,307@56.8; PIC `0020=290` accesses/200M) so the park is a guest wait, not a halted IRQ path; EOS checks keep ticking host-side only (`fires=0`); held frame byte-identical across BOTH chains and all endpoint probes (SHA256 `18C7CC83CEA392DE…`) — RGB=(0,0,0) uniform alpha (no hidden layout), i.e. a black screen, vs pd526's FMV fade-out frame (`2B3397F9…`, meanLuma 0.5) |

**Park decode (from `pd546b.xgs`, verbatim):** loop at module `0x1C3028`:
`83 78 10 00` = `cmp dword [EAX+0x10], 0` ; `0x1C302C`: `75 FA` = `jnz 0x1C3028`.
Registers: `EAX=0x83924800 EBX=0x118 ECX=0xD00190A8 EDX=0x210 ESI=0x1B18
EDI=0xD001C070 EBP=0xD0031CE4 ESP=0xD0031CB4 EFLAGS=0x206`. Wait object at
`0x83924800`: `+0x04=0x4BC +0x08=0x1B08 +0x0C=0x60A +0x10=3` — the counter is
**3** and never drains over +2.2B observed steps. Candidate: an async
completion/refcount (3 pending ops) whose completion source is not firing.

**NEXT GATE (exact repro — the post-stream-end park):**

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd546.xgs --steps 1000000 --dumpv 1C3000,80 --dumpv 83924800,60
```

Fault text: none — `LastFault vec=0`, `Halted=False`; plateau EIP
`0x001C302C` (partner `0x001C3028`), `EAX=0x83924800`, `[EAX+0x10]=0x00000003`.
(Park onset per batch chain: load `pd536.xgs`, +250M already at `0x1C302C`.)
Two gates were hit this wave: (G1) the §112-predicted stream end itself —
node-removal HIT at **52,495,059,473** (repro: `--load-state pd4915.xgs
--steps 3450000000` + the four break-logs, HIT line quoted above) — fully
characterized, not a bug; (G2) the residual park above, which is the next
decode target.

**Verbatim recommended next command for the follow-on wave** (decode the park;
from the parked snapshot, re-arm watch on the wait word and see if ANYTHING
touches it, then identify `0x1C3000`'s caller/module and what should clear
`[0x83924800+0x10]=3`):

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd546.xgs --steps 2000000000 --watch 3924800,3924814 --break-log 0x1C302E --dumpv 1C2F80,100 --dumpv 83924800,60 --frame park2b.bmp
```

(`--watch 3924800,3924814` = physical of VA `0x83924810`; `--break-log
0x1C302E` = first instruction after a successful exit from the spin, so a HIT
there means the gate opened; absent that, the park persists — then bisect the
3 pending ops via the module's other code or APU/DVD state at `pd546.xgs`.)

**Artifacts (worktree, NOT committed — paths only):** snapshots (all v2, 72 MB
each) `pd181.xgs` (copied origin), chain `pd2155/pd2500/pd2845/pd3190/
pd3535/pd3880/pd4225/pd4570/pd4915/pd526.xgs` (21.55…52.6B), `pd536.xgs` /
`pd546b.xgs` (batch chain 53.6/54.6B), `pd546.xgs` (break-log chain 54.6B),
`pd566.xgs` (56.6B); frames `pd2155..pd566.bmp` + `pd526p400/pd536/
pd546b/pd566p200.bmp` (held-frame SHA above); logs `logs\pd2155..pd566.log`,
`logs\pd526p400.log`, `logs\pd566p200.log`, `logs\parkdump.log`,
`logs\parkobj.log`, `logs\sanity1m.log` (`*.log` is git-ignored; snapshots/
BMPs are NOT ignored — keep them untracked); `wt-data\` (private firmware/HDD
copies, `eeprom.bin` pattern-ignored).

**Infra:** §112's two host process kills did **not** recur — every boot this
wave exited 0 (19 runs incl. probes) except one `EXIT=4` caused by this agent
omitting `--save-state` on a probe (instant CLI error, no emulation ran).
Determinism: batch mode self-consistent (pd536 rerun byte-identical Progress
EIPs); both quanta converge to identical coarse end-state (draws/frames/BM/
voiceOff/frame SHA) by 54.6B despite near-deadline interleaving differences.

**Open questions:** (a) semantics of `[0x83924800+0x10]=3` (3 pending what?)
and which device/IRQ should clear it — park may be a real emulation gap or a
guest-side wait needing input/a stimulus; (b) why DVD reads stopped forever at
54.6B with no outstanding IDE command (BM status 0) while the park persists;
(c) next menu/input entry (`0x602F7`) and next PlayMPC channel (`0x20BD0`)
were never hit anywhere in 18.1→56.8B — after the park opens, re-arm both;
(d) post-FMV LBA region (`0x4B4`/`0x4AA62` vs FMV `0x9AFxx`) — which disc
files the new STRM object (`0x80B6C020`) streams, via `xgrid sector` if
needed; (e) `0x16AExx` string-scan spends 250M+ steps — benign bulk compare or
a pathological loop, revisit only if the park decode references it.

## §115 — nvnet first real frame (2026-10-13)

Work in `Temp\opencode\worktrees\nvnet-frame` (`wave/nvnet-frame`, base
459f742). Goal: first-real-frame validation of the §84/§88 nvnet packet
engine against live dashboard traffic, plus the smallest real TCP-proxy
path. Files: `tests/XGrid.Bus.Tests/NvNetFirstFrameTests.cs` (new),
`src/XGrid.Bus/Net/TcpProxy.cs` (new), `tests/XGrid.Bus.Tests/TcpProxyTests.cs`
(new). **No `src/` files modified, no CLI/SLN/package changes** — the engine
and backends already existed; this wave captures evidence and adds gates.

**First real frame: the dashboard DOES transmit — a 342-byte DHCP DISCOVER
(boot-time, deterministic).** §88 left "first real frame type open (no guest
TX on boot)"; that was measured before the engine's IRQ/filter wiring matured.
Recipe run (private `wt-data/` media copies, 1B steps, pad-script
down@950M/a@970M) prints:

```
NVNET tx_pkts=1 tx_bytes=342 kicks=2 cant_tx=0 rx_pkts=1 rx_bytes=342
  drop_filter=0 drop_nobuf=0 oversize=0 cant_rx=0 link=True autoneg=False
  backend=LoopbackNetworkBackend
  tx base=0x03FDB000 cur=0x03FDB000 next=0x03FDB008
  rx base=0x03FDB800 cur=0x03FDB800 next=0x03FDB808
  irq=False asserts=2 deasserts=2
NVNET reads=52 writes=325 regs: 190=22 194=10 180=6 00=6 98=2 88=2 188=2 144=1
  wr: 190=12 04=7 144=7 00=5 180=5 110=4 94=3 84=3
```

- **Differential (neutral vs pad-script, both 1B):** NVNET counter/ring lines
  are byte-identical — the frame is boot-time traffic, not input-driven; ring
  addresses and counters are run-stable. The captured frame below is from the
  pad-recipe run (xid/ident were not diffed across runs).
- **Descriptor state (`--dump 03FDB000,4000`):** TX desc0 =
  buf `46 60 33 00` (phys `0x00336046`), len `55 01` (341 → xemu's
  `desc.length + 1` = 342, `nvnet.c:510`), flags `01 00` — post-TX writeback:
  VALID + error bits cleared, LASTPACKET retained, length/buffer preserved
  (xemu `nvnet.c:536-539`). TX desc1 is zeroed: the second KICK walked to
  !VALID and stopped without a frame (`kicks=2`, `tx_pkts=1`, xemu
  `nvnet.c:518-520`). RX desc0 at `0x03FDB800` shows the post-replenish
  state (buf `0x03FDC002`, len `0x7FD` = 2045 → 2046 B capacity, flags
  `0x8000` AVAIL) — the driver re-armed the ring after consuming the loopback
  copy. `asserts=2 deasserts=2` = TX `0x10` + RX `0x02` IRQs, each W1C'd
  (xemu `set_intr_status`, `nvnet.c:552`/`430`).

**Frame capture:** `--dump 00336046,156` at 1B steps (same recipe) reads the
TX buffer — a well-formed DHCP DISCOVER, embedded byte-exact in
`NvNetFirstFrameTests` as `GoldenDhcpDiscoverHex` (684 hex chars):

| Field | Value | xemu / protocol fact |
| --- | --- | --- |
| Length | 342 B = `tx_bytes` | coalesce = sum of `len+1` |
| Eth dst/src | `FF:FF:FF:FF:FF:FF` / `00:50:F2:A4:F5:A8` | broadcast; Microsoft-OUI guest MAC |
| EtherType | `0x0800` | IPv4 |
| IP | v4 ihl5, totlen 328 (=342-14), TTL 64, proto 17, `0.0.0.0`→`255.255.255.255`, checksum folds to 0 | client has no lease yet |
| UDP | 68→67, len 308, checksum folds to 0 over pseudo-header | DHCP client→server |
| BOOTP | op1 htype1 hlen6 xid `F5EAFE08`, chaddr = src MAC | RFC 2131 |
| Magic | `63:82:53:63` | DHCP |
| Options | 53=1 DISCOVER, 61 client-id (1+MAC), 60 `"XBOX 1.0"`, 55 param-list {1,3,6}, 255 END | Xbox kernel stack |

Loopback backend returns it through the receive filter (broadcast passes,
§88) → `rx_pkts=1 rx_bytes=342`, RX IRQ, driver replenish — the full
TX→backend→RX path exercised by real guest traffic. Nothing else at 1B:
no ARP, no UDP beyond this, no TCP (zero titles speak TCP yet).

**Synthetic gate (2 tests, green):** `GoldenFrame_TxKickToRxPoll_RoundTrips
ThroughDeviceModel` drives the captured frame through the device model
directly (2-entry rings SIZE=`0x00010001`, TX desc VALID|LASTPACKET, RX desc
AVAIL cap `0x7FD` mirroring the live ring): KICK → backend SentLog[0] byte-
equal → TX writeback {buf/len preserved, flags LASTPACKET-only} → CURRENT/NEXT
+8 → TX IRQ asserted; `PollReceive` → RX buffer byte-equal, RX writeback
{len=342 raw size, flags BIT4|VALID} → `0x10|0x02` status → W1C deassert.
`GoldenFrame_MatchesCapturedLiveDhcpDiscoverFields` re-parses every field
above (both checksums recomputed, options walked) — the executable field
table.

**TCP proxy (Goal B) — host side landed, guest side honestly scoped out.**
Recon: `--net-backend loopback|socket`; `SocketNetworkBackend` already does
ARP/ICMP/UDP-NAT and drops+counts TCP (`TcpFrame_DroppedAndCounted`); the
live dashboard emits only the DHCP DISCOVER — no guest title speaks TCP at
1B. Per the gate ("proxy host side verified by loopback test + documented
integration point"): `TcpProxy` (`src/XGrid.Bus/Net/TcpProxy.cs`) binds a
host loopback listener and relays bytes bidirectionally to a target
host:port with `Connections`/`FailedConnections`/`RelayedBytes` counters,
thread-per-session pumps, `Start`/`Stop`/`Dispose`. 3 tests green: 4 KB
loopback echo end-to-end (payload byte-equal, `RelayedBytes` >= 8192),
unreachable target (`FailedConnections=1`, client EOF, proxy survives),
dispose (listener refused, `Start` throws). **Guest integration point
(documented, NOT built):** a future title's TCP segments would be terminated
by a TCP state machine inside/behind `SocketNetworkBackend` (the §88
follow-up), whose byte streams attach to `TcpProxy` sessions — building that
state machine before any title needs it would violate scope-to-the-gate.

**Acceptance:** 1131 tests green (1126 baseline untouched, +5 new: 2 golden-
frame + 3 proxy), zero existing-expectation changes, `dotnet format
--verify-no-changes` clean. No boot-path files modified (only new files added
— `git diff` empty), so default-flag boot behavior is unchanged by
construction; the pre-change recipe runs above stand as the baseline.
Open risks: the golden frame's xid/ident are boot-run-specific (deterministic
here; if the guest ever randomizes them the synthetic test still only uses
the frame as inert input); `TcpProxy` is unwired (by design — no CLI flag,
no backend hook) until a TCP-speaking title arrives; loopback TX→RX means the
guest currently receives its own DISCOVER (broadcast filter pass) — a future
`socket`-backend DHCP path would need a UDP/67 sink instead.

## §116 — kernel-test-suite bring-up: reporting mechanism found, full failure chain diagnosed (2026-10-13)

Worktree `wave/ktest-suite` (private media under `Temp\opencode\worktrees\
ktest-suite\wt-data\`; recon scripts kept in `tools/ktest-recon/`).
Time-box honesty: the suite still does NOT run its tests on XGrid - every
output path was empirically closed by *guest-kernel* semantics (statuses
below), not by an XGrid gap. Acceptance met under clause 4 (recon +
diagnosis; zero src changes; baseline 1126 green).

**Reporting mechanism (suite sources, facts only - GPL project, no code
copied; release da80f1f = current master):** `main()` in `src/main.cpp`
1) reads `D:\name.txt`, 2) **`open_output_file("D:\kernel_tests.log")` via
CreateFile(CREATE_ALWAYS) and `return`s from main if it fails**, 3) reads
`D:\config.txt` (missing -> defaults), 4) with `output_video` (default
true) runs `XVideoSetMode`+`pb_init`+`pb_show_debug_screen`, 5) every
`print()` = `DbgPrint` + optional pbkit `debugPrint` + `WriteFile` to the
log, 6) `run_tests()` iterates the 378-entry `kernel_api_tests` table
(`%03u - <Api>: Tests Starting/All tests PASSED/...` footers), 7) ends
with `close_output_file()` + `Sleep(10000)` + `pb_kill()`. Consequence:
**when the log open fails there are ZERO prints, ZERO video, ZERO tests** -
exactly the observed "boots clean for 900M, produces nothing" (§104/§106).

**Launch facts (XGrid LLE, Complex_4627 custom BIOS):** suite entry is
absolute VA `0x4DF80`; kernel `call [0x10128]` launch fires at step
**525,703,665** (DVD boot) / **525,704,363** (FATX boot) - same DVD path
cost as triangle (§106). After main's early return the kernel relaunches
the XBE forever: next entry every **9.44M steps** (DVD: kernel re-probes
disc + reloads XBE; ATAPI `5A/03/25/28/AD` cycle in the command tail) or
**~9.8M** (FATX). EIP-histogram of a cycle: dominated by kernel IDE/FS
code `0x800315xx`; suite code alive only ~33K steps per cycle.

**Kernel thunk table (NOT the problem - decoded from the XBE):** slots
hold `0x80000000 | ordinal` (unordered, terminator-0 at index 165; 165
imports, e.g. idx 67 -> ordinal 67 `IoCreateSymbolicLink` at slot
`0x57F1C`, `NtCreateFile` slot `0x57FB4` = `0x8001694D`, `DbgPrint` slot
`0x57ECC`). The kernel resolves every slot at launch (RAM dump at entry
shows kernel addresses throughout). All static `FF 15 <slot>` call sites
in the binary map to these slots. XGrid-side thunk/export handling needs
no fix: LLE kernel resolves them itself.

**Failure chain - four log-path shapes, each probed with `--break-log`
on `0x39E27` (NtCreateFile return, EAX=NTSTATUS), `0x2EE7B` (open return),
`0x4E03A` (main return):**

| path (patched into private media) | NTSTATUS | meaning |
|---|---|---|
| `D:\kernel_tests.log` (stock) | `C0000022` INVALID_ACCESS | letter `D:` resolves (kernel-created `\??\D:`); kernel XDVDFS rejects the write - **correct read-only-DVD behavior, not an XGrid gap** |
| `C:\...` / `E:\...` | `C000003A` PATH_NOT_FOUND | **no `\??\C:`/`\??\E:` exists** - DOS letter map lookup fails inside the kernel path converter (trace `0x80020A49`..; Cxbx `EmuKrnlOb.cpp:703` documents the `ObpDosDevicesDriveLetterMap` fast path) |
| `\Device\Harddisk0\Partition2\kernel_tests.log` | `C0000033` NAME_COLLISION | this kernel's converter **hard-rejects backslash-rooted paths**: `cmp byte[eax],'\'` falls into `mov esi,0xC0000033` (trace `0x80020A49-0x80020A52`) |
| `kernel_tests.log` (relative) | `C0000034` NAME_NOT_FOUND | CWD is the object root, not a volume |

**Drive-letter discovery (the root blocker):** ramdump diffs show the
kernel creates only `\??\D:` (string at kernel phys `0x127BC`, plus
`\??\CdRom0:`/`\??\A:` templates). The full set `\??\C:`..`\??\G:` exists
ONLY in a dashboard boot (dump @600M: runtime array at phys
`0x1e3d58`+ built from a `\??\%c:` format string living in the *dash*
data, `xboxdash2.xbe` file region holds `\??\D:\`/`\Device\
Harddisk0\Partition7\` path tables) - i.e. **the dash/app runtime creates
the letters, not the kernel**, and the suite (first title launched) never
gets them. Supporting evidence: DVD boot issues ZERO master HDD reads
(command tail ends `M91` setup, all later cmds ATAPI); dash boot reads
FATX `MC8` at `0x4654xx` (§47); even booting the suite ITSELF from FATX
(see injector below) still lacks letters at main time.

**Experiments (all on private media, original qcow2 backed up as
`xbox_hdd.orig.qcow2`):** (1) log path letter patches - table above;
(2) **FATX injector** (`tools/ktest-recon/inject_dash.py`: in-place
qcow2 read/write mirroring `Qcow2DiskImage`, FATX facts from §47 +
XboxDevWiki: LE header @byte `0x8CA80000` (sector `0x465400`), 8-sector
header, 128-sector u16-LE FAT, 32-sector clusters, root=cluster 1;
dirent = `[u16 namelen][name][FF pad to 42][u32 cluster LE][u32 size LE]`)
- replaced `C:\xboxdash.xbe` with the suite (chain 268+, size patched
319488, kernel loads it: entry hit at 525,704,363, XBEH verified);
letters STILL absent after a FATX-native launch; (3) device-path string
relocation (rdata rawSize 0x9160->0xA000 + lea fixup at VA `0x2EDF8`)
-> COLLISION; (4) relative path -> NOT_FOUND; (5) **guest shellcode in
the ignored `load_name_file` donor (VA `0x2E620`, 164 bytes: builds two
UNICODE_STRINGs `\??\C:` / `\Device\Harddisk0\Partition2` and `call
[0x57F1C]` IoCreateSymbolicLink)** -> kernel reaches the API but
**KeBugCheck** (CLI/HLT at `0x800151ED`; saved params include
`0xC0000005`) at identical step 33137 with both argument orders; first
iteration also exposed an `loop rel8` off-by-one (fixed) - cause of the
AV is OPEN (possible Complex_4627 ordinal-67 semantic difference vs
retail, or call-context requirement); EvoX's own letter creation
demonstrates the API works in dash context, so the kernel side is
capable.

**XGrid verdict:** no gap found in the assigned area - thunks resolve,
FATX create/write works (§47), NtCreateFile statuses above are faithful
kernel outputs. The missing piece (DOS letter links / writable D:) lives
inside the guest kernel/dash runtime, i.e. out of scope for a clean-room
emulator fix. xemu cannot arbitrate (deadlocks pre-launch, §32).

**Open / next:** (a) diagnose the shellcode AV (dump CR2/exception
record at bugcheck; compare EvoX's exact IoCreateSymbolicLink call
context; verify ordinal 67 semantics on Complex_4627); (b) once any
`print()` runs, capture DbgPrint (retail likely UART-gated - XGrid has
no 0x3F8 model) or rely on the FATX log file + `--writedump`; (c) suite
totals unknown (0 of 378 executed). Baseline: **1126 tests green, build
+ `format --verify-no-changes` clean; zero src changes, zero
expectation changes.**

## §117 — GPU-backend slice 2: triangle golden gate PASSED, FBO-grow blit landed, all §113 tripwires evidence-disposed (2026-10-13)

Wave `wave/gpu-slice2` (worktree `Temp\opencode\worktrees\gpu-slice2`,
branch from master @1f0d918). Scope per wave assignment: `src/XGrid.GpuGl/**`,
`tests/XGrid.GpuGl.Tests/**`, `scripts/`, the two research docs. The
triangle gate and the dashboard re-verify both **PASS** on this branch;
suite baseline **1205/1205** green (zero existing-expectation changes),
`dotnet format --verify-no-changes` clean. Host: NVIDIA GL 4.6.0 driver
610.88, probe ok (real context). Media: private `wt-data\` copies
(BIOS/MCPX/EEPROM/qcow2), triangle ISO read-only at
`%TEMP%\opencode\triangle-iso\triangle.iso`.

### Triangle golden gate (the slice-2 acceptance)

Harness: `scripts/gpu-slice2-triangle-golden.ps1` (twin of the slice-1
script + `--dvd`, anchor pins for the recorded counters
`draws=2706 frames=68 stalls=34` and the recorded software frame SHA;
§3.4 BMP policy identical). Run:

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd ...\triangle.iso --steps 1000000000 --gpu sw|gl --frame ...
```

Result: **GOLDEN: PASS** on both the unmodified slice-1 backend (first
pair, proving the triangle stream trips **zero** §113 `unsup[...]` rows)
and again on the final instrumented build. Evidence:

1. Both runs `LastFault vec=0` (exit 0).
2. Structural counters identical sw<->gl **and** == recorded anchors:
   `NV2A draws executed=2706 skipped=0 pixels=89142121 textured=48754488
   clears=6455 blended=518 depth=15667336 zclip=0 zclipCull=21526
   frames=68 stalls=34` (byte-identical lines in both logs).
3. `GPU backend=gl probe ok` then
   `draws=54492 clears=6455 flips=68 methods=223956 programs=10 uploads=10
   unsupported=0 unsup[colorBytes=0 zformat=0 modes=0 cubeVol=0 fbo=0
   flip=0 fatal=0] diag[fboReallocs=14 fboZetaDrops=7 clipXY=0]
   BackendUnsupported=0`.
4. BMP diff (§3.4 policy): **violations=0, outsideBand=0, maxDelta=1** —
   27,849 differing pixels (9.1% of 307,200; the Gouraud triangle is edge-
   heavy), every one inside the ±1 px edge band, flat regions byte-exact.
5. Software frame SHA verified (not trusted):
   `A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`
   = the recorded §109 hatch-free triangle oracle; gl frame
   `7A860ED7378A86E8E63403573D92D0D5B248EA93E66D7A34716947535F34FC2D`
   (bit-identical before/after this wave's FBO change — deterministic
   driver, the change is frame-neutral on this title).

Notes: the §110 §1.2 histogram's `pixels=88,944,649` predates the §109
depth-compare fix; current master counts `89,142,121` (draws/frames/
stalls unchanged — those are pusher-level). The triangle's combiner is
`stages=1 control=00000001 final=True draws=2672` — the same single-stage
shape as the menu, so the multi-stage-combiner worry in the wave brief
did **not** materialize. The pbkit depth-texture stage (`0x30`
LU_IMAGE_DEPTH_Y16_FIXED, 62,444,852 samples, all software misses via
`bytesPerTexel==0` -> none-texel) has GL parity **by construction**
(`ApplyTextures` marks `uTexOk=0` for `bytes==0`; `SampleSlot` returns
the same none-texel) — proven by violations=0, not tripwired.

### Dashboard menu golden re-verified

`scripts/gpu-slice1-golden.ps1` on this branch: **PASS** — sw SHA
`4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`
and gl SHA `B6116DAE805822BF0D7C984516B9EC18E8D4AA56A148045BB32F0FCC7CFCF33D`
are **bit-identical to the §113 records**; counters
`draws=33632 frames=686 stalls=686` identical; diff=2602 violations=0
maxDelta=1; `BackendUnsupported=0` with the same
`diag[fboReallocs=14 fboZetaDrops=7 clipXY=0]` (the reallocs are the
shared boot-prefix video-mode setup — 14 per boot on both titles).

### What changed in `src/XGrid.GpuGl` (all additive; no seam/test changes)

- **Evidence diagnostics** (`DebugSummary` suffix, informational, never
  `BackendUnsupported`): `diag[fboReallocs= fboZetaDrops= clipXY=]` —
  FBO reallocations with prior content, zeta-format-change depth drops,
  draws/clears with a clip-window origin != (0,0). These convert two
  §113 open risks from assumption into per-run measurements.
- **Content-preserving FBO realloc** (the §113 "FBO-grow window realloc"
  gap — the one silent fidelity hole behind a green counter): on grow or
  zeta change the backend now creates the new FBO first and
  `glBlitFramebuffer`s the old content in (color always; depth/stencil
  only when the zeta format is unchanged — different depth formats are
  not blit-compatible, and the drop is counted in `fboZetaDrops`). A
  failed FBO creation now **keeps the previous FBO alive** (slice 1
  destroyed it, silently disabling all later draws). Gate validation:
  the path fires 14x per boot (7 with zeta change) on both titles and
  both golden frames are **bit-identical** before/after — on menu+
  triangle the realloc-dropped content was always fully redrawn or
  cleared before present, so the fix is observably neutral here and
  content-correct for future mid-frame-grow titles.
- `scripts/gpu-slice2-triangle-golden.ps1` — the triangle gate harness
  with anchor pins (counters + sw SHA), reusable per driver/host.

### §113 tripwire disposition (every `unsup[...]` row + open risk)

| §113 row / risk | Disposition | Evidence (this wave) |
|---|---|---|
| `unsup[modes]` exotic DOT/BUMP tex modes | stays counted; GLSL emitter emits deterministic none-texel for invalid stages (software parity, §96), counts + continues for valid-stage use so the gate fails loudly | menu=0, triangle=0 (both golden gl logs) |
| `unsup[cubeVol]` cube/volume success paths | stays counted; implementing samplerCube/face-selection without a guest that uses it would be untestable speculation (Velocity rule 1) | menu=0, triangle=0 |
| `unsup[zformat]` float-zeta + depth test | stays counted; GL float-depth path (Depth32F + range compare) deferred until a title histogram proves usage | menu=0, triangle=0 |
| `unsup[colorBytes]` non-4-byte color | stays counted; software 2-byte present path exists but no menu/triangle surface uses it | menu=0, triangle=0 |
| `unsup[fbo]` EnsureFbo failure | stays counted; failure path now preserves the previous FBO (previously destroyed it) | menu=0, triangle=0 |
| `unsup[flip]` oversized flip window | stays counted; faking a larger readback would silently present wrong pixels | menu=0, triangle=0 |
| `unsup[fatal]` GL exception | stays counted (fail-loud mechanism is its own disposition) | menu=0, triangle=0 |
| FBO-grow content loss (silent behind the counter) | **IMPLEMENTED**: blit-preserving realloc + `diag[fboReallocs/fboZetaDrops]` (GL glue, gate-validated live 14x/boot; not headless-unit-testable — documented honestly) | both gates bit-identical before/after |
| clipY!=0 readback origin assumption | stays open; `diag[clipXY]` counter added as detector | clipXY=0 draws+clears on menu+triangle |
| 16-word texture fingerprint miss | stays open; the golden pair is the tripwire; full-buffer CRC remains too slow per draw (§113 measurement) | both gates violations=0; uploads=10 triangle / 13 menu |
| multi-stage combiner (wave-brief worry) | no work needed: triangle combiner is stages=1, same as menu; emitter is generic-8-stage with unit-tested shape | `stages=1 control=00000001` in the sw gl logs |

Design-doc slice-2 items beyond the wave brief, for the record: the
"full dashboard surface" milestone items are covered by the §113
pad-script gate (menu+submenu navigation) re-verified here; the §95
texture decode table (02-07/10-12/1C-1E) is one shared software table
(`DecodeTextureTexel`, widened public in §113) used by the GL upload
path — unexercised rows stay guest-unproven; `0x3628000`
render-to-texture is GPU-correct **by construction while the software
path stays unconditional**: texture uploads read guest RAM that the
software rasterizer keeps coherent, so aliasing a color surface as a
texture samples software-rendered content (the menu does exactly this
— 10,137,600 samples of offset `0x3628000` per boot, gate green). A
GPU-only software-bypass slice would need real surface tracking; that
is explicitly later scope.

**Acceptance:** (1) triangle sw<->gl golden passes policy (BMP
violations=0 maxDelta=1, counters exact 2706/68/34, BackendUnsupported=0)
+ dashboard re-verified green bit-identical to §113; (2) every §113
unsup row implemented+gate-validated (FBO-grow) or evidence-justified
(table above); (3) suite 1205/1205 green, zero existing-expectation
changes, format clean. Open risks for the next wave: clipY!=0 readback
(detector armed), fingerprint interior-miss (gate-armed), zeta-drop
depth content on format change (counted), Vulkan/swapchain/async (design
§3/§5/§7 unchanged), 007 G2 decode (§114, sibling track).

## §118 — 007 G2 park decode

Branch `wave/007-g2park` (worktree `Temp\opencode\worktrees\007-g2park`, master
@1f0d918). PROBE-ONLY wave: no `src/`/`tests/` changes in the commit — two
temporary instrumentations were added during diagnosis (an APU raise counter;
a forced ISTS bit3 raise) and both were fully reverted (`git diff` empty,
build + `format --verify-no-changes` clean, suite **1205**). Snapshots copied
read-only from the `007-streamend` worktree (`pd4915/pd536/pd546/pd546b.xgs`);
firmware private in `wt-data\`; shared CHD on `E:`; no `--idle-skip`.
Answers §114 open-q (a): `[0x83924800+0x10]=3` is **not** a pending-op
count — it is a 3-valued state enum of the DirectSound shared parameter
block (below). Park onset is now exact: **53,657,687,344** (pd536 +
57,687,344), tightening §114's (53.6, 53.85]B.

### Object identification

The gate is module `.text` function **0x1C2FFA** ("submit buffered block").
Module code uses per-page paging, so §98's `phys = VA + 0xAAF000` rule does
not apply here: VA `0x1C3000` → phys `0x1ED000` (+0x2A000; verified by
finding the gate bytes at phys `0x1ED028` and by the page-table walker
`scripts/dumpva.py`, CR3 `0xF000`). Park-site disassembly (from
`--dumpv 1C3000`, probe1):

```
1C2FFA: mov edx,[esp+8]         ; arg2 (flush hint; 0 = use accumulators)
1C3000: push esi
1C3001: jnz 1C3017              ; arg!=0 path (not taken at park)
1C3003: mov esi,[ecx+0x18]      ; accumulated bytes; this = ECX = 0xD00190A8
1C3006: cmp esi,0x8000          ; 0x8000 = "nothing pending"
1C300C: mov edx,[ecx+0x1C]
1C300F: je 1C305B               ; ret 8 when idle
1C3011: test edx,edx / je 1C305B ; jmp 1C301B
1C301B: mov eax,[ecx+0x8]       ; sub-object
1C301E: mov eax,[eax+0x10]
1C3021: mov eax,[eax]           ; shared-block base 0x83924000
1C3023: add eax,0x800           ; -> state block 0x83924800
1C3028: cmp dword [eax+0x10],0  ; <-- GATE (park; jnz -6 spins here)
1C302E: push ebx
1C302F: mov ebx,esi / shr ebx,2 / sub ebx,[eax+4] / shr edx,2 / sub ebx,0x206
1C3040: mov [eax],ebx           ; count (dwords)
1C3042: mov [eax+8],esi         ; size
1C3045: mov [eax+0xC],edx
1C3048: mov dword [eax+0x10],2  ; STATE = 2 (data ready)
1C304F: and dword [ecx+0x1C],0 / mov dword [ecx+0x18],0x8000 / ret 8
```

Shared block: global `[0x1C99B0] = 0x83924000` (phys alias `[0x1C99B4] =
0x3924000`, extent `[0x1C99B8] = 0xC000`; VA `0x83xxxxxx` is identity-mapped
to phys). Layout: 0x20-byte ring entries from +0x08 (index*0x20, builder
0x1C2FD3), **state block at +0x800** (`+0x00` count/idx, `+0x04` ring-base
idx, `+0x08` size, `+0x0C`, **`+0x10` state**, `+0x14`), ring payloads from
+0x818 whose entries carry guest-RAM buffer addresses (0x65F400, 0x44F400,
0x380200/0x390200, …) — i.e. the block is consumed by DMA from a device
(the GP/EP DSP), not by another CPU loop. Park-state fields
`+0x04=0x4BC +0x08=0x1B08 +0x0C=0x60A` are F0's parameter copy (0x1C334F),
not A's data; A never got to write.

**State machine — every writer** (exhaustive scan of module text VA
0x10000-0x1D0000 with `scripts/scanstate.py`: all `add reg,0x800` idioms +
`mov dword [reg+0x10],imm`; cross-checked against `--watch 3924800,3924818`
live):

| value | writer(s) | meaning |
|---|---|---|
| 0 | 0x1C2DAE (heap zero, alloc helper 0x1C2D71); 0x1C3370 (`rep stos` in 0x1C334F = F0 start); **0x1C37FD (FB start)** | block free / open window |
| 2 | **0x1C3048** (the gate exit above) | mixer submission posted |
| 3 | **0x1C3226** (tail of 0x1C30CD, EAX=3; reached via F0: 0x139C0→0x1C1F68→0x1C1977→0x1C1892→0x1C18A4→0x1C334F); **0x1C381D (FB end)** | parameters loaded / DSP restart complete |
| transient | param copies 0x1C2FB1 (in FB) and 0x1C334F set `state=[param+0x38]`, immediately overwritten by 3 | — |

No other CPU writer exists in the module (scan covers the XBE's `.text`;
VA > 0x1D0000 is data — small residual risk, noted in open-q (b)).

**Parked call chain** (stack walk of pd546: ret-addr chain
0x1C3927→0x1C21AC→0x1C260D→0x1C2833→0x14981→0x1168C→0x22917→0x251B4C):
game movie/audio function 0x22880 → `call 0x115f0` → sound dispatcher
0x148A0 (index word [0x3AF3EC] → code 7 → 48-byte table entry 0x24BA80
from [0x3AF4A4]) → 0x1C2816 → 0x1C25A2 → 0x1C2173 → **0x1C388F** (mixer
tick: 0x118-byte buffer copy via 0x1C32DE, 0x10/0x108 parameter blocks via
0x1C3235) → 0x1C2FFA (park). Main thread, stack D0031xxx.

**Who must advance the state — full chain decoded** (the §107 pattern:
"waiting for device feedback"):

- **FB = 0x1C37B9**, called only from worker loop 0x1C3B9F when
  `[this+0x6EC] & 8`. FB body: `state=0` (0x1C37FD, this=0xD001C008,
  EDI=0x83924800) → GPRST `0xFE83FFFC`=0, EPRST `0xFE85FFFC`=0 → 0x1C3430 =
  **0x1C6565 (GP DSP program+start**: SGE/GPFADDR setup, GPOF ring regs
  0xFE803024.., spin on VpPioFree `0xFE820010`, GPRST 1..3) + **0x1C6689
  (EP DSP program+start)** + **kernel busy-stall 0x800153A0(0x2710)** =
  cpuid+countdown ≈10.0 ms guest (7,341,175 measured steps; IF=1, does not
  block) → parameter copy 0x1C2FB1 → `state=3` (0x1C381D).
- FB is reached through: `IRQ5 → IDT[0x35]`. The library **patches the IDT
  directly**: descriptor 0x1C98A8 `{handler=0x1C3E5C, ctx=0xD001C008,
  irq=5, 0x15, …, trampoline at +0x18 (mov edi,0x1C98A8; jmp 0x8001AE8D)}`
  built by `[0x1D12A0]=0x800195F0` (copy of template 0x8001AEB6 with
  relocation) and installed by `[0x1D129C]=0x8001965D` (validates the
  default stub `0x8001B0E0 + irq*10`, patches the IDT word-pair — IDTR base
  `0x80035E60`, vector 0x35 at `0x80036008` = `C0 98 08 00 00 8E 1C 00` →
  target 0x001C98C0, present in RAM at park; registered-flag `desc+0x10=1`
  written by 0x800196C7 — all verified by watch: pd536+56,176,666).
- Dispatch: trampoline → kernel 0x8001AE8D → 0x1C3E5C wrapper → **0x1C3E13**
  (reads ISTS `0xFE801000`, acks by write-back, `or [this+0x6E4],ists`;
  bit0x10 → 0x1C3D85; **bits 0x40|0x08 → `[0x1D12B0]=0x8001A3C3` queues
  descA** `{start=0x1C3C69}` — descA's only queue site) → kernel work item →
  worker **0x1C3C69 → 0x1C3B9F** (wait `[0x1D12EC]=0x8001AE30`; drain
  callback 0x1C378D: 0x6E4→0x6EC) → `0x6EC & 8` (**ISTS bit3**) → FB.
- Guest masks: `IEN=0xAFB` enables bits {0,1,3,4,5,6,7,9,11} — **bit3 is
  enabled but never raised**.

**DSP start proven** (logs\p16.log, pd536+60M): `HIT 48776238
EIP=001C6FBA → HIT 48776242 EIP=001C6565 → HIT 48777480 EIP=001C6689` — the
GP and EP DSPs are programmed and started on the main thread during the
DSOUND init at 53,648,776,238; XGrid stores their registers only ("The DSP
cores are not executed" — `McpxApuDevice` class doc, §107 open item).

### Transition timeline (absolute steps; breaks in logs\p4.log/p16.log)

| absolute step | event |
|---|---|
| 53,648,649,202 | `0x12C8D → 0x139C0` sound-config entry (main thread) |
| 53,648,650,786 | 0x1C3E68 DSOUND init: descA/descB `{start 0x1C3C69/0x1C3AD3}`, IRQ5+IRQ6 registrations, ack-all ISTS |
| 53,648,776,238 | **GP+EP DSP start** (0x1C6565/0x1C6689) — registers stored, cores not executed |
| 53,656,176,666 | IDT[0x35] patched to trampoline 0x1C98C0 (0x8001965D hit, desc+0x10→1) |
| 53,656,195,410 | **F0 config**: 0x139C0 → 0x1C1F68 → 0x1C1977 → 0x1C1892 → 0x1C18A4 → 0x1C334F |
| 53,656,195,433 | state=0 (rep stos, transient) |
| 53,656,533,280 | state=3 (0x1C3226, EAX=3) — parameters loaded |
| 53,657,497,263..53,657,528,860 | game sound-manager objects 0x1C2AAA ×6 (0x13445 loop) |
| 53,657,663,267 | 0x22912 `call 0x115f0` (movie/sound start) |
| **53,657,687,344** | **0x1C3922 reached → park** (A submit; stacks: A on D0031xxx) |

Watch `3924800,3924814` over pd536+400M: only three writers ever touch the
state word (0x1C2DAE, 0x1C3370, 0x1C3226); after park onset: **zero
writes**.

### Stimulus analysis

| # | candidate | evidence for | evidence against / status |
|---|---|---|---|
| 1 | **APU ISTS bit3 (DSP-originated interrupt) missing → ISR/worker/FB never run** | FB is the only CPU path that re-opens state; FB gated strictly on ISTS bit3 (`test [0x6EC],8` ← raw ISTS via ISR 0x1C3E13); xemu defines ISTS only {0 GINTSTS,4 FET,5 FEN,6 FEV} (`apu_regs.h:27-31`) and never raises bit3 anywhere (grep of `hw/xbox/mcpx/apu/`); XGrid same (consts IsTsGintsts/FeT/Fen/FeV only). **Temp raise counter (reverted): pd536+120M `raises=0`, pd546+60M `raises=0`** — no APU interrupt source fires at all after 53.6B; APU `reads=8611 writes=35118` frozen through the park. descA never queued (prev/next=0), 0x1C3E13/0x1C3E5C/0x1C98C0/0x1C3B9F/FB = 0 hits in every baseline run | proven as the missing **service-path stimulus**; force-raise (below) proves the whole downstream chain works — but it does **not** open the gate (see #2) |
| 2 | **CPU-side state=0 windows are structurally unobservable; the awaited 0 must come from a non-CPU writer (the GP/EP DSP)** | Exhaustive writer scan (table above): the only post-F0 writers of 0 are FB-start/F0-start, and 0 exists only while that thread owns the CPU (FB's 10 ms is the kernel busy-stall 0x800153A0, does not block). **P15b direct proof**: save-state taken *inside* an FB window (win2.xgs, EIP=0x800153D3 stall) + restore +8M: `state=0` held for ~7.3M steps while A executed **nothing** (0 hits of 0x1C302E/0x1C3048); A resumes only after state=3. Force-raise run: 8 full windows, A entered submit at +65,540,197 and never exited (KTHREAD.Quantum at 0x80035A90 = 70, no mid-window rotation observed). Additional closure of the "unidentified CPU writer" alternative: watch `3924800,3924818` recorded exactly 3 writers over pd536+400M (0x1C2DAE, 0x1C3370, 0x1C3226) and **zero** over pd546+200M; A's non-exit over §114's 2.2B + this wave's probes (now >4B total) means no CPU writer ran for billions of steps | strongest remaining explanation: on hardware the block is freed by DSP-side DMA/writeback (entries in +0x818 point at RAM buffers; DSP started at init) — consistent with "DSP cores are not executed". Alternative (weak): a CPU writer that only runs after a stimulus XGrid also lacks (i.e. inside the missing chain itself), or retail scheduler rotates mid-window (kernel code is identical, so this would have to be a timing/quantum-accounting difference — not observed) |
| 3 | DVD tail / disc read pending (§114 open-q b/d) | LBA region 0x4B4/0x4AA62 read during transition | **ruled out**: IDE BM status=0, `lastCmd=0xA0` with no outstanding packet, all device counters frozen with the park; gate code does no I/O. (Disc files still unidentified — `xgrid xdvdfs` cannot open a CHD directly; deferred, not gate-relevant.) |
| 4 | NV2A flip/vblank notify | — | **ruled out**: frames/stalls frozen at 2017, draws 2673; interrupts keep firing yet the park never exits; gate code touches no NV2A state |
| 5 | AC'97 codec word / IRQ6 | IRQ6 was also client-patched (IDT[0x36]=0x1C9828, registered +48,740,442) and AC'97 traffic starts in the transition | ACI counters freeze with the park; the IRQ6 instance's worker never touches this state block (its code has no add-0x800 sites); not required by the gate decode |
| 6 | XInput/pad stimulus | — | ruled out: no pad polling (USB acc frozen) but the gate is a state-flag spin, not an input wait; no code path from pad to this state |
| 7 | Guest wall-clock/timer pacing | PIT ticks live (Interrupts delivered 96,128→99,307 over 2B in §114) | ruled out: a timed wait would eventually fire; observed stable >3.0B steps across this wave's probes with `LastFault vec=0` |
| 8 | Kernel thread/queue starved by an XGrid gap | descA (the only trigger of FB) is queued **only** by the APU ISR; baseline: never queued. EIPHIST/§114: only parked main + idle runnable | downstream of #1, not an independent gap — scheduler itself switches threads correctly (worker↔main observed in force-raise runs) |
| 9 | SMC/event | — | no SMC stimulus expected post-boot; IRQs live; ruled out for this gate |

### Force-raise experiment (temporary instrumentation, reverted)

Raising ISTS bit3 whenever the guest IEN enables it (first every Advance,
then once per audio frame) produced, from pd536:

- **Chain reversal works**: `HIT +56,180,774 EIP=001C3E13` (caller 0x1C3E65
  ← kernel 0x8001AEAC = trampoline dispatch), `HIT +56,180,951 EIP=001C3B9F`
  (worker, stack D0020FE0), `HIT +56,181,017 EIP=001C37B9` (FB) — i.e.
  IRQ5 → IDT[0x35] → ISR → queue → worker → FB → GP/EP re-program →
  state toggles 0↔3 (watch: 0x1C37FD/0x1C381D alternating, windows
  7,341,175 steps ≈ 10 ms every ≈11.7M steps). `logs\p9-p13.log`.
- **But the gate never opened**: `0x1C3048`=0 hits, `0x1C302E`=0 hits,
  final EIP 0x1C302C, over 8 windows (P12/P13) — and P15b (restore inside
  an open window) shows A executes nothing while state=0. ⇒ necessary but
  not sufficient: the missing bit3 is the service-path stimulus; the
  state=0 that A actually waits for must be produced off-CPU (candidate #2).

### VERDICT

**Best-evidenced partial conclusion — two-part gap, probe-only, no fix
landed:**

1. **Primary missing stimulus (proven): no APU interrupt source fires after
   the transition.** 007's XDK DirectSound mixer submit (0x1C2FFA) spins on
   the shared parameter block at `0x83924800+0x10`; the only CPU path that
   re-opens the state (FB, 0x1C37B9 — a GP/EP DSP restart handshake) is
   driven by ISTS **bit3** via the client IRQ5 ISR, and XGrid (like xemu)
   never raises ISTS bit3 — measured `raises=0` over pd536+120M and
   pd546+60M, all counterpart breaks zero, descA never queued, APU
   read/write counters frozen. Root cause sits on top of the §107 open
   item: **the GP/EP DSP cores are started at init (0x1C6565/0x1C6689 at
   53,648,776,238) but never executed**, so neither their frame/interrupt
   side (ISTS bit3) nor their buffer-consumption side can occur.
2. **Secondary structural finding (proven, refines the fix):** every
   CPU-side state=0 window (FB start, F0 start) exists only while its own
   thread owns the CPU (FB's 10 ms is busy-stall kernel 0x800153A0, does
   not block; no mid-window rotation observed — P15b holds state=0 open
   7.3M steps with A executing nothing; 8/8 windows missed in force-raise
   runs). Therefore A's awaited 0 cannot come from the CPU-side writers —
   **the gate is opened by an off-CPU writer, most consistently the GP/EP
   DSP's DMA/writeback into the block** (the +0x818 ring entries carry
   guest-RAM buffer addresses; the DSPs were started before F0). The exact
   hardware writer was not directly observed (nothing writes the word in
   XGrid), so this leg is strongly-evidenced, not proven.

**Recommended minimal fix for a build wave (decision-first):** before
coding, settle which mechanism frees the block on hardware: (i) decode the
ring/DSP SGE targets (0x1C6565 tables, globals [0x1C9924]/[0x1C998C]
=0x396B000/0x83938000 vs the block at 0x83924000+0x800) to confirm a DSP
writeback; (ii) if instead a CPU writer exists in unscanned space, find it.
Likely fix scope = execute (or faithfully bridge) enough of the GP/EP DSP —
xemu 0.8.136 provides the reference (`hw/xbox/mcpx/apu/dsp/`,
`mcpx_apu_dsp_frame` in apu.c) — plus raising ISTS bit3 from the DSP frame
path (note: xemu itself never raises bit3 either, so neither emulator is an
oracle here; ground truth = hardware or the NVIDIA nForce MCP APU technical
brief / JayFoxRox xbox-tools APU scripts referenced by DevWiki). A
short-term gate-only shortcut that does not fake state: **none known** —
writing the state word from the emulator would violate the no-fake rule
unless tied to a modeled DSP event.

**Acceptance test for the future fix wave (verbatim, baseline artifacts):**

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd536.xgs --steps 200000000 --watch 3924800,3924818 --break-log 0x1C3048 --break-log 0x1C302E --break-log 0x602F7 --break-log 0x20BD0 --frame gate118.bmp
```

PASS = `HIT ... EIP=001C3048` (state=2 written) and `HIT ... EIP=001C302E`
(park exited), EIP leaves `{0x1C3028,0x1C302C}`, and (per §114 open-q c)
the previously-unseen menu entry `0x602F7` and next PlayMPC channel
`0x20BD0` become reachable. Expected secondary effects: APU raise activity
resumes, worker descA queued, USB pad polls return. Also re-run the
force-raise-free baseline: state word must open **without** any emulator
shortcut.

**Decisive next probes (verbatim):**

```
rem (1) ring/DSP target decode vs block — dump block + SGE globals pre-gate:
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd546.xgs --steps 1000000 --dumpv 83924000,100 --dumpv 83924800,80 --dumpv 83934000,40
rem (2) catch ANY writer of the state word between F0 (53,656.5M) and park (53,657.7M) in case an unidentified writer exists:
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 --dvd "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd" --load-state pd536.xgs --steps 60000000 --watch 3924800,3924818 --break-log 0x1C3226 --break-log 0x1C3922 --dumpv 83924800,40
```

If probe (2) shows no 4th writer (expected), close the CPU-writer
hypothesis and take the DSP path.

**Open questions:** (a) exact hardware semantics of ISTS bit3 (undocumented
in xemu/apu_regs.h; candidates: GP/EP frame, DSP host event, back-end) —
needs hardware/nForce-brief ground truth; (b) the exact off-CPU writer of
state=0 (DSP DMA writeback vs an unidentified CPU site in VA>0x1D0000);
(c) §114(c) menu/input and PlayMPC re-entry remain unobserved (gate never
opened); (d) DVD LBAs 0x4B4/0x4AA62 disc files still unidentified (`xdvdfs`
CLI cannot open CHD directly; gate is not I/O-bound so deferred); (e) who
acked pre-pd536 ISTS raises (no absolute-immediate ISTS access exists
outside the 0x1C library — a base+offset kernel path cannot be excluded).

**Artifacts (worktree paths, not committed):** snapshots `pd4915/pd536/
pd546/pd546b.xgs` (copied read-only from `007-streamend`), `win2.xgs`
(force-raise window-open snapshot, requires the reverted instrumentation to
be meaningful), `probe1.ram` (64 MB post-park RAM), logs `logs\probe1.log`,
`logs\p1..p16.log`, `logs\tests.log` (`*.log` ignored; snapshots/BMPs kept
untracked), firmware copies `wt-data\`. **Committed:** this note plus
`scripts/dumpva.py` (VA→phys page-table extractor for --ramdump) and
`scripts/scanstate.py` (state-writer scanner for a module .text).
Status: docs + scripts only; suite **1205**, build + format clean.

## §119 — Idle-skip V2: MMIO/PIO parks measured negligible; deadline-model skip implemented and unit-tested, not wired to devices (2026-10-13)

Branch `wave/idle-skip-v2` (worktree `Temp\opencode\worktrees\idle-skip-v2`).
Closes §111's recorded open risk ("MMIO/PIO parks rejected by design - V2
needs per-device read-effect modeling") by the velocity rule: MEASURE FIRST,
then scope to evidence. Scope: `src/XGrid.Cpu/` (IdleSkip*), CPU tests, one
CLI counter-print hunk, `scripts/idle-skip-v2.ps1` harness. No device was
touched (see verdict below).

**Measurement method (flag-off-neutral).** The detector gained exclusive-time
step attribution, active whenever it exists (probe/skip; flag-off detector is
null -> zero behavior change): every backward branch opens a span on its loop
head; the inter-TSC delta is credited to the open head MINUS steps already
attributed to other tracked heads since its last visit (single-open-span
stack discipline), so nested/sequential loops do not double count (untracked
code between visits stays with the open head - documented approximation).
Heads are classified on their first verdict: MmioRead / Pio (with port) /
MemWrite / WatchOverflow / StaticRule / Structural (128-head cap,
`headsUntracked` counts overflow). Armable = heads that reached a trigger.
Cross-validation: probe `armable` (dashboard 131.9M, triangle 455.6M) tracks
V1 flag-on `skippedSteps` (129.4M / 447.8M, §111) within re-measure overhead -
the attribution is honest.

**Measurement table (`--idle-skip-probe`, 1B steps, fresh private media):**

| class | dashboard | triangle |
|---|---|---|
| armable parks (V1-skippable) | 131,930,736 (13.19%) | 455,608,763 (45.56%) |
| rejected: mem-write | 43,902,293 (4.39%) | 27,230,237 (2.72%) |
| rejected: structural | 10,675,717 (1.07%) | 1,293,423 (0.13%) |
| rejected: static-rule | 2,440,506 (0.24%) | 817,180 (0.08%) |
| rejected: MMIO-read | 353,818 (0.035%) | 353,818 (0.035%) |
| rejected: PIO | 24,189 (0.002%) | 19,211 (0.002%) |
| rejected: watch-overflow | 0 | 0 |

Named device heads (dashboard; triangle shares the boot prefix): the whole
MMIO bucket is **BIOS-ROM fetch loops**, not register polls - heads
`0xFFFFFF5A` (344,055 steps) / `0xFFFFFE0F` / `0xFFFFFF12` / `0xFFFFFEEE`
(reset-vector region `0xFFFxxxxx`; V1 rejects non-RAM instruction fetches).
PIO is the kernel's `out 0xA0` loop at `0x800143BC` (PIC EOI path, 24,107
steps) plus one DX-form head (42 steps). **The §111-hypothesized FLIP_STALL
/ vblank / PIT-counter CPU polls never appear**: flip-stall is a FIFO-side
park (§43, the CPU never spins on it), the 007 waits are udiv/RAM parks
(§§74/92/114), PIT timing rides the IRQ0 path already modeled in V1.
MMIO+PIO = **0.038% of 1B steps** - three orders below the 2% reassessment
bar. The remaining rejected mass is mem-write busy loops (dashboard XBE at
`0x0040xxxx` + kernel at `0x8003158A`) = real work, correctly unskippable.

**Verdict (Velocity rule).** No device deadline hooks are wired: the park
class §111 named is negligible in the measured workloads, so touching
NV2A/PIT/APU for ~0.04% would add risk for no measurable gain. `XboxMachine`
does NOT implement `IIdleSkipReadModel`; flag-on behavior is bit-identical
to V1 (proven: skippedSteps/triggers/stopIrq reproduce §111 exactly, see
below). The generic machinery IS implemented and unit-tested so a future
title with a hot device poll needs only a `TryGetReadEffect` on the device
plus one property assignment.

**Design shipped (scaffolding, §111 theorem extended).** `IIdleSkipReadModel`
+ `IdleSkipReadEffect` (Stable | ChangeIn(steps)): a device may classify a
physical-address read as side-effect free with a value that changes only as
device time advances. Classified reads stay in the candidate body
(`_validatedMmio`); the skip loop clamps every 4096-cycle chunk to the min of
the watched registers' fresh deadlines, so the skip lands EXACTLY at (never
past) the value transition; Stable spans whole skips. Any unclassified
non-RAM read keeps the V1 rejection (conservative default); model loss
mid-skip stops immediately (`stopModelLost`); MMIO/PIO WRITES remain V1 hard
stops (a device write is now attributed MemWrite with its address); IRQ
pending+enabled remains the first hard stop; instruction-fetch from non-RAM
(incl. BIOS ROM) still rejects. New counters (flag-on print only):
`stopReadDeadline stopModelLost invocations triggerDrops hostNull`, step
buckets `armable/mmio/pio/memWrite/watchOverflow/staticRule/structural`,
per-head table + `device head` lines naming MMIO/PIO heads.

**Bug found & fixed on the way (kept as diagnostics).** A flag-on dashboard
run showed `skippedSteps=0` with `triggers>0` and zero stop counters: the
CLI's `--break-log` path drives `Cpu.Step()` directly, bypassing
`XboxCpu.Execute`'s trigger check, so idle-skip can never engage under
break-log. This is exactly §111's "absolute-step recipes are flag-off-only"
rule, now enforced in the harness (break-log only in off mode) and
diagnosable via the new `invocations/triggerDrops/hostNull` counters.

**Acceptance evidence.**
- (1) Flag-off byte-identity: pre-change baseline (dashboard 1B, pad-script
  + break-log 1859D/1E420, fresh wt-data) frame SHA
  `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`,
  HIT 972246032/972349245, draws=33623 frames=685 stalls=685
  Interrupts=3152 LastFault=0 EIP=0x0002EA06 - final build log byte-identical
  (Frame path + wall excluded); suite **1215 green** (1205 baseline + 10 new
  `IdleSkipMmioTests`, zero existing expectations touched);
  `format --verify-no-changes` exit 0.
- (2) Unit tests (`IdleSkipMmioTests`): scheduled-change poll lands EXACTLY
  on its deadline (`Advanced==1000`, `stopReadDeadline=1`, zero overshoot);
  Stable register spans chunks until the RAM poll stops the skip; unknown
  register rejected conservatively; model loss mid-skip stops at the last
  completed chunk; MMIO write in body is a hard stop even when the register
  is classified; IRQ boundary preserved (IRQ stop wins the check);
  attribution records MMIO head/addr, PIO port 0x40, static-rule split, and
  armable steps in probe mode; flag-off identity re-asserted.
- (3) Flag-on milestone parity, 1B, fresh media: **dashboard** no fault,
  frame SHA identical to flag-off (`4EF1FBF6...`), pad-script events at their
  exact steps, `triggers=2203 skippedSteps=129,424,394 stopIrq=203` -
  byte-identical counters to §111's flag-on (V2 changes did not alter skip
  behavior); two independent flag-on boots fully deterministic. **triangle**
  no fault, frame SHA identical to flag-off (`A1740DE0...`), draws/pixels/
  frames/stalls identical, `triggers=7561 skippedSteps=447,784,409` = §111
  exactly.
- (4) Speedup (back-to-back pairs, same load window; machine heavily shared
  with sibling agents so absolutes drift ±40% between windows - ratios are
  the same-window pairs): dashboard 478.3s off -> 435.5s on = **1.10x**
  (12.9% skipped; matches interpreter-cost arithmetic); triangle 310.0s off
  -> 186.4s on = **1.66x** (44.8% skipped). V1's larger dashboard/triangle
  ratios were contention-inflated; today's lighter load shrinks the ratio
  with the same absolute skip share.

**Open risks / follow-ups.**
- The BIOS-ROM fetch loops (344K steps, 0.034%) would become skippable if
  ROM/stable-ROM-fetch classification were modeled; negligible, not done.
- Attribution approximation: untracked code executed between two visits of a
  tracked head is credited to that head (park spans absorb untracked
  dispatcher excursions); buckets are for scope decisions, not billing.
- `--break-log` + `--idle-skip` silently disables skipping (CLI Step path);
  banner documents flag-off-only recipes, counters now make it visible.
- A title with a real hot device poll (e.g. vblank-status spin) still gets
  V1 rejection until its device implements `IIdleSkipReadModel`; the unit
  tests define the expected semantics (exact landing, conservative defaults).
- Snapshot format untouched; detector transient state resets on load (V1
  behavior). Baseline: **1215 tests, build + format clean.**

## §120 — kernel-test-suite unblocked: full 378-test run captured, CMC decoder gap fixed, xemu differential closed (2026-10-13)

Worktree `wave/ktest-logsink` (private media under `wt-data\`; tooling in
`tools/ktest-recon/`). Resumes the cancelled `wave/ktest-logsink` session:
its uncommitted artifacts (working shellcode XBE, partial run logs, CMC
decoder diff) were inspected, verified against the kernel image, and either
landed or reproduced. **PRIMARY ACCEPTANCE MET: the suite executes all 378
tests on XGrid and emits a verifiable `kernel_tests.log` — 53,700,164 bytes,
659,843 lines, 378 `Test completed` entries, `------------------------
End of Tests -----------------------` footer.**

**Root-cause chain (§116's open items, all closed):**

1. **Letter gate — H3 solved; guest-shellcode bug, not an XGrid gap.**
   §116's `IoCreateSymbolicLink` shellcode KeBugChecked at step 33137
   (`0xC0000005`, both arg orders) because it passed UNICODE_STRINGs where
   the Xbox kernel takes OBJECT_STRING = STRING (ANSI) — Cxbx
   `kernel/common/types.h:273` (`typedef STRING OBJECT_STRING`); the
   kernel's path parser/hash/compare (0x80021709 / 0x80020912 /
   0x80022678) walk BYTES, and the wide layout puts `Buffer` at the wrong
   offset -> the kernel dereferences garbage -> AV. The fixed shellcode
   (`tools/ktest-recon/patch_suite_xbe.py build_shellcode`) builds
   ANSI_STRINGs (Length/MaximumLength/Buffer), honors stdcall (kernel
   export IoCreateSymbolicLink is `ret 8` at 0x80015E68), and lives in the
   suite's ignored `load_name_file` donor (VA 0x2E620, called by `main`
   at 0x2EDF1 BEFORE `open_output_file`; thunk slot 0x57F1C = ordinal 67).
   It creates `\??\C:` -> `\Device\Harddisk0\Partition2`, so the
   media-patched log path `C:\kernel_tests.log` (in-place `D:`->`C:` at
   file 0x43599 / VA 0x53599) resolves to the writable FATX C: volume.
   EvoX creates the same letter set at runtime (§116); the kernel side was
   always capable.
2. **Second gate — CMC decoder gap (genuine product fix, landed).** The
   first full-suite run (cancelled session, pre-fix, `wt-data\vfull.*`)
   reached test 282 then KeBugChecked: `LastFault vec=6 (#UD)
   EIP=0x80023276` -> `KeBugCheck` cli/hlt at 0x800151ED at step
   1,104,771,679 with tests 1-281 already in the log. Objdump of the
   kernel image (phys 0x10000 + 0x13276) shows
   `80023276: f5  cmc` inside the 64-bit math chain
   (`rcl esi,1 / sbb edi,edi / cmp esi,ebx / cmc / sbb ebp,ebp / ...`)
   used by RtlExtendedLargeIntegerDivide / Xc big-number code. XGrid's
   decoder did not implement opcode 0xF5 -> UnsupportedInstructionException
   -> #UD -> kernel bugcheck. Fix landed: `Mnemonic.Cmc` decode +
   `EFLAGS ^= CF` executor + unit test `Cmc_ComplementsCarryFlag` (the
   cancelled session's `src/XGrid.Cpu` diff — kept and justified, not
   scaffolding). Post-fix test 282 reports `All tests PASSED`.
3. **Full run (acceptance evidence).** Boot command: `XGrid.Cli.dll boot
   --bios Complex_4627.bin --mcpx mcpx_1.0.bin --eeprom eeprom.bin
   --hdd ktest-hdd.qcow2 --dvd ktest-full.iso --steps 20000000000
   --break 4E03A --writedump ktestfull --ramdump ktestfull-ram.bin`.
   0x4E03A is `main`'s return site (instruction after `call 0x2ede0` in
   the CRT wrapper at 0x4E010; `main` = 0x2ede0 runs shellcode 0x2EDF1 ->
   open 0x2EE76 -> tests -> close -> `Sleep(10000)` -> ret). The run stops
   there after **12,264,291,047 instructions, Halted=False, LastFault
   vec=0** (~58 min at ~3.5M insn/s; ~9.5B of that is the final
   `Sleep(10000)` at guest time — `--break` does not use idle-skip).
   The log is recovered from the HddOverlay sector journal with
   `read_writedump_fatx.py` (root LBA 0x465488, FAT 0x465409+, dirent
   cluster 315 / size 53,700,164): 84 verbose-PASS, 3 verbose-FAIL, 291
   silent stubs, 378 `Test completed`, `End of Tests` footer.

**Failure results + xemu 0.8.136 differential (same media recipe, private
profile `wt-data/xemu2`, log read back from xemu's qcow2 with
`read_fatx_qcow2.py`):**

| test | XGrid | xemu | verdict |
|---|---|---|---|
| 071 IoFileObjectType | FAIL `ERROR(line 72): Expected file_handle = 0x1, Got = 0x0` | FAIL, byte-identical ERROR | **faithful**: test does `CreateFileA("D:\IoFileObjectType", CREATE_ALWAYS)` — D: is the read-only XDVDFS DVD on any boot that lacks a dashboard-remapped D: |
| 224 NtResumeThread | PASS (1.354 s) | FAIL: `return_threadCounter` out-params garbage (`0xD00409E0`, `0x20303030`, `0xC000004A`) at `ERROR(line 110)` | **XGrid ahead**: xemu leaves the out-param unwritten / returns STATUS_THREAD_IS_TERMINATING |
| 231 NtSuspendThread | PASS (1.518 s) | FAIL, same line-110 errors | XGrid ahead (same xemu quirk) |
| 319 RtlWalkFrameChain | FAIL `ERROR(line 132): Expected total = 0x8, Got = 0xA` | FAIL, identical ERROR | **faithful**: frame-chain walk yields 10 vs the suite's expected 8 frames on both emulators |
| 344 XcVerifyPKCS1Signature | FAIL `ERROR(line 250): Expected XcVerifyPKCS1Signature return = 0x1, Got = 0x0` | FAIL, identical (subset `config.txt` run) | **faithful**: Complex_4627's XcVerifyPKCS1Signature returns 0 for the suite's habibi-key PKCS1 vector on both |

Differential method notes: (a) xemu boots the patched suite fine — the §32
animation-wait deadlock does NOT affect this bare title — but per-print
`WriteFile` traffic keeps sampled EIPs in kernel space, so detect runs via
the FATX log, not EIP ranges; (b) after `Sleep(10000)` the kernel relaunches
the XBE forever and each relaunch truncates the log (`CREATE_ALWAYS`) —
XGrid stops deterministically with `--break 4E03A`, xemu via monitor quit
during the sleep window; (c) the suite's config parser needs CRLF — an
LF-only `config.txt` is silently ignored ("No specific tests were
requested"); (d) xemu commits guest FATX writes straight into its qcow2,
so the log is readable offline once xemu exits.

**Media recipe (reproducible):** `patch_suite_xbe.py --in
kernel-test-suite-default.xbe --out default.xbe --log C --target
'\Device\Harddisk0\Partition2'` (XBE sha256
`76E2C52AE6C28F2154CA94C7579166F7373BFAD1C3FEE492AEBAB547B1EB1A1C`,
byte-identical to the cancelled session's proven v6 XBE) + optional
`config.txt` (`disable-video=1`, CRLF; `tests=<subset>` for fast xemu
iterations) -> `XISOSharp -y -m -c <dir> out.iso` -> boot with
`--writedump` (+ `--break 4E03A`). Suite sources were cloned read-only
into `wt-data/src/xbox_kernel_test_suite` (GPL-2; facts only, no code
copied).

**Landed changes:** `src/XGrid.Cpu/{Decoder,Mnemonic,XboxCpu}.cs` CMC
(opcode 0xF5) + `tests/XGrid.Cpu.Tests/InterpreterTests.cs` unit test;
`tools/ktest-recon/`: ANSI rewrite of `patch_suite_xbe.py`, new
`read_fatx_qcow2.py`, new `run_xemu_ktest_full.py`, README refresh.
Baseline **1206 tests green** (1205 + `Cmc_ComplementsCarryFlag`), zero
existing-expectation changes, `dotnet format --verify-no-changes` clean.

**Open / next:** (a) the 3 faithful failures need no XGrid action (071
requires a writable D: — a dashboard-era letter set or FATX-boot layout;
319/344 look like suite expectation gaps against Complex_4627); (b) xemu's
224/231 out-param bugs are oracle caveats for future thread-test
differentials; (c) idle-skip V2 (timer-park fast-forward) would compress
the 9.5B-step `Sleep(10000)` and shrink this acceptance run from ~58 min
to ~10 min; (d) the suite is now a standing cross-emulator regression
asset — a `tests=` subset ISO gives a ~2-min xemu/XGrid smoke run.

## §121 — APU GP/EP DSP bridge: G2 park drained, 007 proceeds past 56.8B (2026-10-14)

Branch `wave/apu-dsp` (worktree `Temp\opencode\worktrees\apu-dsp`, master
@da76d91). Closes the §107 open item per the §118 verdict: the GP/EP DSP
cores are now executed as frame-driven engines, ISTS bit3 raises under a
modeled condition, and 007's G2 park drains. Scope: `src/XGrid.Audio/` +
`tests/XGrid.Audio.Tests/` only. **1229 tests green** (1216 baseline + 13
new), zero existing-expectation changes, `dotnet format
--verify-no-changes` clean.

**Reference facts extracted (no code copied; xemu/QEMU are GPL):**
- xemu `apu_regs.h`: ISTS bits {0 GINTSTS, 4 FET, 5 FEN, 6 FEV}; IEN-gated
  IRQ (`apu.c` update_irq); GPRST/EPRST run bits {0 GPRST, 1 GPDSPRST}.
- xemu `dsp/gp_ep.c` proc_rst_write: clearing either run bit resets the DSP,
  the rising edge of both bootstraps it (scratch DMA of 0x800 words into
  PRAM); `mcpx_apu_dsp_frame` runs GP every EP frame and EP every 8th when
  the run bits are set; EPRST write resets the frame divider. DSP scratch
  and FIFO access go through the GPSADDR/GPFADDR/EPSADDR/EPFADDR SGE tables
  (DSP page n -> host page SGE[n]); xemu never raises ISTS bit3 anywhere,
  so neither emulator is the oracle for bit3 (Playtest-v2 identical).
- Guest-side facts (007 module, decoded live): submitter 0x1C2FFA spins on
  `[block+0x10]` (0 = free); writers are F0-config (3, 0x1C3226), FB-restart
  (0 then 3, 0x1C37FD/0x1C381D — the 0-write is register-form `mov
  [edi+0x10],ebp`, missed by §118's imm-store scan), alloc-zero; ISR
  0x1C3E13 queues worker descA on ISTS 0x40|0x08; worker 0x1C3B9F calls FB
  on accumulated bit3; FB clears bit3 from 0x6E4/0x6EC at its end and stalls
  ~10 ms unconditionally — hence bit3 must be rare (per-block raises would
  mean a 10 ms restart per ~1.5 ms mixer block, unserviceable).

**Decisive probe (this wave):** at the park, GP scratch SGE[0..] maps host
pages 0x3924000+ — the phys alias of the shared block VA 0x83924000 — so
the GP DSP's scratch window covers the state word (DSP scratch addr 0x810
= phys 0x3924810). The DSP microcode is the off-CPU writer (§118 candidate
#2, now structurally confirmed): it absorbs posted blocks via scratch DMA.
GPOF rings are all zero (no FIFO I/O); only EPOF1 is programmed
([0x2000,0x6000) cur 0x3800, undrained — no S/PDIF sink modeled, EP frame
processing left open).

**Design (`McpxApuDevice`, all in-area, no CLI/seam changes):**
- GPRST/EPRST run-state with proc_rst_write semantics; EPRST write resets
  the EP frame divider. Fresh bootstrap is service-eligible.
- Per-frame GP engine: absorb one pending block (state 2/3 -> 0) through
  the scratch mapping, gated on the GPSADDR SGE table actually covering the
  mailbox page (first 16 entries); raise ISTS bit3 as the DSP subunit
  service request on a submission wake, debounced to at most one raise per
  240 frames (~1.28 s) so continuous submissions flow silently. 3-absorbs
  never wake (no FB self-trigger loop). EP engine only advances its divider.
- Snapshot v3 (run-state, divider, wake gap, both counters); v2 loads
  derive run-state from stored GPRST/EPRST so pd546.xgs-class snapshots
  resume a running engine. New counters `DspAbsorbs`/`DspBit3Raises` +
  `GpRunning`/`EpRunning`; key events in the existing APU EventLog (the
  CLI already prints it — no CLI change needed).

**Acceptance evidence (final build, private `wt-data\`, shared CHD):**
- (1) bit3 raises under the real condition: pd546 +200M (§118 command +
  breaks on 0x1C3E13/0x1C3B9F/0x1C37C5): `dsp-absorb 3->0 n=1`, `2->0 n=2`,
  `dsp bit3 raise n=1`, then HIT 1C3E13 (caller 0x1C3E65) -> HIT 1C3B9F
  (stack D0020FE0) -> HIT 1C37C5 (FB), FB's GP/EP stop->start, `dsp-absorb
  3->0 n=3` — exactly one raise and one FB in 200M (no storm), ist back to
  0 (guest acked). (`gate121.log`, worktree.)
- (2) G2 drains, game proceeds: HIT 1C302E +3,022,850 (park exit), HIT
  1C3048 (state=2 posted), HIT 0x20BD0 +3,835,029 (previously-unseen next
  PlayMPC channel), EIP leaves {0x1C3028,0x1C302C}; +2.2B run to 56.8B and
  +1B more to ~57.8B with `LastFault vec=0`: DVD streams a new 3 MB region
  (LBA 0x33182-0x33772, BM 5885->8993), frames 2017->2125, game advances
  through its 0x15BExx buffer-drain ladder (worker countdown 8->0 observed
  live, flag [0x35743C] moving). The ladder is guest-paced content
  consumption (~29M steps/tick), not a stuck gate; no fault anywhere.
- (3) dashboard neutral boot unchanged: recipe
  `--pad-script "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"`,
  1B steps — frame SHA `4EF1FBF6...` (== acceptance oracle) on both final
  and pristine-master builds; full log diff is only 2 added APU EventLog
  lines (`GP DSP start/stop` — the dash starts the DSP, SGE gate stays
  closed, zero absorbs) plus the frame filename. All numeric counters
  byte-identical.
- (4) suite 1229/1229 (13 new `DspBridgeTests`: run-bit edges, absorb/silent
  paths, SGE gating both ways, wake + debounce + re-eligibility, guest ack,
  v3 round-trip, hand-built-v2 derive-and-resume), format clean.

**Open risks:** EP frame processing/FIFO drain needs an S/PDIF sink (EPOF1
sits mid-ring); the 0x810 mailbox offset is an observed XDK-protocol fact,
not a spec register — if a title remaps scratch so no SGE entry covers the
mailbox, absorbs stop (diagnosable: park + `DspAbsorbs` frozen); wake
debounce (240) may over-suppress if a title needs back-to-back restarts
(FB rate is the observable); post-57.8B 007 content beyond the drain
ladder is unprobed (snapshots `pdE1-fwd.xgs` kept untracked in the
worktree as the forward origin).

## §122 — GPU-backend slice 3: 007 FMV + transition goldens, headless fps table, flipEmpty tripwire (2026-10-14)

Wave `wave/gpu-slice3` (worktree `Temp\opencode\worktrees\gpu-slice3`,
branch from master @da76d91). Scope per wave assignment:
`src/XGrid.GpuGl/**`, `tests/XGrid.GpuGl.Tests/**`,
`scripts/gpu-*.ps1`, GPU-backend docs (`gpu-backend-design.md` §6/§8
status). `src/XGrid.Nv2a/` read-only (seam host); no Nv2a edit was
needed — one seam follow-up is REPORTED, not implemented. Host: NVIDIA
GL 4.6.0 driver 610.88, probe ok (real context, same driver as
§113/§117). Media: private `wt-data\` copies (BIOS/MCPX/EEPROM/qcow2),
triangle ISO read-only at `%TEMP%\opencode\triangle-iso\triangle.iso`,
007 CHD read-only on `E:` (`E:\Microsoft Xbox\007 - Agent Under Fire
(USA).chd`); firmware copied to the worktree's own `wt-data\`, never
booted against shared files. Suite baseline **1222 green** (1216 master
+ 6 `GlBackendTimingsTests` from the prior session on this worktree).

### Prior-session handoff (host power loss, state verified intact)

A prior agent session on this worktree was killed before reporting. Its
partial state was audited, not redone: kept (after diff review) the
`GlBackendTimings` struct + tests, the `WALL:` timing lines in
`gpu-slice1/2-golden.ps1`, the timings wiring in `GlPgraphBackend.cs`,
the new `scripts/gpu-slice3-007.ps1` harness, and the copied snapshots
`pd181/pd526/pd536/pd546.xgs`. Audit findings that shaped this wave:
(1) the pre-loss 007 logs (`s3-007*.log`) predate the timings change
(no `timed[` fragment) and their script stdout (WALL/FPS) is lost, so
all four 007 runs were re-run on the final build; (2) the triangle gl
run never completed (`tri-slice3/` held only a 250M partial sw log), so
the full triangle pair was re-run; (3) the menu pair had completed but
without captured stdout, so it was re-run with stdout capture; (4)
`run-slice3-reverify.ps1` (detached driver) never completed (no
`driver.log` artifacts) and does not capture script stdout — the
campaign ran the three harnesses directly with `Tee-Object` stdout
capture instead; that file is left untracked, superseded. Nothing from
the prior session had been committed.

### Snapshot verification (all four LOAD on this build, short runs)

`--load-state <snap> --steps 5000000`, all `LastFault vec=0`,
`Halted=False`:

| snap | load EIP | end EIP (+5M) | matches |
|---|---|---|---|
| `pd181.xgs` (72195794 B) | `0x000218BF` | `0x000138E5` (FMV clock code, live) | §114 chain origin |
| `pd526.xgs` | `0x8001984F` | `0x8002312F` (kernel, live) | §114 endpoint EIP exact |
| `pd536.xgs` | `0x0016AE37` | `0x0016AE47` (transient, live) | §114 transient exact |
| `pd546.xgs` (72208646 B) | `0x001C3028` | `0x001C3028` (still parked) | §114/§118 G2 park exact |

(Caveat learned: `--hdd` must use the same `wt-data\...` backslash
spelling recorded in the snapshot — forward slashes fail load with an
`IDE HDD path mismatch` error. The harnesses already use backslashes.)

### What changed in `src/XGrid.GpuGl` (additive only; no seam changes)

- **`diag[flipEmpty=]` counter** (`GlPgraphBackend.cs`: field +
  increment on the `_fbo == 0` flip path + `DebugSummary` suffix +
  `ResetState` zeroing). This was the one silent flip path: with no
  backend-rendered content yet the backend serves no frame (the
  no-mixing rule — `TryCopyPresentedFrame` returns exclusively
  `Backend.ReadbackPresented` when a backend is attached,
  `Nv2aMmio.cs:4987-4992`), and previously nothing distinguished "39
  empty flips" from "no flips". Now the 007 FMV gap has a counter
  signature (`flipEmpty == flips`, gate-asserted) instead of a missing
  file. Menu/triangle/transition all read `flipEmpty=0`, proving their
  flips served rendered content.
- **Kept as-is** (prior session, reviewed): `GlBackendTimings.cs`
  (draw/clear/readback wall-clock accumulators + `timed[...]` summary
  fragment + 6 unit tests) and its wiring into `Draw`/`Clear`/`FlipCore`.
- **Harnesses:** `WALL:` wall-clock lines in `gpu-slice1/2-golden.ps1`
  (kept); `scripts/gpu-slice3-007.ps1` (new this worktree): fmv +
  transition segments, each sw-vs-gl with structural +
  `BackendUnsupported=0` + probe checks; fmv asserts the
  coherence-gap signature (sw frame only, `flipEmpty == flips`),
  transition asserts the recorded SHA + gl byte-identity +
  `flipEmpty=0`. This wave added those two `flipEmpty` assertions.

### Menu golden re-verified (final build, `GOLDEN: PASS`)

`pwsh scripts/gpu-slice1-golden.ps1 -OutDir menu-slice3`
(stdout `menu-slice3/campaign-stdout.log`):

- Both `LastFault vec=0`; `NV2A draws executed=33632 skipped=1402
  pixels=285171381 textured=278577344 clears=695 blended=211696978
  depth=136 zclip=0 zclipCull=21526 frames=686 stalls=686
  programLoad=12 constLoad=2` — byte-identical lines sw<->gl.
- `GPU backend=gl draws=2760841 clears=695 flips=686
  methods=18125062 programs=11 uploads=13 unsupported=0
  unsup[colorBytes=0 zformat=0 modes=0 cubeVol=0 fbo=0 flip=0 fatal=0]
  diag[fboReallocs=14 fboZetaDrops=7 clipXY=0 flipEmpty=0]
  timed[drawMs=8623.1 clearMs=12.2 readbackMs=1130.8 readbacks=686]
  BackendUnsupported=0`. (`draws=` counts submitted GL triangles, not
  draw calls — same convention as §117.)
- BMP policy: `diff=2602 violations=0 outsideBand=0 maxDelta=1`;
  sw SHA `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`
  = §113 oracle; gl SHA
  `B6116DAE805822BF0D7C984516B9EC18E8D4AA56A148045BB32F0FCC7CFCF33D`
  = §113/§117 record.
- WALL: sw 352.6 s, gl 342.3 s.

### Triangle golden re-verified (final build, `GOLDEN: PASS`)

`pwsh scripts/gpu-slice2-triangle-golden.ps1 -OutDir tri-slice3`
(stdout `tri-slice3/campaign-stdout.log`):

- Both `LastFault vec=0`; `draws=2706 skipped=0 pixels=89142121
  textured=48754488 clears=6455 blended=518 depth=15667336 zclip=0
  zclipCull=21526 frames=68 stalls=34` == recorded anchors
  (`pixels=89142121` = the post-§109 value, §117).
- `GPU backend=gl draws=54492 clears=6455 flips=68 methods=223956
  programs=10 uploads=10 unsupported=0` + all-zero `unsup[` +
  `diag[fboReallocs=14 fboZetaDrops=7 clipXY=0 flipEmpty=0]
  timed[drawMs=722.8 clearMs=10.9 readbackMs=103.5 readbacks=68]`.
- BMP: `diff=27849 violations=0 outsideBand=0 maxDelta=1`; sw SHA
  `A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`
  = oracle; gl SHA
  `7A860ED7378A86E8E63403573D92D0D5B248EA93E66D7A34716947535F34FC2D`
  bit-identical to the §117 record (driver determinism re-confirmed
  across waves).
- WALL: sw 298.4 s, gl 360.6 s.

### 007 slice-3 goldens (final build, `GOLDEN: PASS`)

`pwsh scripts/gpu-slice3-007.ps1 -Segment both -OutDir 007-slice3`
(stdout `007-slice3/campaign-stdout.log`). Absolute-step note (§114):
Progress from `--load-state` is steps-since-load; absolute =
snapshot-base + progress.

**fmv (`pd181.xgs` + 1B = 19.1B absolute, steady intro FMV):**

- Both `LastFault vec=0`, end EIP `0x0002139C` both; `NV2A draws
  executed=2672 skipped=0 pixels=73474921 textured=33087288 clears=61
  blended=518 depth=136 zclip=0 zclipCull=21526 frames=730 stalls=730`
  — byte-identical lines sw<->gl. (`draws=2672`/`frames=730` include the
  snapshot history — pd181 saved at draws=2672/frames=691 per §114; see
  the counter-semantics finding below.)
- `GPU backend=gl draws=0 clears=0 flips=39 methods=3471 programs=0
  uploads=0 unsupported=0` + all-zero `unsup[` +
  `diag[fboReallocs=0 fboZetaDrops=0 clipXY=0 flipEmpty=39]
  timed[drawMs=0.0 clearMs=0.0 readbackMs=0.0 readbacks=0]
  BackendUnsupported=0`.
- Gap signature holds, now counter-precise: sw saved
  `007-fmv-sw.bmp` (SHA
  `458356268B09CDD290D2C591D9DBDF2EEAD7C2048D81DADB19EFC750E383BABD`
  — **byte-identical to the pre-power-loss run's `s3-007-sw.bmp`**,
  i.e. the FMV software frame reproduces exactly across builds);
  the gl run reports `Frame unavailable (...)` and saves nothing;
  `flipEmpty=39 == flips=39` — every post-restore present was a
  CPU-blitted surface with zero PGRAPH draws/clears behind it
  (surface `0x9BC000`, nonzero=307200, real video imagery).
- WALL: sw 249.5 s, gl 202.4 s.

**transition (`pd536.xgs` + 1B = 54.6B absolute, stream-end screen
transition into the G2 park):**

- Both `LastFault vec=0`, end EIP `0x001C302C` both (the §118 park —
  the segment runs *into* the gate); `NV2A draws executed=2673
  skipped=0 pixels=73782121 textured=33394488 clears=66 blended=518
  depth=136 zclip=0 zclipCull=21526 frames=2017 stalls=2017
  programLoad=12 constLoad=2` — byte-identical lines sw<->gl.
- `GPU backend=gl draws=1 clears=3 flips=1 methods=854 programs=1
  uploads=1 unsupported=0` + all-zero `unsup[` +
  `diag[fboReallocs=0 fboZetaDrops=0 clipXY=0 flipEmpty=0]
  timed[drawMs=16.2 clearMs=3.9 readbackMs=2.4 readbacks=1]
  BackendUnsupported=0`. The guest's whole transition is 1 fullscreen
  draw + clears onto the new surface `0x3BD8000` (§114) — the backend
  renders it and serves the flip.
- sw SHA == recorded oracle `18C7CC83CEA392DEA278E6449F2827
  66158616FE8AE0D62337056DDC5308F0` (the §114 black held frame);
  **gl frame byte-identical to sw** (stronger than the §3.4 policy);
  `flipEmpty=0`.
- WALL: sw 227.0 s, gl 198.8 s.

### Counter-semantics finding (resolves the flips=39-vs-730 question)

A reader of the fmv gl line will ask why `flips=39` when
`frames=730`. Answer, verified in `Nv2aMmio.cs`: `Backend.ResetState`
fires on snapshot load (line 1589) and zeroes the backend counters,
while the software counters (`FramesPresented`, draws, stalls) are
*restored from the snapshot* and keep their history. So backend
counters = post-restore only (39 flips / 0 draws since pd181),
software counters = cumulative (730 frames incl. the 691 at save).
Same for transition: `flips=1` post-restore vs `frames=2017`
cumulative (2016 at save). The harness compares sw<->gl *within* the
same segment, so both sides share the same snapshot history —
structural equality is still exact. This also explains why the
transition's single post-restore draw (2673-2672=1) is the §114
screen-transition draw. No action needed; recorded here so the next
wave does not re-diagnose it.

### Tripwire disposition: 007 trips zero `unsup[` rows

| §113 row / risk | fmv (pd181+1B) | transition (pd536+1B) |
|---|---|---|
| `unsup[colorBytes/zformat/modes/cubeVol/fbo/flip/fatal]` | 0/0/0/0/0/0/0 | 0/0/0/0/0/0/0 |
| `diag[fboReallocs/fboZetaDrops/clipXY]` | 0/0/0 | 0/0/0 |
| `diag[flipEmpty]` (new) | **39 (== flips: all CPU-blitted)** | 0 (flip served GL content) |
| methods observed | 3471 (zero draw-triggering) | 854 (1 draw + 3 clears) |

The 007 segments add no new tripwire exposure: the FMV path is pure
CPU blits (no PGRAPH methods that draw), the transition is one
fullscreen draw + clears. The only backend gap on this title is the
present-upload gap below — deliberately NOT counted in
`BackendUnsupported` (the backend serves nothing rather than wrong
pixels), now measured by `flipEmpty`.

### fps table, headless (milestone 2 — real numbers)

WALL = whole-run wall per harness (`dotnet` CLI startup included,
≈ seconds). Msteps/s = 1B steps / WALL. GL share = `timed[` total /
gl WALL. Post-restore flips = backend `flips=` (the presents actually
served during the segment; software `frames=` includes snapshot
history — see above). Host was shared with sibling waves, so absolutes
carry contention noise (§119 documents ±40% windows); the `timed[`
counters bound the backend's own contribution.

| segment | sw WALL | gl WALL | Msteps/s sw/gl | post-restore flips | timed draw/clear/readback (gl) | GL share of gl WALL |
|---|---|---|---|---|---|---|
| menu 1B | 352.6 s | 342.3 s | 2.84 / 2.92 | 686 | 8623 / 12 / 1131 ms | 2.9% |
| triangle 1B | 298.4 s | 360.6 s | 3.35 / 2.77 | 68 | 723 / 11 / 104 ms | 0.2% |
| 007 fmv 1B | 249.5 s | 202.4 s | 4.01 / 4.94 | 39 | 0 / 0 / 0 ms | 0% |
| 007 transition 1B | 227.0 s | 198.8 s | 4.41 / 5.03 | 1 | 16 / 4 / 2 ms | ~0% |

(gl-faster-than-sw on the 007 segments is contention noise, not a
backend speedup: `timed[` proves the backend adds ≤ seconds while the
sw/gl deltas are tens of seconds, and the direction flips between
titles.)

**What this decides for the swapchain wave:** the GL submission +
readback path costs 0–2.9% of headless wall on all four workloads —
**emulation (CPU interpretation + devices) is 97–100% of every run**.
A swapchain present path therefore buys ~nothing for throughput; it is
a latency/interactivity feature, not a speed feature. The priorities it
sets: (1) Track C JIT remains the throughput lever (this host measured
2.8–5.0 Msteps/s vs the 14 Msteps/s Release figure — contended, but
the ratio is all emulation); (2) for parity on CPU-blitted titles the
need is a *present-upload* path (below), not a faster present; (3)
async GL submission only makes sense after (1), per Amdahl —
serializing at `0x100`/`0x12C`/`0x1D70` (§7 risk 3) for a ≤3% path
would add risk for no measurable gain.

(Definition honesty: "flips/s" as printed by the harness =
cumulative `frames=` / WALL, which mixes snapshot history with segment
time — e.g. transition prints ~10/s while only 1 flip happened
post-restore. The table above uses post-restore flips instead. The
§114 "~22 fps" figure is guest-side video cadence; host-wall presents
during FMV measured ≈ 39 flips / ~200 s ≈ 0.2/s — the guest is
flip-paced by emulation throughput, not running at video rate in wall
time. No contradiction once the definitions are separated.)

### Reported follow-up (needs an Nv2a seam change — NOT done here)

**Present-upload at flip for CPU-blitted surfaces.** The FMV gap has a
structural cause, not a tuning cause: `PgraphSurfaceRecord` carries
only (width, height, colorBytes, frame) — no surface DMA base/offset —
so `FlipCore` cannot upload a CPU-written present surface into the FBO
when the FBO is empty (or stale after CPU writes over GPU-rendered
content). The scoped follow-up: extend the seam record with the
resolved surface address the software path already computes
(`CapturePresentedFrame`: `SurfaceBase(_dmaColor) +
PresentedColorOffset`, `Nv2aMmio.cs:4972`), and in `FlipCore`, when
`_fbo == 0` (or on a surface-generation change), upload the guest-RAM
surface via `IGpuMemory` and serve it — a full-frame replace, still
never a mix, so the no-mixing rule survives. Staleness detection for
the non-empty-FBO case needs guest-visible surface-access tracking
(design §7 risk 6) and stays later scope; the empty-FBO upload alone
closes the FMV golden (gl frame would become byte-exact, since it is a
copy of the same guest RAM the sw path reads). This is Nv2a-seam work
for a follow-up wave in the assigned area split — recorded, not
started.

### Acceptance

(1) 007-segment goldens: transition passes the full §3.4 golden
(byte-identical, SHA == §114 oracle, `BackendUnsupported=0`,
`flipEmpty=0`); fmv meets the documented-gap clause (structural
equality, `BackendUnsupported=0`, `flipEmpty == flips` with the sw
frame reproduced exactly across builds) — each remaining row justified
with counter evidence (table above). Menu + triangle re-verified green
on the final build, SHAs bit-identical to §113/§117 records.
(2) fps table above with real numbers + swapchain-wave priorities.
(3) Suite **1222/1222 green** (1216 baseline + 6 prior-session timing
tests; zero existing-expectation changes) + `dotnet format
--verify-no-changes` clean. Commit on `wave/gpu-slice3`.

Open risks (unchanged unless noted): clipY!=0 (`clipXY=0` on all four
workloads this wave), fingerprint interior-miss (gate-armed; FMV sw
frame determinism across builds raises confidence in the software
side), zeta-drop depth (0 exposure), Vulkan/swapchain/async (design
§3/§5/§7 unchanged — fps table reframes swapchain as
latency-not-throughput), 007 G2 decode (§118, sibling track — the
transition segment now ends parked at `0x1C302C`, consistent).

## §124 — NV2A remaining texture modes: measured, implemented, closed (2026-10-14)

Worktree `Temp\opencode\worktrees\nv2a-remaining` (`wave/nv2a-remaining`;
this agent resumed a session killed by host power loss — its partial state
was verified intact: the `Nv2aMmio.cs` + `Nv2aExoticTexModesTests.cs`
diffs were reviewed line-by-line against the references below (not
redone), `Nv2aRemainingModesTests.cs` (13 tests) kept, and the
`meas-*`/`post-*` artifacts re-validated rather than re-run). Scope was
strictly `src/XGrid.Nv2a/` + `tests/XGrid.Nv2a.Tests/`; the GpuGl seam was
not touched (see below).

**Measurement (mode x stream counts — the table that motivates every
change).** Histograms over the captured method streams
(`meas-menu.pgraph`, 18,125,062 methods, 1B-step dashboard boot with the
§87 pad recipe; `meas-tri.pgraph`, 223,956 methods, triangle ISO boot):

| item | menu | tri | verdict |
|---|---|---|---|
| texture formats written | s0: 04 x8140, 05 x652, 06 x82, 12 x686; s1/s2: 07 x36; s3: **30 x1626** | s0: 06 x82, 12 x34; s1/s2: 07 x36; s3: **30 x1626** | 0x30 done; DXT (0C/0E/0F) x0 both -> left |
| depth formats 2C/2E/2F/31 | x0 | x0 | left (set completed for reference-completeness) |
| combiner stage modes (0x1E70) | s0: 01 x8222; s1/s2: 03 x104; s3: 02 x36; rest 00 | s0: 01 x81; s1/s2: 03 x104; s3: 02 x36; rest 00 | PROJECT2D/3D + cubemap only |
| DOT_STR_3D (0x0D) / BRDF (0x08) / all other dot modes | x0 | x0 | left |
| texture dimensionality != 2D | x0 (all dim field 0) | x0 | DOT_STR_3D-dim3 doubly unexercised -> left |
| TEXADDRESS ADDR words | s0: 010303 x9477 (U/V clamp); s1-3: 010101 x8793 (wrap); BORDER words 030303 x81, 030404 x80, 010404 x35 | same words, smaller counts | V-decode + BORDER done |
| BORDER_COLOR (0x1B24+) writes | FF000000 x34 (s0), FFFFFFFF x6 (s3), zeros | same | done |
| SHADOW_DEPTH_FUNC (0x1E6C) | {0: 2} (reset default only) | {0: 2} | NEVER/ALWAYS done; compare funcs 1-6 never programmed -> left as miss accounting |
| TEXFILTER MIN/MAG | only 01012000 (BOX_LOD0/NEAREST) and 02022000 (TENT_LOD0/LINEAR) | same pair | **mipmap LOD provably unexercised -> left** |
| TEXFMT MIPMAP_LEVELS = 8 | s0 color 05 (SZ_R5G6B5) x652 | x0 | still samples level 0 (LOD0-only filters) -> left |
| TEXADDRESS WRAP_U/V/P/Q bits (4/12/20/24) | x0 of 36,471 ADDR writes | x0 | ignored, matching xemu (see below) |
| dot-map (0x1E74) values | {0: 37} | {0: 37} | nothing to do |

**Implemented (all exercised above; reference citations are facts,
no code copied).**
- PROJECT2D non-cube projective divide (`ResolveTexel`): `SampleInto(u/w,
  v/w)` — xemu `psh.c:1129-1132` `textureProj(texSamp, norm(pT.xyw))`
  (rect-tex `norm` = /size is already inside the linear texel-unit path,
  so the extra divide composes exactly). Zero-w keeps undivided coords.
- TEXADDRESS V decode moved to bits 8-10 (ADDRV `0x700`) with U 0-2 / P
  16-18 (`nv2a_regs.h:528-539`; values 1 wrap / 2 mirror / 3 edge /
  4 border / 5 ~= edge). Mode map verified against the GL backend table
  (`gl/constants.h:48-55`: 1 REPEAT, 2 MIRRORED, 3 EDGE, 4 BORDER,
  5 ~= EDGE); mode 0 (reset) wraps (= GL default REPEAT), reserved 6-7
  clamp (xemu would index its 6-entry map out of bounds — safe choice).
  WRAP_U/V/P bits are unconsumed by xemu's GL path (`gl/texture.c:124-126`
  uses only ADDRU/V/P masks) and absent from both streams. This closes the
  §96-noted V-bit quirk in both the 2D and volume samplers.
- BORDER: out-of-range coord on a BORDER axis returns
  `NV097_SET_TEXTURE_BORDER_COLOR` (`0x1B24` + slot stride `0x40`;
  `nv2a_regs.h:1264`, `pgraph.c:2614`), ARGB-packed per
  `pgraph_argb_pack32_to_rgba_float` (`pgraph.h:409-415`), per-axis
  independence matching xemu's `needs_border_color` (`gl/texture.c:166-183`).
- 0x30 depth semantics (`TryResolveShadow`, PROJECT2D/PROJECT3D stages,
  shadow checked before sampling exactly as `psh.c:1113-1116,1146-1148`):
  NEVER -> `vec4(0.0)`, ALWAYS -> `vec4(1.0)` (`psh.c:706-716`); funcs 1-6
  keep the format-miss accounting (open). Depth-format set
  {2C,2E,2F,30,31} verified (`nv2a_regs.h:1222-1226`,
  `texture.c:62-69` f.depth table). Method `0x1E6C` verified
  (`nv2a_regs.h:1293`); reset NEVER matches xemu's zeroed SHADOWCTL
  (`pgraph.c:2956-2958`, `nv2a.c:301` memset).
- Snapshot v2 -> v3 (new `_textureBorderColor[4]` + `_shadowDepthFunc`;
  both draw-affecting, so loud version rejection beats silent resume
  corruption; §106 precedent). Cost: pre-existing v2 `.xgs` files
  (pd181/pd4915/pd546) no longer load — regenerate.

**Explicitly left with counter evidence (closing the measurement is the
outcome).** Mipmap LOD: MIN is always BOX_LOD0 (-> GL_NEAREST) or
TENT_LOD0 (-> GL_LINEAR) (`gl/constants.h:29-38`), so the 652
levels=8 programs still sample level 0; XGrid's base-level-only sampler is
exact here. DOT_STR_3D dim==3: mode 0x0D x0 AND dim 3 x0 (keeps the §96
2D-sample non-goal). BRDF 0x08: x0 (xemu-faithful zero stub already in
place, §96). DXT 0C/0E/0F: x0 both streams — stays deprioritized per §105.
Shadow compare funcs 1-6: zero `0x1E6C` writes beyond reset.

**Live validation (post-fix `post-*` runs, artifacts in worktree root,
NOT committed — 600 MB+ of pgraph).** `post-menu.bmp` SHA
`4EF1FBF6...` (= §113 golden) and `post-tri.bmp` SHA `A1740DE0...`
(= slice-2 golden) — byte-identical files, so RGB and alpha diffs are both
exactly 0. All non-texture counters identical (menu: draws 33632,
blended 211696978, depth 136, frames/stalls 686; tri: draws 2706,
pixels 89142121, frames/stalls 68). Deltas confined to diagnostics:
`formats= 30=62444852/62444852` -> gone (NEVER/ALWAYS resolve without miss
accounting), per-state `miss=` -> 0. The 62.4M-sample `(0,0,0,1)` ->
`(0,0,0,0)` alpha change is pixel-invisible because the stage-3 0x30
texels feed decorative PROJECT3D stages whose alpha is not consumed
downstream (blend/depth/clear counters all identical).

**Tests: 1229 green (1216 + 13 new), zero existing-expectation changes,
format clean.** New `Nv2aRemainingModesTests.cs` (13): address bit-layout
helpers, ARGB border decode, depth-format set, PROJECT2D w-divide (+ w=1
guard), U-wrap / V-clamp reference-bit draws, border in/out-of-bounds,
shadow NEVER/ALWAYS/compare + snapshot round-trip. Fail-pre-fix proven by
stashing the `Nv2aMmio.cs` fix and running the 9 compilable behavior tests
on baseline: exactly the 6 predicted tests fail
(Project2DDivides, AddressUWrap, AddressVClamp, BorderAxisReturns,
ShadowNever, ShadowAlways); the 3 guard/characterization tests pass both
(build fails for the 3 helper-only tests without the new APIs, as
expected). One existing-test setup touch: `Nv2aExoticTexModesTests`
`Draw_Project3DSamplesVolumeTexel` input word `0x00030033` -> `0x00030303`
— assertions unchanged; the old word only clamped under the buggy bits-4-7
V decode, the new word is the reference ADDRU/V/P layout. Full suite
`dotnet test XGrid.sln -c Release` 1229/1229; `dotnet format
--verify-no-changes` clean; warnings-as-errors held.

**Seam note for the GpuGl sibling:** `PgraphTextureSlot` was deliberately
NOT extended (brief: feed through existing visible state). Raw
format/address words already cross the seam; the new public static decoders
(`DecodeTextureAddressU/V/P`, `ToWrapMode`, `DecodeBorderColor`,
`IsDepthTextureFormat`) are available for the GL backend to interpret
them. Border-color values, filter words and shadow func are not yet in the
seam structs — extend when a GL slice needs them.

**Open risks:** (a) 007's texture census is unmeasured (50B+ steps; menu +
tri only) — its later stages may exercise DXT/mipmaps/compare-shadows;
(b) v2 snapshot invalidation (see above); (c) reserved address modes 6-7
clamp fallback is a safe choice, not a reference behavior (xemu asserts).

## §123 — ktest smoke scripted: 7-test subset green on both emulators in 4:40 (2026-10-14)

Worktree `wave/ktest-smoke` (branch from master @da76d91). Turns the §120
one-off into `scripts/ktest-smoke.ps1`: subset ISO -> XGrid leg -> xemu leg
-> `ktest_diff.py` verdict vs the §120 known deltas. **Acceptance run on
this machine: `KTEST-SMOKE: PASS`, 7/7 rows green, total wall 04:40**
(XGrid 04:19, xemu 00:14, setup/diff the rest). **Second full cycle
re-run by the follow-up session: `KTEST-SMOKE: PASS`, 7/7 green, total
wall 05:02** (XGrid 04:41, xemu 00:14) with a bit-identical XGrid leg —
`Break: EIP=0x0004E03A after 792800535 instruction(s)`, log 40797 B at
dirent cluster 315; xemu `reason=parked samples=14 saw_suite=3`, log
43213 B at cluster 315. Step count + log sizes reproduce exactly; wall
varies ±20 s with host load (sibling agents). Zero product-source changes
(tools + script + docs only); suite baseline 1216 green, format clean.

**Subset (the full known-delta table in 7 tests):**
`tests=51,71,224,231,282,319,344` with `disable-video=1` (CRLF `config.txt`,
50 B) — 051 silent-stub pass, 071/319/344 faithful fails, 224/231
XGrid-ahead pair, 282 the former CMC-`#UD` site (§120 fix verification).
`disable-video=1` skips the 10 s `Sleep(10000)`, so XGrid stops at main's
return instead of emulating 9.5B sleep steps (the §120 58-min cost).

**Completion-park mechanism (new `--park` flag on `patch_suite_xbe.py`).**
The parked XBE (sha256
`A76DEC38E9E483F29FB3BC60F36B5AC43938E61B05F3C932394E1BDF8A3E2E09`
vs §120's unparked `76E2C52A...`) overwrites 2 bytes at VA `0x4E03A` (main's
return site) with `EB FE` (jmp $). The suite spins there AFTER
`close_output_file`, so the kernel never relaunches the XBE and the FATX
log is never truncated via CREATE_ALWAYS — completion is detectable on both
emulators without a relaunch race (quantified on xemu: relaunch would follow
within ~9.44M guest steps, far below any safe poll-then-quit window).
XGrid keeps `--break 4E03A` (fires identically on the loop); xemu polls
`info registers` for EIP==`0x4E03A` x settle=3 (one kernel-IRQ sample at
`0x80014F2D` mid-park proved the settle counter necessary and sufficient).

**xemu-0.8.136 automation quirks diagnosed (both new, both worked around in
`run_xemu_ktest_smoke.py`):** (1) xemu.toml `flashrom_path` is NOT bridged
into the QEMU launch (params omit `-bios` -> `Failed to load BIOS '(null)'`,
guest frozen at reset vector `0xFFF0` indefinitely; xbox.c reads BIOS from
`ms->firmware`, i.e. QEMU `-bios`) — the runner passes `-bios` explicitly;
the toml row is kept for fixed builds. A stale `flashrom_path` in
`%APPDATA%\xemu\xemu\xemu.toml` (D:\-era paths) was found and left
untouched. (2) xemu starts `paused (prelaunch)` under `-monitor` automation
(`info status` proves it; `info registers` reads `FFF0` forever) — the
runner sends `cont` after connecting. (3) Process hygiene: killing xemu
loses cached qcow2 L2 metadata -> torn FATX tail (`OK(l` + NULs, log ends
mid-231); the runner quits gracefully via monitor `quit`, then the log is
read back with `read_fatx_qcow2.py`.

**Acceptance evidence (verbatim):**

```
pwsh -File scripts/ktest-smoke.ps1 -FirmwareDir "C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator\data\xemu-0.8.136-windows-x86_64" -SuiteXbe "C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator\data\test-samples\kernel-test-suite-default.xbe"
```

- XGrid: `Break: EIP=0x0004E03A after 792800535 instruction(s)`,
  `Halted=False LastFault vec=0`; log 40797 B (dirent cluster 315) with
  `End of Tests`.
- xemu: `reason=parked samples=14 saw_suite=3` (park at t=11.1 s, one IRQ
  excursion at 12.1 s absorbed by settle); log 43213 B (cluster 315) with
  `End of Tests`.
- Verdict table (`ktest_diff.py`, requires `End of Tests` + exact §120
  ERROR substrings on faithful rows + `line 110` on xemu 224/231):

| test | XGrid | xemu | expect | verdict |
|---|---|---|---|---|
| 051 InterlockedCompareExchange | PASS | PASS | PASS/PASS | green |
| 071 IoFileObjectType | FAIL (`file_handle = 0x1, Got = 0x0`) | FAIL (identical) | FAIL/FAIL | green |
| 224 NtResumeThread | PASS (All tests PASSED) | FAIL (`line 110`, out-param `0x800205BA`) | PASS/FAIL | green |
| 231 NtSuspendThread | PASS | FAIL (`line 110`, same quirk) | PASS/FAIL | green |
| 282 RtlExtendedLargeIntegerDivide | PASS | PASS | PASS/PASS | green |
| 319 RtlWalkFrameChain | FAIL (`total = 0x8, Got = 0xA`) | FAIL (identical) | FAIL/FAIL | green |
| 344 XcVerifyPKCS1Signature | FAIL (`return = 0x1, Got = 0x0`) | FAIL (identical) | FAIL/FAIL | green |

Pass counts XGrid 4/7, xemu 2/7 — exactly the §120 deltas, no unexpected
failures. xemu 224's garbage values vary per run (`0x800205BA` here vs
`0xD00409E0`-family in §120); the diff keys on `line 110` + FAIL, robust to
that churn.

**Runtime note.** 04:40 total on a machine shared with sibling agents (XGrid
792.8M steps in 04:19 ≈ 3M insn/s under load; quiet-machine arithmetic from
§119's 14M insn/s predicts ~2:10 total). The step count to the park
(792,800,535) is deterministic; wall varies with host load. Runtime
dominated by the XGrid boot + the two 1.3–1.5 s-guest thread tests
(224/231); the xemu leg is ~15 s + a 1.5 GB HDD refresh.

**Landed:** `scripts/ktest-smoke.ps1`; `tools/ktest-recon/`
`run_xemu_ktest_smoke.py` + `ktest_diff.py` + `--park` on
`patch_suite_xbe.py`; README Smoke section; this note. Media layout
(gitignored): `wt-data/ktest-smoke/{xgrid,xemu}/` — private firmware/HDD
copies (XGrid's HDD stays pristine: `Qcow2DiskImage` opens `FileAccess.Read`
+ `HddOverlay` journal; xemu's copy is refreshed from the template every
run), subset ISO, `xemu.toml` (bare filenames, colocated `xemu.exe`).

**Open risks:** (a) reproducibility across hosts: two full cycles on this
machine now agree exactly (step count + log sizes bit-identical, verdict
PASS both); cross-host confirmation still open but cheap (~5 min) for any
reviewer with the firmware + suite XBE; (b) xemu's unbridged `flashrom_path` + prelaunch-pause look like
upstream automation gaps worth one issue if the team leans on scripted xemu;
(c) the subset pins 7 tests — extend only by editing the script's `-Subset`
default + `ktest_diff.py` expectations together (they are intentionally
duplicated, not shared, so the diff stays an independent oracle).

## §125 - present-upload plumbing: PgraphSurfaceRecord carries the surface guest address (2026-10-14)

Wave-9 T3 build task (branch `wave/present-upload`, commit `5311181`,
merged `--no-ff` as `aabf85a`): the §122 Nv2a-seam follow-up. Plumbing
only — zero behavior change (no new GL calls, no upload,
`DebugSummary` untouched). 4 files, +91/-7:
`IPgraphBackend.cs` (`PgraphSurfaceRecord` gains `uint SurfaceAddress`
as 5th ctor param — the resolved guest-physical byte address of the
presented surface; named per CA1720); `Nv2aMmio.cs` flip site
(`MethodFlipIncrementWrite`) populates it with
`SurfaceBase(_dmaColor) + PresentedColorOffset`, the exact expression
`CapturePresentedFrame` already computes — reference check xemu
`gl/surface.c:1060` (`vram_addr = dma.address + surface->offset`),
facts only; `GlPgraphBackend.cs` `FlipCore` stores it into observable
`LastFlipSurfaceAddress` (zeroed in `ResetState`) BEFORE every early
return, so the `_fbo == 0` CPU-blitted path the future upload serves
carries it too, never acted on. Tests: `Flip_CarriesResolvedSurfaceAddress`
(DMA handle 9 → base `0x8000` + offset `0x1000` ⇒ `0x9000`) and
`Flip_SurfaceAddressTracksOffsetAcrossFlips` (`0x3000`/`0x3040` per-flip
recompute); plus a 2-line fix to the file-local `PushMethods` helper,
whose terminator jumped to hardcoded `0x1001` while the stream lives at
`0x8000` (minimal streams re-executed ~559× to the 4M cap; old tests
survived only via accidental suspension — probe-verified err 4/err 6).
Suite 1250/1250 green post-merge, format clean. Menu golden on branch
(`gpu-slice1-golden.ps1`, 1B): sw SHA `4EF1FBF6…CB0DE81` exact,
counters identical, gl `unsupported=0`/`flipEmpty=0`/BMP exact.
Deferred: the actual upload slice hooks at `GlPgraphBackend.FlipCore`
(`GlPgraphBackend.cs:569`) — on `_fbo == 0` (later: surface-generation
change) read guest RAM at the address via `IGpuMemory` (same walk as
`TryCopyPresentedFrame`), full-frame replace, never mix. Still open:
staleness detection for the non-empty-FBO case (needs guest-visible
surface-access tracking, design §7 risk 6).

## §126 - Track C JIT scoping: lever quantification, hotspot-profile proposal, candidate first slice (2026-10-14)

Read-only wave (no `src/`/`tests/` changes). Question from §122: GL submission+readback is 0–2.9% of wall, so emulation is 97–100% of every run — scope the CPU lever. All throughput figures below are measured values from the notes; anything unmeasured is labeled ESTIMATE.

### 1. Lever quantification (measured)

**Interpreter rate, quiet host.** AGENTS.md Velocity-5: 1B steps ≈ 70–90 s at ~14M insn/s. ImplementationPlan Gap 3: 5B-step batch ≈ 6 min at ~14M insn/s Release. §111: interpreted cost ≈ 70 ns/instruction vs skipped chunks at ~ns/cycle (device remainder math only).

**Same runs, contended host (shared with sibling agents — ratios valid, absolutes drift ±40%, §119).** §122 fps table, 1B-step segments: menu sw 352.6 s (2.84 Msteps/s) / gl 342.3 s (2.92); triangle sw 298.4 s (3.35) / gl 360.6 s (2.77); 007 fmv sw 249.5 s (4.01) / gl 202.4 s (4.94); 007 transition sw 227.0 s (4.41) / gl 198.8 s (5.03). GL `timed[` totals: menu 9.8 s / 342 s = 2.9%; triangle 0.2%; both 007 segments ~0%. §122 verdict: emulation (CPU + devices) is 97–100% of every run.

**Long-probe rates.** §112: 13.1B cold run 4347.7 s (~3.0M insn/s contended). §114: 3.45B batches 699–806 s (~4.3–4.9M insn/s contended). §120: ktest full run 12.26B insn ≈ 58 min (~3.5M insn/s), of which ~9.5B is the final `Sleep(10000)` — idle-skip V2 would compress it to ~10 min (§120 open item c). §123: 792.8M steps in 04:19 ≈ 3M insn/s loaded; quiet-host arithmetic predicts ~2:10.

**How much is parks vs real work (§§111/119, `--idle-skip-probe`, 1B steps).** Armable (V1-skippable) parks: dashboard 131.9M (13.2%), triangle 455.6M (45.6%) — cross-validated against flag-on `skippedSteps` (129.4M/447.8M). Rejected mass: mem-write busy loops 43.9M/27.2M (real work — dashboard XBE `0x0040xxxx`, kernel `0x8003158A`), structural 10.7M/1.3M, static-rule 2.4M/0.8M. **MMIO-read + PIO parks = 0.038% both titles** (MMIO bucket is BIOS-ROM fetch loops `0xFFFxxxxx`, PIO is kernel `out 0xA0` PIC-EOI path) — the flip-stall/vblank/PIT CPU polls hypothesized in §111 never appear. Flag-on speedups (same-window pairs): dashboard 1.10× (12.9% skipped), triangle 1.66× (44.8% skipped); V1's 1.63×/1.90× were contention-inflated (§119).

**Interpreter-vs-device split: NOT measured — open.** No run separates `Cpu.Step()` from `XboxMachine.Advance` (bus + APU + USB0/1 + NET + NV2A PTIMER remainder, `XboxMachine.cs:314-330`). Per-instruction device cost is amortized over 65536-step `Run` batches (`XboxMachine.cs:467-485`) vs 4096-step quanta under `--break-log` (§114). ESTIMATE only: with idle-skip proving skipped chunks cost ~ns/cycle, the ~70 ns/insn is dominated by fetch+decode+dispatch+translate, not devices — but the first profiling act must confirm this (proposal below).

**Per-instruction cost centers (code facts, sizes measured, shares ESTIMATE).** `XboxCpu.Step()` (`XboxCpu.cs:63-107`): IRQ check → `Fetch` → `Decoder.Decode` → 354-case `switch` dispatch (`XboxCpu.cs:170ff`, 354 `case Mnemonic.` sites counted) → `_idle?.Observe`. `Fetch` (`XboxCpu.cs:160-168`) reads all 15 bytes individually, each through `TranslateLinear` → `Mmu.Translate` (256-entry direct-mapped 4 KB SoftTLB, `SoftTlb.cs`; 4 KB + 4 MB pages, A/D bits, WP faults, `Mmu.cs:37-100`) → `IMemoryBus.Read8`. Cross-page operands fall to byte-by-byte translate loops (`XboxCpu.cs:865-903`). `Decoder.Decode` is a 2812-line table-driven decoder (`Decoder.cs`); `Mnemonic.cs` is 428 lines. Plan Phase-16 order (decode → memory → branch → flags → block cache → regalloc → indirect → MMIO → TLB → sync) already ranks decode and memory first.

### 2. Hot-spot profile proposal (deterministic, flag-off comparable)

**Run substrate (all exists).** Restore a v3 snapshot (§§121/124 — old `.xgs` must be regenerated: Nv2a v2→v3 breaks loads, Audio v3 keeps a v2 fallback) and run a fixed `--steps` window: snapshot save/restore is bit-exact (EIP/GPRs/NV2A counters/frame SHA/RAM SHA identical, §101), so every profile run is reproducible; 50M post-restore steps ≈ 20 s (§101). Use the golden anchors as the determinism check on every profiled run: menu frame SHA `4EF1FBF6…`, triangle `A1740DE0…` (§§113/117/122).

**Hook point.** `XboxCpu.Step()` line 106 already calls `_idle?.Observe(instruction, eipBefore, eipAfter)` after every instruction. Add an analogous nullable `IHotspotObserver` (`null` = zero behavior change, same pattern as §111's "flag-off: detector is null"). Cost ceiling is measured: the `--break-log` single-step loop (a `Contains` check + `Step()` + 4096-quantum advance, `Program.cs:449-489`) costs ~3% vs the main loop (§112) — a few counter increments per instruction will cost less; verify by A/B wall on the triangle 1B pair.

**Counters (bounded, following §119 precedent: 128-head cap, `headsUntracked` overflow count).**
- Per-`Mnemonic` execution counts (array indexed by mnemonic — decode-stream mix: which of the 354 cases are hot).
- Per-PC execution counts for code heads (taken-branch targets / block entries), capped with overflow accounting; attribute loop spans exactly like §119's exclusive-time attribution (single-open-span discipline, untracked code credited to the open head — documented approximation, good enough for scoping).
- Fetch/translate counters: TLB hit/miss (`SoftTlb.TryLookup` outcomes), cross-page split frequency, MMIO-region vs RAM data accesses. These decide fetch-fast-path vs decode-cache priority with numbers instead of estimates.
- Coarse sampler already exists: `EipHistogram` (`XGrid.Core/Diagnostics/EipHistogram.cs`, caller-driven cadence, `Program.cs:551`) — use for plateau checks, not for billing.

**Outputs.** Ranked mnemonic table + ranked PC table + TLB/split/MMIO rates for (a) dashboard 1B menu boot, (b) triangle 1B boot, (c) one post-restore window on a later-stage snapshot once the sample-driven chain reaches one. Keep the profiler OFF for all acceptance comparisons (flag-off byte-identity rule, §§111/119); profile runs and acceptance runs are separate invocations.

### 3. Candidate next slice: decode cache + fetch fast path (no codegen)

**Recommendation: block cache without codegen** — cache `DecodedInstruction` streams keyed by (guest VA, CPU mode, operand/address-size defaults, segment-base generation, CPL-equivalent state) per Plan §20.4, plus a single-translate fetch fast path when all instruction bytes lie in one RAM page. This is Plan Phase-16 items 1+2+5, the smallest increment that removes the two largest per-instruction costs (re-decode + 15 translates/fetch) while executing the identical interpreter bodies. Threaded dispatch has no C# computed-goto equivalent (nearest is delegate-table dispatch — ESTIMATE, measure against the 354-switch baseline first); tier-1 IL/`DynamicMethod` compile (Plan §20.6 options 1–3) is a later slice behind the same gates.

**Prerequisites.** Write-path invalidation hooks: `WriteMemory`/`RawWrite` (`XboxCpu.cs:881-903`), DMA/BM-DMA guest writes (`IdeController` PRDT walk through `IGuestMemory`), device RAM writes (APU DSP scratch DMA §121, NV2A blits/clears into guest RAM), kernel in-place code mutation (§34), and snapshot load (flush cache). Cache key must include everything decode/execution depends on: CR0.PE/PG, `DefaultOperandSize`/`DefaultAddressSize`, CS base (real vs protected mode), CR3 generation, and INVLPG/page-fault paths (`Mmu.Flush/FlushPage`). Determinism harness: §101 bit-exact restore + the two frame SHAs + suite green.

**Risks (all with note citations).** Self-modifying code is REAL on this guest: kernel decrypts the XBE entry in place (`xor [0x10128],0xA8FC57AB` at `0x8002EFE6`, §34); the dashboard decompressor (`0x5C4A0`, 16 KB ring, §§34–35) writes its output to `0x7FA00000`; the EOS quirk mutates guest RAM host-side (`XboxMachine.cs:392-459`). Invalidation must be conservative-first: any untracked write path flushes. IRQ-precise points: `Step()` polls interrupts before every fetch (`XboxCpu.cs:69-81`); cached blocks must still check interrupts at every original instruction boundary — no boundary elision in this slice. Fault precision: `#UD`/page-fault paths must reproduce the exact faulting EIP and vector (CMC precedent §120; `fcomp` fidelity class §34). Single-step observability: `--break-log`/`--break`/`--trace` drive `Cpu.Step()` directly (so the cache must be transparent to step granularity; absolute-step recipes stay flag-off-only). Composes with, not replaces, idle-skip (`XboxCpuIdleSkip.cs:162-237`).

**Concrete acceptance gate (same-host ratios, per §119).** Triangle 1B boot (`--gpu sw`, flag-off vs slice build, back-to-back same load window): Msteps/s improves (report ratio); `draws=2706 frames=68 stalls=34`, sw frame SHA `A1740DE0…` unchanged; menu 1B pair: frame SHA `4EF1FBF6…`, counters identical sw↔build. Full suite green + format clean; flag-off (cache disabled) byte-identical logs per §§111/119 precedent. Profiler re-run shows decode + translate shares moved by the predicted amount.

### 4. Explicit non-goals for the slice

Any host codegen (Plan §20.6); async/threaded submission (no-mixing/no-async rules, design §7 risks 3–5); swapchain/windowing (latency feature, §122); Vulkan (GL-first stands); device deadline hooks (MMIO/PIO parks 0.038%, §119 closed it); NV2A/DSP/device fidelity changes; snapshot format changes; default-on (opt-in flag like `--idle-skip`, flag-off byte-identical); texture census / DXT / mipmap-LOD work (§124 reserve rows); absolute-step recipe compatibility under the flag (§111 semantics stand).

## §127 - wave 9 BOSS record: re-verify green, present-upload merged, JIT scoped, 007 probe CLOSED (2026-10-14)

Five tasks fanned out; four green, one closed by operator rule. **T1 dashboard golden PASS:** 1B-step menu recipe ×2 runs, vec=0, frame SHA `4EF1FBF6…CB0DE81` bit-exact both runs, EIP `0x1E0EB`, full counter table run-identical (`draws=33632 skipped=1402 pixels=285171381 … frames=686 stalls=686`, `zclipCull=21526`, unmapped only `045=4 065=8`, NVNET 342B loopback intact). Note: absolute counters differ from the §42 `draws=2670…frames=33` baseline — different configuration (1B pad-driven menu on merged master vs 1.7B neutral pre-wave-8 build; pusher-timing baseline shift per AGENTS.md) — the binding golden for this recipe is the SHA, which matches, and §121 item (3) already pins this recipe SHA-identical on pristine master. Wall 345 s/1B contended (~2.9M/s vs 70–90 s quiet rule-of-thumb — host contention, ratios only). **T2 ktest smoke PASS 7/7** in 04:36 (XGrid 4/7, xemu 2/7, suite steps bit-reproducible 792,800,535, xemu park signature identical) — zero deviations from §123. **T3 merged** (§125). **T4 filed** (§126). **T5 (007 fresh-v3-chain long probe) CLOSED-unrun:** the task spanned ~57.8B steps with no intermediate deliverable — still running past every review point (stuck by the poll rule: no checkpoint return inside any 10-min window) — and the operator then closed the whole line of work with a standing scope rule: **no real Xbox games at this stage of development; all title work uses `data/test-samples/`** (`triangle.xbe`, `nxdk-hello/led/mesh/triangle/`, `kernel-test-suite-default.xbe`); 007/real-game probes deferred to a later stage. Consequences recorded: the §121 DSP-bridge live verification stands on its pre-merge evidence only (dashboard-neutral byte-identity re-proven post-merge by T1); Nv2a v3 snapshot regeneration for any long chain is still open; the old `.xgs` chain (`007-streamend/pd*.xgs`, `apu-dsp/pdE1-*.xgs`) remains unloadable. **Poll-discipline lesson (now in the BOSS protocol):** no task may span work without a checkpoint return inside ~10-min windows — long probes must be scoped as checkpointed legs (save snapshot + return evidence per leg) so BOSS can poll, restart, or close them.

## §128 - JIT slice 1a LANDED: IHotspotObserver profiler (no cache) + first 1B profiles (2026-10-14)

Wave-10 build task (§126 §2, profiler half only — NO decode cache). `IHotspotObserver` (`XGrid.Cpu/IHotspotObserver.cs`, nullable, null = zero behavior change) called from `XboxCpu.Step()` after every instruction at the same site as `_idle?.Observe` (`XboxCpu.cs:106`); bounded counters per the §119 precedent (128-head cap + overflow visits): per-`Mnemonic` array, per-PC taken-transfer heads, fetch windows + cross-page rate, SoftTLB hit/miss (`Mmu.Translate` TryLookup outcomes; probe translates and paging-off report nothing), RAM-vs-MMIO data accesses + cross-page splits (`ReadMemory`/`WriteMemory`, paging on or off — a paging-off ReadMemory/WriteMemory bypass found by a failing unit test now reports too). `HotspotProfiler` (`XGrid.Cpu/HotspotProfiler.cs`) writes `section,key,count,share` ranked reports; CLI `--hotspot <csv>` attaches for one run, prints top-15 + saves top-25. Five unit tests (`HotspotProfilerTests.cs`); full suite 1255 green (1250 + 5); format clean; flag-off carries only null checks.

**Acceptance.** Flag-off: menu 1B pad recipe SHA `4EF1FBF6…CB0DE81` + full §127 counter table identical (EIP `0x1E0EB`, draws=33632/frames=686/stalls=686, vec=0); triangle 1B SHA `A1740DE0…` + draws=2706/frames=68/stalls=34. Flag-on runs are guest-identical too (both SHAs + counter lines bit-exact with the profiler attached — non-intrusion proven, not just flag-off). A/B same-host adjacent: menu 348.4 s off / 351.1 s on (+0.8%); triangle 260.6 s off / 271.6 s on (+4.2%); drift-cancelled 100M ABBA (off/on/off/on: 23.6/24.9/23.5/24.8 s) = **+5.5% hook cost — above the §112 ~3% ceiling**, so the §126 "will cost less" estimate is disproven by measurement.

**First profiles (flag-on 1B, sw).** Menu (999,996,847 obs; 3,153 IRQs bypass Observe): Mov 33.8%, Jcc 17.6%, Dec 10.3%, Cmp 5.9%, Nop 3.5% (top-5 = 71%); translates 15.5B (15.1 fetch + 0.5 data per insn) at TLB 97.5% hit / 2.5% miss (386M walks); data 502M (RAM 99.9%, MMIO 0.10%); splits 274 (~0); fetch-cross-page 0.12%. Triangle (999,998,117 obs): Mov 25.8%, Jcc 22.3%, Cmp 12.2%, Nop 11.3% (top-5 = 72%, Cli/Sti 5.7% each — kernel idle); TLB 97.5%/2.5% (385M); MMIO 0.05%; splits 272; xpage 0.08%.

**Three findings for slice 2.** (1) Zero TLB hits in a 10M-step paging-off smoke, then steady 97.5% hit with 2.5% irreducible misses, is consistent with the kernel mapping RAM with 4 MB large pages (never cached in the 256-entry SoftTLB): the fetch fast path must treat a large-page residency as a whole-page single-translate win, not a TLB problem. (2) Hook cost driver is ~18 interface dispatches/insn (15.5 TLB notes dominate); batch per-step/per-access tallies (e.g. `NoteTlbHits(int)`) are the fix if profile runs need to be cheaper — the flag-off path keeps only null checks. (3) Head-table saturation: 128 slots fill with first-seen BIOS/2BL heads (top head 105K = 0.01%) while 177.8M/230.6M head visits (17.8%/23.1% of all insn) overflow — first-wins capping cannot locate steady-state hot blocks over a full boot; slice-2 profiling needs windowed or top-N head tracking. Profiler total vs steps differs by the IRQ count (interrupt entries return before Observe) — determinism anchor stays the frame SHA, not the total.
## §129 - present-upload slice: empty-FBO flip serves the guest-RAM surface (2026-10-14)

Wave-10 T2 build task (branch `wave/gl-present-upload`): the §125 deferred
follow-up, first half. Hooks at `GlPgraphBackend.FlipCore`
(`GlPgraphBackend.cs:569`), `_fbo == 0` branch ONLY.

What landed (5 modified + 3 new files): `IPgraphBackend.cs`
(`PgraphSurfaceRecord` gains `uint Pitch`, 6th ctor param — the low 16 bits
of the surface-pitch register, i.e. the guest-row stride; the record's
documented purpose is already "same fields CapturePresentedFrame records",
so pitch belongs there); `Nv2aMmio.cs` flip site populates it from
`_surfacePitch & 0xFFFFu` (the exact value `CapturePresentedFrame` records
into `_presentedPitch`); new `XGrid.Nv2a/PresentUpload.cs`
(`TryBuildUploadPixels`: pure, no GL — pitch/offset walk mirroring
`TryCopyPresentedFrame`, A8R8G8B8 `0xAARRGGBB` → RGBA, guest row order,
xemu `gl/surface.c pgraph_gl_upload_surface_data` pitch-repack rule for
`pitch > width * bytes`, facts only; guards mirror the software copy —
4-byte-only, dims 1..4096, pitch != 0, `pitch >= width * bytes`, 32-bit span
no-wrap; no RAM-size check by design — production `IGpuMemory` reads are
total functions, so OOB can only serve benign unmapped values, never throw);
`GlPgraphBackend.cs` uploads on `_fbo == 0` (build pixels → `EnsureFbo(w,h,0)`
→ `UploadFullFrame` throwaway-texture + NEAREST blit, then falls through to
the existing readback; any failure `DeleteFbo()`s back to `_fbo == 0` so the
session keeps the pre-upload honest gap) with counter
`diag[presentUploads]` (reset in `ResetState`); non-empty FBO deliberately
untouched. Tests: 11 in `XGrid.Nv2a.Tests/PresentUploadTests.cs`
(address→RGBA exact bytes, padded-pitch byte-parity vs a real
`TryCopyPresentedFrame` run, 7 guard cases + span-wrap + null-memory) and 1
live GL test in `XGrid.GpuGl.Tests/PresentUploadLiveTests.cs`
(plain `[Fact]` with a WGL-guard early return — passes unexercised where
no GL context exists, no new test dependency per the sources.md rule;
on this host it RUNS: empty-FBO flip
serves the exact guest pattern, second flip at a different address serves
the first frame again, `presentUploads=1`, `BackendUnsupported=0`).
Suite 1262/1262 green (1250 + 12), format clean. Goldens 1B pairs on this
branch: menu sw SHA `4EF1FBF6…CB0DE81` exact, counters identical
(`draws=33632 frames=686 stalls=686`), gl `unsupported=0`, BMP
`violations=0 maxDelta=1`; triangle sw SHA `A1740DE0…`, anchors
`draws=2706 frames=68 stalls=34`, gl `unsupported=0`, `violations=0
maxDelta=1`.

Acceptance discrepancy (diagnosis, not a fix — the `_fbo == 0`-only scope
stands): `presentUploads=0` AND `flipEmpty=0` on BOTH titles, so the
"counter > 0 on menu flips" sub-criterion cannot fire as scoped — draws and
clears always create the FBO before the first flip on menu/triangle, a fact
already measured on master (§125: menu `flipEmpty=0`) and re-confirmed on
this branch. The branch's live shape is the FMV one (`flipEmpty == flips`,
§122: 39/39) — reachable only via 007, which the standing no-real-games
rule forbids; the WGL-guarded live test covers exactly that shape instead
(empty-FBO flip + guest-RAM surface → uploaded frame served). If BOSS wants
a live-run counter > 0, the next slice must be the non-empty-FBO case, not
more empty-FBO work.

Precise open point — non-empty-FBO staleness design: when `_fbo != 0` at
flip, the backend cannot tell a GL-rendered present (FBO is current — serve
it) from a CPU-blitted present (FBO is stale — must upload or abstain).
Solving it needs guest-visible surface-access tracking (options, unsolved:
(a) dirty-watch the presented span via the bus watch/fault path and upload
on dirty, full-frame replace; (b) reuse the texture fingerprint sampler as a
staleness probe with a policy for hash cost vs false-negative rate; (c) an
xemu-style surface cache keyed by (vram_addr, pitch, format) with
upload_pending/download_pending state — the heaviest, most faithful).
Constraint survivors for any option: never mix (full-frame replace or no
frame), no async (emulation-thread only), headless FBO only (no
windowing/swapchain until the operator decides).

## §130 - wave-10 BOSS record: clamp merged, profiler + upload merged, sample matrix set, smoke green (2026-10-14)

Five tasks, all green, three merges (`94997e1` clamp67, `f1a55d4` jit-profiler, `59bf909` gl-present-upload). **T5 clamp MERGED:** modes 6–7 → CLAMP_TO_EDGE in `DecodeTextureAddressU/V/P` (`NormalizeTextureAddressMode`); reference fact xemu `pgraph/gl/constants.h:48-55` + asserts at `texture.c:160,169,178` (no behavior exists for 6–7 — clamp is ours); fail-pre-fix proven via stash; menu+tri byte-identical (zero head movement, as §124 predicted); +1 test. **Merge conflict (T1/T2):** both appended §128 — resolved §-union (profiler keeps §128, upload → §129), plus an `Nv2aMmio.cs` auto-merge (flip-site pitch vs normalize — disjoint hunks, verified by suite). **BOSS dependency fix:** T2's live test used new NuGet `Xunit.SkippableFact`, which violates the sources.md rule (only XISOSharp + Silk.NET approved) — replaced with a plain `[Fact]` + WGL-guard early return (same operational behavior: runs on GL hosts, passes unexercised headless), pin + project ref removed, §129 text updated; suite still 1268. **T3 sample matrix (master, flag-off):** triangle 1B `vec=0 @0x8001B032`, `2706/89142121px/68/34`, SHA `A1740DE0…` exact (89142121 is the post-§109 anchor); ktest smoke `PASS` (792,800,535 steps, 4/7 vs 2/7); **nxdk-hello/led/mesh/triangle sources BLOCKED — no nxdk toolchain on this host** (`NXDK_DIR` unset, no checkout; only mingw/cmake/nasm) — binding missing piece for mesh (vertex buffers + textures = next GPU step), plus mesh lacks upstream `texture.h`/`verts.h`. Standing recipes recorded in the T3 return (triangle-iso + smoke script). **T4 smoke PASS 7/7** in 03:54, steps bit-reproducible, zero deviations. **Disk:** C: had 4.8 GB free (61 scratch 1.5 GB qcow2s = 84.5 GB under `Temp\opencode`); BOSS deleted all `*.qcow2` there (regenerable private copies; template lives in `data\`; `.xgs`/ISOs/logs kept) → 89.3 GB free. Finding for hygiene: probes should copy qcow2 to D: when C: is tight (T3/T5 precedent), and `Qcow2DiskImage` is read-only so sharing one private copy across runs is safe. **Baseline 1268/1268** (1250 + 5 profiler + 12 upload + 1 clamp), format clean.

## 131 - JIT slice 2 LANDED (legs a+b+c): batched tallies, windowed heads, fetch fast path +35% (2026-10-14)

Wave-11 build task on branch `wave/jit-slice2` (from master 65d40a3): the three
§128 findings worked as one sequential chain a->d, each leg gated by unit tests
before the next. Legs (a)(b)(c) LANDED, all acceptance gates green. Leg (d)
(decode cache) DEFERRED with a precise next-task scoping at the end of this
note (what blocked, exact site inventory, next-task recipe) — a clean
checkpoint per the task brief. No codegen anywhere; no new NuGet dependencies;
TreatWarningsAsErrors/nullable/format clean throughout.

### Leg (a): batch tallies — 18 dispatches/insn down to ~5, hook cost +4.3%

§128 finding 2: ~18 observer dispatches/insn, 15.5 of them TLB notes from the
15-byte fetch loop. Fix, all in `XGrid.Cpu`:

- `IHotspotObserver.cs`: +`NoteTlbHits(int)`/`NoteTlbMisses(int)` (batch
  contract documented: totals identical to N singular calls).
- `HotspotProfiler.cs`: implements with `+= (ulong)count`,
  `ThrowIfNegative` guard (CA1512).
- `Mmu.cs`: `Translate` body extracted verbatim into private
  `TranslateCore(..., bool notify, out bool tlbHit, out bool tlbCounted)`;
  public `Translate` forwards with notify:true (behavior identical for all
  existing callers: descriptor/gate reads in `XboxCpu.cs:1111,1528,1759`,
  EOS-quirk probes in `XboxMachine.cs:409,441`, CLI debug paths in
  `Program.cs:473,1227,1639`); new `TranslateUncounted` forwards with
  notify:false and returns the outcome (identical page walks, A/D bits, TLB
  inserts — only the notification is suppressed).
- `XboxCpu.cs`: `Fetch` split into flag-off-verbatim loop (observer null)
  and profiler-on `FetchCounted` (one `NoteFetch`, accumulate 15 outcomes,
  one `NoteTlbHits`/`NoteTlbMisses` flush). Data paths untouched (0.5
  notes/insn average — batching buys nothing there; stated explicitly so the
  next reader does not re-derive it).

**Fail-pre-fix bug the new tests caught (real logic error, not a test bug).**
First version accumulated in the loop with try/finally flush. The
`FetchFast_FaultPrecision` test failed: off-run reported 2 misses, on-run 3
(eventually bisected to fetch-only misses=0 vs 1 with a temporary debug
test, since deleted). Root cause: a faulting iteration never returns, so its
outcome is never accumulated — `finally` only flushes iterations that
returned. Fix: `TranslateLinearCounted` returns outcome+fault WITHOUT
throwing (`Mmu.TranslateUncounted` outcome outs passed straight through);
`FetchCounted` accumulates first, then throws `PageFaultSignal`, then does
`_idle?.NoteAccess` + bus read — guest-visible effect order identical to
legacy (fault implies no access note). Lesson for (d): never read inline
out-vars in a `finally` after a throwing call — accumulate before throwing.

**Hook-cost measurement (100M triangle ABBA, same method as §128).**
OFF 28.1/28.2/27.8 (avg 28.03), ON 29.4/29.0/29.3 (avg 29.23): **+4.3% +-
~0.6pp** (pairs +4.6/+2.8/+5.4) vs §128 +5.5%. Under, but inside host noise —
reported honestly: the dispatch count fell 18->~5 and the residual is
`Observe`-body counting work (mnemonic array + branch dict), irreducible
without sampling (out of scope). Batch-equivalence is proven by the profile
totals below matching §128 exactly.

### Leg (b): windowed head tracking — overflow 23.1% -> 0.28%, idle loop located

§128 finding 3: 128 first-wins slots fill with BIOS/2BL heads (top head 105K
= 0.01%), 177.8M/230.6M visits (17.8%/23.1%) overflow. Fix in
`HotspotProfiler.cs`: every `HeadWindowInstructions` (1M, const) observed
insns, truncate to top `HeadWindowRetained` (64) by count
(`EvictColdHeads`; survivors keep cumulative counts; evicted heads re-enter
at 1 — self-healing; windows are instruction-count based hence deterministic).
New counters `HeadWindows`/`HeadEvictions` + two additive report lines
(existing report asserts unaffected). Unit test drives 2M `Observe` calls:
128 one-shot heads -> window keeps 64/evicts 64 -> 100K-visit hot head tops
the table -> next window retains it (plus report-line asserts).

**1B profile evidence (profiler-on runs, `wt-data` CSVs kept out-of-repo).**
Triangle: overflow 230.6M (23.1%) -> **2,828,373 (0.28%)**, 999 windows,
16,082 evictions, final table 71 heads. Menu: 177.8M (17.8%) ->
**14,277,354 (1.43%)**, 999 windows, 33,760 evictions, table full at 128.
Top heads are now real steady-state blocks, cross-title consistent: kernel
spin `0x800422B0` 92.5M tri / 92.8M menu (9.3% — vs 0.01% before, ~880x),
**kernel idle `0x8001B02E`/`0x8001B043`** 56.5M+56.5M tri (the §111 idle
thread; triangle's 45.6% parks visible) / 16.1M+16.1M menu, then
`0x80042161`/`0x80042168`/`0x80045AF0` ~3.4M each both titles. The JIT slicing
question "where are the hot blocks" is answered: kernel spin + kernel idle +
a handful of kernel helpers. Mnemonic mix unchanged (menu Mov 33.8 / Jcc 17.6
/ Dec 10.3 / Cmp 5.9 / Nop 3.5 — exact §128 match), totals bit-exact with
§128 (999,998,117 tri / 999,996,847 menu; tri TLB misses 385,216,807 exact).

### Leg (c): fetch fast path — single translate, +35% Msteps/s, goldens exact

§128 finding 1: steady 97.5% hit / 2.5% irreducible miss = kernel 4 MB large
pages (never in the SoftTLB). Design (`XboxCpu.cs` `Fetch`/`FetchFast`, `Mmu`
unchanged — the single translate IS `Mmu.Translate` called once): when the
15-byte window lies in one 4 KB page it lies in one 4 MB region (4 KB pages
cannot straddle 4 MB boundaries — no separate large-region check needed), so
one lookup resolves every byte: TLB hit for 4 KB, one directory walk (one
miss per contract) for large pages. Equivalence argument (unit-proven):
single-page window implies identical mapping/fault for all bytes, so the slow
path always faults on byte 0 with no prior access notes — exactly what the
single translate reports; A/D/TLB-insert side effects, per-byte
`_idle?.NoteAccess`, per-byte bus reads, and `NoteFetch(crossesPage)` value
are all preserved. Cross-page windows (~0.1%) take the legacy path and count
`NoteFetchSlow` (new observer method + `FetchSlowFallbacks` counter; fast
coverage = fetches - fallbacks). Opt-in `XboxCpu.FetchFastPathEnabled`
(default off) + CLI `--fetch-fast` (usage line + `Fetch fast path=on`
announcement); host config untouched by Reset/snapshot like the profiler.

**Acceptance (1) — Msteps/s ratio UP (back-to-back, same host window).**
Triangle 1B: off 291.5/293.6 s (3.43/3.41 Msteps/s), on 216.1/217.3 s
(4.63/4.60) = **1.349 / 1.351 (repeat confirms)**. Menu 1B: off 372.2 s
(2.69), on 286.1 s (3.50) = **1.301**. Far above the "any UP counts" bar:
14 fetch translates/insn were ~26% of runtime.

**Acceptance (2) — goldens exact.** Triangle 1B SHA
`A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C` +
draws=2706/frames=68/stalls=34 (+pixels=89142121 and all sub-counters) on
off/off2/on/on2. Menu 1B SHA
`4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81` +
draws=33632/skipped=1402/frames=686/stalls=686 (+all sub-counters) on
off/on. **(3) flag-off byte-identical:** full-log diffs off-vs-on on both
titles show ONLY the flag announcement + frame-path lines (invocation
filenames); profiler OFF for all acceptance comparisons.

**Acceptance (5) — shares moved by the predicted amount.**
Translates/insn: triangle 15.386 (15,000,398,142H+385,216,807M) -> **1.401**
(1,360,682,752H+40,592,639M, 97.10% hit); menu 15.5 -> **1.537**. Fast
coverage 99.92%/99.88% (fallbacks == crossPage exactly: 761,045 tri /
1,222,767 menu). All non-TLB counters identical base-vs-fast (guest-identity
proof). Decode share has no counter (profiler measures translates, not
decode) — that unmeasured remainder IS the (d) prize (see scoping).

**Tests:** 9 new (2 batch tallies + Mmu parity, 1 window eviction, 6
fetch-fast: default-off, straight-line identity+shares, cross-page fallback
equality, large-page one-miss-per-fetch, fault precision vs vector-14
delivery, paging-off silence). **Suite 1277/1277** (1268 + 9), format clean.

### Leg (d) DEFERRED — precise next-task scoping (diagnosis, not an attempt)

(d) needs the decode cache (Plan Phase-16 items 1+2+5, §126 §3): cache
`DecodedInstruction` streams, conservative invalidation, key on
CR0.PE/PG + operand/address-size defaults + CS base/D/B + CR3, INVLPG paths,
no codegen, no boundary elision, exact fault EIP/vector. The enumeration
below is verified against the tree (this branch); it is the handoff.

**Why it did not fit this task.** (a)-(c) + validation consumed the budget:
7 title runs (~35 min wall: tri off/on/off2/on2, menu off/on) + 3 profile
runs (~15 min) + ABBA (~3 min) on a contended host. (d) is 1.5-2x that whole
slice (see recipe) with a much larger blast radius; rushing it risked the
green (a-c). No (d) code was written; nothing half-built is left behind.

**What the next task inherits (all measured/proven above).** Per-insn cost
after (c): 1 fetch translate + 15 bus reads + `Decoder.Decode` (2812-line
table switch) + 354-case dispatch + execute. (d) removes the decode (and the
fetch bytes entirely on hit); the +35% from 14 translates bounds the prize
(decode share unmeasured — first act below should size it). Hot blocks are
known (b): kernel spin/idle/helpers — a small table wins big. No device
fidelity involved; no snapshot format change; opt-in flag
(`--decode-cache`, implies fast-path behavior), flag-off byte-identical.

**Invalidation site inventory (verified file:line, this branch).**
CPU stores: `XboxCpu.WriteMemory`/`RawWrite` (guest VA, physical known post-
translate — page-granular drop point #1; covers stack, §34 XBE decrypt
`xor [0x10128]`, decompressor output to `0x7FA00000`). Device/DMA RAM writes
ALL funnel through five private adapter shims in `XboxMachine.cs` (no device
changes needed): `AddressSpaceGuestMemory.WritePhysical` (:837, IDE BM-DMA
`IdeController.cs:413` PRDT data), `PhysicalGpuMemory.WritePhysical` (:817,
NV2A semaphore `Nv2aMmio.cs:3368` + surface/zeta writes `:3501,3694,5203,
5209,5214`), `PhysicalApuMemory.Write32/8` (:732/:737, DSP state + VP
notifiers `McpxApuDevice.cs:486,758,883,923,1110,1133`), `PhysicalNvNetMemory`
(:757, RX frames + descriptors `McpxNvNetDevice.cs:1065,1154`), `PhysicalUsbMemory`
(:787/:792/:797, HCCA/done/ED `OhciUsbController.cs:895,899,964`). Host
writes: EOS quirk `XboxMachine.cs:446` (direct `Memory.Write32` call site);
snapshot load (`XboxCpuSnapshots.cs:171` already `Mmu.Flush()`s — add cache
flush on the same line); `TestRomRunner.cs:71` image loads (test-only, needs
the flush API). Mapping changes (full flush — all rare): CR3/CR0.PG/CR4.PSE
writes (`XboxCpu.cs:1752,1763,1781` already `Mmu.Flush()`), CS base/D/B
(`LoadSegment`, `XboxCpu.cs:1104` — note SYSENTER/SYSCALL paths set CS too),
INVLPG/INVD/WBINVD (`XboxCpu.cs:654,659`). Page-table writes (remaps): track
observed PDE/PTE pages in `TranslateCore` (2 set-adds per walk only, ~2.5%
of translates) + full flush on write to a tracked page.

**Gotchas the next task must honor (all learned above).** (i) A/D-bit writes
(`Mmu.TranslateCore` `_memory.Write32`) are translation-neutral and happen
on every walk — with a bus-level hook they false-trigger; slice-d1 should
OVER-flush on PT-page writes (sound; boot-phase churn only) and defer A/D
suppression (needs an `IMemoryBus` change: interface + `AddressSpace`,
`PhysicalMemory`, fakes, `MachineGpuMemory` wrappers) to d2. (ii) Faulting
iterations never return their outs — accumulate before throwing (§131a
lesson). (iii) Replay per-byte `_idle?.NoteAccess` on hit (physicals known
from the entry) or skip caching non-RAM fetches — else idle-skip verdicts
drift. (iv) Skip caching cross-page windows (0.1%, 2 tags otherwise).
(v) Same physical page at two linears (kernel aliases): key entries by
linear, reverse-map by physical page. (vi) Cache the invalid marker too
(#UD with exact EIP). (vii) Entry ≈ 200 B (`Operand` ~48 B ×3 + header +
≤15 source bytes); cap the table (64-128K entries + flush discipline),
unbounded growth over a boot is the failure mode. (viii) IRQs already poll
per `Step` (`XboxCpu.cs:87`) and `--break-log` drives `Step` directly — keep
lookup inside `Step` after the poll: no boundary elision, step transparency
free. (ix) Debug-verify mode (memcmp source bytes on hit, flag-gated) until
the 1B goldens prove the invalidation exhaustive.

**Next-task recipe.** Unit: fill/hit, page-invalidate (incl. XBE-decrypt
pattern), PT-write flush, CR3/CS/INVLPG/snapshot flushes, cross-page + MMIO
skip, fault precision. Gates: suite green + format; tri/menu 1B SHAs +
counters; flag-off byte-identical; ratio UP again; profile shows decode
share gone (add a decode-avoided counter to prove it). Estimated 1.5-2x the
(a-c) slice; do NOT combine with codegen or boundary elision.

## §132 - present-upload staleness design: dirty-bit (a*) now, surface cache deferred (2026-10-14)

Wave-11 T2 read-only design task (no code). Problem: `GlPgraphBackend.FlipCore`
serves the FBO whenever `_fbo != 0`, but after §§125/129 it cannot tell a
GL-rendered present (serve) from a CPU-blitted present (stale — upload or
abstain). Survivors still binding: never mix, no async, headless only. Menu/
triangle always create the FBO pre-flip (`flipEmpty=0`, `presentUploads=0`
both) — the live non-empty-stale shape is FMV-like and unreachable without
007 (forbidden), so the implementing gate needs a synthetic harness, not a
title (testrom §102 vehicle).

**Options vs our code.** (a) Dirty-watch: arm watched span = presented
surface (`SurfaceAddress`, `pitch × height`) at `DrawCore`/`ClearCore`/
`FlipCore` ends; any guest write inside sets the bit; `FlipCore` dirty →
upload, clean → serve. State: one `{base,len,epoch,dirty}` + `diag[
flipStaleUploads]` in `GlPgraphBackend.cs`; precedent `AddressSpace.
WatchStart/WatchEnd/WatchHit` + `CheckWatch` in `Write8/16/32`. (b)
Fingerprint probe (reuse `FingerprintTexture` 16-texel FNV): hash at draw/
clear end, compare at flip, upload on mismatch — no write hook, but UNSOUND
alone (see below). (c) xemu-style surface cache (facts only: `SurfaceBinding`
`vram_addr/size` + `draw_dirty/download_pending/upload_pending`, per-surface
CPU range callbacks + `mem_dirty` bitmap + pitch-repack upload): keyed FBO
cache on `(SurfaceAddress,pitch,format,size)` replacing the grow-only FBO.

**Two soundness findings (source-verified).** (i) Twin-write ordering: the
software path runs unconditionally beside the backend, and `Backend?.Draw`
fires BEFORE the software twin (`Nv2aMmio.cs:5530` → fragment loop `5591`)
while `Backend.Clear` fires AFTER the fill (`3433/3455` → `3460`) — so the
backend must NOT clear-dirty inside `DrawCore`; writer identity comes free:
all NV2A surface writers go through exactly two `IGpuMemory` adapters
(`XboxMachine.PhysicalGpuMemory`, `Program.MachineGpuMemory`) into
`AddressSpace.Write32`, so a suppress-scope around those two `WritePhysical`
bodies (single-threaded — plain counter) exempts twins. (ii) Watch placement:
`Write64` RAM fast path skips `CheckWatch`, and `RamAliasDevice` writes
`PhysicalMemory` directly — guest writes via the `0x80000000` alias, RAM
mirror, and `0xF0000000` WC view evade an `AddressSpace`-level watch
(`GetSpan` bypasses are load-time/CLI-only, never runtime guest writers).
Fix: bit lives in `PhysicalMemory.Write8/16/32/64` (single choke covering
aliases) with the adapter suppress flag. Deeper kill for (b): a RAM-hash
reference stored in `DrawCore` never includes the post-`Backend.Draw` twin →
100% false-dirty (every flip uploads; correct pixels, wasted work); storing
it later re-admits CPU dirt — (b) works only as a pre-filter inside (a)/(c).
(c) is unjustified today: our surfaces are never GPU-resident-only (software
path touches guest RAM every draw — no `download_pending` consumer) and the
working set is one surface per flip, not a render-to-texture graph.

**Failure rule:** wrong SERVE = frozen/torn frame (high severity); wrong
UPLOAD = software-twin pixels = the golden oracle itself (perf cost only);
guard-failed abstain = existing `flipEmpty` path (honest, counted). So: **on
any uncertainty, upload; abstain only when `TryBuildUploadPixels` guards
fail.** (a) degrades to upload-everything (perf); (b) to wrong-serve on small
rects (disqualifying as primary); (c) to (a) on key miss.

**RECOMMENDATION (a*):** external-dirty bit in `PhysicalMemory`, suppress-
scoped around the two `IGpuMemory.WritePhysical` adapters, armed only when
the backend is attached with live FBO (sw default pays zero — flag-off
identity preserved), fail-closed (dirty on `ResetState`/snapshot load → no
`.xgs` bump, backend already excluded from snapshots, §101 holds). Flip rule
in `FlipCore`: dirty → existing upload path (`presentUploads++`), clean →
serve, guard failure → `flipEmpty` abstain. No record change (address+pitch
already carried). **Acceptance gate:** synthetic harness — GL-draw→flip serves
(`flipServed` > 0, GL pattern); GL-draw→CPU-blit→flip uploads
(`presentUploads=1`, guest bytes); blit via the `0x80000000` alias → uploads
(alias-gap regression); save/load mid-sequence → first flip uploads; menu +
triangle 1B goldens unchanged (`violations=0`, `presentUploads=0` both —
titles still never trip it); A/B wall within noise of the +5.5% precedent;
suite + format green. Non-goals: swapchain/windowing (operator-pending),
async, partial-region upload, download/eviction path, snapshot change,
Vulkan, default-on, title validation beyond menu/tri.

## §133 - wave-11 BOSS record: slice-2 speedup merged, staleness designed, goldens + smoke green (2026-10-14)

Four tasks, all green, one merge (`6f2e79b` jit-slice2). **T1 MERGED (§131
on branch):** (a) `NoteTlbHits/Misses(int)` batching + `TranslateUncounted`
+ profiler-on `FetchCounted` (15 outcomes → 1–2 dispatches; data paths left
alone — 0.5 notes/insn buys nothing); fail-pre-fix catch en route (faulting
byte's miss lost — `finally` never flushes unreturned iterations — fixed by
accumulate-before-throw, proven by fault-precision test); hook cost +5.5% →
+4.3% (inside host noise; residual is `Observe`-body work). (b) 1M-insn
windowed top-64 heads: overflow tri 23.1%→0.28%, menu 17.8%→1.43%; top heads
now real (`0x800422B0` spin 9.3%, idle `0x8001B02E`/`43`). (c) `--fetch-fast`
(default off): one `Mmu.Translate` per single-page window (4 KB check
subsumes 4 MB), cross-page → legacy + `NoteFetchSlow`, vector-14 precision
unit-proven. **Acceptance: triangle 3.43→4.63 Msteps/s (1.35×), menu 2.69→
3.50 (1.30×); both SHAs + full counters exact; off-vs-on logs byte-identical
bar flag/paths; translates/insn 15.5→1.4–1.5; 1277/1277 green.** (d) decode
cache DEFERRED with a complete handoff in §131 (invalidation inventory —
all five device adapters are private `XboxMachine.cs` shims needing no
device changes; 9 gotchas incl. A/D over-flush, linear-key/physical-reverse-
map, #UD marker, table cap; unit→golden recipe; est. 1.5–2× the (a–c)
budget). **T2 filed** (§132, recommendation (a*) + synthetic-harness gate).
**T3 smoke PASS 7/7** in 03:51 (steps bit-reproducible 792,800,535, zero
deviations). **T4 merged-master goldens PASS both legs** (menu SHA +
§127 counters exact @`0x1E0EB`; triangle SHA + 2706/68/34 @`0x8001B032`) —
the wave-10 post-merge gap is closed. **Baseline 1277/1277** (1268 + 9),
format clean.

## 134 - JIT slice 3: decode cache LANDED (leg d) - 2.1-3.1x, hit share 99.5-99.8%, goldens exact both flags (2026-10-14)

Wave-12 build task on branch `wave/jit-decode` (from master 2b78a2d, 1277
green): the §131 leg-(d) handoff implemented in full - DecodedInstruction
block cache, conservative invalidation, `--decode-cache`/`--decode-verify`,
decode-avoided counters, 14 unit tests. ALL acceptance gates green: suite
1291/1291 (+14), format clean, menu+triangle 1B SHAs + full counters exact
on BOTH flag-off and flag-on (repeat-confirmed), ratio UP 2.12-3.07x, decode
share gone (0.20-0.47% of flag-off), debug-verify tripwire ZERO failures
through 1B real-title steps. No codegen, no boundary elision, no snapshot
format change, no new NuGet packages.

### Design (as landed)

`DecodeCache` (`src/XGrid.Cpu/DecodeCache.cs`): `Dictionary<uint linear,
Entry>` keyed by LINEAR fetch address (kernel aliases of one physical page
are distinct keys, gotcha v) + physical-page reverse map
`Dictionary<uint page, List<uint>>` + a 16 KB active-page bitset that makes
the common store-drop a bounds check + one bit. Entry (~200 B): the
`DecodedInstruction` (invalid #UD markers cached too, gotcha vi), the
physical base of the 15-byte fetch window (idle-skip replay + reverse map),
the fill-time operand/address-size defaults (hit-validation belt), and the
source window packed into 15 bytes (verify mode). Cap 128 K entries
(`MaxEntries`); reaching it full-flushes (gotcha vii - bounded memory,
`peak` counters prove the cap on menu/tri runs are never approached: peaks
12,644 / 8,100).

CPU integration (`XboxCpu.Step` + new partial `XboxCpuDecodeCache.cs`):
flag off = byte-identical legacy path (plus a `_decodeCache is null` check
per Step and per store, a verbatim `FetchLegacy` extraction, and the
host-only `DecodeCount++`). Flag on: single-page windows look up the cache
by `CsBase+Eip`; a HIT replays the 15 per-byte `_idle.NoteAccess` calls
from the entry physicals (gotcha iii - idle-skip verdicts unit-proven
byte-exact vs flag-off), reports `NoteFetch(false)`, and returns the cached
decode - no bus traffic, no translate, no decode; a MISS takes the §131c
single-translate path (cross-page windows the legacy path) and fills when
the physical is RAM. Misses include cross-page/non-RAM skips, so
hits+misses == steps. `DecodeCacheEnabled=true` implies
`FetchFastPathEnabled` and starts MMU page-table tracking (with a cold TLB
so every back-table page is re-observed); Reset/snapshot leave the flag and
counters alone (host-side state, §126/§131 discipline).

### Invalidation discipline (all conservative; verify=0 proves exhaustiveness)

- CPU stores (`WriteMemory`/`RawWrite`/`WriteMemory64`/
  `WriteMemoryBytes`, post-translate physical): page-granular drop of the
  store's page(s); a store to an OBSERVED page-table page full-flushes
  instead (remap may change other pages - gotcha i over-flush). The MMU's
  own A/D-bit writes never reach these hooks (they are Mmu-internal
  `Memory.Write32`s, not CPU stores), so A/D false-triggers are impossible
  by construction - no `IMemoryBus` change needed, deferred suppression is
  moot at this hook level.
- The five `XboxMachine` device-adapter shims (IDE BM-DMA, NV2A surface/
  zeta/semaphore, APU, nvnet, USB HCCA/ED) now carry the CPU and call
  `InvalidateDecodeCachePage` after every physical write. DEVIATION from
  the §131/brief wording ("full flush on the shims"): page-granular drop
  instead - the NV2A software rasterizer funnels ~89 M pixel writes per 1B
  steps through `PhysicalGpuMemory.WritePhysical`, so full flushes there
  would keep the table permanently cold; page drop is equally sound
  (cached windows never cross a page) and costs a bitset test per write.
- Mapping changes: every Mmu.Flush/FlushPage site (CR0.PG/CR3/CR4.PSE
  writes, INVD/WBINVD, INVLPG, Reset, snapshot load - the §131 inventory's
  choke point) invokes CPU hooks wired in the XboxCpu constructor:
  full flush both. INVLPG full-flushes (v1 carries no linear-page index).
- CS reload: full flush ONLY when base or D/B actually changed
  (DEVIATION from "flush on LoadSegment": same-base reloads happen on
  every interrupt/iret and would flush the hot table thousands of times;
  same-base cannot change any cached decode since keys are linears and
  sizes are validated per hit - exact-#UD EIP preserved). CR0.PE is
  covered by the CR-write flush.
- Host writes: EOS quirk (`XboxMachine.CheckSevenEosQuirk` WriteVa) calls
  `InvalidateDecodeCachePage`; snapshot load rides the Mmu.Flush hook
  (LoadState already flushes - comment updated); TestRomRunner image loads
  ride `Machine.Reset -> Cpu.Reset -> Mmu.Flush`.
- Non-RAM fetches are never cached (flash/BIOS/MMIO: physical bound
  `MaxTrackedPhysical=512 MB` excludes every MMIO + RAM-alias window on the
  Xbox map; `IIdleSkipHost.IsRam` is the second gate) - gotcha iii's other
  half.
- `--decode-verify` (flag-gated, off by default, gotcha ix): on every hit
  the 15 source bytes are re-read from the entry physicals and memcmp'd; a
  mismatch counts `verifyFailures` and re-decodes (self-healing).
  **verifyFailures==0 on menu AND triangle 1B runs** - the invalidation
  discipline is exhaustively proven on real titles at 1B steps.

### Acceptance (exact numbers; 16-core host shared with other wave agents -
walls contended, SHAs/counters step-exact and immune)

Unit: 14 new tests (`DecodeCacheTests.cs`): off-by-default + imply
fetch-fast, fill/hit identity + profiler shares + report lines, xor-write
page drop (§34 decrypt pattern), PT-write full flush, CR3+INVLPG flush,
CS same-base warm / changed-base flush, snapshot-load flush, cross-page +
non-RAM skips, vector-14 fault precision, cached #UD marker with exact
EIP, idle-skip verdict exactness, verify tripwire (incl. the 15-byte
window-overlap semantics), entry cap (peak==131072, one flush), host flush
APIs. Suite **1291/1291** (1277+14), `dotnet format --verify-no-changes`
clean, zero warnings.

Goldens (off then on, back-to-back, repeated; profiler off):
- Menu 1B sw: SHA `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`,
  draws=33632 skipped=1402 frames=686 stalls=686 (+ all sub-counters) on
  off1/off2/on1/on2 - EXACT everywhere.
- Triangle 1B sw: SHA `A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`,
  draws=2706 frames=68 stalls=34 on off1/off2/on1/on2 - EXACT everywhere.
  NOTE for future agents: the triangle golden REQUIRES `--hdd` AND `--dvd`
  together (the recorded anchors came from `gpu-slice2-triangle-golden.ps1`
  which attaches both); a DVD-only boot diverges at 1B (frames=35).
- Flag-off log diffs (off1 vs off2, menu+tri): only the frame-filename
  line -> off runs bit-reproducible; off-vs-on: ONLY the `Decode cache=on`
  announcement, the gated decode-stats line, and the frame path - the
  flag-off codepath carries null checks + host-side counters only, and the
  off SHAs matching the §131 master-recorded anchors is the strongest
  byte-identity proof (master baseline re-run skipped, justified).

Walls (1B steps, seconds; pair order off->on; host contended - §131's
uncontended off-walls were 372 menu / 292 tri):
- Menu: off 700.4 / 662.8, fetch-fast-only 549.8, decode-cache 318.8 /
  312.5 -> **off->on 2.20x / 2.12x**; off->ff 1.27x; ff->on **1.72x**
  (leg-(d) contribution alone).
- Triangle: off 530.3 / 528.6, fetch-fast-only 391.4, decode-cache 178.3 /
  172.2 -> **off->on 2.97x / 3.07x**; off->ff 1.35x (matches §131);
  ff->on **2.19x / 2.27x**.
- Msteps/s: menu off ~1.43-1.51 -> on 3.14-3.20; tri off ~1.89 -> on
  5.61-5.81.

Decode-avoided counter (acceptance 6): menu on decodes=4,679,841 =
**0.468 per-insn** (flag-off = 1.0 by construction; unit-proven) with
hits=995,317,006 (**99.53%**); triangle decodes=1,995,661 = **0.200
per-insn**, hits=998,002,456 (**99.80%**). Repeat runs bit-identical on
every counter (menu on1==on2, tri on1==on2). Profiler report gained
`summary,decodeCacheHits/Misses` lines emitted only when nonzero (flag-off
reports byte-identical to §126/§131 baselines).

Other counters (tri on1): fills=880,681 entries=4,156 peak=8,100
pageDrops=352 fullFlushes=31,781 crossPageSkips=761,045 (0.076% of steps)
nonRamSkips=353,935 (BIOS/flash - correctly uncached); menu on1:
fills=3,103,139 entries=10,192 peak=12,644 pageDrops=4,069
fullFlushes=83,265 crossPageSkips=1,222,767 (0.122%). The fullFlush counts
are INVLPG/CR/PT-write churn of the kernel; even so the steady-state hit
share stays >=99.5% (flushes mostly hit cold boot-phase code).

### Deferred / notes for the next wave

- INVLPG -> full flush (no linear-page index). Counters show 32-83 K
  flushes/1B with no golden impact; a linear-page drop index is a cheap d2
  optimization IF a title ever needs it.
- A/D-suppression for PT-page stores (the §131 gotcha-i d2) is MOOT at the
  current hook level: A/D writes never traverse CPU store sites; only a
  future bus-level writer hook (§132 dirty-bit track) would need it.
- `Program.MachineGpuMemory` (GL backend texture reads) needs no flush
  wiring: the GL backend never writes guest RAM (source-verified).
- idle-skip + decode-cache at 1B title scale: unit-proven exact; not run
  at 1B (not in the acceptance set).
- ktest smoke not run by this task (standing merge gate for BOSS).
- Snapshot format unchanged; old `.xgs` remain valid.

## 135 - GL dirty-bit (a*) LANDED: present-upload staleness closed at the RAM write choke (2026-10-14)

Wave-12 T2 implements §132 recommendation (a*) exactly: an external dirty
bit at the `PhysicalMemory.Write8/16/32/64` choke — the single writer path
that also covers the `0x80000000` alias, the RAM mirror, and the
`0xF0000000` write-combined view (`RamAliasDevice` maps to the same flat
offsets; an `AddressSpace`-level watch misses all three — §132 finding ii,
now regression-tested) — with a GPU-writer-identity suppression counter
around the two `IGpuMemory.WritePhysical` adapter bodies
(`XboxMachine.PhysicalGpuMemory`, `Program.MachineGpuMemory`) so the NV2A
software twin's own surface writes never false-dirty (§132 finding i).
`GlPgraphBackend` arms the watch at flip end with the resolved record span
(`SurfaceAddress`, `pitch × height`; up to 4 round-robin slots so a
double-buffered app's surfaces all stay watched), reads the bit in
`FlipCore`, and the Flip rule is fail-closed: dirty → the FBO is dropped
into the existing `_fbo == 0` upload path (`presentUploads++`,
`flipStaleUploads++`); clean → serve the FBO; upload guard failure → the
existing `flipEmpty` abstain path. `ResetState`/snapshot load mark the bit
dirty (host-side only — no `.xgs` change, §101 holds; `PhysicalMemory.
LoadState`/`Clear` mark only while armed, so pre-arm loads cannot poison a
later session). The bit is cleared ONLY by a successful upload. sw default
pays one predicted branch (`_presentWatchCount == 0`); no user flag — the
watch exists only when a backend adapter is wired.

**Deviations from the §132 prose (all deliberate, documented):** (i) span
slots instead of a single span (alternating present surfaces — the §60
menu-idle `0x3D00000↔0x3E2C000` pair — would otherwise under-watch);
(ii) no "span-mismatch → upload" rule — it would force uploads on the
menu's alternating flips and break the required `presentUploads=0`;
instead the pre-first-flip interval (no span armed yet) serves, matching
today's titles and the `flipServed` acceptance; (iii) arm happens at flip
end only (draw/clear never touch the bit — `Backend.Draw` fires BEFORE
the twin, `Backend.Clear` AFTER; nothing may clear-dirty inside them);
(iv) a backend wired to a bare `IGpuMemory` seam fake (no watch) keeps the
pre-§135 serve-whenever-FBO-non-empty contract — the real CLI wiring
always carries the watch.

**Known corners (fail-closed or unreachable, deferred):** (a) >4 presented
surfaces in flight evicts the oldest span round-robin (a CPU writer to an
evaded span can under-watch; menu+tri use ≤3); (b) a CPU write to a
surface NEVER presented before the very first flip of a session with a
live FBO serves stale (pre-first-flip interval has no span; FMV-before-any-
flip is the `_fbo == 0` upload path, which handles it); (c) double-buffered
CPU-blit FMV where flip N presents a surface first seen at flip N — same
pre-arm corner; all unreachable without real titles (operator rule) and
none reachable by menu/triangle.

**Tests (+25 → 1302 green):** 19 unit (`PresentDirtyWatchTests`: all four
write widths dirty inside the span; boundary-straddle; suppression incl.
nesting; arm idempotence + no-launder; disarm-doesn't-clear; multi-span;
round-robin eviction; LoadState-armed marks / LoadState-disarmed doesn't;
Clear-armed marks; `RamAliasDevice` flat-coverage) + 6 live GL
(`PresentDirtyWatchLiveTests`, early-return non-GL pattern): (a) GL-render
→ flip serves GL bytes (≠ guest bytes; `presentUploads=0
flipStaleUploads=0`); (b) GL-render → flip → guest CPU blit → flip uploads
guest bytes (`presentUploads=1 flipStaleUploads=1`), repeat flip serves
again (no double upload); (c) same blit via the `0x80000000` alias →
uploads (choke regression); (d) save/load mid-sequence → first flip uploads
the LOADED bytes; ResetState → next flip uploads rather than serving
leftovers; (e) twin identity over the full pusher path: one stream,
clear+flip+clear+flip — the second twin fill writes the watched surface
through the suppression-scoped adapter and the flip still serves
(`presentUploads=0 flipStaleUploads=0`). The pre-existing
`PresentUploadLiveTests` (bare `ArrayMemory`, no watch) passes unchanged —
the no-watch contract is now explicit.

**Acceptance (all met).** Suite **1302/1302 green** (1277 baseline + 19
unit + 6 live; `dotnet test -c Release` clean) and
`dotnet format XGrid.sln --no-restore` clean with
`--verify-no-changes` clean (0 warnings — TreatWarningsAsErrors, no new
NuGet, no SkippableFact). **Menu 1B golden** (`scripts/gpu-slice1-golden.ps1`):
sw SHA `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`
== recorded anchor; gl↔sw diff `violations=0 outsideBand=0 maxDelta=1`
(diff=1852 px); counters identical `draws=33632 frames=686 stalls=686`;
`BackendUnsupported=0`; **`presentUploads=0 flipStaleUploads=0 flipEmpty=0`**
(the watch never trips on the title — arm/dirty logic has no false
positives), and the sw log carries no `presentUploads` line at all (correct
absence — the diag only exists with a backend); walls sw 692.9 s / gl
690.6 s. **Triangle 1B golden** (`gpu-slice2-triangle-golden.ps1 -Dvd
D:\wave-worktrees\triangle.iso`): sw SHA
`A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C` == anchor,
anchors `2706/68/34` match, sw==gl, `violations=0 outsideBand=0 maxDelta=1`,
`BackendUnsupported=0`, **`presentUploads=0 flipStaleUploads=0`**, sw log
absence correct; walls sw 516.5 s / gl 529.4 s. **A/B hook cost (menu 1B
gl, back-to-back pairs):** baseline (master build) **718.1 s + 713.1 s
(mean 715.6 s)** vs post-change **674.2 s + 684.1 s (mean 679.2 s)** —
ratio 0.949, i.e. the choke is not measurable above the host's contention
noise (bar was "within noise of the +5.5% precedent"; the shared 16-core
host ran the after-pair under lighter load, so the delta reads negative).
Both pairs' runs: counters identical to each other, `presentUploads=0`.

## §136 - JIT slice 4: block formation + block-granular execution behind --block-exec (2026-10-15)

Wave-13 build task on branch `wave/jit-block-exec`. Design source:
`docs/research-notes/jit-codegen-scope.md` (the scope task's measured design +
recipe; landed as the wave-13 T1 deliverable) plus §§126/131/134/135.

### Design (as landed)

`DecodedBlockCache` (`src/XGrid.Cpu/DecodedBlockCache.cs`): a parallel
derived table over `DecodeCache` entries. A block is a stackalloc-formed
array of up to `MaxInstructions=32` already-cached `DecodeCacheEntry`
structs, all within one 4 KiB linear page AND one physical page, whose full
15-byte fetch windows fit the page; formation reads only EXISTING decodes
(`DecodeCache.TryGet` — no speculative fetch, translate, walk, fault, or
A/D write, per the recipe's rule 1). `Terminates` (conservative slice-4
whitelist): branch/call/ret/far-return/IRET/LOOP/JECXZ, #UD marker, traps
(int/int3/into/hlt), REP/string/I/O, CR/segment/descriptor/system
instructions, x87, MMX/SSE, and every unlisted mnemonic end the block;
only common straight-line integer/data ops (mov/alu/inc/dec/test/setcc/
cmovcc/shifts/rotates/mul-imul/movzx-movsx/lea/push/pop/xchg/not-neg/
flag-sets/leave/enter/bswap/xadd/cmpxchg/bt*/bsf/bsr/cbw/cdq/shld/shrd/
cmpxchg8b, no REP) may continue. Key: the head linear address + operand/
address-size defaults, exactly matching the entry's hit-validation belt;
mapping identity rides `DecodeCache`'s existing flush discipline (rule 3's
CR0/CR3/CR4 identity — the decode cache already full-flushes on every CR0/
CR3/CR4.PSE/INVLPG/CS-base change, so those state changes can never reach a
stale block). Execution (rule 4/5): `Step()` stays a one-original-boundary
API — TSC increments, the IRQ poll runs at its existing pre-fetch site and a
delivered interrupt or fault (`#UD`, `PageFaultSignal`) discards the active
cursor via `EndBoundary()`; one block dispatch per Step selects the member's
`DecodedInstruction` and then the IDENTICAL interpreter body runs with its
normal per-instruction idle `_idle.NoteAccess` replay (15-byte fetch notes
replayed from entry physicals on every step, the §134 hit-path rule),
`Observe` calls, and commit points. Host codegen: none (the mnemonic switch
still runs per instruction; block lookup/cursor reuse is the only change).
Invalidation (rule 6): rides §134's writers — `Invalidate()` fires from
`DecodeCache.DropPhysicalPage`, `DecodeCache.Flush`, and the `Insert`
replacement path; a store to a live code page therefore discards all blocks
conservatively (over-invalidation, under-invalidation impossible without a
missed writer). `--block-verify` (rule 7, default off): full 15-byte window
re-read + compare BEFORE execution; mismatch counts `VerifyFailures`, drops
the page, and decodes fresh (self-healing).

`XboxCpu.BlockExecutionEnabled` (implies `DecodeCacheEnabled`),
`XboxCpu.BlockVerify`, `BlockStatistics`. Bounded: `MaxBlocks=4096`
(≤131072 decoded references = the decode entry cap), reaching it flushes
blocks only (`CapFlushes`; the decode table itself keeps its own cap). No
snapshot format change; Reset/snapshot leave host tables cold via the
existing LoadState flush. CLI `--block-exec`/`--block-verify` + announcement
+ `Block stats` line; profiler report gains `summary,blockDispatches/
blockInstructions/blockBuilds/blockVerifyFailures` rows.

### Unit tests (§134 style, `DecodedBlockCacheTests.cs` + `BlockExecutionTests.cs`)

Cursor invalidation at page-drop AND flush (including the empty-table flush
no-op case — recipe rule 6's gotcha), formation from existing entries only +
branch/page-tail termination + page-tail window rejection, cap discipline
(peak==MaxBlocks, one cap flush, held cursor discarded), Step/budget
transparency vs decode-cache-only (state-hash equality after every call
across mixed Step/Execute patterns), verify tripwire rejecting a stale
member before execution, host-write + snapshot-load discarding active
members, idle-skip verdict exactness through blocks (the §134 hit-path
replay), and #UD/#PF exact vector+EIP+CR2 with cursor discard. Suite
**1324 green** (1316 baseline + 8), format --verify-no-changes clean;
zero warnings; no new packages.

### Goldens + counters (all EXACT across modes)

- Triangle 1B (`--hdd AND --dvd`): SHA `A1740DE0…AA27C`, draws=2706
  pixels=89142121 frames=68 stalls=34 — decode-cache-on AND block-exec
  identical (first pair + reverse-order repeat), verify + flag-off rows in
  the final table below.
- Menu 1B (pad recipe): SHA `4EF1FBF6…CB0DE81`, draws=33632 skipped=1402
  frames=686 stalls=686 — decode-cache-on AND block-exec identical.
- `verifyFailures=0` everywhere (invalidation discipline exhaustively
  proven at 1B steps on both titles). Block stats (triangle 1B):
  dispatches=364,819,853 instructions=998,002,456 builds=144,708
  entries=198 peak=1,828 invalidations=750 capFlushes=0 redirects=0
  boundaryExits=950 verifyFailures=0 (36.6% dispatch share; block average
  ~2.7 instructions). Menu 1B: dispatches=307,156,366
  instructions=995,317,006 builds=1,926,977 peak=2,761 invalidations=5,534
  boundaryExits=1,621 verifyFailures=0 (30.9% dispatch share).
- Flag-off log identity: `wt-data/wave/logdiff.ps1` compares decode-on vs
  block-on full logs with the §111/§134 ignore set (announcement lines +
  gated stats lines + frame path); IDENTICAL on both titles at 1B.

### Wall pairs (1B steps, --gpu sw, profiler OFF, same host, sequential;
legs killed by the operator session are excluded — every quoted row exit=0)

| Run | decode-cache-on | block-exec | ratio |
|---|---:|---:|---:|
| triangle first pair | 134.2 s | 111.4 s | **1.204x** |
| triangle ABBA pair 1 | 95.8 s | 91.3 s | **1.049x** |
| triangle ABBA pair 2 | 95.3 s | — (leg killed) | — |
| triangle contended pair (overlapped batches) | 120.6 s | 124.6 s | 0.968x |
| menu first pair | 224.6 s | 215.7 s | **1.041x** |
| menu flag-off reference | 436.0 s | — | off->decode 1.94x, off->block 2.02x |
| triangle flag-off reference | 338.6 s | — | off->decode 2.81x, off->block 3.04x |

Adjacent-pair verdict: block-exec is faster than decode-cache-on in every
uncontended adjacent pair (triangle 1.204x, 1.049x; menu 1.041x); the one
contended pair (two batches overlapped) inverted — walls on this shared
16-core host are noisy, so the DETERMINISTIC counters are the primary
dispatch-share evidence: blockDispatches/blockInstructions = 36.6%
(triangle) / 30.9% (menu), run-identical across every mode (verify/profile
rows match the plain block rows digit-for-digit). Menu flag-off wall 436.0 s
vs the scope task's 524.78 s (same recipe, lighter contention).

## 137. CLI decode-cache default-on soak (2026-10-15)

Scope: only `boot` CLI policy (`src/XGrid.Cli/Program.cs`), not CPU defaults,
block execution, cache invalidation, device behavior, or snapshot formats.
`--decode-cache` now defaults on; `--no-decode-cache` restores the legacy
fetch/decode path. Repeated on/off switches use the last value. Explicit
`--fetch-fast` remains independent/default-off; `--decode-verify` remains
default-off and implies cache even with opt-out, preserving its old semantics.
Announcements and gated stats are unchanged. No packages added.

Reference-first cross-check: xemu-0.8.136 `accel/tcg/translate-all.c:608`
invalidates translated code through `tb_invalidate_phys_range`; XGrid's
existing invalidation inventory remains exactly as documented in §134.
No hardware semantics or reference files were changed.

Acceptance: `dotnet build XGrid.sln -c Release --nologo` zero warnings/errors;
`dotnet test XGrid.sln -c Release --nologo --no-build` **1316/1316**;
`dotnet format XGrid.sln --no-restore --verify-no-changes` clean. No full
ktest execution (reserved for BOSS). Repro: `scripts/decode-cache-soak.ps1`
with `-Title menu|triangle -Mode off|default|on|verify`, then `-Compare` for
each title; raw logs/BMPs/JSON commands and timings in `wt-data/wave/`.

All eight 1B-step SW runs have LastFault vec=0 and exact anchors:
- Menu SHA `4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`;
  draws=33632 skipped=1402 pixels=285171381 textured=278577344 clears=695
  blended=211696978 depth=136 zclip=0 zclipCull=21526 frames=686 stalls=686
  programLoad=12 constLoad=2 inlineElements=0.
- Triangle SHA `A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`;
  draws=2706 skipped=0 pixels=89142121 textured=48754488 clears=6455
  blended=518 depth=15667336 zclip=0 zclipCull=21526 frames=68 stalls=34
  programLoad=8 constLoad=100 inlineElements=0.
- Entire logs match after removing only decode announcement/stats lines and
  normalizing the frame filename (all other frame diagnostics retained).
  Both verify runs have verifyFailures=0 (menu hits=995317006;
  triangle hits=998002456). Off has no decode or fetch-fast announcement.

Sequential same-host walls (seconds; shared-host contention, not isolated
microbenchmarks):

| Title | Off | Default | Explicit on | Verify | Off/default | Off/on |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Menu | 485.113 | 189.553 | 184.501 | 226.842 | 2.559x | 2.629x |
| Triangle | 398.452 | 131.103 | 112.278 | 173.286 | 3.039x | 3.549x |

Off/off is 1.000x; default/on wall ratios are 1.027x and 1.168x respectively
(the CLI settings enable identical code, so do not attribute that variation
to the flags). Initial neutral-menu off/default probes agreed with each
other but not the requested anchor (SHA DF0FEB86..., frames=682): the
harness had omitted the recorded `gpu-slice1-golden.ps1` timed pad recipe.
Restoring `down@950000000+hold,none@962000000,a@970000000+hold,none@976000000`
produced the exact anchor, without retuning tolerances or product behavior.
Triangle uses both HDD and test-sample DVD as required by §134.

## 138. Wave-13 JIT first-codegen scope: measured emitter pick + boundary-safe block recipe (2026-10-15)

Wave-13 probe task (`wave/jit-codegen-scope`, 4 commits, suite 1316 green, zero
`src/`/`tests/` diff; branch unmerged by design, `merge:false`). Full study:
`docs/research-notes/jit-codegen-scope.md`. Key results:

- **Remaining cost after §134 (measured, 1B pairs, profiler OFF):** menu
  flag-off 524.78 s vs `--decode-cache` 245.74 s (2.14×), triangle 355.09 vs
  107.47 s (3.30×); hit shares 99.532%/99.800%, verifyFailures=0 — the §134
  numbers reproduce on this host. Scratch-instrumented sampled breakdown
  (upper bounds; probe inflation documented): fixed per-step overhead
  (boundary+fetch+dispatch+tail) ≈105 ns instrumented, nearly identical on
  both titles; the workload-dependent part is execute bodies, dominated by
  data-path helpers (76% menu / 29% triangle of execute time).
  **The 354-case switch dispatch is not the dominant remaining cost** — the
  executed bodies, especially memory/data helpers, are.
- **§20.6 options 1–3 benchmarked in order** (Ryzen 9 7900; tiering-off primary
  + default-runtime cross-check): Expression 14.737 / DynamicMethod 14.263 /
  TypeBuilder 14.188 ns/insn (tied within noise) vs cache-on `Step` 32.7 ns and
  a plain C# decoded-helper loop 14.285 ns. **Negative result: no managed
  emitter beats the helper loop** — IL codegen over unspecialized bodies buys
  nothing; a future emitter must win via semantic specialization. DynamicMethod
  chosen provisionally (124.6 µs / 6 KB construction+first-call vs Expression
  205.6/29 840, TypeBuilder 319.3/8 136; compare build+first-call sums, not
  build alone). Correctness hashes matched for all variants; 13-instruction
  budget exits matched 13 real Steps.
- **Corrections to the scoping premise:** `XboxCpuDecodeCache.cs:137–177` hits
  do zero MMU translations and zero bus reads (verify mode excepted — the
  1-translate + 15-read cost is a miss path); `HotspotProfiler` counts events,
  not elapsed time (mnemonic shares are not wall shares); the IRQ boundary is
  `XboxCpu.cs:102–116` (TSC increments before polling; a delivered IRQ consumes
  the budget unit without executing the original instruction).
- **Block design (no boundary elision) — implemented as §136:** form blocks
  only from valid DecodeCache entries (no speculative reads/translation/faults),
  15-byte window within one 4 KiB page mapping one physical page, RAM only,
  32-instruction initial cap; terminate at branch/call/return/indirect/#UD/
  traps/HLT/page-window/missing entry plus conservative mode/system/REP/I/O
  terminators; key = head EIP + CS base/mode/sizes + mapping generation (never
  live GPR/flags/segments); `Step()` stays one original boundary, `Execute()`
  may reuse the cursor within the caller's budget; IRQ poll at every boundary,
  exact fault EIP/vector/error-code/CR2, 15-entry idle NoteAccess replay;
  invalidation rides the §134 sites (extend the cache's drop/flush machinery,
  no new hooks); `--block-exec`/`--block-verify` default-off; host state only,
  no snapshot change, no new packages.
- **Reference facts consulted:** xemu 0.8.136 `target/i386/tcg/translate.c`
  `4235–4278` (CS base/mode capture), `4300–4310` (per-instruction PCs),
  `4353–4370` (single-step/IRQ-inhibit/page termination); no implementation
  copied; XGrid keeps every original boundary (stronger than QEMU chaining).


## 139. JIT semspec spike: measured winner + build design + recipe (2026-10-15)

READ-ONLY spike (wave/jit-semspec-spike, zero src/tests diff). Harness: Ryzen 9 7900 host (PETERSONPC, 24 threads), SDK 9.0.319, runtime 9.0.21, Release, profiler OFF for comparisons. Prototypes + raw logs uncommitted in wt-data/wave/semspec*/ + wt-data/wave/guests/; independent re-run log wt-data/wave/verify-rerun.log.

## 1. Baseline reproduced on this host

(a) Guest walls (§138 Table-a shape, 1B steps, --gpu sw, back-to-back, binding SHAs + counters exact on all six runs, LastFault vec=0): menu off/on/on2 = 494.17/212.22/162.86 s (2.33x/3.03x); triangle = 297.09/89.40/98.08 s (3.32x/3.03x). SHA menu 4EF1FBF6… draws=33632 skipped=1402 frames=686 stalls=686; triangle A1740DE0… draws=2706 frames=68 stalls=34 pixels=89142121; hit shares 0.9953/0.9980 reproduce §134; verifyFailures=0. Ratios agree with §138 (2.14x/3.30x) and §137 (2.56x/3.04x) within shared-host variance; menu on/on2 spread (212->163 s, 23%) is host contention, which is why the build gate below requires median-of-3. BMP SHAs independently re-hashed this session (Get-FileHash): menu-on 4EF1FBF6… and triangle-on A1740DE0… both exact; menu-on.log re-read: LastFault vec=0, draws=33632 skipped=1402 frames=686 stalls=686, hitShare=0.9953, verifyFailures=0.

(b) Micro baseline (§138 14.285 ns/insn helper loop): faithful repro semspec-real (hand-assembled 33-insn stream, same MOV/ADD/XOR/SUB/INC/AND/CMP/NOP x4 + JMP shape; real product Decoder.Decode with per-index mnemonic asserts; ACTUAL private XboxCpu.ExecuteInstruction forwarded via a DynamicMethod adapter, per-insn TSC++, EIP reset per block; ProjectReference to product XGrid.Cpu — plain Release build; ImportDirectoryBuildProps=false does NOT restore here). V0R tier-off median 13.767 (13.075–14.427, sd 0.528) — within ~4% of 14.285; residual is harness detail (no per-insn budget decrement, ref-forwarding vs in-copy). Default runtime: 10.494 (10.076–12.172). INDEPENDENT re-run same host: tier-off V0R 10.805 (10.523–11.142), default V0R 8.313 (8.303–11.619, trial-0 tiering warmup 51.6 then stable) — absolute micros float ~20% run-to-run on this shared host; ranking and winner margin below are unaffected (see verify-rerun.log).

## 2. Candidates, in order — winner by numbers only

All variants hash-gated to identical end state (regs/flags/EIP) before any timing is accepted; the gate caught two real scratch bugs first (V3 IL stack imbalances; V4R EIP-length accounting) — hashes mismatched, timings discarded, fixed, re-run.

Table R (REAL product bodies, rotated head-to-head, 7 trials x 3.3M insns): V0R helper loop 13.767 (13.075–14.427) tier-off / 10.494 (10.076–12.172) default; V2R (a2) per-shape table over real AluOps/Context, zero switches 4.689 (4.405–5.376), 2.94x / 5.763 (4.434–8.721 tier-transition outlier), 1.82x; V4R (c) hand-unrolled C# over the same specialized bodies 2.313 (2.156–2.957), 5.95x / 2.386 (2.240–3.001), 4.40x. Re-run: V2R 3.849/3.844, V4R 1.978/1.906 — same ranking, bands never overlap in ANY run. Model-harness Table M (lean bodies, same ranking): V0 5.473 / V1 (a1) per-mnemonic delegate 5.412 (1.01x — worthless alone) / V2 4.519 (1.21x) / V4 unrolled 3.182 (1.72x) / V3 (b) DynamicMethod straight-line block 2.881 (1.90x, re-run 2.856 vs V4 3.150 = ~9.4% premium); construction ~1.8 ms + ~43 KB per 33-insn block with boundary checks retained (vs §138's 124.6 us/6 KB for a leaner emission — cost scales with retained checks).

WINNER: V4R (pre-specialized per-shape handlers + unrolled block runner in plain C#, NO emitter). Beats 14.285 AND this-host V0R with variance-proof margins in every observed run, both runtimes (max V4R 3.001 vs min V0R 8.303 — no overlap anywhere). (b) DynamicMethod measured for hot shapes: works, hash-exact, but only ~10% over unrolled C# — REJECTED for the next slice (second negative result for managed codegen as a dispatch play stands; the positive result is specialization-without-codegen). Scope honesty per §138: micros cover dispatch + reg/imm bodies only; title execute is dominated by memory/data helpers (76% menu / 29% triangle), so micro ratios do not transfer 1:1 — the build gate is title walls, not micros.

## 3. Implementation design (every standing constraint honored)

Shape key baked per member at FORM time (never live state): (mnemonic, op1kind, op2kind, size, Jcc condition, seg-override-or-none). GPR indices, immediates, displacements, EFLAGS, segments and addresses stay DATA. Control/privileged/mode-changing/REP/string/IO/FPU/invalid shapes are NOT specialized: they end blocks (as-landed Form) and keep the interpreter. Initial cover: Mov r,r/r,imm/r,m/m,r; Add/Sub/And/Or/Xor r,r/r,imm; Cmp/Test r,r/r,imm; Inc/Dec r; Lea; Push/Pop r/imm; Nop (HotspotProfiler top: menu Mov 33.8/Jcc 17.6/Dec 10.3, triangle Mov 25.8/Jcc 22.3/Cmp 12.2/Nop 11.3; Jcc/Jmp/Call/Ret stay control terminators). Memory shapes bake kind+size+segment, still call ReadMemory/WriteMemory (bus/TLB cost unchanged; fault order preserved by advancing Eip only after successful completion, exactly like ExecuteInstruction's nextEip-at-end). Storage/lifetime: resolve in DecodedBlockCache.Form from valid entries only; store handlers IN the Block record (shared lifetime); invalidation rides §134 unchanged (Invalidate drops derived handlers with blocks; held cursor explicitly discarded; no new hooks). NO elision: per-member cursor check (as-landed TryNext), IRQ poll at the Step site (delivered IRQ -> EndBoundary, budget consumed), TSC++, per-member Eip, exact fault EIP/vector/error/CR2, #UD stays interpreter-served. Step() stays one-boundary; Execute() uses the unrolled runner ONLY with break/trace disarmed, else the member-cursor path (4096-step cadence + pre-insn transparency bit-identical). Hotspot/idle Observe + 15-entry NoteAccess replay stay per member in both paths. Flag --spec-exec default-off (implies --block-exec + cache); --block-verify extended to before-member compare -> NoteVerifyFailure path. Counters: shapeHits, shapeFallbacks, specBlocks, verifyFailures. Host-only: CpuStateVersion stays 1, no snapshot change, no new packages, TreatWarningsAsErrors, nullable, format clean, plain [Fact].

## 4. Next-slice recipe + acceptance gate

Slice 1: shape table + Form-time resolution + cursor-served handlers. Slice 2: unrolled runner for all-specialized blocks (break/trace disarmed only). No DynamicMethod, no packages. Gate: (i) unit tests — every shape at 8/16/32, seg overrides, mem-shape fault EIP/vector/CR2 vs interpreter, #UD terminator, mode/CS-alias keys, SMC mid-block, PT change, CR3/INVLPG/reset/load, cap eviction with live cursor, IRQ at every member incl. IF flip, one-Step + budget-1/N transparency, idle NoteAccess counts, verify-before-execute, trace/break-log differential; (ii) full suite green, format clean; (iii) menu + triangle 1B off/spec/verify: binding SHAs + ALL counters exact, verifyFailures=0, flag-off byte-identical modulo announced paths; (iv) wall gate (profiler OFF, back-to-back, median of 3, same host): spec faster than cache-only on BOTH titles — else report, keep opt-in, no fake gate.

## 5. Sources

Product: XboxCpu.cs:102-116 (IRQ boundary), :333+/891 (dispatch + fall-through Eip), :894-937 (ExecuteAlu), :939-986 (kind switches); XboxCpuDecodeCache.cs:169+ (fetch integration, NoteAccess replay); DecodeCache.cs:15-50/69+ (entry, key/invalidation contract); DecodedBlockCache.cs:11+ (as-landed blocks); XboxCpuSnapshots.cs:12 (version 1); Cli/Program.cs:245-264 (flags). Reference: xemu-0.8.136 target/i386/tcg/translate.c:4235-4278/4300-4310/4353-4370 (per §138, facts only). Prior notes §§134/136/137/138. Full design text: wt-data/wave/section139-final.md.

## 140. JIT semspec build: V4R winner lands behind --spec-exec (2026-10-15)

Branch `wave/jit-semspec` (merged). Implements the §139 spike recipe (slices 1+2, no
emitter): the spike's measured winner V4R (pre-specialized per-shape handlers
+ unrolled block runner in plain C#, 5.95x/4.40x over the helper loop with
non-overlapping bands; DynamicMethod rejected at ~10% over unrolled C#).

## What landed

`src/XGrid.Cpu/XboxCpuSpecExec.cs` (new): `XboxCpu.SpecHandler` delegate
(cpu + member; EIP advanced only after successful completion, exactly like
`ExecuteInstruction`'s nextEip-at-end, so faults keep precise EIP),
`XboxCpu.SpecShape` key (mnemonic, op1kind, op2kind, size, Jcc condition,
seg-override-or-none; GPR indices/immediates/displacements stay data) and the
form-time resolver with 69 cached (allocation-free) handlers. Initial cover
exactly per recipe: Mov r,r/r,imm/r,m/m,r; Add/Sub/And/Or/Xor r,r/r,imm;
Cmp/Test r,r/r,imm; Inc/Dec r; Lea r,m; Push r/imm; Pop r; Nop, each at
8/16/32. Every body is the verbatim interpreter case with kinds pre-resolved
(same call sequence into Context/AluOps/ReadMemory/WriteMemory/Push/Pop/
ComputeAddress, including Push's ESP-before-write partial order). Adc/Sbb
(carry live), control/privileged/mode/REP/string/IO/FPU/invalid and all other
shapes resolve null and keep the interpreter. `Terminates` unchanged.

`DecodedBlockCache`: `SpecEnabled` (default off; enabling invalidates
handler-less blocks), handlers stored IN the Block record (shared lifetime;
invalidation rides §134 unchanged), `CurrentHandler` for the just-served
member, counters ShapeHits/ShapeFallbacks/SpecBlocks/SpecRan (VerifyFailures
shared with the `--block-verify` path). `TryNext` signature unchanged.

`Step` (slice 1): serves the cursor handler with unchanged boundary/fault/
observe treatment; flag-off path byte-identical (untouched branch).
`Execute` (slice 2): `RunSpecBlock` serves consecutive members without
returning through Step per member -- TSC++, IRQ poll at the Step site
(delivered IRQ -> EndBoundary + budget unit consumed), full-window verify
before execution with the Step-identical self-healing DecodeFresh path (no
double-count: fresh members skip NoteHit exactly like Step), #UD/#PF paths
mirrored, 15-entry idle NoteAccess replay, hotspot/idle Observe per member,
fallback members run ExecuteInstruction in-runner (cursor never rewound).
Only used inside Execute; trace/break-log callers use Step (member-cursor
path, 4096 cadence + pre-insn transparency unchanged).

CLI `--spec-exec` (default off; implies decode cache + block exec),
announcement + gated `Spec stats` line (all other lines byte-identical in
every other mode). No snapshot change (host-only), no packages.

## Tests (16 new, suite 1340 = 1324 + 16)

`SpecExecutionTests`: Mov/Alu/CmpTest/IncDec-Lea-PushPop-Nop at 8/16/32 vs
the interpreter (full state + whole-RAM equality), FS-override mem shapes,
resolver cover/exclusion pins (incl. adc/sbb/mov-mem-imm/push-mem/inc-mem/
control/traps/#UD excluded), control-mix loop (ShapeHits+Fallbacks+SpecRan),
warm-block mem-fault precision (vector/EIP/CR2 + committed prefix), #UD
terminator, IRQ at the 3rd member + STI IF-flip delivery positions, 200-round
Step/Execute(i) transparency incl. DecodeCacheHits/DecodeCount/profiler
totals, BlockVerify stale-member tripwire (block + decode counters),
cap-with-live-cursor, mode-change redirect, flag-off default pins.

Fail-pre-fix evidence: the suite caught a real test-harness trap during
development (bare IRET after #PF pops the pushed error code as EIP --
identical on both sides, so the differential held; the test now enters the
handler once and rewinds EIP instead). No product fail-pre-fix stunt needed:
goldens below are the behavioral gate.

## Goldens (1B steps, --gpu sw, profiler OFF, sequential same-host, BOSS-run)

Validation recipe executed by BOSS directly (worker handoff, attempts
exhausted on a provider-hiccup-poisoned session): six off/cache/spec pairs
via `wt-data/wave/run-pairs.ps1` + two specverify legs, all PASS:

- triangle: off 299.15 s / cache 92.11 s / spec 78.85 s / specverify 161.06 s;
  SHA `A1740DE0…` exact on all four; NV2A table exact
  (draws=2706 skipped=0 frames=68 stalls=34); spec/cache = 1.17x.
- menu: off 379.67 s / cache 164.30 s / spec 153.50 s / specverify 284.96 s;
  SHA `4EF1FBF6…` exact on all four; NV2A table exact
  (draws=33632 skipped=1402 frames=686 stalls=686); spec/cache = 1.07x.
- `LastFault vec=0` every run; `verifyFailures=0` on block, spec, and decode
  paths of both specverify legs (triangle specRan=503961463,
  menu specRan=603218307).
- off-vs-spec log diff: zero lines differ after stripping Decode/Block/Spec
  + frame-path lines (both titles, 282/282 and 243/243 lines).
- tools/wave suite 42/42 + typecheck clean alongside.
- Honest caveat: single runs, not median-of-3 (recipe asked median); the menu
  margin is thin (7%) but positive on the same host back-to-back. Flag stays
  opt-in; no fake gate.

## Provenance (wave-15 ops record)

Spike (§139) done clean in 3.7 min. Build worker implemented slices 1+2 and
committed (`4d0b5b2`, tree clean, 16 tests) but hit `provider.invalid-output`
mid-validation, then re-prompted the damaged session on resume and died in
seconds -- attempts exhausted with validation unrun, reported blocked with an
exact resume recipe instead of a fake done. BOSS ran the recipe verbatim. Tool
lesson (post-`tools/wave` -- the runner is retired after this wave): retries
after `provider.invalid-output` must start a fresh session; re-prompting the
broken one is doomed. Same root lesson as the wave-14 spike loss.

## 141. Spec-exec median-of-3: flag stays opt-in (2026-10-16)

Direct development, no wave. Gate from `docs/development-direction.md` §2/§4:
median-of-3 walls before any default-flip claim. Harness:
`scripts/spec-exec-soak.ps1` (modeled on `decode-cache-soak.ps1`; legs
`cache` = default decode-cache-on, `spec` = `--spec-exec`,
`specverify` = `--spec-exec --block-verify`; 1B steps, `--gpu sw`).
Media: private copies on `D:\spec-soak\media` (BIOS/mcpx/eeprom/dashboard
HDD from `D:\wt-data\nv2a-clamp67`, triangle.iso built from
`data/test-samples/triangle.xbe` via XISOSharp); legs on
`D:\spec-soak\legs` (C: has ~5 GB free — never on C:). One leg
(menu/cache/r1) ran pre-restart; a server restart cancelled the batch and it
was resumed with skip-done logic — all 18 legs exit 0.

Correctness (all 18): BMP SHAs exact both anchors
(menu `4EF1FBF6…`, triangle `A1740DE0…`), `LastFault vec=0`, counter
anchors exact (menu 33632/1402/686/686, triangle 2706/68/34),
`verifyFailures=0` everywhere, full logs identical modulo the mode
announcement/stats lines + frame path (`-Compare` PASS).

Median walls (seconds, sequential, shared host):

| Title | cache | spec | spec/cache | specverify |
| --- | ---: | ---: | ---: | ---: |
| Menu | 166.08 | 160.43 | 1.035x | 190.84 |
| Triangle | 88.10 | 77.99 | 1.130x | 118.13 |

Menu margin (1.035x) is inside host noise (spec r3 outlier 187.9 s vs
157.2/160.4); triangle 1.13x is real but modest. Verdict: `--spec-exec`
stays opt-in, default off — the §140 provisional status is confirmed, no
default-flip. Next JIT work per the direction file: specialize semantics
further (dispatch is not dominant); DynamicMethod stays rejected.

## 142. JIT slice 5: ALU-with-memory family behind --spec-exec (2026-10-16)

Direct development, overnight slice. The §141 histogram instrument paid off
immediately: a 1B menu+triangle `--spec-exec` pair ranked fallback shapes
with both titles agreeing — Jcc/imm (control), Cmp r/m32 + Cmp m/imm32
(20M/57M and 19M/57M executions), Cli/Sti (boundary), Fld/Fstp (x87).
Jcc terminates blocks (control flow — needs a runner EIP-check design, a
later slice); Cli/Sti must keep the interpreter (IF flips change IRQ
polling at boundaries); x87 stays out. So slice 5 = the ALU-mem family.

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecAluMem` covers Add/Sub/
And/Or/Xor/Cmp/Test × (reg,mem)/(mem,reg)/(mem,imm) × 8/16/32 (63 shapes)
plus Mov mem,imm × 8/16/32 — 66 pre-specialized handlers mirroring the
interpreter bodies through the same `ReadMemory`/`WriteMemory` (fault
order, invalidation and idle-notes identical by construction). Adc/Sbb
stay out (carry live), like slices 1+2. Resolver falls through
(`?? ResolveSpecAluMem`) so existing shapes are untouched.
`DecodedBlockCache` histogram from §141 is the ongoing instrument;
CLI prints top-8 `Spec fallback` lines (spec-only output, flag-off
byte-identical). `scripts/spec-exec-soak.ps1` normalizer strips them.

Tests: `AluMem_Shapes_AtAllSizes_MatchInterpreter` (66 shapes vs the
interpreter, incl. /0–/7 group encodings and F6/F7 test forms; one
modrm fix en route — `test m,r` must address `[EBX]`, not `[EAX]`);
`Resolver_CoversOnlyTheInitialShapes` updated (mov m,imm now resolves).
Suite **1343/1343**, format clean, zero warnings.

Goldens (18-leg matrix reps 5–7, `D:\spec-soak\legs`, all PASS): SHAs
exact both anchors, `vec=0`, counters exact, `verifyFailures=0`,
full logs identical modulo mode lines. Fallbacks absorbed: menu
392M→339M, triangle 494M→374M; Cmp shapes gone from the top-8. Median
walls within-matrix: menu cache 158.66 / spec 151.69 = **1.046x**,
triangle cache 88.19 / spec 81.18 = **1.086x**. Thin again — consistent
with §138 (helpers dominate, and the new handlers still call the same
`ReadMemory`/`WriteMemory`). Remaining top fallbacks: Jcc (both
conditions, both titles), Cli/Sti, Fld/Fstp, Movq/Movntq (MMX),
Jmp/Call/Ret. Next: Jcc-specialization design (runner must detect
EIP divergence) or MMX mov shapes - measurement first, as always.
Flag stays opt-in.

## 143. JIT slice 6: near conditional jumps behind --spec-exec (2026-10-16)

Direct development, overnight slice. The slice-5 histogram top
(Jcc/imm 153M menu / 218M triangle executions) fell to a safe design:
Jcc always terminates its block, so a handler only sets EIP (taken
target or fallthrough) and the existing cursor completion ends the
block - no runner EIP check needed, no IRQ/fault/idle change (Jcc
cannot fault; NoteAccess replay is per-Step, outside bodies).

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecJcc` maps all 16
`ConditionCode`s (size-insensitive - target arithmetic is identical
for every operand size) to 16 handlers with the flag tests inlined
verbatim from `XboxCpu.EvaluateCondition` (`XboxCpu.cs:4274`) and the
`nextEip + Immediate` / fallthrough assignment verbatim from the
interpreter Jcc body (`XboxCpu.cs:448`). Other control flow
(Jmp/Call/Ret near and far) keeps the interpreter.

Tests: `Jcc_AllConditions_TakenAndFallthrough_MatchInterpreter`
(16 conditions x rel8/rel32/rel16 x 2 flag setups, branch-skips-inc
so a wrong direction shows in EAX) + `Jcc_Resolver_CoversAllConditions`.
Suite **1345/1345**, format clean, zero warnings.

Goldens (18-leg matrix reps 8-10, `D:\spec-soak\legs`, all PASS): SHAs
exact both anchors, `vec=0`, counters exact, `verifyFailures=0`,
full logs identical modulo mode lines. Jcc gone from the fallback
top-8 on both titles; fallbacks menu 339M->163M (-52%), triangle
374M->151M (-60%). Median walls within-matrix: menu cache 160.65 /
spec 150.58 = **1.067x**, triangle cache 91.23 / spec 78.91 =
**1.156x** (best yet, still thin). Remaining top fallbacks:
Cli/Sti (IRQ-boundary, untouchable), Fld/Fstp/Fmul (x87),
Movq/Movntq (MMX), Jmp/Call/Ret (near control). Next candidates:
near Jmp/Call/Ret handlers (same terminator argument as Jcc) or MMX
mov shapes - the histogram decides. Flag stays opt-in.

## 144. JIT slice 7: near jumps, calls and returns behind --spec-exec (2026-10-16)

Direct development, overnight slice. Same terminator argument as the
Jcc slice: near Jmp/Call/Ret always end their block, so the handlers
mirror the interpreter bodies verbatim (`XboxCpu.cs:442` Jmp,
`:457` Call, `:467` Ret) and cursor completion ends the block.
A faulting indirect target throws before any EIP write, preserving
precise fault EIP exactly like the interpreter. Far forms keep the
interpreter.

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecJmp/Call/Ret` cover
(imm/reg/mem, none), (imm/reg/mem, none) and (none|imm, none) - 8
handlers. Report widened to top-12 `Spec fallback` lines (spec-only
output; soak normalizer already strips the prefix).

Tests: `NearControl_Shapes_MatchInterpreter` (jmp rel8/rel32/reg/mem,
call rel32/reg/mem + ret, call + ret imm16 - jumps skip an inc,
calls return onto a hlt). Fail-pre-fix discipline held again: the
3 failures on the first run were all stale premises (jmp/call/ret
resolver Nulls, the ControlMix fallback expectation, the histogram's
Jmp-only premise) - updated, then 22/22 green. Suite **1346/1346**,
format clean, zero warnings.

Goldens (18-leg matrix reps 11-13, `D:\spec-soak\legs`, all PASS):
SHAs exact both anchors, `vec=0`, counters exact, `verifyFailures=0`,
full logs identical modulo mode lines. Jmp/Call/Ret gone from the
top-12 on both titles; fallbacks menu 163M->147M, triangle 151M->136M.
Median walls within-matrix: menu cache 156.40 / spec 147.88 =
**1.058x**, triangle cache 89.68 / spec 77.43 = **1.158x** (consistent
~1.02x over the slice-6 spec medians on both titles). Newly visible
tail (top-12): Movzx r/m32, Shl/Ror/Rol r,imm32, Inc m32, x87
Fadd/Fistp/Fild, Movq/Movntq (MMX). Next: shift/rotate + movzx
immediates family, then MMX mov - histogram order. Flag stays opt-in.

## 145. JIT slice 8: shifts/rotates + zero/sign extension behind --spec-exec (2026-10-16)

Direct development, overnight slice. Histogram tail from §144:
Shl/Ror/Rol r,imm32 (~6-7M menu) and Movzx r/m32 (6.6M menu).
`ExecuteShift`/`ExecuteMoveExtend` (`XboxCpu.cs:1350,1860`) are thin
wrappers over `ShiftOps`/mask rules through `ReadOperand`/
`WriteOperand`, so per-shape handlers mirror them with direct
register/memory access and the same helper calls.

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecShift` covers
Shl/Shr/Sar/Rol/Ror x (reg,imm)/(mem,imm) x 8/16/32 (30 shapes;
CL-counted and Rcl/Rcr keep the interpreter) and
`ResolveSpecMoveExtend` covers Movzx/Movsx x (reg,reg)/(reg,mem) x
dst 16/32 x src 8/16 (16 shapes; the source width selects the
mask/sign rule exactly like the interpreter). 46 handlers.

Tests: `ShiftExtend_Shapes_AtAllSizes_MatchInterpreter` (46 shapes).
Fail-pre-fix discipline again: 2 failures, both stale premises -
and one real discovery: the D1 implicit-count shift decodes WITH an
immediate operand, so it was already covered (locked with a
resolver assert); the histogram loop now uses the CL-counted form.
Suite **1347/1347**, format clean, zero warnings.

Goldens (18-leg matrix reps 14-16, `D:\spec-soak\legs`, all PASS,
`scripts/compare-soak-reps.ps1`): SHAs exact both anchors, `vec=0`,
counters exact, `verifyFailures=0`, full logs identical modulo mode
lines. Targets absorbed (no Shl/Ror/Rol/Sar/Shr/Movzx/Movsx in the
top-12 either title); fallbacks menu 147M->124M, triangle 136M->131M.
Median walls within-matrix: menu cache 158.97 / spec 147.57 =
**1.077x**, triangle cache 85.30 / spec 81.97 = **1.041x**.

Two lessons. (1) Per-shape specialization is saturating: four slices
absorbed ~260M menu / ~360M triangle fallback executions and the
cumulative spec/cache ratio sits at ~1.06-1.16x - exactly the §138
prediction (helpers dominate, dispatch does not). The remaining cost
is the memory path (`ReadMemory`/`WriteMemory` -> translate + bus);
the next lever is a memory-path design, not more shapes. It needs
daylight scoping, not a midnight implementation. (2) Soak-binaries
must come from a FULL solution build: the first reps-14-16 matrix
silently measured slice-7 code (`dotnet test` on one project does not
refresh the CLI's `XGrid.Cpu.dll` copy) - caught by digit-identical
counters, discarded, rebuilt, re-run for real. New `Spec fallback`
tail: Movq/Movntq, x87, Inc/Dec m32, Mulss (SSE), Jmp-nothing -
control is fully specialized. Flag stays opt-in.

## 146. JIT slice 9: Inc/Dec with memory behind --spec-exec (2026-10-16)

Direct development, overnight slice. First item off the §145 tail:
Inc/Dec m8/m16/m32 are the smallest safe family left (register handlers
+ `ComputeAddress` RMW; CF-preserving `AluOps.Inc/Dec` already in
codebase; fault order identical by construction — `Eip += Length` only
after `WriteMemory` completes, exactly like the interpreter's
`Context.Eip = nextEip` at `XboxCpu.cs:919` which a faulting
`ReadOperand`/`WriteOperand` never reaches).

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecIncDec` now takes the
first `OperandKind` and covers (reg/mem × 8/16/32) — 12 handlers total;
dispatch routes both `OperandKind.Register` and `OperandKind.Memory`
(`second` is always `None`; anything else falls to null). Bodies mirror
the slice-8 shift-mem pattern through the same
`ComputeAddress`/`ReadMemory`/`WriteMemory` (invalidation rides §134;
segment override rides the `SpecShape` key like every other mem shape).

Tests: `IncDecMem_Shapes_MatchInterpreter` (6 shapes vs the
interpreter through `Execute(16)` + whole-RAM equality; `Preset` sets
CF so a CF-clobbering body fails EFLAGS; modrm `0x03/0x0B` address
`[EBX]`); `Resolver_CoversOnlyTheInitialShapes` pins the four new
NotNulls (`FF /0`, `FF /1`, `FE /0`, `FE /1`; `push m` stays null).
Fail-pre-fix anchor: the resolver pins (a differential `RunShapes`
passes on interpreter fallback, so the pins are the coverage proof).
Suite **1348/1348**, format clean, zero warnings.

Goldens (`D:\spec-soak\slice9`, full-solution build per the §145 lesson,
`--gpu sw` spec legs): triangle spec r1 PASS 85.6 s —
SHA `A1740DE0…` exact, `vec=0`, `verifyFailures=0`, draws=2706 anchor
exact; fallbacks 131M→129M with Inc/Dec gone from the top-12 tail
(remaining: Cli/Sti, x87 Fld/Fstp/Fmul/Fadd, Mulss/Cvttss2Si SSE).
Menu spec r1 PASS 197.0 s — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact; fallbacks
124M→120.8M with Inc/Dec gone from the top-12 tail (remaining top:
Cli/Sti, x87 Fld/Fstp/Fmul/Fadd/Fistp/Fild/Fst, Movq/Movntq,
Fadd-mem). Next tail: MMX Movq/Movntq (flag-free, no FPU-stack
interaction — smallest safe slice 10) then x87 as its own slice.
Flag stays opt-in — specialization remains saturated per §145, and the
memory path still needs its daylight design.

## 147. JIT slice 10: MMX 64-bit moves behind --spec-exec (2026-10-16)

Direct development, overnight slice. Second item off the §145 tail:
Movq (reg,reg/reg,mem/mem,reg) + Movntq (mem,reg) are flag-free pure
data moves through the x87-aliased MMX file — so unlike the Jcc/Jmp
terminators they may CONTINUE their block (`DecodedBlockCache.
Terminates` whitelists both mnemonics; formation + invalidation ride
§134/§136 unchanged). Bodies mirror the interpreter through the same
`ReadMmxOperand`/`WriteMmxOperand`/`WriteMemory64` helpers with EIP
advanced only after completion (precise fault EIP preserved exactly
like `XboxCpu.cs:919`).

`src/XGrid.Cpu/XboxCpuSpecExec.cs`: `ResolveSpecMovq` + the Movntq
inline case — 4 handlers. No formation change beyond the two whitelist
entries; no snapshot change; flag stays opt-in.

Tests: `MmxMov_Shapes_MatchInterpreter` (4 shapes vs the interpreter
+ whole-RAM equality; modrm `0x03` = `[EBX]`, `0xC8` = reg,reg) +
4 resolver NotNull pins. Suite **1349/1349**, format clean.

Goldens (`D:\spec-soak\slice10`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 87.3 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact (fallbacks −90K; triangle
barely uses MMX). Menu spec r1 PASS 200.1 s — SHA `4EF1FBF6…` exact,
`vec=0`, `verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
fallbacks 120.8M→105.6M (−15.2M, −12.6%) with Movq/Movntq gone from
the top-12 tail (new tail: Cli/Sti, x87 Fld/Fstp/Fmul/Fadd/Fistp/Fild/
Fst, Prefetch 1.9M); block dispatches 307M→292M (continuation works —
fewer dispatches, longer blocks). Walls within noise (menu 197.0→200.1,
triangle 85.6→87.3) — absorption only, no wall claim; saturation holds.
Remaining: x87 family (its own slice — FPU stack/TOP/tags, daylight),
SSE Mulss/Cvttss2Si, Prefetch hint. Flag stays opt-in.

## 148. JIT slice 11: Prefetch + ordering fences behind --spec-exec (2026-10-16)

Direct development, overnight micro-slice. The slice-10 menu tail's
newly visible Prefetch (1.9M) plus the three fences are the cheapest
remaining shapes: all four are interpreter nops (`ExecuteSse` breaks
without touching any operand, flag, or memory — `SseTests.
Prefetch_IsNoOp` pins the family), so one shared EIP-advance-only
handler (`SpecHintNop`, four cached fields) serves them and all four
continue their block (whitelist + resolver cases). Prefetch's memory
operand is never read, so even an unmapped address cannot fault —
exactly like the interpreter.

Tests: `Hint_Shapes_MatchInterpreter` (4 shapes vs the interpreter)
+ 4 resolver NotNull pins. Suite **1350/1350**, format clean.

Goldens (`D:\spec-soak\slice11`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 117.6 s (contention-inflated wall) — SHA
`A1740DE0…` exact, `vec=0`, `verifyFailures=0`, draws=2706 anchor
exact. Menu spec r1 PASS 223.2 s (same contention) — SHA `4EF1FBF6…`
exact, `vec=0`, `verifyFailures=0`, draws=33632 skipped=1402 anchor
exact; fallbacks 105.6M→103.7M (−1.9M, exactly the Prefetch count)
with Prefetch gone from the top-12 tail (fences never reached the
top-12 but are covered + pinned); block dispatches 291.9M→290.0M.
Walls contention-inflated — no wall claim. Remaining top: Cli/Sti
(IRQ-boundary, untouchable), x87 family (daylight slice), SSE
Mulss/Cvttss2Si/Fadd. Flag stays opt-in.

**ktest smoke re-run (2026-10-16, post-slices 9–11): PASS 7/7** in
02:14 — all rows green with §120 known deltas matching (NtResume/
NtSuspend XGrid-ahead, the rest faithful); XGrid park break at exactly
792,800,535 steps (bit-reproducible vs the §123/§136 anchors), both
logs `EndOfTests=True`. Slices 9–11 disturb nothing in the kernel suite.

## 149. JIT slice 12: scalar SSE arithmetic + converts behind --spec-exec (2026-10-16)

Direct development, overnight slice. The remaining measurable SSE tail
(Mulss 713K triangle; Fadd-mem 4.5M + Fmul + Cvttss2Si menu): scalar
Addss/Subss/Mulss/Divss/Minss/Maxss (reg,reg/reg,mem) are one verbatim
`ExecuteSseArithmetic` call each — plain host-float ops, no MXCSR or
exception behavior anywhere in the interpreter — and Cvtss2Si/Cvttss2Si
(reg,reg/reg,mem) mirror their case through the same
`ConvertFloatToInt32` helper with the truncate flag baked. 16 handlers.

Real discovery en route (fail-pre-fix proven): the 0xF3 byte is BOTH a
mandatory opcode prefix AND stamped `Rep==Rep` (`Decoder.cs:72-73,94`),
so the `Rep != None` early-rejects in `ResolveSpec` and `Terminates`
silently excluded every F3 shape — the first resolver pins failed null
before the fix, pass after. Shared gate helper
`IsMandatoryF3Scalar` (exactly the 8 slice-12 mnemonics; packed SSE,
string/REP and all others still reject/terminate) + a `rep movsb`
stays-null pin so a real repeat can never specialize. Like slice 10,
the shapes continue their block (pure straight-line data).

Tests: `SseScalar_Shapes_MatchInterpreter` (16 shapes vs the
interpreter) + 9 resolver NotNull pins + the `rep movsb` null pin.
Suite **1351/1351**, format clean.

Goldens (`D:\spec-soak\slice12`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 99.7 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Mulss/Cvttss2Si gone from
the tail, fallbacks −1.8M; block dispatches −1.8M (continuation works).
Menu spec r1 PASS 178.6 s — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact; Fadd-mem
gone from the tail, fallbacks 103.7M→101.8M (−1.9M); dispatches
290.0M→288.2M. Remaining top: Cli/Sti (untouchable) + the x87 family
alone — scalar SSE is fully absorbed. Walls within noise, no wall
claim. Flag stays opt-in; x87 stays a daylight slice.

**Block-exec cross-check (same evening):** slices 10–12 widened
`Terminates`, which also reshapes `--block-exec` blocks, so one
triangle 1B `--block-exec` leg ran (`D:\spec-soak\slice12\
triangle-block-r1`): SHA `A1740DE0…` exact, `vec=0`, draws=2706 anchor
exact. The shared whitelist change is safe under both opt-in flags.

## 151. JIT slice 13: x87 loads/stores/add/multiply behind --spec-exec (2026-10-16)

Direct development, evening slice built on the §150 scope (the daylight
caution is lifted: every subtle point verified — read-before-mutate,
zero new invalidation hooks, no reordering). `Fld/Fst/Fstp` m32/m64/m80
(+ `Fstp ST(i)`), `Fild/Fistp` m16/m32/m64, `Fadd/Fmul` (reg,reg +
reg,mem via one shared body each): 7 shared handler methods covering
~25 shapes. All continue their block (straight-line data).

Tests: `FpuMem_Shapes_MatchInterpreter` (21 shapes vs the interpreter,
stack ops behind `fld1` so they run against a real ST0) + 12 resolver
NotNull pins. Suite **1352/1352**, format clean, zero warnings.

Goldens (`D:\spec-soak\slice13`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 87.9 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; x87 mem nearly gone
(only `Fld ST(i)` + `Faddp` remain), fallbacks −8M; dispatches −2.5%.
Menu spec r1 PASS 175.6 s (fastest spec yet, single run, no claim) —
SHA `4EF1FBF6…` exact, `vec=0`, `verifyFailures=0`, draws=33632
skipped=1402 anchor exact; **fallbacks 101.8M→49.3M (−52.5M, −52%)**
with all six mem shapes gone from the tail; dispatches 288.2M→235.6M
(−18%, much longer blocks). New tail is diverse smalls: Cli/Sti,
Shl/Shr r,r (1.4M — slice 8 covered reg,imm/mem,imm only), Fld-reg,
Fxch, Movs, Faddp, Push-m, Fld1, Fsub r,m, Mul-m. Flag stays opt-in.

## 150. x87 slice scope: the last big family, filed for daylight (2026-10-16)

Read-only scope (no product change; inventory by a probe agent, verified
against `XboxCpu.cs` + `FpuState.cs` + the slice-12 menu/triangle
fallback tails). x87 is the #1 remaining fallback family by far:
**~50.8M menu** (`Fld/Mem` 12.8M, `Fstp/Mem` 11.3M, `Fmul/Reg/Mem`
6.6M, `Fadd/Reg/Mem` 4.5M, `Fistp/Mem` 4.2M, `Fild/Mem` 3.8M,
`Fmul/Reg/Reg` 2.7M, `Fst/Mem` 2.6M, `Fstp/Reg` 2.1M, `Fadd/Reg/Reg`
1.6M) and **~9M triangle** — bigger than slices 9–12 combined. The
only slice with a real chance of moving walls before the memory-path
design. It stays daylight work (FPU stack/TOP/tags + IE semantics
deserve a fresh session), this note is the build recipe.

**Why it is safe (verified, not assumed).** (i) EIP precision is free:
`nextEip` commits only at `XboxCpu.cs:919`; every memory-form case
reads before mutating any FPU state, so a faulting bus access throws
with EIP still on the member — handlers that advance EIP only after
completion mirror this exactly. (ii) Invalidation needs ZERO new
hooks: `WriteMemoryFpu80` = `WriteMemory64` + `WriteMemory`, and every
CPU store path funnels through `NoteStorePhysical` (`RawWrite:1138`,
`WriteMemory64`, `WriteMemoryBytes`) into the §134 page-drop — FPU
writes are ordinary CPU stores. (iii) No reordering anywhere: handlers
are verbatim bodies, so the MMX-aliasing hazard (`SetMmxRegister`
clobbers phys regs without moving TOP) cannot trigger. (iv) TOP lives
in SW bits 11–13 and tags auto-classify on Push/SetRegister — all data,
all mirrored.

**Recommended scope (slice 13): 6 mem shapes ≈ 88% of menu x87** —
`Fld m32/m64/m80`, `Fstp m32/m64/m80`, `Fst m32/m64/m80`,
`Fild m16/m32/m64`, `Fistp m16/m32/m64` (RoundingMode + IE-on-overflow
via the same `WriteFpuInteger` helper, truncate baked per width),
`Fadd/Fmul ST(0),m32/m64` (double read into a temp, then SetRegister —
fault precedes all mutation). Follow-up slice 14: reg-reg ALU,
`Faddp`, `Fstp ST(i)`, `Fcom/Fcomp` (CC + IE-on-NaN + conditional pop),
`Fldcw/Fnstcw/Fnstsw` (CW changes later-conversion meaning — order
with Fistp shapes needs the verbatim-mirror discipline, no batching).
Explicitly OUT: BCD (`Fbld/Fbstp`), env/state (`Fldenv/Fnstenv/
Fnsave/Frstor`), transcendentals, `Fxam/Ftst`, `Fcmovcc`.

**Acceptance gate (same as slices 9–12 plus FPU-specific rows):**
differentials at every width incl. m80 + all 4 CW rounding modes for
`Fistp` + overflow→IE/indefinite cases + 8-deep stack wraparound +
MMX-clobber sequence; resolver pins incl. a real-REP still-null guard;
`Terminates` continuation (straight-line data like slice 10) with the
2-leg 1B golden gate (SHAs, `vec=0`, `verifyFailures=0`, anchors
exact) + the `--block-exec` cross-check leg that slices 10–12
established; flag-off byte-identical; walls reported without claim
(but this is the first slice since slice 8 allowed to hope).

## 153. JIT slice 15: shifts-by-CL + Push-m + Mul/Imul behind --spec-exec (2026-10-16)

Direct development, late-evening slice taking the whole top of the
§152 tail in one go. Shifts/rotates counted by CL (the decoder stamps
D2/D3 counts as Register index 1 — verified in `DecodeShiftRm`, so the
`second == Register` gate is exactly CL): 5 shared per-mnemonic bodies
calling the same `ExecuteShift` helper the interpreter uses (count
masking + flags identical by construction). Push-m: 1 shared body
through the same `Push` helper (source read faults before ESP moves;
stack-write fault order identical since the helper sequence is shared).
Mul single + Imul all-forms: 2 shared bodies through `ExecuteMulDiv`/
`ExecuteImul` (reads precede writes in every form). 8 handlers covering
~50 shapes; no `Terminates` change needed (all four families already
continued blocks) — but the shared whitelist now serves genuinely new
traffic, so the `--block-exec` cross-check leg runs again.

En-route test maintenance: `FallbackHistogram_TalliesServeTimeShapes`
inverted to the zero-fallback form — shl r,cl was its last uncovered
shape, so the loop is now fully specialized (and the zeros double as a
regression pin). The stale `push m` and `shl r,cl` Null pins flipped to
NotNull; a new `rcl eax,cl` Null pin guards the kept-interpreter forms.

Tests: `ShiftCountCl_Mul_Shapes_MatchInterpreter` (14 shapes; shift
shapes prefix `mov cl,3` so both sides shift by a real count) + 11
resolver pins (10 NotNull + rcl-Null). Suite **1354/1354**, format clean.

Goldens (`D:\\spec-soak\\slice15`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 88.0 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Shl-r,r/Mul/Imul/Push-m
gone from the tail, fallbacks −2.0M (formation unchanged — no
whitelist delta — so dispatches identical at 352,840,133, as expected).
Menu spec r1 PASS 168.3 s (fastest menu spec yet, single run, no
claim) — SHA `4EF1FBF6…` exact, `vec=0`, `verifyFailures=0`,
draws=33632 skipped=1402 anchor exact; **fallbacks 43.5M→38.4M
(−5.1M, −12%)** with every slice-15 shape gone from the tail.
New tail: Cli/Sti (untouchable), Movs (string, stays interpreter),
Leave, Neg, Fnstsw, Fiadd, Div-r, Bswap, Not, Movss. Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 154. JIT slice 16: integer remainder + x87 integer arithmetic behind --spec-exec (2026-10-16)

Direct development, late-evening slice sweeping the §153 tail's
safe remainder. Neg (Inc/Dec-pattern mirror via `AluOps.Neg`), Not
(flag-free complement), Bswap (reg-only byte-swap), Xchg (verbatim
read-read-write-write — fault order identical by shared code shape),
Leave (same `ExecuteLeave` helper), Div/Idiv single through the shared
`SpecMulDivSingle` body (Mul's method renamed — a Div/Idiv #DE raise
transfers EIP mid-block and the serve-time `_expectedLinear` guard
redirects exactly like the control-flow handlers, so the new Div/Idiv
whitelist entries are safe; verified against `TryNext:196`), x87
integer arithmetic (`Fiadd/Fimul/Fisub/Fisubr/Fidiv/Fidivr` mem-only via
one shared body — integer read faults before ST(0) updates) and Fnstsw
(AX + m16 verbatim mirror). 15 handlers; whitelist += Div/Idiv,
Fiadd-family, Fnstsw (Neg/Not/Bswap/Xchg/Leave already continued).

Tests: `IntRemainder_Shapes_MatchInterpreter` (19 shapes; Div shapes
use small dividends — Preset EAX/EDX would overflow the quotient and
raise #DE identically on both sides, which `RunShapes` would misread as
divergence; div-mem relies on Preset values whose quotient fits u32;
idiv-mem stores 7 first; Fiadd shapes run behind `fld1`) + 18 resolver
pins. Suite **1355/1355**, format clean.

Goldens (`D:\\spec-soak\\slice16`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 86.8 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Leave/Neg/Bswap/Xchg gone
from the tail (−0.5M; new triangle tail exposes packed-SSE moves,
Rcl-imm, Fchs, Sbb-imm). Menu spec r1 PASS 167.9 s (fastest menu spec
yet, single run, no claim) — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
**fallbacks 38.4M→36.2M (−2.2M)** with every slice-16 shape gone from
the tail; dispatches 231.3M→230.4M. New tail: Cli/Sti (untouchable),
Movs (string, out), Movss-family/Rsqrtss (packed SSE), Rcl-imm,
Fcom/Fcomp/Fchs, Sbb-imm (carry-live, out by design). Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 158. JIT slice 20: carry-rotates + sign-extension + flags + cond-move + TSC behind --spec-exec (2026-10-17)

Direct development, early-morning slice closing the integer/flags
remainder. Rcl/Rcr (imm + CL, excluded from slice 8 for scope, not
safety) via shared `ExecuteShift` bodies — CF threaded identically;
Cbw/Cdq (already whitelisted, operand-size branch in-body);
Pushf/Popf (verbatim; IF effects land at existing IRQ boundaries on
both paths); Setcc/Cmovcc (condition rides the member through the
shared evaluator; Cmovcc is flags-only, no control flow); Rdtsc
(TSC advances identically per step on both differential sides). 9
handlers; whitelist += Popf/Rdtsc only (the rest already continued —
verified, no duplicates). One self-caught redundancy en route (Rcl/Rcr
routed twice — Shl-group + inserted block; removed the latter) and one
stale-guard flip (the slice-15 `rcl eax,cl` Null pin now resolves).

Tests: `FlagsMisc_Shapes_MatchInterpreter` (14 shapes; popf runs
behind pushfd so both sides restore identical flags) + 13 resolver
pins. Suite **1359/1359**, format clean.

Goldens (`D:\\spec-soak\\slice20`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 96.9 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Rcl-imm gone (−0.2M).
Menu spec r1 PASS 184.2 s — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
**fallbacks 34.6M→34.1M (−0.5M)** with Rcl-imm/Cdq/Pushf/Popf/Setcc
gone (newly exposed: Cmpxchg8B, Fldcw/Fnstcw, Fwait, Shufps). New tail:
Cli/Sti (untouchable), Movs/Stos (string, out), Sbb/Adc (carry-live,
out), Cmpxchg8B, Fldcw/Fnstcw/Fwait, Shufps. Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 159. Memory-path scope: the next lever, filed from a probe (2026-10-17)

Read-only scope (no product change; inventory by a probe agent against
`XboxCpu.cs` + `DecodedBlockCache.cs` + `Mmu.cs` + `AddressSpace.cs` +
the slice-20 menu golden). Shape work is now in extreme diminishing
returns (every remaining tail row is <100k or a deliberate out:
Cli/Sti, Movs/Stos, Sbb/Adc, Cmpxchg8B, FPU control, Shufps) — the
§138 verdict stands and the numbers now prove the mechanism: slice-20
menu runs 961M specialized handler invocations against 228.8M block
dispatches moving 995.3M instructions (~4.35 insn/dispatch, 96.6%
specialized, fallbacks 3.4% topped by untouchables). Dispatch is not
the cost; the per-touch memory path is.

**Common-case cost (`ReadMemory32`, MMU on, no page cross):** ~6-7 small
calls + 1 `Mmu.Translate` + 1 RAM touch — `ComputeAddress`
(base+index+disp), `IsEnabled` + `CrossesPage` prologue, `TranslateLinear`
(translate + fault branch + idle-note), `RawRead` → `AddressSpace.Read32`
→ `PhysicalMemory.Read32` (span + endian), plus the profiler pair when
hooked. Writes add `NoteStorePhysical` (page-drop) + `CheckWatch`/
`NoteGuestWrite`. Confirmed: every interpreter + all ~20 spec slices
funnel through the same 4-6 helpers (`Read/WriteMemory`,
`Read/WriteMemory64`, `ComputeAddress`, `RawRead/RawWrite`) — zero direct
bus touches from spec code — so optimizing the helpers covers everything.

**Options (with hazards):** (1) TLB-hit inline / last-page cache —
subscribe to the existing flush set (`OnMmuFlush/Page`, page-table-page
stores already full-flush); faults must stay exact so the generation
must cover PTE stores. (2) Identity-translation fast path for pinned
low-RAM pages — must still prove access rights (validate-once-per-page
verdict cache) and stay RAM-only (MMIO Region hits + unmapped counters
must keep firing). (3) Span/burst path for 64/F80/Bytes/push/pop/string
— one translate + `Ram.GetSpan` with whole-span RAM pre-check;
write path must still run per-page `NoteStorePhysical` + present-dirty
(aliases `0x80000000`/mirror/WC: translate first, span second, never
span the linear address) + profiler notes or golden counters drift.

**Gate for the build slice:** median-of-3 cache-vs-spec on both titles
(the median20 matrix running tonight is the pre-change baseline) +
`verifyFailures=0` + flag-off byte-identical + counter anchors exact;
walls reported before any default-flip talk. Not tonight's work —
this note is the recipe.

## 160. Median-of-3 post-slices-13–20: spec/cache 1.072x menu / 1.185x triangle (2026-10-17)

12-leg matrix (`D:\\spec-soak\\median20`, cache+spec × 3 reps × 2 titles,
full-solution build, undisturbed overnight run — only light file reads
alongside): all 12 legs PASS (SHAs exact both titles, `vec=0`,
counter anchors exact, `verifyFailures=0` everywhere, so the matrix
doubles as a 6-rep golden re-validation of slices 13–20).

Walls (median-of-3):
- menu cache 221.58 / 227.28 / 221.48 → median **221.58 s**;
  spec 206.75 / 201.25 / 207.47 → median **206.75 s** → **spec/cache 1.072x**.
- triangle cache 118.49 / 127.68 / 115.98 → median **118.49 s**;
  spec 101.27 / 99.75 / 99.99 → median **99.99 s** → **spec/cache 1.185x**.

Vs §141 (menu 1.035x, triangle 1.130x, slices 1+2 only): slices 5–20
bought **+3.7 points menu / +5.5 points triangle** — the first measured
wall movement since slice 8, and it tracks the fallback absorption
(menu 392M→34.1M, −91%; triangle to carry/string/privileged residue).
Triangle-cache r2 (127.68 s vs 116–118 s siblings) shows host noise is
real; medians are robust to it. Verdict: real but thin — flag stays
opt-in; default-flip needs the memory-path build (§159) first, then a
fresh median-of-3. No wall claim beyond the ratio itself.

## 161. Memory-path slice 1: RAM-direct fast path + wide-access single-translate (2026-10-17)

Direct development, early-morning build from the §159 recipe (first of
three options; TLB-inline and span/burst remain). All in
`XboxCpu.cs`, no resolver/whitelist change (no new shapes — this is
pure data-path cost removal, active in every mode including flag-off):

- **RawRead/RawWrite RAM-direct:** when the bus is the standard
  `AddressSpace` (cached at construction; snapshots reload RAM in
  place so it never stales), physical accesses inside RAM skip the
  `IMemoryBus` dispatch + region-table branch and call the same
  `Ram.Read*/Write*` the bus would. Reads are side-effect-free;
  writes keep `NoteStorePhysical` first and fall back whenever the
  `AddressSpace` write watch could fire (watch check is pure).
- **64-bit direct:** `Read/WriteMemory64` do one `Ram.Read/Write64`
  instead of two bus `Read/Write32` (identical LE combine; no note
  counts change anywhere on these paths, so no gate needed).
- **XMM128/FPU80 single-translate:** two translates → one + direct
  Ram halves, guarded by one-page containment (the second translate
  can only fault on a different page), RAM range re-checked
  post-translate (PTEs may point at MMIO), write-watch fallback, and
  the `FastWideAllowed` gate (observers-null: translate-count change
  would drift idle/TLB tallies otherwise). `NoteStorePhysical(128/80)`
  on one page drops exactly the page the legacy double-drop hit.

Deliberately OUT (follow-ups): `Read/WriteMemoryBytes` bulk path
(Fxsave/Fnsave env — rare, per-byte exact today), RMW
compute-address-once (saves little; fault-priority subtleties),
`AggressiveInlining` annotations (unmeasured).

Tests: `MemoryFastPathTests` (8): RAM32/XMM128/FPU80 round trips with
paging on, XMM128 + FPU80 page-cross fallback exactness, MMIO-physical
fallback with identical 4×32 device traffic each way, write-watch
still firing, and the observer gate pin (cold-TLB XMM128 load reports
exactly 2 singular TLB notes under a profiler). Suite **1367/1367**,
format clean.

Goldens (`D:\\spec-soak\\slice21`, full-solution build, `--gpu sw`):
triangle spec r1 PASS 102.1 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact, dispatches identical to
slice20 (formation untouched). Menu spec r1 PASS 203.8 s — SHA
`4EF1FBF6…` exact, `vec=0`, `verifyFailures=0`, draws=33632
skipped=1402 anchor exact. Menu cache r1 PASS 230.9 s SHA exact
(flag-off byte-identical — the fast path is mode-independent and
bit-exact). `--block-exec` cross-check PASS (SHA `A1740DE0…` exact).
Walls are single-run gate checks only (menu 203.8 vs 184.2 slice20,
triangle 102.1 vs 96.9 — host noise dominates single runs; the §160
median stands as the wall reference, fresh median-of-3 comes after
the remaining memory-path slices). Flag stays opt-in.

## 157. JIT slice 19: SSE sqrt/rcp + packed arithmetic/logicals/compares behind --spec-exec (2026-10-17)

Direct development, early-morning slice through the shared-helper
pattern at its most leveraged: 5 shared bodies covering 20 mnemonics —
Sqrtps/Sqrtss/Rsqrtps/Rsqrtss/Rcpps/Rcpss (`ExecuteSseUnary`),
Addps/Subps/Mulps/Divps/Minps/Maxps (`ExecuteSseArithmetic`, whose
packed loop is as verbatim as the slice-12 scalar path),
Andps/Andnps/Orps/Xorps (`ExecuteSseLogical`), Cmpps/Cmpss
(`ExecuteSseCompare`), Comiss/Ucomiss (`ExecuteSseCompareScalar`,
EFLAGS-only, no control flow). `IsMandatoryF3Scalar` gains four more
members (Sqrtss/Rsqrtss/Rcpss/Cmpss); whitelist += all 20.

Tests: `SseCompute_Shapes_MatchInterpreter` (16 shapes, F3 forms
proving the extended gate) + 14 resolver pins. Suite **1358/1358**,
format clean.

Goldens (`D:\\spec-soak\\slice19`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 85.6 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Rsqrtss gone (−0.2M).
Menu spec r1 PASS 165.2 s (fastest menu spec yet, single run, no claim)
— SHA `4EF1FBF6…` exact, `vec=0`, `verifyFailures=0`, draws=33632
skipped=1402 anchor exact; **fallbacks 34.8M→34.6M (−0.2M)** with
Rsqrtss gone (newly exposed: Setcc r8). New tail: Cli/Sti
(untouchable), Movs/Stos (string, out), Rcl-imm, Sbb/Adc (carry-live,
out), Cdq, Pushf/Popf, Setcc. Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 156. JIT slice 18: packed-SSE moves behind --spec-exec (2026-10-17)

Direct development, early-morning slice taking the packed-move tail.
Movaps/Movups (reg,reg/reg,mem/mem,reg via the same
`ReadXmmOperand`/`WriteXmmOperand` helpers), Movss (all three forms —
scalar low-element move with the F3 mandatory prefix, so
`IsMandatoryF3Scalar` gains its ninth member with comment updated),
Movlps/Movhps (both directions, including the reg,reg fall-through
untouched on both paths exactly like the interpreter), Movhlps/Movlhps
(pure register), Movntps (hint ignored on both paths — plain store),
Movmskps (verbatim sign-mask loop). 9 handlers; whitelist += all 9.
One compile-time typo (Movskps) caught by the build, zero test impact.

Tests: `SseMove_Shapes_MatchInterpreter` (17 shapes vs the
interpreter on shared zero XMM state + Preset [EBX]) + 14 resolver
pins. Suite **1357/1357**, format clean.

Goldens (`D:\\spec-soak\\slice18`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 89.7 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; every packed move gone
(−0.8M). Menu spec r1 PASS 167.6 s — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
**fallbacks 35.7M→34.8M (−0.8M)** with all Movss gone (newly exposed:
Popf, Sbb r,m, Stos smalls). New tail: Cli/Sti (untouchable), Movs/
Stos (string, out), Rsqrtss, Rcl-imm, Sbb/Adc (carry-live, out), Cdq,
Pushf/Popf. Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 155. JIT slice 17: x87 compares + sign/test + tag control behind --spec-exec (2026-10-16)

Direct development, late-evening slice completing the x87 data family
(the §150 scope's last register-safe rows). Fcom/Fcomp/Fucom/Fucomp
(ST(i) + ST(0),mem via one shared body — operand read into a temp
before `SetFpuCompare` touches status bits, conditional pop only after
success), Ficom/Ficomp (mem-only, same discipline with
`ReadFpuInteger`), Fchs/Fabs/Ftst (pure ST(0) ops), Ffree/Ffreep
(tag-only + optional pop), Fdecstp/Fincstp (TOP wrap), Fnclex/Fninit
(exception control). 16 handlers; whitelist += all 16. Explicitly still
OUT (daylight): transcendentals, Fcmovcc, Fcomi (EFLAGS-writing),
Fldcw/Fnstcw/env/state, BCD.

Tests: `FpuCompare_Shapes_MatchInterpreter` (16 shapes vs the
interpreter, compares behind `fld1` against real stack values) + 15
resolver pins. Suite **1356/1356**, format clean.

Goldens (`D:\\spec-soak\\slice17`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 85.1 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Fchs gone (−0.2M, x87 fully
absorbed on triangle — remaining tail is Cli/Sti, packed-SSE,
Rcl-imm, Sbb-imm). Menu spec r1 PASS 169.9 s — SHA `4EF1FBF6…` exact,
`vec=0`, `verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
**fallbacks 36.2M→35.7M (−0.5M)** with Fcom/Fcomp/Fchs gone (newly
exposed: Adc-imm, Cdq, Pushf smalls). Flag stays opt-in.
Block-exec cross-check PASS (`triangle-block-r1`: SHA `A1740DE0…` exact,
`vec=0`, draws=2706 anchor exact).

## 152. JIT slice 14: x87 reg-forms + Fsub/Fdiv families behind --spec-exec (2026-10-16)

Direct development, late-evening slice completing the §150 scope's
register half. `Fld ST(i)`, `Fst ST(i)` (same verbatim body — 
`WriteFpuReal` handles both kinds), `Fxch`, all 7 constant loads
(`Fld1/Fldl2T/Fldl2E/Fldpi/Fldlg2/Fldln2/Fldz`), the 6 pop-arithmetic
forms (`Faddp/Fmulp/Fsubp/Fsubrp/Fdivp/Fdivrp` via one shared
read-read-write-pop body) and the 4 missing ST(0) families
(`Fsub/Fsubr/Fdiv/Fdivr` reg,reg + reg,mem via the existing shared
`SpecFpuArithmetic` — `ApplyFpuArithmetic` already mapped them, only
the resolver excluded them). All continue their block (pure register
ops or verbatim mirrors); no faults possible except reg,mem arithmetic
which reads into temps first.

Tests: `FpuReg_Shapes_MatchInterpreter` (18 shapes vs the interpreter,
all behind `fld1` prefixes against real stack values) + 13 resolver
NotNull pins. Suite **1353/1353**, format clean.

Goldens (`D:\\spec-soak\\slice14`, full-solution build, `--gpu sw` spec
legs): triangle spec r1 PASS 102.0 s — SHA `A1740DE0…` exact, `vec=0`,
`verifyFailures=0`, draws=2706 anchor exact; Fld-reg/Faddp gone from
the tail, fallbacks −2.2M; dispatches −1.3M. Menu spec r1 PASS 204.1 s
(contention-inflated wall) — SHA `4EF1FBF6…` exact, `vec=0`,
`verifyFailures=0`, draws=33632 skipped=1402 anchor exact;
**fallbacks 49.3M→43.5M (−5.8M, −12%)** with every slice-14 shape
gone from the tail; dispatches 235.6M→231.3M. New tail: Cli/Sti
(untouchable), Shl/Shr r,r (CL-count forms — slice 15), Movs (string,
stays interpreter), Push-m, Mul-m, Leave, Neg, Fnstsw, Fiadd. Flag
stays opt-in.

## 162. Memory-path slice 2: Read/WriteMemoryBytes bulk path (2026-10-17)

Direct development, second build from the §159 recipe (Bytes bulk path;
RMW compute-once stays OUT — the hot spec handlers already hoist
`uint address` once per handler, and the interpreter generic path keeps
its read/write fault-priority semantics). All callers move ≤512 B
(Fxsave/Fxrstor 512, Fnsave/Frstor 94/108, Fldenv/Fnstenv 14/28), so
every op touches ≤2 pages.

- **MMU-off:** whole-range RAM + no-watch → one span copy (writes: per-
  page `NoteStorePhysical` + new `PhysicalMemory.WriteSpan`, which runs
  the same present-dirty choke the N per-byte `Write8` calls hit — the
  choke is range-based so one call is exact); otherwise the legacy
  per-byte bus loop (MMIO Region traffic + unmapped counters preserved).
- **MMU-on (FastWideAllowed-gated, same observers-null gate as slice
  1):** page-bounded chunks, one translate per chunk + span copy for RAM
  chunks, exact per-byte legacy loop for non-RAM-physical chunks.
  Fault behavior identical: a chunk-start translate can only fault for
  its own page, so the first-failing linear matches the legacy
  first-failing byte, and already-copied chunks match the legacy partial
  fill (writes keep translate→store→drop order per chunk). Watch
  overlap falls back per chunk (per-byte WatchHit traffic preserved).
- **Fxsave 512 B: 512 translates + 512 bus dispatches → 1–2 translates
  + span copies** on the RAM fast path.

Tests: 5 new in `MemoryFastPathTests` (Fxsave/Fxrstor round trip paging
on incl. image-magic check, page-crossing two-chunk exactness, MMIO
fallback with 512×size-1 device traffic each way, write-watch 512
per-byte hits, observer gate pin at 512 singular translates). Suite
**1372/1372** at that point, format clean.

## 163. Memory-path slice 3: 4-entry data-translate cache (2026-10-17)

Direct development, third build from the §159 recipe (option 1,
TLB-hit inline — estreito: a 4-entry direct-mapped VPN→(4 KB-slice
base + PTE-writable) memo inside `TranslateLinear`, not a second TLB).
A hit skips the two Translate call layers + SoftTLB lookup and replays
the SoftTLB-hit branch exactly (WP-gated writability fault with the
same P=1/W=1 data error code 3); a miss runs the full core with
identical side effects via the new `Mmu.TranslateDataFast` wrapper
(same `TranslateCore`, verdict threaded out, observer notes off) and
populates one slot. Fetch (`isInstruction`) never consults it; the
idle-skip access note fires once per call on both paths; only the
Mmu-observer TLB notes are skipped, covered by the shared
`FastWideAllowed` gate (profiler attached → legacy tallies exact).

- **Coherence proof (no new discipline needed):** a cache hit implies
  the SoftTLB would hit too — any access evicting the SoftTLB entry
  also evicts this strictly-coarser slot (SoftTLB index vpn&255,
  mine vpn&3; collisions in the former always collide in the
  latter). So the memo can never return anything the SoftTLB walk
  path wouldn't. Invalidation still mirrors every SoftTLB flush
  (`OnMmuFlush`/`OnMmuFlushPage`) plus the page-table-page store hook
  as belt-and-braces.
- **Hole found and closed en route:** `Mmu.PageTableTrackingEnabled`
  defaulted off and `--no-decode-cache` turned it off, which would
  blind the store hook. Tracking is now enabled at `XboxCpu`
  construction and the decode setter no longer disables it (host-side
  only, no guest effect; no tests depended on the toggle).

Tests: 3 new (guest-PTE-store + INVLPG remap observes the new mapping;
WP fault on the cached hit traps to vector 14 with Cr2 + error code 3
— fail-pre-fix proven by corrupting the verdict to writable=true;
observer gate pin at 2 singular notes for two cold stores). Suite
**1375/1375**, format clean.

Joint 4-leg golden gate (`D:\\spec-soak\\slice22`, full-solution build,
`--gpu sw`, undisturbed except light file reads): triangle spec r1
PASS 98.9 s — SHA `A1740DE0…` exact, `vec=0`, `verifyFailures=0`,
draws=2706; menu spec r1 PASS 187.9 s — SHA `4EF1FBF6…` exact,
`vec=0`, `verifyFailures=0`, draws=33632 skipped=1402; menu cache r1
PASS 207.4 s SHA exact (flag-off byte-identical — both slices are
mode-independent and bit-exact); triangle `--block-exec` cross-check
PASS (SHA `A1740DE0…` exact, `vec=0`, `verifyFailures=0`,
draws=2706). Walls are single-run gate checks only (directionally
faster than slice21's 102.1/203.8/230.9, but host noise dominates
single runs — the §160 median stands; fresh median-of-3 next). Flag
stays opt-in.


## 164. Median-of-3 post-memory-path-slices-1-3: spec/cache 1.070x menu / 1.164x triangle (2026-10-17)

12-leg matrix (`D:\spec-soak\median22`, cache+spec x 3 reps x 2 titles,
full-solution build, undisturbed run): all 12 legs PASS (SHAs exact both
titles, `vec=0`, counter anchors exact, `verifyFailures=0` everywhere --
the per-leg script gate checks each; the `-Compare` log-identity step
was not run because it expects specverify legs the matrix omits by
design, same 12-leg shape as section 160).

Walls (median-of-3):
- menu cache 205.07 / 209.04 / 203.80 --> median **205.07 s**;
  spec 191.60 / 191.59 / 186.35 --> median **191.59 s** --> **spec/cache
  1.070x**.
- triangle cache 110.41 / 115.99 / 113.14 --> median **113.14 s**;
  spec 90.34 / 110.61 / 97.17 --> median **97.17 s** --> **spec/cache
  1.164x** (spec r2 110.61 vs 90-97 siblings is host noise of the same
  kind as section 160's cache r2; medians are robust to it).

Vs section 160 (menu 1.072x, triangle 1.185x): ratios essentially flat
(-0.2/-2.1 points, within noise). The slices' win is ABSOLUTE and
mode-independent, exactly as designed: menu cache 221.58-->205.07
(-7.5%), menu spec 206.75-->191.59 (-7.3%), triangle cache
118.49-->113.14 (-4.5%), triangle spec 99.99-->97.17 (-2.8%). Slices 1-3
bought ~7% menu / ~3-4% triangle on BOTH modes -- the RAM-direct,
bulk-span and translate-cache costs were never spec-specific.

**Default-flip decision: flag stays opt-in.** The flip calculus is the
ratio, and the ratio hasn't moved: spec's edge is still thin
(+7%/+16%) for the extra code-path surface it carries. The remaining
levers are (a) the rep-string span path (section 159 option 3 remainder --
median22 tail shows Movs 981,712 + Stos 56,886 interpreter dispatches,
each iterating ECX bytes at 2 translates per byte), (b) block-executor
dispatch share (section 136: 30.9-36.6%), (c) a machine-code emitter
(section 138 -- the actual lever; everything so far is specialization).
No wall claim beyond the ratios themselves. ktest smoke PASS 7/7
alongside (02:12, known deltas, park-break reproducibility unbroken).

## 165. Memory-path slice 4: rep movs/stos span path (2026-10-17)

Direct development, fourth build from the section 159 recipe (option 3
remainder -- the median22 tail showed Movs 981,712 + Stos 56,886
interpreter dispatches, each iterating ECX bytes at two translates per
byte). `ExecuteStringBulk` in `XboxCpu.cs`, hooked into `ExecuteString`
for Rep-prefixed ZF-independent moves only (Cmps/Scas exit early on ZF;
Lods/Ins/Outs touch registers or IO per iteration -- legacy loop kept).

- Paging on (FastWideAllowed-gated): page-bounded chunks with one
  translate per side per chunk + direct RAM element ops in legacy visit
  order (overlap/aliasing exact by order -- the overlap test pins the
  iterative [0,1,0,1...] result a naive span copy would get wrong);
  straddling elements take one exact legacy iteration; non-RAM or
  watched chunks fall back per chunk (MMIO traffic, watch hits,
  unmapped counters preserved); indices/counter commit per chunk so
  fault partial states match legacy.
- Paging off: whole-range RAM + no-watch check, then the same chunked
  move with per-page decode drops (no translates, no faults possible).
- 16-bit address size falls back on any 64K wrap; 32-bit on any 4 GB
  wrap; ECX=0 is a no-op in both paths.

**Real bug caught by the golden gate (not by unit tests):** the first
version advanced ESI on `rep stos`, which the legacy loop never touches
-- all 26 unit tests passed, but the triangle boot halted at 6.6M steps
(EIP 0x0040079E) while the --hotspot run (bulk force-off) sailed to 10M.
Fixed (per-mnemonic register commit), verified by a 10M-step probe with
EIP bit-identical to the legacy path, and pinned by a new ESI-
preservation test. Lesson: register-touch asymmetry is the string-op
fault-priority equivalent -- the gate earns its keep.

Tests: 10 new (forward/multi-page/backward-DF1/overlap-order/fill/
zero-count/MMIO-fallback/watch-fallback/addr16-wrap/observer-gate +
ESI pin). Suite **1385/1385**, format clean. 4-leg golden gate
(`D:\spec-soak\slice23`, full-solution rebuild -- the earlier `dotnet
test` does NOT rebuild the CLI, so the first slice23 pass ran stale
binaries and was redone): triangle spec 82.2 s + menu spec 154.7 s
SHAs exact, `vec=0`, `verifyFailures=0`, anchors exact; menu cache
166.0 s SHA exact (flag-off byte-identical); triangle `--block-exec`
SHA `A1740DE0...` exact, draws=2706. Walls single-run only -- and note
the menu-cache 207-->166 drop happened between runs of IDENTICAL
binaries (host shift, not code). The section 164 median stands. Flag
stays opt-in.

## 166. Median-of-3 post-slice-4: spec/cache 1.088x menu / 1.161x triangle (2026-10-17)

12-leg matrix (`D:\spec-soak\median23`, cache+spec x 3 reps x 2 titles,
full-solution build with slice 4, undisturbed run): all 12 legs PASS.

Walls (median-of-3):
- menu cache 166.54 / 158.89 / 156.36 --> median **158.89 s**;
  spec 146.10 / 154.21 / 145.71 --> median **146.10 s** --> **spec/cache
  1.088x**.
- triangle cache 92.44 / 87.92 / 91.62 --> median **91.62 s**;
  spec 80.57 / 78.59 / 78.93 --> median **78.93 s** --> **spec/cache
  1.161x**.

Vs section 164 (menu 1.070x, triangle 1.164x): triangle flat (-0.3
points); menu +1.8 points, which is exactly the arithmetic of
mode-INDEPENDENT savings (equal absolute seconds off both modes raises
C/S when C>S) -- but NO attribution claim is made: absolutes fell
~20% between matrices on IDENTICAL binaries (slice23-stale menu/cache
166.9 vs median22 ~205), so a host shift dominates and slice-4's share
is unmeasurable from walls. The robust signals are the ratios (both
modes share the host) and the engagement probe (section 165: bulk fires
for Movs+Stos, both paging modes, up to 6144-iteration runs).

Gate-integrity fix alongside: the soak script hashed a STALE bmp when
no frame was produced (the slice-4 bug run "passed" SHA on a leftover
file; only the counter anchor caught it). The script now deletes the
bmp before the run, fails on `Frame unavailable`, and requires the bmp
to exist. No product code touched.

## 167. Dispatch slice 5: Cli/Sti continue blocks (2026-10-17)

Direct development, first slice at the block-dispatch share (section
136: 30.9-36.6%). The slice23 tails showed Cli/Sti are 95-100% of ALL
remaining fallbacks (triangle 113.3M of 113.3M, menu 32.5M of 34.1M) --
and since unlisted mnemonics terminate blocks, every one of those
~145M executions was a 1-instruction boundary dispatch.

Two-token change (`DecodedBlockCache.Terminates` continue-list + doc):
Cli/Sti stay fallback-interpreted, blocks just merge across them.
Behavior-neutral BY CONSTRUCTION, not just by gate: interrupt delivery
is polled before every instruction in both `Step` and `RunSpecBlock`,
so no delivery point moves (the STI 1-insn shadow was already not
modeled -- delivery fires at the next poll either way).

Measured (slice23 --> slice24, identical guest code):
- dispatches triangle 351.6M --> 238.5M (-113.1M, -32%); menu 228.8M
  --> 196.4M (-32.4M, -14%). Exactly the Cli+Sti boundary count.
- instructions, shape hits/fallbacks, builds: identical.
- `Interrupts delivered` IDENTICAL both titles (triangle 1883, menu
  3153) -- delivery timing provably unchanged at 1B steps.
- SHAs exact everywhere (see gate below).

Tests: 5 new `BlockFormationTests` (Cli/Sti continue pins, Ret/Hlt
contrast pins, IF toggle/clear functional under enabled blocks).
Suite **1390/1390**, format clean. 4-leg golden gate
(`D:\spec-soak\slice24`, full-solution rebuild BEFORE the legs -- the
slice23 staleness lesson is now procedure): triangle spec 77.2 s +
menu spec 145.4 s SHAs exact, `vec=0`, `verifyFailures=0`, anchors
exact; menu cache 158.3 s SHA exact (control leg -- Step path
untouched, as designed); triangle `--block-exec` SHA `A1740DE0...`
exact, draws=2706. Walls single-run only (directionally -6% both spec
legs, consistent with -14/-32% fewer dispatches at a ~30% dispatch
share -- but no claim; the next median owns wall truth). Flag stays
opt-in.

## 168. Shape slice 6: Cli/Sti spec handlers (2026-10-17)

Direct development, complement to slice 5: slice 5 merged the blocks
(the boundary dispatches are gone) but every Cli/Sti still paid the
fallback body (interpreter `ExecuteInstruction` switch + the
`NoteFallbackShape` dictionary). Two verbatim handlers (`SpecCli`/
`SpecSti`: IF bit + EIP advance) + resolver cases + 2 pins turn the
entire 95-100% fallback volume into shape hits.

Measured (slice24 --> slice25, identical guest code):
- triangle fallbacks 113,328,528 --> 184,872 (-99.84%); menu
  34,146,625 --> 1,700,771 (-95%). Cli/Sti gone from both tails.
- new tails: triangle Sbb-imm 87k / Movs 36k / Punpcklwd+hwd 25k /
  Stos 8k; menu Movs 982k (rep dispatches -- bulk-covered inside) /
  Sbb+Adc ~330k / Stos 57k. The carry-live and string shapes stay
  deliberate outs; MMX unpacks are noise.
- instructions, interrupts (1883/3153), builds: identical.

Tests: `CliSti_Shapes_MatchInterpreter` (4 shapes incl. both orders)
+ 2 resolver pins. Suite **1391/1391**, format clean. 4-leg golden
gate (`D:\spec-soak\slice25`, full-solution rebuild first):
triangle spec 73.0 s + menu spec 142.8 s SHAs exact, `vec=0`,
`verifyFailures=0`, anchors exact; menu cache 160.3 s SHA exact
(Step path untouched -- Cli/Sti handlers only resolve in block/spec
modes); triangle `--block-exec` SHA `A1740DE0...` exact, draws=2706.
Walls single-run only. Flag stays opt-in.

## 169. Median-of-3 post-slices-5+6: spec/cache 1.084x menu / 1.231x triangle (2026-10-17)

12-leg matrix (`D:\spec-soak\median25`, cache+spec x 3 reps x 2 titles,
full-solution build with slices 5+6, undisturbed run): all 12 legs PASS.

Walls (median-of-3):
- menu cache 157.06 / 156.15 / 156.28 --> median **156.28 s**;
  spec 144.16 / 143.12 / 145.42 --> median **144.16 s** --> **spec/cache
  1.084x**.
- triangle cache 89.88 / 86.70 / 90.21 --> median **89.88 s**;
  spec 72.84 / 73.00 / 75.65 --> median **73.00 s** --> **spec/cache
  1.231x**.

Vs median24 (menu 1.054x, triangle 1.124x): triangle +10.7 points, menu
+3.0. This one reads as REAL, not host shift, for three converging
reasons: (1) the gains are spec-ONLY (triangle spec -4.8%, menu spec
-1.1%) while the cache controls -- byte-identical code paths slices 5+6
cannot touch -- drifted the OTHER way (+4.2%/+1.7% noise); (2) the
magnitudes track the fallback volumes slice 6 removed (triangle 113M
vs menu 34M bodies); (3) triangle-spec legs are tight (72.8/73.0/75.6)
and cleanly below median24's (80.0/76.4/76.7). Slice 5's dispatch cut
and slice 6's fallback-body cut compose exactly where predicted.
Median24's menu 1.054 now looks like the outlier; the menu ratio is
back at its section-166 level (1.084 vs 1.088).

Standing: flag stays opt-in (1.08-1.23x still thin for a default flip;
the emitter of section 138 remains the lever). The specialization
program is declared at its asymptote: remaining tails are carry-live
(deliberate outs), bulk-covered strings, and noise-floor unpacks;
the per-dispatch remainder (~7ns dict lookups) was scoped and rejected
as sub-noise (section 167 deliberation). Next wall truth (if any
further slice lands) needs a fresh median -- single runs have been
proven unreliable twice tonight.

## 170. Shape slice 7: Adc/Sbb + FPU-control + Emms/Fwait behind --spec-exec (2026-10-17)

Direct development, from the 3B deep-run tail (section 165 follow-up):
Adc/Sbb 3.4M + Fldcw/Fnstcw/Fwait ~1M + Emms 306k -- the largest
unspecialized volume left after slice 6 (Movs/Stos are bulk-covered;
Cmpxchg8B/Shufps stay deliberate outs). The old "carry-live, out" label
(phase-15 era) was stale: handlers read CF live from EFLAGS exactly
like the interpreter's `ExecuteAlu`, so multi-instruction carry chains
observe identical traffic on both paths.

- 30 handlers: Adc/Sbb x RegReg/RegImm/RegMem/MemReg/MemImm x 8/16/32,
  verbatim Add/Sub mirrors with the live carry bit.
- 4 one-liners: Fldcw/Fnstcw (m16, fault-order exact), Fwait (nop),
  Emms (`Fpu.EmptyAll`).
- Resolver rows in `ResolveSpecAlu`/`ResolveSpecAluMem` (whose doc still
  claimed Adc/Sbb unresolved -- corrected); Fldcw/Fnstcw/Fwait/Emms join
  the `Terminates` continue-list (straight-line, EIP-precise).

Tests: `AdcSbb_Shapes_MatchInterpreter` (44 shapes: every form/size,
carry=1 from the CF-set preset plus clc-prefixed carry=0 32-bit twins;
Fldcw/Fnstcw/Fwait/Emms) + 34 resolver pins; 2 stale `Null` pins
(adc/sbb carry-live) removed. Suite **1392/1392**, format clean.
4-leg golden gate (`D:\spec-soak\slice26`, full-solution rebuild
first): triangle spec 74.4 s + menu spec 144.3 s SHAs exact, `vec=0`,
`verifyFailures=0`, anchors exact; menu cache 155.1 s SHA exact
(control leg); triangle `--block-exec` SHA `A1740DE0...` exact,
draws=2706. Fallbacks triangle 185k-->90k (-52%), menu 1.70M-->1.22M
(-28%); interrupts identical 1883/3153. Walls single-run only. Flag
stays opt-in.

## 171. First codegen: DynamicMethod block-tail emitter behind --emit (2026-10-17)

Direct development, per the section-138 recipe (block-granular execution
first, DynamicMethod only when specialization is attempted -- specialization
is now at asymptote after slices 5-20 + memory-path 1-4 + dispatch 5-6 +
shape 7, so the emitter is the next lever). E1 serves members 1..N-1 of
each fully-specialized block through a per-head cached DynamicMethod with
direct static calls (no per-member TryNext cursor, no delegate Invoke);
member 0 always runs the existing member path so head lookup, formation
and all head counting are untouched.

Boundary preservation (per member, in emitted order): budget cap, live
generation check (self-modification), baked-EIP check (redirects), TSC++,
IRQ poll at the Step site (a pending IRQ reports its consumed vector and
bails; the C# wrapper runs the exact member-path transfer), then the
direct static call. Faults propagate through the emitted frames to the
wrapper catch, which replays the Step catch body verbatim (CR2, vector 14,
EIP, error code, transfer-or-throw) with the exact served count from the
progress array. The wrapper skips emission whenever idle-skip, hotspot
observation, or either verify mode is active, so those paths are
byte-identical by construction; invalid members can never enter (only
AllSpecialized blocks emit, and ResolveSpec rejects invalid). Counters are
kept exact: decode NoteHit per served member plus Instructions/ShapeHits/
SpecRan/EmitRan advances and cursor/completion bookkeeping in
NoteEmittedRan; redirects replicate the TryNext cursor-drop; invalidation
(DecodeCache drops/flushes, cap eviction, snapshot load via Mmu.Flush)
clears the emit table through the single Invalidate choke point with a
generation bump, so a stale runner can never serve.

Two real bugs found en route (both fixed, both covered): (1) the first
IL revision used ldelema-DecodedInstruction on a DecodeCacheEntry array
(wrong stride -- garbage RegisterIndex, IndexOutOfRange in
SpecXorRegImm32); the fix addresses the entry then ldflda's the member,
which required converting DecodeCacheEntry.Instruction from an
auto-property to a public readonly field plus an explicit constructor
(CA1051 suppressed with justification; all reads source-compatible, one
construction site updated). (2) Test chains ending in jmp-$ spin on a
single-member block whose tail is always empty -- vacuous for emission;
chains now loop to the block head. Proven en route: a same-page CPU store
poisons formation (every store drops the page's entries, so no block ever
forms across it) -- the mid-tail generation check is unreachable via CPU
stores and exists for device writes; the SMC test pins the
perpetual-miss path instead. Also established: an IF flip cannot happen
inside an emitted tail (every IF writer is a fallback or a terminator),
so the STI test flips in the preceding block.

Tests: new EmitTests (11 tests: straight chain vs plain+spec, Jcc loop,
register-steered mid-tail fault with precise CR2/EIP, IRQ at tail
positions 2-5, STI-flip-then-emit, SMC perpetual-miss + patch coherence,
CPUID fallback tallies, verify/observer gating with zero EmitRan and
identical profiler counts). Suite 1403/1403 (1392 + 11), format clean.
50M-step triangle probe with --emit: builds=4599, hits=8.4M,
ran=40.8M/49.4M specialized (83% tail-served), vec=0.

GATE (D:\spec-soak\slice27_run.ps1, 8 legs, full-solution rebuild first):
ALL PASS, SHAs exact both titles, vec=0, NV2A anchors exact, interrupts
identical (triangle 1883, menu 3153). Walls (single-run, host stable --
triangle spec reproduces slice-26's 74.4 s at 74.5 s):
triangle emit 144.0 s / spec 74.5 s / cache 94.7 s / block 86.1 s;
menu emit 984.1 s / spec 151.4 s / cache 158.9 s / block 160.0 s.
Emit tails served 759M/998M (triangle) and 782M/995M (menu) with
redirects=0 and verifyFailures=0 throughout.
VERDICT: E1 is wall-NEGATIVE, decisively: 1.93x (triangle) and 6.50x
(menu) slower than spec-exec. The slowdown scales with runner builds
(80k vs 1.24M): ~0.7 ms construction+first-JIT per DynamicMethod fits
both excesses (80k x 0.7 ms ~= 56 s vs 69 s; 1.24M x 0.7 ms ~= 870 s vs
833 s; §138's 0.12 ms was a tiny synthetic method). Menu builds
1.34M distinct block heads, each paying construction once for ~3-member
tails. Wrap-only codegen cannot amortize per-block construction over
short churned tails -- the §138 negative result confirmed at title
scale. Flag stays opt-in (default-off: zero product impact); E1 is kept
as the correctness asset and the harness E2 builds on.
Counter-exactness follow-up: the gate showed host-only micro-deltas
(instructions/shapeHits -612 triangle / -823 menu = in-flight member of
tail fault/pending exits never TryNext-counted; BoundaryExits +3 menu =
cursor still active at last-member exits). Fixed via NoteEmittedExit
(mirrors the member path's serve-count + cursor advance before
EndBoundary). Refining further, the member path's IRQ poll precedes its
decode hit, so pending exits earn NO hit while fault exits earn both hit
and SpecRan -- NoteEmittedExit mirrors exactly that split, pinned by a
7-position steady-state delivery sweep (fault needs the hit, pending
must not have it) plus the fault differential. A failed transfer now
re-serves via the member path instead of skipping the member. Unit
counter asserts added (all serve/exit tallies incl. fault + IRQ exits).
Post-fix 50M triangle diff spec-vs-emit: every Block/Spec/decode/IRQ/
vec line bit-identical. Closed-delegate/shape guard added to Build
(falls back exact). Suite 1410/1410, format clean.
E2 SCOPE (next): threshold-tiered emission -- build the runner lazily
after N head visits so cold single-visit code (the 1.3M) never pays
construction while hot loops amortize it; attach the runner to the
Block (kills the head-key dict + silent cap + per-visit lookup); hoist
Context/InterruptSource loads to per-invocation locals. If tiered
emission cannot beat the C# unrolled path either, the serve layer is
declared at ceiling and only body-level semantic work remains.

## 172. Tiered emission: lazy runner builds + block-attached cache (2026-10-17)

Direct follow-up to the §171 wall verdict (per-runner ~0.7 ms
construction+first-JIT cannot amortize over short churned tails: menu
built 1.24M runners for ~3-member tails). E2 attacks construction
directly, changing only the serve vehicle: (1) lazy builds -- a block's
tail runner is built on its 8th head visit (`DecodedBlockCache.
EmitThreshold`; visits counted at head selection), so cold single-visit
code never pays construction while hot loops amortize it; pre-threshold
visits run the exact member path. (2) The runner cache moved onto the
Block (Runner/RunnerGen validated against the invalidation generation),
killing the head-key dict, its per-visit lookup, and the silent 4096-cap
clear -- runners die with their blocks, and Invalidate needs no separate
table clear. (3) The emitted prologue hoists Context into a local (one
callvirt per invocation instead of three per member); InterruptSource
stays a live per-member read and generation/EIP stay per-member checks.
Threshold/tiering change counting nowhere (unit counter asserts
extended by longer budgets, not new rules).
50M triangle probe: builds 4599-->1269 with identical emission volume
(40.7M) and identical spec stats.
GATE (D:\spec-soak\slice28_run.ps1, 8 legs): ALL PASS, SHAs exact both
titles, vec=0, anchors exact, interrupts identical 1883/3153.
triangle emit 71.6 s / spec 73.0 s (emit at parity-or-better -- the first
codegen configuration not slower than the C# unrolled path); menu emit
376.2 s / spec 147.2 s (was 984 s: 2.6x better, still 2.56x spec).
Builds collapsed by tiering: triangle 80k-->9k, menu 1.24M-->432k with
identical emission volume and bit-identical serve/exit/decode tallies.
Remaining menu excess (~229 s) fits 432k warm-head constructions
(~0.5 ms each): threshold tuning (raising T) is the next knob.
50M triangle probe: builds 4599-->1269 with identical emission volume
(40.7M) and identical spec stats.

## 173. Threshold tuning: T=8 --> 64 (2026-10-17, same session)

E2b timebox on the §172 tail: menu still pays 432k warm-head
constructions at T=8 (heads visited 8-63x run member-path at spec speed
anyway, so raising T strictly sheds construction with negligible
steady-state cost). Single const change; unit budgets extended for the
higher threshold (no rule changes -- 18/18 green). 1B menu-emit probe
leg at T=64: 178.7 s (was 376.2 s -- another 2.1x, now 1.21x spec),
builds 432496-->69722 (6.2x), ran 714M (warm code on the member path,
as designed), SHA exact, interrupts 3153, vec=0, NV2A anchors exact.
GATE (D:\spec-soak\slice29_run.ps1, 8 legs): ALL PASS, SHAs exact both
titles, vec=0, anchors exact, interrupts identical 1883/3153.
triangle emit 71.0 s / spec 73.6 s (parity-or-better reproduced);
menu emit 179.2 s / spec 153.2 s (1.17x -- was 2.56x at T=8, 6.50x at
E1). Builds: triangle 3624, menu 69722 (warm-head constructions only).
Remaining menu excess (~26 s) fits the 70k constructions. ktest smoke
PASS 7/7 (01:40). Flag stays opt-in.

## 174. Threshold T=1024: emit at spec parity (2026-10-17, same session)

Same one-const timebox, one step further: the T=64-->256 probe leg
showed 163.4 s / 24k builds (another 15 s), so T=1024. Unit budgets
extended again (18/18 green). 1B menu-emit probe leg at T=1024:
153.6 s (spec band 147-153 s -- parity), builds 24450-->5816,
ran 626M, SHA exact, interrupts 3153, vec=0, anchors exact.
Construction is now a minority cost; the remaining delta is the
per-member floor (E3 body work). GATE
(D:\spec-soak\slice30_run.ps1, 8 legs): ALL PASS, SHAs exact both
titles, vec=0, anchors exact, interrupts identical 1883/3153.
triangle emit 66.0 s / spec 76.0 s (parity-or-better, reproduced
across three gates); menu emit 180.3 s / spec 182.2 s (parity;
was 6.50x at E1). Builds: triangle 1044, menu 5816. Host drift
~3-19% between gates (single-run walls, no wall claim -- the
within-gate emit<=spec ordering is the robust signal). Emit stays
opt-in (zero default impact); E3 (body inlining) is the next lever.
ktest smoke PASS 7/7 (01:50). Suite 1410/1410, format clean.

## 175. E3 scope: semantic specialization of hot bodies (2026-10-17, scoped, not started)

Serve-layer verdict (§§171-174): per-block DynamicMethod tails with
lazy tiered builds reach spec parity-or-better on both titles;
per-member boundaries (gen/EIP/TSC/poll) are irreducible per §138
rules 4-5, and the remaining wall lives in executed BODIES (data-path
helpers were 76% of menu execute in §138). E3 must therefore specialize
bodies, not serve them faster. Scoped program (each slice lands
independently with unit differentials + pins, full suite, 4-leg goldens,
median-of-3 before any wall claim, flag stays opt-in throughout):
- Step 0 (measurement, no product change): attribute wall to
  (shape x body-cost) under `--emit` on both titles from HotspotProfiler
  mnemonic mix x emitted-member volume; rank families by volume x body
  call depth. Pick the first family from numbers, not instinct.
  Measured 2026-10-17 (menu 1B, `--spec-exec --hotspot`, same guest
  sequence as emit): Mov 33.8% (338M), Jcc 17.6%, Dec 10.3%, Cmp 5.9%,
  dataReadsRam 291M + dataWritesRam 211M (= 502M RAM accesses, ~0.5 per
  step), Push/Fld/Fstp/Fmul ~5% combined, Cli+Sti 3.3% (already minimal
  2-op bodies). Missing piece: the profiler counts mnemonics, not
  shapes -- Mov reg-reg vs Mov r/m volume is unknown. E3-0 (tiny,
  flag-gated): extend the fallback-shape histogram pattern to ALL
  served shapes (a served-shape tally, default-off so the hot path is
  untouched) and re-probe; rank = shape volume x body call depth.
- Candidate E3-1: inline hot memory-load bodies (Mov r/m32-family first
  if the ranking says so -- 338M Movs + 502M RAM accesses make it the
  volume x depth leader by a wide margin; per-shape split pending E3-0) -- address compute (live segment base +
  base+index*scale+disp), single-translate fast path, direct RAM read,
  all as IL with the interpreter's exact fault order (compute -->
  translate which may PageFaultSignal --> access; EIP advances only
  after completion). Reuse the EmitTests differential + fault-precision
  pattern.
- Candidate E3-2: flag-folding across straight-line ALU runs
  (formation-time liveness + no-flags handler twins) -- only if E3-1
  measurement shows flags material.
- Explicit non-goals: no EH changes, no speculation, no cross-block
  fusion, no background-thread JIT (needs a determinism review first).
- Standing rules carry over: median-of-3 before any wall claim;
  single-run walls reported without claim.
Median-of-3 (§175 gate `D:\spec-soak\median31`, 12 legs, all SHAs
exact): triangle emit 74.29 s (72.78/74.29/80.00) vs spec 80.91 s
(79.72/80.91/81.64) --> **emit 1.089x spec**; menu emit 155.71 s
(171.47/155.71/145.36) vs spec 146.97 s (146.90/146.97/152.02) -->
**emit 0.944x spec**. First median-backed emitter claim: triangle
faster, menu 6% deficit. Observation (not a claim): menu emit legs
descend across reps (171-->156-->145) while spec legs are flat,
consistent with a first-run warmup transient (construction + first-JIT
+ tiering); a second pre-registered menu-emit triplet (r4-r6) tests it.
Second triplet: 148.0 / 150.3 / 154.1, SHAs exact -- r1 (171.5) stands
alone as the warmup outlier; r2-r6 span 145-156 (host noise dominates).
Six-sample median 152.2 vs spec 147.0 = 0.965x: the robust claim stays
parity-with-slight-deficit, i.e. the serve layer is at ceiling and only
body work (E3) can move walls further.

## 176. E3-0 served-shape tally + E3-1 inline bodies (2026-10-17, direct)

E3-0 (measurement, landed): `--shape-tally` (implies spec-exec, suppresses
`--emit`, default-off) extends the §139 fallback-histogram pattern to every
specialized serve: `DecodedBlockCache.ShapeTallyEnabled` + `NoteServedShape` +
`TopServedShapes`; the CLI prints `Spec served
<Mnemonic>/<Op1>/<Op2>/<Size>x<Cond>/<Seg>=count` (top 40) at exit.
Diagnostic only: the flag-off serve path is unchanged (one field read inside
the spec branch) and enabling it leaves guest state identical (SaveState
differential pin). Unit pins: +3 tests.

Measured 1B rankings (`D:\spec-soak\e30`, spec-exec member path; totals are
specialized serves):
- menu (994.1M): Mov/R/M/32 156.1M; Mov/M/R/32 128.2M; Jcc/NE 115.0M;
  Dec 101.2M; Jcc/EQ 38.1M; Nop 34.7M; Inc 31.3M; Cmp/R/M/32 20.2M;
  Mov/R/R/32 18.5M; Cmp/M/Imm/32 18.5M; Cli 16.3M; Sti 16.2M;
  Mov/R/M/8 13.0M; Add/R/Imm 13.0M; Fld/M 12.8M; Fstp/M 11.3M;
  Mov/M/R/8 11.1M; Movq/R/M/64 7.6M; Movntq/M/R/64 7.6M; Movzx/R/M 6.6M.
- triangle (997.9M): Mov/R/M/32 122.4M; Jcc/EQ 118.5M; Nop 113.5M;
  Mov/M/R/32 105.8M; Jcc/NE 100.0M; Dec 93.6M; Cmp/M/Imm/32 57.4M;
  Cmp/R/M/32 57.3M; Cli 56.6M; Sti 56.6M; Mov/R/R/32 15.9M; ...
Twins cover 347.1M/994.1M = 34.9% of menu members and 352.3M/997.9M = 35.3%
of triangle members (Mov 8/32 both directions + the three Cmp forms +
Test/M/Imm). x87/MMX memory shapes (Fld/Fstp/Fild/Fistp/Movq/Movntq) stay
uncovered by this slice.

E3-1a (landed, opt-in `--emit-inline`, implies `--emit`): `SpecInlineHandler`
twins derived from `ResolveSpec` handlers (twin-for-handler: the two can
never disagree about cover; only the vehicle differs); `BlockEmitter.Build`
bakes the twin call with the runner's hoisted context; runners record the
flag they were built with (`RunnerInline`) and rebuild on mismatch;
`EmitInlineMembers` build counter. Eight twins mirror ReadMemory/WriteMemory
minus the hotspot branches (unreachable under the RunEmitTail guard), keeping
the MMU gate, cross-page per-byte order, store watch and NoteStorePhysical
semantics statement-for-statement.

E3-1b (landed, same flag): for the two 32-bit Mov reg<->mem shapes the
emitted member is a baked IL fast path: inline effective-address compute
(baked base/index/scale/disp/segment; live register and segment-base values),
cross-page guard, data-translate cache probe (tag + store writable/WP), RAM
bounds, then PhysicalMemory.Read32/Write32 with RawWrite's
IsWatched/NoteStorePhysical order; every non-fast case (16-bit addressing,
cross-page, tag miss, non-RAM, watched/unwritable store, Mmu.Observer
attached) branches to the exact twin. `Mmu.Observer is null` is hoisted per
invocation (TranslateLinear's FastWideAllowed gate; its hotspot/idle halves
are guaranteed by the RunEmitTail guard). `EmitFastMembers` build counter.

Tests: +7 EmitTests (all covered shapes incl. FS/GS overrides, 16-bit
addressing stays on the twin, watched store hits exact, non-RAM physical
falls back, load fault mid-tail precise Cr2, store cross-page fault precise
Cr2, observer/verify gates, resolver discipline). Gate: `scripts/emit-gate.ps1`
(title x mode legs, SHA + counter + fault + verify asserts).

Gate evidence (`D:\spec-soak\e31`, emit-gate legs; all PASS, SHAs exact both
titles, `vec=0`, counter anchors exact, `verifyFailures=0`): menu cache
(216.2 s) + spec (206.5 s) on the E3-1a binary; then on the E3-1b binary
menu emit x3 (204.7/174.2/170.1 s) + menu emit-inline x3
(171.4/166.4/172.1 s) interleaved, triangle emit (75.3 s) + triangle
emit-inline (72.6 s). Interleaved median-of-3 (menu): **emit 174.2 s vs
emit-inline 171.4 s = 1.016x** -- thin-positive; the per-pair ratios
(1.19/1.05/0.99) show the first emit leg as a warmup outlier and the
steady-state pairs at ~parity. Triangle single-run: emit 75.3 s vs
emit-inline 72.6 s (1.037x, single run, **no claim**).
Second interleaved triplet on the E3-1c binary (`D:\spec-soak\e32`):
menu emit 184.6/145.6/146.4 (median 146.4) vs emit-inline 152.1/144.7/150.5
(median 150.5) -- **the sign flips**; per-pair ratios 1.21/1.006/0.973.
The robust statement after both triplets: E3-1 (twins + fast paths) is
**wall-neutral within host noise** on the menu (host load swings +-25%,
sub-3% effects unresolvable); triangle single-run deltas 1.015-1.037x
favour inline but carry no claim. Verdict: mechanism landed, exact and
opt-in; the removed call layer is a small fraction of covered members'
cost, and the non-covered ~65% is dominated by irreducible per-member
boundaries. Before further body work, measure the 4-entry data-translate
cache hit rate inside the emitted fast paths (a twin-call counter
diagnostic) -- a low hit rate would make the guard cost exceed the saved
call layer on thrashing working sets.

## 177. E3-1c: fast-path cover for Cmp/Test/8-bit Mov (2026-10-17, direct)

Extends the E3-1b plan mechanism from two kinds to eight
(`InlineFastPlan.Kind`): MovLoad32/MovStore32/MovLoad8/MovStore8,
CmpRegMem32/CmpMemReg32/CmpMemImm32, TestMemImm32. The shared prologue
(baked address, size-aware cross-page guard -- 8-bit never crosses --,
translate-tag probe, store writable/WP guard, RAM bounds, watched-store
check) feeds a kind-specific tail: 32-bit register moves via direct
`_registers` access, 8-bit moves through the masking accessors
(ReadRegister8/WriteRegister8), Cmp through `AluOps.Sub` with the baked
immediate for the imm form, Test through `value & imm` (Mask(32) is the
identity). Baked immediates come from the immutable decoded instruction;
every non-fast case still branches to the exact twin.

Coverage (1B tallies): fast-eligible shapes rise from 284.3M (28.6%) to
348.6M (35.1%) of menu members; triangle from 228.2M (22.9%) to 344.9M
(34.6%) -- triangle gains the Cmp forms (114.7M) that dominate its mix.
Build-time evidence: the 100M-step menu smoke bakes 550/550 inline members
onto fast paths (was 461 at E3-1b).

Tests: the existing E3-1 differentials cover every new kind by construction
(MemoryShapes carries 8-bit Mov/Cmp/Test shapes; fault, cross-page, watched,
non-RAM and 16-bit pins unchanged); all 26 EmitTests stay green, suite
1421/1421, format clean.

Gate + walls: e32 interleaved triplet on the E3-1c binary -- all 8 legs
PASS, SHAs exact both titles (`menu emit 184.6/145.6/146.4` median 146.4 vs
`menu emit-inline 152.1/144.7/150.5` median 150.5; triangle emit 66.1 vs
emit-inline 65.1). Combined with the e31 triplet (sign flipped: 174.2 vs
171.4), E3-1a/b/c is **wall-neutral within host noise**; triangle
single-run 1.015x favours inline, no claim. Flag stays opt-in.

## 178. E3-1d: emitted fast-path hit rate measured (2026-10-17, direct)

`--emit-diag` (implies `--emit-inline`): E3-1d diagnostic counters baked
into runners at build time (zero cost when off; runners record the flag and
rebuild on mismatch). Counters: fastHits (fast path completed), tagMisses
(data-translate tag miss), twinCalls (every fallback through the exact
twin). Paged unit differential pins hits, a steered-VPN tag-miss fallback
and the accounting; unpaged runs never take the fast path (no translate
cache without paging -- the twin keeps them exact).

1B measurements (`D:\spec-soak\e33`, SHAs exact both titles, `vec=0`):
- menu: fastHits 158,738,154; tagMisses 14,213,974; twinCalls 14,533,678;
  **hitRate 0.9161**; EmitRan 626.4M of 994.1M members tail-served.
- triangle: fastHits 178,889,942; tagMisses 2,147,082; twinCalls 3,068,370;
  **hitRate 0.9831**; EmitRan 745.0M of 997.9M.
Fallbacks are ~98% tag misses (no thrash pathology; the 4-entry cache
behaves). The fast path is therefore alive at scale -- the wall-neutral
verdict of §§176-177 is a scale fact: only ~50% of fast-kind members are
tail-served (menu 173.2M of 348.6M; triangle 181.9M of 344.9M), and
158.7M/178.9M hits at a few ns saved per hit project to ~0.3-0.5% (menu,
unmeasurable under the +-25% host noise) and ~1-1.5% (triangle, matching
the single-run 1.015-1.037x readings). Remaining levers are scoped, not
taken: raising tail-serve share (threshold re-tune against §174's
construction trade-off) or member-path fast dispatch; both are low-value
against the boundary-dominated remainder. Flag stays opt-in.

Verification addendum: a randomized differential net (seeded programs of
12 mixed covered-shape members with random registers/displacements/
prefixes/immediates, looping) runs each program under plain, spec, emit
and emit-inline in both unpaged and paged harnesses and asserts identical
state, RAM and counters -- 40 programs x 2 x 4 modes per suite run, plus
the earlier hand-written differentials and the diag counter pins.

## 179. E3-1 verdict, quiet-host re-measurement (2026-10-17)

18-leg interleaved campaign (`D:\spec-soak\quiet1`; 3 rounds x {spec, emit,
emit-inline} x {menu, triangle}; host verified quiet -- Fortnite off, load
23%; every leg SHA-exact, counters exact, vec=0, verifyFailures=0). The
earlier triplets (e31/e32, §§176-177) ran under +-25% host noise and were
reported wall-neutral; this campaign supersedes them.

Medians (seconds):
- menu: spec 143.7 (141.9/143.7/149.1); emit 147.8 (141.7/147.8/151.9);
  **emit-inline 145.8 (142.4/145.8/145.9)**.
- triangle: spec 74.0 (71.6/74.0/76.1); emit 64.3 (64.2/64.3/65.0);
  **emit-inline 62.5 (61.2/62.5/63.0)**.

Ratios:
- menu: emit/spec 1.0285; inline/spec 1.0146; **emit/inline 1.0137**.
- triangle: spec/emit 1.1509; spec/inline 1.1840; **emit/inline 1.0288**.

Paired-round signs: emit-inline vs emit 5/6 wins (menu r5/r6 win, r7 -0.5%;
triangle 3/3). Verdict: the E3-1 fast paths are a **measured win over plain
emit on both titles** and a **measured 18.4% win over spec on triangle**;
on the memory-heavy menu, spec remains marginally fastest (inline 1.5%
behind, emit 2.9% behind) -- the fast paths recover most of the emitter's
menu deficit but do not overtake the plain handler path there. Flag stays
opt-in; a default-mode change is an operator decision (the §137
decode-cache precedent applies: conditions met -- median-backed, SHAs and
counters exact, verifyFailures=0, flag-off byte-identical).

### 179a. Menu n=7 correction (same day, immediate addendum)

Four more interleaved menu rounds (r8-r11, same conditions) were added
before this verdict was relied upon. Menu median-of-7:
- spec 143.7 (141.9/142.6/143.0/143.7/143.9/145.2/149.1);
- **emit 141.8** (139.0/141.2/141.7/141.8/142.9/147.8/151.9);
- emit-inline 144.4 (140.8/142.4/143.0/144.4/144.8/145.8/145.9).
**The menu ordering flips again**: emit < spec < emit-inline, all within
~1.8%, and emit-inline-vs-emit signs are mixed (inline wins 3 of 7 rounds).
The r5-r7 menu readings were a warmup-biased sample (r5-r7 legs run
systematically slower than r8-r11 on both modes). The corrected menu
verdict is **parity across all three modes (+-2%)**; only the triangle
verdict stands as measured (emit-inline 62.5 < emit 64.3 < spec 74.0,
3/3 paired wins, 1.184x over spec). Lesson recorded: menu's E3-1 delta is
below what median-of-3 resolves even on a quiet host -- n>=7 is the
menu-evidence standard for sub-2% claims, and mixed signs must be reported
as parity, not averaged into a win.

### 179b. Triangle n=7 confirmation (same day)

Four more interleaved triangle rounds (r8-r11). Triangle median-of-7:
- spec 71.6 (69.0/71.2/71.5/71.6/74.0/75.4/76.1);
- emit 64.3 (63.1/63.2/64.2/64.3/64.3/65.0/65.5);
- **emit-inline 63.0** (61.2/61.6/62.5/63.0/64.6/64.6/64.7).
Paired emit-vs-inline signs: inline wins 5 of 7 rounds (losses r8/r10 at
~2.3%). Corrected triangle verdict: **emit-inline 1.021x over emit (5/7
pairs) and 1.137x over spec** -- softer than the n=3 reading (2.9%, 3/3)
but consistent in direction. The spec-vs-emitter gap (1.113x) is far
beyond noise and is the dominant, robust emitter win on this title.

## 180. Rasterizer slice R1: bit-exact combiner fast path for the dashboard's dominant config (2026-10-17, direct)

Measurement first (new opt-in `--gpu-time` host-only rasterizer wall
accounting; `D:\spec-soak\probe2`): the software rasterizer's combiner fill
path (`FillTrianglePsh`) is **119.2 s of the menu's 175 s wall (68%)** and
**54.7 s of triangle's 83.4 s (66%)**; the legacy path is ~0.5 s on both.
Segment timestamps (`D:\spec-soak\probe1`) show the menu's first 750M steps
run at ~30M steps/s (~26 s) and the dashboard phase (last 250M steps, 686
flips, 285M px) owns the rest. `--gpu sw` vs `--gpu gl` walls are identical
(163.3 / 162.9 s): the GL backend coexists with (does not replace) the
software rasterizer, by design (`IPgraphBackend` contract).

Ablation (temporary switch, removed after use; `D:\spec-soak\probe3`, menu):
skipping the combiner `Evaluate` alone cuts the rasterizer 119.2 -> 36.0 s
(**Evaluate = 83.2 s = ~47% of the whole menu wall**, ~291 ns/call over
~285M fragments); skipping `ProcessFragment` 119.2 -> 104.4 s (14.8 s);
skipping the texel resolve loop 119.2 -> 96.0 s (23.2 s).

Config histogram (rides `--gpu-time`; `D:\spec-soak\probe4`): the menu runs
86% of its fragments (245.5M) through ONE single-stage combiner config -
rgb = T0*V0, alpha = T0.a*V0.a (all unsigned-identity mapped), stage written
to R0 (AB/CD dest 0 = no-op slots), final combiner passing R0 through
(E=F=A=B=C=0, D=R0, G=R0.a) - i.e.
`PshCombiner.IsModulateT0V0Passthrough()`. The rest are two multi-stage
configs (stages=8 c=00011108, stages=7 c=00011107; 62.4M fragments) shared
with the triangle, which uses those two for 70% of its pixels.

Slice R1 (landed): `PshCombiner.EvaluateModulateT0V0` - a literal
transcription of the generic path with the configuration constants folded
(same `Math.Max`/`Math.Clamp` calls, same operation order), selected once per
draw; `Nv2aMmio` branches on it in the fill loop. Exactness pinned by a
4000-vector differential test (random floats including NaN/+/-0/+/-1/+/-inf,
per-component bit compare vs the generic `Evaluate`) plus the end-to-end
goldens: menu/triangle 1B SHAs exact, all counter anchors exact,
`LastFault vec=0`, `verifyFailures=0`, and the full normalized stdout
byte-identical to the pre-change baseline (`D:\spec-soak\r1` vs `probe4`).

Measured effect (single runs, no median claim): menu rasterizer
113.2-119.2 s -> **80.1 s** (~35 s saved; the fast path covers 245.5M
fragments at roughly 135-160 ns each); menu leg wall 164.9 -> 150.3 s (host
was ~15% slower than the quiet1 campaign; the raster delta is the signal).
Triangle unchanged (does not use this config; SHA/counters exact). No flag -
the recognizer is per-draw and exact by construction + differential.

Also landed: `--gpu-time` diagnostics (raster seconds per fill path + the
combiner config histogram) with tests; decode-flush audit filed
(`docs/research-notes/decode-flush-audit.md`: steady-state page-drop
churn ~2-5% of menu wall via whole-block-table wipes; per-page block
versioning is the narrowing candidate) and rasterizer inventory filed
(`docs/research-notes/rasterizer-inventory.md`, O1-O10).

Next: the two multi-stage configs (62.4M menu + 62.4M triangle fragments) are
the largest remaining rasterizer cost; then sampler (~23 s menu) and
ProcessFragment (~15 s menu) hoists.

## 181. Rasterizer slices R2 + R3: loop skips, per-fill constants, and the planned combiner evaluator (2026-10-17, direct)

R2 (safe hoists, `D:\spec-soak\r2`): per-fill `PrepareFragmentConstants`
(alpha-ref scale, blend constant vector, zclip depth range - same expressions,
computed once per fill instead of per pixel); per-fill scratch reuse in
`FillTrianglePsh` (the per-triangle array allocations become instance scratch,
fully re-initialized per fill); per-fill loop skips: `None`-mode slots skip the
`ResolveTexel` call (texel already None), the clip-plane scan is skipped when
no slot is CLIPPLANE, `Array.Clear(stageDots)` is skipped when no slot is
DOT_ST/DOT_STR_3D (the only stageDots readers). Gate: menu/triangle SHAs +
all counters exact, normalized stdout byte-identical. Effect: menu raster
80.07 -> 76.16 s, triangle 52.70 -> 49.55 s (single runs).

R3 (planned combiner, `D:\spec-soak\r3`): `PshCombiner.RebuildPlan`
precomputes once per draw the per-stage/final input decode (flat regs offsets
+ mappings), the already-mapped constant-source values (Zero/C0/C1 via the
same ReadComponent+MapComponent calls), the per-half output words and the
final EfProd kind; `EvaluatePlanned` mirrors `Evaluate` operation for
operation while reading the plan. Wired in `FlushProgramDraw` next to
`BuildConsts`; the fill loop uses it for every non-F1 config (the two 7/8-
stage configs that cover 22% of menu and 70% of triangle fragments).
Exactness: a planned-vs-generic differential test over the two real configs
plus 40 randomized configurations x 120 samples caught one real transcription
bug during development (missing input mapping on the final EF_PROD reads; now
a four-case regression pin) - fixed and re-pinned; plus the end-to-end goldens
(SHAs exact, counters exact, normalized logs byte-identical). Effect: menu
raster 76.16 -> 59.50 s, triangle 49.55 -> 38.36 s (single runs).

Cumulative rasterizer progress this session (single-run walls, host varying;
the raster seconds are the signal): menu 119.2 -> 59.5 s (2.0x), triangle
54.7 -> 38.4 s (1.4x); menu leg wall ~165 -> 110.7 s, triangle ~83 -> 67.1 s.
Both titles: SHAs and every counter exact at every step; suite 1427 tests.

Next (largest remaining raster cost): the two 7/8-stage configs still run the
planned evaluator (~25-30 s menu / ~20 s triangle estimated); a hand-
transcribed specialized evaluator per config (F1-style, differential-pinned)
is the next slice. Then sampler descriptor/swizzle hoists and the
fragment/memory lane (inventory O1-O5).

## 182. Rasterizer slices R4 + R5: lighting fast path (corrected) + sampler plans (2026-10-17, direct)

R4 (lighting specialization): `PshCombiner.GetSpecializedKind` recognizes the
two multi-stage "squared lighting" configs (stages=8 c=00011108 and stages=7
c=00011107; 62.4M fragments on EACH title) and
`EvaluateLightingSpecialized` transcribes them operation for operation
(stage 0 dots, 1 unsigned + N signed power stages, combine stage, add stage,
final with SPECFOGFACTOR0). First recognizer attempt required unsigned power
stages for all stages and never matched - the histogram words are
`ri1=0D0D0000` then `ri2..=CDCD0000` (stage 1 unsigned, stages 2+ signed);
the corrected recognizer + transcription are pinned by a differential test
over the REAL words (250 randomized constant sets x 200 samples each) plus
the end-to-end goldens. A `--gpu-time` fastfill counter (`k1/k2/k3` fills per
kind) was added so a silent recognizer miss can never masquerade as "no
speedup" again.

R5 (sampler plans): per-draw `SamplerPlan` (built in `EnterTextureState` for
BOTH draw paths) hoists the per-sample format/shape/wrap-mode/pitch/swizzle-
mask decode; `WrapCoordinatePlanned` folds the modulo to a mask for
power-of-two extents (proven identical for every int including negatives);
the swizzle path uses the hoisted masks and `InterleaveBits` stops at the
mask's highest set bit (skipped iterations were no-ops); `SwizzleOffset`
shares the mask builder. Pure hoists: every counter and texel value
unchanged.

Gate (`D:\spec-soak\r5`, all counters exact, normalized stdout byte-identical
to r4 on both titles, SHAs exact):
- menu: raster psh 59.24 -> **30.17 s**, leg wall 112.4 -> 77.5 s
  (fastfills k1=402600, k2=39645, k3=13929 - all three kinds engaged).
- triangle: raster psh 38.36 -> **9.87 s**, leg wall 64.8 -> 37.3 s
  (k2=39645, k3=13929; k1=0 as expected).

Cumulative session rasterizer progress (single runs, host varying):
menu raster 119.2 -> 30.2 s (3.9x), triangle 54.7 -> 9.9 s (5.5x); menu leg
wall ~165 -> 77.5 s, triangle ~83 -> 37.3 s. Suite 1427 tests.

## 183. Rasterizer session close: final medians, neutral experiments reverted (2026-10-17, direct)

Final median-of-3 (interleaved rounds, `scripts/emit-median.ps1`,
`D:\spec-soak\final-median`; every leg SHA-exact):
- menu spec: 76.4 / 77.6 / 83.1 s -> **median 77.6 s** (quiet-host baseline
  median 143.7 s -> **1.85x**).
- triangle spec: 35.2 / 35.5 / 37.8 s -> **median 35.5 s** (baseline 71.6 s
  -> **2.02x**).

Rasterizer-only progress (`--gpu-time`, single runs): menu 119.2 -> 30.2 s
(3.9x), triangle 54.7 -> 9.9 s (5.5x). Session slices: F1 modulate fast path
(§180), loop skips + per-fill constants (§181), planned combiner (§181),
lighting fast paths + sampler plans (§182).

Two further experiments measured NEUTRAL and were REVERTED (negative results
recorded here so they are not re-attempted without new evidence):
1. GPU write scopes + page-drop memo (`IGpuMemory.Begin/EndGpuWriteScope`
   default methods, `PhysicalGpuMemory` scoped `WritePhysical`, per-triangle
   scope wraps at all three fill/clear call sites): menu raster 30.26 vs
   30.17 s, triangle 10.09 vs 9.87 s - the per-write suppression pair and the
   decode-cache page-drop check are already ~1-2 ns each; the scope wraps
   cost as much as they save. Reverted (r6).
2. Byte->float decode tables (`/ 255f`, `/ 31f`, `/ 63f`, `/ 15f` replaced by
   precomputed tables with identical bit patterns) in `DecodeTextureTexel`,
   `TryReadPixelFloat`, `TryDecodeSwizzledTexel`: menu 30.50 vs 30.17 s,
   triangle 9.90 vs 9.87 s - the four independent divss per texel pipeline
   better than expected and the table lookups' bounds checks offset the
   difference. Reverted (r7).

Interpretation: after the six landed slices the remaining raster cost is
fundamental per-pixel work (barycentric + interpolation + one sample + one
fragment pipeline pass at ~100 ns/px combined), not hoistable decode or
bookkeeping. The next raster lever would be structural (e.g., row-span
iteration, inventory O6/O7-class work), and the CPU side is again the larger
half of both walls (menu ~47 s of 77.6 s).

Standing gates at session close: full suite 1427 tests, format clean, ktest
smoke PASS 7/7, menu/triangle 1B SHAs and every counter exact across all
gated legs (r1-r7 + final median campaign), normalized stdout byte-identical
at every slice.

## 184. GL backend latent regression fixed + opt-in live window (--window, brief O2) (2026-10-17, direct)

**Latent regression found and fixed.** The docs-cleanup commit `4b5af0d`
("concrete summaries replace interface <inheritdoc/>") renamed the
`wglGetProcAddress` P/Invoke to `WglGetProcAddress` while converting it to
`[LibraryImport]` WITHOUT an `EntryPoint`, so the opengl32 export lookup has
been case-mismatched ever since: `GlBackendFactory.TryCreate` threw
`EntryPointNotFoundException`, caught it, and `--gpu gl` silently fell back
to software ("GPU backend=sw (gl probe failed: ...)"). Consequence: the
§180-era probe that measured `--gpu gl` wall == `--gpu sw` wall was
sw-vs-sw (both legs fell back); the GL-coexistence finding itself stands on
the code evidence (the backend contract + unconditional SW loop) and the
historical §§113-122 gates. Fix: `EntryPoint = "wglGetProcAddress"`.
Verified: `GPU backend=gl probe ok; GL_VERSION=4.6.0 NVIDIA 610.88` and the
slice-1 golden script passes again.

**Opt-in live window (swapchain brief O2; T1 conditions met: §132 (a*)
dirty-bit merged + free build slot; no new dependency — user32/gdi32/
opengl32 interop only).**
- `WglContext`: `TryShowWindow` (restyle WS_OVERLAPPEDWINDOW via
  SetWindowLongPtrW — the pixel format is set once and kept — client area
  sized to the frame via AdjustWindowRect + SetWindowPos, centered,
  ShowWindow, `wglSwapIntervalEXT(0)`), `SwapBuffers`, `PumpMessages`
  (PeekMessageW loop), `SetTitle`, `TryGetClientSize`.
- `GlPgraphBackend`: `PresentToWindow` at the end of `FlipCore` (after the
  readback — a sink only, never mixed): NEAREST blit FBO -> default
  framebuffer scaled to the client area, SwapBuffers, pump, title counters;
  `WindowPresents`/`WindowStatus` diagnostics; any show failure degrades to
  headless. `IPgraphBackend.Pump()` default no-op;
  `GlPgraphBackend.Pump` pumps between batches.
- CLI: `--window` (implies `--gpu gl`), 2M-step batch cap in window mode so
  the pump runs at ~100 ms granularity (host scheduling only — guest
  boundaries unchanged), exit diagnostic `GL window presents=N status=...`.

Acceptance evidence: WGL-guarded `WindowPresentLiveTests` (disabled => "off"
+ 0 presents; enabled => presents increment and the readback bytes are
unchanged, else the degradation is recorded); live run
`--steps 820000000 --spec-exec --window` reports `GPU backend=gl probe ok;
GL_VERSION=4.6.0 NVIDIA 610.88` and `GL window presents=35 status=window
640x480 vsync=off`, `BackendUnsupported=0`; guest counters at the same step
count are IDENTICAL to a `--gpu sw` control run (2720 draws / 73792923
pixels / 35 frames / all counters), i.e. the window path does not perturb
guest state; SW goldens flag-off byte-identical (`D:\spec-soak\r8`); suite
1432 tests; format clean. Headless (`--gpu sw`) remains the default and the
gate currency; `--window` is an observation sink only (no input, no vsync).

GL-mode cost observation (live backend, 1B menu, `D:\spec-soak\slice1-golden`):
with the backend actually running, `--gpu gl` costs sw 91.8 s -> gl 107.8 s
(+16 s: drawMs=8029.8 for 2,760,841 triangle submissions + readbackMs=1326.6
for 686 flips). The §122 "0-2.9% of wall" figure predates this session's
rasterizer speedup and measured different workloads; the honest current
shape is that GL mode is SW + ~9-16 s of submission/readback on the menu at
1B. This does not touch the default (`--gpu sw`) or any gate; `--window`
carries the same cost plus one blit+swap per presented frame.

## 185. Flag/tuning measurements at session close: idle-skip inverted, TLB capacity neutral (2026-10-17, direct)

**`--idle-skip` is now a REGRESSION on both goldens** (SHA-exact every leg;
the flag only changes host-side scheduling):
- menu 1B `--spec-exec`: 112.4 s with the flag vs 77.6 s without (0.69x).
- triangle 1B `--spec-exec`: 82.2 s vs 35.5 s (0.43x).
- triangle 1B cache-only: 56.5 s with the flag vs ~41.3 s without (0.73x;
  the skip fired hard here: 447.8M of 1B steps skipped, 7561 triggers).
Diagnosis: the skip machinery's per-iteration bookkeeping (structural aborts
5.6M menu / 4.0M triangle, iters 16M / 54M, static rejects, trigger drops)
is unchanged since §119, while the JIT slices (default decode cache, block
exec, spec slices 1-20, memory-path 1-4) made base execution 2-3x faster -
the skipped idle steps were the CHEAP ones, so the fixed overhead now dwarfs
the saving and the §119 "1.10x / 1.66x" win inverted. Recommendation: retire
or rework the flag (operator decision); do not use it on the goldens. The
flag remains functional and SHA-exact.

**SoftTlb capacity**: 256 -> 4096 entries measured 77.9 s vs 77.6 s on the
menu 1B (neutral-to-negative; the boot's 9.5% TLB miss rate is driven by the
frequent full flushes (PTE-store bursts, per the decode-flush audit), not by
table capacity - a bigger table only makes each flush more expensive).
Reverted.

## 186. Rasterizer slice R6: SrcAlpha/InvSrcAlpha/ADD blend specialization (2026-10-17, direct)

Blend-config probe (temporary per-fragment histogram under `--gpu-time`):
the menu's 211,696,978 blended fragments are 211,696,842x
SrcAlpha/InvSrcAlpha/ADD (99.99993%); the triangle's 518 blended fragments
are 382x the same config. `ProcessFragment` now takes a per-fill
`_blendFastSrcAlpha` branch into `BlendSrcAlphaAdd` - the generic path's
operations with the factor tuples folded (fs = src.A, fd = 1 - src.A, ADD,
same multiply/add order and clamps); every other config keeps the generic
path. Exactness: the existing `Blend_AlphaBlendsOverDestination` test uses
exactly this config (0x0302/0x0303/0x8006) and pins the blended pixel;
menu/triangle 1B SHAs exact, all counters exact, normalized stdout
byte-identical to r7 (`D:\spec-soak\r9`). **Wall effect NOT yet claimed:**
the measurement legs ran while the host was busy (Rider active, then
Fortnite - the operator rule pauses timing for the game); the contended
single-run raster seconds (36.5/13.3 s vs 30.2/9.9 s) are not evidence. The
interleaved A/B (old-vs-new raster seconds) is pending a quiet window; the
change is a strict op-count reduction for the dominant config and is
reverted if the quiet measurement disagrees.

### 186a. Blend slice quiet-host A/B + session closing medians (2026-10-18, direct)

Interleaved A/B (`D:\spec-soak\ab-blend`; pre-blend binary built from HEAD~1
staged beside the current one, 3 paired rounds, order alternating, every leg
SHA-exact):
- menu raster psh: pre-blend 30.56/29.83/30.15 (median 30.15) vs blend
  26.98/26.83/26.90 (median **26.90**) - **3/3 paired wins, -3.25 s**
  (10.8% of the rasterizer; covers 211.7M blended fragments).
- menu wall: 78.9/75.1/75.4 (median 75.4) vs 79.1/74.3/73.5 (median 74.3) -
  2/3 wins; the wall carries ~4 s of host drift across the campaign, but the
  closing median (74.3 s) sits exactly 3.3 s under the S183 median (77.6 s),
  matching the raster delta.
- triangle: raster 10.16/9.62/10.16 vs 10.05/9.60/9.90 (unchanged, as
  expected - only 382 blended fragments); walls within noise.

**Session closing medians (median-of-3, interleaved, quiet host, SHAs
exact): menu 74.3 s vs the 143.7 s baseline (1.93x); triangle 36.6 s vs
71.6 s (1.96x).** Rasterizer-only: menu 119.2 -> 26.9 s (4.4x), triangle
54.7 -> 9.9 s (5.5x). The blend slice is claimed on the raster delta (3/3
paired wins) with the wall median as corroboration; no other claim changed.

## 187. Rasterizer slice R7: lean interpolation for modulate fills (2026-10-18, direct)

Fresh ablation on the current binary attributed the menu's 26.9 s raster as:
barycentric + interpolation + loop ~10-11 s, sampler ~8-9 s, fragment ~8-9 s
(fast combiners ~1-2 s). The kind-1 modulate config (402,600 fills, ~245M px)
consumes only the interpolated diffuse, stage-0 texcoords and depth -
specular, stages 1-3 texcoords and the fog factor (16 of 24 interpolated
vectors) are dead for those fills whenever stages 1-3 are unbound.
`FillTrianglePsh` now computes only the live vectors under a per-fill
`leanInterp` condition (kind==1 && texModes[1..3]==None; all k1 fills
qualify: `lean=402600` measured). The computed values stay operation-for-
operation identical, and no reader of the skipped vectors exists under the
condition (the resolve loop skips None-mode slots, no dot mode can live in
stages 1-3 for kind 1, the modulate evaluator reads only texels[0]).

Gate (`D:\spec-soak\r10` + `D:\spec-soak\lean-median`): SHAs exact, counters
exact, normalized stdout byte-identical to r9. Median-of-3 menu raster
26.90 -> **24.55 s** (24.38/24.55/24.69), menu wall 74.3 -> **70.8 s**
(70.4/70.8/71.0). Triangle unchanged (k1=0 fills; 9.90 s raster).

Closing numbers: menu **70.8 s vs 143.7 s baseline (2.03x)**, rasterizer-only
menu 119.2 -> 24.6 s (4.9x); triangle **36.6 s (1.96x)**, raster 54.7 ->
9.9 s (5.5x).

## 188. Sampler counter batching measured neutral, reverted (2026-10-18, direct)

Fresh ablation on the lean-path binary: skipping the texel-resolve loop cuts
the menu raster 24.55 -> 15.96 s (the sampler is ~8.6 s, 35% of the raster -
still the largest single block). A per-fill accumulation of the sampler
diagnostics (per-slot format attempts/misses, per-state samples/rgbZero/
misses, min/max seeding, first-address capture - flushed at fill end with
pre-fill snapshotted conditions, exact semantics) measured NEUTRAL: menu
raster 24.50 s vs the 24.55 s median (SHAs exact, counters exact). The
per-sample counter updates were already ~1-2 ns each and the accumulation's
per-call branch offsets them. Reverted. This is the fifth neutral/reverted
experiment of the campaign (write scopes/page memo, byte->float tables, TLB
resize, counter batching) - the remaining sampler cost is the irreducible
per-sample work (floor/wrap/address/decode/read) and the remaining raster is
at its practical floor for this architecture.

## 189. Session close: full-suite re-run attempt filed as an open item (2026-10-18)

Attempted the full 378-test ktest suite on the closing binary (full
config.txt, patched XBE, `--break 4E03A --writedump`, 40B-step budget). The
run did not reach the park within the window and produced no writedump -
inconclusive, not a session gate (the standing 7-test smoke passes 7/7 on
the same binary). A 75-second `--hotspot` probe of the full config shows the
guest advancing ~1.3M steps/s with a **33% TLB miss rate** (vs 9.5% in the
menu boot; 100M steps, decodeCacheHits 99.35%, headVisits 15%) - consistent
with heavy flush-driven TLB churn in the suite's test code (the §185 finding
that the TLB misses are flush-driven, not capacity-driven). Next step if
picked up: profile the full-config boot with per-trigger flush counters (the
decode-flush audit's §7 probes) before theorizing; the 7-test smoke remains
the standard cross-check meanwhile.

## 190. Fast-mode JIT track chartered (2026-10-18, operator decision)

Operator decision: pursue **at least xemu-class performance** long-run; repo
constraints may be relaxed to speed development, but the GPL/TCG-transport
route was rejected — QEMU TCG is entangled with QEMU, and a C# port is both a
GPL derivative work and ≈ equal effort to an own emitter; MIT references
exist (Ryujinx ARMeilleure infrastructure, Iced encoding). The project stays
non-GPL.

Charter filed: `docs/research-notes/jit-track-charter.md` — governing doc for
the track:

- **Dual-mode contract.** Accurate mode unchanged (gate currency,
  boundary-preserving, byte-identical contracts). Fast mode (`--jit`,
  opt-in) relaxes TSC/step/IRQ observability to block granularity while
  retaining MMIO program order + fault precision; `--jit-exact` is the
  bit-exact debug variant; one engine per run; fast mode is never gate
  currency.
- **Phases 0–4 with kill criteria.** P0 bench/verify harness; P1
  DynamicMethod block JIT — differ in kind from E1–E3 (whole-block
  straight-line, register-cached guest state, inlined RAM fast paths,
  block-local flags); target ≥2.5–3× bench CPU-side, kill <1.5× after 4
  focused weeks or 3 structural iterations; P2 lazy flags/chaining/tiering
  (5–10×); P3 raw emitter only if P1–2 plateau >3× short of target; P4
  fast-mode playability (GPU coverage/pacing/audio/input) after P2.
- **Two-tier validation.** Exploration slices = bench + unit differentials;
  promotion = the full accurate-mode ceremony + sync-point differential +
  ktest outcome.
- **Constraint deltas approved.** `DynamicMethod` authorized for fast mode
  only; Iced (MIT) approved for Phase 3 (not yet referenced); per-slice
  ceremony relaxed to two-tier; games rule unchanged (fast-mode
  benchmark-only use pends an explicit operator decision); license stays
  non-GPL.
- **Why now (measured).** Interpreter track asymptoted (fallbacks −66% vs
  walls 1.07–1.19×, §§149–169); relay emission wall-neutral (E1–E3,
  §§171–179b; §20.6 microbenchmark); current post-R7 walls menu 70.8 s /
  triangle 36.6 s per 1B steps (§186a/§187) vs the 10²–10³ Msteps/s
  real-time band (`gpu-swapchain-brief.md` §5 math).

Updated pointers: `development-direction.md` §1/§2b/§3/§4; `AGENTS.md`
"Development direction" + next-session; `ImplementationPlan.md` Phase 15 +
Track C + status table. Next work item: Phase 0 bench + `--jit-verify`
harness.

## 191. Fast-mode JIT J1a: bench harness + first whole-block engine (2026-10-18 night, direct)

Charter Phase 0/1 started the same day. Landed:

- **`boot --bench [--bench-name]`** (Phase 0): one grep-able line after the
  run loop — `BENCH name= steps= wall= msps= ramSha256= cpuSha256=` (CPU SHA
  over `XboxCpu.SaveState` bytes). Validated: hashes byte-identical across
  repeats; the flag-off logs unchanged (guarded print).
- **J1a engine** (`--jit`; `XboxCpuJit.cs` / `JitBlockCache.cs` /
  `JitEmitter.cs` / `JitPlanner.cs`): whole-block DynamicMethod runners that
  keep all eight GPRs, EIP, EFLAGS and TSC in IL locals across the block;
  inlined translate-cache probe + RAM-direct read/write (same guard set as
  E3-1b: cross-page, WP replay, watched ranges, NoteStorePhysical order);
  every non-fast case takes the exact interpreter path (`ReadMemory`/
  `WriteMemory`) after a full local→context sync, so faults unwind at the
  member boundary exactly. Fast mode's only relaxation vs the interpreter:
  the IRQ poll is once per block (charter §2). Head-keyed cache with
  generation-stamped verdicts (rebuilt on decode-cache invalidation),
  permanent rejections cached, transient formation bails retried.
- First differential tests (12): mov/alu/flags loops, jcc condition sweep
  (32 rows), cross-page memory, paged fault delivery, rejection fallback,
  fuzz (seeded random programs).

Found+fixed en route (all caught by differential tests, never in titles):
`MovRR32` loaded destination/stored source (operands swapped); register-only
members never advanced the EIP local; `cmp [m],imm` (planned as AluMI)
stored its result because the no-store check only covered AluMR.

Measurements (single runs, same host window; absolutes host-shifted vs the
rasterizer campaign — the triangle plain-default 1B wall measured 63.7 s
today vs the §186 median 36.6 s, so only same-window ratios are quoted):
triangle 10M/100M `--jit` runs were **bit-identical** to plain (RAM+CPU SHA)
in J1a, no fault.

## 192. Fast-mode JIT J1b–J1d: shapes, CallSpec, tuning, exact mode, and the real-time-band result (2026-10-18 night, direct)

**J1b shapes.** 8/16/32-bit mov/alu (incl. Adc/Sbb) RR/RI/RM/MR/MI, test MI,
inc/dec mem, mov m,imm, lea, movzx/movsx (both widths, reg+mem), shift-imm
(7 ops), push/pop/call/ret/jmp-indirect; generalized memory probe helper.
**J1c.** Cli/Sti as flag members (defragments the kernel idle loop — the top
two heads, each 5.3M visits at 100M); `--jit-threshold N` (default 8; visit
count before a head is built — a DynamicMethod compile costs ~1 ms and
one-shot cold code must never pay it; building at every first sight built
4415 blocks for ~9 s of pure compile time); `--jit-exact` (per-member IRQ
poll mirroring Step's order; bit-exact); `_jitProgress` fault accounting;
run diagnostics (runnerMs/outerMs/nonJitMs/buckets/slowMembers/calls).
**J1d.** CallSpec fallback members: any valid non-control-transfer
instruction with a static spec handler runs that exact handler inside the
block after a full context sync + state reload (x87, scalar SSE, shift-by-CL,
not/neg, rdtsc, …) — control transfers and CS-affecting ops excluded; the
handler owns EIP (double-advance bug fixed). RunJitBlock now chains blocks
inside one call until budget/boundary (628K calls vs 18.5M per 100M) with a
32-entry direct-mapped head memo and loop-invariant fetch base/decode sizes.

**Correctness evidence (the strong part).**

- 29 JIT differential tests green (full state incl. FPU/XMM/RAM, IRQ sweeps
  single + periodic, fault delivery, paged, fuzz over 24-shape programs).
- **1B exact-mode runs are bit-exact vs the accurate control**: menu
  `ramSha256=D9C99A58… cpuSha256=3D2740B8…` and frame
  `4EF1FBF66CB96CD7…` (= recorded golden), frames=686 stalls=686;
  triangle `5E6A6741…/9AC069E6…` and frame
  `A1740DE069128A04…` (= recorded golden), frames=68 stalls=34. 266M
  (menu) / 355M (triangle) instructions served through emitted code with
  pixel-identical output.
- Fast mode diverges from the accurate SHAs at high coverage — **proven to
  be IRQ-position timing only** (exact mode on the same binary is bit-exact;
  the divergence appears only once coverage × IRQ density is high). Fast
  mode is per the charter never gate currency.

**Measured rates (1B steps, same host window, single runs — no median
claim; ratios robust, absolutes host-shifted).**

| run | wall | Msteps/s |
|---|---:|---:|
| menu plain-default | (not re-run) | — |
| menu spec-ctl (`--spec-exec`) | 88.05 s | 11.4 |
| menu **fast** (`--jit --jit-threshold 16`) | **13.07 s** | **76.5** |
| menu exact | 109.9 s | 9.1 |
| triangle plain-default | 63.7 s | 15.7 |
| triangle spec-ctl | 44.7 s | 22.4 |
| triangle **fast** | **13.14 s** | **76.1** |
| triangle exact | 44.9 s | 22.3 |

Coverage at 1B: menu 97.1% served, triangle 97.6%; runner time itself ≈
6.5 s of the 13.1 s (the rest is RunJitBlock bookkeeping + the non-served
tail). **Both titles land at ~76 Msteps/s — the 10²–10³ Msteps/s real-time
band the charter called xemu-class.** Threshold tuning at 100M: 2→7.2 s
(compile churn), 4→3.7, 8→3.09, 16→2.99 (served 76.1M), 32→4.44 (**cliff**:
the hot heads sit at 16–31 start-of-block visits, so default stays 8 as the
robust point; re-tune at 1B where visit counts are ~10× higher).

Open items carried forward: fast-mode block chaining beyond the inner loop
(direct block-to-block jumps); shape tail (strings, mul/div, push/pop
memory forms, shifts-by-CL native); fast-mode disassembly dump; re-measure
with medians once the host is quiescent; measure the menu fast mode with
rendering-identical interleaving (the fast run renders 1 frame vs 686 — its
work composition differs, see below).

Caution recorded: fast-mode runs execute the guest on a timing-perturbed
schedule, so their *work composition* can differ (the fast menu run
presents 1 frame vs the control's 686; the triangle 1 frame vs 68) — rate
comparisons are like-for-like per instruction served, but not per unit of
guest work.

**Kernel-test-suite validation (same night).** Standing accurate smoke
re-run after the JIT work: **PASS 7/7 rows green in 01:18** (xgrid 4/7 vs
xemu 2/7, known deltas match). JIT on the suite workload (staged subset ISO,
2B steps, Execute-driven — the `--break` leg uses `Step` and bypasses the
JIT by design): accurate `722BCBA2…/AB1417FA…` 88.9 s; **jit-exact
bit-identical SHAs** (`722BCBA2…/AB1417FA…`, 1.35B instructions served,
faults=0) 84.2 s; **fast 23.6 s = 84.7 Msteps/s, 98.5% served**, no fault
(the fast SHA differs by timing as on the titles).

**Fast-mode medians (1B, interleaved, same window, th=16).** triangle
12.95/14.45/13.02 → **13.02 s median = 76.8 Msteps/s**; menu
13.04/12.82/12.84 → **12.84 s median = 77.9 Msteps/s**. Threshold at 1B is
insensitive within ~3% (8→13.33, 16→~13.0, 24→13.19). vs the §186 recorded
medians from a different day/host state (menu 70.8, triangle 36.6 plain-
default): 5.4× / 2.8× — cross-day numbers, quoted with the §176 caveat;
same-window controls: menu 88.05 s, triangle 44.65–44.9 s.

## 193. Fast-mode lost-wakeup fixed: the post-STI poll (2026-10-19 early, direct)

**Symptom.** While auditing the 192 fast-mode runs' frame counts: both 1B
fast runs presented 1 frame vs the control's 686 (menu) / 68 (triangle) —
the "76 Msteps/s" numbers were a *wedged* guest spinning in the kernel idle
loop, not the workload. Triangle bisect: EIP and interrupt counts identical
to the control through 32M; between 32M and 35M the fast run's "Interrupts
delivered" froze at 267 (control went 270 -> 292 -> 365) and the guest
wedged at 0x8001B047 forever.

**Root cause.** The kernel idle thread's loop (0x8001B031):
`sti; nop; nop; cli; cmp ebp,[ebp]; je spin` — its only IF=1 window is
between STI and CLI *inside one block*, and the block entry always runs
with IF cleared (the previous iteration's CLI executed last). The
once-per-block entry poll therefore never ran with IF=1: the pending
interrupt was never acknowledged and every wakeup starved. That is not the
charter's tolerated "<block-length delivery shift" — it is a systematic
lost wakeup that invalidated the 192 fast-mode rate claims.

**Fix (3211788).** STI members poll at their own boundary in fast mode
(the interpreter delivers at the very next instruction boundary): the poll
acknowledges, records `_jitPending`, and exits with exact boundary
accounting (STI counts as served, the delivery consumes one unit, TSC gets
the boundary increment, return address = instruction after STI). POPF —
the only other instruction that can enable IF — leaves the member set
(block ends before it; the next entry poll handles the restored IF).
Exact mode is unchanged (its per-member poll already covered both).

**Evidence (deterministic; wall clocks ignored — Fortnite was running).**
- Triangle 1B fast: ram/cpu SHA = the control's (5E6A6741... / 9AC069E6...),
  frame A1740DE0... = golden, 68/34 frames — **bit-exact**.
- Menu 1B fast: D9C99A58... / 3D2740B8..., frame 4EF1FBF6... = golden,
  686/686 — **bit-exact**.
- ktest 2B fast: cpuSha = accurate's; RAM identical through 1.2B; at 2B the
  only difference in the whole suite log is one measured duration
  (NtResumeThread: completed in 1.359 s vs 1.354 s) — every test row
  identical.
- Triangle trajectory: EIP + interrupt counts identical to the control at
  35M/50M/100M; at 200M the guest is rendering (2622 draws, 65.8M pixels).
- +4 regression tests (STI-window starvation sweep); suite 1466 green.

**What it changes.** The 192 caveat "fast mode renders 1 frame vs 686; work
composition differs" is obsolete: fast mode now executes the same workload
bit-for-bit on both titles at 1B. One residual relaxation remains on paper
(a device raising an IRQ from a guest MMIO action mid-block is delivered at
the next block boundary — the ktest 5 ms measurement is that allowance seen
in the wild). The dual-mode contract stands unchanged procedurally.

**Next frontier (filed).** With the guest no longer spinning, the JIT-served
share of the *real* workloads is 26.6% (menu) / 35.5% (triangle) at 1B; the
unserved tail now dominates the fast-mode wall. Next performance work:
coverage of that tail (RunJitBlock zero-return reasons + non-JIT
instruction mix), then quiet-host medians of the post-fix fast mode.

## 194. Coverage campaign: the unserved tail, the flush amnesia, and the new members (2026-10-19, direct)

**The problem.** With the guest no longer wedging (193), the fast-mode wall
at 1B was dominated by the 73% of menu instructions running through the
member path. The 193-era fast numbers measured a spinning guest; the real
workload needed coverage, not runner micro-tuning.

**Diagnosis (new `--jit-tally` diagnostics).** Three distinct losses:
1. **Spec-run swallowing**: one RunSpecBlock call served whole ~20K-region
   stretches (33K runs = 93% of the unserved mass at 1B), so no head inside
   them was ever probed.
2. **Visit-counter amnesia**: the guest rewrites page tables constantly
   (5K-66K PT-page stores per 100M; every one full-flushes the decode cache)
   - 23,865 generation bumps per 200M reset every pending head's visit
   counter, so hot heads never reached the build threshold.
3. **Formation dependence on member-path warming**: after a code-page drop
   the page's decode entries are gone; formation walks bailed transiently
   and got retry-suppressed (1.73M transient bails at 1B).

**Fixes.**
- `--jit-slice N` (default 256): the non-JIT fallback runs the spec path in
  slices so the fast engine gets a head probe every N instructions.
- Lazy block revalidation: on a generation change a built block is offered
  as `Verdict.Stale`; the CPU re-checks every member's 15-byte source
  window against the *current* mapping (fresh translate + bytes) and keeps
  the block if byte-identical (baked code depends only on the decoded
  bytes; decode sizes are checked separately). Mismatch/fault drops it for
  a fresh formation. 1.1M-1.5M revalidations per 1B - blocks survive the
  guest's page-table churn instead of rebuilding from scratch.
- Pending/rejected verdicts keep their visit counters across generation
  changes (only the verdict is generation-bound).
- Fill-on-demand formation: the walk decodes missing members itself
  (FetchFast + DecodeFresh) instead of depending on the member path.
- New members: indirect `call r/m32` + `jmp m32` terminators, `Popf` as a
  CallSpec member with a post-member IRQ poll (the STI rule), `Movs`/`Stos`
  as exact interpreter-call members (ExecuteStringIteration with full
  sync/reload; do not end blocks anymore), `Cld`/`Std`, `Invlpg` (computes
  the operand address and drives the interpreter's flush inline).

**Results (menu 1B; deterministic; loaded host).** Served share: 26.6%
before the slice -> 76.8% (slice) -> 96.9% (revalidation) -> **98.2%**
(members). FormBails 1.76M->38K; transient bails 1.73M->5.5K; unserved
instructions 734M->18M. All SHAs and the golden frame (4EF1FBF6...) stay
bit-exact throughout; loaded wall 141.8 s -> 105.0 s at identical load
(idle-median re-measurement still pending).

**Triangle fast-mode residual (documented, contract-covered).** After ~550M
the triangle fast run diverges in exactly 3 RAM dwords (0xB7FFC and the
qword at 0x121850/54) while cpuSha, both counters and the golden frame
A1740DE0... all match at 1B. `--watch` shows the qword is a saved
context frame: exact's last writer is the kernel's context-save
(0x8001A42C, value = kernel EIP + EFLAGS=0x206), fast's is a user EIP
(0x2883D, user stack pointer). That is a mid-block device-IRQ delivery
shift at one context save - the charter's explicit relaxation - and the
resume works (hence identical cpuSha/frame). Exact mode remains bit-exact
end to end; the menu fast run is fully bit-exact at 1B.

**Tests.** 43 JIT differential tests (adds: flush-mid-run revalidation,
self-modifying code, indirect call reg/mem + jmp mem, POPF poll sweep,
Movs/Stos both directions, INVLPG). Suite 1475 green, format clean.

**Amendment (same day): all string ops + port I/O as members, and a real
poll-exit bug found by the bit-exactness gates.** `Movs/Stos/Cmps/Scas/
Lods/Ins/Outs` (including the rep forms, via the interpreter's ExecuteString
as a CallInterpreter member) and `In/Out` (ExecuteIn/ExecuteOut) joined the
member set with post-member IRQ polls (both are device-facing; the
interpreter would deliver a raised vector at the next instruction
boundary). First attempt diverged massively (302K dwords at 100M): the
poll-exit path advanced EIP only for Sti, so an interpreter-call member
exited the poll with EIP still on itself and re-executed after the handler
(double port I/O, PIC/PIT derailment). Fix: every poll-after member that
does not own EIP advances it on the poll exit (`advanceEip = kind !=
CallSpec`). After the fix: menu 1B fast is fully bit-exact again (ram/cpu
SHA + golden frame, 686/686), triangle fast keeps its documented 3-dword
context-save residual (identical across builds; exact mode bit-exact), and
the ktest suite log stays identical except the single 5 ms NtResumeThread
measurement. Served share at 1B menu: 99.0%.

**Amendment 2 (quiet host, load 3-5%): the honest end-to-end numbers.**
The 192-era "76 Msteps/s" fast figures were a wedged guest (193); after
the fix + coverage campaign (194), the fast mode runs the real workloads
bit-exactly (menu) or with the documented 3-dword residual (triangle), so
the end-to-end walls are finally comparable. Medians of 3 (fast), single
controls, all SHAs verified:

| 1B run | plain (interpreter) | spec-control | fast | fast vs ctl | fast vs plain |
|---|---:|---:|---:|---:|---:|
| triangle | 51.28 s (19.5 Msteps/s) | 37.10 s | **27.91 s** (35.8 Msteps/s) | 1.33x | 1.84x |
| menu | 81.91 s (12.2 Msteps/s) | 73.61 s | **64.02 s** (15.6 Msteps/s) | 1.15x | 1.28x |
| ktest 2B (CPU-bound) | - | 88.9 s (accurate) | **23.6 s** | 3.76x | - |

Breakdown (`--gpu-time` + JIT stats, quiet): menu fast 63.3 s = CPU-JIT
~23.4 s + bookkeeping 4.8 s + non-JIT 10.8 s + **software rasterizer
24.3 s (38% of the wall)**; triangle fast 27.7 s = CPU-JIT ~4.3 s +
bookkeeping 4.5 s + non-JIT 8.8 s + **raster 10.1 s (36%)**. The runner's
non-raster time is dominated by MMIO slow-path members dispatching to
NV2A devices (118.9M slow members on menu 1B; 21.3M triangle) - real
device work fast mode cannot remove. So: the fast JIT is ~3.8x on
CPU-bound code, ~1.5x/1.3x CPU-side on the triangle/menu, and 1.15-1.33x
end-to-end because both walls are now owned by the software GPU path; the
next levers are Track B (GPU backend/rasterizer) and MMIO-path work, not
further CPU-side JIT tuning.

**Amendment 3: the GL backend is not a wall lever for the 1B workloads
(measured, quiet host).** Pairing fast/accurate modes with `--gpu gl` (all
guest SHAs identical, frames correct): menu fast sw 64.02 vs gl 71.16-72.48
(worse by 7-10 s), menu spec-ctl sw 73.61 vs gl 83.65 (also worse - the
extra cost is CPU-side and mode-independent); triangle fast sw 27.91 vs gl
27.48 (parity). The GL path's draw/readback timings are small (menu
drawMs 6.85 s vs sw raster 24.3 s) but its per-method/per-draw CPU cost on
2.76M tiny draws / 18M methods replaces the saved raster time. Conclusion:
for these tests the software rasterizer is the better default; GL wins
would need batched/bigger draws or per-draw overhead work, and that track
stays gated as chartered. The end-to-end fast-mode story is therefore
CPU+MMIO-bound: 27.9 s triangle / 64.0 s menu (1.33x / 1.15x vs spec-ctl).

## 195. Performance re-aim: GPU backend + MMIO lead the xemu-class campaign (2026-10-19, operator-directed)

**Decision.** The operator asked whether the plan needs changing to reach
xemu-class performance long-run. Answer: the architecture and the
correctness strategy stay; the *performance priority order* changes. Track C
(fast-mode JIT) is retired as lead track after reaching 99% served / 3.76x
CPU-bound / bit-exact menu 1B (§§191-194). The lead tracks are now:
**B — GPU backend performance** (the software rasterizer is the largest wall
item), **D — MMIO fast dispatch** (119M slow members at 1B menu), then
**C — JIT Phase-2 leftovers** (chaining, threshold; ~5-8%).

**Evidence (quiet host, all SHAs verified; §194 am.2/am.3).**
- 1B menu fast 64.0 s = CPU-JIT ~23.4 s + bookkeeping 4.8 s + non-JIT
  10.8 s + software raster 24.3 s (38%). Triangle fast 27.9 s = CPU-JIT
  ~4.3 s + bookkeeping 4.5 s + non-JIT 8.8 s + raster 10.1 s (36%).
- MMIO slow-path members: 118.9M (menu) / 21.3M (triangle) — the largest
  CPU-side line item after the rasterizer; each pays a full context sync plus
  device dispatch (real device work cannot be removed, the generic overhead
  can).
- CPU-bound ktest 2B: 3.76x (23.6 s vs 88.9 s accurate) — the CPU-side target
  is met; further CPU-only tuning cannot move the golden walls much.
- GL backend pairing: menu fast sw 64.0 s vs gl 71.2-72.5 s (worse);
  menu spec-ctl sw 73.6 s vs gl 83.7 s (worse, so the cost is mode
  independent and CPU-side); triangle fast sw 27.9 s vs gl 27.5 s (parity).
  Its drawMs (6.85 s menu) beats the sw raster (24.3 s) but per-draw /
  per-method CPU cost cancels the win — this is B2's target.

**Target definition (new gate class).** 1 guest-second = 733M steps (PIT
cross-check §68). xemu-class = content runs at wall <= guest-time, with the
bar measured against xemu on the same `data/test-samples/` workloads
(triangle ISO, ktest smoke): wall, frames, frames-per-wall-second.
Today's fast mode is ~2.1% (menu) / ~4.9% (triangle) of real-time — the
campaign is a multi-slice effort, not a tuning pass.

**Slices + acceptance tests** are recorded in `ImplementationPlan.md`
("Performance campaign — xemu-class re-aim (2026-10-19)"): B1 GL per-phase
profiling, B2 per-draw overhead elimination (menu fast+gl <=45 s, triangle
<=20 s, golden policy), B3 method-stream coverage (zero unsup, counters
identical), B4 present polish on need, D1 MMIO fast dispatch (-50% slow
members, bit-exact), C1 JIT leftovers (>=5% both goldens).

**Unchanged.** LLE-first + reference-first; accurate mode is gate currency;
fast mode never is; headless `--gpu sw` default + no mixing + no async;
quiet-host medians for wall claims (pause and ask the operator when Fortnite
is running); no real Xbox games in the sample corpus; non-GPL; Phase 3 raw
emitter stays not indicated.

## 196. B1+B2a: GL replacement mode landed — the GPU path now replaces the software rasterizer (2026-10-19, direct)

**B1 finding (mechanism).** The GL backend was an observation *mirror* by
design (interface contract: "the software path executes exactly as today,
unconditionally") — `FillTriangle`/`FillTrianglePsh` called `Backend.Draw`
and then rasterized in software anyway. That is exactly why the measured
`--gpu gl` walls were `sw + drawMs` (menu 64.0 -> 71.2-72.5 s; triangle
27.9 -> 27.5 s) rather than `sw - raster + drawMs`: the backend could only
add its per-draw cost, never remove the 24.3 s / 10.1 s software raster.

**B2a implementation.** `IPgraphBackend` gained `ReplaceMode` + `TryDraw`
(default interface members; mirror backends unchanged). `--gpu-replace`
(implies `--gpu gl`) selects replacement mode: the two fill sites skip the
software rasterizer when `TryDraw` succeeds (unsupported draws fall back to
software, never to a partly-mapped state); `GlPgraphBackend.TryDrawCore`
returns success/failure per draw. Clears stay software+mirrored (cheap, and
they keep the cleared surface coherent in guest RAM).

**Measured (quiet host, 1B, fast mode, BackendUnsupported=0 everywhere):**

| run | sw fast | gl mirror | gl replace | vs sw | vs spec-ctl | vs plain |
|---|---|---|---|---|---|---|
| menu | 64.02 s | 71.2-72.5 s | **48.29 s** | 1.33x | 1.52x | 1.70x |
| triangle | 27.91 s | 27.5 s | **17.82 s** | 1.57x | 2.08x | 2.88x |

Acceptance (campaign table): menu <= fast+sw - 15 s (= 49.0) PASS;
triangle <= 20 s PASS. Software raster seconds 0.00/0.00 (0 calls) on both;
drawMs 7.07 s / 0.38 s; readback 0.78 s / 0.14 s. The triangle run is the
fastest end-to-end 1B result to date (56.1 Msteps/s; 7.6% of real-time).

**Fidelity.** CPU SHA exact on both titles (the sample corpus never reads
the framebuffer back). Presented-frame policy vs the sw goldens: menu 3193
diff channels / triangle 34908, **maxDelta 1, violations 0** on both (same
policy class as mirror mode). Guest-RAM divergence: triangle 1,531,742
dwords (6.1 MB), first diff 0x32A4000, last 0x3F5AB40 — **zero divergence
below the first rendered surface and above 0x4000000**: all of it is
renderer-owned surface memory (flip-chain color + zeta + render targets)
that software used to write; kernel/code/data are byte-identical. This is
the documented replacement-mode RAM contract (fast mode / playability
only; accurate mode keeps the software path as the pixel source).
Mirror-mode golden re-verified after the change: sw SHA 4EF1FBF6...,
diff violations=0 maxDelta=1 — GOLDEN: PASS. Suite 1477 green, format
clean.

**Remaining (B2b).** drawMs 7.07 s is now the largest GPU-path line item
(~2.6 us/draw across 2.76M draws): per-draw state caching, uniform
handling, vertex upload. Then RAM writeback for content that reads the
framebuffer back (required before real-game use; the sample corpus does
not read it).

**Amendment (B2b, same day): per-draw overhead pass = semantically
transparent, wall-neutral — the draw cost is driver-side.** Landed:
`ApplyState` GL-state shadowing (blend/color-mask/depth/stencil groups are
re-issued only when the resolved values change; only the ctor and
ApplyState touch those states), per-program uniform caches (scalar 14 +
texture 32 + consts 72 floats; GL uniform state lives on the program
object so unchanged uploads are skipped), and a non-allocating
`PshCombiner.FillConsts` replacing the 19-array `BuildConsts` allocation
per draw (2.76M draws -> 52M allocations removed; GC pressure gone for
sustained play). Evidence: the refactored replace run is **pixel-identical
to the pre-refactor run** (BMP byte-equal) and still within policy vs the
sw golden (maxDelta 1, violations 0); drawMs 7.07 -> 6.96 s and wall
48.3 -> 50.2 s across single runs = neutral within host noise. Conclusion:
the remaining ~2.5 us/draw is NVIDIA driver draw-call cost (per-draw
BufferData orphan + DrawArrays + validation), not redundant state or
allocations — the next GPU-path lever is **B2c: draw batching** (move the
per-draw uniforms to attributes/UBO and issue one call per state run),
which is a shader/backend redesign slice, not a micro-optimization.

## 197. D3a: cross-page decode-cache entries; the triangle's real bottleneck is a page-end spin loop (2026-10-19, direct)

**Diagnosis first (measured, not guessed).** The triangle 1B fast run showed
nonJitMs 10.8 s of a 32 s wall with exactly 1,281,917 non-JIT calls /
6,152,091 instructions, and the decode-cache skip counters matched the call
count 1:1 (crossPageSkips 761,045 + nonRamSkips 359,476 = 1.12M events). A
temporary split showed nonJitSpecMs 8.4 s vs nonJitStepMs 2.0 s; a temporary
non-JIT EIP histogram pinned the single largest contributor at the kernel's
vblank-wait spin: EIPs 0x80060FF3/FF6/FF9/FFB/FFE — **the last 13 bytes of a
4 KB page** — each ~131,070 entries. A fetch window is 15 bytes, so any
instruction whose EIP sits in the last 14 bytes of a page can never enter the
decode cache; the JIT block builder (`FormJitBlock` page-edge guard) and the
spec runner both reject such heads, and every iteration falls to `Step()`
plus a 15-translate legacy fetch. The vblank spin is thus executed at
interpreter-ish speed for the whole run.

**D3a (landed).** `DecodeCacheEntry` carries a second physical base + split
offset for cross-page windows; `FetchFastCrossPage` (XboxCpu) does one
translate per page with fault/note semantics identical to the per-byte loop
(profiler runs keep `FetchCounted`); `DecodeFresh` fills cross-page entries
when both pages are cacheable; the reverse map registers BOTH pages so a
store to either drops the entry; the idle-note and verify readers use
`PhysicalOf(i)`; block formation stays single-page-bounded (relaxing that is
D3b). Evidence: menu 1B fast ram/cpu `D9C99A58…`/`3D2740B8…`, triangle
`BD2DD021…`/`9AC069E6…` (exact anchors), exact-mode 200M identical
(`F91A9D1E…`/`4135499D…`), a full `--decode-verify` triangle run reports
verifyFailures=0 and reproduces the accurate SHA `5E6A6741…`, crossPageSkips
761,045 -> 0 (documented counter change), suite 1479 green, format clean.
Wall effect in-window: triangle nonJitMs 10.8 -> ~9.0 s, wall ~27.6-30 s
(within host drift); the fetch was only part of the per-event cost — the
remaining cost is the page-end spin's per-iteration Step()+spec pipeline,
which the next slice targets (fast-mode spin skip).

**Real-content policy (operator-delegated 2026-10-19).** The operator offered
real ISOs at `H:\Tests\XBOXTest` and delegated the rule decision. Decision
(recorded in `development-direction.md` §4 and `AGENTS.md` rule 3):
`data/test-samples/` stays gate currency; real ISOs are authorized for
fast-mode **benchmark-only** use (wall/throughput and xemu-class
comparisons) — never golden gates, never accurate-mode acceptance, never
NV2A/APU fidelity work. Rationale: the samples (including the real dashboard
menu) cover the hot paths, but the xemu-class target needs at least one
real-title throughput point; the guardrails keep gate discipline intact.

**xemu baseline (measured, same host).** ktest smoke workload: xemu completes
the 7-test subset in 15 s; XGrid fast mode needs ~9-10 s for the same guest
work (793M steps to the park) — CPU-bound work is already ahead of xemu. The
nxdk triangle (guest-side counter read from xemu screenshots, framebuffer
pmemsave + PNG): xemu renders 2,093 frames in 30 wall-seconds (~70 fps,
guest FPS 61-62 = real-time); XGrid fast presents 68 frames in 27.9 s (sw) /
17.8 s (gl-replace) = 2.4 / 3.8 fps. **The xemu-class gap on GPU/vblank-bound
content is 18-29x; on CPU-bound content it is closed.**

## 198. Fast-mode idle-wait skip (--jit-spin): the triangle's spin was the kernel idle loop (2026-10-19, direct)

**Diagnosis.** The §197 page-end spin (0x80060FF3) was only part of the
story: tracing the guest at 400M/600M/900M steps shows it spends most of its
time in the kernel idle loop at 0x8001B02E (sti/nop/nop/cli/cmp/je — the
§111/§193 park), and in a pure delay loop at 0x800422B0 (dec [esp]; jne).
Neither is JIT-expensive per se, but the idle loop is a store-free block
cycle that re-enables interrupts with nothing pending — a textbook
fast-forward target. The existing `--idle-skip` detector cannot see it under
`--jit` (the JIT's inline paths bypass the detector's access notes, and the
JIT is deliberately inert while `_idle` is set).

**Implementation (landed, opt-in `--jit-spin`).** JIT-side detection, no
detector interaction: `JitBlock` now carries `HasSti`/`HasStore` (computed
from the member plans; `AluMR`/`AluMI` count as stores only when the ALU op
is not `Cmp` — the compare forms are pure reads, exactly as the emitter runs
them; the initial classification missed this and reset the streak every idle
iteration, spinMax=1). In `RunJitBlock`, a store-free block with an STI
member increments a streak; at 32 the runner calls `TrySkipIdleWait`, which
advances the host (`IIdleSkipHost.AdvanceIdle`) in `IdleSkipChunkSteps`
chunks until an interrupt is pending (the machine's `StepsUntilNextDeadline`
is null, so the IRQ condition is the stop), advances the guest TSC by the
same count, and reports the steps to both the outer budget and
`TakeIdleAdvanced` (no double device advance). Fast mode only; runs with it
are not bit-exact by construction (time advances in lumps) — the accurate
mode and every gate path are untouched.

**Measured (1B, same host window, single runs; frames identical to the
baselines: 68/34 triangle, 686/686 menu).**
| config | before | with --jit-spin |
|---|---|---|
| triangle fast sw | 27.58 s | **22.95 s** (1.20x) |
| triangle fast gl+replace | 17.82 s | **13.99 s** (1.27x) |
| menu fast sw | 63.19 s | **60.65 s** (1.04x) |
| menu fast gl+replace | 48.29 s | **44.96 s** (1.07x) |
Fast-forwarded steps: triangle 450.6M (45% of 1B), menu 128.6M (12.9%); 7,514/2,160 skip events (~60K steps each — the machine's 65,536-step Execute batch caps one skip). The triangle is the best-ever 1B result (13.99 s; 2.0x vs the 27.9 s sw baseline) and the xemu-class gap narrows from 18-29x to **14.4x** (xemu ~70 fps vs 4.86 fps).

**Why the remaining gap.** With the spin gone the triangle's wall is real
work: the guest's pbkit pushbuffer stores + NV2A method processing + the
software raster (sw) or the GL draw path. The next levers are the JIT's
per-block overhead (237M block invocations at ~4.2 instructions each) and
the non-JIT residue (page-end instructions). Suite 1480 green, format
clean, ktest smoke PASS (1:06).

**Amendment (§198a, same day): the per-invocation runner timing was itself a
top cost.** `RunJitBlock` took two `Stopwatch.GetTimestamp()` calls plus
bucket accounting per block invocation — always on — which on call-heavy
code (237M invocations at ~4.2 members each) cost ~15-25 ns each. Gated
behind the new opt-in `--jit-timing` (default off; bucket *counts* stay
exact, ms fields read 0 without it). Same SHAs, pure diagnostic removal:
**triangle fast gl+replace 13.99 -> 10.68 s (-24%), menu 44.96 -> 41.39 s
(-8%); sw legs triangle 22.95 -> 20.28 s, menu 60.65 -> 56.82 s.** Best-ever
1B table: triangle fast sw **20.28 s** (1.38x vs the 27.9 s baseline),
triangle gl+replace **10.68 s** (1.67x), menu sw **56.82 s** (1.13x),
menu gl+replace **41.39 s** (1.17x). Triangle now runs at 12.7% of
real-time (6.4 fps vs xemu ~70 fps: the xemu-class gap is ~11x, from
18-29x this morning).

## 199. Plan re-aim: the CPU engine is the lead track again; charter Phase 3 unblocked (2026-10-19 evening)

**The question.** "Are we on the right track for xemu-class performance?" —
answered from measurements, not intent.

**Measured structure after §§195–198a (1B goldens, fast mode, quiet host,
single runs).**
- triangle **10.68 s** gl+replace+spin (20.28 s sw) = runner **6.1 s (57%)**
  + nonJit 1.9 s (18%) + GL 0.5 s + bookkeeping ~2.2 s.
- menu **41.39 s** gl+replace+spin (56.82 s sw) ≈ CPU-JIT **17–19 s (~45%)**
  + GL draw 7.0 s (17%) + bookkeeping 4.8 s (12%) + nonJit 3.6 s (9%).
- Runner internals: **125M block invocations at ~18 ns fixed** (context
  load/store + dispatch + memo) + **543M members at ~7 ns** (helper-call
  memory paths). The DynamicMethod block JIT's remaining Phase-2 leftovers
  are worth ~1.5–2× at most.
- xemu on the same triangle: 2,093 frames / 30 wall-s ≈ 1.13× real-time
  ≈ **830 Msteps/s**. XGrid: 94 Msteps/s (triangle) / 24 Msteps/s (menu) —
  **~9× / ~30×** off. CPU-bound work (ktest) stays ahead of xemu (9–10 s vs
  15 s); the gap is the per-instruction cost on non-trivial guest code.

**Verdict.** The 2026-10-19 re-aim ("Track B + Track D lead, JIT leftovers
only") was correct for the *pre-`--gpu-replace`* cost structure and is now
stale: with the rasterizer gone, the CPU engine is again the largest line
item on both titles, and its engine is structurally capped. The charter's
own Phase-3 entry condition ("plateau >3× short of the xemu-class target")
is **met**. This is a plan correction, not a product regression: the day's
landed work (gpu-replace, jit-mmio, 256-slot data-translate cache, cross-page
decode entries, `--jit-spin`, `--jit-timing`) is all still net-positive.

**Real-dashboard profile (new evidence).** A 4B-step window-mode dashboard
run (single run, window overhead included, 480 s = 8.3 Msteps/s vs 24
Msteps/s headless — **window mode costs ~3× today**): **53,115,811 GL draws
at ~2 µs → drawMs 106.1 s (22% of the run)**; **12,675 flips each paying a
1.6 ms readback → readbackMs 20.7 s (4%)** although the window path blits the
FBO directly and does not need that readback; 736M slow members; 349M NV2A
method writes. The dashboard's real workload is therefore **draw-submission-
and-present-bound as much as CPU-bound** — B2c batching and the window present
path are not optional polish.

**Decision — execution order (docs updated: `development-direction.md` §1/§2b/
§2c, `AGENTS.md` rule 1, `ImplementationPlan.md` campaign section,
`jit-track-charter.md` status + Phase 2.5/Phase 3).**
1. **D3b + bookkeeping** (cheap, days): identify the page-end wait loop and
   remove the nonJit residue (1.9 s triangle / 3.6 s menu) via cross-page
   block formation or a generalized wait-skip; profile the machine/CLI
   advance path (2.2 s / 4.8 s) before optimizing it.
2. **C2 trace emitter** (charter Phase 2.5, immediate): multi-block traces in
   one DynamicMethod, guest state in locals across the trace, inline RAM fast
   paths, interrupt polls at every block boundary (the §193 wakeup contract),
   side exits to the block JIT. Target CPU-side ≥2×.
3. **C3 raw emitter** (charter Phase 3, Iced now authorized): register
   allocation, direct block chaining (patch-on-link), inline softmmu-style TLB
   probes. Target another ≥2×.
4. **B2c draw batching** (2 µs/draw → ≤0.5 µs) and the **window present
   path** (drop the redundant per-flip readback; batch the pump).

**Milestone ladder (1B, fast mode, gl+replace+spin).** M1 (C2 + D3b +
bookkeeping): triangle ≤6 s, menu ≤20 s. M2 (C3): triangle ≤3 s, menu ≤12 s.
M3 (xemu-class): triangle ≤1.5 s, menu ≤4 s — within ~1.3–3× of the real-time
band. The menu has no xemu oracle (xemu deadlocks at the dashboard animation
wait, §32); its target is the real-time band, the triangle/ktest keep measured
xemu comparisons.

**Amendment (§199b, same evening): the "bookkeeping" line item was a
misattribution.** `XboxMachine.Run` now times `Cpu.Execute` vs device
`Advance`: on the 1B triangle **executeMs 18,475 / advanceMs 51** (wall
18,532 on a loaded host) — the device-advance path and the CLI loop are
~100 ms total, i.e. **the whole wall is the CPU engine**. The earlier "menu
bookkeeping 4.8 s / triangle ~2.2 s" was a derived residual that actually
belonged to the JIT outer-loop overhead (chain dispatch, memo, budget
checks) inside `Cpu.Execute`. Plan consequence: drop the separate
"bookkeeping profiling/optimization" item; the outer-loop overhead is part
of C2 (traces remove per-block dispatch) and is measured by
`Host timing executeMs/advanceMs` from now on.

## 199a. D3b landed: page-edge heads form (fast mode); the non-JIT tail is now the spec runner's job (2026-10-19 evening)

**Change.** `FormJitBlock` drops its page-edge guard (the decode cache has
carried cross-page entries since D3a); `TryFillDecodeEntry` fills cross-page
windows via `FetchFastCrossPage`; `RevalidateJitBlock` reads a page-aware
15-byte window (one translate per page touched); `DecodedBlockCache.Form`
forms *spec* blocks across page boundaries (invalidation is already the
conservative all-block eviction, and the idle-window replay / verify paths
already go through `entry.PhysicalOf(i)`), with the spec verify re-decode
passing the entry's `CrossSplit`. Tests updated to pin the new contract plus
a cross-page formation test.

**Evidence (deterministic, load-independent).** Triangle 1B fast:
`nonJitCalls` 1,281,644 → **522,460 (−59%)**, `rejectedHeads` 431 → **113
(−74%)**, `specRan` now 4.9M (page-edge and cold heads are served by the
~10–20 ns spec runner instead of the ~500 ns interpreter `Step`).
Bit-exactness: triangle 200M **accurate interpreter = `--spec-exec` =
`--block-exec` = `--jit --jit-exact` = `29A00539/B4482225`**; menu 1B fast
anchor `660CA11B/F5D61C94` exact; triangle 1B fast `4F1FCBC0/7DAB63EB`
exact. Suite 1481 green, format clean. Walls not claimed (host loaded —
quiet-host medians pending, ask the operator for an idle window).

**Also landed:** JIT yield-reason counters and the non-JIT mnemonic tally are
always-on diagnostics (the env-gated `XGRID_JIT_TAIL=1` report now works in
ordinary runs; `--jit-tally` stays inert by design); the top yield reason is
`pending` (cold heads below the 16-visit build threshold — by design).

**Next slice: C2 trace emitter** (charter Phase 2.5) — the measured
per-block fixed cost is the target: one `DynamicMethod` per multi-block
trace, guest state in locals across the trace, inline RAM fast paths, the
§193 interrupt poll at every block boundary, side exits to the block JIT.
First contained step: lazy GPR state sync (load only the GPRs a block reads,
store only those it writes; the plan struct already exposes `RegIndex` /
`SecondRegIndex` / `BaseRegister` / `IndexRegister` per member, so the sets
are derivable from the plans — validate with the 200M four-engine
differential and the 1B fast anchors).

## 199c. Quiet-host medians (2026-10-19 late): D3b is counter-positive but wall-neutral; the real residue is the Step-episode cost

**Measured (1B, fast mode, gl+replace+spin, 3 interleaved runs, quiet host
after the operator stopped Fortnite):**
| Leg | Walls | Median | SHA |
|---|---|---|---|
| triangle | 10.88 / 11.09 / 11.30 s | **11.09 s** | 4F1FCBC0/7DAB63EB exact |
| menu | 42.80 / 47.35 / 47.58 s | **47.35 s** | DD023100/F5D61C94 exact |

`Host timing` confirms the wall is the CPU engine (triangle executeMs 10,869–
11,284, advanceMs 8; menu executeMs 42,775–47,554, advanceMs 23). The menu
spread (42.8–47.6 s) shows residual host noise — the earlier single 41.39 s
was a quieter window; no menu improvement is claimed.

**D3b verdict.** Deterministic counters improved (nonJitCalls 1,281,644 →
522,460, rejectedHeads 431 → 113, specRan 4.9M) but the **non-JIT time did not
move** (1.86–1.93 s before and after). Attribution: the 522K remaining
episodes still average ~10 `Step()` calls each (~350 ns/instruction) — the
spec runner declines them, most likely because its `Form` only uses
`decodes.TryGet` (no fill-on-demand) and the page churn/`MaxBlocks` churn
leaves entries missing at the moment of formation. **Follow-up item (cheap,
deterministic): give the spec `Form` the same fill-on-demand the JIT has
(`TryFillDecodeEntry`), or count why it declines (a spec-decline reason
counter) before changing anything.** This is a C2-adjacent item, not a
blocker for the trace emitter.

**Plan impact.** None on the milestone ladder: triangle 11.09 s vs the M1
target ≤6 s (C2), menu 47.35 s vs M1 ≤20 s (C2 + B2c). The CPU engine remains
the whole wall; C2 (`jit-trace-emitter-design.md`) is the next slice.

## 199d. C2 slices 1-3 + two negative results: where the fast-mode wall actually goes (2026-10-19/20)

**Landed (all SHAs exact, four-engine 200M bit-identical, suite 1481 green):**

1. **C2 slice 1 - lazy GPR state sync** (`JitStateMask`): per-kind read/write
   masks; the runner loads the union at the prologue and stores only the write
   mask at the epilogue and every fault/interpreter sync (over-stores write
   back unchanged values, so no cumulative bookkeeping is needed). 8-bit
   AH..BH fold to `index & 3` (caught by `PartialRegisters_Mov8`).
   Plus: emitted Eip/Eflags/Tsc accesses now use the auto-property backing
   fields instead of six callvirts per block (and one per memory member).
   Measured: triangle outer 9462 -> 8593 ms.
2. **C2 slice 3 - native single-precision memory x87** (`FpuLoadReal`,
   `FpuStoreReal`, `FpuStoreRealPop`): `Fld/Fst/Fstp m32` run through the same
   RAM probe as integer loads/stores with one FPU helper call mirroring
   `ReadFpuReal`/`WriteFpuReal`. Slow members: triangle 16.5M -> 12.5M,
   menu 88.9M -> 62.6M.
3. **Diagnostics**: per-mnemonic slow-member tally (`XGRID_SLOW=1`, emitted
   counter at the CallSpec site); non-cacheable fetch page tally
   (`XGRID_NONRAM=1`).

**Final quiet-host medians (1B, fast, gl+replace+spin, 3 runs, SHAs exact):**
triangle **10.63 s** (10.54/10.63/10.89), menu **45.62 s** (45.33/45.62/46.53).

**The measured cost structure (this is the important part):**

| Component | Triangle 1B | Notes |
|---|---:|---|
| Chain (memo + lookup + poll + call) | ~1.0 s | ~8 ns/invocation |
| State sync | ~1.5-2 s | ~15-20 ns/block |
| Ordinary member bodies | ~5-6 s | ~14 ns/member (memory probe ~25 ns, ALU ~8 ns) |
| Slow (interpreter-fallback) members | ~1 s | 12.5M members at ~80 ns |
| Non-JIT cold code | ~1.9 s | 523K episodes (spec runner) |
| GL replace path | ~0.5 s | |

**Negative result 1 - block fusion.** Fusing `JmpRel` successors and then
guarded fusion of branch/call successors (fall-through for forward branches,
target for loops, callee for calls, one EIP compare per boundary, exact
fast-mode boundary poll) cut invocations 125M -> 22.4M (-82%) but the wall
rose to 14.98 s (12.9 s outer): bigger runners cost ~50% more per member
(codegen quality / I-cache) and the emitted boundary poll costs more than the
outer dispatch it saves. At one fusion per block: 11.70 s vs 10.75 s. Reverted.
Fusion also silently disabled the spin skip (fused blocks contain stores, so
`HasSti && !HasStore` never held: spinSkips 7514 -> 0) - a fused block must
never span an STI/POPF, which the reverted code enforced.

**Negative result 2 - 1024-slot data translate cache.** 11.41 s vs 10.72 s at
256 slots (L1 pressure). Reverted.

**Conclusion for the next session.** The block JIT is at its structural floor:
~14 ns per ordinary member, of which the memory probe and the per-member
bookkeeping dominate, and neither can be removed inside IL emission. The
remaining path to xemu-class (charter Phase 3 / C3) is the raw emitter:
fixed host registers for the guest GPRs, flags in a host register, inline
TLB probes, and patch-on-link block chaining. The trace/fusion family is
measured out (see above) - do not retry it without a different boundary-poll
strategy (a cheap `Pending` flag on the interrupt source would be the
prerequisite: the interface `Acknowledge` call per boundary is what killed
fusion).

**Follow-ups filed:** (a) `IInterruptSource.Pending` as a cheap field so both
the outer poll and any future fused poll cost ~2 ns; (b) the non-JIT cold
residue (1.9 s) needs the spec runner to stop declining episodes (entries not
cached at formation time - `DecodedBlockCache.Form` uses `decodes.TryGet`
only); (c) 64/80-bit and register x87 forms still take the spec handler.

## 199e. C3 slice 1: the raw x86-64 block emitter foundation (2026-10-20)

Charter Phase 3 (authorized, §4) is now **started**: `RawBlockEmitter` builds
one native x86-64 function per block with **Iced 1.21.0** (MIT, the approved
Phase-3 dependency; `Directory.Packages.props` + `sources.md`).

**Architecture (as landed):**

- Guest GPRs pinned in fixed host registers across the whole block:
  eax/ecx/edx/ebx -> EAX/ECX/EDX/EBX (kept in the legacy registers so
  AH/CH/DH/BH stay addressable), esp/ebp/esi/edi -> R12D/R13D/R14D/R15D.
  An ordinary member costs its own instruction plus one EIP slot store and a
  three-instruction served increment (lea, so live host flags survive).
- Guest arithmetic flags live in the **host FLAGS register**; the
  non-arithmetic bits (IF/DF/IOPL/TF/NT/...) ride in a control frame slot
  updated by CLI/STI/CLD/STD. A flag materializer writes
  `(host & 0x8D5) | ctrl` into the frame only when a probe or helper needs the
  host flags, and at exits; `popfq` is always fed
  `eflags & (0x8D5 | DF)` because IOPL/TF/NT fault in user mode.
- Memory members inline the data translate-cache probe (tag compare, physical
  page, writable bit for stores, RAM bound) against **pinned** pointers, then
  load/store directly through the pinned RAM base. Any miss, watched range or
  non-RAM physical exits to the exact `ReadMemory`/`WriteMemory` helper with
  the state synced first, so fault precision is unchanged.
- Shapes the raw path does not encode (x87/SSE, string ops, calls, indirect
  jumps, awkward 8-bit forms) call `RawHelpers.ExecuteSpec`, which resolves the
  same spec handler the IL emitter uses (or the interpreter body) and leaves
  `Context.Eip` at the next instruction - so **every block is emittable**.
- `RawState` carries the pinned pointers (register file, translate tables, RAM)
  plus an unmanaged scratch block for Eip/Eflags/Tsc/progress. Objects that
  contain references cannot be pinned, hence the scratch block rather than
  addressing `XboxContext` directly.
- The raw runner is entered through `Marshal.GetDelegateForFunctionPointer`
  (a real managed->native transition), which is what lets the helpers be plain
  `[UnmanagedCallersOnly]`; calling them from a raw frame entered via `calli`
  from managed code is rejected by the runtime ("attempted to call a
  UnmanagedCallersOnly method from managed code") - measured, not guessed.
- Blocks are formed and invalidated by the existing block cache; the raw runner
  replaces the IL runner only when it builds. **No per-member budget check is
  emitted**: `RunJitBlock` calls a raw block only when the remaining budget
  covers every member, which is what keeps the host flags live between
  members. Exact mode (`--jit-exact`) keeps the IL runner.

**Bring-up bugs fixed (all caught by the differentials):** 32-bit `sub esp`
truncating RSP (the host stack is above 4 GB); `popf` vs `popfq` (16-bit pop,
stack imbalance); unmasked `popfq`; prologue argument order; loading the
`RawState` pointer fields instead of dereferencing them; the flag materializer
clobbering guest EAX before the register store; the probe's VPN computation
clobbering the staged store value (store values now stage in RSI); missing
served increments for terminators (Jmp/Jcc returned before the increment, and
the Jcc emitter's internal jumps skipped it - every block was one short).

**Validation:** 7 raw differentials green (`RawJitTests`: ALU loop, memory
forms, memory-immediate forms, `cmp [mem],imm`, stack push/pop, flags across
conditionals, x87 fallback trampoline), suite **1488 green**, format clean.

**Known gaps (documented, not hidden):**
1. A **backward `jne` over a `cmp [mem],imm` block** crashes the test host
   (the single-pass form passes). This is the next bring-up item; until it is
   fixed the raw path stays **opt-in** (`--jit-raw`, default off) and title
   runs must not use it.
2. `--jit-raw` has no performance measurement yet (it cannot run the triangle
   until gap 1 is closed).
3. High-byte 8-bit memory forms, 64/80-bit x87 and MMX moves still route
   through the helper trampoline (correct, just not native yet).
4. Each raw block allocates a 64 KB executable page and never frees it; block
   rebuilds are rare (revalidation), so this is bounded but should become an
   exact-size allocation plus a free list.

**Next steps (in order):** close gap 1 (loop re-entry), then measure the raw
path on the triangle/menu 1B goldens against the 10.63 s / 45.62 s baselines,
then extend the native shape set (64/80-bit x87, MMX moves, push/pop/call/ret),
then flip the fast-mode default once the medians show a win.

## 199f. C3 slice 2: the raw emitter runs (2026-10-20, second session)

Slice 1 committed a raw emitter that built blocks but crashed the moment a
memory member executed. This session found and fixed the four root causes, and
the raw path now serves real blocks end to end.

**The four blockers (all measured, none guessed):**

1. **Win64 shadow space.** The frame slots started at `[rsp+0]`, which is
   exactly the 32 bytes every call site must reserve for its callee. The helper
   trampolines write their register arguments there, so the first helper call
   overwrote the saved cpu/entries/state pointers and the next `mov r11,[rsp+10h]`
   dereferenced garbage. The frame now starts at `ShadowSpace = 0x20`.
2. **Delegate parameter marshalling.** `Marshal.GetDelegateForFunctionPointer`
   over a delegate with `XboxCpu`/`DecodeCacheEntry[]` parameters hands the
   native callee a marshaller proxy, not the object reference (diagnosed by
   comparing the received pointer against the running CPU: `match=False`).
   `RawRun` now takes `IntPtr` for both, and `RunJitBlock` converts with
   `Unsafe.As<XboxCpu, IntPtr>`.
3. **The probe used guest registers.** `EmitProbe` computed the VPN and the
   physical page in RAX/RCX/RDX - but those hold guest eax/ecx/edx. The fast
   path was fine; every *miss* then synced garbage into the register file before
   calling the helper. The probe now uses only R8/R10/RDI/RBP (non-guest
   scratch) and leaves `[rbp+rdi]` as the probed access.
4. **The helper updates the context, the block reads a scratch copy.**
   Eip/Eflags/Tsc live in an unmanaged scratch block (objects with references
   cannot be pinned), so after a spec handler ran, the reload replayed the
   values from *before* the call: `cmp [mem],imm` returned with the flags from
   the probe. `ExecuteSpec` now takes the state pointer and publishes
   Eip/Eflags/Tsc back before returning.

**Also fixed:** `cmp [mem],reg/imm` is a read, not a write (it was routed through
the write path, corrupting memory and comparing with an unstaged operand);
`and`/`or` in the CLI/STI/CLD/STD control-flag update clobbered the live guest
flags (now materialized first, and `_flagsLive` is cleared); ADC/SBB now load the
host CF when it is not live; addressing uses the 64-bit register forms (a 32-bit
base is not a GPR64); the memory slow path parks its address in RDI, which the
sync preserves; any emit failure falls back to the IL runner instead of
propagating.

**Validation:** **all 11 raw differentials pass** (ALU loops, memory
loads/stores/RMW, memory-immediate forms, `cmp [mem],imm`, both loop cases,
stack push/pop, flags across conditionals, and the x87 fallback trampoline).
Suite **1492 green** (0 skipped), build and format clean. The raw path stays
**opt-in** (`--jit-raw`).

**The TSC gap is closed.** A loop whose block contains a helper-served
memory member under-counted the TSC by one tick per block because the reload
after the helper re-based the frame TSC on the scratch copy; the frame slot is
the authoritative one (the helper never ticks it), so the rebase was removed and
all 11 raw differentials now pass, including the two loop cases.

**Next steps (in order):**
1. With `JitRawBlocks` forced on, a dozen `JitTests` still fail (MMIO fast path,
   string ops, indirect call/jmp, self-modifying code, `popf`/STI IRQ windows,
   the Jcc condition sweep, x87/SSE exotic). That suite is the broadest
   validation net the CPU has, so driving it green is the gate before anything
   else — no default flip, no measurement claim until it is clean.
2. Then measure `--jit-raw` on the triangle/menu 1B goldens against the
   10.63 s / 45.62 s baselines (median-of-3, SHAs + counters exact, and a
   `--jit-raw`-off control leg).
3. Then extend the native shape set (push/pop/call/ret, 64/80-bit x87, MMX
   moves, patch-on-link chaining) and re-measure.
4. Only then consider flipping the fast-mode default; the accurate mode and
   every standing gate are untouched throughout.

## 199g. C3 correctness campaign: JitTests closed (12 → 0); fourteen latent raw-emitter bugs fixed; the title differential matches to 25.05M steps (2026-10-19→20, third session)

**Gate 1 closed: `JitRawBlocks` forced on runs the whole JitTests suite green**
(56/56 JitTests + RawJitTests; full suite **1492 green**, build + format
clean). The harness change: `JitTests.CreateJit` now sets `JitRawBlocks = true`
— on Windows x64 the suite exercises the native runner, on other hosts
`RawCodeMemory.IsSupported` makes `TryBuild` return 0 and the same tests
exercise the IL runner, so both engines keep coverage across CI.

**Fourteen bugs found and fixed, in discovery order** (all found by
differential testing; every fix has a reproducer in the suite):

1. **Non-terminator members never advanced EIP.** `EmitMember` set F_EIP to
   the member's address but nothing advanced it, so a raw block whose last
   member was not a terminator re-executed that member forever
   (`MemoryShapes`, `JccConditions` hung at one EIP). Terminator members and
   helper members (which advance EIP themselves) are excluded.
2. **Helpers ran against a stale `Context.Eip`.** The block keeps EIP in its
   frame; the interpreter bodies read `Context.Eip` for return addresses and
   relative targets, so `call eax` pushed the wrong return address
   (`IndirectCallReg` divide-error crash) and `rep movs` used stale direction.
   `ExecuteSpec` now publishes Eip/Eflags/Tsc *forward* before the handler,
   then copies the result back (was write-back only).
3. **CLI/STI/CLD/STD clobbered guest EAX** (`EmitUpdateControl` used EAX for
   the control word) and, while the host flags were live, clobbered CF/ZF/etc.
   via its `and`/`or` (`CliStiMembers` lost CF). Now materializes first and
   uses R10D. `EmitLoadFlags` likewise moved EAX→R10D.
4. **Missing fast-mode post-member IRQ poll** for STI/POPF/string/port members
   (`IrqFast_*_NoStarvation`): the §193 wakeup contract — the raw path polled
   only at block entry. `EmitPostPoll` mirrors the IL emitter's post-poll
   (IF check, `Acknowledge` once, pending → record vector, advance EIP for
   native STI, TSC++/served++, exit).
5. **DF in the host FLAGS across helper calls** — a **Win64 ABI violation**.
   The raw block loaded the guest's DF into the host FLAGS (popfq) and left it
   set while calling runtime helpers, whose `rep movs` then ran *backwards* —
   GC heap corruption (crashes in `AssertSameState`,
   "Internal CLR error 0x80131506"). DF now lives only in the control slot;
   masking the load/exit paths with `ArithmeticMask` (DF never enters host
   FLAGS) is the whole fix.
6. **Flag liveness leaked from slow-path branches into merge points.** The
   emitter's compile-time `_flagsLive` was set by `EmitReloadAfterHelper` in
   the *slow-path* emission, so code after the merge label (fast path, flags
   dead) skipped its `EmitLoadFlags` reload: at triangle step ~6.47M a `jne`
   after [load / store / load] used the probe's stale flags with the *same
   architectural state* (`save-state` match at step 6,473,481, full separation
   at 6,473,482; the JNE fell through in raw and looped in IL). `EmitLoad`/
   `EmitStore` now reset `_flagsLive = false` at the merge point.
7. **The translate-cache probe indexed by raw VPN** instead of
   `vpn & DataXlatMask` → tag/phys/writable reads out of bounds for every
   address ≥ 1 MB (the original triangle crash at 1.6 s and every
   high-address miss; `MmioFastPath` documented it: the device page never
   hit). One-line fix; the IL probe always masked.
8. **Inline stores skipped `NoteStorePhysical` and the watch guard.**
   Self-modifying code kept stale decodes (`SelfModifyingCode`); watched
   stores bypassed `CheckWatch`. The store probe now ends with an inline guard:
   a `bt` against the pinned PT-page bitset and the decode-cache active-page
   bitset; a hit calls the new `RawNoteStorePhysical` helper and continues
   with the direct write (exactly the IL path: no re-translate, so A/D bits
   stay invisible to the guest), and the `WatchArmed` flag routes watched
   stores to the exact bus path.
9. **The poll's parked registers landed in the callee's shadow space.** The
   poll saves guest eax/ecx/edx/ebx with four pushes and then calls a managed
   helper; the callee's argument spills overwrote the parked values (all
   guest registers trashed after the first poll). Fixed by reserving the
   32-byte shadow area below the pushes (`sub rsp,0x20`).
10. **The store guard's `NoteStorePhysical` call clobbered guest
    EAX/ECX/EDX** (host registers) — the next `dec edx`/`jne` diverged
    (the 6,473,481 divergence's *first* cause). Park/restore like the poll.
11. **GC-stale object pointers.** The raw runner received the CPU and the
    block's `DecodeCacheEntry[]` as bare `IntPtr`s; a GC during a helper call
    moves the objects and every later helper call wrote into freed memory
    (diagnosed by `plans.Length == 0` on a freshly allocated array — heap
    corruption). The CPU now travels as a **weak `GCHandle`** resolved by each
    helper (`RawFromHandle`), and the block's entries array is allocated with
    `GC.AllocateArray<DecodeCacheEntry>(count, pinned: true)` so its address is
    stable for the block's lifetime.
12. **Memory-source `movzx/movsx`** was emitted as a register move
    (`SecondRegIndex` is 0 for memory sources) — routed to the exact handler.
13. **disp32 ≥ 0x80000000 failed to encode** (`AssemblerMemoryOperand + long`
    rejects values that do not fit an `int`), silently dropping every block
    containing such an addressing form to the IL runner. Sign-extend the
    `uint` displacement (`unchecked((int)`) at the emission sites.
14. **WP and A/D parity with the IL probe.** (a) The raw writability check
    took the slow path for every non-writable page even when CR0.WP was
    clear; now a `Cr0Wp` state flag gates it like the IL probe. (b) Stores to
    active code pages / tracked PT pages re-translate via the slow path in
    the first guard cut, making guest-visible A/D bits diverge; now the guard
    calls `NoteStorePhysical` and writes inline (7's design).

**Gate 2 (title differential) — reached 25,053,067 of 1B triangle steps.**
The IL-fast vs raw-fast 1B differential (`--jit --jit-threshold 16
--jit-spin` ± `--jit-raw`, `--gpu sw`, triangle ISO) matches **bit-exactly
through step 25,053,067** (RAM SHA bisect: same at 25,053,067, RAM differs in
[25,053,067, 25,053,130]). The first divergent block is raw head
`0x8001F9EB`:

```
0x8001F9EB  movzx edi,di        ; 0F B7 FF
0x8001F9EE  shl   edi,4         ; C1 E7 04
0x8001F9F1  add   edi,ecx       ; 03 F9
0x8001F9F3  mov   [0x83FF0002+edi*4],bx   ; 66 89 1C BD 02 00 FF 83
0x8001F9FB  jmp   +4
```

The store targets a page-table page (the guest writes PTE high words), so the
new PT guard + `NoteStorePhysical` path runs. The raw run takes
`LastFault vec=14 EIP=0x8001F9F3 err=0x00000002 CR2=0x0000000D`; the IL run
does not fault. The block's emitted code was read end-to-end (dump via
`XGRID_RAW_DUMP=1`): address computation, slot mask, WP check, guard, note
call (with the register park/restore) and the final `[rbp+rdi]` write all
match the IL semantics.

**The block is confirmed as the source.** A temporary env-gated hook
(`XGRID_RAW_SKIP_HEADS=8001F9EB`, kept in `RawBlockEmitter.TryBuild` as a
bisection diagnostic) forces that head to the IL runner, and the raw run then
produces the **IL SHA `E11E7536B754BC3C` exactly with no fault** at the same
step count. So the bug is inside this block's raw execution (or its handoff),
not in an earlier block. Next diagnostic step: instrument this store's linear
address and staged value in the emitter (or add a per-store trace helper), and
compare against the IL values at the same step — the fault's `CR2=0x0D`
cannot come from the store's own linear (≈`0x83FF33FE`) as currently
understood.

Diagnostics added (all trace-only, `XGRID_RAW_TRACE=1`): raw enter/exit with
GPRs and pinned-array pointers, the resolved spec member (index/mnemonic/
handler), the poll's control word + result, and the `TryBuild` failure reason
(which is how bugs 12–14 were found: "unknown shift op", "Displacement must
fit in an int").

**No performance claim.** Single runs on a Fortnite-loaded host showed the raw
path at 2–5× the IL fast mode's msps on the triangle (e.g., 48.9 vs 9.8
Msteps/s at 200M steps) — informational only; the quiet-host median campaign
waits for a differential-clean build.

**Next steps (in order):**
1. Identify and fix the 25,053,067-step divergence (raw block `0x8001F9EB`:
   the faulting store's linear address and pre-fault registers — instrument
   the emitter/helper, since `--save-state` cannot land mid-raw-block).
2. Then run the full 1B IL/raw differential on triangle **and** menu (the
   menu's animation path exercises strings/MMIO/DVD and may expose further
   gaps; the triangle's 1B also covers the GL-replace path).
3. Then the quiet-host median-of-3 against the 10.63 s / 45.62 s baselines
   (SHAs + counters exact, `--jit-raw`-off control legs) — with the operator's
   idle window.
4. Then C3 performance slices: patch-on-link block chaining (raw→raw direct
   jumps), native ROL/ROR/RCL/RCR (currently rejects the whole block - the
   key-schedule loops are IL-served), native x87 m64/m80, MMX, push/pop/call/
   ret emission, and the remaining probe/entry overhead.
5. Only then consider a fast-mode default flip; the accurate mode and every
   standing gate are untouched throughout.

## 199h. C3 correctness, second campaign: the store-guard linear clobber and the exit-edge flag merge; triangle 1B exact, menu open at 968.38M (2026-10-20, fourth session)

Two raw-emitter bugs closed, both found by the title differentials (the
"JitTests green" state from §199g still had them - the suite never arms a bus
watch and never delivers an interrupt into a block whose emission ends with
live flags).

**Bug 1: the store guard's note path clobbered R9 (the linear address).**
`EmitStoreGuard` ran the PT/active-page bitset check first; a hit called the
`NoteStorePhysical` helper (which clobbers R9 for its arguments) and then
fell into the tail "any watch armed → slow" check. The CLI arms a bus watch
by default (`WatchStart/WatchEnd = 0x35B00/0x35D00`), so this fired on every
PT-page store: the slow path received the helper call's leftovers as the
linear address. On the triangle that was a #PF at EIP=0x8001F9F3 with
CR2=0x0D at step 25,053,213 (the previous session's open item). The fix:
- `RawState` gains `WatchStart`/`WatchEnd` (filled per call from
  `AddressSpace`), and the guard runs the exact `IsWatched` overlap test
  (`phys < WatchEnd && WatchStart < phys + size`) *before* the note path,
  matching the IL emitter's order (probe → watch guard → note → write).
- Watched stores now diverge to the exact bus path with the linear intact;
  stores outside the range stay inline. That also removed a large hidden
  cost: with the old conservative check, *every* store went slow whenever a
  watch was armed (all CLI runs).
- Fail-pre-fix evidence: `StoreToPageTablePage_WithWatchArmed_MatchesInterpreter`
  (the store's distinct per-iteration values land at 0x44332212 instead of
  0x44332215) and the triangle 1B differential (25.05M #PF → bit-exact 1B).

**Bug 2: the exit-edge flag merge.** `EmitEpilogue` materialized the frame
EFLAGS using the emitter's *compile-time* `_flagsLive` state, but `_exit` is
also the target of the poll's pending path, the `Jcc` arms and the
terminator jumps - edges where the host FLAGS are dead or clobbered (the
poll's helper call leaves `test r8d,r8d` results). The kernel idle delivery
pushed EFLAGS 0x202 (control bits only: IF|bit1) instead of 0x246
(CF|PF|IF) and the triangle diverged at step 30,916,862. The fix:
- Every `jmp _exit` edge normalizes the frame flags on its own path
  (`EmitMaterializeFlags()` before the jump in `JmpRel`, the helper
  terminator and both `Jcc` arms), and the epilogue only stores the frame.
- The `Jcc` taken arm needs the compile-time state reset (`_flagsLive =
  true`) because the fall-through arm's materialize otherwise leaves the
  emitter believing the frame is authoritative while it is stale on the
  taken path (that variant regressed `RandomPrograms_MatchInterpreter(seed:
  6)` during development).
- Fail-pre-fix evidence: `IrqFast_DeliveredFlags_MatchBetweenRunners`
  delivers 0x202 on the raw runner vs 0x246 on IL; after the fix both push
  the same EFLAGS and the triangle matches to 1B.

**Regression tests (3 new, suite 1495 green):**
- `IrqFast_DeliveredFlags_MatchBetweenRunners` - the kernel idle shape
  (`xor edx,edx; sti; add [mem],eax; cli; dec; jne`) warmed with a
  never-firing source, then a fresh source whose first acknowledge lands on
  the STI's post-poll (IF forced clear first). Asserts both engines record
  the same delivered EFLAGS (the ISR reads `[esp+8]`) and the raw served.
- `StoreToPageTablePage_WithWatchArmed_MatchesInterpreter` - paging on
  (PD 0x4000 / PT 0x5000), a covering watch, per-iteration store values; the
  final word at the page-table page and the watch-hit sequences must match
  the interpreter.
- `StoreToWatchedRange_RaisesHitOnBothRunners` - the exact range semantics:
  a store inside `[WatchStart, WatchEnd)` raises `WatchHit` on both runners
  with the physical address/size/value, a store outside writes inline.

**Performance work (same session; loaded-host numbers informational only).**
The raw path was 1.9 s per 100M steps *behind* IL; it is now at parity:
- `RawEnter`/`RawExit` built their interpolated trace strings on every
  invocation (13M allocations per 100M steps). Guarded; triangle outerMs
  4741 → 3081 (IL 2737).
- `BuildRawState` caches the invariant half of `RawState` (pinned pointers,
  xlat mask, scratch addresses) and refreshes only RAM size, CR0.WP, the
  watch range and the six segment bases per call; outerMs 3081 → 2905.
- `Jit stats` now prints `rawBuilt/rawInv/rawServed`: the raw runner serves
  76% of members on the triangle (40.8M of 53.5M at 100M steps) with 5.9M
  invocations.
- Measured on the quiet host before the session's later edits: triangle IL
  1B 10.20 s (baseline 10.63), menu IL 1B 39.6-40.4 s (baseline 45.62);
  after the fixes the raw legs were at parity on the loaded host (tri 20.55
  vs 19.08, menu 55.23 vs 54.77). No raw-advantage claim yet: the remaining
  per-invocation cost (~35 ns × 5.9M/100M) and the frame-slot bookkeeping
  are the structural levers (chaining / register-resident state), and both
  must preserve the per-block IRQ poll contract to keep the IL/raw
  differential meaningful.

**Open item: the menu diverges at step 968,380,640 (after 968,380,639 the
RAM differs by two bytes).** The guest is in the dashboard's MMX copy loop
(0x39F90-0x39FDF: `prefetch ×2; movq mm0-7,[esi+8N]; nop ×2;
movntq [edi+8N],mm0-7; add esi,0x40; add edi,0x40; dec ecx; jne`). Evidence
gathered:
- The differing bytes are at physical 0x7534B8 and 0x7534C8: `E3` vs `E4`
  in the low byte of two 8-byte values (IL wrote 0x39B850E3, raw 0x39B850E4
  - a one-byte difference in what the copy stored).
- RAM SHA bisect: match at 968,380,639, differ at 968,380,640; the window
  [968,380,583, 968,380,659) is entirely inside the copy loop.
- The IL and raw traces of the loop (interpretive replay) show identical
  register states at the same steps.
- `XGRID_RAW_SKIP_HEADS` with 39F8E/39F90 (and four more loop heads) did
  *not* clear it - the serving block has not been isolated yet.
- A temporary bounded trace in the shared `SpecMovqRegMem`/`SpecMovntqMemReg`
  handlers logged 400 identical events at the loop's first occurrences
  (TSC 746M) but never fired inside the TSC window [968,380,560,
  968,380,700) - so the divergent store either is not one of those handler
  calls or the handler's TSC/EIP view differs there (the instrumentation was
  removed; the save-state layout fact `offset 170 = Tsc = step count` is
  recorded for the next attempt).
- Next steps for the next session: instrument the *emitted* store path for
  the loop block (the raw block at head 0x39F90 exists, len=5130), compare
  the block's exit state against the IL runner's, and check whether the
  block's `Entries`/member mapping is the one the handlers see.

**Status: triangle 1B fast-IL vs fast-raw bit-exact (RAM SHA, CPU SHA and
every NV2A counter identical); menu exact through 968,380,639 and open
beyond; suite 1495 green; format clean; the raw path stays opt-in.**

## 199i. C3 correctness, third campaign: the split-member EIP advance (menu 1B now exact) (2026-10-20, fifth session)

The §199h open item is CLOSED: the menu 1B IL-vs-raw differential is now
bit-exact, and the root cause was not a store helper at all - it was a
block-exit EIP bug in the split fast/slow members.

**Root cause.** Four member forms emit a native fast path and a
spec-handler slow path in the same `EmitMember`: the RMW destination
(`AluMR`/`AluMI` non-cmp), the cmp-memory forms (`AluRM`/`AluMI` cmp via
`EmitCmpMemorySource`), `TestMI`, and `IncM`/`DecM`. The slow path calls
`EmitHelperMember`, whose exact handler advances EIP itself and sets the
emitter's compile-time `_memberHelper` flag. `EmitMember` uses that flag to
skip its own per-member EIP advance - correct for the slow path, but the flag
is compile-time state, so the fast path also lost its advance. Mid-block this
is invisible because the next member's `EmitSetEip` rewrites F_EIP; only when
such a member is the block's LAST member does the missing advance survive to
the exit. The raw runner then returned with EIP on the member's first byte
(while counting it as served), the member path re-executed the instruction,
and the guest step/TSC accounting drifted by one.

**Evidence chain (block-level trace, new diagnostics).** `XGRID_BLOCK_TRACE`
(window + head dump) showed identical block streams until one invocation:
head=0x1E010, entry=0x1E010, members=32, served=32, tsc=968,380,477 - but
exit=0x1E08B in IL vs 0x1E088 in raw. The block's 32nd member (M31, dumped by
`XGRID_BLOCK_TRACE_HEAD=1E010`) is `or [eax+0x2C],edx` at 0x1E088, and
`MaxMembers=32`; 0x1E088 is exactly where the raw run stopped. The raw trace
then resumed at 0x1E088 (the re-execution), leaving the engine exactly one
guest instruction behind: at step 968,380,640 IL was at EIP 0x1E164 (after
`push eax` at 0x1E163) and raw at 0x1E163, with ESP 4 bytes apart. Every later
boundary kept the same one-instruction lag (step 968,380,700: IL 0x1D13B vs
raw 0x1D136; step 968,400,000: IL 0x39FB9 vs raw 0x39FB8 - two consecutive
1-byte `nop`s). The §199h "missing 0xD003B4B8 store" is a consequence: the raw
run simply had not reached the `push eax` that writes it.

**Fix.** The fast path of the four forms now emits `EmitAdvanceEip(plan.Length)`
before its merge jump; the slow path's spec handler keeps owning its own
advance, and the outer `EmitMember` skip stays as-is (the flag is only an
optimization boundary now). Regression test
`BlockEndingInMemoryRmw_AdvancesEipOnce` builds a 32-member block whose last
member is `inc dword [0x2000]` (paging on so the inline probe is taken);
fail-pre-fix it reports TSC 71 vs 70 and the inc applied twice, with the fix
it is a full differential.

**Validation.** Suite 1497 green (1495 + the two new differentials), format
clean.
Menu 1B IL vs raw: identical RAM SHA `660CA11B...`, CPU SHA `F5D61C94...`
(= the recorded IL anchor) and every JIT counter identical. Triangle 1B IL vs
raw: identical RAM SHA `A2DCA55F...` and CPU SHA `7DAB63EB...`. ktest smoke
PASS 7/7 known deltas at the bit-reproducible 792,800,535 steps (xemu's leg
could not bind its monitor socket on this host - `Failed to bind socket:
Input/output error` on any port - so the XGrid rows were diffed against the
known-delta table via `-SkipXemu`).

**Diagnostics added for the next differential.** `XGRID_WATCH_START` /
`XGRID_WATCH_END` (hex) retarget the CLI bus watch; `XGRID_WATCH_TRACE=1`
prints every hit as `WHIT tsc=... eip=... addr=... size=... value=...`.
`XGRID_BLOCK_TRACE=1` with `XGRID_BLOCK_TRACE_FROM/TO` (TSC window) logs
`head/entry/members/served/exit/tsc/raw` per block invocation and
`XGRID_BLOCK_TRACE_HEAD=hex` dumps that block member list (EIP/length/mnemonic).
Together they localize any IL-vs-raw divergence to a single block invocation.

## 199j. C3 negative result: mask-driven register sync is below resolution, and the helper ABI rule it exposed (2026-10-20)

With the raw path finally bit-exact, the first performance experiment after
the correctness campaign was the IL runner's C2 slice-1 register-mask
contract applied to the raw emitter: the prologue loads only the union of
the block's read and write masks, the epilogue/sync sites store only the
write union, and helper reloads cover the member's write mask instead of all
eight registers. It was implemented end-to-end and validated bit-exactly
(triangle and menu SHAs + counters at 100M, both title 1B differentials
exact).

**Negative result.** Interleaved 100M runs (3 per leg, same binary, raw-only):
menu pre 5.236/5.500/5.098 s (median 5.236) vs mask 5.034/5.348/5.195
(median 5.195) - 0.8%; triangle pre 5.643/5.466/5.180 (5.466) vs mask
5.621/5.404/5.192 (5.404) - 1.1%. Both below the project's sub-2% resolution
standard (n>=7 medians), i.e. host noise. Reverted; the raw emitter keeps
the all-eight load/store contract, which is also the obviously safe one.

**The ABI rule it exposed (keep for any future sync/chaining slice).** Every
raw helper is a managed call, and the Win64 ABI lets it clobber RAX/RCX/RDX -
which are exactly the host registers holding guest eax/ecx/edx (guest
EBX/R12D-R15D ride in callee-saved host registers). The mask slice reloaded
only the member's write mask after helpers, so a helper whose mask excludes
eax/ecx/edx (MMX moves, x87/SSE, memory read/write helpers) left the caller's
values replaced by ABI garbage. It crashed the early boot at EIP=0x402D42
with CR2=0x5315828B: the caller's `rep movsd`/call block had EAX/EDX equal to
the call target instead of the caller values, and the callee's saved ESI/EDI
were the callee's internals. Fix (whenever reload masks are introduced):
always include bits 0-2 (`{eax,ecx,edx}`) in the post-helper reload, in
addition to the handler's write mask. Regression test
`HelperClobberedCallerSavedRegisters_AreReloaded` (`mov ecx,5; mov edx,7;
movq mm0,[mem]` helper; `add edx,ecx; mov [mem],edx`) fails pre-fix with
garbage in the stored sum and passes with the full reload.

Lesson for the structural levers: per-invocation sync is only worth touching
once chaining removes the block boundary itself; slicing it alone does not
move the wall, and every slice must carry the ABI-clobber rule.

## 199k. C3 performance slice: hoist the raw machine state out of the block loop (2026-10-20)

With correctness closed, the first measured raw-emitter win. `BuildRawState`
ran once per *block invocation* (161,161,668 times on the 1B menu; 124M on
the triangle) although `RunJitBlock` serves ~166 blocks per call (970,543
calls on the menu). Its expensive half - six `GetSegmentBase` lookups plus the
watch range - is invariant across a call: segment loads are excluded from
planned members (the same invariant the loop already uses for CS/fetch), the
watch range is host-side configuration, RAM size and the pinned pointers do
not change mid-call. The state is now built once per `RunJitBlock` call and
only `Cr0Wp` (a MovCr0 helper can change it) and the decode-cache guard
tables (paging may be enabled) refresh per block invocation
(`RefreshRawState`).

**State storage matters.** The first version hoisted the state as a
`RunJitBlock` stack local; the CPU suite then crashed the test host with an
access violation in `JitTests.PagedCrossVisitFault_DeliveredFromJit` (every
JitTests CPU runs with `JitRawBlocks` on), while the same test passed alone -
the stack-local lifetime across the helper calls and exception unwinds was
unsafe in practice. The state now lives in an unmanaged block allocated once
per CPU (`_rawStateBlock`, `NativeMemory.Alloc`), which is also cheaper (no
96-byte struct copy per call) and gives the runner a stable pointer. Suite
1497 green, format clean.

**Measurements.** 100M interleaved raw-only A/B (3 reps per leg, same
binary/flags, SHAs identical): menu baseline 5.458 s median vs hoisted
4.966 s (**9.0% faster**); triangle 5.406 s vs 5.277 s (**2.4% faster**).
One-shot 1B A/B (bit-exact on both title differentials, RAM/CPU SHAs and
every counter identical): menu raw `outerMs` 94,827 vs IL 98,492 (-3.7%),
triangle 22,789 vs 23,333 (-2.3%); end-to-end walls were within host noise on
the loaded host (menu 135.5 vs 134.3 s, triangle 45.0 vs 44.8 s), so no
end-to-end claim - the CPU-serving time is the honest metric for this slice.

**Second negative result (bookkeeping).** Removing the per-member served
increment (static served counts on each exit edge) and the per-member entry
EIP store (one end store for pure-native members) was implemented and
validated bit-exactly, but a 100M A/B showed 0.9% (menu) / 1.6% (triangle) -
below the sub-2% resolution standard - and was reverted. The reason is now
clear: raw blocks average ~5 members on the menu, so per-invocation work
dominates per-member work; chaining (removing the block boundary) is the next
lever, not per-member shaving.

## 199l. C3 structural lever: direct raw block chaining — LANDED (2026-10-20, sixth session)

**Status: LANDED (commit `21b0f8a`).** The chartered structural lever is in,
opt-in with `--jit-raw` like the rest of C3, together with the fault-contract
fix that had forced its revert (§199m is the diagnosis). The previously
reverted implementation is still preserved as
`wt-data/raw-chain/chain-199l.patch` (gitignored, historical); the landed tree
is authoritative.

**Design (as landed).** A raw block's epilogue looks up its next guest EIP in a
per-CPU direct-mapped table (16384 slots x 32 bytes, unmanaged) and tail-jumps
into the target block's native entry, passing the target's arguments in the
Win64 registers; the target returns to the original `RunJitBlock` caller. The
entry carries the target EIP, generation (low 32), member count, native entry
and pinned decode-entry pointer.
- Budget: `remaining = F_BUDGET - F_SERVED` (the budget is already net of prior
  chained blocks), signed compare (an unsigned compare let a negative
  remainder look huge and ran a 100M probe to 739M - fixed before measuring).
- Served accounting: a per-invocation carry; the final return is
  `F_SERVED + carry`, reset for the next invocation; the fault path publishes
  `Progress = F_SERVED + carry`; `RawExit` resets the carry on unwind.
- IRQ boundary: with IF set the source chains only when
  `IInterruptSource.PendingFlagAddress` (new default interface member) points at
  a clear 0/1 word; the 8259 PIC keeps that word current on SetIrq,
  Acknowledge, PIC port writes, reset and load. **STI blocks are never chain
  sources or targets** - the `--jit-spin` idle-wait skip lives in
  `RunJitBlock`'s per-block logic, and chaining through the kernel idle loop
  bypassed it (the 100M menu SHA changed and 37M skip steps disappeared until
  the STI gate was added).
- Invalidation: the chain edge re-reads the **live pinned generation word**
  (`DecodedBlockCache.GenerationPointer`); the table is cleared whenever the
  JIT block cache is cleared without a generation bump (a `Cleared` hook +
  `Reset`/`InvalidateJitBlocks`), and revalidated blocks are republished with
  their new generation (they keep their runner but lose their table entry when
  the table is cleared).
- The chain table is allocated on first publication (not first invocation):
  blocks formed before the first raw invocation otherwise could not publish,
  which silently disabled chains in small tests.
- Diagnostics: `rawChained` in Jit stats plus `JitRawFaults`,
  `RawChainPublishes`, `RawChainClears`; `XGRID_RAW_CHAIN=0` disables the chain
  tails and publications for A/B.

**Measurements (quiet host, `--gpu sw`, IL control = same flags without
`--jit-raw`; SHAs identical in both modes on every leg).**
- 1B medians of 3 interleaved pairs: triangle wall 26.97 -> 23.72 s
  (**-12.0%**, raw 3/3) with `outerMs` 10,565 -> 7,546 (**-28.6%**); menu wall
  109.39 -> 95.06 s (**-13.1%**, 3/3) with `outerMs` 88,601 -> 75,985
  (**-14.2%**). Counters are identical between the modes: menu
  `interrupts=3153`, `draws=33638 pixels=285477537 frames=686`, the same IDE
  command log and `specRan=9547099 verifyFailures=0`; triangle
  `interrupts=1689`, `draws=2673 frames=35`.
- 1B menu reproduces the §199i anchor exactly: RAM `660CA11B…`, CPU
  `F5D61C94…`, `rawInv=3.27M` vs IL `162.5M`, `rawChained=157.9M` (§199l's
  numbers to the digit). 1B triangle: RAM `F57934BF…`, CPU `CF79FC17…` on both
  modes; the older `A2DCA55F…` anchor does not reproduce on this host even on
  pre-patch `025d20d`, so the environment moved, not the patch (recorded so the
  next session does not chase it).
- 100M A/B, 5 interleaved pairs: menu wall 0.9315 (raw 5/5) / `outerMs`
  0.9019; triangle wall 0.9927 (noise: the 100M window is start-up bound) /
  `outerMs` 0.9670.

**Gates.** Pair reproducer `AluLoop`+`PagedCrossVisitFault` clean 10/10; CPU
suite 625/625 green x10; full solution suite green; format clean; ktest XGrid
leg stops at the 4E03A park after `792,800,535` steps and its 739-line suite
log is byte-identical to the accepted baseline. `scripts/chain-199l-gate.ps1`
(1B IL-vs-raw legs) and `scripts/chain-199l-ab.ps1` (interleaved pair medians)
reproduce the runs.

## 199m. C3 raw fault contract: a managed exception may never unwind through the raw frame (2026-10-20, sixth session)

**The §199l crash was a CLR contract bug, not a chaining bug.** A raw helper
throwing (e.g. `PageFaultSignal` from the interpreter's `ReadMemory`) had to
unwind out of the hand-emitted native frame. That frame has no unwind data, so
CoreCLR cannot walk it: the exception was delivered only when the surrounding
stack layout happened to survive the walk. The pre-chaining layout got lucky,
the chaining diff (bigger `RawState`, extra frame stores) did not - so the test
host aborted with `0x80131506` / `0xE0434352` in exactly the pair
`JitTests.AluLoop_MatchesInterpreter_AndServesBlocks` +
`JitTests.PagedCrossVisitFault_DeliveredFromJit`.

Evidence chain (each step reproducible):
- the pair crashed 2/2 while the Paged test passed alone; `XGRID_RAW_CHAIN=0`
  still crashed (so the tail jump was innocent); `XGRID_RAW_SKIP_HEADS=100F`
  (AluLoop's raw block forced to IL) cleared it - i.e. *any* raw block ahead of
  the faulting one changed the layout enough;
- the four bisect candidates from the §199l handoff were all eliminated: the
  PIC pending word (no PIC in the CPU tests; unit-tested separately), the
  pinned generation slot (removing it kept the crash and moved it *earlier*),
  the `RawState` layout (a startup trace confirmed `Marshal.SizeOf`/`OffsetOf`
  agree with the runtime field addresses, `sizeof(RawState)=200`, `Fault` at
  160, and `state->Fault` equal to the scratch slot the helpers write), and the
  chain table allocation/clear (the crash also reproduced with the table never
  published);
- a standalone harness (`[UnmanagedCallersOnly]` thrower called from a
  RWX-emitted frame) reproduced it independently of XGrid: the throw aborted
  the process as soon as a managed `try/finally` sat above the raw call, and
  only "worked" in the exact shape where nothing forced a physical unwind -
  proving the success was luck, not support;
- a crash dump of the pair (`DOTNET_DbgEnableMiniDump`) showed the two expected
  `PageFaultSignal` objects plus the pre-allocated `ExecutionEngineException`
  singleton and a managed stack whose test-method frame was already gone.

**Fix (landed).** The helpers never throw across the native frame:
- every `RawHelpers` entry catches, records the exception on the CPU
  (`RawCaptureFault`, which also sets `RawState.Fault`) and returns;
- the emitted block tests that word after **every** helper call (spec handler,
  memory read/write, device read/write, store note, IRQ poll) and branches to
  `_faultRet`, which clears the chain carry, returns the `-1` sentinel and
  leaves via the shared epilogue;
- `RunJitBlock` sees the sentinel after `RawExit` published the context and
  rethrows the original exception with `ExceptionDispatchInfo`, so the
  `catch (PageFaultSignal)` path, `_jitProgress` accounting (`total + done + 1`)
  and every diagnostic are exactly the IL runner's. `JitRawFaults` counts these;
- `RawFromHandleOrNull` makes the capture path throw-free (`GCHandle`
  `IsAllocated` check instead of a cast, so a dead handle cannot rethrow).

**Invariant for future raw work:** nothing that can throw may run between the
raw frame's prologue and its epilogue except through the capture path. Adding a
new helper means: wrap the body in `try/catch -> Capture`, and emit
`EmitFaultCheck()` right after the call (after any pushed-register restore, so
the frame is balanced when the fault exit runs). Regression test
`RawJitTests.FaultingMember_DeliveredFromRawHelper_MatchesInterpreter` faults
from a chained invocation and fails pre-fix (`JitRawFaults=0`).

## 199n. Fast-mode position after C3 chaining: the wall is GPU- and helper-bound, not block-bound (2026-10-20, sixth session)

**Why this section exists.** §199l's chaining win was measured as a wall delta;
this section records *where the remaining wall actually is*, so the next slices
are chosen from evidence rather than from the previous plan's order.

**Method.** Same host/window, 1B steps, `--jit --jit-threshold 16 --jit-spin
--jit-raw --jit-timing --gpu-time`, `--gpu sw`; the hardware legs add
`--gpu gl --gpu-replace`. Raster seconds come from the rasterizer's own timer
(`NV2A raster seconds legacy/psh`), so they are a subset of the runner/member
time, not additive. Logs: `wt-data/chain-199l/{tri,menu}-breakdown.log` and
`{tri,menu}-glraw.log`; median harness `scripts/chain-199l-ab.ps1`.

| 1B, raw+chain | menu | triangle |
|---|---:|---:|
| wall `--gpu sw` | 112.5 s | 22.2 s |
| wall `--gpu gl --gpu-replace` | 58.9 s | 13.5 s |
| software-rasterizer cost (sw − gl) | 53.6 s (**48%**) | 8.7 s (**39%**) |
| raster timer (legacy + psh) | 0.89 + 54.08 s | 0.63 + 12.03 s |
| raw members served | 828,779,766 | 452,469,056 |
| raw runner time (`runnerMs`) | 83.8 s sw / 50.5 s gl | 6.5 s sw / 8.35 s gl |
| `ns` per served member (gl leg) | **61 ns** | **18.5 ns** |
| chain jumps / block invocations | 157.9M / 4.63M | 109.0M / 1.62M |
| idle-skipped steps | 128.6M | 530.2M |
| flips per 1B | 686 | 35 |

Caveat: this window was noisier than the median run (sw walls 112.5/22.2 s vs
the median 95.1/23.7 s), so the sw−gl deltas, the member counts and ns/member
are the robust readings; the absolute walls are host-drifted. The gl leg's
`runnerMs` is *higher* than the sw leg's on the triangle (8.35 vs 6.5 s)
because the GL submission happens inside the block that triggers the draw.

**Findings.**

1. **The software rasterizer is the largest single item on both goldens**
   (48% menu / 39% triangle of the sw wall). Whatever else improves, the sw
   wall cannot fall below the raster cost; xemu renders on the hardware GPU,
   so the like-for-like comparison is our `--gpu gl --gpu-replace` leg.
2. **Block mechanics are no longer material.** 1.6–4.6M invocations and
   109–158M chain jumps carry 450–830M members: the entry/epilogue and the
   chain edge are a few percent of the CPU path. The b4 bucket (long blocks)
   is 5.6 s of the triangle's 7.5 s outer time - i.e. the time is *inside*
   members, not at boundaries. Further block/trace work (the old Phase 2.5
   trace emitter) would not move the wall; that phase is absorbed by chaining.
3. **The dashboard's CPU premium is the helper-routed shape mix (plus device
   work).** With GL, the menu pays 61 ns/member vs the triangle's 18.5 ns. Both
   legs execute the same raw emitter; the difference is the dashboard's MMX
   copy loops (`movq`/`movntq`), string ops and x87, which still take an
   `ExecuteSpec` managed call per member, on top of its NV2A method-write
   traffic. Note the raw leg's `slowMembers` counter (653k) is *not* evidence
   that the mix is cheap: it is incremented by the IL emitter only - a
   helper-routed raw member is counted in `rawServed`. Ranking the raw helper
   shapes needs `XGRID_SLOW`/`--shape-tally` on the raw leg (or a
   raw-specific tally). That is the biggest remaining CPU-side item and it is
   bounded, diagnosable work with an existing histogram.
4. **Per-member bookkeeping is still per member**: an EIP store, a TSC tick and
   the store-guard (WP/PT/active-page/watch) tests on every member. The
   boundary contract only needs the EIP at exits and polls, so this can be
   compressed to a per-block exit plus a per-member counter.
5. **Against xemu**: xemu's measured triangle rate is ~830 Msteps/s (charter
   §1, §199). Our `gl+replace` triangle leg is 74 Msteps/s → **~11×**
   end-to-end. Per member we pay 18.5 ns (triangle) against a realistic
   TCG-class 5–10 ns per guest instruction → **~2–4×**; the dashboard's 61 ns is
   **~6–12×**, but that number includes its NV2A/device traffic, so the
   pure-emulation part is smaller. Net: the engine is a small multiple off, and
   the end-to-end gap is dominated by the GPU backend (xemu renders on
   hardware) and the helper mix - not by codegen structure.
6. **Flip density differs from xemu's** (35 flips per 1B steps here vs the ~84
   implied by 70 fps at 830 Msteps/s). Either the guest's vblank/flip wait is
   being extended by something we delay, or the two workloads pace
   differently. Worth one probe before any present-path work: it is both a
   fidelity question and a frame-rate loss.

**Consequences recorded in the governing docs** (charter status block, plan,
direction, AGENTS): the next fast-mode order is **C3-x hot-shape native
encoding** (target: menu 61 → ~30 ns/member) → **C3-y bookkeeping trim**
(EIP per exit, hoisted guard) → **C3-z inline TLB/RAM page pair**, then
**Track B leads** (rasterizer structure, `gl+replace` per-frame path, present
pacing). Reporting moves to **ns per served member + `gl+replace` 1B wall +
wall per rendered frame**; 1B-step walls are kept only as continuity anchors
because 530M of the triangle's steps are idle-spin. **Redirect criterion:** if
the dashboard is still >15× from real time with GL after C3-x/y/z, further
codegen stops and the remainder is Track B/D.



## §199o C3-x hot-shape native encoding (2026-10-20, seventh session) - LANDED

**Scope.** The §199n re-weight put the raw path's helper-routed shape mix first:
one managed `ExecuteSpec` call per unencoded member (full GPR/EIP/EFLAGS/TSC
sync, dispatch, reload) on top of the pinned-register block. This session added
native encodings for the shapes the `--raw-shapes` tally showed on the two
goldens, leaving the block/chain structure untouched.

**New instrumentation.** `--raw-shapes` (with `--jit-raw`) tallies every
helper-routed raw member by mnemonic and by form
(`RawHelperShapeCounts` / `RawHelperShapeDetail`, printed at exit). This closes
the §199n note that the raw mix was invisible: the counters are execution
counts from the helper trampoline, not build-time counts. `rawBuildFails` was
also added to the Jit stats line (raw builds that fell back to the IL runner).

**Shapes now native (raw path; IL keeps the exact spec handler).**

- Stack/control: `push r32/imm32`, `push [m32]`, `pop r32`, `call rel32`,
  `ret`/`ret imm16`, `leave`, `jmp [m32]`, `call [m32]`.
- Integer: memory-source `movzx/movsx`, `neg`, `not`, `bswap`, `xchg r32,r32`,
  `cdq`, `setcc r8`, `shl/shr/sar/rol/ror reg, cl` (host instruction + OF fixup
  where ShiftOps clears it), `rol/ror reg, imm`.
- x87: `Fld/Fst/Fstp m32` and `st(i)`, `Fild/Fist/Fistp m32`, `Fadd/Fmul/
  Fsub/Fsubr/Fdiv/Fdivr` (reg and m32/m64), the `Faddp` family, `Fchs`,
  `Fcom/Fcomp/Fucom/Fucomp` (reg and m32/m64), `Fld1`, `Fxch`, `Fiadd` family
  (m16/m32). The FPU state stays in the managed `FpuState`; the emitted code
  calls tiny `[UnmanagedCallersOnly]` entries that take/return the value bits,
  so every IL/raw differential compares the same semantics.
- MMX: `movq mm, m64`, `movq/movntq m64, mm`.
- SSE scalar: `Addss/Subss/Mulss/Divss/Minss/Maxss` (reg and m32),
  `Sqrtss/Rsqrtss/Rcpss`, `Movss` (reg/load/store), `Cvttss2Si` (reg/m32).
- `prefetch` is a member-level Nop (the interpreter's SSE path is already a
  no-op; it must not fault).

Each new shape follows the existing contract: fast path advances EIP and the
slow path (probe miss / device / watch / high-byte / unsupported form) runs the
exact spec handler; helpers catch, record and report faults (the §199m
contract). Tiny helpers that return a value capture it in **R8** because
`EmitFaultCheck` clobbers R9/R10/R11 - the first version used R9 and stored a
fault-pointer fragment into the FPU store (nondeterministic value, found by the
IL/raw differential before any title run).

**Two correctness bugs found and fixed en route.**

1. **Register-form FPU members advanced EIP twice** (`EmitFpuRegister` emitted
   its own advance although no slow-path helper marks `_memberHelper`), which
   drove the block's EIP into the middle of the next instruction. The title
   run then jumped through garbage and faulted at CR2=0xFFFFFFC2/EIP
   0x80058191 - a mid-instruction EIP that the block trace (`served=6`,
   `exit=0x8005818D`) and the emitted-code dump localized.
2. **The IL runner advanced EIP for handler-backed new kinds.** `JitEmitter`'s
   generic `!plan.IsTerminator && kind != CallSpec` advance assumed only
   `CallSpec` owns EIP; the new FPU/MMX/SSE/`PushM`/`Cdq`/`Setcc` kinds run a
   spec handler that already advanced it, so the IL runner (used for the tail
   when the remaining budget is smaller than a block) walked off the
   instruction stream by one member. Fixed by centralizing
   `JitPlanner.HandlerOwnsEip(kind)` and using it for both the generic advance
   and the post-poll `advanceEip`.

**Coverage at 1B (1B-step runs, `--jit --jit-threshold 16 --jit-spin
--jit-raw --raw-shapes --gpu sw`; SHAs exact, every counter identical).**

| title | helper members before | after | rawServed |
|---|---:|---:|---:|
| triangle | 24,651,572 | 1,810,041 | 464.8M |
| menu | 122,648,956 | 5,565,545 | 860.9M |

Remaining helpers are now dominated by `mul/imul` (1.09M triangle / 1.53M
menu), `movs` (string 1.0M menu), high-byte `mov` (0.8M menu), `fnstsw`,
rotate-through-carry (exact ShiftOps loop), the unplanned `cmp [mem],imm`
with a segment override, and SSE moves. None is a large structural item; the
next lever is C3-y per-member bookkeeping (the §199n item 4), since every
native member still pays EIP+TSC+served stores.

**Validation.** `XGrid.Cpu.Tests` 636/636 (11 new raw differentials: call/ret +
push/pop, ret near, movzx/movsx memory, prefetch, FPU memory/register
arithmetic, FPU integer load/store, MMX movq/movntq, shifts/rotates by
immediate and CL, SSE scalar cluster, shift helper-free pins); full solution
1510/1510 green; format clean; ktest smoke **PASS 7/7** (known deltas, 01:10
wall). Title differentials: triangle RAM `F57934BF...` / CPU `CF79FC17...`,
menu RAM `660CA11B...` / CPU `F5D61C94...` at 1B - the established anchors.

**Quiet-host A/B (same window, 3 interleaved pairs per leg, old = `dcd0f3f^`
built from `git archive`, new = `dcd0f3f`; 1B, `--jit --jit-threshold 16
--jit-spin --jit-raw`; SHAs and counters identical between old and new on
every pair).**

| leg (1B) | wall old | wall new | delta | outerMs old -> new | ns/served member |
|---|---:|---:|---:|---:|---:|
| triangle `--gpu sw` | 14.74 s | 13.14 s | **-10.8%** (3/3) | 5596 -> 4039 (-27.8%) | - |
| menu `--gpu sw` | 52.20 s | 48.43 s | **-7.2%** (3/3) | 40908 -> 37154 (-9.2%) | - |
| triangle `--gpu gl --gpu-replace` | 7.56 s | 6.49 s | **-14.1%** (3/3) | 5363 -> 4087 (-23.8%) | **11.9 -> 8.8** |
| menu `--gpu gl --gpu-replace` | 34.29 s | 31.56 s | **-8.0%** (3/3) | 30416 -> 27213 (-10.5%) | **36.7 -> 31.6** |

The wall gain is smaller than the outerMs gain because the software rasterizer
dominates those walls (39-48%, §199n) and the `gl+replace` legs still include
the dashboard's device work inside `RunJitBlock`; the pure per-member cost is
now **8.8 ns (triangle) / 31.6 ns (dashboard)** on the GL legs, against the
§199n 18.5 / 61 ns. That is the C3-x target hit (menu 61 -> ~30 ns).

**§199o amendment - C3-y per-member bookkeeping trim: measured NEUTRAL, reverted.**
Implemented the full trim in the working tree: EIP published only at
helpers/exits/polls (the epilogue stores the fall-through once), served as a
compile-time immediate at the same points, and TSC as `entry + compile-time
count` with a per-helper rebase from the context (so a `wrmsr 0x10` handler
stays exact). It was bit-exact at 1B on both titles (same anchors, every
counter identical), but the A/B against `dd976db` was **neutral**: triangle
`--gpu gl --gpu-replace`, 5 interleaved pairs - wall median 7.048 -> 7.015 s
(+0.5%), `outerMs` 4326 -> 4314 (+0.3%); menu showed no win above host noise.
**Diagnosis:** the per-member bookkeeping was off the critical path (store-to-
load forwarding on the same slot, spare issue slots), so removing ~9
instructions/member did not move the wall. This confirms the §199n read that
the remaining engine cost is the *bodies* (probes, helper calls, device work),
not bookkeeping. The change was reverted (no product code kept); the finding
re-aims the next step at **C3-z (inline page pair) or Track B**, not more
bookkeeping. The triangle engine is at 8.8 ns/served member, TCG-class; the
menu's 31.6 ns is dominated by short blocks plus NV2A/device work, i.e., a
Track B/D problem.

**§199o amendment 2 - raw-kind profile, xlat sizing, and flip pacing (same session).**
Added an env-gated raw-kind profiler (`XGRID_RAW_PROFILE=1`, run with
`XGRID_RAW_CHAIN=0`): after each raw runner call the served prefix is
re-planned and tallied by `JitKind` (exact, because an unchained runner cannot
serve past its own members). The 1B menu mix (`--gpu gl --gpu-replace`, 706.6M
profiled members of 860.9M served - the rest are IL-served budgets/tails):

| kind | count | share |
|---|---:|---:|
| MovLoad | 171.6M | 24.3% |
| MovStore | 139.4M | 19.7% |
| DecR | 101.1M | 14.3% |
| AluRR / IncR / AluRI | 96.6M | 13.7% |
| MovRR / ShiftRI / TestRR | 42.6M | 6.0% |
| FpuLoad / FpuArithMem / FpuStorePop | 36.1M | 5.1% |
| MovExtend / PushR / Lea / AluRM | 34.1M | 4.8% |
| MmxLoad + MmxStore | 15.1M | 2.1% |
| RetNear / CallRel | 13.9M | 2.0% |

So the dashboard is a **move/loop workload**, not a helper or x87 workload:
~50% of members are memory operations and another ~20% are loop-counter
inc/dec. Its 31.6 ns/member is the memory probe's dependent-load chain
(tag -> physical -> RAM base), its store guard and branch costs, not helper
dispatch. Two follow-ups measured and **rejected as neutral**:
(1) a 1024-slot data translate cache (256 -> 1024; measured on both titles,
3 interleaved pairs: menu `outerMs` +0.1%, triangle -0.5% - the raw probe's
misses are not dominated by 256-slot conflicts); (2) C3-y (above).
**Flip-pacing probe (closes the §199n queued item):** `FLIP_STALL` semantics
match the reference (stall until `READ_3D == WRITE_3D`; the read index advances
on vblank), so the triangle's 35 flips/1B is render work plus one vblank, not
an extra-frame delay: 1B ticks = 1.36 s of guest time, 35 frames = 39 ms/frame
= ~1 vblank (16.7 ms) + the demo's ~18-22 ms render. The "84 implied by xemu"
number was not an apples-to-apples guest-step measure (xemu's 1B includes its
idle loop and its rate is wall-clock-derived). Playability remains an engine
speed problem (74 Msteps/s vs xemu ~830), i.e., Track B/D, not a pacing bug.
The profiler is kept as an opt-in diagnostic; the xlat size stays 256.


## §199p Rasterizer O2 memory fast lane (2026-10-20, seventh session) - LANDED

**Scope.** The software rasterizer (the default `--gpu sw` path) writes every
color/depth fragment and reads every texel/color destination through
`IGpuMemory` -> `AddressSpace` -> `PhysicalMemory`: an interface call, a
try/finally GPU-writer suppression pair, two bounds layers, a region scan
fallback and a per-write decode-cache page drop. The rasterizer inventory
(`rasterizer-inventory.md` O2) ranked a direct RAM lane as the biggest
structural win; this session implemented it as an *optional* interface so the
test fakes keep the legacy path.

**Design (`IGpuMemoryFast`, `src/XGrid.Nv2a/IGpuMemoryFast.cs`).**
`BeginGpuBatch`/`EndGpuBatch` open one GPU-writer suppression scope per fill
(`FillSurface`, `FillTriangle`, `FillTrianglePsh` via a `GpuWriteBatch`
`using` scope) and reset the adapter's decode-page memo. Inside a batch,
`Nv2aMmio.ReadGpu`/`WriteGpu` use `TryReadPhysical`/`TryWritePhysical`
directly on the pinned `PhysicalMemory.RawBytes` word; anything the adapter
cannot serve exactly - non-RAM, an address overlapping the diagnostic bus
watch (`AddressSpace.WatchStart/End`), or a write outside the batch - returns
false and falls back to the legacy adapter call. The decode cache needs at
most one drop per page per draw (the CPU cannot run mid-draw), and the
present-dirty watch is suppressed for the whole batch exactly as before.

The production adapter (`XboxMachine.PhysicalGpuMemory`, now `IGpuMemory,
IGpuMemoryFast`) implements the lane; `ArrayMemory` test fakes stay legacy,
and `MachineGpuMemory` (the GL backend's texture adapter) does not implement
it (the sw rasterizer never sees it).

**Validation.** New Nv2a differential `GpuMemoryFastLane_MatchesLegacyRasterizer_AndIsUsed`
runs the element-draw golden stream on the fast and legacy adapters, compares
every framebuffer word, and pins the batch/read/write counters (batches
balanced, fast write and read lanes used). SW 1B goldens: triangle RAM
`F57934BF...`, menu RAM `660CA11B...` (both anchors exact), pixel/textured/
blended counters unchanged. Suite 1511.

**Measured (quiet host, 3 interleaved pairs against `dd976db`, 1B, `--jit
--jit-threshold 16 --jit-spin --jit-raw --gpu sw`):**

| title | wall old | wall new | delta |
|---|---:|---:|---:|
| menu | 50.01 s | 48.99 s | **-2.0%** (3/3) |
| triangle | 12.95 s | 12.69 s | **-2.0%** (3/3) |

Smaller than the inventory's 5-12 s estimate: the per-write chain was partly
devirtualized/inlined by the JIT already, so the lane mainly removes the
suppression pair and the per-write decode-drop call. It is kept because it is
consistent (3/3 on both titles), exact, and cheap. The remaining rasteriser
cost is the per-pixel fragment/sampler work (inventory O1/O3/O4/O5), which is
the next Track B slice; O6 (row spans) and O7 stay filed.

**§199p amendment - rasterizer O4 swizzle bit-spread (same session) - LANDED.**
`TrySampleTexture`'s swizzled path called `InterleaveBits` twice per sample
(a per-set-bit mask loop, up to ~8-16 iterations each). The sampler plan's
masks for POT dimensions are the standard alternating Morton masks, so the
plan now checks that property once per draw (`MortonMasks`: maskX has no odd
positions, maskY no even ones) and the sample uses the branchless 4-step bit
spread (`SpreadMortonBits`) masked to the same positions; non-standard masks
(width or height 1) keep the loop path. Exact by construction; SW goldens and
every texture/blend counter unchanged.

Quiet-host 1B `--gpu sw`, 3 interleaved pairs against the O2 commit (SHAs
identical on every pair): **triangle 12.51 -> 11.76 s (-5.9%, 3/3); menu
47.90 -> 47.33 s (-1.2%, 2/3)**. The triangle is the swizzle-heavy title
(38.3M swizzled 32-bit samples), the menu is 76% linear textures, which is why
the split looks like this. Suite 1511.

## §200 Rasterizer O5 measured-negative, then O3b fast sampler + O1 compiled modulate fill (2026-10-20, eighth session) - LANDED

**O5 counter batching: measured negative, reverted.** The inventory estimated
5-12 ns/fragment for the always-on per-pixel diagnostics (texstate min/max,
first address/texel, per-format attempts, draw/blend counters). Before
implementing the batch, the ceiling was measured: a temporary probe elided
*every* hot diagnostic increment (the printed counters went to 0) and ran the
1B goldens. Raster time moved 24.00 -> 23.34 s (menu) and 6.19 -> 6.20 s
(triangle), i.e. the whole diagnostic body is ~2% of the menu raster and zero
on the triangle - below the 2% wall standard. The batch implementation (per-fill
`FillDiag` struct, flushed at fill end) was written, measured neutral
(23.82 -> 24.03 s menu, within host noise), and reverted. The inventory's row
10/17 cost estimates were wrong: these are predictable L1 increments, not the
bottleneck. Do not re-attempt without a new measurement.

**Ablation of the real pixel cost (raster seconds, `--gpu-time`, 1B).** Four
temporary probes (skip sampler / skip fragment / skip combiner / skip
interpolation) decomposed the two loops:

| ablation | menu (baseline 24.0 s) | triangle (6.19 s) |
|---|---:|---:|
| texture sampling | 8.17 (34%) | 1.96 (32%) |
| fragment stage (ProcessFragment) | 7.76 (32%) | 0.98 (16%) |
| combiner evaluation | 4.04 (17%) | 1.72 (28%) |
| interpolation | 4.02 (17%) | 1.05 (17%) |

The per-pixel *descriptor/tap* work, not the counters, is the raster cost.
Two more probes on the sampler: removing the texel decode was neutral, removing
the texel **memory read** was worth ~4.8 s of the menu's 8.2 s (with a
downstream confound, but it bounds the irreducible part).

**Slice 1 - O3b per-fill fast sampler (`SamplerPlan.FastKind`).** The plan now
classifies each slot once per draw as a *fast* shape when the layout is linear
or Morton-swizzled, the texel is 2 or 4 bytes, no axis is BORDER, and each
address mode folds to an integer op (`clean`: clamp, POT mirror, POT
wrap-by-mask). `SampleFast` then runs the exact generic arithmetic (same
`plan.Linear ? u : u * width` scale, same `MathF.Floor`, same fold, same
address expression, same `DecodeTextureTexel`, same `TrackTextureCoords` /
`CountSample`) with the per-sample invariant tests (enable, size, border,
bytes-per-texel, layout branch, wrap switch) gone. Non-standard interleave
masks keep `InterleaveBits`; only the descriptor checks are skipped.

**Exact decode tables.** `DecodeTextureTexel` and `TryReadPixelFloat` replaced
their per-channel float divisions with static tables built by the *same*
expression (`i / 255f` etc., `ReadOnly` once at type init), so every decoded
value is bit-identical and the sampler/blend-destination reads lose 3-4 `divss`
per sample. This is the one change that also helps the generic path.

**Slice 2 - O1 compiled modulate fill (`FillTrianglePshModulate`).** The menu
spends 86% of its pixels (245.8M at 1B) in one shape: kind-1 modulate combiner,
one Project2D stage, lean interpolation, no depth/stencil, opaque color mask,
SrcAlpha/InvSrcAlpha/Add blend, 4-byte color. A per-fill predicate selects a
dedicated loop for exactly that shape; the pixel body is the generic loop's
arithmetic transcribed operation-for-operation (same barycentric expressions,
same perspective renormalization, same interpolation order, same shared
`SampleFast` / `PshCombiner.ModulatePass` / `CompareFunc` / `BlendSrcAlphaAdd`
/ `PackPixel` / `WritePixel` helpers). Per-draw values travel as locals
(color base, pitch, alpha ref, zclip bounds) and the ZCLIP block keeps its
reference position *after* the stage resolve and before the alpha test, so
culled fragments keep their sampler and combiner counters. Cubemap and
depth-format stage-0 textures are excluded so the generic resolve's cube and
shadow branches can never be bypassed; anything not matching falls back to the
generic loop unchanged.

**Diagnostics.** `FastKindDebug` (printed with `--gpu-time`) now reports
`compiled=<fills>/<pixels>/<seconds>` and, when `RasterTimingEnabled`, a
`rej=<reason>:<fills>` histogram of the per-fill rejection bits (this is how the
2-byte and general-mask sampler shapes were found). Two diagnostic switches,
`DiagnosticGenericSampler` / `DiagnosticGenericFill`, force the reference paths
for differential tests.

**Validation.** New `Nv2aSamplerFastTests` renders a two-stage modulate draw
five ways (linear 4bpp clamp with out-of-range texel-unit coords, swizzled 4bpp
mirror, swizzled 2bpp wrap-by-mask, and two non-standard-interleave shapes) and
requires the fast and generic runs to agree on the framebuffer word, the sample
counters, the per-format attempts and the whole `TextureStateStats` line
(first address/texel, min/max U/V). Fail-pre-fix proven by breaking the clamp
fold: the linear case then fails with the exact pixel difference
(generic `FF381C10` vs broken `FF482410`). Suite 1516.

**SW 1B goldens (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`,
1B steps, `--bench`).** Both titles exact against the anchors on every run -
menu RAM `660CA11B...` / CPU `F5D61C94...`, triangle RAM `F57934BF...` / CPU
`CF79FC17...` - and every printed NV2A counter byte-identical (pixels,
textured, blended, depth, zclip, clears, frames, stalls, formats, texstate
min/max/first). `rasterPsh` (host-only, the exact loop time) is a low-noise
metric (six baseline runs 23.82-24.07 s menu, 6.18-6.20 s triangle):

| 1B `rasterPsh` | baseline | + fast sampler | + compiled fill |
|---|---:|---:|---:|
| menu | 24.00 s | 23.35 s | **16.37-17.02 s** |
| triangle | 6.19 s | 5.97 s | 5.98 s |
| menu compiled share | - | - | 10.25 s / 245.8M px (41.7 ns/px) |

The compiled fill is 402,590 of 402,590 kind-1 fills on the menu
(`compiled=402590/245797540`), i.e. the whole dominant shape. Menu wall moved
47.96 -> ~39-40 s in single runs; the interleaved 3-pair wall claim is recorded
in the session close note once the k2/k3 shape is also covered.

**Remaining.** Menu `rasterPsh` 16.4 s = 10.25 s compiled k1 (245.8M px) + ~5.8 s
generic k2/k3 lighting fills (62.4M px, ~93 ns/px) + 0.44 s legacy. The k2/k3
8-stage / 7-stage "squared lighting" combiner fills are also the *entire*
triangle raster (5.98 s), so they are the next and only remaining Track B
lever of this size; they need an exact transcription of
`EvaluateLightingSpecialized` plus the DotSt/DotStr3D/Project2D cube stage
resolve.

## §200 amendment - compiled k2/k3 lighting fill (same session) - LANDED

**What the shape actually is (measured, not inferred).** A per-fill histogram of
`(texModes, texture-enable bits, combiner kind)` showed the two lighting
shapes exactly: **k2** (39.9M px, 39,645 fills) runs modes `0,3,3,2` and **k3**
(22.5M px, 13,929 fills) runs `1,3,3,2` (Project2D, Cubemap, Cubemap,
Project3D) with all four slots enabled and no blend/alpha test, `zetaBytes==0`,
opaque mask. Per-stage resolution counters then showed the decisive fact:
stages 1-3 are resolved 3 x 62.4M times but produce **zero samples, zero
format attempts and zero mode fallbacks** - they are per-fill constants:

- Cubemap mode with CUBEMAPENABLE set and an **unconfigured face extent**
  (`_textureImageRect` width or height 0): `TrySampleTextureCube` returns before
  any counter, texel = `SetTexelNone` (0,0,0,1).
- Project3D on a **depth-format** texture (color 0x30): `TryResolveShadow`
  short-circuits on `ShadowDepthFuncNever` (zero texel) / `Always` (one texel)
  with no counters.
- Project3D on a 3D color texture with an unconfigured extent: `SampleInto3D`
  takes the unconfigured branch with no counters.

**Predicate (`FillTrianglePsh`).** The compiled lighting fill engages only when
`_combinerFastKind` is 2/3, `bytes==4`, `zetaBytes==0`, mask all, no blend, no
alpha test, no clip-plane stage, no dot-string stage, and every stage 1-3 is one
of the constant cases above (verified per fill from the same registers the
generic resolve reads, including `_shadowDepthFunc`). Stage 0 must be `None`
(k2) or Project2D with a fast sampler plan, no cubemap, no depth format (k3).
Otherwise the generic loop runs unchanged.

**Loop.** Barycentric/perspective/interpolation are transcribed exactly; the
diffuse RGB interpolation is kept (alpha is dead: `EvaluateLightingSpecialized`
reads `diffuse[0..2]` only), specular and the stage 1-3 texcoords are not
interpolated (dead for this shape; stage 0's texcoords are), stage 0 samples
through the shared `SampleFast` with `_textureFormatAttempts`/`TexturedPixels`
kept, and the combiner is `EvaluateLightingSpecialized` transcribed
operation-for-operation with its per-draw constants (`consts[0]`,
`consts[combineStage*2]`, `consts[combineStage*2+1]`, `consts[addStage*2]`,
`consts[16]`) hoisted into locals. The fragment stage is the ProcessFragment
color path for the proven state (no alpha test, no blend, needDst false), ZCLIP
kept at its reference position (after the stage resolve, before the write).
`DiagnosticGenericFill` forces the reference loop for differentials.

**Measured (1B, `--gpu sw`, interleaved pairs against `8a7b94b`; SHAs and every
counter identical on all 12 legs: menu `660CA11B…/F5D61C94…`, triangle
`F57934BF…/CF79FC17…`).**

| 1B `rasterPsh` | baseline | final | delta |
|---|---:|---:|---:|
| menu | 24.0-29.4 s* | 13.9-15.1 s | **-44%** |
| triangle | 6.19-7.04 s* | 3.31-3.45 s | **-45%** |

\*the baseline leg drifted upward during the session (host load); the ratios are
interleaved. Wall, 3/3 pairs: **triangle 12.44 -> 9.51 s (-23.5%),
menu 53.78 -> 42.01 s (-21.9%)** (medians; the shared host slowed ~15% between
the session start and these pairs, so the session's own quiet-host anchors read
47.96 -> ~39-40 s for the menu, 12.21 -> ~9.4-9.8 s for the triangle).

A later quiet-host pass (single runs, same anchors) read **menu 47.96 -> 36.01 s
wall / 13.34 s raster** and **triangle 12.21 -> 9.13 s wall / 3.30 s raster**.
Compiled shares at 1B: menu `compiled=456164/308242392/13.8s` (245.8M modulate +
62.4M lighting), triangle `compiled=53574/62444852/3.2s`. Remaining raster:
~0.45 s legacy on both titles plus the k1/k2/k3 compiled loops themselves
(menu 14.1 s total = 13.8 s compiled, i.e. the rasterizer is now a single
specialized path for 96% of menu pixels and 85% of triangle pixels).

**Negative result kept for the record.** Hoisting the cube-map face stride and
swizzle masks into the sampler plan (exact, removes a mip-level loop and two
interleave loops per cube sample) measured **neutral**, interleaved A/B 0.02 s
below resolution, because the titles' cube stages never reach the face math
(unconfigured extents). Kept in the plan anyway: it is exact, removes real work
on any cube-mapped content, and the plan already carries the fields.

Suite **1519** (8 new differentials: 5 fast-vs-generic sampler shapes, the compiled modulate fill and both compiled lighting kinds, each asserting the specialized path engaged and comparing the framebuffer + every sampler/combiner counter), ktest smoke PASS 7/7.

**Accurate-mode (gate-currency) differential.** The rasterizer is shared, so the
same 1B legs were run with the JIT off (accurate mode) on the session-start build
and this one: **RAM and CPU SHAs byte-identical** (menu `D9C99A58…` /
`3D2740B8…`, triangle `F57934BF…` / `98039867…`), every NV2A counter identical
(the accurate-mode menu counters `pixels=285171381 textured=278577344
blended=211696978 clears=695 frames=686` match the §109-era recorded baseline
exactly). `rasterPsh` in accurate mode: triangle **6.90 -> 3.63 s (-47%)**;
menu wall 87.3 -> 75.6 s (-13.4%). The accurate-mode *triangle* wall barely
moves (48.1 -> 48.0 s over two interleaved pairs) because the interpreter is
~90% of that wall - the raster win is a fast-mode lever, not an accurate-mode
one.

**Deferred with an estimate: C3-z inline page pair.** The charter's scoped CPU
experiment (a per-block one-entry VPN->phys cache in a raw frame slot plus a
TLB epoch) was priced by inspection against the raw probe: a hit would save
about 10 of the probe's ~18 instructions, but the cache test itself costs a
load+compare pair, and the raw member mix is only ~50% memory members, so the
expected `outerMs` win is ~2-3% - at or below the experiment's own 2% keep bar,
with a correctness surface (stale page after a remap) that the epoch must cover.
Not attempted; the next session can run it with the acceptance criteria already
written in `next-session.md`.

## §201 xemu 0.8.137 adopted as the primary oracle (2026-10-20)

**Decision.** `References2/xemu-0.8.137` replaces `References2/xemu-0.8.136` as the
primary LLE reference. 0.8.136 stays vendored so the historical file:line citations in
this document remain traceable; any fact that matters to current work is re-extracted
from 0.8.137 before it is used.

**Why.** 0.8.137 is a substantially newer snapshot (QEMU 10.2.0, 11,352 files):
`hw/xbox` goes 164 -> 167 files with **150 differing or new**, of which `nv2a/` is 88/97
and `mcpx/` is 38 files. The material addition is the APU DSP: `mcpx/apu/dsp/` now
ships a full GP/EP interpreter (`interp/dsp_cpu.c`, 1,176 lines), an optional JIT
(`dsp_jit.c`, 320 lines, selected by `g_config.audio.use_dsp_jit`), a restructured
`dsp.c` (ops vtable) and an updated 441-line `gp_ep.c`. That is the reference for the
remaining phase-12 DSP/mixing work; the §121 GP/EP bridge currently reproduces the
handshake and notifiers only.

**What to re-check.** APU/DSP semantics and DMA first; then the NV2A pgraph deltas
(`methods.h.inc`, `texture.c`, `surface.c`, `glsl/psh.c`, `glsl/vsh-prog.c`) and
MCPX/SMBus/USB/XID. The measured file-hash delta is catalogued in
`docs/research-notes/xemu-0.8.137.md`; `xemu.md` keeps the 0.8.136-era facts with a
superseded banner. Licensing is unchanged (GPL-2.0 overall, facts only; `dsp/**` is
GPL-2+).

**Process.** AGENTS.md, ImplementationPlan.md, `sources.md`, `xemu.md` and
`xemu-playtest-v2.md` now name 0.8.137 as primary. Future notes should cite the
snapshot they actually read; a 0.8.136 citation is acceptable only when the relevant
file is unchanged in 0.8.137 or the point is explicitly historical.

## §202 Rasterizer compiled-loop fast lanes: direct-RAM texel/dest reads, per-fill decode, lighting clamp drops (2026-10-20, ninth session) - LANDED

**Method (measure first, exactly as §200 demands).** A temporary ablation switch
(`XGRID_RASTER_PROBE`, removed before commit) ran the two 1B golden legs with
individual blocks elided, so every slice below is chosen by a measured ceiling
rather than an op-count guess. Menu (`--jit --jit-threshold 16 --jit-spin
--jit-raw --gpu sw`, `compiled` = the two loop timers, 308.2M px / 278.9M samples):

| ablation | compiled | delta |
|---|---:|---:|
| baseline | 13.47 / 13.37 s | - |
| all sampler work elided | 11.85 / 11.77 s | **-1.6 s** |
| texel decode elided | 12.34 / 12.42 s | -1.0 s |
| `TrackTextureCoords` elided | 13.33 / 13.44 s | ~0 |
| `CountSample` elided | 13.27 / 13.26 s | -0.15 s |
| blend destination read elided | 13.54 / 13.51 s | -0.5 s |
| pack+blend elided | 12.35 / 12.45 s (extra pack) | pack ~0.3 s |
| surface write elided | 13.54 / 13.84 s | ~0.2 s |

Two shapes were also measured directly on the traffic the titles run: **93.5% of the
menu's k1 fragments have `fa == 1`** (198.15M of 212.00M) and the alpha-1 skip path
produced **0 mismatches over 198M pixels** against the full blend. Implementing it
anyway measured **neutral** (13.72/13.66 vs 13.89/13.37): the destination read is only
~2.4 ns/px, so a per-pixel branch costs what it saves. Not landed.

**Slice 1 - per-fill direct-RAM sampler with a per-fill decode branch (both compiled
fills).** `IGpuMemoryFast.TryGetRamBytes(out byte[] bytes, out uint size)` exposes the
RAM array and the exact size `TryReadPhysical` bounds-checks against.
`SamplerAddressBound` proves the highest address a plan can read (the accepted wrap
folds keep the coordinate inside the image; a swizzled index stays inside the union of
the two swizzle masks), so one per-fill test replaces the per-sample interface call:
inside the bound the loop reads `BinaryPrimitives.ReadUInt32LittleEndian` over a
`MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref ramBase, addr), 4)` (same
little-endian decode, same four-byte requirement as the adapter, so a 2-byte texel
still reads a word and masks), otherwise it calls `ReadGpu` unchanged. `SamplerPlan`
gained `DecodeKind`, the exact `DecodeTextureTexel` branch for the fill's color
(0 = the A8R8G8B8 default branch, 1 = A4R4G4B4, 2 = the generic switch), so the
per-sample jump table is gone for the two shapes the titles use. The blend
destination read (`TryReadPixelFloat`'s 4-byte branch) uses the same direct word when
the fill's whole framebuffer span is inside RAM. Both compiled fills transcribe
`SampleFast` operation-for-operation; the generic path keeps `SampleFast`.

**Slice 2 - lighting-loop provable clamp drops and per-row hoists.** The reference
chain `R1 = clamp(max(R1,0)^2)` then `clamp(R1*R1)` keeps R1 in [0,1], so every
following -1..1 clamp is a provable no-op (NaN passes both forms identically); the
combine-stage products `c0c*max(R0,0)` and `c1c*max(R1,0)` are no-ops whenever
`|c0c|,|c1c| <= 1`, which is now a per-fill predicate (measured: **all 53,574
lighting fills** pass) with the generic loop as the fallback. `addCd = clamp(1*c0Add)`
is a per-fill constant and is hoisted. Both compiled fills hoist `py` and the two
barycentric y terms, which are loop invariant in x (same expressions, same operands).

**Negative result kept: the k2 constant fold.** For kind 2 the stage-0 dot inputs are
the C0 constants and stages 1-3 are per-fill constants, so the whole dot/power/combine
chain collapses to per-fill values. It was implemented exactly (same expressions
evaluated once) and measured **2-3% slower** on the same binary with the switch
interleaved (`--jit` triangle, fold on 3.15/3.15/3.17 vs off 3.11/3.07/3.07 s psh):
the hoisted constants did not stay in registers and the branch/register pressure cost
more than the folded math saved. Reverted. `[MethodImpl(AggressiveInlining)]` on
`SampleFast` likewise measured neutral (psh +0.1/-0.7/-0.1%) and was dropped.

**Measurement lesson (cost an hour).** The candidate leg of the first wall A/Bs still
carried the CPU-side probe timers from the investigation (`RunNonJit` Stopwatch pair,
block-build timers). That pair costs ~0.35 s over 479K calls and read as "the raster
win did not reach the wall" (+1.3% triangle wall against a -10% raster). Revert probe
instrumentation **before** any A/B leg; the same-binary gl-replace control (6.48/6.44/
6.51 vs 6.83/6.79/6.80 s) is what localised it.

**Validation (fast mode 1B, 3 interleaved pairs, `--gpu sw`; SHAs and every NV2A
counter byte-identical on all legs - menu `660CA11B...`/`F5D61C94...`, triangle
`F57934BF...`/`CF79FC17...`).**

| 1B | baseline (`65e136e`) | final | delta |
|---|---:|---:|---:|
| triangle `rasterPsh` | 3.39 s | 2.87 s | **-14.2%** (3/3) |
| triangle wall | 9.15 s | 8.69 s | **-4.0%** (3/3) |
| menu `rasterPsh` | 13.47 s | 12.49 s | **-5.3%** (3/3) |
| menu wall | 35.45 s | 34.64 s | **-2.3%** (3/3, -0.3/-2.4/-4.2) |
| menu `compiled` | 13.18 s | 12.20 s | -7.4% |

**Accurate mode (gate currency) is unchanged:** 1B legs with the JIT off reproduce the
§200 anchors exactly - menu RAM `D9C99A58...` / CPU `3D2740B8...`, triangle
`F57934BF...` / `98039867...` - with every counter identical (menu pixels 285171381,
textured 278577344, blended 211696978, clears 695, frames 686; triangle 73781653 /
33394020 / 518 / 306868 depth / 35 frames) and the texture-format histogram identical.
`rasterPsh` in accurate mode: menu 13.22 -> 12.14 s (-8.2%), triangle 3.22 -> 2.75 s
(-14.6%). The accurate *wall* barely moves (menu 74.2 -> 70.9 s, triangle 43.0 -> 43.3 s
within host noise) because the interpreter owns ~90% of it.

**CPU-side investigation (measured, no change landed).** Two candidate levers were
priced and dropped: (a) the non-JIT time is not interpreter overhead to be trimmed -
with gl+replace the triangle's `nonJitMs` falls 5.35 -> 2.5 s, i.e. most of it is the
software raster executing inside an MMIO member (`Mov` 1.15M members at 1.64-3.38 us
= 3.9 s of 5.8 s, matching `rasterPsh`); (b) block builds cost **~200 us raw + ~20 us
IL** per block (`buildRaw=1222 ms` / `buildIl=133 ms` for 6,202 blocks at 1B), and a
threshold sweep (16 / 64 / 256) trades build cost for interpreter time exactly one for
one (outer -1.1 s, nonJit +1.4 s at 256), so `--jit-threshold 16` is at its optimum.
The IL runner is built even when the raw runner succeeds; making it lazy would save
only the 133 ms, so it was not done.

Suite **1519** green, `dotnet format --verify-no-changes` clean, ktest smoke **PASS
7/7** (known deltas match).

**§202 addendum - `--jit-mmio` was broken (pre-existing) and is now exact.** The
opt-in device fast path (`--jit-mmio`, `RawBlockEmitter.EmitDeviceLoad` /
`EmitStore`'s device labels) had been unusable: the 1B menu leg halted at
**25,146,493 steps** (EIP `0x800151EF`, IF=0) with a different RAM image. Bisected by
1B-leg SHA bisection to a single raw block (`head=8001A3DC`, 13 members) whose final
`jne` (`cmp [0x80035C34],0` earlier in the block) took the wrong direction; the
block's own emitted code is byte-identical in both configurations and the device path
is **never executed** (`mmioMembers=0`), so the flag's only effect was code layout.
Disabling the store device label alone cured it, which pointed at emitter bookkeeping
rather than at the device code:

- `EmitMaterializeFlags()` writes the **host** FLAGS into `F_EFLAGS` when the
  emitter's compile-time `_flagsLive` is true, and `EmitReloadAfterHelper()` (the end
  of every device block) sets `_flagsLive = true`.
- The device block is emitted *before* the member's `slow` block, so that
  compile-time `true` leaked into the `slow` code: `EmitSyncForHelper()` then
  materialized the **probe's own comparison results** (the host FLAGS were already
  clobbered by the probe) into `F_EFLAGS`, and the next flag consumer loaded them.
- Fix: reset `_flagsLive = false` before `a.Label(ref slow)` in both `EmitLoad` and
  `EmitStore` (compile-time state does not flow along the emitted control flow), and
  give the device paths `EmitSyncGuestStateForHelper()` (guest-state publish +
  progress, no flag materialization) since the probe already materialized the correct
  flags. The `[Fact] raw` **`MmioFast_FlagsAcrossSlowMemoryMember_MatchInterpreter`**
  runs a `cmp -> watched store (slow path) -> jne` loop with `JitMmioFast` on and
  matches the interpreter; fail-pre-fix proven (EFLAGS 0x70 vs 0x130).
- Result: `--jit-mmio` now completes both 1B goldens with the exact anchors (menu
  `660CA11B...`/`F5D61C94...`, triangle `F57934BF...`/`CF79FC17...`). It is **not a
  perf win on these titles** (menu 33.4/33.2/33.7 s with the flag vs 33.6/32.9/32.9 s
  without; `mmioMembers=0` - the probe already routes device pages to the slow path),
  so it stays opt-in and out of the gate recipes. The bug is worth recording because
  the same "state leaks across a never-taken branch" class would bite any future
  emitted-path addition.

Suite **1522** (Nv2a 255, Cpu 637).

**§202 addendum 2 - where the raw runner's slow memory members go.** A temporary
counter pair on the raw slow-path helpers (`RawReadMemory`/`RawWriteMemory`, kept as
the `slowMem=`/`slowDev=` fields in the JIT stats line) measured 1B: **menu 1,996,335
slow memory members of which 119,126 (6%) are device space** (linear >= 0xFD000000);
triangle 76,104 / 14,732. So the `--jit-mmio` device lane can address at most ~36 ms of
the menu, and the remaining 1.88M are translate-cache misses / cross-page / store-guard
entries. Combined with the §199-era finding that a 1024-slot data translate cache was
neutral, that closes the "cache the device translations / widen the probe" idea: the
slow members are cold-page traffic, not capacity misses.

## §203 Course correction: product-path-first (2026-10-20) - docs-only

**Decision.** The canonical priority order changes from "Track C (CPU engine)
leads, then Track B optimizes the software rasterizer" to:

1. **Track B - host GPU present path (lead).** The Avalonia frontend
   (`ea8b38f`) renders on the software rasterizer, so the product is GPU-bound.
   `--gpu gl --gpu-replace` replaces the 12.0 s menu raster yet nets only 2.2 s
   (31.1 vs 33.3 s) because the GL draw/readback/present path costs ~9.9 s over
   686 frames (~14 ms/frame). The 33.6K draws at ~2 us would be ~0.07 s and the
   known readback is 1.6 ms/flip (~1.1 s/1B), so ~8.8 s is unmeasured; measure
   the split before scoping. The per-flip `glReadPixels` sync is the prime
   suspect. First slice: measure the split (submission / readback / present),
   then remove the readback from fast-mode / window present (keep it for gates,
   `--frame`, screenshots). Acceptance:
   menu fast+gl <= 24 s, triangle <= 6 s at 1B, `BackendUnsupported=0`, guest
   SHAs and every counter exact, `--gpu sw` goldens unchanged.
2. **Track E - compatibility corpus.** `docs/compatibility.md` + a
   boot-to-first-frame harness + >= 8 rows (M-B). Fidelity work must cite a row
   and a usage histogram. Gates and the real-content policy are unchanged.
3. **Track P - product completeness (parallel).** Host audio sink, physical
   gamepad binding, save-state V1, per-title config. The frontend is sw-only,
   keyboard-only, silent, stateless.
4. **Track C - frozen.** C3-x hit its target (8.8 ns/served member triangle,
   31.6 ns menu); C3-y and the 1024-slot xlat cache measured neutral and were
   reverted; C3-z is deferred (2-3%). Only the operator's `--jit`/`--jit-raw`
   default-flip decision remains. Re-open only if Track B lands and the engine
   is again >50% of a measured wall.
5. **Track D - measured >2% only.**

**Evidence.** 1B fast mode, quiet host: triangle 8.69 s sw / 6.49 s
gl+replace, menu 33.3 s sw / 31.1 s gl+replace; xemu ~830 Msteps/s on the
triangle (~7x gap), menu ~27x real time. 1 guest-second ~= 733M steps. The
software rasterizer is 12.0 s of the menu wall; the present readback is the
larger remaining lever.

**What this is not.** It is not a reversal of the JIT work: the raw emitter,
chaining, and fault contract stay landed and opt-in, and the fast-mode contract
is unchanged. It is a re-aim of where the next sessions go, because the engine
is no longer the largest measured item and the product surface is now real.

**Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
measured ceiling behind its acceptance. A task that serves none of them is
rejected even if it is interesting.

**Docs updated:** `docs/development-direction.md` (rewritten §0-§6),
`AGENTS.md` (direction + next-session pointer + session note),
`ImplementationPlan.md` (direction section, phase rows 12/15/18/20-23, milestone
pointer, performance-campaign supersession note), `next-session.md` (§0c, §2-§5),
`docs/compatibility.md` (new), `compatibility-corpus.md` (new). No code changed.

## §204 Track B slice 1: the GL present path measured, readback removed, per-draw submission cut (2026-10-20, eleventh session) - LANDED

**The measurement (§203 asked for it first).** `GlBackendTimings` already
split draw submission / clear / readback; a 1-in-64 sampled probe
(`XGRID_GL_PROBE=1`, section "Diagnostics") added the intra-draw split. Menu
1B `--gpu gl --gpu-replace`, quiet host, before any change:

| item | seconds | note |
|---|---:|---|
| draw submission (`drawMs`) | 6.87 | 2,760,841 draws = 2.49 us/draw |
| clear submission | 0.01 | 696 clears |
| flip readback (`ReadPixels`) | 0.71 | 686 flips = 1.04 ms/flip |
| BGRA swizzle + 2x 1.2 MB alloc per flip | ~0.3 | untimed (new array + per-pixel loop) |
| **total GL path** | **~7.9** | of the ~9.9 s §203 attributed to GL |

So the per-flip readback was **~1 s of the ~10 s**, not 8.8: the *per-draw
submission* was the lever. The sampled probe (with an ablation build for the
disputed items) put the submission split at ~60% `ApplyTextures`, ~25% the 13
per-draw `glUniform` calls, ~10% `BufferData` + `DrawArrays`, ~5% program
state. Root causes, each measured:

1. `PshGlslGenerator.BuildKey` allocated a 500-char string per draw (structure
   key, though the structure changes ~11 times per run).
2. One `string.Create` texture cache key per slot per draw, and
   `FingerprintTexture` recomputed the 16 sample addresses every draw: **two
   integer divisions per texel** (32 divisions/draw) plus a load -> FNV ->
   load chain that serialised 16 cold DRAM lines.
3. Thirteen `glUniform*` calls per draw (`uPx`, `uColorV`, `uSpecV`, `uT0V`..
   `uT3V`, `uVtx`, 5 scalars) for what is one per-draw block.
4. Redundant `glBindFramebuffer`/`glViewport`/`glUseProgram`/
   `glActiveTexture`/`glBindTexture` per draw.
5. `glBufferData` per draw for 24 bytes of NDC vertex data (the vertex shader
   only consumes positions, which are already in the uniform block).
6. `Stopwatch.GetElapsedTime` (TimeSpan) per draw.
7. Latent bug: a texture content re-upload created a **new** GL texture and
   deleted neither the old id nor updated the entry, so later draws bound the
   stale texture (the entry kept the old id). Fixed with the entry owning a
   settable id and deleting the old texture.

**Landed slices.**

1. **Readback is opt-in.** `GlPgraphBackend.FlipReadbackEnabled` (default true
   for API compatibility) is set by the CLI only when `--frame` or the new
   `--frame-first` is given; a flip otherwise stays on the GPU (the window
   present blits the FBO, never CPU pixels). `--frame` behavior is unchanged,
   so the goldens keep the exact pre-slice path.
2. **Packed per-draw uniform block.** All per-draw values travel as
   `uniform vec4 uTri[25]`, uploaded with **one** `glUniform4` per draw
   (slot layout is the shader contract, pinned by
   `PackedTriangleUniformBlock_ReplacesPerDrawUniformArrays` and the two GLSL
   string goldens).
3. **Program structure cache.** The last draw's 37 combiner structure words are
   compared (10 ns-ish) and the key string is only built when they change.
4. **Texture cache.** `TextureKey` value struct + per-slot last-hit cache (no
   per-draw string), per-entry sample-address table (divisions move to upload
   time), 16 independent loads plus `Sse.Prefetch0` over the sample lines.
5. **GL binding shadows**: FBO, program, viewport, active texture unit,
   per-slot bound texture; invalidated on any out-of-band Gen/Delete.
6. **Vertex positions by `gl_VertexID`.** The vertex shader derives
   `gl_Position` from `uTri[0]`/`uTri[1]`/`uTri[2].zw`/`uTri[24].xy`; the per-draw
   VBO upload is gone (2 GL calls per draw: one uniform, one draw). The NDC
   mapping and the centroid inflate mirror the old C# math; the inflate only
   widens GL coverage and the fragment shader's software-exact inside test still
   decides every pixel (goldens byte-identical at 200M, policy-clean at 1B).
7. **`GlBackendTimings.AddDrawTicks`** (raw tick delta, no TimeSpan per draw).
8. **Texture-creation double-upload fixed**: a new cache entry started with
   fingerprint 0 and was immediately re-uploaded, so every unique texture was
   sent to GL twice (menu 25 uploads, triangle 19). The entry now stores the
   fingerprint of the bytes just uploaded (13 and 10 respectively); frames are
   byte-identical (`6BE6325F` / `3FC4B419`).
9. **Track D bonus, measured on a shared path.** `Nv2aMmio.CollectMethodHistogram`
   (default true for the tests) is turned off by the CLI: the two dictionary
   updates per PGRAPH method cost **4 s of the 1B menu sw wall (64.2 -> 60.3 s,
   -6.2%)** and no CLI path consumes the histogram (`--pgraph` dumps
   `MethodStream`). `IPgraphBackend.ObserveMethods` (default true) lets a
   backend decline the per-method callback; `GlPgraphBackend` declines it and
   the CLI prints `methods=<Nv2aMmio.MethodTotal>` instead. That callback alone
   was **5.8 s** of the loaded menu gl wall (18.1M virtual calls through a
   per-method seam in the pushbuffer pump).

**Evidence (same-window, 3 interleaved pairs, baseline `4f0bd47` vs this
session, SHAs identical on every pair).**

| leg | baseline | candidate | delta |
|---|---:|---:|---:|
| menu `gl+replace` 1B wall | 63.09 s (med) | **45.25 s (med)** | **-20.5%**, 3/3 |
| menu `gl+replace` `drawMs` | 12.55 s | **2.41 s** | -81% |
| menu `gl+replace` readback | 1.50 s (686) | **0** | removed |
| triangle `gl+replace` 1B wall | 10.26 s (med) | 9.89 s (med) | -0.9%, 3/3 |

The same ceremony is now one command: `scripts/present-path-acceptance.ps1`
(interleaved sw/gl pairs, medians, `drawMs`/readback, projection against the
recorded quiet references, SHA check). **Host-state correction (measured at
session close):** the recorded quiet references (menu sw 33.3 s, triangle sw
8.69 s) are **not reproducible on this host today** — the *unmodified baseline
build* (`4f0bd47`) measures menu sw **62.57 s** and triangle sw **14.01 s**
(1.88x / 1.61x slower) with an idle CPU (0.7 cores background; the CPU reports
its 3.6 GHz base clock), and its raster share is inflated unevenly (menu
`rasterPsh` 20.0 s vs the recorded 12.0 s = 1.67x). Under that uneven inflation
no single host factor converts today's walls into the reference session's, so:

- **Durable evidence (host-independent):** the same-window A/B table above
  (both builds measured back to back), `drawMs` 12.55 -> ~2.1-2.4 s, readback
  1.5 s -> 0, `BackendUnsupported=0`, and the exact goldens.
- **M-A status: not decidable on this host.** The acceptance run's projections
  (menu 24.8-25.1 s, triangle 6.0-6.1 s, i.e. at the boundary) depend on which
  leg is used as the host reference and move by >1 s under that choice. Re-run
  `present-path-acceptance.ps1` on the reference host state (menu sw ~33 s
  before trusting the SUMMARY lines) before recording M-A as met or missed.
- **What is not in doubt:** the ~7 s of GL-path overhead that §203 measured is
  gone, and the guest/counter/golden state is unchanged.

**Status: the slice delivers the measured GL-overhead removal but M-A remains
open (needs a reference-state measurement).** If the reference-state run still
shows the menu over 24 s, the remaining items are per-triangle batching (2.76M
triangles x 2 GL calls is ~0.4 s quiet) and the texture-content fingerprint
(~0.7 s quiet) - even removing the fingerprint entirely would leave ~24.1 s, so
batching is the structural step. The triangle is CPU-bound (`nonJitMs` 3.75 s of
`outerMs` 6.3 s).

**Gates (all green).** 1530 tests; `dotnet format --verify-no-changes` clean;
1B menu golden sw `4EF1FBF6…` (anchor) vs gl `6BE6325F…` (diff 1852 px,
maxDelta 1, 0 violations); 1B triangle golden sw `A1740DE0…` (anchor) vs gl
`3FC4B419…` (diff 16744, maxDelta 1, 0 violations); ktest smoke **7/7 rows
green** with the XGrid leg at **792,800,535** steps (suite log 40,797 bytes,
byte-identical to the accepted run). `BackendUnsupported=0`, every counter
identical on both goldens. Accurate mode is untouched (every change is
host-side; the sw anchors reproduce exactly).

**What is left on the GL path (measured, not guessed).** Menu quiet draw
submission is now ~1.2-1.5 s: the texture-content fingerprint is ~0.5-0.9 s
(16 scattered cold lines per draw; the *exact* fix is guest-write span tracking
in `PhysicalMemory`, not weaker sampling - see below), the two GL calls are
~0.4 s, the rest is state compares and packing. Further reduction needs
per-triangle batching (instanced draws with a texture-buffer-object for the
per-triangle block), which is the structural next step if the 24 s/6 s targets
must be met without Track C. **Rejected this session (measured):** weakening
the fingerprint to a contiguous window or fewer samples (a silent fidelity
regression), and a per-write texture-span watch (an extra compare on every
guest write: ~0.2-0.4 s, net +0.5 s at best, and it touches the frozen CPU hot
path).

**Diagnostics added.** `XGRID_GL_PROBE=1` -> `probe[samples prep tex state uni
vtx gl fpRam fpAdapter]` (1-in-64 sampled, only sampled draws pay), `--gpu gl`
prints `readback=on|off` and the CLI prints `methods=` from `MethodTotal`,
`--frame-first <bmp>` saves the first presented frame with its step anchor
(`FrameFirst step=...`).

**Docs updated:** `docs/compatibility.md` (harness rows + reproducible
commands), `scripts/boot-to-first-frame.ps1` (new Track E harness),
`docs/development-direction.md` (measured position), `AGENTS.md` (session note +
current state), `next-session.md` (rewritten), `ImplementationPlan.md`
(status), plus the two other-session reference notes
(`gpu-reference-map.md`, `nv097-coverage.md`) that are indexed from
`next-session.md`.

## §205 Track B draw-command batching: one instanced draw per PGRAPH primitive flush (2026-10-20, twelfth session) - LANDED

**Question.** §204 left menu GL submission at ~1.2-1.5 s quiet, and named
"per-triangle batching (instanced draws)" as the structural next step. Before
building it, the batchable run length was measured, because the whole idea dies
if a draw command is one triangle.

**Measurement first (no code changes).** One 1B menu `--gpu gl --gpu-replace`
run prints both numbers the ratio needs: `NV2A draws executed=33638` (draw
commands, one per PGRAPH primitive flush) and `GPU backend=gl draws=2761019`
(triangles offered to the backend) - **~82 triangles per draw command**. The
1-in-64 `XGRID_GL_PROBE=1` split of `drawMs=2171` (786 ns/triangle) was texture
work ~53%, uniform upload ~23%, `EnsureProgram` ~12%, `glDrawArrays` ~7%,
state compares ~4%. So batching is worth ~2 s at 1B, and a *per-triangle* GL
call reduction of ~98% is available.

**Design (uniform-array blocks + instancing, not a texture buffer).** The §204
plan sketched a TBO indexed by `gl_InstanceID`, but that moves the 25-vec4 block
fetch into the *fragment* stage - ~19-25 texture-buffer lookups per fragment,
i.e. a per-pixel cost on a path where there are 210M fragments/1B. Instead the
per-triangle block stays a **uniform array** of `capacity * 25` vec4:

- `flat out int vTri` in the vertex stage carries `gl_InstanceID` to the
  fragment stage; both stages resolve the block with `#define T(i)
  uTri[((vTri) * 25) + (i)]`. Dynamic indexing of a *uniform* array is a
  constant-bank read (~1 cycle, and the driver can hoist it per primitive), not
  a memory fetch. `gl_InstanceID` is 0 for a non-instanced draw, so the same
  program serves the single-triangle path (block 0) with no second shader.
- Capacity is the uniform budget: `min(MaxFragmentUniformVectors,
  MaxVertexUniformVectors)` minus a 128-vec4 reserve over 25 vec4, clamped to
  [1, 64]. This host (GL 4.6 compat) resolves to **64**; a minimum-spec GL 3.3
  driver resolves to 3, and 1 disables batching. `XGRID_GL_NO_BATCH=1` forces
  1 (same-binary A/B switch).
- The *boundary* comes from the guest, not from a state fingerprint:
  `IPgraphBackend.BeginDrawBatch()`/`EndDrawBatch()` (default no-ops) bracket
  `Nv2aMmio.FlushDraw()`, the single entry point for every primitive flush. That
  is the only place where "PGRAPH state and guest RAM cannot change between
  these triangles" is *provable*: the flush loop is host-side and runs to
  completion before the next method is processed. `TryDraw` outside a batch
  keeps the exact old behavior, so an unwrapped caller can never be grouped.
- Per batch: `EnsureProgram`, `ApplyTextures` (the 16-word content
  fingerprint), `ApplyState`, `ApplyUniforms`, FBO/viewport/program binds run
  **once**; each triangle appends its 100 floats to `_batchData`; the batch
  submits with one `glUniform4(capacity*25)` + one
  `glDrawArraysInstanced(TRIANGLES,0,3,count)`.
- Ordering/validity: `Clear`, `Flip`, `ResetState`, `Dispose` and any
  `TryDraw` that falls back to software flush the pending batch first, so the
  software/GL write order is the pre-batching order. `drawMs` now includes the
  flush (`timed[drawMs= flushMs= ...]`, `AddBatchFlushTicks` adds to both) so
  submission stays comparable across batched and unbatched runs.

**Evidence (same binary, interleaved pairs, `XGRID_GL_NO_BATCH` off/on, 1B menu
`--gpu gl --gpu-replace`).**

| metric | unbatched | batched | delta |
|---|---|---|---|
| `drawMs` (total submission) | 1971 ms | 1045 ms | **-47% (3/3)** |
| of which batched flush | - | 242 ms | - |
| `batchCalls` (instanced draws) | 0 | 59,437 | 46.5 tri/call |
| wall (median of 3) | 42.20 s | 40.43 s | **-4.2% (3/3)** |
| RAM / CPU SHA | `DD023100` / `F5D61C94` | identical | - |

Per pair: wall -4.8 / -12.3 / -4.2%, `drawMs` -46.9 / -46.4 / -47.1%. The
triangle leg batches too (`batchCalls=2423` for 54,492 triangles, `drawMs`
251.8 ms of which flush 12.7 ms) but its wall is CPU-bound, so the win there is
noise. Projected onto §204's quiet 24.8 s menu projection, -4.2% is ~23.8 s
against the 24 s M-A target - **a projection, not an absolute** (this host
still measures the reference sw legs ~1.9x slow; see `next-session.md` §5).

**Gates (all green).** 1538 tests; `dotnet format --verify-no-changes` clean;
1B menu golden sw `4EF1FBF6.` (anchor) vs gl `6BE6325F.` - **diff 1852 px,
maxDelta 1, 0 violations** (byte-identical to the unbatched GL output, i.e. the
batched fragment path is bit-exact); 1B triangle golden sw `A1740DE0.` (anchor)
vs gl `3FC4B419.` - diff 16744, maxDelta 1, 0 violations; every counter
identical (`draws=33632 frames=686 stalls=686`, `unsupported=0`,
`BackendUnsupported=0`); fast-mode sw anchors exact (menu
`660CA11B/F5D61C94`, triangle `F57934BF/CF79FC17`, pixels/textured unchanged);
ktest XGrid leg at **792,800,535** steps to the 4E03A park with the 40,797-byte
suite log **byte-identical** (SHA `CA5D3867.`). Mirror mode (`--gpu gl` without
`--gpu-replace`) is covered by the menu golden run; replacement mode by the A/B.

**Rejected / not done.** A TBO keyed by `gl_InstanceID` (per-fragment fetch
cost, above). A state-fingerprint batch boundary (a per-triangle compare that
must provably cover every field `PgraphDrawInfo` carries - the guest-side
boundary is exact and free). Re-measuring §204's split from scratch (it stands).
The remaining 803 ms of menu submission is now 2.76M x ~290 ns of block packing
(`PackVerticesInto`, 100 float stores), not GL calls - that is the next
structural item if submission matters again; per §204's fingerprint note it is
below the current noise floor of every other lever.

**Seam regression tests (fail-pre-fix proven).** Four `PgraphBackendSeamTests`
pin the contract the batching depends on, because a moved call would silently
group state across draw commands: `DrawBatch_BracketsOneDrawCommandWithAllItsTriangles`
(one element-draw stream with a second element = 1 begin, 2 draws, 1 end, no
nesting), `DrawBatch_TwoDrawCommandsAreTwoBatches` (two `SET_BEGIN_END` blocks
stay separate), `DrawBatch_SkippedCommandStillClosesItsBatch` (a non-triangle
mode early-returns through the `finally`), and
`DrawBatch_ClearAndFlipArriveOutsideBatches`. Removing the bracket from
`FlushDraw` fails 3 of the 4 - the Clear/Flip test only asserts the negative
invariant.

**Track E rows refreshed (same session).** `scripts/boot-to-first-frame.ps1 -All`
re-ran all six rows (three samples x both backends) on the batched build and
`docs/compatibility.md` now carries the post-§205 walls and SHAs: triangle
20.7 s sw / 10.5 s gl+replace, dashboard 62.0 / 44.6 s, ktest-600M 14.9 / 10.0 s,
with the GL last-frame SHAs still the gate goldens (`A1740DE0`/`777F4DEB`,
`4EF1FBF6`/`36B72FE1` on sw; `3FC4B419`/`8AE82781`,
`6BE6325F`/`CF151863` on gl) and identical draw/flip counters. The GL walls
moved only ~1% here because this host's run-to-run spread is larger than the
parked -4.2%; the durable number stays the same-binary A/B above. Row count is
6 measured rows but still **3 samples**, so M-B's ">=8 rows / >=2 beyond the
three" exit criterion is unchanged and still blocked on the nxdk toolchain.

**Diagnostics added.** `XGRID_GL_NO_BATCH=1` (force the single-triangle path),
`diag[... batchCalls= batchTris= batchCap=]`, `timed[flushMs=]`, probe `vtxMs`
now measures the pack (the state fields measure per batch head).
`scripts/batch-205-ab.ps1` runs the interleaved off/on A/B.

## 206 Environment governance + Track P product slice: references as a single store, a host dimension, XInput and save states (2026-10-20)

**Why this section exists.** Two development machines share one Dropbox tree, and
the plan had no concept of a host. That produced a specific failure: a later
session read the other machine's wall clocks as a regression (`§204`: the
*unmodified baseline* measured menu sw 62.6 s against a recorded 33.3 s), and an
earlier session recorded a 1B fast-mode SHA that "no longer reproduces - environment
moved" (`§199l`). Both are plan defects, not emulator defects.

### 206a References are a single store, verified not assumed

Operator decision 2026-10-20: `References2/` is the canonical **and only** store.
`D:\xgrid-refs-restore\` is retired and must not be reintroduced; no out-of-tree
reference copy is promoted. What replaces the backup is **detection**, not
duplication:

- `scripts/refs-manifest.ps1` writes `docs/refs-manifest.json` (tracked by git):
  one line per set with file count, byte total and a SHA-256 over the sorted
  `path|size|content-hash` list.
- `-Verify` re-derives and compares. Current tree: **9 sets, 43,153 files, PASS**.
- The manifest is a checksum list, not a copy of any reference, so it survives a
  wipe precisely because it lives in git rather than in the folder.
- `xemu-Playtest-v2` is the one set with **no upstream URL**; its loss is
  unrecoverable, which is why the manifest exists at all.

### 206b Host dimension: semantic vs performance anchors

`docs/hosts.md` (new) records a fingerprint per machine; `scripts/host-doctor.ps1`
prints and checks it at session start. The binding rule:

- **Semantic anchors** (1B RAM/CPU SHAs, frame SHAs, every counter) must be
  identical on every host. A mismatch is a determinism bug, never an environment
  note.
- **Performance anchors** (walls, `msps`, `rasterPsh`, `drawMs`) are host-local;
  only same-window A/B pairs travel between hosts.

**H1 verified as the reference host.** All four 1B anchors reproduce exactly
(`vec=0`, no fault): triangle `F57934BF…`/`CF79FC17…` 9.56 s, menu
`660CA11B…`/`F5D61C94…` 39.08 s. So the emulator is deterministic and `§199l`'s
drift was not a live bug here. Walls are +10%/+17% against the recorded quiet
references, so ceilings are read as ratios on H1. The 62.6 s / 14.0 s reading did
not reproduce and is treated as a transient.

### 206c Track P: XInput controller support

`XGrid.Input` was an orphaned project (no references, no tests). It is now the
home of host-input translation - correctly outside `XGrid.Bus`, which must stay
host-API-free:

- `HostPad` - host state in the XID report's shape (digital mask, 8 analog bytes,
  4 stick axes) with `IsNeutral` and shape validation.
- `XInputPadMapping.Map(...)` - **pure** translation, so it is unit-testable
  without a controller. The low byte of the XInput mask is identical to the Xbox
  digital mask (d-pad, START, BACK, thumb clicks), so it passes through; A/B/X/Y
  and LB/RB move to the analog bytes where the real pad carries their pressure;
  triggers use XInput's 30/255 press threshold; sticks use **radial** dead zones
  (7849 left / 8689 right) rather than per-axis, so a diagonal push is not eaten.
- `XInputGamepadReader` - lazy `NativeLibrary` load of `xinput1_4` -> `xinput1_3`
  -> `xinput9_1_0`, no `DllImport`, no throw when absent (a CI test asserts the
  graceful-degradation contract on all three OSes).
- Frontend: 60 Hz `DispatcherTimer` polls, merges with keyboard by OR (digital)
  and max (analog), and is gated on window activation so a background window
  cannot drive the game. No new dependency.

**29 new tests**, suite 1538 -> **1567**.

### 206d Track P: save/load in the app

`EmulatorSession` publishes the live machine and takes a `_machineLock` around
each batch, so `SaveState`/`LoadState` always observe a quiescent machine instead
of racing a half-run batch. `LoadState` also resets the presented-frame baseline
so the restored frame is delivered to the UI. UI: Home buttons, F8/F9, a
`.xgs` save picker (`PickSaveFileAsync` added to `FileDialogService`), and the
existing `.xgs` versioned format is unchanged.

### 206e Host audio is NOT last-mile wiring - scoped, not attempted

`McpxApuDevice` has no PCM sink: its DSP EP "only advances its frame divider
here (its FIFO drain needs an S/PDIF sink)". So a host audio sink requires real
output emulation (EP FIFO drain, mixer sample fetch, S/PDIF framing), not a
buffer handoff, and it has no audio oracle. It is explicitly **not** a Track P
one-session item; treat it as a fidelity feature gated by the usual
row+histogram rule.

### 206f Known environment hazard (evidence)

`dotnet build` for the frontend failed twice with `CreateAppHost` /
`BadImageFormatException: Image is too small` and a locked `.pdb`, both cleared
only by deleting `src/XGrid.Frontend.Avalonia/obj` and `bin`. That is the
signature of Dropbox touching build outputs mid-build, not of a code defect. It
is the third independent argument for moving this tree out of the sync root.

### 206g NV097 backface culling: 41k triangles removed per 1B, 35% of the triangle's pixels, every frame unchanged (2026-10-20)

**Trigger was a measurement, not a hunch.** A benchmark probe of `Halo - Combat
Evolved (USA) (v1.09)` (real ISO, benchmark-only per §4 Corpus) booted to a
presented frame and produced a 188,435-method stream with the method histogram
the plan has been missing. The top of that stream is the path XGrid already
implements — `ARRAY_ELEMENT16` 58,577, transform constants ~28k, `SET_BEGIN_END`
5,368, texture state ~6.5k, transform-program words ~4k — so the draw path is
not the gap. The gap is state XGrid stores nothing for, and the highest-count
entry there was `SET_CULL_FACE_ENABLE` (144), `SET_CULL_FACE` (105),
`SET_FRONT_FACE` (2).

The same probe against the **gate sample** (`triangle.xbe`) showed the same
methods (72 / 52 / 1), so this was not a title quirk: the sample the gates are
built on enables culling and XGrid was drawing the back faces anyway.

**Implementation.** `SET_CULL_FACE_ENABLE` (0x0308), `SET_CULL_FACE` (0x039C) and
`SET_FRONT_FACE` (0x03A0) latch state; `ShouldCull(area)` runs in both fill paths
(`FillTriangle` legacy, `FillTrianglePsh` program) right after the degenerate
rejection. Screen space is Y-down, so a visually clockwise triangle has positive
signed area under `((bx-ax)*(cy-ay)) - ((cx-ax)*(by-ay))`; `SET_FRONT_FACE`
picks which winding is front, `SET_CULL_FACE` which is discarded (xemu's
`NV097_SET_*_V_*` values: 0x0404 front, 0x0405 back, 0x0406 both; 0x0900 CW,
0x0901 CCW). `TrianglesCulled` is a public counter and `culled=` joins the CLI's
NV2A diagnostic line. Snapshot version 3 -> **4**.

**Controlled A/B on both gate legs** (same commands, pre-cull binary built from
the stashed sources, post-cull rebuilt):

| leg (1B, accurate, `--gpu sw`) | RAM pre -> post | CPU | frame |
|---|---|---|---|
| triangle (+`--hdd`) | `5E6A6741` -> `812EA159` | `2F632483` = `2F632483` | `A1740DE0` = `A1740DE0` |
| dashboard (+`--hdd`, pad script) | `D9C99A58` -> `01BE5D64` | `3D2740B8` = `3D2740B8` | `4EF1FBF6` = `4EF1FBF6` |

So culling changes **RAM only**, and only because culled triangles no longer
write zeta. Every CPU SHA and both gate frames are byte-identical. Fast-mode RAM
anchors moved with it (triangle `F57934BF` -> `DD1D516E`, menu `660CA11B` ->
`51B56687`); the anchor tables in `next-session.md`, `compatibility.md` and
`hosts.md` are updated and the two older accurate values in the plan text were
found to belong to the no-HDD invocation (the control reproduced
`D9C99A58`/`3D2740B8` for the HDD command, so it is the triangle pair that was
mislabelled).

**Raster effect (same runs):** triangle pixels **89,142,121 -> 57,663,082
(-35.3%)**, textured 48,754,488 -> 37,475,999, blended 518 -> 390, `culled=41035`
of ~54k triangles; dashboard pixels 285,477,537 -> 253,692,342 (-11.1%),
`culled=41035`. Both are pure overdraw removal: the images do not change.

**Tests:** six in `Nv2aCullFaceTests` — culling disabled draws both windings;
cull-back keeps the front winding and discards the other; cull-front is the
mirror; flipping `SET_FRONT_FACE` flips the survivor; front-and-back discards
everything; cull state round-trips through a snapshot. Suite 1567 -> **1573**.

**Coverage:** `nv097-coverage.md` moves the three methods to `handled`
(108/93 -> 111/90) and closes the "backface culling" feature-gap row.

## 207 Track E: the nxdk corpus is real - 11 rows, 7 samples, and two measured gaps (2026-10-20)

**M-B is met.** The blocker was never the emulator: it was that the corpus had
three runnable rows and six blocked ones, all blocked on a toolchain that had
never been installed. This session installed it, built the samples, generalized
the harness, and measured the rows.

### 207a The toolchain recipe (and the two traps)

MSYS2 at `C:\msys64` (already present on H1) plus:

    pacman -S --needed git make bison flex cmake \
             mingw-w64-x86_64-clang mingw-w64-x86_64-lld mingw-w64-x86_64-llvm

Traps, both cost a build cycle:

1. **`mingw-w64-x86_64-llvm` is mandatory, not optional.** nxdk links its import
   libraries with `llvm-dlltool`, which is not in the clang package; without it
   every link dies with `llvm-dlltool: No such file or directory`. pacman lists
   llvm only as an optional dependency of clang, so the obvious install is
   incomplete.
2. **msys2's own git cannot reach the network** here (`remote helper 'https'
   aborted session`) - its curl/ca-cert stack is not configured. The Windows
   PATH git clones fine. Use `git clone` from outside MSYS2, then build inside.
3. `bash script.sh` does not source `/etc/profile`, so `make` is not on PATH
   even though MSYS2 is installed. The build script prepends
   `/usr/bin:<msys>/mingw64/bin` explicitly and sets `MSYSTEM=MINGW64` (nxdk's
   Makefile hard-errors otherwise).

`scripts/build-nxdk-samples.ps1` wraps all of that: it checks for the toolchain
(it names the missing `llvm-dlltool` and prints the exact pacman line), clones
nxdk on request, builds the requested samples, and stages ISOs into
`data/test-samples/corpus/`. It builds **nxdk's own `samples/<name>`** at a
pinned commit (`a6c31ee`), not the copies under `data/test-samples/`: those are
stale partial snapshots (see 207d).

### 207b Harness: `-Corpus`, and two measured corrections

`scripts/boot-to-first-frame.ps1` gained `-Corpus` (every staged ISO),
`-IsoPath` (any loose ISO) and a `-CorpusSteps` budget of 1B.

- **400M steps is not enough for a corpus row.** At 400M every sample sat in the
  boot animation and returned identical counters (25 frames, 2,654 draws) - the
  boot animation is the dominant draw source, so identical output looks like a
  harness bug rather than "not booted yet". At 1B the samples separate.
- **Corpus rows must use HDD + DVD.** DVD-only runs never launch the disc's XBE:
  all five samples ended in the kernel idle thread with the boot animation as
  their last frame. With the HDD present the kernel launches the disc XBE, which
  is the same media set the triangle gate row uses.
- The `-All` set (`dashboard`/`triangle`/`ktest`) is untouched, and corpus names
  that collide with a named sample (`triangle`) are skipped with an explicit
  message instead of silently shadowed.

PowerShell lesson worth keeping: **variable names are case-insensitive**, so a
`foreach ($iso in $isos)` loop variable silently collides with a `[string]$Iso`
parameter and gets coerced to a string - every row printed an empty sample name
and re-ran the same media. The parameter is now `$IsoPath` and the loop variable
`$corpusIso`.

### 207c Measured rows (1B fast, HDD+DVD, `--gpu sw`)

| sample | frames | draws | last frame | RAM | CPU | verdict |
|---|---:|---:|---|---|---|---|
| `mesh` | 66 | 2,864 | `806CBEA7` | `D6A41BAC` | `6DA19D89` | PASS - renders its own content |
| `gamma-fade` | 34 | 2,672 | `18C7CC83` | `ED83DC23` | `D0A7BB8A` | PASS boot, frame not captured |
| `gamma` | 34 | 2,672 | `18C7CC83` | `4EE18F78` | `C3371A8C` | PASS boot, frame not captured |
| `hello` | 34 | 2,672 | `18C7CC83` | `4650454F` | `C3371A8C` | PASS boot, frame not captured |
| `led` | 34 | 2,672 | `18C7CC83` | `3D1F72DB` | `1C98838F` | PASS boot, frame not captured |
| `xaudio` | 34 | 2,672 | `18C7CC83` | `B0C863C5` | `07DB79D2` | PASS boot, APU live |

`mesh` is the only new sample that renders: 5,115 clears, 44 programs, 48,627
triangles culled, 42.6M pixels. Its mipmap usage is **not** yet verified, so the
"textured 3D with mipmaps" wording in M-B is only half satisfied.

### 207d Two gaps the corpus found by measuring

1. **Direct-framebuffer output is never presented.** `CapturePresentedFrame`
   snapshots the **3D color surface** at flip time; `TryCopyPresentedFrame`
   reads it live. `XVideoSetMode` + `XVideoGetFB` programs (nxdk `gamma`, and
   `debugPrint` in `hello`) write the scanout buffer directly and issue no 3D
   draws and no flips, so nothing is captured. Four samples with four different
   guest RAM SHAs all exported the identical frame `18C7CC83` - the boot
   animation. The register dump confirms different buffers: `gamma`
   `surface=32A4000 pcrtc=3EB4000`, `mesh` `surface=3D00000 pcrtc=3BD4000`.
   A correct fix models the display the way xemu does (`nv2a.c:184-190`:
   `pcrtc.start / 4` plus the VGA CRTC line offset), because the guest writes a
   VRAM-aperture address that XGrid masks into its 64 MB space, and the scanout
   moves during boot. **Scoped, not attempted** - it would have been a guess.
   This is now the top corpus-driven item.
2. **`data/test-samples/nxdk-mesh` could never have built.** Its `main.c`
   `#include`s `verts.h`; the snapshot had neither `verts.h` (49 KB) nor
   `texture.h` (1.7 MB), and its README links a `screenshot.png` that was also
   absent. `data/` is gitignored, which is why nothing noticed. The corpus now
   builds from nxdk upstream, so the partial snapshots are off the build path,
   and the missing files were restored locally for anyone building by hand.

### 207e Diagnostic added

The `Frame` line now prints `pcrtc=<hex>` next to `surface=<hex>`, which is what
made 207d provable in one run instead of a RAM-diff investigation.

### 207f Follow-up: the scanout buffer was there all along

207d said the direct-framebuffer samples export the boot animation. That is
true of the *flipped* surface; the pixels themselves were never missing.
`Nv2aMmio.TryCopyScanoutFrame` (new) reads the display controller's buffer from
`PCRTC START` with the current surface geometry and pitch, and
`--frame-scanout <path>` writes it. Same 1B runs, both exports:

| sample | flipped frame | scanout | content |
|---|---|---|---|
| `gamma` | 1 colour, 0 non-black | **768 colours, 298,744 non-black** | the gradient and grey borders |
| `hello` | 1 colour, 0 non-black | **2 colours, 302 non-black** | the printed text |
| `led` | `18C7CC83` | `18C7CC83` | nothing - the correct negative control |
| `mesh` | 8,695 colours | 8,548 colours | both populated, different buffers |

So the diagnosis is confirmed and the export exists; every corpus row is now
distinguishable by `scanoutSha` (`boot-to-first-frame.ps1` records it), which is
what makes the rows evidence rather than assertions.

**Deliberately NOT made the default.** For a flipping title the two buffers
differ (`mesh` 8,695 vs 8,548 colours), so switching `--frame`/the frontend to
the scanout would change the gate frames and require a re-record. Doing it
correctly means xemu's display model (`nv2a.c:184-190`: `pcrtc.start / 4` plus
the VGA CRTC line offset), because the guest writes a VRAM-aperture address that
XGrid masks into its 64 MB space. That is the next scoped item, not a late-night
one-liner.

**Tests:** three in `Nv2aScanoutFrameTests` - the two exports read different
buffers (green vs blue), the scanout export requires both a non-zero PCRTC start
and presented geometry, and the scanout bytes match the guest's per-pixel values
(so a row/pitch mistake cannot pass). Suite 1573 -> **1576**.

### 207g Correction: the cull landing moved the GL *fingerprints*, not the software frames

Closing the corpus loop re-ran both golden harnesses, and they had never been
re-run on the post-cull build with these media:

- `gpu-slice1-golden.ps1` (dashboard): sw `4EF1FBF6` **exact**, gl
  **`6BE6325F` -> `B6116DAE`**, `diff=2602 violations=0 outsideBand=0 maxDelta=1`.
- `gpu-slice2-triangle-golden.ps1`: sw `A1740DE0` **exact**, gl
  **`3FC4B419` -> `7A860ED7`**, `diff=27849 violations=0 maxDelta=1`.

So the honest statement of 206g's evidence is: **every software frame and every
CPU SHA is byte-identical, and the GL frames stay inside the diff policy with
zero violations** - the exact GL SHA is a fingerprint, not a golden, and culling
triangles before `TryDraw` moves it. Nothing regressed; the earlier note simply
implied the GL SHAs were byte-exact too. `docs/compatibility.md` now says
"policy" next to every GL value and the measured-rows table carries the current
numbers.

Both scripts also had machine-bound firmware defaults (`wt-data\Complex_4627.bin`,
`wt-data\mcpx_1.0.bin`, `wt-data\eeprom.bin`) that do not exist on H1, so they
exited 4 with no useful message. They now default to the canonical
`data\xemu-0.8.136-windows-x86_64\` paths, like every other script.

Post-cull measured rows (1B fast, `boot-to-first-frame.ps1 -All`): triangle sw
11.7 s / 85.6 msps / `A1740DE0`, gl+replace 7.1 s / 141.5; dashboard sw 34.0 s /
29.4 / `4EF1FBF6`, gl+replace 26.5 s / 37.7; ktest 600M sw 9.0 s / 66.4, gl 7.4 s /
81.5. All six PASS, `vec=0`.

### 207h Byte-order texture formats: 3.42M samples per boot were being dropped

**How it was found.** The corpus row for `xgu vp_lighting` rendered a 3-colour
frame while issuing 2,730 draws and 3.4M depth-tested fragments. The texture
diagnostic named it in one line:

    formats= 06=11363999/0 12=10444800/0 3F=3420787/3420787

The second number is the miss count, and for `0x3F` it equalled the attempt
count: **every sample failed**, and the count matched the app's fragment total
exactly. `0x3F` is not in the 0.8.137 header at all; the newer fork defines it as
`NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8B8G8R8`, next to `0x40`
(`B8G8R8A8`) and `0x41` (`R8G8B8A8`), with swizzled twins at `0x3A`-`0x3C`.

**What they are.** The same 32-bit words as A8R8G8B8 with the channels permuted.
`A8R8G8B8` reads red from bits 16-23; `A8B8G8R8` reads it from bits 0-7, and so
on. Nothing about addressing, swizzling or filtering changes.

**The fix, three places:**

1. `TextureFormatBytesPerTexel`: `0x3A`-`0x3C`, `0x3F`-`0x41` -> 4 bytes.
2. `DecodeTextureTexel`: three new channel permutations (SZ and LU twins share
   one case each).
3. `DecodeKind`: these must map to the **generic** decode. This was the trap -
   `DecodeKind` defaults to 0, and 0 means "the A8R8G8B8 branch" in the compiled
   fills, so a format with bytes-per-texel set but DecodeKind 0 would have
   silently swapped red and blue with no miss counter to show it.

`IsLinearTextureFormat` gains `0x3F`-`0x41` (LU) but not `0x3A`-`0x3C` (SZ).

**Evidence.** Same command, before/after:

| | before | after |
|---|---|---|
| `3F` format misses | 3,420,787 | **0** |
| textured pixels | 21,808,799 | **25,229,586** |
| frame | 3 colours / 250,090 px | **1,592 colours / 307,200 px** |

Both gate frames are byte-identical after the change (`A1740DE0`, `4EF1FBF6`),
because the gates use `0x06`/`0x07`/`0x12`/`0x1E`. Twenty tests in
`Nv2aTextureByteOrderTests` pin the channel order per family, the SZ/LU twin
equivalence, that `0x3F` is *not* the A8R8G8B8 layout (the trap above), and that
genuinely unknown ids still report no texel width. Suite 1576 -> **1596**.

**Also landed:** the BRDF / DOT_RFLCT_SPEC_CONST branches now count through
`CountTexModeFallback(mode)`, so `byMode=` can name an offending stage instead
of only showing a total (both modes are `NV2A_UNIMPLEMENTED` in xemu too).

**Not landed, and why.** `xgu bump_cubemap_reflect` samples `0x3A` cube maps,
which the reference-mirroring whitelist (`0x05`/`0x06`) rejects: 896,067 mode
fallbacks, flat green shape. Extending the whitelist to the SZ variants took the
fallbacks to 0 and changed **no pixels at all** (identical frame and RAM), so the
change was reverted rather than shipped without an oracle. The remaining work is
cube direction mapping for those formats, not the decode.

### 207i The corpus got an external oracle: reference screenshots

The xgu repository ships 640x480 screenshots of each example. Comparing mean
channel values and an 8-bucket luminance histogram against XGrid's presented
frame gives the corpus something it lacked: a way to say "renders correctly"
rather than "renders something".

| sample | reference mean | XGrid mean | verdict |
|---|---|---|---|
| `vp_lighting` | (32.3, 26.7, 224.7) | (33.9, 28.1, 226.4) | matches within 1.7/255 per channel |
| `alpha_blending` | (20.8, 17.2, 233.2) | (2.2, 1.8, 253.0) | differs: XGrid is 100% in the lowest luminance bucket, the reference spreads 11% + 6% into mid buckets |
| `bump_cubemap_reflect` | (62.4, 100.2, 28.9) | (2.5, 52.3, 15.9) | differs: darker and greener, 36% black |

Two consequences. First, 207h's byte-order fix is validated end to end: after it,
`vp_lighting` reproduces the reference's colour profile almost exactly, which is
much stronger evidence than "the frame has 1,592 colours now". Second, it splits
two samples that both looked "flat-ish": `alpha_blending` is a *separate* gap
(the blended geometry is missing or black) and is now the best-scoped next
fidelity item, ahead of the cubemap work, because its background is already
correct.

`ktest` smoke re-run on the final build: **PASS 7/7**, 792,800,535 steps,
40,797-byte suite log byte-identical, so the texture change is invisible to the
standing gate.

## 208 Display model: the console shows what the display controller scans out (2026-10-21, fifteenth session) - LANDED

**Trigger.** The corpus measured this in 207d/207f: four nxdk samples with four
different guest-RAM SHAs exported the *same* frame, because `--frame` and the
Avalonia frontend present `CapturePresentedFrame`'s 3D colour surface and
`gamma`/`hello` (`XVideoGetFB`, `debugPrint`) never draw 3D and never flip. The
pixels were there (the scanout export recovered them) but the *product* path
never looked at them. 207f deliberately deferred the fix: for a flipping title
the two buffers differ, so switching needs a display model, not a buffer swap.

### 208a The VGA register file behind NV_PRMCIO

xemu forwards the NV2A's `NV_PRMCIO` MMIO window to `vga_ioport_write`, and its
display parameters come from that register file (`nv2a.c:184-190`), not from the
3D surface record. XGrid stored the window verbatim (`_prmcio`) and ignored it.
It now decodes the index/data latches the mode set uses:

- **CRTC** (ports 0x3D4/0x3D5) into `cr[256]`, with the CR0-CR7 write protection
  from CR11 bit 7 (CR7 bit 4 stays writable), exactly QEMU's rule.
- Sequencer (0x3C4/5), graphics (0x3CE/F) and the attribute index/data flip-flop
  (0x3C0), so a later read-back model has somewhere to read from. Guest reads
  still return the stored word (`_prmcio`), unchanged from before - making them
  faithful is a separate fidelity item with its own gate.

**The trap, found by a test not by reasoning.** The first implementation inferred
which port a write addressed by diffing the stored word. That is wrong: two
registers can share a data byte, so `WriteCrtc(0x11,0)` after `WriteCrtc(0x07,0)`
was silently swallowed (the index lane changed, the data lane did not). The bus
already knows the exact lanes - `Nv2AMmio.Write(address,size,value)` now applies
`size` byte ports at `offset & 3` directly. `CrtcWriteProtection...` pins it.

### 208b Geometry, and what "the displayed frame" means

`TryGetDisplayGeometry` mirrors the reference:

| field | source |
|---|---|
| base | `NV_PCRTC_START & 0x07FFFFFF` (bytes) |
| line pitch | `(CR13 \| (CR19 & 0xE0) << 3 \| (CR25 & 0x20) << 6) << 3` (xemu `nv2a_get_params`) |
| width | `(CR01 + 1) * 8` (QEMU `vga_get_resolution`) |
| height | `(CR12 \| (CR07 & 0x02) << 7 \| (CR07 & 0x40) << 3) + 1` |
| depth | CR28 bits 0-1: 2 = 16 bpp, 3 = 32 bpp (`nv2a_get_bpp`); 16 bpp is 15-bit X1R5G5B5 unless PRAMDAC general control bit 12 (ALT_MODE_SEL) selects RGB565 |

`TryCopyDisplayFrame` reads that buffer; it falls back to the flipped surface
until the guest programs a mode (early boot) and defers to a GPU backend's
readback when one is attached (never mixes software and GL pixels).
`TryCopyScanoutFrame` keeps its "raw scanout, no fallback to the flip surface"
contract and now uses the same geometry, falling back to the old surface-record
geometry when the CRTC is unprogrammed.

`--frame` **stays the last flipped 3D surface** - that is the gate anchor and the
"last completed frame". New `--frame-display` exports the display frame. The
Avalonia frontend uses `TryCopyDisplayFrame` and samples it at ~30 Hz instead of
only on `FramesPresented` changes, because a guest that never flips still has a
picture. Snapshot v4 -> **v5** (CRTC/sequencer/graphics/attribute state).

### 208c Evidence

**The decode is independent, and it agrees with the old geometry bit for bit.**
On six legs the CRTC-derived export reproduces the exact bytes the previous
presented-surface geometry produced, which is a strong cross-check because the
two paths share no numbers: `gamma` `D3EBF420`, `hello` `72D33DC2`, `led`
`18C7CC83`, `mesh` `7328A99F`, menu `51658B3D`, triangle `AB50EF68`, all 640x480,
pitch 2560, bpp 4 - and `pcrtc`/`surface` differ by exactly one page (0x12C000)
on the double-buffered legs, i.e. the scanout is the front buffer and the 3D
surface the back buffer.

**Gate currency unchanged.** 1B accurate, HDD+DVD: menu RAM `01BE5D64` / CPU
`3D2740B8` / frame `4EF1FBF6`; triangle RAM `812EA159` / CPU `2F632483` / frame
`A1740DE0` - all exact. Fast menu `51B56687` / `F5D61C94` exact. `vec=0`, no
fault on every leg. Suite 1596 -> **1605** (nine `Nv2aDisplayFrameTests`: CRTC
decode, the CR19/CR25 extended offset, exact-lane port writes, CR protection,
the display frame reading PCRTC START with the CRTC pitch, the pre-mode
fallback, the SR01 screen-off blank, 15-bit vs 565, snapshot round-trip).

### 208d Correction: the fast-mode "triangle" leg was measuring the boot animation

Fixing the display model required running the triangle in the gate's media
configuration (HDD + DVD), and the fast-mode anchors did not reproduce. The
cause is a harness bug, not a code change:

| leg (1B fast, sw) | draws | frames | pixels | RAM | CPU |
|---|---:|---:|---:|---|---|
| `--dvd` only (`raster-leg -Workload tri`) | 2,673 | 35 | 42,302,614 | `DD1D516E` | `CF79FC17` |
| `--hdd --dvd` (gate media) | 2,706 | 68 | 57,663,082 | `B45254CA` | `7DAB63EB` |

207b recorded the rule for corpus rows ("DVD-only runs never launch the disc's
XBE") but the *performance* harnesses kept the DVD-only form, so
`raster-leg.ps1 -Workload tri` and `present-path-acceptance.ps1` have been
measuring the boot animation with a triangle's name. A stashed pre-change build
reproduces the HDD+DVD pair exactly, so this session's change is not the cause.
Both scripts now use the gate media.

Corrected the position rather than the target: on H1 at 1B fast, the real
triangle is **11.3 s sw** (was "8.0") and **7.5 s `gl+replace`** (was "6.4"),
menu unchanged (`~37-54 s` depending on load, `gl+replace` unchanged). The
triangle's GL win is -34% (11.3 -> 7.5), so Track B's conclusion stands - it is
just measured on the real workload now. The fast triangle anchor becomes
`B45254CA` / `7DAB63EB` (HDD+DVD); the DVD-only pair stays recorded above as the
boot-animation anchor it always was.

### 208e Left open

- **Faithful VGA read-back** (0x3D5 returning `cr[index]`) - not done: the guest
  reads the window 302 times per 1B and the raw word is what it wrote; changing
  that is a behavior change that needs its own differential.
- **`--frame` default.** Deliberately unchanged: the flipped surface is the gate
  anchor, and the display frame is one frame behind for a flipping title. The
  product path is fixed; flipping the export default would need a deliberate
  re-record of every frame anchor (nothing in this landing requires it).
- **16-bit display modes** are modelled (CR28/ALT_MODE_SEL) but no corpus sample
  uses one, so the branch has unit coverage only.

### 208d addendum: the fast-mode triangle RAM anchor also depends on `--frame-first`

While correcting the media above, the same 1B fast triangle produced three RAM
SHAs. They are all deterministic and all explainable by the flag set:

| media | `--frame-first` | frames | RAM | CPU |
|---|---|---|---:|---|
| DVD only | no | 35 | `DD1D516E` | `CF79FC17` |
| HDD + DVD | no | 68 | `B45254CA` | `7DAB63EB` |
| HDD + DVD | yes | 68 | `0117A5E2` | `7DAB63EB` |

`--frame-first` caps the run loop's batch at 2M steps until the first flip is
saved (`Program.cs`), and batch boundaries move interrupt delivery, so guest RAM
moves while the CPU state and every counter stay identical. `raster-leg.ps1`
never passes the flag; `boot-to-first-frame.ps1` always does, which is why the
corpus rows and the anchor table disagree by construction. Rule recorded in
`docs/hosts.md`: quote the exact flag set with any fast-mode RAM SHA, and treat
CPU + counters as the portable anchors.

### 208f `alpha_blending` is not a gap - the oracle compared two animation phases

207i recorded `xgu-alpha_blending` as the best-scoped fidelity item ("blended
geometry missing or black", XGrid 100% in the lowest luminance bucket vs the
reference's 11% + 6% in mid buckets). It is not a rendering gap.

The sample ping-pongs a vertex-shader `transparency` uniform from 1.0 to 0.0 and
back (0.02 per frame, so a 100-frame cycle) and the register combiner multiplies
it into the fragment alpha (`col0 = tex0.rgba * col0.rgba`). A frame captured at
one step count therefore shows one alpha. Measured over the cycle
(`scripts/frame-oracle.py`, reference `D:\xgu\_screenshots\alpha_blending.png`):

| steps | flips | mean (R,G,B) | |dmean| vs reference |
|---|---:|---|---|
| 3.0B | 92 | (3.2, 2.6, 252.1) | (17.7, 14.6, 18.9) |
| 3.8B | 157 | (12.1, 10.0, 246.4) | (8.7, 7.3, 13.2) |
| 4.2B | 190 | (1.0, 0.8, 254.1) | (19.8, 16.4, 20.9) |
| 4.6B | 223 | (25.6, 21.2, 229.0) | **(4.8, 4.0, 4.2)** |
| 5.0B | 256 | (31.7, 26.2, 234.5) | (10.8, 8.9, 1.3) |
| reference | - | (20.8, 17.2, 233.2) | - |

At 4.6B XGrid is within 4.8/255 per channel of the reference - the same order as
`vp_lighting`'s 1.7/255 "match". The texel arithmetic checks out too: the
texture is gold (mean 235,194,130), and the 3B frame `(21,20,242)` is exactly
`alpha*src + (1-alpha)*(0,0,255)` with `alpha ~= 0.08`, i.e. mid-fade.

**Consequence for the corpus:** an animated sample needs a phase-tolerant
oracle. New `scripts/frame-oracle.py` scores several step counts against the
reference and reports the best; `scripts/frame-stat.py` prints the per-frame
mean/histogram. `bump_cubemap_reflect` stays far from its reference at every
phase tested (`|dmean|` ~60/43/12 at 3.0B/3.6B/4.2B), so its gap is real and is
now the only remaining xgu oracle failure.

### 208g Cube maps: the three gates, and why the partial path was reverted

207h tried to make `xgu bump_cubemap_reflect` (texture format 0x3A,
`SZ_A8B8G8R8`) sample by widening `IsSupportedSwizzledFormat` to the SZ twins,
saw zero pixel change, and concluded there was no oracle for cube direction
mapping. The zero-pixel result was a measurement artifact: **cube sampling is
behind three gates and 207h moved one.**

1. `IsSupportedSwizzledFormat` (the whitelist) - moved by 207h.
2. `SamplerPlan.CubeBytes` - `TrySampleTextureCube` returns false when it is 0,
   and the switch only knew 0x05/0x06. With the whitelist widened, the sampler
   still bailed at `bytes == 0`, which returns *before* the fallback counter, so
   the counters dropped to 0 while the texel stayed NONE.
3. `TryDecodeSwizzledTexel` - knew only 0x06 (A8R8G8B8) and 0x05 (R5G6B5); a
   0x3A texel returned false, i.e. NONE again.

With all three enabled together the cube is genuinely sampled
(`3A=5709991/0 fallbacks=0`) and the frame moves a long way toward the reference:
`|dmean|` (59.6, 43.3, 11.5) -> **(47.2, 14.1, 7.2)** at 3.6B, green within
14/255 and blue within 7/255. The image is still visibly wrong, though - the
reflection is striped and shifted (`wt-data/alpha/bump-fix2.png`), i.e. the
error is now the **direction/reflection chain** (the stage 3
DOT_REFLECT_SPECULAR math fed by the stage 1/2 normal-map dot products), not the
format gate.

**Not shipped.** A partially correct cube map renders garbage in place of a flat
shape, which is worse evidence than the documented gap; the change was reverted
and the frame reproduces the pre-experiment pixels byte for byte. The three-gate
list above is the durable result, and it is why the whitelist comment now warns
that widening it alone is unobservable. Next attempt: start from all three
enabled, compare the striped frame against the reference, and fix the reflection
vector (face selection, s/t orientation, and the `DOT_RFLCT_SPEC` eye vector
from texture-coord `.w`) before shipping.

### 208h Corpus widened by three xgu rows - and the fixed-function pipeline is unimplemented

Building the remaining xgu examples from `D:\xgu` (three more bootable ISOs;
the §207 recipe plus `NXDK_DIR=/d/nxdk` and `MSYSTEM=MINGW64` - do *not* source
`nxdk/bin/activate`, it ends in `exec` and replaces the shell) adds three rows:

| sample | transform mode | 3B frame | oracle |
|---|---|---|---|
| `bumpenv_ripple` | programmable | own scene from ~5B (217 flips, 9.7M depth px) | |dmean| (21.0, 19.0, 9.7) at 5B |
| `distance_fog_ffp` | **`XGU_FIXED`** | flat orange, 26 colours | |dmean| (38.7, 4.1, 16.6) |
| `spheremap_ffp` | **`XGU_FIXED`** | flat, 93k triangles culled | |dmean| (22.4, 13.7, 80.8) |

`distance_fog_ffp` and `spheremap_ffp` upload MVPs to the **fixed-function**
block (`SET_TRANSFORM_EXECUTION_MODE = FIXED`) and load no vertex program. XGrid
only executes programmable programs, so the draws are degenerate: `programLoad`
stays at the boot animation's 12, cull counts jump to 93k-112k, and the frame is
the clear colour. That is a new, large feature gap (fixed-function vertex
stage), recorded as compatibility gap 6 - it sits upstream of both renderers, so
the software rasterizer and the GL backend are equally affected.

Corpus is now **17 rows over 13 samples**; the oracle table has five xgu
entries, two of which match (`vp_lighting`, `alpha_blending` at a phase) and
three of which name real gaps (cubemap direction chain, FFP x2).

## 209 Cubemap reflection closed, and TEXFILTER became a real sampler feature (2026-10-21, sixteenth session)

**Trigger.** `xgu bump_cubemap_reflect` was the last xgu oracle failure (208f):
2 colours, a solid green shape, `|dmean|` (59.6, 43.3, 11.5) against the
reference screenshot. 208g had already split the format gate into three
(`IsSupportedSwizzledFormat`, `SamplerPlan.CubeBytes`, `TryDecodeSwizzledTexel`)
and proved that all three together sample the cube but leave a striped frame -
which it read as a broken `DOT_RFLCT_SPEC` direction chain. That reading was
wrong, and the session's probes prove it.

### 209a The fourth gate: a swizzled texture's extent is BASE_SIZE, not TEXIMAGERECT

The dominant bug was not in the direction chain at all. `TrySampleTextureCube`
took the face extent from `_textureImageRect` (the `SET_TEXTURE_IMAGE_RECT`
register). xemu takes a **swizzled** texture's extent from TEXFMT
`BASE_SIZE_U/V` and reads TEXIMAGERECT only for linear formats
(`pgraph_get_texture_shape`, `hw/xbox/nv2a/pgraph/texture.c:269-293`).

The guest never programs IMAGE_RECT for this sample's cube (the xgu helper sets
`sizes` through `xgu_set_texture_format`, not the rect), so the register held
the stale default `0x02000200` = 512x512. With a 512x512 face the sampler
walked **1 MB face strides over 256 KB faces** and sampled coordinates 0..511
inside a 256x256 face: the XGU logo stayed readable (the low coordinate bits
are still correct) but everything above them aliased into the hatch that 208g
called a broken direction chain.

Evidence that this was the whole story: fixing the extent plus the three gates
in one change moved the row from **2 colours / 36% black / `|dmean|`
(59.6, 43.3, 11.5)** to **20,843 colours / 0% black / `|dmean|` (5.3, 2.4, 1.3)**
at 3.6B - a match of the same order as `vp_lighting`'s (1.7/255), with no
change to the reflection math.

The three gates remain a real constraint, so they were widened together:

- `IsSupportedSwizzledFormat` accepts the SZ byte-order twins `0x3A`-`0x3C`
  (a cube map is always swizzled; linear cube maps are invalid in the
  reference).
- `SamplerPlan.CubeBytes` knows `0x3A`-`0x3C` are 4 bytes.
- `TryDecodeSwizzledTexel` no longer hard-codes `0x05`/`0x06`: it reads the
  texel width from `TextureFormatBytesPerTexel` and decodes through
  `DecodeTextureTexel`, so the cube and volume paths can never disagree with
  the 2D sampler about a format's channel order again. This is what makes the
  next format a one-line change instead of a three-place trap.

### 209b The direction chain was correct all along (probe evidence)

208g's "striped and shifted" frame was re-examined with a temporary
`XGRID_CUBE_DEBUG` probe (removed before commit; the findings are the durable
part):

| probe | what it rendered | result |
|---|---|---|
| `n = +Z` (smooth eye vector alone) | the cube sampled at a smooth direction field | **no speckle**, coherent environment faces |
| stage-0 texel as colour | the normal map itself | clean: readable XGU, rivets, no noise |
| `pT1.xyz` as colour | the `TEX1` varying | flat per face (the TBN rows are per-face constants) |
| `n` as colour | the dot-chain normal | smooth gradient with the XGU emboss |
| `rv` as colour | the final reflection vector | smooth gradient with the XGU emboss |

Every input and every intermediate is smooth, so the chain is not noisy. The
remaining "hatch" in a 3.6B frame is the **environment map's own content at a
grazing view** (the cube texture is an industrial room with striped walls), not
a sampling artifact. The reference screenshot was taken at a more frontal
phase.

### 209c The oracle needs a phase sweep for this sample too

The camera orbits at 0.015 rad/frame, so `bump_cubemap_reflect` is as
phase-dependent as `alpha_blending`. Measured at eight step counts
(`--gpu sw`, fast mode, HDD+DVD):

| steps (B) | 3.0 | 3.2 | 3.4 | 3.6 | 3.8 | 4.0 | 4.2 | 4.4 |
|---|---|---|---|---|---|---|---|---|
| oracle score | 106.5 | 81.5 | 47.5 | **27.5** | 66.4 | 81.7 | 69.1 | 44.9 |

3.6B is the best mean match; 4.4B is the best *structural* match (the frontal
plate that the reference shows). A single fixed step count would have reported
the row as failing. `scripts/frame-oracle.py` already implements the sweep.

### 209d TEXFILTER was stored and ignored - now it is a sampler feature (opt-in)

While instrumenting the row, the new `filters=` diagnostic showed every stage
of both textures programming **MIN/MAG 2 and convolution QUINCUNX**
(`filters=2/2/1`). XGrid stored `_textureFilter` and never read it: the sampler
is nearest-only, and the compatibility matrix lists that as a known engine
limitation. Measured usage: `bump_cubemap_reflect` samples 26M texels/3.6B
through it, and the **dashboard menu's stages 0/1 are 2/2/1** - stage 0 alone
accounts for 211M of the menu's 267M texel samples per 1B steps, i.e. most of
the product path's textured pixels.

Landing (software renderer; `--tex-filter registers`, default `legacy`):

- `SamplerPlan.Bilinear` - MIN in `{2,4,6,7}` or MAG in `{2,4}` is a tent
  filter at LOD 0 (the only LOD the software sampler implements). The four taps
  are wrapped/clamped **per tap**, exactly as GL wraps a bilinear footprint;
  BORDER axes return the border colour for an outside tap.
- `SamplerPlan.Convolution` - bits 13-15; PROJECT2D runs xemu
  `apply_convolution_filter`: nine `textureProj` taps with the 1-2-1 gaussian
  (`gaussian3x3`, `/16`), offsets one texel of the base level divided by `w`.
- Cube maps get the 2D footprint with a **face-local clamp** (GL never blends
  across cube faces).
- The compiled fills only handle the nearest shape, so
  `!plan.Bilinear && plan.Convolution == 0` was added to both their predicates;
  a filtered draw falls back to the generic loop (perf follow-up, not
  correctness).
- Counters record one sample per fragment for a bilinear tap set and one per
  tap for the convolution (so `tex ok` triples/×9s on filtered legs - that is
  the real tap count).

**Measured effect.** At 4.4B the filtered frame is visibly cleaner on the
embossed bevels and the metal frame, but the corpus metric is neutral
(`|dmean|` 5.0/10.8/5.3 → 5.2/11.1/5.4, `histL1` 23.9 → 23.9): at this
resolution the residual error is dominated by the environment content, not by
tap filtering. The honest statement is that the feature is **semantically
required** (the guest asks for it and the reference renderer does it) and
**metric-neutral on the only oracle available**, which is why it stays opt-in
until the menu has an oracle.

### 209e Validation

- **Fail-pre-fix:** 4 of the 5 new cube tests fail on the pre-change source
  (the three byte-order cases and the BASE_SIZE-vs-IMAGE_RECT case); the two
  original cube tests had encoded the IMAGE_RECT extent and were moved to
  `CubeFormat(0x06, 1, 1)`.
- **New tests:** `Draw_CubeMapSamplesByteOrderFace` (0x3A/0x3B/0x3C channel
  order through the cube path), `Draw_CubeMapExtentComesFromBaseSizeNotImageRect`,
  and five `TextureFilterRegisters_*` tests (off stays nearest; on blends
  between texel centres - pinned to 90/255, the exact four-tap average; a 1/1
  word is byte-identical to legacy; the 9-tap convolution spreads a
  single-texel impulse to its centre weight 64/255; the property defaults off).
  Suite 1605 -> **1615** after the follow-up colour guard: the existing
  compiled-modulate differential compared frames that were black because the
  harness texture's alpha (0x11) multiplied the blend source away, so
  `CompiledModulateFill_RendersTextureColors` now pins the four texture grays
  byte-exact (the harness itself was correct - V0 is white, 0x08040000 is
  `T0 * V0`).
- **Corpus:** `xgu swizzle_texture` built with the §207 recipe (add
  `$NXDK_DIR/bin` to PATH) is an 18th row / 14th sample: 2,725 draws, 45.1M
  px, 1,620 colours, `0x3A` 3.14M samples with zero misses, no oracle (the
  repo ships no screenshot). `compatibility.md` carries the row.
  The whole corpus (`-Corpus`, 1B) was then re-measured on the final build:
  every row PASS, and the six 1B anchors reproduce exactly (`gamma`
  `D3EBF420`, `gamma-fade` `2BC1C443`, `hello` `72D33DC2`, `led` `18C7CC83`,
  `mesh` `806CBEA7`/`7328A99F`, `xaudio` `8A969D86`), so the sampler changes
  did not move any boot-animation row.
- **Default path is unchanged:** fast 1B menu `51B56687…`/`F5D61C94…` exact;
  accurate 1B menu golden PASS (sw `4EF1FBF6…`, gl `B6116DAE…`,
  `violations=0 maxDelta=1`, counters 33632/686/686); accurate triangle golden
  PASS (sw `A1740DE0…`, counters 2706/68/34); BTFF triangle row
  `0117A5E2`/`7DAB63EB` (matches the documented frame-first anchors); ktest
  smoke PASS 7/7.
- The **plain** triangle leg (no `--frame-first`) reads `CB95C006…`/`B8EF1021…`
  on this host both before and after the change (verified with a stash-built
  baseline), i.e. it is a different leg from the documented `B45254CA…` anchor,
  not a regression. The anchor table should quote the flag set per row.

### 209f Measured negatives / do-not-retry

- Do not infer a VGA/texel extent from `_textureImageRect` for swizzled
  textures; it is a stale guest default for cube maps (209a).
- The `DOT_RFLCT_SPEC` chain (`n = (dot1, dot2, dot3)`, `e = pT1.w, pT2.w,
  pT3.w`, `rv = 2n(n·e)/(n·n) - e`) is faithful to xemu `psh.c:1285-1304`;
  208g's "direction chain" suspicion is disproven by the probe set in 209b.
- Bilinear taps alone do not clean up a grazing environment map; the remaining
  speckle is content + LOD 0 (no mip selection), not a tap-weight bug.
- `--gpu gl --gpu-replace` cannot compose a software-fallback draw: the
  fallback's pixels go to guest RAM and the present serves the GL FBO
  (`bump_cubemap_reflect`: `BackendUnsupported=14`, 784,684 fallback pixels,
  presented frame = the clear colour). The gate scripts assert
  `BackendUnsupported=0`, so gates never see it; do not read a GL frame of a
  partially-supported row as a rendering result.
- xemu as a frame oracle: HMP `-monitor stdio` produces nothing, `-display sdl`
  does not exist in this build, and `-drive index=1` collides with xemu's own
  drive (`-dvd_path` is the supported route); QMP stdio answers, `screendump`
  is still uncaptured (gap 9).

