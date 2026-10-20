# XGrid — Development Direction (canonical, revised 2026-10-20)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 0. Course correction (2026-10-20)

**The goal, restated.** A "xemu-like emulator" is four things, in user-value
order: (1) **breadth** — a growing set of real titles boots, renders, and stays
up; (2) **fidelity** — hardware behavior matches the console where titles depend
on it; (3) **playability** — speed, audio, input, save states; (4) **performance**
— the same content in the real-time band. XGrid is strong on (2) as *methodology*
(LLE, exact gates, independent differentials) and on (4) as *engineering* (native
CPU blocks, direct chaining). But its fidelity is steered by three programs, and
its product renderer is a CPU rasterizer. The previous order — Track C leads,
then Track B optimizes the software rasterizer — optimizes two synthetic
workloads and leaves the corpus and product surface frozen. That is the classic
emulator plateau: a very accurate, very well-tested demo.

**What is right and must not change.**

- LLE, no HLE: MCPX ROM → 1BL → 2BL → kernel → XBE. This is the only
  architecture that scales to a compatibility list.
- Accurate mode is gate currency; fast mode is a separate engine validated by
  bit-exact 1B differentials (charter §5). Every default flip keeps the
  median-of-3 + exact SHA/counter ceremony.
- Reference-first: xemu 0.8.137 primary; independent CPU oracle
  (`tools/cpu-oracle`, Unicorn/TCG); non-GPL clean-room.
- The device *set* is complete (MCPX, PCI, IDE/ATAPI, USB-OHCI + XID, SMC,
  SMBus, EEPROM, PIC/PIT/RTC, APU, nvnet, NV2A). Only depth remains.
- The CPU engine (raw emitter, chaining, fault contract) stays landed and
  opt-in. Do not spend more sessions on it until the tracks below clear.

**What changes.**

1. **Track B leads; its first slice is the present path, not the fill loop.**
   The product surface (`XGrid.Frontend.Avalonia`) renders on `--gpu sw` and is
   therefore GPU-bound. `--gpu gl --gpu-replace` exists but nets only 2.2 s on
   the menu (31.1 vs 33.3 s): ~9.9 s of GL draw/readback/present. The
   window-mode 4B run measured 12,675 flips at 1.6 ms readback each (20.7 s/4B,
   ≈1.1 s/1B) — real, but not the whole 9.9 s. **Measure the split first**
   (submission / readback / present), then remove the readback from
   fast-mode/window present (keep it for gates, screenshots, `--frame`). The
   ceiling is ~10 s, larger than any remaining CPU slice; the unexplained part
   is why the measurement is the first deliverable.
   The 2-pixel k1 interleave is demoted (≥2% acceptance, bounded ceiling); take
   it only if the present path stalls.
2. **Track E opens the corpus — the exit from sample-only fidelity.** Gate
   currency stays `data/test-samples/` (triangle, ktest, dashboard), but
   fidelity work gets a steering signal: `docs/compatibility.md` (boot stage,
   first frame, known gap, step anchor) plus a boot-to-first-frame harness;
   widen the sample set with legal homebrew/demos (nxdk samples once the
   toolchain exists, xgu examples, nxdk_vsh_tests goldens, OpenXDK) so
   NV097/APU/XBE work is driven by observed usage. Real ISOs stay benchmark-only
   (no gates, no fidelity work) until the operator changes the policy.
3. **Track P makes it feel like an emulator.** The frontend landed (`ea8b38f`)
   but is sw-only, keyboard-only, silent, and stateless. In parallel: host
   audio sink (APU output), physical gamepad binding, save-state V1, per-title
   config. Cheap relative to their product value; they do not block Track B.
