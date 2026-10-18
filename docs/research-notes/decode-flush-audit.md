# Decode-cache flush / invalidation audit (menu 1B golden)

Scope: XGrid decode cache (`src/XGrid.Cpu/DecodeCache.cs`) + derived block cache
(`src/XGrid.Cpu/DecodedBlockCache.cs`), default-on. Question: what produces
`fullFlushes=83265` on the 1B menu run, which triggers are over-conservative,
and what can be narrowed without ever executing stale decoded code.

All file:line references are against the working tree of
`C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator` as of 2026-10-17.
No code was modified; this is a read-only audit. No instrumentation was run.

---

## 0. Evidence base (measured counters only)

| run | steps | fullFlushes | pageDrops | fills | entries | peak | blockBuilds | blockInvalidations | capFlushes | crossPageSkips | interrupts |
|---|---|---|---|---|---|---|---|---|---|---|---|
| slice23 probe (2BL) `D:\spec-soak\slice23\probe-fixed.log` | 10,000,000 | 12 | 2 | 3,528 | 2,019 | 2,019 | (n/a) | (n/a) | | 17,047 | 0 |
| slice23 probe (kernel early) `probe-engage.log` | 50,000,000 | 3,840 | 65 | 157,373 | 7,662 | 7,678 | (n/a) | (n/a) | | 89,520 | |
| ktest smoke `wt-data\ktest-smoke\xgrid\xgrid.log` | 792,800,535 | 42,163 | 3,162 | 5,223,005 | 1,061 | 8,100 | (n/a) | (n/a) | | 828,384 | 2,894 |
| menu spec `D:\spec-soak\quiet1\menu-spec-r11.log` | 1,000,000,000 | **83,265** | 4,069 | 3,103,139 | 10,192 | 12,644 | 1,344,447 | 5,534 | 0 | 1,222,767 | 3,153 |
| triangle spec `D:\spec-soak\quiet1\triangle-spec-r11.log` | 1,000,000,000 | 31,781 | 352 | 880,681 | 4,156 | 8,100 | 92,524 | 750 | 0 | 761,045 | 1,883 |
| menu soak `D:\spec-soak\soak3b\menu-3b.log` | 3,000,000,000 | 85,070 | 12,936 | 7,250,979 | 10,264 | 12,644 | 16,507,159 | 15,110 | 0 | 6,284,311 | |

Derived deltas:

* menu 10M->50M: +3,828 flushes over 40M steps = **95.7 flushes / M steps**
* menu 50M->1B: +79,425 over 950M steps = **83.6 / M**
* menu 1B->3B (steady menu loop): **+1,805 over 2B steps = 0.90 / M** (a 90x drop)
* triangle whole run: 31.8 / M; ktest whole run: 53.2 / M (ktest is allocation-churn heavy:
  fills 6.6M/M steps with a 1,061-entry live set)
* menu 1B->3B fills: +4,147,840 over 2B = **2.07M fills / 1B steady steps**
* menu 1B->3B blockBuilds: +15,160,712 over 2B = **7.58M builds / 1B steady steps**
  (= 5.2% of the 294M dispatches in the same window)
* menu 1B->3B blockInvalidations: +9,576; pageDrops +8,867; fullFlushes +1,805
* menu 1B->3B frames: +8,362  => **~1.06 effective page drops per frame, ~0.22 flushes per frame**

Counter identities that make the arithmetic exact:

* `misses = fills + crossPageSkips + nonRamSkips` (menu 1B: 4,679,841 = 3,103,139 + 1,222,767 + 353,935) —
  so every miss is accounted; no hidden insert path.
* removals = `fills + liveStart - liveEnd`. Cache starts empty at CLI start, so menu 1B removals
  = 3,103,139 - 10,192 = **3.09M entries removed** by (a) full flushes, (b) `DropPhysicalPage`
  (incl. the Insert-replacement variant). Nothing else removes entries.
* `FullFlushes` counts only *effective* flushes (table non-empty at Flush); a Mmu.Flush on an empty
  table is silent for the counter but still runs `Blocks?.Invalidate()` + `InvalidateDataXlat()`.

