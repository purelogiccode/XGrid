"""Prepares a guarded copy of the xemu 0.8.139 DSP interpreter.

The reference interpreter asserts on out-of-range accesses (and, in a release
build, reads out of bounds).  The oracle must survive a random case long enough
to report it, so this script copies ``dsp_cpu.c`` into the build directory and
adds guards that set a flag instead of crashing.  Everything else is unchanged.

Development tool; see README.md.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
REF = os.path.join(ROOT, 'References2', 'xemu-0.8.139', 'hw', 'xbox', 'mcpx', 'apu', 'dsp', 'interp')
OUT = os.path.join(HERE, 'build')

REPLACEMENTS = [
    (
        '#define BITMASK(x)  ((1<<(x))-1)',
        'bool dsp_oracle_oob;\n\n#define BITMASK(x)  ((1<<(x))-1)',
    ),
    (
        '''static uint32_t read_memory_p(dsp_core_t* dsp, uint32_t address)
{
    assert((address & 0xFF000000) == 0);
    assert(address < DSP_PRAM_SIZE);''',
        '''static uint32_t read_memory_p(dsp_core_t* dsp, uint32_t address)
{
    if (address >= DSP_PRAM_SIZE) {
        dsp_oracle_oob = true;
        return 0;
    }''',
    ),
    (
        '''    } else if (space == DSP_SPACE_Y) {
        assert(address < DSP_YRAM_SIZE);
        return dsp->yram[address];''',
        '''    } else if (space == DSP_SPACE_Y) {
        if (address >= DSP_YRAM_SIZE) {
            dsp_oracle_oob = true;
            return 0;
        }
        return dsp->yram[address];''',
    ),
    (
        '''            if (address < DSP_XRAM_SIZE) {
                return dsp->xram[address];
            } else {
                fprintf(stderr, "Out of bounds read at %x!\\n", address);
                return 0x00FFFFFF; // FIXME: What does the DSP actually do in this case?
            }''',
        '''            if (address < DSP_XRAM_SIZE) {
                return dsp->xram[address];
            } else {
                dsp_oracle_oob = true;
                return 0x00FFFFFF;
            }''',
    ),
    (
        '''        } else {
            assert(address < DSP_XRAM_SIZE);
            dsp->xram[address] = value;
        }
    } else if (space == DSP_SPACE_Y) {
        assert(address < DSP_YRAM_SIZE);
        dsp->yram[address] = value;
    } else if (space == DSP_SPACE_P) {
        assert(address < DSP_PRAM_SIZE);
        stl_le_p(&dsp->pram[address], value);
        dsp->pram_opcache[address] = NULL;''',
        '''        } else {
            if (address >= DSP_XRAM_SIZE) {
                dsp_oracle_oob = true;
                return;
            }
            dsp->xram[address] = value;
        }
    } else if (space == DSP_SPACE_Y) {
        if (address >= DSP_YRAM_SIZE) {
            dsp_oracle_oob = true;
            return;
        }
        dsp->yram[address] = value;
    } else if (space == DSP_SPACE_P) {
        if (address >= DSP_PRAM_SIZE) {
            dsp_oracle_oob = true;
            return;
        }
        stl_le_p(&dsp->pram[address], value);
        dsp->pram_opcache[address] = NULL;''',
    ),
    (
        '''    fprintf(stderr, "op = %08x\\n", op);
    assert(!"Invalid op code in dsp_cpu");
    return NULL;''',
        '''    /* Oracle guard: an invalid opcode runs the reference's undefined handler
     * instead of dereferencing NULL (the assert is compiled out in release). */
    static const OpcodeEntry undefined_entry = {
        NULL, "undefined", NULL, emu_undefined, NULL
    };
    return &undefined_entry;''',
    ),
    (
        '''        const OpcodeEntry *op = dsp->pram_opcache[dsp->pc];
        if (op == NULL) {
            op = lookup_opcode(dsp->cur_inst);
            dsp->pram_opcache[dsp->pc] = op;
        }''',
        '''        const OpcodeEntry *op = dsp->pc < DSP_PRAM_SIZE ? dsp->pram_opcache[dsp->pc] : NULL;
        if (op == NULL) {
            op = lookup_opcode(dsp->cur_inst);
            if (dsp->pc < DSP_PRAM_SIZE) {
                dsp->pram_opcache[dsp->pc] = op;
            }
        }''',
    ),
]


def main():
    src = open(os.path.join(REF, 'dsp_cpu.c'), encoding='utf-8', errors='replace').read()
    for old, new in REPLACEMENTS:
        if old not in src:
            sys.exit('reference text not found (xemu version changed?):\n' + old[:80])
        src = src.replace(old, new, 1)

    os.makedirs(OUT, exist_ok=True)
    open(os.path.join(OUT, 'dsp_cpu_oracle.c'), 'w', encoding='utf-8').write(src)
    print('prepared', os.path.join(OUT, 'dsp_cpu_oracle.c'))


if __name__ == '__main__':
    main()
