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
#include "qemu/main-loop.h"
#include "qemu/thread.h"
#include "qemu/timer.h"
#include "block/aio.h"
#include "qapi/error.h"
#include "system/runstate.h"
#include "ui/xemu-run.h"
#include "ui/xemu-snapshots.h"
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

/*
 * Save states.
 *
 * The request is queued from the shell's thread and runs as a bottom half on
 * the core's main loop: snapshot save/load touches the block layer (which
 * asserts it is on the main thread) and the snapshot thumbnail reads the
 * renderer's framebuffer, whose GL context lives on that same loop.
 */
typedef enum XGridSnapshotRequest {
    XGRID_SNAPSHOT_REQUEST_NONE = 0,
    XGRID_SNAPSHOT_REQUEST_SAVE,
    XGRID_SNAPSHOT_REQUEST_LOAD,
    XGRID_SNAPSHOT_REQUEST_DELETE,
    XGRID_SNAPSHOT_REQUEST_REFRESH,
} XGridSnapshotRequest;

static QemuMutex g_snapshot_mutex;
static bool g_snapshot_mutex_ready;

/* Guarded by g_snapshot_mutex. */
static XGridSnapshotRequest g_snapshot_request;
static char *g_snapshot_request_name;
static int g_snapshot_status;
static char g_snapshot_error[1024];

/* Published list; the array is swapped whole under the mutex. */
static char **g_snapshot_names;
static int g_snapshot_count;

static void xgrid_core_snapshot_lock(void)
{
    if (!g_snapshot_mutex_ready) {
        qemu_mutex_init(&g_snapshot_mutex);
        g_snapshot_mutex_ready = true;
    }
    qemu_mutex_lock(&g_snapshot_mutex);
}

static void xgrid_core_snapshot_unlock(void)
{
    qemu_mutex_unlock(&g_snapshot_mutex);
}

static void xgrid_core_snapshot_set_failed(const char *message)
{
    xgrid_core_snapshot_lock();
    g_snapshot_status = XGRID_SNAPSHOT_FAILED;
    snprintf(g_snapshot_error, sizeof(g_snapshot_error), "%s",
             message != NULL && message[0] != '\0' ? message : "Snapshot failed");
    xgrid_core_snapshot_unlock();
}

static void xgrid_core_snapshot_free_names(void)
{
    if (g_snapshot_names != NULL) {
        for (int i = 0; i < g_snapshot_count; i++) {
            g_free(g_snapshot_names[i]);
        }
        g_free(g_snapshot_names);
        g_snapshot_names = NULL;
    }
    g_snapshot_count = 0;
}

/* Runs on the core's main loop, with the BQL held. */
static void xgrid_core_snapshot_run(void *opaque)
{
    XGridSnapshotRequest request;
    char *name;
    Error *err = NULL;

    (void)opaque;

    xgrid_core_snapshot_lock();
    request = g_snapshot_request;
    name = g_snapshot_request_name;
    g_snapshot_request = XGRID_SNAPSHOT_REQUEST_NONE;
    g_snapshot_request_name = NULL;
    xgrid_core_snapshot_unlock();

    switch (request) {
    case XGRID_SNAPSHOT_REQUEST_SAVE:
        xemu_snapshots_save(name, &err);
        break;

    case XGRID_SNAPSHOT_REQUEST_LOAD: {
        /*
         * The reference stops the machine for the restore and only restarts it
         * when the load succeeded. A failed load must not leave the shell with
         * a frozen guest, so the machine is resumed here.
         */
        bool was_running = runstate_is_running();
        xemu_snapshots_load(name, &err);
        if (err != NULL && was_running && !runstate_is_running()) {
            vm_start();
        }
        break;
    }

    case XGRID_SNAPSHOT_REQUEST_DELETE:
        xemu_snapshots_delete(name, &err);
        break;

    case XGRID_SNAPSHOT_REQUEST_REFRESH: {
        QEMUSnapshotInfo *info = NULL;
        int count = xemu_snapshots_list(&info, NULL, &err);
        if (!err && count >= 0) {
            char **names = count > 0 ? g_new0(char *, count) : NULL;
            for (int i = 0; i < count; i++) {
                names[i] = g_strdup(info[i].name);
            }
            xgrid_core_snapshot_lock();
            xgrid_core_snapshot_free_names();
            g_snapshot_names = names;
            g_snapshot_count = count;
            xgrid_core_snapshot_unlock();
        }
        break;
    }

    case XGRID_SNAPSHOT_REQUEST_NONE:
        break;
    }

    if (err != NULL) {
        char *message = g_strdup(error_get_pretty(err));
        error_free(err);
        xgrid_core_snapshot_set_failed(message);
        g_free(message);
    } else if (request != XGRID_SNAPSHOT_REQUEST_NONE) {
        xgrid_core_snapshot_lock();
        g_snapshot_status = XGRID_SNAPSHOT_COMPLETED;
        g_snapshot_error[0] = '\0';
        xgrid_core_snapshot_unlock();
    }

    g_free(name);
}

