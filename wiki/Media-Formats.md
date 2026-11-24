# Media formats

## Disc images

| Format | Support | Notes |
| --- | --- | --- |
| ISO / XISO | Yes | Direct XDVDFS image. |
| CHD | Yes | Compressed; decoded on demand by `xgridchd`. |
| ZArchive (`.zar`) | Entry form | Must contain the disc as one `.iso` entry. |
| XBE | Inspect only | Not bootable on its own. |

### ZArchive

The `xgridzar` driver exposes the largest `.iso` entry as the guest DVD and
decodes zstd blocks on demand. Archives that store the game as a file tree are
not supported yet; the shell refuses them with an explanation. Pack the ISO as
one file entry (for example, pack a folder that contains the ISO) rather than
expanding it into game files.

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
- Use ZAR only with an entry-form archive.
- Keep the HDD on fast local storage.
