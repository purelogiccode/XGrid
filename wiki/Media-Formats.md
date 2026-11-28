# Media formats

XGrid reads discs on demand. Nothing is extracted or converted before launch.

## Disc images

### ISO / XISO

XDVDFS image with 2,048-byte sectors. Optimized XISOs start at the game
partition; full Redump images are detected by `xgridiso` and the game partition
is mapped to LBA 0.

### CHD

Compressed MAME CHD. `xgridchd` decodes hunks on demand. Both optimized and full
Redump CHDs work. Read-only.

### CCI

Cerbios/Repackinator compressed image. `xgridcci` decodes raw or LZ4 sectors on
demand. Single files and split `.1.cci` / `.2.cci` sets work; open the first
part. Read-only.

### ZArchive (`.zar`)

`xgridzar` decodes zstd blocks on demand.

- **Entry form** exposes the `.iso` entry as the DVD.
- **File tree** synthesizes an XDVDFS volume in memory. Names are Latin-1, up to
  42 characters, no path separators.

### Chihiro `.bin`

A raw FATX netboot image for the Sega Chihiro, not an Xbox disc. Convert an
`XBOXOG - Chihiro` ISO or a GD-ROM first. See [Chihiro](Chihiro).

### XBE

A loose Xbox executable. It is listed for inspection but is not bootable; wrap it
in a disc image.

## HDD and firmware

- HDD: raw or qcow2. Holds the dashboard. Xbox save states live in a qcow2 HDD,
  or in `xbox_snapshots.qcow2` when the HDD is raw or read-only.
- BIOS: 256 KB or 512 KB, required.
- MCPX ROM: 512 bytes.
- EEPROM: 256 bytes.

## Disc swap

While the console runs, `Ctrl+O`, the toolbar **Open** button or another Library
game inserts a new disc. `Ctrl+E` ejects. Both need in-process hosting.

## Format matrix

| Format | Library | Launch | Notes |
| --- | --- | --- | --- |
| `.iso` optimized | Yes | Yes | Game partition at sector 0. |
| `.iso` full Redump | Yes | Yes | XGD offset detected. |
| `.xiso` | Yes | Yes | Treated like ISO. |
| `.chd` | Yes | Yes | Real-time decode. |
| `.cci` | Yes | Yes | Single or split set. |
| `.zar` | Yes | Yes | Entry form or file tree. |
| `.bin` | Yes | Chihiro | Netboot image. |
| `.xbe` | Yes | No | Inspection only. |

Full guide: [Media formats](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/media.md).
