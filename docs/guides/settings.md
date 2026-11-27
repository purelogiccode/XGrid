# Settings

The Settings page is a draft editor: change values, then click **Save settings**
(or press `Ctrl+S` while the page is open). Emulator settings take effect on the
next power-on; session choices that act on power-off or window close apply
immediately.

Every control has a tooltip. Hover a button, path box, slider or tab header to
read what it does before changing it.

## System

### Firmware

| Setting | Meaning |
| --- | --- |
| **BIOS image** | Retail Xbox BIOS/flash ROM. Required to power on. 256 KB or 512 KB. |
| **MCPX boot ROM** | 512-byte `mcpx_1.0.bin`. Empty looks beside the core. |
| **EEPROM image** | 256-byte `eeprom.bin`. Empty looks beside the core. |

XGrid ships no firmware. Configure these from files you own. A missing file is
reported when you power on, not silently replaced.

### Storage

| Setting | Meaning |
| --- | --- |
| **Hard-disk image** | Xbox HDD image, raw or qcow2. Boots the dashboard when no disc is inserted and stores save states. |
| **Default disc image** | Inserted when the console powers on without a selected game. ISO/XISO, CHD, CCI or ZAR. |

### Sega Chihiro

| Setting | Meaning |
| --- | --- |
| **Enable Chihiro** | Boot the Chihiro machine instead of the Xbox. A `.bin` netboot image also selects it for that launch. |
| **Chihiro firmware folder** | Folder with the Chihiro BIOS, media-board flash and EEPROM files. Empty uses the folder beside the Xbox BIOS. |
| **Cabinet** | Free play, region, DIMM size, media-board type and force feedback. |
| **Separate gun pointers** | Read each light gun or mouse independently. |
| **Card files** | Card image per reader; empty creates a blank card on first insert. |
| **Convert ISO / Convert GD-ROM** | Build a netboot `.bin` from a supported source. |
| **Linked cabinets** | Relay the Chihiro network board between instances. |

See [Sega Chihiro](chihiro.md) and [Chihiro controls](chihiro-controls.md).

### Session

| Setting | Meaning |
| --- | --- |
| **Power on when XGrid starts** | Boot the configured disc or dashboard as soon as the window opens. A first run still asks for firmware first; a command-line autostart wins. |
| **Keep XGrid open after power off** | Leave the shell on the powered-off screen after a normal shutdown. Turn off to close XGrid after power off. A faulted core stays open so the error is visible. |
| **Show the powered-off screen** | Show the XGrid logo and power hint while the console is off. Turn off for a plain black picture area. |
| **Save state before power off** | Write the automatic `xgrid-session` snapshot before Power off, Stop or window close. Needs in-process hosting. |
| **Resume the saved session** | Restore that snapshot when the Power button starts the dashboard. Launching a game from the Library starts it fresh. |

### Emulator

| Setting | Meaning |
| --- | --- |
| **Emulator binary** | Optional explicit path to the core executable. Empty uses the packaged core under `core/xemu/`, then the in-tree build. |
| **Guest memory** | 64 MB (retail) or 128 MB (debug kit). A Chihiro launch is forced to 128 MB. |
| **Core hosting** | **In-process (recommended)** loads the core library into the shell and enables save states, pause, frame step, disc swap and core-side screenshots. It hosts one machine per shell; later launches fall back to a separate process until XGrid restarts. **Separate process** can start a new machine every time and isolates a core crash; fast-forward still works through the core window. The in-process library is Windows-only. |

> Save states and the live playback controls require in-process hosting. On
> Linux the core always runs as a separate process with its own window.

## Graphics

### Core rendering

| Setting | Options | Meaning |
| --- | --- | --- |
| **Renderer** | Direct3D 11 (Windows preview), OpenGL, Vulkan, Null | The core renderer. Unsupported backends are not offered; a saved choice the build cannot start falls back to OpenGL. |
| **Internal resolution scale** | 1, 2, 3, 4, 5, 6, 8 | Renders the guest at a multiple of its native resolution. Higher values cost GPU time and memory. |
| **Texture filtering** | Linear, Nearest | Applied by the core to guest textures. |
| **Window startup size** | various | Used when the core starts in a window; the shell still embeds it. |
| **Vertical sync** | on/off | Waits for display refresh. Off is the low-latency default. |
| **Frame-rate overlay** | on/off | Draws the present rate and average frame time over the picture. |
| **Frame-rate limit** | Unlimited, 30, 60, 90, 120, 144, 240 fps | Caps presented frames without changing the guest clock. Unlimited is the historical pace; 60 is a fixed real-time target. Fast-forward scales the chosen limit. |
| **Skip missed frames** | on/off | Drops late presentations instead of showing a catch-up burst. Applies only with a frame-rate limit. |
| **Start fullscreen** | on/off | The embedded core follows the core's own mode. |
| **Exclusive fullscreen** | on/off | Only used when fullscreen starts. |
| **Aspect ratio** | Auto, Native, 4:3, 16:9 | How the core fits the picture inside its window. |
| **Fit mode** | Scale, Center, Stretch | Scale preserves aspect; stretch fills the window. |
| **AV pack** | HDTV, Composite, S-Video, SCART, VGA, RFU, None | The video output the console reports to the guest. |

