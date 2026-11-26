# Cover catalog packaging

The online cover catalog is a static pack:

```text
catalog.json
images/<sha256>.webp
SHA256SUMS.txt
report.json
```

`catalog.json` is schema version 1, `imagesBase` is `images/`, and every image
id is the SHA-256 of the encoded WebP bytes. The shell verifies each downloaded
image against its id before caching it.

## Requirements

- PowerShell 7 (`pwsh`)
- `ffmpeg` on `PATH` (libwebp encoder)
- `rclone`, with an R2 remote named `r2`

The packager runs on Windows; the source collection is read only.

## Build

```powershell
pwsh -File scripts/package-covers.ps1 `
  -SourceDirectory "D:\Emulators\Simple Launcher\images\Microsoft Xbox"
```

The default output is `artifacts/covers/v1/`. Useful options:

| Option | Meaning |
| --- | --- |
| `-OutputDirectory` | Output root. |
| `-PackVersion` | Catalog label; defaults to the UTC date. |
| `-Quality` | WebP quality (default 85). |
| `-MaxHeight` | Maximum encoded height (default 600; smaller images are not upscaled). |
| `-ThrottleLimit` | Parallel ffmpeg workers (default processor count). |
| `-Clean` | Delete the output root before writing. Never use this against R2. |

The packager:

1. reads the source PNG/JPEG/BMP images;
2. scales to at most 600 px high and encodes WebP q85 with ffmpeg;
3. hashes the encoded bytes and deduplicates identical covers;
4. parses `(USA)`, `(Europe)`, `(En,Fr,De)`, `(Rev 1)` and edition groups into
   regions, year and rank;
5. keeps every source file stem as an alias;
6. writes `catalog.json`, `images/`, `SHA256SUMS.txt` and `report.json`.

A nonzero exit means at least one source image failed; inspect
`report.json` before publishing. `catalog.json` is not written for a partial
pack.

`gameIds` are empty in this pass. The client still matches by exact and fuzzy
title/alias, which covers the Redump-style names. A later packager pass can add
XBE title ids for the exact game-id lookup.

## Publish

Preview first:

```powershell
pwsh -File scripts/publish-covers.ps1 -DryRun
```

Then publish:

```powershell
pwsh -File scripts/publish-covers.ps1
```

The default remote is:

```text
r2:simplelauncher-thumbnails/xgrid/v1
```

Override it with `-Remote`. The script uploads `images/` with
`Cache-Control: public, max-age=31536000, immutable`, and the catalog/checksum/
report with a five-minute cache. The public catalog is:

```text
https://assets.purelogiccode.com/xgrid/v1/catalog.json
```

Old images must stay available: clients with a stale catalog keep requesting
their hashed names. The packager already keeps old output images unless
`-Clean` is used; do not delete objects from R2 when publishing a new pack.

## Verify

```powershell
rclone ls r2:simplelauncher-thumbnails/xgrid/v1
```

Or open the public catalog URL and check the `packVersion`, `covers` and
`imagesBase` fields. In XGrid, Settings → Library → Cover art → **Refresh**,
then rescan the library.

For the 2026-11-26 pack, 2,288 source images deduplicated to 1,632 covers and
68.2 MB of WebP. A library of 945 games downloaded 880 matches from the catalog.
