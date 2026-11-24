# Input

XGrid maps up to four Xbox controllers, one per port. The keyboard always drives
player 1.

> Shell remapping and pad forwarding require **Present core frames in the shell**.
> With the embedded window, the core reads its own SDL input. On Linux the shell
> frame sink is unavailable, so the core reads SDL input in its own window.

## Default keyboard map

| Action | Key(s) |
| --- | --- |
| A | `A`, `Space` |
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

Keyboard presses are overlaid on the controller report; digital buttons combine.

## Remapping

**Settings → Input**:

1. Click the key button.
2. Press the new key.
3. **Esc** cancels.

**Reset input defaults** restores the shipped map.

## XInput

- Loads `xinput1_4`, then `xinput1_3`, then `xinput9_1_0`.
- **Settings → Input → Players** assigns an XInput slot to each Xbox port; the
  default maps slot 1 to player 1 and so on, and **No gamepad** leaves a port
  empty.
- Player 1 merges the keyboard with its gamepad; players 2–4 use their gamepad.
- All players share the gamepad map and stick options.
- Keyboard still works without XInput.

## Stick and trigger options

- Left/right radial dead zone.
- Trigger threshold.
- Invert left/right Y.
- Swap left and right sticks.

## Shell shortcuts

| Key | Action |
| --- | --- |
| `F5` | Power on/off |
| `F6` | Quick save |
| `F7` | Quick load |
| `F8` | Screenshot |
| `F10` | Step one frame (while paused) |
| `F11` | Fullscreen |
| `P` | Pause / resume |
| `Ctrl+O` | Open disc (swaps while running) |
| `Ctrl+E` | Eject disc |
| `Ctrl+S` | Save settings |
