# Contributing to XGrid

XGrid is an enhanced xemu core with an Avalonia desktop shell. Read the
[architecture](docs/reference/architecture.md) and
[development guide](docs/development/contributing.md) before making a change.

## Ground rules

- The product is the **Avalonia shell + native xemu core**. The managed C#
  emulator is a validation oracle, not the product path.
- XGrid is **GPL-2.0-or-later**. Do not add code that cannot be licensed
  compatibly.
- Never commit firmware, BIOS images, EEPROMs, HDD images or game data.
- Keep one logical change per pull request.
- Add tests for behavior changes and update the documentation with the code.

## Build and test

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

Build the native core when you touch it:

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

The core build needs Windows + MSYS2 MINGW64. The shell builds on Windows, Linux
and macOS. See [Building from source](docs/development/building.md).

## Code style

- Nullable enabled; warnings are errors; analyzers clean.
- Explicit `using` directives (implicit usings are disabled).
- Avoid allocations and LINQ in hot paths.
- Native code follows xemu's style; run `clang-format` on new C files.
- Tests use xUnit and describe the behavior, not the implementation.

## Core changes

- New native code belongs in `xemu/xgrid-core/`.
- Upstream edits must be recorded in `xemu/xemu-patches/`.
- Preserve original copyright/license notices, add SPDX and a `Ported from` line.
- Update `THIRD_PARTY_NOTICES.md` and the legal pages.

See [The XGrid core](docs/development/core.md) and
[Porting and provenance](docs/legal/porting-policy.md).

## Documentation changes

- User pages live under `docs/`; the sidebar is `docs/_sidebar.md`.
- Wiki-ready pages live under `wiki/`.
- Use relative links inside `docs/`.
- Do not add historical session logs, wave notes or internal task tracking to the
  user documentation.

## Security

Do not report vulnerabilities in public issues. Follow [SECURITY.md](SECURITY.md).

## Review checklist

- [ ] Build, tests and format pass.
- [ ] New behavior has a test.
- [ ] Documentation is updated.
- [ ] No firmware or game data is committed.
- [ ] License/provenance records are updated for ported code.
- [ ] The change is limited to one logical purpose.
