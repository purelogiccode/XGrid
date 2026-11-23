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

The program is the **full port** (`ImplementationPlan.md` E1, ledger
`docs/xemu-port-manifest.md`), ordered by what unblocks a retail title:

1. **NV2A PGRAPH + the GL renderer** (`nv2a/pgraph/**`, the `nv2a/*` blocks) -
   XFP1-XFP5. Halo's D3D resource wait, Ninja Gaiden Black's fragment dither and
   every visual row depend on it.
2. **APU front end + AC'97 codec** (`mcpx/apu/apu.c`, `mcpx/aci.c`,
   `hw/audio/ac97.c`) - XFP6. The VP mixer (`vp/vp.c`) and the DSP
   (`apu/dsp/`) are already ported and verified; the AC'97 path makes the
   output audible.
3. **IDE/block, SMBus/SMC/EEPROM, nvnet, USB/OHCI/XID** - XFP7-XFP9.
4. **Machine/chipset** - XFP10.
5. **CPU/memory core** (`softmmu/`, `accel/tcg/`, `tcg/`, `target/i386/tcg/`) -
   XFP11, in scope but sequenced last and gated on "not slower" than the
   current bit-exact raw engine on the 1B/ktest workloads.
6. **Host backends and snapshots** - XFP12-XFP13; **parity close** - XFP14.

Vulkan (`nv2a/pgraph/vk/`) is a host-renderer choice and is out of scope; the GL
renderer is the port target. Chihiro and QEMU user-mode/packaging
infrastructure are out of scope with reasons in the ledger.
