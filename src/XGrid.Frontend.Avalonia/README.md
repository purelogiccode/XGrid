# XGrid frontend (Avalonia)

Cross-platform desktop frontend for XGrid, built with [Avalonia](https://avaloniaui.net/)
11.3. The same project runs on Windows, Linux, and macOS.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

Release build:

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
```

The frontend lives entirely in this folder and talks to the emulator only through
the public `XGrid.Host` API (`XboxMachine`). It never reaches into emulator internals.

## Pages

| Page | Purpose |
| --- | --- |
| **Home** | Power on/off, pause/resume, live presented-frame display, session counters, recent games, keyboard pad reference. |
| **Library** | Recursive scan of configured folders for `.iso`, `.chd`, and `.xbe` entries; search, launch, rescan. |
| **Settings** | Firmware/storage paths, RAM size, JIT/performance options, network backend, library folders, accent color. Persisted as JSON. |
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

The frontend boots the emulator in-process on a dedicated background thread and uses
the portable software rasterizer (`--gpu sw` equivalent). Presented frames are copied
from `Nv2AMmio.TryCopyPresentedFrame` into a `WriteableBitmap` on the UI thread. This
keeps the frontend fully cross-platform with no WGL/OpenGL dependency and no mixing of
software and GPU pixels. The emulator is never advanced from the UI thread.

## Default performance profile

- Fast-mode block JIT enabled.
- Raw x86-64 block emitter enabled.
- Idle-spin fast-forward enabled.
- Decode cache enabled.
- JIT build threshold 8.

All of these are configurable, and **Fast mode** can be disabled to run the accurate
interpreter. Toggle `Accent` to restyle the UI live.

## Current scope

- Portable software rasterizer only; the GPU readback is displayed through Avalonia.
  There is no WGL/OpenGL backend or separate emulator window.
- The keyboard mapping above drives the emulated player-1 XID pad. No physical gamepad
  binding or analog stick capture yet.
- Audio devices are emulated by the core but host audio output is not wired to the UI.
- Save states, debugger panes, and per-title compatibility profiles are future work.
- A loose `.xbe` is listed for inspection but must be wrapped in an ISO/CHD image to boot.

## Keyboard

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `F6` | Pause / resume |
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
Services/                   settings store, library scanner, emulator session, dialogs, accents
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
