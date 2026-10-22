# Continuous integration

Every change to `master` (and every pull request) runs the same quality gates.
Releases are produced from tags. No CI job needs game data, firmware, or the
`References2/` oracle tree: the test suite is self-contained.

## Prerequisites

- **GitHub-hosted runners need Actions minutes.** This repository is private, so
  standard runners are billed against the account's included minutes. If the
  account has no payment method / spending limit configured, jobs fail at
  startup with *"The job was not started because recent account payments have
  failed or your spending limit needs to be increased."* Fix it in
  **Settings → Billing & plans**, or make the repository public (standard
  runners are free for public repositories), or point the workflows at
  self-hosted runners.
- **Code scanning, dependency review and build attestations need GitHub
  Advanced Security on a private repository** (they are free on public
  repositories). The InspectCode SARIF upload is best-effort
  (`continue-on-error`), so its job still gates on findings without GHAS.
- If the project stays private without GHAS, disable `codeql.yml` and
  `dependency-review.yml` (or switch them to `workflow_dispatch`) until the
  repository becomes public.

## Workflows

| Workflow | Trigger | What it proves |
|---|---|---|
| `ci.yml` / **build-test** | push, PR, merge queue, nightly, manual | Release build with warnings-as-errors and the full test suite on **Windows, GNU/Linux and macOS** (1812 tests). GNU/Linux also collects coverage. |
| `ci.yml` / **determinism** | same | Two clean builds produce byte-identical assemblies (deterministic build). |
| `ci.yml` / **format** | same | `dotnet format --verify-no-changes`. |
| `ci.yml` / **inspectcode** | same | ReSharper InspectCode at `WARNING` severity reports **zero** findings; SARIF goes to the Security tab. |
| `ci.yml` / **coverage** | same | Merges all 14 Cobertura reports and enforces **60% line / 50% branch** floors. |
| `ci.yml` / **security-audit** | same | `dotnet list package --vulnerable --include-transitive` finds no advisories. |
| `ci.yml` / **package** | push to `master` | Self-contained CLI + frontend for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`, with checksums. |
| `codeql.yml` | push, PR, weekly | CodeQL `security-and-quality` analysis for C#. |
| `dependency-review.yml` | PR | Fails a PR that introduces a high-severity vulnerable dependency. |
| `release.yml` | tag `v*` | Re-verifies all three OSes, publishes every RID, generates an SPDX SBOM, attests build provenance, writes checksums and creates the GitHub release. |
| `dependabot.yml` | weekly | Grouped NuGet and GitHub Actions update PRs. |

## Gate details

- **TreatWarningsAsErrors** is on in `Directory.Build.props`; the build job is
  the first gate. `CI=true` also enables `ContinuousIntegrationBuild` and
  `DeterministicSourcePaths` (`Directory.Build.targets`).
- **Tests** run with `--blame-hang --blame-hang-timeout 20m`; TRX files and
  coverage attach to the workflow run as artifacts.
- **InspectCode** is pinned (`JetBrains.ReSharper.GlobalTools 2026.2.3.1`) and
  uses `XGrid.sln.DotSettings`, which documents the two deliberate inspection
  exemptions (bit-exact float comparisons and Xbox-domain naming).
- **Coverage** uses the `coverlet.collector` already referenced by every test
  project; `dotnet-reportgenerator-globaltool 5.5.11` merges the reports.
  Thresholds live in `.github/scripts/coverage_gate.py` (60/50). Raise them as
  the suite grows.
- **No external services**: no Codecov/Sonar token, no hosted reporting. The
  coverage HTML report and step summary are uploaded to the run.

## Releases

```bash
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` then:

1. verifies the tagged tree on Windows, Linux and macOS;
2. publishes `XGrid.Cli` and `XGrid.Frontend.Avalonia` self-contained for
   `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64`;
3. strips symbols/docs and adds `LICENSE`, `README.md` and `VERSION`;
4. archives (`.zip` on Windows, `.tar.gz` elsewhere) and writes per-RID
   `SHA256SUMS-*.txt`;
5. generates an SPDX JSON SBOM with `anchore/sbom-action`;
6. attests build provenance with `actions/attest-build-provenance`;
7. creates the release with generated notes and all assets
   (re-running the workflow uploads with `--clobber`).

The `release` job uses the `release` environment. Configure required reviewers
on that environment in the repository settings if releases should be approved.

## Local equivalents

```powershell
dotnet restore XGrid.sln
dotnet build   XGrid.sln -c Release --no-restore
dotnet test    XGrid.sln -c Release --no-build
dotnet format  XGrid.sln --no-restore --verify-no-changes
dotnet list    XGrid.sln package --vulnerable --include-transitive
```

```bash
# InspectCode (same pinned tool as CI)
dotnet tool install --global JetBrains.ReSharper.GlobalTools --version 2026.2.3.1
jb inspectcode XGrid.sln --format=Sarif --output=inspectcode.sarif --severity=WARNING
python3 .github/scripts/inspectcode_gate.py inspectcode.sarif
```

## Not in CI (by design)

The media-dependent gates need files that are intentionally not committed:

- `xgrid boot --steps ...` goldens (`data/`, `wt-data/`), the ktest smoke,
  GPU slice goldens, and the compatibility corpus (`docs/compatibility.md`)
  need firmware/HDD/DVD images and `References2/`.
- Real Xbox titles are benchmark-only by project policy.

Run those on a self-hosted runner or a development machine; the expected
anchors and commands are in `docs/research-notes/next-session.md`. If a runner
has the media, add a separate workflow with `runs-on: [self-hosted, xbox-media]`
rather than weakening the hosted gates.

## Repository settings

Branch protection is intentionally **not** configured by these files because the
current workflow pushes directly to `master`. If the project moves to a
PR-only flow, enable these required status checks: `Build & test (ubuntu-latest)`,
`Build & test (windows-latest)`, `Build & test (macos-latest)`,
`Format verification`, `InspectCode (warnings)`, `Coverage report`,
`Package audit`, `Analyze C#` and `Review dependency changes`.
