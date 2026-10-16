# nv2a-trace — apitrace for the real Xbox GPU

Reference: `References2/nv2a-trace-master` (upstream: https://github.com/XboxDev/nv2a-trace)
License: **MIT**, Copyright (c) 2018-2022 XboxDev maintainers.

What it is: a remote tracer that runs on a dev PC, talks to a **real Xbox** through
`xboxpy`, and steps the GPU command stream method-by-method — the NV2A analogue of
apitrace. Per the README it is preliminary/unstable, may temporarily corrupt console state,
and has **no replayable/parsable trace format yet**.

## Two ways it helps XGrid

1. **Hardware ground truth (if a box is available).** It pauses the DMA pusher and, for each
   method, captures pre/post state — PGRAPH registers, textures and intermediate surfaces
   dumped as PNGs (`out/`) plus a method-by-method `debug.html` log. That is a stronger
   oracle than comparing against xemu (which deadlocks at the animation wait, AGENTS.md),
   and it matches XGrid's differential-first diagnosis method: dump state around each
   method and diff. Acceptance-style use: render a known scene on hardware, dump
   per-draw surfaces, compare with XGrid's `--pgraph`/`--frame` output.
2. **Facts-only use (no hardware needed).** MIT, so the pushbuffer command decoder, the
   NV2A MMIO/CACHE1 register constants and the texture channel/bpp table are usable
   knowledge for XGrid's pusher, debugger (plan Phase 19) and `--pgraph` instrumentation.
   Record any copied/adapted snippet in `sources.md`.

## Key files

| File | Content |
|---|---|
| `nv2a-trace.py` | Entry point: `_wait_for_stable_push_buffer_state`, skips already-executed methods (`dma_method_count * 4`), signal handling, experimental Z-compression/tiling disable helpers |
| `Trace.py` | `Tracer` class: method hooks, `_parse_push_buffer_command`, PGRAPH RDI dump (`_read_pgraph_rdi`), texture/surface PNG dumps, flip-stall counting, `debug.html` logging |
| `XboxHelper.py` | NV2A MMIO map + CACHE1 register addresses + `parse_command` pushbuffer decoder (below) |
| `Texture.py` | Texture/surface decode: `TextureDescription(bpp, channel_bpps, channel_offsets)` table (Y8, AY8, A8, A8Y8, R5G6B5, A4R4G4B4, ...), swizzled vs linear, AGP base `0xF0000000` |
| `Xbox.py`, `XboxHelper.py` | `xboxpy` wrappers (read/write u32, MMIO, `ke` API) |
| `KickFIFO.py`/`kick_fifo.asm`, `ExchangeU32.py`/`exchange_u32.asm` | small guest-side helper binaries executed on the box to re-kick the FIFO / atomically swap words |
| `HTMLLog.py`, `NV2ALog.py` | `debug.html` + method log (`log_method(method_info, data, pre_info, post_info)`) |

## Verified facts worth reusing

### Pushbuffer command decoding (`XboxHelper.parse_command`)

Matches xemu's CACHE1 pusher and cites envytools `hw/fifo/dma-pusher.rst`:

- old jump: `(word & 0xE0000003) == 0x20000000` → `addr = word & 0x1FFFFFFC`
- jump: `(word & 3) == 1` → `addr = word & 0xFFFFFFFC`
- call: `(word & 3) == 2`, return: `word == 0x00020000` (both flagged unhandled here)
- method: `masked = word & 0xE0030003`; increasing when `masked == 0`, non-increasing when
  `masked == 0x40000000`; fields: `method = word & 0x1FFF`,
  `subchannel = (word >> 13) & 7`, `method_count = (word >> 18) & 0x7FF`;
  command length = `4 + method_count * 4`.
  Open question noted in-source: whether `method` should be `(word >> 2) & 0x7FF`.

### NV2A MMIO / PFIFO registers (`XboxHelper.py`)

`NV2A_MMIO_BASE = 0xFD000000` with blocks `PMC 0x0000, PBUS 0x1000, PFIFO 0x2000,
PRMA 0x7000, PVIDEO 0x8000, PTIMER 0x9000, PCOUNTER 0xA000, PVPE 0xB000, PTV 0xD000,
PRMFB 0xA0000, PRMVIO 0xC0000`. CACHE1/state registers: `DMA_STATE 0x1228`,
`DMA_PUT 0x1240`, `DMA_GET 0x1244`, `DMA_SUBROUTINE 0x124C`, `PUSH0 0x1200`,
`DMA_PUSH 0x1220`, `PULL0 0x1250`, `PUT 0x1210`, `GET 0x1270`, `METHOD 0x1800`,
`RAMHT 0x0210`, plus `NV_PGRAPH_STATUS 0x0700`, `CTX_SWITCH1 0x014C`,
`FIFO 0x0720`, `TEXOFFSET0 0x1A24`.

### Methods it hooks (`Trace._hook_methods`)

`0x97` class: `NV097_SET_BEGIN_END 0x17FC` (dumps textures on BEGIN, surfaces on END),
`NV097_CLEAR_SURFACE 0x1D94`, `NV097_FLIP_STALL 0x0130`,
`NV097_BACK_END_WRITE_SEMAPHORE_RELEASE 0x1D70` (dumps surfaces) — the same constants
XGrid implements (§32 semaphore/notify, §43 flip path).

## Limitations

- Requires real hardware + `xboxpy`; random behavior, can corrupt console state.
- No trace replay — output is PNGs + `debug.html`, not a machine-comparable stream.
- Call/return opcodes and texture-address hooks are stubbed/commented out in the source.

## How to use it

1. With hardware: run `nv2a-trace.py`, capture `out/*.png` + `debug.html` for a scene, and
   diff against XGrid's `--frame`/`--pgraph` dumps at the same draw index.
2. Without hardware: read `Trace.py`/`XboxHelper.py` as documentation of pushbuffer and
   register semantics (cross-check with envytools `fifo/dma-pusher.rst` and xemu
   `hw/xbox/nv2a/pfifo/`), and `Texture.py` as an independent texture format table
   (compare §40/§95 format work).
