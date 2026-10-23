# Reference Sources and Licensing Inventory

Date reviewed: 2026-10-08; extended 2026-10-13 (Copetti, envytools, nv2a-trace, xemu-Playtest-v2 added);
2026-10-20 xemu 0.8.137 adopted as the primary snapshot; **2026-10-23 xemu 0.8.139
adopted as the primary snapshot and XGrid relicensed to GPL-2.0 with a porting
policy.**
Policy: XGrid is **GPL-2.0** and may port source code from GPL-compatible
reference projects. These notes record both factual hardware/format knowledge and
the provenance of ported code. Every ported file keeps its original copyright and
license notice and is listed in `THIRD_PARTY_NOTICES.md`; the binding rules are in
`docs/porting-policy.md`.

## Project inventory

| Project | Version/Snapshot | Upstream | License | Code copied? |
|---|---|---|---|---|
| xemu | **0.8.139 (primary since 2026-10-23)** | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | **Yes — ported/derived where noted** |
| xemu (prior snapshot) | 0.8.137 | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | **Yes — ported/derived where noted** |
| xemu (prior snapshot) | 0.8.136 | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and cross-checks |
| QEMU (xemu base) | 10.2.0 | https://www.qemu.org | GPL-2.0; some files GPL-2.0-or-later | **Yes — ported/derived where noted**; not embedded as a native dependency |
| xemu-Playtest-v2 | local snapshot 2026-09 (QEMU 10.2.0 base) | xemu in-development fork (Sega Chihiro arcade bring-up); no upstream URL recorded | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | Facts and cross-checks |
| Cxbx-Reloaded | master | https://github.com/Cxbx-Reloaded/Cxbx-Reloaded | GPL-2.0 (some files LGPL-2) | **Yes — ported/derived where noted** |
| xqemu | master (QEMU 4.0 base) | https://github.com/xqemu/xqemu | GPL-2.0 (mixed per-file) | **Yes — ported/derived where noted** |
| XboxDevWiki | offline dump 2026-10-12 | https://xboxdevwiki.net | none declared | No — facts only |
| Copetti — Xbox Architecture | local snapshot | https://www.copetti.org/writings/consoles/xbox/ (text: github.com/flipacholas/Architecture-of-consoles) | **CC BY 4.0** (attribution required) | No — facts only |
| envytools | master snapshot | https://github.com/envytools/envytools | **MIT** | Facts and ported tables where noted |
| nv2a-trace | master snapshot | https://github.com/XboxDev/nv2a-trace | **MIT** | Facts and ported tables where noted |
| XISOSharp | current master | https://github.com/purelogiccode/XISOSharp | **MIT** | Approved for direct dependency integration (see below) |
| Silk.NET | pinned when first added (GPU slice 1) | https://github.com/dotnet/Silk.NET | **MIT** | Approved for direct dependency (operator 2026-10-13; see below) |
| Avalonia (Desktop/Fluent/Inter) | 11.3.22 (frontend) | https://github.com/AvaloniaUI/Avalonia | **MIT** | Approved for the desktop frontend (operator-requested 2026-10-20; see below) |
| CommunityToolkit.Mvvm | 8.4.2 (frontend) | https://github.com/CommunityToolkit/dotnet | **MIT** | Approved for the desktop frontend (see below) |

## Ported modules

