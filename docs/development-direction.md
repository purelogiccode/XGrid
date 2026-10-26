# XGrid — Development Direction (canonical, revised 2026-10-24)

Direct development. The wave runner is retired (`tools/wave/` to be deleted).
No BOSS protocol, no worktrees, no detached servers. `AGENTS.md` stays the
day-to-day agent entry point; this file is the priority order every agent follows.

## 0. Course correction (2026-10-24, second): a playable emulator, not a wholesale port

**Operator decision.** The goal is a **playable Xbox emulator**: at least three
retail titles reach gameplay with input, audio and save/load. The previous
order (M-G -> M-P -> M-D) is replaced because it committed 40-60 sessions to a
136k-LOC xemu port before measuring how broad the gap was. The new order is
**M-T: title matrix -> blocker probe -> fix the shared blocker -> playability
gate -> grow the matrix**.

**The port rule.** xemu 0.8.139 is the oracle and the source for specific
modules, not the base to rewrite. Port a module only when the title matrix
shows the same xemu source file blocks **>=3 titles or >=20% of the matrix**,
and keep the old path as the fallback until the differential is exact. Never
port `tcg/` or `softmmu/` unless the matrix proves the CPU is the blocker -
today it is not (ktest 2B is bit-exact and faster than xemu).

**Why.** The current engine already boots the real firmware to the dashboard,
runs 007's XBE, enumerates USB, streams DVD/HDD and executes the guest DSP
microcode. The gap is device behaviour (the APU VP mixer, a handful of NV2A
PGRAPH behaviours, timing), not the QEMU core. A wholesale rewrite produces no
user-visible product until late; the matrix loop produces a playable title
every few sessions.

**Media.** `H:\Tests\XBOXReady` has the two optimized ISOs (007 1.60 GB, Halo
3.38 GB) and is oracle-grade. `H:\Tests\XBOXTest` has 37 full-disc Redump ISOs
that xemu cannot boot; they are **XGrid-only compatibility media** that may
prove a blocker but are never oracle rows or gates. `data/test-samples/` stays
gate currency.

**Exit (M-T):** three retail titles playable by the seven-part definition in
`ImplementationPlan.md` E1 (own render, scripted input, 10 minutes, save/load,
xemu frame match, audible output, gates exact). Speed is reported, never
claimed, until then.

## 0a. Previous correction (2026-10-22): xemu-class performance is the long-run objective

