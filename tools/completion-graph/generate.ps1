# generate.ps1 - renders completion-data.json as SVG treemaps (+ self-contained HTML).
# Canonical output: the solid style. Only completion-treemap.* is committed;
# fill/squares remain available on demand and must not be re-added to the repo.
#   solid : tile is fully colored, color = percent done (red 0% -> yellow 50% -> green 100%)
#   fill  : tile is a dark "slot"; the bottom `done%` of the tile is filled with the
#           same heat color (100% = fully filled, 50% = half filled, 0% = empty)
# Area = phase weight (share of remaining effort).
# Usage: pwsh -File tools/completion-graph/generate.ps1
param(
    [string]$DataPath = (Join-Path $PSScriptRoot 'completion-data.json'),
    [string]$OutDir = $PSScriptRoot,
    [ValidateSet('solid', 'fill', 'squares', 'all')]
    [string]$Style = 'solid',
    [switch]$Png
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

function ConvertTo-XmlText([string]$s) {
    return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

# estimate wrapped lines (px per char is ~0.58*fontsize for bold Segoe)
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

function Invoke-Squarify {
    param([object[]]$Items, [double]$X, [double]$Y, [double]$W, [double]$H)
    $rects = New-Object System.Collections.Generic.List[object]
    if ($Items.Count -eq 0) { return $rects }
    $remaining = [System.Collections.ArrayList]::new()
    foreach ($it in $Items) { [void]$remaining.Add($it) }
    $cx = $X; $cy = $Y; $cw = $W; $ch = $H

    while ($remaining.Count -gt 0) {
        $short = [Math]::Min($cw, $ch)
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

# ---------- layout (once; both styles share it) ----------
$totalWeight = 0.0
foreach ($track in $data.tracks) { foreach ($p in $track.phases) { $totalWeight += $p.weight } }
$trackScale = ($contentW * $contentH) / $totalWeight
$trackItems = @($data.tracks | ForEach-Object { [pscustomobject]@{ Area = ([double]($_.phases | Measure-Object -Property weight -Sum).Sum) * $trackScale; track = $_ } })
$trackRects = Invoke-Squarify -Items $trackItems -X $contentX -Y $contentY -W $contentW -H $contentH

$phaseRects = New-Object System.Collections.Generic.List[object]
$trackLabels = New-Object System.Collections.Generic.List[object]

foreach ($tr in $trackRects) {
    $track = $tr.item.track
    $band = 30.0
    $hasBand = (($tr.w * $tr.h) -gt 30000)
    if ($hasBand) {
        $trackLabels.Add([pscustomobject]@{ text = $track.name; x = $tr.x + 8; y = $tr.y + 20 })
        $px = $tr.x + 6; $py = $tr.y + $band + 4; $pw = $tr.w - 12; $ph_ = $tr.h - $band - 8
    } else {
        $px = $tr.x + 3; $py = $tr.y + 3; $pw = $tr.w - 6; $ph_ = $tr.h - 6
    }
    if ($pw -le 8 -or $ph_ -le 8) { continue }

    $tw = [double](($track.phases | Measure-Object -Property weight -Sum).Sum)
    $phaseScale = ($pw * $ph_) / $tw
    $phaseItems = @($track.phases | Sort-Object -Property weight -Descending | ForEach-Object { [pscustomobject]@{ Area = [double]$_.weight * $phaseScale; phase = $_ } })
    foreach ($pr in (Invoke-Squarify -Items $phaseItems -X $px -Y $py -W $pw -H $ph_)) {
        $phaseRects.Add([pscustomobject]@{ phase = $pr.item.phase; x = $pr.x; y = $pr.y; w = $pr.w; h = $pr.h })
    }
}

# weighted overall
$doneAcc = 0.0
foreach ($track in $data.tracks) { foreach ($p in $track.phases) { $doneAcc += $p.weight * ($p.done / 100.0) } }
$overall = [Math]::Round(100.0 * $doneAcc / $totalWeight, 1)

# ---------- renderers ----------
function Get-TextAttrs([string]$style, [double]$done, [double]$fontSize) {
    if ($style -eq 'squares') {
        # plain light text; it sits on a dark label plate over the mosaic
        return "fill='#F1F5F9'"
    }
    if ($style -eq 'fill') {
        # light text with a dark outline reads on both the dark slot and the bright fill
        return "fill='#F8FAFC' stroke='#0B1220' stroke-width='2.4' paint-order='stroke'"
    }
    $fg = if (Test-Light $done) { '#111827' } else { '#FFFFFF' }
    return "fill='$fg'"
}

function Build-Svg([string]$style) {
    $svg = [System.Text.StringBuilder]::new()
    $fillNote = switch ($style) {
        'fill' { "Fill height = percent done (bottom-up); fill color = same heat." }
        'squares' { "Each tile holds 100 unit squares; filled count = percent done (bottom-up)." }
        default { "Tile color = percent done." }
    }
    [void]$svg.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' width='$W' height='$H' viewBox='0 0 $W $H' font-family='Segoe UI, Segoe UI Emoji, sans-serif'>")
    [void]$svg.AppendLine("<rect width='$W' height='$H' fill='#0B1220'/>")
    [void]$svg.AppendLine("<text x='$M' y='34' font-size='25' font-weight='700' fill='#F8FAFC'>XGrid - code completion by phase</text>")
    [void]$svg.AppendLine("<text x='$M' y='55' font-size='13.5' fill='#94A3B8'>Tile size = phase weight (share of remaining effort). $fillNote Refreshed $($data.generated) - ImplementationPlan assessment ($([Math]::Round($totalWeight,0)) weight pts).</text>")

    foreach ($lbl in $trackLabels) {
        [void]$svg.AppendLine("<text x='$($lbl.x.ToString('0.#'))' y='$($lbl.y.ToString('0.#'))' font-size='14' font-weight='700' fill='#CBD5E1'>$(ConvertTo-XmlText $lbl.text)</text>")
    }

    foreach ($r in $phaseRects) {
        $ph = $r.phase
        $color = Get-Color $ph.done
        $t = $ph.done / 100.0
        $rr = [Math]::Min(10, [Math]::Min($r.w, $r.h) / 4)
        $rectX = $r.x.ToString('0.#'); $rectY = $r.y.ToString('0.#'); $rectW = $r.w.ToString('0.#'); $rectH = $r.h.ToString('0.#'); $rectR = $rr.ToString('0.#')
        $attrs = "data-p='$($ph.p)' data-name='$(ConvertTo-XmlText $ph.name)' data-done='$($ph.done)' data-weight='$($ph.weight)'"
        if ($style -eq 'squares') { $attrs += " data-squares='1'" }

        [void]$svg.AppendLine("<g class='tile' $attrs>")
        if ($style -eq 'squares') {
            # the tile is a slot holding a 10x10 grid of unit squares; the bottom
            # `done` squares are filled (row-major, bottom-up)
            [void]$svg.AppendLine("<rect x='$rectX' y='$rectY' width='$rectW' height='$rectH' rx='$rectR' fill='#141E33' stroke='#0B1220' stroke-width='1.5'/>")
            $padG = 7.0
            $cell = [Math]::Min(($r.w - 2 * $padG - 18) / 10, ($r.h - 2 * $padG - 18) / 10)
            if ($cell -ge 4.0) {
                $gap = [Math]::Min(2.0, [Math]::Max(1.0, $cell * 0.15))
                $cell = [Math]::Min(($r.w - 2 * $padG - 9 * $gap) / 10, ($r.h - 2 * $padG - 9 * $gap) / 10)
                $gridW = 10 * $cell + 9 * $gap
                $gx = $r.x + ($r.w - $gridW) / 2
                $gy = $r.y + ($r.h - $gridW) / 2
                $cellR = ($cell * 0.18).ToString('0.#')
                for ($i = 0; $i -lt 100; $i++) {
                    $row = 9 - [int][Math]::Floor($i / 10)
                    $col = $i % 10
                    $cellX = ($gx + $col * ($cell + $gap)).ToString('0.#')
                    $cellY = ($gy + $row * ($cell + $gap)).ToString('0.#')
                    $cellFill = if ($i -lt $ph.done) { '#1EC85A' } else { '#1F2C47' }
                    [void]$svg.AppendLine("<rect x='$cellX' y='$cellY' width='$($cell.ToString('0.#'))' height='$($cell.ToString('0.#'))' rx='$cellR' fill='$cellFill'/>")
                }
            }
        } elseif ($style -eq 'fill') {
            $clipId = "clip$($ph.p)"
            [void]$svg.AppendLine("<defs><clipPath id='$clipId'><rect x='$rectX' y='$rectY' width='$rectW' height='$rectH' rx='$rectR'/></clipPath></defs>")
            [void]$svg.AppendLine("<rect x='$rectX' y='$rectY' width='$rectW' height='$rectH' rx='$rectR' fill='#1A2740' stroke='#0B1220' stroke-width='1.5'/>")
            $fillH = $r.h * $t
            if ($fillH -gt 0.5) {
                $fillY = ($r.y + $r.h - $fillH).ToString('0.#')
                [void]$svg.AppendLine("<g clip-path='url(#$clipId)'><rect x='$rectX' y='$fillY' width='$rectW' height='$($fillH.ToString('0.#'))' fill='$color'/></g>")
            }
        } else {
            [void]$svg.AppendLine("<rect x='$rectX' y='$rectY' width='$rectW' height='$rectH' rx='$rectR' fill='$color' stroke='#0B1220' stroke-width='1.5'/>")
        }

        $area = $r.w * $r.h
        if ($area -lt 1800) {
            [void]$svg.AppendLine('</g>')
            continue
        }

        $pad = 9.0
        $availW = [Math]::Max(20, $r.w - 2 * $pad)
        # pick font size: prefer comfortable, shrink to fit
        $nameFs = 0.0; $lines = $null
        foreach ($cand in @(24, 21, 18, 16, 14, 12, 11)) {
            $ls = Get-WrappedLines $ph.name $cand $availW
            $needed = $ls.Count * ($cand * 1.18) + $cand * 1.0 + 8
            if (($needed -le $r.h - 2 * $pad) -and ($ls.Count -le 3)) { $nameFs = $cand; $lines = $ls; break }
        }
        if ($nameFs -eq 0) {
            foreach ($cand in @(14, 12, 11, 10)) {
                if (($cand * 1.3 + 14) -le ($r.h - 2 * $pad) -and $availW -gt ($cand * 3)) { $nameFs = $cand; break }
            }
            if ($nameFs -eq 0) {
                [void]$svg.AppendLine('</g>')
                continue
            }
            $cpl = [int]($availW / (0.58 * $nameFs))
            if ($cpl -lt 3) {
                [void]$svg.AppendLine('</g>')
                continue
            }
            $short = $ph.name
            if ($short.Length -gt $cpl) { $short = $short.Substring(0, [Math]::Max(1, $cpl - 1)) + [char]0x2026 }
            $lines = @($short)
        }

        $chipSize = [Math]::Min(30.0, [Math]::Max(16.0, $nameFs * 1.1))
        $chipY = $r.y + $pad
        $chipX = $r.x + $pad
        $textX = $r.x + $pad
        $textY = $chipY + $chipSize + 6 + $nameFs

        # percent + weight under the name, when there is room
        $subFs = [Math]::Max(11.0, $nameFs * 0.72)
        $subY = $textY + $lines.Count * $nameFs * 1.18 + $subFs * 0.6 + 2
        if ($style -eq 'squares') {
            $subPlain = "$($ph.done)/100 - weight $($ph.weight)"
        } else {
            $subPlain = "$($ph.done)% done - weight $($ph.weight)"
        }
        $subEst = $subPlain.Length * 0.5 * $subFs
        $subShown = (($subY - $r.y) -le ($r.h - 6)) -and ($subEst -le $availW)
        $textEnd = if ($subShown) { $subY + 2 } else { $textY + ($lines.Count - 1) * $nameFs * 1.18 + 2 }

        if ($style -eq 'squares') {
            # dark label plate so text stays readable over the mosaic
            $plateX = ($r.x + 6).ToString('0.#'); $plateY = ($r.y + 6).ToString('0.#')
            $plateW = ($r.w - 12).ToString('0.#')
            $plateH = ([Math]::Min($r.h - 12, $textEnd - $r.y - 6 + 6)).ToString('0.#')
            [void]$svg.AppendLine("<rect x='$plateX' y='$plateY' width='$plateW' height='$plateH' rx='8' fill='rgba(11,18,32,0.72)'/>")
        }

        $chipColor = if ($style -eq 'squares') { 'rgba(255,255,255,0.12)' } elseif ($style -eq 'fill') { 'rgba(11,18,32,0.45)' } elseif (Test-Light $ph.done) { 'rgba(17,24,39,0.16)' } else { 'rgba(255,255,255,0.20)' }
        $chipR = ($chipSize / 4).ToString('0.#')
        [void]$svg.AppendLine("<rect x='$($chipX.ToString('0.#'))' y='$($chipY.ToString('0.#'))' width='$($chipSize.ToString('0.#'))' height='$($chipSize.ToString('0.#'))' rx='$chipR' fill='$chipColor'/>")
        $chipTx = ($chipX + $chipSize / 2).ToString('0.#'); $chipTy = ($chipY + $chipSize * 0.68).ToString('0.#'); $chipFs = ($chipSize * 0.52).ToString('0.#')
        [void]$svg.AppendLine("<text x='$chipTx' y='$chipTy' font-size='$chipFs' font-weight='700' $(Get-TextAttrs $style $ph.done 0) text-anchor='middle'>P$($ph.p)</text>")

        $textAttrs = Get-TextAttrs $style $ph.done $nameFs
        $lineY = $textY
        foreach ($line in $lines) {
            [void]$svg.AppendLine("<text x='$($textX.ToString('0.#'))' y='$($lineY.ToString('0.#'))' font-size='$($nameFs.ToString('0.#'))' font-weight='700' $textAttrs>$(ConvertTo-XmlText $line)</text>")
            $lineY += $nameFs * 1.18
        }
        if ($subShown) {
            $subText = $subPlain.Replace(' - ', ' &#183; ')
            $subAttrs = Get-TextAttrs $style $ph.done $subFs
            [void]$svg.AppendLine("<text x='$($textX.ToString('0.#'))' y='$($subY.ToString('0.#'))' font-size='$($subFs.ToString('0.#'))' opacity='0.94' $subAttrs>$subText</text>")
        }

        [void]$svg.AppendLine('</g>')
    }

    # ---------- legend ----------
    $legendY = $H - $M - 30
    $lx = $M + 4
    if ($style -eq 'fill') {
        [void]$svg.AppendLine("<text x='$lx' y='$($legendY - 8)' font-size='12.5' fill='#94A3B8'>fill = percent done</text>")
        $box = 34.0
        $bx = $lx
        foreach ($pct in @(25, 50, 100)) {
            $color = Get-Color $pct
            $t = $pct / 100.0
            $by = $legendY
            [void]$svg.AppendLine("<rect x='$($bx.ToString('0.#'))' y='$($by.ToString('0.#'))' width='$box' height='$box' rx='6' fill='#1A2740' stroke='#334155'/>")
            $fh = $box * $t
            [void]$svg.AppendLine("<rect x='$($bx.ToString('0.#'))' y='$(($by + $box - $fh).ToString('0.#'))' width='$box' height='$($fh.ToString('0.#'))' rx='6' fill='$color'/>")
            [void]$svg.AppendLine("<text x='$(($bx + $box + 8).ToString('0.#'))' y='$(($by + $box * 0.68).ToString('0.#'))' font-size='13' fill='#CBD5E1'>$pct%</text>")
            $bx += $box + 66
        }
    } elseif ($style -eq 'squares') {
        [void]$svg.AppendLine("<text x='$lx' y='$($legendY - 8)' font-size='12.5' fill='#94A3B8'>filled squares / 100 = percent done</text>")
        $bx = $lx
        $cell = 3.0; $gap = 1.4
        $gw = 10 * $cell + 9 * $gap
        foreach ($pct in @(25, 50, 100)) {
            for ($i = 0; $i -lt 100; $i++) {
                if ($i -ge $pct) { continue }
                $row = 9 - [int][Math]::Floor($i / 10)
                $col = $i % 10
                $cellX = ($bx + $col * ($cell + $gap)).ToString('0.#')
                $cellY = ($legendY + $row * ($cell + $gap)).ToString('0.#')
                [void]$svg.AppendLine("<rect x='$cellX' y='$cellY' width='3' height='3' fill='#1EC85A'/>")
            }
            [void]$svg.AppendLine("<rect x='$(($bx - 2).ToString('0.#'))' y='$(($legendY - 2).ToString('0.#'))' width='$(($gw + 4).ToString('0.#'))' height='$(($gw + 4).ToString('0.#'))' rx='5' fill='none' stroke='#334155'/>")
            [void]$svg.AppendLine("<text x='$(($bx + $gw + 9).ToString('0.#'))' y='$(($legendY + $gw * 0.62).ToString('0.#'))' font-size='13' fill='#CBD5E1'>$pct/100</text>")
            $bx += $gw + 64
        }
    } else {
        [void]$svg.AppendLine("<text x='$lx' y='$($legendY - 8)' font-size='12.5' fill='#94A3B8'>completion</text>")
        for ($i = 0; $i -lt 100; $i++) {
            $color = Get-Color ($i + 0.5)
            [void]$svg.AppendLine("<rect x='{0:0.#}' y='$legendY' width='4.2' height='13' fill='$color'/>" -f ($lx + $i * 4.2))
        }
        [void]$svg.AppendLine("<text x='$($lx + 2)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>0%</text>")
        [void]$svg.AppendLine("<text x='$([int]($lx + 210))' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>50%</text>")
        [void]$svg.AppendLine("<text x='$([int]($lx + 420) - 8)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8'>100%</text>")
    }
    # overall readout: proportional arc + a legible right-aligned value
    $ringR = 9.0
    $ringCx = $W - $M - 80
    $ringCy = $legendY + 10
    $ringC = 2.0 * [Math]::PI * $ringR
    $ringDash = $ringC * ($overall / 100.0)
    $ringGap = [Math]::Max(0.0, $ringC - $ringDash)
    [void]$svg.AppendLine("<circle cx='$ringCx' cy='$ringCy' r='$ringR' fill='none' stroke='#F8FAFC' stroke-width='3' stroke-dasharray='$($ringDash.ToString('0.#')) $($ringGap.ToString('0.#'))' transform='rotate(-90 $ringCx $ringCy)'/>")
    [void]$svg.AppendLine("<text x='$($W - $M)' y='$($legendY + 15)' font-size='17' font-weight='700' fill='#F8FAFC' text-anchor='end'>$overall%</text>")
    [void]$svg.AppendLine("<text x='$($W - $M)' y='$($legendY + 27)' font-size='11.5' fill='#94A3B8' text-anchor='end'>weighted overall</text>")
    [void]$svg.AppendLine('</svg>')
    return $svg.ToString()
}

function Build-Html([string]$svgText, [string]$title) {
    return @"
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>$title</title>
<style>
  body { background:#0B1220; color:#E2E8F0; font-family:Segoe UI, system-ui, sans-serif; margin:0; padding:16px 0; }
  .wrap { max-width:1680px; margin:0 auto; position:relative; }
  svg { display:block; margin:0 auto; }
  .tooltip { position:fixed; pointer-events:none; background:#111827; color:#F8FAFC; border:1px solid #334155;
             border-radius:8px; padding:8px 12px; font-size:13px; line-height:1.45; opacity:0; transition:opacity .08s; max-width:340px; }
  .tooltip b { color:#FACC15; }
</style></head>
<body><div class="wrap">
$svgText
<div class="tooltip" id="tt"></div>
</div>
<script>
const tt = document.getElementById('tt');
document.querySelectorAll('svg g.tile').forEach(g => {
  g.style.transition = 'filter .1s';
  g.addEventListener('mousemove', e => {
    g.style.filter = 'brightness(1.14)';
    tt.innerHTML = 'Phase <b>' + g.dataset.p + ' - ' + g.dataset.name + '</b><br>' +
      g.dataset.done + '% complete' + (g.dataset.squares ? ' &middot; ' + g.dataset.done + '/100 squares' : '') +
      ' &middot; weight ' + g.dataset.weight + ' of 108 weight-points';
    tt.style.left = Math.min(e.clientX + 14, innerWidth - 360) + 'px';
    tt.style.top = (e.clientY + 16) + 'px';
    tt.style.opacity = '1';
  });
  g.addEventListener('mouseleave', () => { g.style.filter = ''; tt.style.opacity = '0'; });
});
</script>
</body></html>
"@
}

# ---------- write styles (solid is the repo default) ----------
$utf8 = [System.Text.UTF8Encoding]::new($false)
$styles = if ($Style -eq 'all') { @('solid', 'fill', 'squares') } else { @($Style) }

$edge = $null
if ($Png) {
    foreach ($cand in @(
            "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "C:\Program Files\Microsoft\Edge\Application\msedge.exe")) {
        if (Test-Path $cand) { $edge = $cand; break }
    }
    if ($null -eq $edge) { Write-Warning 'Edge not found; skipping PNG refresh' }
}

foreach ($style in $styles) {
    $suffix = switch ($style) { 'solid' { '' } 'fill' { '-fill' } 'squares' { '-squares' } }
    $svgPath = Join-Path $OutDir "completion-treemap$suffix.svg"
    $htmlPath = Join-Path $OutDir "completion-treemap$suffix.html"
    $svg = Build-Svg $style
    [System.IO.File]::WriteAllText($svgPath, $svg, $utf8)
    [System.IO.File]::WriteAllText($htmlPath, (Build-Html $svg "XGrid completion treemap ($style)"), $utf8)
    Write-Host "wrote $svgPath + $htmlPath"

    if ($null -ne $edge) {
        $pngPath = Join-Path $OutDir "completion-treemap$suffix.png"
        $uri = "file:///" + ($svgPath -replace '\\', '/')
        Start-Process -FilePath $edge -Wait -NoNewWindow -ArgumentList @(
            '--headless=new', '--disable-gpu', "--screenshot=$pngPath", '--window-size=1680,1080',
            '--hide-scrollbars', $uri) | Out-Null
        Write-Host "wrote $pngPath"
    }
}
Write-Host "overall $overall% across $($phaseRects.Count) phases"
