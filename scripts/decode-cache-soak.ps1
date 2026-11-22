# CLI-default soak; private firmware/HDD and the test-sample triangle ISO only.
# Run each title with off, default, on, verify, then use -Compare.
param(
    [ValidateSet('menu', 'triangle')][string]$Title = 'menu',
    [ValidateSet('off', 'default', 'on', 'verify')][string]$Mode = 'default',
    [string]$Media = 'wt-data/wave',
    [string]$OutDir = 'wt-data/wave',
    [switch]$Compare
)
$ErrorActionPreference = 'Stop'
$sha = if ($Title -eq 'menu') { '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81' } else { 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C' }
function Normalize-Log($text) {
    # Preserve every other byte/counter, including frame diagnostics.
    (($text -split '\r?\n' | Where-Object { $_ -notmatch '^Decode cache' }) -join "`n") -replace '(?m)^Frame \S+ ', 'Frame <path> '
}
if ($Compare) {
    $baseline = Normalize-Log (Get-Content "$OutDir/$Title-off.log" -Raw)
    $results = foreach ($setting in @('off', 'default', 'on', 'verify')) {
        $log = Get-Content "$OutDir/$Title-$setting.log" -Raw
        if ((Normalize-Log $log) -cne $baseline) { throw "Unexpected log difference: $Title/$setting" }
        Get-Content "$OutDir/$Title-$setting.json" -Raw | ConvertFrom-Json
    }
    $offWall = $results[0].wallSeconds
    $results | ForEach-Object { $_ | Add-Member -NotePropertyName speedupVsOff -NotePropertyValue ($offWall / $_.wallSeconds) }
    $results | ConvertTo-Json | Set-Content "$OutDir/$Title-comparison.json"
    $results | Format-Table title, mode, wallSeconds, speedupVsOff, sha256
    Write-Host 'PASS: entire logs identical except decode announcements/stats and frame filename.'
    exit 0
}
$cli = 'src/XGrid.Cli/bin/Release/net11.0/XGrid.Cli.dll'
$cliArgs = @($cli, 'boot', '--bios', "$Media/Complex_4627.bin", '--mcpx', "$Media/mcpx_1.0.bin", '--eeprom', "$Media/eeprom.bin", '--hdd', "$Media/xbox_hdd.qcow2", '--steps', '1000000000', '--gpu', 'sw')
if ($Title -eq 'triangle') { $cliArgs += @('--dvd', "$Media/triangle.iso") }
else { $cliArgs += @('--pad-script', 'down@950000000+hold,none@962000000,a@970000000+hold,none@976000000') }
switch ($Mode) {
    'off' { $cliArgs += '--no-decode-cache' }
    'on' { $cliArgs += '--decode-cache' }
    'verify' { $cliArgs += '--decode-verify' }
}
$bmp = "$OutDir/$Title-$Mode.bmp"
$logPath = "$OutDir/$Title-$Mode.log"
$cliArgs += @('--frame', $bmp)
$command = 'dotnet ' + ($cliArgs -join ' ')
$watch = [Diagnostics.Stopwatch]::StartNew()
& dotnet @cliArgs > $logPath 2>&1
$exitCode = $LASTEXITCODE
$watch.Stop()
if ($exitCode -ne 0) { throw "Boot failed: $exitCode ($logPath)" }
$log = Get-Content $logPath -Raw
$actualSha = (Get-FileHash $bmp -Algorithm SHA256).Hash
if ($actualSha -ne $sha) { throw "SHA mismatch: $actualSha" }
if ($log -notmatch 'LastFault vec=0\b') { throw 'Guest fault' }
$counter = if ($Title -eq 'menu') { 'NV2A draws executed=33632 skipped=1402 .*frames=686 stalls=686\b' } else { 'NV2A draws executed=2706 .*frames=68 stalls=34\b' }
if ($log -notmatch $counter) { throw 'Counter anchor mismatch' }
if ($Mode -eq 'off') {
    if ($log -match 'Decode cache|Fetch fast path=on') { throw 'Legacy opt-out was not silent' }
} else {
    if ($log -notmatch 'Decode cache=on' -or $log -notmatch 'verifyFailures=0\b') { throw 'Cache/verification mismatch' }
    if (($log -match 'Decode cache verify=on') -ne ($Mode -eq 'verify')) { throw 'Unexpected verification mode' }
}
[ordered]@{ title = $Title; mode = $Mode; command = $command; wallSeconds = $watch.Elapsed.TotalSeconds; sha256 = $actualSha; counters = ([regex]::Match($log, '(?m)^NV2A draws executed=.*').Value); result = 'pass' } | ConvertTo-Json | Set-Content "$OutDir/$Title-$Mode.json"
Write-Host "PASS $Title/$Mode $($watch.Elapsed.TotalSeconds)s SHA=$actualSha"
