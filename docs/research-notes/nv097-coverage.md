# NV097 method coverage (xemu 0.8.139 vs XGrid)

Status: generated reference checklist, 2026-10-20; **refreshed 2026-10-24 for
G1** (xemu 0.8.139, the G1 recorded-method audit and the texture-format slice).
Companion to `gpu-reference-map.md`. **Not a gate** and not a
development-plan change.

## How to read this

- **Source:** `References2/xemu-0.8.139/hw/xbox/nv2a/pgraph/methods.h.inc`
  (204 entries, 201 unique names after merging the four
  `SET_TEXTURE_SET_BUMP_ENV_MAT` words). The 0.8.139 table is byte-identical to
  0.8.137, so the offsets below are unchanged.
- **`handled`** means an XGrid `src/XGrid.Nv2a/Nv2aMmio.cs` case or range
  applies the method to core state.
- **`recorded`** means **no core state sink exists**. The method still
  increments `MethodTotal` and is appended to the method stream when the cap
  allows, but the histogram is opt-in (`CollectMethodHistogram`) and the
  backend callback is opt-in (`ObserveMethods`), so "recorded" does not imply
  the GL backend saw it. "Recorded" is not automatically a bug:
  `WAIT_FOR_IDLE` is effectively a no-op on XGrid's synchronous pusher. It is a
  gap when a title actually depends on the state. Since 213/215 the fixed-function
  vertex stage (transform, texgen, skinning, texture matrix, color/material chain,
  lighting) has sinks behind the opt-in `--ffp`; the back-light context and the
  eye-vector methods do not.
- **`Count`** is the number of method offsets in a range/case group, not the
  number of xemu table entries.
- **Group** is a heuristic prefix label; the method name and offset are
  authoritative. A few entries sit in a neighbouring group because their
  names do not share the prefix.

## Method table

