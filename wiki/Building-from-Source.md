# Building from source

## Shell

Prerequisites: .NET 11 SDK, PowerShell 7, Git.

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
dotnet run --project src/XGrid.Frontend.Avalonia
```

## Native core (Windows)

Prerequisites: MSYS2 MINGW64 at `C:\msys64` with GCC, meson, ninja, CMake,
Python, pkg-config, GLib, pixman, zlib and zstd.

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Output:

```text
xemu/dist/
  xemu.exe
  xgrid_core.dll
  xgrid-core.json
  <runtime DLLs>
```

The solution anchor can run the same build:

```powershell
dotnet build XGrid.sln -c Release -p:BuildXemuCore=true
```

## Native core (Linux)

Prerequisites: GCC, make, ninja, pkg-config, meson 1.5+, Python with PyYAML,
GLib, zlib, zstd, libepoxy, libusb, libpcap, libsamplerate, GMP and SDL3
development packages. The tested Ubuntu package list and SDL3 build commands are
in the [full guide](https://github.com/purelogiccode/XGrid/blob/master/docs/development/building.md).

```bash
bash scripts/build-native-xemu.sh
```

Output:

```text
xemu/dist/
  xemu
  xgrid-core.json
  LICENSE.txt
```

The shell finds `xemu/dist/` automatically. The in-process core, save states and
embedded display are Windows-only; on Linux the shell launches the core as a
separate process with its own window.

## Firmware for development

Put scratch firmware in `data/xemu/`:

```text
Complex_4627.bin
mcpx_1.0.bin
eeprom.bin
xbox_hdd.qcow2
```

`data/` is git-ignored. Never commit firmware or game data.

## Packaging

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

The win-x64 frontend archive bundles `core/xemu/`; the Linux/macOS archives are
shell-only.

## Linux and macOS

The Linux core can be built from source with `scripts/build-native-xemu.sh`, but
the release archives still contain the shell only. macOS has no XGrid core build
script; point `XGRID_XEMU_CORE` or the **Emulator binary** setting at an upstream
xemu 0.8.139 build for your platform.

Full guide: [Building from source](https://github.com/purelogiccode/XGrid/blob/master/docs/development/building.md).
