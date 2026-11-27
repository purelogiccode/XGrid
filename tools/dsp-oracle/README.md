# dsp-oracle - xemu reference differential for the DSP56300 port

Runs the **unmodified xemu 0.8.139 DSP56300 interpreter** (`References2/`,
read-only) as a standalone oracle and compares it with the managed port in
`src/XGrid.Audio/Dsp/`.  This is a differential, not a golden: the expected
state is whatever the reference produces.

Nothing here is part of the XGrid build or the distribution.  The reference
sources are never copied into the tree; `prepare.py` writes a guarded copy into
`build/` at run time (git-ignored).

## Layout

| File | Purpose |
|---|---|
| `oracle.c` | case-file driver: sets up a core, executes N instructions, prints the state |
| `prepare.py` | copies `dsp_cpu.c` into `build/` and adds out-of-range guards |
| `gen_vectors.py` | synthesizes cases from the reference tables and writes the golden file |
| `shims/` | minimal `qemu/osdep.h`, `qemu/bswap.h` and `trace.h` so the C interpreter compiles standalone |
| `build.ps1` | compiles the oracle (`-Regenerate` also rewrites the vectors) |

## Usage

```powershell
pwsh -File tools/dsp-oracle/build.ps1               # compile the oracle
pwsh -File tools/dsp-oracle/build.ps1 -Regenerate   # + rewrite the vectors
```

`-Regenerate` rewrites
`tests/XGrid.Audio.Tests/TestData/dsp-vectors.txt`, which
`Dsp56300DifferentialTests` replays against the port.  The file is generated;
do not edit it by hand.

## What it covers

| Group | Cases |
|---|---|
| decode table | three synthesized words per `nonparallel_opcodes[]` entry with a handler |
| ALU / parallel move | two words per defined `opcodes_alu[]` entry (all 16 `opcodes_parmove[]` dispatch arms are exercised by the random `pm` field) |
| programs | 240 random eight-word programs, 1-24 steps |
| interrupt pipeline | every vector (reset/illegal/stack/trap) with 6 step budgets |

A case is fully described by `(program words, seed, steps, pending interrupts)`.
The state setup is a deterministic xorshift32 stream from the seed, mirrored by
`Dsp56300DifferentialTests.SetupState`; the golden file stores only the
registers that differ from that initial state.  Cases whose execution leaves the
reference's addressable memory are dropped (the reference asserts; a release
build would read out of bounds), so the oracle guards those accesses and reports
`OOB`.

## What it found (2026-10-23, P1.2)

| Finding | Status |
|---|---|
| `emu_pm_1`'s "else" brace closes before `registers[numreg1] = save_1`, so the store is **unconditional**: the parallel move also writes the virtual `DSP_REG_A`/`DSP_REG_B` register | port reproduces it (comment in `Pm1`) |
| `emu_macr_p_x1_y0_b` calls `dsp_rnd56` **before** loading the accumulator, so the rounding is lost and the handler behaves as a plain `mac` | port reproduces it (`_alu[0xEB]` in `Dsp56300Opcodes.cs`) |
| `dsp->pram_opcache[dsp->pc]` and `lookup_opcode()` dereference NULL / read out of bounds on an out-of-range PC or an unmatched opcode | oracle guards; the port bounds-checks and reports `UndefinedInstructions` |

The vectors also caught an XGrid decode-cache bug: the 256-entry lookup cache
used `0` as "empty", and `0` is a valid instruction word (`nop`), so every
all-zero word decoded as undefined until the sentinel was fixed.
