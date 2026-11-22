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
#define XGC_HOST_VERSION 2u

#define XGC_HOST_RAM      (1024u * 1024u)
#define XGC_HOST_CTRL     XGC_HOST_RAM
#define XGC_HOST_SHADOW   0x100u
#define XGC_HOST_SHADOWSZ 0x10000u
#define XGC_HOST_RING     (XGC_HOST_SHADOW + XGC_HOST_SHADOWSZ)
#define XGC_HOST_RINGSZ   (64u * 24u)
#define XGC_HOST_REGS     (XGC_HOST_RING + XGC_HOST_RINGSZ)
#define XGC_HOST_SECTION  (XGC_HOST_REGS + 0x100u)

#define XGC_HOST_MAX_WINDOWS 8
#define XGC_WIN_IO           0x1u  /* the base is an I/O port */
#define XGC_WIN_LIVE         0x2u  /* reads are serviced by C# */

#define XGC_IO_IDLE     0u
#define XGC_IO_REQUEST  1u
#define XGC_IO_DONE     2u
#define XGC_IO_ERROR    0x80000000u

typedef struct XgcHostWindow {
    uint32_t base;
    uint32_t size;
    uint32_t flags;
    uint32_t shadow; /* byte offset into the section's shadow area */
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
XGC_ASSERT_OFF(io_state, 184);
XGC_ASSERT_OFF(io_flags, 188);
XGC_ASSERT_OFF(io_addr, 192);
XGC_ASSERT_OFF(io_size, 196);
XGC_ASSERT_OFF(io_value, 200);
XGC_ASSERT_OFF(io_icount, 208);
_Static_assert(sizeof(XgcHostWindow) == 16, "window size");
_Static_assert(offsetof(XgcHostControl, windows) + 8 * 16 <= 0x100,
               "header fits");

typedef struct XgcHost {
    uint8_t *base;
    XgcHostControl *ctl;
    XgcCpu *cpu;
    XgcMmioAccess *ring;
    XgcRegs *regs;
    bool error;
} XgcHost;

typedef struct XgcHostWindowCtx {
    XgcHost *host;
    uint32_t flags;
} XgcHostWindowCtx;

/* Move every queued write into the section so C# sees it before a live read
 * (and at slice end). */
static void host_publish_ring(XgcHost *h)
{
    XgcMmioAccess access;

    while (h->ctl->ring_count < 64 && xgc_mmio_take(h->cpu, &access) != 0) {
        h->ring[h->ctl->ring_count++] = access;
    }
}

static uint64_t host_io_read(void *opaque, uint32_t addr, unsigned size)
{
    XgcHostWindowCtx *ctx = opaque;
    XgcHost *h = ctx->host;
    XgcHostControl *ctl = h->ctl;
    ULONGLONG deadline;

    host_publish_ring(h);

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
        XgcHostWindowCtx *ctx;
        uint8_t *shadow;

        if (w->size == 0) {
            return -1;
        }
        if (!live) {
            if (w->shadow + w->size > XGC_HOST_SHADOWSZ) {
                return -1;
            }
            shadow = h->base + XGC_HOST_CTRL + XGC_HOST_SHADOW + w->shadow;
        } else {
            shadow = NULL;
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
                return -1;
            }
        } else {
            int rc = live
                ? xgc_map_mmio_ring_live(h->cpu, w->base, w->size,
                                         host_io_read, ctx)
                : xgc_map_mmio_ring_shadow(h->cpu, w->base, w->size, shadow);
            if (rc != XGC_OK) {
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

    if (argc < 2) {
        fprintf(stderr, "usage: %s <shared-section-name>\n", argv[0]);
        return 2;
    }

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

    ctl = (XgcHostControl *)(base + XGC_HOST_CTRL);
    if (ctl->magic != XGC_HOST_MAGIC || ctl->version != XGC_HOST_VERSION) {
        fprintf(stderr, "shared control magic/version mismatch: %08x/%u\n",
                ctl->magic, ctl->version);
        return 4;
    }

    memset(&host, 0, sizeof(host));
    host.base = base;
    host.ctl = ctl;
    host.ring = (XgcMmioAccess *)(base + XGC_HOST_CTRL + XGC_HOST_RING);
    host.regs = (XgcRegs *)(base + XGC_HOST_CTRL + XGC_HOST_REGS);

    if (xgc_init() != XGC_OK) {
        fprintf(stderr, "xgc_init failed\n");
        return 4;
    }
    host.cpu = xgc_create();
    if (!host.cpu) {
        fprintf(stderr, "xgc_create failed\n");
        return 4;
    }
    if (xgc_map_ram(host.cpu, 0, XGC_HOST_RAM, base) != XGC_OK ||
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
                xgc_reset_real(host.cpu, 0x1000, 0x8000);
                ctl->reset = 0;
            }
            if (ctl->irq_pending) {
                xgc_set_irq(host.cpu, ctl->irq_vector);
                ctl->irq_pending = 0;
            }

            rc = xgc_run(host.cpu, ctl->run_budget, &executed);

            ctl->executed = executed;
            ctl->rc = rc;
            host_publish_ring(&host);
            xgc_get_regs(host.cpu, host.regs);
            ctl->status = 2;
        }
    }

    xgc_destroy(host.cpu);
    UnmapViewOfFile(base);
    CloseHandle(mapping);
    return host.error ? 5 : 0;
}
