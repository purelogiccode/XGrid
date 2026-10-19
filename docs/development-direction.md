# XGrid — Development Direction (canonical, 2026-10-19)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 1. Priority stack (in order; re-aimed again 2026-10-19 evening, §199)

**Measured position (1B goldens, fast mode, quiet host, single runs — §198/§198a).**
Triangle **10.68 s** (gl+replace+spin; 20.28 s sw), menu **41.39 s** (56.82 s sw).
xemu on the triangle: ~70 fps ≈ 1.13× real-time ≈ **830 Msteps/s** → the
triangle gap is **~9×** (94 vs 830 Msteps/s) and the menu is **~30×** from the
real-time band (24 Msteps/s; no xemu oracle — xemu deadlocks at the dashboard
animation wait, §32).

**Cost structure (measured/derived):**
- triangle: runner **6.1 s (57%)** — 125M block invocations ≈ 18 ns fixed +
  543M members ≈ 7 ns — plus nonJit 1.9 s (18%, page-end spin), GL 0.5 s,
  bookkeeping ~2.2 s.
- menu: CPU-JIT **≈17–19 s (~45%)** + GL draw 7.0 s (17%) + bookkeeping 4.8 s
  (12%) + nonJit 3.6 s (9%).

1. **Track C CPU engine — the lead track again (§199).** The `DynamicMethod`
   block JIT is at its structural ceiling: per-block fixed cost (context
   load/store + dispatch + memo lookup) ≈ 18 ns, per-member helper calls
   ≈ 7 ns; the remaining Phase-2 leftovers are worth ~1.5–2× at most. The
   charter's Phase-3 entry condition is **met** (plateau >3× short of
   xemu-class). Order:
   **C2 trace emitter** (design: jit-trace-emitter-design.md) — multi-block traces with guest state in locals across
   the whole trace, inline RAM fast paths for the hot member shapes, interrupt
   polls at the same boundaries, side exits to the existing block JIT;
   target CPU-side ≥2× (triangle runner ≤3 s, menu CPU ≤11 s).
   **C3 raw emitter** — Iced (MIT, now unblocked), register allocation, direct
   block chaining (patch-on-link), inline softmmu-style TLB probes, helpers
   only for complex ops; target another ≥2× (triangle end-to-end ≤3 s, menu
   ≤12 s). Accurate mode untouched; fast mode is never gate currency.
   **Started (§§199e/199f): slices 1-2b landed behind opt-in `--jit-raw`** —
   per-block native x86-64 with fixed host registers, flags in the host FLAGS
   register, inline translate-cache probe against pinned pointers, and a
   reverse-P/Invoke trampoline for every unencoded shape. All 11 raw
   differentials green, suite 1492 green. **Still open before any default
   flip: a dozen `JitTests` fail with the raw path forced on, and there is no
   performance measurement yet** — close both, then measure against the
   10.63 s / 45.62 s baselines.
2. **Track D cheap wins — do first (days).** D3b: identify the page-end wait
   loop's shape (trace it) and remove the nonJit residue by cross-page block
   formation **or** (preferred) a generalized wait-skip that fast-forwards to
   the device event (same principle as `--jit-spin`, §198) — 1.9 s triangle /
   3.6 s menu. Bookkeeping: profile the machine/CLI advance path (2.2 s /
   4.8 s) with a measured breakdown before optimizing.
3. **Track B GPU** — B2c draw batching (menu drawMs 7.0 s → ≤2 s target);
   B3 coverage after. With `--gpu-replace` the rasterizer is no longer a cost;
   per-draw CPU submission is.
4. **Correctness gates before speed** — unchanged for the accurate mode
   (golden SHAs + counters exact, then walls); the fast mode has its own
   two-tier validation (charter §5) and is never gate currency.
5. **Unblock the sample matrix.** NXDK toolchain (operator/host) is the mesh
   blocker — not more dashboard runs.

