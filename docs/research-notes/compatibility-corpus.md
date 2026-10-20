# Compatibility corpus — Track E plan

Status: **chartered 2026-10-20** (course correction; decisions §203). Tracking
document: `docs/compatibility.md`. Priority order: `docs/development-direction.md`.

## Why this track exists

XGrid's fidelity work is steered by three programs: the EvoX dashboard, the nxdk
triangle, and the kernel test suite. That was the right anti-distraction rule
while the core was being built, but it cannot steer an xemu-like emulator:
xemu is driven by a compatibility list, and a list is a corpus. Without broader
samples, every NV2A/APU decision is a guess about what titles do, and the
project plateaus as a very accurate demo.

The corpus track does not change the gates. `data/test-samples/` and the 1B
goldens stay the currency. It changes **what fidelity work is allowed to be
prioritized on**.

## Deliverables (one session)

1. **`docs/compatibility.md`** — the matrix. Each row: sample, provenance, boot
   stage, first frame, render evidence, audio, known gaps, step anchor, and the
   exact command that reproduces it.
2. **Boot-to-first-frame harness** — a script (not a new test project) that
   takes a workload, runs it headless to a fixed step count, and records:
   steps, `FramesPresented`, first-frame SHA (if exported), the NV2A counter
   line, and the wall. One invocation per row; output appended to the matrix
   notes.
3. **Sample wish list** — the sources below, with the dependency that blocks
   each (toolchain, download, build) stated as a dependency, not an excuse.
4. **A prioritization note** — which three rows the next fidelity slice should
   serve and why.

## Exit criterion (M-B in the direction doc)

- ≥ 8 rows in `docs/compatibility.md`.
- At least two rows beyond the dashboard / triangle / ktest, covering:
  (a) textured 3D (ideally with mipmaps and a nontrivial shader),
  (b) an audio or DVD/HDD streaming path.
- Every row reproducible from its command on a clean tree.
- A fidelity proposal can cite "row X + histogram Y" without hand-waving.

Until then, NV2A/APU polish beyond the existing rows stays frozen.

## Candidate sources (cheapest first)

1. **nxdk samples already in the repo** — `nxdk-mesh` (textured mesh, VS/PS
   shaders), `nxdk-hello`, `nxdk-led`. The blocker is the nxdk toolchain on the
   host (`bin/activate` + `make`), not emulator code. This is the highest-value
   dependency to clear.
2. **xgu examples** (`Voxel9/xbox-xgu-examples`) — spinning cubes, textures;
   needs nxdk next to it.
3. **nxdk_vsh_tests** (`abaire/nxdk_vsh_tests`) — vertex-shader golden data; a
   comparison corpus rather than a boot row, useful for VSH regressions.
4. **OpenXDK framebuffer demos** — non-D3D raw framebuffer bring-up; tests the
   surface write path without the shader stack.
5. **Homebrew demos / emulator test XBEs** — only with a clear license and a
   recorded provenance. No commercial ISOs.

Real Xbox ISOs at `H:\Tests\XBOXTest` stay **benchmark-only**: they may measure
wall/throughput and xemu-class comparisons, never gates and never fidelity work.

## Guardrails

- **One quirk at a time.** A title-specific workaround is allowed only when it
  is isolated, documented, and does not replace the hardware behavior. The 007
  EOS quirk is the historical precedent, not a pattern to extend.
- **No real-game patches in core paths.** Prefer the hardware model; if the
  behavior is genuinely title-specific, gate it by title ID and record it.
- **The matrix is evidence, not a gate.** Do not add matrix runs to CI; the
  standing gates stay the 1B goldens + ktest smoke.
- **Do not let Track E block Track B.** The present path is the lead; the corpus
  is the steering signal. One session for the initial matrix, then it grows as
  rows are added.

## Relationship to the old sample rule

The 2026-10-19 rule ("no NV2A/APU fidelity polish until a `data/test-samples/`
histogram proves need") was correct while the core was under construction. It is
now replaced by: **a matrix row plus a histogram proves need**. The gates and the
real-content policy are unchanged.
