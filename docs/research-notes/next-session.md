# Next session instructions (updated 2026-10-22 after the twenty-ninth session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §229 (the `nxdk/usb`
corpus row + the corrected H1 toolchain verdict) + §228 (the B3 row's 4.4B phase
anchor, the `reach[...]` read-set column, the cube word lane) + §227 (the
computed-mode shaders never compiled; the live compile gate) + §226 (B3 slice 1)
+ §225 (CPU correctness campaign) + §224 (per-title renderer, one GPU-memory
adapter, `KeyedOption`) + §223 (renderer policy seam, snapshot requests on the
emulation thread, frontend smoke harness) + §222 (CHD title id) + §221 (the GPU
renderer as a frontend setting) + §220 (GL sampler decode, replace-mode
composition, frame stepping) + §219 (GL TEXFILTER parity, clear write masks,
per-title overrides V2) + §218 (APU `NV_PAPU_XGSCNT`) + §217 (per-title
overrides + EEPROM state) + §216 (PTIMER 0.8.137) + §215 (fixed-function
lighting) + §213 (the fixed-function vertex stage, `--ffp`) + §212/§210
(TEXFILTER) + §211 (gap 10 diagnosed) + §209 (cubemap) + §208 (display model) +
§207 (corpus), `docs/compatibility.md` (matrix + harness + the gaps the corpus
found), `docs/research-notes/corpus-phase-anchors.md` (a row's step count is
part of its identity), `docs/research-notes/nxdk-usb-sample.md` (the newest
corpus row), `docs/research-notes/computed-mode-coverage.md` (the B3 phase
table), `docs/research-notes/usage-histogram.md` (measured NV097 / TEXFILTER /
sampler address-mode usage), `docs/research-notes/nv097-coverage.md`
(method-presence gap), `docs/research-notes/gpu-reference-map.md`,
`docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, `tools/cpu-oracle/README.md`,
then this file. (If a name above does not resolve, the file was renamed - search
`docs/research-notes/` rather than skipping it.)

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest + MSYS2 toolchain
git log --oneline -1        # expect: the tv229 commit ("nxdk/usb corpus row ...")
git status --short          # must be clean (a harness file like .repofleet-task.md is not ours)
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1836 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window.

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD;
`host-doctor.ps1` names them - `wt-data\chain-199l\hdd.qcow2` is the HDD used by
the gate scripts on H1):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 31-54 s (load); 32.5 s measured |
| menu + `--tex-filter registers` | `EE8F69FB…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `CC2A94DF…` | `F5D61C94…` | ~59 s (the FFP layer draws 121M more px) |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` (HDD + DVD) | `B45254CA…` | `7DAB63EB…` | 11.5 s measured (frame `A1740DE0`, 68/34) |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.9 s (frame identical too) |
| triangle `--gpu gl --gpu-replace` 1B | `4F1FCBC0…` | `7DAB63EB…` | 7.9 s, frame `7A860ED7…` |
| menu `--gpu gl --gpu-replace` 1B | `B06BA6E5…` | `F5D61C94…` | 24.3 s, frame `B6116DAE…` |
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
`violations=0`, `maxDelta=1`, `BackendUnsupported=0` - re-run in session 29).

**The menu RAM SHAs moved in §218:** the APU `NV_PAPU_XGSCNT` register now returns
`ep_frame_div * 32`, the dashboard reads it and stores it (§218). `51B56687 ->
34973510` (fast), `19FEA0EA -> EE8F69FB` (`--tex-filter registers`),
`87BF40DA -> CC2A94DF` (`--ffp`), `01BE5D64 -> 6AB39AB0` (accurate).

- **1836 tests green** (session 29 added 9: `PshCombinerReachTests` x3, the
  two-case `DescribeReach_ReportsTheWritingStages`, the live
  `CubeStage_ReadsFaceTexelsFromGuestRam_WithoutCarriedWords`, two more programs
  in the live compile gate, and `GameEntryTests` x3 for the library title id),
  build/format clean. The ktest XGrid leg was **not** re-run in session 29 (no
  guest-visible change; it stands at **792,800,535** steps, suite log 40,797 B,
  SHA `CA5D3867…`).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp`, `--tex-filter registers` and the OpenGL renderer are opt-in too**,
  and all three are settings in the Avalonia frontend (§217, §221, §223).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the last sessions landed - do not redo it

