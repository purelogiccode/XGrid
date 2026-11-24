# XGrid implementation plan

XGrid is one application: the Avalonia shell plus the console core that ships
inside it. The core is part of this build, not a separate download; the shell
presents its frames, feeds it the pad and owns every setting.

**How to read this file.** Every line is a real state, not an aspiration:

- `[x]` - in the tree today and verified.
- `[ ]` - missing. Each one carries a **Done when:** line that says how it will
  be accepted.

`docs/status.json` is the machine-readable status, but its queue still describes
the retired managed emulator (see **Q1**), so this file is the product plan
until that is re-baselined.

The previous 6,695-line plan for the managed C# emulator is archived at
`docs/archive/implementation-plan-managed-emulator.md`; it is history, not work.

<!-- STATUS:BEGIN (generated from docs/status.json by scripts/status.ps1; do not edit) -->
**Current status (2026-11-23):** A3 / **A3 - Core: ZAR disc driver (entry form done; XDVDFS synthesizer for --iso archives)** (active, attempt 0).
**Gameplay (A3):** H:\Tests\XBOXReady (7 optimized ISOs; 007 1.60 GB, Halo 3.38 GB, 50 Cent, Bad Boys II, Destroy All Humans!, Tak, Ninja Gaiden Black) - Operator decision 2026-11-22 (performance-first, binding): maximum performance is the primary objective; the operator then granted language freedom and chose Option A - XGrid is an enhanced C# frontend for a version of xemu that this repository maintains and evolves from 0.8.139, not an independent emulator and not a GitHub fork. The maintained core is the tracked xemu/ tree (0.8.139 baseline + xemu/xgrid-core additions + xemu/xemu-patches), built by scripts/build-native-xemu.ps1; README, THIRD_PARTY_NOTICES and porting-policy 2c record the arrangement. A0 (window embedding) is done: XGrid.Core.Xemu writes the core config, starts the process, finds its window and reparents it into the shell's display control; scripts/a0-shell-check.ps1 launches 007 from the library and reports one visible core child with a clean teardown. The shell now has the 5DO visual language (RPCS3-style neutral grey + #3DAEE9 blue), a File/Emulation/View/Help menu, an icon toolbar, a collapsible log dock, a status bar and a display-first console page. Real time CHD is landed: xemu/xgrid-core/block/xgridchd.c decodes hunks on demand through libchdr 0.3.0 (BSD-3, vendored), qemu-img reports format xgridchd and the true virtual size, qemu-io reads the XDVDFS descriptor, and scripts/chd-boot-check.ps1 boots Smashing Drive (USA) from a 44.6 MB CHD with no extraction. A2 is done: the core publishes frames into a named section and the shell presents them, the core reads the shell pad for port 1, and an embedded core creates its window hidden with the XGridCore title so there is no start-up flash. A3 is half done: entry-form ZAR archives play (qemu-img reports xgridzar, qemu-io reads the descriptor, xemu mounts it with format=xgridzar and the library lists .zar), while an --iso archive stores a file tree and needs an in-memory XDVDFS synthesizer - the next slice, with the format facts in docs/research-notes/zarchive-format.md. A4 has nearest/bilinear/scanline/CRT filters pinned by tests; integer scaling and per-title overrides remain. A5 release follows. The managed emulator remains the oracle: four accurate 1B goldens, the ktest log and the suite (2,279 tests) validate the core. Oracle: data/xemu/xemu.exe (xemu 0.8.139), same optimized ISO + firmware/EEPROM; scripts/xemu-oracle.ps1 selects the disc with -config_path and captures a DPI-correct client area; a capture is a window screenshot, so the policy is mean/colour/coverage, never pixel-exact.
**Perf reference (M-D, deferred; H1, 1B, fast):** triangle gl+replace 6.40 s / sw 11.10 s; menu gl+replace 17.63 s / sw 27.40 s; gap ~4.9x / ~11.7x.
**Phase score:** 79.65% (93.99 of 118). **Next:** A4 (Shell: graphic filters (nearest, bilinear, sharp-bilinear, integer scale, scanline, CRT)), A5 (Release: patched-core source offer, package gate, seven-title pass), A6 (Core: XDVDFS synthesizer for --iso ZArchive (file tree to a real disc)).
**Do not retry without a new measurement:** N1 C4 host-pointer translate probe, N2 Read-only-ROM decode caching alone, N3 Full trace/superblock formation, N4 Own the encoder (Assembler rewrite), N5 Remove the raw emitter's per-member EIP/TSC/served, N6 VshProgram.Execute allocations / scratch reuse. See docs/status.json for all 23.
<!-- STATUS:END -->

