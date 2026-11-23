# Build the XGrid core (patched xemu 0.8.139) with MSYS2 MINGW64.
#
# The build tree is native/xemu; the output is native/xemu/dist, which the
# shell launches exactly like the prebuilt 0.8.139 binary.
param(
    [int]$Jobs = 12,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tree = Join-Path $root 'native/xemu'
$log = Join-Path $root 'wt-data/xemu-build.log'
if (-not (Test-Path $tree)) {
    throw "Missing $tree; run scripts/sync-native-xemu.ps1 first."
}

New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
# Our additions live in native/xgrid-core; meson cannot subdir() outside its
# project, so refresh the generated copy inside the build tree first.
robocopy (Join-Path $root 'native/xgrid-core') (Join-Path $tree 'xgrid-core') /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy (xgrid-core) failed with $LASTEXITCODE" }
$cleanStep = if ($Clean) { 'rm -rf build' } else { '' }
$script = @"
export MSYSTEM=MINGW64
export PATH=/c/cmake/bin:/mingw64/bin:/usr/bin:`$PATH
cd /d/repos/CSharp_XGrid/native/xemu
$cleanStep
./build.sh -j$Jobs --disable-werror --disable-docs --disable-guest-agent \
  --disable-vnc --disable-spice --disable-gtk --disable-sdl-image \
  --disable-libssh --disable-curses --disable-brlapi -Dzstd=enabled --enable-tools --disable-curl > /d/repos/CSharp_XGrid/wt-data/xemu-build.log 2>&1
status=`$?
# The packaging step copies DLLs it finds under /mingw64/bin; the slirp
# subproject builds its own, so ship it beside the executable.
cp -f build/subprojects/slirp/libslirp-0.dll dist/ 2>/dev/null || true
# The packaging step misses transitive dependencies (libcurl pulls brotli,
# idn2, nghttp2, pcre2, ...); copy the whole closure from the MinGW tree.
copy_deps() {
  ldd "`$1" 2>/dev/null | awk '{print `$3}' | grep -E '/mingw64/bin/' | while read -r dll; do
    cp -f "`$dll" dist/ 2>/dev/null || true
  done
}
for pass in 1 2 3; do
  copy_deps build/qemu-system-i386w.exe
  for d in dist/*.dll; do copy_deps "`$d"; done
done
echo "EXIT=`$status" >> /d/repos/CSharp_XGrid/wt-data/xemu-build.log
exit `$status
"@
$scriptPath = Join-Path $root 'wt-data/build-xemu.sh'
Set-Content -Path $scriptPath -Value $script -NoNewline
& 'C:\msys64\usr\bin\bash.exe' -lc "bash '/d/repos/CSharp_XGrid/wt-data/build-xemu.sh'"
if ($LASTEXITCODE -ne 0) {
    Get-Content $log -Tail 30
    throw "Core build failed (see $log)."
}

$exe = Join-Path $tree 'dist/xemu.exe'
if (-not (Test-Path $exe)) { throw "Build finished but $exe is missing." }
Write-Output ("core: {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
