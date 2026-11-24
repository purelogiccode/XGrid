# Licensing

XGrid is free software. This page is a summary; the binding texts are `COPYING`,
`LICENSE` and `THIRD_PARTY_NOTICES.md`.

## XGrid

XGrid's own source is licensed under the **GNU General Public License, version 2
or later** (GPL-2.0-or-later).

## The emulation core

The core is the vendored **xemu 0.8.139** codebase and its **QEMU 10.2.0** base.
Those files keep their original per-file notices:

- GPL-2.0 / GPL-2.0-or-later;
- LGPL-2.0-or-later;
- LGPL-2.1-or-later;
- MIT.

XGrid's additions in `xemu/xgrid-core/` are GPL-2.0-or-later. Edits to upstream
files are recorded in `xemu/xemu-patches/`.

## Third-party components

| Component | License | Role |
| --- | --- | --- |
| xemu / QEMU | GPL-2.0 family, LGPL, MIT per file | Emulation core. |
| libchdr | BSD-3-Clause | CHD decoding. |
| Avalonia | MIT | Desktop UI framework. |
| CommunityToolkit.Mvvm | MIT | MVVM helpers. |
| Silk.NET.OpenGL | MIT | Managed OpenGL bindings in the oracle. |
| XISOSharp | MIT | XDVDFS reader adapter. |
| CHDSharp | MIT | Managed CHD reader in the oracle. |
| SDL3, GLib, OpenSSL, libcurl, zstd, and other core DLLs | see notices | Redistributed with the Windows core package. |

The complete list, including every runtime DLL and ported file, is
`THIRD_PARTY_NOTICES.md`.

## Combined Windows package

The Windows frontend archive redistributes the core's runtime DLLs. Some of them
are LGPL-3.0-or-later (`libidn2`, `libunistring`). Because XGrid's own code is
GPL-2.0-or-later, the combined distribution is made under GPL-3.0-or-later where
those libraries are included; the GPL-2.0-only xemu/QEMU files are used under
GPL-2.0, which that combination permits.

If you redistribute XGrid binaries:

- include `COPYING`, `LICENSE` and `THIRD_PARTY_NOTICES.md`;
- include the dependency license texts required by the notices;
- provide the corresponding source, including `xemu/` and
  `xemu/xemu-patches/` (see [Source availability](source-availability.md));
- do not add firmware, BIOS images, EEPROMs or game data.

## Firmware and game data

XGrid ships none. Users are responsible for the firmware and disc images they
use. The GPL does not grant rights to Microsoft's firmware or game content.

## No warranty

XGrid is distributed in the hope that it will be useful, but **without any
warranty**, without even the implied warranty of merchantability or fitness for a
particular purpose. See `COPYING`.

## Porting and provenance

Ported code keeps its original copyright and license notice and is recorded in
`THIRD_PARTY_NOTICES.md`. The workflow is [Porting and provenance](porting-policy.md).

## Not legal advice

This page summarizes the project's licensing posture. If you intend to
redistribute XGrid commercially or under a different license, review the actual
license texts and, where appropriate, obtain legal advice.
