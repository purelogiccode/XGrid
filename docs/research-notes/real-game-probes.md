# Real-game probes (local media, not gates)

Status: **2026-10-25.** Retail gameplay is the lead milestone (**M-T**, operator
decision 2026-10-24): titles must reach gameplay with input, verified against
xemu 0.8.139 on the same ISO. **007 Agent Under Fire is playable** (TM4); Halo is
the active TM5 target. These probes are **acceptance, not merge gates**; the
standing gates remain the 1B accurate goldens and the ktest smoke.

## Media

The oracle media is the **optimized XDVDFS set** at `H:\Tests\XBOXReady` - seven
ISOs, all verified as XDVDFS (`MICROSOFT*XBOX*MEDIA` at `0x10000`, `XBEH` in the
root `default.xbe`). They are local media: never committed, never CI, never gate
currency.

| File | Size | Notes |
|---|---:|---|
| `007 - Agent Under Fire [NTSC-U][Redump].iso` | 1.6 GB | **PLAYABLE** (TM4); the M-T target. |
| `Nickelodeon Tak - The Great Juju Challenge (Europe).iso` | 2.5 GB | own-render at 1B (TM3's second row). |
| `Halo - Combat Evolved (USA) (v1.09) (Redump).iso` | 3.5 GB | **TM5 target**; dashboard at 1B, own XBE code running. |
| `Ninja Gaiden Black (USA) (En,Ja).iso` | 6.0 GB | dashboard at 1B, own XBE code running. |
| `50 Cent - Bulletproof (France).iso` | 5.6 GB | dashboard at 1B, own XBE code running. |
| `Bad Boys II (Germany).iso` | 1.9 GB | dashboard at 1B, own XBE code running. |
| `Destroy All Humans! (Germany).iso` | 2.2 GB | dashboard at 1B, kernel EIP. |

**The 37 full-disc Redump ISOs at `H:\Tests\XBOXTest` are not oracle media:**
xemu 0.8.139 cannot boot them (operator, 2026-10-24). They remain XGrid-only
benchmark media. `D:\Samples\XBOXReady` no longer exists; 4x4 Evo 2 is not in
the current sets, so its kernel-idle probe is historical.

Boot media: the gate HDD (`wt-data\chain-199l\hdd.qcow2`, EvoX dashboard) and
the firmware set in `data\xemu` (`Complex_4627.bin`, `mcpx_1.0.bin`,
`eeprom.bin`).

## The oracle

**xemu 0.8.139** at `data\xemu\xemu.exe`, the same **optimized** ISO, the same
firmware/EEPROM. `data\xemu\xemu.toml` points `games_dir` at
`H:\Tests\XBOXReady`, and `scripts/xemu-oracle.ps1 -IsoPath <iso>` selects the
disc the way xemu actually supports it: a temporary config whose
`sys.files.dvd_path` is the ISO, passed with `-config_path` (xemu has no
`-cdrom`). The capture is the window's **client area** at 1:1 scale, taken by a
DPI-aware process. The QEMU monitor socket fails to bind on H1, so the capture is
a window screenshot; the comparison is the software frame at the same scene,
never a cross-renderer pixel-exact claim. **xemu cannot be driven on this host:**
its Xbox pad comes only from an SDL gamepad (`hw/xbox/usb-xbox-gamepad.c`) and
the configured GUID is absent, so it stays in the attract/intro loop - a retail
comparison is always at a scene both reach without input.

**2026-10-24 capture (SUPERSEDED - it was the wrong disc).** That run passed
`-cdrom`, which xemu does not have, and `Start-Process` split the ISO path on its
spaces, so xemu booted whatever its config pointed at: `wt-data/xemu-007-test.png`
is **not** 007 (it is a different title entirely). The real oracle capture is
`wt-data/tm4/xemu-007-150s.png`, taken after TM4 fixed `scripts/xemu-oracle.ps1`
(disc selected through `sys.files.dvd_path` + `-config_path`, DPI-aware client
capture); it shows 007's first-level driving scene and is the frame compared in
the TM4 outcome below. The historical contrast stands as a *progress* statement,
not as a 007 frame: XGrid at 1B was still presenting the boot animation
(`draws=2672`, `pixels=41,995,882`, EIP `0x000138B0`).

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

## TM2 outcome (2026-10-24)

**The fix.** `src/XGrid.Bus/Interrupts/Pic8259.cs` now models the edge/level
control register (0x4D0/0x4D1) and the xemu 0.8.139 `hw/intc/i8259.c` level
semantics, so a held PCI line's request survives the acknowledge and the EOI
re-presents it (decisions §253).

**007 advanced two stages.**

- **50B, dashboard pad script:** the guest presents **its own 007 Agent Under
  Fire title screen** (`wt-data/007-50b-fixed-display.png`: 640x480, 21,186
  colours, 49.7% black, EIP `0x1FD0E` in the XBE `.text`, 1,914 flips, no
  fault). TM1's same-step run was a frozen black transition.
- **26.5B, A press at 20B:** the game's own render (79,581 draws / 603,250,282 px
  / 2,594 frames) and then a `#PF` at **EIP `0x1C386E`, CR2=`0xE4`, err=0**
  inside the APU ISR (`PIC req=0x20 isr=0x20`, `APU ISTS=0x11`, `voiceOff=18`).
