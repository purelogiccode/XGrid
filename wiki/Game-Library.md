# Game library

The Library scans configured folders and launches disc images. It never copies or
modifies your files.

## Add folders

Use **View → Add library folder…**, **Add folder** in the empty state, or
**Settings → Library → Add folder**. Scans are recursive, skip inaccessible
folders and reparse points, read metadata only, and stop at **4,096 entries** per
scan.

## Recognized files

| Extension | Launch | Notes |
| --- | --- | --- |
| `.iso`, `.xiso` | Yes | Optimized XISO or full Redump. |
| `.chd` | Yes | Real-time decode. |
| `.cci` | Yes | Single or split set; list shows the first part once. |
| `.zar` | Yes | Entry form or file-tree form. |
| `.bin` | Chihiro | Sega Chihiro netboot image. |
| `.xbe` | No | Inspection only; wrap it in an image. |

## Search, filters and sorts

- Search by title, path, format, title ID, region, year, publisher, developer,
  genre or description.
- Filter: **All games**, **Favorites**, **Played**, **Unplayed**.
- Sort: **Title**, **Recently played**, **Most played**, **Release year**,
  **Publisher**.
- Switch **Tiles** / **List** and resize tiles from 128 to 384 px.

Double-click a card, or select and press **Enter**, to launch. A single click
only selects it.

## Favorites and play time

Click the star to favorite a game. XGrid records accumulated play time, launch
count and last-played time. State is keyed by XBE title ID when known, otherwise
by image path; the first launch resolves the ID and migrates the entry.

## Covers

Local covers live in `<data>/covers/` by default. XGrid matches a title-ID file
(`4D530002.png`), exact name, annotation-stripped name, token forms, fuzzy
Jaro-Winkler (default 0.85) and subtitle aliases. PNG, JPEG, WebP and BMP work.

The optional online catalog is content-addressed WebP on Cloudflare R2. It
downloads only matched covers, verifies each SHA-256, and keeps local covers
working offline. Configure it under **Settings → Library → Cover art**.

## Launching and disc swap

If the console is stopped, launching a title powers it on with that disc. If it
is running, XGrid inserts the disc into the running machine (in-process hosting
only). **Ctrl+E** ejects; **F5** powers off.

Per-title overrides live under **Settings → Titles** and are keyed by title ID
when readable, otherwise by image path.

Full guide: [Game library](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/library.md).
