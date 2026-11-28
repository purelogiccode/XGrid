/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - texture cache
 *
 * Copyright (c) 2012 espes
 * Copyright (c) 2015 Jannik Vogel
 * Copyright (c) 2018-2026 Matt Borgerson
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, see <http://www.gnu.org/licenses/>.
 */

#ifndef COBJMACROS
#define COBJMACROS
#endif

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/pgraph.h"
#include "hw/xbox/nv2a/pgraph/swizzle.h"
#include "hw/xbox/nv2a/pgraph/s3tc.h"
#include "renderer.h"
#include "texture.h"

typedef enum D3D11TextureConversion {
    TEX_CONVERT_NONE,
    TEX_CONVERT_PALETTE,
    TEX_CONVERT_DXT1,
    TEX_CONVERT_DXT3,
    TEX_CONVERT_DXT5,
    TEX_CONVERT_YUY2,
    TEX_CONVERT_UYVY,
    TEX_CONVERT_R6G5B5,
} D3D11TextureConversion;

typedef struct D3D11TextureFormatInfo {
    DXGI_FORMAT format;
    DXGI_FORMAT converted_format;
    unsigned int bytes_per_pixel;
    bool linear;
    D3D11TextureConversion conversion;
    int swizzle[4];
    DXGI_FORMAT srv_format;
} D3D11TextureFormatInfo;

#define SWZ_IDENTITY { 0, 1, 2, 3 }
#define SWZ_Y8       { 0, 0, 0, 4 }
#define SWZ_AY8      { 0, 0, 0, 0 }
#define SWZ_A8       { 4, 4, 4, 0 }
#define SWZ_A8Y8     { 0, 0, 0, 1 }
#define SWZ_G8B8     { 0, 1, 0, 1 }
#define SWZ_R8B8     { 1, 0, 0, 1 }
#define SWZ_Y16      { 4, 1, 0, 4 }
#define SWZ_REVERSE  { 3, 2, 1, 0 }
#define SWZ_OPAQUE   { 0, 1, 2, 4 }
#define SWZ_DEPTH16  { 0, 5, 5, 5 }
#define SWZ_DEPTH16F { 0, 5, 4, 5 }
#define SWZ_DEPTH24  { 0, 4, 5, 5 }

