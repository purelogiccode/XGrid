# title-probe.ps1 - the M-T blocker probe.
#
# Runs one retail ISO to a step anchor, then records the evidence a blocker
# report needs: the final EIP, the fault vector, the NV2A counters, the top
# PGRAPH methods (count, method, value) and the JIT/engine split. With -Save it
# also writes a .xgs state at the anchor, and with -RamDump a RAM image, so a
# follow-up probe can resume AT the park instead of replaying billions of steps.
#
# This is compatibility evidence, not a gate. Retail ISOs never enter CI.
#
# Usage:
#   pwsh -File scripts/title-probe.ps1 -IsoPath "H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso" -Steps 100000000000
#   pwsh -File scripts/title-probe.ps1 -IsoPath <iso> -Steps 60000000000 -PadScript "a@55000000000+hold,none@56000000000"
#
# Exit 0 = the probe ran and wrote a report (a fault is a finding, not a failure).

param(
    [Parameter(Mandatory = $true)][string]$IsoPath,
    [ulong]$Steps = 100000000000,
    [int]$TimeoutMinutes = 60,
    [string]$OutDir = "wt-data\title-probe",
    [string]$Hdd = "wt-data\chain-199l\hdd.qcow2",
    [string]$FirmwareDir = "data\xemu",
    [string]$Cli = "",
    [string]$PadScript = "",
    [ValidateSet("sw", "gl")][string]$Gpu = "sw",
    [switch]$GpuReplace,
    [switch]$NoFast,
    [switch]$NoSave,
    [switch]$RamDump
)

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not (Test-Path -LiteralPath $IsoPath)) { Write-Error "Missing ISO: $IsoPath"; exit 2 }
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
if (-not (Test-Path $FirmwareDir)) { Write-Error "Missing firmware dir: $FirmwareDir"; exit 2 }
if (-not (Test-Path $Hdd)) { Write-Error "Missing HDD image: $Hdd"; exit 2 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$name = [System.IO.Path]::GetFileNameWithoutExtension($IsoPath)
$slug = ($name -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
if ($slug.Length -gt 80) { $slug = $slug.Substring(0, 80) }
if (-not $slug) { $slug = "title" }
$logPath = Join-Path $OutDir "$slug.log"
$jsonPath = Join-Path $OutDir "$slug.json"
$pgraphPath = Join-Path $OutDir "$slug-pgraph.txt"
$statePath = Join-Path $OutDir "$slug.xgs"
$ramPath = Join-Path $OutDir "$slug-ram.bin"

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "dotnet"
foreach ($a in @(
    $Cli, "boot",
    "--bios", (Join-Path $FirmwareDir "Complex_4627.bin"),
    "--mcpx", (Join-Path $FirmwareDir "mcpx_1.0.bin"),
    "--eeprom", (Join-Path $FirmwareDir "eeprom.bin"),
    "--hdd", (Resolve-Path $Hdd).Path,
    "--dvd", (Resolve-Path -LiteralPath $IsoPath).Path
)) { [void]$psi.ArgumentList.Add($a) }
if (-not $NoFast) {
    foreach ($a in @("--jit", "--jit-threshold", "16", "--jit-spin", "--jit-raw")) { [void]$psi.ArgumentList.Add($a) }
}
foreach ($a in @("--steps", "$Steps", "--gpu", $Gpu, "--bench", "--bench-name", $slug,
    "--jit-timing", "--pgraph", $pgraphPath, "--eip-hist", "24")) { [void]$psi.ArgumentList.Add($a) }
if ($GpuReplace) { [void]$psi.ArgumentList.Add("--gpu-replace") }
if ($PadScript) { foreach ($a in @("--pad-script", $PadScript)) { [void]$psi.ArgumentList.Add($a) } }
if (-not $NoSave) { foreach ($a in @("--save-state", $statePath)) { [void]$psi.ArgumentList.Add($a) } }
if ($RamDump) { foreach ($a in @("--ramdump", $ramPath)) { [void]$psi.ArgumentList.Add($a) } }

$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
Write-Host ("probe: {0} steps={1} gpu={2} fast={3}" -f $name, $Steps, $Gpu, (-not $NoFast))

$started = Get-Date
$p = [System.Diagnostics.Process]::new()
$p.StartInfo = $psi
[void]$p.Start()
$outTask = $p.StandardOutput.ReadToEndAsync()
$errTask = $p.StandardError.ReadToEndAsync()
$timedOut = $false
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $timedOut = $true
    try { $p.Kill($true) } catch { }
    try { [void]$p.WaitForExit(30000) } catch { }
}
$out = ""
$err = ""
try { $out = $outTask.GetAwaiter().GetResult() } catch { }
try { $err = $errTask.GetAwaiter().GetResult() } catch { }
$wall = ((Get-Date) - $started).TotalSeconds
$full = $out
if ($err) { $full += "`n--- stderr ---`n" + $err }
Set-Content -Path $logPath -Value $full -Encoding utf8

function LastMatch([string]$text, [string]$pattern) {
    $m = [regex]::Matches($text, $pattern)
    if ($m.Count -eq 0) { return $null }
    return $m[$m.Count - 1]
}
function NvInt([string]$name, [string]$line) {
    $m = [regex]::Match($line, [regex]::Escape($name) + '=(\d+)')
    if ($m.Success) { return [uint64]$m.Groups[1].Value }
    return [uint64]0
}

$bench = LastMatch $out '(?m)^BENCH name=.*$'
$benchLine = if ($bench) { $bench.Value } else { "" }
$stepsRun = if ($bench) { [uint64][regex]::Match($benchLine, 'steps=(\d+)').Groups[1].Value } else { [uint64]0 }
$benchWall = if ($bench) { [double][regex]::Match($benchLine, 'wall=([0-9.]+)').Groups[1].Value } else { 0.0 }
$msps = if ($bench) { [double][regex]::Match($benchLine, 'msps=([0-9.]+)').Groups[1].Value } else { 0.0 }
$ram = if ($bench) { [regex]::Match($benchLine, 'ramSha256=([0-9A-Fa-f]+)').Groups[1].Value } else { "" }
$cpu = if ($bench) { [regex]::Match($benchLine, 'cpuSha256=([0-9A-Fa-f]+)').Groups[1].Value } else { "" }

$eipM = LastMatch $out '(?m)^EIP=0x([0-9A-Fa-f]+) Halted=(\w+) CR0=0x([0-9A-Fa-f]+) EFLAGS=0x([0-9A-Fa-f]+)'
$eip = if ($eipM) { [Convert]::ToUInt64($eipM.Groups[1].Value, 16) } else { [uint64]0 }
$halted = if ($eipM) { $eipM.Groups[2].Value -eq "True" } else { $false }
$cr0 = if ($eipM) { "0x" + $eipM.Groups[3].Value } else { "" }
$eflags = if ($eipM) { "0x" + $eipM.Groups[4].Value } else { "" }

$faultM = LastMatch $out '(?m)^LastFault vec=(\d+) EIP=0x([0-9A-Fa-f]+) err=0x([0-9A-Fa-f]+) CR2=0x([0-9A-Fa-f]+)'
$faultVec = if ($faultM) { [int]$faultM.Groups[1].Value } else { 0 }
$faultEip = if ($faultM) { "0x" + $faultM.Groups[2].Value } else { "" }
$faultErr = if ($faultM) { "0x" + $faultM.Groups[3].Value } else { "" }
$faultCr2 = if ($faultM) { "0x" + $faultM.Groups[4].Value } else { "" }

$nv = LastMatch $out '(?m)^NV2A draws executed=.*$'
$nvLine = if ($nv) { $nv.Value } else { "" }
$draws = NvInt "draws executed" $nvLine
$skipped = NvInt "skipped" $nvLine
$pixels = NvInt "pixels" $nvLine
$frames = NvInt "frames" $nvLine
$stalls = NvInt "stalls" $nvLine
$culled = NvInt "culled" $nvLine
$programs = NvInt "programLoad" $nvLine
$fixedFn = NvInt "fixedFn" $nvLine

$jit = LastMatch $out '(?m)^Jit stats .*$'
$jitLine = if ($jit) { $jit.Value } else { "" }

$eips = @()
foreach ($m in [regex]::Matches($out, '(?m)^EIPHIST ([0-9A-Fa-f]+) n=(\d+)')) {
    $eips += [pscustomobject]@{ Eip = "0x$($m.Groups[1].Value)"; Count = [int64]($m.Groups[2].Value) }
}

$top = @()
if (Test-Path $pgraphPath) {
    $pairs = @(Get-Content $pgraphPath | ForEach-Object {
        $f = $_ -split '\s+'
        if ($f.Count -ge 3) {
            [pscustomobject]@{ Subchannel = $f[0]; Method = $f[1]; Value = $f[2] }
        }
    })
    $top = @($pairs | Group-Object Method | ForEach-Object {
        [pscustomobject]@{
            Count = [int64]$_.Count
            Method = $_.Name
            Values = @($_.Group.Value | Select-Object -Unique).Count
        }
    } | Sort-Object Count -Descending | Select-Object -First 25)
}

$stage = if ($timedOut) { "timeout" } elseif ($faultVec -ne 0) { "fault" } elseif ($draws -eq 2672 -and [math]::Abs([double]$pixels - 41995882) -le 1000) { "boot-animation" } else { "own-render" }
$eipZone = if ($eip -ge [uint64]2147483648) { "kernel" } elseif ($eip -ge [uint64]65536) { "xbe" } else { "low" }

$row = [ordered]@{
    title = $name
    slug = $slug
    path = (Resolve-Path -LiteralPath $IsoPath).Path
    steps = $stepsRun
    wallSeconds = [math]::Round($benchWall, 3)
    processSeconds = [math]::Round($wall, 3)
    msps = [math]::Round($msps, 2)
    eip = ("0x{0:X8}" -f $eip)
    eipZone = $eipZone
    halted = $halted
    cr0 = $cr0
    eflags = $eflags
    faultVec = $faultVec
    faultEip = $faultEip
    faultErr = $faultErr
    faultCr2 = $faultCr2
    stage = $stage
    timedOut = $timedOut
    frames = $frames
    stalls = $stalls
    draws = $draws
    skipped = $skipped
    pixels = $pixels
    culled = $culled
    programs = $programs
    fixedFn = $fixedFn
    ram = $ram
    cpu = $cpu
    jit = $jitLine
    topMethods = $top
    topEips = $eips
    log = $logPath
    pgraph = $pgraphPath
    state = $(if (-not $NoSave) { $statePath } else { "" })
    ramdump = $(if ($RamDump) { $ramPath } else { "" })
    command = "dotnet $Cli boot --bios $FirmwareDir\Complex_4627.bin --mcpx $FirmwareDir\mcpx_1.0.bin --eeprom $FirmwareDir\eeprom.bin --hdd $Hdd --dvd `"$((Resolve-Path -LiteralPath $IsoPath).Path)`" " +
              $(if (-not $NoFast) { "--jit --jit-threshold 16 --jit-spin --jit-raw " } else { "" }) +
              "--steps $Steps --gpu $Gpu --jit-timing --pgraph $pgraphPath --eip-hist 24" + $(if ($GpuReplace) { " --gpu-replace" } else { "" })
}
$row | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding utf8

$md = @()
$md += "# Probe: $name"
$md += ""
$md += ("steps={0}, wall={1:F1}s, msps={2:F1}, stage=**{3}**, eip={4} ({5}), faultVec={6}, frames={7}, draws={8}, pixels={9}, programs={10}" -f `
    $stepsRun, $benchWall, $msps, $stage, $row.eip, $eipZone, $faultVec, $frames, $draws, $pixels, $programs)
$md += ""
$md += "Command: ``$($row.command)``" # command is filled below for the report
$md += ""
$md += "| # | count | method | distinct values |"
$md += "|---:|---:|---|---:|"
$n = 0
foreach ($t in $top) {
    $n++
    $md += ("| {0} | {1} | 0x{2} | {3} |" -f $n, $t.Count, $t.Method, $t.Values)
}
$md += ""
$md += "Engine: ``$jitLine``"
$md += ""
$md += "## Hot EIPs"
$md += ""
$md += "| # | EIP | count |"
$md += "|---:|---|---:|"
$n = 0
foreach ($e in $eips) {
    $n++
    $md += ("| {0} | {1} | {2} |" -f $n, $e.Eip, $e.Count)
}
Set-Content -Path (Join-Path $OutDir "$slug.md") -Value ($md -join "`n") -Encoding utf8

Write-Host ("PROBE {0}: stage={1} steps={2} wall={3:F1}s msps={4:F1} eip={5} ({6}) fault={7} frames={8} draws={9} px={10} topMethod={11} topEip={12}" -f `
    $name, $stage, $stepsRun, $benchWall, $msps, $row.eip, $eipZone, $faultVec, $frames, $draws, $pixels,
    $(if ($top.Count -gt 0) { "0x$($top[0].Method)=$($top[0].Count)" } else { "-" }),
    $(if ($eips.Count -gt 0) { "$($eips[0].Eip)=$($eips[0].Count)" } else { "-" }))
Write-Host ("probe: wrote {0} and {1}" -f $jsonPath, $pgraphPath)
exit 0
