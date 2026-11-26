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

#ifndef HW_XBOX_NV2A_PGRAPH_D3D11_TEXTURE_H
#define HW_XBOX_NV2A_PGRAPH_D3D11_TEXTURE_H

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/texture.h"

#include <d3d11.h>

typedef struct D3D11TextureBinding {
    bool valid;
    bool failed;

    TextureShape shape;
    hwaddr vram_addr;
    size_t length;

    ID3D11Texture2D *texture2d;
    ID3D11Texture3D *texture3d;
    ID3D11ShaderResourceView *srv;
    ID3D11SamplerState *sampler;
    uint32_t sampler_hash;

    unsigned int width;
    unsigned int height;
    int swizzle[4];
    bool surface_backed;
} D3D11TextureBinding;

void pgraph_d3d11_init_textures(PGRAPHState *pg);
void pgraph_d3d11_finalize_textures(PGRAPHState *pg);
void pgraph_d3d11_bind_textures(NV2AState *d);
void pgraph_d3d11_invalidate_textures(PGRAPHState *pg);

#endif /* HW_XBOX_NV2A_PGRAPH_D3D11_TEXTURE_H */
