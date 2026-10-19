# Fast-mode execution engine (JIT track) — charter (2026-10-18, operator-approved)

> **STATUS 2026-10-20 (third session — C3 correctness campaign, §199g):
> GATE 1 CLOSED, GATE 2 AT 25.05M/1B.** `JitRawBlocks` forced on now runs the
> whole `JitTests` suite green — **12 → 0 failures, 56/56 JitTests +
> RawJitTests, suite 1492 green** — after fourteen latent raw-emitter fixes
> (non-terminator EIP advance, stale EIP for helpers, CLI/STI EAX clobber, the
> missing post-STI/POPF poll, the VPN-vs-slot xlat probe bug, unguarded inline
> stores, the poll's shadow-space overlap, host DF across helper calls — a
> Win64 ABI violation, flag-liveness leaking across merge points, the store
> guard clobbering guest EAX/ECX/EDX, GC-stale CPU/entries pointers, memory
> MovExtend, disp32 ≥ 0x80000000 encoding, WP/A-D parity). The **IL-fast vs
> raw-fast title differential matches bit-exactly through 25,053,067 triangle
> steps**, then diverges inside a raw block at `0x8001F9EB` (a 16-bit store to
> a page-table page; raw takes a #PF the IL run does not) — that is the gate
> before any measurement or default flip. The JitTests harness now forces
> `JitRawBlocks` on Windows so the native runner stays under the differential
> net; other hosts fall back to the IL runner automatically.
>
> **STATUS 2026-10-20 (second 12-hour window): PHASE 3 STARTED — the raw
> emitter executes real blocks.** C2 (DynamicMethod, state sync + native
> Fld/Fst/Fstp m32) and D3b landed first and took the fast-mode 1B goldens to
> **triangle 10.63 s / menu 45.62 s** (gl+replace+spin, SHAs exact; §199d).
> **C3 (`--jit-raw`, opt-in) now emits one native x86-64 function per block**
> — guest GPRs in fixed host registers, arithmetic flags in the host FLAGS
> register, memory members running the data translate-cache probe inline
> against pinned pointers, and a reverse-P/Invoke trampoline to the existing
> spec handlers for every unencoded shape, so every block is emittable
> (§§199e/199f; the bring-up blockers and the TSC gap are recorded there).
> Validation at that point: 11/11 raw differentials green, suite 1492 green;
> the Phase-3 exit criterion was *not* met (a dozen `JitTests` failed with the
> path forced on and no 1B measurement existed).
>
> **STATUS 2026-10-19 (evening, quiet host): PHASE 0 + 1 LANDED; PHASE 3
> ENTRY CONDITION MET — CPU ENGINE IS THE LEAD TRACK AGAIN (§199).**
> J1a–J1d + the coverage campaign + the day's fast-mode work (`--jit-spin`
> idle-wait skip, `--jit-timing` gate, `--jit-mmio`, 256-slot data-translate
> cache, cross-page decode entries) brought the 1B goldens to **triangle
> 20.28 s sw / 10.68 s gl+replace, menu 56.82 s / 41.39 s**. The structure
> inverted again: the **CPU engine is 57% of the triangle wall and ≈45% of the
> menu wall**, and the `DynamicMethod` block JIT has hit its structural
> ceiling — ~18 ns fixed per block (context load/store + dispatch + memo) ×
> 125M invocations plus ~7 ns per member × 543M, with the remaining Phase-2
> leftovers worth only ~1.5–2×. Measured xemu: **triangle ≈830 Msteps/s
> (1.13× real-time)** vs XGrid 94M (triangle) / 24M (menu) — a **~9× / ~30×**
> plateau, i.e. >3× short, which is exactly this charter's Phase-3 entry
> condition. **Decision (§199): C2 trace emitter next (DynamicMethod,
> multi-block traces, ≥2× CPU), then C3 raw emitter (Iced, now unblocked).**
> Fast mode is never gate currency; runs are deterministic per binary+flags.
> Cheap Track-D wins (D3b, bookkeeping) and Track B (B2c draw batching, the
> window present path) run behind the CPU track.

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

**Measured position (2026-10-19 evening, §199; 1B, fast mode, single runs —
the pre-§198a figures in parentheses):**

| Workload | 1B-step wall (sw) | 1B-step wall (gl+replace+spin) | End-to-end |
|---|---:|---:|---:|
| menu (dashboard) | 56.8 s (64.0) | 41.4 s (48.3) | ≈24 Msteps/s |
| triangle | 20.3 s (27.9) | 10.7 s (17.8) | ≈94 Msteps/s |

xemu on the triangle: ≈830 Msteps/s (1.13× real-time, 70 fps) → the triangle
gap is ~9× and the menu ~30× from the real-time band (733 Msteps/s). The menu
has no xemu oracle (xemu deadlocks at the dashboard animation wait, §32). The
structural remainder is the CPU engine — runner 57% of the triangle wall,
≈45% of the menu — see the status header and Phase 2.5/3.

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
*Status 2026-10-19 evening (§199):* partially landed (outer-loop chaining +
32-entry memo + lazy revalidation + fill-on-demand formation); the measured
plateau is ~94 Msteps/s (triangle) / ~24 Msteps/s (menu), and the remaining
items are worth ~1.5–2×. The fixed per-block cost (~18 ns: context
load/store + dispatch + memo) is the structural remainder — hence Phase 2.5.

**Phase 2.5 — trace emitter (C2; the immediate next slice, §199).**
Design: docs/research-notes/jit-trace-emitter-design.md (formation rules,
runner contract, lazy-sync mask table, validation order, kill criteria).
Emit *multi-block traces* in one `DynamicMethod`: follow the planned
successor at build time (unconditional jumps first, then hot conditional
paths), keep guest GPR/EFLAGS/EIP/TSC in locals **across the whole trace**
(no per-block context load/store), inline the RAM fast paths already proven
in `--emit-inline`, keep the interrupt poll at every block boundary (the
§193 wakeup contract), and exit to the existing block JIT on any trace miss
(state stored once per exit). *Target:* CPU-side ≥2× — triangle runner
6.1 s → ≤3 s, menu CPU ≈18 s → ≤11 s; end-to-end triangle ≤6 s / menu ≤20 s
with D3b + bookkeeping. *Kill:* <1.4× CPU-side after 3 structural iterations
→ skip straight to C3.

**Phase 3 — raw emitter (entry condition: MET, §199).**
Iced (MIT; approved, now authorized) or own encoding; guest context pinned in
a host register (or a register-allocated subset); direct block chaining
(patch-on-link); inline softmmu-style TLB probes; helper calls only for
complex ops; reuses the Phase-1/2.5 planner, decode cache and boundary
contract. *Entry:* Phase 1–2 plateau >3× short of the xemu-class target —
**met** (measured ~9× triangle / ~30× menu vs the real-time band; §199).
*Target:* another ≥2× CPU-side — triangle 1B ≤3 s end-to-end, menu ≤12 s
(headless gl+replace+spin), live window ≥60 guest-fps-equivalent on the menu
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
