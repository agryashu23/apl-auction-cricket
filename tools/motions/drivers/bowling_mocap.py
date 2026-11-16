"""
Round 13: bowling from the owner's Rokoko takes (build/mocap/ROKOKO_FINAL, exported as <clip>_mixamo.fbx and
extracted by mocap_extract.py into work/mocap/raw/<take>.json).

A take is ~3 s: the last strides, the bound, back-foot contact (BFC), front-foot contact (FFC), release (REL) and
the follow-through.  Each target clip keeps its procedural run-up from bowling_real.build() (the mark, the
approach, its length and line); from a few frames before BFC the body and both arms come from the take:
  time   the take's BFC / FFC / REL are found in it (feet plants and the bowling hand's highest point) and
         time-warped onto the clip's own BFC / FFC / REL, so the clip's metadata frames (and the game's release
         timing) stay exactly as they were; after REL the take runs at its own pace, then hands back to the
         procedural follow-through / stop over 12 frames
  space  the take is turned so its run goes down the pitch (-Y) and moved so its front foot lands where the
         clip's front foot lands at FFC (over / around the wicket kept)
  arms   both wrists placed from our solved shoulders along the take's shoulder->wrist (scaled to our arms),
         elbow poles from the take's elbows; the procedural hand / palm / finger channels (the variation's
         wrist work: googly, cutter, knuckle...) are kept
  hand   right-arm space throughout: a left-arm take is mirrored in, a left-arm target mirrored out

    motion.ps1 gen drivers\\bowling_mocap.py [<take> ...] [--check]
Output: build/motions/bowling_r13 and bowling_fast_r13 (FBX + per-clip JSON like bowling_real's), work/r13/bowl_<clip>.txt
"""
import json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "lib"))
import numpy as np
import mocap_lib as ml
import bowling_real as BR
from bowling import mirror
from fielding import V, C, PFX, FieldRig, load_rig

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
RAW = _APL_REPO + "/build/motions/work/mocap/raw"
OUT = {"main": os.path.join(BR.mm.APL_OUT, "bowling_r13"), "fast": os.path.join(BR.mm.APL_OUT, "bowling_fast_r13")}
PRE, POST = 8, 12

def fam(prefix, la):
    return [k for k, v in BR.MAIN.items() if k.startswith(prefix + "_") and v["left"] == la]

# take -> (its bowling arm, the target clips it drives: MAIN / FAST names)
TAKES = {
    # ev: the take's back-foot contact / front-foot contact / release (mocap frames = video frame - 1), read off the
    # videos frame by frame (the automatic guess -- highest hand, last plants -- caught run-up arm swings on 03-05)
    "02_bowl_leg_spin":         dict(left=False, ev=(13, 16, 18), main=fam("ra_leg_spin", False) + fam("la_chinaman", True)),
    "03_bowl_right_arm_medium": dict(left=False, ev=(30, 32, 33.5), main=fam("ra_medium", False) + fam("ra_slow_medium", False) + fam("ra_fast_medium", False) + fam("la_fast_medium", True)),
    "04_bowl_left_arm_fast":    dict(left=True, ev=(46, 50, 52), main=fam("la_fast", True) + fam("ra_express_fast", False), fast=list(BR.FAST)),
    "05_bowl_off_spin":         dict(left=False, ev=(49, 53, 56), main=fam("ra_off_spin", False) + fam("la_orthodox", True)),
}


def take(name, left, given=None):
    """the take's channels per frame (right-arm space, run toward -Y), its events, the arm-length ratio"""
    M = ml.ground_feet(ml.load(os.path.join(RAW, name + ".json")))
    P, n = M["P"], M["n"]
    if left:                                    # a left-armer: mirror x and swap the sides (right-arm space)
        sw = lambda k: "Right" + k[4:] if k.startswith("Left") else "Left" + k[5:] if k.startswith("Right") else k
        P = {sw(k): v * np.array([-1, 1, 1]) for k, v in P.items()}
        M["P"] = P
        M["Q"] = {k: [type(q)((q.w, q.x, -q.y, -q.z)) for q in qs] for k, qs in M["Q"].items()}
        M["Left_planted"], M["Right_planted"] = M.get("Right_planted"), M.get("Left_planted")
    # turn the run down the pitch: the hips' travel from the start to the release toward -Y
    rel = given[2] if given else int(np.argmax(P["RightHand"][:, 2]))
    d = P["Hips"][int(rel), :2] - P["Hips"][0, :2]
    ang = math.atan2(d[0], -d[1])               # rotate by -ang about Z to put d on -Y
    c, s = math.cos(-ang), math.sin(-ang)
    Rz = np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
    for k in P: P[k] = P[k] @ Rz.T
    from mathutils import Matrix
    q = Matrix(Rz.tolist()).to_quaternion()
    M["Q"] = {k: [q @ x for x in qs] for k, qs in M["Q"].items()}
    chs = ml.filtered([ml.channels(M, f) for f in range(n)])
    for f in range(n):                          # elbows, for the arm poles
        chs[f]["relb"], chs[f]["lelb"] = V(P["RightForeArm"][f]), V(P["LeftForeArm"][f])
    # events: release = the bowling hand's highest point; FFC / BFC = the front / back foot's last plant before it
    def plant_before(pl, t):
        if pl is None: return None
        for f in range(t, 0, -1):
            if pl[f] and not pl[f - 1]: return f
        return None
    ffc = plant_before(M.get("Left_planted"), int(rel) + 1) or int(rel) - 3
    bfc = plant_before(M.get("Right_planted"), ffc - 1) or ffc - 6
    if given: bfc, ffc, rel = given
    ev = dict(BFC=bfc, FFC=ffc, REL=rel, END=n - 1)
    return chs, ev, ml.arm_len(M)


