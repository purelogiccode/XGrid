/*
 * xemu Input Management
 *
 * Copyright (C) 2020-2021 Matt Borgerson
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */


#include "qemu/osdep.h"
#include "hw/qdev-core.h"
#include "hw/qdev-properties.h"
#include "qapi/error.h"
#include "monitor/qdev.h"
#include "qobject/qdict.h"
#include "qemu/option.h"
#include "qemu/timer.h"
#include "qemu/config-file.h"

#include "xemu-input.h"
#include "../xgrid-core/input/xgrid-input.h"
#include "xemu-notifications.h"
#include "xemu-settings.h"
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <SDL3/SDL_haptic.h>

#include "system/blockdev.h"
#include "hw/xbox/chihiro/chihiro.h"
#include "hw/xbox/chihiro/chihiro-jvs.h"
#include "hw/xbox/chihiro/chihiro-driveboard-sega838.h"
#include "hw/xbox/chihiro/chihiro-driveboard-v257.h"
#include "hw/xbox/chihiro/chihiro-log.h"

// #define DEBUG_INPUT

#ifdef DEBUG_INPUT
#define DPRINTF(fmt, ...) \
    do { fprintf(stderr, fmt, ## __VA_ARGS__); } while (0)
#else
#define DPRINTF(fmt, ...) \
    do { } while (0)
#endif

#define XEMU_INPUT_MIN_INPUT_UPDATE_INTERVAL_US  2500
#define XEMU_INPUT_MIN_RUMBLE_UPDATE_INTERVAL_US 2500

#if 0
static void xemu_input_print_controller_state(ControllerState *state)
{
    DPRINTF("     A = %d,      B = %d,     X = %d,     Y = %d\n"
           "  Left = %d,     Up = %d, Right = %d,  Down = %d\n"
           "  Back = %d,  Start = %d, White = %d, Black = %d\n"
           "Lstick = %d, Rstick = %d, Guide = %d\n"
           "\n"
           "LTrig   = %.3f, RTrig   = %.3f\n"
           "LStickX = %.3f, RStickX = %.3f\n"
           "LStickY = %.3f, RStickY = %.3f\n\n",
        !!(state->buttons & CONTROLLER_BUTTON_A),
        !!(state->buttons & CONTROLLER_BUTTON_B),
        !!(state->buttons & CONTROLLER_BUTTON_X),
        !!(state->buttons & CONTROLLER_BUTTON_Y),
        !!(state->buttons & CONTROLLER_BUTTON_DPAD_LEFT),
        !!(state->buttons & CONTROLLER_BUTTON_DPAD_UP),
        !!(state->buttons & CONTROLLER_BUTTON_DPAD_RIGHT),
        !!(state->buttons & CONTROLLER_BUTTON_DPAD_DOWN),
        !!(state->buttons & CONTROLLER_BUTTON_BACK),
        !!(state->buttons & CONTROLLER_BUTTON_START),
        !!(state->buttons & CONTROLLER_BUTTON_WHITE),
        !!(state->buttons & CONTROLLER_BUTTON_BLACK),
        !!(state->buttons & CONTROLLER_BUTTON_LSTICK),
        !!(state->buttons & CONTROLLER_BUTTON_RSTICK),
        !!(state->buttons & CONTROLLER_BUTTON_GUIDE),
        state->axis[CONTROLLER_AXIS_LTRIG],
        state->axis[CONTROLLER_AXIS_RTRIG],
        state->axis[CONTROLLER_AXIS_LSTICK_X],
        state->axis[CONTROLLER_AXIS_RSTICK_X],
        state->axis[CONTROLLER_AXIS_LSTICK_Y],
        state->axis[CONTROLLER_AXIS_RSTICK_Y]
        );
}
#endif

ControllerStateList available_controllers =
    QTAILQ_HEAD_INITIALIZER(available_controllers);
ControllerState *bound_controllers[4] = { NULL, NULL, NULL, NULL };
const char *bound_drivers[4] = { DRIVER_DUKE, DRIVER_DUKE, DRIVER_DUKE,
                                 DRIVER_DUKE };
int test_mode;

static const char **port_index_to_settings_key_map[] = {
    &g_config.input.bindings.port1,
    &g_config.input.bindings.port2,
    &g_config.input.bindings.port3,
    &g_config.input.bindings.port4,
};

static const char **port_index_to_driver_settings_key_map[] = {
    &g_config.input.bindings.port1_driver,
    &g_config.input.bindings.port2_driver,
    &g_config.input.bindings.port3_driver, 
    &g_config.input.bindings.port4_driver
};

static int *peripheral_types_settings_map[4][2] = {
    { &g_config.input.peripherals.port1.peripheral_type_0,
      &g_config.input.peripherals.port1.peripheral_type_1 },
    { &g_config.input.peripherals.port2.peripheral_type_0,
      &g_config.input.peripherals.port2.peripheral_type_1 },
    { &g_config.input.peripherals.port3.peripheral_type_0,
      &g_config.input.peripherals.port3.peripheral_type_1 },
    { &g_config.input.peripherals.port4.peripheral_type_0,
      &g_config.input.peripherals.port4.peripheral_type_1 }
};

static const char **peripheral_params_settings_map[4][2] = {
    { &g_config.input.peripherals.port1.peripheral_param_0,
      &g_config.input.peripherals.port1.peripheral_param_1 },
    { &g_config.input.peripherals.port2.peripheral_param_0,
      &g_config.input.peripherals.port2.peripheral_param_1 },
    { &g_config.input.peripherals.port3.peripheral_param_0,
      &g_config.input.peripherals.port3.peripheral_param_1 },
    { &g_config.input.peripherals.port4.peripheral_param_0,
      &g_config.input.peripherals.port4.peripheral_param_1 }
};

int *g_keyboard_scancode_map[25] = {
    &g_config.input.keyboard_controller_scancode_map.a,
    &g_config.input.keyboard_controller_scancode_map.b,
    &g_config.input.keyboard_controller_scancode_map.x,
    &g_config.input.keyboard_controller_scancode_map.y,
    &g_config.input.keyboard_controller_scancode_map.back,
    &g_config.input.keyboard_controller_scancode_map.guide,
    &g_config.input.keyboard_controller_scancode_map.start,
    &g_config.input.keyboard_controller_scancode_map.lstick_btn,
    &g_config.input.keyboard_controller_scancode_map.rstick_btn,
    &g_config.input.keyboard_controller_scancode_map.white,
    &g_config.input.keyboard_controller_scancode_map.black,
    &g_config.input.keyboard_controller_scancode_map.dpad_up,
    &g_config.input.keyboard_controller_scancode_map.dpad_down,
    &g_config.input.keyboard_controller_scancode_map.dpad_left,
    &g_config.input.keyboard_controller_scancode_map.dpad_right,
    &g_config.input.keyboard_controller_scancode_map.lstick_up,
    &g_config.input.keyboard_controller_scancode_map.lstick_left,
    &g_config.input.keyboard_controller_scancode_map.lstick_right,
    &g_config.input.keyboard_controller_scancode_map.lstick_down,
    &g_config.input.keyboard_controller_scancode_map.ltrigger,
    &g_config.input.keyboard_controller_scancode_map.rstick_up,
    &g_config.input.keyboard_controller_scancode_map.rstick_left,
    &g_config.input.keyboard_controller_scancode_map.rstick_right,
    &g_config.input.keyboard_controller_scancode_map.rstick_down,
    &g_config.input.keyboard_controller_scancode_map.rtrigger,
};

static void check_and_reset_in_range(int *btn, int min, int max,
                                     const char *message)
{
    if (*btn < min || *btn >= max) {
        fprintf(stderr, "%s\n", message);
        *btn = min;
    }
}

static void xemu_input_bindings_set_in_range(ControllerState *con)
{
#define CHECK_RESET_BUTTON(btn)                                            \
    check_and_reset_in_range(&con->controller_map->controller_mapping.btn, \
                             SDL_GAMEPAD_BUTTON_INVALID,                   \
                             SDL_GAMEPAD_BUTTON_COUNT,                     \
                             "Invalid entry for button " #btn ", resetting")

    CHECK_RESET_BUTTON(a);
    CHECK_RESET_BUTTON(b);
    CHECK_RESET_BUTTON(x);
    CHECK_RESET_BUTTON(y);
    CHECK_RESET_BUTTON(dpad_left);
    CHECK_RESET_BUTTON(dpad_up);
    CHECK_RESET_BUTTON(dpad_right);
    CHECK_RESET_BUTTON(dpad_down);
    CHECK_RESET_BUTTON(back);
    CHECK_RESET_BUTTON(start);
    CHECK_RESET_BUTTON(lshoulder);
    CHECK_RESET_BUTTON(rshoulder);
    CHECK_RESET_BUTTON(lstick_btn);
    CHECK_RESET_BUTTON(rstick_btn);
    CHECK_RESET_BUTTON(guide);

#undef CHECK_RESET_BUTTON

#define CHECK_RESET_AXIS(axis)                                              \
    check_and_reset_in_range(&con->controller_map->controller_mapping.axis, \
                             SDL_GAMEPAD_AXIS_INVALID,                      \
                             SDL_GAMEPAD_AXIS_COUNT,                        \
                             "Invalid entry for button " #axis ", resetting")

    CHECK_RESET_AXIS(axis_trigger_left);
    CHECK_RESET_AXIS(axis_trigger_right);
    CHECK_RESET_AXIS(axis_left_x);
    CHECK_RESET_AXIS(axis_left_y);
    CHECK_RESET_AXIS(axis_right_x);
    CHECK_RESET_AXIS(axis_right_y);

#undef CHECK_RESET_AXIS
}

static void xemu_input_bindings_reload_map(ControllerState *con)
{
    assert(con->type == INPUT_DEVICE_SDL_GAMEPAD);

    char guid[35] = { 0 };
    SDL_GUIDToString(con->sdl_joystick_guid, guid, sizeof(guid));
    if (!xemu_settings_load_gamepad_mapping(guid, &con->controller_map)) {
        return;
    }

    // If this controller did not exist in the mapping array, the config will
    // have been reallocated. Any gamepad mapping pointers for other controllers
    // are now invalid, and need to be reloaded.
    ControllerState *iter, *next;
    bool is_new_mapping;
    QTAILQ_FOREACH_SAFE (iter, &available_controllers, entry, next) {
        if (iter == con || iter->type != INPUT_DEVICE_SDL_GAMEPAD) {
            continue;
        }

        memset(guid, 0, sizeof(guid));
        SDL_GUIDToString(iter->sdl_joystick_guid, guid, sizeof(guid));

        is_new_mapping =
            xemu_settings_load_gamepad_mapping(guid, &iter->controller_map);
        assert(!is_new_mapping &&
               "Existing controller GUIDs should exist in the config");

        xemu_input_bindings_set_in_range(iter);
    }
}

static const char *get_bound_driver(int port)
{
    assert(port >= 0 && port <= 3);
    const char *driver = *port_index_to_driver_settings_key_map[port];

    // If the driver in the config is NULL, empty, or unrecognized 
    // then default to DRIVER_DUKE
    if (driver == NULL)
        return DRIVER_DUKE;
    if (strlen(driver) == 0)
        return DRIVER_DUKE;
    if (strcmp(driver, DRIVER_DUKE) == 0)
        return DRIVER_DUKE;
    if (strcmp(driver, DRIVER_S) == 0)
        return DRIVER_S;

    return DRIVER_DUKE;
}

static const int port_map[4] = { 3, 4, 1, 2 };

void xemu_input_init(void)
{
    if (g_config.input.background_input_capture) {
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    }

    if (!SDL_Init(SDL_INIT_GAMEPAD)) {
        fprintf(stderr, "Failed to initialize SDL gamepad subsystem: %s\n", SDL_GetError());
        exit(1);
    }

    if (g_config.input.gamecontrollerdb_path && strlen(g_config.input.gamecontrollerdb_path) > 0) {
        int count = SDL_AddGamepadMappingsFromFile(g_config.input.gamecontrollerdb_path);
        if (count > 0) {
            fprintf(stderr, "Loaded %d custom gamepad mapping(s) from '%s'\n",
                    count, g_config.input.gamecontrollerdb_path);
        } else if (count < 0) {
            fprintf(stderr, "Failed to load gamepad mappings from '%s': %s\n",
                    g_config.input.gamecontrollerdb_path, SDL_GetError());
        }
    }

    // Create the keyboard input (always first)
    ControllerState *new_con = malloc(sizeof(ControllerState));
    memset(new_con, 0, sizeof(ControllerState));
    new_con->type = INPUT_DEVICE_SDL_KEYBOARD;
    new_con->name = "Keyboard";
    new_con->bound = -1;
    new_con->peripheral_types[0] = PERIPHERAL_NONE;
    new_con->peripheral_types[1] = PERIPHERAL_NONE;
    new_con->peripherals[0] = NULL;
    new_con->peripherals[1] = NULL;

    for (int i = 0; i < 25; i++) {
        static const char *format_str =
            "WARNING: Keyboard controller map scancode out of range "
            "(%d) : Disabled\n";
        char buf[128];
        snprintf(buf, sizeof(buf), format_str, i);
        check_and_reset_in_range(g_keyboard_scancode_map[i],
                                 SDL_SCANCODE_UNKNOWN, SDL_SCANCODE_COUNT, buf);
    }

    bound_drivers[0] = get_bound_driver(0);
    bound_drivers[1] = get_bound_driver(1);
    bound_drivers[2] = get_bound_driver(2);
    bound_drivers[3] = get_bound_driver(3);

    // Check to see if we should auto-bind the keyboard
    int port = xemu_input_get_controller_default_bind_port(new_con, 0);
    if (port >= 0) {
        xemu_input_bind(port, new_con, 0);
        char buf[128];
        snprintf(buf, sizeof(buf), "Connected '%s' to port %d", new_con->name, port+1);
        xemu_queue_notification(buf);
        xemu_input_rebind_xmu(port);
    }

    QTAILQ_INSERT_TAIL(&available_controllers, new_con, entry);

    /*
     * XGrid: the shell supplies up to four pads through the input sink. Bind a
     * controller to every port the keyboard did not take, so the sink can feed
     * players 2-4. The bindings are not saved: a launch without the shell
     * keeps the configured controller layout.
     */
    if (xgrid_input_enabled()) {
        for (int i = 0; i < 4; i++) {
            if (bound_controllers[i] != NULL) {
                continue;
            }

            ControllerState *shell_con = calloc(1, sizeof(ControllerState));
            shell_con->type = INPUT_DEVICE_XGRID;
            shell_con->name = "XGrid pad";
            shell_con->bound = -1;
            shell_con->peripheral_types[0] = PERIPHERAL_NONE;
            shell_con->peripheral_types[1] = PERIPHERAL_NONE;
            QTAILQ_INSERT_TAIL(&available_controllers, shell_con, entry);
            xemu_input_bind(i, shell_con, 0);
        }
    }
}

int xemu_input_get_controller_default_bind_port(ControllerState *state, int start)
{
    char guid[35] = { 0 };
    if (state->type == INPUT_DEVICE_SDL_GAMEPAD) {
        SDL_GUIDToString(state->sdl_joystick_guid, guid, sizeof(guid));
    } else if (state->type == INPUT_DEVICE_SDL_KEYBOARD) {
        snprintf(guid, sizeof(guid), "keyboard");
    }

    for (int i = start; i < 4; i++) {
        if (strcmp(guid, *port_index_to_settings_key_map[i]) == 0) {
            return i;
        }
    }

    return -1;
}

void xemu_save_peripheral_settings(int player_index, int peripheral_index,
                                   int peripheral_type,
                                   const char *peripheral_parameter)
{
    int *peripheral_type_ptr =
        peripheral_types_settings_map[player_index][peripheral_index];
    const char **peripheral_param_ptr =
        peripheral_params_settings_map[player_index][peripheral_index];

    assert(peripheral_type_ptr);
    assert(peripheral_param_ptr);

    *peripheral_type_ptr = peripheral_type;
    xemu_settings_set_string(
        peripheral_param_ptr,
        peripheral_parameter == NULL ? "" : peripheral_parameter);
}

void xemu_input_process_sdl_events(const SDL_Event *event)
{
    if (event->type == SDL_EVENT_GAMEPAD_ADDED) {
        DPRINTF("Controller Added: %d\n", event->gdevice.which);

        // Attempt to open the added controller
        SDL_Gamepad *sdl_con;
        sdl_con = SDL_OpenGamepad(event->gdevice.which);
        if (sdl_con == NULL) {
            DPRINTF("Could not open joystick %d as a Gamepad\n", event->gdevice.which);
            return;
        }

        // Success! Create a new node to track this controller and continue init
        ControllerState *new_con = malloc(sizeof(ControllerState));
        memset(new_con, 0, sizeof(ControllerState));
        new_con->type                 = INPUT_DEVICE_SDL_GAMEPAD;
        new_con->name                 = SDL_GetGamepadName(sdl_con);
        new_con->sdl_gamepad          = sdl_con;
        new_con->sdl_joystick         = SDL_GetGamepadJoystick(new_con->sdl_gamepad);
        new_con->sdl_joystick_id      = SDL_GetJoystickID(new_con->sdl_joystick);
        new_con->sdl_joystick_guid    = SDL_GetJoystickGUID(new_con->sdl_joystick);
        new_con->bound                = -1;
        new_con->peripheral_types[0] = PERIPHERAL_NONE;
        new_con->peripheral_types[1] = PERIPHERAL_NONE;
        new_con->peripherals[0] = NULL;
        new_con->peripherals[1] = NULL;

        char guid_buf[35] = { 0 };
        SDL_GUIDToString(new_con->sdl_joystick_guid, guid_buf, sizeof(guid_buf));
        DPRINTF("Opened %s (%s)\n", new_con->name, guid_buf);

        QTAILQ_INSERT_TAIL(&available_controllers, new_con, entry);
        xemu_input_bindings_reload_map(new_con);

        // Do not replace binding for a currently bound device. In the case that
        // the same GUID is specified multiple times, on different ports, allow
        // any available port to be bound.
        //
        // This can happen naturally with X360 wireless receiver, in which each
        // controller gets the same GUID (go figure). We cannot remember which
        // controller is which in this case, but we can try to tolerate this
        // situation by binding to any previously bound port with this GUID. The
        // upside in this case is that a person can use the same GUID on all
        // ports and just needs to bind to the receiver and never needs to hit
        // this dialog.


        // Attempt to re-bind to port previously bound to
        int port = 0;
        bool did_bind = false;
        while (!did_bind) {
            port = xemu_input_get_controller_default_bind_port(new_con, port);
            if (port < 0) {
                // No (additional) default mappings
                break;
            } else if (!xemu_input_get_bound(port)) {
                xemu_input_bind(port, new_con, 0);
                did_bind = true;
                break;
            } else {
                // Try again for another port
                port++;
            }
        }

        // Try to bind to any open port, and if so remember the binding
        if (!did_bind && g_config.input.auto_bind) {
            for (port = 0; port < 4; port++) {
                if (!xemu_input_get_bound(port)) {
                    xemu_input_bind(port, new_con, 1);
                    did_bind = true;
                    break;
                }
            }
        }

        if (did_bind) {
            char buf[128];
            snprintf(buf, sizeof(buf), "Connected '%s' to port %d", new_con->name, port+1);
            xemu_queue_notification(buf);
            xemu_input_rebind_xmu(port);
        }
    } else if (event->type == SDL_EVENT_GAMEPAD_REMOVED) {
        DPRINTF("Controller Removed: %d\n", event->gdevice.which);
        int handled = 0;
        ControllerState *iter, *next;
        QTAILQ_FOREACH_SAFE(iter, &available_controllers, entry, next) {
            if (iter->type != INPUT_DEVICE_SDL_GAMEPAD) continue;

            if (iter->sdl_joystick_id == event->gdevice.which) {
                DPRINTF("Device removed: %s\n", iter->name);

                // Disconnect
                if (iter->bound >= 0) {
                    // Queue a notification to inform user controller disconnected
                    // FIXME: Probably replace with a callback registration thing,
                    // but this works well enough for now.
                    char buf[128];
                    snprintf(buf, sizeof(buf), "Port %d disconnected", iter->bound+1);
                    xemu_queue_notification(buf);

                    // Unbind the controller, but don't save the unbinding in
                    // case the controller is reconnected
                    xemu_input_bind(iter->bound, NULL, 0);
                }

                // Unlink
                QTAILQ_REMOVE(&available_controllers, iter, entry);

                // Deallocate
                if (iter->sdl_gamepad) {
                    SDL_CloseGamepad(iter->sdl_gamepad);
                }

                for (int i = 0; i < 2; i++) {
                    if (iter->peripherals[i])
                        g_free(iter->peripherals[i]);
                }
                free(iter);

                handled = 1;
                break;
            }
        }
        if (!handled) {
            DPRINTF("Could not find handle for joystick instance\n");
        }
    } else if (event->type == SDL_EVENT_GAMEPAD_REMAPPED) {
        DPRINTF("Controller Remapped: %d\n", event->gdevice.which);
    } else if (event->type == SDL_EVENT_JOYSTICK_ADDED) {
        if (!SDL_IsGamepad(event->jdevice.which)) {
            const char *name = SDL_GetJoystickNameForID(event->jdevice.which);
            SDL_GUID guid = SDL_GetJoystickGUIDForID(event->jdevice.which);
            char guid_buf[35] = { 0 };
            SDL_GUIDToString(guid, guid_buf, sizeof(guid_buf));
            fprintf(
                stderr,
                "Input: Detected unmapped joystick '%s' (GUID: %s, ID: %u). "
                "This device is not recognized as a gamepad by SDL and "
                "requires an SDL gamepad mapping to work with xemu.\n",
                name ? name : "Unknown", guid_buf,
                (unsigned int)event->jdevice.which);
        }
    }
}

void xemu_input_update_controller(ControllerState *state)
{
    int64_t now = qemu_clock_get_us(QEMU_CLOCK_REALTIME);
    if (ABS(now - state->last_input_updated_ts) <
        XEMU_INPUT_MIN_INPUT_UPDATE_INTERVAL_US) {
        return;
    }

    /* XGrid: the shell supplies every bound pad when the input sink is on. */
    if (xgrid_input_enabled() && state->bound >= 0) {
        uint32_t xg_buttons = 0;
        int32_t xg_lx = 0, xg_ly = 0, xg_rx = 0, xg_ry = 0, xg_lt = 0, xg_rt = 0;
        if (xgrid_input_read(state->bound, &xg_buttons, &xg_lx, &xg_ly, &xg_rx,
                             &xg_ry, &xg_lt, &xg_rt)) {
            state->buttons = xg_buttons;
            memset(state->axis, 0, sizeof(state->axis));
            state->axis[CONTROLLER_AXIS_LSTICK_X] = xg_lx;
            state->axis[CONTROLLER_AXIS_LSTICK_Y] = xg_ly;
            state->axis[CONTROLLER_AXIS_RSTICK_X] = xg_rx;
            state->axis[CONTROLLER_AXIS_RSTICK_Y] = xg_ry;
            state->axis[CONTROLLER_AXIS_LTRIG] = xg_lt;
            state->axis[CONTROLLER_AXIS_RTRIG] = xg_rt;
            state->last_input_updated_ts = now;
            return;
        }
    }

    if (state->type == INPUT_DEVICE_SDL_KEYBOARD) {
        xemu_input_update_sdl_kbd_controller_state(state);
    } else if (state->type == INPUT_DEVICE_SDL_GAMEPAD) {
        xemu_input_update_sdl_controller_state(state);
    }

    state->last_input_updated_ts = qemu_clock_get_us(QEMU_CLOCK_REALTIME);
}

static bool chihiro_key_down(const bool *kbd, int scancode)
{
    return scancode > 0 && scancode < SDL_SCANCODE_COUNT && kbd[scancode];
}

/* The pad that plays the cabinet. A wheel that SDL exposes as a gamepad works
 * through its sticks/triggers too. */
static ControllerState *chihiro_gamepad(void)
{
    for (int i = 0; i < 4; i++) {
        ControllerState *pad = bound_controllers[i];
        if (pad && pad->type == INPUT_DEVICE_SDL_GAMEPAD) {
            return pad;
        }
    }

    ControllerState *iter;
    QTAILQ_FOREACH(iter, &available_controllers, entry) {
        if (iter->type == INPUT_DEVICE_SDL_GAMEPAD) {
            return iter;
        }
    }
    return NULL;
}

/* The second player's pad. Bound slot 1 goes first; an unbound second pad is
 * otherwise the first available one that is not player 1's. */
static ControllerState *chihiro_gamepad_p2(ControllerState *p1)
{
    for (int i = 1; i < 4; i++) {
        ControllerState *pad = bound_controllers[i];
        if (pad && pad != p1 && pad->type == INPUT_DEVICE_SDL_GAMEPAD) {
            return pad;
        }
    }

    ControllerState *iter;
    QTAILQ_FOREACH(iter, &available_controllers, entry) {
        if (iter != p1 && iter->type == INPUT_DEVICE_SDL_GAMEPAD) {
            return iter;
        }
    }
    return NULL;
}

static bool chihiro_pad_down(ControllerState *pad, uint16_t mask)
{
    return pad && (pad->buttons & mask);
}

static bool chihiro_pressed(bool down, bool *was_down)
{
    bool pressed = down && !*was_down;
    *was_down = down;
    return pressed;
}

static uint16_t chihiro_stick_u16(ControllerState *pad, int axis)
{
    if (!pad) {
        return 0x8000;
    }
    int value = pad->axis[axis];
    value = value < -32768 ? -32768 : value > 32767 ? 32767 : value;
    return (uint16_t)(0x8000 + value);
}

static uint16_t chihiro_trigger_u16(ControllerState *pad, int axis)
{
    if (!pad) {
        return 0;
    }
    int value = pad->axis[axis];
    value = value < 0 ? 0 : value > 32767 ? 32767 : value;
    return (uint16_t)(value * 2);
}

/* The renderer publishes where the guest picture sits inside the window. */
static float display_viewport_x;
static float display_viewport_y;
static float display_viewport_w;
static float display_viewport_h;

void xemu_input_set_display_viewport(float x, float y, float w, float h)
{
    display_viewport_x = x;
    display_viewport_y = y;
    display_viewport_w = w;
    display_viewport_h = h;
}

bool xemu_input_get_display_viewport(float *x, float *y, float *w, float *h)
{
    *x = display_viewport_x;
    *y = display_viewport_y;
    *w = display_viewport_w;
    *h = display_viewport_h;
    return display_viewport_w > 0 && display_viewport_h > 0;
}

/* A Chihiro binding in the 1000 range is 1000 plus SDL's button number:
 * 1001 = left, 1003 = right. SDL's button mask uses bit (button - 1). */
static bool chihiro_mouse_button(int binding, uint32_t buttons)
{
    int index = binding - 1001;
    return index >= 0 && index < 8 && (buttons & (1u << index)) != 0;
}

/* The gun games aim with the host pointer. The SDL window is the picture, so
 * the cursor position maps straight onto the JVS analog pair; leaving the
 * picture reports the off-screen position the games treat as a reload. */
static void chihiro_gun_aim(ChihiroJVSState *jvs)
{
    SDL_Window *window = SDL_GetMouseFocus();
    if (!window) {
        /* Outside the picture: the games read (0,0) as off-screen/reload. */
        jvs->analog[0] = 0;
        jvs->analog[1] = 0;
        return;
    }

    float mouse_x = 0.5f, mouse_y = 0.5f;
    int width = 0, height = 0;
    SDL_GetMouseState(&mouse_x, &mouse_y);
    SDL_GetWindowSize(window, &width, &height);

    float vx, vy, vw, vh;
    if (width <= 0 || height <= 0 ||
        !xemu_input_get_display_viewport(&vx, &vy, &vw, &vh)) {
        jvs->analog[0] = 0x8000;
        jvs->analog[1] = 0x8000;
        return;
    }

    float nx = (mouse_x / (float)width - vx) / vw;
    float ny = (mouse_y / (float)height - vy) / vh;
    if (nx < 0.0f || nx > 1.0f || ny < 0.0f || ny > 1.0f) {
        jvs->analog[0] = 0;
        jvs->analog[1] = 0;
    } else {
        jvs->analog[0] = (uint16_t)(nx * 0xFFFF);
        jvs->analog[1] = (uint16_t)(ny * 0xFFFF);
    }
}

/* Player 2's gun: the second pad's right stick or the aim keys move a
 * crosshair that stays where it is released. The stick is squared after its
 * deadzone so the middle of the picture is easier to hold. */
#define CHIHIRO_P2_AIM_DEADZONE       0.12f
#define CHIHIRO_P2_AIM_ACROSS_SECONDS 0.75f
#define CHIHIRO_P2_AIM_SAFE           0.01f

static float chihiro_p2_aim_axis(int16_t raw)
{
    float value = raw / 32767.0f;
    float magnitude = fabsf(value);
    if (magnitude < CHIHIRO_P2_AIM_DEADZONE) {
        return 0.0f;
    }

    magnitude = (magnitude - CHIHIRO_P2_AIM_DEADZONE) /
                (1.0f - CHIHIRO_P2_AIM_DEADZONE);
    if (magnitude > 1.0f) {
        magnitude = 1.0f;
    }
    return copysignf(magnitude * magnitude, value);
}

static float chihiro_p2_aim_x = 0.5f, chihiro_p2_aim_y = 0.5f;
static int64_t chihiro_p2_aim_last_ns;

static void chihiro_gun_aim_p2(ChihiroJVSState *jvs, ControllerState *pad,
                               const __typeof__(g_config.chihiro.jvs_p2) *cfg,
                               bool reload)
{
    const bool *kbd = SDL_GetKeyboardState(NULL);
    int64_t now = qemu_clock_get_ns(QEMU_CLOCK_REALTIME);
    float dt = chihiro_p2_aim_last_ns
        ? (now - chihiro_p2_aim_last_ns) / 1e9f : 0.0f;
    chihiro_p2_aim_last_ns = now;
    if (dt < 0.0f) {
        dt = 0.0f;
    } else if (dt > 0.05f) {
        dt = 0.05f;
    }

    float dx = 0.0f, dy = 0.0f;
    if (pad) {
        dx = chihiro_p2_aim_axis(pad->axis[CONTROLLER_AXIS_RSTICK_X]);
        dy = -chihiro_p2_aim_axis(pad->axis[CONTROLLER_AXIS_RSTICK_Y]);
    }
    bool key_left = chihiro_key_down(kbd, cfg->aim_left);
    bool key_right = chihiro_key_down(kbd, cfg->aim_right);
    bool key_up = chihiro_key_down(kbd, cfg->aim_up);
    bool key_down = chihiro_key_down(kbd, cfg->aim_down);
    if (key_left) {
        dx -= 1.0f;
    }
    if (key_right) {
        dx += 1.0f;
    }
    if (key_up) {
        dy -= 1.0f;
    }
    if (key_down) {
        dy += 1.0f;
    }
    dx = dx < -1.0f ? -1.0f : dx > 1.0f ? 1.0f : dx;
    dy = dy < -1.0f ? -1.0f : dy > 1.0f ? 1.0f : dy;

    int speed_pct = cfg->aim_speed;
    speed_pct = speed_pct < 0 ? 0 : speed_pct > 400 ? 400 : speed_pct;
    float speed = speed_pct / 100.0f / CHIHIRO_P2_AIM_ACROSS_SECONDS * dt;
    chihiro_p2_aim_x += dx * speed;
    /* The picture is 4:3, so the same speed on screen up and down is 4/3 of
     * the fraction. Screen down is positive; xemu's Y axis is up. */
    chihiro_p2_aim_y += dy * speed * 4.0f / 3.0f;

    if (chihiro_p2_aim_x < CHIHIRO_P2_AIM_SAFE) {
        chihiro_p2_aim_x = CHIHIRO_P2_AIM_SAFE;
    } else if (chihiro_p2_aim_x > 1.0f - CHIHIRO_P2_AIM_SAFE) {
        chihiro_p2_aim_x = 1.0f - CHIHIRO_P2_AIM_SAFE;
    }
    if (chihiro_p2_aim_y < CHIHIRO_P2_AIM_SAFE) {
        chihiro_p2_aim_y = CHIHIRO_P2_AIM_SAFE;
    } else if (chihiro_p2_aim_y > 1.0f - CHIHIRO_P2_AIM_SAFE) {
        chihiro_p2_aim_y = 1.0f - CHIHIRO_P2_AIM_SAFE;
    }

    if (reload) {
        jvs->analog[2] = 0;
        jvs->analog[3] = 0;
    } else {
        jvs->analog[2] = (uint16_t)(chihiro_p2_aim_x * 0xFFFF);
        jvs->analog[3] = (uint16_t)(chihiro_p2_aim_y * 0xFFFF);
    }
}

bool xemu_input_chihiro_gun_active(void)
{
    if (!xemu_chihiro_mode()) {
        return false;
    }

    int profile = chihiro_detected_game_profile();
    return profile == CONFIG_CHIHIRO_JVS_PROFILE_HOTD3 ||
           profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3 ||
           profile == CONFIG_CHIHIRO_JVS_PROFILE_GS;
}

/* Steering-wheel force feedback. The drive boards describe a spring, a
 * damper and a directional push; SDL renders them as condition, constant and
 * periodic effects. A wheel that supports spring or damper forces is
 * preferred; without one the event channel stays on the first gamepad's
 * rumble motors. */
typedef struct {
    bool active;
    int  spring;    /* 0-32767 */
    int  damper;    /* 0-32767 */
    int  constant;  /* -32767-32767 */
    int  sine;      /* 0-32767 */
} ChihiroForceState;

typedef struct {
    SDL_Haptic        *haptic;
    SDL_HapticID       id;
    SDL_HapticEffectID constant_id;
    SDL_HapticEffectID spring_id;
    SDL_HapticEffectID damper_id;
    SDL_HapticEffectID sine_id;
    int                last_constant;
    int                last_spring;
    int                last_damper;
    int                last_sine;
    int64_t            last_apply_ns;
    int64_t            next_try_ms;
    bool               no_wheel_logged;
} ChihiroWheelFFB;

static ChihiroWheelFFB chihiro_wheel = {
    .constant_id = -1,
    .spring_id = -1,
    .damper_id = -1,
    .sine_id = -1,
};

static int chihiro_wheel_score(Uint32 features)
{
    if (!(features & SDL_HAPTIC_SPRING) &&
        !((features & SDL_HAPTIC_DAMPER) &&
          (features & SDL_HAPTIC_CONSTANT))) {
        return -1;
    }

    int score = 0;
    if (features & SDL_HAPTIC_SPRING) score += 4;
    if (features & SDL_HAPTIC_DAMPER) score += 2;
    if (features & SDL_HAPTIC_CONSTANT) score += 1;
    if (features & SDL_HAPTIC_SINE) score += 1;
    return score;
}

static SDL_HapticEffectID chihiro_wheel_create(const SDL_HapticEffect *effect)
{
    if (!SDL_HapticEffectSupported(chihiro_wheel.haptic, effect)) {
        return -1;
    }

    SDL_HapticEffectID id = SDL_CreateHapticEffect(chihiro_wheel.haptic, effect);
    if (id < 0) {
        return -1;
    }
    if (!SDL_RunHapticEffect(chihiro_wheel.haptic, id, 1)) {
        SDL_DestroyHapticEffect(chihiro_wheel.haptic, id);
        return -1;
    }
    return id;
}

static void chihiro_wheel_close(void)
{
    if (!chihiro_wheel.haptic) {
        return;
    }

    if (chihiro_wheel.constant_id >= 0) {
        SDL_DestroyHapticEffect(chihiro_wheel.haptic, chihiro_wheel.constant_id);
    }
    if (chihiro_wheel.spring_id >= 0) {
        SDL_DestroyHapticEffect(chihiro_wheel.haptic, chihiro_wheel.spring_id);
    }
    if (chihiro_wheel.damper_id >= 0) {
        SDL_DestroyHapticEffect(chihiro_wheel.haptic, chihiro_wheel.damper_id);
    }
    if (chihiro_wheel.sine_id >= 0) {
        SDL_DestroyHapticEffect(chihiro_wheel.haptic, chihiro_wheel.sine_id);
    }
    SDL_CloseHaptic(chihiro_wheel.haptic);
    memset(&chihiro_wheel, 0, sizeof(chihiro_wheel));
    chihiro_wheel.constant_id = -1;
    chihiro_wheel.spring_id = -1;
    chihiro_wheel.damper_id = -1;
    chihiro_wheel.sine_id = -1;
}

static void chihiro_wheel_open(void)
{
    int count = 0;
    SDL_HapticID *ids = SDL_GetHaptics(&count);
    SDL_Haptic *best = NULL;
    SDL_HapticID best_id = 0;
    int best_score = -1;

    if (!ids) {
        return;
    }

    for (int i = 0; i < count; i++) {
        SDL_Haptic *haptic = SDL_OpenHaptic(ids[i]);
        if (!haptic) {
            continue;
        }

        int score = chihiro_wheel_score(SDL_GetHapticFeatures(haptic));
        if (score > best_score) {
            if (best) {
                SDL_CloseHaptic(best);
            }
            best = haptic;
            best_id = ids[i];
            best_score = score;
        } else {
            SDL_CloseHaptic(haptic);
        }
    }
    SDL_free(ids);

    if (!best) {
        if (!chihiro_wheel.no_wheel_logged) {
            CHIHIRO_LOGF(FFB, "wheel haptics: no force-feedback wheel found\n");
            chihiro_wheel.no_wheel_logged = true;
        }
        return;
    }

    chihiro_wheel.haptic = best;
    chihiro_wheel.id = best_id;
    chihiro_wheel.last_apply_ns = 0;
    chihiro_wheel.no_wheel_logged = false;
    const char *name = SDL_GetHapticName(best);

    Uint32 features = SDL_GetHapticFeatures(best);
    if (features & SDL_HAPTIC_GAIN) {
        SDL_SetHapticGain(best, 100);
    }

    SDL_HapticEffect effect;

    if (features & SDL_HAPTIC_CONSTANT) {
        memset(&effect, 0, sizeof(effect));
        effect.type = SDL_HAPTIC_CONSTANT;
        effect.constant.direction.type = SDL_HAPTIC_CARTESIAN;
        effect.constant.direction.dir[0] = 1;
        effect.constant.length = SDL_HAPTIC_INFINITY;
        chihiro_wheel.constant_id = chihiro_wheel_create(&effect);
    }

    if (features & SDL_HAPTIC_SPRING) {
        memset(&effect, 0, sizeof(effect));
        effect.type = SDL_HAPTIC_SPRING;
        effect.condition.direction.type = SDL_HAPTIC_CARTESIAN;
        effect.condition.direction.dir[0] = 1;
        effect.condition.length = SDL_HAPTIC_INFINITY;
        effect.condition.right_sat[0] = 0xFFFF;
        effect.condition.left_sat[0] = 0xFFFF;
        chihiro_wheel.spring_id = chihiro_wheel_create(&effect);
    }

    if (features & SDL_HAPTIC_DAMPER) {
        memset(&effect, 0, sizeof(effect));
        effect.type = SDL_HAPTIC_DAMPER;
        effect.condition.direction.type = SDL_HAPTIC_CARTESIAN;
        effect.condition.direction.dir[0] = 1;
        effect.condition.length = SDL_HAPTIC_INFINITY;
        effect.condition.right_sat[0] = 0xFFFF;
        effect.condition.left_sat[0] = 0xFFFF;
        chihiro_wheel.damper_id = chihiro_wheel_create(&effect);
    }

    if (features & SDL_HAPTIC_SINE) {
        memset(&effect, 0, sizeof(effect));
        effect.type = SDL_HAPTIC_SINE;
        effect.periodic.direction.type = SDL_HAPTIC_CARTESIAN;
        effect.periodic.direction.dir[0] = 1;
        effect.periodic.length = SDL_HAPTIC_INFINITY;
        effect.periodic.period = 25;
        chihiro_wheel.sine_id = chihiro_wheel_create(&effect);
    }

    if (chihiro_wheel.constant_id < 0 && chihiro_wheel.spring_id < 0 &&
        chihiro_wheel.damper_id < 0) {
        CHIHIRO_LOGF(FFB, "wheel haptics: %s has no usable effect\n",
                     name ? name : "unnamed");
        chihiro_wheel_close();
        return;
    }

    CHIHIRO_LOGF(FFB, "wheel haptics: %s (constant %d, spring %d, "
                 "damper %d, sine %d)\n", name ? name : "unnamed",
                 chihiro_wheel.constant_id, chihiro_wheel.spring_id,
                 chihiro_wheel.damper_id, chihiro_wheel.sine_id);
}

static bool chihiro_wheel_ensure(void)
{
    if (!g_config.chihiro.settings.force_feedback) {
        chihiro_wheel_close();
        return false;
    }

    if (chihiro_wheel.haptic) {
        if (SDL_GetHapticName(chihiro_wheel.haptic)) {
            return true;
        }
        chihiro_wheel_close();
    }

    int64_t now = qemu_clock_get_ms(QEMU_CLOCK_REALTIME);
    if (now < chihiro_wheel.next_try_ms) {
        return false;
    }
    chihiro_wheel.next_try_ms = now + 2000;
    chihiro_wheel_open();
    return chihiro_wheel.haptic != NULL;
}

static int chihiro_scale_force(int value, int max, int strength)
{
    if (value <= 0 || strength <= 0) {
        return 0;
    }

    int64_t scaled = (int64_t)value * 32767 / max;
    scaled = scaled * strength / 100;
    return scaled > 32767 ? 32767 : (int)scaled;
}

static int chihiro_wheel_weight(void)
{
    int weight = g_config.chihiro.settings.wheel_weight;
    return weight < 0 ? 0 : weight > 100 ? 100 : weight;
}

static void chihiro_force_from_sega(const DriveBoardFFB *ffb,
                                    uint16_t rumble, int strength,
                                    ChihiroForceState *out)
{
    int spring = chihiro_scale_force(ffb->centering_power, 0xFF, strength);
    if (spring == 0 && g_config.chihiro.settings.wheel_autocenter) {
        spring = chihiro_scale_force(
            g_config.chihiro.settings.wheel_autocenter_strength, 100, strength);
    }

    int damper = chihiro_scale_force(ffb->friction_power, 0xFF, strength);
    damper = damper * chihiro_wheel_weight() / 100;

    int move = chihiro_scale_force(ffb->event_power, 0x7F, strength);
    int dir = ffb->event_dir;
    int push = chihiro_scale_force(ffb->movement_power, 0x7F, strength);
    if (push > move) {
        move = push;
        dir = ffb->movement_dir;
    }
    int constant = dir ? -move : move;
    if (g_config.chihiro.settings.ffb_invert) {
        constant = -constant;
    }

    out->active = ffb->active;
    out->spring = spring;
    out->damper = damper;
    out->constant = constant;
    out->sine = chihiro_scale_force(rumble, 0xFFFF, strength);
}

static void chihiro_force_from_v257(int spring, int damping, int torque,
                                    int strength, ChihiroForceState *out)
{
    int auto_spring = chihiro_scale_force(spring, 0x7F, strength);
    if (auto_spring == 0 && g_config.chihiro.settings.wheel_autocenter) {
        auto_spring = chihiro_scale_force(
            g_config.chihiro.settings.wheel_autocenter_strength, 100, strength);
    }

    int damper = chihiro_scale_force(damping, 0x7F, strength);
    damper = damper * chihiro_wheel_weight() / 100;

    int constant = chihiro_scale_force(abs(torque), 0x7F, strength);
    if (torque < 0) {
        constant = -constant;
    }
    if (g_config.chihiro.settings.ffb_invert) {
        constant = -constant;
    }

    out->active = spring || damping || torque;
    out->spring = auto_spring;
    out->damper = damper;
    out->constant = constant;
    out->sine = 0;
}

static void chihiro_wheel_set_condition(SDL_HapticEffectID id,
                                        SDL_HapticEffectType type,
                                        int value, int *last)
{
    if (id < 0 || value == *last) {
        return;
    }

    SDL_HapticEffect effect;
    memset(&effect, 0, sizeof(effect));
    effect.type = type;
    effect.condition.direction.type = SDL_HAPTIC_CARTESIAN;
    effect.condition.direction.dir[0] = 1;
    effect.condition.length = SDL_HAPTIC_INFINITY;
    effect.condition.right_sat[0] = 0xFFFF;
    effect.condition.left_sat[0] = 0xFFFF;
    effect.condition.right_coeff[0] = (Sint16)value;
    effect.condition.left_coeff[0] = (Sint16)value;

    if (SDL_UpdateHapticEffect(chihiro_wheel.haptic, id, &effect)) {
        *last = value;
    }
}

static void chihiro_wheel_set_constant(int value)
{
    if (chihiro_wheel.constant_id < 0 ||
        value == chihiro_wheel.last_constant) {
        return;
    }

    SDL_HapticEffect effect;
    memset(&effect, 0, sizeof(effect));
    effect.type = SDL_HAPTIC_CONSTANT;
    effect.constant.direction.type = SDL_HAPTIC_CARTESIAN;
    effect.constant.direction.dir[0] = value < 0 ? -1 : 1;
    effect.constant.length = SDL_HAPTIC_INFINITY;
    effect.constant.level = (Sint16)(value < 0 ? -value : value);

    if (SDL_UpdateHapticEffect(chihiro_wheel.haptic, chihiro_wheel.constant_id,
                               &effect)) {
        chihiro_wheel.last_constant = value;
    }
}

static void chihiro_wheel_set_sine(int value)
{
    if (chihiro_wheel.sine_id < 0 || value == chihiro_wheel.last_sine) {
        return;
    }

    SDL_HapticEffect effect;
    memset(&effect, 0, sizeof(effect));
    effect.type = SDL_HAPTIC_SINE;
    effect.periodic.direction.type = SDL_HAPTIC_CARTESIAN;
    effect.periodic.direction.dir[0] = 1;
    effect.periodic.length = SDL_HAPTIC_INFINITY;
    effect.periodic.period = 25;
    effect.periodic.magnitude = (Sint16)value;

    if (SDL_UpdateHapticEffect(chihiro_wheel.haptic, chihiro_wheel.sine_id,
                               &effect)) {
        chihiro_wheel.last_sine = value;
    }
}

static void chihiro_wheel_apply(const ChihiroForceState *forces)
{
    if (!chihiro_wheel.haptic) {
        return;
    }

    int64_t now = qemu_clock_get_ns(QEMU_CLOCK_REALTIME);
    if (chihiro_wheel.last_apply_ns &&
        now - chihiro_wheel.last_apply_ns < 10000000) {
        return;
    }
    chihiro_wheel.last_apply_ns = now;

    chihiro_wheel_set_condition(chihiro_wheel.spring_id, SDL_HAPTIC_SPRING,
                                forces->spring, &chihiro_wheel.last_spring);
    chihiro_wheel_set_condition(chihiro_wheel.damper_id, SDL_HAPTIC_DAMPER,
                                forces->damper, &chihiro_wheel.last_damper);
    chihiro_wheel_set_constant(forces->constant);
    chihiro_wheel_set_sine(forces->sine);
}

/* A wheel gets the drive board's persistent forces; a gamepad only renders
 * the event channel, because it cannot reproduce centering or weight. */
static void chihiro_ffb_update(ControllerState *pad)
{
    static uint16_t last_rumble;

    if (!g_config.chihiro.settings.force_feedback) {
        if (last_rumble && pad && pad->sdl_gamepad) {
            SDL_RumbleGamepad(pad->sdl_gamepad, 0, 0, 0);
        }
        last_rumble = 0;
        chihiro_wheel_close();
        return;
    }

    int strength = g_config.chihiro.settings.ffb_strength;
    strength = strength < 0 ? 0 : strength > 200 ? 200 : strength;

    ChihiroForceState forces = { 0 };
    uint16_t sega_rumble = 0;
    bool v257 = false;
    int v257_torque = 0;

    if (chihiro_driveboard_global) {
        DriveBoardFFB ffb;
        driveboard_get_ffb(chihiro_driveboard_global, &ffb);
        sega_rumble = driveboard_get_rumble(chihiro_driveboard_global);
        chihiro_force_from_sega(&ffb, sega_rumble, strength, &forces);
    } else if (chihiro_v257_global) {
        int target, spring, damping, torque;
        v257_drive(chihiro_v257_global, &target, &spring, &damping, &torque);
        v257 = true;
        v257_torque = torque;
        chihiro_force_from_v257(spring, damping, torque, strength, &forces);
    } else if (g_config.chihiro.settings.wheel_autocenter) {
        forces.spring = chihiro_scale_force(
            g_config.chihiro.settings.wheel_autocenter_strength, 100, strength);
    }

    if (chihiro_wheel_ensure()) {
        last_rumble = 0;
        chihiro_wheel_apply(&forces);
        return;
    }

    uint16_t rumble = sega_rumble;
    if (v257) {
        int magnitude = v257_torque < 0 ? -v257_torque : v257_torque;
        rumble = (uint16_t)((uint32_t)magnitude * 0xFFFFu / 0x7F);
    }
    rumble = (uint16_t)((uint32_t)rumble * strength / 100u);

    if (rumble != last_rumble) {
        CHIHIRO_LOGF(FFB, "gamepad rumble %04X (pad %s)\n", rumble,
                     pad ? "yes" : "no");
        if (pad && pad->sdl_gamepad) {
            SDL_RumbleGamepad(pad->sdl_gamepad, rumble, rumble, 250);
        }
        last_rumble = rumble;
    }
}

/* The cabinet's JVS state: player 1's keyboard, mouse, first pad and wheel,
 * and player 2's gun, card and switches on a two-player gun cabinet. */
static void xemu_input_update_chihiro(void)
{
    if (!chihiro_jvs_global || !xemu_chihiro_mode()) {
        chihiro_wheel_close();
        return;
    }

    ChihiroJVSState *jvs = chihiro_jvs_global;
    const bool *kbd = SDL_GetKeyboardState(NULL);
    uint32_t mouse_buttons = SDL_GetMouseState(NULL, NULL);
    ControllerState *pad = chihiro_gamepad();
    ControllerState *pad2 = chihiro_gamepad_p2(pad);
    const __typeof__(g_config.chihiro.jvs) *jvs_cfg = &g_config.chihiro.jvs;
    const __typeof__(g_config.chihiro.jvs_p2) *p2_cfg =
        &g_config.chihiro.jvs_p2;
    int profile = chihiro_detected_game_profile();
    if (profile < 0) {
        profile = jvs_cfg->profile;
    }

    chihiro_ffb_update(pad);

    uint8_t sw0 = 0;
    uint8_t sw1 = 0;
    uint8_t p2_sw0 = 0;
    uint8_t p2_sw1 = 0;
    jvs->player_switches[1][0] = 0;
    jvs->player_switches[1][1] = 0;
    for (int i = 0; i < JVS_MAX_ANALOG; i++) {
        jvs->analog[i] = 0x8000;
    }

    switch (profile) {
    case CONFIG_CHIHIRO_JVS_PROFILE_HOTD3:
    case CONFIG_CHIHIRO_JVS_PROFILE_VC3:
    case CONFIG_CHIHIRO_JVS_PROFILE_GS: {
        int trigger = profile == CONFIG_CHIHIRO_JVS_PROFILE_HOTD3
            ? jvs_cfg->hotd3.trigger
            : profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
                ? jvs_cfg->vc3.trigger : jvs_cfg->gs.trigger;
        int body = profile == CONFIG_CHIHIRO_JVS_PROFILE_HOTD3
            ? jvs_cfg->hotd3.body_button
            : profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
                ? jvs_cfg->vc3.body_button : jvs_cfg->gs.body_button;
        int extra = profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
            ? jvs_cfg->vc3.pedal : jvs_cfg->gs.change;

        if (chihiro_key_down(kbd, trigger) ||
            chihiro_mouse_button(trigger, mouse_buttons) ||
            chihiro_trigger_u16(pad, CONTROLLER_AXIS_RTRIG) > 0x4000) {
            sw0 |= 0x02;
        }
        if (chihiro_key_down(kbd, body) ||
            chihiro_mouse_button(body, mouse_buttons) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_A)) {
            sw1 |= 0x80;
        }
        if (chihiro_key_down(kbd, extra) ||
            chihiro_mouse_button(extra, mouse_buttons) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_B)) {
            sw1 |= 0x40;
        }
        if (profile == CONFIG_CHIHIRO_JVS_PROFILE_GS) {
            static bool card_was_down;
            bool card = chihiro_key_down(kbd, jvs_cfg->card_in) ||
                        chihiro_pad_down(pad, CONTROLLER_BUTTON_X);
            if (chihiro_pressed(card, &card_was_down)) {
                chihiro_hw210_card_key(0);
            }
            if (chihiro_card_reader_present(0)) {
                sw1 |= 0x20;
            }
        }
        chihiro_gun_aim(jvs);

        int p2_trigger = profile == CONFIG_CHIHIRO_JVS_PROFILE_HOTD3
            ? p2_cfg->hotd3_trigger
            : profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
                ? p2_cfg->vc3_trigger : p2_cfg->gs_trigger;
        int p2_body = profile == CONFIG_CHIHIRO_JVS_PROFILE_HOTD3
            ? p2_cfg->hotd3_body_button
            : profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
                ? p2_cfg->vc3_body_button : p2_cfg->gs_body_button;
        int p2_extra = profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
            ? p2_cfg->vc3_pedal : p2_cfg->gs_change;
        int p2_reload = profile == CONFIG_CHIHIRO_JVS_PROFILE_VC3
            ? p2_cfg->vc3_reload
            : profile == CONFIG_CHIHIRO_JVS_PROFILE_GS
                ? p2_cfg->gs_reload : 0;

        if (chihiro_key_down(kbd, p2_trigger) ||
            chihiro_mouse_button(p2_trigger, mouse_buttons) ||
            chihiro_trigger_u16(pad2, CONTROLLER_AXIS_RTRIG) > 0x4000) {
            p2_sw0 |= 0x02;
        }
        if (chihiro_key_down(kbd, p2_body) ||
            chihiro_mouse_button(p2_body, mouse_buttons) ||
            chihiro_pad_down(pad2, CONTROLLER_BUTTON_A)) {
            p2_sw1 |= 0x80;
        }
        if (chihiro_key_down(kbd, p2_extra) ||
            chihiro_mouse_button(p2_extra, mouse_buttons) ||
            chihiro_pad_down(pad2, CONTROLLER_BUTTON_B)) {
            p2_sw1 |= 0x40;
        }

        bool p2_reload_down = chihiro_key_down(kbd, p2_reload) ||
            chihiro_trigger_u16(pad2, CONTROLLER_AXIS_LTRIG) > 0x4000;
        chihiro_gun_aim_p2(jvs, pad2, p2_cfg, p2_reload_down);

        if (profile == CONFIG_CHIHIRO_JVS_PROFILE_GS) {
            static bool card_was_down_p2;
            bool card = chihiro_key_down(kbd, p2_cfg->card_in) ||
                        chihiro_pad_down(pad2, CONTROLLER_BUTTON_X);
            if (chihiro_pressed(card, &card_was_down_p2)) {
                chihiro_hw210_card_key(1);
            }
            if (chihiro_card_reader_present(1)) {
                p2_sw1 |= 0x20;
            }
        }
        break;
    }
    case CONFIG_CHIHIRO_JVS_PROFILE_CTX:
    case CONFIG_CHIHIRO_JVS_PROFILE_OR2:
    case CONFIG_CHIHIRO_JVS_PROFILE_WMMT2: {
        bool left = chihiro_key_down(kbd, jvs_cfg->steer_left);
        bool right = chihiro_key_down(kbd, jvs_cfg->steer_right);
        jvs->analog[0] = chihiro_stick_u16(pad, CONTROLLER_AXIS_LSTICK_X);
        if (left) {
            jvs->analog[0] = 0;
        } else if (right) {
            jvs->analog[0] = 0xFFFF;
        }
        jvs->analog[1] = chihiro_key_down(kbd, jvs_cfg->gas)
            ? 0xFFFF : chihiro_trigger_u16(pad, CONTROLLER_AXIS_RTRIG);
        jvs->analog[2] = chihiro_key_down(kbd, jvs_cfg->brake)
            ? 0xFFFF : chihiro_trigger_u16(pad, CONTROLLER_AXIS_LTRIG);

        if (profile == CONFIG_CHIHIRO_JVS_PROFILE_CTX) {
            if (chihiro_key_down(kbd, jvs_cfg->ctx.drive_gear) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_X)) {
                sw0 |= 0x20;
            }
            if (chihiro_key_down(kbd, jvs_cfg->ctx.reverse) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_B)) {
                sw0 |= 0x10;
            }
            if (chihiro_key_down(kbd, jvs_cfg->ctx.jump) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_A)) {
                sw0 |= 0x02;
            }
        } else if (profile == CONFIG_CHIHIRO_JVS_PROFILE_OR2) {
            if (chihiro_key_down(kbd, jvs_cfg->or2.view_change) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_Y)) {
                sw0 |= 0x10;
            }
        } else {
            if (chihiro_key_down(kbd, jvs_cfg->wmmt2.view_change) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_Y)) {
                sw1 |= 0x02;
            }
            if (chihiro_key_down(kbd, jvs_cfg->wmmt2.intrude_change) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_B)) {
                sw1 |= 0x01;
            }
        }
        bool gear_up = chihiro_key_down(kbd, jvs_cfg->gear_up) ||
                       chihiro_pad_down(pad, CONTROLLER_BUTTON_DPAD_UP);
        bool gear_down = chihiro_key_down(kbd, jvs_cfg->gear_down) ||
                         chihiro_pad_down(pad, CONTROLLER_BUTTON_DPAD_DOWN);
        if (gear_up) {
            sw1 |= 0x80;
        }
        if (gear_down) {
            sw1 |= 0x40;
        }
        if (profile == CONFIG_CHIHIRO_JVS_PROFILE_OR2) {
            /* The desktop fork found OutRun 2's sequential shifter on the
             * second player's up/down pins; keep the player-1 byte too for
             * the cabinets that wire it there. */
            if (gear_up) {
                p2_sw0 |= 0x20;
            }
            if (gear_down) {
                p2_sw0 |= 0x10;
            }
        }
        if (profile == CONFIG_CHIHIRO_JVS_PROFILE_WMMT2) {
            static bool card_was_down;
            bool card = chihiro_key_down(kbd, jvs_cfg->card_in) ||
                        chihiro_pad_down(pad, CONTROLLER_BUTTON_X);
            if (chihiro_pressed(card, &card_was_down)) {
                chihiro_crp1231_card_key();
            }
        }
        break;
    }
    case CONFIG_CHIHIRO_JVS_PROFILE_OK: {
        jvs->analog[1] = chihiro_stick_u16(pad, CONTROLLER_AXIS_LSTICK_X);
        if (chihiro_key_down(kbd, jvs_cfg->ok.swing_left)) {
            jvs->analog[1] = 0;
        } else if (chihiro_key_down(kbd, jvs_cfg->ok.swing_right)) {
            jvs->analog[1] = 0xFFFF;
        }
        if (chihiro_key_down(kbd, jvs_cfg->ok.board_front) ||
            chihiro_trigger_u16(pad, CONTROLLER_AXIS_RTRIG) > 0x4000) {
            sw0 |= 0x20;
        }
        if (chihiro_key_down(kbd, jvs_cfg->ok.board_rear) ||
            chihiro_trigger_u16(pad, CONTROLLER_AXIS_LTRIG) > 0x4000) {
            sw0 |= 0x10;
        }
        if (chihiro_key_down(kbd, jvs_cfg->ok.left_grab) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_A)) {
            sw0 |= 0x02;
        }
        if (chihiro_key_down(kbd, jvs_cfg->ok.right_grab) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_B)) {
            sw0 |= 0x01;
        }
        break;
    }
    case CONFIG_CHIHIRO_JVS_PROFILE_GUNDAM: {
        int16_t lx = pad ? pad->axis[CONTROLLER_AXIS_LSTICK_X] : 0;
        int16_t ly = pad ? pad->axis[CONTROLLER_AXIS_LSTICK_Y] : 0;
        int16_t rx = pad ? pad->axis[CONTROLLER_AXIS_RSTICK_X] : 0;
        int16_t ry = pad ? pad->axis[CONTROLLER_AXIS_RSTICK_Y] : 0;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_up) || ly > 0x4000) sw0 |= 0x20;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_down) || ly < -0x4000) sw0 |= 0x10;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_left) || lx < -0x4000) sw0 |= 0x08;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_right) || lx > 0x4000) sw0 |= 0x04;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_trigger) ||
            chihiro_trigger_u16(pad, CONTROLLER_AXIS_RTRIG) > 0x4000) sw0 |= 0x02;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.l_button) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_A)) sw0 |= 0x01;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_up) || ry > 0x4000) sw1 |= 0x20;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_down) || ry < -0x4000) sw1 |= 0x10;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_left) || rx < -0x4000) sw1 |= 0x08;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_right) || rx > 0x4000) sw1 |= 0x04;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_trigger) ||
            chihiro_trigger_u16(pad, CONTROLLER_AXIS_LTRIG) > 0x4000) sw1 |= 0x02;
        if (chihiro_key_down(kbd, jvs_cfg->gundam.r_button) ||
            chihiro_pad_down(pad, CONTROLLER_BUTTON_B)) sw1 |= 0x01;
        jvs->analog[1] = chihiro_key_down(kbd, jvs_cfg->gundam.pedal)
            ? 0xFFFF : chihiro_trigger_u16(pad, CONTROLLER_AXIS_LTRIG);
        {
            static bool card_was_down;
            bool card = chihiro_key_down(kbd, jvs_cfg->card_in) ||
                        chihiro_pad_down(pad, CONTROLLER_BUTTON_X);
            if (chihiro_pressed(card, &card_was_down)) {
                chihiro_hw210_card_key(0);
            }
            if (chihiro_card_reader_present(0)) {
                sw1 |= 0x80;
            }
        }
        break;
    }
    default:
        break;
    }

    if (chihiro_key_down(kbd, jvs_cfg->start) ||
        chihiro_pad_down(pad, CONTROLLER_BUTTON_START)) {
        sw0 |= 0x80;
    }
    if (chihiro_key_down(kbd, jvs_cfg->service) ||
        chihiro_pad_down(pad, CONTROLLER_BUTTON_LSTICK)) {
        sw0 |= 0x40;
    }
    if (chihiro_key_down(kbd, p2_cfg->start) ||
        chihiro_pad_down(pad2, CONTROLLER_BUTTON_START)) {
        p2_sw0 |= 0x80;
    }

    jvs->player_switches[0][0] = sw0;
    jvs->player_switches[0][1] = sw1;
    jvs->player_switches[1][0] = p2_sw0;
    jvs->player_switches[1][1] = p2_sw1;
    jvs->system_switches =
        (chihiro_key_down(kbd, jvs_cfg->test) ||
         chihiro_pad_down(pad, CONTROLLER_BUTTON_RSTICK)) ? 0x80 : 0;

    static bool coin_was_down;
    bool coin = chihiro_key_down(kbd, jvs_cfg->coin) ||
                chihiro_pad_down(pad, CONTROLLER_BUTTON_BACK);
    if (coin && !coin_was_down) {
        jvs->coin_count[0]++;
    }
    coin_was_down = coin;

    static bool coin_was_down_p2;
    bool coin_p2 = chihiro_key_down(kbd, p2_cfg->coin) ||
                   chihiro_pad_down(pad2, CONTROLLER_BUTTON_BACK);
    if (coin_p2 && !coin_was_down_p2) {
        jvs->coin_count[1]++;
    }
    coin_was_down_p2 = coin_p2;
}

