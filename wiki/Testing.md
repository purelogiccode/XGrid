# Testing

The test suite is part of the release gate. Tests are split by assembly so a
focused change can run quickly.

## Run everything

```powershell
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --no-build --nologo
dotnet format XGrid.sln --no-restore --verify-no-changes
```

## Run one area

```powershell
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj -c Release --nologo
dotnet test tests/XGrid.Diagnostics.Tests/XGrid.Diagnostics.Tests.csproj -c Release --nologo

dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj `
  -c Release --filter "FullyQualifiedName~SettingsStoreTests"
```

## Projects

| Project | Covers |
| --- | --- |
| `XGrid.Diagnostics.Tests` | Logs, bug reports, statistics, updates, donation client. |
| `XGrid.Frontend.Tests` | Settings, CLI, input, library, filters, screenshots, runners. |
| `XGrid.Core.Tests` | Launch settings, TOML, paths, display sink, hosting. |
| `XGrid.Cpu.Tests` | Decoder, interpreter, JIT, paging, interrupts, oracle. |
| `XGrid.Memory.Tests` | Address space, snapshots, dirty watch. |
| `XGrid.Bus.Tests` | PCI, PIC, PIT, SMBus, USB, audio bus, network. |
| `XGrid.Storage.Tests` | ATA/ATAPI, IDE, DMA, disc and disk images. |
| `XGrid.Audio.Tests` | APU, DSP, sinks, snapshots. |
| `XGrid.Nv2a.Tests` / `XGrid.GpuGl.Tests` | GPU command processing and OpenGL. |
| `XGrid.Xbe.Tests` / `XGrid.XboxFs.Tests` | XBE and FATX/XDVDFS readers. |
| `XGrid.Integration.Tests` | Machine boot and media integration. |

## Rules for new tests

- Use a unique temporary folder and clean it up.
- Never call a live service; inject an `HttpClient` handler.
- Avoid fixed sleeps; wait on a signal with a timeout.
- Do not change process-wide environment variables in parallel tests.
- Add a regression test for every bug fix.

Full guide:
[Testing](https://github.com/purelogiccode/XGrid/blob/master/docs/development/testing.md)
and [Code inspection](https://github.com/purelogiccode/XGrid/blob/master/docs/development/inspection.md).
