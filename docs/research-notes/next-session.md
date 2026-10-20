# Next session handoff (written 2026-10-20, after commit `6fb57b8`)

Read in this order: `AGENTS.md` (session notes + current state),
`docs/research-notes/jit-track-charter.md` (status header),
`docs/research-notes/xgrid-architecture-decisions.md` §200 (+ amendment),
`docs/research-notes/rasterizer-inventory.md`, then this file.

## Start state (verified at close)

- `master` @ `6fb57b8`; working tree clean.
- **1516 tests green**, `dotnet build -c Release` clean,
  `dotnet format --verify-no-changes` clean.
- ktest smoke **PASS 7/7**.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- 1B SW anchors (fast + raw): menu RAM `660CA11B…` / CPU `F5D61C94…`,
  triangle RAM `F57934BF…` / CPU `CF79FC17…` (all counters as recorded).
- **Rasterizer: the end-to-end wall is no longer raster-dominated.** 1B
  `rasterPsh` is now 13.9-15.1 s (menu) and 3.31-3.45 s (triangle), from
  24.0 / 6.19 at the session start (-44% / -45%); interleaved wall medians
  -21.9% / -23.5%. `compiled=<fills>/<pixels>/<seconds>` in the `--gpu-time`
  line reports the compiled-loop share (menu 456,164 fills / 308.2M px /
  13.8 s; triangle 53,574 / 62.4M / 3.2 s).

## What landed this session (§200)

1. **O5 counter batching: measured negative and reverted.** Eliding *every*
   per-pixel diagnostic moves the 1B raster by ~2% (menu) / ~0 (triangle).
   Do not re-attempt without a new measurement.
2. **O3b per-fill fast sampler** (`SamplerPlan.FastKind`): linear/Morton 2- and
   4-byte texels with clamp/mirror/wrap folds; exact unit tables in
   `DecodeTextureTexel` and `TryReadPixelFloat`.
3. **O1 compiled modulate fill** (k1, 245.8M menu px) and **compiled lighting
   fill** (k2/k3, 62.4M px on *both* titles). Both are per-fill predicates with
   the generic loop as the untouched fallback.
4. Cube-map face stride + swizzle masks hoisted into the sampler plan (exact,
   measured neutral here — the titles' cube stages never reach the face math).

## Where the remaining 1B wall is

Menu: ~40 s wall = 13.9-15.1 s software raster + ~25 s CPU engine/boot. The
raster is now two compiled loops (96% of menu pixels, 85% of triangle pixels)
plus 0.45 s legacy. Per-pixel cost in the compiled loops is ~45 ns/px (menu
blended+textured) and ~54 ns/px (triangle lighting).

## Candidate next tasks (pick by measured value)

1. **Compiled-loop micro-structure (Track B, medium value).** The k1 loop is
   245.8M blended+textured px at ~45 ns. The remaining per-pixel costs are the
   sampler memory read (bounds + span construction per texel), `TryReadPixelFloat`
   for the blend destination, `PackPixel`'s four clamps and `WritePixel`'s
   interface call. One scoped experiment each, exactly as O5 was priced:
   measure the ceiling first (elide the block, compare `rasterPsh`), keep only
   >= 2% and 3/3.
2. **O6 row spans (Track B, filed, medium risk).** Bbox iterations still cost
   ~5-10 ns each outside the triangles; the compiled loops are the place to add
   an x-interval superset per row (never change the float inside test).
3. **Track C again (charter redirect criterion).** With the raster at ~14 s of
   a 40 s menu wall, the CPU engine is the largest single item again. The raw
   member mix is ~50% `MovLoad`/`MovStore`; the C3-z inline page-pair experiment
   (one scoped trial, revert below 2%) is still un-run and still the only
   scoped CPU item on file.
4. **GL/`--gpu-replace` per-frame path** (charter Phase 4) and the `--window`
   present-path readback: unchanged, still queued behind the above.
5. **Differential-test coverage for the compiled fills.** The two compiled
   paths are currently pinned only by the 1B goldens (SHAs + every counter over
   308M / 62M pixels). `DiagnosticGenericFill` exists; a synthetic k2-k3 draw
   needs the 8-stage combiner register stream
   (`MethodCombinerColorIcwBase + stage*4`, `AlphaOcw`, `Factor0/1`, Control
   `0x00011108`, Final `0x0C0B000F/0x0C011180`) plus the four texture slots.

## Explicit non-goals / rules

- Do **not** re-try O5 counter batching (measured), C3-y bookkeeping (neutral),
  the 1024-slot data translate cache (neutral), or relay/E-series emission.
- Do not flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games; `data/test-samples/` only. `References2/` is read-only —
  never delete/move/clean it. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Fast mode is never gate currency; a shared-code or default change needs the
  full accurate-mode ceremony (median-of-3 + exact SHAs/counters +
  `verifyFailures=0` + flag-off byte-identical) plus fast-mode gates.
- If you restore a file with `Copy-Item`, **touch it before rebuilding**:
  MSBuild compares timestamps and will silently keep a stale binary (this cost
  an hour in the O5/sampler work).

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

# baseline tree for interleaved A/B
$base = Join-Path $env:TEMP "xgrid-base-<commit>"
git archive <commit> | tar -x -C $base
dotnet build (Join-Path $base "XGrid.sln") -c Release --nologo
```

## Diagnostics worth keeping

`--gpu-time` (raster seconds + `fastfills... compiled=...` + the rejection
histogram `rej=` when timing is on), `DiagnosticGenericSampler` /
`DiagnosticGenericFill` (differential test switches), `--raw-shapes`,
`XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--gpu-replace`, `--bench`.