static const D3D11TextureFormatInfo texture_format_map[66] = {
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_Y8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, false, TEX_CONVERT_NONE, SWZ_Y8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_AY8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, false, TEX_CONVERT_NONE, SWZ_AY8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A1R5G5B5] =
        { DXGI_FORMAT_B5G5R5A1_UNORM, 0, 2, false, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_X1R5G5B5] =
        { DXGI_FORMAT_B5G5R5A1_UNORM, 0, 2, false, TEX_CONVERT_NONE,
          SWZ_OPAQUE },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A4R4G4B4] =
        { DXGI_FORMAT_B4G4R4A4_UNORM, 0, 2, false, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_R5G6B5] =
        { DXGI_FORMAT_B5G6R5_UNORM, 0, 2, false, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A8R8G8B8] =
        { DXGI_FORMAT_B8G8R8A8_UNORM, 0, 4, false, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_X8R8G8B8] =
        { DXGI_FORMAT_B8G8R8A8_UNORM, 0, 4, false, TEX_CONVERT_NONE,
          SWZ_OPAQUE },

    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_I8_A8R8G8B8] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_B8G8R8A8_UNORM, 1, false,
          TEX_CONVERT_PALETTE, SWZ_IDENTITY },

    [NV097_SET_TEXTURE_FORMAT_COLOR_L_DXT1_A1R5G5B5] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_UNORM, 4, false,
          TEX_CONVERT_DXT1, SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_L_DXT23_A8R8G8B8] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_UNORM, 4, false,
          TEX_CONVERT_DXT3, SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_L_DXT45_A8R8G8B8] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_UNORM, 4, false,
          TEX_CONVERT_DXT5, SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A1R5G5B5] =
        { DXGI_FORMAT_B5G5R5A1_UNORM, 0, 2, true, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_R5G6B5] =
        { DXGI_FORMAT_B5G6R5_UNORM, 0, 2, true, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8R8G8B8] =
        { DXGI_FORMAT_B8G8R8A8_UNORM, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_Y8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, true, TEX_CONVERT_NONE, SWZ_Y8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_G8B8] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, true, TEX_CONVERT_NONE, SWZ_G8B8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, false, TEX_CONVERT_NONE, SWZ_A8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A8Y8] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, false, TEX_CONVERT_NONE, SWZ_A8Y8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_AY8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, true, TEX_CONVERT_NONE, SWZ_AY8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_X1R5G5B5] =
        { DXGI_FORMAT_B5G5R5A1_UNORM, 0, 2, true, TEX_CONVERT_NONE,
          SWZ_OPAQUE },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A4R4G4B4] =
        { DXGI_FORMAT_B4G4R4A4_UNORM, 0, 2, true, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_X8R8G8B8] =
        { DXGI_FORMAT_B8G8R8A8_UNORM, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_OPAQUE },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8] =
        { DXGI_FORMAT_R8_UNORM, 0, 1, true, TEX_CONVERT_NONE, SWZ_A8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8Y8] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, true, TEX_CONVERT_NONE, SWZ_A8Y8 },

    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_R6G5B5] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_SNORM, 2, false,
          TEX_CONVERT_R6G5B5, SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_G8B8] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, false, TEX_CONVERT_NONE, SWZ_G8B8 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_R8B8] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, false, TEX_CONVERT_NONE, SWZ_R8B8 },

    [NV097_SET_TEXTURE_FORMAT_COLOR_LC_IMAGE_CR8YB8CB8YA8] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_UNORM, 2, true,
          TEX_CONVERT_YUY2, SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LC_IMAGE_YB8CR8YA8CB8] =
        { DXGI_FORMAT_UNKNOWN, DXGI_FORMAT_R8G8B8A8_UNORM, 2, true,
          TEX_CONVERT_UYVY, SWZ_IDENTITY },

    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_Y16] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, false, TEX_CONVERT_NONE, SWZ_Y16 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_Y16] =
        { DXGI_FORMAT_R8G8_UNORM, 0, 2, true, TEX_CONVERT_NONE, SWZ_Y16 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_A8B8G8R8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, false, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_B8G8R8A8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, false, TEX_CONVERT_NONE,
          { 1, 2, 3, 0 } },
    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_R8G8B8A8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, false, TEX_CONVERT_NONE,
          SWZ_REVERSE },

    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_A8B8G8R8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_IDENTITY },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_B8G8R8A8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, true, TEX_CONVERT_NONE,
          { 1, 2, 3, 0 } },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_R8G8B8A8] =
        { DXGI_FORMAT_R8G8B8A8_UNORM, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_REVERSE },

    [NV097_SET_TEXTURE_FORMAT_COLOR_SZ_DEPTH_Y16_FIXED] =
        { DXGI_FORMAT_R16_UNORM, 0, 2, false, TEX_CONVERT_NONE,
          SWZ_DEPTH16, 0 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_DEPTH_Y16_FIXED] =
        { DXGI_FORMAT_R16_UNORM, 0, 2, true, TEX_CONVERT_NONE, SWZ_DEPTH16,
          0 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_DEPTH_Y16_FLOAT] =
        { DXGI_FORMAT_R16_FLOAT, 0, 2, true, TEX_CONVERT_NONE, SWZ_DEPTH16F,
          0 },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_DEPTH_X8_Y24_FIXED] =
        { DXGI_FORMAT_R24G8_TYPELESS, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_DEPTH24, DXGI_FORMAT_R24_UNORM_X8_TYPELESS },
    [NV097_SET_TEXTURE_FORMAT_COLOR_LU_IMAGE_DEPTH_X8_Y24_FLOAT] =
        { DXGI_FORMAT_R24G8_TYPELESS, 0, 4, true, TEX_CONVERT_NONE,
          SWZ_DEPTH24, DXGI_FORMAT_R24_UNORM_X8_TYPELESS },
};

static unsigned int conversion_block_size(D3D11TextureConversion conversion)
{
    switch (conversion) {
    case TEX_CONVERT_DXT1:
        return 8;
    case TEX_CONVERT_DXT3:
    case TEX_CONVERT_DXT5:
        return 16;
    default:
        return 0;
    }
}

