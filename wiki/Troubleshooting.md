# Troubleshooting

Start with **Activity**. It records the launch plan, renderer, disc, audio backend
and failures. The full exception is in the rolling log under `XGrid/logs` in the
per-user local application-data folder.

## First checks

1. Is a BIOS configured and found?
2. Are the MCPX, EEPROM and HDD images present?
3. Is the core under `core/xemu/` or at the configured path?
4. On Linux/macOS, is the core's own window visible?
5. What is the first error in Activity?

Use **File → Open Config Folder** to reach `settings.json` and `xemu.toml`.
Separate-process mode also writes `<data>/xemu/xemu.log`.

## Power-on

| Message | Fix |
| --- | --- |
| "Choose a BIOS image first." | Set **Settings → System → BIOS image**. |
| "The emulator binary was not found." | Set **Emulator binary**, `XGRID_XEMU_CORE`, or build `xemu/dist`. |
| "The in-process core library is not available…" | Build `xgrid_core.dll` or use the separate process deliberately. |
| The core opens its own window | Linux/macOS hosting; embedding is Windows-only. |
| Save-state entries disabled | Switch to in-process hosting on Windows. |
| A core crash closes the whole app | Switch **Core hosting** to **Separate process**. |

## Display

| Symptom | Fix |
| --- | --- |
| Black display | Wait for the core; check Activity; try OpenGL and scale 1. |
| Colored/stale bottom band after resize | Update the core; the window now reconciles sizes, clears the full target and clamps edge sampling. |
| No filters | Relaunch the separate process; in-process applies live. |
| Low FPS | Scale 1, OpenGL, close background work; try a 60 fps limit with **Skip missed frames**. |
| Vulkan missing | Build has no Vulkan; use OpenGL. |
| Headless no frames | Use the XGrid core; try `--renderer OPENGL`. |

## Input

| Symptom | Fix |
| --- | --- |
| Keyboard ignored | Focus the window; leave text fields; power on. |
| Controller not found | Connect before start; check Windows Game Controllers. |
| Wrong button | Remap in Settings → Input. |
| Drift | Raise the dead zone. |
| Player 2–4 does nothing | Assign an XInput slot to that port. |
| No input on Linux/macOS | Use the core's SDL input in its window. |

## Audio

| Symptom | Fix |
| --- | --- |
| No sound | Backend not Null; Guest DSP on; output device works. |
| Crackling | Lower scale; close background work; try another backend. |
| Empty WAV | Guest produced no audio or DSP is off. |

## Media

| Symptom | Fix |
| --- | --- |
| `.zar` refused | Corrupt archive or a name XDVDFS cannot store; check Activity. |
| `.cci` refused | Open the first part of a split set. |
| Full Redump shows the video partition | The game partition was not detected; check the image. |
| `.xbe` won't launch | Wrap it in ISO/CHD/CCI/ZAR. |
| Game not listed | Check extension, folder and the 4,096-entry cap. |

## Save states

| Symptom | Fix |
| --- | --- |
| "Needs the in-process emulator core." | Switch hosting mode. |
| Save fails | Check HDD permissions and free space. |
| Load frozen | Check Activity; power-cycle and try another slot. |
| Resume did not run | Start from Power, not the Library; check Activity. |

## Chihiro

See [Chihiro](Chihiro) for firmware and conversion errors, and
[Chihiro controls](Chihiro-Controls) for input, guns and cards.

## Reset

Close XGrid, back up and rename `<data>/settings.json`, restart. Do not delete the
HDD image; it holds the dashboard and save states.

## Report a bug

Include the version, platform, renderer, hosting mode, Activity lines and the
matching log excerpt. Use the
[issue tracker](https://github.com/purelogiccode/XGrid/issues). For
vulnerabilities, follow
[SECURITY.md](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md).

Full page: [Troubleshooting](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/troubleshooting.md).
