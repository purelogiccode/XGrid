# XGrid compatibility matrix (Track E)

Status: **first measured rows, 2026-10-20; refreshed after §205 (draw-command
batching) with both backends per sample.** This is the steering document for
fidelity work, not a gate document. A row is evidence from a recorded run; gates
stay in `docs/development-direction.md` §6 and `docs/research-notes/next-session.md`.

## Rules

- Every row names a **reproducible command** and a **step anchor**. A row without
  a command is a claim, not data.
- Rows do not gate merges. The gates are the accurate-mode 1B goldens and the
  ktest smoke; this matrix decides *what fidelity work is worth doing*.
- Real Xbox ISOs are **not** rows. They are benchmark-only (fast-mode wall /
  xemu comparison) per the real-content policy.
- Update a row in the same change that alters its behavior, with the commit and
  the measured evidence.
- A fidelity landing must cite the row it fixes and the histogram that proves
  the usage. No row and no histogram means no NV2A/APU fidelity work.
- Method-presence side of the histogram: `docs/research-notes/nv097-coverage.md`
  (xemu 0.8.137 method table vs `Nv2aMmio`). **Usage side:**
  `docs/research-notes/usage-histogram.md` (measured NV097 streams for the gate
  sample and a benchmark probe). Reference orientation:
  `docs/research-notes/gpu-reference-map.md`.

## The harness (one invocation per row)

```powershell
# one row
pwsh -File scripts/boot-to-first-frame.ps1 -Sample triangle
pwsh -File scripts/boot-to-first-frame.ps1 -Sample dashboard -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample ktest          # needs the staged ISO

# every runnable row, both backends
pwsh -File scripts/boot-to-first-frame.ps1 -All
```

It boots the sample headless to a fixed step count, then prints one `BTFF` line
and writes `wt-data/btff/<row>.json`:

```text
BTFF sample= dashboard gpu=sw+... steps= wall= msps= frames= stalls= draws= pixels=
     firstFrameStep= firstFrameSha= lastFrameSha= ram= cpu= PASS|CHECK
```

