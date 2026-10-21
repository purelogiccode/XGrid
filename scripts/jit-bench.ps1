# jit-bench.ps1 - repeatable fast-mode JIT bench (§§191-192).
#
# Runs the two 1B golden workloads with --jit (and, with -Controls, the
# accurate --spec-exec control), asserts LastFault vec=0 on every leg,
# extracts the BENCH line (wall/msps/RAM SHA) and reports the median.
#
# Notes:
#   - Fast mode is deterministic for a fixed binary + flags, but its SHAs
#     differ from the accurate control by IRQ-position timing (charter §2).
#     This script therefore asserts faults and BENCH presence, never
#     cross-mode SHA equality. Use --jit-exact for bit-exact runs.
#   - Absolutes are host-sensitive (§176/§192): quote same-window ratios.
#
# Usage (from the repo root, after dotnet build -c Release):
#   pwsh -File scripts/jit-bench.ps1 [-Steps 1000000000] [-Threshold 16]
#        [-Reps 3] [-Controls] [-SkipTriangle] [-SkipMenu]

param(
    [ulong]$Steps = 1000000000,
    [int]$Threshold = 16,
    [int]$Reps = 3,
    [switch]$Controls,
    [switch]$SkipTriangle,
    [switch]$SkipMenu
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$cli = "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
if (-not (Test-Path $cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }

$media = "D:\spec-soak\media"
$triangleIso = Join-Path $root "wt-data\xemu-tri\triangle.iso"
$outDir = "wt-data\jit-bench"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$firmware = @(
    "--bios", (Join-Path $media "Complex_4627.bin"),
    "--mcpx", (Join-Path $media "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $media "eeprom.bin"),
    "--hdd", (Join-Path $media "xbox_hdd.qcow2")
)
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"

function Invoke-Leg([string]$name, [string[]]$extra) {
    $log = Join-Path $outDir "$name.log"
    $argsList = @($cli, "boot") + $firmware + $extra + @(
        "--steps", "$Steps", "--gpu", "sw", "--bench", "--bench-name", $name
    )
    & dotnet @argsList *>&1 | Tee-Object -FilePath $log | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "run failed exit=$LASTEXITCODE ($name)"; exit 1 }

    $bench = Select-String -Path $log -Pattern "BENCH name=$name steps=(\d+) wall=([0-9.]+) msps=([0-9.]+)" |
        Select-Object -Last 1
    if (-not $bench) { Write-Error "no BENCH line ($name)"; exit 1 }
    $fault = Select-String -Path $log -Pattern "LastFault vec=(\d+)" | Select-Object -Last 1
    if (-not $fault -or $fault.Matches[0].Groups[1].Value -ne "0") {
        Write-Error "guest fault ($name): $($fault.Line)"
        exit 1
    }

    return [pscustomobject]@{
        Name = $name
        Wall = [double]$bench.Matches[0].Groups[2].Value
        Msps = [double]$bench.Matches[0].Groups[3].Value
    }
}

function Get-Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return $sorted[[int]($n / 2)] }
    return ($sorted[$n / 2 - 1] + $sorted[$n / 2]) / 2
}

$results = @()

function Run-Workload([string]$label, [string[]]$mediaArgs, [string[]]$modeArgs) {
    $walls = @()
    $msps = @()
    for ($i = 1; $i -le $Reps; $i++) {
        $r = Invoke-Leg "$label-r$i" ($mediaArgs + $modeArgs)
        $walls += $r.Wall
        $msps += $r.Msps
        Write-Host ("  {0} run {1}: wall={2:F3}s msps={3:F2}" -f $label, $i, $r.Wall, $r.Msps)
    }

    $script:results += [pscustomobject]@{
        Workload = $label
        MedianWall = Get-Median $walls
        MedianMsps = Get-Median $msps
    }
}

$modes = @()
$modes += , @("fast", @("--jit", "--jit-threshold", "$Threshold"))
if ($Controls) {
    $modes += , @("ctl", @("--spec-exec"))
}

if (-not $SkipTriangle) {
    foreach ($mode in $modes) {
        Run-Workload "tri-$($mode[0])" @("--dvd", $triangleIso) $mode[1]
    }
}

if (-not $SkipMenu) {
    foreach ($mode in $modes) {
        Run-Workload "menu-$($mode[0])" @("--pad-script", $padScript) $mode[1]
    }
}

Write-Host ""
Write-Host ("JIT bench: steps=$Steps threshold=$Threshold reps=$Reps")
$results | ForEach-Object {
    Write-Host ("  {0,-10} median wall={1,7:F3}s  msps={2,7:F2}" -f $_.Workload, $_.MedianWall, $_.MedianMsps)
}
$results | ConvertTo-Json | Set-Content (Join-Path $outDir "results.json")
Write-Host "logs+results: $outDir"
