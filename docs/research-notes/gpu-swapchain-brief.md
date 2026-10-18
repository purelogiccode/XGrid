# Swapchain / windowing decision brief (wave-12 T4, 2026-10-14)

> **STATUS 2026-10-17: T1 IMPLEMENTED (O2 adopted).** The triggers were met
> (§132 (a*) dirty-bit merged; free build slot) and the opt-in `--window`
> landed — pure user32/gdi32/opengl32 interop, `wglSwapIntervalEXT(0)`,
> sink-after-readback, headless `--gpu sw` unchanged and still the default +
> gate currency. Implementation + acceptance evidence: research-notes §184
> (`docs/research-notes/xgrid-architecture-decisions.md`). Also fixed en
> route: a latent `wglGetProcAddress` EntryPoint regression that had made
> `--gpu gl` silently fall back to software (§184). T2 (O3 package) stays
> deferred; O4 stays a non-goal.

Decision brief for the operator-pending item: **"should XGrid present frames to a
visible host window (swapchain), and via which mechanism?"** Read-only research —
one new file, no existing file touched, no commits. `§N` refs (with line numbers)
= `docs/research-notes/xgrid-architecture-decisions.md`; `design §N` =
`docs/research-notes/gpu-backend-design.md`. xemu refs are facts only, never code.

Anchor points: `§132:7202` lists *swapchain/windowing (operator-pending)* as an
explicit non-goal of the dirty-bit work; `§129:6927-6929` states the survivors
binding every option — *"never mix (full-frame replace or no frame), no async
(emulation-thread only), headless FBO only (no windowing/swapchain until the
operator decides)"*; `AGENTS.md` "Next session" item 4: *Swapchain/windowing
decision (operator) still pending.*

---

## 1. Question (precisely scoped)

The question is **not** GPU throughput: `§122:6489-6492` measured GL submission +
readback at **0–2.9% of headless wall** on all four workloads (menu/triangle/007
fmv/007 transition) → *"emulation (CPU interpretation + devices) is 97–100% of
every run… a swapchain present path therefore buys ~nothing for throughput; it is
a latency/interactivity feature."* So the decision is:

1. Do we want **live frames on screen during multi-minute runs** (today: zero
   visual output until the run ends, and only the *last* frame —
   `Program.cs:704-707` writes `--frame` once after the batch loop)?
2. If yes, do we pay a **windowing package** (operator single-dependency rule,
   `sources.md:151-156`) or stay on **pure platform interop** like the existing
   hidden WGL window (`WglContext.cs`)?
3. Does any of it endanger the **golden-frame gate methodology**, the
   **never-mix / no-async / headless** survivors (`§129:6927-6929`), or CI?

---

## 2. Current-state map (our present path, end-to-end, file:line)

**Trigger (guest → seam).** `NV097_FLIP_INCREMENT_WRITE (0x12C)` is handled at
`Nv2aMmio.cs:2794-2806`: bump `FlipWriteDebug` modulo, `FramesPresented++`,
`CapturePresentedFrame()` (`Nv2aMmio.cs:5092-5112` — validates clip/pitch/format
and latches the guest surface address), then `Backend?.Flip(new
PgraphSurfaceRecord(width, height, colorBytes, frame, SurfaceBase(_dmaColor) +
PresentedColorOffset, pitch))` (`Nv2aMmio.cs:2800-2804`). `0x130` flip-stall
only counts (`Nv2aMmio.cs:2807-2809`); guest pacing lives *above* the seam:
`Advance` walks `FlipReadDebug` toward `FlipWriteDebug` on vblank
(`Nv2aMmio.cs:783, 805-811`). **No host present is involved in pacing.**

**Seam.** `IPgraphBackend` (`IPgraphBackend.cs:16-42`): `Flip(in
PgraphSurfaceRecord)` at `:34`, `ReadbackPresented(...)` at `:38`, plus
`Method/Draw/Clear/ResetState`. Synchronous, emulation-thread-only by contract
(`IPgraphBackend.cs:13-14`; design `§7` risk 3 `:453-462` no-async). Backend
attached only under `--gpu gl` (`Program.cs:314-328`, probe-gated via
`GlBackendFactory.TryCreate` `GlBackendFactory.cs:17-58`, self-test
`GlProbe.TryRun` `:32`, `GlProbe.cs:34`).

