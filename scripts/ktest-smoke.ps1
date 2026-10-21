# ktest-smoke.ps1 — scripted ~2-min cross-emulator kernel-test-suite smoke (§123).
#
# Builds a minimal 7-test subset ISO (tests=51,71,224,231,282,319,344, CRLF
# config.txt), runs it under XGrid (--break 4E03A + --writedump FATX readback)
# and under xemu.exe (private profile + own xemu.toml, monitor park-detect),
# then diffs the pass/fail tables against the §120 known deltas.
#
# All media live under wt-data\ktest-smoke\ (gitignored); the shared firmware
# checkout is only READ (private copies are booted, never the shared files).
#
# Usage (from the worktree root, after dotnet build -c Release):
#   pwsh -File scripts/ktest-smoke.ps1 `
#     -FirmwareDir "C:\...\CSharp_XboxEmulator\data\xemu-0.8.136-windows-x86_64" `
#     -SuiteXbe "C:\...\CSharp_XboxEmulator\data\test-samples\kernel-test-suite-default.xbe"
# See tools/ktest-recon/README.md (Smoke section) for the exact §123 command.
#
# Exit 0 = SMOKE PASS (known deltas match, no unexpected failures).

param(
    [string]$FirmwareDir = "data\xemu-0.8.136-windows-x86_64",
    [string]$SuiteXbe = "data\test-samples\kernel-test-suite-default.xbe",
    [string]$XemuExe = "",
    [string]$XisoSharp = "",
    [string]$WorkDir = "wt-data\ktest-smoke",
    [string]$Subset = "51,71,224,231,282,319,344",
    [string]$Steps = "2000000000",
    # -SkipXemu leaves no xemu leg to read, but the pairwise diff still needs the
    # oracle log from the last full run. Default: the standing work dir's copy.
    [string]$XemuOracleLog = "wt-data\ktest-smoke\xemu\xemu-log.txt",
    [int]$XemuPort = 4455,
    [double]$XemuTimeout = 420.0,
    [switch]$SkipXgrid,
    [switch]$SkipXemu
)

$ErrorActionPreference = "Stop"
$Wall = [Diagnostics.Stopwatch]::StartNew()
function Stamp([string]$m) { Write-Host ("[{0:mm\:ss}] {1}" -f $Wall.Elapsed, $m) }

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

# XISOSharp lives in the sibling repo CSharp_XISOSharp. Resolve it from this
# repo's parent so the gate works on any machine and user name; pass
# -XisoSharp explicitly to override. (The old absolute default embedded one
# machine's profile path and a stale TFM.)
if ($XisoSharp -eq "") {
    $sibling = Join-Path (Split-Path $Root -Parent) "CSharp_XISOSharp\XISOSharp.Cli\bin\Release"
    foreach ($tfm in "net10.0", "net9.0", "net8.0") {
        $candidate = Join-Path $sibling "$tfm\XISOSharp.exe"
        if (Test-Path $candidate) { $XisoSharp = $candidate; break }
    }
    if ($XisoSharp -eq "" -and (Test-Path $sibling)) {
        $found = Get-ChildItem -Path $sibling -Recurse -Filter XISOSharp.exe -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { $XisoSharp = $found.FullName }
    }
}

$cli = "src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
if (-not (Test-Path $cli)) { Write-Error "Build first: dotnet build XGrid.sln -c Release"; exit 2 }
if (-not (Test-Path $FirmwareDir)) { Write-Error "Missing firmware dir: $FirmwareDir"; exit 2 }
if (-not (Test-Path $SuiteXbe)) { Write-Error "Missing suite XBE: $SuiteXbe"; exit 2 }
if (-not (Test-Path $XisoSharp)) { Write-Error "Missing XISOSharp.exe: pass -XisoSharp <path> (looked for the sibling repo CSharp_XISOSharp)"; exit 2 }
if ($XemuExe -eq "") { $XemuExe = Join-Path $FirmwareDir "xemu.exe" }
if (-not (Test-Path $XemuExe)) { Write-Error "Missing xemu.exe: $XemuExe"; exit 2 }

$G = Join-Path $WorkDir "xgrid"
$XE = Join-Path $WorkDir "xemu"
$IsoDir = Join-Path $G "iso"
$WdDir = Join-Path $G "xgrid-wd"
New-Item -ItemType Directory -Path $IsoDir -Force | Out-Null
New-Item -ItemType Directory -Path $XE -Force | Out-Null
if (Test-Path $WdDir) { Remove-Item -Recurse -Force $WdDir }

# --- 1. private media copies (never boot against shared files) ---
Stamp "staging private media"
foreach ($f in @("Complex_4627.bin", "mcpx_1.0.bin", "eeprom.bin")) {
    Copy-Item (Join-Path $FirmwareDir $f) (Join-Path $G $f) -Force
    Copy-Item (Join-Path $FirmwareDir $f) (Join-Path $XE $f) -Force
}
$HddTemplate = Join-Path $FirmwareDir "xbox_hdd.qcow2"
if (-not (Test-Path (Join-Path $G "ktest-hdd.qcow2"))) {
    Stamp "copying XGrid HDD template (1.5 GB, one-time)"
    Copy-Item $HddTemplate (Join-Path $G "ktest-hdd.qcow2") -Force
}
# XGrid opens the HDD read-only (Qcow2DiskImage FileAccess.Read + HddOverlay
# journal), so its copy stays pristine. xemu commits guest writes into its
# qcow2, so it is refreshed from the template on every run.
Stamp "refreshing xemu HDD from template (1.5 GB)"
Copy-Item $HddTemplate (Join-Path $XE "ktest-hdd.qcow2") -Force
if (-not (Test-Path (Join-Path $XE "xemu.exe"))) {
    Stamp "copying xemu.exe into profile (one-time)"
    Copy-Item $XemuExe (Join-Path $XE "xemu.exe") -Force
}

