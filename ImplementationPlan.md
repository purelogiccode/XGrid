# XGrid — Detailed Implementation Plan

## 0. Project Overview

**Project name:** XGrid  
**Target:** Native C#/.NET original Xbox emulator inspired by the hardware behavior and architecture of existing open-source Xbox emulators.

### Primary goal

Build a cross-platform original Xbox emulator in modern C#/.NET, with:

- C#/.NET 9 or newer
- NativeAOT-compatible core where practical
- Windows, Linux, and macOS support
- Vulkan graphics backend as the primary renderer
- Optional OpenGL backend
- Custom x86/Pentium III CPU emulation/JIT
- Xbox MCPX, NV2A, APU, IDE, USB, SMBus and related hardware
- FATX and XDVDFS support
- XBE loading
- Debugger and tracing facilities
- Automated conformance/regression tests
- No dependency on QEMU for the final emulator core

### Reference projects

The implementation uses existing projects as technical references (facts, register/format knowledge,
algorithms) rather than performing a mechanical source translation. Full extracted findings are in
`docs/research-notes/` (see the reference study deliverables below).

1. **xemu** — `References/xemu-0.8.136` — GPL-2.0 (per-file LGPL-2+/MIT/GPL-2+)
   - Primary reference for Xbox hardware behavior and mature compatibility.
   - Xbox machine/memory map, PCI IDs and IRQ routing, PIII CPUID/MSR quirks, MCPX
     (SMBus/SMC/EEPROM/flash/ACPI/ACI), NV2A (PFIFO/PGRAPH/renderer), APU, USB/XID, nvnet.

2. **Xermu** — `References/Xermu-main` — **unlicensed (all rights reserved)**
   - Primary reference for standalone Xbox emulation architecture.
   - Pentium III interpreter/JIT organization, 4 GB fastmem, device register maps, four boot
     modes (nboxkrnl, LLE kernel, LLE BIOS, HLE), NASM golden-test harness concept.

3. **Cxbx-Reloaded** — `References/Cxbx-Reloaded-master` — GPL-2.0
   - Reference for XBE format (exact struct offsets), kernel ordinal semantics, NV2A/NV097
     register and method maps, texture swizzle, OHCI/XID, XDVDFS layout.

4. **xqemu** — `References/xqemu-master` — GPL-2.0
   - Older QEMU fork; compact single-file APU register map, SMC/EEPROM defaults, TSC and
     accelerator quirks, hardware documentation pointers. No XDVDFS/FATX/XBE code.

5. **XISOSharp** — `C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp` — **MIT**
   - Pure C# XDVDFS/XISO library (read/write, CISO, XBE metadata). The only reference project
     approved for direct integration as a library dependency, where architecture allows.

**Licensing rule:** no source code is copied from xemu, Xermu, Cxbx, or xqemu (GPL/LGPL or
unlicensed). Only facts are extracted. XISOSharp (MIT) may be referenced as a dependency; preserve
its notices and the extract-xiso advertising clause if any code is adapted. Never commit
copyrighted firmware, EEPROMs, BIOS images, or game data.

### Reference study deliverables

The first project activity was a completed reference study. Its outputs are committed under
`docs/research-notes/` and must be consulted during implementation:

```text
docs/research-notes/sources.md                      licensing/provenance inventory (plan section 46)
docs/research-notes/xemu.md                         xemu hardware facts
docs/research-notes/xermu.md                        Xermu architecture and boot strategy
docs/research-notes/cxbx.md                         XBE, kernel ordinals, NV2A, USB, XDVDFS
docs/research-notes/xqemu.md                        xqemu unique artifacts
docs/research-notes/xisosharp.md                    reusable MIT library + XDVDFS format facts
docs/research-notes/xgrid-architecture-decisions.md synthesis, constants appendix, open questions
```

**Part II** at the end of this document expands every phase into a concrete, file-level task plan.

### Guiding principle

Do not attempt to translate xemu or Xermu line-by-line.

Instead:

> Reimplement the documented/reverse-engineered Xbox hardware behavior in an independently designed C# architecture.

---

# 1. Design Principles

## 1.1 Core must be independent of the UI

The emulator must not depend on WPF, Avalonia, WinForms, SDL UI code, or any frontend framework.

Required architecture:

```text
XGrid.Core
       |
       +-- CLI
       |
       +-- Avalonia frontend
       |
       +-- future WPF frontend
       |
       +-- automated test runner
```

## 1.2 Hardware emulation must be separated from host APIs

Xbox hardware code must not directly call:

- Vulkan
- OpenGL
- SDL
- Windows APIs
- Linux APIs
- macOS APIs

Use interfaces between Xbox hardware and host implementations.

Example:

```csharp
public interface IGraphicsBackend
{
    void Initialize(GraphicsConfiguration configuration);
    void BeginFrame();
    void EndFrame();
}
```

## 1.3 Correctness before optimization

Development order:

```text
Correctness
    ↓
Functional compatibility
    ↓
Profiling
    ↓
Optimization
    ↓
JIT
    ↓
Advanced optimization
```

Do not optimize unmeasured code.

## 1.4 Every hardware component needs tests

Each major device should have:

- unit tests
- register tests
- state transition tests
- integration tests
- regression tests

## 1.5 Build the debugger early

The debugger should not be an afterthought.

CPU state, memory inspection, breakpoints, traces, and device registers should be available before commercial games work.

---

# 2. Technology Stack

## 2.1 Required

- C#
- .NET 9 initially
- SDK-style projects
- nullable reference types
- implicit usings where appropriate
- analyzers
- xUnit or NUnit
- Git
- GitHub Actions

## 2.2 Recommended

- .NET 9/10 as the runtime evolves
- Avalonia for the desktop frontend
- Vulkan as primary graphics backend
- Silk.NET or another maintained C# Vulkan binding
- System.Numerics
- System.Runtime.Intrinsics
- System.Runtime.CompilerServices.Unsafe
- `Span<T>`
- `MemoryMarshal`
- function pointers
- NativeAOT for supported components

## 2.3 Avoid in the core

Avoid making the emulator dependent on:

- QEMU
- native xemu libraries
- proprietary Xbox SDKs
- Windows-only APIs
- WPF
- SDL-only APIs
- external emulation processes

External/native libraries can be used as optional host backends where justified.

---

# 3. Repository Structure

```text
XGrid/
|
+-- XGrid.sln
|
+-- Directory.Build.props
+-- Directory.Build.targets
+-- Directory.Packages.props
+-- global.json
+-- README.md
+-- LICENSE
+-- CONTRIBUTING.md
+-- SECURITY.md
|
+-- docs/
|   +-- architecture/
|   +-- cpu.md
|   +-- memory.md
|   +-- nv2a.md
|   +-- mcpx.md
|   +-- storage.md
|   +-- audio.md
|   +-- input.md
|   +-- boot.md
|   +-- debugging.md
|   +-- performance.md
|   +-- compatibility.md
|   +-- research-notes/
|
+-- src/
|   +-- XGrid.Core/
|   +-- XGrid.Cpu/
|   +-- XGrid.Memory/
|   +-- XGrid.Bus/
|   +-- XGrid.Mcpx/
|   +-- XGrid.Nv2a/
|   +-- XGrid.Audio/
|   +-- XGrid.Storage/
|   +-- XGrid.XboxFs/
|   +-- XGrid.Xbe/
|   +-- XGrid.Input/
|   +-- XGrid.Graphics/
|   +-- XGrid.Debug/
|   +-- XGrid.Host/
|   +-- XGrid.Frontend.Avalonia/
|   +-- XGrid.Cli/
|
+-- tests/
|   +-- XGrid.Core.Tests/
|   +-- XGrid.Cpu.Tests/
|   +-- XGrid.Memory.Tests/
|   +-- XGrid.Bus.Tests/
|   +-- XGrid.Mcpx.Tests/
|   +-- XGrid.Nv2a.Tests/
|   +-- XGrid.Audio.Tests/
|   +-- XGrid.Storage.Tests/
|   +-- XGrid.XboxFs.Tests/
|   +-- XGrid.Xbe.Tests/
|   +-- XGrid.Integration.Tests/
|   +-- XGrid.Compatibility.Tests/
|   +-- XGrid.Performance.Tests/
|
+-- tools/
|   +-- XGrid.CpuTestRunner/
|   +-- XGrid.XbeTool/
|   +-- XGrid.MemoryTool/
|   +-- XGrid.TraceTool/
|   +-- XGrid.Nv2aTool/
|
+-- benchmarks/
|   +-- CpuBenchmarks/
|   +-- MemoryBenchmarks/
|   +-- XdvdfsBenchmarks/
|   +-- Nv2aBenchmarks/
|   +-- EndToEndBenchmarks/
|
+-- test-data/
|   +-- cpu/
|   +-- xbe/
|   +-- xdvdfs/
|   +-- fatx/
|   +-- nv2a/
|   +-- audio/
|   +-- integration/
|
+-- scripts/
    +-- build.ps1
    +-- build.sh
    +-- test.ps1
    +-- test.sh
    +-- benchmark.ps1
    +-- format.ps1
```

---

# 4. Dependency Graph

The implementation should follow this dependency direction:

```text
                    Frontend
                       |
                      Host
                       |
                  Emulator Core
                       |
        +--------------+--------------+
        |              |              |
       CPU           Memory          Bus
        |              |              |
        +--------------+--------------+
                       |
          +------------+------------+
          |            |            |
        MCPX          NV2A         APU
          |            |            |
          +------------+------------+
                       |
                    Storage
                       |
               FATX / XDVDFS / XBE
```

Lower layers must never depend on higher layers.

---

# 5. Phase 0 — Project Bootstrap

## Objectives

Create a compiling solution and establish engineering standards.

## Tasks

- Create solution.
- Create all major projects.
- Enable nullable.
- Enable warnings.
- Add analyzers.
- Configure formatting.
- Add CI.
- Add unit test project.
- Add benchmark project.
- Establish logging.
- Establish versioning.
- Establish documentation structure.

## Acceptance criteria

```text
dotnet build
```

must succeed on supported platforms.

```text
dotnet test
```

must succeed with zero tests failing.

CI must execute:

- build
- test
- formatting validation
- analyzers

---

# 6. Phase 1 — Emulator Core

## 6.1 Emulator lifecycle

Create:

```csharp
public interface IEmulator
{
    EmulatorState State { get; }

    void Initialize();
    void Reset();
    void Start();
    void Pause();
    void Resume();
    void Stop();
}
```

## 6.2 Clock

Implement:

- emulated cycle accounting
- device scheduling
- deterministic test mode
- host-time mode

The first version should favor determinism.

## 6.3 Event scheduler

Create:

```csharp
public interface IEmulatedDevice
{
    void Reset();
    void Tick(ulong cycles);
}
```

Later replace coarse ticking with event-driven scheduling where useful.

## Acceptance criteria

- Emulator can initialize and reset deterministically.
- A test can execute a fixed number of cycles and obtain identical state.
- No frontend dependency exists.

---

# 7. Phase 2 — Memory System

## 7.1 Physical memory

Implement Xbox RAM representation.

Required operations:

```text
Read8
Read16
Read32
Read64
Write8
Write16
Write32
Write64
```

Also provide span access:

```csharp
Span<byte> GetSpan(uint address, int length);
```

## 7.2 Endianness

Create explicit little-endian helpers.

Never depend on accidental host behavior.

## 7.3 Memory map

Implement:

```text
Physical RAM
ROM/flash
MMIO
PCI regions
GPU memory
device windows
```

## 7.4 MMIO

Create:

```csharp
public interface IMmioDevice
{
    uint Read(uint address, int size);
    void Write(uint address, int size, uint value);
}
```

## 7.5 Fast memory path

Implement:

```text
Address
  ↓
range check
  ↓
RAM direct access
```

for normal RAM.

MMIO must take the slower device path.

## Acceptance criteria

- Memory read/write tests pass.
- MMIO routing works.
- RAM performance benchmark exists.
- Invalid addresses produce deterministic behavior.

---

# 8. Phase 3 — x86/Pentium III CPU

This is one of the highest-risk components.

## 8.1 CPU state

Implement:

```text
EAX
EBX
ECX
EDX
ESI
EDI
EBP
ESP
EIP
EFLAGS
CS
DS
ES
FS
GS
SS
CR0
CR2
CR3
CR4
DR0-DR7
```

and relevant descriptor/control state.

## 8.2 Decoder

Create:

```text
Prefix decoder
Opcode decoder
ModRM decoder
SIB decoder
Immediate decoder
Address-size handling
Operand-size handling
```

## 8.3 Instruction representation

Create an internal representation:

```csharp
public readonly struct DecodedInstruction
{
    public Opcode Opcode { get; init; }
    public Operand Operand1 { get; init; }
    public Operand Operand2 { get; init; }
    public Operand Operand3 { get; init; }
    public int Length { get; init; }
}
```

## 8.4 Interpreter

Implement instruction families in this order:

> **Status:** Tiers 1–10 are implemented and covered by tests (integer/logic, control flow,
> shifts/rotates, MUL/IMUL/DIV/IDIV, string ops with REP/REPE/REPNE, system instructions
> including CPUID/RDTSC/RDMSR/WRMSR/SETcc/CMOVcc, MOV CR/DR, LGDT/LIDT, INVLPG, x87/FPU,
> MMX, and SSE1). x87 arithmetic is double-rounded; FWAIT/#MF and SSE2 are intentionally absent.

### Tier 1

- NOP
- MOV
- PUSH
- POP
- XCHG
- LEA

### Tier 2

- ADD
- ADC
- SUB
- SBB
- INC
- DEC
- CMP
- TEST
- AND
- OR
- XOR
- NOT
- NEG

### Tier 3

- JMP
- CALL
- RET
- Jcc
- LOOP
- INT
- IRET

### Tier 4

- SHL
- SHR
- SAR
- ROL
- ROR
- RCL
- RCR

### Tier 5

- MUL
- IMUL
- DIV
- IDIV

### Tier 6

- string instructions

### Tier 7

- system instructions

### Tier 8

- x87/FPU

### Tier 9

- MMX

### Tier 10

- SSE

## 8.5 Flags

Implement exact behavior for:

```text
CF
PF
AF
ZF
SF
OF
DF
IF
TF
```

Avoid recomputing every flag eagerly if profiling later proves lazy flags worthwhile.

## 8.6 Exceptions

Implement:

- divide error
- invalid opcode
- general protection
- page fault
- breakpoint
- debug exception
- invalid TSS where needed
- device-not-available where needed

## 8.7 CPU test framework

Create a test format:

```json
{
  "name": "ADD_EAX_EBX",
  "registers": {
    "EAX": "0x00000005",
    "EBX": "0x00000007"
  },
  "code": "01D8",
  "expected": {
    "EAX": "0x0000000C"
  }
}
```

## Acceptance criteria

The interpreter must pass an extensive instruction-level regression suite before Xbox boot development proceeds.

---

# 9. Phase 4 — x86 MMU

Implement:

- segmentation where required
- paging
- page directory
- page tables
- CR0
- CR3
- CR4
- TLB
- page faults
- access permissions
- user/supervisor semantics where required

## Optimization plan

Start with correctness.

Then:

```text
Virtual address
     ↓
TLB hit
     ↓
physical address
     ↓
RAM
```

Only on TLB miss:

```text
page table walk
```

## Acceptance criteria

- Synthetic page-table tests pass.
- Page faults are correct.
- TLB invalidation is tested.
- CPU can execute code through virtual addresses.

---

