# Audit an upstream xemu release before a selective port.
#
# The script compares two upstream commits and classifies every changed file
# against the files XGrid has modified (the xemu-patches record) and the paths
# declared in scripts/xemu-owned-paths.txt. It never edits xemu/ or References2.
#
# The first run clones xemu into wt-data/xemu-upstream (git-ignored). Later runs
# reuse that clone; pass -RefreshUpstream to fetch new tags and branches.
param(
    [string]$From = '',
    [Parameter(Mandatory = $true)]
    [string]$To,
    [string]$UpstreamRepo = '',
    [string]$UpstreamUrl = 'https://github.com/xemu-project/xemu.git',
    [string]$Cache = 'wt-data/xemu-upstream',
    [string]$OwnedPaths = 'scripts/xemu-owned-paths.txt',
    [string]$Report = '',
    [switch]$RefreshUpstream,
    [switch]$All
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$xemu = Join-Path $root 'xemu'
$patchDirectory = Join-Path $xemu 'xemu-patches'
$versionPath = Join-Path $xemu 'XEMU_VERSION'
$commitPath = Join-Path $xemu 'XEMU_COMMIT'
$reviewedPath = Join-Path $xemu 'XEMU_REVIEWED_COMMIT'

function Invoke-Git {
    param(
        [string]$Repository,
        [string[]]$GitArguments,
        [switch]$AllowFailure
    )

    $output = & git -C $Repository @GitArguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($GitArguments -join ' ') failed in ${Repository} (exit $exitCode):`n$($output -join "`n")"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = @($output)
    }
}

function Resolve-UpstreamCommit {
    param(
        [string]$Repository,
        [string]$Reference
    )

    $result = Invoke-Git -Repository $Repository `
        -GitArguments @('rev-parse', '--verify', '--quiet', "$Reference^{commit}") `
        -AllowFailure
    if ($result.ExitCode -ne 0 -or $result.Output.Count -eq 0) {
        return $null
    }

    return ($result.Output | Select-Object -First 1).Trim()
}

