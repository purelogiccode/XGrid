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

/*
 * Picture presentation.
 *
 * The core owns the picture in both presentation modes. These select the
 * core-side filter and overlay; no shell component processes pixels. The
 * separate-process path passes the initial values through XGRID_PRESENT_FILTER
 * and XGRID_FPS_OVERLAY.
 */

/** Selects a presentation filter; returns -1 for an unknown value. */
XGRID_CORE_EXPORT int xgrid_core_set_present_filter(int filter);

/** Enables (non-zero) or disables the core-drawn frame-rate overlay. */
XGRID_CORE_EXPORT void xgrid_core_set_fps_overlay(int enabled);

/** Selects embedding before the core creates its window. */
XGRID_CORE_EXPORT void xgrid_core_set_embed(int enabled);

/**
 * Queues a screenshot of the running picture. The core writes the PNG into
 * its configured screenshot directory on the next rendered frame, so the
 * shell never receives or copies the picture.
 *
 * @return 0 when queued, -1 when no machine is running.
 */
XGRID_CORE_EXPORT int xgrid_core_request_screenshot(void);

/*
 * Playback control.
 *
 * Pause, resume and single-frame step are requested from the shell's thread
 * and applied on the core's main loop: a run-state change needs the BQL, and
 * a step has to stop on the frame boundary the display path publishes.
 *
 * The shell reads the intent it asked for; a step resumes the machine and
 * pauses it again after the next frame.
 */

/**
 * Queues a pause (@p paused non-zero) or a resume (@p paused zero). A pause
 * stops the machine on the next frame the display path publishes.
 *
 * @return 0 when queued, -1 when no machine is running, -2 for a bad value.
 */
XGRID_CORE_EXPORT int xgrid_core_set_paused(int paused);

/** Returns 1 while the shell has the machine paused, 0 otherwise. */
XGRID_CORE_EXPORT int xgrid_core_is_paused(void);

/**
 * Runs one frame while paused and pauses again after that frame. A pause that
 * is still waiting for its frame boundary is reported the same way as a
 * running machine.
 *
 * @return 0 when queued, -1 when no machine is running, -2 when the machine
 *         is not paused (or the pause is still settling), -3 while the
 *         previous step is still running.
 */
XGRID_CORE_EXPORT int xgrid_core_frame_step(void);

/**
 * Sets the presentation pace used for fast-forward (1 is real time, up to 8).
 * The guest clock is unchanged; the vblank pace scales instead.
 *
 * @return 0 when accepted, -1 when no machine is running, -2 for a multiplier
 *         outside 1..8.
 */
XGRID_CORE_EXPORT int xgrid_core_set_speed(int multiplier);

/** Returns the current speed multiplier (1 is real time). */
XGRID_CORE_EXPORT int xgrid_core_get_speed(void);

/*
 * Disc change.
 *
 * The standalone emulator changes the inserted disc through its own menu; the
 * shell has no menu, so it queues the same block-layer operation here. The
 * request runs on the core's main loop and the shell polls the status, exactly
 * like a save-state request. The medium format follows the file extension:
 * .chd and .zar open through the XGrid block drivers, everything else is raw.
 *
 * Status values returned by xgrid_core_disc_status().
 */
#define XGRID_DISC_IDLE 0
#define XGRID_DISC_PENDING 1
#define XGRID_DISC_COMPLETED 2
#define XGRID_DISC_FAILED 3

/**
 * Queues a change of the inserted disc.
 *
 * @return 0 when queued, -1 when no machine is running, -2 for a bad path,
 *         -3 when another disc request is still in flight.
 */
XGRID_CORE_EXPORT int xgrid_core_disc_load(const char *path);

/**
 * Queues an eject, leaving the drive connected and empty.
 *
 * @return 0 when queued, -1 when no machine is running, -3 when another disc
 *         request is still in flight.
 */
XGRID_CORE_EXPORT int xgrid_core_disc_eject(void);

/** Returns one of the XGRID_DISC_* status values. */
XGRID_CORE_EXPORT int xgrid_core_disc_status(void);

/** Returns the last disc request's error text, or an empty string. */
XGRID_CORE_EXPORT const char *xgrid_core_disc_error(void);

/*
 * Save states.
 *
 * xemu keeps a save state as an internal snapshot of the hard disk image, so
 * the state survives the process. Snapshot work touches the block layer and
 * the renderer's GL context, which only the core's own main loop may do, so
 * every request is queued and runs there. The shell starts one request and
 * polls its status; a request is accepted only while no other one is running.
 *
 * Status values returned by xgrid_core_snapshot_status().
 */
#define XGRID_SNAPSHOT_IDLE 0
#define XGRID_SNAPSHOT_PENDING 1
#define XGRID_SNAPSHOT_COMPLETED 2
#define XGRID_SNAPSHOT_FAILED 3

/**
 * Queues a snapshot save under @p name (overwriting a snapshot of that name).
 *
 * @return 0 when queued, -1 when no machine is running, -2 for a bad name,
 *         -3 when another request is still in flight.
 */
XGRID_CORE_EXPORT int xgrid_core_snapshot_save(const char *name);

/** Queues a snapshot load. Same return values as the save call. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_load(const char *name);

/** Queues a snapshot delete. Same return values as the save call. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_delete(const char *name);

/**
 * Queues a refresh of the snapshot list.
 *
 * @return 0 when queued, -1 when no machine is running, -3 when another
 *         request is still in flight.
 */
XGRID_CORE_EXPORT int xgrid_core_snapshot_refresh(void);

/** Returns one of the XGRID_SNAPSHOT_* status values. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_status(void);

/** Returns the last request's error text, or an empty string. */
XGRID_CORE_EXPORT const char *xgrid_core_snapshot_error(void);

/** Returns how many snapshots the last refresh found. */
XGRID_CORE_EXPORT int xgrid_core_snapshot_count(void);

/** Returns the name of snapshot @p index, or NULL when out of range. */
XGRID_CORE_EXPORT const char *xgrid_core_snapshot_name(int index);

#ifdef __cplusplus
}
#endif

#endif /* XGRID_CORE_ABI_H */
