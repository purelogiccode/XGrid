# XGrid computed-mode coverage: the B3 acceptance row at a later phase

Status: **created 2026-10-22 (session 29, Track B/E).** Companion to
`compatibility.md` gap 12 and decisions §228. This note records the method and
the numbers behind the §228 landing; the matrix row lives in
`docs/compatibility.md`.

## Why the 3B anchor is not the B3 row

§227 concluded that `xgu bump_cubemap_reflect` "does not exercise the dot
modes at all" because its 3B combiner histogram read
`stages=1 control=00000001 texmodes=0000`. §228 found the opposite at 4.4B:
the same sample spends most of a frame in an **eight-stage program whose
stages 1-3 are DOT_PRODUCT / DOT_REFLECT_SPECULAR**, i.e. exactly the modes
B3 slice 1 (§226) put on the seam. What was wrong was the **anchor step**, not
the sample.

Measured with the new `reach[...]` summary on the combiner histogram (same run
shape: HDD + DVD, `--jit --jit-threshold 16 --jit-spin --jit-raw --gpu sw
--gpu-time`):

| steps | dot-mode program present? | texture slots read by the 8-stage program |
|---|---|---|
| 0.2B - 1.4B | no (the menu-like `stages=8` record is a different, non-sampled program) | - |
| 3B | no (the panel is `stages=1 tm=3332` with `reach[tx=0123]`) | 0,1,2,3 from ONE stage |
| 4.4B | **yes** - `stages=8 c=00011108 tm=0332 reach[tx=123]` (19.9M px) and `stages=7 c=00011107 tm=1332 reach[tx=0123]` (12.4M px) | 1,2,3 (stage 3 is REFLECT_SPEC) |

The 3B panel also draws through a cube **TEXFMT with zero BASE_SIZE**
(`03...`: BASE_SIZE 0, sampling disabled), which is why its texture counts are
zero on both renderers.

## What the phase shows on each renderer

At 4.4B (`--steps 4400000000`, BTFF-style, one run each):

| leg | last flipped frame | colours | black | mean (R,G,B) | GPU diag |
|---|---|---|---|---|---|
| `--gpu sw` | `36A4C2A8` | 18,980 | 0.0% | (57.4, 89.3, 34.2) | - |
| `--gpu gl --gpu-replace` (before §228) | `59F1AB97` | **4** | 34.4% | (2.6, 53.8, 16.4) | `unsupported=0 fatal=0 composedFrames=0 guestWordDraws=0` |
| `--gpu gl --gpu-replace` (after §228) | see §228 | - | - | - | `guestWordDraws=` (counted) |

The GPU image is the clear plus a **black panel**: the cube/dot stage samples
read a word pool that the draw's carried set did not contain, and the pool
reads 0 there. The software path is unaffected.

## Why the words were missing, and what §228 changed

`GlPgraphBackend.AppendCubeWords` filled the pool only from the seam's
carried set (`PgraphGuestTexelSet`) and returned false - declining the draw -
for any address the set did not enumerate. The seam's set is a bounded window
(32 entries, most-recent last) over the words the software sampler has touched
recently, not the authority: `AppendDependentInputWords` (the 16-bit HILO
input) has always fallen back to `TryReadDirectWord` (the same direct RAM lane
the texture fingerprint uses). §228 makes the cube faces use that lane too,
and adds `FindPooledWord` so two cube slots in one draw command do not
re-append the slot-0 face words the earlier slot already resolved.

The rationale is in `IPgraphBackend.PgraphGuestTexelSet`: the guest cannot
rerun a draw command, so the word at that address when the draw runs is the
word the hardware sampled. A decline remains the answer only when the address
is not readable at all.

## Which stage carries the flags (added 2026-10-22, §230)

`reach[...]` reports the union of the output-flag bits the writing stages pass
through; it cannot say *which* stage does, and a MUX/blue-to-alpha bit carried
by a stage whose write nothing reads is not exercised. The row now also prints
`flags@=<first>[-<last>]:<flags>,…` (runs of consecutive writing stages that
share a flag word). Measured on this row at 3.6B:

| record | `flags=` | `flags@=` |
|---|---|---|
| `stages=8 c=00011108 tm=0332` (19.9M px) | `C0` | `0:C0,1-7:00` |
| `stages=7 c=00011107 tm=1332` (12.4M px) | `C0` | `0:C0,1-6:00` |
| panel `stages=1 c=00000001 tm=11111C` (5.7M px) | `00` | `0:00` |

So the union came from **stage 0's** RGB word alone; stages 1-7 write only
no-op output ids. Nothing in the panel program's flags explains the black panel
- the leads stay texture content and the dot chain, plus the final-lerp
negation the shader still does not implement (gap 12).

## Method notes (for the next phase sweep)

- `scripts/boot-to-first-frame.ps1 -Sample xgu-bump_cubemap_reflect -Steps N`
  gives the `BTFF` row; `--gpu-time` (CLI) adds the combiner histogram rows with
  `tm=` and `reach[...]`.
- A single step count measures the phase, not the renderer: this sample
  ping-pongs between a logo panel, the reflection plane and an interstitial
  ("XXGU") phase. Compare at 4.4B (reflection) or 3.6B (`|dmean|` best vs the
  reference screenshot), never 3B.
- The probe logs and BMPs behind this note were written to `wt-data/probe/`
  (gitignored scratch).