static size_t texture_mip_size(const D3D11TextureFormatInfo *fmt,
                               unsigned int width, unsigned int height,
                               unsigned int depth)
{
    unsigned int block_size = conversion_block_size(fmt->conversion);
    if (block_size) {
        unsigned int physical_width = (width + 3) & ~3;
        unsigned int physical_height = (height + 3) & ~3;
        return (size_t)(physical_width / 4) * (physical_height / 4) * depth *
               block_size;
    }
    return (size_t)MAX(width, 1) * MAX(height, 1) * MAX(depth, 1) *
           fmt->bytes_per_pixel;
}

static void texture_binding_release(D3D11TextureBinding *b)
{
    if (b->srv) {
        ID3D11ShaderResourceView_Release(b->srv);
        b->srv = NULL;
    }
    if (b->sampler) {
        ID3D11SamplerState_Release(b->sampler);
        b->sampler = NULL;
    }
    if (b->texture2d) {
        ID3D11Texture2D_Release(b->texture2d);
        b->texture2d = NULL;
    }
    if (b->texture3d) {
        ID3D11Texture3D_Release(b->texture3d);
        b->texture3d = NULL;
    }
    b->valid = false;
    b->failed = false;
    b->surface_backed = false;
    b->sampler_hash = 0;
    b->palette_vram_addr = 0;
    b->palette_length = 0;
}

static uint8_t *texture_decode_level(const TextureShape *shape,
                                     const D3D11TextureFormatInfo *fmt,
                                     const uint8_t *data,
                                     const uint8_t *palette,
                                     unsigned int width, unsigned int height,
                                     unsigned int row_pitch,
                                     unsigned int *out_pitch)
{
    unsigned int block_size = conversion_block_size(fmt->conversion);
    if (block_size) {
        enum S3TC_DECOMPRESS_FORMAT s3tc_format;
        switch (fmt->conversion) {
        case TEX_CONVERT_DXT1:
            s3tc_format = S3TC_DECOMPRESS_FORMAT_DXT1;
            break;
        case TEX_CONVERT_DXT3:
            s3tc_format = S3TC_DECOMPRESS_FORMAT_DXT3;
            break;
        default:
            s3tc_format = S3TC_DECOMPRESS_FORMAT_DXT5;
            break;
        }
        *out_pitch = width * 4;
        return s3tc_decompress_2d(s3tc_format, data, width, height);
    }

    uint8_t *decoded = NULL;
    const uint8_t *source = data;
    if (!fmt->linear) {
        uint8_t *unswizzled = g_malloc((size_t)height * row_pitch);
        unswizzle_rect(data, width, height, unswizzled, row_pitch,
                       fmt->bytes_per_pixel);
        source = unswizzled;
        decoded = unswizzled;
    }

    uint8_t *converted = NULL;
    if (fmt->conversion != TEX_CONVERT_NONE) {
        converted = pgraph_convert_texture_data(
            *shape, source, palette, width, height, 1, row_pitch, 0, NULL);
        if (converted) {
            g_free(decoded);
            decoded = converted;
            *out_pitch = width * 4;
            return decoded;
        }
    }

    *out_pitch = row_pitch;
    return decoded;
}

