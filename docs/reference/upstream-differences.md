# XGrid core vs the xemu 0.8.139 baseline

This page documents the historical starting delta between the XGrid core in
`xemu/` and the pristine **xemu 0.8.139** baseline in
`References2/xemu-0.8.139`. XGrid's core now evolves separately from xemu, so
this is a baseline record rather than a commitment to track upstream releases.
It is a developer and advanced-user reference; the user-facing behavior is
described in the guides.

> `References2/` is a read-only oracle. Compare against it, but never edit,
> move or clean it. The product source is the tracked `xemu/` tree.

## Baseline and provenance

| Item | Value |
| --- | --- |
| Upstream baseline | xemu 0.8.139 in `References2/xemu-0.8.139` |
| XGrid core | xemu 0.8.139 lineage + XGrid changes in `xemu/`, now evolving separately |
| Recorded upstream commit | `aa54c34cae1c09410d74c69a44210a9867fe2d16` (`xemu/XEMU_COMMIT`) |
| XGrid-native code | `xemu/xgrid-core/` |
| Upstream edit record | `xemu/xemu-patches/` (retained patches `0007`–`0022`; 16 files) |
| Ported arcade machine | `xemu/hw/xbox/chihiro/` |
| Third-party notices | `THIRD_PARTY_NOTICES.md`, `docs/legal/licensing.md` |

The comparison below ignores build outputs (`build/`, `dist/`), downloaded Meson
subprojects, documentation and line-ending-only differences. It is a source
comparison, not a claim that every changed line is listed.

### By the numbers

| Measure | Count |
| --- | --- |
| Existing upstream source/build files changed | 40 |
| Lines added/removed in those files | about +4,400 / −120 |
| Patch files in the current record | 16 |
| New Sega Chihiro files | 34 |
| New Direct3D 11 and HLSL files | 24 |
| Files under `xemu/xgrid-core/` (including vendored libchdr/libCCI) | 82 |

`ui/xemu-input.c` alone accounts for 2,636 added lines because it carries the
Chihiro JVS, gun, card and force-feedback input paths as well as the shell pad
hooks.

## At a glance

| Capability | Upstream xemu 0.8.139 | XGrid core |
| --- | --- | --- |
| **Process model** | Standalone `xemu.exe` / `xemu` | Standalone binary **plus** `xgrid_core.dll` for in-process hosting |
| **Shell integration** | None | Embedding, four-pad input sink, core-side filter/overlay, screenshots, playback and disc requests |
| **Media** | ISO/XISO and raw block images | ISO/XISO, CHD, CCI and ZArchive drivers with XGD offset detection |
| **Renderers** | OpenGL, Vulkan, NULL | OpenGL, Vulkan, NULL **plus** Direct3D 11 preview |
| **Input** | SDL keyboard/gamepad | SDL **plus** the shell's four-port shared section; Chihiro JVS and light guns |
| **Fast-forward** | Standalone emulation speed only | 1–8× vblank pace and guest virtual-clock scale; the shell uses 4× and can reach both hosting modes |
| **Save states** | Core snapshot UI | Core snapshots exposed through the shell ABI as four slots and a session state |
| **Machines** | Xbox | Xbox **plus** Sega Chihiro media board, DIMM, JVS, cards and netboard |
| **Audio** | QEMU audio backends | Backends **plus** DirectSound device GUIDs and the `-audio-drivers` probe |
| **Build** | Upstream Meson options | `xgrid_vulkan`, `xgrid_d3d11`, the `xgrid-core` subdir and the Windows DLL |

## Current patch record