1. **The B3 row's real phase + read set (§228).** `xgu bump_cubemap_reflect`
   runs its dot programs at **4.4B** (`tm=0332`/`1332`, 19.9M/12.4M px), not at
   3B. The `--gpu-time` combiner rows print `tm=`, `sf=` and
   `PshCombiner.DescribeReach()` (`reach[tx= stg= src= consts= flags= lerp=]`).
   The cube word lane reads guest RAM through the direct lane instead of
   requiring the 32-entry carried window. The GL frame at that phase is still
   **4 colours** (2 at 3.6B): a computed black panel - see Option B-lead.
2. **`nxdk/usb` is a corpus row and H1 builds nxdk rows (§229).**
   `host-doctor.ps1` now checks MSYS2 (`C:\msys64`) as well as PATH; nxdk is at
   `D:\nxdk` (`a6c31ee`). `scripts/build-nxdk-samples.ps1 -Samples @("usb")`.
   The sample enumerates the hub with its own USB stack and prints
   `Device connected on USB hub port 1 (VID: 0409 PID: 55aa)` on the display
   export.
3. **Library rows carry the certificate title id (§228).** `GameEntry.TitleId`
   via `XGrid.Host.DiscTitleId` (the same resolver the per-title overrides use),
   `TitleIdText`/`HasDistinctTitleId`; the Library view shows `Title XXXXXXXX`
   only for a distinct id (homebrew shares the `FFFF0002` placeholder).
4. **CPU correctness campaign (§225).** Nine interpreter/decoder fixes from a
   333 -> 629-case oracle, each pinned; rules in `tools/cpu-oracle/README.md`.
5. **Per-title renderer, one GPU-memory adapter (§224).**
   `TitleOverride.Renderer`; `XboxMachine.GpuMemory`; `KeyedOption` collapse.
6. **Renderer policy seam + threaded snapshots + smoke harness (§223).**
   `RendererPolicy.Decide`/`Describe`/`Normalize`; snapshot save/load queued onto
   the emulation thread; `tools/frontend-smoke` drives a real `EmulatorSession`.
7. **CHD title id (§222)** (`XdvdfsSectorReader` over any `IDvdImage`) and the
   GPU renderer as a frontend setting (§221). **§220**: GL sampler decode,
   replace-mode one-writer composition, frame stepping. **§219**: GL TEXFILTER
   parity, clear masks, per-title overrides V2. **§226/§227**: the B3 seam
   state, the live compile gate, the armed texel capture.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~0.7 s, CPU engine ~17 s, block builds
~1.4 s, interpreter ~2.5 s, boot ~2 s, readback ~0.8 s only when a frame is
captured. **Menu `sw` (the frontend default)**: ~32 s at 1B, of which the
software rasterizer is ~11.5 s. **Triangle `gl+replace`** ~7.5-7.9 s (11.5 s sw
on gate media). Track C stays frozen.

**Through the real frontend session (H1, 1B, gate media, §221d/§223):** software
**34.8 s** vs OpenGL **24.7 s** (-29%), 687 vs 688 flips, same non-black pixel
count; the 40 s-pair throughput probe reads +9.8%/+8.2%.

## 2. Recommended next session - pick one

### Option B-lead (Track B/E, §228): the GL path computes a black panel on `bump_cubemap_reflect`

