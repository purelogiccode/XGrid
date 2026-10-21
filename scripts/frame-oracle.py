#!/usr/bin/env python3
"""Compare XGrid frames against a reference screenshot (Track E oracle).

The xgu corpus ships 640x480 screenshots of each example (decisions 207i), and
some of those examples animate: `alpha_blending` ping-pongs a vertex-shader
transparency between 1.0 and 0.0 every 100 frames, so a frame captured at one
fixed step count shows whatever alpha that phase has. Comparing one frame
against the screenshot therefore says more about the phase than about the
renderer - measure a few step counts and keep the best match (decisions 208f).

Usage:
    python scripts/frame-oracle.py <reference.png> <frame.bmp> [<frame.bmp> ...]

Prints one line per frame (mean channel delta and 8-bucket luminance L1) and a
BEST line naming the closest frame.
"""

import sys

from PIL import Image


def stats(path):
    image = Image.open(path).convert("RGB")
    pixels = list(image.getdata())
    total = len(pixels)
    sums = [0, 0, 0]
    buckets = [0] * 8
    for red, green, blue in pixels:
        sums[0] += red
        sums[1] += green
        sums[2] += blue
        luminance = (red * 299 + green * 587 + blue * 114) // 1000
        buckets[min(luminance >> 5, 7)] += 1
    return [value / total for value in sums], [count * 100.0 / total for count in buckets]


def compare(reference, frame):
    (ref_mean, ref_hist), (frame_mean, frame_hist) = stats(reference), stats(frame)
    delta = [abs(a - b) for a, b in zip(ref_mean, frame_mean)]
    hist = sum(abs(a - b) for a, b in zip(ref_hist, frame_hist))
    return delta, hist


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2

    reference = argv[1]
    best = None
    for frame in argv[2:]:
        delta, hist = compare(reference, frame)
        score = sum(delta) + hist
        print(
            "  %-48s |dmean|=(%5.1f,%5.1f,%5.1f) histL1=%5.1f score=%6.1f"
            % (frame, delta[0], delta[1], delta[2], hist, score)
        )
        if best is None or score < best[0]:
            best = (score, frame)

    print("BEST %s (score %.1f)" % (best[1], best[0]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
