# Next session instructions (updated 2026-10-20 after the draw-command batching session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §205 (draw-command
batching: measurement, design, evidence, gates) + §204 (present-path split,
readback removal) + §203 (course correction), `docs/compatibility.md` (matrix +
harness), `docs/research-notes/nv097-coverage.md` (method-presence gap),
`docs/research-notes/gpu-reference-map.md` (where to look for GPU facts),
`docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
git log --oneline -1        # expect: the §205 draw-command batching commit
git status --short          # only other-session edits may appear - never commit those
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1534 green
```

Expected 1B fast-mode anchors (if these differ, the environment moved - record it,
do not "fix" the code):

| leg | RAM | CPU | `rasterPsh` (quiet) | `drawMs` (gl) |
|---|---|---|---|---|
| menu (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | `660CA11B…` | `F5D61C94…` | ~12.0 s | — |
| menu `--gpu gl --gpu-replace` | `DD023100…` | `F5D61C94…` | 0 | ~1.05 s (`flushMs` ~0.24 s) |
| triangle (same flags, sw) | `F57934BF…` | `CF79FC17…` | ~2.8 s | — |
| triangle `--gpu gl --gpu-replace` | `D1EDD170…` | `CF79FC17…` | 0 | ~0.25 s (`flushMs` ~0.013 s) |

Accurate-mode anchors (JIT off): menu `D9C99A58…`/`3D2740B8…`, triangle
`F57934BF.`/`9B5AFC2A.`. GL frame anchors: menu `6BE6325F…`, triangle `3FC4B419…`
(policy diff vs sw: menu 1852 px / maxDelta 1 / 0 violations, triangle 16744 px /
maxDelta 1 / 0 violations). Note the gl legs' **RAM** SHA differs by design (the
software twin does not write the surfaces); CPU SHA and every counter must match
the sw leg. Batching must never change either frame SHA: both were reproduced
byte-identically in §205.

- **1534 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, SHA `CA5D3867…`, byte-identical to the accepted
  run). The xemu leg still cannot bind its monitor socket on this host, and
  `scripts/ktest-smoke.ps1` still needs `-XisoSharp` (its default path has the
  old user name); §205 re-ran the gate's XGrid leg directly against the existing
  `wt-data\ktest-smoke\xgrid\ktest-smoke.iso`.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the twelfth session landed (§205) - do not redo it

1. **Draw-command batching is in and default-on.** `IPgraphBackend`
   `BeginDrawBatch()`/`EndDrawBatch()` bracket `Nv2aMmio.FlushDraw()`; the GL
   backend stages every triangle of one primitive flush and submits it as one
   `glDrawArraysInstanced` with one `glUniform4(capacity*25)` upload. Measured:
   `drawMs` 1971 → 1045 ms (-47%, 3/3), wall -4.2% (3/3), 59,437 instanced draws
   for 2.76M triangles. Do not re-add per-triangle `EnsureProgram`/
   `ApplyTextures`/`ApplyState`/`ApplyUniforms` inside a batch, and do not
   invent a state-fingerprint batch boundary: the guest-side boundary is exact.
2. **The shader is instanced.** `flat out/in int vTri` carries
   `gl_InstanceID`; both stages read the block with
   `#define T(i) uTri[((vTri) * 25) + (i)]`. `gl_InstanceID` is 0 for
   non-instanced draws, so **there is one program for both paths** - do not fork
   a second fragment source for the single-triangle case. The array size is
   `capacity * 25` and is part of `BuildFragmentSource`, so a fragment-source
   golden test must pass the capacity.
3. **Capacity = uniform budget.** `min(MaxFragmentUniformVectors,
   MaxVertexUniformVectors) - 128)/25`, clamped to [1, 64] (64 on this host,
   3 on a minimum-spec GL 3.3 driver). `XGRID_GL_NO_BATCH=1` forces 1 and is the
   same-binary A/B switch (`scripts/batch-205-ab.ps1`). Do not hard-code 64.
4. **Flush points are load-bearing.** `Clear`, `Flip`, `ResetState`, `Dispose`
   and every `TryDraw` that falls back to software flush the pending batch
   first, which keeps the software/GL write order identical to the pre-batching
   behavior. Any new backend entry point that can change FBO content or guest
   RAM must flush too.
