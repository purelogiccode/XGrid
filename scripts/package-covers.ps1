param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [string] $OutputDirectory,
    [string] $PackVersion = (Get-Date -AsUTC -Format 'yyyy.MM.dd'),
    [int] $Quality = 85,
    [int] $MaxHeight = 600,
    [int] $ThrottleLimit = [Environment]::ProcessorCount,
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell 7 (pwsh) is required for parallel cover encoding.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/covers/v1'
}
if ($ThrottleLimit -le 0) {
    $ThrottleLimit = [Environment]::ProcessorCount
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source image directory not found: $SourceDirectory"
}
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg is required. Install ffmpeg and make it available on PATH.'
}

$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputRoot = [System.IO.Path]::GetPathRoot($output)
if ([string]::Equals($output.TrimEnd('\', '/'), $outputRoot.TrimEnd('\', '/'),
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a filesystem root as the output directory: $output"
}

$imagesDirectory = Join-Path $output 'images'
if ($Clean -and (Test-Path -LiteralPath $output)) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
[void](New-Item -ItemType Directory -Force -Path $imagesDirectory)

$extensions = @('.png', '.jpg', '.jpeg', '.bmp')
$files = @(Get-ChildItem -LiteralPath $source -File |
    Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() } |
    Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No PNG, JPEG or BMP images found in $source"
}

Write-Host "Packing $($files.Count) source images with $ThrottleLimit workers..."
$started = Get-Date
$results = @(
    $files | ForEach-Object -Parallel {
        $file = $_
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
        $temporary = $null
        try {
            Add-Type -AssemblyName System.Drawing
            $image = [System.Drawing.Image]::FromFile($file.FullName)
            try {
                $sourceWidth = $image.Width
                $sourceHeight = $image.Height
            }
            finally {
                $image.Dispose()
            }

            if ($sourceWidth -le 0 -or $sourceHeight -le 0) {
                throw 'The image has no usable dimensions.'
            }

            $targetHeight = [Math]::Min($sourceHeight, $using:MaxHeight)
            $targetWidth = [int][Math]::Round(
                $sourceWidth * ($targetHeight / [double]$sourceHeight))
            if ($targetWidth -lt 2) {
                $targetWidth = 2
            }
            if (($targetWidth % 2) -ne 0) {
                $targetWidth--
            }

            $temporary = Join-Path $using:imagesDirectory (
                'tmp-' + [Guid]::NewGuid().ToString('N') + '.webp')
            $ffmpeg = @(
                '-hide_banner', '-loglevel', 'error', '-y',
                '-i', $file.FullName,
                '-map_metadata', '-1',
                '-an',
                '-vf', "scale=${targetWidth}:${targetHeight}:flags=lanczos",
                '-c:v', 'libwebp',
                '-quality', $using:Quality,
                '-compression_level', '6',
                '-preset', 'picture',
                '-threads', '1',
                $temporary
            )
            $null = & ffmpeg @ffmpeg 2>&1
            if ($LASTEXITCODE -ne 0 -or
                -not (Test-Path -LiteralPath $temporary) -or
                (Get-Item -LiteralPath $temporary).Length -eq 0) {
                throw "ffmpeg failed with exit code $LASTEXITCODE."
            }

            $id = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).
                Hash.ToLowerInvariant()
            $destination = Join-Path $using:imagesDirectory ($id + '.webp')
            if (-not (Test-Path -LiteralPath $destination)) {
                try {
                    [System.IO.File]::Move($temporary, $destination, $true)
                }
                catch {
                    if (-not (Test-Path -LiteralPath $destination)) {
                        throw
                    }
                }
            }
            if (Test-Path -LiteralPath $temporary) {
                Remove-Item -LiteralPath $temporary -Force
            }
            $temporary = $null
            $size = (Get-Item -LiteralPath $destination).Length

            # Parse the same Redump annotation groups the shell's matcher uses.
            $regionTokens = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($region in @(
                'USA', 'US', 'Europe', 'EU', 'Japan', 'JP', 'Asia', 'Australia',
                'Korea', 'Brazil', 'Canada', 'China', 'Taiwan', 'World', 'France',
                'Germany', 'Spain', 'Italy', 'Netherlands', 'Sweden', 'Denmark',
                'Norway', 'Finland', 'Poland', 'Russia', 'Mexico', 'New Zealand',
                'Portugal', 'Greece', 'Ireland', 'UK', 'United Kingdom')) {
                [void]$regionTokens.Add($region)
            }

            $year = ''
            $rank = 0
            $regions = [System.Collections.Generic.List[string]]::new()
            $groups = [regex]::Matches(
                $stem, '\(([^()]*)\)|\[([^\[\]]*)\]|\{([^{}]*)\}')
            foreach ($match in $groups) {
                $group = if ($match.Groups[1].Success) {
                    $match.Groups[1].Value
                }
                elseif ($match.Groups[2].Success) {
                    $match.Groups[2].Value
                }
                else {
                    $match.Groups[3].Value
                }
                $group = $group.Trim()

                if ($year.Length -eq 0 -and $group -match '^(19|20)\d{2}$') {
                    $year = $group
                    continue
                }
                if ($group -match '(?i)demo|sample|preview') {
                    $rank = [Math]::Max($rank, 1)
                }
                if ($group -match '(?i)beta|alpha|proto') {
                    $rank = [Math]::Max($rank, 2)
                }
                foreach ($token in ($group -split '[,/;]')) {
                    $trimmed = $token.Trim()
                    if ($regionTokens.Contains($trimmed) -and
                        -not $regions.Contains($trimmed)) {
                        $regions.Add($trimmed)
                    }
                }
            }

            $title = [regex]::Replace(
                $stem, '\([^()]*\)|\[[^\[\]]*\]|\{[^{}]*\}', '').
                Trim().
                TrimEnd([char[]]@('.', '_', ' '))
            if ([string]::IsNullOrWhiteSpace($title)) {
                $title = $stem
            }

            [pscustomobject]@{
                Source  = $file.FullName
                Stem    = $stem
                Title   = $title
                Regions = $regions.ToArray()
                Year    = $year
                Rank    = $rank
                Id      = $id
                File    = $id + '.webp'
                Width   = $targetWidth
                Height  = $targetHeight
                Size    = $size
                Error   = $null
            }
        }
        catch {
            if ($temporary -and (Test-Path -LiteralPath $temporary)) {
                Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            }
            [pscustomobject]@{
                Source  = $file.FullName
                Stem    = $stem
                Title   = $stem
                Regions = @()
                Year    = ''
                Rank    = 0
                Id      = ''
                File    = ''
                Width   = 0
                Height  = 0
                Size    = 0
                Error   = $_.Exception.Message
            }
        }
    } -ThrottleLimit $ThrottleLimit
)

