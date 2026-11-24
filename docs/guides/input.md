# Input

XGrid exposes up to four Xbox controllers, one per port. The keyboard always
drives **player 1**; an XInput controller can drive any player.

> The shell's remapping and pad forwarding are active when **Present core frames
> in the shell** is enabled. With the embedded core window, the core reads its
> own SDL input instead.

## Default keyboard map

| Action | Key(s) |
| --- | --- |
| A | `A`, `Space` |
| B | `S` |
| X | `D` |
| Y | `F` |
| D-pad up | `Up` |
| D-pad down | `Down` |
| D-pad left | `Left` |
| D-pad right | `Right` |
| Start | `Enter` |
| Back | `Backspace` |
| White | `Q` |
| Black | `E` |
| Left trigger | `Z` |
| Right trigger | `C` |

Keyboard presses are overlaid on the controller report; digital buttons combine.
The shell only publishes input while the console is running and the window is
active.

## Remapping

Open **Settings → Input**.

1. Click the key button beside an action.
2. Press the new key.
3. Press **Esc** to cancel capture.

A keyboard action can hold several comma-separated keys. **Reset input defaults**
restores the shipped map; save the settings to apply it.

## XInput controllers

- XGrid loads `xinput1_4`, then `xinput1_3`, then `xinput9_1_0`.
- If no XInput library is present, the keyboard still works.
- **Settings → Input → Players** assigns one XInput slot to each Xbox port. The
  default maps slot 1 to player 1, slot 2 to player 2, and so on; choose
  **No gamepad** to leave a port empty.
- Player 1 merges the keyboard and its assigned gamepad; players 2–4 use their
  gamepad only.
- The physical source for each action is selected in **Settings → Input** and
  applies to every player.
- Triggers use a configurable threshold; sticks use radial dead zones so
  diagonals survive.

### Stick and trigger options

| Option | Meaning |
| --- | --- |
| **Left stick dead zone** | Radial percentage ignored around the left stick center. |
| **Right stick dead zone** | Same for the right stick. |
| **Trigger threshold** | Values below this report the trigger as released. |
| **Invert left stick Y** | Flips vertical motion on the left stick. |
| **Invert right stick Y** | Flips vertical motion on the right stick. |
| **Swap left and right sticks** | Exchanges the two stick reports. |

## Application shortcuts

These are shell shortcuts, not gamepad bindings:

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `F6` | Quick save to the selected slot |
| `F7` | Quick load from the selected slot |
| `F8` | Save a screenshot of the window |
| `F10` | Step one frame while paused |
| `F11` | Toggle fullscreen |
| `Esc` | Leave fullscreen |
| `P` | Pause or resume the console |
| `Ctrl+O` | Open a disc image (swaps the disc while running) |
| `Ctrl+E` | Eject the inserted disc |
| `Ctrl+S` | Save settings (Settings page) |

Pause and frame step need in-process core hosting, like save states: the shell
calls the core's ABI to stop the machine and to run exactly one frame.

See [Shortcuts](../reference/shortcuts.md) for the full list.

## Troubleshooting

- **Controller not detected** — connect it before starting XGrid, check Windows
  Game Controllers, and watch the Activity log.
- **Wrong button** — remap the action or reset defaults.
- **Stick drifts** — raise the dead zone.
- **No input in game** — confirm the console is running, the shell window is
  focused, and **Present core frames in the shell** is on.
