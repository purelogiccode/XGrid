# Architecture

XGrid is two cooperating parts: a **native emulator core** and a **managed
desktop shell**. The core began as xemu 0.8.139 and now evolves as part of XGrid;
the shell owns everything the user touches.

```text
┌──────────────────────────────────────────────────────────────┐
│ XGrid.Frontend.Avalonia                                      │
│ Home · Library · Settings · Activity · About                │
│                                                              │
│ settings.json → launch plan → generated xemu.toml            │
│ shell pad → shared input     save-state requests → core ABI  │
│ native window host ← core    status/errors ← core            │
└───────────────────────────┬──────────────────────────────────┘
                            │ in-process DLL or separate process
┌───────────────────────────▼──────────────────────────────────┐
│ XGrid core (xemu 0.8.139 lineage + XGrid changes)            │
│ QEMU TCG i386 · NV2A · MCPX · APU/ACI · IDE · USB · SMBus    │
│ xgridiso · xgridcci · xgridchd · xgridzar · display/input/ABI│
└──────────────────────────────────────────────────────────────┘
```

## The core

The emulator is the **XGrid core** in `xemu/`. It began from **xemu 0.8.139**
and its QEMU 10.2 base, but it is now a custom enhanced and patched core that
evolves separately from the original xemu codebase. It is the native emulator,
not a managed port. It provides:

- the QEMU TCG i386 CPU core;
- the NV2A GPU and its OpenGL/Vulkan/Direct3D 11/NULL renderers (Direct3D 11 is
  Windows-only and a preview backend);
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
| `display/xgrid-display.c` | Presentation filter/overlay selection and the headless frame sink. |
| `input/xgrid-input.c` | Reads pad records from a named shared section. |
| `abi/xgrid-core.c` | In-process start/stop/join/version, playback and save-state bridge. |
| `hw/xbox/chihiro/` | Sega Chihiro machine, media/base boards and cabinet devices. |
| `block/blkmemory.c` | Memory-backed block driver used by the Chihiro DIMM. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 (BSD-3-Clause). |
| `thirdparty/libcci/` | Vendored libCCI (GPL-2.0-or-later), built against the host LZ4. |

Original edit provenance is recorded in `xemu/xemu-patches/`; XGrid-native
additions live in `xemu/xgrid-core/`. The original xemu tree is a reference
point, not a required merge target. See
[Core lineage and upstream](../development/xemu-upstream.md).

## Why the core is native

The native XGrid core, built on the xemu/QEMU lineage, is the product path
because it provides mature device emulation and the performance a playable
emulator needs. XGrid invests in the shell, the media drivers and the core
enhancements rather than replacing the emulator.

The managed validation projects and their test suite remain in the repository as
a **reference oracle** for validation. They are not the product path.

## Hosting modes

### In-process (Windows default)

- The shell loads `xgrid_core.dll` with `NativeLibrary.Load`.
- The machine runs on its own thread inside the shell process.
- The core's own window is embedded in the Home page and paints with its own
  swapchain. The shell copies no frames.
- The shell publishes pad records to a named shared section.
- Presentation filters, the FPS overlay, the presented frame-rate limit and
  frame skipping are core features selected through the ABI
  (`xgrid_core_set_present_filter`, `xgrid_core_set_fps_overlay`,
  `xgrid_core_set_fps_limit`, `xgrid_core_set_frame_skip`); the shell never
  touches a pixel.
- Save states, pause/resume, frame step, fast-forward and disc swap use the same
  ABI.
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
- `XGRID_PRESENT_FILTER`, `XGRID_FPS_OVERLAY`, `XGRID_FPS_LIMIT` and
  `XGRID_FRAME_SKIP` carry the initial presentation selection. A live change
  needs the in-process ABI.
- The core writes `xemu.log` in its working directory.
- Save states, pause/step, fast-forward and live disc swap are not exposed,
  because the shell has no ABI to call.

### Linux and macOS

The packaged `linux-x64` frontend archive carries the standalone core under
`core/xemu/`. The shell starts it as a separate process, and the core owns its
SDL window: presentation, filters, overlay, keyboard and gamepad input stay
inside the core. The shell keeps the library, settings, logs and screenshots.

Native in-window embedding and the host pad sink are Windows-only, so save
states, pause/step and disc swap are unavailable there. macOS archives carry the
shell only; point **Emulator binary** at an upstream xemu 0.8.139 build.

On Unix, `RequestStop` sends `SIGTERM`, which QEMU handles as a shutdown request,
so the core flushes storage instead of being killed.

## Picture ownership

The core owns every emulated pixel. It renders, filters, overlays, paces and
presents with its own window and swapchain. The Avalonia shell owns chrome,
layout and controls only. It must not receive frames, keep a frame bitmap, apply
a filter or draw an overlay over the picture; doing so reintroduces a
per-frame copy and a shell redraw into the hot path.

