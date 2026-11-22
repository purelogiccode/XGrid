# XGrid porting policy (GPL-2.0, full C#)

Status: **2026-10-23, operator decision.**  XGrid is GPL-2.0 and may port code
from GPL-compatible reference projects.  This supersedes the earlier
clean-room-only policy.  `AGENTS.md`, `README.md`, `docs/research-notes/sources.md`
and `THIRD_PARTY_NOTICES.md` are the binding records.

## 1. License

- XGrid is distributed under **GPL-2.0**; the full text is `COPYING`.
- A ported file keeps its original copyright notice and license.  Add
  `// SPDX-License-Identifier: <original>` at the top and a
  `// Ported from <project> <version> <path>` line below it.
- Files that are GPL-2.0-or-later may be included under GPL-2.0; files that are
  GPL-2.0-only stay GPL-2.0-only.  Do not relicense a ported file.  LGPL-2.0-or-later
  files are included under GPL-2.0 through LGPL-2.0 section 3, as `LICENSE` states.
- The project as a whole is GPL-2.0.  A distribution that contains a
  GPL-2.0-only file is therefore GPL-2.0, not GPL-3.0.
- Update `THIRD_PARTY_NOTICES.md` and `docs/research-notes/sources.md` in the
  same change as the port.

## 2. Full C#

- The emulator, devices, GPU and audio stay **managed C#**.
- The **CPU core is the single native exception** (§2b): the vendored
  xemu/QEMU TCG i386 core behind `libxgridcpu`. No QEMU machine, device,
  block layer or UI is added to the build.
- Do not embed a second emulator or ship any other native emulator library.
- Port the algorithm to C# and keep the existing interfaces (`IGpuMemory`,
  `AddressSpace`, `IMmioDevice`, `IAudioBackend`, ...).  A line-by-line C
  translation is usually slower to integrate than a port that fits the existing
  seam.
- Existing native *APIs* (OpenGL through Silk.NET, XInput, host audio) are
  allowed; they are host APIs, not an embedded emulator.

**C# first (2026-11-22).** Managed C# is the default for every emulator
semantic. P/Invoke or unmanaged code is allowed only when a pure-C# path would
be materially worse or does not exist (a host GPU API, the OS input stack,
executable memory for the raw JIT), and every native edge is inventoried below
with its reason. A new P/Invoke needs a row and a reason in the same commit.
P/Invoke is **never** the way to embed QEMU/xemu or to call emulator code.

| Native edge | Where | Why it is allowed |
|---|---|---|
| OpenGL + WGL | `XGrid.GpuGl` (`Silk.NET.OpenGL`, `WglContext`) | Host GPU API; there is no managed GPU API. The software rasterizer stays the exact oracle. |
| XInput | `XGrid.Input/XInputGamepadReader` (`NativeLibrary` + function pointer) | Host gamepad API; the pure-managed keyboard pad and `XPadMapping` remain the fallback. |
| Executable memory + unmanaged block entry | `XGrid.Cpu/RawState`, `RawHelpers`, `RawBlockEmitter` (`kernel32` VirtualAlloc/Free, `[UnmanagedCallersOnly]`, `delegate* unmanaged`) | The opt-in raw JIT emits and runs x86-64 blocks. The managed IL JIT (`--jit`) is the pure-C# engine and the fallback. |
| UI rendering | Avalonia (managed API, native Skia/HarfBuzz inside the package) | UI framework choice, not emulator semantics; **kept by operator decision 2026-11-22**. The CLI has no UI dependency. |
| Host audio | `WaveAudioBackend` (WAV file, managed); a live sink via a NuGet package (P1 sprint: **NAudio**, MIT/WASAPI) | The emulator side (`IAudioBackend`) is managed; the sink is a host facility. |

### 2b. The native CPU core (XCN0-XCN3, 2026-11-22)

The operator decision of 2026-11-22 makes the vendored xemu 0.8.139 / QEMU
10.2.0 TCG i386 core the product CPU. It is the **one** exception to the
managed-C# rule, and it is bounded:

