#!/usr/bin/env python3
"""Compare an XGrid frame with a reference screenshot (M-T oracle policy).

The retail oracle is a window screenshot (xemu 0.8.139) and the XGrid frame is
a BMP (640x480). They are never compared pixel-exact: this tool reports mean
channel values, colour count, black share, an 8-bucket luminance histogram and
the mean/histogram deltas after normalising both images to a common size.

The xemu debug overlay sits in the top-left of the window; `--crop-top` removes
a fraction of the height from both images before the statistics (the default
0.25 drops the overlay band). Use `--size WxH` to pick the comparison size.

Usage:
    python scripts/frame-compare.py <reference.png> <frame.bmp> [--crop-top 0.25] [--size 320x240]
"""

import argparse
import sys

from PIL import Image


def stats(path, crop_top, size):
    image = Image.open(path).convert("RGB")
    if crop_top > 0:
        width, height = image.size
        image = image.crop((0, int(height * crop_top), width, height))
    if size is not None:
        image = image.resize(size, Image.BILINEAR)

    pixels = list(image.getdata())
    total = len(pixels)
    sums = [0, 0, 0]
    buckets = [0] * 8
    colours = set()
    black = 0
    for red, green, blue in pixels:
        sums[0] += red
        sums[1] += green
        sums[2] += blue
        luminance = (red * 299 + green * 587 + blue * 114) // 1000
        buckets[min(luminance >> 5, 7)] += 1
        colours.add((red << 16) | (green << 8) | blue)
        if (red | green | blue) == 0:
            black += 1

    return {
        "size": image.size,
        "mean": [value / total for value in sums],
        "colours": len(colours),
        "blackPct": black * 100.0 / total,
        "histogram": [count * 100.0 / total for count in buckets],
    }


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference")
    parser.add_argument("frame")
    parser.add_argument("--crop-top", type=float, default=0.25)
    parser.add_argument("--size", default="320x240")
    args = parser.parse_args(argv[1:])

    width, height = (int(v) for v in args.size.lower().split("x"))
    ref = stats(args.reference, args.crop_top, (width, height))
    frame = stats(args.frame, args.crop_top, (width, height))

    dmean = [abs(a - b) for a, b in zip(ref["mean"], frame["mean"])]
    dhist = sum(abs(a - b) for a, b in zip(ref["histogram"], frame["histogram"]))

    print(f"reference {args.reference}: {ref['size'][0]}x{ref['size'][1]} "
          f"mean=({ref['mean'][0]:.1f}, {ref['mean'][1]:.1f}, {ref['mean'][2]:.1f}) "
          f"colours={ref['colours']} black={ref['blackPct']:.1f}%")
    print(f"frame     {args.frame}: {frame['size'][0]}x{frame['size'][1]} "
          f"mean=({frame['mean'][0]:.1f}, {frame['mean'][1]:.1f}, {frame['mean'][2]:.1f}) "
          f"colours={frame['colours']} black={frame['blackPct']:.1f}%")
    print(f"COMPARE dmean=({dmean[0]:.1f}, {dmean[1]:.1f}, {dmean[2]:.1f}) "
          f"histL1={dhist:.1f} cropTop={args.crop_top:.2f} size={width}x{height}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
