# Next session instructions (updated 2026-10-22 late, after C5 slice 1 landed: read §1 before starting any Track C work)

**Read in this order:** `docs/development-direction.md` §0 (the 2026-10-22
course correction: xemu-class performance is the long-run objective, Track C
re-opened, C4-C6), §1-§3 (priority stack, measured position, milestone ladder),
`docs/research-notes/jit-track-charter.md` (the C4-C6 status block),
**`docs/research-notes/xgrid-architecture-decisions.md` §233 (the per-stage
price of the whole wall - this is the map; every "next lever" claim in older
notes is superseded by it)** + §232 (C4 null, the non-JIT decomposition) + §231
(the course correction) + §229/§228/§227/§226 (the B3 corpus row and its
computed-mode work) + §225 (CPU correctness campaign) + §224 + §223 + §222 +
§221 + §220 + §219 + §218 + §217 + §216 + §215 + §213 + §212/§210 (TEXFILTER) +
§211 (gap 10 diagnosed) + §209 (cubemap) + §208 (display model) + §207 (corpus),
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
git log --oneline -1        # expect: the C5 slice-1 commit ("cpu: C5 slice 1 ...")
git status --short          # must be clean (a harness file like .repofleet-task.md is not ours)
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1836 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window. **Wall noise on H1 is
+-3-5%**: a claim needs >=3 interleaved pairs and 3/3 sign agreement (233).

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD;
`host-doctor.ps1` names them - `wt-data\chain-199l\hdd.qcow2` is the HDD used by
the gate scripts on H1):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 29.8 s measured this session |
| menu + `--tex-filter registers` | `EE8F69FB…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `CC2A94DF…` | `F5D61C94…` | ~59 s |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` | `B45254CA…` | `7DAB63EB…` | 10.4 s measured (frame `A1740DE0`, 68/34, 57,663,082 px) |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.9 s (frame identical too) |
| triangle `--gpu gl --gpu-replace` 1B | `4F1FCBC0…` | `7DAB63EB…` | 6.66 s, frame `7A860ED7…` |
| menu `--gpu gl --gpu-replace` 1B | `B06BA6E5…` | `F5D61C94…` | 20.36 s, frame `B6116DAE…` |
| `xgu-spheremap_ffp` 3B BTFF | off `B5747761…` / on `8062CFB5…` | `F0075C2F…` (both) | ~9 / ~14 s |
| `xgu-distance_fog_ffp` 3B BTFF | off `AFF08085…` / on `12BAD517…` | `571363B4…` (both) | ~9 / ~30 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |
| `xgu-bump_cubemap_reflect` **4.4B** gl+replace (§228) | - | - | frame `59F1AB97…` = **4 colours** (software `36A4C2A8…`, 18,980) - open |
| `xgu-bump_cubemap_reflect` **3.6B** gl+replace (§228) | - | - | **2 colours** vs software's 20,843 - same defect, pre-dot |
| `xgu-vp_lighting` 3B BTFF | `162DAFF1…` | `0380C502…` | ~9.5 s |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |
| `nxdk/usb` 1B BTFF (§229) | `ACDB5720…` | `1590C697…` | 8.2 s, display `119AFA48…` (its text) |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). Compare BTFF rows with BTFF rows and `raster-leg` rows with
`raster-leg` rows. A `-Gpu gl` row's RAM SHA reflects the backend's own writes;
its **frame** is the anchor.

Accurate-mode anchors (JIT off, HDD+DVD): triangle RAM `812EA159…` / CPU
`2F632483…`, menu RAM `6AB39AB0…` / CPU `3D2740B8…`; both frames `A1740DE0…` and
`4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle `7A860ED7…`
(policy diff, never byte-exact; both reproduce with `diff=2602` / `diff=27849`,
`violations=0`, `maxDelta=1`, `BackendUnsupported=0` - re-run this session).

- **1836 tests green**, build/format clean. The ktest XGrid leg is
  **792,800,535** steps, suite log 40,797 B, SHA `CA5D3867…` (re-run this
  session, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp`, `--tex-filter registers` and the OpenGL renderer are opt-in too**,
  and all three are settings in the Avalonia frontend (§217, §221, §223).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the last session landed - do not redo it

