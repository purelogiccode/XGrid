# Next session instructions (updated 2026-10-22 late, after §238: the non-JIT run slice went 256 -> 64 for another -3.7% / -2.2%, and §237 made the boot ROM a cached, blocked image window)

**Read in this order:** `docs/development-direction.md` §0 (the 2026-10-22
course correction: xemu-class performance is the long-run objective, Track C
re-opened, C4-C6, now with the §237/§238 status line), §1-§3 (priority stack,
measured position, milestone ladder),
`docs/research-notes/jit-track-charter.md` (the Phase 3b status block incl. the
C7 decision inputs),
**`docs/research-notes/xgrid-architecture-decisions.md` §238 (the `JitSpecSlice`
sweep - two constants, one A/B, 2-4%)**, **§237 (the image-window class, the
measured table, the fast-mode spin-skip trap, the repricing)**, §236 (the cube
2D-array decode), §235 (trace formation priced out), §234 (the display source
under a GPU backend + the encoder price), §233 (the per-stage price of the whole
wall - this is the map), §232 (C4 null, the non-JIT decomposition) + §231 (the
course correction) + §230/§228/§227/§226 (the B3 history) + §225 (CPU
correctness campaign) + §224 + §223 + §222 + §221 + §220 + §219 + §218 + §217 +
§216 + §215 + §213 + §212/§210 (TEXFILTER) + §211 (gap 10 diagnosed) + §209
(cubemap) + §208 (display model) + §207 (corpus),
`AGENTS.md` (session notes + current state), `docs/compatibility.md`,
`docs/research-notes/corpus-phase-anchors.md` (a row's step count is part of its
identity), `docs/research-notes/nxdk-usb-sample.md`,
`docs/research-notes/computed-mode-coverage.md` (the B3 phase table),
`docs/research-notes/usage-histogram.md`,
`docs/research-notes/nv097-coverage.md`, `docs/research-notes/gpu-reference-map.md`,
`docs/research-notes/rasterizer-inventory.md`, `tools/cpu-oracle/README.md`,
then this file. (If a name above does not resolve, the file was renamed - search
`docs/research-notes/` rather than skipping it.)

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest + MSYS2 toolchain
git log --oneline -1        # expect: the §237 commit ("cpu: ... image windows ...")
git status --short          # must be clean (a harness file like .repofleet-task.md is not ours)
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1863 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window. **Wall noise on H1 is
+-3-5%**: a claim needs >=3 interleaved pairs and 3/3 sign agreement (233).

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD;
`host-doctor.ps1` names them - `wt-data\chain-199l\hdd.qcow2` is the HDD used by
the gate scripts on H1). These did **not** move in §237:

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 30.1 s |
| menu + `--tex-filter registers` | `EE8F69FB…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `CC2A94DF…` | `F5D61C94…` | ~59 s |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.3 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` | `B45254CA…` | `7DAB63EB…` | 10.3-10.5 s (frame `A1740DE0`, 68/34, 57,663,082 px) |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.9 s (frame identical too) |
| triangle `--gpu gl --gpu-replace` 1B | `4F1FCBC0…` | `7DAB63EB…` | **6.08 s** (was 6.67), frame `7A860ED7…`, `source=backend` |
| menu `--gpu gl --gpu-replace` 1B | `B06BA6E5…` | `F5D61C94…` | **19.76 s** (was 20.46), frame `B6116DAE…` |
| `xgu-spheremap_ffp` 3B BTFF | off `B5747761…` / on `8062CFB5…` | `F0075C2F…` (both) | ~9 / ~14 s |
| `xgu-distance_fog_ffp` 3B BTFF | off `AFF08085…` / on `12BAD517…` | `571363B4…` (both) | ~9 / ~30 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |
| `xgu-bump_cubemap_reflect` 3.6B BTFF | `5ACD06FF…` | `C848610E…` | 12.0 s, frame `BFC41A59…` |
| `xgu-bump_cubemap_reflect` 3.6B **gl+replace** (§236) | `52CE70A3…` | `C848610E…` | 10.1 s, frame `0AE9A617…`, **2 px from software** |
| `xgu-bump_cubemap_reflect` 4.4B BTFF | `D90376B9…` | `EBAC6B99…` | 14.3 s, frame `5A949A40…` |
| `xgu-bump_cubemap_reflect` 4.4B **gl+replace** (§236) | `17ABCA37…` | `EBAC6B99…` | 10.4 s, frame `CF65CDC2…`, **2 px from software** |
| `xgu-vp_lighting` 3B BTFF | `162DAFF1…` | `0380C502…` | 9.0 s, frame `72B2F490…` |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |
| `nxdk/usb` 1B BTFF (§229) | `ACDB5720…` | `1590C697…` | 7.8 s, display `119AFA48…` (its text) |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). **The corpus samples need HDD+DVD too**: a DVD-only
`xgu bump_cubemap_reflect` leg shows the kernel's "requires service" screen (the
sample never launches). Compare BTFF rows with BTFF rows and `raster-leg` rows
with `raster-leg` rows. A `-Gpu gl` row's RAM SHA reflects the backend's own
writes; its **frame** is the anchor.

