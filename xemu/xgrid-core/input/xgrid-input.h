/*
 * XGrid host input source.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * When XGRID_INPUT_SHM is set, each bound controller reads its state from a
 * named shared section that the shell writes, instead of SDL. The shell owns
 * the window (and with the display sink there is no SDL window at all), so this
 * is the only path that works in both presentation modes.
 *
 * Layout (little endian, 144 bytes):
 *   uint32 magic   'XGDI'
 *   uint32 version 2
 *   uint32 pads    XGRID_INPUT_PADS
 *   uint32 reserved
 *   per pad, 32 bytes:
 *     uint32 buttons xemu's XID mask (A=0, B=1, X=2, Y=3, dleft=4, dup=5,
 *                    dright=6, ddown=7, back=8, start=9, white=10, black=11,
 *                    lstick=12, rstick=13, guide=14)
 *     int32  lx, ly, rx, ry   sticks, -32768..32767
 *     int32  lt, rt           triggers, 0..32767
 *     uint32 sequence         increments once per shell update
 */

#ifndef XGRID_INPUT_H
#define XGRID_INPUT_H

#include <stdbool.h>
#include <stdint.h>

/** Number of pads the shell publishes, one per Xbox port. */
#define XGRID_INPUT_PADS 4

/** Layout version; a shell and core of different versions do not mix. */
#define XGRID_INPUT_VERSION 2u

/** Size of the section header before the first pad record. */
#define XGRID_INPUT_HEADER 16u

/** Size of one pad record. */
#define XGRID_INPUT_STRIDE 32u

/** Returns true when a shared-section input source was requested. */
bool xgrid_input_enabled(void);

/**
 * Selects the shared-section source by name instead of the environment. Used
 * by the in-process core, whose CRT environment predates the shell's settings.
 */
void xgrid_input_set_sink(const char *name);

/**
 * Reads the latest state of pad @p pad (0 to XGRID_INPUT_PADS - 1). The
 * pointer arguments may be NULL. Returns false while the shell has not
 * published a state yet.
 */
bool xgrid_input_read(int pad, uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt);

#endif /* XGRID_INPUT_H */
