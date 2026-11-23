# XISOSharp — Reusable MIT C# XDVDFS Library

Reference: `C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp` (sibling repo).
License: **MIT** (PureLogicCode.com; lead developer Peterson Fernandes), with third-party
notices for extract-xiso (BSD-style + advertising clause), xdvdfs (MIT), XboxKit (MIT),
ZArchiveSharp (MIT). Approved for direct integration into XGrid per plan §13.5.

---

## 1. Reuse surface (what XGrid can consume directly)

Library project: `XISOSharp/XISOSharp.csproj`, namespace `XISOSharp`, targets
`net8.0;net9.0;net10.0`, nullable enabled, `IsTrimmable` + `IsAotCompatible`, strong-named
(`XISOSharp.snk`), `PackageLicenseExpression=MIT`, MinVer versioning, XML docs on.
Runtime dependency: **`ZArchiveSharp` 1.4.0** (not BCL-only, despite docs claiming zero deps).

### Reading APIs

- `XisoReader` (static):
  - `OpenImageStream(path[, share])` — `.cso`-aware (extension + `CISO` magic).
  - `VerifyXiso(Stream|IBlockDevice, name[, skipSectors])` → `(rootDirSector, rootDirSize, discLseek)`.
  - `GetVolumeInfo(...)` → `VolumeInfo` (`IsValid, RootDirSector/Size, DiscLseek, DiscFormat,
    FileLength, CreationTime, DescriptorSector`) — never throws on invalid.
  - `ListDirectory(path|Stream, internalPath)`, `ListDirectoryFlat`, `GetEntryInfo`.
  - `ReadFileBytes(path|Stream, internalPath, Span<byte>, fileOffset)` — in-place reads, clamped
    to file size; 0 past EOF; `InvalidDataException` for missing/dir.
  - `ComputeFileHash` / `ComputeDirectoryHashes` (MD5/SHA-256...).
  - `Extract` / `UnpackImage` / `CopyOut` / `CopyIn` / `Repair` / `Salvage` / `SplitXiso` /
    `JoinSplitXiso`.
  - `GetXbeInfo(path|Stream, internalPath)` / `GetXexInfo(...)`.
  - `GetSectorLayout`, `AuditXiso`, `IsOptimizedImage`.
- `XisoExplorer` (sealed, IDisposable): `Volume`, `ListChildren`, `GetNode`,
  `OpenReadStream(internalPath)` — bounded seekable stream over a file extent
  (`DiscLseek + StartSector*2048`, length = `FileSize`), CISO-transparent. Stateless mode opens
  a fresh image handle per stream; `KeepOpen` mode serializes with an internal lock.
  `XisoExplorerOptions { KeepOpen, Share }`.
- `IBlockDevice` (BCL-friendly seam):
  ```csharp
  public interface IBlockDevice : IDisposable
  {
      long Length { get; }
      int Read(long offset, Span<byte> buffer);
      void Write(long offset, ReadOnlySpan<byte> buffer);
  }
  ```
  Implementations: `FileBlockDevice`, `MemoryBlockDevice`, `OffsetBlockDevice` (with
  `Probe` over the known disc offsets), `CisoBlockDevice` (random-access, single-sector cache,
  DEFLATE v1 + LZ4 v2), `BlockDeviceStream` (read-only seekable Stream adapter).
- `IFilesystem`: `CreateFile`, `CreateDirectory`, `FileExists`, `FileLength`; `LocalFilesystem`
  (root-escape guarded) and `MemoryFilesystem` (`ReadAllBytes`, `FileNames`).
- Async/cancellation/progress: `*Async` wrappers, `CancellationToken` on long ops,
  `IProgress<ProgressInfo>` (`FileCount/DirCount/DirAdded/FileAdded/FileProgress/FinishedPacking`).
- Errors: `XisoFormatException`, `XisoEmptyException`, `XisoFileTooLargeException`,
  `ExtractErrorException` (+ codes), `ExtractFileException`, `TruncatedCopyException`.

### Suggested XGrid integration
- **Host-side game library/scanner and tools**: reference the library (project or NuGet).
- **Virtual DVD backing**: implement an XGrid `IBlockDevice` adapter over the emulated ATAPI
  media so `VerifyXiso`/`AuditXiso`/`XisoChecksum` can validate images in tooling.
