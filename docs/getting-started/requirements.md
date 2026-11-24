# Requirements

This page lists what XGrid needs to run a game. For building XGrid itself, see
[Building from source](../development/building.md).

## Host system

| Item | Requirement |
| --- | --- |
| **Operating system** | Windows 10 or 11, x64 (fully supported) |
| **Linux / macOS** | The Avalonia shell targets these platforms, but the emulator core is currently packaged for Windows only. See [Compatibility](../reference/compatibility.md). |
| **CPU** | A modern x64 processor. The QEMU TCG core is CPU-intensive. |
| **GPU** | An OpenGL-capable GPU with current drivers. Vulkan is used only when the core build includes it. |
| **RAM** | 4 GB or more recommended; the guest itself uses 64 MB or 128 MB. |
| **Disk** | Space for the application, firmware, HDD image and your disc images. |

Release archives are **self-contained**: you do not need to install .NET. Source
builds need the .NET 11 SDK and the native toolchain described in
[Building from source](../development/building.md).

## Firmware and storage images

XGrid ships **no Microsoft firmware and no game data**. Supply your own legally
obtained files.

| Image | Typical file | Size | Purpose |
| --- | --- | --- | --- |
| **BIOS / flash ROM** | `Complex_4627.bin` | 256 KB or 512 KB | The retail Xbox BIOS. **Required.** |
| **MCPX boot ROM** | `mcpx_1.0.bin` | 512 bytes | The first-stage boot ROM. Required for the retail boot chain. |
| **EEPROM** | `eeprom.bin` | 256 bytes | Console settings and EEPROM state. |
| **Hard-disk image** | `xbox_hdd.qcow2` | varies | The Xbox HDD. Boots the dashboard when no disc is inserted. Raw and qcow2 images are supported. |
| **Disc image** | `.iso`, `.chd`, `.zar` | varies | The game disc. Optional if you only want the dashboard. |

> **Firmware compatibility matters.** Use images from a known-good Xbox setup.
> A mismatched BIOS, EEPROM or HDD image can prevent the console from booting.

When a firmware path is left empty, XGrid first looks beside the emulator for
`mcpx_1.0.bin`, `eeprom.bin` and `xbox_hdd.qcow2`. In a packaged release those
files are not included, so configure them explicitly in **Settings → System**.

## Disc images

| Format | Support | Notes |
| --- | --- | --- |
| **ISO / XISO** | Yes | Standard XDVDFS disc images. |
| **CHD** | Yes | Compressed MAME CHD; decoded on demand. |
| **ZArchive (`.zar`)** | Entry form | The archive must contain the disc as an `.iso` entry. File-tree archives are not supported yet. |
| **XBE** | Inspect only | A loose executable is not a disc; wrap it in an ISO/CHD to boot. |

See [Media formats](../guides/media.md) for the details.

## Controller

- **Keyboard** — always available; bindings are configurable.
- **XInput controller** — supported on Windows. Player 1 only.
- The shell's input mapping is used when **Present core frames in the shell** is
  enabled. Otherwise the core reads its own SDL input.

## Legal

- XGrid's own code is GPL-2.0-or-later.
- The emulator core is xemu 0.8.139 and QEMU, under their original licenses.
- You are responsible for the firmware and game images you use.

See [Licensing](../legal/licensing.md).
