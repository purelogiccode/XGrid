# Display and filters

XGrid can present the core's picture in two ways.

## Shell frame sink (default)

**Settings → System → Present core frames in the shell** is on.

- The core publishes BGRA frames to a shared section.
- The shell presents the latest frame with pacing and filters.
- The core window is hidden.
- The shell pad is forwarded to the core.

## Embedded core window

Turn the setting off.

- The core creates its own window; XGrid embeds it.
- The core paints directly; the shell filter does **not** apply.
- The core reads its own SDL input.

## Linux and macOS

The shell frame sink and embedding are Windows-only. On Linux the core opens
its own SDL window outside the Home page; use it for the picture, filters and
input. The macOS archives contain the shell only and need an external xemu
build.

## Filters

| Filter | Effect |
| --- | --- |
| Nearest | Sharp pixels. |
| Bilinear | Smooth upscale. |
| Scanlines | Every other row darkened. |
| CRT mask | Scanlines plus an RGB phosphor triad. |

Filters are applied at source resolution by the shell before scaling. The masks
preserve alpha, and a frame whose size disagrees with its declared geometry is
left unfiltered.

## Core graphics settings

- Renderer: OpenGL, Vulkan (if compiled) or Null.
- Internal resolution scale 1–8.
- Texture filtering: linear or nearest.
- Aspect ratio: auto, native, 4:3, 16:9.
- Fit: scale, center, stretch.
- AV pack.
- Vertical sync.

## Fullscreen and screenshots

- **F11** toggles fullscreen.
- In fullscreen, **Esc** pauses a running game on the first press. Press **Esc**
  again to close XGrid. With no game running, Esc returns to the windowed shell.
- **F8** saves the whole window as a PNG in `<data>/screenshots/`.
- The Library page shows a strip of recent captures; click one to open it,
  **Refresh** to re-read the folder.

## Pause and frame step

- **P** pauses on the next frame and keeps the last picture, with a **Paused**
  badge over it.
- **F10** advances exactly one frame.

Both need in-process core hosting, like save states. While paused, the status
bar reads **Paused**.

Press **Space** while a game runs to toggle **4x fast-forward**; press it again
to return to normal speed. The status bar reads **Running 4x** while it is on.

## If the picture is black

1. Check **Activity**.
2. Wait a few seconds for the first frame.
3. Try the other presentation mode.
4. Try OpenGL and scale 1.
5. See [Troubleshooting](Troubleshooting).
