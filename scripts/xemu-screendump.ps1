# Capture one frame from xemu through QMP over stdio (no sockets: this host
# cannot bind the monitor socket the ktest harness uses).
#
# STATUS 2026-10-21: **STAGED, NOT VALIDATED.** The QMP handshake over stdio
# works (greeting, qmp_capabilities, cont, query-status all answer correctly),
# xemu loads the BIOS and starts the DVD, but no frame has been captured yet:
# the script's read loop hangs or the process exits early once the VM runs
# under redirected stdio. Dead ends already tried and recorded:
#   - `-display sdl`: this build has no `sdl` display backend ("Display 'sdl'
#     is not available"); use `-display xemu`.
#   - `-monitor stdio` (HMP): no monitor banner, no reply, no screendump.
#   - `-drive index=1,...`: xemu already builds that drive from its settings
#     ("drive with bus=0, unit=1 exists"); pass `-dvd_path` instead.
#   - The BIOS is not in xemu.toml: without `-bios` it fails with
#     "Failed to load BIOS '(null)'".
# A more promising route is xemu's own F12 screenshot (ui/xui/actions.cc
# SaveScreenshot writes xemu-<timestamp>.png into general.screenshot_dir, "."
# by default), which needs the window focused for a synthetic key.
#
# Usage:
#   pwsh -File scripts/xemu-screendump.ps1 [-Iso <iso>] [-BootSeconds 60] [-Name frame]
#
# xemu prints its "Created QEMU launch parameters ..." line on stdout before the
# QMP greeting, and starts paused (prelaunch), so responses are matched
# explicitly: skip non-JSON lines, then capabilities + cont, then screendump.
param(
    [string]$Iso = "data\test-samples\corpus\xgu-swizzle_texture.iso",
    [string]$OutDir = "wt-data\xemu-oracle",
    [int]$BootSeconds = 60,
    [string]$Name = "frame"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outPpm = (Join-Path (Resolve-Path $OutDir) "$Name.ppm") -replace '\\', '/'
$exe = Join-Path $root "data\xemu-0.8.136-windows-x86_64\xemu.exe"
$bios = Join-Path $root "data\xemu-0.8.136-windows-x86_64\Complex_4627.bin"
if (-not (Test-Path $exe)) { Write-Error "xemu.exe not found: $exe"; exit 2 }
if (-not (Test-Path $bios)) { Write-Error "BIOS not found: $bios"; exit 2 }
if (-not (Test-Path $Iso)) { Write-Error "ISO not found: $Iso"; exit 2 }

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.Arguments = @(
    "-bios", $bios,
    "-dvd_path", (Resolve-Path $Iso).Path,
    "-display", "xemu",
    "-qmp", "stdio",
    "-serial", "none"
) -join ' '
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $false

$process = [System.Diagnostics.Process]::Start($psi)
$stderrTask = $process.StandardError.ReadToEndAsync()

function Read-Json([int]$timeoutMs = 10000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [int]([DateTime]::UtcNow - $deadline).TotalMilliseconds + $timeoutMs
        $task = $process.StandardOutput.ReadLineAsync()
        if (-not $task.Wait([Math]::Max(1, $remaining))) { return $null }
        $line = $task.Result
        if ($null -eq $line) { return $null }
        if ($line.StartsWith('{')) { return $line }
    }

    return $null
}

function Send-Qmp([string]$json, [int]$timeoutMs = 10000) {
    $process.StandardInput.WriteLine($json)
    $process.StandardInput.Flush()
    return Read-Json $timeoutMs
}

Write-Host "greeting: $(Read-Json 15000)"
Write-Host "caps:     $(Send-Qmp '{"execute":"qmp_capabilities"}')"
Write-Host "cont:     $(Send-Qmp '{"execute":"cont"}')"
Start-Sleep -Milliseconds 500
Write-Host "status:   $(Send-Qmp '{"execute":"query-status"}')"

Start-Sleep -Seconds $BootSeconds
Write-Host "status2:  $(Send-Qmp '{"execute":"query-status"}')"
Write-Host "dump:     $(Send-Qmp ('{"execute":"human-monitor-command","arguments":{"command-line":"screendump ' + $outPpm + '"}}') 20000)"

Send-Qmp '{"execute":"quit"}' 3000 | Out-Null
if (-not $process.HasExited -and -not $process.WaitForExit(15000)) { $process.Kill() }

if ($stderrTask.Wait(5000)) {
    $err = $stderrTask.Result
    if ($err) { $err.Split("`n") | Select-Object -Last 4 }
}

$localPpm = $outPpm -replace '/', '\'
if (Test-Path $localPpm) {
    $info = Get-Item $localPpm
    Write-Host "SCREENDUMP ok $($info.FullName) $($info.Length) bytes"
} else {
    Write-Host "SCREENDUMP missing"
}
