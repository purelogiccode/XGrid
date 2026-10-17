# Emitter median-of-3 soak (§§171-175 wall-claim discipline): runs
# title x mode x rep legs at 1B steps and prints per-mode medians. Modes:
# spec, emit, emitinline. Every leg asserts the frame SHA so wall numbers
# are only ever compared across behaviorally identical legs.
# Private firmware/HDD and test-sample media only.
param(
    [string]$Title = 'menu,triangle',
    [string]$Mode = 'emit,emitinline',
    [int]$Reps = 3,
    [string]$Media = 'D:\spec-soak\media',
    [string]$OutDir = 'D:\spec-soak\median'
)
$ErrorActionPreference = 'Stop'
$menuSha = '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81'
$triSha = 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C'
$cli = 'src/XGrid.Cli/bin/Release/net10.0/XGrid.Cli.dll'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$rows = @()
foreach ($t in $Title.Split(',')) {
    foreach ($m in $Mode.Split(',')) {
        $walls = @()
        foreach ($r in 1..$Reps) {
            $cliArgs = @($cli, 'boot', '--bios', "$Media/Complex_4627.bin", '--mcpx', "$Media/mcpx_1.0.bin", '--eeprom', "$Media/eeprom.bin", '--hdd', "$Media/xbox_hdd.qcow2", '--steps', '1000000000', '--gpu', 'sw')
            if ($t -eq 'triangle') { $cliArgs += @('--dvd', "$Media/triangle.iso") }
            else { $cliArgs += @('--pad-script', 'down@950000000+hold,none@962000000,a@970000000+hold,none@976000000') }
            switch ($m) {
                'spec' { $cliArgs += '--spec-exec' }
                'emit' { $cliArgs += '--emit' }
                'emitinline' { $cliArgs += '--emit-inline' }
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
            $walls += $watch.Elapsed.TotalSeconds
            Write-Host "leg $t/$m/r$r $([math]::Round($watch.Elapsed.TotalSeconds,1))s"
        }

        $sorted = $walls | Sort-Object
        $median = $sorted[[int][math]::Floor($sorted.Count / 2)]
        $rows += [ordered]@{ title = $t; mode = $m; walls = ($walls | ForEach-Object { [math]::Round($_, 1) }); median = [math]::Round($median, 1) }
    }
}

foreach ($t in $Title.Split(',')) {
    $emit = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'emit' }).median
    $inline = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'emitinline' }).median
    if ($null -ne $emit -and $null -ne $inline) {
        Write-Host "$t median emit=${emit}s emitinline=${inline}s emit/emitinline=$([math]::Round($emit / $inline, 3))"
    }
}
$rows | ConvertTo-Json | Set-Content "$OutDir/median.json"
$rows | Format-Table title, mode, median
