# XGrid core additions

These are the files we own inside the emulation core. They are **tracked in this
repository** and wired into the xemu build by `meson.build` and the patch record
in `xemu/xemu-patches/`. The build adds one `subdir('xgrid-core')` call after the
block subsystem is initialised.

`References2/xemu-0.8.139` is the pristine upstream baseline. `xemu/` is the
tracked tree: 0.8.139 + this folder + `xemu/xemu-patches/`. A new xemu release
is a patch rebase, not a re-port.

## Layout

| Path | What it is |
|---|---|
| `block/xgrid-iso.c` | Shared XDVDFS game-partition detection (optimized XISO and the XGD retail offsets) used by the disc drivers. |
| `block/xgridiso.c` | QEMU block driver for ISO/XISO images: maps the detected game partition to LBA 0, so a full Redump image boots without conversion. |
| `block/xgridcci.c` | QEMU block driver for CCI images: LZ4 sectors through vendored libCCI, split-part discovery and game-partition mapping. |
| `block/xgridchd.c` | QEMU block driver that decodes CHD hunks on demand and maps the detected game partition. |
| `block/xgridzar.c` | QEMU block driver for ZArchive discs: serves an entry-form archive's `.iso` entry directly and synthesizes an XDVDFS volume for a file-tree archive, decoding zstd blocks on demand. |
| `display/xgrid-display.c` | Presentation filter/overlay selection and the headless display sink. The core always owns the picture; the shell never presents frames. |
| `input/xgrid-input.c` | Host input source: each Xbox port reads its pad record from the shell's section. |
| `abi/xgrid-core.c` | In-process ABI: starts one machine on its own thread, exposes stop/join/version, playback, disc and save-state requests. |
| `abi-baseline.json` | Version, upstream commit and ABI export list checked by `scripts/core-baseline.ps1`. |
| `thirdparty/libchdr/` | libchdr 0.3.0 (BSD-3-Clause, Copyright Romain Tisserand) and its small LZMA decoder, vendored so the build never fetches the network. |
| `thirdparty/libcci/` | libCCI 0.1 (GPL-2.0-or-later, Copyright Team-Resurgent), vendored and built against the host LZ4. |

## In-process core

`meson.build` builds `xgrid_core.dll` from the same objects as the emulator
executable (`xemu/meson.build`, the `xgrid_core` shared library). The shell
loads it with `NativeLibrary.Load` and calls `xgrid_core_start`; the core's own
window paints with its own swapchain, so the shell never receives a frame. The
ABI is `xgrid_core_start(config, display_sink, input_sink, argv, argc)`,
`xgrid_core_request_stop`, `xgrid_core_is_running`, `xgrid_core_exit_code`,
`xgrid_core_join`, `xgrid_core_version`,
`xgrid_core_set_present_filter`, `xgrid_core_set_fps_overlay`,
`xgrid_core_set_fps_limit` and `xgrid_core_set_frame_skip`.

### Presentation filter, overlay and pacing

The shell selects one of nearest, bilinear, scanline or CRT. The final blit
shader in `ui/xui/gl-helpers.cc` applies it at source resolution; the shell never
processes pixels. The frame-rate overlay is drawn by the core's ImGui HUD in
`ui/xui/main.cc`. Both are patch-recorded in
`xemu-patches/0010-ui-presentation-filter-overlay.patch`.

The render loop in `ui/xemu.c` owns presentation pacing. `xgrid_present_frame_limit`
returns 0 for the historical unlimited pace or a 1..1000 fps cap;
`xgrid_present_frame_skip` chooses whether a late render drops its missed
deadlines or catches up with a burst. The guest clock is untouched. Fast-forward
multiplies the limit by the speed multiplier. The separate-process path passes
the initial values through `XGRID_PRESENT_FILTER`, `XGRID_FPS_OVERLAY`,
`XGRID_FPS_LIMIT` and `XGRID_FRAME_SKIP`.

### Playback control

`xgrid_core_set_paused(1/0)` pauses or resumes the machine, `xgrid_core_is_paused`
reports the shell's intent and `xgrid_core_frame_step` runs one frame while
paused. The requests are queued and applied as a bottom half on the core's main
loop, where the run-state transition is allowed. A pause stops the machine on
the next frame the display path publishes, and a step resumes from that boundary
and stops after the next frame. A 50 ms timer does the same when the sink is off,
because then no frame is published.

