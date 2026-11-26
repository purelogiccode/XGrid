# Game library

The Library is a folder scanner and launcher. It does not copy or modify your
images.

## Adding folders

Use either entry point:

- **View → Add library folder…**
- **Add folder** in the empty state
- **Settings → Library → Add folder**

Folders are scanned recursively. A scan:

- follows subdirectories;
- skips inaccessible folders and reparse points;
- stops at **4,096 entries**;
- de-duplicates paths;
- reads only file metadata (name, size, format). It does **not** hash the
  images, open the disc, or decompress a CHD.

Add as many roots as you need. Removing a folder from the list does not delete
anything on disk.

## Toolbar

The library toolbar follows the 5DO layout:

- **Search games…** filters as you type by title, path, format, title id,
  region, year, publisher or description.
- **All games / Favorites / Played / Unplayed** filters the collection.
- **Title / Recently played / Most played / Release year / Publisher** sorts
  the collection. Recently played and Most played use the play time recorded by
  XGrid.
- **Icon size** changes the tile size from 128 to 384 pixels.
- **Tiles** and **List** switch between the cover grid and the detailed rows.

Select a game and **double-click** it (or press **Enter**) to boot it. A single
click only selects it. The chosen tile/list mode is saved in `settings.json`.

## Favorites and play time

- Click the star on a card or row to add or remove a favorite.
- XGrid records the accumulated play time, launch count and last-played time
  when a game starts and when the console stops.
- The **Recently played** and **Most played** sorts, and the **Played** and
  **Unplayed** filters, use this state.
- State is keyed by the XBE certificate title id when it is known, otherwise by
  the image path. The first launch resolves the title id and migrates the
  favorite/play-time entry, so renamed or moved images keep their state once
  XGrid has seen them launch. The resolved id is cached in
  `LibraryTitleIds` in `settings.json` so later scans do not open every disc.

The old recent strip is gone; use the **Recently played** sort. Screenshots are
still saved with **F8** and can be opened from **View → Open screenshots
folder**.

## What the scanner recognizes

| Extension | Listed | Launchable | Notes |
| --- | --- | --- | --- |
| `.iso` | Yes | Yes | Optimized XISO or full Redump; the game partition is mapped automatically. |
| `.chd` | Yes | Yes | Compressed disc image, with the same partition-offset detection. |
| `.zar` | Yes | Yes | Entry form or synthesized file tree. |
| `.xbe` | Yes | No | Shown for inspection; wrap it in an image to boot. |

The library is capped at 4,096 entries per scan. Split a very large collection
across multiple roots if you need more.

## Titles and title IDs

- For a **loose XBE**, XGrid reads the certificate title name when the title is
  resolved.
- For a **disc image**, the display title comes from the file name.
- Region, language and revision groups such as `(USA)`, `(Europe)` and
  `(En,Fr,De)` are removed from the title shown on a card.
- The **title ID** (`Title XXXXXXXX`) is resolved when the game is launched, not
  during the folder scan, so adding a large CHD collection does not decompress
  every image. The launch uses the resolved ID for per-title overrides.

Homebrew images that never set a certificate ID share a placeholder ID, so their
file names remain the distinguishing text.

## Cover art

The Library shows a cover thumbnail for each game when one is available.

### Local covers

Covers live in a folder you choose, never beside the disc. The default is:

```text
<data>/covers/
```

Set a different folder under **Settings → Library → Cover art → Local covers
folder**, or use **Browse**. The library matches covers in this order:

1. A file named with the XBE title ID: `4D530002.png`.
2. An exact file base name: `Halo - Combat Evolved.png`.
3. An annotation-stripped exact name: `Halo - Combat Evolved (USA).png` matches
   **Halo - Combat Evolved**.
4. Token-order and compound-word forms.
5. A fuzzy Jaro-Winkler match. The default threshold is **0.85**; raise it under
   **Settings → Library → Cover art** to prefer a missing cover over a wrong
   one.
6. Subtitle aliases (`A - B - C` also matches `B - C` and `C`).

Sequel numbers are protected: `Halo` does not match `Halo 2`. Region and release
year from the image file name break ties. Annotation stripping ignores `(USA)`,
`(Europe)`, `(En,Fr,De)`, `[!]`, `(Rev 1)` and similar groups.

PNG, JPEG, WebP and BMP are supported. A missing or unreadable image is shown as
the disc placeholder; it never hides the game.

### Online covers

The optional online catalog uses the same content-addressed layout as 5DO on
Cloudflare R2:

```text
https://assets.purelogiccode.com/xgrid/v1/catalog.json
```

This is the live default endpoint. The 2026-11-26 pack contains 1,632
deduplicated covers. If the catalog is unavailable, local covers keep working;
a self-hosted or `file://` catalog can be used instead.

The catalog contains a JSON index plus immutable WebP images. XGrid:

- caches the catalog under `<data>/covers-cache/` and revalidates it with
  `If-None-Match`;
- matches an exact XBE title id first, then exact and fuzzy titles/aliases;
- downloads only covers matched to games in your library, at most four at a
  time;
- verifies every image against its SHA-256 id before publishing it to the
  cache;
- keeps local covers working when the network or catalog is unavailable.

Set **Settings → Library → Cover art → Use the online cover catalog** to enable
it, and **Download missing covers automatically** to fetch matches after a scan.
The catalog URL is editable, so a self-hosted R2 bucket or a `file://` catalog
works too.

Use **Refresh** in Settings to fetch the catalog now; **Clear cache** removes the
catalog and downloaded images.

## Searching

The search box filters by title, path, format, title id, region, year,
publisher, developer, genre and description. Clearing it restores the current
filter and sort.

## Launching

Double-click a library card or press **Enter** on the selection. XGrid powers on
the core with:

- the selected disc image;
- the effective global settings;
- any matching per-title override;
- the library title in the status bar and session log.

If the console is already running, choosing another game **swaps the disc**
instead of restarting it. Use **File → Eject Disc** (Ctrl+E) to leave the tray
empty, or **Emulation → Power On / Off** (F5) to power off. Swapping needs
in-process core hosting, like save states.

## Per-title overrides

Open **Settings → Titles → Add override**. XGrid tries to key the override by the
XBE certificate title ID, so it follows the title across renamed, moved or
re-wrapped images. If no ID can be read, the override is keyed by the image path.

The override carries **guest memory** (64 or 128 MB), the **renderer** (OpenGL,
Vulkan or Null), the **texture filtering** (linear or nearest) and the
**presentation filter** (nearest, bilinear, scanlines or CRT). Each row offers
only the renderers the selected core was built with. Unset values inherit the
global configuration.

## Missing games

If a game does not appear:

1. Check the extension is supported.
2. Confirm the folder is in the list and still exists.
3. Press **Rescan** or **View → Rescan library**.
4. Check **Activity** for scan errors.

If a game appears but does not boot, see [Compatibility](../reference/compatibility.md)
and [Troubleshooting](../reference/troubleshooting.md).
