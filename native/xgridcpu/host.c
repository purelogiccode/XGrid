/*
 * XCN1 native CPU host process.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Opens a named shared section created by the C# device model, maps guest RAM
 * at GPA 0 and the MMIO ring window at 0xF0000000, then runs the vCPU in
 * slices. The C# side owns the read shadow and drains the write ring between
 * slices; no managed code is called from the TCG/softmmu stack.
 */
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <stddef.h>

#include "xgridcpu.h"

#define XGC_HOST_MAGIC   0x58474348u
#define XGC_HOST_VERSION 1u
#define XGC_HOST_RAM     (1024u * 1024u)
#define XGC_HOST_SECTION (XGC_HOST_RAM + 0x2000u)

typedef struct XgcHostControl {
    uint32_t magic;
    uint32_t version;
    volatile uint32_t ready;
    volatile uint32_t quit;
    volatile uint32_t status;   /* 1 = run requested, 2 = stopped */
    uint32_t run_budget;
    uint64_t executed;
    int32_t rc;
    uint32_t ring_count;
    volatile uint32_t irq_pending;
    uint32_t irq_vector;
    volatile uint32_t reset;
    uint32_t reserved[3];
    uint8_t shadow[0x1000];
    XgcMmioAccess ring[64];
    XgcRegs regs;
} XgcHostControl;

_Static_assert(offsetof(XgcHostControl, executed) == 24, "executed offset");
_Static_assert(offsetof(XgcHostControl, rc) == 32, "rc offset");
_Static_assert(offsetof(XgcHostControl, irq_pending) == 40, "irq offset");
_Static_assert(offsetof(XgcHostControl, irq_vector) == 44, "vector offset");
_Static_assert(offsetof(XgcHostControl, reset) == 48, "reset offset");
_Static_assert(offsetof(XgcHostControl, shadow) == 64, "shadow offset");
_Static_assert(offsetof(XgcHostControl, ring) == 4160, "ring offset");
_Static_assert(offsetof(XgcHostControl, regs) == 5696, "regs offset");
_Static_assert(sizeof(XgcRegs) == 52, "XgcRegs size");
_Static_assert(sizeof(XgcHostControl) <= 0x2000, "control fits");

int main(int argc, char **argv)
{
    HANDLE mapping;
    uint8_t *base;
    XgcHostControl *ctl;
    XgcCpu *cpu;

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

    ctl = (XgcHostControl *)(base + XGC_HOST_RAM);
    if (ctl->magic != XGC_HOST_MAGIC || ctl->version != XGC_HOST_VERSION) {
        fprintf(stderr, "shared control magic/version mismatch: %08x/%u\n",
                ctl->magic, ctl->version);
        return 4;
    }

    if (xgc_init() != XGC_OK) {
        fprintf(stderr, "xgc_init failed\n");
        return 4;
    }
    cpu = xgc_create();
    if (!cpu) {
        fprintf(stderr, "xgc_create failed\n");
        return 4;
    }
    if (xgc_map_ram(cpu, 0, XGC_HOST_RAM, base) != XGC_OK ||
        xgc_map_mmio_ring_shadow(cpu, 0xF0000000u, 0x1000, ctl->shadow) != XGC_OK) {
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
                xgc_reset_real(cpu, 0x1000, 0x8000);
                ctl->reset = 0;
            }
            if (ctl->irq_pending) {
                xgc_set_irq(cpu, ctl->irq_vector);
                ctl->irq_pending = 0;
            }


            rc = xgc_run(cpu, ctl->run_budget, &executed);
            int n = 0;
            XgcMmioAccess access;

            ctl->executed = executed;
            ctl->rc = rc;
            while (n < 64 && xgc_mmio_take(cpu, &access) != 0) {
                ctl->ring[n++] = access;
            }
            ctl->ring_count = (uint32_t)n;
            xgc_get_regs(cpu, &ctl->regs);
            ctl->status = 2;
        }
    }

    xgc_destroy(cpu);
    UnmapViewOfFile(base);
    CloseHandle(mapping);
    return 0;
}
