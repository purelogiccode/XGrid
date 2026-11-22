# CPU engine port (xemu/QEMU to C#)

Status: **2026-11-22, P1S2-P1S5.** The operator chose the full CPU engine port
after P1S0/N15 (007 runs at 0.100x; CPU is 94% of the wall; shape fixes top out
at ~0.20x). This note is the reference map and the phase contract. The ledger
row is `docs/xemu-port-manifest.md` (`target/i386/tcg/**`, `accel/tcg/**`,
`tcg/**`, `softmmu/**`).

## 1. Why this engine

The current engine already has: an accurate interpreter, a managed IL block
JIT, a native raw emitter (`RawBlockEmitter` + Iced), a 256-entry direct-mapped
`SoftTlb`, and a page-table walker (`Mmu`). It measures **73 Msteps/s** on 007
(6-8 ns per served member). xemu's C TCG measures ~830 Msteps/s on the same
workload (1.2 ns/step). The 5x gap is the **memory model + block shape**, not a
missing instruction:

- the raw emitter probes a 3-load translate cache per memory member;
- every block re-publishes EIP/TSC/served and re-checks the budget at the edge;
- the TLB stores a physical page + a writable bool, not the reference's
  per-access tags + addend;
- there is no TCG IR and no `tb_gen_code`-style single translation per guest
  page.

## 2. Reference map (xemu 0.8.139, QEMU 10.2 base)

| Reference | Function | C# target |
|---|---|---|
| `include/exec/tlb-common.h` | `CPUTLBEntry` (`addr_read`, `addr_write`, `addr_code`, `addend`), `CPU_TLB_ENTRY_BITS` | `XGrid.Cpu/QemuTlb.cs` (Phase A) |
| `accel/tcg/cputlb.c` | `tlb_fill`, `probe_read`/`probe_write`, `tlb_set_page`, `do_ld*_mmu`/`do_st*_mmu`, MMIO slow path | `Mmu` + `QemuTlb` + `AddressSpace` classification |
| `softmmu/physmem.c` | `address_space_translate`, `flatview_*`, `address_space_rw` | `XGrid.Memory/AddressSpace.cs` (already the flat map; port the translate/slow-path split) |
| `system/memory.c` + `include/exec/memory.h` | memory regions, `MemoryRegionOps`, MMIO dispatch | `IMmioDevice` / `IMemoryBus` (existing seam) |
| `accel/tcg/translate-all.c` | `tb_gen_code`, `page_protect`, `tb_flush` | `TranslatedBlockCache` (new) + `RawBlockEmitter` reuse |
| `accel/tcg/cpu-exec.c` | `cpu_exec`, TB lookup, `tb_add_jump` | `XboxCpu.Run` block loop (existing) |
| `tcg/tcg.c` | `TCGOp`, temps, globals, labels, `tcg_gen_code` | `XGrid.Cpu/Tcg/TcgOp.cs`, `TcgBuilder.cs`, `TcgX86Emitter.cs` |
| `tcg/tcg-op.c` | IR helpers (`tcg_gen_add_i32`, loads/stores, branches) | `TcgBuilder` helper methods |
| `tcg/i386/tcg-target.c.inc` | host codegen, register allocation, prologue/epilogue | `TcgX86Emitter` (Iced) |
| `target/i386/tcg/translate.c` | guest decoder/translator | `XGrid.Cpu/Tcg/I386Translator.cs` |
| `target/i386/tcg/decode.h`, `emit.c.inc` | decode tables, per-op emission | `I386DecodeTable.cs` / translator cases |
| `target/i386/tcg/helper.c` | exceptions, interrupts, `do_interrupt` | `XboxCpu` fault path (existing) |
| `include/exec/cpu-common.h`, `memop.h` | `MemOp`, endianness, access sizes | `XGrid.Memory` access helpers |

The current files that stay: `XboxContext`, `XboxCpu` state/registers, the
interpreter (`Step`/spec handlers) as the exact fallback, `Mmu` page-table
semantics, `AddressSpace`, the raw helpers, and the snapshot format.

## 3. Phases and gates

### P1S2 - softmmu/TLB and memory dispatch (4-6 sessions)

- Port `CPUTLBEntry` as `QemuTlb`: per-access tags (`addr_read`/`addr_write`/
  `addr_code`), the addend, per-entry slow flags, 4 KB pages, direct-mapped
  index.
- Port `tlb_fill`/`probe_read`/`probe_write` semantics: page-table walk on
  miss, permission faults, cross-page accesses, MMIO classification,
  invalidation on CR3/CR0/INVLPG/page-table stores.
- Differential: the existing `MmuTests` + a TLB vector suite.
- **Gate:** the differential is exact and the probe measures >=2x the current
  per-member memory cost, or the measurement is a recorded negative.

