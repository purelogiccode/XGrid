#requires -Version 7.0
<#
.SYNOPSIS
  One command per retail row: run the XGrid leg, run the xemu 0.8.139 oracle leg,
  compare the two frames under the M-T screenshot policy, and write the evidence.

.DESCRIPTION
  This is the TM7 harness. It wraps the two existing halves instead of
  re-implementing them:

    * scripts/title-probe.ps1  - the XGrid leg (EIP, fault, NV2A counters, top
      PGRAPH methods, engine split, a --save-state anchor and now a
      --frame-display export).
    * scripts/xemu-oracle.ps1  - the xemu 0.8.139 window capture (the disc is
      selected through -config_path because xemu has no -cdrom).
    * scripts/frame-compare.py - the screenshot-policy maths (mean / colour
      count / black coverage, --crop-top drops a letterboxed band).

  One directory per probe receives: the XGrid frame (<slug>.bmp and .png), the
  xemu frame (xemu-<slug>-<seconds>s.png), the per-title JSON (<slug>.json), the
  Markdown report (<slug>.md), the raw XGrid probe (<slug>-xgrid.json/.md), the
  run log, the PGRAPH method dump and the state anchor.

  IT FAILS SOFT. Retail rows are evidence, never gates, so a missing xemu frame
  is a recorded finding ("xemu: blocked - <reason>") and the run still exits 0
  with the XGrid evidence intact. It never fabricates a comparison: when the
  xemu leg is blocked the comparison fields are empty and the policy verdict is
  "n/a".

  The screenshot policy (ImplementationPlan.md E1) is never pixel-exact: a row
  passes when the black coverage is within 2 percentage points, the colour count
  is within 25% and |dmean| is <= 25/255 on every channel.

.PARAMETER IsoPath
  The retail ISO (positional). Both legs use the same image.

.PARAMETER Steps
  Steps to run in the XGrid leg (default 2B). With -LoadState the counter
  restarts at the snapshot, so this is "steps after the anchor".

.PARAMETER LoadState
  Resume from a .xgs anchor instead of replaying from boot (the TM4/TM5/TM6
  anchors live in wt-data/tm4, tm5 and tm6).

.PARAMETER PadScript
  XGrid pad script, e.g. "down@950000000+hold,none@962000000,a@970000000+hold".
  Offsets restart from 0 on a -LoadState run.

.PARAMETER SaveState
  Where the XGrid leg writes its .xgs anchor (default: <OutDir>/<probe-slug>.xgs).

.PARAMETER Scene
  Free text naming the scene the row compares (same level / camera context on
  both sides). Recorded in the row; it is the policy's "comparable scene" rule.

.PARAMETER XemuSeconds
  Seconds to let xemu boot before the capture (default 150, the TM4 anchor).

.PARAMETER SkipXemu
  Skip the xemu leg entirely (the report records xemu: skipped).

.PARAMETER XemuFrame
  Reuse an existing xemu capture instead of booting xemu again (the report
  records it as captured, with the source path). Useful when the comparable
  scene needs a different boot time than the default, or when iterating on the
  XGrid side.

