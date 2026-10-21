# NV097 method coverage (xemu 0.8.137 vs XGrid)

Status: generated reference checklist, 2026-10-20. Companion to
`gpu-reference-map.md`. **Not a gate** and not a development-plan change.

## How to read this

- **Source:** `References2/xemu-0.8.137/hw/xbox/nv2a/pgraph/methods.h.inc`
  (204 entries, 201 unique names after merging the four
  `SET_TEXTURE_SET_BUMP_ENV_MAT` words).
- **`handled`** means an XGrid `src/XGrid.Nv2a/Nv2aMmio.cs` case or range
  applies the method to core state.
- **`recorded`** means **no core state sink exists**. The method still
  increments `MethodTotal` and is appended to the method stream when the cap
  allows, but the histogram is opt-in (`CollectMethodHistogram`) and the
  backend callback is opt-in (`ObserveMethods`), so "recorded" does not imply
  the GL backend saw it. "Recorded" is not automatically a bug:
  fixed-function lighting/material/texgen state is normally replaced by
  driver-generated vertex programs on the Xbox D3D path, and `WAIT_FOR_IDLE`
  is effectively a no-op on XGrid's synchronous pusher. It is a gap when a
  title actually depends on the state.
- **`Count`** is the number of method offsets in a range/case group, not the
  number of xemu table entries.
