# Reference Sources and Licensing Inventory

Date reviewed: 2026-10-08; extended 2026-10-13 (Copetti, envytools, nv2a-trace, xemu-Playtest-v2 added)
Policy: XGrid is a clean-room reimplementation. These notes record factual hardware/format
knowledge (addresses, register maps, struct layouts, protocol constants, algorithms). No source
code from any reference project is copied into XGrid unless its license explicitly permits it and
the reuse is recorded below.

## Project inventory

| Project | Version/Snapshot | Upstream | License | Code copied? |
|---|---|---|---|---|
| xemu | 0.8.136 | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | No — facts only |
| xemu-Playtest-v2 | local snapshot 2026-09 (QEMU 10.2.0 base) | xemu in-development fork (Sega Chihiro arcade bring-up); no upstream URL recorded | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | No — facts only |
| Cxbx-Reloaded | master | https://github.com/Cxbx-Reloaded/Cxbx-Reloaded | GPL-2.0 (some files LGPL-2) | No — facts only |
| xqemu | master (QEMU 4.0 base) | https://github.com/xqemu/xqemu | GPL-2.0 (mixed per-file) | No — facts only |
| XboxDevWiki | offline dump 2026-10-12 | https://xboxdevwiki.net | none declared | No — facts only |
| Copetti — Xbox Architecture | local snapshot | https://www.copetti.org/writings/consoles/xbox/ (text: github.com/flipacholas/Architecture-of-consoles) | **CC BY 4.0** (attribution required) | No — facts only |
| envytools | master snapshot | https://github.com/envytools/envytools | **MIT** | No — facts only |
| nv2a-trace | master snapshot | https://github.com/XboxDev/nv2a-trace | **MIT** | No — facts only |
| XISOSharp | current master | https://github.com/purelogiccode/XISOSharp | **MIT** | Approved for direct dependency integration (see below) |

## Restoring the vendored set (References/)

