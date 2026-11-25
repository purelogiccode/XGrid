# XGrid — agent notes

XGrid is one product: the native **xemu 0.8.139** core in `xemu/` plus the
**Avalonia** shell in `src/XGrid.Frontend.Avalonia`. The managed C# emulator in
`src/XGrid.Core`, `src/XGrid.Cpu`, `src/XGrid.Nv2a`, etc. is a **validation
oracle**, not the product path.

Read:

- `docs/index.md` — product overview.
- `docs/reference/architecture.md` — shell/core split.
- `docs/development/building.md` — toolchain.
- `docs/development/quality-gates.md` — checks.
- `docs/legal/porting-policy.md` — provenance rules.

## Product rules

1. **Do not re-implement the emulator in the shell.** New emulation behavior
   belongs in `xemu/xgrid-core/` or an upstream patch.
2. **The core owns every emulated pixel.** The shell must never receive, copy,
   filter, overlay, scale or redraw the console picture. Presentation filters
   and the FPS overlay are core features selected through the core ABI or the
   launch environment. Do not add a `WriteableBitmap`/`Image` frame canvas, a
   shell frame sink, or any per-frame Avalonia dependency on the picture.
3. **Upstream edits are patches.** Record them in `xemu/xemu-patches/`; keep the
   original copyright/license notices.
4. **C# first for the shell.** P/Invoke is only for host APIs/OS facilities.
5. **No firmware or game data in git.** `data/`, HDD images, BIOS, EEPROM and
   games stay local.
6. **One logical change per commit.** Code, tests, docs and provenance together.

## Build and gates

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

Native core (Windows + MSYS2 MINGW64):

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

Release tree:

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

Warnings are errors; nullable is enabled; implicit usings are disabled.

## Repository map

| Path | Role |
| --- | --- |
| `src/XGrid.Frontend.Avalonia/` | Avalonia shell. |
| `src/XGrid.Core.Xemu/` | Launch planning, hosting, sinks, save-state wrapper. |
| `src/XGrid.Diagnostics/` | Serilog, bug reports, usage statistics and update checks. |
| `src/XGrid.Input/` | Pad model and mapping. |
| `src/XGrid.Xbe/`, `src/XGrid.XboxFs/` | XBE/FATX/XDVDFS readers. |
| `xemu/xgrid-core/` | XGrid native additions. |
| `xemu/xemu-patches/` | Upstream edit record. |
| `tests/` | Unit/integration tests. |
| `docs/` | User documentation and reference. |
| `wiki/` | Wiki-ready pages. |
| `scripts/` | Build, test, format, core, package. |

## References

`References2/` is a **read-only** oracle set. Never delete, move or rewrite
anything inside it. Do not run cleanup commands against it. Extract facts and
port only GPL-compatible code with notices.

## Documentation

- User documentation starts at `docs/index.md`; the sidebar is
  `docs/_sidebar.md`.
- Keep `wiki/` in sync for user-visible behavior.
- No historical session logs, wave notes or internal task narratives in the user
  documentation.
- Update the docs in the same change as the behavior.

## Licensing

XGrid is GPL-2.0-or-later. The vendored xemu/QEMU tree keeps its per-file
GPL/LGPL/MIT notices. Third-party components and ported modules are recorded in
`THIRD_PARTY_NOTICES.md`. See `docs/legal/licensing.md` and
`docs/legal/porting-policy.md`.
