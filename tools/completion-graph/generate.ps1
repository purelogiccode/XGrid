# generate.ps1 - renders completion-data.json as an SVG treemap (+ self-contained HTML).
# Area = phase weight (share of remaining effort); color = percent done (red 0% -> yellow 50% -> green 100%).
# Usage: pwsh -File tools/completion-graph/generate.ps1
param(
    [string]$DataPath = (Join-Path $PSScriptRoot 'completion-data.json'),
    [string]$SvgOut = (Join-Path $PSScriptRoot 'completion-treemap.svg'),
    [string]$HtmlOut = (Join-Path $PSScriptRoot 'completion-treemap.html')
)
$ErrorActionPreference = 'Stop'
# pt-BR hosts format doubles with commas ("389,6"), which is invalid in SVG attributes.
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$data = Get-Content $DataPath -Raw | ConvertFrom-Json

# ---------- geometry ----------
$W = 1680; $H = 1060
$M = 22
$HEAD = 64          # title band
$FOOT = 54          # legend band
$contentX = [double]$M
$contentY = [double]($M + $HEAD)
$contentW = [double]($W - 2 * $M)
$contentH = [double]($H - $M * 2 - $HEAD - $FOOT)

$svg = [System.Text.StringBuilder]::new()

function Get-Color([double]$pct) {
    $t = $pct / 100.0
    if ($t -le 0.5) {
        $u = $t / 0.5
        $r = 232; $g = [int](92 + (198 - 92) * $u); $b = 40
    } else {
        $u = ($t - 0.5) / 0.5
        $r = [int](198 + (30 - 198) * $u); $g = [int](165 + (200 - 165) * $u); $b = [int](50 + (90 - 50) * $u)
    }
    return "#{0:X2}{1:X2}{2:X2}" -f $r, $g, $b
}

function Test-Light([double]$pct) { return $pct -ge 62 }

function Invoke-Squarify {
    param([object[]]$Items, [double]$X, [double]$Y, [double]$W, [double]$H, [double]$Scale, [int]$Depth)
    # Items: objects with .Area; emits { x, y, w, h } rects proportional to area
    $rects = New-Object System.Collections.Generic.List[object]
    if ($Items.Count -eq 0) { return $rects }
    $remaining = [System.Collections.ArrayList]::new()
    foreach ($it in $Items) { [void]$remaining.Add($it) }
    $cx = $X; $cy = $Y; $cw = $W; $ch = $H

    while ($remaining.Count -gt 0) {
        $short = [Math]::Min($cw, $ch)
        $rowSum = 0.0
        $prevWorst = [double]::MaxValue
        $bestCount = 0
        $running = 0.0
        for ($i = 0; $i -lt $remaining.Count; $i++) {
            $running += $remaining[$i].Area
            $side = $running / $short
            $worst = 0.0
            for ($k = 0; $k -le $i; $k++) {
                $other = $remaining[$k].Area / $side
                $ratio = [Math]::Max($side / $other, $other / $side)
                if ($ratio -gt $worst) { $worst = $ratio }
            }
            if ($worst -le $prevWorst) { $prevWorst = $worst; $bestCount = $i + 1 } else { break }
        }

        $rowSum = 0.0
        for ($i = 0; $i -lt $bestCount; $i++) { $rowSum += $remaining[$i].Area }
        $row = New-Object System.Collections.Generic.List[object]
        for ($i = 0; $i -lt $bestCount; $i++) { $row.Add($remaining[$i]) }
        $remaining.RemoveRange(0, $bestCount)

        if ($cw -ge $ch) {
            $side = $rowSum / [Math]::Max($ch, 1)
            $ry = $cy
            foreach ($r in $row) {
                $rh = $r.Area / [Math]::Max($side, 0.001)
                $rects.Add([pscustomobject]@{ item = $r; x = $cx; y = $ry; w = $side; h = $rh })
                $ry += $rh
            }
            $cx += $side; $cw -= $side
        } else {
            $side = $rowSum / [Math]::Max($cw, 1)
            $rx = $cx
            foreach ($r in $row) {
                $rw = $r.Area / [Math]::Max($side, 0.001)
                $rects.Add([pscustomobject]@{ item = $r; x = $rx; y = $cy; w = $rw; h = $side })
                $rx += $rw
            }
            $cy += $side; $ch -= $side
        }
    }
    return $rects
}

