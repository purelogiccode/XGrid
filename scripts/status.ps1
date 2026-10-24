# status.ps1 - one source of truth for the project's current status and worker sequence.
#
# docs/status.json holds the current task, position, phase scores, queue, done
# items and measured negatives. This script validates it, advances the worker
# sequence, and (with -Sync) regenerates the status blocks and the graph.
#
# Usage:
#   pwsh -File scripts/status.ps1 -Check              # validate (default)
#   pwsh -File scripts/status.ps1 -Show               # current task + queue
#   pwsh -File scripts/status.ps1 -Prompt             # ready-to-paste task command
#   pwsh -File scripts/status.ps1 -Next               # worker: refuse a dirty tree, claim/print the active task
#   pwsh -File scripts/status.ps1 -Complete -Evidence "<evidence>" -CommitMessage "<area>: <summary> (D1)"
#   pwsh -File scripts/status.ps1 -Negative -Evidence "<measurement>" -Reopen "<condition>"
#   pwsh -File scripts/status.ps1 -Negative -Task D4 -Evidence "<why>" -Reopen "<condition>"  # parked/queued task
#   pwsh -File scripts/status.ps1 -Sync               # validate + write blocks + graph
#   pwsh -File scripts/status.ps1 -Sync -NoGraph      # blocks only
param(
    [switch]$Check,
    [switch]$Sync,
    [switch]$Show,
    [switch]$Prompt,
    [switch]$Next,
    [switch]$Complete,
    [switch]$Negative,
    [string]$Task = "",
    [string]$Evidence = "",
    [string]$CommitMessage = "",
    [string]$Reopen = "only with a new measurement",
    [string]$DataPath = "",
    [switch]$NoSync,
    [switch]$NoGraph
)
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$root = Split-Path -Parent $PSScriptRoot
$statusPath = if ([string]::IsNullOrWhiteSpace($DataPath)) { Join-Path $root 'docs/status.json' } else { $DataPath }
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
function Save-Status {
    $script:status.updated = (Get-Date -Format 'yyyy-MM-dd')
    $script:status | ConvertTo-Json -Depth 10 | Set-Content -Path $script:statusPath -Encoding utf8
}

# ---------- sequence helpers ----------
function Get-ClosedIds {
    $closed = @{}
    foreach ($d in $script:status.done) { $closed[[string]$d.id] = $true }
    foreach ($n in $script:status.negatives) { $closed[[string]$n.id] = $true }
    return $closed
}
function Get-EligibleTask {
    $closed = Get-ClosedIds
    foreach ($q in $script:status.queue) {
        if ($q.status -ne 'queued') { continue }
        $ok = $true
        foreach ($dep in @($q.depends)) {
            if ($dep -and -not $closed.ContainsKey([string]$dep)) { $ok = $false; break }
        }
        if ($ok) { return $q }
    }
    return $null
}
function Set-CurrentFromQueue($q) {
    $q.status = 'active'
    $script:status.current = [ordered]@{
        milestone = [string]$script:status.current.milestone
        id = [string]$q.id
        title = [string]$q.title
        plan = [string]$q.plan
        status = 'active'
        started = [string]$script:status.updated
        attempts = 0
        acceptance = [string]$q.acceptance
        price = [string]$q.price
        bar = [string]$q.bar
    }
}
function Set-CurrentComplete {
    $script:status.current = [ordered]@{
        milestone = [string]$script:status.current.milestone
        id = 'NONE'
        title = 'No eligible queued task; add one to docs/status.json'
        plan = ''
        status = 'complete'
        started = [string]$script:status.updated
        attempts = 0
        acceptance = ''
        price = ''
    }
}
function Get-TaskPrompt($q, [int]$attempt) {
    $plan = [string]$q.plan
    $done = 'pwsh -File scripts/status.ps1 -Complete -Evidence "<numbers/anchors>" -CommitMessage "<area>: <summary> (' + $q.id + ')"'
    $neg = 'pwsh -File scripts/status.ps1 -Negative -Evidence "<measurement>" -Reopen "<condition>"'
    @(
        "Title: XGrid $($q.id) - $($q.title)",
        "Attempt: $attempt of $($script:status.rules.maxAttempts)",
        "Repository: CSharp_XboxEmulator",
        "",
        "Work only on the active task in docs/status.json ($($q.id)). Read ImplementationPlan.md -> Execution plan (current) -> $plan ($($q.id)) and follow its checkboxes in order.",
        "1. Start with ``pwsh -File scripts/status.ps1 -Check -Show``; if the check fails, stop and report. Do not start any other task or a docs refresh.",
        $(if ([string]$q.bar -eq 'correctness') {
            "2. This is a CORRECTNESS task: the performance bar (>=10% wall / >=20% engine) does NOT apply. The price is the session budget; the acceptance is behavior (gameplay, oracle parity, port fidelity). A negative is recorded only if the behavior cannot be reached, with the measurement that shows it."
        } else {
            "2. Price first: the first unchecked box is the price measurement. If the ceiling is <10% wall or <20% engine, run the negative close below and stop."
        }),
        $(if ([string]$q.bar -eq 'correctness') {
            "3. Follow the remaining checkboxes exactly: the acceptance differential first, the smallest implementation, the exact anchors, and the definition of done. The acceptance is behavior, not wall clock."
        } else {
            "3. If the ceiling clears the bar, follow the remaining checkboxes exactly: acceptance test first, smallest implementation, exact 1B anchors, 3 interleaved pairs, the definition of done."
        }),
        "4. Do not retry a measured negative (docs/status.json negatives); cite a new measurement or stop.",
        "5. Close with the matching command below. Commit code + tests + status + graph in one commit. Commit yourself so the message follows this protocol; if you leave changes uncommitted, the harness commits them as ``<title> (RepoFleet task #N)``.",
        "6. Finish with: task id, what changed, evidence (numbers/anchors), and any stop condition.",
        "",
        "Close (done): $done",
        "Close (negative): $neg",
        "",
        "Acceptance: $($q.acceptance)",
        "Price/ceiling: $($q.price)"
    )
}