0. **C5 slice 1 (§233, 2026-10-22 late).** Two exact host-side changes:
   (a) the IL runner is built on demand instead of at formation
   (`JitBlock.Plans` + a lazy build in the invoke path; `ilBuildMs` 72 -> 1,
   `formMs` 1026 -> 846); (b) an **in-frame chain entry** per raw block
   (`_chainIn`, resolved with `BlockEncoderOptions.ReturnNewInstructionOffsets`
   + `GetLabelRIP`, published in the chain table instead of the function
   pointer): a chain jump keeps the source's frame instead of tearing it down
   and re-running the target's prologue. `outerMs` 4477 -> 4353; interleaved 1B
   A/B **triangle `gl+replace` 6.951 -> 6.660 s (-4.2%, 3/3)**, **menu
   `gl+replace` 20.991 -> 20.356 s (-3.0%, 3/3)**, all four fast anchors and
   both accurate goldens byte-identical. **Do not "optimize" the chain edge
   further without reading §233d:** keeping the guest registers live across the
   edge (skipping the state publish + reload) *diverges* at 30.5M steps.
1. **C4 slice 1 is priced and REVERTED (decisions 232a).** The host-pointer
   translate probe was implemented and measured neutral. Do not re-attempt.
2. **C4b (read-only ROM decode caching) is priced and REVERTED (232f).** The
   new price (233a) says the whole `Step` fetch+decode is 345 ms at 1B and only
   ~250 ms of it is ROM - **the lever is closed on a number now, not a hunch.**
3. **`XboxCpu.PollInterrupt()`** (232c) and the raw store probe's lost translate
   slot (232d) are in.
4. **`nxdk/usb` is a corpus row and H1 builds nxdk rows (§229).**
5. **Library rows carry the certificate title id (§228)**; per-title renderer
   (§224); renderer policy seam + threaded snapshots (§223); CHD title id
   (§222); GL sampler decode / replace-mode composition / frame stepping
   (§220); GL TEXFILTER parity (§219); APU `NV_PAPU_XGSCNT` (§218); PTIMER
   (§216); fixed-function stage + lighting (§213/§215); the B3 seam state and
   the live compile gate (§226/§227).

## 1. Where the wall is now (measured per stage, 233a - the map)

**Triangle 1B `gl+replace` = 6.66 s, decomposed by a call-level probe
(`XGRID_JIT_PROBE` was temporary; the numbers stand):**

| stage | ms | share | what it is |
|---|---|---|---|
| native raw runner | 3393 | 49% | 543M members at **6.25 ns/member** |
| formation | 1026 | 15% | 2,597 blocks; **682 ms is Iced `Assemble`** |
| `Step` path (boot) | 1012 | 15% | 489k steps, *every one a decode miss* |
| GL submission | ~1000 | 14% | 405k stores to `0xFD800000`, 2.4 µs each |
| interpreter (non-GPU) | ~400 | 6% | 5.3M members at 259 ns |
| cold JIT entries | 68 | 1% | 523k entries at **132 ns** |

- **The one-instruction round trip is 68 ms, not the ~0.75 s 232g credited.**
  Caching "no block formable here" is no longer a stage.
- **Formation is the biggest addressable item and it is the encoder.** Emitted
  code = 2,585 blocks / 19,175 members / **1,904,074 Iced instructions**
  (99 per guest member, 397 bytes), and `Assembler.Assemble` costs **377 ns per
  instruction** (Iced's `BlockEncoder` multi-pass). Blocks average 7.4 members,
  so the fixed prologue/epilogue is ~109 instructions/block and the marginal
  cost ~104/member.
- **The interpreter's 2.4 µs `mov [mem],reg` outlier is the GPU**: those stores
  are NV2A FIFO writes that run the GL backend's submission (§205 `drawMs`)
  inside the guest's method write. It is Track B, not interpreter overhead.
- **The boot (first 100M steps) is 2.28 s of the 6.9 s wall**: formation 515
  (1,116 of the 2,585 blocks), `Step` 663, spec 306, runner 699.
- **Menu `gl+replace`** ~20.4 s: the same shape with 2.7x the non-JIT calls.
- **xemu-class is 1.20 s** on the triangle (830 Msteps/s) and 1.36 s on the menu
  (real-time band), so the gap is still **~5.5x / ~15x**.

## 2. Recommended next session - pick one

### Option A (Track C, C7 reassessment): the charter's own stop condition is met

C4 measured null (232a/232f) and C5 slice 1 is **-3 to -4% wall** (§233). The
charter's kill criterion ("if C4+C5 miss 3x engine after two structural
iterations, stop and reassess, including the native-core option") is therefore
**triggered**; the next C work should be a *decision*, not another slice. The
priced facts a decision needs are all in §233:

- the runner's 6.25 ns/member is per-member work, not transitions (refining the
  chain edge from ~82 to ~40 instructions bought 1 ns/jump);
