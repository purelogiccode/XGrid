# Next session instructions (updated 2026-10-20 after the oracle session)

**Read in this order:** `docs/development-direction.md` (course correction,
2026-10-20), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §203 (+ §202 for the
raster history), `docs/research-notes/compatibility-corpus.md`,
`docs/research-notes/xemu-0.8.137.md` (verified 0.8.137 delta),
`docs/research-notes/cpu-oracle.md` (independent x86 differential),
`docs/research-notes/jit-track-charter.md` (status header),
`docs/research-notes/rasterizer-inventory.md`, then this file.

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

## 0c. What the 2026-10-20 course correction changed - do not redo

Documentation/priority change only (decisions §203; no code). The canonical
order is now **Track B (host GPU present path) → Track E (corpus) → Track P
(product surface, parallel) → Track D (measured >2% only); Track C is frozen.**

- **Do not start a new codegen slice.** C3-x hit its target; C3-y and the
  1024-slot xlat cache measured neutral and were reverted; C3-z is deferred
  (2–3%). Only the operator's `--jit`/`--jit-raw` default flip remains.
- **Do not lead with the 2-pixel k1 interleave.** It is demoted to a ≥2%
  acceptance with a bounded ceiling. The present path is the measured bigger
  lever (~9.9 s / 686 menu frames).
- **Do not treat the sample corpus as the fidelity universe.** Fidelity work
  cites `docs/compatibility.md` rows + a usage histogram (Track E).
- **The frontend is now the product surface.** Track P items (audio, pad, save
  states) are first-class, not "future work".

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

Menu (~33 s sw / 31.1 s gl+replace): **raster ~12.0 s**, CPU engine ~17 s, block
builds ~1.4 s, interpreter ~2.5 s. Triangle (~8.7 s sw / 6.5 s gl+replace):
raster ~2.8 s, CPU engine ~4 s, interpreter ~1.5 s.

**Course correction (2026-10-20): the GL path is not a side quest.** It replaces a
12 s software raster yet nets only 2.2 s: ~9.9 s of GL draw/readback/present for 686
frames (~14 ms/frame), while the 33.6K draws at the documented ~2 us would be
~0.07 s. The known readback is 1.6 ms/flip (~1.1 s/1B), so ~8.8 s is unexplained —
measure the split before scoping. It is the largest lever left.

Inside the raster (§202 ablation of the k1 loop): **sampler ~4.5 s** (read latency and
the dependent chain, *not* descriptor work), interpolation ~2.5 s, combiner/pack ~1 s,
destination read 0.5 s, write 0.2 s, diagnostics ~0.

## 3. Recommended next session - fixed order (2026-10-20 course correction)

Do these in order; each is one session-sized slice. Do not start a lower item while
a higher one is unfinished, and do not re-open Track C codegen.

### Track B (lead): the `gl+replace` present path

Why: the product frontend renders on the software rasterizer, and the menu's GL path
spends ~9.9 s on 686 frames. It is the largest single unexplained overhead after the
rasterizer; the known readback alone (~1.1 s/1B) is not the whole story.

How:
- **Measure first, no code change.** Split the GL cost into draw submission,
  readback, and present/blit (a temporary timer pair, stripped before any A/B leg).
  If readback is < 2 s, stop and record the split — do not guess.
- Then add a fast-mode present that blits the GL FBO to the default framebuffer (or
  the window) **without a CPU readback**. Keep the readback for gate runs, `--frame`,
  and screenshots. Full-frame replace or nothing; never mix backends.
- Do not change the software rasterizer in this slice.

**Acceptance:** menu fast+gl ≤ 24 s and triangle ≤ 6 s at 1B (3 interleaved pairs),
`BackendUnsupported=0`, guest SHAs and every counter identical to the sw run,
`--gpu sw` goldens unchanged.
**Kill:** if the measured present-path overhead is < 2 s, revert and record the
negative; if the readback is small but submission/present dominates, the slice becomes
that item (not the raster).

### Track E: open the corpus (one session, docs + harness only)

Read `docs/research-notes/compatibility-corpus.md`. Deliver:
- `docs/compatibility.md` rows for the current corpus (already seeded) with a
  reproducible run command and a step anchor per row;
- a boot-to-first-frame harness (script, not a new test project) that runs a sample
  headless and records steps / frames / first-frame SHA;
- the sample wish list and the nxdk-toolchain blocker stated as a dependency.

**Acceptance:** the three current rows green (dashboard, triangle, ktest), every row
has a command, and a fidelity task can cite "row X + histogram Y".
**Kill:** none — this is the steering signal; do not let it slip past Track B.

### Track P (parallel, one session each)

Pick exactly one: host audio sink (APU output), physical gamepad binding, or
save-state V1. Do not start with the debugger or per-title profiles.
**Acceptance:** the Avalonia app plays a sample with sound, a physical pad, or
save/load without the CLI (`docs/development-direction.md` §3, M-C).

### Filed, lower value - do not start while B/E/P are open

- **2-pixel interleave in the k1 fill**: acceptance ≥ 2% menu, 3/3 pairs. Its ceiling
  is bounded (sampler ~4.5 s, loop ~150 instructions); demoted 2026-10-20.
- **O6 row spans**, pack/blend `Vector128`: ceilings ~0.5 s and less.
- **Track C identity-window probe**: frozen with Track C (§0.4 of the direction doc).
  The measurement recipe is kept below for a future re-open, not for now.

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

- **Steering rule (2026-10-20).** Every proposal states the track it serves
  (B/E/P/D) and the measured ceiling behind its acceptance. A task that serves
  none of them is rejected even if it is interesting.
- **Track C is frozen.** No new codegen while Track B/E/P are open; re-open only
  if Track B lands and the engine is again >50% of a measured wall.
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
- No real Xbox games in gates; `data/test-samples/` is gate currency and real ISOs are
  fast-mode benchmark-only. `References2/` is read-only - never delete/move/clean it.
  Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files; do not sweep unrelated working-tree edits.
- Write it up: the next decisions section (§204+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this file.

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
