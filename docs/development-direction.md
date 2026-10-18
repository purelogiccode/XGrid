# XGrid — Development Direction (canonical, 2026-10-18)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 1. Priority stack (in order)

1. **Track C JIT throughput — the binding constraint.** Emulation (CPU
   interpretation + devices) is 97–100% of wall; GL submission + readback is
   0–2.9% (§122). Post-R7 medians: menu 70.8 s / triangle 36.6 s per 1B steps
   (§186a/§187) ≈ 14/27 Msteps/s end-to-end (CPU-side ≈22/37 s-derived,
   raster subtracted) — the real-time band is 10²–10³ Msteps/s, a 1–2 orders
   magnitude gap (≈3–45× per title). The specialization track has asymptoted
   (§§149–169: fallbacks −66%, walls 1.07–1.19×) and relay emission measured
   wall-neutral (§§171–179b). The authorized lever is the **fast-mode
   execution engine** chartered 2026-10-18
   (`docs/research-notes/jit-track-charter.md` — dual-mode contract, phases,
   kill criteria). Nothing else buys interactivity.
2. **Correctness gates before speed** — unchanged for the accurate mode
   (golden SHAs + counters exact, then walls); the fast mode has its own
   two-tier validation (charter §5) and is never gate currency.
3. **Unblock the sample matrix.** NXDK toolchain (operator/host) is the mesh
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

## 3. GPU / present rules (`gpu-backend-design.md`, `gpu-swapchain-brief.md`)

- Headless FBO (`--gpu sw` default) is the gate currency. Never mix
  (full-frame replace or no frame), no async (emulation-thread only).
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs
  an operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish (exotics, DXT/BRDF/mipmaps, cost model, seeds,
  writeback) until a title histogram in `data/test-samples/` proves usage.
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
