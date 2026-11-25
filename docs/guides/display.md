# Display and filters

The core owns the emulator's picture. It renders and presents with its own
swapchain; the Avalonia shell provides the window, layout and controls but never
receives, copies, filters or redraws a frame.

## Presentation

### Windows: embedded core window

- The core creates its own window; XGrid finds it and reparents it into the Home
  page, whether the core is in-process or a separate process.
- The core paints directly into that child surface. The shell performs no
  per-frame work: this is the fastest path and the one to use for maximum
  performance.
- The core window is created hidden and shown only after the reparent, so it
  does not flash as a second top-level window.
- The embedded child is created `WS_EX_NOACTIVATE`, so the shell window keeps
  keyboard focus and the mapped pad input stays live.

### Linux and macOS

Native in-window embedding is Windows-only for now. On Linux and macOS the core
opens its own SDL window outside the Home page; use that window for the picture,
filters and input. The shell keeps the library, settings, log and screenshots.
The macOS archives contain the shell only; point them at an external xemu build.
The platform embedding plan is in
[Platform embedding](../development/platform-embedding.md).

Headless runs use the POSIX shared-memory display sink on Linux and macOS, so the
core still renders off-screen with no visible window. See
[Headless runs](headless.md).

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

These belong to the emulator and affect the picture before the blit:

- **Renderer** — OpenGL, Vulkan (if compiled in) or Null.
- **Internal resolution scale** — up to 8× the guest resolution (the list offers
  1–6 and 8). Higher scales cost GPU time and memory.
- **Texture filtering** — linear or nearest.
- **Aspect ratio** — auto, native, 4:3, 16:9.
- **Fit mode** — scale, center, stretch.
- **AV pack** — the video output reported to the guest.
- **Vertical sync** — off by default for low latency.
- **Fullscreen on startup / exclusive fullscreen**.

When the core reports that Vulkan is not compiled in, XGrid removes Vulkan from
the renderer list and coerces a saved Vulkan choice to OpenGL with a warning in
the Activity log.

## Fullscreen

- **F11** toggles fullscreen; the toolbar's **Fullscreen** button enters it.
  The shell chrome (menu, toolbar, header, transport strip, log dock and status
  bar) is hidden, and the console picture fills the screen.
- In fullscreen, **Esc** pauses a running game on the first press. Press **Esc**
  again to close XGrid.
- With no game running, **Esc** returns to the windowed shell.

The shell fullscreen mode is separate from the core's **Start fullscreen**
setting. Use F11 when you only want to leave fullscreen during play.

## Captions and screenshots

The caption under the display shows the renderer; it no longer reports a frame
size because the shell never sees a frame.

**F8** (or **View → Save Screenshot**) saves a PNG of the XGrid window,
including the console picture, to `<data>/screenshots/`. The file name is
timestamped (`screenshot-YYYYMMDD-HHMMSS.png`), with a suffix if two captures land
in the same second. While a console runs on Windows the window is captured from
the screen so the native picture is included; otherwise the shell's visual tree
is rendered.

The Library page shows the newest captures as a thumbnail strip. Click a picture
to open it in the default viewer, use **Refresh** to re-read the folder after
adding files, and **Open folder** to browse the PNGs directly.

## Pause and frame step

- **P** (or **Emulation → Pause / Resume**) stops the machine on the next frame
  boundary and keeps the last picture on screen with a **Paused** badge.
- **F10** (or **Emulation → Step One Frame**) advances exactly one frame while
  paused.

Both controls need in-process core hosting, like save states. The status bar
shows **Paused** and the picture stays until the next step or resume.

## Fast forward

Press **Space** while a game runs, or click **4x** in the toolbar, to turn on
**4x**; press Space again or click **4x** to return to normal speed. The status
bar shows **Running 4x** while it is on. The core scales the guest clock and the
vblank pace together, so timers, the TSC and the picture all run four times
faster. It needs in-process core hosting like pause and frame step.

## Frame-rate overlay

Turn on **View → FPS Overlay** to draw the core's present rate and the average
frame time over the console picture. The core draws the overlay in its own HUD,
so it needs no shell frame path and works with the same performance as the game.
The choice is remembered in `settings.json`. With a separate core process the
setting takes effect on the next launch; in-process hosting applies it live.

## Performance notes

- The core's own window is embedded directly; the shell adds no per-frame work.
- Prefer **OpenGL** unless your build reports Vulkan and your driver is stable.
- Lower **Internal resolution scale** first when frames are slow.
- Leave **Vertical sync** off unless you see tearing.
- Keep filters in the core. A shell-side filter or bitmap canvas is not
  supported by design.

## If the picture is black or frozen

1. Check the **Activity** log for a renderer or launch error.
2. Confirm the core is running.
3. On Linux or macOS, look for the core's own window.
4. Try OpenGL and a scale of 1.
5. See [Troubleshooting](../reference/troubleshooting.md).
