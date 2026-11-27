# xemu baseline differences

XGrid's core began as **xemu 0.8.139** and is now a custom enhanced and patched
core that evolves separately. The pristine baseline is
`References2/xemu-0.8.139`; the product source is `xemu/`. `References2/` is
read-only. This page is the historical baseline, not a commitment to track
upstream.

Full page: [XGrid core vs upstream xemu](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/upstream-differences.md)

## At a glance

| Area | Upstream | XGrid |
| --- | --- | --- |
| Process | Standalone `xemu` | Standalone **plus** `xgrid_core.dll` in-process |
| Shell integration | None | Embedding, 4-pad input sink, filter/overlay, screenshots, playback, disc requests |
| Media | ISO/XISO | ISO/XISO, CHD, CCI, ZArchive with XGD offset detection |
| Renderers | OpenGL, Vulkan, NULL | Plus Direct3D 11 preview |
| Input | SDL | Plus shell pads; Chihiro JVS, guns, cards |
| Speed | Upstream run loop | Pause/step and 1–8× fast-forward clock |
| Save states | Core snapshot UI | Shell slots + session state over ABI |
| Machines | Xbox | Plus Sega Chihiro |
| Audio | QEMU backends | Plus DirectSound GUIDs and `-audio-drivers` probe |

## Native additions

- `xemu/xgrid-core/abi/` — start/stop/join, playback, disc, screenshot and
  snapshot ABI.
- `xemu/xgrid-core/block/` — `xgridiso`, `xgridchd`, `xgridcci`, `xgridzar` and
  shared XDVDFS/XGD detection.
- `xemu/xgrid-core/display/` — core-side presentation filters, FPS overlay and
  the headless `XGDS` frame sink.
- `xemu/xgrid-core/input/` — four-port `XGDI` input sink.
- `xemu/xgrid-core/thirdparty/` — libchdr 0.3.0 and libCCI 0.1.
- `xemu/hw/xbox/chihiro/` — 34-file Chihiro port: machine, media board, DIMM,
  JVS, card readers, drive boards, netboard, 8051/MIPS/V850/PIC/AN2131/DES.
- `xemu/hw/xbox/nv2a/pgraph/d3d11/` and `hlsl/` — 24-file Direct3D 11 preview.
- `block/blkmemory.c` — Chihiro DIMM memory-backed block driver.

## Upstream files changed

The current patch record is `xemu/xemu-patches/0007`–`0019`:

| Patch | Area |
| --- | --- |
| 0007 | In-process host, thread stack, `sigsetjmp` shim, library mode, DLL |
| 0008 | Shell pads on every port the keyboard did not take |
| 0009 | Vblank fast-forward |
| 0010 | Core-side presentation filter and FPS overlay |
| 0011 | DXGI exit cleanup |
| 0012 | Guest virtual-clock scale |
| 0013 | Direct3D 11 renderer and shader cache |
| 0014 | Core-side screenshot request |
| 0015 | WGL/DXGI interop presentation |
| 0016 | Disc driver selection |
| 0017 | DirectSound device GUID and audio probe |
| 0018 | Sega Chihiro machine and cabinet devices |
| 0019 | Snapshot path skips non-snapshot devices |

Thirty-seven existing source/build files are changed, about +4,200/−100 lines.
`ui/xemu-input.c` carries the largest share (2,636 lines) because of Chihiro JVS,
gun, card and force-feedback input.

## What did not change

- The Xbox machine, QEMU TCG, MCPX/APU/ACI, IDE, USB, SMBus and retail boot chain
  remain upstream except for the Chihiro hooks.
- OpenGL and Vulkan keep their upstream paths; D3D11 is an additional preview.
- XBE, XDVDFS, FATX and the upstream snapshot format are unchanged.
- The standalone `xemu` still exists and uses its own UI.

## Baseline and verification

- `pwsh -File scripts/core-baseline.ps1` checks the ABI export baseline.
- `pwsh -File scripts/build-native-xemu.ps1` builds the Windows core and DLL.
- `bash scripts/build-native-xemu.sh` builds the Linux core.
- See [Core lineage and upstream](https://github.com/purelogiccode/XGrid/blob/master/docs/development/xemu-upstream.md).

Compare a file without touching either tree:

```powershell
git diff --no-index -- References2/xemu-0.8.139/hw/xbox/xbox.c xemu/hw/xbox/xbox.c
```