---

## 1. What ships today

### 1.1 Application

- [x] Avalonia 12 desktop shell: console, library, settings, activity, about.
- [x] 5DO visual language, dark chrome, accent palettes.
- [x] Menu, toolbar, log dock, status bar, toast messages.
- [x] Library scan of ISO/CHD/ZAR/XBE with title parsing and launch.
- [x] Settings tabs: System, Graphics, Audio, Input, Titles, Library, Appearance.
- [x] Keyboard + gamepad bindings with capture, dead zones, trigger threshold.
- [x] Per-title overrides (guest RAM).
- [x] `--autostart` to power on from the command line.
- [x] F5 power, F8 whole-window screenshot, F11 full screen, Ctrl+O disc,
      Ctrl+S save settings.

### 1.2 Core integration

- [x] The core is built from the tracked `xemu/` tree in this repository.
- [x] In-process hosting by default; separate process as the fallback.
- [x] Shell presents the core's frames (shared section) and supplies port 1.
- [x] Settings are written as one generated configuration per launch.
- [x] Firmware/EEPROM/HDD paths, 64/128 MB, AV pack, surface scale,
      filtering, fit/aspect, VSynch, renderer, audio backend and DSP options.

### 1.3 Media and packaging

- [x] ISO, compressed CHD and ZAR discs mount directly.
- [x] `win-x64` frontend archive carries the core under `core/xemu/`
      (stripped, 62 MB).
- [x] Package gate: licences, archive contents, checksums, manifest, CLI smoke.
- [x] GPL-2.0-or-later licence set, third-party notices, source offer.

---

## 2. Missing - player features

- [x] **P1. Save states.** The core writes a save state as an internal snapshot
      of the hard disk image (QEMU `savevm`), so the shell exposes slots rather
      than a private format. `Emulation -> Save State / Load State / Delete
      State` offer four slots; F6 saves and F7 loads the selected slot; the
      console page has *Save state* and *Load state* buttons. Each request runs
      on the core's own main loop and the shell polls it, so the UI never
      blocks.
      **Done when:** a slot menu (and F6/F7) saves and loads a snapshot through
      the shell, a load resumes the same frame, and a test pins the round trip.
      **Done (2026-11-24):** `scripts/save-state-check.ps1` boots the console
      through the real shell, F6 writes `xgrid-slot-1` (50 MiB VM state at guest
      clock 0:00:21.7, `qemu-img snapshot -l`), the guest runs on, F7 restores it
      and the post-load capture is the dashboard again with the same guest
      clock; `SaveStateRunnerTests` (11 tests) pins the queue, the slots and the
      completion messages.

- [ ] **P2. Pause, resume, frame step.** Today only power on/off exist.
      **Done when:** pause freezes guest time, resume continues, and frame step
      advances exactly one presented frame (and one million instructions with
      Shift), with the toolbar and menu reflecting the state.

- [ ] **P3. Disc swap / multi-disc.** A title that asks for disc 2 has no path.
      **Done when:** the running session can eject and mount another image
      without powering off, and the guest sees the new disc.

