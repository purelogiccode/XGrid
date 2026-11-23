# Compares soak legs across modes for one rep range (SHA + full-log identity
# modulo mode announcements/stats and the frame path). Slice-5+ companion to
# spec-exec-soak.ps1 -Compare (which covers reps 1-3).
param(
    [int]$First = 14,
    [int]$Last = 16,
    [string]$OutDir = 'D:\spec-soak\legs'
)
$ErrorActionPreference = 'Stop'
function Normalize-Log($text) {
    (($text -split '\r?\n' | Where-Object { $_ -notmatch '^(Decode cache|Block stats|Spec stats|Spec fallback|Block execution=|Spec execution=|Fetch fast path)' }) -join "`n") -replace '(?m)^Frame \S+ ', 'Frame <path> '
}
$menuSha = '4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81'
$triSha = 'A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C'
$rows = @()
foreach ($t in @('menu', 'triangle')) {
    $sha = if ($t -eq 'menu') { $menuSha } else { $triSha }
    $baseline = Normalize-Log (Get-Content "$OutDir/$t-cache-r$First.log" -Raw)
    foreach ($m in @('cache', 'spec', 'specverify')) {
        $walls = @()
        foreach ($r in $First..$Last) {
            $log = Get-Content "$OutDir/$t-$m-r$r.log" -Raw
            if ((Normalize-Log $log) -cne $baseline) { throw "Unexpected log difference: $t/$m/r$r" }
            $j = Get-Content "$OutDir/$t-$m-r$r.json" -Raw | ConvertFrom-Json
            if ($j.sha256 -ne $sha) { throw "SHA mismatch: $t/$m/r$r $($j.sha256)" }
            if ($j.result -ne 'pass') { throw "Leg not pass: $t/$m/r$r" }
            $walls += $j.wallSeconds
        }
        $sorted = $walls | Sort-Object
        $rows += [ordered]@{ title = $t; mode = $m; walls = $walls; median = $sorted[1]; sha256 = $sha; result = 'pass' }
    }
    $cm = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'cache' }).median
    $sm = ($rows | Where-Object { $_.title -eq $t -and $_.mode -eq 'spec' }).median
    Write-Host "$t median cache=${cm}s spec=${sm}s spec/cache=$($cm / $sm)"
}
$rows | ConvertTo-Json | Set-Content "$OutDir/comparison-r$First-r$Last.json"
Write-Host 'PASS: logs identical except mode announcements/stats and frame filename; SHAs exact.'