- Build from the pinned `References2/xemu-0.8.139` tree with the recorded
  `native/qemu-patches/`; a release build never fetches QEMU from the network.
- Ship only the CPU, softmmu/TLB, TB cache and the memory-dispatch subset behind
  `libxgridcpu`; never a QEMU machine, device model, block layer, UI, audio or
  network backend.
- The C# side owns guest RAM (zero copy), MMIO/PIO dispatch, interrupts,
  snapshots and every device model.
- `--cpu managed` stays selectable and remains the gate engine; `--cpu native`
  is the product engine. One CPU core per run.
- The GPL-2.0 source offer, the patches and the notices are part of every
  release. The build is inventoried in `THIRD_PARTY_NOTICES.md`; a new native
  edge needs a row and a reason in the same commit.
- The licence stays GPL-2.0; QEMU and xemu are the same licence family, so no
  relicense is needed (and a permissive relicense is not possible).

### 2a. NuGet packages (2026-11-22)

NuGet packages are **allowed and preferred** for a host facility (audio sink,
input, GPU binding, UI) over inventing it. This is the fast path to a playable
title. Rules:

- Record the package in `Directory.Packages.props`, `THIRD_PARTY_NOTICES.md` and
  the release licence review **in the same commit**.
- The licence must be compatible (MIT/Apache-2.0/BSD, or GPL-compatible); the
  package must **not** embed an emulator core or native emulator code.
- A host API binding is allowed; any native dependency it brings must be listed
  and shipped for every RID (the package gate covers it).
- Current host packages: Silk.NET.OpenGL (MIT), Avalonia (MIT),
  CommunityToolkit.Mvvm (MIT). P1 sprint candidate: **NAudio** (MIT, Windows
  WASAPI); an OpenAL/SDL2 backend is the cross-platform follow-up.

**There is no license wall.**  xemu is GPL-2.0 (per-file GPL-2+/LGPL-2+/MIT) and
the QEMU base is GPL-2.0, i.e. the same license family as XGrid.  Any bounded
module in `References2/` may be ported - devices, NV2A, APU, whatever the
task needs - subject only to the provenance rules (§1, §4), the managed-C#
rule above and the single CPU exception in §2b.  The former "facts only / clean-room" framing is
superseded; it must not be cited as a reason to avoid a port.

**Port before rewrite.**  When a GPL-compatible reference implements the module,
porting its structure is the default and a fresh design needs a reason (the
reference is wrong, unmaintained, or its shape does not fit the seam).  The DSP
port is the model: the reference's decoder and handler bodies were taken
verbatim in semantics, and the differential vectors prove it.

**Price a port like any other slice.**  A port is not free: it is read, adapted,
tested and kept in sync with the reference.  Large ports (the NV2A PGRAPH
draw path, a full device family) need a priced prototype or a measured
per-stage target before
the full port, exactly like a performance slice.  The license makes the port
*legal*; the plan still has to make it *worth doing*.

## 3. Allowed sources

| Source | Version | License | Use |
|---|---|---|---|
| xemu | **0.8.139** (primary) | GPL-2.0; per-file LGPL-2+/MIT/GPL-2+ | Port source and behavior oracle. |
| QEMU | 10.2.0 (xemu base) | GPL-2.0 | Port source for CPU/device algorithms where xemu does not own the code. |
| xemu | 0.8.137 / 0.8.136 | same | Prior snapshots; use 0.8.139 unless a regression needs the older behavior. |
| xemu-Playtest-v2 | local | same | Cross-check, tuned-GPU reference. |
| Cxbx-Reloaded | master | GPL-2.0 | Kernel/XBE/GPU behavior and port source. |
| xqemu | master | GPL-2.0 | Older/simpler device paths and port source. |
| envytools | master | MIT | Register/method tables and GPU docs. |
| nv2a-trace | master | MIT | Pushbuffer decoder, texture formats, hardware traces. |
| XboxDevWiki | offline dump | facts only | Documentation. |
| Copetti | local | CC BY 4.0 | Orientation only; attribution required. |

