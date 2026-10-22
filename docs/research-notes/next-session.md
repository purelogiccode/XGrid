# Next session instructions (updated 2026-10-22 late, after §236: gap 12 closed - a cube stage is a host 2D-array texture. Track B has no open item; the next substantive work is Track C's engine-shape decision)

**Read in this order:** `docs/development-direction.md` §0 (the 2026-10-22
course correction: xemu-class performance is the long-run objective, Track C
re-opened, C4-C6, now with the §236 status line), §1-§3 (priority stack,
measured position, milestone ladder),
`docs/research-notes/jit-track-charter.md` (the Phase 3b status block incl. the
C7 decision inputs),
**`docs/research-notes/xgrid-architecture-decisions.md` §236 (the cube 2D-array
decode and the four defects behind gap 12)**, §235 (trace formation priced out),
§234 (the display source under a GPU backend + the encoder price), §233 (the
per-stage price of the whole wall - this is the map), §232 (C4 null, the non-JIT
decomposition) + §231 (the course correction) + §230/§228/§227/§226 (the B3
history) + §225 (CPU correctness campaign) + §224 + §223 + §222 + §221 + §220 +
§219 + §218 + §217 + §216 + §215 + §213 + §212/§210 (TEXFILTER) + §211 (gap 10
diagnosed) + §209 (cubemap) + §208 (display model) + §207 (corpus),
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
git log --oneline -1        # expect: the §236 commit ("nv2a/gpugl: ... cube array texture ...")
git status --short          # must be clean (a harness file like .repofleet-task.md is not ours)
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1857 green
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
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 29.8-31.7 s measured |
| menu + `--tex-filter registers` | `EE8F69FB…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `CC2A94DF…` | `F5D61C94…` | ~59 s |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` | `B45254CA…` | `7DAB63EB…` | 10.4-12.1 s (frame `A1740DE0`, 68/34, 57,663,082 px) |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.9 s (frame identical too) |
| triangle `--gpu gl --gpu-replace` 1B | `4F1FCBC0…` | `7DAB63EB…` | 6.5-7.1 s, frame `7A860ED7…`, `source=backend` |
| menu `--gpu gl --gpu-replace` 1B | `B06BA6E5…` | `F5D61C94…` | 20.3 s, frame `B6116DAE…` |
| `xgu-spheremap_ffp` 3B BTFF | off `B5747761…` / on `8062CFB5…` | `F0075C2F…` (both) | ~9 / ~14 s |
| `xgu-distance_fog_ffp` 3B BTFF | off `AFF08085…` / on `12BAD517…` | `571363B4…` (both) | ~9 / ~30 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |
| `xgu-bump_cubemap_reflect` 3.6B BTFF | `5ACD06FF…` | `C848610E…` | 12.3 s, frame `BFC41A59…` |
| `xgu-bump_cubemap_reflect` 3.6B **gl+replace** (§236) | `52CE70A3…` | `C848610E…` | 10.1 s, frame `0AE9A617…`, **2 px from software** |
| `xgu-bump_cubemap_reflect` 4.4B BTFF | `D90376B9…` | `EBAC6B99…` | 14.3 s, frame `5A949A40…` |
| `xgu-bump_cubemap_reflect` 4.4B **gl+replace** (§236) | `17ABCA37…` | `EBAC6B99…` | 10.4 s, frame `CF65CDC2…`, **2 px from software** |
| `xgu-vp_lighting` 3B BTFF | `162DAFF1…` | `0380C502…` | ~9.5 s, frame `72B2F490…` |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |
| `nxdk/usb` 1B BTFF (§229) | `ACDB5720…` | `1590C697…` | 8.2 s, display `119AFA48…` (its text) |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). **The corpus samples need HDD+DVD too**: a DVD-only
`xgu bump_cubemap_reflect` leg shows the kernel's "requires service" screen (the
sample never launches), which cost an hour this session. Compare BTFF rows with
BTFF rows and `raster-leg` rows with `raster-leg` rows. A `-Gpu gl` row's RAM SHA
reflects the backend's own writes; its **frame** is the anchor.

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

