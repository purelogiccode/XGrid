# Platform embedding roadmap

The core owns the picture on every platform. The shell never draws, copies,
filters or overlays a frame. What changes per platform is only **how the core's
surface is attached to the Avalonia window**.

## Current status

| Target | Presentation | Status |
| --- | --- | --- |
| Windows x64 | Core window reparented into the console page (Win32 child HWND) | Implemented |
| Windows ARM64 | Same Win32 path | Code path ready; needs an ARM64 core build and `win-arm64` packaging |
| Linux x64/ARM64, X11 | Core window reparented with X11 (`XReparentWindow`) into the Avalonia native child | Not implemented; core opens its own window |
| Linux x64/ARM64, Wayland | Core creates a `wl_subsurface` under the Avalonia surface (or a DMA-BUF texture bridge) | Not implemented; core opens its own window |
| macOS Apple Silicon | Core's `NSView` added as a subview of the Avalonia host view | Not implemented; core opens its own window |

Until a platform backend lands, the core's SDL window is the picture at that
platform. This is a supported mode, not a fallback to the Avalonia canvas.

## Rules

- No platform may add a `WriteableBitmap`/`Image` frame canvas, a shell frame
  sink, or a per-frame Avalonia redraw. See `AGENTS.md` product rule 2.
- The core's filter and overlay are platform-independent and must stay in the
  core. Embedding must not take ownership of pixels.
- The display sink stays headless-only (frame counting for benchmarks).

## Work items

### Windows ARM64

1. Build the core with the MSYS2 `clangarm64` toolchain (or an ARM64 MSYS2
   environment) and publish `xemu/dist` for ARM64.
2. Add `win-arm64` to the packaging matrix.
3. Verify the in-process DLL ABI and the child-window reparent path on ARM64.

### Linux X11

1. Replace the Windows-only `XemuDisplaySurface.Create` with a platform host:
   an X11 child window created on Avalonia's native parent handle.
2. Implement `XemuWindowEmbedder` for X11 using `XReparentWindow`, resize and
   map/unmap calls through `libX11`.
3. Keep the input sink and the `WS_EX_NOACTIVATE` equivalent (X11 focus handling)
   so the shell keeps keyboard focus.

### Linux Wayland

A foreign `wl_surface` cannot be reparented into another client. One of these is
required:

- **Subsurface** — the core creates its SDL window with a Wayland subsurface role
  under the shell's `wl_surface`. This needs the shell to pass its `wl_display`
  and `wl_surface` (or a file descriptor) to the core and needs SDL3 support for
  the subsurface role.
- **Texture bridge** — the core exports DMA-BUF frames and the shell composites
  them with a native draw operation. Avalonia has no public DMA-BUF surface API,
  so this means a custom platform integration.

The subsurface route keeps the core in charge of presentation and is the
recommended path.

### macOS Apple Silicon

1. Expose the Avalonia host `NSView` through `NativeControlHost`.
2. Add the core's SDL `NSView` as a subview, with autoresizing or explicit frame
   updates from the shell's layout.
3. Keep keyboard focus on the shell and retain the pad sink.

## Verification

Each backend must pass the same acceptance checks:

- The game picture updates with no Avalonia invalidation between frames.
- The shell CPU usage stays flat while the core presents.
- Resize, fullscreen and page navigation do not destroy the core surface.
- Filters and the FPS overlay are visible and selectable from the shell.
- Screenshots include the native picture on that platform.

## Related pages

- [Architecture](../reference/architecture.md) — picture ownership and hosting.
- [Display and filters](../guides/display.md) — current user-facing behavior.
