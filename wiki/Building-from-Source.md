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

The shell builds; the packaged core is Windows-only today. Set
`XGRID_XEMU_CORE` or the **Emulator binary** setting to a core built for your
platform.

Full guide: [Building from source](https://github.com/purelogiccode/XGrid/blob/master/docs/development/building.md).