.PARAMETER NoCompareReason
  Record the xemu frame but skip the policy comparison, with this reason (for
  example "xemu sits on the title screen: it cannot be driven on this host, so
  the scenes are not comparable"). The row then reads "captured, not compared"
  instead of inventing a verdict across two different scenes.

.EXAMPLE
  pwsh -File scripts/retail-game-probe.ps1 "H:\Tests\XBOXReady\Ninja Gaiden Black (USA) (En,Ja).iso" `
      -LoadState wt-data\tm6\ngb-10b.xgs -Steps 2000000000 -OutDir wt-data\tm7\ngb `
      -Scene "stormy-sea intro" -XemuSeconds 150

.EXAMPLE
  # the 007 playability row (TM4 anchor)
  pwsh -File scripts/retail-game-probe.ps1 "H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso" `
      -LoadState wt-data\tm4\007-100b.xgs -Steps 2000000000 -OutDir wt-data\tm7\007
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$IsoPath,
    [ulong]$Steps = 2000000000,
    [string]$LoadState = "",
    [string]$PadScript = "",
    [string]$OutDir = "",
    [string]$Label = "",
    [string]$Scene = "",
    [string]$SaveState = "",
    [int]$TimeoutMinutes = 60,
    [string]$Hdd = "wt-data\title-matrix-1b\hdd-clean.qcow2",
    [string]$FirmwareDir = "data\xemu",
    [string]$Cli = "",
    [ValidateSet("sw", "gl")][string]$Gpu = "sw",
    [switch]$GpuReplace,
    [switch]$NoFast,
    [switch]$NoSave,
    [int]$XemuSeconds = 150,
    [switch]$SkipXemu,
    [string]$XemuIsoPath = "",
    [string]$XemuFrame = "",
    [string]$NoCompareReason = "",
    [double]$CropTop = 0.25
)

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# --- inputs ---------------------------------------------------------------
if (-not (Test-Path -LiteralPath $IsoPath)) { Write-Error "Missing ISO: $IsoPath"; exit 2 }
if (-not (Test-Path $FirmwareDir)) { Write-Error "Missing firmware dir: $FirmwareDir"; exit 2 }
if (-not (Test-Path $Hdd)) { Write-Error "Missing HDD image: $Hdd (retail runs use a copy, never wt-data\chain-199l\hdd.qcow2)"; exit 2 }
if ($LoadState -and -not (Test-Path -LiteralPath $LoadState)) { Write-Error "Missing save state: $LoadState"; exit 2 }
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }

$name = [System.IO.Path]::GetFileNameWithoutExtension($IsoPath)
# title-probe.ps1 derives its own slug from the ISO name (80 chars, same rule);
# the raw evidence files are named after it, so compute it here too.
$probeSlug = ($name -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
if ($probeSlug.Length -gt 80) { $probeSlug = $probeSlug.Substring(0, 80) }
if (-not $probeSlug) { $probeSlug = "title" }

$slug = ($name -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
if ($slug.Length -gt 60) { $slug = $slug.Substring(0, 60) }
if (-not $slug) { $slug = "title" }
if ($Label) {
    $labelSlug = ($Label -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
    if ($labelSlug) { $slug = $labelSlug }
}

# A .xgs records the exact HDD path it was taken with and the loader refuses a
# mismatch, so a resumed probe reads the image out of the snapshot. That is what
# makes the TM4/TM5/TM6 anchors reusable without replaying billions of steps.
# The path lives in the device-state block after the RAM image, so scan the file
# backwards in chunks instead of materialising a 140 MB snapshot as a string.
if ($LoadState) {
    $snapshotPath = (Resolve-Path -LiteralPath $LoadState).Path
    $stream = [System.IO.File]::OpenRead($snapshotPath)
    try {
        $chunkSize = 8MB
        $pos = [Math]::Max(0, $stream.Length - $chunkSize)
        while ($true) {
            $stream.Position = $pos
            $readLen = [int]([Math]::Min($chunkSize + 512, $stream.Length - $pos))
            $buf = New-Object byte[] $readLen
            $read = $stream.Read($buf, 0, $readLen)
            $text = [System.Text.Encoding]::Latin1.GetString($buf, 0, $read)
            $hddMatch = [regex]::Match($text, '[A-Za-z0-9_.\\/-]*?\.qcow2')
            if ($hddMatch.Success) {
                $snapshotHdd = $hddMatch.Value.Trim()
                if (Test-Path -LiteralPath $snapshotHdd) {
                    if ($Hdd -ne $snapshotHdd) { Write-Host ("  hdd: {0} (from the snapshot)" -f $snapshotHdd) }
                    $Hdd = $snapshotHdd
                }
                else {
                    Write-Warning "the snapshot records HDD '$snapshotHdd', which is missing; using $Hdd"
                }
                break
            }
            if ($pos -eq 0) { break }
            $pos = [Math]::Max(0, $pos - $chunkSize)
        }
    }
    finally { $stream.Dispose() }
}
if (-not (Test-Path $Hdd)) { Write-Error "Missing HDD image: $Hdd"; exit 2 }

if (-not $OutDir) { $OutDir = Join-Path "wt-data\retail-probe" $slug }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$frameBmp = Join-Path $OutDir "$slug.bmp"
$framePng = Join-Path $OutDir "$slug.png"
$xemuPng = Join-Path $OutDir ("xemu-{0}-{1}s.png" -f $slug, $XemuSeconds)
$jsonPath = Join-Path $OutDir "$slug.json"
$mdPath = Join-Path $OutDir "$slug.md"
$xgridJson = Join-Path $OutDir "$slug-xgrid.json"
$xgridMd = Join-Path $OutDir "$slug-xgrid.md"

Write-Host ("retail-probe: {0}" -f $name)
Write-Host ("  steps={0} loadState={1} out={2}" -f $Steps, $(if ($LoadState) { $LoadState } else { "(boot)" }), $OutDir)

# --- helpers --------------------------------------------------------------
function Get-FrameStats([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $lines = & python scripts\frame-stat.py $path 2>&1
    $text = ($lines | Out-String)
    $m = [regex]::Match($text, '(\d+)x(\d+) mean=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) colours=(\d+) black=([\d.]+)%')
    if (-not $m.Success) { return $null }
    return [ordered]@{
        width = [int]$m.Groups[1].Value
        height = [int]$m.Groups[2].Value
        mean = @([double]$m.Groups[3].Value, [double]$m.Groups[4].Value, [double]$m.Groups[5].Value)
        colours = [int]$m.Groups[6].Value
        blackPct = [double]$m.Groups[7].Value
    }
}

function Convert-BmpToPng([string]$bmpPath, [string]$pngPath) {
    if (-not (Test-Path -LiteralPath $bmpPath)) { return $false }
    Add-Type -AssemblyName System.Drawing
    $img = [System.Drawing.Image]::FromFile([System.IO.Path]::GetFullPath($bmpPath))
    try {
        $img.Save([System.IO.Path]::GetFullPath($pngPath), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $img.Dispose() }
    return (Test-Path -LiteralPath $pngPath)
}

# --- XGrid leg ------------------------------------------------------------
$probeArgs = @(
    "-NoProfile", "-File", (Join-Path $root "scripts\title-probe.ps1"),
    "-IsoPath", (Resolve-Path -LiteralPath $IsoPath).Path,
    "-Steps", "$Steps",
    "-TimeoutMinutes", "$TimeoutMinutes",
    "-OutDir", $OutDir,
    "-Hdd", $Hdd,
    "-FirmwareDir", $FirmwareDir,
    "-Cli", $Cli,
    "-Gpu", $Gpu,
    "-FramePath", $frameBmp
)
if ($LoadState) { $probeArgs += @("-LoadState", (Resolve-Path -LiteralPath $LoadState).Path) }
if ($SaveState) { $probeArgs += @("-StatePath", $SaveState) }
if ($PadScript) { $probeArgs += @("-PadScript", $PadScript) }
if ($GpuReplace) { $probeArgs += "-GpuReplace" }
if ($NoFast) { $probeArgs += "-NoFast" }
if ($NoSave) { $probeArgs += "-NoSave" }

$probeText = (& pwsh @probeArgs 2>&1 | Out-String)
$probeText.TrimEnd() | Write-Host
$probeLine = ([regex]::Matches($probeText, '(?m)^PROBE .*$') | ForEach-Object { $_.Value.TrimEnd() } | Select-Object -Last 1)

$xgrid = $null
$rawJson = Join-Path $OutDir "$probeSlug.json"
if (Test-Path -LiteralPath $rawJson) {
    $xgrid = Get-Content -LiteralPath $rawJson -Raw | ConvertFrom-Json
    # Keep the raw XGrid evidence under its own name; the merged row takes the
    # canonical one so a reader finds the published record first.
    Move-Item -LiteralPath $rawJson -Destination $xgridJson -Force
}
if (Test-Path -LiteralPath (Join-Path $OutDir "$probeSlug.md")) {
    Move-Item -LiteralPath (Join-Path $OutDir "$probeSlug.md") -Destination $xgridMd -Force
}
if ($null -eq $xgrid) {
    Write-Warning "the XGrid leg wrote no JSON (see $OutDir\$probeSlug.log)"
}

$frameSha = if (Test-Path -LiteralPath $frameBmp) { (Get-FileHash -LiteralPath $frameBmp -Algorithm SHA256).Hash.Substring(0, 8) } else { "" }
$framePngOk = Convert-BmpToPng $frameBmp $framePng
$xgridStats = Get-FrameStats $frameBmp

# --- xemu leg -------------------------------------------------------------
$xemu = [ordered]@{
    status = "skipped"
    reason = ""
    seconds = $XemuSeconds
    frame = ""
    command = ""
    stats = $null
}
$xemuIso = if ($XemuIsoPath) { $XemuIsoPath } else { (Resolve-Path -LiteralPath $IsoPath).Path }
if (-not $SkipXemu -and $XemuFrame) {
    if (-not (Test-Path -LiteralPath $XemuFrame)) { Write-Error "Missing xemu frame: $XemuFrame"; exit 2 }
    $xemu.status = "captured"
    $xemu.frame = (Resolve-Path -LiteralPath $XemuFrame).Path
    $xemu.stats = Get-FrameStats $XemuFrame
    # A reused capture carries its boot time in the file name (xemu-<slug>-<n>s.png);
    # record that rather than the default, so the row names the scene's phase.
    $secMatch = [regex]::Match([System.IO.Path]::GetFileName($XemuFrame), '-(\d+)s\.png$')
    if ($secMatch.Success) { $xemu.seconds = [int]$secMatch.Groups[1].Value }
    $xemu.command = "reused capture: $XemuFrame"
    $xemuPng = $xemu.frame
    Write-Host ("xemu: reused capture {0} ({1} s)" -f $xemu.frame, $xemu.seconds)
}
elseif (-not $SkipXemu) {
    $xemu.status = "blocked"
    if (Get-Process -Name xemu -ErrorAction SilentlyContinue) {
        $xemu.reason = "xemu is already running; stop it first"
    }
    else {
        $xemuArgs = @(
            "-NoProfile", "-File", (Join-Path $root "scripts\xemu-oracle.ps1"),
            "-IsoPath", $xemuIso,
            "-Seconds", "$XemuSeconds",
            "-OutPath", $xemuPng,
            "-Stop"
        )
        $xemu.command = "pwsh -File scripts\xemu-oracle.ps1 -IsoPath `"$xemuIso`" -Seconds $XemuSeconds -OutPath `"$xemuPng`" -Stop"
        try {
            $xemuText = (& pwsh @xemuArgs 2>&1 | Out-String)
            $xemuText.TrimEnd() | Write-Host
            if (Test-Path -LiteralPath $xemuPng) {
                $xemuStats = Get-FrameStats $xemuPng
                $xemu.stats = $xemuStats
                # A capture can be technically written and still be useless: an
                # SDL/GL surface that answered black, or a shot of the window
                # chrome. Record it as a block instead of comparing nonsense.
                if ($null -eq $xemuStats) {
                    $xemu.reason = "capture written but unreadable"
                }
                elseif ($xemuStats.colours -le 2 -or $xemuStats.blackPct -ge 99.0) {
                    $xemu.reason = ("capture is blank ({0} colours, {1:F1}% black) - PrintWindow on the SDL/GL surface answered black" -f $xemuStats.colours, $xemuStats.blackPct)
                    $xemu.stats = $xemuStats
                }
                else {
                    $xemu.status = "captured"
                    $xemu.frame = (Resolve-Path -LiteralPath $xemuPng).Path
                }
            }
            else {
                $candidates = (($xemuText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -Last 8)
                $reasonLine = ($candidates | Where-Object { $_ -match 'contended|Exception|Error|Missing|xemu exited|did not appear|not available|black' } | Select-Object -Last 1)
                $xemu.reason = if ($reasonLine) { ($reasonLine.Trim() -replace '^\|\s*', '') } else { "no screenshot was written" }
            }
        }
        catch {
            $xemu.reason = "xemu leg threw: $($_.Exception.Message)"
        }
    }
    if ($xemu.status -eq "blocked") { Write-Host ("xemu: blocked - {0}" -f $xemu.reason) }
}

