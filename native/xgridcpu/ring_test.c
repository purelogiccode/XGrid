/*
 * XCN1 MMIO shadow+ring C smoke test.
 * SPDX-License-Identifier: GPL-2.0-or-later
 */
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#ifdef _WIN32
#include <malloc.h>
#endif

#include "xgridcpu.h"

static void *alloc_ram(size_t size)
{
#ifdef _WIN32
    return _aligned_malloc(size, 4096);
#else
    void *p = NULL;
    if (posix_memalign(&p, 4096, size) != 0) {
        return NULL;
    }
    return p;
#endif
}

static void free_ram(void *p)
{
#ifdef _WIN32
    _aligned_free(p);
#else
    free(p);
#endif
}

int main(void)
{
    static const uint8_t code[] = {
        0x66, 0xC7, 0x06, 0x00, 0x20, 0x78, 0x56, 0x34, 0x12,
        0x67, 0x66, 0xA1, 0x00, 0x00, 0x00, 0xF0,
        0x67, 0x66, 0xA3, 0x04, 0x00, 0x00, 0xF0,
        0x66, 0xB9, 0x0A, 0x00, 0x00, 0x00,
        0x66, 0x49,
        0x75, 0xFC,
        0xEB, 0xFE
    };
    uint8_t *ram;
    XgcCpu *cpu;
    XgcRegs regs;
    XgcMmioAccess access;
    uint64_t executed = 0;
    int rc;
    int writes = 0;
    uint32_t write_gpa = 0;
    uint64_t write_value = 0;

    if (xgc_init() != XGC_OK) {
        return 1;
    }
    ram = alloc_ram(1024 * 1024);
    if (!ram) {
        return 1;
    }
    memset(ram, 0x90, 1024 * 1024);
    memcpy(ram + 0x1000, code, sizeof(code));

    cpu = xgc_create();
    if (!cpu) {
        return 1;
    }
    if (xgc_map_ram(cpu, 0, 1024 * 1024, ram) != XGC_OK ||
        xgc_map_mmio_ring(cpu, 0xF0000000u, 0x1000) != XGC_OK) {
        return 1;
    }
    xgc_mmio_set_read(cpu, 0xF0000000u, 4, 0xCAFEBABEull);

    rc = xgc_run(cpu, 100000, &executed);
    while (xgc_mmio_take(cpu, &access) != 0) {
        if (access.is_write) {
            writes++;
            write_gpa = access.gpa;
            write_value = access.value;
        }
    }
    xgc_get_regs(cpu, &regs);

    printf("ring: rc=%d executed=%llu writes=%d write[0x%08X]=0x%llX\n",
           rc, (unsigned long long)executed, writes, write_gpa,
           (unsigned long long)write_value);
    printf("ring: eax=0x%08X ecx=0x%08X eip=0x%08X\n",
           regs.eax, regs.ecx, regs.eip);

    xgc_destroy(cpu);
    free_ram(ram);

    if (rc != 0 || executed != 100000u || writes != 1 ||
        write_gpa != 0xF0000004u || write_value != 0xCAFEBABEull ||
        regs.eax != 0xCAFEBABEu || regs.ecx != 0 || regs.eip != 0x1021u) {
        fprintf(stderr, "RING FAIL\n");
        return 1;
    }
    printf("XCN1 MMIO SHADOW+RING C PASS\n");
    return 0;
}
