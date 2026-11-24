# Configuration

XGrid keeps its own settings, generates a fresh core configuration for every
launch, and stores save states inside the HDD image. Nothing in the application
folder is modified at runtime.

## Data folder

| Platform | Default |
| --- | --- |
| Windows | `%APPDATA%\XGrid\` |
| Linux | the platform application-data folder (usually `~/.config/XGrid/`) |
| macOS | `~/Library/Application Support/XGrid/` |

Override it with:

```text
XGRID_FRONTEND_DATA_DIR=/path/to/folder
```

Useful for portable installs and automated tests.

## Files

```text
<data>/
  settings.json          shell configuration
  screenshots/           F8 captures
  xemu/
    xemu.toml            generated core configuration
    xemu.log             core log (separate-process mode)
    audio.wav            WAV capture, when selected
```

### `settings.json`

The shell writes indented JSON. The main fields are:

| Field | Meaning |
| --- | --- |
| `BiosPath` | BIOS/flash ROM image. |
| `McpxRomPath` | 512-byte MCPX boot ROM. |
| `EepromPath` | 256-byte EEPROM image. |
| `HddPath` | Raw or qcow2 HDD image. |
| `DvdPath` | Default disc image. |
| `RamMb` | 64 or 128. |
| `DisplaySink` | Shell presents frames instead of embedding the core window. |
| `CoreHosting` | `in-process` or `process`. |
| `Filter` | `nearest`, `bilinear`, `scanline` or `crt`. |
| `Graphics` | Renderer, scale, filtering, window, aspect, fit, AV pack. |
| `Audio` | Backend, DSP, JIT, HRTF, volume, workers. |
| `Input` | Keyboard and gamepad maps, dead zones, trigger threshold, inversion, stick swap. |
| `Accent` | Accent palette key. |
| `XemuCorePath` | Explicit core executable, or empty for auto-detect. |
| `LibraryFolders` | Folders scanned by the Library. |
| `TitleOverrides` | Per-title overrides keyed by `title:XXXXXXXX` or image path: RAM, renderer, texture filtering and presentation filter. |

Invalid values are repaired on load: RAM is 64 or 128, scale is 1–8, unknown
renderers fall back to OpenGL, unknown filters fall back to nearest, and empty
override keys or rows are dropped.

A corrupt settings file is moved to `settings.json.bak` and defaults are used.

### Generated `xemu.toml`

The shell never reads a user-edited core configuration. Every launch writes a
complete file, so two launches cannot inherit each other's state.

| Section | Keys |
| --- | --- |
| `[general]` | `show_welcome`, `skip_boot_anim` |
| `[general.updates]` | `check = false` |
| `[display]` | `renderer`, `filtering` |
| `[display.quality]` | `surface_scale` |
| `[display.window]` | `startup_size`, `last_width`, `last_height`, `vsync`, `fullscreen_on_startup`, `fullscreen_exclusive` |
| `[display.ui]` | `show_menubar`, `show_notifications`, `fit`, `aspect_ratio` |
| `[input.bindings]` | `port1 = 'keyboard'` when the shell supplies the pad |
| `[audio]` | `use_dsp`, `use_dsp_jit`, `hrtf`, `volume_limit` |
| `[audio.vp]` | `num_workers` |
| `[sys]` | `mem_limit`, `avpack` |
| `[sys.files]` | `bootrom_path`, `flashrom_path`, `eeprom_path`, `hdd_path`, `dvd_path` |

`mem_limit` is the core's string enum (`'64'` or `'128'`).

### Core capability manifest

`xgrid-core.json` beside the core reports the compiled renderers:

```json
{
  "core": "xemu",
  "version": "0.8.139",
  "renderers": ["OPENGL", "NULL"]
}
```

The shell uses it to filter renderer choices. A missing or malformed manifest
means “unknown”; the shell keeps the configured choice and the core may fall back.

## Environment variables

| Variable | Effect |
| --- | --- |
| `XGRID_FRONTEND_DATA_DIR` | Override the shell data folder. |
| `XGRID_XEMU_CORE` | Override the core executable path. |
| `XGRID_DISPLAY_SHM` | Display sink section name (set by the shell for the separate process). |
| `XGRID_INPUT_SHM` | Input sink section name (set by the shell for the separate process). |

## Save states

Snapshots are stored inside the HDD image with these names:

```text
xgrid-slot-1
xgrid-slot-2
xgrid-slot-3
xgrid-slot-4
```

They are QEMU/xemu snapshots, not files in the data folder. See
[Save states](../guides/save-states.md).

## Screenshots

**F8** writes a PNG to `<data>/screenshots/`:

```text
screenshot-YYYYMMDD-HHMMSS.png
```

Collisions get `-1`, `-2`, and so on.

## Core working directory

The shell creates `<data>/xemu/` and uses it as the core's current directory.
That is where `xemu.toml`, `xemu.log` and `audio.wav` live.

## Related pages

- [Architecture](architecture.md)
- [Settings](../guides/settings.md)
- [Troubleshooting](troubleshooting.md)
