# Fast-mode execution engine (JIT track) — charter (2026-10-18, operator-approved)

> **STATUS 2026-10-18 NIGHT: PHASE 0 + PHASE 1 LANDED; PHASE-1 TARGETS
> EXCEEDED (see §§191–192 for the full evidence).** J1a–J1d: `boot --bench`;
> `--jit` whole-block engine (visit-threshold builds, inner block loop,
> CallSpec fallback members, Cli/Sti members); `--jit-exact` per-member poll
> — bit-exact. **1B exact-mode runs are bit-identical to the accurate
> control on both titles and reproduce the golden frames** (menu
> `4EF1FBF6…` 686/686; triangle `A1740DE0…` 68/34); the ktest smoke passes
> 7/7 and the 2B suite workload is bit-identical under exact mode. Fast
> mode (never gate currency) reaches **~77 Msteps/s at 1B** (menu 12.84 s /
> triangle 13.02 s medians; 97% served) — the 10²–10³ Msteps/s band, past
> the Phase-1 targets (triangle ≤22 s / menu ≤50 s) and already inside the
> Phase-2 integration targets (≤12 s / ≤30 s) on the menu. Remaining for
> Phase 2: block-to-block chaining in IL (the outer-loop bookkeeping is
> ~4 s/1B), threshold auto-tuning, medians on a quiescent host. Phase 3
> (raw emitter) entry condition: measure after Phase 2 — the DynamicMethod
> path is currently well inside the band, so Phase 3 is not indicated yet.
> Fast-mode SHAs differ from accurate by timing only (proven via exact
> mode); fast-mode runs are deterministic for a fixed binary+flags.

> **STATUS: AUTHORIZED 2026-10-18 by the operator.** Long-run goal: *at least
> xemu-class performance*. This file is the governing document for the track —
> dual-mode contract (§2), correctness methodology (§3), phases + kill criteria
> (§4), validation gates (§5), the approved constraint deltas (§6), and the
> invariants that must not change (§7). `docs/development-direction.md` and
> `AGENTS.md` summarize it; `ImplementationPlan.md` Phase 15 / Track C point
> here. Nothing in this charter weakens the accurate mode or the standing
> gates. `§N` refs = `docs/research-notes/xgrid-architecture-decisions.md`.

---

## 1. Goal, and why the interpreter track cannot reach it

**Goal:** reach the xemu-class execution band (QEMU TCG class) — end-to-end
**10²–10³ Msteps/s**, i.e. the 733 MHz Pentium III executed in the real-time
band — on top of the existing LLE core (real MCPX ROM → kernel → XBE).

**Current measured position (post-R7, median-of-3, §§186a/187):**

| Workload | 1B-step wall | End-to-end | CPU-side (derived*) |
|---|---:|---:|---:|
| menu (dashboard) | 70.8 s | ≈14 Msteps/s | ≈22 Msteps/s |
| triangle | 36.6 s | ≈27 Msteps/s | ≈37 Msteps/s |

\* raster `--gpu-time` seconds subtracted from the median wall — an estimate,
not a separate measurement. Gap to the real-time band: 1–2 orders of
magnitude (≈3–45× per title; `gpu-swapchain-brief.md` §5).

**Why the current track has asymptoted:**

- The decode-cache frontier is captured: default-on 2.56×/3.04× (§137) plus
  fetch-fast 1.30–1.35× (§131) are already in the baseline.
- Shape specialization cut fallbacks 101.8M → 34.1M but moved walls only
  1.07–1.19× (§§149–169; §160, §164, §166, §169) — §138: the 354-case switch
  is *not* the remaining cost; per-instruction execute bodies (76% data-path
  helpers on menu) and the fixed ~105 ns/step instrumented overhead are.
- Relay-style emission is **measured wall-neutral**: E1 −1.93×/−6.50×
  (§171), E2 parity (§§172–174), E3 quiet-host triangle 1.021× vs emit /
  1.137× vs spec, menu parity (§§175–179b; §178 hitRate 0.9161/0.9831), and
  the §20.6 microbenchmark (in `jit-codegen-scope.md`): no managed emitter
  beats a plain C# helper loop (14.188 vs 14.285 ns; construction
  124.6–319.3 µs + 6–30 KB per block).

Only a codegen tier that removes per-instruction helper dispatch and
context load/store can move the wall (≈3–45× per title). That tier is this
track.

