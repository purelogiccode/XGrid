# Requirements

This page lists what XGrid needs to run. For building XGrid itself, see
[Building from source](../development/building.md).

## Host system

| Item | Requirement |
| --- | --- |
| **Windows** | Windows 10 or 11, x64. This is the fully integrated target. |
| **Linux** | Linux x64. The frontend archive bundles the standalone core, which presents in its own SDL window. Shell save states, pause/step, fast-forward and live disc swap need the Windows in-process core. |
| **macOS** | The archives contain the shell only. Point it at an upstream xemu 0.8.139 build with **Settings → System → Emulator binary** or `XGRID_XEMU_CORE`. |
| **CPU** | A modern x64 processor. QEMU TCG is CPU-intensive; faster single-core performance matters most. |
| **GPU** | An OpenGL-capable GPU with current drivers. Vulkan is offered only when the core build includes it; Direct3D 11 is a Windows preview backend. |
| **RAM** | 4 GB or more recommended. The emulated Xbox uses 64 MB (retail) or 128 MB (debug kit). |
| **Disk** | Space for the application, firmware, HDD image, cover cache and your disc images. Save states live inside the HDD image and need free space there. |

Release archives are **self-contained**. You do not need to install .NET to run
them. Building from source needs the .NET 11 SDK and the native toolchain
described in [Building from source](../development/building.md).

## Firmware and storage images

XGrid ships **no Microsoft firmware and no game data**. Supply your own legally
obtained files.

| Image | Typical file | Size | Purpose |
| --- | --- | --- | --- |
| **BIOS / flash ROM** | `Complex_4627.bin` | 256 KB or 512 KB | The retail Xbox BIOS. **Required.** |
| **MCPX boot ROM** | `mcpx_1.0.bin` | 512 bytes | First-stage boot ROM for the retail boot chain. |
| **EEPROM** | `eeprom.bin` | 256 bytes | Console settings and EEPROM state. |
| **Hard-disk image** | `xbox_hdd.qcow2` | varies | Dashboard, game saves and save-state storage. Raw and qcow2 are supported. |
| **Disc image** | `.iso`, `.chd`, `.cci`, `.zar` | varies | The game disc. Optional if you only want the dashboard. |
| **Chihiro firmware** | `chihiro_xbox_bios.bin` and four board files | varies | Optional. Required for Sega Chihiro netboot `.bin` images. |

For a normal retail launch, XGrid validates that the MCPX ROM, BIOS, EEPROM and
HDD paths resolve to files. If a path is left empty, XGrid first tries the file
beside the emulator (`mcpx_1.0.bin`, `eeprom.bin`, `xbox_hdd.qcow2`). Packaged
releases do not include them, so configure them explicitly in
**Settings → System** during the first-run wizard.

> **Firmware compatibility matters.** Use a matched, known-good Xbox firmware
> and HDD set. A mismatched BIOS, EEPROM or HDD image can prevent the console
> from booting, produce the wrong dashboard language, or fail to mount saves.

## Disc images

| Format | Support | Notes |
| --- | --- | --- |
| **ISO / XISO** | Yes | Optimized XISO or full Redump; the game partition is mapped automatically. |
| **CHD** | Yes | Compressed MAME CHD; decoded on demand. |
| **CCI** | Yes | Single or split Cerbios/Repackinator image; decoded on demand. |
| **ZArchive (`.zar`)** | Yes | Entry form or file-tree form. |
| **Chihiro `.bin`** | Yes | Sega Chihiro netboot image; not an Xbox disc. |
| **XBE** | Inspect only | A loose executable is not bootable on its own. |

See [Media formats](../guides/media.md).

## Controller

- **Keyboard** — always available; bindings are configurable.
- **XInput controllers** — supported on Windows for players 1–4. The shell
  publishes all four ports to the core when in-process input is available.
- **Linux and macOS** — the core reads its own SDL input in its window. The
  shell's XInput mapping is a Windows integration.

A controller is optional. The default keyboard map is enough to reach the
dashboard and play.

## Network

XGrid works offline. The optional network features are the update check, the
cover catalog, usage statistics, bug reports and donation verification. Every
one is best-effort and can be disabled. See
[Privacy and data](../legal/privacy.md) and
[Service endpoints](../reference/service-endpoints.md).

## Legal

- XGrid's own code is GPL-2.0-or-later.
- The core began from xemu 0.8.139 and QEMU and keeps their original notices; XGrid's core changes are GPL-2.0-or-later.
- You are responsible for the firmware and game images you use. The GPL does not
  grant rights to Microsoft's firmware or game content.

See [Licensing](../legal/licensing.md).

## Next step

Continue to [Installation](installation.md).