| Offset | Method | Kind | Count | Status | Group |
|---|---|---|---:|---|---|
| 0x0000 | `SET_OBJECT` | method | 1 | handled | Control |
| 0x0100 | `NO_OPERATION` | method | 1 | handled | Control |
| 0x0110 | `WAIT_FOR_IDLE` | method | 1 | recorded | Control |
| 0x0120 | `SET_FLIP_READ` | method | 1 | handled | Presentation |
| 0x0124 | `SET_FLIP_WRITE` | method | 1 | handled | Presentation |
| 0x0128 | `SET_FLIP_MODULO` | method | 1 | handled | Presentation |
| 0x012C | `FLIP_INCREMENT_WRITE` | method | 1 | handled | Presentation |
| 0x0130 | `FLIP_STALL` | method | 1 | handled | Presentation |
| 0x0180 | `SET_CONTEXT_DMA_NOTIFIES` | method | 1 | recorded | Context DMA |
| 0x0184 | `SET_CONTEXT_DMA_A` | method | 1 | recorded | Context DMA |
| 0x0188 | `SET_CONTEXT_DMA_B` | method | 1 | recorded | Context DMA |
| 0x0190 | `SET_CONTEXT_DMA_STATE` | method | 1 | recorded | Context DMA |
| 0x0194 | `SET_CONTEXT_DMA_COLOR` | method | 1 | handled | Surface / clear / present |
| 0x0198 | `SET_CONTEXT_DMA_ZETA` | method | 1 | handled | Surface / clear / present |
| 0x019C | `SET_CONTEXT_DMA_VERTEX_A` | method | 1 | recorded | Context DMA |
| 0x01A0 | `SET_CONTEXT_DMA_VERTEX_B` | method | 1 | recorded | Context DMA |
| 0x01A4 | `SET_CONTEXT_DMA_SEMAPHORE` | method | 1 | handled | Context DMA |
| 0x01A8 | `SET_CONTEXT_DMA_REPORT` | method | 1 | recorded | Context DMA |
| 0x0200 | `SET_SURFACE_CLIP_HORIZONTAL` | method | 1 | handled | Surface / clear / present |
| 0x0204 | `SET_SURFACE_CLIP_VERTICAL` | method | 1 | handled | Surface / clear / present |
| 0x0208 | `SET_SURFACE_FORMAT` | method | 1 | handled | Surface / clear / present |
| 0x020C | `SET_SURFACE_PITCH` | method | 1 | handled | Surface / clear / present |
| 0x0210 | `SET_SURFACE_COLOR_OFFSET` | method | 1 | handled | Surface / clear / present |
| 0x0214 | `SET_SURFACE_ZETA_OFFSET` | method | 1 | handled | Surface / clear / present |
| 0x0260 | `SET_COMBINER_ALPHA_ICW` | range | 8 | handled | Combiner / shader |
| 0x0288 | `SET_COMBINER_SPECULAR_FOG_CW0` | method | 1 | handled | Combiner / shader |
| 0x028C | `SET_COMBINER_SPECULAR_FOG_CW1` | method | 1 | handled | Combiner / shader |
| 0x0290 | `SET_CONTROL0` | method | 1 | handled | Raster state |
| 0x0294 | `SET_LIGHT_CONTROL` | method | 1 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x0298 | `SET_COLOR_MATERIAL` | method | 1 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x029C | `SET_FOG_MODE` | method | 1 | handled | Fog |
| 0x02A0 | `SET_FOG_GEN_MODE` | method | 1 | handled | Fog |
| 0x02A4 | `SET_FOG_ENABLE` | method | 1 | handled | Fog |
| 0x02A8 | `SET_FOG_COLOR` | method | 1 | handled | Fog |
| 0x02B4 | `SET_WINDOW_CLIP_TYPE` | method | 1 | recorded | Raster modes / queries |
| 0x02C0 | `SET_WINDOW_CLIP_HORIZONTAL` | range | 8 | recorded | Raster modes / queries |
| 0x02E0 | `SET_WINDOW_CLIP_VERTICAL` | range | 8 | recorded | Raster modes / queries |
| 0x0300 | `SET_ALPHA_TEST_ENABLE` | method | 1 | handled | Raster state |
| 0x0304 | `SET_BLEND_ENABLE` | method | 1 | handled | Raster state |
| 0x0308 | `SET_CULL_FACE_ENABLE` | method | 1 | handled | Raster modes / queries |
| 0x030C | `SET_DEPTH_TEST_ENABLE` | method | 1 | handled | Raster state |
| 0x0310 | `SET_DITHER_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0314 | `SET_LIGHTING_ENABLE` | method | 1 | handled | Other |
| 0x0318 | `SET_POINT_PARAMS_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x031C | `SET_POINT_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0320 | `SET_LINE_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0324 | `SET_POLY_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0328 | `SET_SKIN_MODE` | method | 1 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x032C | `SET_STENCIL_TEST_ENABLE` | method | 1 | handled | Raster state |
| 0x0330 | `SET_POLY_OFFSET_POINT_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0334 | `SET_POLY_OFFSET_LINE_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0338 | `SET_POLY_OFFSET_FILL_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x033C | `SET_ALPHA_FUNC` | method | 1 | handled | Raster state |
| 0x0340 | `SET_ALPHA_REF` | method | 1 | handled | Raster state |
| 0x0344 | `SET_BLEND_FUNC_SFACTOR` | method | 1 | handled | Raster state |
| 0x0348 | `SET_BLEND_FUNC_DFACTOR` | method | 1 | handled | Raster state |
| 0x034C | `SET_BLEND_COLOR` | method | 1 | handled | Raster state |
| 0x0350 | `SET_BLEND_EQUATION` | method | 1 | handled | Raster state |
| 0x0354 | `SET_DEPTH_FUNC` | method | 1 | handled | Raster state |
| 0x0358 | `SET_COLOR_MASK` | method | 1 | handled | Raster state |
| 0x035C | `SET_DEPTH_MASK` | method | 1 | handled | Raster state |
| 0x0360 | `SET_STENCIL_MASK` | method | 1 | handled | Raster state |
| 0x0364 | `SET_STENCIL_FUNC` | method | 1 | handled | Raster state |
| 0x0368 | `SET_STENCIL_FUNC_REF` | method | 1 | handled | Raster state |
| 0x036C | `SET_STENCIL_FUNC_MASK` | method | 1 | handled | Raster state |
| 0x0370 | `SET_STENCIL_OP_FAIL` | method | 1 | handled | Raster state |
| 0x0374 | `SET_STENCIL_OP_ZFAIL` | method | 1 | handled | Raster state |
| 0x0378 | `SET_STENCIL_OP_ZPASS` | method | 1 | handled | Raster state |
| 0x037C | `SET_SHADE_MODE` | method | 1 | recorded | Raster modes / queries |
| 0x0384 | `SET_POLYGON_OFFSET_SCALE_FACTOR` | method | 1 | recorded | Raster modes / queries |
| 0x0388 | `SET_POLYGON_OFFSET_BIAS` | method | 1 | recorded | Raster modes / queries |
| 0x038C | `SET_FRONT_POLYGON_MODE` | method | 1 | recorded | Other |
| 0x0390 | `SET_BACK_POLYGON_MODE` | method | 1 | recorded | Other |
| 0x0394 | `SET_CLIP_MIN` | method | 1 | handled | Raster state |
| 0x0398 | `SET_CLIP_MAX` | method | 1 | handled | Raster state |
| 0x039C | `SET_CULL_FACE` | method | 1 | handled | Raster modes / queries |
| 0x03A0 | `SET_FRONT_FACE` | method | 1 | handled | Raster modes / queries |
| 0x03A4 | `SET_NORMALIZATION_ENABLE` | method | 1 | handled | Vertex submission |
| 0x03A8 | `SET_MATERIAL_EMISSION` | range | 3 | handled | Fixed-function lighting (opt-in `--ffp`) |
| 0x03B4 | `SET_MATERIAL_ALPHA` | method | 1 | handled | Fixed-function lighting (opt-in `--ffp`) |
| 0x03B8 | `SET_SPECULAR_ENABLE` | method | 1 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x03BC | `SET_LIGHT_ENABLE_MASK` | method | 1 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x03C0 | `SET_TEXGEN_S` | case4 | 4 | handled | Texture / texgen |
| 0x03C4 | `SET_TEXGEN_T` | case4 | 4 | handled | Texture / texgen |
| 0x03C8 | `SET_TEXGEN_R` | case4 | 4 | handled | Texture / texgen |
| 0x03CC | `SET_TEXGEN_Q` | case4 | 4 | handled | Texture / texgen |
| 0x0420 | `SET_TEXTURE_MATRIX_ENABLE` | case4 | 4 | handled | Texture / texgen |
| 0x043C | `SET_POINT_SIZE` | method | 1 | recorded | Raster modes / queries |
| 0x0440 | `SET_PROJECTION_MATRIX` | range | 16 | handled | Other |
| 0x0480 | `SET_MODEL_VIEW_MATRIX` | range | 64 | handled | Other |
| 0x0580 | `SET_INVERSE_MODEL_VIEW_MATRIX` | range | 64 | handled | Other |
| 0x0680 | `SET_COMPOSITE_MATRIX` | range | 16 | handled | Other |
| 0x06C0 | `SET_TEXTURE_MATRIX` | range | 64 | handled | Texture / texgen |
| 0x0840 | `SET_TEXGEN_PLANE_S` | range | 64 | handled | Texture / texgen |
| 0x09C0 | `SET_FOG_PARAMS` | range | 3 | handled | Fog |
| 0x09CC | `SET_TEXGEN_VIEW_MODEL` | method | 1 | recorded | Texture / texgen |
| 0x09D0 | `SET_FOG_PLANE` | range | 4 | handled | Fog |
| 0x09E0 | `SET_SPECULAR_PARAMS` | range | 6 | handled | Fixed-function lighting (opt-in `--ffp`) |
| 0x09FC | `SET_PROVOKING_VERTEX` | method | 1 | recorded | Raster modes / queries |
| 0x0A10 | `SET_SCENE_AMBIENT_COLOR` | range | 3 | handled | Fixed-function lighting (opt-in `--ffp`) |
| 0x0A20 | `SET_VIEWPORT_OFFSET` | range | 4 | handled | Other |
| 0x0A30 | `SET_POINT_PARAMS` | range | 8 | recorded | Raster modes / queries |
| 0x0A50 | `SET_EYE_POSITION` | range | 4 | handled | Fixed-function vertex stage (opt-in --ffp) |
| 0x0A60 | `SET_COMBINER_FACTOR0` | range | 8 | handled | Combiner / shader |
| 0x0A80 | `SET_COMBINER_FACTOR1` | range | 8 | handled | Combiner / shader |
| 0x0AA0 | `SET_COMBINER_ALPHA_OCW` | range | 8 | handled | Combiner / shader |
| 0x0AC0 | `SET_COMBINER_COLOR_ICW` | range | 8 | handled | Combiner / shader |
| 0x0AE0 | `SET_COLOR_KEY_COLOR` | range | 4 | recorded | Raster modes / queries |
| 0x0AF0 | `SET_VIEWPORT_SCALE` | range | 4 | handled | Other |
| 0x0B00 | `SET_TRANSFORM_PROGRAM` | range | 32 | handled | Transform / VSH |
| 0x0B80 | `SET_TRANSFORM_CONSTANT` | range | 32 | handled | Transform / VSH |
| 0x0C00 | `SET_BACK_LIGHT_AMBIENT_COLOR` | range | 128 | recorded | Fixed-function lighting (recorded) |
| 0x1000 | `SET_LIGHT_AMBIENT_COLOR` | range | 256 | handled | Fixed-function lighting (opt-in `--ffp`) |
| 0x1500 | `SET_VERTEX3F` | range | 3 | recorded | Vertex submission |
| 0x1518 | `SET_VERTEX4F` | range | 4 | recorded | Vertex submission |
| 0x1530 | `SET_NORMAL3F` | range | 3 | recorded | Vertex submission |
| 0x1540 | `SET_NORMAL3S` | range | 3 | recorded | Vertex submission |
| 0x1550 | `SET_DIFFUSE_COLOR4F` | range | 4 | recorded | Vertex submission |
| 0x1560 | `SET_DIFFUSE_COLOR3F` | range | 3 | recorded | Vertex submission |
| 0x156C | `SET_DIFFUSE_COLOR4UB` | range | 1 | recorded | Vertex submission |
| 0x1570 | `SET_SPECULAR_COLOR4F` | range | 4 | recorded | Vertex submission |
| 0x1580 | `SET_SPECULAR_COLOR3F` | range | 3 | recorded | Vertex submission |
| 0x158C | `SET_SPECULAR_COLOR4UB` | range | 1 | recorded | Vertex submission |
| 0x1590 | `SET_TEXCOORD0_2F` | range | 2 | recorded | Vertex submission |
| 0x1598 | `SET_TEXCOORD0_2S` | range | 1 | recorded | Vertex submission |
| 0x15A0 | `SET_TEXCOORD0_4F` | range | 4 | recorded | Vertex submission |
| 0x15B0 | `SET_TEXCOORD0_4S` | range | 2 | recorded | Vertex submission |
| 0x15B8 | `SET_TEXCOORD1_2F` | range | 2 | recorded | Vertex submission |
| 0x15C0 | `SET_TEXCOORD1_2S` | range | 1 | recorded | Vertex submission |
| 0x15C8 | `SET_TEXCOORD1_4F` | range | 4 | recorded | Vertex submission |
| 0x15D8 | `SET_TEXCOORD1_4S` | range | 2 | recorded | Vertex submission |
| 0x15E0 | `SET_TEXCOORD2_2F` | range | 2 | recorded | Vertex submission |
| 0x15E8 | `SET_TEXCOORD2_2S` | range | 1 | recorded | Vertex submission |
| 0x15F0 | `SET_TEXCOORD2_4F` | range | 4 | recorded | Vertex submission |
| 0x1600 | `SET_TEXCOORD2_4S` | range | 2 | recorded | Vertex submission |
| 0x1608 | `SET_TEXCOORD3_2F` | range | 2 | recorded | Vertex submission |
| 0x1610 | `SET_TEXCOORD3_2S` | range | 1 | recorded | Vertex submission |
| 0x1620 | `SET_TEXCOORD3_4F` | range | 4 | recorded | Vertex submission |
| 0x1630 | `SET_TEXCOORD3_4S` | range | 2 | recorded | Vertex submission |
| 0x1698 | `SET_FOG_COORD` | method | 1 | handled | Fog |
| 0x169C | `SET_WEIGHT1F` | method | 1 | recorded | Vertex submission |
| 0x16A0 | `SET_WEIGHT2F` | range | 2 | recorded | Vertex submission |
| 0x16B0 | `SET_WEIGHT3F` | range | 3 | recorded | Vertex submission |
| 0x16C0 | `SET_WEIGHT4F` | range | 4 | recorded | Vertex submission |
| 0x1720 | `SET_VERTEX_DATA_ARRAY_OFFSET` | range | 16 | handled | Vertex submission |
| 0x1760 | `SET_VERTEX_DATA_ARRAY_FORMAT` | range | 16 | handled | Vertex submission |
| 0x17BC | `SET_LOGIC_OP_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x17C0 | `SET_LOGIC_OP` | method | 1 | recorded | Raster modes / queries |
| 0x17C8 | `CLEAR_REPORT_VALUE` | method | 1 | recorded | Raster modes / queries |
| 0x17CC | `SET_ZPASS_PIXEL_COUNT_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x17D0 | `GET_REPORT` | method | 1 | recorded | Raster modes / queries |
| 0x17E0 | `SET_EYE_DIRECTION` | range | 3 | recorded | Fixed-function lighting (recorded) |
| 0x17F8 | `SET_SHADER_CLIP_PLANE_MODE` | method | 1 | handled | Combiner / shader |
| 0x17FC | `SET_BEGIN_END` | method | 1 | handled | Vertex submission |
| 0x1800 | `ARRAY_ELEMENT16` | method | 1 | handled | Vertex submission |
| 0x1808 | `ARRAY_ELEMENT32` | method | 1 | handled | Vertex submission |
| 0x1810 | `DRAW_ARRAYS` | method | 1 | handled | Vertex submission |
| 0x1818 | `INLINE_ARRAY` | method | 1 | handled | Vertex submission |
| 0x181C | `SET_EYE_VECTOR` | range | 3 | recorded | Fixed-function lighting (recorded) |
| 0x1880 | `SET_VERTEX_DATA2F_M` | range | 32 | handled | Vertex submission |
| 0x1900 | `SET_VERTEX_DATA2S` | range | 16 | handled | Vertex submission |
| 0x1940 | `SET_VERTEX_DATA4UB` | range | 16 | handled | Vertex submission |
| 0x1980 | `SET_VERTEX_DATA4S_M` | range | 32 | handled | Vertex submission |
| 0x1A00 | `SET_VERTEX_DATA4F_M` | range | 64 | handled | Vertex submission |
| 0x1B00 | `SET_TEXTURE_OFFSET` | case4 | 4 | handled | Texture / texgen |
| 0x1B04 | `SET_TEXTURE_FORMAT` | case4 | 4 | handled | Texture / texgen |
| 0x1B08 | `SET_TEXTURE_ADDRESS` | case4 | 4 | handled | Texture / texgen |
| 0x1B0C | `SET_TEXTURE_CONTROL0` | case4 | 4 | handled | Texture / texgen |
| 0x1B10 | `SET_TEXTURE_CONTROL1` | case4 | 4 | handled | Texture / texgen |
| 0x1B14 | `SET_TEXTURE_FILTER` | case4 | 4 | handled | Texture / texgen |
| 0x1B1C | `SET_TEXTURE_IMAGE_RECT` | case4 | 4 | handled | Texture / texgen |
| 0x1B20 | `SET_TEXTURE_PALETTE` | case4 | 4 | handled | Texture / texgen |
| 0x1B24 | `SET_TEXTURE_BORDER_COLOR` | case4 | 4 | handled | Texture / texgen |
| 0x1B28 | `SET_TEXTURE_SET_BUMP_ENV_MAT` | case4+case4off | 16 | handled | Texture / texgen |
| 0x1B38 | `SET_TEXTURE_SET_BUMP_ENV_SCALE` | case4 | 4 | handled | Texture / texgen |
| 0x1B3C | `SET_TEXTURE_SET_BUMP_ENV_OFFSET` | case4 | 4 | handled | Texture / texgen |
| 0x1D6C | `SET_SEMAPHORE_OFFSET` | method | 1 | handled | Surface / clear / present |
| 0x1D70 | `BACK_END_WRITE_SEMAPHORE_RELEASE` | method | 1 | handled | Surface / clear / present |
| 0x1D78 | `SET_ZMIN_MAX_CONTROL` | method | 1 | handled | Raster state |
| 0x1D7C | `SET_ANTI_ALIASING_CONTROL` | method | 1 | recorded | Raster modes / queries |
| 0x1D8C | `SET_ZSTENCIL_CLEAR_VALUE` | method | 1 | handled | Surface / clear / present |
| 0x1D90 | `SET_COLOR_CLEAR_VALUE` | method | 1 | handled | Surface / clear / present |
| 0x1D94 | `CLEAR_SURFACE` | method | 1 | handled | Surface / clear / present |
| 0x1D98 | `SET_CLEAR_RECT_HORIZONTAL` | method | 1 | handled | Surface / clear / present |
| 0x1D9C | `SET_CLEAR_RECT_VERTICAL` | method | 1 | handled | Surface / clear / present |
| 0x1E20 | `SET_SPECULAR_FOG_FACTOR` | range | 2 | handled | Combiner / shader |
| 0x1E28 | `SET_SPECULAR_PARAMS_BACK` | range | 6 | recorded | Fixed-function lighting (recorded) |
| 0x1E40 | `SET_COMBINER_COLOR_OCW` | range | 8 | handled | Combiner / shader |
| 0x1E60 | `SET_COMBINER_CONTROL` | method | 1 | handled | Combiner / shader |
| 0x1E68 | `SET_SHADOW_ZSLOPE_THRESHOLD` | method | 1 | recorded | Transform misc / shadow |
| 0x1E6C | `SET_SHADOW_DEPTH_FUNC` | method | 1 | handled | Transform misc / shadow |
| 0x1E70 | `SET_SHADER_STAGE_PROGRAM` | method | 1 | handled | Combiner / shader |
| 0x1E74 | `SET_DOT_RGBMAPPING` | method | 1 | handled | Combiner / shader |
| 0x1E78 | `SET_SHADER_OTHER_STAGE_INPUT` | method | 1 | handled | Combiner / shader |
| 0x1E80 | `SET_TRANSFORM_DATA` | range | 4 | recorded | Transform / VSH |
| 0x1E90 | `LAUNCH_TRANSFORM_PROGRAM` | method | 1 | recorded | Transform misc / shadow |
| 0x1E94 | `SET_TRANSFORM_EXECUTION_MODE` | method | 1 | handled | Transform / VSH |
| 0x1E98 | `SET_TRANSFORM_PROGRAM_CXT_WRITE_EN` | method | 1 | handled | Transform / VSH |
| 0x1E9C | `SET_TRANSFORM_PROGRAM_LOAD` | method | 1 | handled | Transform / VSH |
| 0x1EA0 | `SET_TRANSFORM_PROGRAM_START` | method | 1 | handled | Transform / VSH |
| 0x1EA4 | `SET_TRANSFORM_CONSTANT_LOAD` | method | 1 | handled | Transform / VSH |

