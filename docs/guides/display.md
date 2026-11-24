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
  page.
- The core paints with its own swapchain, so the shell adds no per-frame copy.
- The shell presentation filter does not apply.
- The shell pad is not forwarded; the core reads its own SDL input.

> Both modes require the patched core. A stock xemu executable has neither the
> display sink nor the input sink.

## Shell presentation filters

| Filter | Effect |
| --- | --- |
| **Nearest (sharp)** | Nearest-neighbor sampling. Crisp pixels, cheapest. |
| **Bilinear (smooth)** | Bilinear upscale. Softer edges. |
| **Scanlines** | Nearest sampling plus an every-other-row light reduction. |
| **CRT mask** | Scanlines plus a repeating R/G/B phosphor triad. |

Filters are applied at the source resolution by the shell before scaling, so the
mask stays stable at any window size. The filter is a shell setting; the core's
**Texture filtering** setting is separate.

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
- **Esc** leaves fullscreen.

The shell fullscreen mode is separate from the core's **Start fullscreen**
setting. Use F11 for normal desktop use.

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
