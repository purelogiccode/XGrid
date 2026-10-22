# Corpus matrix: the toolkit and every current row (Track E)

Status: created 2026-10-22 (session 29, decisions §228/§229). This is the
working card for the corpus: what is staged, what each row measures, and what is
blocked. `docs/compatibility.md` stays the authoritative matrix (every anchor and
command lives there); this card is the orientation layer, kept in sync with it.

## Toolkit status (H1, 2026-10-22)

| piece | state |
|---|---|
| nxdk toolchain | **present** in MSYS2 (`C:\msys64`: clang, lld, llvm-dlltool, make, bison, flex, cmake) |
| nxdk checkout | `D:\nxdk` at `a6c31ee` |
| builder | `scripts/build-nxdk-samples.ps1 -Samples @("usb")` (a quoted comma list binds as ONE name) |
| harness | `scripts/boot-to-first-frame.ps1 -Sample <name> [-Steps N] [-Gpu sw\|gl] [-GpuReplace] [-Ffp] [-TexFilter registers]` |
| frame tools | `scripts/frame-stat.py` (colours/black/mean), `scripts/frame-oracle.py` (phase sweep vs a reference png) |
| gate scripts | `scripts/gpu-slice1-golden.ps1 -Hdd wt-data\chain-199l\hdd.qcow2`, `scripts/gpu-slice2-triangle-golden.ps1 -Hdd ...`, `scripts/ktest-smoke.ps1 -SkipXemu` |

## Rows (anchor, verdict, file)

| row | anchor | verdict / what it proves | note |
|---|---|---|---|
| dashboard (EvoX XBE on HDD) | 1B, pad script | **gate**: menu renders, 686 flips, frame `4EF1FBF6…` | `compatibility.md` |
| triangle (nxdk) | 1B, HDD+DVD | **gate**: 68 flips, frame `A1740DE0…` | `compatibility.md` |
| kernel-test-suite | 792,800,535 steps | **gate**: 378/378 tests, log byte-identical | `ktest-smoke.ps1` |
| nxdk mesh | 1B | renders its own lit/textured scene | `compatibility.md` |
| nxdk gamma / gamma-fade / hello / led | 1B | direct-framebuffer output on the display export only (gap 1) | `compatibility.md` |
| nxdk xaudio | 1B | APU streams (`voiceOff=1`), no host sink | `compatibility.md` |
| nxdk usb | 1B | **new (§229)**: guest USB stack enumerates the hub, prints VID/PID on the display export | `nxdk-usb-sample.md` |
| xgu vp_lighting | 3B | renders; TEXFILTER parity sw-vs-gl within 1 step | `computed-mode-coverage.md` |
| xgu alpha_blending | 4.6B | renders; oracle match once phase-matched | `compatibility.md` |
| xgu bump_cubemap_reflect | **4.4B** (dots) / 3.6B (panel) | **closed 2026-10-22 (§236):** GL replace == software within 2 px of 307,200 at both anchors (a cube stage is a decoded host 2D-array texture); sw 19,042 / 21,041 colours | `computed-mode-coverage.md`, §228 |
| xgu bumpenv_ripple | 5.0-5.6B | renders black - gap 10 (app state) | `compatibility.md` |
| xgu swizzle_texture | 3B | renders; samples `0x3A` with zero misses | `compatibility.md` |
| xgu distance_fog_ffp / spheremap_ffp | 3B + `-Ffp` | fixed-function scenes render (flat without the flag) | `compatibility.md` |

## Blocked / filed

- **USB disconnect row**: `nxdk/usb` prints only on connect; needs a hot-plug
  hook on `UsbHub` (§229 filed).
- **`bumpenv_ripple` state**: needs the gap-9 second-emulator frame oracle
  (`scripts/xemu-screendump.ps1`; the QMP stdio read loop is the open piece).
- **Mipmapped / DXT / paletted textures**: no staged sample uses them; a row
  needs a sample that does (nxdk SDL samples build, but none of them exercise
  NV2A mip selection).

## How to add a row (checklist)

1. Stage bootable media: `scripts/build-nxdk-samples.ps1 -Samples @("<name>")`
   (nxdk) or build the ISO from the sample's own Makefile (xgu examples).
2. Run the harness at a phase where the sample's own scene is up; record
   `BTFF` + the JSON in `wt-data/btff/`.
3. Confirm the phase with `--gpu-time` (`tm=`/`reach[...]`) and with
   `--frame-display` when the sample writes the scanout (`debugPrint`).
4. Add the row to `docs/compatibility.md` (command, step anchor, SHAs, verdict);
   add a phase-anchor line to `corpus-phase-anchors.md` if the sample animates.
5. A fidelity claim needs the histogram (`usage-histogram.md`) or the row itself;
   otherwise it is a rendering claim without evidence.