void xemu_input_update_controllers(void)
{
    ControllerState *iter;
    QTAILQ_FOREACH(iter, &available_controllers, entry) {
        xemu_input_update_controller(iter);
    }
    QTAILQ_FOREACH(iter, &available_controllers, entry) {
        xemu_input_update_rumble(iter);
    }
    xemu_input_update_chihiro();
}

void xemu_input_update_sdl_kbd_controller_state(ControllerState *state)
{
    state->buttons = 0;
    memset(state->axis, 0, sizeof(state->axis));

    const bool *kbd = SDL_GetKeyboardState(NULL);

#define KBD_STATE(btn) \
    (kbd[g_config.input.keyboard_controller_scancode_map.btn])

    state->buttons |= KBD_STATE(a) << 0;
    state->buttons |= KBD_STATE(b) << 1;
    state->buttons |= KBD_STATE(x) << 2;
    state->buttons |= KBD_STATE(y) << 3;
    state->buttons |= KBD_STATE(dpad_left) << 4;
    state->buttons |= KBD_STATE(dpad_up) << 5;
    state->buttons |= KBD_STATE(dpad_right) << 6;
    state->buttons |= KBD_STATE(dpad_down) << 7;
    state->buttons |= KBD_STATE(back) << 8;
    state->buttons |= KBD_STATE(start) << 9;
    state->buttons |= KBD_STATE(white) << 10;
    state->buttons |= KBD_STATE(black) << 11;
    state->buttons |= KBD_STATE(lstick_btn) << 12;
    state->buttons |= KBD_STATE(rstick_btn) << 13;
    state->buttons |= KBD_STATE(guide) << 14;

    if (KBD_STATE(lstick_up))
        state->axis[CONTROLLER_AXIS_LSTICK_Y] = 32767;
    if (KBD_STATE(lstick_left))
        state->axis[CONTROLLER_AXIS_LSTICK_X] = -32768;
    if (KBD_STATE(lstick_right))
        state->axis[CONTROLLER_AXIS_LSTICK_X] = 32767;
    if (KBD_STATE(lstick_down))
        state->axis[CONTROLLER_AXIS_LSTICK_Y] = -32768;
    if (KBD_STATE(ltrigger))
        state->axis[CONTROLLER_AXIS_LTRIG] = 32767;

    if (KBD_STATE(rstick_up))
        state->axis[CONTROLLER_AXIS_RSTICK_Y] = 32767;
    if (KBD_STATE(rstick_left))
        state->axis[CONTROLLER_AXIS_RSTICK_X] = -32768;
    if (KBD_STATE(rstick_right))
        state->axis[CONTROLLER_AXIS_RSTICK_X] = 32767;
    if (KBD_STATE(rstick_down))
        state->axis[CONTROLLER_AXIS_RSTICK_Y] = -32768;
    if (KBD_STATE(rtrigger))
        state->axis[CONTROLLER_AXIS_RTRIG] = 32767;

#undef KBD_STATE
}

