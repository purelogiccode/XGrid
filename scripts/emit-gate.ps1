# Emitter golden gate (§§171-175): runs one title x mode leg at 1B steps and
# asserts the frame SHA, counter anchors, no guest fault and zero verify
# failures. Modes: cache (flag-off baseline), spec, emit, emitinline.
# Private firmware/HDD and test-sample media only.
param(
    [ValidateSet('menu', 'triangle')][string]$Title = 'menu',
    [ValidateSet('cache', 'spec', 'emit', 'emitinline', 'emitdiag')][string]$Mode = 'spec',
    [int]$Rep = 1,
    [string]$Media = 'D:\spec-soak\media',
    [string]$OutDir = 'D:\spec-soak\legs'
)
$ErrorActionPreference = 'Stop'
$menuSha = '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81'
$triSha = 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C'
$cli = 'src/XGrid.Cli/bin/Release/net11.0/XGrid.Cli.dll'
$cliArgs = @($cli, 'boot', '--bios', "$Media/Complex_4627.bin", '--mcpx', "$Media/mcpx_1.0.bin", '--eeprom', "$Media/eeprom.bin", '--hdd', "$Media/xbox_hdd.qcow2", '--steps', '1000000000', '--gpu', 'sw')
if ($Title -eq 'triangle') { $cliArgs += @('--dvd', "$Media/triangle.iso") }
else { $cliArgs += @('--pad-script', 'down@950000000+hold,none@962000000,a@970000000+hold,none@976000000') }
switch ($Mode) {
    'cache' { }
    'spec' { $cliArgs += '--spec-exec' }
    'emit' { $cliArgs += '--emit' }
    'emitinline' { $cliArgs += '--emit-inline' }
    'emitdiag' { $cliArgs += @('--emit-inline', '--emit-diag') }
}
$sha = if ($Title -eq 'menu') { $menuSha } else { $triSha }
$bmp = "$OutDir/$Title-$Mode-r$Rep.bmp"
$logPath = "$OutDir/$Title-$Mode-r$Rep.log"
$cliArgs += @('--frame', $bmp)
$command = 'dotnet ' + ($cliArgs -join ' ')
$watch = [Diagnostics.Stopwatch]::StartNew()
if (Test-Path $bmp) { Remove-Item $bmp -Force }
& dotnet @cliArgs > $logPath 2>&1
$exitCode = $LASTEXITCODE
$watch.Stop()
if ($exitCode -ne 0) { throw "Boot failed: $exitCode ($logPath)" }
$log = Get-Content $logPath -Raw
if ($log -match 'Frame unavailable') { throw 'Frame unavailable (no present happened)' }
if (!(Test-Path $bmp)) { throw "Frame BMP missing: $bmp" }
$actualSha = (Get-FileHash $bmp -Algorithm SHA256).Hash
if ($actualSha -ne $sha) { throw "SHA mismatch: $actualSha" }
if ($log -notmatch 'LastFault vec=0\b') { throw 'Guest fault' }
$counter = if ($Title -eq 'menu') { 'NV2A draws executed=33632 skipped=1402 .*frames=686 stalls=686\b' } else { 'NV2A draws executed=2706 .*frames=68 stalls=34\b' }
if ($log -notmatch $counter) { throw 'Counter anchor mismatch' }
if ($Mode -ne 'cache' -and $log -notmatch 'verifyFailures=0\b') { throw 'Verify failures reported' }
if ($Mode -eq 'emit' -and $log -notmatch 'Emit stats builds=') { throw 'Emit not enabled' }
if ($Mode -eq 'emitinline' -and $log -notmatch 'Emit inline bodies=on') { throw 'Inline emit not enabled' }
if ($Mode -eq 'emitdiag' -and $log -notmatch 'Emit inline diag=') { throw 'Inline diag not enabled' }
[ordered]@{ title = $Title; mode = $Mode; rep = $Rep; command = $command; wallSeconds = $watch.Elapsed.TotalSeconds; sha256 = $actualSha; counters = ([regex]::Match($log, '(?m)^NV2A draws executed=.*').Value); result = 'pass' } | ConvertTo-Json | Set-Content "$OutDir/$Title-$Mode-r$Rep.json"
Write-Host "PASS $Title/$Mode/r$Rep $([math]::Round($watch.Elapsed.TotalSeconds,1))s SHA=$actualSha"
