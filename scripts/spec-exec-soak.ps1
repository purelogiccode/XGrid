# spec-exec median-of-3 soak (§140 follow-up); private firmware/HDD and test-sample media only.
# Run each title x mode x rep, then use -Compare for correctness + median walls.
param(
    [ValidateSet('menu', 'triangle')][string]$Title = 'menu',
    [ValidateSet('cache', 'spec', 'specverify')][string]$Mode = 'cache',
    [int]$Rep = 1,
    [string]$Media = 'D:\spec-soak\media',
    [string]$OutDir = 'D:\spec-soak\legs',
    [switch]$Compare
)
$ErrorActionPreference = 'Stop'
$menuSha = '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81'
$triSha = 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C'
function Normalize-Log($text) {
    # Preserve every other byte/counter, including frame diagnostics.
    (($text -split '\r?\n' | Where-Object { $_ -notmatch '^(Decode cache|Block stats|Spec stats|Spec fallback|Block execution=|Spec execution=|Fetch fast path)' }) -join "`n") -replace '(?m)^Frame \S+ ', 'Frame <path> '
}
if ($Compare) {
    $rows = @()
    foreach ($t in @('menu', 'triangle')) {
        $sha = if ($t -eq 'menu') { $menuSha } else { $triSha }
        $baseline = Normalize-Log (Get-Content "$OutDir/$t-cache-r1.log" -Raw)
        foreach ($m in @('cache', 'spec', 'specverify')) {
            $walls = @()
            foreach ($r in 1..3) {
                $log = Get-Content "$OutDir/$t-$m-r$r.log" -Raw
                if ((Normalize-Log $log) -cne $baseline) { throw "Unexpected log difference: $t/$m/r$r" }
                $j = Get-Content "$OutDir/$t-$m-r$r.json" -Raw | ConvertFrom-Json
                if ($j.sha256 -ne $sha) { throw "SHA mismatch: $t/$m/r$r $($j.sha256)" }
                $walls += $j.wallSeconds
            }
            $sorted = $walls | Sort-Object
            $rows += [ordered]@{ title = $t; mode = $m; walls = $walls; median = $sorted[1]; sha256 = $sha; result = 'pass' }
        }
        $cm = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'cache' }).median
        $sm = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'spec' }).median
        Write-Host "$t median cache=${cm}s spec=${sm}s spec/cache=$($cm / $sm)"
    }
    $rows | ConvertTo-Json | Set-Content "$OutDir/comparison.json"
    $rows | Format-Table title, mode, median
    Write-Host 'PASS: logs identical except mode announcements/stats and frame filename; SHAs exact.'
    exit 0
}
$cli = 'src/XGrid.Cli/bin/Release/net10.0/XGrid.Cli.dll'
$cliArgs = @($cli, 'boot', '--bios', "$Media/Complex_4627.bin", '--mcpx', "$Media/mcpx_1.0.bin", '--eeprom', "$Media/eeprom.bin", '--hdd', "$Media/xbox_hdd.qcow2", '--steps', '1000000000', '--gpu', 'sw')
if ($Title -eq 'triangle') { $cliArgs += @('--dvd', "$Media/triangle.iso") }
else { $cliArgs += @('--pad-script', 'down@950000000+hold,none@962000000,a@970000000+hold,none@976000000') }
if ($Mode -eq 'spec') { $cliArgs += '--spec-exec' }
if ($Mode -eq 'specverify') { $cliArgs += @('--spec-exec', '--block-verify') }
$sha = if ($Title -eq 'menu') { $menuSha } else { $triSha }
$bmp = "$OutDir/$Title-$Mode-r$Rep.bmp"
$logPath = "$OutDir/$Title-$Mode-r$Rep.log"
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
if ($Mode -eq 'cache') {
    if ($log -notmatch 'Decode cache=on' -or $log -notmatch 'verifyFailures=0\b') { throw 'Cache stats mismatch' }
} else {
    if ($log -notmatch 'Spec execution=on') { throw 'Spec not enabled' }
    if ($log -notmatch 'verifyFailures=0\b') { throw 'Spec verify failures' }
}
[ordered]@{ title = $Title; mode = $Mode; rep = $Rep; command = $command; wallSeconds = $watch.Elapsed.TotalSeconds; sha256 = $actualSha; counters = ([regex]::Match($log, '(?m)^NV2A draws executed=.*').Value); result = 'pass' } | ConvertTo-Json | Set-Content "$OutDir/$Title-$Mode-r$Rep.json"
Write-Host "PASS $Title/$Mode/r$Rep $($watch.Elapsed.TotalSeconds)s SHA=$actualSha"
