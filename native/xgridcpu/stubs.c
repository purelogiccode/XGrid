/*
 * XGrid native CPU core - XCN0 link stubs.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The x86 CPU model and the monitor reference a handful of PC-machine
 * facilities (8259 PIC, IOAPIC, KVM IRQ accounting, SGX, MSI and the QMP
 * version marshal) that a CPU-only library does not build. The spike never
 * calls them: the C# machine owns the PIC and the QMP surface. The product
 * integration (XCN1) replaces these with the C# PIC lines and a real monitor
 * policy; this file exists only to keep the XCN0 link honest.
 */
#include <stdbool.h>
#include <stddef.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>

/* hw/intc/i8259.c */
int pic_read_irq(void *s);
int pic_get_output(void *s);
void *isa_pic;
extern int xgc_irq_vector;

/* accel/kvm + hw/i386/vapic.c */
void kvm_report_irq_delivered(int delivered);
void kvm_reset_irq_delivered(void);
void vapic_report_tpr_access(void *env, void *cs, int access);

/* hw/intc/ioapic.c */
void ioapic_eoi_broadcast(int vector);

/* target/i386/cpu.c */
void check_sgx_support(void);

/* hw/pci/msi.c */
bool msi_nonbroken;

/* qapi/qapi-commands-control.c */
void qmp_marshal_query_version(void *args, void **ret, void *errp);

/* Optional PC-machine surfaces pulled in by libsystem.a. */
void fw_cfg_add_file_callback(void *s, ...);
void fw_cfg_modify_file(void *s, ...);
unsigned long long get_plugged_memory_size(void);
void *qmp_memory_device_list(void);
void qmp_watchdog_set_action(void *action, void *errp);
void *get_vhost_net(void *nc);

/* Monitor / AIO glue referenced by util/ but not built in the CPU-only lib. */
int monitor_vprintf(void *mon, const char *fmt, va_list ap);
void *monitor_cur(void);
int monitor_get_fd(void *mon, const char *fdname, void *errp);
void register_aiocontext(void *ctx);
void unregister_aiocontext(void *ctx);
void qapi_event_emit(int event, void *qdict);

/* More monitor/block glue pulled by util/qapi. */
void progress_increase_remaining(void *p, unsigned long long delta);
void monitor_set_cur(void *mon);
bool qmp_command_available(void *cmd, void *errp);
int monitor_fdset_dup_fd_remove(int dupfd);
int error_vprintf(const char *fmt, va_list ap);

/* CPU vmstate descriptors (opaque here; only the address is referenced) and
 * HMP register dumps. */
unsigned char vmstate_info_uint8[64];
unsigned char vmstate_info_uint16[64];
unsigned char vmstate_info_uint32[64];
unsigned char vmstate_info_uint64[64];
unsigned char vmstate_info_int8[64];
unsigned char vmstate_info_int16[64];
unsigned char vmstate_info_int32[64];
unsigned char vmstate_info_int64[64];
unsigned char vmstate_info_bool[64];
unsigned char vmstate_info_tmp[64];
unsigned char vmstate_info_pointer[64];
unsigned char vmstate_info_string[64];
unsigned char vmstate_info_buffer[64];
unsigned char vmstate_info_float32[64];
unsigned char vmstate_info_float64[64];
void *mon_get_cpu(void *mon);
void monitor_printf(void *mon, const char *fmt, ...);

/* Migration/replay glue referenced by qdev and the timer core. */
int vmstate_register_with_alias_id(void *obj, int instance_id, const void *vmsd,
                                   int alias_id, int required_for_version,
                                   void *errp);
void vmstate_unregister(void *obj, const void *vmsd, void *opaque);
bool vmstate_check_only_migratable(const void *vmsd);
long long replay_read_clock(int kind, long long rt_clock);
void replay_checkpoint(int checkpoint);
void replay_save_clock(int kind, long long clock, long long raw_icount);
void replay_save_random(long long value);
long long replay_read_random(void);
void replay_mutex_lock(void);
void replay_mutex_unlock(void);