Downloaded 2026-10-12 (extract into `References/`; GitHub zips carry these
root folder names):
- xemu 0.8.136 — https://github.com/xemu-project/xemu/archive/refs/tags/v0.8.136.zip
- xemu-Playtest-v2 — no upstream URL recorded (operator-supplied local snapshot); ask the operator.
- Cxbx-Reloaded (master) — https://github.com/Cxbx-Reloaded/Cxbx-Reloaded/archive/refs/heads/master.zip
- xqemu (master) — https://github.com/xqemu/xqemu/archive/refs/heads/master.zip
- Copetti "Xbox Architecture" — `References/CopettiDocumentation/` (article:
  https://www.copetti.org/writings/consoles/xbox/ ; article source text:
  https://github.com/flipacholas/Architecture-of-consoles ). Files:
  `xbox-architecture.md` + `.html` + `images/`.
- envytools — https://github.com/envytools/envytools/archive/refs/heads/master.zip
  (extracts to `envytools-master/`; note the vendored `README.rst` carries a
  prepended one-line note that NV20/Kelvin register/method data is the target).
- nv2a-trace — https://github.com/XboxDev/nv2a-trace/archive/refs/heads/master.zip
  (extracts to `nv2a-trace-master/`).
- XboxDevWiki — full offline dump in `References/XboxDevWiki/` (captured
  2026-10-12 via the MediaWiki API): all 160 ns-0 articles as standalone HTML
  (`html/`) + raw wikitext (`pages/`), all 117 images (`images/`),
  `manifest.json` + `index.html`; re-dump via `scripts/dump-xboxdevwiki.ps1`.
  Old numeric `.mhtml` citations (`16` NV2A, `22` Memory, `41` USB, `51` Boot
  Process, `53` FATX, ...) map to title-based files — see
  `References/XboxDevWiki/README.md`. No explicit license declared on the wiki
  (footer lists only privacy/about/disclaimer); facts-only per project policy.

## Per-project license detail

### xemu 0.8.136
- Top-level `COPYING` = GNU GPL v2; `LICENSE` states files without headers are GPL-2.0-or-later.
- `hw/xbox/**` per-file:
  - **LGPL-2+**: `xbox.c/h`, `xbox_pci.c/h`, `acpi_xbox.c/h`, `lpc47m157.c`, `eeprom_generation.c/h`,
    `xid*.c/h`, `xblc.c`, `chihiro*.c`, all `nv2a/**` except the GPL files below, all `mcpx/apu/**`
    except `dsp/*`, `mcpx/nvnet/**`, `mcpx/aci.c`.
  - **MIT**: `amd_smbus.c/h`, all `smbus_*.c`, `nv2a/pgraph/s3tc.c/h`,
    `nv2a/pgraph/thirdparty/gloffscreen/*`.
  - **GPL-2+**: `mcpx/apu/dsp/*` (adapted from Hatari), `nv2a/pgraph/glsl/psh.c`,
    `nv2a/pgraph/glsl/vsh-prog.c/h`, `nv2a/pgraph/vsh_regs.h`.
- The QEMU base itself is GPL-2.0. XGrid must not depend on QEMU.
- Verdict: read for facts only. Do not copy code/tables/comments.

### xemu-Playtest-v2 (in-development Chihiro fork)
- Operator-supplied local snapshot; **QEMU 10.2.0** base (`QEMU_VERSION`), xemu UI/settings
  present; new files copyright 2026 Réda Chérif-Touil. No upstream URL recorded.
- Purpose per operator: in-development fork to bring up the **Sega Chihiro** (arcade) board.
- vs 0.8.136: full `hw/xbox/chihiro/` subsystem; NV2A performance work (the "tuned GPU":
  `pgraph/cost.c`, `seed.c`, `writeback.c`, `gl/probe.c`, `glsl/uniform-cache.c`) with the
  un-tuned baseline vendored as `hw/xbox/nv2a-stock/` (`diff -ru` isolates the perf delta);
  `xid-light-gun.c`; SMBus `adm1032`/`cx25871`/`fs454`/`xcalibur`; 5 newer NV097 methods.
- License: xemu-family (GPL-2.0 overall; per-file LGPL-2+/MIT/GPL-2+). Verdict: facts only.
- See `docs/research-notes/xemu-playtest-v2.md`.

### Xermu - WITHDRAWN (never a real reference)
- Older drafts of this inventory listed a "Xermu" standalone C# Xbox emulator
  (`References/Xermu-main`). No such project exists in the workspace and the operator
  confirmed on 2026-10-13 that the correct (and only) primary LLE reference is
  `References/xemu-0.8.136`.
- `docs/research-notes/xermu.md` was deleted. Historical citations to it (early plan text,
  `xgrid-architecture-decisions.md`) are unverified - do not use them as facts. Use xemu,
  xqemu, Cxbx, XboxDevWiki or envytools instead.

### Cxbx-Reloaded (master)
- Root `COPYING` = GPLv2; most headers GPL-2.0-or-later; a few GPL-2.0-only.
- `src/devices/video/swizzle.cpp` is LGPL-2 (from QEMU/Nouveau); QEMU USB core files are
  MIT/GPL-compatible dual-licensed. README states xqemu-derived code is GPL-2.0.
- Verdict: read for facts only (XBE structs, ordinal semantics, NV2A maps, XDVDFS layout).

### xqemu (master)
- Root `COPYING` = GPLv2; per-file mix: `xbox.c`, `xbox_pci.c`, `nv2a.c`, `nv2a_pgraph.c`,
  `nv2a_shaders.c/h`, `nv2a_int.h`, `acpi_xbox.c`, `xid.c`, `adpcm*.h` are LGPL-2.1-or-later;
  `nv2a_vsh.c/h`, `nv2a_psh.c`, `dsp/` are GPL-2+; `smbus_xbox_smc.c`, `amd_smbus.c/h` MIT.
- Verdict: read for facts only.

### Copetti — Xbox Architecture (local snapshot)
- `References/CopettiDocumentation/xbox-architecture.md` (+ `.html`, `images/`).
- **CC BY 4.0** — the article's own "Copyright and permissions" section requires
  attribution (link to the article + license, per its guidelines); commercial use
  permitted. If any text/figure is reused in XGrid docs, cite
  "Rodrigo Copetti, Xbox Architecture: A Practical Analysis" + the CC BY 4.0 link.
- Verdict: prose/architecture reference (onboarding, big-picture validation,
  README/background material). Not a register-level oracle — for addresses and
  register semantics use xemu / XboxDevWiki / envytools.

### envytools (master snapshot)
- `References/envytools-master/`. Root `COPYING` is the **MIT/Expat** license
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
- `References/nv2a-trace-master/`. **MIT**, Copyright (c) 2018-2022 XboxDev maintainers.
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
  must be preserved. `References/` inside that repo (extract-xiso, xdvdfs 0.8.3, XboxKit 0.7,
  Avalonia sources) is third-party material — do not copy.
- Caveat: `XISOSharp` has a runtime dependency on `ZArchiveSharp 1.4.0` (not BCL-only).
- Integrated 2026-10-11: `XGrid.XboxFs` references NuGet `XISOSharp` 1.4.2
  (centrally pinned in `Directory.Packages.props`; transitive `ZArchiveSharp`
  1.4.0 confirmed in restore graph). No source copied - consumption is via
  the public `XisoExplorer` API behind `XGrid.XboxFs/XdvdfsFileSystem`.

### CHDSharp (MIT)
- MIT License, Copyright (c) 2026 Peterson Fernandes and Gordon Jefferyes
  (sibling repo `C:/Users/HomePC/Dropbox/source/repos/CSharp_CHDSharp`,
  docs + source used for API facts).
- Integrated 2026-10-11: `XGrid.Storage` references NuGet `CHDSharp` 1.4.3
  (centrally pinned). No source copied - consumption is via the public
  `ChdFile` API (`Open`/`Read`/`TotalBytes`) behind
  `XGrid.Storage/ChdDvdImage`.

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
| GPU backend performance design | **xemu-Playtest-v2 `nv2a` vs `nv2a-stock` diff** (cost model, shader seeds, uniform cache, writeback, driver probe) | xemu 0.8.136 `pgraph/` | Facts for the future host-GPU backend; `diff -ru` isolates the perf delta |
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
3. XISOSharp is the only approved direct dependency; record any version change here.
4. Maintain this file when a new reference is studied (plan §46).
