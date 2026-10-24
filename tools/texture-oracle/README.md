# S3TC differential oracle (development only)

Compiles xemu 0.8.139 `hw/xbox/nv2a/pgraph/s3tc.c` standalone and writes the
decoded RGBA texels for a fixed block corpus. The output is replayed by
`tests/XGrid.Nv2a.Tests/Nv2aTextureFormatTests.cs` (the `S3tcVectors` facts), so
the port is checked against the reference implementation rather than against
itself.

- `stub/qemu/osdep.h` supplies `g_malloc`/`MIN` so the reference file builds
  without QEMU.
- `References2/` is read-only and is not redistributed; only this generator and
  its output (facts) are in the repository.
- `build.ps1 -Regenerate` rewrites
  `tests/XGrid.Nv2a.Tests/TestData/s3tc-vectors.txt`.

Run (host gcc):

```powershell
pwsh -File tools/texture-oracle/build.ps1 -Regenerate
```