function Test-OwnedPath {
    param(
        [string]$Path,
        [string[]]$Owned
    )

    $normalized = $Path.Replace('\', '/')
    foreach ($entry in $Owned) {
        if ($entry.EndsWith('/')) {
            if ($normalized.StartsWith($entry, [StringComparison]::Ordinal)) {
                return $true
            }
        }
        elseif ($normalized -eq $entry) {
            return $true
        }
    }

    return $false
}

function Get-PathArea {
    param([string]$Path)

    $parts = $Path -split '/'
    if ($parts.Count -ge 2) {
        return "$($parts[0])/$($parts[1])"
    }

    return $parts[0]
}

function Format-Change {
    param($Change)

    $display = $Change.Path
    if ($Change.OldPath) {
        $display = "$($Change.OldPath) -> $display"
    }

    return "$($Change.Status)`t$display"
}

if ([string]::IsNullOrWhiteSpace($From)) {
    if (Test-Path $reviewedPath) {
        $From = (Get-Content $reviewedPath -TotalCount 1).Trim()
    }
    elseif (Test-Path $commitPath) {
        $From = (Get-Content $commitPath -TotalCount 1).Trim()
    }
    else {
        throw "missing $reviewedPath and $commitPath; pass -From explicitly"
    }
}

$currentVersion = if (Test-Path $versionPath) {
    (Get-Content $versionPath -TotalCount 1).Trim()
}
else {
    'unknown'
}

$upstreamPath = $UpstreamRepo
$cloned = $false
if ([string]::IsNullOrWhiteSpace($upstreamPath)) {
    $cachePath = Join-Path $root $Cache
    if (-not (Test-Path $cachePath)) {
        New-Item -ItemType Directory -Force (Split-Path $cachePath) | Out-Null
        Write-Host "Cloning upstream xemu into $cachePath ..."
        & git clone --filter=blob:none --no-checkout $UpstreamUrl $cachePath
        if ($LASTEXITCODE -ne 0) {
            throw "could not clone $UpstreamUrl into $cachePath"
        }

        $cloned = $true
    }
    elseif ($RefreshUpstream) {
        Write-Host "Fetching upstream xemu into $cachePath ..."
        & git -C $cachePath fetch --filter=blob:none --tags --prune origin
        if ($LASTEXITCODE -ne 0) {
            throw "could not fetch origin in $cachePath"
        }
    }

    $upstreamPath = $cachePath
}
else {
    $upstreamPath = (Resolve-Path $UpstreamRepo).Path
    if ($RefreshUpstream) {
        Write-Host "Fetching upstream xemu into $upstreamPath ..."
        & git -C $upstreamPath fetch --filter=blob:none --tags --prune origin
        if ($LASTEXITCODE -ne 0) {
            throw "could not fetch origin in $upstreamPath"
        }
    }
}

$fromSha = Resolve-UpstreamCommit -Repository $upstreamPath -Reference $From
if (-not $fromSha) {
    $hint = if ($cloned) { '' } else { ' Try -RefreshUpstream to fetch new tags and branches.' }
    throw "cannot resolve -From '$From' in $upstreamPath.$hint"
}

$toSha = Resolve-UpstreamCommit -Repository $upstreamPath -Reference $To
if (-not $toSha) {
    $hint = if ($cloned) { '' } else { ' Try -RefreshUpstream to fetch new tags and branches.' }
    throw "cannot resolve -To '$To' in $upstreamPath.$hint"
}

$ownedFile = if ([System.IO.Path]::IsPathRooted($OwnedPaths)) {
    $OwnedPaths
}
else {
    Join-Path $root $OwnedPaths
}
if (-not (Test-Path $ownedFile)) {
    throw "missing owned-path manifest: $ownedFile"
}

$owned = @(
    Get-Content $ownedFile |
        ForEach-Object { $_.Trim().Replace('\', '/') } |
        Where-Object { $_.Length -gt 0 -and -not $_.StartsWith('#') }
)

$patched = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($patch in Get-ChildItem $patchDirectory -Filter '*.patch') {
    foreach ($line in Get-Content $patch.FullName) {
        # Older records use paths relative to the tracked xemu tree
        # (a/ui/xemu.c); newer records use paths relative to the XGrid
        # repository (a/xemu/ui/xemu.c). Both name a file under xemu/.
        if ($line -match '^diff --git a/(?:xemu/)?(.+?) b/') {
            [void]$patched.Add($Matches[1])
        }
    }
}

$diff = Invoke-Git -Repository $upstreamPath `
    -GitArguments @('diff', '--name-status', '--find-renames', "$fromSha..$toSha")
$changes = @(
    foreach ($line in $diff.Output) {
        $parts = $line -split "`t"
        if ($parts.Count -lt 2) {
            continue
        }

        $status = $parts[0]
        $path = $parts[-1]
        $oldPath = if ($parts.Count -ge 3) { $parts[1] } else { '' }
        [pscustomobject]@{
            Status  = $status
            Path    = $path
            OldPath = $oldPath
        }
    }
)

$commitCount = [int](
    (Invoke-Git -Repository $upstreamPath `
        -GitArguments @('rev-list', '--count', "$fromSha..$toSha")).Output[0]
)

$rows = @(
    foreach ($change in $changes) {
        $reason = if ($patched.Contains($change.Path) -or
                       ($change.OldPath -and $patched.Contains($change.OldPath))) {
            'XGrid patch'
        }
        elseif ((Test-OwnedPath -Path $change.Path -Owned $owned) -or
                ($change.OldPath -and (Test-OwnedPath -Path $change.OldPath -Owned $owned))) {
            'XGrid-owned'
        }
        elseif ($change.Path.EndsWith('.wrap', [StringComparison]::Ordinal) -and
                $change.Path.StartsWith('subprojects/', [StringComparison]::Ordinal)) {
            'dependency lock'
        }
        else {
            'upstream only'
        }

        [pscustomobject]@{
            Status  = $change.Status
            Path    = $change.Path
            OldPath = $change.OldPath
            Reason  = $reason
        }
    }
)

$high = @($rows | Where-Object { $_.Reason -ne 'upstream only' })
$low = @($rows | Where-Object { $_.Reason -eq 'upstream only' })
$fromShort = $fromSha.Substring(0, 12)
$toShort = $toSha.Substring(0, 12)

Write-Host 'XGrid upstream audit'
Write-Host '===================='
Write-Host "Lineage version: $currentVersion"
Write-Host "From:            $From ($fromShort)"
Write-Host "To:              $To ($toShort)"
Write-Host "Upstream path:   $upstreamPath"
Write-Host "Upstream commits: $commitCount"
Write-Host "Changed files:   $($rows.Count)"
Write-Host ''

Write-Host "High-risk changes: $($high.Count) (XGrid patches, owned areas, dependency locks)"
foreach ($row in $high | Sort-Object Reason, Path) {
    Write-Host ("  [{0}] {1}" -f $row.Reason, (Format-Change $row))
}

Write-Host ''
Write-Host "Upstream-only changes: $($low.Count)"
if ($low.Count -gt 0) {
    $areas = $low |
        Group-Object { Get-PathArea $_.Path } |
        Sort-Object Count -Descending |
        Select-Object -First 12
    foreach ($area in $areas) {
        Write-Host ("  {0,5}  {1}" -f $area.Count, $area.Name)
    }

    if ($All) {
        Write-Host ''
        foreach ($row in $low | Sort-Object Path) {
            Write-Host ("  {0}" -f (Format-Change $row))
        }
    }
    else {
        Write-Host '  (pass -All to list every upstream-only file)'
    }
}

if ($high.Count -gt 0) {
    Write-Host ''
    Write-Host 'Review the high-risk paths first. See docs/development/xemu-upgrade.md.'
}

$reportFullPath = ''
if (-not [string]::IsNullOrWhiteSpace($Report)) {
    $reportFullPath = if ([System.IO.Path]::IsPathRooted($Report)) {
        $Report
    }
    else {
        Join-Path $root $Report
    }

    $reportDirectory = Split-Path -Parent $reportFullPath
    if ($reportDirectory) {
        New-Item -ItemType Directory -Force $reportDirectory | Out-Null
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# XGrid upstream audit: $To")
    $lines.Add('')
    $lines.Add("Generated from the tracked core on $(Get-Date -Format 'yyyy-MM-dd HH:mm').")
    $lines.Add('')
    $lines.Add('| Item | Value |')
    $lines.Add('| --- | --- |')
    $lines.Add("| Lineage version | ``$currentVersion`` |")
    $lines.Add("| From | ``$From`` (``$fromShort``) |")
    $lines.Add("| To | ``$To`` (``$toShort``) |")
    $lines.Add("| Upstream commits | $commitCount |")
    $lines.Add("| Changed files | $($rows.Count) |")
    $lines.Add("| High-risk changes | $($high.Count) |")
    $lines.Add("| Upstream-only changes | $($low.Count) |")
    $lines.Add('')

    if ($high.Count -gt 0) {
        $lines.Add('## High-risk changes')
        $lines.Add('')
        $lines.Add('| Status | File | Why |')
        $lines.Add('| --- | --- | --- |')
        foreach ($row in $high | Sort-Object Reason, Path) {
            $display = $row.Path
            if ($row.OldPath) {
                $display = "$($row.OldPath) -> $display"
            }

            $lines.Add("| ``$($row.Status)`` | ``$display`` | $($row.Reason) |")
        }
        $lines.Add('')
    }

    if ($low.Count -gt 0) {
        $lines.Add('## Upstream-only changes')
        $lines.Add('')
        if ($All) {
            foreach ($row in $low | Sort-Object Path) {
                $display = $row.Path
                if ($row.OldPath) {
                    $display = "$($row.OldPath) -> $display"
                }

                $lines.Add("- ``$($row.Status)`` ``$display``")
            }
        }
        else {
            $areas = $low |
                Group-Object { Get-PathArea $_.Path } |
                Sort-Object Count -Descending
            $lines.Add('| Area | Files |')
            $lines.Add('| --- | --- |')
            foreach ($area in $areas) {
                $lines.Add("| ``$($area.Name)`` | $($area.Count) |")
            }
        }
        $lines.Add('')
    }

    $lines.Add('Apply these changes selectively; do not replay the xemu-patches record.')
    [System.IO.File]::WriteAllText($reportFullPath, ($lines -join "`n") + "`n")
    Write-Host ''
    Write-Host "Audit report:    $reportFullPath"
}
