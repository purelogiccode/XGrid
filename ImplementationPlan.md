# XGrid implementation plan

XGrid is the **Avalonia shell + native xemu 0.8.139 core**. This file is the
current product roadmap; user documentation lives in `docs/`.

## 1. Shipped

### Application

- [x] Avalonia desktop shell: Home, Library, Settings, Activity, About.
- [x] Dark theme with accent palettes.
- [x] Menu, toolbar, log dock, status bar and toast messages.
- [x] Library scan of ISO/CHD/ZAR/XBE with title parsing and launch.
- [x] Settings tabs: System, Graphics, Audio, Input, Titles, Library, Appearance.
- [x] Keyboard and XInput bindings with capture, dead zones and trigger threshold.
- [x] Per-title overrides (memory, renderer, texture filtering, presentation filter) keyed by title ID or path.
- [x] Pause, resume and single-frame stepping.
- [x] 4x fast-forward toggled with Space.
- [x] Fullscreen Esc pauses a running game, then closes the shell on the next press.
- [x] Command-line launch switches: `--autostart`, `-fullscreen`/`--fullscreen`/`-f`.
- [x] Disc swap and eject through the in-process ABI.
- [x] Screenshot strip in the Library.
- [x] Local cover art from the user covers folder, keyed by title ID or title.
- [x] First-run wizard with a shortcut sheet.
- [x] Controller assignment for players 1–4, with all four pad records published to the core.
- [x] Whole-window screenshots (F8).
- [x] Power, stop, fullscreen, disc-open and settings shortcuts.
- [x] Command-line settings overrides and headless runs with console and JSON
      reports for automation and benchmarks.
- [x] Multiple library roots, a recently played list and `--benchmark` runs.
- [x] Frame-rate overlay with present rate and average frame time.
- [x] Tooltips on every interactive control in every window and page.
- [x] Serilog rolling logs, warning/error bug reports with environment and
      exception details, startup usage statistics and a GitHub update check.
- [x] Donationware banner with email-based donation verification; it hides only
      after the service confirms a donation.
- [x] Multi-page documentation for donations, accessibility, privacy, service
      endpoints, testing and code inspection, mirrored in `wiki/`.
- [x] XML documentation required for every public type and member.
- [x] `Async` suffix on asynchronous methods.

### Core integration

- [x] Core built from the tracked `xemu/` tree.
- [x] In-process hosting by default; separate-process fallback.
- [x] Shell frame sink and shell pad input sink.
- [x] Generated `xemu.toml` per launch.
- [x] Firmware, storage, RAM, AV pack, renderer, scale, filtering, aspect, audio
      and DSP settings.
- [x] Four save-state slots through the in-process ABI.
- [x] Renderer capability manifest (`xgrid-core.json`).
- [ ] Implement Direct-X rendering.
- [x] Review and test all graphic filters.


### Media and packaging

- [x] ISO/XISO, compressed CHD and entry-form ZAR discs.
- [x] XDVDFS synthesis for file-tree ZArchive discs.
- [x] Entry-form ZArchive creation documented and covered by directory tests.
- [x] `win-x64` frontend archive carries the core under `core/xemu/`.
- [x] Package gate: licenses, archive contents, checksums, manifest, CLI smoke,
      verified for all four release RIDs.
- [x] Unsigned archives with documented SmartScreen/Gatekeeper behavior.
- [x] Dependency license texts in `licenses/`, required by the package gate.
- [x] ABI, version and upstream-commit baseline check for each core rebase.
- [x] GPL-2.0-or-later license set, third-party notices, source availability.

## 2. Next

### Compatibility

- [ ] Expand the tested retail-title set.
- [ ] Track title-level behavior regressions against xemu 0.8.139.
- [ ] Keep the media drivers current with CHD/ZArchive format changes.

### Graphics

The core has OpenGL, Vulkan and NULL render backends plus DXGI presentation;
a DirectX NV2A renderer is still a new backend, not a shell switch.

- [ ] Choose Direct3D 11 or 12 and document the choice.
- [ ] Share NV2A command decoding between the OpenGL backend and the DirectX backend.
- [ ] Translate Xbox vertex and pixel shaders for the chosen DirectX API.
- [ ] Add a DirectX texture cache with Xbox tiling and swizzle handling.
- [ ] Implement surface/render-target caching and presentation.
- [ ] Add the renderer to the capability manifest and Settings choices.
- [ ] Test a retail-title sample against OpenGL for visual and performance parity.

