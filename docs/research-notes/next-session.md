# Next session instructions (updated 2026-10-21 after the fixed-function vertex stage session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §213 (the fixed-function
vertex stage, `--ffp`) + §211 (gap 10 diagnosed) + §210 (convolution gate + the
compiled tent fill) + §209 (cubemap extent bug + TEXFILTER) + §208 (display model
+ triangle-media correction) + §207 (corpus: toolchain, rows, scanout export),
`docs/compatibility.md` (matrix + harness + the gaps the corpus found),
`docs/research-notes/nv097-coverage.md` (method-presence gap; the fixed-function
rows moved to `handled` with §213), `docs/research-notes/usage-histogram.md`
(measured NV097 and TEXFILTER usage), `docs/research-notes/gpu-reference-map.md`
(where to look for GPU facts), `docs/research-notes/jit-track-charter.md`
(status), `docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest
git log --oneline -1        # expect: the §213 fixed-function vertex stage commit
git status --short          # must be clean
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1653 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window.

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `51B56687…` | `F5D61C94…` | 31-54 s (load) |
| menu + `--tex-filter registers` | `19FEA0EA…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `87BF40DA…` | `F5D61C94…` | ~59 s (the FFP layer draws 121M more px) |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` (HDD + DVD) | `B45254CA…` | `7DAB63EB…` | ~10.5 s |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.6 s (frame identical too) |
| `xgu-spheremap_ffp` 3B BTFF | off `B5747761…` / on `8062CFB5…` | `F0075C2F…` (both) | ~9 / ~14 s |
| `xgu-distance_fog_ffp` 3B BTFF | off `AFF08085…` / on `12BAD517…` | `571363B4…` (both) | ~9 / ~30 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). Compare BTFF rows with BTFF rows and `raster-leg` rows with
`raster-leg` rows - the `bumpenv_ripple` row above is the plain leg and does
**not** match the BTFF row in `compatibility.md`.

Accurate-mode anchors (JIT off, HDD+DVD, re-verified after §213):
triangle RAM `812EA159…` / CPU `2F632483…`, menu RAM `01BE5D64…` / CPU
`3D2740B8…`; both frames `A1740DE0…` and `4EF1FBF6…`. GL frame fingerprints:
menu `B6116DAE…`, triangle `7A860ED7…` (policy diff, never byte-exact).

- **1653 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, SHA `CA5D3867…`, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp` is opt-in too** (the fixed-function vertex stage).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the nineteenth session landed (§213) - do not redo it

1. **The fixed-function vertex stage exists, opt-in `--ffp`.** `FixedFunctionVertex`
   (transform / 1-4-matrix skinning / nothing-to-normalize / six texgen modes /
   texture matrix / color+specular selection / eye-space fog / screen-space
   epilogue) + `FixedFunctionVertexState` (the latched registers), transcribed
   from `xemu 0.8.137 hw/xbox/nv2a/pgraph/glsl/vsh-ff.c`. `FlushProgramDraw`
   runs it when `SET_TRANSFORM_EXECUTION_MODE` is FIXED (0) *and*
   `Nv2AMmio.FixedFunctionTransform` is on; no resident program is needed in that
   mode. Nv2a snapshot v5 -> **v6**. Diagnostics `fixedFn=` / `fixedFnLit=` on
   the NV2A draws line.
2. **Compatibility gap 6 is closed under the flag.** `xgu spheremap_ffp` 2 ->
   **7,358 colours**, `xgu distance_fog_ffp` 26 -> **7,601 colours**, guest CPU
   SHAs unchanged on both rows (rendering-only). Rows + commands in
   `docs/compatibility.md`; the harness gained `-Ffp`.
4. **Why it is opt-in (do not "fix" the default without an operator decision).**
   The EvoX dashboard issues 22,207 fixed-function draws per 1B steps; mode-0
   draws used to run the resident program, so the flag changes the dashboard's
   picture (that layer *is* the menu's animated background - the scanout leg
   shows the clean EvolutionX background). Flag-on menu: RAM `87BF40DA…`,
   CPU `F5D61C94…` (unchanged), 375,569,329 px, +28 s wall. The triangle is
   byte-identical either way (RAM, CPU and the `A1740DE0` gate frame).
   **Blast radius:** every corpus row boots through that dashboard, so a
   `-Corpus -Ffp` sweep (1B, 13 rows) changes every RAM SHA (e.g. `gamma`
   `4EE18F78` -> `FC56207C`) and the pixel count (42.0M -> 63.1M) at +3.5-4 s,
   while every CPU SHA and every frame/scanout/display SHA is identical; the
   flag-off sweep reproduces every documented 1B row anchor.
