# Boot the patched core with a disc image and capture its window after a delay.
# Used to verify a block driver end to end (the guest reads the disc with no
# extraction step). Works for .iso/.xiso (raw), .chd and .zar.
param(
    [string]$Disc = 'E:\Microsoft Xbox\Smashing Drive (USA).chd',
    [int]$Seconds = 30,
    [string]$Shot = 'wt-data/chd-boot.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$core = Join-Path $root 'xemu/dist/xemu.exe'
if (-not (Test-Path $core)) { throw "Missing core: $core (build it with scripts/build-native-xemu.ps1)" }
if (-not [System.IO.Path]::IsPathRooted($Disc)) {
    $Disc = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Disc))
}
if (-not (Test-Path -LiteralPath $Disc)) { throw "Missing disc: $Disc" }
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
Get-ChildItem (Join-Path $root 'xemu/dist') -Filter *.dll | ForEach-Object {
    Copy-Item $_.FullName $dir -Force
}
Copy-Item $core (Join-Path $dir 'xemu.exe') -Force

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ChdBootWin32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@

$started = Get-Date
# The MSYS2 build links runs of glib/curl dynamically; the loader finds the rest
# of their dependencies on the MinGW bin directory.
$env:PATH = 'C:\msys64\mingw64\bin;' + $env:PATH
$stdout = Join-Path $dir 'boot-stdout.txt'
$stderr = Join-Path $dir 'boot-stderr.txt'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $dir 'xemu.exe')
$psi.Arguments = '-config_path "' + $configPath + '"'
$psi.WorkingDirectory = $dir
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdoutTask = $p.StandardOutput.ReadToEndAsync()
$stderrTask = $p.StandardError.ReadToEndAsync()
Write-Output "core: pid=$($p.Id) disc=$Disc"

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $p.Refresh()
    if ($p.MainWindowHandle -ne 0) { break }
}
if ($p.MainWindowHandle -eq 0) { throw 'The core window never appeared.' }
$h = $p.MainWindowHandle

[void][ChdBootWin32]::SetProcessDPIAware()
[void][ChdBootWin32]::ShowWindow($h, 9)
[void][ChdBootWin32]::SetWindowPos($h, [IntPtr](-1), 40, 40, 0, 0, 0x0001 -bor 0x0002)
[void][ChdBootWin32]::SetForegroundWindow($h)
Start-Sleep -Seconds $Seconds

try {
$p.Refresh()
if ($p.HasExited) {
    throw "the core exited with code $($p.ExitCode) before the capture"
}

# Keep the core in front and ask it to paint its own client area; a plain screen
# copy catches whatever window is on top when another app steals the foreground.
[void][ChdBootWin32]::SetWindowPos($h, [IntPtr](-1), 40, 40, 0, 0, 0x0001 -bor 0x0002)
[void][ChdBootWin32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 400

$cr = New-Object ChdBootWin32+RECT
$width = 0
$height = 0
$deadline = (Get-Date).AddSeconds(10)
do {
    Start-Sleep -Milliseconds 250
    [void][ChdBootWin32]::GetClientRect($h, [ref]$cr)
    $width = $cr.Right - $cr.Left
    $height = $cr.Bottom - $cr.Top
} while (($width -le 0 -or $height -le 0) -and (Get-Date) -lt $deadline)
if ($width -le 0 -or $height -le 0) {
    $wr = New-Object ChdBootWin32+RECT
    [void][ChdBootWin32]::GetWindowRect($h, [ref]$wr)
    $width = $wr.Right - $wr.Left
    $height = $wr.Bottom - $wr.Top
}
if ($width -le 0 -or $height -le 0) {
    $width = 960
    $height = 540
}
$out = Join-Path $root $Shot
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dark = 0
$samples = 0
for ($attempt = 1; $attempt -le 5; $attempt++) {
    $hdc = $graphics.GetHdc()
    [void][ChdBootWin32]::PrintWindow($h, $hdc, 3)
    $graphics.ReleaseHdc($hdc)
    $dark = 0
    $samples = 0
    for ($y = 0; $y -lt $height; $y += 16) {
        for ($x = 0; $x -lt $width; $x += 16) {
            $c = $bitmap.GetPixel($x, $y)
            $samples++
            if ($c.R -lt 8 -and $c.G -lt 8 -and $c.B -lt 8) { $dark++ }
        }
    }
    if ($samples -gt 0 -and ($dark / $samples) -le 0.5) { break }
    if ($attempt -lt 5) {
        [void][ChdBootWin32]::SetWindowPos($h, [IntPtr](-1), 40, 40, 0, 0, 0x0001 -bor 0x0002)
        [void][ChdBootWin32]::SetForegroundWindow($h)
        Start-Sleep -Milliseconds 800
    }
}
$blank = ($samples -eq 0) -or (($dark / [double]$samples) -gt 0.5)
if ($blank) {
    if ([ChdBootWin32]::GetForegroundWindow() -ne $h) {
        $bitmap.Dispose()
        $graphics.Dispose()
        throw 'capture contended: PrintWindow answered black and the core is not the foreground window (no frame is claimed)'
    }
    $pt = New-Object ChdBootWin32+POINT
    [void][ChdBootWin32]::ClientToScreen($h, [ref]$pt)
    $graphics.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bitmap.Size)
    Write-Output "PrintWindow was black ($dark/$samples); used a screen copy"
}
else {
    Write-Output 'Captured through PrintWindow'
}
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
if ($colours.Count -gt 64) { Write-Output 'RESULT: PASS (non-blank frame)' } else { Write-Output 'RESULT: FAIL (blank capture)' }
}
finally {
    if (-not $p.HasExited) {
        $p.Kill()
        $p.WaitForExit()
    }
    Set-Content -LiteralPath $stdout -Value $stdoutTask.GetAwaiter().GetResult() -NoNewline
    Set-Content -LiteralPath $stderr -Value $stderrTask.GetAwaiter().GetResult() -NoNewline
    $errors = Select-String -Path $stderr -Pattern 'error|failed|cannot|unrecognized|invalid|no medium' -CaseSensitive:$false |
        Select-Object -Last 8
    if ($errors) {
        Write-Output '--- core errors ---'
        $errors | ForEach-Object { Write-Output $_.Line }
    }
}
