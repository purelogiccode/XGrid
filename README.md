<p align="center">
  <img src="docs/assets/xgrid-mark.svg" width="96" height="96" alt="XGrid">
</p>

<h1 align="center">XGrid</h1>

<p align="center">
  <strong>Xbox emulation, refined.</strong><br>
  An enhanced <a href="https://xemu.app">xemu</a> core with a modern Avalonia desktop shell.
</p>

<p align="center">
  <a href="https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml/badge.svg?branch=master"></a>
  <a href="https://github.com/purelogiccode/XGrid/releases"><img alt="Release" src="https://img.shields.io/github/v/release/purelogiccode/XGrid?display_name=tag&sort=semver"></a>
  <a href="COPYING"><img alt="License: GPL-2.0-or-later" src="https://img.shields.io/badge/license-GPL--2.0--or--later-blue.svg"></a>
  <img alt=".NET 11" src="https://img.shields.io/badge/.NET-11-512BD4">
  <img alt="Avalonia 12.2" src="https://img.shields.io/badge/Avalonia-12.2-8B44AC">
  <a href="xemu/XEMU_VERSION"><img alt="Core: xemu 0.8.139" src="https://img.shields.io/badge/core-xemu%200.8.139-2E7D32"></a>
  <img alt="Platform: Windows, Linux, macOS" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey">
  <img alt="Status: alpha" src="https://img.shields.io/badge/status-alpha-orange">
  <a href="docs/index.md"><img alt="Documentation" src="https://img.shields.io/badge/docs-multipage-3DAEE9"></a>
</p>

---

XGrid is a maintained **in-tree fork of xemu 0.8.139** with a first-class desktop
application around it. The emulator itself — QEMU TCG i386, the NV2A and its
renderers, MCPX, APU/ACI, IDE, USB and the rest of the Xbox — is the xemu core
tracked in [`xemu/`](xemu/). XGrid adds the Avalonia shell, real-time CHD/ZAR
media, save states, display and input plumbing, per-title settings, diagnostics
and packaging.

> XGrid is not affiliated with Microsoft or the xemu project. It ships no
> firmware, BIOS images, EEPROMs or game data. You must supply your own legally
> obtained files.

## Why XGrid?

- **A real desktop application.** Home, Library, Settings, Activity and About
  pages instead of a bare emulator window.
- **The core is the proven one.** xemu 0.8.139 with a small, documented patch
  set — not a re-implementation.
- **No extraction for compressed media.** CHD and ZArchive discs are decoded on
  demand by `xgridchd` and `xgridzar`; file-tree ZArchives are synthesized as
  XDVDFS volumes.
- **Save states that belong to the machine.** Four QEMU/xemu snapshot slots,
  saved through the in-process core ABI.
- **Display your way.** Shell frame sink with nearest, bilinear, scanline and CRT
  filters, or the core's own embedded window.
- **Input your way.** Keyboard and XInput mapping, dead zones, trigger threshold,
  Y inversion and stick swap for players 1–4.
- **Per-title overrides.** Guest memory, renderer and filters keyed by XBE title
  ID or image path.

## Feature summary

| Area | Support |
| --- | --- |
| **Core** | xemu 0.8.139 / QEMU 10.2 TCG i386, NV2A OpenGL/Vulkan/NULL, MCPX, APU/DSP, ACI, IDE/ATAPI, USB/OHCI, SMBus |
| **Media** | ISO/XISO, CHD, ZAR (entry form or file tree); raw/qcow2 HDD |
| **Hosting** | In-process core (`xgrid_core.dll`) by default; separate process fallback |
| **Display** | Shell frame sink or embedded core window; filters, resolution scale, aspect/fit, fullscreen |
| **Audio** | Core APU/DSP, Auto/DirectSound/None/WAV capture, HRTF, volume limit |
| **Input** | Player-1 keyboard and XInput, remapping, dead zones, trigger threshold |
| **Quality of life** | Four save-state slots, library scan, per-title overrides, screenshots, activity log, accent themes |
| **Platform** | Windows 10/11 x64 fully supported; shell targets Linux/macOS |

## Quick start

