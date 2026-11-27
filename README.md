<p align="center">
  <img src="assets/banner.jpg" width="100%" alt="XGrid — Original Xbox and Chihiro emulator">
</p>

<h1 align="center">XGrid</h1>

<p align="center">
  <strong>Original Xbox emulation, with a desktop shell that feels at home.</strong><br>
  A custom enhanced and patched emulator core, originally derived from
  <a href="https://xemu.app">xemu 0.8.139</a>, with a modern Avalonia application.
</p>

<p align="center">
  <a href="https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml/badge.svg?branch=master"></a>
  <a href="docs/release-notes/v0.1.0.md"><img alt="Version: 0.1.0 alpha" src="https://img.shields.io/badge/version-0.1.0-blue"></a>
  <a href="COPYING"><img alt="License: GPL-2.0-or-later" src="https://img.shields.io/badge/license-GPL--2.0--or--later-blue.svg"></a>
  <img alt="Status: alpha" src="https://img.shields.io/badge/status-alpha-orange">
</p>

<p align="center">
  <img alt=".NET 11" src="https://img.shields.io/badge/.NET-11-512BD4">
  <img alt="Avalonia 12.2" src="https://img.shields.io/badge/Avalonia-12.2-8B44AC">
  <a href="xemu/XEMU_VERSION"><img alt="Core: XGrid custom / xemu 0.8.139 lineage" src="https://img.shields.io/badge/core-XGrid%20custom%20%2F%20xemu%200.8.139%20lineage-2E7D32"></a>
  <img alt="Platforms: Windows, Linux, macOS" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey">
</p>

<p align="center">
  <a href="https://github.com/purelogiccode/XGrid/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/purelogiccode/XGrid?style=flat"></a>
  <a href="https://github.com/purelogiccode/XGrid/network/members"><img alt="GitHub forks" src="https://img.shields.io/github/forks/purelogiccode/XGrid?style=flat"></a>
  <a href="https://github.com/purelogiccode/XGrid/issues"><img alt="Open issues" src="https://img.shields.io/github/issues/purelogiccode/XGrid"></a>
  <a href="docs/index.md"><img alt="Documentation" src="https://img.shields.io/badge/docs-read%20the%20manual-3DAEE9"></a>
</p>

---

XGrid is a desktop Xbox emulator built around a **custom, enhanced, and patched
core**. The core began as **xemu 0.8.139**, but it now lives in this repository
as part of XGrid and **will evolve separately from the original xemu codebase**.
It is not a stock xemu build, and future XGrid core changes are not tied to
upstream xemu releases.

The emulator itself — QEMU TCG, the NV2A and its renderers, MCPX, the APU/ACI,
IDE, USB and the rest of the Xbox — started from xemu and QEMU. XGrid adds the
library, settings, save states, real-time compressed media, per-title
configuration, diagnostics and packaging that turn a core into a comfortable
application.

> **XGrid is not affiliated with Microsoft or the xemu project.** It ships no
> BIOS, EEPROM, hard-disk image or game data. You must supply your own legally
> obtained files.

