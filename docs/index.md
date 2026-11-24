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
| **Hosting** | In-process core (`xgrid_core.dll`) by default; separate-process fallback |
| **Media** | ISO/XISO, compressed CHD, entry-form ZArchive and raw/qcow2 HDD images |
| **Display** | Shell frame sink with nearest, bilinear, scanline and CRT filters; embedded core window mode; resolution scale, aspect and fit controls |
| **Input** | Player-1 keyboard and XInput mapping, dead zones, trigger threshold, Y inversion and stick swap |
| **Quality of life** | Four save-state slots, per-title overrides, whole-window screenshots, activity log, accent themes |
| **Platform** | Windows x64 is the fully supported emulation host today; the shell targets Linux and macOS |

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
- **Library** — recursive scanning of ISO, CHD, ZAR and XBE files, search and launch.
- **Settings** — System, Graphics, Audio, Input, Titles, Library and Appearance tabs.
- **Activity** — a searchable, copyable session log.
- **About** — build, runtime and project links.

### Media without extraction

- **CHD** discs are decoded on demand by the `xgridchd` block driver.
- **ZArchive** discs are decoded on demand by `xgridzar`; the archive must contain
  the disc as an `.iso` entry.
- **ISO/XISO** is handled directly by the core block layer.
- **XBE** files are listed for inspection but must be wrapped in a disc image to boot.

### Save states that belong to the machine

XGrid does not invent a snapshot format. It exposes xemu/QEMU snapshots as four
named slots (`xgrid-slot-1` … `xgrid-slot-4`), saved inside the hard-disk image.
F6 saves, F7 loads, and the core performs the work on its own main loop.

### Pause and frame step

**P** pauses a running console where it stands and resumes it; the last frame
stays on screen with a **Paused** badge. **F10** advances exactly one frame while
paused. Both need in-process core hosting, like save states.

### Disc swap without a restart

While the console runs, **Ctrl+O**, the toolbar **Open** button, or a Library row
inserts the chosen disc; **Ctrl+E** ejects and leaves the tray empty. The core
changes the block medium on its own loop, so the guest sees a tray cycle rather
than a reboot. In-process hosting only, like save states.

### Display and input done in the shell

When **Present core frames in the shell** is enabled, the core publishes BGRA
frames to a shared section and the shell presents them with pacing, scaling and
filters. The same session publishes a 32-byte pad report to the core, so the
keyboard and XInput mapping is XGrid's, not the core's.

## Quick start

1. **Download** the release archive for your platform. The Windows x64 frontend
   archive includes the emulator under `core/xemu/`.
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
| Using XGrid | [Library](guides/library.md) · [Settings](guides/settings.md) · [Display](guides/display.md) · [Input](guides/input.md) · [Audio](guides/audio.md) · [Save states](guides/save-states.md) · [Media](guides/media.md) |
| Reference | [Architecture](reference/architecture.md) · [Configuration](reference/configuration.md) · [Shortcuts](reference/shortcuts.md) · [Compatibility](reference/compatibility.md) · [Troubleshooting](reference/troubleshooting.md) · [FAQ](reference/faq.md) · [Glossary](reference/glossary.md) |
| Development | [Building](development/building.md) · [Layout](development/repository-layout.md) · [Core](development/core.md) · [Quality gates](development/quality-gates.md) · [Releasing](development/releasing.md) · [Release notes](release-notes/v0.1.0.md) · [Contributing](development/contributing.md) |
| Legal | [Licensing](legal/licensing.md) · [Porting](legal/porting-policy.md) · [Release licensing](legal/release-licensing.md) · [Source availability](legal/source-availability.md) |

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