- **GTA: San Andreas (Redump) 5B:** the pre-fix 5B sweep presented the dashboard
  (801,359 draws / 5,447,672,941 px / 16,673 frames, EIP `0x39FC4`); the fixed
  build presents **the disc's own Xbox boot screen** ("SELECT A / B BACK",
  27,543 colours, 0.1% black) with 769,527 draws / 5,690,277,680 px / 17,410
  frames at EIP `0x2DC4F`, no fault. The NV2A IRQ3 half of the blocker is fixed
  for the Redump group too.

**The next blockers (TM3 candidates).**

1. **The APU ISR null-dereference** (007, 1 title): the ISR now runs (that is the
   fix), reads a null structure at `[0xE4]` and faults. `ISTS=0x11` is GINTSTS +
   FETINTSTS, so the ISR is handling an `idle-voice` FE trap when it faults. The
   xemu 0.8.139 files that own this are `hw/xbox/mcpx/apu/vp/vp.c` (the voice
   processor / FE trap semantics) and `apu.c`.
2. **The fast-mode `#UD`** (Halo `0x180AEC` at 604M, Tak `0x33C810` at 673M,
   Ninja Gaiden Black `0x30D63D` at 660M): one JIT correctness bug across **3 of
   the 7 oracle titles** - the matrix's ">=3 titles" threshold - with accurate
   mode clean. This leads TM3 by the matrix rule.
   **The Halo repro is deterministic** (verified twice on the post-TM2 build):
   the command in `wt-data/title-matrix-1b-fixed/*Halo*-8ce45d.json` faults at
   exactly **604,260,882 steps, `vec=6`, EIP `0x180AEC`**, 2,672 draws / 35
   frames, every counter identical across runs. TM0's "nondeterministic" note
   was a different flag set; `--jit --jit-threshold 16 --jit-spin --jit-raw` is
   stable. Start TM3 with `XGRID_BLOCK_TRACE=1` + `XGRID_RAW_DUMP=1` around that
   step and compare the IL and raw blocks at the faulting head.

## What to do next (TM3)

1. Reproduce the fast-mode `#UD` on the three titles with `--jit-raw` and the
   block trace (`XGRID_BLOCK_TRACE=1`, `XGRID_RAW_DUMP=1`); the differential is
   IL vs raw at the faulting block, and the acceptance is all three titles past
   the fault with the accurate anchors unchanged.
2. Then the 007 APU ISR fault: dump the guest code around `0x1C386E` to name the
   null structure; decide whether it is the FE-trap/VP path (port `vp.c`) or a
   kernel object, by the same matrix rule.
3. Re-run 007 past 26.5B; the acceptance is the intro FMV/gameplay, then TM4's
   playability gate.



## TM3 outcome (2026-10-24)

**The "fast-mode `#UD`" was a missing instruction, not a JIT bug.** The engine
ladder on the deterministic Halo repro settles it: `--jit-raw`, plain IL
(no `--jit-raw`), `--jit-exact` and **accurate mode** all fault at
`EIP=0x00180AEC` with `vec=6` (604,260,882 steps fast / 604,264,734 accurate;
the difference is interrupt timing). `--dumpv` shows the byte there is `0x9E`
= **SAHF**, the tail of the retail x87 loop `fprem; fnstsw ax; sahf; jp`, and
the decoder had no case for `0x9E` or `0x9F` (LAHF). The kernel then took the
`#UD` (EAX=`0x1E` exception number, ECX=`0xC000001D`) and halted - the
`0x800151EF` "last EIP" in the matrix rows is that handler, which is why three
titles with different fault addresses shared it.