**Result (2026-11-22).** `QemuTlb` is ported (`src/XGrid.Cpu/QemuTlb.cs`) with
the reference per-access tags (`addr_read`/`addr_write`/`addr_code`) and the
addend; 9 differential tests cover read/write/code, a read-only write miss, a
non-executable code miss, page offsets, flush page/all, collision and an
`Mmu`-translate differential. Probe benchmark: **0.84 ns/lookup (QemuTlb) vs
0.64 ns (current SoftTlb)**. The TLB shape is **not** the 2x lever (0.76x of the
current probe); the current TLB is already the fast shape. The 5x is the
block/translator shape, so **P1S3** (TCG IR + i386 translator) is the next lever.
Recorded as **N16**; the QemuTlb stays as the reference shape for the TCG port.

### P1S3 - TCG IR and the i386 translator (8-12 sessions)

- Port the TCG IR (ops, temps, globals, labels, blocks) and the i386 translator
  for the shapes the gates/titles use (start with the 641-case cpu-oracle and
  the ktest shapes, extend by measured fallback gaps).
- Keep the interpreter as the fallback; count every fallback.
- **Gate:** `tools/cpu-oracle` exact on the IR interpreter before codegen.

**Slices (2026-11-22).** Slice 1: the managed TCG IR (`TcgOp`, `TcgProgram`,
`TcgBuilder`), the reference IR interpreter (`TcgInterpreter`, exact `AluOps`
flags), and the i386 translator for 32-bit register/immediate MOV,
ADD/SUB/AND/OR/XOR/CMP, INC/DEC (CF preserved), NOP, JMP and Jcc; 17
differentials. Slice 2: the reference memory shape (`Lea` from `MemoryOperand`,
1/2/4-byte `Load`/`Store`, segment bases), 8/16-bit register and memory forms,
partial-register (AL..BH) reads/writes, and 16-bit addressing declined. Slice 3:
shifts/rotates (SHL/SHR/SAR/ROL/ROR/RCL/RCR by imm and CL), SHLD/SHRD,
NEG/NOT, MOVZX/MOVSX (8/16), LEA, XCHG (reg/reg and reg/mem) and SAHF/LAHF.
Slice 4: MUL/IMUL (1/2/3-operand)/DIV/IDIV with the reference EAX:EDX and flag
semantics (a divide error raises `TcgDivideErrorException`), and PUSH/POP
(register/immediate/memory, 16/32-bit) over the SS stack. Slice 5: BT/BTS/BTR/BTC
(register and bit-string memory forms), CMPXCHG, CMPXCHG8B and XADD. Slice 6:
16-bit addressing (BX/BP/SI/DI forms, 16-bit wrap before the segment base, the
BP-based SS default). Slice 7: REP string ops (MOVS/CMPS/STOS/LODS/SCAS,
REP/REPNE termination, DF direction, 16/32-bit address size, 1/2/4-byte
elements). Slice 8: the MMX register file (<c>TcgMmxState</c>, the hardware alias
onto <c>ST(Top+i)</c> with the tag-word rule) and the packed integer family -
padd/psub (wrap and signed/unsigned saturation), pmullw/pmulhw/pmulhuw/pmaddwd,
pcmp/pack/punpck, pand/pandn/por/pxor, pavg/pmin/pmax/psadbw, the eight shifts
(immediate and MMX/memory counts), movd/movq/movntq/maskmovq/pmovmskb/pextrw/
pinsrw/pshufw and EMMS. **122 differentials total** (registers, EFLAGS, EIP,
guest memory, all eight MMX registers and the tag word); `XGrid.Cpu.Tests`
**825/825**, format clean. Slice 9: the IR's MMX file is the machine's real
`FpuState` (the hardware alias onto `ST(Top+i)`, tags and EMMS included) instead
of a private copy, and the **P1S3 corpus gate** is live:
`TcgCorpusTests` runs all 641 corpus cases through an IR engine that decodes each
instruction, offers it to the translator, executes translated instructions on
the IR interpreter and falls back to the exact interpreter otherwise, then
compares EIP, GPRs, masked EFLAGS, the FXSAVE image and the case data page
against the oracle. **Exact** (`translated=2430 fallback=2415`; the two harness
instructions `fxsave`/`ldmxcsr` are 1,282 of the fallbacks). The corpus caught a
real translator bug: 8/16-bit `CMPXCHG` compared the full 32-bit destination
instead of the operand-sized register, so `cmpxchg cl, bl` never matched (two
focused differentials added; the corpus fails pre-fix). Next shapes, by the
measured fallback list: ADC/SBB (carry-live), SETcc/CMOVcc, x87 (59 cases),
SSE (37), LOOP/JECXZ, ENTER/LEAVE, XLAT and the string/high-byte tails. Slice 10:
that integer list - ADC/SBB, TEST, SETcc, CMOVcc (16/32), BSWAP, BSF/BSR (the
reference zero-source rule), ARPL (register and memory), CBW/CWDE/CWD/CDQ, XLAT,
CLD/STD/CLC/STC/CMC, PUSHF/POPF, PUSHA/POPA, LEAVE and LOOP/LOOPE/LOOPNE/JECXZ
(16/32 address size), with 16-bit stack-pointer wrap on PUSH/POP. **159
differentials total**, 863 CPU tests green, corpus fallback 2415 -> 2344. ENTER
and the 16-bit LEAVE stay fallbacks (documented). Slice 11: x87 through the
shared <c>TcgFpu</c> executor (every arithmetic/compare/unary/constant/stack and
BCD shape over the same <c>FpuState</c>/<c>Fpu80</c>, with FNSTSW AX as an IR
read) and slice 12: SSE through <c>TcgSse</c> (moves, packed/scalar arithmetic,
unary, logical, compares, shuffles, unpack, conversions, MXCSR). The corpus now
runs **4,202 translated / 643 fallback**, and the only body fallbacks left are
ENTER (2, 16-bit); the other 641 are the harness epilogue's FXSAVE. **188
differentials**, 892 CPU tests green, format clean; the harness compares the
full FPU (control/status/tag/top and every 80-bit register) and XMM/MXCSR state.

