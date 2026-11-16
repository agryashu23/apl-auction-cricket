"""
Batting / keeping pads for make_gear.py (left leg; right mirrored), measured
against the reference leg (sections in make_gear's probe):

    pad          the front: vertical cane bolsters on the shin, knee rolls, a
                 thigh flap; side wings round the calf. The knee rows lean from
                 the shin bone onto the thigh bone gradually, so the knee bends
                 like a sleeve instead of splitting. UV: u across, v up (pad.png).
    pad_trim     the edges rolled back in team colour
    pad_straps   three straps round the back of the calf (team / dark)
"""
import math

from gearlib import V, Part, TEAM, DARK

PAD = 4  # material slot: white with the canes and stitching texture (pad.png)
SHIN, THIGH = "LeftLowerLeg", "LeftUpperLeg"


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def _lerp_table(pts, y):
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        if y <= y1:
            return z0 + (z1 - z0) * max(0.0, (y - y0) / (y1 - y0))
    return pts[-1][1]


def pads(keeper=False):
    cx = -0.112
    shin_top, knee_top = 0.45, (0.575 if not keeper else 0.54)
    top = 0.67 if not keeper else 0.59
    rolls = 3 if not keeper else 2
    deep = 0.0 if not keeper else -0.012
    bol = 0.014 if not keeper else 0.009
    face_w, wing_r = (0.084 if not keeper else 0.074), 0.03
    nf, nw = 12, 3
    cols = nf + 2 * nw
    tf = nf / cols

    def zf(y):  # the face's front (before bolsters): clear of shin, knee and thigh
        return _lerp_table([(0.07, 0.106), (0.15, 0.07), (0.3, 0.085), (0.42, 0.108), (0.5, 0.128), (0.6, 0.142), (0.7, 0.152)], y) + deep

    def cz(y):
        return -0.024 + 0.03 * smooth(0.1, 0.55, y)

    def section(y, t, bolster, roll):
        w = face_w * (1 - 0.18 * smooth(knee_top, top, y)) * (1 - 0.3 * smooth(top - 0.05, top, y))
        if abs(t) <= tf:
            tau = t / tf
            bump = abs(math.cos(1.5 * math.pi * tau)) ** 0.6 * bolster + roll
            return V(cx + tau * w, y, zf(y) - 0.012 * tau * tau + bump)
        u = (abs(t) - tf) / (1 - tf)
        sgn = 1 if t > 0 else -1
        ang = min(u * 1.25, 1.0) * math.pi / 2
        back = max(u * 1.25 - 1.0, 0.0) / 0.25
        z = zf(y) - 0.012 - wing_r * (1 - math.cos(ang))
        z += (cz(y) - 0.012 - z) * back
        return V(cx + sgn * (w + wing_r * math.sin(ang)), y, z)

    # Rows: (y, bolster height, roll bulge, how far onto the thigh bone).
    rows = [(0.075 + (shin_top - 0.075) * k / 7, bol, 0.0, 0.0) for k in range(8)]
    for k in range(1, rolls * 2 + 1):
        f = k / (rolls * 2)
        rows.append((shin_top + (knee_top - shin_top) * f, 0.0, 0.017 * math.sin(math.pi * rolls * f) ** 2 + 0.006, 0.5 * smooth(0.2, 1.0, f)))
    for k in range(1, 4):
        rows.append((knee_top + (top - knee_top) * k / 3, bol * 0.75, 0.0, 0.5 + 0.5 * k / 3))

    def on(w):  # vertex bone: shin, thigh, or between
        if w <= 0:
            return SHIN, None, 0.0
        if w >= 1:
            return THIGH, None, 0.0
        return SHIN, THIGH, w

    pad = Part("pad" if not keeper else "pad", PAD)
    trim = Part("pad_trim", TEAM)
    grid = []
    for r, (y, b, roll, w) in enumerate(rows):
        row = []
        for c in range(cols + 1):
            t = -1 + 2 * c / cols
            yy = y + 0.09 * smooth(0.45, 1.0, abs(t)) if r == 0 else y  # the ankle cut-out
            b1, b2, w2 = on(w)
            row.append(pad.vert(section(yy, t, b, roll), b1, (c / cols, yy / 0.7), b2, w2))
        grid.append(row)
    out = lambda p: V(p.x - cx, 0, p.z - cz(p.y))  # noqa: E731
    for r in range(len(rows) - 1):
        for c in range(cols):
            pad.quad(grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1], out)

    # Trim: the wing edges and the top roll back on themselves.
    for c in (0, cols):
        sgn = 1 if c == cols else -1
        a = [trim.vert(pad.v[grid[r][c]] + V(0, 0, -0.001), *on(rows[r][3])[:1], (0, 0), *on(rows[r][3])[1:]) for r in range(len(rows))]
        bb = [trim.vert(pad.v[grid[r][c]] + V(-sgn * 0.011, 0, -0.004), *on(rows[r][3])[:1], (0, 0), *on(rows[r][3])[1:]) for r in range(len(rows))]
        for r in range(len(rows) - 1):
            trim.quad(a[r], a[r + 1], bb[r + 1], bb[r], out=V(sgn, 0, -0.3))
    a = [trim.vert(pad.v[grid[-1][c]] + V(0, 0.001, 0), THIGH) for c in range(cols + 1)]
    bb = [trim.vert(pad.v[grid[-1][c]] + V(0, -0.004, -0.012), THIGH) for c in range(cols + 1)]
    for c in range(cols):
        trim.quad(a[c], bb[c], bb[c + 1], a[c + 1], out=V(0, 1, 0.3))

    # Straps round the back of the calf (and the thigh), just off the skin.
    calf_back = [(0.1, -0.07), (0.15, -0.066), (0.21, -0.072), (0.3, -0.078), (0.39, -0.07), (0.45, -0.06), (0.5, -0.093), (0.7, -0.094)]
    straps = Part("pad_straps", DARK if keeper else TEAM)
    for sy in ([0.2, 0.38, 0.62] if not keeper else [0.2, 0.38]):
        bone = THIGH if sy > knee_top else SHIN
        z0 = cz(sy) - 0.012
        rx = face_w + wing_r - 0.004
        rz = z0 - (_lerp_table(calf_back, sy) - 0.01)
        pts = [V(cx + rx * math.cos(math.pi * (1 - k / 8)), sy, z0 - rz * math.sin(math.pi * (1 - k / 8))) for k in range(9)]
        lo = [straps.vert(p - V(0, 0.016, 0), bone) for p in pts]
        hi = [straps.vert(p + V(0, 0.016, 0), bone) for p in pts]
        for k in range(8):
            straps.quad(lo[k], hi[k], hi[k + 1], lo[k + 1], out=lambda c, z0=z0: V(c.x - cx, 0, c.z - z0 + 0.04))
    return [pad, trim, straps]
