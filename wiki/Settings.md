# Settings

The Settings page is a draft editor. Change values, then click **Save settings**
(or **Ctrl+S** while the page is open). Emulator settings apply on the next
power-on; session stop/close choices apply immediately. The FPS overlay toggle
applies immediately, and the presentation filter, frame-rate limit and frame-skip
choice apply live with in-process hosting.

## System

**Firmware**

- **BIOS image** — required. Retail Xbox BIOS/flash ROM (256 KB or 512 KB).
- **MCPX boot ROM** — 512-byte `mcpx_1.0.bin`. Empty looks beside the core.
- **EEPROM image** — 256-byte `eeprom.bin`. Empty looks beside the core.

**Storage**

- **Hard-disk image** — raw or qcow2. Boots the dashboard with no disc and holds
  save states.
- **Default disc image** — inserted when no game is selected.

**Sega Chihiro**

- **Enable Chihiro** — boot the arcade machine. A `.bin` netboot image also
  selects it for that launch.
- **Chihiro firmware folder** — the five required firmware files. Empty uses the
  folder beside the Xbox BIOS.
- Chihiro runs use 128 MB and no Xbox HDD. See [Chihiro](Chihiro) and
  [Chihiro controls](Chihiro-Controls).

**Session**

- **Power on when XGrid starts**.
- **Keep XGrid open after power off**.
- **Show the powered-off screen**.
- **Save state before power off** — writes `xgrid-session`. Needs in-process
  hosting.
- **Resume the saved session** — restores it from the Power button; launching a
  game from the Library starts fresh.

**Emulator**

- **Emulator binary** — optional explicit core path.
- **Guest memory** — 64 MB retail, 128 MB debug kit.
- **Core hosting** - **In-process (recommended, Windows)** enables save states,
  pause/step and disc swap, but hosts one machine per shell; **Separate process**
  can start a new machine every time and isolates a crash. Fast-forward works in
  both modes (ABI or the core window's speed message).

## Graphics

- **Renderer** — Direct3D 11 (Windows preview), OpenGL, Vulkan (if compiled),
  Null. Only what the core reports is offered.
- **Internal resolution scale** — 1, 2, 3, 4, 5, 6 or 8.
- **Texture filtering** — Linear or Nearest.
- **Window startup size**, **Vertical sync**, **Start fullscreen**,
  **Exclusive fullscreen**.
- **Frame-rate overlay** - present rate and frame time over the picture.
- **Frame-rate limit** - Unlimited or 30/60/90/120/144/240 fps. Presentation only;
  the guest clock is unchanged and fast-forward scales the limit.
- **Skip missed frames** - drop late presentations when a limit is set.
- **Aspect ratio** - Auto, Native, 4:3, 16:9.
- **Fit mode** — Scale, Center, Stretch.
- **AV pack** — HDTV, Composite, S-Video, SCART, VGA, RFU, None.
- **Presentation filter** — Nearest, Bilinear, Scanlines, CRT mask; applied by
  the core.

## Audio

- **Host audio backend** — Auto, DirectSound (Windows), Core Audio (macOS),
  native Linux/BSD backends, **Null (no output)** and **WAV capture**.
- **Output device** — DirectSound lists host devices; PulseAudio, PipeWire, ALSA,
  OSS, sndio and JACK accept an optional device/server name.
- **Guest DSP**, **DSP JIT**, **HRTF**, **Volume limit**, **Voice workers**.

## Input

Keyboard and gamepad bindings for players 1–4. Click a key button and press a
key; **Esc** cancels. Configure stick dead zones, trigger threshold, Y inversion
and stick swap. **Reset input defaults** restores the shipped map.

## Titles

Per-title overrides for guest memory (64/128/inherit), renderer, texture
filtering and presentation filter. Keyed by title ID when readable, otherwise by
image path. Unset values inherit the global configuration.

## Library

**Folders** are scanned by the Library. See [Game library](Game-Library).

**Cover art** controls the local covers folder, online catalog, automatic
downloads, catalog URL, fuzzy matching, annotation stripping and the fuzzy
threshold (0.70–0.95, default 0.85). Local covers keep working offline. The cache
lives in `<data>/covers-cache/`.

## Appearance

Choose Blue, Emerald, Aqua, Violet, Amber or Crimson. Save to persist.

## Files

`settings.json` lives in the per-user data folder. The core configuration is
generated fresh for every launch as `xemu/xemu.toml`.

Full guide: [Settings](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/settings.md).
