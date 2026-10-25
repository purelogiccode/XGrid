# Retail title matrix (M-T)

Generated 2026-10-25T16:11:40 by `scripts/title-matrix.ps1` - steps=1000000000, gpu=sw, fast=True, padScript=down@950000000+hold,none@962000000,a@970000000+hold,none@976000000.

**44 titles: 6 own-render, 38 dashboard, 0 fault, 0 timeout.**

`dashboard` means the row matches a draw/pixel/frame tuple shared by two or more titles (the dashboard boot animation or menu); `own-render` means a unique tuple (the title took the display). `early` means fewer than 1B steps ran. `fault`/`timeout` are rows, not harness failures.

| # | title | source | stage | score | EIP | EIP zone | steps | wall s | msps | draws | pixels | frames | fault |
|---:|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | Ninja Gaiden Black (USA) (En,Ja).iso | oracle | own-render | 150 | 0x0021A97D | xbe | 1000000000 | 36.282 | 27.56 | 12800 | 166119138 | 6475 | 0 |
| 2 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | oracle | own-render | 150 | 0x002B893E | xbe | 1000000000 | 12.5 | 80 | 3701 | 41995882 | 1068 | 0 |
| 3 | 007 - Agent Under Fire [NTSC-U][Redump].iso | oracle | own-render | 130 | 0x000138D3 | xbe | 1000000000 | 13.223 | 75.63 | 2672 | 41995882 | 55 | 0 |
| 4 | 50 Cent - Bulletproof (France).iso | oracle | own-render | 120 | 0x001E0863 | xbe | 1000000000 | 31.34 | 31.91 | 2864 | 100987370 | 98 | 0 |
| 5 | Destroy All Humans! (Germany).iso | oracle | own-render | 110 | 0x8001434F | kernel | 1000000000 | 12.788 | 78.2 | 2672 | 41995882 | 34 | 0 |
| 6 | Bad Boys II (Germany).iso | oracle | own-render | 100 | 0x8001B02E | kernel | 1000000000 | 10.703 | 93.44 | 2673 | 42303082 | 35 | 0 |
| 7 | OutRun 2 (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.285 | 33.02 | 34339 | 268744098 | 686 | 0 |
| 8 | Obscure (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.563 | 32.72 | 34339 | 268744098 | 686 | 0 |
| 9 | Ninja Gaiden Black (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 29.956 | 33.38 | 34339 | 268744098 | 686 | 0 |
| 10 | Padrino, El (Spain).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.447 | 32.84 | 34339 | 268744098 | 686 | 0 |
| 11 | Grand Theft Auto - San Andreas (USA) (En,Es) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.78 | 29.6 | 34339 | 268744098 | 686 | 0 |
| 12 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.768 | 32.5 | 34339 | 268744098 | 686 | 0 |
| 13 | Phantom Dust (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.012 | 33.32 | 34339 | 268744098 | 686 | 0 |
| 14 | N.U.D.E.@ - Natural Ultimate Digital Experiment (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 32.144 | 31.11 | 34339 | 268744098 | 686 | 0 |
| 15 | Myst IV - Revelation (USA, Canada).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.4 | 29.94 | 34339 | 268744098 | 686 | 0 |
| 16 | Monster Garage (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.425 | 29.92 | 34339 | 268744098 | 686 | 0 |
| 17 | JSRF - Jet Set Radio Future (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.066 | 30.24 | 34339 | 268744098 | 686 | 0 |
| 18 | Halo 2 (USA, Europe) (En,Ja,Fr,De,Es,It,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.066 | 30.24 | 34339 | 268744098 | 686 | 0 |
| 19 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 34.982 | 28.59 | 34339 | 268744098 | 686 | 0 |
| 20 | Quantum Redshift (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.355 | 32.94 | 34339 | 268744098 | 686 | 0 |
| 21 | OutRun 2 (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.347 | 32.95 | 34339 | 268744098 | 686 | 0 |
| 22 | Gauntlet - Seven Sorrows [PAL][En,Fr,De,Es,It][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.096 | 30.22 | 34339 | 268744098 | 686 | 0 |
| 23 | Elder Scrolls III, The - Morrowind - Game of the Year Edition (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.834 | 29.56 | 34339 | 268744098 | 686 | 0 |
| 24 | Simpsons, The - Hit & Run (USA, Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.71 | 32.56 | 34339 | 268744098 | 686 | 0 |
| 25 | 007 - Agent Under Fire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.661 | 31.58 | 34339 | 268744098 | 686 | 0 |
| 26 | 007 - Everything or Nothing [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 32.08 | 31.17 | 34339 | 268744098 | 686 | 0 |
| 27 | 007 - From Russia with Love [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.581 | 31.67 | 34339 | 268744098 | 686 | 0 |
| 28 | 007 - Nightfire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.125 | 32.13 | 34339 | 268744098 | 686 | 0 |
| 29 | 50 Cent - Bulletproof (France).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.17 | 32.08 | 34339 | 268744098 | 686 | 0 |
| 30 | AFL Live Premiership Edition (Australia).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.806 | 31.44 | 34339 | 268744098 | 686 | 0 |
| 31 | Amped 2 (Europe, Australia) (En,Fr,De).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.009 | 32.25 | 34339 | 268744098 | 686 | 0 |
| 32 | Gauntlet - Seven Sorrows [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 32.07 | 31.18 | 34339 | 268744098 | 686 | 0 |
| 33 | Bad Boys II (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.647 | 31.6 | 34339 | 268744098 | 686 | 0 |
| 34 | Breakdown (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.652 | 31.59 | 34339 | 268744098 | 686 | 0 |
| 35 | Breakdown (USA) (Demo).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.858 | 31.39 | 34339 | 268744098 | 686 | 0 |
| 36 | Club Football - Chelsea FC (UK).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.239 | 32.01 | 34339 | 268744098 | 686 | 0 |
| 37 | Conker - Live & Reloaded (USA) (En,Ja,Fr,De,Es,It).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.534 | 31.71 | 34339 | 268744098 | 686 | 0 |
| 38 | Crimson Skies - High Road to Revenge (Japan) (En,Ja,Fr,De,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.778 | 31.47 | 34339 | 268744098 | 686 | 0 |
| 39 | Dead or Alive Xtreme Beach Volleyball (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.172 | 32.08 | 34339 | 268744098 | 686 | 0 |
| 40 | Destroy All Humans! (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 33.201 | 30.12 | 34339 | 268744098 | 686 | 0 |
| 41 | Battle Engine Aquila (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 31.774 | 31.47 | 34339 | 268744098 | 686 | 0 |
| 42 | Star Wars - Knights of the Old Republic (USA) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 30.182 | 33.13 | 34339 | 268744098 | 686 | 0 |
| 43 | Arx Fatalis (USA, Europe) (Beta) (2003-07-11).iso | compat | dashboard | 30 | 0x0013EC5C | xbe | 1000000000 | 20.31 | 49.24 | 2672 | 41995882 | 35 | 0 |
| 44 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | oracle | dashboard | 30 | 0x000AC3AC | xbe | 1000000000 | 11.154 | 89.65 | 2672 | 41995882 | 35 | 0 |

## Reading the table

- **own-render** rows are the closest to playable; probe them first (TM1).
- **dashboard** rows still present the shared dashboard display; their EIP says whether the title code or the kernel idle is executing.
- **fault** rows name the vector, EIP and CR2; a cluster of identical faults is one shared blocker.
- **timeout** rows are hangs or very slow titles; re-run them alone with a longer timeout before calling them blockers.

Full logs and per-title JSON are in `wt-data/title-matrix/`.