---

## 1. Trigger inventory — every invalidation path (full vs page)

### 1a. FULL-flush paths (whole decode table + whole block table)

| # | Trigger | Code site | Notes / guest semantics |
|---|---|---|---|
| F1 | CR0 write (`mov cr0, r`) | `XboxCpu.cs:1983-1993` + tail `:2018` | Conditional flush when the PG bit toggles, then an **unconditional** tail flush => every CR0 write flushes (twice when PG toggles). |
| F2 | CR2 write (`mov cr2, r`) | `XboxCpu.cs:1995-1997` + tail `:2018` | CR2 is the fault-address register; architecturally it has **no TLB effect at all**. The tail flush makes every CR2 write a full decode+block flush. |
| F3 | CR3 write (`mov cr3, r`) | `XboxCpu.cs:1998-2001` + tail `:2018` | Case 3 flushes, then the tail flushes again (second is counter-silent: table already empty). Architecturally correct to flush; the duplicate is pure waste (2x `Array.Clear` of the two 16 KB bitsets, `SoftTlb` clear, `Blocks.Invalidate()` generation bump). |
| F4 | CR4 write | `XboxCpu.cs:2002-2012` + tail `:2018` | Same pattern as CR0 (PSE check, then unconditional tail). |
| F5 | INVD / WBINVD | `XboxCpu.cs:753-756` | Architectural full flush. |
| F6 | **INVLPG** | `XboxCpu.cs:749-751` -> `Mmu.FlushPage` `Mmu.cs:96-100` -> `OnMmuFlushPage` `XboxCpuDecodeCache.cs:762-766` | Architecturally invalidates **one linear page**; XGrid full-flushes (comment: v1 table carries no linear-page index). Prime suspect for the boot-phase flush volume. |
| F7 | CS reload with changed base or D/B | `XboxCpu.cs:1479-1491` | Already narrowed: same-base reloads (every interrupt/iret) do not flush; test-pinned (`DecodeCacheTests.cs:236-260`). All CS reloads (jmp/call far, retf, iret, interrupt gates) route through it. |
| F8 | Guest store to a **tracked** page-table page | `XboxCpuDecodeCache.cs:717-738` (flush at `:735`) | Fires when `Mmu.IsPageTablePage(first|lastPage)`; a PTE write may remap *another* page, so the coarse answer is full flush. Tracking is epoch-scoped: `Mmu.Flush()` clears `_pageTablePages` (`Mmu.cs:87-90`); walks re-record (`Mmu.cs:250,282`). |
| F9 | Decode table cap | `DecodeCache.cs:189-192` | `MaxEntries=131,072`; never reached (peak 12,644) => 0 fires. |
| F10 | Reset | `XboxCpu.cs:130` (`Reset` -> `Mmu.Flush`) | Host-driven, once per boot. |
| F11 | Snapshot load | `XboxCpuSnapshots.cs:172` | Host-driven, 0 in these runs. |
| F12 | Cold-start of the cache | `XboxCpuDecodeCache.cs:190-200` (`Mmu.Flush` at `:198`) | Once, at CLI flag application (`Program.cs:409`). |
| F13 | Host API `InvalidateDecodeCache()` | `XboxCpuDecodeCache.cs:265-268` | **No runtime callers** (only unit tests). Dead in boot runs. |

Every one of F1-F12 funnels through `Mmu.OnFlush` -> `OnMmuFlush` (`XboxCpuDecodeCache.cs:752-756`)
-> `DecodeCache.Flush()` (`DecodeCache.cs:305-317`), which also calls `Blocks?.Invalidate()`
(`DecodedBlockCache.cs:170-181`) and `InvalidateDataXlat()`.

### 1b. PAGE-scoped drops (`DropPhysicalPage`, `DecodeCache.cs:282-302`)

