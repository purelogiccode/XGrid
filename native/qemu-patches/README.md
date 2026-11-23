# Vendored QEMU patches

Apply from `native/qemu` with:

```bash
for p in ../qemu-patches/*.patch; do patch -p1 < "$p"; done
```

| Patch | Scope | Why |
|---|---|---|
| `0001-configure.patch` | build | MSYS2 Python `ensurepip` is broken; use the system Python and the pacman meson. |
| `0002-subprojects-dsp56300-meson.patch` | build | Create the extraction dir and pass `--force-local` to tar (Windows drive colon). |
| `0003-ui-meson.patch` | build | No xemu UI; the C# frontend owns it. |
| `0004-hw-9pfs-meson.patch` | build | No 9p backend on MinGW (`sys/resource.h`). |
| `0005-hw-meson.patch` | build | `hw/core` only; no QEMU device models. |
| `0006-hw-core-meson.patch` | build | No EIF/FDT; add the portable x86 `cpu_get_tsc` + APIC objects. |
| `0007-tests-meson.patch` | build | Skip the xemu test tree (it depends on the NV2A sources). |
| `0008-system-meson.patch` | build | No libfdt; use the device-tree stub. |
| `0009-backends-meson.patch` | build | No IOMMUFD/SGX host-memory backends (Linux-only). |
| `0010-meson-vulkan-imgui.patch` | build | No Vulkan/GLSL/volk/SPIRV/imgui/implot; set them `not_found`. |
| `0011-thirdparty-vma.patch` | build | No VulkanMemoryAllocator. |
| `0012-hw-core-null-machine.patch` | runtime | Drop the arm/aarch64 machine interfaces from the `none` machine (x86-only library). |
| `0013-system-cpus.patch` | runtime | `qemu_init_vcpu` does not start a TCG vCPU thread; the host calls `cpu_exec()` itself. |

These are build-scoping and single-threaded-host patches, not emulator
semantics. The CPU/memory/TCG code is otherwise unmodified. The GPL-2.0 source
offer includes these patches and the pinned xemu 0.8.139 tree.

The XCN0 C and C# smoke tests pass with this set
(`native/xgridcpu/test.c`, `tools/xcn0-probe`); the managed MMIO callback
path is the XCN1 native-ring work. See
`docs/research-notes/native-cpu-core.md`.