| Patch | Area | Existing upstream files touched |
| --- | --- | --- |
| `0007-ui-in-process-core.patch` | In-process host, thread stack, `sigsetjmp` shim, library mode, `xgrid_core.dll` | `include/qemu/thread.h`, `include/system/os-win32.h`, `meson.build`, `ui/xemu.c`, `util/oslib-win32.c`, `util/qemu-thread-win32.c` |
| `0008-ui-shell-pads.patch` | Bind a controller to every port the keyboard did not take | `ui/xemu-input.c`, `ui/xemu-input.h` |
| `0009-ui-fast-forward.patch` | Vblank pace multiplier | `ui/xemu.c`, `ui/xemu-run.h` |
| `0010-ui-presentation-filter-overlay.patch` | Core-side presentation filter and FPS overlay | `ui/xui/gl-helpers.cc`, `ui/xui/main.cc` |
| `0011-ui-dxgi-exit-cleanup.patch` | Do not call GL after the context is gone | `ui/xui/win32-dxgi-present.cc` |
| `0012-ui-fast-forward-clock.patch` | Scale the guest virtual clock with fast-forward | `include/system/cpu-timers.h`, `system/cpus.c`, `ui/xemu.c` |
| `0013-d3d11-renderer.patch` | Native Direct3D 11 renderer and shader cache | `config_spec.yml`, `hw/xbox/nv2a/nv2a.c`, `hw/xbox/nv2a/pgraph/*`, `meson.build`, `meson_options.txt` |
| `0014-ui-in-process-screenshot.patch` | Core-side screenshot request | `ui/xemu-snapshots.h`, `ui/xui/main.cc` |
| `0015-ui-dxgi-interop-presentation.patch` | WGL/DXGI interop keeps native flip presentation on NVIDIA | `ui/xui/win32-dxgi-present.cc` |
| `0016-core-disc-formats.patch` | Select the XGrid disc driver from the file extension | `system/vl.c` |
| `0017-core-audio-device.patch` | DirectSound device GUID and audio-driver probe | `qapi/audio.json`, `audio/dsoundaudio.c`, `ui/xemu.c` |
| `0018-core-chihiro.patch` | Sega Chihiro machine, boards, JVS, cards, guns, force feedback and netboard | `block/meson.build`, `config_spec.yml`, `hw/ide/core.c`, `hw/usb/hcd-ohci.c`, `hw/xbox/*`, `system/vl.c`, `ui/*`, `ui/xui/*` |
| `0019-block-snapshot-skip-nonsnapshot.patch` | Let the all-device snapshot path skip devices that cannot snapshot (Chihiro DIMM) | `block/snapshot.c` |
| `0020-ui-frame-pacing-and-clear.patch` | Core presentation frame limit/skip, safe fast-forward clock update, Space handling, and full-target clear/edge clamp for the embedded picture | `hw/xbox/nv2a/pgraph/gl/display.c`, `system/cpus.c`, `ui/xemu.c`, `ui/xui/gl-helpers.cc`, `ui/xui/win32-dxgi-present.cc` |
| `0021-build-meson-introspect.patch` | Read Meson build options from the configured build directory so a Rust-less host can regenerate them | `Makefile` |
| `0022-repo-ignore-xemu-patches.patch` | Keep the patch record tracked and ignore generated Meson wrap-hash files | `.gitignore` |

Some files appear in more than one patch because they carry more than one
feature. The patch files are the provenance record for edits to existing
upstream files; new XGrid-native files are ordinary commits in this repository.

## New native modules

### `xemu/xgrid-core/`

| Path | What it adds |
| --- | --- |
| `abi/xgrid-core.c`, `abi/xgrid-core.h` | In-process start/stop/join/version, playback, screenshot, disc and snapshot requests. |
| `abi-baseline.json` | Version, upstream commit and ABI export list checked by `scripts/core-baseline.ps1`. |
| `block/xgrid-iso.c` | Shared XDVDFS descriptor search and XGD game-partition offset detection. |
| `block/xgridiso.c` | ISO/XISO block driver that maps the game partition to LBA 0. |
| `block/xgridchd.c` | CHD block driver, decoding hunks on demand through libchdr. |
| `block/xgridcci.c` | CCI block driver with LZ4 sectors through libCCI, split-part discovery and offset mapping. |
| `block/xgridzar.c` | ZArchive block driver; entry-form ISO or synthesized file-tree XDVDFS volume. |
| `display/xgrid-display.c`, `.h` | Core-side presentation filter/overlay selection and the headless shared-memory frame sink (`XGDS`). |
| `input/xgrid-input.c`, `.h` | Four-port shell input source (`XGDI`), one 32-byte record per Xbox pad. |
| `thirdparty/libchdr/` | Vendored libchdr 0.3.0 and its LZMA decoder (BSD-3-Clause). |
| `thirdparty/libcci/` | Vendored libCCI 0.1, built against the host LZ4 (GPL-2.0-or-later). |
| `meson.build` | Wires the drivers, display, input and ABI into the same object set as the emulator. |

