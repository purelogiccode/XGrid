# Game library

The Library is a folder scanner and launcher. It does not copy or modify your
images.

## Adding folders

Use either entry point:

- **Library → Add folder**
- **Settings → Library → Add folder**

Folders are scanned recursively. A scan:

- follows subdirectories;
- skips inaccessible folders and reparse points;
- stops at **4,096 entries**;
- de-duplicates paths;
- sorts by title;
- reads only file metadata (name, size, format). It does **not** hash the
  images, open the disc, or decompress a CHD.

Add as many roots as you need. Removing a folder from the list does not delete
anything on disk.

## Tile and list views

XGrid opens the Library in **Tiles** mode: one card per game with its cover
art, the title, format and size. Click a card to launch it.

Use **Tiles** and **List** in the library toolbar to switch modes:

- **Tiles** shows the cover grid (the default).
- **List** shows the detailed rows with folder, title id, format, size and a
  **Play** button.

The choice is saved in `settings.json` and restored on the next start. XGrid
still opens on the Library page; use **Console** to see the emulated picture.

## Recently played

The **Recently played** strip above the game list shows the titles you launched
most recently, newest first. It keeps up to ten entries in `settings.json`, even
when the disc lives outside your configured folders. Click a card to launch the
game again; **Clear** removes the list. If a file was moved or deleted, the
entry is dropped the next time you try to launch it.

The list records successful starts only. Changing discs while the console is
running is a disc swap, not a new launch, so it does not change the order.

## What the scanner recognizes

| Extension | Listed | Launchable | Notes |
| --- | --- | --- | --- |
| `.iso` | Yes | Yes | XDVDFS ISO/XISO. |
| `.chd` | Yes | Yes | Compressed disc image. |
| `.zar` | Yes | Yes | Entry form or synthesized file tree. |
| `.xbe` | Yes | No | Shown for inspection; wrap it in an image to boot. |

The library is capped at 4,096 entries per scan. Split a very large collection
across multiple roots if you need more.

## Titles and title IDs

- For a **loose XBE**, XGrid reads the certificate title name when the title is
  resolved.
- For a **disc image**, the display title comes from the file name.
- The **title ID** (`Title XXXXXXXX`) is resolved when the game is launched, not
  during the folder scan, so adding a large CHD collection does not decompress
  every image. The launch uses the resolved ID for per-title overrides.
- Cover art named by title ID is matched when that ID has been resolved; naming
  the cover after the game title always works.

Homebrew images that never set a certificate ID share a placeholder ID, so their
file names remain the distinguishing text.

## Cover art

The Library shows a cover thumbnail for each game when one is available. Covers
live in the user data folder, never beside the disc:

```text
<data>/covers/
```

XGrid looks in that folder in this order:

1. A file named with the XBE title ID: `4D530002.png`.
2. A file named with the game title. Annotation groups are ignored for
   matching, so `Halo - Combat Evolved (USA).jpg` matches
   **Halo - Combat Evolved**.

PNG, JPEG, WebP and BMP are supported. Use **Library → Covers** (or
**Settings → Library → Cover art → Open folder**) to open the folder. A missing
or unreadable image is shown as the disc placeholder; it never hides the game.
Press **Rescan** after adding covers.

## Searching

The search box filters by title or format text as you type. Clearing it restores
the full list.

## Launching

Click a library row to launch it. XGrid powers on the core with:

- the selected disc image;
- the effective global settings;
- any matching per-title override;
- the library title in the status bar and session log.

If the console is already running, clicking a library row **swaps the disc**
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
3. Press **Rescan**.
4. Check **Activity** for scan errors.

If a game appears but does not boot, see [Compatibility](../reference/compatibility.md)
and [Troubleshooting](../reference/troubleshooting.md).

## Screenshots

The bottom of the library page shows the newest captures from
`<data>/screenshots/` as thumbnails:

- Click a picture to open it in the default image viewer.
- **Refresh** re-reads the folder after captures are added or removed.
- **Open folder** shows the PNGs in the file manager.

**F8** captures the whole shell window. See [Display](display.md#captions-and-screenshots).