# --- comparison (the screenshot policy) -----------------------------------
$compare = [ordered]@{
    status = "n/a"
    policy = "n/a"
    cropTop = $CropTop
    dmean = @()
    colourRatio = $null
    blackDeltaPts = $null
    referenceColours = $null
    frameColours = $null
    referenceBlackPct = $null
    frameBlackPct = $null
    reasons = @()
}
if ($xemu.status -eq "captured" -and (Test-Path -LiteralPath $frameBmp)) {
    $cmpText = (& python scripts\frame-compare.py $xemuPng $frameBmp --crop-top $CropTop 2>&1 | Out-String)
    $cmpText.TrimEnd() | Write-Host
    $refM = [regex]::Match($cmpText, 'reference\s+.*?colours=(\d+) black=([\d.]+)%')
    $frmM = [regex]::Match($cmpText, 'frame\s+.*?colours=(\d+) black=([\d.]+)%')
    $dM = [regex]::Match($cmpText, 'COMPARE dmean=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)')
    if ($refM.Success -and $frmM.Success -and $dM.Success) {
        $refColours = [int]$refM.Groups[1].Value
        $frmColours = [int]$frmM.Groups[1].Value
        $refBlack = [double]$refM.Groups[2].Value
        $frmBlack = [double]$frmM.Groups[2].Value
        $dmean = @([double]$dM.Groups[1].Value, [double]$dM.Groups[2].Value, [double]$dM.Groups[3].Value)
        $colourRatio = if ($refColours -gt 0) { [math]::Round($frmColours / $refColours, 3) } else { $null }
        $blackDelta = [math]::Round($frmBlack - $refBlack, 2)
        $reasons = @()
        $worst = ($dmean | Measure-Object -Maximum).Maximum
        if ($worst -gt 25.0) { $reasons += ("dmean {0:F1}/255 > 25" -f $worst) }
        if ([math]::Abs($blackDelta) -gt 2.0) { $reasons += ("black coverage {0:F1}% vs {1:F1}% ({2:+0.0;-0.0} pts)" -f $frmBlack, $refBlack, $blackDelta) }
        if ($null -ne $colourRatio -and ($colourRatio -lt 0.75 -or $colourRatio -gt 1.25)) { $reasons += ("colour count {0} vs {1} (x{2:F2})" -f $frmColours, $refColours, $colourRatio) }
        $compare.status = "compared"
        $compare.policy = if ($reasons.Count -eq 0) { "pass" } else { "fail" }
        if ($NoCompareReason) {
            # The numbers are still worth recording (they bound how far apart two
            # scenes are), but a verdict across two different scenes would be a
            # fabrication: the policy verdict stays "n/a" with the reason.
            $compare.policy = "n/a"
            $reasons = @($NoCompareReason) + $reasons
        }
        $compare.dmean = $dmean
        $compare.colourRatio = $colourRatio
        $compare.blackDeltaPts = $blackDelta
        $compare.referenceColours = $refColours
        $compare.frameColours = $frmColours
        $compare.referenceBlackPct = $refBlack
        $compare.frameBlackPct = $frmBlack
        $compare.reasons = $reasons
    }
    else {
        $compare.status = "unparsed"
        $compare.reasons = @("frame-compare output did not parse")
    }
}

