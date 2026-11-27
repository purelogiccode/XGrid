# XGrid Wiki

**XGrid** is a custom enhanced and patched core that began as **xemu 0.8.139**,
now evolving separately with a modern **Avalonia** desktop shell. It adds a
library, settings, per-title overrides, real-time CHD/CCI/ZAR media, save states,
display/input plumbing, screenshots and packaging.

> XGrid is not affiliated with Microsoft or xemu. It ships no BIOS, EEPROM, HDD
> or game data. Supply your own legally obtained files.

## Start here

| Page | What it covers |
| --- | --- |
| [Getting started](Getting-Started) | Install, configure firmware and boot. |
| [Game library](Game-Library) | Scan folders, search, favorites, play time and covers. |
| [Playing games](Playing-Games) | Power, pause, fast-forward, disc swap and screenshots. |
| [Settings](Settings) | Every tab and option. |
| [Display and filters](Display-and-Filters) | Embedded window, core filters, FPS overlay, frame-rate limit/skip, fullscreen. |
| [Input](Input) | Keyboard defaults, XInput players 1–4, dead zones. |
| [Audio](Audio) | Backends, DSP, HRTF and WAV capture. |
| [Save states](Save-States) | Four slots, F6/F7 and the in-process requirement. |
| [Media formats](Media-Formats) | ISO, CHD, CCI, ZAR and XBE behavior. |
| [Sega Chihiro](Chihiro) | Firmware, netboot conversion, linked cabinets. |
| [Chihiro controls](Chihiro-Controls) | Cabinet keys, guns, cards and force feedback. |
| [Headless and CLI](Headless) | Settings overrides, headless runs and JSON reports. |
| [Logging and diagnostics](Diagnostics) | Serilog logs, bug reports, statistics and updates. |
| [Donations](Donations) | The donation banner, verification and privacy. |
| [Accessibility](Accessibility) | Tooltips, keyboard navigation and screen readers. |
| [Privacy and data](Privacy) | What is local, what is sent and how to opt out. |
| [Troubleshooting](Troubleshooting) | Common failures and fixes. |
| [FAQ](FAQ) | Short answers. |
| [Building from source](Building-from-Source) | Shell, native core and gates. |
| [xemu baseline differences](Upstream-Differences) | The historical delta from pristine xemu 0.8.139. |
| [Testing](Testing) | Test projects, commands and conventions. |
| [Licensing](Licensing) | GPL, xemu/QEMU and third-party notices. |

## At a glance

- **Core:** XGrid custom core, originally xemu 0.8.139 / QEMU 10.2 TCG i386;
  NV2A OpenGL/Vulkan/Direct3D 11 preview/NULL, MCPX, APU/DSP, ACI,
  IDE/ATAPI, USB/OHCI, SMBus.
- **Shell:** Home, Library, Settings, Activity, About.
- **Hosting:** in-process `xgrid_core.dll` on Windows; separate process on Linux
  and as the fallback.
- **Media:** ISO/XISO (optimized or full Redump), compressed CHD, CCI, ZArchive
  (entry form or file tree), raw/qcow2 HDD.
- **Input:** keyboard and XInput for players 1–4 on Windows.
- **Session:** powered-off screen, stay-open option, power on at launch and
  optional automatic save/resume.
- **Automation:** command-line settings overrides and headless runs with JSON
  reports.
- **Diagnostics:** Serilog rolling logs, warning/error bug reports, startup usage
  statistics and a GitHub update check.
- **Donationware:** optional banner and email-based recognition; no feature
  locks.
- **Platform:** Windows x64 has the full shell integration; Linux x64 ships the
  standalone core in its own window; macOS ships the shell with an external core.

## Full documentation

The complete multi-page manual lives in the repository:

- [Documentation home](https://github.com/purelogiccode/XGrid/blob/master/docs/index.md)
- [Quick start](https://github.com/purelogiccode/XGrid/blob/master/docs/getting-started/quick-start.md)
- [Architecture](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/architecture.md)
- [Configuration](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/configuration.md)
- [Compatibility](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/compatibility.md)
- [Troubleshooting](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/troubleshooting.md)
- [Licensing](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/licensing.md)

## Status

XGrid is **alpha software**. The core is mature; the shell and compatibility work
are still evolving. Treat the compatibility page as the supported surface.