The block drivers share one game-partition detector. It probes offset `0` first
(optimized XISO) and then the retail XGD offsets `0x0FD90000`, `0x02080000`,
`0x89D80000` and `0x18300000` (XGD2, XGD3, XGD2 hybrid and XGD1 in the driver's
order). The descriptor is `MICROSOFT*XBOX*MEDIA`, also checked at its trailing
`0x7EC` position. The effect is that an image built for LBA 0 and a full Redump
image produce the same guest view: the game starts at LBA 0.

### Sega Chihiro

`xemu/hw/xbox/chihiro/` is a 34-file port of the xemu-Playtest-v2 /
XPSemu-Chihiro machine. It adds:

- the Chihiro machine and media board (`chihiro.c`, `chihiro-asic`, `chihiro-spd`);
- the 8051, MIPS and V850 helper processors, PIC, AN2131 USB and DES logic;
- JVS input and cabinet profiles (`chihiro-jvs`, `chihiro-cabinet.h`);
- HW210 and CRP-1231 card readers;
- Sega 838 and V257 drive boards, force feedback and steering-wheel effects;
- the netboard and linked-cabinet bridge;
- the media-board flash, baseboard EEPROM and optional network firmware paths.

The DIMM is memory-backed through `block/blkmemory.c` and
`include/block/blkmemory.h`, which are also new. The machine is selectable with
`sys.default_machine = 'chihiro'`, a Chihiro `.bin` image, or the shell's
`Enable Chihiro` switch. It uses 128 MB of guest RAM, its own BIOS and no Xbox
HDD.

Chihiro source keeps its original licenses (LGPL-2.0-or-later, with the PIC
LGPL-2.1-or-later and DES BSD-3-Clause); provenance is in
`xemu/xemu-patches/0018-core-chihiro.patch` and `THIRD_PARTY_NOTICES.md`.

### Direct3D 11

`xemu/hw/xbox/nv2a/pgraph/d3d11/` (15 files) and
`xemu/hw/xbox/nv2a/pgraph/hlsl/` (9 files) add a native Windows renderer:

- D3D11 device and context, surface/zeta caches, texture cache and pipeline state;
- HLSL vertex and pixel shader translation;
- CPU vertex decoding, draw submission, occlusion queries and report readback;
- presentation through a D3D11 readback into the core's GL display texture.

It is a **preview** backend. It is compiled only on Windows and only offered when
`xgrid-core.json` lists `D3D11`. Upstream has OpenGL, Vulkan and NULL only.

### Other new files

| File | Purpose |
| --- | --- |
| `block/blkmemory.c`, `include/block/blkmemory.h` | Memory-region-backed block driver used by the Chihiro DIMM. |
| `ui/xemu-run.h` | Fast-forward/shutdown entry points for the embedded core. |
| `XEMU_VERSION`, `XEMU_COMMIT` | Lineage base release and commit markers. |
| `XEMU_REVIEWED_COMMIT` | Latest upstream commit reviewed for a selective port. |
| `GNUmakefile`, `XGrid.Xemu.Tree.csproj` | Build conveniences for the tracked tree and the solution anchor. |
| `xemu-patches/*.patch` | Provenance for edits to existing upstream files. |

## Behavior differences

### Process and window model

Upstream runs one `xemu` process with its own SDL window. XGrid builds the same
objects as `xgrid_core.dll` as well, with a fixed image base and a 16 MB thread
stack. The shell loads it with `NativeLibrary.Load`; the core starts one machine
on a background thread and the shell calls the ABI.

