# Track E: corpus phase anchors - a sample is measured at the step where its scene runs

Status: created 2026-10-22 (session 29, decisions §228). `docs/compatibility.md`
carries the matrix; this note carries the rule and the per-sample anchors that
make a row comparable.

## The rule

A corpus row is only meaningful with its step count. Animated samples bounce
between phases, and a row measured in a phase that is not the one the row names
is a measurement of the wrong program:

| sample | phase anchor | what runs there | why it matters |
|---|---|---|---|
| `xgu bump_cubemap_reflect` | **4.4B** (reflection); 3.6B (panel, reference-matched) | 8-stage `tm=0332` + 7-stage `tm=1332` (dot/reflection modes) | 3B runs a one-stage panel only, so the previous session's "no dot program" verdict was an anchor error (§228) |
| `xgu alpha_blending` | **4.6B** | the fade at its comparable phase | the shader ping-pongs transparency every 100 frames (§208f) |
| `xgu vp_lighting` | 3B | its own scene, no fallback draws | the TEXFILTER parity row (§220c) |
| `xgu distance_fog_ffp` / `spheremap_ffp` | 3B with `-Ffp` | the fixed-function scene (§213/§215) | flag off renders the flat background |
| `xgu bumpenv_ripple` | 5.0-5.6B | its own scene | renders black (gap 10) |
| gate titles | 1B | menu / triangle | the gate anchors |

## Measure the phase before trusting a row

`--gpu-time` prints one `NV2A combiner cfg` line per distinct combiner state with
pixels and fills, and since §228 it also prints `tm=` (the four texture modes),
`sf=` (the final constant pair) and `reach[...]` (the read set: slots, writing
stages, sockets, constants, flags, lerp selects). Two checks first:

1. **Is the phase's program the one the row is about?** A `tm=` field answers it
   (`0000` = no texture; `0332` = PROJECT2D + CUBEMAP + dot + reflection).
2. **Does the exported frame come from that program?** `Frame` prints the
   presented surface address, the flip count and the CRTC; a frame whose
   `frames=` is the boot animation's count is the animation.

The mode histogram alone is not enough: a sample can program a state and never
draw with it, and `reach[...]` is what separates "programmed" from "evaluated"
(a stage whose output words write no register contributes nothing).

## Rules for new rows

- State the anchor in the row and in the command (`-Steps`).
- Compare software and GL legs **at the same anchor**; a GL leg at another phase
  measures a different program and is not a regression report.
- A frame-quality claim needs the export of that phase, not the last presented
  surface of a different one.
