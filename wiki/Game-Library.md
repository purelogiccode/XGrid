# Game library

The Library scans folders recursively and launches disc images. It never copies
or modifies your files.

## Add folders

Use **View → Add library folder…**, the empty-state **Add folder** button, or
**Settings → Library → Add folder**.

A scan:

- follows subdirectories;
- skips inaccessible folders and reparse points;
- stops at 4,096 entries;
- de-duplicates paths;
- reads only file metadata.

## Toolbar

- **Search games…** filters by title, path, format, title id, region, year,
  publisher or description.
- **All games / Favorites / Played / Unplayed** filters the collection.
- **Title / Recently played / Most played / Release year / Publisher** sorts.
- **Icon size** changes the tile width from 128 to 384 pixels.
- **Tiles** and **List** switch layouts.

Select a game and **double-click** it (or press **Enter**) to boot it. A single
click only selects. The tile/list choice is saved in `settings.json`.

## Favorites and play time

- Click the star on a card or row to favorite a game.
- XGrid records accumulated play time, launch count and last-played time when a
  game starts and when the console stops.
- **Recently played**, **Most played**, **Played** and **Unplayed** use this
  state.
- State follows the XBE title id once XGrid has seen the game launch; otherwise
  it uses the image path. The resolved id is cached so later scans stay fast.

The old recent strip is gone; use the **Recently played** sort.

## Supported files

| Extension | Listed | Launchable |
| --- | --- | --- |
| `.iso` | Yes | Yes |
| `.chd` | Yes | Yes |
| `.zar` | Yes | Yes (entry form or file tree) |
| `.xbe` | Yes | No (inspection only) |

## Cover art

Covers live in a folder you choose, never beside the disc. The default is:

```text
<data>/covers/
```

Matching order:

1. A file named after the title ID, e.g. `4D530002.png`.
2. An exact base name.
3. An annotation-stripped exact name (`(USA)`, `(Europe)`, `(En,Fr,De)`, `[!]`
   and `Rev 1` are ignored).
4. Token-order and compound-word forms.
5. A catalog title-id or exact-title match.
6. Local fuzzy Jaro-Winkler matching (default threshold 0.85).
7. Catalog fuzzy matching.
8. Subtitle aliases.

Sequel numbers are protected: `Halo` does not match `Halo 2`. Region and year
break ties. PNG, JPEG, WebP and BMP are supported. A missing image falls back to
the disc placeholder.

### Online covers

The optional R2 catalog uses the same content-addressed layout as 5DO:

```text
https://assets.purelogiccode.com/xgrid/v1/catalog.json
```

This is the live default endpoint. The 2026-11-26 pack contains 1,632
deduplicated covers. If the catalog is unavailable, local covers keep working;
a self-hosted or `file://` catalog works instead.

XGrid caches the catalog and images under `<data>/covers-cache/`, revalidates
with `If-None-Match`, verifies every image by SHA-256, and downloads only covers
matched to games in your library. Local covers keep working offline.

Configure it under **Settings → Library → Cover art**: local folder, online
toggle, automatic downloads, catalog URL, fuzzy matching, annotation stripping
and threshold. **Refresh** fetches the catalog now; **Clear cache** deletes the
cache.

## Titles and overrides

- Loose XBE files show their certificate title.
- Disc images use the file name as the display title, with annotation groups
  removed for the card.
- When readable, the XBE title ID (`Title XXXXXXXX`) keys per-title overrides.
- Otherwise the image path keys the override.
- An override can set guest memory, the renderer, texture filtering and the
  presentation filter; unset values inherit the global configuration.

## Launch

Double-click a tile or press **Enter** on the selection. XGrid applies the
global settings plus any matching per-title override.

While the console runs, choosing a different game inserts that disc instead of
power-cycling. **Ctrl+E** ejects. Both need in-process core hosting.

## Missing games

1. Check the extension.
2. Confirm the folder exists and is in the list.
3. Press **Rescan** or use **View → Rescan library**.
4. Read **Activity** for scan errors.

For boot failures, see [Media formats](Media-Formats) and
[Troubleshooting](Troubleshooting).

## Screenshots

**F8** saves the console picture. Open the folder with **View → Open screenshots
folder**; the Library no longer embeds a screenshot strip.
