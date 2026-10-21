# Next session instructions (updated 2026-10-22 after the twenty-fifth session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §225 (CPU correctness
campaign) + §224 (per-title renderer, one GPU-memory adapter, `KeyedOption`) +
§223 (renderer policy seam, snapshot requests on the emulation thread, frontend
smoke harness) + §222 (CHD title id) + §221 (the GPU renderer as a frontend
setting) + §220 (GL sampler decode, replace-mode composition, frame stepping) +
§219 (GL TEXFILTER parity, clear write masks, per-title overrides V2) + §218
(APU `NV_PAPU_XGSCNT`) + §217 (per-title overrides + EEPROM state) + §216
(PTIMER 0.8.137) + §215 (fixed-function lighting) + §213 (the fixed-function
vertex stage, `--ffp`) + §212/§210 (TEXFILTER) + §211 (gap 10 diagnosed) + §209
(cubemap) + §208 (display model) + §207 (corpus), `docs/compatibility.md` (matrix
+ harness + the gaps the corpus found), `docs/research-notes/usage-histogram.md`
(measured NV097 / TEXFILTER / sampler address-mode usage),
`docs/research-notes/nv097-coverage.md` (method-presence gap; the fixed-function
lighting rows moved to `handled` with §215),
`docs/research-notes/gpu-reference-map.md`,
`docs/research-notes/jit-track-charter.md` (status),
`docs/research-notes/rasterizer-inventory.md`, `tools/cpu-oracle/README.md`,
then this file.

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest
git log --oneline -1        # expect: the "Merge pull request #10" commit
git status --short          # must be clean
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1812 green
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
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 31-54 s (load); 33.17 s measured |
| menu + `--tex-filter registers` | `EE8F69FB…` | `F5D61C94…` | ~32 s |
| menu + `--ffp` | `CC2A94DF…` | `F5D61C94…` | ~59 s (the FFP layer draws 121M more px) |
| triangle BTFF (HDD + DVD, `--frame-first`) | `0117A5E2…` | `7DAB63EB…` | ~11.2 s |
| triangle, plain (HDD + DVD, no `--frame*`) | `CB95C006…` | `B8EF1021…` | ~10.5 s |
| triangle, `scripts/raster-leg.ps1 -Workload tri` (HDD + DVD) | `B45254CA…` | `7DAB63EB…` | ~10.9 s |
| triangle + `--tex-filter registers` | `B45254CA…` | `7DAB63EB…` | ~11.7 s |
| triangle + `--ffp` | `B45254CA…` | `7DAB63EB…` | ~14.9 s (frame identical too) |
| `xgu-spheremap_ffp` 3B BTFF | off `B5747761…` / on `8062CFB5…` | `F0075C2F…` (both) | ~9 / ~14 s |
| `xgu-distance_fog_ffp` 3B BTFF | off `AFF08085…` / on `12BAD517…` | `571363B4…` (both) | ~9 / ~30 s |
| `xgu-bump_cubemap_reflect` 3B BTFF | `DDB59317…` | `D5D38C64…` | ~12 s |
| `xgu-bump_cubemap_reflect` 3B BTFF **`-Gpu gl -GpuReplace`** | `19D576A8…` | `D5D38C64…` | ~10.4 s; frame `DA0750EE…` (= sw), `composedFrames=7` |
| `xgu-vp_lighting` 3B BTFF | `162DAFF1…` | `0380C502…` | ~9.5 s |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). Compare BTFF rows with BTFF rows and `raster-leg` rows with
`raster-leg` rows. A `-Gpu gl` row's RAM SHA reflects the backend's own writes
(the §220d composition writes the FBO image into guest RAM); its **frame** is
the anchor.

Accurate-mode anchors (JIT off, HDD+DVD): triangle RAM `812EA159…` / CPU
`2F632483…`, menu RAM `6AB39AB0…` / CPU `3D2740B8…`; both frames `A1740DE0…` and
`4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle `7A860ED7…`
(policy diff, never byte-exact). Both GL goldens were re-run **after** the
single-adapter change (§221a/§224) and reproduce these exactly (`diff=2602` /
`diff=27849`, `violations=0`, `maxDelta=1`, `BackendUnsupported=0`).

**The menu RAM SHAs moved in §218:** the APU `NV_PAPU_XGSCNT` register now returns
`ep_frame_div * 32` (the reference behaviour), the dashboard reads it and stores
the value, so every menu RAM image differs by that stored word while the CPU
SHA, both frames and every counter are unchanged. `51B56687 -> 34973510` (fast),
`19FEA0EA -> EE8F69FB` (`--tex-filter registers`), `87BF40DA -> CC2A94DF`
(`--ffp`), `01BE5D64 -> 6AB39AB0` (accurate). The triangle does not read it.

- **1812 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, SHA `CA5D3867…`, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp`, `--tex-filter registers` and the OpenGL renderer are opt-in too**,
  and all three are settings in the Avalonia frontend (§217, §221, §223).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the last sessions landed - do not redo it

