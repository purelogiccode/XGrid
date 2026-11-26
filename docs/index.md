# XGrid

**XGrid is an in-tree fork of [xemu](https://xemu.app) 0.8.139 with a modern
Avalonia desktop shell.** The emulator is the maintained xemu core, built from the
`xemu/` tree in this repository. XGrid adds the application around it: library,
settings, per-title overrides, display and input plumbing, save states,
diagnostics, packaging and the documentation you are reading.

> XGrid is not affiliated with Microsoft or the xemu project. It ships no
> firmware, BIOS images, EEPROMs or game data. You must supply your own legally
> obtained files.

## At a glance

| Area | What XGrid provides |
| --- | --- |
| **Core** | xemu 0.8.139 / QEMU 10.2.0 TCG i386, NV2A OpenGL/Vulkan/NULL renderers, MCPX, APU/ACI, IDE/ATAPI, USB/OHCI, SMBus and the full Xbox boot chain |
| **Shell** | Avalonia 12 desktop application with Home, Library, Settings, Activity and About pages |
| **Hosting** | In-process core (`xgrid_core.dll`) on Windows; separate process on Linux and as a fallback |
| **Media** | ISO/XISO (optimized or full Redump), compressed CHD, CCI, ZArchive (entry form or file tree) and raw/qcow2 HDD images |
| **Display** | Core-owned picture with nearest, bilinear, scanline and CRT filters; embedded core window; resolution scale, aspect and fit controls |
| **Input** | Player-1 keyboard and XInput mapping, dead zones, trigger threshold, Y inversion and stick swap |
| **Quality of life** | Four save-state slots, automatic session save/resume, powered-off screen, multiple library roots, favorites, play time, per-title overrides, 4x fast-forward, local and online cover art, core-owned console screenshots, activity log, tooltips on every control, accent themes, optional donation banner |
| **Performance** | Core-drawn FPS overlay with present rate and frame time; headless `--benchmark` runs with JSON reports |
| **Diagnostics** | Serilog rolling logs, warning/error bug reports, startup usage statistics and a GitHub update check |
| **Automation** | Command-line settings overrides and headless runs with a JSON report for scripts and benchmarks |
| **Platform** | Windows x64 with full shell integration; Linux x64 with a bundled standalone core; macOS shell with an external core |

## Why a native core?

The emulator uses the proven native xemu/QEMU implementation because that is the
right tool for performance and hardware fidelity. XGrid invests in the shell, the
media drivers and a small, well-defined set of core additions.

The result is deliberately boring in the best way:

- the CPU, GPU, audio and devices are the mature xemu implementation;
- XGrid owns the user experience, media drivers, snapshot bridge and packaging;
- an in-tree patch record makes upstream updates a rebase, not a rewrite.

Read [Architecture](reference/architecture.md) for the full picture.

## Feature highlights

### A real desktop shell

- **Home** — power, stop, the live display, current disc and renderer status.
- **Library** - recursive scanning of ISO, CHD, CCI, ZAR and XBE files across several folders, search, filters, sorts, favorites, play time, local and online cover art and launch.
- **Settings** - System, Graphics, Audio, Input, Titles, Library and Appearance tabs.
- **Playback** - pause, single-frame step, 4x fast-forward and fullscreen with a
  pause-then-close Escape sequence.
- **Activity** — a searchable, copyable session log.
- **About** — build, runtime and project links.

### Media without extraction

- **CHD** discs are decoded on demand by the `xgridchd` block driver.
- **CCI** discs are decoded on demand by `xgridcci`; single files and split
  `.1.cci` / `.2.cci` sets are read directly.
- **ZArchive** discs are decoded on demand by `xgridzar`; an entry-form archive
  exposes its `.iso` entry, and a file-tree archive is synthesized into an
  XDVDFS volume at open time.
- **ISO/XISO** is handled directly by the core block layer. The `xgridiso`
  driver detects the XGD game-partition offset in a full Redump image and maps
  it to LBA 0; an optimized XISO needs no shift.
- **XBE** files are listed for inspection but must be wrapped in a disc image to boot.

### Save states that belong to the machine

XGrid does not invent a snapshot format. It exposes xemu/QEMU snapshots as four
named slots (`xgrid-slot-1` … `xgrid-slot-4`), saved inside the hard-disk image.
F6 saves, F7 loads, and the core performs the work on its own main loop.

### Power and session lifecycle

- **Powered-off screen** - a console-off panel with the XGrid logo and a power
  button instead of a blank picture area.
- **Stay-open mode** - the shell stays open after the console powers off by
  default; turn it off to close XGrid after a normal shutdown.
- **Power on when XGrid starts** - boot the configured disc or dashboard as soon
  as the window opens.
- **Automatic session save/resume** - optionally write `xgrid-session` before
  power off or window close, then restore it from the Power button. It is
  separate from the four manual slots and needs in-process hosting.

### Pause and frame step

**P** pauses a running console where it stands and resumes it; the last frame
stays on screen with a **Paused** badge. **F10** advances exactly one frame while
paused. Both need in-process core hosting, like save states.

### Disc swap without a restart

While the console runs, **Ctrl+O**, the toolbar **Open** button, or a Library row
inserts the chosen disc; **Ctrl+E** ejects and leaves the tray empty. The core
changes the block medium on its own loop, so the guest sees a tray cycle rather
than a reboot. In-process hosting only, like save states.

### Display and input

The core owns the picture: its window is embedded in the console page and paints
with its own swapchain, so the shell never copies, filters or redraws a frame.
The presentation filter and the FPS overlay are core features selected from the
shell. On Windows the same session publishes one 32-byte pad record per Xbox port,
so the keyboard and XInput mapping for players 1–4 is XGrid's, not the core's.

### Scriptable runs

`--headless` boots the core without a window and still renders off-screen. A
wall-clock or frame limit, the console summary and an optional JSON report make it
usable from a CI job or a benchmark script. The same command line can override
the renderer, scale, filtering, RAM, firmware and disc for one session without
touching the saved settings. See [Headless and CLI](guides/headless.md).

### Diagnostics you can act on

Serilog keeps a rolling log in the per-user data folder. Every warning or error
also carries the environment and exception details to the bug-report service,
and a startup check offers the GitHub release page when a newer build exists.
See [Logging and diagnostics](guides/diagnostics.md).

### Donationware, without feature locks

XGrid is free to use. The optional banner under the toolbar links to the
PureLogicCode donation page and disappears when the service confirms a completed
donation for your email. Donating is never required for a feature. See
[Supporting XGrid](guides/donations.md) and [Privacy and data](legal/privacy.md).

## Quick start

1. **Download** the release archive for your platform. The Windows and Linux
   x64 frontend archives include the emulator under `core/xemu/`.
2. **Extract** it anywhere. The applications are portable and self-contained.
3. **Start** `XGrid.Frontend.Avalonia`.
4. The first-run wizard asks for the BIOS, EEPROM, hard-disk image and an optional
   game folder. Everything can also be set later in **Settings → System** and
   **Settings → Library**.
5. Pick a title in **Library**, or press **Power on** to reach the dashboard.

Full details: [Installation](getting-started/installation.md) and
[First boot](getting-started/first-boot.md).

## Documentation map

| Section | Start here |
| --- | --- |
| Getting started | [Requirements](getting-started/requirements.md) · [Installation](getting-started/installation.md) · [First boot](getting-started/first-boot.md) |
| Using XGrid | [Library](guides/library.md) · [Settings](guides/settings.md) · [Display](guides/display.md) · [Input](guides/input.md) · [Audio](guides/audio.md) · [Headless and CLI](guides/headless.md) · [Logging and diagnostics](guides/diagnostics.md) · [Supporting XGrid](guides/donations.md) · [Accessibility](guides/accessibility.md) · [Save states](guides/save-states.md) · [Media](guides/media.md) |
| Reference | [Architecture](reference/architecture.md) · [Configuration](reference/configuration.md) · [Shortcuts](reference/shortcuts.md) · [Compatibility](reference/compatibility.md) · [Service endpoints](reference/service-endpoints.md) · [Donation API](reference/donation-api.md) · [Troubleshooting](reference/troubleshooting.md) · [FAQ](reference/faq.md) · [Glossary](reference/glossary.md) |
| Development | [Building](development/building.md) · [Layout](development/repository-layout.md) · [Core](development/core.md) · [Quality gates](development/quality-gates.md) · [Testing](development/testing.md) · [Inspection](development/inspection.md) · [Releasing](development/releasing.md) · [Release notes](release-notes/v0.1.0.md) · [Contributing](development/contributing.md) |
| Legal | [Licensing](legal/licensing.md) · [Privacy](legal/privacy.md) · [Porting](legal/porting-policy.md) · [Release licensing](legal/release-licensing.md) · [Source availability](legal/source-availability.md) |

## Project status

XGrid is **alpha software**. The core is mature, but the shell, media drivers and
compatibility work are still evolving. Treat the [Compatibility](reference/compatibility.md)
page as the supported surface, not a promise that every Xbox disc will boot.

## License

XGrid's own source is **GPL-2.0-or-later**. The vendored xemu tree keeps its
original GPL-2.0, LGPL and MIT notices. Every third-party component and ported
module is recorded in `THIRD_PARTY_NOTICES.md`. See [Licensing](legal/licensing.md).

## Wiki

A shorter, GitHub-wiki-ready set of pages lives in [`wiki/`](https://github.com/purelogiccode/XGrid/tree/master/wiki).
The full reference remains in this documentation.