### Media

- [ ] Re-verify CHD/ZAR boot paths on each core rebase.

### Library

- [ ] Optional online cover-art catalog for titles without local covers.

### Platform

- [x] Standalone core build on Linux from the tracked tree.
- [x] Package a core build in the Linux frontend archive.
- [ ] Verify the packaged Linux core on a Linux host.
- [ ] Package the core for macOS.
- [ ] Native display embedding on Linux/macOS.
- [ ] Verify save states on non-Windows hosts.

## 3. Upstream watchlist

[Issues.md](Issues.md) is the full filtered set of open xemu issues that are not
game-specific (298 at the 2026-11-24 snapshot). The subset below is the part
that maps to XGrid's shell, broad compatibility, DirectX renderer, media and
platform work. Sections 3.1-3.7 cover issues; 3.8 covers open pull requests that
implement relevant work. It is a watchlist, not a commitment: shell items are
implemented in `src/XGrid.Frontend.Avalonia/`; core items are tracked for a
future xemu rebase and ported through `xemu/xemu-patches/`. Check an item when
the shell feature lands or the upstream fix is ported.

Selection rule: include issues and pull requests that change shell behaviour,
broad compatibility, the DirectX renderer, media/platform support, or
performance/stability. Do not add game-specific reports or game-specific pull
requests; use per-title overrides or an upstream patch.

### 3.1 Shell and UX