`References2/` is read-only and is not part of the build or the distribution.
Never copy firmware, BIOS, MCPX ROM, EEPROM or game data into the repository.

## 4. Porting workflow

1. **Pick one bounded module.**  One file or one coherent subsystem per change.
   Do not port a whole emulator in one commit.
2. **Write the C# seam first.**  Define the state, the memory/callback
   interfaces and the tests before translating instruction semantics.
3. **Port the state and helpers before the dispatcher.**  The DSP is the model:
   state/registers/memory first, DMA second, interpreter third.
4. **Keep the old path behind a flag until the new path passes.**  The existing
   gates must stay byte-identical while a port is in flight.
5. **Add a differential test.**  Prefer a test that fails on the old code or a
   known-good table from the reference.  A ported module needs at least one
   behavior test, not just a compile.
6. **Record the provenance.**  SPDX header, `Ported from ...` (or
   `Derived from ...`), notices table, sources table.  `scripts/package-verify.ps1`
   fails when a `src/**` file declares a port without either the SPDX line or the
   notices row, so the policy is checked, not remembered.
7. **Gate it.**  `dotnet build`, `dotnet test`, `dotnet format
   --verify-no-changes`; for emulator behavior, the 1B goldens and ktest smoke
   in `docs/research-notes/next-session.md`; for a release, `scripts/package.ps1`
   plus `scripts/package-verify.ps1`.

## 5. Priority order for ports

The immediate program is the **P1 sprint** (`ImplementationPlan.md` E1
P1S0-P1S3): one playable game fast, on the existing engine, with NuGet host
packages allowed. After P1, the program is the **full xemu 0.8.139 port**
(ledger `docs/xemu-port-manifest.md`), ordered by what unblocks a retail title:

1. **NV2A PGRAPH and the GL renderer** (`hw/xbox/nv2a/pgraph/**` plus the
   `nv2a/*` blocks) - XFP1-XFP5. This is the largest fidelity gap and the
   blocker for Halo, Ninja Gaiden Black and every visual row.
2. **APU front end + AC'97 codec** (`hw/xbox/mcpx/apu/apu.c`,
   `hw/xbox/mcpx/aci.c`, `hw/audio/ac97.c`) - XFP6. The VP mixer and the DSP
   are already ported; this makes the output audible.
3. **Storage/IDE, SMBus/SMC/EEPROM, nvnet, USB/OHCI/XID** - XFP7-XFP9. These
   own the documented waits (Bad Boys II, Destroy All Humans!) and the pad and
   save/load paths.
4. **Machine and chipset assembly** (`hw/xbox/xbox.c`, `xbox_pci.c`,
   `lpc47m157.c`, `acpi_xbox.c`, i8259/i8254/RTC) - XFP10.
5. **CPU and memory core** (`softmmu/`, `system/memory.c`, `accel/tcg/`,
   `tcg/`, `target/i386/tcg/`) - XFP11. In scope, sequenced last: the existing
   interpreter/raw JIT is bit-exact and faster than xemu on the CPU-bound
   ktest, so the port is gated on **not slower** than the current engine. If it
   is slower, keep the raw engine behind the same seam and port only the memory
   dispatch/decoder with the measured exception recorded in the ledger.
6. **Host backends and snapshots** (`ui/`, `audio/`, `input/`, `migration/`) -
   XFP12-XFP13.
7. **Parity close** - XFP14: ledger 100%, seven titles playable, release.

Vulkan (`nv2a/pgraph/vk/`) is a host-renderer choice and is out of scope for the
port; the GL renderer is the target. Chihiro, QEMU user-mode and QEMU
packaging/test infrastructure are out of scope with reasons in the ledger.

A port is a **correctness task**: the bar is the module's differential and the
title/corpus row, not wall clock. The performance bar applies only to XFP11's
"not slower" acceptance and to any later optimization task.

Do not start a port without its ledger row and its differential. The DSP port
is the model: state/helpers first, dispatcher second, behavior third, a vector
oracle that fails on the old code, then the gates.