4. **Track C is frozen after the landed slices.** C3-x hit its target
   (8.8 ns/served member triangle, 31.6 ns menu); C3-y and the 1024-slot xlat
   cache measured neutral and were reverted; C3-z is deferred (2–3% estimate).
   The only remaining CPU action is the operator's `--jit`/`--jit-raw`
   default-flip decision (the frontend already defaults both on; the CLI does
   not). Report ns/served member and wall per presented frame, not 1B-step
   walls (530M of the triangle's steps are idle-spin).
5. **Track D only with a measured >2% ceiling.** D3b page-end wait and
   bookkeeping remain candidates, not commitments.

**Steering rule.** Every proposal states which track it serves and the measured
ceiling behind its acceptance. A task that serves none of B/E/P is rejected even
if it is interesting.

## 1. Priority stack (in order)

1. **B — host GPU present path (lead).** Measure the split first (draw
   submission / readback / present) with `--gpu-time`; then fast-mode present
   must blit to the window/surface without a CPU readback. Readback stays only
   for gate runs, `--frame`, and screenshots. Acceptance: menu fast+gl ≤ 24 s,
   triangle fast+gl ≤ 6 s at 1B, `BackendUnsupported=0`, guest SHAs and every
   counter identical to the sw run. Then B3 method coverage on the corpus
   (zero `unsup` rows, guest counters identical). Keep the software rasterizer:
   it is the exact-mode gate renderer and the differential oracle.
2. **E — compatibility corpus.** `docs/compatibility.md` + the harness +
   ≥8 samples (section 3, M-B) or no fidelity work. Acceptance: every row has a
   reproducible run command and a step anchor; a fidelity landing cites the row
   it fixes and the histogram that proves usage.
3. **P — product completeness (parallel).** Host audio output, gamepad
   binding, save-state V1 (the `.xgs` format is versioned), per-title config,
   pause/step UI. Acceptance: the Avalonia app plays a sample with sound, a
   physical pad, and save/load, without the CLI.
4. **C — CPU engine: maintenance only.** The default-flip decision (operator)
   and bug fixes only. No new codegen slices. Re-open only if Track B lands and
   the engine is again >50% of a measured wall.
5. **D — cheap wins with a measured ceiling.** Page-end wait, bookkeeping.
6. **Gates — unchanged and non-negotiable.** Accurate mode is currency.

## 2. Measured position (1B fast-mode legs, quiet host, 2026-10-20)

1 guest-second ≈ 733M steps (§68), so 1B steps ≈ 1.364 guest-seconds.
**"xemu-class" means the same content runs at wall ≤ guest-time**, measured
against xemu where an oracle exists (triangle ≈830 Msteps/s; ktest smoke xemu
15 s vs XGrid fast 9–10 s) and against the real-time band otherwise (menu).

| leg | wall | raster | xemu reference | gap |
|---|---:|---:|---|---:|
| triangle `--gpu sw` | 8.69 s | 2.8 s | 830 Msteps/s (1.13× real time) | ~7× |
| triangle `gl+replace` | 6.49 s | — | same | ~5.6× |
| menu `--gpu sw` | 33.3 s | 12.0 s | no oracle (xemu deadlocks) | ~27× real time |
| menu `gl+replace` | 31.1 s | — | same | ~25× real time |

The menu GL overhead is ~9.9 s for 686 frames; the known per-flip readback is
1.6 ms (≈1.1 s/1B), so ~8.8 s is unexplained and is exactly what the first
measurement pass must split (submission / readback / present). The triangle's
35 flips make the same readback only ~0.06 s. The sw rasterizer is the largest
single item on the menu; the GL path replaces it but has not yet been made to
pay off. That is the lead lever.

Anchors (fast mode): menu RAM `660CA11B…` / CPU `F5D61C94…`; triangle
`F57934BF…` / `CF79FC17…`. Accurate mode: menu `D9C99A58…`/`3D2740B8…`,
triangle `F57934BF…`/`9B5AFC2A…`. 1B walls are continuity anchors only.

## 3. Milestone ladder

- **M-A (Track B, present path):** menu fast+gl ≤ 24 s, triangle ≤ 6 s at 1B;
  `BackendUnsupported=0`; SHAs/counters exact; the Avalonia display is driven
  by presents, not a per-frame readback.
- **M-B (Track E, corpus):** ≥8 samples in `docs/compatibility.md`, at least
  two beyond the dashboard/triangle/ktest (one textured 3D with mipmaps, one
  audio/DVD-path sample), boot-to-first-frame harness green, every row
  reproducible.
- **M-C (Track P, playability):** audio output, physical pad, save/load in the
  frontend; no CLI required.
- **M-D (xemu-class):** triangle ≤ 1.5 s, menu ≤ 4 s at 1B with the fast engine;
  measured against xemu where an oracle exists. M-D is *after* A/B/C, not
  before.

## 4. Rules

**Accurate mode (unchanged; §§136–140, `jit-codegen-scope.md`).**

- Specialize semantics (pre-specialized per-shape handlers + unrolled runner),
  not dispatch. The 354-case switch is NOT dominant — data-path helpers are.
- `DynamicMethod` stays rejected *for accurate-mode relay slices* (~10% over
  unrolled C#; E1–E3 measured wall-neutral). No machine-code emitter for the
  accurate path without beating the unrolled-C# baseline.
- Boundary-preserving only: `Step()` = one original boundary (TSC, IRQ poll,
  faults, idle replay unchanged). `--decode-cache` default-on (opt-out
  `--no-decode-cache`); `--block-exec` / `--spec-exec` / `--fetch-fast` /
  `--decode-verify` stay opt-in, default-off.
- Accurate-mode promotion + every default flip still needs: 1B off/cache/(new)
  pairs on menu + triangle with SHAs (`4EF1FBF6…`, `A1740DE0…`) + all counters
  exact, `verifyFailures=0`, vec=0, flag-off logs byte-identical, median-of-3
  walls before claiming a win.

**Fast mode.** Governing doc: `docs/research-notes/jit-track-charter.md`.
Fast mode (`--jit`, opt-in) is a separate engine: MMIO program order and fault
precision retained; TSC/step/IRQ observability relaxed to block granularity;
deterministic per binary+script; never gate currency; one engine per run (no
mixing). `--jit-exact` is the bit-exact debug variant. The raw emitter (Iced,
MIT) is authorized and landed; it is frozen by section 0.4.

**GPU / present.**

- Headless FBO (`--gpu sw`) is the gate currency. Never mix (full-frame replace
  or no frame), no async (emulation-thread only).
- The GL backend is the fast-mode product renderer (Track B). Readback is a
  gate/screenshot facility, not a per-frame cost.
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs an
  operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish beyond `docs/compatibility.md` + a sample histogram
  (Track E gates it). Exotics, DXT/BRDF/mipmaps, cost model, seeds, writeback
  stay gated on observed usage.

**Corpus.** `data/test-samples/` stays gate currency. Real Xbox ISOs at
`H:\Tests\XBOXTest` are authorized for fast-mode **benchmark-only** use
(wall/throughput, xemu-class comparisons) — never gates, never accurate-mode
acceptance, never fidelity work. A title quirk is allowed only when isolated,
documented, and not a substitute for the hardware behavior (the 007 EOS quirk is
the historical precedent, not a pattern to extend).

## 5. Hard non-goals

- **License stays non-GPL.** No QEMU/xemu code translation; GPL is not the
  TCG-port vehicle (charter §1/§6). New deps: only Iced (MIT, Phase 3) approved
  so far; otherwise the NuGet rule stands (precedent: `Xunit.SkippableFact`
  rejected; plain `[Fact]` + WGL-guard instead).
- No `References2/` deletes/moves/rewrites ever (Dropbox sync root — restore
  from `D:\xgrid-refs-restore\`, never work around a missing reference).
- No new codegen slices while Track C is frozen (section 0.4).
- No micro-optimization below a measured 2% ceiling.
- No "borrow" of xemu source; facts only, reimplemented clean-room.

## 6. Standing gates

- `dotnet build XGrid.sln -c Release`, `dotnet test`, `dotnet format
  --verify-no-changes` clean; warnings are errors.
- ktest smoke 7/7 (~5 min, 792,800,535 steps bit-reproducible) + menu/triangle
  1B golden pairs before any perf/default-flip merge.
- Frontend changes: `dotnet build src/XGrid.Frontend.Avalonia -c Release` +
  project format clean; UI-only work does not re-run core gates, but it must not
  touch core/CLI/test files.
- Every Track B/E/P landing updates `ImplementationPlan.md` status,
  `docs/compatibility.md` (when it changes a sample row), and the session notes.