1. **CPU correctness campaign (§225).** `tools/cpu-oracle/` went 333 -> 629
   cases and found nine defects, each fixed and pinned: `PMINSW`/`PMAXSW` were
   compared unsigned (decoded as invented `Pminuw`/`Pmaxuw`); `PSRAW`/`PSRAD`
   saturating counts sign-filled from the low element only; `XLAT` was not
   decoded; 16-bit `[SI]`/`[DI]` consumed a bogus disp16 (a decoder bug that
   desynchronized the stream); x87 NaN stores flipped the sign and dropped the
   payload; the `FUCOM` family latched IE for a quiet NaN; `FSQRT` of a negative
   value left C2 clear; `MINPS`/`MAXPS`/`MINSS`/`MAXSS` returned the destination
   on equal operands; and three corpus cases read an absolute low address. Rules
   are in `tools/cpu-oracle/README.md` (page-relative cases, no SSE2 - the CPU is
   a Pentium III). The oracle test sizes its memory from the case count.
2. **Per-title renderer, one GPU-memory adapter (§224).**
   `TitleOverride.Renderer` (`software`/`opengl`, null inherits) lets the
   settings row offer `inherit`/`software`/`OpenGL` per title;
   `XboxMachine.GpuMemory` publishes the adapter the NV2A uses (the CLI wraps
   nothing); `NetworkOption`/`TextureFilterOption`/`RendererOption` collapsed
   into `KeyedOption`; the Avalonia `AVLN5001` warnings are fixed.
3. **Renderer policy seam + threaded snapshots + smoke harness (§223).**
   `RendererPolicy.Decide`/`Describe`/`Normalize` is the pure platform policy,
   `GraphicsBackendHost.TryAttach`/`Detach` the side effects; snapshot save/load
   is queued and served on the emulation thread (a load resets GL backend state);
   `tools/frontend-smoke` drives a real `EmulatorSession` headlessly. In-app
   throughput: software 1.014B/1.021B steps vs OpenGL 1.121B/1.106B
   (+9.8%/+8.2%), same picture, 72 MB `.xgs` round trip with GL attached.
4. **CHD title id (§222).** `XGrid.XboxFs.XdvdfsSectorReader` walks the XDVDFS
   volume over any `IDvdImage` (descriptor at partition sector 32, magic at
   +0x00/+0x7EC, root table at +20/+24, `14 + nameLen` entries, the `0xFFFF`
   sector-pad sentinel, XGD disc-offset probe list); `DiscTitleId` resolves
   `.chd` through `ChdDvdImage.Open` + `TryReadImage(IDvdImage, …)`.
   Cross-checked against the XISOSharp path on six real ISOs. **No CHD exists on
   H1**, so the container hand-off is test-covered but not run end to end.
   Doc correction: `xisosharp.md`'s trailing-magic offset is `+0x7EC`.
5. **The GPU renderer is a frontend setting (§221).** Settings → Renderer:
   software (default) or OpenGL. `EmulatorSession.AttachRenderer` (via
   `GraphicsBackendHost`) creates the headless WGL context **on the emulation
   thread** in replacement mode with `frameReadback: true`, logs the probe line,
   and falls back to software with a logged reason on any failure (non-Windows,
   probe, GL version). `DetachRenderer` clears `Nv2A.Backend` and disposes in the
   `finally`. The display follows the renderer; `TryCopyScanoutFrameFromMemory`
   reads the RAM scanout **before** the first backend frame (direct-framebuffer
   guests that never flip). Measured at 1B: 34.8 s software vs 24.7 s OpenGL.
6. **GL sampler decode + composition (§220).** The seam carries resolved
   `SamplerWrapU/V` (0 wrap, 1 mirror, 2 clamp, 3 clamp-to-border), `BorderColor`
   and the resolved TEXFILTER pair from `ResolveTextureFilter`; the shader emits
   `SampleTap`/`SampleTent`/`SampleConvolved` behind
   `uTexBilinear`/`uTexConvolution`/`uTexBorder`, the PROJECT2D w divide and the
   clip origin on both axes. A declined replace-mode draw composes the FBO into
   guest RAM (`ColorSurfaceAddress`/`ColorPitch`) and the frame continues in
   software; **depth/stencil are not composed**. Track P frame stepping (F7 /
   Shift+F7, 8M-instruction budget).
