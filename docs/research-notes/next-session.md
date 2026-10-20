# Next session handoff (written 2026-10-20, after the Track B fast-lane session)

Read in this order: `AGENTS.md` (session notes + current state),
`docs/research-notes/jit-track-charter.md` (status header),
`docs/research-notes/xgrid-architecture-decisions.md` §202 (+ §200 for the loops'
history), `docs/research-notes/rasterizer-inventory.md`, then this file.

## Start state (verified at close)

- `master` @ the commit titled *"gpu: rasterizer compiled-loop fast lanes"* (`git log --oneline -1`); working tree clean apart from
  other-session edits (do not commit those).
- **1522 tests green**, `dotnet build -c Release` clean,
  `dotnet format --verify-no-changes` clean.
- ktest smoke **PASS 7/7**.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary** (0.8.136 stays vendored).
- 1B SW anchors (fast + raw): menu RAM `660CA11B…` / CPU `F5D61C94…`,
  triangle RAM `F57934BF…` / CPU `CF79FC17…`; accurate mode: menu
  `D9C99A58…` / `3D2740B8…`, triangle `F57934BF…` / `98039867…`.
- Rasterizer 1B `rasterPsh` on the current build: **menu 12.2-12.7 s**,
  **triangle 2.85-3.05 s** (from 13.5 / 3.4 at the session start).
  `compiled=<fills>/<pixels>/<seconds>` in the `--gpu-time` line reports the
  compiled-loop share (menu ~456K fills / 308.2M px / ~12.0 s; triangle
  53,574 / 62.4M / ~2.6 s).

## What landed this session (§202)

1. **Per-fill direct-RAM sampler** in both compiled fills:
   `IGpuMemoryFast.TryGetRamBytes(out byte[], out uint)` +
   `SamplerAddressBound(in SamplerPlan)` prove the fill's highest sampled
   address, so the per-sample adapter call and span construction become one
   little-endian word read over the RAM array; every non-RAM/out-of-bound case
   still takes `ReadGpu`. `SampleFast` remains the reference implementation for
   the generic path.
2. **`SamplerPlan.DecodeKind`**: the fill's exact `DecodeTextureTexel` branch
   (0 = A8R8G8B8 default, 1 = A4R4G4B4, 2 = generic switch), removing the
   per-sample jump table for the shapes the titles use.
3. **Blend destination direct word** in the k1 fill when the whole framebuffer
   span is inside RAM (same bounds test `TryReadPixelFloat` performs).
4. **Lighting-loop provable clamp drops** (R1 power chain in [0,1]; combine
   products gated by a per-fill `|c0c|,|c1c| <= 1` predicate that all 53,574
   fills pass) plus per-fill `addCd` hoists and per-row barycentric hoists in
   both fills.

**Measured (1B, 3 interleaved pairs vs `65e136e`, SHAs + every counter
identical):** triangle `rasterPsh` 3.39 -> 2.87 s (-14.2%, 3/3), wall 9.15 ->
8.69 s (-4.0%, 3/3); menu 13.47 -> 12.49 s (-5.3%, 3/3), wall 35.45 -> 34.64 s
(-0.3/-2.4/-4.2%). Accurate mode byte-identical (see anchors above).

## Also landed (§202 addendum)

