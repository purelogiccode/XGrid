# xemu 0.8.137 — primary oracle delta (2026-10-20, re-based)

Reference: `References2/xemu-0.8.137` (QEMU fork, QEMU 10.2.0 base, 11,352 files).
License: GPL-2.0 overall; `hw/xbox/**` per-file LGPL-2+ / MIT / GPL-2+; the
`mcpx/apu/dsp/**` subtree is GPL-2+ (including the new `dsp_jit.c`). Facts only -
no code, tables or comments copied.

Status: **primary LLE oracle from 2026-10-20**. `References2/xemu-0.8.136` stays
vendored as the prior snapshot so historical citations in
`xgrid-architecture-decisions.md` and the other research notes remain traceable.

## How the delta was measured (2026-10-20, this session)

`git diff --stat` is misleading on this tree: most of the 0.8.136 -> 0.8.137
change is a repository-wide `clang-format` pass (braces moved, `case` bodies
joined, wrapped argument lists). To separate formatting from semantics every
file pair was compared **after stripping all whitespace** (the same method used
for the DSP folder). Result:

| Directory | Files that differ at all | Files that differ semantically |
|---|---:|---:|
| `hw/xbox/nv2a` | 88 of 97 | **4 + 4 cosmetic** |
| `hw/xbox/mcpx` | 30 (incl. moves) | APU/DSP restructure (below) |

The NV2A semantic set is exactly `ptimer.c`, `nv2a.c`, `nv2a_int.h`,
`nv2a_regs.h`, plus `pgraph/vk/surface.c` (filter choice) and
`pgraph/gl/shaders.c` (cache key). Everything else - `pgraph.c`,
`methods.h.inc`, `psh_regs.h`, `glsl/psh.c`, `glsl/vsh-prog.c`,
`glsl/vsh-ff.c`, `texture.c`, `vertex.c`, `s3tc.c`, `swizzle.c`, the GL and
Vulkan backends, `pfifo.c`, `pmc.c` - differs **only in whitespace**.

**Consequence 1: 0.8.137 adds no NV097 methods.** `methods.h.inc` is identical
apart from `16 * 4` -> `16*4`. The method inventory built from 0.8.136 +
Playtest needs no re-extraction for 0.8.137.

**Consequence 2: the shader/combiner/vertex semantics we transcribed from
0.8.136 are unchanged.** `glsl/psh.c` (+276 normalized lines), `glsl/vsh-ff.c`
(+186) and `glsl/vsh-prog.c` (+13) are formatting only; this was verified by
token comparison, not by reading the diff. Historical 0.8.136 citations for
PSH/VSH behaviour stay valid.

## NV2A: the one real functional delta - PTIMER

`hw/xbox/nv2a/ptimer.c` was rewritten (0.8.136: 61 lines; 0.8.137: 280) and is
now a real alarm engine rather than a free-running counter. The facts:

**Register time model** (`ptimer.c:26-54`)

- `NV_PTIMER_TIME_0` (0x009400) is the low **32 bits** of the register clock in
  units of `1 << 5` internal ticks; `NV_PTIMER_TIME_1` (0x009410) is the high
  **29 bits** (mask `0x1FFFFFFF`). Register time is therefore 61 bits, low 32
  bits at 5-bit granularity.
- Internal time = register time >> 5, so internal ticks are 1/32 of a register
  unit. The old model exposed `(clock & 0x7FFFFFF) << 5` for TIME_0 and
  `(clock >> 27) & 0x1FFFFFFF` for TIME_1, i.e. the same scaling but with **no
  writable offset** and no alarm.
- `time_offset` (0.8.137 addition) is added to the absolute core clock, so
  **writing TIME_0 or TIME_1 sets the current time** (the guest can seed the
  counter; the write computes `target_internal - absolute_clock`). Writes keep
  the other half of the clock.
- `NV_PTIMER_ALARM_0` (0x009420) is a **32-bit compare on the low register
  word with the low 5 bits masked** (`ALARM_MASK 0xFFFFFFE0`). The alarm is
  re-armed to the next 2^32 wrap when it fires (`get_next_alarm_time`:
  `target = (now & ~0xFFFFFFFF) | (alarm & 0xFFFFFFE0)`, `+= 1<<32` when the
  value is <= now). `ptimer_alarm_distance` treats the wrapped delta as
  unreached when it is > half the 32-bit space.
