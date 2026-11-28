# Dependency license texts

Every release archive carries this folder next to `LICENSE`, `COPYING` and
`THIRD_PARTY_NOTICES.md`. The license texts here are shipped in full; the
per-component copyright lines and attribution live in
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

| File | Used by |
| --- | --- |
| `GPL-2.0.txt` | XGrid, the xemu core and its QEMU base, the ported DSP modules |
| `LGPL-2.1.txt` | GLib, the gettext runtime, libiconv, libusb, and LGPL files inside xemu/QEMU |
| `LGPL-3.0.txt` | libidn2, libunistring |
| `GPL-3.0.txt` | GCC runtime (`libgcc_s_seh-1.dll`, `libstdc++-6.dll`) |
| `GCC-exception-3.1.txt` | GCC runtime library exception |
| `Apache-2.0.txt` | OpenSSL |
| `MIT.txt` | Avalonia, CommunityToolkit.Mvvm, Iced, XISOSharp, CHDSharp, Silk.NET.OpenGL, NAudio, libepoxy, nghttp2/nghttp3/ngtcp2, libpsl, Brotli, winpthreads |
| `BSD-2-Clause.txt` | libsamplerate, LZ4, xxHash |
| `BSD-3-Clause.txt` | libchdr, PCRE2, libssh2, libslirp, Zstandard, winpthreads |
| `Zlib.txt` | SDL3, zlib |
| `curl.txt` | libcurl |
| `OFL-1.1.txt` | Inter font (Avalonia.Fonts.Inter) |

Test-only tooling (xUnit, analyzers, BenchmarkDotNet, coverlet, the .NET test
SDK) is not redistributed with the application and does not need a text here.

`scripts/package-verify.ps1` requires every file in this folder in every
archive.