static bool texture_create_2d(NV2AState *d, D3D11TextureBinding *b,
                              const TextureShape *shape, const uint8_t *data,
                              const uint8_t *palette)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    const D3D11TextureFormatInfo *fmt = &texture_format_map[shape->color_format];

    if (fmt->format == DXGI_FORMAT_UNKNOWN &&
        fmt->conversion == TEX_CONVERT_NONE) {
        return false;
    }

    unsigned int width = shape->width;
    unsigned int height = shape->height;
    unsigned int depth = shape->depth;
    unsigned int levels = shape->levels;
    if (!fmt->linear && shape->border) {
        width = MAX(16, width * 2);
        height = MAX(16, height * 2);
        depth = MAX(16, depth * 2);
    }
    if (fmt->linear) {
        levels = 1;
    }
    if (depth == 0) {
        depth = 1;
    }

    bool cube = shape->cubemap;
    unsigned int faces = cube ? 6 : 1;
    DXGI_FORMAT dxgi_format =
        fmt->conversion != TEX_CONVERT_NONE ? fmt->converted_format
                                            : fmt->format;

    size_t face_length = 0;
    if (cube) {
        unsigned int level_width = width;
        unsigned int level_height = height;
        for (unsigned int level = 0; level < levels; level++) {
            face_length += texture_mip_size(fmt, level_width, level_height, 1);
            level_width = MAX(level_width / 2, 1);
            level_height = MAX(level_height / 2, 1);
        }
        face_length =
            ROUND_UP(face_length, NV2A_CUBEMAP_FACE_ALIGNMENT);
    }

    D3D11_TEXTURE2D_DESC desc = {
        .Width = width,
        .Height = height,
        .MipLevels = levels,
        .ArraySize = faces,
        .Format = dxgi_format,
        .SampleDesc = { 1, 0 },
        .Usage = D3D11_USAGE_DEFAULT,
        .BindFlags = D3D11_BIND_SHADER_RESOURCE,
        .CPUAccessFlags = 0,
        .MiscFlags = cube ? D3D11_RESOURCE_MISC_TEXTURECUBE : 0,
    };
    HRESULT hr = ID3D11Device_CreateTexture2D(r->device, &desc, NULL,
                                              &b->texture2d);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 texture creation failed (hr=0x%08lx, %ux%u "
                "fmt=%d levels=%u)\n",
                (unsigned long)hr, width, height, (int)dxgi_format, levels);
        return false;
    }

    for (unsigned int face = 0; face < faces; face++) {
        const uint8_t *level_data = data + (size_t)face * face_length;
        unsigned int level_width = width;
        unsigned int level_height = height;
        for (unsigned int level = 0; level < levels; level++) {
            unsigned int row_pitch =
                fmt->linear
                    ? (level == 0
                           ? (shape->pitch
                                  ? shape->pitch
                                  : level_width * fmt->bytes_per_pixel)
                           : level_width * fmt->bytes_per_pixel)
                    : level_width * fmt->bytes_per_pixel;
            unsigned int decoded_pitch = row_pitch;
            uint8_t *decoded = texture_decode_level(
                shape, fmt, level_data, palette, level_width, level_height,
                row_pitch, &decoded_pitch);

            UINT subresource = level + face * levels;
            if (decoded) {
                ID3D11DeviceContext_UpdateSubresource(
                    r->context, (ID3D11Resource *)b->texture2d, subresource,
                    NULL, decoded, decoded_pitch, 0);
                g_free(decoded);
            } else {
                ID3D11DeviceContext_UpdateSubresource(
                    r->context, (ID3D11Resource *)b->texture2d, subresource,
                    NULL, level_data, row_pitch, 0);
            }

            level_data += texture_mip_size(fmt, level_width, level_height, 1);
            level_width = MAX(level_width / 2, 1);
            level_height = MAX(level_height / 2, 1);
        }
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srv_desc;
    memset(&srv_desc, 0, sizeof(srv_desc));
    srv_desc.Format = fmt->srv_format ? fmt->srv_format : dxgi_format;
    srv_desc.ViewDimension =
        cube ? D3D11_SRV_DIMENSION_TEXTURECUBE : D3D11_SRV_DIMENSION_TEXTURE2D;
    if (cube) {
        srv_desc.TextureCube.MostDetailedMip = 0;
        srv_desc.TextureCube.MipLevels = levels;
    } else {
        srv_desc.Texture2D.MostDetailedMip = 0;
        srv_desc.Texture2D.MipLevels = levels;
    }
    hr = ID3D11Device_CreateShaderResourceView(
        r->device, (ID3D11Resource *)b->texture2d, &srv_desc, &b->srv);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 texture SRV creation failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }

    b->width = width;
    b->height = height;
    return true;
}

