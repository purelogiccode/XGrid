# Next session instructions (updated 2026-10-21 after the cubemap/TEXFILTER session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §209 (cubemap extent bug +
TEXFILTER landing) + §208 (display model + triangle-media correction) + §207
(corpus: toolchain, rows, scanout export) + §206 (references as one verified
store, the host dimension, culling) + §205 (draw-command batching) + §204
(present-path split), `docs/compatibility.md` (matrix + harness + the gaps the
corpus found), `docs/research-notes/usage-histogram.md` (measured NV097 and
TEXFILTER usage), `docs/research-notes/nv097-coverage.md` (method-presence gap),
`docs/research-notes/gpu-reference-map.md` (where to look for GPU facts),
`docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, then this file.

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest
git log --oneline -1        # expect: the §209 cubemap/TEXFILTER commit
git status --short          # must be clean
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1614 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window.

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `51B56687…` | `F5D61C94…` | 35-54 s (load) |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e: the
plain triangle leg reads `CB95C006…`/`B8EF1021…` both before and after §209 -
it is a different leg from the BTFF row, not a regression). Compare BTFF rows
with BTFF rows.

Accurate-mode anchors (JIT off, HDD+DVD, post-cull): triangle RAM `812EA159…` /
CPU `2F632483…`, menu RAM `01BE5D64…` / CPU `3D2740B8…`; both frames
`A1740DE0…` and `4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle
`7A860ED7…` (policy diff, never byte-exact).

- **1614 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, byte-identical); the smoke ran in 48 s on H1.
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the sixteenth session landed (§209) - do not redo it

1. **Cubemap reflection works.** The dominant bug was **the face extent**:
   `TrySampleTextureCube` read `TEXIMAGERECT`, but xemu takes a swizzled
   texture's extent from TEXFMT `BASE_SIZE_U/V` (`pgraph_get_texture_shape`).
   The guest never programs the rect, so the stale 512x512 default walked 1 MB
   face strides over 256 KB faces. With that fixed plus all three format gates
   (`0x3A`-`0x3C` whitelist, `CubeBytes`, a shared `DecodeTextureTexel`), the
   row is **21,041 colours / 0% black / `|dmean|` (5.3, 2.4, 1.3)**.
2. **The direction chain was correct**; §208g's "striped = DOT_RFLCT_SPEC"
   reading is disproven by a probe set (forced normal, normal-map dump,
   `TEX1`/`n`/`rv` dumps are all smooth). The hatch in a 3.6B frame is the
   environment map at a grazing view.
3. **TEXFILTER is a real sampler feature, opt-in.** `--tex-filter registers`
   enables a tent filter at LOD 0 (MIN/MAG, per-tap wrap, border taps, cube
   face-local clamp) and xemu's nine-tap 1-2-1 quincunx/gaussian convolution on
   PROJECT2D. Default `legacy` is byte-identical to the old sampler.
   - Menu effect: 8.7% of pixels change (mean |delta| 2.3/2.1/2.0, colours
     32,360 -> 39,254), **CPU SHA unchanged** (`F5D61C94…`), wall 34.9 -> 85.6 s.
   - Compiled fills only know the nearest shape, so a filtered draw falls back
     to the generic loop - that is the whole 2.4x.
4. **New diagnostics:** `filters=<min>/<mag>/<conv>` on the NV2A tex line (the
   measurement that justified the feature: menu stage 0/1 are `2/2/1`, stage 0
   = 211M of 267M texel samples/1B).

## 0c. What the fourteenth/fifteenth sessions landed (§206-§208) - do not redo it

- **§208 display model.** `NV_PRMCIO`'s VGA register file is decoded (CRTC with
  CR0-7 protection, sequencer, graphics, attribute latch), `TryGetDisplayGeometry`
  mirrors xemu `nv2a_get_params` + QEMU `vga_get_resolution`,
  `TryCopyDisplayFrame`/`--frame-display` export it and the Avalonia frontend
  samples it at ~30 Hz. `--frame` still exports the last flipped surface, so no
  frame anchor moved. Snapshot v5.
- **§208d triangle media.** DVD-only triangle legs never launch the disc XBE -
  they measure the boot animation. Gate media is HDD + DVD.
- **§207/§206** corpus toolchain (`scripts/build-nxdk-samples.ps1`), scanout
  export, references-as-one-store (`scripts/refs-manifest.ps1`), host dimension
  (`docs/hosts.md`, `scripts/host-doctor.ps1`), XInput + save states.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~1.05 s (0.24 s instanced draws), CPU
engine ~17 s, block builds ~1.4 s, interpreter ~2.5 s, boot ~2 s. Submission is
not the problem; Track C is frozen.

**Menu `sw` (the product path)**: 34.9 s at 1B, of which the software
rasterizer is ~12.5 s. With `--tex-filter registers` the raster path goes
generic: 85.6 s. Teaching the compiled fills the bilinear tap is the next
perf item if the flag is ever to become the default, and its ceiling is
measured (85.6 -> 34.9 s).