4. **Fixed-function lighting: half landed.** The material chain (ambient source,
   the material-emission product, the emission source, the oD0-alpha rule, from
   `SET_SCENE_AMBIENT_COLOR` / `SET_MATERIAL_EMISSION` / `SET_MATERIAL_ALPHA`)
   runs for lit draws; the **enabled-light loop** (infinite/local/spot,
   attenuation, spot cone, `reconstruct_specular_power`) does not and is counted
   in `fixedFnLights=`. Every lit draw measured so far has all lights off
   (`fixedFnLit=39`, `fixedFnLights=0` on every gate and corpus leg), and the
   landing is behavior-neutral on every leg (identical RAM/CPU/frames). Recipe
   for the rest: decisions §213b.

5. **§214: the Z24S8 clear-word layout is verified, not a defect.** Every
   consumer already reads depth = bits 8-31 / stencil = bits 0-7 (store, store
   path, compare, GL clear decode); `Nv2aZetaClearLayoutTests` (+4) pins it with
   a synthetic stencil differential (ref `0x74` draws, ref `0x00` rejected,
   zpass REPLACE writes the low byte and keeps the depth field). Found by
   re-reading §211f's filing; no product code changed.

## 0c. What the earlier sessions landed (§206-§212) - do not redo it

- **§212** the convolution tap step on linear (`LU_IMAGE`) stages.
- **§211** gap 10 diagnosed: `bumpenv_ripple`'s black frame is app state
  (combiner `V0 = T1`, stage-1 `NONE`, black edge texels) plus a zeta clear
  nearer than every fragment - not a rasterizer bug.
- **§210 TEXFILTER gate.** A kernel field only convolves when MIN is
  `CONVOLUTION_2D_LOD0` (7) (`psh.c:197-210`); the dashboard and the cubemap
  sample program `2/2/1`, i.e. a tent. Default stays `legacy` (operator
  decision, no oracle).
- **§209 cubemap + TEXFILTER.** A swizzled cube's extent comes from TEXFMT
  `BASE_SIZE_U/V`, not `TEXIMAGERECT`; `--tex-filter registers` landed opt-in.
- **§208 display model.** `NV_PRMCIO`'s VGA register file is decoded, and
  `TryCopyDisplayFrame`/`--frame-display` export the scanout; `--frame` still
  exports the last flipped surface. Snapshot v5 at the time.
- **§207/§206** corpus toolchain (`scripts/build-nxdk-samples.ps1`), scanout
  export, references-as-one-store (`scripts/refs-manifest.ps1`), host dimension
  (`docs/hosts.md`, `scripts/host-doctor.ps1`), XInput + save states.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~1.05 s (0.24 s instanced draws), CPU
engine ~17 s, block builds ~1.4 s, interpreter ~2.5 s, boot ~2 s. Submission is
not the problem; Track C is frozen.

**Menu `sw` (the product path)**: 31 s at 1B on this host, of which the software
rasterizer is ~10.8 s (`rasterPsh`; the compiled fills serve 429,350 fills /
277.0M px). The filtered path is the same cost; `--ffp` adds another ~28 s
because the dashboard's fixed-function layer is 121M extra fragments.

**Triangle `gl+replace` (gate media, 1B)**: ~7.5 s (10.5 s sw). The old
"6.4 s / <= 6 s target" was computed on the DVD-only leg, so **the M-A triangle
target still needs an operator decision**.

## 2. Recommended next session - fixed order

Pick **one**:

### Option A (gap 6 remainder, well scoped): the fixed-function light loop

`FixedFunctionVertex`'s lit branch runs the material chain; the enabled-light
loop is the missing half. Needed: the light color/position contexts
(`ltctxb` per-light ambient/diffuse/specular, `ltc1` local range, the
`SET_LIGHT_INFINITE_*` / `SET_LIGHT_LOCAL_*` / `SET_LIGHT_SPOT_*` state), the
three light types with attenuation and the spot cone, `specularPower`
(`reconstruct_specular_power` over `SET_SPECULAR_PARAMS`, a curve
reconstruction with coefficient tables) and the local-eye eye vector for
per-vertex specular. No measured leg enables a light today
(`fixedFnLights=0` on every gate and corpus row), so the acceptance is
necessarily synthetic: unit differentials per light type + a device draw that
writes a distinguishable color; every existing anchor must stay byte-identical
with `--ffp` on both backends.