| # | Trigger | Code site | Notes |
|---|---|---|---|
| P1 | Any other CPU store (8/16/32/64-bit, strings, rep-movs span, XMM/FPU stores, emitted-tail stores) | `NoteStorePhysical` `XboxCpuDecodeCache.cs:717-745` (drops at `:740-744`); all store sites `XboxCpu.cs:1154,1200,1218,2495,2565,2816,2821,2833,2838,2848,2889,2992,3002,3019,3028,3044,3048,4903`; emit `BlockEmitter.cs:631,646`, `XboxCpuEmitInline.cs:74` | Drops the store page(s) of the *post-translate physical*. A no-op (bitset test) for pages with no cached decodes — the common case for data pages. |
| P2 | Insert replacement (same linear key, changed physical) | `DecodeCache.cs:206-213` | Counted through the same `PageDrops` counter. |
| P3 | Device/DMA writers (the five XboxMachine adapters + EOS quirk) | `XboxMachine.cs:748,754` (APU), `:782` (nvnet), `:820,826,832` (USB), `:877` (NV2A surface/zeta/semaphore; ~285M writes per 1B menu steps), `:925` (IDE BM-DMA), `:449-462` (007 EOS quirk WriteVa) | All page-granular by design (§134 deviation note): full flush on the GPU shim would keep the table permanently cold. |
| P4 | Verify tripwire (self-healing) | `XboxCpuDecodeCache.cs:329-341`, `:438-448` | `verifyFailures=0` everywhere; 0 fires. |

### 1c. Things that do NOT invalidate (checked explicitly)

* **No trigger invalidates on all guest RAM writes.** CPU stores are page-scoped (P1); device writes
  are page-scoped (P3). The present-upload dirty watch (`PhysicalMemory.cs:136-197`, `IPresentDirtyWatch`)
  is GL-present-only and never touches the decode cache.
* The MMU's own A/D-bit writes (`Mmu.cs:272,302`) are Mmu-internal `Memory.Write32` calls and never
  reach `NoteStorePhysical` — no false drops by construction (comment `Mmu.cs:106-107`).
* `Program.MachineGpuMemory` (GL adapter, `Program.cs:2302-2352`) writes RAM without decode
  invalidation, but the GL backend never calls `WritePhysical` (only `ReadPhysical` at
  `GlPgraphBackend.cs:1159-1191`) — latent-only risk, irrelevant to these sw runs.
* Real-mode interrupt CS reload (`XboxCpu.cs:1923`) bypasses `LoadSegment`; reachable only with
  paging off (identity mappings, stable bases) — no staleness.
* CLI default write-watch `0x35B00-0x35D00` (`Program.cs:108-109`, armed at `:557-558`) is a
  host-side diagnostic only (kernel BSS per research note §68); it forces those stores onto the
  bus path but does not flush anything.

---

## 2. Which trigger fires ~83K times? (attribution)

### 2a. Where the flushes are (measured)

* 10M steps: 12 flushes (2BL/ROM phase; PT stores there are untracked and paging is off).
* 50M steps: 3,840 (kernel boot under way; rate 95.7/M over 10M-50M).
* 1B steps: 83,265. **The kernel-boot + dashboard-launch phase (50M-1B) owns 79,425 of them.**
* 3B steps: 85,070 — the entire menu steady loop (1B-3B, 2B steps, 8,362 frames) adds only
  **1,805 flushes (~0.9/M steps)**.
