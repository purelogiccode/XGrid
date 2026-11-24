/*
 * XGrid in-process core entry points.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The standalone binary and the shell-hosted core share one run loop. QEMU's
 * global state is single-use, so xemu_run() may be called at most once per
 * process; xgrid_core_start() (xgrid-core/abi) is the supported wrapper.
 */

#ifndef XEMU_RUN_H
#define XEMU_RUN_H

#include <stdbool.h>

/**
 * Runs one machine to completion.
 *
 * @param argc Argument count, including the program name.
 * @param argv Arguments; "-config_path <file>" selects the settings file.
 * @param library_mode True when the caller owns the process: modal error
 *                     dialogs are suppressed and startup failures are
 *                     returned instead of terminating the process.
 * @return The QEMU exit status.
 */
int xemu_run(int argc, char **argv, bool library_mode);

/**
 * Asks the run loop to power the machine off at its next iteration. Safe to
 * call from any thread, including before xemu_run() has started.
 */
void xemu_request_shutdown(void);

/**
 * Sets the vblank pace multiplier used by the shell's fast-forward control.
 * 1 is real time; 4 presents four times as many frames per real second. Safe
 * to call from any thread while the machine runs.
 */
void xemu_set_speed_multiplier(int multiplier);

/** Returns the current vblank pace multiplier (1 is real time). */
int xemu_get_speed_multiplier(void);

#endif /* XEMU_RUN_H */