/* Firmware-config and the optional disassembler logger. */
void fw_cfg_add_file(void *s, ...);
void *syminfos;
void *lookup_symbol(unsigned long long addr);
void target_disas(void *f, unsigned long long pc, unsigned long long size);
bool replay_running_debug(void);
void replay_breakpoint(void);
int vmstate_register_ram_global(void *mr);
bool cpr_is_incoming(void);
void disas(void *out, void *code, unsigned long long pc);
bool replay_has_event(void);
bool replay_async_events(void);
bool replay_has_interrupt(void);
void tcg_get_stats(void *accel, void *buf);
bool replay_interrupt(void);
bool replay_exception(void);
bool replay_has_exception(void);
void qmp_x_accel_stats(void *args, void **ret, void *errp);
void monitor_register_hmp_info_hrt(void *mon, const char *name, void *fn);
void bdrv_close_all(void);
void net_cleanup(void);
void audio_cleanup(void);
void monitor_cleanup(void);
void qemu_chr_cleanup(void);
void migration_shutdown(void);
void blk_exp_close_all(void);
void replay_finish(void);
void bdrv_drain_all_begin(void);
void job_cancel_sync_all(void);
void precopy_infrastructure_init(void);
void postcopy_infrastructure_init(void);
void monitor_init_globals(void);
void bdrv_init_with_whitelist(void);
void replay_shutdown_request(int kind);
void migrate_del_blocker(void *blocker);
int total_dirty_pages;
void *qtest_driver;
void xen_invalidate_map_cache_entry(void *p);
void xen_hvm_modified_memory(unsigned long long start, unsigned long long nr);
int migrate_add_blocker_modes(void *blocker, void *errp, int modes);
unsigned long xen_ram_alloc(unsigned long block_id, unsigned long size, void *mr);
void cpr_delete_fd(int fd);
unsigned long xen_ram_addr_from_mapcache(void *p);
void *xen_map_cache(void *block, unsigned long offset, unsigned long size,
                    unsigned int flags, int *paddr);
void *ram_block_attributes_create(const char *name);
void ram_block_attributes_destroy(void *attr);
unsigned long host_memory_backend_pagesize(void *backend);
bool host_memory_backend_is_mapped(void *backend);
bool xen_mr_is_memory(void *mr);
void vmstate_register_ram(void *mr, void *dev);
unsigned char vmstate_info_unused_buffer[64];
unsigned char vmstate_info_timer[64];
int monitor_get_cpu_index(void *mon);
void bdrv_drain_all(void);
void bdrv_flush_all(void);
void host_memory_backend_set_mapped(void *backend, bool mapped);
void replay_start(void);
void register_global_state(void);
bool qtest_allowed;
int replay_mode;
void *audio_be_by_name(const char *name);
void *host_memory_backend_get_memory(void *backend);
int qemu_chr_be_write(void *chr, const void *buf, int len);
int qemu_chr_fe_write_all(void *chr, const void *buf, int len);
void replay_gdb_attached(void);
void qemu_chr_fe_set_handlers(void *chr, void *fd_cb, void *read_cb,
                              void *event_cb, void *opaque, void *context,
                              bool set_open);
void *qemu_chr_new_noreplay(const char *label, const char *filename,
                           bool permute);
void qemu_chr_fe_deinit(void *chr, bool remove);
void *semihosting_get_target(void);
void *qemu_chardev_new(const char *id, const char *typename, void *backend,
                       void *context, void *errp);
void *monitor_init_hmp(void *chr, bool qmp, void *errp);
int qemu_chr_fe_init(void *chr, void *be, void *errp);
void replay_reverse_step(void);
void replay_reverse_continue(void);
void qemu_init_main_loop_lock(void);
void qemu_mutex_lock_main_loop(void);
void qemu_mutex_unlock_main_loop(void);

int pic_read_irq(void *s)
{
    (void)s;
    {
        extern int xgc_irq_ack_vector(void);
        int acked = xgc_irq_ack_vector();
        if (acked >= 0) {
            if (getenv("XGRID_CPU_TRACE")) {
                fprintf(stderr, "pic_read_irq ack=%d\n", acked);
                fflush(stderr);
            }
            return acked;
        }
        /* The device model has no deliverable vector: answer with the spurious
         * IRQ7 the 8259 gives a withdrawn INT. Returning -1 made QEMU call
         * do_interrupt_x86_hardirq(env, -1) - a negative IDT index that
         * corrupts the guest. */
        if (getenv("XGRID_CPU_TRACE")) {
            fprintf(stderr, "pic_read_irq spurious\n");
            fflush(stderr);
        }
        return 7;
    }
    if (getenv("XGRID_CPU_TRACE")) {
        fprintf(stderr, "pic_read_irq queued=%d\n", xgc_irq_vector);
        fflush(stderr);
    }
    if (xgc_irq_vector >= 0) {
        int vector = xgc_irq_vector;
        xgc_irq_vector = -1;
        return vector;
    }
    return 7;
}

int pic_get_output(void *s)
{
    (void)s;
    return 0;
}

void kvm_report_irq_delivered(int delivered)
{
    (void)delivered;
}

void kvm_reset_irq_delivered(void)
{
}

void vapic_report_tpr_access(void *env, void *cs, int access)
{
    (void)env;
    (void)cs;
    (void)access;
}

void ioapic_eoi_broadcast(int vector)
{
    (void)vector;
}

void check_sgx_support(void)
{
}

void qmp_marshal_query_version(void *args, void **ret, void *errp)
{
    (void)args;
    (void)errp;
    if (ret) {
        *ret = NULL;
    }
}

