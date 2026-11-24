<#
.SYNOPSIS
Builds the reproducible release tree for the CLI and the Avalonia frontend.

.DESCRIPTION
This is the local, cross-platform replacement for the dormant `release.yml`
publish job (`docs/ci.md`). For every runtime identifier it publishes the two
applications self-contained and single-file, strips symbols and XML docs, adds
the files a GPL-2.0-or-later binary distribution must carry (`COPYING`, `LICENSE`,
`THIRD_PARTY_NOTICES.md`, the release notes), writes `VERSION`, archives with
fixed timestamps and sorted entries, and records SHA-256 sums plus a manifest.

The output is a *release tree*:

    dist/
      XGrid-cli-<rid>.zip|.tar.gz
      XGrid-frontend-<rid>.zip|.tar.gz
      SHA256SUMS-<rid>.txt
      SHA256SUMS.txt
      package-manifest.json

`scripts/package-verify.ps1` is the acceptance gate and runs at the end unless
`-NoVerify` is passed. Cross-publishing works from any host (the runtime packs
are resolved by NuGet), so a Linux or macOS runner produces the same tree; the
only host-specific step in the verifier is the Windows CLI smoke run.

.PARAMETER Version
Bare semantic version written to every `VERSION` file. Defaults to the
`Version` MSBuild property (`Directory.Build.props`), so the packages and the
binaries cannot disagree.

.PARAMETER Rids
Runtime identifiers to publish (default: all four release RIDs).

.PARAMETER Output
Release-tree directory, relative to the repository root (default `dist`).

.PARAMETER Work
Intermediate publish directory (default `publish`, already git-ignored).

.PARAMETER SkipCore
Do not bundle the xemu core into the win-x64 frontend archive. Development
only: a release archive without the core cannot emulate anything.

.PARAMETER NoVerify
Do not run `scripts/package-verify.ps1` at the end.

