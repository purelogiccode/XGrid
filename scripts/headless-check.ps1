# Headless smoke check: boots the core without the shell, renders off-screen,
# writes a JSON report and verifies that frames were published.
#
# Usage:
#   pwsh -File scripts/headless-check.ps1
#   pwsh -File scripts/headless-check.ps1 -Seconds 30 -Disc 'D:\Games\Halo.iso'
#   pwsh -File scripts/headless-check.ps1 -Keep -DataDir 'D:\Bench\data'
#
# Requires a built core (scripts/build-native-xemu.ps1) and a Release build of
# the frontend. A private copy of the HDD is used unless -Hdd points at one.
param(
    [int]$Seconds = 30,
    [string]$Disc = '',
    [string]$WorkDir = '',
    [string]$DataDir = '',
    [string]$Core = '',
    [string]$Frontend = '',
    [string]$Hdd = '',
    [string]$Bios = '',
    [string]$Mcpx = '',
    [string]$Eeprom = '',
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($WorkDir)) {
    $WorkDir = Join-Path $root 'wt-data/headless-check'
}

if ([string]::IsNullOrWhiteSpace($Core)) { $Core = Join-Path $root 'xemu/dist/xemu.exe' }
if ([string]::IsNullOrWhiteSpace($Frontend)) {
    $Frontend = Join-Path $root 'src/XGrid.Frontend.Avalonia/bin/Release/net11.0/XGrid.Frontend.Avalonia.exe'
}
if ([string]::IsNullOrWhiteSpace($Bios)) { $Bios = Join-Path $root 'data/xemu/Complex_4627.bin' }
if ([string]::IsNullOrWhiteSpace($Mcpx)) { $Mcpx = Join-Path $root 'data/xemu/mcpx_1.0.bin' }
if ([string]::IsNullOrWhiteSpace($Eeprom)) { $Eeprom = Join-Path $root 'data/xemu/eeprom.bin' }
if ([string]::IsNullOrWhiteSpace($DataDir)) { $DataDir = Join-Path $WorkDir 'data' }

foreach ($required in @($Core, $Frontend, $Bios, $Mcpx, $Eeprom)) {
    if (-not (Test-Path $required)) {
        throw "Missing $required. Build the core and the Release frontend first."
    }
}

if (-not [string]::IsNullOrWhiteSpace($Disc) -and -not (Test-Path $Disc)) {
    throw "Missing disc image $Disc"
}

if ([string]::IsNullOrWhiteSpace($Hdd)) {
    $sourceHdd = Join-Path $root 'data/xemu/xbox_hdd.qcow2'
    if (-not (Test-Path $sourceHdd)) {
        throw "Missing $sourceHdd; pass -Hdd or add the development HDD image."
    }

    $Hdd = Join-Path $WorkDir 'hdd.qcow2'
    New-Item -ItemType Directory -Force $WorkDir | Out-Null
    Write-Output "copying the hard disk image to $Hdd ..."
    Copy-Item $sourceHdd $Hdd -Force
}
elseif (-not (Test-Path $Hdd)) {
    throw "Missing hard disk image $Hdd"
}

if (-not $Keep) {
    Remove-Item $DataDir -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force $DataDir | Out-Null
$summary = Join-Path $WorkDir 'headless-report.json'
$stdout = Join-Path $WorkDir 'headless-stdout.txt'
$stderr = Join-Path $WorkDir 'headless-stderr.txt'

function Quote([string]$value) { return '"' + $value + '"' }

$arguments = @(
    '--headless',
    '--seconds', $Seconds,
    '--summary', (Quote $summary),
    '--data-dir', (Quote $DataDir),
    '--core', (Quote $Core),
    '--bios', (Quote $Bios),
    '--mcpx', (Quote $Mcpx),
    '--eeprom', (Quote $Eeprom),
    '--hdd', (Quote $Hdd)
)
if (-not [string]::IsNullOrWhiteSpace($Disc)) {
    $arguments += @('--disc', (Quote $Disc))
}

$started = Get-Date
$process = Start-Process -FilePath $Frontend -ArgumentList $arguments -Wait -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$elapsed = ((Get-Date) - $started).TotalSeconds

Get-Content $stdout -ErrorAction SilentlyContinue | Write-Output
Write-Output ("process: exit={0} elapsed={1:N1}s" -f $process.ExitCode, $elapsed)

if (-not (Test-Path $summary)) {
    Get-Content $stderr -ErrorAction SilentlyContinue | Select-Object -Last 20 | Write-Output
    throw 'The headless run did not write a report.'
}

$report = Get-Content $summary -Raw | ConvertFrom-Json
$report | ConvertTo-Json -Depth 4 | Write-Output
Write-Output ("report: result={0} frames={1} seconds={2:N2} fps={3:N2} stop={4}" -f `
    $report.result, $report.frames, $report.seconds, $report.fps, $report.stop)

if ($report.result -ne 0) {
    Get-Content $stderr -ErrorAction SilentlyContinue | Select-Object -Last 20 | Write-Output
    throw "The headless run failed with result $($report.result)."
}

if ($report.frames -le 0) {
    throw 'The headless run published no frames.'
}

Write-Output 'RESULT: PASS'
