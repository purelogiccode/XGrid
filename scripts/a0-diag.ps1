# Temporary A0 diagnostic: run the shell with tracing, power on, report the
# native-surface trace and the core's window parentage.
param([int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/XGrid.Frontend.Avalonia/bin/Release/net11.0/XGrid.Frontend.Avalonia.exe'
$env:XGRID_FRONTEND_DATA_DIR = (Join-Path $env:TEMP 'xgrid-a0-shell')
$env:XGRID_FRONTEND_TRACE = '1'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Diag {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr x);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr x);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    public static List<string> Describe(IntPtr root, int pid) {
        var lines = new List<string>();
        EnumChildWindows(root, (h, p) => {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner == (uint)pid) {
                RECT r; GetWindowRect(h, out r);
                lines.Add(string.Format("child hwnd=0x{0:X} pid={1} visible={2} style=0x{3:X8} rect={4},{5},{6},{7} parent=0x{8:X}",
                    h.ToInt64(), owner, IsWindowVisible(h), GetWindowLongW(h, -16), r.Left, r.Top, r.Right, r.Bottom, GetParent(h).ToInt64()));
            }
            return true;
        }, IntPtr.Zero);
        return lines;
    }

    public static List<string> TopLevel(int pid) {
        var lines = new List<string>();
        EnumWindows((h, p) => {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner == (uint)pid) {
                RECT r; GetWindowRect(h, out r);
                lines.Add(string.Format("top hwnd=0x{0:X} visible={1} style=0x{2:X8} rect={3},{4},{5},{6}",
                    h.ToInt64(), IsWindowVisible(h), GetWindowLongW(h, -16), r.Left, r.Top, r.Right, r.Bottom));
            }
            return true;
        }, IntPtr.Zero);
        return lines;
    }
}
'@

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardError = $true
$psi.RedirectStandardOutput = $true
$started = Get-Date
$app = [System.Diagnostics.Process]::Start($psi)
$err = $app.StandardError.ReadToEndAsync()

$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { break }
}
$hwnd = $app.MainWindowHandle
Write-Output ("shell hwnd=0x{0:X}" -f $hwnd.ToInt64())
[void][Diag]::SetForegroundWindow($hwnd)
Start-Sleep -Seconds 1
Write-Output "--- shell children before power on ---"
[Diag]::Describe($hwnd, $app.Id) | ForEach-Object { $_ }

[void][Diag]::PostMessageW($hwnd, 0x0100, [IntPtr]0x74, [IntPtr]0)
Start-Sleep -Milliseconds 80
[void][Diag]::PostMessageW($hwnd, 0x0101, [IntPtr]0x74, [IntPtr]0)

$core = $null
$d = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $d) {
    Start-Sleep -Milliseconds 500
    $core = Get-Process xemu -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $started } | Select-Object -First 1
    if ($core) { break }
}
Write-Output "core pid=$($core.Id)"
Start-Sleep -Seconds $Seconds

Write-Output "--- core top-level windows ---"
[Diag]::TopLevel($core.Id) | ForEach-Object { $_ }
Write-Output "--- shell children now ---"
[Diag]::Describe($hwnd, $core.Id) | ForEach-Object { $_ }

[void][Diag]::PostMessageW($hwnd, 0x0010, [IntPtr]0, [IntPtr]0)
$app.WaitForExit(15000) | Out-Null
if (-not $app.HasExited) { $app.Kill() }
Write-Output "--- trace ---"
$err.Result
