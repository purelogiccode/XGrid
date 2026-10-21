# XGrid frontend (Avalonia)

Cross-platform desktop frontend for XGrid, built with [Avalonia](https://avaloniaui.net/)
(12.1, `VersionOverride` in the project file). The same project runs on Windows,
Linux, and macOS.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

Release build:

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
```

The frontend lives entirely in this folder and talks to the emulator only through
the public `XGrid.Host` API (`XboxMachine`); the OpenGL renderer is attached through
the same public seam (`Nv2A.Backend`) using `XGrid.GpuGl`'s factory. It never reaches
into emulator internals.

## Pages

| Page | Purpose |
| --- | --- |
| **Home** | Power on/off, pause/resume, live presented-frame display, session counters, recent games, keyboard pad reference. |
| **Library** | Recursive scan of configured folders for `.iso`, `.chd`, and `.xbe` entries; search, launch, rescan. |
| **Settings** | Firmware/storage paths, RAM size, renderer (software/OpenGL), JIT/performance options, NV2A graphics fidelity switches, per-title overrides, network backend, library folders, accent color. Persisted as JSON. |
| **Activity** | Filtered emulator log with copy and clear. |
| **About** | Build/runtime information, project links, shortcuts. |

## Configuration

Settings are stored in the platform application-data folder:

- Windows: `%APPDATA%\XGrid\settings.json`
- Linux: `~/.config/XGrid/settings.json`
- macOS: `~/Library/Application Support/XGrid/settings.json`

`XGRID_FRONTEND_DATA_DIR` overrides the folder (useful for portable or test runs).
XGrid never ships firmware, EEPROMs, or game data; provide your own legally obtained
images on the Settings page.

## Display path

The frontend boots the emulator in-process on a dedicated background thread. Two
renderers are available (Settings -> Emulation -> **Renderer**):

- **Software rasterizer** (default) - the exact renderer, the same code path the
  accurate-mode gates use. Frames are decoded from the display controller
  (`TryCopyDisplayFrame`: the VGA CRTC scanout, falling back to the last flipped
  3D surface before a mode exists), so direct-framebuffer guests update too.
- **OpenGL** - the host GPU backend in replacement mode
  (`--gpu gl --gpu-replace`), several times faster on the dashboard menu. The
  backend renders offscreen into its own framebuffer (Windows/WGL); nothing is
  embedded in the Avalonia visual tree and no foreign window is created. The
  picture is the backend's per-flip readback, and a draw the backend declines is
  painted in software and composed into the presented frame (all-or-nothing,
  never mixed). When no GL context can be created (any non-Windows host, no
  driver, failed probe) the session logs the reason and runs the software
  rasterizer instead of failing. The context is created, used and disposed on
  the emulation thread.

Both renderers hand the same `FramePacket` (BGRA, top-down, 4:3) to the UI
through a 100 ms dispatcher timer, which copies the rows into a `WriteableBitmap`.
The emulator is never advanced from the UI thread, and none of this requires a
WGL/OpenGL type in the UI layer.

Note that the OpenGL renderer presents the last **flipped** frame, so a guest
that only ever writes the framebuffer directly (nxdk `gamma`, `hello`) is not
displayed on that renderer once a backend frame exists; the software renderer
shows the scanout and does. Before the first backend frame the session reads the
RAM scanout (`TryCopyScanoutFrameFromMemory`), so a guest that never flips is
still visible.

## Default performance profile

- Fast-mode block JIT enabled.
- Raw x86-64 block emitter enabled.
- Idle-spin fast-forward enabled.
- Decode cache enabled.
- JIT build threshold 8.

All of these are configurable, and **Fast mode** can be disabled to run the accurate
interpreter. Toggle `Accent` to restyle the UI live.

## Current scope

- Two renderers: the portable software rasterizer (default, exact) and the
  opt-in host OpenGL backend (offscreen FBO + readback, Windows-only at runtime,
  automatic software fallback elsewhere). Neither opens a separate emulator window.
- Settings also expose the NV2A graphics-fidelity switches (`--ffp`,
  `--tex-filter registers`), per-title overrides keyed by image path or XBE
  title id (including a per-title renderer), and a persisted EEPROM image.
- Programs that write the framebuffer directly (nxdk `gamma`/`hello`, `debugPrint`)
  issue no 3D draws and never flip, so on the GPU renderer they are shown from the RAM
  scanout until the backend presents its first frame; after that the backend readback
  is the display source.
- The keyboard mapping above drives the emulated player-1 XID pad. No physical gamepad
  binding or analog stick capture yet.
- Audio devices are emulated by the core but host audio output is not wired to the UI.
- Save states exist (`.xgs` save/load on the Home page, F8/F9); debugger panes,
  screenshot capture and per-title compatibility profiles are future work.
- A loose `.xbe` is listed for inspection but must be wrapped in an ISO/CHD image to boot.

## Keyboard

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `F6` | Pause / resume |
| `F7` / `Shift+F7` | Frame step / 1,000,000-instruction step (while paused) |
| `F11` / `Esc` | Full screen toggle / exit |
| `Ctrl+O` | Open a disc image |
| `Ctrl+S` | Save settings (Settings page) |
| Arrow keys | D-pad |
| `Enter` / `Backspace` | Start / Back |
| `A` `S` `D` `F` | A / B / X / Y |
| `Q` `E` | White / black |
| `Z` `C` | Left / right trigger |
| `Space` | A |

## Code layout

```text
App.axaml(.cs)              application, theme, page data templates
Program.cs                  Avalonia bootstrap
Models/                     settings, game entries, log entries, frame packets
Services/                   settings store, library scanner, emulator session (renderer attach,
                            GPU backend lifetime), dialogs, accents
ViewModels/                 MVVM shell and page view models (CommunityToolkit.Mvvm)
Views/                      MainWindow + one UserControl per page
Styles/Theme.axaml          dark palette, typography, control styles
Converters/                 log-level brush mapping
AppIcons.cs                 vector icon geometries
```

Add a page by creating a `PageViewModel` + `UserControl`, adding it to
`MainWindowViewModel.NavigationItems`, and registering a `DataTemplate` in `App.axaml`.

## Validation

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
dotnet format src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj --no-restore
```

The frontend is an observation/interaction surface; it is not part of the accurate-mode
golden gates. Gate runs continue to use `XGrid.Cli` headless.
