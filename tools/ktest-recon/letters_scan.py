"""letters_scan.py - find DOS device letter evidence in a 64 MiB RAM dump.

Scans for ASCII '\??\<letter>:' strings and '\Device\...' strings, dumps
context bytes around each hit, and (optionally) decodes a pointer array at a
given physical address. Used to answer: which drive letters exist and what
does each resolve to (120).
"""

import re
import struct
import sys

path = sys.argv[1]
base = int(sys.argv[2], 0) if len(sys.argv) > 2 else None
data = open(path, 'rb').read()

letter_re = re.compile(rb'\\\?\?\\[A-Za-z]:')
dev_re = re.compile(rb'\\Device\\[A-Za-z0-9_\\]*')

print('=== \\??\\X: occurrences ===')
seen = set()
for m in letter_re.finditer(data):
    a = m.start()
    ctx = data[a:a + 48]
    end = ctx.find(b'\x00')
    s = ctx[:end] if end >= 0 else ctx
    key = (a, s)
    if key in seen:
        continue
    seen.add(key)
    print('  %08X  %r' % (a, s))

print('=== \\Device\\... strings (unique, first 3 of each name) ===')
counts = {}
for m in dev_re.finditer(data):
    a = m.start()
    ctx = data[a:a + 64]
    end = ctx.find(b'\x00')
    s = ctx[:end] if end >= 0 else ctx
    if not re.fullmatch(rb'\\Device\\[A-Za-z0-9_\\]*', s):
        continue
    counts.setdefault(s, []).append(a)
for s, addrs in sorted(counts.items()):
    print('  %r  x%d  @ %s' % (s, len(addrs), ' '.join('%08X' % x for x in addrs[:4])))

if base is not None:
    print('=== pointer array at phys %08X ===' % base)
    for i in range(26):
        off = base + i * 4
        if off + 4 > len(data):
            break
        (ptr,) = struct.unpack_from('<I', data, off)
        note = ''
        if 0 < ptr < len(data):
            raw = data[ptr:ptr + 40]
            n = raw.find(b'\x00')
            note = repr(raw[:n] if n >= 0 else raw)
        print('  [%c] %08X -> %08X  %s' % (ord('A') + i, off, ptr, note))
