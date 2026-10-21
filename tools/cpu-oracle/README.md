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

## What it found (2026-10-21, second campaign - corpus 333 -> 629 cases)

| Finding | Status |
|---|---|
| `PMINSW`/`PMAXSW` (`0F EA`/`0F EE`) were decoded as `Pminuw`/`Pmaxuw` and compared **unsigned** | renamed + signed compare |
| `PSRAW`/`PSRAD` with count >= element width sign-filled from the low element only | per-element sign fill |
| `XLAT` (`D7`) was not decoded | added (16-bit offset wrap, segment override) |
| 16-bit `[SI]`/`[DI]` (`mod=00 rm=4/5`) consumed a bogus disp16 and desynchronized the stream | only `mod=00 rm=6` has a disp16 |
| x87 NaN stores flipped the sign / dropped the payload (.NET `double.NaN` is negative) | `Fpu80.From/To{Single,Double}Bits` |
| `FUCOM`/`FUCOMP`/`FUCOMPP`/`FUCOMI`/`FUCOMIP` latched IE for a **quiet** NaN | quiet-compare rule + `IsSignalingNaN` |
| `FSQRT` of a negative value left C2 clear | sets C2 with the invalid flag |
| `MINPS`/`MAXPS`/`MINSS`/`MAXSS` returned the destination on equal operands | returns the source (sign of a +/-0 pair) |

1B effects: **none on the gate titles** - both 1B accurate goldens are
byte-identical and the fast anchors are exact (they execute none of these
shapes). 18 of the 23 new unit assertions fail on the pre-fix source. Details:
`docs/research-notes/xgrid-architecture-decisions.md` §220.

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
  undefined (`mul`/`div`, shifts with count > 1, `bt`, `bsf`/`bsr`, and any
  shift whose count reaches the operand size - CF/OF are undefined there). The
  oracle and the test only compare defined bits.
- Keep the block under `CASE_STRIDE` (128) bytes including the epilogue.
- **Every memory reference must stay inside the case's own page** and the case
  must only read memory it wrote. The test harness reuses one memory image
  across cases (only `esp` is per-case), while the oracle maps a fresh one, so a
  case that touches an absolute low address reads another case's writes in CI
  and zeros in the oracle. Use `lea {p}+0x1000, %reg` to point a base register
  at the case page instead of loading an absolute address.
- Set `esp`/`ebp` from `{p}` too; the default `esp` is `page + 0x1800` in both
  harnesses, which is enough for `push`/`call`/`enter` cases.
- **Pentium III only.** There is no SSE2 on the Xbox CPU: the `66`-prefixed
  128-bit packed-integer forms, `PADDQ`/`PSUBQ`/`MOVDQA`/`PSHUFD` and
  `MOVD xmm, r/m` are correctly rejected by the decoder and must not be added as
  cases.

## Oracle caveats

- QEMU's FXSAVE abridged tag byte, FOP word, FPU pointers and MXCSR mask are
  implementation metadata and are not compared. The architectural FCW/FSW and
  MXCSR are.
- Empty x87 register slots have undefined contents; only stored results and
  status words are compared.
- Unicorn reuses translated blocks aggressively; a fresh `Uc` per case is used
  (0.25 s for 629 cases) because rewriting code at the same address produced
  stale translations.
- Both sides run at CPL=0 with flat segments and paging off. Fault delivery is
  out of scope for now (a faulting case would need a matching handler dump).
- `FSTENV`/`FSAVE` are **not** cases: they store the FPU instruction/data
  pointers, which the reference fills with model metadata (FIP = guest address,
  FCS/FOP = its own conventions). No measured row reads them.
- `0/0` is an exception-class difference, not a bug: the reference reports
  zero-divide (`ZE`) where the 387+ SDM (and XGrid) report invalid (`IE`).
  Create NaNs by loading a NaN constant instead of dividing zero by zero.
- `FSQRT` of a negative value: the reference sets C2 only, the SDM (and XGrid)
  also raise invalid. Likewise not a case.
- `LES`/`LDS`/`LFS`/`LGS`/`LSS` load a segment selector from the descriptor
  table, and the harness has no descriptors beyond the flat code/data pair, so
  a null selector is the only loadable one. Far pointers are out of scope.
