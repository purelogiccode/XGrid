# Build the maintained xemu core from the vendored, tracked tree (xemu/).
#
# The tree at xemu/ is the source of truth: xemu 0.8.139 plus the XGrid
# additions in xemu/xgrid-core and the patch record in xemu/xemu-patches.
# The output is xemu/dist, which the shell launches as its core.
param(
    [int]$Jobs = 12,
    [switch]$Clean,
    [switch]$NoVulkan
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tree = Join-Path $root 'xemu'
$log = Join-Path $root 'wt-data/xemu-build.log'
if (-not (Test-Path $tree)) {
    throw "Missing $tree; the vendored tree is part of this repository."
}

New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

Write-Host "Building native xemu core (MSYS2 MINGW64)..."
Write-Host "  log: $log"
$buildStarted = Get-Date

# Convert D:\repo\CSharp_XGrid to /d/repo/CSharp_XGrid for the MSYS2 shell.
$drive = $root.Substring(0, 1).ToLowerInvariant()
$rest = $root.Substring(2).Replace('\', '/')
$rootPosix = "/$drive$rest"

$cleanStep = if ($Clean) { 'rm -rf build' } else { '' }
# Vulkan is part of the release build. Use -NoVulkan for an OpenGL-only core.
$vulkanOption = if ($NoVulkan) { '-Dxgrid_vulkan=false' } else { '-Dxgrid_vulkan=true' }
$script = @"
export MSYSTEM=MINGW64
# xemu's build.sh drives `make`; this host has ninja but no make, so provide a
# repo-local shim that forwards the same targets/options to ninja.
mkdir -p $rootPosix/wt-data/bin
printf '#!/bin/sh\nexec /mingw64/bin/ninja -C build "`$@"\n' > $rootPosix/wt-data/bin/make
chmod +x $rootPosix/wt-data/bin/make
export PATH=$rootPosix/wt-data/bin:/c/cmake/bin:/mingw64/bin:/usr/bin:`$PATH
cd $rootPosix/xemu
$cleanStep
echo "[xemu] configuring and building (full log: $rootPosix/wt-data/xemu-build.log)"
./build.sh -j$Jobs --disable-werror --disable-docs --disable-guest-agent \
  --disable-vnc --disable-spice --disable-gtk --disable-sdl-image \
  --disable-libssh --disable-curses --disable-brlapi -Dzstd=enabled --enable-tools --disable-curl $vulkanOption 2>&1 \
  | tee $rootPosix/wt-data/xemu-build.log \
  | grep --line-buffered -E '^\[[0-9]+/[0-9]+\]|^Configuring|^Building|^Linking|^Compiling|^Generating|^ERROR|^FAILED|error:|no work to do'
status=`${PIPESTATUS[0]}
# The shell loads the core as a DLL so the loader initialises its CRT.
if [ `$status -eq 0 ]; then
  echo "[xemu] building xgrid_core.dll"
  ninja -C build xgrid_core.dll >> $rootPosix/wt-data/xemu-build.log 2>&1 || status=`$?
fi
# The media checks read snapshot lists and disc descriptors with the QEMU
# tools; they live in the object tree (copying them into dist would ship them
# in the release archive).
if [ `$status -eq 0 ]; then
  echo "[xemu] building qemu-img and qemu-io"
  ninja -C build qemu-img.exe qemu-io.exe >> $rootPosix/wt-data/xemu-build.log 2>&1 || status=`$?
fi
if [ `$status -eq 0 ]; then
  cp -f build/xgrid_core.dll dist/
fi
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
  for d in dist/*.dll; do
    case "`$d" in *xgrid_core.dll) continue;; esac
    copy_deps "`$d"
  done
done
echo "EXIT=`$status" >> $rootPosix/wt-data/xemu-build.log
exit `$status
"@
$scriptPath = Join-Path $root 'wt-data/build-xemu.sh'
Set-Content -Path $scriptPath -Value $script -NoNewline
& 'C:\msys64\usr\bin\bash.exe' -lc "bash '$rootPosix/wt-data/build-xemu.sh'"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Native core build FAILED. Last log lines:" -ForegroundColor Red
    Get-Content $log -Tail 40
    throw "Core build failed (see $log)."
}

$elapsed = (Get-Date) - $buildStarted
Write-Host ("Native core build finished in {0:N1} s." -f $elapsed.TotalSeconds)

$exe = Join-Path $tree 'dist/xemu.exe'
if (-not (Test-Path $exe)) { throw "Build finished but $exe is missing." }

# The shell offers only the renderers this build can actually start, so the
# build writes what it compiled beside the core. `CONFIG_VULKAN` is a meson
# feature: when the Vulkan dependencies are absent it stays undefined.
$configHeader = Join-Path $tree 'build/config-host.h'
$hasVulkan = (Test-Path $configHeader) -and
    [bool](Select-String -Path $configHeader -Pattern '^#define CONFIG_VULKAN\b' -Quiet)
$version = if (Test-Path (Join-Path $tree 'XEMU_VERSION')) {
    (Get-Content (Join-Path $tree 'XEMU_VERSION') -TotalCount 1).Trim()
}
else { '' }
$renderers = @('OPENGL')
if ($hasVulkan) { $renderers += 'VULKAN' }
$renderers += 'NULL'
[ordered]@{ core = 'xemu'; version = $version; renderers = $renderers } |
    ConvertTo-Json |
    Set-Content -Path (Join-Path $tree 'dist/xgrid-core.json') -Encoding utf8

Write-Output ("core: {0} ({1:N1} MB, renderers {2})" -f $exe, ((Get-Item $exe).Length / 1MB), ($renderers -join ', '))