# 10. Phase 5 — Xbox Bus and Interrupt Infrastructure

Implement:

- PCI bus
- device registration
- MMIO regions
- interrupt lines
- PIC-like behavior
- IOAPIC behavior
- timers
- DMA infrastructure

Create:

```csharp
public interface IXboxDevice
{
    string Name { get; }
    void Reset();
}
```

## Acceptance criteria

Devices can register themselves without modifying central switch statements.

---

# 11. Phase 6 — MCPX [PARTIAL]

Implement in stages:

1. SMBus
2. EEPROM
3. Flash
4. SMC-related functionality
5. USB host integration
6. interrupts
7. timers
8. remaining MCPX functions required by boot

Architecture:

```text
MCPX
 |
 +-- SMBus
 +-- EEPROM
 +-- Flash
 +-- SMC
 +-- USB
 +-- Interrupts
 +-- Timers
```

## Acceptance criteria

The Xbox boot firmware can initialize MCPX-dependent functionality.

---

# 12. Phase 7 — Firmware and Boot [PARTIAL]

Implement configuration for user-provided firmware files.

Required conceptual inputs:

```text
MCPX boot ROM
Xbox BIOS
Hard disk image
```

Do not distribute copyrighted firmware.

## Boot pipeline

```text
Reset
 ↓
CPU reset state
 ↓
MCPX
 ↓
BIOS mapping
 ↓
BIOS execution
 ↓
hardware initialization
 ↓
storage initialization
 ↓
kernel loading
```

## Debugging requirements

At every stage log:

- EIP
- CR0
- CR3
- EFLAGS
- interrupts
- MMIO accesses
- PCI accesses
- page faults

## Milestone

### M1 — BIOS execution

Success means the C# emulator executes real Xbox firmware far enough to demonstrate correct CPU/memory/basic hardware behavior.

---

# 13. Phase 8 — Storage

## 13.1 ATA/IDE

Implement:

```text
IDE controller
ATA command handling
PIO
DMA where required
device registers
status/error registers
interrupts
```

## 13.2 Hard disk

Support a virtual disk backed by a file.

Avoid loading the entire image into memory.

Use:

```text
random-access file
block cache
read-ahead
write-back where required
```

## 13.3 DVD

Implement a virtual DVD device.

## 13.4 FATX

Implement:

- partition parsing
- boot sector
- FAT
- directories
- files
- timestamps/metadata as needed
- allocation
- read-only mode initially

## 13.5 XDVDFS

Integrate the existing C# XDVDFS implementation where licensing and architecture allow.

Support:

```text
ISO
XISO
raw XDVDFS
```

Test media on this machine (verified 2026-10-11; local paths, not in repo):

```text
H:\Tests\XBOXTest      37 Redump retail XISO .iso files (~263 GB: NTSC-U/Europe/Japan/Australia)
E:\Microsoft Xbox      945 USA retail .chd files (~1.44 TB) + 1403 .dummy placeholders
```

