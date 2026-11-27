# First boot

This walkthrough takes a fresh install to a running Xbox dashboard and then to a
game.

## 1. Start the shell

Launch `XGrid.Frontend.Avalonia`. The Library page opens with the console
powered off. On a fresh install, a **first-run wizard** asks for the BIOS, EEPROM
and hard-disk images and an optional game folder.

- **Finish** saves the choices and closes the wizard.
- **Set up later** leaves everything for the Settings page.

If no BIOS is configured, Home keeps showing a setup warning and the Activity
log explains what is missing.

## 2. Configure firmware and storage

Open **Settings → System**. The values below are the minimum for a normal retail
boot.

| Field | What to select |
| --- | --- |
| **BIOS image** | Your Xbox BIOS/flash ROM (required). |
| **MCPX boot ROM** | `mcpx_1.0.bin` (512 bytes). Empty looks beside the core. |
| **EEPROM image** | `eeprom.bin` (256 bytes). Empty looks beside the core. |
| **Hard-disk image** | Your Xbox HDD image (raw or qcow2). Empty looks beside the core. |
| **Default disc image** | Optional ISO/XISO, CHD, CCI or ZAR inserted at power-on when no game is selected. |
| **Guest memory** | 64 MB for a retail console, 128 MB for a debug kit. |
| **Core hosting** | **In-process (recommended)** on Windows. **Separate process** isolates a crashing core. |

Click **Save settings** at the top of the page (or press `Ctrl+S` while the
Settings page is open).

> **In-process hosting is required for save states, pause/step, fast-forward,
> live disc swap and core-side screenshots.** The separate-process fallback
> still boots and plays. On Linux the core always runs as a separate process with
> its own window.

## 3. Add your games

Open **Settings → Library → Add folder**, or use **View → Add library folder…**.
XGrid scans recursively for:

- `.iso` and `.xiso`
- `.chd`
- `.cci`
- `.zar`
- `.bin` (Sega Chihiro netboot)
- `.xbe` (listed for inspection; not directly bootable)

The scan reads metadata only: names, sizes and extensions. It does not open the
disc, hash the image or decompress a CHD. A scan stops at 4,096 entries and skips
inaccessible folders.

## 4. Power on

Return to **Home** and click **Power on**, or press `F5`.

- With no disc selected, XGrid boots the Xbox dashboard from the HDD image.
- With a default disc configured, the disc is inserted at power-on.
- Launching a title from the Library boots that disc.

The status bar shows whether the console is running, the active renderer and the
current machine. The Activity page records the launch plan, RAM size, disc, audio
backend and any warning.

## 5. Launch a game

Open **Library**, find the title and double-click it (or select it and press
`Enter`).

If the console is already running, choosing another game **swaps the disc** into
the running machine. That needs in-process hosting. Use **Emulation → Power
On / Off** (`F5`) to restart instead, or `Ctrl+E` to eject.

Loose `.xbe` files are not launchable on their own. Wrap them in an ISO, CHD,
CCI or ZAR image.

## 6. Check the Activity log

**Activity** shows the session log: core start, renderer, RAM size, disc, audio
backend and failures. If a launch fails, the reason is logged there and in a
toast.

Use **File → Open Config Folder** and **File → Open Screenshots Folder** to reach
the data files from the shell.

## Where files live

| Path | Contents |
| --- | --- |
| `<data>/settings.json` | Shell configuration. |
| `<data>/covers/` | Local cover art. |
| `<data>/covers-cache/` | Online catalog cache and downloaded covers (safe to delete). |
| `<data>/screenshots/` | `F8` captures. |
| `<data>/xemu/xemu.toml` | Generated core configuration for the current launch. |
| `<data>/xemu/xemu.log` | Core log in separate-process mode. |
| `<data>/xemu/audio.wav` | Audio capture when the WAV backend is selected. |

The HDD image and your game images stay wherever you put them. XGrid never
copies them into the data folder.

## Next steps

- [Game library](../guides/library.md) — folders, search, favorites and covers.
- [Playing games](../guides/playing-games.md) — pause, fast-forward, disc swap and
  screenshots.
- [Settings](../guides/settings.md) — every option explained.
- [Save states](../guides/save-states.md) — four slots and session resume.
- [Troubleshooting](../reference/troubleshooting.md) — if the console does not
  start.