# --- the published row ----------------------------------------------------
$row = [ordered]@{
    schema = 1
    title = $name
    slug = $slug
    iso = (Resolve-Path -LiteralPath $IsoPath).Path
    scene = $Scene
    stepAnchor = $Steps
    loadState = $(if ($LoadState) { (Resolve-Path -LiteralPath $LoadState).Path } else { "" })
    padScript = $PadScript
    gpu = $Gpu
    gpuReplace = [bool]$GpuReplace
    fast = (-not $NoFast)
    xgrid = [ordered]@{
        stage = $(if ($xgrid) { $xgrid.stage } else { "unknown" })
        eip = $(if ($xgrid) { $xgrid.eip } else { "" })
        eipZone = $(if ($xgrid) { $xgrid.eipZone } else { "" })
        faultVec = $(if ($xgrid) { $xgrid.faultVec } else { $null })
        faultEip = $(if ($xgrid) { $xgrid.faultEip } else { "" })
        halted = $(if ($xgrid) { $xgrid.halted } else { $null })
        timedOut = $(if ($xgrid) { $xgrid.timedOut } else { $null })
        steps = $(if ($xgrid) { $xgrid.steps } else { 0 })
        wallSeconds = $(if ($xgrid) { $xgrid.wallSeconds } else { 0 })
        msps = $(if ($xgrid) { $xgrid.msps } else { 0 })
        frames = $(if ($xgrid) { $xgrid.frames } else { 0 })
        draws = $(if ($xgrid) { $xgrid.draws } else { 0 })
        skipped = $(if ($xgrid) { $xgrid.skipped } else { 0 })
        pixels = $(if ($xgrid) { $xgrid.pixels } else { 0 })
        programs = $(if ($xgrid) { $xgrid.programs } else { 0 })
        ram = $(if ($xgrid) { $xgrid.ram } else { "" })
        cpu = $(if ($xgrid) { $xgrid.cpu } else { "" })
        ram8 = $(if ($xgrid -and $xgrid.ram) { ([string]$xgrid.ram).Substring(0, [Math]::Min(8, ([string]$xgrid.ram).Length)).ToUpperInvariant() } else { "" })
        cpu8 = $(if ($xgrid -and $xgrid.cpu) { ([string]$xgrid.cpu).Substring(0, [Math]::Min(8, ([string]$xgrid.cpu).Length)).ToUpperInvariant() } else { "" })
        frame = $frameBmp
        framePng = $(if ($framePngOk) { $framePng } else { "" })
        frameSha = $frameSha
        frameStats = $xgridStats
        state = $(if ($xgrid) { $xgrid.state } else { "" })
        command = $(if ($xgrid) { $xgrid.command } else { "" })
        log = $(if ($xgrid) { $xgrid.log } else { "" })
        pgraph = $(if ($xgrid) { $xgrid.pgraph } else { "" })
        raw = $xgridJson
        topMethods = $(if ($xgrid) { @($xgrid.topMethods) } else { @() })
        topEips = $(if ($xgrid) { @($xgrid.topEips) } else { @() })
    }
    xemu = $xemu
    compare = $compare
    probeLine = $probeLine
    note = "Retail rows are evidence, never merge gates. The xemu frame is a window screenshot, so the comparison is the documented policy (mean / colour count / black coverage), never pixel-exact."
}
$row | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

