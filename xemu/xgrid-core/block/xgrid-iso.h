/*
 * XDVDFS game-partition detection shared by the XGrid block drivers.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * A retail Xbox disc keeps a video partition in front of the game area. The
 * guest DVD drive expects the game volume at LBA 0, which is the shape of an
 * optimized XISO. These offsets locate the game partition in a full Redump
 * image (or a CHD/ZArchive built from one) so the drivers can expose it at
 * LBA 0 without extracting or rewriting the container.
 */
#ifndef XGRID_ISO_H
#define XGRID_ISO_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/** Reads exactly @size bytes at @offset; returns false on any short read. */
typedef bool (*XGridIsoReadFn)(void *opaque, uint64_t offset, void *buffer,
                               size_t size);

/**
 * Finds the byte offset of the XDVDFS game partition.
 *
 * Probes the known XISO/XGD layout offsets for the volume descriptor at
 * sector 32. Returns UINT64_MAX when no descriptor is found.
 */
uint64_t xgrid_iso_find_game_offset(XGridIsoReadFn read, void *opaque,
                                    uint64_t size);

#endif
