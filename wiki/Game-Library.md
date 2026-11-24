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

## Supported files

| Extension | Listed | Launchable |
| --- | --- | --- |
| `.iso` | Yes | Yes |
| `.chd` | Yes | Yes |
| `.zar` | Yes | Yes (entry form or file tree) |
| `.xbe` | Yes | No (inspection only) |

## Titles and overrides

- Loose XBE files show their certificate title.
- Disc images use the file name as the display title.
- When readable, the XBE title ID (`Title XXXXXXXX`) keys per-title overrides.
- Otherwise the image path keys the override.
- An override can set guest memory, the renderer, texture filtering and the
  presentation filter; unset values inherit the global configuration.

## Launch

Click a row to launch. Power off a running console first. XGrid applies the
global settings plus any matching per-title override.

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
to browse the PNGs. **F8** captures the whole window.