- [ ] **P4. Netplay.** The core supports it; the shell has no screen for it.
      **Done when:** a host/join screen writes the core's netplay configuration
      and two shells on one host connect and reach the dashboard.

- [ ] **P5. Screenshot quick access.** F8 saves; there is no way back to the
      file from the app.
      **Done when:** the toast offers "Show", and the View menu has
      "Open Last Screenshot".

- [ ] **P6. Audio device picker.** Only the backend is selectable
      (Auto / dsound / none / WAV).
      **Done when:** the settings list the host's playback devices and the
      chosen device reaches the core.

- [ ] **P7. Per-title overrides beyond RAM.** Only guest memory can be
      overridden per title.
      **Done when:** renderer, filtering, scale and fit can be pinned per title
      and a launch logs which overrides applied.

- [ ] **P8. Controller assignment.** Port 1 is the shell pad; ports 2-4 and
      multi-player are not exposed.
      **Done when:** each port can be assigned to a host gamepad or left empty,
      and the core sees the assignment.

---

## 3. Missing - shell and UX

- [x] **U1. Caption from the real frame.** `DisplayCaption` hardcodes
      `640 x 480` while the sink publishes 1280x960 frames.
      **Done when:** the caption reads the presented frame size.
      **Done (2026-11-24):** `PresentFrame` records the packet size and
      `DisplayCaptionText.Describe` formats it (`DisplayCaptionTextTests`, 7
      cases); the caption is an overlay on the picture and is hidden until a
      frame exists. UI Automation on the running shell reads
      `1280 x 960 - OpenGL` (was the hardcoded `640 x 480 - OpenGL`).

- [ ] **U2. Window geometry persisted.** The shell reopens at its default size;
      only the core's `last_width/last_height` are written.
      **Done when:** size, position and maximised state survive a restart.

- [ ] **U3. First-run wizard.** A fresh install shows "add your BIOS" and
      expects every path to be found by hand.
      **Done when:** first launch offers BIOS/MCPX/EEPROM/HDD pickers with a
      validate step and writes the settings.

- [ ] **U4. Crash guard and report.** An in-process fault takes the shell down.
      **Done when:** a core fault shows a dialog with the log path, the shell
      stays alive, and the next power-on offers the separate-process host.

- [x] **U5. Vulkan honest.** `CONFIG_VULKAN` is undefined in the build, so the
      option logs `Configured renderer unavailable. Switching to OpenGL.`
      **Done when:** either the build enables Vulkan and the option works, or
      the option is hidden with a hint.
      **Done (2026-11-24):** `scripts/build-native-xemu.ps1` writes
      `xemu/dist/xgrid-core.json` with the renderers `CONFIG_VULKAN` actually
      enabled (`OPENGL`, `NULL` today), the shell reads it through
      `CoreCapabilities`, and the settings page offers only what the build has
      (`RendererChoices`, hint "This emulator build has no Vulkan renderer").
      A configuration that asked for Vulkan is coerced in `XemuLaunchPlan` and
      logged (`VULKAN is not compiled into this emulator build; OPENGL is
      used.`). Evidence: the running shell with a Vulkan settings file reads
      `OpenGL` on the console page, logs both fallback lines, and Save settings
      writes `"Renderer": "OPENGL"`.

- [ ] **U6. Full screen on the picture.** F11 full-screens the whole shell.
      **Done when:** a full-screen mode shows only the game surface and Esc
      returns.

- [ ] **U7. Shortcut sheet on the console page.** Keys are only listed in About.
      **Done when:** the empty-display state lists F5/F8/F11/Ctrl+O.

---

## 4. Missing - core and renderer

- [ ] **C1. Vulkan backend in the build.** See **U5**; the build-side half.
      **Done when:** `scripts/build-native-xemu.ps1` produces a core with
      `CONFIG_VULKAN` and the corpus rows pass on it.

- [ ] **C2. Linux and macOS core builds.** The core is a Windows build today.
      **Done when:** the build script runs on each host and the package gate
      finds a working core in those archives.

