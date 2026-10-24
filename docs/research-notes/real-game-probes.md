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

## TM1 findings (2026-10-24)

**Ranking.** The 1B matrix ranks **007** first (its own render at 20B), **Halo**
second (the only other oracle ISO; fast-mode fault, accurate mode clean) and the
**36-row Redump group** third (the shared dashboard display at 1B). The probes
name one shared device bug and two independent items.

### B1 - a level-triggered IRQ is lost after its first acknowledge (shared)

The 007 stall and the Redump group's dashboard park are **the same defect**: a
device holds its PCI interrupt line high, the PIC latched the request once,
`AcknowledgeLine` cleared the request bit, and nothing ever re-latched the level.
The device's own `SetAsserted` de-dupes, so no second edge is produced.

**007 - Agent Under Fire, the anchor table (fast engine, `--gpu sw`, HDD+DVD):**

| steps | stage | EIP | draws | pixels | frames | wall | source |
|---:|---|---|---:|---:|---:|---:|---|
| 1B | boot animation | 0x138D3 | 2,672 | 41,995,882 | 55 | 18.0 s | `wt-data/title-matrix-1b` |
| 10B | boot animation | - | 2,672 | 41,995,882 | - | - | `wt-data/title-probe-007-10b` |
| 20B (no pad) | **own render** (Harrier/carrier intro) | 0x21307 | 2,672 | 41,995,882 | 764 | 1486 s | `wt-data/title-probe-007-20b` |
| 30B (A at 20B) | own render, **stalled** | 0x1C478D | 79,581 | 603,250,282 | 2,594 | 396.7 s | `wt-data/title-matrix-007-30b` |
| 50B (A at 20B) | own render, **stalled** | 0x1C4787 | 79,581 | 603,250,282 | 2,594 | 504.1 s | `wt-data/title-matrix-007-50b` |

30B and 50B are **counter-identical** (same draws/pixels/frames, same EIP
neighbourhood): the guest stopped rendering at 30B and spent the next 20B steps
in one loop. The 30B save state is `wt-data/title-probe-007-30b/*.xgs`.

**The device evidence at the 50B park** (`wt-data/title-matrix-007-50b/logs/`):

```
PIC master base=0x30 mask=0x98 slaveMask=0xA6 req=0x00 isr=0x00
Interrupts delivered=87784
NV2A irq pgraphEn=FFFFFFFF pgraphPending=00000000 pmcEn=00000001 notifications=2615 asserted=True
APU ist=00000079 ien=00000AFB fectl=00001FEF asserted=True reads=5323 writes=25034 voiceOff=15
APU log: idle-voice 237
APU log: raise ist=00000079 ien=00000AFB fectl=00001FEF      (repeated ~60x in the ring)
```

`APU asserted=True` with `PIC req=0x00` and IRQ5 unmasked (`mask=0x98`) is the
bug in one line: the APU's level line is high, the PIC does not have the
request. `ists=0x79` carries GINTSTS plus the voice-completion bits the ISR
would clear, and `voiceOff=15` says 15 voices completed before the stall.

**The same line shape parks the Redump group.** A 1B Redump row (GTA: San
Andreas) ends with

```
PIC master base=0x30 mask=0xE0 slaveMask=0xA6 req=0x00 isr=0x00
NV2A irq pgraphEn=FFFFFFFF pgraphPending=00000000 pmcEn=00000001 notifications=700 asserted=True
EIP=0x800422B0  (kernel idle spin: mov eax,[esp]; mov [rsp],rax; jnz)
```

`NV2A asserted=True` (its level line is held by a pending source) with
`PIC req=0x00` and IRQ3 unmasked (`mask=0xE0` masks only 5-7) is the same lost
level on a different line. The guest is in the kernel idle thread, which only
leaves on an interrupt.

**Owner.** xemu 0.8.139 `hw/intc/i8259.c`: `pic_set_irq` keeps `irr` set while a
level line is high (`s->ltim || (s->elcr & mask)`), and `pic_intack` does **not**
clear `irr` for a level line (`if (!s->ltim && !(s->elcr & (1 << irq)))`). XGrid
`src/XGrid.Bus/Interrupts/Pic8259.cs` clears `RequestRegister` on every
acknowledge and ignores ICW1 bit 3 (LTIM), so a held level is never re-delivered.

