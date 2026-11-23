# XGrid core additions

These are the files we own inside the emulation core. They are **tracked in this
repository** and compiled into the xemu tree by
`native/xemu-patches/0003-meson-include-xgrid-core.patch`, which adds one
`subdir('../xgrid-core')` call after the block subsystem is initialised.

Nothing here is a GitHub fork: `References2/xemu-0.8.139` is the pristine
upstream baseline, `native/xemu/` is a throwaway build tree, and this folder plus
`native/xemu-patches/` **is** the version of xemu that XGrid maintains.

## Layout

| Path | What it is |
|---|---|
| `block/xgridchd.c` | QEMU block driver that decodes CHD hunks on demand, so a compressed disc plays without extraction. |
| `thirdparty/libchdr/` | libchdr 0.3.0 (BSD-3-Clause, Copyright Romain Tisserand) and its small LZMA decoder, vendored so the build never fetches the network. |

## Build

```
pwsh -File scripts/sync-native-xemu.ps1      # recreate native/xemu and apply the patch set
pwsh -File scripts/build-native-xemu.ps1     # MSYS2 MINGW64 build, produces native/xemu/dist
```

## Licensing

The driver is GPL-2.0 (this project). libchdr is BSD-3-Clause and is recorded in
`THIRD_PARTY_NOTICES.md`; its licence text ships in
`thirdparty/libchdr/LICENSE.txt`. The release ships the core source, our
additions and `native/xemu-patches/`, as GPL-2.0 requires.