`xgrid_core_set_speed(1..8)` applies its virtual-clock scale through the same
bottom-half path. `system/cpus.c` stores the scale, anchor and base in a seqlock,
so timer and vCPU readers always see one consistent snapshot while the shell
changes the pace. `xemu_set_speed_multiplier` also scales the vblank interval, so
the guest timers, the Xbox TSC and the presented frame rate move together.

A separate core process has no ABI. It handles the registered `XGrid.SetSpeed`
window message in `ui/xemu.c` and applies the same multiplier, so the shell's
Space/4× control works in both hosting modes.

### Disc change

`xgrid_core_disc_load(path)` and `xgrid_core_disc_eject` queue a block-layer
medium change; `xgrid_core_disc_status` and `xgrid_core_disc_error` report it.
The load picks the driver from the extension (`xgridiso` for `.iso`/`.xiso`,
`xgridcci` for `.cci`, `xgridchd` for `.chd`, `xgridzar` for `.zar`) and cycles
the tray so the guest notices. No power cycle is involved. The ISO/XISO, CCI,
CHD and entry-form ZArchive drivers detect the XDVDFS game-partition offset, so
optimized and full Redump images both appear at LBA 0.

### Input

`xgrid_input_read(port, ...)` serves one 32-byte record per Xbox port from the
shell's section: buttons, four stick axes, two triggers and a sequence word.
When the input sink is enabled, `ui/xemu-input.c` binds a synthetic controller to
every port the keyboard did not take (recorded in
`xemu-patches/0008-ui-shell-pads.patch`), so players 2-4 can be driven from the
shell. The bindings are not saved to the core configuration.

The Chihiro JVS input in the same file also reads per-device gun pointers from
the host: Raw Input on Windows, evdev on Linux and IOHID on macOS. The first two
absolute devices aim players 1 and 2, and relative mice start at the centre of
the picture. `chihiro.settings.pointer_p1` and `pointer_p2` can pin a device by
name; the shell fills those names from the pointer lines in `xemu.log`. This is
recorded with the Chihiro patch.

### Save states

`xgrid_core_snapshot_save/load/delete/refresh` queue one request at a time;
`xgrid_core_snapshot_status` (idle, pending, completed, failed),
`xgrid_core_snapshot_error`, `xgrid_core_snapshot_count` and
`xgrid_core_snapshot_name` report it. A save state is xemu's own internal
snapshot of the hard disk image (QEMU `savevm`), so it includes the VM state and
the frame thumbnail and survives the process.

The work runs as a bottom half on the core's main loop, because the block layer
asserts it is on the main thread and the thumbnail reads the renderer's GL
context, which lives on that same loop. The shell therefore never blocks on a
snapshot and polls the status; a request made before the main loop exists (the
first moments after `start`) is refused with `-1` so the caller can retry.

A DLL, not the EXE: the Windows loader initialises a DLL's CRT, which an EXE
loaded with `LoadLibrary` does not do. The DLL is linked at a fixed image base
with a larger thread stack, because QEMU's TCG needs both inside a host
process.

### The setjmp/longjmp shim

QEMU's CPU loop leaves a translation block with `siglongjmp(cpu->jmp_env, 1)`.
Inside a managed host, msvcrt's `longjmp` rejects that buffer and raises
`STATUS_STACK_BUFFER_OVERRUN` (0xC0000409) even though the buffer and the target
stack are valid. `include/system/os-win32.h` therefore maps the pair to
`xgrid_sigsetjmp`/`xgrid_siglongjmp` (`util/oslib-win32.c`), which use
`RtlCaptureContext`/`RtlRestoreContext` directly:

* the capture happens in the shim, so the saved `Rip`/`Rsp`/`Rbp` are replaced
  with the **caller's** return address and stack pointer before returning —
  a setjmp-style call must resume in the frame that is still alive;
* the jump value travels in `Rax`, which is the caller's return value;
* no unwinding is needed: the CPU loop only ever jumps back to its own
  `cpu_exec` frame.

The shell owns the process streams. `xgrid_core_start` does not redirect
stdout/stderr: a loaded library shares them with the host, so a redirect would
capture the shell's own output too.

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