* triangle (same kernel boot, DVD title instead of dashboard) = 31,781: menu - triangle =
  51,484, i.e. the dashboard boot/menu phase is the flush-heaviest episode (confounded by the
  triangle's own title phase, but it is small: 68 frames).
* ktest (same kernel boot, kernel test suite, no dashboard) = 42,163 at 792.8M steps with 5.2M
  fills and a 1,061-entry live set — allocation churn dominates.

### 2b. Burst structure (derived, exact)

* Block-table invalidations are counted only when the block table was non-empty:
  menu 1B: 5,534 of 87,334 invalidation events = **6.3%**. During normal execution the block table
  refills within a few dispatches, so a 93.7% empty-at-event share means the flushes arrive in
  **tight bursts** (consecutive flush events with too few instructions between them to re-form a
  block: a block needs the head decode entry to be re-filled and the head re-dispatched).
* Average per burst: ~87,334 events / ~5,534 non-empty starts ~= 15-16 events per burst.
* Average live entries at flush time (menu 1B): fills/flushes ~= 37; hits/flushes ~= 12K executions
  between flush events.
* Forced by the removals identity (section 0): boot-phase flushes must hit a *cold* cache
  (~30 entries average); steady-state page drops must remove ~460 entries each
  (4.15M removals over 2B steps cannot be explained by 1,805 flushes without contradicting the
  10K live set — the flushes must be clustered where the table is small, the drops must be on hot
  code pages).

### 2c. Ranked attribution (model-derived; no per-trigger counters exist)

1. **PTE/PDE stores to tracked page-table pages (F8) + INVLPG (F6), paired.** A classic kernel
   PTE-update is `store PTE; invlpg` => **2 full flushes per page update**. The sustained ~84-96/M
   step rate through the whole boot matches the kernel's memory-management volume (identity-map and
   process/PTE construction, contiguous-memory unmap/remap churn, XBE section mapping, dashboard
   D3D/audio/texture allocations), and the near-total stop in the idle menu (0.9/M) matches "no
   more allocation churn". Estimated 30-60K fires each at 1B (i.e., 15-30K page updates x 2).
   Confidence: medium-high on the class, low on the split between F6 and F8.
2. **CR3 writes (F3)**: process creations during boot, context switches between processes.
   Bounded by scheduler activity (interrupts=3,153 at 1B); every write costs a double flush.
   Estimated <= a few hundred to ~1-2K fires (each counted twice). Confidence: low (no counter);
   steady state ~0.
3. **CR0/CR4 writes (F1/F4)**: boot-time only (no CR0.TS/EM toggling visible; final CR0=0x8001002B).
   Estimated <= tens.
4. **CR2 writes (F2)**: unknown; the page-fault path *reads* CR2 (`mov r, cr2`); no evidence the
   Xbox kernel writes it. If it does (e.g. per fault), this could be thousands. Estimated 0-500.
   Confidence: low.
5. **INVD/WBINVD (F5)**: 0-5.
6. **CS changed-base (F7)**: ~0 after 2BL (flat 32-bit segments; same-base reloads proven no-flush).
7. **Reset/snapshot/cold-start (F10-F12)**: 1 each, host-side.
8. **Cap (F9)**: 0 — proven by peak 12,644 << 131,072. **EOS quirk**: 0 (menu log line:
   `checks=15258 fires=0`). **Host API (F13)**: 0 callers.

Excluded by construction: the device shims (P3) and CPU stores (P1) never full-flush; the 285M/1B
NV2A pixel writes only ever *drop* pages that hold cached code (see section 4 for why ~1/frame does).

---

## 3. Over-conservatism judgments (correctness bar: never execute stale decoded code)