## Summary by group

| Group | Entries | Handled | Recorded |
|---|---:|---:|---:|
| Combiner / shader | 14 | 14 | 0 |
| Context DMA | 8 | 1 | 7 |
| Control | 3 | 2 | 1 |
| Fixed-function lighting | 9 | 5 | 4 |
| Fixed-function vertex stage (opt-in --ffp) | 6 | 6 | 0 |
| Fog | 7 | 7 | 0 |
| Other | 9 | 7 | 2 |
| Presentation | 5 | 5 | 0 |
| Raster modes / queries | 27 | 3 | 24 |
| Raster state | 24 | 24 | 0 |
| Surface / clear / present | 15 | 15 | 0 |
| Texture / texgen | 20 | 19 | 1 |
| Transform / VSH | 8 | 7 | 1 |
| Transform misc / shadow | 3 | 1 | 2 |
| Vertex submission | 43 | 13 | 30 |

## Feature-level gaps (not visible as method entries)

These are state values or execution paths that the method table cannot show.
They are the items a commercial title is most likely to hit first.

| Area | State / method | XGrid status | Impact |
|---|---|---|---|
| **DXT textures** | `SET_TEXTURE_FORMAT` color `0x0C`/`0x0E`/`0x0F` | **Decoded 2026-10-24 (G1)**: `S3tcDecoder` (DXT1/3/5 block decode, differential against xemu `s3tc.c`) and the sampler's compressed block lane; `TextureFormatBytesPerTexel` reports 4 | The gate media bind no DXT format (format histogram), so the decode is pinned by the reference vectors and a synthetic draw |
| **Paletted textures** | `SET_TEXTURE_PALETTE` + `SZ_I8_A8R8G8B8` (`0x0B`) | **Decoded 2026-10-24 (G1)**: palette state sink (snapshot v10) and the sampler's index+palette lane | No measured row binds 0x0B; synthetic test only |
| **Mipmaps** | `SET_TEXTURE_FORMAT` MIPMAP_LEVELS, `SET_TEXTURE_CONTROL0` MAX_LOD | **Chain layout decoded 2026-10-24 (G1)**: `TextureFormats.LevelLength/LevelOffset/ChainLength` (shared with the cube face stride). Sampling remains level 0: no per-pixel LOD yet (G2 GL renderer / a software LOD slice) | Distant textures alias/shimmer; some titles depend on mip chains |
| **Texture filtering** | `SET_TEXTURE_FILTER` | **Closed 2026-10-21 (§209/§219), opt-in `--tex-filter registers`**: tent at LOD 0 + the effective PROJECT2D convolution, in the software sampler and the GL shader | The gate titles sample at texel centres; the corpus rows with fractional texcoords gain colour depth |
| **Multistream / vertex DMA** | `SET_CONTEXT_DMA_VERTEX_A/B`, `SET_CONTEXT_DMA_A/B` | Recorded only; vertex fetch reads guest arrays directly | Titles using multi-stream vertex buffers will not fetch correctly |
| **Immediate-mode vertices** | `SET_VERTEX3F`/`SET_VERTEX4F`, `SET_NORMAL*`, `SET_DIFFUSE*`, `SET_SPECULAR_COLOR*`, `SET_TEXCOORD*`, `SET_WEIGHT*` (`0x1500`-`0x16FF`) | Recorded only; XGrid's inline path handles `SET_VERTEX_DATA*` (`0x1880`-`0x1AFC`) | Titles using immediate-mode draws produce no/partial geometry |
| **Window clip / scissor** | `SET_WINDOW_CLIP_TYPE`, `SET_WINDOW_CLIP_HORIZONTAL/VERTICAL` | Recorded only | UI panels, split-screen and viewport sub-rects draw outside their rect |
| **Backface culling** | `SET_CULL_FACE_ENABLE`, `SET_CULL_FACE`, `SET_FRONT_FACE` | **Closed 2026-10-20 (§206g)** — state latched, screen-space winding test in both fill paths | Was extra overdraw + see-through backfaces |
| **Flat shading** | `SET_SHADE_MODE`, `SET_PROVOKING_VERTEX` | Recorded only | Per-vertex colors interpolate where the title expects flat |
| **Polygon offset** | `SET_POLY_OFFSET_*`, `SET_POLYGON_OFFSET_*` | Recorded only | Z-fighting on decals/shadows |
| **Point/line/wireframe** | `SET_POINT_*`, `SET_LINE_SMOOTH_ENABLE`, `SET_FRONT/BACK_POLYGON_MODE`, `SET_POLY_SMOOTH_ENABLE` | Recorded only | Only filled triangles are rasterized |
| **Logic op / dither / AA** | `SET_LOGIC_OP*`, `SET_DITHER_ENABLE`, `SET_ANTI_ALIASING_CONTROL` | Recorded only | Mostly cosmetic; logic op can be functional for some effects |
| **Color key** | `SET_COLOR_KEY_COLOR` | Recorded only | Color-keyed transparency fails |
| **Occlusion query** | `SET_ZPASS_PIXEL_COUNT_ENABLE`, `GET_REPORT`, `CLEAR_REPORT_VALUE` | Recorded only | Titles that gate effects/visibility on the report stall or draw everything |
| **XVSS path** | `SET_TRANSFORM_DATA`, `LAUNCH_TRANSFORM_PROGRAM` | Recorded only; the programmable VSH path is implemented | Fixed-function/XVSS titles do not get vertex processing |
| **Fixed-function lighting/material/texgen** | `SET_LIGHT_*`, `SET_MATERIAL_*`, `SET_TEXGEN_*`, `SET_TEXTURE_MATRIX_ENABLE`, `SET_EYE_*`, `SET_SPECULAR_PARAMS*`, `SET_SCENE_AMBIENT_COLOR` | **Closed 2026-10-21 (213 + 215), opt-in `--ffp`**: the transform/texgen/skinning/texture-matrix/fog/color half, the material chain and the full enabled-light loop (infinite/local/spot, attenuation, cone, reconstructed specular power). The back-light color context and the eye-direction/eye-vector methods stay recorded (two-sided lighting is a reference TODO) | No measured title enables a light (`fixedFnLights=0` on every gate and corpus row); a lit draw with an enabled light now runs the light loop instead of the unlit pass-through |
| **Render-to-texture** | surface/texture aliasing | Partial: the presented surface and RAM-backed textures work; general RTT feedback is not a first-class path | Titles that render to a texture and then sample it may show stale/black results |

