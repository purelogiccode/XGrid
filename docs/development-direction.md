# XGrid — Development Direction (canonical, 2026-10-16)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 1. Priority stack (in order)

1. **Track C JIT throughput — the binding constraint.** Emulation (CPU
   interpretation + devices) is 97–100% of wall; GL submission + readback is
   0–2.9% (§122). Current best is ~3.5 Msteps/s (menu) — real-time needs
   10²–10³ Msteps/s. Nothing else buys interactivity.
2. **Correctness gates before speed.** Golden SHAs + counters exact, then walls.
3. **Unblock the sample matrix.** NXDK toolchain (operator/host) is the mesh
   blocker — not more dashboard runs.

## 2. JIT rules (§§136–140, `jit-codegen-scope.md`)

- Specialize semantics (pre-specialized per-shape handlers + unrolled runner),
  not dispatch. The 354-case switch is NOT dominant — data-path helpers are.
- `DynamicMethod` stays rejected (~10% over unrolled C#) unless new numbers
  say otherwise. No machine-code emitter without beating the unrolled-C# baseline.
- Boundary-preserving only: `Step()` = one original boundary (TSC, IRQ poll,
  faults, idle replay unchanged). `--decode-cache` default-on (opt-out
  `--no-decode-cache`); `--block-exec` / `--spec-exec` / `--fetch-fast` /
  `--decode-verify` stay opt-in, default-off.
- Every perf slice needs: 1B off/cache/(new) pairs on menu + triangle with SHAs
  (`4EF1FBF6…`, `A1740DE0…`) + all counters exact, `verifyFailures=0`, vec=0,
  flag-off logs byte-identical, median-of-3 walls before claiming a win.

## 3. GPU / present rules (`gpu-backend-design.md`, `gpu-swapchain-brief.md`)

- Headless FBO (`--gpu sw` default) is the gate currency. Never mix
  (full-frame replace or no frame), no async (emulation-thread only).
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs
  an operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish (exotics, DXT/BRDF/mipmaps, cost model, seeds,
  writeback) until a title histogram in `data/test-samples/` proves usage.

## 4. Hard non-goals

- **No real Xbox games.** All title work uses `data/test-samples/`
  (`triangle.xbe`, `nxdk-hello/led/mesh/triangle/`,
  `kernel-test-suite-default.xbe`). 007/retail probes deferred (operator rule).
- No `References2/` deletes/moves/rewrites ever (Dropbox sync root — restore
  from `D:\xgrid-refs-restore\`, never work around a missing reference).
- No new NuGet without operator approval (precedent: `Xunit.SkippableFact`
  rejected; plain `[Fact]` + WGL-guard instead).

## 5. Standing gates

- `dotnet build XGrid.sln -c Release`, `dotnet test`, `dotnet format
  --verify-no-changes` clean; warnings are errors.
- ktest smoke 7/7 (~5 min, 792,800,535 steps bit-reproducible) + menu/triangle
  1B golden pairs before any perf/default-flip merge.
