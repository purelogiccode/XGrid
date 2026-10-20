# xemu 0.8.137 - primary oracle delta (2026-10-20)

Reference: `References2/xemu-0.8.137` (QEMU fork, QEMU 10.2.0 base, 11,352 files).
License: GPL-2.0 overall; `hw/xbox/**` per-file LGPL-2+ / MIT / GPL-2+; the
`mcpx/apu/dsp/**` subtree is GPL-2+ (including the new `dsp_jit.c`). Facts only -
no code, tables or comments copied.

Status: **primary LLE oracle from 2026-10-20**. `References2/xemu-0.8.136` stays
vendored as the prior snapshot so historical citations in
`xgrid-architecture-decisions.md` and the other research notes remain traceable.

## Why the switch

Every fact recorded from 0.8.136 should now be re-checked against 0.8.137 before
it is used for new work. The measured delta (`Get-FileHash` over both trees):

- `hw/xbox`: 164 -> 167 files; **150 files differ or are new**.
- `hw/xbox/nv2a`: 88 of 97 files differ.
- `hw/xbox/mcpx`: 38 files differ, including the APU DSP restructure below.
- QEMU base moves to 10.2.0 (the same generation as the `xemu-Playtest-v2` fork),
  so QEMU framework facts also need re-reading if the 0.8.136 line is cited.

## The material addition: APU GP/EP DSP

0.8.136 already carried a DSP interpreter (`dsp_cpu.c`, `dsp_emu.c.inc`,
`dsp_dis.c.inc`, `dsp_dma.c`, `gp_ep.c`). 0.8.137 restructures it and adds the
pieces that matter for phase 12:

| Path (0.8.137) | Lines | What |
|---|---:|---|
| `hw/xbox/mcpx/apu/dsp/dsp.c` | 196 | DSP core with an ops vtable; selects the interpreter or the JIT via `g_config.audio.use_dsp_jit`; frame/interrupt entry points |
| `hw/xbox/mcpx/apu/dsp/dsp_jit.c` | 320 | **New** optional JIT for the DSP core |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | 1,176 | DSP56300-class interpreter (moved from `dsp_cpu.c`; content differs) |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc` | - | Per-opcode semantics (moved; content differs) |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_dis.c.inc` | - | Disassembler (moved; content differs) |
| `hw/xbox/mcpx/apu/dsp/gp_ep.c` | 441 | GP/EP bootstrap, run, DMA and interrupt glue (updated) |
| `hw/xbox/mcpx/apu/dsp/dsp_internal.h` | - | New core/JIT internals split |

`hw/xbox/mcpx/apu/vp/` still carries `adpcm.h`, `hrtf.h` and `svf.h`; the
XboxDevWiki `APU.wiki` + `DSP.wiki` pages (DSP56300-class cores, GP/EP memory
sizes, MIXBUF at `X:$001400`) remain the architectural cross-check.

**Consequence for XGrid:** the existing GP/EP bridge (§121) reproduces the
handshake and notifier behaviour but does not execute the DSP programs. The full
interpreter/JIT is now available as the reference for implementing DSP execution,
mixing and host output - the remaining 45% of phase 12. Extract semantics
(instruction behaviour, DMA command blocks, frame timing), not code.

## Other deltas to re-check before reuse

- **NV2A:** 88/97 files differ. Re-read `pgraph/methods.h.inc`, `pgraph/pgraph.c`,
  `pgraph/texture.c`, `pgraph/surface.c`, `pgraph/glsl/psh.c`,
  `pgraph/glsl/vsh-prog.c` and `nv2a_regs.h` before leaning on a 0.8.136 line.
  The project's method inventory/coverage already spans 0.8.136 + Playtest; a
  three-way diff is the right way to spot genuinely new methods.
- **MCPX/SMBus/USB/XID:** 38 files differ; re-verify SMC/EEPROM/flash register
  behaviour and XID report shapes.
- **PCI/IDE/ACPI:** re-verify topology/IDs and IRQ routing if a 0.8.136 line is
  cited; the machine model files also differ.

## Process

- Consult `References2/xemu-0.8.137` first (AGENTS.md, ImplementationPlan.md and
  `sources.md` now say so). Keep citing the snapshot you actually read.
- When a research note cites `xemu-0.8.136` for a behaviour that matters to the
  task, re-extract it from 0.8.137 and record the new file:line in the note.
- `xemu-Playtest-v2` stays a cross-check only (Chihiro fork); it is not the
  primary oracle for Xbox content.
