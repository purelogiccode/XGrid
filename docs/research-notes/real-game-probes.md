# Real-game probes (local media, not gates)

Status: **2026-10-24.** Retail gameplay is now the lead milestone (**M-G**,
operator decision 2026-10-24): one retail title must reach a gameplay frame at
any speed, verified against xemu 0.8.139 on the same ISO. These probes are
**acceptance, not merge gates**; the standing gates remain the 1B accurate
goldens and the ktest smoke.

## Media

The oracle media is the **optimized XDVDFS set** at `H:\Tests\XBOXReady`. Both
files were verified as XDVDFS (`MICROSOFT*XBOX*MEDIA` at `0x10000`, `XBEH` at
`0x84800`). They are local media: never committed, never CI, never gate
currency.

| File | Size | Notes |
|---|---:|---|
| `H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso` | 1.60 GB | **M-T target**; the XBE executes today. |
| `H:\Tests\XBOXReady\Halo - Combat Evolved (USA) (v1.09) (Redump).iso` | 3.38 GB | second title (the only other optimized image). |

**The 37 full-disc Redump ISOs at `H:\Tests\XBOXTest` are not oracle media:**
xemu 0.8.139 cannot boot them (operator, 2026-10-24). They remain XGrid-only
benchmark media. `D:\Samples\XBOXReady` no longer exists; 4x4 Evo 2 is not in
the current sets, so its kernel-idle probe is historical.

Boot media: the gate HDD (`wt-data\chain-199l\hdd.qcow2`, EvoX dashboard) and
the firmware set in `data\xemu` (`Complex_4627.bin`, `mcpx_1.0.bin`,
`eeprom.bin`).

## The oracle

**xemu 0.8.139** at `data\xemu\xemu.exe`, the same **optimized** ISO, the same
firmware/EEPROM, the same scripted input. `data\xemu\xemu.toml` now points
`games_dir` at `H:\Tests\XBOXReady`, and `scripts/xemu-oracle.ps1` takes
`-IsoPath` and passes it as `-cdrom` (the capture is the window's client area).
The QEMU monitor socket fails to bind on H1, so the capture is a window
screenshot; the comparison is the software frame at the same scene, never a
cross-renderer pixel-exact claim.

**2026-10-24 capture:** xemu 0.8.139 reaches a **007 gameplay frame within
15 s** of wall time on H1 (a beach/pool scene with the player character and a
flamethrower), captured at `wt-data/xemu-007-test.png`. The capture carries
xemu's performance overlay, so the clean 640x480 oracle (overlay off, same
scene) is a TM1 step. The contrast is the M-T measurement: **XGrid at 1B steps
is still presenting the boot animation** (`draws=2672`, `pixels=41,995,882`,
EIP `0x000138B0`), while xemu is already in gameplay. The gap is guest
progress, not just speed.

## Command

```powershell
pwsh -File scripts/boot-to-first-frame.ps1 `
  -Sample 007 `
  -IsoPath "H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso" `
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

## M-T matrix findings (2026-10-24)

**The harness.** `scripts/title-matrix.ps1` runs every ISO at a fixed budget
with the dashboard pad script, records the stage/EIP/NV2A counters, and writes
`docs/research-notes/title-matrix.md`. `scripts/title-probe.ps1` is the
follow-up for one title (`--save-state`, `--pgraph`, `--eip-hist`,
`--jit-timing`).

**The dashboard launches a disc only after a pad edge.** Without
`--pad-script`, every title stays on the dashboard's shared display. The
default script (`down@950M+hold, none@962M, a@970M+hold, none@976M`) is the
same one the dashboard corpus row uses.

**The 37 Redump ISOs at `H:\Tests\XBOXTest` do not launch through the
dashboard.** At 1B/5B every one of them presents the same dashboard
draw/pixel/frame tuple as the optimized 007 (33,811 draws / 252,795,848 px /
682 frames at 1B; 801,359 / 5,447,672,941 / 16,673 at 5B), i.e. the shared
dashboard display, not their own render. The optimized 007 at 2B with the pad
script is still the boot animation (2,672 draws / 41,995,882 px / 93 frames),
while the dashboard-only run at 2B is the menu (4,867 frames). The DVD's
presence changes the dashboard's boot path; the disc reads complete (ATAPI
`0x28` at LBA 0x643F8..0x64670), but the guest then sits in a kernel idle spin
(`0x800422B0`: `mov eax,[esp]; mov [rsp],rax; jnz`), with the PIC reporting
`req=0x00` and the NV2A `asserted=True` (107 notifications).

**The game's own render is reached at 20B.** The 20B snapshot (no pad script)
presents the 007 Harrier/carrier intro scene (`wt-data/xgs-20b-display.png`),
so the game has launched and is rendering its own content. The 10B run is
still the dashboard animation; the launch happens between 10B and 20B. The
NV2A counters (2,672 draws / 41,995,882 px) coincidentally match the boot
animation, so a draw counter cannot classify a row - export the display frame
and compare it (`--frame-display`). The next probe is the game-menu input at
~20B (A) and a 30B/50B run to reach the intro FMV/gameplay.

**The pad script can delay the auto-launch.** The no-pad 20B snapshot is the
Harrier intro; the pad-script 20B probe is still the dashboard animation
(764 frames, 2,672 draws). The 30B pad run reaches the game's own code
(79,581 draws). Use the no-pad path for the intro and the pad only for menu
input after the game is up; do not over-interpret the 950M-976M press timing.

**At 30B the game is rendering and the APU is live.** The 30B run (pad A at
20B) records 79,581 draws / 603,250,282 px / 2,594 frames, EIP `0x1C478D`,
APU ISTS `0x79` with 15 voices completed, no fault; the display frame is black
(a transition/loading screen), so gameplay is not yet reached. The game's own
code (`0x13xxxx`-`0x1Cxxxx`) is running, not the dashboard. The next anchor is
40B/50B, where the intro FMV/gameplay should appear.

**Halo (fast mode) faults at 592M with `#UD`; accurate mode does not.** The
fast-mode probe reports `vec=6` at `EIP=0x180AEC`, while the accurate run at
the same step has `LastFault vec=0` and runs past it. The fault is a fast-mode
JIT bug (nondeterministic: a second fast run without the probe flags did not
fault), not a missing guest instruction. Retail probes use accurate mode
(`-NoFast`) or `--jit-exact` for correctness; the fast mode stays a speed
experiment. Record it as a JIT correctness item, not a title blocker.

## What to do next (TM1)

1. 007 at 30B/50B in **accurate mode** (or `--jit-exact`) with the game-menu A
   press, exporting the display frame at each anchor; the 20B frame is the
   Harrier intro.
2. Halo at 20B in **accurate mode** (the fast-mode `#UD` is a JIT bug, not a
   title blocker); name its first own-render frame or its park.
3. Compare the 007/Halo frames with xemu 0.8.139 (the gameplay capture is
   `wt-data/xemu-007-test.png`) and name the next blocker.
4. Add a real-game row to `docs/compatibility.md` only after a run reaches a
   gameplay frame or a documented wait; a dashboard-only run is a probe, not a
   compatibility row.
