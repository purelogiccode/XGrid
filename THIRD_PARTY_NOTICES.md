# Third-party notices and porting provenance

XGrid's own source is distributed under the GNU General Public License,
version 2 or later (`COPYING`, `LICENSE`).  The vendored xemu tree under
`xemu/` keeps its own per-file GPL-2.0, LGPL-2.0-or-later, LGPL-2.1-or-later
and MIT notices.  This file records the reference projects used for facts and
for ported source code, and the license of each.  A ported file carries its own
SPDX identifier and the original copyright notice at the top of the file.

`References2/` is a development-only reference tree.  It is not part of the
XGrid build and is not redistributed with XGrid.

## Reference projects

| Project | Version / snapshot | Upstream | License | Use in XGrid |
|---|---|---|---|---|
| xemu | **0.8.139 (vendored at `xemu/`)** | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | The maintained emulation core, tracked in `xemu/`. XGrid additions are in `xemu/xgrid-core/`; upstream edits are recorded in `xemu/xemu-patches/`. The APU DSP port is derived from `hw/xbox/mcpx/apu/dsp/` (GPL-2+). |
| QEMU (xemu base) | 10.2.0 (via xemu 0.8.139) | https://www.qemu.org | GPL-2.0; some files GPL-2.0-or-later | The xemu codebase, including its QEMU 10.2.0 base, is the maintained emulation core and is built from `xemu/`. QEMU is not linked as a separate library. |
| xemu-Playtest-v2 | local snapshot, QEMU 10.2.0 base | no upstream URL recorded | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | The Sega Chihiro machine under `xemu/hw/xbox/chihiro/`, the memory-backed block driver (`xemu/block/blkmemory.c`) and their integration are ported from this snapshot. Original copyright and license notices are kept per file; the edits to upstream xemu files are recorded in `xemu/xemu-patches/0018-core-chihiro.patch`. |
| XPSemu-Chihiro | alpha 2 | https://github.com/ZiZc3/XPSemu (unofficial fork) | GPL-2.0 overall; per-file LGPL-2+, BSD-3-Clause (MAME DES routines) | Cross-check and later Chihiro fixes; the ported files carry the notices of their original authors, including Réda Chérif-Touil and the MAME `naomigd.cpp` DES routines. |
| Cxbx-Reloaded | master | https://github.com/Cxbx-Reloaded/Cxbx-Reloaded | GPL-2.0 (some files LGPL-2) | Facts and, where useful, ported source. |
| xqemu | master | https://github.com/xqemu/xqemu | GPL-2.0 (mixed per-file) | Facts and, where useful, ported source. |
| xbased | 1.2.3 | https://github.com/crazyriddler/xbased | **GPL-3.0** | Facts and UI/architecture cross-check. Code may be ported only when the combined build is GPL-3.0-compatible; the build-scoped audit is pending, so no code is ported yet. |
| envytools | master | https://github.com/envytools/envytools | MIT | Facts, register/method tables, GPU docs. |
| nv2a-trace | master | https://github.com/XboxDev/nv2a-trace | MIT | Facts, pushbuffer decoder, texture-format tables. |
| XboxDevWiki | offline dump 2026-10-12 | https://xboxdevwiki.net | No license declared | Facts only. |
| Copetti, "Xbox Architecture" | local snapshot | https://www.copetti.org/writings/consoles/xbox/ | CC BY 4.0 (attribution required) | Documentation orientation only. |

## The emulation core (decision A, 2026-11-23)

XGrid runs the **xemu 0.8.139 codebase** (QEMU 10.2.0 base, GPL-2.0) as its
emulation core. The tree is committed under `xemu/`; XGrid's own core
additions live in `xemu/xgrid-core/` and the edits to upstream files are
recorded in `xemu/xemu-patches/`. `References2/xemu-0.8.139` remains the
pristine, read-only oracle and is never modified. The tree builds through
`xemu/build.sh` (MSYS2/meson), driven by `scripts/build-native-xemu.ps1`; the
solution carries `xemu/XGrid.Xemu.Tree.csproj` as the build anchor. The
release ships the core source and the patches.

