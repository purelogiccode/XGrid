# envytools — NVIDIA register/method database and GPU docs

Reference: `References2/envytools-master` (upstream: https://github.com/envytools/envytools)
License: **MIT** (root `COPYING`) — copy-permissive; still facts-only by default (see
`sources.md` rule: record before any programmatic reuse).

Why it matters: xemu/Cxbx give *implementations* of NV2A behavior; envytools gives the
*specification* — a machine-readable register/method/enum database plus prose on the FIFO
pusher/puller and the XF transform unit. Use it to cross-check method offsets, enum values
and pipeline semantics without reading GPL code.

## Contents

| Path | What it holds |
|---|---|
| `rnndb/` | rules-ng-ng XML database: MMIO registers, per-class methods, enums, bitfields |
| `docs/hw/fifo/` | DMA pusher / puller / PFIFO prose (`dma-pusher.rst`, `puller.rst`, `nv1-pfifo.rst`) |
| `docs/hw/graph/` | PGRAPH prose (`intro.rst`, `bundles.rst`) + per-family dirs + `xf/` (transform unit) |
| `docs/hw/bus/` | `pmc.rst` (IRQ/enable/pending), `ptimer.rst`, `pbus.rst`, `pci.rst` |
| `docs/hw/memory/` | VRAM/swizzle/peephole docs (`peephole.rst`, `nv1-vram.rst`, `vram.rst`) |
| `rnn/` | XML query/decode helpers (`rnndec.py`, `lookup.py`, `rnn.py`) |
| `demmt/` | MMIO trace decoder (GPU register log → readable methods) |
| `envydis/`, `vstream/` | GPU ISA disassemblers |

## The Xbox GPU in this database (naming caveat)

- envytools' own **"NV2A" is the NVIDIA nForce IGP** (`docs/glossary.txt`), *not* the Xbox GPU.
- The Xbox 3D object class is `0x0097`, listed in `rnndb/fifo/nv_object.xml` as
  `NV20_3D` (`variants="NV20:NV34"`), i.e. the Kelvin family — the same class Cxbx/xemu
  call **NV097**.
- Entry point: `rnndb/graph/nv20_3d.xml` (imports `graph/nv10_3d.xml`, which imports
  `nv_3ddefs.xml` + `fifo/nv_object.xml`). Method offsets for the Xbox live in that chain.

## Method layout syntax

There are no `<method>` tags. Methods are `<reg32 offset="..." name="...">` entries inside a
`<stripe prefix="obj-class" ...>` block; enums/bitfields are separate `<enum>` elements with
GL-style names from `nv_3ddefs.xml` (`gl_blend_factor`, `gl_comparison_op`, `gl_stencil_op`,
`gl_blend_equation`) — direct cross-checks for XGrid's enum tables.

Verified offset matches against XGrid/NV097 (search `rnndb/graph/nv20_3d.xml`,
`nv10_3d.xml`):

| Offset | envytools name | XGrid/NV097 name |
|---|---|---|
| `0x1500` | `VERTEX_POS_3F` | vertex position input |
| `0x17FC` | `VERTEX_BEGIN_END` | `SET_BEGIN_END` |
| `0x1800` | `VTXBUF_ELEMENT_U16` | `ARRAY_ELEMENT16` |
| `0x1810` | `VTXBUF_BATCH` | `DRAW_ARRAYS` |
| `0x1818` | `VTXBUF_DATA` | `INLINE_ARRAY` |
| `0x0B00` | `VP_UPLOAD_INST` (length 4) | `SET_TRANSFORM_PROGRAM` |
| `0x0B80` | `VP_UPLOAD_CONST` (length 4) | transform constant upload |
| `0x01A4` / `0x01A8` | `DMA_FENCE` / `DMA_QUERY` | DMA object attrs |
| `0x0304` | `BLEND_FUNC_ENABLE` (`nv10_3d.xml`) | blend enable |
| `0x032C`, `0x0360`-`0x0378` | `STENCIL_ENABLE`, `STENCIL_MASK/FUNC_*/OP_*` | stencil state |
| `0x1D78` | `DEPTH_CLAMP` | ZCLIP clamp |

(Also relevant to §97's ZCLIP work: `0x1D78` is present here; a `0x1D70`
back-end-write-semaphore method is *not* defined in these two files — use xemu/Cxbx for it.)

## Prose worth reading before touching these subsystems

- **Pusher:** `docs/hw/fifo/dma-pusher.rst` — pushbuffer → command stream, jump/call/return,
  GET/PUT, method counting, how methods land in CACHE for the puller. Mirrors XGrid's PFIFO
  pusher (`CACHE1`, DMA jump/call/return/inc, GET writeback).
- **Puller/CACHE:** `docs/hw/fifo/puller.rst` — object lookup (RAMHT analogue), method
  execution, notifications.
- **XF (transform unit):** `docs/hw/graph/xf/`
  - `intro.rst` — XF stages: position transform (two matrices), texgen modes, lighting (LT),
    fog; exists from NV10 onward, replaced by unified shaders at G80.
  - `isa.rst` — **VLIW instruction set**: one insn reads one IBUF slot + one XFCTX
    (constant) slot + three sources, each with independent source select, swizzle, abs/negate
    (NV30+). This is the documented shape of what `XGrid.Nv2a/VshProgram` executes — good
    cross-check for mux/swizzle/mask handling and for the c56-c59/c64-95 XF-context constant
    writers (§71).
  - `mode.rst` — XFMODE vector (NV10:NV40), mode bits shipped to XF on change.
  - `ctx.rst` — XF context layout/state.
- **Bundles:** `docs/hw/graph/bundles.rst` — FE→unit data bundles (9-bit type on Kelvin);
  explains data that flows through the pipeline unchanged.
- **PMC/PTIMER:** `docs/hw/bus/pmc.rst`, `ptimer.rst` — enable/pending/intr semantics for
  the PMC block XGrid wires to the PIC (NV2A IRQ path).
- **Swizzle/peephole:** `docs/hw/memory/peephole.rst`, `nv1-vram.rst` — tiled/swizzled
  address mapping background behind Morton-order texture sampling (§40) and surface access.

## Not useful here

- `docs/hw/graph/kelvin/3d.rst` is an empty `.. todo::` stub — no Kelvin 3D method prose.
- g80/GF100+ material (Tesla/Fermi/Kepler/Maxwell, `g80_*`, `gf100_*` XML) is irrelevant to
  NV2A; `rnndb` name `NV2A` = nForce IGP (see above).

## How to use it

1. Look up an unknown method offset: grep `rnndb/graph/nv20_3d.xml` then `nv10_3d.xml`.
2. Confirm enum/field values: `<enum>` blocks + `nv_3ddefs.xml` (GL-named tables).
3. Before changing pusher/puller/notify behavior, read `fifo/dma-pusher.rst` + `puller.rst`
   and compare against xemu `hw/xbox/nv2a/pfifo/`.
4. For XF/vertex-program semantics, read `graph/xf/isa.rst` + `mode.rst` against
   `VshProgram` and the §71/§78/§91/§96/§97 interpreter work.
5. `rnn/rnndec.py` shows how to decode MMIO/method logs programmatically if the debugger
   (plan Phase 19) ever wants a shared decoder — MIT, but record reuse in `sources.md`.