**The fix.** `Decoder` 0x9E/0x9F -> `Mnemonic.Sahf`/`Lahf`; `XboxCpu` and the
spec-exec handlers implement the reference `gen_SAHF`/`gen_LAHF` rules
(SF/ZF/AF/PF/CF from AH bits 7/6/4/2/0, OF and every non-condition bit kept;
LAHF stores those five plus bit 1 into AH); the block executor continues through
both so the FPREM loop stays JIT-served. Decisions §254.

**Evidence.**

| level | result |
|---|---|
| unit / JIT / spec differentials | 15 tests; the pre-fix build throws `Unsupported instruction 0x9E` |
| independent oracle | 12 new QEMU-TCG (Unicorn) cases, corpus 629 -> **641**, all green; a mask mutation fails the oracle |
| titles | Halo `0xAC3AC` (fast) / `0xAC543` (accurate), Tak `0x39CE8B`, Ninja Gaiden Black `0x37DC15` - **1B steps, `LastFault vec=0`** |
| matrix (44 rows, 1B) | **2 own-render, 42 dashboard, 0 fault, 0 timeout** (was 2 / 39 / 3 / 0); **Tak is now the second own-render row** (2,675 draws / 41,995,882 px / 42 frames) |
| gates | four accurate 1B goldens byte-identical, four fast anchors exact, ktest `CA5D3867` (40,797 B) byte-identical at 792,800,535 steps, suite 1988 |

**The remaining one-byte decoder holes (filed, not fixed).** A temporary sweep of
all 256 one-byte opcodes reports: `27 2F 37 3F` (DAA/DAS/AAA/AAS), `62` (BOUND),
`82` (the 32-bit alias of the `0x80` group - QEMU accepts it), `C4 C5` (LES/LDS),
`D6` (SALC), `F1` (INT1). The reference implements all of them in 32-bit mode.
No measured row executes any of them today; if a title faults `#UD` again, check
this list before probing anything else.

**Next (TM4, 007).** The 007 `#PF` at 26.5B (`EIP 0x1C386E`, CR2=`0xE4`) inside
the APU ISR is still the title's blocker; the SAHF fix does not touch it.

## TM4 outcome (2026-10-24/25)

**007 Agent Under Fire is playable.** It runs **100,000,000,000 steps with no
fault** (`LastFault vec=0`, not halted), 6,478 frames / 134,004 draws /
1,005,989,482 pixels, and presents the game's own first-level driving scene (the
Z8 on the docks, `wt-data/tm4/007-100b-display.png`: 640x480, 48,208 colours,
6.8% black, mean `(78.7, 65.5, 45.3)`).

### The blocker: a duplicated `SE2FE_IDLE_VOICE`

The 26.5B `#PF` was not a guest bug. `McpxApuDevice` delivered the idle-voice FE
trap **twice per voice completion**: once from the VP frame walk (xemu-faithful)
and once from a pending queue that `VoiceOff` filled. xemu 0.8.139's `voice_off`
only clears `NV_PAVS_VOICE_PAR_STATE_ACTIVE_VOICE` and writes the notifier; the
**only** `SE2FE_IDLE_VOICE` sender is `mcpx_apu_vp_frame` (`vp.c:1833-1835`).

The guest's ISR frees the voice object on the first trap and leaves a null in the
device's 256-entry voice table; the duplicate trap then dereferences
`[null + 0xE4]`.

**The evidence chain** (all from a 26B snapshot, so each probe is ~5 s):

| step | observation |
|---|---|
| `--save-at 26000000000` | `wt-data/tm4/007-26b.xgs`, EIP `0x40B1F` |
| `--break 0x1C386E` (first visit) | ECX=`0xD00D9548` - a live voice object, no fault |
| `--break-log 0x1C3BF1` + `0x1C386E` | caller `0x1C3C22`, arg `0x2E`; the last visit has **ECX=0** |
| `--dumpv 0xD001C2C0,0x400` at the first call | the `DSND` device's voice table at `this+0x2C4` is fully populated |
| same dump at the fault | the whole 256-entry table is **zero** (the guest freed every voice) |
| APU state at the fault | `fedec=00008000/0000002E` (SE2FE_IDLE_VOICE, voice 46), `ISTS=0x11` |
| APU event log | `voice-off 46` x2, then `idle-voice 46` **x2** (the queued pair), then the walk's `idle-voice 47`, `idle-voice 46` |

