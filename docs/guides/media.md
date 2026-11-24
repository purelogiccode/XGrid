# Media formats

XGrid accepts the disc formats the core can mount, plus two real-time drivers
added for XGrid. Nothing is extracted to a temporary folder.

## Disc images

### ISO / XISO

A standard XDVDFS image with 2,048-byte sectors. The core block layer reads it
directly. This is the simplest and most compatible format.

### CHD

A compressed MAME CHD disc image. The `xgridchd` block driver decodes hunks on
demand through the vendored libchdr 0.3.0, so a compressed disc plays without
extraction.

- Supported by the library, **Open Disc**, and the default disc setting.
- The core reports the true virtual size and the guest sees a normal DVD.

### ZArchive (`.zar`)

A ZArchive container. The `xgridzar` driver decodes zstd blocks on demand and
supports both archive shapes:

- **Entry form** — the archive carries the disc as one `.iso` file entry; the
  driver exposes that entry as the guest DVD.
- **File tree** — the archive carries the game as individual files (the shape
  `ZArchiveSharp --iso` produces). The driver synthesizes an XDVDFS volume in
  memory: the descriptor, directory tables and a balanced name tree are built
  at open time, and each file is placed at a 2048-byte sector boundary. Reads
  still come from the compressed archive, so nothing is extracted.

The XDVDFS name limit applies to the file tree: names are Latin-1, up to 42
characters, without path separators.

#### Creating an entry-form archive

1. Confirm the source ISO boots in XGrid on its own.
2. Run the packer; `--iso` produces the entry form:

   ```powershell
   ZArchiveSharp --iso game.iso game.zar
   ```

3. Add or open `game.zar`. Entry-form archives use the largest `.iso` entry;
   file-tree archives get the synthesized volume.
4. A name XDVDFS cannot represent is reported with the offending entry.

The shell's directory reader is covered by synthetic entry-form and file-tree
archives in `ZarArchiveTests`, so the shape check also runs on a clean checkout.

### XBE

A loose Xbox executable. XGrid can list it and read its certificate title, but it
is **not a disc** and cannot boot on its own. Wrap it in an ISO or CHD.

## Changing discs while the console runs

**File → Open Disc** (Ctrl+O), the toolbar **Open** button, or clicking another
game in the Library inserts the new disc without power-cycling. **File → Eject
Disc** (Ctrl+E) opens the tray and leaves it empty. The core runs the block-layer
change on its own loop, so the picture keeps running while the disc is swapped.

Both actions need in-process core hosting, like save states. With a separate
core process, power off before changing the disc.

A multi-disc game that asks for the next disc accepts the swap directly. The new
image can be ISO/XISO, CHD or ZArchive, and the tray cycle makes the guest notice
the change.

## Hard-disk images

The HDD image is the console's storage. Raw and qcow2 images are supported.

- With no disc inserted, the console boots the dashboard from the HDD.
- Save states are stored inside the HDD image.
- Keep a backup of your HDD image before experimenting.

## Firmware

| File | Size | Required |
| --- | --- | --- |
| BIOS / flash ROM | 256 KB or 512 KB | Yes |
| MCPX boot ROM | 512 bytes | Yes for the retail boot chain |
| EEPROM | 256 bytes | Yes for a normal setup |

XGrid ships none of these. See [Requirements](../getting-started/requirements.md).

## Format support matrix

| Format | Library | Launch | Notes |
| --- | --- | --- | --- |
| `.iso` | Yes | Yes | Direct block access. |
| `.chd` | Yes | Yes | Real-time decode. |
| `.zar` | Yes | Yes | Entry form or synthesized file tree. |
| `.xbe` | Yes | No | Inspection only. |
| HDD raw/qcow2 | Settings | Boot | Dashboard and save states. |

## Choosing a format

- Use **ISO** for maximum compatibility and simplicity.
- Use **CHD** to save disk space with no runtime extraction.
- Use **ZAR** for either an entry-form archive or a file-tree archive; the
  driver serves the entry directly or synthesizes the volume.
- Keep the **HDD image** on fast local storage; save states write to it.