- **Tests**: `MemoryBlockDevice`, `MemoryFilesystem`, deterministic writer (`fileTime: 0`),
  `TestData/output/source.iso`, `XISOSharp.Tests/Fixtures/test_fixture.iso` (MIT-generated
  synthetic data — no copyrighted game content).

### Integration caveats
- Adds `ZArchiveSharp 1.4.0` runtime dependency.
- `XisoWriter.CreateXiso` mutates the process CWD and serializes process-wide — never call from
  an emulator thread.
- Block devices are **not thread-safe** (shared seek position / single-sector cache). Serialize
  or use `XisoExplorer(KeepOpen)`.
- `GetXbeInfo`/`GetXexInfo` require the executable to be inside a valid XISO; the underlying
  `ParseXbeHeader`/`ParseXexHeader` are private. For a standalone XBE, XGrid must implement its
  own parser (see cxbx.md §2).
- Doc/API mismatch: `ListDirectory(IBlockDevice)` is advertised but does not exist; wrap with
  `new BlockDeviceStream(dev)`.

---

## 2. XDVDFS format facts (for XGrid's own implementation / virtual drive)

### Volume descriptor — sector 32 (byte 0x10000), exactly 2048 bytes

| Field | Offset | Size |
|---|---|---|
| Magic `MICROSOFT*XBOX*MEDIA` | 0x00 | 20 |
| Root dir sector (u32 LE, partition-relative) | +20 | 4 |
| Root dir size (bytes) | +24 | 4 |
| FILETIME (1601 epoch, 100 ns) | +28 | 8 |
| Unused/zero | +36 | 0x7C8 |
| Trailing magic (verified) | +0x7EC | 20 |

`+0x7EC` (= `20 + 4 + 4 + 8 + 0x7C8`) is the offset the XISOSharp reader seeks to
(`XisoReader.TraverseXiso`), and it is the only position where a 20-byte magic
still fits inside the 2048-byte descriptor sector. An earlier revision of this
table said `+0x7F4`, which is 8 bytes past the sector end; corrected 2026-10-22
while implementing `XGrid.XboxFs.XdvdfsSectorReader` (decisions 221).

Writer default root sector `0x108`; root sector+size 0 = empty image.

### Directory entry

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | Left child offset in DWORDs (×4 = byte offset in table); 0 = none |
| 2 | 2 | Right child offset in DWORDs |
| 4 | 4 | Start sector (partition-relative) |
| 8 | 4 | File size (dir: table size) |
| 12 | 1 | Attributes |
| 13 | 1 | Filename length (0–255) |
| 14 | n | Filename, Latin-1/Windows-1252 |

