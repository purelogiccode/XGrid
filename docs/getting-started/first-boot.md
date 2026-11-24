# First boot

This walkthrough takes a fresh install to a running Xbox dashboard and then to a
game.

## 1. Start the shell

Launch `XGrid.Frontend.Avalonia`. The Home page opens with the console powered
off. On a fresh install, a **first-run wizard** asks for the BIOS, EEPROM and
hard-disk images and an optional game folder. **Finish** saves the choices;
**Set up later** leaves everything for the Settings page.

If no BIOS is configured, XGrid keeps showing a setup warning on Home.

## 2. Configure firmware and storage

Open **Settings → System**.

| Field | What to select |
| --- | --- |
| **BIOS image** | Your Xbox BIOS/flash ROM (required). |
| **MCPX boot ROM** | `mcpx_1.0.bin` (512 bytes). |
| **EEPROM image** | `eeprom.bin` (256 bytes). |
| **Hard-disk image** | Your Xbox HDD image (raw or qcow2). |
| **Default disc image** | Optional ISO/CHD/ZAR inserted at power-on when no game is selected. |
| **Guest memory** | 64 MB for a retail console, 128 MB for a debug kit. |
| **Present core frames in the shell** | Leave on to use shell presentation, filters and the shell pad. |
| **Core hosting** | **In-process (recommended)**. Use **Separate process** if a crashing core must not take the shell down. |

Click **Save settings** at the top of the page.

> The in-process core is required for save states. The separate-process fallback
> still boots and plays, but its save-state menu is disabled.

## 3. Add your games

Open **Settings → Library** and add one or more folders. XGrid scans them
recursively for:

- `.iso`
- `.xiso`
- `.chd`
- `.zar`
- `.xbe` (listed for inspection; not directly bootable)

You can also add folders from the **Library** page. The scan is limited to 4,096
entries and skips inaccessible folders.

## 4. Power on

Return to **Home** and click **Power on**, or press **F5**.

- With no disc selected, XGrid boots the Xbox dashboard from the HDD image.
- With a disc selected, XGrid boots the dashboard and inserts the disc, or boots
  the game directly depending on the image.

The display caption shows the frame size and the renderer in use. The status bar
shows whether the console is running.

## 5. Launch a game

Open **Library**, find the title and click it. If the console is already running,
the disc is swapped into the running machine (in-process hosting, like save
states); use **Emulation → Power On / Off** to restart instead.

Loose XBE files are not launchable on their own. Wrap them in an ISO or CHD.

## 6. Check the Activity log

**Activity** shows the session log: core start, renderer, RAM size, disc, audio
backend and any warnings. If a launch fails, the reason is logged there and in a
toast.

## Where files live

| Path | Contents |
| --- | --- |
| `<data>/settings.json` | Shell configuration. |
| `<data>/xemu/xemu.toml` | Generated core configuration for the current launch. |
| `<data>/xemu/xemu.log` | Core log in separate-process mode. |
| `<data>/xemu/audio.wav` | Audio capture when the WAV backend is selected. |
| `<data>/screenshots/` | F8 captures. |

Use **File → Open Config Folder** and **File → Open Screenshots Folder** to reach
them from the shell.

## Next steps

- [Settings](../guides/settings.md) — every option explained.
- [Display and filters](../guides/display.md) — presentation, scaling and screenshots.
- [Input](../guides/input.md) — keyboard and controller mapping.
- [Save states](../guides/save-states.md) — four slots and their limits.
- [Troubleshooting](../reference/troubleshooting.md) — if the console does not start.