- [ ] **C3. Audio verified end to end.** The backend is selected, but audible
      output from the shell has not been measured.
      **Done when:** a boot with a title that plays audio produces non-zero
      samples at the host sink and a WAV capture is non-silent.

- [x] **C4. Save-state bridge.** Core side of **P1**. The ABI exposes
      `xgrid_core_snapshot_save/load/delete/refresh`, `_status`, `_error`,
      `_count` and `_name`. The calls only queue: snapshot work touches the
      block layer and the renderer's GL context, which only the core's main
      loop may do, so a bottom half runs it there and the shell polls.
      **Done when:** the core exposes save/load over the ABI (or a documented
      command) and the shell can call it without stopping the machine.
      **Done (2026-11-24):** the shell saves and loads while the machine keeps
      running; the request never blocks the UI and the snapshot survives in the
      qcow2 (`qemu-img snapshot -l`). A separate-process core reports no
      save-state service, and the shell disables the feature instead of
      pretending.

- [ ] **C5. Netplay plumbing.** Core side of **P4**.
      **Done when:** the core's netplay session can be started and stopped from
      the shell and reports its state.

- [ ] **C6. DirectX renderer (D3D11).** The core ships OpenGL, Vulkan and a null
      renderer. A DirectX option is a **renderer port**, not a switch: the
      combiner and the vertex programs are generated as GLSL today
      (`hw/xbox/nv2a/pgraph/glsl/psh.c`, `vsh-prog.c`, `vsh-ff.c`), so a D3D11
      backend needs HLSL from the same NV2A state. D3D11 first (it runs on
      D3D12 via D3D11On12); D3D12 is a later option, not part of this item.
      Reference for the shader half: Cxbx-Reloaded's
      `src/core/hle/D3D8/Direct3D9/` generates HLSL from the same NV2A state
      (`nv2a_psh.cpp`, `nv2a_vsh.cpp`, `VertexShader.cpp`), so it is the
      oracle for the translation, while xemu stays the oracle for behaviour.
      - [ ] **C6a. Device and surfaces.** D3D11 device, render targets for the
            colour and zeta surfaces, the surface pitch/depth decodes and the
            clear path, selected by `display.renderer = 'D3D11'`.
      - [ ] **C6b. Textures and samplers.** Upload the guest's swizzled and
            linear formats through the existing decoders, and map
            TEXFILTER/TEXADDRESS (nearest, tent, the 1-2-1 convolution,
            wrap/mirror/clamp/border) to sampler objects and shader code.
      - [ ] **C6c. Shaders.** Emit HLSL for the combiner and the vertex
            programs from the same intermediate state the GLSL generators read,
            so the two backends cannot drift; transcribe the fixed-function
            stage as the reference does.
      - [ ] **C6d. Present into the shell.** The shell's sink expects BGRA
            pixels in a shared section; the D3D11 backend must read back (or
            share) the presented surface without changing the sink contract.
      - [ ] **C6e. Shell option.** Add the renderer key to `GraphicsSettings`,
            its validation (today `DIRECTX` is a rejected value), the Settings
            hint and the tests that currently treat `directx` as unknown.
      - [ ] **C6f. Parity.** The corpus rows (the `xgu` samples plus the
            triangle and the dashboard) match the OpenGL leg under the
            documented window-capture policy; no pixel-exact claim.
      **Done when:** a Windows build with `CONFIG_D3D11` boots the dashboard and
      the corpus rows with `display.renderer = 'D3D11'`, the shell offers the
      option, and the build still works with Vulkan disabled.

---

## 5. Missing - packaging and distribution

- [ ] **D1. Core in the Linux and macOS archives.**
      **Done when:** `package.ps1` bundles the core for every RID that has a
      build and `package-verify.ps1` enforces it.