**The measured citation (TM2's first probe).** A temporary port trace shows the
guest programs the **edge/level control register** and XGrid discarded the writes
(nothing was mapped at 0x4D0/0x4D1):

```
UNMAPPED PORT WRITE 4D0<=0 / 4D0<=40 / 4D0<=60 / 4D0<=68   (master: IRQ3 NV2A, IRQ5 APU, IRQ6 ACI level)
UNMAPPED PORT WRITE 4D1<=0 / 4D1<=08 / 4D1<=18              (slave:  IRQ11 USB0, IRQ12 USB1 level)
```

The PIC programming is otherwise edge mode (`ICW1 = 0x11` on 0x20 and 0xA0), so
the ELCR is the only level switch - and it was dropped. That is the whole
blocker: the guest asks for level delivery on exactly the PCI lines, the
emulator answers with edge-only latches, and every held line is lost after its
first acknowledge.

**The method histogram at the park** (`wt-data/title-probe-007-30b/`, 30B steps,
`--pgraph` + `--eip-hist 24`, wall 1254 s): the stall is a guest spin -
`EIP=0x1C4787` at the sample, top EIP `0x1C478D` with **73,815,723** hits - and
the NV097 stream is the boot-animation/intro set (`0x1800` x58577, `0x1EA4`
x9185, `0x0B80`-`0x0B9C` constants, `0x17FC` x5344, `0x1D90` x3860), i.e. the
game's own draws use the same method families the engine already runs. The 30B
save state is `wt-data/title-probe-007-30b/*.xgs` (72 MB).

**xemu 0.8.139 at the same scene:** 007 reaches a gameplay frame (beach/pool,
player + flamethrower) within 15 s of boot, `wt-data/xemu-007-test.png`. XGrid
(pre-fix) at 50B is still in a black transition, so the gap is guest progress,
not pixels.

### B2 - the Halo fast-mode `#UD` is a JIT correctness item, not a title blocker

The optimized Halo row faults at **592-604M steps**, `vec=6` (`#UD`) at
`EIP=0x180AEC`, only in fast mode; the accurate run at the same step has
`LastFault vec=0` and runs past it. The fault is not deterministic across fast
runs (a second fast run without the probe flags did not fault), so it is a
raw-engine correctness item, not a missing guest instruction. Retail probes use
`-NoFast` / `--jit-exact` until it is fixed.

**xemu 0.8.139 at the same scene:** the optimized Halo ISO reaches its beach
gameplay within 50 s (`wt-data/xemu-halo-test.png`, palm/beach scene). XGrid at
1B is still on the dashboard animation, so Halo's blocker is guest progress
(B1-class: the dashboard never launches the disc), not its own render path.

### B3 - the Redump group never launches through the dashboard

36 of 39 rows present the shared dashboard display at 1B (`33,638` draws /
`253,998,498` px / `686` frames). The disc reads complete, then the guest parks
in the kernel idle spin with the NV2A line asserted and no PIC request (B1).
xemu 0.8.139 cannot boot these images at all, so they are XGrid-only
compatibility media: they may prove a blocker, never an oracle row.

### The selected shared blocker

**B1** (level-triggered IRQ re-latch) is the TM2 fix: it is the only blocker
that stops the closest title from progressing, it is one device-correctness
bug, and it covers the 007 stall and the 36-row dashboard park (same line
shape, two different devices). B2 is recorded as a JIT correctness item; B3
follows B1 (re-run the matrix after the fix to see how many rows leave the
dashboard).

## What to do next (TM2)

1. Fix B1 in `Pic8259` (model the input level and ICW1 LTIM; re-latch on EOI and
   unmask), with a differential test that fails on the current source.
2. Re-run 007 from the 30B save state (and then a full 30B/50B run) with the
   frame export; the acceptance is that it leaves `0x1C4787` and the intro
   FMV/gameplay appears.
3. Re-run the matrix and the four accurate goldens + the ktest log.
4. Add a real-game row to `docs/compatibility.md` only after a run reaches a
   gameplay frame or a documented wait; a dashboard-only run is a probe, not a
   compatibility row.
