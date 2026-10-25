#requires -Version 7.0
<#
.SYNOPSIS
  Launch xemu 0.8.139 with the data/xemu media and capture a screenshot.

.DESCRIPTION
  xemu 0.8.139 reads the machine/drive layout from data/xemu/xemu.toml, but it
  does not pass flashrom_path into QEMU -bios, so -bios is explicit here. The
  QEMU monitor socket fails to bind on this host, so the capture is a window
  screenshot (a visual oracle, not a pixel-exact framebuffer dump).

  With -IsoPath the disc is passed explicitly with -cdrom (the same optimized
  ISO XGrid runs). The HDD is opened with -snapshot so the template is not
  modified. The capture is the window's CLIENT area, resized to 640x480 by
  default (xemu's 1x scale) so it can be compared with the software frame.

.PARAMETER IsoPath
  Optional ISO to mount as the DVD (the same image XGrid boots).

.PARAMETER Seconds
  Time to let the guest boot before the capture (default 45).

.PARAMETER Stop
  Stop the xemu process after the capture (default: leave it running).

.PARAMETER OutPath
  Screenshot path. Default: data/xemu/xemu-oracle-<stamp>.png.

.EXAMPLE
  pwsh -File scripts/xemu-oracle.ps1 -IsoPath "H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso" -Seconds 60 -Stop
#>
param(
    [string]$IsoPath = "",
    [int]$Seconds = 45,
    [int]$ClientWidth = 640,
    [int]$ClientHeight = 480,
    [string]$OutPath = "",
    [switch]$NoResize,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$base = Join-Path $root "data\xemu"
$exe = Join-Path $base "xemu.exe"
$bios = Join-Path $base "Complex_4627.bin"
$eeprom = Join-Path $base "eeprom.bin"
$config = Join-Path $base "xemu.toml"

foreach ($path in @($exe, $bios, $eeprom, $config)) {
    if (-not (Test-Path $path)) { throw "Missing xemu oracle file: $path" }
}
if ($IsoPath -and -not (Test-Path -LiteralPath $IsoPath)) { throw "Missing ISO: $IsoPath" }

$existing = Get-Process -Name xemu -ErrorAction SilentlyContinue
if ($existing) {
    throw "xemu is already running (PID $($existing.Id)). Stop it first, or use it for the screenshot."
}

$stdout = Join-Path $base "oracle-stdout.txt"
$stderr = Join-Path $base "oracle-stderr.txt"
$args = @(
    "-bios", $bios,
    "-device", "smbus-storage,file=$eeprom",
    "-snapshot"
)
if ($IsoPath) {
    # xemu has no -cdrom: the disc is the settings key sys.files.dvd_path. Write a
    # temporary config that points at the ISO and select it with -config_path
    # (a bare -cdrom also loses an ISO path containing spaces: Start-Process joins
    # the argument list without quoting, and the split tokens are parsed as options).
    $configText = Get-Content -LiteralPath $config -Raw
    $iso = (Resolve-Path -LiteralPath $IsoPath).Path
    $configText = [regex]::Replace($configText, "(?m)^dvd_path\s*=.*$", "")
    $configText = $configText -replace "\[sys\.files\]", "[sys.files]`ndvd_path = '$iso'"
    $oracleConfig = Join-Path $root "wt-data\xemu-oracle.toml"
    Set-Content -LiteralPath $oracleConfig -Value $configText -NoNewline
    $args += @("-config_path", $oracleConfig)
}

Write-Host "Starting xemu 0.8.139 from $base"
if ($IsoPath) { Write-Host "DVD: $IsoPath" }
$p = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $base `
    -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class XemuOracleWin32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@

try {
    # Physical pixels only: a DPI-unaware process gets virtualized rects while the
    # screen capture is physical, which shifts the shot and drags in the window
    # chrome (the first TM4 capture was off by the caption height).
    [XemuOracleWin32]::SetProcessDPIAware() | Out-Null

    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $p.Refresh()
        if ($p.HasExited) { throw "xemu exited with code $($p.ExitCode); see $stderr" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "xemu window did not appear" }

    $h = $p.MainWindowHandle
    [XemuOracleWin32]::ShowWindow($h, 9) | Out-Null      # SW_RESTORE
    # HWND_TOPMOST (-1) so the capture sees xemu even when another window owns the
    # foreground (SetForegroundWindow is refused for a background caller on Win11).
    [XemuOracleWin32]::SetWindowPos($h, [IntPtr](-1), 40, 40, 1280, 760, 0x0040) | Out-Null
    [XemuOracleWin32]::SetForegroundWindow($h) | Out-Null

    if (-not $NoResize) {
        # Shrink the window so its CLIENT area is the requested size. This keeps
        # the capture at xemu's 1x scale (640x480) rather than a scaled window.
        $wr = [XemuOracleWin32+RECT]::new()
        $cr = [XemuOracleWin32+RECT]::new()
        [XemuOracleWin32]::GetWindowRect($h, [ref]$wr) | Out-Null
        [XemuOracleWin32]::GetClientRect($h, [ref]$cr) | Out-Null
        $borderW = ($wr.Right - $wr.Left) - ($cr.Right - $cr.Left)
        $borderH = ($wr.Bottom - $wr.Top) - ($cr.Bottom - $cr.Top)
        if ($borderW -lt 0) { $borderW = 16 }
        if ($borderH -lt 0) { $borderH = 39 }
        [XemuOracleWin32]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, ($ClientWidth + $borderW), ($ClientHeight + $borderH), 0x0040) | Out-Null
        Start-Sleep -Milliseconds 600
    }

    $cr = [XemuOracleWin32+RECT]::new()
    $pt = [XemuOracleWin32+POINT]::new()
    $cw = 0
    $ch = 0
    $deadline2 = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 250
        [XemuOracleWin32]::GetClientRect($h, [ref]$cr) | Out-Null
        $cw = $cr.Right - $cr.Left
        $ch = $cr.Bottom - $cr.Top
    } while (($cw -le 0 -or $ch -le 0) -and (Get-Date) -lt $deadline2)
    if ($cw -le 0 -or $ch -le 0) {
        # Fallback: capture the window area the previous script used.
        $cw = 1280
        $ch = 760
    }
    Write-Host "Booting for $Seconds s (client ${cw}x${ch})"
    Start-Sleep -Seconds $Seconds

    # Re-read the client origin now: a window can settle (or be nudged) while it
    # boots, and capturing from a stale origin shifts the frame and drags the
    # window chrome into the shot.
    [XemuOracleWin32]::GetClientRect($h, [ref]$cr) | Out-Null
    $cw = $cr.Right - $cr.Left
    $ch = $cr.Bottom - $cr.Top
    $pt.X = 0
    $pt.Y = 0
    [XemuOracleWin32]::ClientToScreen($h, [ref]$pt) | Out-Null
    Write-Host "Capturing client ${cw}x${ch} at $($pt.X),$($pt.Y)"

    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap $cw, $ch
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)

    if (-not $OutPath) {
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $OutPath = Join-Path $base "xemu-oracle-$stamp.png"
    }
    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()

    $version = (Get-Item $exe).VersionInfo.FileVersion
    Write-Host "Captured: $OutPath"
    Write-Host "xemu version: $version"
    Write-Host "Log: $stderr"
}
finally {
    if ($Stop -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force
        Write-Host "Stopped xemu PID $($p.Id)"
    }
}
