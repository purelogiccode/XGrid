# XGrid core additions

These are the files we own inside the emulation core. They are **tracked in this
repository** and compiled into the xemu tree by
`xemu/xemu-patches/0003-meson-include-xgrid-core.patch`, which adds one
`subdir('xgrid-core')` call after the block subsystem is initialised.

`References2/xemu-0.8.139` is the pristine upstream baseline. `xemu/` is the
tracked tree: 0.8.139 + this folder + `xemu/xemu-patches/`. A new xemu release
is a patch rebase, not a re-port.

## Layout

| Path | What it is |
|---|---|
| `block/xgridchd.c` | QEMU block driver that decodes CHD hunks on demand, so a compressed disc plays without extraction. |
| `block/xgridzar.c` | QEMU block driver that exposes a ZArchive's disc image as the guest DVD, decoding zstd blocks on demand. |
| `display/xgrid-display.c` | Host display sink: publishes presented frames to a named section for the shell. |
| `input/xgrid-input.c` | Host input source: port 1 reads the shell's pad state. |
| `thirdparty/libchdr/` | libchdr 0.3.0 (BSD-3-Clause, Copyright Romain Tisserand) and its small LZMA decoder, vendored so the build never fetches the network. |

## Build

```powershell
pwsh -File scripts/build-native-xemu.ps1     # MSYS2 MINGW64 build, produces xemu/dist
```

The solution also carries `xemu/XGrid.Xemu.Tree.csproj`; the native build is
opt-in with `dotnet build -p:BuildXemuCore=true`.

## Licensing

The XGrid additions are GPL-2.0-or-later (this project). libchdr is
BSD-3-Clause and is recorded in `THIRD_PARTY_NOTICES.md`; its licence text ships
in `thirdparty/libchdr/LICENSE.txt`. The release ships the core source, our
additions and `xemu/xemu-patches/`.
