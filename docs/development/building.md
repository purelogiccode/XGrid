# Building from source

XGrid has two build products:

1. the **Avalonia shell** (managed .NET); and
2. the **xemu core** (native C, built with meson/ninja in MSYS2).

The release build combines them. The core build is currently supported on
Windows with MSYS2; the shell builds on Windows, Linux and macOS.

## Prerequisites

### Shell

- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- PowerShell 7 (`pwsh`)
- Git

### Core (Windows)

- PowerShell 7 (`pwsh`).
- **MSYS2** with the MINGW64 toolchain. The default install path is `C:\msys64`.
- MINGW64 packages: GCC, meson, ninja, CMake, Python, pkg-config, GLib, pixman,
  zlib, zstd, SDL3 and libepoxy.
- The repository's build script expects `C:\msys64\usr\bin\bash.exe` and uses
  `C:\cmake\bin` and `/mingw64/bin` on `PATH`; adjust the script if your install
  lives elsewhere.

The core vendors libchdr and uses system zlib/zstd. A clean QEMU tree may still
let meson fetch its own subprojects on the first build.

## Build the shell

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
```

Warnings are errors. The solution also builds the managed oracle and test
projects; they are validation infrastructure, not the product.

## Build the core

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

The script:

- enters the MINGW64 environment;
- runs xemu's `build.sh` with a local `make`→`ninja` shim;
- builds `xemu.exe`;
- builds `xgrid_core.dll` from the same objects for in-process hosting;
- builds `qemu-img`/`qemu-io` for media checks;
- copies runtime DLLs into `xemu/dist/`;
- writes `xemu/dist/xgrid-core.json` with the compiled renderers.

A clean rebuild:

```powershell
pwsh -File scripts/build-native-xemu.ps1 -Clean
```

The build is also wired into the solution anchor:

```powershell
dotnet build XGrid.sln -c Release -p:BuildXemuCore=true
```

A normal solution build only builds the empty anchor, so it stays fast.

### Core output

```text
xemu/dist/
  xemu.exe
  xgrid_core.dll
  xgrid-core.json
  <runtime DLLs>
```

`qemu-img` and `qemu-io` are built in `xemu/build/` for the media checks; they
are not part of the packaged core.

## Run the shell

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

`XemuPaths` looks for the core in this order:

1. the explicit **Emulator binary** setting;
2. `XGRID_XEMU_CORE`;
3. `core/xemu/` beside the application (packaged layout);
4. `xemu/dist/` found by walking up from the application or current directory;
5. `data/xemu/` found the same way (development layout).

## Development firmware

For local development, put firmware and a scratch HDD in `data/xemu/`:

```text
data/xemu/
  Complex_4627.bin
  mcpx_1.0.bin
  eeprom.bin
  xbox_hdd.qcow2
```

`data/` is git-ignored and must never contain committed firmware or games. The
shell still lets you choose explicit paths in Settings; explicit paths win.

## Tests and formatting

```powershell
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

The test suite is self-contained and needs no firmware or game media. See
[Quality gates](quality-gates.md).

## Package a release

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

The Windows frontend archive must contain `core/xemu/`; build the core first or
pass `-SkipCore` for a development-only archive. See [Releasing](releasing.md).

## Linux and macOS

- The Avalonia shell builds normally with the .NET SDK.
- There is no packaged core for Linux/macOS yet. The `xemu/` tree is portable C,
  but the supported build script is Windows/MSYS2-only and the release archive is
  the shell only.
- Point the shell at a core built for your platform with `XGRID_XEMU_CORE` or the
  **Emulator binary** setting.

## Troubleshooting the build

| Symptom | Fix |
| --- | --- |
| `MSYS2 not found` / bash path error | Install MSYS2 at `C:\msys64` or update the script path. |
| meson/ninja missing | Install the MINGW64 packages listed above. |
| Vulkan not listed after build | The Vulkan dependencies were absent; the core still builds with OpenGL. |
| `xgrid_core.dll` missing | Build the `xgrid_core` target; check `wt-data/xemu-build.log`. |
| Shell cannot find the core | Set **Emulator binary** or `XGRID_XEMU_CORE`. |
