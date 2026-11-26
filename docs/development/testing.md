# Testing XGrid

The test suite is part of the product gate. It is split by assembly so a
focused change can run quickly, while the full solution run remains the release
condition.

## Test projects

| Project | Covers |
| --- | --- |
| `XGrid.Diagnostics.Tests` | Serilog report text, environment snapshot, version comparison, diagnostics HTTP clients, bug-report queue and donation verification. |
| `XGrid.Frontend.Tests` | Settings persistence, launch arguments, input mapping, library scanning, cover matching and caching, library filters/sorts, frame filters, pacing, screenshots, save-state/playback runners, updates and presentation models. |
| `XGrid.Core.Tests` | Core capabilities, launch settings, TOML writer, paths, display sink and the core hosting contract. |
| `XGrid.Cpu.Tests` | Decoder, interpreter and JIT execution, paging, interrupts, FPU/SSE and the CPU oracle corpus. |
| `XGrid.Memory.Tests` | Address space, physical memory, endianness, snapshots and dirty-watch behavior. |
| `XGrid.Bus.Tests` | PCI, PIC, PIT, SMBus, USB/OHCI, ACI, NVNet, pad input and socket helpers. |
| `XGrid.Storage.Tests` | ATA/ATAPI, IDE channels, DMA, DVD images, qcow2 and overlay disks. |
| `XGrid.Audio.Tests` | APU voices, DSP porting, frame counter, snapshots, sinks and the tee backend. |
| `XGrid.Graphics.Tests` | Display frames and frame diffing. |
| `XGrid.Nv2a.Tests` | PFIFO, PGRAPH, shaders, textures, scanout and register reports. |
| `XGrid.GpuGl.Tests` | OpenGL state mapping, shader generation, presentation and live GPU paths. |
| `XGrid.Input.Tests` | Pad mapping and XInput translation. |
| `XGrid.Xbe.Tests` | XBE headers, sections, libraries, TLS and malformed files. |
| `XGrid.XboxFs.Tests` | FATX and XDVDFS readers. |
| `XGrid.Integration.Tests` | Machine boot, discs, EEPROM, snapshots and network integration. |

## Commands

```powershell
# Restore once after a project or package change
dotnet restore XGrid.sln

# Release build; warnings are errors
dotnet build XGrid.sln -c Release --no-restore --nologo

# Full suite without rebuilding
dotnet test XGrid.sln -c Release --no-build --nologo

# One project
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj -c Release --nologo

# One class
dotnet test tests/XGrid.Diagnostics.Tests/XGrid.Diagnostics.Tests.csproj `
  -c Release --filter "FullyQualifiedName~DonationVerificationClientTests"

# One test
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj `
  -c Release --filter "FullyQualifiedName~KeyboardKeysTests.TryParse_RejectsMissingOrUnknownNames"
```

`dotnet test XGrid.sln` builds the test projects if `--no-build` is omitted.
Use `--no-build` for repeated local runs after a successful build.

## Coverage

Collect coverage for a focused project:

```powershell
dotnet test tests/XGrid.Frontend.Tests/XGrid.Frontend.Tests.csproj `
  -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
```

Coverage is a review aid, not a target. A high line count with no assertions or
no failure branches is not useful. Prefer tests that pin a contract or a bug
that was actually observed.

## Deterministic tests

The suite runs on Windows, Linux and macOS CI. Keep tests independent of the
host:

- use `Path.GetTempPath()` and a unique folder per test;
- dispose temporary folders in `Dispose` and tolerate a leftover file;
- do not depend on a real firmware image, disc, GPU, audio device or network;
- inject `HttpClient` handlers instead of calling a live service;
- set culture-sensitive formatting explicitly with `CultureInfo.InvariantCulture`;
- avoid fixed sleeps; use a `SemaphoreSlim`, `TaskCompletionSource` or a
  bounded wait when a background worker must be observed;
- do not modify process-wide environment variables in parallel tests.

The native core tests may need a built `xemu/dist/xemu` binary. They are skipped
or isolated where the core is unavailable; the shell and diagnostics tests do
not need the core.

## What to test

| Change | Minimum tests |
| --- | --- |
| Parser or settings model | Valid values, invalid values, null/empty input, round trip. |
| File or network boundary | Success, not-found/empty, malformed response, transport failure. |
| Background worker | Delivery, ordering, failure isolation and shutdown. |
| UI state | The pure view-model or helper, not the rendered control. |
| Bug fix | A regression test that fails on the old behavior. |

UI rendering, native window embedding and live GPU paths are covered by the
smoke and headless scripts, not by unit tests.

## Static analysis

The solution also runs `dotnet format --verify-no-changes` and
`jb inspectcode`. See [Code inspection](inspection.md) and
[Quality gates](quality-gates.md).
