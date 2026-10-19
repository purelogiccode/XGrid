# 199l re-land gate: fast-IL vs fast-raw 1B differentials (--gpu sw recipe).
# Anchors: triangle RAM A2DCA55F... CPU 7DAB63EB...; menu RAM 660CA11B... CPU F5D61C94...
param(
    [string[]]$Legs = @("tri-il", "tri-raw", "menu-il", "menu-raw"),
    [ulong]$Steps = 1000000000
)

$ErrorActionPreference = "Stop"
$Legs = @($Legs | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
$media = Join-Path $root "data\xemu-0.8.136-windows-x86_64"
$hdd = Join-Path $root "wt-data\chain-199l\hdd.qcow2"
$tri = Join-Path $root "wt-data\xemu-tri\triangle.iso"
$outDir = Join-Path $root "wt-data\chain-199l"

$firmware = @(
    "--bios", (Join-Path $media "Complex_4627.bin"),
    "--mcpx", (Join-Path $media "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $media "eeprom.bin")
)
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"

# The guest may write to the HDD: boot a private copy, never the shared image.
if (-not (Test-Path $hdd)) {
    Copy-Item (Join-Path $media "xbox_hdd.qcow2") $hdd -Force
}

foreach ($leg in $Legs) {
    $isMenu = $leg.StartsWith("menu")
    $isRaw = $leg.EndsWith("raw")
    $mediaArgs = if ($isMenu) { @("--hdd", $hdd, "--pad-script", $padScript) } else { @("--dvd", $tri) }
    $modeArgs = @("--jit", "--jit-threshold", "16", "--jit-spin")
    if ($isRaw) { $modeArgs += "--jit-raw" }

    $log = Join-Path $outDir "$leg.log"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet $cli boot @firmware @mediaArgs @modeArgs --steps "$Steps" --gpu sw --bench --bench-name $leg *>&1 |
        Out-File $log
    $sw.Stop()
    $bench = (Select-String -Path $log -Pattern "^BENCH name=" | Select-Object -Last 1).Line
    $fault = (Select-String -Path $log -Pattern "^LastFault" | Select-Object -Last 1).Line
    if (-not $bench) { Write-Host "$leg NO BENCH | $fault"; continue }
    $ram = [regex]::Match($bench, "ramSha256=([0-9A-F]+)").Groups[1].Value.Substring(0, 8)
    $cpu = [regex]::Match($bench, "cpuSha256=([0-9A-F]+)").Groups[1].Value.Substring(0, 8)
    Write-Host ("{0,-9} wall={1,7:F2}s  ram={2} cpu={3}  {4}" -f $leg, $sw.Elapsed.TotalSeconds, $ram, $cpu, $fault)
}
