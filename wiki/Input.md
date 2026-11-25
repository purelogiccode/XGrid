# Input

XGrid maps up to four Xbox controllers, one per port. The keyboard always drives
player 1.

> Shell remapping and pad forwarding work in both presentation modes on Windows.
> On Linux and macOS the core reads its own SDL input in its window.

## Default keyboard map

| Action | Key(s) |
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

Keyboard presses are overlaid on the controller report; digital buttons combine.
**Space** toggles 4x fast-forward while a game runs and is never stored as a pad
binding.

## Remapping

**Settings → Input**:

1. Click the key button.
2. Press the new key.
3. **Esc** cancels.

**Space** is reserved for fast-forward, so it is ignored as a pad key. **Reset
input defaults** restores the shipped map.

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
| `Space` | Toggle 4x fast-forward |
| `Esc` | Fullscreen: pause, then close on the next press |
| `P` | Pause / resume |
| `Ctrl+O` | Open disc (swaps while running) |
| `Ctrl+E` | Eject disc |
| `Ctrl+S` | Save settings |