<!-- STATUS:BEGIN (generated from docs/status.json by scripts/status.ps1; do not edit) -->
**Current status (2026-10-25):** M-T / **TM9 - Ninja Gaiden Black colour depth: stop the fragment-path dither the TM7 row measured** (active, attempt 0).
**Gameplay (M-T):** H:\Tests\XBOXReady (7 optimized ISOs; 007 1.60 GB, Halo 3.38 GB, 50 Cent, Bad Boys II, Destroy All Humans!, Tak, Ninja Gaiden Black) - TM8 (2026-10-26): the APU is AUDIBLE - the xemu VP mixer (vp.c) is ported and 007 writes real PCM. --apu-dsp --audio-out went from 877,568 bytes of all-zero PCM (TM4) to 377,856 bytes / 94,464 frames with 42.4% non-zero samples (peak 7,500, RMS 1,023) from boot at 2B steps, and 47.6% non-zero from the 26B anchor (wt-data/tm8/007-audio-boot-2b.wav, 007-audio-26b-2b.wav); the envelope, the stereo difference and a DFT (105.5/152.3/199.2/316.4/503.9 Hz) say it is audio, not noise. The port is vp.c's frame walk, both envelopes, PCM/ADPCM buffer+stream fetch through the SSL/SGE tables, resampling, the volume/headroom mix into the 32 mix bins and multipass, plus svf.h/hrtf.h/adpcm.h; the frame cadence was 8x slow and is fixed (one XGrid frame = eight 32-sample reference frames). APU snapshot v6; old .xgs still load and now bootstrap the DSP from the restored scratch window. TM6 (2026-10-25): the second-tier five were probed at 10B each and the shared blocker was our own NV2A - NV097_INLINE_ARRAY was a screen-space shortcut (float-only attributes, the vertex program never run), so Ninja Gaiden Black's 28-byte D3D FVF vertices (F32x4 position + UB_D3D colour + F32x2 texcoord) were skipped: 1,073,213 skipped draws at 10B and a black display. The inline array is now laid out and decoded like the reference (BuildInlineArrayLayout, FetchInlineArrayAttribute through the shared DecodeVertexAttribute, VertexFetchMode.InlineArray) with the legacy path kept as the fallback, and the NV2A line gained skipReasons= (eight counters). Ninja Gaiden Black now renders its own animated stormy-sea intro - 720x480, 13,789 colours, 0.5% black, 4,252 draws / 187,381,609 px / 238,138 frames at 10B+2B, LastFault vec=0, kernel idle 0x8001B02E between frames, a held A changes the scene - and at 1B the matrix row went 2,672 -> 12,800 draws / 41,995,882 -> 166,119,138 px at the same 6,475 frames. 50 Cent renders its own 3D scene (18,638 colours, 3,534 draws / 299,139,810 px, EIP 0x1455BA, PIC req=0x08); Tak renders its own scene and then a ~910-draw/frame one where the software rasterizer is the wall (300M steps = 362 s, 347 s in 3,228,625 fills, 1.27e9 z-clipped fragments); Bad Boys II is a documented wait (own-code resource-handle scan 0x1A3C80 -> 0x1A2250, handles 0x300C5+, no device traffic, black display); Destroy All Humans! renders its own Pandemic logo card and then spins in a kernel stall loop (KeStallExecutionProcessor 0x800153D0, 25% of samples, 6,819 draws / 86,802,891 px). The xemu oracle could not be captured this session (another agent owns the desktop: PrintWindow returns black, the screen copy catches the editor), so no xemu frame is claimed for TM6. TM5 (2026-10-25): Halo launches and renders its own main menu - 35B steps, 516 frames / 3,247 draws / 191,496,512 px, 97,235 colours, 0% black (wt-data/tm5/halo-35b-display.png), 100B steps with LastFault vec=0. The blocker was a per-title constant: McpxApuDevice absorbed the DirectSound mixer block at 007's fixed 0x03924810, so Halo's block at 0x02034810 was never consumed and the guest spun at EIP 0x195EC0 from ~1.5B; the block is now discovered from the guest's own GP scratch (SGE) table (page + 0x810). Gameplay is a DOCUMENTED WAIT: a guest-side D3D resource retry loop (EIP 0x1810F0 from 0x6348B, D3DERR_NOTFOUND for handle 0, 99.99% of samples) after 100B clean steps; xemu 0.8.139 on the same ISO shows the same menu at 70 s and in-game gameplay at 150 s. The menu frame is the same scene but dmean=(101.8, 100.6, 113.3), so the screenshot policy is not met. Audio is silent (--audio-out writes 0 PCM frames, gpFrames=0). 007 Agent Under Fire remains the playable title (TM4): 100,000,000,000 steps with LastFault vec=0 (134,004 draws / 1,005,989,482 px / 6,478 frames, EIP 0x214C2), presenting the first-level Z8-on-the-docks scene (48,208 colours, 6.8% black, mean (78.7,65.5,45.3)); the xemu oracle on the same ISO reaches the same scene at 150 s (dmean (18.0,22.6,14.6), black 4.1% vs 4.2%). The 26.5B #PF was our own duplicate SE2FE_IDLE_VOICE trap; the queue is gone (APU snapshot v5). Scripted input from the 100B snapshot changes state (distinct RAM SHAs per pad) and a held A skips a cutscene. The 44-row 1B matrix is now 6 own-render / 38 dashboard / 0 fault / 0 timeout (was 2/42): the same fix advances Ninja Gaiden Black (35 -> 6,475 frames), Tak (1,068 frames / 3,701 draws), 50 Cent (98 frames / 100,987,370 px), Bad Boys II (42,303,082 px) and Destroy All Humans!, with the pre-fix build reproducing the old NGB row exactly. TM7 (2026-10-25): the seven-title table is published with one command per row (scripts/retail-game-probe.ps1) and a live xemu 0.8.139 oracle for six of them - 007 still passes (dmean 18.0/22.6/14.6), Halo's menu still fails on brightness (101.6/100.4/113.1), and Ninja Gaiden Black matches xemu's intro within 11/255 on the means while failing on colour count (12,913 vs 1,351) - the dither pattern is now the next named rendering defect; Bad Boys II's black display is quantified against xemu's own cutscene (100% vs 20.5% black). Oracle: data/xemu/xemu.exe (xemu 0.8.139), same optimized ISO + firmware/EEPROM; scripts/xemu-oracle.ps1 selects the disc with -config_path and captures a DPI-correct client area. TM7 (2026-10-25): the oracle is live again - six of the seven rows carry a real capture (007 150 s, Halo 70 s, Ninja Gaiden Black 52 s, 50 Cent 90 s, Tak 150 s, Bad Boys II 90 s, Destroy All Humans! 90 s). The script now retries PrintWindow five times and uses the screen-copy fallback ONLY when xemu is the foreground window, otherwise it throws 'capture contended' - TM7 caught a browser frame that way (PrintWindow answered black while the GL surface was starting, and the screen copy photographed Chrome). A blank capture (<=2 colours or >=99% black) is rejected by the harness. xemu still cannot be driven on this host (pad input is SDL-gamepad only), so it sits on title screens for Tak, Destroy All Humans! and Ninja Gaiden Black@70 s.
**Perf reference (M-D, deferred; H1, 1B, fast):** triangle gl+replace 6.40 s / sw 11.10 s; menu gl+replace 17.63 s / sw 27.40 s; gap ~4.9x / ~11.7x.
**Phase score:** 79.65% (93.99 of 118). **Next:** .
**Do not retry without a new measurement:** N1 C4 host-pointer translate probe, N2 Read-only-ROM decode caching alone, N3 Full trace/superblock formation, N4 Own the encoder (Assembler rewrite), N5 Remove the raw emitter's per-member EIP/TSC/served, N6 VshProgram.Execute allocations / scratch reuse. See docs/status.json for all 20.
<!-- STATUS:END -->

