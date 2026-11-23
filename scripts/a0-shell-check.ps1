# A0 acceptance: launch 007 from the shell on the xemu core, confirm the core
# window is embedded in the shell window (a visible child owned by the core
# process), capture the shell's client area and verify a clean teardown.
#
# Usage: pwsh -File scripts/a0-shell-check.ps1 [-Seconds 45] [-Shot wt-data/a0-shell.png]
param(
    [int]$Seconds = 45,
    [string]$Shot = 'wt-data/a0-shell.png',
    [string]$Iso = 'H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/XGrid.Frontend.Avalonia/bin/Release/net11.0/XGrid.Frontend.Avalonia.exe'
if (-not (Test-Path $exe)) { throw "Build the frontend first: $exe" }

$data = Join-Path $env:TEMP 'xgrid-a0-shell'
New-Item -ItemType Directory -Force $data | Out-Null
$firmware = Join-Path $root 'data/xemu'
$settings = [ordered]@{
    BiosPath     = (Join-Path $firmware 'Complex_4627.bin')
    McpxRomPath  = (Join-Path $firmware 'mcpx_1.0.bin')
    EepromPath   = (Join-Path $firmware 'eeprom.bin')
    HddPath      = (Join-Path $firmware 'xbox_hdd.qcow2')
    DvdPath      = $Iso
    RamMb        = 64
    Core         = 'xemu'
    XemuCorePath = $(if (Test-Path (Join-Path $root 'native/xemu/dist/xemu.exe')) { Join-Path $root 'native/xemu/dist/xemu.exe' } else { Join-Path $firmware 'xemu.exe' })
}
$settings | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $data 'settings.json')

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class A0Win32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    private delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr p);

    // Returns visible child windows of the shell that belong to the target pid.
    public static List<long> FindChildrenOf(IntPtr parent, int pid) {
        var found = new List<long>();
        EnumChildWindows(parent, (h, p) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == (uint)pid && IsWindowVisible(h)) found.Add(h.ToInt64());
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$env:XGRID_FRONTEND_DATA_DIR = $data
$started = Get-Date
$app = Start-Process -FilePath $exe -PassThru
Write-Output "app: pid=$($app.Id)"

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { break }
}
if ($app.MainWindowHandle -eq 0) { throw 'The shell window never appeared.' }
$hwnd = $app.MainWindowHandle
Write-Output "shell: hwnd=0x$('{0:X}' -f $hwnd.Int64)"
[void][A0Win32]::SetForegroundWindow($hwnd)
Start-Sleep -Seconds 1

# F5 = power on (MainWindow key handler).
[void][A0Win32]::PostMessageW($hwnd, 0x0100, [IntPtr]0x74, [IntPtr]0)
Start-Sleep -Milliseconds 80
[void][A0Win32]::PostMessageW($hwnd, 0x0101, [IntPtr]0x74, [IntPtr]0)
Write-Output 'input: F5 posted (power on)'

$core = $null
$coreDeadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $coreDeadline) {
    Start-Sleep -Milliseconds 500
    $core = Get-Process xemu -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $started } |
        Select-Object -First 1
    if ($core) { break }
}
if (-not $core) { throw 'No xemu core process was started by the shell.' }
Write-Output "core: pid=$($core.Id)"

$embedDeadline = (Get-Date).AddSeconds(40)
$children = @()
while ((Get-Date) -lt $embedDeadline) {
    Start-Sleep -Milliseconds 500
    if ($core.HasExited) { break }
    $children = [A0Win32]::FindChildrenOf($hwnd, $core.Id)
    if ($children.Count -gt 0) { break }
}
Write-Output "embed: visibleCoreChildren=$($children.Count) handles=$($children -join ',')"

Start-Sleep -Seconds $Seconds
$app.Refresh()
$core.Refresh()
Write-Output "run: shellAlive=$(-not $app.HasExited) coreAlive=$(-not $core.HasExited)"

$rect = New-Object A0Win32+RECT
[void][A0Win32]::GetClientRect($hwnd, [ref]$rect)
$origin = New-Object A0Win32+POINT
[void][A0Win32]::ClientToScreen($hwnd, [ref]$origin)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$shotFull = Join-Path $root $Shot
$shotPath = [System.IO.Path]::ChangeExtension($shotFull, '.png')
New-Item -ItemType Directory -Force (Split-Path $shotPath) | Out-Null
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$bitmap.Save($shotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

$png = [System.Drawing.Bitmap]::FromFile($shotPath)
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
Write-Output ("shot: {0} {1}x{2} colours={3} black={4:P1}" -f $shotPath, $width, $height, $colours.Count, ($black / $sampled))

# Clean teardown: close the shell, then confirm the core is gone.
[void][A0Win32]::PostMessageW($hwnd, 0x0010, [IntPtr]0, [IntPtr]0)
$exitDeadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $exitDeadline) {
    Start-Sleep -Milliseconds 300
    $app.Refresh()
    if ($app.HasExited) { break }
}
if (-not $app.HasExited) { $app.Kill(); Write-Output 'cleanup: shell had to be killed' }
Start-Sleep -Seconds 2
$orphan = Get-Process xemu -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $started }
Write-Output "teardown: shellExit=$($app.HasExited) coreLeft=$([bool]$orphan)"
if ($orphan) { $orphan | Stop-Process -Force }

if ($colours.Count -gt 64) {
    Write-Output 'RESULT: PASS'
} else {
    Write-Output 'RESULT: FAIL'
}
