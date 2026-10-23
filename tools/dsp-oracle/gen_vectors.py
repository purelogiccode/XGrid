"""Generates the DSP56300 differential vectors.

Reads the xemu 0.8.139 reference tables (References2/, read-only), synthesizes
instruction words, writes a case file for the standalone C oracle, runs it, and
combines the case list with the expected state into the committed golden file
``tests/XGrid.Audio.Tests/TestData/dsp-vectors.txt``.

The case is fully described by (program words, seed, steps, interrupts): the
state setup is a deterministic xorshift32 stream from the seed, mirrored by
``Dsp56300DifferentialTests.SetupState``.  Only the registers that differ from
that initial state are stored, which keeps the file small.

Development tool; see README.md.
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
REF = os.path.join(ROOT, 'References2', 'xemu-0.8.139', 'hw', 'xbox', 'mcpx', 'apu', 'dsp')
OUT = os.path.join(ROOT, 'tests', 'XGrid.Audio.Tests', 'TestData', 'dsp-vectors.txt')
ORACLE = os.path.join(HERE, 'build', 'oracle.exe' if os.name == 'nt' else 'oracle')

MASK = 0xFFFFFFFF
REG_SP = 0x3B
REG_SR = 0x39
REG_LA = 0x3E
REG_LC = 0x3F
REG_SSH = 0x3C
REG_SSL = 0x3D
REG_R0 = 0x10
REG_N0 = 0x18
REG_M0 = 0x20


def next_rng(state):
    x = state
    x ^= (x << 13) & MASK
    x ^= x >> 17
    x ^= (x << 5) & MASK
    return x & MASK


def setup_state(seed):
    """Returns (registers, x writes, y writes, mix writes, p fill) for a seed."""
    rng = seed
    regs = []
    for _ in range(64):
        rng = next_rng(rng)
        regs.append(rng)
    for i in range(8):
        rng = next_rng(rng)
        regs[REG_R0 + i] = rng % 1024
    for i in range(8):
        rng = next_rng(rng)
        regs[REG_N0 + i] = rng % 32
    for i in range(8):
        rng = next_rng(rng)
        regs[REG_M0 + i] = (0xFFFF, 0x0F, 0x1F, 0x3F)[rng & 3]
    rng = next_rng(rng)
    regs[REG_SP] = rng % 9
    regs[REG_SSH] = 0
    regs[REG_SSL] = 0
    regs[REG_LA] = 0
    regs[REG_LC] = 0
    regs[REG_SR] &= 0x7FFF

    xw = []
    for _ in range(64):
        rng = next_rng(rng)
        addr = rng & 0xFFF
        rng = next_rng(rng)
        xw.append((addr, rng & 0xFFFFFF))
    yw = []
    for _ in range(64):
        rng = next_rng(rng)
        addr = rng & 0x7FF
        rng = next_rng(rng)
        yw.append((addr, rng & 0xFFFFFF))
    mw = []
    for _ in range(32):
        rng = next_rng(rng)
        addr = rng & 0x3FF
        rng = next_rng(rng)
        mw.append((addr, rng & 0xFFFFFF))
    pf = []
    for _ in range(32):
        rng = next_rng(rng)
        pf.append(rng & 0xFFFFFF)
    return regs, xw, yw, mw, pf


def parse_templates():
    lines = open(os.path.join(REF, 'interp', 'dsp_cpu.c'), encoding='utf-8', errors='replace').read().split('\n')
    start = [i for i, l in enumerate(lines) if 'nonparallel_opcodes[]' in l][0]
    rows = []
    for l in lines[start + 1:]:
        if l.strip() == '};':
            break
        m = re.match(r'\s*\{\s*"([0-9A-Za-z]{24})"\s*,\s*"((?:[^"\\]|\\.)*)"\s*,(.*)\},?\s*(//.*)?$', l)
        if not m:
            continue
        parts = [re.sub(r'/\*.*?\*/', '', x).strip() for x in m.group(3).split(',')]
        rows.append((m.group(1), m.group(2), parts[1]))
    return rows


def parse_alu():
    s = open(os.path.join(REF, 'interp', 'dsp_emu.c.inc'), encoding='utf-8', errors='replace').read()
    a = s.index('static const emu_func_t opcodes_alu[256] = {')
    b = s.index('};', a)
    names = re.findall(r'emu_[a-z0-9_]+', re.sub(r'/\*.*?\*/', '', s[a:b], flags=re.S))
    return names[1:]


def synth(template, rng):
    word = 0
    x = rng
    for ch in template:
        if ch == '1':
            word = (word << 1) | 1
        elif ch == '0':
            word <<= 1
        else:
            x = next_rng(x)
            word = (word << 1) | (x & 1)
    return word & 0xFFFFFF, x


def main():
    if not os.path.exists(ORACLE):
        sys.exit('oracle not built; run build.ps1 first')

    templates = parse_templates()
    alu = parse_alu()
    if len(templates) != 187 or len(alu) != 256:
        sys.exit('reference tables changed: %d templates, %d ALU entries' % (len(templates), len(alu)))

    cases = []  # (words, seed, steps, interrupt mask)

    # 1. One case per decode-table entry that has a handler.
    for index, (template, name, emu) in enumerate(templates):
        if emu == 'NULL':
            continue
        for variant in range(3):
            seed = 0x1000 + (index * 16) + variant
            rng = next_rng(seed ^ 0x5A5A5A5A)
            word, rng = synth(template, rng)
            cases.append(([word], seed, 1 + (seed % 3), 0))

    # 2. Parallel-move cases over every defined ALU opcode.
    for index, emu in enumerate(alu):
        if emu == 'emu_undefined':
            continue
        for variant in range(2):
            seed = 0x200000 + (index * 16) + variant
            rng = next_rng(seed ^ 0xA5A5A5A5)
            pm = rng & 0xF
            word = 0x100000 | (pm << 20) | index
            cases.append(([word], seed, 1 + (seed % 2), 0))

    # 3. Random multi-instruction programs built from valid words.
    pool = []
    for index, (template, name, emu) in enumerate(templates):
        if emu == 'NULL':
            continue
        for variant in range(2):
            word, _ = synth(template, next_rng(0x300000 + (index * 8) + variant))
            pool.append(word)

    for index in range(240):
        seed = 0x400000 + (index * 32)
        rng = next_rng(seed ^ 0x1234567)
        words = []
        for _ in range(8):
            rng = next_rng(rng)
            words.append(pool[rng % len(pool)])
        cases.append((words, seed, 1 + (rng % 24), 0))

    # 4. Interrupt pipeline: a pending interrupt on each vector.
    vectors = (0x00, 0x04, 0x02, 0x08)
    for index in range(4):
        for variant in range(6):
            seed = 0x500000 + (index * 16) + variant
            words = [0x000000, 0x000000]
            address = vectors[index]
            while len(words) <= address:
                words.append(0x000000)
            words[address] = 0x00000C  # rts
            cases.append((words, seed, 12 + variant, 1 << index))

    lines = []
    for words, seed, steps, ints in cases:
        regs, xw, yw, mw, pf = setup_state(seed)
        lines.append('CASE')
        for i, value in enumerate(regs):
            lines.append('R %x %x' % (i, value))
        for addr, value in xw:
            lines.append('X %x %x' % (addr, value))
        for addr, value in yw:
            lines.append('Y %x %x' % (addr, value))
        for addr, value in mw:
            lines.append('M %x %x' % (addr, value))
        for i, value in enumerate(pf):
            lines.append('P %x %x' % (i, value))
        for i, word in enumerate(words):
            lines.append('P %x %x' % (i, word))
        for bit in range(4):
            if ints & (1 << bit):
                lines.append('I %x' % bit)
        lines.append('S %d' % steps)
        lines.append('END')

    case_path = os.path.join(HERE, 'build', 'cases.txt')
    os.makedirs(os.path.dirname(case_path), exist_ok=True)
    open(case_path, 'w').write('\n'.join(lines) + '\n')

    out = subprocess.run([ORACLE, case_path], capture_output=True, text=True, check=True)
    result = [l for l in out.stdout.split('\n') if l and not l.startswith('Dsp: ')]
    if len(result) != len(cases) * 2:
        sys.exit('oracle output has %d lines for %d cases' % (len(result), len(cases)))

    golden = []
    dropped = 0
    for i, (words, seed, steps, ints) in enumerate(cases):
        state = result[i * 2]
        regs = result[i * 2 + 1]
        if state == 'OOB':
            dropped += 1
            continue
        final = [int(x, 16) for x in regs[len('regs='):].split(',')]
        initial = setup_state(seed)[0]
        delta = ','.join('%02x:%06x' % (j, final[j]) for j in range(64) if final[j] != initial[j])
        golden.append('%s|%x|%d|%s|%s|%x' % (
            ','.join('%06x' % w for w in words), seed, steps, state, delta, ints))

    header = [
        '# DSP56300 differential vectors.',
        '# Generated from the xemu 0.8.139 reference interpreter by',
        '# tools/dsp-oracle/gen_vectors.py.  Each line is:',
        '#   program words (P:0.., hex, comma separated) | seed | steps |',
        '#   pc=.. sr=.. ic=.. len=.. cyc=.. h=.. | registers changed vs the',
        '#   seed state (idx:value) | interrupts (bit i = vector i pending)',
        '# The test replays the deterministic state setup from the seed and',
        '# compares the whole result.  Do not edit by hand.',
    ]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, 'w').write('\n'.join(header + golden) + '\n')
    print('cases', len(cases), 'kept', len(golden), 'dropped', dropped, '->', OUT)


if __name__ == '__main__':
    main()