- **1857 tests green**, build/format clean. The ktest XGrid leg is
  **792,800,535** steps, suite log 40,797 B, SHA `CA5D3867…` (re-run this
  session, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp`, `--tex-filter registers` and the OpenGL renderer are opt-in too**,
  and all three are settings in the Avalonia frontend (§217, §221, §223).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the last session landed - do not redo it

0. **§236: gap 12 closed.** A cube stage samples a host `sampler2DArray`
   (`uCube0..3`, units 4-7) that the backend decodes from the guest's six
   swizzled faces with `Nv2AMmio.SwizzleAddressNT` + `DecodeSwizzledTexelWord`;
   `PshGlslGenerator.GuestWordPoolSize` (24) is the one pool constant and the
   pool only carries a HILO dot input's raw pair (dot maps 4-7 only). Deleted:
   `AppendCubeWords`, `CubeTexelAddress`, `DescribeTextureDecode`,
   `DecodedChannel`, the GLSL descriptor decode, the swizzle bit-spread helpers
   and nine dead uniforms. `GuestTexelsFor` no longer hands out a wrapping ring.
   **Do not re-add a word-pool cube path**: a 256x256 face is 393,216 texels.
1. **§234: the display source under a GPU backend** (recently-presented ring;
   Nv2a snapshot v9) and **the C7 encoder price** (do not start a `FastAssembler`).
2. **C5 slice 1 (§233)**: lazy IL runner + in-frame chain entry (-4.2% / -3.0%,
   3/3). Do not "optimize" the chain edge further without reading §233d.
3. **C4 slice 1 and C4b are priced and REVERTED (§232a/§232f)**; the ROM-decode
   lever is closed at a ~250 ms ceiling.
4. **`XboxCpu.PollInterrupt()`** (232c) and the raw store probe's lost translate
   slot (232d) are in.
5. **`nxdk/usb` is a corpus row and H1 builds nxdk rows (§229).**
6. Per-title renderer (§224); renderer policy seam + threaded snapshots (§223);
   CHD title id (§222); GL sampler decode / replace-mode composition / frame
   stepping (§220); GL TEXFILTER parity (§219); APU `NV_PAPU_XGSCNT` (§218);
   PTIMER (§216); fixed-function stage + lighting (§213/§215); the B3 seam state
   and the live compile gate (§226/§227).

## 1. Where the wall is now (measured per stage, 233a - the map)

**Triangle 1B `gl+replace` = 6.66 s, decomposed by a call-level probe:**

| stage | ms | share | what it is |
|---|---|---|---|
| native raw runner | 3393 | 49% | 543M members at **6.25 ns/member** |
| formation | 1026 | 15% | 2,597 blocks; **682 ms is Iced `Assemble`** |
| `Step` path (boot) | 1012 | 15% | 489k steps, *every one a decode miss* |
| GL submission | ~1000 | 14% | 405k stores to `0xFD800000`, 2.4 µs each |
| interpreter (non-GPU) | ~400 | 6% | 5.3M members at 259 ns |
| cold JIT entries | 68 | 1% | 523k entries at **132 ns** |

- **The one-instruction round trip is 68 ms, not the ~0.75 s 232g credited.**
- **Formation's encoder leg is priced out** (§234e): a 3.2x faster encoder is
  ~7% of the wall. What is left of formation (344 ms) is the emit loop and allocs.
- **The interpreter's 2.4 µs `mov [mem],reg` outlier is the GPU**: those stores
  are NV2A FIFO writes that run the GL backend's submission (§205 `drawMs`).
- **The boot (first 100M steps) is 2.28 s of the 6.9 s wall.**
- **Menu `gl+replace`** ~20.4 s: the same shape with 2.7x the non-JIT calls.
- **xemu-class is 1.20 s** on the triangle (830 Msteps/s) and 1.36 s on the menu
  (real-time band), so the gap is still **~5.5x / ~15x**.

## 2. Recommended next session - pick one

### Option A (Track C, C7): the engine-shape decision is the work item

Every cheaper lever now has a price and is out: C4 (null), C4b (~250 ms ceiling),
shared stubs (-160 ms), slimming members (instruction count is not the wall's
currency), the encoder (~7%, §234e), trace formation (~3-5%, §235). The charter's
kill criterion has fired twice, so the next C work is a **decision with a
measurement attached**:

- **(i) Trace formation - priced out on a number (§235).** Do not spend a session
  on a trace emitter without a new measurement that contradicts §233d/§233e.
- **(ii) The interpreter/`Step` side of the boot** (2.0 s of the 6.9 s wall,
  1.15 s of it in the first 100M steps). The cache line that refuses non-RAM
  pages is priced at ~250 ms; the rest is `RunSpecBlock` (1.4 s over 34k calls,
  259 ns/member) and the one-instruction decode. **A superblock for the boot
  (form blocks in ROM pages) is the only untried shape here.** Its ceiling,
  measured on the current build at 100M steps with `--jit-timing`:
  `outerMs=1221`, `runnerMs=701`, **`nonJitMs=995` over `nonJitCalls=449,772`
  serving `nonJitInstructions=3,654,056`** (8.1 per call, i.e. 272 ns per
  instruction served). If blocks served those 3.65M instructions at the raw
  path's ~10 ns, the boot would lose ~0.85 s: **~8% of the 10.6 s triangle `sw`
  wall, ~13% of the 6.6 s `gl+replace` wall** - over the C bar, unlike every
  other remaining lever. Design constraints found while scoping it: the decode
  cache refuses `physical >= 0x20000000` (`MaxTrackedPhysical`) *and* its
  store-invalidation bitset only indexes base RAM, so a ROM entry needs its own
  "no store invalidation" class plus a full flush on any guest write into the
  ROM window (the flash part is writable in principle) - and the boot fetches
  are `0xFFFFFFxx` (the MCPX ROM region). Verify the write path before assuming
  ROM is immutable.
- **(iii) A different execution model** (threaded code / a native core), the only
  option whose argument is not "fewer instructions". Price it the §233a way
  before a rewrite: a per-member probe of one candidate transformation with the
  exactness gates attached.

Whichever is chosen: same-window A/B, exact 1B SHAs/counters, flag-off
byte-identical, ktest log byte-identical, and `docs/development-direction.md`
§0's stage bar (>=10% wall or >=20% engine) applies.

### Option B (Track B) - **closed; do not reopen without a failing row**

Gap 12 is closed (§236) and the present path is closed (§204/§205/§219/§220).
The one filed remainder: a HILO dot-map input (maps 4-7) is still **one word per
stage** (`PoolWord(inSlot, 0)`), i.e. a per-stage constant. No measured row uses
a HILO map; if one appears, the fix is §236's shape (decode the input stage's raw
words into a texture and sample it per fragment). The MUX-lsb/final-lerp "leads"
in §230 were **not** the cause of anything measured - do not change them without
a failing differential.

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

## 3. Measured negatives - do NOT re-try without a new measurement

- **A word pool for cube faces** (§236): a 256x256 face is 393,216 texels; the
  window sampled the wrong texel and read a zero word when the draw carried none.
- **Reading the raw texture-address word** - 1 is wrap, 2 is mirror, 4 is border.
- **Requiring the seam's carried window to contain a cube face** (§228).
- **Declining every `DOT_PRODUCT` stage** (§236): only dot maps 4-7 need a guest
  word; the colour maps read the already-sampled texel.
- **Blaming the MUX-lsb select / final-lerp negation for the black panel** (§236).
- **Owning the encoder** (§234e): 3.2x on `Assemble` is ~7% triangle / ~3% menu.
- **A renderer-side display latch** (§234d): the display controller decides.
- **Keeping the guest registers live across a chain edge** (§233d).
- **Shared cold stubs** (§233e): per-block ~-60 ms, global ~-160 ms.
- **Caching ROM decodes** (§233a/232f): ceiling ~250 ms, measured null once.
  (Note: *forming blocks* over ROM pages is a different lever - it removes the
  per-call dispatch, not the fetch - and its measured ceiling is ~0.85 s of the
  triangle wall; see §2 Option A (ii).)
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
- **Track C leads** but is at a decision point (§2 Option A). Incremental
  sub-2% codegen slices remain banned. Interpreter *correctness* work is still
  allowed.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run. A **display-source change** needs the 1B goldens *and* the
  GL-replace corpus legs (`gamma`/`hello`/`usb` = software, `led`/`mesh` =
  backend, §234c). A **texture-sampling / computed-mode change** needs the 1B
  goldens plus the `xgu-bump_cubemap_reflect` 3.6B and 4.4B sw-vs-gl pixel
  pairs (§236). A **flagged** behavior change must show the default path
  byte-identical and the flag-on delta measured. A CPU instruction change must
  keep the CPU oracle and `XGrid.Cpu.Tests` green. **A raw-emitter change must
  reproduce all four fast 1B anchors and the ktest log.**
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp`/renderer defaults
  without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency.
  `References2/` is read-only. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- A **snapshot version bump invalidates every `.xgs`** (now Nv2a v9, audio v3).
- Write it up: a decisions section (§237+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-22)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
trusting any wall-clock number. `References2/` is the single verified reference
store (`scripts/refs-manifest.ps1 -Verify`).

- **H1 (this machine):** Windows 11, NVIDIA GL 4.6.0 (610.88), ~86 GB free on C:
  (keep media copies on D: anyway). The nxdk toolchain is **installed** inside
  MSYS2 (`C:\msys64`) with nxdk at `D:\nxdk` (`a6c31ee`), so nxdk corpus rows
  build here: `pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")`.
- **Walls on H1 at 1B (fast mode, this session's window):** triangle
  10.6-11.1 s sw / 6.5-7.1 s `gl+replace`; menu 29.8-31.7 s sw / 20.3 s
  `gl+replace` (the high ends were contended parallel runs - no perf claim).
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

# frontend
dotnet run --project src/XGrid.Frontend.Avalonia
dotnet run --project tools/frontend-smoke -c Release -- gl 20   # or: -- software 20
```

## 7. Diagnostics to keep

`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads=
flipStaleUploads= composedFrames= composedPixels= composedDraws= batchCalls=
batchTris= batchCap= guestWordDraws= guestWordMisses= guestWordDeclined=]`,
`unsupTex[...]`, `readback=on|off`, `methods=`,
**`source=backend|memory-scanout`** on the `Frame`/`FrameDisplay`/
`FrameScanout` lines (`Nv2AMmio.DisplaySourceDebug`, §234),
**`--gpu-time` combiner rows carry `tm=` / `sf=` / `ts<n>=<TEXFMT>:d<dim>[:c][:x][:s]` /
`reach[tx=… stg=… src=… consts=… flags=… flags@=… lerp=…]`** (§228 + §230 + §236 -
`ts<n>=` is the per-slot texture state, which is what identified the row's
cube-flagged `0x3A` slot),
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
