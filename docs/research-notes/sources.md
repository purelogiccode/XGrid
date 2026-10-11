# Reference Sources and Licensing Inventory

Date reviewed: 2026-10-08
Policy: XGrid is a clean-room reimplementation. These notes record factual hardware/format
knowledge (addresses, register maps, struct layouts, protocol constants, algorithms). No source
code from any reference project is copied into XGrid unless its license explicitly permits it and
the reuse is recorded below.

## Project inventory

| Project | Version/Snapshot | Upstream | License | Code copied? |
|---|---|---|---|---|
| xemu | 0.8.136 | https://github.com/xemu-project/xemu | GPL-2.0 overall; per-file LGPL-2+, MIT, GPL-2+ | No — facts only |
| Xermu | main snapshot | local copy (no upstream URL recorded) | **None — all rights reserved** | No — facts only |
| Cxbx-Reloaded | master | https://github.com/Cxbx-Reloaded/Cxbx-Reloaded | GPL-2.0 (some files LGPL-2) | No — facts only |
| xqemu | master (QEMU 4.0 base) | https://github.com/xqemu/xqemu | GPL-2.0 (mixed per-file) | No — facts only |
| XISOSharp | current master | https://github.com/purelogiccode/XISOSharp | **MIT** | Approved for direct dependency integration (see below) |

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

### Xermu (main)
- **No LICENSE, COPYING, or NOTICE anywhere. No SPDX headers.** Default copyright applies
  (all rights reserved).
- `doc/reference/` contains Cxbx-Reloaded HLSL shaders (GPL-2.0) — licensing hazard.
- `doc/NBOXKRNL_INTEGRATION.md` itself discusses GPL risk (nboxkrnl GPL-2.0, nxbx GPL-3.0).
- Verdict: design reference only. Do not copy source, including the NASM test files.

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
  1.4.0 confirmed in restore graph). No source copied — consumption is via
  the public `XisoExplorer` API behind `XGrid.XboxFs/XdvdfsFileSystem`.

## Subsystem → reference map (facts)

| XGrid subsystem | Primary reference | Secondary | Notes |
|---|---|---|---|
| Boot flow / reset vector | xemu `hw/xbox/xbox.c` | xqemu `xbox.c`, Xermu `bootstrap.hpp` | MCPX ROM overlay at top of BIOS |
| Physical memory map | xemu `xbox.c`, Xermu `address_map.hpp` | xqemu `xbox.c` | See constants appendix |
| Pentium III model/CPUID/MSR | xemu `target/i386` XBOX patches | — | Model 8/10, CPUID leaf quirks |
| PCI IDs/IRQ routing | xemu `xbox_pci.c` | xqemu `xbox_pci.c`, Xermu `pci.hpp` | Routing regs 0x64/0x68/0x6C |
| SMBus/SMC/EEPROM | xemu `amd_smbus.c`, `smbus_xbox_smc.c`, `eeprom_generation.c` | xqemu same, Xermu `smbus.hpp` | EEPROM layout/crypto |
| Flash | Xermu `flash.hpp` (CFI command set) | xemu (ROM only) | AMD/SST command model |
| APU | xemu `mcpx/apu/**` | xqemu `mcpx_apu.c` (single file) | DSP56300 semantics |
| NV2A registers/PFIFO/PGRAPH | xemu `hw/xbox/nv2a/**` | Cxbx `nv2a_regs.h`, xqemu | NV097 method map |
| Shaders (VSH/PSH) | Cxbx `nv2a_vsh/psh.cpp` | xemu `pgraph/glsl/**` (GPL) | Behavior only |
| Texture swizzle | Cxbx `swizzle.cpp` (LGPL-2) | xemu `pgraph/swizzle.c` | Algorithm facts |
| USB OHCI + XID | xemu `xid.c`, Cxbx `OHCI.cpp` | xqemu `xid.c` | Report formats |
| XBE format | Cxbx `Xbe.h` | xemu `xemu-xbe.h`, XISOSharp `XbeInfo` | Full struct offsets |
| Kernel exports | Cxbx `KernelThunk.cpp` | Xermu `hle_kernel.hpp` | 379 ordinals, semantics |
| XDVDFS | XISOSharp (MIT, reusable) | Cxbx `xdvdfs-tools` (GPL-2) | Format facts documented |
| FATX | **No implementation in workspace** | xemu superblock creator only | Must implement clean-room |
| CPU test strategy | Xermu NASM harness (concept) | — | Re-write tests ourselves |

## Rule going forward
1. Extract facts into notes like this one before implementing.
2. Never copy code from GPL/LGPL/unlicensed references.
3. XISOSharp is the only approved direct dependency; record any version change here.
4. Maintain this file when a new reference is studied (plan §46).
