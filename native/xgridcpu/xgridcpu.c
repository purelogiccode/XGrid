/*
 * XGrid native CPU core - C ABI over the vendored xemu/QEMU TCG i386 core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Ported from: xemu 0.8.139 / QEMU 10.2.0 (TCG i386 CPU, softmmu, TB cache).
 *
 * The XCN0 spike: create a vCPU without qemu_init()/a QEMU machine, map guest
 * RAM and MMIO regions, run a bounded instruction budget and expose the
 * architectural registers. The C# seam owns the devices and dispatch.
 */
#include "qemu/osdep.h"
#include "qemu/module.h"
#include "qemu/cacheinfo.h"
#include "qemu/seqlock.h"
#include "qapi/error.h"
#include "qom/object.h"
#include "hw/boards.h"
#include "hw/core/cpu.h"
#include "system/address-spaces.h"
#include "system/memory.h"
#include "system/tcg.h"
#include "system/cpu-timers-internal.h"
#include "exec/cpu-common.h"
#include "exec/icount.h"
#include "tcg/startup.h"
#include "accel/tcg/internal-common.h"
#include "target/i386/cpu.h"
#include "target/i386/cpu-qom.h"

#include "xgridcpu.h"

/* Not exported by a public QEMU header; implemented in
 * accel/tcg/translate-all.c. */
void tb_flush(CPUState *cpu);

typedef struct XgcMmio {
    uint32_t gpa;
    uint32_t size;
    XgcMmioReadFn read;
    XgcMmioWriteFn write;
    void *opaque;
} XgcMmio;

struct XgcCpu {
    CPUState *cpu;
    MemoryRegion *ram[4];
    int nram;
    MemoryRegion *mmio_mr[8];
    XgcMmio *mmio[8];
    int nmmio;
};

static bool g_initialized;

static uint64_t xgc_mmio_read(void *opaque, hwaddr addr, unsigned size)
{
    XgcMmio *m = opaque;
    if (!m->read) {
        return 0;
    }
    return m->read(m->opaque, m->gpa + (uint32_t)addr, size);
}

static void xgc_mmio_write(void *opaque, hwaddr addr, uint64_t value,
                           unsigned size)
{
    XgcMmio *m = opaque;
    if (m->write) {
        m->write(m->opaque, m->gpa + (uint32_t)addr, size, value);
    }
}

static const MemoryRegionOps xgc_mmio_ops = {
    .read = xgc_mmio_read,
    .write = xgc_mmio_write,
    .endianness = DEVICE_NATIVE_ENDIAN,
    .valid.min_access_size = 1,
    .valid.max_access_size = 8,
    .impl.min_access_size = 1,
    .impl.max_access_size = 8,
};

int xgc_init(void)
{
    MachineState *machine;

    if (g_initialized) {
        return XGC_OK;
    }

    /* QOM types (CPU models, the "none" machine) are registered by
     * type_init() constructors; run them without qemu_init(). */
    qemu_init_cpu_list();
    module_call_init(MODULE_INIT_QOM);

    /* cpu_create() -> cpu_common_realizefn() -> qdev_get_machine(), which
     * asserts unless a machine object is present. The "none" machine has no
     * devices and its init is never run here. */
    machine = MACHINE(object_new("none-machine"));
    if (!machine) {
        return XGC_ERR_INIT;
    }
    object_property_add_child(object_get_root(), "machine", OBJECT(machine));
    current_machine = machine;

    /* system_memory/system_io + the target page size. */
    cpu_exec_init_all();

    /* The TCG accelerator normally does this from tcg_init_machine(). */
    tcg_allowed = true;
    /* qemu_init_cpu_loop()/flush_info_init() normally probe these. */
    qemu_icache_linesize = 64;
    qemu_icache_linesize_log = 6;
    qemu_dcache_linesize = 64;
    qemu_dcache_linesize_log = 6;
    page_init();
    tb_htable_init();
    tcg_init(32 * 1024 * 1024, 0, 1);
    tcg_prologue_init();

    /* Deterministic instruction accounting: one nanosecond per instruction
     * (the product patch will map the Xbox 733 MHz TSC explicitly). */
    use_icount = ICOUNT_PRECISE;
    timers_state.icount_time_shift = 0;

    g_initialized = true;
    return XGC_OK;
}

