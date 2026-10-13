# ktest-recon — kernel-test-suite bring-up scripts (§116)

Recon-only helper scripts used by `wave/ktest-suite`. They operate on
**private media copies** (never the main checkout, never `References/`):

- `inject_dash.py` — in-place qcow2 sector R/W (mirrors
  `XGrid.Storage.Qcow2DiskImage`) + FATX parse/inject: replaces
  `C:\xboxdash.xbe` (cluster 268+, 32-sector clusters, u16-LE FAT, root =
  cluster 1, superblock at byte `0x8CA80000`) with another XBE and
  patches the root-dirent size. Modes: `scan` | `inject`.
- `list_root.py` — lists the C: FATX root directory entries.
- `patch_symlink.py` — patches the private suite XBE: restores the
  `C:\kernel_tests.log` path and writes the 164-byte guest shellcode into
  the ignored `load_name_file` donor (VA `0x2E620`) that attempts
  `IoCreateSymbolicLink("\??\C:", "\Device\Harddisk0\Partition2")`
  (call site `FF 15 1C 7F 05 00` = thunk slot `0x57F1C`). Currently
  **triggers a kernel KeBugCheck** on Complex_4627 — kept as the
  documented starting point for the open diagnosis in §116.
- `extract_dash.py` — extracts the original `xboxdash.xbe` from a qcow2
  copy for offline analysis.

All paths default to the wave's `wt-data\` scratch directory (gitignored);
edit the constants at the top of each script to reuse elsewhere.