`firstFrameStep` is the step anchor of the **first presented frame** (2M-step
resolution; the CLI's `--frame-first` saves it). `lastFrameSha` is the final
presented frame (the gate anchor). Fast mode slices its batches while hunting
the first frame, so the `ram`/`cpu` SHAs here are fast-mode anchors, **not** the
accurate-mode gate SHAs. A row with `verdict=CHECK` (fault, no frame) is red.

## Matrix

| Sample | Provenance | Boot | First frame | Render | Audio | Known gaps | Anchor (1B fast) | Command |
|---|---|---|---|---|---|---|---|---|
| **EvoX dashboard** (`xboxdash.xbe` on HDD, softmod) | `data/xemu-0.8.136-windows-x86_64` boot media; XBE 770,048 B, entry `0x12A17` | yes - MCPX ROM -> 1BL -> 2BL -> kernel -> XBE | **yes**, step 50M (boot animation), menu content from flip 33 | colored menu (32,789 colors; 7,389 RGB-diff px vs neutral) | APU/ACI emulated, **no host sink** | submenu state-100 pixel-negative; NV2A method coverage partial (see nv097 coverage); 686 flips/1B | RAM `51B56687` / CPU `F5D61C94`; last frame `4EF1FBF6`; gl `B6116DAE` (policy) | `-Sample dashboard` |
| **triangle.xbe** (nxdk `f998bdc`) | XboxDev ghidra-xbe `tests/xbefiles/triangle.xbe` | yes | **yes**, step 32M | colored Gouraud triangle, 68 frames / 34 stalls | n/a | none on this stream; CPU-bound leg | RAM `DD1D516E` / CPU `CF79FC17`; accurate frame `A1740DE0`; gl `7A860ED7` (policy) | `-Sample triangle` |
| **triangle.xbe, GL replace** | same XBE | yes | yes, step 32M | GPU rasterized, `BackendUnsupported=0` | n/a | target <= 6 s not met (6.4 s projected, CPU-bound) | CPU `CF79FC17`; frame `7A860ED7` (policy) | `-Sample triangle -Gpu gl -GpuReplace` |
| **kernel-test-suite-default.xbe** | Cxbx-Reloaded release `20260819191914-da80f1f` | yes | n/a (console output; `disable-video=1`) | n/a | n/a | 378/378 execute; known deltas 319 `RtlWalkFrameChain`, 344 `XcVerifyPKCS1Signature`; xemu leg cannot bind its monitor socket on this host | XGrid leg **792,800,535** steps, suite log 40,797 B byte-identical; smoke 7/7 rows green | `-Sample ktest` (needs the staged ISO) / `scripts/ktest-smoke.ps1 -SkipXemu` |
| **dashboard, 2B-step animation probe** | same HDD + gate pad script | yes | yes, 4,867 flips | same menu content (239 colors, same means); pixel-heavy 1.64B px | n/a | A-press does not reach a submenu; the submenu state-100 gap is unfixed | 2B run: 217,622 draws / `5.8 msps`; last frame `535C48FA` | `-Sample dashboard -Steps 2000000000` |
| **nxdk `mesh`** (textured, lit 3D) | nxdk `a6c31ee` `samples/mesh`, built by `scripts/build-nxdk-samples.ps1` | yes | **yes**, step 40M | 66 flips / 2,864 draws / 42.6M px; 5,115 clears, 44 programs, 48,627 triangles culled | n/a | renders its own content — the only corpus sample that does | RAM `D6A41BAC` / CPU `6DA19D89`; last frame `806CBEA7`, scanout `7328A99F` | `-Sample mesh` |
| **nxdk `gamma`** | nxdk `a6c31ee` `samples/gamma` | yes | **no new flip**: writes the scanout buffer directly (`XVideoGetFB`) and never flips | 0 extra NV2A draws (34 boot flips) | n/a | output visible only through the scanout export (768 colours, 298,744 px — the gradient); the flipped frame is a single black colour | RAM `4EE18F78` / CPU `C3371A8C`; scanout `D3EBF420` | `-Sample gamma` |
| **nxdk `gamma-fade`** | nxdk `a6c31ee` `samples/gamma-fade` | yes | same as `gamma` | 0 extra draws | n/a | same | RAM `ED83DC23` / CPU `D0A7BB8A`; scanout `2BC1C443` | `-Sample gamma-fade` |
| **nxdk `hello`** | nxdk `a6c31ee` `samples/hello` | yes | same (`debugPrint` writes text into the framebuffer) | 0 extra draws | n/a | text visible only through the scanout export (2 colours, 302 px) | RAM `4650454F` / CPU `C3371A8C`; scanout `72D33DC2` | `-Sample hello` |
| **nxdk `led`** | nxdk `a6c31ee` `samples/led` | yes | no video output at all (toggles the SMC LED) | 0 extra draws | n/a | scanout equals the flipped frame — the correct negative control | RAM `3D1F72DB` / CPU `1C98838F`; scanout `18C7CC83` | `-Sample led` |
| **nxdk `xaudio`** | nxdk `a6c31ee` `samples/xaudio` | yes | same as `gamma` (it is an audio sample) | 0 extra draws; **APU live**: 135 reads / 203 writes, `voiceOff=1`, ISTS bit 4 | **exercised**, but no host sink - nothing audible to verify | audio correctness unverifiable until a PCM sink exists | RAM `B0C863C5` / CPU `07DB79D2`; scanout `8A969D86` | `-Sample xaudio` |
| **xgu examples** (spinning cubes, textures) | https://github.com/Voxel9/xbox-xgu-examples | **blocked** - needs the examples fetched (toolchain now available) | - | textured 3D | - | fetch + build | - | - |
| **nxdk_vsh_tests** (VS goldens) | https://github.com/abaire/nxdk_vsh_tests | **blocked** - needs the repo fetched | - | vertex-shader oracle data | - | fetch + compare, not a boot row | - | - |
| **OpenXDK framebuffer demos** | xbdev.net / OpenXDK | **blocked** - needs the demos fetched | - | raw framebuffer | - | would exercise the direct-framebuffer gap above | - | - |

## Known gaps surfaced by the corpus (2026-10-20)

These are measured, not suspected — each one was found by running a sample, not
by reading code.

### 1. Direct-framebuffer output is not what `--frame`/the frontend present

`Nv2aMmio.CapturePresentedFrame` snapshots the **3D colour surface** at flip time,
and `TryCopyPresentedFrame` reads that surface live. Programs that write the
scanout buffer directly — nxdk's `XVideoGetFB()` and `debugPrint` — issue no 3D
draws and no flips, so the flipped frame stays at the boot animation.

The buffer was never missing: `TryCopyScanoutFrame` (new, reads PCRTC START) plus
`--frame-scanout` recover it. Measured at 1B, same runs:

| sample | flipped frame | scanout | what the scanout contains |
|---|---|---|---|
| `gamma` | 1 colour | **768 colours, 298,744 px** | the gradient + grey borders |
| `hello` | 1 colour | **2 colours, 302 px** | the printed text |
| `led` | `18C7CC83` | `18C7CC83` | nothing (correct negative control) |
| `mesh` | 8,695 colours | 8,548 colours | both have content; the buffers differ |

Four samples with four different RAM SHAs used to export one identical frame;
they are now all distinguishable (`scanoutSha` per row above).

**What is still open:** the *product* path (`--frame`, and the Avalonia frontend
via `TryCopyPresentedFrame`) still presents the flip surface. Making scanout the
default is not a one-line swap — for a flipping title the two buffers differ
(`mesh`: 8,695 vs 8,548 colours), so it needs xemu's display model (`pcrtc.start
/ 4` plus the VGA CRTC line offset, `nv2a.c:184-190`) and a deliberate gate
re-record. Scoped, measured, not guessed.

### 2. The repo's `nxdk-mesh` snapshot could never build

`data/test-samples/nxdk-mesh/` was missing `verts.h` (49 KB) and `texture.h`
(1.7 MB), both `#include`d by its own `main.c`, plus the `screenshot.png` its
README links. The corpus now builds from nxdk upstream (`samples/<name>` at
pinned `a6c31ee`) via `scripts/build-nxdk-samples.ps1`, so the stale snapshots
are no longer on the build path. `data/` is gitignored, which is why the gap
survived: nothing tracked the sample sources.

## Measured rows (2026-10-20, refreshed after §206g/§207)

> **Anchor note:** the matrix table above carries the current 1B fast anchors
> (dashboard RAM `51B56687`, triangle RAM `DD1D516E`) for the `raster-leg`
> command set. The `BTFF` rows below use the harness's own command (dashboard
> with the gate pad script, triangle with HDD+DVD), so their RAM SHAs differ by
> construction — compare rows within this table, not against the matrix.

