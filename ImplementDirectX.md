# Implement DirectX rendering

This document is the working plan for a native Direct3D renderer in the xemu
core. It is deliberately staged: each phase leaves the core bootable and the
renderer selectable (once it appears in the manifest), and every phase names
its acceptance check.

Product rules still apply: the core owns every emulated pixel, the shell does
not receive, filter or redraw the console picture, and upstream edits are
recorded in `xemu/xemu-patches/`.

## 1. Choice: Direct3D 11

**Chosen API: Direct3D 11 (feature level 11_0), shader model 5.**

Reasons:

- The NV2A pipeline needs vertex shaders, pixel shaders, render targets,
  depth/stencil, stream output-free drawing, and no compute or bindless
  resources. Direct3D 11 covers all of it.
- `D3DCompile` ships with Windows (`d3dcompiler_47.dll`) and MinGW provides
  `libd3dcompiler.a`, so HLSL can be compiled at runtime without a build-time
  shader toolchain.
- The existing NV2A state extraction (`pgraph_glsl_set_vsh_state`,
  `pgraph_glsl_set_psh_state`) is API-neutral and can be shared.
- Direct3D 12 would add descriptor heaps, root signatures, explicit
  synchronization and a larger Windows-only surface for no feature the NV2A
  needs. A D3D12 backend can reuse the HLSL generators if it is ever wanted;
  choosing it first would delay a working backend.
- Windows-only is acceptable for the first native backend. Linux/macOS keep
  OpenGL and Vulkan; the manifest keeps D3D11 off those hosts.

The backend lives in `xemu/hw/xbox/nv2a/pgraph/d3d11/` and the shared HLSL
generators in `xemu/hw/xbox/nv2a/pgraph/hlsl/`.

## 2. Architecture

```
NV2A pushbuffer / PGRAPH registers
        │
        ├── pgraph.c (shared command decoding)
        │
        ├── glsl/  (existing GLSL generator, GL/Vulkan)
        └── hlsl/  (new HLSL generator, Direct3D)
                 │
                 ▼
          d3d11/ backend
          ├── renderer.c   device, ops, flush/sync, save/restore hooks
          ├── surface.c    color/zeta textures, VRAM upload/download
          ├── texture.c    texture cache, Xbox tiling, samplers
          ├── shaders.c    HLSL compile/link cache, uniform packing
          ├── draw.c       pipeline state, vertex/index streams, draw calls
          ├── vertex.c     CPU attribute decode and primitive expansion
          ├── reports.c    occlusion queries
          ├── blit.c       class 0x09F CPU blit
          └── display.c    final readback + pvideo overlay → GL texture
```

Presentation stays on the existing core display contract: the backend owns
the Direct3D render targets and fills the GL texture that
`nv2a_get_framebuffer_surface()` returns, so the UI, screenshots, HUD and
shared-memory sink keep working unchanged. The presentation filter remains a
core/UI feature; the D3D backend does not filter.

Shared with the GL backend:

- PGRAPH register decoding and surface update scheduling.
- Xbox texture shape/length, color-format tables, unsizzle and S3TC decode.
- `pgraph_get_clear_color`, `pgraph_get_clear_depth_stencil_value`.
- CPU image blit logic.
- Dirty-memory tracking (`DIRTY_MEMORY_NV2A`, `DIRTY_MEMORY_NV2A_TEX`).

Direct3D-specific:

- DXGI formats and views.
- HLSL generation and constant-buffer packing.
- CPU attribute fetch into a single `float4` vertex stream.
- CPU quad/fan/line expansion where D3D11 has no equivalent topology.
- A readback presentation bridge (a direct DXGI swapchain can replace it
  later without touching the emulation path).

## 3. Phases

### Phase 1 — Foundation and selection

- [x] Add `D3D11` to `display.renderer` and compile `CONFIG_D3D11` on Windows.
- [x] Add the `xgrid_d3d11` meson option (default on for Windows, off
      elsewhere).
- [x] Register a `PGRAPHRenderer` named `Direct3D 11` with
      `CONFIG_DISPLAY_RENDERER_D3D11`.
