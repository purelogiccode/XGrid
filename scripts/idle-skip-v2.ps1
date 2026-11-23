# Idle-skip V2 harness (research-notes §119): one measured boot run with
# fresh private media copies. Logs wall-clock + full CLI output + frame BMP
# into the out dir (media copies are re-copied each run, never shared).
#
# Usage (from the worktree root, after dotnet build -c Release):
#   pwsh -File scripts/idle-skip-v2.ps1 -Workload dash -Out runs\d0 -Mode off
#   pwsh -File scripts/idle-skip-v2.ps1 -Workload tri  -Out runs\t1 -Mode probe
#   pwsh -File scripts/idle-skip-v2.ps1 -Workload dash -Out runs\d3 -Mode on

param(
    [Parameter(Mandatory = $true)][ValidateSet("dash", "tri")][string]$Workload,
    [Parameter(Mandatory = $true)][string]$Out,
    [ValidateSet("off", "probe", "on")][string]$Mode = "off",
    [string]$Steps = "1000000000",
    [string]$Cli = "src\XGrid.Cli\bin\Release\net11.0\XGrid.Cli.dll",
    [string]$TriangleIso = ""
)

$ErrorActionPreference = "Stop"
# Default to the repo's staged ISO instead of an ephemeral per-user temp path.
if ($TriangleIso -eq "") { $TriangleIso = Join-Path (Split-Path -Parent $PSScriptRoot) "wt-data\xemu-tri\triangle.iso" }
if (-not (Test-Path $Cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$media = Join-Path $Out "media"
New-Item -ItemType Directory -Force -Path $media | Out-Null
Copy-Item -Force wt-data\Complex_4627.bin, wt-data\mcpx_1.0.bin, wt-data\eeprom.bin, wt-data\xbox_hdd.qcow2 $media\

$bootArgs = @(
    "boot", "--bios", "$media\Complex_4627.bin", "--mcpx", "$media\mcpx_1.0.bin",
    "--eeprom", "$media\eeprom.bin", "--hdd", "$media\xbox_hdd.qcow2"
)
if ($Workload -eq "tri") {
    Copy-Item -Force $TriangleIso (Join-Path $media "triangle.iso")
    $bootArgs += @("--dvd", "$media\triangle.iso")
} elseif ($Mode -eq "off") {
    # Pad-script menu journey + the two §111 neutral-recipe break-log probes.
    # NOTE: --break-log drives Cpu.Step() directly (bypasses Execute), so it is
    # flag-off-only per §111's recipe rule; flag-on/probe runs use pad-script only.
    $bootArgs += @(
        "--pad-script", "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000",
        "--break-log", "1859D", "--break-log", "1E420"
    )
} else {
    $bootArgs += @(
        "--pad-script", "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"
    )
}

$bootArgs += @("--steps", $Steps, "--frame", (Join-Path $Out "frame.bmp"))
switch ($Mode) {
    "probe" { $bootArgs += "--idle-skip-probe" }
    "on" { $bootArgs += "--idle-skip" }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$run = & dotnet $Cli @bootArgs 2>&1 | Tee-Object -FilePath (Join-Path $Out "run.log")
$sw.Stop()
$exit = $LASTEXITCODE
$wall = $sw.Elapsed.TotalSeconds
Add-Content -Path (Join-Path $Out "run.log") -Value "WALL_SECONDS=$([math]::Round($wall, 2)) EXIT=$exit"
$frame = Join-Path $Out "frame.bmp"
$sha = if (Test-Path $frame) { (Get-FileHash $frame -Algorithm SHA256).Hash } else { "NO_FRAME" }
Write-Host "[$Workload/$Mode] wall=$([math]::Round($wall, 2))s exit=$exit frameSHA=$sha"
if ($exit -ne 0) { exit $exit }
