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
│ xgridiso · xgridcci · xgridchd/xgridzar · display/input/ABI │
└──────────────────────────────────────────────────────────────┘
```

## The core

The emulator is the tracked **xemu 0.8.139** tree in `xemu/`, with a QEMU 10.2
base. It is not re-implemented in C#. It provides:

- the QEMU TCG i386 CPU core;
- the NV2A GPU and its OpenGL/Vulkan/Direct3D 11/NULL renderers (Direct3D 11
  is Windows only and currently a preview backend);
- MCPX, APU/ACI, IDE/ATAPI, USB/OHCI, SMBus/SMC, PCI and the rest of the Xbox;
- the retail boot chain (MCPX ROM → 1BL → 2BL → kernel → dashboard);
- xemu's own configuration, snapshot and input infrastructure.

XGrid's additions live in `xemu/xgrid-core/`:

| Path | Purpose |
| --- | --- |
| `block/xgrid-iso.c` | Shared XDVDFS game-partition detection for the disc drivers. |
| `block/xgridiso.c` | ISO/XISO block driver; maps optimized and full Redump game partitions to LBA 0. |
| `block/xgridcci.c` | CCI block driver; LZ4 sectors through vendored libCCI, split sets and partition-offset detection. |
| `block/xgridchd.c` | Real-time CHD block driver (vendored libchdr) with partition-offset detection. |
| `block/xgridzar.c` | Real-time ZArchive block driver (zstd); entry form and synthesized file tree. |
| `display/xgrid-display.c` | Publishes presented frames to a named shared section. |
| `input/xgrid-input.c` | Reads player 1 from a named shared section. |
| `abi/xgrid-core.c` | In-process start/stop/join/version and save-state bridge. |
| `hw/xbox/chihiro/` | Sega Chihiro machine, media/base boards and cabinet devices. |
| `block/blkmemory.c` | Memory-backed block driver used by the Chihiro DIMM. |
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
- The core's own window is embedded in the Home page and paints with its own
  swapchain, exactly like the separate-process path. The shell copies no frames.
- The pad shared section is used on Windows.
- Presentation filters and the FPS overlay are core features. The shell selects
  them through the ABI (`xgrid_core_set_present_filter`,
  `xgrid_core_set_fps_overlay`); it never touches a pixel.
- Save states are available through the in-process ABI.
- Pause, resume and single-frame step are available through the same ABI.
- QEMU's global state is single-use: the in-process core hosts one machine per
  shell. A later power-on falls back to the separate process with a log message
  and a toast; restart XGrid to use in-process features again.
- A missing or unusable library falls back to the separate process with a log
  message.

### Separate process

- The shell starts `xemu.exe` (or `xemu` on Linux) with
  `-config_path <generated xemu.toml>`.
- On Windows the process gets the input sink name through `XGRID_INPUT_SHM`.
- `XGRID_EMBED=1` asks the core to create its window hidden and titled
  `XGridCore`, ready for the shell to reparent without a flash.
- `XGRID_PRESENT_FILTER` and `XGRID_FPS_OVERLAY` carry the initial presentation
  selection. A live change needs the in-process ABI.
- The core writes `xemu.log` in its working directory.
- Save states are not exposed; the shell has no ABI to call.
- Pause and frame step are not exposed for the same reason.

### Linux and macOS

The packaged `linux-x64` frontend archive carries the standalone core under
`core/xemu/`. The shell starts it as a separate process, and the core owns its
SDL window: presentation, filters, overlay, keyboard and gamepad input stay
inside the core. The shell keeps the library, settings, logs and screenshots.
Native in-window embedding and the host pad sink are Windows-only, so save
states, pause/step and disc swap are unavailable there. macOS archives carry the
shell only; point the **Emulator binary** setting at an upstream xemu 0.8.139
build. The platform embedding plan is in
[Platform embedding](../development/platform-embedding.md).

On Unix, `RequestStop` sends `SIGTERM`, which QEMU handles as a shutdown
request, so the core flushes storage instead of being killed.

## Picture ownership

The core owns every emulated pixel. It renders, filters, overlays and presents
with its own window and swapchain. The Avalonia shell owns chrome, layout and
controls only. It must not receive frames, keep a frame bitmap, apply a filter
or draw an overlay over the picture; doing so reintroduces a per-frame copy and
a shell redraw into the hot path.

The only shell-side picture work is native window hosting (Windows). In-process
screenshots are written by the core on request; the shell never reads the frame.
Separate-process hosting falls back to a whole-window capture.

## Headless display sink

Headless runs and benchmarks request the core's display sink, which writes a
top-down BGRA image into a named shared section:

```text
uint32 magic   'XGDS'
uint32 width
uint32 height
uint32 stride  bytes per row
uint64 frame   increments once per published frame
pixel data     width × height BGRA, top row first
```

Windows uses a named file mapping; Linux and macOS use a POSIX shared-memory
object with the same layout. The core renders off-screen when the sink is active.
No shell window presents these frames; the runner reads the header to count
throughput.

Headless runs use the same sink on every host, but only read the 32-byte header:
they count the frame counter without copying pixels, so a benchmark does not pay
for presentation it never shows.

## Headless runner

`--headless` starts the core before Avalonia is initialized. It writes the same
generated `xemu.toml`, uses no input sink, defaults the audio backend to `none`,
and stops on `--seconds`, `--frames`, guest power-off or Ctrl+C. The run prints a
summary and can write the same fields as JSON. See
[Headless runs](../guides/headless.md).

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
the guest; the bindings are not persisted. The sink is used in both presentation
modes on Windows. Elsewhere the core uses SDL input instead.

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

Pause, resume, frame step and fast-forward use the in-process ABI:

```text
xgrid_core_set_paused(bool)   xgrid_core_is_paused()   xgrid_core_frame_step()
xgrid_core_set_speed(int)     xgrid_core_get_speed()
```

A request is queued and applied as a bottom half on the core's main loop, so the
run-state change happens where QEMU expects it. A pause stops the machine on the
next published frame; a frame step resumes from that frame boundary and stops
the same way after the next one. When no frame is published (the display sink is
off), a short timer stops the machine instead. The shell keeps the paused state
it asked for and reports it in the status bar, the toolbar and a badge over the
picture.

Fast-forward scales the guest's `QEMU_CLOCK_VIRTUAL` and the vblank interval
together, so the vCPU timers, the Xbox TSC and the presented frame rate all run
at 4x. The shell toggles it with Space or the **4x** toolbar button and restores
1x on the next press, pause or session end.

## Disc change

The running console can change its disc without a power cycle:

```text
xgrid_core_disc_load(path)   xgrid_core_disc_eject()
xgrid_core_disc_status()     xgrid_core_disc_error()
```

The request runs as a bottom half on the core's main loop, where the block
layer expects it. The medium format follows the file extension (`.iso`/`.xiso`
through `xgridiso`, `.cci` through `xgridcci`, `.chd` through `xgridchd`, `.zar`
through `xgridzar`), and the tray is cycled so the guest notices. The shell polls the status, then updates the
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
warning. The same rule applies to Direct3D 11: only a Windows core that reports
`D3D11` offers it, and a saved `D3D11` choice on another build is coerced to
OpenGL. `ImplementDirectX.md` records the renderer plan and current phase.

## Diagnostics and services

The shell routes logs through Serilog and keeps rolling files beside its data.
Warnings and errors go to a bounded background queue that forwards environment
and exception details to the bug-report service. The same diagnostics project
owns the single startup statistics hit, the GitHub release check and the
donation verification client. None of these calls run on the emulation thread,
and every one is best-effort: a network failure is logged and ignored.

The donation banner is presentation state: `HasDonated` lives in
`settings.json`, and only a successful verification response hides the banner.
The API contract and privacy notes are in [Donation API](donation-api.md) and
[Privacy and data](../legal/privacy.md).

## Related pages

- [Configuration](configuration.md) - files, paths and environment variables.
- [Service endpoints](service-endpoints.md) - every outbound call and opt-out.
- [The XGrid core](../development/core.md) - building and extending the core.
- [Save states](../guides/save-states.md) - user-facing behavior.
