# FAQ

### What is XGrid?

An enhanced **xemu 0.8.139** core with a modern **Avalonia** desktop shell.

### Is it a fork of xemu?

A maintained in-tree fork of xemu 0.8.139: xemu is tracked in `xemu/`, XGrid
additions live in `xemu/xgrid-core/`, and upstream edits are recorded in
`xemu/xemu-patches/`.

### Is the emulator written in C#?

No. The **shell** is C#/Avalonia; the emulator is the native xemu/QEMU core. A
managed C# emulator is kept as a validation oracle.

### Why is the emulator native?

The native xemu/QEMU core gives mature device emulation and the performance the
product needs. The shell is C#; the emulator is not. A managed C# emulator exists
as a validation oracle, not as the product path.

### Does it include BIOS or games?

No. Supply your own legally obtained firmware and discs.

### Which platforms work?

Windows x64 has the full shell integration. The Linux x64 archive bundles the
standalone core, which runs in its own window; save states, pause/step and disc
swap need the Windows in-process path. The macOS archives contain the shell only
and need an external xemu 0.8.139 build.

### Do I need .NET?

No for release archives. Yes for building from source (.NET 11 SDK).

### Which renderer?

OpenGL by default. Direct3D 11 on Windows builds that report it. Vulkan only if
the core build includes it. Null is headless.

### Why is Vulkan missing?

The build lacks Vulkan dependencies; `xgrid-core.json` reports the compiled
renderers and the shell hides unsupported options.

### What media works?

ISO/XISO (optimized or full Redump), CHD, CCI, ZAR (entry form or file tree),
raw/qcow2 HDD. XBE is inspection only. Full Redump ISOs are mapped to the game
partition automatically.

### Why does my ZAR fail?

The archive may be corrupt or contain a name XDVDFS cannot store (Latin-1, up to
42 characters, no separators). Both entry-form and file-tree archives are
supported; read the core message in **Activity**.

### Does XGrid stay open when the console powers off?

Yes, by default. **Settings → System → Session → Keep XGrid open after power
off** leaves the shell on the powered-off screen. Turn it off to close XGrid
after a normal shutdown; a core fault stays open so the error is visible.

### Can XGrid resume where I left off?

Yes, with in-process hosting. Enable **Save state before power off** and
**Resume the saved session** under **Settings → System → Session**. XGrid writes
`xgrid-session` and restores it when the Power button starts the dashboard.
Launching a game from the Library starts it fresh. The console boots for about
12 seconds before the saved state is restored.

### Do save states work in separate process?

No. Use in-process hosting.

### Where are save states?

Inside the HDD image, named `xgrid-slot-1` … `xgrid-slot-4`.

### Where is settings.json?

`%APPDATA%\XGrid\` (Windows), `~/Library/Application Support/XGrid/` (macOS), or
the platform application-data folder (Linux). Override with
`XGRID_FRONTEND_DATA_DIR`.

### Can I use my own xemu.toml?

No. XGrid generates a complete configuration for each launch.

### Does it support netplay or more controllers?

The shell exposes one player-1 pad and no netplay UI.

### Can I swap discs while running?

No. Power off before launching another disc.

### How do I update?

Keep the data folder, replace the application folder, start the new release.

### Where do I report bugs?

Use the [issue tracker](https://github.com/purelogiccode/XGrid/issues). For
vulnerabilities, follow `SECURITY.md`.
