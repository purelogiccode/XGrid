#!/usr/bin/env python3
"""Report picture content, or how different two pictures are.

One image: prints the mean channel value, the colour count and the black share
so a caller can tell "a rendered frame" from "an empty canvas".

Two images: prints the per-pixel mean absolute difference per channel, the peak
difference and the share of pixels that differ.

Both must have the same size when two are given. `--crop L,T,R,B` takes
fractions of the width/height (for example `0.2,0.2,0.8,0.85` for the display
area of a shell window).

Usage:
    python scripts/frame-diff.py <image> [--crop L,T,R,B]
    python scripts/frame-diff.py <a.png> <b.png> [--crop L,T,R,B]
"""

import argparse
import sys

from PIL import Image, ImageChops


def load(path, crop):
    image = Image.open(path).convert("RGB")
    if crop is not None:
        left, top, right, bottom = crop
        width, height = image.size
        image = image.crop(
            (int(width * left), int(height * top), int(width * right), int(height * bottom))
        )
    return image


def describe(image):
    data = image.tobytes()
    total = len(data) // 3
    sums = [0, 0, 0]
    colours = set()
    black = 0
    for index in range(0, len(data), 3):
        red, green, blue = data[index], data[index + 1], data[index + 2]
        sums[0] += red
        sums[1] += green
        sums[2] += blue
        colours.add((red << 16) | (green << 8) | blue)
        if red == 0 and green == 0 and blue == 0:
            black += 1

    means = [value / total for value in sums]
    print(
        "mean=({:.2f},{:.2f},{:.2f}) colours={} blackPct={:.2f}".format(
            means[0], means[1], means[2], len(colours), black * 100.0 / total
        )
    )


def compare(a, b):
    diff = ImageChops.difference(a, b)
    data = diff.tobytes()
    total = len(data) // 3
    channels = [0, 0, 0]
    peak = 0
    changed = 0
    for index in range(0, len(data), 3):
        red, green, blue = data[index], data[index + 1], data[index + 2]
        channels[0] += red
        channels[1] += green
        channels[2] += blue
        value = max(red, green, blue)
        if value > peak:
            peak = value
        if value > 2:
            changed += 1

    means = [value / total for value in channels]
    print(
        "dmean=({:.2f},{:.2f},{:.2f}) peak={} changed={:.2f}%".format(
            means[0], means[1], means[2], peak, changed * 100.0 / total
        )
    )


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", nargs="+")
    parser.add_argument("--crop", default="")
    args = parser.parse_args(argv[1:])

    crop = None
    if args.crop:
        values = [float(value) for value in args.crop.split(",")]
        if len(values) != 4:
            print("--crop takes four fractions: left,top,right,bottom")
            return 2
        crop = values

    if len(args.images) == 1:
        describe(load(args.images[0], crop))
        return 0

    if len(args.images) != 2:
        print(__doc__.strip())
        return 2

    a = load(args.images[0], crop)
    b = load(args.images[1], crop)
    if a.size != b.size:
        print(f"size mismatch: {a.size} vs {b.size}")
        return 2

    compare(a, b)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
