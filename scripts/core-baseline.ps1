# Verify (or refresh) the core baseline the shell is built against.
#
# The shell depends on the in-process ABI and on the xemu version/commit pair.
# A rebase must be deliberate: this check names every drift instead of letting
# a silently changed core break the shell at runtime.
param(
    [switch]$Refresh
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$xemu = Join-Path $root 'xemu'
$baselinePath = Join-Path $xemu 'xgrid-core/abi-baseline.json'
$headerPath = Join-Path $xemu 'xgrid-core/abi/xgrid-core.h'
$versionPath = Join-Path $xemu 'XEMU_VERSION'
$commitPath = Join-Path $xemu 'XEMU_COMMIT'
$manifestPath = Join-Path $xemu 'dist/xgrid-core.json'
$libraryPath = Join-Path $xemu 'dist/xgrid_core.dll'

foreach ($required in $headerPath, $versionPath, $commitPath) {
    if (-not (Test-Path $required)) { throw "missing $required" }
}

$version = (Get-Content $versionPath -TotalCount 1).Trim()
$commit = (Get-Content $commitPath -TotalCount 1).Trim()
$abi = @(foreach ($line in Get-Content $headerPath) {
    if ($line -cmatch '^\s*XGRID_CORE_EXPORT\b' -and $line -cmatch '\b(xgrid_core_[a-z0-9_]+)\s*\(') {
        $Matches[1]
    }
}) | Sort-Object -Unique

if ($abi.Count -eq 0) { throw "no xgrid_core_* exports found in $headerPath" }

if ($Refresh) {
    $baseline = [ordered]@{ version = $version; commit = $commit; abi = $abi }
    $baseline | ConvertTo-Json -Depth 4 | Set-Content -Path $baselinePath -Encoding utf8
    Write-Host ("baseline written: xemu {0} {1}, {2} ABI exports" -f $version, $commit.Substring(0, 12), $abi.Count)
    exit 0
}

if (-not (Test-Path $baselinePath)) { throw "missing $baselinePath (run with -Refresh)" }
$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json
$problems = [System.Collections.Generic.List[string]]::new()

if ($baseline.version -ne $version) {
    $problems.Add("xemu version is $version, baseline expects $($baseline.version)")
}
if ($baseline.commit -ne $commit) {
    $problems.Add("xemu commit is $commit, baseline expects $($baseline.commit)")
}

$expected = @($baseline.abi | Sort-Object -Unique)
$added = @($abi | Where-Object { $expected -notcontains $_ })
$removed = @($expected | Where-Object { $abi -notcontains $_ })
foreach ($name in $added) { $problems.Add("ABI export added: $name") }
foreach ($name in $removed) { $problems.Add("ABI export removed: $name") }

if (Test-Path $libraryPath) {
    $objdump = 'C:\msys64\mingw64\bin\objdump.exe'
    if (Test-Path $objdump) {
        $dump = & $objdump -p $libraryPath 2>$null | Out-String
        $built = @([regex]::Matches($dump, '(xgrid_core_[A-Za-z0-9_]+)') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        foreach ($name in $abi) {
            if ($built -notcontains $name) {
                $problems.Add("built core does not export $name")
            }
        }
    }
    else {
        Write-Host '  note: objdump not found; the ABI export check used the header only'
    }
}
else {
    Write-Host '  note: xemu/dist/xgrid_core.dll not built; the ABI export check used the header only'
}

if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -and $manifest.version -ne $version) {
        $problems.Add("xgrid-core.json version is $($manifest.version), expected $version")
    }
}

if ($problems.Count -gt 0) {
    Write-Host 'CORE BASELINE FAIL'
    foreach ($problem in $problems) { Write-Host "  - $problem" }
    Write-Host 'If the change is deliberate, re-run scripts/core-baseline.ps1 -Refresh, then re-run the media boot checks.'
    exit 1
}

Write-Host ("CORE BASELINE OK: xemu {0} {1}, {2} ABI exports" -f $version, $commit.Substring(0, 12), $abi.Count)
