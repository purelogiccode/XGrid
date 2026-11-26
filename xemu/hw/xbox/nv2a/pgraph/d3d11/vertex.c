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

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/pgraph.h"
#include "vertex.h"

static void read_compressed_11_11_10(const uint8_t *data, float out[4])
{
    const int32_t val = *(const int32_t *)data;
    int32_t x = val & 0x7FF;
    if (x & 0x400) {
        x |= 0xFFFFF800;
    }
    int32_t y = (val >> 11) & 0x7FF;
    if (y & 0x400) {
        y |= 0xFFFFF800;
    }
    int32_t z = (val >> 22) & 0x7FF;
    if (z & 0x200) {
        z |= 0xFFFFFC00;
    }

    out[0] = MAX(-1.0f, (float)x / 1023.0f);
    out[1] = MAX(-1.0f, (float)y / 1023.0f);
    out[2] = MAX(-1.0f, (float)z / 511.0f);
    out[3] = 1.0f;
}

static void read_attribute(const VertexAttribute *attr, const uint8_t *data,
                           float out[4])
{
    out[0] = 0.0f;
    out[1] = 0.0f;
    out[2] = 0.0f;
    out[3] = 1.0f;

    switch (attr->format) {
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_UB_D3D:
        out[0] = data[2] / 255.0f;
        out[1] = data[1] / 255.0f;
        out[2] = data[0] / 255.0f;
        out[3] = data[3] / 255.0f;
        break;
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_UB_OGL:
        for (unsigned int i = 0; i < attr->count; i++) {
            out[i] = data[i] / 255.0f;
        }
        break;
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_S1: {
        const int16_t *s = (const int16_t *)data;
        for (unsigned int i = 0; i < attr->count; i++) {
            out[i] = MAX(-1.0f, (float)s[i] / 32767.0f);
        }
        break;
    }
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_F:
        memcpy(out, data, attr->size * attr->count);
        break;
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_S32K: {
        const int16_t *s = (const int16_t *)data;
        for (unsigned int i = 0; i < attr->count; i++) {
            out[i] = (float)s[i];
        }
        break;
    }
    case NV097_SET_VERTEX_DATA_ARRAY_FORMAT_TYPE_CMP:
        read_compressed_11_11_10(data, out);
        break;
    default:
        fprintf(stderr, "nv2a: unknown D3D11 vertex type 0x%x\n",
                attr->format);
        break;
    }
}

static void fetch_vertex(PGRAPHState *pg, unsigned int element,
                         const uint8_t *const bases[NV2A_VERTEXSHADER_ATTRIBUTES],
                         unsigned int inline_stride, bool inline_data,
                         bool inline_buffer_data, XGridVertex *vertex)
{
    for (int i = 0; i < NV2A_VERTEXSHADER_ATTRIBUTES; i++) {
        VertexAttribute *attr = &pg->vertex_attributes[i];

        if (inline_buffer_data) {
            if (attr->inline_buffer_populated) {
                memcpy(vertex->a[i], attr->inline_buffer + element * 4,
                       sizeof(float) * 4);
            } else {
                memcpy(vertex->a[i], attr->inline_value, sizeof(float) * 4);
            }
            continue;
        }

        if (!attr->count) {
            memcpy(vertex->a[i], attr->inline_value, sizeof(float) * 4);
            continue;
        }

        if (!bases[i]) {
            memcpy(vertex->a[i], attr->inline_value, sizeof(float) * 4);
            continue;
        }

        unsigned int stride = inline_data ? inline_stride : attr->stride;
        const uint8_t *data = bases[i] + (size_t)element * stride;
        read_attribute(attr, data, vertex->a[i]);
    }
}

static unsigned int prepare_inline_array_offsets(PGRAPHState *pg)
{
    unsigned int offset = 0;
    for (int i = 0; i < NV2A_VERTEXSHADER_ATTRIBUTES; i++) {
        VertexAttribute *attr = &pg->vertex_attributes[i];
        if (attr->count == 0) {
            continue;
        }
        offset = ROUND_UP(offset, attr->size);
        attr->inline_array_offset = offset;
        offset += attr->size * attr->count;
        offset = ROUND_UP(offset, attr->size);
    }
    return offset;
}

