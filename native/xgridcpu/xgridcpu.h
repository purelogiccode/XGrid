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
#include <stdbool.h>

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
    uint32_t tsc_lo, tsc_hi;
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
    uint32_t gpa;      /* guest physical address, or the port for I/O */
    uint32_t size;     /* access width in bytes */
    uint32_t is_write; /* 1 = write */
    uint32_t flags;    /* bit0: port I/O (in/out), not memory-mapped */
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

/* Live read windows: a register read that cannot be served from a shadow
 * calls `read` synchronously. The host implementation publishes the address
 * and waits for the device model to answer, so device state (and time) is
 * exact at the access. Writes stay queued either way. */
typedef uint64_t (*XgcIoReadFn)(void *opaque, uint32_t gpa, unsigned size);

XGC_API int xgc_map_mmio_ring_live(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                                   XgcIoReadFn read, void *opaque);

/* Port I/O windows (the guest's in/out instructions). `port` is the I/O
 * address, not a guest physical address. */
XGC_API int xgc_map_io_ring_shadow(XgcCpu *cpu, uint32_t port, uint32_t size,
                                   void *shadow);
XGC_API int xgc_map_io_ring_live(XgcCpu *cpu, uint32_t port, uint32_t size,
                                 XgcIoReadFn read, void *opaque);

/* Direct windows: the CPU reads and writes `host` with no callback (RAM, ROM,
 * flash and aliases). `priority` orders overlapping windows. */
XGC_API int xgc_map_direct(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                           void *host, int priority);

/* RAM alias window: aliases the RAM mapped at `src_gpa` (QEMU region alias,
 * no second RAM block), wrapping every `ram_size` bytes of the window. */
XGC_API int xgc_map_ram_alias(XgcCpu *cpu, uint32_t gpa, uint32_t size,
                              uint32_t src_gpa, int priority);

/* Writes the queue dropped because the slice produced more than the shared
 * ring can hold (diagnostic; a non-zero value means the slice is too big). */
XGC_API uint64_t xgc_mmio_dropped(XgcCpu *cpu);

/* Optional synchronous write sink. When installed, every device write is
 * handed to the sink instead of the internal ring, so the host can publish it
 * into a shared ring immediately and apply backpressure (spin while the
 * consumer is behind). Returns false to drop the access (shutdown). */
typedef bool (*XgcMmioSinkFn)(void *opaque, const XgcMmioAccess *access);
XGC_API void xgc_set_mmio_sink(XgcCpu *cpu, XgcMmioSinkFn sink, void *opaque);

/* Inject one hardware interrupt vector (0-255); the x86 CPU takes it at the
 * next instruction boundary when IF=1. */
XGC_API int xgc_set_irq(XgcCpu *cpu, uint32_t vector);

/* Optional interrupt-acknowledge callback: when installed, the CPU asks the
 * device model for the vector at the moment it takes the interrupt (so a
 * masked or IF=0 interrupt is never acknowledged). The legacy vector set by
 * xgc_set_irq is only used when no callback is installed. */
typedef uint32_t (*XgcIrqAckFn)(void *opaque);
XGC_API void xgc_set_irq_callback(XgcCpu *cpu, XgcIrqAckFn ack, void *opaque);

XGC_API int xgc_reset_real(XgcCpu *cpu, uint32_t eip, uint32_t esp);
XGC_API int xgc_reset(XgcCpu *cpu);
XGC_API int xgc_run(XgcCpu *cpu, uint32_t max_insns, uint64_t *executed);

XGC_API void xgc_get_regs(XgcCpu *cpu, XgcRegs *regs);
XGC_API void xgc_set_regs(XgcCpu *cpu, const XgcRegs *regs);
XGC_API void xgc_flush_tb(XgcCpu *cpu);

XGC_API uint64_t xgc_icount(XgcCpu *cpu);
XGC_API uint64_t xgc_tsc(XgcCpu *cpu);

/* XCN0 diagnostic: call a host callback outside QEMU. */
typedef uint64_t (*XgcTestCallback)(void *opaque, uint32_t gpa, uint32_t size);
XGC_API uint64_t xgc_test_callback(XgcTestCallback fn);

#ifdef __cplusplus
}
#endif

#endif /* XGRIDCPU_H */