- Alarm interrupts are delivered by a QEMU virtual timer scheduled in
  nanoseconds from `(alarm - now)` register units, rounded up through
  `numerator/denominator` and the PRAMDAC core clock. `schedule_qemu_timer` is
  re-run on `INTR_EN_0`, `NUMERATOR`, `DENOMINATOR`, `ALARM_0`, `TIME_0/1`
  writes; a read of `INTR_0` **also** samples the alarm and latches it
  (`ptimer.c:173-186`), so a polling guest sees the pending bit without an
  interrupt.
- `NV_PTIMER_INTR_0` read returns `pending_interrupts`; `INTR_EN_0` write
  latches an already-reached alarm immediately, and clearing the enable stops
  the timer. Enable-bit clearing is level-based (no edge latch).
- `ptimer_reset` zeroes `alarm_time`, `time_offset`, `numerator` and
  `denominator` to 1/1 and deletes the timer.

**IRQ wiring** (`nv2a.c:48-56`, `nv2a_regs.h`)

- `NV_PMC_INTR_0_PTIMER` is **bit 20** (0x00100000). `nv2a_update_irq` now
  sets/clears it from `ptimer.pending_interrupts & ptimer.enabled_interrupts`.
  0.8.136 had no PTIMER IRQ path at all: a guest waiting on a PTIMER alarm
  interrupt would hang.
- `nv2a_int.h`: `alarm_time` widened to `uint64_t`, `time_offset` added, and a
  `QEMUTimer` embedded in the PTIMER state.
- VM state version 3 -> **4**; `alarm_time`+`time_offset`+the timer are
  version-4 fields (`VMSTATE_UNUSED(4)` replaces the old 32-bit alarm in the
  base section).

**XGrid consequence.** `Nv2aMmio` (0.8.136 model) has `_ptimerClock`, the
0x7FFFFFF/0x1FFFFFFF scaling and `ALARM_0` ignored; PTIMER never raises
`NV_PMC_INTR_0` bit 20 and writing TIME_0/TIME_1 does nothing. Anything that
programs a PTIMER alarm (timeout/watchdog/deadline code, some XDK timing code)
will not see an interrupt. Landing the 0.8.137 model is a self-contained change
in `Nv2aMmio` plus the IRQ aggregate and snapshot fields (snapshot format bump).

## NV2A: the two renderer-side changes (not relevant to `--gpu sw`)

- `pgraph/vk/surface.c:255,1239`: blits of a colour surface use
  `VK_FILTER_NEAREST` unconditionally; 0.8.136 used `VK_FILTER_LINEAR` when
  `surface->color`. The Vulkan backend no longer filters colour downloads.
- `pgraph/gl/shaders.c`: the on-disk shader cache key gained the GL version
  string, so a driver upgrade invalidates cached shaders instead of reusing
  stale SPIR-V. Both are host-renderer behaviour; neither changes guest-visible
  NV2A state.

## APU / GP-EP DSP: the substantial change

0.8.136 carried one C interpreter (`dsp_cpu.c`, `dsp_emu.c.inc`,
`dsp_dis.c.inc`, `dsp_dma.c`, `gp_ep.c`) operating directly on `dsp_core_t`.
0.8.137 splits into a **backend-agnostic core** (`dsp.c` + `dsp.h` +
`dsp_internal.h` + `dsp_dma.c`) with two implementations:

| Path (0.8.137) | Lines | What |
|---|---:|---|
| `hw/xbox/mcpx/apu/dsp/dsp.c` | 230 | `DSPOps` vtable; `read_peripheral`/`write_peripheral`; `dsp_set_engine` hot switch |
| `hw/xbox/mcpx/apu/dsp/dsp_jit.c` | 362 | **New** backend over the external Rust crate `dsp56300` |
| `hw/xbox/mcpx/apu/dsp/dsp_c.c` | 299 | C-interpreter backend (`dsp56k_*`), state sync |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_cpu.c` | ~1,000 | interpreter core (moved, semantics fixed) |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_emu.c.inc` | 189,824 chars | per-opcode semantics (moved, **unchanged**) |
| `hw/xbox/mcpx/apu/dsp/interp/dsp_dis.c.inc` | - | disassembler (moved; one `lua` field-width fix) |
| `hw/xbox/mcpx/apu/dsp/dsp_dma.c` | 386 | DMA engine, now memory-agnostic |
| `hw/xbox/mcpx/apu/dsp/gp_ep.c` | 441 | GP/EP frame glue (run loop, reset bits, FIFOs) |

`debug.c`, `dsp_cpu.c`, `dsp_cpu_regs.h`, `dsp_emu.c.inc`, `dsp_dis.c.inc`
moved into `interp/`; `dsp_state.h` and the old `dsp/debug.c` were deleted.

### The JIT is a Rust crate, not new x86 codegen in xemu