void fw_cfg_add_file_callback(void *s, ...)
{
    (void)s;
}

void fw_cfg_modify_file(void *s, ...)
{
    (void)s;
}

unsigned long long get_plugged_memory_size(void)
{
    return 0;
}

void *qmp_memory_device_list(void)
{
    return NULL;
}

void qmp_watchdog_set_action(void *action, void *errp)
{
    (void)action;
    (void)errp;
}

void *get_vhost_net(void *nc)
{
    (void)nc;
    return NULL;
}

int monitor_vprintf(void *mon, const char *fmt, va_list ap)
{
    (void)mon;
    (void)fmt;
    (void)ap;
    return 0;
}

void *monitor_cur(void)
{
    return NULL;
}

int monitor_get_fd(void *mon, const char *fdname, void *errp)
{
    (void)mon;
    (void)fdname;
    (void)errp;
    return -1;
}

void register_aiocontext(void *ctx)
{
    (void)ctx;
}

void unregister_aiocontext(void *ctx)
{
    (void)ctx;
}

void qapi_event_emit(int event, void *qdict)
{
    (void)event;
    (void)qdict;
}

void progress_increase_remaining(void *p, unsigned long long delta)
{
    (void)p;
    (void)delta;
}

void monitor_set_cur(void *mon)
{
    (void)mon;
}

bool qmp_command_available(void *cmd, void *errp)
{
    (void)cmd;
    (void)errp;
    return false;
}

int monitor_fdset_dup_fd_remove(int dupfd)
{
    (void)dupfd;
    return 0;
}

int error_vprintf(const char *fmt, va_list ap)
{
    return vfprintf(stderr, fmt, ap);
}

void *mon_get_cpu(void *mon)
{
    (void)mon;
    return NULL;
}

void monitor_printf(void *mon, const char *fmt, ...)
{
    (void)mon;
    (void)fmt;
}

int vmstate_register_with_alias_id(void *obj, int instance_id, const void *vmsd,
                                   int alias_id, int required_for_version,
                                   void *errp)
{
    (void)obj;
    (void)instance_id;
    (void)vmsd;
    (void)alias_id;
    (void)required_for_version;
    (void)errp;
    return 0;
}

void vmstate_unregister(void *obj, const void *vmsd, void *opaque)
{
    (void)obj;
    (void)vmsd;
    (void)opaque;
}

bool vmstate_check_only_migratable(const void *vmsd)
{
    (void)vmsd;
    return true;
}

long long replay_read_clock(int kind, long long rt_clock)
{
    (void)kind;
    return rt_clock;
}

void replay_checkpoint(int checkpoint)
{
    (void)checkpoint;
}

void replay_save_clock(int kind, long long clock, long long raw_icount)
{
    (void)kind;
    (void)clock;
    (void)raw_icount;
}

void replay_save_random(long long value)
{
    (void)value;
}

long long replay_read_random(void)
{
    return 0;
}

void replay_mutex_lock(void)
{
}

void replay_mutex_unlock(void)
{
}

void fw_cfg_add_file(void *s, ...)
{
    (void)s;
}

void *lookup_symbol(unsigned long long addr)
{
    (void)addr;
    return NULL;
}

void target_disas(void *f, unsigned long long pc, unsigned long long size)
{
    (void)f;
    (void)pc;
    (void)size;
}

bool replay_running_debug(void)
{
    return false;
}

void replay_breakpoint(void)
{
}

int vmstate_register_ram_global(void *mr)
{
    (void)mr;
    return 0;
}

bool cpr_is_incoming(void)
{
    return false;
}

void disas(void *out, void *code, unsigned long long pc)
{
    (void)out;
    (void)code;
    (void)pc;
}

bool replay_has_event(void)
{
    return false;
}

bool replay_async_events(void)
{
    return false;
}

bool replay_has_interrupt(void)
{
    return false;
}

void tcg_get_stats(void *accel, void *buf)
{
    (void)accel;
    (void)buf;
}

bool replay_interrupt(void)
{
    /* No replay: QEMU delivers the pending interrupt/exception. */
    return replay_mode == 0;
}

bool replay_exception(void)
{
    /* No replay: this is what lets cpu_handle_exception() call do_interrupt()
     * (returning false made every exception an exit with zero instructions
     * executed, so #PF/#NM handlers never ran). */
    return replay_mode == 0;
}

bool replay_has_exception(void)
{
    return false;
}

void qmp_x_accel_stats(void *args, void **ret, void *errp)
{
    (void)args;
    (void)errp;
    if (ret) {
        *ret = NULL;
    }
}

void monitor_register_hmp_info_hrt(void *mon, const char *name, void *fn)
{
    (void)mon;
    (void)name;
    (void)fn;
}