function ConvertTo-XmlText([string]$s) {
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

# estimate wrapped lines for one string at a scale factor (px per char is ~0.58*fontsize for bold Segoe)
function Get-WrappedLines([string]$text, [double]$fontSize, [double]$maxWidth) {
    $words = $text -split ' '
    $lines = New-Object System.Collections.Generic.List[string]
    $current = ''
    $cpl = [Math]::Max(3, [int]($maxWidth / (0.58 * $fontSize)))
    foreach ($word in $words) {
        $w = $word
        if ($w.Length -gt $cpl) { $w = $w.Substring(0, [Math]::Max(1, $cpl - 1)) + [char]0x2026 }
        if ($current -eq '') { $current = $w }
        elseif (($current.Length + 1 + $w.Length) -le $cpl) { $current = "$current $w" }
        else { $lines.Add($current); $current = $w }
    }
    if ($current -ne '') { $lines.Add($current) }
    return $lines
}

# ---------- pass 1: top-level squarify of the two tracks ----------
$totalWeight = ($data.tracks | ForEach-Object { ([double]($_.phases | Measure-Object -Property weight -Sum).Sum) } | Measure-Object -Sum).Sum
$trackScale = ($contentW * $contentH) / $totalWeight
$trackItems = @($data.tracks | ForEach-Object { [pscustomobject]@{ Area = ([double]($_.phases | Measure-Object -Property weight -Sum).Sum) * $trackScale; track = $_ } })
$trackRects = Invoke-Squarify -Items $trackItems -X $contentX -Y $contentY -W $contentW -H $contentH -Scale 1 -Depth 0

# ---------- svg scaffold ----------
$manifest = [System.IO.Path]::GetFileName($SvgOut)
[void]$svg.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' width='$W' height='$H' viewBox='0 0 $W $H' font-family='Segoe UI, Segoe UI Emoji, sans-serif'>")
[void]$svg.AppendLine("<rect width='$W' height='$H' fill='#0B1220'/>")
[void]$svg.AppendLine("<text x='$M' y='34' font-size='25' font-weight='700' fill='#F8FAFC'>XGrid - code completion by phase</text>")
[void]$svg.AppendLine("<text x='$M' y='55' font-size='13.5' fill='#94A3B8'>Tile size = phase weight (share of remaining effort). Tile color = percent done. Refreshed $($data.generated) - ImplementationPlan assessment ($([Math]::Round($totalWeight,0)) weight pts).</text>")

# overall weighted percent for the tracker dot
$doneAcc = 0.0
foreach ($track in $data.tracks) { foreach ($ph in $track.phases) { $doneAcc += $ph.weight * ($ph.done / 100.0) } }
$overall = [Math]::Round(100.0 * $doneAcc / $totalWeight, 1)

# ---------- per track: label band + per-phase squarify ----------
foreach ($tr in $trackRects) {
    $track = $tr.item.track
    $band = 30.0
    $hasBand = (($tr.w * $tr.h) -gt 30000)
    if ($hasBand) {
        [void]$svg.AppendLine("<text x='$([int]($tr.x + 8))' y='$([int]($tr.y + 20))' font-size='14' font-weight='700' fill='#CBD5E1'>$(ConvertTo-XmlText $track.name)</text>")
        $px = $tr.x + 6; $py = $tr.y + $band + 4; $pw = $tr.w - 12; $ph_ = $tr.h - $band - 8
    } else {
        $px = $tr.x + 3; $py = $tr.y + 3; $pw = $tr.w - 6; $ph_ = $tr.h - 6
    }
    if ($pw -le 8 -or $ph_ -le 8) { continue }

    $tw = [double](($track.phases | Measure-Object -Property weight -Sum).Sum)
    $phaseScale = ($pw * $ph_) / $tw
    $phaseItems = @($track.phases | Sort-Object -Property weight -Descending | ForEach-Object { [pscustomobject]@{ Area = [double]$_.weight * $phaseScale; phase = $_ } })
    $phaseRects = Invoke-Squarify -Items $phaseItems -X $px -Y $py -W $pw -H $ph_ -Scale 1 -Depth 1

    foreach ($pr in $phaseRects) {
        $ph = $pr.item.phase
        $color = Get-Color $ph.done
        $fg = if (Test-Light $ph.done) { '#111827' } else { '#FFFFFF' }
        $rr = [Math]::Min(10, [Math]::Min($pr.w, $pr.h) / 4)
        $rectX = $pr.x.ToString('0.#'); $rectY = $pr.y.ToString('0.#'); $rectW = $pr.w.ToString('0.#'); $rectH = $pr.h.ToString('0.#'); $rectR = $rr.ToString('0.#')
        [void]$svg.AppendLine("<rect class='tile' data-p='$($ph.p)' data-name='$(ConvertTo-XmlText $ph.name)' data-done='$($ph.done)' data-weight='$($ph.weight)' x='$rectX' y='$rectY' width='$rectW' height='$rectH' rx='$rectR' fill='$color' stroke='#0B1220' stroke-width='1.5'/>")

        $area = $pr.w * $pr.h
        if ($area -lt 1800) { continue }

        $pad = 9.0
        $availW = [Math]::Max(20, $pr.w - 2 * $pad)
        # pick font size: prefer comfortable, shrink to fit
        $nameFs = 0.0; $lines = $null
        foreach ($cand in @(24, 21, 18, 16, 14, 12, 11)) {
            $ls = Get-WrappedLines $ph.name $cand $availW
            $needed = $ls.Count * ($cand * 1.18) + $cand * 1.0 + 8
            if (($needed -le $pr.h - 2 * $pad) -and ($ls.Count -le 3)) { $nameFs = $cand; $lines = $ls; break }
        }
        if ($nameFs -eq 0) {
            # no room for full name: largest single line with ellipsis
            foreach ($cand in @(14, 12, 11, 10)) {
                if (($cand * 1.3 + 14) -le ($pr.h - 2 * $pad) -and $availW -gt ($cand * 3)) { $nameFs = $cand; break }
            }
            if ($nameFs -eq 0) { continue }
            $cpl = [int]($availW / (0.58 * $nameFs))
            if ($cpl -lt 3) { continue }
            $short = $ph.name
            if ($short.Length -gt $cpl) { $short = $short.Substring(0, [Math]::Max(1, $cpl - 1)) + [char]0x2026 }
            $lines = @($short)
        }

        # draw the phase chip + name lines
        $chipSize = [Math]::Min(30.0, [Math]::Max(16.0, $nameFs * 1.1))
        $chipY = $pr.y + $pad
        $chipX = $pr.x + $pad
        $chipColor = if (Test-Light $ph.done) { 'rgba(17,24,39,0.16)' } else { 'rgba(255,255,255,0.20)' }
        $chipR = ($chipSize / 4).ToString('0.#')
        [void]$svg.AppendLine("<rect x='$($chipX.ToString('0.#'))' y='$($chipY.ToString('0.#'))' width='$($chipSize.ToString('0.#'))' height='$($chipSize.ToString('0.#'))' rx='$chipR' fill='$chipColor'/>")
        $chipTx = ($chipX + $chipSize / 2).ToString('0.#'); $chipTy = ($chipY + $chipSize * 0.68).ToString('0.#'); $chipFs = ($chipSize * 0.52).ToString('0.#')
        [void]$svg.AppendLine("<text x='$chipTx' y='$chipTy' font-size='$chipFs' font-weight='700' fill='$fg' text-anchor='middle'>P$($ph.p)</text>")

        $textX = $pr.x + $pad
        $textY = $chipY + $chipSize + 6 + $nameFs
        foreach ($line in $lines) {
            [void]$svg.AppendLine("<text x='$($textX.ToString('0.#'))' y='$($textY.ToString('0.#'))' font-size='$($nameFs.ToString('0.#'))' font-weight='700' fill='$fg'>$(ConvertTo-XmlText $line)</text>")
            $textY += $nameFs * 1.18
        }
        # percent + weight under the name, when there is room
        $subFs = [Math]::Max(11.0, $nameFs * 0.72)
        $subY = $textY + $subFs * 0.6 + 2
        $subText = "$($ph.done)% done &#183; weight $($ph.weight)"
        $subEst = ($ph.done.ToString().Length + 22) * 0.5 * $subFs
        if (($subY - $pr.y) -le ($pr.h - 6) -and $subEst -le $availW) {
            [void]$svg.AppendLine("<text x='$($textX.ToString('0.#'))' y='$($subY.ToString('0.#'))' font-size='$($subFs.ToString('0.#'))' fill='$fg' opacity='0.92'>$subText</text>")
        }
    }
}

$legendY = $H - $M - 30
$lx = $M + 4
[void]$svg.AppendLine("<text x='$lx' y='$($legendY - 8)' font-size='12.5' fill='#94A3B8'>completion</text>")
for ($i = 0; $i -lt 100; $i++) {
    $color = Get-Color ($i + 0.5)
    [void]$svg.AppendLine("<rect x='{0:0.#}' y='$legendY' width='4.2' height='13' fill='$color'/>" -f ($lx + $i * 4.2))
}
[void]$svg.AppendLine("<text x='$($lx + 2)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>0%</text>")
[void]$svg.AppendLine("<text x='$([int]($lx + 210))' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>50%</text>")
[void]$svg.AppendLine("<text x='$([int]($lx + 420) - 8)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>100%</text>")
[void]$svg.AppendLine("<circle cx='$($W - $M - 96)' cy='$($legendY + 6)' r='9' fill='none' stroke='#F8FAFC' stroke-width='3' stroke-dasharray='26 34.5' transform='rotate(-90 $($W - $M - 96) $($legendY + 6))'/>")
[void]$svg.AppendLine("<text x='$($W - $M - 96)' y='$($legendY + 10)' font-size='10.5' font-weight='700' fill='#F8FAFC' text-anchor='middle'>$overall%</text>")
[void]$svg.AppendLine("<text x='$($W - $M)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8' text-anchor='end'>weighted overall</text>")
[void]$svg.AppendLine('</svg>')

[System.IO.File]::WriteAllText($SvgOut, $svg.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "wrote $SvgOut (overall $overall%)"

# ---------- self-contained HTML (same SVG inline + hover tooltips) ----------
$svgInline = $svg.ToString()
$html = @"
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>XGrid completion treemap</title>
<style>
  body { background:#0B1220; color:#E2E8F0; font-family:Segoe UI, system-ui, sans-serif; margin:0; padding:16px 0; }
  .wrap { max-width:1680px; margin:0 auto; position:relative; }
  svg { display:block; margin:0 auto; }
  .tooltip { position:fixed; pointer-events:none; background:#111827; color:#F8FAFC; border:1px solid #334155;
             border-radius:8px; padding:8px 12px; font-size:13px; line-height:1.45; opacity:0; transition:opacity .08s; max-width:340px; }
  .tooltip b { color:#FACC15; }
</style></head>
<body><div class="wrap">
$svgInline
<div class="tooltip" id="tt"></div>
</div>
<script>
const tt = document.getElementById('tt');
document.querySelectorAll('svg rect.tile').forEach(r => {
  r.style.transition = 'filter .1s';
  r.addEventListener('mousemove', e => {
    r.style.filter = 'brightness(1.14)';
    tt.innerHTML = 'Phase <b>' + r.dataset.p + ' - ' + r.dataset.name + '</b><br>' +
      r.dataset.done + '% complete &middot; weight ' + r.dataset.weight +
      ' of 108 weight-points';
    tt.style.left = Math.min(e.clientX + 14, innerWidth - 360) + 'px';
    tt.style.top = (e.clientY + 16) + 'px';
    tt.style.opacity = '1';
  });
  r.addEventListener('mouseleave', () => { r.style.filter = ''; tt.style.opacity = '0'; });
});
</script>
</body></html>
"@
Set-Content -Path $HtmlOut -Value $html -NoNewline -Encoding utf8
Write-Host "wrote $HtmlOut"