**A mid-boot fast-mode RAM delta is NOT a regression by itself (§237 §4).** The
`--jit-spin` idle skip lumps device time, so a change in *block population* moves
where it fires: this session's change moves the triangle's 99.5M-step RAM SHA and
heals by 1B. Diff such a report with `--jit-spin` off and with a different
`--jit-threshold` on the base build before diagnosing anything.

**Display-source rows (1B, `-Gpu gl -GpuReplace`, §234).** `displaySha` equals
the software row for a direct-framebuffer guest and the backend frame for a
flipping title: `gamma` `D3EBF420…`, `hello` `72D33DC2…`, `usb` `119AFA48…`
(all `source=memory-scanout`), `led` `18C7CC83…` and `mesh` `A7442AB1…`
(`source=backend`). `gamma`/`usb` are the cheapest checks of that rule
(`pwsh -File scripts/boot-to-first-frame.ps1 -Sample gamma -Gpu gl -GpuReplace`).

Accurate-mode anchors (JIT off, HDD+DVD): triangle RAM `812EA159…` / CPU
`2F632483…`, menu RAM `6AB39AB0…` / CPU `3D2740B8…`; both frames `A1740DE0…` and
`4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle `7A860ED7…`
(policy diff, never byte-exact; both reproduce with `diff=2602` / `diff=27849`,
`violations=0`, `maxDelta=1`, `BackendUnsupported=0`).

- **1863 tests green**, build/format clean. The ktest XGrid leg is
  **792,800,535** steps, suite log 40,797 B, SHA `CA5D3867…` (re-run this
  session, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp`, `--tex-filter registers` and the OpenGL renderer are opt-in too**,
  and all three are settings in the Avalonia frontend (§217, §221, §223).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the last session landed - do not redo it

0. **§238: `JitSpecSlice` is 64 (was 256)**, in both the library and the CLI.
   A non-JIT run of 256 members crossed many hot heads the engine never got to
   offer a block for. Measured -3.7% triangle `gl+replace` / -2.2% menu (3/3) and
   -11.5% on the 100M boot phase, every 1B anchor and corpus row byte-identical.
   **The sweep is in the decisions note**: `nonJitMs` keeps falling to slice 4 but
   formation eats it, so 64 is the wall minimum - do not "optimize" it to 16.
1. **§237: image windows are cached and blocked.** `IImmutableImageDevice` +
   `AddressSpace.TryGetImmutableImageWindow` classify flash/BIOS/mirror/overlay
   fetches; `DecodeCache` holds them in a key set with no store path
   (`FlushImmutableImages`), a store into the window drops them conservatively,
   the MCPX overlay disable and `Reset` flush them, and `RunJitBlock` re-forms a
   block whose decode defaults no longer match (with the memo slot cleared).
   Measured -6.4% triangle `gl+replace` at the time (now folded into the numbers
   above). **`nonRamSkips` is 0 on a fast run now** - do not "restore" it, and do
   not re-add a physical-range test in place of the region classification (an
   MMIO store must never look like an image window).
