# Getting started

## Requirements

- Windows 10/11 x64 for the packaged emulator.
- An OpenGL-capable GPU with current drivers.
- Your own legally obtained Xbox firmware:
  - BIOS/flash ROM (256 KB or 512 KB) — required;
  - MCPX boot ROM (`mcpx_1.0.bin`, 512 bytes);
  - EEPROM (`eeprom.bin`, 256 bytes);
  - HDD image (raw or qcow2).
- A disc image: ISO/XISO, CHD, or entry-form ZArchive.

## Install

1. Download the release archive. The **Windows x64 frontend** archive includes
   the emulator under `core/xemu/`.
2. Extract it anywhere. The application is portable.
3. Run `XGrid.Frontend.Avalonia`.

Release archives are self-contained; no .NET runtime is needed.

> The binaries are unsigned. Windows SmartScreen may warn; verify the archive
> checksum. On macOS, clear quarantine with
> `xattr -dr com.apple.quarantine /path/to/XGrid`.

## First boot

On a fresh install, a first-run wizard opens and asks for the BIOS, EEPROM and
hard-disk images plus an optional game folder. **Finish** saves them; **Set up
later** leaves everything for Settings. The same fields stay available in
**Settings → System**:

1. Open **Settings → System**.
2. Set **BIOS image**, **MCPX boot ROM**, **EEPROM image** and **Hard-disk image**.
3. Leave **Guest memory** at 64 MB for a retail console.
4. Leave **Present core frames in the shell** on for filters and shell input.
5. Click **Save settings**.
6. Open **Settings → Library** and add your game folders.
7. Return to **Home** and click **Power on** (or press **F5**).
8. Pick a title in **Library**.

## Where files live

| Path | Contents |
| --- | --- |
| `<data>/settings.json` | Shell configuration. |
| `<data>/xemu/xemu.toml` | Generated core configuration. |
| `<data>/xemu/xemu.log` | Core log (separate-process mode). |
| `<data>/xemu/audio.wav` | WAV capture. |
| `<data>/screenshots/` | F8 captures. |

Default data folders:

- Windows: `%APPDATA%\XGrid\`
- Linux: platform application-data folder (usually `~/.config/XGrid/`)
- macOS: `~/Library/Application Support/XGrid/`

Set `XGRID_FRONTEND_DATA_DIR` to override.

## Next

- [Settings](Settings)
- [Display and filters](Display-and-Filters)
- [Input](Input)
- [Save states](Save-States)
- [Troubleshooting](Troubleshooting)
