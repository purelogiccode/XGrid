# 199l A/B: interleaved fast-IL vs fast-raw pairs (--gpu sw recipe).
# Reports, per workload: median wall for each mode, median outerMs, paired-win
# count and the ratio. Interleaving makes host drift cancel; ratios are the
# claim, absolutes are host-bound.
param(
    [ulong]$Steps = 100000000,
    [int]$Pairs = 5,
    [string[]]$Workloads = @("tri", "menu"),
    [string]$Tag = "ab100m"
)

$ErrorActionPreference = "Stop"
$Workloads = @($Workloads | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
$media = Join-Path $root "data\xemu-0.8.136-windows-x86_64"
$hdd = Join-Path $root "wt-data\chain-199l\hdd.qcow2"
$tri = Join-Path $root "wt-data\xemu-tri\triangle.iso"
$outDir = Join-Path $root "wt-data\chain-199l"
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"

$firmware = @(
    "--bios", (Join-Path $media "Complex_4627.bin"),
    "--mcpx", (Join-Path $media "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $media "eeprom.bin")
)

function Run-Leg([string]$name, [string]$workload, [bool]$raw) {
    $mediaArgs = if ($workload -eq "menu") { @("--hdd", $hdd, "--pad-script", $padScript) } else { @("--dvd", $tri) }
    $modeArgs = @("--jit", "--jit-threshold", "16", "--jit-spin")
    if ($raw) { $modeArgs += "--jit-raw" }

    $log = Join-Path $outDir "$Tag-$name.log"
    & dotnet $cli boot @firmware @mediaArgs @modeArgs --steps "$Steps" --gpu sw --bench --bench-name $name *>&1 |
        Out-File $log
    $bench = (Select-String -Path $log -Pattern "^BENCH name=" | Select-Object -Last 1).Line
    if (-not $bench) { throw "no BENCH line ($name)" }
    $wall = [double]([regex]::Match($bench, "wall=([0-9.]+)").Groups[1].Value)
    $msps = [double]([regex]::Match($bench, "msps=([0-9.]+)").Groups[1].Value)
    $ram = [regex]::Match($bench, "ramSha256=([0-9A-F]+)").Groups[1].Value
    $cpu = [regex]::Match($bench, "cpuSha256=([0-9A-F]+)").Groups[1].Value
    $outer = [double]([regex]::Match((Select-String -Path $log -Pattern "^Jit stats").Line, "outerMs=(\d+)").Groups[1].Value)
    $fault = (Select-String -Path $log -Pattern "^LastFault" | Select-Object -Last 1).Line
    return [pscustomobject]@{ Wall = $wall; Msps = $msps; Ram = $ram; Cpu = $cpu; Outer = $outer; Fault = $fault }
}

function Median([double[]]$v) {
    $s = $v | Sort-Object
    $n = $s.Count
    if ($n % 2 -eq 1) { return $s[[int]($n / 2)] }
    return ($s[$n / 2 - 1] + $s[$n / 2]) / 2
}

foreach ($w in $Workloads) {
    $ilWalls = @(); $rawWalls = @(); $ilOuter = @(); $rawOuter = @(); $wins = 0
    $ramIl = $null; $ramRaw = $null; $cpuIl = $null; $cpuRaw = $null
    for ($p = 1; $p -le $Pairs; $p++) {
        $il = Run-Leg "$w-il-p$p" $w $false
        $raw = Run-Leg "$w-raw-p$p" $w $true
        $ilWalls += $il.Wall; $rawWalls += $raw.Wall
        $ilOuter += $il.Outer; $rawOuter += $raw.Outer
        if ($raw.Wall -lt $il.Wall) { $wins++ }
        $ramIl = $il.Ram; $ramRaw = $raw.Ram; $cpuIl = $il.Cpu; $cpuRaw = $raw.Cpu
        Write-Host ("  {0} pair {1}: il={2:F3}s raw={3:F3}s  outer il={4:F0} raw={5:F0}  {6}" -f
            $w, $p, $il.Wall, $raw.Wall, $il.Outer, $raw.Outer, $raw.Fault)
    }

    $mi = Median $ilWalls; $mr = Median $rawWalls
    $oi = Median $ilOuter; $or = Median $rawOuter
    Write-Host ("{0,-5} steps={1} pairs={2}  wall il={3:F3}s raw={4:F3}s  raw/il={5:F4}  outer il={6:F0} raw={7:F0} (raw/il={8:F4})  rawWins={9}/{10}" -f
        $w, $Steps, $Pairs, $mi, $mr, ($mr / $mi), $oi, $or, ($or / $oi), $wins, $Pairs)
    Write-Host ("      SHA il  = {0} / {1}" -f $ramIl.Substring(0, 8), $cpuIl.Substring(0, 8))
    Write-Host ("      SHA raw = {0} / {1}  identical={2}" -f $ramRaw.Substring(0, 8), $cpuRaw.Substring(0, 8),
        (($ramIl -eq $ramRaw) -and ($cpuIl -eq $cpuRaw)))
}
