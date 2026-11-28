# End-to-end save-state check for the shell and the in-process core.
#
# Boots the console through the real shell, saves a rendered frame with F6,
# lets the guest run on, loads it back with F7 and compares the pictures. The
# snapshot must also be in the hard disk image (qemu-img), so the check covers
# both the shell and the core.
#
#   pwsh -File scripts/save-state-check.ps1
#   pwsh -File scripts/save-state-check.ps1 -Iso 'H:\Tests\XBOXReady\...' -Keep
#   pwsh -File scripts/save-state-check.ps1 -RawHdd
#   pwsh -File scripts/save-state-check.ps1 -Hdd D:\Bench\hdd.raw -RawHdd
#
# -RawHdd uses a raw HDD (converting the development image unless -Hdd is
# supplied) and checks the dedicated xbox_snapshots.qcow2 store instead of the
# HDD image.
#
# Requires: a built core (scripts/build-native-xemu.ps1) and a Release build of
# the frontend (dotnet build src/XGrid.Frontend.Avalonia -c Release).

param(
    [string]$Iso = '',
    [string]$WorkDir = '',
    [int]$FirstFrameTimeoutSeconds = 180,
    [int]$OperationTimeoutSeconds = 120,
    [int]$SettleSeconds = 20,
    [string]$DisplayCrop = '0.18,0.18,0.82,0.88',
    [string]$Hdd = '',
    [switch]$RawHdd,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($WorkDir)) {
    $WorkDir = Join-Path $root 'wt-data/save-state-check'
}

