# NV097 usage histogram (measured, 2026-10-20)

Purpose: the fidelity steering signal the plan requires — *which* NV097 methods a
workload actually issues, and therefore which "recorded" entries in
`nv097-coverage.md` are worth implementing. Instance of the recurring rule: **no
NV2A fidelity work without a row or histogram that proves usage.**

## Method

`--pgraph <file>` writes the pushed method stream as `<subchannel> <method hex>
<parameter hex>`, capped by `MethodStreamCap` (default 262,144 entries; raise it
with `--pgraph-max`). Group the second column and count. Everything below is
reproducible from the commands given.

Both probes used the H1 reference host (see `docs/hosts.md`).

## Probe A — `triangle.xbe` (gate sample, 300M steps, fast)

```powershell
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot `
  --bios data\xemu-0.8.136-windows-x86_64\Complex_4627.bin `
  --mcpx data\xemu-0.8.136-windows-x86_64\mcpx_1.0.bin `
  --eeprom data\xemu-0.8.136-windows-x86_64\eeprom.bin `
  --dvd wt-data\xemu-tri\triangle.iso --steps 300000000 `
  --jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw `
  --pgraph wt-data\tri-pg.txt
```

173,846 methods, **443 distinct** offsets.

## Probe B — Halo: Combat Evolved (USA) v1.09 (benchmark-only, 600M steps, fast)

```powershell
# same firmware flags, then:
  --dvd "H:\Tests\XBOXTest\Halo - Combat Evolved (USA) (v1.09) (Redump).iso" `
  --steps 600000000 --jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw `
  --pgraph wt-data\halo-pgraph.txt --pgraph-max 3000000
```

188,435 methods, **447 distinct** offsets. Booted to a presented 640×480 frame
(35 flips, 33.4M textured pixels, 2,639 draws), 79 distinct colours — it renders
*something*, it is not gameplay.

## Top of the stream (both workloads agree)

| Method | Name | Halo | Triangle |
|---|---|---:|---:|
| 0x1800 | `ARRAY_ELEMENT16` | 58,577 | 58,577 |
| 0x1EA4 | `SET_TRANSFORM_CONSTANT_LOAD` | 8,309 | ~8,237 |
| 0xB80..0xBBC | transform constants | ~28,000 | ~28,000 |
| 0x17FC | `SET_BEGIN_END` | 5,368 | — |
| 0x1BC0..0x1BDC | texture slot 3 state | 6,504 | — |
| 0xB00..0xB3C | transform program words | ~4,000 | — |

**Interpretation:** the stream is dominated by the *programmable* path — vertex
arrays, transform constants, transform programs. That is the path XGrid already
implements. Whatever separates XGrid from a title is **not** the draw-path shape;
it is ignored state and texture formats.

## Recorded (no core sink) methods that the workloads actually use

Counts are Halo / triangle over their windows:

| Method | Name | Halo | Tri | Status after §206g |
|---|---|---:|---:|---|
| 0x0308 | `SET_CULL_FACE_ENABLE` | 144 | 72 | **handled (§206g)** |
| 0x039C | `SET_CULL_FACE` | 105 | 52 | **handled (§206g)** |
| 0x03A0 | `SET_FRONT_FACE` | 2 | 1 | **handled (§206g)** |
| 0x02B4 | `SET_WINDOW_CLIP_TYPE` | 113 | 61 | recorded |
| 0x02C0 | `SET_WINDOW_CLIP_HORIZONTAL` | 113 | 61 | recorded |
| 0x02E0 | `SET_WINDOW_CLIP_VERTICAL` | 113 | 61 | recorded |
| 0x0294 | `SET_LIGHT_CONTROL` | 109 | — | recorded |
| 0x0314 | `SET_LIGHTING_ENABLE` | 109 | — | recorded |
| 0x03B8 | `SET_SPECULAR_ENABLE` | 109 | — | recorded |
| 0x0390 | `SET_BACK_POLYGON_MODE` | 142 | — | recorded |
| 0x038C | `SET_FRONT_POLYGON_MODE` | 142 | — | recorded |
| 0x0384 | `SET_POLYGON_OFFSET_SCALE_FACTOR` | 72 | 36 | recorded |
| 0x0388 | `SET_POLYGON_OFFSET_BIAS` | 72 | 36 | recorded |
| 0x03C0..0x03CC | `SET_TEXGEN_S/T/R/Q` | 72 | — | recorded |
| 0x0310 | `SET_DITHER_ENABLE` | 70 | 35 | recorded |
| 0x17BC | `SET_LOGIC_OP_ENABLE` | 70 | 35 | recorded |
| 0x0318..0x031C | point params / smooth | 75 | — | recorded |
| 0x0320 | `SET_LINE_SMOOTH_ENABLE` | 70 | — | recorded |
| 0x043C | `SET_POINT_SIZE` | 75 | — | recorded |
| 0x1D7C | `SET_ANTI_ALIASING_CONTROL` | 72 | — | recorded |
| 0x0330..0x0338 | poly-offset point/line/fill enable | 72 | — | recorded |

## Recorded-method parameters (G1 audit, 2026-10-24)

The G1 audit needed the *values* the gate titles program into the recorded
methods, not just the counts. Repro: the same `--pgraph` windows as Probe A and
the 1B dashboard (`--pgraph-max 3000000` / `4000000`), then group the third
column. The full per-method decision table is `nv097-coverage.md` "G1 audit".

| Method | Triangle 300M | Menu 1B | Reading |
|---|---|---|---|
| `WAIT_FOR_IDLE` | 21 x `00000000` | 185 x `00000000` | No-op. |
| `SET_CONTEXT_DMA_*` | handles 2/3/4/7/0x0B/0x11/0x0C | same plus 0x19 | No non-zero context base in any frame. |
| `SET_WINDOW_CLIP_TYPE` | 61 x 0 | 113 x 0 | Inside mode, all surfaces. |
| `SET_WINDOW_CLIP_HORIZONTAL` | `04FF0000` (0..1279) x37, `027F0000` (0..639) x17, `00FF0000` (0..255) x6, `03FF0000` x1 | `04FF0000` x71, `027F0000` x34, `00FF0000` x6, `03FF0000` x1, `02800000` x1 | Every rect starts at 0 and covers a bound surface; a scissor would not narrow the gate frames. |
| `SET_WINDOW_CLIP_VERTICAL` | `01DF0000` (0..479) x54, `00FF0000` x6, `03FF0000` x1, `00000003` x1 | `01DF0000` x105, `00FF0000` x6, `03FF0000` x1, `00000003` x2, `01E00000` x1 | Same. |
| `SET_DITHER_ENABLE` | 18 x 0, 17 x 1 | 172 x 1, 36 x 0 | xemu's GL path ignores dither; the menu would move by construction. |
| `SET_POINT_PARAMS_ENABLE` / `POINT_SMOOTH` | 40 x 0 | 75 x 0 | Points off. |
| `SET_LINE_SMOOTH` / `POLY_SMOOTH` | 35 x 0 | 1851 x 0 | Off. |
| `SET_POLY_OFFSET_*_ENABLE` | 36 x 0 | 72 x 0 | Off. |
| `SET_SHADE_MODE` | `00001D01` (SMOOTH) | `00001D01` | Already interpolated. |
| `SET_FRONT/BACK_POLYGON_MODE` | `00001B02` (FILL) x71 | `00001B02` x2061 | Already filled. |
| `SET_PROVOKING_VERTEX` | 1 | 1 | Only flat shading reads it. |
| `SET_POLYGON_OFFSET_SCALE/BIAS` | `80000000` (-0.0f) x35 / x35 | `80000000` x70 | Enables are 0. |
| `SET_ANTI_ALIASING_CONTROL` | `FFFF0001` x35 | `FFFF0001` x69 | Surface AA field is 0 (`0x128`/`0x228`), so the factor is 1. |
| `SET_EYE_VECTOR` | 108 x 0 | 108 x 0 | Two-sided lighting unused. |
| `SET_SHADOW_ZSLOPE_THRESHOLD` | 1 x `7F800000` (+inf) | 2 x `7F800000` | No depth texture bound. |
| `SET_SURFACE_FORMAT` | `00000128` x37, `08080228` x12, `08080128` x6, `0A0A0228` x2, `0A0A0128` x1 | same | AA = 0 in every word. |
| `SET_TEXTURE_PALETTE` | 0 | 0 | Handled in G1; no measured user. |

