"""
Builds the cricket gear meshes on the Rocketbox skeleton's bind pose and
writes them for the game (read by Apl.Client.Match.GearMeshes):

    client-assets/gear/gear.bytes   helmet, batting pads + gloves, keeper pads + gloves, the bat
    client-assets/gear/bat.png      the bat's willow, stickers and grip
    build/gear/preview_*.png        renders on the reference body, for review

    blender -b --python tools/gear/make_gear.py [-- --no-preview]

Frame: Unity body frame (x right, y up, z forward), metres. All 22 cast
bodies share one skeleton, so the gear is modelled once against the
reference body (Male_Adult_01) and fitted to each bone at runtime.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gearlib as G  # noqa: E402
from gearlib import V, Part, TEAM, WHITE, DARK, BAT  # noqa: E402

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
BODY = ROOT + "client-assets/rocketbox/Male_Adult_01/Export/Male_Adult_01.fbx"
OUT = ROOT + "client-assets/gear/"
PREVIEW = ROOT + "build/gear/"
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

# Rocketbox bone -> Unity HumanBodyBones name (left side; right is mirrored).
HUMAN = {
    "Bip01 Head": "Head", "Bip01 Neck": "Neck",
    "Bip01 L Thigh": "LeftUpperLeg", "Bip01 L Calf": "LeftLowerLeg", "Bip01 L Foot": "LeftFoot",
    "Bip01 L Forearm": "LeftLowerArm", "Bip01 L Hand": "LeftHand",
    "Bip01 L Finger0": "LeftThumbProximal", "Bip01 L Finger01": "LeftThumbIntermediate", "Bip01 L Finger02": "LeftThumbDistal",
    "Bip01 L Finger1": "LeftIndexProximal", "Bip01 L Finger11": "LeftIndexIntermediate", "Bip01 L Finger12": "LeftIndexDistal",
    "Bip01 L Finger2": "LeftMiddleProximal", "Bip01 L Finger21": "LeftMiddleIntermediate", "Bip01 L Finger22": "LeftMiddleDistal",
    "Bip01 L Finger3": "LeftRingProximal", "Bip01 L Finger31": "LeftRingIntermediate", "Bip01 L Finger32": "LeftRingDistal",
    "Bip01 L Finger4": "LeftLittleProximal", "Bip01 L Finger41": "LeftLittleIntermediate", "Bip01 L Finger42": "LeftLittleDistal",
}


def to_unity(b):
    return V(-b.x, b.z, -b.y)


def to_blender(u):
    return V(-u.x, -u.z, u.y)


def smooth(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def spow(x, p):
    return math.copysign(abs(x) ** p, x)


# ---------------------------------------------------------------- the body

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BODY)
ARM = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
BIND = {}
for pb in ARM.pose.bones:
    if pb.name in HUMAN:
        BIND[HUMAN[pb.name]] = to_unity(ARM.matrix_world @ pb.head)
for k in list(BIND):
    if k.startswith("Left"):
        p = BIND[k]
        BIND["Right" + k[4:]] = V(-p.x, p.y, p.z)
H = BIND["Head"]

# The reference body's skin, by the bone each vertex leans on most (body frame).
SKIN = {}
_body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
_groups = {g.index: g.name for g in _body.vertex_groups}
for _v in _body.data.vertices:
    if _v.groups:
        _g = _groups.get(max(_v.groups, key=lambda x: x.weight).group)
        if _g in HUMAN:
            SKIN.setdefault(HUMAN[_g], []).append(to_unity(_body.matrix_world @ _v.co))


# ---------------------------------------------------------------- helmet

HC = V(0, 0.095, -0.004)          # shell centre from the head bone
HR = (0.123, 0.157, 0.150, 0.142)  # x, y, z front, z back
BROW, FLAP, NAPE = 79.0, 126.0, 110.0


def phi_max(lam):
    """How far down the shell comes at bearing lam (deg, 0 = front): brow, ear flaps, nape."""
    a = abs(lam)
    if a <= 52:
        return BROW
    if a <= 78:
        return BROW + (FLAP - BROW) * smooth(52, 78, a)
    if a <= 128:
        return FLAP
    if a <= 150:
        return FLAP + (NAPE - FLAP) * smooth(128, 150, a)
    return NAPE


def shell(phi, lam, grow=0.0):
    """A point on the shell (head-bone relative): a squarish ellipsoid whose flaps hang nearly straight."""
    p, l = math.radians(phi), math.radians(lam)
    s = math.sin(p)
    if phi > 90:
        s = 1 - 0.35 * (p - math.pi / 2) ** 2
    hx, hz = spow(math.sin(l), 2 / 2.5), spow(math.cos(l), 2 / 2.5)
    rz = HR[2] if hz > 0 else HR[3]
    return HC + V((HR[0] + grow) * s * hx, (HR[1] + grow) * math.cos(p), (rz + grow) * s * hz)


def helmet():
    parts = []
    out = lambda c: c - (H + HC)  # noqa: E731
    nj, ni = 36, 13
    sh = Part("helmet_shell", TEAM)
    vents = Part("helmet_vents", DARK)
    lams = [-180 + 360 * j / nj for j in range(nj)]

    def P(i, j):
        lam = lams[j % nj]
        top = 4.0
        return H + shell(top + (phi_max(lam) - top) * i / ni, lam)

    idx = [[sh.vert(P(i, j), "Head") for j in range(nj)] for i in range(ni + 1)]
    pole = sh.vert(H + shell(0, 0), "Head")
    for j in range(nj):
        sh.tri(pole, idx[0][j], idx[0][(j + 1) % nj], out)

    def vent(i, j):
        lam = -180 + 360 * (j + 0.5) / nj
        phi = 4 + (phi_max(lam) - 4) * (i + 0.5) / ni
        front = any(abs(lam - c) < 5 for c in (-45, -15, 15, 45)) and 22 <= phi <= 58
        back = any(abs(lam - c) < 5 for c in (-165, 165, -135, 135)) and 48 <= phi <= 86
        return front or back

    for i in range(ni):
        for j in range(nj):
            a, b, c, d = idx[i][j], idx[i + 1][j], idx[i + 1][(j + 1) % nj], idx[i][(j + 1) % nj]
            if not vent(i, j):
                sh.quad(a, b, c, d, out)
                continue
            # A slot: recessed 5 mm, dark floor and walls.
            corners = [sh.v[k] for k in (a, b, c, d)]
            inner = [H + HC + (p - H - HC) * 0.965 for p in corners]
            ci = [vents.vert(p, "Head") for p in inner]
            co = [vents.vert(p, "Head") for p in corners]
            vents.quad(ci[0], ci[1], ci[2], ci[3], out)
            for k in range(4):
                k1 = (k + 1) % 4
                vents.quad(co[k], ci[k], ci[k1], co[k1], out)
    parts += [sh, vents]

    # The rim round the opening: padding showing at the edge.
    rim = Part("helmet_rim", DARK)
    edge = [P(ni, j) for j in range(nj)]
    eo = [rim.vert(p, "Head") for p in edge]
    ei = [rim.vert(H + HC + (p - H - HC) * 0.9 + V(0, 0.004, 0), "Head") for p in edge]
    for j in range(nj):
        j1 = (j + 1) % nj
        rim.quad(eo[j], ei[j], ei[j1], eo[j1], out=lambda c: V(0, -1, 0))
    parts.append(rim)

    # Ear holes: a dark disc on each flap.
    for side in (-1, 1):
        lam = 90 * side
        c0 = H + shell(102, lam, 0.0015)
        n = (c0 - H - HC).normalized()
        u = V(0, 1, 0)
        u = (u - n * u.dot(n)).normalized()
        w = n.cross(u)
        ring = [rim.vert(c0 + (u * math.cos(t) + w * math.sin(t)) * 0.011, "Head") for t in [k * math.pi / 4 for k in range(8)]]
        mid = rim.vert(c0, "Head")
        for k in range(8):
            rim.tri(mid, ring[k], ring[(k + 1) % 8], out=n)

    # The peak: off the brow, out and down, with a little thickness.
    peak = Part("helmet_peak", TEAM)
    under = Part("helmet_peak_under", DARK)
    lams_p = [-66 + 132 * k / 16 for k in range(17)]
    rows_top, rows_bot = [], []
    for lam in lams_p:
        base = H + shell(BROW - 5, lam, 0.001)
        l = math.radians(lam)
        out_h = V(math.sin(l) * 0.45, 0, math.cos(l)).normalized()  # forward, not out to the sides like a hard hat
        length = 0.064 * max(math.cos(l * 1.25), 0.0) ** 0.8 + 0.004
        drop = length * 0.34
        mid = base + out_h * length * 0.5 - V(0, drop * 0.38, 0)
        tip = base + out_h * length - V(0, drop, 0)
        rows_top.append([base, mid, tip])
        rows_bot.append([base - V(0, 0.006, 0) - out_h * 0.004, mid - V(0, 0.006, 0), tip - V(0, 0.005, 0)])
    pt = [[peak.vert(p, "Head") for p in row] for row in rows_top]
    tipb = [peak.vert(row[2], "Head") for row in rows_bot]
    pb = [[under.vert(p, "Head") for p in row] for row in rows_bot]
    for k in range(len(lams_p) - 1):
        for r in range(2):
            peak.quad(pt[k][r], pt[k][r + 1], pt[k + 1][r + 1], pt[k + 1][r], out=V(0, 1, 0))
            under.quad(pb[k][r], pb[k][r + 1], pb[k + 1][r + 1], pb[k + 1][r], out=V(0, -1, 0))
        l = math.radians((lams_p[k] + lams_p[k + 1]) / 2)
        peak.quad(pt[k][2], tipb[k], tipb[k + 1], pt[k + 1][2], out=V(math.sin(l), 0, math.cos(l)))
    parts += [peak, under]

    # The grille: steel bars across the face, three uprights, the chin strap.
    gr = Part("helmet_grille", DARK)

    def bar(ys, yf, xs, zs, zf, n=12):
        pts = []
        for k in range(n + 1):
            s = -1 + 2 * k / n
            pts.append(H + V(xs * s, yf + (ys - yf) * s * s, zf - (zf - zs) * s * s))
        return pts

    # Ends tucked just inside the side flaps (they bolt on there).
    bars = [bar(0.106, 0.108, 0.108, 0.05, 0.198), bar(0.072, 0.058, 0.106, 0.04, 0.204),
            bar(0.042, 0.008, 0.1, 0.03, 0.196), bar(0.020, -0.044, 0.09, 0.02, 0.168)]
    # Titanium bars read thin at 1.7 m: 4.6 mm round, the uprights 4 mm.
    for b in bars:
        gr.tube(b, 0.0046, 6, "Head", up=V(0, 1, 0))
    mid = len(bars[0]) // 2
    gr.tube([b[mid] for b in bars], 0.0046, 6, "Head", up=V(0, 0, 1))
    for k in (3, len(bars[0]) - 4):
        gr.tube([b[k] for b in bars[:3]], 0.004, 6, "Head", up=V(0, 0, 1))
    # Stem guards: two padded tabs hanging behind the ears over the back of the neck.
    for sx in (-1, 1):
        cols = [(0.068, 0.026, -0.103), (0.045, 0.026, -0.112), (0.022, 0.026, -0.116)]
        rows = [0.0, -0.035, -0.07]
        grid = [[gr.vert(H + V(sx * x, y0 + dy, z + 0.012 * (dy / -0.07)), "Head") for (x, y0, z) in cols] for dy in rows]
        back = [[gr.vert(gr.v[i] + V(0, 0, 0.008), "Head") for i in row] for row in grid]
        for r in range(2):
            for c in range(2):
                gr.quad(grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1], out=V(0, 0, -1))
                gr.quad(back[r][c], back[r + 1][c], back[r + 1][c + 1], back[r][c + 1], out=V(0, 0, 1))
    strap = [V(0.1, 0.022, 0.0), V(0.078, -0.022, 0.036), V(0.042, -0.04, 0.068), V(0.0, -0.044, 0.08)]
    strap = [H + p for p in strap] + [H + V(-p.x, p.y, p.z) for p in reversed(strap[:-1])]
    gr.tube(strap, (0.0075, 0.0022, 0.0022), 4, "Head", up=V(0, 0, 1))
    parts.append(gr)
    return parts


# ---------------------------------------------------------------- pads

def pads(keeper=False):
    """Left pad: a shin and a thigh piece (the thigh one rides the thigh bone), knee rolls, wings, straps, trim."""
    K, A = BIND["LeftLowerLeg"], BIND["LeftFoot"]
    cx = -0.112
    shin_top = 0.455
    knee_top = 0.585 if not keeper else 0.545
    top = 0.72 if not keeper else 0.6
    rolls = 3 if not keeper else 2
    deep = 0.0 if not keeper else -0.014
    bol = 0.014 if not keeper else 0.009

    def zf(y):
        # Front of the pad's face (before bolsters): clears shin, knee and thigh.
        pts = [(0.07, 0.106), (0.15, 0.07), (0.3, 0.085), (0.42, 0.11), (0.5, 0.13), (0.6, 0.15), (0.74, 0.165)]
        for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
            if y <= y1:
                return z0 + (z1 - z0) * max(0.0, (y - y0) / (y1 - y0)) + deep
        return pts[-1][1] + deep

    def cz(y):
        return -0.024 + 0.03 * smooth(0.1, 0.55, y)

    face_w = 0.086 if not keeper else 0.074
    wing_r = 0.03
    nf, nw = 12, 3  # face columns, wing columns each side

    def section(y, t, bolster, roll):
        """Point across the pad at height y; t in [-1, 1] (-1 = outer wing edge)."""
        w = face_w * (1 - 0.35 * smooth(top - 0.06, top, y))  # rounded top corners
        tf = nf / (nf + 2 * nw)
        if abs(t) <= tf:
            tau = t / tf
            x = cx + tau * w  # +t toward the inside of the left leg (+x)
            bump = abs(math.cos(1.5 * math.pi * tau)) ** 0.6 * bolster + roll
            z = zf(y) - 0.012 * tau * tau + bump
            return V(x, y, z)
        u = (abs(t) - tf) / (1 - tf)
        sgn = 1 if t > 0 else -1
        ang = min(u * 1.25, 1.0) * math.pi / 2
        back = max(u * 1.25 - 1.0, 0.0) / 0.25
        z_back_end = cz(y) - 0.012
        z0 = zf(y) - 0.012
        x = cx + sgn * (w + wing_r * math.sin(ang))
        z = z0 - wing_r * (1 - math.cos(ang))
        z = z + (z_back_end - z) * back
        return V(x, y, z)

    # Rows: (y at the centre, bolster height, roll bulge) — shin, knee rolls, thigh.
    rows = []
    for k in range(8):
        y = 0.075 + (shin_top - 0.075) * k / 7
        rows.append(("shin", y, bol, 0.0))
    for k in range(1, rolls * 2 + 1):
        f = k / (rolls * 2)
        y = shin_top + (knee_top - shin_top) * f
        rows.append(("knee", y, 0.0, 0.017 * math.sin(math.pi * rolls * f) ** 2 + 0.006))
    for k in range(1, 5):
        y = knee_top + (top - knee_top) * k / 4
        rows.append(("thigh", y, bol * 0.75, 0.0))
    cols = nf + 2 * nw

    def bottom_y(t, y):
        return y + 0.09 * smooth(0.45, 1.0, abs(t)) if y < 0.08 else y

    shin = Part("pad", WHITE)
    trim = Part("pad_trim", TEAM)
    grid = []
    for r, (sec, y, b, roll) in enumerate(rows):
        bone = "LeftUpperLeg" if sec == "thigh" else "LeftLowerLeg"
        row = []
        for c in range(cols + 1):
            t = -1 + 2 * c / cols
            yy = bottom_y(t, y)
            row.append(shin.vert(section(yy, t, b, roll), bone))
        grid.append(row)
    out = lambda p: V(p.x - cx, 0, p.z - cz(p.y))  # noqa: E731
    for r in range(len(rows) - 1):
        for c in range(cols):
            shin.quad(grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1], out)

    # Trim: the wing edges and the top roll back on themselves in team colour.
    def roll_back(p, q, inward):
        return p + inward * 0.012

    for c in (0, cols):
        edge = [shin.v[grid[r][c]] for r in range(len(rows))]
        bones = ["LeftUpperLeg" if rows[r][0] == "thigh" else "LeftLowerLeg" for r in range(len(rows))]
        sgn = 1 if c == cols else -1
        a = [trim.vert(p + V(0, 0, -0.001), b_) for p, b_ in zip(edge, bones)]
        bb = [trim.vert(p + V(-sgn * 0.011, 0, -0.004), b_) for p, b_ in zip(edge, bones)]
        for r in range(len(rows) - 1):
            trim.quad(a[r], a[r + 1], bb[r + 1], bb[r], out=V(sgn, 0, -0.3))
    toprow = [shin.v[grid[-1][c]] for c in range(cols + 1)]
    a = [trim.vert(p + V(0, 0.001, 0), "LeftUpperLeg") for p in toprow]
    bb = [trim.vert(p + V(0, -0.004, -0.012), "LeftUpperLeg") for p in toprow]
    for c in range(cols):
        trim.quad(a[c], bb[c], bb[c + 1], a[c + 1], out=V(0, 1, 0.3))

    # Straps round the back of the calf (and the thigh): flat bands just off the skin.
    def calf_back(y):
        pts = [(0.1, -0.07), (0.15, -0.066), (0.21, -0.072), (0.3, -0.078), (0.39, -0.07), (0.45, -0.06), (0.5, -0.093), (0.7, -0.094)]
        for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
            if y <= y1:
                return z0 + (z1 - z0) * max(0.0, (y - y0) / (y1 - y0))
        return pts[-1][1]

    straps = Part("pad_straps", DARK if keeper else TEAM)
    strap_ys = [0.2, 0.38, 0.65] if not keeper else [0.2, 0.38]
    for sy in strap_ys:
        bone = "LeftUpperLeg" if sy > knee_top else "LeftLowerLeg"
        z0 = cz(sy) - 0.012
        rx = face_w + wing_r - 0.004
        rz = z0 - (calf_back(sy) - 0.01)
        pts = []
        for k in range(9):
            a_ = math.pi * (1 - k / 8)  # outer wing (-x) round the back to the inner wing
            pts.append(V(cx + rx * math.cos(a_), sy, z0 - rz * math.sin(a_)))
        lo = [straps.vert(p - V(0, 0.016, 0), bone) for p in pts]
        hi = [straps.vert(p + V(0, 0.016, 0), bone) for p in pts]
        for k in range(len(pts) - 1):
            straps.quad(lo[k], hi[k], hi[k + 1], lo[k + 1], out=lambda c, z0=z0: V(c.x - cx, 0, c.z - z0 + 0.04))
        # Buckle tab on the outer wing.
        tab = pts[0] + V(-0.004, 0, 0.012)
        straps.quad(straps.vert(tab - V(0, 0.016, 0.02), bone), straps.vert(tab + V(0, 0.016, -0.02), bone),
                    straps.vert(tab + V(0, 0.016, 0.0), bone), straps.vert(tab - V(0, 0.016, 0.0), bone), out=V(-1, 0, 0))
    return [shin, trim, straps]


# ---------------------------------------------------------------- gloves

FINGERS = [("Index", 1.0), ("Middle", 1.04), ("Ring", 0.98), ("Little", 0.86)]


def chain(f):
    p = [BIND[f"Left{f}{s}"] for s in ("Proximal", "Intermediate", "Distal")]
    tip = p[2] + (p[2] - p[1]) * 0.9
    return p + [tip]


def hand_frame():
    h = BIND["LeftHand"]
    knuckles = sum((BIND[f"Left{f}Proximal"] for f, _ in FINGERS), Vector()) / 4
    f = (knuckles - h).normalized()
    t = (BIND["LeftThumbProximal"] - h)
    t = (t - f * t.dot(f)).normalized()
    d = f.cross(t).normalized()
    if d.x > 0:  # the back of the left hand faces out (-x) in the bind pose
        d = -d
    return h, knuckles, f, t, d


def superellipsoid(part, centre, ax, ay, az, ext, n_lat, n_lon, bone, p=3.0, half=None, slot_split=None):
    """
    A rounded box: ax along (length), ay across, az up (dorsal); ext = (length/2,
    width/2, dorsal, palmar). bone(pt) picks each vertex's bone. half='top'
    or 'bottom' keeps only one side of az (two slots).
    """
    L, W, Du, Dd = ext
    rows = []
    for i in range(n_lat + 1):
        th = -math.pi / 2 + math.pi * i / n_lat
        row = []
        for j in range(n_lon):
            ph = 2 * math.pi * j / n_lon
            c1, s1 = spow(math.cos(th), 2 / p), spow(math.sin(th), 2 / p)
            c2, s2 = spow(math.cos(ph), 2 / p), spow(math.sin(ph), 2 / p)
            up = s2 * c1
            pt = centre + ax * (L * s1) + ay * (W * c1 * c2) + az * ((Du if up > 0 else Dd) * up)
            row.append(pt)
        rows.append(row)
    out = lambda c: c - centre  # noqa: E731
    idx = {}

    def vi(prt, i, j):
        key = (id(prt), i, j)
        if key not in idx:
            idx[key] = prt.vert(rows[i][j], bone(rows[i][j]))
        return idx[key]

    for i in range(n_lat):
        for j in range(n_lon):
            j1 = (j + 1) % n_lon
            mid = (rows[i][j] + rows[i + 1][j1]) / 2
            prt = part
            if slot_split is not None and (mid - centre).dot(az) < 0:
                prt = slot_split
            prt.quad(vi(prt, i, j), vi(prt, i + 1, j), vi(prt, i + 1, j1), vi(prt, i, j1), out)


def gloves():
    h, kn, f, t, d = hand_frame()
    g = Part("glove", WHITE)
    cuff = Part("glove_cuff", TEAM)
    across = d.cross(f).normalized()
    # Back of the hand: a padded block from the wrist to the knuckles.
    centre = h + (kn - h) * 0.5
    superellipsoid(g, centre, f, across, d, ((kn - h).length * 0.64, 0.05, 0.025, 0.024), 6, 10, lambda p: "LeftHand", p=4.0)
    # Finger rolls: a fat sausage on each phalanx, fattest on the back.
    for name, scale in FINGERS:
        pts = chain(name)
        for s, seg in enumerate(("Proximal", "Intermediate", "Distal")):
            a, b = pts[s], pts[s + 1]
            dirn = (b - a).normalized()
            a2 = a - dirn * (0.014 if seg == "Proximal" else 0.004)
            ring_pts = [a2 + (b - a2) * k / 3 for k in range(4)]
            rad = lambda k, _s, sc=scale: tuple(r * sc * (0.9 if k in (0, 3) else 1.06) for r in (0.0108, 0.0142, 0.0095))  # noqa: E731
            g.tube(ring_pts, rad, 5, f"Left{name}{seg}", up=d, cap_end=(seg == "Distal"))
    # The thumb, with its own roll; the cuff round the wrist in team colour.
    tp = [BIND["LeftThumbProximal"], BIND["LeftThumbIntermediate"], BIND["LeftThumbDistal"]]
    tp.append(tp[2] + (tp[2] - tp[1]) * 0.9)
    for s, seg in enumerate(("Proximal", "Intermediate", "Distal")):
        a, b = tp[s], tp[s + 1]
        ring_pts = [a + (b - a) * k / 3 for k in range(4)]
        r0 = 0.0135 if seg == "Proximal" else 0.0118
        g.tube(ring_pts, lambda k, _s, r=r0: (r, r * 1.2, r * 0.9), 5, f"LeftThumb{seg}", up=d, cap_end=(seg == "Distal"))
    fore = (h - BIND["LeftLowerArm"]).normalized()
    cuff_pts = [h + fore * 0.012, h - fore * 0.025, h - fore * 0.062]
    cuff.tube(cuff_pts, lambda k, _s: (0.037, 0.032, 0.028) if k else (0.035, 0.03, 0.026), 8, "LeftHand", up=d)
    return [g, cuff]


def keeper_gloves():
    h, kn, f, t, d = hand_frame()
    g = Part("glove", WHITE)
    palm = Part("glove_palm", DARK)
    cuff = Part("glove_cuff", TEAM)
    across = d.cross(f).normalized()
    centre = h + (kn - h) * 0.5 + d * 0.004
    superellipsoid(g, centre, f, across, d, ((kn - h).length * 0.66, 0.056, 0.03, 0.022), 6, 10, lambda p: "LeftHand", slot_split=palm)
    # The fingers in one padded paddle that curls with the middle finger.
    mc = chain("Middle")
    fd = (mc[3] - mc[0]).normalized()
    pc = (mc[0] + mc[3]) / 2 + fd * 0.008 + d * 0.002

    def fbone(p):
        s = (p - mc[0]).dot(fd)
        return "LeftMiddleProximal" if s < (mc[1] - mc[0]).length else "LeftMiddleIntermediate" if s < (mc[2] - mc[0]).length else "LeftMiddleDistal"

    superellipsoid(g, pc, fd, across, d, ((mc[3] - mc[0]).length * 0.62, 0.056, 0.022, 0.02), 8, 10, fbone, p=2.6, slot_split=palm)
    tp = [BIND["LeftThumbProximal"], BIND["LeftThumbIntermediate"], BIND["LeftThumbDistal"]]
    tp.append(tp[2] + (tp[2] - tp[1]) * 0.9)
    bones = ["LeftThumbProximal", "LeftThumbIntermediate", "LeftThumbDistal", "LeftThumbDistal"]
    g.tube(tp, lambda k, _s: (0.018, 0.02, 0.016), 6, None, up=d, cap_end=True, bones=bones)
    # Webbing from the thumb to the side of the index finger.
    ic = chain("Index")
    web = Part("glove_web", WHITE)
    edge_a = [tp[1] + (tp[3] - tp[1]) * k / 3 for k in range(4)]
    edge_b = [ic[0] + (ic[2] - ic[0]) * k / 3 + across * 0.0 for k in range(4)]
    wa = [web.vert(p, "LeftThumbIntermediate" if k < 2 else "LeftThumbDistal") for k, p in enumerate(edge_a)]
    wb = [web.vert(p, "LeftIndexProximal" if k < 2 else "LeftIndexIntermediate") for k, p in enumerate(edge_b)]
    for k in range(3):
        web.quad(wa[k], wa[k + 1], wb[k + 1], wb[k], out=d)
    fore = (h - BIND["LeftLowerArm"]).normalized()
    cuff.tube([h + fore * 0.01, h - fore * 0.03, h - fore * 0.075], lambda k, _s: (0.05, 0.044, 0.036), 8, "LeftHand", up=d)
    return [g, palm, web, cuff]


# ---------------------------------------------------------------- the bat

BLADE_TOP, TOE, HANDLE = -0.31, -0.86, -0.30
HALF_W = 0.0535


def bat():
    """Grip at the origin, blade down -y, face toward +z (BatterRig's frame)."""
    b = Part("bat", BAT)
    L = BLADE_TOP - TOE

    def v_blade(y):
        return (y - TOE) / L * 0.75

    def profile(y):
        """Edge half-width, face z, edge thickness, spine height at this height of the blade."""
        s = (BLADE_TOP - y) / L  # 0 at the shoulder, 1 at the toe
        w = HALF_W
        toe_round = TOE + 0.028
        if y < toe_round:
            w *= math.sqrt(max(1 - ((toe_round - y) / 0.03) ** 2, 0.0)) * 0.55 + 0.45
        shoulder = smooth(0.0, 0.08, s)
        w = 0.019 + (w - 0.019) * shoulder
        face = 0.019 + 0.004 * math.sin(math.pi * s)
        edge = 0.034 + 0.007 * s
        spine = 0.029 * math.exp(-((s - 0.62) / 0.33) ** 2) * shoulder + 0.004
        return w, face, edge, spine

    rings = []
    ys = [BLADE_TOP + (TOE - BLADE_TOP) * k / 15 for k in range(16)]
    ys = sorted(set(ys + [BLADE_TOP - 0.012, BLADE_TOP - 0.03, TOE + 0.012, TOE + 0.004]), reverse=True)
    for y in ys:
        w, face, edge, spine = profile(y)
        ring = []
        # Around the section: face (left to right), right edge, back (right to left), left edge.
        for k in range(7):
            x = -w + 2 * w * k / 6
            ring.append((V(x * 0.985, y, face + 0.0012 * (1 - (x / w) ** 2)), (0.02 + 0.46 * k / 6, v_blade(y))))
        for k in range(1, 3):
            a = math.pi / 2 * k / 3
            ring.append((V(w * (0.985 + 0.015 * math.sin(a * 2)), y, face - edge * k / 3), (0.49, v_blade(y))))
        for k in range(9):
            x = w - 2 * w * k / 8
            zb = face - edge - spine * max(1 - (x / w) ** 2, 0.0) ** 1.3
            ring.append((V(x, y, zb), (0.52 + 0.44 * k / 8, v_blade(y))))
        for k in range(1, 3):
            a = math.pi / 2 * k / 3
            ring.append((V(-w * (0.985 + 0.015 * math.sin(a * 2)), y, face - edge * (3 - k) / 3), (0.49, v_blade(y))))
        rings.append([b.vert(p, "None", uv) for p, uv in ring])
    n = len(rings[0])
    for r in range(len(rings) - 1):
        for k in range(n):
            k1 = (k + 1) % n
            b.quad(rings[r][k], rings[r + 1][k], rings[r + 1][k1], rings[r][k1], out=lambda c: V(c.x, 0, c.z - 0.0))
    # The toe and the shoulders closed.
    for ring, y, sign in ((rings[-1], ys[-1], -1), (rings[0], ys[0], 1)):
        _, face, edge, spine = profile(y)
        c = b.vert(V(0, y + sign * 0.002, face - edge * 0.5 - spine * 0.4), "None", (0.5, v_blade(y)))
        for k in range(n):
            b.tri(ring[k], ring[(k + 1) % n], c, out=V(0, sign, 0))
    # Handle: an oval grip from the shoulders up to a rounded cap at the origin.
    hpts = [V(0, y, 0.0) for y in [-0.33, -0.3, -0.27] + [-0.26 + 0.25 * k / 6 for k in range(7)]]

    def hr(k, _s):
        y = hpts[k].y
        r = 0.0168 + (0.006 * smooth(-0.27, -0.33, y))
        return (r * 1.03, r, r)

    rings_h = b.tube(hpts, hr, 10, "None", up=V(0, 0, 1), cap_end=True)
    for k, ring in enumerate(rings_h):
        v = 0.78 + 0.22 * (hpts[k].y + 0.33) / 0.33
        for s, vi in enumerate(ring):
            b.uv[vi] = (s / len(ring), v)
    b.uv[-1] = (0.5, 1.0)
    return [b]


def bat_texture(path):
    """Willow with grain, the APL stickers on the face and back, a dark spiral grip."""
    W, Hh = 256, 512
    import random
    rnd = random.Random(7)
    grain = [rnd.random() for _ in range(W)]
    font = {
        "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    }

    def text_px(u, v, u0, v0, scale):
        """True where 'APL' is drawn, rotated to read along the blade (bottom to top)."""
        gx = int((v - v0) * Hh / scale)
        gy = int((u - u0) * W / scale)
        ch = gx // 6
        if not (0 <= ch < 3 and 0 <= gy < 7):
            return False
        cx = gx % 6
        if cx >= 5:
            return False
        return font["APL"[ch]][6 - gy][cx] == "1"

    rows = []
    for yy in range(Hh):
        v = 1 - (yy + 0.5) / Hh
        row = []
        for x in range(W):
            u = (x + 0.5) / W
            if v > 0.77:
                # Grip: rubber with spiral ridges, a darker cap.
                ridge = (u * 6 + v * 110) % 1.0
                c = (40, 40, 46) if ridge < 0.55 else (78, 24, 30)
                if v > 0.985:
                    c = (25, 25, 28)
                row.append(c + (255,))
                continue
            gx = grain[x] * 0.6 + grain[(x * 7) % W] * 0.4
            base = (226 - 18 * gx, 199 - 20 * gx, 150 - 22 * gx)
            line = 0.92 if (x % 9 == 0 or (x * 13) % 31 == 0) else 1.0
            c = tuple(int(ch * line * (0.97 + 0.06 * math.sin(v * 40 + gx * 6))) for ch in base)
            face = u < 0.5
            lu = u * 2 if face else (u - 0.5) * 2
            if 0.46 <= v <= 0.72:
                # The main sticker: navy panel, gold border stripes, white APL along the blade.
                if 0.08 < lu < 0.92:
                    c = (22, 30, 74)
                    if abs(lu - 0.16) < 0.03 or abs(lu - 0.84) < 0.03:
                        c = (232, 178, 40)
                    if text_px(lu / 2 if face else lu / 2, v, 0.12, 0.5, 5.2):
                        c = (245, 245, 245)
                    if 0.46 <= v < 0.48 or 0.70 < v <= 0.72:
                        c = (205, 30, 45)
            if face and 0.04 <= v <= 0.12 and 0.22 < lu < 0.78:
                c = (22, 30, 74) if not (0.07 < v < 0.09) else (232, 178, 40)
            row.append(c + (255,))
        rows.append(row)
    G.write_png(path, W, Hh, rows)


# ---------------------------------------------------------------- export, previews

def build():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(PREVIEW, exist_ok=True)
    left = {
        "helmet": helmet(),
        "pads": __import__("pads").pads(),
        "gloves": __import__("gloves").gloves(BIND, SKIN, superellipsoid),
        "kpads": __import__("pads").pads(keeper=True),
        "kgloves": keeper_gloves(),
    }
    parts = []
    for p in left["helmet"]:
        parts.append(p.finish())
    for set_name in ("pads", "gloves", "kpads", "kgloves"):
        for p in left[set_name]:
            base = (set_name[0] == "k" and "k_" or "") + p.name
            p.name = base + "_L"
            parts.append(p.finish())
            parts.append(p.mirrored(base + "_R"))
    parts += [p.finish() for p in bat()]
    import cap
    import hats
    parts += [p.finish() for p in cap.cap(H) + hats.hat(H)]
    size = G.export(parts, OUT + "gear.bytes", BIND)
    bat_texture(OUT + "bat.png")
    __import__("padtex").write(OUT + "pad.png")
    for p in parts:
        print(f"PART {p.name:24s} slot {p.slot} verts {len(p.v):5d} tris {p.tris:5d}")
    batter = sum(p.tris for p in parts if not p.name.startswith(("k_", "bat", "cap", "hat")))
    keeper = sum(p.tris for p in parts if p.name.startswith("k_"))
    print(f"TOTAL batter {batter} tris, keeper {keeper} tris, bat {next(p.tris for p in parts if p.name == 'bat')} tris; gear.bytes {size} bytes")
    return parts


COLORS = {TEAM: (0.78, 0.1, 0.12, 1), WHITE: (0.93, 0.93, 0.9, 1), DARK: (0.1, 0.1, 0.12, 1), BAT: (0.85, 0.72, 0.5, 1), 4: (0.93, 0.93, 0.9, 1)}


def preview(parts, keeper_view=False):
    # The body in plain clay; hair cards off under the helmet (as the game does).
    import bmesh
    for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        for m in ob.data.materials:
            m.use_nodes = False
            m.diffuse_color = (0.62, 0.5, 0.44, 1) if "head" in m.name else (0.3, 0.34, 0.5, 1)
        if not keeper_view:
            bm = bmesh.new()
            bm.from_mesh(ob.data)
            hair = [i for i, m in enumerate(ob.data.materials) if "opacity" in m.name]
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in hair], context="FACES")
            bm.to_mesh(ob.data)
            bm.free()
    mats = {}
    for slot, col in COLORS.items():
        m = bpy.data.materials.new(f"slot{slot}")
        m.diffuse_color = col
        mats[slot] = m
    batm = bpy.data.materials.new("bat_tex")
    batm.use_nodes = True
    img = bpy.data.images.load(OUT + "bat.png")
    tex = batm.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    bsdf = batm.node_tree.nodes["Principled BSDF"]
    batm.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    batm.diffuse_color = COLORS[BAT]
    padm = bpy.data.materials.new("pad_tex")
    padm.use_nodes = True
    ptex = padm.node_tree.nodes.new("ShaderNodeTexImage")
    ptex.image = bpy.data.images.load(OUT + "pad.png")
    padm.node_tree.links.new(ptex.outputs["Color"], padm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    mats[4] = padm
    posed = []
    for p in parts:
        hats_view = next((x[2:] for x in ARGS if x in ("--cap", "--hat")), None)
        if hats_view:
            # Headwear review: the batter's view with that headwear in place of the helmet.
            if p.name.startswith("helmet") or (p.name.startswith(("cap", "hat")) and not p.name.startswith(hats_view)):
                continue
        elif p.name.startswith(("cap", "hat")):
            continue
        is_k = p.name.startswith("k_")
        if p.name == "bat":
            continue
        if is_k != keeper_view:
            continue
        me = bpy.data.meshes.new(p.name)
        me.from_pydata([tuple(to_blender(v)) for v in p.v], [], [(a, c, b) for a, b, c in p.t])
        me.update()
        for poly in me.polygons:
            poly.use_smooth = True
        uvl = me.uv_layers.new()
        for poly in me.polygons:
            for li in poly.loop_indices:
                uvl.data[li].uv = p.uv[me.loops[li].vertex_index]
        ob = bpy.data.objects.new(p.name, me)
        ob.data.materials.append(mats[p.slot])
        bpy.context.scene.collection.objects.link(ob)
        posed.append((ob, p))
    # The bat, held out in front of the body at the right hand, face toward the camera.
    pb = next(p for p in parts if p.name == "bat")
    me = bpy.data.meshes.new("bat")
    at = V(0.45, 1.25, 0.25)
    me.from_pydata([tuple(to_blender(v + at)) for v in pb.v], [], [(a, c, b) for a, b, c in pb.t])
    uvl = me.uv_layers.new()
    for poly in me.polygons:
        poly.use_smooth = True
        for li in poly.loop_indices:
            uvl.data[li].uv = pb.uv[me.loops[li].vertex_index]
    ob = bpy.data.objects.new("bat", me)
    ob.data.materials.append(batm)
    bpy.context.scene.collection.objects.link(ob)

    if "--pose" in ARGS:
        import posecheck
        posecheck.skin(posed, ARM, HUMAN)
        posecheck.bend(ARM)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    sc.render.film_transparent = False
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam

    def shoot(name, eye, look, lens=50):
        cam.location = to_blender(eye)
        d = to_blender(look) - cam.location
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        sc.render.filepath = PREVIEW + name
        bpy.ops.render.render(write_still=True)

    hd = H + V(0, 0.08, 0.04)
    tag = "keeper_" if keeper_view else ""
    if not keeper_view:
        shoot("preview_head_34.png", hd + V(0.45, 0.06, 0.55), hd, 85)
        shoot("preview_head_side.png", hd + V(0.75, 0.0, 0.0), hd, 85)
        shoot("preview_head_back.png", hd + V(-0.35, 0.25, -0.6), hd, 85)
        shoot("preview_head_front.png", hd + V(0.0, 0.0, 0.75), hd, 85)
    shoot(tag + "preview_legs.png", V(0.55, 0.55, 1.3), V(0, 0.38, 0.02), 50)
    shoot(tag + "preview_legs_back.png", V(-0.7, 0.6, -1.0), V(0, 0.38, 0.0), 50)
    shoot(tag + "preview_legs_side.png", V(-1.5, 0.45, 0.25), V(-0.1, 0.4, 0.05), 50)
    hand = BIND["LeftHand"] + V(-0.03, -0.05, 0.03)
    shoot(tag + "preview_hand.png", hand + V(-0.35, 0.05, 0.25), hand, 85)
    shoot(tag + "preview_hand_in.png", hand + V(0.3, 0.1, 0.25), hand, 85)
    shoot(tag + "preview_body.png", V(1.2, 1.3, 3.2), V(0, 0.95, 0), 50)
    if not keeper_view:
        shoot("preview_bat.png", at + V(0.0, -0.4, 1.1), at + V(0, -0.45, 0), 50)
        shoot("preview_bat_back.png", at + V(0.6, -0.4, -0.9), at + V(0, -0.45, 0), 50)


parts = build()
if "--no-preview" not in ARGS:
    preview(parts, keeper_view="--keeper" in ARGS)
