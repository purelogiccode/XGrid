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

/* hw/intc/i8259.c */
int pic_read_irq(void *s);
int pic_get_output(void *s);
void *isa_pic;

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

int pic_read_irq(void *s)
{
    (void)s;
    return -1;
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
