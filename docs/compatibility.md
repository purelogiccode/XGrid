# XGrid compatibility matrix (Track E)

Status: **rows re-measured 2026-10-21 after §208 (display model).** Every row now
carries the product-path frame (`displaySha` = the CRTC scanout) next to the
flipped-surface gate anchor, and the triangle's fast anchor is the gate media
(HDD+DVD) after the §208d correction. Earlier: first measured rows 2026-10-20,
refreshed after §205 (draw-command
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

`displaySha` is the frame the **product** path shows (CLI `--frame-display`, the
Avalonia frontend): the display controller's scanout, decoded from the VGA CRTC
since §208. `lastFrameSha` is the last **flipped** 3D surface and stays the gate
anchor; on a double-buffered title the two differ by one buffer
(`surface - pcrtc = 0x12C000`, one 640x480x4 page). For every row below
`displaySha == scanoutSha` - the model reproduces the raw scanout exactly.

| Sample | Provenance | Boot | First frame | Render | Audio | Known gaps | Anchor (1B fast) | Command |
|---|---|---|---|---|---|---|---|---|
| **EvoX dashboard** (`xboxdash.xbe` on HDD, softmod) | `data/xemu-0.8.136-windows-x86_64` boot media; XBE 770,048 B, entry `0x12A17` | yes - MCPX ROM -> 1BL -> 2BL -> kernel -> XBE | **yes**, step 50M (boot animation), menu content from flip 33 | colored menu (32,789 colors; 7,389 RGB-diff px vs neutral) | APU/ACI emulated, **no host sink** | submenu state-100 pixel-negative; NV2A method coverage partial (see nv097 coverage); 686 flips/1B | RAM `51B56687` / CPU `F5D61C94`; last frame `4EF1FBF6`; gl `B6116DAE` (policy) | `-Sample dashboard` |
| **triangle.xbe** (nxdk `f998bdc`) | XboxDev ghidra-xbe `tests/xbefiles/triangle.xbe` | yes | **yes**, step 32M | colored Gouraud triangle, 68 frames / 34 stalls | n/a | none on this stream; CPU-bound leg | RAM `B45254CA` / CPU `7DAB63EB` (HDD+DVD, §208d); accurate frame `A1740DE0`; display `AB50EF68`; gl `7A860ED7` (policy) | `-Sample triangle` |
| **triangle.xbe, GL replace** | same XBE | yes | yes, step 32M | GPU rasterized, `BackendUnsupported=0`; 1B `gl+replace` **7.5 s** vs 11.3 s sw (gate media) | n/a | the old "<= 6 s target" was measured on a DVD-only leg that never launched the XBE (§208d); re-base before chasing | CPU `7DAB63EB`; frame `7A860ED7` (policy) | `-Sample triangle -Gpu gl -GpuReplace` |
| **kernel-test-suite-default.xbe** | Cxbx-Reloaded release `20260819191914-da80f1f` | yes | n/a (console output; `disable-video=1`) | n/a | n/a | 378/378 execute; known deltas 319 `RtlWalkFrameChain`, 344 `XcVerifyPKCS1Signature`; xemu leg cannot bind its monitor socket on this host | XGrid leg **792,800,535** steps, suite log 40,797 B byte-identical; smoke 7/7 rows green | `-Sample ktest` (needs the staged ISO) / `scripts/ktest-smoke.ps1 -SkipXemu` |
| **dashboard, 2B-step animation probe** | same HDD + gate pad script | yes | yes, 4,867 flips | same menu content (239 colors, same means); pixel-heavy 1.64B px | n/a | A-press does not reach a submenu; the submenu state-100 gap is unfixed | 2B run: 217,622 draws / `5.8 msps`; last frame `535C48FA` | `-Sample dashboard -Steps 2000000000` |
| **nxdk `mesh`** (textured, lit 3D) | nxdk `a6c31ee` `samples/mesh`, built by `scripts/build-nxdk-samples.ps1` | yes | **yes**, step 40M | 66 flips / 2,864 draws / 42.6M px; 5,115 clears, 44 programs, 48,627 triangles culled | n/a | renders its own content — the only corpus sample that does | RAM `D6A41BAC` / CPU `6DA19D89`; last frame `806CBEA7`, scanout/display `7328A99F` | `-Sample mesh` |
| **nxdk `gamma`** | nxdk `a6c31ee` `samples/gamma` | yes | **no new flip**: writes the scanout buffer directly (`XVideoGetFB`) and never flips | 0 extra NV2A draws (34 boot flips) | n/a | output visible only through the scanout export (768 colours, 298,744 px — the gradient); the flipped frame is a single black colour | RAM `4EE18F78` / CPU `C3371A8C`; scanout `D3EBF420`; **display `D3EBF420`** | `-Sample gamma` |
| **nxdk `gamma-fade`** | nxdk `a6c31ee` `samples/gamma-fade` | yes | same as `gamma` | 0 extra draws | n/a | same | RAM `ED83DC23` / CPU `D0A7BB8A`; scanout `2BC1C443`; **display `2BC1C443`** | `-Sample gamma-fade` |
| **nxdk `hello`** | nxdk `a6c31ee` `samples/hello` | yes | same (`debugPrint` writes text into the framebuffer) | 0 extra draws | n/a | text visible only through the scanout export (2 colours, 302 px) | RAM `4650454F` / CPU `C3371A8C`; scanout `72D33DC2`; **display `72D33DC2`** (the text) | `-Sample hello` |
| **nxdk `led`** | nxdk `a6c31ee` `samples/led` | yes | no video output at all (toggles the SMC LED) | 0 extra draws | n/a | scanout equals the flipped frame — the correct negative control | RAM `3D1F72DB` / CPU `1C98838F`; scanout `18C7CC83`; **display `18C7CC83`** | `-Sample led` |
| **nxdk `xaudio`** | nxdk `a6c31ee` `samples/xaudio` | yes | same as `gamma` (it is an audio sample) | 0 extra draws; **APU live**: 135 reads / 203 writes, `voiceOff=1`, ISTS bit 4 | **exercised**, but no host sink - nothing audible to verify | audio correctness unverifiable until a PCM sink exists | RAM `B0C863C5` / CPU `07DB79D2`; scanout `8A969D86` | `-Sample xaudio` |
| **xgu `vp_lighting`** (vertex-program lighting) | Voxel9/xbox-xgu-examples, built against nxdk `a6c31ee` | yes | **yes**, step 80M; its own scene from ~3B steps | 90 flips / 2,728 draws / **1,592 colours**, full coverage | n/a | needs 3B steps (1B only reaches the boot animation); **output matches the reference screenshot** (gap 5) | RAM `162DAFF1` / CPU `0380C502`; last frame `72B2F490` | `-Sample xgu-vp_lighting -Steps 3000000000` |
| **xgu `alpha_blending`** | same | yes | yes, own scene from ~3B | 2,962 draws at 3B; **renders correctly** - within 4.8/255 of the reference at 4.6B, mid-fade at 3B (the shader transparency ping-pongs every 100 frames) | n/a | none on the render path; the oracle needs a phase sweep (§208f) | RAM `40081D03` / CPU `1C5C575D` at 3B; **oracle anchor 4.6B** (mean 25.6/21.2/229.0 vs 20.8/17.2/233.2) | `-Sample xgu-alpha_blending -Steps 4600000000` |
| **xgu `bump_cubemap_reflect`** | same | yes | flips yes, own scene at 3B | **renders the reflection**: 2,679 draws / 42.8M px / full coverage / 21,041 colours / 0% black | n/a | none on the render path (gap 3 closed 2026-10-21); the environment-map hatch at grazing phases is the content, not a bug - compare at the right phase | RAM `DDB59317` / CPU `D5D38C64`; last frame `DA0750EE`, scanout/display `668D7A48` at 3B; oracle anchor 3.6B | `-Sample xgu-bump_cubemap_reflect -Steps 3000000000` |
| **xgu `bumpenv_ripple`** (programmable bump env) | xgu repo, built 2026-10-21 | yes | own scene from ~5B (217 flips) | 3,038 draws / 9.7M depth px; 41,401 culled | n/a | oracle delta (21.0, 19.0, 9.7) at 5B - animate/fog, needs a phase sweep | RAM `7A11CBEF` / CPU `5B0665B2` at 5B; last frame `18C7CC83` | `-Sample xgu-bumpenv_ripple -Steps 5000000000` |
| **xgu `swizzle_texture`** (swizzled 2D texture) | xgu repo, built 2026-10-21 | yes | flips yes, own scene at 3B | 87 flips / 2,725 draws / 45.1M px / 1,620 colours, full coverage; samples `0x3A` 3.14M with 0 misses | n/a | no reference screenshot in the xgu repo, so no oracle row; exercises the swizzled byte-order path (the §207h/§209 formats) | RAM `E2EB41ED` / CPU `B887B3C6`; last frame `FE32FB3C`, scanout/display `869CB402` at 3B | `-Sample xgu-swizzle_texture -Steps 3000000000` |
| **xgu `distance_fog_ffp`** (fixed-function fog) | xgu repo, built 2026-10-21 | yes | flips yes, scene **flat orange** | 2,730 draws, **26 colours**, 112,259 culled, `programLoad=12` | n/a | **`XGU_FIXED` transform is not implemented** - see gap 6 | RAM `AFF08085` / CPU `571363B4` at 3B | `-Sample xgu-distance_fog_ffp -Steps 3000000000` |
| **xgu `spheremap_ffp`** (fixed-function sphere map) | xgu repo, built 2026-10-21 | yes | flips yes, scene **flat** | 2,728 draws, 93,003 culled, `programLoad=12` | n/a | same FFP gap 6 | RAM `B5747761` / CPU `F0075C2F` at 3B | `-Sample xgu-spheremap_ffp -Steps 3000000000` |
| **xgu examples** (spinning cubes, textures) | https://github.com/Voxel9/xbox-xgu-examples | **blocked** - needs the examples fetched (toolchain now available) | - | textured 3D | - | fetch + build | - | - |
| **nxdk_vsh_tests** (VS goldens) | https://github.com/abaire/nxdk_vsh_tests | **blocked** - needs the repo fetched | - | vertex-shader oracle data | - | fetch + compare, not a boot row | - | - |
| **OpenXDK framebuffer demos** | xbdev.net / OpenXDK | **blocked** - needs the demos fetched | - | raw framebuffer | - | would exercise the direct-framebuffer gap above | - | - |

### 6. The fixed-function transform pipeline is not implemented (2026-10-21)

`xgu distance_fog_ffp` and `xgu spheremap_ffp` call
`xgu_set_transform_execution_mode(p, XGU_FIXED, ...)` and upload MVP matrices to
the fixed-function block instead of loading a vertex program. XGrid only runs
programmable vertex programs, so those draws produce nothing: the frames are
flat backgrounds (26 colours vs the reference's 12,764; 93k-112k triangles
culled) with `programLoad=12` (the boot animation only).

This is a **new, large, well-scoped gap** found by widening the corpus (three
more xgu rows, built here). It is not a small fidelity item: it needs the
fixed-function vertex stage (matrix/lighting state, `SET_TRANSFORM_EXECUTION_MODE
= FIXED`, the per-stage texture coordinate generation that goes with it). No
XGrid feature covers it today; the sw rasterizer and the GL backend both sit
downstream of the vertex stage, so both are affected identically.

## Known gaps surfaced by the corpus (2026-10-20)

These are measured, not suspected — each one was found by running a sample, not
by reading code.

### 1. Direct-framebuffer output is not what `--frame`/the frontend present - **CLOSED 2026-10-21 (§208)**

> Fixed by the display model: the VGA CRTC register file behind `NV_PRMCIO` is
> decoded (pitch/width/height/depth), `TryCopyDisplayFrame` reads PCRTC START
> with that geometry, the CLI exports it with `--frame-display`, and the Avalonia
> frontend samples it at ~30 Hz (so a guest that never flips still updates the
> picture). `--frame` stays the last flipped 3D surface - the gate anchor - and
> is one frame ahead of the display for a double-buffered title. Details and the
> evidence are in decisions §208. The original finding follows.

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

**Resolved (2026-10-21, §208).** The product path now uses the display model:
`--frame-display` exports it, the Avalonia frontend samples it at ~30 Hz, and
`--frame` deliberately keeps exporting the last flipped surface so every gate
frame anchor stays valid. The model decodes the VGA CRTC the guest programs
through `NV_PRMCIO` (pitch `(CR13 | CR19<<3 | CR25<<6) << 3`, size from CR01/
CR12/CR07, depth from CR28 + PRAMDAC general control), and it reproduces the
old scanout bytes exactly on six legs.

### 2. The repo's `nxdk-mesh` snapshot could never build

`data/test-samples/nxdk-mesh/` was missing `verts.h` (49 KB) and `texture.h`
(1.7 MB), both `#include`d by its own `main.c`, plus the `screenshot.png` its
README links. The corpus now builds from nxdk upstream (`samples/<name>` at
pinned `a6c31ee`) via `scripts/build-nxdk-samples.ps1`, so the stale snapshots
are no longer on the build path. `data/` is gitignored, which is why the gap
survived: nothing tracked the sample sources.

### 3. Cubemap reflection rendered flat (`xgu bump_cubemap_reflect`) - **CLOSED 2026-10-21 (§209)**

This sample samples texture format **`0x3A`** (`SZ_A8B8G8R8`) for a cube map.
`IsSupportedSwizzledFormat` mirrored the reference renderer and accepted only
`0x05`/`0x06`, so all 896,067 samples counted a texture-mode fallback and the
stage contributed nothing: the frame was a solid green shape on black.

**The four gates (208g found three, §209a found the dominant one):**

1. `IsSupportedSwizzledFormat` - widened to the SZ twins `0x3A`-`0x3C`.
2. `SamplerPlan.CubeBytes` - knows they are 4-byte texels.
3. `TryDecodeSwizzledTexel` - now shares `DecodeTextureTexel` instead of
   hard-coding `0x05`/`0x06`, so the cube path cannot disagree with the 2D
   sampler about channel order.
4. **The face extent came from `TEXIMAGERECT`, but xemu reads a swizzled
   texture's extent from TEXFMT `BASE_SIZE_U/V`.** The guest never programs the
   rect, so the stale 512x512 default made the sampler walk 1 MB face strides
   over 256 KB faces. This was the real cause of the "striped reflection" that
   208g blamed on the direction chain.

Result at 3B: **2 colours -> 21,041 colours, 36% black -> 0% black**, mean
(58.0, 98.7, 29.8) against the reference (62.3, 100.1, 28.8); at 3.6B
`|dmean|` is (5.3, 2.4, 1.3). The direction chain was verified faithful with a
probe set (forced normal, normal-map dump, `TEX1`/`n`/`rv` vector dumps - all
smooth), so no math changed. Evidence and the phase table: decisions §209.


### 4. Byte-order texture formats were unsupported (fixed 2026-10-20)

`xgu vp_lighting` sampled format **`0x3F`** (`LU_IMAGE_A8B8G8R8`, defined in the
newer xemu fork but not in 0.8.137) 3,420,787 times and **every sample missed**,
so its geometry rendered untextured. Formats `0x3A`-`0x3C` and `0x3F`-`0x41` are
now decoded as the same 32-bit words with permuted channels. Result on the same
run: `3F` misses `3,420,787 -> 0`, textured pixels `+3.42M`, and the frame went
from **3 colours / 250,090 px** to **1,592 colours / 307,200 px**. Both gate
frames stayed byte-identical (`A1740DE0`, `4EF1FBF6`), because the gates use
`0x06`/`0x07`/`0x12`/`0x1E`.

### 5. Reference-screenshot oracle (xgu examples)

The xgu repo ships 640x480 screenshots of each example, which is an external
oracle the corpus did not have before. Compared as mean channel value plus an
8-bucket luminance histogram (2-pixel stride):

| sample | reference mean (R,G,B) | XGrid mean | verdict |
|---|---|---|---|
| `vp_lighting` | (32.3, 26.7, 224.7) | (33.9, 28.1, 226.4) | **matches** - within 1.7/255 per channel, luminance histogram close (`83/81` in the darkest non-black bucket) |
| `alpha_blending` | (20.8, 17.2, 233.2) | (25.6, 21.2, 229.0) at 4.6B | **matches** - within 4.8/255 per channel once the frame is taken at a comparable phase. The sample ping-pongs a shader `transparency` 1.0 -> 0.0 every 100 frames, so a single fixed step count measures the phase, not the renderer (decisions §208f; the earlier "blended geometry missing" verdict compared a mid-fade frame) |
| `bump_cubemap_reflect` | (62.3, 100.1, 28.8) | (58.0, 98.7, 29.8) at 3B; (57.0, 97.7, 30.2) at 3.6B | **matches** - within 5.3/255 per channel at the best phase, 21,041 colours vs the reference's 30,806, 0% black (was 2 colours / 36% black before §209a) |

This is the first corpus evidence that can say "renders correctly" rather than
just "renders something". **Animated samples need a phase sweep** (§208f):
`scripts/frame-oracle.py <reference.png> <frame.bmp>...` scores several step
counts and reports the best. With that, all three oracle rows match; the two
`*_ffp` rows are blocked on compatibility gap 6 (fixed-function transforms).

### 7. Texture filtering was ignored (opt-in since 2026-10-21, §209d)

The sampler read no `TEXFILTER` state: every stage was nearest, and the
`QUINCUNX`/`GAUSSIAN` convolution kernel was skipped. Measured usage: the
dashboard menu's stage 0/1 program `2/2/1` (tent + quincunx) and stage 0 alone
accounts for 211M of its 267M texel samples per 1B steps; `bump_cubemap_reflect`
programs `2/2/1` on all four slots.

`--tex-filter registers` now honors MIN/MAG as a tent filter at LOD 0 (per-tap
wrap, face-local clamp on cube maps) and runs xemu's nine-tap 1-2-1 gaussian on
PROJECT2D. It is **opt-in** because the menu has no external oracle and honoring
the register changes 8.7% of its menu pixels (mean |delta| 2.3/2.1/2.0 per
channel, colours 32,360 -> 39,254) while costing 2.4x wall on the software
rasterizer (compiled fills fall back to the generic loop; a perf follow-up).
The default path is byte-identical to the previous build (both 1B goldens PASS
on their exact anchors). The GL backend is still nearest-only.

### 8. GL replace mode cannot compose a software-fallback draw (2026-10-21, §209f)

`--gpu gl --gpu-replace` sends every draw to the GL backend and falls back to
the software rasterizer when the backend declines a shape. The fallback is
correct, but its pixels go to **guest RAM** while the presented image is the
**GL FBO**, and the two are never merged: the flip serves the FBO as-is
(`presentUploads=0`, `flipStaleUploads=0`), so a frame with any declined draw
loses *all* fallback pixels.

Measured on `xgu bump_cubemap_reflect` at 3B (`-Gpu gl -GpuReplace`):
`BackendUnsupported=14` (`unsup[modes=14]` - the dot-product/reflection
texture modes), 784,684 software-fallback pixels, and the presented frame is
the **clear colour only** (`3553B0DF…` vs the software frame `DA0750EE…`;
CPU SHA identical). The gate scripts assert `BackendUnsupported=0`, so the
gate legs never enter this state; the corpus row now documents it.

Fixing it needs a per-frame composition policy (all-or-nothing: drop the FBO
and re-render the frame in software, or upload the fallback region into the
FBO), not a one-line patch. Not started - the GL backend is not the product
path.

### 9. A second-emulator frame oracle is staged, not yet captured (§209f)

The corpus's only external oracle is the xgu reference screenshots. xemu could
be a second one (any sample, any phase), and the pieces that work are recorded
in `scripts/xemu-screendump.ps1`: QMP over **stdio** answers correctly
(greeting, `qmp_capabilities`, `cont`, `query-status`), the BIOS must be passed
with `-bios`, the DVD with `-dvd_path` (xemu builds the drive itself), and
`-display xemu` is the only display backend in this build. `screendump` has
not produced a file yet (the redirected-stdio read loop is the open problem);
xemu's own **F12** screenshot (`SaveScreenshot` into
`general.screenshot_dir`) is the alternative route.


