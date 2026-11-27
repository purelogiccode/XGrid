# Troubleshooting

Start with the **Activity** page. It records the launch plan, renderer, RAM size,
disc, audio backend and every failure the shell sees. Use the rolling log for the
full exception and stack trace.

## First checks

1. Is the console powered on and the status bar showing **Running**?
2. Is a BIOS image configured and found on disk?
3. Are the MCPX, EEPROM and HDD images present?
4. Is the core executable present under `core/xemu/` (or at the configured path)?
5. On Linux/macOS, is the core's own window visible?
6. What is the first error in **Activity**, not the last?

Use **File → Open Config Folder** to reach `settings.json` and the generated
`xemu.toml`. In separate-process mode, `xemu/xemu.log` has the core's own log.

The shell's rolling log lives under `%LOCALAPPDATA%\XGrid\logs` on Windows (the
per-user local application-data folder elsewhere). See
[Logs and diagnostics](../guides/diagnostics.md).

## Power-on failures

| Message | Cause | Fix |
| --- | --- | --- |
| “Choose a BIOS image first.” | No BIOS path, or the file is missing. | Set **Settings → System → BIOS image**. |
| “The emulator binary was not found.” | The packaged core is missing and no path is set. | Set **Emulator binary**, set `XGRID_XEMU_CORE`, or build `xemu/dist`. |
| “The in-process core library is not available; starting a separate core process.” | `xgrid_core.dll` is missing or incompatible. | Build the core, or use separate process deliberately. |
| “Power-on failed: …” | Validation or process-start error. | Read the full message and the Activity log. |
| The core opens its own window | Linux/macOS hosting; native in-window embedding is Windows-only. | Use the core window; headless runs use the shared-memory sink instead. |
| Save-state entries disabled | Separate-process hosting, including Linux. | Switch to in-process on Windows and power-cycle. |
| A core crash closes the whole app | In-process hosting shares the shell's process. | Switch **Core hosting** to **Separate process** for isolation. |
| The first-run wizard keeps returning | The data folder is not writable, or settings were not saved. | Check folder permissions and the Activity log. |

## Display problems

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Black display | Renderer or core start failure. | Wait a few seconds, check Activity, try OpenGL and a scale of 1. |
| Black display only in a game | The title may not render yet; compatibility. | Try a known-good title, check the core log. |
| Frozen picture | Core stopped or UI stalled. | Check status; stop and power on again. |
| No filters | A separate process has not relaunched since the filter changed. | Relaunch; in-process hosting applies filters live. |
| Vulkan missing | The core build has no Vulkan. | Use OpenGL; the Settings hint explains why. |
| Direct3D 11 missing | The Windows core build did not compile the preview backend. | Use OpenGL. |
| Low frame rate | High internal resolution, heavy title, CPU/GPU load. | Set scale to 1, try OpenGL, close background work. |
| Headless run publishes no frames | The binary is not the XGrid core, or the renderer cannot render. | Point `--core` at the XGrid build; try `--renderer OPENGL`. |

## Input problems

| Symptom | Cause | Action |
| --- | --- | --- |
| Keyboard does nothing | Window not focused, console off, or typing in a text field. | Click the display, power on, leave text fields. |
| Controller not detected | Not connected before start, or driver issue. | Connect, restart, check Windows Game Controllers. |
| Wrong button | Binding mismatch. | Remap in **Settings → Input**. |
| Sticks drift | Dead zone too low. | Raise the radial dead zone. |
| Player 2–4 does nothing | No XInput slot assigned to that port. | Assign a slot in **Settings → Input → Players**. |
| No input on Linux or macOS | The mapped-pad sink is Windows-only. | Use the core's own SDL input in its window. |
| Space types instead of fast-forwarding | A text field has focus. | Click the game picture first. |

## Audio problems

| Symptom | Cause | Action |
| --- | --- | --- |
| No sound | Backend **Null**, DSP off, or host device issue. | Select Auto/DirectSound, turn **Guest DSP** on. |
| Crackling | Host CPU load or backend issue. | Lower scale, close background work, try another backend. |
| WAV empty | No guest audio yet or DSP not running. | Check Activity and the core log; confirm DSP on. |
| Device disappeared | DirectSound device unplugged. | Refresh the device list or use **System default**. |

## Media problems

| Symptom | Cause | Action |
| --- | --- | --- |
| `.zar` refused | The archive is corrupt or stores a file name XDVDFS cannot represent. | Check Activity for the entry; rebuild the archive. |
| `.cci` refused | A later part of a split set was opened. | Open the first part (`game.1.cci`). |
| Full Redump ISO shows the video partition | The game partition was not detected. | Check that the image is complete and not truncated. |
| CHD will not boot | Corrupt or unsupported CHD. | Verify with another tool; try the ISO. |
| `.xbe` will not launch | A loose XBE is not a disc. | Wrap it in ISO/CHD/CCI/ZAR. |
| Game not in Library | Unsupported extension, missing folder, or cap reached. | Check the extension, folder and 4,096-entry cap. |
| Disc not inserted | No default disc and no selected game. | Select a game or set **Default disc image**. |

## Save-state problems

| Symptom | Cause | Action |
| --- | --- | --- |
| “Save states need the in-process emulator core.” | Separate-process hosting. | Switch to in-process. |
| Save fails | HDD image not writable or full. | Check permissions and free space. |
| Load leaves the guest frozen | The core resumed after a failed load; the state may not match. | Check Activity; power-cycle and try another slot. |
| Automatic resume did not happen | Snapshot missing, unreadable, or the game was launched from the Library. | Start from the Power button; check Activity. |
| Chihiro load rejected | A different netboot image or cabinet link is active. | Use the same `.bin`; disable the link. |

## Chihiro problems

See [Sega Chihiro](../guides/chihiro.md#troubleshooting) for firmware file
errors, SEGABOOT hangs, Error 05/14, linked cabinets and audio. For controls and
gun pointers, see [Chihiro controls](../guides/chihiro-controls.md).

## Performance problems

- Set **Internal resolution scale** to 1.
- Use **OpenGL**.
- Leave **Vertical sync** off.
- Close other GPU/CPU-heavy applications.
- Use an ISO instead of a heavily compressed image for the first test.
- On a laptop, ensure the discrete GPU is used.
- Keep the HDD image on local storage.

## Reset to defaults

1. Close XGrid.
2. Back up `<data>/settings.json`.
3. Rename or delete `settings.json`.
4. Start XGrid and configure firmware again.

Do not delete your HDD image; it contains the dashboard, saves and save states.

## Reporting a bug

Include:

- XGrid version and platform;
- renderer and core hosting mode;
- the Activity log lines and the matching `xgrid-YYYYMMDD.log` file around the
  failure;
- the image format (ISO/CHD/CCI/ZAR);
- whether the same title works in upstream xemu 0.8.139.

Use the [issue tracker](https://github.com/purelogiccode/XGrid/issues) for
emulation and UI bugs. Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/purelogiccode/XGrid/security/advisories)
or [`SECURITY.md`](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md).
