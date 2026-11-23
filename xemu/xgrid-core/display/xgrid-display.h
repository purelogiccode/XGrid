/*
 * XGrid host display sink.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * When XGRID_DISPLAY_SHM is set, the core publishes every presented frame into
 * a named shared section instead of (or as well as) showing its own window. The
 * shell owns presentation from there: scaling, filters, OSD and screenshots are
 * all frontend work.
 *
 * Layout (little endian):
 *   uint32 magic   'XGDS'
 *   uint32 width
 *   uint32 height
 *   uint32 stride  bytes per row
 *   uint64 frame   increments once per published frame
 *   pixel data     width x height BGRA, top row first
 */

#ifndef XGRID_DISPLAY_H
#define XGRID_DISPLAY_H

#include <stdbool.h>
#include <stdint.h>

/**
 * Returns true when the shell will embed the core window (XGRID_EMBED).
 */
bool xgrid_embed_enabled(void);

/**
 * Selects the shared-section sink by name instead of the environment. Used by
 * the in-process core, whose CRT environment predates the shell's settings.
 */
void xgrid_display_set_sink(const char *name);

/**
 * Returns true when a shared-section sink was requested in the environment.
 */
bool xgrid_display_enabled(void);

/**
 * Copies one BGRA frame into the shared section, top row first.
 *
 * The section is created on the first call and recreated when the geometry
 * changes. Safe to call when the sink is disabled (it does nothing).
 */
void xgrid_display_publish(const void *pixels, uint32_t width, uint32_t height,
                           uint32_t stride);

#endif /* XGRID_DISPLAY_H */
