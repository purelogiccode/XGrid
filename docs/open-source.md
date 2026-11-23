# Open-source status and obligations

Status: **2026-11-22.** This page answers one question: *is XGrid an open-source
project, and what must be true for the full xemu port to keep it that way?*

## 1. Licence: GPL-2.0 (confirmed)

- `COPYING` is the unmodified **GNU General Public License, version 2**.
- `LICENSE` is the project notice: GPL-2.0, no warranty, ported-file rule,
  LGPL-2.0 §3 election for the LGPL-2.0-or-later files.
- GPL-2.0 is an OSI-approved open-source licence and grants the four freedoms
  (run, study, redistribute, improve) to every recipient of the source or a
  binary.
- The full port does not change the licence: xemu is GPL-2.0 (per-file
  LGPL-2+/MIT/GPL-2+) and QEMU is GPL-2.0, the same licence family. A port
  keeps each file's original notice and adds `SPDX-License-Identifier` plus
  `Ported from <project> <version> <path>` (`docs/porting-policy.md`).
- The **maintained xemu core** (the pinned xemu 0.8.139 baseline plus
  `native/xgrid-core` and `native/xemu-patches/`) is GPL-2.0. The release
  ships its corresponding source or a written offer, the patch set and the
  notices; `THIRD_PARTY_NOTICES.md` records the component
  (`docs/porting-policy.md` §2c). There is no relicense.
- `THIRD_PARTY_NOTICES.md` records every reference project, every ported module
  and every redistributed package; `docs/research-notes/sources.md` is the
  development-side inventory.

**Conclusion: the code is open source by licence.** Every file that ships is
GPL-2.0-compatible, and the obligations are already mechanised by
`scripts/package-verify.ps1` (license set in every archive, notices coverage for
every `Ported from` file).

## 2. Publication: private by decision until a game is playable

The licence is open source; the **repository is deliberately private**.

| Fact | Current state |
|---|---|
| Remote | `https://github.com/purelogiccode/XGrid` |
| Visibility | **private by operator decision (2026-11-22): the repository stays private because no game is playable yet** |
| Playability | 007 reaches its own scene and takes scripted input, but runs at **0.11x guest time** (12.85 s per 1B steps) and its audio is an offline WAV; the AC'97 live path is missing. The strict bar is `ImplementationPlan.md` E1 section 1 |
| CI | disabled: every workflow is `workflow_dispatch` only (GitHub Free private repository) |
| Releases | `scripts/package.ps1` produces portable archives locally; no public release exists |
| Source distribution | the source tree is the distribution vehicle; nothing is sold or downloaded publicly |

A private repository that only its owner can read is not "open source" in the
practical sense, even when it carries a GPL-2.0 file: the freedoms exist only
once the source (or a binary) reaches a recipient. Nothing is mislicensed; the
project is simply not published, and the operator has made publication
conditional on a playable game.

### 2.1 Publication gate (the trigger)

**Publish only after at least one game is playable (P1)**
(`ImplementationPlan.md` E1 section 1): own render, live input, **>= 0.5x guest
time**, 10 minutes, save/load, xemu frame policy, **live host audio** and the
gates exact. Until that happens, the checklist below is preparation, not a
schedule; do not publish a build that cannot be played.

### 2.2 Publish checklist (run when the trigger fires)

- [ ] Make the GitHub repository **public** (or mirror it to a public host) and
      keep the full history, including this file.
- [ ] Keep `COPYING`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `README.md` and
      `docs/` in the public tree (they are already tracked).
- [ ] Re-enable the workflow triggers (each workflow has its commented-in
      trigger block; `docs/ci.md`) or run the gates from a public CI.
- [ ] Publish at least one tagged release with the archives **and** the matching
      source tag; GPL-2 §3 then needs no written offer because the source is
      distributed alongside the binaries.
- [ ] Keep firmware (`BIOS`, MCPX ROM, EEPROM) and game data **out** of the
      repository and every archive; users supply them. This is already enforced
      by `scripts/package.ps1` and the licensing review.
- [ ] Keep the "not affiliated with Microsoft" notice in `README.md` (Xbox is a
      Microsoft trademark; the project only claims compatibility).
- [ ] If any binary is ever offered from a page that does not carry the source,
      add the GPL-2 §3 written offer valid for three years
      (`docs/release-licensing-review.md` §2).

## 3. Obligations that the full port increases

A full port multiplies the number of ported files. The following stay
non-negotiable:

1. **Notices per file.** Every ported file keeps its copyright, SPDX identifier
   and `Ported from` line. A file that is GPL-2.0-only stays GPL-2.0-only; a
   GPL-2.0-or-later file is included under GPL-2.0; LGPL-2.0-or-later files are
   included under GPL-2.0 through LGPL-2.0 §3.
2. **No relicensing.** The combined work is GPL-2.0. Do not add a EULA, DRM,
   field-of-use limit or activation to a distributed build.
3. **No native emulator embedding.** The port stays managed C#; OpenGL, XInput,
   host audio and Silk.NET are host APIs, not embedded GPL-incompatible code.
4. **Complete corresponding source.** Every published binary must have the
   matching source tag and the exact revision in `package-manifest.json`.
5. **Provenance coverage is a gate, not a habit.** `scripts/package-verify.ps1`
   fails when a `src/**` file declares a port without the SPDX line or the
   notices row; `scripts/port-manifest.ps1 -Check` (XFP0) adds the ledger check.
6. **No firmware, no games, no reference tree.** `References2/` is read-only,
   never shipped, and never copied into `src/`; only the ported algorithm is.

## 4. Dependency licences (unchanged by the port)

Runtime: XISOSharp, CHDSharp, Silk.NET.OpenGL, **NAudio** (live WASAPI sink),
Avalonia, CommunityToolkit.Mvvm, Iced - all MIT. Build/test only: xunit,
Microsoft.NET.Test.Sdk, coverlet, Meziantou.Analyzer, Roslynator.Analyzers,
BenchmarkDotNet - MIT/Apache-2.0 and never linked into the shipped emulator.
`docs/release-licensing-review.md` is the binding review;
`THIRD_PARTY_NOTICES.md` is the inventory.

## 5. Verdict

- **Open-source licence:** yes, GPL-2.0, complete and compatible.
- **Open-source publication:** no, by operator decision: the repository stays
  private until a game is playable (0.11x today). The trigger and checklist in
  §2 are the remaining work.
- **Full port:** lawful, and it strengthens the open-source case: every ported
  module is recorded, every derivative keeps its notice, and the complete
  corresponding source is the repository itself.