static bool texture_create_3d(NV2AState *d, D3D11TextureBinding *b,
                              const TextureShape *shape, const uint8_t *data,
                              const uint8_t *palette)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    const D3D11TextureFormatInfo *fmt = &texture_format_map[shape->color_format];

    if (fmt->format == DXGI_FORMAT_UNKNOWN ||
        fmt->conversion != TEX_CONVERT_NONE) {
        return false;
    }

    unsigned int width = shape->width;
    unsigned int height = shape->height;
    unsigned int depth = MAX(shape->depth, 1);
    unsigned int levels = shape->levels;

    D3D11_TEXTURE3D_DESC desc = {
        .Width = width,
        .Height = height,
        .Depth = depth,
        .MipLevels = levels,
        .Format = fmt->format,
        .Usage = D3D11_USAGE_DEFAULT,
        .BindFlags = D3D11_BIND_SHADER_RESOURCE,
        .CPUAccessFlags = 0,
        .MiscFlags = 0,
    };
    HRESULT hr = ID3D11Device_CreateTexture3D(r->device, &desc, NULL,
                                              &b->texture3d);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 3D texture creation failed (hr=0x%08lx, "
                "%ux%ux%u fmt=%d)\n",
                (unsigned long)hr, width, height, depth, (int)fmt->format);
        return false;
    }

    const uint8_t *level_data = data;
    unsigned int level_width = width;
    unsigned int level_height = height;
    unsigned int level_depth = depth;
    for (unsigned int level = 0; level < levels; level++) {
        unsigned int row_pitch = level_width * fmt->bytes_per_pixel;
        unsigned int slice_pitch = row_pitch * level_height;
        uint8_t *unswizzled = g_malloc((size_t)slice_pitch * level_depth);
        unswizzle_box(level_data, level_width, level_height, level_depth,
                      unswizzled, row_pitch, slice_pitch,
                      fmt->bytes_per_pixel);
        ID3D11DeviceContext_UpdateSubresource(
            r->context, (ID3D11Resource *)b->texture3d, level, NULL,
            unswizzled, row_pitch, slice_pitch);
        g_free(unswizzled);

        level_data += texture_mip_size(fmt, level_width, level_height,
                                       level_depth);
        level_width = MAX(level_width / 2, 1);
        level_height = MAX(level_height / 2, 1);
        level_depth = MAX(level_depth / 2, 1);
    }

    D3D11_SHADER_RESOURCE_VIEW_DESC srv_desc;
    memset(&srv_desc, 0, sizeof(srv_desc));
    srv_desc.Format = fmt->srv_format ? fmt->srv_format : fmt->format;
    srv_desc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE3D;
    srv_desc.Texture3D.MostDetailedMip = 0;
    srv_desc.Texture3D.MipLevels = levels;
    hr = ID3D11Device_CreateShaderResourceView(
        r->device, (ID3D11Resource *)b->texture3d, &srv_desc, &b->srv);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 3D texture SRV creation failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }

    b->width = width;
    b->height = height;
    return true;
}

static D3D11_TEXTURE_ADDRESS_MODE texture_address_mode(unsigned int mode)
{
    switch (mode) {
    case NV_PGRAPH_TEXADDRESS0_ADDRU_WRAP:
        return D3D11_TEXTURE_ADDRESS_WRAP;
    case NV_PGRAPH_TEXADDRESS0_ADDRU_MIRROR:
        return D3D11_TEXTURE_ADDRESS_MIRROR;
    case NV_PGRAPH_TEXADDRESS0_ADDRU_BORDER:
        return D3D11_TEXTURE_ADDRESS_BORDER;
    case NV_PGRAPH_TEXADDRESS0_ADDRU_CLAMP_TO_EDGE:
    case NV_PGRAPH_TEXADDRESS0_ADDRU_CLAMP_OGL:
    default:
        return D3D11_TEXTURE_ADDRESS_CLAMP;
    }
}

