# GPU reference map (XGrid): which reference answers which NV2A question

Status: **orientation note, 2026-10-20.** Companion to `nv097-coverage.md`.
It is the "where do I look" index for GPU work, not a fidelity claim, not a gate,
and not a development-plan change. Facts only - no code is copied from the GPL
references (see `sources.md`).

## The four usable layers

### 1. xemu 0.8.137 - primary LLE oracle

Path: `References2/xemu-0.8.137/` (0.8.136 stays vendored as the prior snapshot;
the 0.8.137 delta is catalogued in `xemu-0.8.137.md`).

| Path | Covers |
|---|---|
| `hw/xbox/nv2a/pgraph/methods.h.inc` | the complete NV097 method table: offset -> handler macro, including the ranges (arrays/elements/inline, vertex data, transform programs/constants, combiners, textures, surfaces, swizzles, DMA contexts) |
| `hw/xbox/nv2a/pgraph/nv2a_regs.h` | register/method names + bitfield constants (surface format/pitch/clip, DMA context bases, PFIFO/PGRAPH/PMC interrupt bits, PTIMER) |
| `hw/xbox/nv2a/pgraph/nv2a.c` | method dispatch entry, PGRAPH interrupt aggregation, `SET_BEGIN_END` drain |
| `hw/xbox/nv2a/pgraph/draw.c` | draw setup: vertex fetch/format decode, primitive assembly, the `NV097_SET_BEGIN_END` path |
| `hw/xbox/nv2a/pgraph/vertex.c` | attribute formats (`UB_D3D`, `UB_OGL`, `S1`, `F`, `S32K`, `CMP`), count/stride, inline arrays |
| `hw/xbox/nv2a/pgraph/vsh.c`, `vsh-prog.c` | vertex-program decode + interpreter (XF: MAC/ILU opcodes, R/V/C muxes, swizzles/negates, write masks, final oPos/oD0/oT0, A0-relative constants) |
| `hw/xbox/nv2a/pgraph/psh.c` | register-combiner state (stages, inputs/outputs, factors, final combiner, alpha test, fog, zclip) and the fragment semantics XGrid's `PshCombiner` transcribes |
| `hw/xbox/nv2a/pgraph/texture.c` | texture layout (swizzled vs linear), formats, mipmap levels, border color, cube/volume modes |
| `hw/xbox/nv2a/pgraph/surface.c` | color/zeta surface state, clear values, surface copy/blit, flip read/write/modulo |
| `hw/xbox/nv2a/pgraph/transfer.c` | `NV097_LAZY_SWIZZLE`/transfer, writeback, `WAIT_FOR_IDLE` |
| `hw/xbox/nv2a/pfifo.c` | pusher/puller, DMA context object mode, RAMHT handles, semaphores, CACHE1 |
| `hw/xbox/nv2a/ptimer.c` | PTIMER rewrite in 0.8.137: 61-bit time with writable offset, 27-bit-masked `ALARM_0` re-armed on wrap, `INTR_0` latch, `NV_PMC_INTR_0_PTIMER` (bit 20) |
| `hw/xbox/nv2a/glsl/*` | the host GLSL translation (`psh.c`, `vsh.c`, shader cache) - useful as a cross-check for our own `PshGlslGenerator`, never as a source |
| `hw/xbox/nv2a/vk/*` | the newer Vulkan renderer (0.8.137 adds a nearest-filter fix); behavior parity with GL, not a separate oracle |

`xemu-Playtest-v2` (`References2/xemu-Playtest-v2`, QEMU 10.2 base) carries the
*tuned* NV2A (`pgraph/cost.c`, `seed.c`, `writeback.c`, `gl/probe.c`,
`glsl/uniform-cache.c`) with the un-tuned baseline at `hw/xbox/nv2a-stock/`.
`diff -ru` isolates the performance delta; cross-check only, 0.8.137 is primary.

### 2. envytools - MIT register/method database + GPU docs

Path: `References2/envytools-master/`.

| Path | Covers |
|---|---|
| `rnndb/graph/nv20_3d.xml` | NV20 3D method/register definitions (Xbox class `0x0097` uses the NV20 method space): offsets, types, enums |
| `rnndb/graph/nv10_3d.xml` | NV10 base methods shared upward (begin/end, arrays, inline, VP upload) |
| `docs/hw/fifo/dma-pusher.rst` | how a pushbuffer is produced (methods, jump, call, return, ref) |
| `docs/hw/fifo/puller.rst` | how the puller consumes it (context switch, prefetch, subchannels) |
| `docs/hw/graph/xf/isa.rst` | XF/vertex-program instruction encoding and semantics (MAC/ILU, muxes, write masks) |
| `docs/hw/graph/xf/mode.rst` | XF control/mode registers, viewport/clip, `NV097_SET_TRANSFORM_*` semantics |
| `docs/hw/graph/` (other) | texture address/layout, surface, zeta, ROP state notes |

Caveat: envytools' own "NV2A" is the nForce IGP, not the Xbox GPU. Method
offsets/enums are shared; some Xbox-specific behavior is not documented there.

### 3. nv2a-trace - MIT, real-hardware tracer

Path: `References2/nv2a-trace-master/`.

| Path | Covers |
|---|---|
| `XboxHelper.py` (`parse_command`) | pushbuffer command decoder (method vs jump/call/return, subchannel, count) |
| `Nv2a.py`, `Constants.py` | NV2A/CACHE1 register constants as observed on hardware |
| `Texture.py` | texture format table (color formats, bytes per texel, swizzle) |
| `Debug.py`, `debug.html` output | per-method pre/post state, surface/texture PNG dumps, draw call log |