2. **§236: gap 12 closed** - a cube stage samples a host `sampler2DArray`
   (`uCube0..3`, units 4-7) decoded from the guest's swizzled faces;
   `GuestWordPoolSize` (24) carries only a HILO dot input's raw pair (dot maps
   4-7). Do not re-add a word-pool cube path.
2. **§234: the display source under a GPU backend** (recently-presented ring;
   Nv2a snapshot v9) and **the C7 encoder price** (do not start a
   `FastAssembler`).
3. **C5 slice 1 (§233)**: lazy IL runner + in-frame chain entry. Do not
   "optimize" the chain edge further without reading §233d.
4. **C4 slice 1 and C4b are priced and REVERTED (§232a/§232f)**.
5. **`XboxCpu.PollInterrupt()`** (232c) and the raw store probe's lost translate
   slot (232d) are in.
6. Per-title renderer (§224); renderer policy seam + threaded snapshots (§223);
   CHD title id (§222); GL sampler decode / replace-mode composition / frame
   stepping (§220); GL TEXFILTER parity (§219); APU `NV_PAPU_XGSCNT` (§218);
   PTIMER (§216); fixed-function stage + lighting (§213/§215); the B3 seam state
   and the live compile gate (§226/§227).

## 1. Where the wall is now (measured, §233a's map with §237 applied)

**Triangle 1B `gl+replace` = 6.08 s** (was 6.67 before this session; a
`--jit-timing --jit-tally` run on the current build reads `wall=5.968
executeMs=5960`):

| stage | ms | share | what it is |
|---|---|---|---|
| `RunJitBlock` (`outerMs`) | 4297 | 72% | 783k invocations |
| - native raw runner (`runnerMs`) | 3235 | 54% | 547M members at **5.9 ns/member** |
| - formation + chain + bookkeeping | 1062 | 18% | 2,812 blocks; **~700 ms is Iced `Assemble`** |
| `RunNonJit` (`nonJitMs`) | 1650 | 28% | 182k calls, 2.62M instructions |
| - `RunSpecBlock` (~48k runs) | ~1470 | 25% | ~2.48M members at **~590 ns** (device-heavy runs) |
| - `Step` (134k calls) | ~180 | 3% | ~1.3 us each (was 523k calls) |

GL submission (~1000 ms, 405k user-FIFO stores at 2.4 µs) is *inside* the rows
above, not an extra row; the raw runner's share of it is what makes its 5.9 ns
per member. §238's slice change moved the non-JIT side from `nonJitMs` 2190 /
5.48M instructions to **1650 / 2.62M** and paid for it in formation
(`outerMs` 3981 -> 4297, blocks 2,603 -> 2,812).

- **The boot (first 100M steps) is 2.04 s of the 6.24 s wall** (was 2.28 s).
- **Menu `gl+replace`** 20.16 s: the same shape with 2.7x the non-JIT calls.
- **xemu-class is 1.20 s** on the triangle (830 Msteps/s) and 1.36 s on the menu
  (real-time band), so the gap is still **~5.2x / ~15x**.
- **What is left of the boot**: 55,215 heads that never reach the 16-visit build
  threshold (1,122 blocks form). Forming them costs ~326 us each (Iced), which is
  the encoder price (§234e, ~7%) - so the boot's remainder is *not* a cache
  problem.

## 2. Recommended next session - pick one

### Option A (Track C): `RunSpecBlock`'s 259 ns/member is now the largest non-JIT item

It is the only remaining item that is neither measured null nor priced out. The
shape to look for is a way to serve **8-30-member runs** without paying a
`DynamicMethod`/raw build per head, because those runs are exactly the 55k heads
below the threshold. Constraints already measured: the `--jit-threshold` sweep
(1/4/8/16/64 -> 9.27/7.86/7.32/6.99/6.79 s at 100M on the old build) is at its
optimum, the encoder is ~7% (§234e), trace formation removes block-boundary
population, not this one (§235), and shared cold stubs are -160 ms (§233e).
Price any candidate the §232e way (stashed interleaved A/B legs, never an
env-gated ablation that changes guest behavior).