**GL side.** `GlPgraphBackend.Flip` (`GlPgraphBackend.cs:308-324`) →
`FlipCore` (`:573-661`):
- `:581-589` non-RGBA32 → `BackendUnsupported++`, drop readback;
- `:591-620` **empty-FBO present-upload** (§129): `PresentUpload.TryBuildUploadPixels`
  (`:602`) + `EnsureFbo` + `UploadFullFrame` (throwaway texture + NEAREST
  full-rect blit, `:671-…`) → `presentUploads++`; failure → `DeleteFbo` +
  `flipEmpty++` and *no frame served* (honest abstain, never mixed);
- `:622-630` FBO smaller than flip → unsupported + drop (guard);
- `:634-645` `glReadPixels` FBO → RGBA (`:641`) — this is the §122 "readback"
  timing line (menu 1131 ms / 686 flips ≈ 1.65 ms per flip, §122 table `:6477-6482`);
- `:647-656` row-order note + R/B swap → `_readback` BGRA bytes (`:658-660`).

**Readback out.** `ReadbackPresented` is a pure cached getter
(`GlPgraphBackend.cs:327-333` — returns `_readback`, never re-reads GL).
`Nv2aMmio.TryCopyPresentedFrame` (`Nv2aMmio.cs:5116-5126`): backend attached →
**exclusively** `Backend.ReadbackPresented` (the never-mix rule, `§122:6320`);
no backend → software copy from guest RAM (`:5128-5154`).

**Export.** CLI `--frame` at end of run (`Program.cs:704-707` boot, `:1045-1047`
testrom) → `SavePresentedFrame` (`Program.cs:1131-1146`) →
`DisplayFrame.Show` (`DisplayFrame.cs:28-49`, BGRA clone) → `SaveBmp`
(`DisplayFrame.cs:68-102`). `--fb` is a raw guest-RAM dump, separate path
(`Program.cs:1098-1123`).

**Window plumbing today.** `WglContext.TryCreate` (`WglContext.cs:37-105`):
hidden `STATIC` window, `WS_OVERLAPPED`, **1×1** (`:48-50`), pixel format
**already double-buffered** (`PfdDoubleBuffer` `:18`, set `:68`), one legacy
`wglCreateContext` made current on the calling thread (`:84-100`), no vsync
call anywhere, `GetProcAddress` with fallback (`:108-118`). `internal sealed`
(`:13`) — reached only through `GlBackendFactory`, which is public. **There is
no message pump anywhere in `XGrid.GpuGl`** (no `PeekMessage`/`GetMessage`/
`DispatchMessage` in `src/`), and the boot loop just runs batches
(`Program.cs:602` `RunBatch`). `XGrid.GpuGl` has exactly one NuGet package:
`Silk.NET.OpenGL` (`XGrid.GpuGl.csproj:15`); only `XGrid.Cli` references it.

**Consequence for the decision:** the *rendering* side is already
double-buffered and window-backed; only show/resize/present/pump are missing.
The FBO is the source of truth at flip; `_readback` is a byte sink consumed once
per run by the golden harness.

---

## 3. How xemu presents (References2/xemu-0.8.136 — facts, file:line)

**xemu's present is completely decoupled from guest flips.**

- `NV097_FLIP_INCREMENT_WRITE` only bumps the surface write index and
  `pg->frame_time++` (`pgraph/pgraph.c:882-899`); `FLIP_STALL` sets
  `waiting_for_flip` (`:901-908`). **Neither presents.** The scanout source is
  `NV_PCRTC_START + line_offset`, not the flip method
  (`pgraph/gl/display.c:380, 417-418`; `pcrtc.c:65-68`).
- Present pull chain: QEMU UI → `nv2a_get_framebuffer_surface()`
  (`pgraph/pgraph.c:357-367`) → `pgraph_gl_get_framebuffer_surface`
  (`gl/display.c:406-440`): sets `sync_pending`, `pfifo_kick` (`:435`), waits on
  `sync_complete` (`:437`), returns the `gl_display_buffer` **texture id** (`:439`).
  The PGRAPH worker services it (`gl/renderer.c:119-149`, sync at `:137-138`).
