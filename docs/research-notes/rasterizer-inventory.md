# Software rasterizer per-pixel inventory - XGrid NV2A (`--gpu sw`, the default)

> **Status 2026-10-22 (239/240) - no rasterizer change; the *surface clear* fill
> is no longer per-pixel.** `FillSurface` (the colour/zeta clear writer the
> rasterizer shares with the GL path) used to store one 32-bit pixel per
> `WriteGpu` call, i.e. an interface dispatch plus a bounds check per pixel; a
> 4-byte aligned, exactly in-range rect now fills whole rows through
> `Span<uint>.Fill` and everything else keeps the loop (decisions 240). The
> clears were ~540 ms of the triangle 1B `gl+replace` wall and are ~40 ms now;
> the `sw` legs' `rasterPsh` is unaffected (menu 11.15 s, triangle 4.55 s at 1B,
> every anchor byte-identical). Nothing else in this inventory moved.

> **Status 2026-10-21 (§211) - no rasterizer change this session; one opt-in
> sampler correction.** `xgu bumpenv_ripple`'s black frame (compatibility gap 10)
> was diagnosed as *state*: the per-frame quads take their colour from `T1`
> under `V0 = T1` / `D = V0`, one of them with stage 1 programmed `NONE`, and the
> app's zeta clear is nearer than every fragment - forcing the clear far or the
> depth test off yields the same 406-colour wedge, so the fill loop is not the
> cause (§211a-211e). The landed change is in the opt-in convolution path:
> `SampleConvolved` stepped `1/Width` of a texel on linear (LU_IMAGE) stages
> instead of one texel, so the nine taps collapsed onto one texel; the swizzled
> path is untouched (§211b). Raster numbers are unchanged from §210: 1B menu
> `rasterPsh` 10.84 s of a 31.16 s wall, triangle 4.35 s, `compiled=` fills
> identical to the legacy leg. O6 row spans and the 2-pixel interleave remain
> demoted at their bounded ceilings.

> **Status 2026-10-21 (§210) - the compiled fills serve TEXFILTER's tent, and
> the "2.4x filter cost" was not the fill loop.** A kernel field only convolves
> when MIN is CONVOLUTION_2D_LOD0 (7) (`xemu psh.c:197-210`); the dashboard and
> the cubemap sample program `2/2/1`, i.e. a plain tent. With that gate fixed,
> both compiled fills accept a bilinear plan and transcribe `SampleBilinear`
> through one shared `ReadTapFast` tap reader, so the filtered path runs the
> same loops: 1B menu 79.87 -> **31.90 s** (legacy 32.22 s), triangle
> 10.53 -> 11.66 s (`rasterPsh` 4.28 -> 5.05 s = four taps on the k2/k3 shape),
> and the menu's `compiled=` drives are identical to the legacy leg
> (429,350 fills / 277.0M px). What is left of the rasterizer is unchanged: the
> per-pixel work this inventory prices, with O6 row spans and the 2-pixel
> interleave still demoted at their bounded ceilings (§208/§204). The remaining
> menu raster cost is ~11.0 s of a 32.2 s wall.

> **Status 2026-10-20 (§205) - the present path is closed; the fill loop is again
> the raster lever.** §204 removed the `gl+replace` present-path overhead
> (`drawMs` 12.55 -> 2.41 s) and §205 batched a whole draw command into one
> instanced draw (`drawMs` 1971 -> 1045 ms, -47%), so Track B has no scoped slice
> left: the remaining menu submission is ~0.8 s of per-triangle block packing and
> the ~0.5-0.9 s texture fingerprint. What is left of the software rasterizer is
> the per-pixel work this inventory prices (O6 row spans and the 2-pixel
> interleave stay demoted at their bounded ceilings; nothing here is scheduled
> while Track E/P are open).

> **Status 2026-10-20 (course correction, §203) - the next raster lever is the
> present path, not the fill loop.** The product frontend renders on `--gpu sw`;
> `--gpu gl --gpu-replace` replaces this raster but pays ~9.9 s / 686 frames in
> per-flip readbacks (31.1 vs 33.3 s). Track B's first slice is that present
> path. The 2-pixel interleave and O6/O7 are demoted; take them only if the
> present slice stalls.