> The generated block above is the current status; the narrative below is
> history. The execution plan and the task queue are in `ImplementationPlan.md`
> -> "Execution plan (current)".

**2026-10-23 operator decision: GPL-2.0, port xemu/QEMU, full C#.** XGrid is
now licensed under **GPL-2.0** (`COPYING`) and may port code from xemu, QEMU and
the other GPL-compatible references, preserving every notice. The project stays
**full managed C#** - no QEMU/xemu native library and no C/C++ emulator code in
the build. `References2/xemu-0.8.139` is the primary source. The first port, the
**APU GP/EP DSP** (`hw/xbox/mcpx/apu/dsp/`), is **landed** (P1.2-P1.4: the
interpreter, the GP/EP frame glue and a 48 kHz PCM sink); the VP mixer
(`apu/vp/`) and the AC'97 codec path are what make it audible next. The CPU TCG
is **not** a port target: the raw emitter is already bit-exact and faster than
xemu on the CPU-bound ktest; the gap is the boot/device/GPU path. The working
xemu 0.8.139 menu oracle is `docs/research-notes/xemu-menu-oracle.md` (the old
"xemu deadlocks" note was specific to the 0.8.136/0.8.137 media). Binding policy:
`docs/porting-policy.md`; provenance: `THIRD_PARTY_NOTICES.md`.

**Status 2026-10-22 very late (39th session, §§239-240): the device stage was
measured for the first time and one half of it landed.** A temporary probe priced
the triangle 1B `gl+replace` leg as **1.31 s of 6.1 s inside 44,030 device
writes** - 766 ms in the 2,706 draw flushes (interpreted vertex program 413-435
ms, GL submission 173-181 ms, setup 61-66, attribute fetch 34-36, epilogue ~70)
and ~540 ms in the 6,455 surface clears. (1) §240: a 32-bit clear now fills rows
through `Span<uint>.Fill` instead of one `WriteGpu` per pixel - device time
**1,306 -> 816 ms**, **triangle `gl+replace` 6.31/6.43/6.34 -> 5.61/5.74/5.73 s
(-10.6%, 3/3)**, menu 22.43/20.76 -> 19.98/18.82 s (-10.6%/-9.4%). (2) §239: the
raw emitter's EIP/TSC/served became static per edge (11 emitted instructions per
member removed) - **-3% engine, 0% wall**, which is the session's decisive
negative: the raw runner is **latency-bound, not instruction-bound**, so C's next
lever must remove *edges* (the chain edge is ~60 host instructions per 4-member
loop iteration), not instructions. Every 1B anchor, both accurate goldens, both
GL frames and the ktest log are byte-identical. **The largest unowned CPU-side
item left is the interpreted vertex program inside the draw flush (413-435 ms,
next-session.md §2 Option A).**

**Status 2026-10-22 late (38th session, §237 + §238): two exact Track C slices,
-8.9% / -3.4% on the headline legs.** (1) The boot ROM is a cached, blocked image
window: instruction fetches from flash/BIOS/mirror/overlay windows are a
decode-cache class of their own with no store path (wholesale flush on the host
actions that move an image), and the JIT forms blocks over them -
`nonRamSkips` 359,476 -> 0, one-instruction non-JIT runs 434k -> 136k. (2) The
non-JIT run slice `JitSpecSlice` went 256 -> 64 after its first-ever sweep
(`nonJitInstructions` 5.48M -> 2.62M; `nonJitMs` keeps falling to slice 4 but
formation eats it). Measured together: **triangle `gl+replace` 6.672 -> 6.081 s
(-8.9%)**, menu `gl+replace` 20.456 -> 19.762 s (-3.4%), 100M boot phase
2.276 -> 1.813 s (-20.3%), every 1B SHA/counter/frame and every corpus row
byte-identical. §237 also **reprized the "superblock for the boot" lever out**:
the ROM share of the non-JIT population is 9%, not the 100% §2 assumed, so its
ceiling was 6.4%; the remaining boot mass is 55k RAM heads below the build
threshold (an encoder price, §234e). Track B stays closed (gap 12 closed in §236).

**Status 2026-10-22 late (36th session, §236): compatibility gap 12 is CLOSED -
the GL backend decodes a cube stage's six swizzled faces into a host 2D-array
texture (`sampler2DArray`), so `xgu bump_cubemap_reflect`'s reflection panel
renders on the GPU and agrees with software within 2 pixels of 307,200 at both of
its anchors. The word pool now carries only a HILO dot input's raw pair, and only
when the stage's dot map is 4-7. Track B's bounded item is done: B stops here
until C has a measured result.** Track order unchanged; the next C work is the
engine-shape decision (§0 kill/reassess, `next-session.md`).

