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

## Filters

| Filter | Effect |
| --- | --- |
| Nearest | Sharp pixels. |
| Bilinear | Smooth upscale. |
| Scanlines | Every other row darkened. |
| CRT mask | Scanlines plus an RGB phosphor triad. |

Filters are applied at source resolution by the shell.

## Core graphics settings

- Renderer: OpenGL, Vulkan (if compiled) or Null.
- Internal resolution scale 1–8.
- Texture filtering: linear or nearest.
- Aspect ratio: auto, native, 4:3, 16:9.
- Fit: scale, center, stretch.
- AV pack.
- Vertical sync.

## Fullscreen and screenshots

- **F11** toggles fullscreen; **Esc** leaves it.
- **F8** saves the whole window as a PNG in `<data>/screenshots/`.

## If the picture is black

1. Check **Activity**.
2. Wait a few seconds for the first frame.
3. Try the other presentation mode.
4. Try OpenGL and scale 1.
5. See [Troubleshooting](Troubleshooting).