7. **§219 (previous session).** GL TEXFILTER parity (gap 7), clear masks
   (`ClearMaskLiveTests`), per-title overrides V2 keyed by `title:XXXXXXXX`, and
   the original flip-drop composition (§219b, superseded by §220d).

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~1.05 s, CPU engine ~17 s, block builds
~1.4 s, interpreter ~2.5 s, boot ~2 s, readback ~0.8 s only when a frame is
captured. **Menu `sw` (the frontend default)**: ~34 s at 1B, of which the
software rasterizer is ~11.5 s. **Triangle `gl+replace`** ~7.5 s (10.9 s sw on
gate media). Track C stays frozen.

**Through the real frontend session (H1, 1B, gate media, §221d/§223):** software
**34.8 s** vs OpenGL **24.7 s** (-29%), 687 vs 688 flips, same non-black pixel
count; the 40 s-pair throughput probe reads +9.8%/+8.2%. The app is still
`Run(65536)`-batched with a 30 Hz display capture, so the remaining product cost
is the same CPU engine the CLI measures.

## 2. Recommended next session - pick one

### Option A (Track P/B, filed limit): the GL renderer cannot show a direct-framebuffer guest

The renderer presents the **flipped** surface, so nxdk `gamma`/`hello` (which
write the framebuffer and never flip, §208) show nothing in GL mode while the
software renderer shows their scanout. `TryCopyScanoutFrameFromMemory` already
exists (§221c) but is only used *before* the first backend frame. Fix shape: when
the guest's PCRTC start/pitch describe a buffer the backend did not render, fall
back to the software scanout for that frame instead of dropping it (the display
capture is a host-side read, so this is not "mixing" in the guest-visible sense,
but it needs a deliberate policy statement and a caretaker for
`presentUploads`). Acceptance: `boot-to-first-frame.ps1 -Sample gamma -Gpu gl
-GpuReplace` shows the same colours as the `-Gpu sw` row; the two gate titles' GL
frames stay byte-identical.

### Option B (Track B): B3 method coverage - zero `unsup` rows

The GL backend declines a deliberate list of texture modes
(`CheckModesUnsupported`: DOT_PRODUCT/DOT_ZW/DOT_ST, DOT_REFLECT_DIFF/SPEC,
DOT_STR3D/CUBE, BUMPENVMAP/LUM, and the cube/volume success paths). At 1B the
corpus rows are all in the boot animation with `unsupported=0`; the cubemap row
at **3B** is the one that trips, and `unsupTex[m0C=14 m11=28]` (§221e) says it is
**two** mode families: DOT_REFLECT_SPECULAR on stage 3 and DOT_PRODUCT on two
earlier stages. Honouring them in GLSL needs a cube-texture upload path plus the
`ResolveDotReflection`/`TrySampleTextureCube` semantics transcribed. Measure
first (the mode histogram exists now) before building it.

### Option C (Track E): the two remaining corpus gaps

- **Gap 10, `bumpenv_ripple`'s app state** (§211): diagnosed to a colour-clear
  frame with a nearer-than-everything zeta clear; needs the gap-9 oracle to know
  what state the reference ends up in. The cheapest start is gap 9 (xemu
  screendump over stdio/QMP - `scripts/xemu-screendump.ps1` has the working
  pieces and the open read-loop problem), then a per-frame differential.
- **Second-emulator oracle** (gap 9): `xemu -display xemu` + F12 screenshot, or
  the redirected-stdio QMP read loop. This one unlocks both gap 10 and a menu
  oracle.
- **The cull winding on `bumpenv_ripple`'s quads** (§211f), and the remaining
  DXT/paletted/mipmapped texture paths (unobserved by the corpus).

### Option D (Track B): replace-mode depth

