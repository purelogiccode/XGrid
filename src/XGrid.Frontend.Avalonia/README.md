# XGrid frontend (Avalonia)

Cross-platform desktop shell for the maintained xemu core. The same project
builds for Windows, Linux and macOS. Windows and Linux release archives bundle
the core; macOS archives carry the shell only.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

Release build:

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
```

## Pages

| Page | Purpose |
| --- | --- |
| **Home** | Power, stop, live display, current disc and renderer status. |
| **Library** | Recursive scan of `.iso`, `.chd`, `.zar` and `.xbe`; search and launch. |
| **Settings** | System, Graphics, Audio, Input, Titles, Library and Appearance. |
| **Activity** | Filtered session log with copy and clear. |
| **About** | Build/runtime information, project links and shortcuts. |

## Integration

`XGrid.Core.Xemu` owns the launch plan, generated `xemu.toml`, in-process and
separate-process hosting, the display/input sinks and the save-state wrapper.

- **In-process** loads `xgrid_core.dll`; frames and input use shared sections
  (Windows only).
- **Separate process** starts `xemu.exe` or `xemu` with the generated
  configuration.
- **Frame sink** lets the shell present and filter frames (Windows only).
- **Embedded window** lets the core paint with its own swapchain (Windows only).
- **Linux** runs the packaged core as a separate process with its own window.

## Configuration

Settings are stored in the platform application-data folder:

- Windows: `%APPDATA%\XGrid\settings.json`
- Linux: the platform application-data folder (usually `~/.config/XGrid/`)
- macOS: `~/Library/Application Support/XGrid/settings.json`

`XGRID_FRONTEND_DATA_DIR` overrides the folder. The core working directory is
`<data>/xemu/`.

## Documentation

- [Documentation home](../../docs/index.md)
- [Settings](../../docs/guides/settings.md)
- [Display and filters](../../docs/guides/display.md)
- [Save states](../../docs/guides/save-states.md)
- [Architecture](../../docs/reference/architecture.md)

## Validation

```powershell
dotnet build src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release
dotnet format src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj --no-restore
```

The shell is part of the product; the accurate-mode validation corpus uses the
managed oracle and `XGrid.Cli`.
