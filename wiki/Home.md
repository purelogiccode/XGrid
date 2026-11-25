# XGrid Wiki

**XGrid** is an enhanced **xemu 0.8.139** core with a modern **Avalonia** desktop
shell. It adds a library, settings, per-title overrides, real-time CHD/ZAR media,
save states, display/input plumbing, screenshots and packaging.

> XGrid is not affiliated with Microsoft or xemu. It ships no BIOS, EEPROM, HDD
> or game data. Supply your own legally obtained files.

## Start here

| Page | What it covers |
| --- | --- |
| [Getting started](Getting-Started) | Install, configure firmware and boot. |
| [Game library](Game-Library) | Scan folders, search and launch titles. |
| [Settings](Settings) | Every tab and option. |
| [Display and filters](Display-and-Filters) | Embedded window, core filters, FPS overlay, fullscreen. |
| [Input](Input) | Keyboard defaults, XInput, dead zones. |
| [Audio](Audio) | Backends, DSP, HRTF and WAV capture. |
| [Headless and CLI](Headless) | Settings overrides, headless runs and JSON reports. |
| [Logging and diagnostics](Diagnostics) | Serilog logs, bug reports, statistics and updates. |
| [Donations](Donations) | The donation banner, verification and privacy. |
| [Accessibility](Accessibility) | Tooltips, keyboard navigation and screen readers. |
| [Privacy and data](Privacy) | What is local, what is sent and how to opt out. |
| [Save states](Save-States) | Four slots, F6/F7 and the in-process requirement. |
| [Media formats](Media-Formats) | ISO, CHD, ZAR and XBE behavior. |
| [Troubleshooting](Troubleshooting) | Common failures and fixes. |
| [FAQ](FAQ) | Short answers. |
| [Building from source](Building-from-Source) | Shell, native core and gates. |
| [Testing](Testing) | Test projects, commands and conventions. |
| [Licensing](Licensing) | GPL, xemu/QEMU and third-party notices. |

## At a glance

- **Core:** xemu 0.8.139 / QEMU 10.2 TCG i386, NV2A OpenGL/Vulkan/NULL,
  MCPX, APU/DSP, ACI, IDE/ATAPI, USB/OHCI, SMBus.
- **Shell:** Home, Library, Settings, Activity, About.
- **Hosting:** in-process `xgrid_core.dll` on Windows; separate process on Linux and as a fallback.
- **Media:** ISO/XISO, compressed CHD, ZArchive (entry form or file tree), raw/qcow2 HDD.
- **Automation:** command-line settings overrides and headless runs with JSON reports.
- **Diagnostics:** Serilog rolling logs, warning/error bug reports, startup usage statistics and a GitHub update check.
- **Donationware:** optional banner and email-based recognition; no feature locks.
- **Platform:** Windows x64 has the full shell integration; Linux x64 ships the
  standalone core in its own window; macOS ships the shell with an external core.

## Full documentation

The complete multi-page documentation lives in the repository:

- [Documentation home](https://github.com/purelogiccode/XGrid/blob/master/docs/index.md)
- [Architecture](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/architecture.md)
- [Configuration](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/configuration.md)
- [Service endpoints](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/service-endpoints.md)
- [Donations](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/donations.md)
- [Privacy and data](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/privacy.md)
- [Testing](https://github.com/purelogiccode/XGrid/blob/master/docs/development/testing.md)
- [Compatibility](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/compatibility.md)

## Status

XGrid is **alpha software**. The core is mature; the shell and compatibility work
are still evolving. Treat the compatibility page as the supported surface.
