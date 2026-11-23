/*
 * XGrid host input source.
 *
 * SPDX-License-Identifier: GPL-2.0
 *
 * When XGRID_INPUT_SHM is set, port 1's controller state comes from a named
 * shared section that the shell writes, instead of SDL. The shell owns the
 * window (and with the display sink there is no SDL window at all), so this is
 * the only path that works in both presentation modes.
 *
 * Layout (little endian, 32 bytes):
 *   uint32 magic   'XGDI'
 *   uint32 buttons xemu's XID mask (A=0, B=1, X=2, Y=3, dleft=4, dup=5,
 *                  dright=6, ddown=7, back=8, start=9, white=10, black=11,
 *                  lstick=12, rstick=13, guide=14)
 *   int32  lx, ly, rx, ry   sticks, -32768..32767
 *   int32  lt, rt           triggers, 0..32767
 *   uint32 sequence         increments once per shell update
 */

#ifndef XGRID_INPUT_H
#define XGRID_INPUT_H

#include <stdbool.h>
#include <stdint.h>

/** Returns true when a shared-section input source was requested. */
bool xgrid_input_enabled(void);

/**
 * Reads the latest pad state. The pointer arguments may be NULL.
 * Returns false while the shell has not published a state yet.
 */
bool xgrid_input_read(uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt);

#endif /* XGRID_INPUT_H */
