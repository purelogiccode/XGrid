# Troubleshooting

Start with the **Activity** page. It records the launch plan, renderer, RAM size,
disc, audio backend and every failure the shell sees.

## First checks

1. Is the console powered on and the status bar showing **Running**?
2. Is a BIOS image configured and found on disk?
3. Is the core executable present (packaged `core/xemu/` or an explicit path)?
4. Is the core's own window visible (on Linux/macOS it is outside XGrid)?
5. What is the last error in **Activity**?

Use **File → Open Config Folder** to reach `settings.json` and the generated
`xemu.toml`. In separate-process mode, `xemu/xemu.log` has the core's own log.

Serilog writes the shell's rolling log to `XGrid/logs` under the per-user local
application-data folder (on Windows, `%LOCALAPPDATA%\XGrid\logs`). The file has
the full exception and stack trace even when **Activity** only shows the
message. See [Logging and diagnostics](../guides/diagnostics.md).

## Power-on failures

| Message | Cause | Fix |
| --- | --- | --- |
| “Choose a BIOS image first.” | No BIOS path, or the file is missing. | Set **Settings → System → BIOS image**. |
| “The emulator binary was not found.” | The packaged core is missing and no path is set. | Set **Emulator binary**, set `XGRID_XEMU_CORE`, or build `xemu/dist`. |
| “The in-process core library is not available; starting a separate core process.” | `xgrid_core.dll` is missing or incompatible. | Build the core, or use separate process deliberately. |
| “Power-on failed: …” | Validation or process start error. | Read the full message and the Activity log. |
| The core opens its own window | Linux/macOS hosting; native in-window embedding is Windows-only. | Use the core window; headless runs use the POSIX sink instead. |
| Save-state entries disabled | Separate-process hosting, including Linux. | Switch to in-process on Windows and power-cycle. |
| A core crash closes the whole app | In-process hosting shares the shell's process. | Switch **Core hosting** to **Separate process** for isolation. |

## Display problems

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Black display | Renderer or core start failure. | Wait a few seconds, check Activity, try OpenGL and a scale of 1. |
| Black display only in a game | The title may not render yet; compatibility. | Try a known-good title, check the core log. |
| Frozen picture | Core stopped or UI stalled. | Check status; stop and power on again. |
| Upside-down picture | Should not occur in current releases. | Report it with the renderer and a screenshot. |
| No filters | A core build without the filter shader, or a separate process that has not relaunched. | Use the XGrid core build and relaunch the separate process. |
| A second game window appears | An older build could not embed the in-process core's window. | Update XGrid; both hosting modes embed the core window now. |
| Low frame rate at a large window | High internal resolution, heavy title, or shell presentation from an older build. | Set scale to 1, try OpenGL, and keep filters/overlay in the core. |
| Vulkan missing | Core build has no Vulkan. | Use OpenGL; the settings hint explains why. |
| Very low frame rate | High internal resolution scale, heavy title, or CPU load. | Set scale to 1, try OpenGL, close background work. |
| Headless run publishes no frames | The binary is not the XGrid core, or the renderer cannot render. | Point `--core` at the XGrid build; try `--renderer OPENGL`. See [Headless runs](../guides/headless.md). |
| Headless run exits `2` | A switch, firmware path or media path was rejected. | Read stderr; run `--help` for the accepted switches. |

## Input problems

| Symptom | Cause | Action |
| --- | --- | --- |
| Keyboard does nothing | Window not focused, console off, or typing in a text field. | Click the display, power on, leave text fields. |
| Controller not detected | Not connected before start, or driver issue. | Connect, restart, check Windows Game Controllers. |
| Wrong button | Binding mismatch. | Remap in **Settings → Input**. |
| Sticks drift | Dead zone too low. | Raise the radial dead zone. |
| No input on Linux or macOS | The mapped-pad sink is Windows-only. | Use the core's own SDL input in its window. |

## Audio problems

| Symptom | Cause | Action |
| --- | --- | --- |
| No sound | Backend **None**, DSP off, or host device issue. | Select Auto/DirectSound, turn **Guest DSP** on. |
| Crackling | Host CPU load or backend issue. | Lower scale, close background work, try another backend. |
| WAV empty | No guest audio yet or DSP not running. | Check Activity and the core log; confirm DSP on. |

## Media problems

| Symptom | Cause | Action |
| --- | --- | --- |
| `.zar` refused | Archive stores a file tree, not an `.iso` entry. | Repack with the ISO as a single entry. |
| `.xbe` will not launch | Loose XBE is not a disc. | Wrap it in an ISO/CHD. |
| CHD will not boot | Corrupt or unsupported CHD. | Verify with another tool; try the ISO. |
| Game not in Library | Unsupported extension, missing folder, or cap reached. | Check the extension, folder, and 4,096-entry cap. |
| Disc not inserted | No default disc and no selected game. | Select a game or set **Default disc image**. |

## Save-state problems

| Symptom | Cause | Action |
| --- | --- | --- |
| “Save states need the in-process emulator core.” | Separate-process hosting. | Switch to in-process. |
| Save fails | HDD image not writable or full. | Check permissions and free space. |
| Load leaves the guest frozen | The core resumed after a failed load; the state may not match. | Check Activity; power-cycle and try another slot. |

## Performance problems

- Set **Internal resolution scale** to 1.
- Use **OpenGL**.
- Leave **Vertical sync** off.
- Close other GPU/CPU-heavy applications.
- Use an ISO instead of a heavily compressed image for the first test.
- On a laptop, ensure the discrete GPU is used.

## Reset to defaults

1. Close XGrid.
2. Back up `<data>/settings.json`.
3. Rename or delete `settings.json`.
4. Start XGrid and configure firmware again.

Do not delete your HDD image; it contains the dashboard and save states.

## Reporting a bug

Include:

- XGrid version and platform;
- renderer and core hosting mode;
- the Activity log lines and the matching `xgrid-YYYYMMDD.log` file around the failure;
- the image format (ISO/CHD/ZAR);
- whether the same title works in another emulator or in xemu 0.8.139.

See `SECURITY.md` for vulnerabilities; use the
[issue tracker](https://github.com/purelogiccode/XGrid/issues) for emulation and
UI bugs.