**Contents:** [Why XGrid?](#why-xgrid) · [At a glance](#at-a-glance) ·
[The XGrid core](#the-xgrid-core) · [What you need](#what-you-need) ·
[Quick start](#quick-start) ·
[Keyboard](#keyboard-essentials) · [Media](#supported-media) ·
[Platforms](#platform-support) · [Documentation](#documentation) ·
[Build](#build-from-source) · [Credits](#credits) · [License](#license)

## Why XGrid?

- **A custom core, not a wrapper.** XGrid owns a patched and enhanced core that
  began from xemu 0.8.139. It is maintained here and will diverge as the project
  grows.
- **A real desktop application.** Home, Library, Settings, Activity and About
  pages, folder scanning, search, favorites, play time and cover art.
- **Play compressed media directly.** ISO/XISO, CHD, CCI and ZArchive discs are
  read on demand. There is no extraction or conversion step.
- **Retail images just work.** Optimized XISOs and full Redump images both boot;
  the game partition is detected and mapped to LBA 0 automatically.
- **Save states that belong to the machine.** Four QEMU/xemu slots plus an
  optional automatic session snapshot that resumes from the Power button.
- **Arcade support.** Sega Chihiro netboot images with cabinet settings, linked
  cabinets, card readers, light guns and force feedback.
- **A picture the core owns.** The emulator renders, filters and overlays every
  frame. The shell never copies, filters or redraws the console picture.
- **Per-title settings.** Guest memory, renderer, texture filtering and the
  presentation filter can differ for each game.
- **Built for tinkering.** Command-line overrides, headless runs, JSON reports,
  rolling logs and a searchable Activity page.

## At a glance

| Area | What you get |
| --- | --- |
| **Core** | XGrid custom core, originally xemu 0.8.139 / QEMU 10.2 TCG i386; NV2A OpenGL/Vulkan/Direct3D 11 preview/NULL, MCPX, APU/DSP, ACI, IDE/ATAPI, USB/OHCI, SMBus |
| **Shell** | Avalonia 12.2 desktop app: Home, Library, Settings, Activity, About |
| **Hosting** | In-process `xgrid_core.dll` on Windows; separate process on Linux and as the Windows fallback |
| **Media** | ISO/XISO, CHD, CCI, ZArchive (entry form or file tree), Chihiro netboot `.bin`, raw/qcow2 HDD |
| **Arcade** | Sega Chihiro media board, DIMM, JVS, card readers, force feedback, two-to-four linked cabinets |
| **Display** | Core-owned picture (embedded window on Windows, own SDL window on Linux/macOS), nearest/bilinear/scanline/CRT filters, resolution scale, aspect/fit, fullscreen, FPS overlay |
| **Input** | Keyboard and XInput mapping for players 1–4, radial dead zones, trigger threshold, Y inversion and stick swap |
| **Quality of life** | Four save-state slots, session resume, powered-off screen, multiple library roots, favorites, play time, per-title overrides, screenshots, accent themes |
| **Diagnostics** | Serilog rolling logs, Activity page, warning/error bug reports, optional usage statistics, GitHub update check |
| **Automation** | Session overrides and headless runs with JSON reports for CI and benchmarks |
| **Platforms** | Windows 10/11 x64 (full integration), Linux x64 (bundled core in its own window), macOS (shell only) |

## The XGrid core

The core lives under [`xemu/`](xemu/), a directory name kept for provenance. It
is **not a pristine upstream checkout**: it is the XGrid-maintained core, and it
is free to evolve independently from xemu from this point forward.

Starting from the xemu 0.8.139 baseline, XGrid has added:

- an in-process **`xgrid_core.dll` ABI** for embedding, input, playback, disc and
  snapshots;
- real-time **ISO/XISO, CHD, CCI and ZArchive** block drivers with XGD partition
  detection;
- core-side **presentation filters, FPS overlay, screenshots and a headless
  display sink**;
- **pause, frame step and 1–8× fast-forward** driven from the shell (the UI
  exposes 4×);
- four **save-state slots** and the automatic session state;
- a native **Direct3D 11 preview renderer** for Windows;
- the ported **Sega Chihiro** machine and cabinet devices.

The file-level comparison with pristine xemu 0.8.139 remains available in
[xemu baseline differences](docs/reference/upstream-differences.md). It records
where XGrid started; it is no longer a promise that XGrid will track upstream
release-for-release.

## What you need

XGrid is a portable application: download, extract and run. The Windows and
Linux x64 frontend archives include the XGrid core. The macOS archives contain
the shell only and need an external xemu 0.8.139-compatible core.

| Host | Requirement |
| --- | --- |
| **OS** | Windows 10/11 x64, or Linux x64, or macOS (shell only) |
| **CPU** | A modern x64 processor; QEMU TCG is CPU-intensive |
| **GPU** | OpenGL-capable GPU with current drivers; Vulkan only when the core was built with it |
| **RAM** | 4 GB or more recommended (the guest uses 64 MB or 128 MB) |
| **Storage** | Space for the app, firmware, HDD image and your disc images |

You also need your own Xbox firmware and storage images:

| File | Typical name | Purpose |
| --- | --- | --- |
| **BIOS / flash ROM** | `Complex_4627.bin` | Required to boot the Xbox |
| **MCPX boot ROM** | `mcpx_1.0.bin` | 512-byte first-stage boot ROM |
| **EEPROM** | `eeprom.bin` | 256-byte console settings |
| **Hard-disk image** | `xbox_hdd.qcow2` | Dashboard, saves and snapshot storage |

If a path is left empty, XGrid looks for the file beside the emulator. Packaged
releases do **not** include these files, so configure your own in
**Settings → System** during the first-run wizard.

## Quick start

1. **Download** the [latest release](https://github.com/purelogiccode/XGrid/releases).
   Windows and Linux x64 frontend archives include the XGrid core under
   `core/xemu/`.
2. **Extract** the archive anywhere you can write to. There is no installer.
3. **Run** `XGrid.Frontend.Avalonia` (`XGrid.Frontend.Avalonia.exe` on Windows).
4. **Finish the first-run wizard** with your BIOS, EEPROM and HDD image, then add
   a folder of games.
5. **Pick a game** in **Library**, or press **F5** on **Home** to boot the
   dashboard.

If the releases page does not have a packaged build yet, use
[Build from source](#build-from-source).

Full walkthrough: [Quick start](docs/getting-started/quick-start.md) ·
[Installation](docs/getting-started/installation.md) ·
[First boot](docs/getting-started/first-boot.md).

> The binaries are not code-signed. Windows SmartScreen may warn; macOS may
> quarantine the archive. Verify the published checksum and only run archives
> from the official release page. See
> [Installation](docs/getting-started/installation.md#first-launch-warnings).

## Keyboard essentials

| Key | Action |
| --- | --- |
| `F5` | Power on / off |
| `P` | Pause / resume |
| `F10` | Step one frame while paused |
| `F6` / `F7` | Quick save / quick load |
| `F8` | Screenshot |
| `F11` | Toggle fullscreen |
| `Space` | Toggle 4× fast-forward |
| `Ctrl+O` | Open or swap a disc |
| `Ctrl+E` | Eject the disc |
| `Ctrl+S` | Save settings (Settings page) |
| `Esc` | Fullscreen: pause, then close on the next press |

Default player-1 controls: `A`/`S`/`D`/`F` = A/B/X/Y, arrows = D-pad,
`Enter` = Start, `Backspace` = Back, `Q`/`E` = White/Black, `Z`/`C` = triggers.
**Space is reserved for fast-forward.** Everything else is remappable in
**Settings → Input**.

## Supported media

| Format | Support | Notes |
| --- | --- | --- |
| **ISO / XISO** | Yes | Optimized XISO or full Redump; game partition found automatically |
| **CHD** | Yes | Compressed MAME CHD, decoded on demand |
| **CCI** | Yes | Single or split `.1.cci` / `.2.cci` sets |
| **ZArchive (`.zar`)** | Yes | Entry-form archives and synthesized file-tree archives |
| **Chihiro netboot (`.bin`)** | Yes | Sega Chihiro FATX netboot images |
| **XBE** | Inspect only | A loose executable must be wrapped in a disc image |
| **HDD** | Yes | Raw or qcow2; dashboard and snapshot storage |

See [Media formats](docs/guides/media.md) for the full matrix and conversion
notes.

## Platform support

| Platform | Status |
| --- | --- |
| **Windows 10/11 x64** | Full shell integration: in-process core, embedded picture, save states, pause/step, fast-forward, live disc swap, mapped pads |
| **Linux x64** | Frontend archive bundles the standalone core. The core presents in its own window; shell save states, pause/step and disc swap are not available |
| **macOS** | Shell only. Point **Settings → System → Emulator binary** at an xemu 0.8.139-compatible core |

XGrid is **alpha software**. The core is mature; the shell, media drivers and
compatibility work are still evolving. Read
[Compatibility](docs/reference/compatibility.md) for the supported surface.

XGrid is **donationware**: every feature works without donating. The optional
banner disappears after a confirmed donation; see
[Supporting XGrid](docs/guides/donations.md).

## Documentation

The full manual is a multi-page Docsify site in [`docs/`](docs/index.md). Open
[`docs/index.html`](docs/index.html) or serve the folder with a static server.

| Section | Start here |
| --- | --- |
| **Getting started** | [Quick start](docs/getting-started/quick-start.md) · [Requirements](docs/getting-started/requirements.md) · [Installation](docs/getting-started/installation.md) · [First boot](docs/getting-started/first-boot.md) |
| **Using XGrid** | [Library](docs/guides/library.md) · [Playing games](docs/guides/playing-games.md) · [Settings](docs/guides/settings.md) · [Display](docs/guides/display.md) · [Input](docs/guides/input.md) · [Audio](docs/guides/audio.md) · [Save states](docs/guides/save-states.md) · [Media](docs/guides/media.md) |
| **Advanced** | [Sega Chihiro](docs/guides/chihiro.md) · [Chihiro controls](docs/guides/chihiro-controls.md) · [Command line and headless](docs/guides/headless.md) · [Logs and diagnostics](docs/guides/diagnostics.md) · [Accessibility](docs/guides/accessibility.md) · [Supporting XGrid](docs/guides/donations.md) |
| **Reference** | [Architecture](docs/reference/architecture.md) · [xemu baseline differences](docs/reference/upstream-differences.md) · [Configuration](docs/reference/configuration.md) · [Shortcuts](docs/reference/shortcuts.md) · [Compatibility](docs/reference/compatibility.md) · [Troubleshooting](docs/reference/troubleshooting.md) · [FAQ](docs/reference/faq.md) |
| **Development** | [Building](docs/development/building.md) · [Repository layout](docs/development/repository-layout.md) · [Native core](docs/development/core.md) · [Core lineage and upstream](docs/development/xemu-upstream.md) · [Quality gates](docs/development/quality-gates.md) · [Contributing](docs/development/contributing.md) |
| **Legal** | [Licensing](docs/legal/licensing.md) · [Privacy](docs/legal/privacy.md) · [Porting](docs/legal/porting-policy.md) |
| **Project** | [Release notes](docs/release-notes/index.md) · [Security policy](SECURITY.md) · [Issues](https://github.com/purelogiccode/XGrid/issues) |

Preview the documentation locally with any static server:

```bash
python -m http.server 8000 -d docs
# open http://localhost:8000
```

A shorter, GitHub-wiki-ready set of pages lives in [`wiki/`](wiki/Home.md).

## Build from source

```powershell
# Shell and tests
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build

# Native core (Windows + MSYS2 MINGW64)
pwsh -File scripts/build-native-xemu.ps1

# Native core (Linux)
bash scripts/build-native-xemu.sh

# Run
dotnet run --project src/XGrid.Frontend.Avalonia
```

The Windows core build produces `xemu/dist/xemu.exe`, `xgrid_core.dll` and the
runtime DLLs. Linux produces `xemu/dist/xemu` and `xgrid-core.json`. The `xemu/`
path is retained for provenance; this is the XGrid-maintained core. See
[Building from source](docs/development/building.md) for the toolchain and
[Quality gates](docs/development/quality-gates.md) for the checks.

To build release archives:

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

## Contributing and security

- Read [Contributing](docs/development/contributing.md) and
  [`CONTRIBUTING.md`](CONTRIBUTING.md).
- Report emulation and UI bugs in the
  [issue tracker](https://github.com/purelogiccode/XGrid/issues).
- Report vulnerabilities privately through
  [GitHub Security Advisories](https://github.com/purelogiccode/XGrid/security/advisories)
  or [`SECURITY.md`](SECURITY.md).

Please do not commit firmware, BIOS images, EEPROMs, HDD images or game data.

## Credits

XGrid's core began as **xemu 0.8.139**. We thank every developer who built the
xemu tree that made XGrid possible, and the QEMU project beneath it.

The following people contributed code to xemu's Xbox hardware, xemu-specific
host/UI paths or its XBE support through the 0.8.139 release. Names are shown
with GitHub handles where one is known; “Fabxx” also appears in the history as
“Revix”:

> 7oxicshadow · Alexandre Bouvier ([@abouvier](https://github.com/abouvier)) ·
> Antonio Abbatangelo ([@antangelo](https://github.com/antangelo)) ·
> CakeLancelot · Camden Smallwood ([@camden-smallwood](https://github.com/camden-smallwood)) ·
> coldhex · Dalai ([@Dalai-](https://github.com/Dalai-)) ·
> Daniel Cook ([@DCFUKSURMOM](https://github.com/DCFUKSURMOM)) ·
> Darren ([@DiscoStarslayer](https://github.com/DiscoStarslayer)) ·
> DaveX ([@0DaveX](https://github.com/0DaveX)) ·
> Dustin Holden ([@LoveMHz](https://github.com/LoveMHz)) ·
> Erik Abair ([@abaire](https://github.com/abaire)) · espes ·
> Fabxx ([@Fabxx](https://github.com/Fabxx)) ·
> Fred Hallock ([@faha223](https://github.com/faha223)) · haxar ·
> Igor Leonovich ([@igorleonovich](https://github.com/igorleonovich)) ·
> Jannik Vogel ([@JayFoxRox](https://github.com/JayFoxRox)) ·
> Jason ([@theboy181](https://github.com/theboy181)) · jessedye ·
> Josef Šimánek ([@simi](https://github.com/simi)) · jyetcv ·
> Logan Stromberg ([@lostromb](https://github.com/lostromb)) ·
> Lucas Eriksson ([@dracc](https://github.com/dracc)) ·
> Luke Usher ([@LukeUsher](https://github.com/LukeUsher)) ·
> Lynne ([@cyanreg](https://github.com/cyanreg)) ·
> Mason Thompson ([@MasonT8198](https://github.com/MasonT8198)) ·
> Matt Borgerson ([@mborgerson](https://github.com/mborgerson)) ·
> Mike Davis ([@Ernegien](https://github.com/Ernegien)) · NZJenkins ·
> octopoulos · oltolm · RageXbox · Reedzit ·
> Ryan Wendland ([@Ryzee119](https://github.com/Ryzee119)) ·
> Shiralyn ([@ShiralynDev](https://github.com/ShiralynDev)) ·
> Silent ([@CookiePLMonster](https://github.com/CookiePLMonster)) ·
> smx ([@smx-smx](https://github.com/smx-smx)) ·
> Stanislav Motylkov ([@binarymaster](https://github.com/binarymaster)) ·
> Stefan Schmidt ([@thrimbor](https://github.com/thrimbor)) ·
> Stian Eklund ([@stianeklund](https://github.com/stianeklund)) ·
> Trent Seed ([@TrentSeed](https://github.com/TrentSeed)) · Voxel9 ·
> Wilhelm Kovatch ([@wilkovatch](https://github.com/wilkovatch)) ·
> wutno ([@GXTX](https://github.com/GXTX)) ·
> Yuvi ([@Yuvi-App](https://github.com/Yuvi-App)).

We also thank the wider xemu community: the testers, compatibility reporters,
documentation contributors and everyone who kept the project moving before the
XGrid fork.

### Sega Chihiro lineage

The direct source of XGrid's Chihiro machine is the **xemu Chihiro fork
(Playtest-v2)** snapshot in `References2/xemu-Playtest-v2`. The
**XPSemu Chihiro alpha 2** fork was used as a cross-check and for later fixes.
We thank:

- **[Tovarichtch](https://github.com/Tovarichtch)** for the xemu Chihiro fork
  (Playtest-v2) that supplied the machine.
- **Réda Chérif-Touil**, whose copyright is carried by the Chihiro source files.
- **espes** and **Matt Borgerson**, whose earlier QEMU Chihiro, USB and Xbox
  code is carried in the same source files.
- **[tattoohanz](https://github.com/tattoohanz)** for maintaining the XPSemu
  Chihiro fork used as a cross-check.
- **[ZiZc3](https://github.com/ZiZc3)** for [XPSemu](https://github.com/ZiZc3/XPSemu),
  the PS5 port on which that fork is built, and everyone in XPSemu's credits.
- **The MAME contributors** whose tables and routines are used by the Chihiro
  devices: Samuele Zannoli, R. Belmont, ElSemi, David Haywood, Angelo Salese,
  Olivier Galibert and MetalliC. The DES routines come from Olivier Galibert's
  MAME `naomigd.cpp` (BSD-3-Clause), and the PIC tables come from `naomi.cpp`.

Xemu itself is built on QEMU. We thank **Fabrice Bellard and the entire QEMU
team**, past and present. The full QEMU contributor history is maintained at
[github.com/qemu/qemu](https://github.com/qemu/qemu/graphs/contributors).

## License

XGrid's own source is **GPL-2.0-or-later**. The XGrid core began from the
xemu/QEMU tree and keeps those original GPL, LGPL and MIT notices. Third-party
components and ported modules are recorded in
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). See
[Licensing](docs/legal/licensing.md).

<p align="center">
  <sub>XGrid is an independent project. Xbox and Microsoft are trademarks of Microsoft Corporation.</sub>
</p>
