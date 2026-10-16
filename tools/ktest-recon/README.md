# ktest-recon — kernel-test-suite bring-up scripts (§116, §120)

Recon/helper scripts for the kernel-test-suite bring-up. They operate on
**private media copies** (never the main checkout, never `References2/`).
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

## Smoke (§123): scripted ~2-min cross-emulator subset run

`scripts/ktest-smoke.ps1` turns the §120 one-off into a repeatable gate:
7-test subset ISO (`tests=51,71,224,231,282,319,344`, CRLF `config.txt`,
`disable-video=1`) built from a `--park 4E03A` XBE, XGrid leg
(`--break 4E03A` + `read_writedump_fatx.py`), xemu leg (private profile +
own `xemu.toml`, `run_xemu_ktest_smoke.py` park-detect), then
`ktest_diff.py` checks every row against the §120 known deltas
(071/319/344 faithful-FAIL both incl. exact ERROR text, 224/231 XGrid-ahead
with xemu failing at line 110, 051/282 PASS both).

Run (from worktree root, after `dotnet build -c Release`):

```
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator\data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator\data\test-samples\kernel-test-suite-default.xbe"
```

Defaults assume `data\` next to the worktree root; `-FirmwareDir`/`-SuiteXbe`
override to any checkout holding the (gitignored) firmware + suite XBE.
`-XemuExe` defaults to `<FirmwareDir>\xemu.exe`, `-XisoSharp` to the
sibling-repo CLI build. `-SkipXgrid`/`-SkipXemu` re-run one leg.
Exit 0 = `KTEST-SMOKE: PASS`. Layout (all gitignored): `wt-data\ktest-smoke\
{xgrid,xemu}\` — media copies, subset ISO, `xemu.toml`, both recovered logs.

Two xemu-0.8.136 automation quirks the script works around (diagnosed §123):
(1) xemu.toml `flashrom_path` is not bridged into QEMU `-bios` (launch
params omit it -> `Failed to load BIOS '(null)'`, guest frozen at reset
vector `0xFFF0`); the runner passes `-bios` explicitly (xbox.c uses
`ms->firmware`). (2) xemu starts `paused (prelaunch)` under `-monitor`
automation; the runner sends `cont` after connecting, then quits gracefully
via `quit` (killing xemu loses cached qcow2 L2 metadata -> torn FATX tail).
The `--park 4E03A` XBE patch (new `--park` flag on `patch_suite_xbe.py`)
makes completion detectable on both emulators without a relaunch race: the
suite spins at main's return site instead of returning to the kernel (which
would relaunch the XBE and truncate the log via CREATE_ALWAYS). Parked XBE
sha256 `A76DEC38E9E483F29FB3BC60F36B5AC43938E61B05F3C932394E1BDF8A3E2E09`
(vs the §120 unparked `76E2C52A...`).

Helpers: `run_xemu_ktest_smoke.py` (boot + `cont` + EIP==`0x4E03A` x settle
park-detect + graceful `quit`; exit 0 = parked), `ktest_diff.py` (parse both
logs, require `End of Tests`, per-row green/RED vs expectations incl. ERROR
substrings; exit 0 = all match). Subset choice: 051 silent-stub pass,
071/319/344 faithful fails, 224/231 XGrid-ahead pair, 282 former CMC-#UD
site — the full known-delta table in 7 tests.