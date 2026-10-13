# xemu-Playtest-v2 — in-development xemu fork (Sega Chihiro arcade bring-up)

Reference: `References/xemu-Playtest-v2` (operator-supplied local snapshot, 2026-09-28;
no upstream URL recorded — ask the operator before re-downloading).
License: xemu-family — GPL-2.0 overall; per-file LGPL-2+ (most `hw/xbox/**`, including
`nv2a-stock`), MIT (`smbus_xcalibur.c` etc.), GPL-2+ (shader/GLSL files). **Facts only.**

What it is: a fork of xemu on a **QEMU 10.2.0 base** (`QEMU_VERSION`) with the xemu UI
(`ui/xemu*.c/cc`) intact, in development to bring up the **Sega Chihiro** arcade board
(operator statement). New files carry a 2026 copyright (Réda Chérif-Touil).

## What differs from `xemu-0.8.136` (`diff -rq hw/xbox`)

- `hw/xbox/chihiro/` — a full Chihiro subsystem (vs the two single files in 0.8.136):
  `chihiro.c`, 8051 / V850 / MIPS CPUs, ASIC, JVS, card readers (CRP1231, HW210),
  drive boards (SEGA838, V257), netboard, PIC, SPD, USB, DES.
- `hw/xbox/nv2a/` is the **"tuned GPU"** with performance work; `hw/xbox/nv2a-stock/`
  is the same GPU **without** it. Per `stock-names.h`: "GPU boost" off at start-up plugs
  the stock one in, and `diff -ru hw/xbox/nv2a-stock hw/xbox/nv2a` shows every change.
- New NV2A perf modules (facts):
  - `pgraph/cost.c/h` — per-frame cost model: per-segment (run of draws sharing one
    register-derived per-fragment cost profile) fragment counting via backend queries,
    resolved at the flip.
  - `pgraph/seed.c/h` — shader seeds: embedded dictionary (`data/shader_seeds.bin`) +
    per-game `shader_seeds/<game>.seed` (XSED format), so shader families are (almost)
    never compiled inside a frame; an unseeded known family draws with a dynamic sibling
    while the exact program compiles.
  - `pgraph/writeback.c/h` — heuristic surface writeback: an evicted surface whose
    writeback no reader is known to need parks its binding (live ranges / discard spans).
  - `glsl/uniform-cache.c/h` — sectioned vertex-shader uniforms (vshc/ltctx/light/pp/fogp)
    uploaded only when dirty; per-program last-value diffing.
  - `gl/probe.c` — one-time driver self-test (separable stage programs, pipelines,
    uniforms, textured draw, texture copy) that gates the optimization pack.
- New devices: `xid-light-gun.c`; SMBus `smbus_adm1032.c` (temp sensor),
  `smbus_cx25871.c` (video decoder), `smbus_fs454.c`, `smbus_xcalibur.c`.
- NV097 method coverage: 5 methods newer than 0.8.136 —
  `SET_LINE_WIDTH`, `SET_BACK_SCENE_AMBIENT_COLOR` (range 3),
  `SET_BACK_MATERIAL_ALPHA`, `SET_BACK_MATERIAL_EMISSION` (range 3),
  `SET_TWO_SIDE_LIGHT_EN`.

## How it helps XGrid

1. **GPU-backend performance track (primary use).** The `nv2a` vs `nv2a-stock` diff is a
   complete catalog of proven NV2A host-GPU optimizations — cost model, shader-seed
   caching, uniform caching, writeback heuristics, driver gating. Extract the *techniques*
   (facts), not the code, when scoping XGrid's OpenGL/Vulkan backend.
2. **NV2A method coverage cross-check** — the 5 extra NV097 methods above; confirm whether
   XGrid needs them (lighting state; the dashboard may already exercise them).
3. **Chihiro is arcade-only** — JVS/card-reader/drive-board devices are not needed for
   console Xbox; keep as a future arcade-track reference only.
4. **New XID/SMBus devices** (light gun, video decoder, temp sensor) for completeness if a
   title ever needs them.

## Caveats

- In-development fork, not an official xemu release; behavior may be ahead of or diverge
  from upstream. Use as a cross-check, not as the primary oracle — `xemu-0.8.136` remains
  the operator-confirmed primary LLE reference.
- QEMU base is not a dependency (same rule as 0.8.136).
- Local snapshot only: no recorded upstream URL; do not re-download from an unverified
  source.
