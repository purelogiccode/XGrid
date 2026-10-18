# XGrid — Development Direction (canonical, 2026-10-19)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 1. Priority stack (in order)

1. **Track B GPU backend — the binding constraint (re-aimed 2026-10-19).**
   The fast-mode campaign (§§191–194) moved the wall. Measured on the 1B
   goldens, quiet host: the **software rasterizer is 24.3 s of the 64.0 s menu
   wall (38%) and 10.1 s of the 27.9 s triangle wall (36%)** (present in every
   CPU mode); MMIO slow-path device dispatch (118.9M / 21.3M member accesses)
   dominates the runner's remaining non-raster time; the pure CPU-JIT portion
   is only ~23 s / ~4 s. The GL backend is functional (slices 1–3 goldens,
   `--gpu gl`) but **measured parity-to-worse** on these workloads (menu
   +7–10 s, triangle ±0; §194 am.3) — its per-draw/per-method CPU cost cancels
   the raster savings. xemu-class needs a hardware GPU path with amortized
   per-draw cost, so the GPU backend performance pass is the lead track
   (§2c, slices B1–B4).
2. **Track D MMIO fast dispatch.** Device-window accesses must stop paying
   generic translate+sync overhead (target −50% slow-member overhead,
   bit-exact SHAs). 119M slow members at 1B menu is now the largest single
   CPU-side line item after the rasterizer.
3. **Track C JIT — leftovers only.** Fast mode is at 99% served, bit-exact on
   the menu, 3.76× on CPU-bound work; the remaining Phase-2 items (IL chaining
   ~4.5 s/1B, threshold auto-tune) are worth ~5–8% on the goldens — do them
   after B/D, not as the lead track. Phase 3 (raw emitter) stays not indicated.
4. **Correctness gates before speed** — unchanged for the accurate mode
   (golden SHAs + counters exact, then walls); the fast mode has its own
   two-tier validation (charter §5) and is never gate currency.
5. **Unblock the sample matrix.** NXDK toolchain (operator/host) is the mesh
   blocker — not more dashboard runs.

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
  raw emitter (Iced, MIT — approved for Phase 3, not yet referenced) only
  under the charter's Phase-3 entry condition.
- Exploration slices: bench + unit differentials + build/format (no golden
  obligation). Promotion: charter §5 (accurate-mode ceremony + sync-point
  differential + ktest outcome). The project stays non-GPL — no QEMU/xemu
  code translation (charter §1/§6).

## 2c. xemu-class performance campaign (chartered 2026-10-19)

**Target, defined as a number.** 1 guest-second ≈ 733M steps (PIT
cross-check, §68). Today the fast mode runs the goldens at ~2.1% (menu) and
~4.9% (triangle) of real-time. "xemu-class" means the same content runs at
wall ≤ guest-time on a quiet host. The bar is **measured against xemu on the
same samples**, not asserted: run xemu on `data/test-samples/` content
(triangle ISO and the ktest smoke workload) and record wall, frames, and
frames-per-wall-second as the baseline row. Perf work is measured in fast
mode with verified guest-work equivalence (SHAs/counters); fast mode is never
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
the operator is running Fortnite, pause and ask for an idle window; no real
Xbox games; non-GPL.

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

- **No real Xbox games.** All title work uses `data/test-samples/`
  (`triangle.xbe`, `nxdk-hello/led/mesh/triangle/`,
  `kernel-test-suite-default.xbe`). 007/retail probes deferred (operator
  rule). Fast-mode *benchmark-only* use of real titles requires an explicit
  operator decision — none granted as of 2026-10-18 (charter §6).
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
