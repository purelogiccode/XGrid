# Media formats

## Disc images

| Format | Support | Notes |
| --- | --- | --- |
| ISO / XISO | Yes | Optimized XISO or full Redump; `xgridiso` maps the detected game partition to LBA 0. |
| CHD | Yes | Compressed; decoded on demand by `xgridchd`, with the same partition-offset detection. |
| CCI (`.cci`) | Yes | Cerbios/Repackinator LZ4 image; single or split, decoded on demand by `xgridcci`. |
| ZArchive (`.zar`) | Entry form or file tree | An `.iso` entry is served directly; a file tree is synthesized into XDVDFS. |
| XBE | Inspect only | Not bootable on its own. |

A full Redump image keeps a video partition in front of the game area. It is
played as-is: the core finds the XDVDFS descriptor at the XGD1/XGD2/XGD3 offset
and exposes the game volume at LBA 0. Nothing is converted or extracted.

### CCI

CCI stores each 2,048-byte sector raw or as one LZ4 block with a random-access
index. `xgridcci` reads it directly and applies the same game-partition mapping
as ISO and CHD.

- Single-part `game.cci` and split `game.1.cci`, `game.2.cci`, … sets work.
- Open the first part of a split set; the core discovers the rest.
- The library lists a split set once under its base name.

### ZArchive

The `xgridzar` driver decodes zstd blocks on demand and supports both shapes:

- **Entry form** — the largest `.iso` entry is the guest DVD. The entry may be
  an optimized XISO or a full Redump image.
- **File tree** — the game files are synthesized into an XDVDFS volume at open
  time (descriptor, directory tables, sector-aligned files), so a directory
  pack boots without extraction.

Names in a file tree use the XDVDFS rules: Latin-1, up to 42 characters,
without separators.

Build an entry-form archive:

```powershell
ZArchiveSharp --iso game.iso game.zar
```

The packer detects a full Redump image and packs the game partition rather than
the video partition.

## Changing discs

- **Ctrl+O**, the toolbar **Open** button, or clicking a Library row while the
  console runs swaps the disc; no power cycle.
- **Ctrl+E** ejects and leaves the tray empty.
- ISO/XISO (optimized or Redump), CHD, CCI and ZArchive images can be swapped in.
- Needs in-process core hosting, like save states.

## HDD

- Raw or qcow2.
- Boots the dashboard when no disc is inserted.
- Stores save states.
- Back it up before experimenting.

## Firmware

| File | Size | Required |
| --- | --- | --- |
| BIOS / flash ROM | 256 KB or 512 KB | Yes |
| MCPX boot ROM | 512 bytes | Yes for the retail boot chain |
| EEPROM | 256 bytes | Yes for a normal setup |

XGrid ships none of these.

## Tips

- Use ISO for the first boot of a title; optimized and full Redump images both
  work.
- Use CHD or CCI to save space with no extraction step.
- Use ZAR with either archive shape; entry form avoids the XDVDFS name rules.
- Keep the HDD on fast local storage.
