# C2 trace emitter — design note (2026-10-19, for the next session)

Status: **specified, not implemented.** Charter Phase 2.5 (`jit-track-charter.md` §4).
Plan entry: `development-direction.md` §1 item 1; decisions §199/§199a.

## 1. Why (measured, load-independent)

Fast-mode 1B triangle (gl+replace+spin, quiet host, §198a): **10.68 s**.
`Cpu.Execute` is the whole wall (§199b: device advance 51 ms, CLI ~50 ms).

| Component | Cost | Source |
|---|---:|---|
| Block invocations | 125.0M × **~18 ns fixed** ≈ 2.3 s | `invocations` in `Jit stats` |
| Members served | 543.9M × **~7 ns** ≈ 3.8 s | `served` |
| Non-JIT residue | 522K calls / 5.4M insns ≈ 1.9 s (D3b cut calls 59%) | `nonJitMs` |
| GL replace path | ~0.5 s | `timed[drawMs/readbackMs]` |

The 18 ns fixed cost = per-block context load/store (8 GPRs + EIP/EFLAGS/TSC
through properties) + outer dispatch (memo probe, dictionary lookup, budget
checks, IRQ poll). The DynamicMethod block JIT cannot amortize it: blocks
average ~4.2 members on this workload.

Target: CPU-side ≥2× (triangle runner ≤3 s, menu CPU ≤11 s; charter Phase
2.5). Do **not** claim walls on a loaded host — the deterministic gates below
are the currency until a quiet window.

## 2. Architecture

**Trace = a straight-line run of blocks with a single entry.** Build:

1. Start at a head block (existing `JitBlockCache`).
2. Follow the *statically known* successor while possible:
   - `JmpRel` terminator → target head (unconditional jump);
   - fall-through after a block that ends at a *conditional* branch is NOT
     followed (two-way split — stop the trace there);
   - `CallRel` → stop (return is dynamic; the existing chain handles it);
   - `RetNear`/`JmpReg`/`JmpMem`/`CallSpec` → stop.
3. Cap: 8 blocks or 96 members per trace (start; tune by measurement).
4. Reject a trace whose blocks are not all "simple" (a block containing
   `CallInterpreter`, `Invlpg`, or an unplanned member ends the trace there).

**Trace runner contract** (one `DynamicMethod` per trace):

- Signature identical to `JitRun` (`XboxCpu cpu, DecodeCacheEntry[] entries,
  int budget`) so `RunJitBlock` can call either. `entries` = the
  concatenated member entries; the trace carries a parallel
  `(blockIndex, memberIndex)` map for the boundary logic.
- Load state **once** (lazy masks, §3), execute all members with the same
  per-member EIP/TSC semantics as today, store state **once** at every exit.
- **Boundary contract (must not change, §193):**
  - IRQ poll at every block boundary: emit the same `InterruptSource != null
    && IF` check; on a pending vector, store state, set `_jitPending`, return
    the members served so far (the caller delivers the IRQ exactly as today).
  - Budget: check `served < budget` per member group; exit with the count.
  - Fault: identical `PageFaultSignal` handling; the fault path must store
    the *cumulative write mask up to that member* (see §3), the EIP of the
    faulting member, and `_jitProgress` exactly as the block emitter does.
  - Self-modifying code / generation change: exit at the next block boundary
    if `blocks.Generation` differs (one compare per boundary; cheap).
- Side exits: any trace-miss (unexpected EIP after a member, an unsupported
  member, a stale entry) exits with the state stored; the outer loop falls
  back to the block JIT/interpreter unchanged.

**Cache:** `TraceCache` keyed by head linear + generation, holding the built
runner + member map + entry array. Invalidate with the existing block cache
policy (generation bumps, page drops → conservative all-block eviction; reuse
`DecodedBlockCache.Invalidate` semantics). Start with no memo/tier: build on
the first eligible head visit above the existing `JitBuildThreshold`.

## 3. Lazy state sync (the first contained step — do this before traces)

Per-block, compute two masks over the 8 GPRs (`eax,ecx,edx,ebx,esp,ebp,esi,edi`):

- `readMask` — every GPR a member consumes;
- `writeMask` — every GPR a member produces.

Load only `readMask`, store only `writeMask`. **Fault/interpreter-call sync
paths store the *cumulative* write mask at that member** (the emitter already
knows the member index) — storing a register that was neither loaded nor
written would write a stale local, so the cumulative set is required.