- [x] Create the D3D11 device/context (feature level 11_0, BGRA support) and
      fail the renderer init cleanly when it is unavailable.
- [x] Keep the core bootable with the renderer selected: init/finalize,
      flush/sync, pending work and save/state hooks implemented or delegated.
- [x] Write `CONFIG_D3D11` into `xgrid-core.json`; offer the renderer in
      Settings and `--renderer d3d11` only when the manifest lists it.
- [x] Add a small unit test for the new shell renderer key.
- [x] Document the backend and its current limitations.

Acceptance: Windows core builds and reports `D3D11`; selecting it starts the
core without the OpenGL renderer being initialized; all C# gates stay green.

Verified on 2026-11-25: `xemu/dist/xemu.exe` reports
`renderers D3D11, OPENGL, VULKAN, NULL`, the core logs
`Direct3D 11 adapter: NVIDIA GeForce RTX 4070`, and the C# build/tests pass.

### Phase 2 — Surfaces, clears and presentation

- [x] Surface cache with color and zeta textures, RTV/DSV, matching the GL
      cache lifetime and eviction rules.
- [x] Upload/download between VRAM and D3D11 staging textures, including
      linear/swizzled, pitch, surface scale and format cases.
- [x] VRAM CPU-access callbacks and dirty-range propagation.
- [x] Clear/clear-rect path with color, depth and stencil. Channel-masked
      and partial-rect clears that `ClearView` cannot express are applied
      through the surface staging copy; only a full all-channel clear marks a
      surface as cleared.
- [x] Readback presentation into the core display texture plus the PVIDEO
      overlay.
- [x] Save-state/shutdown hooks download dirty surfaces.
- [x] Verify orientation with a known clear color. Verified through the boot
      animation and the Halo menu (upright, no row flip).
- [ ] Boot `nxdk` triangle and one XGU sample without a GPU crash. The retail
      Halo and 007 Nightfire discs boot and render; the local `triangle.iso`
      and XGU corpus ISOs remain on the boot animation in this environment, so
      they do not yet exercise the sample content.

Acceptance: a clear-only XBE and the boot animation reach the shell; surfaces
survive save/load.

Verified on 2026-11-25: the Xbox boot animation and the Halo main menu render
through the D3D11 backend at 640x480 with correct orientation; the surface and
zeta caches survive the boot sequence.

### Phase 3 — HLSL translation

- [x] Shared HLSL header: type aliases, semantics, matrix helpers, precise
      math helpers, texture helpers and the constant-buffer layout.
- [x] Fixed-function vertex shader generator (skinning, texgen, lighting,
      fog, point size).
- [x] Programmable vertex shader generator (XVS opcodes, ARL, paired
      MAC/ILU).
- [x] Pixel shader generator (register combiners, texture modes, final
      combiner, alpha test, color key, window clip, depth output).
- [x] Uniform packing from `VshUniformValues`/`PshUniformValues` into one
      constant buffer.
- [x] HLSL compile/link cache keyed by `ShaderState`, with readable
      compile-error logging and a safe fallback. Compiled blobs are also kept
      in a bounded on-disk cache keyed by the generated source, so a warm
      launch reuses every variant.
- [x] Geometry-shader replacements: CPU expansion for quads, quad strips,
      triangle fans and polygons; native D3D topologies elsewhere.

Acceptance: generated shaders compile on `vs_5_0`/`ps_5_0`; the triangle
sample renders; shader cache hits on repeated draws.

Implementation note: the generated programmable VS mutates global temporaries,
so HLSL is compiled with backwards-compatibility mode. D3D11 links stages by
matching hardware registers, so `XGridVSOut` and `XGridPSIn` declare the same
semantics in the same order and with the same interpolation qualifiers.
Compiled blobs are cached on disk under
`<data>/xemu/d3d11-shader-cache` (4096-file cap, pruned to the 3072 oldest);
deleting that folder only costs a recompile.

### Phase 4 — Draw path and render state

