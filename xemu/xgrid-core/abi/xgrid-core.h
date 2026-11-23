/*
 * XGrid in-process core ABI.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The shell loads xgrid_core.dll and calls these functions. One process hosts
 * one machine: start() is accepted once and join() releases the thread handle
 * after the run loop stops.
 */

#ifndef XGRID_CORE_ABI_H
#define XGRID_CORE_ABI_H

#ifdef _WIN32
#define XGRID_CORE_EXPORT __declspec(dllexport)
#else
#define XGRID_CORE_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Starts the machine on a background thread.
 *
 * @param config_path The xemu settings file (the shell writes it).
 * @param display_sink Shared-section name for presented frames, or NULL.
 * @param input_sink Shared-section name for port 1, or NULL.
 * @param extra_argv Additional command-line arguments, or NULL.
 * @param extra_argc Count of extra_argv entries.
 * @return 0 on success, -1 when a machine already ran, -2 for a bad path,
 *         -3 for a bad argument count.
 */
XGRID_CORE_EXPORT int xgrid_core_start(const char *config_path,
                                       const char *display_sink,
                                       const char *input_sink,
                                       const char *const *extra_argv,
                                       int extra_argc);

/** Asks the machine to power off. Safe to call from any thread. */
XGRID_CORE_EXPORT void xgrid_core_request_stop(void);

/** Returns 1 while the machine is running, 0 otherwise. */
XGRID_CORE_EXPORT int xgrid_core_is_running(void);

/** Returns the last run's exit status (0 before it finishes). */
XGRID_CORE_EXPORT int xgrid_core_exit_code(void);

/**
 * Joins the run thread once it has stopped and returns its exit status.
 *
 * @return The exit status, or -1 when no machine was started, -2 while the
 *         machine is still running.
 */
XGRID_CORE_EXPORT int xgrid_core_join(void);

/** Returns the core version string (for example "0.8.139"). */
XGRID_CORE_EXPORT const char *xgrid_core_version(void);

#ifdef __cplusplus
}
#endif

#endif /* XGRID_CORE_ABI_H */
