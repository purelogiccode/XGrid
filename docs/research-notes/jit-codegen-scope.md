# First-codegen scope — wave worker checkpoint

Base: `6cf7d0e1202ea4d3bf263b85101d6c94ce90f84d`. No product or test changes.
Measurements and final recommendation are pending; this checkpoint is not an acceptance claim.

## Established facts

- Full Release suite: **1316 passed, zero failed/skipped**, 12 assemblies. Command:
  `dotnet test XGrid.sln -c Release --nologo --no-build` (scratch log
  `wt-data/wave/suite.log`). Release build completed with zero errors.
- Correction to the task premise: `XboxCpuDecodeCache.cs:137–177` hits do **zero
  MMU translations and zero bus reads**, unless verification is enabled (15 reads).
  The single translate plus 15 reads occur on single-page **misses**, not hits.
- `HotspotProfiler.WriteReport` reports event counts, not time. Its mnemonic
  proportions must not be presented as dispatch/execute/fetch wall-time shares.
- Current IRQ boundary is `XboxCpu.cs:102–116` (the prompt's line 87 is stale).
  TSC increments before polling; a successfully delivered IRQ returns without
  executing the original instruction and still consumes a Step/Execute budget unit.
- Reference consulted, facts only: xemu 0.8.136
  `target/i386/tcg/translate.c:4235–4278` carries CS base and mode flags into
  translation; `4300–4310` records per-instruction PCs; `4353–4370` ends at
  single-step/IRQ-inhibit and page boundaries. XGrid's stronger requirement here
  is to retain **every original boundary**, not copy QEMU's chaining behavior.

## Block design (no boundary elision)

1. Form blocks only by looking at **already valid DecodeCache entries**, with no
   speculative bus reads, page walks, faults, accessed-bit writes, or observers.
   Cache misses execute normally and seed later formation. Each entry's entire
   **15-byte fetch window**, not just its instruction length, must fit the same
   4 KiB linear page; all entries must map the same physical page. RAM only.
   Cap each block at 32 instructions initially; cap retained block entries and
   decoded references as well as block count.
2. End at branch (including untaken Jcc/LOOP/JCXZ), direct or indirect call/jump,
   return/far return/IRET, invalid decode/#UD marker, explicit trap/interrupt,
   HLT, page/window boundary, missing entry, or state-changing instruction.
   Conservatively end at CR/segment/descriptor/system changes, INVLPG, cache
   maintenance, STI/CLI/POPF and REP/string/I/O paths initially. Unexpected
   runtime faults end immediately; do not predict memory faults during formation.
3. Key by head guest EIP **and linear PC/CS base**, default operand/address sizes,
   protected-mode/code-size state and mapping generation. Include CR0/CR3/CR4
   identity or generation covering their changes; do not specialize live GPRs,
   EFLAGS, data segments, memory operands, or interrupt source. The precise key
   must document every constant baked into the chosen executor. A mode-changing
   instruction is a terminator even if its successor happens to have the same PC.
4. Keep `Step()` a one-original-boundary API. A block cursor may serve its cached
   next entry, but a public Step call must never execute multiple entries.
   `Execute(maxInstructions)` can reuse that cursor across boundaries and must
   cap execution to the caller's remaining budget. Preserve Halted checks, TSC,
   IRQ poll/ack/transfer order, and the interrupt-only budget case exactly.
   Do not change `XboxMachine.Run`'s device-advance cadence (65536 batches), CLI
   `--break-log`/`--break`/trace 4096-step cadence, or pad-script deadlines.
5. Before each instruction, validate cursor identity/generation and the expected
   EIP; poll IRQ at the existing site. After IRQ transfer discard the cursor.
   Preserve original EIP throughout the semantic body; retain existing commit
   points, #UD/#PF/other vector/error-code paths and partial side effects (no
   blanket rollback, especially REP). Observe idle/hotspot only at the same
   original post-execution site; replay **15** idle `NoteAccess` fetch entries per
   instruction with the cached physical base, exactly as §134 does.
6. Reuse §134's existing invalidation writers and MMU hooks unchanged: CPU stores,
   page-table stores, five host/device shims, EOS shim, CS/mapping changes,
   INVLPG, reset, snapshot load. Extend the cache's central drop/flush machinery
   with block-page generations/reverse links; do not add ad hoc device hooks.
   An executing block must notice self-modification **before its next member**.
   Full flush/cap eviction must invalidate even a cursor held outside the table.
   Beware current Flush's empty-table early return: derived blocks must not
   outlive their entries. Bound metadata/reverse lists; reject generation ABA.
7. `--block-exec` is default-off and implies decode cache. `--block-verify` is
   default-off: per-member full-window comparison **before** execution, report a
   mismatch and leave the stale block, decode fresh, never execute stale bytes.
   Preserve normal decode counters per original instruction. Record block builds,
   hits, instructions, exits by reason, invalidations, peak size and verify failures.
   Configuration/code tables are host state only: no snapshot version change;
   reset/load makes all cursors cold. No new packages, plain `[Fact]` tests.

## Next slice recipe (first establish block correctness)

The wave plan's dependent build task explicitly requests **no host codegen yet**.
A block array still calling `ExecuteInstruction` retains a mnemonic switch per
instruction: call that block lookup/cursor reuse, **not dispatch elimination**.
Do not claim speed merely because a block table exists. A later generated block
can call selected semantic bodies without the switch, but must retain the above
boundary checks and fault bookkeeping at each instruction.

Acceptance for the dependent block slice:

- Unit tests for all terminators, 15-byte window at page tail, missing entry/no
  speculative fault, mode/CS aliasing, SMC in mid-block, physical aliases and
  device invalidation, page-table change, CR3/INVLPG/reset/load, table-cap eviction
  with an active cursor, #UD and #PF precise EIP/vector/error/CR2, IRQ delivered at
  every member (including IF changes), one-Step and budget 1/N transparency,
  idle-fetch replay, halted/REP and verify mismatch before member execution.
- Full suite stays >=1316 green; no package/snapshot changes; format clean.
- Menu and triangle 1B off/on/verify: both binding SHAs and **all** guest counters
  exact; zero verify failures. Triangle requires HDD **and** DVD. Flag-off logs
  byte-identical except intentionally identified output paths/config announcements.
- Trace/break-log differential tests compare pre-instruction EIP/state/step and
  side-effect counts; do not simply disable correctness assertions in block mode.
  Absolute-step diagnostic recipes remain flag-off-only; the task's fixed 1B
  off/on golden runs are explicit differential gates, not revised step recipes.
- Profiler OFF back-to-back decode-cache-only versus block executor pairs on the
  same host; repeat reversed order. Require a reproducible improvement on **both**
  workloads, full counters and SHAs unchanged. If no improvement, report the
  diagnosis, retain opt-in and do not declare a fake throughput gate.