## Stored but not consumed

These methods have a core state sink (so they are `handled` in the table)
but the value is not fully honored by the current renderer:

- `SET_TEXTURE_FILTER` - consumed under `--tex-filter registers` (tent at LOD 0
  plus the effective PROJECT2D convolution, §209/§219); the default remains the
  legacy nearest sampler so every gate anchor is byte-identical.
- `SET_TEXTURE_FORMAT` - the MIPMAP_LEVELS field is stored, but sampling is
  level 0; the color-format field now covers the reference's 66-entry table
  (2026-10-24 G1): the original `0x02`-`0x07`, `0x10`-`0x12`, `0x1C`-`0x1E`,
  the byte-order variants `0x3A`-`0x3C` / `0x3F`-`0x41`, the 8-bit Y8/AY8/A8
  family, A8Y8/Y16/R6G5B5, the two YUV formats and the DXT1/3/5 block formats.
  Cube maps still accept only `0x05`/`0x06`/`0x3A`-`0x3C` (no measured row binds
  another cube format).
- `SET_TEXTURE_CONTROL0` — the enable bit is used; the LOD/MAX_LOD bits
  affect only the cube face-stride calculation, not mip selection.
- `SET_TRANSFORM_PROGRAM_CXT_WRITE_EN` — stored, not consumed.
- `SET_TRANSFORM_EXECUTION_MODE` — used only for the
  `NonProgrammableModeDraws` diagnostic; draws still use the programmable
  path.
