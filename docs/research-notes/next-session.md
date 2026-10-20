# Next session instructions (updated 2026-10-20 after the oracle session)

**Read in this order:** `AGENTS.md` (session notes + current state),
`docs/research-notes/xemu-0.8.137.md` (verified 0.8.137 delta),
`docs/research-notes/cpu-oracle.md` (independent x86 differential),
`docs/research-notes/jit-track-charter.md` (status header),
`docs/research-notes/xgrid-architecture-decisions.md` §202 (+ §200 for the loops'
history), `docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
git log --oneline -1        # expect: the oracle/0.8.137 session commit
git status --short          # only other-session edits may appear - never commit those
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build   # expect 1519 green
python tools/cpu-oracle/oracle.py --list | Measure-Object -Line   # 333 cases
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\start-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\start-menu.log
```

Expected 1B fast-mode anchors (if these differ, the environment moved - record it,
do not "fix" the code):

| leg | RAM | CPU | `rasterPsh` (quiet host) |
|---|---|---|---|
| menu (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | `660CA11B…` | `F5D61C94…` | ~12.0 s |
| triangle (same flags) | `F57934BF…` | `CF79FC17…` | ~2.8 s |

Accurate-mode anchors (JIT off): menu `D9C99A58…` / `3D2740B8…`, triangle
`F57934BF.` / **`9B5AFC2A.`** (moved from `98039867` when the FPU
status word started latching ZE on divide-by-zero; a byte diff of the two
`.xgs` saves is exactly one byte, `0x0101 -> 0x0105`, RAM identical).

- **1519 tests green**, `dotnet build -c Release` clean, `dotnet format
  --verify-no-changes` clean, ktest XGrid leg at **792,800,535** steps with only the
  known deltas (319, 344); the xemu leg cannot bind its monitor socket on this host.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary** (0.8.136 stays vendored).
- Rasterizer 1B `compiled` share: menu ~456K fills / 308.2M px / ~12.0 s; triangle
  53,574 / 62.4M / ~2.6 s. Same host window: menu sw wall **33.3 s**, menu
  `--gpu gl --gpu-replace` **31.1 s** (use both as the session's reference pair).

## 0b. What the tenth session landed - do not redo it

1. **0.8.137 re-base.** `docs/research-notes/xemu-0.8.137.md` records the measured
   delta: the NV2A tree is formatting-identical except PTIMER (`ptimer.c` rewrite,
   `NV_PMC_INTR_0_PTIMER` bit 20 wired into `nv2a_update_irq`), the Vulkan blit
   filter and the GL shader-cache key. No new NV097 methods and no PSH/VSH/vertex/
   texture semantic change. The APU/DSP restructure is catalogued (4-vector
   interrupt table with ILLEGAL at 0x04, SR bit names, DMA format 0, the
   `DMA_CONTROL` 3-read completion, `XGSCNT`).
2. **CPU differential oracle** (`tools/cpu-oracle/`, 333 cases, defined-flag masks).
   It found and fixed `0F BA` (BT/BTS/BTR/BTC r/m, imm8) not being decoded, the
   `FPREM` condition-code mapping, and `FDIV` ZE/IE latching.
3. **Queued fidelity item (not done):** `Nv2aMmio` still implements the 0.8.136
   PTIMER (counter-only, ALARM_0 ignored, no PTIMER IRQ). Land the 0.8.137 model
   (61-bit register time with writable offset, 27-bit-masked ALARM_0 re-armed on
   wrap, alarm pending latched on `INTR_0` read, PMC bit 20) with unit
   differentials and unchanged menu/triangle goldens.

## 1. What the ninth session landed (202) - do not redo it

1. **Per-fill direct-RAM sampler in both compiled fills.** `IGpuMemoryFast.TryGetRamBytes
   (out byte[], out uint)` + `SamplerAddressBound(in SamplerPlan)` prove the highest
   address a fill can sample (wrap folds keep the coordinate inside the image;
   swizzled indices stay inside the mask union), so the texel read is one
   `BinaryPrimitives.ReadUInt32LittleEndian` over the RAM array behind one per-fill
   bounds proof. Non-RAM/out-of-bound cases still take `ReadGpu`; `SampleFast` stays
   the reference for the generic path.
2. **`SamplerPlan.DecodeKind`**: the fill's exact `DecodeTextureTexel` branch
   (0 = A8R8G8B8 default, 1 = A4R4G4B4, 2 = generic switch).
3. **Blend-destination direct word** in the k1 fill when the whole framebuffer span is
   in RAM (the same test `TryReadPixelFloat` performs, hoisted per fill).
4. **Lighting-loop provable clamp drops** (R1 power chain in [0,1]; combine products
   gated by a per-fill `|c0c|,|c1c| <= 1` predicate that all 53,574 fills pass) plus
   per-fill `addCd` and per-row barycentric hoists in both fills.
5. **`--jit-mmio` bug fixed** (pre-existing 25,146,493-step boot halt): the device
   block's `EmitReloadAfterHelper()` set the emitter's compile-time `_flagsLive`, which
   leaked into the `slow` block emitted after it, so the slow path materialized the
   probe's own comparison flags into `F_EFLAGS`. Reset before `slow` in
   `EmitLoad`/`EmitStore`; `EmitSyncGuestStateForHelper()` for the device blocks.
   Pinned by `MmioFast_FlagsAcrossSlowMemoryMember_MatchInterpreter`.
6. New diagnostics: `slowMem=` / `slowDev=` in the JIT stats line (raw slow-path memory
   members and their device share).

**Result (1B, 3 interleaved pairs vs `65e136e`, SHAs + every counter identical):**
triangle `rasterPsh` 3.39 -> 2.87 s (**-14.2%**, 3/3), wall 9.15 -> 8.69 s (-4.0%);
menu 13.47 -> 12.49 s (**-5.3%**, 3/3), wall 35.45 -> 34.64 s (-0.3/-2.4/-4.2%).
Accurate mode byte-identical.

## 2. Where the remaining wall is

Menu (~33 s): **raster ~12.0 s**, CPU engine ~17 s, block builds ~1.4 s, interpreter
~2.5 s. Triangle (~8.5 s): raster ~2.8 s, CPU engine ~4 s, interpreter ~1.5 s.

Inside the raster (§202 ablation of the k1 loop): **sampler ~4.5 s** (read latency and
the dependent chain, *not* descriptor work), interpolation ~2.5 s, combiner/pack ~1 s,
destination read 0.5 s, write 0.2 s, diagnostics ~0.

## 3. Recommended next session - pick exactly one track

### Track B first: 2-pixel interleave in the k1 compiled fill (raster)

Why: the sampler's 4.5 s is latency-bound (measured ~61 cycles per sample = no
overlap between pixels); the loop body is ~150 instructions, so the OoO window covers
only ~1.8 pixels. Interleaving two independent pixel chains is the only remaining
sizeable raster lever.

How:
- Work in `FillTrianglePshModulate` only. Keep the generic loop and
  `FillTrianglePshLighting` untouched.
- Structure it as two pipeline stages: (a) for pixel x and x+1 compute barycentrics,
  perspective, interpolate, and issue **both** texel loads / destination reads;
  (b) run pixel x's combine/format/write, then pixel x+1's. Writes and the counter
  commits stay in x order; `_textureStateFirstAddress`/`FirstTexel` must still record
  the x-order first sample.
- The three `continue`s (inside test, ZCLIP cull, alpha test) are the whole difficulty:
  a naive unroll that `continue`s would skip the sibling pixel. Either guard each
  pixel's stage-2 with its own `inside`/cull flags or fall back to the single-pixel
  body for the tail.
- `DiagnosticGenericFill` must keep forcing the reference loop (differentials).

**Acceptance:** 1B `rasterPsh` >= 2% better on the menu, 3/3 interleaved pairs, exact
SHAs and every NV2A counter on both titles, `CompiledModulateFill_*` differentials
green (they compare compiled vs generic on the same draw), accurate mode unchanged.
**Kill:** if the first complete implementation is < 2% on the menu, revert, record the
measurement in §203 and stop raster work for the session.

### Track C alternative: identity-window fast path in the raw memory probe (CPU)

Why: the menu's engine serves 861M members at ~20 ns; ~50% are MovLoad/MovStore
(raw-kind profile), so ~430M probes pay two dependent translate-cache loads. The Xbox
kernel maps RAM 1:1, so a per-block identity window would test `addr - base < size` and
read `[ramBase + addr]` directly (~8-12 cycles saved per memory member, ~1.2 s on the
menu = -3.5%).

How:
- Add `IdBase`/`IdSize` to `RawState`, published by `BuildRawState`.
- Probe order: identity window -> existing tag/phys cache -> slow. The identity test
  must be no weaker than the per-page cache: it is valid only while (a) the same CR3,
  (b) no PTE was written in the window, (c) the window was derived from the actual
  page tables (walk it, or better: only trust it when every page in the window was
  *observed* identity-mapped through the existing translate path).
- Invalidate from the same choke points that bump the translate epoch: the PT-page
  store guard, `invlpg`, CR3 writes, MMU flushes. **Any** doubt -> clear the window.
- Do not ship without a raw-vs-IL-vs-interpreter differential over the paging/fault
  tests and both 1B SHAs.

**Acceptance:** `outerMs` >= 2% better on the menu, 3/3 pairs, exact SHAs, all paging
tests green. **Kill:** any SHA difference that cannot be explained bit-exactly.

### Filed but lower value (only if the above stall)

3. **Pack/blend `Vector128`** for the k1 fragment: measured ceiling ~0.5 s (pack 0.3 s
   + blend ~0.2 s). `cvttps2dq` + `packuswb` are exact for finite non-negative input;
   write the NaN/signed-zero guard down first.
4. **O6 row spans**: an x-interval superset per row for the inside test; never change
   the float inside test itself. Ceiling is small for UI quads (bbox ~= triangle).
5. **GL / `--gpu-replace` per-frame path** and `--window` present readback (charter
   Phase 4). Measured on the quiet host at session close: menu sw **33.3 s** (raster
   12.0 s) vs `--gpu gl --gpu-replace` **31.1 s**, i.e. the GL path *replaces* a 12 s
   software raster yet nets only 2.2 s because the GL draw + readback costs **~9.9 s
   for 686 frames (~14 ms/frame)** while the 33.6K draws at the documented ~2 us would
   be ~0.07 s - the per-flip `glReadPixels` sync is the prime suspect. Worth a
   measurement pass (split draw submission vs readback vs present) before any change;
   not gate currency.

## 4. Measured negatives - do NOT re-try without a new measurement

- **k2 constant fold** (kind-2 stage-0 dot + power + combine is per-fill constant):
  implemented exactly, **2-3% slower** (fold 3.15/3.15/3.17 vs off 3.11/3.07/3.07 s
  `psh`). Hoisted constants do not stay in registers.
- **Alpha-1 blend skip** (93.5% of menu k1 fragments have `fa == 1`, bit-exact over
  198M px): neutral - the destination read is only 2.4 ns/px.
- **`[MethodImpl(AggressiveInlining)]` on `SampleFast`**: neutral.
- **Spec-runner per-member bookkeeping** (idle replay, mnemonic tally, `idle.Observe`,
  hotspot notes, IRQ poll): all neutral.
- **Block-build threshold** 16/64/256: trades build cost (~200 us raw + 20 us IL per
  block) for interpreter time one-for-one; 16 is optimal.
- **Device-translation caching / `--jit-mmio` as a win**: only 6% of the raw slow-path
  memory members are device space (menu 119K of 2.0M), so at most ~36 ms.
- Earlier sessions: O5 counter batching, C3-y bookkeeping, the 1024-slot data translate
  cache, relay/E-series emission, block fusion - all measured, all out.

## 5. Rules (non-negotiable)

- **Two-tier validation.** Accurate mode is gate currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` + flag-off
  byte-identical, plus the fast-mode gates. Fast-mode exploration needs interleaved
  A/B + unit differentials.
- **Strip probe instrumentation before any A/B leg.** A `Stopwatch` pair left in
  `RunNonJit` cost 0.35 s over 479K calls and disguised a -10% raster win as a +1.3%
  wall regression. Use a same-binary switch first, and `git archive <commit> | tar -x`
  + build for the baseline tree.
- **1B runs only from a full solution build**; if you `Copy-Item` a binary back, touch
  it (MSBuild compares timestamps).
- Do **not** flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games; `data/test-samples/` only. `References2/` is read-only -
  never delete/move/clean it. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files; the other session's xemu-0.8.137 docs and
  `tools/completion-graph` changes are not yours.
- Write it up: decisions §203 with the measured table, charter + inventory status
  headers, `AGENTS.md` session note, and a fresh copy of this file.

## 6. Commands

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

# interleaved A/B with per-pair deltas (untracked scratch from the last session)
pwsh -File wt-data\ab-leg.ps1 -Workload tri  -Pairs 3
pwsh -File wt-data\ab-leg.ps1 -Workload menu -Pairs 3     # -BaseCli overrides the control

# accurate-mode leg (no --jit) for the shared-rasterizer differential
pwsh -File wt-data\run-leg.ps1 -Workload menu -NoFast -Log wt-data\acc-menu.log

# baseline tree for A/B (HEAD = whatever this session starts from)
$base = Join-Path $env:TEMP "xgrid-base-<commit>"
git archive <commit> | tar -x -C $base
dotnet build (Join-Path $base "XGrid.sln") -c Release --nologo
```

`wt-data/ab-leg.ps1`, `wt-data/run-leg.ps1` and the baseline tree are untracked
scratch - recreate them from this file if they are gone (ab-leg runs the baseline and
the working tree alternately and prints per-pair wall/`psh` deltas plus both SHAs).

## 7. Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`;
`--gpu-replace` sends draws to the GL backend so `psh` reads 0 and the CPU path is
isolated), `DiagnosticGenericSampler` / `DiagnosticGenericFill` (differential test
switches; the latter also disables the compiled fills and the direct lanes),
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--jit-tally` ("Jit tail" non-JIT mnemonic mix), `--bench`.