- `pgraph_gl_sync` (`gl/display.c:375-404`): upload surface if needed (`:389`),
  fence (`:366-373`), switch to the **display context** (`:394`), then
  `render_display` (`:279-364`) composites the scanout surface texture through a
  fullscreen-triangle display shader (PVIDEO overlay inline, `:44-85`) into the
  offscreen `disp_rndr.fbo` (`gl_display_buffer`). Fence again, switch back
  (`:396-400`).
- **Window + swapchain = SDL, separate context:** UI main loop
  `poll_events(); gl_render_frame();` forever (`ui/xemu.c:1368-1371`);
  `gl_render_frame` (`:803-871`) does `SDL_GL_MakeCurrent(win, winctx)` (`:810`),
  pulls the texture (`:825`, VGA-fallback upload path `:829-837`), HUD, `glFinish`
  (`:854`), then **`SDL_GL_SwapWindow` (`:863`)** — with an explicit comment that
  the main-loop lock is released *before* the swap "to avoid possible lengthy
  blocking (for vsync)" (`:844-848`). Re-render on expose/resize (`:873-883`).
  Window created `SDL_WINDOW_OPENGL|RESIZABLE|HIGH_PIXEL_DENSITY` (`:1024-1027`)
  with its own context (`:1044`).
- **vsync:** config `display.window.vsync`, **default `true`**
  (`config_spec.yml:257-259`) → `SDL_GL_SetSwapInterval(vsync ? 1 : 0)` at init
  (`ui/xemu.c:1095`) and live-toggle on config change (`ui/xui/main.cc:330-332`).
- **Present cadence = host timer, not guest:** a dedicated thread paces
  `vblank_interval_ns = 16666666` (60 Hz, `ui/xemu.c:73, 750-777`) →
  `process_vblank` → `graphic_hw_update` (`:724-739`); the render loop then
  swaps as fast as it spins (vsync-capped when on).
- **Offscreen contexts are hidden windows, exactly like ours:** `gloffscreen/sdl.c:60-70`
  creates a `SDL_WINDOW_HIDDEN` 640×480 GL 4.0 core window per context (render +
  display contexts, `:52-57` share-with-current).
- **Vulkan in xemu is a renderer, not a swapchain:** `pgraph_vk_get_framebuffer_surface`
  returns a **GL texture id** under `HAVE_EXTERNAL_MEMORY`
  (`pgraph/vk/renderer.c:193-199`) into the *same* SDL-GL swap path; both
  renderers register the same `get_framebuffer_surface` op
  (`pgraph/pgraph.h:133`, `gl/renderer.c:209`, `vk/renderer.c:231`).

**Design deltas we must not import blindly:** xemu presents on *scanout/PCRTC +
host vblank timer* with its own locking/HUD/threads; XGrid presents on *guest
`0x12C` flip* synchronously with guest pacing already handled by
PIT + flip-stall + vblank-advance (`design §5:362-371`, `§67`). Our flip-driven
present is the simpler and already-gated shape — keep it.

---

## 4. Options

### O1 — Stay headless; BMP/screenshot export (status quo)
- **Mechanism:** unchanged: `--frame` once per run; `--fb` raw dump; `--pgraph`
  logs. Cheap increment available inside O1: **`--frame-every N` / periodic BMP
  dump** (reuses `SavePresentedFrame`, `Program.cs:1131`) to get mid-run visuals
  without any window.
- **Unlocks:** golden gates, offline diffing, determinism evidence (SHA
  records). **Not** live viewing; last-frame-only at run end.
- **Risks:** none (0 code). **Effort:** 0 (O1b: one CLI flag ≈ half a task).
- **Dependency policy:** clean.

