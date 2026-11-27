# Chihiro controls

The cabinet has its own keyboard, mouse, gamepad and light-gun map. Configure it
under **Settings → Input → Sega Chihiro cabinet keys**.

Click the game picture once after launch so the core receives keyboard focus.
Click the toolbar or a shell page to return focus to XGrid.

## Default controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Start | `Enter` | Start |
| Service | `L` | L3 |
| Coin | `5` | Back |
| Test | `K` | R3 |
| Steer left / right | `Left` / `Right` | Left stick X |
| Gas / brake | `Up` / `Down` | RT / LT |
| Gear up / down | `]` / `[` | D-pad up / down |
| View change | (game-specific) | Y |
| Card in | `C` | X |
| Gun trigger / body | (game-specific) | RT / A |
| Gun change / reload | (game-specific) | B |

Press **5** for a coin and **Enter** to start.

## Player 2

A second gamepad aims with its right stick, or use the arrow keys. Start is keypad
Enter, coin is keypad 2, card in is `V`. RT/A/B/LT map trigger/body/change/reload.
Bind the P2 keyboard alternatives under **Player 2** in Settings.

## Guns and mouse

Move the host cursor over the picture to aim player 1; left click fires, right
click is body/action or reload. Player 2 uses the second gamepad's right stick or
the arrow keys. The core menu is on **F2** in gun games.

Turn on **Separate gun pointers** to read each light gun or mouse independently.
Absolute guns aim players 1 and 2 first; relative mice start at the centre. Pin a
device by name with **Player 1/2 pointer**. Windows uses Raw Input, Linux evdev
and macOS IOHID.

## Gundam cockpit

WASD + Q/E on the left, arrow keys + Right Shift/Ctrl on the right, Space for the
pedal. Written to `[chihiro.jvs.gundam]`.

## Cards

Ghost Squad and Maximum Tune use the emulated card readers. Press **C** (X) for
player 1 and **V** for player 2. Configure card files under **Settings → System →
Sega Chihiro → Card files**; empty creates a blank card on first insert.

## Force feedback

**Force feedback** maps drive-board effects to an SDL haptic wheel, or rumbles the
first gamepad when no wheel is found. **Feedback strength**, **Wheel weight**,
**Autocenter**, **Autocenter strength** and **Invert direction** tune it.

Full guide: [Chihiro controls](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/chihiro-controls.md).