$core = Join-Path $root 'xemu/dist/xemu.exe'
$library = Join-Path $root 'xemu/dist/xgrid_core.dll'
# The build leaves the QEMU tools in the object tree; older layouts shipped
# them beside the core.
$qemuImg = @(
    (Join-Path $root 'xemu/dist/qemu-img.exe'),
    (Join-Path $root 'xemu/build/qemu-img.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
$frontend = Join-Path $root 'src/XGrid.Frontend.Avalonia/bin/Release/net11.0/XGrid.Frontend.Avalonia.exe'
$firmware = Join-Path $root 'data/xemu'

if ($null -eq $qemuImg) {
    throw 'qemu-img.exe was not found; build it with: ninja -C xemu/build qemu-img.exe'
}

# qemu-img links the same runtime DLLs as the core; they sit beside the core.
$env:PATH = (Join-Path $root 'xemu/dist') + ';' + $env:PATH

foreach ($required in @($core, $library, $qemuImg, $frontend)) {
    if (-not (Test-Path $required)) {
        throw "Missing $required (build the core with scripts/build-native-xemu.ps1 and the frontend with -c Release)."
    }
}

if (-not [string]::IsNullOrWhiteSpace($Iso) -and -not (Test-Path $Iso)) {
    throw "Missing disc image $Iso"
}

$work = $WorkDir
if (Test-Path $work) {
    Remove-Item -Recurse -Force $work
}
New-Item -ItemType Directory -Force $work, (Join-Path $work 'data') | Out-Null

# A private copy: the snapshot is written into this image, so the repository's
# development HDD stays untouched. -Hdd uses the supplied image instead.
if (-not [string]::IsNullOrWhiteSpace($Hdd)) {
    if (-not (Test-Path $Hdd)) {
        throw "Missing hard disk image $Hdd"
    }

    $hdd = (Resolve-Path $Hdd).Path
}
else {
    $hdd = Join-Path $work $(if ($RawHdd) { 'hdd.raw' } else { 'hdd.qcow2' })
    if ($RawHdd) {
        Write-Output "converting the hard disk image to $hdd ..."
        & $qemuImg dd -f qcow2 -O raw if=(Join-Path $firmware 'xbox_hdd.qcow2') of=$hdd bs=1M
        if ($LASTEXITCODE -ne 0) { throw 'qemu-img dd failed' }
    }
    else {
        Write-Output "copying the hard disk image to $hdd ..."
        Copy-Item (Join-Path $firmware 'xbox_hdd.qcow2') $hdd
    }
}

$stderrPath = Join-Path $work 'shell-stderr.txt'
$stdoutPath = Join-Path $work 'shell-stdout.txt'
$logDirectory = Join-Path $work 'logs'
$dataDir = Join-Path $work 'data'
$snapshotStore = if ($RawHdd) {
    Join-Path $dataDir 'xemu/xbox_snapshots.qcow2'
}
else {
    $hdd
}
$screenshots = Join-Path $dataDir 'screenshots'
$frameDiff = Join-Path $PSScriptRoot 'frame-diff.py'

$settings = @{
    BiosPath       = (Join-Path $firmware 'Complex_4627.bin')
    McpxRomPath    = (Join-Path $firmware 'mcpx_1.0.bin')
    EepromPath     = (Join-Path $firmware 'eeprom.bin')
    HddPath        = $hdd
    DvdPath        = $Iso
    XemuCorePath   = $core
    CoreHosting    = 'in-process'
    DisplaySink    = $true
    RamMb          = 64
    LibraryFolders = @()
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $dataDir 'settings.json') -Encoding utf8

Add-Type -Namespace XGridCheck -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern bool PostMessage(System.IntPtr hWnd, uint Msg, System.IntPtr wParam, System.IntPtr lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr hWnd);
'@

function Send-Key([IntPtr]$handle, [int]$virtualKey, [int]$holdMilliseconds = 60) {
    [void][XGridCheck.Native]::PostMessage($handle, 0x0100, [IntPtr]$virtualKey, [IntPtr]1)
    Start-Sleep -Milliseconds $holdMilliseconds
    [void][XGridCheck.Native]::PostMessage($handle, 0x0101, [IntPtr]$virtualKey, [IntPtr]0xC0000001)
}

function Wait-Trace([string]$pattern, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $sources = @($stderrPath)
        if (Test-Path $logDirectory) {
            $sources += @(Get-ChildItem $logDirectory -Filter 'xgrid-*.log' |
                ForEach-Object { $_.FullName })
        }

        foreach ($source in $sources) {
            if (Test-Path $source) {
                if ((Get-Content $source -Raw) -match [regex]::Escape($pattern)) {
                    return $true
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    return $false
}

function Get-SnapshotList {
    return (& $qemuImg snapshot -l $snapshotStore 2>&1 | Out-String)
}

function Measure-Frame([string]$path) {
    $text = & python $frameDiff $path --crop $DisplayCrop 2>&1 | Out-String
    if ($text -match 'colours=([0-9]+) blackPct=([0-9.]+)') {
        return [pscustomobject]@{ Colours = [int]$matches[1]; Black = [double]$matches[2] }
    }

    return [pscustomobject]@{ Colours = 0; Black = 100.0 }
}

function Compare-Frame([string]$a, [string]$b) {
    $text = & python $frameDiff $a $b --crop $DisplayCrop 2>&1 | Out-String
    if ($text -match 'dmean=\(([0-9.]+),([0-9.]+),([0-9.]+)\)') {
        return [double]$matches[1] + [double]$matches[2] + [double]$matches[3]
    }

    return [double]::MaxValue
}

# A rendered frame of the running guest has many colours and little pure black;
# the boot animation's black stage or an empty canvas does not.
function Test-Rendered([string]$path) {
    $stats = Measure-Frame $path
    Write-Host ("  {0}: {1} colours, {2:N1}% black" -f (Split-Path -Leaf $path), $stats.Colours, $stats.Black)
    return ($stats.Colours -gt 500 -and $stats.Black -lt 60)
}

function Save-Screenshot([IntPtr]$handle, [string]$target) {
    $existing = @()
    if (Test-Path $screenshots) {
        $existing = Get-ChildItem $screenshots -Filter *.png | ForEach-Object { $_.Name }
    }

    $deadline = (Get-Date).AddSeconds(30)
    Send-Key $handle 0x77

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $screenshots) {
            $new = Get-ChildItem $screenshots -Filter *.png |
                Where-Object { $existing -notcontains $_.Name } |
                Sort-Object LastWriteTime |
                Select-Object -First 1
            if ($new -ne $null) {
                # The encoder may still be writing; wait for the size to settle.
                Start-Sleep -Milliseconds 400
                Copy-Item $new.FullName $target -Force
                return $true
            }
        }

        Start-Sleep -Milliseconds 200
    }

    return $false
}

$process = $null
try {
    $env:XGRID_FRONTEND_DATA_DIR = $dataDir
    $env:XGRID_FRONTEND_TRACE = '1'
    $env:XGRID_LOG_DIRECTORY = $logDirectory
    $process = Start-Process -FilePath $frontend `
        -ArgumentList '--autostart' `
        -WorkingDirectory (Split-Path -Parent $frontend) `
        -RedirectStandardError $stderrPath `
        -RedirectStandardOutput $stdoutPath `
        -PassThru

    Write-Output "shell pid $($process.Id); waiting for the core to start ..."
    if (-not (Wait-Trace 'xgrid-core: machine ready' $FirstFrameTimeoutSeconds)) {
        throw "the core never started (see $stderrPath)"
    }

    $handle = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 120 -and $handle -eq [IntPtr]::Zero; $attempt++) {
        $process.Refresh()
        $handle = $process.MainWindowHandle
        if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
    }

    if ($handle -eq [IntPtr]::Zero) {
        throw 'the shell window was not found'
    }

    [void][XGridCheck.Native]::SetForegroundWindow($handle)

    # Let the console reach its own screen (the boot animation runs first).
    Start-Sleep -Seconds $SettleSeconds

    # Save only while the guest is painting a real picture: the boot animation
    # has black stages, and a snapshot of one makes the evidence unreadable.
    $saved = Join-Path $work 'shot-saved.png'
    $captured = $false
    for ($attempt = 1; $attempt -le 8 -and -not $captured; $attempt++) {
        if (-not (Save-Screenshot $handle $saved)) {
            throw 'the shell did not write the pre-save screenshot'
        }

        if (-not (Test-Rendered $saved)) {
            Start-Sleep -Seconds 3
            continue
        }

        Write-Output "F6: saving slot 1 (attempt $attempt) ..."
        Send-Key $handle 0x75
        if (-not (Wait-Trace 'save state Save slot 1: ok' $OperationTimeoutSeconds)) {
            throw "the shell did not report a completed save (see $stderrPath)"
        }

        Start-Sleep -Milliseconds 400
        if (-not (Save-Screenshot $handle $saved)) {
            throw 'the shell did not write the saved-state screenshot'
        }

        if (Test-Rendered $saved) {
            $captured = $true
        }
        else {
            Start-Sleep -Seconds 3
        }
    }

    if (-not $captured) {
        throw 'the console never presented a rendered frame to save'
    }

    Start-Sleep -Seconds $SettleSeconds
    $beforeLoad = Join-Path $work 'shot-before-load.png'
    if (-not (Save-Screenshot $handle $beforeLoad)) {
        throw 'the shell did not write the pre-load screenshot'
    }

    Write-Output 'F7: loading slot 1 ...'
    Send-Key $handle 0x76
    if (-not (Wait-Trace 'save state Load slot 1: ok' $OperationTimeoutSeconds)) {
        throw "the shell did not report a completed load (see $stderrPath)"
    }

    $afterLoad = Join-Path $work 'shot-after-load.png'
    if (-not (Save-Screenshot $handle $afterLoad)) {
        throw 'the shell did not write the post-load screenshot'
    }

    if (-not (Test-Rendered $afterLoad)) {
        throw 'the frame after the load is blank; the restore did not resume the picture'
    }

    if ($process.HasExited) {
        throw "the shell exited during the check (code $($process.ExitCode))"
    }

    # The machine holds the image open; close it before reading the qcow2.
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(20000)) {
        throw 'the shell did not close in time'
    }

    $listing = Get-SnapshotList
    Write-Output $listing
    if ($listing -notmatch 'xgrid-slot-1') {
        throw "the snapshot store carries no 'xgrid-slot-1' snapshot: $snapshotStore"
    }

    $restoredDelta = Compare-Frame $saved $afterLoad
    $movedDelta = Compare-Frame $saved $beforeLoad
    Write-Output ''
    Write-Output ("saved -> before load: {0:N1}" -f $movedDelta)
    Write-Output ("saved -> after load:  {0:N1}" -f $restoredDelta)
    Write-Output "screenshots: $saved / $beforeLoad / $afterLoad"
    Write-Output 'SAVESTATE PASS'
}
catch {
    Write-Output ("SAVESTATE FAIL: " + $_.Exception.Message)
    if (Test-Path $stderrPath) {
        Get-Content $stderrPath -Tail 20
    }

    exit 1
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(8000)) {
            $process.Kill($true)
        }
    }

    if ($null -ne $process) {
        $process.Dispose()
    }

    Remove-Item Env:\XGRID_FRONTEND_DATA_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\XGRID_FRONTEND_TRACE -ErrorAction SilentlyContinue
    Remove-Item Env:\XGRID_LOG_DIRECTORY -ErrorAction SilentlyContinue

    if (-not $Keep -and (Test-Path $work)) {
        Remove-Item -Recurse -Force $work
    }
}
