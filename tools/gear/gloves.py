"""
Batting gloves fitted to the Rocketbox hand (make_gear.py), left hand; the
right is mirrored. Everything is measured from the reference body's skin:

    glove        back-of-hand padding (the hand's skin extents + a few mm) under a
                 sausage-roll tube per finger running from the wrist over the
                 knuckle to the tip (blended across the joints so it curls with
                 the finger), a padded thumb
    glove_cuff   the wrist cuff, hand -> forearm blended so it bends with the
                 wrist instead of standing off it (team colour)
"""
import math

from gearlib import V, Part, WHITE, TEAM

FINGERS = ["Index", "Middle", "Ring", "Little"]
SEGS = ("Proximal", "Intermediate", "Distal")


def frame(bind):
    h = bind["LeftHand"]
    kn = sum((bind[f"Left{f}Proximal"] for f in FINGERS), V(0, 0, 0)) / 4
    f = (kn - h).normalized()
    t = bind["LeftThumbProximal"] - h
    t = (t - f * t.dot(f)).normalized()
    d = f.cross(t).normalized()
    if d.x > 0:  # the back of the left hand faces out (-x) in the bind pose
        d = -d
    return h, kn, f, d.cross(f).normalized(), d


def _extent(pts, origin, axis):
    vals = [(p - origin).dot(axis) for p in pts]
    return min(vals), max(vals)


def _radius(pts, a, b, pad):
    """Skin thickness round the segment a-b (90th percentile distance from its axis) + pad."""
    ax = (b - a).normalized()
    ds = sorted(((p - a) - ax * (p - a).dot(ax)).length for p in pts if -0.005 <= (p - a).dot(ax) <= (b - a).length + 0.005)
    return (ds[int(len(ds) * 0.9)] if ds else 0.009) + pad


def gloves(bind, skin, superellipsoid):
    """skin: HumanBodyBones name -> skin points (body frame) mostly weighted to that bone;
    superellipsoid: make_gear's rounded-box builder."""
    h, kn, f, across, d = frame(bind)
    g = Part("glove", WHITE)
    # Back of the hand: the hand's own skin box, padded (thicker on the back).
    hand = skin["LeftHand"]
    lo_f, hi_f = _extent(hand, h, f)
    lo_a, hi_a = _extent(hand, h, across)
    lo_d, hi_d = _extent(hand, h, d)
    centre = h + f * ((lo_f + hi_f) / 2 + 0.004) + across * ((lo_a + hi_a) / 2) + d * ((lo_d + hi_d) / 2)
    superellipsoid(g, centre, f, across, d, ((hi_f - lo_f) / 2 + 0.002, (hi_a - lo_a) / 2 + 0.004, (hi_d - lo_d) / 2 + 0.008, (hi_d - lo_d) / 2 + 0.003),
                   6, 10, lambda p: "LeftHand", p=2.6)
    top = (hi_d - lo_d) / 2 + 0.008 + ((lo_d + hi_d) / 2)
    for i, name in enumerate(FINGERS):
        chain = [bind[f"Left{name}{s}"] for s in SEGS]
        tip_pts = skin.get(f"Left{name}Distal", [])
        ax = (chain[2] - chain[1]).normalized()
        tip = chain[2] + ax * max(((p - chain[2]).dot(ax) for p in tip_pts), default=0.018)
        pts = chain + [tip]
        radii = [_radius(skin.get(f"Left{name}{s}", []), pts[k], pts[k + 1], 0.0035) for k, s in enumerate(SEGS)]
        # One roll from the wrist over the knuckle to the tip: the back of the hand
        # carries each finger's roll (as on a real glove), no seam at the knuckle.
        wrist = h + f * (lo_f + 0.022) + across * ((chain[0] - h).dot(across) * 0.85) + d * (chain[0] - h).dot(d)
        _roll(g, [wrist] + pts, ["LeftHand"] + [f"Left{name}{s}" for s in SEGS], [radii[0] * 0.95] + radii, [4, 6, 6, 5], d)
    # Thumb: blended along its three bones, fatter on the outside (the thumb guard).
    tc = [bind[f"LeftThumb{s}"] for s in SEGS]
    tax = (tc[2] - tc[1]).normalized()
    ttip = tc[2] + tax * max(((p - tc[2]).dot(tax) for p in skin.get("LeftThumbDistal", [])), default=0.02)
    tr = [_radius(skin.get(f"LeftThumb{s}", []), a, b, 0.004) for s, a, b in zip(SEGS, tc, tc[1:] + [ttip])]
    _roll(g, tc + [ttip], [f"LeftThumb{s}" for s in SEGS], tr, [6, 6, 5], d, fat=1.15)
    # Cuff: round the wrist's measured section, hand -> forearm.
    fore = (h - bind["LeftLowerArm"]).normalized()
    side = d.cross(fore).normalized()
    wrist = [p for p in skin["LeftHand"] + skin["LeftLowerArm"] if abs((p - h).dot(fore) + 0.02) < 0.015]
    ws = max((abs((p - h).dot(side)) for p in wrist), default=0.03) + 0.006
    wd = max((abs((p - h).dot(d)) for p in wrist), default=0.022) + 0.006
    cuff = Part("glove_cuff", TEAM)
    # From over the glove's wrist edge (no skin between) back onto the forearm.
    rings = [(0.024, ("LeftHand", None, 0)), (0.0, ("LeftHand", "LeftLowerArm", 0.5)), (-0.032, ("LeftLowerArm", None, 0)), (-0.045, ("LeftLowerArm", None, 0))]
    cuff.tube([h + fore * o for o, _ in rings], lambda k, _s: (ws * (0.86 if k == 3 else 1), wd * (0.86 if k == 3 else 1), wd * (0.86 if k == 3 else 1)), 8, None,
              up=d, bones=[b if b[1] else b[0] for _, b in rings])
    return [g, cuff]


def _roll(g, path, bones, radii, rings, d, fat=1.0):
    """
    A padded roll along `path` (one segment per bone), fattest on the back,
    bulging in sausages, capped at the tip; the rings near each joint lean half
    on the next bone so the roll bends there instead of breaking.
    """
    pts, rad, on = [], [], []
    last = len(bones) - 1
    for s, (a, b) in enumerate(zip(path, path[1:])):
        n = rings[s]
        for k in range(n + (1 if s == last else 0)):
            t = k / n
            pts.append(a + (b - a) * t)
            r = radii[s] if t > 0.15 or s == 0 else (radii[s - 1] + radii[s]) / 2
            roll = 1 + 0.16 * abs(math.sin(math.pi * t * (2 if n >= 6 else 1.5)))
            taper = 0.8 if (s == last and k == n) else 1.0
            rad.append((r * taper * fat, r * 1.25 * roll * taper * fat, r * 0.9 * taper))
            if s > 0 and t < 0.2:
                on.append((bones[s], bones[s - 1], 0.5 * (1 - t / 0.2)))
            elif s < last and t > 0.8:
                on.append((bones[s], bones[s + 1], 0.5 * (t - 0.8) / 0.2))
            else:
                on.append(bones[s])
    g.tube(pts, lambda k, _s: rad[k], 5, None, up=d, cap_end=True, bones=on)
