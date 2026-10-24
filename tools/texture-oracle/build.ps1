# Builds the development-only S3TC oracle against the vendored xemu 0.8.139
# reference and (with -Regenerate) rewrites the differential vector file.
param(
    [switch]$Regenerate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pgraph = Join-Path $root "References2\xemu-0.8.139\hw\xbox\nv2a\pgraph"
$stub = Join-Path $PSScriptRoot "stub"
$out = Join-Path $env:TEMP "xgrid-s3tc-oracle.exe"
$vectors = Join-Path $root "tests\XGrid.Nv2a.Tests\TestData\s3tc-vectors.txt"

if (-not (Test-Path (Join-Path $pgraph "s3tc.c"))) {
    throw "missing reference s3tc.c under $pgraph"
}

& gcc -O2 -std=c11 -Wall -Wextra -I $stub -I $pgraph `
    (Join-Path $pgraph "s3tc.c") (Join-Path $PSScriptRoot "s3tc_oracle.c") -o $out
if ($LASTEXITCODE -ne 0) { throw "gcc failed" }

$lines = & $out
if ($LASTEXITCODE -ne 0) { throw "oracle failed" }

if ($Regenerate) {
    $dir = Split-Path -Parent $vectors
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -Path $vectors -Value $lines -Encoding ascii
    Write-Host "wrote $($lines.Count) vectors to $vectors"
} else {
    $lines | Select-Object -First 5
    Write-Host "($($lines.Count) vectors; pass -Regenerate to write $vectors)"
}