The row's own dot programs run at **4.4B** and the GL frame is the clear plus a
black panel; the **3.6B** leg shows the same with no dot program at all
(`tm=11111C`), so the defect predates the computed modes. Leads already visible
in the state: the 3.6B program is one stage whose slot-0 texture and final lerp
(`f0=00000004`, `f1=00001400`) decide the panel; the 4.4B dot records put the
reflected colour in **alpha** through BLUE_TO_ALPHA into R0, which only the
**MUX-lsb** select consumes (the shader's mux expression reads R0 regardless),
and their final lerp (`f0=0C0B000F`, `f1=0C011180`, `sf=FF3F3F3F`) is not
implemented at all. **Cheapest first step:** a per-fragment colour/depth
differential on the 3.6B panel program - dump the CPU's `FillTrianglePsh`
texel/combiner values for one panel fragment and compare them with the shader's
`t0`/stage outputs (a temporary probe, stripped before any A/B). Acceptance: the
4.4B GL replace frame gains the panel's colours (well above 4 colours, black
share down toward software's 0%) with both 1B GL goldens unchanged.

### Option A (Track P/B, filed limit): the GL renderer cannot show a direct-framebuffer guest

The renderer presents the **flipped** surface, so nxdk `gamma`/`hello`/`usb`
(which write the framebuffer and never flip, §208) show nothing in GL mode while
the software renderer shows their scanout. `TryCopyScanoutFrameFromMemory` exists
(§221c) but is only used *before* the first backend frame. Fix shape: when the
guest's PCRTC start/pitch describe a buffer the backend did not render, fall back
to the software scanout for that frame instead of dropping it (the display
capture is a host-side read; it needs a deliberate policy statement and a
caretaker for `presentUploads`). Acceptance: `boot-to-first-frame.ps1 -Sample
gamma -Gpu gl -GpuReplace` shows the same colours as the `-Gpu sw` row; the two
gate titles' GL frames stay byte-identical.

### Option C (Track E): the remaining corpus gaps

- **A USB hot-plug row** (§229 filed): `nxdk/usb` prints only on connect; a
  disconnect event needs a hot-plug hook on `UsbHub` (the pad-script analogue),
  then the same sample asserts the disconnect line too.
- **Gap 10, `bumpenv_ripple`'s app state** (§211): needs the gap-9 frame oracle
  (xemu screendump - `scripts/xemu-screendump.ps1` has the working pieces).
- The cull winding on `bumpenv_ripple`'s quads, and the remaining DXT/paletted/
  mipmapped texture paths (unobserved by the corpus).

### Option D (Track B): replace-mode depth

The composition policy is colour-only; a mixed frame keeps guest zeta from the
software half. Composing depth is possible but needs its own differential. Do
not start it without a measured pixel delta.

### Filed, lower value - do not start while B-lead is open

- Mip selection (LOD computation); the corpus has no mipmapped sample yet.
- A GL *window* renderer in the frontend (the offscreen FBO + readback path is
  the product path).
- Mirror mode in the frontend (it still runs the software rasterizer).

## 3. Measured negatives - do NOT re-try without a new measurement

- **Reading a corpus sample at 3B** - `xgu bump_cubemap_reflect`'s dot-mode
  programs live at 4.4B; 0.2-3B only reaches the boot animation and the panel
  (§228, `corpus-phase-anchors.md`).
- **Requiring the seam's carried window to contain a cube face** - the window
  holds 32 words, a 256² face holds 65,536 texels; the word at the address the
  draw samples is the one the hardware read (§228).
- **Treating an 8-stage combiner record as one program** - check `reach[...]`:
  a stage whose output words write no register contributes nothing (§228).
- **Reading the convolution kernel field alone as "convolve"**: MIN must be
  `CONVOLUTION_2D_LOD0` (7) (`psh.c:204`); `2/2/1` is a tent (§210a).
- **Expecting a tent to change a gate title's frame** - both sample at texel
  centers (§210d; the GL registers leg is byte-identical too, §220c).
- **Blaming `bumpenv_ripple`'s black frame on the texture format or the bump
  direction chain** (§211a-211e).
- **Reading a swizzled texture's extent from `TEXIMAGERECT`** (§209a).
- **Reading the raw texture-address word** - 1 is wrap, 2 is mirror, 4 is
  border (§220b).
- **DVD-only triangle legs** - they never launch the disc XBE (§208d).
- **Running snapshot save/load on the UI thread** - a load resets backend state
  (§223b).
- **Adding SSE2 opcodes to the CPU oracle** - the Xbox CPU is a Pentium III
  (§225). **Reading absolute low addresses in an oracle case** (§225).
- A texture-buffer object for the per-triangle block; state-fingerprint batch
  boundaries; per-draw probes (sample 1-in-64 or ablate).
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.
- **"Mode 0 means the draw is degenerate"** (§213c). **"A lit fixed-function
  draw implies an enabled light"** (§215b). **Indexing a hashtable by
  `HashSet.Add()`'s return value** (it returns `bool`) - use a sorted hash.
- **Trusting `glClear` to match a PGRAPH clear** - it honors the draw write
  masks; the software fill does not (§219c).
- **`Get-Command clang` proving the nxdk toolchain is missing** - it only reads
  PATH; MSYS2 installs under `C:\msys64` (§229).
- **Passing a quoted comma list to `-Samples`** - PowerShell binds it as one
  name (`"mesh,gamma"`); use `@("mesh","gamma")`.

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
  measured ceiling behind its acceptance.
- **Track C is frozen.** No new codegen while Track B/E/P are open (interpreter
  *correctness* work is still allowed - that is what §225 was).
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run (no `--gpu-replace`). A **flagged** behavior change must show
  the default path byte-identical and the flag-on delta measured. A CPU
  instruction change must keep the CPU oracle and `XGrid.Cpu.Tests` green.
  Frontend work builds `src/XGrid.Frontend.Avalonia` (it references
  `XGrid.GpuGl`, so the solution build must stay platform-clean) and does not
  re-run the core gates, but it must not change core behavior.
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp`/renderer defaults
  without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency. `References2/`
  is read-only. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- A **snapshot version bump invalidates every `.xgs`** (now Nv2a v8, audio v3).
- Write it up: a decisions section (§230+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-22)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
trusting any wall-clock number. `References2/` is the single verified reference
store (`scripts/refs-manifest.ps1 -Verify`).

- **H1 (this machine):** Windows 11, NVIDIA GL 4.6.0 (610.88), ~90 GB free on C:
  (keep media copies on D: anyway). The nxdk toolchain is **installed** inside
  MSYS2 (`C:\msys64`: clang, lld, llvm-dlltool, make, bison, flex, cmake) with
  nxdk at `D:\nxdk` (`a6c31ee`), so nxdk corpus rows **do** build here:
  `pwsh -File scripts/build-nxdk-samples.ps1 -Samples @("usb")`.
- **Walls on H1 at 1B (fast mode, same window as session 29):** triangle
  11.5-12.2 s sw / 7.9 s `gl+replace`; menu 32.5-33.0 s sw / 24.3 s
  `gl+replace`. Treat these as ratios within one window, never as absolutes
  across hosts.

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

# corpus / per-sample frames (mind the phase anchor)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 4400000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3600000000 -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample usb -Steps 1000000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-vp_lighting -Steps 3000000000 -Gpu gl -GpuReplace -TexFilter registers
pwsh -File scripts/raster-leg.ps1 -Workload tri -TexFilter legacy

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
consts=… flags=… lerp=…]`** (§228; `PshCombiner.DescribeReach`),
**`filters=<min>/<mag>/<conv>`**, **`fixedFn=` / `fixedFnLit=` /
`fixedFnLights=`**, `PresentedFrameDebug`, `Nv2AMmio.VgaCrtcDebug`,
`--tex-filter registers`, `--ffp`, `DiagnosticGenericSampler`/
`DiagnosticGenericFill`, `--raw-shapes`, `XGRID_RAW_PROFILE=1` +
`XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`, `frames=`/`pcrtc=` on
the `Frame` line, `--frame-display` (the product surface - the only one that
shows `debugPrint` output), `--pgraph <path> --pgraph-max <n>`, `--eip-hist`,
`--eeprom-out`, and the frontend's `Renderer:` power-on line.