Entry length `14 + len` rounded up to multiple of 4; tables padded to next sector if an entry
would cross 2048. Names with `.`, `..`, `/`, `\` rejected/sanitized.

### Sentinels / layout

- `0xFFFF` = end of table; mid-table it jumps to the start of the next sector (legacy linked-list).
- `0x0000` + 12 zero header bytes = xdvdfs empty-directory sentinel; empty dirs are written as
  one sector of `0xFF` (size 2048).
- Directory tables are case-insensitive AVL trees keyed by ASCII-uppercased filename; shorter
  string sorts first on shared prefix. Duplicate inserts rejected.
- Writer three-pass layout: table sizes → sector assignment (root table 0x108, then file data
  depth-first) → write (file data pre-order, then tables, sector-padded).
- `llCompat` legacy right-offset fix: if `rightOffset*4 / 2048 > currentSector`, then
  `rightOffset = currentSector*256 + 256` (treat pointer as "next sector"). Any non-zero left
  child forces `llCompat=false`.
- Optimized tag: 24-byte ASCII `in!xiso!2.7.1 (01.11.14)` at byte offset **31337**; detection
  compares the 7-byte prefix `in!xiso`. Position shifts by `discLseek`/`prependSectors`.
- Alignment: `FileModulus = 0x10000` (64 KiB); image length padded to 64 KiB multiple;
  `PadByte = 0xFF`; `PadShort = 0xFFFF`; 4 GB per-file limit (`XisoFileTooLargeException`).
- Media-enable patch: pattern `E8 CA FD FF FF 85 C0 7D`, byte 7 `0x7D → 0xEB` applied to `.xbe`
  files on create/rewrite/copy-in (Boyer-Moore), default enabled.
- ECMA-119 descriptors for burning: PVD at `0x8000` (`01 "CD001" 01`), volume space size at
  +80, volume set size +120, volume set id +190, creation date +813; terminator at `0x8800`.
- Attributes: `0x01` RO, `0x02` HID, `0x04` SYS, `0x10` DIR, `0x20` ARC, `0x80` NOR; reserved
  `0x08`/`0x40`; valid mask `0xB7`.

### Disc-format detection (probe order)

| Offset (`DiscLseek`) | Format |
|---|---|
| 0x00000000 | RAW |
| 0x0FD90000 | GLOBAL (XGD2) |
| 0x02080000 | XGD3 |
| 0x89D80000 | XGD2 Hybrid |
| 0x18300000 | XGD1 |
| absolute 0 (descriptor at file start) | rebuilt "sector-0" variant (DescriptorSector=0) |

Magic checked at `0x10000 + probe`. `skipSectors` bypasses probing (header at
`skipSectors*2048 + 0x10000`).

### XGD tables (`XgdTables.cs`)

- `XisoOffset = [0x18300000, 0x0FD90000, 0x89D80000, 0x02080000]` (XGD1, XGD2, Hybrid, XGD3).
- `XisoLength = [0x1A2DB0000, 0x1B3880000, 0x0BF8A0000, 0x204510000]`.
- `RedumpIsoLength[9]`, `VideoL0Length[19]`, `VideoL1Length[19]`, `VideoLength[19]`,
  `WavePvd[24]` (PVD ASCII timestamp at 0x832D).
- Helpers: `GetXgdType`, `GetVideoType`, `GetRedumpLength`, `GetXisoTypeFromVideo`,
  `GetRedumpIsoTypeBySize`, `GetVideoTypeBySize`, `GetXisoTypeBySize`.

### CISO container

- Magic `CISO` (`0x4F534943`), 24-byte header, block size 2048, `ver` 1 = DEFLATE
  (`0x80000000` = plain) / 2 = LZ4 (`0x80000000` = compressed), `align` 0/1/2, index
  `totalBlocks+1` LE u32, compression kept only when it saves > 12 bytes. Split `*.1.cso` parts.

---

## 3. XBE parsing coverage in XISOSharp

- `XbeInfo`: `BaseAddress (0x104), EntryPoint (0x128), SectionCount (0x11C), InitFlags (0x124),
  CertSize (0x00), CertTimeDate (0x04), TitleId (0x08), TitleName (UTF-16 80 bytes, 0x0C),
  AlternateTitleIds[16] (0x5C), AllowedMedia (0x9C), GameRegion (0xA0), GameRatings (0xA4),
  DiskNumber (0xA8), Version (0xAC)`.
- Media flags: HDD 0x01, DVD/CD 0x02, DVD-5 RO 0x04, DVD-9 RO 0x08, DVD-5 RW 0x10, DVD-9 RW 0x20,
  dongle 0x40, media board 0x80. Region: NA 0x01, Japan 0x02, ROW 0x04, worldwide 0x07.
- Guards: file `< 0x12C` → null; read limit `0x8000`; cert size must equal `0x1D0`.
- XEX2 (Xbox 360) parsing also present (`XexInfo`) — not needed by XGrid.

## 4. Gaps for XGrid

1. **No FATX parser** anywhere — only FATX-friendly ISO split/join (`DefaultPartSizeBytes =
   4 GiB`). XGrid must implement FATX clean-room.
2. **No raw XBE parser** (only inside-XISO introspection) — XGrid needs its own loader.
3. **No DVD hardware model** — no drive commands, security sectors, video partitions,
   challenge/response; only Redump archival table helpers (`XisoRedump`, `SecuritySectors`,
   `XboxPrng`).
4. Block devices not thread-safe; CISO has a single-sector cache.
5. `VolumeInfo.DiscFormat` reports rebuilt sector-0 images as `RAW`; use `DescriptorSector` to
   distinguish.
