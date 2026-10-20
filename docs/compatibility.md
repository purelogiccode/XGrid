# XGrid compatibility matrix (Track E)

Status: **starter, 2026-10-20.** This is the steering document for fidelity
work, not a gate document. A row is evidence from a recorded run; gates stay in
`docs/development-direction.md` §6 and `docs/research-notes/next-session.md`.

## Rules

- Every row names a **reproducible command** and a **step anchor**. A row without
  a command is a claim, not data.
- Rows do not gate merges. The gates are the accurate-mode 1B goldens and the
  ktest smoke; this matrix decides *what fidelity work is worth doing*.
- Real Xbox ISOs are **not** rows. They are benchmark-only (fast-mode wall /
  xemu comparison) per the real-content policy.
- Update a row in the same change that alters its behavior, with the commit and
  the measured evidence.
- A fidelity landing must cite the row it fixes and the histogram that proves
  the usage. No row and no histogram means no NV2A/APU fidelity work.

## Matrix

| Sample | Source / provenance | Boot | First frame | Render | Audio | Known gaps | Anchor |
|---|---|---|---|---|---|---|---|
| **EvoX dashboard** (`xboxdash.xbe` on HDD, softmod) | `data/xemu-0.8.136-windows-x86_64` boot media; XBE 770,048 B, entry `0x12A17` | yes — MCPX ROM → 1BL → 2BL → kernel → XBE | yes | colored menu (32,789 colors; 7,389 RGB-diff px vs neutral) | APU/ACI emulated, **no host sink** | submenu state-100 pixel-negative; NV2A method coverage partial; 686 flips/1B | fast 1B menu RAM `660CA11B…` / CPU `F5D61C94…`; accurate `D9C99A58…` / `3D2740B8…` |
| **triangle.xbe** (nxdk) | XboxDev ghidra-xbe test XBE; nxdk `f998bdc` | yes | yes | colored Gouraud triangle | n/a | none on this stream | accurate frame `A1740DE0…`; fast 1B RAM `F57934BF…` / CPU `CF79FC17…` |
| **kernel-test-suite-default.xbe** | Cxbx-Reloaded release `20260819191914-da80f1f` | yes | n/a (console output) | n/a | n/a | 378/378 execute; known deltas 319 `RtlWalkFrameChain`, 344 `XcVerifyPKCS1Signature`; xemu leg cannot bind its monitor socket on this host | XGrid leg **792,800,535** steps, suite log byte-identical; smoke 7/7 |
| **nxdk-mesh** | source in `data/test-samples/nxdk-mesh/` | **blocked** — nxdk toolchain not on host | — | expected textured 3D + shaders | — | build dependency, not an emulator gap | — |
| **nxdk-hello / nxdk-led** | source in `data/test-samples/` | **blocked** — toolchain | — | text / SMC LED | — | build dependency | — |
| **xgu examples** (spinning cubes, textures) | https://github.com/Voxel9/xbox-xgu-examples | not fetched | — | textured 3D | — | needs nxdk next to it | — |
| **nxdk_vsh_tests** (VS goldens) | https://github.com/abaire/nxdk_vsh_tests | not fetched | — | vertex-shader oracle data | — | fetch + compare, not a boot row | — |
| **OpenXDK demos** (framebuffer/XOR) | xbdev.net / OpenXDK | not fetched | — | raw framebuffer | — | candidate for non-D3D bring-up | — |

## Current coverage reading

- **Boot chain:** covered (dashboard + triangle + ktest all pass the real firmware
  path).
- **Kernel surface:** covered by ktest (378 tests).
- **3D pipeline:** thin — one static triangle plus the dashboard's menu stream.
  No textured mesh, no mipmaps, no multistream/DMA vertex, no animated title.
- **Texture formats / shader shapes:** whatever the dashboard's menu uses; the
  rest of the NV097 space is unobserved.
- **Audio:** emulated, but no streaming consumer and no host output.
- **Storage:** HDD boot + DVD/CHD boot; no per-title DVD streaming row.

The two "blocked" rows are the cheapest breadth win: the nxdk toolchain turns
four existing source samples into four rows.
