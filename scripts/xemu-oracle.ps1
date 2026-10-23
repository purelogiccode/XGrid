#requires -Version 7.0
<#
.SYNOPSIS
  Launch xemu 0.8.139 with the data/xemu media and capture a menu screenshot.

.DESCRIPTION
  xemu 0.8.139 reads the machine/drive layout from data/xemu/xemu.toml, but it
  does not pass flashrom_path into QEMU -bios, so -bios is explicit here.  The
  QEMU monitor socket fails to bind on this host, so the capture is a window
  screenshot (a visual menu oracle, not a pixel-exact framebuffer dump).

  The HDD is opened with -snapshot so the template is not modified.

.PARAMETER Seconds
  Time to let the guest boot before the capture (default 45).

.PARAMETER Stop
  Stop the xemu process after the capture (default: leave it running).

.EXAMPLE
  pwsh -File scripts/xemu-oracle.ps1 -Seconds 60 -Stop
#>
param(
    [int]$Seconds = 45,
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

Write-Host "Starting xemu 0.8.139 from $base"
$p = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $base `
    -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

try {
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $p.Refresh()
        if ($p.HasExited) { throw "xemu exited with code $($p.ExitCode); see $stderr" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw "xemu window did not appear" }

    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class XemuOracleWin32 {
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

    $h = $p.MainWindowHandle
    [XemuOracleWin32]::ShowWindow($h, 9) | Out-Null      # SW_RESTORE
    [XemuOracleWin32]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1280, 760, 0x0040) | Out-Null
    [XemuOracleWin32]::SetForegroundWindow($h) | Out-Null

    Write-Host "Booting for $Seconds s (xemu window at 40,40 1280x760)"
    Start-Sleep -Seconds $Seconds

    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 1280, 760
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(40, 40, 0, 0, $bmp.Size)

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $out = Join-Path $base "xemu-oracle-$stamp.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()

    $version = (Get-Item $exe).VersionInfo.FileVersion
    Write-Host "Captured: $out"
    Write-Host "xemu version: $version"
    Write-Host "Log: $stderr"
}
finally {
    if ($Stop -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force
        Write-Host "Stopped xemu PID $($p.Id)"
    }
}
