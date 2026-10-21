# Track E: boot-to-first-frame harness (compatibility corpus).
#
# Runs one corpus sample headless to a fixed step count and records the
# evidence a matrix row needs: steps, wall, throughput, the NV2A draw/flip
# counters, the step anchor of the FIRST presented frame, and the SHA-256 of
# the first and last presented frames. One invocation per row; the output line
# (BTFF ...) is what goes into docs/compatibility.md and the JSON record keeps
# the full detail.
#
# This is corpus evidence, not a gate: the standing gates stay the accurate-mode
# 1B goldens and the ktest smoke (docs/development-direction.md section 6).
#
# Usage (from the repo root, after dotnet build -c Release):
#   pwsh -File scripts/boot-to-first-frame.ps1 -Sample triangle
#   pwsh -File scripts/boot-to-first-frame.ps1 -Sample dashboard -Gpu gl -GpuReplace
#   pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-spheremap_ffp -Ffp   # fixed-function vertex stage
#   pwsh -File scripts/boot-to-first-frame.ps1 -All
#   pwsh -File scripts/boot-to-first-frame.ps1 -Corpus          # every staged nxdk sample
#   pwsh -File scripts/boot-to-first-frame.ps1 -Iso path\to\x.iso   # any loose ISO
#
# Corpus samples are DVD-only (no HDD) so the boot path is the sample itself.
# Stage them with scripts/build-nxdk-samples.ps1.
#
# Exit 0 = harness ran and the sample presented at least one frame,
# 1 = sample faulted / no frame presented, 2 = environment problem.

