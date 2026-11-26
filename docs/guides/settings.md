# Settings

The Settings page is a draft editor: change values, then click **Save settings**
(or press **Ctrl+S** while the page is open). Most changes take effect on the
next power-on because the core is launched with a generated configuration.

Every control on every page has a tooltip. Hover a button, path box, slider,
toggle or tab header to read what it does before changing it.

## System

### Firmware

| Setting | Meaning |
| --- | --- |
| **BIOS image** | Retail Xbox BIOS/flash ROM. Required to power on. 256 KB or 512 KB. |
| **MCPX boot ROM** | 512-byte `mcpx_1.0.bin`. |
| **EEPROM image** | 256-byte `eeprom.bin`. |

XGrid ships no firmware. Configure these from files you own.

### Storage

| Setting | Meaning |
| --- | --- |
| **Hard-disk image** | Xbox HDD image, raw or qcow2. Boots the dashboard when no disc is inserted. |
| **Default disc image** | Inserted when the console powers on without a selected game. ISO, CHD or ZAR. |

### Emulator

| Setting | Meaning |
| --- | --- |
| **Emulator binary** | Optional explicit path to the core executable. Empty uses the packaged core, then the in-tree build. |
| **Guest memory** | 64 MB (retail) or 128 MB (debug kit). |
| **Core hosting** | **In-process (recommended)** loads the core library into the shell and enables save states, pause, frame step and disc swap. It hosts one machine per shell; later launches fall back to a separate process until XGrid restarts. **Separate process** can start a new machine every time and isolates a core crash. The in-process library is Windows-only. |

> Save states require the in-process core. See [Save states](save-states.md).
> On Linux the core always runs as a separate process with its own window.

## Graphics

### Core rendering

| Setting | Options | Meaning |
| --- | --- | --- |
| **Renderer** | Direct3D 11 (Windows, preview), OpenGL, Vulkan, Null | The core renderer. Direct3D 11 and Vulkan are offered only when the core build reports them; Null is headless. |
| **Internal resolution scale** | 1–8 (the list offers 1–6 and 8) | Renders the guest at a multiple of its native resolution. Higher values cost GPU time and memory. |
| **Texture filtering** | Linear, Nearest | Applied by the core to guest textures. |
| **Window startup size** | various | Used when the core starts in a window; the shell still embeds it. |
| **Vertical sync** | on/off | Waits for display refresh. Off is the low-latency default. |
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

### Performance

The core owns the picture and paints with its own swapchain into the embedded
window; the shell adds no per-frame copy, upload or redraw. Keep filters and the
FPS overlay in the core. See [Display and filters](display.md).

## Audio

| Setting | Meaning |
| --- | --- |
| **Host audio backend** | **Auto** lets the core choose; **DirectSound** on Windows; **None** disables host output; **WAV capture** writes `audio.wav` in the core working directory. |
| **Guest DSP** | Runs the APU's GP/EP microcode. Required for game audio. |
| **DSP JIT** | Native code generation for the DSP. Off keeps the interpreter. |
| **HRTF** | Head-related filtering for 3D voices. |
| **Volume limit** | Caps the mixed output; 100% is the core default. |
| **Voice workers** | Voice-processor worker count; 0 = auto. |

See [Audio](audio.md) for details and troubleshooting.

## Input

Player-1 keyboard and controller bindings.

- Click a key button, then press the new key. **Esc** cancels capture.
- Keyboard rows accept one or more comma-separated key names. `Space` is
  reserved for fast-forward, so it is never stored as a pad binding.
- Gamepad rows select a physical source (A, B, X, Y, D-pad, triggers, sticks,
  etc.).
- **Dead zones** are radial percentages applied before the report is published.
- **Trigger threshold** is the value below which a trigger reports released.
- **Invert left/right stick Y** and **Swap left and right sticks** adjust the
  report.

See [Input](input.md) for the default keyboard map and behavior.

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

Manage the folders scanned by the Library. See [Game library](library.md).

### Cover art

| Setting | Meaning |
| --- | --- |
| **Local covers folder** | Folder containing PNG, JPEG, WebP or BMP covers. Empty uses `<data>/covers`. |
| **Use the online cover catalog** | Resolve covers from the XGrid R2 catalog. Local covers keep working offline. |
| **Download missing covers automatically** | Fetch only covers matched to games in the library after a scan. |
| **Catalog URL** | Full `catalog.json` URL or a base URL. The default is the XGrid R2 catalog. |
| **Fuzzy title matching** | Use Jaro-Winkler scoring after the exact and annotation-stripped passes. |
| **Ignore region, language and revision annotations** | Strip `(USA)`, `(Europe)`, `(En,Fr,De)`, `Rev 1` and similar groups before matching. |
| **Fuzzy threshold** | 0.70-0.95. The default **0.85** prefers a missing cover over a wrong one. |
| **Refresh** | Fetch the catalog now and rescan covers. |
| **Clear cache** | Delete the cached catalog and downloaded images. |

Matching order is title id, exact name, annotation-stripped exact name,
token/no-space forms, a catalog title-id/exact match, local fuzzy, and catalog
fuzzy. The cover source and match confidence appear in the card tooltip.

## Appearance

Choose an accent palette for highlights and controls. The dark theme is shared by
all platforms. **Save settings** persists the choice.

## Saving

- **Save settings** writes `settings.json` in the data folder.
- **Ctrl+S** saves while the Settings page is open.
- Failed writes are reported in the Activity log.
- Settings apply on the next power-on; currently running cores keep their
  generated configuration.

See [Configuration](../reference/configuration.md) for file locations and formats.
