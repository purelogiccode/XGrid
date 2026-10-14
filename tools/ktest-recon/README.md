# ktest-recon — kernel-test-suite bring-up scripts (§116, §120)

Recon/helper scripts for the kernel-test-suite bring-up. They operate on
**private media copies** (never the main checkout, never `References/`).
§116 diagnosed the gate; §120 unblocked it — see
`docs/research-notes/xgrid-architecture-decisions.md`.

## Working pipeline (§120) — the suite runs, 378/378 tests

- `patch_suite_xbe.py` — **the winning media patch.** Rewrites the private
  suite XBE so the log lands on writable FATX C: instead of the read-only
  XDVDFS DVD (D:): (1) patches the log path string at file `0x43599`
  (VA `0x53599`) from `D:\kernel_tests.log` to `C:\kernel_tests.log`; (2)
  injects ANSI_STRING guest shellcode into the ignored `load_name_file`
  donor (VA `0x2E620`, file `0x1E620`, 164 B) that calls
  `IoCreateSymbolicLink("\??\C:", "\Device\Harddisk0\Partition2")` through
  thunk slot `0x57F1C` (ordinal 67). The earlier §116 KeBugCheck was a
  **guest-shellcode bug, not an emulator gap**: it passed UNICODE_STRINGs
  where the kernel takes OBJECT_STRING = STRING (ANSI). See the module
  docstring. Verified: post-patch XBE sha256
  `76E2C52AE6C28F2154CA94C7579166F7373BFAD1C3FEE492AEBAB547B1EB1A1C`.
- `read_writedump_fatx.py` — extracts a file the guest wrote from an XGrid
  `--writedump` sector journal (root LBA `0x465488`, FAT `0x465409`, 32
  sector/cluster). **This is how the 53.7 MB acceptance log was recovered.**
- `run_xemu_ktest_full.py` — differential xemu run in a private profile
  (`wt-data/xemu2`). EIP-samples via the QEMU monitor, pmemsave forensics,
  quits when the suite settles into `Sleep(10000)`. Suite code EIPs are
  almost never sampled (per-print `WriteFile` keeps the CPU in kernel
  space) — detect the run via the FATX log, not EIP ranges.
- `read_fatx_qcow2.py` — standalone FATX walker over a qcow2; reads the log
  xemu committed to its private HDD after `run_xemu_ktest_full.py`.
- `va2pa.py` — VA→PA via the page tables in a RAM dump (needs CR3).
- `scan_ram.py` / `scan_objects.py` / `dump_strings.py` — string/object
  forensics over RAM dumps (which drive letters / `\Device\` paths exist).

Reproduce the acceptance run: `patch_suite_xbe.py --log C --target
'\Device\Harddisk0\Partition2'` → `XISOSharp -y -m -c <dir> out.iso` (add a
CRLF `config.txt`, e.g. `disable-video=1`) → `xgrid boot ... --dvd out.iso
--writedump <dir> --break 4E03A` (0x4E03A = main's return site) →
`read_writedump_fatx.py`. The suite's config parser needs **CRLF** line
endings; LF-only `config.txt` is silently ignored.

## §116 recon (historical)

- `inject_dash.py` — in-place qcow2 sector R/W (mirrors
  `XGrid.Storage.Qcow2DiskImage`) + FATX parse/inject: replaces
  `C:\xboxdash.xbe` (cluster 268+, 32-sector clusters, u16-LE FAT, root =
  cluster 1, superblock at byte `0x8CA80000`) with another XBE and
  patches the root-dirent size. Modes: `scan` | `inject`.
- `list_root.py` — lists the C: FATX root directory entries.
- `patch_symlink.py` — the **pre-§120** shellcode attempt (UNICODE_STRING
  layout) that triggered the kernel KeBugCheck. Superseded by
  `patch_suite_xbe.py`; kept as the documented §116 starting point.
- `extract_dash.py` — extracts the original `xboxdash.xbe` from a qcow2
  copy for offline analysis.
- `kernel_exports.py`, `letters_scan.py` — kernel export-table + drive
  letter discovery helpers.

All paths default to the wave's `wt-data\` scratch directory (gitignored);
edit the constants at the top of each script to reuse elsewhere.