- formation is 377 ns/Iced-instruction, and the emitted code is 99
  instructions per guest instruction;
- the remaining interpreter cost is the guest's own device traffic.

So the three real options are: **(i) own the encoder** (a `FastAssembler` with
fixed-size rel32 branches and one pass - the emitter uses ~40 `Assembler`
methods, so it is a contained but real rewrite; price it by measuring
`assembleMs` on a spike), **(ii) slim the per-member code** (the fast path is
~15 of 99 instructions; the rest is probe/guard/slow paths - the measured
shared-stub saving is only -60 ms per block, -160 ms global, §233e), or
**(iii) a different engine shape** (a native core / threaded code, which is the
"native-core option" the charter names). Do not start (i) without a spike that
shows `Assemble` is the 682 ms it is measured to be.

### Option B (bounded B3 finish, §228): the GL path computes a black panel on `bump_cubemap_reflect`

Unchanged from last session and still the only bounded B item: the row's dot
programs run at **4.4B** and the GL frame is the clear plus a black panel; the
**3.6B** leg shows the same with no dot program (`tm=11111C`), so the defect
predates the computed modes. Leads: the 3.6B program is one stage whose slot-0
texture and final lerp (`f0=00000004`, `f1=00001400`) decide the panel; the 4.4B
dot records put the reflected colour in **alpha** through BLUE_TO_ALPHA into R0,
which only the **MUX-lsb** select consumes, and their final lerp
(`f0=0C0B000F`, `f1=0C011180`, `sf=FF3F3F3F`) is not implemented at all.
**Cheapest first step:** a per-fragment colour/depth differential on the 3.6B
panel program (temporary probe, stripped before any A/B). Acceptance: the 4.4B
GL replace frame gains the panel's colours with both 1B GL goldens unchanged.

### Option C (Track P/B, filed limit): the GL renderer cannot show a direct-framebuffer guest

`gamma`/`hello`/`usb` write the framebuffer and never flip, so GL mode shows
nothing while software shows their scanout. `TryCopyScanoutFrameFromMemory`
exists (§221c) but is only used *before* the first backend frame. Fix shape:
when PCRTC start/pitch describe a buffer the backend did not render, fall back to
the software scanout for that frame. Acceptance: `boot-to-first-frame.ps1
-Sample gamma -Gpu gl -GpuReplace` shows the software row's colours; both 1B GL
goldens stay byte-identical.

### Option D (Track E): the remaining corpus gaps

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

## 3. Measured negatives - do NOT re-try without a new measurement

- **Keeping the guest registers live across a chain edge** (§233d): the
  in-frame chain must keep the state publish + register reload; removing them
  diverges at 30.5M steps (RAM `EED915B5` vs `4F1FCBC0`, CPU exact).
- **Shared cold stubs** (§233e): per-block ~-60 ms, global ~-160 ms. Priced out.
- **A cheaper formation path that is not the encoder**: nothing else is left -
  `Assemble` is 682 of the 916 ms of raw emission.
- **Caching ROM decodes to fix the boot `Step` path** (§233a/232f): ceiling
  ~250 ms (3.6%), measured null once already.
- **The C4 host-pointer translate probe** (§232a): neutral, SHAs exact.
- **Env-gated emitter ablations as a pricing tool** (§232e): they change guest
  behavior. Price with a stashed build and interleaved legs.
- **Explaining the non-JIT time by mnemonic** (§232g): it is ordinary
  Mov/Add/Inc/Jcc; the cost is per-call dispatch, and that dispatch is 68 ms.
