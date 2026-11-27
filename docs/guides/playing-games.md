# Playing games

This page covers the controls and session behavior around a running console.
Display tuning is in [Display and filters](display.md); controller mapping is in
[Controllers and input](input.md).

## The Home page

The Home page (the console page) contains:

- **Power on / off** and **Stop**;
- the live console picture, or the powered-off panel;
- the current disc and format;
- the renderer and run state;
- **Open disc**, **Save state** and **Load state** shortcuts;
- status messages and the most recent activity.

The status bar at the bottom of the window repeats the renderer and run state:
**Stopped**, **Running**, **Running 4x**, **Paused** or **Faulted**.

## Power on and off

- **Power on** (`F5`) boots the dashboard from the HDD image, or the selected
  disc if one is configured.
- **Power off** (`F5` while running) shuts the guest down cleanly.
- **Stop** ends the session immediately.

By default XGrid stays open after a normal power-off and shows the powered-off
screen. Turn that off under **Settings → System → Session → Keep XGrid open
after power off** if you want the window to close instead. A faulted core always
stays open so the error is visible.

## Starting a game

Launch from the Library, press `Ctrl+O` to open a disc, or use the toolbar's
**Open** button.

- If the console is stopped, XGrid starts it with the chosen disc.
- If the console is running, XGrid inserts the disc into the running machine
  instead of rebooting. This is a tray cycle; the game notices a disc change.

Live disc insertion needs in-process core hosting. With a separate core process,
power off before changing the disc.

## Pause and frame step

- **P** or **Emulation → Pause / Resume** pauses the machine on the next frame
  boundary. The last picture stays on screen with a **Paused** badge.
- **F10** or **Emulation → Step One Frame** advances exactly one frame while
  paused.
- Press **P** again to resume.

Pause and frame step need in-process core hosting.

## Fast-forward

Press `Space` or click **4×** in the toolbar to toggle 4× fast-forward. Press it
again to return to 1×. The status bar shows **Running 4x** while it is active.

The core scales the guest clock and the vblank pace together, so guest timers,
the Xbox TSC and the picture all run four times faster. Fast-forward needs
in-process core hosting.

> `Space` is reserved for fast-forward. It is never stored as a pad binding.

## Changing discs

While the console runs:

- **File → Open Disc…** (`Ctrl+O`) chooses a new disc and inserts it.
- **File → Eject Disc** (`Ctrl+E`) opens the tray and leaves it empty.
- Clicking another game in the Library swaps to that disc.

The core changes the block medium on its own loop, so the guest sees a tray
cycle rather than a reboot. Multi-disc games that ask for the next disc accept
the swap directly. In-process hosting is required.

## Save states

XGrid exposes four snapshot slots and an optional automatic session snapshot.
Use `F6` / `F7`, the Home page buttons, or the **Emulation** menu. See
[Save states](save-states.md).

## Screenshots

Press `F8`, or use **View → Save Screenshot**. With in-process hosting the core
writes a PNG of the console picture to `<data>/screenshots/`. With no running
console, or with a separate core process, the shell captures the whole window
instead.

Open the folder with **View → Open screenshots folder** or **File → Open
Screenshots Folder**.

## Fullscreen

- **F11** toggles fullscreen; the toolbar's **Fullscreen** button also enters it.
- The shell chrome is hidden and the console picture fills the screen.
- **Esc** pauses a running game on the first press.
- Press **Esc** again to close XGrid.
- If no game is running, **Esc** returns to the windowed shell.

The shell fullscreen mode is separate from the core's **Start fullscreen**
setting.

## Session lifecycle

Settings under **Settings → System → Session** control the edges of a session:

| Setting | Behavior |
| --- | --- |
| **Power on when XGrid starts** | Boot the configured disc or dashboard when the window opens. A command-line autostart wins. |
| **Keep XGrid open after power off** | Stay on the powered-off screen after a normal shutdown. |
| **Show the powered-off screen** | Show the XGrid power panel instead of a black picture area. |
| **Save state before power off** | Write the automatic `xgrid-session` snapshot on Power off, Stop or window close. |
| **Resume the saved session** | Restore that snapshot when the Power button starts the dashboard. Launching a game from the Library starts it fresh. |

The automatic snapshot needs in-process hosting and a writable HDD image.

## Frame-rate overlay

Turn on **View → FPS Overlay** to draw the core's present rate and average frame
time over the picture. The core draws it; the shell never touches a frame. The
choice is remembered in `settings.json`.

In-process hosting applies a change live. With a separate core process, the
overlay setting takes effect on the next launch.

## When something goes wrong

Open **Activity** first. It records the launch plan, renderer, RAM, disc, audio
backend and the first failure. See
[Troubleshooting](../reference/troubleshooting.md).