Recorded with `scripts/boot-to-first-frame.ps1 -All` at 1B steps (ktest at 600M).
Rows refreshed 2026-10-20 after the cull-face landing, so these are post-cull
values.

**Software frames are exact anchors; GL frames are policy-validated** (diff
against the software frame: flat regions byte-exact, `±1 px` edge band, per-channel
tolerance ≤ 2). The exact GL SHA moved with the cull landing
(`6BE6325F -> B6116DAE` dashboard, `3FC4B419 -> 7A860ED7` triangle) while the
software frame stayed byte-identical and both GL gates still report
`violations=0 maxDelta=1` — that is the documented contract, not a regression.

| Row | wall | msps | frames | first frame | last frame | SHAs (ram/cpu) |
|---|---:|---:|---:|---|---|---|
| triangle, `sw` | 11.7 s | 85.6 | 68 | step 32M, `6F780A33` | `A1740DE0` | `0117A5E2` / `7DAB63EB` |
| triangle, `gl+replace` | 7.1 s | 141.5 | 68 | step 32M, `6F780A33` | `7A860ED7` (policy) | `8AE82781` / `7DAB63EB` |
| dashboard, `sw` | 34.0 s | 29.4 | 686 | step 50M, `6F780A33` | `4EF1FBF6` | `94A6D53C` / `F5D61C94` |
| dashboard, `gl+replace` | 26.5 s | 37.7 | 686 | step 50M, `6F780A33` | `B6116DAE` (policy) | `CF151863` / `F5D61C94` |
| ktest (600M, `disable-video=1`), `sw` | 9.0 s | 66.4 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `974F9901` / `76002239` |
| ktest (600M, `disable-video=1`), `gl+replace` | 7.4 s | 81.5 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `42C9C960` / `76002239` |

