# Track B measurement leg: one boot run with the rasterizer timing diagnostic.
# Prints a single parsed METRIC line so A/B scripts can diff it. Host-only;
# the diagnostic never changes guest-visible state.
param(
    [ValidateSet("tri", "menu")][string]$Workload = "tri",
    [ulong]$Steps = 1000000000,
    [ValidateSet("sw", "gl")][string]$Gpu = "sw",
    [string]$Log,
    [string]$Cli,
    [switch]$GpuReplace,
    [switch]$NoFast,
    [string[]]$Extra = @()
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll" }
$media = Join-Path $root "data\xemu-0.8.136-windows-x86_64"
$hdd = Join-Path $root "wt-data\chain-199l\hdd.qcow2"
$tri = Join-Path $root "wt-data\xemu-tri\triangle.iso"
$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"
if (-not $Log) { $Log = Join-Path $env:TEMP "xgrid-raster-leg.log" }

$firmware = @(
    "--bios", (Join-Path $media "Complex_4627.bin"),
    "--mcpx", (Join-Path $media "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $media "eeprom.bin")
)
# The triangle needs the HDD as well as the disc: with DVD only the kernel never
# launches the disc's XBE and the leg measures the boot animation (2,673 draws /
# 35 frames / 42.3M px instead of 2,706 / 68 / 57.7M). That is the gate's media
# set (scripts/gpu-slice2-triangle-golden.ps1), so performance legs use it too.
$mediaArgs = if ($Workload -eq "menu") {
    @("--hdd", $hdd, "--pad-script", $padScript)
}
else {
    @("--hdd", $hdd, "--dvd", $tri)
}
$modeArgs = @()
if (-not $NoFast) { $modeArgs = @("--jit", "--jit-threshold", "16", "--jit-spin", "--jit-raw") }
$gpuArgs = @("--gpu", $Gpu, "--gpu-time")
if ($GpuReplace) { $gpuArgs += "--gpu-replace" }

& dotnet $Cli boot @firmware @mediaArgs @modeArgs --steps "$Steps" @gpuArgs --bench --bench-name $Workload @Extra *>&1 |
    Out-File $Log

function Last([string]$pattern) {
    $m = Select-String -Path $Log -Pattern $pattern | Select-Object -Last 1
    if ($m) { return $m.Line }
    return ""
}

$bench = Last "^BENCH name="
$raster = Last "^NV2A raster seconds"
$draws = Last "^NV2A draws executed="
$fault = Last "^LastFault"
if (-not $bench) { throw "no BENCH line in $Log" }

$wall = [regex]::Match($bench, "wall=([0-9.]+)").Groups[1].Value
$msps = [regex]::Match($bench, "msps=([0-9.]+)").Groups[1].Value
$ram = [regex]::Match($bench, "ramSha256=([0-9A-Fa-f]+)").Groups[1].Value
$cpu = [regex]::Match($bench, "cpuSha256=([0-9A-Fa-f]+)").Groups[1].Value
$legacy = [regex]::Match($raster, "legacy=([0-9.]+)").Groups[1].Value
$psh = [regex]::Match($raster, "psh=([0-9.]+)").Groups[1].Value
$outerLine = Last "^Jit stats"
$outer = [regex]::Match($outerLine, "outerMs=(\d+)").Groups[1].Value

$pixels = [regex]::Match($draws, "pixels=(\d+)").Groups[1].Value
$textured = [regex]::Match($draws, "textured=(\d+)").Groups[1].Value
$blended = [regex]::Match($draws, "blended=(\d+)").Groups[1].Value
$depth = [regex]::Match($draws, "depth=(\d+)").Groups[1].Value

"METRIC workload={0} steps={1} gpu={2} wall={3} msps={4} outerMs={5} rasterLegacy={6} rasterPsh={7} pixels={8} textured={9} blended={10} depth={11} ram={12} cpu={13}" -f `
    $Workload, $Steps, $Gpu, $wall, $msps, $outer, $legacy, $psh, $pixels, $textured, $blended, $depth,
    $ram.Substring(0, [Math]::Min(8, $ram.Length)), $cpu.Substring(0, [Math]::Min(8, $cpu.Length))
"FAULT {0}" -f $fault
