# Native CPU core (XCN0-XCN3)

The product CPU is the vendored xemu 0.8.139 / QEMU 10.2.0 TCG i386 core
behind `libxgridcpu`. Every device, the NV2A, the APU, the host layer and the
frontend stay managed C#. See `ImplementationPlan.md` E0N,
`docs/research-notes/native-cpu-core.md` and `docs/porting-policy.md` §2b.

## Layout

| Path | What |
|---|---|
| `qemu/` | A build copy of `References2/xemu-0.8.139` (**git-ignored**). Never edit `References2/`; this copy is disposable. |
| `qemu-patches/` | The build-scoping patches applied to `qemu/`. Tracked; part of the GPL source offer. |
| `xgridcpu/` | The C shim (`xgridcpu.h`, `xgridcpu.c`), the C smoke test (`test.c`) and `build.sh`. |
| `../src/XGrid.Cpu.Native/` | The C# P/Invoke seam (XCN1). |
| `../tools/xcn0-probe/` | The XCN0 C# acceptance probe. |
| `../scripts/build-native-cpu.ps1` | Reproducible build entry point. |

## Build (H1)

MSYS2 **MINGW64** (`C:\msys64\mingw64`) is the working toolchain: gcc 16.1,
meson 1.12.1, ninja 1.13.2, python 3.14.10, glib 2.88.1, pixman 0.46.4, zlib
1.4, libepoxy, xxhash, diffutils, PyYAML, expat. `C:\mingw64`, `C:\ninja`,
`C:\cmake` and `C:\Qt` are standalone fallbacks.

```powershell
pwsh -File scripts/build-native-cpu.ps1
```

Or by hand, in an `MSYSTEM=MINGW64` shell:

```bash
cp -r References2/xemu-0.8.139 native/qemu      # only if native/qemu is absent
cd native/qemu
for p in ../qemu-patches/*.patch; do patch -p1 < "$p"; done
./configure --target-list=i386-softmmu --without-default-features --enable-tcg \
  --disable-werror --disable-docs --disable-tools --disable-sdl --disable-opengl \
  --disable-curl --disable-slirp --disable-virtfs
ninja -C build -j 16 libqemu-i386-softmmu.a libsystem.a libqemuutil.a libqom.a \
  libio.a libcrypto.a
cd ../xgridcpu && ./build.sh
```

The normal `qemu-system-i386.exe` target is not built: it pulls xemu's UI,
which the C# frontend replaces. The archives above are the CPU/memory subset.

## Patches (build-tooling only)

1. `0001-configure.patch` - MSYS2 `ensurepip` is broken; use the system Python
   and `/mingw64/bin/meson` instead of a pyvenv.
2. `0002-subprojects-dsp56300-meson.patch` - create the extraction directory and
   use `tar --force-local -xzf` (GNU tar treats `D:` as a remote host).
3. `0003-ui-meson.patch` - do not build xemu's UI.
4. `0004-hw-9pfs-meson.patch` - no 9p backend (MinGW has no `sys/resource.h`).
5. `0005-hw-meson.patch` - `hw/core` only (no QEMU device models).
6. `0006-hw-core-meson.patch` - no EIF/FDT; add the portable x86 TSC + APIC
   objects.
7. `0007-tests-meson.patch` - skip the xemu test tree.
8. `0008-system-meson.patch` - no libfdt; use the stub.
9. `0009-backends-meson.patch` - no IOMMUFD/SGX.
10. `0010-meson-vulkan-imgui.patch` - no Vulkan/GLSL/volk/SPIRV/imgui/implot.
11. `0011-thirdparty-vma.patch` - no VulkanMemoryAllocator.
12. `0012-hw-core-null-machine.patch` - x86-only `none` machine (no ARM
    interfaces).
13. `0013-system-cpus.patch` - no TCG vCPU thread; the host calls `cpu_exec`.

The archives build and the XCN0 tests pass:

```bash
cd native/xgridcpu && ./build.sh && ./xgc_test.exe
# XCN0 C SPIKE PASS (100k steps, RAM, real MMIO callbacks, registers)

./xgc_ring_test.exe
# XCN1 MMIO SHADOW+RING C PASS (shadow reads, queued writes, 100k steps)

$env:PATH = 'C:\msys64\mingw64\bin;' + $env:PATH
$env:XGRIDCPU_DLL = (Resolve-Path native/xgridcpu/xgridcpu.dll).Path
dotnet run --project tools/xcn0-probe -c Release
# XCN0 CSHARP SPIKE PASS (vCPU/RAM/step; MMIO host boundary is XCN1)
```

The native shadow+ring path passes in C: reads come from a native shadow
window and writes are queued for the host. Under the **.NET-hosted DLL** the
MMIO path fails before the first ring read (`0xC0000409`), and a managed
callback fails at the callback, while the same code passes as a native
executable. The C# vCPU/RAM/step path is proven. XCN1 must put the CPU in a
native process/boundary (or fix the DLL/TCG ABI) and expose the shadow+write
ring to C#.

Measured on the 100M-step self-loop: **246.9 Msteps/s from C** (0.405 s) and
**247.5 Msteps/s from C#** (0.404 s), 4.05 ns/step (~0.34x guest time on this
trivial loop). This is 3.4x the current raw JIT (73 Msteps/s) and the first
engine rate in the xemu class; a real title workload adds x87/SSE, TB churn and
devices, so 0.5x (365 Msteps/s) is the next measured gate.

None of these changes guest behaviour. A release ships the patches and the
corresponding QEMU source (or a written offer).