### Option B (Track C, the honest re-check): is the 6.25 ns/member runner number still the floor?

With `nonJitMs` at ~2.1 s and the runner at 3.4 s, the pair is 88% of the wall. A
100M-step `--jit-timing` run plus `XGRID_RAW_PROFILE=1` (with
`XGRID_RAW_CHAIN=0`) reproduces the shape in one command each; if the mix has not
moved since §199n (~50% MovLoad/MovStore, 14% DecR, 14% Alu), the answer is the
engine shape (option iii of the old §2), not another slice.

### Option C (Track E): the remaining corpus gaps

- **A USB hot-plug row** (§229 filed): `nxdk/usb` prints only on connect; a
  disconnect event needs a hot-plug hook on `UsbHub`, then the same sample
  asserts the disconnect line too.
- **Gap 10, `bumpenv_ripple`'s app state** (§211): needs the gap-9 frame oracle.
- The cull winding on `bumpenv_ripple`'s quads, and the remaining DXT/paletted/
  mipmapped texture paths (unobserved by the corpus).

### Filed, lower value

- Mip selection (LOD computation); the corpus has no mipmapped sample yet.
- A GL *window* renderer in the frontend (the offscreen FBO + readback path is
  the product path). Mirror mode in the frontend.
- Host audio (APU GP/EP DSP execution + a PCM sink) - Track P's largest item.
- The HILO dot-map input one-word-per-stage limitation (§236) - no row uses it.

## 3. Measured negatives - do NOT re-try without a new measurement

- **Lowering `--jit-slice` to 16/4 because `nonJitMs` keeps falling** (§238): the
  extra formation eats it; 64 is the wall minimum (measured at 100M and 1B).
- **A `RunSpecBlock` handoff at built heads** (§238 §4, probed this session): only
  4.2-4.9% of the non-JIT members sit at a head that already has a block
  (50,550/1,190,276 at 100M, 127,173/2,618,986 at 1B) = ~1.2% of the wall. The
  remaining mass is the 55k *unbuilt* heads (a formation price).
- **`AddressSpace.TryGetRegion`-free range tests for the image class**: a store
  to `0xFD000000` (NV2A FIFO, 405k/1B) would flush the cache every time. Use the
  region/priority classification (§237).
- **Treating a mid-boot fast-mode RAM delta as a bug** (§237 §4): the spin skip
  is block-population sensitive; reproduce it with `--jit-spin` off first.
- **A word pool for cube faces** (§236): a 256x256 face is 393,216 texels.
- **Reading the raw texture-address word** - 1 is wrap, 2 is mirror, 4 is border.
- **Requiring the seam's carried window to contain a cube face** (§228).
- **Declining every `DOT_PRODUCT` stage** (§236): only dot maps 4-7 need a guest
  word; the colour maps read the already-sampled texel.
- **Blaming the MUX-lsb select / final-lerp negation for the black panel** (§236).
- **Owning the encoder** (§234e): 3.2x on `Assemble` is ~7% triangle / ~3% menu.
- **A renderer-side display latch** (§234d): the display controller decides.
- **Keeping the guest registers live across a chain edge** (§233d).
- **Shared cold stubs** (§233e): per-block ~-60 ms, global ~-160 ms.
- **Caching ROM decodes alone** (232f/§237): the fetch was ~250 ms; the win is
  the per-call dispatch, and it is 317k instructions, not 3.65M.
- **The C4 host-pointer translate probe** (§232a): neutral, SHAs exact.
- **Env-gated emitter ablations as a pricing tool** (§232e).
- **Sub-2% performance slices** - banned.
- **Reading a corpus sample at 3B** - `xgu bump_cubemap_reflect`'s dot-mode
  programs live at 4.4B (`corpus-phase-anchors.md`).