- `SET_SURFACE_FORMAT` — color/zeta/type fields are used; anti-aliasing
  fields are not.
- `SET_CONTROL0` — the Z-format bit is used; the rest is not.
- `SET_SHADOW_DEPTH_FUNC` — used; `SET_SHADOW_ZSLOPE_THRESHOLD` is recorded
  only.

## G1 audit: the 73 recorded methods (2026-10-24)

The G1 acceptance is "the 73 `recorded` methods are either handled or explicitly
declined with a measured reason and a corpus/histogram citation". This is that
audit. The list is the 73 entries the group summary below marks `recorded`
(the caveat's older "108 handled / 93 recorded" line was a miscount; the table
is 128 handled / 73 recorded).

**Probe (reproducible).** `--pgraph` windows on the gate media:
`triangle.xbe` 300M steps and the dashboard 1B steps, both fast
(`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`), with
`--pgraph-max 3000000` / `4000000`. The counts below are **writes observed in
that window** (a 0 means the method was not issued, not that the title never
issues it); the parameter column names the distinct values observed. The same
probe backs `usage-histogram.md` "Recorded-method parameters".

**Result: 1 handled, 72 declined.** `SET_TEXTURE_PALETTE` now has a state sink
(`_texturePalette`, snapshot v10) and a consumer (the paletted I8 sampler lane);
the rest are declined with the measurement below. A decline is a deliberate
compatibility choice, not a claim the method is irrelevant to every title.

