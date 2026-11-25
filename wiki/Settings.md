# Settings

The Settings page is a draft editor. Change values, then click **Save settings**
(or **Ctrl+S** while the page is open). Most changes apply on the next power-on.

## System

**Firmware**

- **BIOS image** — required. Retail Xbox BIOS/flash ROM (256 KB or 512 KB).
- **MCPX boot ROM** — 512-byte `mcpx_1.0.bin`.
- **EEPROM image** — 256-byte `eeprom.bin`.

**Storage**

- **Hard-disk image** — raw or qcow2. Boots the dashboard with no disc.
- **Default disc image** — inserted when no game is selected.

**Emulator**

- **Emulator binary** — optional explicit path.
- **Guest memory** — 64 MB retail, 128 MB debug kit.
- **Presentation filter** - applied by the core's final blit. In-process hosting can change it live; a separate process applies it on the next launch.
- **Core hosting** - **In-process (recommended, Windows)** enables save states, pause and disc swap but hosts one machine per shell; **Separate process** can start a new machine every time.

## Graphics

- **Renderer** — OpenGL, Vulkan (if compiled), Null.
- **Internal resolution scale** — 1–8 (the list offers 1–6 and 8).
- **Texture filtering** — Linear or Nearest.
- **Window startup size**, **Vertical sync**, **Start fullscreen**,
  **Exclusive fullscreen**.
- **Aspect ratio** — Auto, Native, 4:3, 16:9.
- **Fit mode** — Scale, Center, Stretch.
- **AV pack** — HDTV, Composite, S-Video, SCART, VGA, RFU, None.
- **Presentation filter** - Nearest, Bilinear, Scanlines, CRT mask; applied by the core.

## Audio

- **Host audio backend** — Auto, DirectSound (Windows), None, WAV capture.
- **Guest DSP** — required for game audio.
- **DSP JIT**, **HRTF**, **Volume limit**, **Voice workers**.

## Input

- Keyboard bindings; click a key button and press a key. Esc cancels.
- Gamepad bindings by physical source.
- Left/right stick radial dead zones.
- Trigger threshold.
- Invert left/right Y; swap sticks.

## Titles

Per-title overrides for guest memory (64/128/inherit), renderer, texture
filtering and presentation filter, keyed by title ID when readable, otherwise by
image path. Unset values inherit the global configuration.

## Library

Folders scanned by the Library. See [Game library](Game-Library).

## Appearance

Accent palette for the dark theme. Save to persist.

## Files

`settings.json` lives in the per-user data folder. The core configuration is
generated fresh for every launch as `xemu/xemu.toml`.