**Real-leg coverage (P1S3 acceptance, 2026-11-22).** `TcgCoverage` is an opt-in,
state-free probe (`XGRID_TCG_COVERAGE=1`, deterministic stride
`XGRID_TCG_COVERAGE_SAMPLE`): it asks the translator about every instruction the
interpreter serves (JIT-served instructions do not reach it), so the share
characterizes exactly the population P1S4 would translate. Measured with the
stride at 256:

| Leg | Translated | Fallback | Share | Fallback reasons |
|---|---|---|---|---|
| 007 `Agent Under Fire` 1B boot (fast raw, sw) | 996 | 83 | 0.9231 | Call 56, Ret 20, Iret 2, MovCrWrite 1, indirect jump 1, Cli/Out/Interrupt 1 |
| ktest 792,800,535 steps | 2,744,940 | 351,926 | 0.8864 | Sti 181,496, Cli 130,146, Call 19,975, Ret 19,974, Out 142, Invlpg 48, Rdtsc 44, indirect jump 33, In 27, Iret 24, Interrupt 10, Cpuid 4 |
| triangle 1B (fast raw) | 347 | 29 | 0.9229 | Call 18, Ret 7, MovCrRead 2, MovCrWrite 1, Sti 1 |
| menu 1B (fast raw) | 1,093 | 93 | 0.9216 | Call 51, Ret 32, Iret 3, Out 2, Interrupt 2, MovCrWrite 1, In 1, indirect jump 1 |

The probe is behavior-neutral by same-command A/B against the parent commit
(triangle 1B `gl+replace` RAM `F5D26889` / CPU `8836C7EB` on both builds). The
real-code fallback list is the P1S4 shape order: **CALL/RET** (and IRET), then
CLI/STI, IN/OUT, MOV CR, INVLPG/RDTSC/CPUID and indirect jumps.

### P1S4 - host code generation (6-10 sessions)

- Map the IR to the existing raw emitter (one native function per TB) or a new
  IR executor; port the memory/FPU/SSE/string/CALL/RET helpers.
- **Gate:** the 1B anchors, the ktest log and the cpu-oracle exact; the member
  cost is below the current raw runner.

### P1S5 - parity and switchover (4-6 sessions)

- Four accurate 1B goldens, ktest, 22-row corpus, 30B/100B 007 legs exact.
- Switch the product default only on parity; keep the raw engine as the
  fallback and a documented flag.
- **Gate:** the nine-part P1 gate is re-run; 0.5x measured or the ceiling
  recorded (no third variation).

## 4. Risks and stop rules

- **A C# TCG may not beat the native raw emitter.** The raw emitter is already
  native x86-64; the port must win on the memory model and block shape, not on
  "TCG magic". P1S4's not-slower gate is binding; a slower port is not switched.
- **The reference is a moving target.** Pin 0.8.139 (`aa54c34`); the QEMU 10.2
  base is vendored in the same tree.
- **No mixed engine.** A run uses either the interpreter, the IL/raw engine, or
  the ported TCG engine; `--jit-exact` and the differential legs stay as they
  are.
- **Stop after two failures** per phase and record the negative; do not grind a
  third variation.