`dsp_jit.c` includes `<dsp56300.h>` and calls `dsp56300_create/reset/step/run/
get_state/set_state/read_memory/write_memory/invalidate_cache`. The core lives
outside this tree (Rust; not vendored here), so **the C interpreter in
`interp/` remains our semantics oracle**, not the JIT. What the JIT file does
document is the memory map and the state contract, which we can use:

- Memory map (`dsp_jit.c:275-318`):
  - X space: `0x0000-0x1000` XRAM; `0x1400-0x1800` **alias of XRAM words
    `0xC00-0x1000`** (the MIXBUF window); `0xFFFF80-0x1000000` peripheral
    callbacks.
  - Y space: `0x0000-0x0800` YRAM; P space: `0x0000-0x1000` PRAM.
  - Addresses are 24-bit; the interpreter asserts `address & 0xFF000000 == 0`.
- Bootstrap (`dsp_jit.c:86-100`): the scratch buffer is DMA'd into PRAM
  (`0x800` words), then any word with a nonzero high byte is masked to
  `0x00FFFFFF` (the boot ROM writes tag bytes the core does not use).
- State widening on load (`dsp_jit.c:244-256`): legacy snapshots store `M`
  as 16-bit; `M == 0x00FFFF` becomes `0x00FFFFFF` (linear addressing). `N`
  values with bit 15 set are sign-extended to 24 bits. This is a real
  addressing-mode fact: **modifier `$FFFF` is the 16-bit spelling of the
  24-bit linear mask**.
- Interrupt slots: the C core indexes four interrupts 0..3; the architectural
  IVT slot is `vectorAddr / 2` per DSP56300FM Table 2-2, and the JIT maps
  `{0,2,1,4}` = `{RESET, ILLEGAL, STACK_ERROR, TRAP}` (`dsp_jit.c:29-34`).

### Corrected DSP core facts (semantic, not formatting)

`dsp_emu.c.inc` is **byte-identical after whitespace removal** (189,824
normalized characters, zero differences): the instruction semantics did not
change between the snapshots. The core files did change in four places:

1. **Interrupt table is now four entries with architectural vectors**
   (`interp/dsp_cpu.c:78-83`, `dsp_cpu_regs.h:127-130`):

   | Index | Name | Vector address | IVT slot |
   |---|---:|---:|---:|
   | 0 | RESET | 0x00 | 0 |
   | 1 | ILLEGAL | **0x04** (was 0x3E) | 2 |
   | 2 | STACK_ERROR | 0x02 | 1 |
   | 3 | TRAP | 0x08 | 4 |

   The 0.8.136 table had 12 entries including TRACE, SWI, HOST_COMMAND,
   HOST_RCV/TRX and SSI vectors; those have no producer in the Xbox and their
   handlers were `assert()`s. The illegal-instruction vector move 0x3E -> 0x04
   is the meaningful fix.
2. **SR bit names corrected** (`dsp_cpu_regs.h:34-49`): bit 13 is `SC` (not
   `T`), bit 14 is `DM`, bit 15 is `LF`, bit 16 is `FV`. Interrupt entry now
   clears `LF` and `FV` (`(1<<LF)|(1<<FV)|S1|S0|I0|I1`) instead of `LF|T`.
3. **Trace interrupts removed**: the `SR.T` -> `DSP_INTER_TRACE` injection in
   `dsp_postexecute_interrupts` is gone, along with the SSI/host-command
   asserts (`interp/dsp_cpu.c:759-880`).
4. **Disassembler `lua` fix** (`interp/dsp_dis.c.inc`): the destination
   register field is 5 bits, not 4 (`BITMASK(4)` -> `BITMASK(5)`). Trace-only.

### DMA facts (`dsp_dma.c`)

- Block descriptor is **7 words** at `next_block` in DSP memory: `next`,
  `control`, `count`, `dsp_offset`, `scratch_offset`, `scratch_base`,
  `scratch_size - 1` (`dsp_dma.c:115-140`).
- Address-space decode: `0x0000-0x1800` = X, `0x1800-0x2000` = Y
  (`- 0x1800`), `0x2800-0x3800` = P (`- 0x2800`); anything else asserts.
  `next & 1` is the EOL flag.
- Formats: **format 0 is now valid** - 8-bit items, mask `0x000000FF`
  (`dsp_dma.c:168`). Formats 1/2/3/4 are 16/16/24/32-bit; format 2 sign-extends
  8-bit items to 16; format 4 is float32.
- Interleaved samples: with `format & 4`, `channel_count` samples are
  de-interleaved into `block_count` frames (`i * item_size * channels + ch *
  item_size`), 2-byte items take the low 16 bits (`v >> 8` in the 16-bit store).
