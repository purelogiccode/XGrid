# Retail title matrix (M-T)

Generated 2026-10-24T22:08:13 by `scripts/title-matrix.ps1` - steps=1000000000, gpu=sw, fast=True, padScript=down@950000000+hold,none@962000000,a@970000000+hold,none@976000000.

**44 titles: 2 own-render, 42 dashboard, 0 fault, 0 timeout.**

`dashboard` means the row matches a draw/pixel/frame tuple shared by two or more titles (the dashboard boot animation or menu); `own-render` means a unique tuple (the title took the display). `early` means fewer than 1B steps ran. `fault`/`timeout` are rows, not harness failures.

| # | title | source | stage | score | EIP | EIP zone | steps | wall s | msps | draws | pixels | frames | fault |
|---:|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | 007 - Agent Under Fire [NTSC-U][Redump].iso | oracle | own-render | 130 | 0x000138D3 | xbe | 1000000000 | 12.271 | 81.5 | 2672 | 41995882 | 55 | 0 |
| 2 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | oracle | own-render | 130 | 0x0039CE8B | xbe | 1000000000 | 10.153 | 98.49 | 2675 | 41995882 | 42 | 0 |
| 3 | Phantom Dust (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.254 | 39.6 | 33638 | 253998498 | 686 | 0 |
| 4 | Gauntlet - Seven Sorrows [PAL][En,Fr,De,Es,It][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.697 | 38.92 | 33638 | 253998498 | 686 | 0 |
| 5 | Grand Theft Auto - San Andreas (USA) (En,Es) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.247 | 39.61 | 33638 | 253998498 | 686 | 0 |
| 6 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.183 | 39.71 | 33638 | 253998498 | 686 | 0 |
| 7 | Halo 2 (USA, Europe) (En,Ja,Fr,De,Es,It,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.376 | 39.41 | 33638 | 253998498 | 686 | 0 |
| 8 | JSRF - Jet Set Radio Future (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.405 | 39.36 | 33638 | 253998498 | 686 | 0 |
| 9 | Monster Garage (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.219 | 39.65 | 33638 | 253998498 | 686 | 0 |
| 10 | Myst IV - Revelation (USA, Canada).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.11 | 39.83 | 33638 | 253998498 | 686 | 0 |
| 11 | N.U.D.E.@ - Natural Ultimate Digital Experiment (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.03 | 39.95 | 33638 | 253998498 | 686 | 0 |
| 12 | Gauntlet - Seven Sorrows [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.198 | 39.69 | 33638 | 253998498 | 686 | 0 |
| 13 | Nickelodeon Tak - The Great Juju Challenge (Europe).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.335 | 39.47 | 33638 | 253998498 | 686 | 0 |
| 14 | Ninja Gaiden Black (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.339 | 39.47 | 33638 | 253998498 | 686 | 0 |
| 15 | Obscure (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.264 | 39.58 | 33638 | 253998498 | 686 | 0 |
| 16 | OutRun 2 (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.264 | 39.58 | 33638 | 253998498 | 686 | 0 |
| 17 | OutRun 2 (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.463 | 39.27 | 33638 | 253998498 | 686 | 0 |
| 18 | Padrino, El (Spain).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.882 | 38.64 | 33638 | 253998498 | 686 | 0 |
| 19 | Quantum Redshift (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.284 | 39.55 | 33638 | 253998498 | 686 | 0 |
| 20 | Simpsons, The - Hit & Run (USA, Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.437 | 39.31 | 33638 | 253998498 | 686 | 0 |
| 21 | Elder Scrolls III, The - Morrowind - Game of the Year Edition (USA).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.168 | 39.73 | 33638 | 253998498 | 686 | 0 |
| 22 | Star Wars - Knights of the Old Republic (USA) (Rev 1).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.783 | 38.79 | 33638 | 253998498 | 686 | 0 |
| 23 | AFL Live Premiership Edition (Australia).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.456 | 39.28 | 33638 | 253998498 | 686 | 0 |
| 24 | 007 - From Russia with Love [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.2 | 39.68 | 33638 | 253998498 | 686 | 0 |
| 25 | Amped 2 (Europe, Australia) (En,Fr,De).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.361 | 39.43 | 33638 | 253998498 | 686 | 0 |
| 26 | Destroy All Humans! (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.662 | 38.97 | 33638 | 253998498 | 686 | 0 |
| 27 | 007 - Everything or Nothing [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 27.336 | 36.58 | 33638 | 253998498 | 686 | 0 |
| 28 | Bad Boys II (Germany).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.534 | 39.16 | 33638 | 253998498 | 686 | 0 |
| 29 | 50 Cent - Bulletproof (France).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.563 | 39.12 | 33638 | 253998498 | 686 | 0 |
| 30 | Battle Engine Aquila (Korea).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.44 | 39.31 | 33638 | 253998498 | 686 | 0 |
| 31 | Breakdown (USA) (Demo).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.719 | 38.88 | 33638 | 253998498 | 686 | 0 |
| 32 | Club Football - Chelsea FC (UK).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.594 | 39.07 | 33638 | 253998498 | 686 | 0 |
| 33 | 007 - Agent Under Fire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.915 | 38.59 | 33638 | 253998498 | 686 | 0 |
| 34 | Conker - Live & Reloaded (USA) (En,Ja,Fr,De,Es,It).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.605 | 39.05 | 33638 | 253998498 | 686 | 0 |
| 35 | Crimson Skies - High Road to Revenge (Japan) (En,Ja,Fr,De,Zh,Ko).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.04 | 39.94 | 33638 | 253998498 | 686 | 0 |
| 36 | Dead or Alive Xtreme Beach Volleyball (USA) (En,Ja).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.38 | 39.4 | 33638 | 253998498 | 686 | 0 |
| 37 | Breakdown (Japan).iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 26.002 | 38.46 | 33638 | 253998498 | 686 | 0 |
| 38 | 007 - Nightfire [NTSC-U][Redump].iso | compat | dashboard | 50 | 0x0003A057 | xbe | 1000000000 | 25.671 | 38.95 | 33638 | 253998498 | 686 | 0 |
| 39 | Arx Fatalis (USA, Europe) (Beta) (2003-07-11).iso | compat | dashboard | 30 | 0x0013EC5C | xbe | 1000000000 | 18.432 | 54.25 | 2672 | 41995882 | 35 | 0 |
| 40 | Ninja Gaiden Black (USA) (En,Ja).iso | oracle | dashboard | 30 | 0x0037DC15 | xbe | 1000000000 | 9.514 | 105.11 | 2672 | 41995882 | 35 | 0 |
| 41 | Halo - Combat Evolved (USA) (v1.09) (Redump).iso | oracle | dashboard | 30 | 0x000AC3AC | xbe | 1000000000 | 9.67 | 103.41 | 2672 | 41995882 | 35 | 0 |
| 42 | Bad Boys II (Germany).iso | oracle | dashboard | 30 | 0x00281D05 | xbe | 1000000000 | 9.153 | 109.26 | 2672 | 41995882 | 34 | 0 |
| 43 | 50 Cent - Bulletproof (France).iso | oracle | dashboard | 30 | 0x0026FB54 | xbe | 1000000000 | 9.942 | 100.58 | 2672 | 41995882 | 34 | 0 |
| 44 | Destroy All Humans! (Germany).iso | oracle | dashboard | 10 | 0x8001434F | kernel | 1000000000 | 11.074 | 90.3 | 2672 | 41995882 | 34 | 0 |

## Reading the table

- **own-render** rows are the closest to playable; probe them first (TM1).
- **dashboard** rows still present the shared dashboard display; their EIP says whether the title code or the kernel idle is executing.
- **fault** rows name the vector, EIP and CR2; a cluster of identical faults is one shared blocker.
- **timeout** rows are hangs or very slow titles; re-run them alone with a longer timeout before calling them blockers.

Full logs and per-title JSON are in `wt-data/title-matrix/`.
