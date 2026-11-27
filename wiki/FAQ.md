# FAQ

## What is XGrid?

A custom enhanced and patched core that began as **xemu 0.8.139**, now evolving
separately, with a modern **Avalonia desktop shell**.

## Does it include BIOS files or games?

No. Supply your own legally obtained firmware, HDD image and game images.

## Which platforms work?

Windows 10/11 x64 has full integration. Linux x64 ships the standalone core in
its own window. macOS ships the shell only and needs an external xemu 0.8.139
build.

## Does it need .NET?

No for release archives; they are self-contained. Yes for source builds (.NET 11
SDK).

## Which renderer should I use?

**OpenGL** is the safe default. Vulkan and Direct3D 11 are offered only when the
core was built with them; Direct3D 11 is a Windows preview. Null is headless.

## Which media formats work?

ISO/XISO (optimized or Redump), CHD, CCI (single or split), ZArchive (entry or
file tree), Chihiro `.bin`, and raw/qcow2 HDD. Loose XBE is inspection only.

## Do file-tree ZAR archives work?

Yes. The core synthesizes an XDVDFS volume in memory. Names must be Latin-1, up
to 42 characters, without separators.

## Can I play Chihiro games?

Yes, with netboot `.bin` images and the Chihiro firmware. Convert Xbox-format
Chihiro ISOs or GD-ROMs in Settings or with the CLI. Direct GD-ROM boot is not
supported.

## Can I use a split CCI set?

Yes. Open the first part (`game.1.cci`); the core discovers the rest.

## Does XGrid stay open after power off?

Yes by default. Turn off **Keep XGrid open after power off** to close after a
normal shutdown. A faulted core stays open.

## Can it resume where I left off?

Yes with in-process hosting. Enable **Save state before power off** and **Resume
the saved session**. Launching a game from the Library starts fresh.

## Do save states work in the separate process?

No. They need the in-process ABI on Windows.

## Where are save states stored?

Inside the HDD image (`xgrid-slot-1` … `xgrid-slot-4`, `xgrid-session`). Chihiro
uses `chihiro_snapshots.qcow2`.

## Where is `settings.json`?

Windows: `%APPDATA%\XGrid\settings.json`. Linux: `~/.config/XGrid/settings.json`.
macOS: `~/Library/Application Support/XGrid/settings.json`. Override with
`XGRID_FRONTEND_DATA_DIR` or `--data-dir`.

## Can I use my own xemu.toml?

No. XGrid generates a complete file for every launch. Configure through the
shell.

## Can I swap discs while running?

Yes with in-process hosting. `Ctrl+O` opens/swaps, `Ctrl+E` ejects. With a
separate process, power off first.

## Does it support multiple controllers or netplay?

Up to four XInput players are supported on Windows, with keyboard on player 1.
There is no shell netplay UI.

## How do I update?

Keep your data folder, replace the application folder, and start it. Save states
live in the HDD image and survive.

## Is XGrid affiliated with Microsoft or xemu?

No. It is an independent project. xemu and QEMU are credited in
`THIRD_PARTY_NOTICES.md`.

Full FAQ: [FAQ](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/faq.md).
