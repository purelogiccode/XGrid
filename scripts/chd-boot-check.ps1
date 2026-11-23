# Boot the patched core with a disc image and capture its window after a delay.
# Used to verify the CHD driver end to end (the guest reads a compressed disc
# with no extraction step).
param(
    [string]$Disc = 'E:\Microsoft Xbox\Smashing Drive (USA).chd',
    [int]$Seconds = 30,
    [string]$Shot = 'wt-data/chd-boot.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$core = Join-Path $root 'native/xemu/dist/xemu.exe'
if (-not (Test-Path $core)) { throw "Missing core: $core" }
$fw = Join-Path $root 'data/xemu'
$dir = Join-Path $root 'wt-data/chd-boot'
New-Item -ItemType Directory -Force $dir | Out-Null

$config = @"
[general]
show_welcome = false

[general.updates]
check = false

[display.window]
startup_size = 'last_used'
last_width = 960
last_height = 540
vsync = false

[display.ui]
show_menubar = false
show_notifications = false

[audio]
use_dsp = true

[sys]
mem_limit = '64'
avpack = 'hdtv'

[sys.files]
bootrom_path = '$fw\mcpx_1.0.bin'
flashrom_path = '$fw\Complex_4627.bin'
eeprom_path = '$fw\eeprom.bin'
hdd_path = '$fw\xbox_hdd.qcow2'
dvd_path = '$Disc'
"@
$configPath = Join-Path $dir 'xemu.toml'
Set-Content $configPath $config -NoNewline
Get-ChildItem (Join-Path $root 'native/xemu/dist') -Filter *.dll | ForEach-Object {
    Copy-Item $_.FullName $dir -Force
}
Copy-Item $core (Join-Path $dir 'xemu.exe') -Force

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ChdBootWin32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@

$started = Get-Date
# The MSYS2 build links runs of glib/curl dynamically; the loader finds the rest
# of their dependencies on the MinGW bin directory.
$env:PATH = 'C:\msys64\mingw64\bin;' + $env:PATH
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $dir 'xemu.exe')
$psi.Arguments = '-config_path "' + $configPath + '"'
$psi.WorkingDirectory = $dir
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
Write-Output "core: pid=$($p.Id) disc=$Disc"

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $p.Refresh()
    if ($p.MainWindowHandle -ne 0) { break }
}
if ($p.MainWindowHandle -eq 0) { throw 'The core window never appeared.' }
[void][ChdBootWin32]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Seconds $Seconds

$rect = New-Object ChdBootWin32+RECT
[void][ChdBootWin32]::GetWindowRect($p.MainWindowHandle, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$out = Join-Path $root $Shot
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

$png = [System.Drawing.Bitmap]::FromFile($out)
$colours = New-Object 'System.Collections.Generic.HashSet[int]'
$black = 0
for ($y = 0; $y -lt $png.Height; $y += 4) {
    for ($x = 0; $x -lt $png.Width; $x += 4) {
        $c = $png.GetPixel($x, $y).ToArgb()
        [void]$colours.Add($c)
        if (($c -band 0x00FFFFFF) -eq 0) { $black++ }
    }
}
$sampled = [math]::Ceiling($png.Width / 4) * [math]::Ceiling($png.Height / 4)
$png.Dispose()

$p.Refresh()
Write-Output ("shot: {0} {1}x{2} colours={3} black={4:P1}" -f $out, $width, $height, $colours.Count, ($black / $sampled))
Write-Output "core alive=$(-not $p.HasExited)"
$p.Kill()
$p.WaitForExit()
if ($colours.Count -gt 64) { Write-Output 'RESULT: PASS' } else { Write-Output 'RESULT: FAIL (blank capture)' }
