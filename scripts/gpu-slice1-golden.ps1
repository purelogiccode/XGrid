# GPU-backend slice-1 golden harness (gpu-backend-design.md §8 step 5).
# Runs the §95-style neutral 1B menu boot twice (--gpu sw / --gpu gl) and
# applies the §3.4 diff policy: flat regions byte-exact; differences only
# within a ±1 px edge band of software-detected edges with per-channel
# tolerance ≤ 2. Requires private media copies in wt-data\ (not committed).
#
# Usage (from the worktree root, after dotnet build -c Release):
#   pwsh -File scripts/gpu-slice1-golden.ps1 [-Steps 1000000000] [-OutDir .]

param(
    [string]$Steps = "1000000000",
    [string]$OutDir = ".",
    [string]$Bios = "wt-data\Complex_4627.bin",
    [string]$Mcpx = "wt-data\mcpx_1.0.bin",
    [string]$Eeprom = "wt-data\eeprom.bin",
    [string]$Hdd = "wt-data\xbox_hdd.qcow2",
    [string]$PadScript = "down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"
)

$ErrorActionPreference = "Stop"
$cli = "src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll"
if (-not (Test-Path $cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }

$swBmp = Join-Path $OutDir "golden-sw.bmp"
$glBmp = Join-Path $OutDir "golden-gl.bmp"
$swLog = Join-Path $OutDir "golden-sw.log"
$glLog = Join-Path $OutDir "golden-gl.log"

$baseArgs = @(
    "boot", "--bios", $Bios, "--mcpx", $Mcpx, "--eeprom", $Eeprom, "--hdd", $Hdd,
    "--steps", $Steps, "--pad-script", $PadScript
)

Write-Host "== sw run =="
& dotnet $cli @baseArgs --gpu sw --frame $swBmp 2>&1 | Tee-Object -FilePath $swLog | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "sw run failed exit=$LASTEXITCODE"; exit 1 }

Write-Host "== gl run =="
& dotnet $cli @baseArgs --gpu gl --frame $glBmp 2>&1 | Tee-Object -FilePath $glLog | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "gl run failed exit=$LASTEXITCODE"; exit 1 }

function Get-LogLine([string]$path, [string]$pattern) {
    Select-String -Path $path -Pattern $pattern | Select-Object -First 1 -ExpandProperty Line
}

$fail = $false

# --- acceptance: LastFault vec=0 on both runs ---
foreach ($log in @($swLog, $glLog)) {
    $line = Get-LogLine $log "LastFault"
    if ($line -notmatch "LastFault vec=0") { Write-Host "FAIL: $log -> $line"; $fail = $true }
    else { Write-Host "OK: $log -> $line" }
}

# --- acceptance: frames/stalls/draws identical sw<->gl ---
function Get-Counters([string]$path) {
    $line = Get-LogLine $path "NV2A draws executed"
    $m = [regex]::Match($line, "draws executed=(\d+).*frames=(\d+) stalls=(\d+)")
    return ,@($m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value)
}
$swC = Get-Counters $swLog
$glC = Get-Counters $glLog
if (($swC -join ",") -ne ($glC -join ",")) {
    Write-Host "FAIL: counters sw=($($swC -join ',')) gl=($($glC -join ','))"
    $fail = $true
} else {
    Write-Host "OK: counters identical draws=$($swC[0]) frames=$($swC[1]) stalls=$($swC[2])"
}

# --- acceptance: BackendUnsupported == 0 on the gl run ---
$glLine = Get-LogLine $glLog "BackendUnsupported="
if ($glLine -notmatch "BackendUnsupported=0\b") { Write-Host "FAIL: $glLine"; $fail = $true }
else { Write-Host "OK: $glLine" }