void bdrv_close_all(void)
{
}

void net_cleanup(void)
{
}

void audio_cleanup(void)
{
}

void monitor_cleanup(void)
{
}

void qemu_chr_cleanup(void)
{
}

void migration_shutdown(void)
{
}

void blk_exp_close_all(void)
{
}

void replay_finish(void)
{
}

void bdrv_drain_all_begin(void)
{
}

void job_cancel_sync_all(void)
{
}

void precopy_infrastructure_init(void)
{
}

void postcopy_infrastructure_init(void)
{
}

void monitor_init_globals(void)
{
}

void bdrv_init_with_whitelist(void)
{
}

void replay_shutdown_request(int kind)
{
    (void)kind;
}

void migrate_del_blocker(void *blocker)
{
    (void)blocker;
}

void xen_invalidate_map_cache_entry(void *p)
{
    (void)p;
}

void xen_hvm_modified_memory(unsigned long long start, unsigned long long nr)
{
    (void)start;
    (void)nr;
}

int migrate_add_blocker_modes(void *blocker, void *errp, int modes)
{
    (void)blocker;
    (void)errp;
    (void)modes;
    return 0;
}

unsigned long xen_ram_alloc(unsigned long block_id, unsigned long size, void *mr)
{
    (void)block_id;
    (void)size;
    (void)mr;
    return 0;
}

void cpr_delete_fd(int fd)
{
    (void)fd;
}

unsigned long xen_ram_addr_from_mapcache(void *p)
{
    (void)p;
    return 0;
}

void *xen_map_cache(void *block, unsigned long offset, unsigned long size,
                    unsigned int flags, int *paddr)
{
    (void)block;
    (void)offset;
    (void)size;
    (void)flags;
    (void)paddr;
    return NULL;
}

void *ram_block_attributes_create(const char *name)
{
    (void)name;
    return NULL;
}

void ram_block_attributes_destroy(void *attr)
{
    (void)attr;
}

unsigned long host_memory_backend_pagesize(void *backend)
{
    (void)backend;
    return 4096;
}

bool host_memory_backend_is_mapped(void *backend)
{
    (void)backend;
    return false;
}

bool xen_mr_is_memory(void *mr)
{
    (void)mr;
    return false;
}

void vmstate_register_ram(void *mr, void *dev)
{
    (void)mr;
    (void)dev;
}

int monitor_get_cpu_index(void *mon)
{
    (void)mon;
    return 0;
}

void bdrv_drain_all(void)
{
}

void bdrv_flush_all(void)
{
}

void host_memory_backend_set_mapped(void *backend, bool mapped)
{
    (void)backend;
    (void)mapped;
}

void replay_start(void)
{
}

void register_global_state(void)
{
}

void *audio_be_by_name(const char *name)
{
    (void)name;
    return NULL;
}

void *host_memory_backend_get_memory(void *backend)
{
    (void)backend;
    return NULL;
}

int qemu_chr_be_write(void *chr, const void *buf, int len)
{
    (void)chr;
    (void)buf;
    (void)len;
    return 0;
}

int qemu_chr_fe_write_all(void *chr, const void *buf, int len)
{
    (void)chr;
    (void)buf;
    (void)len;
    return 0;
}

void replay_gdb_attached(void)
{
}

void qemu_chr_fe_set_handlers(void *chr, void *fd_cb, void *read_cb,
                              void *event_cb, void *opaque, void *context,
                              bool set_open)
{
    (void)chr;
    (void)fd_cb;
    (void)read_cb;
    (void)event_cb;
    (void)opaque;
    (void)context;
    (void)set_open;
}

void *qemu_chr_new_noreplay(const char *label, const char *filename,
                           bool permute)
{
    (void)label;
    (void)filename;
    (void)permute;
    return NULL;
}

void qemu_chr_fe_deinit(void *chr, bool remove)
{
    (void)chr;
    (void)remove;
}

void *semihosting_get_target(void)
{
    return NULL;
}

void *qemu_chardev_new(const char *id, const char *typename, void *backend,
                       void *context, void *errp)
{
    (void)id;
    (void)typename;
    (void)backend;
    (void)context;
    (void)errp;
    return NULL;
}

void *monitor_init_hmp(void *chr, bool qmp, void *errp)
{
    (void)chr;
    (void)qmp;
    (void)errp;
    return NULL;
}

int qemu_chr_fe_init(void *chr, void *be, void *errp)
{
    (void)chr;
    (void)be;
    (void)errp;
    return 0;
}

void replay_reverse_step(void)
{
}

void replay_reverse_continue(void)
{
}

void qemu_init_main_loop_lock(void)
{
}

void qemu_mutex_lock_main_loop(void)
{
}

void qemu_mutex_unlock_main_loop(void)
{
}
