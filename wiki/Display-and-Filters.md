# Display and filters

The core owns the picture. It renders and presents with its own swapchain; the
Avalonia shell owns the window, layout and controls only.

## Windows: embedded core window

- The core creates its own window; XGrid embeds it, in-process or separate.
- The core paints straight into that child surface. There is no per-frame copy,
  upload or shell redraw.
- The embedded child does not take activation, so the shell pad input stays live.
- This is the fastest path.

## Linux and macOS

Native in-window embedding is Windows-only for now. The core opens its own SDL
window outside the Home page; use that window for the picture, filters and input.
Headless runs use the POSIX frame sink instead. See
[Headless and CLI](Headless).

## Filters

| Filter | Effect |
| --- | --- |
| Nearest | Sharp pixels. |
| Bilinear | Smooth upscale. |
| Scanlines | Every other row darkened. |
| CRT mask | Scanlines plus an RGB phosphor triad. |

Filters are implemented in the core's final blit shader; the shell only selects
one. `XGRID_FILTER`/`--filter` sets the launch choice. In-process hosting can
change it live through the core ABI.

## Core graphics settings

- Renderer: Direct3D 11 (Windows builds, preview), OpenGL, Vulkan (if compiled) or Null.
- Internal resolution scale 1–8.
- Texture filtering: linear or nearest.
- Aspect ratio: auto, native, 4:3, 16:9.
- Fit: scale, center, stretch.
- AV pack.
- Vertical sync.

## Fullscreen and screenshots

- **F11** toggles fullscreen; the toolbar's **Fullscreen** button enters it.
  The shell chrome is hidden and the console picture fills the screen.
- In fullscreen, **Esc** pauses a running game on the first press. Press **Esc**
  again to close XGrid. With no game running, Esc returns to the windowed shell.
- **F8** saves the console picture as a PNG in `<data>/screenshots/`. With
  in-process hosting the core writes the capture; with separate-process hosting
  the shell captures the whole window instead.
- The Library page shows a strip of recent captures; click one to open it,
  **Refresh** to re-read the folder.

## Pause and frame step

- **P** pauses on the next frame and keeps the last picture.
- **F10** advances exactly one frame.

Both need in-process core hosting, like save states. While paused, the status
bar reads **Paused**.

Press **Space** while a game runs, or click **4x** in the toolbar, to toggle
**4x fast-forward**; do it again to return to normal speed. The status bar reads
**Running 4x** while it is on. The core runs the guest clock four times faster,
so timers and the picture speed up together.

## Frame-rate overlay

Turn on **View → FPS Overlay** to draw the core's present rate and average frame
time over the picture. The core draws the overlay; the shell never touches a
frame. In-process hosting applies the toggle live; a separate process applies it
on the next launch.

## If the picture is black

1. Check **Activity**.
2. Wait a few seconds for the core to start.
3. On Linux or macOS, look for the core's own window.
4. Try OpenGL and scale 1.
5. See [Troubleshooting](Troubleshooting).