The embedded mode hides the SDL window, titles it `XGridCore`, and reparents it
into the Home page. Library mode suppresses modal dialogs and returns startup
errors to the shell instead of terminating the host. QEMU's global state remains
single-use: one machine per process. A second in-process start is refused and the
shell falls back to the separate process.

The Windows `sigsetjmp`/`siglongjmp` pair is replaced by a
`RtlCaptureContext`/`RtlRestoreContext` shim because msvcrt rejects QEMU's jump
buffer inside a managed host. This affects the CPU loop only and does not change
emulation behavior.

### Input

Upstream reads controllers through SDL. XGrid adds a 144-byte shared section
(`XGDI`) with four 32-byte pad records. The shell publishes keyboard and XInput
state for players 1–4; the core binds a synthetic controller to each port the
keyboard did not take. Bindings are not written into the core configuration.

The Chihiro path is separate: its JVS input, mouse/light-gun pointers, player 2
aim, card insertion and force feedback are implemented in `ui/xemu-input.c` and
the Chihiro devices. The shell deliberately disables its Xbox pad sink for a
Chihiro launch so the arcade USB bus is not claimed by a shell pad.

### Picture, filters and overlay

Upstream presents the core's picture directly and has no shell-owned filter
selection. XGrid keeps the picture in the core and adds:

- nearest, bilinear, scanline and CRT presentation filters in the final blit;
- an ImGui FPS/frame-time overlay drawn by the core;
- a core-side presentation pacer with an unlimited or fixed frame-rate limit and
  optional skipping of missed presentation deadlines;
- a headless `XGDS` sink for frame counting;
- a core-side screenshot request that writes the PNG without the shell reading
  a frame.

The shell selects the filter/overlay/pacing through the ABI (or
`XGRID_PRESENT_FILTER`/`XGRID_FPS_OVERLAY`/`XGRID_FPS_LIMIT`/`XGRID_FRAME_SKIP`
for a separate process). No shell frame canvas or per-frame Avalonia copy
exists.

### Playback

Upstream's run loop has no shell-facing pause/step/speed ABI. XGrid adds:

- pause/resume queued on the core's main loop;
- single-frame step while paused;
- a 1–8× speed multiplier that scales `QEMU_CLOCK_VIRTUAL` and the vblank pace
  together, so guest timers, the Xbox TSC and presentation all speed up; the
  virtual-clock scale uses a seqlock because timer/vCPU threads read it while the
  shell changes it;
- a registered `XGrid.SetSpeed` window message so the separate process gets the
  same live speed control without an ABI;
- a 50 ms fallback when no display sink is publishing frames.

The shell exposes 4× on `Space` or the toolbar button and restores 1× on the next
toggle, pause or session end.

### Media

Upstream reads ISO/XISO and raw block media. XGrid adds four drivers and a shared
detector:

| Format | Driver | Decode |
| --- | --- | --- |
| ISO/XISO | `xgridiso` | Direct, game partition mapped to LBA 0 |
| CHD | `xgridchd` | libchdr hunks on demand |
| CCI | `xgridcci` | libCCI + host LZ4 sectors; split parts discovered |
| ZArchive | `xgridzar` | zstd blocks; entry-form ISO or synthesized XDVDFS tree |

`system/vl.c` chooses the driver from the extension, and the same drivers are
used for the initial disc and for a live disc change. CHD/CCI/ZAR are read-only.

### Save states and snapshots

Upstream has its own snapshot UI. XGrid keeps the same QEMU/xemu snapshot
machinery but exposes it to the shell as four named slots and one automatic
session state. Requests are queued on the core's main loop because the block
layer and the GL thumbnail require that thread; the shell polls status.

`0019` changes the all-device snapshot path to skip devices that cannot
snapshot. An explicit device list still gets the strict check. This is what lets
a Chihiro snapshot skip the writable DIMM interface and use its dedicated
`chihiro_snapshots.qcow2`.

### Audio

Upstream exposes QEMU audio backends. XGrid adds a DirectSound device GUID to the
QAPI options and passes it to `IDirectSound_Initialize`; the shell enumerates
devices and stores the GUID. XGrid also adds the `-audio-drivers` probe, which
lists the backends the binary actually registered before SDL/GL is initialized.

