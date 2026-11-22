# Emitter median-of-N soak (§§171-179 wall-claim discipline): runs
# title x mode legs in INTERLEAVED ROUNDS (all modes close in time, so host
# drift affects them alike) and prints per-title/per-mode medians. Modes:
# spec, emit, emitinline, emitdiag. Every leg asserts the frame SHA so wall
# numbers are only ever compared across behaviorally identical legs.
# Private firmware/HDD and test-sample media only.
param(
    [string]$Title = 'menu,triangle',
    [string]$Mode = 'spec,emit,emitinline',
    [int]$Reps = 3,
    [int]$FirstRep = 1,
    [string]$Media = 'D:\spec-soak\media',
    [string]$OutDir = 'D:\spec-soak\median'
)
$ErrorActionPreference = 'Stop'
$menuSha = '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81'
$triSha = 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C'
$cli = 'src/XGrid.Cli/bin/Release/net11.0/XGrid.Cli.dll'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$titles = $Title.Split(',')
$modes = $Mode.Split(',')
$walls = @{}

foreach ($r in $FirstRep..($FirstRep + $Reps - 1)) {
    foreach ($t in $titles) {
        foreach ($m in $modes) {
            $cliArgs = @($cli, 'boot', '--bios', "$Media/Complex_4627.bin", '--mcpx', "$Media/mcpx_1.0.bin", '--eeprom', "$Media/eeprom.bin", '--hdd', "$Media/xbox_hdd.qcow2", '--steps', '1000000000', '--gpu', 'sw')
            if ($t -eq 'triangle') { $cliArgs += @('--dvd', "$Media/triangle.iso") }
            else { $cliArgs += @('--pad-script', 'down@950000000+hold,none@962000000,a@970000000+hold,none@976000000') }
            switch ($m) {
                'spec' { $cliArgs += '--spec-exec' }
                'emit' { $cliArgs += '--emit' }
                'emitinline' { $cliArgs += '--emit-inline' }
                'emitdiag' { $cliArgs += @('--emit-inline', '--emit-diag') }
            }
            $bmp = "$OutDir/$t-$m-r$r.bmp"
            $logPath = "$OutDir/$t-$m-r$r.log"
            $cliArgs += @('--frame', $bmp)
            if (Test-Path $bmp) { Remove-Item $bmp -Force }
            $watch = [Diagnostics.Stopwatch]::StartNew()
            & dotnet @cliArgs > $logPath 2>&1
            $exitCode = $LASTEXITCODE
            $watch.Stop()
            if ($exitCode -ne 0) { throw "Boot failed: $exitCode ($logPath)" }
            $sha = if ($t -eq 'menu') { $menuSha } else { $triSha }
            $actualSha = (Get-FileHash $bmp -Algorithm SHA256).Hash
            if ($actualSha -ne $sha) { throw "SHA mismatch: $t/$m/r$r $actualSha" }
            $log = Get-Content $logPath -Raw
            if ($log -notmatch 'LastFault vec=0\b') { throw "Guest fault: $t/$m/r$r" }
            $key = "$t|$m"
            if (-not $walls.ContainsKey($key)) { $walls[$key] = @() }
            $walls[$key] += $watch.Elapsed.TotalSeconds
            Write-Host "leg $t/$m/r$r $([math]::Round($watch.Elapsed.TotalSeconds,1))s"
        }
    }
}

$rows = @()
foreach ($t in $titles) {
    foreach ($m in $modes) {
        $list = $walls["$t|$m"] | Sort-Object
        $median = $list[[int][math]::Floor($list.Count / 2)]
        $rows += [ordered]@{ title = $t; mode = $m; walls = ($list | ForEach-Object { [math]::Round($_, 1) }); median = [math]::Round($median, 1) }
    }
}
$rows | ConvertTo-Json | Set-Content "$OutDir/median.json"
$rows | Format-Table title, mode, median

foreach ($t in $titles) {
    $meds = @{}
    foreach ($m in $modes) { $meds[$m] = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq $m }).median }
    Write-Host "$t medians: $(($meds.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '  ')"
}
