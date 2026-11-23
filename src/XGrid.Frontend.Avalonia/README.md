# XGrid frontend (Avalonia)

Cross-platform desktop shell for the maintained xemu core, built with
[Avalonia](https://avaloniaui.net/) (12.1, `VersionOverride` in the project
file). The same project runs on Windows, Linux, and macOS.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

Release build:

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
```

The shell talks to the core through `XGrid.Core.Xemu`: it writes the core
configuration, starts the process, embeds the core window (or presents the
core's frame sink) and forwards the player-1 pad. The managed C# emulator is
not part of this application; it is the test oracle in `XGrid.Cli`.

## Pages

| Page | Purpose |
| --- | --- |
| **Home** | Power on/off, stop, the embedded core display, current disc and core/renderer labels. |
| **Library** | Recursive scan of configured folders for `.iso`, `.chd`, `.zar` and `.xbe` entries; search and launch. |
| **Settings** | Firmware/storage paths, guest RAM, xemu core path, display sink and filter, per-title overrides, library folders, accent color. Persisted as JSON. |
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

The shell starts the xemu core and embeds its native window in the Home page
(`XemuDisplayHost`), so the core paints with its own swapchain and the shell
never copies a frame. When **Present core frames in the shell** is enabled, the
patched core publishes BGRA frames into a named section instead; the shell
copies the latest frame into a `WriteableBitmap` and can apply the presentation
filter (nearest, bilinear, scanline, CRT). The display source and the filter are
independent settings.

The core window is found and reparented after it appears; a 250 ms poll retries
until it exists, so the UI thread never blocks. Resizing the page resizes the
embedded child. Stopping the console closes the process and releases the
display.

## Current scope

- One engine: the maintained xemu core (ISO, CHD and entry-form ZAR media).
- Firmware, storage, guest memory and display-filter settings; per-title
  overrides keyed by image path or XBE title id.
- Keyboard and XInput controller input for the player-1 pad.
- A loose `.xbe` is listed for inspection but must be wrapped in an ISO/CHD/ZAR
  image to boot.
- Save states, pause/frame-step and the managed-engine renderer options belong
  to the oracle CLI, not the shell.

## Keyboard

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
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
Services/                   settings store, library scanner, xemu launch plan,
                            core session, display host, dialogs, accents
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

The shell is an observation/interaction surface; it is not part of the accurate-mode
golden gates. Gate runs continue to use `XGrid.Cli` headless.
