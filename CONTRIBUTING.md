# Contributing to XGrid

## Before you start

Read `ImplementationPlan.md` (especially Part II) and the relevant notes in `docs/research-notes/`.
XGrid is a clean-room reimplementation: do not copy source code from xemu,
Cxbx-Reloaded, or xqemu. Extract facts, then implement independently.

## Licensing

- Do not add code derived from GPL, LGPL, or unlicensed projects.
- XISOSharp (MIT) is the only approved third-party library; keep it behind an adapter/interface.
- Record any new reference or reused component in `docs/research-notes/sources.md`.
- Never commit firmware, BIOS images, EEPROMs, or game data.

## Coding standards

- Nullable reference types are required.
- Warnings are errors; keep analyzers clean.
- Follow plan section 31 for performance-sensitive code (no LINQ, boxing, or per-instruction
  allocations in hot paths).
- Add tests for new behavior. Devices need register/state tests; parsers need boundary and
  corruption tests.

## Workflow

```powershell
dotnet build XGrid.sln -c Release
dotnet test XGrid.sln -c Release
dotnet format XGrid.sln --verify-no-changes
```

Keep pull requests focused and update the plan/notes when behavior or facts change.
