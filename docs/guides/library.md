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
- sorts by title.

Add as many roots as you need. Removing a folder from the list does not delete
anything on disk.

## What the scanner recognizes

| Extension | Listed | Launchable | Notes |
| --- | --- | --- | --- |
| `.iso` | Yes | Yes | XDVDFS ISO/XISO. |
| `.chd` | Yes | Yes | Compressed disc image. |
| `.zar` | Yes | Yes | Only when the archive contains an `.iso` entry. |
| `.xbe` | Yes | No | Shown for inspection; wrap it in an image to boot. |

The library is capped at 4,096 entries per scan. Split a very large collection
across multiple roots if you need more.

## Titles and title IDs

- For a **loose XBE**, XGrid reads the certificate title name.
- For a **disc image**, the display title comes from the file name.
- When the XBE certificate can be read, XGrid also records the **title ID**
  (`Title XXXXXXXX`). That ID is what per-title overrides use when available.

Homebrew images that never set a certificate ID share a placeholder ID, so their
file names remain the distinguishing text.

## Searching

The search box filters by title or format text as you type. Clearing it restores
the full list.

## Launching

Click a library row to launch it. XGrid powers on the core with:

- the selected disc image;
- the effective global settings;
- any matching per-title override;
- the library title in the status bar and session log.

If the console is already running, power it off before launching another disc.
Use **Emulation → Power On / Off** (F5) or the Home page.

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