> **Status 2026-10-20 (§202): the compiled loops own the sampler fast lane.**
> Both compiled fills read texels (and the k1 blend destination) straight out of the
> RAM array behind one per-fill `SamplerAddressBound` proof, and pick their exact
> `DecodeTextureTexel` branch per fill (`SamplerPlan.DecodeKind`), so the per-sample
> interface dispatch and decode switch are gone for the shapes the titles run; the
> lighting loop also drops the clamps its own algebra proves are no-ops and hoists
> the per-row barycentric terms. 1B A/B vs `65e136e`: **triangle `rasterPsh`
> 3.39 -> 2.87 s (-14.2%) / wall -4.0%, menu 13.47 -> 12.49 s (-5.3%) / wall -2.3%**,
> SHAs and every counter identical, accurate mode byte-identical. Measured negatives
> (do not re-try): the k2 constant fold (-2-3%), the alpha-1 blend skip (neutral),
> `AggressiveInlining` on `SampleFast` (neutral). The remaining sampler cost is the
> dependent load chain (~4.5 s of the menu's 12 s), so the next structural lever is
> 2-pixel interleaving for ILP, not more hoisting: the ablation table in §202 prices
> each block (decode 1.0 s, destination read 0.5 s, pack 0.3 s, write 0.2 s,
> `TrackTextureCoords` ~0).

> **Status 2026-10-20 (§199p): O2 landed.** `IGpuMemoryFast` now provides a
> draw-wide batch (one writer-suppression scope + a decode-page memo per fill)
> and direct pinned-RAM word reads/writes with exact fallbacks (non-RAM, bus
> watch, unbatched write). Measured -2.0% on both 1B SW walls, 3/3 interleaved
> pairs, goldens/counters exact; suite 1511. **O4 also landed:** the swizzled
> sampler path replaces the per-sample `InterleaveBits` loops with the
> branchless Morton bit-spread when the plan masks are the standard
> alternating ones (`MortonMasks`; non-standard masks keep the loop). 1B SW,
> 3 pairs vs O2: triangle -5.9% (3/3), menu -1.2% (2/3). O1/O3/O5 (per-pixel
> fragment/counter work) are the next Track B levers; O6/O7 stay filed.

> **Revision note (line-number currency, 2026-10-17 evening).** `src/XGrid.Nv2a/Nv2aMmio.cs`
> is being edited concurrently in the working tree (uncommitted `--gpu-time` rasterizer
> instrumentation: `RasterTimingEnabled` + `Stopwatch` pairs around both fill paths, +38
> lines; see section 8). **Every Nv2aMmio.cs line number in this report is against the
> committed HEAD revision** (file = 6,508 lines, `_memory` at 356, `FillTriangle` at 5544,
> `FillTrianglePsh` at 6277 — the same numbering the task prompt used). In the current
> working tree the mapping is +24 after the `_memory` field, +31 after `FillTriangle`,
> +38 after `FillTrianglePsh`; method names are the stable anchors.
Read-only analysis of `src/XGrid.Nv2a/Nv2aMmio.cs` (6,508 lines at committed HEAD), the
`IGpuMemory` adapters and the memory path they sit on. No code changed, no build
run. All line numbers are HEAD as of 2026-10-17. Deliverable for the rasterizer
wall-share investigation; nothing here is a claim of measured wall — cost columns
are order-of-magnitude reasoning from op counts, branches and call chains.

Measured shape (operator-supplied 1B-step `--gpu sw` anchors):

| counter | menu | triangle |
|---|---|---|
| pixels (color-written fragments) | 285,171,381 | 89,142,121 |
| textured | 278,577,344 (97.7%) | 48,754,488 (54.7%) |
| blended | 211,696,978 (74.2%) | 518 |
| depth-tested | 136 | 15,667,336 (17.6%) |
| frames / stalls | 686 / 686 | 68 / 34 |
| zclip / zclipCull | 0 / 21526 | 0 / 21526 |
| draws | 33632 (skipped=1402) | 2706 |

Texture-format mix (attempts/misses): menu `12`=210,739,200/0 (LU A8R8G8B8,
**linear**, no swizzle), `04`=45,195,656/0 (A4R4G4B4 swizzled), `06`=22,642,488/0
(A8R8G8B8 swizzled); triangle `30`=62,444,852/62,444,852 (depth format — combiner
shadow/miss short-circuit), `06`=38,309,688/0, `12`=10,444,800/0.
So menu sampling is 76% linear / 24% swizzled; triangle sampling is 70% the
format-miss short-circuit plus 38.3M swizzled 32-bit samples.

Two different pixel loops carry all of it:

```
RecordMethod (Nv2aMmio.cs:2599)
  └─ MethodBeginEnd param 0 → FlushDraw (3859)
      ├─ FlushInlineDraw (3704)          → FillTriangle (5544)          legacy/combiner-off
      └─ FlushProgramDraw (4016)         → FillTrianglePsh (6277)  when _combiner.FinalEnabled (4075)
                                         → FillTriangle (5544)     otherwise (4243)
             both → TrySampleTexture* (4347/4352) + ProcessFragment (5283) → WritePixel/WriteZeta
  └─ MethodClearSurface → ExecuteClear (3372) → FillSurface (3479)   per-pixel clears
```

## 1. `IGpuMemory` implementations and their per-call work

Production adapters (only two):

1. **`XboxMachine.PhysicalGpuMemory`** (`src/XGrid.Host/XboxMachine.cs:844-899`) — **the
   one the rasterizer uses in `xgrid boot`**: `XboxMachine.cs:55` constructs
   `new Nv2AMmio(ramSize, "NV2A", new PhysicalGpuMemory(Memory, Cpu))`. Also implements
   `IPresentDirtyWatch`.
   - `ReadPhysical` (857-860): one `AddressSpace.Read32`.
   - `WritePhysical` (862-878): `Ram.BeginGpuWriteSuppression()` → try `_memory.Write32`
     → finally `EndGpuWriteSuppression()` → `_cpu.InvalidateDecodeCachePage(address)`.
2. **`Program.MachineGpuMemory`** (`src/XGrid.Cli/Program.cs:2285-2335`) — used **only**
   for the GL backend's texture reads/uploads (`Program.cs:359`); the software
   rasterizer never sees it. Same body as above minus the decode-cache drop.
3. Test fakes (must keep working through the interface): `ArrayMemory` in
   `tests/XGrid.Nv2a.Tests/*` (7 files) and `tests/XGrid.GpuGl.Tests/Present*.cs`;
   plain array access, no hooks. Any fast path must be an *optional* interface so
   these keep the exact legacy semantics.

Per-call work beyond a raw array access (production, default boot):

**Read** (`TrySampleTexture` 4445/4446, `TryReadPixelFloat` 5267/5275, `ReadHalf` 5125,
`WriteZeta` 5232/5246, `FillSurface` 3504):

1. interface dispatch `IGpuMemory.ReadPhysical` (call site sees mostly one impl;
   .NET 8 dynamic PGO may guarded-devirtualize, but the interface is multi-impl
   process-wide because of the test fakes);
2. `AddressSpace.Read32` (`src/XGrid.Memory/AddressSpace.cs:124-137`):
   `(ulong)address + 4 <= Ram.Size` bounds check → `Ram.Read32`;
3. `PhysicalMemory.Read32` (`src/XGrid.Memory/PhysicalMemory.cs:60-63`): **second**
   bounds check (`CheckedRange` 276-285) + `_bytes.AsSpan` + `BinaryPrimitives.ReadUInt32LittleEndian`;
4. non-RAM fallback: `TryGetRegion` linear scan of the region list (`AddressSpace.cs:69-89`)
   or `UnmappedRead` (255-274), which **counts** unmapped buckets/pages — an observable
   diagnostic, so a fast path must fall back, not silently return 0.

**Write** (`WritePixel` 3693/3701, `WriteZeta` 5240/5251, `FillSurface` 3500/3507):

1. interface dispatch;
2. `Ram.BeginGpuWriteSuppression()` (`PhysicalMemory.cs:139-142`, `_gpuWriteSuppression++`);
3. try/finally around `AddressSpace.Write32` (`AddressSpace.cs:191-207`):
   - `CheckWatch` (234-240): `WatchEnd > WatchStart && address >= WatchStart && address < WatchEnd`
     (3 compares; the `WatchHit` event is only wired by diagnostics);
   - RAM bounds check; `Ram.Write32` (86-90): bounds check + span + LE store +
     `NoteGuestWrite` (246-269), which early-returns when
     `_presentWatchCount == 0 || _gpuWriteSuppression != 0`;
4. finally `EndGpuWriteSuppression()`;
5. `XboxCpu.InvalidateDecodeCachePage` (`src/XGrid.Cpu/XboxCpuDecodeCache.cs:277-280`) →
   `DecodeCache.DropPhysicalPage(address >> 12)` (`src/XGrid.Cpu/DecodeCache.cs:282-302`):
   `IsActive(page)` = `page < TrackedPages && bit` then return (framebuffer pages hold no
   cached code). **The decode cache is default-on since §137, so this runs in every gate run.**

Notes:

- **No 0x80000000 alias translation here.** The rasterizer already works in guest-physical
  addresses (surface base resolved from the RAMHT at draw start, 5558-5559/6290-6291).
  The alias/mirror/WC-view handling lives only on the CPU side (`RamAliasDevice`); the GPU
  adapter writes `AddressSpace` → `Ram` directly.
- The try/finally makes `PhysicalGpuMemory.WritePhysical` a **non-inlinable** call in current
  RyuJIT (methods with EH regions are not inlined), so every pixel write is a full
  non-inlined interface call whose body does ~6-8 compares, 2 span constructions and 3 calls.
  Rough order: raw array store ~0.5-1 ns vs this chain ~10-25 ns on a desktop core.
- Counters: none on the RAM fast path; unmapped counters only off-RAM. The present-dirty
  watch is a counter + branch when disarmed (sw default) or suppressed (all rasterizer writes).

## 2. Per-pixel operation tables

Legend: **per** = how often; **always?** = unconditionally in the loop (A) vs gated by
per-draw state (G) or data (D); **hoist** = can be lifted/cached per draw or per row
without changing arithmetic order or results. Cost guesses are ns/pixel on a desktop
core (straight-line managed code ~1-2 ops/ns; branch/mispredict 5-20 ns; float div 4-6 ns;
interface chain 10-25 ns).

### 2.1 `FillTriangle` (legacy / combiner-disabled) — loop 5581-5633

| # | op | site | per | cost | always? | hoist? |
|---|---|---|---|---|---|---|
| 1 | `px = x + 0.5f`, `py = y + 0.5f` | 5585-5586 | bbox px | 1-2 ns | A | row-invariant py; per-x increment changes FP order -> no |
| 2 | `w0 = ((bx-ax)*(py-ay) - (by-ay)*(px-ax)) * invArea` | 5587 | bbox px | 3-8 ns | A | subexpressions JIT-hoisted; re-association NOT exact |
| 3 | `w1` same | 5588 | bbox px | 3-8 ns | A | same |
| 4 | `w2 = 1f - w0 - w1` | 5589 | bbox px | <1 ns | A | no (order) |
| 5 | inside test (6 compares) | 5590-5591 | bbox px | 2-5 ns | A | row-span prefilter possible if a proven superset (see O6) |
| 6 | perspective renormalize (3 mul, sum, reciprocal, 3 mul) | 5600-5613 | inside px | 6-15 ns | G(`perspective`) | flag is per draw; per-pixel reciprocal is irreducible |
| 7 | interpolate r,g,b,a (12 mul + 8 add) | 5615-5618 | inside px | 8-15 ns | A | weights per draw; no re-association |
| 8 | interpolate depth (3 mul + 2 add) | 5619 | inside px | 2-4 ns | A | no |
| 9 | interpolate tu,tv (6 mul + 4 add) | 5620-5621 | inside px | 3-6 ns | A | no |
| 10 | `TrySampleTexture0(tu,tv)` | 5622 | inside px | see 2.2 | G(tex0 on) | per-draw descriptor (O3) |
| 11 | modulate r,g,b,a by texel + `TexturedPixels++` | 5623-5628 | sampled px | 2-4 ns | G | counter batch (O5) |
| 12 | `ProcessFragment(...)` | 5631 | inside px | see 2.3 | A | split into specialized variants (O1) |

### 2.2 `TrySampleTexture` (both paths; `TrySampleTexture0` is a 1-line forwarder 4347-4350)

| # | op | site | per | cost | always? | hoist? |
|---|---|---|---|---|---|---|
| 1 | `_memory is null` + control0 enable + `TextureSampleDisabled++` | 4358-4362 | attempt | 1-2 ns | A | per draw |
| 2 | `color = (format>>8)&0x7F`; `_textureFormatAttempts[color]++` | 4364-4365 | attempt | 2-4 ns | A | color per draw; counter batch (O5) |
| 3 | `IsLinearTextureFormat` switch | 4366, 4459-4462 | attempt | 1-3 ns | A | per draw |
| 4 | width/height decode (image rect vs `1 << BASE_SIZE`) | 4367-4383 | attempt | 1-3 ns | A | per draw |
| 5 | badsize check + `TextureSampleBadSize++` | 4385-4389 | attempt | <1 ns | A | per draw |
| 6 | `DecodeTextureAddressU/V` + `NormalizeTextureAddressMode` x2 | 4391-4393, 4738-4774 | attempt | 2-6 ns | A | per draw (uAddr/vAddr + ToWrapMode ints) |
| 7 | `MathF.Floor` x2 + linear/POT scaling | 4399-4400 | attempt | 2-4 ns | A | no (needs u,v) |
| 8 | border-mode compares (+ border path: decode, track, count) | 4401-4413 | attempt | 1-3 ns | G(border) | mode per draw |
| 9 | `WrapCoordinate` x2 | 4415-4416, 4705-4726 | attempt | clamp 2-4 ns; **wrap/mirror use int `%` = idiv, 10-25 ns each** | A | per draw: clamp inline; POT wrap/mirror -> mask (exact) |
| 10 | `TrackTextureCoords(x,y)` | 4417, 5878-5909 | attempt | 2-6 ns | A | per-draw locals + one merge (O5); min/max exact |
| 11 | `TextureFormatBytesPerTexel` switch | 4419, 4472-4480 | attempt | 1-3 ns | A | per draw |
| 12 | miss path: `_textureFormatMisses[color]++`, `_textureStateMisses[idx]++`, return | 4420-4425 | attempt | 2-4 ns | D | per draw + batch |
| 13 | address: linear `y*pitch + x*bpp` | 4429-4438 | sample | 2-5 ns | D(linear) | per draw; row base + add is exact (integer) |
| 14 | address: `SwizzleOffset(x,y,w,h) * bpp` | 4439-4442, 4826-4920 | sample | **20-50 ns** (mask loop + 2x `InterleaveBits`, up to 32 iters each) | D(swizzled) | masks per draw + branchless interleave -> same bits (O4) |
| 15 | `_memory.ReadPhysical(address)` (+ `&0xFFFF` for 2-byte) | 4444-4446 | sample | 5-15 ns | A | memory fast lane (O2) |
| 16 | `DecodeTextureTexel` switch | 4447, 4488-4534 | sample | 2-6 ns | A | format per draw -> direct code (O3) |
| 17 | `CountSample` (success++, rgb0 test, state counters, first addr/texel) | 4448, 5911-5934 | sample | 3-8 ns | A | batch (O5) |

### 2.3 `ProcessFragment` — 5283-5465 (both loops call it)

| # | op | site | per | cost | always? | hoist? |
|---|---|---|---|---|---|---|
| 1 | `_memory is null` | 5287 | fragment | <1 ns | A | per draw |
| 2 | `_zclipActive` + `ApplyZclip` (2 switches, `depth*range`, 2 compares) | 5295, 5094-5121 | fragment | 3-10 ns | G | `range` + min/max per draw (exact); counters stay per pixel or batch |
| 3 | alpha test: `_alphaRef / 255f` (**float div**) + `CompareFunc` switch | 5300, 3612-3641 | fragment | 5-10 ns | G | `_alphaRef/255f` per draw (exact) |
| 4 | `colorWrite` compute + early-out | 5305-5309 | fragment | 1-2 ns | A | per draw |
| 5 | zeta address + bounds + read (`ReadPhysical`/`ReadHalf`) | 5316-5324 | fragment | 5-15 ns | G(depth/stencil) | row base + add (exact); memory lane (O2) |
| 6 | depth decode (shift/mask/div or ConvertF24ToFloat) | 5331-5346 | fragment | 2-6 ns | G | branch on `_zFormatFloat`/`zetaBytes` per draw |
| 7 | stencil test (`CompareFunc`, masks) | 5348-5357 | fragment | 1-4 ns | G | masks per draw |
| 8 | `Math.Clamp(depth)` + depth compare incl. `EncodeDepth24/16` | 5359-5391, 5050-5077 | fragment | 8-20 ns | G(depth) | encode variant per draw (exact) |
| 9 | `WriteZeta` RMW through the interface | 5408-5411, 5223-5252 | fragment | 15-40 ns | G(depthMask/stencil) | memory lane; row batching preserves order |
| 10 | color address compute | 5415 | fragment | 2-4 ns | G(colorWrite) | row base + add (exact) |
| 11 | `needDst` + `TryReadPixelFloat` | 5418-5424, 5254-5281 | fragment | 8-20 ns | G(blend/partial mask) | per draw flag; memory lane |
| 12 | blend constant unpack — **4 float divisions per pixel** | 5429-5433 | blended | **16-24 ns** | G(blend) | per draw (pure constants; exact) |
| 13 | `BlendFactor` x2 + equation switch + 4 clamps | 5434-5453, 3642-3665 | blended | 10-25 ns | G(blend) | factor kinds per draw; same math (exact) |
| 14 | color-mask selects | 5457-5461 | fragment | 2-4 ns | A | per draw |
| 15 | `PackPixel` (4x clamp+mul+add+cast) | 5462, 3667-3677 | fragment | 5-12 ns | A | `bytes` per draw -> specialized (exact) |
| 16 | `WritePixel` -> interface write chain | 5462, 3684-3702 | fragment | 10-25 ns | G(colorWrite) | memory lane + suppression/page memo (O2) |
| 17 | `DrawnPixels++` | 5463 | fragment | ~1 ns | G | batch (O5) |

### 2.4 `FillTrianglePsh` (program draws with the final combiner enabled) — loop 6333-6465

Everything from 2.1 (barycentric 6337-6347, perspective 6352-6365) plus:

| # | op | site | per | cost | always? | hoist? |
|---|---|---|---|---|---|---|
| 1 | interpolate 24 vectors: diffuse 4, specular 4, 4x texcoord quads, fog, depth (72 mul + 48 add) + 16 array writes | 6367-6408 | inside px | 30-55 ns | A | no re-association; the `coords` arrays could become 16 locals (exact, still stack) |
| 2 | clip-plane scan over 4 stages (+`ClipPlaneDiscards` per matching stage) | 6410-6423, 6100-6112 | inside px | 2-6 ns | G(CLIPPLANE) | texModes already per draw; skip when no stage is CLIPPLANE (exact) |
| 3 | `Array.Clear(stageDots)` | 6426 | inside px | 5-15 ns | A | 4 explicit writes / reuse (exact) |
| 4 | `ResolveTexel` x4 (validity switch, mode switch, SampleInto -> 2.2, SetTexelNone/Zero) | 6427-6433, 5692-5831 | inside px | 20-120 ns | A | mode set per draw; could specialize per draw (exact) |
| 5 | `Evaluate` — combiner: stackalloc regs, 6 CopyVec, `EvalStage` x2 (stages=1 on both titles), final combiner (14 DecodeInput, 15 ReadFinalComponent, 20 MapComponent switches, 6 mul, 6 add) | 6439-6448, PshCombiner.cs:692-784, 928-... | inside px | 40-150 ns | A | decode per draw into a flat op list; same float order (O7) |
| 6 | `TexturedPixels++` | 6450-6453 | inside px | ~1 ns | A | batch |
| 7 | rgb0 checks + `_combinerRgbZeroFragments/...Alpha++` | 6455-6462 | inside px | 2-4 ns | A | batch (exact) |
| 8 | `ProcessFragment` | 6464 | inside px | see 2.3 | A | O1 |

Per-draw (not per-pixel) costs in `FlushProgramDraw`, for completeness: `constants`
(192x4 floats), `inputs`/`outputs` (32x4 each), `transformed` dictionary, `program` copy
(4032-4071); `FillTrianglePsh` allocates 4x float[4] + 3 arrays per *triangle*
(6311-6316) — amortized over ~8.5k (menu) / ~33k (triangle) px/draw.

### 2.5 Clears — `ExecuteClear` (3372) -> `FillSurface` (3479-3511)

Per cleared pixel: 4-byte one `WritePhysical`; 2-byte read+write RMW through the interface.
`ClearsExecuted++` per clear (3374). Triangle `clears=6455` with 68 frames means ~68
full-screen zeta clears (~20.9M RMW pixel pairs) plus small color clears; menu `clears=695`.
Same memory lane applies (O2); a whole-row span fill is exact when the guest surface
allows it (the pitch/width are per draw).

## 3. Diagnostics: every counter that updates per pixel (all ALWAYS-ON)

There is no flag gating any rasterizer diagnostic; the only conditional input is
`Backend != null` (adds the GL call). Printed by `XGrid.Cli/Program.cs:1736-1764`.

| counter | increment site | per | printed as |
|---|---|---|---|
| `DrawsExecuted` | 3773 / 4074 | draw | `draws executed=` |
| `SkippedDraws` | 3711, 3729, 3748, 3756, 3766, 4020, 4028, 4043, 4049 | draw | `skipped=` |
| `DrawnPixels` | 5463 | color-written fragment | `pixels=` |
| `TexturedPixels` | 5628 / 6452 | fragment with a successful sample | `textured=` |
| `BlendedPixels` | 5454 | blend fragment | `blended=` |
| `DepthTestedPixels` | 5363 | depth-tested fragment | `depth=` |
| `ZclipClampedPixels` / `ZclipCulledPixels` | 5108 / 5112 | out-of-bounds fragment | `zclip=` / `zclipCull=` |
| `TextureSampleSuccesses` | 5913 | sample | `tex ok=` |
| `TextureSampleRgbZero` | 5917 | sample (r==g==b==0) | `rgb0=` |
| `TextureSampleDisabled` | 4360 | disabled attempt | `disabled=` |
| `TextureSampleBadSize` | 4387 | zero-size attempt | `badsize=` |
| `_textureFormatAttempts[color]` | 4365 | attempt | `formats= NN=attempts/misses` |
| `_textureFormatMisses[color]` | 4422 | miss | same |
| `TexModeFallbacks` + `_texModeFallbackByMode[mode]` + `_textureStateFallbacks[idx]` | 5786 / 5835-5837 / 5988/6002/6024/6051/6151 | stage resolve | `fallbacks= byMode=` |
| `UnsupportedDotMaps` | 6267 | dot-map stage | `dotMaps=` |
| `_combinerRgbZeroFragments` / `...AlphaFragments` | 6457 / 6460 | combiner fragment | `combiner rgb0=/rgb0Alpha=` |
| `CombinerDraws` | 4081 | draw | `combiner ... draws=` |
| `_textureStateSamples[idx]` / `_textureStateRgbZero[idx]` / `_textureStateMisses[idx]` | 5927 / 5930 / 4423 | sample | `texstate n= rgb0= miss=` |
| `_textureStateFirstAddress/FirstTexel[idx]` | 5923-5924 | first sample per state | `texstate addr= texel=` |
| `_textureStateMinU/MaxU/MinV/MaxV[idx]` | 5883-5908 | attempt | `texstate u=[..] v=[..]` |
| `FramesPresented` / `FlipStalls` | 2797 / 2807 | flip / stall | `frames= stalls=` |

Per-pixel cost of the diagnostics themselves: ~10-20 ns/fragment in the menu path
(attempts + track + count + draw counters), i.e. a real but not dominant share.

## 4. Ranked bit-exact optimization list

Rank = expected wall saving on the two 1B goldens (menu 285.2M px, triangle 89.1M px)
with LOW exactness risk first. Savings are order-of-magnitude estimates; multiply
per-pixel deltas by the pixel/sample counts in the header.

### O1. Per-draw fragment-state hoisting + specialized `ProcessFragment` variants (LOW risk, highest certainty)

Split `ProcessFragment` into a small set of variants selected once per draw by
`(zetaBytes==4|2|0, _zFormatFloat, _depthTestEnable, _stencilTestEnable, _blendEnable,
bytes==4|2, _colorMask==all, needDst)`; inside each variant use locals for
`_zclipActive`/range, `_alphaRef/255f`, blend constant vector, `_colorSurfaceBase + PresentedColorOffset`
and row bases (`row = base + y*pitch`, then `+ x*bytes` per pixel — same integer arithmetic).

- Kills per pixel: 4 blend divisions (5429-5433) ~16-24 ns, `_alphaRef/255f` ~4-6 ns,
  ~15 field loads, ~10 branches, the `zetaFormat`/`ZetaDepthRange` switches in `ApplyZclip`.
- Estimate: **15-40 ns/px on the menu path (4-11 s), 5-15 ns/px triangle (0.5-1.5 s)**;
  the blend-division hoist alone is ~211.7M x 16-24 ns = **3.4-5 s menu**.
- Exactness: all hoisted values are pure functions of per-draw register state; the code
  already caches `bytes/pitch/clip*/zetaBytes/zetaPitch` per draw (5548-5559, 6279-6291),
  so this only extends an established pattern. Keep every per-pixel counter increment
  or batch it via O5. Risk: none beyond code size.

### O2. Memory fast lane for pixel/texel I/O (MEDIUM risk, biggest structural win)

Add an optional interface (e.g. `IGpuMemoryFast { bool TryGetRamSpan(uint address, int length, out Span<byte> span); void NoteGpuWriteSpan(uint address, int length); }`)
implemented by `PhysicalGpuMemory`/`MachineGpuMemory` (delegating to
`AddressSpace.Ram.GetSpan` + `BeginGpuWriteSuppression/EndGpuWriteSuppression` +
`InvalidateDecodeCachePage`), with the existing `IGpuMemory` calls as the fallback for
test fakes and non-RAM addresses. Then:

1. **Suppression scope per row (or per draw), not per write** (867/874): one
   `BeginGpuWriteSuppression`/`EndGpuWriteSuppression` pair instead of ~500M pairs
   (menu). Nesting is safe (plain counter, single-threaded, no CPU runs inside a draw).
2. **Page-drop memo**: remember the last dropped `address >> 12` and skip the
   `InvalidateDecodeCachePage` call when unchanged (per-row contiguous framebuffer writes
   change page every ~1-2 rows). `DropPhysicalPage` is idempotent for the same page and
   the decode cache cannot be re-filled mid-draw (no CPU fetch while the pusher runs) — exact.
3. **Span reads/writes** for texel/pixel/zeta access after the existing
   `address + bytes > RamSize` checks (3686, 5225, 5260) — same bytes, same order.
- Estimate: **10-25 ns saved per memory op**; menu ~500M ops (285M writes + 212M color
  reads) = **5-12 s**; triangle ~105M ops = **1-2.5 s**; clears add a slice.
- Exactness risks: (a) must keep the non-RAM fallback (unmapped counters are printed);
  (b) must keep suppression semantics (the present-dirty bit is a gate-visible behavior
  under GL); (c) must keep the per-write order for overlapping pixels (no cross-pixel
  reordering — do not buffer writes past a read that could alias; per-row flush is the
  safe granularity if buffering is used at all).

### O3. Per-draw texture-slot descriptor (LOW risk)

Precompute once per draw (in `EnterTextureState`, 5840, or at draw start): color id,
linear flag, width/height, bytes/texel, uAddr/vAddr + ToWrapMode ints, pitch, base
offset, border floats, swizzle masks. `TrySampleTexture` then reads one struct instead of
re-decoding `_textureFormat/_textureAddress/_textureControl1/_textureImageRect` per pixel.
- Estimate: **8-20 ns/sample**: menu 278.6M samples = **2-6 s**; triangle 48.8M = 0.4-1 s.
- Exactness: values are loop-invariant register decodes; identical results.

### O4. Swizzle fast path (LOW risk)

`SwizzleOffset` (4826-4854) rebuilds maskX/maskY and runs two `InterleaveBits`
(4899-4920) per sample. Hoist the masks per draw; replace the interleave with the classic
branchless bit-spread (or 256-entry byte tables). Output bits are a pure function of
(x, y, maskX, maskY) — identical by construction.
- Estimate: **15-35 ns saved per swizzled sample**: menu 67.8M = **1-2.4 s**;
  triangle 38.3M = 0.6-1.3 s. Combined with O3 this removes most of the sampler overhead.

### O5. Counter batching per draw (LOW-MEDIUM risk, all printed values exact)

Accumulate in per-draw locals and flush once at draw end (or per row):
`_textureFormatAttempts/_textureFormatMisses`, `_textureStateSamples/RgbZero/Misses/Fallbacks`,
`_texModeFallbackByMode`, `TextureSampleSuccesses/RgbZero/Disabled/BadSize`,
`TexturedPixels/DrawnPixels/BlendedPixels/DepthTestedPixels`,
`_combinerRgbZeroFragments/...Alpha`, `ZclipClamped/Culled`.
- `TrackTextureCoords` min/max: accumulate in 4 locals per draw and merge with the same
  `if (v < min)` semantics (NaN never wins, same as today), then write once.
- First-address/texel: keep a per-draw `bool first` initialized from the pre-draw
  `samples + misses == 0` test (5878-5888, 5921-5925) so the "first" write stays exact.
- Estimate: **5-12 ns/fragment**: menu 285M = **1.5-3.5 s**; triangle 0.5-1 s.
- Risks: the `texstate` lines are printed sorted by `samples+fallbacks+misses`
  (`Program.cs:1756`), so relative counts must be exact — they are, because the totals are
  identical; just never flush mid-draw (no observer exists mid-draw: snapshots are taken
  at step boundaries, and a whole draw completes inside one step).

### O6. Row-span iteration (MEDIUM risk, keeps pixel coverage exact)

Per row, compute an x-interval that is a **superset** of the row's inside pixels
(double-precision edge solve + margin) and skip the loop for x outside it; keep the
existing float inside test unchanged for every pixel in the span. Also precompute
`rowBase = surface + y*pitch` (exact integer) and `py` per row.
- Estimate: for the menu's mostly-large quads the win is small; for diagonal/thin
  geometry it removes ~50% of bbox iterations (each ~5-10 ns) plus 2 int multiplies/px:
  **1-4 s menu, 0.5-1.5 s triangle** (shape-dependent).
- Risk: only the superset property; a non-superset span silently changes `pixels=`.
  Mitigation: margin + the SHA gates; do NOT replace the inside test with an analytic
  predicate (FP boundary pixels would flip).

### O7. Combiner "compiled" per-draw program (MEDIUM risk)

Decode `PshCombiner` stage config (RgbInputs/AlphaInputs/outputs, mapping, flags,
final CW) once per draw into a flat op list; execute with the same float operations in
the same order. Removes per-pixel `DecodeInput`/`RegOffset`/`MapComponent` switches and
`consts` indirection inside `Evaluate` (PshCombiner.cs:692-784, 928-...).
- Estimate: 10-25 ns/fragment on combiner draws; the menu's rgb0=44.4M proves at least
  44.4M combiner fragments (likely most of the 285M); triangle ~62.5M:
  **2-7 s menu, 2-5 s triangle**.
- Risk: exact op order and the `FinalEnabled=false` fallback (r0 return) must be kept;
  this is the highest-effort item, do it after O1/O3.

### O8. Blend-path specialization (LOW risk)

Precompute per draw: constant vector, source/dest factor kinds, equation kind; keep the
`Math.Min/Max` and clamp order identical (5436-5453). Removes the two `BlendFactor`
switches and the equation switch per blended pixel.
- Estimate: **5-15 ns/blended px**: menu 211.7M = **1-3 s**; triangle negligible.

### O9. `PackPixel`/`WritePixel` specialization by `bytes` (LOW risk)

`bytes==4` branch is per draw; keep `(uint)((Math.Clamp(x,0f,1f) * 255f) + 0.5f)` exact.
- Estimate: 2-6 ns/px: menu 0.6-1.7 s; triangle 0.2-0.5 s.

### O10. Clears via row spans (LOW risk)

`FillSurface` (3479-3511): for 4-byte clears a row of identical words can be written as a
span fill; for 2-byte RMW keep per-pixel reads but use the O2 lane. Triangle ~20.9M
RMW pairs: **0.3-1 s**. Exact (same bytes; the per-pixel `address + bytes > RamSize`
early-return must become a per-row bound check with the same cutoff point).

### Explicitly rejected (NOT bit-exact / out of scope)

- Incremental barycentric updates (`w0 += dx` per x): changes FP rounding; would flip
  `pixels=`/`depth=`/frames. Reject.
- Re-associating interpolation or blend math, changing encode/decode order, replacing
  `MathF.Floor` semantics. Reject.
- Skipping the software loop under `--gpu gl` (see section 5): changes guest RAM contents
  the guest can read (`--fb` dumps, FMV/CPU blits, present-upload fallback). Only safe
  with a full behavioral study; not a bit-exact change.

Sum of O1-O10 for the menu: roughly **15-45 s**; triangle **6-16 s** — i.e. on the order
of half the rasterizer's wall, which is the reason to do the safe items first.

## 5. GL coexistence answer: with `--gpu gl` the software rasterizer STILL RUNS (coexist by design, not a bug)

Evidence chain (all HEAD):

1. `src/XGrid.Nv2a/IPgraphBackend.cs:6-15` (interface doc, verbatim): *"Optional second
   consumer of the PGRAPH dispatch (GPU backend seam). **The software path executes
   exactly as today, unconditionally**; the backend observes the same resolved state at
   the flush sites (observation-only mirror of `RecordMethod` plus draw/clear/flip
   callbacks...)"*.
2. `src/XGrid.Nv2a/Nv2aMmio.cs:565-572` (`Backend` property doc): *"when attached the
   software path still executes unconditionally and the backend observes the same
   resolved state synchronously at the flush sites."*
3. Call sites have **no guard**: `Nv2aMmio.cs:5570` (`FillTriangle`) and `:6302`
   (`FillTrianglePsh`) call `Backend?.Draw(...)`, then the pixel loops at `:5581-5633`
   and `:6333-6465` run unconditionally. Same for clears: the SW `FillSurface` runs at
   `:3432/3454` and `Backend.Clear(...)` fires *after* it at `:3460-3476`. Flips:
   `:2797-2803` does `CapturePresentedFrame()` **and** `Backend?.Flip(...)`.
4. The null-conditional short-circuits the argument evaluation, so with `Backend == null`
   (default `--gpu sw`) `BuildDrawInfo`/`BuildLegacyDrawInfo` are not even constructed —
   the sw path pays nothing.
5. When a backend IS attached, the presented frame comes exclusively from the backend:
   `TryCopyPresentedFrame` `:5158-5163` returns `Backend.ReadbackPresented(...)` and never
   reads guest RAM. So under `--gpu gl` the **software rasterizer's pixels are not what is
   displayed** — the FBO is. The SW writes remain because they mutate guest RAM
   (the guest can read surfaces; `PresentUpload.TryBuildUploadPixels` re-uploads from
   guest RAM when the FBO is empty/stale; `--fb` dumps RAM).
6. The golden gate relies on this: `scripts/gpu-slice1-golden.ps1:56-69` parses the
   `NV2A draws executed=... frames=... stalls=...` line from **both** the sw and gl logs
   and requires them identical — those counters are only incremented by the software
   rasterizer, so identical values prove the SW loop ran in the gl session too.
7. Design record: `docs/research-notes/xgrid-architecture-decisions.md:7160-7167`
   (§132 finding i): *"the software path runs unconditionally beside the backend, and
   `Backend?.Draw` fires BEFORE the software twin"*; `docs/research-notes/gpu-backend-design.md:499-505`
   (slice-1 work order: "observation-only mirror ... No behavior change when `Backend == null`").

Consequence: under `--gpu gl` the wall is CPU emulation + **the full software rasterizer**
+ GL submission/readback (measured 0-2.9% of wall in §122). The GL run therefore cannot
be faster than the sw run minus nothing — it is strictly more work. That is intended for
slice 1 (no silent mixing; the sw pixels stay the oracle), but it means any rasterizer
optimization benefits BOTH modes, and a future "GL replaces SW" mode must first account
for the guest-RAM side effects listed in point 5.

## 6. Counters/diagnostics that MUST keep exact values

The binding gate currency is: (a) the two frame SHAs
(`menu 4EF1FBF66CB96CD74DB3EC076D128CC2DAC47E8709A583449EE9EB1C8CB0DE81`,
`triangle A1740DE069128A04226D67B2896BACF4A4C481F01FF89DA2152C6278D87AA27C`),
(b) `LastFault vec=0`, (c) the full **normalized stdout log compared byte-for-byte**
(`scripts/spec-exec-soak.ps1:14-17,27` only strips the mode-announcement lines and the
frame path), and (d) the anchor regexes (`spec-exec-soak.ps1:67-68`, `emit-gate.ps1:43`,
`decode-cache-soak.ps1:52`). Therefore *every* printed line must stay identical, not just
the quoted anchors:

- `NV2A draws executed=... skipped=... pixels=... textured=... clears=... blended=...
  depth=... zclip=... zclipCull=... frames=... stalls=... programLoad=... constLoad=...
  inlineElements=...`
- `NV2A tex ok=... rgb0=... disabled=... badsize=... formats= NN=a/m ... fallbacks=...
  byMode=... dotMaps=...`
- `NV2A combiner rgb0=... rgb0Alpha=... stages=... control=... final=... draws=...
  texmodes=...`
- the top-16 `NV2A texstate n=... rgb0=... fb=... miss=... addr=... texel=...
  u=[..] v=[..] ...` lines — the **sort order** (by samples+fallbacks+misses) and the
  min/max/first values are printed, so relative per-state counts must be exact too.
- `Frame <path> 640x480 nonzero=307200 ... surface=... pitch=...` (the path is normalized
  away by the soak script; `nonzero` is not) and the BMP SHA256.
- Also snapshot-serialized: `_textureFormatAttempts` (1218), `_textureStateSamples` (1236),
  `_combinerRgbZeroFragments` (1221), `DrawnPixels` (1247) etc. — snapshot loads happen at
  step boundaries only, so per-draw batching (O5) cannot be observed mid-draw, but any
  change to when counters are flushed must keep them exact at those boundaries.

Counters NOT gate-pinned but still worth keeping: none found — the log comparison covers
everything the CLI prints.

## 7. Verification recipe for any change here

1. Build the full solution (`dotnet build XGrid.sln -c Release`), run
   `dotnet test XGrid.sln -c Release --no-build` + `dotnet format --verify-no-changes`.
2. Menu + triangle 1B legs exactly as `scripts/spec-exec-soak.ps1` (`--gpu sw`, private
   media on `D:\spec-soak\media`): require SHA exact, `LastFault vec=0`, full normalized
   log identical to the recorded cache-mode baseline, counter anchors exact.
3. For memory-path changes (O2/O10) additionally run a `--decode-verify` leg
   (`verifyFailures=0`) and a `--block-exec` cross-check leg (SHA exact) per house rules.
4. For anything touching the sampler, keep a `--frame` export diff of the sw vs gl runs
   (`scripts/gpu-slice1-golden.ps1` policy: flat exact, edges within policy) so the GL
   side proves it reads the same state.
5. ktest smoke 7/7 remains the standing cross-check.

## Appendix — line index (HEAD)

- Loops: `FillTriangle` 5544-5634; `FillTrianglePsh` 6277-6467; `ProcessFragment`
  5283-5465; `TrySampleTexture` 4352-4449; `TrySampleTexture0` 4347-4350;
  `TrySampleTextureCube` 4544-4583; `TrySampleTexture3D` 4590-4629;
  `ResolveTexel` 5692-5831; `SampleInto` 6122-6135; `Evaluate` (PshCombiner.cs) 692-784;
  `FillSurface` 3479-3511; `ExecuteClear` 3372-3477.
- Helpers: `WrapCoordinate` 4705-4726; `SwizzleOffset` 4826-4854; `InterleaveBits`
  4899-4920; `DecodeTextureTexel` 4488-4534; `TextureFormatBytesPerTexel` 4472-4480;
  `IsLinearTextureFormat` 4459-4462; `DecodeTextureAddressU/V/P` 4738-4762;
  `ToWrapMode` 4783-4792; `PackPixel` 3667-3677; `WritePixel` 3684-3702;
  `WriteZeta` 5223-5252; `TryReadPixelFloat` 5254-5281; `ReadHalf` 5123-5127;
  `ApplyZclip` 5094-5121; `EncodeDepth24/16` 5050-5077; `BlendFactor` 3642-3665;
  `CompareFunc` 3612-3641; `TrackTextureCoords` 5878-5909; `CountSample` 5911-5934;
  `CountTexModeFallback` 5833-5838; `EnterTextureState` 5840-5876.
- Memory: `PhysicalGpuMemory` (XboxMachine.cs) 844-899, constructed at 55;
  `MachineGpuMemory` (Program.cs) 2285-2335, constructed at 359;
  `AddressSpace.Read32/Write32/CheckWatch` (AddressSpace.cs) 124-137 / 191-207 / 234-240;
  `PhysicalMemory.Read32/Write32/NoteGuestWrite/BeginGpuWriteSuppression` 60-63 / 86-90 /
  246-269 / 139-148; `XboxCpuDecodeCache.InvalidateDecodeCachePage` 277-280;
  `DecodeCache.DropPhysicalPage` 282-302.
- Backend seam: `IPgraphBackend` doc 6-15; `Backend` property 565-572; `Backend?.Draw`
  5570, 6302; `Backend.Clear` 3460-3476; `Backend.Flip` 2799-2803;
  `TryCopyPresentedFrame` 5153-5221 (backend branch 5158-5163).
- CLI print sites: `Program.cs:1736-1764` (draws/tex/combiner/texstate lines),
  `Program.cs:1756` (texstate sort), `Program.cs:1201-1211`/`1246-1261` (frame export).
- Gates: `scripts/spec-exec-soak.ps1:12-17,44-75`; `scripts/emit-gate.ps1:43`;
  `scripts/decode-cache-soak.ps1:52`; `scripts/gpu-slice1-golden.ps1:56-69`.

## 8. Concurrent working-tree change observed during this analysis (context, not part of the inventory)

While this inventory was being written, `src/XGrid.Nv2a/Nv2aMmio.cs` gained an uncommitted
host-only timing hook (`git diff`: +38 lines) — `RasterTimingEnabled` plus
`RasterLegacy/PshTicks/Calls` and two `Stopwatch.GetTimestamp()` pairs per fill-path
invocation (`FillTriangle` start/end, `FillTrianglePsh` start/end), surfaced as
`RasterLegacySeconds`/`RasterPshSeconds`. Relevance to this report:

- It is default-off, host-only, never snapshotted, and does not touch pixel math — so the
  default `--gpu sw` goldens and every counter anchor above are unaffected.
- When enabled it adds ~2 `Stopwatch` reads per draw (33,632 menu / 2,706 triangle draws)
  — negligible (<1 ms), so the measured rasterizer share will be honest.
- It also means the wall-share numbers the operator collects will cover exactly the two
  loops inventoried here (2.1 + 2.4), not `FillSurface` clears (2.5) or the per-draw
  vertex stage; if clears turn out to matter, `FillSurface` needs the same treatment.
- Line-number currency is addressed in the revision note at the top of this file.

No repository file was modified by this analysis; the only artifact written is this report
(`D:\spec-soak\analysis\rasterizer-inventory.md`).
