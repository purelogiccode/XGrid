# §110 research — GPU backend design (first host-GPU slice)

Wave `wave/gpu-backend-scope` (worktree `Temp\opencode\worktrees\gpu-backend-scope`,
branch from master @692653c). DOCS ONLY — no `src/`/`tests/` changes in this wave.
Companion pointer: `xgrid-architecture-decisions.md` §110.

**Evidence policy.** Facts only from `References2/` (xemu/Cxbx = GPL/LGPL: structure and
semantics extracted, **no code copied**; envytools/nv2a-trace = MIT, names/offsets cited
as facts, no programmatic reuse) plus this repo's own research notes (cited as §N) and
runs captured for this document (command + outputs quoted verbatim). Where a required
run did not happen, the gap is stated, never invented.

---

## 1. Method/state subset inventory

### 1.1 Dashboard numbers (from research notes)

At 1.7B steps the dashboard's boot/render prefix produced **186,844 PGRAPH methods**
(§"Next gate — NV2A M5" / §37 instrumentation), of which the notes itemize:

| Method | Offset | Count (1.7B boot) |
|---|---|---|
| `ARRAY_ELEMENT16` | `0x1800` | 58,577 |
| `SET_TRANSFORM_CONSTANT_LOAD` | `0x1EA4` | 8,301 |
| transform constant uploads | `0x0B80`+ | (counted with the loads; words at `0x0B80-0x0BFC`) |
| `SET_BEGIN_END` | `0x17FC` | 5,340 draws |
| `INLINE_ARRAY` | `0x1818` | 396 |
| `DRAW_ARRAYS` | `0x1810` | 32 |
| surface/clear/flip group | `0x0200-0x0214`, `0x1D8C/1D90/1D94/1D98/1D9C`, `0x0120-0x0130` | not itemized in the notes |

