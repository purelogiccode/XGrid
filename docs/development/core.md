# The native core

The XGrid core began from xemu 0.8.139 and is now maintained directly in
`xemu/`. It is a custom enhanced and patched core that **evolves separately from
the original xemu codebase**. For the historical comparison with pristine xemu,
see [xemu baseline differences](../reference/upstream-differences.md).

## Tree structure

```text
xemu/
  <xemu 0.8.139 lineage and its QEMU base, maintained as XGrid's core>
  xgrid-core/          XGrid-owned native code
  hw/xbox/chihiro/     ported Sega Chihiro machine and cabinet devices
  block/blkmemory.c    memory-backed block driver for the Chihiro DIMM
  xemu-patches/        patch record for upstream edits
  XGrid.Xemu.Tree.csproj
  dist/                build output (git-ignored)
```

### `xgrid-core/`

| File | Role |
| --- | --- |
| `block/xgrid-iso.c` | Shared XDVDFS game-partition detection for the XISO, CHD and ZArchive drivers. |
| `block/xgridiso.c` | ISO/XISO block driver; maps the detected game partition (optimized XISO or full Redump) to LBA 0. |
| `block/xgridcci.c` | CCI block driver; LZ4 sectors through vendored libCCI, with split-part discovery and game-partition mapping. |
| `block/xgridchd.c` | CHD block driver; decodes hunks on demand through libchdr and maps the detected game partition. |
| `block/xgridzar.c` | ZArchive block driver; decodes zstd blocks, exposes an entry-form `.iso`, and synthesizes XDVDFS for file-tree archives. |
| `display/xgrid-display.c` | Presentation filter/overlay selection plus the headless frame sink (Windows named section or POSIX shared memory). |
| `input/xgrid-input.c` | Input sink; reads one pad record per Xbox port. |
| `abi/xgrid-core.c` | In-process host ABI, playback and save-state request bridge. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 (BSD-3-Clause). |
| `thirdparty/libcci/` | Vendored libCCI (GPL-2.0-or-later), built against the host LZ4. |
| `meson.build` | Wires the additions into the xemu build. |

### `xemu-patches/`

`xemu/xemu-patches/` is the provenance record for upstream edits. It includes
the in-process host changes (the Windows `sigsetjmp`/`siglongjmp` shim, the QEMU
thread stack size and the xemu run entry point), the shell-pad binding in
`ui/xemu-input.c` that gives players 1–4 a controller to feed, the core-side
presentation filter/overlay, the exit-time GL guard in the Windows DXGI
presenter, the WGL interop attachment fix, the guest-clock part of fast-forward,
the disc-driver selection in `system/vl.c`, the DirectSound device option plus
the `-audio-drivers` probe, and the Sega Chihiro machine and its Xbox
integration.

XGrid-native additions live in `xemu/xgrid-core/`; new changes are ordinary
commits in this repository. Upstream xemu is a reference and a source of
selective fixes, not a release-for-release merge obligation. See
[Core lineage and upstream](xemu-upstream.md) for the dependency locks, the
upstream revision markers and the weekly report.

## Renderers

The NV2A has four renderer backends:

| Backend | Path | Host |
| --- | --- | --- |
| OpenGL | `hw/xbox/nv2a/pgraph/gl/`, shaders in `pgraph/glsl/` | All |
| Vulkan | `hw/xbox/nv2a/pgraph/vk/`, shaders in `pgraph/glsl/` | Builds with Vulkan |
| Direct3D 11 | `hw/xbox/nv2a/pgraph/d3d11/`, shaders in `pgraph/hlsl/` (preview) | Windows |
| NULL | `hw/xbox/nv2a/pgraph/null/` | All |

Each backend registers a `PGRAPHRenderer`; PGRAPH owns state extraction and
calls the selected backend's operations. The Direct3D 11 backend is built when
`xgrid_d3d11` is enabled and the Windows toolchain provides `d3d11` and
`d3dcompiler`; only then is `D3D11` listed in `xgrid-core.json`. It has its own
device/context, surface and zeta caches, HLSL vertex/pixel shader translation, a
CPU vertex decoder, pipeline state objects, occlusion-query reporting and a
texture cache; presentation is a D3D11 readback into the core GL display
texture. The staged plan and known limitations are in `ImplementDirectX.md` at
the repository root.

## Shell cover art

The shell resolves cover art without touching the emulated picture:

