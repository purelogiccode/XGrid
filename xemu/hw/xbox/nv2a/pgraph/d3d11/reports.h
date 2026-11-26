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

#ifndef HW_XBOX_NV2A_PGRAPH_D3D11_REPORTS_H
#define HW_XBOX_NV2A_PGRAPH_D3D11_REPORTS_H

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"

void pgraph_d3d11_init_reports(NV2AState *d);
void pgraph_d3d11_finalize_reports(PGRAPHState *pg);
void pgraph_d3d11_get_report(NV2AState *d, uint32_t parameter);
void pgraph_d3d11_clear_report_value(NV2AState *d);
void pgraph_d3d11_process_pending_reports(NV2AState *d);
void pgraph_d3d11_begin_zpass_query(PGRAPHState *pg);
void pgraph_d3d11_end_zpass_query(PGRAPHState *pg);

#endif /* HW_XBOX_NV2A_PGRAPH_D3D11_REPORTS_H */
