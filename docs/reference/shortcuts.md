# Keyboard shortcuts

## Application

| Shortcut | Action |
| --- | --- |
| `F5` | Power on / off |
| `P` | Pause / resume the console |
| `F10` | Step one frame while paused |
| `F6` | Quick save to the selected slot |
| `F7` | Quick load from the selected slot |
| `F8` | Save a screenshot (core picture in-process, whole window otherwise) |
| `F11` | Toggle fullscreen |
| `Space` | Toggle 4× fast-forward while a game is running |
| `Esc` | Pause and leave fullscreen; stop the game and show the library; close XGrid |
| `Ctrl+O` | Open a disc image (swaps the disc while running) |
| `Ctrl+E` | Eject the inserted disc |
| `Ctrl+S` | Save settings (Settings page only) |

`P`, `F10`, `F6`, `F7`, `Space` and live disc swap need in-process core hosting.
`Space` is ignored while a text field has focus, so it can still type a space.

## Escape sequence

| Start | First `Esc` | Second `Esc` | Third `Esc` |
| --- | --- | --- | --- |
| Game running | Pause and leave fullscreen | Stop the game and show the library | Close XGrid |
| Game paused | Stop the game and show the library | Close XGrid | - |
| No game, library visible | Close XGrid | - | - |
| No game, other page | Leave fullscreen | - | - |
| Console cannot pause | Leave fullscreen | Stop the game and show the library | Close XGrid |

## Default player-1 controls

| Xbox control | Keyboard |
| --- | --- |
| A | `A` |
| B | `S` |
| X | `D` |
| Y | `F` |
| D-pad | Arrow keys |
| Start | `Enter` |
| Back | `Backspace` |
| White | `Q` |
| Black | `E` |
| Left trigger | `Z` |
| Right trigger | `C` |

All bindings except **Space** are configurable in **Settings → Input**. See
[Controllers and input](../guides/input.md).

## Menus

| Menu | Entries |
| --- | --- |
| **File** | Open Disc…, Choose Disc Image…, Eject Disc, Open Config Folder, Open Screenshots Folder, Exit |
| **Emulation** | Power On / Off, Stop, Pause / Resume, Step One Frame, Save State (slot submenu), Load State, Delete State, Fullscreen |
| **View** | Console, Library, Settings, Activity, Add library folder…, Rescan library, Open covers folder, Open screenshots folder, Save Screenshot, Show Log Dock, FPS Overlay, Fullscreen |
| **Help** | About XGrid |

`Ctrl+O` and `F8` are also exposed in the File and View menus.

## Sega Chihiro

The Chihiro cabinet has a separate keyboard, mouse and gamepad map. See
[Chihiro controls](../guides/chihiro-controls.md).