Libraries for them (both pure C#, zero native deps, target
net8.0/net9.0/net10.0 - compatible with this repo's net9.0): NuGet
`XISOSharp` 1.4.2 (purelogiccode's C# extract-xiso implementation;
sibling repo `CSharp_XISOSharp` holds docs + source) for ISO/XISO -
**referenced since §64** (`XGrid.XboxFs/XdvdfsFileSystem` + `xgrid
xdvdfs ls/cat`; 007 validated: DVD_X2, TitleId `0x4541000D`), and NuGet
`CHDSharp` 1.4.3 (pure C# MAME CHD V1-V5 reader, all codecs) for CHD -
**referenced since §66** (`XGrid.Storage/ChdDvdImage`, `boot --dvd`
accepts `.chd` by magic; 007 USA validated, SHA-identical XBE).

## Milestone

### M2 — Xbox filesystem access

The emulator can access a real Xbox filesystem through emulated storage.

---

# 14. Phase 9 — XBE Loader

Implement:

- XBE header parser
- sections
- entry point
- image base
- certificates
- TLS information
- imports/exports as needed
- section mapping
- relocation behavior where applicable
- debug information when present

Create:

```csharp
public sealed class XbeImage
{
    public uint EntryPoint { get; }
    public uint BaseAddress { get; }
    public IReadOnlyList<XbeSection> Sections { get; }
}
```

Create a standalone tool:

```text
XGrid.XbeTool
```

Commands:

```text
xbe info game.xbe
xbe sections game.xbe
xbe imports game.xbe
xbe extract game.xbe
```

## Milestone

### M3 — XBE loading

An XBE can be parsed and mapped into emulated memory.

---

# 15. Phase 10 — Xbox Kernel Interface

This area must be implemented incrementally.

Start with the minimum services required to boot known test software.

Potential categories:

```text
memory management
threads
events
IRQs
filesystem
object manager
timers
device I/O
Xbox kernel exports
```

Do not implement the entire kernel API upfront.

Use tracing to discover missing functionality.

## Strategy

```text
Run
 ↓
unimplemented kernel call
 ↓
trace
 ↓
implement
 ↓
regression test
```

This is preferable to speculative implementation.

---

# 16. Phase 11 — NV2A

This is the second major high-risk subsystem.

## 16.1 Device hierarchy

```text
NV2A
 |
 +-- PBUS
 +-- PFIFO
 +-- PGRAPH
 +-- PFB
 +-- PCRTC
 +-- PVIDEO
 +-- PRAMDAC
 +-- memory interface
```

## 16.2 PFIFO

Implement:

- channels
- push buffers
- DMA objects
- method dispatch
- command parsing

## 16.3 PGRAPH

Implement:

- graphics state
- register state
- surfaces
- textures
- vertex state
- render state
- shader state
- primitive submission

## 16.4 Memory

Handle:

```text
GPU virtual/physical memory
surfaces
textures
framebuffers
depth buffers
```

## 16.5 Renderer abstraction

Do not expose Vulkan types to NV2A emulation.

Use an intermediate representation:

```csharp
public interface INv2aRenderer
{
    void SetState(Nv2aState state);
    void UploadTexture(Nv2aTexture texture);
    void Draw(Nv2aDrawCommand command);
}
```

## 16.6 Vulkan backend

Primary target.

Implement:

- device creation
- swapchain
- command buffers
- descriptor management
- textures
- render targets
- pipelines
- synchronization

## 16.7 OpenGL backend

Optional after Vulkan reaches stable rendering.

## Milestones

### M4 — Framebuffer output [DONE 2026-10-09]

A software rasterizer renders PGRAPH inline-vertex triangles (Gouraud fill,
scissor, A8R8G8B8/R5G6B5) through the full pushbuffer path; the dashboard's
boot triangle extracts as a black→red→green gradient (307K pixels, viewed).

### M5 — 2D graphics [DONE 2026-10-10]

Transform-program interpreter (`VshProgram`, §38), transform
program/constant storage with load-pointer semantics, inline element/array
semantics, vertex attribute fetch (all formats) and the element draw path
through the program to the shared barycentric rasterizer (§39). Ran on the
real dashboard without fault (2670 draws); §42 proved all 10.1M pixels
come from legacy inline draws while program draws are degenerate at this
boot stage — steady-state program draws later validated live (§§57–58:
real shaders execute, zero pixels proven guest-side clip).

### M6 — basic 3D [DONE 2026-10-10]

Per-slot texture state, swizzled (Morton via xemu's mask algorithm) and linear
A8R8G8B8/R5G6B5 sampling with wrap/mirror/clamp addressing, modulate by `oD0`,
texcoords from `oT0` (§40). All 10.1M dashboard pixels sample; §44 later
showed the textures are CPU-uploaded assets, dark by nature (only
`0x3628000` is a true render target).

### M7 — shaders [DONE 2026-10-10]

Fragment pipeline (`ProcessFragment`, §41): alpha test, stencil test with
fail/zfail/zpass ops, depth test/write (Z16/Z24S8, depth mask), full blend
factors/equations with `BLEND_COLOR`, color mask, nop-draw skip. 7 unit tests
with exact pixel/dword expectations. Validation found no live fragments at
this boot stage (`blended=0 depth=0` — the 66 draws using these paths execute
programs that read never-loaded zero constants, degenerate on xemu too, §42;
steady-state programs later validated live in §§57–58).

### M8 — game rendering [DONE 2026-10-10]

Flip tracking + present path (§43): `FLIP_INCREMENT_WRITE` rotates the write
buffer, counts a presented frame and captures the at-flip surface;
`FLIP_STALL` counting; vblank read-advance; `XGrid.Graphics/DisplayFrame`
(BGRA store, 32-bit BMP export); CLI `--frame` saves the presented frame.
Validated: `frames=33 stalls=33` (exact oracle match), 640×480 from
`0x32A4000`, opaque black — the flip chain presents the dashboard surface.

### M9 — Dashboard boot (target: same dashboard xemu boots from `xbox_hdd.qcow2`)

Status 2026-10-11: boot through **interactive menu** is SOLVED
end-to-end (5B steps, no fault): Gate 1, animation (§32), ACI/APU
(§33), fcomp (§34), M5–M8 (§37–43), content-discovery (§45),
file-save (§47–48), shadow alias (§50–51), NIC gates I+II (§52–53),
XID gates I–III (§54–56), D3D probe (§57), oPos/C0/zeta/viewport
verdicts (§§58–61), the NV2A fidelity wave (§71) and — the missing
piece — the **USB daughterboard hub chain** (§69): the guest's XPP
stack now opens the pad and polls `GetState` every frame, D-pad
levels move the menu selection (`obj+0x270` 1→5), and a step-timed
A edge (`--pad-step`) fires the activation writer `0x1859D`
(state 14 -> handler `0x1E66A`; §§69–70). The Xbox PIT frequency is
fixed to 1,125,001 (§68). Definition of done: the real kernel (LLE,
no HLE) loads the dashboard XBE off the HDD (DONE §31), the dashboard
renders its menu through our NV2A path (OPEN — state-0 menu is
black; visible UI still unproven), and controller input navigates it
(DONE at the plumbing level — selection moves, activation fires;
scripted `--pad-script` injection merged §74, control list decoded
§76). Probes EXECUTED (§79): submenu state-100 is state-proven
(`0x1E420`@972M, `FC0→0x5E4560`, no load) but pixel-negative
(black-identical thru 1.6B) — next is the `--pgraph` method-stream
diff (submenu vs neutral) to decide guest-side clip vs rasterizer
gap; idx0 XBMC is D-pad-unreachable (clamps {1..5}); DVD auto-boots
past the menu to the EA splash (mechanism TBD).
007: limiter exited via temp `[AFE34]=2` (reverted); game parks in a
SECOND wait (parked STRM `remaining=0 state=2`, zero writes 1.8B→3B;
exit needs `MPCh` magic or `0x2A830` — §85 suspects user-mode
completion delivery, APC/producer wakeup);
nvnet P0+P1 MERGED (§84: ring latches, IRQ4, TX walker + loopback).
DVD auto-launch TRACED (§83): handoff step 537,926,130 via
`call [0x10128]` (same gate as §34) → game entry `0x22E35`;
file-path decision (`D:\default.xbe` vs `C:\` fallback).
Next: viewport-bake audit (surface/zeta/caps at SetViewport time —
§82 proves the submenu black is guest-accurate clip, not a rasterizer
gap) + 0x2AA65 windowed break-log + thread/APC observability, then
nvnet P2/P3. Order matters; do not work on later
gaps before earlier gates pass.

Gap 1 — kernel→storage handoff [DONE 2026-10-09]. The kernel loads the
dashboard: guest sequence matches the xemu oracle through HDD unlock
(`MEF MEF MEC MF2 MEC M91`), the full DVD probe, then 27 `READ DMA (0xC8)`
commands (~828 KB); RAM holds the real EvoX XBE (`XBEH` at phys `0xBF000`,
loader `CMP [ESI],"XBEH"`, cert string). Engine: 8/16/32-bit data port,
dual-drive taskfile, ATAPI protocol details, synchronous BM-DMA engine
(BAR4/PRDT/IRQ). See research-notes §31.

Gap 1b — boot-animation wait [DONE 2026-10-10]. The `0x800426D8` park was the
animation/loading-screen frame pacing waiting on the NV2A notifies page; the
kernel's `NV097_BACK_END_WRITE_SEMAPHORE_RELEASE` (0x1D70) was never written
because the semaphore DMA context is a RAMHT handle, and PGRAPH notifications
(`NV097_NO_OPERATION`) were not raised. Both implemented (semaphore via RAMHT
resolution; notification INTR/NSOURCE/TRAPPED_*; `UpdateIrq` matches xemu's
`pending && enabled`); the loader now runs the full animation and proceeds to
video mode set. See research-notes §32.

Gap 1c — AV-device bring-up [DONE 2026-10-10]. Two more missing MCPX devices
blocked the video-device open after the animation: the **ACI (AC'97) at
0xFEC00000** (the kernel polls CAS bit 0 with a 1000×`StallExecution` timeout;
unmapped all-ones → E_FAIL → NULL video device → BugCheck 0x1E) and the **APU
at 0xFE800000** (a voice-off completion — `SE2FE_IDLE_VOICE` FE message — must
arrive on a VP frame boundary, not synchronously, or the stop method
deadlocks on its own operation counter). `McpxAciDevice` + `McpxApuDevice`
implement both register models. See research-notes §33.

Gap 1d — animation + XBE launch [DONE 2026-10-10]. The remaining NULL D3D
surface crash (`[AV+0x64C]` released by the AV teardown `0x8005BACE` →
`0x800409F5`) was **not** an NV2A gap but a **CPU bug**: `fcomp`/`fcom` memory
operands (`D8`/`DC` forms, decoded as `(ST0, memory)`) were executed comparing
`Operand1` (=ST0 itself), so every `fcomp mem` returned "equal". The
intro-animation tick loop (`0x800579B8` → `0x80057949` → `0x800571AD`) exited
immediately, the animation never ran, and the teardown released the
never-created surface. Fix: compare `Operand2` when present (XboxCpu.cs;
regression tests in `FpuTests`). Now the animation ticks (32 scene updates,
137.4M→488.9M), the teardown no longer faults, and `xgrid boot --steps
700000000` completes **all 700M steps with no fault**. **The dashboard XBE
entry executes**: the header entry field (VA `base+0x128` = `0x10128`) is an
absolute VA (`0x12A17`), XOR-encrypted in the file; the kernel decrypts it
(`xor [0x10128],0xA8FC57AB` at `0x8002EFE6`) and calls it (`call [0x10128]` at
`0x800158DA`); the entry runs at phys `0x5CA17`, resolves its kernel thunks
(`[0x12C4B]=0x8001EEAC`) and starts its init (bit-stream decompressor at
`0x5C4A0`). See research-notes §34. (Init long completed: the dashboard went
on to render (§37–43), discover content (§45) and save files (§46).)

Gap 2 — exact dashboard identity [DONE 2026-10-09]. Both sides agree: the host
FATX track identified softmod `xboxdash.xbe` (770048 B, `EvoX`, entry
`0x12A17`) at partition `0x465400`, and Gate 1's guest RAM forensics proved the
kernel itself DMA-loads that exact image (`XBEH` at phys `0xBF000`, loader
`CMP [ESI],"XBEH"` at `0x2EE98`, `EvoX` cert string). "Same dashboard" claim
is now evidence-backed; no further confirmation needed.

Gap 3 — CPU throughput. Dashboard boot + menu needs billions of guest
instructions; 5B-step batch runs take ~6 min (~14M insn/s Release), so
forensics runs are practical, but interactive rates need JIT (Phase
15) or a much faster interpreter first, benchmark-driven (Phase 20/29).

Gap 4 — NV2A 3D depth (M5–M8) [M5–M8 DONE; M9 visible render OPEN]. M5
(programmable vertex pipeline), M6 (textures), M7 (fragment pipeline)
and M8 (present) are done (§37–43); the §71 fidelity wave closed the M5
gap (inline `SET_VERTEX_DATA*` + `DRAW_ARRAYS`), implemented all 11
XF-context constant writers (c56–c59, c64–95 — correcting §42's wrong
"no-ops on xemu" claim; xemu feeds c58/c59 from the viewport methods),
fixed stride-0/bit31/program-start/output-defaults, and aligned the
interpreter with xemu's GLSL (MUL zero rule, min/max/LIT, paired-ILU,
oPos epilogue) plus perspective-correct varyings and oD0 NaN→1. §57–58
proved live program draws execute real shaders (`oPos=V0*C0+C0`) and
clip guest-side; the state-0 menu is black, so *visible* menu
rasterization is still unproven. **Register combiner (psh) merged
(§75):** real combiner methods (ALPHA_ICW `0x260`, SPEC_FOG_CW
`0x288/0x28C`, FACTOR `0xA60/0xA80`, ALPHA_OCW `0xAA0`, COLOR_ICW
`0xAC0`, SPEC_FOG_FACTOR `0x1E20`, COLOR_OCW `0x1E40`, CONTROL
`0x1E60`, SHADER_STAGE_PROGRAM `0x1E70`, DOT_RGBMAPPING `0x1E74`,
OTHER_STAGE_INPUT `0x1E78` — correcting the brief's 0x1940 range,
which is inline vertex data), stage/final evaluation with
combiner-disabled draws pixel-identical. **Fog + dependent/bump/dot
modes done (§78, wave/nv2a-fog-tex):** SET_FOG_COLOR/MODE/GEN/ENABLE/
PARAMS, oFog varying with fog_mask order, per-vertex LINEAR/EXP/EXP2
(+ABS) factor into the FOG register (disabled stays `(0,0,0,1)`);
DPNDNT_AR/GB, DOTPRODUCT/DOT_ST/DOT_ZW/DOT_STR_3D, BUMPENVMAP[_LUM]
with dot-map/sign decode and bump matrix/scale/offset; unknown modes
degrade to NONE with `TexModeFallbacks`. Remaining: fixed-function
foggen, cubemap/reflection/project3d/clipplane/BRDF modes, HILO dot
maps, oPos.z clipRange normalization, then a menu screen that
actually draws. **§82 verdict:** the submenu lever is guest-accurate
black (real draws, all vertex-clipped on poisoned C0) — no rasterizer
gap. Fault is upstream in the viewport bake: audit surface/zeta/caps
responses at SetViewport-bake time (driver emits Z=16.7M from sane
RAM MinZ=0/MaxZ=1).

Gap 5 — USB/OHCI + controller input (Phase 13) [DONE 2026-10-11]. The
OHCI host controller (`OhciUsbController`, §36/§54) plus the
xemu-faithful USB hub chain (§69: root port 1 -> 4-port daughterboard
hub -> 1.3 -> 3-port controller hub -> 1.3.1 -> XID; RHSC/done-head/
UsbReset fixes) made the guest's XPP stack go live: it opens the pad
(`0x79E99`/`0x76A1F`/`0x76A81` @947–949M) and polls `GetState`
(`0x76C59`) every frame (§69); D-pad levels move the menu selection
(`obj+0x270` 1→5) and a step-timed A edge (`--pad-step`) fires the
activation writer `0x1859D` (§70; A-press now differs from neutral by
23,786 RAM bytes). Scripted `--pad-script` navigate+activate sequences
merged (§74). **007 input verdict (§77): never-reaches, not mismatch**
— no `XInputOpen`/`GetState`, no device fix; unblock its `0x20E40`
limiter first. **007 second wait (§85):** temp `[AFE34]=2` forcing exited the
limiter (reverted); the game parks on a STRM (`remaining=0 state=2`,
zero writes 1.8B→3B) whose async completion never arrives — suspect
user-mode completion delivery (APC/producer wakeup), not DVD
serving; next is thread/APC observability + windowed break-log on
`0x2AA65`. **nvnet P0+P1 MERGED (§84):** ring latches, RESET wrap,
IDLE/KICK, IRQ STATUS/MASK + W1C → IRQ4, TX walker + coalesce +
VALID-clear writeback, loopback backend seam, CLI counters (956
tests). **DVD auto-launch (§83):** `call [0x10128]` handoff at step
537,926,130 → entry `0x22E35`; file-path decision; `0xAD` requested
once (§66 correction); trace/break worlds diverge ~100M steps
(chunk-sensitive Advance — use break-world steps). Remaining:
memory-unit/storage class USB devices + nvnet P2 RX / P3 backend.

Gap 6 — APU depth (Phase 12). The register model is implemented
(`McpxApuDevice`, §33) and the front-end handshake/voice-off flow works; DSP,
mixing and real audio output are pending. Non-blocking for boot; dashboard
sounds come later.

---

# 17. Phase 12 — APU

Implement:

1. device registers
2. voices
3. DMA/audio buffers
4. mixing
5. DSP behavior
6. resampling
7. host output

Architecture:

```text
Xbox APU
   ↓
voice processing
   ↓
mixer
   ↓
IAudioBackend
```

Host backend should support:

- Windows
- Linux
- macOS

## Acceptance criteria

- Audio initialization works.
- Known audio test produces expected waveform.
- Long-running audio does not drift excessively.

---

# 18. Phase 13 — USB and Controllers

Implement:

- OHCI host behavior where required
- USB device model
- Xbox controller protocol
- controller state
- rumble

Architecture:

```text
HostInput
   ↓
XboxControllerDevice
   ↓
USB
   ↓
MCPX
```

Support four controllers.

---

# 19. Phase 14 — Networking

Implement only after core gameplay works.

Potential architecture:

```text
Xbox network device
       ↓
emulated NIC
       ↓
INetworkBackend
       ↓
host socket/network API
```

Start with the minimum required for games.

---

# 20. Phase 15 — CPU JIT

This should begin only after the interpreter is stable.

## 20.1 JIT architecture

```text
x86 bytes
   ↓
decoder
   ↓
basic block
   ↓
IR
   ↓
optimizer
   ↓
host code emitter
   ↓
code cache
```

## 20.2 Intermediate representation

Example:

```text
LOAD r1, [EBX]
ADD r1, ECX
SHL r1, 2
STORE [EDX], r1
```

## 20.3 Basic blocks

Compile until:

- branch
- call
- return
- indirect jump
- exception
- page boundary where required

## 20.4 Block cache

Key by:

```text
guest virtual address
CPU mode
relevant execution state
```

Invalidate blocks when guest code changes.

## 20.5 Direct host execution

Investigate later:

- executable memory
- machine-code emitter
- host register allocation
- trampolines
- direct memory mapping

These techniques may require unsafe/native platform support.

## 20.6 Managed JIT options

Evaluate in this order:

1. Expression/IL-based compilation
2. `DynamicMethod`
3. `System.Reflection.Emit`
4. custom machine-code emitter
5. NativeAOT-assisted infrastructure
6. platform-specific executable memory

The final choice must be benchmark-driven.

---

# 21. Phase 16 — CPU Optimization

Optimize in this order:

1. instruction decode
2. memory access
3. branch handling
4. flags
5. block cache
6. register allocation
7. indirect branches
8. MMIO checks
9. TLB
10. synchronization

Avoid optimizing the interpreter after JIT performance becomes the primary path.

---

# 22. Phase 17 — Memory Optimization

Potential techniques:

- direct RAM array
- unsafe references
- spans
- `MemoryMarshal`
- fixed-size pages
- TLB cache
- read/write helpers
- branch reduction
- host virtual-memory mapping where justified

Benchmark every change.

---

# 23. Phase 18 — GPU Optimization

Measure:

```text
CPU → GPU command generation
GPU submission
shader compilation
texture upload
render pass creation
synchronization
frame pacing
```

Avoid excessive GPU/CPU synchronization.

Use:

- persistent resources
- descriptor caching
- pipeline caching
- texture caching
- command-buffer reuse
- asynchronous resource upload where appropriate

---

# 24. Phase 19 — Debugger

Create an integrated debugger.

## CPU

- registers
- flags
- instruction pointer
- disassembly
- step
- step over
- continue
- breakpoints
- watchpoints

## Memory

- hex viewer
- search
- read/write
- mapped regions

## GPU

- NV2A registers
- PGRAPH state
- texture inspection
- framebuffer inspection
- push-buffer trace

## Storage

- FATX browser
- XDVDFS browser
- mounted image information

## Trace

Support:

```text
CPU trace
MMIO trace
PCI trace
NV2A trace
kernel trace
filesystem trace
```

---

# 25. Phase 20 — Automated Testing

## 25.1 Unit tests

Every component gets isolated tests.

## 25.2 CPU tests

Test:

- every instruction family
- flags
- exceptions
- addressing modes
- prefixes
- FPU
- MMX
- SSE

## 25.3 Memory tests

Test:

- alignment
- paging
- TLB
- page faults
- MMIO
- cache behavior

## 25.4 Device tests

Test each device independently.

## 25.5 Golden traces

Create expected traces for known scenarios.

Example:

```text
EIP=00100000
EAX=00000000
...
MMIO WRITE 0xFED00000 = 0x00000001
...
```

Compare emulator output against the golden trace.

---

# 26. Differential Testing

Where legally and technically practical, compare behavior against reference emulators.

Run identical test scenarios:

```text
Test input
   |
   +---- xemu
   |
   +---- Xermu
   |
   +---- XGrid
```

Compare:

- CPU registers
- memory
- interrupts
- MMIO
- GPU state
- output framebuffer

Differences should be investigated, not automatically assumed to mean XGrid is wrong.

---

# 27. GPU Differential Testing

For deterministic test scenes:

```text
Xbox GPU commands
       ↓
xemu
       ↓
reference image
```

and:

```text
Xbox GPU commands
       ↓
XGrid
       ↓
test image
```

Compare:

- exact pixels where deterministic
- tolerance-based pixel differences
- geometry
- texture sampling
- depth behavior

Generate heatmaps for differences.

---

# 28. Compatibility Test Matrix

Create a database:

```text
Game
Title ID
Region
Version
Boots
Menus
2D
3D
Audio
Input
Gameplay
Performance
Known issues
```

Statuses:

```text
Not tested
Boots
Menu
Playable
Playable with issues
Playable
Perfect
```

Never claim compatibility from one successful launch.

---

# 29. Performance Benchmarking

Benchmark separately:

## CPU

```text
instructions/sec
guest MIPS
basic blocks/sec
JIT compile time
JIT cache hit rate
```

## Memory

```text
RAM reads/sec
RAM writes/sec
TLB hit rate
MMIO accesses/sec
```

## GPU

```text
command submission
draw calls/frame
frame time
GPU time
CPU render time
```

## Full emulator

Measure:

```text
FPS
frame time
CPU utilization
GPU utilization
RAM usage
startup time
JIT compilation time
```

---

# 30. Performance Targets

These are project targets, not guarantees.

## Milestone targets

### Interpreter

Target:

```text
10–20% of native/reference emulator performance
```

depending heavily on workload.

### Initial JIT

Target:

```text
50–70%
```

### Mature JIT

Target:

```text
80–100%+
```

The final result must be evaluated against the same:

- game
- resolution
- renderer
- host CPU
- host GPU
- synchronization settings

---

# 31. C# Performance Rules

## Avoid

```csharp
object
dynamic
LINQ
boxing
per-instruction allocations
virtual calls in hot loops
exceptions for normal control flow
```

## Prefer

```csharp
struct
readonly struct
ref struct
Span<T>
ReadOnlySpan<T>
ref
in
Unsafe
MemoryMarshal
ArrayPool<T>
Vector128<T>
Vector256<T>
function pointers
```

Do not blindly use unsafe code. Profile first.

---

# 32. Allocation Policy

The CPU execution loop should ideally perform:

```text
0 allocations/frame
0 allocations/basic block
0 allocations/instruction
```

Use object allocation primarily for:

- initialization
- configuration
- debugger
- logging
- device creation

---

# 33. Logging Architecture

Use structured categories:

```text
CPU
MEMORY
MMIO
PCI
MCPX
NV2A
APU
USB
IDE
FATX
XDVDFS
XBE
KERNEL
INPUT
AUDIO
VIDEO
JIT
```

Levels:

```text
Trace
Debug
Information
Warning
Error
Critical
```

Hot paths must not generate strings unnecessarily.

---

# 34. Configuration

Example:

```json
{
  "system": {
    "bios": "bios.bin",
    "mcpx": "mcpx.bin",
    "hardDisk": "xbox_hdd.img"
  },
  "graphics": {
    "backend": "Vulkan",
    "resolutionScale": 1,
    "vsync": true
  },
  "cpu": {
    "backend": "Jit",
    "jitEnabled": true
  },
  "audio": {
    "enabled": true
  },
  "input": {
    "backend": "Default"
  }
}
```

Do not store copyrighted firmware in the repository.

---

# 35. CLI

Create:

```text
xgrid
```

Commands:

```text
xgrid run game.iso
xgrid run game.xbe
xgrid pause
xgrid reset
xgrid info
xgrid verify
xgrid trace
xgrid benchmark
```

Useful developer commands:

```text
xgrid cpu-test
xgrid xbe-info game.xbe
xgrid xdvdfs-info game.iso
xgrid nv2a-test
```

---

# 36. Avalonia Frontend

The frontend should provide:

- game selection
- firmware configuration
- storage configuration
- graphics configuration
- controller configuration
- audio configuration
- debugger
- logs
- performance overlay
- save-state management if implemented

The frontend must communicate with the emulator through an API, not internal fields.

---

# 37. Save States

Do not implement save states early.

When implemented, the emulator must serialize:

```text
CPU
RAM
MMU
device state
GPU state
APU state
timers
interrupt controllers
storage state
```

Save-state versioning is mandatory.

Example:

```text
XGrid Save State
Version 1
Architecture version
Device versions
Compressed state
Checksum
```

---

# 38. Snapshot/Replay System

A valuable debugging feature.

Record:

```text
initial state
input events
timing events
external events
```

Then replay deterministically.

This can make bugs reproducible.

---

# 39. Deterministic Mode

Add:

```text
--deterministic
```

This mode should:

- use fixed timing
- control random sources
- avoid nondeterministic thread scheduling
- serialize external input
- use deterministic event ordering

This is extremely useful for regression tests.

---

# 40. Threading Model

Start with a single emulation thread:

```text
Emulation Thread
       |
       +-- CPU
       +-- devices
       +-- scheduler
```

Host services can run separately:

```text
UI Thread
Audio Thread
GPU submission where safe
File I/O
```

Do not parallelize Xbox CPU/device execution prematurely.

The Xbox is fundamentally a single-core CPU target, and deterministic ordering is valuable.

---

# 41. GPU Threading

Keep Xbox GPU command generation logically synchronized with the emulated CPU.

Host GPU submission can be optimized later.

Avoid:

```text
CPU thread
    ↕
GPU thread
    ↕
blocking synchronization every draw
```

Prefer batching.

---

# 42. Error Handling

Create specific emulator exceptions only for unrecoverable developer/configuration conditions.

Examples:

```text
InvalidFirmwareException
InvalidXbeException
InvalidStorageImageException
UnsupportedInstructionException
GpuStateException
EmulationFatalException
```

Normal guest exceptions must remain part of emulated CPU behavior and must not become host exceptions.

---

# 43. Security

The emulator processes untrusted game images.

Treat all images as hostile input.

Requirements:

- bounds checking
- overflow checking where appropriate
- no arbitrary host path access through guest filesystem APIs
- no uncontrolled memory allocation
- no execution of guest-provided host code
- safe archive handling
- fuzz testing of parsers

Particularly fuzz:

```text
XBE
XDVDFS
FATX
ISO
NV2A command streams
```

---

# 44. Fuzzing

Create fuzz targets for:

```text
x86 decoder
XBE parser
XDVDFS parser
FATX parser
NV2A push-buffer parser
MMIO decoder
```

Requirements:

- no host crash
- no infinite loop
- no unbounded allocation
- no out-of-range memory access

---

# 45. Documentation

Every major subsystem needs:

```text
Purpose
Hardware behavior
Registers
Address ranges
Timing assumptions
Implementation status
Known deviations
Tests
Reference material
```

Example:

```text
docs/nv2a.md

# NV2A

## Overview
## Register map
## PFIFO
## PGRAPH
## Memory
## Texture formats
## Shaders
## Renderer mapping
## Known deviations
## Tests
```

---

# 46. Licensing and Clean-Room Process

Before copying code from any project:

1. Identify its license.
2. Confirm compatibility with the intended XGrid license.
3. Record the source.
4. Record whether code is copied, adapted, or independently reimplemented.
5. Preserve required notices.
6. Avoid copying code where the licensing situation is unclear.

Maintain:

```text
docs/research-notes/sources.md
```

with:

```text
Project
URL
License
Subsystem
Relevant information
Code copied?
Date reviewed
```

---

# 47. Milestone Plan

## M0 [DONE] — Repository

- Solution
- CI
- tests
- analyzers
- documentation

## M1 — CPU Core [DONE]

- registers
- decoder
- interpreter
- flags
- exceptions

## M2 — Memory [DONE]

- RAM
- MMIO
- MMU
- TLB

## M3 — Xbox Machine [PARTIAL - bus/PIC/timers/SMBus/EEPROM/SMC/encoder done; GPE/Super I/O pending]

- bus
- interrupts
- timers
- MCPX skeleton

## M4 — BIOS [PARTIAL - MCPX ROM boots, 1BL runs, 2BL reaches its scheduler idle loop]

- firmware mapping
- boot
- BIOS execution

## M5 — Storage

- IDE
- HDD
- FATX
- DVD
- XDVDFS

## M6 — XBE

- parser
- loader
- memory mapping

## M7 — Kernel

- initial Xbox kernel compatibility
- system calls
- threads
- memory
- filesystem

## M8 — NV2A

- registers
- PFIFO
- PGRAPH
- framebuffer

## M9 — Vulkan

- renderer
- textures
- surfaces
- shaders
- 3D

## M10 — Input

- USB
- controllers
- rumble

## M11 — Audio

- APU
- DSP
- mixer

## M12 — JIT

- IR
- block compiler
- code cache
- optimized memory

## M13 — Debugger

- breakpoints
- memory viewer
- CPU viewer
- GPU viewer

## M14 — Compatibility

- game test matrix
- regression tests
- compatibility reports

## M15 — Optimization

- CPU
- memory
- GPU
- synchronization

## M16 — Release

- Windows
- Linux
- macOS
- installer/packages
- documentation

---

# 48. Definition of Done

The project should not be considered production-ready until:

- [x] CPU passes comprehensive instruction tests. (343 CPU tests: integer, control flow, shifts,
  MUL/DIV, strings, system, x87, MMX, SSE1, boot-path instructions)
- [x] MMU passes paging tests.
- [ ] BIOS boots.
- [ ] HDD works.
- [ ] DVD/XDVDFS works.
- [ ] XBE loader works.
- [ ] Xbox kernel compatibility is sufficient for target software.
- [ ] NV2A renders known test scenes.
- [ ] Vulkan backend is stable.
- [ ] Audio works.
- [ ] Controllers work.
- [ ] Debugger works.
- [ ] No known critical memory corruption.
- [ ] Fuzz tests are established.
- [ ] Regression suite runs automatically.
- [ ] Performance benchmarks are reproducible.
- [ ] Windows build works.
- [ ] Linux build works.
- [ ] macOS build works.
- [ ] User-supplied firmware workflow is documented.
- [ ] Licensing documentation is complete.

---

# 49. Recommended First 30 Implementation Tasks

1. [x] Create Git repository.
2. [x] Create .NET solution.
3. [x] Create Core project.
4. [x] Create CPU project.
5. [x] Create Memory project.
6. [x] Create test projects.
7. [x] Configure analyzers.
8. [x] Implement logging.
9. [x] Implement emulator lifecycle.
10. [x] Implement physical RAM.
11. [x] Implement memory read/write.
12. [x] Implement CPU register structure.
13. [x] Implement EFLAGS.
14. [x] Implement instruction decoder.
15. [x] Implement NOP/MOV.
16. [x] Implement stack instructions.
17. [x] Implement arithmetic.
18. [x] Implement branches.
19. [x] Implement CALL/RET.
20. [x] Implement CPU test runner.
21. [x] Implement MMIO abstraction.
22. [x] Implement memory map.
23. [x] Implement paging.
24. [x] Implement TLB.
25. [x] Implement interrupts. (CPU IDT delivery + dual 8259 PIC + PIT IRQ0 done)
26. [x] Implement PCI abstraction.
27. [x] Implement MCPX skeleton. (SMBus AMD756 + EEPROM/SMC/encoder, overlay, boot-script interpreter; real MCPX ROM runs end-to-end)
28. [x] Implement firmware mapping. (flash window/alias + MCPX overlay + reset vector)
29. [x] Implement CPU instruction tracing. (`xgrid boot --trace/--trace-regs/--trace-start/--dump` disassembly + register trace)
30. [~] Attempt first BIOS boot. (MCPX ROM -> 1BL -> 2BL reaches its scheduler idle loop; runs 300M instructions with interrupts enabled — timer/device event delivery under investigation)

Completed beyond the original list: shifts/rotates, MUL/IMUL/DIV/IDIV, string instructions
(REP/REPE/REPNE), system instructions (CPUID/RDTSC/RDMSR/WRMSR/SETcc/CMOVcc/MOV CR-DR/LGDT/LIDT/
INVLPG/ARPL/BSWAP/LEAVE/ENTER/WBINVD/INVD/0F 00 group/`FF /6`/`8F /0`), the full x87 FPU
(loads/stores/arithmetic/compare/transcendentals/env+state save), MMX, SSE1 (with FXSAVE/FXRSTOR/
LDMXCSR/STMXCSR), the full x86 MMU (4 KB/4 MB paging, software TLB, `#PF`, CR0/CR3/CR4 TLB flush,
paging-aware GDT/IDT access), the I/O port space with IN/OUT, PCI bus/config/BARs, LPC routing and
ROM overlay control, dual 8259 PIC with CPU interrupt injection, the 8254 PIT, and the AMD756
SMBus with EEPROM/SMC/encoder devices.

Do not begin NV2A before the CPU/memory/boot foundation is stable.

---

# 50. Recommended First Prototype

The first useful prototype should be extremely small:

```text
XGrid
 |
 +-- CPU
 |    +-- decoder
 |    +-- interpreter
 |
 +-- Memory
 |    +-- 64 MB RAM
 |
 +-- MMIO
 |
 +-- Interrupt controller
 |
 +-- BIOS loader
 |
 +-- CLI
```

Target:

```text
Reset
 ↓
CPU starts
 ↓
BIOS mapped
 ↓
x86 instructions execute
 ↓
MMIO accesses appear in trace
```

This is a much better first milestone than trying to boot a commercial game.

---

# 51. Long-Term Architecture

The intended final architecture is:

```text
                           XGrid
                              |
        +---------------------+---------------------+
        |                     |                     |
      Frontend              Debugger              CLI
        |                     |                     |
        +---------------------+---------------------+
                              |
                         Emulator Core
                              |
       +----------------------+----------------------+
       |                      |                      |
      CPU                   Memory                  Bus
       |                      |                      |
   +---+---+             +----+----+          +-----+-----+
   |       |             |         |          |           |
Interpreter JIT         RAM       MMU       PCI         IRQ
                                                |
                     +--------------------------+----------------+
                     |             |             |              |
                    MCPX          NV2A           APU          Storage
                     |             |             |              |
                   USB          Renderer         Audio      IDE/DVD
                                   |                             |
                            +------+-----+                  +----+----+
                            |            |                  |         |
                         Vulkan       OpenGL              FATX     XDVDFS
```

---

# 52. Final Development Strategy

The project should be approached as three parallel tracks.

## Track A — Compatibility

```text
CPU
 ↓
Memory
 ↓
MCPX
 ↓
BIOS
 ↓
Storage
 ↓
XBE
 ↓
Kernel
```

## Track B — Graphics

```text
NV2A
 ↓
PFIFO
 ↓
PGRAPH
 ↓
Vulkan
 ↓
Shaders
```

## Track C — Performance

```text
Interpreter
 ↓
Profiling
 ↓
IR
 ↓
JIT
 ↓
Block cache
 ↓
Memory optimization
 ↓
GPU optimization
```

Do not let Track C block Track A.

A slow but correct emulator is useful.

A fast emulator that cannot boot is not.

---

# 53. Strategic Recommendation

The strongest implementation strategy is:

```text
xemu
    ↓
Xbox hardware knowledge

Xermu
    ↓
standalone CPU/JIT architecture

Cxbx
    ↓
XBE/HLE/direct execution research

Existing C# Xbox tooling
    ↓
XDVDFS/XISO/storage

             ↓

        XGrid
             ↓
     Native C#/.NET Xbox
          emulator
```

The project should initially target **correctness and boot compatibility**, then introduce the JIT only after the interpreter has enough coverage to serve as a reliable reference implementation.

The interpreter should remain permanently available as a debugging/reference backend even after the JIT becomes the default.

---

# 54. First Major Success Criterion

The first major project goal should be:

> Boot a legally obtained/user-supplied Xbox BIOS and execute it correctly through the C# CPU, memory, MMU, interrupt, MCPX, and basic bus implementation.

The second major goal:

> Load an XBE from a user-supplied Xbox storage image and reach executable Xbox code.

The third:

> Render a deterministic NV2A test scene through Vulkan.

Only after these three goals should the project be considered ready for serious game compatibility work.

---

# 55. Suggested Project Identity

Working name:

**XGrid**

Possible alternatives:

- XboxSharp
- XSharpEmu
- XBoxEmu.NET
- Xemu.NET
- XemuCS
- XboxCore
- XCore

The project name should be reviewed for trademark/confusion concerns before public release.

---

# 56. Final Checklist

## Architecture

- [x] Core independent of frontend
- [ ] Hardware independent of host graphics API
- [ ] CPU backend abstraction
- [ ] Graphics backend abstraction
- [ ] Audio backend abstraction
- [ ] Input backend abstraction
- [ ] Storage backend abstraction

## CPU

- [x] x86 decoder
- [x] interpreter
- [x] MMU
- [x] TLB
- [x] FPU
- [x] MMX
- [x] SSE
- [ ] JIT
- [ ] block cache
- [ ] profiling

## Xbox hardware

- [x] MCPX
- [x] SMBus
- [x] EEPROM
- [~] Flash
- [x] PCI
- [x] interrupts
- [x] timers
- [ ] IDE
- [ ] USB
- [ ] APU
- [ ] NV2A

## Storage

- [ ] FATX
- [ ] XDVDFS
- [ ] XISO
- [ ] HDD
- [ ] DVD
- [ ] XBE

## Graphics

- [ ] PFIFO
- [ ] PGRAPH
- [ ] textures
- [ ] surfaces
- [ ] shaders
- [ ] Vulkan
- [ ] optional OpenGL

## Developer tools

- [ ] debugger
- [ ] trace viewer
- [ ] XBE tool
- [ ] XDVDFS tool
- [x] CPU test runner
- [ ] GPU test runner
- [~] benchmarks

## Quality

- [x] unit tests
- [x] integration tests
- [ ] differential tests
- [ ] fuzzing
- [ ] deterministic mode
- [ ] regression suite
- [ ] compatibility database

## Release

- [x] Windows
- [ ] Linux
- [ ] macOS
- [~] documentation
- [ ] licensing review
- [ ] firmware/user-data documentation
- [ ] performance documentation

---

# Conclusion

XGrid should not be treated as a direct port of xemu.

It should be a clean, modular C# implementation of the original Xbox hardware model, informed by xemu, Xermu, Cxbx, public technical documentation, reverse-engineering research, and controlled differential testing.

The most important architectural decision is to keep:

```text
CPU
Memory
Xbox hardware
Storage
GPU emulation
Host graphics
Frontend
Debugger
```

as separate layers.

The most important performance decision is to implement:

```text
Interpreter → Reference implementation

JIT → Production CPU backend
```

rather than attempting to write the JIT first.

The most important compatibility decision is to implement the Xbox machine incrementally and use deterministic tests and differential traces throughout development.

The ultimate target is a genuinely independent:

```text
C# / .NET
        +
Pentium III emulation/JIT
        +
Xbox hardware emulation
        +
NV2A
        +
Vulkan
        +
XDVDFS/FATX
=
Native Xbox emulator
```

---

# PART II — DETAILED XGRID IMPLEMENTATION PLAN

This part turns the phases above into a concrete, file-level plan. It is the working checklist.
Every phase lists: goal, prerequisites, tasks, files/interfaces, tests, acceptance criteria, risks,
and the reference notes to consult. Phases are ordered; do not start a later phase on an unstable
earlier one except where explicitly parallel (Track B/C).

## Implementation status (last updated 2026-10-12; XboxWiki findings integrated below)

| Phase | Status | Notes |
|---|---|---|
| 0 — Project Bootstrap | **Done** | `XGrid.sln`, 16 src + 5 test + benchmark projects, `Directory.*`, CI, scripts, docs |
| 1 — Emulator Core | **Done** | `Emulator` lifecycle, `EmulatedClock`, `Scheduler`, configuration + AOT-safe JSON loader |
| 2 — Memory System | **Done** | `PhysicalMemory`, `Endian`, `AddressSpace` (regions/priority/aliases), `IMmioDevice`, RAM benchmark; flash/MCPX/PCI-hole regions register in their phases |
| 3 — x86/Pentium III CPU | **Done** | Tiers 1–10 done (integer, control flow, shifts/rotates, MUL/DIV, strings, system incl. CPUID/RDTSC/MSR/SETcc/CMOVcc, x87, MMX, SSE1) plus boot-path instructions (WBINVD/INVD, ARPL, BSWAP, LEAVE, ENTER, 0F 00 group, `FF /6`, `8F /0`, INS/OUTS, XADD/CMPXCHG, BSF/BSR, BT/BTS/BTR/BTC, AAM/AAD with #DE on base 0 and QEMU-verified flag semantics); exceptions #DE/#UD/#PF/#BP; x87 arithmetic uses double-precision rounding (documented limitation). Fixed 2026-10-10: `FCOM`/`FCOMP` memory operands (`D8`/`DC`) compared ST0 with itself instead of memory (research-notes §34) |
| 4 — x86 MMU | **Done** | 4 KB/4 MB paging, software TLB (large pages always walk), A/D bits, WP, `#PF` error codes, MOV CR/DR, INVLPG, CR0/CR3/CR4 TLB flushes, paging-aware GDT/IDT access |
| 5 — Bus & Interrupts | **Partial** | I/O port space + CPU IN/OUT, PCI bus (0xCF8/0xCFC, BAR sizing, command, secondary-bus routing, interrupt raise + lower events), host/AGP bridges, LPC routing (INT/PIRQ/ACPI bytes) + ROM overlay + ACPI PM, dual 8259 PIC + CPU interrupt injection, PIT 8254 (verified firing at ~1060 Hz during real boot), MC146818 RTC (ports 0x70/0x71, periodic IRQ8); PCI devices with xemu IDs — OHCI USB0/USB1 (`OhciUsbController`, 0:2.0/0:3.0, `0xFED00000`/`0xFED08000`, IRQ1/IRQ9) with a synchronous schedule engine + the xemu-faithful USB daughterboard hub chain and `XidGamepad` behind it (§§54–55, §69) + latched and step-timed `--pad` injection (§56, `--pad-step` §70 — XPP
           opens the pad and polls it live) + scripted `--pad-script` sequences (§74); nvnet MMIO (`McpxNvNetDevice`, `0xFEF00000`, idle/link-up/MDIO, §52–53); APU MMIO in `XGrid.Audio`, ACI in `XGrid.Bus/Aci`; bus-1 NV2A; IDE corrected to nForce 10DE:01BC; SMBus completion IRQ wired end-to-end (device raise/lower → ACPI byte 1 → PIC, xemu `amd_smbus.c` parity); CLI prints PIC/PIT/interrupt/IDE/PCI/MMIO/SMC/SMBus/NV2A/USB/NVNET diagnostics (USB `ctl/intr/addr/cfg` + transfer/request logs, NVNET `reads/writes`, 1MB unmapped buckets + top pages, EIP-tagged 250M progress); DMA helper pending |
| 6 — MCPX | **Partial** | AMD756 SMBus controller (exact status/protocol semantics + xemu-parity IRQ raise on completion with `HCYC_EN` / lower on status write + transaction diagnostics with 192-entry history), 24C02 EEPROM (user-supplied image), SMC (version/challenge/scratch, `TRAYCLOSED` set at realize and `SHORT_ANIMATION` scratch default per xemu), Conexant CX25871 encoder stub, **MCPX ACI (AC'97) register model at 0xFEC00000, xemu `ac97.c`-faithful NAM/NABM (`McpxAciDevice`)** + **APU at 0xFE800000 (`McpxApuDevice`, §33)**; the real MCPX 1.0 ROM executes end-to-end (boot-script PCI/SMBus/MMIO interpreter, decrypt loop); flash AMD/SST command set, GPE/GPIO, Super I/O pending |
| 7 — Firmware & Boot | **Partial** | `XboxMachine` (RAM/aliases, flash window + 16 MB alias, MCPX 512-byte overlay with disable/reset semantics), BIOS loader, `xgrid boot` CLI debugger (`--trace/--trace-regs/--trace-start/--dump/--dumpv/--scan/--break/--break-log/--watch/--ramdump/--ramin/--fb/--frame/--pgraph/--pgraph-max/--writedump/--pad-buttons/--pad-analog/--pad-sticks/--pad-step/--pad-script`, 250M-step progress with EIP trail, `ulong` steps; `xgrid sector` raw LBA dump); real MCPX 1.0 ROM -> 1BL -> 2BL -> kernel executes (LLE). **Kernel cold-boot init completes and the system runs menu-idle at 5B steps with no fault** (EIP `0x2D3B9`, live dashboard render loop, configured USB pad + link-up NIC, black degenerate flips; §§51–61): SMC handshake, PCI setup, RTC, NV2A driver, HDD unlock, DVD probe, 27 READ DMA (~828 KB), EvoX XBE in RAM. The animation wait (§32), ACI/APU gates (§33) and the CPU `fcomp` bug (§34) are resolved; the boot runs the full intro animation, **executes the dashboard XBE**, renders through NV2A (M5–M8, §37–43), discovers content (§45) and re-saves `C:\evox.ini` (§47) — then runs its menu loop. The Xbox PIT frequency is fixed to 1,125,001 (§68, pump = 10 guest-ms) and the full input path is live (§§69–70): the USB hub chain unblocks XPP, the dashboard opens the pad, polls `GetState` every frame, D-pad levels move the menu selection and a            step-timed A edge (`--pad-step`) fires the activation writer `0x1859D`. See M9 + research-notes §31-34, §51-61, §68-72, §74 |
| 8 — Storage | **Partial** | `XGrid.Storage`: `IDiskImage` + `RawDiskImage` + read-only `Qcow2DiskImage` (v2/v3, L1/L2, zero-flag clusters return zeros per QEMU `qcow2.h`) + `OverlayDiskImage` (in-memory copy-on-write; used by `AttachHdd` so guest WRITE DMA cannot modify the shared reference image); ATA/ATAPI `AtaDevice` (IDENTIFY, READ/WRITE SECTORS + DMA `0xC8`/`0xCA`, SET FEATURES, FLUSH, SECURITY UNLOCK, ATAPI PACKET + REQUEST SENSE, 8/16/32-bit data port), `IdeController` (nForce 10DE:01BC per xemu `#ifdef XBOX`, PCI 0:9.0, legacy ports 0x1F0/0x3F6, IRQ14 via LPC routing, dual-drive taskfile HDD master + ATAPI DVD slave, synchronous bus-master DMA: BAR4 window, PRDT walk through `IGuestMemory`, IRQ ack); `XGrid.XboxFs/FatxFileSystem` (read-only XATF) + `xgrid fatx ls/cat`; guest kernel loads the dashboard exclusively via READ DMA (research-notes §30–31) and the dashboard later issues WRITE DMA `0xCA` to LBA `0x80000` (§35); host-side XDVDFS reader done (`XGrid.XboxFs/XdvdfsFileSystem` over NuGet `XISOSharp` 1.4.2 + `xgrid xdvdfs ls/cat`, §64); guest DVD reads served (`IDvdImage`/`IsoDvdImage`, `boot --dvd`, TUR/capacity/READ10/12, §65, validated: kernel probes real sectors, no auth traffic); compressed CHDs (`ChdDvdImage` over CHDSharp 1.4.3, magic dispatch in `OpenDvdImage`, §66) |
| 9 — XBE Loader | **Partial** | `XGrid.Xbe/XbeImage` (host inspection parser: header/cert/sections/TLS/libraries from Cxbx offsets, per-type XOR decode, strict bounds validation) + `xgrid xbe info` (raw file or via FATX); verified against the real dashboard (`EvoX`, retail, entry `0x12A17`, 1 section); guest-side verify/relocate/section-map/entry-call pending (the XBE launch gate — the kernel maps the dashboard at base `0x10000`, image phys `0x5A000`, entry VA `0x22A17`; the guest runs the full dashboard lifetime — rendering, discovering, saving, menu-idle render loop at 5B steps, EIP `0x2D3B9`; see M9 Gap 1d, §§51–61) |
| 10 — Kernel Interface | Not started | LLE-first: only needed if the dashboard's kernel-thunk imports require host-side ordinals; guest kernel is real and running |
| 11 — NV2A | **Partial** | `XGrid.Nv2a/Nv2aMmio`: 16 MB BAR0 window at 0xFD000000 wired in `XboxMachine`; xemu-faithful blocks/defaults; PFIFO pusher (CACHE1 parse, DMA jump/call/return/inc, RAMHT, GET writeback, ERROR+suspend), PGRAPH recording sink + software rasterizer (Gouraud triangle renders through the full pushbuffer path, 307K px, M4 done); surface/clear/flip tracking, PRMCIO retrace, 60 Hz VBLANK; semaphore release via RAMHT DMA object (`0x1D70`) and `NV097_NO_OPERATION` PGRAPH notification (`0x100`) unblocked the boot-animation wait (§32); **M5 done (§37–39):** transform-program/constant storage, inline element semantics, `VshProgram` interpreter (MAC/ILU, muxes/swizzles/masks, paired ops, A0-relative constants), vertex attribute fetch (all formats) and the element draw path — ran on the dashboard without fault (2670 draws; §42: pixels all inline, program draws degenerate); **M6 done (§40):** per-slot texture state, swizzled (Morton) + linear A8R8G8B8/R5G6B5 sampling with wrap/mirror/clamp, modulate by `oD0`, texcoords from `oT0` (all 10.1M px sample; §44: CPU-uploaded assets, dark by nature). **M7 done (§41–42):** alpha/stencil/depth/blend/mask fragment pipeline; validation fixed two interpreter bugs (R/V/C mux rotation, ORB polarity) and proved exact pixel accounting (all 10.1M px from 33 legacy fullscreen inline draws; ~2637 program draws degenerate at this boot stage, 0 px — steady-state program draws validated live in §§57–58). **M8 done (§43):** flip present events with at-flip surface capture, BGRA snapshot, `XGrid.Graphics/DisplayFrame` BMP export, CLI `--frame` (`frames=33 stalls=33`, 640×480 `0x32A4000`, opaque black). Content discovery (§45) + file-save (§46–48) done; menu-idle render loop at 5B (EIP `0x2D3B9`, double-buffer `0x3D00000↔0x3E2C000`, still black) with viewport-baked `C0.z` clip proven guest-side (§§57–61). **§71 fidelity wave:** inline `SET_VERTEX_DATA*` + `DRAW_ARRAYS` (M5 gap closed), all 11 XF-context constant writers (c56–c59, c64–95 — fixes §42's wrong "no-ops on xemu" claim), stride-0/bit31/program-start/output-defaults, MUL zero rule + GLSL min/max/LIT + paired-ILU + oPos epilogue, perspective-correct varyings + oD0 NaN→1. Register combiner (psh) merged (§75: real methods 0x260/0x288/0xA60/0xA80/0xAA0/0xAC0/0x1E20/0x1E40/0x1E60/0x1E70/0x1E74/0x1E78 — the brief's 0x1940 range was inline vertex data, corrected; combiner-disabled draws pixel-identical). Fog + dependent/bump/dot
modes merged (§78). Remaining: fixed-function foggen, exotic modes,
HILO dot maps, oPos.z clipRange normalization, Vulkan/OpenGL backends
(16.6–16.7) |
| 12 — APU | **Partial** | `XGrid.Audio/McpxApuDevice`: 512 KB window at 0xFE800000, xemu `apu.c`/`vp.c`-faithful register model (ISTS/IEN + `update_irq` gating, FECTL traps, FEDECMETH/FEDECPARAM FE messages, FEMEMADDR/FEMEMDATA magic write, VP PIO_FREE=0x80, VOICE_ON/OFF, voice-list regs), notifier/voice-state writes to guest RAM, frame-quantized `SE2FE_IDLE_VOICE` delivery (`Advance`, 256 samples @ 48 kHz), PCI IRQ wired to PIC; DSP/mixing/voice processing pending |
| 13 — USB & Controllers | **Partial** | OHCI schedule engine (control/bulk/periodic, done-queue/WDH, §54), xemu-faithful USB hub chain (4-port daughterboard hub -> 3-port controller hub -> XID, §69), `XidGamepad` with latched + step-timed `--pad` injection; **the guest's XPP stack now opens the pad and polls `GetState` every frame; D-pad navigates the menu, A-edge activation fires `0x1859D` (§§69–70)**; scripted `--pad-script` sequences (§74). Remaining: memory units/USB storage class, more device types |
| 14 — Networking | Partial (MMIO + TX) | nvnet MMIO + per-reg histograms (§52–53, §62) + P0/P1 packet engine (§84: ring latches, IRQ4, TX walker + loopback backend, CLI counters; guest init-only steady state preserved, 956 tests); P2 RX + P3 socket backend remain |
| 15–23 | Not started | |

Current test baseline: **885 tests passing** (39 Core, 398 Cpu, 52 Memory, 172 Bus, 16 Integration, 94 Nv2a, 8 Audio, 46 Storage, 19 XboxFs, 6 Xbe, 4 Graphics);
`dotnet build -c Release` clean; `dotnet format --verify-no-changes` clean.

## Project completion assessment (2026-10-12)

Weighted estimate over the 24 plan phases (0–23). Phase fractions reflect
implemented-vs-planned surface (behavior, tests, reference parity), and the
weights approximate remaining effort (CPU interpreter, NV2A and JIT
dominate). This is an estimate, not a metric — treat it as ±5 points.

| Phase | Weight | Done | What remains |
|---|---|---|---|
| 0 Bootstrap | 1 | 100% | — |
| 1 Core | 2 | 100% | — |
| 2 Memory | 3 | 100% | — |
| 3 CPU | 11 | 95% | rare-op audit; x87 double-rounding limitation |
| 4 MMU | 3 | 100% | — |
| 5 Bus & IRQ | 6 | 85% | DMA helper, ELCR, Super-I/O, GPE |
| 6 MCPX | 6 | 80% | flash command set, GPE/GPIO, Super I/O |
| 7 Firmware & Boot | 5 | 90% | ATA-security edge cases; guest XBE-loader items |
| 8 Storage | 5 | 85% | writable host-side FATX; DVD media edge cases |
| 9 XBE Loader | 3 | 90% | host-side relocation/launch paths |
| 10 Kernel Interface | 2 | 5% | whole phase (HLE ordinals) — deferred, LLE-first |
| 11 NV2A | 15 | 75% | visible menu render; fixed-function foggen; exotic modes; depth clipRange; Vulkan/OpenGL backends (16.6–16.7) |
| 12 APU | 4 | 55% | DSP, voice processing, mixing, resampling, host output |
| 13 USB & Controllers | 4 | 70% | memory units/storage class, more device types |
| 14 Networking | 4 | 45% | RX consume/filter (P2), socket backend (P3) |
| 15 CPU JIT | 11 | 5% | whole phase (IR, basic blocks, block cache, codegen) |
| 16 CPU Optimization | 3 | 10% | benchmark-driven tuning |
| 17 Memory Optimization | 2 | 10% | tuning |
| 18 GPU Optimization | 4 | 10% | rasterizer/backend performance |
| 19 Debugger | 3 | 30% | save states, symbols, GUI |
| 20 Automated Testing | 3 | 55% | fuzzing, wider compatibility corpus |
| 21 CLI & Frontend | 3 | 50% | GUI frontend |
| 22 Save States & Replay | 2 | 5% | snapshot/replay |
| 23 Release | 3 | 10% | packaging, compatibility list, installer |

**Result: ~60% complete overall** (weighted: 64.85 of 108 weight-points).
By track:

- **LLE core (phases 0–14): ~80%.** The real console chain — MCPX ROM ->
  1BL -> 2BL -> kernel -> dashboard — boots, renders, discovers content,
  saves files, and now navigates with a real controller through the
  daughterboard hub topology (§§68–70).
- **Performance/finish track (phases 15–23): ~17%.** Dominated by the CPU
  JIT (5%) and optimization phases; this is the throughput gap between
  forensic runs (~14M insn/s) and interactive rates.
- **Flagship milestone M9 (dashboard boot): ~80%.** Boot, render pipeline
  and the full input path (XPP open -> `GetState` polling -> menu
  navigation -> activation) all work; a visibly rendered menu screen is
  the remaining piece.

Interpretation: the "does the console boot and respond" core is close;
the long tail is JIT/optimization, NV2A completion (fog, backends)
and productization (frontend, save states, release).

**NEXT SESSION — visible menu.** pgraph verdict GUEST-ACCURATE (§82):
submenu adds real draws (+139 tris, +98K elements) but all die in
vertex clip on byte-identical poisoned C0 — no rasterizer gap. Fault
is upstream in the viewport bake: audit surface/zeta/caps responses
at SetViewport-bake time. DVD auto-launch TRACED (§83): handoff step
537,926,130 via `call [0x10128]` → entry `0x22E35`; file-path
decision; `0xAD` requested once (§66 correction); trace/break worlds
diverge ~100M steps — use break-world steps for exactness.
007 second wait DECODED (§85): parked STRM, exit needs `MPCh` magic
or `0x2A830` — suspect user-mode completion delivery; next is
0x2AA65 windowed break-log (~1.9B) + thread/APC observability.
nvnet P0+P1 MERGED (§84); P2 RX + P3 backend remain.
NV2A fog + dependent/bump/dot modes MERGED (§78);
remaining: fixed-function foggen, exotic modes, HILO dot maps,
clipRange, Vulkan/OpenGL backends (16.6–16.7).
`F36A2`/LBA 8 CLOSED as no-bug (§73).
**`RemoteX`** dash (XBApp, static IP); streams + carved runner in temp.
M5 (`VshProgram` interpreter — mux mapping and ORB polarity corrected in §42,
transform constants, attribute fetch, element draws), M6 (per-slot texture
state, swizzled/linear sampling), M7 (alpha/stencil/depth/blend/mask
fragment pipeline) and M8 (flip present events with at-flip surface capture,
`XGrid.Graphics/DisplayFrame` BMP export, CLI `--frame`) are implemented and
unit-tested. **Pixel accounting:** all pixels still come from the legacy
fullscreen inline draws; program draws paint ~0 px in the black state-0 menu.
The §42 c58/c59 "no-ops on xemu too" claim was wrong — xemu feeds c58/c59
from the viewport methods; §71 implemented all 11 aliased constant writers.
Steady-state program draws validated live in §§57–58 (real shaders, clip
proven guest-side). The presented frame
(§43) is 640×480 from surface `0x32A4000`, opaque black — the flip chain
presents the dashboard surface, not the `0x1000` early-boot region.
The 2.5B content scan (§45) gave way to the evox.ini re-save at 3.2B
(§47), complete at 4.0B with post-save disk.bin reads, GPU stirrings and
the 2×2 MB Shadow-RAM surface upload (§48–51) — landing in RAM top via
the write-combined alias (§50), 59% BGRA pixels. Menu-idle since
§51 (5B, kernel delay-loop EIP `0x800153D3` pre-NIC; live render loop
EIP `0x2D3B9` post-NIC) with NIC link-up (§52–53) + enumerated USB pad
(§54–55) + latched `--pad` injection (§56, guest never polls); C0
viewport-baked clip proven guest-side (§57–61). Indexed draws are fully
traced through RAM arrays to oPos (§58). Remaining M5 gaps: inline
`SET_VERTEX_DATA*` immediate path and `DRAW_ARRAYS` ranges. Test media
for future DVD/XDVDFS work: `H:\Tests\XBOXTest` (37 Redump ISOs),
`E:\Microsoft Xbox` (945 CHDs) — read via `ChdDvdImage` (§66).
(see §13.5). Reference: xemu
`hw/xbox/nv2a/pgraph/` (`methods.h.inc`, `draw.c`, `surface.c`,
`vsh-prog.c`), `nv2a_regs.h`. Tools: `--break-log <addr>`, `--pgraph`/`--pgraph-max`, `--pad-*`, `--eip-hist N`,
`--ramdump`/`--ramin`, `--fb`/`--frame`, `--writedump`, `sector`, objdump. Evidence
trail: research-notes §30-61.

Phase 7 progress: the CPU gained far control transfers (JMP/CALL ptr16:16/32 and m16:16/32, RETF),
segment loads/stores (MOV Sreg, PUSH/POP Sreg incl. FS/GS), and CS.D-driven default operand size
(`XboxContext.CodeSegment32` set from the GDT descriptor on CS loads, cleared at reset). With the
real MCPX 1.0 ROM: reset vector -> LGDT/LIDT -> CR0.PE -> far jump `0x0008:0xFFFFFE00` -> flat
protected mode -> boot-script interpreter (9-byte opcodes in the BIOS: PCI config, SMBus, MMIO
writes) -> decrypt/copy loop -> MCPX enables paging -> jumps to the decrypted 1BL at 0x90000.
The 1BL sets up MTRRs/PAT, builds its own page tables (4 MB pages), enables caching + WP + NE,
disables the ROM overlay, copies itself to 0x400000, then runs from the high-RAM alias and sets up
its own GDT/IDT/TSS (`LTR`) before executing `HLT` (waiting on an interrupt source) at ~24M
instructions. `xgrid boot --bios <path> [--mcpx <path>] [--eeprom <path>] [--hdd <path>] [--dvd <path>] [--steps N] [--trace N]
[--trace-start N] [--trace-regs] [--dump addr[,len]] [--ramdump <path>] [--frame <path>] [--pgraph <path> [--pgraph-max N]]
[--writedump <dir>] [--pad-buttons <set> --pad-analog <hex> --pad-sticks <lx,ly,rx,ry> [--pad-step N] [--pad-script <set@step[+hold],...>]] [--eip-hist N]` runs user firmware and prints the final CPU/device state.
Remaining for M1: none — M1 (BIOS execution) is complete; the boot chain now runs
MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard DMA load. The paragraphs below are
historical (2BL-era forensics kept for reference).

Kernel-phase diagnosis (session 2026-10-09; all with Complex_4627 + mcpx_1.0 + user eeprom/hdd):
**RESOLVED — kept as history.** The CMPSB mismatch below was the SMBus word-read bug
(kernel got `0x00A8` instead of `0xC4A8`, built a decimated WCHAR table); the fix
(compose consecutive byte registers for multi-byte reads, matching HW/QEMU) let the
gate fall through, after which the IDE path ran and Gate 1 completed (kernel loads the
EvoX dashboard via DMA; see M9 + research-notes §23, §31). The `0x800426D8` park was
later diagnosed and passed (research-notes §32), as were the ACI/APU gates (§33);
the animation + XBE launch path is now unblocked (M9 Gap 1d, research-notes §34).
- Idle loop decoded (`0x8001B02E`: STI/NOP/NOP/CLI, empty-DPC check at `[EBP]`, wake flag at
  `[EBX+0x2C]` = `0x80035C08` = PRCB NextThread). Parks at instruction 25,062,717 with zero
  interrupts delivered; 50M/100M runs are frozen past that point (only tick counts grow).
- The parked thread (`0x80035A90`) is the idle thread itself (its stack is the live ESP,
  `WaitBlockList=NULL`). A second thread (`0xD0008BF8`, prio 8, own 12 KB stack) is dispatched
  right after parking via `KiFindReadyThread` → `SwapContext`; it runs ~140K instructions
  (encoder writes, SMC handshake/scratch/LED, full 256-byte EEPROM word sweep, 9 RTC reads,
  PIC setup, one `KeStallExecutionProcessor` delay) and exits with status 0 at ~25.2M through
  `PspExitThread`. Afterwards: no ready threads, empty DPC list, no timers — true stall.
- Exit cause: 5-byte `CMPSB` at `0x800551B6` (HMAC-SHA1 worker `0x55107`) mismatches on byte 0
  (computed `83 F7 8E BE…` vs expected `A8 00 F9 00 BD…`, the latter = EEPROM `hash[20]` even
  bytes as WCHAR, verified byte-exact against the image across all 128 table entries). Mismatch
  → cleanup → `PspExitThread(0)`; match path (`LEAVE/RET`) returns into the IDE-unlock path
  (`0x80055C30` consumer, IDE taskfile code at `0x80055C67+`), which never runs.
- Unblocked along the way (each verified by boot): NV2A MMIO region (FD unmapped buckets gone,
  kernel probes hit real regs); SMBus completion IRQ + ACPI-byte-1 routing (was the original
  idle cause — kernel is interrupt-driven here, 2BL-era code polls); AAM/AAD (fixes BugCheck
  `0x1E`/`STATUS_ILLEGAL_INSTRUCTION` in the RTC/BCD path).
- Ruled out as mismatch sources: EEPROM bytes (table verified 128/128 against the image),
  SMC challenge/IDs (fixed, xemu-identical), EEPROM image self-consistency (custom double-SHA1
  hash MATCHES under 1.0 IVs, region = 1, both CRCs OK, serial/MAC valid), RTC epoch (host-time
  experiment: identical suicide at the same instruction), AV pack (`hdtv = 0x01` both sides),
  SMC `INTSTATUS.TRAYCLOSED` (now set at init per xemu; suicide unchanged at the same
  instruction), scratch init (`SHORT_ANIMATION`, matching xemu `skip_boot_anim`; suicide
  unchanged), CPUID (now at full parity: leaves 0/1/2 incl. Xbox-faked cache bytes, model
  8/stepping 10, identical feature bits, extended leaf reports 0, out-of-range clamps to
  leaf 2 — all per QEMU `target/i386/cpu.c`).
- A/B proven mode-invariant (identical bytes in fast-run vs single-stepped boots despite
  different IRQ timing): all crypto inputs are static and verified, so the gate outcome is
  input-determined. Pre-wake window trace confirms no divergent live reads (no CPUID/RDTSC/
  SMBus/NV2A; only PCI/RTC-program/RTC-status/PIT/PM/Super-I/O writes and one Super-I/O
  config read). Conclusion: xemu reaches the same gate with the same outcome on this exact
  no-DVD retail-dashboard configuration; the user's working xemu boots must differ in
  effective configuration (DVD game path with `dvd_path` set, and/or custom BIOS per the
  user's own hypothesis). Next: verify with an xemu no-DVD dashboard boot, or proceed down
  the DVD/game path (needs ATAPI) and revisit dashboard with matched images.
- Incidental fixes this session: batching determinism (timer remainders accumulate, so totals
  no longer depend on advance granularity; CLI trace/break loops now share `XboxMachine.Advance`
  including NV2A PTIMER), `--watch start,end` CLI flag for physical watchpoint ranges.
- Dashboard-storage path, host side (session 2026-10-09): `XGrid.XboxFs` FATX reader verified
  against `xbox_hdd.qcow2` (5 superblocks at `0x400/0x177400/0x2EE400/0x465400/0x55F400`,
  cluster size 32; u16 FAT below 0xFFF5 clusters (`FFF8` reserved, `0000` free, `FFFF` end),
  u32 above (`FFFFFFF8` free, `FFFFFFFF` end); 64B entries, `0xFF` ends / `0x00` frees a slot,
  root = first data cluster auto-detected by entry density). The `0x465400` partition is a
  softmod dashboard (`xboxdash.xbe` 770048 B, `evox.ini`, HeXEn, BIOS/EEPROM/HDD-key backups);
  `0x55F400` holds `TDATA/UDATA/CACHE/Dash/SAVES`. `XGrid.Xbe` parses the real dashboard
  (retail `EvoX`, entry `0x12A17` after XOR decode, 1 section, no TLS/imports). Also fixed
`Qcow2DiskImage` to return zeros for zero-flagged clusters (none present here, per QEMU
`qcow2.h`). Remaining on this path: ATA SECURITY (drive reports unlocked, matching QEMU
default, so likely unneeded), guest-side FATX/XBE use, ATAPI DVD. (USB OHCI MMIO done §36.)
- xemu differential via GDB stub (decisive, 2026-10-09): with identical images, xemu's kernel
  reaches the same `CMPSB` at `0x800551B6` with the same thread/stack layout, but its A/B
  buffers both hold the raw EEPROM hash (`A8 C4 F9 56…`, match) while ours hold digest/WCHAR
  (mismatch). The HMAC wrapper (`0x2F641`) fires repeatedly on xemu (boot + steady state), and
  post-match it is called a third time keyed by the match product at `0x8003B008` — confirming
  the match feeds the IDE-unlock crypto (`0x80055C30` consumer). So the fill paths diverge
  upstream of the compare; the selector is still open (all static inputs verified identical).
- FATX ground truth (host-side, verified against `xbox_hdd.qcow2`): 5 superblocks
  (`0x400/0x177400/0x2EE400/0x465400/0x55F400`, cluster size 32, root field 1); u16 FAT below
  0xFFF5 clusters (`FFF8` reserved, `0000` free, `FFFF` end) and u32 above (`FFFFFFF8` free,
  `FFFFFFFF` end); 64B entries (len/attrs/42-char ASCII name/start/size; `0xFF` ends a dir,
  `0x00` frees a slot; case-insensitive lookup); root = first data cluster (auto-detected by
  entry density). The `0x465400` partition holds a softmod dashboard (`xboxdash.xbe` 770048 B,
  base `0x10000`, entry `0x10178`, `evox.ini`, HeXEn, `bios.bin`/`eeprom.bin`/`hddkey.bin`
  backups); `0x55F400` holds `TDATA/UDATA/CACHE/Dash/SAVES`. `xgrid fatx ls/cat` verified
  end-to-end (sizes match, XBEH parses). Next on this path: ATA SECURITY (unlock), XBE loader
  (entry `0x10178`, SHA1 `223a825c…` recorded, file itself stays out of the repo).
Phase 5 notes: `XGrid.Bus` now provides `IoSpace` (dynamic windows), `PciBus` (standard 0xCF8/0xCFC
config mechanism, BAR probe/assign, command register), `PciHostBridge` (0:0.0), `PciAgpBridge`
(30:0.0), `LpcController` (1:0.0: INT/PIRQ/ACPI routing with hardware defaults, MCPX ROM overlay
disable on config 0x80 bit 1, PM1 status/enable, 24-bit PM timer), `Pic8259` (master/slave,
ICW/OCW, cascade, poll, EOI), `Pit8254` (3 channels, modes 0/2/3, latch), and `XboxBus` wiring.
`XboxCpu` gained an `IIoBus`, IN/OUT (0xE4-0xE7, 0xEC-0xEF), and `IInterruptSource` injection
with IF-gated acknowledge. Deferred within Phase 5: DMA helper, ELCR,
Super-I/O UART fidelity (open-bus 0xFF today), GPE beyond PM1/timer.
(USB OHCI MMIO done §36 + schedule engine §54; APU/ACI MMIO done §33.)

Phase 3 notes: x87 values are stored as true 80-bit extended format, but arithmetic and
transcendental operations round through IEEE double (FSIN/FCOS/FPATAN/F2XM1/FYL2X/FPREM/FRNDINT/
FSCALE/FXTRACT) rather than 64-bit-significand soft-float; integer conversion, loads/stores,
constants, and comparison use the full extended format. MMX registers alias the x87 significand
as on hardware; SSE1 executes scalar loops with single-precision floats (no SSE2, per PIII).
FWAIT does not yet deliver #MF for unmasked x87 exceptions.

## Conventions for all phases

- **Names**: solution `XGrid.sln`; assemblies `XGrid.*`; test assemblies `XGrid.*.Tests`.
- **Target**: `.NET 9` (`net9.0`) initially, SDK pinned in `global.json`; `XGrid.Core` must remain
  NativeAOT-compatible where practical (no reflection in hot paths, no `dynamic`).
- **Language**: nullable enabled, implicit usings off (explicit imports for clarity), file-scoped
  namespaces, `LangVersion latest`, `TreatWarningsAsErrors true`, deterministic builds.
- **Style**: no comments except where behavior is non-obvious; no `#region`; public API documented
  with XML docs. Hot paths follow plan section 31 (no LINQ/boxing/allocation).
- **Licensing**: consult `docs/research-notes/sources.md`. Never copy reference code. XISOSharp
  (MIT) is the only permitted third-party library, behind an interface/adapter.
- **Definition of done per phase**: builds clean; all tests pass; new public APIs documented;
  analyzers/format clean; acceptance criteria met; research note updated if a fact changed.
- **Test naming**: `Method_Scenario_Expected`. Devices get a register/state test; parsers get
  boundary/corruption tests; fuzz targets added per plan section 44.

## Phase 0 — Project Bootstrap (detailed) [DONE]

**Goal**: a compiling, tested, CI-gated solution with engineering standards and a minimal Core.

**Prerequisites**: none.

**Tasks**
1. `dotnet --info`; pin installed .NET 9 SDK in `global.json` (`rollForward: latestFeature`).
2. Create `Directory.Build.props` with shared properties (TargetFramework, nullable, implicit
   usings, langversion, deterministic, warnings-as-errors, analyzers, source-link optional).
3. Create `Directory.Build.targets` (shared test/coverage hooks if needed).
4. Create `Directory.Packages.props` (central package management; pin xUnit, analyzers, etc.).
5. Create `.editorconfig`, `.gitignore`, `README.md`, `LICENSE` (placeholder until decided),
   `CONTRIBUTING.md`, `SECURITY.md`.
6. Create `XGrid.sln` and the project skeleton:
   - `src/XGrid.Core`, `src/XGrid.Cpu`, `src/XGrid.Memory`, `src/XGrid.Bus`, `src/XGrid.Mcpx`,
     `src/XGrid.Nv2a`, `src/XGrid.Audio`, `src/XGrid.Storage`, `src/XGrid.XboxFs`, `src/XGrid.Xbe`,
     `src/XGrid.Input`, `src/XGrid.Graphics`, `src/XGrid.Debug`, `src/XGrid.Host`,
     `src/XGrid.Cli`.
   - `tests/XGrid.Core.Tests`, `tests/XGrid.Cpu.Tests`, `tests/XGrid.Memory.Tests`,
     `tests/XGrid.Integration.Tests`.
   - `benchmarks/XGrid.Benchmarks` (BenchmarkDotNet).
7. Implement the minimal Core surface: `EmulatorState` enum, `IEmulator` interface (plan 6.1),
   `IEmulatedDevice` (plan 6.3), `IXboxDevice` (plan 10), and a structured `XGridLog`/category
   skeleton (plan 33).
8. Create `.github/workflows/ci.yml`: restore, build (Release), test, `dotnet format
   --verify-no-changes`, analyzer enforcement, on Windows/Linux/macOS.
9. Create `scripts/build.ps1`, `scripts/test.ps1`, `scripts/format.ps1` (and `.sh` equivalents).
10. Add a first `XGrid.Core.Tests` test that fails if the lifecycle enum/contract changes.

**Interfaces (initial)**
```csharp
public enum EmulatorState { Created, Initialized, Running, Paused, Stopped, Faulted }
public interface IEmulator {
    EmulatorState State { get; }
    void Initialize(); void Reset(); void Start();
    void Pause(); void Resume(); void Stop();
}
public interface IEmulatedDevice { void Reset(); void Tick(ulong cycles); }
public interface IXboxDevice { string Name { get; } void Reset(); }
```

**Acceptance**
- `dotnet build -c Release` succeeds on Windows/Linux/macOS in CI.
- `dotnet test -c Release` succeeds with zero failures.
- `dotnet format --verify-no-changes` succeeds.
- No frontend/UI reference from Core/Cpu/Memory/Bus/devices.

**Risks**: installer/SDK availability; analyzer churn. Mitigation: pin SDK, start with a small
analyzer set and tighten later.

## Phase 1 — Emulator Core (detailed) [DONE]

**Goal**: deterministic lifecycle, cycle clock, device scheduler, no frontend dependency.

**Tasks**
1. `XGrid.Core`: `Emulator` implementing `IEmulator` state machine with explicit transitions and
   invalid-transition guards (throw `InvalidOperationException` only for developer errors).
2. `EmulatedClock` (plan 6.2): cycle counter, host-time mode, deterministic fixed-step mode;
   single source of emulated time used by all devices.
3. `Scheduler` / event queue (plan 6.3): device registration, coarse `Tick(cycles)` first; design
   for later event-driven scheduling; deterministic ordering by registration + event time.
4. `XGrid.Core.Diagnostics`: category logger (plan 33) with source-generated, allocation-free
   `LoggerMessage`-style methods; categories CPU/MEMORY/MMIO/PCI/MCPX/NV2A/APU/USB/IDE/FATX/
   XDVDFS/XBE/KERNEL/INPUT/AUDIO/VIDEO/JIT.
5. `Configuration` model + loader (plan 34): system/bios/mcpx/hdd, graphics backend/resolution,
   cpu backend, audio, input; validated with clear errors (`InvalidFirmwareException` etc.).
6. Deterministic mode flag end-to-end.

**Tests**: determinism (two runs identical state after N cycles); invalid transition matrix;
scheduler ordering; clock arithmetic; config parse round-trip.

**Acceptance (plan 6)**: init/reset deterministic; fixed-cycle state reproducible; no frontend
dependency.

## Phase 2 — Memory System (detailed) [DONE]

**Goal**: correct physical RAM, endianness, memory map, MMIO routing, RAM fast path.

**Tasks**
1. `XGrid.Memory`: `PhysicalMemory` (64/128 MB flat `byte[]`) with `Read8/16/32/64`,
   `Write8/16/32/64`, `Span<byte> GetSpan(uint, int)`; bounds-checked with deterministic behavior.
2. `Endian` helpers: explicit little-endian reads/writes in a `MemoryMarshal`-based static class.
3. `AddressSpace`: region table with `(base, size, read, write, priority)`; RAM fast path
   (range check → direct array); MMIO slow path; aliases/mirrors for RAM (0x0C000000), flash
   mirrors (0xFF000000), MCPX ROM overlay (0xFFFFFE00), PCI hole alias.
4. `IMmioDevice` (plan 7.4) + registration API.
5. Invalid-address policy: deterministic (configurable: fault/log/sentinel), documented.
6. `XGridMemoryTool` (plan tools) later; for now expose an inspection API used by the debugger.

**Facts**: use the consolidated address map in `docs/research-notes/xgrid-architecture-decisions.md`,
validated against XboxWiki `References/XboxWiki/22.mhtml` (Memory): 64 MB RAM 0x00000000-0x03FFFFFF,
NV2A regs 0xFD000000-0xFDFFFFFF, APU regs 0xFE800000-0xFE87FFFF (512 KiB; `ApuSize` corrected
2026-10-10), ACI regs 0xFEC00000-0xFEC00FFF (`IoApicBase` was a misnomer; renamed `AciBase`
2026-10-10 — the window is ACI MMIO), USB0 0xFED00000-0xFED00FFF, USB1 0xFED08000-0xFED08FFF, nvnet
0xFEF00000-0xFEF003FF, flash 0xFF000000-0xFFFFFFFF, MCPX ROM 0xFFFFFE00-0xFFFFFFFF.

**Tests**: read/write each width; unaligned access policy; endianness; MMIO routing; alias
correctness; invalid addresses; RAM benchmark harness.

**Acceptance (plan 7)**: memory tests pass; MMIO routing works; RAM benchmark exists; invalid
addresses deterministic.

## Phase 3 — x86/Pentium III CPU (detailed) [DONE]

**Goal**: table-driven interpreter passing an extensive instruction regression suite.

**Tasks**
1. `XGrid.Cpu`: `XboxContext` (all registers in plan 8.1), `Eflags`, segment/descriptor state,
   CR/DR, FPU/MMX/SSE state.
2. `Decoder`: prefix/opcode/ModRM/SIB/immediate decoders, address/operand-size handling;
   `DecodedInstruction` (plan 8.3); no external decoder dependency.
3. Interpreter tiers in order (plan 8.4): T1 NOP/MOV/PUSH/POP/XCHG/LEA; T2 arithmetic/logic;
   T3 JMP/CALL/RET/Jcc/LOOP/INT/IRET; T4 shifts/rotates; T5 MUL/DIV; T6 strings; T7 system;
   T8 x87; T9 MMX; T10 SSE.
4. Exact flags: CF/PF/AF/ZF/SF/OF/DF/IF/TF (plan 8.5). Eager first; lazy only if profiled.
5. Exceptions (plan 8.6) through a guest IDT model: #DE, #UD, #GP, #PF, #BP, #DB; host exceptions
   never leak.
6. CPU test framework (plan 8.7): JSON instruction tests + an assembly-based golden harness
   (write our own NASM files using the `harness.inc` convention; do not copy Xermu's tests).
7. `XGrid.CpuTestRunner` tool.

**Facts**: PIII CPUID/MSR quirks in `docs/research-notes/xemu.md` section 2.

**Tests**: per instruction family, flags, exceptions, addressing modes, prefixes, FPU/MMX/SSE.

**Acceptance (plan 8)**: interpreter passes the instruction regression suite before boot work.

## Phase 4 — x86 MMU (detailed) [DONE]

**Goal**: segmentation as required, paging, TLB, correct page faults.

**Tasks**: page directory/tables (4 KB and 4 MB), CR0/CR3/CR4 handling, A/D bits, permission
checks, TLB with invalidation on CR3/INVLPG, page-fault error codes, virtual-address execution.
Synthetic page-table tests and TLB invalidation tests; CPU executes code through virtual addresses.

**Acceptance**: page-table tests pass; faults correct; TLB invalidation tested.

## Phase 5 — Xbox Bus & Interrupts (detailed) [PARTIAL - I/O, PCI, LPC, PIC, PIT, RTC, USB/XID, nvnet MMIO done; DMA helper, ELCR, Super-I/O, GPE pending]

### PCI device topology (verified against xemu `hw/xbox/xbox.c`)

Process directive: **consult the reference projects first** (`References/xemu-0.8.136`,
`References/xqemu-master`, `References/Cxbx-Reloaded-master`, `References/Xermu-main`) to
confirm hardware layout, IDs, and behavior before implementing or debugging a device. Xemu is
the LLE reference; Cxbx/Xermu are HLE (kernel reimplementation, no NV2A emulation) and only
help for kernel-facing behavior.

| Slot | Device (vendor:device, revision) | xemu source | XGrid status |
| --- | --- | --- | --- |
| 0:0.0 | Xbox PCI host bridge (10DE:02A5, rev A1, multi-function) | `xbox_pci.c` | done |
| 0:0.3 | Memory-controller stub (xemu: QEMU `pci-testdev` placeholder) | `xbox_pci.c` | not emulated |
| 0:1.0 | MCPX LPC (10DE:01B2, rev B2, multi-function) | `xbox_pci.c` | done |
| 0:1.1 | nForce SMBus (10DE:01B4, rev B1) | `xbox_pci.c` | done |
| 0:2.0, 0:3.0 | nForce OHCI USB (10DE:01C2, rev B1, class 0C0310, BAR0 0x100) | `hcd-ohci-pci.c` | config space + MMIO schedule engine/XID (§36, §54) |
| 0:4.0 | nvnet Ethernet (10DE:01C3, rev B1, BAR0 0x400 + IO 0x8) | `nvnet.c` | config space + MMIO (§52) |
| 0:5.0 | MCPX APU (10DE:01B0, rev B1, BAR0 0x80000) | `apu.c` | config space + MMIO (§33) |
| 0:6.0 | MCPX ACI (10DE:01B1, rev B1, BAR2 0x1000) | `aci.c` | config space + MMIO (§33) |
| 0:9.0 | Xbox IDE (10DE:01BC, rev B1, IRQ 14) | `piix.c` (`#ifdef XBOX`) | done |
| 0:30.0 | AGP bridge (10DE:01B7, rev A1); secondary bus 1 | `xbox_pci.c` | done |
| bus 1, 0:0.0 | NV2A (10DE:02A0, rev A1, BAR0 16 MB, BAR1 64 MB prefetchable) | `nv2a.c` | config space + full PFIFO/PGRAPH model (M5–M8) |

Bus routing: configuration cycles to bus N are forwarded only to the bridge whose programmed
secondary bus number (register 0x19) equals N. Unclaimed buses return all-ones (no aliasing).

Status after this step: the PCI stubs and secondary-bus routing are in place, and the IDE ID now
matches xemu's Xbox variant. The 2BL still idles at the same scheduler loop (100M instructions,
IDE accesses 0, interrupts 108). Diagnostics added: `AddressSpace.UnmappedAccessCounts` (1 MB
buckets, printed by CLI) and `PciBus.ConfigAccessCounts` (per bus+devfn, printed by CLI).

SMC comparison (`hw/xbox/smbus_xbox_smc.c` vs our `SmcDevice`): register maps match (version,
tray, AV pack, error, INTSTATUS clear-on-read, non-zero challenge bytes 0x1C-0x1F, ignored
0x20/0x21 handshake writes). Two minor deltas, no code change: POWER register (0x02) writes
have no effect in ours (no reset plumbing), and xemu sets TRAYCLOSED intstatus + asserts SCI at
realize while ours starts with intstatus 0 (IRQ9 is masked by the 2BL anyway).

Measured at 100M instructions: the 2BL enumerates every PCI function — host 10, memctl-slot 5
(all-ones, not emulated), LPC 20, SMBus 3, USB0/USB1 139 each, nvnet 128, APU 128, ACI 130,
IDE 128, AGP 134, bus-1 NV2A 133 (so the 2BL programs the AGP secondary bus itself). NV2A MMIO
gets 30 one-time probes (FD0=28, FD1=2); USB/APU/nvnet MMIO is never touched, so no driver polls
a status bit. Above-RAM probes (045=4, 065=8) look like one-time memory-size detection. The 2BL
sees all devices and then parks: the wait is for a runtime event, not a missing function.
SMC transaction log (`SmcDevice.ReadCounts/WriteCounts`, printed by CLI) at 100M: R01=1, R1C-R1F=1
(challenge request), R04=1 (AV pack), W01=2, W12=1, W13=1, W20=1, W21=1 (challenge handshake
writes, ignored as in xemu). No reads of tray state (0x03), error (0x0F), or INTSTATUS (0x11):
the 2BL is not waiting on the SMC either. Total: 529 tests pass.
PCI state line (CLI) at 100M: USB0/USB1/nvnet/APU/NV2A all cmd=0007 (IO+MEM+bus master) with
BARs assigned (USB 0xFED00000/0xFED08000, nvnet 0xFEF00000, APU 0xFE800000, NV2A 0xFD000000);
ACI BAR0 shows the 2BL's IO probe write (0xD001) since xemu registers no BAR0/1 there. IDE is
cmd=0005 (legacy IO + bus master, no MEM) with BARs unassigned — yet the legacy ports are never
touched, so storage init never starts. LPC routing registers are untouched defaults
(introute=0E065491, pirq=00031000), so every device IRQ is effectively masked. Drivers are
active but their MMIO is never read back and no interrupt can arrive: the boot thread is parked
before storage init, waiting on an event nothing posts.
Next: trace the timer ISR (vector 0x30 -> 0x8001B180 -> dispatcher 0x8001B9F0 -> C handler
0x8001CA64) to see what it does with each tick.
Done (1.5M-instruction trace + `--dump` thread decode): the running ISR is at **0x80030E4C**
(0x8001B180 never executes): EOI helper 0x80014F10, +1 ms tick accumulation, per-thread
KernelTime/Quantum accounting, IRET. DPC/timer-expiry branch never fires. Exactly one thread
exists (0x80035A90, Type=6, State=Running, KernelTime≈ticks, WaitBlockList=NULL); the
IdleThread/NextThread PRCB slots hold zeros/garbage. The wait is KiIdleLoop-on-empty-DPC-queue
with no waiter object: only a future device event unblocks it. Total: 530 tests pass.
Reframe from XboxWiki `References/XboxWiki/51.mhtml` (Boot Process): `--dump 0x80010000` shows
a valid MZ header, so the 2BL completed its whole program (MCPX ROM disable via LPC 0x80=0x02,
SMC challenge 0x1C-0x1F + response 0x20/0x21, kernel flash copy + RC4 + LZX to 0x80010000,
MZ/PE check, entry call) and we are in the KERNEL's idle loop. A 50M-instruction run shows
byte-identical device histograms to 100M (only tick/PIC-EOI counts differ): kernel cold-boot
init (PCI setup, SMC, Super-I/O debugger, RTC, driver BAR assignment) finishes before 50M and
the kernel parks with IDE and video-progress never started, matching the wiki's cold-boot list
minus its IDE/video items.
Next (kernel phase): map an NV2A MMIO region at 0xFD000000 so display init can proceed (the
wiki's PDE maps it cache-disabled, flags 0xFB; video likely precedes disk), then the
IDE+FATX+XBE dashboard path (Phases 8/10/11).

**Goal**: PCI enumeration, MMIO/IRQ infrastructure, timers, DMA, discoverable devices.

**Tasks**: PCI bus with config ports 0xCF8/0xCFC, BAR allocator, host bridge, AGP bridge; device
registry (`IXboxDevice`) with no central switch; IRQ lines + dual 8259 PIC with LPC routing
registers 0x64/0x68/0x6C; PIT 8254; ACPI PM timer (3.579545 MHz); RTC; DMA helper that reads and
writes guest RAM through `AddressSpace`.

**Facts**: IDs/routing in `docs/research-notes/xemu.md` sections 1 and 3.

**Acceptance (plan 10)**: devices register without modifying central switch statements.

## Phase 6 — MCPX (detailed) [PARTIAL - SMBus AMD756/EEPROM/SMC/encoder/ACI/APU + real MCPX ROM boot done; flash command set, GPE/GPIO, Super I/O pending]

**Goal**: boot firmware can initialize MCPX-dependent functionality.

**Order**: SMBus (AMD756) → EEPROM → Flash → SMC → USB integration → interrupts → timers.

**Tasks/facts**: SMBus register map (`xemu.md` 4.1); EEPROM layout + SHA1/RC4 security section and
generation (`xemu.md` 4.3); flash AMD/SST command set (`xemu.md` 4.7, `xermu.md` 4); SMC register
and challenge/response map (`xemu.md` 4.2); ACPI PM/GPE/GPIO (`xemu.md` 4.5); Super I/O programming
model (`xemu.md` 4.6); ACI AC'97 (`xemu.md` 4.9).

**Tests**: per-device register/state tests (SMBus transaction, EEPROM read/write/wrap, flash
program/erase, SMC challenge, ACPI timer).

**Acceptance (plan 11)**: boot firmware initializes MCPX functionality.

## Phase 7 — Firmware & Boot (detailed) [PARTIAL - machine/overlay/CLI done; real boot through menu-idle done (§§31–61); ATA SECURITY/XBE-loader guest-side items pending]

**Goal**: execute real user-supplied firmware far enough to prove CPU/memory/basic hardware.

**Tasks**: BIOS loader (256 KiB/1 MiB, mirroring), MCPX 512-byte ROM overlay + disable semantics,
reset vector 0xFFFFFFF0, boot pipeline (plan 12), stage logging (EIP/CR0/CR3/EFLAGS/interrupts/
MMIO/PCI/page faults), `--deterministic`, milestone M1 trace. Keep nboxkrnl-style guest-kernel
path and HLE as later fallbacks.

**Acceptance**: M1 — C# CPU executes real firmware with correct basic hardware behavior.

**Findings (XboxWiki `References/XboxWiki/51.mhtml`, Boot Process)**: the 2BL program is fully
documented (MTRR, self-copy, PDE incl. GPU window flags 0xFB, MCPX ROM disable, SMC challenge,
IDE+NIC enable, LDT config, kernel RC4+LZX to 0x80010000, MZ/PE check, entry call) and our boot
matches it end to end — kernel MZ verified in RAM, so the paragraphs below about the 2BL
fatal path are superseded. We are in the kernel idle loop (research note 18); M1 holds through
the kernel handoff, and further progress is kernel-phase work (NV2A MMIO, IDE+FATX+XBE).

## Phase 8 — Storage (detailed)

**Goal**: access a real Xbox filesystem through emulated storage.

**Tasks**: IDE controller (model NVIDIA nForce if feasible; PIIX3-compatible acceptable first),
ATA commands (IDENTIFY, READ/WRITE, SET FEATURES, FLUSH, security IDENTIFY), PIO then bus-master
DMA; virtual HDD backed by random-access file with block cache/read-ahead; ATAPI DVD with
2048-byte sectors for XDVDFS; FATX read-only first (superblock/partition/FAT/dirs/files); host
tooling via XISOSharp adapter behind `IDiscImage`.

**Facts**: `cxbx.md` 6, `xisosharp.md` 2, `xemu.md` 7; XboxWiki `References/XboxWiki/53.mhtml`
(FATX) for the filesystem stage. Note: the boot kernel loads from flash (XboxWiki Boot Process,
page `51`), so disk I/O belongs to the dashboard phase, not firmware boot.

**Acceptance**: M2 — emulator accesses a real Xbox filesystem through emulated storage.

## Phase 9 — XBE Loader (detailed)

**Goal**: parse and map an XBE into emulated memory.

**Tasks**: header/certificate/section/library/TLS parsing (offsets in `cxbx.md` 2), XOR entry
point/kernel thunk decode per type, section mapping with shared-page head/tail semantics, image
base, entry point; `XbeImage` type (plan 14); `XGrid.XbeTool` with `info/sections/imports/extract`.

**Acceptance**: M3 — XBE parsed and mapped into emulated memory.

## Phase 10 — Xbox Kernel Interface (detailed)

**Goal**: minimum kernel services to boot known test software; trace-driven.

**Tasks**: declarative ordinal table (name, arg count, calling convention, data/function) derived
from `cxbx.md` 3; implement categories incrementally (memory, threads, events, IRQs, filesystem,
object manager, timers, device IO); strategy: Run → unimplemented call → trace → implement →
regression test. Real-kernel boot preferred; HLE fallback.

**Acceptance**: sufficient kernel compatibility for target test software.

## Phase 11 — NV2A (detailed)

**Goal**: render known test scenes; second major high-risk subsystem.

**Tasks**: device + block table (`xemu.md` 5.2 / `cxbx.md` 4.1); PFIFO channels/pushbuffers/DMA
objects/RAMHT/method dispatch; PGRAPH state shadow + classes + NV097 methods; surfaces/textures/
vertex/render state; memory handling (surfaces, textures, framebuffers, depth); `INv2aRenderer`
intermediate representation carrying `ShaderState`; Vulkan backend primary, OpenGL optional;
precompiled interpreter ubershaders.

**Acceptance**: M4 framebuffer → M5 2D → M6 3D → M7 shaders → M8 game rendering.

**Facts (XboxWiki)**: `References/XboxWiki/16.mhtml` (NV2A MMIO/pipeline) plus `17`-`21`
(vertex attributes, fixed-function pipeline, vertex shader, pixel combiner, surface formats).
Guest page tables map the 16 MB control window cache-disabled (Boot Process `51`); BAR0 base
observed 0xFD000000 during real boot.

## Phase 12 — APU (detailed)

**Goal**: audio initialization and correct waveform.

**Tasks**: device registers, voices, DMA/audio buffers, mixing (32 float mixbins), DSP behavior
(GP/EP), resampling, host output behind `IAudioBackend`; implement DSP semantics independently
(reference implementations are GPL). Long-run drift test.

**Acceptance (plan 17)**: init works; known test waveform correct; no excessive drift.

## Phase 13 — USB & Controllers (detailed)

**Goal**: four controllers with rumble.

**Status**: OHCI host (SOF/HCCA/IRQ, §36), USB schedule engine
(control/bulk/periodic, done-queue + WDH, STALL/NAK, §54) and XID
controller protocol (descriptors/reports/capabilities per Cxbx facts)
DONE — guest enumerates first try (§55); `--pad` injection latched
(§56). Remaining: host input behind `IInputBackend`, rumble output,
pads 2–4, isochronous transfers.

**Facts (XboxWiki `References/XboxWiki/41.mhtml`, USB)**: two MCPX OHCI controllers, MMIO per
standard OHCI spec Table 7-1; USB0 = PCI 0:2.0 IRQ1 (retail game-controller ports), USB1 =
PCI 0:3.0 IRQ9 (not enabled in retail titles). Firmware values (retail v1.6 PAL):
HcRevision=0x10 both; USB0 HcRhDescriptorA=0x01001204 (4 ports), USB1 =0x01000202 (2 ports);
HcRhDescriptorB DeviceRemovable=0, PortPowerControlMask=0. Front-panel ports 1,2,3,4 map to
USB hardware ports 3,4,1,2. Our stub slots/IRQs/BARs already match; MMIO + SOF interrupt is
the unblock candidate for the kernel idle wait.
**Implementation deltas (2026-10-11, §§54–56):** both controllers report
4 root ports (xemu `xbox.c` `num-ports=4`) with NPS (no power switching:
attached function reads connected without guest power writes); gamepad
on USB0 port 0 only; USB1 never touched by the guest (`acc=0` all runs).
OPEN: wiki says USB1 has 2 ports (`0x01000202`) — our 4-port report is
unvalidated against real hardware; front-panel mapping untested.

## Phase 14 — Networking (detailed)

**Goal**: minimum networking for games, only after core gameplay works.

**Status**: nvnet register/MMIO model DONE (`McpxNvNetDevice` at
`0xFEF00000`: idle TX/RX, link-up, instant MDIO/PHY, qemu-default MAC;
xemu `nvnet_regs.h`-faithful; §52) and validated live (FEF unmapped
collapse, init succeeds first try, live render loop; §53). gate67
proved init-only (§62: 44/350 per-reg, zero ring/KICK/poll) — the
packet engine stays scoped-not-built per the trace-driven rule.
Remaining (when a title drives it): descriptor/DMA packet engine
(`RingDesc` `{u32 buf; u16 len; u16 flags}`, rings `0x100`–`0x138` per
xemu `nvnet.c`), RX/TX interrupts, link-state/DHCP behavior, all behind
`INetworkBackend`.

## Phase 15 — CPU JIT (detailed)

**Goal**: production CPU backend after the interpreter is stable and remains the reference.

**Tasks**: decoder → basic block → IR (plan 20.2) → optimizer → host emitter → code cache;
block keying and invalidation; evaluate managed options in plan 20.6 order; benchmark-driven.
Adopt proven concepts from `xermu.md` 7: trace cache keyed on guest VA, forward-only linking,
page-protection SMC, per-block invalidation.

## Phase 16–18 — Optimization (detailed)

- CPU (16): decode, memory, branches, flags, block cache, register allocation, indirect branches,
  MMIO checks, TLB, synchronization — in that order, benchmarked.
- Memory (17): direct RAM array, unsafe refs, spans, `MemoryMarshal`, fixed pages, TLB cache,
  read/write helpers, branch reduction, host virtual mapping where justified.
- GPU (18): measure command generation, submission, shader compilation, texture upload, render
  pass creation, synchronization, frame pacing; persistent resources, descriptor/pipeline/texture
  caches, command-buffer reuse, async upload.

## Phase 19 — Debugger (detailed)

CPU (registers/flags/IP/disassembly/step/step-over/continue/breakpoints/watchpoints), memory
(hex/search/read-write/mapped regions), GPU (NV2A registers/PGRAPH state/texture/framebuffer/
pushbuffer trace), storage (FATX/XDVDFS browsers), trace (CPU/MMIO/PCI/NV2A/kernel/filesystem).

## Phase 20 — Automated Testing & Fuzzing (detailed)

Unit tests per component; CPU instruction tests; memory alignment/paging/TLB/page-fault/MMIO/cache;
device tests; golden traces (plan 25.5); differential testing vs xemu where legal (plan 26); GPU
differential images/heatmaps (plan 27); compatibility matrix (plan 28); fuzz targets for x86
decoder, XBE, XDVDFS, FATX, ISO, NV2A push-buffer, MMIO decoder (plan 44).

## Phase 21 — CLI & Frontend (detailed)

`XGrid.Cli` commands (plan 35); Avalonia frontend (plan 36) communicating only through a
public API; performance overlay, logs, debugger, configuration.

## Phase 22 — Save States & Replay (detailed)

Deferred (plan 37/38). When implemented: serialize CPU/RAM/MMU/device/GPU/APU/timers/interrupts/
storage with mandatory versioning and checksums; snapshot/replay records initial state + inputs +
timing events.

## Phase 23 — Release (detailed)

Windows/Linux/macOS builds, packages, documentation, licensing review, firmware/user-data
documentation, performance documentation (plan 47 M16, section 48).

## Detailed repository tree (target)

```text
XGrid/
|-- XGrid.sln
|-- Directory.Build.props / Directory.Build.targets / Directory.Packages.props / global.json
|-- .editorconfig / .gitignore / README.md / LICENSE / CONTRIBUTING.md / SECURITY.md
|-- .github/workflows/ci.yml
|-- docs/            (architecture/, cpu.md, memory.md, nv2a.md, mcpx.md, storage.md, audio.md,
|                     input.md, boot.md, debugging.md, performance.md, compatibility.md,
|                     research-notes/)
|-- src/
|   |-- XGrid.Core/ XGrid.Cpu/ XGrid.Memory/ XGrid.Bus/ XGrid.Mcpx/ XGrid.Nv2a/ XGrid.Audio/
|   |-- XGrid.Storage/ XGrid.XboxFs/ XGrid.Xbe/ XGrid.Input/ XGrid.Graphics/ XGrid.Debug/
|   |-- XGrid.Host/ XGrid.Frontend.Avalonia/ XGrid.Cli/
|-- tests/
|   |-- XGrid.Core.Tests/ XGrid.Cpu.Tests/ XGrid.Memory.Tests/ XGrid.Bus.Tests/
|   |-- XGrid.Mcpx.Tests/ XGrid.Nv2a.Tests/ XGrid.Audio.Tests/ XGrid.Storage.Tests/
|   |-- XGrid.XboxFs.Tests/ XGrid.Xbe.Tests/ XGrid.Integration.Tests/
|   |-- XGrid.Compatibility.Tests/ XGrid.Performance.Tests/
|-- tools/           (XGrid.CpuTestRunner/ XGrid.XbeTool/ XGrid.MemoryTool/ XGrid.TraceTool/
|                     XGrid.Nv2aTool/)
|-- benchmarks/      (CpuBenchmarks/ MemoryBenchmarks/ XdvdfsBenchmarks/ Nv2aBenchmarks/
|                     EndToEndBenchmarks/)
|-- test-data/       (cpu/ xbe/ xdvdfs/ fatx/ nv2a/ audio/ integration/)
|-- scripts/         (build/test/benchmark/format .ps1 and .sh)
```

