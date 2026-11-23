/*
 * XGrid in-process core ABI.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The shell loads xgrid_core.dll and calls these functions. The machine runs
 * on its own thread, exactly like the standalone binary's QEMU thread plus UI
 * loop; frames and pad state keep using the shared sections in
 * xgrid-core/display and xgrid-core/input.
 *
 * A process hosts one machine. QEMU keeps global state that is initialised
 * once, so start() is accepted once and join() releases the thread handle
 * after the run loop has stopped. A DLL (not the EXE) is the load target: the
 * Windows loader initialises the CRT of a DLL, which an EXE loaded with
 * LoadLibrary does not do.
 */

#include "qemu/osdep.h"
#include "qemu/atomic.h"
#include "qemu/thread.h"
#include "ui/xemu-run.h"
#include "xemu-version.h"
#include "xgrid-core.h"
#include "../display/xgrid-display.h"
#include "../input/xgrid-input.h"

/* The shell's default stack is smaller than a TCG thread wants. */
#define XGRID_CORE_THREAD_STACK (8u * 1024u * 1024u)

static QemuThread g_thread;
static bool g_thread_created;
static bool g_started;
static bool g_running;
static int g_exit_code;
static char **g_argv;
static int g_argc;

static void *xgrid_core_thread(void *opaque)
{
    int status = xemu_run(g_argc, g_argv, true);

    qatomic_set(&g_exit_code, status);
    qatomic_set(&g_running, false);
    return NULL;
}

/**
 * Starts the machine on a background thread.
 *
 * @param config_path The xemu settings file (the shell writes it).
 * @param log_path Optional file that receives the core's stdout/stderr, or
 *                 NULL to keep the host's streams.
 * @param display_sink Shared-section name for presented frames, or NULL.
 * @param input_sink Shared-section name for port 1, or NULL.
 * @param extra_argv Additional command-line arguments, or NULL.
 * @param extra_argc Count of extra_argv entries.
 * @return 0 on success, -1 when a machine already ran, -2 for a bad path,
 *         -3 for a bad argument count.
 */
XGRID_CORE_EXPORT int xgrid_core_start(const char *config_path,
                                       const char *log_path,
                                       const char *display_sink,
                                       const char *input_sink,
                                       const char *const *extra_argv,
                                       int extra_argc)
{
    if (g_started) {
        return -1;
    }
    if (config_path == NULL || config_path[0] == '\0') {
        return -2;
    }
    if (extra_argc < 0 || (extra_argc > 0 && extra_argv == NULL)) {
        return -3;
    }

    xgrid_display_set_sink(display_sink);
    xgrid_input_set_sink(input_sink);

    if (log_path != NULL && log_path[0] != '\0') {
        FILE *log = freopen(log_path, "w", stderr);
        if (log != NULL) {
            /* stdout shares the file; QEMU reports through stderr. */
            (void)freopen(log_path, "a", stdout);
        }
    }

    g_argc = 3 + extra_argc;
    g_argv = g_new0(char *, g_argc + 1);
    g_argv[0] = g_strdup("xemu");
    g_argv[1] = g_strdup("-config_path");
    g_argv[2] = g_strdup(config_path);
    for (int i = 0; i < extra_argc; i++) {
        g_argv[3 + i] = g_strdup(extra_argv[i] != NULL ? extra_argv[i] : "");
    }
    g_argv[g_argc] = NULL;

    g_started = true;
    qatomic_set(&g_exit_code, 0);
    qatomic_set(&g_running, true);
#ifdef _WIN32
    qemu_thread_stack_size = XGRID_CORE_THREAD_STACK;
#endif
    qemu_thread_create(&g_thread, "xgrid-core", xgrid_core_thread, NULL,
                       QEMU_THREAD_JOINABLE);
    g_thread_created = true;
    return 0;
}

/** Asks the machine to power off. Safe to call from any thread. */
XGRID_CORE_EXPORT void xgrid_core_request_stop(void)
{
    if (qatomic_read(&g_running)) {
        xemu_request_shutdown();
    }
}

/** Returns 1 while the machine is running, 0 otherwise. */
XGRID_CORE_EXPORT int xgrid_core_is_running(void)
{
    return qatomic_read(&g_running) ? 1 : 0;
}

/** Returns the last run's exit status (0 before it finishes). */
XGRID_CORE_EXPORT int xgrid_core_exit_code(void)
{
    return qatomic_read(&g_exit_code);
}

/**
 * Joins the run thread once it has stopped and returns its exit status.
 *
 * @return The exit status, or -1 when no machine was started, -2 while the
 *         machine is still running.
 */
XGRID_CORE_EXPORT int xgrid_core_join(void)
{
    if (!g_thread_created) {
        return -1;
    }
    if (qatomic_read(&g_running)) {
        return -2;
    }

    qemu_thread_join(&g_thread);
    g_thread_created = false;
    return qatomic_read(&g_exit_code);
}

/** Returns the core version string (for example "0.8.139"). */
XGRID_CORE_EXPORT const char *xgrid_core_version(void)
{
    return xemu_version;
}
