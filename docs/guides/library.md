# Game library

The Library is a folder scanner and launcher. It does not copy, move or modify
your images.

## Adding folders

Use any of these entry points:

- **View → Add library folder…**
- **Add folder** in the empty state
- **Settings → Library → Add folder**

Folders are scanned recursively. A scan:

- follows subdirectories;
- skips inaccessible folders and reparse points;
- stops at **4,096 entries** per scan;
- de-duplicates paths;
- reads only file metadata (name, size, format). It does **not** hash the
  images, open the disc, or decompress a CHD.

Add as many roots as you need. Removing a folder from the list does not delete
anything on disk.

## What the scanner recognizes

| Extension | Listed | Launchable | Notes |
| --- | --- | --- | --- |
| `.iso` | Yes | Yes | Optimized XISO or full Redump; the game partition is mapped automatically. |
| `.xiso` | Yes | Yes | Treated like an ISO. |
| `.chd` | Yes | Yes | Compressed disc image, decoded on demand. |
| `.cci` | Yes | Yes | Single part or split set; listed once under the base name. |
| `.zar` | Yes | Yes | Entry-form archive or synthesized file tree. |
| `.bin` | Yes | Chihiro | Sega Chihiro netboot image; see [Sega Chihiro](chihiro.md). |
| `.xbe` | Yes | No | Shown for inspection; wrap it in an image to boot. |

A split CCI set (`game.1.cci`, `game.2.cci`, …) is listed once under `game`.
Open the first part and the core discovers the rest. A later part on its own is
not a complete library entry.

The library is capped at 4,096 entries per scan. If you have a very large
collection, split it across multiple roots.

## Using the toolbar

- **Search games…** filters as you type by title, path, format, title ID, region,
  year, publisher, developer, genre and description.
- **All games / Favorites / Played / Unplayed** filters the collection.
- **Title / Recently played / Most played / Release year / Publisher** sorts it.
  Recently played and Most played use the play time XGrid records.
- **Icon size** changes the tile size from 128 to 384 pixels.
- **Tiles** and **List** switch between the cover grid and the detailed rows.

Double-click a card, or select it and press `Enter`, to launch. A single click
only selects it. The chosen view and icon size are saved in `settings.json`.

## Favorites and play time

- Click the star on a card or row to add or remove a favorite.
- XGrid records accumulated play time, launch count and last-played time when a
  game starts and when the console stops.
- **Recently played** and **Most played**, and the **Played** and **Unplayed**
  filters, use this state.

State is keyed by the XBE certificate title ID when known, otherwise by the image
path. The first launch resolves the title ID and migrates the favorite/play-time
entry, so renamed or moved images keep their state once XGrid has seen them
launch. The resolved ID is cached in `LibraryTitleIds` so later scans do not open
every disc.

## Titles and title IDs

- For a **disc image**, the display title comes from the file name. Region,
  language and revision groups such as `(USA)`, `(Europe)` and `(En,Fr,De)` are
  removed from the card title.
- For a **loose XBE**, XGrid reads the certificate title name when the title is
  resolved.
- The **title ID** is resolved on launch, not during the folder scan, so adding a
  large CHD collection does not decompress every image. The launch uses the
  resolved ID for per-title overrides, favorites and play time.
- Homebrew images that never set a certificate ID share a placeholder ID, so
  their file names remain the distinguishing text.

## Cover art

The Library shows a cover thumbnail when one is available. A missing or
unreadable image is shown as a disc placeholder; it never hides the game.

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
   **Settings → Library → Cover art** to prefer a missing cover over a wrong one.
6. Subtitle aliases (`A - B - C` also matches `B - C` and `C`).

Sequel numbers are protected: `Halo` does not match `Halo 2`. Region and release
year from the image file name break ties. Annotation stripping ignores `(USA)`,
`(Europe)`, `(En,Fr,De)`, `[!]`, `(Rev 1)` and similar groups.

PNG, JPEG, WebP and BMP are supported.

### Online covers

The optional online catalog uses a content-addressed layout hosted on Cloudflare
R2:

```text
https://assets.purelogiccode.com/xgrid/v1/catalog.json
```

XGrid:

- caches the catalog under `<data>/covers-cache/` and revalidates it with
  `If-None-Match`;
- matches an exact XBE title ID first, then exact and fuzzy titles/aliases;
- downloads only covers matched to games in your library, at most four at a time;
- verifies every image against its SHA-256 id before publishing it to the cache;
- keeps local covers working when the network or catalog is unavailable.

Enable **Use the online cover catalog** and **Download missing covers
automatically** under **Settings → Library → Cover art**. The catalog URL is
editable, so a self-hosted bucket or a `file://` catalog works too.

Use **Refresh** to fetch the catalog now; **Clear cache** removes the catalog and
downloaded images. The catalog host never receives your library contents or game
paths. See [Privacy and data](../legal/privacy.md).

## Launching

Double-click a card or press `Enter` on the selection. XGrid powers on the core
with:

- the selected disc image;
- the effective global settings;
- any matching per-title override;
- the library title in the status bar and session log.

If the console is already running, choosing another game **swaps the disc**
instead of restarting it. Use **File → Eject Disc** (`Ctrl+E`) to leave the tray
empty, or **Emulation → Power On / Off** (`F5`) to power off. Swapping needs
in-process core hosting, like save states.

## Per-title overrides

Open **Settings → Titles → Add override** and choose a disc or XBE image. XGrid
keys the override by the XBE certificate title ID when possible, otherwise by the
image path.

Each row can set guest memory (64 or 128 MB), the renderer, texture filtering and
the presentation filter. Unset values inherit the global configuration. The
renderer list follows the core's reported capabilities.

Overrides apply when the title is launched from the Library or **Open Disc**.

## Missing games

If a game does not appear:

1. Check the extension is supported.
2. Confirm the folder is in the list and still exists.
3. Press **Rescan** or use **View → Rescan library**.
4. Check **Activity** for scan errors.
5. Remember the 4,096-entry cap.

If a game appears but does not boot, see
[Compatibility](../reference/compatibility.md) and
[Troubleshooting](../reference/troubleshooting.md).