# ---------- lifecycle (before validation: these mutate the status) ----------
$doSync = [bool]$Sync
$nextTask = $null
if ($Complete -or $Negative) {
    if ([string]::IsNullOrWhiteSpace($Evidence)) { throw "-Evidence is required for -Complete/-Negative" }
    if ($Complete -and -not [string]::IsNullOrWhiteSpace($Task)) {
        throw "-Complete closes the active task; -Task is only for -Negative (parked/queued tasks)"
    }
    $q = $null
    if (-not [string]::IsNullOrWhiteSpace($Task)) {
        $found = @($status.queue | Where-Object { $_.id -eq $Task })
        if ($found.Count -ne 1) { throw "task '$Task' is not in the queue" }
        $q = $found[0]
    } else {
        $active = @($status.queue | Where-Object { $_.status -eq 'active' })
        if ($active.Count -ne 1) { throw "expected exactly one active task, found $($active.Count)" }
        $q = $active[0]
    }
    $wasActive = $q.status -eq 'active'
    if ($Complete) {
        $message = if ([string]::IsNullOrWhiteSpace($CommitMessage)) { "this commit ($($q.id))" } else { $CommitMessage }
        $doneItem = [ordered]@{
            id = [string]$q.id
            title = [string]$q.title
            date = [string]$status.updated
            evidence = $Evidence
            commitMessage = $message
        }
        $status.done = @($status.done) + $doneItem
    } else {
        $negItem = [ordered]@{
            id = [string]$q.id
            title = [string]$q.title
            date = [string]$status.updated
            evidence = $Evidence
            reopen = $Reopen
        }
        $status.negatives = @($status.negatives) + $negItem
    }
    $status.queue = @($status.queue | Where-Object { $_.id -ne $q.id })
    $activeAfter = @($status.queue | Where-Object { $_.status -eq 'active' })
    if ($activeAfter.Count -gt 1) { throw "more than one active task after closing $($q.id)" }
    if ($activeAfter.Count -eq 1) {
        if ($status.current.id -ne $activeAfter[0].id) { Set-CurrentFromQueue $activeAfter[0] }
    } else {
        $promoted = Get-EligibleTask
        if ($promoted) { Set-CurrentFromQueue $promoted } else { Set-CurrentComplete }
    }
    Save-Status
    $doSync = $true
    Write-Host "Closed $($q.id) as $(if ($Complete) { 'done' } else { 'negative' }); current: $($status.current.id) - $($status.current.title)"
}
if ($Next) {
    if ([string]::IsNullOrWhiteSpace($DataPath)) {
        $dirty = @(& git status --porcelain 2>$null | Where-Object { $_ -and $_ -notmatch '\.repofleet-task\.md$' })
        if ($dirty.Count -gt 0) {
            Write-Host "STOP: working tree is dirty. The next task must start clean:"
            $dirty | ForEach-Object { Write-Host "  $_" }
            Write-Host "Commit or discard these changes, then rerun -Next."
            exit 1
        }
    }
    $active = @($status.queue | Where-Object { $_.status -eq 'active' })
    if ($active.Count -eq 0) {
        $promoted = Get-EligibleTask
        if ($promoted) {
            Set-CurrentFromQueue $promoted
            $active = @($promoted)
            $doSync = $true
        }
    }
    if ($active.Count -eq 0) {
        Write-Host "SEQUENCE COMPLETE: no eligible queued task. Add one to docs/status.json."
        exit 0
    }
    if ($active.Count -gt 1) { throw "more than one active task; run -Check" }
    $q = $active[0]
    if ($q.id -ne $status.current.id) { throw "current.id '$($status.current.id)' does not match the active queue item '$($q.id)'" }
    $q | Add-Member -NotePropertyName attempts -NotePropertyValue ([int]$q.attempts + 1) -Force
    Set-CurrentFromQueue $q
    $status.current['attempts'] = $q.attempts
    $status.current['started'] = $status.updated
    Save-Status
    if ($q.attempts -gt [int]$status.rules.maxAttempts) {
        Write-Host "STOP: $($q.id) has been attempted $($q.attempts - 1) times. Do not start another attempt."
        Write-Host "Run: pwsh -File scripts/status.ps1 -Negative -Evidence `"<what failed>`" -Reopen `"<condition>`""
        exit 0
    }
    $nextTask = $q
}

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
if ($phaseIds.Count -ne 25) { Fail "expected 25 phases, found $($phaseIds.Count)" }
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
$isComplete = $status.current.status -eq 'complete'
if ($isComplete) {
    if ($active.Count -ne 0) { Fail "current is complete but $($active.Count) queue item(s) are active" }
} else {
    if ($active.Count -ne 1) { Fail "exactly one active queue item is required, found $($active.Count)" }
    elseif ($active[0].id -ne $status.current.id) {
        Fail "current.id '$($status.current.id)' does not match the active queue item '$($active[0].id)'"
    }
}
foreach ($q in $status.queue) {
    if ([string]::IsNullOrWhiteSpace([string]$q.id)) { Fail 'queue item without id' }
    elseif ($queueIds.ContainsKey([string]$q.id)) { Fail "duplicate queue id $($q.id)" }
    else { $queueIds[[string]$q.id] = $true }
    if ([string]::IsNullOrWhiteSpace([string]$q.acceptance)) { Fail "queue $($q.id): no acceptance test" }
    if ([string]::IsNullOrWhiteSpace([string]$q.price)) { Fail "queue $($q.id): no price/ceiling" }
}
$closedIds = Get-ClosedIds
foreach ($q in $status.queue) {
    foreach ($dep in @($q.depends)) {
        if ($dep -and -not $queueIds.ContainsKey([string]$dep) -and -not $closedIds.ContainsKey([string]$dep)) {
            Fail "queue $($q.id): depends on missing $dep"
        }
    }
}
if (-not $isComplete) {
    if ([string]::IsNullOrWhiteSpace([string]$status.current.acceptance)) { Fail 'current.acceptance is empty' }
    if ([string]::IsNullOrWhiteSpace([string]$status.current.price)) { Fail 'current.price is empty' }
    if ([string]::IsNullOrWhiteSpace([string]$status.current.plan)) { Fail 'current.plan is empty' }
}
if (-not $isComplete -and $active.Count -eq 1) {
    $aq = $active[0]
    if ([string]$status.current.plan -ne [string]$aq.plan) { Fail "current.plan != queue $($aq.id).plan" }
    if ([string]$status.current.acceptance -ne [string]$aq.acceptance) { Fail "current.acceptance != queue $($aq.id).acceptance" }
    if ([string]$status.current.price -ne [string]$aq.price) { Fail "current.price != queue $($aq.id).price" }
}

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
    if ($queueIds.ContainsKey([string]$d.id)) { Fail "done $($d.id) is still in the queue" }
    if ([string]::IsNullOrWhiteSpace([string]$d.evidence)) { Fail "done $($d.id): no evidence" }
    $hasCommit = (@($d.commits).Count -gt 0) -or (-not [string]::IsNullOrWhiteSpace([string]$d.commitMessage))
    if (-not $hasCommit) { Fail "done $($d.id): no commits or commitMessage" }
}
foreach ($n in $status.negatives) {
    if ($queueIds.ContainsKey([string]$n.id)) { Fail "negative $($n.id) is still in the queue" }
    if ([string]::IsNullOrWhiteSpace([string]$n.evidence)) { Fail "negative $($n.id): no evidence" }
    if ([string]::IsNullOrWhiteSpace([string]$n.reopen)) { Fail "negative $($n.id): no reopen condition" }
    if ($planText -notmatch [regex]::Escape([string]$n.id)) { Fail "negative $($n.id): not named in the execution plan" }
}

