/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - occlusion reports
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
#include "renderer.h"
#include "reports.h"

static void release_queries(ID3D11Query **queries, unsigned int count)
{
    for (unsigned int i = 0; i < count; i++) {
        if (queries[i]) {
            ID3D11Query_Release(queries[i]);
        }
    }
    g_free(queries);
}

void pgraph_d3d11_begin_zpass_query(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (!pg->zpass_pixel_count_enable || r->zpass_query_active) {
        return;
    }

    static bool logged_first_query;
    if (!logged_first_query) {
        logged_first_query = true;
        fprintf(stderr, "nv2a: D3D11 occlusion queries active\n");
    }

    D3D11_QUERY_DESC desc = {
        .Query = D3D11_QUERY_OCCLUSION,
        .MiscFlags = 0,
    };
    ID3D11Query *query = NULL;
    HRESULT hr = ID3D11Device_CreateQuery(r->device, &desc, &query);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 occlusion query creation failed "
                        "(hr=0x%08lx)\n", (unsigned long)hr);
        return;
    }

    ID3D11DeviceContext_Begin(r->context, (ID3D11Asynchronous *)query);
    r->zpass_queries = g_realloc_n(r->zpass_queries,
                                   r->zpass_query_count + 1,
                                   sizeof(*r->zpass_queries));
    r->zpass_queries[r->zpass_query_count++] = query;
    r->zpass_query_active = true;
}

void pgraph_d3d11_end_zpass_query(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (!r->zpass_query_active || r->zpass_query_count == 0) {
        return;
    }

    ID3D11Query *query = r->zpass_queries[r->zpass_query_count - 1];
    ID3D11DeviceContext_End(r->context, (ID3D11Asynchronous *)query);
    r->zpass_query_active = false;
}

void pgraph_d3d11_clear_report_value(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->zpass_query_active && r->zpass_query_count) {
        ID3D11DeviceContext_End(
            r->context,
            (ID3D11Asynchronous *)r->zpass_queries[r->zpass_query_count - 1]);
        r->zpass_query_active = false;
    }

    release_queries(r->zpass_queries, r->zpass_query_count);
    r->zpass_queries = NULL;
    r->zpass_query_count = 0;

    D3D11QueryReport *report = g_malloc0(sizeof(*report));
    report->clear = true;
    QSIMPLEQ_INSERT_TAIL(&r->report_queue, report, entry);
}

void pgraph_d3d11_get_report(NV2AState *d, uint32_t parameter)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11QueryReport *report = g_malloc0(sizeof(*report));
    report->parameter = parameter;
    report->query_count = r->zpass_query_count;
    report->queries = r->zpass_queries;
    QSIMPLEQ_INSERT_TAIL(&r->report_queue, report, entry);

    r->zpass_queries = NULL;
    r->zpass_query_count = 0;
}

void pgraph_d3d11_process_pending_reports(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    D3D11QueryReport *report, *next;

    QSIMPLEQ_FOREACH_SAFE(report, &r->report_queue, entry, next) {
        if (report->clear) {
            r->zpass_pixel_count_result = 0;
        } else {
            uint8_t type = GET_MASK(report->parameter, NV097_GET_REPORT_TYPE);
            assert(type == NV097_GET_REPORT_TYPE_ZPASS_PIXEL_CNT);

            uint64_t result = 0;
            for (unsigned int i = 0; i < report->query_count; i++) {
                UINT64 query_result = 0;
                HRESULT hr = ID3D11DeviceContext_GetData(
                    r->context, (ID3D11Asynchronous *)report->queries[i],
                    &query_result, sizeof(query_result), 0);
                if (SUCCEEDED(hr)) {
                    result += query_result;
                }
            }

            result /= (uint64_t)pg->surface_scale_factor *
                      pg->surface_scale_factor;
            r->zpass_pixel_count_result += (uint32_t)result;
            pgraph_write_zpass_pixel_cnt_report(
                d, report->parameter, r->zpass_pixel_count_result);
        }

        release_queries(report->queries, report->query_count);
        QSIMPLEQ_REMOVE_HEAD(&r->report_queue, entry);
        g_free(report);
    }
}

void pgraph_d3d11_init_reports(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    QSIMPLEQ_INIT(&r->report_queue);
    r->zpass_queries = NULL;
    r->zpass_query_count = 0;
    r->zpass_query_active = false;
    r->zpass_pixel_count_result = 0;
}

void pgraph_d3d11_finalize_reports(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    D3D11QueryReport *report, *next;

    QSIMPLEQ_FOREACH_SAFE(report, &r->report_queue, entry, next) {
        release_queries(report->queries, report->query_count);
        QSIMPLEQ_REMOVE_HEAD(&r->report_queue, entry);
        g_free(report);
    }

    release_queries(r->zpass_queries, r->zpass_query_count);
    r->zpass_queries = NULL;
    r->zpass_query_count = 0;
    r->zpass_query_active = false;
}