## Measured rows (2026-10-20, refreshed after §206g/§207)

> **Anchor note (updated 2026-10-21, §208d):** a fast-mode **RAM** SHA is only
> meaningful with its exact flag set. The triangle at 1B fast reads
> `DD1D516E` (DVD only, boot animation), `B45254CA` (HDD+DVD, plain run) or
> `0117A5E2` (HDD+DVD + `--frame-first`, which caps batches while hunting the
> first flip). CPU SHA (`7DAB63EB` on HDD+DVD) and every counter are portable.
> The `BTFF` rows below always pass `--frame-first`, so compare rows within this
> table.

Recorded with `scripts/boot-to-first-frame.ps1 -All` at 1B steps (ktest at 600M).
Rows re-measured **2026-10-21** on the display-model build (§208): the `display`
column is the product path, and it equals the flipped anchor on the GL legs by
design (the backend owns the presented image) and lags it by one buffer on the
double-buffered software legs.

**Software frames are exact anchors; GL frames are policy-validated** (diff
against the software frame: flat regions byte-exact, `±1 px` edge band, per-channel
tolerance ≤ 2). The exact GL SHA moved with the cull landing
(`6BE6325F -> B6116DAE` dashboard, `3FC4B419 -> 7A860ED7` triangle) while the
software frame stayed byte-identical and both GL gates still report
`violations=0 maxDelta=1` — that is the documented contract, not a regression.