### Control and context DMA (8)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x0110 | `WAIT_FOR_IDLE` | 21 / 185 | Every write is 0; XGrid's pusher is synchronous and the reference's handler is empty. No-op by contract. |
| 0x0180 | `SET_CONTEXT_DMA_NOTIFIES` | 2 / 4 | Handles 2/7 only. Notifies are resolved through the semaphore/notification path XGrid already implements; no row's frame or counter depends on the handle. |
| 0x0184 | `SET_CONTEXT_DMA_A` | 2 / 5 | Handles 3 (and 0x19 in the menu). XGrid addresses textures by the guest's absolute offset; every gate/corpus frame matches with the handle ignored. The RAMHT/DMA-context object is a G2-scale seam item. |
| 0x0188 | `SET_CONTEXT_DMA_B` | 2 / 5 | Handles 3/0x0B (0x19 in the menu); same as A. |
| 0x0190 | `SET_CONTEXT_DMA_STATE` | 1 / 3 | Handle 4 (0x19 in the menu); the state context is not read by the measured streams. |
| 0x019C | `SET_CONTEXT_DMA_VERTEX_A` | 2 / 4 | Handles 3/0x11; the vertex fetch reads the guest arrays at their absolute offsets, and the 1B frames are exact. |
| 0x01A0 | `SET_CONTEXT_DMA_VERTEX_B` | 1 / 2 | Handle 3; no multi-stream vertex buffer in any measured row. |
| 0x01A8 | `SET_CONTEXT_DMA_REPORT` | 1 / 2 | Handle 0x0C; no row reads a report (see the query group below). |