- [ ] **Preflight and actionable errors** — reject unsupported hosts, invalid
  configuration and non-XISO files before boot, with a readable reason
  ([#3011](https://github.com/xemu-project/xemu/issues/3011),
  [#370](https://github.com/xemu-project/xemu/issues/370),
  [#2269](https://github.com/xemu-project/xemu/issues/2269)).
- [ ] **Crash reporting and recovery** — surface core assertions with a report
  bundle and make sure a failed run cannot leave an unkillable process
  ([#1329](https://github.com/xemu-project/xemu/issues/1329),
  [#348](https://github.com/xemu-project/xemu/issues/348)).
- [ ] **Update and relaunch path** — handle macOS/Linux update channels and
  relaunch after an update without losing the current disc or settings
  ([#1454](https://github.com/xemu-project/xemu/issues/1454),
  [#1349](https://github.com/xemu-project/xemu/issues/1349),
  [#1350](https://github.com/xemu-project/xemu/issues/1350)).
- [x] **Library roots and recent titles** - more than one games directory and a
  recently played list
  ([#2832](https://github.com/xemu-project/xemu/issues/2832),
  [#2544](https://github.com/xemu-project/xemu/issues/2544)).
- [ ] **Accessibility and localization** — screen-reader support, Chinese
  language packs and non-ASCII path handling
  ([#2760](https://github.com/xemu-project/xemu/issues/2760),
  [#150](https://github.com/xemu-project/xemu/issues/150),
  [#504](https://github.com/xemu-project/xemu/issues/504)).
- [ ] **EEPROM and portable configuration** — integrated EEPROM editor,
  per-title EEPROM settings and paths relative to `xemu.toml`
  ([#1355](https://github.com/xemu-project/xemu/issues/1355),
  [#1310](https://github.com/xemu-project/xemu/issues/1310),
  [#2730](https://github.com/xemu-project/xemu/issues/2730)).
- [ ] **Controller and peripheral UX** — gamepad-only settings navigation,
  XMU/XBLC attachment and memory-card management in the shell
  ([#2762](https://github.com/xemu-project/xemu/issues/2762),
  [#1128](https://github.com/xemu-project/xemu/issues/1128),
  [#2284](https://github.com/xemu-project/xemu/issues/2284)).
- [ ] **Power and session lifecycle** — powered-off shell state, stay-open mode
  and save-state-on-exit
  ([#1457](https://github.com/xemu-project/xemu/issues/1457),
  [#1456](https://github.com/xemu-project/xemu/issues/1456),
  [#1375](https://github.com/xemu-project/xemu/issues/1375)).
- [x] **CLI and headless operation** — settings overrides on the command line
  and working headless mode for automation and benchmarks
  ([#1843](https://github.com/xemu-project/xemu/issues/1843),
  [#1483](https://github.com/xemu-project/xemu/issues/1483)).
- [x] **Benchmark and FPS diagnostics** - headless `--benchmark` mode plus an
  in-shell FPS overlay for performance comparisons
  ([#465](https://github.com/xemu-project/xemu/issues/465),
  [#2195](https://github.com/xemu-project/xemu/issues/2195)).
- [ ] **Audio device and backend UX** — select the audio backend/device and
  expose a null sink for headless runs
  ([#371](https://github.com/xemu-project/xemu/issues/371),
  [#2331](https://github.com/xemu-project/xemu/issues/2331)).

### 3.2 Display and frame pacing

- [ ] **Display modes and HDR** — exclusive fullscreen selection and HDR-safe
  presentation on Windows
  ([#3134](https://github.com/xemu-project/xemu/issues/3134),
  [#2536](https://github.com/xemu-project/xemu/issues/2536),
  [#1876](https://github.com/xemu-project/xemu/issues/1876)).
- [ ] **Aspect, overscan and filtering** — automatic aspect for 16:9 titles,
  output filtering, overscan and filter-off controls
  ([#1719](https://github.com/xemu-project/xemu/issues/1719),
  [#1354](https://github.com/xemu-project/xemu/issues/1354),
  [#1262](https://github.com/xemu-project/xemu/issues/1262)).
- [ ] **Frame pacing** — stable frame caps and an adjustable cap instead of a
  fixed presentation cadence
  ([#1011](https://github.com/xemu-project/xemu/issues/1011),
  [#2399](https://github.com/xemu-project/xemu/issues/2399)).

### 3.3 Snapshots and save data

- [ ] **Snapshot robustness** — overwrite protection, backward-compatible VM
  states and no lockup when snapshots are used while paused
  ([#2564](https://github.com/xemu-project/xemu/issues/2564),
  [#1159](https://github.com/xemu-project/xemu/issues/1159),
  [#1091](https://github.com/xemu-project/xemu/issues/1091)).
- [ ] **Save data management** — a game-save/memory-card manager separate from
  VM snapshots
  ([#3150](https://github.com/xemu-project/xemu/issues/3150),
  [#139](https://github.com/xemu-project/xemu/issues/139)).

### 3.4 Core compatibility

- [ ] **CPU, memory and chipset** — FPU/SSE accuracy, 256 MB mode, PAL50 and
  EEPROM serial handling
  ([#3097](https://github.com/xemu-project/xemu/issues/3097),
  [#1686](https://github.com/xemu-project/xemu/issues/1686),
  [#2338](https://github.com/xemu-project/xemu/issues/2338),
  [#2318](https://github.com/xemu-project/xemu/issues/2318)).
- [ ] **Storage and disc media** — Linux file locks, HDD image integrity, disc
  swap stability and XDVD security responses
  ([#2958](https://github.com/xemu-project/xemu/issues/2958),
  [#1420](https://github.com/xemu-project/xemu/issues/1420),
  [#2581](https://github.com/xemu-project/xemu/issues/2581),
  [#1036](https://github.com/xemu-project/xemu/issues/1036)).
- [ ] **PVIDEO and video output** — overlay/size handling that affects FMV and
  video playback
  ([#1196](https://github.com/xemu-project/xemu/issues/1196),
  [#2429](https://github.com/xemu-project/xemu/issues/2429)).
- [ ] **Audio/APU** — cyclic voice-list lockups, surround hangs and ALSA
  underruns
  ([#3066](https://github.com/xemu-project/xemu/issues/3066),
  [#2901](https://github.com/xemu-project/xemu/issues/2901),
  [#2363](https://github.com/xemu-project/xemu/issues/2363)).
- [ ] **Networking** — netplay, TAP and NAT reliability
  ([#546](https://github.com/xemu-project/xemu/issues/546),
  [#2168](https://github.com/xemu-project/xemu/issues/2168),
  [#2120](https://github.com/xemu-project/xemu/issues/2120)).

### 3.5 Graphics and DirectX

- [ ] **Shader and vertex parity** — exceptional shader values, attribute
  carryover, UB_D3D attributes and combiner bias that the DirectX backend must
  match
  ([#3184](https://github.com/xemu-project/xemu/issues/3184),
  [#3118](https://github.com/xemu-project/xemu/issues/3118),
  [#3117](https://github.com/xemu-project/xemu/issues/3117),
  [#3091](https://github.com/xemu-project/xemu/issues/3091),
  [#3092](https://github.com/xemu-project/xemu/issues/3092)).
- [ ] **Texture, filtering and upscaling** — subpixel upscaling, shadow
  filtering, signed textures and 4K texture-pack injection
  ([#2334](https://github.com/xemu-project/xemu/issues/2334),
  [#1990](https://github.com/xemu-project/xemu/issues/1990),
  [#587](https://github.com/xemu-project/xemu/issues/587),
  [#1337](https://github.com/xemu-project/xemu/issues/1337)).
- [ ] **Surfaces, depth and 2D** — depth-buffer clipping, dirty-surface
  tracking and class 0x19 2D clip rectangles
  ([#843](https://github.com/xemu-project/xemu/issues/843),
  [#1471](https://github.com/xemu-project/xemu/issues/1471),
  [#2408](https://github.com/xemu-project/xemu/issues/2408)).
- [ ] **Vulkan startup and switch crashes** — hard crashes when selecting
  Vulkan or allocating its startup resources
  ([#3188](https://github.com/xemu-project/xemu/issues/3188),
  [#1890](https://github.com/xemu-project/xemu/issues/1890),
  [#1854](https://github.com/xemu-project/xemu/issues/1854)).
- [ ] **Vulkan performance and cache** — VRAM growth and missing on-disk shader
  cache
  ([#2603](https://github.com/xemu-project/xemu/issues/2603),
  [#1860](https://github.com/xemu-project/xemu/issues/1860)).
- [ ] **Host graphics and backends** — Intel Arc/GL failures, MoltenVK and
  custom-dashboard GPU crashes
  ([#3104](https://github.com/xemu-project/xemu/issues/3104),
  [#1581](https://github.com/xemu-project/xemu/issues/1581),
  [#2296](https://github.com/xemu-project/xemu/issues/2296),
  [#1632](https://github.com/xemu-project/xemu/issues/1632)).
- [ ] **Performance regressions** — SteamOS cursor stalls, NVIDIA driver
  conflicts, DXGI swapchain cost and macOS pacing
  ([#3027](https://github.com/xemu-project/xemu/issues/3027),
  [#3016](https://github.com/xemu-project/xemu/issues/3016),
  [#2845](https://github.com/xemu-project/xemu/issues/2845),
  [#2506](https://github.com/xemu-project/xemu/issues/2506)).

### 3.6 Platform stability

- [ ] **Leaks and freezes** — settings-screen GPU leak, idle memory growth and
  long-session memory leaks
  ([#3083](https://github.com/xemu-project/xemu/issues/3083),
  [#2842](https://github.com/xemu-project/xemu/issues/2842),
  [#1400](https://github.com/xemu-project/xemu/issues/1400)).
- [ ] **Startup and launch** — crash-on-start, distribution launch failures and
  silent exits
  ([#2540](https://github.com/xemu-project/xemu/issues/2540),
  [#2336](https://github.com/xemu-project/xemu/issues/2336),
  [#1692](https://github.com/xemu-project/xemu/issues/1692)).
- [ ] **Exit and suspend** — hangs on close and crashes when the host sleeps
  ([#1431](https://github.com/xemu-project/xemu/issues/1431),
  [#1391](https://github.com/xemu-project/xemu/issues/1391),
  [#1033](https://github.com/xemu-project/xemu/issues/1033)).

### 3.7 Tracking

- [ ] **Meta trackers** — use the subpixel, texture-sampling, timing and audio
  trackers to plan compatibility and screenshot tests; do not duplicate them as
  individual shell bugs
  ([#3095](https://github.com/xemu-project/xemu/issues/3095),
  [#979](https://github.com/xemu-project/xemu/issues/979),
  [#1099](https://github.com/xemu-project/xemu/issues/1099),
  [#904](https://github.com/xemu-project/xemu/issues/904)).

### 3.8 Pull requests to review

61 of the 102 open xemu PRs at the 2026-11-24 snapshot are selected here. These
are implementation references, not merge targets: check draft status, upstream
review and overlap with `xemu/xemu-patches/` before porting. Draft PRs are
marked. Prefer picking them up in the next xemu rebase; use a tracked patch only
when XGrid needs the fix sooner.

#### Shell, display, input and diagnostics

- [ ] **Display controls** — exclusive fullscreen, integer scaling, adjustable
  refresh rate, CPU/display-rate override and anisotropic filtering
  ([#2855](https://github.com/xemu-project/xemu/pull/2855),
  [#2567](https://github.com/xemu-project/xemu/pull/2567),
  [#2400](https://github.com/xemu-project/xemu/pull/2400),
  [#2028](https://github.com/xemu-project/xemu/pull/2028),
  [#2026](https://github.com/xemu-project/xemu/pull/2026)).
- [ ] **Input devices** — USB keyboard/mouse, USB passthrough for XBLC/XMU,
  lightgun support and the OHCI race fix
  ([#2990](https://github.com/xemu-project/xemu/pull/2990),
  [#2854](https://github.com/xemu-project/xemu/pull/2854),
  [#2933](https://github.com/xemu-project/xemu/pull/2933),
  [#1531](https://github.com/xemu-project/xemu/pull/1531)).
- [ ] **Shell mapping and snapshots** — rebindable system actions and snapshot
  surface download while paused
  ([#2967](https://github.com/xemu-project/xemu/pull/2967),
  [#819](https://github.com/xemu-project/xemu/pull/819)).
- [ ] **Diagnostics** — non-ignorable asserts, VK_CHECK diagnostics and HID
  enumeration logging
  ([#2535](https://github.com/xemu-project/xemu/pull/2535),
  [#2859](https://github.com/xemu-project/xemu/pull/2859)).

#### Media and I/O

- [ ] **Disc formats** — CCI compressed images and XDVD security responses;
  native CHD is already covered by `xgridchd`, so reassess only if a correct
  upstream CHD PR appears
  ([#2808](https://github.com/xemu-project/xemu/pull/2808),
  [#1659](https://github.com/xemu-project/xemu/pull/1659)).
- [ ] **Disc and tray timing** — SMC tray handling (draft) and disc/HDD
  throttling (draft)
  ([#2864](https://github.com/xemu-project/xemu/pull/2864),
  [#589](https://github.com/xemu-project/xemu/pull/589)).
- [ ] **Networking** — flush queued nvnet packets when the NIC can receive again
  ([#3107](https://github.com/xemu-project/xemu/pull/3107)).

#### CPU and chipset

- [ ] **CPU accuracy** — x87 load precision and an rdpmc stub instead of #UD
  ([#2951](https://github.com/xemu-project/xemu/pull/2951),
  [#2987](https://github.com/xemu-project/xemu/pull/2987)).
- [ ] **TCG performance** — store-to-code page invalidation, indirect-call
  removal and unused-MMU TLB skipping
  ([#3028](https://github.com/xemu-project/xemu/pull/3028),
  [#3141](https://github.com/xemu-project/xemu/pull/3141),
  [#3140](https://github.com/xemu-project/xemu/pull/3140)).
- [ ] **Chipset** — GPIO IO ports and PBUS config mirroring
  ([#2851](https://github.com/xemu-project/xemu/pull/2851),
  [#3067](https://github.com/xemu-project/xemu/pull/3067)).

#### NV2A and DirectX

- [ ] **Renderer architecture** — shared blit refactor, client-side primitive
  rewrite (draft), PRAMIN-backed unified VRAM and DMA through memory callbacks;
  this is the closest upstream work to the DirectX backend
  ([#2675](https://github.com/xemu-project/xemu/pull/2675),
  [#2734](https://github.com/xemu-project/xemu/pull/2734),
  [#2952](https://github.com/xemu-project/xemu/pull/2952),
  [#2514](https://github.com/xemu-project/xemu/pull/2514)).
- [ ] **NV2A synchronization and throughput** — lock-free D3D fence polling,
  deferred PGRAPH IRQ, bulk inline-array consumption and LRU active-node walking
  ([#2941](https://github.com/xemu-project/xemu/pull/2941),
  [#2929](https://github.com/xemu-project/xemu/pull/2929),
  [#2930](https://github.com/xemu-project/xemu/pull/2930),
  [#2994](https://github.com/xemu-project/xemu/pull/2994)).
- [ ] **Geometry and vertex** — render-to-vertex-buffer, dirty surfaces before
  vertex fetch, D3D vertex color order, fog distance and zero-vector cubemap
  ([#2984](https://github.com/xemu-project/xemu/pull/2984),
  [#3189](https://github.com/xemu-project/xemu/pull/3189),
  [#3182](https://github.com/xemu-project/xemu/pull/3182),
  [#3181](https://github.com/xemu-project/xemu/pull/3181),
  [#3168](https://github.com/xemu-project/xemu/pull/3168)).
- [ ] **Surfaces and blending** — larger linear zeta surfaces, render-target
  reinterpretation, subpixel offset correction, DstAlpha blending, signed image
  formats and class 0x39 memory copies
  ([#3096](https://github.com/xemu-project/xemu/pull/3096),
  [#2802](https://github.com/xemu-project/xemu/pull/2802),
  [#3065](https://github.com/xemu-project/xemu/pull/3065),
  [#2478](https://github.com/xemu-project/xemu/pull/2478),
  [#2982](https://github.com/xemu-project/xemu/pull/2982),
  [#3052](https://github.com/xemu-project/xemu/pull/3052)).
- [ ] **Video output** — framebuffer CPU blit/PVIDEO-only rendering (draft) and
  overlapping VGA framebuffer surfaces (draft)
  ([#1146](https://github.com/xemu-project/xemu/pull/1146),
  [#2190](https://github.com/xemu-project/xemu/pull/2190)).

#### Renderers, audio and packaging

- [ ] **Vulkan compatibility** — persisted pipeline/SPIR-V caches, compute
  workgroup fix, device-sized storage buffers, feature enablement, vertex
  attribute conversion and occlusion-query batching
  ([#2993](https://github.com/xemu-project/xemu/pull/2993),
  [#2977](https://github.com/xemu-project/xemu/pull/2977),
  [#2996](https://github.com/xemu-project/xemu/pull/2996),
  [#2999](https://github.com/xemu-project/xemu/pull/2999),
  [#3002](https://github.com/xemu-project/xemu/pull/3002),
  [#2995](https://github.com/xemu-project/xemu/pull/2995)).
- [ ] **OpenGL and macOS** — GL 3.1 fallback, limited-desktop-GL survival,
  MoltenVK support and GL texture-streaming cost (draft)
  ([#3003](https://github.com/xemu-project/xemu/pull/3003),
  [#2979](https://github.com/xemu-project/xemu/pull/2979),
  [#2799](https://github.com/xemu-project/xemu/pull/2799),
  [#3010](https://github.com/xemu-project/xemu/pull/3010)).
- [ ] **Audio** — APU DSP core/DMA accuracy (draft), HRTF and APU improvements,
  surround-format handling and the segment-offset assertion workaround
  ([#3047](https://github.com/xemu-project/xemu/pull/3047),
  [#3105](https://github.com/xemu-project/xemu/pull/3105),
  [#3074](https://github.com/xemu-project/xemu/pull/3074),
  [#3072](https://github.com/xemu-project/xemu/pull/3072)).
- [ ] **Packaging** — honour `SOURCE_DATE_EPOCH` for a reproducible build date
  ([#3180](https://github.com/xemu-project/xemu/pull/3180)).

## 4. Non-goals

- Re-implementing the emulator in managed C# as the product path.
- Shipping firmware, BIOS images, EEPROMs, HDD images or game data.
- Replacing xemu's device emulation with a new implementation.

## 5. Definition of done

A change is done when:

- it builds with warnings as errors;
- tests cover the new behavior;
- `dotnet format --verify-no-changes` is clean;
- the native core still builds when touched;
- documentation and `wiki/` are updated;
- license/provenance records are updated for ported code;
- the package gate passes for release-affecting changes.

## 6. Documentation

- `docs/index.md` — user documentation home.
- `docs/reference/architecture.md` — architecture.
- `docs/development/building.md` — toolchain.
- `docs/development/quality-gates.md` — checks.
- `docs/legal/` — licensing and provenance.
- `wiki/` — wiki-ready pages.

Historical session logs and wave notes are intentionally not part of the product
documentation.
