/* Standalone oracle for the XGrid DSP56300 port (development tool).
 *
 * Compiles the xemu 0.8.139 reference interpreter from References2/ (read-only)
 * with minimal QEMU shims, replays a case file and prints the resulting state.
 * Nothing here is part of the XGrid build or distribution; see README.md.
 *
 * Case file format (one case per CASE..END block):
 *   CASE
 *   R <reg> <value>     set a register (hex)
 *   P <addr> <value>    set a program-memory word
 *   X <addr> <value>    set an X-memory word
 *   Y <addr> <value>    set a Y-memory word
 *   M <addr> <value>    set a mix-buffer word
 *   I <n>               post interrupt n
 *   S <n>               execute n instructions
 *   END                 print the state (two lines: summary + registers)
 *
 * DSP_TRACE=1 in the environment prints a per-step trace to stderr.
 */
#include "dsp_cpu.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Set by the out-of-range guards in the prepared interpreter copy. */
extern bool dsp_oracle_oob;

static dsp_core_t core;

static uint32_t peripheral_read(dsp_core_t *dsp, uint32_t address)
{
    (void)dsp;
    return (address * 2654435761u) & 0xFFFFFF;
}

static void peripheral_write(dsp_core_t *dsp, uint32_t address, uint32_t value)
{
    (void)dsp;
    (void)address;
    (void)value;
}

static uint32_t fnv1a(const void *data, size_t len, uint32_t h)
{
    const uint8_t *p = data;
    for (size_t i = 0; i < len; i++) {
        h ^= p[i];
        h *= 16777619u;
    }
    return h;
}

static void reset_core(void)
{
    memset(&core, 0, sizeof(core));
    core.exception_debugging = false;
    dsp56k_reset_cpu(&core);
    core.exception_debugging = false;
    core.read_peripheral = peripheral_read;
    core.write_peripheral = peripheral_write;
    dsp_oracle_oob = false;
}

static void print_state(void)
{
    if (dsp_oracle_oob) {
        printf("OOB\n");
        printf("regs=\n");
        return;
    }

    uint32_t h = 2166136261u;
    h = fnv1a(core.xram, sizeof(core.xram), h);
    h = fnv1a(core.yram, sizeof(core.yram), h);
    h = fnv1a(core.pram, sizeof(core.pram), h);
    h = fnv1a(core.mixbuffer, sizeof(core.mixbuffer), h);
    h = fnv1a(core.stack, sizeof(core.stack), h);

    printf("pc=%08x sr=%04x ic=%u len=%u cyc=%u h=%08x\n",
           core.pc, core.registers[DSP_REG_SR], core.instr_cycle,
           core.cur_inst_len, core.cycle_count, h);
    printf("regs=");
    for (int i = 0; i < DSP_REG_MAX; i++) {
        printf("%06x%c", core.registers[i], i == DSP_REG_MAX - 1 ? '\n' : ',');
    }
}

int main(int argc, char **argv)
{
    FILE *f = stdin;
    if (argc > 1) {
        f = fopen(argv[1], "r");
        if (!f) {
            perror(argv[1]);
            return 1;
        }
    }

    char line[4096];
    int steps = 0;
    reset_core();

    while (fgets(line, sizeof(line), f)) {
        unsigned a, b;
        if (strncmp(line, "CASE", 4) == 0) {
            reset_core();
            steps = 0;
        } else if (strncmp(line, "R ", 2) == 0) {
            if (sscanf(line, "R %x %x", &a, &b) == 2) {
                core.registers[a] = b;
            }
        } else if (strncmp(line, "P ", 2) == 0) {
            if (sscanf(line, "P %x %x", &a, &b) == 2 && a < DSP_PRAM_SIZE) {
                core.pram[a] = b;
            }
        } else if (strncmp(line, "X ", 2) == 0) {
            if (sscanf(line, "X %x %x", &a, &b) == 2 && a < DSP_XRAM_SIZE) {
                core.xram[a] = b;
            }
        } else if (strncmp(line, "Y ", 2) == 0) {
            if (sscanf(line, "Y %x %x", &a, &b) == 2 && a < DSP_YRAM_SIZE) {
                core.yram[a] = b;
            }
        } else if (strncmp(line, "M ", 2) == 0) {
            if (sscanf(line, "M %x %x", &a, &b) == 2 && a < DSP_MIXBUFFER_SIZE) {
                core.mixbuffer[a] = b;
            }
        } else if (strncmp(line, "I ", 2) == 0) {
            if (sscanf(line, "I %x", &a) == 1) {
                dsp56k_add_interrupt(&core, a);
            }
        } else if (strncmp(line, "S ", 2) == 0) {
            if (sscanf(line, "S %d", &a) == 1) {
                steps = (int)a;
            }
        } else if (strncmp(line, "END", 3) == 0) {
            for (int i = 0; i < steps; i++) {
                dsp56k_execute_instruction(&core);
                core.cycle_count += core.instr_cycle;
                if (getenv("DSP_TRACE")) {
                    fprintf(stderr,
                            "STEP %d pc=%04x ic=%u len=%u sp=%x sr=%04x pipe=%u "
                            "istate=%u fetch=%04x save=%04x\n",
                            i, core.pc, core.instr_cycle, core.cur_inst_len,
                            core.registers[DSP_REG_SP] & 0xF,
                            core.registers[DSP_REG_SR],
                            core.interrupt_pipeline_count, core.interrupt_state,
                            core.interrupt_instr_fetch, core.interrupt_save_pc);
                }
            }
            print_state();
        }
    }

    return 0;
}
