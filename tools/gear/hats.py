"""
Headwear for make_gear.py, on the head bone (Unity body frame, metres):

    hat_crown, hat_brim      a wide-brim hat (fielders' sun hat; the umpire's in cream)
    hat_band                 the band round the hat's crown (dark)

It sits on the head mesh with the hair cards hidden (as under the helmet). The cap is cap.py.
"""
import math

import cap
from gearlib import V, Part, TEAM, DARK


def _dome(part, H, centre, rx, ry, rzf, rzb, phi_end, nj=24, ni=7, grow_end=0.0):
    """A cap of an ellipsoid from the top down to phi_end (deg); returns the rim's points."""
    out = lambda c: c - (H + centre)  # noqa: E731
    rows = []
    for i in range(ni + 1):
        phi = math.radians(3 + (phi_end - 3) * i / ni)
        row = []
        for j in range(nj):
            lam = 2 * math.pi * j / nj
            s, c = math.sin(phi), math.cos(phi)
            rz = rzf if math.cos(lam) > 0 else rzb
            g = 1 + grow_end * (i / ni) ** 3
            p = H + centre + V(rx * s * math.sin(lam) * g, ry * c, rz * s * math.cos(lam) * g)
            row.append(part.vert(p, "Head"))
        rows.append(row)
    top = part.vert(H + centre + V(0, ry, 0), "Head")
    for j in range(nj):
        part.tri(top, rows[0][j], rows[0][(j + 1) % nj], out)
    for i in range(ni):
        for j in range(nj):
            j1 = (j + 1) % nj
            part.quad(rows[i][j], rows[i + 1][j], rows[i + 1][j1], rows[i][j1], out)
    return [part.v[k] for k in rows[-1]]


def _crown(part, H, nj=24, ni=7):
    """
    A soft crown on the skull hull (cap.py), 14 mm out and flattened on top like a
    cloth sun hat; the rim at the brow in front, above the ears, a little lower behind.
    Returns the rim's points.
    """
    flat = H.y + cap.C.y + 0.138
    out = lambda c: c - (H + cap.C)  # noqa: E731

    def pt(phi, lam):
        p = cap.surface(H, phi, lam, 0.014)
        if p.y > flat:
            p.y = flat + (p.y - flat) * 0.3
        return p

    def rim_phi(lam):
        a = abs(((lam + 180) % 360) - 180) / 180
        return 76 + 20 * a

    lams = [360 * j / nj for j in range(nj)]
    rows = [[part.vert(pt(2 + (rim_phi(l) - 2) * i / ni, l), "Head") for l in lams] for i in range(ni + 1)]
    top = part.vert(pt(0, 0), "Head")
    for j in range(nj):
        part.tri(top, rows[0][j], rows[0][(j + 1) % nj], out)
    for i in range(ni):
        for j in range(nj):
            j1 = (j + 1) % nj
            part.quad(rows[i][j], rows[i + 1][j], rows[i + 1][j1], rows[i][j1], out)
    return [part.v[k] for k in rows[-1]]


def hat(H):
    """A wide-brim hat: a soft crown, a band and a brim that turns down a little all round."""
    crown = Part("hat_crown", TEAM)
    centre = cap.C
    rim = _crown(crown, H)
    band = Part("hat_band", DARK)
    lo = [band.vert(p + (p - H - centre).normalized() * 0.0015, "Head") for p in rim]
    hi = [band.vert(p + (p - H - centre).normalized() * 0.0015 + V(0, 0.022, 0), "Head") for p in rim]
    n = len(rim)
    for j in range(n):
        j1 = (j + 1) % n
        band.quad(lo[j], hi[j], hi[j1], lo[j1], out=lambda c: V(c.x - H.x, 0, c.z - H.z))
    brim = Part("hat_brim", TEAM)
    rings = []
    for k, (grow, drop) in enumerate(((0.0, 0.0), (0.04, 0.006), (0.072, 0.016), (0.088, 0.026))):
        ring = []
        for p in rim:
            d = V(p.x - H.x, 0, p.z - H.z - centre.z).normalized()
            ring.append(p + d * grow - V(0, drop, 0))
        rings.append(ring)
    top = [[brim.vert(p, "Head") for p in ring] for ring in rings]
    bot = [[brim.vert(p - V(0, 0.004, 0), "Head") for p in ring] for ring in rings]
    for r in range(len(rings) - 1):
        for j in range(n):
            j1 = (j + 1) % n
            brim.quad(top[r][j], top[r + 1][j], top[r + 1][j1], top[r][j1], out=V(0, 1, 0))
            brim.quad(bot[r][j], bot[r + 1][j], bot[r + 1][j1], bot[r][j1], out=V(0, -1, 0))
    for j in range(n):
        j1 = (j + 1) % n
        brim.quad(top[-1][j], bot[-1][j], bot[-1][j1], top[-1][j1], out=lambda c: V(c.x - H.x, 0, c.z - H.z))
    return [crown, band, brim]
