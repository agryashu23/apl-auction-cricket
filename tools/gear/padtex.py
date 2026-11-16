"""
pad.png for the pads (u across the pad, v = height / 0.7 m): white leather with
the cane bolsters shaded into their grooves, stitch lines down the bolsters and
the wings, and the knee rolls shaded across — the detail that makes a white pad
read as a pad under flat stadium light. Shared by batting and keeping pads.
"""
import math

import gearlib as G

W, H = 128, 256
TF = 12 / 18                     # the face's share of the width (pads.py: nf / cols)
SHIN_TOP, KNEE_TOP = 0.45 / 0.7, 0.575 / 0.7


def shade(u, v):
    x = (u - 0.5) * 2
    if abs(x) <= TF:
        tau = x / TF
        if SHIN_TOP < v < KNEE_TOP:
            f = (v - SHIN_TOP) / (KNEE_TOP - SHIN_TOP)
            s = 0.78 + 0.22 * math.sin(3 * math.pi * f) ** 2
            if abs(math.sin(3 * math.pi * f)) < 0.08:
                s *= 0.85                      # the seam between rolls
            return s
        bump = abs(math.cos(1.5 * math.pi * tau)) ** 0.6
        s = 0.74 + 0.26 * bump
        # Stitches: dashed down the middle of each bolster, solid in the grooves.
        for c in (-2 / 3, 0, 2 / 3):
            if abs(tau - c) < 0.012 and int(v * 90) % 2 == 0:
                s *= 0.9
        for c in (-1 / 3, 1 / 3):
            if abs(tau - c) < 0.02:
                s *= 0.82
        return s
    # Wings: plain leather, two stitch lines, a little darker toward the back edge.
    w = (abs(x) - TF) / (1 - TF)
    s = 0.95 - 0.12 * w
    if (abs(w - 0.35) < 0.025 or abs(w - 0.7) < 0.025) and int(v * 90) % 2 == 0:
        s *= 0.88
    return s


def write(path):
    rows = []
    for y in range(H):
        v = 1 - (y + 0.5) / H
        row = []
        for x in range(W):
            s = shade((x + 0.5) / W, v)
            row.append((int(250 * s), int(250 * s), int(246 * s), 255))
        rows.append(row)
    G.write_png(path, W, H, rows)