| File | Role |
| --- | --- |
| `Services/Covers/CoverMatcher.cs` | Annotation stripping, normalization, Jaro-Winkler scoring, alias and sequel-number rules. |
| `Services/Covers/LocalCoverIndex.cs` | Read-only index of a local cover folder; exact, token, no-space and fuzzy lookups. |
| `Services/Covers/CoverCatalog.cs` | `catalog.json` validation, title/game-id indexes and image URI resolution. |
| `Services/Covers/CoverStore.cs` | Content-addressed image cache and catalog sidecar. |
| `Services/Covers/CoverDownloader.cs` | Conditional catalog GETs, retries and image downloads. |
| `Services/Covers/CoverService.cs` | Local/catalog resolution order, metadata and automatic downloads. |

The matching behavior follows Simple Launcher and 5DO (annotation groups,
token/no-space forms, subtitle aliases, Jaro-Winkler and sequel-number
protection) but is a separate XGrid implementation, not a copy of those
projects. The catalog uses SHA-256-named WebP images and a sidecar ETag, and
every downloaded image is verified before it enters the cache. The default
catalog is `https://assets.purelogiccode.com/xgrid/v1/catalog.json`; it is
configurable and may be a `file://` catalog for offline use.

`scripts/package-covers.ps1` builds the local pack with ffmpeg and
`scripts/publish-covers.ps1` mirrors it to R2 with rclone. See
[Cover catalog packaging](covers.md).

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
extension names: `.chd` uses `xgridchd`, `.cci` uses `xgridcci`, `.zar` uses
`xgridzar`, and `.iso`/`.xiso` use `xgridiso`. The tray is cycled so Xbox
software notices the change; no power cycle is involved.

Presentation functions:

```c
int  xgrid_core_set_present_filter(int filter);
void xgrid_core_set_fps_overlay(int enabled);
int  xgrid_core_set_fps_limit(int fps);
void xgrid_core_set_frame_skip(int enabled);
```

`xgrid_core_set_fps_limit` accepts 0 for the historical unlimited pace or a
1..1000 frames-per-second cap. Frame skipping drops missed presentation
deadlines instead of presenting a catch-up burst and only applies with a limit.
The pacing lives in the core's render loop; the guest clock is unchanged.

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
at the same faster pace. The scale update is queued on the core's main loop, and
`system/cpus.c` publishes the scale/base/anchor triple through a seqlock so the
timer and vCPU readers never see a torn value. The shell returns to 1× on the
next toggle, pause or session end.

A separate core process has no ABI. It handles the registered `XGrid.SetSpeed`
window message and applies the same multiplier, so the shell's Space/4× control
works in both hosting modes on Windows.

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

Windows:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Output: `xemu/dist/xemu.exe`, `xemu/dist/xgrid_core.dll`,
`xemu/dist/xgrid-core.json` and the runtime DLLs. The manifest lists the compiled
renderers and audio drivers; the shell offers only those backends.

Linux:

```bash
bash scripts/build-native-xemu.sh
```

Output: `xemu/dist/xemu`, `xemu/dist/xgrid-core.json` and
`xemu/dist/LICENSE.txt`. This is the standalone core; the in-process
`xgrid_core` library and its save-state ABI are Windows-only.

### Upstream baseline

Each deliberate upstream re-base is checked against a tracked baseline:

```powershell
pwsh -File scripts/core-baseline.ps1
```

It compares `xemu/XEMU_VERSION`, `xemu/XEMU_COMMIT` and the exports in
`xemu/xgrid-core/abi/xgrid-core.h` with `xemu/xgrid-core/abi-baseline.json`, and
verifies the built `xgrid_core.dll` exports every ABI name. A deliberate upstream
re-base updates the baseline with `-Refresh`, then re-runs the media boot checks
and the test suite.

### Media boot checks

The disc drivers are checked end to end by booting a disc with the standalone
core:

```powershell
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\optimized.iso -Shot wt-data\xiso-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\redump.iso -Shot wt-data\redump-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\game.chd -Shot wt-data\chd-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\game.cci -Shot wt-data\cci-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc path\to\game.1.cci -Shot wt-data\cci-split-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc data\test-samples\zar\007-entry.zar -Shot wt-data\zar-boot.png
pwsh -File scripts/chd-boot-check.ps1 -Disc data\test-samples\zar\hello.zar -Shot wt-data\zar-tree-boot.png
```

`RESULT: PASS` means the core stayed alive and presented a non-blank frame. A
full Redump ISO must show the game, not the dashboard's video tray; the
`xgridiso` driver maps the XGD game partition to LBA 0. The file-tree check
exercises the XDVDFS synthesizer. For CCI, check both a single file and the first
part of a split set; `qemu-img compare` can validate the decoded stream against
the source ISO.

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

XGrid's additions are GPL-2.0-or-later. libchdr is BSD-3-Clause; libCCI is
GPL-2.0-or-later. The vendored xemu/QEMU files keep their original notices. See
[Licensing](../legal/licensing.md) and
[Porting and provenance](../legal/porting-policy.md).
