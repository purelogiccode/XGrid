# Next session instructions (updated 2026-10-21 after the twenty-second session)

**Read in this order:** `docs/development-direction.md` (course correction +
measured position), `AGENTS.md` (session notes + current state),
`docs/research-notes/xgrid-architecture-decisions.md` §220 (the frontend renderer
switch on the host GPU backend) + §219 (GL TEXFILTER parity, replace-mode
composition, clear write masks, per-title overrides V2) + §218 (APU
`NV_PAPU_XGSCNT`) + §217 (per-title overrides + EEPROM state) + §216 (PTIMER
0.8.137) + §215 (fixed-function lighting) + §213 (the fixed-function vertex
stage, `--ffp`) + §212/§210 (TEXFILTER) + §211 (gap 10 diagnosed) + §209
(cubemap) + §208 (display model) + §207 (corpus),
`docs/compatibility.md` (matrix + harness + the gaps the corpus found),
`docs/research-notes/nv097-coverage.md` (method-presence gap; the fixed-function
lighting rows moved to `handled` with §215), `docs/research-notes/
usage-histogram.md` (measured NV097 and TEXFILTER usage),
`docs/research-notes/gpu-reference-map.md`, `docs/research-notes/
jit-track-charter.md` (status), `docs/research-notes/rasterizer-inventory.md`,
then this file.

## 0. Verify the start state before touching anything

```powershell
pwsh -File scripts/host-doctor.ps1     # fingerprint + gate media + References2 manifest
git log --oneline -1        # expect: the §220 session commit
git status --short          # must be clean
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build     # expect 1751 green
```

**Two machines share this tree** (`docs/hosts.md`). Start with
`host-doctor.ps1` and read its VERDICT before trusting any number in this file:
semantic anchors (SHAs/counters) must match everywhere, performance anchors
(walls) only compare within one machine and one window.

Expected 1B fast-mode anchors (**gate media**: the triangle needs HDD + DVD):

| leg (`--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw`) | RAM | CPU | wall (H1) |
|---|---|---|---|
| menu (HDD + pad script) | `34973510…` | `F5D61C94…` | 31-54 s (load) |
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
| `xgu-bump_cubemap_reflect` 3B BTFF **`-Gpu gl -GpuReplace`** | `19D576A8…` | `D5D38C64…` | ~10.4 s; frame `DA0750EE…` (= sw), `composeUploads=7` |
| `xgu-bumpenv_ripple` 5B, plain (no `--frame-first`) | `AA10223C…` | `1AB9F002…` | ~12.6 s |

A fast-mode **RAM** SHA is only meaningful with its exact flag set:
`--frame-first` caps batches at 2M steps while it hunts the first flip, which
moves interrupt delivery and therefore the RAM *and* the CPU SHA (§209e), and a
DVD-only triangle leg never launches the disc's XBE at all (§208d - it measures
the boot animation). Compare BTFF rows with BTFF rows and `raster-leg` rows with
`raster-leg` rows.

Accurate-mode anchors (JIT off, HDD+DVD): triangle RAM `812EA159…` / CPU
`2F632483…`, menu RAM `6AB39AB0…` / CPU `3D2740B8…`; both frames `A1740DE0…` and
`4EF1FBF6…`. GL frame fingerprints: menu `B6116DAE…`, triangle `7A860ED7…`
(policy diff, never byte-exact).

**The menu RAM SHAs moved in 218:** the APU `NV_PAPU_XGSCNT` register now returns
`ep_frame_div * 32` (the reference behaviour), the dashboard reads it and stores
the value, so every menu RAM image differs by that stored word while the CPU
SHA, both frames and every counter are unchanged. `51B56687 -> 34973510` (fast),
`19FEA0EA -> EE8F69FB` (`--tex-filter registers`), `87BF40DA -> CC2A94DF`
(`--ffp`), `01BE5D64 -> 6AB39AB0` (accurate). The triangle does not read it.

- **1751 tests green**, build/format clean, ktest XGrid leg at **792,800,535**
  steps (suite log 40,797 B, SHA `CA5D3867…`, byte-identical).
- Fast mode is opt-in (`--jit`, `--jit-raw`); accurate mode is gate currency.
  **`--ffp` and `--tex-filter registers` are opt-in too**, all reachable as
  frontend settings, and since §220 the **renderer** is a frontend setting too
  (software default; OpenGL is Windows-only and opt-in).
- **Oracle: `References2/xemu-0.8.137` is primary.** GPU orientation:
  `gpu-reference-map.md`. Method gaps: `nv097-coverage.md` (regenerate after
  Nv2a edits; it is a snapshot).

