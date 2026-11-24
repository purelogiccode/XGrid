# The XGrid core

The core is the tracked xemu 0.8.139 tree in `xemu/`. XGrid does not maintain a
separate fork repository; the tree and its patch record travel with the shell.

## Tree structure

```text
xemu/
  <upstream xemu 0.8.139 and its QEMU base>
  xgrid-core/          XGrid-owned native code
  xemu-patches/        patch record for upstream edits
  XGrid.Xemu.Tree.csproj
  dist/                build output (git-ignored)
```

### `xgrid-core/`

| File | Role |
| --- | --- |
| `block/xgridchd.c` | CHD block driver; decodes hunks on demand through libchdr. |
| `block/xgridzar.c` | ZArchive block driver; decodes zstd blocks and exposes the largest `.iso` entry. |
| `display/xgrid-display.c` | Frame sink; publishes BGRA frames to a named shared section. |
| `input/xgrid-input.c` | Input sink; reads the shell's player-1 pad. |
| `abi/xgrid-core.c` | In-process host ABI and save-state request bridge. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 (BSD-3-Clause). |
| `meson.build` | Wires the additions into the xemu build. |

### `xemu-patches/`

Edits to upstream files are kept as patches. The current patch record includes
the in-process host changes: the Windows `sigsetjmp`/`siglongjmp` shim, the
QEMU thread stack size and the xemu run entry point.

A new xemu release is handled by rebasing the patch set and the `xgrid-core/`
additions, not by re-porting the emulator.

## In-process ABI

```c
int  xgrid_core_start(const char *config_path,
                      const char *display_sink,
                      const char *input_sink,
                      const char *const *extra_argv,
                      int extra_argc);
void xgrid_core_request_stop(void);
int  xgrid_core_is_running(void);
int  xgrid_core_join(void);
int  xgrid_core_exit_code(void);
const char *xgrid_core_version(void);
```

Save-state functions:

```c
int  xgrid_core_snapshot_save(const char *name);
int  xgrid_core_snapshot_load(const char *name);
int  xgrid_core_snapshot_delete(const char *name);
int  xgrid_core_snapshot_refresh(void);
int  xgrid_core_snapshot_status(void);
const char *xgrid_core_snapshot_error(void);
int  xgrid_core_snapshot_count(void);
const char *xgrid_core_snapshot_name(int index);
```

Playback functions:

```c
int xgrid_core_set_paused(int paused);
int xgrid_core_is_paused(void);
int xgrid_core_frame_step(void);
```

Disc functions:

```c
int xgrid_core_disc_load(const char *path);
int xgrid_core_disc_eject(void);
int xgrid_core_disc_status(void);
const char *xgrid_core_disc_error(void);
```

A disc load opens the medium through the block layer with the format the file
extension names: `.chd` uses `xgridchd`, `.zar` uses `xgridzar`, everything else
is raw. The tray is cycled so Xbox software notices the change; no power cycle
is involved.

Pause and frame step are queued like the save-state requests and applied on the
core's main loop. A pause stops the machine on the next published frame, and a
step resumes from that boundary and stops after the next frame. A 50 ms timer is
the fallback when the display sink is off, because then no frame is published.

One process hosts one machine. Start is accepted once; join releases the thread
handle after the run loop stops.

### Why a DLL?

The Windows loader initializes a DLL's CRT; an EXE loaded with `LoadLibrary` does
not. The DLL is linked at a fixed image base with a larger thread stack because
QEMU TCG needs both inside a host process.

### The `sigsetjmp` shim

QEMU's CPU loop leaves a translation block with `siglongjmp`. Inside a managed
host, msvcrt rejects the buffer and raises `STATUS_STACK_BUFFER_OVERRUN` even
when the buffer and target stack are valid. XGrid maps the pair to
`RtlCaptureContext`/`RtlRestoreContext`, rewrites the saved instruction/stack
pointers to the caller's frame and delivers the jump value in `Rax`. No SEH
unwind is needed because the CPU loop only jumps back to its own `cpu_exec`
frame.

## Build and validation

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Output: `xemu/dist/xemu.exe`, `xemu/dist/xgrid_core.dll`,
`xemu/dist/xgrid-core.json` and the runtime DLLs.

## Adding an enhancement

1. Prefer `xemu/xgrid-core/` for new files.
2. If an upstream file must change, keep the change minimal and record it in
   `xemu/xemu-patches/`.
3. Keep the original copyright/license notice on derived files; add SPDX and a
   `Ported from` line when porting.
4. Update `THIRD_PARTY_NOTICES.md` and `docs/legal/licensing.md`.
5. Build the core and run the shell tests.
6. Update the relevant documentation page in the same change.

## Licensing

XGrid's additions are GPL-2.0-or-later. libchdr is BSD-3-Clause. The vendored
xemu/QEMU files keep their original notices. See
[Licensing](../legal/licensing.md) and [Porting policy](../legal/porting-policy.md).
