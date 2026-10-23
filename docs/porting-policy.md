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
  GPL-2.0-only stay GPL-2.0-only.  Do not relicense a ported file.
- The project as a whole is GPL-2.0.  A distribution that contains a
  GPL-2.0-only file is therefore GPL-2.0, not GPL-3.0.
- Update `THIRD_PARTY_NOTICES.md` and `docs/research-notes/sources.md` in the
  same change as the port.

## 2. Full C#

- The emulator, devices, CPU, GPU and audio stay **managed C#**.
- Do not add QEMU, xemu, Cxbx, xqemu or any C/C++ source to the build.
- Do not P/Invoke into QEMU/xemu or ship a native emulator library.
- Port the algorithm to C# and keep the existing interfaces (`IGpuMemory`,
  `AddressSpace`, `IMmioDevice`, `IAudioBackend`, ...).  A line-by-line C
  translation is usually slower to integrate than a port that fits the existing
  seam.
- Existing native *APIs* (OpenGL through Silk.NET, XInput, host audio) are
  allowed; they are host APIs, not an embedded emulator.

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
6. **Record the provenance.**  SPDX header, `Ported from ...`, notices table,
   sources table.
7. **Gate it.**  `dotnet build`, `dotnet test`, `dotnet format
   --verify-no-changes`; for emulator behavior, the 1B goldens and ktest smoke
   in `docs/research-notes/next-session.md`.

## 5. Priority order for ports

1. **APU GP/EP DSP** (xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/`) - host audio and
   real-game audio; the active P1.
2. **NV2A missing methods and shader semantics** (xemu 0.8.139 `pgraph/`) -
   compatibility breadth.
3. **APU VP mixer / PCM sink** (xemu 0.8.139 `apu/vp/`, `apu/apu.c`) - the
   output path that makes the DSP audible.
4. **QEMU device/CPU algorithms** only where xemu delegates to QEMU and the
   behavior is not already covered.

Do not start a CPU TCG port: XGrid's raw emitter is already bit-exact on both
gates and faster than xemu on the CPU-bound ktest.  The measured gap is the
boot/device/GPU path, not the core interpreter.