## 0b. What the twenty-second session landed (§220) - do not redo it

1. **The frontend can render on the host GPU backend.** `Nv2AMmio.GpuMemory`
   exposes the machine's guest-memory adapter; frontend
   `Services/GraphicsBackendHost.TryAttach` attaches `GlBackendFactory`
   (`windowEnabled: false, replaceSoftware: true, frameReadback: true`) on the
   emulation thread and `Detach`s it in the session's `finally`; frames keep
   flowing through `TryCopyDisplayFrame -> FramePacket -> WriteableBitmap`.
   `RendererPolicy.Decide(renderer, platformSupportsOpenGl)` is the pure
   platform decision (Software / OpenGl / OpenGlUnavailable).
2. **Snapshot save/load runs on the emulation thread.** `EmulatorSession` queues
   the request and the loop serves it between batches (and during a pause; the
   pause gate wait is now a 50 ms timed wait). Reason: `LoadState ->
   Backend?.ResetState()` is GL work and must run where the context is current.
3. **Backend readbacks are cloned before they leave the emulation thread**, so
   the UI cannot copy a frame the next flip is overwriting.
4. Measured on the dashboard (interleaved 40 s pairs, `tools/frontend-smoke`):
   software 1.014B / 1.021B steps vs OpenGL 1.121B / 1.106B (**+9.8% / +8.2%**),
   same non-black pixel count, save/load round trip works with GL attached; the
   CLI's 1B menu leg is the ceiling at **33.95 -> 23.46 s (-30.9%)**. Every gate
   anchor byte-identical.
5. The frontend drops the NV2A per-method histogram (`CollectMethodHistogram =
   false`, matching the CLI); no UI surface reads it.

## 1. Where the remaining wall is (measured, not guessed)

**Menu `gl+replace`**: GL submission ~1.05 s, CPU engine ~17 s, block builds
~1.4 s, interpreter ~2.5 s, boot ~2 s, readback ~0.8 s when the frame is
captured. **Menu `sw` (the frontend default)**: ~34 s at 1B, of which the
software rasterizer is ~11.5 s. **Triangle `gl+replace`** ~7.5 s (10.9 s sw on
gate media). Track C stays frozen.

## 2. Recommended next session - pick one

### Option A (Track P/B, filed limit): the GL renderer cannot show a direct-framebuffer guest

The §220 renderer presents the **flipped** surface, so nxdk `gamma`/`hello`
(which write the framebuffer and never flip, §208) show nothing in GL mode while
the software renderer shows their scanout. Fix shape: when the guest's PCRTC
start/pitch describe a buffer the backend did not render, fall back to the
software scanout for that frame instead of dropping it (the display capture is a
host-side read, so this is not "mixing" in the guest-visible sense, but it needs
a deliberate policy statement and a caretaker for `presentUploads`). Acceptance:
`boot-to-first-frame.ps1 -Sample gamma -Gpu gl -GpuReplace` shows the same
colours as the `-Gpu sw` row; the two gate titles' GL frames stay byte-identical.

### Option B (Track P, the largest product gap): host audio output

Sound is the last "feels like an emulator" piece and the only one from M-C that is
still missing, but it is **not** last-mile wiring: the APU's GP DSP must execute
the guest's mix blocks (`References2/xemu-0.8.137/hw/xbox/mcpx/apu/dsp/interp/`
is the semantics oracle; the JIT is an external Rust crate, ignore it), the EP
side needs the FIFO/DMA protocol (`dma.c`, format 0 = 8-bit), and only then can
an S/PDIF/PCM sink feed a host device. Scope it as **DSP execution first**
(bit-exact vs `interp/`, then observable mixer output counters), then the sink.
No oracle title exists in the corpus yet (`xaudio` only proves the voice engine
is live), so the acceptance is synthetic + the `xaudio` row.

### Option C (filed defects, each needs its own differential)

- **`bumpenv_ripple`'s app state** (gap 10): diagnosed, needs gap 9's
  second-emulator frame.
- **Second-emulator oracle** (gap 9): `xemu -display xemu` + F12 screenshot, or
  the redirected-stdio QMP read loop.
- **The cull winding on `bumpenv_ripple`'s quads** (§211f), and the remaining
  DXT/paletted/mipmapped texture paths (unobserved by the corpus).
- Per-title overrides V2 follow-ups: a CHD title-id reader (feed the CHD sector
  stream into XISOSharp), title-id display in the library rows, and a
  per-title renderer (the renderer is a global setting today).