### Presentation filter

| Setting | Options | Meaning |
| --- | --- | --- |
| **Presentation filter** | Nearest, Bilinear, Scanlines, CRT mask | Applied by the core's final blit shader. |

The presentation filter is independent of the core's texture filtering and is
implemented in the core. See [Display and filters](display.md).

## Audio

| Setting | Meaning |
| --- | --- |
| **Host audio backend** | The list is read from the core and includes **Auto (recommended)**, platform backends such as **DirectSound**, **Core Audio**, **PulseAudio**, **ALSA**, **JACK**, **PipeWire**, **OSS**, **sndio** and **SDL**, plus **Null (no output)** and **WAV capture**. |
| **Output device** | DirectSound shows host playback devices with **Refresh**. PulseAudio, PipeWire, ALSA, OSS, sndio and JACK accept an optional device or server name. Other backends use the system default. |
| **Guest DSP** | Runs the APU's GP/EP microcode. Required for game audio. |
| **DSP JIT** | Native code generation for the DSP. Off keeps the interpreter. |
| **HRTF** | Head-related filtering for 3D voices. |
| **Volume limit** | Caps the mixed output; 100% is the core default. |
| **Voice workers** | Voice-processor worker count; 0 = auto, up to 16. |

The WAV backend writes `audio.wav` in the core working directory
(`<data>/xemu/`). Null discards host audio and is used automatically by headless
runs. See [Audio](audio.md).

## Input

Player-1 keyboard and controller bindings for players 1–4.

- Click a key button, then press the new key. **Esc** cancels capture.
- Keyboard rows accept one or more comma-separated key names. `Space` is
  reserved for fast-forward, so it is never stored as a pad binding.
- Gamepad rows select a physical source (A, B, X, Y, D-pad, triggers, sticks,
  etc.).
- **Players** assigns one XInput slot to each Xbox port. **No gamepad** leaves a
  port empty.
- **Dead zones** are radial percentages applied before the report is published.
- **Trigger threshold** is the value below which a trigger reports released.
- **Invert left/right stick Y** and **Swap left and right sticks** adjust the
  report.
- **Reset input defaults** restores the shipped map; save the settings to apply
  it.

See [Controllers and input](input.md).

## Titles

Per-title overrides:

- Click **Add override** and choose a disc or XBE image.
- XGrid keys the override by the XBE certificate title ID when possible,
  otherwise by the image path.
- Each row sets guest memory (64, 128 or inherit), renderer, texture filtering
  and the presentation filter. Unset values inherit the global configuration.
- The renderer list follows the selected core's build.
- Remove a row with its remove button.

Overrides apply when the title is launched from the Library or **Open Disc**.

## Library

### Folders

Manage the folders scanned by the Library. See
[Game library](library.md).

### Cover art

| Setting | Meaning |
| --- | --- |
| **Local covers folder** | Folder containing PNG, JPEG, WebP or BMP covers. Empty uses `<data>/covers`. |
| **Use the online cover catalog** | Resolve covers from the XGrid catalog. Local covers keep working offline. |
| **Download missing covers automatically** | Fetch only covers matched to games in the library after a scan. |
| **Catalog URL** | Full `catalog.json` URL or a base URL. The default is the XGrid catalog. |
| **Fuzzy title matching** | Use Jaro-Winkler scoring after the exact and annotation-stripped passes. |
| **Ignore region, language and revision annotations** | Strip `(USA)`, `(Europe)`, `(En,Fr,De)`, `Rev 1` and similar groups before matching. |
| **Fuzzy threshold** | 0.70–0.95. The default **0.85** prefers a missing cover over a wrong one. |
| **Refresh** | Fetch the catalog now and rescan covers. |
| **Clear cache** | Delete the cached catalog and downloaded images. |

Matching order is title ID, exact name, annotation-stripped exact name,
token/no-space forms, a catalog title-ID/exact match, local fuzzy, and catalog
fuzzy. The cover source and match confidence appear in the card tooltip.

## Appearance

Choose an accent palette for highlights and controls: **Blue**, **Emerald**,
**Aqua**, **Violet**, **Amber** or **Crimson**. The dark theme is shared by all
platforms. **Save settings** persists the choice.

## Saving and repairing settings

- **Save settings** writes `settings.json` in the data folder.
- `Ctrl+S` saves while the Settings page is open.
- Failed writes are reported in the Activity log.
- Settings apply on the next power-on; a running core keeps its generated
  configuration. The presentation filter, FPS overlay, frame-rate limit and
  frame-skip choice are the exceptions: in-process hosting applies them live, and
  a separate core process picks them up on the next launch.
- Invalid values are repaired on load: RAM is 64 or 128, scale is clamped to
  1–8, unknown renderers fall back to OpenGL, unknown filters fall back to
  nearest, and empty override rows are dropped.
- A corrupt settings file is moved to `settings.json.bak` and defaults are used.

See [Configuration](../reference/configuration.md) for file locations and the
generated core configuration.
