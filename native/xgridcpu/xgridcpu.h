/*
 * XGrid native CPU core - C ABI over the vendored xemu/QEMU TCG i386 core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Ported from: xemu 0.8.139 / QEMU 10.2.0 (TCG i386 CPU, softmmu, TB cache).
 *
 * This is the XCN0 spike surface: enough to create a vCPU, map guest RAM and
 * MMIO, run a bounded number of instructions and read/write the architectural
 * registers. The C# seam (src/XGrid.Cpu.Native) is the only caller.
 */
#ifndef XGRIDCPU_H
#define XGRIDCPU_H

#include <stdint.h>
#include <stddef.h>

#ifdef _WIN32
#define XGC_API __declspec(dllexport)
#else
#define XGC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct XgcCpu XgcCpu;

typedef uint64_t (*XgcMmioReadFn)(void *opaque, uint32_t gpa, uint32_t size);
typedef void (*XgcMmioWriteFn)(void *opaque, uint32_t gpa, uint32_t size,
                               uint64_t value);

typedef struct XgcRegs {
    uint32_t eax, ecx, edx, ebx, esp, ebp, esi, edi;
    uint32_t eip, eflags;
    uint16_t cs, ds, es, fs, gs, ss;
} XgcRegs;

enum {
    XGC_OK = 0,
    XGC_ERR_INIT = -1,
    XGC_ERR_STATE = -2,
    XGC_ERR_ARG = -3,
    XGC_ERR_HALTED = 1,
    XGC_MMIO = 2,
};

typedef struct XgcMmioAccess {
    uint32_t gpa;
    uint32_t size;
    uint32_t is_write;
    uint32_t _pad;
    uint64_t value;
} XgcMmioAccess;

XGC_API int xgc_init(void);
XGC_API XgcCpu *xgc_create(void);
XGC_API void xgc_destroy(XgcCpu *cpu);

XGC_API int xgc_map_ram(XgcCpu *cpu, uint32_t gpa, uint32_t size, void *host);
XGC_API int xgc_map_mmio(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                         XgcMmioReadFn read, XgcMmioWriteFn write,
                         void *opaque);

/* XCN1 MMIO ring: a native shadowed read window plus a write queue. The
 * device model (C#) updates the shadow between run slices and drains the
 * write queue; the softmmu callback never calls managed code. */
XGC_API int xgc_map_mmio_ring(XgcCpu *cpu, uint32_t gpa, uint32_t size);
XGC_API int xgc_map_mmio_ring_shadow(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                                     void *shadow);
XGC_API int xgc_mmio_take(XgcCpu *cpu, XgcMmioAccess *access);
XGC_API void xgc_mmio_set_read(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                               uint64_t value);

XGC_API int xgc_reset_real(XgcCpu *cpu, uint32_t eip, uint32_t esp);
XGC_API int xgc_run(XgcCpu *cpu, uint32_t max_insns, uint64_t *executed);

XGC_API void xgc_get_regs(XgcCpu *cpu, XgcRegs *regs);
XGC_API void xgc_set_regs(XgcCpu *cpu, const XgcRegs *regs);
XGC_API void xgc_flush_tb(XgcCpu *cpu);

XGC_API uint64_t xgc_icount(XgcCpu *cpu);

/* XCN0 diagnostic: call a host callback outside QEMU. */
typedef uint64_t (*XgcTestCallback)(void *opaque, uint32_t gpa, uint32_t size);
XGC_API uint64_t xgc_test_callback(XgcTestCallback fn);

#ifdef __cplusplus
}
#endif

#endif /* XGRIDCPU_H */
