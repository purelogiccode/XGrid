# ZArchive (`.zar`) format — facts for the `xgridzar` block driver

Source of truth: `CSharp_ZArchiveSharp/ZArchiveSharp` (the operator's pure-C#
port of **ZArchive 0.1.2**, Exzap). Everything below is taken from
`ZArchiveCommon.cs`, `ZArchiveFormat.cs` and `ZArchiveReader.cs`; **all integers
are big-endian on disk**.

## Why this note exists

XGrid needs a **real-time** ZAR loader: the guest reads a disc, the core decodes
on demand, nothing is extracted. That is a QEMU block driver in our patch set,
the same shape as `block/xgridchd.c`. The format facts are recorded here so the
driver can be written without re-reading the C# port, and so its parser can be
differentially tested against ZArchiveSharp.

## Layout

```
+---------------------------+ 0
| compressed data section   |   zstd blocks (or stored), addressed by offset records
+---------------------------+
| offset-record section     |   40-byte records, 16 blocks each
+---------------------------+
| name-table section        |   length-prefixed names (15-bit length header)
+---------------------------+
| file-tree section         |   16-byte entries (files and directories)
+---------------------------+
| meta-directory (empty)    |
+---------------------------+
| meta-data (empty)         |
+---------------------------+
| footer, 144 bytes         |
+---------------------------+ EOF
```

### Footer (last 144 bytes)

Six `OffsetInfo` pairs (`u64 offset`, `u64 size`, 16 bytes each) in this order:
`compressedData`, `offsetRecords`, `names`, `fileTree`, `metaDirectory`,
`metaData`; then a 32-byte SHA-256 integrity hash, `u64 totalSize`, `u32
version`, `u32 magic`.

- `footer.magic == 0x169F52D6`
- `footer.version == 0x61BF3A01`
- `footer.totalSize == file length` (a cheap validation)
- every section must lie inside the file; offset records ≤ 4 GiB, names ≤ 2 GiB

### File tree entries (16 bytes, 4 × u32)

`nameOffsetAndType`: bit 31 = file, low 31 bits = offset into the name table.
For a **file**: `field1` = uncompressed offset low 32, `field2` = size low 32,
`field3` = (size high 16 << 16) | offset high 16. For a **directory**: `field1`
= first child index, `field2` = child count, `field3` = reserved.
The root's name offset is `0x7FFFFFFF`.

### Offset records (40 bytes)

`u64 baseOffset` (offset of the first block's compressed bytes) followed by
sixteen `u16` values, each storing `compressedSize - 1`. A file with `n` blocks
uses `ceil(n / 16)` consecutive records; the block size (uncompressed) is
**64 KiB**, so block `i` decompresses to `min(64 KiB, size - i * 64 KiB)`.
Where the record table starts for a given file is derived from the file's
uncompressed offset (the reader in ZArchiveSharp is the reference for the exact
index arithmetic).

### Names

Length-prefixed, Windows-1252, `15` bits of length (max `0x7FFF`); the first
header byte is also re-read in one edge case documented in the C# reader.

## Driver design (`native/xgrid-core/block/xgridzar.c`)

1. `bdrv_open`: read the footer, validate magic/version/totalSize, read the name
   table and file tree (both are small), then locate the disc entry:
   the largest file entry whose name ends in `.iso` or `.xiso` (case
   insensitive). If there is none, fail with a message naming the entries, so
   the shell can tell the user to repack.
2. Expose `logicalbytes` = that entry's uncompressed size; reads map a byte
   offset to block `i`, decompress with `ZSTD_decompress` (libzstd is already a
   QEMU dependency) unless `compressedSize == 64 KiB + 1` (stored block), and
   cache the last decoded block exactly like the CHD driver.
3. `bdrv_probe`: footer magic at `file length - 4`.

## Validation plan (required before it is called done)

- **Differential parser**: a C# tool over ZArchiveSharp opens the same archive
  and dumps (a) the entry list with offsets/sizes and (b) the SHA-256 of the
  disc entry; the C driver's output via `qemu-io` must match byte for byte.
- **End to end**: boot a title whose ISO is stored in a `.zar`, with no file
  written to disk besides the archive.

## Implemented and validated (2026-11-23)

`native/xgrid-core/block/xgridzar.c` reads the footer, the offset records, the
name table and the file tree, picks the largest `.iso` entry and decodes its
blocks on demand with zstd (the last block is cached). It owns its `FILE*`
because QEMU does not allow block-layer reads from `bdrv_open`.

Evidence, against `data/test-samples/zar/hello-entry.zar` (a 37 KB archive
containing the 640 KiB `hello.iso` sample):

```
qemu-img info -f xgridzar hello-entry.zar
  file format: xgridzar
  virtual size: 640 KiB
qemu-io -f xgridzar -c "read -v 0x10000 0x20" hello-entry.zar
  00010000: 4d 49 43 52 4f 53 4f 46 54 2a 58 42 4f 58 2a 4d  MICROSOFT.XBOX.M
```

## The file-tree case: what `--iso` archives actually contain

`ZArchiveSharp --iso game.iso out.zar` converts the XISO into an archive of the
**game files** (`default.xbe`, media, ...), not one `image.iso` entry. That is
the right shape for a virtual filesystem and the wrong shape for a disc: the
guest expects an XDVDFS volume at sector 32.

So a game `.zar` produced that way cannot be served by the entry driver. Playing
it in real time needs an **XDVDFS synthesizer** in the driver: walk the archive
tree, build the volume descriptor and directory tables in memory, place each
file at a 2048-byte sector boundary, and map guest reads onto the archived file
extents. That is the next slice (3-5 days) and it makes `--iso` archives
playable with no extraction, which is the operator's actual goal.