The guest code at the fault is `0x1C3BF1`: `if (index >= 0x100) return;
if (this->[0x2C0]) return; if (table[index].flags & 0x800000) return;
Lookup(index)`, and `Lookup` (`0x1C3863`) is
`ecx = this->voices[index]; edx = [ecx+0xE4]; ...` - so a null table slot faults
at `0xE4`.

**The fix** (`src/XGrid.Audio/McpxApuDevice.cs`): the pending idle-voice queue is
gone. `CompleteVoiceOff` clears the active bit and writes the notifier; the VP
frame walk is the only trap sender. Snapshot **v5** drops the queue (v2-v4 still
load - the list is read and dropped).

**Evidence, three levels.** Three new tests fail on the pre-fix source
(`VoiceOff_UnlinkedVoiceSendsNoIdleVoiceTrap`,
`FrameAdvance_VoiceOffSendsOneIdleVoiceTrapNotTwo`,
`FrameAdvance_VoiceOffOnAnUnlinkedVoiceSendsNoTrap`); the gate titles execute
neither path differently - all four accurate 1B goldens are byte-identical and
the ktest log is unchanged.

### The 100B run

```
steps=100000000000  wall=1284.8 s  EIP=0x000214C2  LastFault vec=0  Halted=False
NV2A draws=134004 pixels=1005989482 frames=6478
APU ist=00000000 ien=00000AFB voiceOff=60
```

The pad script (`down`/`A` at 950M-976M, `A` at 20B) is the same one TM2 used;
the A edge at 20B is what launches the game from the dashboard.

### The xemu comparison (same scene)

`scripts/xemu-oracle.ps1` was broken for this (three real defects, fixed): xemu
has no `-cdrom` (the disc is `sys.files.dvd_path`, selected with `-config_path`),
an ISO path containing spaces was split into option tokens, and the capture was
DPI-virtualized so every shot was shifted into the window chrome.

xemu 0.8.139 with the same ISO reaches **the same first-level driving scene at
150 s** (`wt-data/tm4/xemu-007-150s.png`). `scripts/frame-compare.py --crop-top
0.25` (the documented scene policy: mean / colour count / coverage, never a
pixel-exact claim):

| frame | mean | colours | black |
|---|---|---|---|
| xemu 150 s | `(94.6, 87.6, 63.3)` | 43,921 | 4.1% |
| XGrid 100B | `(76.6, 64.9, 48.7)` | 39,912 | 4.2% |

`dmean=(18.0, 22.6, 14.6)`, `histL1=48.7`. Same framing, same letterbox, same
black coverage, the same palette family; XGrid renders ~15-23/255 darker and with
~9% fewer distinct colours (no cross-renderer pixel claim). The two frames are not
the same animation phase - xemu's explosion is at a different moment - so the
mean delta is an upper bound.

**xemu cannot be driven here.** Its Xbox pad input comes only from an SDL gamepad
(`hw/xbox/usb-xbox-gamepad.c`; the config binds a GUID that is not present), so
xemu sits in the attract/intro loop and never reaches gameplay by itself. The
comparable-scene comparison is therefore at the intro/driving scene, and the
scripted-input evidence comes from XGrid.

### Scripted input

From the **100B snapshot** (`wt-data/tm4/007-100b.xgs`), same state, same step
budget, only the pad differs:

| leg | steps | RAM SHA | CPU SHA | display frame |
|---|---:|---|---|---|
| no input | 50M | `68E22299` | `81D90AE1` | driving scene, 47,168 colours |
| left stick right | 50M | `0FFAF650` | `3AC5D22D` | same scene |
| left stick left | 50M | `946C590A` | `3AC5D22D` | same scene |
| no input | 300M | `A36065AE` | `80D8006F` | plane-crash cinematic, 13,754 colours |
| D-pad right (held) | 300M | `0CD320B0` | `435E4374` | same cinematic |
| **A (held)** | 300M | `604FBC7D` | `74FAA5FA` | **black** - the cutscene is skipped |

Every variant changes guest state; the A edge also changes the presented frame.
Across the whole boot the pad edge is what moves the guest between stages: at
20B with no pad the guest presents the Harrier intro
(`wt-data/tm4/007-20b-nopad-display.png`, 38,350 colours, mean
`(52.9, 39.7, 56.3)`), and with the A press it launches the game and reaches
gameplay.

### Audio: silent, measured

