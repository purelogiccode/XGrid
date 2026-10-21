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
  *Correctness* work is exempt from the freeze (it is not codegen): 220 landed
  nine interpreter/decoder fixes found by the independent oracle, with no
  throughput claim and no gate anchor moved.

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
   but is keyboard-only, silent, and defaults to the software rasterizer (an
   opt-in host-OpenGL renderer landed in §220). In parallel: host
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

**Status 2026-10-22 (22nd session, §§220-221):** the frontend's renderer is a
setting - software (default) or the headless host-GPU OpenGL backend, which
measures **-29% wall** at 1B through the real `EmulatorSession` (34.8 s -> 24.7 s
on gate media) with the same picture; and per-title override V2 resolves a CHD
through a sector-level XDVDFS lookup (§221), so a compressed library keys by
title id. Suite **1757**. Track order is otherwise unchanged; the largest
remaining product gap is host audio, which needs APU DSP execution first.**Status 2026-10-21 (20th session, 215-218):** gap 6 is **closed end to end** -
the fixed-function vertex stage (213) and its enabled-light loop (215) are
opt-in (`--ffp`), PTIMER now models the 0.8.137 alarm registers (216), the APU
sample counter `NV_PAPU_XGSCNT` is derived from the EP frame divider (218, which
re-recorded the two menu RAM anchors; frames/CPU/counters unchanged), and the
frontend exposes the graphics-fidelity switches, per-title overrides and a
persisted EEPROM (217). Suite 1717. Track order is otherwise unchanged; the
largest remaining product gap is host audio, which needs APU DSP execution
first.

**Status 2026-10-21 (21st session, 219):** the GL backend now samples like the
software rasterizer (TEXFILTER tent/convolution, decoded wrap modes + border
colour, the PROJECT2D w divide, the clip origin on both fragment axes) and
`--gpu-replace` composes a software-fallback frame into guest RAM instead of
presenting the clear colour - compatibility gaps 7 (GL side) and 8 are closed.
Both 1B GL gate frames are byte-identical (`B6116DAE` / `7A860ED7`),
`BackendUnsupported=0`, and `xgu vp_lighting` 3B agrees with the software frame
within one 8-bit step in both filter states. Track P gained a frame-step
control (F7 / Shift+F7). Suite 1725; the largest remaining product gap is still
host audio (APU DSP execution first).

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

## 2. Measured position (1B fast-mode legs, reference host, 2026-10-20)

1 guest-second ≈ 733M steps (§68), so 1B steps ≈ 1.364 guest-seconds.
**"xemu-class" means the same content runs at wall ≤ guest-time**, measured
against xemu where an oracle exists (triangle ≈830 Msteps/s; ktest smoke xemu
15 s vs XGrid fast 9–10 s) and against the real-time band otherwise (menu).

**Correction 2026-10-21 (§208d):** the triangle rows below were measured on a
DVD-only leg, which never launches the disc's XBE. On gate media (HDD+DVD) the
same legs cost **11.3 s sw / 7.5 s `gl+replace`** at 1B on H1 (2,706 draws /
68 frames / 57.7M px). Read the table as the boot-animation workload it actually
measured; M-A's triangle target needs re-basing before it is chased.

| leg | wall | raster | xemu reference | gap |
|---|---:|---:|---|---:|
| triangle `--gpu sw` | 8.69 s (corrected: 11.3 s) | 2.8 s | 830 Msteps/s (1.13× real time) | ~7× |
| triangle `gl+replace` | 6.49 s → **~6.4 s** (corrected: 7.5 s) | — | same | ~5.6× |
| menu `--gpu sw` | 33.3 s → **~30 s** | 12.0 s | no oracle (xemu deadlocks) | ~25× real time |
| menu `gl+replace` | 31.1 s → **~23.7 s** | - | same | ~17.4× real time |

**Track B slice 1 (§204) landed:** the GL path's overhead fell from ~7.9 s to
~1.3 s (readback removed from fast-mode present, per-draw submission 2.49 →
~0.5 µs/draw, `BackendUnsupported=0`, guest SHAs/counters identical). Same-window
medians vs `4f0bd47`: menu `gl+replace` **63.09 → 45.25 s (-20.5%, 3/3)**,
triangle 10.26 → 9.89 s (-0.9%, 3/3). The quiet-host projections above are the
ratio method against the previous session's references; the menu is at the
M-A boundary (22-25 s across windows) and the triangle is CPU-bound
(`nonJitMs` 3.75 s of `outerMs` 6.3 s), so it cannot reach 6 s from Track B
alone. Remaining menu GL submission ~1.2-1.5 s, dominated by the texture-content
fingerprint; the exact fix is guest-write span tracking, not weaker sampling.
The shared method-histogram cost (-6.2% menu sw) was also removed (§204 item 8).

**Track B slice 2 (§205) landed — draw-command batching.** A PGRAPH primitive
flush averages **82 triangles**, so the backend now stages a whole flush and
submits it with one instanced draw (per-triangle block = a uniform-array slot,
selected by `gl_InstanceID` through a flat varying; capacity 64 from the uniform
budget; `XGRID_GL_NO_BATCH=1` forces the old path). Same-binary interleaved A/B
at 1B menu `gl+replace`: **`drawMs` 1971 → 1045 ms (-47%, 3/3)**, wall median
42.20 → 40.43 s (**-4.2%, 3/3**), 59,437 instanced draws replace 2.76M single
draws, RAM/CPU SHAs identical, both 1B goldens byte-identical under the §204
policy (menu `6BE6325F.`, triangle `3FC4B419.`). Projected onto the 24.8 s menu
projection that is ~23.8 s against the 24 s M-A target. What is left of
submission is ~803 ms of block packing (100 float stores × 2.76M), not GL calls.


