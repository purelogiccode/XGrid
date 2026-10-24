#pragma once

/* Minimal stand-in for qemu/osdep.h so the reference s3tc.c compiles
 * standalone in the development oracle. Not part of the XGrid build. */
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#define g_malloc(n) malloc(n)
#define MIN(a, b) ((a) < (b) ? (a) : (b))
