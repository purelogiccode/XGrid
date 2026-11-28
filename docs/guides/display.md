# Display and filters

The core owns the emulator's picture. It renders and presents with its own
swapchain; the Avalonia shell provides the window, layout and controls but never
receives, copies, filters or redraws a frame.

## How the picture is presented

### Windows

The core creates its own window; XGrid finds it and reparents it into the Home
page, whether the core is in-process or a separate process.

- The core paints directly into that child surface. The shell performs no
  per-frame work.
- The core window is created hidden and shown only after the reparent, so it does
  not flash as a second top-level window.
- The embedded Xbox child is created `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT`, so
  the shell window keeps keyboard focus, mapped pad input stays live and clicking
  the picture cannot swallow the Space fast-forward shortcut. A Chihiro picture
  keeps direct focus because the cabinet reads its keyboard, mouse and gamepad
  from the core itself.

### Linux and macOS

Native in-window embedding is Windows-only for now. On Linux and macOS the core
opens its own SDL window outside the Home page; use that window for the picture,
filters and input. The shell keeps the library, settings, log and screenshots.

The macOS archives contain the shell only; point **Settings → System → Emulator
binary** at an external xemu 0.8.139 build. Headless runs use the POSIX
shared-memory display sink, so the core still renders off-screen with no visible
window.

### Headless

Headless runs publish frames into a named shared section for frame counting.
They do not present a picture. See
[Command line and headless](headless.md).

## Presentation filters

Filters are implemented in the core's final blit shader; the shell only selects
one.

| Filter | Effect |
| --- | --- |
| **Nearest (sharp)** | Nearest-neighbor sampling. Crisp pixels, cheapest. |
| **Bilinear (smooth)** | Bilinear upscale. Softer edges. |
| **Scanlines** | Nearest sampling plus an every-other-row light reduction. |
| **CRT mask** | Scanlines plus a repeating R/G/B phosphor triad. |

Filters are applied by the core at the source resolution before scaling, so the
mask stays stable at any window size. The filter is a core setting; the core's
**Texture filtering** setting is separate.

## Core rendering settings

These belong to the emulator and affect the picture before the final blit:

- **Renderer** — Direct3D 11 (Windows builds, preview), OpenGL, Vulkan (when
  compiled) or Null.
- **Internal resolution scale** — 1, 2, 3, 4, 5, 6 or 8 times the guest
  resolution. Higher scales cost GPU time and memory.
- **Texture filtering** — linear or nearest.
- **Window startup size** — the core's window size before embedding.
- **Vertical sync** — off by default for low latency.
- **Frame-rate limit** — how often the core presents the latest guest frame.
  **Unlimited** is the historical pace; **60 fps** is a fixed real-time target.
  The guest clock is unchanged.
- **Skip missed frames** — when a frame-rate limit is set and the host is late,
  drop the missed presentations instead of showing them in a burst.
- **Start fullscreen** and **Exclusive fullscreen**.
- **Aspect ratio** — auto, native, 4:3 or 16:9.
- **Fit mode** — scale, center or stretch.
- **AV pack** — the video output reported to the guest.

When the core reports that Vulkan is not compiled in, XGrid removes Vulkan from
the renderer list and coerces a saved Vulkan choice to OpenGL with a warning in
the Activity log. Direct3D 11 follows the same rule and is offered only when the
Windows core reports it.

## Fullscreen

- **F11** toggles fullscreen; the toolbar's **Fullscreen** button enters it.
  The shell chrome (menu, toolbar, header, transport strip, log dock and status
  bar) is hidden, and the console picture fills the screen.
- In fullscreen, **Esc** pauses a running game and returns to the windowed
  shell. The next **Esc** stops the console and shows the library; the next
  closes XGrid.
- With no game running, **Esc** leaves fullscreen outside the library and closes
  XGrid from the library.
- A console that cannot pause leaves fullscreen on the first press; the next
  press stops the console and shows the library.

The shell fullscreen mode is separate from the core's **Start fullscreen**
setting. Use `F11` when you only want to leave fullscreen during play.

## Screenshots

**F8** (or **View → Save Screenshot**) saves a PNG of the console picture to
`<data>/screenshots/`.

- With in-process hosting the core writes the capture itself
  (`xemu-YYYY-MM-DD-HH-MM-SS.png`), so the embedded picture is included without
  the shell reading frames.
- When no console runs, or with separate-process hosting, the shell captures the
  whole window (`screenshot-YYYYMMDD-HHMMSS.png`).

Open the folder with **View → Open screenshots folder**.

## Pause, frame step and fast-forward

- **P** (or **Emulation → Pause / Resume**) stops the machine on the next frame
  boundary and keeps the last picture on screen with a **Paused** badge.
- **F10** advances exactly one frame while paused.
- **Space** toggles 4× fast-forward while a game runs. It works in both hosting
  modes: in-process through the core ABI, and a separate process through the
  core window's registered speed message. The embedded Xbox picture does not
  take keyboard focus, so Space always reaches the shell.

Pause and frame step need in-process core hosting, like save states. See
[Playing games](playing-games.md).

## Frame-rate overlay and pacing

Turn on **Settings → Graphics → Frame-rate overlay** (or **View → FPS Overlay**)
to draw the core's present rate and average frame time over the console picture.
The core draws the overlay in its own HUD, so it needs no shell frame path and
works at game performance. The choice is remembered in `settings.json`.

**Frame-rate limit** caps presented frames per second. **Unlimited** keeps the
core's historical behavior of presenting as often as the host allows; the fixed
choices (30, 60, 90, 120, 144 and 240 fps) limit presentation only. The emulated
machine keeps its own clock, so game speed and audio are unchanged. Fast-forward
scales the limit with the speed multiplier, so a 60 fps choice presents up to
240 fps at 4×.

**Skip missed frames** changes what happens when the host cannot keep up with
the chosen limit. With skipping off, the core catches up by presenting the late
frames back-to-back. With skipping on, it drops the missed deadlines and resumes
at the next interval, trading smoothness for a lighter, more even render load.
The option has no effect while the limit is **Unlimited**.

The shell only selects these values; pacing and skipping run in the core's
render loop. In-process hosting applies a change live. With a separate core
process, the initial values come from the launch environment, so a change takes
effect on the next launch.

## Performance notes

- The core's own window is embedded directly; the shell adds no per-frame work.
- Prefer **OpenGL** unless your build reports Vulkan or Direct3D 11 and that
  backend is stable for your titles.
- Lower **Internal resolution scale** first when frames are slow.
- Set a **Frame-rate limit** of 60 and turn on **Skip missed frames** when a title
  cannot hold its full speed and the extra presentations only add load.
- Leave **Vertical sync** off unless you see tearing.
- Keep filters and the overlay in the core.
- Use an SSD for the HDD image if save states or dashboard loads feel slow.

## If the picture is black or frozen

1. Check the **Activity** log for a renderer or launch error.
2. Confirm the core is running and the status bar says **Running**.
3. On Linux or macOS, look for the core's own window.
4. Try OpenGL and a scale of 1.
5. Check that the included XGrid core is being used; a stock upstream xemu build
   has no embedding or headless display sink.
6. See [Troubleshooting](../reference/troubleshooting.md).
