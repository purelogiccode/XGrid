# Next session instructions (updated 2026-10-21 after the display-model session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §208 (display model + the
triangle-media correction) + §207 (corpus: toolchain, rows, scanout export) +
§206 (references as one verified store, the host dimension, culling) + §205
(draw-command batching) + §204 (present-path split), `docs/compatibility.md`
(matrix + harness + the gaps the corpus found), `docs/research-notes/usage-histogram.md`
(measured NV097 usage), `docs/research-notes/nv097-coverage.md` (method-presence
gap), `docs/research-notes/gpu-reference-map.md` (where to look for GPU facts),
`docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest
git log --oneline -1        # expect: the §208 display-model commit
git status --short          # only other-session edits may appear - never commit those
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1605 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window.

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `51B56687…` | `F5D61C94…` | 35-54 s (load) |
| triangle (HDD + DVD, plain run) | `B45254CA…` | `7DAB63EB…` | ~11.3 s |
| triangle (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.5 s |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery (§208d addendum). CPU + counters are the portable
anchors.

The older triangle pair `DD1D516E…`/`CF79FC17…` is the **DVD-only** leg, which
never launches the disc's XBE - it measures the boot animation (2,673 draws /
35 frames / 42.3M px). §208d has the evidence and both scripts now use the gate
media. Pre-cull (before §206g) the fast menu RAM was `660CA11B…`.

Accurate-mode anchors (JIT off, HDD+DVD, post-cull): triangle RAM `812EA159…` /
CPU `2F632483…`, menu RAM `01BE5D64…` / CPU `3D2740B8…`; both frames `A1740DE0…`
and `4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle `7A860ED7…`
(policy diff, never byte-exact). The gl legs' **RAM** SHA differs by design (the
software twin does not write the surfaces); CPU SHA and every counter must match.

Frame exports at 1B (same runs, §208c): the **flipped** surface is the gate
anchor; the **display** frame is one page behind on a double-buffered title
(`surface - pcrtc = 0x12C000` = one 640x480x4 buffer) and converges when the
picture is static (menu: both `4EF1FBF6`; triangle display `AB50EF68`, menu
display `51658B3D`).

- **1605 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, byte-identical to the accepted run). The xemu leg
  cannot bind its monitor socket on this host; `scripts/ktest-smoke.ps1` needs
  `-XisoSharp` (its default path has an old user name).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).
- **Uncommitted carry-over that the tree needs to build:** the frontend csproj is
  bumped to Avalonia 12.1.4 by a parallel session, and
  `FileDialogService.CopyTextAsync` was migrated to the 12.x
  `DataTransfer`/`SetDataAsync` clipboard API to keep `dotnet build XGrid.sln`
  green. Neither file is part of §208; do not sweep them into your commits, and
  re-check them if the parallel session lands its own version.

## 0b. What the fifteenth session landed (§208) - do not redo it

1. **The display model is real.** The VGA register file behind `NV_PRMCIO` is
   decoded (CRTC index/data with CR0-7 protection, sequencer, graphics,
   attribute flip-flop), and `TryGetDisplayGeometry` mirrors xemu
   `nv2a_get_params` + QEMU `vga_get_resolution`: base = PCRTC START, pitch =
   `(CR13 | CR19[5-7]<<3 | CR25[5]<<6) << 3`, size from CR01/CR12/CR07, depth
   from CR28 (2 = 16 bpp, 3 = 32 bpp) with 15-bit vs RGB565 from PRAMDAC general
   control bit 12.
2. **`TryCopyDisplayFrame`** returns that buffer (backend readback when a GPU
   backend is attached, flipped-surface fallback before a mode is programmed).
   `TryCopyScanoutFrame` keeps its raw contract and uses the same geometry.
   Snapshot v4 -> **v5**.
3. **Product path:** `--frame-display <path>` (new) and the Avalonia frontend
   (`EmulatorSession` samples the display at ~30 Hz, so a guest that never flips
   still updates the picture). **`--frame` still exports the last flipped
   surface** - every gate frame anchor stays valid.
4. **Exact-lane port writes.** Do not infer the addressed VGA port by diffing the
   stored word: two registers may share a data byte (`WriteCrtc(0x11,0)` was
   swallowed after `WriteCrtc(0x07,0)`). `Nv2AMmio.Write` passes the exact lanes.
5. **Triangle media correction (§208d).** `raster-leg.ps1` and
   `present-path-acceptance.ps1` now use HDD+DVD for the triangle. Any wall you
   compare against pre-2026-10-21 triangle numbers is comparing a lighter leg.
   Also measured there: `--frame-first` changes the fast-mode **RAM** SHA (batch
   cap -> IRQ delivery), so quote the flag set with any fast RAM anchor.
