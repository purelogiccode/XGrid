# Accessibility

XGrid aims to keep actions reachable from the keyboard and to explain controls
before they are used.

## Tooltips

Every interactive control has a tooltip: menus, toolbar buttons, path boxes, file
pickers, toggles, sliders, list rows, tab headers and dialogs. Hover or focus a
control to read what it does.

## Keyboard

| Key | Action |
| --- | --- |
| `F5` | Power on/off |
| `P` | Pause/resume |
| `F10` | Step one frame while paused |
| `F6` / `F7` | Quick save / load |
| `F8` | Screenshot |
| `F11` | Fullscreen |
| `Esc` | Pause and leave fullscreen; stop the game and show the library; close XGrid |
| `Ctrl+O` / `Ctrl+E` | Open/swap disc / eject |
| `Space` | 4× fast-forward while running |

## Focus

- `Tab` / `Shift+Tab` move through the page.
- `Enter` activates; `Space` activates when a game is not running (while running
  it is fast-forward). `Esc` closes dialogs.
- Menus support `Alt` and the arrow keys.
- Modal dialogs keep focus until closed.

## Visual

- Six accent palettes under **Settings → Appearance**.
- Dark theme with high-contrast body text.
- Monospace Activity/log lines.
- The display scale follows the desktop scale factor.

## Screen readers

Avalonia exposes control names and automation peers. Text buttons are announced
by label; icon-only buttons have an accessible name and tooltip. The emulator
picture is a native child window and is announced as one image, not individual
game elements.

## Known limits

- Game frames are not announced.
- Update/donation dialogs do not resize with large text settings.
- High-contrast themes are not separately tested; the dark theme is the baseline.
- Chihiro gun games use a pointer for player 1.

Report accessibility issues with the window, control, platform, display scale and
screen reader. See [Logging and diagnostics](Diagnostics).

Full page: [Accessibility](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/accessibility.md).