Reading: the first presented frame is the boot animation (`6F780A33`) in every
row - a boot-chain anchor, not a title anchor. The title anchors are the
last-frame SHAs. **Software frames are exact; GL frames are policy-validated**
against the software frame (`violations=0 maxDelta=1` on both gate scripts), so
the GL column is a fingerprint, not a byte-exact golden.
The ktest rows are the smoke ISO staged by `scripts/ktest-smoke.ps1`; its suite
log is the real evidence (`792,800,535` steps, 7/7 known deltas), the frames are
just the boot animation because the suite config disables video. The GL rows
report `pixels=0` by design: `--gpu-replace` hands the fills to the backend, so
the software raster counters are empty while the draw/flip counters stay
identical to the `sw` row (`draws=33638` dashboard, `2706` triangle).

### Observation: the dashboard keeps presenting past 2B (no submenu reached)

A 2B-step run with the gate's pad script (`down@950M+hold, none@962M,
a@970M+hold, none@976M`) presents **4,867 flips** (686 at 1B) and ends on
surface `0x3D00000` (`read=0 write=0`), but the frame is the **same menu
content**: same 239 distinct colors and the same channel means (55.3/101.4/156.8)
as the 1B menu frame, different SHA (animation/highlight state). It is also
pixel-heavy - 217,622 draws, 1.64B pixels, `5.8 msps` (344 s for 2B) - i.e. the
`-gpu sw` wall is dominated by full-screen animation, not by a new screen. So
the "submenu state-100 pixel-negative" gap is still unfixed *and* the A-press
script does not reach a submenu; a future corpus row needs the correct pad
sequence (an operator-visible route), not more steps.

## Current coverage reading

- **Boot chain:** covered (dashboard + triangle + ktest all pass the real firmware
  path).
- **Kernel surface:** covered by ktest (378 tests).
- **3D pipeline:** thin - one static triangle plus the dashboard's menu stream.
  No textured mesh, no mipmaps, no multistream/DMA vertex, no animated title.
  `nv097-coverage.md` puts the method gap at 108 handled / 93 recorded, with the
  immediate-mode vertex family (`0x1500-0x16FF`) and the raster modes as the
  largest recorded-only groups.
- **Texture formats / shader shapes:** whatever the dashboard's menu uses
  (modulate + a few lighting shapes); DXT/paletted/mipmapped/filtered sampling
  are unobserved. Nearest filtering only, in both renderers.
- **Audio:** emulated, but no streaming consumer and no host output.
- **Storage:** HDD boot + DVD/CHD boot; no per-title DVD streaming row.

## Sample wish list (cheapest first)

1. **nxdk toolchain on the host** (blocker: MSYS2 has no clang/make; C: has
   247 MB free so an install must target D:). Unblocks `nxdk-mesh` (textured
   3D), `nxdk-hello`, `nxdk-triangle`, `nxdk-led` - four rows from sources
   already in `data/test-samples/`.
2. **xgu examples** once nxdk exists (spinning cubes, textures).
3. **nxdk_vsh_tests** once nxdk exists (vertex-shader oracle data).
4. **OpenXDK framebuffer demos** (non-D3D surface path).

The two "blocked" rows are the cheapest breadth win: the toolchain turns four
existing source samples into four rows.
