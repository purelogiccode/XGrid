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

## 3. Non-goals

- Re-implementing the emulator in managed C# as the product path.
- Shipping firmware, BIOS images, EEPROMs, HDD images or game data.
- Replacing xemu's device emulation with a new implementation.

## 4. Definition of done

A change is done when:

- it builds with warnings as errors;
- tests cover the new behavior;
- `dotnet format --verify-no-changes` is clean;
- the native core still builds when touched;
- documentation and `wiki/` are updated;
- license/provenance records are updated for ported code;
- the package gate passes for release-affecting changes.

## 5. Documentation

- `docs/index.md` — user documentation home.
- `docs/reference/architecture.md` — architecture.
- `docs/development/building.md` — toolchain.
- `docs/development/quality-gates.md` — checks.
- `docs/legal/` — licensing and provenance.
- `wiki/` — wiki-ready pages.

Historical session logs and wave notes are intentionally not part of the product
documentation.
