# Accessibility

XGrid is a desktop emulator, so accessibility mostly means **not hiding actions
behind a mouse** and **explaining what a control does before it is used**. The
shell follows the Fluent theme and keeps keyboard focus visible.

## Tooltips

Every interactive control in every window and page carries a tooltip: menus,
toolbar buttons, path boxes, file pickers, combo boxes, toggles, sliders, list
rows, tab headers, the first-run wizard and the update dialog. Tooltips are
written as short action descriptions, not as restatements of the label.

Hover over a control, or focus it and wait for the platform tooltip delay, to see
what it changes. Tooltips are also useful when a label is abbreviated, such as
**Config** for Settings or the icon-only toolbar buttons.

## Keyboard

The main window keeps a global key handler while a game is not typing. The most
important shortcuts are:

| Key | Action |
| --- | --- |
| `F5` | Power on or off |
| `P` | Pause or resume |
| `F10` | Step one frame while paused |
| `F6` / `F7` | Quick save / quick load |
| `F8` | Save a screenshot |
| `F11` | Toggle fullscreen |
| `Esc` | Pause a running game, then close the shell on the next press in fullscreen |
| `Ctrl+O` / `Ctrl+E` | Open or swap a disc / eject |
| `Space` | Toggle 4× fast-forward while running |

Text boxes and other typing targets keep their normal editing keys; the pad
handler ignores input while a text control has focus.

## Focus and navigation

- `Tab` / `Shift+Tab` move through the current page in visual order.
- `Enter` activates the focused button; `Space` also activates it when a game is
  not running (while running, Space is fast-forward). `Esc` closes dialogs.
- Menus are reachable with `Alt` and the arrow keys.
- The first-run wizard, donation dialog and update prompt are modal, so focus
  stays inside them until they are closed.
- Destructive actions (delete state, eject, stop) state their consequence before
  or after the action.

## Visual design

- Six accent palettes (Blue, Emerald, Aqua, Violet, Amber, Crimson) are chosen in
  **Settings → Appearance**.
- The default dark theme keeps body text above the usual contrast target; the
  accent is used for emphasis, not for long text.
- The log dock and Activity page use a monospace font so timestamps and levels
  line up.
- The display can be scaled with the resolution-scale and window-size controls;
  the shell layout follows the desktop scale factor.
- Fullscreen hides the shell chrome, not the console picture or the core's HUD.

## Screen readers

Avalonia exposes control names and automation peers to the platform. Buttons
with text labels are announced by their label; icon-only toolbar buttons carry a
tooltip and an accessible name derived from the label text beside the icon. The
log list exposes each line as text.

If a control is announced without a useful name, report it with the window, the
control label and the platform screen reader. Accessibility reports use the same
diagnostics channel as other bugs; see
[Logs and diagnostics](diagnostics.md).

## Known limitations

- The native emulator display is a child window; a screen reader sees it as a
  single image, not as individual game elements. This is inherent to emulation.
- The frame presentation path is tuned for games and does not announce frames.
- The update and donation dialogs are intentionally small and centered; they do
  not resize with large text settings.
- High-contrast platform themes are not separately tested; the dark theme is the
  supported baseline.
- The Chihiro gun games use the host cursor as an aiming device; keyboard-only
  aim is available for player 2, while player 1's gun is pointer-based.

## Reporting a problem

Include the window, the control, what you expected to hear or see, and the
platform (Windows/Linux/macOS), display scale and screen reader. The diagnostics
log and environment snapshot make the report actionable without a screen
recording.
