# Copetti — "Xbox Architecture: A Practical Analysis"

Reference: `References2/CopettiDocumentation/` (`xbox-architecture.md` + `xbox-architecture.html` +
`images/`). Upstream: https://www.copetti.org/writings/consoles/xbox/ (article source:
https://github.com/flipacholas/Architecture-of-consoles).
License: **CC BY 4.0** — free for commercial use, but attribution is mandatory: cite
"Rodrigo Copetti — Xbox Architecture: A Practical Analysis" with the CC BY 4.0 link and
follow the article's "Copyright and permissions" guidelines if any text/figure is reused in
XGrid docs.

## What it is

A long-form (567-line markdown) independent essay on the whole console — the only reference
in `References2/` that explains *why* the machine is shaped the way it is rather than listing
registers. Sections:

- **CPU** — Pentium III (P6 lineage) core, ISA/microarchitecture, caches; motherboard
  architecture; **memory layout** (64 MiB DDR SDRAM, unified memory architecture / UMA).
- **Graphics** — command flow, vertex stage, pixel stage, post-processing; programmability;
  "The Xbox's frame" (framebuffer/bandwidth choices). Notes NV2A specifics such as the four
  texture shaders and **Z-buffer compression to 1/4 size** for bandwidth.
- **Audio** — AC'97 + APU split (the two devices XGrid added in §33).
- **I/O** — external interfaces, internal interfaces, the controller (XID path) and the
  daughterboard adapter — background for §69's hub topology.
- **Operating System** — boot process, the "green screen" (dashboard), interactive shell,
  updatability.
- **Games** — XDK / **nxdk** / OpenXDK lineage (relevant to the nxdk `triangle.xbe`
  bring-up in `data/test-samples/`), network service.
- **Anti-piracy** — DVD copy protection, bootstrap search, permanent unlock, modchips
  (context for the BIOS/boot-auth behavior).
- **Sources / Changelog** — curated further reading.

## How to use it

1. **Onboarding and docs.** First read for a new contributor (or an agent) before touching a
   subsystem: it gives the mental model that register-level references assume.
2. **Design cross-checks.** Sanity-check XGrid's big-picture choices (UMA and who owns which
   memory window, APU/AC'97 split, controller path, boot chain) against an independent
   analysis, not just xemu's implementation.
3. **README/background material.** Any excerpt or diagram reused in project documentation
   must carry the attribution above.

## Limits

- Not a register-level oracle: no MMIO maps, no method tables, no struct offsets. For those
  use xemu / XboxDevWiki / envytools / Cxbx (see `sources.md` subsystem map).
- Verify any hardware claim against XboxDevWiki or xemu before acting on it; treat the essay
  as orientation, not ground truth.
