# P1S2 - the CPU engine port decision (2026-11-22)

**Operator decision:** port the existing xemu/QEMU CPU engine to C# (**option b**,
the full program), phased as P1S2 (softmmu/TLB), P1S3 (TCG IR + i386
translator), P1S4 (host codegen), P1S5 (parity/switchover), then P1S6 (the P1
gate). The raw JIT stays the fallback until the port's differential is exact and
its member cost is below the current runner.

## The arithmetic

| item | measured | share of 30B wall |
|---|---:|---:|
| wall | 413.254 s | 100% |
| raw runner | 221.061 s | 53.5% |
| non-JIT runs | 83.124 s | 20.1% |
| outer/loop/chain | 310.440 s | 75.1% (inclusive) |
| GL draws | 1.724 s | 0.42% |
| devices (`advance`) | 0.481 s | 0.12% |

To hit 0.5x the 30B run must finish in **~83 s**: a **5.0x** cut. Halving every
CPU stage (runner, non-JIT, outer) gives ~207 s = **0.20x**; halving only `b4`
(the 32+ member bucket, 35.7% of the wall) gives ~339 s = **0.12x**. No
allocation, shape or bookkeeping fix can reach 0.5x.

## The profile (100M steps from the 26B wait loop, `XGRID_RAW_PROFILE=1`)

Raw helper shapes (2.86M): **Fnstsw 457,831; Fild 367,260; Fistp 366,997;
Movntps 330,596; Movups 330,488; Imul 273,847; Movaps 165,476; Movhlps 165,274;
Movlhps 165,269; Mul 131,978; Call 54,451; Movs 20,060**.
Raw kinds (4.14M): MovLoad 1.39M; AluRI 897k; MovStore 502k; PushR 306k; AluRM
259k; ShiftRI 154k; PopR 103k; RetNear 94k; AluRR 60k; CallRel 56k.

This is a classic x87/SSE game wait loop (FPREM/FNSTSW/FILD/FISTP + packed
moves), which the raw emitter already serves with native code. There is no
single dominant shape to bake: the top shape is 16% of helper calls and the
helpers are not the 5x.

## Options

- **(a) Lower the P1 floor to a measured interactive value.** With the b4/outer
  and x87 fixes, ~0.20x is the plausible ceiling (2x), still 5x slower than
  real time. That is a slow-motion emulator, not an action-game experience, but
  it is an honest compatibility build and it can reach the P1 gate if the
  operator redefines "playable" accordingly.
- **(b) Fund a CPU engine program (XFP11 or a new engine).** The current raw JIT
  is already native; the gap is the per-member memory probe and block shape, not
  the instruction encoding. A C# TCG port is **not guaranteed** to beat the raw
  JIT - xemu's C TCG is ~1.2 ns/step, and a managed port could be 2-4x slower,
  i.e. 0.25-0.5x at best. Price it as a 20-40 session research program with a
  hard "not slower" gate before it replaces the raw engine.
- **(c) Change the product target.** Keep the LLE emulator for compatibility and
  accept that it is not real-time on this hardware; pick a turn-based/slow title
  for the "playable" milestone, or reserve real-time for a future native/host
  acceleration path (outside the current all-C# premise).

## Decision (2026-11-22)

The operator chose **(b) the full CPU engine port**, phased as:

1. **P1S2** - softmmu/TLB and memory dispatch (the memory-probe 2x gate).
2. **P1S3** - TCG IR and the i386 translator (cpu-oracle exact).
3. **P1S4** - host code generation (not-slower gate).
4. **P1S5** - parity and switchover (0.5x measured).
5. **P1S6** - the P1 gate.

The raw JIT stays the fallback until the port's differential is exact and its
member cost is below the current runner. The CPU port is now the critical path;
it is priced at 22-34 sessions across the four phases, with hard stop points at
each phase.

## Recommendation (superseded by the decision above)

Do not start (b) as an assumed win. The measurement says the raw JIT is not
5x off because of a missing instruction; it is 5x off because of the memory
model and block granularity. The fastest path to a *usable* build is (a) with a
recorded speed floor, while (b) is a research decision with its own price.

The P1S1 live audio is independent and is **done** (the EP FIFO PCM plays,
SHA-identical to the TM8 capture). The remaining P1 gate is the speed floor.
