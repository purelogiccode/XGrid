# Real-game probes (local media, not gates)

Status: **2026-10-23.**  The operator supplied two Xbox ISOs at
`D:\Samples\XBOXReady` for compatibility testing.  They are **local media**:
never committed, never gate currency, and never used for CI.  The standing gates
remain the 1B accurate goldens and the ktest smoke.

## Media

| File | Size | Notes |
|---|---:|---|
| `D:\Samples\XBOXReady\007 - Agent Under Fire (USA).iso` | 1,719,795,712 B | 007 Nightfire-era Bond title; XBE executes. |
| `D:\Samples\XBOXReady\4x4 Evo 2 (USA).iso` | 1,361,051,648 B | 4x4 Evo 2; still in the kernel idle loop at 5B. |

The boot HDD is the gate HDD (`wt-data\chain-199l\hdd.qcow2`, EvoX dashboard);
the firmware is the `data\xemu` set (`Complex_4627.bin`, `mcpx_1.0.bin`,
`eeprom.bin`).

## Command

```powershell
pwsh -File scripts/boot-to-first-frame.ps1 `
  -Sample 007 `
  -IsoPath "D:\Samples\XBOXReady\007 - Agent Under Fire (USA).iso" `
  -FirmwareDir "data\xemu" `
  -Hdd "wt-data\chain-199l\hdd.qcow2" `
  -Steps 5000000000
```

The harness uses HDD + DVD (the `-IsoPath` branch) and the fast engine.  It
writes `wt-data/btff/<sample>-sw.json`, a BMP of the first/last/scanout/display
frames, and the full CLI log.

## Results

| Probe | Steps | Wall | Flips | Draws | Pixels | EIP at end | Verdict |
|---|---:|---:|---:|---:|---:|---|---|
| 007 | 1B | 19.50 s | 55 | 2,672 | 41,995,882 | — | PASS |
| 007 | 5B | 81.25 s | 201 | 2,672 | 41,995,882 | **0x0001FC66** | PASS - the XBE is executing |
| 4x4 Evo 2 | 1B | 18.82 s | 43 | 2,672 | 41,995,882 | — | PASS |
| 4x4 Evo 2 | 5B | 185.47 s | 219 | 2,672 | 41,995,882 | **0x8001A68C** | PASS (no fault) - kernel idle |

### 007

- At 5B the EIP is `0x0001FC66`, inside the loaded XBE (the 007 probe's XBE base
  is `0x10000`; the historical 007 entry is `0x22A17`).  The game's own code is
  running, not the boot animation loop.
- `firstFrameStep=46,000,000`, `firstFrameSha=6F780A33...` (the boot animation);
  `lastFrameSha=F9F17001...` at 5B.  The first frame is the boot animation, so
  "first frame" is not yet a gameplay frame.
- `ram=C61364E3...`, `cpu=AFD09BF5...`, `draws=2,672`, `frames=201`,
  `stalls=201`, no fault.
- The 007 docs' PlayMPC/audio gate was at ~56B steps; 5B is early.  A longer run
  is needed before claiming gameplay.

### 4x4 Evo 2

- At 5B the EIP is `0x8001A68C`, the kernel idle loop.  The guest is not
  executing game code; it is waiting (most likely on a disc/loading/input
  event).  `draws=2,672` is the boot animation, not the game.
- No fault; the run completes 5B steps and presents 219 flips.
- This is the first real-game compatibility gap to triage: identify the wait
  (EIP 0x8001A68C is the kernel idle thread; the caller/thread state is the
  next probe) and compare with xemu 0.8.139 running the same ISO.

## What to do next

1. Run xemu 0.8.139 with the same ISO (add the DVD to the xemu config or launch
   with a second drive) and capture the screen; use it as the oracle.
2. For 4x4, break at `0x8001A68C` and inspect the thread/object state; the
   guest is likely waiting on a DVD read or a pad edge.  The existing
   `--pad-script` can test the input hypothesis.
3. For 007, run to ~20-60B steps (snapshot at 5B first) and check whether the
   game reaches gameplay and audio.  The APU DSP port (P1) is the likely audio
   gate.
4. Add a real-game row to `docs/compatibility.md` only after a run reaches a
   gameplay frame or a documented wait; a boot-animation-only run is a probe,
   not a compatibility row.
