# Frequently asked questions

## What is XGrid?

An enhanced distribution of **xemu 0.8.139** with a new **Avalonia desktop
shell**. The emulator is the maintained xemu core; XGrid adds the application,
media drivers, save states, display/input plumbing and packaging.

## Is XGrid a fork of xemu?

It is a **maintained in-tree distribution** of xemu 0.8.139: the xemu tree is
tracked in `xemu/`, XGrid's core additions live in `xemu/xgrid-core/`, and edits
to upstream files are recorded in `xemu/xemu-patches/`. It is not an independent
emulator.

## What language is the emulator written in?

The **emulator core** is the native xemu/QEMU codebase. The **shell** is
C#/Avalonia. The managed validation projects are a test oracle, not the product
path.

## Why is the emulator native?

The native xemu/QEMU core gives mature device emulation and the performance the
product needs. The shell is C#; the emulator is not.

## Does XGrid include BIOS files or games?

No. It ships no Microsoft firmware, EEPROMs, HDD images or game data. You must
supply your own legally obtained files.

## Which platforms are supported?

Windows 10/11 x64 has the full shell integration. The Linux x64 frontend archive
bundles the standalone core, which runs in its own window; save states, pause and
disc swap need the Windows in-process path. The macOS archives contain the shell
only and need an external xemu 0.8.139 build.

## Does it need .NET installed?

No for release archives; they are self-contained. Yes for building from source
(.NET 11 SDK).

## Why is there a donation banner?

XGrid is donationware. The banner stays under the toolbar until the donation
service confirms a completed donation for the email you used on the website. It
is hidden in fullscreen and cannot be dismissed without a confirmed donation.
See [Supporting XGrid](../guides/donations.md).

## Does donating unlock features?

No. Every feature is available without a donation. The banner is the only
difference, and it disappears after a confirmed donation.

## Which renderer should I use?

**OpenGL** is the safe default. **Vulkan** and **Direct3D 11** (Windows preview)
are offered only when the core build includes them. **NULL** is headless and
shows no picture.

## Why is Vulkan missing?

The core was built without Vulkan dependencies. The build writes
`xgrid-core.json` with the compiled renderers, and the shell hides what the build
cannot start.

## What media formats work?

- ISO/XISO — direct; optimized XISO and full Redump images both boot.
- CHD — real-time decode, including full Redump CHDs.
- CCI — single or split, real-time LZ4 decode.
- ZAR — real-time decode for entry-form and file-tree archives.
- Chihiro `.bin` — netboot images.
- XBE — inspection only.

See [Media formats](../guides/media.md).

## Do file-tree `.zar` archives work?

Yes. The core builds an XDVDFS volume in memory at open time and reads the files
from the compressed archive. Nothing is extracted. If a file name cannot be
represented in XDVDFS (Latin-1, up to 42 characters, no separators), the archive
is refused with the offending name.

## Can XGrid play Sega Chihiro games?

Yes, with **netboot `.bin` images** and the Chihiro firmware. Open
**Settings → System → Sega Chihiro**, choose the folder with the five firmware
files, then launch a `.bin` from the Library. Xbox-format Chihiro ISOs convert in
Settings or with `XGrid.Cli chihiro-netboot`; Redump cue/bin sets and MAME `.chd`
files convert with the game's key ZIP. XGrid does not boot `.cue` GD-ROM sets
directly. See [Sega Chihiro](../guides/chihiro.md).

## Can I use a split CCI set?

Yes. Open the first part (`game.1.cci`); the core discovers `game.2.cci` and the
later parts and stitches them at sector granularity. The library lists the set
once under `game`.

## Does XGrid stay open when the console powers off?

Yes, by default. **Settings → System → Session → Keep XGrid open after power
off** leaves the shell on the powered-off screen. Turn it off to close XGrid
after a normal shutdown; a core fault always stays open so the error is visible.

## Can XGrid resume where I left off?

Yes, with in-process core hosting. Enable **Save state before power off** and
**Resume the saved session** under **Settings → System → Session**. XGrid writes
an automatic `xgrid-session` snapshot and restores it when the Power button
starts the dashboard. Launching a game from the Library starts it fresh. The
dashboard may appear for about 12 seconds while the core finishes early boot.

## How do I choose the audio device?

Open **Settings → Audio**. The backend list comes from the core's compiled audio
drivers. For DirectSound, **Output device** lists the host playback devices; use
**Refresh** after plugging in a device. PulseAudio, PipeWire, ALSA, OSS, sndio
and JACK accept an optional device or server name. **Null (no output)** discards
audio and is used automatically by headless runs.

A saved DirectSound device that is no longer attached falls back to the system
default with a warning in **Activity**.

## Do save states work in the separate process?

No. They require the in-process core ABI. Switch **Core hosting** to
**In-process (recommended)** on Windows.

## Where are save states stored?

Inside the HDD image, as xemu/QEMU snapshots named `xgrid-slot-1` …
`xgrid-slot-4`, plus `xgrid-session` when automatic resume is enabled. They are
not files in the application folder. Chihiro uses `chihiro_snapshots.qcow2`.

## Where is `settings.json`?

In the per-user data folder:

- Windows: `%APPDATA%\XGrid\settings.json`
- Linux: `~/.config/XGrid/settings.json` (platform application-data folder)
- macOS: `~/Library/Application Support/XGrid/settings.json`

Override with `XGRID_FRONTEND_DATA_DIR` or `--data-dir`.

## Can I use my own xemu configuration?

No. XGrid generates a complete `xemu.toml` for every launch so a launch cannot
inherit stale state. Configure through the shell.

## Can I use a loose XBE?

It is listed for inspection but cannot boot on its own. Wrap it in an ISO, CHD,
CCI or ZAR.

## Does XGrid support netplay or multiple controllers?

The shell publishes up to four XInput players, with keyboard on player 1. It does
not expose netplay. The underlying core may support more, but it is not part of
the documented surface.

## Can I swap discs while the console is running?

Yes, with in-process hosting on Windows. Use `Ctrl+O`, the toolbar **Open**
button, or click another Library game. `Ctrl+E` ejects. With a separate core
process, power off before changing the disc.

## How do I update?

Keep your data folder, replace the application folder with the new release, and
start it. Save states live in the HDD image, so they survive.

## How do I verify a download?

Compare the archive hash with `SHA256SUMS-<rid>.txt` or the release page. See
[Installation](../getting-started/installation.md#verify-the-download).

## Where do I report bugs?

Use the [issue tracker](https://github.com/purelogiccode/XGrid/issues) for
emulation/UI bugs. For vulnerabilities, follow
[`SECURITY.md`](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md)
and use a private advisory.

## Is XGrid affiliated with Microsoft or xemu?

No. XGrid is an independent project. xemu and QEMU are credited in
[`THIRD_PARTY_NOTICES.md`](https://github.com/purelogiccode/XGrid/blob/master/THIRD_PARTY_NOTICES.md).
