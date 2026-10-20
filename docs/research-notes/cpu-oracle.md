# CPU differential oracle (2026-10-20)

Goal: give the fast-mode JIT an **independent architectural oracle** without
hardware. The last few sessions were gated on XGrid-vs-XGrid differentials
(IL engine vs raw engine, flag-off vs flag-on). Those catch disagreement between
two code paths in the same interpreter, but a shared misreading of x86 stays
invisible. This note records the independent differential and what it found.

## Design

`tools/cpu-oracle/` executes a corpus of instruction blocks twice:

- **XGrid** - `tests/XGrid.Cpu.Tests/CpuOracleTests.cs` loads the block at a
  per-case address in a flat 32-bit `XboxCpu` and runs until the `int $0x30`
  stop marker.
- **Oracle** - QEMU's TCG core, used as a library through Unicorn
  (`pip install unicorn`, 2.1.4 on this host).

Both sides start from the same GPRs, EFLAGS, flat segments and memory, run the
same bytes, and are compared on:

- EAX..EDI, EFLAGS, EIP (the stop marker address);
- the 512-byte **FXSAVE** image both sides produce with the same instruction
  (`FCW`, `FSW`, `MXCSR` are compared; tag/FOP/pointer fields are metadata);
- the 256 bytes of the case's data page (every value a case stores).

The corpus is 333 cases (`corpus.py`): ALU reg/reg, reg/mem and mem/imm forms
for all eight operations at 8/16/32 bits with carry and overflow edges;
inc/dec/neg/not; 7 shift/rotate families with counts 0/1/15/31/32/33; mul/imul
(1/2/3 operand)/div/idiv at 8/16/32 bits; mov/movzx/movsx/high-byte/xchg/bswap/
cbw/cwde/cdq; lea addressing forms; push/pop/pusha/popf; bt/bts/btr/btc;
bsf/bsr; cmpxchg/xadd; setcc/cmovcc/jcc/jecxz; string moves/stores/loads/scans
with and without REP and DF; x87 loads, stores, integer loads, arithmetic,
pops, compares, status/control, sqrt/abs/chs/fxch/frndint/fprem/fscale;
MMX moves and packed arithmetic; SSE scalar and packed arithmetic, moves,
compares and conversions.

## What the oracle found

1. **`0F BA` was not decoded.** `BT/BTS/BTR/BTC r/m, imm8` (group 8) produced
   `UnsupportedInstructionException`. The register and memory-register forms
   (`0F A3`/`AB`/`B3`/`BB`) already existed and the executor already accepted an
   immediate bit index, so the fix was a decoder branch plus two tests. This is
   the most common bit-test form in compiler output, so it was a genuine gap.
2. **`FPREM`/`FPREM1` flag mapping was wrong.** The implementation set
   C3 = Q2, C0 = Q0, C1 = Q1; Intel is C0 = Q2, C1 = Q0, C3 = Q1. The oracle
   caught it as `FSW 0x3A00` (QEMU) vs `0x3900` (XGrid) for `1 mod 1`.
3. **`FDIV`/`FDIVR` did not latch exceptions.** `1/0` left FSW = 0 while the
   reference sets ZE (`0x04`); `0/0` now sets IE (`0x01`). The values produced
   were already correct; only the status word was silent, which real code that
   checks `FNSTSW` after a division would notice.

Also found in the corpus itself: a missing `flds` in the memory-form x87 cases
(the oracle reported `0 + 3` results). That is a useful property of the setup -
the first run disagreed on nine cases and every one was a corpus bug, not an
emulator bug.

## Validation

- `XGrid.Cpu.Tests`: **643/643** green, including the 333-case oracle
  differential (one `[Fact]` that loops the corpus), 3 new FPU tests and 2 new
  `0F BA` tests.
- Full solution: **all projects green, 1519 tests** (the documented baseline was
  1522 before this session; the three-test difference is the other session's
  worktree state, not a removal - every listed project passed).
- 1B fast mode, both titles: RAM and CPU SHAs reproduce the §202 anchors
  exactly (menu `660CA11B`/`F5D61C94`, triangle `F57934BF`/`CF79FC17`).
- 1B accurate mode: RAM SHAs unchanged (menu `D9C99A58`, triangle `F57934BF`).
  The triangle CPU SHA moved `98039867 -> 9B5AFC2A`; a byte diff of the two
  `.xgs` saves shows **one differing byte in 71 MB**: the FPU status word
  `0x0101 -> 0x0105` (ZE), i.e. exactly the divide-by-zero fix. Fast mode is
  unaffected because the raw JIT's native x87 encoding bypasses the managed
  arithmetic helper.

## Limits / next steps

- Fault delivery is not compared yet (a faulting case would need the oracle's
  handler dump and XGrid-side synthesis); `#DE`, `#UD`, `#GP` state precision
  is still covered only by the existing unit tests.
- Undefined flags are masked per case (`flags_mask`), so the differential says
  nothing about them by design.
- The x87 transcendental functions (FSIN/FCOS/F2XM1/FYL2X/FPATAN) are not in
  the corpus: their 80-bit results are where XGrid's own `Fpu80` math and QEMU's
  SoftFloat are most likely to differ in the last bits, and a difference there
  needs a decision (not automatically a bug). Worth a separate measurement.
- The system-emulator path (`boot.s` + `harness.s` on `qemu-system-i386`) is
  written but not yet validated on this host; the Unicorn path is the same TCG
  core. A Bochs cross-check would add a second, independent FPU implementation
  (Berkeley SoftFloat lineage on both sides, but a different decoder).
- Add cases for the shapes the raw-JIT profiler reports as remaining
  helper-routed (`movs` strings with REP, `fnstsw`, high-byte moves, carry
  rotates) if a future change touches them.