1. **Download** the [latest release](https://github.com/purelogiccode/XGrid/releases).
   The Windows x64 frontend archive includes the emulator under `core/xemu/`.
2. **Extract** it anywhere and run `XGrid.Frontend.Avalonia`.
3. Open **Settings → System** and set your **BIOS**, **MCPX ROM**, **EEPROM** and
   **HDD** image.
4. Add your game folders in **Settings → Library**.
5. Pick a title in **Library**, or press **Power on** to reach the dashboard.

Full walkthrough: [First boot](docs/getting-started/first-boot.md).

### Keyboard essentials

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `F6` | Quick save |
| `F7` | Quick load |
| `F8` | Screenshot the window |
| `F11` | Fullscreen |
| `Ctrl+O` | Open a disc |
| `Ctrl+S` | Save settings (Settings page) |

Default player-1 controls: `A`/`Space` = A, `S` = B, `D` = X, `F` = Y, arrows =
D-pad, `Enter` = Start, `Backspace` = Back, `Q`/`E` = White/Black, `Z`/`C` =
triggers.

## Architecture

```text
┌───────────────────────────────────────────────────────┐
│ Avalonia shell                                        │
│ Home · Library · Settings · Activity · About          │
│ settings → launch plan → generated xemu.toml          │
│ frame sink ← core        shell pad → input sink       │
│ save-state requests → core ABI                        │
└───────────────────────┬───────────────────────────────┘
                        │ in-process DLL or separate process
┌───────────────────────▼───────────────────────────────┐
│ xemu core (xemu 0.8.139 + XGrid additions)            │
│ TCG CPU · NV2A · MCPX · APU/ACI · IDE · USB · SMBus   │
└───────────────────────────────────────────────────────┘
```

- `xemu/xgrid-core/` — XGrid's native additions (media drivers, sinks, ABI).
- `xemu/xemu-patches/` — the record of upstream edits.
- `src/XGrid.Core.Xemu/` — launch planning, hosting, sinks, save-state wrapper.
- `src/XGrid.Frontend.Avalonia/` — the shell.
- Managed C# emulator projects — the validation oracle, not the product path.

Read [Architecture](docs/reference/architecture.md) for the details.

## Build from source

```powershell
# Shell and tests
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build

# Native core (Windows + MSYS2 MINGW64)
pwsh -File scripts/build-native-xemu.ps1

# Run
dotnet run --project src/XGrid.Frontend.Avalonia
```

The core build produces `xemu/dist/xemu.exe`, `xemu/dist/xgrid_core.dll` and the
runtime DLLs. See [Building from source](docs/development/building.md) for the
toolchain and [Quality gates](docs/development/quality-gates.md) for the checks.

## Release packages

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

The release tree contains self-contained archives for `win-x64`, `linux-x64`,
`osx-x64` and `osx-arm64`, checksums, a package manifest and the full license
set. The **Windows x64 frontend archive includes the emulator**; the Linux and
macOS archives currently contain the shell only.

## Documentation

- **Documentation site** — [`docs/index.md`](docs/index.md), served by
  [`docs/index.html`](docs/index.html) (Docsify; no build step).
- **Wiki** — [`wiki/Home.md`](wiki/Home.md). The `wiki/` folder is wiki-ready
  and can be published as the repository wiki.
- **Getting started** — [requirements](docs/getting-started/requirements.md),
  [installation](docs/getting-started/installation.md),
  [first boot](docs/getting-started/first-boot.md).
- **Guides** — [library](docs/guides/library.md), [settings](docs/guides/settings.md),
  [display](docs/guides/display.md), [input](docs/guides/input.md),
  [audio](docs/guides/audio.md), [save states](docs/guides/save-states.md),
  [media](docs/guides/media.md).
- **Reference** — [architecture](docs/reference/architecture.md),
  [configuration](docs/reference/configuration.md),
  [shortcuts](docs/reference/shortcuts.md),
  [compatibility](docs/reference/compatibility.md),
  [troubleshooting](docs/reference/troubleshooting.md), [FAQ](docs/reference/faq.md),
  [glossary](docs/reference/glossary.md).
- **Development** — [building](docs/development/building.md),
  [layout](docs/development/repository-layout.md),
  [core](docs/development/core.md),
  [quality gates](docs/development/quality-gates.md),
  [releasing](docs/development/releasing.md),
  [contributing](docs/development/contributing.md).
- **Legal** — [licensing](docs/legal/licensing.md),
  [porting](docs/legal/porting-policy.md),
  [release licensing](docs/legal/release-licensing.md),
  [source availability](docs/legal/source-availability.md).

Preview the documentation site locally with any static server:

```bash
python -m http.server 8000 -d docs
# open http://localhost:8000
```

## Project status

XGrid is **alpha software**. The core is mature, but the shell, media drivers and
compatibility work are still evolving. The Windows x64 release is the supported
emulation package today; the Linux/macOS shells are built and published, but the
core and display embedding are Windows-first.

Known limitations:

- Save states require in-process hosting.
- No netplay UI.
- No software or DirectX renderer; OpenGL (and Vulkan when compiled) only.

See [Compatibility](docs/reference/compatibility.md).

## License

XGrid's own source is **GPL-2.0-or-later**. The vendored xemu tree keeps its
original GPL-2.0, LGPL and MIT notices. Every third-party component and ported
module is recorded in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). See
[Licensing](docs/legal/licensing.md).

## Contributing and security

- [Contributing](docs/development/contributing.md)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)
- [`SECURITY.md`](SECURITY.md)

Do not commit firmware, BIOS images, EEPROMs, HDD images or game data.