6. **`alpha_blending` is not a gap (§208f).** The sample ping-pongs a shader
   transparency every 100 frames; at 4.6B XGrid is within 4.8/255 of the
   reference. New `scripts/frame-stat.py` (per-frame stats) and
   `scripts/frame-oracle.py` (best-of-N phase match) make animated samples
   measurable. The only remaining xgu oracle failure is
   `bump_cubemap_reflect` (cube direction mapping).

## 0c. What the thirteenth/fourteenth sessions landed (§206/§207) - do not redo it

- **§206 references governance + host dimension.** `References2/` is the single
  verified store (`scripts/refs-manifest.ps1`, 9 sets / 43,153 files);
  `docs/hosts.md` + `scripts/host-doctor.ps1` split semantic anchors (must match
  everywhere) from performance anchors (host-local).
- **§206 Track P product slice.** `XGrid.Input` owns `HostPad` +
  `XInputPadMapping` (pure, tested) + `XInputGamepadReader` (no `DllImport`);
  the frontend polls at 60 Hz, merges with the keyboard and is gated on window
  activation. `.xgs` save/load in the app.
- **§206g cull face.** `SET_CULL_FACE_ENABLE`/`SET_CULL_FACE`/`SET_FRONT_FACE`
  latch; `ShouldCull(area)` in both fill paths. 41,035 triangles culled per 1B,
  triangle pixels −35.3%, menu −11.1%, **every software frame and CPU SHA
  byte-identical**. `culled=` on the NV2A line.
- **§207 corpus.** Toolchain in `scripts/build-nxdk-samples.ps1` (MSYS2 + clang/
  lld/**llvm**/bison/flex/cmake, nxdk `a6c31ee`), `boot-to-first-frame.ps1
  -Corpus` re-measures; 14 rows over 10 samples, M-B met.
- **§207f/§208 scanout/display export.** `--frame-scanout`, `--frame-display`,
  and a `displaySha` per corpus row.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~1.05 s (0.24 s instanced draws), CPU
engine ~17 s, block builds ~1.4 s, interpreter ~2.5 s, boot ~2 s. Submission is
not the problem; Track C is frozen.

**Triangle `gl+replace` (gate media, 1B)**: **7.5 s** now measured (11.3 s sw).
The old "6.4 s / <= 6 s target" was computed on the DVD-only leg that renders the
boot animation, so **the M-A triangle target needs an operator decision** before
anyone chases it - the workload changed, not the performance.

**`present-path-acceptance.ps1`** is meaningful only when its `sw` leg matches
H1's reference in the same window (menu ~35-40 s, triangle ~11 s with the fixed
media). Absolute M-A status on H1 is still undecided; ratios are not.

## 2. Recommended next session - fixed order

Pick **one**:

### Option A (Track E, the steering signal): close the cubemap gap

**Not `alpha_blending`** - §208f proved that sample renders correctly (within
4.8/255 of the reference at 4.6B; the earlier verdict compared a mid-fade phase
of its 100-frame transparency ping-pong). The only remaining xgu oracle failure
is `bump_cubemap_reflect`: 2 colours at 3B (a solid green shape on black) and
`|dmean|` ~60/43/12 against its reference at 3.0B/3.6B/4.2B, while its texture
format decode is fixed (§207h). It samples **`0x3A` cube maps**, which
`IsSupportedSwizzledFormat` rejects, so 896,067 samples count a texture-mode
fallback. **Start from §208g**: cube sampling has *three* gates
(`IsSupportedSwizzledFormat`, `SamplerPlan.CubeBytes`, `TryDecodeSwizzledTexel`)
and 207h only moved the first, which is why "no pixels changed". With all three
on, the geometry samples and `|dmean|` drops from (59.6, 43.3, 11.5) to
(47.2, 14.1, 7.2) - but the reflection is striped/shifted, so the remaining bug
is the DOT_REFLECT_SPECULAR direction chain (face selection, s/t orientation,
the eye vector from texture-coord `.w`). Oracle: the reference screenshot +
`scripts/frame-oracle.py`. Acceptance: the reflection is visibly a reflection at
some phase, both gate frames stay byte-identical, and `fallbacks` reaches 0 with
pixels changing (not just counters).

### Option B (Track P, product): host audio sink (APU output)

Large and unscoped: `McpxApuDevice` has no PCM sink, so this is real output
emulation (EP FIFO drain, mixer fetch, S/PDIF framing) with no audio oracle
(§206e). Pick it only with an operator mandate.

### Option C (Track B, only if M-A must be proven): re-run the acceptance script

Re-measure `present-path-acceptance.ps1` (now on gate media) in one window and
record the ratios. Do not chase the old triangle 6 s target until the operator
re-bases it on the real workload.