static D3D11_FILTER texture_filter(unsigned int min_filter,
                                   unsigned int mag_filter)
{
    bool min_linear = min_filter == NV_PGRAPH_TEXFILTER0_MIN_TENT_LOD0 ||
                      min_filter == NV_PGRAPH_TEXFILTER0_MIN_TENT_NEARESTLOD ||
                      min_filter == NV_PGRAPH_TEXFILTER0_MIN_TENT_TENT_LOD ||
                      min_filter == NV_PGRAPH_TEXFILTER0_MIN_CONVOLUTION_2D_LOD0;
    bool mag_linear = mag_filter == 2 || mag_filter == 4;
    bool mip_linear = min_filter == NV_PGRAPH_TEXFILTER0_MIN_BOX_TENT_LOD ||
                      min_filter == NV_PGRAPH_TEXFILTER0_MIN_TENT_TENT_LOD;

    if (!min_linear && !mag_linear && !mip_linear) {
        return D3D11_FILTER_MIN_MAG_MIP_POINT;
    }
    if (!min_linear && !mag_linear && mip_linear) {
        return D3D11_FILTER_MIN_MAG_POINT_MIP_LINEAR;
    }
    if (!min_linear && mag_linear && !mip_linear) {
        return D3D11_FILTER_MIN_POINT_MAG_LINEAR_MIP_POINT;
    }
    if (!min_linear && mag_linear && mip_linear) {
        return D3D11_FILTER_MIN_POINT_MAG_MIP_LINEAR;
    }
    if (min_linear && !mag_linear && !mip_linear) {
        return D3D11_FILTER_MIN_LINEAR_MAG_MIP_POINT;
    }
    if (min_linear && !mag_linear && mip_linear) {
        return D3D11_FILTER_MIN_LINEAR_MAG_POINT_MIP_LINEAR;
    }
    if (min_linear && mag_linear && !mip_linear) {
        return D3D11_FILTER_MIN_MAG_LINEAR_MIP_POINT;
    }
    return D3D11_FILTER_MIN_MAG_MIP_LINEAR;
}

static bool texture_update_sampler(NV2AState *d, D3D11TextureBinding *b,
                                   unsigned int stage)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    uint32_t filter = pgraph_reg_r(pg, NV_PGRAPH_TEXFILTER0 + stage * 4);
    uint32_t address = pgraph_reg_r(pg, NV_PGRAPH_TEXADDRESS0 + stage * 4);
    uint32_t border_color = pgraph_reg_r(pg, NV_PGRAPH_BORDERCOLOR0 + stage * 4);
    unsigned int min_filter = GET_MASK(filter, NV_PGRAPH_TEXFILTER0_MIN);
    unsigned int mag_filter = GET_MASK(filter, NV_PGRAPH_TEXFILTER0_MAG);
    unsigned int lod_bias =
        GET_MASK(filter, NV_PGRAPH_TEXFILTER0_MIPMAP_LOD_BIAS);

    uint32_t hash = filter ^ (address * 2654435761u) ^
                    (border_color * 2246822519u);
    if (b->sampler && b->sampler_hash == hash) {
        return true;
    }

    if (b->sampler) {
        ID3D11SamplerState_Release(b->sampler);
        b->sampler = NULL;
    }

    D3D11_SAMPLER_DESC desc;
    memset(&desc, 0, sizeof(desc));
    desc.Filter = texture_filter(min_filter, mag_filter);
    desc.AddressU =
        texture_address_mode(GET_MASK(address, NV_PGRAPH_TEXADDRESS0_ADDRU));
    desc.AddressV =
        texture_address_mode(GET_MASK(address, NV_PGRAPH_TEXADDRESS0_ADDRV));
    desc.AddressW =
        texture_address_mode(GET_MASK(address, NV_PGRAPH_TEXADDRESS0_ADDRP));
    pgraph_argb_pack32_to_rgba_float(border_color, desc.BorderColor);
    desc.MipLODBias = pgraph_convert_lod_bias_to_float(lod_bias);
    desc.MaxAnisotropy = 1;
    desc.ComparisonFunc = D3D11_COMPARISON_NEVER;
    desc.MinLOD = 0.0f;
    desc.MaxLOD = (min_filter == NV_PGRAPH_TEXFILTER0_MIN_BOX_LOD0 ||
                   min_filter == NV_PGRAPH_TEXFILTER0_MIN_TENT_LOD0 ||
                   min_filter ==
                       NV_PGRAPH_TEXFILTER0_MIN_CONVOLUTION_2D_LOD0)
                        ? 0.0f
                        : D3D11_FLOAT32_MAX;

    HRESULT hr = ID3D11Device_CreateSamplerState(r->device, &desc,
                                                 &b->sampler);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 sampler creation failed "
                        "(hr=0x%08lx)\n", (unsigned long)hr);
        return false;
    }
    b->sampler_hash = hash;
    return true;
}

static void texture_unbind_stage(PGRAPHD3D11State *r, unsigned int stage)
{
    ID3D11ShaderResourceView *null_srv = NULL;
    ID3D11SamplerState *null_sampler = NULL;
    ID3D11DeviceContext_PSSetShaderResources(r->context, stage, 1,
                                             &null_srv);
    ID3D11DeviceContext_PSSetSamplers(r->context, stage, 1, &null_sampler);
}

