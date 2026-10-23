# status.ps1 - one source of truth for the project's current status.
#
# docs/status.json holds the current task, position, phase scores, queue, done
# items and measured negatives. This script validates it and (with -Sync)
# regenerates the status blocks in the three canonical docs plus the completion
# graph data/artifacts. Run it at every session start and close.
#
# Usage:
#   pwsh -File scripts/status.ps1 -Check     # validate (default)
#   pwsh -File scripts/status.ps1 -Show      # current task + queue
#   pwsh -File scripts/status.ps1 -Prompt    # ready-to-paste RepoFleet task command
#   pwsh -File scripts/status.ps1 -Sync      # validate + write blocks + graph
#   pwsh -File scripts/status.ps1 -Sync -NoGraph   # blocks only
param(
    [switch]$Check,
    [switch]$Sync,
    [switch]$Show,
    [switch]$Prompt,
    [switch]$NoGraph
)
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$root = Split-Path -Parent $PSScriptRoot
$statusPath = Join-Path $root 'docs/status.json'
$graphPath = Join-Path $root 'tools/completion-graph/completion-data.json'
$planPath = Join-Path $root 'ImplementationPlan.md'
$docs = @(
    $planPath,
    (Join-Path $root 'AGENTS.md'),
    (Join-Path $root 'docs/development-direction.md')
)

if (-not (Test-Path $statusPath)) { throw "missing $statusPath" }
$status = Get-Content $statusPath -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { $script:failures.Add($message) }

# ---------- data checks ----------
$phaseIds = @{}
$totalWeight = 0.0
$donePoints = 0.0
foreach ($p in $status.phases) {
    if ($phaseIds.ContainsKey([int]$p.p)) { Fail "duplicate phase $($p.p)" } else { $phaseIds[[int]$p.p] = $true }
    if ([int]$p.weight -le 0) { Fail "phase $($p.p): weight must be > 0" }
    if ([int]$p.done -lt 0 -or [int]$p.done -gt 100) { Fail "phase $($p.p): done must be 0..100" }
    $totalWeight += [double]$p.weight
    $donePoints += [double]$p.weight * [double]$p.done / 100.0
}
if ($phaseIds.Count -ne 24) { Fail "expected 24 phases, found $($phaseIds.Count)" }
if ([Math]::Abs($totalWeight - [double]$status.result.weight) -gt 0.001) {
    Fail "phase weights sum to $totalWeight, result.weight is $($status.result.weight)"
}
$computedPoints = [Math]::Round($donePoints, 2)
$computedPercent = [Math]::Round(100.0 * $donePoints / $totalWeight, 1)
if ([Math]::Abs($computedPoints - [double]$status.result.points) -gt 0.01) {
    Fail "computed points $computedPoints, result.points is $($status.result.points)"
}
if ([Math]::Abs($computedPercent - [double]$status.result.percent) -gt 0.05) {
    Fail "computed percent $computedPercent, result.percent is $($status.result.percent)"
}

$queueIds = @{}
$active = @($status.queue | Where-Object { $_.status -eq 'active' })
if ($active.Count -ne 1) { Fail "exactly one active queue item is required, found $($active.Count)" }
foreach ($q in $status.queue) {
    if ([string]::IsNullOrWhiteSpace([string]$q.id)) { Fail 'queue item without id' }
    elseif ($queueIds.ContainsKey([string]$q.id)) { Fail "duplicate queue id $($q.id)" }
    else { $queueIds[[string]$q.id] = $true }
    if ([string]::IsNullOrWhiteSpace([string]$q.acceptance)) { Fail "queue $($q.id): no acceptance test" }
    if ([string]::IsNullOrWhiteSpace([string]$q.price)) { Fail "queue $($q.id): no price/ceiling" }
}
if ($active.Count -eq 1 -and $active[0].id -ne $status.current.id) {
    Fail "current.id '$($status.current.id)' does not match the active queue item '$($active[0].id)'"
}
foreach ($q in $status.queue) {
    foreach ($dep in @($q.depends)) {
        if ($dep -and -not $queueIds.ContainsKey([string]$dep)) { Fail "queue $($q.id): depends on missing $dep" }
    }
}
if ([string]::IsNullOrWhiteSpace([string]$status.current.acceptance)) { Fail 'current.acceptance is empty' }
if ([string]::IsNullOrWhiteSpace([string]$status.current.price)) { Fail 'current.price is empty' }

# Every queued task must be named in the detailed execution plan (E1/E2..E6).
$planText = [System.IO.File]::ReadAllText($planPath)
foreach ($q in $status.queue) {
    if ($planText -notmatch [regex]::Escape([string]$q.id)) {
        Fail "queue $($q.id): not named in the execution plan"
    }
    $planSection = [string]$q.plan
    if ([string]::IsNullOrWhiteSpace($planSection)) {
        Fail "queue $($q.id): no plan section (E2..E6)"
    } elseif ($planText -notmatch ('(?m)^## ' + [regex]::Escape($planSection) + '\.')) {
        Fail "queue $($q.id): plan section $planSection is missing"
    }
}