def warp(t, ev_c, ev_m):
    """clip frame -> take frame"""
    pts = [(ev_c["BFC"] - PRE, ev_m["BFC"] - PRE), (ev_c["BFC"], ev_m["BFC"]), (ev_c["FFC"], ev_m["FFC"]), (ev_c["REL"], ev_m["REL"])]
    if t <= pts[0][0]: return pts[0][1] + (t - pts[0][0])
    for (a, x), (b, y) in zip(pts, pts[1:]):
        if t <= b: return x + (y - x) * (t - a) / max(b - a, 1e-6)
    return ev_m["REL"] + (t - ev_c["REL"])


def sample(chs, x):
    i = int(max(0, min(len(chs) - 2, math.floor(x)))); k = max(0.0, min(1.0, x - i))
    a, b = chs[i], chs[i + 1]
    return {key: (a[key].lerp(b[key], k) if hasattr(a[key], "lerp") else a[key] * (1 - k) + b[key] * k) for key in a}


BODY = ("hips", "ptilt", "pside", "pyaw", "lean", "side", "twist", "rball", "lball", "rtoe", "ltoe", "rheel", "lheel", "rkp", "lkp")


def splice(spec, chs, ev_m, arm_len_m, rig):
    """the clip's procedural poses with the take's body and arms from PRE frames before BFC to POST after the take ends"""
    n, poses, ev, info = BR.build(spec["action"], spec["side"], spec["variant"])
    k_arm = (rig.L1 + rig.L2) / 100 / arm_len_m
    # the take's front foot onto the clip's front foot at FFC
    fm = V(sample(chs, ev_m["FFC"])["lball"]); fc = V(poses[ev["FFC"]]["lball"])
    off_ffc = V((fc.x - fm.x, fc.y - fm.y, 0))
    t_end = ev["REL"] + (ev_m["END"] - ev_m["REL"])
    t0 = ev["BFC"] - PRE
    # where the blend starts the take's hips sit on the run-up's (no jump), easing to the front-foot match by FFC
    ts = max(0, t0 - PRE)
    hm = V(sample(chs, warp(ts, ev, ev_m))["hips"]); hc = V(poses[ts]["hips"])
    off_start = V((hc.x - hm.x, hc.y - hm.y, 0))
    for t in range(max(0, t0 - PRE), min(n, t_end + POST)):
        w = BR.smooth01((t - (t0 - PRE)) / PRE) * (1 - BR.smooth01((t - t_end) / POST))
        if w <= 0: continue
        m = sample(chs, warp(t, ev, ev_m))
        p = poses[t]
        off = off_start.lerp(off_ffc, BR.smooth01((t - ts) / max(1.0, ev["FFC"] - ts)))
        for key in BODY:
            v = m[key] + off if key in ("hips", "rball", "lball") else m[key]
            if key in ("rtoe", "ltoe"):
                # a toe pointing the other way (the bound's tracking) would flip the foot: keep the run-up's
                if V(p[key]).dot(V(v)) < 0.2: continue
                v = V(p[key]).lerp(V(v), w).normalized()
                p[key] = v
                continue
            p[key] = V(p[key]).lerp(v, w) if hasattr(v, "lerp") else p[key] * (1 - w) + v * w
        # arms: our shoulders (solved with the take's body) + the take's shoulder->wrist, scaled to our arms
        Pos, _ = rig.solve(p, {}, write=False)
        for k, side_, elb in (("r", "Right", "relb"), ("l", "Left", "lelb")):
            sh = C.transposed() @ Pos[PFX + side_ + "Arm"] / 100
            wr = sh + (m[k + "wrist"] - m[k + "sh"]) * k_arm
            pole = m[elb] - (m[k + "sh"] + m[k + "wrist"]) * 0.5
            p[k + "t"] = V(p[k + "t"]).lerp(wr, w)
            if pole.length > 1e-4: p[k + "p"] = V(p[k + "p"]).lerp(pole.normalized(), w)
            p[k + "dirw"] = p.get(k + "dirw", 0.0) * (1 - w)
            if k == "r":
                # the bowling arm straight through the release (the takes' elbows read 40-80 deg there: a throw), along
                # the take's own shoulder->wrist line
                a_ = m["rwrist"] - m["rsh"]
                bell = max(0.0, 1.0 - abs(t - ev["REL"]) / 6.0)
                if a_.length > 1e-4 and bell > 0:
                    p["rdir"] = a_.normalized()
                    p["rdirw"] = max(p["rdirw"], w * BR.smooth01(bell))
    BR.fix_legs(poses)
    return n, poses, ev, info


