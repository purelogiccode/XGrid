# Building from source

XGrid has two build products:

1. the **Avalonia shell** (managed .NET); and
2. the **xemu core** (native C, built with meson/ninja).

The release build combines them. The core builds on Windows with MSYS2 and
natively on Linux; the shell builds on Windows, Linux and macOS. macOS core
builds are not supported by XGrid's scripts.

## Prerequisites

### Shell

- [.NET 11 SDK](https://dotnet.microsoft.com/download)
- PowerShell 7 (`pwsh`)
- Git

### Core (Windows)

- PowerShell 7 (`pwsh`).
- **MSYS2** with the MINGW64 toolchain. The default install path is `C:\msys64`.
- MINGW64 packages: GCC, meson, ninja, CMake, Python, pkg-config, GLib, pixman,
  zlib, zstd, lz4, SDL3 and libepoxy.
- The repository's build script expects `C:\msys64\usr\bin\bash.exe` and uses
  `C:\cmake\bin` and `/mingw64/bin` on `PATH`; adjust the script if your install
  lives elsewhere.

The core vendors libchdr and libCCI and uses system zlib/zstd/lz4. A clean QEMU
tree may still let meson fetch its own subprojects on the first build.

### Core (Linux)

- A C/C++ toolchain, `make`, `ninja`, `pkg-config`, CMake and Python 3.
- meson 1.5 or newer. Distribution packages are often older (Ubuntu 22.04 ships
  0.61), so use a virtual environment when in doubt.
- Python modules: PyYAML, plus `tomli` on Python older than 3.11.
- Development headers: GLib, zlib, zstd, lz4, libepoxy, libusb, libpcap,
  libsamplerate, GMP, SDL3 and the X11 libraries.

On Ubuntu 22.04 the tested package set is:

```bash
sudo apt install build-essential ninja-build pkg-config cmake \
  python3-venv python3-pip libglib2.0-dev libpixman-1-dev libepoxy-dev \
  libssl-dev libcurl4-openssl-dev libslirp-dev libzstd-dev zlib1g-dev \
  liblz4-dev \
  libusb-1.0-0-dev libpcap-dev libasound2-dev libpulse-dev libx11-dev \
  libxext-dev libxrandr-dev libxi-dev libxcursor-dev libxfixes-dev \
  libxss-dev libxtst-dev libxkbcommon-dev libdrm-dev libgbm-dev \
  libsamplerate0-dev libgmp-dev
```

The core needs SDL3 3.4.x. Ubuntu 22.04 does not package it, so build it once:

```bash
cmake -S SDL3-3.4.20 -B SDL3-build -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_INSTALL_PREFIX=/usr/local -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF \
  -DSDL_WAYLAND=OFF -DSDL_KMSDRM=OFF -DSDL_X11=ON
sudo cmake --build SDL3-build --target install -j"$(nproc)"
```

Enable `SDL_WAYLAND` instead of `SDL_X11` if your desktop has no X11/XWayland.
Install meson and the Python modules the build scripts import:

```bash
sudo python3 -m venv /opt/xgrid-venv
sudo /opt/xgrid-venv/bin/pip install --upgrade pip 'meson>=1.5' pyyaml tomli
export PATH=/opt/xgrid-venv/bin:$PATH
```

## Build the shell

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
```

Warnings are errors. The solution also builds the managed oracle and test
projects; they are validation infrastructure, not the product.

## Build the core

### Windows

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
- writes `xemu/dist/xgrid-core.json` with the compiled renderers and audio
  drivers.

A clean rebuild:

```powershell
pwsh -File scripts/build-native-xemu.ps1 -Clean
```

The Windows script opts into Vulkan. A bare Meson configure defaults
`xgrid_vulkan` off; pass `-Dxgrid_vulkan=true` if you build manually. For an
OpenGL-only core, pass `-NoVulkan` (smaller binary and faster build). The first
Vulkan build fetches the `glslang`, `volk` and `SPIRV-Reflect` subprojects and
takes longer.

Windows builds also compile the Direct3D 11 renderer when the toolchain provides
`d3d11` and `d3dcompiler`; the manifest reports whether it did. The D3D11 backend
compiles HLSL at runtime through `d3dcompiler_47.dll`, a Windows system
component. The release does not bundle a private copy.

The build is also wired into the solution anchor:

```powershell
dotnet build XGrid.sln -c Release -p:BuildXemuCore=true
```

A normal solution build only builds the empty anchor, so it stays fast.

### Linux

```bash
bash scripts/build-native-xemu.sh
```

The script checks the toolchain, runs xemu's `build.sh` with the XGrid media
drivers, builds `qemu-img`/`qemu-io` for the media checks, and writes the
renderer/audio manifest beside the core. Use `-j N` to set the job count and
`--clean` for a clean rebuild. The script leaves `xgrid_vulkan` off; add
`-Dxgrid_vulkan=true` to the underlying Meson configure when the Vulkan
dependencies are installed.

The Linux build is the standalone core. The in-process `xgrid_core` library,
four save-state slots and embedded display remain Windows-only; on Linux the
shell launches the core as a separate process with its own window, while
`--headless` uses the POSIX display sink. The recipe is tested on Ubuntu 22.04
with SDL3 3.4.20, also under WSL2/WSLg.

### Core output

Windows:

```text
xemu/dist/
  xemu.exe
  xgrid_core.dll
  xgrid-core.json
  <runtime DLLs>
```

Linux:

```text
xemu/dist/
  xemu
  xgrid-core.json
  LICENSE.txt
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

Firmware left empty in Settings resolves beside the found core, then in
`data/xemu/`.

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
[Quality gates](quality-gates.md) and [Testing](testing.md).

## Package a release

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

The Windows and Linux frontend archives must contain `core/xemu/`; build the
core on its own host or stage per-RID builds with `-CoreRoot`, or pass
`-SkipCore` for a development-only archive. See [Releasing](releasing.md).

## Linux and macOS notes

- The Avalonia shell builds normally with the .NET SDK on both platforms.
- On Linux, build the core with `bash scripts/build-native-xemu.sh`. The shell
  finds `xemu/dist/xemu` automatically, or point it there with
  `XGRID_XEMU_CORE` or **Emulator binary**.
- The `linux-x64` release archive carries the Linux core under `core/xemu/`. The
  shell runs it as a separate process with its own window.
- The in-process core, host sinks, save states and embedded display are
  Windows-only for the normal shell UI. On Linux the core handles presentation
  and input through SDL; the shell still owns the library, settings and
  diagnostics. Headless runs use the POSIX display sink on Linux and macOS.
- macOS has no XGrid core build script. The macOS archives contain the shell
  only; point the shell at an upstream xemu 0.8.139 build with **Emulator
  binary**. For a local core with the XGrid additions, build the tracked tree
  with Homebrew dependencies:

  ```bash
  brew install meson ninja pkg-config glib pixman zstd lz4 libepoxy libusb \
      libpcap libsamplerate gmp sdl3
  cd xemu
  ./configure --extra-cflags="-DXBOX=1 -I$(brew --prefix)/include" \
      --extra-ldflags="-L$(brew --prefix)/lib" \
      --target-list=i386-softmmu --disable-werror --disable-docs \
      --disable-guest-agent --disable-vnc --disable-spice --disable-gtk \
      --disable-sdl-image --disable-libssh --disable-curses --disable-brlapi \
      -Dzstd=enabled --enable-tools --disable-curl --disable-cocoa
  make -j"$(sysctl -n hw.ncpu)" qemu-system-i386
  cp build/qemu-system-i386 dist/xemu
  xattr -cr dist/xemu && codesign -s - -f dist/xemu
  ```

  The Homebrew build is for local testing; a release core still needs its
  libraries bundled with the archive.

## Troubleshooting the build

| Symptom | Fix |
| --- | --- |
| `MSYS2 not found` / bash path error | Install MSYS2 at `C:\msys64` or update the script path. |
| `meson: command not found` / meson too old | Install meson 1.5+ with `pip install 'meson>=1.5'` and put it on `PATH`. |
| `No package 'sdl3' found` | Build SDL3 3.4.x from source or install its development package. |
| `Dependency "samplerate" not found` | Install `libsamplerate0-dev`. |
| `Dependency "gmp" not found` | Install `libgmp-dev`. |
| `ModuleNotFoundError: No module named 'yaml'` | Install PyYAML into the Python that runs meson. |
| Vulkan not listed after build | The core was built with `-NoVulkan`, or the Vulkan subprojects could not be fetched. Re-run without it; OpenGL still builds. |
| `xgrid_core.dll` missing | Build the `xgrid_core` target; check `wt-data/xemu-build.log`. |
| Shell cannot find the core | Set **Emulator binary** or `XGRID_XEMU_CORE`. |

## Related pages

- [Repository layout](repository-layout.md)
- [The native core](core.md)
- [Quality gates](quality-gates.md)
- [Releasing](releasing.md)
