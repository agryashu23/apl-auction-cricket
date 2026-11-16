"""
The fielders' (and the pace keeper's) team cap for make_gear.py, on the head bone:

    cap_crown   six panels shrink-wrapped to the scalp: the outermost head of the
                cast (tools/gear/head_hull.json, from fit_heads.py) + 6 mm, so it
                follows the skull instead of standing off it like a helmet; the rim
                tilts — forehead at the front, above the ears, low on the nape
    cap_peak    a curved peak, ~7 cm at the middle, a little down and rounded across
    cap_badge   a small square on the front panel for the team crest (slot 3, UV 0-1)
    cap_seams   the button on top (dark)

UVs (round 8, the in-game cloth texture CricketCast.CapCloth): the crown takes
v 0-0.5 (u round from the front, v from the button down to the rim) so the six
panel seams, eyelets and the rim stitching are painted, not modelled; the peak
takes v 0.5-1 (u across, v from the crown out to the tip) for its rows of
stitching. Without them the plain dome read as a hard hat in close-ups.
"""
import json
import math
import os

from gearlib import V, Part, TEAM, DARK

HULL = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "head_hull.json")))
C = V(*HULL["centre"])
STEP_P, STEP_L = HULL["phis"][1] - HULL["phis"][0], HULL["lams"][1] - HULL["lams"][0]
GAP = 0.005


def _r(phi, lam):
    """Scalp radius from C at (phi from the top, lam round from the front), bilinear over the grid."""
    lam %= 360
    p0 = min(int(phi // STEP_P) * STEP_P, HULL["phis"][-2])
    l0 = int(lam // STEP_L) * STEP_L
    tp, tl = (phi - p0) / STEP_P, (lam - l0) / STEP_L

    def at(p, l):
        v = HULL["radius"].get(f"{p},{l % 360}")
        if v is None:  # a hole in the scan: the ring's average
            ring = [HULL["radius"].get(f"{p},{x}") for x in HULL["lams"]]
            ring = [x for x in ring if x]
            v = sum(ring) / len(ring) if ring else 0.11
        return v

    a = at(p0, l0) * (1 - tl) + at(p0, l0 + STEP_L) * tl
    b = at(p0 + STEP_P, l0) * (1 - tl) + at(p0 + STEP_P, l0 + STEP_L) * tl
    return a * (1 - tp) + b * tp


def _dir(phi, lam):
    p, l = math.radians(phi), math.radians(lam)
    return V(math.sin(p) * math.sin(l), math.cos(p), math.sin(p) * math.cos(l))


def rim_phi(lam):
    """Where the cap stops: the forehead at the front, above the ears, low on the nape."""
    a = abs(((lam + 180) % 360) - 180) / 180  # 0 front .. 1 back
    return 80 + 12 * math.sin(a * math.pi / 2) ** 2 + 16 * a ** 3


def surface(H, phi, lam, gap=GAP):
    return H + C + _dir(phi, lam) * (_r(phi, lam) + gap)


def panel(H, phi, lam):
    """The crown's cloth: six soft panels (seams at lam = 0, 60 ...) puffed 3 mm between the seams, none at the button."""
    puff = 0.003 * abs(math.sin(math.radians(3 * lam))) * math.sin(math.radians(min(phi, 90)))
    return surface(H, phi, lam, GAP + puff)


def cap(H):
    crown = Part("cap_crown", TEAM)
    seams = Part("cap_seams", DARK)
    nj, ni = 36, 9
    out = lambda c: c - (H + C)  # noqa: E731
    # From the back round (u 0.5 = the front): one extra column (j = nj, u = 1) so the texture
    # doesn't wrap backwards, and that split falls at the back where the strap is, not the face.
    rows = [[crown.vert(panel(H, 2 + (rim_phi(l) - 2) * i / ni, l), "Head", (j / nj, 0.5 * (0.04 + 0.96 * i / ni)))
             for j, l in [(j, 360 * j / nj - 180) for j in range(nj + 1)]] for i in range(ni + 1)]
    for j in range(nj):
        top = crown.vert(surface(H, 0, 0), "Head", ((j + 0.5) / nj, 0.0))
        crown.tri(top, rows[0][j], rows[0][j + 1], out)
    for i in range(ni):
        for j in range(nj):
            crown.quad(rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1], out)
    # The button on top (panel seams were dropped: at game distance they read as cracks).
    btn = surface(H, 0, 0, GAP + 0.001)
    ring = [seams.vert(btn + V(0.007 * math.sin(t), -0.001, 0.007 * math.cos(t)), "Head") for t in [k * math.pi / 3 for k in range(6)]]
    tip = seams.vert(btn + V(0, 0.004, 0), "Head")
    for k in range(6):
        seams.tri(tip, ring[k], ring[(k + 1) % 6], out=V(0, 1, 0))

    # The peak, off the front rim.
    peak = Part("cap_peak", TEAM)
    lams = [-64 + 128 * k / 12 for k in range(13)]
    tops, bots = [], []
    for lam in lams:
        base = surface(H, rim_phi(lam) - 1, lam)
        l = math.radians(lam)
        fwd = V(math.sin(l) * 0.3, 0, math.cos(l)).normalized()
        across = (lam / 64) ** 2
        length = 0.077 * (1 - 0.7 * across)
        # A cricket cap's peak curves down at the sides more than a baseball cap's.
        drop = length * 0.3 + 0.02 * across
        mid = base + fwd * length * 0.55 - V(0, drop * 0.4, 0)
        tip = base + fwd * length - V(0, drop, 0)
        u = (lam + 64) / 128
        tops.append([peak.vert(p, "Head", (u, 0.5 + 0.5 * f)) for p, f in ((base, 0.0), (mid, 0.55), (tip, 1.0))])
        bots.append([peak.vert(p - V(0, 0.0035, 0), "Head", (u, 0.5 + 0.5 * f)) for p, f in ((base, 0.0), (mid, 0.55), (tip, 1.0))])
    for k in range(len(lams) - 1):
        for r in range(2):
            peak.quad(tops[k][r], tops[k][r + 1], tops[k + 1][r + 1], tops[k + 1][r], out=V(0, 1, 0))
            peak.quad(bots[k][r], bots[k][r + 1], bots[k + 1][r + 1], bots[k + 1][r], out=V(0, -1, 0))
        peak.quad(tops[k][2], bots[k][2], bots[k + 1][2], tops[k + 1][2], out=V(0, 0, 1))

    # The crest: a small square on the front panel, a hair proud of it.
    badge = Part("cap_badge", 3)
    c0 = surface(H, 50, 0, GAP + 0.0015)
    n = (c0 - H - C).normalized()
    u = (V(0, 1, 0) - n * n.y).normalized() * 0.016
    w = n.cross(u).normalized() * 0.016  # toward the viewer's right as he faces the player (body -x)
    q = [badge.vert(c0 - w - u, "Head", (0, 0)), badge.vert(c0 - w + u, "Head", (0, 1)),
         badge.vert(c0 + w + u, "Head", (1, 1)), badge.vert(c0 + w - u, "Head", (1, 0))]
    badge.quad(q[0], q[1], q[2], q[3], out=n)
    return [crown, seams, peak, badge]