$elapsed = (Get-Date) - $started
$success = @($results | Where-Object { -not $_.Error })
$failures = @($results | Where-Object { $_.Error })
if ($success.Count -eq 0) {
    throw 'No cover images were encoded. See the errors above.'
}

$covers = [System.Collections.Generic.List[object]]::new()
foreach ($group in ($success | Group-Object -Property Id)) {
    $rows = @($group.Group)
    $best = $rows |
        Sort-Object @{ Expression = 'Rank'; Ascending = $true },
                    @{ Expression = { $_.Title.Length }; Ascending = $true },
                    @{ Expression = 'Title'; Ascending = $true } |
        Select-Object -First 1
    $aliases = @($rows | ForEach-Object { $_.Stem } | Sort-Object -Unique)
    $regions = @($rows | ForEach-Object { $_.Regions } | Sort-Object -Unique)
    $yearEntry = @($rows | Where-Object { $_.Year } | Select-Object -First 1)
    $year = if ($yearEntry.Count -gt 0) { $yearEntry[0].Year } else { '' }
    $rank = [int](($rows | Measure-Object -Property Rank -Minimum).Minimum)
    $covers.Add([pscustomobject]@{
        id       = $group.Name
        file     = $group.Name + '.webp'
        name     = $best.Title
        aliases  = $aliases
        gameIds  = @()
        width    = $best.Width
        height   = $best.Height
        size     = $best.Size
        regions  = $regions
        year     = $year
        rank     = $rank
    })
}
$covers = @($covers | Sort-Object name, id)

$generatedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$catalog = [pscustomobject]@{
    schemaVersion = 1
    packVersion   = $PackVersion
    generatedUtc  = $generatedUtc
    noticeUrl     = 'https://github.com/purelogiccode/XGrid'
    imagesBase    = 'images/'
    packFile      = ''
    packSize      = 0
    packSha256    = ''
    covers        = $covers
}
$totalBytes = ($covers | Measure-Object -Property size -Sum).Sum
$report = [pscustomobject]@{
    packVersion    = $PackVersion
    generatedUtc   = $generatedUtc
    sourceDirectory = $source
    sourceFiles    = $files.Count
    uniqueImages   = $covers.Count
    duplicateFiles = $success.Count - $covers.Count
    encodedBytes   = $totalBytes
    failures       = $failures
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    (Join-Path $output 'catalog.json'),
    ($catalog | ConvertTo-Json -Depth 10),
    $utf8)
[System.IO.File]::WriteAllText(
    (Join-Path $output 'report.json'),
    ($report | ConvertTo-Json -Depth 10),
    $utf8)
$sums = @($covers | ForEach-Object { "$($_.id)  images/$($_.id).webp" })
[System.IO.File]::WriteAllText(
    (Join-Path $output 'SHA256SUMS.txt'),
    (($sums -join "`n") + "`n"),
    [System.Text.Encoding]::ASCII)

Write-Host ("Encoded {0} source images into {1} unique covers in {2:0.0}s." -f
    $files.Count, $covers.Count, $elapsed.TotalSeconds)
Write-Host ("Output: {0}" -f $output)
Write-Host ("Catalog: {0}" -f (Join-Path $output 'catalog.json'))
Write-Host ("Images:  {0}" -f $imagesDirectory)
Write-Host ("Unique bytes: {0:N1} MB" -f ($totalBytes / 1MB))
if ($failures.Count -gt 0) {
    throw ("{0} source images failed to encode. See {1}." -f
        $failures.Count, (Join-Path $output 'report.json'))
}