**Operator decision.** The long-run objective is **xemu-class performance or
better** on the same content. The 2026-10-20 correction was right to put the
product surface and the corpus ahead of synthetic optimization, and that work
landed: the GL present path is ~1.3 s of the menu wall (was ~9.9 s), the corpus
is 22 rows over 17 samples, and the frontend renders on the host GPU. But it
froze Track C, and the measured position now fails the freeze's own re-open
condition: **the CPU engine is ~17 s of the 24.3 s menu `gl+replace` wall
(~70%) and the dominant part of the 7.5-7.9 s triangle wall**, so the engine is
again >50% of a measured wall. Performance is no longer the last engineering
track.

**The measured gap (1B, H1, gate media).**

| workload | xemu / real-time band | XGrid now | gap |
|---|---:|---:|---:|
| triangle `gl+replace` | 1.20 s (xemu 830 Msteps/s) | 5.72-6.15 s | ~4.9× |
| menu `gl+replace` | 1.36 s (real-time band; xemu has no menu oracle) | 15.6-17.5 s | ~12× |
| ktest 2B (CPU-bound) | 15 s (xemu) | 9-10 s | XGrid already ahead |

The triangle's wall is ~5.8 ns per guest step vs xemu's ~1.2 ns; the engine's
served-member cost is 6.26 ns (triangle) and 14.77 ns (menu, measured 2026-10-23
in §242), and the device stage after D1 is ~596 ms of the triangle wall (§240 +
§241), so the menu's gap is the helper/device mix plus its 2.3x member count. A
2-3% slice cannot close either; the program is structural.

**New order.**

