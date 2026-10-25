# Retail title matrix (M-T)

Generated 2026-10-25T13:39:32 by `scripts/title-matrix.ps1` - steps=1000000000, gpu=sw, fast=True, padScript=down@950000000+hold,none@962000000,a@970000000+hold,none@976000000.

**44 titles: 6 own-render, 38 dashboard, 0 fault, 0 timeout.**

`dashboard` means the row matches a draw/pixel/frame tuple shared by two or more titles (the dashboard boot animation or menu); `own-render` means a unique tuple (the title took the display). `early` means fewer than 1B steps ran. `fault`/`timeout` are rows, not harness failures.

| # | title | source | stage | score | EIP | EIP zone | steps | wall s | msps | draws | pixels | frames | fault |
|---:|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | Ninja Gaiden Black (USA) (En,Ja).iso | oracle | own-render | 150 | 0x0021A97D | xbe | 1000000000 | 12.121 | 82.5 | 2672 | 41995882 | 6475 | 0 |
| 2 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | oracle | own-render | 150 | 0x002B893E | xbe | 1000000000 | 11.66 | 85.76 | 3701 | 41995882 | 1068 | 0 |
| 3 | 007 - Agent Under Fire [NTSC-U][Redump].iso | oracle | own-render | 130 | 0x000138D3 | xbe | 1000000000 | 12.28 | 81.43 | 2672 | 41995882 | 55 | 0 |
| 4 | 50 Cent - Bulletproof (France).iso | oracle | own-render | 120 | 0x001E0863 | xbe | 1000000000 | 31.276 | 31.97 | 2864 | 100987370 | 98 | 0 |
| 5 | Destroy All Humans! (Germany).iso | oracle | own-render | 110 | 0x8001434F | kernel | 1000000000 | 11.632 | 85.97 | 2672 | 41995882 | 34 | 0 |
| 6 | Bad Boys II (Germany).iso | oracle | own-render | 100 | 0x8001B02E | kernel | 1000000000 | 9.613 | 104.02 | 2673 | 42303082 | 35 | 0 |
| 7 | OutRun 2 (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.925 | 38.57 | 33638 | 253998498 | 686 | 0 |
| 8 | Obscure (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.163 | 38.22 | 33638 | 253998498 | 686 | 0 |
| 9 | Ninja Gaiden Black (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.221 | 38.14 | 33638 | 253998498 | 686 | 0 |
| 10 | Padrino, El (Spain).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.129 | 38.27 | 33638 | 253998498 | 686 | 0 |
| 11 | Grand Theft Auto - San Andreas (USA) (En,Es) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.425 | 37.84 | 33638 | 253998498 | 686 | 0 |
| 12 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.015 | 38.44 | 33638 | 253998498 | 686 | 0 |
| 13 | Phantom Dust (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.943 | 38.55 | 33638 | 253998498 | 686 | 0 |
| 14 | N.U.D.E.@ - Natural Ultimate Digital Experiment (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.298 | 38.03 | 33638 | 253998498 | 686 | 0 |
| 15 | Myst IV - Revelation (USA, Canada).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.255 | 38.09 | 33638 | 253998498 | 686 | 0 |
| 16 | Monster Garage (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.507 | 37.73 | 33638 | 253998498 | 686 | 0 |
| 17 | JSRF - Jet Set Radio Future (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.994 | 37.04 | 33638 | 253998498 | 686 | 0 |
| 18 | Halo 2 (USA, Europe) (En,Ja,Fr,De,Es,It,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.514 | 37.72 | 33638 | 253998498 | 686 | 0 |
| 19 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.939 | 37.12 | 33638 | 253998498 | 686 | 0 |
| 20 | Quantum Redshift (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.9 | 38.61 | 33638 | 253998498 | 686 | 0 |
| 21 | OutRun 2 (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.062 | 38.37 | 33638 | 253998498 | 686 | 0 |
| 22 | Gauntlet - Seven Sorrows [PAL][En,Fr,De,Es,It][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.637 | 37.54 | 33638 | 253998498 | 686 | 0 |
| 23 | Elder Scrolls III, The - Morrowind - Game of the Year Edition (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.873 | 37.21 | 33638 | 253998498 | 686 | 0 |
| 24 | Simpsons, The - Hit & Run (USA, Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.9 | 38.61 | 33638 | 253998498 | 686 | 0 |
| 25 | 007 - Agent Under Fire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.549 | 37.67 | 33638 | 253998498 | 686 | 0 |
| 26 | 007 - Everything or Nothing [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.28 | 38.05 | 33638 | 253998498 | 686 | 0 |
| 27 | 007 - From Russia with Love [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.653 | 37.52 | 33638 | 253998498 | 686 | 0 |
| 28 | 007 - Nightfire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.485 | 37.76 | 33638 | 253998498 | 686 | 0 |
| 29 | 50 Cent - Bulletproof (France).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 28.207 | 35.45 | 33638 | 253998498 | 686 | 0 |
| 30 | AFL Live Premiership Edition (Australia).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.784 | 37.34 | 33638 | 253998498 | 686 | 0 |
| 31 | Amped 2 (Europe, Australia) (En,Fr,De).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.613 | 37.58 | 33638 | 253998498 | 686 | 0 |
| 32 | Gauntlet - Seven Sorrows [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.701 | 37.45 | 33638 | 253998498 | 686 | 0 |
| 33 | Bad Boys II (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 27.201 | 36.76 | 33638 | 253998498 | 686 | 0 |
| 34 | Breakdown (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 27.122 | 36.87 | 33638 | 253998498 | 686 | 0 |
| 35 | Breakdown (USA) (Demo).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 27.017 | 37.01 | 33638 | 253998498 | 686 | 0 |
| 36 | Club Football - Chelsea FC (UK).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.427 | 37.84 | 33638 | 253998498 | 686 | 0 |
| 37 | Conker - Live & Reloaded (USA) (En,Ja,Fr,De,Es,It).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.218 | 38.14 | 33638 | 253998498 | 686 | 0 |
| 38 | Crimson Skies - High Road to Revenge (Japan) (En,Ja,Fr,De,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.129 | 38.27 | 33638 | 253998498 | 686 | 0 |
| 39 | Dead or Alive Xtreme Beach Volleyball (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.208 | 38.16 | 33638 | 253998498 | 686 | 0 |
| 40 | Destroy All Humans! (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.898 | 37.18 | 33638 | 253998498 | 686 | 0 |
| 41 | Battle Engine Aquila (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 28.864 | 34.65 | 33638 | 253998498 | 686 | 0 |
| 42 | Star Wars - Knights of the Old Republic (USA) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.975 | 38.5 | 33638 | 253998498 | 686 | 0 |
| 43 | Arx Fatalis (USA, Europe) (Beta) (2003-07-11).iso | compat | dashboard | 30 | 0x0013EC5C | xbe | 1000000000 | 20.353 | 49.13 | 2672 | 41995882 | 35 | 0 |
| 44 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | oracle | dashboard | 30 | 0x000AC3AC | xbe | 1000000000 | 10.321 | 96.89 | 2672 | 41995882 | 35 | 0 |

## Reading the table

- **own-render** rows are the closest to playable; probe them first (TM1).
- **dashboard** rows still present the shared dashboard display; their EIP says whether the title code or the kernel idle is executing.
- **fault** rows name the vector, EIP and CR2; a cluster of identical faults is one shared blocker.
- **timeout** rows are hangs or very slow titles; re-run them alone with a longer timeout before calling them blockers.

Full logs and per-title JSON are in `wt-data/title-matrix/`.
