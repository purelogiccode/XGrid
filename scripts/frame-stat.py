#!/usr/bin/env python3
"""Colour statistics for a presented frame (BMP from XGrid, PNG from a reference).

The Track E corpus compares XGrid's frames against the reference screenshots the
xgu repository ships (decisions 207i/208): mean channel values plus an 8-bucket
luminance histogram separate "renders something" from "renders the right thing"
without needing a pixel-exact oracle.

Usage:
    python scripts/frame-stat.py <image> [<image> ...]
"""

import sys

from PIL import Image


def stats(path):
    image = Image.open(path).convert("RGB")
    width, height = image.size
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

    mean = [value / total for value in sums]
    histogram = [count * 100.0 / total for count in buckets]
    return {
        "path": path,
        "size": (width, height),
        "mean": mean,
        "colours": len(colours),
        "blackPct": black * 100.0 / total,
        "histogram": histogram,
    }


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    results = [stats(path) for path in argv[1:]]
    for result in results:
        print(f"{result['path']}")
        print(
            "  %dx%d mean=(%.1f, %.1f, %.1f) colours=%d black=%.1f%%"
            % (
                result["size"][0],
                result["size"][1],
                result["mean"][0],
                result["mean"][1],
                result["mean"][2],
                result["colours"],
                result["blackPct"],
            )
        )
        print("  luminance: " + " ".join("%.1f%%" % value for value in result["histogram"]))

    if len(results) == 2:
        left, right = results
        delta = [abs(a - b) for a, b in zip(left["mean"], right["mean"])]
        print(
            "  |mean delta| = (%.1f, %.1f, %.1f), histogram L1 = %.1f"
            % (delta[0], delta[1], delta[2],
               sum(abs(a - b) for a, b in zip(left["histogram"], right["histogram"])))
        )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
