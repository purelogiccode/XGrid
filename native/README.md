# Native core tree

XGrid is the shell; the emulator is the vendored **xemu 0.8.139** codebase
(GPL-2.0). The maintained core is:

```text
References2/xemu-0.8.139  (pinned baseline, never modified)
+ native/xgrid-core       (XGrid additions: CHD, ZAR, display sink, input sink)
+ native/xemu-patches     (the tracked patch set)
= native/xemu             (disposable build tree, git-ignored)
```

`native/xemu` is rebuilt by `scripts/sync-native-xemu.ps1` and compiled by
`scripts/build-native-xemu.ps1`. A new xemu release is a patch rebase, not a
re-port.

## Layout

| Path | What |
|---|---|
| `xemu-patches/` | The tracked patches applied to the pinned xemu baseline. |
| `xgrid-core/` | The XGrid additions copied into the build tree: `block/xgridchd.c`, `block/xgridzar.c`, `display/xgrid-display.c`, `input/xgrid-input.c`, and the vendored `libchdr`. |
| `xemu/` | The disposable build tree (**git-ignored**); never edit `References2/`. |

## Build

```powershell
pwsh -File scripts/sync-native-xemu.ps1
pwsh -File scripts/build-native-xemu.ps1
```

The output lands in `native/xemu/dist/` and is the `xemu.exe` the shell starts
(`core/xemu/xemu.exe` beside a packaged app, or `data/xemu/xemu.exe` in a
development checkout).

## History

An earlier program extracted the QEMU TCG i386 core behind `libxgridcpu`
(XCN0-XCN3). That path was **retired on 2026-11-23** (decision A): the whole
xemu codebase is the core, so the shim, its patches and its probes were
removed. Do not rebuild it.
