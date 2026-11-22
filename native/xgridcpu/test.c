/*
 * XGrid native CPU core - XCN0 C smoke test.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Boots a real-mode stub at 0x1000, exercises guest RAM and one MMIO region,
 * runs a 2M-instruction loop, and checks the registers. This is the C half of
 * the XCN0 spike; the C# P/Invoke test is the acceptance.
 */
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#ifdef _WIN32
#include <malloc.h>
#endif

#include "xgridcpu.h"

static uint64_t g_mmio_reads;
static uint64_t g_mmio_writes;
static uint64_t g_mmio_last;
static uint32_t g_mmio_last_addr;

static uint64_t mmio_read(void *opaque, uint32_t gpa, uint32_t size)
{
    (void)opaque;
    (void)size;
    g_mmio_reads++;
    return 0xCAFEBABEull;
}

static void mmio_write(void *opaque, uint32_t gpa, uint32_t size,
                       uint64_t value)
{
    (void)opaque;
    (void)size;
    g_mmio_writes++;
    g_mmio_last = value;
    g_mmio_last_addr = gpa;
}

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
    /* Real-mode code at 0x1000:
     *   mov dword ptr [0x2000], 0x12345678
     *   mov eax, [0xF0000000]
     *   mov [0xF0000004], eax
     *   mov ecx, 1000000
     * loop:
     *   dec ecx
     *   jnz loop
     *   hlt
     */
    static const uint8_t code[] = {
        0x66, 0xC7, 0x06, 0x00, 0x20, 0x78, 0x56, 0x34, 0x12,
        0x67, 0x66, 0xA1, 0x00, 0x00, 0x00, 0xF0,
        0x67, 0x66, 0xA3, 0x04, 0x00, 0x00, 0xF0,
        0x66, 0xB9, 0x40, 0x42, 0x0F, 0x00,
        0x66, 0x49,
        0x75, 0xFC,
        0xF4
    };
    uint8_t *ram;
    XgcCpu *cpu;
    XgcRegs regs;
    uint64_t executed = 0;
    int rc;
    uint32_t ram_word;

    if (xgc_init() != XGC_OK) {
        fprintf(stderr, "xgc_init failed\n");
        return 1;
    }

    ram = alloc_ram(1024 * 1024);
    if (!ram) {
        fprintf(stderr, "ram alloc failed\n");
        return 1;
    }
    memset(ram, 0x90, 1024 * 1024);
    memcpy(ram + 0x1000, code, sizeof(code));

    cpu = xgc_create();
    if (!cpu) {
        fprintf(stderr, "xgc_create failed\n");
        return 1;
    }
    if (xgc_map_ram(cpu, 0, 1024 * 1024, ram) != XGC_OK) {
        fprintf(stderr, "map_ram failed\n");
        return 1;
    }
    if (xgc_map_mmio(cpu, 0xF0000000u, 0x1000, mmio_read, mmio_write,
                     NULL) != XGC_OK) {
        fprintf(stderr, "map_mmio failed\n");
        return 1;
    }

    rc = xgc_run(cpu, 3000000, &executed);
    xgc_get_regs(cpu, &regs);

    ram_word = *(uint32_t *)(ram + 0x2000);
    printf("run rc=%d executed=%llu\n", rc,
           (unsigned long long)executed);
    printf("ram[0x2000]=0x%08X (want 0x12345678)\n", ram_word);
    printf("mmio reads=%llu writes=%llu last=0x%llX addr=0x%08X\n",
           (unsigned long long)g_mmio_reads,
           (unsigned long long)g_mmio_writes,
           (unsigned long long)g_mmio_last, g_mmio_last_addr);
    printf("eax=0x%08X ecx=0x%08X eip=0x%08X eflags=0x%08X\n",
           regs.eax, regs.ecx, regs.eip, regs.eflags);

    xgc_destroy(cpu);
    free_ram(ram);

    if (ram_word != 0x12345678u) {
        fprintf(stderr, "FAIL: guest store\n");
        return 1;
    }
    if (g_mmio_reads != 1 || g_mmio_writes != 1 ||
        g_mmio_last != 0xCAFEBABEu || g_mmio_last_addr != 0xF0000004u) {
        fprintf(stderr, "FAIL: mmio\n");
        return 1;
    }
    if (regs.eax != 0xCAFEBABEu || regs.ecx != 0) {
        fprintf(stderr, "FAIL: registers\n");
        return 1;
    }
    printf("XCN0 C SPIKE PASS\n");
    return 0;
}
