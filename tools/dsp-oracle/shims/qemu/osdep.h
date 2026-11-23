/* Minimal QEMU host shims so the reference DSP interpreter compiles standalone. */
#ifndef QEMU_OSDEP_H
#define QEMU_OSDEP_H
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include <assert.h>
#define ARRAY_SIZE(x) (sizeof(x) / sizeof((x)[0]))
#endif