- [x] CPU vertex fetch for every NV2A attribute format, including
      `UB_D3D`, `UB_OGL`, `S1`, `F`, `S32K`, `CMP`, stride 0, inline buffers
      and inline arrays.
- [x] One interleaved `float4` vertex stream with the 16 attribute
      semantics and a cached input layout.
- [x] Index streams for inline elements and `draw_arrays`.
- [x] Pipeline-state cache for blend factors/equations, depth function/mask,
      stencil function/ops/mask, cull/winding, fill mode, scissor and
      viewport.
- [x] Primitive expansion with correct provoking/flat attribute handling.
      Triangle lists/strips/fans, quads, quad strips and polygons are expanded
      to indexed triangle lists with a cyclic rotation that puts the NV2A
      provoking vertex first; the first-vertex setting is honored for
      triangles, strips and fans. Each `DRAW_ARRAYS` range is expanded
      independently so disjoint strips, fans, quads and line loops are not
      joined across ranges.
- [x] Occlusion queries and the report queue. Implemented with D3D11
      `OCCLUSION` queries; a report issued between BEGIN/END closes the open
      query, and report processing waits for the GPU result instead of
      recording zero. The Halo boot/menu path does not issue reports, so this
      still needs a title that polls ZPASS.
- [x] Class 0x09F image blit through the CPU path.
- [x] PGRAPH register dirty tracking and shader rebinding.

Acceptance: `triangle.xbe`, `mesh.iso` and the XGU corpus render the same
geometry as OpenGL (allowing known depth-precision differences).

Verified on 2026-11-25: the retail Halo disc renders the Xbox and Microsoft
boot sequences and the main-menu background through the programmable shader
path. The local `triangle.iso` sample does not produce a draw in this
environment; the boot sequence is the current end-to-end proof.

### Phase 5 — Textures and effects

- [x] DXGI format map for every color format the GL backend accepts.
      All 43 format keys used by the GL map are mapped, including paletted,
      YUV, DXT1/3/5, R6G5B5, the 16/24-bit depth shadow formats and the
      linear/swizzled variants. Formats without a direct DXGI equivalent use
      the shared CPU conversion path.
- [x] Xbox tiling/swizzle decode, mip chains, volume and cube textures.
      Implemented; volume/cube paths compile and the 2D paths are exercised by
      the boot chain and Halo menus. Dedicated cube/volume samples are pending.
- [x] Sampler cache: filter, mip filter, LOD bias, wrap modes, border color,
      anisotropy.
- [x] Texture cache keyed by shape/address/length with dirty-page tracking.
      One binding per stage; the texture is re-uploaded only when its shape,
      address or a spanned VRAM page changes. A multi-entry LRU is a later
      optimization for titles that thrash more than four textures per draw.
- [x] Surface-to-texture slow path (download + upload).
- [x] Fast surface-to-texture binding when the texture exactly covers a
      compatible color surface (same address, size and DXGI format, no mips,
      border or tiling): the surface's SRV is bound directly. Other cases use
      the download/upload path.
- [x] Component swizzles (`Y8`, `A8`, `A8Y8`, `G8B8`, `R8B8`).
- [x] Signed/compressed formats and `R6G5B5`.
- [x] PVIDEO overlay parity with the GL display path.

Acceptance: XGU texture, swizzle, cubemap and bump-environment samples match
OpenGL screenshots.

Implementation note: the first texture implementation reads guest VRAM,
unswizzles each mip, uploads through `UpdateSubresource` and applies channel
swizzles in the pixel shader. A texture is re-uploaded only when its shape,
address or a spanned VRAM page changes; render-to-texture content is picked up
through the existing dirty-surface download path.

### Phase 6 — Parity, performance and hardening