Immediate-mode vertices, weights, the report/query methods, the XVSS methods and
the back-light/eye-direction context are all **0 writes** in both windows.

## What this buys

1. **Culling was the highest-count genuine gap**, and the gate sample uses it —
   landed, see decisions §206g (41,035 triangles culled per 1B, triangle pixels
   −35.3%, every frame byte-identical).
2. **Window clip (113/61) is the next candidate** with the same shape: state that
   only narrows what the rasterizer touches, so the expected outcome is fewer
   pixels and an unchanged frame.
3. **Lighting/specular/texgen counts are misleading**: the D3D path normally
   replaces them with driver-generated vertex programs, which is why the
   transform-constant traffic is ~28k. Do not implement fixed-function lighting
   on the strength of these counts alone.
4. **Nothing in the top of either stream is unimplemented draw-path work.** Further
   compatibility work should start from texture formats and the state list above,
   not from the pushbuffer.

## Caveats

- Halo is a **benchmark-only** probe (`docs/development-direction.md` §4 Corpus):
  it may inform priorities, it may not become a gate or a fidelity target.
- The 58,577 `ARRAY_ELEMENT16` figure is window-dependent; raise
  `--pgraph-max` and the step count for a longer sample.
- Counts measure *issued* methods, not *respected* ones: a method can be counted
  while the title never depends on its effect.

## Texture formats actually sampled (2026-10-20)

The `NV2A tex` diagnostic prints `formats=<id>=<attempts>/<misses>` per boot, so
texture-format usage is measured rather than assumed. Sampled from the corpus:

| workload | formats sampled (attempts / misses) |
|---|---|
| `triangle.xbe` (gate) | `06`=22,642,488/0, `12`=10,444,800/0, `1E`=306,732/0 |
| `mesh` | `06`, `12` (mipmapped 3D, no misses) |
| `xgu vp_lighting` | `06`=11,363,999/0, `12`=10,444,800/0, **`3F`=3,420,787/3,420,787 -> 0** |
| `xgu bump_cubemap_reflect` | `06`, `12`, **`3A`=5,709,991/0** (cube map; was 896k mode fallbacks) |
| `xgu bumpenv_ripple` | `06`, `12`, `3A`=9,673,664/0, **`28`=3,917,732/3,917,732 -> 0** (G8B8 derivative map) |
| `xgu swizzle_texture` | `06`, `12`, `3A`=3,135,398/0 |

**`0x3F` was the whole ballgame.** Every one of its 3.42M samples missed and the
count matched the app's fragment total exactly, which is what identified the
3-colour frame as a texture-format gap rather than a geometry or shader gap. It
is `LU_IMAGE_A8B8G8R8` - a byte-order permutation of A8R8G8B8 that the 0.8.137
header does not define (the newer fork does, with `0x40`/`0x41` and the swizzled
twins `0x3A`-`0x3C`). Fixed in decisions §207h; the frame went from 3 colours to
1,592 with full coverage.

**`0x28` (`SZ_G8B8`) was the second one** (§209g): 3.92M samples, all missing,
on `xgu bumpenv_ripple`. Decoded like the reference (`GL_RG8` + the
`(R, G, R, G)` swizzle) together with its `0x17` LU twin and the `0x29`
(`R8B8`, `(G, R, R, G)`) sibling; misses went to zero. The sample still renders
black, so the format was necessary but not sufficient (compatibility gap 10).