# ---------- read-only prompt ----------
if ($Prompt) {
    if ($isComplete) {
        Write-Host "SEQUENCE COMPLETE: no eligible queued task. Add one to docs/status.json."
        exit 0
    }
    (Get-TaskPrompt $status.current ([int]$status.current.attempts + 1)) -join "`n"
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
if ($planPhases.Count -ne 25) { Fail "plan assessment table has $($planPhases.Count) phases, expected 25" }
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
    if ($isComplete) {
        return @(
            "**Current status ($($status.updated)):** SEQUENCE COMPLETE - no eligible queued task. Add one to docs/status.json.",
            "**Phase score:** $($status.result.percent)% ($($status.result.points) of $($status.result.weight)).",
            "**Do not retry without a new measurement:** see docs/status.json for all $($status.negatives.Count)."
        )
    }
    $next = @($status.queue |
        Where-Object { $_.status -eq 'queued' } |
        Select-Object -First 4 |
        ForEach-Object { "$($_.id) ($($_.title))" })
    $neg = @($status.negatives |
        Select-Object -First 6 |
        ForEach-Object { "$($_.id) $($_.title)" })
    $gp = $status.position.gameplay
    $gameplayLine = if ($null -ne $gp) {
        "**Gameplay ($($status.current.milestone)):** $($gp.target) - $($gp.state). Oracle: $($gp.oracle)."
    } else { $null }
    $positionPrefix = if ($null -ne $gp) { "Perf reference (M-D, deferred; $($status.position.host), $($steps), $($status.position.mode))" } else { "Position ($($status.position.host), $($steps), $($status.position.mode))" }
    @(
        "**Current status ($($status.updated)):** $($status.current.milestone) / **$($status.current.id) - $($status.current.title)** ($($status.current.status), attempt $($status.current.attempts)).",
        $gameplayLine,
        "**$($positionPrefix):** triangle gl+replace $($status.position.legs.triangle_gl_replace.wall) / sw $($status.position.legs.triangle_sw.wall); menu gl+replace $($status.position.legs.menu_gl_replace.wall) / sw $($status.position.legs.menu_sw.wall); gap ~$($status.position.gap.triangle_x)x / ~$($status.position.gap.menu_x)x.",
        "**Phase score:** $($status.result.percent)% ($($status.result.points) of $($status.result.weight)). **Next:** $($next -join ', ').",
        "**Do not retry without a new measurement:** $($neg -join ', '). See docs/status.json for all $($status.negatives.Count)."
    ) | Where-Object { $_ }
}

