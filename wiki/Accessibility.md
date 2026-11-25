# Accessibility

XGrid is designed to be usable without a mouse, and every interactive control
has a tooltip that explains what it does.

## Keyboard

| Key | Action |
| --- | --- |
| `F5` | Power on or off |
| `P` | Pause or resume |
| `F10` | Step one frame while paused |
| `F6` / `F7` | Quick save / quick load |
| `F8` | Save a screenshot |
| `F11` | Toggle fullscreen |
| `Esc` | Pause, then close the fullscreen shell on the next press |
| `Ctrl+O` / `Ctrl+E` | Open/swap a disc / eject |
| `Space` | Toggle 4x fast-forward |

`Tab` and `Shift+Tab` move through pages; `Enter`/`Space` activate the focused
button; `Esc` closes dialogs. Typing in a text box does not trigger pad input.

## Visuals

- Six accent palettes are available in Settings.
- The dark theme is the supported baseline and keeps body text contrast high.
- Surface scale and window size controls handle display scaling.
- Tooltips describe abbreviated labels such as **Config** and icon-only
  toolbar buttons.

## Screen readers

Buttons and text fields expose their labels to the platform automation layer.
The native emulator display is a single image from the screen reader's point of
view, which is inherent to emulation.

Report an unlabeled control with the window, the control and the platform; the
diagnostics log and environment snapshot are attached automatically. Full
details: [Accessibility guide](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/accessibility.md).
