# Track B present-path acceptance (M-A) - the measured ceremony in one command.
#
# Runs N interleaved pairs of the SAME build: `--gpu sw` (the host reference)
# and `--gpu gl --gpu-replace` (the present path under test) on the menu and the
# triangle, then prints the medians, the GL submission/readback timings and the
# quiet-host projection against the recorded references below.
#
# Why interleaved same-build pairs: this host is often 1.5-2x slower than the
# quiet session that recorded the references (Rider indexing), so absolute walls
# are only comparable within a window. The sw leg measures the host factor and
# the gl/sw ratio is the durable number.
#
# Recorded quiet references (2026-10-20, `4f0bd47` build, direction doc §2):
#   menu   sw 33.3 s   gl+replace 31.1 s
#   triangle sw 8.69 s gl+replace 6.49 s
# M-A target: menu gl <= 24 s, triangle gl <= 6 s at 1B.
#
# Usage (from the repo root, after dotnet build -c Release):
#   pwsh -File scripts/present-path-acceptance.ps1
#   pwsh -File scripts/present-path-acceptance.ps1 -Pairs 5 -Workload menu
#   pwsh -File scripts/present-path-acceptance.ps1 -Cli <other tree>\...\XGrid.Cli.dll

param(
    [ValidateSet("menu", "tri", "both")][string]$Workload = "both",
    [int]$Pairs = 3,
    [ulong]$Steps = 1000000000,
    [string]$Cli = "",
    [string]$OutDir = "wt-data\acceptance",
    [string]$FirmwareDir = "data\xemu-0.8.136-windows-x86_64",
    [string]$Hdd = "wt-data\chain-199l\hdd.qcow2",
    [string]$TriangleIso = "wt-data\xemu-tri\triangle.iso"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"

$quiet = @{
    menu = @{ sw = 33.3; gl = 31.1; target = 24.0 }
    tri  = @{ sw = 8.69; gl = 6.49; target = 6.0 }
}

function Run-Leg([string]$workload, [string]$gpu, [string]$tag) {
    $log = Join-Path $OutDir "$workload-$gpu-$tag.log"
    $argv = @(
        "boot",
        "--bios", (Join-Path $FirmwareDir "Complex_4627.bin"),
        "--mcpx", (Join-Path $FirmwareDir "mcpx_1.0.bin"),
        "--eeprom", (Join-Path $FirmwareDir "eeprom.bin")
    )
    if ($workload -eq "menu") {
        $argv += @("--hdd", $Hdd, "--pad-script", $padScript)
    }
    else {
        # Gate media (HDD + DVD). DVD only never launches the disc's XBE, so it
        # measures the boot animation instead of the triangle (decisions 208).
        $argv += @("--hdd", $Hdd, "--dvd", $TriangleIso)
    }

    $argv += @("--jit", "--jit-threshold", "16", "--jit-spin", "--jit-raw",
        "--steps", "$Steps", "--gpu", $gpu, "--gpu-time")
    if ($gpu -eq "gl") { $argv += "--gpu-replace" }
    $argv += @("--bench", "--bench-name", "$workload-$gpu")

    & dotnet $Cli @argv *>&1 | Out-File $log
    $bench = (Select-String -Path $log -Pattern "^BENCH name=" | Select-Object -Last 1).Line
    if (-not $bench) { throw "no BENCH line in $log" }
    $glLine = (Select-String -Path $log -Pattern "^GPU backend=gl draws=" | Select-Object -Last 1).Line
    $fault = (Select-String -Path $log -Pattern "^LastFault" | Select-Object -Last 1).Line
    [pscustomobject]@{
        Wall   = [double][regex]::Match($bench, "wall=([0-9.]+)").Groups[1].Value
        Ram    = [regex]::Match($bench, "ramSha256=([0-9A-F]{8})").Groups[1].Value
        Cpu    = [regex]::Match($bench, "cpuSha256=([0-9A-F]{8})").Groups[1].Value
        Draw   = [double][regex]::Match($glLine, "drawMs=([0-9.]+)").Groups[1].Value
        Clear  = [double][regex]::Match($glLine, "clearMs=([0-9.]+)").Groups[1].Value
        Read   = [double][regex]::Match($glLine, "readbackMs=([0-9.]+)").Groups[1].Value
        Unsup  = [regex]::Match($glLine, "unsupported=(\d+)").Groups[1].Value
        Fault  = $fault
        Log    = $log
    }
}

function Median($values) {
    $s = @($values | Sort-Object)
    $n = $s.Count
    if ($n % 2 -eq 1) { return $s[[int]($n / 2)] }
    return ($s[$n / 2 - 1] + $s[$n / 2]) / 2
}

$workloads = if ($Workload -eq "both") { @("menu", "tri") } else { @($Workload) }
$fail = $false
foreach ($w in $workloads) {
    $swWalls = @()
    $glWalls = @()
    $ratios = @()
    for ($i = 1; $i -le $Pairs; $i++) {
        $s = Run-Leg $w "sw" "p$i"
        $g = Run-Leg $w "gl" "p$i"
        $swWalls += $s.Wall
        $glWalls += $g.Wall
        $ratios += $g.Wall / $s.Wall
        $shaOk = ($s.Cpu -eq $g.Cpu)
        if (-not $shaOk) { $fail = $true }
        "pair {0}: sw wall={1:F2} ({2}/{3}) | gl wall={4:F2} draw={5:F1}ms read={6:F1}ms unsup={7} ({8}/{9}) | gl/sw={10:F4}" -f `
            $i, $s.Wall, $s.Ram, $s.Cpu, $g.Wall, $g.Draw, $g.Read, $g.Unsup, $g.Ram, $g.Cpu, ($g.Wall / $s.Wall)
    }

    $swMed = Median $swWalls
    $glMed = Median $glWalls
    $ratioMed = Median $ratios
    $proj = $quiet[$w].sw * $ratioMed
    $target = $quiet[$w].target
    $verdict = if ($proj -le $target) { "MEETS M-A" } else { "over by {0:F1} s" -f ($proj - $target) }
    "SUMMARY {0}: sw med={1:F2} gl med={2:F2} ratio med={3:F4} ({4}/{5} pairs won) -> quiet projection {6:F1} s vs target {7:F1} s: {8}" -f `
        $w, $swMed, $glMed, $ratioMed, ($ratios | Where-Object { $_ -lt 1 }).Count, $Pairs,
        $proj, $target, $verdict
}

if ($fail) { "ACCEPTANCE: FAIL (leg SHA mismatch)"; exit 1 }
"ACCEPTANCE: measured (see the SUMMARY lines; quiet-host absolutes need an idle host)"
exit 0
