# Licensing

XGrid is free software. Binding texts: `COPYING`, `LICENSE`,
`THIRD_PARTY_NOTICES.md`.

## XGrid

XGrid's own source is **GPL-2.0-or-later**.

## Core

The core began as **xemu 0.8.139** on a **QEMU 10.2** base and is now maintained
as part of XGrid. Files keep their original GPL-2.0, LGPL-2.0/2.1-or-later and
MIT notices. XGrid's additions in `xemu/xgrid-core/` are GPL-2.0-or-later;
original edit provenance is recorded in `xemu/xemu-patches/`.

## Third parties

| Component | License |
| --- | --- |
| xemu / QEMU | GPL-2.0 family, LGPL, MIT per file |
| libchdr | BSD-3-Clause |
| libCCI | GPL-2.0-or-later |
| xemu-Playtest-v2 / XPSemu-Chihiro | GPL-2.0 overall; LGPL/BSD per file |
| LZ4 | BSD-2-Clause |
| Avalonia | MIT |
| CommunityToolkit.Mvvm | MIT |
| Serilog | Apache-2.0 |
| Silk.NET.OpenGL / XISOSharp / CHDSharp | MIT |
| chihiro-netboot | MIT (format reference) |

The complete list is `THIRD_PARTY_NOTICES.md`.

## Combined Windows package

The Windows archive redistributes the core's runtime DLLs. Some are
LGPL-3.0-or-later, so the combined distribution is made under
GPL-3.0-or-later where those libraries are included. The GPL-2.0-only xemu/QEMU
files are used under GPL-2.0, which that combination permits.

## Linux package

The Linux archive redistributes the standalone core and its `LICENSE.txt`. The
core links the host's system libraries rather than bundling them.

## Firmware and games

XGrid ships none. The GPL does not grant rights to Microsoft's firmware or game
content.

## Redistribution

Include `COPYING`, `LICENSE`, `THIRD_PARTY_NOTICES.md` and `licenses/`; provide
the corresponding source including `xemu/` and `xemu/xemu-patches/`; do not add
firmware or game data.

Full page: [Licensing](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/licensing.md)
and [Release licensing](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/release-licensing.md).