static int xgrid_core_snapshot_request_start(XGridSnapshotRequest request,
                                             const char *name)
{
    if (!qatomic_read(&g_running)) {
        return -1;
    }
    if (qemu_get_aio_context() == NULL) {
        /* The core thread has not set up its main loop yet; retry later. */
        return -1;
    }
    if (request != XGRID_SNAPSHOT_REQUEST_REFRESH &&
        (name == NULL || name[0] == '\0')) {
        return -2;
    }
    if (name != NULL && strlen(name) > 255) {
        return -2;
    }

    xgrid_core_snapshot_lock();
    if (g_snapshot_request != XGRID_SNAPSHOT_REQUEST_NONE) {
        xgrid_core_snapshot_unlock();
        return -3;
    }

    g_snapshot_request = request;
    g_snapshot_request_name = name != NULL ? g_strdup(name) : NULL;
    g_snapshot_status = XGRID_SNAPSHOT_PENDING;
    g_snapshot_error[0] = '\0';
    xgrid_core_snapshot_unlock();

    aio_bh_schedule_oneshot(qemu_get_aio_context(), xgrid_core_snapshot_run,
                            NULL);
    return 0;
}

/*
 * Playback control.
 *
 * Pause, resume and frame step change the machine's run state, which needs
 * the BQL, so they are queued as bottom halves on the core's main loop like
 * the snapshot requests.
 *
 * A pause lands on a frame boundary: the machine keeps running until the
 * display path publishes the next frame, then stops there. A step resumes
 * from that boundary and stops the same way after the next frame. A timer
 * covers a launch where no frame is published (the display sink is off, or
 * the window is gone).
 *
 * g_paused is the shell's intent; g_vm_paused is what the core applied. A
 * snapshot load stops the machine for the restore and a step runs it for one
 * frame, so neither may be mistaken for the shell's pause.
 */
#define XGRID_FRAME_TIMEOUT_MS 50

static int g_paused;
static int g_vm_paused;
static int g_pause_pending;
static int g_step_pending;
static int g_step_armed;
static QEMUTimer *g_step_timer;

static void xgrid_core_stop_on_timeout(void *opaque);

static void xgrid_core_cancel_frame_timer(void)
{
    if (g_step_timer != NULL) {
        timer_del(g_step_timer);
    }
}

static void xgrid_core_arm_frame_timer(void)
{
    if (g_step_timer == NULL) {
        g_step_timer = timer_new_ms(QEMU_CLOCK_REALTIME,
                                    xgrid_core_stop_on_timeout, NULL);
    }
    timer_mod(g_step_timer,
              qemu_clock_get_ms(QEMU_CLOCK_REALTIME) + XGRID_FRAME_TIMEOUT_MS);
}

