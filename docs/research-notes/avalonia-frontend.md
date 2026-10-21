# Avalonia Desktop Frontend

Status: **LANDED 2026-10-20**, commit `ea8b38f`, project
`src/XGrid.Frontend.Avalonia/`. This was an independent frontend-only track run in
parallel with core work: no emulator, CLI, test, or shared project file was modified.

## Goals and non-goals

Goal: a modern, dark-themed, cross-platform desktop shell for XGrid that can boot,
display, and stop the emulator, with a complete settings surface and a game library.

Non-goals for this slice:

- no emulator behavior/fidelity work (the frontend observes the existing public
  `XGrid.Host` API only);
- no replacement of the headless CLI or the accurate-mode gates;
- no WGL/OpenGL *window* backend (that path is Windows-only and would break the
  Linux/macOS requirement). A headless GPU renderer is selectable since 2026-10-21
  (§220): the context is created on the emulation thread and its frames are read
  back into Avalonia, so no foreign window is ever embedded in the visual tree.
- no save-state UI, debugger panes, per-title profiles, or host audio yet.

## Stack

| Area | Choice | Version / note |
|---|---|---|
| UI framework | Avalonia (Desktop, Fluent, Inter) | 11.3.22, MIT |
| MVVM | CommunityToolkit.Mvvm | 8.4.2, MIT |
| Display | software rasterizer + `WriteableBitmap` | no GPU backend referenced |
| Persistence | `System.Text.Json` | reflection serializer, no trimming |
| Target | `net10.0` | same as the solution |

Versions are supplied with `VersionOverride` in the frontend `.csproj`, so
`Directory.Packages.props` stays untouched by this track. Compiled bindings are on by
default (`AvaloniaUseCompiledBindingsByDefault`), and every view/DataTemplate carries an
`x:DataType`; the XAML compiler catches binding drift at build time. `NoWarn=MA0004` is
project-local: UI continuations must resume on the Avalonia dispatcher, so
`ConfigureAwait(false)` is wrong here.

## Architecture

```text
MainWindow (views)
   |  sidebar ListBox -> SelectedNavigationItem.Page
   |  ContentControl + App.axaml DataTemplates
   v
MainWindowViewModel (shell)
   |-- SettingsStore  -> %APPDATA%/XDG-config/Library-Application-Support settings.json
   |-- EmulatorSession -> XboxMachine (public XGrid.Host API, background thread)
   |-- GameLibraryService (recursive .iso/.chd/.xbe scan)
   |-- Home / Library / Settings / Logs / About page view models
   v
WriteableBitmap <- FramePacket <- Nv2aMmio.TryCopyDisplayFrame
```

- The emulator runs on a dedicated background thread (`EmulatorSession.RunLoop`) in
  `XboxMachine.Run(65536)` batches. The UI thread never advances guest state.
- Pause is a `ManualResetEventSlim` gate; stop is a `CancellationTokenSource`; the
  loop joins with a 5 s ceiling. Device images (`HddOverlay`, `Dvd`) are disposed in
  `finally`. Faults are caught at the UI boundary (a guest exception must not kill
  the desktop process) and reported through the session stopped event.
- Frames are captured on the emulation thread when `Nv2a.FramesPresented` changes
  **and at ~30 Hz regardless**, because the picture is a live scanout, not an
  event: nxdk `gamma`/`hello` write the framebuffer directly and never flip
  (§208). `TryCopyDisplayFrame` decodes the display controller's buffer (VGA
  CRTC pitch/size from `NV_PRMCIO`), falls back to the flipped surface before a
  mode exists, and defers to the backend readback when a GPU backend is attached,
  so software and backend pixels are never mixed. The packet is handed to the UI
  through a lock and consumed by a 100 ms `DispatcherTimer`, which copies BGRA
  rows into a `WriteableBitmap` (alpha forced to 255, row-stride aware).
- Keyboard input is translated to XID pad fields on the UI thread, stored as a pending
  state under a lock, and applied with `Gamepad.SetInput` at the next batch boundary.
- The JIT is enabled from the settings profile: `JitEnabled`, `JitRawBlocks`,
  `JitSpinSkip`, `JitBuildThreshold`, and `DecodeCacheEnabled`. Disabling fast mode
  leaves the accurate interpreter path intact. Settings are snapshotted (cloned)
  before the thread starts, so edits cannot race a running console.
- Default profile: fast mode + raw blocks + idle-spin skip + decode cache, threshold 8.

## Configuration

Store: `<app-data>/XGrid/settings.json`. Platform defaults:

- Windows: `%APPDATA%\XGrid\settings.json`
- Linux: `$XDG_CONFIG_HOME/XGrid/settings.json` (or `~/.config/...`)
- macOS: `~/Library/Application Support/XGrid/settings.json`