### Window clip (3 entries, 17 offsets)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x02B4 | `SET_WINDOW_CLIP_TYPE` | 61 / 113 | Every write is 0 (the inside/outside mode bit is clear). |
| 0x02C0 | `SET_WINDOW_CLIP_HORIZONTAL` | 61 / 113 | Slot 0 only; X 0..1279 (`04FF0000`), 0..639 (`027F0000`), 0..255 (`00FF0000`), 0..1023 (`03FF0000`). Every rect starts at 0 and matches a bound surface's width, so a scissor cannot narrow the gate frames. xemu applies the eight rects per fragment; the software fill is 253M px/1B, so a general per-fragment test is a G2 (GL `clipRegion[8]`) item, not a G1 sink. |
| 0x02E0 | `SET_WINDOW_CLIP_VERTICAL` | 62 / 115 | Y 0..479 (`01DF0000`), 0..255 (`00FF0000`), 0..1023 (`03FF0000`); same full-surface result. |

### Raster modes / queries (21; the three window-clip entries are above)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x0310 | `SET_DITHER_ENABLE` | 35 / 208 | 18 zeros / 17 ones (triangle) and 172 ones (menu). xemu's GL path never enables `GL_DITHER` (the hardware dither is a 16-bit-output effect) and the software oracle does not dither; honoring it would move the menu frame by construction with no oracle. |
| 0x0318 | `SET_POINT_PARAMS_ENABLE` | 40 / 75 | All 0. Point rasterization is not implemented; the enable is off. |
| 0x031C | `SET_POINT_SMOOTH_ENABLE` | 40 / 75 | All 0. |
| 0x0320 | `SET_LINE_SMOOTH_ENABLE` | 35 / 1851 | All 0. |
| 0x0324 | `SET_POLY_SMOOTH_ENABLE` | 35 / 1851 | All 0. |
| 0x0330 | `SET_POLY_OFFSET_POINT_ENABLE` | 36 / 72 | All 0. |
| 0x0334 | `SET_POLY_OFFSET_LINE_ENABLE` | 36 / 72 | All 0. |
| 0x0338 | `SET_POLY_OFFSET_FILL_ENABLE` | 36 / 72 | All 0; the scale/bias writes below therefore have no consumer. |
| 0x037C | `SET_SHADE_MODE` | 1 / 2 | `0x1D01` = `V_SMOOTH`; XGrid already interpolates, so the value agrees with the implemented behavior. A `V_FLAT` sink has no measured user. |
| 0x0384 | `SET_POLYGON_OFFSET_SCALE_FACTOR` | 36 / 72 | `0x80000000` (-0.0f) / 0; the fill enable is 0, so there is no depth bias to apply. |
| 0x0388 | `SET_POLYGON_OFFSET_BIAS` | 36 / 72 | Same as the scale factor. |
| 0x043C | `SET_POINT_SIZE` | 40 / 75 | 8, but point params are disabled and only filled triangles are rasterized. |
| 0x09FC | `SET_PROVOKING_VERTEX` | 1 / 2 | 1, and only flat shading reads it; `SET_SHADE_MODE` is SMOOTH. |
| 0x0A30 | `SET_POINT_PARAMS` | 0 / 0 | Not issued; point params disabled. |
| 0x0AE0 | `SET_COLOR_KEY_COLOR` | 4 / 8 | All 0; NV097 has no color-key enable in the stream and xemu's GL path does not color-key. |
| 0x17BC | `SET_LOGIC_OP_ENABLE` | 35 / 70 | All 0. |
| 0x17C0 | `SET_LOGIC_OP` | 0 / 0 | Not issued; the enable is 0. |
| 0x17C8 | `CLEAR_REPORT_VALUE` | 0 / 0 | Not issued; no row uses the report. |
| 0x17CC | `SET_ZPASS_PIXEL_COUNT_ENABLE` | 0 / 0 | Not issued; no occlusion query in any row. |
| 0x17D0 | `GET_REPORT` | 0 / 0 | Not issued; no row stalls on a report. |
| 0x1D7C | `SET_ANTI_ALIASING_CONTROL` | 36 / 74 | `0xFFFF0001` (69 in the menu) / 0. Every `SET_SURFACE_FORMAT` in the same windows has AA field 0 (`0x128`/`0x228`), so the AA factor is 1; the reference applies it only when the surface AA mode is set. |

