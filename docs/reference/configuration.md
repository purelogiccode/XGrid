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

## Command line

Switches are case-insensitive and accept either `--name value` or
`--name=value`. A recognized switch with a bad value stops the run with exit
code `2`; an unrecognized argument is passed through unchanged for Avalonia and
the platform.

| Argument | Meaning |
| --- | --- |
| `--autostart` | Power on the console as soon as the shell is ready. |
| `-fullscreen`, `--fullscreen`, `-f` | Open the shell in fullscreen. |
| `--disc <path>` | Insert this ISO/XISO, CHD or ZAR image and power on. |
| `--core <path>` | Use this emulator binary for the session. |
| `--data-dir <path>` | Use this shell data folder for the session. |
| `--bios <path>` | Flash ROM image for the session. |
| `--mcpx <path>` | 512-byte MCPX boot ROM for the session. |
| `--eeprom <path>` | 256-byte EEPROM image for the session. |
| `--hdd <path>` | Raw or qcow2 hard-disk image for the session. |
| `--renderer <name>` | `OPENGL`, `VULKAN` or `NULL`. |
| `--scale <1-8>` | Internal surface scale. |
| `--filtering <mode>` | Core texture filtering: `linear` or `nearest`. |
| `--filter <mode>` | Shell filter: `nearest`, `bilinear`, `scanline` or `crt`. |
| `--aspect <mode>` | `native`, `auto`, `4x3` (or `4:3`) or `16x9` (or `16:9`). |
| `--fit <mode>` | `center`, `scale` or `stretch`. |
| `--ram <64\|128>` | Guest RAM in megabytes. |
| `--audio <backend>` | `auto`, `dsound` (Windows), `none` or `wav`. |
| `--headless` | Run without the shell window; see [Headless runs](../guides/headless.md). |
| `--benchmark` | Headless benchmark run; 600 frames when no other limit is set. |
| `--seconds <n>` | Headless wall-clock limit (60 when no frame limit is set). |
| `--frames <n>` | Stop a headless run after `n` published frames. |
| `--summary <path>` | Write the headless JSON report to this path. |
| `-h`, `--help`, `-?` | Print the command-line reference. |
| `--version` | Print the application version. |

A positional argument that ends in `.iso`, `.xiso`, `.chd` or `.zar` is treated
as `--disc` and implies `--autostart`:

```text
XGrid.Frontend.Avalonia.exe -f "D:\Games\Halo.iso"
```

Use `--` to stop parsing; every following argument is passed to Avalonia
unchanged. Command-line overrides apply to the current session only. They are
never written into `settings.json`, and they win over per-title overrides.

### Headless runs

`--headless` boots the core without the Avalonia window and without presenting a
picture. The core still renders off-screen. `--seconds`, `--frames`,
`--summary` and `--benchmark` select headless mode on their own. Without
`--seconds` or `--frames`, a normal headless run stops after 60 seconds and a
benchmark run stops after 600 frames. A frame limit without a time limit runs
until that many frames, so combine both when the guest might never reach the
limit. Ctrl+C also stops it cleanly.

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 30 --disc "D:\Games\Halo.iso"
```

The console output and JSON report are documented in
[Headless runs](../guides/headless.md). Exit codes are:

| Code | Meaning |
| --- | --- |
| `0` | Clean run. |
| `1` | Core failure, or the requested summary could not be written. |
| `2` | Configuration or media validation failed. |
| `3` | The core ran but published no frames. |

## Files

```text
<data>/
  settings.json          shell configuration
  screenshots/           F8 captures
  covers/                local game cover art
  xemu/
    xemu.toml            generated core configuration
    xemu.log             core log (separate-process mode)
    audio.wav            WAV capture, when selected
```

A headless run writes its report only where `--summary` points; no file is
created by default.

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
| `ShowFpsOverlay` | Draw the frame-rate overlay over a running console. |
| `CoreHosting` | `in-process` or `process`. |
| `Filter` | `nearest`, `bilinear`, `scanline` or `crt`. |
| `Graphics` | Renderer, scale, filtering, window, aspect, fit, AV pack. |
| `Audio` | Backend, DSP, JIT, HRTF, volume, workers. |
| `Input` | Keyboard and gamepad maps, player-to-XInput-slot assignments, dead zones, trigger threshold, inversion, stick swap. |
| `Accent` | Accent palette key. |
| `XemuCorePath` | Explicit core executable, or empty for auto-detect. |
| `LibraryFolders` | Folders scanned by the Library. |
| `RecentGames` | Recently launched titles (path, title and UTC time), newest first, up to 10. |
| `TitleOverrides` | Per-title overrides keyed by `title:XXXXXXXX` or image path: RAM, renderer, texture filtering and presentation filter. |
| `DonationEmail` | Optional email used to recognize a donation; stored locally and sent only to the donation check. |
| `HasDonated` | `true` after the donation service confirms a completed donation; hides the banner. |
| `DonationLastCheckedUtc` | Last successful or attempted donation check, in UTC. |

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
| `XGRID_FRONTEND_DATA_DIR` | Override the shell data folder (equivalent to `--data-dir`). |
| `XGRID_XEMU_CORE` | Override the core executable path. |
| `XGRID_DISPLAY_SHM` | Display sink name (set by the shell for a separate core process; named file mapping on Windows, POSIX shared memory on Unix). |
| `XGRID_INPUT_SHM` | Input sink name (set by the shell for the separate process on Windows). |
| `XGRID_LOG_DIRECTORY` | Log folder (default: `XGrid/logs` under the per-user local application data). |
| `XGRID_LOG_LEVEL` | Minimum Serilog level: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |
| `XGRID_FRONTEND_TRACE` | `1` enables debug-level frontend and native-surface diagnostics. |
| `XGRID_DISABLE_BUG_REPORTS` | `1` stops forwarding warnings and errors to the bug-report API. |
| `XGRID_DISABLE_STATISTICS` | `1` stops the startup usage hit. |
| `XGRID_DISABLE_UPDATE_CHECK` | `1` stops the GitHub release check. |
| `XGRID_GITHUB_TOKEN` | Token used by the release check for a private repository. |
| `XGRID_BUGREPORT_API_KEY` | Override the bug-report API key. |
| `XGRID_STATS_API_KEY` | Override the statistics API key. |
| `XGRID_DONATION_ENDPOINT` | Override the donation verification endpoint. |
| `XGRID_DONATION_PAGE` | Override the donation page URL. |
| `XGRID_DONATION_API_KEY` | Override the donation verification API key. |

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