| XGrid file | Source | License | Status |
|---|---|---|---|
| `src/XGrid.Audio/Dsp/DspDma.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/dsp_dma.c` / `dsp_dma.h` | LGPL-2+ | Ported (DMA engine). |
| `src/XGrid.Audio/Dsp/Dsp56300State.cs` | xemu 0.8.139 `interp/dsp_cpu.h` / `dsp_cpu_regs.h` | GPL-2+ | Ported (state + constants). |
| `src/XGrid.Audio/Dsp/Dsp56300Arithmetic.cs` | xemu 0.8.139 `interp/dsp_cpu.c` | GPL-2+ | Ported (56-bit arithmetic). |
| `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | xemu 0.8.139 `interp/dsp_cpu.c` | GPL-2+ | Core state/reset/memory/bootstrap. |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Execution.cs` | xemu 0.8.139 `interp/dsp_cpu.c` | GPL-2+ | Ported (fetch/decode, PC, DO/REP, stack, interrupts, effective addresses). |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Alu.cs` | xemu 0.8.139 `interp/dsp_emu.c.inc` | GPL-2+ | Ported (ALU, MAC/MPY families, 24-bit read/limit). |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Instructions.cs` | xemu 0.8.139 `interp/dsp_emu.c.inc` | GPL-2+ | Ported (non-parallel instruction handlers). |
| `src/XGrid.Audio/Dsp/Dsp56300Core.ParallelMove.cs` | xemu 0.8.139 `interp/dsp_emu.c.inc` | GPL-2+ | Ported (parallel-move handlers). |
| `src/XGrid.Audio/Dsp/Dsp56300Opcodes.cs` | xemu 0.8.139 `interp/dsp_cpu.c` / `dsp_emu.c.inc` | GPL-2+ | Ported (decode and ALU dispatch tables). |
| `src/XGrid.Audio/Dsp/Dsp56300Core.Snapshot.cs` | xemu 0.8.139 `dsp_c.c` (`dsp_c_sync_to_vm`/`dsp_c_sync_from_vm`) + `dsp_cpu.h` | GPL-2+ | Ported (complete core state save/restore). |
| `src/XGrid.Audio/Dsp/McpxApuDsp.cs` | xemu 0.8.139 `hw/xbox/mcpx/apu/dsp/gp_ep.c` (LGPL-2+), `dsp.c` + `dsp_c.c` (GPL-2+), `apu/fpconv.h` (LGPL-2+) | GPL-2+ (combined work) | Ported (GP/EP sub-windows, `proc_rst_write`, scatter/gather scratch, FIFO rings, peripheral halt/interrupt/DMA, the frame loop, `float_to_24b`). |
| `tests/XGrid.Audio.Tests/TestData/dsp-vectors.txt` | generated from the xemu 0.8.139 reference interpreter | GPL-2+ (derived data) | Differential vectors; regenerate with `tools/dsp-oracle/build.ps1 -Regenerate`. |
| `tools/dsp-oracle/` | xemu 0.8.139 `interp/` (compiled from `References2/` at run time) | GPL-2+ | Development-only oracle; not part of the build or distribution. |

See `THIRD_PARTY_NOTICES.md` for the full list and `docs/porting-policy.md` for
the workflow.

## Restoring the vendored set (References2/)

Downloaded 2026-10-12 (extract into `References2/`; GitHub zips carry these
root folder names):
- xemu 0.8.139 - https://github.com/xemu-project/xemu/archive/refs/tags/v0.8.139.zip (primary)
- xemu 0.8.137 - https://github.com/xemu-project/xemu/archive/refs/tags/v0.8.137.zip (prior snapshot)
- xemu 0.8.136 - https://github.com/xemu-project/xemu/archive/refs/tags/v0.8.136.zip (prior snapshot)
- QEMU 10.2.0 - the QEMU base vendored inside `xemu-0.8.139/`; upstream
  https://www.qemu.org (GPL-2.0). Do not add it as a separate native dependency.
