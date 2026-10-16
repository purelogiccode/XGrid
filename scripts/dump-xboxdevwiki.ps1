$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\References2\XboxDevWiki'))
$Api  = 'https://xboxdevwiki.net/api.php'
$Base = 'https://xboxdevwiki.net'
$PagesDir = Join-Path $Root 'pages'
$HtmlDir  = Join-Path $Root 'html'
$ImgDir   = Join-Path $Root 'images'
foreach ($d in @($PagesDir, $HtmlDir, $ImgDir)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

$DumpedAt = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

function Get-SafeName([string]$Name) {
    $s = $Name -replace '[<>:"/\\|?*]', '_'
    $s = $s.Trim()
    if ($s.Length -gt 120) { $s = $s.Substring(0, 120) }
    return $s
}

function Invoke-Download([string]$Url, [string]$Dest) {
    for ($try = 1; $try -le 4; $try++) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $Dest -TimeoutSec 90
            return
        } catch {
            if ($try -eq 4) { throw }
            Start-Sleep -Seconds (2 * $try)
        }
    }
}

function Invoke-Api([string]$Url) {
    for ($try = 1; $try -le 4; $try++) {
        try {
            return Invoke-RestMethod -Uri $Url -TimeoutSec 90
        } catch {
            if ($try -eq 4) { throw }
            Start-Sleep -Seconds (2 * $try)
        }
    }
}

# --- 1) enumerate all articles (namespace 0) ---
$titles = [System.Collections.Generic.List[string]]::new()
$cont = $null
do {
    $url = "$Api`?action=query&list=allpages&aplimit=500&apfilterredir=all&format=json&formatversion=2"
    if ($cont) { $url += "&apcontinue=$([uri]::EscapeDataString($cont))" }
    $r = Invoke-Api $url
    foreach ($p in $r.query.allpages) { $titles.Add($p.title) }
    $cont = if ($r.continue) { $r.continue.apcontinue } else { $null }
} while ($cont)
Write-Host "Enumerated $($titles.Count) articles"

# --- 2) enumerate and download all images ---
$images = [System.Collections.Generic.List[object]]::new()
$cont = $null
do {
    $url = "$Api`?action=query&list=allimages&ailimit=500&aiprop=url|size|sha1|timestamp&format=json&formatversion=2"
    if ($cont) { $url += "&aicontinue=$([uri]::EscapeDataString($cont))" }
    $r = Invoke-Api $url
    foreach ($i in $r.query.allimages) { $images.Add($i) }
    $cont = if ($r.continue) { $r.continue.aicontinue } else { $null }
} while ($cont)
Write-Host "Enumerated $($images.Count) images"

$imgLocalExact = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
$imgLocalLower = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
$imgNameUse = @{}
foreach ($img in ($images | Sort-Object name)) {
    $fn = Get-SafeName $img.name
    $k = $fn.ToLowerInvariant()
    if ($imgNameUse.ContainsKey($k)) {
        $imgNameUse[$k]++
        $fn = "$fn ($($imgNameUse[$k]))"
    } else { $imgNameUse[$k] = 1 }
    $imgLocalExact[$img.name] = $fn
    $lk = $img.name.ToLowerInvariant()
    if ($imgLocalLower.ContainsKey($lk)) { $imgLocalLower[$lk] = $null } else { $imgLocalLower[$lk] = $fn }
    $dest = Join-Path $ImgDir $fn
    if (-not (Test-Path -LiteralPath $dest) -or (Get-Item -LiteralPath $dest).Length -eq 0) {
        Invoke-Download $img.url $dest
        Start-Sleep -Milliseconds 60
    }
}

# --- 3) assign unique file names for article pages (case-insensitive FS) ---
$titleLocalExact = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
$titleLocalLower = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
$titleNameUse = @{}
foreach ($t in ($titles | Sort-Object)) {
    $baseName = Get-SafeName $t
    $k = $baseName.ToLowerInvariant()
    if ($titleNameUse.ContainsKey($k)) {
        $titleNameUse[$k]++
        $baseName = "$baseName ($($titleNameUse[$k]))"
    } else { $titleNameUse[$k] = 1 }
    $titleLocalExact[$t] = $baseName
    $lk = $t.ToLowerInvariant()
    if ($titleLocalLower.ContainsKey($lk)) { $titleLocalLower[$lk] = $null } else { $titleLocalLower[$lk] = $baseName }
}

