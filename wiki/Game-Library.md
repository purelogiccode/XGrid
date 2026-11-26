# Game library

The Library scans folders recursively and launches disc images. It never copies
or modifies your files.

## Add folders

Use **Library → Add folder** or **Settings → Library → Add folder**.

A scan:

- follows subdirectories;
- skips inaccessible folders and reparse points;
- stops at 4,096 entries;
- de-duplicates paths;
- sorts by title.

## Tile and list views

XGrid opens the Library in **Tiles** mode: a card per game with its cover art,
title, format and size. Click a card to launch it.

Switch modes with **Tiles** and **List** in the library toolbar:

- **Tiles** shows the cover grid (the default).
- **List** shows the detailed rows with folder, title id, format, size and a
  **Play** button.

The choice is saved in `settings.json` and restored next time. XGrid always
opens on the Library page; use **Console** to see the emulated picture.

## Recently played

The strip above the games shows up to ten recently launched titles, newest first.
Click a card to play it again, or **Clear** to empty the list. The entries are
stored in `settings.json` and survive restarts. A moved or deleted file is
dropped when you try to launch it.

## Supported files

| Extension | Listed | Launchable |
| --- | --- | --- |
| `.iso` | Yes | Yes |
| `.chd` | Yes | Yes |
| `.zar` | Yes | Yes (entry form or file tree) |
| `.xbe` | Yes | No (inspection only) |

## Cover art

Each row shows a cover when one is available. Covers live in the user data
folder, never beside the disc:

```text
<data>/covers/
```

The lookup order is:

1. An image named after the title ID, for example `4D530002.png`.
2. An image named after the title. `(USA)`, `[!]` and similar annotations are
   ignored when matching.

Use **Library → Covers** to open the folder and **Rescan** after adding images.
PNG, JPEG, WebP and BMP are supported. A missing image falls back to the disc
placeholder.

## Titles and overrides

- Loose XBE files show their certificate title.
- Disc images use the file name as the display title.
- When readable, the XBE title ID (`Title XXXXXXXX`) keys per-title overrides.
- Otherwise the image path keys the override.
- An override can set guest memory, the renderer, texture filtering and the
  presentation filter; unset values inherit the global configuration.

## Launch

Click a tile, or the **Play** button in list mode, to launch. Power off a
running console first. XGrid applies the global settings plus any matching
per-title override.

While the console runs, clicking a different game inserts that disc instead of
power-cycling. **Ctrl+E** ejects. Both need in-process core hosting.

## Missing games

1. Check the extension.
2. Confirm the folder exists and is in the list.
3. Press **Rescan**.
4. Read **Activity** for scan errors.

For boot failures, see [Media formats](Media-Formats) and
[Troubleshooting](Troubleshooting).

## Screenshots

The newest captures appear as a thumbnail strip at the bottom of the page.
Click a picture to open it, **Refresh** to re-read the folder, or **Open folder**
to browse the PNGs. **F8** saves the console picture; with separate-process
hosting it captures the whole window instead.