### Filed, lower value - do not start while A is open

- Faithful VGA read-back (0x3D5 -> `cr[index]`): guest reads the window 302
  times/1B; changing what it reads is a behavior change needing a differential.
- Texture fingerprint exactness (guest-write span tracking) and the 2-pixel k1
  interleave - both measured below the noise floor (§204/§205).
- 16-bit display modes: modelled but no corpus sample uses one (unit coverage
  only).
- Cubemap addressing for `0x3A`-`0x3C` (§207h/§compat gap 3): the decode is
  fixed, the direction mapping has no oracle.

## 3. Measured negatives - do NOT re-try without a new measurement

- **DVD-only triangle legs.** They never launch the disc XBE (§207b, §208d);
  any triangle number measured that way is the boot animation.
- A **texture-buffer object** for the per-triangle block (per-fragment fetch).
- **State-fingerprint batch boundaries** - the guest-side `FlushDraw` boundary
  is exact and free.
- Per-draw probes (`Stopwatch` per draw inflated submission ~2x); sample
  1-in-64 (`XGRID_GL_PROBE=1`) or ablate.
- Weakening the texture fingerprint; a per-write texture-span watch in
  `PhysicalMemory`.
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
  measured ceiling behind its acceptance. A task that serves none is rejected.
- **Track C is frozen.** No new codegen while Track B/E/P are open.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run (no `--gpu-replace`).
- **Strip probe instrumentation before any A/B leg.**
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw` defaults without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency and real
  ISOs are fast-mode benchmark-only. `References2/` is read-only. Leave
  `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files. **The Avalonia 12.1.4 csproj bump and its clipboard
  migration are another session's edits** - leave them uncommitted.
- Write it up: a decisions section (§209+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state — two machines (2026-10-21)

**Development runs on two machines sharing this Dropbox tree.** Their fingerprints
live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before believing any wall
in this file. Anchor discipline: **semantic** anchors (SHAs, counters) must match
on both machines - a mismatch is a bug, not an environment note; **performance**
anchors (walls, `msps`, `rasterPsh`) compare only within one machine and one
window.

H1 (PETERSONPC) is the reference host: all four semantic anchors reproduce
exactly, `vec=0`, no fault. Its walls read +10%/+17% against the quiet
references; treat ceilings as ratios. §204-close's 62.6 s / 14.0 s menu/triangle
reading did not reproduce and is treated as a transient. Suspects worth checking
on a fresh boot: CPU boost/power plan, background I/O (Dropbox indexing), Rider
indexing.

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln                     # only after project-graph changes
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore; dotnet format XGrid.sln --no-restore --verify-no-changes

# standing ktest gate (~1-5 min on this host; needs -XisoSharp here)
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "data\test-samples\kernel-test-suite-default.xbe" -SkipXemu `
  -XisoSharp "C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp\XISOSharp.Cli\bin\Release\net10.0\XISOSharp.exe"

# one measurement leg (gate media; parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# GL leg: add -Gpu gl -GpuReplace ; A/B: -Cli <other tree>\...\XGrid.Cli.dll

# all three frame exports in one run (flip = gate anchor, scanout/display = TV)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... `
  --frame wt-data\out.bmp --frame-scanout wt-data\out-scan.bmp --frame-display wt-data\out-display.bmp

# Track E corpus (nxdk samples): build once, then measure every row
pwsh -File scripts/build-nxdk-samples.ps1                 # needs MSYS2 + clang/lld/llvm
pwsh -File scripts/boot-to-first-frame.ps1 -Corpus        # all staged ISOs, 1B, sw
pwsh -File scripts/boot-to-first-frame.ps1 -Sample gamma -Gpu gl -GpuReplace

# GL goldens (1B): sw vs gl frame policy + counters
pwsh -File scripts/gpu-slice1-golden.ps1      -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2 `
  -Dvd wt-data\xemu-tri\triangle.iso

# M-A acceptance: interleaved sw/gl pairs, medians, projection (gate media now)
pwsh -File scripts\present-path-acceptance.ps1 -Pairs 3 -Workload both

# session start: fingerprint + precondition + References2 integrity
pwsh -File scripts/host-doctor.ps1
pwsh -File scripts/refs-manifest.ps1 -Verify          # add -Quick to skip content hashing
pwsh -File scripts/refs-manifest.ps1 -Update          # only after adding/updating a reference set
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
`PresentedFrameDebug` (now carries `crtc=<w>x<h>+<pitch> bpp= display=<base>`),
`Nv2AMmio.VgaCrtcDebug` (CR01/07/12/13/19/25/28 + SR01 + the latched index),
`DiagnosticGenericSampler`/`DiagnosticGenericFill` (differential test switches),
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`.
