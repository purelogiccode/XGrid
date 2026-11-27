# Display and filters

The core owns the picture. It renders, filters and presents with its own
swapchain; the shell never copies, filters or redraws a frame.

## Presentation

- **Windows:** the core's window is reparented into the Home page, in-process or
  separate process. The core paints directly into the child surface.
- **Linux/macOS:** the core opens its own SDL window. Native in-window embedding
  is Windows-only for now.
- **Headless:** the core renders off-screen into a shared-memory display sink for
  frame counting. See [Headless](Headless).

## Presentation filters

| Filter | Effect |
| --- | --- |
| Nearest | Crisp pixels, cheapest. |
| Bilinear | Softer edges. |
| Scanlines | Every-other-row light reduction. |
| CRT mask | Scanlines plus an R/G/B phosphor triad. |

The filter is applied by the core at source resolution before scaling. It is
separate from the core's **Texture filtering** setting.

## Rendering settings

- **Renderer** — OpenGL, Vulkan (if compiled), Direct3D 11 (Windows preview) or
  Null.
- **Internal resolution scale** — 1–6 or 8×.
- **Texture filtering** — linear or nearest.
- **Aspect ratio** — auto, native, 4:3, 16:9.
- **Fit mode** — scale, center, stretch.
- **AV pack** — reported video output.
- **Vertical sync**, **Start fullscreen**, **Exclusive fullscreen**.
- **Frame-rate limit** — Unlimited or a fixed 30/60/90/120/144/240 fps cap on
  presentation. The guest clock is unchanged; fast-forward scales the limit.
- **Skip missed frames** — drop late presentations instead of showing a catch-up
  burst. Applies only with a limit.

A renderer the core was not built with is hidden; a saved choice falls back to
OpenGL.

## Fullscreen

**F11** toggles fullscreen. **Esc** pauses a running game; the next **Esc** closes
XGrid. With no game running, **Esc** leaves fullscreen.

## Screenshots and FPS overlay

**F8** saves a PNG to `<data>/screenshots/`. With in-process hosting the core
captures its own picture; otherwise the shell captures the whole window.

**Settings → Graphics → Frame-rate overlay** (also **View → FPS Overlay**) draws
present rate and average frame time. The same tab sets the presented frame-rate
limit and whether missed frames are skipped. In-process hosting applies them
live; a separate process applies them on the next launch.

## Performance

- Lower **Internal resolution scale** first.
- Prefer **OpenGL** unless another backend is stable for your titles.
- Leave **Vertical sync** off unless you see tearing.
- Keep filters and the overlay in the core.

If the picture is black, check **Activity**, confirm the core is running, and try
OpenGL at scale 1.

Full guide: [Display and filters](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/display.md).