### Sega Chihiro machine

The Sega Chihiro emulation in `xemu/hw/xbox/chihiro/` is ported from the
xemu-Playtest-v2 / XPSemu-Chihiro snapshots. It is GPL-2.0-compatible:
most files are LGPL-2.0-or-later, `chihiro-pic.*` is LGPL-2.1-or-later
(MAME's `naomi.cpp` tables), and `chihiro-des.*` is BSD-3-Clause
(Olivier Galibert's MAME `naomigd.cpp` DES routines, ported by Réda
Chérif-Touil). Each file keeps its original copyright and SPDX header. The
memory-backed block driver `xemu/block/blkmemory.c` is GPL-2.0-or-later and
comes from the same lineage (originally espes / IBM). `xemu/xemu-patches/0018-core-chihiro.patch`
records the changes to the vendored xemu tree.

The Chihiro netboot converters are part of XGrid. The Redump GD-ROM
converter (`src/XGrid.XboxFs/ChihiroRedumpConverter.cs`) is an independent
C# implementation of the format used by the MIT-licensed
`chihiro-netboot` tool (Tovarichtch/chihiro-netboot, based on JayFoxRox's
Chihiro-Tools); its format and DES byte-swap scheme follow that tool, and
its MIT notice is kept in the file. XGrid ships no game keys, GD-ROM data or
firmware.

Vendored inside the core additions:

| Package | Version | License | Use |
|---|---|---|---|
| libchdr | 0.3.0 | BSD-3-Clause (Copyright Romain Tisserand) | CHD hunk decoding behind `block/xgridchd.c`; ships its LZMA decoder, zlib/zstd come from the host. |
| libCCI | 0.1.0 | GPL-2.0-or-later (Copyright Team-Resurgent) | CCI header/index and LZ4 sector decoding behind `block/xgridcci.c`; built against the host LZ4. |
| LZ4 | 1.10.0 (host) | BSD-2-Clause | CCI block decode/encode; linked from the host. |

## Runtime libraries redistributed with the Windows core package

The `win-x64` frontend archive carries the built core under `core/xemu/`
(`xemu.exe`, `xgrid_core.dll`) together with the DLLs they import, as produced
by the MSYS2/MINGW64 build. Those binaries are redistributed, so their licenses
are recorded here. The core is GPL-2.0; these are its dependencies, not
separate programs invoked by it.

| Library | Files | License |
|---|---|---|
| SDL3 | `SDL3.dll` | Zlib |
| GLib | `libglib-2.0-0.dll` | LGPL-2.1-or-later |
| gettext runtime | `libintl-8.dll` | LGPL-2.1-or-later |
| libiconv | `libiconv-2.dll` | LGPL-2.1-or-later |
| PCRE2 | `libpcre2-8-0.dll` | BSD-3-Clause |
| libepoxy | `libepoxy-0.dll` | MIT |
| libsamplerate | `libsamplerate-0.dll` | BSD-2-Clause |
| OpenSSL | `libcrypto-3-x64.dll`, `libssl-3-x64.dll` | Apache-2.0 |
| libcurl | `libcurl-4.dll` | curl license (MIT-like) |
| libssh2 | `libssh2-1.dll` | BSD-3-Clause |
| libslirp | `libslirp-0.dll` | BSD-3-Clause |
| zlib | `zlib1.dll` | Zlib |
| Zstandard | `libzstd.dll` | BSD-3-Clause (dual-licensed with GPL-2.0) |
| LZ4 | `liblz4.dll` | BSD-2-Clause |
| nghttp2 / nghttp3 / ngtcp2 | `libnghttp2-14.dll`, `libnghttp3-9.dll`, `libngtcp2-16.dll`, `libngtcp2_crypto_ossl-0.dll` | MIT |
| libidn2 | `libidn2-0.dll` | LGPL-3.0-or-later |
| libunistring | `libunistring-5.dll` | LGPL-3.0-or-later |
| libpsl | `libpsl-5.dll` | MIT |
| Brotli | `libbrotlicommon.dll`, `libbrotlidec.dll` | MIT |
| GCC runtime | `libgcc_s_seh-1.dll`, `libstdc++-6.dll` | GPL-3.0-or-later WITH GCC-exception-3.1 |
| winpthreads (mingw-w64) | `libwinpthread-1.dll` | MIT / BSD-3-Clause |

The Direct3D 11 renderer imports `d3d11.dll` and `d3dcompiler_47.dll`. Both are
Windows system components on supported versions and are not redistributed with
XGrid; they are covered by the Windows SDK/DirectX redistribution terms.

`libidn2` and `libunistring` are LGPL-3.0-or-later. They are compatible because
XGrid's own code and the core additions are **GPL-2.0-or-later**: the combined
work is distributed under GPL-3.0-or-later where those libraries are included.
The core's GPL-2.0-only files (xemu/QEMU) are used under GPL-2.0, which the
GPL-3.0-or-later combination permits.

The license texts for these libraries ship in `licenses/` in every release
archive; `licenses/README.md` maps each component to its text. The copyright
lines are the tables above.

## Shipped license texts

Every archive carries `licenses/` beside `COPYING` and `LICENSE`:

| Text | Covers |
| --- | --- |
| `GPL-2.0.txt` | XGrid, the xemu core and its QEMU base, the ported DSP modules, libCCI |
| `LGPL-2.1.txt` | GLib, gettext, libiconv, LGPL files inside xemu/QEMU |
| `LGPL-3.0.txt` | libidn2, libunistring |
| `GPL-3.0.txt`, `GCC-exception-3.1.txt` | The GCC runtime DLLs |
| `Apache-2.0.txt` | OpenSSL |
| `MIT.txt` | NuGet runtime dependencies, libepoxy, nghttp2/nghttp3/ngtcp2, libpsl, Brotli, winpthreads |
| `BSD-2-Clause.txt` | libsamplerate, LZ4 |
| `BSD-3-Clause.txt` | libchdr, PCRE2, libssh2, libslirp, Zstandard, winpthreads |
| `Zlib.txt` | SDL3, zlib |
| `curl.txt` | libcurl |
| `OFL-1.1.txt` | Inter font |

`scripts/package-verify.ps1` requires every file in the folder in every
archive.

## Earlier native CPU extraction (retired 2026-11-23)

The TCG i386 core from the pinned xemu 0.8.139 tree (QEMU 10.2.0 base),
GPL-2.0, was first extracted behind `libxgridcpu` (XCN0-XCN3). Decision A made
the whole xemu codebase the core, so the shim, its patches and its probes were
removed. That component is no longer built or shipped; the current core source
offer is the one described in "The emulation core" above.

## Runtime and build dependencies

| Package | License | Use |
|---|---|---|
| XISOSharp | MIT | XDVDFS reader (`XGrid.XboxFs`). |
| CHDSharp | MIT | CHD image support (`XGrid.Storage`) and the Chihiro GD-ROM converter (`XGrid.XboxFs`). |
| Silk.NET.OpenGL | MIT | OpenGL backend (`XGrid.GpuGl`). |
| NAudio | MIT | Live WASAPI audio sink (`XGrid.Audio`, Windows). |
| Avalonia (Desktop/Fluent/Inter) | MIT | Desktop frontend. |
| CommunityToolkit.Mvvm | MIT | Desktop frontend MVVM. |
| Serilog | Apache-2.0 | Structured diagnostic logging (`XGrid.Diagnostics`). |
| Serilog.Sinks.Console / File / Debug | Apache-2.0 | Console, rolling-file and debugger sinks. |
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
| `src/XGrid.Audio/Dsp/DspDma.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/dsp_dma.c` + `dsp_dma.h` + `dsp_dma_regs.h` | LGPL-2+ (xemu file header) | Ported. |
| `src/XGrid.Audio/Dsp/Dsp56300State.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.h` + `dsp_cpu_regs.h` + `dsp.h` + `dsp_internal.h` | GPL-2+ | Ported state/constants. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | GPL-2+ | Core state, reset, memory, bootstrap. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Execution.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | GPL-2+ | Instruction fetch/decode, PC, DO/REP, stack, interrupts, effective addresses. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Alu.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc` | GPL-2+ | Accumulator ALU, MAC/MPY families, 24-bit read/limit. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Instructions.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc` | GPL-2+ | Non-parallel instruction handlers. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.ParallelMove.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc` | GPL-2+ | Parallel-move handlers (`emu_pm_0`..`emu_pm_8`). |
| `src/XGrid.Audio/Dsp/Dsp56300Opcodes.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` + `dsp_emu.c.inc` + `dsp_dis.c.inc` + `interp/debug.c` + `interp/debug.h` | GPL-2+ | Decode table (`nonparallel_opcodes`) and ALU dispatch table. |
| `src/XGrid.Audio/Dsp/DspExceptions.cs` | XGrid | GPL-2+ | Fault types for the interpreter (no reference counterpart). |
| `src/XGrid.Audio/Dsp/Dsp56300Arithmetic.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | GPL-2+ | Ported 56-bit arithmetic helpers. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Snapshot.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/dsp_c.c` + `interp/dsp_cpu.h` | GPL-2+ | Complete core state save/restore (the sync-to/from-VM field set). |
| `src/XGrid.Audio/Dsp/McpxApuDsp.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/gp_ep.c` + `gp_ep.h` (LGPL-2+), `dsp.c` + `dsp_c.c` (GPL-2+), `apu/fpconv.h` (LGPL-2+) | GPL-2+ (combined work) | GP/EP sub-windows, `proc_rst_write`, scatter/gather scratch, FIFO rings, peripheral halt/interrupt/DMA, the frame loop, `float_to_24b`. |
| `src/XGrid.Audio/McpxApuDevice.Vp.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/vp/vp.c` + `vp.h` | GPL-2+ (xemu file header) | The voice-processor mix path: the per-frame voice-list walk, both envelopes, PCM/ADPCM buffer and stream fetch through the SSL/SGE tables, resampling, the 8-tap volume/headroom mix into the 32 mix bins, multipass voices. |
| `src/XGrid.Audio/Vp/ApuVpAdpcm.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/vp/adpcm.h` (ADPCM-XQ, Copyright (c) David Bryant) | BSD-3-Clause | The 4-bit ADPCM block decoder (BSD-3-Clause, GPL-compatible; the notice is in the file header). |
| `src/XGrid.Audio/Vp/ApuVpFilters.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/vp/svf.h` (SWH LADSPA plugins, Steve Harris / andy@vellocet, GPLv2) + `hrtf.h` (c) 2025 Matt Borgerson) | GPL-2+ | The state-variable low-pass filter and the 31-tap HRTF filter with interaural delay. |
| `src/XGrid.Audio/Vp/ApuVpFrame.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/apu_regs.h`, `apu_int.h`, `vp/vp.c`, `apu/fpconv.h` | GPL-2+ | Frame geometry constants and the reference's sample conversions. |
| `src/XGrid.Nv2a/PshCombiner.cs` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/glsl/psh.c`, `pgraph/psh_regs.h` (GPL-2+; itself derived from Cxbx `PixelShader.cpp` and Xeon `XBD3DPixelShader.cpp`) | GPL-2+ | Register-combiner stage decode and evaluation. |
| `src/XGrid.Nv2a/VshProgram.cs` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/glsl/vsh-prog.c` + `vsh-prog.h` + `vsh_regs.h` (GPL-2+; itself derived from Cxbx `VertexShader.cpp` and Dxbx `uPushBuffer.pas`) | GPL-2+ | Transform-program decode and execution. |
| `src/XGrid.Nv2a/VshProgramPlan.cs` | XGrid `VshProgram.cs`, itself ported from xemu 0.8.139 `glsl/vsh-prog.c` | GPL-2+ | Predecoded, allocation-free form of the same program semantics. |
| `src/XGrid.Nv2a/FixedFunctionVertex.cs` | xemu 0.8.137 `hw/xbox/nv2a/pgraph/glsl/vsh-ff.c` + `vsh-ff.h` (LGPL-2+; no semantic delta in 0.8.139) | LGPL-2+ | Fixed-function transform, texgen, lighting and position epilogue. |
| `src/XGrid.Nv2a/FixedFunctionVertexState.cs` | xemu 0.8.137 `hw/xbox/nv2a/pgraph/pgraph.c` + `glsl/vsh.c` + `glsl/vsh.h` (LGPL-2+; no semantic delta in 0.8.139) | LGPL-2+ | Latched fixed-function register state. |
| `src/XGrid.Nv2a/SpecularPowerCurve.cs` | xemu 0.8.137 `hw/xbox/nv2a/pgraph/pgraph.c` `reconstruct_specular_power` (LGPL-2+; no semantic delta in 0.8.139) | LGPL-2+ | Fixed-function specular-exponent reconstruction and coefficient tables. |
| `src/XGrid.Nv2a/S3tcDecoder.cs` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/s3tc.c` + `s3tc.h` | MIT (xemu file header) | DXT1/DXT3/DXT5 block decompression. |
| `src/XGrid.Nv2a/Nv097MethodTable.cs` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/methods.h.inc` + `nv2a_regs.h` | GPL-2+ | Generated handled/declined offset table for the unknown-method counter. |
| `src/XGrid.Nv2a/TextureFormats.cs` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/texture.c`, `pgraph/gl/constants.h`, `pgraph/glsl/psh.c` (`shadow_comparison_map`), `pgraph/util.h` | LGPL-2+ (xemu file headers; included under GPL-2.0 via LGPL §3) | The color-format table, the Y8/AY8/A8/A8Y8/Y16/R6G5B5 word decoders, the YUV conversion, the palette entry, the mip-chain layout, and the depth-texture decode + shadow compare operators. |
| `src/XGrid.Nv2a/Nv2aMmio.cs` (shadow-map path) | xemu 0.8.139 `hw/xbox/nv2a/pgraph/glsl/psh.c` `psh_append_shadowmap` | LGPL-2+ | Sampling and comparison wiring for depth (shadow-map) texture stages. |
| `src/XGrid.Nv2a/Nv2aMmio.cs` (occlusion reports) | xemu 0.8.139 `hw/xbox/nv2a/pgraph/gl/reports.c` + `pgraph.c` `pgraph_write_zpass_pixel_cnt_report` | LGPL-2+ | ZPASS_PIXEL_CNT report semantics and the 16-byte report block layout. |
| `src/XGrid.Nv2a/Nv2aMmio.cs` (clear rules) | xemu 0.8.139 `hw/xbox/nv2a/pgraph/pgraph.c` `pgraph_get_clear_color`/`pgraph_get_clear_depth_stencil_value` + `pgraph/gl/draw.c` `pgraph_gl_clear_surface` | LGPL-2+ | Color clear normalization and the per-channel / per-field clear masks. |
| `src/XGrid.GpuGl/GlPgraphBackend.cs` (swizzled surface download) | xemu 0.8.139 `hw/xbox/nv2a/pgraph/gl/surface.c` `surface_download` + `pgraph/swizzle.c` `swizzle_rect` | LGPL-2+ | Render-target FBO to guest-RAM download in the surface Morton layout. |
| `src/XGrid.GpuGl/GlPgraphBackend.cs` (texture identity) | xemu 0.8.139 `hw/xbox/nv2a/pgraph/gl/texture.c` `pgraph_gl_bind_textures` + `qemu/fast-hash.c` (algorithm) | LGPL-2+ | Whole-range texture content hash gated by a guest write stamp, and the complete texture cache key. |
| `tools/texture-oracle/` | xemu 0.8.139 `hw/xbox/nv2a/pgraph/s3tc.c` (compiled at run time, never redistributed) | MIT | Development-only S3TC differential oracle; not part of the build or distribution. |
| `tools/dsp-oracle/` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/interp/` (compiled at run time, never redistributed) | GPL-2+ | Development-only differential oracle; not part of the build or distribution. |

When a module is ported, add a row here and a matching entry in
`docs/legal/licensing.md`.
