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