### Immediate-mode vertices and weights (30 entries, ~75 offsets)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x1500 | `SET_VERTEX3F` | 0 / 0 | The measured rows submit vertices through the array path (`SET_VERTEX_DATA*`, `ARRAY_ELEMENT16/32`, `DRAW_ARRAYS`, `INLINE_ARRAY`), which is handled. No immediate-mode vertex write appears in either window. |
| 0x1518 | `SET_VERTEX4F` | 0 / 0 | Same. |
| 0x1530 | `SET_NORMAL3F` | 0 / 0 | Same. |
| 0x1540 | `SET_NORMAL3S` | 0 / 0 | Same. |
| 0x1550 | `SET_DIFFUSE_COLOR4F` | 0 / 0 | Same. |
| 0x1560 | `SET_DIFFUSE_COLOR3F` | 0 / 0 | Same. |
| 0x156C | `SET_DIFFUSE_COLOR4UB` | 0 / 0 | Same. |
| 0x1570 | `SET_SPECULAR_COLOR4F` | 0 / 0 | Same. |
| 0x1580 | `SET_SPECULAR_COLOR3F` | 0 / 0 | Same. |
| 0x158C | `SET_SPECULAR_COLOR4UB` | 0 / 0 | Same. |
| 0x1590 | `SET_TEXCOORD0_2F` | 0 / 0 | Same. |
| 0x1598 | `SET_TEXCOORD0_2S` | 0 / 0 | Same. |
| 0x15A0 | `SET_TEXCOORD0_4F` | 0 / 0 | Same. |
| 0x15B0 | `SET_TEXCOORD0_4S` | 0 / 0 | Same. |
| 0x15B8 | `SET_TEXCOORD1_2F` | 0 / 0 | Same. |
| 0x15C0 | `SET_TEXCOORD1_2S` | 0 / 0 | Same. |
| 0x15C8 | `SET_TEXCOORD1_4F` | 0 / 0 | Same. |
| 0x15D8 | `SET_TEXCOORD1_4S` | 0 / 0 | Same. |
| 0x15E0 | `SET_TEXCOORD2_2F` | 0 / 0 | Same. |
| 0x15E8 | `SET_TEXCOORD2_2S` | 0 / 0 | Same. |
| 0x15F0 | `SET_TEXCOORD2_4F` | 0 / 0 | Same. |
| 0x1600 | `SET_TEXCOORD2_4S` | 0 / 0 | Same. |
| 0x1608 | `SET_TEXCOORD3_2F` | 0 / 0 | Same. |
| 0x1610 | `SET_TEXCOORD3_2S` | 0 / 0 | Same. |
| 0x1620 | `SET_TEXCOORD3_4F` | 0 / 0 | Same. |
| 0x1630 | `SET_TEXCOORD3_4S` | 0 / 0 | Same. |
| 0x169C | `SET_WEIGHT1F` | 0 / 0 | Same; the programmable VSH path computes its own weights from the transform constants. |
| 0x16A0 | `SET_WEIGHT2F` | 0 / 0 | Same. |
| 0x16B0 | `SET_WEIGHT3F` | 0 / 0 | Same. |
| 0x16C0 | `SET_WEIGHT4F` | 0 / 0 | Same. |

### Texture, transform and shadow (9)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x09CC | `SET_TEXGEN_VIEW_MODEL` | 0 / 0 | Not issued; the fixed-function texgen modes the rows use are the object/eye/camera modes. |
| 0x1B20 | `SET_TEXTURE_PALETTE` | 0 / 0 | **Handled (G1).** State sink `_texturePalette` (snapshot v10), consumed by the paletted I8 sampler lane; the decode is pinned by `Nv2aTextureFormatTests`. No measured row binds 0x0B, so the synthetic test is the evidence. |
| 0x0C00 | `SET_BACK_LIGHT_AMBIENT_COLOR` | 0 / 0 | Not issued; two-sided lighting is a reference TODO and no row enables a light (`fixedFnLights=0` on every gate/corpus row). |
| 0x17E0 | `SET_EYE_DIRECTION` | 0 / 0 | Not issued; same. |
| 0x181C | `SET_EYE_VECTOR` | 108 / 108 | Every write is 0; same two-sided-lighting consumer. |
| 0x1E28 | `SET_SPECULAR_PARAMS_BACK` | 0 / 0 | Not issued; same. |
| 0x1E68 | `SET_SHADOW_ZSLOPE_THRESHOLD` | 1 / 2 | `0x7F800000` (+inf); no row binds a depth texture (the format histogram has no 0x2C/0x2E/0x2F/0x30/0x31), so the slope threshold has no consumer. |
| 0x1E80 | `SET_TRANSFORM_DATA` | 0 / 0 | Not issued; the XVSS path (`LAUNCH_TRANSFORM_PROGRAM` 0/0) is not used, and the programmable VSH path is what the rows execute. |
| 0x1E90 | `LAUNCH_TRANSFORM_PROGRAM` | 0 / 0 | Not issued; same. |

### The remaining two (polygon mode)

| Offset | Method | Writes tri/menu | Measured reason |
|---|---|---:|---|
| 0x038C | `SET_FRONT_POLYGON_MODE` | 71 / 2061 | `0x1B02` = `V_FILL`; the rasterizer already fills triangles, so the value agrees with the implemented behavior. |
| 0x0390 | `SET_BACK_POLYGON_MODE` | 71 / 2061 | Same. |

**What this leaves for a title.** The largest genuine compatibility items are
window clip (a G2 GL `clipRegion[8]` port), the context-DMA map (a seam item),
and the immediate-mode vertex path (a G1 follow-up only if a target title
issues it). None of them is exercised by the gate media; the measured reason is
the probe above, not an assumption.

## Regenerating this table

The table is a snapshot of:

- `References2/xemu-0.8.139/hw/xbox/nv2a/pgraph/methods.h.inc` (entries),
- `References2/xemu-0.8.139/hw/xbox/nv2a/nv2a_regs.h` (offsets),
- `src/XGrid.Nv2a/Nv2aMmio.cs` (constants, `case` labels, `if` ranges).

To refresh it, extract the `DEF_METHOD*` entries and `NV097_*` defines, build
the offset set from the XGrid `Method*` constants plus the
`method >= MethodX && method <= MethodY` ranges, and compare. The classification
is deliberately mechanical: a method is `handled` if any offset in its
range/case group has an XGrid sink; it does not prove the sink is semantically
complete.

## Caveats

- Verified against the 2026-10-24 G1 tree: `Nv2aMmio.cs` has 146 `Method*`
  constants, 72 exact `case` sinks and 34 range conditions; the table is
  **129 handled / 72 recorded** (the palette sink moved one row). The G1 audit
  above covers the 73 entries that were `recorded` when the task started.
  Regenerate only if a later change adds or removes a state sink.
- The method table is from **xemu 0.8.139** (byte-identical to 0.8.137); the
  Playtest fork has five newer NV097 methods and a tuned backend, so it can
  differ.
- `handled` is not a fidelity claim. The XGrid test suite and the 1B goldens
  are the fidelity gates; this checklist is a gap finder.
- The 30 immediate-mode entries (~75 method offsets) are the largest single
  block; if a target title uses `SET_VERTEX3F`, that is the first
  implementation slice.