- The table is a **method-presence** view. A handled method can still be
  partially honored (see "Stored but not consumed" below).
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
| 0x0294 | `SET_LIGHT_CONTROL` | method | 1 | recorded | Fixed-function lighting (recorded) |
| 0x0298 | `SET_COLOR_MATERIAL` | method | 1 | recorded | Fixed-function lighting (recorded) |
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
| 0x0314 | `SET_LIGHTING_ENABLE` | method | 1 | recorded | Other |
| 0x0318 | `SET_POINT_PARAMS_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x031C | `SET_POINT_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0320 | `SET_LINE_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0324 | `SET_POLY_SMOOTH_ENABLE` | method | 1 | recorded | Raster modes / queries |
| 0x0328 | `SET_SKIN_MODE` | method | 1 | recorded | Fixed-function lighting (recorded) |
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
| 0x03A4 | `SET_NORMALIZATION_ENABLE` | method | 1 | recorded | Vertex submission |
| 0x03A8 | `SET_MATERIAL_EMISSION` | range | 3 | recorded | Fixed-function lighting (recorded) |
| 0x03B4 | `SET_MATERIAL_ALPHA` | method | 1 | recorded | Fixed-function lighting (recorded) |
| 0x03B8 | `SET_SPECULAR_ENABLE` | method | 1 | recorded | Fixed-function lighting (recorded) |
| 0x03BC | `SET_LIGHT_ENABLE_MASK` | method | 1 | recorded | Fixed-function lighting (recorded) |
| 0x03C0 | `SET_TEXGEN_S` | case4 | 4 | recorded | Texture / texgen |
| 0x03C4 | `SET_TEXGEN_T` | case4 | 4 | recorded | Texture / texgen |
| 0x03C8 | `SET_TEXGEN_R` | case4 | 4 | recorded | Texture / texgen |
| 0x03CC | `SET_TEXGEN_Q` | case4 | 4 | recorded | Texture / texgen |
| 0x0420 | `SET_TEXTURE_MATRIX_ENABLE` | case4 | 4 | recorded | Texture / texgen |
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
| 0x09E0 | `SET_SPECULAR_PARAMS` | range | 6 | recorded | Fixed-function lighting (recorded) |
| 0x09FC | `SET_PROVOKING_VERTEX` | method | 1 | recorded | Raster modes / queries |
| 0x0A10 | `SET_SCENE_AMBIENT_COLOR` | range | 3 | recorded | Fixed-function lighting (recorded) |
| 0x0A20 | `SET_VIEWPORT_OFFSET` | range | 4 | handled | Other |
| 0x0A30 | `SET_POINT_PARAMS` | range | 8 | recorded | Raster modes / queries |
| 0x0A50 | `SET_EYE_POSITION` | range | 4 | handled | Fixed-function lighting (recorded) |
| 0x0A60 | `SET_COMBINER_FACTOR0` | range | 8 | handled | Combiner / shader |
| 0x0A80 | `SET_COMBINER_FACTOR1` | range | 8 | handled | Combiner / shader |
| 0x0AA0 | `SET_COMBINER_ALPHA_OCW` | range | 8 | handled | Combiner / shader |
| 0x0AC0 | `SET_COMBINER_COLOR_ICW` | range | 8 | handled | Combiner / shader |
| 0x0AE0 | `SET_COLOR_KEY_COLOR` | range | 4 | recorded | Raster modes / queries |
| 0x0AF0 | `SET_VIEWPORT_SCALE` | range | 4 | handled | Other |
| 0x0B00 | `SET_TRANSFORM_PROGRAM` | range | 32 | handled | Transform / VSH |
| 0x0B80 | `SET_TRANSFORM_CONSTANT` | range | 32 | handled | Transform / VSH |
| 0x0C00 | `SET_BACK_LIGHT_AMBIENT_COLOR` | range | 128 | recorded | Fixed-function lighting (recorded) |
| 0x1000 | `SET_LIGHT_AMBIENT_COLOR` | range | 256 | recorded | Fixed-function lighting (recorded) |
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
| 0x1B20 | `SET_TEXTURE_PALETTE` | case4 | 4 | recorded | Texture / texgen |
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
| Fixed-function lighting (recorded) | 15 | 1 | 14 |
| Fog | 7 | 7 | 0 |
| Other | 9 | 6 | 3 |
| Presentation | 5 | 5 | 0 |
| Raster modes / queries | 27 | 3 | 24 |
| Raster state | 24 | 24 | 0 |
| Surface / clear / present | 15 | 15 | 0 |
| Texture / texgen | 20 | 13 | 7 |
| Transform / VSH | 8 | 7 | 1 |
| Transform misc / shadow | 3 | 1 | 2 |
| Vertex submission | 43 | 12 | 31 |

## Feature-level gaps (not visible as method entries)

These are state values or execution paths that the method table cannot show.
They are the items a commercial title is most likely to hit first.

| Area | State / method | XGrid status | Impact |
|---|---|---|---|
| **DXT textures** | `SET_TEXTURE_FORMAT` color `0x0C`/`0x0E`/`0x0F` | No decode; `TextureFormatBytesPerTexel` returns 0 and the sampler counts a miss | Many 2002-era titles use DXT1/3/5; textures render untextured/white |
| **Paletted textures** | `SET_TEXTURE_PALETTE` + paletted color formats | `SET_TEXTURE_PALETTE` recorded only; no palette lookup | Paletted title assets fail |
| **Mipmaps** | `SET_TEXTURE_FORMAT` MIPMAP_LEVELS, `SET_TEXTURE_CONTROL0` MAX_LOD | Levels are stored; sampling uses level 0 only | Distant textures alias/shimmer; some titles depend on mip chains |
| **Texture filtering** | `SET_TEXTURE_FILTER` | Stored; sampler and GL path always use NEAREST | No bilinear/trilinear/anisotropic; large surfaces look blocky |
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
| **Fixed-function lighting/material/texgen** | `SET_LIGHT_*`, `SET_MATERIAL_*`, `SET_TEXGEN_*`, `SET_TEXTURE_MATRIX_ENABLE`, `SET_EYE_*`, `SET_SPECULAR_PARAMS*`, `SET_SCENE_AMBIENT_COLOR` | Recorded only; the D3D path normally uses driver-generated VSH programs | Usually safe for D3D titles; a gap for XVSS/fixed-function paths |
| **Render-to-texture** | surface/texture aliasing | Partial: the presented surface and RAM-backed textures work; general RTT feedback is not a first-class path | Titles that render to a texture and then sample it may show stale/black results |

## Stored but not consumed

These methods have a core state sink (so they are `handled` in the table)
but the value is not fully honored by the current renderer:

- `SET_TEXTURE_FILTER` — stored; NEAREST is hard-coded in both the software
  sampler and the GL upload path.
- `SET_TEXTURE_FORMAT` — the MIPMAP_LEVELS field is stored, but sampling is
  level 0; the color-format field supports only `0x02`-`0x07`,
  `0x10`-`0x12`, `0x1C`-`0x1E`.
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

## Regenerating this table

The table is a snapshot of:

- `References2/xemu-0.8.137/hw/xbox/nv2a/pgraph/methods.h.inc` (entries),
- `References2/xemu-0.8.137/hw/xbox/nv2a/nv2a_regs.h` (offsets),
- `src/XGrid.Nv2a/Nv2aMmio.cs` (constants, `case` labels, `if` ranges).

To refresh it, extract the `DEF_METHOD*` entries and `NV097_*` defines, build
the offset set from the XGrid `Method*` constants plus the
`method >= MethodX && method <= MethodY` ranges, and compare. The classification
is deliberately mechanical: a method is `handled` if any offset in its
range/case group has an XGrid sink; it does not prove the sink is semantically
complete.

## Caveats

- Verified against the final 2026-10-20 tree after §204 landed (`90abc9b`):
  `Nv2aMmio.cs` still has 125 `Method*` constants, 70 exact sinks and 28
  ranges, and the 108 handled / 93 recorded split is unchanged. The §204
  present-path work changed submission and histogram plumbing, not method
  coverage; regenerate only if a later change adds or removes a state sink.
- The method table is from **xemu 0.8.137**; the Playtest fork has five newer
  NV097 methods and a tuned backend, so it can differ.
- `handled` is not a fidelity claim. The XGrid test suite and the 1B goldens
  are the fidelity gates; this checklist is a gap finder.
- The 30 immediate-mode entries (~75 method offsets) are the largest single
  block; if a target title uses `SET_VERTEX3F`, that is the first
  implementation slice.
