# Playing games

## Power and sessions

- **F5** powers the console on or off.
- With no disc, XGrid boots the dashboard from the HDD image.
- By default the shell stays open on the powered-off screen after shutdown.
- **Settings → System → Session** controls power-on at launch, stay-open,
  powered-off screen, automatic session save and resume.

## Pause and step

- **P** pauses or resumes on the next frame boundary.
- **F10** advances one frame while paused.
- The last frame stays on screen with a **Paused** badge.

Both need in-process core hosting.

## Fast-forward

Press **Space** or click **4×** to toggle 4× speed. Press again to return to 1×.
The core scales the guest clock and vblank together. Needs in-process hosting.
Space is reserved for this shortcut.

## Discs

- **Ctrl+O** or **File → Open Disc…** inserts a new disc.
- **Ctrl+E** ejects and leaves the tray empty.
- Clicking another Library game swaps to it.
- Live swap needs in-process hosting. With a separate core process, power off
  first.

## Save states and screenshots

- **F6** / **F7** quick save / quick load the selected slot.
- **F8** saves a PNG. With in-process hosting the core captures its own picture;
  otherwise the shell captures the whole window.
- Screenshots go to `<data>/screenshots/`.

## Fullscreen

**F11** toggles fullscreen. **Esc** pauses a running game; a second **Esc** closes
XGrid. With no game running, **Esc** leaves fullscreen.

## Overlay

**View → FPS Overlay** draws the core's present rate and average frame time over
the picture. In-process hosting applies it live; a separate process applies it on
the next launch.

Full guide: [Playing games](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/playing-games.md).