void pgraph_d3d11_invalidate_textures(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    for (int i = 0; i < NV2A_MAX_TEXTURES; i++) {
        texture_binding_release(&r->textures[i]);
        memset(&r->textures[i], 0, sizeof(r->textures[i]));
    }
}

static void texture_mark_overlapping_dirty(NV2AState *d, hwaddr start,
                                           hwaddr end)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    for (int i = 0; i < NV2A_MAX_TEXTURES; i++) {
        D3D11TextureBinding *binding = &r->textures[i];
        if (!binding->valid) {
            continue;
        }

        bool overlaps = !(binding->vram_addr >= end ||
                          start >= binding->vram_addr + binding->length);
        if (!overlaps && binding->palette_length) {
            overlaps = !(binding->palette_vram_addr >= end ||
                         start >= binding->palette_vram_addr +
                                      binding->palette_length);
        }
        if (overlaps) {
            /* Another stage cleared the page dirty bit for this binding. */
            pg->texture_dirty[i] = true;
        }
    }
}

static bool texture_dirty_range(NV2AState *d, hwaddr addr, size_t length)
{
    if (length == 0) {
        return false;
    }

    hwaddr start = addr & TARGET_PAGE_MASK;
    hwaddr end = TARGET_PAGE_ALIGN(addr + length);
    hwaddr vram_size = memory_region_size(d->vram);
    end = MIN(end, vram_size);
    if (start >= end) {
        return false;
    }

    bool dirty = memory_region_test_and_clear_dirty(
        d->vram, start, end - start, DIRTY_MEMORY_NV2A_TEX);
    if (dirty) {
        texture_mark_overlapping_dirty(d, start, end);
    }
    return dirty;
}

static bool texture_format_matches_surface(const TextureShape *shape,
                                           const D3D11SurfaceBinding *surface)
{
    const D3D11TextureFormatInfo *fmt = &texture_format_map[shape->color_format];
    return !fmt->linear && fmt->conversion == TEX_CONVERT_NONE &&
           fmt->srv_format == 0 && fmt->format == surface->fmt.format;
}