.EXAMPLE
pwsh -File scripts/package.ps1
pwsh -File scripts/package.ps1 -Version 0.1.0 -Rids win-x64,linux-x64
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string[]]$Rids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64'),
    [string]$Output = 'dist',
    [string]$Work = 'publish',
    [switch]$SkipCore,
    [switch]$NoVerify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $supportedRids = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')
    # `pwsh -File` passes "-Rids win-x64,linux-x64" as one literal string, so
    # split commas here instead of surprising the caller with a bad RID.
    $Rids = @($Rids | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($rid in $Rids) {
        if ($supportedRids -notcontains $rid) { throw "unsupported RID '$rid' (expected: $($supportedRids -join ', '))" }
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        # One -getProperty prints the bare value; two or more print JSON.
        $probe = @(& dotnet msbuild src/XGrid.Cli/XGrid.Cli.csproj -getProperty:Version -nologo)
        if ($LASTEXITCODE -ne 0) { throw 'could not read the Version property with dotnet msbuild' }
        $text = ($probe -join "`n").Trim()
        $Version = if ($text.StartsWith('{', [StringComparison]::Ordinal)) {
            ([string]($text | ConvertFrom-Json).Properties.Version).Trim()
        }
        else { $text }
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+([-+][0-9A-Za-z.-]+)?$') {
        throw "version '$Version' is not a bare semantic version (no leading 'v')"
    }

    $notesPath = Join-Path $repo "docs/release-notes/v$Version.md"
    if (-not (Test-Path $notesPath)) { throw "missing release notes: docs/release-notes/v$Version.md" }

    $outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repo $Output }
    $workPath = if ([System.IO.Path]::IsPathRooted($Work)) { $Work } else { Join-Path $repo $Work }
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    # The output directory is this script's product: clear a previous run so a
    # subset build cannot leave a stale archive in the release tree.
    foreach ($stale in Get-ChildItem $outputPath -File |
        Where-Object { $_.Name -like 'XGrid-*' -or $_.Name -like 'SHA256SUMS*' -or $_.Name -eq 'package-manifest.json' }) {
        Remove-Item -Force $stale.FullName
    }

    $commit = (& git rev-parse HEAD).Trim()
    $dirty = @(& git status --porcelain | Where-Object { $_ }).Count -gt 0
    $dotnet = (& dotnet --version).Trim()
    Write-Host ("PACKAGE XGrid {0} @ {1}{2}" -f $Version, $commit.Substring(0, 12), $(if ($dirty) { ' (dirty tree)' } else { '' }))
    Write-Host ("  dotnet {0}, RIDs: {1}" -f $dotnet, ($Rids -join ', '))

    # Sorted entry order and a fixed timestamp make the archives byte-stable for
    # the same inputs, instead of carrying each build's file times.
    $fixedTime = [datetimeoffset]::new(1980, 1, 1, 0, 0, 0, [timespan]::Zero)
    function Get-ArchiveFiles([string]$Directory) {
        return @(Get-ChildItem $Directory -Recurse -File | Sort-Object { $_.FullName } -CaseSensitive)
    }

    function New-DeterministicZip([string]$Directory, [string]$Path) {
        if (Test-Path $Path) { Remove-Item -Force $Path }
        $stream = [System.IO.File]::Create($Path)
        try {
            $zip = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
            try {
                foreach ($file in Get-ArchiveFiles $Directory) {
                    $name = [System.IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
                    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::SmallestSize)
                    $entry.LastWriteTime = $fixedTime
                    $input = [System.IO.File]::OpenRead($file.FullName)
                    try {
                        $target = $entry.Open()
                        try { $input.CopyTo($target) } finally { $target.Dispose() }
                    }
                    finally { $input.Dispose() }
                }
            }
            finally { $zip.Dispose() }
        }
        finally { $stream.Dispose() }
    }

    function New-DeterministicTarGz([string]$Directory, [string]$Path) {
        if (Test-Path $Path) { Remove-Item -Force $Path }
        $stream = [System.IO.File]::Create($Path)
        try {
            $gzip = [System.IO.Compression.GZipStream]::new($stream, [System.IO.Compression.CompressionLevel]::SmallestSize)
            try {
                $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Ustar, $false)
                try {
                    foreach ($file in Get-ArchiveFiles $Directory) {
                        $name = [System.IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
                        $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $name)
                        $entry.ModificationTime = $fixedTime
                        $entry.Mode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::OtherRead
                        $entry.DataStream = [System.IO.File]::OpenRead($file.FullName)
                        try { $writer.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
                    }
                }
                finally { $writer.Dispose() }
            }
            finally { $gzip.Dispose() }
        }
        finally { $stream.Dispose() }
    }

    # The win-x64 frontend archive carries the emulator: the standalone core and
    # the in-process library, plus the DLLs both import. `XemuPaths` looks for it
    # at `core/xemu/` beside the application.
    $coreDist = Join-Path $repo 'xemu/dist'
    $coreExe = Join-Path $coreDist 'xemu.exe'
    $coreDll = Join-Path $coreDist 'xgrid_core.dll'
    $coreVersion = if (Test-Path (Join-Path $repo 'xemu/XEMU_VERSION')) {
        (Get-Content (Join-Path $repo 'xemu/XEMU_VERSION') -TotalCount 1).Trim()
    }
    else { '' }

    function Find-StripTool {
        foreach ($candidate in @('C:\msys64\mingw64\bin\strip.exe', (Join-Path $env:SystemDrive 'msys64/mingw64/bin/strip.exe'))) {
            if (Test-Path $candidate) { return $candidate }
        }
        $command = Get-Command strip -ErrorAction SilentlyContinue
        return if ($command) { $command.Source } else { $null }
    }

    function Copy-XemuCore([string]$Destination) {
        $coreDirectory = Join-Path $Destination 'core/xemu'
        New-Item -ItemType Directory -Path $coreDirectory -Force | Out-Null
        foreach ($file in Get-ChildItem $coreDist -File) {
            if ($file.Extension -in '.pdb', '.log', '.txt') { continue }
            Copy-Item $file.FullName (Join-Path $coreDirectory $file.Name) -Force
        }

        # The development build carries full debug information; a release
        # archive carries code. 92 MB per binary becomes about 17 MB.
        $strip = Find-StripTool
        if ($strip) {
            foreach ($name in 'xemu.exe', 'xgrid_core.dll') {
                & $strip --strip-all (Join-Path $coreDirectory $name) | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "strip failed for $name" }
            }
        }
        else {
            Write-Warning 'strip was not found; the archive carries the unstripped core'
        }
        $bytes = (Get-ChildItem $coreDirectory -File | Measure-Object Length -Sum).Sum
        Write-Host ("  core {0} -> core/xemu ({1:N1} MB)" -f $coreVersion, ($bytes / 1MB))
    }

    $bundleCore = ($Rids -contains 'win-x64') -and -not $SkipCore
    if ($bundleCore) {
        foreach ($required in $coreExe, $coreDll) {
            if (-not (Test-Path $required)) {
                throw "missing $required - run scripts/build-native-xemu.ps1 first (or pass -SkipCore for a development archive)"
            }
        }
    }

    $apps = @(
        [pscustomobject]@{ Name = 'cli'; Project = 'src/XGrid.Cli/XGrid.Cli.csproj'; Exe = 'XGrid.Cli' },
        [pscustomobject]@{ Name = 'frontend'; Project = 'src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj'; Exe = 'XGrid.Frontend.Avalonia' }
    )

    $archives = [System.Collections.Generic.List[object]]::new()
    foreach ($rid in $Rids) {
        $isWindowsRid = $rid.StartsWith('win-', [StringComparison]::Ordinal)
        $extension = if ($isWindowsRid) { '.zip' } else { '.tar.gz' }
        $sums = [System.Collections.Generic.List[string]]::new()

        foreach ($app in $apps) {
            $publishDirectory = Join-Path $workPath "$($app.Name)/$rid"
            if (Test-Path $publishDirectory) { Remove-Item -Recurse -Force $publishDirectory }
            Write-Host ("PUBLISH {0} {1}" -f $app.Name, $rid)
            & dotnet publish $app.Project -c Release -r $rid --self-contained true `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version `
                -o $publishDirectory --nologo
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($app.Name) $rid" }

            foreach ($symbol in Get-ChildItem $publishDirectory -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.xml' }) {
                Remove-Item -Force $symbol.FullName
            }
            Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'COPYING'), (Join-Path $repo 'THIRD_PARTY_NOTICES.md'), (Join-Path $repo 'README.md') -Destination $publishDirectory -Force
            Copy-Item (Join-Path $repo 'licenses') -Destination $publishDirectory -Recurse -Force
            Copy-Item $notesPath (Join-Path $publishDirectory 'RELEASE-NOTES.md') -Force
            Set-Content -Path (Join-Path $publishDirectory 'VERSION') -Value $Version -NoNewline -Encoding utf8NoBOM

            $exeName = if ($isWindowsRid) { "$($app.Exe).exe" } else { $app.Exe }
            $exePath = Join-Path $publishDirectory $exeName
            if (-not (Test-Path $exePath)) { throw "publish output has no $exeName for $($app.Name) $rid" }

            if ($bundleCore -and $app.Name -eq 'frontend') {
                Copy-XemuCore $publishDirectory
            }

            $file = "XGrid-$($app.Name)-$rid$extension"
            $archivePath = Join-Path $outputPath $file
            if ($isWindowsRid) { New-DeterministicZip $publishDirectory $archivePath }
            else { New-DeterministicTarGz $publishDirectory $archivePath }

            $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $bytes = (Get-Item $archivePath).Length
            $sums.Add("$hash  $file")
            $archives.Add([pscustomobject]@{ file = $file; app = $app.Name; rid = $rid; bytes = $bytes; sha256 = $hash })
            Write-Host ("  {0}  {1:N1} MB  {2}" -f $file, ($bytes / 1MB), $hash.Substring(0, 16))
        }

        $sums | Set-Content -Path (Join-Path $outputPath "SHA256SUMS-$rid.txt") -Encoding utf8NoBOM
    }

    $manifest = [ordered]@{
        product = 'XGrid'
        version = $Version
        commit = $commit
        dirty = $dirty
        dotnet = $dotnet
        rids = @($Rids)
        core = if ($bundleCore) {
            [ordered]@{
                name = 'xemu'
                version = $coreVersion
                commit = if (Test-Path (Join-Path $repo 'xemu/XEMU_COMMIT')) {
                    (Get-Content (Join-Path $repo 'xemu/XEMU_COMMIT') -TotalCount 1).Trim()
                }
                else { '' }
                path = 'core/xemu'
                rids = @('win-x64')
            }
        }
        else { $null }
        archives = @($archives)
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $outputPath 'package-manifest.json') -Encoding utf8NoBOM

    $combined = @($archives | Sort-Object { $_.file } -CaseSensitive | ForEach-Object { "$($_.sha256)  $($_.file)" })
    $combined | Set-Content -Path (Join-Path $outputPath 'SHA256SUMS.txt') -Encoding utf8NoBOM

    Write-Host ("PACKAGE WROTE {0} archives to {1}" -f $archives.Count, $outputPath)
    if (-not $NoVerify) {
        & (Join-Path $PSScriptRoot 'package-verify.ps1') -Dist $outputPath -Version $Version -Rids $Rids
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
