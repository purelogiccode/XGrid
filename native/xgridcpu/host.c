/*
 * XCN1 native CPU host process.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Opens a named shared section created by the C# device model, maps guest RAM
 * at GPA 0 and the device windows described by the section header, then runs
 * the vCPU in slices. The C# side owns the read shadow, the write ring and the
 * live-read service; no managed code is called from the TCG/softmmu stack.
 *
 * Reads come from one of two window kinds:
 *   - shadow: the value was published by C# before the slice (ROM, flash,
 *     registers with pure readback);
 *   - live:   the host publishes a request and waits for C# to answer, so the
 *     device model (and its clock) is exact at the access.
 * Writes are always queued; the queue is flushed to the section before a live
 * read is published, so a read observes every earlier write in order.
 */
#include <windows.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdint.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

#include "xgridcpu.h"

#define XGC_HOST_MAGIC   0x58474348u
#define XGC_HOST_VERSION 5u
#define XGC_HOST_SHADOW  0x250u

#define XGC_HOST_MAX_WINDOWS 24
#define XGC_WIN_IO           0x1u  /* the base is an I/O port */
#define XGC_WIN_LIVE         0x2u  /* reads are serviced by C# */
#define XGC_WIN_DIRECT       0x4u  /* the shadow slice is plain memory */
#define XGC_WIN_RAM_ALIAS    0x8u  /* alias the RAM at gpa 0 */

#define XGC_IO_IDLE     0u
#define XGC_IO_REQUEST  1u
#define XGC_IO_DONE     2u
#define XGC_IO_ERROR    0x80000000u
#define XGC_IO_IRQ_ACK  0x80000000u /* io_flags bit for an interrupt ack */

typedef struct XgcHostWindow {
    uint32_t base;
    uint32_t size;
    uint32_t flags;
    uint32_t shadow; /* byte offset into the section's shadow area */
    int32_t priority;
} XgcHostWindow;

typedef struct XgcHostControl {
    uint32_t magic;
    uint32_t version;
    volatile uint32_t ready;
    volatile uint32_t quit;
    volatile uint32_t status;     /* 1 = run requested, 2 = stopped */
    uint32_t run_budget;
    uint64_t executed;
    int32_t rc;
    uint32_t ring_count;
    volatile uint32_t irq_pending;
    uint32_t irq_vector;
    volatile uint32_t reset;
    uint32_t nwindows;
    XgcHostWindow windows[XGC_HOST_MAX_WINDOWS];
    volatile uint32_t io_state;
    uint32_t io_flags;
    uint32_t io_addr;
    uint32_t io_size;
    volatile uint64_t io_value;
    uint64_t io_icount;
    uint32_t ram_size;
    uint32_t shadow_size;
    uint32_t ring_capacity;
    volatile uint32_t ring_tail;
    uint64_t dropped;
} XgcHostControl;

