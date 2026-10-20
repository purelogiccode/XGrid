# Next session instructions (updated 2026-10-20 after the Track B present-path session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §204 (measured present-path
split, the eight slices, the two shared-path wins) + §203 (course correction),
`docs/compatibility.md` (matrix + harness), `docs/research-notes/nv097-coverage.md`
(method-presence gap: 108 handled / 93 recorded), `docs/research-notes/gpu-reference-map.md`
(where to look for GPU facts), `docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
git log --oneline -1        # expect: the §204 Track B present-path commit
git status --short          # only other-session edits may appear - never commit those
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1532 green
```

Expected 1B fast-mode anchors (if these differ, the environment moved - record it,
do not "fix" the code):

| leg | RAM | CPU | `rasterPsh` (quiet) | `drawMs` (gl) |
|---|---|---|---|---|
| menu (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | `660CA11B…` | `F5D61C94…` | ~12.0 s | — |
| menu `--gpu gl --gpu-replace` | `DD023100…` | `F5D61C94…` | 0 | ~1.2-1.5 s (quiet) |
| triangle (same flags, sw) | `F57934BF…` | `CF79FC17…` | ~2.8 s | — |
| triangle `--gpu gl --gpu-replace` | `D1EDD170…` | `CF79FC17…` | 0 | ~0.3 s (quiet) |

Accurate-mode anchors (JIT off): menu `D9C99A58…`/`3D2740B8…`, triangle
`F57934BF.`/`9B5AFC2A.`. GL frame anchors: menu `6BE6325F…`, triangle `3FC4B419…`
(policy diff vs sw: menu 1852 px / maxDelta 1 / 0 violations, triangle 16744 px /
maxDelta 1 / 0 violations). Note the gl legs' **RAM** SHA differs by design (the
software twin does not write the surfaces); CPU SHA and every counter must match
the sw leg.

- **1532 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, byte-identical to the accepted run), smoke 7/7 rows
  green. The xemu leg still cannot bind its monitor socket on this host.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the eleventh session landed (§204) - do not redo it

1. **The GL split is measured** (do not re-measure from scratch): menu ~9.9 s =
   submission 6.87 s (2.49 us/draw) + readback 0.71 s + swizzle/alloc ~0.3 s.
   The readback was ~1 s, not 8.8 s. `XGRID_GL_PROBE=1` gives the intra-draw
   split (1-in-64 samples).
2. **Readback is opt-in**: `FlipReadbackEnabled` is set by the CLI only for
   `--frame` / `--frame-first`; a flip otherwise stays on the GPU. Do not
   re-enable it in the fast path; gates and screenshots opt in.
3. **One uniform + one draw per triangle** (`uTri[25]`, positions by
   `gl_VertexID`; no VBO upload). Program-structure cache, `TextureKey` structs,
   per-entry sample addresses, prefetched fingerprints, GL binding shadows,
   raw-tick timing, no double-upload on texture creation. Do not re-add per-draw
   strings, per-draw `BufferData`, or redundant binds.
4. **Shared-path wins are in**: `Nv2aMmio.CollectMethodHistogram` off in the CLI
   (menu sw -6.2%), `IPgraphBackend.ObserveMethods` lets a backend decline the
   per-method callback (loaded menu gl -5.8 s). Do not re-add a virtual call per
   PGRAPH method.
5. **Track E harness + rows**: `scripts/boot-to-first-frame.ps1`,
   `--frame-first`, `docs/compatibility.md` with four measured rows.