5. **`drawMs` includes the flush** (`timed[drawMs= flushMs= …]`,
   `AddBatchFlushTicks` adds to both) so submission stays comparable across
   batched and unbatched runs. `diag[... batchCalls= batchTris= batchCap=]`.
6. **What is left of menu submission is packing, not GL calls**: ~803 ms =
   2.76M × ~290 ns of `PackVerticesInto` (100 float stores per triangle). If
   that ever matters, it needs a different data layout (e.g. float4-wide stores
   or a persistent-mapped UBO written directly), not fewer GL calls.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace` (quiet projection ~23.8 s)**: GL submission ~1.05 s
(0.24 s of it the instanced draws), CPU engine ~17 s, block builds ~1.4 s,
interpreter ~2.5 s, boot ~2 s. The remaining menu headroom is CPU engine
(Track C, frozen) plus the ~0.8 s of block packing.

**Triangle `gl+replace` (~6.1 s)**: CPU-bound (`nonJitMs` 3.75 s of `outerMs`
6.3 s); GL submission is ~0.25 s. Track B cannot close the 6 s target without
Track C (frozen).

The automated acceptance run is `scripts/present-path-acceptance.ps1` (interleaved
sw/gl pairs -> medians -> projection against the recorded quiet references). Its
SUMMARY is only meaningful when its `sw` leg matches the reference (menu ~33 s,
triangle ~8.7 s); on this host the *unmodified baseline* measures 62.6 / 14.0 s
even with an idle CPU, so absolute M-A status is undecided (see §5).

## 2. Recommended next session - fixed order

Track B's two scoped slices are now both in, so the order moves to the other
tracks. Pick **one**:

### Option A (Track E, the steering signal): grow the corpus

Highest value: **clear the nxdk toolchain blocker** (install MSYS2 clang/make on
D: - C: has little free space - then `git clone --recursive` nxdk +
`bin/activate`). That turns four existing sources into rows (`nxdk-mesh` = the
first textured 3D row). Then re-run `scripts/boot-to-first-frame.ps1 -All` and
update the matrix. The exit criterion (M-B) is >= 8 rows with at least two
beyond dashboard/triangle/ktest; today there are 3 runnable rows.

### Option B (Track P, product): one feature

Pick exactly one: host audio sink (APU output), physical gamepad binding, or
save-state V1 in the Avalonia app. The frontend is still sw-only, keyboard-only,
silent, stateless. Acceptance: the app plays a sample with sound, a pad, or
save/load without the CLI.

### Option C (Track B, only if M-A must be proven): close the projection gap

Re-run `scripts/present-path-acceptance.ps1` on a *reference-state* host (its
`sw` leg must read ~33 s menu / ~8.7 s triangle) and record M-A. If the menu is
still over 24 s, the measured next items are: block packing (~0.8 s, §205) and
the texture fingerprint (~0.5-0.9 s, needs guest-write span tracking - the
rejected shortcuts are in §204). Do not start either while B/E are open.

### Filed, lower value - do not start while A/B are open

- **Texture fingerprint exactness fix**: guest-write span tracking in
  `PhysicalMemory` (a coarse envelope compare per write, dirty flags per span) so
  the 16 sampled reads only run when the texture's span changed. Measured
  rejections: fewer/contiguous samples (silent fidelity regression), per-write
  8-span watch (net +0.5 s, hot path).
- **The 2-pixel k1 interleave** (>= 2% acceptance, bounded ceiling) - demoted.
- **O6 row spans / Vector128 pack-blend** - ceilings ~0.5 s.
- **Track C** stays frozen; C3-z (2-3%) only on an operator decision.

## 3. Measured negatives - do NOT re-try without a new measurement

- A **texture-buffer object** for the per-triangle block (the §204 sketch):
  it moves the block fetch into the fragment stage (~19-25 lookups/fragment on
  a 210M-fragment leg). The uniform-array + instancing form is why batching
  costs nothing per pixel (§205).
- **State-fingerprint batch boundaries** - a per-triangle compare that must
  cover every `PgraphDrawInfo` field; the guest-side `FlushDraw` boundary is
  exact and free.
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
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs **both 1B frame goldens to reproduce byte-identically**
  (§205 did: `6BE6325F…` / `3FC4B419…`) and the mirror-mode run (no
  `--gpu-replace`), not just replacement mode.
- **Strip probe instrumentation before any A/B leg.** Use a same-binary switch
  first (`XGRID_GL_PROBE=1`, `XGRID_GL_NO_BATCH=1`: off by default, only the
  switched leg pays).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency and real
  ISOs are fast-mode benchmark-only. `References2/` is read-only. Leave
  `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files; do not sweep unrelated working-tree edits.