void pgraph_d3d11_bind_textures(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    hwaddr vram_size = memory_region_size(d->vram);

    for (int i = 0; i < NV2A_MAX_TEXTURES; i++) {
        if (!pgraph_is_texture_enabled(pg, i)) {
            texture_unbind_stage(r, i);
            texture_binding_release(&r->textures[i]);
            pg->texture_dirty[i] = false;
            continue;
        }

        TextureShape shape = pgraph_get_texture_shape(pg, i);
        hwaddr texture_offset = pgraph_get_texture_phys_addr(pg, i);
        size_t palette_length = 0;
        hwaddr palette_offset =
            pgraph_get_texture_palette_phys_addr_length(pg, i, &palette_length);
        size_t length = pgraph_get_texture_length(pg, &shape);
        const bool indexed = shape.color_format ==
                             NV097_SET_TEXTURE_FORMAT_COLOR_SZ_I8_A8R8G8B8;
        const hwaddr binding_palette_offset =
            indexed ? palette_offset : 0;
        const size_t binding_palette_length = indexed ? palette_length : 0;

        if (length == 0 || texture_offset >= vram_size ||
            length > vram_size - texture_offset) {
            texture_unbind_stage(r, i);
            texture_binding_release(&r->textures[i]);
            continue;
        }

        const uint8_t *texture_data = d->vram_ptr + texture_offset;
        const uint8_t *palette_data = NULL;
        if (indexed) {
            if (palette_offset >= vram_size || palette_length == 0 ||
                palette_length > vram_size - palette_offset) {
                texture_unbind_stage(r, i);
                texture_binding_release(&r->textures[i]);
                continue;
            }
            palette_data = d->vram_ptr + palette_offset;
        }

        D3D11TextureBinding *binding = &r->textures[i];
        D3D11SurfaceBinding *surface =
            pgraph_d3d11_surface_get(d, texture_offset);
        bool use_surface =
            surface && surface->color && surface->srv &&
            surface->vram_addr == texture_offset &&
            surface != r->color_binding && surface != r->zeta_binding &&
            shape.dimensionality == 2 && !shape.cubemap && !shape.border &&
            shape.levels == 1 && pg->surface_scale_factor == 1 &&
            surface->width == shape.width &&
            surface->height == shape.height &&
            texture_format_matches_surface(&shape, surface);

        if (use_surface) {
            pgraph_d3d11_upload_surface_data(d, surface, false);

            bool changed = !binding->surface_backed || !binding->valid ||
                           binding->srv != surface->srv ||
                           binding->vram_addr != texture_offset ||
                           binding->length != length ||
                           binding->palette_vram_addr != binding_palette_offset ||
                           binding->palette_length != binding_palette_length ||
                           pg->texture_dirty[i] ||
                           memcmp(&binding->shape, &shape, sizeof(shape)) != 0;
            if (changed) {
                texture_binding_release(binding);
                memset(binding, 0, sizeof(*binding));
                binding->shape = shape;
                binding->vram_addr = texture_offset;
                binding->length = length;
                binding->palette_vram_addr = binding_palette_offset;
                binding->palette_length = binding_palette_length;
                binding->srv = surface->srv;
                ID3D11ShaderResourceView_AddRef(binding->srv);
                binding->width = shape.width;
                binding->height = shape.height;
                memcpy(binding->swizzle,
                       texture_format_map[shape.color_format].swizzle,
                       sizeof(binding->swizzle));
                binding->surface_backed = true;
                binding->valid = true;
            }

            if (!texture_update_sampler(d, binding, i)) {
                texture_unbind_stage(r, i);
                continue;
            }

            ID3D11DeviceContext_PSSetShaderResources(r->context, i, 1,
                                                     &binding->srv);
            ID3D11DeviceContext_PSSetSamplers(r->context, i, 1,
                                              &binding->sampler);
            pg->texture_dirty[i] = false;
            continue;
        }

        bool changed = !binding->valid || binding->failed ||
                       binding->surface_backed ||
                       binding->vram_addr != texture_offset ||
                       binding->length != length ||
                       binding->palette_vram_addr != binding_palette_offset ||
                       binding->palette_length != binding_palette_length ||
                       memcmp(&binding->shape, &shape, sizeof(shape)) != 0 ||
                       pg->texture_dirty[i];

        pgraph_d3d11_download_surfaces_in_range_if_dirty(pg, texture_offset,
                                                         length);
        if (palette_data) {
            pgraph_d3d11_download_surfaces_in_range_if_dirty(
                pg, palette_offset, palette_length);
        }

        if (texture_dirty_range(d, texture_offset, length)) {
            changed = true;
        }
        if (palette_data && texture_dirty_range(d, palette_offset,
                                                palette_length)) {
            changed = true;
        }

        if (changed) {
            texture_binding_release(binding);
            memset(binding, 0, sizeof(*binding));
            binding->shape = shape;
            binding->vram_addr = texture_offset;
            binding->length = length;
            binding->palette_vram_addr = binding_palette_offset;
            binding->palette_length = binding_palette_length;

            bool ok;
            if (shape.dimensionality >= 3) {
                ok = texture_create_3d(d, binding, &shape, texture_data,
                                       palette_data);
            } else {
                ok = texture_create_2d(d, binding, &shape, texture_data,
                                       palette_data);
            }

            if (!ok) {
                binding->failed = true;
                binding->valid = false;
                texture_unbind_stage(r, i);
                continue;
            }

            memcpy(binding->swizzle,
                   texture_format_map[shape.color_format].swizzle,
                   sizeof(binding->swizzle));
            binding->valid = true;
        }

        if (!texture_update_sampler(d, binding, i)) {
            texture_unbind_stage(r, i);
            continue;
        }

        ID3D11DeviceContext_PSSetShaderResources(r->context, i, 1,
                                                 &binding->srv);
        ID3D11DeviceContext_PSSetSamplers(r->context, i, 1,
                                          &binding->sampler);
        pg->texture_dirty[i] = false;
    }
}

void pgraph_d3d11_init_textures(PGRAPHState *pg)
{
    pgraph_d3d11_invalidate_textures(pg);
}

void pgraph_d3d11_finalize_textures(PGRAPHState *pg)
{
    pgraph_d3d11_invalidate_textures(pg);
}