void xemu_input_update_sdl_controller_state(ControllerState *state)
{
    state->buttons = 0;
    memset(state->axis, 0, sizeof(state->axis));

#define SDL_MASK_BUTTON(state, btn, idx)                  \
    (SDL_GetGamepadButton(                                \
         (state)->sdl_gamepad,                            \
         (state)->controller_map->controller_mapping.btn) \
     << idx)

    state->buttons |= SDL_MASK_BUTTON(state, a, 0);
    state->buttons |= SDL_MASK_BUTTON(state, b, 1);
    state->buttons |= SDL_MASK_BUTTON(state, x, 2);
    state->buttons |= SDL_MASK_BUTTON(state, y, 3);
    state->buttons |= SDL_MASK_BUTTON(state, dpad_left, 4);
    state->buttons |= SDL_MASK_BUTTON(state, dpad_up, 5);
    state->buttons |= SDL_MASK_BUTTON(state, dpad_right, 6);
    state->buttons |= SDL_MASK_BUTTON(state, dpad_down, 7);
    state->buttons |= SDL_MASK_BUTTON(state, back, 8);
    state->buttons |= SDL_MASK_BUTTON(state, start, 9);
    state->buttons |= SDL_MASK_BUTTON(state, lshoulder, 10);
    state->buttons |= SDL_MASK_BUTTON(state, rshoulder, 11);
    state->buttons |= SDL_MASK_BUTTON(state, lstick_btn, 12);
    state->buttons |= SDL_MASK_BUTTON(state, rstick_btn, 13);
    state->buttons |= SDL_MASK_BUTTON(state, guide, 14);

#undef SDL_MASK_BUTTON

#define SDL_GET_AXIS(state, axis)    \
    SDL_GetGamepadAxis(              \
        (state)->sdl_gamepad,        \
        (state)->controller_map->controller_mapping.axis)

    state->axis[0] = SDL_GET_AXIS(state, axis_trigger_left);
    state->axis[1] = SDL_GET_AXIS(state, axis_trigger_right);
    state->axis[2] = SDL_GET_AXIS(state, axis_left_x);
    state->axis[3] = SDL_GET_AXIS(state, axis_left_y);
    state->axis[4] = SDL_GET_AXIS(state, axis_right_x);
    state->axis[5] = SDL_GET_AXIS(state, axis_right_y);

#undef SDL_GET_AXIS

// FIXME: Check range
#define INVERT_AXIS(controller_axis) \
    state->axis[controller_axis] = -1 - state->axis[controller_axis]

    if (state->controller_map->controller_mapping.invert_axis_left_x) {
        INVERT_AXIS(CONTROLLER_AXIS_LSTICK_X);
    }

    if (!state->controller_map->controller_mapping.invert_axis_left_y) {
        INVERT_AXIS(CONTROLLER_AXIS_LSTICK_Y);
    }

    if (state->controller_map->controller_mapping.invert_axis_right_x) {
        INVERT_AXIS(CONTROLLER_AXIS_RSTICK_X);
    }

    if (!state->controller_map->controller_mapping.invert_axis_right_y) {
        INVERT_AXIS(CONTROLLER_AXIS_RSTICK_Y);
    }

#undef INVERT_AXIS

    // xemu_input_print_controller_state(state);
}

