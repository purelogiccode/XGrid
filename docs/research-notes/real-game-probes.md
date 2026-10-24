# Real-game probes (local media, not gates)

Status: **2026-10-24.** Retail gameplay is now the lead milestone (**M-G**,
operator decision 2026-10-24): one retail title must reach a gameplay frame at
any speed, verified against xemu 0.8.139 on the same ISO. These probes are
**acceptance, not merge gates**; the standing gates remain the 1B accurate
goldens and the ktest smoke.

## Media

`D:\Samples\XBOXReady` no longer exists on H1. The retail ISOs live at
`H:\Tests\XBOXTest` (37 titles, ~7.3 GB each, Redump where noted). They are
local media: never committed, never CI, never gate currency.

| File | Notes |
|---|---|
| `H:\Tests\XBOXTest\007 - Agent Under Fire [NTSC-U][Redump].iso` | **M-G target (MG1)**; the XBE executes today. |
| the other 36 ISOs | MG2 picks the second title from these. |
| ~~`4x4 Evo 2 (USA)`~~ | **not present** in the current set; the 2026-10-23 kernel-idle probe cannot be re-run here. |

Boot media: the gate HDD (`wt-data\chain-199l\hdd.qcow2`, EvoX dashboard) and
the firmware set in `data\xemu` (`Complex_4627.bin`, `mcpx_1.0.bin`,
`eeprom.bin`).

## The oracle

**xemu 0.8.139** at `data\xemu\xemu.exe`, same ISO, same firmware/EEPROM, same
scripted input. `data\xemu\xemu.toml` is **stale** (it still points at
`D:\Samples\XBOXReady` and `007 - Agent Under Fire (USA).iso`) - MG0 fixes it and
adds an `-IsoPath` parameter to `scripts/xemu-oracle.ps1`. The QEMU monitor
socket fails to bind on H1, so the capture is a window screenshot; the
comparison is the software frame at the same guest time, never a
cross-renderer pixel-exact claim.

## Command

```powershell
pwsh -File scripts/boot-to-first-frame.ps1 `
  -Sample 007 `
  -IsoPath "H:\Tests\XBOXTest\007 - Agent Under Fire [NTSC-U][Redump].iso" `
  -FirmwareDir "data\xemu" `
  -Hdd "wt-data\chain-199l\hdd.qcow2" `
  -Steps 5000000000
```

The harness uses HDD + DVD (the `-IsoPath` branch) and the fast engine. It
writes `wt-data/btff/<sample>-sw.json`, a BMP of the first/last/scanout/display
frames, and the full CLI log.

## Results (2026-10-23, before the DSP port)

| Probe | Steps | Wall | Flips | Draws | Pixels | EIP at end | Verdict |
|---|---:|---:|---:|---:|---:|---|---|
| 007 | 1B | 19.50 s | 55 | 2,672 | 41,995,882 | - | PASS |
| 007 | 5B | 81.25 s | 201 | 2,672 | 41,995,882 | **0x0001FC66** | PASS - the XBE is executing |
| 4x4 Evo 2 | 1B | 18.82 s | 43 | 2,672 | 41,995,882 | - | PASS (media no longer present) |
| 4x4 Evo 2 | 5B | 185.47 s | 219 | 2,672 | 41,995,882 | **0x8001A68C** | PASS (no fault) - kernel idle |

### 007

- At 5B the EIP is `0x0001FC66`, inside the loaded XBE (the 007 probe's XBE base
  is `0x10000`; the historical 007 entry is `0x22A17`). The game's own code is
  running, not the boot animation loop.
- `firstFrameStep=46,000,000`, `firstFrameSha=6F780A33...` (the boot animation);
  `lastFrameSha=F9F17001...` at 5B. The first frame is the boot animation, so
  "first frame" is not yet a gameplay frame.
- `ram=C61364E3...`, `cpu=AFD09BF5...`, `draws=2,672`, `frames=201`,
  `stalls=201`, no fault.
- The historical PlayMPC/audio gate was at ~56B steps; 5B is early, and the
  DSP port (MP1.2-MP1.4) landed after this probe, so **re-run at 5B/20B/60B**
  before trusting the old timeline.

### 4x4 Evo 2

- At 5B the EIP was `0x8001A68C`, the kernel idle loop: waiting, not executing
  game code. The ISO is no longer on this host, so the probe is historical.
- The same shape is what MG0 looks for on the current titles: a park in the
  kernel idle thread with no fault, and the thread/wait object behind it.

## What to do next (MG0)

1. Fix `data\xemu\xemu.toml` and add `-IsoPath` to `scripts/xemu-oracle.ps1`;
   capture the xemu 0.8.139 gameplay frame for 007.
2. Re-run XGrid at 5B / 20B / 60B with `--save-state` anchors and a
   `--pad-script`, then write the wait report (EIP, thread/wait object, last
   device interaction, xemu state at the same guest time).
3. Select exactly one blocker with a measured citation and name the xemu
   0.8.139 file that owns the behavior (the port candidate for M-P).
4. Add a real-game row to `docs/compatibility.md` only after a run reaches a
   gameplay frame or a documented wait; a boot-animation-only run is a probe,
   not a compatibility row.
