# Code inspection

XGrid uses JetBrains InspectCode as a second pair of eyes after the compiler and
the analyzers. The compiler gate is authoritative for build failures; the
inspection report catches unused members, redundant code, suspicious
comparisons, null-flow problems and inconsistent visibility.

## Install

```powershell
dotnet tool install --global JetBrains.ReSharper.GlobalTools
```

If the tool is already installed, update it:

```powershell
dotnet tool update --global JetBrains.ReSharper.GlobalTools
```

## Run

```powershell
# Full solution, warnings and errors, no solution-wide analysis
jb inspectcode XGrid.sln `
  --format=Text `
  --output=wt-data/inspectcode.txt `
  --severity=WARNING `
  --no-swea `
  --verbosity=WARN
```

`--swea` enables solution-wide analysis. It finds cross-project dead code but is
much slower and can trip over file watchers on large trees; use it for a periodic
audit, not every inner loop.

Useful variants:

```powershell
# One project
jb inspectcode src/XGrid.Frontend.Avalonia/XGrid.Frontend.Avalonia.csproj `
  --format=Text --output=wt-data/inspectcode-frontend.txt --severity=WARNING

# Include suggestions and hints
jb inspectcode XGrid.sln --format=Text --output=wt-data/inspectcode-all.txt

# SARIF for tooling
jb inspectcode XGrid.sln --format=Sarif --output=wt-data/inspectcode.sarif
```

Reports under `wt-data/` are local artifacts and are not committed.

## Scope

The managed gate covers the projects under `src/` and `tests/`. The vendored
`xemu/` C tree has upstream QEMU headers and macros that the C++ analysis cannot
resolve without the full native build; those findings are handled in the core
tree, not by the shell gate. Run the inspection from the solution so the shared
project settings and naming rules apply consistently.

InspectCode is a code-quality gate. Its SARIF report stays as a CI artifact; it
is not uploaded to the security code-scanning page. CodeQL is the security
scanner.

## Fix policy

- **Errors** must be fixed; they usually indicate a real null-flow or API
  problem.
- **Warnings** should be fixed unless the warning is a known false positive.
  Prefer a code change over a suppression.
- **Suggestions and hints** are reviewed case by case. Do not mass-rewrite the
  tree for style-only findings.
- A suppression must be as narrow as possible and carry a justification.
  Existing examples are the `UnusedMember.Global` markers on public icon
  geometries and the `CA1001` justification on the Avalonia window.
- Never silence an inspection that points at a real bug in a network parser,
  settings loader or background worker.

## Common findings

| Finding | Preferred fix |
| --- | --- |
| Unused private member or field | Remove it, or use it; do not keep speculative code. |
| Unused public member | Check whether it is part of the documented API. If not, reduce visibility or remove it. |
| Redundant null check | Remove the check or correct the annotation. |
| Possible null dereference | Guard the value or make the null contract explicit. |
| Redundant using | Remove it. |
| Inconsistent naming | Follow the surrounding code and the conventions in [Contributing](contributing.md). |
| Async method without `Async` suffix | Rename it and update callers. |

## After the inspection

```powershell
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --no-build --nologo
dotnet format XGrid.sln --no-restore --verify-no-changes
```

Record the result in the change description. A clean inspection is not a
substitute for tests, and tests are not a substitute for a clean inspection.

## Related pages

- [Quality gates](quality-gates.md)
- [Testing](testing.md)
- [Contributing](contributing.md)
