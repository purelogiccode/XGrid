# Contributing

XGrid welcomes focused fixes and features. Read this page and
[Architecture](../reference/architecture.md) first.

## Before you start

- The product is the **Avalonia shell + native xemu core**.
- The managed C# emulator is a validation oracle, not the product path.
- XGrid is GPL-2.0-or-later. Do not add code you cannot license compatibly.
- Never commit firmware, BIOS images, EEPROMs, HDD images or game data.

## Set up

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
```

Build the core when you touch native code or the integration path:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

On Linux, build the same tree with `bash scripts/build-native-xemu.sh`.

See [Building from source](building.md).

## Workflow

1. Open or claim an issue that describes the behavior.
2. Keep one logical change per pull request.
3. Add or update tests that fail before the change and pass after it.
4. Run the gates:
   ```powershell
   dotnet test XGrid.sln -c Release --nologo --no-build
   dotnet format XGrid.sln --no-restore --verify-no-changes
   jb inspectcode XGrid.sln --format=Text --output=wt-data/inspectcode.txt --severity=WARNING --no-swea
   ```
   See [Testing](testing.md) and [Code inspection](inspection.md).
5. Update the documentation in the same change.
6. Include the licensing/provenance updates for ported code.

## Code style

- C#: nullable enabled, warnings as errors, analyzers clean.
- No implicit usings; add explicit `using` directives.
- Keep hot paths allocation-free.
- Visibility: `public` is a cross-assembly contract, `internal` is the default
  for implementation types, and `private` is the default for helpers. Public
  members on an internal type are `internal` unless an interface or the runtime
  requires otherwise.
- Document every public type and member with XML comments.
- Log through Serilog (`Log.Information`, `Log.Warning`, `Log.Error`). Do not add
  new `Console.WriteLine` or `Debug.WriteLine` diagnostics, and log a caught
  exception before handling it.
- Keep async method names suffixed with `Async`.
- Native core: follow xemu's style; `clang-format` new C files.
- Tests use xUnit; names describe behavior.

## Core changes

- New native code goes in `xemu/xgrid-core/`.
- Upstream edits are recorded in `xemu/xemu-patches/`.
- Preserve original copyright/license notices; add SPDX and `Ported from` lines.
- Update `THIRD_PARTY_NOTICES.md` and the legal pages.
- Rebuild the core and run the shell tests.

## Documentation changes

- Put user-facing pages under `docs/` and keep the sidebar in `docs/_sidebar.md`
  up to date.
- Use relative links inside `docs/`.
- Keep `wiki/` pages in sync when a user-visible behavior changes.
- Do not reintroduce historical session logs or internal wave notes.

## Media and references

- Do not add copyrighted firmware or games to the repository.
- Reference source trees are read-only and are not part of the build.
- If you port code, follow [Porting and provenance](../legal/porting-policy.md).

## Security

Do not file public issues for vulnerabilities. Follow `SECURITY.md`.

## Questions

Open a discussion or an issue with:

- what you expected;
- what happened;
- the Activity log around the failure;
- your platform, renderer and hosting mode.