### Option B (Track E, the operator decision): `--tex-filter registers` / `--ffp` defaults

Both flags are cheap-looking and both move pixels without an oracle. The
missing piece is the same for each: **a second-emulator frame** (compatibility
gap 9) or a real-hardware screenshot at a known step. Without one, both stay
opt-in.

### Option C (filed defects, each needs its own differential)

- **Z24S8 clear-word layout (§211f) - CLOSED 2026-10-21 (§214), verified as a
  no-bug.** The *stored* word already matches the reference (depth = bits 8-31,
  stencil = bits 0-7) in `FillSurface`, `WriteZeta`, the depth/stencil read and
  `GlStateMapper.MapZetaClear`; `Nv2aZetaClearLayoutTests` pins it with a
  synthetic stencil differential (ref `0x74` draws, ref `0x00` is rejected).
- **Stencil write/read masks through a clear** (`SET_STENCIL_WRITE_MASK` vs a
  cleared stencil) - not filed, but the new test file is the place to extend it.
- **Cull winding on quads** (§211d): each of `bumpenv_ripple`'s per-frame quads
  loses one of its two triangles and the app alternates `SET_FRONT_FACE`; needs
  a per-draw area-sign differential.
- **`bumpenv_ripple`'s app state** (what the full-screen reflection needs) -
  gap 9's second-emulator frame, not a decode.

### Filed, lower value - do not start while A/C are open

- GL-backend texture filtering (parity with the software path). The GL shader
  fetches texels itself, so this is a shader change (bilinear + convolution),
  not a `glTexParameter`.
- **GL replace mode loses software-fallback draws** (compatibility gap 8).
- Mip selection (LOD computation) - the sampler always samples level 0; the
  corpus has no mipmapped sample yet.
- Faithful VGA read-back (0x3D5 -> `cr[index]`), texture fingerprint exactness,
  the 2-pixel k1 interleave - all measured below the noise floor
  (§204/§205/§208).

## 3. Measured negatives - do NOT re-try without a new measurement

- **Reading the convolution kernel field alone as "convolve"**: MIN must be
  CONVOLUTION_2D_LOD0 (7) (`psh.c:204`). The `2/2/1` the dashboard and the
  cubemap sample program is a **tent**; applying a 9-tap blur there cost 2.4x
  wall and moved 8.7% of the menu's pixels with no oracle behind it (§210a).
- **Expecting a tent to change a gate title's frame** - both sample at texel
  centers (§210d).
- **Blaming `bumpenv_ripple`'s black frame on the texture format or the bump
  direction chain** - `0x28` misses are 0 and the `dsdt`/matrix/sample math
  matches the reference; it is state plus a zeta clear nearer than every
  fragment (§211a-211e).
- **Reading a swizzled texture's extent from `TEXIMAGERECT`** - stale guest
  default for cube maps; xemu uses `BASE_SIZE` (§209a).
- **Bilinear taps alone to clean up a grazing environment map** - metric-neutral
  at the best phase; the residual is content + no mip selection.
- **DVD-only triangle legs** - they never launch the disc XBE (§208d).
- A **texture-buffer object** for the per-triangle block (per-fragment fetch).
- **State-fingerprint batch boundaries** - the guest-side `FlushDraw` boundary
  is exact and free.
- Per-draw probes (`Stopwatch` per draw inflated submission ~2x); sample
  1-in-64 (`XGRID_GL_PROBE=1`) or ablate.
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.
- **"Mode 0 means the draw is degenerate"** - the dashboard's 22,207 mode-0
  draws render a real animated background layer (§213c).

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
  measured ceiling behind its acceptance. A task that serves none is rejected.
- **Track C is frozen.** No new codegen while Track B/E/P are open.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run (no `--gpu-replace`). A **flagged** behavior change must show
  the default path byte-identical and the flag-on delta measured.
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp` defaults without an
  operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency and real
  ISOs are fast-mode benchmark-only. `References2/` is read-only. Leave
  `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files.
- Write it up: a decisions section (§214+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-21)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
believing any wall in this file. Anchor discipline: **semantic** anchors (SHAs,
counters) must match on both machines - a mismatch is a bug, not an environment
note; **performance** anchors (walls, `msps`, `rasterPsh`) compare only within
one machine and one window.