Derivation per `JitKind` (from `JitMemberPlan` fields; `Base`/`Index` only
when `HasBase`/`HasIndex`, `Seg` ignored — segment registers are not in the
GPR array):

| Kind | reads | writes |
|---|---|---|
| `MovRR` | `SecondRegIndex` | `RegIndex` |
| `MovRI`, `MovLoad` | Base/Index | `RegIndex` |
| `MovStore`, `MovStoreImm` | `RegIndex` (store) / Base/Index | — |
| `AluRR` | `RegIndex`, `SecondRegIndex` | `RegIndex` |
| `AluRI` | `RegIndex` | `RegIndex` |
| `AluRM` | `RegIndex`, Base/Index | `RegIndex` |
| `AluMR`, `AluMI` | `RegIndex` (MR) / Base/Index | — (memory dest) |
| `TestRR` | `RegIndex`, `SecondRegIndex` | — |
| `TestRI` | `RegIndex` | — |
| `TestMI` | Base/Index | — |
| `IncR`, `DecR` | `RegIndex` | `RegIndex` |
| `IncM`, `DecM` | Base/Index | — |
| `Lea` | Base/Index | `RegIndex` |
| `MovExtend` | `SecondRegIndex` (reg) / Base/Index (mem) | `RegIndex` |
| `ShiftRI` | `RegIndex` | `RegIndex` |
| `PushR` | `RegIndex`, `ESP` | `ESP` |
| `PushI` | `ESP` | `ESP` |
| `PopR` | `ESP` | `RegIndex`, `ESP` |
| `CallRel` | `ESP` | `ESP` |
| `CallReg`, `CallMem` | `RegIndex`/Base/Index, `ESP` | `ESP` |
| `RetNear` | `ESP` | `ESP` |
| `JmpReg`, `JmpMem` | `RegIndex`/Base/Index | — |
| `JmpRel`, `JccRel`, `Nop`, `Cli`, `Sti`, `Cld`, `Std` | — | — |
| `CallSpec`, `CallInterpreter`, `Invlpg` | **all 8** (conservative) | **all 8** |

Acceptance for the lazy-sync step: the four-engine 200M differential
(`29A00539/B4482225`) and both 1B fast anchors (`660CA11B/F5D61C94` menu,
`4F1FCBC0/7DAB63EB` triangle) stay exact; suite green; `invocations` and
`served` unchanged (the change is state-movement only). Expect ~1–1.5 s on
the triangle (measured by `Host timing executeMs` on a quiet host).

## 4. Inline memory fast paths (step 3, after traces)

The member cost (~7 ns) is helper calls for memory ops. `--emit-inline`
already proved the shape (translate-cache probe + RAM-direct read/write, with
an exact-twin fallback; hitRate 0.92/0.98, §178) but only ~17% of members were
fast-eligible. In the trace emitter, bake the fast path **inline for every
memory member** (loads and stores), with the fallback as a cold branch to the
existing twin. This is where the member-cost halving comes from; do it after
the trace machinery is green so the two effects are measured separately.

## 5. Validation order (each step)

1. Build + format + full suite (`dotnet test XGrid.sln`).
2. Unit differentials (existing JitTests + new trace tests: boundary polls,
   budget exits, fault precision, side exits, invalidation).
3. Four-engine 200M triangle: accurate interpreter = `--spec-exec` =
   `--block-exec` = `--jit --jit-exact` (currently `29A00539/B4482225`).
4. 1B fast anchors: menu `660CA11B/F5D61C94`, triangle `4F1FCBC0/7DAB63EB`.
5. ktest smoke 7/7.
6. Walls: **quiet host only** (ask the operator to stop Fortnite), median-of-3,
   plus `Host timing executeMs` and `Jit stats` (invocations, served,
   nonJitMs) for the attribution.

## 6. Risks / kill criteria

- **Boundary drift** (IRQ/fault/TSC points) is the main risk: every exit must
  store the state and return the exact served count; pin with the unit
  differentials and the four-engine SHA.
- **Trace formation cost** (dictionary churn) can eat the win: cap traces,
  reuse the block cache's heads, measure `blocks`/`builds` counters.
- **Kill:** <1.4× CPU-side after 3 structural iterations → stop and go to C3
  (raw emitter) with the lazy-sync + inline-memory work kept.
- Never touch the accurate interpreter; fast mode is never gate currency.
