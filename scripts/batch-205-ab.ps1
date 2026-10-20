# Interleaved same-binary A/B for draw-command batching (XGRID_GL_NO_BATCH).
# Each pair runs the unbatched leg first, then the batched leg, back to back.
param(
    [ValidateSet("menu", "tri")][string]$Workload = "menu",
    [int]$Pairs = 3,
    [ulong]$Steps = 1000000000
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
$media = Join-Path $root "data\xemu-0.8.136-windows-x86_64"
$hdd = Join-Path $root "wt-data\chain-199l\hdd.qcow2"
$tri = Join-Path $root "wt-data\xemu-tri\triangle.iso"
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"
$firmware = @(
    "--bios", (Join-Path $media "Complex_4627.bin"),
    "--mcpx", (Join-Path $media "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $media "eeprom.bin")
)
$mediaArgs = if ($Workload -eq "menu") { @("--hdd", $hdd, "--pad-script", $padScript) } else { @("--dvd", $tri) }

function Run-Leg([string]$tag) {
    $log = Join-Path $root "wt-data\ab-batch-$Workload-$tag.log"
    & dotnet $cli boot @firmware @mediaArgs --jit --jit-threshold 16 --jit-spin --jit-raw `
        --steps "$Steps" --gpu gl --gpu-time --gpu-replace --bench --bench-name $Workload *>&1 |
        Out-File $log
    $bench = (Select-String -Path $log -Pattern "^BENCH name=" | Select-Object -Last 1).Line
    $gl = (Select-String -Path $log -Pattern "^GPU backend=gl draws=" | Select-Object -Last 1).Line
    $nv = (Select-String -Path $log -Pattern "^NV2A draws executed=" | Select-Object -Last 1).Line
    [pscustomobject]@{
        Tag    = $tag
        Wall   = [double][regex]::Match($bench, "wall=([0-9.]+)").Groups[1].Value
        Outer  = [double][regex]::Match(((Select-String -Path $log -Pattern "^Jit stats" | Select-Object -Last 1).Line), "outerMs=(\d+)").Groups[1].Value
        DrawMs = [double][regex]::Match($gl, "drawMs=([0-9.]+)").Groups[1].Value
        FlushMs = [double][regex]::Match($gl, "flushMs=([0-9.]+)").Groups[1].Value
        BatchCalls = [int][regex]::Match($gl, "batchCalls=(\d+)").Groups[1].Value
        Ram    = [regex]::Match($bench, "ramSha256=([0-9A-F]+)").Groups[1].Value.Substring(0, 8)
        Cpu    = [regex]::Match($bench, "cpuSha256=([0-9A-F]+)").Groups[1].Value.Substring(0, 8)
        Unsup  = [regex]::Match($gl, "unsupported=(\d+)").Groups[1].Value
        Frames = [regex]::Match($nv, "frames=(\d+)").Groups[1].Value
    }
}

$rows = @()
for ($i = 1; $i -le $Pairs; $i++) {
    $env:XGRID_GL_NO_BATCH = "1"
    $off = Run-Leg ("p{0}-off" -f $i)
    $env:XGRID_GL_NO_BATCH = $null
    $on = Run-Leg ("p{0}-on" -f $i)
    $rows += $off, $on
    "PAIR {0} off wall={1:F2} drawMs={2:F0} | on wall={3:F2} drawMs={4:F0} flushMs={5:F0} | wall {6:P1} drawMs {7:P1}" -f `
        $i, $off.Wall, $off.DrawMs, $on.Wall, $on.DrawMs, $on.FlushMs,
        (($on.Wall - $off.Wall) / $off.Wall), (($on.DrawMs - $off.DrawMs) / $off.DrawMs)
}
$offRows = $rows | Where-Object { $_.Tag -like "*-off" }
$onRows = $rows | Where-Object { $_.Tag -like "*-on" }
function Median($v) { $s = $v | Sort-Object; if ($s.Count % 2 -eq 1) { $s[[int][Math]::Floor($s.Count / 2)] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 } }
$offWall = Median ($offRows | ForEach-Object { $_.Wall })
$onWall = Median ($onRows | ForEach-Object { $_.Wall })
$offDraw = Median ($offRows | ForEach-Object { $_.DrawMs })
$onDraw = Median ($onRows | ForEach-Object { $_.DrawMs })
$offFlush = Median ($offRows | ForEach-Object { $_.FlushMs })
$onFlush = Median ($onRows | ForEach-Object { $_.FlushMs })
"MEDIAN workload={0} offWall={1:F2} onWall={2:F2} delta={3:P1} offDrawMs={4:F0} onDrawMs={5:F0} deltaDraw={6:P1} offFlushMs={7:F0} onFlushMs={8:F0}" -f `
    $Workload, $offWall, $onWall, (($onWall - $offWall) / $offWall), $offDraw, $onDraw, (($onDraw - $offDraw) / $offDraw), $offFlush, $onFlush
$rows | Format-Table Tag, Wall, Outer, DrawMs, FlushMs, BatchCalls, Ram, Cpu, Unsup, Frames -AutoSize |
    Out-String | Write-Output
$shas = $rows | ForEach-Object { "$($_.Ram)/$($_.Cpu)" } | Sort-Object -Unique
"SHAS {0}" -f ($shas -join " ")
