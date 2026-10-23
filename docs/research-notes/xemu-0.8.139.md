# xemu 0.8.139 - primary reference snapshot (2026-10-23)

Adopted 2026-10-23 as the primary reference.  The 0.8.137 and 0.8.136 trees
remain vendored as prior snapshots.  Runtime evidence:
`docs/research-notes/xemu-menu-oracle.md` (0.8.139 boots the XBMC dashboard
from `data/xemu`).

## Identity

- `xemu_version: 0.8.139`
- `xemu_commit: aa54c34cae1c09410d74c69a44210a9867fe2d16`
- `xemu_date: Wed Oct 21 21:40:16 UTC 2026`
- QEMU base: 10.2.0 (same generation as 0.8.137).
- License: GPL-2.0 overall; `hw/xbox/**` per-file LGPL-2+/MIT/GPL-2+; the
  `mcpx/apu/dsp/**` and GLSL shader generators are GPL-2+.  XGrid is GPL-2.0
  and may port with notices (`docs/porting-policy.md`).

## Delta vs 0.8.137

The APU/DSP subtree is **byte-identical** to 0.8.137 (same file sizes and
hashes), so the DSP port source is the same.  The material delta is NV2A:

- 37 `hw/xbox` files differ; the changed set is concentrated in
  `hw/xbox/nv2a/`: `pgraph.c`, `pgraph.h`, `pfifo.c`, `nv2a.c`, `nv2a_regs.h`,
  `pgraph/vertex.c`, `pgraph/texture.c`, `pgraph/glsl/{psh.c,vsh.c,vsh-ff.c,
  vsh-prog.c,vsh.h}`, the GL backend (`pgraph/gl/*`) and the Vulkan backend
  (`pgraph/vk/*`).
- New files: `hw/xbox/nv2a/debug.c`, `debug_gl.h`,
  `pgraph/null/renderer.h`.
- The new/changed NV2A code is a compatibility/performance cross-check, not part
  of the first port slice.  Read the changed files before porting any NV2A
  method or shader behavior; do not assume 0.8.137 semantics where 0.8.139
  differs.

## Porting priority

1. APU GP/EP DSP (`hw/xbox/mcpx/apu/dsp/`) - active P1; unchanged from 0.8.137.
2. NV2A missing methods/shaders - use the 0.8.139 delta above as the source.
3. APU VP mixer/PCM sink - `hw/xbox/mcpx/apu/vp/vp.c` and `apu.c`.

The CPU TCG is not a port target: XGrid's raw emitter is already bit-exact and
faster than xemu on the CPU-bound ktest; the measured gap is the
boot/device/GPU path.