### O2 — Hidden-window WGL + visible present via pure platform interop (no packages)
- **Mechanism:** reuse the existing hidden window + HGLRC (already double-
  buffered, `WglContext.cs:18,68`): show + size it (`ShowWindow`/`SetWindowPos`
  / `SetForegroundWindow` P/Invoke; restyle `WS_OVERLAPPEDWINDOW` via
  `SetWindowLongPtr` — pixel format can only be set once, `WglContext.cs:76`, so
  restyle-not-recreate), set `wglSwapIntervalEXT(0)` via the existing
  `GetProcAddress` (`WglContext.cs:108`), and at the end of `FlipCore`
  (after `_readback` is filled, `GlPgraphBackend.cs:658`) blit FBO → default
  framebuffer (NEAREST full-rect, the `UploadFullFrame` convention
  `:663-670`) + `SwapBuffers(dc)`. A `PeekMessage` pump between batches
  (`Program.cs:602`) and at flip. Opt-in CLI flag (e.g. `--window [--window-vsync]`),
  default **off**.
- **Unlocks:** live frames during a 5–6 minute run (menu 1B ≈ 342 s, §122);
  immediate visual feedback for device/GPU work; window-title counters
  (fps/flips/`BackendUnsupported`); foundation for later input focus. **Does
  not** unlock real-time interactivity (see §5 math) and no input handling
  (XInput pads already flow through XID/`PadScript`, not the window).
- **Risks / mitigations:**
  1. **No message pump** today → a visible window hits Windows'
     ~5 s "Not responding" ghosting. Mitigation: pump between batches + at flip;
     flag-off ⇒ no pump ⇒ byte-identical.
  2. **vsync must default OFF**: SwapBuffers on the emulation thread with vsync
     on blocks up to 16.7 ms *per flip* and serializes present with emulation
     (no-async survivor). Projection from §122: 686 menu flips × 16.7 ms /
     342 s ≈ **3.4% wall** (projection, not measured). xemu dodges the same
     hazard by swapping outside its lock (`ui/xemu.c:844-848`) — we can't defer
     the swap off-thread, so we don't enable vsync.
  3. **Golden gates:** present is a pure *sink after* readback — `_readback`,
     `TryCopyPresentedFrame`, and `--frame` bytes unchanged → `scripts/gpu-slice*-golden.ps1`
     re-run is the acceptance, unchanged policy (`design §6:388-408`,
     `scripts/gpu-slice1-golden.ps1:49-145`).
  4. **Headless/session-0:** window show may fail or be invisible → degrade
     exactly like probe failure (print why, continue headless;
     `GlBackendFactory.cs:33-37` precedent). Non-Windows already refuses
     (`WglContext.cs:41-45`).
  5. **Scope/API:** `WglContext` is `internal` (`:13`) → a small public factory
     parameter; `XGrid.GpuGl` stays referenced by `XGrid.Cli` only.
- **Dependency policy:** **clean** — user32/gdi32/opengl32 P/Invoke only; the
  single-dependency rule (`sources.md:151-156`) untouched.
- **Effort:** **1 wave-task** (one build agent, worktree): ~400–600 LOC incl.
  tests (WGL-guarded plain `[Fact]` per the §129/`sources.md` rule,
  `§129:6893-6898`) + both 1B goldens + suite + format. Optional polish
  (letterbox/resize/DPI/title counters) = a second half-task.

### O3 — Add Silk.NET.Windowing (or SDL) for a real swapchain
- **Mechanism:** package-managed window, event loop, input, vsync, fullscreen,
  DPI; replaces the hand-rolled pump.
- **Unlocks:** host keyboard/mouse input, resize/fullscreen/HiDPI, multi-platform
  windowing (Linux/macOS GL), an event model future input work wants.
- **Risks:** (i) **violates the standing single-dependency rule** — needs a new
  operator approval + `sources.md` entry + central pin in
  `Directory.Packages.props`; precedent: wave-10's `Xunit.SkippableFact` was
  rejected by BOSS for exactly this (`§130:6933`); (ii) transitive *native*
  windowing assets (GLFW/SDL) land in the build and on all three CI OSes
  (`.github/workflows/ci.yml:19`), a new failure surface; (iii) foreign event
  loop vs our batch loop (`Program.cs:545-602`) needs adaptation; (iv) goldens
  must be re-proven on the new present plumbing (same §3.4 policy).