def run(take_name, target, kind, check):
    spec = (BR.FAST if kind == "fast" else BR.MAIN)[target]
    cfg = TAKES[take_name]
    chs, ev_m, alen = take(take_name, cfg["left"], cfg.get("ev"))
    arm, _ = load_rig(); rig = FieldRig(arm)
    n, poses, ev, info = splice(spec, chs, ev_m, alen, rig)
    if spec["left"]: poses = [mirror(p) for p in poses]
    print(f"{target} <- {take_name}: take BFC f{ev_m['BFC']+1} FFC f{ev_m['FFC']+1} REL f{ev_m['REL']+1} end f{ev_m['END']+1}; clip BFC f{ev['BFC']+1} FFC f{ev['FFC']+1} REL f{ev['REL']+1}, {n} frames", flush=True)
    if check: return
    od = OUT[kind]
    frames = BR.make_bowl(target, poses, od)
    m = BR.measure(frames, ev, spec["left"])
    # joints per frame for the review sheet (work/r13/bowl_dumps/<clip>.json)
    dd = os.path.join(BR.mm.APL_WORK, "r13", "bowl_dumps"); os.makedirs(dd, exist_ok=True)
    json.dump(dict(clip=target, take=take_name, ev=ev, take_ev=ev_m, frames=[{k[len(PFX):]: [round(c, 3) for c in v] for k, v in fr[0].items()} for fr in frames]),
              open(os.path.join(dd, target + ".json"), "w"))
    print(BR.fmt(m), flush=True)
    A = BR.ACTIONS[spec["action"]]; vr = BR.VARIANTS[spec["variant"]]
    xl = info["xl"]; left_x = -xl if spec["left"] else xl
    meta = dict(file=target + ".fbx", bowler_action=BR.JSON_ACTION.get(spec["action"], spec["action"]), arm="left" if spec["left"] else "right",
                side=spec["side"] + " the wicket", release_variant=spec["variant"], frames=n, seconds=round(n / 30, 2),
                back_foot_contact_frame=ev["BFC"] + 1, front_foot_contact_frame=ev["FFC"] + 1, release_frame=ev["REL"] + 1,
                run_up_m=round(info["ymark"] - info["y_ffc"], 2), start_position_m=dict(behind_crease=round(info["ymark"], 2), left=round(left_x, 2)),
                release_point_m=m["release_point"], elbow_flexion_at_release_deg=round(m["REL"]["elbow_flex"], 1),
                technique=vr.get("note", ""), take_off_frame=ev["T0"] + 1, biomechanics=BR.bio(m, ev), source=f"rokoko mocap: {take_name}")
    if kind == "fast":
        meta = dict(file=target + ".fbx", arm="left-arm" if spec["left"] else "right-arm", action=spec["action"], description=A["desc"],
                    frames=n, seconds=round(n / 30, 2), release_frame=ev["REL"] + 1, back_foot_contact_frame=ev["BFC"] + 1,
                    front_foot_contact_frame=ev["FFC"] + 1, take_off_frame=ev["T0"] + 1, run_up_m=round(info["ymark"] - info["y_ffc"], 2),
                    start_position_m=dict(behind_crease=round(info["ymark"], 2), left=round(left_x, 2)), release_speed_kmh_typical=A["kmh"],
                    release_point_m=m["release_point"], biomechanics=BR.bio(m, ev), source=f"rokoko mocap: {take_name}")
    json.dump(meta, open(os.path.join(od, target + ".json"), "w"), indent=1)


if __name__ == "__main__":
    args = sys.argv[1:]
    check = "--check" in args
    names = [a for a in args if a != "--check"] or list(TAKES)
    for tk in names:
        if not os.path.exists(os.path.join(RAW, tk + ".json")):
            print(f"{tk}: no raw take yet (work/mocap/raw/{tk}.json) - skipped", flush=True)
            continue
        for kind in ("main", "fast"):
            for target in TAKES[tk].get(kind, []):
                run(tk, target, kind, check)
