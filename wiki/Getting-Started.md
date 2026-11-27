# Getting started

## Requirements

- Windows 10/11 x64, Linux x64, or macOS (shell only).
- An OpenGL-capable GPU with current drivers.
- Your own legally obtained Xbox firmware:
  - BIOS/flash ROM (256 KB or 512 KB) — required;
  - MCPX boot ROM (`mcpx_1.0.bin`, 512 bytes);
  - EEPROM (`eeprom.bin`, 256 bytes);
  - HDD image (raw or qcow2).
- A disc image: ISO/XISO (optimized or full Redump), CHD, CCI or ZArchive
  (entry form or file tree).

## Install

1. Download the release archive. The **Windows and Linux x64 frontend** archives
   include the emulator under `core/xemu/`; the macOS archives are shell-only.
2. Extract it anywhere. The application is portable.
3. Run `XGrid.Frontend.Avalonia` (`./XGrid.Frontend.Avalonia` on Linux).

Release archives are self-contained; no .NET runtime is needed. Verify the
published SHA-256 checksum before running.

> The binaries are unsigned. Windows SmartScreen may warn; on macOS, clear
> quarantine with
> `xattr -dr com.apple.quarantine /path/to/XGrid`.

## First boot

On a fresh install, a first-run wizard opens and asks for the BIOS, EEPROM and
hard-disk images plus an optional game folder. **Finish** saves them; **Set up
later** leaves everything for Settings.

The same fields stay available in **Settings → System**:

1. Set **BIOS image**, **MCPX boot ROM**, **EEPROM image** and **Hard-disk
   image**.
2. Leave **Guest memory** at 64 MB for a retail console.
3. Keep **Core hosting** at **In-process (recommended)** on Windows.
4. Click **Save settings**.
5. Open **Settings → Library** and add your game folders.
6. Open **Library** and double-click a game tile (or press **Enter**).
7. To boot the dashboard without a disc, open **Home** and click **Power on**
   (or press **F5**).

## Where files live

| Path | Contents |
| --- | --- |
| `<data>/settings.json` | Shell configuration. |
| `<data>/xemu/xemu.toml` | Generated core configuration. |
| `<data>/xemu/xemu.log` | Core log (separate-process mode). |
| `<data>/xemu/audio.wav` | WAV capture. |
| `<data>/screenshots/` | F8 captures. |
| `<data>/covers/` | Local cover art. |

Default data folders:

- Windows: `%APPDATA%\XGrid\`
- Linux: `~/.config/XGrid/` (platform application-data folder)
- macOS: `~/Library/Application Support/XGrid/`

Set `XGRID_FRONTEND_DATA_DIR` or `--data-dir` to override.

## Next

- [Settings](Settings)
- [Game library](Game-Library)
- [Playing games](Playing-Games)
- [Save states](Save-States)
- [Troubleshooting](Troubleshooting)

Full guide: [Quick start](https://github.com/purelogiccode/XGrid/blob/master/docs/getting-started/quick-start.md)
and [First boot](https://github.com/purelogiccode/XGrid/blob/master/docs/getting-started/first-boot.md).