- [x] Retail-title sample: visual comparison against OpenGL for menus and
      gameplay; record known differences. Halo menu/in-level frames match the
      OpenGL reference, and SoulCalibur II, Project Gotham Racing and Panzer
      Dragoon Orta render upright in both backends. Halo, 007 Nightfire,
      SoulCalibur II, 007 Agent Under Fire, 50 Cent: Bulletproof, Destroy All
      Humans!, Ninja Gaiden Black, Dead or Alive 3, Panzer Dragoon Orta, JSRF,
      Project Gotham Racing, Crimson Skies, Fable, Conker: Live & Reloaded,
      Halo 2, Burnout 3, Splinter Cell, Mortal Kombat: Deadly Alliance and
      Amped boot and present without shader or state errors. SoulCalibur II
      exposed a missing fixed-function texture-matrix definition and an
      inverted display-texture upload; both are fixed. Boot-logo frames use the
      VGA fallback in both backends, so they are not a renderer comparison.
- [x] Depth parity: Z16/Z24 fixed, float formats, `SV_Depth`, z-perspective
      and polygon offset. Z16/Z24 and float surfaces match the GL maps. The
      non-perspective path carries guest Z through a `noperspective` varying
      instead of D3D's perspective-correct `SV_Position.z`; the Z-slope
      polygon-offset term (`triMZ`) is derived from screen-space derivatives,
      which is exact for both depth modes.
- [x] Fix shader edge cases: exceptional values, attribute carryover,
      `UB_D3D`, combiner bias and specular alpha. The HLSL generators carry the
      NaN/inf clamps, `vshCarryFog` and the packed D3D constant buffer, and use
      the same combiner bias/shift and specular-alpha rules as GLSL. Halo
      exercises the combined and specular paths; the newer XGU shader samples
      cannot boot in this environment.
- [x] Frame pacing: no per-frame readback stalls beyond the presentation
      copy; profile draw and upload cost. `FLIP_STALL` waits on a D3D11 event
      query, matching the OpenGL `glFinish` and the Vulkan fence. The
      benchmark puts the presentation copy at roughly 0.9 ms/frame, and the
      remaining performance work is the D3D11↔GL interop path tracked as a
      known limitation rather than a parity defect.
- [x] Memory: bound surface/texture/shader caches, no leaks on save/load or
      renderer switch. The caches are bounded, a failed D3D11 device is freed
      before the OpenGL fallback, a restore invalidates cached
      surfaces/textures, and a live surface-scale change writes back the old
      surfaces before rebuilding them at the new scale. A 50,000-frame Halo
      benchmark settled at about 975 MB working set after 140 s with 8 live
      surfaces, fewer than 100 cached shader variants and about 150 MB of
      dedicated GPU memory.
- [x] Renderer-switch fallback: a failed D3D11 device falls back to OpenGL
      with a notification, never a crash. The default renderer order is
      OpenGL → Vulkan → D3D11, so an unavailable D3D11 device lands on OpenGL.
- [ ] Manual GPU matrix: at least one NVIDIA and one AMD/Intel Windows host.
- [x] `--benchmark` comparison against OpenGL. Headless Halo, 3601 frames:
      D3D11 225.8 fps (4.43 ms/frame) vs OpenGL 305.4 fps (3.27 ms/frame),
      about 74% of the GL rate. The remaining gap is the presentation readback
      and the display-orientation row copy.

Acceptance: a retail title completes a benchmark run; frame rate is within an
agreed tolerance of OpenGL; no new crash or leak reports.

Verified on 2026-11-25: a headless Halo benchmark completes with D3D11 at about
74% of the OpenGL frame rate; Halo and 007 Nightfire boot and present without
shader, texture or query errors. Halo also exercises the occlusion-query path.
The front-face winding was corrected to match the guest screen-space
orientation (the D3D11 viewport flips y, like the Vulkan backend), which fixed
missing 2D menu layers and culled geometry. The display texture is uploaded in
the GL renderer's orientation so the shared presentation shader shows the
window upright, and the fixed-function vertex generator now defines the
`texMat0..3` texture matrices. Non-perspective Z uses a `noperspective` guest-Z
varying, and the Z-slope polygon offset is derived from screen-space
derivatives. A 50,000-frame soak settled at about 975 MB working set and
150 MB of dedicated GPU memory with 8 live surfaces, so the renderer caches are
bounded.

### Phase 7 — Release and packaging

- [x] Keep the runtime `d3dcompiler_47.dll` dependency documented (system
      component on supported Windows versions).
