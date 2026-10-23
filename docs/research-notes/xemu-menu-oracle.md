# xemu 0.8.139 menu oracle (2026-10-23)

Status: **verified on this host.**  xemu 0.8.139 boots the dashboard from the
`data/xemu` media and is a usable visual menu oracle.  This corrects the older
"xemu deadlocks at the animation wait" note: that observation was specific to
the 0.8.136/0.8.137 runtime and the `data/xemu-0.8.136-windows-x86_64` media, not
to xemu generally.

## The working configuration

- Runtime: `data/xemu/xemu.exe`, **xemu 0.8.139**, commit
  `aa54c34cae1c09410d74c69a44210a9867fe2d16`, 2026-10-21.
- Config: `data/xemu/xemu.toml` (base path `data/xemu`).
- Media:
  - `data/xemu/Complex_4627.bin` (BIOS)
  - `data/xemu/mcpx_1.0.bin` (MCPX boot ROM)
  - `data/xemu/eeprom.bin`
  - `data/xemu/xbox_hdd.qcow2` (1.5 GB; XBMC dashboard)
- Launch: `xemu.exe -bios Complex_4627.bin -device smbus-storage,file=eeprom.bin -snapshot`.
  xemu 0.8.139 reads the machine/drive layout from `xemu.toml`, but it does not
  bridge `flashrom_path` into QEMU `-bios`; pass `-bios` explicitly.  `-snapshot`
  keeps the HDD template untouched.
- The QEMU monitor socket (`-monitor tcp:...`) fails to bind on this host
  (`Failed to bind socket: Input/output error`), so the oracle uses a window
  screenshot.  The guest framebuffer is visible in the xemu window.

## Observed result

The screenshot `data/xemu/xemu-0.8.139-oracle.png` (local, gitignored) shows the
**XBMC dashboard** with:

- `BIOS: Complex 4627` (the boot reached the dashboard);
- `Time: 16:09:42 - 10/23-2026`;
- `C: 519.061.504 free`, `E: 4.854.611.968 free`, `F: 0 free`, `G: 0 free`;
- the Xbox/XBMC background.

The process stayed responsive (`xemu | v0.8.139`, CPU active).  This is past the
kernel animation wait and into the dashboard, so it is a valid menu oracle.

## Why the old note was wrong

The old note came from xemu 0.8.136/0.8.137 with the
`data/xemu-0.8.136-windows-x86_64` media.  The kernel's boot animation waits for
the notifies page to reach a target; the animation pushbuffer writes a semaphore
through `NV097_SET_CONTEXT_DMA_SEMAPHORE` with a RAMHT handle, and the 0.8.136/
0.8.137 NV2A code passes that handle to `nv_dma_map` as if it were a raw PRAMIN
DMA-object address (the source has a TODO at `pgraph.c:910`).  XGrid resolves the
handle through the RAMHT and passes.  The 0.8.139 runtime and/or the
`data/xemu` media do not hit that path, so xemu reaches the dashboard.

## How to use it

1. Run `scripts/xemu-oracle.ps1` (or launch xemu manually with the configuration
   above).  It captures `data/xemu/xemu-oracle-<timestamp>.png`.
2. Run XGrid against the same HDD for a matching guest:
   `pwsh -File scripts/boot-to-first-frame.ps1 -Sample dashboard -Hdd data/xemu/xbox_hdd.qcow2`
3. Compare the XGrid `--frame-display` export with the xemu window.  This is a
   **visual** oracle, not a pixel-exact one: the xemu window is scaled by the
   host and the XGrid gate HDD is the EvoX dashboard, while the `data/xemu` HDD
   is XBMC.  For a pixel-exact comparison, use the same HDD and the same guest
   step, and capture the guest framebuffer rather than the host window.

The gate media (`wt-data/chain-199l/hdd.qcow2`, EvoX dashboard) and the
`data/xemu` HDD (XBMC) are different dashboards.  Do not compare their frames
directly.  Use this oracle for visual sanity and for the XBMC/XBE path; keep the
EvoX gate anchors for the 1B goldens.

## Reproduction note

`data/xemu/xemu.toml` was updated on 2026-10-23 to point at `data/xemu` media
(the old file pointed at `data/xemu-0.8.136-windows-x86_64`, which no longer
exists).  The screenshots are local artifacts under `data/` (gitignored) and are
not committed.
