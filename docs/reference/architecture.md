# Architecture

XGrid is two cooperating parts: a **native emulator core** and a **managed
desktop shell**. The split is deliberate: the core keeps the mature xemu
implementation and its performance, while the shell owns everything the user
touches.

```text
┌──────────────────────────────────────────────────────────────┐
│ XGrid.Frontend.Avalonia                                      │
│ Home · Library · Settings · Activity · About                │
│                                                              │
│ settings.json → launch plan → generated xemu.toml            │
│ shared frame section ← core        shell pad → shared input  │
│ save-state requests → core ABI     ← status/errors           │
└───────────────────────────┬──────────────────────────────────┘
                            │  in-process DLL or separate process
┌───────────────────────────▼──────────────────────────────────┐
│ xemu core (xemu 0.8.139 + XGrid additions)                   │
│ QEMU TCG i386 · NV2A · MCPX · APU/ACI · IDE · USB · SMBus    │
│ xgridchd · xgridzar · display sink · input sink · ABI        │
└──────────────────────────────────────────────────────────────┘
```

## The core

The emulator is the tracked **xemu 0.8.139** tree in `xemu/`, with a QEMU 10.2
base. It is not re-implemented in C#. It provides:

- the QEMU TCG i386 CPU core;
- the NV2A GPU and its OpenGL/Vulkan/NULL renderers;
- MCPX, APU/ACI, IDE/ATAPI, USB/OHCI, SMBus/SMC, PCI and the rest of the Xbox;
- the retail boot chain (MCPX ROM → 1BL → 2BL → kernel → dashboard);
- xemu's own configuration, snapshot and input infrastructure.

XGrid's additions live in `xemu/xgrid-core/`:

| Path | Purpose |
| --- | --- |
| `block/xgridchd.c` | Real-time CHD block driver (vendored libchdr). |
| `block/xgridzar.c` | Real-time ZArchive block driver (zstd). |
| `display/xgrid-display.c` | Publishes presented frames to a named shared section. |
| `input/xgrid-input.c` | Reads player 1 from a named shared section. |
| `abi/xgrid-core.c` | In-process start/stop/join/version and save-state bridge. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 (BSD-3-Clause). |

Edits to upstream files are recorded in `xemu/xemu-patches/`. A new xemu release
is a patch rebase, not a re-port.

## Why the core is native

The native xemu/QEMU core is the product path because it provides mature device
emulation and the performance a playable emulator needs. XGrid invests in the
shell, the media drivers and the integration seams rather than replacing the
emulator.

The managed C# emulator and its test suite remain in the repository as a
**reference oracle** for validation. They are not the product path.

## Hosting modes

### In-process (default)

- The shell loads `xgrid_core.dll` with `NativeLibrary.Load`.
- The machine runs on its own thread inside the shell process.
- Frames and input still use the same shared sections, so presentation is
  identical to the separate-process path.
- Save states are available through the in-process ABI.
- Pause, resume and single-frame step are available through the same ABI.
- A missing or unusable library falls back to the separate process with a log
  message.

### Separate process

- The shell starts `xemu.exe` with `-config_path <generated xemu.toml>`.
- The process gets the display and input sink names through environment
  variables (`XGRID_DISPLAY_SHM`, `XGRID_INPUT_SHM`).
- The core writes `xemu.log` in its working directory.
- Save states are not exposed; the shell has no ABI to call.
- Pause and frame step are not exposed for the same reason.

## Display sink

When the shell presents frames, the core writes a top-down BGRA image into a named
shared section:

```text
uint32 magic   'XGDS'
uint32 width
uint32 height
uint32 stride  bytes per row
uint64 frame   increments once per published frame
pixel data     width × height BGRA, top row first
```

The shell polls the section every 8 ms, keeps the latest frame, and presents at a
minimum 10 ms cadence. A slow UI drops frames instead of building latency.

## Input sink

The shell publishes a 144-byte section with one 32-byte record per Xbox pad:

```text
uint32 magic   'XGDI'
uint32 version 2
uint32 pads    4
uint32 reserved
per pad:
  uint32 buttons  xemu XID mask
  int32  lx, ly, rx, ry
  int32  lt, rt
  uint32 sequence increments once per shell update
```

Player 1 merges the keyboard with its assigned gamepad; players 2-4 carry their
gamepad only. The shell publishes neutral records immediately at start, so the
core never reads uninitialized state.

The core reads the section only when the input sink is configured. It binds a
controller to every port the keyboard did not take, so all four records reach
the guest; the bindings are not persisted. With the embedded core window, the
core uses SDL input instead.

## Save states

A save state is xemu's own QEMU snapshot of the hard-disk image. The shell queues
a request through the in-process ABI:

```text
xgrid_core_snapshot_save/load/delete/refresh
xgrid_core_snapshot_status/error/count/name
```

The request slot accepts one operation at a time. A request made before the
main loop exists is refused so the shell can retry.

The request runs as a bottom half on the core's main loop because the block layer
requires the main thread and the thumbnail reads the renderer's GL context. The
shell polls the status and reports completion.

## Playback control

Pause, resume and frame step use the in-process ABI:

```text
xgrid_core_set_paused(bool)   xgrid_core_is_paused()   xgrid_core_frame_step()
```

A request is queued and applied as a bottom half on the core's main loop, so the
run-state change happens where QEMU expects it. A pause stops the machine on the
next published frame; a frame step resumes from that frame boundary and stops
the same way after the next one. When no frame is published (the display sink is
off), a short timer stops the machine instead. The shell keeps the paused state
it asked for and reports it in the status bar, the toolbar and a badge over the
picture.

## Disc change

The running console can change its disc without a power cycle:

```text
xgrid_core_disc_load(path)   xgrid_core_disc_eject()
xgrid_core_disc_status()     xgrid_core_disc_error()
```

The request runs as a bottom half on the core's main loop, where the block
layer expects it. The medium format follows the file extension (`.chd` and
`.zar` through the XGrid block drivers, everything else raw), and the tray is
cycled so the guest notices. The shell polls the status, then updates the
current title. This needs in-process hosting, like save states.

## Core capabilities

The core build scripts (`scripts/build-native-xemu.ps1` on Windows and
`scripts/build-native-xemu.sh` on Linux) write `xgrid-core.json` beside the core:

```json
{
  "core": "xemu",
  "version": "0.8.139",
  "renderers": ["OPENGL", "NULL"]
}
```

The shell reads it to decide which renderers to offer. A build without Vulkan
does not show Vulkan, and a saved Vulkan choice is coerced to OpenGL with a
warning.

## Related pages

- [Configuration](configuration.md) — files, paths and environment variables.
- [The XGrid core](../development/core.md) — building and extending the core.
- [Save states](../guides/save-states.md) — user-facing behavior.
