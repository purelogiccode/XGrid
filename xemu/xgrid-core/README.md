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
| `block/xgridchd.c` | QEMU block driver that decodes CHD hunks on demand, so a compressed disc plays without extraction. |
| `block/xgridzar.c` | QEMU block driver for ZArchive discs: serves an entry-form archive's `.iso` entry directly and synthesizes an XDVDFS volume for a file-tree archive, decoding zstd blocks on demand. |
| `display/xgrid-display.c` | Host display sink: publishes presented frames to a named section for the shell. |
| `input/xgrid-input.c` | Host input source: each Xbox port reads its pad record from the shell's section. |
| `abi/xgrid-core.c` | In-process ABI: starts one machine on its own thread, exposes stop/join/version, playback, disc and save-state requests. |
| `abi-baseline.json` | Version, upstream commit and ABI export list checked by `scripts/core-baseline.ps1`. |
| `thirdparty/libchdr/` | libchdr 0.3.0 (BSD-3-Clause, Copyright Romain Tisserand) and its small LZMA decoder, vendored so the build never fetches the network. |

## In-process core

`meson.build` builds `xgrid_core.dll` from the same objects as the emulator
executable (`xemu/meson.build`, the `xgrid_core` shared library). The shell
loads it with `NativeLibrary.Load` and calls `xgrid_core_start`; frames and pad
state use the same named sections as the out-of-process core, so the shell's
presentation path does not change. The ABI is
`xgrid_core_start(config, display_sink, input_sink, argv, argc)`,
`xgrid_core_request_stop`, `xgrid_core_is_running`, `xgrid_core_exit_code`,
`xgrid_core_join` and `xgrid_core_version`.

### Playback control

`xgrid_core_set_paused(1/0)` pauses or resumes the machine, `xgrid_core_is_paused`
reports the shell's intent and `xgrid_core_frame_step` runs one frame while
paused. The requests are queued and applied as a bottom half on the core's main
loop, where the run-state transition is allowed. A pause stops the machine on
the next frame the display path publishes, and a step resumes from that boundary
and stops after the next frame. A 50 ms timer does the same when the sink is off,
because then no frame is published.

### Disc change

`xgrid_core_disc_load(path)` and `xgrid_core_disc_eject` queue a block-layer
medium change; `xgrid_core_disc_status` and `xgrid_core_disc_error` report it.
The load picks the driver from the extension (`xgridchd`, `xgridzar`, or raw)
and cycles the tray so the guest notices. No power cycle is involved.

### Input

`xgrid_input_read(port, ...)` serves one 32-byte record per Xbox port from the
shell's section: buttons, four stick axes, two triggers and a sequence word.
When the sink is enabled, `ui/xemu-input.c` binds a synthetic controller to
every port the keyboard did not take (recorded in
`xemu-patches/0008-ui-shell-pads.patch`), so players 2-4 can be driven from the
shell. The bindings are not saved to the core configuration.

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