static void xgrid_core_mark_stopped(void)
{
    if (runstate_is_running()) {
        vm_stop(RUN_STATE_PAUSED);
    }
    qatomic_set(&g_vm_paused, 1);
}

/* Runs on the main loop after the frame that ends a pause request. */
static void xgrid_core_stop_for_pause(void *opaque)
{
    (void)opaque;
    xgrid_core_cancel_frame_timer();
    if (qatomic_read(&g_paused) && !qatomic_read(&g_vm_paused)) {
        xgrid_core_mark_stopped();
    }
}

/* Runs on the main loop after the frame that ends a step. */
static void xgrid_core_stop_for_step(void *opaque)
{
    (void)opaque;
    xgrid_core_cancel_frame_timer();
    qatomic_set(&g_step_pending, 0);
    if (qatomic_read(&g_paused) && !qatomic_read(&g_vm_paused)) {
        xgrid_core_mark_stopped();
    }
}

/*
 * Runs on the main loop when the frame never arrived (no display sink, so the
 * core renders into its own window and publishes nothing). A timer may not
 * delete itself, so this only updates the flags.
 */
static void xgrid_core_stop_on_timeout(void *opaque)
{
    (void)opaque;

    qatomic_set(&g_pause_pending, 0);
    qatomic_set(&g_step_armed, 0);
    qatomic_set(&g_step_pending, 0);
    if (qatomic_read(&g_paused)) {
        xgrid_core_mark_stopped();
    }
}

/* Runs on the display path, possibly on the vblank thread. */
static void xgrid_core_display_frame(void *opaque)
{
    AioContext *context;

    (void)opaque;

    context = qemu_get_aio_context();
    if (context == NULL) {
        return;
    }

    if (qatomic_xchg(&g_pause_pending, 0)) {
        aio_bh_schedule_oneshot(context, xgrid_core_stop_for_pause, NULL);
    } else if (qatomic_xchg(&g_step_armed, 0)) {
        aio_bh_schedule_oneshot(context, xgrid_core_stop_for_step, NULL);
    }
}

static void xgrid_core_apply_pause(void *opaque)
{
    (void)opaque;

    /* A pause or resume cancels any frame wait that is still running. */
    xgrid_core_cancel_frame_timer();
    qatomic_set(&g_pause_pending, 0);
    qatomic_set(&g_step_armed, 0);
    qatomic_set(&g_step_pending, 0);

    if (qatomic_read(&g_paused)) {
        if (qatomic_read(&g_vm_paused)) {
            return;
        }

        if (xgrid_display_enabled()) {
            /* Stop on the next frame boundary the display path publishes. */
            xgrid_core_arm_frame_timer();
            qatomic_set(&g_pause_pending, 1);
        } else {
            xgrid_core_mark_stopped();
        }
    } else {
        if (!runstate_is_running()) {
            vm_start();
        }
        qatomic_set(&g_vm_paused, 0);
    }
}

static void xgrid_core_start_step(void *opaque)
{
    (void)opaque;

    if (!qatomic_read(&g_paused) || !qatomic_read(&g_vm_paused)) {
        qatomic_set(&g_step_pending, 0);
        return;
    }

    xgrid_core_arm_frame_timer();
    qatomic_set(&g_step_armed, 1);
    qatomic_set(&g_vm_paused, 0);
    vm_start();
}

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

    xgrid_core_snapshot_lock();
    g_snapshot_status = XGRID_SNAPSHOT_IDLE;
    g_snapshot_error[0] = '\0';
    g_snapshot_request = XGRID_SNAPSHOT_REQUEST_NONE;
    xgrid_core_snapshot_unlock();

    /*
     * The host owns the process streams: redirecting them here would capture
     * the shell's own output as well, because a loaded library shares them.
     */
    qatomic_set(&g_paused, 0);
    qatomic_set(&g_vm_paused, 0);
    qatomic_set(&g_pause_pending, 0);
    qatomic_set(&g_step_pending, 0);
    qatomic_set(&g_step_armed, 0);
    xgrid_display_set_frame_callback(xgrid_core_display_frame, NULL);
    xgrid_display_set_sink(display_sink);
    xgrid_input_set_sink(input_sink);

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