**Triangle `gl+replace` (gate media, 1B)**: 7.5 s (11.3 s sw). The old
"6.4 s / <= 6 s target" was computed on the DVD-only leg, so **the M-A triangle
target needs an operator decision** before anyone chases it.

## 2. Recommended next session - fixed order

Pick **one**:

### Option A (Track B/D, only if the filter flag should become the default)

**Teach the compiled fills the bilinear tap.** Both compiled fills (k1
modulate, k2/k3 lighting) have a transcribed nearest sampler; the flag currently
disables them. A filtered variant of the inner loop (two-tap row pass or a
4-tap gather, exact per-tap wrap) restores the ~2.4x the flag costs today.
Acceptance: `--tex-filter registers` menu wall within ~10% of the legacy wall,
the flag-on menu frame byte-identical to today's flag-on frame, every legacy
anchor unchanged, and the bumped sample still matches its reference.
Do not start it if the operator decides the default stays `legacy`.

### Option B (Track E, the operator decision first)

**Decide `--tex-filter registers`' future.** The evidence is complete: the
registers say tent + quincunx on 211M menu samples/1B, the flag changes 8.7% of
the menu's pixels with an identical CPU SHA, and the only oracle row
(`bump_cubemap_reflect`) is metric-neutral because its error is dominated by
environment content. What is missing is an oracle for the menu: a real-hardware
screenshot of the dashboard menu at a known step (or an xemu frame at a
matching phase) would settle it. Without one, flipping the default would move
`4EF1FBF6`/`51B56687` with no proof of improvement - do not do it.

### Option C (compatibility gap 6, large)

**Fixed-function transform.** `xgu distance_fog_ffp` and `spheremap_ffp` upload
MVPs to the fixed-function block and load no vertex program, so their draws are
degenerate (flat frames, 93k-112k triangles culled). A full FFP vertex stage
(matrix/lighting state, `SET_TRANSFORM_EXECUTION_MODE = FIXED`, per-stage
texgen) is a multi-session feature, upstream of both renderers. Pick it only
with an operator mandate.

### Option D (Track P, needs a mandate)

**Host audio sink.** `McpxApuDevice` has no PCM sink, so this is real output
emulation (EP FIFO drain, mixer fetch, S/PDIF framing) with no audio oracle.

### Filed, lower value - do not start while A/B are open

- GL-backend texture filtering (parity with the software path). The GL shader
  fetches texels itself, so this is a shader change (bilinear + convolution),
  not a `glTexParameter`.
- Mip selection (LOD computation) - the sampler always samples level 0; the
  corpus has no mipmapped sample yet.
- Faithful VGA read-back (0x3D5 -> `cr[index]`), texture fingerprint exactness,
  the 2-pixel k1 interleave - all measured below the noise floor (§204/§205/§208).

## 3. Measured negatives - do NOT re-try without a new measurement

- **Reading a swizzled texture's extent from `TEXIMAGERECT`** - stale guest
  default for cube maps; xemu uses `BASE_SIZE` (§209a).
- **Blaming `DOT_RFLCT_SPEC` for the cube's hatching** - the chain is faithful;
  probe dumps are smooth (§209b). The hatch is environment content at a grazing
  view.
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
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter` defaults without an
  operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency and real
  ISOs are fast-mode benchmark-only. `References2/` is read-only. Leave
  `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- Commit only your own files.
- Write it up: a decisions section (§210+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-21)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
believing any wall in this file. Anchor discipline: **semantic** anchors (SHAs,
counters) must match on both machines - a mismatch is a bug, not an environment
note; **performance** anchors (walls, `msps`, `rasterPsh`) compare only within
one machine and one window.

H1 (PETERSONPC) is the reference host: all four semantic anchors reproduce
exactly, `vec=0`, no fault. Its walls read +10%/+17% against the quiet
references; treat ceilings as ratios.

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

# TEXFILTER registers (opt-in; default legacy is byte-identical to the old sampler)
#   add --tex-filter registers to any boot leg
#   the NV2A tex line prints filters=<min>/<mag>/<conv> per slot

# bump_cubemap_reflect row + its oracle (phase sweep)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3000000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3600000000
python scripts/frame-oracle.py D:\xgu\_screenshots\bump_cubemap_reflect.png wt-data\alpha\sweep\b*.bmp

# Track E corpus (nxdk samples): build once, then measure every row
pwsh -File scripts/build-nxdk-samples.ps1                 # needs MSYS2 + clang/lld/llvm
pwsh -File scripts/boot-to-first-frame.ps1 -Corpus        # all staged ISOs, 1B, sw

# GL goldens (1B): sw vs gl frame policy + counters
pwsh -File scripts/gpu-slice1-golden.ps1      -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2 `
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
and `methods=` in the `GPU backend=gl` lines, **`filters=`** on the NV2A tex
line (TEXFILTER MIN/MAG/convolution per slot), `PresentedFrameDebug` (CRTC
geometry), `Nv2AMmio.VgaCrtcDebug`, `--tex-filter registers`,
`DiagnosticGenericSampler`/`DiagnosticGenericFill`, `--raw-shapes`,
`XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`.
