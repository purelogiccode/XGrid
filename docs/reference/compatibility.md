# Compatibility

XGrid is alpha software. Its emulator is **xemu 0.8.139**, so game compatibility
broadly tracks that core, while XGrid adds real-time CHD/ZAR media and a new
shell. This page describes the supported surface, not a guarantee for every disc.
For core-level status, xemu's own compatibility database is a useful starting
point, but XGrid's patches and media drivers can change the result.

## Supported today

| Area | Status |
| --- | --- |
| Retail boot chain and dashboard from an HDD image | Supported |
| ISO/XISO disc images | Supported |
| Compressed CHD disc images | Supported (real-time decode) |
| ZArchive discs | Supported, entry form or synthesized file tree |
| 64 MB and 128 MB guest RAM | Supported |
| OpenGL renderer | Supported |
| Vulkan renderer | Only when the core build includes it |
| NULL/headless renderer | Supported, no picture |
| Keyboard input | Supported |
| XInput controllers, players 1–4 | Supported on Windows |
| Live host audio and WAV capture | Supported through the core |
| Four save-state slots | Supported with in-process hosting |
| Shell filters (nearest, bilinear, scanlines, CRT) | Supported with the frame sink |
| Per-title override (memory, renderer, filters) | Supported |
| Disc swap while running | Supported with in-process hosting |
| Whole-window screenshots | Supported |

## Known limitations

| Limitation | Detail |
| --- | --- |
| **Windows is the supported emulation host** | The core and display embedding are packaged for Windows x64. Linux/macOS archives contain the shell only. |
| **ZArchive file-tree archives** | Supported by the XDVDFS synthesizer; entry-form archives remain the simplest shape. |
| **Save states are in-process only** | The separate-process host has no ABI for snapshot control. |
| **Player 1 only** | The shell maps one controller. |
| **No disc swap while running** | Power off before launching another disc. |
| **No shell netplay UI** | The core may support netplay, but the shell does not expose it. |
| **No software or DirectX renderer** | xemu 0.8.139 offers OpenGL, Vulkan and NULL. The shell does not offer renderers the build cannot start. |
| **No firmware or games** | Everything copyrighted must come from the user. |
| **Alpha shell** | UI and settings may change between releases. |

## Renderer notes

- The build writes `xgrid-core.json` with the renderers it compiled.
- A build without Vulkan hides Vulkan in Settings and coerces a saved Vulkan
  choice to OpenGL.
- The NULL renderer runs the machine with no picture; use it for diagnostics.

## Media notes

- A loose `.xbe` is not bootable. Wrap it in an ISO or CHD.
- CHD and ZAR are read on demand; no extraction step is required.
- Keep the HDD image writable if you use save states.

## Compatibility expectations

Use the following as a starting point:

1. Prefer a known-good Xbox BIOS/EEPROM/HDD combination.
2. Prefer ISO for the first boot of a title, then try CHD once it works.
3. Check the Activity log for the first failure, not the last.
4. If a title fails in the core, it is likely an emulation issue shared with
   xemu 0.8.139 rather than a shell issue.

For practical checks, see [Troubleshooting](troubleshooting.md). For the media
drivers, see [Media formats](../guides/media.md).