- **Sub-2% performance slices** - banned. A C stage needs a same-window >=10%
  wall or >=20% engine result (or, for C7, an explicit decision).
- **Reading a corpus sample at 3B** - `xgu bump_cubemap_reflect`'s dot-mode
  programs live at 4.4B (`corpus-phase-anchors.md`).
- **Requiring the seam's carried window to contain a cube face** (§228).
- **Treating an 8-stage combiner record as one program** (§228).
- **Reading the convolution kernel field alone as "convolve"** (§210a).
- **Expecting a tent to change a gate title's frame** (§210d).
- **Blaming `bumpenv_ripple`'s black frame on the texture format or the bump
  direction chain** (§211a-211e).
- **Reading a swizzled texture's extent from `TEXIMAGERECT`** (§209a).
- **Reading the raw texture-address word** - 1 is wrap, 2 is mirror, 4 is border.
- **DVD-only triangle legs** - they never launch the disc XBE (§208d).
- **Running snapshot save/load on the UI thread** (§223b).
- **Adding SSE2 opcodes to the CPU oracle** - the Xbox CPU is a Pentium III.
- **Trusting `glClear` to match a PGRAPH clear** (§219c).
- **`Get-Command clang` proving the nxdk toolchain is missing** (§229).
- **Passing a quoted comma list to `-Samples`** - use `@("mesh","gamma")`.
- A texture-buffer object for the per-triangle block; state-fingerprint batch
  boundaries; per-draw probes (sample 1-in-64 or ablate).
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (C/B/E/P/D) and
  the measured ceiling behind its acceptance.
- **Track C leads** but is at a decision point (§2 Option A). Incremental
  sub-2% codegen slices remain banned. Interpreter *correctness* work is still
  allowed.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run. A **flagged** behavior change must show the default path
  byte-identical and the flag-on delta measured. A CPU instruction change must
  keep the CPU oracle and `XGrid.Cpu.Tests` green. **A raw-emitter change must
  reproduce all four fast 1B anchors and the ktest log** - and, if it moves
  anything, bisect with `XGRID_BLOCK_TRACE`/`--ramdump` + a stashed baseline
  build (that is how 233d was caught in 40 minutes).
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp`/renderer defaults
  without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency.
  `References2/` is read-only. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- A **snapshot version bump invalidates every `.xgs`** (now Nv2a v8, audio v3).
- Write it up: a decisions section (§233+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-22)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
trusting any wall-clock number. `References2/` is the single verified reference
store (`scripts/refs-manifest.ps1 -Verify`).

- **H1 (this machine):** Windows 11, NVIDIA GL 4.6.0 (610.88), ~87 GB free on C:
  (keep media copies on D: anyway). The nxdk toolchain is **installed** inside
  MSYS2 (`C:\msys64`) with nxdk at `D:\nxdk` (`a6c31ee`), so nxdk corpus rows
  build here: `pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")`.
- **Walls on H1 at 1B (fast mode, this session's window):** triangle
  10.4 s sw / 6.66 s `gl+replace`; menu 29.8 s sw / 20.36 s `gl+replace`.
  Treat these as ratios within one window, never as absolutes across hosts.
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

# corpus / per-sample frames (mind the phase anchor)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 4400000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3600000000 -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample usb -Steps 1000000000

# nxdk rows
pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")

# CPU differential
python tools/cpu-oracle/oracle.py --list
dotnet test tests/XGrid.Cpu.Tests -c Release --nologo

# frontend
dotnet run --project src/XGrid.Frontend.Avalonia
dotnet run --project tools/frontend-smoke -- --renderer opengl
```

## 7. Diagnostics to keep

`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads=
flipStaleUploads= composedFrames= composedPixels= composedDraws= batchCalls=
batchTris= batchCap= guestWordDraws= guestWordMisses= guestWordDeclined=]`,
`unsupTex[...]`, `readback=on|off`, `methods=`,
**`--gpu-time` combiner rows carry `tm=` / `sf=` / `reach[tx=… stg=… src=…
consts=… flags=… flags@=… lerp=…]`** (§228 + §230),
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
`--eeprom-out`, and the frontend's `Renderer:` power-on line.
