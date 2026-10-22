# Continuous integration

> **CI is disabled (2026-10-22).** The workflows in `.github/workflows/` no longer
> run automatically. This repository is private and the account's **GitHub Free**
> plan has no GitHub Actions available for it, so every automatic trigger was
> removed and each workflow now starts only on `workflow_dispatch` (manual).
>
> **Nothing was deleted.** The workflow files, the gate scripts in
> `.github/scripts/`, the pinned tool versions and `dependabot.yml` are all still
> in the tree; re-enabling is one commented-in trigger block per workflow (see
> [Re-enabling CI](#re-enabling-ci)). Until then the gates are run **locally**
> with the commands in [Local equivalents](#local-equivalents) — they are exactly
> what the jobs do.

No CI job needs game data, firmware, or the `References2/` oracle tree: the test
suite is self-contained.

## Workflows (present, manual-only)

| Workflow | Used to trigger on | What it proves |
|---|---|---|
| `ci.yml` / **build-test** | push, PR, merge queue, nightly, manual | Release build with warnings-as-errors and the full test suite on **Windows, GNU/Linux and macOS** (1836 tests). GNU/Linux also collects coverage. |
| `ci.yml` / **determinism** | same | Two clean builds produce byte-identical assemblies (deterministic build). |
| `ci.yml` / **format** | same | `dotnet format --verify-no-changes`. |
| `ci.yml` / **inspectcode** | same | ReSharper InspectCode at `WARNING` severity reports **zero** findings; SARIF goes to the Security tab. |
| `ci.yml` / **coverage** | same | Merges all 14 Cobertura reports and enforces **60% line / 50% branch** floors. |
| `ci.yml` / **security-audit** | same | `dotnet list package --vulnerable --include-transitive` finds no advisories. |
| `ci.yml` / **package** | push to `master` | Self-contained CLI + frontend for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`, with checksums. Dormant while CI is disabled (its guard requires a push to `master`). |
| `codeql.yml` | push, PR, weekly | CodeQL `security-and-quality` analysis for C#. |
| `dependency-review.yml` | PR | Fails a PR that introduces a high-severity vulnerable dependency. PR-only, so it is not useful as a manual run. |
| `release.yml` | tag `v*` | Re-verifies all three OSes, publishes every RID, generates an SPDX SBOM, attests build provenance, writes checksums and creates the GitHub release. |
| `dependabot.yml` | weekly | Grouped NuGet and GitHub Actions update PRs. **Still active**: Dependabot is not Actions and needs no minutes, so its PRs keep arriving (without automated checks) while the workflows are manual-only. |

Every automatic trigger now sits in a commented `# --- Restore … ---` block at the
top of its workflow file, next to the replaced `on: workflow_dispatch`.

## Re-enabling CI

Prerequisites come first:

- **GitHub Actions minutes.** A private repository consumes included Actions
  minutes and needs a valid payment method / spending limit, otherwise hosted
  jobs fail at startup with *"The job was not started because recent account
  payments have failed or your spending limit needs to be increased."* Fix it in
  **Settings → Billing & plans**, or make the repository **public** (standard
  runners are free there).
- **GitHub Advanced Security** is required for CodeQL, dependency review, build
  attestations and the Security-tab SARIF upload on a **private** repository.
  They are free on public repositories. Without GHAS, `codeql.yml` and
  `dependency-review.yml` should stay disabled (or keep `continue-on-error`
  behavior in mind for the InspectCode SARIF upload).
- **Self-hosted runners** are an alternative to hosted minutes; point `runs-on:`
  at them, and give media-dependent gates their own workflow rather than
  weakening the hosted jobs (see [Not in CI](#not-in-ci-by-design)).

Then, per workflow file:

1. Open `.github/workflows/<name>.yml`.
2. Delete the placeholder trigger (`on:`, its `# Manual only …` comment and the
   `workflow_dispatch:` line).
3. Uncomment the `# --- Restore … ---` block (`push` / `pull_request` /
   `merge_group` / `schedule` for `ci.yml`; `push` / `pull_request` / `schedule`
   for `codeql.yml`; `pull_request` for `dependency-review.yml`;
   `push: tags: ["v*"]` for `release.yml`).
4. Re-add the required status checks listed under
   [Repository settings](#repository-settings).

No job, script, pinned tool version or threshold was changed for the disable, so
nothing else has to be reconciled.

## Gate details

- **TreatWarningsAsErrors** is on in `Directory.Build.props`; the build job is
  the first gate. `CI=true` also enables `ContinuousIntegrationBuild` and
  `DeterministicSourcePaths` (`Directory.Build.targets`) — set it locally when
  reproducing a CI build (`$env:CI = "true"`).
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

The gate scripts are ordinary command-line tools, so they run locally unchanged —
see below.

## Releases

`release.yml` publishes from a tag and is manual-only while CI is disabled.

**Preferred while disabled — publish locally** (the same commands the workflow
runs, per RID) and attach the archives to a `gh release create` yourself:

```powershell
$rid = "win-x64"
dotnet publish src/XGrid.Cli/XGrid.Cli.csproj -c Release -r $rid `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o "publish/cli/$rid"
dotnet publish src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj -c Release -r $rid `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o "publish/frontend/$rid"
```

Strip `*.pdb`/`*.xml`, copy `LICENSE` and `README.md` into each folder, write a
`VERSION` file, archive (`.zip` on Windows, `.tar.gz` elsewhere) and produce
`SHA256SUMS-<rid>.txt`. `release.yml` does exactly this for `win-x64`,
`linux-x64`, `osx-x64` and `osx-arm64`.

**After re-enabling** (restore the `push: tags: ["v*"]` trigger):

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

A **manual** run of `release.yml` also works while disabled, provided it is
dispatched with a `v*` tag selected as the ref (GitHub's *Run workflow* →
*Use workflow from* → tag): the release job uses `github.ref_name` as the tag, so
the tag must already exist.

The `release` job uses the `release` environment. Configure required reviewers
on that environment in the repository settings if releases should be approved.

## Local equivalents

These are the gates the workflows run; with CI disabled they are the only gates.
Run them from the repository root.

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

```bash
# Package audit with the CI's own parser
dotnet list XGrid.sln package --vulnerable --include-transitive --format json > package-audit.json
python3 .github/scripts/vulnerable_gate.py package-audit.json
```

```powershell
# Coverage floors (60% lines / 50% branches), same scripts as the CI jobs
dotnet test XGrid.sln -c Release --no-build --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.5.11
reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:coverage-report" "-reporttypes:TextSummary;Cobertura"
python3 .github/scripts/coverage_gate.py coverage-report/Summary.txt 60 50
```

The determinism job is a pair of clean builds with a SHA-256 comparison; the
`find | sha256sum`/`diff` steps in `ci.yml` reproduce it on any Unix-like shell
(Git Bash / WSL / MSYS2 on Windows).

The media-dependent gates below are **not** part of these commands and remain
manual on a development machine.

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
PR-only flow, enable these required status checks **after re-enabling CI**:
`Build & test (ubuntu-latest)`, `Build & test (windows-latest)`,
`Build & test (macos-latest)`, `Format verification`, `InspectCode (warnings)`,
`Coverage report`, `Package audit`, `Analyze C#` and `Review dependency changes`.

While CI is disabled, do not configure those checks as required: no workflow
reports them, so every pull request would stay blocked. The equivalent local
commands in [Local equivalents](#local-equivalents) are the gate.
