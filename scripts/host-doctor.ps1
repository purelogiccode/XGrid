<#
.SYNOPSIS
Session-start preflight for a machine that may not be the one the plan was
written on.

.DESCRIPTION
Development runs on two machines sharing one Dropbox tree (see `docs/hosts.md`).
Their free space, toolchains, media and absolute walls differ, and past sessions
have confused that with a code regression. This script prints the fingerprint and
checks the preconditions the plan assumes, so an environment mismatch is a line of
output instead of a lost session.

It changes nothing. Exit 0 = ready, 1 = blocking problem, 2 = cannot start.

.PARAMETER SkipRefsHash
Skip the References2 content hash (counts and sizes only; much faster).

.EXAMPLE
pwsh -File scripts/host-doctor.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipRefsHash
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$repo = Split-Path $PSScriptRoot -Parent
$problems = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'

function Write-Section([string]$Title) {
    Write-Host ''
    Write-Host $Title -ForegroundColor Cyan
}

# --- fingerprint -------------------------------------------------------------
Write-Section 'HOST'
$os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction SilentlyContinue
$cpu = Get-CimInstance -ClassName Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
$ram = if ($os) { [math]::Round($os.TotalVisibleMemorySize / 1MB, 1) } else { 0 }

Write-Host ("  machine      {0}" -f $env:COMPUTERNAME)
Write-Host ("  user         {0}" -f $env:USERNAME)
Write-Host ("  os           {0}" -f ($(if ($os) { $os.Caption + ' ' + $os.Version } else { 'unknown' })))
Write-Host ("  cpu          {0}" -f ($(if ($cpu) { $cpu.Name.Trim() } else { 'unknown' })))
Write-Host ("  cores        {0} logical" -f $env:NUMBER_OF_PROCESSORS)
Write-Host ("  ram          {0} GB" -f $ram)
Write-Host ("  powershell   {0}" -f $PSVersionTable.PSVersion.ToString())
Write-Host ("  repo         {0}" -f $repo)
Write-Host '  -> record this block in docs/hosts.md if the machine is new'

# --- dotnet ------------------------------------------------------------------
Write-Section 'TOOLCHAIN'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $sdk = (& dotnet --version) 2>$null
    Write-Host ("  dotnet       {0}" -f $sdk)
    if ($sdk -notmatch '^10\.') { $warnings.Add("dotnet SDK $sdk is not 10.x (plan expects 10.0.301 on H1)") }
}
else { $problems.Add('dotnet not on PATH'); Write-Host '  dotnet       MISSING' -ForegroundColor Red }

foreach ($tool in 'git', 'gcc', 'clang', 'make', 'bash') {
    $c = Get-Command $tool -ErrorAction SilentlyContinue
    if ($c) { Write-Host ("  {0,-12} {1}" -f $tool, $c.Source) }
    else { Write-Host ("  {0,-12} MISSING" -f $tool) -ForegroundColor DarkGray }
}
if (-not (Get-Command clang -ErrorAction SilentlyContinue)) {
    $warnings.Add('clang/make absent: the nxdk corpus rows (M-B) cannot be built on this machine')
}

# --- disk --------------------------------------------------------------------
Write-Section 'DISK'
foreach ($drive in Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name -in 'C', 'D', 'H' }) {
    $free = [math]::Round($drive.Free / 1GB, 1)
    Write-Host ("  {0}: free      {1} GB" -f $drive.Name, $free)
    if ($drive.Name -eq 'C' -and $free -lt 5) { $problems.Add("C: has $free GB free; builds and 1B runs need more") }
}

