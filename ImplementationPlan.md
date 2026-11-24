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
- [x] Disc swap and eject through the in-process ABI.
- [x] Screenshot strip in the Library.
- [x] First-run wizard with a shortcut sheet.
- [x] Controller assignment for players 1–4.
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

### Media

- [ ] Re-verify CHD/ZAR boot paths on each core rebase.

### Platform

- [x] Standalone core build on Linux from the tracked tree.
- [ ] Package the core for Linux and macOS.
- [ ] Native display embedding on Linux/macOS.
- [ ] Verify save states on non-Windows hosts.

## 3. Non-goals

- Re-implementing the emulator in managed C# as the product path.
- A DirectX renderer; xemu 0.8.139 offers OpenGL, Vulkan and NULL.
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