1. **C — performance (lead, re-opened).** C4 flat memory model / soft TLB,
   C5 trace/superblock formation, C6 device/MMIO fast path. Each stage is
   gated on a same-window A/B (≥10% wall or ≥20% engine), exact 1B
   SHAs/counters on both titles, flag-off byte-identical, ktest log
   byte-identical. The old C3-x/C3-y/C3-z list is closed; this is a new
   program, not a resumption of 2-3% slices. **C6 has paid twice (§240 the clear
   fill, -10.6% on both headline legs; §241/D1 the transform program plan,
   -24.8/-30.4/-32.8% on the menu) and the rest of the device stage is priced
   (~596 ms of the triangle wall, >=2.6 s of the menu's transform stage alone);
   C4 measured null (§232a) and §239 proved the runner is latency- not
   instruction-bound.**
   **C7 was decided (2026-10-23, §242):** option (a) — keep the raw engine and
   take the device/draw-flush path. That path paid twice more (D1 §241, D5
   §244: the combined draw-flush slice -10.3% menu, 6/6 pairs), and the queue is
   now **D8 → D6 → D7** (ImplementationPlan E1/E5b, `docs/status.json`):
   **D8** re-measures the per-member/edge and boot attribution with a hard
   target (steady-state ≤2 ns/member) and selects one option — including, now
   that the license point is settled, **porting the reference's engine/device
   shape** (QEMU/xemu TCG, xemu PGRAPH draw path); **D6** is the priced device
   remainder (compiled transform evaluator, 9.5-10.7% menu); **D7** attacks the
   boot (1.93 s triangle = 33% of that wall, 1.6× the whole 1.20 s target).
   The arithmetic in E1 is the reason: zeroing every non-runner stage still
   leaves the triangle's 3.43 s runner 2.9× off the band, so the engine decision
   must be reopened with data, not closed by the negative table.
2. **B — bounded finish of the in-flight B3 slice — DONE 2026-10-22 (§236).**
   The GL product renderer's computed-mode panel was black because a cube stage
   had no word source; it now samples a host 2D-array texture decoded from the
   guest's swizzled faces and agrees with software within 2 pixels of 307,200 at
   both of the row's anchors. **B stops here**; no new B fidelity rows while C
   leads.
3. **E — corpus steers.** Keep the matrix; add the performance rows
   (Msteps/s, wall per presented frame) and the xemu comparison where it is a
   valid oracle. No new corpus breadth beyond the standing rows while C leads.
   **G1-G3 (E5c)** are the optional wholesale NV2A/PGRAPH port: a compatibility
   milestone (73 recorded methods, DXT/mipmap/multistage formats, exact cache
   semantics), not the path to the band. **G1 landed** (the method audit + the
   texture-format table); **G2 closed negative (§251)** because the removable
   CPU vertex stage is 4.0-4.2% of the triangle wall and the reference design's
   per-draw submission costs 3x the menu's gross saving; **G3** is the
   consolidated re-baseline. The software rasterizer stays as the oracle; only
   GL frames may be re-baselined.
4. **P — product (parallel, not blocking).** The **APU DSP port is landed**
   (P1.2-P1.4: interpreter, GP/EP glue, 48 kHz PCM sink). The largest remaining
   product gap is audible output: the **VP mixer** and the AC'97 codec path.
   Run it parallel to C; do not let it displace C.
5. **D — only with a measured >2% ceiling.** Unchanged.
6. **Gates — unchanged and non-negotiable.** Accurate mode is currency; the
   performance program never relaxes the ceremony.

**Price every stage first.** Each stage starts with a 100M-step ablation that
prices its ceiling (the §199n pattern: elide the mechanism, measure the wall).
If the ceiling is <10% wall, stop and re-scope rather than land a polish pass.
**The draw-flush half landed (D1 + D5); the next stage is D8's measurement, not
another slice** - the trace was priced at 3-5% (§235) and the engine's remaining
levers are all below the bar on their own measurements.

**Kill/reassess.** If the engine program does not reach ≥3× engine improvement
after two structural iterations, stop and reassess the approach (memory model,
trace formation, or a ported reference core) rather than polishing. The
xemu-class target is the aim, not a promise; the measured stage results decide.
**Status 2026-10-23:** C4 null and C5 slice 1 -3/-4% met the first criterion; C6
then paid -10.6%, D1 -25/-31% menu and D5 -10.3%. The reassessment is **D8**: a
per-member/edge/boot attribution with a hard steady-state target and a written
decision, where "port the reference's shape" (GPL-2.0, full managed C#) is a
first-class option.

**Why not B first?** B3 is a correctness item for one homebrew row; C is
~70-90% of every product wall and the stated long-run objective. B3 is bounded
and stays in flight only because leaving the GL renderer visibly wrong is
worse than finishing it.

## 0b. Previous correction (2026-10-20) — retained as history

**Superseded by §0 (2026-10-22).** The 2026-10-20 rationale is kept because it
explains why the product/corpus work happened first.

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
- Reference-first: xemu 0.8.139 primary (0.8.137/0.8.136 prior); independent
  CPU oracle (`tools/cpu-oracle`, Unicorn/TCG); GPL-2.0 with ported-code notices.
- The device *set* is complete (MCPX, PCI, IDE/ATAPI, USB-OHCI + XID, SMC,
  SMBus, EEPROM, PIC/PIT/RTC, APU, nvnet, NV2A). Only depth remains.
- The CPU engine (raw emitter, chaining, fault contract) stays landed and
  opt-in. Do not spend more sessions on it until the tracks below clear.
  *Correctness* work is exempt from the freeze (it is not codegen): 220 landed
  nine interpreter/decoder fixes found by the independent oracle, with no
  throughput claim and no gate anchor moved.

**What changed (2026-10-20; historical — superseded by §0).**

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

**Steering rule (2026-10-20; the current rule is §0).** Every proposal states
which track it serves and the measured ceiling behind its acceptance. A task
that serves none of B/E/P is rejected even if it is interesting.

## 1. Priority stack (in order)

**Status 2026-10-24: the order is M-G -> M-P -> M-D (section 0).** The
performance stack below is retained as the M-D reference and is not the current
task order; the live queue is `docs/status.json` and the task detail is
`ImplementationPlan.md` E1a/E1b/E1c.

**Status 2026-10-22 late (§232): C4 measured NULL; the levers are now measured,
not assumed.** The host-pointer translate probe was implemented and priced on an
interleaved 1B A/B (menu `runnerMs` 15,872 → 16,026, triangle 3,407 → 3,382,
walls within noise, SHAs exact) and reverted. The triangle's 6.9 s is
`RunJitBlock` 4.53 s (native 3.42 + 1.1 bookkeeping) + `RunNonJit` 2.25 s, and
the non-JIT cost is *decode*: 359,476 boot-ROM fetches (`0xFFFFFFxx`, all inside
the first 100M steps) at ~1.5 µs each because the decode cache refuses non-RAM
pages — **priced 0.54 s/run, 7.8% of the triangle wall**. Landed: the unified
interrupt poll (C6 slice 1, architecture; wall delta below resolution) and a
correctness fix for a raw store that lost its translate slot when CR0.WP was
clear. **The plan at that point was C4b (read-only-ROM decode path), then C5
trace formation (measured against `nonJitMs`), then C6 - all three are now done;
see the 39th-session status below.** Price stages with stashed interleaved
A/B legs, never with env-gated ablations that change guest behavior (§232e).

**Status 2026-10-22 very late (39th session, §§237–240): the "next" list above
is done and the device stage is now the paying track.** C4b landed as the
image-window class (§237: `nonRamSkips` 359,476 → 0, `nonJitCalls` 523k → 169k,
triangle `gl+replace` -6.4%), the non-JIT run slice went 256 → 64 (§238:
`nonJitInstructions` 5.48M → 2.62M, `nonJitMs` 2190 → 1650, triangle -3.7%),
and the raw emitter's per-member EIP/TSC/served became one static publish per
edge (§239: engine 6.3 → 6.1 ns/member, wall flat — the runner is
**latency-bound, not instruction-bound**, so the CPU-side lever is *edge*
removal). The device stage was measured for the first time (1.31 s of the
triangle 1B `gl+replace` wall inside 44,030 device writes: 766 ms draw flushes,
~540 ms clears) and its clear half was won (§240: `Span<uint>.Fill`, device
time 1,306 → 816 ms, triangle -10.6% / menu -10.6%/-9.4%). **Next: the
draw-flush half** (766 ms, of which 413-435 ms is the interpreted vertex
program), then the **C7 engine-shape decision** — the encoder (~7%) and the
code-volume question (§239, 0% wall) are already priced out.

**Status 2026-10-23 (41st session, §§241–242): the draw-flush half landed and
C7 is decided — option (a), keep the raw engine and take the device path.**
D1 (§241) compiles the active transform program once per draw (`VshProgramPlan`):
the transform stage 422 → 202 ms triangle and 7,134 → 2,600 ms menu, menu wall
**-24.8/-30.4/-32.8%** (3/3) with every anchor byte-identical; the triangle wall
is -4.02% median (its wall is pacing-bound, not transform-bound). D2 (§242)
re-read every price on the post-D1 build — triangle 5.839 s (runner 58.7%,
6.26 ns/member), menu 16.448 s (runner 77.8%, 14.77 ns/member), anchors exact,
gap 4.87x/12.1x — and wrote the decision table: encoder 8.0%/4.9%, trace
boundary population 6.04%/4.35%, code volume 0% wall, C4 probe null, device
stage 10.2% triangle / ≥15.8% menu. **Only option (a) is above the bar; the
engine-shape program is closed, D3 (menu decomposition) is promoted and D4 (a
new engine shape) is conditional on a per-member probe ≥20% engine.** Price
stages with stashed interleaved A/B legs, never with env-gated ablations that
change guest behavior (§232e).

**Status 2026-10-22 (course correction): Track C is re-opened and leads; B3 is
a bounded finish; E steers; P runs parallel.** The engine is ~17 s of the
24.3 s menu `gl+replace` wall and dominant on the 7.5-7.9 s triangle wall; the
long-run target is xemu-class (triangle 1.2 s, menu 1.36 s at 1B). The
2026-10-20 status blocks below are retained as history.

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

1. **C — performance (lead, re-opened 2026-10-22).** C4 flat memory
   model/soft TLB, C5 trace/superblock formation, C6 device/MMIO fast path.
   Acceptance per stage: same-window A/B ≥10% wall or ≥20% engine, exact 1B
   SHAs/counters on both titles, flag-off byte-identical, ktest log
   byte-identical. Exit: the xemu-class band (triangle ≤1.2 s, menu ≤1.36 s at
   1B) or a measured reassessment after two structural iterations. The
   software rasterizer stays the exact-mode gate renderer and differential
   oracle; the GL path stays the fast-mode product renderer.
2. **B — bounded B3 finish — CLOSED 2026-10-22 (§236).** A cube stage samples a
   host 2D-array texture decoded from the guest's swizzled faces, so the GL
   product renderer's computed-mode panel renders and agrees with software within
   2 pixels of 307,200. No new B fidelity rows while C leads. The
   present path itself is closed (readback opt-in, batching, sampler parity,
   replace-mode composition).
3. **E — compatibility corpus.** `docs/compatibility.md` + the harness +
   performance rows (Msteps/s, wall per presented frame). Every row has a
   reproducible command and a step anchor; a fidelity landing cites the row it
   fixes and the histogram that proves usage. No new corpus breadth beyond the
   standing 22 rows / 17 samples while C leads.
4. **P — product completeness (parallel, not blocking).** Host audio output is
   the remaining large item; P1 is the **xemu 0.8.139 DSP port** (state/DMA in,
   interpreter + GP/EP glue + PCM sink next). Pad binding, save-state V1,
   per-title config, renderer choice, frame stepping and persistent EEPROM are
   landed. Do not let P displace C.
5. **D — cheap wins with a measured ceiling.** Page-end wait, bookkeeping.
6. **Gates — unchanged and non-negotiable.** Accurate mode is currency.

## 2. Measured position (1B fast-mode legs, reference host; 2026-10-20 table + 2026-10-22 very late current position)

1 guest-second ≈ 733M steps (§68), so 1B steps ≈ 1.364 guest-seconds.
**"xemu-class" means the same content runs at wall ≤ guest-time**, measured
against xemu where an oracle exists (triangle ≈830 Msteps/s; ktest smoke xemu
15 s vs XGrid fast 9–10 s) and against the real-time band otherwise (menu).

**Correction 2026-10-21 (§208d):** the triangle rows below were measured on a
DVD-only leg, which never launches the disc's XBE. On gate media (HDD+DVD) the
same legs cost **11.3 s sw / 7.5 s `gl+replace`** at 1B on H1 (2,706 draws /
68 frames / 57.7M px). Read the table as the boot-animation workload it actually
measured; M-A's triangle target needs re-basing before it is chased.

**Current position (2026-10-22 very late, after §§237–240):** the present path
is done and the CPU/device engine is the wall again. On H1 at 1B: menu
`gl+replace` **18.8-20.0 s** / `sw` 30.35 s (raster ~11.2 s), triangle
`gl+replace` **5.61-5.74 s** / `sw` 10.03 s. xemu-class is **1.20 s** on the
triangle (830 Msteps/s) and **1.36 s** on the menu (real-time band; xemu has no
menu oracle), so the gap is **~4.7x / ~14x** (was ~6.6x / ~17.8x before
§§237–240). ktest 2B is already ahead (9-10 s vs xemu 15 s). **M-D is the lead
milestone, not the last one.** The table below is the 2026-10-20 measurement
(pre-§237) and is retained as history.

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


Anchors (fast mode, H1, 2026-10-22): menu RAM `34973510…` / CPU `F5D61C94…`;
triangle RAM `B45254CA…` / CPU `7DAB63EB…`. Accurate mode: menu
`6AB39AB0…`/`3D2740B8…`, triangle `812EA159…`/`2F632483…`; frames
`4EF1FBF6…` / `A1740DE0…`. The full per-leg table is `next-session.md` §0;
1B walls are continuity anchors only.

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
`--gpu sw` ~30-33 s, triangle `--gpu sw` ~10-11.3 s at 1B on the *unmodified
baseline* — the old ~8.7 s triangle figure was the DVD-only boot animation
(§208d). M-A can only be recorded there; on any other host
`present-path-acceptance.ps1`'s SUMMARY is a ratio, not a milestone.

## 3. Milestone ladder

- **M-T (playable titles) - the lead milestone as of 2026-10-24:** three retail
  titles reach gameplay with input, audio and save/load. The loop is title
  matrix -> blocker probe -> fix the shared blocker -> playability gate. The
  seven-part definition is in `ImplementationPlan.md` E1; the first task is TM0
  (title matrix). Retail ISOs are acceptance, never merge gates.
  **Status 2026-10-25 (TM4, decisions §255): 1 of 3 titles playable.** 007 Agent
  Under Fire runs 100B steps with `LastFault vec=0`, presents its own first-level
  driving scene, takes scripted input, round-trips a save state through the
  frontend session and matches the xemu 0.8.139 frame at the same scene within
  the mean/colour/coverage policy; audio is a measured silence (the VP mixer and
  the AC'97 codec are TM8). TM0-TM4 are done, TM5 (Halo) is active, TM6/TM7
  (breadth + the published table) and TM8 (the first targeted port) follow.
- **Port rule:** xemu 0.8.139 is the oracle and the source for the specific
  modules the matrix proves are shared blockers (>=3 titles or >=20%). The CPU,
  memory, chipset, storage and USB stay; the APU VP mixer + AC'97 and the NV2A
  PGRAPH behaviours are the likely first ports. The software rasterizer and the
  existing seams stay.
- **M-A (Track B, present path):** menu fast+gl ≤ 24 s, triangle ≤ 6 s at 1B;
  `BackendUnsupported=0`; SHAs/counters exact; the Avalonia display is driven
  by presents, not a per-frame readback. **Status 2026-10-20 (§204):** the GL
  overhead (~7.9 s) is removed and measured (menu `gl+replace` -20.5% 3/3,
  triangle -0.9% 3/3 same-window; `drawMs` 12.55 → 2.1 s; readback 1.5 s → 0).
  **M-A itself was not decidable on the 2026-10-20 host:** the unmodified baseline build
  measures menu sw 62.6 s / triangle sw 14.0 s against the recorded 33.3 / 8.69
  with an idle CPU, so absolute walls are not comparable. Re-run
  `scripts/present-path-acceptance.ps1` on the reference host state (menu sw
  ~33 s) before recording M-A; the 39th-session status below records it on H1. If
  the menu is still over, per-triangle batching
  is the structural step. **The in-app GL display landed 2026-10-22 (§220):** the
  frontend's Renderer setting runs the GPU backend headless with the per-flip readback (-29% wall at 1B through the real session), opt-in and not the default. The in-app switch is +9.8%/+8.2% guest throughput on the dashboard with every gate anchor unchanged (§223).
  **Closed/re-based 2026-10-22:** the present path is done (readback opt-in,
  batching, sampler parity, replace-mode composition). **Status 2026-10-22 very
  late (§§237–240): the numeric targets are met on H1 on the corrected HDD+DVD
  media — triangle `gl+replace` 5.61-5.74 s (≤6 s) and menu 18.8-20.0 s (≤24 s),
  with `BackendUnsupported=0`, exact SHAs/counters and a present-driven display.
  M-A is MET; the lead is M-D.** The `sw` legs (10.03 s / 30.35 s) are not part
  of M-A.
- **Display model (Track E/P) - LANDED 2026-10-21 (§208).** The console now
  presents what the display controller scans out: the VGA CRTC behind
  `NV_PRMCIO` is decoded (pitch/size/depth), `TryCopyDisplayFrame` +
  `--frame-display` export it, and the Avalonia frontend samples it at ~30 Hz,
  so direct-framebuffer guests (`gamma`, `hello`) finally show their output.
  `--frame` still exports the last flipped surface, so no frame anchor moved.
  The same session corrected the triangle's media (§208d): every triangle leg
  that used DVD only was measuring the boot animation.
- **M-B (Track E, corpus) - MET 2026-10-20; now 22 rows over 17 samples
  (2026-10-22).** Every row reproducible: `scripts/build-nxdk-samples.ps1` builds the nxdk
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
- **Speed (after M-T):** xemu-class is the long-run objective, but it is
  reported, never claimed, until the playability gate is met. The 1B reference
  stays (triangle ≤ 1.2 s, menu ≤ 1.36 s); ktest 2B already beats xemu (9-10 s
  vs 15 s). The old per-slice 10% bar is not the rule for M-T.

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
MIT) is authorized and landed; it is the base of the C4-C6 program re-opened by
§0. Incremental sub-2% slices remain banned; every C4-C6 stage needs the §0
gate (same-window A/B ≥10% wall or ≥20% engine, exact 1B differentials).

**GPU / present.**

- Headless FBO (`--gpu sw`) is the gate currency. Never mix (full-frame replace
  or no frame), no async (emulation-thread only).
- The GL backend is the fast-mode product renderer (Track B). Readback is a
  gate/screenshot facility, not a per-frame cost. The present path is closed, and
  so is the bounded B3 item (gap 12, §236): a cube stage is a decoded host
  2D-array texture, and a HILO dot input is the only remaining word-pool holdout
  (one word per stage; no measured row uses one).
- Swapchain is latency, not throughput. O1 headless stays default; O2 opt-in
  `--window` (pure interop, vsync off) only on the brief's triggers; O3 needs an
  operator single-dependency override; O4 Vulkan is a non-goal (even xemu's
  Vulkan presents via GL/SDL).
- No NV2A fidelity polish beyond `docs/compatibility.md` + a sample histogram
  (Track E gates it). Exotics, DXT/BRDF/mipmaps, cost model, seeds, writeback
  stay gated on observed usage.

**Corpus.** `data/test-samples/` stays gate currency. The **optimized** retail
ISOs at `H:\Tests\XBOXReady` are the M-G acceptance and the xemu-oracle media
(never gates). The 37 full-disc Redump ISOs at `H:\Tests\XBOXTest` are
XGrid-only **benchmark** media (xemu cannot boot them) - never gates, never
accurate-mode acceptance, never fidelity work. A title quirk is allowed only
when isolated, documented, and not a substitute for the hardware behavior (the
007 EOS quirk is the historical precedent, not a pattern to extend).

## 5. Hard non-goals

- **License is GPL-2.0; porting is allowed.** XGrid may port code from
  xemu/QEMU/Cxbx/xqemu under their GPL-compatible licenses, preserving the
  notices. It must **not** embed QEMU/xemu as a native library or add C/C++
  emulator code to the build; port the algorithms to managed C#. See
  `docs/porting-policy.md`.
- No `References2/` deletes/moves/rewrites ever. Its single canonical store is
  `References2/` itself; verify with `scripts/refs-manifest.ps1 -Verify` against
  `docs/refs-manifest.json`. Never work around a missing reference; do not
  reintroduce an out-of-tree reference backup (`D:\xgrid-refs-restore\` is
  retired by operator decision 2026-10-20).
- No new codegen **micro-slices** while the C4-C6 program is open; C4-C6 are
  the authorized structural stages (section 0). Interpreter *correctness* fixes
  (oracle-found) are not codegen slices.
- No micro-optimization below a measured 2% ceiling.
- No native QEMU/xemu embedding; port source to managed C# and record the
  provenance. New dependencies still follow the NuGet rule (precedent:
  `Xunit.SkippableFact` rejected; plain `[Fact]` + WGL-guard instead).

## 6. Standing gates

- **M-G (gameplay):** the four-part gameplay definition (`ImplementationPlan.md`
  E1) with the xemu 0.8.139 frame at the same guest time, a scripted input edge,
  no fault, and the four fast 1B anchors + four accurate goldens + ktest log
  byte-identical. Gameplay is acceptance, not a merge gate.
- **M-P (port):** the module's differential is exact (RAM/CPU SHAs, fault and
  watchpoint cases, or the row matrix), the software anchors stay exact, and
  `scripts/package-verify.ps1` passes with the new notices rows. A module is not
  "done" because it compiles.
- `dotnet build XGrid.sln -c Release`, `dotnet test`, `dotnet format
  --verify-no-changes` clean; warnings are errors.
- Release changes: `scripts/package.ps1` on every affected RID and
  `scripts/package-verify.ps1` green (`PACKAGE PASS`); the tree must be clean so
  the manifest records the commit, and the version's release notes must exist.
- ktest smoke 7/7 (~5 min, 792,800,535 steps bit-reproducible) + menu/triangle
  1B golden pairs before any perf/default-flip merge.
- Frontend changes: `dotnet build src/XGrid.Frontend.Avalonia -c Release` +
  project format clean; UI-only work does not re-run core gates, but it must not
  touch core/CLI/test files.
- Every Track B/E/P/C landing updates `ImplementationPlan.md` status,
  `docs/compatibility.md` (when it changes a sample row), and the session notes.
- Every C4-C6 performance landing reports **ns/served member + wall per
  presented frame**, the same-window A/B, and the exact 1B differentials; 1B
  walls alone are continuity anchors, not a performance claim.
- Session start on any machine: `pwsh -File scripts/host-doctor.ps1` (fingerprint,
  free space, gate media, toolchain, `References2` integrity). A session that
  skips it owns whatever environment surprise it hits.