Needs a console + `xboxpy`; without hardware, the decoder/constants/format table
are the reusable facts. It is the only *hardware ground truth* for PGRAPH
behavior in the reference set.

### 4. XboxDevWiki + Cxbx-Reloaded - documentation and driver behavior

| Path | Covers |
|---|---|
| `References2/XboxDevWiki/pages/<Title>.wiki` | Boot Process, MCPX, PCI, NV2A, USB, SMC, Memory, FATX, XBE prose; identify pages by `<title>` (`html/` is the rendered offline copy) |
| `References2/Cxbx-Reloaded-master/src/core/kernel/common/types.h` | NT-layout kernel structures (KTHREAD/KWAIT_BLOCK/KAPC offsets) - the NT-layout oracle |
| `References2/Cxbx-Reloaded-master/src/devices/video/nv2a_psh.cpp`, `nv2a_vsh.cpp` | shader semantics as the Cxbx HLE driver implements them (a cross-check for `PshCombiner`/`VshProgram`, GPL - facts only) |

Secondary: `References2/xqemu-master` (older QEMU fork, simpler code paths),
`References2/CopettiDocumentation` (essay level, CC BY 4.0, attribution required).

## Where to look for a specific question

| Question | First file |
|---|---|
| Is method `0xNNNN` supported, and what does it do? | `xemu-0.8.137/hw/xbox/nv2a/pgraph/methods.h.inc`, then `nv2a_regs.h` for the name |
| What do a method's bits/enums mean? | `envytools-master/rnndb/graph/nv20_3d.xml` (+ `nv10_3d.xml`), cross-check `nv2a_regs.h` |
| Draw/clear/flip semantics, surface state, zeta format | `pgraph/draw.c`, `pgraph/surface.c`, `pgraph/transfer.c` |
| Vertex program (XF) decode/execute | `pgraph/vsh.c`, `pgraph/vsh-prog.c`, `envytools docs/hw/graph/xf/` |
| Fragment combiner/blend/depth/alpha-test/fog | `pgraph/psh.c` (semantics), `glsl/psh.c` (translation cross-check) |
| Vertex fetch formats, inline/element/array draws | `pgraph/vertex.c`, `methods.h.inc` (`NV097_ARRAY_ELEMENT16/32`, `NV097_INLINE_ARRAY`, `NV097_SET_BEGIN_END`, `NV097_SET_VERTEX_DATA*`) |
| Texture layout, swizzle, formats, mipmaps, border | `pgraph/texture.c`, `nv2a-trace Texture.py`, envytools `.xml` format enums |
| FIFO/pusher/puller, DMA contexts, RAMHT, semaphores | `envytools docs/hw/fifo/*.rst`, `xemu hw/xbox/nv2a/pfifo.c` |
| PTIMER / PMC interrupt wiring | `xemu-0.8.137/hw/xbox/nv2a/ptimer.c`, `nv2a.c` (see `xemu-0.8.137.md`) |
| A real hardware trace of a method stream | `nv2a-trace` (`debug.html`, per-method state diff) - needs a console |
| Boot/kernel/SMC/MCPX/USB prose | `References2/XboxDevWiki/pages/<Title>.wiki` |
| What a game *actually uses* | `docs/research-notes/nv097-coverage.md` (method presence) + `--pgraph` dumps + the histogram rule in `docs/compatibility.md` |

## Corrections worth keeping

- There is **no official NV2A ISA or datasheet**; NVIDIA never published one for
  the Xbox GPU. Everything is inferred from NV20/NV25 material plus traces.
- There is **no fragment-program ISA**: the NV2A fragment stage is the register
  combiner (`psh`), configured by `NV097_SET_COMBINER_*` and modeled by
  `PshCombiner`. Do not look for a "fragment shader binary".
- There is **no GPU microcode** to emulate.
- xemu is **LLE on command/state and HLE on execution**: it decodes and tracks
  the real register model, but vertex programs and the combiner are interpreted
  on the host. It is not a cycle-accurate GPU.
- envytools documents **NV20/NV25** (nForce IGP), not the Xbox-specific NV2A
  stepping; method offsets and enums are shared, some Xbox behavior is not.
- The Xbox D3D driver generates vertex programs for fixed-function lighting, so
  a metadata method that XGrid "records but does not consume" is not
  automatically a bug (`nv097-coverage.md` states this per group).

## How this maps onto XGrid

| XGrid file | Role | Reference side |
|---|---|---|
| `src/XGrid.Nv2a/Nv2aMmio.cs` | command/state model + software rasterizer (accurate-mode oracle) | `methods.h.inc`, `draw.c`, `surface.c`, `psh.c`, `texture.c` |
| `src/XGrid.Nv2a/VshProgram.cs` | vertex-program interpreter | `vsh-prog.c`, `xf/isa.rst` |
| `src/XGrid.Nv2a/PshCombiner.cs` | fragment combiner/pipeline semantics | `psh.c` |
| `src/XGrid.Nv2a/IPgraphBackend.cs` | GPU-backend seam (`Draw`/`Clear`/`Flip`/`TryDraw`) | design note `gpu-backend-design.md` |
| `src/XGrid.GpuGl/GlPgraphBackend.cs` | host-GPU backend: state mapping, draw submission, present | `glsl/*`, `gl/probe.c` |
| `src/XGrid.GpuGl/PshGlslGenerator.cs` | GLSL from `PshCombiner` (string goldens under test) | `glsl/psh.c` (cross-check) |
| `src/XGrid.GpuGl/GlStateMapper.cs` | blend/depth/stencil/mask mapping | `psh.c` ROP state |
| `src/XGrid.GpuGl/WglContext.cs` | WGL context + opt-in window | - (host side) |
