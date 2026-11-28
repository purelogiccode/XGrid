# Record or verify the MSYS2 MINGW64 packages that build and ship the Windows core.
#
# The Windows release links SDL3, GLib, OpenSSL, zlib, zstd, LZ4 and their
# transitive DLLs from the host MSYS2 installation. Recording the package set
# makes the shipped binary closure reviewable; the Meson fallback pins do not
# describe it.
#
# Usage:
#   pwsh -File scripts/msys2-lock.ps1            # compare installed packages
#   pwsh -File scripts/msys2-lock.ps1 -Write     # refresh the lock after review
#
# Run this on the Windows build host, not on a Linux CI runner.
param(
    [switch]$Write,
    [string]$Pacman = '',
    [string]$Lock = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$lockPath = if ([string]::IsNullOrWhiteSpace($Lock)) {
    Join-Path $root 'xemu/msys2-packages.lock'
}
elseif ([System.IO.Path]::IsPathRooted($Lock)) {
    $Lock
}
else {
    Join-Path $root $Lock
}

$pacmanPath = if (-not [string]::IsNullOrWhiteSpace($Pacman)) {
    $Pacman
}
elseif (Test-Path 'C:\msys64\usr\bin\pacman.exe') {
    'C:\msys64\usr\bin\pacman.exe'
}
else {
    $command = Get-Command pacman -ErrorAction SilentlyContinue
    if ($command) { $command.Source } else { '' }
}
if (-not $pacmanPath -or -not (Test-Path $pacmanPath)) {
    throw 'pacman was not found; install MSYS2 or pass -Pacman <path>.'
}

# Build tools and the direct runtime libraries that the Windows core links or
# redistributes. Transitive packages (nghttp2, idn2, brotli, ...) ship too, so
# they are recorded as well.
$packages = @(
    'mingw-w64-x86_64-brotli'
    'mingw-w64-x86_64-cmake'
    'mingw-w64-x86_64-curl'
    'mingw-w64-x86_64-gcc'
    'mingw-w64-x86_64-gcc-libs'
    'mingw-w64-x86_64-gettext-runtime'
    'mingw-w64-x86_64-glib2'
    'mingw-w64-x86_64-gmp'
    'mingw-w64-x86_64-libepoxy'
    'mingw-w64-x86_64-libiconv'
    'mingw-w64-x86_64-libidn2'
    'mingw-w64-x86_64-libpsl'
    'mingw-w64-x86_64-libsamplerate'
    'mingw-w64-x86_64-libssh2'
    'mingw-w64-x86_64-libusb'
    'mingw-w64-x86_64-libunistring'
    'mingw-w64-x86_64-libwinpthread'
    'mingw-w64-x86_64-lz4'
    'mingw-w64-x86_64-meson'
    'mingw-w64-x86_64-nghttp2'
    'mingw-w64-x86_64-nghttp3'
    'mingw-w64-x86_64-ngtcp2'
    'mingw-w64-x86_64-ninja'
    'mingw-w64-x86_64-openssl'
    'mingw-w64-x86_64-pcre2'
    'mingw-w64-x86_64-pixman'
    'mingw-w64-x86_64-pkgconf'
    'mingw-w64-x86_64-python'
    'mingw-w64-x86_64-sdl3'
    'mingw-w64-x86_64-winpthreads'
    'mingw-w64-x86_64-xxhash'
    'mingw-w64-x86_64-zlib'
    'mingw-w64-x86_64-zstd'
) | Sort-Object -Unique

$query = & $pacmanPath -Q 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "pacman -Q failed (exit $LASTEXITCODE):`n$($query -join "`n")"
}

$installed = @{}
foreach ($line in $query) {
    if ($line -match '^(\S+)\s+(\S+)$') {
        $installed[$Matches[1]] = $Matches[2]
    }
}

if ($Write) {
    $missing = @($packages | Where-Object { -not $installed.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        throw "cannot write the lock; install these MSYS2 packages first: $($missing -join ', ')"
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# MSYS2 MINGW64 package lock for the Windows XGrid core.')
    $lines.Add('# Generated ' + (Get-Date -Format 'yyyy-MM-dd') + ' by scripts/msys2-lock.ps1 -Write.')
    $lines.Add('# This is the build-host package set; it is not a Meson dependency lock.')
    $lines.Add('# Verify with: pwsh -File scripts/msys2-lock.ps1')
    $lines.Add('# Format: <package> <version>')
    foreach ($name in $packages) {
        $lines.Add("$name $($installed[$name])")
    }

    $directory = Split-Path -Parent $lockPath
    if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
    [System.IO.File]::WriteAllText(
        $lockPath,
        ($lines -join "`n") + "`n",
        [System.Text.UTF8Encoding]::new($false))
    Write-Host ("Wrote {0} packages to {1}" -f $packages.Count, $lockPath)
    exit 0
}

if (-not (Test-Path $lockPath)) {
    throw "missing $lockPath; run 'pwsh -File scripts/msys2-lock.ps1 -Write' on the build host."
}

$expected = @{}
$problems = [System.Collections.Generic.List[string]]::new()
foreach ($line in Get-Content $lockPath) {
    $text = $line.Trim()
    if (-not $text -or $text.StartsWith('#')) { continue }
    if ($text -match '^(\S+)\s+(\S+)$') {
        $expected[$Matches[1]] = $Matches[2]
    }
    else {
        $problems.Add("unparsable lock line: $line")
    }
}

foreach ($name in $packages) {
    if (-not $expected.ContainsKey($name)) {
        $problems.Add("lock does not record $name")
    }
    elseif (-not $installed.ContainsKey($name)) {
        $problems.Add("missing installed package $name")
    }
    elseif ($expected[$name] -ne $installed[$name]) {
        $problems.Add("$name is $($installed[$name]) but the lock records $($expected[$name])")
    }
}
foreach ($name in $expected.Keys) {
    if ($packages -notcontains $name) {
        $problems.Add("lock records $name, which is not in the package list")
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        Write-Host "FAIL: $problem"
    }
    Write-Host 'Refresh the lock with -Write only after reviewing the package changes.'
    exit 1
}

Write-Host ("MSYS2 lock OK: {0} packages match {1}" -f $packages.Count, $lockPath)
