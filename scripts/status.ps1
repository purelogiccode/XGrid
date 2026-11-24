# status.ps1 - documentation status and link check for XGrid.
#
# The old docs/status.json task queue was retired with the documentation
# rewrite. This script now validates the multi-page documentation and reports
# the current product checklist in ImplementationPlan.md.
#
# Usage:
#   pwsh -File scripts/status.ps1 -Check       # validate docs (default)
#   pwsh -File scripts/status.ps1 -Show        # product checklist summary
#   pwsh -File scripts/status.ps1 -Sync        # validate; no generated blocks
#   pwsh -File scripts/status.ps1 -Prompt      # point at the current plan
#   pwsh -File scripts/status.ps1 -Next        # no managed queue
#
# -Complete, -Negative, -Task, -Evidence, -CommitMessage, -Reopen, -DataPath,
# -NoSync and -NoGraph are accepted for compatibility with older automation.

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
    [string]$Reopen = "",
    [string]$DataPath = "",
    [switch]$NoSync,
    [switch]$NoGraph
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$failures = [System.Collections.Generic.List[string]]::new()

function Fail([string]$message) {
    $script:failures.Add($message)
}

function Get-DocumentationFiles {
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($directory in @("docs", "wiki")) {
        $path = Join-Path $root $directory
        if (Test-Path $path) {
            foreach ($file in Get-ChildItem $path -Recurse -File -Filter "*.md") {
                $files.Add($file)
            }
        }
    }

    foreach ($name in @("README.md", "CONTRIBUTING.md", "SECURITY.md", "AGENTS.md", "ImplementationPlan.md")) {
        $path = Join-Path $root $name
        if (Test-Path $path) {
            $files.Add((Get-Item $path))
        }
    }

    return $files
}

function Test-RequiredFiles {
    $required = @(
        "README.md",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "AGENTS.md",
        "ImplementationPlan.md",
        "docs/index.md",
        "docs/index.html",
        "docs/_sidebar.md",
        "docs/_coverpage.md",
        "docs/assets/style.css",
        "docs/assets/xgrid-mark.svg",
        "docs/getting-started/requirements.md",
        "docs/getting-started/installation.md",
        "docs/getting-started/first-boot.md",
        "docs/guides/settings.md",
        "docs/guides/display.md",
        "docs/guides/input.md",
        "docs/guides/save-states.md",
        "docs/reference/architecture.md",
        "docs/reference/configuration.md",
        "docs/reference/troubleshooting.md",
        "docs/legal/licensing.md",
        "wiki/Home.md",
        "wiki/_Sidebar.md"
    )

    foreach ($relative in $required) {
        if (-not (Test-Path (Join-Path $root $relative))) {
            Fail "missing required documentation file: $relative"
        }
    }
}

function Test-MarkdownLinks {
    foreach ($file in Get-DocumentationFiles) {
        $text = Get-Content $file.FullName -Raw
        foreach ($match in [regex]::Matches($text, '\]\((?!https?://|#|mailto:)([^)]+)\)')) {
            $raw = $match.Groups[1].Value.Trim()
            $target = $raw.Split('#')[0]
            if ([string]::IsNullOrWhiteSpace($target)) {
                continue
            }

            try {
                $target = [uri]::UnescapeDataString($target)
            }
            catch {
                # Keep the raw target; the existence check below reports it.
            }

            $candidate = if ([System.IO.Path]::IsPathRooted($target)) {
                $target
            }
            else {
                Join-Path $file.DirectoryName $target
            }

            if (Test-Path -LiteralPath $candidate) {
                continue
            }

            # GitHub wiki links use page names without the .md suffix.
            $isWikiPage = $file.FullName.StartsWith(
                (Join-Path $root "wiki"),
                [StringComparison]::OrdinalIgnoreCase)
            if ($isWikiPage -and $target -notmatch '\.md$') {
                $wikiCandidate = Join-Path $file.DirectoryName ($target + ".md")
                if (Test-Path -LiteralPath $wikiCandidate) {
                    continue
                }
            }

            Fail "$($file.FullName.Substring($root.Length + 1)): missing link target '$target'"
        }
    }
}

function Test-PageHeadings {
    foreach ($file in Get-DocumentationFiles) {
        if ($file.Name -in @("_sidebar.md", "_coverpage.md", "_Footer.md")) {
            continue
        }

        $head = Get-Content $file.FullName -TotalCount 12
        if (-not ($head | Where-Object { $_ -match '^(#\s+\S|<h1\b)' })) {
            Fail "$($file.FullName.Substring($root.Length + 1)): page has no H1"
        }
    }
}

function Invoke-Check {
    Test-RequiredFiles
    Test-MarkdownLinks
    Test-PageHeadings

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Host "FAIL: $failure"
        }

        Write-Host "status FAILED: $($failures.Count) documentation problem(s)."
        exit 1
    }

    $count = (Get-DocumentationFiles).Count
    Write-Host "status OK: $count documentation files, links and headings valid."
    exit 0
}

function Show-Status {
    $planPath = Join-Path $root "ImplementationPlan.md"
    $plan = Get-Content $planPath -Raw
    $done = ([regex]::Matches($plan, '(?m)^\s*-\s*\[x\]')).Count
    $open = ([regex]::Matches($plan, '(?m)^\s*-\s*\[ \]')).Count

    Write-Host "XGrid product status"
    Write-Host "  checklist: $done shipped, $open open"
    Write-Host "  plan:      ImplementationPlan.md"
    Write-Host "  docs:      docs/index.md"
    Write-Host "  wiki:      wiki/Home.md"

    $next = [regex]::Match($plan, '(?ms)^## 2\. Next\s*(.*?)^## 3\.')
    if ($next.Success) {
        $items = [regex]::Matches($next.Groups[1].Value, '(?m)^\s*-\s*\[ \]\s*(.+)$')
        if ($items.Count -gt 0) {
            Write-Host "  next:"
            foreach ($item in $items) {
                Write-Host "    - $($item.Groups[1].Value.Trim())"
            }
        }
    }
}

if ($Show) {
    Show-Status
    exit 0
}

if ($Prompt -or $Next) {
    Write-Host "No managed task queue. Work from ImplementationPlan.md and docs/index.md."
    exit 0
}

if ($Complete -or $Negative) {
    $task = if ([string]::IsNullOrWhiteSpace($Task)) { "the current task" } else { $Task }
    Write-Host "No managed task queue: $task needs no close step. Update ImplementationPlan.md when the work lands."
    exit 0
}

# -Check is the default, and -Sync validates in place (there are no generated
# status blocks after the documentation rewrite).
Invoke-Check