foreach ($d in $status.done) {
    if ([string]::IsNullOrWhiteSpace([string]$d.evidence)) { Fail "done $($d.id): no evidence" }
    if (@($d.commits).Count -eq 0) { Fail "done $($d.id): no commits" }
}
foreach ($n in $status.negatives) {
    if ([string]::IsNullOrWhiteSpace([string]$n.evidence)) { Fail "negative $($n.id): no evidence" }
    if ([string]::IsNullOrWhiteSpace([string]$n.reopen)) { Fail "negative $($n.id): no reopen condition" }
    if ($planText -notmatch [regex]::Escape([string]$n.id)) { Fail "negative $($n.id): not named in the execution plan" }
}

# ---------- RepoFleet task prompt ----------
if ($Prompt) {
    $t = $status.current
    $plan = [string]$t.plan
    if ([string]::IsNullOrWhiteSpace($plan)) { $plan = 'E2' }
    $lines = @(
        "Title: XGrid $($t.id) - $($t.title)",
        "Repository: CSharp_XboxEmulator",
        "",
        "Work only on the active task in docs/status.json ($($t.id)). Read ImplementationPlan.md -> Execution plan (current) -> $plan ($($t.id)) and follow its checkboxes in order.",
        "1. Start with ``pwsh -File scripts/status.ps1 -Check -Show``; if the check fails, stop and report. Do not start D2/D3/D5/P1/R1 or a docs refresh.",
        "2. Price first: the first unchecked box is the price measurement. If the ceiling is <10% wall or <20% engine, record a new negative in docs/status.json, run ``pwsh -File scripts/status.ps1 -Check -Sync``, commit only the status change, and stop.",
        "3. If the ceiling clears the bar, follow the remaining checkboxes exactly: acceptance test first, smallest implementation, exact 1B anchors, 3 interleaved pairs, the definition of done.",
        "4. Do not retry a measured negative (docs/status.json negatives); cite a new measurement or stop.",
        "5. Close with ``pwsh -File scripts/status.ps1 -Check -Sync``. Commit code + tests + status + graph in one commit: ``<area>: <summary> ($($t.id))``. Commit yourself so the message follows this protocol; if you leave changes uncommitted, the harness commits them as ``<title> (RepoFleet task #N)``.",
        "6. Finish with: task id, what changed, evidence (numbers/anchors), and any stop condition.",
        "",
        "Acceptance: $($t.acceptance)",
        "Price/ceiling: $($t.price)"
    )
    $lines -join "`n"
    exit 0
}

# ---------- plan table ----------
$planPhases = @{}
$inTable = $false
foreach ($line in [System.IO.File]::ReadAllLines($planPath)) {
    if ($line -match '^\| Phase \| Weight \| Done \| What remains \|') { $inTable = $true; continue }
    if (-not $inTable) { continue }
    if ($line -notmatch '^\|') { break }
    if ($line -match '^\| (\d+) .*? \| (\d+) \| (\d+)% \|') {
        $planPhases[[int]$Matches[1]] = @([int]$Matches[2], [int]$Matches[3])
    }
}
if ($planPhases.Count -ne 24) { Fail "plan assessment table has $($planPhases.Count) phases, expected 24" }
foreach ($p in $status.phases) {
    $pl = $planPhases[[int]$p.p]
    if ($null -eq $pl) { Fail "phase $($p.p): missing from the plan assessment table" }
    elseif ($pl[0] -ne [int]$p.weight -or $pl[1] -ne [int]$p.done) {
        Fail "phase $($p.p): plan(w=$($pl[0]), d=$($pl[1])) != status(w=$($p.weight), d=$($p.done))"
    }
}

# ---------- status block ----------
function Get-StatusBlock {
    $steps = if ([long]$status.position.steps -eq 1000000000) { '1B' } else { "$($status.position.steps) steps" }
    $next = @($status.queue |
        Where-Object { $_.status -eq 'queued' } |
        Select-Object -First 4 |
        ForEach-Object { "$($_.id) ($($_.title))" })
    $neg = @($status.negatives |
        Select-Object -First 6 |
        ForEach-Object { "$($_.id) $($_.title)" })
    $lines = @(
        "**Current status ($($status.updated)):** $($status.current.milestone) / **$($status.current.id) - $($status.current.title)** ($($status.current.status)).",
        "**Position ($($status.position.host), $($steps), $($status.position.mode)):** triangle gl+replace $($status.position.legs.triangle_gl_replace.wall) / sw $($status.position.legs.triangle_sw.wall); menu gl+replace $($status.position.legs.menu_gl_replace.wall) / sw $($status.position.legs.menu_sw.wall); gap ~$($status.position.gap.triangle_x)x / ~$($status.position.gap.menu_x)x.",
        "**Phase score:** $($status.result.percent)% ($($status.result.points) of $($status.result.weight)). **Next:** $($next -join ', ').",
        "**Do not retry without a new measurement:** $($neg -join ', '). See docs/status.json for all $($status.negatives.Count)."
    )
    return $lines
}

