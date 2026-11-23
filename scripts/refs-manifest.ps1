<#
.SYNOPSIS
Checksums the vendored reference sets in References2/ so a partial loss is
detected in seconds instead of after a failed session.

.DESCRIPTION
`References2/` is the canonical and only store for the reference projects. It is
git-ignored and carried between development machines by Dropbox, which has
emptied it twice (2026-10-12, 2026-10-14). This script does not copy anything:
it records one line per reference set (file count, total bytes, content hash) in
`docs/refs-manifest.json`, which IS tracked by git. That makes a wipe or a
partial deletion visible at session start, and tells you which set to re-fetch.

-Update   regenerate the manifest from the current tree
-Verify   compare the tree against the manifest (default)
-Quick    counts and sizes only; skips per-file content hashing

.PARAMETER Refs
Reference root. Defaults to <repo>/References2.

.PARAMETER Manifest
Manifest path. Defaults to <repo>/docs/refs-manifest.json.

.EXAMPLE
pwsh -File scripts/refs-manifest.ps1 -Verify
pwsh -File scripts/refs-manifest.ps1 -Update
#>
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$Verify,
    [switch]$Quick,
    [string]$Refs = (Join-Path (Split-Path $PSScriptRoot -Parent) 'References2'),
    [string]$Manifest = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\refs-manifest.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Verify -and -not $Update) { $Verify = $true }
if ($Verify -and $Update) { throw 'Pass either -Verify or -Update, not both.' }

function Get-FileSha256 {
    param([string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose() }
}

function Get-SetSha256 {
    param([string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Measure-ReferenceSet {
    param([System.IO.DirectoryInfo]$Directory, [switch]$NoContent)

    $files = @(Get-ChildItem -LiteralPath $Directory.FullName -Recurse -File -Force)
    $bytes = 0L
    foreach ($f in $files) { $bytes += $f.Length }

    $hash = $null
    if (-not $NoContent) {
        $root = $Directory.FullName.TrimEnd('\', '/')
        $lines = New-Object 'System.Collections.Generic.List[string]'
        foreach ($f in $files) {
            $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
            $lines.Add(('{0}|{1}|{2}' -f $rel, $f.Length, (Get-FileSha256 -Path $f.FullName)))
        }
        $arr = $lines.ToArray()
        [Array]::Sort($arr, [StringComparer]::Ordinal)
        $hash = Get-SetSha256 -Text ([string]::Join("`n", $arr))
    }

    [pscustomobject]@{
        name   = $Directory.Name
        files  = $files.Count
        bytes  = $bytes
        sha256 = $hash
    }
}

if (-not (Test-Path -LiteralPath $Refs)) {
    Write-Host "FAIL  References2 not found: $Refs" -ForegroundColor Red
    exit 2
}

$setDirs = @(Get-ChildItem -LiteralPath $Refs -Directory -Force | Sort-Object Name)

if ($Update) {
    $sets = New-Object 'System.Collections.Generic.List[object]'
    foreach ($d in $setDirs) {
        Write-Host ("hashing {0} ..." -f $d.Name) -NoNewline
        $set = Measure-ReferenceSet -Directory $d -NoContent:$Quick
        $sets.Add($set)
        Write-Host (" {0} files, {1:N0} MB" -f $set.files, ($set.bytes / 1MB))
    }

    $doc = [ordered]@{
        generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        host         = $env:COMPUTERNAME
        contentHash  = (-not $Quick)
        root         = 'References2'
        sets         = $sets.ToArray()
    }
    $doc | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Manifest -Encoding utf8
    Write-Host ("WROTE {0} ({1} sets)" -f $Manifest, $sets.Count) -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $Manifest)) {
    Write-Host "FAIL  manifest not found: $Manifest (run -Update once)" -ForegroundColor Red
    exit 2
}

$expected = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$expectedByName = @{}
foreach ($s in $expected.sets) { $expectedByName[$s.name] = $s }

$byName = @{}
foreach ($d in $setDirs) { $byName[$d.Name] = $d }

$failures = 0
$warnings = 0

foreach ($name in ($expectedByName.Keys | Sort-Object)) {
    $want = $expectedByName[$name]
    if (-not $byName.ContainsKey($name)) {
        Write-Host ("MISSING  {0,-24} {1,6} files / {2,8:N0} MB" -f $name, $want.files, ($want.bytes / 1MB)) -ForegroundColor Red
        $failures++
        continue
    }

    $got = Measure-ReferenceSet -Directory $byName[$name] -NoContent:($Quick -or -not $expected.contentHash)
    $sizeOk = ($got.files -eq $want.files) -and ($got.bytes -eq $want.bytes)
    $hashOk = (-not $got.sha256) -or ($got.sha256 -eq $want.sha256)

    if ($sizeOk -and $hashOk) {
        Write-Host ("ok       {0,-24} {1,6} files / {2,8:N0} MB" -f $name, $got.files, ($got.bytes / 1MB)) -ForegroundColor Green
        continue
    }

    $detail = 'files {0}/{1}, bytes {2:N0}/{3:N0}' -f $got.files, $want.files, $got.bytes, $want.bytes
    if (-not $hashOk) { $detail += ', content hash differs' }
    if ($sizeOk -and -not $hashOk) {
        Write-Host ("changed  {0,-24} {1}" -f $name, $detail) -ForegroundColor Yellow
        $warnings++
    }
    else {
        Write-Host ("DAMAGED  {0,-24} {1}" -f $name, $detail) -ForegroundColor Red
        $failures++
    }
}

foreach ($name in ($byName.Keys | Sort-Object)) {
    if (-not $expectedByName.ContainsKey($name)) {
        Write-Host ("NEW      {0,-24} not in manifest (run -Update)" -f $name) -ForegroundColor Yellow
        $warnings++
    }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "RESULT FAIL  $failures set(s) missing or damaged, $warnings warning(s)" -ForegroundColor Red
    exit 1
}
if ($warnings -gt 0) {
    Write-Host "RESULT WARN  $warnings set(s) changed; regenerate with -Update if intended" -ForegroundColor Yellow
    exit 0
}
Write-Host 'RESULT PASS  all reference sets match the manifest' -ForegroundColor Green
exit 0