/** Queues a pause or a resume. */
XGRID_CORE_EXPORT int xgrid_core_set_paused(int paused)
{
    if (paused != 0 && paused != 1) {
        return -2;
    }
    if (!qatomic_read(&g_running) || qemu_get_aio_context() == NULL) {
        return -1;
    }

    qatomic_set(&g_paused, paused);
    aio_bh_schedule_oneshot(qemu_get_aio_context(), xgrid_core_apply_pause,
                            NULL);
    return 0;
}

/** Returns 1 while the shell has the machine paused. */
XGRID_CORE_EXPORT int xgrid_core_is_paused(void)
{
    return qatomic_read(&g_running) && qatomic_read(&g_paused) ? 1 : 0;
}

/** Runs one frame while paused and pauses again. */
XGRID_CORE_EXPORT int xgrid_core_frame_step(void)
{
    if (!qatomic_read(&g_running) || qemu_get_aio_context() == NULL) {
        return -1;
    }
    if (!qatomic_read(&g_paused)) {
        return -2;
    }
    if (!qatomic_read(&g_vm_paused)) {
        /* The pause is still waiting for its frame boundary. */
        return -2;
    }
    if (qatomic_xchg(&g_step_pending, 1)) {
        return -3;
    }

    aio_bh_schedule_oneshot(qemu_get_aio_context(), xgrid_core_start_step,
                            NULL);
    return 0;
}

/** Queues a snapshot save under @p name. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_save(const char *name)
{
    return xgrid_core_snapshot_request_start(XGRID_SNAPSHOT_REQUEST_SAVE, name);
}

/** Queues a snapshot load. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_load(const char *name)
{
    return xgrid_core_snapshot_request_start(XGRID_SNAPSHOT_REQUEST_LOAD, name);
}

/** Queues a snapshot delete. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_delete(const char *name)
{
    return xgrid_core_snapshot_request_start(XGRID_SNAPSHOT_REQUEST_DELETE,
                                             name);
}

/** Queues a refresh of the snapshot list. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_refresh(void)
{
    return xgrid_core_snapshot_request_start(XGRID_SNAPSHOT_REQUEST_REFRESH,
                                             NULL);
}

/** Returns one of the XGRID_SNAPSHOT_* status values. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_status(void)
{
    int status;

    xgrid_core_snapshot_lock();
    status = g_snapshot_status;
    xgrid_core_snapshot_unlock();
    return status;
}

/** Returns the last request's error text, or an empty string. */
XGRID_CORE_EXPORT const char *xgrid_core_snapshot_error(void)
{
    /*
     * The shell asks from one thread; the copy keeps the text intact while
     * the core publishes the next request's result.
     */
    static char copy[sizeof(g_snapshot_error)];

    xgrid_core_snapshot_lock();
    snprintf(copy, sizeof(copy), "%s", g_snapshot_error);
    xgrid_core_snapshot_unlock();
    return copy;
}

/** Returns how many snapshots the last refresh found. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_count(void)
{
    int count;

    xgrid_core_snapshot_lock();
    count = g_snapshot_count;
    xgrid_core_snapshot_unlock();
    return count;
}

/** Returns the name of snapshot @p index, or NULL when out of range. */
XGRID_CORE_EXPORT const char *xgrid_core_snapshot_name(int index)
{
    const char *name = NULL;

    xgrid_core_snapshot_lock();
    if (index >= 0 && index < g_snapshot_count) {
        name = g_snapshot_names[index];
    }
    xgrid_core_snapshot_unlock();
    return name;
}