**Milestone ladder (1B goldens, fast mode, gl+replace+spin unless noted).**
M1 (C2 + D3b + bookkeeping): triangle ≤6 s, menu ≤20 s. M2 (C3): triangle
≤3 s, menu ≤12 s. M3 (xemu-class): triangle ≤1.5 s, menu ≤4 s — i.e. within
~1.3–3× of the real-time band. Every milestone records a measured xemu
comparison where an oracle exists (triangle, ktest) and the real-time band
otherwise (menu).

## 2. JIT rules — accurate mode (unchanged; §§136–140, `jit-codegen-scope.md`)

- Specialize semantics (pre-specialized per-shape handlers + unrolled runner),
  not dispatch. The 354-case switch is NOT dominant — data-path helpers are.
- `DynamicMethod` stays rejected *for accurate-mode relay slices* (~10% over
  unrolled C#; E1–E3 measured wall-neutral). No machine-code emitter for the
  accurate path without beating the unrolled-C# baseline.
- Boundary-preserving only: `Step()` = one original boundary (TSC, IRQ poll,
  faults, idle replay unchanged). `--decode-cache` default-on (opt-out
  `--no-decode-cache`); `--block-exec` / `--spec-exec` / `--fetch-fast` /
  `--decode-verify` stay opt-in, default-off.
- Accurate-mode promotion + every default flip still needs: 1B off/cache/(new)
  pairs on menu + triangle with SHAs (`4EF1FBF6…`, `A1740DE0…`) + all counters
  exact, `verifyFailures=0`, vec=0, flag-off logs byte-identical, median-of-3
  walls before claiming a win.

## 2b. Fast-mode execution engine (chartered 2026-10-18)

- Governing doc: `docs/research-notes/jit-track-charter.md` — dual-mode
  contract, phases 0–4, kill criteria, approved constraint deltas.
- Fast mode (`--jit`, opt-in) is a separate engine: MMIO program order and
  fault precision retained; TSC/step/IRQ observability relaxed to block
  granularity; deterministic per binary+script; never gate currency; one
  engine per run (no mixing). `--jit-exact` is the bit-exact debug variant.
- `DynamicMethod` block JIT is AUTHORIZED for the fast mode (Phase 1); the
  raw emitter (Iced, MIT) is **authorized and unblocked** — the charter's
  Phase-3 entry condition is met (§199: the DynamicMethod plateau is ~9–30×
  short of the real-time band). Order: C2 trace emission (Phase 2.5, same
  planner, multi-block traces) → C3 raw emitter (register allocation, direct
  chaining, inline TLB).
- Exploration slices: bench + unit differentials + build/format (no golden
  obligation). Promotion: charter §5 (accurate-mode ceremony + sync-point
  differential + ktest outcome). The project stays non-GPL — no QEMU/xemu
  code translation (charter §1/§6).

## 2c. xemu-class performance campaign (chartered 2026-10-19)

**Target, defined as a number.** 1 guest-second ≈ 733M steps (PIT
cross-check, §68). Post-§198a the fast mode runs the goldens at **~3.3%
(menu) and ~12.7% (triangle)** of real-time (gl+replace+spin). "xemu-class"
means the same content runs at wall ≤ guest-time on a quiet host. The bar is
**measured against xemu on the same samples**, not asserted — measured rows:
triangle ISO **xemu ≈830 Msteps/s (1.13× real-time, 70 fps)**, ktest smoke
**xemu 15 s vs XGrid fast 9–10 s** (XGrid ahead on CPU-bound work). The menu
has **no xemu oracle** (xemu deadlocks at the dashboard animation wait, §32);
its target is the real-time band. Perf work is measured in fast mode with
verified guest-work equivalence (SHAs/counters); fast mode is never
correctness currency and exact mode stays the bit-exact oracle.

**Slices (B = GPU backend, D = MMIO, C = JIT leftovers).**

- **B1 — GL backend CPU-side profiling (first slice).** Per-phase timers in
  the GL draw/flip path (state translation, vertex upload, program bind,
  uniform upload, draw call, present/readback) behind `--gpu-time`; deliver
  the measured split of the menu +7–10 s and the triangle parity.
- **B2 — per-draw/per-method overhead elimination.** State-shadow caching,
  batched/UBO uploads, no redundant binds or program work. Acceptance: menu
  1B fast+gl ≤ fast+sw − 15 s (≈ ≤45 s), triangle ≤ sw − 8 s (≈ ≤20 s),
  golden policy (violations=0, maxDelta ≤ 1) and guest SHAs unchanged.
- **B3 — method-stream coverage for the sample corpus.** Extend the backend
  from the menu subset to the triangle stream's 471 unique method pairs and
  the ktest stream. Acceptance: zero `unsup` rows, guest counters identical.
- **B4 — present/pacing polish** only when playability needs it (Phase 4
  and swapchain rules unchanged).
- **D1 — MMIO fast dispatch.** Device-span guard in the emitted member path
  plus a fast device entry; acceptance: −50% slow-member overhead, bit-exact
  menu+triangle SHAs, flag-off byte-identical.
- **C1 — JIT Phase-2 leftovers** (IL chaining, threshold sweep); acceptance:
  ≥5% wall on both goldens, bit-exact.

Rules unchanged: headless `--gpu sw` stays the default and gate currency;
never mix backends; no async; wall claims require quiet-host medians — when
the operator is running Fortnite, pause and ask for an idle window; real ISOs
are benchmark-only per §4 (no gates, no fidelity work); non-GPL.

## 3. GPU / present rules (`gpu-backend-design.md`, `gpu-swapchain-brief.md`)

- Headless FBO (`--gpu sw` default) is the gate currency. Never mix
  (full-frame replace or no frame), no async (emulation-thread only).
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs
  an operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish (exotics, DXT/BRDF/mipmaps, cost model, seeds,
  writeback) until a title histogram in `data/test-samples/` proves usage.
- **GPU backend performance is Track B (lead perf track, §2c).** The
  rasterizer histogram (36–38% of both walls) satisfies the "prove need"
  condition for backend *performance* work; the sw rasterizer remains the
  default and gate renderer until B2's acceptance test is met. Backend
  fidelity coverage beyond the sample corpus stays gated by B3 + Phase 4.
- Fast-mode playability (GPU coverage, pacing, audio, input) is chartered as
  Phase 4, parallel-capable only after Phase 2 meets its bar; the no-mix rule
  keeps binding every gate run and the accurate mode regardless.

## 4. Hard non-goals

- **Real-content policy (operator-delegated 2026-10-19).** `data/test-samples/`
  (`triangle.xbe`, `nxdk-hello/led/mesh/triangle/`, `kernel-test-suite-default.xbe`)
  stays the **gate currency**: golden SHAs, ktest smoke, every acceptance test.
  Real Xbox ISOs at `H:\Tests\XBOXTest` are authorized for **fast-mode
  benchmark-only use** — wall/throughput measurement and xemu-class
  comparisons. Not for golden gates, not for accurate-mode acceptance, not
  for NV2A/APU fidelity work (that stays gated by the sample-usage rule).
  Long real-title probes still need snapshots-first discipline (plan V1).
- **License stays non-GPL.** No QEMU/xemu code translation; GPL is not the
  TCG-port vehicle (charter §1). New deps: only Iced (MIT, Phase 3) approved
  so far; otherwise the NuGet rule stands (precedent: `Xunit.SkippableFact`
  rejected; plain `[Fact]` + WGL-guard instead).
- No `References2/` deletes/moves/rewrites ever (Dropbox sync root — restore
  from `D:\xgrid-refs-restore\`, never work around a missing reference).

## 5. Standing gates

- `dotnet build XGrid.sln -c Release`, `dotnet test`, `dotnet format
  --verify-no-changes` clean; warnings are errors.
- ktest smoke 7/7 (~5 min, 792,800,535 steps bit-reproducible) + menu/triangle
  1B golden pairs before any perf/default-flip merge.