- Buffers: ids 0xE and 0xF are the scratch ring and scratch window; ids 0-3 are
  FIFOs. `scratch_size` wraps modulo the ring; the updated
  `scratch_offset` is written back to word 4 of the block.
- **`DMA_CONTROL` read completion** (`dsp_dma.c:320-330`): reading the control
  register while `RUNNING` increments `dma_read_count`; after **3 reads** the
  state flips to `STOPPED` and the counter resets. A `START` action clears the
  counter. The guest polls that register to detect completion, so an
  implementation that leaves it RUNNING forever stalls the DSP.
  Serialized as an optional VM subsection (only written when nonzero).

### Frame / mixing glue (`gp_ep.c`, `apu.c`, `apu_regs.h`)

- Frame geometry: `NUM_MIXBINS = 32`, `NUM_SAMPLES_PER_FRAME = 32`,
  `EP_FRAME_US = 5333` us (256/48000 s), i.e. 32 stereo samples per 5.333 ms
  tick. The VP writes `mixbins[bin][sample]` as floats into the GP MIXBUF at
  `GP_DSP_MIXBUF_BASE + bin * 32` (`gp_ep.c:443-455`).
- GP runs **every** frame, EP every **8th** frame (`ep_frame_div % 8 == 0`);
  both only when `GPRST` has both run bits set; each run is
  `dsp_start_frame` (raises START_FRAME interrupt bit 1), halt-request=false,
  cycle_count=0, then `dsp_run(1000)` in a loop until the core reports halted
  (or, on the host-realtime path, until the 1000-cycle slice returns).
- `NV_PAPU_XGSCNT` (0x200C) now returns `ep_frame_div * 32` (a sample counter)
  instead of `qemu_clock_get_ns()/100` (`apu.c:50-53`). Guests that pace on it
  see a monotonic 48 kHz sample count. **Landed in XGrid 2026-10-21**
  (decisions 218): `McpxApuDevice` returns `_epFrameDiv * 32`, read-only.
- APU VM stop/start now syncs both DSP backends (`dsp_sync_to_vm` /
  `dsp_sync_from_vm`, `apu.c:352-372`); `mcpx_apu_reset` invalidates the opcode
  cache through the ops vtable.
- `XGSCNT`/throttling/utilization accounting also changed on the host side
  (work-time utilization instead of sleep-time), not guest-visible.

### Peripheral map (unchanged, re-verified)

`read_peripheral`/`write_peripheral` (`dsp.c:44-104`) still implement exactly:
`0xFFFFB3` instruction counter (reads 0), `0xFFFFC4` bit 0 = halt request,
`0xFFFFC5` = interrupt status (bit 0 abort-frame, bit 1 start-frame, bit 7
DMA-EOL; write-one-to-clear), `0xFFFFD4-D7` = next block / start block /
control / configuration. Everything else returns `0xABABA`.

## Action list for XGrid (ordered)

1. **PTIMER 0.8.137 model** in `XGrid.Nv2a.Nv2aMmio`: 61-bit register time,
   writable TIME_0/TIME_1 offset, 27-bit-masked ALARM_0 compare, wrap re-arm,
   alarm pending latch on INTR_0 read, `NV_PMC_INTR_0_PTIMER` (bit 20) in the
   IRQ aggregate, snapshot v-bump. Acceptance: unit differential against the
   facts above (register maths, wrap, latch-on-read) plus unchanged menu/triangle
   goldens.
2. **DSP execution** (phase 12 remainder): start from `interp/` semantics
   (`dsp_emu.c.inc` is the complete opcode set) and the memory/DMA facts above.
   The minimum useful slice is PRAM bootstrap + interrupt entry (4-vector
   table) + MIXBUF read/write + DMA block walk with the 3-read control
   completion. Then the GP/EP frame loop with 32x32 mixbins and EP/8.
3. Re-check any future DSP work against `interp/`, **not** `dsp_jit.c` (the JIT
   is an external crate and only documents the memory/state contract).

## Process

- Consult `References2/xemu-0.8.137` first (AGENTS.md, ImplementationPlan.md and
  `sources.md` now say so). Keep citing the snapshot you actually read.
- When a research note cites `xemu-0.8.136` for a behaviour that matters to the
  task, re-extract it from 0.8.137 and record the new file:line in the note.
  For NV2A PGRAPH/PSH/vertex/texture methods this is a no-op: those files are
  formatting-identical.
- `xemu-Playtest-v2` stays a cross-check only (Chihiro fork); it is not the
  primary oracle for Xbox content.