- [ ] **D2. Dependency licence texts.** The archive ships SDL3, GLib, OpenSSL,
      libcurl and the rest without their full texts.
      **Done when:** `core/xemu/licences/` carries every text listed in
      `THIRD_PARTY_NOTICES.md` and the gate checks the count.

- [ ] **D3. Installer and signing.** Portable archives only; macOS quarantines
      the download and Windows SmartScreen warns.
      **Done when:** a signed/notarised build opens without a warning on both
      hosts.

- [ ] **D4. Publish the repository and tag a release.**
      **Done when:** the repository is public, the tag matches the packaged
      version, and the release carries the archives, checksums and the source
      offer.

---

## 6. Missing - quality and project

- [ ] **Q1. Re-baseline `docs/status.json`.** Its 21 queue items and 25 phases
      still describe the retired managed emulator (A3 ZAR, A4 filters, XFP
      rows). `status.ps1 -Check` passes only because this file names them.
      **Done when:** the queue holds the product tasks P1-P8, U1-U7, C1-C5,
      D1-D4 and Q2-Q6, the phase table matches, and `-Check` is green without
      legacy ids.

- [ ] **Q2. Committed end-to-end shell check.** Today the shell+core checks are
      ad-hoc scripts under `wt-data/` that are not in the repository.
      **Done when:** `scripts/shell-check.ps1` powers on a corpus ISO, proves
      frames arrive, the pad is read and a screenshot is written, and runs in
      the suite or the package gate.

- [ ] **Q3. CI re-enabled.** Every workflow is `workflow_dispatch`-only.
      **Done when:** push/PR run build, test, format and the package gate.

- [ ] **Q4. Shell test coverage.** 63 of 2,272 tests cover the frontend.
      **Done when:** the settings model, launch plan, input mapping, screenshots,
      save states and folder menu are covered, and a smoke test drives a real
      session.

- [ ] **Q5. User guide.** No end-user documentation exists outside the release
      notes.
      **Done when:** `docs/user-guide.md` covers first run, settings, library,
      keys and where files live.

- [ ] **Q6. Performance numbers for the shipped core.** The old 1B anchors
      describe the managed emulator; the shell+core path has no published
      number.
      **Done when:** a repeatable measurement reports steps/s and wall for the
      corpus rows on the shipped core.

---

## 7. Legacy workstreams (kept for the status queue)

The queue in `docs/status.json` still points at these. They are history, not
current work; **Q1** replaces them.

## E0N. Native CPU extraction (retired 2026-11-23)

`XCN0`, `XCN1`, `XCN2` and `XCN3` were the extraction of the QEMU TCG core
behind `libxgridcpu`. Decision A made the whole `xemu/` tree the core, so the
shim, its patches and its probes were deleted. Do not rebuild them.

## E1. Full source port ledger (parked)

`XFP5` through `XFP14` were the module-by-module port of xemu to managed C#.
`XFP5` (GPU renderer), `XFP6` (APU), `XFP7` (IDE), `XFP8` (SMBus), `XFP9`
(USB), `XFP10` (machine), `XFP11` (CPU), `XFP12` (host backends), `XFP13`
(snapshots) and `XFP14` (parity close) are parked: the vendored core already
implements them. `A1` (enhanced shell over the core) is done, and `TM9` was
superseded by the core.

## E2. Shell phases

`A2` (frames into the shell, pad for port 1) is done. `A3` (ZAR disc driver)
remains active in the queue but its remaining slice - the XDVDFS synthesizer
for `--iso` archives - is a core feature, not a shell one. `A4` (shell filters)
is done in the shell (`nearest`, `bilinear`, `scanline`, `crt`); integer
scaling and per-title overrides for filters are **P7**. `A6` is the ZAR
synthesizer.

## E3. Release

`A5` is the release workstream; its remaining items are **D1-D4** and **Q3**.

---

## 8. Phase table

