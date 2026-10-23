# Release licensing review (R1.1, v0.1.0)

Scope: the artifacts produced by `scripts/package.ps1` (XGrid CLI and Avalonia
frontend, self-contained, `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) and the
source tree they are built from. Reviewed 2026-10-23 against GPL-2.0 and the
third-party inventory in `THIRD_PARTY_NOTICES.md`.

**Conclusion: distributable.** The combined work is GPL-2.0, every reference
project and redistributed package is GPL-2.0-compatible, and every package
carries the complete license set. The obligations that need an operator action
are the source offer in §3 and the macOS notarization note in §6.

## 1. What is distributed

| Artifact | Contents |
|---|---|
| `XGrid-cli-<rid>.zip/.tar.gz` | `XGrid.Cli[.exe]` (single file, self-contained), `LICENSE`, `COPYING`, `THIRD_PARTY_NOTICES.md`, `README.md`, `RELEASE-NOTES.md`, `VERSION` |
| `XGrid-frontend-<rid>.zip/.tar.gz` | `XGrid.Frontend.Avalonia[.exe]` plus the same seven files |
| Release tree | The archives, `SHA256SUMS-<rid>.txt`, `SHA256SUMS.txt`, `package-manifest.json` (version, commit, per-archive SHA-256) |

Nothing else ships. In particular the packages contain **no** Xbox firmware
(BIOS/MCPX/EEPROM), **no** game or disc data, **no** `References2/` tree, **no**
`data/` or test media, and **no** code from `tools/dsp-oracle` (it compiles the
reference DSP interpreter at development time and is deliberately outside
`XGrid.sln`). The emulator is full managed C#; no QEMU/xemu native library is
linked or bundled.

## 2. GPL-2.0 obligations

| Obligation | How it is met |
|---|---|
| GPL-2 §1: ship the license and keep the notices | `COPYING` (full GPL-2.0 text) and `LICENSE` (project notice + warranty disclaimer) are in every archive; ported files keep their original copyright, SPDX identifier and `Ported from`/`Derived from` line; `THIRD_PARTY_NOTICES.md` records every reference project, every ported module and every redistributed package. |
| GPL-2 §2: modified sources under the GPL | The whole tree is GPL-2.0 (`COPYING`); ported files are GPL-2.0-or-later or LGPL-2.0-or-later, both compatible with a GPL-2.0 combined work. |
| GPL-2 §3: binary distribution needs the source | Binaries are published next to the tagged source tree; the tag is the complete corresponding source (no generated code is shipped without it). If a binary is ever offered from a page that does not carry the source, §3 requires either the source alongside it or a written offer valid for three years - **operator action, see §6**. |
| GPL-2 §6 / no additional restrictions | No EULA, DRM, field-of-use or activation is added to the packages; nothing restricts reverse engineering for debugging modifications. |
| LGPL-2.0 §3 (files that are LGPL-2.0-or-later) | The project exercises the LGPL-2.0 §3 option to distribute the combined work under GPL-2.0; `LICENSE` states it and each LGPL file keeps its LGPL notice. Because the LGPL code is compiled into the program, LGPL §6 relinking does not apply. |
| Attribution for non-GPL references | MIT references (envytools, nv2a-trace, XISOSharp, CHDSharp, Silk.NET, Avalonia, CommunityToolkit.Mvvm, Iced) are credited in `THIRD_PARTY_NOTICES.md`; their notices are retained in the package sources. |

## 3. Redistributed dependencies

Runtime (linked into the shipped binaries), all MIT: XISOSharp, CHDSharp,
Silk.NET.OpenGL (a binding to the host's OpenGL driver, which is not shipped),
Avalonia Desktop/Fluent/Inter and its native Skia/HarfBuzz assets,
CommunityToolkit.Mvvm, Iced. Build- and test-only (not linked): xunit
(Apache-2.0), Microsoft.NET.Test.Sdk (MIT), coverlet (MIT), Meziantou.Analyzer
(MIT), Roslynator.Analyzers (Apache-2.0), BenchmarkDotNet (MIT). No GPL
incompatible license is linked; the Apache-2.0 packages are tooling and are not
part of the distributed binaries.

## 4. Reference projects

xemu 0.8.139 (primary; GPL-2.0 overall, per-file LGPL-2+/GPL-2+/MIT), xemu
0.8.137/0.8.136 (prior snapshots), the QEMU 10.2.0 base (GPL-2.0), Cxbx-Reloaded
and xqemu (GPL-2.0), envytools and nv2a-trace (MIT). All are recorded with
upstream URLs and per-module provenance in `THIRD_PARTY_NOTICES.md` and
`docs/research-notes/sources.md`.

Two references are facts-only and must stay that way:

- **XboxDevWiki** declares no license. Only facts are extracted (register
  layouts, boot order); no page text is copied into the product.
- **Copetti, "Xbox Architecture"** is CC BY 4.0, which requires attribution if
  content is reused. It is used for orientation only and nothing is copied; if
  that changes, the attribution line must be added to the notices.

The provenance headers are the review's evidence. A file is treated as ported
when its header says `Ported from` or `Derived from`; every other source file is
an original implementation of documented behaviour (the project's policy before
2026-10-23 was facts-only, which is why the pre-porting device models carry
citations but no port notice). `docs/porting-policy.md` requires a header and a
notices row for every new port, and `scripts/package-verify.ps1` fails the build
when either is missing.

## 5. Findings and resolutions

| id | Finding | Resolution |
|---|---|---|
| F1 | The dormant `release.yml` publish job copied only `LICENSE` and `README.md`, and `ci.yml`'s package job did the same. A GPL-2.0 binary without `COPYING` and the third-party notices is an incomplete distribution. | `scripts/package.ps1` ships `COPYING`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `RELEASE-NOTES.md`, `README.md` and `VERSION`; both workflows now use that one recipe (`release.yml` for a tag, `ci.yml`'s package job per RID). `scripts/package-verify.ps1` fails an archive that misses any of them. |
| F2 | The product version existed twice: `Program.cs` printed `0.1.0` while every assembly was `1.0.0`, and the local recipe produced no `VERSION`, manifest or checksums. | One version in `Directory.Build.props` (`<Version>`), the CLI reads its assembly version, and every package writes `VERSION`, per-RID and combined SHA-256 lists and `package-manifest.json`. The verifier cross-checks all four. |
| F3 | Six NV2A files are transcribed from xemu shader/`pgraph.c` sources (`PshCombiner`, `VshProgram`, `VshProgramPlan`, `FixedFunctionVertex`, `FixedFunctionVertexState`, `SpecularPowerCurve`) but carried no SPDX line, no original copyright and no porting-log row. | Headers and `THIRD_PARTY_NOTICES.md` rows added (GPL-2.0-or-later for the `psh.c`/`vsh-prog.c` derivatives, LGPL-2.0-or-later for the `vsh-ff.c`/`pgraph.c` derivatives, with the Cxbx/Xeon/Dxbx upstream copyrights recorded). The verifier now fails any `src/**.cs` that declares `// Ported from` or `// Derived from` without an SPDX line and a notices row. |
| F4 | `vsh-ff.c`, `psh.c` and `vsh-prog.c` differ between 0.8.137 and 0.8.139, so a bare "0.8.139" citation for the older transcriptions would be imprecise. | The three 0.8.137-era headers cite 0.8.137 and state that 0.8.139 has no semantic delta for that file, referencing `docs/research-notes/xemu-0.8.139.md`. The DSP port cites 0.8.139, which is the version it was taken from. |
| F5 | No OS installer (MSI/`pkg`/`dmg`) is produced, although the task title says "installer". | Scope decision: v0.1.0 ships portable, self-contained archives. XGrid needs no install-time state (settings and EEPROM live in the per-user data directory), so an installer adds signing/notarization work without changing any obligation in this review. Revisit when a store or signed-installer channel is chosen. |
| F6 | macOS builds are ad-hoc signed at most, and no Apple Developer ID is available, so Gatekeeper quarantines a downloaded `.tar.gz`. | Documented in the release notes with the exact `xattr -dr com.apple.quarantine` command; the binaries are notarized only once an operator supplies a certificate. Windows SmartScreen shows the same uninformed warning for an unsigned `.exe`. |

## 6. Operator actions before a public release

1. Tag the reviewed commit (`v0.1.0`) and make the tag's source tree available
   from the same place as the archives (GPL-2 §3, §1).
2. Decide the distribution channel. A store page that does not carry the source
   requires the written-offer text in `LICENSE` or a source link beside every
   download.
3. Optional: Apple Developer ID + notarization for the macOS archives and an
   Authenticode certificate for Windows. Neither changes the license review.

## 7. Verification

```powershell
pwsh -File scripts/package.ps1                      # publish, archive, checksums, manifest
pwsh -File scripts/package-verify.ps1               # license + integrity acceptance gate
```

`package-verify.ps1` checks that `COPYING` is the GPL-2.0 text, that `LICENSE`
and the notices are present in every archive, that every ported source file is
in the notices, that no archive contains symbols or firmware/game data, that
every archive matches its SHA-256 sum and the manifest, and (on Windows) that
the packaged CLI reports the version in its `VERSION` file. The review is
re-run whenever a reference is added, a port lands, or a redistributed package
changes.