### Machine selection and Chihiro hardware

Upstream always boots the Xbox machine. XGrid adds:

- `sys.default_machine` (`auto`, `xbox`, `chihiro`) and `sys.last_machine`;
- Chihiro BIOS/media-board/EEPROM paths and a dedicated 128 MB launch;
- a FATX netboot image loaded into DIMM memory;
- baseboard IDE IDENTIFY geometry and DMA service;
- OHCI re-enumeration for the QC/SC USB devices;
- SMC AV-pack/tray behavior for a machine without a DVD drive;
- the FS454 SMBus device and EEPROM generation;
- the Chihiro JVS, card, drive-board and netboard devices.

A `.bin` image selects Chihiro for that launch; the shell also writes
`default_machine = 'chihiro'` when **Enable Chihiro** is on.

### Windows presentation

`0011` and `0015` fix two host-side presentation problems: the DXGI presenter no
longer calls GL after the context is destroyed, and the WGL interop attachment
uses a renderbuffer that keeps NVIDIA's native flip presentation active. These
do not change emulation, but they prevent exits/crashes and a slow copy path on
Windows.

## Existing upstream files changed

The following table is the complete source/build file list for the comparison,
with the feature that owns the change. Some files are listed more than once
because they carry more than one feature.

| File | Change |
| --- | --- |
| `audio/dsoundaudio.c` | Parse a DirectSound device GUID and pass it to `IDirectSound_Initialize`. |
| `block/meson.build` | Build `blkmemory.c` into the system sources. |
| `block/snapshot.c` | Skip non-snapshot-capable devices in the all-device path; keep the strict check for explicit lists. |
| `config_spec.yml` | Add `general.shader_cache_dir`, the `D3D11` renderer value, `sys.default_machine`/`last_machine`, and the full `chihiro.*` schema. |
| `hw/ide/core.c` | Chihiro baseboard IDENTIFY geometry, DIMM capacity and DMA service hook. |
| `hw/usb/hcd-ohci.c` | Chihiro USB enumeration/re-enumeration hooks and port behavior. |
| `hw/xbox/meson.build` | Build the `chihiro` subdirectory. |
| `hw/xbox/nv2a/nv2a.c` | Renderer `post_load` hook; Chihiro shader-cache game-name hook (currently a no-op). |
| `hw/xbox/nv2a/nv2a.h` | Declares the hooks above. |
| `hw/xbox/nv2a/pgraph/meson.build` | Add the D3D11/HLSL subdirectories when D3D11 is compiled. |
| `hw/xbox/nv2a/pgraph/pgraph.c` | Register/select the D3D11 renderer. |
| `hw/xbox/nv2a/pgraph/pgraph.h` | Add the D3D11 renderer state and `post_load` operation. |
| `hw/xbox/smbus_xbox_smc.c` | Chihiro AV pack, tray state and QuickReboot scratch handling. |
| `hw/xbox/xbox.c` | Add the `chihiro` machine property, media/base boards, DIMM image load, FS454, QC/SC USB and save flush. |
| `hw/xbox/xbox.h` | Add the Chihiro machine flag. |
| `include/qemu/thread.h` | Add the host-controlled QEMU thread stack size. |
| `include/system/cpu-timers.h` | Declare the virtual-clock scale. |
| `include/system/os-win32.h` | Replace the Win32 `sigsetjmp` pair with the XGrid context shim. |
| `meson.build` | Add `xgrid-core`, `xgrid_vulkan`, `xgrid_d3d11`, the D3D11 config and `xgrid_core.dll`. |
| `meson_options.txt` | Add `xgrid_vulkan` (off by default) and `xgrid_d3d11` (Windows, on by default). |
| `qapi/audio.json` | Add the DirectSound `device` string option. |
| `system/cpus.c` | Apply the virtual-clock scale. |
| `system/vl.c` | Select Xbox/Chihiro, choose Chihiro firmware/EEPROM, skip the HDD/DVD for Chihiro and select XGrid disc drivers. |
| `ui/meson.build` | Link CoreFoundation/IOKit on macOS for Chihiro pointer input. |
| `ui/xemu-input.c` | Add the shell pad sink and the entire Chihiro JVS/gun/card/force-feedback input path. |
| `ui/xemu-input.h` | Declare the new input hooks. |
| `ui/xemu-notifications.h` | Add a warning notification entry point. |
| `ui/xemu-settings.cc` | Add Chihiro machine detection/settings and media probing. |
| `ui/xemu-settings.h` | Declare the Chihiro helpers. |
| `ui/xemu-snapshots.h` | Declare the core-side screenshot request. |
| `ui/xemu.c` | Add library mode, the audio probe, fast-forward, the display sink and the standalone run split. |
| `ui/xui/gl-helpers.cc` | Add the presentation filter shader, filter uniforms and the Chihiro gun viewport mapping. |
| `ui/xui/main.cc` | Draw the FPS overlay, honor gun-mode menu suppression, handle screenshot requests and Chihiro state. |
| `ui/xui/notifications.cc` | Implement the warning notification. |
| `ui/xui/win32-dxgi-present.cc` | Exit cleanup and WGL/DXGI interop presentation fixes. |
| `util/oslib-win32.c` | Implement the context capture/restore shim. |
| `util/qemu-thread-win32.c` | Use the configurable thread stack size. |