param(
    # No ValidateSet on purpose: any ISO staged in $CorpusDir is a valid sample.
    [string]$Sample = "triangle",
    [ValidateSet("sw", "gl")][string]$Gpu = "sw",
    [switch]$GpuReplace,
    [switch]$NoFast,
    [switch]$Ffp,
    [switch]$All,
    [switch]$Corpus,
    [string]$IsoPath = "",
    [string]$CorpusDir = "data\test-samples\corpus",
    [ulong]$CorpusSteps = 1000000000,
    [ulong]$Steps = 0,
    [string]$OutDir = "wt-data\btff",
    [string]$Cli = "",
    [string]$FirmwareDir = "data\xemu-0.8.136-windows-x86_64",
    [string]$Hdd = "wt-data\chain-199l\hdd.qcow2",
    [string]$TriangleIso = "wt-data\xemu-tri\triangle.iso",
    [string]$KtestIso = "wt-data\ktest-smoke\xgrid\ktest-smoke.iso"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $Cli) { $Cli = Join-Path $root "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
if (-not (Test-Path $FirmwareDir)) { Write-Error "Missing firmware dir: $FirmwareDir"; exit 2 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$padScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"

function Resolve-Sample([string]$name) {
    switch ($name) {
        "dashboard" {
            if (-not (Test-Path $Hdd)) { throw "dashboard needs the HDD image: $Hdd" }
            return [pscustomobject]@{
                Name     = "dashboard"
                Media    = @("--hdd", (Resolve-Path $Hdd).Path, "--pad-script", $padScript)
                Steps    = if ($Steps -gt 0) { $Steps } else { [uint64]1000000000 }
                FirstFrame = "EvoX dashboard menu"
            }
        }
        "triangle" {
            if (-not (Test-Path $TriangleIso)) { throw "triangle needs the DVD image: $TriangleIso" }
            if (-not (Test-Path $Hdd)) { throw "triangle needs the HDD image (gate configuration): $Hdd" }
            return [pscustomobject]@{
                Name     = "triangle"
                # Same media set as the slice-2 golden gate (HDD + DVD).
                Media    = @("--hdd", (Resolve-Path $Hdd).Path, "--dvd", (Resolve-Path $TriangleIso).Path)
                Steps    = if ($Steps -gt 0) { $Steps } else { [uint64]1000000000 }
                FirstFrame = "nxdk Gouraud triangle"
            }
        }
        "ktest" {
            if (-not (Test-Path $KtestIso)) {
                throw "ktest needs the smoke ISO ($KtestIso); stage it with scripts/ktest-smoke.ps1 -SkipXemu"
            }
            return [pscustomobject]@{
                Name     = "ktest"
                Media    = @("--hdd", (Resolve-Path $Hdd).Path, "--dvd", (Resolve-Path $KtestIso).Path)
                Steps    = if ($Steps -gt 0) { $Steps } else { [uint64]600000000 }
                FirstFrame = "console output (disable-video=1: no frame expected)"
            }
        }
        default {
            # Corpus sample: a standalone nxdk ISO from data\test-samples\corpus.
            # HDD + DVD, the same media set the triangle gate row uses: without
            # the HDD the kernel never launches the disc's XBE and the run stops
            # at the boot animation (measured 2026-10-20).
            $sampleIso = if ($IsoPath) { $IsoPath } else { Join-Path $CorpusDir "$name.iso" }
            if (-not (Test-Path $sampleIso)) {
                throw "sample '$name' has no corpus ISO at $sampleIso (build with scripts/build-nxdk-samples.ps1)"
            }
            if (-not (Test-Path $Hdd)) { throw "corpus samples need the HDD image: $Hdd" }
            return [pscustomobject]@{
                Name     = $name
                Media    = @("--hdd", (Resolve-Path $Hdd).Path, "--dvd", (Resolve-Path $sampleIso).Path)
                Steps    = if ($Steps -gt 0) { $Steps } else { $CorpusSteps }
                FirstFrame = "nxdk sample: $name"
            }
        }
    }
}

function Run-Sample([string]$name, [string]$gpu, [bool]$replace) {
    $spec = Resolve-Sample $name
    $expectNoFrame = $name -eq "ktest"
    $tag = "$name-$gpu$(if ($replace) { '-replace' } else { '' })$(if ($Ffp) { '-ffp' } else { '' })"
    $log = Join-Path $OutDir "$tag.log"
    $firstBmp = Join-Path $OutDir "$tag-first.bmp"
    $lastBmp = Join-Path $OutDir "$tag-last.bmp"
    $scanBmp = Join-Path $OutDir "$tag-scanout.bmp"
    $displayBmp = Join-Path $OutDir "$tag-display.bmp"
    $json = Join-Path $OutDir "$tag.json"

    $argv = @(
        "boot",
        "--bios", (Join-Path $FirmwareDir "Complex_4627.bin"),
        "--mcpx", (Join-Path $FirmwareDir "mcpx_1.0.bin"),
        "--eeprom", (Join-Path $FirmwareDir "eeprom.bin")
    ) + $spec.Media
    if (-not $NoFast) { $argv += @("--jit", "--jit-threshold", "16", "--jit-spin", "--jit-raw") }
    if ($Ffp) { $argv += "--ffp" }
    $argv += @("--steps", "$($spec.Steps)", "--gpu", $gpu, "--frame", $lastBmp, "--frame-first", $firstBmp,
        "--frame-scanout", $scanBmp, "--frame-display", $displayBmp, "--bench", "--bench-name", $tag)
    if ($replace) { $argv += "--gpu-replace" }

    & dotnet $Cli @argv *>&1 | Out-File $log

    function Last([string]$pattern) {
        $m = Select-String -Path $log -Pattern $pattern | Select-Object -Last 1
        if ($m) { return $m.Line }
        return ""
    }

    $bench = Last "^BENCH name="
    if (-not $bench) { Write-Error "no BENCH line in $log (see the log)"; return $null }
    $draws = Last "^NV2A draws executed="
    $fault = Last "^LastFault"
    $firstLine = Last "^FrameFirst step="
    $frameLine = Last "^Frame "

    $wall = [double][regex]::Match($bench, "wall=([0-9.]+)").Groups[1].Value
    $msps = [double][regex]::Match($bench, "msps=([0-9.]+)").Groups[1].Value
    $steps = [uint64][regex]::Match($bench, "steps=(\d+)").Groups[1].Value
    $ram = [regex]::Match($bench, "ramSha256=([0-9A-Fa-f]+)").Groups[1].Value
    $cpu = [regex]::Match($bench, "cpuSha256=([0-9A-Fa-f]+)").Groups[1].Value
    $frames = [regex]::Match($draws, "frames=(\d+)").Groups[1].Value
    $stalls = [regex]::Match($draws, "stalls=(\d+)").Groups[1].Value
    $executedDraws = [regex]::Match($draws, "draws executed=(\d+)").Groups[1].Value
    $pixels = [regex]::Match($draws, "pixels=(\d+)").Groups[1].Value
    $firstStep = [regex]::Match($firstLine, "step=(\d+)").Groups[1].Value
    $vec = [regex]::Match($fault, "vec=(\d+)").Groups[1].Value

    function Sha-Of([string]$path) {
        if (Test-Path $path) { return (Get-FileHash $path -Algorithm SHA256).Hash }
        return "-"
    }

    $firstSha = Sha-Of $firstBmp
    $lastSha = Sha-Of $lastBmp
    $scanSha = Sha-Of $scanBmp
    $displaySha = Sha-Of $displayBmp
    $verdict = if ($fault -match "vec=0" -and ([int]$frames -gt 0 -or $expectNoFrame)) { "PASS" } else { "CHECK" }

    $record = [pscustomobject]@{
        sample         = $name
        gpu            = $gpu
        replace        = $replace
        fast           = (-not $NoFast)
        steps          = $steps
        wallSeconds    = $wall
        msps           = $msps
        frames         = [int]$frames
        flipStalls     = [int]$stalls
        drawsExecuted  = [int]$executedDraws
        pixels         = [int]$pixels
        firstFrameStep = if ($firstStep) { [uint64]$firstStep } else { [uint64]0 }
        firstFrameSha  = $firstSha
        lastFrameSha   = $lastSha
        scanoutSha     = $scanSha
        displaySha     = $displaySha
        ramSha256      = $ram
        cpuSha256      = $cpu
        lastFault      = $fault
        verdict        = $verdict
        log            = $log
        command        = "dotnet $Cli boot --bios $FirmwareDir\Complex_4627.bin --mcpx $FirmwareDir\mcpx_1.0.bin " +
                         "--eeprom $FirmwareDir\eeprom.bin " + ($spec.Media -join " ") +
                         $(if (-not $NoFast) { " --jit --jit-threshold 16 --jit-spin --jit-raw" } else { "" }) +
                         " --steps $steps --gpu $gpu" + $(if ($replace) { " --gpu-replace" } else { "" }) +
                         $(if ($Ffp) { " --ffp" } else { "" })
    }

    $record | ConvertTo-Json | Set-Content $json
    Write-Host ("BTFF sample={0} gpu={1}{2} steps={3} wall={4:F2} msps={5:F2} frames={6} stalls={7} draws={8} pixels={9} firstFrameStep={10} firstFrameSha={11} lastFrameSha={12} scanoutSha={13} displaySha={14} ram={15} cpu={16} {17}" -f `
        $name, $gpu, $(if ($replace) { "+replace" } else { "" }), $steps, $wall, $msps, $frames, $stalls,
        $executedDraws, $pixels, $(if ($firstStep) { $firstStep } else { "-" }),
        $firstSha.Substring(0, [Math]::Min(16, $firstSha.Length)),
        $lastSha.Substring(0, [Math]::Min(16, $lastSha.Length)),
        $scanSha.Substring(0, [Math]::Min(16, $scanSha.Length)),
        $displaySha.Substring(0, [Math]::Min(16, $displaySha.Length)),
        $ram.Substring(0, [Math]::Min(8, $ram.Length)),
        $cpu.Substring(0, [Math]::Min(8, $cpu.Length)), $verdict)
    return $record
}

$failed = $false
if ($Corpus) {
    # Every staged corpus ISO, on the selected backend(s). Rows go into
    # docs/compatibility.md; a sample that fails to present is a CHECK row,
    # not a crash of the harness.
    $isos = @(Get-ChildItem $CorpusDir -Filter *.iso -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($isos.Count -eq 0) {
        Write-Error "no corpus ISOs in $CorpusDir (build with scripts/build-nxdk-samples.ps1)"
        exit 2
    }

    foreach ($corpusIso in $isos) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($corpusIso.Name)
        if ($name -in @("dashboard", "triangle", "ktest")) {
            Write-Host "BTFF corpus: skipping $name (a named gate sample owns that name; use -Sample $name)"
            continue
        }

        Write-Host "BTFF corpus: running $name from $($corpusIso.FullName)"
        try {
            $r = Run-Sample $name $Gpu $GpuReplace.IsPresent
            if ($null -eq $r -or $r.verdict -ne "PASS") { $failed = $true }
        }
        catch {
            Write-Host "BTFF sample=$name gpu=$Gpu SKIP ($($_.Exception.Message))"
            $failed = $true
        }
    }
}
elseif ($All) {
    foreach ($s in @("dashboard", "triangle", "ktest")) {
        foreach ($g in @("sw", "gl")) {
            try {
                $r = Run-Sample $s $g ($g -eq "gl")
                if ($null -eq $r -or $r.verdict -ne "PASS") { $failed = $true }
            }
            catch {
                Write-Host "BTFF sample=$s gpu=$g SKIP ($($_.Exception.Message))"
            }
        }
    }
}
else {
    $r = Run-Sample $Sample $Gpu $GpuReplace.IsPresent
    if ($null -eq $r -or $r.verdict -ne "PASS") { $failed = $true }
}

if ($failed) { exit 1 }
exit 0
