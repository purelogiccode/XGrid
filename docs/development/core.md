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
| `block/xgridzar.c` | ZArchive block driver; decodes zstd blocks, exposes an entry-form `.iso`, and synthesizes XDVDFS for file-tree archives. |
| `display/xgrid-display.c` | Presentation filter/overlay selection plus the headless frame sink (Windows named section or POSIX shared memory). |
| `input/xgrid-input.c` | Input sink; reads one pad record per Xbox port. |
| `abi/xgrid-core.c` | In-process host ABI and save-state request bridge. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 (BSD-3-Clause). |
| `meson.build` | Wires the additions into the xemu build. |

### `xemu-patches/`

Edits to upstream files are kept as patches. The current patch record includes
the in-process host changes (the Windows `sigsetjmp`/`siglongjmp` shim, the
QEMU thread stack size and the xemu run entry point), the shell-pad binding in
`ui/xemu-input.c` that gives players 2-4 a controller to feed, the core-side
presentation filter/overlay in `ui/xui/gl-helpers.cc` and `ui/xui/main.cc`
(`0010-ui-presentation-filter-overlay.patch`), the exit-time GL guard in the
Windows DXGI presenter (`0011-ui-dxgi-exit-cleanup.patch`), the WGL interop
attachment fix that keeps native flip presentation active on NVIDIA drivers
(`0015-ui-dxgi-interop-presentation.patch`), and the guest-clock part of
fast-forward (`0012-ui-fast-forward-clock.patch`).

A new xemu release is handled by rebasing the patch set and the `xgrid-core/`
additions, not by re-porting the emulator.

## Renderers

The NV2A has four renderer backends:

| Backend | Path | Host |
| --- | --- | --- |
| OpenGL | `hw/xbox/nv2a/pgraph/gl/`, shaders in `pgraph/glsl/` | All |
| Vulkan | `hw/xbox/nv2a/pgraph/vk/`, shaders in `pgraph/glsl/` | All with Vulkan |
| Direct3D 11 | `hw/xbox/nv2a/pgraph/d3d11/`, shaders in `pgraph/hlsl/` (preview) | Windows |
| NULL | `hw/xbox/nv2a/pgraph/null/` | All |

Each backend registers a `PGRAPHRenderer`; PGRAPH owns the state extraction
and calls the selected backend's operations. The Direct3D 11 backend is built
when `xgrid_d3d11` is enabled (default on Windows), and only then is `D3D11`
listed in `xgrid-core.json`. It has its own device/context, surface and zeta
caches, HLSL vertex/pixel shader translation, a CPU vertex decoder, pipeline
state objects, occlusion-query reporting and a texture cache; presentation is a
D3D11 readback into the core GL display texture. The staged plan, current phase
and known limitations are in `ImplementDirectX.md` at the repository root.

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

Playback and speed functions:

```c
int xgrid_core_set_paused(int paused);
int xgrid_core_is_paused(void);
int xgrid_core_frame_step(void);
int xgrid_core_set_speed(int multiplier);
int xgrid_core_get_speed(void);
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

Screenshot requests are queued on the core's render loop:

```c
int xgrid_core_request_screenshot(void);
```

The core writes the PNG into `general.screenshot_dir`; the shell never receives
the pixels.

Pause and frame step are queued like the save-state requests and applied on the
core's main loop. A pause stops the machine on the next published frame, and a
step resumes from that boundary and stops after the next frame. A 50 ms timer is
the fallback when the display sink is off, because then no frame is published.

The speed multiplier scales the guest's `QEMU_CLOCK_VIRTUAL` and the vblank pace
together, so the vCPU timers, the Xbox TSC and the presented frame rate all run
at the same faster pace. The value is atomic, so the shell can set 4x while the
core reads it. The shell toggles 4x with Space or the **4x** toolbar button and
returns to 1x on the next press, pause or session end.

One process hosts one machine. Start is accepted once; join releases the thread
handle after the run loop stops.

### Headless

The display sink is also the headless renderer: the core hides its SDL window,
keeps the OpenGL context and publishes every presented frame. A headless shell
run reads only the 32-byte header, so counting frames does not copy pixels. On
Unix the transport is a POSIX shared-memory object; the core unlinks it at exit.

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

Windows:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Output: `xemu/dist/xemu.exe`, `xemu/dist/xgrid_core.dll`,
`xemu/dist/xgrid-core.json` and the runtime DLLs.

Linux:

```bash
bash scripts/build-native-xemu.sh
```

Output: `xemu/dist/xemu`, `xemu/dist/xgrid-core.json` and
`xemu/dist/LICENSE.txt`. This is the standalone core; the in-process
`xgrid_core` library and its save-state ABI are Windows-only. The release
packager bundles this output in the `linux-x64` frontend archive; stage a
different host's build with `-CoreRoot` when one machine packages several RIDs
(see [Releasing](releasing.md)).

### Rebase baseline

Each core rebase is checked against a tracked baseline:

```powershell
pwsh -File scripts/core-baseline.ps1
```

It compares `xemu/XEMU_VERSION`, `xemu/XEMU_COMMIT` and the exports in
`xemu/xgrid-core/abi/xgrid-core.h` with `xemu/xgrid-core/abi-baseline.json`, and
verifies the built `xgrid_core.dll` exports every ABI name. A deliberate rebase
updates the baseline with `-Refresh`, then re-runs the media boot checks and the
test suite.

### Media boot checks

The CHD and ZAR drivers are checked end to end by booting a disc with the
standalone core:

```powershell
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\game.chd -Shot wt-data\chd-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc data\test-samples\zar\hello-entry.zar -Shot wt-data\zar-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc data\test-samples\zar\hello.zar -Shot wt-data\zar-tree-boot.png
```

`RESULT: PASS` means the core stayed alive and presented a non-blank frame. Run
them after a core rebase that touches the block layer. The file-tree check
exercises the XDVDFS synthesizer; entry-form archives must carry the disc as one
`.iso` entry.

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