`XGRID_FRONTEND_DATA_DIR` overrides the folder for portable/test runs.

Persisted fields: BIOS/MCPX/EEPROM/HDD/default-disc paths, RAM (64/128 MB), fast mode,
raw blocks, spin skip, decode cache, JIT threshold, EOS quirk, network backend/link,
accent key, library folders, recent games. `Normalize()` clamps RAM/threshold and
repairs nulls on load; a corrupt JSON file is backed up to `settings.json.bak` and
defaults are used.

## Pages

- **Home** — hero status, power/pause/stop, disc picker, live 4:3 display, session
  telemetry (speed, steps, frames, FPS, EIP), current disc, recent games, keyboard pad.
- **Library** — recursive scan, title search, format badges, size/folder context,
  launch. XBE titles are read with `XbeImage.Load`; loose XBEs are listed but cannot
  boot without an ISO/CHD wrapper.
- **Settings** — firmware, storage, memory/accuracy, performance, network, library
  folders, and appearance. Explicit save; unsaved edits do not affect a running session.
- **Activity** — level-filtered log (2000-entry cap), auto-scroll, copy, clear.
- **About** — frontend/core versions, runtime/OS, configuration path, repository link,
  shortcuts, third-party notice.

Theme: custom dark palette in `Styles/Theme.axaml` with Fluent dark underneath.
Five accent palettes are applied at runtime by rewriting `SystemAccentColor*` and
`XgAccent*` application resources; all custom styles use `DynamicResource`, so
swatches, buttons, switches, and selection highlights update live.

## Validation (2026-10-20, Windows 11 host)

- `dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release`
  — 0 warnings, 0 errors (analyzers + warnings-as-errors).
- `dotnet format <frontend project> --verify-no-changes` — clean.
- Live integration with the repo's test media
  (`data/xemu-0.8.136-windows-x86_64`: `Complex_4627.bin`, `mcpx_1.0.bin`,
  `eeprom.bin`, `xbox_hdd.qcow2`): the EvoX dashboard booted and rendered inside the
  Avalonia display. Observed 861,995,008 guest steps / 138 presented frames in the
  smoke run (host was under concurrent test load; the dashboard is idle-heavy).
- Pause froze steps and flipped the transport to Play; resume; power-off returned to
  Stopped/Idle; restart reset counters and returned to Booting/Running.
- Library scan of `data/test-samples` found the two XBE samples and parsed their
  titles; Play was correctly disabled.
- Accent selection (Aqua) applied live, saved to `settings.json`, and was restored on
  the next launch. App icon is embedded and shown in the window chrome.
- No core project source was touched; this UI-only track did not re-run the headless
  CLI, gate goldens, or the 1519-test baseline.

## Known limitations / next steps

- Software rasterizer only. A GL backend owned by a foreign window cannot be embedded
  in the Avalonia visual tree; the portable default is intentional.
- Keyboard pad only; no physical gamepad binding, analog stick capture, or rumble.
- No host audio sink (APU/ACI are emulated by the core; the frontend does not open an
  audio device).
- No save states, debugger panes, per-title compatibility profiles, or snapshot UI.
- Save/load photo capture of arbitrary frames could be added at the `FramePacket` seam.
- A headless UI test would need `Avalonia.Headless` and a separate test project; not
  added here to keep the track isolated.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

Project-local usage notes and the shortcut table live in
`src/XGrid.Frontend.Avalonia/README.md`.

## Update 2026-10-21 (decisions 217): graphics fidelity, per-title overrides, EEPROM state

- **The NV2A fidelity switches are reachable from the UI.** Settings now has
  "Fixed-function vertex stage" (`--ffp`) and "Texture filtering" (`legacy` |
  `TEXFILTER (tent, LOD 0)`), both defaulting to the CLI defaults, and the
  power-on log line reports the active pair.
- **Per-title overrides (V1).** `EmulatorSettings.TitleOverrides` keys a
  `TitleOverride` (nullable `FixedFunctionVertex`, `TextureFilter`, `RamMb`) by
  the launched image path; `WithTitleOverride(path)` merges it over the global
  configuration at launch. The Settings page lists the rows with per-row
  `ffp` / `filter` / `inherit` toggles, an "Add override" file picker and a
  remove button. Title-id keying needs the XBE inside the disc and is filed V2.
- **The EEPROM persists.** The guest rewrites the 24C02 EEPROM (video mode,
  language, region) at runtime; the frontend now stores that image in
  `<data dir>/eeprom.bin` (seeded from the configured firmware image, which is
  never modified), loads it at power-on and saves it when a run stops. The
  Settings page shows the path and has a "Reset state" button. The CLI gained
  `--eeprom-out <path>` for the same image.