- **A DVD-only corpus leg** - it shows the kernel's error screen, not the sample.
- **Treating an 8-stage combiner record as one program** (§228).
- **Reading the convolution kernel field alone as "convolve"** (§210a).
- **Blaming `bumpenv_ripple`'s black frame on the texture format** (§211a-211e).
- **Reading a swizzled texture's extent from `TEXIMAGERECT`** (§209a).
- **DVD-only triangle legs** - they never launch the disc XBE (§208d).
- **Running snapshot save/load on the UI thread** (§223b).
- **Adding SSE2 opcodes to the CPU oracle** - the Xbox CPU is a Pentium III.
- **Trusting `glClear` to match a PGRAPH clear** (§219c).
- **`Get-Command clang` proving the nxdk toolchain is missing** (§229).
- **Passing a quoted comma list to `-Samples`** - use `@("mesh","gamma")`.
- **A `BufferCodeWriter`/`Decoder` name from the product in a test**: qualify
  `Iced.Intel.*` (§234e).
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (C/B/E/P/D) and
  the measured ceiling behind its acceptance.
- **Track C leads** but the cheap levers are gone (see §2). Incremental sub-2%
  codegen slices remain banned. Interpreter *correctness* work is still allowed.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run. A **display-source change** needs the 1B goldens *and* the
  GL-replace corpus legs (§234c). A **texture-sampling / computed-mode change**
  needs the 1B goldens plus the `xgu-bump_cubemap_reflect` 3.6B and 4.4B sw-vs-gl
  pixel pairs (§236). A **decode-cache / block-formation change** needs the 1B
  goldens, `--decode-verify` on a boot leg (`verifyFailures=0`), the ktest log
  and a pass over the fast 1B anchors (§237). A **flagged** behavior change must
  show the default path byte-identical and the flag-on delta measured. A CPU
  instruction change must keep the CPU oracle and `XGrid.Cpu.Tests` green.
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp`/renderer defaults
  without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency.
  `References2/` is read-only (and `IImmutableImageDevice` must never be put on a
  device a guest store can change). Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- A **snapshot version bump invalidates every `.xgs`** (now Nv2a v9, audio v3).
  §237 is host-side only: no snapshot change.
- Write it up: a decisions section (§238+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-22)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
trusting any wall-clock number. `References2/` is the single verified reference
store (`scripts/refs-manifest.ps1 -Verify`).

- **H1 (this machine):** Windows 11, NVIDIA GL 4.6.0 (610.88), ~85 GB free on C:
  (keep media copies on D: anyway). The nxdk toolchain is **installed** inside
  MSYS2 (`C:\msys64`) with nxdk at `D:\nxdk` (`a6c31ee`), so nxdk corpus rows
  build here: `pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")`.