static void xgc_set_real_mode(XgcCpu *c, uint32_t eip, uint32_t esp)
{
    CPUX86State *env = cpu_env(c->cpu);
    int i;

    cpu_reset(c->cpu);

    for (i = 0; i < 8; i++) {
        env->regs[i] = 0;
    }
    env->regs[R_ESP] = esp;
    env->regs[R_EBP] = esp;
    env->eip = eip;
    env->eflags = 0x2;
    env->hflags = 0;
    env->cr[0] = 0x10; /* real mode, ET */
    env->cr[4] = 0;

    for (i = 0; i < 6; i++) {
        env->segs[i].selector = 0;
        env->segs[i].base = 0;
        env->segs[i].limit = 0xFFFF;
        env->segs[i].flags = 0;
    }
    env->segs[R_CS].flags = 0x9B; /* present, code, readable */
    env->segs[R_SS].flags = 0x93; /* present, data, writable */
    env->segs[R_DS].flags = 0x93;
    env->segs[R_ES].flags = 0x93;

    xgc_flush_tb(c);
}

XgcCpu *xgc_create(void)
{
    XgcCpu *c;
    ObjectClass *oc;
    const char *type;

    if (!g_initialized && xgc_init() != XGC_OK) {
        return NULL;
    }

    oc = cpu_class_by_name(TYPE_X86_CPU, "pentium3");
    if (!oc) {
        return NULL;
    }
    type = object_class_get_name(oc);
    if (!type) {
        return NULL;
    }

    c = g_new0(XgcCpu, 1);
    c->cpu = cpu_create(type);
    if (!c->cpu) {
        g_free(c);
        return NULL;
    }
    c->cpu->as = &address_space_memory;
    xgc_set_real_mode(c, 0x1000, 0x8000);
    return c;
}

void xgc_destroy(XgcCpu *c)
{
    int i;

    if (!c) {
        return;
    }
    for (i = 0; i < c->nram; i++) {
        memory_region_destroy(c->ram[i]);
        g_free(c->ram[i]);
    }
    for (i = 0; i < c->nmmio; i++) {
        memory_region_destroy(c->mmio_mr[i]);
        g_free(c->mmio_mr[i]);
        g_free(c->mmio[i]);
    }
    if (c->cpu) {
        object_unref(OBJECT(c->cpu));
    }
    g_free(c);
}

int xgc_map_ram(XgcCpu *c, uint32_t gpa, uint32_t size, void *host)
{
    MemoryRegion *mr;

    if (!c || !host || size == 0 || c->nram >= 4) {
        return XGC_ERR_ARG;
    }
    mr = g_new0(MemoryRegion, 1);
    memory_region_init_ram_ptr(mr, NULL, "xgrid-ram", size, host);
    memory_region_add_subregion(get_system_memory(), gpa, mr);
    c->ram[c->nram++] = mr;
    xgc_flush_tb(c);
    return XGC_OK;
}

int xgc_map_mmio(XgcCpu *c, uint32_t gpa, uint32_t size, XgcMmioReadFn read,
                 XgcMmioWriteFn write, void *opaque)
{
    MemoryRegion *mr;
    XgcMmio *m;

    if (!c || size == 0 || (!read && !write) || c->nmmio >= 8) {
        return XGC_ERR_ARG;
    }
    m = g_new0(XgcMmio, 1);
    m->gpa = gpa;
    m->size = size;
    m->read = read;
    m->write = write;
    m->opaque = opaque;

    mr = g_new0(MemoryRegion, 1);
    memory_region_init_io(mr, NULL, &xgc_mmio_ops, m, "xgrid-mmio", size);
    memory_region_add_subregion(get_system_memory(), gpa, mr);
    c->mmio_mr[c->nmmio] = mr;
    c->mmio[c->nmmio] = m;
    c->nmmio++;
    xgc_flush_tb(c);
    return XGC_OK;
}