| Trigger | Over-conservative? | Narrowing sound? | Risk |
|---|---|---|---|
| F6 INVLPG -> full flush | **Yes, severely** (drops 10K entries for a 1-page event) | Yes: x86 semantics are exactly "invalidate the entry for the page containing the operand". A linear-page index (fetchLinear>>12 -> keys) would drop exactly that page. Soundness rests on the same contract the whole cache already assumes: the guest INVLPGs after each PTE change (that is why verifyFailures=0 today). F8 remains the belt-and-braces for untracked PT writes. | Medium: must keep F8 conservative for untracked pages; if the guest ever batched N PTE writes behind one INVLPG, F8 (tracked) still catches each store; F8-untracked pages rely on the guest discipline (same as today). |
| F8 PT-page store -> full flush | **Yes** (one PTE write remaps exactly one linear page) | Yes, if the PT page -> linear page(s) association is known. The MMU walk already knows both (`Mmu.cs:282` records the PT page; the walk has `linearAddress`). Record (ptPage -> linear 4 KB pages) and drop exactly those; keep full flush for PDE-page (CR3-page) stores (a PDE remaps 4 MB) or drop the 4 MB range. | Medium-high complexity; must handle aliased PT pages (same PT page referenced from multiple PDEs / shared page tables). |
| F2 CR2 write -> full flush | **Yes, 100% pure waste** (CR2 has no mapping effect) | Remove the flush for case 2. Zero semantic risk. | None. |
| F1/F4 CR0/CR4 tail flush | Yes (flush even when no paging-relevant bit changed) | Flush only when CR0.PG (already checked) / CR0.PE / CR4.PSE actually change; the unconditional tail at `XboxCpu.cs:2018` can be deleted for cases 0/2/4 (keep for CR3). | None for CR0.PE/PG/CR4.PSE (mapping-relevant bits). Note: if the guest toggles other CR0 bits (WP, cache) the mapping does not change; WP affects fault semantics only and the WP check reads `Context.Cr0` live. |
| F3 CR3 duplicate flush | Yes (second call is a no-op for the table but repeats 32 KB of `Array.Clear` + block generation bump) | Drop the tail flush for case 3 (keep the case-3 flush). Optionally skip when the base is unchanged (no global pages emulated). | Low: Intel says MOV CR3 always invalidates; skipping a same-value write cannot change any emulated mapping. |
| F7 CS reload | Already narrow | No further need. | - |
| F9 cap | Never fires | - | - |
| P1/P3 page drops | **Page granularity is the over-conservatism**: a 4-byte data write drops every cached decode on the 4 KB page (up to ~hundreds of entries) AND wipes the entire block table (`DecodeCache.cs:289` -> `DecodedBlockCache.Invalidate`) | Yes: an entry's 15-byte fetch window only changes if the store overlaps it. A line/window-granular reverse map (e.g. 64-byte lines) is exactly conservative. Blocks are single-physical-page by construction (`DecodedBlockCache.cs:369-380` breaks when the page changes), so a per-page version stamp can let blocks on *other* pages survive a drop. | Medium: new reverse index + block versioning; must keep mapping-change (full flush) semantics and the emitted-runner generation guard consistent. |

**Is anything invalidating on ALL guest writes to RAM?** No. (Checked: `NoteStorePhysical` is
page-scoped; the dirty-bit watch is present-upload-only; the MMU A/D writes bypass the hooks.)
The over-invalidation is (i) full flushes for 1-page mapping events (F6/F8) and (ii) page-granular
drops + whole-block-table wipes for arbitrary data writes (P1/P3).

---

## 4. Counter relationships (Q4/Q5)

**Q4 - do fullFlushes explain builds=1,344,447?** Not as a direct product. builds =
(non-empty invalidation events) x (live blocks cleared per event):

* non-empty events 5,534; builds/event = 1,344,447 / 5,534 = **243 blocks** (matches the average
  live block table size; peak 1,968, steady entries 986-1,429).
* Only 6.3% of the 87,334 invalidation events (83,265 flushes + 4,069 drops) found a non-empty
  block table => flush bursts (section 2b).
* Which events are non-empty? Page drops happen during active execution when the block table is
  warm (they should be non-empty almost always); burst flushes happen when it is cold. Model split:
  ~4,000 of 5,534 non-empty events are page drops (=> ~0.97M builds) and ~1,500 are flush starts
  (=> ~0.36M builds). So at 1B the **block-rebuild budget is driven mostly by page drops**, while
  the **decode-refill budget (fills) is driven mostly by flush bursts** (~2.5M of 3.1M fills).
* Builds are NOT dominated by genuine new code: the live block table is tiny (986-1,429) and the
  block cache has no capacity eviction in these runs (`capFlushes=0`), so ~all builds are rebuilds
  after invalidations.

**Steady-state inversion (1B->3B).** fullFlushes collapse to 1,805 but pageDrops stay at 8,867 and
block builds explode to 15.2M (7.58M/1B = 5.2% of dispatches; one drop every ~1.06 frames wipes the
whole block table -> ~1,583 rebuilds each). The steady menu loop's invalidation cost is
**page-drop-driven, not flush-driven**: the guest writes some page that holds ~460 cached decodes
about once per frame, and the wholesale block invalidation turns each such write into a
whole-table rebuild. (Identifying the exact page/writer needs instrumentation - see section 7.)

**Q5 - capFlushes=0 / is the block cache dropped by the same flushes?**
* `capFlushes` = `DecodedBlockCache.TryNext` eviction when the table would exceed `MaxBlocks=4096`
  (`DecodedBlockCache.cs:310-314`). It is 0 because the block table peaks at 1,968. So no block
  drops come from capacity; **all block drops are invalidation-driven**.
