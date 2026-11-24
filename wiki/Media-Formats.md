# Media formats

## Disc images

| Format | Support | Notes |
| --- | --- | --- |
| ISO / XISO | Yes | Direct XDVDFS image. |
| CHD | Yes | Compressed; decoded on demand by `xgridchd`. |
| ZArchive (`.zar`) | Entry form or file tree | An `.iso` entry is served directly; a file tree is synthesized into XDVDFS. |
| XBE | Inspect only | Not bootable on its own. |

### ZArchive

The `xgridzar` driver decodes zstd blocks on demand and supports both shapes:

- **Entry form** — the largest `.iso` entry is the guest DVD.
- **File tree** — the game files are synthesized into an XDVDFS volume at open
  time (descriptor, directory tables, sector-aligned files), so `--iso`
  archives boot without extraction.

Names in a file tree use the XDVDFS rules: Latin-1, up to 42 characters,
without separators.

Build an entry-form archive:

```powershell
ZArchiveSharp --iso game.iso game.zar
```

## Changing discs

- **Ctrl+O**, the toolbar **Open** button, or clicking a Library row while the
  console runs swaps the disc; no power cycle.
- **Ctrl+E** ejects and leaves the tray empty.
- ISO/XISO, CHD and ZArchive images can be swapped in.
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

- Use ISO for the first boot of a title.
- Use CHD to save space with no extraction step.
- Use ZAR with either archive shape; entry form avoids the XDVDFS name rules.
- Keep the HDD on fast local storage.