void xemu_input_update_rumble(ControllerState *state)
{
    if (state->type != INPUT_DEVICE_SDL_GAMEPAD) {
        return;
    }

    if (!state->controller_map->enable_rumble) {
        return;
    }

    int64_t now = qemu_clock_get_us(QEMU_CLOCK_REALTIME);
    if (ABS(now - state->last_rumble_updated_ts) <
        XEMU_INPUT_MIN_RUMBLE_UPDATE_INTERVAL_US) {
        return;
    }

    SDL_RumbleGamepad(state->sdl_gamepad, state->rumble_l, state->rumble_r, 250);
    state->last_rumble_updated_ts = qemu_clock_get_us(QEMU_CLOCK_REALTIME);
}

ControllerState *xemu_input_get_bound(int index)
{
    return bound_controllers[index];
}

void xemu_input_bind(int index, ControllerState *state, int save)
{
    // FIXME: Attempt to disable rumble when unbinding so it's not left
    // in rumble mode

    // Unbind existing controller
    if (bound_controllers[index]) {
        assert(bound_controllers[index]->device != NULL);
        Error *err = NULL;

        // Unbind any XMUs
        for (int i = 0; i < 2; i++) {
            if (bound_controllers[index]->peripherals[i]) {
                // If this was an XMU, unbind the XMU
                if (bound_controllers[index]->peripheral_types[i] ==
                    PERIPHERAL_XMU)
                    xemu_input_unbind_xmu(index, i);

                // Free up the XmuState and set the peripheral type to none
                g_free(bound_controllers[index]->peripherals[i]);
                bound_controllers[index]->peripherals[i] = NULL;
                bound_controllers[index]->peripheral_types[i] = PERIPHERAL_NONE;
            }
        }

        qdev_unplug((DeviceState *)bound_controllers[index]->device, &err);
        assert(err == NULL);

        bound_controllers[index]->bound = -1;
        bound_controllers[index]->device = NULL;
        bound_controllers[index] = NULL;
    }

    // Save this controller's GUID in settings for auto re-connect
    if (save) {
        char guid_buf[35] = { 0 };
        if (state) {
            if (state->type == INPUT_DEVICE_SDL_GAMEPAD) {
                SDL_GUIDToString(state->sdl_joystick_guid, guid_buf, sizeof(guid_buf));
            } else if (state->type == INPUT_DEVICE_SDL_KEYBOARD) {
                snprintf(guid_buf, sizeof(guid_buf), "keyboard");
            }
        }
        xemu_settings_set_string(port_index_to_settings_key_map[index], guid_buf);
        xemu_settings_set_string(port_index_to_driver_settings_key_map[index],
                                 bound_drivers[index]);
    }

    // Bind new controller
    if (state) {
        if (state->bound >= 0) {
            // Device was already bound to another port. Unbind it.
            xemu_input_bind(state->bound, NULL, 1);
        }

        bound_controllers[index] = state;
        bound_controllers[index]->bound = index;

        char *tmp;

        // Create controller's internal USB hub.
        QDict *usbhub_qdict = qdict_new();
        qdict_put_str(usbhub_qdict, "driver", "usb-hub");
        tmp = g_strdup_printf("1.%d", port_map[index]);
        qdict_put_str(usbhub_qdict, "port", tmp);
        qdict_put_int(usbhub_qdict, "ports", 3);
        QemuOpts *usbhub_opts = qemu_opts_from_qdict(qemu_find_opts("device"), usbhub_qdict, &error_abort);
        DeviceState *usbhub_dev = qdev_device_add(usbhub_opts, &error_abort);
        g_free(tmp);

        // Create XID controller. This is connected to Port 1 of the controller's internal USB Hub
        QDict *qdict = qdict_new();

        // Specify device driver
        qdict_put_str(qdict, "driver", bound_drivers[index]);

        // Specify device identifier
        static int id_counter = 0;
        tmp = g_strdup_printf("gamepad_%d", id_counter++);
        qdict_put_str(qdict, "id", tmp);
        g_free(tmp);

        // Specify index/port
        qdict_put_int(qdict, "index", index);
        tmp = g_strdup_printf("1.%d.1", port_map[index]);
        qdict_put_str(qdict, "port", tmp);
        g_free(tmp);

        // Create the device
        QemuOpts *opts = qemu_opts_from_qdict(qemu_find_opts("device"), qdict, &error_abort);
        DeviceState *dev = qdev_device_add(opts, &error_abort);
        assert(dev);

        // Unref for eventual cleanup
        qobject_unref(usbhub_qdict);
        object_unref(OBJECT(usbhub_dev));
        qobject_unref(qdict);
        object_unref(OBJECT(dev));

        state->device = usbhub_dev;
    }
}