Steady-state program shaders read **loaded C0** (§57–§59; the earlier "c58/c59 never
loaded" claim was corrected by §71 — all 11 XF-context constant writers exist).
Pixel accounting: at 1.7B all 10,137,600 px came from 33 legacy fullscreen inline draws;
the ~2,637 program draws were geometrically degenerate then (§42), later proven live
(§§57–58). Menu texture formats: `12`=210.7M/0, `04`=45.2M/0, `06`=22.6M/0 samples,
**zero DXT**, only miss `0x30` LU_IMAGE_DEPTH_Y16_FIXED 62.4M/62.4M (~18%, decorative)
(§105). Menu fragment load (1B menu-idle, §95): `blended=210,800,484`,
`depth=136`, `frames=682`.

### 1.2 Triangle demo histogram (captured for this wave)

Run (private media copies in `wt-data\`, triangle ISO read-only):

```
dotnet src\XGrid.Cli\bin\Release\net9.0\XGrid.Cli.dll boot ^
  --bios wt-data\Complex_4627.bin --mcpx wt-data\mcpx_1.0.bin ^
  --eeprom wt-data\eeprom.bin --hdd wt-data\xbox_hdd.qcow2 ^
  --dvd C:\Users\HomePC\AppData\Local\Temp\opencode\triangle-iso\triangle.iso ^
  --steps 1000000000 --pgraph <temp>\triangle-pgraph.txt --frame <temp>\triangle-frame.bmp
```

Result (2026-10-13, Release, exit 0): **1B steps, `LastFault vec=0`**, EIP `0x8001B032`
Halted=False. Counters verbatim:

```
NV2A draws executed=2706 skipped=0 pixels=88944649 textured=48754488 clears=6455
  blended=518 depth=15667336 zclip=0 zclipCull=21526 frames=68 stalls=34
  programLoad=8 constLoad=100 inlineElements=0
NV2A tex ok=48754488 rgb0=26208389 disabled=0 badsize=0
  formats= 30=62444852/62444852 06=38309688/0 12=10444800/0 fallbacks=0 byMode= dotMaps=0
NV2A combiner rgb0=62540443 rgb0Alpha=62487673 stages=1 control=00000001
  final=True draws=2672 texmodes=1330
NV2A semaphore releases=53
Frame ... 640x480 nonzero=307200 frames=68 stalls=34 read=1 write=2 modulo=3
  surface=3D00000 640x480 pitch=2560 valid=True frame=68
PGRAPH ... methods=223956
```

`methods=223956 < MethodStreamCap (262144)` → the dump is the **full method stream**
for the run (not truncated). **471 unique (subchannel, method) pairs.** The figures
match §106's fresh triangle boot exactly (`draws=2706 pixels=88.9M frames=68`), so the
capture is reproducible. The stream contains the dashboard/kernel boot prefix (the
`ARRAY_ELEMENT16` count 58,577 is *identical* to the notes' dashboard figure) plus the
triangle XBE phase (e.g. `SET_BEGIN_END` +72 over the dashboard figure ≈ 3 triangles ×
24 begin/end pairs; attribution of individual deltas is approximate).

Histogram (subchannel 0 unless noted; top entries, counts from
`triangle-histogram.txt`):

| Offset | Name (xemu `nv2a_regs.h` / envytools) | Count | Implemented today? | Backend feature needed |
|---|---|---|---|---|
| `0x1800` | `ARRAY_ELEMENT16` (envytools `VTXBUF_ELEMENT_U16`) | 58,577 | yes — fetch + element draw (§39, §71) | GPU vertex fetch + draw submission |
| `0x0B80-0x0BFC` | transform constant upload words (4/entry) | 73,376 words | yes (§37, §71 all 11 XF writers) | uniform upload, dirty-section tracked (§4 below) |
| `0x1EA4` | `SET_TRANSFORM_CONSTANT_LOAD` | 8,340 | yes (§37) | load-index shadow state (uniform path) |
| `0x1D90` / `0x1D8C` | `SET_COLOR_CLEAR_VALUE` / `SET_ZSTENCIL_CLEAR_VALUE` (+ mirrors `0x186C`/`0x1A88`, §106) | 7,125 / 7,089 | yes (§106) | clear-value shadow state |
| `0x1D94` (+`0x1D98`/`0x1D9C` rect) | `CLEAR_SURFACE` + clear rect | 6,455 | yes (DMA-context base §106) | GPU clear (scissored) |
| `0x17FC` | `SET_BEGIN_END` (envytools `VERTEX_BEGIN_END`) | 5,412 | yes — flushes draws (§37) | draw boundary / state commit point |
| `0x0B90-0x0BBC` | more constant-upload words | 39,304 words | yes | uniform upload |
| `0x0100` | `NO_OPERATION` (stall-until-`PGRAPH_INTR`, §106) | 702 | yes — **pusher stall** | none (pusher-level; must not go async) |
| `0x0B00-0x0B7C` | `SET_TRANSFORM_PROGRAM` words (4/insn; `programLoad=8`) | 494/486/280/279 per word slot | yes (§38 `VshProgram`) | shader key + cache; slice-1 keeps CPU VshProgram (§3) |
| `0x1818` | `INLINE_ARRAY` (envytools `VTXBUF_DATA`) | 408 | yes (§39, §71) | vertex submission (inline → buffer) |
| `0x1808` | `ARRAY_ELEMENT32` | 374 | yes (§39) | vertex fetch |
| `0x0394`/`0x0398` | ZCLIP bounds — XGrid `ClipMin/Max`; envytools `DEPTH_RANGE_NEAR/FAR` | 358 | yes (§90, §97) | fragment-stage clip uniform (or `glClipControl`-equivalent in shader) |
| `0x0A20-0x0A2C` | viewport offset | 358 | yes (§37) | uniform |
| `0x0B40-0x0B7C` | program words (later slots) | 280/279 | yes | shader key |
| `0x0290` | `SET_CONTROL0` | 218 | yes (case `MethodControl0`) | shader-key input (combiner control) |
| `0x0A60-0x0A9C` | combiner FACTOR0/1 (C0/C1) | 107–218 | yes (§75) | uniform (unique-C0/C1 bits) |
| `0x0110` | `WAIT_FOR_IDLE` | 213 | **record-only** (no case; xemu's handler only does a surface update — harmless) | none |
| `0x1760-0x179C` | vertex array format/offset | 141–209 | yes (§39, §71 stride-0/bit31 fixes) | vertex-attrib binding state |
| `0x1E9C` | `SET_TRANSFORM_PROGRAM_LOAD` | 208 | yes (§37) | shader key |
| `0x0200-0x0214` | surface clip H/V, format, pitch, color/zeta offset | 147–161 | yes (§43, DMA bases §106) | FBO/attachment config |
| `0x032C` | stencil-test enable | 148 | yes (§41) | GL fixed-function stencil state |
| `0x1E78` | `SET_SHADER_OTHER_STAGE_INPUT` | 148 | yes (§75–§78) | shader key |
| `0x0304` | blend enable | 145 | yes (§41) | GL blend state |
| `0x0308` | **cull-face enable** (envytools `CULL_FACE_ENABLE`) | 139 | **record-only** (no case; software rasterizer renders both windings) | parity rule: backend must also stay double-sided for slice 1 (§3.4) |
| `0x1B00`/`0x1B04` (+`0x40`-stride slots) | texture offset/format per slot | 81–116/each | yes (§40, §95) | texture upload + sampler state |
| `0x0300` | alpha-test enable | 110 | yes (§41) | shader discard / GL alpha test |
| `0x030C` | depth-test enable | 83 | yes (§41) | GL depth state |
| `0x0310-0x0324` | dither/lighting/point/line/poly smooth enables | 70–74 | **record-only** | none (keep inert for parity) |
| `0x0358`/`0x035C` | color mask / depth mask | 70 / 101 | yes (§41) | GL masks |
| `0x0344`/`0x0348`/`0x0350` | blend src/dst factor, equation | 36 / 36 / 2 | yes (§41, GL-style codes) | GL blend state (direct map) |
| `0x17BC` | `SET_LOGIC_OP_ENABLE` | 70 | **record-only** | none for parity; later GL logic-op |
| `0x1D7C` | `SET_ANTI_ALIASING_CONTROL` | 71 | **record-only** | none |
| `0x1810` | `DRAW_ARRAYS` (envytools `VTXBUF_BATCH`) | 67 | yes (§71 range semantics) | draw submission |
| `0x1D70` | `BACK_END_WRITE_SEMAPHORE_RELEASE` | 53 | yes (§32, RAMHT handle) | none (pusher-level) |
| `0x17F8` | `SET_SHADER_CLIP_PLANE_MODE` (handled via trailing `if`, not switch) | 37 | yes (§96 CLIPPLANE) | shader key + discard snippet |
| `0x0130` | `FLIP_STALL` | 34 | yes (§43) | none (pusher-level; readback hooks read, §5) |
| `0x012C` | `FLIP_INCREMENT_WRITE` (present event) | 68 | yes (§43) | **readback → DisplayFrame at this event** |
| `0x1D78` | `SET_ZMIN_MAX_CONTROL` (ZCLIP CULL/CLAMP) | 2 | yes (§97) | fragment-stage clip mode |
| `0x1B0C-0x1B1C` (+slots) | texture control0/1/filter/image-rect | 81–118 | yes (§40, §95 rect-vs-BASE_SIZE rule) | sampler + upload sizing |
| `0x0260-0x027C`, `0x0288/0x028C`, `0x0AA0-0x0ADC`, `0x1E20/0x1E40/0x1E60/0x1E70/0x1E74` | combiner ICW/OCW/factors/final CW/CONTROL/SHADER_STAGE_PROGRAM | 1–108 | yes (§75; `0x288/28C` via trailing `if`) | **GLSL fragment generation** (§3) |
| `0x0440-0x06BC`, `0x0840-0x093C`, `0x09C0-0x09DC`, `0x0A50-0x0A5C`, `0x0AF0-0x0AFC` | matrices / texgen planes / fog params+plane / eye / viewport scale | 2–147 | yes (§71, §78, §91) | uniform sections |
| `0x1E94/0x1E98/0x1EA0` | execution-mode / cxt-write / program-start | 177/72/72 | yes (§37, §71) | shader key |
| `0x039C`/`0x03A0` | cull face / front face | 104 / 2 | **record-only** (software = both windings) | parity: keep inert |
| `0x02B4-0x02F8` | window clip type + H/V (8 slots each) | ~560 total | **record-only** | none for parity (software clips to surface only) |
| `0x0294`/`0x0298` | light control / color material | 107 / 36 | **record-only** | none (fixed-function lighting unimplemented; menu/triangle use programmable VP) |
| `0x17C4` | `SET_TWO_SIDE_LIGHT_EN` — **one of Playtest-v2's 5 newer methods** (`nv2a_regs.h` playtest `0x17C4`) | 107 | **record-only** | none; confirms the Playtest method-coverage cross-check (xemu-playtest-v2.md item 2) |
| `0x0380` | `SET_LINE_WIDTH` — also Playtest-newer (`0x380`) | 3 | **record-only** | none |
| `0x0384-0x0390`, `0x03A4-0x03BC`, `0x03C0-0x03CC`, `0x03E0-0x03F8`, `0x0A10-0x0A18`, `0x0AE0-0x0AEC`, `0x181C-0x1824` (eye vector), `0x1710` (`VTXBUF_VALIDATE`), `0x16BC` (`EDGEFLAG`), `0x09F8/0x09FC` | polygon offset params/modes, material/texgen-mode/lighting block, scene ambient, color key, misc | 1–140 | **record-only** (named above from xemu `nv2a_regs.h` + envytools `nv10_3d.xml`/`nv20_3d.xml`) | none for parity; revisit when a title renders lines/points/flat/wireframe |
| `0x1D80`/`0x1D84` | **unnamed** in xemu-0.8.136 `nv2a_regs.h` and envytools (`UNK1D80/UNK1D84`) | 2 / 73 | **record-only** | unknown — keep counted; name via Playtest `methods.h.inc` or hardware trace if a title depends on them |
| `0x0000` (ctx binds) + `0x0180-0x01FC` object params, subchannels 2/3/4 | RAMHT binds / notifications | 1–2 each | yes (§106 RAMHT, §32) | none (puller-level) |

Notes:

- A named-case + range scan of `RecordMethod` covers ~98.9% of subchannel-0 instances;
  every "record-only" row above was additionally verified by direct source grep (no
  literal, no constant, no trailing `if`). The scan is heuristic — the per-row
  "record-only" claims are the verified ones.
- All 223,956 methods executed **without suspending the pusher or faulting**; inert
  state is invisible today. The risk is only where a GPU backend would *newly honor*
  state the software path ignores (cull-face is the one the menu itself writes 139×).
- **007 histogram: OPEN.** 007 boots from a CHD at a path not recorded in the notes
  (§66/§86) and reaches rendering only after billions of steps (§67/§77 gate world) —
  not cheap to add to this wave; the re-inventory procedure in §7 makes it a 1-command
  follow-up when the CHD path is known.

## 2. Current architecture map

All in-worktree facts (`src/XGrid.Nv2a/Nv2aMmio.cs` 6,102 lines; line numbers from
692653c-era file, wave left `src/` untouched):

```
guest MMIO write 0xFD000000..  →  Nv2AMmio.Write
  └─ RunPusher (L2296)          synchronous CACHE1 DMA pusher, NO_OPERATION stall
      └─ RunPuller (L2502)      method header decode, RAMHT ctx binds, notifications
          └─ RecordMethod (L2566)   ← THE SEAM ("recording sink" of the class doc)
              ├─ side-tap: _methodStream (cap, --pgraph dump), _methodCounts/MethodParams
              ├─ giant switch + trailing ifs: shadow state (surface/flip/texture/
              │    combiner/fragment/XF/matrices/fog/...) + mirrors into _pgraph[]
              ├─ draw accumulation: _inlineVertices/_inlineElements/_drawArraysRanges
              │    + SET_VERTEX_DATA* words
              └─ on END(0x17FC param 0) / clear / flip:
                   FlushDraw (L3787) ──► per-vertex VshProgram.Run (L~4166)
                       (screen-space oPos, oD0, oT0; fixed-function foggen §91)
                   FillTriangle (L5218) barycentric (perspective-correct §71),
                       texture0 sample (TrySampleTexture0 §40/§95/§96)
                       └─ FillTrianglePsh (L5875)  PshCombiner 8-stage + final (§75/§78)
                           └─ ProcessFragment (L5062) zclip→alpha→stencil→depth→
                              blend→colormask (§41, §90, §97)  → guest RAM (IGpuMemory)
                   ExecuteClear (L3324) color/zeta fill → guest RAM
                   0x12C flip: rotate write idx, capture at-flip surface record
                       TryCopyPresentedFrame (L4939) → BGRA bytes
                           └─ XGrid.Graphics.DisplayFrame.Show / SaveBmp (CLI --frame)
```

- **M4–M8 organization** (AGENTS.md): M4 inline draws → M5 program/attribute draw →
  M6 textures → M7 fragment → M8 flip/present, all inside `Nv2AMmio` +
  `VshProgram.cs` (556 lines) + `PshCombiner.cs` (1,026 lines) + `IGpuMemory`.
- **`DisplayFrame`** (`XGrid.Graphics`, 103 lines): BGRA frame store, dimension/size
  validation, `CountNonZeroPixels`, 32-bit bottom-up BMP export. No GPU objects.
- **The seam:** `RecordMethod` is the single point where every `(subchannel, method,
  parameter)` is observed, and the flush sites (`FlushDraw`, `ExecuteClear`, the
  `0x12C` flip case) are the only places rendering/present happens. A GPU backend is
  exactly a **second consumer of the same dispatch**: shadow state → GPU state, flush →
  draw/clear commands, flip → readback into the existing `DisplayFrame`. The
  pusher/puller/stall/semaphore/IRQ logic above the seam is backend-agnostic and must
  not change.
- Public observability already exists for differential work: `MethodStream`,
  `MethodCounts`, `MethodParams`, and all §43 counters — no new instrumentation is
  required just to inventory a title.

## 3. First-slice design

### 3.1 Scope: menu method subset vs full NV2A

Per Velocity rule 1 ("GPU backend for the menu's method stream, not full NV2A"),
slice 1 = the rows of §1.2 marked "yes" **that the menu actually exercises**, namely:

- draws: `0x17FC` begin/end, `0x1800` elements (dominant), `0x1818` inline, `0x1810`
  draw-arrays, `0x1808` element32, `SET_VERTEX_DATA* 0x1880-0x1AFC` (§71), vertex
  array format/offset `0x1720-0x179C`;
- vertex: constant uploads `0x0B80+` + loads `0x1EA4`/`0x1E9C`/`0x1EA0` (program
  executes on **CPU** `VshProgram` in slice 1 — see §3.3);
- surfaces/clear/flip: `0x0200-0x0214`, `0x1D8C/1D90/1D94/1D98/1D9C`, `0x0120-0x0130`;
- fragment state: `0x300-0x378` (all implemented rows), ZCLIP `0x394/0x398/0x1D78`,
  shader-clip `0x17F8`;
- combiner: the `0x260-0x2FC` / `0xA60-0xADC` / `0x1E20-0x1E78` sets — **the menu's
  observed combiner is `stages=1 control=00000001 final=True`** (own run §1.2), so the
  generated GLSL must correctly cover 1 general stage + final combiner; the emitter
  is written generically (8 stages) but only 1-stage+final is *accepted* by the
  slice-1 golden gate;
- textures: slots with formats **`12` (SZ/LU A8R8G8B8), `04` (A4R4G4B4), `06`
  (R5G6B5... actually `06`=A8R8G8B8 — see §3.2)**, wrap/clamp, LU texel-unit vs SZ
  normalized coordinates (§95), swizzle BASE_SIZE rule (§95);
- explicitly **out** for slice 1: DXT (§105: zero dashboard usage), mip LOD (§105),
  `0x30` depth texture (§105 open, decorative), exotic modes (PROJECT3D/CUBEMAP/dot/
  HILO §96 — dashboard falls back decoratively), fixed-function lighting (record-only
  today), window clip/poly mode/cull (record-only), render-to-texture blits (§44: none
  on the menu path), pbkit DMA-context surface-offset path (§106 — slice 3), `0x110`
  WAIT_FOR_IDLE etc.

### 3.2 Texture formats actually exercised

- Menu (§95/§105): `12`×210.7M, `04`×45.2M, `06`×22.6M samples, 0 misses after §95;
  DXT `0x0C/0x0E/0x0F` = 0 usage; only miss `0x30` (decorative).
- Triangle run (own): `30`=62,444,852/62,444,852 (all miss — the same decorative
  depth-format stage), `06`=38,309,688/0, `12`=10,444,800/0.
- So slice-1 texture support = **A8R8G8B8 (SZ + LU), A4R4G4B4 (SZ + LU), R5G6B5
  (SZ + LU)** — i.e. the §95 decode table rows 02/10, 03/1C, 04/1D, 05/11, 06/12,
  07/1E (implement the whole small table; it is one table, not six features). No DXT,
  no mip levels (base level only, §105).

### 3.3 Fragment features used

- Menu (§95): alpha test, blend (heavy, 210M blended px), color mask, near-zero depth
  (`depth=136`), no stencil counts observed; combiner 1 stage + final; fog state exists
  in the path (§78/§91 — keep in slice 2 if the menu's gradient needs it; the §95
  readable-menu frames are the oracle for whether slice 1 must include fog).
- Triangle (own run): `depth=15,667,336`, `blended=518`, `zclipCull=21,526`,
  `zclip=0` → slice 3 needs depth-heavy correctness; ZCLIP CULL must run at fragment
  top before alpha (§97).
- GL mapping is favorable: §41's factors/equations/funcs are already "raw GL-style
  codes"; depth/stencil/blend/color-mask/alpha-test map to GL fixed-function state;
  only the combiner math + texture-mode equations + ZCLIP discard need GLSL.

### 3.4 Shader translation options

**(a) Translate XGrid's own `PshCombiner`/`VshProgram` semantics to GLSL at draw /
uniform-update time.**
- Structure facts (no code): xemu 0.8.136 builds a `ShaderState` snapshot
  (`glsl/shaders.c` `pgraph_glsl_get_shader_state`) and dirty-checks a fixed list of
  PGRAPH regs + per-stage combiner words + per-slot TEXCTL/TEXFMT/TEXFILTER
  (`pgraph_glsl_check_shader_state_dirty`) → hash → module cache with LRU and
  on-disk program-binary persistence (`gl/shaders.c`: `get_shader_module_for_key`,
  `generate_shaders`, `shader_load_from_disk`, `apply_uniform_updates`,
  `update_shader_uniforms`, `pgraph_gl_bind_shaders`); fragment generation entry
  `pgraph_glsl_gen_psh(PshState)` walks stage code + final stage + sampler types
  per texture mode (`glsl/psh.c`, 1,628 lines); vertex-program generation entry
  `pgraph_glsl_gen_vsh_prog(tokens)` decodes each instruction to GLSL
  (`decode_token`→`decode_opcode`→swizzle/inputs, 711 lines); fixed-function VS is a
  separate generator with XFCTX uniform macros (`glsl/vsh-ff.c`, 472 lines). Cxbx
  mirrors this shape: `src/devices/video/nv2a_psh.cpp` (796 lines:
  `add_stage_code`/`add_final_stage_code`/`psh_convert`), `nv2a_vsh.cpp` (717:
  `decode_token`/`vsh_translate`), `nv2a_shaders.cpp` (853:
  `generate_fixed_function`/`generate_vertex_shader`). **Facts/structure only — both
  are GPL/LGPL; nothing is ported.**
- Pro: `PshCombiner.cs`/`VshProgram.cs` are the *validated oracle* (1,000+ tests,
  golden frames §95/§97/§106). One semantic source, two executors; divergence shows
  up as golden-frame diffs. No license exposure (we write GLSL from our own C#
  semantics). §95's norm()/BASE_SIZE rules were already distilled into facts we own.
- Con: two executors to keep in lockstep forever (mitigated: goldens gate every
  combiner/texture change already).

**(b) xemu-style per-shader GLSL generation ported as a second semantics.**
- Pro: battle-tested output shape; Cxbx same.
- Con: re-implements combiner/texture math from GPL sources with no test oracle of its
  own until goldens exist anyway; high clean-room overhead; XGrid's semantics already
  diverge deliberately in documented ways (§90-§97 deviations list) — a port would
  have to re-derive each deviation.

**(c) Keep the software interpreter only.**
- Pro: zero risk, current fidelity.
- Con: no performance goal met (the whole point of the backend track, AGENTS velocity
  program: menu interactivity needs GPU fill).

**Recommendation: (a), staged.** Slice 1 translates **fragment** semantics
(`PshCombiner` + texture-mode equations + ZCLIP/alpha discard) to GLSL, keeps the
**vertex** path on CPU `VshProgram` (exact oPos epilogue incl. 1/16-pixel truncation
and w-clamp, §38/§71 — vertex counts are tiny: 58K element refs per boot vs 48.7M
texture samples; the fragment stage is where fill-rate lives), and uses GL
fixed-function for blend/depth/stencil/mask/alpha-test. Later slices may move the VP
to generated GLSL (option-b *structure* — token decode → GLSL — is available then),
but only behind the same goldens.

**Golden-frame acceptance path.** Deterministic same-boot pair: software
(`--gpu sw`, default) vs backend (`--gpu gl`) at fixed steps, both `--frame` BMPs
compared: (1) **structural**: `frames=`, `stalls=`, `draws` must match exactly
(these are pusher-level / dispatch-level and backend-independent); (2) **pixel**:
flat regions byte-exact; differences allowed only within a ±1 px edge band of
software-detected edges, per-channel tolerance ≤2 — the same ±2 tolerance xemu's own
driver self-test uses for a known texel (`gl/probe.c` L184-189); report
diff-pixel-count and max-delta like §95's RGB-diff method; (3) a scene with large
flat triangles (triangle ISO, or `data/test-samples` ROM-A textured quad §103) must be
near-exact since edge bands are tiny. Cross-driver variance is a risk (§7); goldens
are recorded per host GPU + driver until a tolerance band is proven portable.

## 4. Playtest-v2 performance lessons (applicability matrix)

Facts read directly from `References2/xemu-Playtest-v2/hw/xbox/nv2a/` (LGPL/GPL headers;
structure + techniques only):

- **`pgraph/cost.c` (745 lines)** — per-frame *real-hardware speed* cost model:
  per-segment (runs of draws sharing a register-derived fragment cost profile)
  fragment/query accounting resolved at flip; roofline max(vertex, pixel, bandwidth)
  + per-draw + report serialization; EMA-smoothed deadline feeding flip completion
  under the live `perf.real_hw_speed` toggle; heuristic constants explicitly labeled
  PROVEN/MEASURED/HEURISTIC (texture-cache 4 KB cap, z-accept, 79% ROP coalescing).
- **`pgraph/seed.c` (328 lines)** — XSED-format per-game shader-seed files +
  embedded zlib dictionary (`XSDD`), so known shader families never compile inside a
  frame; an unseeded known family draws with a *dynamic sibling* while the exact
  program compiles; item-size check invalidates stale layouts.
- **`pgraph/writeback.c` (104 lines)** — evicted-surface writeback heuristics: skip
  writeback when no CPU reader read the range, no live-range consumer, and it is not
  the scanout framebuffer; learned live ranges + discard spans; scanout always
  preserved (`pcrtc.start` check).
- **`pgraph/glsl/uniform-cache.c` (195 lines)** — sectioned VS uniforms
  (vshc/ltctx/light/pp/fogp) with generation counters; dirty sections detected from
  per-constant dirty bytes; per-program 8-slot open-addressed cache; skip compare +
  upload when the section generation matches, else last-value memcmp diff.
- **`gl/probe.c` (250 lines)** — one-time self-test at context creation: separable
  stage programs, program pipelines + switch, stage uniforms, textured draw with
  **±2 pixel check**, pipeline switch, `glCopyImageSubData`; any GL error or wrong
  pixel disables the optimization pack **for the session** (not trusted by version
  string).
- **`nv2a-stock/` baseline** — `git diff --no-index --stat hw/xbox/nv2a-stock
  hw/xbox/nv2a` (run in this worktree): 92 files, **+14,373/−1,921** — the entire
  tuned-vs-stock delta is enumerable (`cost.c` +745, `seed.c` +328, `writeback.c`
  +104, `uniform-cache.c` +195, `probe.c` +250, `gl/shaders.c` +1,713,
  `gl/surface.c` +1,728, `vk/draw.c` +2,417, …). This is the catalog to re-read when
  justifying each later optimization; `stock-names.h` lists the stock file set.

| Technique | Slice 1 | Slice 2 | Slice 3 | Later / never |
|---|---|---|---|---|
| **Uniform dirty-sections + last-value diff** (uniform-cache.c) | **YES — early.** 8,340 const loads/boot (own run); sections = (constants, fog, viewport, matrices); generation counters cost ~0 and prevent per-draw 73K-word uploads | keep | keep | — |
| **Shader module cache keyed by state hash** (structure: `glsl/shaders.c` + `gl/shaders.c` LRU + disk binaries) | **YES** — needed for slice 1 (combiner/texture state churns); in-memory LRU first | + disk persistence if recompile hitches show | — | — |
| **Shader seeds** (seed.c XSED/XSDD) | no — menu compiles few programs (programLoad=8/boot) | no | evaluate for 007 | **after slice ships**: needs stable ShaderState key layout + a per-title corpus + the dynamic-sibling draw path; premature now |
| **Writeback heuristics** (writeback.c live-ranges/discard-spans) | **no** — slice 1 reads surfaces only at flip (readback, §5); no eviction exists | when GPU-side surface cache/render-to-texture (§44 `0x3628000`) starts evicting | yes (titles read surfaces) | scanout-never-skip rule applies whenever enabled |
| **Per-frame cost model** (cost.c real-hw-speed) | no | no | no | **likely never**: XGrid paces the guest via PIT/vblank/flip-stall (§67 frame limiter), not via modeled GPU cost; cost.c exists to emulate *real-hardware frame time* — a feature XGrid has not scoped. Revisit only if a title gates on GPU timing finer than vblank |
| **Driver probe gating** (probe.c) | **YES — the pattern.** Run a self-test (small shader compile, FBO completeness, known-texel draw ±2, optional copy-image) at backend init; failure → fall back to software for the session + print why | extend per feature used | Vulkan path gets its own probe | informs **GL-first**: probe.c is written against GL; xemu's `vk/` is the tuned-but-heavier second backend |
| **Blit suspend/resume around cost segments, seed builds at flip-time services** | no | no | no | follows cost/seeds decisions above |

## 5. Present/flip integration

- **DisplayFrame parity first; swapchain later.** Slice 1 keeps the present path
  exactly as §43/§106 built it: at `FLIP_INCREMENT_WRITE (0x12C)` the backend
  readbacks its color surface (GPU → host BGRA bytes) and feeds the existing
  `TryCopyPresentedFrame` contract → `DisplayFrame.Show` → CLI `--frame` BMP.
  Nothing about `--frame`, `--fb`, or BMP format changes; the only new fact is the
  byte source (GPU readback instead of guest-RAM copy). Window/swapchain presentation
  is explicitly out of scope (the CLI BMP *is* the display for gate purposes).
- **Flip-stall semantics must be preserved bit-for-bit** because they pace the guest:
  `0x130` parks the FIFO until read≠write; vblank in `Advance` steps read toward
  write; these live above the seam and are backend-independent. Oracle: §43 recorded
  `frames=33 stalls=33` (exact 1:1) — but §106 notes the faithful `NO_OPERATION`
  stall changed pacing, so **§43's absolute counters need re-recording** (open);
  invariance required of the backend is (a) `frames`/`stalls` equality between sw/gl
  runs of the same boot, and (b) an at-flip capture each `0x12C` (surface record:
  offset/clip/pitch/format valid, as §43 defines). Own triangle capture
  (`frames=68 stalls=34` — a 2:1 pairing, dashboard-prefix + triangle phases mixed)
  is recorded here as current data, not as the oracle.
- §106 open item applies to slice 3: pbkit presents via `NV_PGRAPH_INCREMENT
  (0x71C)` + `PCRTC_START`, not `0x12C`; the triangle frame is currently captured
  through the dashboard-style flip path. A dedicated pbkit present hook is optional
  follow-up; slice 1 (menu) uses `0x12C` only.
- Coherence rule for slice 1: textures are uploaded from guest RAM **on bind** (keyed
  by offset+format+size+content-generation), color/zeta live in the GPU for the whole
  acceptance run, readback happens only at flip. No guest-RAM read of GPU surfaces is
  needed on the menu path (§44: no blit, no surface-read traffic; CPU reads of the
  color buffer outside flip would be the first eviction/writeback trigger — counter
  them).

## 6. Milestones with acceptance tests

Each gate is scoped to its acceptance test (Velocity rule 1). The **software path is
always retained behind a flag** (default) for differential diagnosis (Velocity rule 3).

- **Slice 1 — dashboard menu method subset via GPU backend.**
  Acceptance: neutral 1B-step menu boot (§95 recipe) run twice, `--gpu sw --frame
  sw.bmp` and `--gpu gl --frame gl.bmp`: both complete with `LastFault vec=0`;
  `frames=`/`stalls=`/`draws` equal across runs; pixel diff obeys the §3.4 policy
  (flat regions exact, edges ≤2/channel inside ±1px band); backend fallback counter
  = 0 (every draw in the subset was GPU-executed); existing suite green unchanged
  with `--gpu sw` default.
- **Slice 2 — triangle golden gate + tripwire closure (wave `wave/gpu-slice2`,
  §117).** DONE 2026-10-13: triangle 1B boot sw<->gl golden PASSES the §3.4
  policy (BMP violations=0 maxDelta=1, counters exact 2706/68/34, sw SHA
  `A1740DE0…` verified, `BackendUnsupported=0` — the triangle stream trips
  zero §113 unsup rows); dashboard menu golden re-verified bit-identical to
  §113; every §113 `unsup[...]` row either implemented+gate-validated
  (FBO-grow content-preserving blit, fires 14×/boot, frames bit-identical
  before/after) or evidence-justified with per-run counters (§117 table);
  risk-exposure diagnostics `diag[fboReallocs/fboZetaDrops/clipXY]` armed.
  The original "full dashboard surface" sub-items (menu+submenu navigation
  golden, texture-format table, blend/alpha/fog exercised, `0x3628000`
  render-to-texture) are covered by the §113 pad-script gate re-verified
  here plus the §117 by-construction note (software path stays
  unconditional, so texture uploads read software-coherent guest RAM).
- **Slice 3 — 007 streams** (triangle half landed early as the slice-2
  gate above). **DONE 2026-10-14 (research notes §122):** two bounded
  007 segments, each sw-vs-gl from a v2 snapshot
  (`scripts/gpu-slice3-007.ps1`): fmv (`pd181.xgs` + 1B, steady
  intro FMV) and transition (`pd536.xgs` + 1B, stream-end screen
  transition into the §118 G2 park). Transition = full §3.4 golden
  (structural equality, `BackendUnsupported=0`, sw SHA `18C7CC83…` =
  the §114 held-frame oracle, gl frame byte-identical, new
  `diag[flipEmpty]=0`). FMV = documented-gap golden: 39 post-restore
  flips with zero PGRAPH draws/clears (pure CPU blits), `flipEmpty=39
  == flips`, sw frame reproduced exactly across builds
  (`45835626…`), gl serves nothing (no-mixing rule) —
  `BackendUnsupported=0` on both. 007 trips zero `unsup[` rows in
  either segment. Headless fps table (§122): GL submission+readback =
  0–2.9% of wall on all four workloads — emulation is 97–100%, so the
  swapchain wave is a latency feature, not a throughput fix (Track C
  JIT stays the lever). Follow-up REPORTED (needs an Nv2a seam
  change, not done here): present-upload at flip for CPU-blitted
  surfaces (extend `PgraphSurfaceRecord` with the resolved surface
  address `CapturePresentedFrame` already computes).
- **Never-gate:** full NV2A parity is *not* a milestone; unmapped needs are added to
  slices only when a title's inventory proves them (§7).

## 7. Risks

1. **Method-stream growth across titles.** Menu+triangle = 471 unique pairs; other
   titles will add methods (Playtest itself adds 5: two, `SET_TWO_SIDE_LIGHT_EN
   0x17C4` and `SET_LINE_WIDTH 0x380`, are *already* in the dashboard's stream per
   §1.2). **Cheap re-inventory procedure (verbatim):** boot the title with
   `--pgraph out.txt --pgraph-max 262144`, run the histogram
   (`group '(sub,method)' counts, sort desc` — the one used for §1.2), diff the
   unique-offset set against §1.2; every new offset gets an xemu-regs/envytools name
   lookup and a handled/inert classification (direct grep in `Nv2aMmio.cs`). Cost:
   one boot + one script. DXT/mipmap titles: §105 already specs both decode rules —
   implement only when a histogram proves usage.
2. **GL vs Vulkan.** xemu ships both (`gl/`, `vk/`); Playtest tunes both (vk/draw.c
   +2,417 in the stock diff). probe.c's lesson: **gate optimizations on a behavioral
   self-test, never on version strings**, and degrade to software for the session on
   failure. Slice 1 recommendation: **OpenGL first** (probe.c pattern ports 1:1;
   simpler readback; wide driver coverage), behind an `IRenderer` seam so `vk/`
   can follow. Host driver variance → goldens recorded per driver (§3.4); the
   ±2 tolerance comes from xemu's own probe, not from guesswork. **C# binding
   library for GL/Vulkan** - RESOLVED: Silk.NET.OpenGL (operator 2026-10-13),
   pinned centrally in Directory.Packages.props; no other new dependency.
3. **Synchronous pusher + §106 NO_OPERATION stall vs async backend.** The pusher
   runs synchronously on the emulation thread; `0x100` stalls until the guest writes
   `PGRAPH_INTR`, and resume is deferred one tick via `_pusherKickPending`
   consumed in `Advance` (§106 items 1-2). If the backend ever submits GPU work
   asynchronously, method *observation* must stay in-lockstep with this stall logic
   (or stalls fire early/late and pbkit frames drop — §106's original crash class).
   **Slice 1 rule: no async.** GL calls are issued synchronously at the existing
   flush sites on the emulation thread; readback at `0x12C` before the flip count
   advances. Async command threads are a later, flag-gated change with the
   golden-frame suite as its gate.
4. **Golden-frame determinism / tolerance.** GL fill rules, precision, and
   driver-specific interpolation make byte-exact cross-path equality unrealistic at
   triangle edges; too-loose tolerance hides real bugs. Policy (§3.4): structural
   counters exact; flat regions exact; edge band ±1px with per-channel ≤2 (probe.c
   precedent); report diff counts like §95. If a driver proves noisy, tighten by
   comparing sw-vs-sw across two runs first (the software path must already be
   deterministic — snapshots §101 bit-exactness is the precedent) to establish the
   machine's own noise floor before blaming GL.
5. **Threading model.** GL contexts are affinity-bound; slice 1 = single thread
   (emulation thread owns the context; no GL during `Advance` reentry beyond the
   flush sites). Later multi-threaded rendering needs an explicit command queue with
   sync points exactly at: `0x100` stall ack, `0x12C` flip, `0x1D70` semaphore
   release, snapshot save/load. Snapshot format: backend host state is **not** part
   of `.xgs` (recreated from guest state on load) — keep `ISnapshotable` surface
   unchanged so V1/V2 snapshots stay software-reproducible.
6. **Surface coherence / eviction.** Until writeback.c-style tracking exists, the
   backend must conservatively read back on any guest-visible surface access outside
   flip; counter those events — if the menu shows nonzero counts, slice 2 scope grows
   (writeback moves earlier).
7. **Counter-oracle drift.** §43/§95/§101 baselines were re-based by §106's faithful
   stall; acceptance comparisons are always **sw-vs-gl within the same build**, never
   against stale absolute numbers (open: re-record absolute baselines).

## 8. Concrete next implementation work order (verbatim task for the next wave)

> **Task: slice-1 GPU backend skeleton + menu golden-frame gate.**
> Prerequisite: merge the sibling `XGrid.Nv2a` changes first (this design assumes the
> post-merge file; coordinate the hook so both waves touch `Nv2AMmio` once).
>
> 1. **Operator checkpoint: RESOLVED 2026-10-13 — `Silk.NET.OpenGL`** (operator
>    choice; recorded in `sources.md`). No other new dependency.
> 2. **New project `src/XGrid.GpuGl/`** (referenced by `XGrid.Cli` only; do **not**
>    put renderer code in `XGrid.Nv2a` or `XGrid.Graphics` beyond the seam):
>    - `IPgraphBackend` (lives in `XGrid.Nv2a`): `void Method(uint sub, uint method,
>      uint param); void Draw(in PgraphDrawInfo); void Clear(in PgraphClearInfo);
>      void Flip(in SurfaceRecord); void ReadbackPresented(out FrameFrame);`
>      — observation-only mirror of `RecordMethod`; the software path executes
>      exactly as today, unconditionally.
>    - `Nv2AMmio` gains `public IPgraphBackend? Backend { get; set; }`; invoke
>      `Backend?.Method(...)` at the top of `RecordMethod` (after the side-tap),
>      `Backend?.Draw(...)` inside `FlushDraw`/`ExecuteClear`/flip-case with the
>      same resolved state the software path uses (surface record, resolved DMA
>      bases §106). No behavior change when `Backend == null`.
> 3. **CLI:** `--gpu sw|gl` (default `sw`). `--gpu gl` constructs the backend,
>    runs the probe self-test at init (compile tiny program, FBO complete,
>    known-texel draw ±2 — `gl/probe.c` pattern); probe failure prints why and
>    falls back to `sw` for the session.
> 4. **Backend implementation (slice-1 subset only, §3.1):** shadow state for
>    surface/flip/fragment/combiner/texture rows of §1.2; vertex path = reuse the
>    existing fetch + CPU `VshProgram` outputs to build a screen-space vertex
>    buffer at `FlushDraw`; GLSL fragment generator written **from `PshCombiner.cs`
>    semantics** (1 stage + final accepted, generic 8-stage emitter shape);
>    textures: SZ/LU A8R8G8B8/A4R4G4B4/R5G6B5 table (§95 rules: LU texel-unit,
>    SZ BASE_SIZE); blend/depth/stencil/mask/alpha-test → GL fixed-function;
>    ZCLIP discard at shader top (§97); **cull/window-clip/lighting rows stay
>    ignored to match software** (§1.2 parity rules). Unknown in-subset state →
>    increment a named `BackendUnsupported` counter and fail the gate (no silent
>    mixing with the software rasterizer in slice 1).
> 5. **Golden harness (test, host-local; document CI GL limitation honestly):**
>    pure-C# unit tests for state→GL-state mapping and GLSL-generator string
>    goldens; an integration script that runs the §95 neutral 1B boot twice
>    (`--gpu sw` / `--gpu gl`) and applies the §3.4 diff policy.
> 6. **Acceptance (slice-1 gate):** `LastFault vec=0` on both runs; `frames=`,
>    `stalls=`, `draws` identical; `BackendUnsupported == 0`; BMP diff within
>    policy (flat exact, edges ≤2/channel in ±1px band); default path unchanged —
>    full suite green (1126 baseline at this wave's start; 1200 after the
>    wave's new tests) + `dotnet format --verify-no-changes`.
> 7. **Docs:** append the outcome + any new method-inventory deltas as §111-style
>    research-note section (coordinator renumbers), update this file's open
>    questions (007 histogram still open).
>
> **GATE OUTCOME (2026-10-13, wave/gpu-slice1 → §113): PASS.** All six
> acceptance points met on this host (NVIDIA GL 4.6.0, probe ok): both runs
> `LastFault vec=0`; `draws=33632 frames=686 stalls=686` identical sw↔gl;
> `BackendUnsupported=0` (named sub-counters all 0); BMP diff `violations=0`
> (`maxDelta=1`, all 2602 differing pixels inside the ±1px edge band, flat
> regions byte-exact, sw SHA `4EF1FBF6…` = the recorded §95/§109 dashboard
> frame); suite 1200/1200 green (1126 baseline unchanged) + format clean.
> Harness: `scripts/gpu-slice1-golden.ps1`. Three fidelity mechanisms the
> gate taught us are recorded in §113 (integer texelFetch sampling,
> software-exact barycentric in the fragment shader, coverage-superset
> inflation + shader inside-test for software's inclusive-edge double-blend).
>
> **SLICE-2 GATE OUTCOME (2026-10-13, wave/gpu-slice2 → §117): PASS.**
> Triangle golden (`scripts/gpu-slice2-triangle-golden.ps1`, anchor-pinned):
> both runs `LastFault vec=0`; `draws=2706 frames=68 stalls=34` identical
> sw↔gl and == recorded anchors; sw frame SHA `A1740DE0…` verified;
> BMP `violations=0 maxDelta=1` (27,849 edge-band pixels); gl frame
> `7A860ED7…` bit-identical before/after the wave's FBO change;
> `BackendUnsupported=0` (all seven `unsup[...]` rows zero on both titles).
> Dashboard menu golden re-verified **bit-identical to §113** (sw
> `4EF1FBF6…`, gl `B6116DAE…`, diff=2602 violations=0). Code delta:
> `diag[fboReallocs/fboZetaDrops/clipXY]` evidence counters + the
> content-preserving FBO realloc (glBlitFramebuffer color always,
> depth/stencil on unchanged zeta; failed creation keeps the prior FBO)
> — the §113 "FBO-grow window realloc" silent gap, now gate-validated
> live at 14 firings/boot with frame-neutral results on menu+triangle.
> Every other §113 tripwire row is evidence-justified (menu+triangle
> never exercise it; per-run counter output in the §117 disposition
> table). Suite 1205/1205 green (baseline unchanged), format clean.

## Evidence sources

- Research notes: §32, §37–§44, §57–§59, §67, §71, §74–§79, §87, §90–§91, §95–§97,
  §101, §103, §105–§106; AGENTS.md velocity program + M4–M8 status;
  `xemu-playtest-v2.md`, `envytools.md`, `nv2a-trace.md`.
- References (read-only): `xemu-0.8.136/hw/xbox/nv2a/pgraph/{methods.h.inc,pgraph.c,
  gl/draw.c,gl/shaders.c,gl/probe.c,glsl/{shaders.c,psh.c,vsh-prog.c,vsh-ff.c}}`,
  `nv2a_regs.h`; `xemu-Playtest-v2/hw/xbox/nv2a/pgraph/{cost.c,seed.c,writeback.c,
  glsl/uniform-cache.c,gl/probe.c}` + stock diff; Cxbx
  `src/devices/video/{nv2a_psh.cpp,nv2a_vsh.cpp,nv2a_shaders.cpp}` (structure);
  envytools `rnndb/graph/{nv10_3d.xml,nv20_3d.xml}` (names for inert rows).
- Own runs (this wave): triangle 1B boot with `--pgraph`/`--frame` (command §1.2);
  artifacts `triangle-boot.log`, `triangle-pgraph.txt`, `triangle-histogram.txt`,
  `triangle-frame.bmp`, `triangle-summary.txt` under
  `%TEMP%\opencode\gpu-backend-scope\`; `git diff --no-index --stat` of
  nv2a-stock vs nv2a; source greps of `src/XGrid.Nv2a/Nv2aMmio.cs` for the
  handled/inert classification (file left untouched).

## Open questions

1. 007 method histogram (CHD path + multi-billion-step reachability) — PARTIALLY CLOSED 2026-10-14 (§122): two bounded segment inventories captured from v2 snapshots (fmv `pd181.xgs`+1B: 3471 methods, zero draw-triggering; transition `pd536.xgs`+1B: 854 methods, 1 draw + 3 clears). A full offset-level histogram (the §1.2 procedure) is still open for the G2-parked title path, but no longer blocks any slice: both segments trip zero `unsup[` rows.
2. ~~GL binding library choice~~ — RESOLVED 2026-10-13: **Silk.NET.OpenGL**
   (operator; recorded in `sources.md`).
3. Absolute counter baselines to re-record post-§106 (§43/§95/§101 numbers noted
   as shifted) — coordinator/triage, not a backend blocker (acceptance is sw-vs-gl).
4. `0x1D80`/`0x1D84` names (UNK in both xemu-0.8.136 and envytools) — resolve via
   Playtest `methods.h.inc` or hardware trace if a title depends on them.
5. Whether the menu's gradient needs fog inside slice 1 or slice 2 (§95 frame pair
   is the deciding measurement) — **CLOSED for slice 1** (gate passed with fog
   carried as a uniform; the frame pair needed no fog-only fallback).
6. pbkit present hook (`NV_PGRAPH_INCREMENT 0x71C`) — slice-3 optional follow-up
   per §106.
7. **Slice-1 gate** — CLOSED 2026-10-13: PASS, see §8 GATE OUTCOME + §113.
   Slice-2 open items recorded there (swapchain out of scope, Vulkan later,
   exotic dot/bump/cube-success modes counted-but-unimplemented in the GLSL
   emitter with `BackendUnsupported` tripwires).
8. **Slice-2 gate** — CLOSED 2026-10-13: PASS, see §8 SLICE-2 GATE OUTCOME
   + §117. FBO-grow content loss: **fixed** (blit-preserving realloc,
   gate-validated, `diag[fboReallocs]` exposure 14×/boot on menu+triangle;
   zeta-format-change depth drops remain counted in `diag[fboZetaDrops]`).
   Exotic modes / cubeVol / zformat / colorBytes / oversized flips:
   evidence-justified out (zero exposure on both acceptance titles, §117
   table) — implement only when a title histogram proves usage (risk 1).
   clipY≠0 readback: exposure measured **zero** on menu+triangle
   (`diag[clipXY]=0`); detector armed for future titles. 16-word texture
   fingerprint: both golden pairs pass (the tripwire); interior-miss
   residual risk stays documented. Multi-stage combiner worry: closed —
   the triangle stream is single-stage like the menu.

> **SLICE-3 GATE OUTCOME (2026-10-14, wave/gpu-slice3 → §122): PASS.**
> 007 harness (`scripts/gpu-slice3-007.ps1`, fmv + transition segments):
> fmv `draws=2672 frames=730 stalls=730` identical sw↔gl, end EIP
> `0x2139C` both, backend `draws=0 clears=0 flips=39 methods=3471
> flipEmpty=39==flips timed=0 BackendUnsupported=0`, sw frame SHA
> `45835626…` reproduced exactly across builds, gl frame correctly
> absent; transition `draws=2673 frames=2017 stalls=2017` identical,
> end EIP `0x1C302C` (the §118 G2 park) both, backend `draws=1
> clears=3 flips=1 methods=854 flipEmpty=0`, sw SHA `18C7CC83…` ==
> §114 oracle, gl byte-identical. Menu + triangle re-verified green
> on the final build (SHAs bit-identical to §113/§117; triangle gl
> `7A860ED7…` re-confirms driver determinism). Code delta:
> `diag[flipEmpty]` empty-FBO-flip counter (the one silent flip path,
> gate-asserted on both 007 segments) + `GlBackendTimings`
> draw/clear/readback accumulators (prior session, reviewed) feeding
> the §122 headless fps table (GL path 0–2.9% of wall → swapchain is
> latency, not throughput; present-upload seam follow-up reported).
> Suite 1222/1222 green (1216 baseline + 6 timing tests), format clean.
