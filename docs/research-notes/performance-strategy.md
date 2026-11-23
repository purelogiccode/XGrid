# Performance-first strategy (operator decision, 2026-11-22)

Status: **binding**. The operator set *maximum performance* as the primary
objective and delegated the plan/strategy change needed to reach it. This
supersedes the earlier "every module except the CPU stays managed C#" rule
wherever that rule costs speed; the GPL-2.0 license, the porting policy and the
provenance rules are unchanged.

## Target architecture

**One process. Native hot path. C# shell.**

```
xgrid (C#)                                  libxgridcore (C, in-process)
  UI / settings / library                     QEMU TCG i386 + RAM + TB cache
  input mapping / audio sink        <──►      PIC / PIT / RTC / SMC / SMBus
  snapshots orchestration                     IDE / ATAPI + block
  corpus + goldens + tests                    NV2A FIFO / PGRAPH / renderer
  diagnostics                                 APU / ACI / DSP
```

- The emulator core is native and **in the same process** as the shell: no
  shared-memory protocol, no per-access ring, no spin-wait.
- C# keeps what is not on the hot path: UI, configuration, input mapping, the
  audio sink, snapshot orchestration, the test/corpus tooling and the
  diagnostics that make the project verifiable.
- The managed devices, GPU backend, APU/DSP model and 2258-test suite stay as
  the **reference and oracle** for the native port, not as the product path.

## Why this and not the current seam

Measured today, on this host:

| Path | Cost |
|---|---|
| RAM access (direct, shared section) | 25 ns |
| MMIO access through QEMU's I/O path | 1.85 µs |
| Live cross-process MMIO read | ~3.9 µs |
| C# answer inside a live read | 27 ns |
| NV2A method writes on the 007 leg | 18.6M per 1B guest instructions |
| C# NV2A cost per method | **unmeasured** — the open question |

At 18.6M methods per 1B steps, even 200 ns per method in C# costs 3.7 s per 1B
steps against 1.36 s of guest time: the device model, not the CPU, becomes the
ceiling. A boundary per access cannot be optimized away below its per-call
cost; the code has to move.

## The ladder (measured ceiling, work, consequence)

| Level | Change | Expected rate | Work | Consequence |
|---|---|---|---|---|
| **L0** | today: native CPU, C# devices over a ring | 0.22x boot | — | keeps the C# emulator identity |
| **L1** | batched ring (one entry per FIFO burst), optimized C# method dispatch, shadowed reads | 0.4–0.6x (unproven) | 2–4 weeks | still a boundary; ceiling is the per-method cost |
| **L2** | **native hot devices** (NV2A FIFO/PGRAPH, IDE, APU/DSP), C# shell | ~0.7–1x | 2–3 months | XGrid becomes "xemu's core with a C# shell" |
| **L3** | link xemu wholesale behind the C# shell | ~1x (xemu parity) | days–weeks | fastest, least work, project identity changes most |

Recommendation: **L2**, with **L3** as the explicit fallback if wall-clock time
matters more than the size of the C# footprint. L1's optimizations are a subset
of L2 and are not wasted, but L1 alone cannot pass 0.5x if the per-method cost
is what we fear.

## Execution order

**P0 — price the two ceilings (this week, before any porting).**

1. **Root-cause the single-process crash.** The .NET-hosted DLL failed with
   `0xC0000409` on the MMIO path and was attributed to Control Flow Guard; that
   attribution was never proven. A single process removes the entire seam, so
   this is the highest-leverage experiment in the project. If it is a stack
   cookie/alignment/longjmp interaction, the seam disappears.
2. **Measure ns per NV2A method** in the managed device. This decides L1 vs L2.
3. **Reprice icount/MMIO**: `ICOUNT_PRECISE` forces `CF_MEMI_ONLY` +
   `cpu_io_recompile` on every I/O access; xemu does not run in that mode.

**P1 — single process.** If (1) succeeds, `libxgridcore` becomes an in-process
DLL with direct calls; the ring and shared section are deleted.

**P2 — NV2A native.** FIFO, method decode, PGRAPH state and draw submission
move to C. The renderer can stay C# behind one callback per draw batch, or move
to the existing GL backend in C; measure both.

**P3 — IDE/ATAPI and APU/DSP native.** DMA and the DSP interpreter are the next
two per-sample/per-sector hot paths.

**P4 — shell polish**: snapshots, input, audio sink, product defaults.

## What we keep, whatever happens

- The GPL-2.0 license, `THIRD_PARTY_NOTICES.md`, `native/qemu-patches/` and the
  written source offer. xemu/QEMU code is GPL-2.0 and may be ported.
- The managed goldens (`--cpu managed`), the ktest log and the 2258-test suite
  as the behavioural oracle for the native port.
- The C# frontend, settings, input and diagnostics.

## Kill criteria

- If P0.1 (single process) cannot be made to work **and** P0.2 shows the C#
  device path above ~100 ns/method, then L1 cannot pass 0.5x on 007 and the
  project either goes L2/L3 or re-scopes the speed gate. Decide with the
  numbers, not with fatigue.
- If the icount/MMIO reprice leaves the core below ~300 Msteps/s with no
  identified lever, the 0.5x gate is out of reach for this host class.
