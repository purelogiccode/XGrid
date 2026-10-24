# Retail title matrix (M-T)

Generated 2026-10-24T19:40:28 by `scripts/title-matrix.ps1` - steps=1000000000, gpu=sw, fast=True, padScript=down@950000000+hold,none@962000000,a@970000000+hold,none@976000000.

**39 titles: 2 own-render, 36 dashboard, 1 fault, 0 timeout.**

`dashboard` means the row matches a draw/pixel/frame tuple shared by two or more titles (the dashboard boot animation or menu); `own-render` means a unique tuple (the title took the display). `early` means fewer than 1B steps ran. `fault`/`timeout` are rows, not harness failures.

| # | title | source | stage | score | EIP | EIP zone | steps | wall s | msps | draws | pixels | frames | fault |
|---:|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 007 - Agent Under Fire [NTSC-U][Redump].iso | oracle | own-render | 130 | 0x000138D3 | xbe | 1000000000 | 18.028 | 55.47 | 2672 | 41995882 | 55 | 0 |
| 2 | Arx Fatalis (USA, Europe) (Beta) (2003-07-11).iso | compat | own-render | 130 | 0x0013F624 | xbe | 1000000000 | 34.211 | 29.23 | 2672 | 41995882 | 35 | 0 |
| 3 | Grand Theft Auto - San Andreas (USA) (En,Es) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 45.794 | 21.84 | 33638 | 253998498 | 686 | 0 |
| 4 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 42.706 | 23.42 | 33638 | 253998498 | 686 | 0 |
| 5 | Halo 2 (USA, Europe) (En,Ja,Fr,De,Es,It,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.191 | 22.63 | 33638 | 253998498 | 686 | 0 |
| 6 | JSRF - Jet Set Radio Future (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 43.892 | 22.78 | 33638 | 253998498 | 686 | 0 |
| 7 | Monster Garage (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 41.242 | 24.25 | 33638 | 253998498 | 686 | 0 |
| 8 | Myst IV - Revelation (USA, Canada).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 42.501 | 23.53 | 33638 | 253998498 | 686 | 0 |
| 9 | N.U.D.E.@ - Natural Ultimate Digital Experiment (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 39.708 | 25.18 | 33638 | 253998498 | 686 | 0 |
| 10 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 47.857 | 20.9 | 33638 | 253998498 | 686 | 0 |
| 11 | Ninja Gaiden Black (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.74 | 22.35 | 33638 | 253998498 | 686 | 0 |
| 12 | Obscure (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 41.107 | 24.33 | 33638 | 253998498 | 686 | 0 |
| 13 | OutRun 2 (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 41.657 | 24.01 | 33638 | 253998498 | 686 | 0 |
| 14 | OutRun 2 (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 40.629 | 24.61 | 33638 | 253998498 | 686 | 0 |
| 15 | Padrino, El (Spain).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 40.569 | 24.65 | 33638 | 253998498 | 686 | 0 |
| 16 | Phantom Dust (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 42.356 | 23.61 | 33638 | 253998498 | 686 | 0 |
| 17 | Quantum Redshift (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 51.067 | 19.58 | 33638 | 253998498 | 686 | 0 |
| 18 | Gauntlet - Seven Sorrows [PAL][En,Fr,De,Es,It][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.602 | 22.42 | 33638 | 253998498 | 686 | 0 |
| 19 | Simpsons, The - Hit & Run (USA, Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 41.816 | 23.91 | 33638 | 253998498 | 686 | 0 |
| 20 | Gauntlet - Seven Sorrows [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.07 | 22.69 | 33638 | 253998498 | 686 | 0 |
| 21 | Destroy All Humans! (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.314 | 22.57 | 33638 | 253998498 | 686 | 0 |
| 22 | 007 - Agent Under Fire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 32.561 | 30.71 | 33638 | 253998498 | 686 | 0 |
| 23 | 007 - Everything or Nothing [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 47.489 | 21.06 | 33638 | 253998498 | 686 | 0 |
| 24 | 007 - From Russia with Love [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 43.294 | 23.1 | 33638 | 253998498 | 686 | 0 |
| 25 | 007 - Nightfire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 38.086 | 26.26 | 33638 | 253998498 | 686 | 0 |
| 26 | 50 Cent - Bulletproof (France).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 39.653 | 25.22 | 33638 | 253998498 | 686 | 0 |
| 27 | AFL Live Premiership Edition (Australia).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 45.333 | 22.06 | 33638 | 253998498 | 686 | 0 |
| 28 | Amped 2 (Europe, Australia) (En,Fr,De).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.69 | 22.38 | 33638 | 253998498 | 686 | 0 |
| 29 | Elder Scrolls III, The - Morrowind - Game of the Year Edition (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 47.511 | 21.05 | 33638 | 253998498 | 686 | 0 |
| 30 | Bad Boys II (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.593 | 22.42 | 33638 | 253998498 | 686 | 0 |
| 31 | Breakdown (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 44.779 | 22.33 | 33638 | 253998498 | 686 | 0 |
| 32 | Breakdown (USA) (Demo).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 42.618 | 23.46 | 33638 | 253998498 | 686 | 0 |
| 33 | Club Football - Chelsea FC (UK).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 43.501 | 22.99 | 33638 | 253998498 | 686 | 0 |
| 34 | Conker - Live & Reloaded (USA) (En,Ja,Fr,De,Es,It).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 43.552 | 22.96 | 33638 | 253998498 | 686 | 0 |
| 35 | Crimson Skies - High Road to Revenge (Japan) (En,Ja,Fr,De,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 47.576 | 21.02 | 33638 | 253998498 | 686 | 0 |
| 36 | Dead or Alive Xtreme Beach Volleyball (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 46.647 | 21.44 | 33638 | 253998498 | 686 | 0 |
| 37 | Battle Engine Aquila (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 41.761 | 23.95 | 33638 | 253998498 | 686 | 0 |
| 38 | Star Wars - Knights of the Old Republic (USA) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 40.66 | 24.59 | 33638 | 253998498 | 686 | 0 |
| 39 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | oracle | fault | -990 | 0x800151EF | kernel | 604260882 | 12.016 | 50.29 | 2672 | 41995882 | 35 | 6 |

## Reading the table

- **own-render** rows are the closest to playable; probe them first (TM1).
- **dashboard** rows still present the shared dashboard display; their EIP says whether the title code or the kernel idle is executing.
- **fault** rows name the vector, EIP and CR2; a cluster of identical faults is one shared blocker.
- **timeout** rows are hangs or very slow titles; re-run them alone with a longer timeout before calling them blockers.

Full logs and per-title JSON are in `wt-data/title-matrix/`.

## Findings (2026-10-24, TM0)

- **36 of 39 rows are the shared dashboard menu** (`33,638` draws /
  `253,998,498` px / `686` frames, EIP `0x3A057`). The 37 full-disc Redump
  ISOs at `H:\Tests\XBOXTest` do not launch through the dashboard; they are
  not a usable retail corpus until a direct-XBE boot path exists.
- **The optimized 007 is the boot animation at 1B** (`2,672` draws /
  `41,995,882` px / `55` frames, EIP `0x138D3`). It reaches its own
  Harrier/carrier intro at **20B** (`wt-data/xgs-20b-display.png`, frame
  `2666CFBC...`) and stalls at **30B/50B** in a wait loop at EIP `0x1C4787`
  (79,581 draws / 603,250,282 px / 2,594 frames, black display).
- **The 30B/50B stall is an APU interrupt-delivery problem.** The APU log
  repeats `idle-voice 237` / `raise ist=00000079`, `InterruptAsserted=True`,
  but the PIC reports `req=0x00`. The APU level line stays high; the PIC
  clears the request bit on acknowledge and never re-latches it, and the
  device's `SetAsserted` dedupes, so no new edge is produced. The first TM2
  candidate is the PIC level re-latch (`src/XGrid.Bus/Interrupts/Pic8259.cs`).
- **Halo fast mode `#UD` at 604M** (EIP `0x180AEC`); accurate mode runs past
  it. This is a fast-mode JIT bug, not a title blocker. Retail probes use
  `-NoFast` or `--jit-exact`.
- **The xemu oracle reaches 007 gameplay in 15 s** (`wt-data/xemu-007-test.png`).
  The comparison scene is the beach/pool gameplay frame, not the 20B intro.