#define XGC_ASSERT_OFF(field, off) \
    _Static_assert(offsetof(XgcHostControl, field) == (off), #field " offset")

XGC_ASSERT_OFF(executed, 24);
XGC_ASSERT_OFF(rc, 32);
XGC_ASSERT_OFF(ring_count, 36);
XGC_ASSERT_OFF(irq_pending, 40);
XGC_ASSERT_OFF(irq_vector, 44);
XGC_ASSERT_OFF(reset, 48);
XGC_ASSERT_OFF(nwindows, 52);
XGC_ASSERT_OFF(windows, 56);
XGC_ASSERT_OFF(io_state, 536);
XGC_ASSERT_OFF(io_flags, 540);
XGC_ASSERT_OFF(io_addr, 544);
XGC_ASSERT_OFF(io_size, 548);
XGC_ASSERT_OFF(io_value, 552);
XGC_ASSERT_OFF(io_icount, 560);
XGC_ASSERT_OFF(ram_size, 568);
XGC_ASSERT_OFF(shadow_size, 572);
XGC_ASSERT_OFF(ring_capacity, 576);
XGC_ASSERT_OFF(ring_tail, 580);
XGC_ASSERT_OFF(dropped, 584);
_Static_assert(sizeof(XgcHostWindow) == 20, "window size");
_Static_assert(offsetof(XgcHostControl, windows) + 24 * 20 <= 536,
               "header fits");

typedef struct XgcHost {
    uint8_t *base;
    XgcHostControl *ctl;
    XgcCpu *cpu;
    uint8_t *shadow;
    XgcMmioAccess *ring;
    XgcRegs *regs;
    uint32_t ring_capacity;
    bool error;
} XgcHost;

typedef struct XgcHostWindowCtx {
    XgcHost *host;
    uint32_t flags;
} XgcHostWindowCtx;

/* SPSC ring into the section: publish each write immediately and spin while
 * the consumer is behind, so no device write is ever dropped. C# drains the
 * ring continuously while it waits for the slice to end. */
static bool host_ring_push(void *opaque, const XgcMmioAccess *access)
{
    XgcHost *h = opaque;
    XgcHostControl *ctl = h->ctl;
    uint32_t cap = h->ring_capacity;
    uint32_t tail = ctl->ring_tail;
    uint32_t next = (tail + 1) % cap;

    while (__atomic_load_n(&ctl->ring_count, __ATOMIC_ACQUIRE) == next) {
        if (ctl->quit) {
            return false;
        }
        YieldProcessor();
    }
    h->ring[tail] = *access;
    __atomic_store_n(&ctl->ring_tail, next, __ATOMIC_RELEASE);
    return true;
}

static uint64_t host_io_read(void *opaque, uint32_t addr, unsigned size)
{
    XgcHostWindowCtx *ctx = opaque;
    XgcHost *h = ctx->host;
    XgcHostControl *ctl = h->ctl;
    ULONGLONG deadline;

    ctl->io_flags = (ctx->flags & XGC_WIN_IO) ? 1u : 0u;
    ctl->io_addr = addr;
    ctl->io_size = size;
    ctl->io_value = 0;
    ctl->io_icount = xgc_icount(h->cpu);
    __atomic_store_n(&ctl->io_state, XGC_IO_REQUEST, __ATOMIC_RELEASE);

    deadline = GetTickCount64() + 5000;
    while (__atomic_load_n(&ctl->io_state, __ATOMIC_ACQUIRE) == XGC_IO_REQUEST) {
        if (GetTickCount64() > deadline) {
            h->error = true;
            return 0xFFFFFFFFu;
        }
        YieldProcessor();
    }
    return __atomic_load_n(&ctl->io_value, __ATOMIC_ACQUIRE);
}

/* CPU interrupt acknowledge: ask the device model (the C# PIC) for the vector
 * at delivery time, so an IF=0 or masked interrupt is never acknowledged. */
static uint32_t host_irq_ack(void *opaque)
{
    XgcHost *h = opaque;
    XgcHostControl *ctl = h->ctl;
    ULONGLONG deadline;

    ctl->io_flags = XGC_IO_IRQ_ACK;
    ctl->io_addr = 0;
    ctl->io_size = 0;
    ctl->io_value = 0xFFFFFFFFu;
    ctl->io_icount = xgc_icount(h->cpu);
    __atomic_store_n(&ctl->io_state, XGC_IO_REQUEST, __ATOMIC_RELEASE);

    deadline = GetTickCount64() + 5000;
    while (__atomic_load_n(&ctl->io_state, __ATOMIC_ACQUIRE) == XGC_IO_REQUEST) {
        if (GetTickCount64() > deadline) {
            h->error = true;
            return 0xFFFFFFFFu;
        }
        YieldProcessor();
    }
    return (uint32_t)__atomic_load_n(&ctl->io_value, __ATOMIC_ACQUIRE);
}

static int host_map_windows(XgcHost *h)
{
    uint32_t i;

    if (h->ctl->nwindows > XGC_HOST_MAX_WINDOWS) {
        return -1;
    }
    for (i = 0; i < h->ctl->nwindows; i++) {
        XgcHostWindow *w = &h->ctl->windows[i];
        bool is_io = (w->flags & XGC_WIN_IO) != 0;
        bool live = (w->flags & XGC_WIN_LIVE) != 0;
        bool direct = (w->flags & XGC_WIN_DIRECT) != 0;
        XgcHostWindowCtx *ctx;
        uint8_t *shadow;

        if (w->size == 0) {
            return -1;
        }
        shadow = h->shadow + w->shadow;
        if (!live && (w->flags & XGC_WIN_RAM_ALIAS) == 0 &&
            w->shadow + w->size > h->ctl->shadow_size) {
            fprintf(stderr, "window %u shadow overflow\n", i);
            return -1;
        }
        if (direct) {
            if (xgc_map_direct(h->cpu, w->base, w->size, shadow,
                               w->priority) != XGC_OK) {
                fprintf(stderr, "window %u direct failed\n", i);
                return -1;
            }
            continue;
        }
        if ((w->flags & XGC_WIN_RAM_ALIAS) != 0) {
            if (xgc_map_ram_alias(h->cpu, w->base, w->size, 0,
                                  w->priority) != XGC_OK) {
                fprintf(stderr, "window %u ram-alias failed\n", i);
                return -1;
            }
            continue;
        }
        ctx = calloc(1, sizeof(*ctx));
        ctx->host = h;
        ctx->flags = w->flags;

        if (is_io) {
            int rc = live
                ? xgc_map_io_ring_live(h->cpu, w->base, w->size,
                                       host_io_read, ctx)
                : xgc_map_io_ring_shadow(h->cpu, w->base, w->size, shadow);
            if (rc != XGC_OK) {
                fprintf(stderr, "window %u io failed\n", i);
                return -1;
            }
        } else {
            int rc = live
                ? xgc_map_mmio_ring_live(h->cpu, w->base, w->size,
                                         host_io_read, ctx)
                : xgc_map_mmio_ring_shadow(h->cpu, w->base, w->size, shadow);
            if (rc != XGC_OK) {
                fprintf(stderr, "window %u mmio failed\n", i);
                return -1;
            }
        }
    }
    return 0;
}

int main(int argc, char **argv)
{
    HANDLE mapping;
    uint8_t *base;
    XgcHostControl *ctl;
    XgcHost host;
    uint64_t ctl_offset;

    if (argc < 3) {
        fprintf(stderr, "usage: %s <shared-section-name> <control-offset>\n",
                argv[0]);
        return 2;
    }

    ctl_offset = _strtoui64(argv[2], NULL, 0);
    mapping = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, argv[1]);
    if (!mapping) {
        fprintf(stderr, "OpenFileMapping(%s) failed: %lu\n", argv[1],
                (unsigned long)GetLastError());
        return 3;
    }
    base = MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, 0);
    if (!base) {
        fprintf(stderr, "MapViewOfFile failed: %lu\n",
                (unsigned long)GetLastError());
        CloseHandle(mapping);
        return 3;
    }

    ctl = (XgcHostControl *)(base + ctl_offset);
    if (ctl->magic != XGC_HOST_MAGIC || ctl->version != XGC_HOST_VERSION) {
        fprintf(stderr, "shared control magic/version mismatch: %08x/%u\n",
                ctl->magic, ctl->version);
        return 4;
    }

    memset(&host, 0, sizeof(host));
    host.base = base;
    host.ctl = ctl;
    host.shadow = (uint8_t *)ctl + XGC_HOST_SHADOW;
    host.ring_capacity = ctl->ring_capacity;
    if (host.ring_capacity == 0 || host.ring_capacity > 4096) {
        fprintf(stderr, "bad ring capacity %u\n", ctl->ring_capacity);
        return 4;
    }
    host.ring = (XgcMmioAccess *)(host.shadow + ctl->shadow_size);
    host.regs = (XgcRegs *)((uint8_t *)host.ring +
                            host.ring_capacity * sizeof(XgcMmioAccess));

    if (xgc_init() != XGC_OK) {
        fprintf(stderr, "xgc_init failed\n");
        return 4;
    }
    host.cpu = xgc_create();
    if (!host.cpu) {
        fprintf(stderr, "xgc_create failed\n");
        return 4;
    }
    xgc_set_irq_callback(host.cpu, host_irq_ack, &host);
    xgc_set_mmio_sink(host.cpu, host_ring_push, &host);
    if (ctl->ram_size == 0 ||
        xgc_map_ram(host.cpu, 0, ctl->ram_size, base) != XGC_OK ||
        host_map_windows(&host) != 0) {
        fprintf(stderr, "map failed\n");
        return 4;
    }

    ctl->ready = 1;

    for (;;) {
        while (ctl->status != 1 && !ctl->quit) {
            Sleep(1);
        }
        if (ctl->quit) {
            break;
        }

        {
            uint64_t executed = 0;
            int rc;

            if (ctl->reset) {
                if (ctl->reset == 2) {
                    xgc_reset_real(host.cpu, 0x1000, 0x8000);
                } else {
                    xgc_reset(host.cpu);
                }
                ctl->reset = 0;
            }
            if (ctl->irq_pending) {
                xgc_set_irq(host.cpu, ctl->irq_vector);
                ctl->irq_pending = 0;
            }

            rc = xgc_run(host.cpu, ctl->run_budget, &executed);

            ctl->executed = executed;
            ctl->rc = rc;
            ctl->dropped = xgc_mmio_dropped(host.cpu);
            xgc_get_regs(host.cpu, host.regs);
            ctl->status = 2;
        }
    }

    xgc_destroy(host.cpu);
    UnmapViewOfFile(base);
    CloseHandle(mapping);
    return host.error ? 5 : 0;
}