# --- references --------------------------------------------------------------
Write-Section 'REFERENCES2'
$refs = Join-Path $repo 'References2'
if (-not (Test-Path -LiteralPath $refs)) {
    $problems.Add('References2/ is missing entirely; recover it before any reference work')
    Write-Host '  References2  MISSING' -ForegroundColor Red
}
else {
    $sets = @(Get-ChildItem -LiteralPath $refs -Directory -Force | Sort-Object Name)
    $total = 0
    foreach ($s in $sets) { $total += @(Get-ChildItem -LiteralPath $s.FullName -Recurse -File -Force).Count }
    Write-Host ("  sets         {0}" -f (($sets | ForEach-Object { $_.Name }) -join ', '))
    Write-Host ("  files        {0}" -f $total)

    $manifestScript = Join-Path $PSScriptRoot 'refs-manifest.ps1'
    if (Test-Path -LiteralPath $manifestScript) {
        $pwshArgs = @('-NoProfile', '-File', $manifestScript, '-Verify')
        if ($SkipRefsHash) { $pwshArgs += '-Quick' }
        & pwsh @pwshArgs 2>&1 | ForEach-Object { Write-Host ("  " + $_) }
        if ($LASTEXITCODE -ne 0) { $problems.Add('References2 integrity check failed') }
    }
    else {
        $warnings.Add('scripts/refs-manifest.ps1 not found; integrity not verified')
    }
}

# --- gate media --------------------------------------------------------------
Write-Section 'GATE MEDIA'
$media = @(
    'data\xemu-0.8.136-windows-x86_64\Complex_4627.bin',
    'data\xemu-0.8.136-windows-x86_64\mcpx_1.0.bin',
    'data\xemu-0.8.136-windows-x86_64\eeprom.bin',
    'data\test-samples\triangle.xbe',
    'data\test-samples\kernel-test-suite-default.xbe',
    'wt-data\chain-199l\hdd.qcow2',
    'wt-data\xemu-tri\triangle.iso'
)
foreach ($m in $media) {
    $p = Join-Path $repo $m
    if (Test-Path -LiteralPath $p) { Write-Host ("  ok      {0}" -f $m) }
    else {
        Write-Host ("  MISSING {0}" -f $m) -ForegroundColor Yellow
        $warnings.Add("gate media missing: $m")
    }
}

# --- real-game corpus (benchmark-only) ---------------------------------------
Write-Section 'REAL-ISO CORPUS (benchmark-only, never gates)'
$isos = 'H:\Tests\XBOXTest'
if (Test-Path -LiteralPath $isos) {
    $count = @(Get-ChildItem -LiteralPath $isos -File -Filter *.iso).Count
    Write-Host ("  {0}  {1} iso" -f $isos, $count)
}
else { Write-Host ("  {0}  absent on this machine" -f $isos) -ForegroundColor DarkGray }

# --- sibling repo (ktest smoke) ----------------------------------------------
Write-Section 'SIBLING REPO (XISOSharp, ktest smoke gate)'
$sibling = Join-Path (Split-Path $repo -Parent) 'CSharp_XISOSharp\XISOSharp.Cli\bin\Release'
$xiso = $null
foreach ($tfm in 'net10.0', 'net9.0', 'net8.0') {
    $candidate = Join-Path $sibling "$tfm\XISOSharp.exe"
    if (Test-Path -LiteralPath $candidate) { $xiso = $candidate; break }
}
if ($xiso) { Write-Host ("  ok      {0}" -f $xiso) }
else {
    Write-Host '  MISSING in the sibling repo; pass -XisoSharp to ktest-smoke.ps1' -ForegroundColor Yellow
    $warnings.Add('XISOSharp not found; the ktest smoke gate needs -XisoSharp')
}

# --- verdict -----------------------------------------------------------------
Write-Section 'VERDICT'
if ($problems.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host '  READY' -ForegroundColor Green
    exit 0
}
foreach ($w in $warnings) { Write-Host ("  warn  {0}" -f $w) -ForegroundColor Yellow }
foreach ($p in $problems) { Write-Host ("  FAIL  {0}" -f $p) -ForegroundColor Red }
if ($problems.Count -gt 0) { exit 1 }
Write-Host '  READY (with warnings)' -ForegroundColor Green
exit 0
