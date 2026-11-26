# Frequently asked questions

## What is XGrid?

An enhanced distribution of **xemu 0.8.139** with a new **Avalonia desktop
shell**. The emulator is the maintained xemu core; XGrid adds the application,
media drivers, save states, display/input plumbing and packaging.

## Is XGrid a fork of xemu?

It is a **maintained in-tree fork** of xemu 0.8.139: the xemu tree is tracked in
`xemu/`, XGrid's core additions live in `xemu/xgrid-core/`, and edits to upstream
files are recorded in `xemu/xemu-patches/`. It is not an independent emulator.

## Is XGrid written in C#?

The **shell** is C#/Avalonia. The **emulator core** is the native xemu/QEMU
codebase. A managed C# emulator exists in the repository as a test oracle, but it
is not the product path.

## Why is the emulator native?

The native xemu/QEMU core gives mature device emulation and the performance the
product needs. The shell is C#; the emulator is not. A managed C# emulator exists
as a validation oracle, not as the product path.

## Does XGrid include BIOS files or games?

No. It ships no Microsoft firmware, EEPROMs, HDD images or game data. You must
supply your own legally obtained files.

## Which platforms are supported?

Windows 10/11 x64 has the full shell integration. The Linux x64 frontend archive
bundles the standalone core, which runs in its own window; save states, pause
and disc swap need the Windows in-process path. The macOS archives contain the
shell only and need an external xemu 0.8.139 build.

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

## How do I check a donation?

Press **I already donated**, enter the email used on the donation page, and let
the app check the service. The email is stored locally and is never written to
the log or a bug report.

## Which renderer should I use?

**OpenGL** is the safe default. **Vulkan** and **Direct3D 11** (Windows) are
offered only when the core build includes them. **NULL** is headless and shows
no picture.

## Why is Vulkan missing?

The core was built without Vulkan dependencies. `scripts/build-native-xemu.ps1`
writes `xgrid-core.json` with the compiled renderers, and the shell hides what the
build cannot start.

## What media formats work?

- ISO/XISO — direct; optimized XISO and full Redump images both boot.
- CHD — real-time decode, including full Redump CHDs.
- CCI — Cerbios/Repackinator compressed image; single or split, real-time decode.
- ZAR — real-time decode for entry-form and file-tree archives.
- XBE — inspection only.

See [Media formats](../guides/media.md).

## Do file-tree `.zar` archives work?

Yes. The core builds an XDVDFS volume in memory at open time and reads the
files from the compressed archive. Nothing is extracted. If a file name cannot
be represented in XDVDFS (Latin-1, up to 42 characters, no separators), the
archive is refused with the offending name.

## Can XGrid play Sega Chihiro games?

Yes, with **netboot `.bin` images** and the Chihiro firmware. Open
**Settings → System → Sega Chihiro**, choose the folder with
`chihiro_xbox_bios.bin`, `fpr21042_m29w160et.bin`, `ic10_g24lc64.bin`,
`ic11_24lc024.bin` and `pc20_g24lc64.bin`, then launch a `.bin` from the
Library. The homebrew `XBOXOG - Chihiro` style ISOs convert in
**Settings → System → Sega Chihiro** (or with `XGrid.Cli chihiro-netboot`), and
Redump cue/bin GD-ROM sets convert there too (or with
`XGrid.Cli chihiro-redump`) using the game's MAME key ZIP. XGrid does not boot
`.cue` GD-ROM sets directly. See [Sega Chihiro](../guides/chihiro.md).

## Can I use a split CCI set?

Yes. Open the first part (`game.1.cci`); the core discovers `game.2.cci` and
the later parts and stitches them at sector granularity. The library lists the
set once under `game`.

## Does XGrid stay open when the console powers off?

Yes, by default. **Settings → System → Session → Keep XGrid open after power
off** leaves the shell on the powered-off screen. Turn it off to close XGrid
after a normal shutdown; a core fault always stays open so the error is visible.

## Can XGrid resume where I left off?

Yes, with in-process core hosting. Enable **Save state before power off** and
**Resume the saved session** under **Settings → System → Session**. XGrid writes
an automatic `xgrid-session` snapshot and restores it when the Power button
starts the dashboard. Launching a game from the Library starts it fresh.

The console finishes its early boot first, so the dashboard or game may appear
for about 12 seconds before the saved state is restored. See
[Save states](../guides/save-states.md#automatic-session-state).

## How do I choose the audio device?

Open **Settings → Audio**. The backend list comes from the core's compiled
audio drivers. For DirectSound, **Output device** lists the host playback
devices; use **Refresh** after plugging in a device. PulseAudio, PipeWire, ALSA,
OSS, sndio and JACK accept an optional device or server name. **Null (no
output)** discards audio and is used automatically by headless runs.

A saved DirectSound device that is no longer attached falls back to the system
default with a warning in **Activity**.

## Do save states work in the separate process?

No. They require the in-process core ABI. Switch **Core hosting** to **In-process
(recommended)**.

## Where are save states stored?

Inside the HDD image, as xemu/QEMU snapshots named `xgrid-slot-1` … `xgrid-slot-4`.
They are not files in the application folder.

## Where is `settings.json`?

In the per-user data folder:

- Windows: `%APPDATA%\XGrid\settings.json`
- Linux: the platform application-data folder (usually `~/.config/XGrid/`)
- macOS: `~/Library/Application Support/XGrid/settings.json`

Override with `XGRID_FRONTEND_DATA_DIR`.

## Can I use my own xemu configuration?

No. XGrid generates a complete `xemu.toml` for every launch so a launch cannot
inherit stale state. Configure through the shell.

## Does XGrid support CHD without extraction?

Yes. The `xgridchd` block driver decodes hunks on demand.

## Can I use a loose XBE?

It is listed for inspection but cannot boot on its own. Wrap it in an ISO or CHD.

## Does XGrid support netplay or multiple controllers?

The shell exposes one player-1 pad and no netplay UI. The underlying core may
support more, but it is not part of the documented surface.

## Can I swap discs while the console is running?

No. Power off before launching another disc.

## How do I update?

Keep your data folder, replace the application folder with the new release, and
start it. Save states live in the HDD image, so they survive.

## Where do I report bugs?

Use the [issue tracker](https://github.com/purelogiccode/XGrid/issues) for
emulation/UI bugs. For vulnerabilities, follow `SECURITY.md` and use a private
advisory.

## Is XGrid affiliated with Microsoft or xemu?

No. XGrid is an independent project. xemu and QEMU are credited in
`THIRD_PARTY_NOTICES.md`.