# --- acceptance: BMP diff under the §3.4 policy ---
function Read-Bmp($path) {
    $b = [IO.File]::ReadAllBytes($path)
    $offset = [BitConverter]::ToInt32($b, 10)
    $w = [BitConverter]::ToInt32($b, 18)
    $h = [BitConverter]::ToInt32($b, 22)
    $rowBytes = $w * 4
    $pixels = New-Object byte[] ($rowBytes * $h)
    for ($y = 0; $y -lt $h; $y++) {
        $srcRow = $offset + (($h - 1 - $y) * $rowBytes)
        [Array]::Copy($b, $srcRow, $pixels, $y * $rowBytes, $rowBytes)
    }
    return ,@($w, $h, $pixels)
}

$sw = Read-Bmp $swBmp
$gl = Read-Bmp $glBmp
$w = $sw[0]; $h = $sw[1]
if ($w -ne $gl[0] -or $h -ne $gl[1]) { Write-Host "FAIL: size mismatch"; exit 1 }
$a = $sw[2]; $c = $gl[2]
$edge = New-Object bool[] ($w * $h)
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $p = ($y * $w + $x) * 4
        $isEdge = $false
        if ($x + 1 -lt $w) { for ($k = 0; $k -lt 4; $k++) { if ($a[$p+$k] -ne $a[$p+4+$k]) { $isEdge = $true; break } } }
        if (-not $isEdge -and $x -gt 0) { for ($k = 0; $k -lt 4; $k++) { if ($a[$p+$k] -ne $a[$p-4+$k]) { $isEdge = $true; break } } }
        if (-not $isEdge -and $y + 1 -lt $h) { $q = $p + $w * 4; for ($k = 0; $k -lt 4; $k++) { if ($a[$p+$k] -ne $a[$q+$k]) { $isEdge = $true; break } } }
        if (-not $isEdge -and $y -gt 0) { $q = $p - $w * 4; for ($k = 0; $k -lt 4; $k++) { if ($a[$p+$k] -ne $a[$q+$k]) { $isEdge = $true; break } } }
        $edge[$y * $w + $x] = $isEdge
    }
}
function In-Band($edge, $w, $h, $x, $y) {
    for ($dy = -1; $dy -le 1; $dy++) {
        $ny = $y + $dy
        if ($ny -lt 0 -or $ny -ge $h) { continue }
        for ($dx = -1; $dx -le 1; $dx++) {
            $nx = $x + $dx
            if ($nx -ge 0 -and $nx -lt $w -and $edge[$ny * $w + $nx]) { return $true }
        }
    }
    return $false
}
$diff = 0; $violations = 0; $maxDelta = 0; $outside = 0
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $p = ($y * $w + $x) * 4
        $same = $true
        for ($k = 0; $k -lt 4; $k++) { if ($a[$p+$k] -ne $c[$p+$k]) { $same = $false; break } }
        if ($same) { continue }
        $diff++
        $pixelMax = 0
        for ($k = 0; $k -lt 4; $k++) { $d = [Math]::Abs($a[$p+$k] - $c[$p+$k]); if ($d -gt $pixelMax) { $pixelMax = $d } }
        if ($pixelMax -gt $maxDelta) { $maxDelta = $pixelMax }
        if ((In-Band $edge $w $h $x $y)) {
            if ($pixelMax -gt 2) { $violations++ }
        } else {
            $violations++; $outside++
        }
    }
}
$swSha = (Get-FileHash $swBmp -Algorithm SHA256).Hash
$glSha = (Get-FileHash $glBmp -Algorithm SHA256).Hash
Write-Host "diff=$diff violations=$violations outsideBand=$outside maxDelta=$maxDelta ${w}x${h}"
Write-Host "sw SHA256=$swSha"
Write-Host "gl SHA256=$glSha"
if ($violations -gt 0) { Write-Host "FAIL: $violations policy violations"; $fail = $true }

if ($fail) { Write-Host "GOLDEN: FAIL"; exit 1 }
Write-Host "GOLDEN: PASS"
exit 0