# --- 2. patch XBE + subset config + ISO ---
Stamp "patching suite XBE (C: log + symlink shellcode + 4E03A park)"
$ParkedXbe = Join-Path $G "default-parked.xbe"
python tools/ktest-recon/patch_suite_xbe.py --in $SuiteXbe --out $ParkedXbe `
    --log C --target '\Device\Harddisk0\Partition2' --park 4E03A
if ($LASTEXITCODE -ne 0) { Write-Error "patch_suite_xbe failed"; exit 1 }
Copy-Item $ParkedXbe (Join-Path $IsoDir "default.xbe") -Force
[IO.File]::WriteAllBytes((Join-Path $IsoDir "config.txt"),
    [Text.Encoding]::ASCII.GetBytes("disable-video=1`r`ntests=$Subset`r`n"))
$Iso = Join-Path $G "ktest-smoke.iso"
Stamp "building subset ISO"
$env:XISO_NO_UPDATE_CHECK = 1
& $XisoSharp -y -m -c $IsoDir $Iso | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { Write-Error "XISOSharp failed"; exit 1 }
Copy-Item $Iso (Join-Path $XE "ktest-smoke.iso") -Force
@'
[sys.files]
bootrom_path = 'mcpx_1.0.bin'
flashrom_path = 'Complex_4627.bin'
eeprom_path = 'eeprom.bin'
hdd_path = 'ktest-hdd.qcow2'
dvd_path = 'ktest-smoke.iso'
'@ | Set-Content (Join-Path $XE "xemu.toml") -Encoding Ascii
# NOTE: xemu 0.8.136 does not bridge xemu.toml flashrom_path into QEMU -bios
# (launch params omit -bios -> "Failed to load BIOS '(null)' -> guest stuck at
# reset-vector 0xFFF0); the runner passes -bios explicitly. The toml row is
# kept so a fixed xemu keeps working. xemu also starts "paused (prelaunch)"
# under monitor automation; the runner sends 'cont'.

$xgridLog = Join-Path $G "xgrid-log.txt"
$xemuLog = Join-Path $XE "xemu-log.txt"

# The diff step reads both logs even under -SkipXemu (the differential is
# against the §120 xemu oracle run, not against this invocation). Reuse the
# stored oracle log rather than failing on a fresh work dir, and fail loudly -
# never silently - when there is no oracle to compare against.
if ($SkipXemu -and -not (Test-Path $xemuLog)) {
    if (-not (Test-Path $XemuOracleLog)) {
        Write-Error "No xemu oracle log for the pairwise diff: pass -XemuOracleLog <path> or run once without -SkipXemu"
        exit 2
    }

    Copy-Item $XemuOracleLog $xemuLog -Force
    Stamp "using stored xemu oracle log $XemuOracleLog"
}

# --- 3. XGrid leg ---
if (-not $SkipXgrid) {
    Stamp "XGrid leg starting"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $xgridOut = Join-Path $G "xgrid.log"
    & dotnet $cli boot --bios (Join-Path $G "Complex_4627.bin") `
        --mcpx (Join-Path $G "mcpx_1.0.bin") --eeprom (Join-Path $G "eeprom.bin") `
        --hdd (Join-Path $G "ktest-hdd.qcow2") --dvd $Iso `
        --steps $Steps --break 4E03A --writedump $WdDir 2>&1 |
        Tee-Object -FilePath $xgridOut | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "XGrid boot failed exit=$LASTEXITCODE (see $xgridOut)"; exit 1 }
    $sw.Stop()
    Stamp ("XGrid leg done in {0:mm\:ss}" -f $sw.Elapsed)
    $breakLine = Select-String -Path $xgridOut -Pattern "Break: EIP=" |
        Select-Object -Last 1 -ExpandProperty Line
    Write-Host "XGrid $breakLine"
    if ($breakLine -notmatch "EIP=0x0004E03A") { Write-Error "XGrid did not stop at the 4E03A park: $breakLine"; exit 1 }
    Stamp "XGrid FATX readback"
    python tools/ktest-recon/read_writedump_fatx.py $WdDir $xgridLog
    if ($LASTEXITCODE -ne 0) { Write-Error "writedump readback failed"; exit 1 }
}

# --- 4. xemu leg ---
if (-not $SkipXemu) {
    Stamp "xemu leg starting"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    python tools/ktest-recon/run_xemu_ktest_smoke.py --dir $XE `
        --xemu-exe (Join-Path $XE "xemu.exe") --tag smoke --port $XemuPort `
        --timeout $XemuTimeout 2>&1 | Tee-Object -FilePath (Join-Path $XE "smoke-run.log")
    if ($LASTEXITCODE -ne 0) { Write-Error "xemu leg failed (no park detected)"; exit 1 }
    $sw.Stop()
    Stamp ("xemu leg done in {0:mm\:ss}" -f $sw.Elapsed)
    Stamp "xemu FATX readback"
    python tools/ktest-recon/read_fatx_qcow2.py (Join-Path $XE "ktest-hdd.qcow2") $xemuLog
    if ($LASTEXITCODE -ne 0) { Write-Error "qcow2 readback failed"; exit 1 }
}

# --- 5. diff vs §120 known deltas ---
Stamp "diffing logs"
python tools/ktest-recon/ktest_diff.py $xgridLog $xemuLog
$verdict = $LASTEXITCODE
Stamp ("total wall {0:mm\:ss}" -f $Wall.Elapsed)
if ($verdict -ne 0) { Write-Host "KTEST-SMOKE: FAIL"; exit 1 }
Write-Host "KTEST-SMOKE: PASS"
exit 0