**Decision record — licensing (why not "port TCG"):** the project **stays
non-GPL** and does **not** transliterate QEMU/xemu code. QEMU TCG is
entangled with QEMU (softmmu, TB cache, `CPUArchState`, signal-based
invalidation); a C# port is a GPL derivative work *and* ≈ equal effort to an
own emitter. Legitimate references (techniques/design only unless a license
permits more): QEMU TCG (GPL-2.0 — **design reference, never code**),
Ryujinx ARMeilleure (**MIT** — C# runtime machine-code JIT infrastructure),
Iced (**MIT** — encoding, Phase 3, approved not yet referenced), Intel SDM
(x86-64 encodings). Any code reuse gets its license notice + a `sources.md`
entry.

## 2. The dual-mode contract (the load-bearing change)

**Accurate mode (existing interpreter; today's default):** unchanged in every
way — boundary-preserving `Step()`, exact TSC/IRQ/step observability, golden
SHAs/counters, byte-identical flag contracts. Gate currency forever.

**Fast mode (`--jit`, opt-in):** a new execution engine under a *precisely
defined* weaker contract.

- **Preserved (must hold in fast mode):** guest memory semantics and program
  order; fault precision (exact faulting instruction + architectural state at
  the fault); MMIO/device side effects in instruction order (non-RAM access
  exits to the exact helper at the access point); IF semantics (blocks
  bounded/structured at IF changes); deterministic output for the same
  binary + input script (verifiable via a bench state hash).
- **Relaxed (fast mode only):** TSC per-instruction values; step-boundary
  observability and step-count reproducibility; IRQ delivery point (block
  granularity); intermediate-state and frame SHAs are *informational*, never
  gates.
- **`--jit-exact` (debug variant):** the same JIT code with per-instruction
  serialization → bit-exact architectural state vs accurate mode at every
  boundary. Slow by design; used for differential debugging (QEMU singlestep
  precedent).
- **One engine per run** — a run is accurate or fast; no per-region mixing.

## 3. Correctness methodology for fast mode

- **Sync-point differential** (the fast mode's correctness contract): run
  both engines over the same deterministic input and compare architectural
  state at every sync point — MMIO access, guest fault, IRQ delivery, device
  callback. Harness: `--jit-verify`; first divergence prints full
  register/memory context and exits nonzero.
- **Unit differentials per emitted shape**, in the existing test style
  (differential vs interpreter, fault-precision pins, observed-store cases).
- **Title acceptance per milestone (§4):** triangle frame SHA target
  bit-exact where timing-independent (static scene); menu acceptance is
  structural/classified (the animation is timing-driven and fast mode runs
  faster than guest pacing); ktest *outcome* (pass/fail) must match from
  Phase 2 on.
- **Shared-code rule:** any change to code both engines use (decoder, MMU,
  `PhysicalMemory`, devices) still passes the full accurate-mode golden
  ceremony. Fast mode never substitutes for accurate gates.

## 4. Phases and kill criteria

**Phase 0 — bench + verify harness (days; no product change).**
Deterministic bench workloads ≤10 s wall each (hot-loop streams from the
§138 heads; menu/triangle trace replays), reporting Msteps/s + state hash;
`--jit-verify` scaffold (accurate-vs-accurate self-check green first).
Optional calibration: capture xemu throughput on this host if a permitted
oracle run exists; otherwise the 10²–10³ Msteps/s band stands.
*Accept:* reproduces current rates within ±10%, deterministic, adds no
product behavior.

**Phase 1 — block JIT v1 (DynamicMethod; opt-in `--jit`; weeks).**
Differ in kind from E1–E3 (why those were neutral): whole-block straight-line
code; guest GPR/EFLAGS **cached in locals across the block**; **inlined RAM
fast paths** (translate-probe + direct read/write) with MMIO/observe exits
inline-checked; block-local flag materialization; fast-mode block-granular
boundaries.
*Targets:* bench CPU-side **≥2.5–3×** vs current (triangle ≥ ~90–110
Msteps/s CPU-side, menu ≥ ~2×); integration **triangle 1B ≤ 22 s / menu
≤ 50 s** (vs 36.6/70.8).
*Kill:* <1.5× bench CPU-side after 4 focused weeks or 3 structural
iterations → stop polishing, reassess (Phase 3 design or re-scope). Do not
repeat the E-series pattern of landing wall-neutral slices for weeks.

**Phase 2 — lazy flags, block chaining, tiering, SMC invalidation (weeks).**
Reuse the §131/§134 invalidation inventory; tiering thresholds informed by
§§172–174. *Target:* cumulative CPU-side **5–10×** (≥100–370 Msteps/s);
integration triangle 1B ≤ 12 s / menu ≤ 30 s.

**Phase 3 — raw emitter (entry condition, not an assumption).**
Iced (or own) encoding; guest context pinned in a host register; direct block
chaining; reuses the Phase-1 translator. *Entry:* Phase 1–2 plateau >3×
short of the xemu-class target — otherwise the DynamicMethod path is
sufficient and cheaper to maintain. *Target:* xemu-class — triangle 1B ≤10 s
end-to-end, menu 1B ≤25 s, live window ≥30 guest-fps-equivalent on the menu
animation.

**Phase 4 — fast-mode playability (parallel-capable after Phase 2 passes).**
GPU offload coverage, presentation pacing, audio sync (APU mixer/host
output), input. Gate runs and the accurate mode are unaffected (no-mix binds
all gate runs). Scope detail filed when Phase 2 meets its bar.

## 5. Validation and gates (two-tier)

- **Exploration slice (fast mode only):** build + format clean, unit
  differentials green, bench numbers recorded with fixed commands. No golden
  obligation — fast-mode outputs are not gate material.
- **Promotion (default-on, product-visible, or shared-code effect):** the
  full accurate-mode ceremony unchanged (development-direction §2:
  median-of-3 + exact SHAs/counters + `verifyFailures=0` + flag-off
  byte-identical) **plus** fast-mode gates: sync-point differential clean on
  menu + triangle 1B, ktest outcome match, title acceptance per phase.
- **ktest smoke 7/7 stays the standing gate** (accurate mode).

## 6. Constraint deltas (operator-approved 2026-10-18)

| # | Rule (before) | Delta (now) |
|---|---|---|
| 1 | "Boundary-preserving only" | Accurate mode only; fast mode per §2 |
| 2 | "`DynamicMethod` stays rejected" | Rejected for accurate-mode relay slices (E1–E3 unchanged); **authorized for the fast-mode block JIT** (Phase 1) |
| 3 | "No machine-code emitter without beating the unrolled-C# baseline" | Accurate mode unchanged; Phase 3 authorized under §2/§4 entry condition |
| 4 | "No new NuGet without operator approval" | Iced (MIT) approved for Phase 3 (not yet referenced); Ryujinx/ARMeilleure (MIT) design reference; `sources.md` entry on any reuse |
| 5 | Perf-slice ceremony every slice | Two-tier (§5): exploration vs promotion |
| 6 | "No real Xbox games" | Unchanged; fast-mode **benchmark-only** use requires an explicit operator decision (none as of 2026-10-18) |
| 7 | License undecided | Stays non-GPL; GPL/QEMU-translation vehicle explicitly rejected (§1) |

## 7. Invariants (must not change)

- Accurate-mode determinism, goldens, byte-identical contracts, flag
  discipline — forever the gate currency.
- MMIO program-order semantics and fault precision in **all** modes.
- The dual-mode separation: never mix engines within a run; fast mode never
  becomes gate currency; fast ↔ accurate equivalence is defined by §3 sync
  points, not byte equality.
- `References2/` protection rules; ktest smoke; direct development
  (wave runner retired); **single-brain rule for codegen work** — no
  parallel agents inside the JIT.

## 8. Open items

- Flag naming (`--jit`; `--jit-exact` is fixed) and CLI wiring.
- Tier policy and thresholds (evidence base §§172–174).
- SMC invalidation design (reuse `DecodeCache` inventory; validate with a
  synthetic self-modifying harness).
- xemu throughput calibration (Phase 0, optional).
- Whether fast mode ever becomes the default — operator decision, not
  proposed.
- Phase 4 scope detail (GPU coverage, pacing, audio, input).

---

**Provenance.** Measured evidence: `xgrid-architecture-decisions.md` §§131,
134, 137–138, 149–179b, 186a, 187–189; `jit-codegen-scope.md` (§20.6
microbenchmark, §138 decomposition); `gpu-swapchain-brief.md` §5 (gap math);
`development-direction.md`. External (facts/design only): QEMU TCG (GPL-2.0),
Ryujinx ARMeilleure (MIT), Iced (MIT), Intel SDM. Operator decision
2026-10-18, recorded in decisions §190, `AGENTS.md`, `ImplementationPlan.md`.