--jit-mmio (the opt-in device fast path) was **broken since it landed** - it halted
the 1B menu leg at 25,146,493 steps. Root cause was emitter bookkeeping, not the
device code: the device block is emitted *before* the member's slow block and ends in
EmitReloadAfterHelper() (_flagsLive = true), so the slow block materialized the
probe's own comparison flags into F_EFLAGS. Fixed by resetting _flagsLive = false
before slow (EmitLoad/EmitStore) and adding
EmitSyncGuestStateForHelper() (state + progress, no flag re-materialization) for the
device blocks. MmioFast_FlagsAcrossSlowMemoryMember_MatchInterpreter pins it
(fail-pre-fix EFLAGS 0x70 vs 0x130); both 1B goldens are exact with the flag on. It is
**not** a perf win (mmioMembers=0: the probe already sends device pages to the slow
path), so no recipe uses it - but the class of bug ("compile-time emitter state leaks
across a never-taken branch") is worth remembering for any new emitted path.

## Measured negatives - do NOT re-try without a new measurement

- **k2 constant fold** (stage-0 dot + power + combine chain is per-fill constant
  for kind 2): implemented exactly, measured **2-3% slower** on the same binary
  (fold 3.15/3.15/3.17 vs off 3.11/3.07/3.07 s psh). The hoisted constants do
  not stay in registers; the branch/register pressure costs more than the folded
  math saves.
- **Alpha-1 blend skip** (93.5% of menu k1 fragments have `fa == 1`, verified
  bit-exact over 198M pixels): neutral. The destination read is 2.4 ns/px.
- **`[MethodImpl(AggressiveInlining)]` on `SampleFast`**: neutral.
- **Per-member CPU bookkeeping / idle-replay / hotspot notes in the spec
  runner**: all neutral (interleaved probes).
- **Block-build threshold below 16**: the sweep (16/64/256) trades build cost
  (~200 us raw + 20 us IL per block) for interpreter time one-for-one.
- Earlier sessions: O5 counter batching, C3-y bookkeeping, the 1024-slot data
  translate cache, relay/E-series emission - all measured, all out.

## Where the remaining 1B wall is

Menu (~35 s): raster ~12.3 s + CPU engine ~18 s + block builds ~1.4 s +
interpreter ~2.5 s. Triangle (~8.7 s): raster ~2.9 s, CPU engine ~4 s,
interpreter ~1.5 s. Inside the raster, §202's ablation prices the k1 loop as:
**sampler ~4.5 s** (read latency + the dependent chain, not descriptor work),
interpolation ~2.5 s, combiner/pack ~1 s, destination read 0.5 s, write 0.2 s.

## Candidate next tasks (pick by measured value)

1. **2-pixel interleave in the k1 compiled loop (Track B, structural).** The
   sampler's 4.5 s is dominated by the dependent chain
   (interpolate -> floor -> fold -> address -> load -> decode -> combine), and
   the loop body is ~150 instructions, so the OoO window covers only ~2 pixels.
   Two independent pixel bodies per iteration (writes and counter commits kept
   in x order) is the only remaining sizeable raster lever. Acceptance: 1B
   `rasterPsh` >= 2% better, 3/3 pairs, exact SHAs + every counter, and the
   two compiled-fill differential tests still green.
2. **Pack/blend SIMD (`Vector128`)** for the k1 fragment: pack measured 0.3 s,
   blend similar; `cvttps2dq` + `packuswb` reproduce `(uint)(clamp(x,0,1)*255+0.5)`
   exactly for finite non-negative input, but the guard for NaN/signed-zero has
   to be written down first. Ceiling is ~0.5 s; do it only if (1) stalls.
3. **O6 row spans** (filed): an x-interval superset per row for the inside test;
   never change the float inside test itself.
4. **Track C** is at its structural limit for the current engine: the memory
   probe's dependent-load chain is the cost, and every bookkeeping/cache
   experiment so far measured neutral. Further CPU work needs a different
   design (e.g. block-level memory versioning), not more slices.
5. **GL / `--gpu-replace` per-frame path** and the `--window` present readback
   (charter Phase 4) - unchanged, still queued behind the raster.

## Explicit non-goals / rules

- Do **not** re-try the items in the measured-negatives list.
- Do not flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games; `data/test-samples/` only. `References2/` is read-only.
  Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Fast mode is never gate currency; a shared-code or default change needs the
  full accurate-mode ceremony (median-of-3 + exact SHAs/counters +
  `verifyFailures=0` + flag-off byte-identical) plus fast-mode gates.
- **Strip probe instrumentation before any A/B leg.** A `Stopwatch` pair left in
  `RunNonJit` cost 0.35 s over 479K calls and disguised a -10% raster win as a
  +1.3% wall regression. Use a same-binary switch to A/B a candidate against
  itself first; use `git archive <commit> | tar -x` + build for the baseline.
- If you restore a file with `Copy-Item`, touch it before rebuilding (MSBuild
  compares timestamps).

## Commands

```powershell
# build / test / format
dotnet restore XGrid.sln                     # only after project-graph changes
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore; dotnet format XGrid.sln --no-restore --verify-no-changes

# standing ktest gate (~1-2 min)
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "data\test-samples\kernel-test-suite-default.xbe"

# one measurement leg (parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# A/B against another build: add -Cli <other tree>\src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll

# scratch (untracked, this session): interleaved A/B with per-pair deltas
pwsh -File wt-data/ab-leg.ps1 -Workload tri  -Pairs 3
pwsh -File wt-data/ab-leg.ps1 -Workload menu -Pairs 3     # -BaseCli overrides the control

# accurate-mode leg (no --jit) for the shared-rasterizer differential
pwsh -File wt-data/run-leg.ps1 -Workload menu -NoFast -Log wt-data\acc-menu.log
```

## Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`;
`--gpu-replace` sends draws to the GL backend so `psh` reads 0 and the CPU path is
isolated), `DiagnosticGenericSampler` / `DiagnosticGenericFill` (differential test
switches; the latter also disables the compiled fills), `--raw-shapes`,
`XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--bench`.
