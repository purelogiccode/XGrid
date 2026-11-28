# Quality gates

The local gates are the source of truth. CI runs the same commands on the
supported matrix.

## Local gates

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

- **Warnings are errors** (`TreatWarningsAsErrors`).
- Nullable is enabled; implicit usings are disabled.
- XML documentation is required for every public type and member, including the
  Avalonia shell; missing comments fail the build.
- The build is deterministic.
- The test suite needs no firmware or game data.
- Diagnostic logging goes through Serilog, never `Console.WriteLine` or
  `Debug.WriteLine`, and caught exceptions are logged before they are handled.
- Every interactive control in the Avalonia views has a tooltip.

See [Testing](testing.md) for the suite layout and
[Code inspection](inspection.md) for the InspectCode workflow.

The convenience scripts wrap the same commands:

```powershell
pwsh -File scripts/build.ps1
pwsh -File scripts/test.ps1
pwsh -File scripts/format.ps1
```

## CI

`.github/workflows/ci.yml` runs on push, pull request, merge queue, nightly and
manual dispatch:

- Release build with warnings as errors.
- Full test suite on Linux and Windows. Linux collects coverage and enforces a
  60% line / 50% branch floor.
- Native core build on Linux from the tracked `xemu/` tree.
- Deterministic build check: two clean builds must produce identical assemblies.
- `dotnet format --verify-no-changes`.
- InspectCode gate on warnings. Accepted findings live in
  `inspectcode-baseline.json`; style warnings are kept as a CI artifact and are
  not published as code-scanning security alerts.
- NuGet vulnerability audit, including transitive packages.

Other workflows:

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| `codeql.yml` | push, pull request, weekly, manual | CodeQL C# security analysis (`security-extended`). |
| `dependency-review.yml` | pull request, manual | Dependency review on pull requests. |
| `release.yml` | manual | Release publish/attestation pipeline. |
| `xemu-upstream-report.yml` | weekly, manual | Opens or updates one issue when upstream xemu or a Meson dependency lock is behind. |

## Security settings

The public repository uses GitHub's free security features:

- Dependabot alerts and security updates.
- Secret scanning with push protection.
- Private vulnerability reporting.
- CodeQL code scanning for the managed C# shell.
- Dependency review on pull requests.

The InspectCode warning gate is separate from security scanning; its accepted
style findings do not appear on the security page.

The release tree is produced locally with `scripts/package.ps1` and verified by
`scripts/package-verify.ps1`.

## Core gates

Before adopting an upstream release, run the selective-port audit and follow
the [upstream update runbook](xemu-upgrade.md).

The native core is built locally, and the `native-core` CI job rebuilds it on
Linux from the tracked tree:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

On Linux, `bash scripts/build-native-xemu.sh` builds the same tree and writes the
renderer/audio manifest.

The shell tests cover launch planning, generated configuration, display/input
sinks, save-state orchestration, filters, settings, the library and the
command-line parser. Media and emulation checks are described in the script
headers under `scripts/`.

The headless path has its own end-to-end smoke check; it needs a built core, a
Release frontend and the development firmware:

```powershell
pwsh -File scripts/headless-check.ps1 -Seconds 20 -Disc 'D:\Games\Halo.iso'
```

The script renders off-screen, writes a JSON report and fails when no frames were
published.

The core baseline and disc boot checks are in
[The native core](core.md#build-and-validation).

## Documentation gate

- Every documentation page must render as Markdown.
- Internal links must resolve within `docs/`.
- Code samples must be copy-pasteable and use the current command names.
- Do not link to removed historical pages.
- Keep `wiki/` in sync when user-visible behavior changes.

## Packaging gate

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

`package-verify.ps1` checks license completeness, archive contents, the bundled
Windows and Linux cores, Unix execute bits, SHA-256 sums, the package manifest
and the CLI version. See [Releasing](releasing.md).

## Pull requests

- Keep one logical change per pull request.
- Include tests for behavior changes.
- Update documentation with the change.
- Do not commit firmware, HDD images or game data.
- Do not break the license/provenance records.

See [Contributing](contributing.md).