6. **Fidelity gate**: no NV2A/APU polish without a `docs/compatibility.md` row
   and a usage histogram (`nv097-coverage.md` is the method-presence side).

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace` (quiet projection ~24.8 s)**: GL submission ~1.2-1.5 s
(texture fingerprint ~0.5-0.9 s, two GL calls ~0.4 s, state compares), CPU
engine ~17 s, block builds ~1.4 s, interpreter ~2.5 s, boot ~2 s. The menu's
2.76M triangles are the structural limit: 2 GL calls each = 5.5M calls.

**Triangle `gl+replace` (~6.1 s)**: CPU-bound (`nonJitMs` 3.75 s of `outerMs`
6.3 s); GL submission is ~0.3-0.6 s. Track B cannot close the 6 s target without
Track C (frozen) or draw batching.

The automated acceptance run is `scripts/present-path-acceptance.ps1` (interleaved
sw/gl pairs -> medians -> projection against the recorded quiet references). Run
it on an idle host to settle M-A; the host was 1.6-2.0x slow all session.

## 2. Recommended next session - fixed order

### Option A (Track B, if the 24 s/6 s targets must be closed): per-triangle batching

Scope: batch the triangles of one draw command into **one** `glDrawArrays`
instanced call, with the per-triangle block (the current `uTri[25]`) read from a
**texture buffer object** indexed by `gl_InstanceID`. The fragment shader keeps
its software-exact barycentric math (values are identical float32 reads), so the
goldens must stay policy-clean. Ceiling: menu submission 1.2-1.5 s → ~0.2 s,
i.e. the menu target met with ~1 s margin; it does nothing for the triangle.
Kill: if the TBO path cannot reproduce the 200M byte-identical frame, stop and
record (do not weaken the interpolation).

### Option B (Track E, the steering signal): grow the corpus

Highest value: **clear the nxdk toolchain blocker** (install MSYS2 clang/make on
D: - C: has 247 MB free - then `git clone --recursive` nxdk + `bin/activate`).
That turns four existing sources into rows (`nxdk-mesh` = the first textured 3D
row). Then re-run `scripts/boot-to-first-frame.ps1 -All` and update the matrix.
The exit criterion (M-B) is >= 8 rows with at least two beyond
dashboard/triangle/ktest; today there are 3 runnable rows.

### Option C (Track P, parallel): one product feature

Pick exactly one: host audio sink (APU output), physical gamepad binding, or
save-state V1 in the Avalonia app. The frontend is still sw-only, keyboard-only,
silent, stateless. Acceptance: the app plays a sample with sound, a pad, or
save/load without the CLI.

### Filed, lower value - do not start while B/E/P are open

- **Texture fingerprint exactness fix** (the last ~0.5-0.9 s of menu submission):
  guest-write span tracking in `PhysicalMemory` (a coarse envelope compare per
  write, dirty flags per span) so the 16 sampled reads only run when the
  texture's span changed. Measured rejections: fewer/contiguous samples (silent
  fidelity regression), per-write 8-span watch (net +0.5 s, hot path).
- **The 2-pixel k1 interleave** (>= 2% acceptance, bounded ceiling) - demoted.
- **O6 row spans / Vector128 pack-blend** - ceilings ~0.5 s.
- **Track C** stays frozen; C3-z (2-3%) only on an operator decision.

## 3. Measured negatives - do NOT re-try without a new measurement

- Per-draw probes: a `Stopwatch` pair per draw inflated the submission bill
  ~2x (`drawMs` 6.87 -> 12.6 s). Sample 1-in-64 (`XGRID_GL_PROBE=1`) or ablate.
- `TimeSpan` per draw (`AddDraw(TimeSpan)`) - measurable; use `AddDrawTicks`.
- Weakening the texture fingerprint (contiguous window / fewer samples) - a
  fidelity regression disguised as a win; do not.
- A per-write texture-span watch in `PhysicalMemory` - ~0.2-0.4 s of write cost
  for ~0.5-0.9 s of read cost, and it touches the frozen CPU hot path.
- Earlier sessions: O5 counter batching, C3-y bookkeeping, the 1024-slot data
  translate cache, relay/E-series emission, block fusion - all measured, all out.

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
  measured ceiling behind its acceptance. A task that serves none is rejected.
- **Track C is frozen.** No new codegen while Track B/E/P are open.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke.
- **Strip probe instrumentation before any A/B leg.** Use a same-binary switch
  first (`XGRID_GL_PROBE=1` is off by default and only sampled draws pay).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency and real
  ISOs are fast-mode benchmark-only. `References2/` is read-only. Leave
  `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files; do not sweep unrelated working-tree edits.
- Write it up: the next decisions section (§205+) with the measured table,
  charter + inventory status headers, `AGENTS.md` session note, and a fresh copy
  of this file.

## 5. Host-state warning (2026-10-20)

The host was 1.6-2.0x slower than the quiet-host baselines all session (Rider
sustained >100% CPU; `Get-Counter` total 10-40%). Absolutes in this session's
notes are loaded-host numbers; compare same-window pairs only, and re-measure
the quiet absolutes when the host is idle (the goldens' `sw` legs are the host
reference: menu 33.3 s, triangle 8.69 s at 1B quiet).

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln                     # only after project-graph changes
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore; dotnet format XGrid.sln --no-restore --verify-no-changes

# standing ktest gate (~2-5 min on this host)
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "data\test-samples\kernel-test-suite-default.xbe" -SkipXemu

# one measurement leg (parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# GL leg: add -Gpu gl -GpuReplace ; A/B: -Cli <other tree>\...\XGrid.Cli.dll

# GL goldens (1B): sw vs gl frame policy + counters
pwsh -File scripts/gpu-slice1-golden.ps1      -OutDir wt-data\b1gold `
  -Bios data\xemu-0.8.136-windows-x86_64\Complex_4627.bin `
  -Mcpx data\xemu-0.8.136-windows-x86_64\mcpx_1.0.bin `
  -Eeprom data\xemu-0.8.136-windows-x86_64\eeprom.bin -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b1gold <same four> `
  -Dvd wt-data\xemu-tri\triangle.iso

# Track E corpus rows (harness: one BTFF line + JSON per row)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample dashboard -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -All

# first presented frame + its step anchor on any boot
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... --frame-first out.bmp

# interleaved A/B with per-pair deltas (untracked scratch)
pwsh -File wt-data\ab-leg.ps1 -Workload menu -Pairs 3 -GpuReplace -BaseCli <base>\XGrid.Cli.dll

# M-A acceptance in one command: interleaved sw/gl pairs, medians, projection
pwsh -File scripts\present-path-acceptance.ps1 -Pairs 3 -Workload both
```

`wt-data/ab-leg.ps1`, `wt-data/run-leg.ps1` and the baseline tree are untracked
scratch - recreate them from this file if they are gone.

## 7. Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`;
`--gpu-replace` sends draws to the GL backend so `psh` reads 0), **`XGRID_GL_PROBE=1`**
(sampled intra-draw split + `fpRam/fpAdapter` fingerprint reads), the GL line's
`timed[drawMs= clearMs= readbackMs= readbacks=]` and `diag[...]` counters,
`readback=on|off` and `methods=` in the `GPU backend=gl` lines,
`DiagnosticGenericSampler`/`DiagnosticGenericFill` (differential test switches),
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`.
