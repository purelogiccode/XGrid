# Building from source

XGrid has two products: the **Avalonia shell** (.NET) and the **native xemu
core** (C, meson/ninja).

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- PowerShell 7 (`pwsh`)
- Git
- **Windows core:** MSYS2 MINGW64 with GCC, meson, ninja, CMake, Python,
  pkg-config, GLib, pixman, zlib, zstd, lz4, SDL3 and libepoxy.
- **Linux core:** GCC/Clang, ninja, meson 1.5+, Python 3 with PyYAML, and the
  GLib/zlib/zstd/lz4/libepoxy/libusb/libpcap/libsamplerate/GMP/SDL3/X11
  development packages. SDL3 3.4.x is required.

## Build the shell

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
```

Warnings are errors; nullable is enabled; implicit usings are disabled.

## Build the core

Windows:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Linux:

```bash
bash scripts/build-native-xemu.sh
```

Outputs:

```text
# Windows
xemu/dist/xemu.exe
xemu/dist/xgrid_core.dll
xemu/dist/xgrid-core.json
<runtime DLLs>

# Linux
xemu/dist/xemu
xemu/dist/xgrid-core.json
xemu/dist/LICENSE.txt
```

Vulkan is built by default on Windows; pass `-NoVulkan` for an OpenGL-only core.
Use `-Clean` for a clean rebuild.

## Run

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

The shell finds the core in this order: explicit **Emulator binary**,
`XGRID_XEMU_CORE`, `core/xemu/`, `xemu/dist/` walking up, then `data/xemu/`.

## Development firmware

Put firmware and a scratch HDD in `data/xemu/` (git-ignored):

```text
data/xemu/
  Complex_4627.bin
  mcpx_1.0.bin
  eeprom.bin
  xbox_hdd.qcow2
```

## Tests and packages

```powershell
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

The tests need no firmware or game media. See [Testing](Testing) and
[Quality gates](https://github.com/purelogiccode/XGrid/blob/master/docs/development/quality-gates.md).

Full guide: [Building from source](https://github.com/purelogiccode/XGrid/blob/master/docs/development/building.md).
