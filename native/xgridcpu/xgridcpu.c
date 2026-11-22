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
#include "qemu/accel.h"
#include "accel/accel-ops.h"
#include "accel/accel-cpu-ops.h"
#include "qapi/error.h"
#include "qom/object.h"
#include "hw/boards.h"
#include "hw/core/cpu.h"
#include "hw/qdev-core.h"
#include "system/address-spaces.h"
#include "system/memory.h"
#include "system/tcg.h"
#include "system/system.h"
#include "system/cpu-timers-internal.h"
#include "exec/cpu-common.h"
#include "exec/icount.h"
#include "tcg/startup.h"
#include "accel/tcg/internal-common.h"
#include "target/i386/cpu.h"
#include "target/i386/cpu-qom.h"

#include "xgridcpu.h"

/* Not exported by a public QEMU header; implemented in
 * accel/tcg/tb-maint.c. */
void tb_flush__exclusive_or_serial(void);

typedef struct XgcMmio {
    uint32_t gpa;
    uint32_t size;
    XgcMmioReadFn read;
    XgcMmioWriteFn write;
    void *opaque;
} XgcMmio;

#define XGC_RING_CAP 64

typedef struct XgcRing {
    struct XgcCpu *cpu;
    uint32_t base;
    uint32_t size;
    uint8_t *shadow;
    bool own_shadow;
} XgcRing;

struct XgcCpu {
    CPUState *cpu;
    MemoryRegion *ram[4];
    int nram;
    MemoryRegion *mmio_mr[8];
    XgcMmio *mmio[8];
    int nmmio;
    MemoryRegion *ring_mr[8];
    XgcRing *rings[8];
    int nrings;
    XgcMmioAccess ring[XGC_RING_CAP];
    int ring_head;
    int ring_tail;
};

static bool g_initialized;

/* XCN0: the host drives cpu_exec() itself; the accel ops only need the
 * mandatory create_vcpu_thread/handle_interrupt entries so the CPU reset
 * path sees a valid table. */
static void xgc_vcpu_thread(CPUState *cpu)
{
    qemu_sem_init(&cpu->sem, 0);
    cpu->created = true;
}

static void xgc_handle_interrupt(CPUState *cpu, int mask)
{
    (void)cpu;
    (void)mask;
}

static const AccelOpsClass xgc_accel_ops = {
    .create_vcpu_thread = xgc_vcpu_thread,
    .handle_interrupt = xgc_handle_interrupt,
};

void cpus_register_accel(const AccelOpsClass *ops);

#define XGC_TRACE(...) \
    do { \
        if (getenv("XGRID_CPU_TRACE")) { \
            fprintf(stderr, __VA_ARGS__); \
            fflush(stderr); \
        } \
    } while (0)

__attribute__((force_align_arg_pointer))
static uint64_t xgc_mmio_read(void *opaque, hwaddr addr, unsigned size)
{
    XgcMmio *m = opaque;
    if (!m->read) {
        return 0;
    }
    return m->read(m->opaque, m->gpa + (uint32_t)addr, size);
}

__attribute__((force_align_arg_pointer))
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

static bool xgc_ring_push(XgcCpu *c, uint32_t gpa, uint32_t size,
                          uint32_t is_write, uint64_t value)
{
    int next = (c->ring_tail + 1) % XGC_RING_CAP;

    if (next == c->ring_head) {
        return false;
    }
    c->ring[c->ring_tail].gpa = gpa;
    c->ring[c->ring_tail].size = size;
    c->ring[c->ring_tail].is_write = is_write;
    c->ring[c->ring_tail]._pad = 0;
    c->ring[c->ring_tail].value = value;
    c->ring_tail = next;
    return true;
}

static uint64_t xgc_ring_read(void *opaque, hwaddr addr, unsigned size)
{
    XgcRing *r = opaque;
    uint32_t off = (uint32_t)addr;
    uint64_t value = 0;

    XGC_TRACE("ring read gpa=%x size=%u\n", r->base + off, size);
    if (off + size > r->size) {
        return 0;
    }
    memcpy(&value, r->shadow + off, size);
    return value;
}

