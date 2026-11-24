# port-manifest.ps1 - provenance and notice check for the current XGrid tree.
#
# The old per-file C# port ledger (docs/xemu-port-manifest.md) was retired when
# the native xemu core became the product path. This script now checks the
# records that still matter:
#   1. the license and notice files exist;
#   2. the notices name xemu, QEMU and the vendored libchdr;
#   3. every XGrid-owned native source file carries an SPDX identifier;
#   4. libchdr's own license text ships with the vendored source;
#   5. every C# file with a `Ported from` header is named in the notices.
#
# Usage (from the repo root):
#   pwsh -File scripts/port-manifest.ps1 -Check
#   pwsh -File scripts/port-manifest.ps1 -UpdateFileList   # retired, no-op

param(
    [switch]$Check,
    [switch]$UpdateFileList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failures = [System.Collections.Generic.List[string]]::new()

function Fail([string]$message) {
    $script:failures.Add($message)
}

function Require-File([string]$relative) {
    if (-not (Test-Path (Join-Path $root $relative))) {
        Fail "missing required file: $relative"
    }
}

function Invoke-Check {
    $noticesPath = Join-Path $root "THIRD_PARTY_NOTICES.md"
    $required = @(
        "COPYING",
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "docs/legal/licensing.md",
        "docs/legal/porting-policy.md",
        "xemu/xgrid-core/thirdparty/libchdr/LICENSE.txt"
    )
    foreach ($relative in $required) {
        Require-File $relative
    }

    if (Test-Path $noticesPath) {
        $notices = Get-Content $noticesPath -Raw
        foreach ($token in @("xemu", "QEMU", "libchdr", "GPL-2.0", "BSD-3-Clause")) {
            if ($notices -notmatch [regex]::Escape($token)) {
                Fail "THIRD_PARTY_NOTICES.md does not mention $token"
            }
        }

        $ported = Get-ChildItem (Join-Path $root "src") -Recurse -File -Include "*.cs" |
            Select-String -Pattern '(?m)^\s*(///?|\*)\s*Ported from' -List
        foreach ($match in $ported) {
            $relative = [System.IO.Path]::GetRelativePath($root, $match.Path).Replace('\', '/')
            if ($notices -notmatch [regex]::Escape($relative)) {
                Fail "ported file is not named in THIRD_PARTY_NOTICES.md: $relative"
            }
        }
        Write-Host "ported C# files checked: $($ported.Count)"
    }

    $nativeFiles = Get-ChildItem (Join-Path $root "xemu/xgrid-core") -Recurse -File -Include "*.c", "*.h" |
        Where-Object { $_.FullName -notlike "*\thirdparty\*" }
    foreach ($file in $nativeFiles) {
        if (-not (Select-String -Path $file.FullName -Pattern "SPDX-License-Identifier" -Quiet)) {
            $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
            Fail "native source has no SPDX identifier: $relative"
        }
    }
    Write-Host "XGrid native files checked: $($nativeFiles.Count)"

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Host "FAIL: $failure"
        }

        Write-Host "provenance FAILED: $($failures.Count) problem(s)."
        exit 1
    }

    Write-Host "provenance OK: notices, license files, SPDX headers and ported-file records are present."
    exit 0
}

if ($UpdateFileList) {
    Write-Host "The per-file port ledger is retired; there is no generated appendix to update."
    Write-Host "The current records are THIRD_PARTY_NOTICES.md and docs/legal/."
    exit 0
}

Invoke-Check