Anchors (fast mode): menu RAM `660CA11B…` / CPU `F5D61C94…`; triangle
`F57934BF…` / `CF79FC17…`. Accurate mode: menu `D9C99A58…`/`3D2740B8…`,
triangle `F57934BF…`/`9B5AFC2A…`. 1B walls are continuity anchors only.

**Two hosts, two anchor classes (2026-10-20).** Development runs on two machines
that share this Dropbox tree; `docs/hosts.md` holds their fingerprints and which
anchors reproduce on each. Every measured table must say which class it is:

- **Semantic anchors** — 1B RAM/CPU SHAs, frame SHAs, and every counter
  (`draws`, `frames`, `stalls`, `BackendUnsupported`, `verifyFailures`,
  `MethodTotal`). These are **host-independent**: a mismatch between machines on
  the same commit is a determinism bug to file and fix, never an "environment
  moved" note. Open item: the §199l fast-mode triangle SHA (`A2DCA55F…`) that
  stopped reproducing — diagnose it, do not document it.
- **Performance anchors** — walls, `msps`, `rasterPsh`, `drawMs`/`flushMs`.
  Host-local, not comparable across machines; only same-window A/B pairs count.

**Reference host** is whichever machine reproduces the quiet-host absolutes (menu
`--gpu sw` ~33 s, triangle ~8.7 s at 1B on the *unmodified baseline*). M-A can
only be recorded there; on any other host `present-path-acceptance.ps1`'s SUMMARY
is a ratio, not a milestone.

## 3. Milestone ladder

- **M-A (Track B, present path):** menu fast+gl ≤ 24 s, triangle ≤ 6 s at 1B;
  `BackendUnsupported=0`; SHAs/counters exact; the Avalonia display is driven
  by presents, not a per-frame readback. **Status 2026-10-20 (§204):** the GL
  overhead (~7.9 s) is removed and measured (menu `gl+replace` -20.5% 3/3,
  triangle -0.9% 3/3 same-window; `drawMs` 12.55 → 2.1 s; readback 1.5 s → 0).
  **M-A itself is not decidable on today's host:** the unmodified baseline build
  measures menu sw 62.6 s / triangle sw 14.0 s against the recorded 33.3 / 8.69
  with an idle CPU, so absolute walls are not comparable. Re-run
  `scripts/present-path-acceptance.ps1` on the reference host state (menu sw
  ~33 s) before recording M-A. If the menu is still over, per-triangle batching
  is the structural step. **The in-app GL display landed 2026-10-22 (§220):** the
  frontend's Renderer setting runs the GPU backend headless with the per-flip readback (-29% wall at 1B through the real session), opt-in and not the default. The in-app switch is +9.8%/+8.2% guest throughput on the dashboard with every gate anchor unchanged (§223).`n- **Display model (Track E/P) - LANDED 2026-10-21 (§208).** The console now
  presents what the display controller scans out: the VGA CRTC behind
  `NV_PRMCIO` is decoded (pitch/size/depth), `TryCopyDisplayFrame` +
  `--frame-display` export it, and the Avalonia frontend samples it at ~30 Hz,
  so direct-framebuffer guests (`gamma`, `hello`) finally show their output.
  `--frame` still exports the last flipped surface, so no frame anchor moved.
  The same session corrected the triangle's media (§208d): every triangle leg
  that used DVD only was measuring the boot animation.
- **M-B (Track E, corpus) - MET 2026-10-20.** 14 rows over 10 distinct samples,
  every row reproducible: `scripts/build-nxdk-samples.ps1` builds the nxdk
  samples from a pinned upstream commit (`a6c31ee`) and
  `scripts/boot-to-first-frame.ps1 -Corpus` re-measures them. Beyond
  dashboard/triangle/ktest: `mesh` (textured 3D with VS/PS shaders - mipmap use
  not yet exercised), `gamma`/`gamma-fade`/`hello` (direct-framebuffer), `led`
  (SMC), `xaudio` (APU voice engine live). The corpus immediately produced two
  measured gaps: the direct-framebuffer present gap and the never-buildable
  `nxdk-mesh` snapshot (both in `docs/compatibility.md`).
- **Cubemap reflection (Track E) - LANDED 2026-10-21 (§209).**
  `xgu bump_cubemap_reflect` went from 2 colours / 36% black to 21,041 colours /
  0% black, `|dmean|` (5.3, 2.4, 1.3) against its reference screenshot. The
  dominant bug was the cube face extent read from `TEXIMAGERECT` instead of
  TEXFMT `BASE_SIZE` (xemu's swizzled-texture rule), plus the three format
  gates. The reflection math needed no change - the "striped" frame that 208g
  blamed on the direction chain was the wrong face stride.
  Same session: **TEXFILTER became a real sampler feature** (tent at LOD 0 +
  quincunx convolution), opt-in as `--tex-filter registers` because it changes
  every textured pixel of the menu and the menu has no oracle. See gap 7.

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
- No `References2/` deletes/moves/rewrites ever. Its single canonical store is
  `References2/` itself; verify with `scripts/refs-manifest.ps1 -Verify` against
  `docs/refs-manifest.json`. Never work around a missing reference; do not
  reintroduce an out-of-tree reference backup (`D:\xgrid-refs-restore\` is
  retired by operator decision 2026-10-20).
- No new codegen slices while Track C is frozen (section 0.4). Interpreter
  *correctness* fixes (oracle-found) are not codegen slices.
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
- Session start on any machine: `pwsh -File scripts/host-doctor.ps1` (fingerprint,
  free space, gate media, toolchain, `References2` integrity). A session that
  skips it owns whatever environment surprise it hits.
