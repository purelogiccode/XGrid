# Track E: build the nxdk corpus samples into bootable ISOs.
#
# The corpus rows in docs/compatibility.md are produced by
# scripts/boot-to-first-frame.ps1, which needs bootable media. nxdk samples are
# sources, so something has to compile them. This script is that something, and
# it is committed so a second machine can reproduce the corpus from scratch.
#
# Why nxdk's own samples and not the copies under data/test-samples: those are
# stale partial snapshots (the mesh copy was missing verts.h and texture.h, so
# it could never build). Upstream samples/<name> at a pinned commit is the
# authoritative, complete source.
#
# Usage (from the repo root):
#   pwsh -File scripts/build-nxdk-samples.ps1                 # build the default set
#   pwsh -File scripts/build-nxdk-samples.ps1 -Clone          # clone nxdk first
#   pwsh -File scripts/build-nxdk-samples.ps1 -Samples mesh,gamma
#
# Prerequisites on Windows (MSYS2, installs into C:\msys64):
#   pacman -S --needed git make bison flex cmake \
#            mingw-w64-x86_64-clang mingw-w64-x86_64-lld mingw-w64-x86_64-llvm
# The LLVM package matters: nxdk links with llvm-dlltool, which is NOT part of
# the clang package (a build fails with "llvm-dlltool: No such file").
#
# Exit 0 = every requested sample produced an ISO, 1 = a build failed,
# 2 = environment problem.

param(
    [string]$NxdkDir = "D:\nxdk",
    [string]$MsysRoot = "C:\msys64",
    [string]$OutDir = "data\test-samples\corpus",
    [string[]]$Samples = @("hello", "led", "mesh", "gamma", "gamma-fade", "triangle"),
    [string]$Repo = "https://github.com/XboxDev/nxdk",
    [switch]$Clone
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# D:\nxdk  ->  /d/nxdk  (MSYS2 path form)
function ConvertTo-MsysPath([string]$path) {
    $slashed = $path -replace '\\', '/'
    if ($slashed -match '^([A-Za-z]):(.*)$') {
        return '/' + $matches[1].ToLowerInvariant() + $matches[2]
    }
    return $slashed
}

$bash = Join-Path $MsysRoot "usr\bin\bash.exe"
$pacman = Join-Path $MsysRoot "usr\bin\pacman.exe"
if (-not (Test-Path $bash)) {
    Write-Error "MSYS2 not found at $MsysRoot (install it, then see the header of this script)"
    exit 2
}

if (-not (Test-Path (Join-Path $MsysRoot "mingw64\bin\llvm-dlltool.exe"))) {
    Write-Host "llvm-dlltool is missing. Install the toolchain with:"
    Write-Host "  & '$pacman' -S --needed --noconfirm git make bison flex cmake mingw-w64-x86_64-clang mingw-w64-x86_64-lld mingw-w64-x86_64-llvm"
    exit 2
}

if (-not (Test-Path (Join-Path $NxdkDir "Makefile"))) {
    if (-not $Clone) {
        Write-Error "nxdk not found at $NxdkDir; re-run with -Clone (or clone $Repo there yourself)"
        exit 2
    }

    Write-Host "Cloning $Repo -> $NxdkDir"
    & git clone --depth 1 $Repo $NxdkDir
    if ($LASTEXITCODE -ne 0) { Write-Error "git clone failed"; exit 2 }
    & git -C $NxdkDir submodule update --init --recursive --depth 1
    if ($LASTEXITCODE -ne 0) { Write-Error "submodule checkout failed"; exit 2 }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$msysNxdk = ConvertTo-MsysPath $NxdkDir
$msysMsys = ConvertTo-MsysPath $MsysRoot
$msysLog = ConvertTo-MsysPath (Join-Path $env:TEMP "xgrid-nxdk-build")
$sampleList = $Samples -join ' '

# Build inside the MSYS2 MinGW64 environment: nxdk's Makefile refuses to run
# without MSYSTEM=MINGW64, and the tools live under mingw64/bin.
$lines = @(
    'set -e',
    'export MSYSTEM=MINGW64',
    # bash started with a script (not a login shell) does not source
    # /etc/profile, so the MSYS2 tool dirs must be added explicitly: make and
    # the coreutils live in /usr/bin, the compiler in mingw64/bin.
    ("export PATH=/usr/bin:{0}/mingw64/bin:`$PATH" -f $msysMsys),
    ("export NXDK_DIR='{0}'" -f $msysNxdk),
    'export PATH=$NXDK_DIR/bin:$PATH',
    ("for n in {0}; do" -f $sampleList),
    '    cd "$NXDK_DIR/samples/$n"',
    ("    make -j4 > `"{0}-`$n.log`" 2>&1 || {{ echo `"BUILD-FAILED `$n`"; tail -15 `"{0}-`$n.log`"; exit 1; }}" -f $msysLog),
    '    echo "BUILT $n"',
    'done'
)
$scriptText = ($lines -join "`n") + "`n"
$tempScript = Join-Path $env:TEMP "xgrid-build-samples.sh"
[System.IO.File]::WriteAllText($tempScript, $scriptText)

Write-Host "Building $($Samples -join ', ') from $NxdkDir"
& $bash (ConvertTo-MsysPath $tempScript)
if ($LASTEXITCODE -ne 0) {
    Write-Error "sample build failed"
    exit 1
}

$built = 0
foreach ($sample in $Samples) {
    $iso = Join-Path $NxdkDir "samples\$sample\nxdk sample - $sample.iso"
    if (-not (Test-Path $iso)) {
        Write-Host "MISSING ISO: $iso"
        continue
    }

    Copy-Item -Force $iso (Join-Path $OutDir "$sample.iso")
    $built++
}

$commit = (& git -C $NxdkDir rev-parse --short HEAD) 2>$null
Write-Host "Staged $built/$($Samples.Count) ISO(s) into $OutDir (nxdk $commit)"
if ($built -ne $Samples.Count) { exit 1 }
exit 0
