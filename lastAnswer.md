# Oracle assessment for XGrid — 2026-10-20

Yes — but most of the folders you listed are *references*, not oracles. An oracle has to be executable or independently authoritative, so you can diff against it. Here's the honest ranking of what you already have, then the oracles you don't have yet.

## What you already have

| Reference | Oracle value | What it can decide |
|---|---|---|
| **xemu-0.8.137** | **High — and new** | You added it 10-19; it's a substantially newer snapshot than 0.8.136: **150 of 167 `hw/xbox` files differ**, and it adds an optional **DSP JIT** (`mcpx/apu/dsp/dsp_jit.c`, 320 lines) plus a restructured DSP core (`dsp.c` ops vtable, `interp/`, 441-line `gp_ep.c`). **Make this your primary, not 0.8.136.** Caveat unchanged: xemu deadlocks at the dashboard animation wait, so it only oracles the run-up. |
| **Cxbx-Reloaded** | Medium | HLE, so never frame-exact. Valid for: Nt* kernel-thunk semantics, XBE loader/relocation, and D3D8→NV2A shader translation (`nv2a_psh.cpp`/`nv2a_vsh.cpp`). It also runs titles xemu can't — useful as a *behavior* oracle for kernel paths. |
| **envytools** | Medium (definitions, not behavior) | `nv20_3d.xml` method/register database, pusher/puller semantics, XF ISA. But its own “NV2A” is the nForce IGP, so treat it as a cross-check for offsets/enums, not a behavior oracle. |
| **nv2a-trace** | Medium only *with hardware* | The repo is tooling, not a trace corpus. Without a box it still gives you `XboxHelper.parse_command` (a second independent FIFO decoder to validate yours), `Texture.py` format tables and `kick_fifo`. Its real value is when someone runs it on a modded Xbox. |
| **XboxDevWiki** | Medium (facts) | The **APU/DSP pages are substantive**: DSP cores identified as Parthus/Motorola **DSP56300-class**, GP/EP memory maps, MIXBUF at `X:$001400`, DMA registers, VP voice feature list. Also NV2A combiner/surface/vertex pages, MCPX/SMC/PCI/USB. It's a spec oracle, not a behavior oracle. |
| **xemu-Playtest-v2** | Low for Xbox | Chihiro fork on QEMU 10.2. Its value is the *tuned pgraph vs `nv2a-stock` diff* and newer NV097 methods (cross-check), not title execution. |
| **xqemu-master** | Low | Old QEMU tree; only simpler device paths, heavily superseded. |
| **CopettiDocumentation** | None | Architecture essay; already correctly marked as non-oracle. |

## Oracles you don't have (ranked by value)

1. **A physical Xbox + `xboxpy` + `nv2a-trace`** — the only ground truth for PGRAPH, APU, SMC and timing. A used console + modchip is ~$60–120. This is the single biggest unlock for the remaining GPU/NV2A and APU work, and it also gives you audio/frame captures. If buying one is impossible, ask the xemu/nv2a-trace community for **trace corpora** — those files are reusable input for `nv2a-trace`.
2. **A CPU instruction oracle: Bochs or QEMU i386 user-mode.** This costs nothing and directly de-risks the raw JIT: execute the same blocks with identical register/memory state on XGrid and Bochs/QEMU, compare architectural state after each block. That is a *differential,* not a golden — exactly what you need for the last C3-z and future shape work. (Watch CPUID/segment/SMM differences.)
3. **pbkit's GPU test suite** (`pbkit/tests`) as sanctioned guest-side conformance: surfaces, textures, shaders, depth, blending — run the XBEs on XGrid vs xemu/hardware. This fits your "sample content only" rule and is far more systematic than the current four samples. The **nxdk toolchain** is the blocker; getting it is a force multiplier for both this and `nxdk-mesh`.
4. **Linux in-kernel drivers as register-semantics oracles** (facts only, never code): `forcedeth.c` → nvnet, `snd-intel8x0.c` → AC'97/ACI, `ohci-hcd.c` → OHCI, `ata_piix.c`/`pata_amd` → IDE/BM-DMA, `i2c-nforce2.c` → SMBus. These are close siblings of the MCPX blocks and fill exactly the "every register bit" gaps the wiki leaves as FIXME.
5. **The NXP DSP56300 family manual** (linked from the wiki DSP page) — the real ISA spec for GP/EP. Combined with xemu 0.8.137's DSP interpreter/JIT, that's your APU path: spec + reference implementation + a playback oracle.
6. **Mesa/nouveau (nv30/nvfx)** for combiner/shader math cross-checks, and optionally the **D3D8 reference rasterizer** for synthetic fixed-function/shader tests. Both are weaker than envytools for NV2A specifics.
7. **A 480p capture card + real console** for visual/audio regression oracles — not bit-exact, but catches the "right numbers, wrong picture" class.

## Practical next step

- **Re-base the reference notes on 0.8.137** and diff `hw/xbox/mcpx/apu/dsp/` and `hw/xbox/nv2a/` against .136 — the DSP JIT and the NV2A deltas are new facts for exactly the two phases that dominate your remaining 20%.
- Add the **Bochs/QEMU block differential** now; it needs no hardware and pays for itself in JIT confidence.
- If you can spend a little money, the **modded Xbox** is the highest-leverage purchase for this project — it converts `nv2a-trace`, `xboxpy`, the APU and SMC from "documented" to "measurable".

One licensing note that matters for all of the above: xemu, Linux and Mesa are GPL — facts and behavior only, no code. envytools, nv2a-trace and xqemu are MIT; Cxbx is a mix. Your existing "extract facts, never copy" rule already covers this.
