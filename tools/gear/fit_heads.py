"""
Measures every cast body's head (the head material only: hair cards are hidden
under headwear in the game) from the head bone, along a grid of directions out
of the cap centre, and writes:

    tools/gear/head_hull.json   per direction the outermost scalp over the bodies
                                (the big-hair bodies in CricketCast.HeadFit left out:
                                they get their own scale), for hats.cap()
    build/gear/head_fit.txt     per body: top / back / side / front of the scalp
                                (head-bone-relative, metres) and how far it pokes
                                past the hull (> 0 = would clip the cap)

    blender -b --python tools/gear/fit_heads.py
"""
import json
import math

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
ROOT = _APL_REPO + "/"
try:
    CAST = json.load(open(ROOT + "client/Assets/Resources/Data/kit_cast.json"))["cast"]
except OSError:  # the client folder is being rebuilt: KitBuilder.Cast's bodies
    CAST = [{"model": m} for m in (
        "Male_Adult_11 Male_Adult_02 Male_Adult_16 Male_Adult_09 Sports_Male_04 Sports_Male_02 Male_Adult_10 Male_Adult_05 "
        "Male_Adult_14 Male_Adult_01 Male_Adult_20 Male_Adult_08 Male_Adult_06 Male_Adult_03 Male_Adult_13 Male_Adult_07 "
        "Sports_Male_03 Wood_Male_01 Male_Adult_04 Male_Adult_17 Male_Adult_12 Male_Adult_18").split()]
# Bodies added to KitBuilder.Cast since the last catalog build (measured before Unity rebuilds it).
for extra in ("Sports_Male_01", "Business_Male_02", "Business_Male_04", "Business_Male_05", "Business_Male_06", "Business_Male_07"):
    if all(e["model"] != extra for e in CAST):
        CAST.append({"model": extra})
OWN_FIT = {"Sports_Male_03", "Male_Adult_04"}
# Hair modelled into the head mesh (buns, quiffs, afros): these never get a cap
# (CricketCast.NoCap) — they'd put a bulge in everyone's.
NO_CAP = OWN_FIT | {"Wood_Male_01", "Male_Adult_17", "Male_Adult_18", "Male_Adult_10"}
C = Vector((0, 0.1, -0.005))       # cap centre from the head bone (Unity frame)
PHIS = list(range(0, 121, 5))      # from the top down
LAMS = list(range(0, 360, 10))     # round from the front, + to the right


def direction(phi, lam):
    p, l = math.radians(phi), math.radians(lam)
    return Vector((math.sin(p) * math.sin(l), math.cos(p), math.sin(p) * math.cos(l)))


def head_tree(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{ROOT}client-assets/rocketbox/{name}/Export/{name}.fbx")
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    hb = arm.matrix_world @ arm.pose.bones["Bip01 Head"].head
    head = Vector((-hb.x, hb.z, -hb.y))
    m = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    verts = [(lambda w: Vector((-w.x, w.z, -w.y)) - head)(m.matrix_world @ v.co) for v in m.data.vertices]
    mats = [i for i, mt in enumerate(m.data.materials) if "head" in mt.name.lower()]
    polys = [list(p.vertices) for p in m.data.polygons if p.material_index in mats]
    return BVHTree.FromPolygons(verts, polys)


def radius(tree, d):
    """Outermost scalp along d from C (cast inward from outside), or None."""
    hit = tree.ray_cast(C + d * 0.4, -d)
    return (hit[0] - C).length if hit[0] is not None else None


bodies = {}
for e in CAST:
    tree = head_tree(e["model"])
    bodies[e["model"]] = {f"{p},{l}": radius(tree, direction(p, l)) for p in PHIS for l in LAMS}

# The outermost scalp of the bodies that wear caps, smoothed over neighbouring
# directions but never more than 3 mm inside the raw max (the cap's gap is 6 mm).
raw = {}
for key in bodies[CAST[0]["model"]]:
    rs = [b[key] for n, b in bodies.items() if n not in NO_CAP and b[key] is not None]
    raw[key] = max(rs) if rs else None
hull = dict(raw)
for _ in range(3):
    nxt = {}
    for p in PHIS:
        for l in LAMS:
            near = [hull.get(f"{q},{(l + dl) % 360}") for q in (p - 5, p, p + 5) for dl in (-10, 0, 10)]
            near = [x for x in near if x]
            nxt[f"{p},{l}"] = sum(near) / len(near) if near else None
    hull = nxt
hull = {k: (max(v, raw[k] - 0.003) if v and raw[k] else v) for k, v in hull.items()}
json.dump({"centre": list(C), "phis": PHIS, "lams": LAMS, "radius": hull}, open(ROOT + "tools/gear/head_hull.json", "w"))

lines = ["head-bone-relative metres; poke = scalp past the cap hull under the cap (>0.006 clips the cap)",
         "body               top    back   side   front  poke    at (phi,lam)"]
for n, b in bodies.items():
    def r(p, l):
        v = b[f"{p},{l}"]
        return v if v is not None else float("nan")
    # Only where the cap is (above its rim): how far the scalp pokes past the cap's lining (gap 6 mm).
    capped = [k for k in b if int(k.split(",")[0]) <= 80 + 28 * (abs(((int(k.split(",")[1]) + 180) % 360) - 180) / 180)]
    worst, at = max(((b[k] or 0) - (hull[k] or 0), k) for k in capped)
    top = C.y + r(0, 0)
    back = C.z - r(90, 180)
    side = r(90, 90)
    front = C.z + r(80, 0)
    lines.append(f"{n:18s} {top:6.3f} {back:6.3f} {side:6.3f} {front:6.3f} {worst:+.3f} at ({at})" + ("  [no cap]" if n in NO_CAP else ""))
open(ROOT + "build/gear/head_fit.txt", "w").write("\n".join(lines) + "\n")
print("\n".join(lines))
