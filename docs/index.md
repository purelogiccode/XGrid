# XGrid documentation

**XGrid is an enhanced xemu 0.8.139 core with a modern Avalonia desktop shell.**
The emulator is the maintained xemu/QEMU core tracked in `xemu/`. XGrid adds the
application around it: a game library, tabbed settings, per-title overrides,
real-time compressed media, save states, display and input plumbing,
diagnostics and packaging.

> XGrid is not affiliated with Microsoft or the xemu project. It ships no
> firmware, BIOS images, EEPROMs, hard-disk images or game data. You must supply
> your own legally obtained files.

## New to XGrid?

Start with the five-minute setup, then read the page for whatever you want to
change next.

1. **Install** — [Quick start](getting-started/quick-start.md) or
   [Installation](getting-started/installation.md).
2. **Configure firmware** — [First boot](getting-started/first-boot.md).
3. **Add games** — [Game library](guides/library.md).
4. **Play** — [Playing games](guides/playing-games.md).
5. **Tune it** — [Settings](guides/settings.md), [Display](guides/display.md),
   [Input](guides/input.md) and [Audio](guides/audio.md).

## What XGrid provides

| Area | Details |
| --- | --- |
| **Core** | xemu 0.8.139 / QEMU 10.2 TCG i386, NV2A OpenGL/Vulkan/Direct3D 11 preview/NULL, MCPX, APU/DSP, ACI, IDE/ATAPI, USB/OHCI, SMBus |
| **Shell** | Avalonia 12.2 application with Home, Library, Settings, Activity and About pages |
| **Hosting** | In-process `xgrid_core.dll` on Windows; separate process on Linux and as the Windows fallback |
| **Media** | ISO/XISO, CHD, CCI, ZArchive (entry form or file tree), Chihiro netboot `.bin`, raw/qcow2 HDD |
| **Display** | Core-owned picture, nearest/bilinear/scanline/CRT filters, resolution scale, aspect/fit, fullscreen, FPS overlay |
| **Input** | Keyboard and XInput mapping for players 1–4, dead zones, trigger threshold, stick options |
| **Session** | Four save-state slots, automatic session save/resume, powered-off screen, stay-open mode |
| **Library** | Recursive folder scan, search, filters, sorts, favorites, play time, local and online cover art |
| **Diagnostics** | Serilog rolling logs, searchable Activity page, bug reports, usage statistics, update check |
| **Automation** | Session overrides and headless runs with JSON reports |

## Highlights

### A desktop application, not a bare emulator window

The Library scans your folders without hashing or opening every image. Cards show
covers, format, size, title ID and play time. Search, filters, sorts, favorites
and per-title overrides are all in the shell. The console picture itself is still
rendered by the core.

### Media without an extraction step

- **ISO/XISO** — optimized XISOs and full Redump images boot directly.
- **CHD** — compressed discs are decoded on demand by `xgridchd`.
- **CCI** — single and split sets are decoded on demand by `xgridcci`.
- **ZArchive** — entry-form archives expose their ISO; file-tree archives are
  synthesized into an XDVDFS volume at open time.

Read [Media formats](guides/media.md).

### A picture the core owns

The emulator renders and presents every frame with its own swapchain. On Windows
the core window is embedded in the Home page; the shell never copies, filters or
redraws the picture. Presentation filters, the FPS overlay and screenshots are
core features selected by the shell.

Read [Display and filters](guides/display.md) and
[Architecture](reference/architecture.md).

### Save states and a session that resumes

Four QEMU/xemu slots are stored inside the machine's storage. An optional
automatic `xgrid-session` snapshot can be written on power-off and restored from
the Power button. Both need in-process hosting.

Read [Save states](guides/save-states.md).

### Sega Chihiro in the same shell

The ported Chihiro machine boots netboot `.bin` images with its own firmware,
media board, DIMM, JVS, card readers, force feedback and linked cabinets. Xbox
discs and Chihiro images can share one library.

Read [Sega Chihiro](guides/chihiro.md).

## Platform support

| Platform | Status |
| --- | --- |
| **Windows 10/11 x64** | Full shell integration: in-process core, embedded picture, save states, pause/step, fast-forward, live disc swap and shell-mapped pads |
| **Linux x64** | Frontend archive bundles the standalone core. The core presents and reads input in its own SDL window; shell save states, pause/step and disc swap are unavailable |
| **macOS** | Shell only. Point **Settings → System → Emulator binary** at an xemu 0.8.139 build |

## Project status

XGrid is **alpha software**. The emulator core is mature; the shell, media
drivers and compatibility work are still evolving. Treat
[Compatibility](reference/compatibility.md) as the supported surface, not a
promise that every Xbox disc will boot.

Known limitations:

- Save states, pause/step, fast-forward and live disc swap need in-process
  hosting.
- The shell does not expose netplay.
- CHD and CCI are read-only. Keep the HDD image writable if you use save states.
- Chihiro audio and network titles have upstream limitations.
- macOS archives contain the shell only.

## Documentation map

| Section | Pages |
| --- | --- |
| **Getting started** | [Quick start](getting-started/quick-start.md) · [Requirements](getting-started/requirements.md) · [Installation](getting-started/installation.md) · [First boot](getting-started/first-boot.md) |
| **Using XGrid** | [Library](guides/library.md) · [Playing games](guides/playing-games.md) · [Settings](guides/settings.md) · [Display](guides/display.md) · [Input](guides/input.md) · [Audio](guides/audio.md) · [Save states](guides/save-states.md) · [Media](guides/media.md) |
| **Advanced use** | [Sega Chihiro](guides/chihiro.md) · [Chihiro controls](guides/chihiro-controls.md) · [Command line and headless](guides/headless.md) · [Logs and diagnostics](guides/diagnostics.md) · [Accessibility](guides/accessibility.md) · [Supporting XGrid](guides/donations.md) |
| **Reference** | [Architecture](reference/architecture.md) · [Upstream differences](reference/upstream-differences.md) · [Configuration](reference/configuration.md) · [Shortcuts](reference/shortcuts.md) · [Compatibility](reference/compatibility.md) · [Troubleshooting](reference/troubleshooting.md) · [FAQ](reference/faq.md) · [Glossary](reference/glossary.md) · [Service endpoints](reference/service-endpoints.md) · [Donation API](reference/donation-api.md) |
| **Development** | [Building](development/building.md) · [Repository layout](development/repository-layout.md) · [Native core](development/core.md) · [Maintaining the xemu tree](development/xemu-upstream.md) · [Quality gates](development/quality-gates.md) · [Testing](development/testing.md) · [Inspection](development/inspection.md) · [Covers](development/covers.md) · [Platform embedding](development/platform-embedding.md) · [Releasing](development/releasing.md) · [Contributing](development/contributing.md) |
| **Legal** | [Licensing](legal/licensing.md) · [Privacy](legal/privacy.md) · [Porting](legal/porting-policy.md) · [Release licensing](legal/release-licensing.md) · [Source availability](legal/source-availability.md) |
| **Project** | [Release notes](release-notes/index.md) · [Security policy](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md) · [Issues](https://github.com/purelogiccode/XGrid/issues) · [Wiki](https://github.com/purelogiccode/XGrid/tree/master/wiki) |

## License

XGrid's own source is **GPL-2.0-or-later**. The vendored xemu/QEMU tree keeps
its original GPL, LGPL and MIT notices. Every third-party component and ported
module is recorded in [`THIRD_PARTY_NOTICES.md`](https://github.com/purelogiccode/XGrid/blob/master/THIRD_PARTY_NOTICES.md).

See [Licensing](legal/licensing.md).
