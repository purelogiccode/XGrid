# Testing

The suite is part of the release gate. It is split by assembly so focused changes
run quickly.

## Main projects

| Project | Covers |
| --- | --- |
| `XGrid.Diagnostics.Tests` | Logging, bug reports, statistics, update and donation clients. |
| `XGrid.Frontend.Tests` | Settings, launch args, input, library, covers, filters, screenshots, save-state and playback runners. |
| `XGrid.Core.Tests` | Core capabilities, launch settings, TOML writer, paths, display sink and hosting contract. |
| Managed oracle projects | CPU, memory, bus, storage, audio, NV2A, OpenGL, XBE, FATX/XDVDFS. |
| `XGrid.Integration.Tests` | Boot, discs, EEPROM, snapshots and network integration. |

## Commands

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --no-build --nologo

# One project
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj -c Release --nologo

# One test
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj `
  -c Release --filter "FullyQualifiedName~KeyboardKeysTests"
```

## Conventions

- Tests must not need firmware, a disc, a GPU, an audio device or a live service.
- Use a unique temp folder per test and dispose it.
- Inject `HttpClient` handlers instead of touching the network.
- Use `CultureInfo.InvariantCulture` for formatting.
- Avoid fixed sleeps; use a semaphore or completion source.
- Do not change process environment variables in parallel tests.

Coverage is a review aid. CI enforces 60% line / 50% branch on Linux.

## Static analysis

`dotnet format --verify-no-changes` and `jb inspectcode` are part of the gate.
See [Quality gates](https://github.com/purelogiccode/XGrid/blob/master/docs/development/quality-gates.md)
and [Code inspection](https://github.com/purelogiccode/XGrid/blob/master/docs/development/inspection.md).

Full guide: [Testing](https://github.com/purelogiccode/XGrid/blob/master/docs/development/testing.md).