| Row | wall | msps | frames | first frame | last frame (flip) | display | SHAs (ram/cpu) |
|---|---:|---:|---:|---|---|---|---|
| triangle, `sw` | 12.1 s | 82.7 | 68 | step 32M, `6F780A33` | `A1740DE0` | `AB50EF68` | `0117A5E2` / `7DAB63EB` |
| triangle, `gl+replace` | 7.2 s | 139.5 | 68 | step 32M, `6F780A33` | `7A860ED7` (policy) | `7A860ED7` | `8AE82781` / `7DAB63EB` |
| dashboard, `sw` | 44.0 s | 22.7 | 686 | step 50M, `6F780A33` | `4EF1FBF6` | `51658B3D` | `94A6D53C` / `F5D61C94` |
| dashboard, `gl+replace` | 29.9 s | 33.5 | 686 | step 50M, `6F780A33` | `B6116DAE` (policy) | `B6116DAE` | `CF151863` / `F5D61C94` |
| ktest (600M, `disable-video=1`), `sw` | 8.6 s | 69.7 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `18C7CC83` | `4573E131` / `76002239` |
| ktest (600M, `disable-video=1`), `gl+replace` | 7.3 s | 82.0 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `18C7CC83` | `71C0FD0F` / `76002239` |

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
  (modulate + a few lighting shapes) plus the xgu samples' byte-order formats,
  a cube map and a normal-map bump chain; DXT/paletted/mipmapped sampling are
  unobserved. **Filtering:** the software sampler honors MIN/MAG + convolution
  behind `--tex-filter registers` (default off, gap 7); the GL backend is
  nearest-only.
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
