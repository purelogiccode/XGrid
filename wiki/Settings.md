# Settings

The Settings page is a draft editor. Change values, then click **Save settings**
(or **Ctrl+S** while the page is open). The startup and emulator settings apply on
the next power-on; the session stop/close choices apply immediately.

## System

**Firmware**

- **BIOS image** — required. Retail Xbox BIOS/flash ROM (256 KB or 512 KB).
- **MCPX boot ROM** — 512-byte `mcpx_1.0.bin`.
- **EEPROM image** — 256-byte `eeprom.bin`.

**Storage**

- **Hard-disk image** — raw or qcow2. Boots the dashboard with no disc.
- **Default disc image** — inserted when no game is selected.

**Emulator**

- **Emulator binary** - optional explicit path.
- **Guest memory** - 64 MB retail, 128 MB debug kit.
- **Presentation filter** - applied by the core's final blit. In-process hosting can change it live; a separate process applies it on the next launch.
- **Core hosting** - **In-process (recommended, Windows)** enables save states, pause and disc swap but hosts one machine per shell; **Separate process** can start a new machine every time.

**Session**

- **Power on when XGrid starts** - boot the configured disc or dashboard when the window opens. A first run still asks for firmware first.
- **Keep XGrid open after power off** - leave the shell on the powered-off screen. Turn off to close XGrid after a normal shutdown.
- **Show the powered-off screen** - logo and power hint while off; turn off for a plain black picture area.
- **Save state before power off** - write the automatic `xgrid-session` snapshot before a Power off, Stop or window close. Needs in-process hosting.
- **Resume the saved session** - restore that snapshot when the Power button starts the dashboard. Launching a game from the Library starts it fresh.

## Graphics

- **Renderer** — Direct3D 11 (Windows builds, preview), OpenGL, Vulkan (if compiled), Null.
- **Internal resolution scale** — 1–8 (the list offers 1–6 and 8).
- **Texture filtering** — Linear or Nearest.
- **Window startup size**, **Vertical sync**, **Start fullscreen**,
  **Exclusive fullscreen**.
- **Aspect ratio** — Auto, Native, 4:3, 16:9.
- **Fit mode** — Scale, Center, Stretch.
- **AV pack** — HDTV, Composite, S-Video, SCART, VGA, RFU, None.
- **Presentation filter** - Nearest, Bilinear, Scanlines, CRT mask; applied by the core.

## Audio

- **Host audio backend** - the list is read from the core: Auto, DirectSound (Windows), native Linux/BSD backends when compiled, **Null (no output)** and WAV capture. Null discards audio; headless runs use it automatically.
- **Output device** - DirectSound shows host playback devices; PulseAudio, PipeWire, ALSA, OSS, sndio and JACK take an optional device/server name.
- **Guest DSP** - required for game audio.
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

**Folders** are scanned by the Library. See [Game library](Game-Library).

**Cover art** controls:

- local covers folder (empty uses `<data>/covers`);
- use the online R2 catalog;
- download missing covers automatically;
- catalog URL (full `catalog.json` or a base URL);
- fuzzy title matching and annotation stripping;
- fuzzy threshold (0.70-0.95, default 0.85);
- refresh catalog and clear the cache.

Matching checks the XBE title id, exact names, annotation-stripped names,
token/no-space forms and then fuzzy scores. Local covers keep working offline.
The cache lives in `<data>/covers-cache/`.

## Appearance

Accent palette for the dark theme. Save to persist.

## Files

`settings.json` lives in the per-user data folder. The core configuration is
generated fresh for every launch as `xemu/xemu.toml`.
