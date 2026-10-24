/*
 * Development-only differential oracle for XGrid's S3TC port.
 *
 * Compiles xemu 0.8.139 hw/xbox/nv2a/pgraph/s3tc.c standalone (with a stub
 * qemu/osdep.h), decodes a fixed corpus of blocks and writes the RGBA texels
 * as text. The vector file is replayed by
 * tests/XGrid.Nv2a.Tests/Nv2aTextureFormatTests.cs.
 *
 * The reference source is read from References2/ and is never redistributed;
 * only this generator and its output (facts) are in the repository.
 */

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "s3tc.h"

static uint32_t lcg_state = 0x12345678u;

static uint8_t next_byte(void)
{
    lcg_state = (lcg_state * 1664525u) + 1013904223u;
    return (uint8_t)(lcg_state >> 24);
}

static void fill_block(uint8_t *block, int size)
{
    for (int i = 0; i < size; i++) {
        block[i] = next_byte();
    }
}

static void emit(const char *name, enum S3TC_DECOMPRESS_FORMAT format,
                 const uint8_t *block, int size)
{
    unsigned int width = 4, height = 4;
    uint8_t *rgba = s3tc_decompress_2d(format, block, width, height);
    printf("%s ", name);
    for (int i = 0; i < size; i++) {
        printf("%02X", block[i]);
    }

    printf(" ");
    for (int i = 0; i < 64; i++) {
        printf("%02X", rgba[i]);
    }

    printf("\n");
    free(rgba);
}

static void emit_dxt1_case(const char *name, uint16_t c0, uint16_t c1,
                           uint32_t indices)
{
    uint8_t block[8];
    memcpy(block, &c0, 2);
    memcpy(block + 2, &c1, 2);
    memcpy(block + 4, &indices, 4);
    emit(name, S3TC_DECOMPRESS_FORMAT_DXT1, block, 8);
}

static void emit_dxt3_case(const char *name, uint64_t alpha, uint16_t c0,
                           uint16_t c1, uint32_t indices)
{
    uint8_t block[16];
    memcpy(block, &alpha, 8);
    memcpy(block + 8, &c0, 2);
    memcpy(block + 10, &c1, 2);
    memcpy(block + 12, &indices, 4);
    emit(name, S3TC_DECOMPRESS_FORMAT_DXT3, block, 16);
}

static void emit_dxt5_case(const char *name, uint64_t alpha, uint16_t c0,
                           uint16_t c1, uint32_t indices)
{
    uint8_t block[16];
    memcpy(block, &alpha, 8);
    memcpy(block + 8, &c0, 2);
    memcpy(block + 10, &c1, 2);
    memcpy(block + 12, &indices, 4);
    emit(name, S3TC_DECOMPRESS_FORMAT_DXT5, block, 16);
}

int main(void)
{
    uint8_t block[16];

    /* Endpoint/alpha edge cases: equal, ordered and inverted pairs. */
    emit_dxt1_case("dxt1-equal", 0xF800, 0xF800, 0xE4E4E4E4u);
    emit_dxt1_case("dxt1-opaque", 0xF800, 0x001F, 0xE4E4E4E4u);
    emit_dxt1_case("dxt1-transparent", 0x001F, 0xF800, 0xE4E4E4E4u);
    emit_dxt3_case("dxt3-equal", 0x0123456789ABCDEFull, 0x07E0, 0x07E0,
                   0x1B1B1B1Bu);
    emit_dxt3_case("dxt3-alpha", 0xFEDCBA9876543210ull, 0xF800, 0x001F,
                   0xE4E4E4E4u);
    emit_dxt5_case("dxt5-eight", 0x0123456789ABCDEFull, 0xF800, 0x001F,
                   0xE4E4E4E4u);
    emit_dxt5_case("dxt5-six", 0xFEDCBA9876543210ull, 0x07E0, 0x001F,
                   0x1B1B1B1Bu);

    /* Deterministic random blocks, three of each format. */
    for (int i = 0; i < 3; i++) {
        fill_block(block, 8);
        emit("dxt1-random", S3TC_DECOMPRESS_FORMAT_DXT1, block, 8);
        fill_block(block, 16);
        emit("dxt3-random", S3TC_DECOMPRESS_FORMAT_DXT3, block, 16);
        fill_block(block, 16);
        emit("dxt5-random", S3TC_DECOMPRESS_FORMAT_DXT5, block, 16);
    }

    return 0;
}
