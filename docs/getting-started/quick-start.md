# Quick start

This page takes you from a downloaded archive to a running Xbox game with the
fewest steps. The detailed versions are
[Installation](installation.md) and [First boot](first-boot.md).

## Before you begin

You need:

- a Windows 10/11 x64, Linux x64 or macOS machine;
- your own legally obtained Xbox **BIOS**, **MCPX boot ROM**, **EEPROM** and
  **hard-disk image**;
- at least one game image (ISO/XISO, CHD, CCI or ZArchive), or the HDD dashboard
  is enough to start.

XGrid ships no firmware or game data. The Windows and Linux x64 frontend
archives include the emulator; the macOS archives contain the shell only.

## 1. Download and extract

Download the archive for your platform from the
[releases page](https://github.com/purelogiccode/XGrid/releases) and extract it
to a folder you can write to. There is no installer. If no packaged archive is
published yet, build the tree with
[Building from source](../development/building.md).

| Platform | Run |
| --- | --- |
| Windows | `XGrid.Frontend.Avalonia.exe` |
| Linux | `./XGrid.Frontend.Avalonia` |
| macOS | the `XGrid.Frontend.Avalonia` executable inside the archive |

Keep the extracted folder together. The packaged core lives under `core/xemu/`.

## 2. Finish the first-run wizard

On a fresh install, XGrid opens a setup wizard. Choose:

- **BIOS image** — required (256 KB or 512 KB);
- **EEPROM** — `eeprom.bin` (optional if it sits beside the core);
- **Hard disk** — raw or qcow2 HDD image;
- **Games folder** — optional; you can add folders later.

Click **Finish**. If you choose **Set up later**, configure the same files in
**Settings → System** before powering on. The wizard does not ask for the MCPX
boot ROM; if `mcpx_1.0.bin` is not beside the core, set **MCPX boot ROM** there
too.

> If XGrid cannot find a required file at launch, the Home page shows a setup
> warning and the Activity log names the missing image.

## 3. Add your games

Open **Settings → Library → Add folder**, or use **View → Add library folder…**.
XGrid scans recursively for `.iso`, `.xiso`, `.chd`, `.cci`, `.zar`, `.bin`
(Chihiro) and `.xbe` (inspection only).

The scan reads file metadata, not disc contents. A library is capped at 4,096
entries per scan; add another root if your collection is larger.

## 4. Play

- To boot the dashboard: open **Home** and click **Power on** (or press `F5`).
- To play a disc: open **Library**, then double-click a tile or select it and
  press `Enter`.

The first launch of a title resolves its XBE title ID, so the title can pick up
its per-title override, favorite state and play time. Later scans reuse the
cached ID.

If the console is already running, choosing another game inserts the new disc
instead of rebooting. This needs in-process core hosting. Press `Ctrl+E` to
eject, or `F5` to power off.

## 5. Learn the essentials

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `P` | Pause / resume |
| `F10` | Step one frame while paused |
| `F6` / `F7` | Quick save / quick load |
| `F8` | Screenshot |
| `F11` | Fullscreen |
| `Space` | Toggle 4× fast-forward |
| `Ctrl+O` / `Ctrl+E` | Open or swap a disc / eject |
| `Esc` | Fullscreen: pause, then close on the next press |

See [Keyboard shortcuts](../reference/shortcuts.md) and
[Controllers and input](../guides/input.md).

## If something does not work

1. Open **Activity** and read the last error.
2. Confirm the BIOS, EEPROM and HDD paths exist.
3. Confirm the core is present under `core/xemu/`.
4. Read [Troubleshooting](../reference/troubleshooting.md).

## Next steps

- [Game library](../guides/library.md) — folders, covers, favorites and search.
- [Playing games](../guides/playing-games.md) — pause, fast-forward, disc swap
  and screenshots.
- [Settings](../guides/settings.md) — every option explained.
- [Media formats](../guides/media.md) — what files XGrid can read.
