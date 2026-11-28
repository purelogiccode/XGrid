<#
.SYNOPSIS
Verifies a packaged release tree produced by `scripts/package.ps1`.

.DESCRIPTION
This is the acceptance gate for the release task (R1.1). It checks the two
things a GPL-2.0-or-later binary distribution can get wrong without anyone noticing:

  1. license completeness - every archive carries COPYING, LICENSE and
     THIRD_PARTY_NOTICES.md, the repo's COPYING really is the GPL-2.0 text, and
     every source file that declares itself ported is recorded in the notices;
  2. package integrity - the expected archives exist for every RID, contain the
     executable and the release metadata, carry no symbols or firmware/game
     data, and match their SHA-256 sum and the package manifest. Each frontend
     archive that has a core build carries the emulator (core/xemu/), and every
     Unix executable entry is mode 0755 so the extracted archive can run.

It also smoke-runs the Windows CLI from its archive (`--version` must equal the
version in the archive's VERSION file). That leg is skipped on other hosts.

Exit 0 = pass, 1 = one or more failures.

.PARAMETER Dist
Package output directory (default `dist`, relative to the repository root).

.PARAMETER Version
Expected bare semantic version, e.g. `0.1.0`. Read from `package-manifest.json`
when omitted.

.PARAMETER Rids
Runtime identifiers the tree must contain.

.PARAMETER SkipSmoke
Skip the Windows CLI smoke run (for a host without PowerShell on Windows).

.PARAMETER SkipCoreCheck
Accept a development tree without the Windows or Linux core (the matching
`scripts/package.ps1 -SkipCore`). A release tree must not use this.

.EXAMPLE
pwsh -File scripts/package.ps1 -Rids win-x64,linux-x64
pwsh -File scripts/package-verify.ps1 -Rids win-x64,linux-x64
#>
[CmdletBinding()]
param(
    [string]$Dist = 'dist',
    [string]$Version = '',
    [string[]]$Rids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64'),
    [switch]$SkipSmoke,
    [switch]$SkipCoreCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $failures = [System.Collections.Generic.List[string]]::new()
    function Fail([string]$message) { $script:failures.Add($message) }

    # `pwsh -File` passes "-Rids win-x64,linux-x64" as one literal string.
    $Rids = @($Rids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

    $distPath = if ([System.IO.Path]::IsPathRooted($Dist)) { $Dist } else { Join-Path $repo $Dist }
    if (-not (Test-Path $distPath)) {
        Write-Host "PACKAGE FAIL: missing package directory $distPath"
        exit 1
    }

    $manifestPath = Join-Path $distPath 'package-manifest.json'
    if (-not (Test-Path $manifestPath)) {
        Fail "missing package-manifest.json in $Dist"
        $manifest = $null
    }
    else {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($Version)) { $Version = [string]$manifest.version }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) { Fail 'no expected version (pass -Version)' }
    if ($manifest) {
        if ([string]$manifest.version -ne $Version) {
            Fail "package-manifest.json version '$([string]$manifest.version)' does not match '$Version'"
        }
        if ($manifest.dirty) {
            Fail 'package-manifest.json records a dirty source tree'
        }
        if ([string]::IsNullOrWhiteSpace([string]$manifest.commit)) {
            Fail 'package-manifest.json does not record the source commit'
        }
        else {
            $head = @(& git -C $repo rev-parse HEAD 2>$null)
            if ($LASTEXITCODE -ne 0 -or $head.Count -eq 0) {
                Fail 'could not read the current Git commit'
            }
            elseif ([string]$manifest.commit -ne $head[0].Trim()) {
                Fail "package-manifest.json commit '$([string]$manifest.commit)' does not match HEAD '$($head[0].Trim())'"
            }
        }
        $status = @(& git -C $repo status --porcelain 2>$null)
        if ($LASTEXITCODE -ne 0) {
            Fail 'could not read the working-tree status'
        }
        elseif ($status.Count -gt 0) {
            Fail 'working tree is dirty; package and verify a clean commit'
        }
    }

    $expectedCore = if (Test-Path (Join-Path $repo 'xemu/XEMU_VERSION')) {
        (Get-Content (Join-Path $repo 'xemu/XEMU_VERSION') -TotalCount 1).Trim()
    }
    else { '' }
    $coreRids = @()
    if ($manifest -and $manifest.core) {
        if ($expectedCore -and [string]$manifest.core.version -ne $expectedCore) {
            Fail "package-manifest.json records core version '$($manifest.core.version)', expected '$expectedCore'"
        }
        if ($manifest.core.rids) {
            $coreRids = @($manifest.core.rids | ForEach-Object { [string]$_ })
        }
        foreach ($rid in $coreRids) {
            if ($Rids -notcontains $rid) {
                Fail "package-manifest.json claims a core for $rid, which is not in the requested RIDs"
            }
        }
    }
    if (-not $SkipCoreCheck) {
        foreach ($rid in $Rids) {
            if (($rid -eq 'win-x64' -or $rid -eq 'linux-x64') -and $coreRids -notcontains $rid) {
                Fail "package-manifest.json does not describe a core for $rid (pass -SkipCoreCheck for a development tree)"
            }
        }
    }

    # --- license inputs -------------------------------------------------------
    Write-Host 'LICENSE INPUTS'
    $copyingPath = Join-Path $repo 'COPYING'
    if (-not (Test-Path $copyingPath)) {
        Fail 'COPYING is missing from the repository'
    }
    else {
        $copying = Get-Content $copyingPath -Raw
        if ($copying -notmatch 'GNU GENERAL PUBLIC LICENSE') { Fail 'COPYING is not the GNU GPL text' }
        if ($copying -notmatch 'Version 2, June 1991') { Fail 'COPYING is not GPL version 2' }
        if ($copying -notmatch 'TERMS AND CONDITIONS') { Fail 'COPYING has no license terms' }
        if ($copying.Length -lt 15000) { Fail "COPYING is only $($copying.Length) bytes; the GPL-2.0 text is ~18 KB" }
        Write-Host ("  copying      GPL-2.0 text, {0} bytes" -f $copying.Length)
    }

    $licensePath = Join-Path $repo 'LICENSE'
    if (-not (Test-Path $licensePath)) { Fail 'LICENSE is missing from the repository' }
    elseif ((Get-Content $licensePath -Raw) -notmatch 'GNU General Public License') {
        Fail 'LICENSE does not name the GNU General Public License'
    }
    else { Write-Host '  license      project GPL-2.0-or-later notice' }

    $noticesPath = Join-Path $repo 'THIRD_PARTY_NOTICES.md'
    if (-not (Test-Path $noticesPath)) { Fail 'THIRD_PARTY_NOTICES.md is missing from the repository' }
    $notices = if (Test-Path $noticesPath) { Get-Content $noticesPath -Raw } else { '' }

    # Every file whose header declares a port ("// Ported from" or
    # "// Derived from") must carry an SPDX line and be recorded in the notices
    # by repository path.
    $portedCount = 0
    foreach ($file in Get-ChildItem (Join-Path $repo 'src') -Recurse -Filter '*.cs' -File) {
        $header = Get-Content $file.FullName -TotalCount 8
        if (-not ($header -match '^// (Ported|Derived) from')) { continue }
        $portedCount++
        $relative = [System.IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
        if (-not ($header -match 'SPDX-License-Identifier')) {
            Fail "$relative is ported but has no SPDX-License-Identifier line"
        }
        if ($notices -notmatch [regex]::Escape($relative)) {
            Fail "$relative is ported but is not recorded in THIRD_PARTY_NOTICES.md"
        }
    }
    if ($portedCount -eq 0) { Fail 'no ported source file was found; the notices check is meaningless' }
    Write-Host ("  notices      {0} ported files recorded" -f $portedCount)

    # --- dependency license texts ---------------------------------------------
    $licenseDir = Join-Path $repo 'licenses'
    if (-not (Test-Path $licenseDir)) { Fail 'licenses/ is missing from the repository' }
    $licenseTexts = @(Get-ChildItem $licenseDir -File | Sort-Object Name | ForEach-Object { $_.Name })
    if ($licenseTexts.Count -lt 10) { Fail "licenses/ only has $($licenseTexts.Count) files" }
    foreach ($text in $licenseTexts) {
        if ((Get-Item (Join-Path $licenseDir $text)).Length -lt 500) {
            Fail "licenses/$text is too small to be a license text"
        }
    }
    Write-Host ("  license texts {0} files" -f $licenseTexts.Count)

    # --- archives -------------------------------------------------------------
    Write-Host 'PACKAGE CONTENTS'
    $required = @('COPYING', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md', 'RELEASE-NOTES.md', 'VERSION')
    $forbiddenExtension = @('.pdb', '.xml', '.iso', '.xiso', '.cso', '.img', '.qcow2', '.qcow2c', '.xbe', '.bin')
    $archiveRows = [System.Collections.Generic.List[object]]::new()

    # Returns the entry names, the text of small entries and the first four
    # bytes of every entry (enough to prove an executable matches its RID).
    function Read-Archive([string]$Path) {
        $names = [System.Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
        $text = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        $head = [System.Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
        $modes = [System.Collections.Generic.Dictionary[string, System.IO.UnixFileMode]]::new([StringComparer]::Ordinal)

        # A small entry is read whole (prefix + text); a large one only needs its
        # first four bytes, so it is never decompressed to the end.
        function Add-Entry([string]$Name, [long]$Length, [System.IO.Stream]$Stream) {
            if ($Length -lt 1048576) {
                $buffer = [System.IO.MemoryStream]::new()
                try {
                    $Stream.CopyTo($buffer)
                    $bytes = $buffer.ToArray()
                    $text[$Name] = [System.Text.Encoding]::UTF8.GetString($bytes)
                    $take = [Math]::Min(4, $bytes.Length)
                    $head[$Name] = if ($take -gt 0) { $bytes[0..($take - 1)] } else { [byte[]]@() }
                }
                finally { $buffer.Dispose() }
            }
            else {
                $prefix = [byte[]]::new(4)
                $read = $Stream.Read($prefix, 0, 4)
                $head[$Name] = if ($read -gt 0) { $prefix[0..($read - 1)] } else { [byte[]]@() }
            }
            $names[$Name] = $Length
        }

        if ($Path.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
            $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
            try {
                foreach ($entry in $zip.Entries) {
                    if ($entry.Name.Length -eq 0) { continue }
                    $stream = $entry.Open()
                    try { Add-Entry $entry.FullName $entry.Length $stream } finally { $stream.Dispose() }
                }
            }
            finally { $zip.Dispose() }
        }
        else {
            $stream = [System.IO.File]::OpenRead($Path)
            try {
                $gzip = [System.IO.Compression.GZipStream]::new($stream, [System.IO.Compression.CompressionMode]::Decompress)
                try {
                    $reader = [System.Formats.Tar.TarReader]::new($gzip)
                    try {
                        while ($null -ne ($entry = $reader.GetNextEntry())) {
                            if ($entry.EntryType -ne [System.Formats.Tar.TarEntryType]::RegularFile -and
                                $entry.EntryType -ne [System.Formats.Tar.TarEntryType]::V7RegularFile) { continue }
                            if (-not $entry.DataStream) { continue }
                            $name = $entry.Name.Replace('\', '/')
                            Add-Entry $name ([long]$entry.Length) $entry.DataStream
                            $modes[$name] = $entry.Mode
                        }
                    }
                    finally { $reader.Dispose() }
                }
                finally { $gzip.Dispose() }
            }
            finally { $stream.Dispose() }
        }
        return [pscustomobject]@{ Names = $names; Text = $text; Head = $head; Modes = $modes }
    }

    function Test-ExecutableMode([System.IO.UnixFileMode]$Mode) {
        return (($Mode -band [System.IO.UnixFileMode]::UserExecute) -ne 0) -or
               (($Mode -band [System.IO.UnixFileMode]::GroupExecute) -ne 0) -or
               (($Mode -band [System.IO.UnixFileMode]::OtherExecute) -ne 0)
    }

    foreach ($rid in $Rids) {
        $isWindowsRid = $rid.StartsWith('win-', [StringComparison]::Ordinal)
        $extension = if ($isWindowsRid) { '.zip' } else { '.tar.gz' }
        $sumsPath = Join-Path $distPath "SHA256SUMS-$rid.txt"
        $sums = @{}
        if (-not (Test-Path $sumsPath)) {
            Fail "missing SHA256SUMS-$rid.txt"
        }
        else {
            foreach ($line in Get-Content $sumsPath) {
                if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { Fail "SHA256SUMS-$rid.txt has an unparsable line: $line"; continue }
                $sums[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant()
            }
        }

        foreach ($app in @('cli', 'frontend')) {
            $file = "XGrid-$app-$rid$extension"
            $path = Join-Path $distPath $file
            if (-not (Test-Path $path)) { Fail "missing archive $file"; continue }

            $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if (-not $sums.ContainsKey($file)) { Fail "SHA256SUMS-$rid.txt does not list $file" }
            elseif ($sums[$file] -ne $actual) { Fail "$file hash $actual does not match the recorded $($sums[$file])" }
            if ($manifest) {
                $row = @($manifest.archives | Where-Object { $_.file -eq $file })
                if ($row.Count -ne 1) { Fail "package-manifest.json does not describe $file" }
                elseif ($row[0].sha256 -ne $actual) { Fail "package-manifest.json hash for $file is stale" }
            }

            $archive = Read-Archive $path
            if ($archive.Names.Count -eq 0) { Fail "$file is empty" }
            foreach ($entry in $required) {
                if (-not $archive.Names.ContainsKey($entry)) { Fail "$file is missing $entry" }
            }
            foreach ($text in $licenseTexts) {
                if (-not $archive.Names.ContainsKey("licenses/$text")) {
                    Fail "$file is missing licenses/$text"
                }
            }
            $exe = if ($app -eq 'cli') { 'XGrid.Cli' } else { 'XGrid.Frontend.Avalonia' }
            $exeName = if ($isWindowsRid) { "$exe.exe" } else { $exe }
            if (-not $archive.Names.ContainsKey($exeName)) { Fail "$file is missing $exeName" }
            elseif ($archive.Head.ContainsKey($exeName)) {
                # The binary must actually be built for the archive's platform,
                # not just named for it.
                $magic = -join ($archive.Head[$exeName] | ForEach-Object { $_.ToString('x2') })
                $matchesPlatform = if ($isWindowsRid) { $magic.StartsWith('4d5a', [StringComparison]::Ordinal) }
                    elseif ($rid.StartsWith('linux-', [StringComparison]::Ordinal)) { $magic -eq '7f454c46' }
                    else { @('cffaedfe', 'cafebabe', 'cefaedfe', 'feedfacf') -contains $magic }
                if (-not $matchesPlatform) { Fail "$file $exeName has magic $magic, not a $rid executable" }
            }
            if (-not $isWindowsRid -and $archive.Names.ContainsKey($exeName)) {
                # A Unix archive that loses the execute bit cannot start. The
                # packager writes 0755 for the CLI, the frontend and a core.
                if (-not $archive.Modes.ContainsKey($exeName)) {
                    Fail "$file does not record a mode for $exeName"
                }
                elseif (-not (Test-ExecutableMode ($archive.Modes[$exeName]))) {
                    Fail "$file $exeName has mode $($archive.Modes[$exeName]), not executable"
                }
            }
            if ($archive.Names.ContainsKey('VERSION') -and $Version) {
                $declared = $archive.Text['VERSION'].Trim()
                if ($declared -ne $Version) { Fail "$file VERSION is '$declared', expected '$Version'" }
            }
            foreach ($name in $archive.Names.Keys) {
                if ($name -match '(^|/)References2(/|$)' -or $name -match 'dsp-oracle') {
                    Fail "$file ships a development-only path: $name"
                }
                foreach ($ext in $forbiddenExtension) {
                    if ($name.EndsWith($ext, [StringComparison]::OrdinalIgnoreCase)) {
                        Fail "$file ships a forbidden file: $name"
                    }
                }
            }

            $coreEntries = @($archive.Names.Keys | Where-Object { $_ -like 'core/xemu/*' })
            $hasCore = $coreRids -contains $rid
            if ($app -eq 'cli' -and $coreEntries.Count -gt 0) {
                Fail "$file carries core files; the core belongs in the frontend archive"
            }
            if ($app -eq 'frontend') {
                if (-not $hasCore -and $coreEntries.Count -gt 0) {
                    Fail "$file carries core files but package-manifest.json does not list $rid"
                }
                elseif ($hasCore) {
                    # The archive carries the emulator itself; a frontend without
                    # it can present nothing.
                    if ($rid.StartsWith('win-', [StringComparison]::Ordinal)) {
                        foreach ($entry in 'core/xemu/xemu.exe', 'core/xemu/xgrid_core.dll', 'core/xemu/xgrid-core.json') {
                            if (-not $archive.Names.ContainsKey($entry)) { Fail "$file is missing $entry" }
                        }
                        $coreLibraries = @($archive.Names.Keys | Where-Object { $_ -like 'core/xemu/*.dll' })
                        if ($coreLibraries.Count -lt 5) {
                            Fail "$file ships $($coreLibraries.Count) files under core/xemu; the core needs its runtime DLLs"
                        }
                        $recordedLibraries = @()
                        if ($manifest -and $manifest.core -and
                            $manifest.core.PSObject.Properties.Name -contains 'libraries') {
                            $recordedLibraries = @($manifest.core.libraries | Where-Object { $_.rid -eq $rid })
                        }
                        if ($recordedLibraries.Count -eq 0) {
                            Fail "$file ships core libraries but package-manifest.json has no core.libraries rows for $rid"
                        }
                        else {
                            $actualNames = @($coreLibraries | ForEach-Object { ($_ -split '/')[-1] } | Sort-Object)
                            $recordedNames = @($recordedLibraries | ForEach-Object { [string]$_.name } | Sort-Object)
                            foreach ($name in $actualNames) {
                                if ($recordedNames -notcontains $name) {
                                    Fail "$file ships core/xemu/$name but package-manifest.json does not record it"
                                }
                            }
                            foreach ($name in $recordedNames) {
                                if ($actualNames -notcontains $name) {
                                    Fail "package-manifest.json records core/xemu/$name but $file does not ship it"
                                }
                            }
                        }
                        foreach ($entry in 'core/xemu/xemu.exe', 'core/xemu/xgrid_core.dll') {
                            if ($archive.Head.ContainsKey($entry)) {
                                $magic = -join ($archive.Head[$entry] | ForEach-Object { $_.ToString('x2') })
                                if (-not $magic.StartsWith('4d5a', [StringComparison]::Ordinal)) {
                                    Fail "$file $entry is not a Windows binary"
                                }
                            }
                        }
                    }
                    else {
                        foreach ($entry in 'core/xemu/xemu', 'core/xemu/xgrid-core.json', 'core/xemu/LICENSE.txt') {
                            if (-not $archive.Names.ContainsKey($entry)) { Fail "$file is missing $entry" }
                        }
                        $coreName = 'core/xemu/xemu'
                        if ($archive.Head.ContainsKey($coreName)) {
                            $magic = -join ($archive.Head[$coreName] | ForEach-Object { $_.ToString('x2') })
                            $matchesPlatform = if ($rid.StartsWith('linux-', [StringComparison]::Ordinal)) {
                                $magic -eq '7f454c46'
                            }
                            else {
                                @('cffaedfe', 'cafebabe', 'cefaedfe', 'feedfacf') -contains $magic
                            }
                            if (-not $matchesPlatform) {
                                Fail "$file $coreName has magic $magic, not a $rid executable"
                            }
                        }
                        if (-not $archive.Modes.ContainsKey($coreName)) {
                            Fail "$file does not record a mode for $coreName"
                        }
                        elseif (-not (Test-ExecutableMode ($archive.Modes[$coreName]))) {
                            Fail "$file $coreName has mode $($archive.Modes[$coreName]), not executable"
                        }
                    }
                }
            }

            $bytes = (Get-Item $path).Length
            $archiveRows.Add([pscustomobject]@{ File = $file; Bytes = $bytes; Sha256 = $actual; Entries = $archive.Names.Count })
            Write-Host ("  {0,-30} {1,8:N1} MB  {2} entries  {3}" -f $file, ($bytes / 1MB), $archive.Names.Count, $actual.Substring(0, 16))

            if ($app -eq 'cli' -and $rid -eq 'win-x64' -and -not $SkipSmoke -and $IsWindows) {
                $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("xgrid-smoke-" + [guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $temp | Out-Null
                try {
                    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
                    try {
                        $entry = $zip.GetEntry($exeName)
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $temp $exeName), $true)
                    }
                    finally { $zip.Dispose() }
                    $reported = (& (Join-Path $temp $exeName) --version | Out-String).Trim()
                    if ($reported -ne "XGrid $Version") { Fail "packaged CLI reports '$reported', expected 'XGrid $Version'" }
                    else { Write-Host "  smoke        $reported" }
                }
                finally { Remove-Item -Recurse -Force $temp }
            }
        }
    }

    # --- release notes --------------------------------------------------------
    if ($Version) {
        $notes = Join-Path $repo "docs/release-notes/v$Version.md"
        if (-not (Test-Path $notes)) { Fail "docs/release-notes/v$Version.md is missing" }
        else { Write-Host ("  notes        docs/release-notes/v{0}.md ({1} bytes)" -f $Version, (Get-Item $notes).Length) }
    }

    Write-Host ''
    if ($failures.Count -gt 0) {
        Write-Host "PACKAGE FAIL: $($failures.Count) problem(s)"
        foreach ($failure in $failures) { Write-Host "  - $failure" }
        exit 1
    }
    Write-Host ("PACKAGE PASS: {0} archives, version {1}, {2} RIDs, licenses complete" -f $archiveRows.Count, $Version, $Rids.Count)
    foreach ($row in $archiveRows) {
        Write-Host ("  {0}  {1}  {2:N0} bytes" -f $row.Sha256, $row.File, $row.Bytes)
    }
    exit 0
}
finally {
    Pop-Location
}
