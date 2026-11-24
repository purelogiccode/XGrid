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

**OpenGL** is the safe default. **Vulkan** is offered only when the core build
includes it. **NULL** is headless and shows no picture.

## Why is Vulkan missing?

The core was built without Vulkan dependencies. `scripts/build-native-xemu.ps1`
writes `xgrid-core.json` with the compiled renderers, and the shell hides what the
build cannot start.

## What media formats work?

- ISO/XISO — direct.
- CHD — real-time decode.
- ZAR — real-time decode when the archive contains an `.iso` entry.
- XBE — inspection only.

See [Media formats](../guides/media.md).

## Why does a `.zar` say it needs a synthesizer?

That archive stores the game as a **file tree**, not as one ISO entry. The guest
expects an XDVDFS volume, so the archive must contain the disc as an `.iso` entry.
File-tree archives are a known limitation.

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