* Yes, the block cache is dropped by the same flushes: every `DecodeCache.Flush()` and every
  effective `DropPhysicalPage` calls `Blocks?.Invalidate()` (`DecodeCache.cs:289,307`), clearing all
  blocks and bumping `Generation` (which also invalidates cached emitted runners on next use).
  `invalidations=5,534` only counts events where the table was non-empty (the empty-table wipes are
  free but still bump `Generation`).

---

## 5. Ranked narrowing opportunities

Estimated benefit basis: menu spec 1B wall 142.6 s (~7.0M steps/s); steady-state wall ~143 s/1B.
Churn costs are estimates (no micro-benchmarks run): decode miss+fill ~0.5-1.5 us,
block Form/build ~0.5-1.5 us (2-4 small array allocations + up to 32 dictionary lookups per build).

**R1 (best candidate) - stop whole-block-table invalidation on page drops; version blocks per
physical page.**
Blocks are single-physical-page by construction (`DecodedBlockCache.cs:369-380`). Give each block a
(page, version) stamp; `DropPhysicalPage` bumps the page's version instead of `Blocks.Invalidate()`;
`TryNext`/`TryBeginTail` reject a block whose stamp is stale (then Form re-forms it from the decode
cache exactly as after today's wipe). Full flushes (mapping changes) keep wiping everything.
* Soundness: a block is served only while (a) every member's decode entry is still in the decode
  cache (guaranteed: a drop removes exactly the entries whose source page was written, and any block
  on that page is version-stamped stale) and (b) the mapping has not changed (full flush keeps
  wiping). No new stale-execution surface: today's behavior is strictly coarser.
* Benefit: steady-state builds 7.58M/1B -> ~1.5-2.5M/1B (only blocks on the dropped page, ~30% of
  the table, die) => recover ~5M builds/1B; at ~0.5-1.5 us each that is **~2.5-7 s per 1B steady
  steps (~2-5% of wall)**, plus the same effect on the 1B run's 1.34M builds (~-60%).
  This is the single largest measured invalidation cost in the menu run.
* Risks: medium. Must bump the version on every effective drop site (CPU stores, shims, EOS,
  Insert-replacement) and keep the emitted-runner guard correct (a stale block must not serve a
  cached runner built for it - either version the runner with the block stamp or keep a global
  generation for runners only). Host-side only; no snapshot format change.

**R2 - line/window-precise page drops (finer reverse map).**
Replace the page-keyed reverse map (`_byPhysicalPage`, 4 KB granularity) with a coarser-than-entry,
finer-than-page granularity (e.g. 64-byte lines: an entry registers the lines its 15-byte window
touches). A store drops only entries whose registered lines overlap the stored bytes.
* Soundness: exactly conservative (an unchanged 15-byte window cannot be affected by a store that
  does not overlap it). Combined with R1's per-line (or per-page) versioning this also stops
  irrelevant block wipes.
* Benefit: attacks the steady-state decode refills (2.07M/1B) and, with line-level versions, most of
  the steady block wipes too - IF the per-frame writer is a data structure sharing a page with hot
  code (which the counters suggest). Unknown until the writer is identified (section 7).
* Risks: medium; new index memory (~one list entry per cached key).

**R3 - page-precise PT-store (F8) + linear-page INVLPG (F6).**
Drop only the affected linear page's entries: (a) INVLPG: drop keys with `fetchLinear>>12 == page`
(new linear-page index); (b) PT store: record (ptPage -> linear pages) during TLB walks
(`Mmu.cs:282` already has both values) and drop exactly those; PDE-page stores keep full flush
(or drop the 4 MB range). Keep the F8 fallback (full flush) whenever the PT page association is
unknown/untracked.
* Soundness: x86 semantics; each PTE write changes exactly one page's mapping; each INVLPG
  invalidates exactly one page. The guest's own discipline (INVLPG per change) is the same contract
  today's verifyFailures=0 proof rests on.
* Benefit: removes most of the boot-phase flush burst cost: up to ~2-2.5M of the 3.1M fills at 1B
  plus flush bookkeeping => **~1-3% of the 1B wall**; larger relative effect on the boot phase,
  small on steady state (which has ~0 flushes).
* Risks: higher complexity; tracking must be airtight; keep the `--decode-verify` gate binding.

**R4 - CR-write flush cleanup (F1-F4).** Remove the CR2 flush (F2); remove the duplicate tail flush
for CR3 (F3); flush CR0/CR4 only on paging-relevant bit changes (F1/F4). ~10 lines, no semantic
risk. Benefit unquantified (likely <1% of flushes; CR-write counts are unmeasured - could be ~0).
Worth doing as pure hygiene, not as a performance bet.

**R5 (adjacent, not an invalidation change) - cheaper block builds.** Form allocates 2-4 arrays per
build and performs up to 32 dictionary lookups; at 7.6M builds/1B steady steps the allocation churn
is a first-order cost. Pooling/arena-allocating block arrays or storing entries in one reusable
structure would reduce R1's remaining rebuild cost. Flag-gated per the perf-flag rule.

Expected total recoverable churn: roughly **2-6% of menu wall** (steady-state dominant via R1+R2;
boot-phase share via R3); triangle is much less affected (92K builds, 880K fills per 1B).

---

## 6. Do-not-touch list (must stay conservative)

1. **CR3 base change -> full flush** (`XboxCpu.cs:2000`). Mapping changes under a new directory
   invalidate every linear key; keep. (Only the *duplicate* tail call is removable.)
2. **CR0.PG / CR0.PE / CR4.PSE changes -> full flush** (mapping semantics).
3. **INVD/WBINVD -> full flush** (architectural).
4. **CS changed-base/D-B reload -> full flush** (`XboxCpu.cs:1487-1490`). Keep the
   changed-only narrowing (same-base reloads must stay no-flush, test-pinned).
5. **Snapshot load -> full flush** (`XboxCpuSnapshots.cs:172`).
6. **PT-page store -> full flush as long as the PT->linear association is unknown** (F8). Dropping
   the store's own page here would be WRONG: the written PTE remaps a *different* page whose cached
   entries would silently go stale. The existing comment (`XboxCpuDecodeCache.cs:729-734`) is
   correct; only narrow this together with R3's association tracking.
7. **Full flush must keep wiping the block table** (mapping changes invalidate block keys). R1 must
   not weaken this path.
8. **Device-shim page drops** (`XboxMachine.cs`) - already minimal (bitset test); do not convert to
   full flushes and do not remove (the NV2A twin's 285M/1B pixel writes need the drop check).
9. **The `_activePages` fast path** and the conservative empty-table flush behavior (still needed to
   bump `Generation` for runners) - keep.
10. **The 16-bit real-mode interrupt CS path** (`XboxCpu.cs:1923`) - no flush needed only because it
    runs pre-paging; if that path ever becomes reachable with paging on, it needs a CS-changed flush.

---

## 7. Open questions and the exact probe that would settle them (not run here)

1. **Per-trigger flush counters** (add a 6-way enum counter at the F1-F12 call sites; ~30 min of
   work in a scratch build): settles F6 vs F8 vs F3 shares of the 83,265.
2. **Steady-state drop writer**: log (physical page, writer kind, EIP, offset) for every effective
   `DropPhysicalPage`; bucket per frame. This identifies the page holding ~460 decodes that is
   written ~once per frame and tells whether R2 needs line granularity or the writer is a device.
3. **CR-write counts**: count MOV CR2/CR0/CR3/CR4 executions at the dispatch site (mnemonic counters
   already exist in the profiler; top-25 hides them). Bounds R4's value.
4. **Live-entries-at-flush histogram**: bucket `_entries.Count` at each `Flush()` to confirm the
   burst/cold-cache model and quantify R3's refill savings directly.
5. **Build cost micro-benchmark**: time `DecodedBlockCache.Form` on the hot head set to firm up R1/R5
   numbers.

Until (1) and (2) are measured, treat the per-trigger estimates in section 2c as model-derived, not
measured.