$pattern = '(?s)(<!-- STATUS:BEGIN[^>]*-->).*?(<!-- STATUS:END -->)'
foreach ($doc in $docs) {
    if (-not (Test-Path $doc)) { Fail "missing $doc"; continue }
    $text = [System.IO.File]::ReadAllText($doc)
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { Fail "$([System.IO.Path]::GetFileName($doc)): missing STATUS block markers"; continue }
    if ($doSync -and -not $NoSync) {
        $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $block = (Get-StatusBlock) -join $nl
        $block = $block.Replace('$', '$$')
        $replacement = '$1' + $nl + $block + $nl + '$2'
        $text = [regex]::Replace($text, $pattern, $replacement)
        [System.IO.File]::WriteAllText($doc, $text, [System.Text.UTF8Encoding]::new($false))
        $match = [regex]::Match($text, $pattern)
    }
    if ($match.Success -and -not $NoSync) {
        if ($isComplete) {
            if ($match.Value -notmatch 'SEQUENCE COMPLETE') { Fail "$([System.IO.Path]::GetFileName($doc)): status block is not complete" }
        } elseif ($match.Value -notmatch [regex]::Escape([string]$status.current.id)) {
            Fail "$([System.IO.Path]::GetFileName($doc)): status block does not name $($status.current.id)"
        }
    }
}

# ---------- completion graph ----------
if ($doSync -and -not $NoSync) {
    $trackNames = @($status.phases | ForEach-Object { [string]$_.track } | Select-Object -Unique)
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
    if ($isComplete) {
        Write-Host "Current: SEQUENCE COMPLETE - no eligible queued task"
    } else {
        Write-Host "Current: $($status.current.milestone) / $($status.current.id) - $($status.current.title) [$($status.current.status), attempt $($status.current.attempts)]"
    }
    Write-Host "Position: triangle $($status.position.legs.triangle_gl_replace.wall) gl+replace, menu $($status.position.legs.menu_gl_replace.wall); gap ~$($status.position.gap.triangle_x)x / ~$($status.position.gap.menu_x)x"
    if ($null -ne $status.position.gameplay) {
        Write-Host "Gameplay: $($status.position.gameplay.target) - $($status.position.gameplay.state)"
    }
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
if ($nextTask) {
    (Get-TaskPrompt $nextTask ([int]$nextTask.attempts)) -join "`n"
    exit 0
}
if ($isComplete) {
    Write-Host "status OK: SEQUENCE COMPLETE, $($status.result.percent)% ($($status.result.points) of $($status.result.weight)), $($status.negatives.Count) measured negatives"
} else {
    Write-Host "status OK: $($status.current.id) active, $($status.result.percent)% ($($status.result.points) of $($status.result.weight)), $($status.queue.Count) queue items, $($status.negatives.Count) measured negatives"
}
