# GPU-backend slice-3 007 harness (gpu-backend-design.md §6 slice 3).
# Two bounded 007 segments, each run --gpu sw vs --gpu gl from a v2 snapshot:
#   - "fmv"        (pd181 + 1B): steady intro-FMV playback. Presents are
#                  CPU-blitted surfaces + flip; the segment executes ZERO
#                  PGRAPH draws, so the GL backend (draw/clear -> FBO model)
#                  legitimately has no pixels: sw saves a frame, gl saves
#                  none. Acceptance = structural equality + BackendUnsupported=0
#                  + the documented coherence-gap signature (no gl frame).
#   - "transition" (pd536 + 1B): post-stream-end screen transition. The guest
#                  issues 1 fullscreen draw + clears onto a new surface; the
#                  backend renders it. Acceptance = full §3.4 golden (structural
#                  equality, BackendUnsupported=0, frames byte-identical to the
#                  recorded SHA 18C7CC83...).
# Requires private media copies in wt-data\ (not committed); snapshots copied
# from the 007-streamend worktree (read-only source); shared CHD on E:.
#
# Usage (from the worktree root, after dotnet build -c Release):
#   pwsh -File scripts/gpu-slice3-007.ps1 [-Segment fmv|transition|both] [-OutDir .]
#
# Exit 0 = GOLDEN: PASS (or documented-GAP PASS for fmv), 1 = FAIL, 2 = environment.

param(
    [string]$Segment = "both",
    [string]$OutDir = ".",
    [string]$Bios = "wt-data\Complex_4627.bin",
    [string]$Mcpx = "wt-data\mcpx_1.0.bin",
    [string]$Eeprom = "wt-data\eeprom.bin",
    [string]$Hdd = "wt-data\xbox_hdd.qcow2",
    [string]$Dvd = "E:\Microsoft Xbox\007 - Agent Under Fire (USA).chd",
    [string]$ExpectedTransitionSha = "18C7CC83CEA392DEA278E6449F28276616158616FE8AE0D62337056DDC5308F0"
)

