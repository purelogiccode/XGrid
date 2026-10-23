# Third-party notices and porting provenance

XGrid is distributed under the GNU General Public License, version 2
(`COPYING`).  This file records the reference projects used for facts and for
ported source code, and the license of each.  A ported file carries its own
SPDX identifier and the original copyright notice at the top of the file.

`References2/` is a development-only reference tree.  It is not part of the
XGrid build and is not redistributed with XGrid.

## Reference projects

| Project | Version / snapshot | Upstream | License | Use in XGrid |
|---|---|---|---|---|
| xemu | **0.8.139 (primary)** | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and ported source. The APU DSP port is derived from `hw/xbox/mcpx/apu/dsp/` (GPL-2+). |
| xemu | 0.8.137 (prior) | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and cross-checks. |
| xemu | 0.8.136 (prior) | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and cross-checks. |
| QEMU (xemu base) | 10.2.0 (via xemu 0.8.139) | https://www.qemu.org | GPL-2.0; some files GPL-2.0-or-later | Facts and ported source where a module is taken from the QEMU base. QEMU is not embedded as a native dependency. |
| xemu-Playtest-v2 | local snapshot, QEMU 10.2.0 base | no upstream URL recorded | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and cross-checks only. |
| Cxbx-Reloaded | master | https://github.com/Cxbx-Reloaded/Cxbx-Reloaded | GPL-2.0 (some files LGPL-2) | Facts and, where useful, ported source. |
| xqemu | master | https://github.com/xqemu/xqemu | GPL-2.0 (mixed per-file) | Facts and, where useful, ported source. |
| envytools | master | https://github.com/envytools/envytools | MIT | Facts, register/method tables, GPU docs. |
| nv2a-trace | master | https://github.com/XboxDev/nv2a-trace | MIT | Facts, pushbuffer decoder, texture-format tables. |
| XboxDevWiki | offline dump 2026-10-12 | https://xboxdevwiki.net | No license declared | Facts only. |
| Copetti, "Xbox Architecture" | local snapshot | https://www.copetti.org/writings/consoles/xbox/ | CC BY 4.0 (attribution required) | Documentation orientation only. |

## Runtime and build dependencies

| Package | License | Use |
|---|---|---|
| XISOSharp | MIT | XDVDFS reader (`XGrid.XboxFs`). |
| CHDSharp | MIT | CHD image support (`XGrid.Storage`). |
| Silk.NET.OpenGL | MIT | OpenGL backend (`XGrid.GpuGl`). |
| Avalonia (Desktop/Fluent/Inter) | MIT | Desktop frontend. |
| CommunityToolkit.Mvvm | MIT | Desktop frontend MVVM. |
| Iced | MIT | x86 encoder used by the raw JIT. |
| xunit / xunit.runner.visualstudio | Apache-2.0 | Test projects only; not part of the emulator runtime. |
| Meziantou.Analyzer | MIT | Build-time analyzer. |
| Roslynator.Analyzers | Apache-2.0 | Build-time analyzer. |
| BenchmarkDotNet | MIT | Benchmarks. |
| coverlet.collector | MIT | Coverage. |
| Microsoft.NET.Test.Sdk | MIT | Test projects only. |

The Apache-2.0 packages are build/test tooling and are not linked into the
distributed emulator binaries.  Their notices remain in the package sources.

## Porting log

| XGrid module | Source | License | Status |
|---|---|---|---|
| `src/XGrid.Audio/Dsp/DspDma.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/dsp_dma.c` + `dsp_dma.h` | LGPL-2+ (xemu file header) | Ported. |
| `src/XGrid.Audio/Dsp/Dsp56300State.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.h` + `dsp_cpu_regs.h` | GPL-2+ | Ported state/constants. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | GPL-2+ | Core state, reset, memory, 56-bit arithmetic; the instruction interpreter is the next slice. |
| `src/XGrid.Audio/Dsp/Dsp56300Arithmetic.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | GPL-2+ | Ported 56-bit arithmetic helpers. |

When a module is ported, add a row here and a matching entry in
`docs/research-notes/sources.md`.