- Write it up: the next decisions section (§206+) with the measured table,
  charter + inventory status headers, `AGENTS.md` session note, and a fresh copy
  of this file.

## 5. Host-state warning (2026-10-20, measured at session close)

**Absolute walls are NOT comparable to the recorded references on this host.**
At the §204 session close the *unmodified baseline* build (`4f0bd47`) measured
menu sw **62.6 s** and triangle sw **14.0 s** at 1B, against the recorded 33.3 s
/ 8.69 s, **with an idle CPU** (0.7 cores of background; `Win32_Processor`
reports its 3.6 GHz base clock). The raster share is inflated unevenly
(`rasterPsh` 20.1 s vs the recorded 12.0 s = 1.67x), so no single factor converts
today's numbers into the reference session's. Consequences:

- Trust same-window A/B pairs and `drawMs`/`flushMs` (host-side GL timers), not
  absolute walls or cross-session projections.
- Before reading `present-path-acceptance.ps1`'s SUMMARY as M-A, check that its
  `sw` leg is ~33 s (menu) / ~8.7 s (triangle); if not, the host is off the
  reference state and only the ratio matters.
- Suspects worth checking on a fresh boot: CPU boost/frequency state, power
  plan, memory clock, background I/O (Dropbox/syncthing), and Rider indexing.

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln                     # only after project-graph changes
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore; dotnet format XGrid.sln --no-restore --verify-no-changes

# standing ktest gate (~2-5 min on this host; needs -XisoSharp on this host)
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "data\test-samples\kernel-test-suite-default.xbe" -SkipXemu

# one measurement leg (parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# GL leg: add -Gpu gl -GpuReplace ; A/B: -Cli <other tree>\...\XGrid.Cli.dll

# draw-command batching A/B (same binary, interleaved off/on, medians)
pwsh -File scripts/batch-205-ab.ps1 -Workload menu -Pairs 3

# GL goldens (1B): sw vs gl frame policy + counters
pwsh -File scripts/gpu-slice1-golden.ps1      -OutDir wt-data\b1gold `
  -Bios data\xemu-0.8.136-windows-x86_64\Complex_4627.bin `
  -Mcpx data\xemu-0.8.136-windows-x86_64\mcpx_1.0.bin `
  -Eeprom data\xemu-0.8.136-windows-x86_64\eeprom.bin -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b1gold <same four> `
  -Hdd wt-data\chain-199l\hdd.qcow2 -Dvd wt-data\xemu-tri\triangle.iso

# Track E corpus rows (harness: one BTFF line + JSON per row)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample dashboard -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -All

# first presented frame + its step anchor on any boot
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... --frame-first out.bmp

# M-A acceptance in one command: interleaved sw/gl pairs, medians, projection
pwsh -File scripts\present-path-acceptance.ps1 -Pairs 3 -Workload both
```

`wt-data/ab-leg.ps1`, `wt-data/run-leg.ps1` and the baseline tree are untracked
scratch - recreate them from this file if they are gone.

## 7. Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`;
`--gpu-replace` sends draws to the GL backend so `psh` reads 0), **`XGRID_GL_PROBE=1`**
(sampled intra-draw split + `fpRam/fpAdapter` fingerprint reads), **`XGRID_GL_NO_BATCH=1`**
(force the single-triangle GL path), the GL line's
`timed[drawMs= flushMs= clearMs= readbackMs= readbacks=]` and
`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads= flipStaleUploads= batchCalls= batchTris= batchCap=]`
counters, `readback=on|off` and `methods=` in the `GPU backend=gl` lines,
`DiagnosticGenericSampler`/`DiagnosticGenericFill` (differential test switches),
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`.
