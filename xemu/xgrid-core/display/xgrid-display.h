/*
 * XGrid host display integration.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The core owns the picture. It paints directly into its own swapchain when the
 * shell embeds the window, and publishes frames only for headless runs and
 * benchmarks (XGRID_DISPLAY_SHM). Presentation filters and the frame-rate
 * overlay are core features: the shell only selects them. Windows uses a named
 * file mapping for the sink; other hosts use a POSIX shared-memory object.
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

#ifdef __cplusplus
extern "C" {
#endif

/** Presentation filters implemented by the core's final blit shader. */
typedef enum XGridPresentFilter {
    XGRID_FILTER_NEAREST = 0,
    XGRID_FILTER_BILINEAR = 1,
    XGRID_FILTER_SCANLINE = 2,
    XGRID_FILTER_CRT = 3,
} XGridPresentFilter;

/**
 * Selects the presentation filter. Safe to call before or during a run.
 * Returns -1 for an unknown filter.
 */
int xgrid_present_set_filter(int filter);

/**
 * Returns the active filter, or -1 when the shell has not selected one. A
 * standalone launch falls back to the core's configured filter method.
 */
int xgrid_present_filter(void);

/** Enables or disables the core-drawn frame-rate overlay. */
void xgrid_present_set_overlay(bool enabled);

/** Returns true when the core should draw the frame-rate overlay. */
bool xgrid_present_overlay(void);

/**
 * Returns true when the shell will embed the core window (XGRID_EMBED) or the
 * in-process ABI selected embedding before the machine started.
 */
bool xgrid_embed_enabled(void);

/** Selects embedding before the window is created. */
void xgrid_present_set_embed(bool enabled);

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
 * Called after a frame has been published, so the core can react to a new
 * picture (a single-frame step stops here). May run on any thread.
 */
typedef void (*XGridDisplayFrameCallback)(void *opaque);

/** Registers the frame callback, or clears it with NULL. */
void xgrid_display_set_frame_callback(XGridDisplayFrameCallback callback,
                                      void *opaque);

/**
 * Copies one BGRA frame into the shared section, top row first.
 *
 * The section is created on the first call and recreated when the geometry
 * changes. Safe to call when the sink is disabled (it does nothing).
 */
void xgrid_display_publish(const void *pixels, uint32_t width, uint32_t height,
                           uint32_t stride);

#ifdef __cplusplus
}
#endif

#endif /* XGRID_DISPLAY_H */