Presentation pacing is a core decision. The guest's vblank thread decides when
new guest frames exist; the render loop decides how often the latest one is
presented. A frame-rate limit and frame skipping therefore live in the core next
to the filter and overlay, and the shell only selects them. The guest clock is
not touched, so a 60 fps limit changes the presentation rate, not the game
speed. Fast-forward scales the limit with the speed multiplier.

The presentation target is fully cleared and initialized before every blit. A
clear only covers the scissor rectangle and writes enabled color channels, so
the core resets scissor/color-mask state and clamps edge sampling; this keeps a
stale or uninitialized band from appearing at the bottom of a resized embedded
window.

The only shell-side picture work is native window hosting (Windows). In-process
screenshots are written by the core on request; the shell never reads the frame.
Separate-process hosting falls back to a whole-window capture.

## Input sink

On Windows the shell publishes a 144-byte section with one 32-byte record per
Xbox pad:

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

Player 1 merges the keyboard with its assigned gamepad; players 2–4 carry their
gamepad only. The shell publishes neutral records immediately at start, so the
core never reads uninitialized state.

The core reads the section only when the input sink is configured. It binds a
controller to every port the keyboard did not take, so all four records reach the
guest; the bindings are not persisted. The sink is used in both presentation
modes on Windows. Elsewhere the core uses SDL input instead.

## Save states

A save state is xemu's own QEMU snapshot of the hard-disk image. The shell queues
a request through the in-process ABI:

```text
xgrid_core_snapshot_save/load/delete/refresh
xgrid_core_snapshot_status/error/count/name
```

The request slot accepts one operation at a time. A request made before the main
loop exists is refused so the shell can retry.

The request runs as a bottom half on the core's main loop because the block layer
requires the main thread and the thumbnail reads the renderer's GL context. The
shell polls the status and reports completion.

## Playback control

Pause, resume and frame step use the in-process ABI. Fast-forward uses the same
ABI in-process and the registered `XGrid.SetSpeed` window message for a separate
process:

```text
xgrid_core_set_paused(bool)   xgrid_core_is_paused()   xgrid_core_frame_step()
xgrid_core_set_speed(int)     xgrid_core_get_speed()
```

A request is queued and applied as a bottom half on the core's main loop. A pause
stops the machine on the next published frame; a frame step resumes from that
frame boundary and stops the same way after the next one. When no frame is
published (the display sink is off), a short timer stops the machine instead. The
shell keeps the paused state it asked for and reports it in the status bar, the
toolbar and a badge over the picture.

Fast-forward scales the guest's `QEMU_CLOCK_VIRTUAL` and the vblank interval
together, so the vCPU timers, the Xbox TSC and the presented frame rate all run
at 4×. The shell toggles it with Space or the **4×** toolbar button and restores
1× on the next press, pause or session end. In-process hosting sends the change
through the ABI; a separate process receives it as the registered
`XGrid.SetSpeed` window message, so the shortcut works in both modes.

## Disc change

The running console can change its disc without a power cycle:

```text
xgrid_core_disc_load(path)   xgrid_core_disc_eject()
xgrid_core_disc_status()     xgrid_core_disc_error()
```

The request runs as a bottom half on the core's main loop, where the block layer
expects it. The medium format follows the file extension (`.iso`/`.xiso` through
`xgridiso`, `.cci` through `xgridcci`, `.chd` through `xgridchd`, `.zar` through
`xgridzar`), and the tray is cycled so the guest notices. The shell polls the
status, then updates the current title. This needs in-process hosting, like save
states.

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

## Core capabilities

The core build scripts (`scripts/build-native-xemu.ps1` on Windows and
`scripts/build-native-xemu.sh` on Linux) write `xgrid-core.json` beside the core:

```json
{
  "core": "xemu",
  "version": "0.8.139",
  "renderers": ["D3D11", "OPENGL", "VULKAN", "NULL"],
  "audioDrivers": ["dsound", "none"]
}
```

The shell reads it to decide which renderers and audio backends to offer. A build
without Vulkan does not show Vulkan, and a saved Vulkan choice is coerced to
OpenGL with a warning. The same rule applies to Direct3D 11: only a Windows core
that reports `D3D11` offers it, and a saved `D3D11` choice on another build is
coerced to OpenGL.

## Diagnostics and services

The shell routes logs through Serilog and keeps rolling files beside its data.
Warnings and errors go to a bounded background queue that forwards environment
and exception details to the bug-report service. The same diagnostics project
owns the startup statistics hit, the GitHub release check and the donation
verification client. None of these calls run on the emulation thread, and every
one is best-effort: a network failure is logged and ignored.

The donation banner is presentation state: `HasDonated` lives in `settings.json`,
and only a successful verification response hides the banner.

## Related pages

- [xemu baseline differences](upstream-differences.md) — the historical delta
  from pristine xemu 0.8.139.
- [Configuration](configuration.md) — files, paths and environment variables.
- [Service endpoints](service-endpoints.md) — every outbound call and opt-out.
- [The native core](../development/core.md) — building and extending the core.
- [Save states](../guides/save-states.md) — user-facing behavior.
- [Platform embedding](../development/platform-embedding.md) — the non-Windows
  window-hosting plan.
