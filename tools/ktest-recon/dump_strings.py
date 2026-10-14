"""dump_strings.py - print C strings in a phys range of a RAM dump.

Usage: python dump_strings.py <ram.bin> <start_hex> <end_hex>
"""

import sys

path, start, end = sys.argv[1], int(sys.argv[2], 0), int(sys.argv[3], 0)
data = open(path, 'rb').read()
i = start
while i < end:
    if data[i] == 0:
        i += 1
        continue
    j = i
    while j < end and 32 <= data[j] < 127:
        j += 1
    if j - i >= 3 and (j >= end or data[j] == 0):
        print('%08X  %r' % (i, data[i:j].decode('ascii')))
        i = j + 1
    else:
        i = j + 1
