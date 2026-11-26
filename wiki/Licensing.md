# Licensing

XGrid's own source is **GPL-2.0-or-later**.

The emulation core is **xemu 0.8.139** and its **QEMU 10.2.0** base, keeping their
original per-file GPL-2.0, LGPL and MIT notices. XGrid's additions in
`xemu/xgrid-core/` are GPL-2.0-or-later. Upstream edits are recorded in
`xemu/xemu-patches/`.

## Third-party components

- libchdr - BSD-3-Clause
- libCCI - GPL-2.0-or-later
- LZ4 - BSD-2-Clause
- Avalonia — MIT
- CommunityToolkit.Mvvm — MIT
- Silk.NET.OpenGL — MIT
- XISOSharp — MIT
- CHDSharp — MIT
- SDL3, GLib, OpenSSL, libcurl, zstd, LZ4 and other runtime DLLs - see
  `THIRD_PARTY_NOTICES.md`

The Windows core package redistributes runtime DLLs, including LGPL-3.0-or-later
libraries. The combined package is GPL-3.0-or-later where those are included;
GPL-2.0-only xemu/QEMU files are used under GPL-2.0, which that combination
permits. The Linux core package links the host's system libraries instead of
bundling them; it carries the core's own `LICENSE.txt` and the shared license
set.

## Firmware and games

XGrid ships no BIOS, MCPX ROM, EEPROM, HDD image or game data. The GPL does not
grant rights to Microsoft's firmware or game content.

## Redistribution

If you ship XGrid binaries:

- include `COPYING`, `LICENSE` and `THIRD_PARTY_NOTICES.md`;
- include the dependency license texts (`licenses/`);
- provide the corresponding source, including `xemu/` and
  `xemu/xemu-patches/`;
- do not add firmware or game data.

Full texts: [Licensing](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/licensing.md)
and [Release licensing](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/release-licensing.md).