static uint32_t *element_sequence(const XGridDrawData *data,
                                  unsigned int *count)
{
    if (data->indexed && data->indices) {
        *count = data->index_count;
        return data->indices;
    }

    *count = data->vertex_count;
    uint32_t *sequence =
        g_malloc_n(MAX(data->vertex_count, 1), sizeof(uint32_t));
    for (unsigned int i = 0; i < data->vertex_count; i++) {
        sequence[i] = i;
    }
    return sequence;
}

static uint32_t *triangle_indices(unsigned int triangles)
{
    return g_malloc_n(MAX(triangles * 3, 1), sizeof(uint32_t));
}

static void expand_primitive(PGRAPHState *pg, XGridDrawData *out)
{
    unsigned int count;
    uint32_t *sequence = element_sequence(out, &count);
    bool sequence_is_output = sequence == out->indices;
    uint32_t control_3 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_3);
    bool first_provoking =
        GET_MASK(control_3, NV_PGRAPH_CONTROL_3_PROVOKING_VERTEX) ==
        NV_PGRAPH_CONTROL_3_PROVOKING_VERTEX_FIRST;
    uint32_t polygon_mode =
        GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_SETUPRASTER),
                 NV_PGRAPH_SETUPRASTER_FRONTFACEMODE);

    uint32_t *indices = NULL;
    unsigned int index_count = 0;
    bool replace_indices = false;

    switch (pg->primitive_mode) {
    case PRIM_TYPE_POINTS:
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_POINTLIST;
        break;
    case PRIM_TYPE_LINES:
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_LINELIST;
        break;
    case PRIM_TYPE_LINE_STRIP:
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP;
        break;
    case PRIM_TYPE_LINE_LOOP:
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_LINELIST;
        if (count >= 2) {
            index_count = count * 2;
            indices = g_malloc_n(index_count, sizeof(uint32_t));
            for (unsigned int i = 0; i < count; i++) {
                indices[i * 2 + 0] = sequence[i];
                indices[i * 2 + 1] = sequence[(i + 1) % count];
            }
            replace_indices = true;
        }
        break;
    case PRIM_TYPE_TRIANGLES: {
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
        unsigned int triangles = count / 3;
        if (triangles) {
            index_count = triangles * 3;
            indices = triangle_indices(triangles);
            for (unsigned int t = 0; t < triangles; t++) {
                uint32_t a = sequence[t * 3 + 0];
                uint32_t b = sequence[t * 3 + 1];
                uint32_t c = sequence[t * 3 + 2];
                indices[t * 3 + 0] = first_provoking ? a : c;
                indices[t * 3 + 1] = first_provoking ? b : a;
                indices[t * 3 + 2] = first_provoking ? c : b;
            }
            replace_indices = true;
        }
        break;
    }
    case PRIM_TYPE_TRIANGLE_STRIP: {
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
        unsigned int triangles = count >= 3 ? count - 2 : 0;
        if (triangles) {
            index_count = triangles * 3;
            indices = triangle_indices(triangles);
            for (unsigned int t = 0; t < triangles; t++) {
                uint32_t a, b, c;
                if ((t & 1) == 0) {
                    a = sequence[t + 0];
                    b = sequence[t + 1];
                    c = sequence[t + 2];
                } else {
                    a = sequence[t + 1];
                    b = sequence[t + 0];
                    c = sequence[t + 2];
                }
                if (first_provoking) {
                    indices[t * 3 + 0] = a;
                    indices[t * 3 + 1] = b;
                    indices[t * 3 + 2] = c;
                } else if ((t & 1) == 0) {
                    indices[t * 3 + 0] = c;
                    indices[t * 3 + 1] = a;
                    indices[t * 3 + 2] = b;
                } else {
                    indices[t * 3 + 0] = b;
                    indices[t * 3 + 1] = c;
                    indices[t * 3 + 2] = a;
                }
            }
            replace_indices = true;
        }
        break;
    }
    case PRIM_TYPE_TRIANGLE_FAN: {
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
        unsigned int triangles = count >= 3 ? count - 2 : 0;
        if (triangles) {
            index_count = triangles * 3;
            indices = triangle_indices(triangles);
            for (unsigned int t = 0; t < triangles; t++) {
                uint32_t a = sequence[0];
                uint32_t b = sequence[t + 1];
                uint32_t c = sequence[t + 2];
                indices[t * 3 + 0] = first_provoking ? a : b;
                indices[t * 3 + 1] = first_provoking ? b : c;
                indices[t * 3 + 2] = first_provoking ? c : a;
            }
            replace_indices = true;
        }
        break;
    }
    case PRIM_TYPE_QUADS:
    case PRIM_TYPE_QUAD_STRIP: {
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
        unsigned int quads;
        unsigned int stride;
        if (pg->primitive_mode == PRIM_TYPE_QUADS) {
            quads = count / 4;
            stride = 4;
        } else {
            quads = count >= 4 ? (count - 2) / 2 : 0;
            stride = 2;
        }
        if (quads) {
            index_count = quads * 6;
            indices = g_malloc_n(index_count, sizeof(uint32_t));
            for (unsigned int q = 0; q < quads; q++) {
                unsigned int offset = stride * q;
                uint32_t v0 = sequence[offset + 0];
                uint32_t v1 = sequence[offset + 1];
                uint32_t v2 = sequence[offset + 2];
                uint32_t v3 = sequence[offset + 3];
                indices[q * 6 + 0] = v3;
                indices[q * 6 + 1] = v0;
                indices[q * 6 + 2] = v1;
                indices[q * 6 + 3] = v3;
                indices[q * 6 + 4] = v1;
                indices[q * 6 + 5] = v2;
            }
            replace_indices = true;
        }
        break;
    }
    case PRIM_TYPE_POLYGON:
        if (polygon_mode == NV_PGRAPH_SETUPRASTER_FRONTFACEMODE_LINE) {
            out->topology = D3D11_PRIMITIVE_TOPOLOGY_LINELIST;
            if (count >= 2) {
                index_count = count * 2;
                indices = g_malloc_n(index_count, sizeof(uint32_t));
                for (unsigned int i = 0; i < count; i++) {
                    indices[i * 2 + 0] = sequence[i];
                    indices[i * 2 + 1] = sequence[(i + 1) % count];
                }
                replace_indices = true;
            }
        } else {
            out->topology = D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST;
            unsigned int triangles = count >= 3 ? count - 2 : 0;
            if (triangles) {
                index_count = triangles * 3;
                indices = triangle_indices(triangles);
                for (unsigned int t = 0; t < triangles; t++) {
                    uint32_t a = sequence[0];
                    uint32_t b = sequence[t + 1];
                    uint32_t c = sequence[t + 2];
                    indices[t * 3 + 0] = c;
                    indices[t * 3 + 1] = a;
                    indices[t * 3 + 2] = b;
                }
                replace_indices = true;
            }
        }
        break;
    default:
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;
        break;
    }

    if (replace_indices) {
        g_free(out->indices);
        out->indices = indices;
        out->index_count = index_count;
        out->indexed = index_count > 0;
        if (!sequence_is_output) {
            g_free(sequence);
        }
    } else if (!sequence_is_output) {
        g_free(sequence);
    }

    if (polygon_mode == NV_PGRAPH_SETUPRASTER_FRONTFACEMODE_POINT &&
        out->topology != D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED) {
        out->topology = D3D11_PRIMITIVE_TOPOLOGY_POINTLIST;
        out->indexed = false;
        out->index_count = 0;
        g_free(out->indices);
        out->indices = NULL;
    }
}
bool pgraph_d3d11_prepare_vertices(NV2AState *d, XGridDrawData *out)
{
    PGRAPHState *pg = &d->pgraph;
    const uint8_t *bases[NV2A_VERTEXSHADER_ATTRIBUTES] = { 0 };
    unsigned int count = 0;
    unsigned int min_element = 0;
    unsigned int inline_stride = 0;
    bool inline_data = false;

    memset(out, 0, sizeof(*out));

    if (pg->draw_arrays_length) {
        min_element = pg->draw_arrays_min_start;
        count = pg->draw_arrays_max_count - pg->draw_arrays_min_start;
        out->use_draw_arrays = true;
        out->base_index = min_element;
    } else if (pg->inline_elements_length) {
        min_element = UINT32_MAX;
        unsigned int max_element = 0;
        for (unsigned int i = 0; i < pg->inline_elements_length; i++) {
            min_element = MIN(min_element, pg->inline_elements[i]);
            max_element = MAX(max_element, pg->inline_elements[i]);
        }
        if (min_element == UINT32_MAX) {
            return false;
        }
        count = max_element - min_element + 1;
        out->base_index = min_element;
    } else if (pg->inline_buffer_length) {
        count = pg->inline_buffer_length;
        out->base_index = 0;
    } else if (pg->inline_array_length) {
        inline_data = true;
        inline_stride = prepare_inline_array_offsets(pg);
        if (inline_stride == 0) {
            return false;
        }
        count = pg->inline_array_length * 4 / inline_stride;
        out->base_index = 0;
        for (int i = 0; i < NV2A_VERTEXSHADER_ATTRIBUTES; i++) {
            VertexAttribute *attr = &pg->vertex_attributes[i];
            if (attr->count) {
                bases[i] = (const uint8_t *)pg->inline_array +
                           attr->inline_array_offset;
            }
        }
    } else {
        return false;
    }

    if (!count) {
        return false;
    }

    if (!inline_data && !pg->inline_buffer_length) {
        for (int i = 0; i < NV2A_VERTEXSHADER_ATTRIBUTES; i++) {
            VertexAttribute *attr = &pg->vertex_attributes[i];
            if (!attr->count) {
                continue;
            }
            hwaddr dma_len;
            uint8_t *attr_data = (uint8_t *)nv_dma_map(
                d, attr->dma_select ? pg->dma_vertex_b : pg->dma_vertex_a,
                &dma_len);
            if (!attr_data || attr->offset >= dma_len) {
                bases[i] = NULL;
                continue;
            }
            bases[i] = attr_data + attr->offset +
                       (size_t)min_element * attr->stride;
        }
    }

    out->vertices = g_malloc_n(count, sizeof(XGridVertex));
    out->vertex_count = count;

    for (unsigned int i = 0; i < count; i++) {
        fetch_vertex(pg, i, bases, inline_stride, inline_data,
                     pg->inline_buffer_length != 0, &out->vertices[i]);
    }

    if (pg->inline_buffer_length) {
        for (int i = 0; i < NV2A_VERTEXSHADER_ATTRIBUTES; i++) {
            VertexAttribute *attr = &pg->vertex_attributes[i];
            if (attr->inline_buffer_populated) {
                memcpy(attr->inline_value,
                       attr->inline_buffer +
                           (pg->inline_buffer_length - 1) * 4,
                       sizeof(attr->inline_value));
                attr->inline_buffer_populated = false;
            }
        }
    }

    if (pg->draw_arrays_length) {
        out->vertex_count = count;
        out->base_index = min_element;

        /* A triangle list can be concatenated across multiple DRAW_ARRAYS
         * calls; each range is a whole number of triangles. Strip/fan ranges
         * are separate primitives and are expanded from the fetched span. */
        if (pg->draw_arrays_length > 1 &&
            pg->primitive_mode == PRIM_TYPE_TRIANGLES) {
            unsigned int total = 0;
            for (unsigned int i = 0; i < pg->draw_arrays_length; i++) {
                total += pg->draw_arrays_count[i];
            }
            out->indices = g_malloc_n(MAX(total, 1), sizeof(uint32_t));
            unsigned int index = 0;
            for (unsigned int i = 0; i < pg->draw_arrays_length; i++) {
                unsigned int start = pg->draw_arrays_start[i];
                for (unsigned int j = 0; j < pg->draw_arrays_count[i]; j++) {
                    int relative = (int)(start + j) - (int)min_element;
                    if (relative >= 0 && relative < (int)count) {
                        out->indices[index++] = (uint32_t)relative;
                    }
                }
            }
            out->index_count = index;
            out->indexed = index > 0;
        }
    } else if (pg->inline_elements_length) {
        out->indices = g_malloc_n(pg->inline_elements_length,
                                  sizeof(uint32_t));
        out->index_count = pg->inline_elements_length;
        for (unsigned int i = 0; i < pg->inline_elements_length; i++) {
            out->indices[i] = pg->inline_elements[i] - min_element;
        }
        out->indexed = true;
    }

    expand_primitive(pg, out);

    if (out->topology == D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED) {
        pgraph_d3d11_free_draw_data(out);
        return false;
    }

    return true;
}

void pgraph_d3d11_free_draw_data(XGridDrawData *data)
{
    g_free(data->vertices);
    g_free(data->indices);
    memset(data, 0, sizeof(*data));
}