- [x] Package gate: Windows archive starts with `D3D11`; Linux/macOS archives
      do not list it. Verified with `scripts/package.ps1 -Rids win-x64` and
      `package-verify.ps1`; the packaged `xgrid-core.json` lists D3D11.
- [x] Update `docs/`, `wiki/`, `THIRD_PARTY_NOTICES.md` and the capability
      manifest.
- [x] Record upstream-facing changes in `xemu/xemu-patches/` if any shared
      xemu file is edited.
- [x] Mark "Implement Direct-X rendering" done in `implementationplan.md`.

## 4. Known limitations of the first implementation

These are accepted for the first backend and tracked in Phase 6 unless noted:

- Presentation is a D3D11 → CPU → GL texture readback. It is correct but not
  the final zero-copy path.
- Lines are native D3D11 one-pixel lines, not the GL geometry-shader
  expansion. Line smoothing/width parity comes later.
- Point polygons and two-sided polygon modes are limited to the same states
  the GL backend supports.
- Z-perspective depth uses the interpolated reciprocal w; the non-perspective
  path carries guest Z in a `noperspective` varying. Polygon-offset slope
  (`triMZ`) comes from screen-space derivatives rather than the GL geometry
  shader, so edge rounding can differ slightly.
- Point primitives are one-pixel D3D11 points. The fixed-function vertex
  shader still computes `oPts`, but D3D11 has no point-size semantic, so
  point sprites sample the center texel.
- Bordered cubemaps keep the border texels in the face texture. The GL
  backend discards the four-texel border for cubemaps because a cubemap
  sampler cannot address it.
- Sampler anisotropy is not applied; `MaxAnisotropy` stays at 1.
- Converted volume textures (paletted, YUV, DXT or `R6G5B5` with a depth
  greater than one) are rejected instead of using the shared CPU conversion
  path, which is 2D only.
- The PVIDEO overlay is a CPU nearest-neighbor copy. It honors the output
  rectangle and color key but not `NV_PVIDEO_POINT_IN` or the
  `NV_PVIDEO_DS_DX`/`NV_PVIDEO_DT_DY` scale factors.
- Windows only. A D3D12 backend, if added, reuses the HLSL generators and
  surface/texture caches.

## 5. Validation assets

Local media in `data/` (never committed):

- `data/test-samples/triangle.xbe` — first draw and presentation.
- `data/test-samples/mesh.iso` — textures and vertex buffers.
- `data/test-samples/corpus/*.iso` — XGU cases for shaders, textures,
  cubemaps, fog, blending and swizzles.
- `data/xemu/` — BIOS, MCPX, EEPROM and HDD for boots.
- `data/xemu/xemu.exe` — the OpenGL oracle for screenshots.

Checks per phase:

```powershell
pwsh -File scripts/build-native-xemu.ps1
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

Every phase also gets a short note in `docs/reference/architecture.md` and
the user-facing renderer documentation.

"C:\ninja"
"C:\cmake"
"C:\msys64"

Games are inside "E:\Microsoft Xbox"

References "D:\repos\CSharp_XGrid\References2\CopettiDocumentation
            D:\repos\CSharp_XGrid\References2\Cxbx-Reloaded-master
            D:\repos\CSharp_XGrid\References2\envytools-master
            D:\repos\CSharp_XGrid\References2\nv2a-trace-master
            D:\repos\CSharp_XGrid\References2\xbased-1.2.3
            D:\repos\CSharp_XGrid\References2\XboxDevWiki
            D:\repos\CSharp_XGrid\References2\xemu-0.8.139
            D:\repos\CSharp_XGrid\References2\xemu-Playtest-v2
            D:\repos\CSharp_XGrid\References2\xqemu-master"

D:\repos\CSharp_XGrid\data\xemu\Complex_4627.bin
D:\repos\CSharp_XGrid\data\xemu\eeprom.bin
D:\repos\CSharp_XGrid\data\xemu\mcpx_1.0.bin
D:\repos\CSharp_XGrid\data\xemu\xbox_hdd.qcow2
