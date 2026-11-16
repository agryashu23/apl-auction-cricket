"""
Round 9: keep the blade OUT of the body.  The audit (build/motions/work/r4/audit.py) still counted ~250 frames
with the bat through a thigh, pad, torso or head -- mostly brushes in the shape-up, the settle and the reverse /
switch shots.  This pass solves every frame of a clip once (the body hardly depends on the bat: the bat is IK'd
to the hands, not the other way), finds where the blade sits inside the audit's body capsules, and slides the
whole bat (its grip, so the hands follow it) straight out by the depth + 1.5 cm.  The per-frame shifts are smoothed
(an envelope, so smoothing never under-corrects) and left at zero round the contact frame (contact stays where
the shot needs it).  They are cached per clip (work/r9/bat_clear/<clip>.json, right-handed; the left-hander's
clip is the mirror) and baked into the keys as dense per-frame keys, so the FBX, the dump and the game's bat track
all carry the same bat.

    APL_BAT_CLEAR=1   (re)compute the cache while generating; without it a cached clip is just applied
"""
import json
import math
import os

import batting
from batting import BAT_LEN, GRIP_R, V
from mixamo_motions import sample

CACHE = os.path.join(batting.APL_WORK, "r9", "bat_clear")
# the audit's capsules: joint a -> joint b (or a's tail), radius, name
CAPS = [("Hips", "Spine2", 0.12), ("Spine2", "Neck", 0.11), ("Head", "HeadTop_End", 0.11),
        ("LeftUpLeg", "LeftLeg", 0.08), ("RightUpLeg", "RightLeg", 0.08),
        ("LeftLeg", "LeftFoot", 0.075), ("RightLeg", "RightFoot", 0.075)]
MARGIN = 0.015
KEEP = 3           # frames either side of contact left alone


def _seg(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.dot(ab), 1e-9)))
    return a + ab * t


def _joints(rig, pose):
    Pos, tails, _ = rig.solve(pose, {}, write=False)
    Ct = batting.C.transposed()
    nm = lambda k: k.split(":")[-1]                       # "mixamorig:Hips" -> "Hips"
    J = {nm(k): V(tuple(Ct @ v / 100)) for k, v in Pos.items()}
    T = {nm(k): V(tuple(Ct @ tails[k] / 100)) for k in tails}
    return J, T


def _push(J, T, bg, a):
    """the shift that takes the blade (30-100 % down from the top, as the audit samples it) out of every capsule"""
    d = V((0, 0, 0))
    for _ in range(4):
        top = V(bg) + d - a * GRIP_R
        worst, push = 0.0, None
        for i in range(11):
            p = top + a * (BAT_LEN * (0.30 + 0.70 * i / 10.0))
            for ja, jb, r in CAPS:
                if ja not in J:
                    continue
                A = J[ja]
                B = J[jb] if jb in J else T.get(ja, A)
                c = _seg(p, A, B)
                off = p - c
                pen = r + MARGIN - off.length
                if pen > worst:
                    n = off.normalized() if off.length > 1e-5 else V((0, -1, 0))
                    worst, push = pen, n * pen
        if push is None:
            break
        d = d + push
    return d


def _smooth(raw, w=2.0):
    out = []
    n = len(raw)
    for i in range(n):
        js = range(max(0, i - 3 * int(w) - 1), min(n, i + 3 * int(w) + 2))
        ws = [math.exp(-((j - i) / w) ** 2 / 2) for j in js]
        env = sum((raw[j] * wj for j, wj in zip(js, ws)), V((0, 0, 0))) / sum(ws)
        # envelope: never less than the frame's own need along its own direction
        if raw[i].length > 1e-6 and env.dot(raw[i].normalized()) < raw[i].length:
            env = env + raw[i].normalized() * (raw[i].length - env.dot(raw[i].normalized()))
        out.append(env)
    return out


def compute(rig, keys, n, tc, name):
    raw = []
    for f in range(n):
        pose = sample(keys, f)
        batting.step_lift(keys, f, pose)
        if tc is not None and abs(f - tc) <= KEEP:
            raw.append(V((0, 0, 0)))
            continue
        J, T = _joints(rig, pose)
        raw.append(_push(J, T, pose["bg"], V(pose["ba"]).normalized()))
    sm = _smooth(raw)
    if tc is not None:                       # ease back to zero into the contact frames
        for f in range(n):
            g = abs(f - tc)
            if g <= KEEP:
                sm[f] = V((0, 0, 0))
            elif g <= KEEP + 4:
                sm[f] = sm[f] * ((g - KEEP) / 5.0)
    os.makedirs(CACHE, exist_ok=True)
    rows = [[round(c, 4) for c in v] for v in sm]
    json.dump(dict(clip=name, frames=n, contact=tc, offsets=rows), open(os.path.join(CACHE, name + ".json"), "w"))
    moved = [v.length for v in sm]
    print(f"  bat clear {name}: {sum(1 for m in moved if m > 0.005)} frames moved, max {max(moved):.3f} m", flush=True)
    return rows


def apply(name, keys, n):
    """the keys with the cached shifts baked in (dense, one key a frame); unchanged if there's no cache"""
    p = os.path.join(CACHE, name + ".json")
    if not os.path.exists(p):
        return keys
    d = json.load(open(p))
    if d.get("frames") != n:
        return keys
    out = []
    for f in range(n):
        q = sample(keys, f)
        q["bg"] = V(q["bg"]) + V(d["offsets"][f])
        out.append((f, q))
    return out


def wanted():
    return bool(os.environ.get("APL_BAT_CLEAR"))