- **Dependency policy:** **violates** as-is; clean only after operator approval.
- **Effort:** 1–2 wave-tasks + approval/pin/`sources.md` work.

### O4 — Vulkan present path (explicit future non-goal)
- **Status:** non-goal per `§132:7202-7204`, `§128:6850`, design risk 2
  (GL-first, `:444-452`). Also note two distinct things called "swapchain":
  `ImplementationPlan.md` §16.6 (`:1157-1174`) lists a *Vulkan renderer*
  swapchain (with GL demoted to "optional after Vulkan") — that ordering was
  superseded by the operator's GL-first choice (`sources.md:151-156`,
  design `:444-452`). Neither is this windowing question.
- **Fact that collapses it further:** even xemu's Vulkan renderer presents
  through a **GL texture → SDL GL swap** (`vk/renderer.c:193-199`), so
  "Vulkan" buys renderer backend, not a Vulkan swapchain.
- **Effort/risk:** a renderer-scale track; irrelevant to present latency (§122).

---

## 5. Measured-fact tie-in

| Fact | Value | Source | Consequence for the decision |
|---|---|---|---|
| GL submission + readback share of wall | **0–2.9%** (menu 2.9%, triangle 0.2%, 007 ~0%) | §122 table `:6477-6487`, verdict `:6489-6500` | Swapchain cannot be a speedup; it is latency/UX only. |
| Emulation share of wall | 97–100% | `§122:6491` | Amdahl: present work is unmeasurable vs CPU. |
| Per-flip readback cost | 1131 ms / 686 flips ≈ **1.65 ms/flip** | §122 table | A present blit + non-blocking SwapBuffers is the same order; vsync-on would not be (16.7 ms). |
| JIT throughput (fetch-fast) | triangle **4.63**, menu **3.50 Msteps/s** (1.35×/1.30×) | §133 `:7219-7221` (§131) | Best current rate. |
| Menu flip density | 686 flips / 1B steps ≈ 1.46 M steps/flip → **≈2.4 presents/s wall** at 3.50 Msteps/s (derived) | §122 `:6479` + §131 | A window would show ~2 fps *today* — live progress, not playback. Honest framing for the operator. |
| Real-time bar | guest ≈ 733 MHz class → order **10²–10³ Msteps/s** (estimate) | — | Gap = 1–2 orders of magnitude: **Track C JIT remains the interactivity lever** (§122 `:6494-6495`). |
| vsync-on projection | 686 × 16.7 ms / 342 s ≈ **3.4% wall** (projection) | §122 `:6479` | Keep `SwapInterval 0`; guest pacing is PIT/flip-stall, never host vsync (`§67`, design `§5:362-371`). |
| Golden gate contract | sw↔gl `frames/stalls/draws` exact, `BackendUnsupported=0`, BMP §3.4 policy, SHAs recorded | `scripts/gpu-slice1-golden.ps1:49-145`, design `:388-408, 521-540` | Any present change must leave `--frame` bytes and counters untouched; goldens re-run = acceptance. |
| Survivors | never mix, no async, headless-FBO (until operator decides) | `§129:6927-6929`, `§132:7141` | O2 satisfies all three (sink-after-readback, emulation-thread, opt-in). |
| Dirty-bit (a*) not yet implemented | design filed, code pending | `§132:7189-7204`, AGENTS item 4 | Sequencing constraint: both changes touch `FlipCore`. |

---

## 6. Recommendation, decision matrix, triggers

### Decision matrix

| | O1 headless (+O1b periodic BMP) | O2 interop window | O3 windowing package | O4 Vulkan present |
|---|---|---|---|---|
| Live frames on screen | no (mid-run BMP only) | **yes** | yes | yes |
| Frame pacing / vsync control | no | yes (off by default) | yes | yes |
| Host input / resize / fullscreen | no | no (output-only) | **yes** | yes |
| Debugging value | medium (files) | **high** (live + files) | high | high |
| Dependency policy | clean | **clean** | violates (operator approval + pin) | violates |
| Golden-gate risk | none | **low** (sink after readback; goldens re-run) | medium (plumbing rewrite) | high |
| Determinism / survivors risk | none | **low** (flag-off byte-identical; sink only) | low–medium | high |
| Headless/CI risk | none | **low** (flag-off; probe-fail degrade; WGL-guard tests) | medium (native assets, 3 OSes) | high |
| Effort (wave-tasks) | 0 (½ for O1b) | **1** | 1–2 + approval | renderer-scale |
| Interactivity bottleneck solved? | no | no (≈2 fps live) | no | no |