- **Walls on H1 at 1B (fast mode, this session's window):** triangle
  10.3-10.5 s `sw` / 6.24-6.41 s `gl+replace`; menu 30.1 s `sw` / 20.2-20.5 s
  `gl+replace`. Accurate mode (no JIT): triangle 44.4 s `sw` / 44.0 s `gl`,
  menu 62.2 s `sw` / 65.6 s `gl`.
- **A/B recipe that works here:** `git stash` -> build -> copy
  `src/XGrid.Cli/bin/Release/net10.0` to `%TEMP%\xgrid-base-bin` ->
  `git stash pop` -> build -> run both binaries interleaved (3 pairs minimum).

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore
dotnet format XGrid.sln --verify-no-changes --no-restore

# gates (pass the HDD; the default path in the scripts may not exist on H1)
pwsh -File scripts/gpu-slice1-golden.ps1 -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/ktest-smoke.ps1 -SkipXemu

# performance legs (raster-leg prints one METRIC line)
pwsh -File scripts/raster-leg.ps1 -Workload tri
pwsh -File scripts/raster-leg.ps1 -Workload menu
pwsh -File scripts/raster-leg.ps1 -Workload tri -Gpu gl -GpuReplace -Extra @("--jit-timing")
# the §238 slice sweep (one constant, four runs; wall + nonJitMs + blocks):
#   --jit-slice 256/64/16/4 with --jit-timing --jit-tally

# decode-cache / image-window evidence (§237)
pwsh -File scripts/raster-leg.ps1 -Workload tri -Gpu gl -GpuReplace -Extra @("--decode-verify")
# expect verifyFailures=0 nonRamSkips=0 imageFills>0 ; imageEntries is 0 at the end
# because an ordinary full flush dropped them during the boot
# the strongest exactness leg: interpreter + decode cache + verify at 1B must equal
# the accurate anchor (RAM 812EA159 cpu 2F632483) - ~76 s

# corpus / per-sample frames (mind the phase anchor; HDD+DVD is mandatory)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample gamma -Gpu gl -GpuReplace   # §234 display source
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 4400000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3600000000 -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample usb -Steps 1000000000
# the §236 acceptance pair (compare the two -last.bmp pixel by pixel):
python -c "from PIL import Image; a=list(Image.open('wt-data/btff/xgu-bump_cubemap_reflect-sw-last.bmp').convert('RGB').getdata()); b=list(Image.open('wt-data/btff/xgu-bump_cubemap_reflect-gl-replace-last.bmp').convert('RGB').getdata()); print(sum(1 for x,y in zip(a,b) if x!=y))"

# nxdk rows
pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")

# CPU differential + the encoder spike (prints the C7 price)
python tools/cpu-oracle/oracle.py --list
dotnet test tests/XGrid.Cpu.Tests -c Release --nologo
dotnet test tests/XGrid.Cpu.Tests -c Release --nologo --no-build --filter "FullyQualifiedName~EncoderSpikeTests" --logger "console;verbosity=detailed"
dotnet test tests/XGrid.Cpu.Tests -c Release --nologo --no-build --filter "FullyQualifiedName~ImageWindowFetchTests"

# frontend
dotnet run --project src/XGrid.Frontend.Avalonia
dotnet run --project tools/frontend-smoke -c Release -- gl 20   # or: -- software 20
```

## 7. Diagnostics to keep

`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads=
flipStaleUploads= composedFrames= composedPixels= composedDraws= batchCalls=
batchTris= batchCap= guestWordDraws= guestWordMisses= guestWordDeclined=]`,
`unsupTex[...]`, `readback=on|off`, `methods=`,
**`imageFills=` / `imageFlushes=` / `imageEntries=` on the `Decode cache` line
(§237: distinct image-window decodes, wholesale drops, live count; a fast run
ends at `imageFlushes=0 imageEntries=0` because an ordinary full flush drops them
during the boot)**, **`slice=` on the JIT line (§238; the default is 64)**,
**`source=backend|memory-scanout`** on the `Frame`/`FrameDisplay`/
`FrameScanout` lines (`Nv2AMmio.DisplaySourceDebug`, §234),
**`--gpu-time` combiner rows carry `tm=` / `sf=` / `ts<n>=<TEXFMT>:d<dim>[:c][:x][:s]` /
`reach[tx=… stg=… src=… consts=… flags=… flags@=… lerp=…]`** (§228 + §230 + §236),
**`filters=<min>/<mag>/<conv>`**, **`fixedFn=` / `fixedFnLit=` /
`fixedFnLights=`**, `PresentedFrameDebug`, `Nv2AMmio.VgaCrtcDebug`,
`--tex-filter registers`, `--ffp`, `DiagnosticGenericSampler`/
`DiagnosticGenericFill`, `--raw-shapes`, `XGRID_RAW_PROFILE=1` +
`XGRID_RAW_CHAIN=0` (chain A/B switch), `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`XGRID_WATCH_START`/`XGRID_WATCH_END` + `XGRID_WATCH_TRACE=1` (a write trace;
under the raw engine the reported EIP is the frame's, not necessarily the exact
instruction - use `--ramdump` + `fc /b` to localize), `--jit-timing`,
`--jit-tally`, `--bench`, `--frame-first`, `frames=`/`pcrtc=` on the `Frame`
line, `--frame-display`, `--pgraph <path> --pgraph-max <n>`, `--eip-hist`,
`--eeprom-out`, `XGRID_TRACE_PRESENT=1` (a *temporary* flip/CRTC trace; re-add
it the same way if a display-source question needs it), and the frontend's
`Renderer:` power-on line.