### Filed, lower value - do not start while A/B/C are open

- Mip selection (LOD computation); the corpus has no mipmapped sample yet.
- Faithful VGA read-back, texture fingerprint exactness, the 2-pixel k1
  interleave - all measured below the noise floor (§204/§205/§208).

## 3. Measured negatives - do NOT re-try without a new measurement

- **Reading the convolution kernel field alone as "convolve"**: MIN must be
  `CONVOLUTION_2D_LOD0` (7) (`psh.c:204`); `2/2/1` is a tent (§210a, and the GL
  shader applies the same rule since §219a).
- **Expecting a tent to change a gate title's frame** - both sample at texel
  centers (§210d).
- **Blaming `bumpenv_ripple`'s black frame on the texture format or the bump
  direction chain** (§211a-211e).
- **Reading a swizzled texture's extent from `TEXIMAGERECT`** (§209a).
- **DVD-only triangle legs** - they never launch the disc XBE (§208d).
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
- **Running snapshot save/load on the UI thread** - a load resets backend state
  (GL) and the context belongs to the emulation thread (§220b).

## 4. Rules (non-negotiable)

- **Steering rule.** Every proposal states the track it serves (B/E/P/D) and the
  measured ceiling behind its acceptance.
- **Track C is frozen.** No new codegen while Track B/E/P are open.
- **Two-tier validation.** Accurate mode is currency. Any shared-code or default
  change needs median-of-3 + exact SHAs/counters + `verifyFailures=0` +
  flag-off byte-identical, plus the fast-mode gates. Host-side-only diagnostics
  still need the 1B goldens (both backends) + ktest smoke. A GL-submission
  change additionally needs both 1B frame goldens byte-identical and the
  mirror-mode run (no `--gpu-replace`). A **flagged** behavior change must show
  the default path byte-identical and the flag-on delta measured.
- **Strip probe instrumentation before any A/B leg** (and before committing).
- **1B runs only from a full solution build.**
- Do **not** flip `--jit`/`--jit-raw`/`--tex-filter`/`--ffp`/renderer defaults
  without an operator decision.
- No real Xbox games in gates; `data/test-samples/` is gate currency. `References2/`
  is read-only. Leave `Directory.Packages.props` alone.
- Direct development only (no wave runner, no worktrees, no detached servers).
- A **snapshot version bump invalidates every `.xgs`** (now Nv2a v8, audio v3).
- Write it up: a decisions section (§220+) with the measured table, charter +
  inventory status headers, `AGENTS.md` session note, and a fresh copy of this
  file.

## 5. Host state - two machines (2026-10-21)

**Development runs on two machines sharing this Dropbox tree.** Their
fingerprints live in `docs/hosts.md`; run `scripts/host-doctor.ps1` before
believing any wall in this file. Anchor discipline: **semantic** anchors (SHAs,
counters) must match on both machines; **performance** anchors (walls, `msps`,
`rasterPsh`) compare only within one machine and one window.

H1 (PETERSONPC) is the reference host: every semantic anchor reproduces exactly,
`vec=0`, no fault. This session's walls at 1B: menu slice-1 golden 67.5 s sw /
76.8 s gl, triangle slice-2 golden 44 s sw / 50 s gl, fast menu 33.9 s sw
(23.5 s `gl+replace`), fast triangle 10.9 s, frontend dashboard session (fast,
interleaved 40 s pairs) 1.014B-1.021B steps software / 1.106B-1.121B OpenGL.
Treat ceilings as ratios.

## 6. Commands

```powershell
# build / test / format
dotnet restore XGrid.sln                     # only after project-graph changes
dotnet build XGrid.sln -c Release --nologo --no-restore
dotnet test  XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore; dotnet format XGrid.sln --no-restore --verify-no-changes

# standing ktest gate (~1-5 min on this host; needs -XisoSharp here)
pwsh -File scripts/ktest-smoke.ps1 `
  -FirmwareDir "data\xemu-0.8.136-windows-x86_64" `
  -SuiteXbe "data\test-samples\kernel-test-suite-default.xbe" -SkipXemu `
  -XisoSharp "C:\Users\HomePC\Dropbox\source\repos\CSharp_XISOSharp\XISOSharp.Cli\bin\Release\net10.0\XISOSharp.exe" `
  -WorkDir wt-data\ktest-<n>
# (a stale work dir can be locked: pass a fresh -WorkDir)

# one measurement leg (gate media; parses wall/outerMs/rasterPsh/pixels/SHAs)
pwsh -File scripts/raster-leg.ps1 -Workload tri  -Log wt-data\leg-tri.log
pwsh -File scripts/raster-leg.ps1 -Workload menu -Log wt-data\leg-menu.log
# TEXFILTER leg:    add -TexFilter registers
# fixed-function:   add -Extra @("--ffp")
# GL leg:           add -Gpu gl -GpuReplace ; A/B: -Cli <other tree>\...\XGrid.Cli.dll

# accurate 1B RAM/CPU anchors (the goldens bake --frame; this prints --bench SHAs)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... --steps 1000000000 --gpu sw --bench

# all three frame exports in one run (flip = gate anchor, scanout/display = TV)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... `
  --frame wt-data\out.bmp --frame-scanout wt-data\out-scan.bmp --frame-display wt-data\out-display.bmp

