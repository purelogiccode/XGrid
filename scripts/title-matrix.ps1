# title-matrix.ps1 - the M-T progress bar.
#
# Runs every retail ISO at a fixed step budget and records the evidence the
# title matrix needs: did the title reach its own render path, where did it
# park, did it fault, and what were the NV2A/CPU counters. One JSON row per
# title plus a summary JSON and a Markdown table.
#
# The dashboard launches a disc only after a pad edge, so the default
# -PadScript presses Down and A on the dashboard menu (the same script the
# dashboard corpus row uses). Without it every title stays on the dashboard.
#
# This is compatibility evidence, not a gate. The standing gates stay the
# accurate-mode 1B goldens and the ktest smoke. Retail ISOs never enter CI.
#
# Usage (from the repo root, after dotnet build -c Release):
#   pwsh -File scripts/title-matrix.ps1 -MediaDir "H:\Tests\XBOXReady" -AlsoDir "H:\Tests\XBOXTest"
#   pwsh -File scripts/title-matrix.ps1 -TitleFilter '^007' -Force
#   pwsh -File scripts/title-matrix.ps1 -Resume            # skip titles with a row
#   pwsh -File scripts/title-matrix.ps1 -Steps 5000000000 -Parallel 1
#
# Exit 0 = every title produced a row (faults/timeouts are rows, not failures).

param(
    [string]$MediaDir = "H:\Tests\XBOXReady",
    [string]$AlsoDir = "H:\Tests\XBOXTest",
    [string]$TitleFilter = "",
    [ulong]$Steps = 5000000000,
    [int]$TimeoutMinutes = 12,
    [int]$Parallel = 1,
    [string]$OutDir = "wt-data\title-matrix",
    [string]$Cli = "",
    [string]$FirmwareDir = "data\xemu",
    [string]$Hdd = "wt-data\chain-199l\hdd.qcow2",
    [string]$PadScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000",
    [ValidateSet("sw", "gl")][string]$Gpu = "sw",
    [switch]$GpuReplace,
    [switch]$NoFast,
    [switch]$Resume,
    [switch]$Force,
    [switch]$Frame,
    [switch]$NoHddCopy,
    [switch]$NoMarkdown
)

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net11.0\XGrid.Cli.dll" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
if (-not (Test-Path $FirmwareDir)) { Write-Error "Missing firmware dir: $FirmwareDir"; exit 2 }
if (-not (Test-Path $Hdd)) { Write-Error "Missing HDD image: $Hdd"; exit 2 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "logs") | Out-Null

function Get-Slug([string]$path) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
    $slug = ($name -replace '[^A-Za-z0-9._-]+', '_').Trim('_')
    if ($slug.Length -gt 80) { $slug = $slug.Substring(0, 80) }
    if (-not $slug) { $slug = "title" }
    return $slug
}

function Get-ShortHash([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))
        return ([System.BitConverter]::ToString($bytes).Replace("-", "").Substring(0, 6)).ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Get-IsoList {
    $list = @()
    foreach ($dir in @($MediaDir, $AlsoDir)) {
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        if (-not (Test-Path $dir)) { Write-Host "matrix: media dir missing, skipping: $dir"; continue }
        $source = if ($dir -eq $MediaDir) { "oracle" } else { "compat" }
        foreach ($f in Get-ChildItem -Path $dir -Filter *.iso -File -ErrorAction SilentlyContinue) {
            $list += [pscustomobject]@{ Path = $f.FullName; Name = $f.Name; Source = $source }
        }
    }
    $seen = @{}
    $out = @()
    foreach ($t in $list) {
        $key = $t.Path.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $out += $t
    }
    if ($TitleFilter) { $out = @($out | Where-Object { $_.Name -match $TitleFilter }) }
    $out = @($out | Sort-Object Source, Name)
    $slugs = @{}
    foreach ($t in $out) {
        $slug = Get-Slug $t.Path
        if ($slugs.ContainsKey($slug)) { $slug = "$slug-" + (Get-ShortHash $t.Path) }
        $slugs[$slug] = $true
        $t | Add-Member -NotePropertyName Slug -NotePropertyValue $slug -Force
    }
    return $out
}

