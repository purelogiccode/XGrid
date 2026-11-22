# nv097-coverage.ps1 - XFP1 method-surface checker and table generator.
#
# Verifies docs/research-notes/nv097-coverage.md against the reference:
#   - every plain NV097_ method define in xemu 0.8.139 nv2a_regs.h has one
#     table row with the same offset;
#   - every table row names a define with the same offset;
#   - the status is one of handled|recorded.
# -GenerateTable writes src/XGrid.Nv2a/Nv097MethodTable.cs (the handled and
# declined offset arrays the runtime unknown-method counter uses).
#
# Usage (from the repo root):
#   pwsh -File scripts/nv097-coverage.ps1 -Check
#   pwsh -File scripts/nv097-coverage.ps1 -GenerateTable

param(
    [switch]$Check,
    [switch]$GenerateTable
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$regsPath = Join-Path $root "References2\xemu-0.8.139\hw\xbox\nv2a\nv2a_regs.h"
$notePath = Join-Path $root "docs\research-notes\nv097-coverage.md"
$tablePath = Join-Path $root "src\XGrid.Nv2a\Nv097MethodTable.cs"

if (-not (Test-Path $regsPath)) {
    Write-Error "reference header not found: $regsPath"
    exit 2
}

if (-not $Check -and -not $GenerateTable) {
    $Check = $true
}

# Plain method defines: "#   define NV097_NAME 0x00000000". Sub-field defines
# (X/Y/WIDTH/OFFSET...) are indented deeper and are not methods.
$defines = @{}
foreach ($line in (Get-Content $regsPath)) {
    if ($line -match '^#   define NV097_([A-Z0-9_]+)\s+0x([0-9A-Fa-f]{8})\s*$') {
        $defines[$Matches[1]] = [Convert]::ToUInt32($Matches[2], 16)
    }
}

# The main table rows: | offset | `NAME` | kind | count | status | group |.
# A `range` row covers count consecutive offsets (4 bytes apart), including the
# alias methods nv2a_regs.h names inside it.
$rows = @()
foreach ($line in (Get-Content $notePath)) {
    if ($line -match '^\| 0x([0-9A-Fa-f]{4,8}) \| `([A-Z0-9_]+)` \| ([^|]+) \| (\d+) \| (handled|recorded) \|') {
        $kind = $Matches[3].Trim()
        $rows += [pscustomobject]@{
            Offset = [Convert]::ToUInt32($Matches[1], 16)
            Name = $Matches[2]
            Kind = $kind
            Count = [int]$Matches[4]
            Status = $Matches[5]
        }
    }
}

$failures = [System.Collections.Generic.List[string]]::new()

if ($rows.Count -eq 0) {
    $failures.Add("no method rows parsed from docs/research-notes/nv097-coverage.md")
}

$rowNames = @{}
foreach ($row in $rows) {
    $rowNames[$row.Name] = $row
    if (-not $defines.ContainsKey($row.Name)) {
        $failures.Add("table row has no NV097_ define: $($row.Name)")
        continue
    }

    if ($defines[$row.Name] -ne $row.Offset) {
        $failures.Add(("offset mismatch for {0}: note 0x{1:X4}, define 0x{2:X4}" -f
            $row.Name, $row.Offset, $defines[$row.Name]))
    }
}

function Find-Row([uint32]$offset) {
    foreach ($row in $rows) {
        if ($row.Offset -eq $offset) { return $row }
        if ($row.Kind -eq "range") {
            $span = [uint32]($row.Count * 4)
            if ($offset -ge $row.Offset -and $offset -lt ($row.Offset + $span) -and
                (($offset - $row.Offset) % 4) -eq 0) {
                return $row
            }
        }

        if ($row.Kind -eq "case4") {
            for ($slot = 0; $slot -lt 4; $slot++) {
                if ($offset -eq ($row.Offset + [uint32]($row.Count * $slot))) { return $row }
            }
        }

        if ($row.Kind -eq "case4x4") {
            for ($slot = 0; $slot -lt 4; $slot++) {
                $base = [uint32]($row.Offset + $row.Count * $slot)
                if ($offset -ge $base -and $offset -lt ($base + 16) -and
                    (($offset - $base) % 4) -eq 0) {
                    return $row
                }
            }
        }
    }

    return $null
}

foreach ($name in $defines.Keys) {
    if ($null -eq (Find-Row $defines[$name])) {
        $failures.Add(("define has no table row or range: NV097_{0} (0x{1:X4})" -f $name, $defines[$name]))
    }
}

# The runtime table expands every range over its count so an offset a title
# writes inside a range is known even when nv2a_regs.h only names the range
# base (0xB84 is a transform constant, not an unknown method).
$handled = @()
$declined = @()
foreach ($row in $rows) {
    if ($row.Kind -eq "case4") {
        for ($slot = 0; $slot -lt 4; $slot++) {
            $offset = $row.Offset + [uint32]($row.Count * $slot)
            if ($row.Status -eq "handled") { $handled += $offset }
            else { $declined += $offset }
        }

        continue
    }

    if ($row.Kind -eq "case4x4") {
        for ($slot = 0; $slot -lt 4; $slot++) {
            for ($word = 0; $word -lt 4; $word++) {
                $offset = $row.Offset + [uint32]($row.Count * $slot + 4 * $word)
                if ($row.Status -eq "handled") { $handled += $offset }
                else { $declined += $offset }
            }
        }

        continue
    }

    $limit = if ($row.Kind -eq "range") { $row.Count } else { 1 }
    for ($i = 0; $i -lt $limit; $i++) {
        $offset = $row.Offset + [uint32](4 * $i)
        if ($row.Status -eq "handled") { $handled += $offset }
        else { $declined += $offset }
    }
}
$handled = @($handled | Sort-Object -Unique)
$declined = @($declined | Sort-Object -Unique)

if ($GenerateTable) {
    $handledText = ($handled | ForEach-Object { "0x{0:X4}" -f $_ }) -join ", "
    $declinedText = ($declined | ForEach-Object { "0x{0:X4}" -f $_ }) -join ", "
    $source = @"
// SPDX-License-Identifier: GPL-2.0
// Derived from xemu 0.8.139 hw/xbox/nv2a/pgraph/methods.h.inc and
// hw/xbox/nv2a/nv2a_regs.h (the NV097 method surface), classified by
// docs/research-notes/nv097-coverage.md. Generated by
// scripts/nv097-coverage.ps1 -GenerateTable; edit the note, not this file.

using System;

namespace XGrid.Nv2a;

/// <summary>
/// The NV097 method surface: the offsets the engine latches (<c>handled</c>) and
/// the offsets it deliberately ignores (<c>recorded</c>, with the reason in the
/// coverage note). A write outside both sets is a genuinely unknown method.
/// </summary>
public static class Nv097MethodTable
{
    private static readonly uint[] HandledOffsets =
    [
        $handledText
    ];

    private static readonly uint[] DeclinedOffsets =
    [
        $declinedText
    ];

    /// <summary>Gets the number of methods the engine latches.</summary>
    public static int HandledCount => HandledOffsets.Length;

    /// <summary>Gets the number of methods explicitly declined.</summary>
    public static int DeclinedCount => DeclinedOffsets.Length;

    /// <summary>Returns whether the method offset is a known NV097 method.</summary>
    /// <param name="method">The method offset.</param>
    /// <returns>True for a handled or explicitly declined method.</returns>
    public static bool IsKnown(uint method)
    {
        return Array.BinarySearch(HandledOffsets, method) >= 0
            || Array.BinarySearch(DeclinedOffsets, method) >= 0;
    }
}
"@
    $source = $source -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText(
        $tablePath, ($source.TrimEnd() + "`n"), [System.Text.UTF8Encoding]::new($false))
    Write-Host "generated $tablePath (handled=$($handled.Count) declined=$($declined.Count))"
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "NV097 COVERAGE: FAIL ($($failures.Count))"
    foreach ($failure in $failures) {
        Write-Host "  $failure"
    }

    exit 1
}

Write-Host ("NV097 COVERAGE: PASS defines={0} table={1} handled={2} recorded={3}" -f
    $defines.Count, $rows.Count, $handled.Count, $declined.Count)
exit 0