`--apu-dsp --audio-out` on 007 (28B steps, past the old fault, no fault) writes
**877,568 bytes / 219,392 frames** of 48 kHz 16-bit stereo PCM - and every sample
is **zero** (`peak 0`, `nonzero 0.0%`). The guest's DSP microcode runs
(`gpFrames=6914 gpUndefined=0 gpLimitHits=0 epFrames=863`), but the voices are
never mixed into the EP's input: the **VP mixer (`vp.c`) is not modelled**, and
the AC'97 codec has no PCM path. That is TM8's default scope, so part 6 of the
playability definition is a **documented silence**, not a claim.


## TM5 outcome (2026-10-25)

**Halo launched and rendered its own main menu.** The blocker was ours: the APU
DSP bridge knew only 007's mixer-block address.

### The blocker: the mixer block was hard-coded to 007's page

From the 10B snapshot (no fault) the guest spins at `0x195EC0`
(`cmp dword [eax+0x10],0; jne`) on a DirectSound mixer state word at
`0x82034810` (phys `0x02034810`) holding 3 (parameters loaded). The block is the
XDK DirectSound submission: a six-dword descriptor at scratch page + 0x800 and
its state word at + 0x810 (`2` = submit posted, `3` = parameters loaded; the DSP
writes `0` when it consumes the block). 007 posts the **same** block at
`0x03924800` - and `McpxApuDevice` carried that address as a constant
(`DspStateWordPhys`), so only 007's submission was ever absorbed.

`McpxApuDevice` now discovers the block from the guest's own GP scratch
(`GPSADDR`) SGE table: for every mapped page it reads page + 0x810 and absorbs a
`2`/`3` (`TryGetMixerStateWord`). The per-title constant is gone; the mapping is
the guest's, and the DSP can only touch guest RAM through it.

Evidence: with the fix the log reads `dsp-absorb 3->0 n=1`, `2->0 n=2`,
`3->0 n=3` and the guest leaves the spin at once (EIP `0x1C8FC0`). Three new
tests; two fail on the pre-fix source
(`Absorb_StateWordDiscoveredFromTheScratchMapping`,
`Absorb_PostedSubmitOnASecondScratchPageRaisesBit3`).

### The progress

| anchor | steps | draws | pixels | frames | state |
|---|---:|---:|---:|---:|---|
| 1B matrix | 1B | 2,672 | 41,995,882 | 35 | dashboard animation, own XBE code |
| first probe | 1.5B | - | - | - | **the mixer wait** (EIP `0x195EC0`) |
| +0.5B after the fix | 10.5B | 2,694 | 48,139,882 | 56 | past the wait (EIP `0x1C8FC0`) |
| 15B | 15B | 2,889 | 108,043,882 | 251 | **Halo loading screen** (blue capsules + clock) |
| 35B | 35B | 3,247 | 191,496,512 | 516 | **Halo main menu** |
| 100B | 100B | 3,247 | 191,496,512 | 516 | unchanged - the second wait |

The frames: `wt-data/tm5/halo-15b-display.png` (loading) and
`wt-data/tm5/halo-35b-display.png` (the main menu: HALO / CAMPAIGN /
MULTIPLAYER / SETTINGS / GAME DEMOS, 97,235 colours, mean (153.5, 157.7, 176.0),
0% black).

### The 1B matrix moved: 6 own-render rows (was 2)

The same fix is what the matrix rule looks for - one shared blocker across many
titles. Re-running `scripts/title-matrix.ps1` (44 rows, 1B, same media and pad
script) gives **6 own-render / 38 dashboard / 0 fault / 0 timeout**, and the
oracle rows show it:

| title | before (TM4 matrix) | after (TM5) |
|---|---|---|
| Ninja Gaiden Black | 2,672 / 41,995,882 / **35** frames, EIP `0x37DC15` | 2,672 / 41,995,882 / **6,475** frames, EIP `0x21A97D` |
| Tak | 2,675 / 41,995,882 / 42 frames | 3,701 / 41,995,882 / **1,068** frames, EIP `0x2B893E` |
| 50 Cent | 2,672 / 41,995,882 / 34 frames | 2,864 / **100,987,370** / 98 frames, EIP `0x1E0863` |
| Bad Boys II | 2,672 / 41,995,882 / 34 frames | 2,673 / **42,303,082** / 35 frames, EIP `0x8001B02E` |
| Destroy All Humans! | dashboard menu 33,638 / 253,998,498 / 686 | 2,672 / 41,995,882 / 34 frames, EIP `0x8001434F` |
| 007 / Halo | 2,672 / 41,995,882 / 55 and 35 | unchanged |

