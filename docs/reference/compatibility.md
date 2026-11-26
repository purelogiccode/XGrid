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
| ISO/XISO disc images | Supported, optimized or full Redump (game partition mapped automatically) |
| Compressed CHD disc images | Supported (real-time decode, optimized or full Redump) |
| Compressed CCI disc images | Supported (single part or split set, real-time LZ4 decode) |
| ZArchive discs | Supported, entry form or synthesized file tree |
| 64 MB and 128 MB guest RAM | Supported |
| OpenGL renderer | Supported |
| Vulkan renderer | Only when the core build includes it |
| NULL/headless renderer | Supported, no picture |
| Keyboard input | Supported |
| XInput controllers, players 1–4 | Supported on Windows |
| Live host audio and WAV capture | Supported through the core |
| Audio backend and device selection | Supported; the list comes from the core and DirectSound devices are enumerated |
| Null audio sink for headless runs | Supported (automatic) |
| Sega Chihiro netboot `.bin` images | Supported with the Chihiro machine; Chihiro firmware and a FATX netboot image are required |
| Chihiro cabinet controls | Supported with the default keyboard map and a basic gamepad map |
| Four save-state slots | Supported with in-process hosting |
| Automatic session save/resume | Supported with in-process hosting; one `xgrid-session` snapshot |
| Powered-off screen and stay-open mode | Supported |
| Power on when XGrid starts | Supported |
| Core filters (nearest, bilinear, scanlines, CRT) | Supported in the core blit |
| Core FPS overlay | Supported, in-process live; next launch for a separate process |
| Per-title override (memory, renderer, filters) | Supported |
| Disc swap while running | Supported with in-process hosting |
| Console screenshots | Supported core-side with in-process hosting; whole-window fallback otherwise |
| Headless runs and CLI overrides | Supported with the XGrid core |
| Linux core from the tracked tree | Supported as a separate process with its own window |
| macOS core | Bring your own upstream xemu 0.8.139 build |

## Known limitations

| Limitation | Detail |
| --- | --- |
| **Windows is the integrated emulation host** | In-process hosting, the shell frame/pad sinks, save states, pause/step, disc swap and window embedding are Windows-only. The `linux-x64` archive bundles the standalone core, which presents and reads input through its own window; headless runs use the shared-memory sink there too. macOS archives contain the shell only. |
| **ZArchive file-tree names** | The synthesizer uses XDVDFS rules: Latin-1, up to 42 characters, no separators. Entry-form archives have no such limit. |
| **Save states are in-process only** | The separate-process host has no ABI for snapshot control, so manual slots and automatic session save/resume require in-process hosting. |
| **Pause, frame stepping and disc swap need in-process hosting** | On Linux, power off before changing a disc. |
| **No shell netplay UI** | The core may support netplay, but the shell does not expose it. |
| **Chihiro GD-ROMs need conversion** | The ported machine boots FATX netboot `.bin` images. Xbox-format `XBOXOG - Chihiro` ISOs convert in Settings or with `XGrid.Cli chihiro-netboot`; Redump cue/bin sets and MAME `.chd` files convert in Settings or with `XGrid.Cli chihiro-redump` and the game's MAME key ZIP. Direct GD-ROM emulation is not available. |
| **Chihiro network titles need the network firmware** | Put `ver1305.bin` in the firmware folder. MJ2, MJ3, MJ3 Evolution and OutRun 2 SP check its version; the MJ titles then wait for their cabinet network, which the shell does not expose yet. |
| **Chihiro audio is incomplete** | Upstream xemu's Chihiro audio emulation can produce silence or missing effects. The WAV capture and the Xbox audio path work; the Chihiro APU stream is a known upstream limitation. |
| **Chihiro wheels, light guns and force feedback** | The core supports them in its wider fork. The shell maps the first gamepad, aims player 1's gun with the host pointer and player 2's with the second gamepad's right stick or the arrow keys, reads separate light guns or mice on Windows, uses both card readers with configurable card files, links two to four cabinets over TCP, edits the common/driving/gun/player-2/Gundam keys, and drives a force-feedback wheel from the drive boards (gamepad rumble when no wheel is found). Separate light-gun aiming on Linux and macOS is a follow-up. |
| **Direct3D 11 renderer is a preview** | The native Direct3D 11 NV2A backend renders the boot chain, menus and gameplay draws through HLSL shaders, a surface/zeta cache, occlusion queries and a texture cache. Remaining differences are the CPU presentation readback (no D3D11↔GL interop yet), line smoothing and some texture/filter edge cases; `ImplementDirectX.md` tracks them. Windows builds report `D3D11` only when the backend is compiled. OpenGL remains the safe default. |
| **No firmware or games** | Everything copyrighted must come from the user. |
| **Alpha shell** | UI and settings may change between releases. |

## Renderer notes

- The build writes `xgrid-core.json` with the renderers it compiled.
- A build without Vulkan hides Vulkan in Settings and coerces a saved Vulkan
  choice to OpenGL.
- Direct3D 11 is Windows-only and is listed only when the native backend is
  compiled. `ImplementDirectX.md` records the current phase. A saved `D3D11`
  choice on another build is coerced to OpenGL.
- The NULL renderer runs the machine with no picture; use it for diagnostics.

## Media notes

- A loose `.xbe` is not bootable. Wrap it in an ISO or CHD.
- CHD and ZAR are read on demand; no extraction step is required.
- Keep the HDD image writable if you use save states. Chihiro launches create
  `chihiro_snapshots.qcow2` for their snapshots instead.

## Compatibility expectations

Use the following as a starting point:

1. Prefer a known-good Xbox BIOS/EEPROM/HDD combination.
2. Prefer ISO for the first boot of a title, then try CHD once it works.
3. Check the Activity log for the first failure, not the last.
4. If a title fails in the core, it is likely an emulation issue shared with
   xemu 0.8.139 rather than a shell issue.

For practical checks, see [Troubleshooting](troubleshooting.md). For the media
drivers, see [Media formats](../guides/media.md).
