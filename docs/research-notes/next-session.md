# Next session handoff (written 2026-10-20, after commit `903c75b`)

Read in this order: `AGENTS.md` (session notes + current state),
`docs/research-notes/jit-track-charter.md` (status header),
`docs/research-notes/xgrid-architecture-decisions.md` §199o/§199p,
`docs/research-notes/rasterizer-inventory.md`, then this file.

## Start state (verified at close)

- `master` @ `903c75b`; working tree clean.
- **1511 tests green**, `dotnet build -c Release` clean,
  `dotnet format --verify-no-changes` clean.
- ktest smoke **PASS 7/7** (never pass `References2/` to a cleanup tool).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- Quiet-host 1B anchors (fast + raw): menu RAM `660CA11B…` / CPU `F5D61C94…`,
  triangle RAM `F57934BF…` / CPU `CF79FC17…` (`--gpu sw`);
  with `--gpu gl --gpu-replace`: menu RAM `DD023100…`, triangle RAM
  `D1EDD170…` (CPU shas unchanged). Per-member cost now **8.8 ns triangle /
  31.6 ns menu** on the GL legs.
- Rasterizer O2 (RAM fast lane) and O4 (Morton swizzle spread) are in;
  O3 (sampler plans) was already in.

## Primary task: Track B - rasterizer O5 counter batching, then O1 leftovers

Per the charter's redirect criterion the end-to-end gap is now Track B/D; do
not restart broad CPU codegen.

### O5 - batch per-pixel diagnostics per fill (estimate 1.5-3.5 s menu, 0.5-1 s triangle)

Hot sites (all per pixel/sample today):

- `Nv2aMmio.TrySampleTexture`: `_textureFormatAttempts[color]++`,
  `TrackTextureCoords(rawX/rawY)` / `(x,y)`, `CountSample(...)` (state
  counters, first-address/texel, `TextureSampleSuccesses/RgbZero`).
- `ProcessFragment`: `DepthTestedPixels++`, `BlendedPixels++`,
  `DrawnPixels++`, zclip counters.
- The two fill loops: `TexturedPixels++` (also the inline path around line
  6008).

Recipe: build a small per-fill metrics struct (or locals) and thread it by
`ref` into `TrySampleTexture`/`CountSample`/`TrackTextureCoords` and
`ProcessFragment`; flush once at the end of each `FillTriangle` /
`FillTrianglePsh` / `FillSurface` (the fills already open a `GpuWriteBatch`
scope - do the flush next to `Dispose`). Exactness rules from the inventory:
all printed totals must be identical (never flush mid-draw - no observer
exists), `TrackTextureCoords` min/max must keep the same `if (v < min)`
semantics (NaN never wins), and the "first address/texel" state must be
initialized from the pre-draw `samples + misses == 0` test (the printed
`texstate` ordering depends on exact relative counts).

### O1 leftovers - per-fill fragment constants / variants

The blend fast path (`_blendFastSrcAlpha`) and `PrepareFragmentConstants`
already hoist the big items. Remaining per-pixel field loads to audit in
`ProcessFragment`: `_zclipActive`/range, `_alphaRefScaled`, `_blendConst*`,
`_colorSurfaceBase + PresentedColorOffset`, `pitch`/`bytes`, `zetaBytes`/
`zetaPitch`, and the `_zFormatFloat`/`zetaFormat` switches in depth decode.
Hoist pure per-draw reads into the fill (e.g., a `FragmentPlan` built once in
`EnterTextureState`/`PrepareFragmentConstants`); do **not** re-associate float
arithmetic (pixel-boundary exactness) and keep every counter increment.

### Acceptance (fast-mode exploration tier)

1. Build + format clean, `XGrid.Nv2a.Tests` + full suite green.
2. SW 1B goldens both titles: **RAM/CPU SHAs and every NV2A counter byte-for-
   byte identical** to the anchors above (pixels/textured/blended/depth/
   zclip/clears/frames/stalls).
3. Interleaved A/B against `c247bfb` (or the then-current master) on a quiet
   host, median-of-3 minimum: keep only if **>= 2% and 3/3 wins** on at least
   one title and no regression on the other; sub-2% needs n>=7 with mixed
   signs treated as parity (never averaged into a win).
4. Commit only what passes; record the measurement and any negative result in
   decisions (new §-section) and update AGENTS + plan.

## Secondary task: C3-z inline page pair (one scoped experiment only)

The `XGRID_RAW_PROFILE` tally (1B menu) is ~50% `MovLoad`/`MovStore`, so the
raw probe's dependent-load chain is the remaining CPU item. One experiment:
per-block one-entry page cache (VPN -> phys page + writable) in a frame slot,
with an invalidation epoch from the data-translate cache so a remap cannot
serve a stale page; fall back to the full probe. Acceptance: exact 1B
differentials + >= 2% outerMs on one title, 3/3; **revert if below 2%** (the
C3-y lesson: bookkeeping/instruction-count work was off the critical path).
Do not expand into general TLB work without this evidence.

## Also possible (pick by measured value, not by novelty)

- GL/`--gpu-replace` per-frame path and present pacing (charter Phase 4).
  The `--window` present path still does a redundant per-flip readback.
- Rasterizer O6 (row spans) / O7 (compiled combiner) - filed, medium risk.
- The remaining raw helper tail (`mul/imul`, string `movs`, high-byte `mov`,
  `fnstsw`, carry-rotates): small; only touch with a measured plan.

## Explicit non-goals / rules

- Do **not** re-try C3-y (neutral), the 1024-slot data translate cache
  (neutral), or relay/E-series emission (measured neutral).
- Do not flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games; `data/test-samples/` only. `References2/` is read-only -
  never delete/move/clean it. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Fast mode is never gate currency; a shared-code or default change needs the
  full accurate-mode ceremony (median-of-3 + exact SHAs/counters +
  `verifyFailures=0` + flag-off byte-identical) plus fast-mode gates.

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

# 1B goldens (fast + raw); add --gpu gl --gpu-replace for the GL legs
$root=(Get-Location).Path; $cli="$root\src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll"
$media="$root\data\xemu-0.8.136-windows-x86_64"; $pad="down@950000000+hold,none@962000000,a@970000000+hold,none@976000000"
dotnet $cli boot --bios "$media\Complex_4627.bin" --mcpx "$media\mcpx_1.0.bin" --eeprom "$media\eeprom.bin" `
  --dvd "$root\wt-data\xemu-tri\triangle.iso" `
  --jit --jit-threshold 16 --jit-spin --jit-raw --steps 1000000000 --gpu sw --bench --bench-name tri
dotnet $cli boot --bios "$media\Complex_4627.bin" --mcpx "$media\mcpx_1.0.bin" --eeprom "$media\eeprom.bin" `
  --hdd "$root\wt-data\chain-199l\hdd.qcow2" --pad-script $pad `
  --jit --jit-threshold 16 --jit-spin --jit-raw --steps 1000000000 --gpu sw --bench --bench-name menu
```

For the medians: `git archive <base-commit> | tar -x -C $env:TEMP\xgrid-base`,
build it there, then run interleaved old/new pairs and report wall + `outerMs`
+ SHAs (the pattern used in `wt-data/o4-ab`). Ask the operator to pause games
before timing; otherwise use interleaved ratios and say so.

## Diagnostics worth keeping

`--raw-shapes` (helper mnemonic/form tally), `XGRID_RAW_PROFILE=1` +
`XGRID_RAW_CHAIN=0` (raw member-kind mix; exact), `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--gpu-time`, `--gpu-replace`, `--bench`.
