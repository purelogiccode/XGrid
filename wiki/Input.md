# Input

XGrid exposes up to four Xbox controllers. The keyboard always drives player 1;
an XInput controller can drive any player. On Windows the shell maps and forwards
all four ports. On Linux/macOS the core reads its own SDL input.

## Default keyboard map

| Action | Keys |
| --- | --- |
| A / B / X / Y | `A` / `S` / `D` / `F` |
| D-pad | Arrow keys |
| Start / Back | `Enter` / `Backspace` |
| White / Black | `Q` / `E` |
| Left / right trigger | `Z` / `C` |

**Space** is reserved for 4× fast-forward and is never stored as a pad binding.

## Remapping

Open **Settings → Input**. Click a key button, press the new key, and press
**Esc** to cancel capture. A keyboard action can hold comma-separated keys.
**Reset input defaults** restores the shipped map.

## XInput

- Load order: `xinput1_4`, `xinput1_3`, `xinput9_1_0`.
- **Players** assigns one XInput slot per Xbox port; **No gamepad** leaves a port
  empty.
- Player 1 merges keyboard and gamepad; players 2–4 use their gamepad only.
- The gamepad source per action applies to every player.

## Sticks and triggers

- Left/right radial dead zones.
- Trigger threshold.
- Invert left/right Y.
- Swap left and right sticks.

## Application shortcuts

`F5` power, `P` pause, `F10` step, `F6`/`F7` save/load, `F8` screenshot, `F11`
fullscreen, `Space` fast-forward, `Ctrl+O` open disc, `Ctrl+E` eject, `Ctrl+S`
save settings.

See [Playing games](Playing-Games) and
[Keyboard shortcuts](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/shortcuts.md).

## Troubleshooting

- Controller missing: connect it before starting XGrid and check Windows Game
  Controllers.
- Wrong button: remap or reset defaults.
- Drift: raise the dead zone.
- No input on Linux/macOS: use the core's own SDL input in its window.

Full guide: [Controllers and input](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/input.md).