### Recommendation (operator decides; this equips it)

1. **Answer the pending question with "yes to a window, no to a package":**
   adopt **O2 as the target state, O1 as the permanent default and gate
   currency.** Goldens and CI keep running exactly as today (`--frame` BMP,
   flag off); the window is an opt-in observation sink, mirroring the
   `--idle-skip` / `--fetch-fast` pattern (flag-off byte-identical,
   `§128:6850`).
2. **Do not build it yet — sequence it:** (a) `§132` dirty-bit (a*) lands first
   (it owns the `FlipCore` serve/upload decision; window present is a *separate,
   later concern* bolted after readback); (b) prefer it *after* the §131 leg-d
   decode-cache slice, since wall-clock present rate scales with JIT (§5 math) —
   a window at 1.7–3.5 Msteps/s shows ~1–2 fps and would underwhelm.
3. **Keep O3 deferred and conditional:** revisit only when a gate genuinely needs
   host input (keyboard/mouse) or multi-platform windowing — i.e. the Track C
   "interactive session" milestone — and then only with a fresh operator
   approval + `sources.md` entry (SkippableFact precedent `§130:6933`).
4. **Keep O4 a non-goal**, and record the fact that even xemu's Vulkan renderer
   presents via GL/SDL (`vk/renderer.c:193-199`), so "Vulkan present" is not a
   coherent future need on the current architecture.

### Trigger points

- **T0 (now):** operator records the decision (AGENTS item 4 + `§132` non-goal
  line retire). **No code.**
- **T1 (implement O2):** first wave after **both** (i) `§132` (a*) dirty-bit is
  merged and (ii) a free build slot exists → **1 wave-task**; acceptance = both
  1B goldens byte-unchanged, suite + format green, flag-off logs byte-identical
  to flag-off today, flag-on run shows live frames + `SwapBuffers` counter,
  WGL-guarded test pattern, no new NuGet.
- **T2 (reassess O3):** when a title/device wave makes **host input** an
  acceptance criterion (keyboard/mouse beyond XID pads), or O2 hits a concrete
  interop wall (fullscreen/DPI/multi-platform). Requires operator package
  approval first.
- **T3 (vsync/window pacing):** only if the operator wants cosmetic frame
  pacing of the *display* (never the guest) — implement pump-side, default off,
  measured against §122 wall (expect ≤3.4% on menu, projection).
- **T4 (O4):** revisit only with a scoped Vulkan *renderer* track.

---

## 7. Not verified / limits

- **vsync-on 3.4% wall** and **~2.4 presents/s** are *derived projections* from
  §122 flip counts + §131 rates, not measured runs (a window does not exist yet).
- The ~5 s Windows "Not responding" ghosting threshold and DWM behavior for a
  pumped-vs-unpumped OpenGL window are **general platform knowledge**, not
  verified on this host — T1 must confirm the pump cadence empirically.
- Whether `ShowWindow`/`SwapBuffers` behaves on a **headless/session-0** agent
  is untested here (CI `ci.yml` runs build+test only, no GL/golden scripts;
  design `:521` already documents the CI-GL limitation honestly).
- xemu's `SDL_GL_SetSwapInterval` blocking magnitude and its HUD-swap lock
  behavior were read from source, **not measured** (no xemu run in this task).
- xemu present chain was read from **0.8.136 only**; the Playtest fork's tuned
  `pgraph/` was not re-diffed for present-path changes (its scope is
  perf-track: cost/seed/writeback/probe, per `docs/research-notes/xemu-playtest-v2.md`).
- No claim is made about *when* Track C reaches a rate where a window shows
  near-real-time frames; only the current gap (§5) is stated.
