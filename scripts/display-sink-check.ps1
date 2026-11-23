# A2 slice 1 acceptance: the core publishes frames into a named shared section
# (XGRID_DISPLAY_SHM) with no visible window, and the shell can read them.
#
# Usage: pwsh -File scripts/display-sink-check.ps1 [-Seconds 25] [-Shot wt-data/display-sink.png]
param(
    [int]$Seconds = 25,
    [string]$Shot = 'wt-data/display-sink.png',
    [string]$Disc = 'H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso',
    [string]$Name = 'Local\XGridDisplayCheck'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$core = Join-Path $root 'native/xemu/dist/xemu.exe'
if (-not (Test-Path $core)) { throw "Missing core: $core" }
$fw = Join-Path $root 'data/xemu'
$dir = Join-Path $root 'wt-data/display-sink'
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

function Read-SinkFrame([string]$name, [string]$path) {
    $map = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting(
        $name, [System.IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
    try {
        $view = $map.CreateViewAccessor(0, 0, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
        try {
            $magic = $view.ReadUInt32(0)
            $width = $view.ReadUInt32(4)
            $height = $view.ReadUInt32(8)
            $stride = $view.ReadUInt32(12)
            $frame = $view.ReadUInt64(16)
            if ($magic -ne 0x53444758 -or $width -eq 0 -or $height -eq 0 -or $stride -lt ($width * 4)) {
                return 'no-frame'
            }

            $pixels = New-Object byte[] ($stride * $height)
            $view.ReadArray(32, $pixels, 0, $pixels.Length)

            $bitmap = New-Object System.Drawing.Bitmap([int]$width, [int]$height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $rect = New-Object System.Drawing.Rectangle(0, 0, [int]$width, [int]$height)
            $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                # glReadPixels is bottom-up; copy rows in reverse.
                for ($y = 0; $y -lt $height; $y++) {
                    $dst = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
                    [System.Runtime.InteropServices.Marshal]::Copy($pixels, [int](($height - 1 - $y) * $stride), $dst, [int]($width * 4))
                }
            } finally {
                $bitmap.UnlockBits($data)
            }

            $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()
            return ('{0}x{1} frame={2}' -f $width, $height, $frame)
        } finally {
            $view.Dispose()
        }
    } finally {
        $map.Dispose()
    }
}

$env:PATH = 'C:\msys64\mingw64\bin;' + $env:PATH
$env:XGRID_DISPLAY_SHM = $Name
$started = Get-Date
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $dir 'xemu.exe')
$psi.Arguments = '-config_path "' + $configPath + '"'
$psi.WorkingDirectory = $dir
$psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
Write-Output "core: pid=$($p.Id) sink=$Name"

# Wait for the sink to exist, then let the guest reach a rendered scene.
$deadline = (Get-Date).AddSeconds(40)
$result = 'no-section'
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    try {
        $result = Read-SinkFrame $Name (Join-Path $root $Shot)
        if ($result -ne 'no-frame') { break }
    } catch {
        $result = 'no-section'
    }
}
Write-Output "sink: $result"
if ($result -ne 'no-section' -and $result -ne 'no-frame') {
    Start-Sleep -Seconds $Seconds
    $out = Join-Path $root $Shot
    $result = Read-SinkFrame $Name $out
    Write-Output "sink: $result"
} else {
    throw 'The display sink produced no frames.'
}

$p.Refresh()
Write-Output "core alive=$(-not $p.HasExited) window=$($p.MainWindowHandle) title='$($p.MainWindowTitle)'"
$p.Kill()
$p.WaitForExit()

$png = [System.Drawing.Bitmap]::FromFile((Join-Path $root $Shot))
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
Write-Output ("shot: {0} colours={1} black={2:P1}" -f $Shot, $colours.Count, ($black / $sampled))
if ($colours.Count -gt 64) { Write-Output 'RESULT: PASS' } else { Write-Output 'RESULT: FAIL (blank sink frame)' }