static void xgc_ring_write(void *opaque, hwaddr addr, uint64_t value,
                           unsigned size)
{
    XgcRing *r = opaque;
    XgcCpu *c = r->cpu;

    XGC_TRACE("ring write gpa=%x size=%u value=%llx\n",
              r->base + (uint32_t)addr, size, (unsigned long long)value);
    xgc_ring_push(c, r->base + (uint32_t)addr, size, 1, value);
}

static const MemoryRegionOps xgc_ring_ops = {
    .read = xgc_ring_read,
    .write = xgc_ring_write,
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

    XGC_TRACE("xgc_init: enter\n");

    /* QEMU's normal init order: locks, RCU, BQL, the trace/QOM/migration
     * module lists and the runstate. qemu_init() itself is not called. */
    XGC_TRACE("xgc_init: qemu_init_subsystems\n");
    qemu_init_subsystems();
    cpus_register_accel(&xgc_accel_ops);
    XGC_TRACE("xgc_init: object_new(none-machine)\n");
    XGC_TRACE("  root=%p\n", (void *)object_get_root());
    {
        ObjectClass *oc_none = object_class_by_name("none-machine");
        XGC_TRACE("  class none-machine=%p\n", (void *)oc_none);
        {
            ObjectClass *oc_machine = object_class_by_name(TYPE_MACHINE);
            XGC_TRACE("  class machine=%p\n", (void *)oc_machine);
        }
        {
            ObjectClass *oc_cpu = object_class_by_name("i386-cpu");
            XGC_TRACE("  class i386-cpu=%p\n", (void *)oc_cpu);
        }
    }

    /* cpu_create() -> cpu_common_realizefn() -> qdev_get_machine(), which
     * asserts unless a machine object is present. The "none" machine has no
     * devices and its init is never run here. */
    machine = MACHINE(object_new("none-machine"));
    if (!machine) {
        return XGC_ERR_INIT;
    }
    XGC_TRACE("xgc_init: add machine child\n");
    object_property_add_child(object_get_root(), "machine", OBJECT(machine));
    current_machine = machine;
    /* qemu_create_machine_containers(): the memory core expects these. */
    object_property_add_new_container(OBJECT(machine), "unattached");
    object_property_add_new_container(OBJECT(machine), "peripheral");
    object_property_add_new_container(OBJECT(machine), "peripheral-anon");

    /* system_memory/system_io + the target page size. */
    XGC_TRACE("xgc_init: cpu_exec_init_all\n");
    cpu_exec_init_all();

    /* The TCG accelerator normally does this from machine_run_board_init()
     * -> accel_init_machine(); it installs page_init/tb_htable_init/tcg_init
     * and the prologue, and publishes the accelerator for cpu realize. */
    machine->smp.max_cpus = 1;
    machine->smp.cpus = 1;
    qemu_icache_linesize = 64;
    qemu_icache_linesize_log = 6;
    qemu_dcache_linesize = 64;
    qemu_dcache_linesize_log = 6;
    XGC_TRACE("xgc_init: accel init\n");
    {
        AccelState *accel = ACCEL(object_new("tcg-accel"));
        if (accel_init_machine(accel, machine) < 0) {
            return XGC_ERR_INIT;
        }
    }

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
    XGC_TRACE("xgc_create: cpu_create\n");
    type = object_class_get_name(oc);
    if (!type) {
        return NULL;
    }

    c = g_new0(XgcCpu, 1);
    {
        Error *err = NULL;
        CPUState *cpu = CPU(object_new(type));
        if (!object_property_set_int(OBJECT(cpu), "apic-id", 0, &err)) {
            error_report_err(err);
            object_unref(OBJECT(cpu));
            g_free(c);
            return NULL;
        }
        if (!qdev_realize(DEVICE(cpu), NULL, &err)) {
            error_report_err(err);
            object_unref(OBJECT(cpu));
            g_free(c);
            return NULL;
        }
        c->cpu = cpu;
    }
    if (!c->cpu) {
        g_free(c);
        return NULL;
    }
    XGC_TRACE("xgc_create: reset real mode\n");
    xgc_vcpu_thread(c->cpu);
    c->cpu->tcg_cflags |= CF_USE_ICOUNT;
    c->cpu->num_ases = 1;
    cpu_address_space_init(c->cpu, 0, "cpu-memory", get_system_memory());
    xgc_set_real_mode(c, 0x1000, 0x8000);
    XGC_TRACE("xgc_create: done\n");
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
    for (i = 0; i < c->nrings; i++) {
        memory_region_destroy(c->ring_mr[i]);
        g_free(c->ring_mr[i]);
        if (c->rings[i]->own_shadow) {
            g_free(c->rings[i]->shadow);
        }
        g_free(c->rings[i]);
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

static int xgc_map_ring_impl(XgcCpu *c, uint32_t gpa, uint32_t size,
                             uint8_t *shadow, bool own_shadow)
{
    MemoryRegion *mr;
    XgcRing *r;

    if (!c || !shadow || size == 0 || c->nrings >= 8) {
        if (own_shadow) {
            g_free(shadow);
        }
        return XGC_ERR_ARG;
    }
    r = g_new0(XgcRing, 1);
    r->cpu = c;
    r->base = gpa;
    r->size = size;
    r->shadow = shadow;
    r->own_shadow = own_shadow;

    mr = g_new0(MemoryRegion, 1);
    memory_region_init_io(mr, NULL, &xgc_ring_ops, r, "xgrid-mmio-ring",
                          size);
    memory_region_add_subregion(get_system_memory(), gpa, mr);
    c->ring_mr[c->nrings] = mr;
    c->rings[c->nrings] = r;
    c->nrings++;
    xgc_flush_tb(c);
    return XGC_OK;
}

int xgc_map_mmio_ring(XgcCpu *c, uint32_t gpa, uint32_t size)
{
    return xgc_map_ring_impl(c, gpa, size, g_malloc0(size), true);
}

int xgc_map_mmio_ring_shadow(XgcCpu *c, uint32_t gpa, uint32_t size,
                             void *shadow)
{
    return xgc_map_ring_impl(c, gpa, size, shadow, false);
}

int xgc_mmio_take(XgcCpu *c, XgcMmioAccess *access)
{
    if (!c || !access || c->ring_head == c->ring_tail) {
        return 0;
    }
    *access = c->ring[c->ring_head];
    c->ring_head = (c->ring_head + 1) % XGC_RING_CAP;
    return 1;
}

void xgc_mmio_set_read(XgcCpu *c, uint32_t gpa, uint32_t size, uint64_t value)
{
    int i;

    if (!c || size == 0 || size > 8) {
        return;
    }
    for (i = 0; i < c->nrings; i++) {
        XgcRing *r = c->rings[i];
        if (gpa >= r->base && gpa + size <= r->base + r->size) {
            memcpy(r->shadow + (gpa - r->base), &value, size);
            return;
        }
    }
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
        int ret;
        int64_t before, after;
        uint64_t n;

        if (chunk > 0x7FFF) {
            chunk = 0x7FFF;
        }

        before = icount_get_raw();
        c->cpu->exit_request = false;
        c->cpu->stop = false;
        c->cpu->halted = false;
        c->cpu->neg.icount_decr.u32 = chunk;
        c->cpu->icount_extra = 0;
        c->cpu->icount_budget = chunk;
        c->cpu->exception_index = -1;

        ret = cpu_exec(c->cpu);
        icount_update(c->cpu);
        after = icount_get_raw();

        {
            static int dbg;
            if (getenv("XGRID_CPU_TRACE") &&
                (dbg < 3 || ret == EXCP_HALTED)) {
                CPUX86State *env = cpu_env(c->cpu);
                XGC_TRACE("chunk before=%lld after=%lld low=%u extra=%d "
                          "budget=%lld ret=%d eip=%x ecx=%x\n",
                          (long long)before, (long long)after,
                          c->cpu->neg.icount_decr.u16.low,
                          (int)c->cpu->icount_extra,
                          (long long)c->cpu->icount_budget, ret,
                          env->eip, env->regs[R_ECX]);
                dbg++;
            }
        }

        n = (after > before) ? (uint64_t)(after - before) : 0;
        if (n > chunk) {
            n = chunk;
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
        tb_flush__exclusive_or_serial();
    }
}

uint64_t xgc_icount(XgcCpu *c)
{
    (void)c;
    return (uint64_t)icount_get_raw();
}

uint64_t xgc_test_callback(XgcTestCallback fn)
{
    return fn(NULL, 0x1234, 4);
}