- xemu-Playtest-v2 — no upstream URL recorded (operator-supplied local snapshot); ask the operator.
- Cxbx-Reloaded (master) — https://github.com/Cxbx-Reloaded/Cxbx-Reloaded/archive/refs/heads/master.zip
- xqemu (master) — https://github.com/xqemu/xqemu/archive/refs/heads/master.zip
- Copetti "Xbox Architecture" — `References2/CopettiDocumentation/` (article:
  https://www.copetti.org/writings/consoles/xbox/ ; article source text:
  https://github.com/flipacholas/Architecture-of-consoles ). Files:
  `xbox-architecture.md` + `.html` + `images/`.
- envytools — https://github.com/envytools/envytools/archive/refs/heads/master.zip
  (extracts to `envytools-master/`; note the vendored `README.rst` carries a
  prepended one-line note that NV20/Kelvin register/method data is the target).
- nv2a-trace — https://github.com/XboxDev/nv2a-trace/archive/refs/heads/master.zip
  (extracts to `nv2a-trace-master/`).
- XboxDevWiki — full offline dump in `References2/XboxDevWiki/` (captured
  2026-10-12 via the MediaWiki API): all 160 ns-0 articles as standalone HTML
  (`html/`) + raw wikitext (`pages/`), all 117 images (`images/`),
  `manifest.json` + `index.html`; re-dump via `scripts/dump-xboxdevwiki.ps1`.
  Old numeric `.mhtml` citations (`16` NV2A, `22` Memory, `41` USB, `51` Boot
  Process, `53` FATX, ...) map to title-based files — see
  `References2/XboxDevWiki/README.md`. No explicit license declared on the wiki
  (footer lists only privacy/about/disclaimer); facts-only per project policy.

## Per-project license detail

### xemu 0.8.139 (primary snapshot, adopted 2026-10-23)
- QEMU 10.2.0 base; the 0.8.137 and 0.8.136 trees stay vendored as prior snapshots.
- License split is unchanged: top-level `COPYING` = GPL-2.0; `hw/xbox/**` per-file LGPL-2+ /
  MIT / GPL-2+; the `mcpx/apu/dsp/**` subtree (including `dsp_jit.c` and `interp/`) is
  GPL-2+, as are the GLSL shader generators. XGrid is GPL-2.0 and may port these files
  with their notices; see `THIRD_PARTY_NOTICES.md` and `docs/porting-policy.md`.
- The primary APU DSP port source is `hw/xbox/mcpx/apu/dsp/` (0.8.139); the C
  interpreter (`dsp_c.c`, `interp/dsp_cpu.c`, `interp/dsp_emu.c.inc`, `dsp_dma.c`,
  `gp_ep.c`) is the reference for the C# port.
- **Delta vs 0.8.137:** 37 `hw/xbox` files differ, concentrated in `nv2a/`
  (see `docs/research-notes/xemu-0.8.139.md`); the APU/DSP subtree is
  byte-identical. The older 0.8.137 vs 0.8.136 delta was 150 of 167 `hw/xbox`
  files and added the APU DSP interpreter.
- See `docs/research-notes/xemu-0.8.137.md` for the catalogued delta and
  `docs/research-notes/xemu.md` for the 0.8.136-era facts that remain valid where the file
  is unchanged.

### xemu 0.8.136 (prior snapshot, 2026-10-13 - 2026-10-20)
- Top-level `COPYING` = GNU GPL v2; `LICENSE` states files without headers are GPL-2.0-or-later.
- `hw/xbox/**` per-file:
  - **LGPL-2+**: `xbox.c/h`, `xbox_pci.c/h`, `acpi_xbox.c/h`, `lpc47m157.c`, `eeprom_generation.c/h`,
    `xid*.c/h`, `xblc.c`, `chihiro*.c`, all `nv2a/**` except the GPL files below, all `mcpx/apu/**`
    except `dsp/*`, `mcpx/nvnet/**`, `mcpx/aci.c`.
  - **MIT**: `amd_smbus.c/h`, all `smbus_*.c`, `nv2a/pgraph/s3tc.c/h`,
    `nv2a/pgraph/thirdparty/gloffscreen/*`.
  - **GPL-2+**: `mcpx/apu/dsp/*` (adapted from Hatari), `nv2a/pgraph/glsl/psh.c`,
    `nv2a/pgraph/glsl/vsh-prog.c/h`, `nv2a/pgraph/vsh_regs.h`.
- The QEMU base itself is GPL-2.0. XGrid does not embed QEMU as a native
  dependency; QEMU algorithms may be ported to managed C# under the GPL-2.0
  policy.

### QEMU 10.2.0 (xemu base)
- Vendored inside `References2/xemu-0.8.139/`; top-level `COPYING` = GPL-2.0,
  with some files GPL-2.0-or-later.
- XGrid may port QEMU algorithms to managed C#. It must not link QEMU or
  embed its native code; see `docs/porting-policy.md`.
- The CPU TCG is **not** a porting target: XGrid's raw emitter is already
  bit-exact and faster than xemu on the CPU-bound ktest. The gap is the
  boot/device/GPU path.

### xemu-Playtest-v2 (in-development Chihiro fork)
- Operator-supplied local snapshot; **QEMU 10.2.0** base (`QEMU_VERSION`), xemu UI/settings
  present; new files copyright 2026 Réda Chérif-Touil. No upstream URL recorded.
- Purpose per operator: in-development fork to bring up the **Sega Chihiro** (arcade) board.
- vs 0.8.136: full `hw/xbox/chihiro/` subsystem; NV2A performance work (the "tuned GPU":
  `pgraph/cost.c`, `seed.c`, `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the
  un-tuned baseline vendored as `hw/xbox/nv2a-stock/` (`diff -ru` isolates the perf delta);
  `xid-light-gun.c`; SMBus `adm1032`/`cx25871`/`fs454`/`xcalibur`; 5 newer NV097 methods.
- License: xemu-family (GPL-2.0 overall; per-file LGPL-2+/MIT/GPL-2+). Verdict: facts
  and cross-checks; port source where a tuned-GPU behavior is needed.
- See `docs/research-notes/xemu-playtest-v2.md`.

### Xermu - WITHDRAWN (never a real reference)
- Older drafts of this inventory listed a "Xermu" standalone C# Xbox emulator
  (`References2/Xermu-main`). No such project exists in the workspace and the operator
  confirmed on 2026-10-13 that the correct (and only) primary LLE reference is
  `References2/xemu-0.8.137` (0.8.136 until the 2026-10-20 adoption).
- `docs/research-notes/xermu.md` was deleted. Historical citations to it (early plan text,
  `xgrid-architecture-decisions.md`) are unverified - do not use them as facts. Use xemu,
  xqemu, Cxbx, XboxDevWiki or envytools instead.

### Cxbx-Reloaded (master)
- Root `COPYING` = GPLv2; most headers GPL-2.0-or-later; a few GPL-2.0-only.
- `src/devices/video/swizzle.cpp` is LGPL-2 (from QEMU/Nouveau); QEMU USB core files are
  MIT/GPL-compatible dual-licensed. README states xqemu-derived code is GPL-2.0.
- Verdict: facts and port source (XBE structs, ordinal semantics, NV2A maps,
  XDVDFS layout). Ported files keep the GPL-2.0/LGPL-2 notices.

### xqemu (master)
- Root `COPYING` = GPLv2; per-file mix: `xbox.c`, `xbox_pci.c`, `nv2a.c`, `nv2a_pgraph.c`,
  `nv2a_shaders.c/h`, `nv2a_int.h`, `acpi_xbox.c`, `xid.c`, `adpcm*.h` are LGPL-2.1-or-later;
  `nv2a_vsh.c/h`, `nv2a_psh.c`, `dsp/` are GPL-2+; `smbus_xbox_smc.c`, `amd_smbus.c/h` MIT.
- Verdict: facts and port source. Ported files keep the GPL-2.0/LGPL-2.1+
  notices.

### Copetti — Xbox Architecture (local snapshot)
- `References2/CopettiDocumentation/xbox-architecture.md` (+ `.html`, `images/`).
- **CC BY 4.0** — the article's own "Copyright and permissions" section requires
  attribution (link to the article + license, per its guidelines); commercial use
  permitted. If any text/figure is reused in XGrid docs, cite
  "Rodrigo Copetti, Xbox Architecture: A Practical Analysis" + the CC BY 4.0 link.
- Verdict: prose/architecture reference (onboarding, big-picture validation,
  README/background material). Not a register-level oracle — for addresses and
  register semantics use xemu / XboxDevWiki / envytools.

### envytools (master snapshot)
- `References2/envytools-master/`. Root `COPYING` is the **MIT/Expat** license
  (Copyright 2013 the envytools authors) — unlike the other GPU references, this
  one is copy-permissive.
- Contains: `rnndb/` (rules-ng-ng XML register/class/method/enum database),
  `docs/` (GPU architecture docs: FIFO DMA pusher/puller, PGRAPH, XF, PMC/PTIMER,
  memory/swizzle), `rnn/` (XML query/decode helpers incl. `rnndec.py`),
  `demmt/` (MMIO trace decoder), `envydis/` (GPU ISA disassembler).
- Verdict: primary cross-check for NV2A method offsets/semantics and FIFO/pusher
  behavior (see `docs/research-notes/envytools.md`). MIT means programmatic reuse
  of the XML data is permissible if ever wanted — record it here first (rule 3).
- **Naming caveat:** envytools' own "NV2A" (see `docs/glossary.txt`) is the NVIDIA
  nForce IGP, *not* the Xbox GPU. The Xbox 3D object class `0x0097` is what
  envytools calls `NV20_3D` (Kelvin family).

### nv2a-trace (master snapshot)
- `References2/nv2a-trace-master/`. **MIT**, Copyright (c) 2018-2022 XboxDev maintainers.
- apitrace-style tracer for a *real* Xbox GPU: pauses the DMA pusher, inspects each
  method before execution, dumps intermediate surfaces/textures (PNG in `out/`,
  plus `debug.html`). Requires real hardware + `xboxpy`; README warns it is
  unstable and may temporarily corrupt console state; no replayable trace format yet.
- Verdict: (a) hardware ground truth for PGRAPH validation if a box is available;
  (b) facts-only (MIT): method-hook constants, pushbuffer parsing and the texture
  channel/bpp table are usable — see `docs/research-notes/nv2a-trace.md`.

### XISOSharp (MIT)
- MIT License, Copyright (c) PureLogicCode.com; lead developer Peterson Fernandes.
- Ports with third-party notices reproduced in `LICENSE`:
  - **extract-xiso** (BSD-style, requires advertising acknowledgement
    "This product includes software developed by in <in@fishtank.com>.") — core XISO engine.
  - **xdvdfs** (MIT, Copyright 2023 antangelo) — build-image remap, CISO, checksum.
  - **XboxKit** (MIT declared via package metadata, Copyright (c) Deterous 2024-2026) —
    Redump archival logic.
  - **ZArchiveSharp** (MIT, PureLogicCode.com) — consumed as NuGet dependency 1.4.0.
- Approved integration: XGrid may reference the `XISOSharp` library (project or NuGet). If any
  XISOSharp source is adapted into XGrid, the MIT notice and the extract-xiso advertising clause
  must be preserved. `References2/` inside that repo (extract-xiso, xdvdfs 0.8.3, XboxKit 0.7,
  Avalonia sources) is third-party material — do not copy.
- Caveat: `XISOSharp` has a runtime dependency on `ZArchiveSharp 1.4.0` (not BCL-only).
- Integrated 2026-10-11: `XGrid.XboxFs` references NuGet `XISOSharp` 1.4.2
  (centrally pinned in `Directory.Packages.props`; transitive `ZArchiveSharp`
  1.4.0 confirmed in restore graph). No source copied - consumption is via
  the public `XisoExplorer` API behind `XGrid.XboxFs/XdvdfsFileSystem`.

### Silk.NET (MIT) — approved 2026-10-13
- Operator decision (2026-10-13) for GPU-backend slice 1
  (`gpu-backend-design.md` §8): **`Silk.NET.OpenGL`** as the net9.0 GL binding.
  **MIT** (https://github.com/dotnet/Silk.NET). Pin package + version centrally
  in `Directory.Packages.props` when first added; no other new dependency is
  allowed for slice 1. Chosen over OpenTK and Veldridu.

### CHDSharp (MIT)
- MIT License, Copyright (c) 2026 Peterson Fernandes and Gordon Jefferyes
  (sibling repo `C:/Users/HomePC/Dropbox/source/repos/CSharp_CHDSharp`,
  docs + source used for API facts).
- Integrated 2026-10-11: `XGrid.Storage` references NuGet `CHDSharp` 1.4.3
  (centrally pinned). No source copied - consumption is via the public
  `ChdFile` API (`Open`/`Read`/`TotalBytes`) behind
  `XGrid.Storage/ChdDvdImage`.

### Avalonia 11.3.22 (MIT) — approved 2026-10-20
- Operator-requested desktop frontend track. UI-only dependency of
  `XGrid.Frontend.Avalonia`: packages `Avalonia`, `Avalonia.Desktop`,
  `Avalonia.Themes.Fluent`, and `Avalonia.Fonts.Inter`, all 11.3.22, plus the
  transitive SkiaSharp/HarfBuzzSharp/MicroCom runtime. **MIT**
  (https://github.com/AvaloniaUI/Avalonia).
- No Avalonia source copied: application code, XAML, and the dark theme under
  `src/XGrid.Frontend.Avalonia/` are original. XISOSharp's vendored Avalonia
  sources remain off-limits (facts only).
- Scope rule: core/CLI/test projects must never reference UI packages (plan §1.1).
  Versions are supplied with `VersionOverride` in the frontend project so
  `Directory.Packages.props` is not part of the frontend track.

### CommunityToolkit.Mvvm 8.4.2 (MIT) — approved 2026-10-20
- MVVM helpers (observable properties, relay commands, source generators) for the
  frontend only. **MIT** (https://github.com/CommunityToolkit/dotnet). Not
  referenced by the emulator core, CLI, or tests.

## Subsystem → reference map (facts)

| XGrid subsystem | Primary reference | Secondary | Notes |
|---|---|---|---|
| Boot flow / reset vector | xemu `hw/xbox/xbox.c` | xqemu `xbox.c` | MCPX ROM overlay at top of BIOS |
| Physical memory map | xemu `xbox.c` | xqemu `xbox.c` | See constants appendix |
| Pentium III model/CPUID/MSR | xemu `target/i386` XBOX patches | — | Model 8/10, CPUID leaf quirks |
| PCI IDs/IRQ routing | xemu `xbox_pci.c` | xqemu `xbox_pci.c` | Routing regs 0x64/0x68/0x6C |
| SMBus/SMC/EEPROM | xemu `amd_smbus.c`, `smbus_xbox_smc.c`, `eeprom_generation.c` | xqemu same | EEPROM layout/crypto |
| Flash | xemu `xbox.c` (ROM window/alias only) | - | AMD/SST CFI command set still pending; implement clean-room (Phase 6) |
| APU | xemu `mcpx/apu/**` | xqemu `mcpx_apu.c` (single file) | DSP56300 semantics |
| NV2A registers/PFIFO/PGRAPH | xemu `hw/xbox/nv2a/**` | Cxbx `nv2a_regs.h`, xqemu, xemu-Playtest-v2 | NV097 method map (+5 newer methods in Playtest-v2) |
| GPU backend performance design | **xemu-Playtest-v2 `nv2a` vs `nv2a-stock` diff** (cost model, shader seeds, uniform cache, writeback, driver probe) | xemu 0.8.137 `pgraph/` | Facts for the future host-GPU backend; `diff -ru` isolates the perf delta |
| Shaders (VSH/PSH) | Cxbx `nv2a_vsh/psh.cpp` | xemu `pgraph/glsl/**` (GPL) | Behavior only |
| Texture swizzle | Cxbx `swizzle.cpp` (LGPL-2) | xemu `pgraph/swizzle.c` | Algorithm facts |
| USB OHCI + XID | xemu `xid.c`, Cxbx `OHCI.cpp` | xqemu `xid.c` | Report formats |
| XBE format | Cxbx `Xbe.h` | xemu `xemu-xbe.h`, XISOSharp `XbeInfo` | Full struct offsets |
| Kernel exports | Cxbx `KernelThunk.cpp` | - | 379 ordinals, semantics |
| XDVDFS | XISOSharp (MIT, reusable) | Cxbx `xdvdfs-tools` (GPL-2) | Format facts documented |
| FATX | **No implementation in workspace** | xemu superblock creator only | Must implement clean-room |
| CPU test strategy | - | - | Write own NASM golden tests (harness.inc convention) |
| NV2A method offsets/names (cross-check) | **envytools `rnndb/graph/nv20_3d.xml`** (class `0x0097`) | Cxbx `nv2a_regs.h`, xemu `methods.h.inc` | Offsets verified identical (0x17FC, 0x1800/0x1810/0x1818, 0x0B00/0x0B80) |
| FIFO pusher / puller semantics | **envytools `docs/hw/fifo/dma-pusher.rst`, `puller.rst`, `nv1-pfifo.rst`** | xemu `nv2a/pfifo/` | GET/PUT, jump/call/return, CACHE→puller |
| Vertex program / XF unit semantics | **envytools `docs/hw/graph/xf/`** (`intro`, `isa`, `mode`, `ctx`) | xemu `vsh-prog.c`, Cxbx `nv2a_vsh.cpp` | XF VLIW ISA, IBUF/XFCTX reads, texgen/lighting/fog |
| GPU method-stream debugging / intermediate dumps | **nv2a-trace** (`Trace.py` method hooks, `Texture.py` table) | XGrid `--pgraph` instrumentation | Hardware oracle if a real Xbox is available |
| Console architecture background (docs/onboarding) | **Copetti `xbox-architecture.md`** (CC BY 4.0, attribute) | XboxDevWiki | High-level only; not register-level |

## Rule going forward
1. Extract facts into notes like this one before implementing.
2. Never copy code from GPL/LGPL/unlicensed references.
3. XISOSharp, Silk.NET, Avalonia, and CommunityToolkit.Mvvm are the approved direct
   dependencies (the latter two are frontend-only); record any version change here.
4. Maintain this file when a new reference is studied (plan §46).