# --- the Markdown report --------------------------------------------------
$md = @()
$md += "# Retail probe: $name"
$md += ""
$md += ("Generated {0} by ``scripts/retail-game-probe.ps1``. Retail rows are evidence, never merge gates." -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
$md += ""
if ($Scene) { $md += ("**Scene:** {0}" -f $Scene); $md += "" }
$md += "## Row"
$md += ""
$md += "| field | value |"
$md += "|---|---|"
$md += ("| stage | **{0}** |" -f $row.xgrid.stage)
$md += ("| EIP | ``{0}`` ({1}) |" -f $row.xgrid.eip, $row.xgrid.eipZone)
if ($row.xgrid.faultVec -ne 0) {
    $md += ("| fault | **vec={0}**, EIP ``{1}`` |" -f $row.xgrid.faultVec, $row.xgrid.faultEip)
}
else {
    $md += "| fault | none (vec=0) |"
}
$md += ("| steps | {0:N0} |" -f [double]$row.xgrid.steps)
$md += ("| wall / rate | {0:F1} s / {1:F1} msps |" -f $row.xgrid.wallSeconds, $row.xgrid.msps)
$md += ("| draws / pixels | {0:N0} / {1:N0} |" -f [double]$row.xgrid.draws, [double]$row.xgrid.pixels)
$md += ("| frames | {0:N0} |" -f [double]$row.xgrid.frames)
$md += ("| programs | {0:N0} |" -f [double]$row.xgrid.programs)
$md += ("| RAM / CPU | ``{0}`` / ``{1}`` |" -f $row.xgrid.ram8, $row.xgrid.cpu8)
$md += ("| XGrid frame | ``{0}`` sha ``{1}`` |" -f $frameBmp, $frameSha)
if ($xgridStats) {
    $md += ("| XGrid frame stats | {0}x{1}, mean ({2:F1}, {3:F1}, {4:F1}), {5} colours, {6:F1}% black |" -f `
        $xgridStats.width, $xgridStats.height, $xgridStats.mean[0], $xgridStats.mean[1], $xgridStats.mean[2], $xgridStats.colours, $xgridStats.blackPct)
}
if ($row.loadState) { $md += ("| step anchor | ``{0}`` ({1:N0} steps after it) |" -f $row.loadState, [double]$Steps) }
if ($row.padScript) { $md += ("| pad script | ``{0}`` |" -f $row.padScript) }
$md += ""
$md += "## xemu 0.8.139 oracle"
$md += ""
if ($xemu.status -eq "captured") {
    $md += ("**captured** - ``{0}`` ({1} s)" -f $xemu.frame, $xemu.seconds)
    $md += ""
    if ($xemu.stats) {
        $md += ("{0}x{1}, mean ({2:F1}, {3:F1}, {4:F1}), {5} colours, {6:F1}% black" -f `
            $xemu.stats.width, $xemu.stats.height, $xemu.stats.mean[0], $xemu.stats.mean[1], $xemu.stats.mean[2], $xemu.stats.colours, $xemu.stats.blackPct)
    }
}
elseif ($xemu.status -eq "blocked") {
    $md += ("**blocked** - {0}" -f $xemu.reason)
    $md += ""
    $md += "No xemu frame is claimed. The XGrid evidence above stands on its own; re-run on an idle desktop to fill the oracle column."
}
else {
    $md += "**skipped** (-SkipXemu)."
}
if ($xemu.command) {
    $md += ""
    $md += "``````"
    $md += $xemu.command
    $md += "``````"
}
$md += ""
$md += "## Screenshot policy"
$md += ""
if ($compare.status -eq "compared") {
    $md += "| metric | xemu | XGrid | rule |"
    $md += "|---|---:|---:|---|"
    $md += ("| mean (R, G, B) | ({0:F1}, {1:F1}, {2:F1}) | ({3:F1}, {4:F1}, {5:F1}) | abs dmean <= 25/255 |" -f `
        $xemu.stats.mean[0], $xemu.stats.mean[1], $xemu.stats.mean[2], $xgridStats.mean[0], $xgridStats.mean[1], $xgridStats.mean[2])
    $md += ("| dmean | - | ({0:F1}, {1:F1}, {2:F1}) | <= 25 |" -f $compare.dmean[0], $compare.dmean[1], $compare.dmean[2])
    $md += ("| colours | {0} | {1} | within 25% |" -f $compare.referenceColours, $compare.frameColours)
    $md += ("| black | {0:F1}% | {1:F1}% | within 2 points |" -f $compare.referenceBlackPct, $compare.frameBlackPct)
    $md += ""
    if ($compare.policy -eq "pass") {
        $md += "**Policy: PASS** (crop-top {0:F2})." -f $CropTop
    }
    elseif ($compare.policy -eq "n/a") {
        $md += ("**Policy: n/a** - no verdict is claimed. {0} (crop-top {1:F2})." -f (($compare.reasons) -join "; "), $CropTop)
    }
    else {
        $md += ("**Policy: FAIL** - {0} (crop-top {1:F2})." -f (($compare.reasons) -join "; "), $CropTop)
    }
}
elseif ($xemu.status -eq "captured") {
    if ($compare.status -eq "not-compared") {
        $md += ("**Not compared** - {0}" -f (($compare.reasons) -join "; "))
    }
    else {
        $md += ("Comparison not available: {0}." -f $compare.status)
    }
}
else {
    $md += "n/a - no xemu frame to compare."
}
$md += ""
$md += "## Reproduce"
$md += ""
$md += "``````powershell"
$md += $row.xgrid.command
$md += "``````"
$md += ""
$md += "The full XGrid probe report (top PGRAPH methods, hot EIPs, engine split) is ``$xgridMd``."
Set-Content -LiteralPath $mdPath -Value ($md -join "`n") -Encoding utf8

# --- one-line summary -----------------------------------------------------
$xemuCell = if ($xemu.status -eq "captured") { "captured" } elseif ($xemu.status -eq "blocked") { "blocked" } else { "skipped" }
$cmpCell = if ($compare.status -eq "compared") { "dmean=({0:F1}, {1:F1}, {2:F1}) policy={3}" -f $compare.dmean[0], $compare.dmean[1], $compare.dmean[2], $compare.policy } elseif ($compare.status -eq "not-compared") { "not-compared" } else { "n/a" }
$topMethod = if ($xgrid -and @($xgrid.topMethods).Count -gt 0) { "0x{0}" -f $xgrid.topMethods[0].Method } else { "-" }
Write-Host ("PROBE {0}: stage={1} steps={2} wall={3:F1}s msps={4:F1} eip={5} fault={6} frames={7} draws={8} px={9} frame={10} xemu={11} compare={12} topMethod={13}" -f `
    $name, $row.xgrid.stage, $row.xgrid.steps, $row.xgrid.wallSeconds, $row.xgrid.msps, $row.xgrid.eip,
    $row.xgrid.faultVec, $row.xgrid.frames, $row.xgrid.draws, $row.xgrid.pixels,
    $(if ($frameSha) { $frameSha } else { "-" }), $xemuCell, $cmpCell, $topMethod)
if ($xemu.status -eq "blocked") { Write-Host ("  xemu blocked: {0}" -f $xemu.reason) }
Write-Host ("  wrote {0} and {1}" -f $jsonPath, $mdPath)
exit 0