bool xemu_input_bind_xmu(int player_index, int expansion_slot_index,
                         const char *filename, bool is_rebind)
{
    assert(player_index >= 0 && player_index < 4);
    assert(expansion_slot_index >= 0 && expansion_slot_index < 2);

    ControllerState *player = bound_controllers[player_index];
    enum peripheral_type peripheral_type =
        player->peripheral_types[expansion_slot_index];
    if (peripheral_type != PERIPHERAL_XMU)
        return false;

    XmuState *xmu = (XmuState *)player->peripherals[expansion_slot_index];

    // Unbind existing XMU
    if (xmu->dev != NULL) {
        xemu_input_unbind_xmu(player_index, expansion_slot_index);
    }

    if (filename == NULL)
        return false;

    // Look for any other XMUs that are using this file, and unbind them
    for (int player_i = 0; player_i < 4; player_i++) {
        ControllerState *state = bound_controllers[player_i];
        if (state != NULL) {
            for (int peripheral_i = 0; peripheral_i < 2; peripheral_i++) {
                if (state->peripheral_types[peripheral_i] == PERIPHERAL_XMU) {
                    XmuState *xmu_i =
                        (XmuState *)state->peripherals[peripheral_i];
                    assert(xmu_i);

                    if (xmu_i->filename != NULL &&
                        strcmp(xmu_i->filename, filename) == 0) {
                        char *buf =
                            g_strdup_printf("This XMU is already mounted on "
                                            "player %d slot %c\r\n",
                                            player_i + 1, 'A' + peripheral_i);
                        xemu_queue_notification(buf);
                        g_free(buf);
                        return false;
                    }
                }
            }
        }
    }

    xmu->filename = g_strdup(filename);

    const int xmu_map[2] = { 2, 3 };
    char *tmp;

    static int id_counter = 0;
    tmp = g_strdup_printf("xmu_%d", id_counter++);

    // Add the file as a drive
    QDict *qdict1 = qdict_new();
    qdict_put_str(qdict1, "id", tmp);
    qdict_put_str(qdict1, "format", "raw");
    qdict_put_str(qdict1, "file", filename);

    QemuOpts *drvopts =
        qemu_opts_from_qdict(qemu_find_opts("drive"), qdict1, &error_abort);

    DriveInfo *dinfo = drive_new(drvopts, 0, &error_abort);
    assert(dinfo);

    // Create the usb-storage device
    QDict *qdict2 = qdict_new();

    // Specify device driver
    qdict_put_str(qdict2, "driver", "usb-storage");

    // Specify device identifier
    qdict_put_str(qdict2, "drive", tmp);
    g_free(tmp);

    // Specify index/port
    tmp = g_strdup_printf("1.%d.%d", port_map[player_index],
                          xmu_map[expansion_slot_index]);
    qdict_put_str(qdict2, "port", tmp);
    g_free(tmp);

    // Create the device
    QemuOpts *opts =
        qemu_opts_from_qdict(qemu_find_opts("device"), qdict2, &error_abort);

    DeviceState *dev = qdev_device_add(opts, &error_abort);
    assert(dev);

    xmu->dev = (void *)dev;

    // Unref for eventual cleanup
    qobject_unref(qdict1);
    qobject_unref(qdict2);

    if (!is_rebind) {
        xemu_save_peripheral_settings(player_index, expansion_slot_index,
                                      peripheral_type, xmu->filename);
    }

    return true;
}

