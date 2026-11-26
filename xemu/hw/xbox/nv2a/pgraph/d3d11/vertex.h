/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - vertex stream
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

#ifndef HW_XBOX_NV2A_PGRAPH_D3D11_VERTEX_H
#define HW_XBOX_NV2A_PGRAPH_D3D11_VERTEX_H

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"

#include <d3d11.h>

typedef struct XGridVertex {
    float a[NV2A_VERTEXSHADER_ATTRIBUTES][4];
} XGridVertex;

typedef struct XGridDrawData {
    XGridVertex *vertices;
    unsigned int vertex_count;
    unsigned int base_index;

    uint32_t *indices;
    unsigned int index_count;

    D3D11_PRIMITIVE_TOPOLOGY topology;
    bool indexed;
    bool use_draw_arrays;
} XGridDrawData;

bool pgraph_d3d11_prepare_vertices(NV2AState *d, XGridDrawData *out);
void pgraph_d3d11_free_draw_data(XGridDrawData *data);

#endif /* HW_XBOX_NV2A_PGRAPH_D3D11_VERTEX_H */