**Reading the counters.** `attempts/misses` is the useful pair: attempts tell you
whether a format matters at all, misses tell you whether the sampler can serve
it. A format with 0 attempts is irrelevant however exotic it looks; a format with
attempts == misses is a guaranteed-broken draw path.

**What this says about priorities.** The two 32-bit formats the gates and `mesh`
use (`06`, `12`) were already handled, so three of the four workloads above never
touched the gap. The corpus earned its keep exactly here: a title using an
uncommon channel order would have rendered untextured with no other symptom.

## TEXFILTER usage: the sampler's filter modes (2026-10-21)

The `NV2A tex` line now also prints `filters=<min>/<mag>/<conv>` per slot (MIN in
bits 16-23, MAG in bits 24-27, convolution kernel in bits 13-15; 1 = nearest,
2 = linear/tent, conv 1 = quincunx). Measured on the same legs:

| workload | filters (slot 0,1,2,3) | texel samples/1B attributed to stage 0 |
|---|---|---|
| dashboard menu (1B, fast) | `2/2/1, 2/2/1, 1/1/1, 1/1/1` | 211M of 267M (`0x12`) |
| `triangle.xbe` (1B) | `2/2/1` x4 (boot-animation state) | 11.4M (`0x06`) |
| `xgu bump_cubemap_reflect` (3.6B) | `2/2/1` x4 | 5.7M (`0x3A`) + a 5.7M-sample cube |

So the product path's dominant texture stage asks for **tent filtering plus a
5x5-equivalent quincunx blur**, and XGrid ignored both until §209d (the sampler
was nearest-only on every stage). The feature landed opt-in
(`--tex-filter registers`) because the menu has no external oracle: honoring
the register changes every textured pixel of the gate frame. The histogram is
the evidence that it matters; the corpus row is the only oracle for whether it
is right.


## Sampler address modes and border colours (2026-10-21, §219)

The same `--pgraph` window answers a second question the GL sampler needed:
*which* NV097_SET_TEXTURE_ADDRESS / TEXFILTER / BORDER_COLOR words the gate
title programs. Repro: 1B fast menu, `--pgraph wt-data\probe-menu-pgraph.txt
--pgraph-max 8000000` (8,000,000-method window), then count column 3 for the
texture-slot offsets.

| slot | SET_TEXTURE_ADDRESS words (count) | resolved U/V |
|---|---|---|
| 0 | `00010303` (4,265), `00010404` (35), `00010101` (12), `00030303` (1) | clamp/clamp; border/border; wrap/wrap |
| 1 | `00010101` (3,950), `00030303` (81) | **wrap/wrap** dominant |
| 2 | `00010101` (3,950), `00030303` (81) | wrap/wrap dominant |
| 3 | `00010101` (3,950), `00030404` (80), `00030303` (1) | wrap/wrap; **border/border** |

| | words |
|---|---|
| TEXFILTER slot 0 | `01012000` (3,949 nearest), `02022000` (364 tent) |
| TEXFILTER slots 1-3 | `02022000` / `01012000` mixtures (tent + a kernel field next to an ordinary MIN, so the effective kernel is 0 - §210a) |
| BORDER_COLOR | slot 0 `FF000000` (34), slot 3 `FFFFFFFF` (6) |

**Why this mattered.** The raw address word assigns **1 to wrap and 2 to
mirror**, and the GL backend was reading the raw word: its wrap/mirror were
swapped and mode 4 (border) fell into the clamp branch. The gate frames could
not show it because their coordinates stay inside [0,1), but slot 1 (3,950
writes) is `wrap`, slot 3 is `border`, and any draw that steps outside the
texture would have sampled a different texel than the software rasterizer.
The seam now carries the *decoded* mode (0 wrap, 1 mirror, 2 clamp, 3 border)
plus the border colour, resolved once in `Nv2AMmio`, so the two executors
cannot disagree (§219).
