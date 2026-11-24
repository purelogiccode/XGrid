# Display and filters

XGrid can present the emulator's picture in two ways. The default is the shell
frame sink; the fallback is the core's own window embedded in the Home page.

## Presentation modes

### Shell frame sink (default)

**Settings → System → Present core frames in the shell** is on by default.

- The patched core publishes every presented frame as top-down BGRA into a named
  shared section.
- The shell reads the latest frame, ignores stale frames, and presents at a
  paced cadence (latest wins; it never queues frames).
- The core window is hidden; the shell owns scaling, filtering, the caption and
  screenshots.
- The shell also publishes the player-1 pad to the core.

### Embedded core window

Turn **Present core frames in the shell** off.

- The core creates its own window; XGrid finds it and reparents it into the Home
  page, whether the core is in-process or a separate process.
- The core paints with its own swapchain, so the shell adds no per-frame copy.
  This is the fastest mode at large window sizes.
- The core window is created hidden and shown only after the reparent, so it
  does not flash as a second top-level window.
- The shell presentation filter does not apply.
- The shell pad is not forwarded; the core reads its own SDL input.

### Choosing a mode

| Mode | Best for | Trade-offs |
| --- | --- | --- |
| Shell frame sink | Shell filters, FPS overlay, uniform screenshots | Copies and scales each frame on the UI thread; high resolutions can cap below 60 fps |
| Embedded core window | Maximum frame rate and native input | No shell filters or FPS overlay; the core's window is a child of the Home page |

If a game feels slow at a large window size, turn **Present core frames in the
shell** off and compare. The embedded window is the path upstream xemu uses.

> Both modes require the patched core. A stock xemu executable has neither the
> display sink nor the input sink.

### Linux and macOS

The shell frame sink and window embedding are Windows-only. On Linux the core
opens its own SDL window, outside the Home page; use that window for the
picture, filters and input. The shell keeps the library, settings, log and
screenshots. The macOS archives contain the shell only; point them at an
external xemu build.

Headless runs are the exception: `--headless` uses a POSIX shared-memory display
sink on Linux and macOS, so the core still renders off-screen with no visible
window. See [Headless runs](headless.md).

## Shell presentation filters

| Filter | Effect |
| --- | --- |
| **Nearest (sharp)** | Nearest-neighbor sampling. Crisp pixels, cheapest. |
| **Bilinear (smooth)** | Bilinear upscale. Softer edges. |
| **Scanlines** | Nearest sampling plus an every-other-row light reduction. |
| **CRT mask** | Scanlines plus a repeating R/G/B phosphor triad. |

Filters are applied at the source resolution by the shell before scaling, so the
mask stays stable at any window size. The masks preserve the frame's alpha and
leave a frame that does not match its declared geometry unfiltered rather than
half-masked. The filter is a shell setting; the core's **Texture filtering**
setting is separate.

## Core rendering settings

These belong to the emulator and affect the picture before the shell sees it:

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

- **F11** toggles fullscreen.
- In fullscreen, **Esc** pauses a running game on the first press. Press **Esc**
  again to close XGrid.
- With no game running, **Esc** returns to the windowed shell.

The shell fullscreen mode is separate from the core's **Start fullscreen**
setting. Use F11 when you only want to leave fullscreen during play.

## Captions and screenshots

The caption under the display shows the actual frame size and renderer, for
example `1280 x 960 - OpenGL`. It appears only after the first frame.

**F8** (or **View → Save Screenshot**) renders the whole shell window to a PNG in
`<data>/screenshots/`. The file name is timestamped
(`screenshot-YYYYMMDD-HHMMSS.png`), with a suffix if two captures land in the same
second.

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

Press **Space** while a game runs to turn on **4x**; press it again to return to
normal speed. The status bar shows **Running 4x** while it is on. Fast-forward
speeds up the vblank pace the core presents at, so it needs in-process core
hosting like pause and frame step.

## Frame-rate overlay

Turn on **View  FPS Overlay** to draw the shell's present rate and the average
frame time over the console picture. The overlay is hidden while the console is
stopped and is remembered in `settings.json`. It reports what the shell actually
presented, so a fast core with a capped display still shows the cap. Use it with
the headless `--benchmark` mode when you need a repeatable number.

## Performance notes

- Prefer **OpenGL** unless your build reports Vulkan and your driver is stable.
- Lower **Internal resolution scale** first when frames are slow.
- Leave **Vertical sync** off unless you see tearing.
- The shell presents the latest available frame; a slow UI drops frames instead
  of building latency.

## If the picture is black or frozen

1. Check the **Activity** log for a renderer or launch error.
2. Confirm the core is running and the caption appeared.
3. Try the other presentation mode.
4. Try OpenGL and a scale of 1.
5. See [Troubleshooting](../reference/troubleshooting.md).
