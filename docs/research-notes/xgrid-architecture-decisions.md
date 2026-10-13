# XGrid — Architecture Decisions from Reference Study

Project: XGrid (name replacing the plan's working name "XemuSharp").
Sources: `docs/research-notes/{xemu,cxbx,xqemu,xisosharp,sources}.md`.
This document converts the reference study into concrete decisions for the plan's phases.

**Note — "Xermu" citations below are unverified.** An earlier draft listed a standalone C#
emulator "Xermu" (`References/Xermu-main`). It never existed in the workspace; the operator
confirmed 2026-10-13 the primary reference is `References/xemu-0.8.136`. Mentions of Xermu
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
Xemu (`References/xemu-0.8.136`) is the LLE reference for MCPX/PCI/IDE/NV2A; Cxbx-Reloaded and
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

`References/XboxWiki/51.mhtml` documents the 2BL program: MTRR setup, self-copy, PDE at 0xF000
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

- XboxWiki Boot Process (`References/XboxWiki/51.mhtml`): reset `0xFFFFFFF0`, flash
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
- xemu LLE (`References/xemu-0.8.136`): PCI all `10DE` (host `02A5`, LPC `01B2`,
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
- Cxbx (`References/Cxbx-Reloaded-master`): `types.h` KTHREAD `0x110`,
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
- Reference crypto nailed down (`References/xemu-0.8.136/hw/xbox/
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

**Correction.** `CSharp_XISOSharp` is NOT vendored under `References/`
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

Reference facts (`References/xemu-0.8.136`, `nv2a_regs.h` + `pgraph/`
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
Closes the two §78 remainders. Reference facts (`References/xemu-0.8.136`,
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
facts from `References/xemu-0.8.136/hw/xbox/nv2a/pgraph/` (`texture.c`
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
reference `References/xemu-0.8.136/hw/xbox/nv2a/pgraph/glsl/psh.c`
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
from `References/xemu-0.8.136/hw/xbox/mcpx/apu/`: `vp/vp.c` (`fe_method`
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

**xemu facts extracted** (`References/xemu-0.8.136/hw/xbox/mcpx/apu/`):
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