function Resolve-PageFile([string]$Target) {
    if ($titleLocalExact.ContainsKey($Target)) { return $titleLocalExact[$Target] }
    $lk = $Target.ToLowerInvariant()
    if ($titleLocalLower.ContainsKey($lk)) { return $titleLocalLower[$lk] }
    return $null
}

function Resolve-ImageFile([string]$MediaName) {
    if ($imgLocalExact.ContainsKey($MediaName)) { return $imgLocalExact[$MediaName] }
    $lk = $MediaName.ToLowerInvariant()
    if ($imgLocalLower.ContainsKey($lk)) { return $imgLocalLower[$lk] }
    return $null
}

$css = @'
body{font-family:-apple-system,Segoe UI,Arial,sans-serif;max-width:62em;margin:1em auto;padding:0 1.5em;line-height:1.55;color:#202122}
a{color:#0645ad}
h1,h2,h3,h4{border-bottom:1px solid #a2a9b1;padding-bottom:.15em;font-weight:normal}
table{border-collapse:collapse;margin:.5em 0;max-width:100%}
td,th{border:1px solid #a2a9b1;padding:.25em .5em;font-size:.95em;vertical-align:top}
th{background:#eaecf0}
pre,code,tt{background:#f8f9fa;border:1px solid #eaecf0;font-family:Consolas,monospace;font-size:.9em}
pre{padding:.6em;overflow-x:auto}
code,tt{padding:.05em .25em}
.thumb{float:right;clear:right;margin:.4em 0 .8em 1em;border:1px solid #c8ccd1;background:#f8f9fa;padding:.25em;max-width:100%}
.thumb.tleft{float:left;margin:.4em 1em .8em 0}
.thumb img{max-width:100%;height:auto;display:block}
.thumbcaption{font-size:.85em;padding:.2em}
.magnify{display:none}
#toc{display:inline-block;background:#f8f9fa;border:1px solid #a2a9b1;padding:.5em 1em;font-size:.9em;margin:.5em 0}
#toc table{border:0;margin:0}
#toc td,#toc th{border:0;padding:.05em .3em}
#toc ul{margin:0;padding-left:1.2em}
#toc .toctitle{text-align:center}
#dumphdr{background:#eaf3ff;border:1px solid #a2a9b1;padding:.4em .8em;margin-bottom:1em;font-size:.85em;border-radius:2px}
.mw-empty-elt{display:none}
'@

# --- 4) download every article: raw wikitext + rendered standalone HTML ---
$pageManifest = [System.Collections.Generic.List[object]]::new()
$n = 0
foreach ($title in $titles) {
    $n++
    $esc = [uri]::EscapeDataString($title)
    $baseName = $titleLocalExact[$title]
    $wikiFile = Join-Path $PagesDir ($baseName + '.wiki')
    $htmlFile = Join-Path $HtmlDir ($baseName + '.html')

    Invoke-Download "$Base/index.php?title=$esc&action=raw" $wikiFile
    Start-Sleep -Milliseconds 60

    $parseUrl = "$Api`?action=parse&page=$esc&prop=text&format=json&formatversion=2&disableeditsection=1"
    $pr = Invoke-Api $parseUrl
    $html = $pr.parse.text
    $revid = $pr.parse.revid
    $ts = $pr.parse.timestamp

    $html = [regex]::Replace($html, '\s+srcset="[^"]*"', '')
    $html = [regex]::Replace($html, 'src="([^"]+)"', {
        param($m)
        $u = $m.Groups[1].Value
        if ($u -match '^(https://xboxdevwiki-images\.storage\.googleapis\.com/|/images/)') {
            $leaf = [uri]::UnescapeDataString(($u -split '/')[-1]) -replace '^\d+px-', ''
            $hit = Resolve-ImageFile $leaf
            if ($hit) { return 'src="../images/' + [System.Net.WebUtility]::HtmlEncode($hit) + '"' }
        }
        return $m.Value
    })
    $html = [regex]::Replace($html, 'href="([^"]+)"', {
        param($m)
        $u = $m.Groups[1].Value
        $raw = $null
        $frag = ''
        if ($u -match '^/index\.php\?title=([^&"#]+)(#[^"]*)?') { $raw = $matches[1]; $frag = $matches[2] }
        elseif ($u -match '^/([^?#]+)(#[^"]*)?$') { $raw = $matches[1]; $frag = $matches[2] }
        if (-not $raw) { return $m.Value }
        $t = ([uri]::UnescapeDataString($raw) -replace '_', ' ').Trim()
        if ($t.ToLowerInvariant().StartsWith('file:')) {
            $media = $t.Substring(5).Trim()
            $hit = Resolve-ImageFile $media
            if ($hit) { return 'href="../images/' + [System.Net.WebUtility]::HtmlEncode($hit) + '"' }
            return 'href="' + $Base + $u + '"'
        }
        $hit = Resolve-PageFile $t
        if ($hit) { return 'href="' + [System.Net.WebUtility]::HtmlEncode($hit) + '.html' + $frag + '"' }
        return 'href="' + $Base + $u + '"'
    })

    $titleHtml = [System.Net.WebUtility]::HtmlEncode($title)
    $doc = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>$titleHtml - XboxDevWiki</title>
<style>
$css
</style>
</head>
<body>
<div id="dumphdr">Offline dump of <a href="https://xboxdevwiki.net/$esc">xboxdevwiki.net/$titleHtml</a> &mdash; revision $revid ($ts) &mdash; captured $DumpedAt</div>
<h1>$titleHtml</h1>
<div class="mw-parser-output">
$html
</div>
</body>
</html>
"@
    [System.IO.File]::WriteAllText($htmlFile, $doc, [System.Text.UTF8Encoding]::new($false))

    $pageManifest.Add([pscustomobject]@{
        title     = $title
        revid     = $revid
        timestamp = $ts
        wikitext  = "pages/$baseName.wiki"
        html      = "html/$baseName.html"
    })
    Start-Sleep -Milliseconds 60
    if ($n % 20 -eq 0) { Write-Host "  $n / $($titles.Count) pages" }
}

$imgManifest = foreach ($img in ($images | Sort-Object name)) {
    [pscustomobject]@{
        name      = $img.name
        file      = "images/$($imgLocalExact[$img.name])"
        url       = $img.url
        size      = $img.size
        sha1      = $img.sha1
        timestamp = $img.timestamp
    }
}

$manifest = [pscustomobject]@{
    source    = 'https://xboxdevwiki.net/Main_Page'
    api       = 'https://xboxdevwiki.net/api.php'
    captured  = $DumpedAt
    pages     = $pageManifest
    images    = $imgManifest
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $Root 'manifest.json'), $manifestJson, [System.Text.UTF8Encoding]::new($false))

# --- 5) human-readable index ---
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<!DOCTYPE html>')
[void]$sb.AppendLine('<html lang="en"><head><meta charset="utf-8"><title>XboxDevWiki offline dump</title>')
[void]$sb.AppendLine('<style>body{font-family:-apple-system,Segoe UI,Arial,sans-serif;max-width:60em;margin:1em auto;padding:0 1.5em}a{color:#0645ad}li{margin:.1em 0}</style></head><body>')
[void]$sb.AppendLine("<h1>XboxDevWiki offline dump</h1>")
[void]$sb.AppendLine("<p>Source: <a href=""https://xboxdevwiki.net/Main_Page"">xboxdevwiki.net</a> &mdash; captured $DumpedAt &mdash; $($titles.Count) articles, $($images.Count) images.</p>")
[void]$sb.AppendLine('<p>Each article exists as rendered HTML (<code>html/</code>) and raw wikitext (<code>pages/</code>).</p><ul>')
foreach ($p in ($pageManifest | Sort-Object title)) {
    [void]$sb.AppendLine(('<li><a href="' + [System.Net.WebUtility]::HtmlEncode($p.html) + '">' + [System.Net.WebUtility]::HtmlEncode($p.title) + '</a></li>'))
}
[void]$sb.AppendLine('</ul></body></html>')
[System.IO.File]::WriteAllText((Join-Path $Root 'index.html'), $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

$wikiCount = (Get-ChildItem -LiteralPath $PagesDir -File).Count
$htmlCount = (Get-ChildItem -LiteralPath $HtmlDir -File).Count
$imgCount  = (Get-ChildItem -LiteralPath $ImgDir -File).Count
Write-Host "DONE: $wikiCount wikitext, $htmlCount html, $imgCount images"