$ErrorActionPreference = "Stop"
$cli = "src\XGrid.Cli\bin\Release\net11.0\XGrid.Cli.dll"
if (-not (Test-Path $cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
if (-not (Test-Path $Dvd)) { Write-Error "007 CHD not found: $Dvd"; exit 2 }

$segments = @()
if ($Segment -eq "both" -or $Segment -eq "fmv") { $segments += ,@("fmv", "pd181.xgs", "1000000000") }
if ($Segment -eq "both" -or $Segment -eq "transition") { $segments += ,@("transition", "pd536.xgs", "1000000000") }
if ($segments.Count -eq 0) { Write-Error "Unknown -Segment '$Segment' (expected fmv|transition|both)"; exit 2 }

function Get-LogLine([string]$path, [string]$pattern) {
    Select-String -Path $path -Pattern $pattern | Select-Object -First 1 -ExpandProperty Line
}

$fail = $false

foreach ($seg in $segments) {
    $name = $seg[0]; $snap = $seg[1]; $steps = $seg[2]
    if (-not (Test-Path $snap)) { Write-Error "Snapshot not found: $snap"; exit 2 }
    $swBmp = Join-Path $OutDir "007-$name-sw.bmp"
    $glBmp = Join-Path $OutDir "007-$name-gl.bmp"
    $swLog = Join-Path $OutDir "007-$name-sw.log"
    $glLog = Join-Path $OutDir "007-$name-gl.log"
    $baseArgs = @(
        "boot", "--bios", $Bios, "--mcpx", $Mcpx, "--eeprom", $Eeprom, "--hdd", $Hdd,
        "--dvd", $Dvd, "--load-state", $snap, "--steps", $steps
    )

    Write-Host "== [$name] sw run =="
    $swWall = Measure-Command { & dotnet $cli @baseArgs --gpu sw --frame $swBmp > $swLog 2>&1 }
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: [$name] sw exit=$LASTEXITCODE"; $fail = $true; continue }
    Write-Host "== [$name] gl run =="
    $glWall = Measure-Command { & dotnet $cli @baseArgs --gpu gl --frame $glBmp > $glLog 2>&1 }
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: [$name] gl exit=$LASTEXITCODE"; $fail = $true; continue }

    foreach ($log in @($swLog, $glLog)) {
        $line = Get-LogLine $log "LastFault"
        if ($line -notmatch "LastFault vec=0") { Write-Host "FAIL: [$name] $log -> $line"; $fail = $true }
        else { Write-Host "OK: [$name] $log fault-free" }
    }

    function Get-Counters([string]$path) {
        $line = Get-LogLine $path "NV2A draws executed"
        $m = [regex]::Match($line, "draws executed=(\d+).*frames=(\d+) stalls=(\d+)")
        return ,@([int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value)
    }
    $swC = Get-Counters $swLog
    $glC = Get-Counters $glLog
    if (($swC -join ",") -ne ($glC -join ",")) {
        Write-Host "FAIL: [$name] counters sw=($($swC -join ',')) gl=($($glC -join ','))"; $fail = $true
    } else {
        Write-Host "OK: [$name] counters identical draws=$($swC[0]) frames=$($swC[1]) stalls=$($swC[2])"
    }
    $fpsSw = if ($swWall.TotalSeconds -gt 0) { $swC[1] / $swWall.TotalSeconds } else { 0 }
    $fpsGl = if ($glWall.TotalSeconds -gt 0) { $glC[1] / $glWall.TotalSeconds } else { 0 }
    Write-Host ("FPS: [{0}] sw {1:F2} flips/s ({2} flips / {3:F1}s) gl {4:F2} flips/s ({5} flips / {6:F1}s)" -f $name, $fpsSw, $swC[1], $swWall.TotalSeconds, $fpsGl, $glC[1], $glWall.TotalSeconds)

    $glProbe = Get-LogLine $glLog "GPU backend="
    if ($glProbe -notmatch "GPU backend=gl probe ok") { Write-Host "FAIL: [$name] gl probe: $glProbe"; $fail = $true }
    else { Write-Host "OK: [$name] $glProbe" }

    $glLine = Get-LogLine $glLog "BackendUnsupported="
    if ($glLine -notmatch "BackendUnsupported=0\b") { Write-Host "FAIL: [$name] $glLine"; $fail = $true }
    else { Write-Host "OK: [$name] $glLine" }
    $diag = [regex]::Match($glLine, "diag\[[^\]]*\]")
    if ($diag.Success) { Write-Host "INFO: [$name] $($diag.Value)" }
    $timed = [regex]::Match($glLine, "timed\[[^\]]*\]")
    if ($timed.Success) { Write-Host "INFO: [$name] $($timed.Value)" }

    $swExists = Test-Path $swBmp
    $glExists = Test-Path $glBmp
    if ($name -eq "fmv") {
        # Coherence-gap signature: sw saves the FMV frame; gl has no FBO
        # content (zero draws in the segment) and saves nothing. Every
        # backend flip must be an empty-FBO flip (diag flipEmpty == flips):
        # the backend never mixes software pixels into its readback.
        if (-not $swExists) { Write-Host "FAIL: [fmv] sw frame missing"; $fail = $true }
        if ($glExists) { Write-Host "FAIL: [fmv] gl frame unexpectedly present (gap signature changed)"; $fail = $true }
        else { Write-Host "OK: [fmv] documented coherence gap holds (sw frame only, backend draws=0)" }
        $mFlip = [regex]::Match($glLine, "flips=(\d+)")
        $mEmpty = [regex]::Match($glLine, "flipEmpty=(\d+)")
        if (-not $mFlip.Success -or -not $mEmpty.Success) { Write-Host "FAIL: [fmv] no flips/flipEmpty in: $glLine"; $fail = $true }
        elseif ($mFlip.Groups[1].Value -ne $mEmpty.Groups[1].Value) { Write-Host "FAIL: [fmv] flipEmpty=$($mEmpty.Groups[1].Value) != flips=$($mFlip.Groups[1].Value)"; $fail = $true }
        else { Write-Host "OK: [fmv] flipEmpty=$($mEmpty.Groups[1].Value) == flips (all presents CPU-blitted)" }
    } else {
        if (-not $swExists -or -not $glExists) { Write-Host "FAIL: [transition] frame missing sw=$swExists gl=$glExists"; $fail = $true; continue }
        $swSha = (Get-FileHash $swBmp -Algorithm SHA256).Hash
        $glSha = (Get-FileHash $glBmp -Algorithm SHA256).Hash
        if ($swSha -ne $ExpectedTransitionSha) { Write-Host "FAIL: [transition] sw SHA $swSha != recorded"; $fail = $true }
        else { Write-Host "OK: [transition] sw SHA matches recorded oracle" }
        if ($glSha -ne $swSha) { Write-Host "FAIL: [transition] gl SHA $glSha != sw SHA $swSha"; $fail = $true }
        else { Write-Host "OK: [transition] gl frame byte-identical to sw" }
        $mEmpty = [regex]::Match($glLine, "flipEmpty=(\d+)")
        if ($mEmpty.Success -and $mEmpty.Groups[1].Value -ne "0") { Write-Host "FAIL: [transition] flipEmpty=$($mEmpty.Groups[1].Value) (expected 0 — the flip served rendered content)"; $fail = $true }
        elseif ($mEmpty.Success) { Write-Host "OK: [transition] flipEmpty=0 (flip served backend-rendered content)" }
    }
}

if ($fail) { Write-Host "GOLDEN: FAIL"; exit 1 }
Write-Host "GOLDEN: PASS"
exit 0
