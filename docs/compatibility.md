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
  (xemu 0.8.137 method table vs `Nv2aMmio`). Reference orientation:
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
| **EvoX dashboard** (`xboxdash.xbe` on HDD, softmod) | `data/xemu-0.8.136-windows-x86_64` boot media; XBE 770,048 B, entry `0x12A17` | yes - MCPX ROM -> 1BL -> 2BL -> kernel -> XBE | **yes**, step 50M (boot animation), menu content from flip 33 | colored menu (32,789 colors; 7,389 RGB-diff px vs neutral) | APU/ACI emulated, **no host sink** | submenu state-100 pixel-negative; NV2A method coverage partial (see nv097 coverage); 686 flips/1B | RAM `660CA11B` / CPU `F5D61C94`; last frame `4EF1FBF6`; gl `6BE6325F` | `-Sample dashboard` |
| **triangle.xbe** (nxdk `f998bdc`) | XboxDev ghidra-xbe `tests/xbefiles/triangle.xbe` | yes | **yes**, step 32M | colored Gouraud triangle, 68 frames / 34 stalls | n/a | none on this stream; CPU-bound leg | RAM `F57934BF` / CPU `CF79FC17`; accurate frame `A1740DE0`; gl `3FC4B419` | `-Sample triangle` |
| **triangle.xbe, GL replace** | same XBE | yes | yes, step 32M | GPU rasterized, `BackendUnsupported=0` | n/a | target <= 6 s not met (6.4 s projected, CPU-bound) | CPU `CF79FC17`; frame `3FC4B419` | `-Sample triangle -Gpu gl -GpuReplace` |
| **kernel-test-suite-default.xbe** | Cxbx-Reloaded release `20260819191914-da80f1f` | yes | n/a (console output; `disable-video=1`) | n/a | n/a | 378/378 execute; known deltas 319 `RtlWalkFrameChain`, 344 `XcVerifyPKCS1Signature`; xemu leg cannot bind its monitor socket on this host | XGrid leg **792,800,535** steps, suite log 40,797 B byte-identical; smoke 7/7 rows green | `-Sample ktest` (needs the staged ISO) / `scripts/ktest-smoke.ps1 -SkipXemu` |
| **dashboard, 2B-step animation probe** | same HDD + gate pad script | yes | yes, 4,867 flips | same menu content (239 colors, same means); pixel-heavy 1.64B px | n/a | A-press does not reach a submenu; the submenu state-100 gap is unfixed | 2B run: 217,622 draws / `5.8 msps`; last frame `535C48FA` | `-Sample dashboard -Steps 2000000000` |
| **nxdk-mesh** (textured mesh, VS/PS shaders) | source in `data/test-samples/nxdk-mesh/` | **blocked** - nxdk toolchain not on host (MSYS2 base only: no clang/make; C: 247 MB free, must install on D:) | - | expected textured 3D + shaders | - | build dependency, not an emulator gap | - | - |
| **nxdk-hello / nxdk-triangle / nxdk-led** | source in `data/test-samples/` | **blocked** - toolchain | - | text / triangle / SMC LED | - | build dependency | - | - |
| **xgu examples** (spinning cubes, textures) | https://github.com/Voxel9/xbox-xgu-examples | **blocked** - needs nxdk | - | textured 3D | - | fetch + toolchain | - | - |
| **nxdk_vsh_tests** (VS goldens) | https://github.com/abaire/nxdk_vsh_tests | **blocked** - needs nxdk | - | vertex-shader oracle data | - | fetch + compare, not a boot row | - | - |
| **OpenXDK framebuffer demos** | xbdev.net / OpenXDK | **blocked** - toolchain | - | raw framebuffer | - | candidate for non-D3D bring-up | - | - |

## Measured rows (2026-10-20, twelfth session, loaded host)

Recorded with `scripts/boot-to-first-frame.ps1 -All` at 1B steps (ktest at 600M);
the host runs ~1.6-1.9x slower than the quiet-host baselines, so compare rows
within a session, not across. These are the post-§205 rows (draw-command
batching): the GL walls moved by ~1% here, while the same-binary A/B measured
`drawMs` -47% and wall -4.2% on the §205 commit.

| Row | wall | msps | frames | first frame | last frame | SHAs (ram/cpu) |
|---|---:|---:|---:|---|---|---|
| triangle, `sw` | 20.7 s | 48.3 | 68 | step 32M, `6F780A33` | `A1740DE0` | `777F4DEB` / `7DAB63EB` |
| triangle, `gl+replace` | 10.5 s | 94.8 | 68 | step 32M, `6F780A33` | `3FC4B419` | `8AE82781` / `7DAB63EB` |
| dashboard, `sw` | 62.0 s | 16.1 | 686 | step 50M, `6F780A33` | `4EF1FBF6` | `36B72FE1` / `F5D61C94` |
| dashboard, `gl+replace` | 44.6 s | 22.4 | 686 | step 50M, `6F780A33` | `6BE6325F` | `CF151863` / `F5D61C94` |
| ktest (600M, `disable-video=1`), `sw` | 14.9 s | 40.3 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `4D4849FA` / `76002239` |
| ktest (600M, `disable-video=1`), `gl+replace` | 10.0 s | 59.8 | 34 (boot animation only) | step 36M, `6F780A33` | `18C7CC83` | `42C9C960` / `76002239` |

Reading: the first presented frame is the boot animation (`6F780A33`) in every
row - a boot-chain anchor, not a title anchor. The title anchors are the
last-frame SHAs, and the GL rows match their gate goldens
(`3FC4B419`/`6BE6325F`), which is what makes these rows comparable to the gates.
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