void xemu_input_unbind_xmu(int player_index, int expansion_slot_index)
{
    assert(player_index >= 0 && player_index < 4);
    assert(expansion_slot_index >= 0 && expansion_slot_index < 2);

    ControllerState *state = bound_controllers[player_index];
    if (state->peripheral_types[expansion_slot_index] != PERIPHERAL_XMU)
        return;

    XmuState *xmu = (XmuState *)state->peripherals[expansion_slot_index];
    if (xmu != NULL) {
        if (xmu->dev != NULL) {
            qdev_unplug((DeviceState *)xmu->dev, &error_abort);
            object_unref(OBJECT(xmu->dev));
            xmu->dev = NULL;
        }

        g_free((void *)xmu->filename);
        xmu->filename = NULL;
    }
}

void xemu_input_rebind_xmu(int port)
{
    // Try to bind peripherals back to controller
    for (int i = 0; i < 2; i++) {
        enum peripheral_type peripheral_type =
            (enum peripheral_type)(*peripheral_types_settings_map[port][i]);

        // If peripheralType is out of range, change the settings for this
        // controller and peripheral port to default
        if (peripheral_type < PERIPHERAL_NONE ||
            peripheral_type >= PERIPHERAL_TYPE_COUNT) {
            xemu_save_peripheral_settings(port, i, PERIPHERAL_NONE, NULL);
            peripheral_type = PERIPHERAL_NONE;
        }

        const char *param = *peripheral_params_settings_map[port][i];

        if (peripheral_type == PERIPHERAL_XMU) {
            if (param != NULL && strlen(param) > 0) {
                // This is an XMU and needs to be bound to this controller
                if (qemu_access(param, R_OK | W_OK) == 0) {
                    bound_controllers[port]->peripheral_types[i] =
                        peripheral_type;
                    bound_controllers[port]->peripherals[i] =
                        g_malloc(sizeof(XmuState));
                    memset(bound_controllers[port]->peripherals[i], 0,
                           sizeof(XmuState));
                    bool did_bind = xemu_input_bind_xmu(port, i, param, true);
                    if (did_bind) {
                        char *buf =
                            g_strdup_printf("Connected XMU %s to port %d%c",
                                            param, port + 1, 'A' + i);
                        xemu_queue_notification(buf);
                        g_free(buf);
                    }
                } else {
                    char *buf =
                        g_strdup_printf("Unable to bind XMU at %s to port %d%c",
                                        param, port + 1, 'A' + i);
                    xemu_queue_error_message(buf);
                    g_free(buf);
                }
            }
        }
    }
}

void xemu_input_set_test_mode(int enabled)
{
    test_mode = enabled;
}

int xemu_input_get_test_mode(void)
{
    return test_mode;
}

void xemu_input_reset_input_mapping(ControllerState *state)
{
    if (state->type == INPUT_DEVICE_SDL_GAMEPAD) {
        char guid[35] = { 0 };
        SDL_GUIDToString(state->sdl_joystick_guid, guid, sizeof(guid));
        xemu_settings_reset_controller_mapping(guid);
    } else if (state->type == INPUT_DEVICE_SDL_KEYBOARD) {
        xemu_settings_reset_keyboard_mapping();
    }
}