function Start-OneProcess([pscustomobject]$title, [int]$index, [string]$hddPath) {
    $slug = $title.Slug
    $logPath = Join-Path $OutDir "logs\$slug.log"
    $jsonPath = Join-Path $OutDir "$slug.json"
    $bmpPath = Join-Path $OutDir "frames\$slug-display.bmp"
    if ($Frame) { New-Item -ItemType Directory -Force -Path (Join-Path $OutDir "frames") | Out-Null }

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = "dotnet"
    foreach ($a in @(
        $Cli, "boot",
        "--bios", (Join-Path $FirmwareDir "Complex_4627.bin"),
        "--mcpx", (Join-Path $FirmwareDir "mcpx_1.0.bin"),
        "--eeprom", (Join-Path $FirmwareDir "eeprom.bin"),
        "--hdd", (Resolve-Path -LiteralPath $hddPath).Path,
        "--dvd", $title.Path
    )) { [void]$psi.ArgumentList.Add($a) }
    if (-not $NoFast) {
        foreach ($a in @("--jit", "--jit-threshold", "16", "--jit-spin", "--jit-raw")) { [void]$psi.ArgumentList.Add($a) }
    }
    foreach ($a in @("--steps", "$Steps", "--gpu", $Gpu, "--bench", "--bench-name", $slug)) { [void]$psi.ArgumentList.Add($a) }
    if ($PadScript) { foreach ($a in @("--pad-script", $PadScript)) { [void]$psi.ArgumentList.Add($a) } }
    if ($GpuReplace) { [void]$psi.ArgumentList.Add("--gpu-replace") }
    if ($Frame) { foreach ($a in @("--frame-display", $bmpPath)) { [void]$psi.ArgumentList.Add($a) } }

    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $p = [System.Diagnostics.Process]::new()
    $p.StartInfo = $psi
    [void]$p.Start()
    return [pscustomobject]@{
        Title = $title; Index = $index; Process = $p
        OutTask = $p.StandardOutput.ReadToEndAsync()
        ErrTask = $p.StandardError.ReadToEndAsync()
        Started = Get-Date
        LogPath = $logPath; JsonPath = $jsonPath; Hdd = $hddPath
    }
}

