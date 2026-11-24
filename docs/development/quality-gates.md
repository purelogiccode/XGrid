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

See [Testing](testing.md) for the suite layout and [Code inspection](inspection.md)
for the InspectCode workflow.

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
- Full test suite on Windows and Linux self-hosted runners; Linux collects
  coverage.
- Deterministic build check.
- `dotnet format --verify-no-changes`.
- InspectCode ratchet and coverage floors.
- Dependency vulnerability audit.

Other workflows:

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| `codeql.yml` | manual | CodeQL analysis. |
| `dependency-review.yml` | manual | Dependency review. |
| `release.yml` | manual | Release publish/attestation pipeline. |

The release tree is produced locally with `scripts/package.ps1` and verified by
`scripts/package-verify.ps1`.

## Core gates

The native core is built separately:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

On Linux, `bash scripts/build-native-xemu.sh` builds the same tree and writes the
renderer manifest.

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

## Documentation gate

- Every documentation page must render as Markdown.
- Internal links must resolve within `docs/`.
- Code samples must be copy-pasteable and use the current command names.
- Do not link to removed historical pages.

## Packaging gate

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

`package-verify.ps1` checks license completeness, archive contents, the bundled
Windows and Linux cores, Unix execute bits, SHA-256 sums, the package manifest
and the CLI version.

## Pull requests

- Keep one logical change per pull request.
- Include tests for behavior changes.
- Update documentation with the change.
- Do not commit firmware, HDD images or game data.
- Do not break the license/provenance records.

See [Contributing](contributing.md).