### Build and dependency changes

- The tracked tree adds `GNUmakefile`, `XEMU_VERSION`, `XEMU_COMMIT`,
  `XEMU_REVIEWED_COMMIT` and the solution anchor `XGrid.Xemu.Tree.csproj`.
- Vulkan is gated by `xgrid_vulkan` (default off) instead of always probed. The
  Windows release build script turns it on; a Linux build only gets Vulkan when
  the build passes `-Dxgrid_vulkan=true` and the dependencies are present.
- Direct3D 11 is gated by `xgrid_d3d11` (default on, Windows only).
- The Windows DLL keeps SEH unwind data, uses image base `0x150000000` and a
  16 MB stack, because QEMU TCG needs both in a host process.
- `.gitignore` keeps `xemu-patches/*.patch` tracked and ignores Meson wrap-hash
  sidecars.
- The Rust dependency wraps present in the pristine 0.8.139 subproject set are
  not used by this build; Vulkan subprojects are fetched on demand.

## What did not change

- The Xbox machine, QEMU TCG CPU, MCPX/APU/ACI, IDE/ATAPI, USB/OHCI, SMBus and
  the retail boot chain remain the upstream implementation except for the
  Chihiro hooks listed above.
- The OpenGL and Vulkan renderers keep their upstream rendering paths. D3D11 is
  an additional backend, not a replacement.
- The XBE, XDVDFS and FATX readers and the upstream snapshot format are
  unchanged.
- The standalone `xemu.exe` still exists and still uses the upstream UI. The
  XGrid shell uses the ABI or the separate process; it does not reimplement the
  Xbox.
- Game compatibility is inherited from xemu 0.8.139, plus the media drivers and
  Chihiro support documented in [Compatibility](compatibility.md).

## Rebuilding and checking the comparison

Build the tracked core:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Check the ABI baseline:

```powershell
pwsh -File scripts/core-baseline.ps1
```

Compare a file against the pristine baseline without changing either tree:

```powershell
git diff --no-index -- References2/xemu-0.8.139/hw/xbox/xbox.c xemu/hw/xbox/xbox.c
```

The pristine tree is an oracle, not a working copy. The current upstream
revision, dependency locks and weekly report are described in
[Core lineage and upstream](../development/xemu-upstream.md).

## Related pages

- [Architecture](architecture.md) — shell/core split and the ABI.
- [The native core](../development/core.md) — build and extension rules.
- [Compatibility](compatibility.md) — supported surface and limitations.
- [Licensing](../legal/licensing.md) — GPL, LGPL and BSD notices.
- [Core lineage and upstream](../development/xemu-upstream.md) — lineage,
  upstream reference and dependency locks.
