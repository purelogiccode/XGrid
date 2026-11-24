# Troubleshooting

Start with **Activity**. It records the launch plan, renderer, disc, audio backend
and failures.

## Power-on

| Message | Fix |
| --- | --- |
| “Choose a BIOS image first.” | Set **Settings → System → BIOS image**. |
| “The emulator binary was not found.” | Set the core path, `XGRID_XEMU_CORE`, or build `xemu/dist`. |
| “The in-process core library is not available…” | Build `xgrid_core.dll` or accept the separate process. |
| The core opens its own window | Linux/macOS hosting; the shell frame sink is Windows-only. |
| Save-state entries disabled | Switch to in-process hosting on Windows. |
| A core crash closes the whole app | Switch **Core hosting** to **Separate process**. |

## Display

| Symptom | Fix |
| --- | --- |
| Black display | Wait for first frame; check Activity; try the other presentation mode. |
| No filters | Enable **Present core frames in the shell**. |
| Vulkan missing | Build has no Vulkan; use OpenGL. |
| Low FPS | Scale 1, OpenGL, close background work. |

## Input

| Symptom | Fix |
| --- | --- |
| Keyboard ignored | Focus the window; leave text fields; power on. |
| Controller not found | Connect before start; check Windows Game Controllers. |
| Wrong button | Remap in Settings → Input. |
| Drift | Raise the dead zone. |
| No input with embedded window | Enable the frame sink. |

## Audio

| Symptom | Fix |
| --- | --- |
| No sound | Backend not None; Guest DSP on; check output device. |
| Crackling | Lower scale; close background work; try another backend. |
| Empty WAV | Guest produced no audio or DSP off. |

## Media

| Symptom | Fix |
| --- | --- |
| `.zar` refused | Check **Activity**: the archive may be corrupt or contain a name XDVDFS cannot store. Both entry-form and file-tree archives are supported. |
| `.xbe` won’t launch | Wrap it in ISO/CHD. |
| Game not listed | Check extension, folder and 4,096-entry cap. |

## Save states

| Symptom | Fix |
| --- | --- |
| “Needs the in-process emulator core.” | Switch hosting mode. |
| Save fails | Check HDD permissions and free space. |
| Load frozen | Check Activity; power-cycle and try another slot. |

## Reset

Close XGrid, back up and rename `<data>/settings.json`, restart. Do not delete the
HDD image; it holds the dashboard and save states.

For more, see the
[full troubleshooting page](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/troubleshooting.md).
