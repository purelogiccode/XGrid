# XCN0-XCN3 native CPU core build.
#
# Copies the pinned xemu 0.8.139 tree to native/qemu (git-ignored), applies the
# build-scoping patches, builds the QEMU CPU/memory archives and links the
# XGrid shim. Requires MSYS2 MINGW64 at C:\msys64 (gcc, meson, ninja, glib,
# pixman, zlib, libepoxy, xxhash, diffutils, PyYAML, expat).
param(
    [int]$Jobs = 16,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$msysRoot = '/' + $root.Substring(0, 1).ToLower() + ($root.Substring(2) -replace '\\', '/')
$source = Join-Path $root 'References2/xemu-0.8.139'
$qemu = Join-Path $root 'native/qemu'
$bash = 'C:\msys64\usr\bin\bash.exe'

if (-not (Test-Path $source)) {
    throw "missing pinned reference tree: $source"
}
if (-not (Test-Path $bash)) {
    throw "missing MSYS2 bash: $bash"
}

if ($Force -and (Test-Path $qemu)) {
    Remove-Item -Recurse -Force $qemu
}
if (-not (Test-Path $qemu)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $qemu) | Out-Null
    robocopy $source $qemu /E /XD .git build dist /XF *.zip /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -gt 3) {
        throw "robocopy failed with $LASTEXITCODE"
    }
}

$env:MSYSTEM = 'MINGW64'
$env:XGRID_MSYS_ROOT = $msysRoot
$env:XGRID_JOBS = [string]$Jobs

$script = @'
set -euo pipefail
cd "$XGRID_MSYS_ROOT/native/qemu"

if ! grep -q "XGRID SPIKE PATCH" configure; then
  for p in ../qemu-patches/*.patch; do
    echo "applying $p"
    patch -p1 < "$p"
  done
fi

if [ ! -f build/build.ninja ]; then
  ./configure --target-list=i386-softmmu --without-default-features --enable-tcg \
    --disable-werror --disable-docs --disable-tools --disable-sdl --disable-opengl \
    --disable-curl --disable-slirp --disable-virtfs
fi

ninja -C build -j "$XGRID_JOBS" \
  libqemu-i386-softmmu.a libsystem.a libqemuutil.a libqom.a libio.a libcrypto.a

cd "$XGRID_MSYS_ROOT/native/xgridcpu"
./build.sh
'@

& $bash -lc $script
if ($LASTEXITCODE -ne 0) {
    throw "native CPU build failed with $LASTEXITCODE"
}

Write-Host "native CPU core built:"
Write-Host "  $qemu/build/libqemu-i386-softmmu.a"
Write-Host "  $root/native/xgridcpu/xgc_test.exe"