- **The frontend has a test project.** `tests/XGrid.Frontend.Tests` (in
  `XGrid.sln`) covers the plain models with no UI: settings defaults and
  `Normalize` repairs, the override merge, `Clone` independence, the one-line
  description, path-key normalization and the EEPROM store
  (save/load/clamp/reject/reset). 15 tests.


## Update 2026-10-21 (decisions 220): the GPU renderer is a frontend setting

- **Two renderers.** Settings → *Renderer*: **Software** (portable reference
  rasterizer, default) or **OpenGL** (host GPU, Windows/WGL, the CLI's
  `--gpu gl --gpu-replace` configuration with the per-flip readback on). The GPU
  context is created, used and disposed on the emulation thread; an unavailable
  GPU path logs the reason and the session stays on software for the whole run
  (never a mix).
- **Display source follows the renderer.** Software shows the display
  controller's RAM scanout (`TryCopyDisplayFrame`, falling back to the flipped
  surface). OpenGL shows the backend's per-flip readback. Before the backend has
  presented its first frame - the direct-framebuffer guests that never flip - the
  app reads the RAM scanout through the new
  `Nv2AMmio.TryCopyScanoutFrameFromMemory` lane, which is never used once a
  backend frame exists.
- **Measured (this host, gate media, real `EmulatorSession`, 1B steps, scratch
  harness in `wt-data/glprobe`, not committed):** software 34.8 s / 687 flips /
  last display frame 306,817 non-black px, sha `2239732C…`; OpenGL 24.7 s /
  688 flips / the same 306,817 non-black px, sha `B3C8042C…`. Same picture
  (2,385 of 307,200 pixels differ by more than nothing, |dmean| ≈ 0.005), ~29%
  less wall clock. The backend path itself is pinned by the 1B GL golden gates,
  which this slice re-ran.
- **Host API:** `Nv2AMmio.GpuMemory` exposes the guest-memory adapter the device
  was built with (null for test fakes). The CLI now uses it too, which deleted
  its duplicate adapter - one reader/writer identity for the device and every
  backend.
- **Diagnostics off in the app.** The session sets
  `Nv2A.CollectMethodHistogram = false` (the `--pgraph` histogram costs two
  dictionary updates per PGRAPH method; measured -6.2% menu wall, §204). The
  method *total* and the method stream are unaffected, but a `.xgs` saved by the
  app carries an empty histogram map where a CLI save would carry it - the same
  self-describing format, less diagnostic data.
- **Tests:** `tests/XGrid.Frontend.Tests` 21 → 24 (renderer default, clone,
  `Normalize` repair, ordinal key comparison) and `tests/XGrid.Nv2a.Tests`
  389 → 393 (`GpuMemory` exposure, the memory scanout lane with and without a
  backend, lane parity with the software export).

## Update 2026-10-21 (decisions 219d): per-title overrides V2 - keyed by title id

- **A title keeps its configuration when the image moves.** `EmulatorSettings`
  now accepts `title:XXXXXXXX` override keys (`TitleIdKey`,
  `TryParseTitleIdKey`, `CanonicalTitleKey`); `FindTitleOverride(path, titleId)`
  prefers the title key and falls back to the V1 path key, so an existing
  path-keyed row keeps working.
- **Where the id comes from:** `XGrid.Host.DiscTitleId.TryRead` reads the XBE
  certificate title id at launch - a loose `.xbe` directly, an ISO/XISO through
  `XdvdfsFileSystem` (the root `default.xbe`). A **CHD has no XDVDFS reader yet**
  and reports false, so CHD titles keep the path key (documented gap, not a
  silent mismatch). A zero certificate id is treated as unknown rather than
  keying every such image under `title:00000000`.
- **In the UI:** "Add override" resolves the picked image's title id and keys the
  row by it; the row reads `NAME (Title XXXXXXXX)` over "applies wherever this
  title id is launched", while a path-keyed row still shows its file path. The
  power-on log line reports `Title XXXXXXXX · NAME (per-title override applied)`.
- **Tests:** `tests/XGrid.Frontend.Tests` 15 -> 21 (`TitleOverrideKeyTests`), and
  `tests/XGrid.Integration.Tests` +8 (`DiscTitleIdTests`, including the synthetic
  XBE certificate and the CHD/missing-file negatives).
- **Caveat:** the id is the *certificate* id, so homebrew images that never set
  one share the placeholder (`xgu`/nxdk samples all read `0xFFFF0002`), and a
  title-keyed override applies to every such image. Real titles have unique ids.
  `xgrid disc title <path>` prints the id and the exact `title:XXXXXXXX` key.
