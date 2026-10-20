# cpu-oracle - independent x86 differential for the JIT

Executes the same instruction bytes from the same architectural state on XGrid
and on an **independent x86 implementation**, then compares the final state.
This is a differential, not a golden: nothing here was recorded from XGrid.

## What it found (2026-10-20)

| Finding | Status |
|---|---|
| `0F BA` (`BT/BTS/BTR/BTC r/m, imm8`) was not decoded at all | fixed in `Decoder.DecodeTwoByte` (group 8) |
| `FPREM`/`FPREM1` reported the quotient bits on C0/C3/C1 in the wrong positions | fixed in `XboxCpu.ExecuteFprem` |
| `FDIV`/`FDIVR` never latched the ZE (`x/0`) or IE (`0/0`) status bits | fixed in `XboxCpu.SetFpuDivideExceptions` |

1B effects: triangle and menu RAM SHAs are unchanged in both fast and accurate
mode; the accurate-mode CPU SHA moves by exactly one byte (the FPU status word
`0x0101 -> 0x0105`, the ZE bit). Fast mode is unchanged because the raw JIT
executes the x87 arithmetic natively instead of through `ApplyFpuArithmetic`.

## Layout

| File | Purpose |
|---|---|
| `corpus.py` | the cases (flag-neutral setup + tested block) |
| `oracle.py` | builds the corpus, runs the oracle, writes `int.json` + `int.dat` |
| `boot.s`, `harness.s` | 16/32-bit guest harness for the system-emulator path |

Output lives at `tests/XGrid.Cpu.Tests/cpu-oracle/int.json` (cases + expected registers/flags)
and `tests/XGrid.Cpu.Tests/cpu-oracle/int.dat` (fixed-size records: 512-byte FXSAVE image +
256 bytes of the case's data page). `tests/XGrid.Cpu.Tests/CpuOracleTests.cs`
runs the corpus in CI; the emulator is only needed to regenerate it.

## Regenerating

```powershell
# default oracle: QEMU TCG core via Unicorn (pip install unicorn)
python tools/cpu-oracle/oracle.py --out tests/XGrid.Cpu.Tests/cpu-oracle/int

# list cases
python tools/cpu-oracle/oracle.py --list
```

Each case starts with `fninit; emms; ldmxcsr [0x1f1000]`, so cases are
independent, and ends with `fxsave [0x1f0000]; int $0x30`. Both sides execute
the same bytes at the same per-case address (`0x40000 + index*128`), so x87,
MMX and SSE state is compared through the architectural FXSAVE layout and the
memory the case stores results to.

### System-emulator path (staged)

`oracle.py --emulator qemu` boots `boot.s` + `harness.s` on
`qemu-system-i386 -fda ...` and reads the 832-byte records from COM1. It needs
`qemu-system-i386` at `References2/qemu/qemu-system-i386.exe` (or
`XGRID_QEMU`); the current host only has the 197 MB installer partially
downloaded, so this path has not been validated end-to-end yet. The Unicorn
path is the same QEMU TCG core packaged as a library.

## Rules for new cases

- The setup must not change EFLAGS (`mov`, `fld`, `movq` only) because the
  driver loads the case's EFLAGS immediately before the entry.
- Store every result you want compared to the case page (offset < 0x100); the
  page is part of the record. Registers, EFLAGS and EIP are always compared.
- Declare `flags_mask` for instructions whose flags are architecturally
  undefined (`mul`/`div`, shifts with count > 1, `bt`, `bsf`/`bsr`). The
  oracle and the test only compare defined bits.
- Keep the block under `CASE_STRIDE` (128) bytes including the epilogue.

## Oracle caveats

- QEMU's FXSAVE abridged tag byte, FOP word, FPU pointers and MXCSR mask are
  implementation metadata and are not compared. The architectural FCW/FSW and
  MXCSR are.
- Empty x87 register slots have undefined contents; only stored results and
  status words are compared.
- Unicorn reuses translated blocks aggressively; a fresh `Uc` per case is used
  (0.5 s for 333 cases) because rewriting code at the same address produced
  stale translations.
- Both sides run at CPL=0 with flat segments and paging off. Fault delivery is
  out of scope for now (a faulting case would need a matching handler dump).