Required by `scripts/status.ps1 -Check`; must match `docs/status.json` exactly.
The weights describe the retired managed emulator, which is why **Q1** exists.
The row number is the 0-based phase index in `docs/status.json`.

| Phase | Weight | Done | What remains |
|---|---|---|---|
| 0 Bootstrap | 1 | 100% | - |
| 1 Core | 2 | 100% | - |
| 2 Memory | 3 | 100% | - |
| 3 CPU (x86 PIII) | 11 | 97% | the shipped core owns this |
| 4 MMU | 3 | 100% | - |
| 5 Bus & IRQ | 6 | 90% | - |
| 6 MCPX | 6 | 80% | - |
| 7 Firmware & Boot | 5 | 90% | - |
| 8 Storage | 5 | 86% | - |
| 9 XBE Loader | 3 | 90% | - |
| 10 Kernel Interface | 2 | 5% | retired with the managed emulator |
| 11 NV2A (GPU) | 15 | 94% | the shipped core owns this; DirectX is C6 |
| 12 APU (audio) | 4 | 95% | C3 verifies it end to end |
| 13 USB & Controllers | 4 | 70% | P8 |
| 14 Networking | 4 | 70% | P4 / C5 |
| 15 CPU JIT | 11 | 90% | retired with the managed emulator |
| 16 CPU Optimization | 3 | 35% | retired |
| 17 Memory Optimization | 2 | 40% | retired |
| 18 GPU Optimization | 4 | 78% | retired |
| 19 Debugger | 3 | 30% | not in the product plan |
| 20 Automated Testing | 3 | 85% | Q2 / Q3 / Q4 |
| 21 CLI & Frontend | 3 | 75% | U1-U7 |
| 22 Save States & Replay | 2 | 40% | P1 / C4 |
| 23 Release | 3 | 55% | D1-D4 |
| 24 Retail gameplay & xemu full port | 10 | 60% | the core replaced this track |

---

## 9. Queue cross-reference

`scripts/status.ps1 -Check` requires every queued id to be named here.

| Id | Plan section | State |
|---|---|---|
| A1 | E1 | done |
| A2 | E2 | done |
| A3 | E2 | active (ZAR; the `--iso` synthesizer remains) |
| A4 | E2 | done in the shell; P7 covers per-title filters |
| A5 | E3 | queued; superseded by D1-D4 / Q3 |
| A6 | E2 | queued (ZAR synthesizer) |
| XCN0-XCN3 | E0N | superseded |
| XFP5-XFP14 | E1 | parked |
| TM9 | E1 | superseded |

---

## 10. Measured negatives (do not retry without a new measurement)

The binding list lives in `docs/status.json`; these ids are named here for the
gate: `N1` `N2` `N3` `N4` `N5` `N6` `N7` `N8` `N9` `N10` `N11` `N12` `N13`
`N14` `N15` `N16` `D3` `D4` `D6` `D7` `D8` `G2` `P1S6`.

Each one records a price that was measured and rejected; the reopen condition
is in `docs/status.json`.

---

## 11. Suggested order

1. **P1 + C4** - save states; the one thing a player misses immediately.
   **Done 2026-11-24.**
2. **U1 + U5** - the two UI inconsistencies (caption size, Vulkan option).
3. **Q1** - re-baseline the status queue so the repository stops pointing at
   the retired emulator.
4. **Q2 + Q3** - a committed shell check and CI, so the next changes are gated.
5. **D1 + D2** - core in every archive and the dependency licences.
6. **P2, P3, U2, U3, U4** - the rest of the everyday UX.
7. **P4/P5/C5, P6, P7, P8, U6, U7, C1-C3, D3, D4, Q4-Q6** - breadth.
8. **C6** - the DirectX (D3D11) renderer. The largest single item in this
   plan; start it only after C1/C2 have settled how the renderers are built,
   and land it milestone by milestone (surfaces, textures, shaders, present,
   shell option, parity) so every step is testable.