function Complete-OneProcess([pscustomobject]$h) {
    $p = $h.Process
    $timedOut = $false
    if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $timedOut = $true
        try { $p.Kill($true) } catch { }
        try { [void]$p.WaitForExit(30000) } catch { }
    }
    $out = ""
    $err = ""
    try { $out = $h.OutTask.GetAwaiter().GetResult() } catch { }
    try { $err = $h.ErrTask.GetAwaiter().GetResult() } catch { }
    $processWall = ((Get-Date) - $h.Started).TotalSeconds
    $full = $out
    if ($err) { $full += "`n--- stderr ---`n" + $err }
    Set-Content -Path $h.LogPath -Value $full -Encoding utf8

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

    $eipM = LastMatch $out '(?m)^EIP=0x([0-9A-Fa-f]+) Halted=(\w+)'
    $eip = if ($eipM) { [Convert]::ToUInt64($eipM.Groups[1].Value, 16) } else { [uint64]0 }
    $halted = if ($eipM) { $eipM.Groups[2].Value -eq "True" } else { $false }

    # A timed-out run has no BENCH line; the CLI still printed progress every
    # 250M steps, so the last Progress line is the step/EIP the title reached.
    $progressM = LastMatch $out '(?m)^Progress (\d+) instruction\(s\) EIP=0x([0-9A-Fa-f]+)'
    $progressSteps = if ($progressM) { [uint64]$progressM.Groups[1].Value } else { [uint64]0 }
    $progressEip = if ($progressM) { [Convert]::ToUInt64($progressM.Groups[2].Value, 16) } else { [uint64]0 }
    if ($stepsRun -eq 0 -and $progressSteps -gt 0) {
        $stepsRun = $progressSteps
        $eip = $progressEip
    }

    $faultM = LastMatch $out '(?m)^LastFault vec=(\d+) EIP=0x([0-9A-Fa-f]+) err=0x([0-9A-Fa-f]+) CR2=0x([0-9A-Fa-f]+)'
    $faultVec = if ($faultM) { [int]$faultM.Groups[1].Value } else { 0 }
    $faultEip = if ($faultM) { [Convert]::ToUInt64($faultM.Groups[2].Value, 16) } else { [uint64]0 }
    $faultCr2 = if ($faultM) { [Convert]::ToUInt64($faultM.Groups[4].Value, 16) } else { [uint64]0 }

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

    $eipZone = if ($eip -ge [uint64]2147483648) { "kernel" } elseif ($eip -ge [uint64]65536) { "xbe" } else { "low" }

    # The per-row stage is provisional; the post-pass labels a shared display
    # tuple (the dashboard) as "dashboard" and a unique tuple as "own-render".
    $stage = "unknown"
    if ($timedOut) { $stage = "timeout" }
    elseif ($faultVec -ne 0 -or $out -match '(?m)^Unhandled|Fatal error') { $stage = "fault" }
    elseif ($stepsRun -lt 1000000000) { $stage = "early" }
    else { $stage = "own-render" }

    $row = [ordered]@{
        title = $h.Title.Name
        slug = $h.Title.Slug
        source = $h.Title.Source
        path = $h.Title.Path
        steps = $stepsRun
        wallSeconds = [math]::Round($benchWall, 3)
        processSeconds = [math]::Round($processWall, 3)
        msps = [math]::Round($msps, 2)
        eip = ("0x{0:X8}" -f $eip)
        eipZone = $eipZone
        halted = $halted
        faultVec = $faultVec
        faultEip = ("0x{0:X8}" -f $faultEip)
        faultCr2 = ("0x{0:X8}" -f $faultCr2)
        frames = $frames
        stalls = $stalls
        draws = $draws
        skipped = $skipped
        pixels = $pixels
        culled = $culled
        programs = $programs
        fixedFn = $fixedFn
        stage = $stage
        score = 0
        timedOut = $timedOut
        ram = $ram
        cpu = $cpu
        log = $h.LogPath
        command = "dotnet $Cli boot --bios $FirmwareDir\Complex_4627.bin --mcpx $FirmwareDir\mcpx_1.0.bin --eeprom $FirmwareDir\eeprom.bin --hdd $($h.Hdd) --dvd `"$($h.Title.Path)`" " +
                  $(if (-not $NoFast) { "--jit --jit-threshold 16 --jit-spin --jit-raw " } else { "" }) +
                  "--steps $Steps --gpu $Gpu" + $(if ($GpuReplace) { " --gpu-replace" } else { "" }) +
                  $(if ($PadScript) { " --pad-script `"$PadScript`"" } else { "" })
    }
    $row | ConvertTo-Json -Depth 4 | Set-Content -Path $h.JsonPath -Encoding utf8
    Write-Host ("[{0}] {1}: stage={2} steps={3} wall={4:F1}s msps={5:F1} eip={6} draws={7} px={8} frames={9} fault={10}" -f `
        $h.Index, $h.Title.Name, $stage, $stepsRun, $benchWall, $msps, $row.eip, $draws, $pixels, $frames, $faultVec)
    return [pscustomobject]$row
}

$titles = @(Get-IsoList)
if ($titles.Count -eq 0) { Write-Error "no ISOs found under $MediaDir / $AlsoDir"; exit 2 }
Write-Host ("matrix: {0} titles, steps={1}, parallel={2}, out={3}" -f $titles.Count, $Steps, $Parallel, $OutDir)

$hddPaths = @()
if (-not $NoHddCopy -and $Parallel -gt 1) {
    $hddDir = Join-Path $OutDir "hdd"
    New-Item -ItemType Directory -Force -Path $hddDir | Out-Null
    $sourceHdd = (Resolve-Path -LiteralPath $Hdd).Path
    for ($w = 0; $w -lt $Parallel; $w++) {
        $dst = Join-Path $hddDir "worker-$w.qcow2"
        if (-not (Test-Path $dst)) {
            Write-Host "matrix: staging HDD copy $dst"
            Copy-Item -LiteralPath $sourceHdd -Destination $dst
        }
        $hddPaths += $dst
    }
} else {
    $hddPaths = @((Resolve-Path -LiteralPath $Hdd).Path)
}

$rows = @()
$i = 0
for ($offset = 0; $offset -lt $titles.Count; $offset += $Parallel) {
    $batch = @($titles[$offset..([Math]::Min($offset + $Parallel - 1, $titles.Count - 1))])
    $handles = @()
    $worker = 0
    foreach ($t in $batch) {
        $i++
        $existing = Join-Path $OutDir "$($t.Slug).json"
        if ($Resume -and (Test-Path $existing) -and -not $Force) {
            Write-Host ("[{0}] {1}: resume (row exists)" -f $i, $t.Name)
            try { $rows += (Get-Content $existing -Raw | ConvertFrom-Json) } catch { }
            continue
        }
        $handles += (Start-OneProcess $t $i $hddPaths[$worker % $hddPaths.Count])
        $worker++
    }
    foreach ($h in $handles) { $rows += (Complete-OneProcess $h) }
}

# Re-read every row so -Resume batches and prior runs are included.
$all = @()
foreach ($f in Get-ChildItem -Path $OutDir -Filter *.json -File | Where-Object { $_.Name -ne "summary.json" }) {
    try { $all += (Get-Content $f.FullName -Raw | ConvertFrom-Json) } catch { }
}

# The dashboard's boot animation and menu are shared by every title, so at a
# fixed step budget several titles produce the SAME draw/pixel/frame tuple.
# A tuple shared by >=2 titles is the dashboard; a unique tuple means the title
# took the display. (A fixed draw count only works near 1B; at 5B the shared
# display has many more draws, so the baseline is measured from the run.)
$sharedTuples = @{}
$candidates = @($all | Where-Object { $_.steps -ge 1000000000 -and $_.faultVec -eq 0 -and -not $_.timedOut })
if ($candidates.Count -gt 0) {
    $groups = @($candidates | Group-Object { "$($_.draws)|$($_.pixels)|$($_.frames)" })
    foreach ($g in $groups) {
        if ($g.Count -ge 2) { $sharedTuples[$g.Name] = $g.Count }
    }
}
$baselineKey = if ($sharedTuples.Count -gt 0) {
    ($sharedTuples.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
} else { $null }

foreach ($r in $all) {
    $tuple = "$($r.draws)|$($r.pixels)|$($r.frames)"
    if ($r.timedOut) { $r.stage = "timeout" }
    elseif ($r.faultVec -ne 0) { $r.stage = "fault" }
    elseif ($r.steps -lt 1000000000) { $r.stage = "early" }
    elseif ($sharedTuples.ContainsKey($tuple)) { $r.stage = "dashboard" }
    else { $r.stage = "own-render" }

    $score = 0
    if ($r.stage -eq "own-render") { $score += 100 }
    if ($r.frames -gt 250) { $score += 20 }
    if ($r.programs -gt 10) { $score += 10 }
    if ($r.eipZone -eq "xbe") { $score += 20 }
    if ($r.stage -eq "fault") { $score -= 1000 }
    if ($r.stage -eq "timeout") { $score -= 500 }
    $r.score = $score
}

$all = @($all | Sort-Object @{ Expression = "score"; Descending = $true }, @{ Expression = "frames"; Descending = $true }, Name)

$summary = [ordered]@{
    generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
    steps = $Steps
    gpu = $Gpu
    fast = (-not $NoFast)
    mediaDir = $MediaDir
    alsoDir = $AlsoDir
    baseline = $baselineKey
    count = $all.Count
    ownRender = @($all | Where-Object { $_.stage -eq "own-render" }).Count
    dashboard = @($all | Where-Object { $_.stage -eq "dashboard" }).Count
    faults = @($all | Where-Object { $_.stage -eq "fault" }).Count
    timeouts = @($all | Where-Object { $_.stage -eq "timeout" }).Count
    rows = $all
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutDir "summary.json") -Encoding utf8

if (-not $NoMarkdown) {
    $md = @()
    $md += "# Retail title matrix (M-T)"
    $md += ""
    $md += ("Generated {0} by ``scripts/title-matrix.ps1`` - steps={1}, gpu={2}, fast={3}, padScript={4}." -f $summary.generated, $Steps, $Gpu, (-not $NoFast), $PadScript)
    $md += ""
    $md += ("**{0} titles: {1} own-render, {2} dashboard, {3} fault, {4} timeout.**" -f $summary.count, $summary.ownRender, $summary.dashboard, $summary.faults, $summary.timeouts)
    $md += ""
    $md += "``dashboard`` means the row matches a draw/pixel/frame tuple shared by two or more titles (the dashboard boot animation or menu); ``own-render`` means a unique tuple (the title took the display). ``early`` means fewer than 1B steps ran. ``fault``/``timeout`` are rows, not harness failures."
    $md += ""
    $md += "| # | title | source | stage | score | EIP | EIP zone | steps | wall s | msps | draws | pixels | frames | fault |"
    $md += "|---:|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|"
    $n = 0
    foreach ($r in $all) {
        $n++
        $md += ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |" -f `
            $n, $r.title, $r.source, $r.stage, $r.score, $r.eip, $r.eipZone, $r.steps, $r.wallSeconds, $r.msps, $r.draws, $r.pixels, $r.frames, $r.faultVec)
    }
    $md += ""
    $md += "## Reading the table"
    $md += ""
    $md += "- **own-render** rows are the closest to playable; probe them first (TM1)."
    $md += "- **dashboard** rows still present the shared dashboard display; their EIP says whether the title code or the kernel idle is executing."
    $md += "- **fault** rows name the vector, EIP and CR2; a cluster of identical faults is one shared blocker."
    $md += "- **timeout** rows are hangs or very slow titles; re-run them alone with a longer timeout before calling them blockers."
    $md += ""
    $md += "Full logs and per-title JSON are in ``wt-data/title-matrix/``."
    Set-Content -Path "docs\research-notes\title-matrix.md" -Value ($md -join "`n") -Encoding utf8
}

Write-Host ("matrix: done - {0} rows, {1} own-render, {2} dashboard, {3} fault, {4} timeout" -f $summary.count, $summary.ownRender, $summary.dashboard, $summary.faults, $summary.timeouts)
exit 0