# live EEPROM image out of a run (same bytes the frontend persists)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll boot ... --eeprom-out wt-data\eeprom-out.bin

# the per-title override key of an image (V2)
dotnet src\XGrid.Cli\bin\Release\net10.0\XGrid.Cli.dll disc title wt-data\xemu-tri\triangle.iso

# corpus row + its trend (the oracle screenshot lives outside this repo)
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3000000000
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps 3000000000 -Gpu gl -GpuReplace
pwsh -File scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -TexFilter registers
python scripts/frame-stat.py wt-data\cube-legacy.bmp wt-data\cube-reg.bmp

# GL goldens (1B): both need -Hdd (the default wt-data\xbox_hdd.qcow2 is gone on H1)
pwsh -File scripts/gpu-slice1-golden.ps1 -OutDir wt-data\b1gold -Hdd wt-data\chain-199l\hdd.qcow2
pwsh -File scripts/gpu-slice2-triangle-golden.ps1 -OutDir wt-data\b2gold -Hdd wt-data\chain-199l\hdd.qcow2 `
  -Dvd wt-data\xemu-tri\triangle.iso

# frontend application (software default; choose the OpenGL renderer on Settings)
dotnet run --project src/XGrid.Frontend.Avalonia
# §220 measurement harness (headless session, one renderer per run):
#   dotnet build tools\frontend-smoke\frontend-smoke.csproj -c Release
#   dotnet run --project tools\frontend-smoke -c Release --no-build -- <software|gl> 40

# session start: fingerprint + precondition + References2 integrity
pwsh -File scripts/host-doctor.ps1
pwsh -File scripts/refs-manifest.ps1 -Verify          # add -Quick to skip content hashing
```

## 7. Diagnostics worth keeping

`--gpu-time` (`raster seconds legacy= psh= ... fastfills=k1/k2/k3 compiled=...`),
**`XGRID_GL_PROBE=1`**, **`XGRID_GL_NO_BATCH=1`**, the GL line's
`timed[drawMs= flushMs= clearMs= readbackMs= readbacks=]` and
`diag[fboReallocs= fboZetaDrops= clipXY= flipEmpty= presentUploads=
flipStaleUploads= composeUploads= batchCalls= batchTris= batchCap=]`,
`readback=on|off`, `methods=`, **`filters=<min>/<mag>/<kernel>`**, **`fixedFn=` /
`fixedFnLit=` / `fixedFnLights=`**, `PresentedFrameDebug`, `Nv2AMmio.VgaCrtcDebug`,
`--tex-filter registers`, `--ffp`, `DiagnosticGenericSampler`/`DiagnosticGenericFill`,
`--raw-shapes`, `XGRID_RAW_PROFILE=1` + `XGRID_RAW_CHAIN=0`,
`XGRID_RAW_SKIP_HEADS`, `XGRID_BLOCK_TRACE[_FROM/_TO/_HEAD]`, `XGRID_SLOW=1`,
`XGRID_RAW_DUMP=1`, `--jit-timing`, `--jit-tally`, `--bench`, `--frame-first`,
`frames=`/`pcrtc=` on the `Frame` line, `--pgraph <path> --pgraph-max <n>`,
`--eip-hist`, `--eeprom-out`, and the frontend's power-on `Renderer:` line (which
renderer attached and, on a fallback, why).

**Probe recipe for a black-frame row** (§211 used it): print the method stream
with `--pgraph`, then gate temporary probes on a single env var and print (a) per
flip the presented surface's non-black count, (b) per draw the mode/ranges/
program/combiner/texture-state key, (c) the first vertices' post-transform
position + diffuse + uv, (d) the first fragments' coords, texels, diffuse and
final colour. Strip every probe before committing - `git status` must show only
the intended files.