The differential is clean: the **pre-fix** build reproduces Ninja Gaiden Black's
old row exactly (2,672 / 41,995,882 / 35 frames, EIP `0x37DC15`), and the fixed
build logs `dsp-absorb 3->0`, `2->0`, `2->0`, `2->0` on the same run. Five of the
seven oracle titles now leave the boot animation at 1B. Two caveats, stated
rather than hidden: `own-render` is a tuple-uniqueness rule, so Destroy All
Humans! (34 frames vs the animation's 35) counts as one although it only changed
stage, and the 37 XBOXTest rows are unchanged (they present the dashboard menu
tuple).

### The remaining wait: a guest D3D resource retry loop

The menu renders, then the guest busy-waits:

```
001810F0  mov eax,[esp+4]              ; handle
001810F4  mov edx,[0x192390]           ; resource table (0x18F800)
001810FC  shr ecx,8                    ; bucket
001810FF  and eax,0xFF                 ; slot
00181105  mov esi,[edx+ecx*4+0x48C]    ; bucket 0 -> 0x80060000
0018110F  mov ecx,[eax+esi+0xC]        ; slot field
00181115  cmp ecx,-1
00181119  jne 0x181123
0018111B  mov eax,0x88760828           ; D3DERR_NOTFOUND
00181120  ret 0xC
...
00063476  call 0x1810F0
0006347B  cmp eax,0x88760828
00063480  jne 0x63499                ; resolved -> continue
00063482  ... retry while NOTFOUND
```

The handle is **0** on every call (`--break-log`: `arg=00000000`, ESI=0), the
bucket pointer at `[0x18FC8C]` is `0x80060000` (which maps to physical
`0x00060000`, Halo's own code image) and the slot field is `-1`, so the lookup
never resolves. 99.99% of 280M sampled instructions are the 23 EIPs of this
loop; `--eip-hist 40` puts every other EIP at 46-381 samples of 7,000,000.

Last device I/O at the park: IDE `lastCmd=0xC8` (READ DMA, 32,216 accesses),
APU `reads=14,173,492 writes=14,173,551 voiceOff=7` with the mixer absorbs,
NV2A 538 notifications / 3,247 draws / 191,496,512 px / 516 frames, PIC
`elcr=0x78/0x18` (IRQ3 level) `req=0x00`. Interrupts keep arriving (165,903 by
100B) and `LastFault vec=0`, so the guest is not wedged: it waits for a
guest-side resource that never appears.

The pad does reach the guest: an A press at 19.5B changes the frame phase
(frames 449 vs 511 at the same budget) but does not move the menu, because the
menu logic is already inside the wait.

### The xemu comparison

`scripts/xemu-oracle.ps1` on the same ISO reaches **the same main menu at 70 s**
(`wt-data/tm5/xemu-halo-70s.png`) and **in-game gameplay at 150 s**
(`wt-data/tm5/xemu-halo-150s.png`: first-person Master Chief, HUD, "RELOAD"
prompt) - so the remaining wait is ours, not the title's.

The capture had to be fixed twice as well: a screen copy caught the editor
behind xemu (the window had lost the top slot), so the script re-asserts
topmost/foreground and now captures through `PrintWindow` (client-only), falling
back to the screen copy when the GL surface answers black.

**Screenshot policy at the menu: not met.** xemu's menu at 70 s reads mean
(44.0, 49.5, 54.9) with 31,998 colours and 0.0% black; XGrid's at 35B reads
(145.9, 150.1, 168.2) with 39,040 colours and 0.0% black -
`dmean=(101.8, 100.6, 113.3)`, far outside the 25/255 bar. The scene is the same
(the logo and the four entries are in both) but XGrid's menu background renders
far brighter, so no frame-policy claim is made for Halo.

### Audio: silent, measured

`--apu-dsp --audio-out` on Halo writes a 44-byte WAV header and **0 PCM frames**
(`gpFrames=0 epFrames=0`): the guest starts the GP/EP (`GP DSP start`, `EP DSP
start` in the APU log) but the pair never runs a frame in this state. Same TM8
gap as 007, one stage earlier.

### Gates

Not one anchor moved: accurate menu `4EF1FBF6` / `B6116DAE`
(`5C0CF968`/`E2B4D327`, 33,632/686/686), accurate triangle `5B50373D` /
`79142DE4` (`E58AE5D7`/`C43D5B49`, 2,707/69/34), ktest `CA5D3867` (40,797 B) at
792,800,535 steps. Suite 1990 -> 1993, build 0 warnings, format clean.