H1 (PETERSONPC) is the reference host: every semantic anchor reproduces exactly,
`vec=0`, no fault. This session's walls: accurate menu 64.6 s, accurate triangle
43.7 s, fast menu 30.9 s (`rasterPsh` 10.75 s), fast triangle 10.4 s
(`rasterPsh` 4.17 s), menu `--ffp` 59.0 s, triangle `--ffp` 14.6 s, GL golden
legs 68.1 s (sw) / 71.6 s (gl) for the menu and ~44/52 s for the triangle. Treat
ceilings as ratios.

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
# (a stale work dir can be locked: pass -WorkDir wt-data\ktest-<n>)

# one measurement leg (gate media; parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# TEXFILTER leg: add -TexFilter registers   (METRIC prints filters=legacy|registers)
# fixed-function stage: -Extra @("--ffp")   (METRIC prints the FFP pixel delta)
# GL leg: add -Gpu gl -GpuReplace ; A/B: -Cli <other tree>\...\XGrid.Cli.dll

# accurate 1B RAM/CPU anchors (the goldens below bake --frame; this prints --bench SHAs)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... --steps 1000000000 --gpu sw --bench

# all three frame exports in one run (flip = gate anchor, scanout/display = TV)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... `
  --frame wt-data\out.bmp --frame-scanout wt-data\out-scan.bmp --frame-display wt-data\out-display.bmp

# fixed-function vertex stage (opt-in; default off keeps every anchor byte-identical)
#   add --ffp to any boot leg; the NV2A draws line prints fixedFn=<n> fixedFnLit=<n>

# TEXFILTER registers (opt-in; default legacy is byte-identical to the old sampler)
#   add --tex-filter registers to any boot leg
#   the NV2A tex line prints filters=<min>/<mag>/<effective kernel> per slot

# corpus row + its trend (the oracle screenshot lives outside this repo)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bumpenv_ripple -Steps 5000000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-spheremap_ffp -Steps 3000000000 -Ffp
python scripts/frame-stat.py wt-data\cube-legacy.bmp wt-data\cube-reg.bmp

# Track E corpus (nxdk samples): build once, then measure every row
pwsh -File scripts/build-nxdk-samples.ps1                 # needs MSYS2 + clang/lld/llvm
pwsh -File scripts/boot-to-first-frame.ps1 -Corpus        # all staged ISOs, 1B, sw

# GL goldens (1B): sw vs gl frame policy + counters (BOTH need -Hdd; the default
# path wt-data\xbox_hdd.qcow2 does not exist on H1 any more; the OutDir must exist)
pwsh -File scripts/gpu-slice1-golden.ps1 -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b2gold -Hdd wt-data\chain-199l\hdd.qcow2 `
  -Dvd wt-data\xemu-tri\triangle.iso

# session start: fingerprint + precondition + References2 integrity
pwsh -File scripts/host-doctor.ps1
pwsh -File scripts/refs-manifest.ps1 -Verify          # add -Quick to skip content hashing
pwsh -File scripts/refs-manifest.ps1 -Update          # only after adding/updating a reference set
```

## 7. Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`),
**`XGRID_GL_PROBE=1`** (sampled intra-draw split), **`XGRID_GL_NO_BATCH=1`**
(force the single-triangle GL path), the GL line's
`timed[drawMs= flushMs= clearMs= readbackMs= readbacks=]` and
`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads=
flipStaleUploads= batchCalls= batchTris= batchCap=]` counters, `readback=on|off`
and `methods=` in the `GPU backend=gl` lines, **`filters=<min>/<mag>/<kernel>`**
on the NV2A tex line (TEXFILTER per slot, kernel already gated on MIN 7),
**`fixedFn=`/`fixedFnLit=`** on the NV2A draws line (fixed-function vertex stage),
`PresentedFrameDebug` (CRTC geometry), `Nv2AMmio.VgaCrtcDebug`,
`--tex-filter registers`, `--ffp`, `DiagnosticGenericSampler`/`DiagnosticGenericFill`,
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`,
`frames=`/`pcrtc=` on the `Frame` line, `--pgraph <path> --pgraph-max <n>` (the
method stream - the tool that made §211 possible), `--eip-hist`.

**Probe recipe for a black-frame row** (§211 used it): print the method stream
with `--pgraph`, then gate temporary probes on a single env var and print (a) per
flip the presented surface's non-black count, (b) per draw the mode/ranges/
program/combiner/texture-state key, (c) the first vertices' post-transform
position + diffuse + uv, (d) the first fragments' coords, texels, diffuse and
final colour. Strip every probe before committing - `git status` must show only
the intended files. (§213's probe printed the execution mode, composite/model-view
rows, VPOFF/VPSCL and the first vertices' in/out values the same way.)
