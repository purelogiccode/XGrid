<#
.SYNOPSIS
Builds the standalone xemu DSP56300 reference interpreter and (optionally)
regenerates the committed differential vectors.

.DESCRIPTION
Development tool only: nothing here is part of the XGrid build or
distribution.  The reference sources stay in References2/ (read-only); this
script writes a guarded copy into tools/dsp-oracle/build/ and compiles it with
the host gcc.

.EXAMPLE
pwsh -File tools/dsp-oracle/build.ps1
pwsh -File tools/dsp-oracle/build.ps1 -Regenerate
#>
[CmdletBinding()]
param(
    [switch]$Regenerate
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here "..\..")
$ref = Join-Path $root "References2\xemu-0.8.139\hw\xbox\mcpx\apu\dsp"
$build = Join-Path $here "build"

if (-not (Test-Path $ref)) {
    throw "reference not found: $ref"
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
python (Join-Path $here "prepare.py")
if ($LASTEXITCODE -ne 0) { throw "prepare.py failed" }

$gcc = (Get-Command gcc -ErrorAction SilentlyContinue)
if (-not $gcc) { throw "gcc not found on PATH" }

$exe = Join-Path $build "oracle.exe"
& $gcc.Source -O1 -w -DNDEBUG `
    -I (Join-Path $here "shims") `
    -I $ref -I (Join-Path $ref "interp") `
    -o $exe `
    (Join-Path $here "oracle.c") `
    (Join-Path $build "dsp_cpu_oracle.c")
if ($LASTEXITCODE -ne 0) { throw "gcc failed" }
Write-Host "built $exe"

if ($Regenerate) {
    python (Join-Path $here "gen_vectors.py")
    if ($LASTEXITCODE -ne 0) { throw "gen_vectors.py failed" }
}