The composition policy is colour-only: a frame that mixes GL and software
writers keeps guest zeta from the software half, so the GL geometry's depth is
lost. Composing depth is possible (read the depth/stencil renderbuffer and write
it into guest zeta in the surface's Z16/Z24S8 layout) but needs its own
differential - `bumpenv_ripple` (gap 10) or a synthetic seam test. Do not start
it without a measured pixel delta.

### Filed, lower value - do not start while A/B are open

- Mip selection (LOD computation); the corpus has no mipmapped sample yet.
- Title-id display in the library rows (the CHD reader landed in §222).
- Faithful VGA read-back, texture fingerprint exactness, the 2-pixel k1
  interleave - all measured below the noise floor (§204/§205/§208).
- A GL *window* renderer in the frontend: the offscreen FBO + readback path is
  the product path; a second window would need input routing for no gain.
- Mirror mode in the frontend: it still runs the software rasterizer for every
  draw, so it buys no speed.

## 3. Measured negatives - do NOT re-try without a new measurement

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
- **"A GL backend needs a foreign window, so it cannot be embedded"** - the
  headless backend is an offscreen FBO; the frontend uses it with no window and
  no conditional compilation (§221).
- **Running snapshot save/load on the UI thread** - a load resets backend state
  (GL) and the context belongs to the emulation thread (§223b).
- **Adding SSE2 opcodes to the CPU oracle** - the Xbox CPU is a Pentium III, so
  the `66`-prefixed 128-bit packed-integer forms, `PADDQ`/`PSUBQ`/`MOVDQA`/
  `PSHUFD` and `MOVD xmm` are *correctly* `UnsupportedInstruction` (§225).
- **Reading absolute low addresses in an oracle case** - the CI harness reuses
  one memory image across cases while the oracle maps a fresh one, so such a
  case measures the previous case. Keep every case page-relative (§225).
- **Treating an x87 quiet-NaN compare as invalid** - `FUCOM`/`FUCOMI` only
  signal for a *signaling* NaN (§225).
- A texture-buffer object for the per-triangle block; state-fingerprint batch
  boundaries; per-draw probes (sample 1-in-64 or ablate).
- O5 counter batching, C3-y bookkeeping, the 1024-slot data translate cache,
  relay/E-series emission, block fusion - all measured, all out.
- **"Mode 0 means the draw is degenerate"** - the dashboard's 22,207 mode-0
  draws render a real background layer (§213c).
- **Assuming a lit fixed-function draw implies an enabled light** - every leg
  measured so far has all lights off (§215b).
- **Trusting `glClear` to match a PGRAPH clear** - it honors the draw write
  masks; the software fill does not (§219c).

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
- Write it up: a decisions section (§226+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-21)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
trusting any wall-clock number. `References2/` is the single verified reference
store (`scripts/refs-manifest.ps1 -Verify`).

- **H1 (this machine):** Windows 11, NVIDIA GL 4.6.0 (610.88), C: has limited
  free space - keep media copies on D:. `clang`/`make` are absent, so the nxdk
  corpus rows cannot be rebuilt here (build them on a host with MSYS2 +
  `scripts/build-nxdk-samples.ps1`).
- **Walls on H1 at 1B (fast mode, quiet host):** triangle ~10.5-11.2 s sw /
  ~7.5 s `gl+replace`; menu ~33 s sw / ~24 s `gl+replace` (frontend session
  34.8 s sw / 24.7 s gl). Treat these as ratios within one window, never as
  absolutes across hosts.

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore
dotnet format XGrid.sln --verify-no-changes --no-restore

# gates
pwsh -File scripts/gpu-slice1-golden.ps1 -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-golden.ps1 -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/ktest-smoke.ps1 -SkipXemu

# corpus / per-sample frames
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3000000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3000000000 -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -TexFilter registers
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-vp_lighting -Steps 3000000000 -Gpu gl -GpuReplace -TexFilter registers
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-spheremap_ffp -Steps 3000000000 -Ffp
pwsh -File scripts/raster-leg.ps1 -Workload tri -TexFilter legacy

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
batchTris= batchCap=]`, `unsupTex[...]`, `readback=on|off`, `methods=`,
**`filters=<min>/<mag>/<conv>`**, **`fixedFn=` / `fixedFnLit=` /
`fixedFnLights=`**, `PresentedFrameDebug`, `Nv2AMmio.VgaCrtcDebug`,
`--tex-filter registers`, `--ffp`, `DiagnosticGenericSampler`/
`DiagnosticGenericFill`, `--raw-shapes`, `XGRID_RAW_PROFILE=1` +
`XGRID_RAW_CHAIN=0`, `XGRID_RAW_SKIP_HEADS`,
`XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`, `XGRID_RAW_DUMP=1`,
`--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`, `frames=`/`pcrtc=` on
the `Frame` line, `--pgraph <path> --pgraph-max <n>`, `--eip-hist`,
`--eeprom-out`, and the frontend's `Renderer:` power-on line.