int xgc_reset_real(XgcCpu *c, uint32_t eip, uint32_t esp)
{
    if (!c) {
        return XGC_ERR_ARG;
    }
    xgc_set_real_mode(c, eip, esp);
    return XGC_OK;
}

int xgc_run(XgcCpu *c, uint32_t max_insns, uint64_t *executed)
{
    uint64_t done = 0;

    if (executed) {
        *executed = 0;
    }
    if (!c) {
        return XGC_ERR_ARG;
    }

    while (done < max_insns) {
        uint32_t chunk = max_insns - done;
        int64_t before, after;
        int ret;
        uint64_t n;

        if (chunk > 0x7FFF) {
            chunk = 0x7FFF;
        }

        before = icount_get_raw();
        c->cpu->neg.icount_decr.u32 = chunk;
        c->cpu->icount_extra = 0;
        c->cpu->icount_budget = 0;
        c->cpu->exception_index = -1;

        ret = cpu_exec(c->cpu);
        icount_update(c->cpu);
        after = icount_get_raw();

        n = (uint64_t)(after - before);
        if (n == 0) {
            /* The TB prologue decremented the local budget; recover the
             * executed count from the remaining 16-bit counter. */
            int32_t remaining = (int16_t)c->cpu->neg.icount_decr.u16.low +
                                (int32_t)c->cpu->icount_extra;
            if (remaining >= 0 && (uint32_t)remaining <= chunk) {
                n = chunk - (uint32_t)remaining;
            }
        }
        done += n;

        if (ret == EXCP_HALTED) {
            if (executed) {
                *executed = done;
            }
            return XGC_ERR_HALTED;
        }
        if (n == 0) {
            break;
        }
    }

    if (executed) {
        *executed = done;
    }
    return XGC_OK;
}

void xgc_get_regs(XgcCpu *c, XgcRegs *r)
{
    CPUX86State *env;

    if (!c || !r) {
        return;
    }
    env = cpu_env(c->cpu);
    r->eax = env->regs[R_EAX];
    r->ecx = env->regs[R_ECX];
    r->edx = env->regs[R_EDX];
    r->ebx = env->regs[R_EBX];
    r->esp = env->regs[R_ESP];
    r->ebp = env->regs[R_EBP];
    r->esi = env->regs[R_ESI];
    r->edi = env->regs[R_EDI];
    r->eip = env->eip;
    r->eflags = env->eflags;
    r->cs = env->segs[R_CS].selector;
    r->ds = env->segs[R_DS].selector;
    r->es = env->segs[R_ES].selector;
    r->fs = env->segs[R_FS].selector;
    r->gs = env->segs[R_GS].selector;
    r->ss = env->segs[R_SS].selector;
}

void xgc_set_regs(XgcCpu *c, const XgcRegs *r)
{
    CPUX86State *env;

    if (!c || !r) {
        return;
    }
    env = cpu_env(c->cpu);
    env->regs[R_EAX] = r->eax;
    env->regs[R_ECX] = r->ecx;
    env->regs[R_EDX] = r->edx;
    env->regs[R_EBX] = r->ebx;
    env->regs[R_ESP] = r->esp;
    env->regs[R_EBP] = r->ebp;
    env->regs[R_ESI] = r->esi;
    env->regs[R_EDI] = r->edi;
    env->eip = r->eip;
    env->eflags = r->eflags;
    env->segs[R_CS].selector = r->cs;
    env->segs[R_DS].selector = r->ds;
    env->segs[R_ES].selector = r->es;
    env->segs[R_FS].selector = r->fs;
    env->segs[R_GS].selector = r->gs;
    env->segs[R_SS].selector = r->ss;
    xgc_flush_tb(c);
}

void xgc_flush_tb(XgcCpu *c)
{
    if (c && c->cpu) {
        tb_flush(c->cpu);
    }
}

uint64_t xgc_icount(XgcCpu *c)
{
    (void)c;
    return (uint64_t)icount_get_raw();
}
