# P1S0 - the playable gap on 007 (measured 2026-11-22)

Task: **P1S0** (`ImplementationPlan.md` E1). Price the gap between the current
engine and the P1 playability bar on 007, in the product configuration, and name
the blockers. No emulator source change.

## Configuration

- Engine: Release build, .NET 11 / C# 15, **raw JIT** (`--jit --jit-raw`),
  **OpenGL replacement** (`--gpu gl --gpu-replace`, GL 4.6.0 NVIDIA 621.24),
  software rasterizer only as the fallback.
- Media: `H:\Tests\XBOXReady\007 - Agent Under Fire [NTSC-U][Redump].iso`,
  firmware `data/xemu/Complex_4627.bin` + `mcpx_1.0.bin` + `eeprom.bin`,
  HDD `wt-data/chain-199l/hdd.qcow2`.
- Bar: **>= 0.5x guest time** = <= 2.73 s per 1B steps (1B / 733M = 1.364
  guest-seconds). Stretch: 1.0x = 1.36 s per 1B.
- The three runs are in `wt-data/p1s0/`.

## Measured rows

| run | steps | wall | msps | guest speed | notes |
|---|---:|---:|---:|---:|---|
| boot -> 26B, menu script + A at 20B | 26,000,000,000 | **353.796 s** | 73.49 | **0.100x** | Harrier intro (2,672 draws / 994 frames); GL served 27,107 triangles |
| boot -> 30B, **A held 20B-20.1B** | 30,000,000,000 | **413.254 s** | 72.59 | **0.100x** | **gameplay reached** (79,581 draws / 2,729 frames); GL 28,934 triangles, 1.72 s |
| `007-100b.xgs` + 1B | 1,000,000,000 | 13.780 s | 72.57 | 0.099x | **static**: draws 134,004 -> 134,004, pixels unchanged; GL 0 draws |
| `007-26b.xgs` + 1B | 1,000,000,000 | 14.116 s | 70.84 | 0.097x | draws 65,503 -> 79,581 but pixels unchanged (zero-pixel draws); GL 329 triangles |

**The gap is 5x to the 0.5x floor** (0.100x) and 10x to real time.

## Stage split (30B gameplay, 413.254 s)

| stage | time | share | note |
|---|---:|---:|---|
| `execute` (CPU + devices) | 412.745 s | 99.9% | `advance` (devices) 0.481 s |
| raw runner | 221.061 s | 53.5% | 29.51B served members |
| non-JIT runs | 83.124 s | 20.1% | 361.4M calls, 491.7M instructions |
| outer/loop/chain | 310.440 s | 75.1% | inclusive of the runner/non-JIT |
| **GL draws** | **1.724 s** | **0.42%** | 28,934 triangles, 3,840 batches, 8 programs |
| clears | 0.033 s | ~0% | 3,730 clears, 2,729 flips |

Runner buckets on the gameplay leg: b0 8.1 s / 120.9M, b1 21.4 s / 139.1M,
b2 35.2 s / 140.5M, b3 8.7 s / 25.7M, **b4 147.6 s / 32.1M** (32+ members per
invocation). **b4 is 35.7% of the whole wall** at ~4.6 us per invocation.

## Findings

1. **The GPU is not the wall.** The ported/replacement GL path is 2.5% of the
   wall and serves every visible triangle on the 26B boot; the software pixels
   counter is 0 because replacement mode owns the pixels. Porting the xemu GL
   renderer (XFP5) cannot move P1 by itself.
2. **The CPU engine is the wall.** 94.4% of `execute` is the JIT machinery
   (runner + non-JIT + outer); the raw runner alone is 47.3%. At 73.5 Msteps/s
   the engine is 5x short of 0.5x and 10x short of real time.
3. **`b4` (32+-member block invocations) is the largest single item**: 109.4 s
   = 30.9% of the wall for 21.5M invocations. It needs a profile of the
   long-block bodies (device/poll members, chain exits), not another GPU slice.
4. **Snapshot resume is not a playability measurement.** All three anchors
   resume into zero-draw wait loops (100B: 0 draws/1B; 26B: zero pixels/1B).
   Playability must be measured on a continuous boot run. The save/load path
   needs a state/streaming gap fixed before snapshots are used as a playability
   tool.
5. **Audio is still offline.** The run had no `--apu-dsp`; TM8's PCM is a WAV.
   P1S1 (live sink + AC'97) is independent of the speed wall.
6. **The gameplay leg is the same shape** (30B, A held 20B-20.1B): 79,581 draws
   / 2,729 frames, 413.254 s, 72.59 Msteps/s, **0.100x**, GL 0.42%. The engine,
   not the title stage, sets the wall.

## What this means for P1S2

The P1S2 plan assumed "GL coverage + CPU hot shapes" could reach 0.5x. The
measurement prices that as **not reachable by small fixes**: the runner would
need ~3x and the outer overhead another ~2x, while the GPU is already 0.4%.

The honest options, for the operator decision:

- **(a) Lower the P1 speed floor** to a measured, interactive value (for
  example 0.20-0.25x = 2.5-3.5 s per 1B) and target the b4/outer overhead; this
  is a smaller, measurable program.
- **(b) Fund XFP11** (softmmu/TCG/CPU core) and accept a 20-40 session port,
  with the "not slower" gate; the CPU is the only stage that can pay 5x.
- **(c) Both**: P1S1 audio + the b4/outer fixes for an interactive 0.2x, and
  XFP11 for the 0.5x+ target.

Do not start XFP11 without this decision: the measurement says it is the only
5x lever, and the plan's "playable fast" objective now hinges on it.
