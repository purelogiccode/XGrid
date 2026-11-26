# Media formats

XGrid accepts the disc formats the core can mount, plus real-time drivers added
for XGrid. Nothing is extracted to a temporary folder and nothing is converted
before launch.

## Disc images

### ISO / XISO

A standard XDVDFS image with 2,048-byte sectors. The core block layer reads it
directly. Two layouts are supported:

- **Optimized XISO** (sometimes called a game-ready ISO) starts with the game
  partition at sector 0. It boots as-is.
- **Full Redump ISO** keeps the retail video partition in front of the game
  area (the game volume starts at an XGD1, XGD2 or XGD3 offset). The
  `xgridiso` driver finds the `MICROSOFT*XBOX*MEDIA` descriptor at the known
  offsets and maps the game partition to LBA 0.

Both layouts use the same `.iso` extension and the same library entry. A single
file rename is not needed, and the image is never rewritten.

### CHD

A compressed MAME CHD disc image. The `xgridchd` block driver decodes hunks on
demand through the vendored libchdr 0.3.0, so a compressed disc plays without
extraction. The driver also detects the game-partition offset inside the
decoded stream, so a CHD made from an optimized XISO and a CHD made from a full
Redump image both work.

- Supported by the library, **Open Disc**, and the default disc setting.
- The core reports the true virtual size and the guest sees a normal DVD.

### CCI (`.cci`)

Cerbios/Repackinator's compressed Xbox disc container. Each 2,048-byte sector is
stored raw or as one LZ4 block, with an index for random access. The `xgridcci`
driver decodes sectors on demand through the vendored libCCI, so nothing is
extracted. The same game-partition detection applies, so a CCI built from a full
Redump image boots the game rather than its video partition.

- Single-part (`.cci`) and split (`.1.cci`, `.2.cci`, …) images are supported;
  open the first part and the core discovers the rest.
- The library lists a split set once under its base name.
- Like CHD, CCI is read-only.

### ZArchive (`.zar`)

A ZArchive container. The `xgridzar` driver decodes zstd blocks on demand and
supports both archive shapes:

- **Entry form** — the archive carries the disc as one `.iso` file entry; the
  driver exposes that entry as the guest DVD. The entry may be an optimized
  XISO or a full Redump image; the same game-partition detection applies.
- **File tree** — the archive carries the game as individual files (the shape
  a directory pack produces). The driver synthesizes an XDVDFS volume in
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

   The packer detects a full Redump image and packs the game partition rather
   than the video partition.
3. Add or open `game.zar`. Entry-form archives use the largest `.iso` entry;
   file-tree archives get the synthesized volume.
4. A name XDVDFS cannot represent is reported with the offending entry.

The shell's directory reader is covered by synthetic entry-form and file-tree
archives in `ZarArchiveTests`, so the shape check also runs on a clean checkout.

### XBE

A loose Xbox executable. XGrid can list it and read its certificate title, but it
is **not a disc** and cannot boot on its own. Wrap it in an ISO, CHD or ZAR.

## Changing discs while the console runs

**File → Open Disc** (Ctrl+O), the toolbar **Open** button, or clicking another
game in the Library inserts the new disc without power-cycling. **File → Eject
Disc** (Ctrl+E) opens the tray and leaves it empty. The core runs the block-layer
change on its own loop, so the picture keeps running while the disc is swapped.

Both actions need in-process core hosting, like save states. With a separate
core process, power off before changing the disc.

A multi-disc game that asks for the next disc accepts the swap directly. The new
image can be ISO/XISO (optimized or Redump), CHD, CCI or ZArchive, and the tray
cycle makes the guest notice the change.

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
| `.iso` optimized XISO | Yes | Yes | Game partition at sector 0. |
| `.iso` full Redump | Yes | Yes | XGD partition offset detected automatically. |
| `.chd` | Yes | Yes | Real-time decode and partition-offset detection. |
| `.cci` | Yes | Yes | Single part or split set; LZ4 sectors decoded on demand. |
| `.zar` | Yes | Yes | Entry form or synthesized file tree. |
| `.xbe` | Yes | No | Inspection only. |
| HDD raw/qcow2 | Settings | Boot | Dashboard and save states. |

## Choosing a format

- Use **ISO** when you want the simplest file; optimized XISO and full Redump
  images are both accepted.
- Use **CHD** to save disk space with no runtime extraction.
- Use **CCI** for a Repackinator/Cerbios collection; single and split images are
  read directly.
- Use **ZAR** for either an entry-form archive or a file-tree archive; the
  driver serves the entry directly or synthesizes the volume.
- Keep the **HDD image** on fast local storage; save states write to it.