$pattern = '(?s)(<!-- STATUS:BEGIN[^>]*-->).*?(<!-- STATUS:END -->)'
foreach ($doc in $docs) {
    if (-not (Test-Path $doc)) { Fail "missing $doc"; continue }
    $text = [System.IO.File]::ReadAllText($doc)
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { Fail "$([System.IO.Path]::GetFileName($doc)): missing STATUS block markers"; continue }
    if ($Sync) {
        $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $block = (Get-StatusBlock) -join $nl
        $block = $block.Replace('$', '$$')
        $replacement = '$1' + $nl + $block + $nl + '$2'
        $text = [regex]::Replace($text, $pattern, $replacement)
        [System.IO.File]::WriteAllText($doc, $text, [System.Text.UTF8Encoding]::new($false))
        $match = [regex]::Match($text, $pattern)
    }
    if ($match.Success -and $match.Value -notmatch [regex]::Escape([string]$status.current.id)) {
        Fail "$([System.IO.Path]::GetFileName($doc)): status block does not name $($status.current.id)"
    }
}

# ---------- completion graph ----------
if ($Sync) {
    $trackNames = @('LLE core (real console chain)', 'Performance & finish')
    $tracks = @()
    foreach ($trackName in $trackNames) {
        $phases = @($status.phases |
            Where-Object { $_.track -eq $trackName } |
            ForEach-Object {
                [ordered]@{ p = [int]$_.p; name = [string]$_.name; weight = [int]$_.weight; done = [int]$_.done }
            })
        $tracks += [ordered]@{ name = $trackName; phases = $phases }
    }
    $graph = [ordered]@{
        generated = [string]$status.updated
        source = "Generated from docs/status.json by scripts/status.ps1. The phase weights/done are the single source; ImplementationPlan.md's assessment table is checked against them."
        note = 'Area = phase weight (remaining-effort share). Color = percent done.'
        tracks = $tracks
    }
    $graph | ConvertTo-Json -Depth 8 | Set-Content -Path $graphPath -Encoding utf8
    if (-not $NoGraph) {
        $generator = Join-Path $root 'tools/completion-graph/generate.ps1'
        & $generator -Png
        if (-not $?) { Fail 'completion graph generation failed' }
    }
} else {
    if (-not (Test-Path $graphPath)) { Fail 'missing tools/completion-graph/completion-data.json' }
    else {
        $graph = Get-Content $graphPath -Raw | ConvertFrom-Json
        $graphPhases = @{}
        foreach ($t in $graph.tracks) { foreach ($p in $t.phases) { $graphPhases[[int]$p.p] = $p } }
        foreach ($p in $status.phases) {
            $g = $graphPhases[[int]$p.p]
            if ($null -eq $g) { Fail "phase $($p.p): missing from the completion graph data" }
            elseif ([int]$g.weight -ne [int]$p.weight -or [int]$g.done -ne [int]$p.done) {
                Fail "phase $($p.p): graph(w=$($g.weight), d=$($g.done)) != status(w=$($p.weight), d=$($p.done)); run -Sync"
            }
        }
    }
}

# ---------- show ----------
if ($Show -or $failures.Count -gt 0) {
    Write-Host "Current: $($status.current.milestone) / $($status.current.id) - $($status.current.title) [$($status.current.status)]"
    Write-Host "Position: triangle $($status.position.legs.triangle_gl_replace.wall) gl+replace, menu $($status.position.legs.menu_gl_replace.wall); gap ~$($status.position.gap.triangle_x)x / ~$($status.position.gap.menu_x)x"
    Write-Host "Score: $($status.result.percent)% ($($status.result.points) of $($status.result.weight))"
    Write-Host 'Queue:'
    foreach ($q in $status.queue) {
        $dep = if (@($q.depends).Count -gt 0 -and @($q.depends)[0]) { " (depends $($q.depends -join ', '))" } else { '' }
        Write-Host "  $($q.id) [$($q.status)] $($q.title)$dep"
    }
    Write-Host "Negatives: $($status.negatives.Count) (do not retry without a new measurement)"
}

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "FAIL: $f" }
    exit 1
}
Write-Host "status OK: $($status.current.id) active, $($status.result.percent)% ($($status.result.points) of $($status.result.weight)), $($status.queue.Count) queue items, $($status.negatives.Count) measured negatives"
