"""
Bat-grip check (headless, no Unity): every bat-holding clip's hands, wrists, elbows and blade against its bat
track, from dump_many.py's pose dumps + Resources/Data/cricket/bat_tracks.json. Right-handed clips (the
left-hander's are the mirror, so the same numbers hold).

    blender -b --python tools/motions/dump_many.py -- build/motions/work/batgrip @build/motions/work/batgrip/list.txt
    python tools/motions/batgrip_check.py build/motions/work/batgrip [--clip 02_straight_drive] [--frames]

Generator frame: metres, Z up, bowler +X, a right-hander's chest (off side) -Y. A track row is
grip (bottom hand's point on the handle, GRIP_R below the handle top), axis (down the blade), face normal, top-hand flag.

Per clip (over the frames where the hand is on the handle):
  gapB/gapT    knuckle centre (index..little MCP) -> handle axis, cm: max.  A fist round a 3.5 cm handle: 2-5 cm.
  slideB/T     along the handle, cm from the handle top (top hand 2-12, bottom 10-24; outside 0-30 = off the handle)
  wristB/T     angle forearm -> blade, deg (0 = the bat continues the forearm): min..max. Human: 0-110.
  bendB/T      hand bent off the forearm (forearm vs wrist->middle knuckle), deg: max. Human <= ~75.
  idxUp%       frames with the index knuckle ABOVE the little knuckle on the handle (a real grip: index toward the blade)
  elbB/T       elbow flexion, deg (0 = straight): min..max;  flip = frames where the elbow's pointing turns > 100 deg in one frame
  pen          blade inside a body capsule (torso, head, thighs, shins): frames, max depth cm
  goalB/T      the GAME's IK wrist goal (BatterRig.Sample: grip - fingers*0.075 -/+ face*0.03) vs the clip's own wrist, cm: max.
               Big = the game drags the wrist off the clip's pose (bent/flipped elbows).
  rollB/T      the GAME's OrientHand target (knuckle line little->index toward the handle TOP) vs the clip's own, deg: median.
               ~180 = the game turns the hand over on the handle.
"""
import argparse
import glob
import json
import math
import os
import statistics as st

import numpy as np

GRIP_R, BAT_LEN, HANDLE, HANDLE_RAD = 0.17, 0.86, 0.30, 0.018
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..")).replace("\\", "/")  # repo root
TRACKS = _APL_REPO + "/client-src/Assets/Resources/Data/cricket/bat_tracks.json"
CAPS = [("Hips", "Spine2", 0.12), ("Spine2", "Neck", 0.11), ("Head", "HeadTop_End", 0.10),
        ("LeftUpLeg", "LeftLeg", 0.075), ("RightUpLeg", "RightLeg", 0.075),
        ("LeftLeg", "LeftFoot", 0.065), ("RightLeg", "RightFoot", 0.065)]


def U(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def ang(u, v):
    return math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(U(u), U(v)))))))


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, float(np.dot(p - a, ab) / max(float(np.dot(ab, ab)), 1e-9))))
    return float(np.linalg.norm(p - (a + ab * t)))


def head(fr, n):
    b = fr["bones"].get(n) or fr["bones"].get("mixamorig:" + n)
    return None if b is None else np.array(b[0])


def tail(fr, n):
    b = fr["bones"].get(n) or fr["bones"].get("mixamorig:" + n)
    return None if b is None else np.array(b[1])


def hand(fr, side):
    """wrist, elbow, shoulder, knuckle centre, index MCP, little MCP, middle MCP of one hand"""
    g = lambda n: head(fr, side + n)
    k = [g("HandIndex1"), g("HandMiddle1"), g("HandRing1"), g("HandPinky1")]
    if any(x is None for x in k):
        return None
    return dict(wrist=g("Hand"), elbow=g("ForeArm"), shoulder=g("Arm"), knuck=sum(k) / 4, index=k[0], little=k[3], middle=k[1])


def game_goals(g, a, n):
    """BatterRig.Sample's wrist goals (generator frame) and its OrientHand knuckle-line target."""
    across = np.cross(a, n)
    fingers = U(across + a * 0.35)
    top = g - a * GRIP_R
    bottom = g - fingers * 0.075 - n * 0.03
    topw = top + a * 0.065 - fingers * 0.075 + n * 0.03
    kwant = U(-a - fingers * float(np.dot(-a, fingers)))
    return bottom, topw, fingers, kwant


def analyse(pose, rows, show_frames=False):
    frames = pose["frames"]
    n = min(len(frames), len(rows))
    out = {k: [] for k in ("gapB", "gapT", "slideB", "slideT", "wristB", "wristT", "bendB", "bendT", "elbB", "elbT", "goalB", "goalT", "rollB", "rollT")}
    idx_up = [0, 0]
    pen_frames, pen_max = 0, 0.0
    flips = [0, 0]
    prev_point = [None, None]
    lines = []
    for i in range(n):
        fr, r = frames[i], rows[i]
        g, a, nf, topon = np.array(r[0:3]), U(np.array(r[3:6])), U(np.array(r[6:9])), (len(r) < 10 or r[9] > 0.5)
        top = g - a * GRIP_R
        hb, ht = hand(fr, "Right"), hand(fr, "Left")
        if hb is None or ht is None:
            continue
        gb, gt, fingers, kwant = game_goals(g, a, nf)
        row = {}
        for tag, h, on, goal, side in (("B", hb, True, gb, 0), ("T", ht, topon, gt, 1)):
            fore = h["wrist"] - h["elbow"]
            upper = h["elbow"] - h["shoulder"]
            elb = ang(upper, fore)
            out["elb" + tag].append(elb)
            # where the elbow points (perpendicular to shoulder->wrist), for flip detection
            sw = U(h["wrist"] - h["shoulder"])
            pt = h["elbow"] - h["shoulder"]
            pt = pt - sw * float(np.dot(pt, sw))
            if elb > 20 and np.linalg.norm(pt) > 1e-3:
                if prev_point[side] is not None and ang(prev_point[side], pt) > 100:
                    flips[side] += 1
                prev_point[side] = pt
            if not on:
                continue
            gap = float(np.linalg.norm(np.cross(h["knuck"] - top, a)))
            slide = float(np.dot(h["knuck"] - top, a))
            wrist = ang(fore, a)
            bend = ang(fore, h["middle"] - h["wrist"])
            k = h["index"] - h["little"]
            if float(np.dot(k, a)) < 0:
                idx_up[side] += 1
            kp = k - fingers * float(np.dot(k, fingers))
            roll = ang(kp, kwant) if np.linalg.norm(kp) > 1e-4 else 0.0
            goal_d = float(np.linalg.norm(goal - h["wrist"]))
            for key, v in (("gap", gap), ("slide", slide), ("wrist", wrist), ("bend", bend), ("goal", goal_d), ("roll", roll)):
                out[key + tag].append(v)
            row[tag] = (gap, slide, wrist, bend, elb, goal_d, roll)
        # blade inside the body
        worst = 0.0
        for frac in np.linspace(0.30, 1.0, 8):
            p = top + a * (BAT_LEN * frac)
            for ja, jb, rad in CAPS:
                pa, pb = head(fr, ja), (head(fr, jb) if jb != "HeadTop_End" or head(fr, jb) is not None else tail(fr, ja))
                if pb is None:
                    pb = tail(fr, ja)
                if pa is None or pb is None:
                    continue
                worst = max(worst, rad - seg_dist(p, pa, pb))
        if worst > 0.01:
            pen_frames += 1
            pen_max = max(pen_max, worst)
        if show_frames:
            def f(t):
                if t not in row:
                    return "      --      "
                gp, sl, wr, bd, el, gd, ro = row[t]
                return f"gap {gp*100:4.1f} sl {sl*100:5.1f} wr {wr:5.1f} bd {bd:5.1f} el {el:5.1f} goal {gd*100:4.1f} roll {ro:5.1f}"
            lines.append(f"  f{i+1:3d} B[{f('B')}] T[{f('T')}] pen {max(0,worst)*100:4.1f}")
    return out, idx_up, flips, pen_frames, pen_max, n, lines


# ---- the round-14 palm grip (BatterRig.HandModel.cs) re-run on each clip's own arms: what the game will show.
# Same constants and steps as the C#: fingers held 25 deg down the handle on the face's side, turned round the
# handle toward the forearm until the hand is <= 50 deg off it, wrist goal from the palm, two-bone IK with the
# elbow flexion clamped to 4..145, and the elbow swivelled (+-60 deg) so the forearm lines up with the grip.
DIAG, MAX_BEND, FLEX_MAX, FLEX_MIN, REACH, DEPTH = 25.0, 50.0, 145.0, 4.0, 0.95, 0.30
ALONG = {"B": 0.18, "T": 0.085}
SW_COST, CONT_COST, SW_MAX = float(os.environ.get("SW_COST", 0.3)), float(os.environ.get("CONT_COST", 0.6)), float(os.environ.get("SW_MAX", 60))


def rot(v, axis, deg):
    axis = U(axis)
    t = math.radians(deg)
    return v * math.cos(t) + np.cross(axis, v) * math.sin(t) + axis * float(np.dot(axis, v)) * (1 - math.cos(t))


def slerp_dir(u, v, t):
    u, v = U(u), U(v)
    om = math.acos(max(-1.0, min(1.0, float(np.dot(u, v)))))
    if om < 1e-5:
        return u
    return U((math.sin((1 - t) * om) * u + math.sin(t * om) * v) / math.sin(om))


def limit_bend(ff, u, a):
    if ang(u, ff) <= MAX_BEND:
        return ff
    up = u - a * float(np.dot(u, a))
    if np.linalg.norm(up) < 1e-6:
        return ff
    up = U(up)
    fp = U(ff - a * float(np.dot(ff, a)))
    dc = float(np.dot(ff, a))
    pl = math.sqrt(max(0.0, 1 - dc * dc))
    full = math.degrees(math.atan2(float(np.dot(np.cross(fp, up), a)), float(np.dot(fp, up))))
    for t in np.linspace(0.1, 1.0, 10):
        f = rot(fp, a, full * t) * pl + a * dc
        if ang(u, f) <= MAX_BEND:
            return U(f)
    fb = U(rot(fp, a, full) * pl + a * dc)
    return slerp_dir(u, fb, MAX_BEND / max(MAX_BEND, ang(u, fb)))


def two_bone(s, e, w, t):
    la, lb = np.linalg.norm(e - s), np.linalg.norm(w - e)
    dmin = math.sqrt(la * la + lb * lb - 2 * la * lb * math.cos(math.radians(180 - FLEX_MAX)))
    dmax = math.sqrt(la * la + lb * lb - 2 * la * lb * math.cos(math.radians(180 - FLEX_MIN)))
    d = min(max(float(np.linalg.norm(t - s)), dmin), dmax)
    dr = U(t - s)
    perp = (e - s) - dr * float(np.dot(e - s, dr))
    if np.linalg.norm(perp) < 1e-6:
        perp = np.cross(dr, [0, 0, 1.0])
    perp = U(perp)
    al = math.acos(max(-1.0, min(1.0, (la * la + d * d - lb * lb) / (2 * la * d))))
    e2 = s + dr * la * math.cos(al) + perp * la * math.sin(al)
    w2 = e2 + U(s + dr * d - e2) * lb
    return e2, w2


def elbow_point(e, s, ax):
    v = e - s
    return U(v - ax * float(np.dot(v, ax)))


def solve_arm(h, pc, a, ff, nsign, face, L, last):
    s, e, w = h["shoulder"], h["elbow"], h["wrist"]
    phi_used = 0.0
    for it in range(2):
        u = U(w - e)
        f = limit_bend(ff, u, a)
        k = U(a - f * float(np.dot(a, f)))
        n = U(np.cross(k, f))
        if float(np.dot(n, face)) * nsign < 0:
            n = -n
        goal = pc - f * (REACH * L) - n * (DEPTH * L)
        e, w = two_bone(s, e, w, goal)
        if it == 0:
            ax = U(w - s)
            best, bc = 0.0, 1e9
            for phi in np.arange(-SW_MAX, SW_MAX + 0.1, 7.5):
                e2 = s + rot(e - s, ax, phi)
                c = ang(w - e2, ff) + SW_COST * abs(phi)
                if last is not None:
                    c += CONT_COST * ang(elbow_point(e2, s, ax), last)
                if c < bc:
                    bc, best = c, phi
            e = s + rot(e - s, ax, best)
            phi_used = best
    u = U(w - e)
    f = limit_bend(ff, u, a)
    return dict(bend=ang(u, f), wrist=ang(u, a), elb=ang(e - s, w - e), phi=abs(phi_used), ept=elbow_point(e, s, U(w - s)),
                miss=float(np.linalg.norm(w - goal)))


def analyse_after(pose, rows):
    frames = pose["frames"]
    n = min(len(frames), len(rows))
    out = {k: [] for k in ("bendB", "bendT", "wristB", "wristT", "elbB", "elbT", "phiB", "phiT", "missB", "missT")}
    last = {"B": None, "T": None}
    flips = {"B": 0, "T": 0}
    for i in range(n):
        fr, r = frames[i], rows[i]
        g, a, nf, topon = np.array(r[0:3]), U(np.array(r[3:6])), U(np.array(r[6:9])), (len(r) < 10 or r[9] > 0.5)
        top = g - a * GRIP_R
        fg = U(np.cross(a, nf) + a * 0.35)
        fp = U(fg - a * float(np.dot(fg, a)))
        ff = U(fp * math.cos(math.radians(DIAG)) + a * math.sin(math.radians(DIAG)))
        for tag, side, on, sg in (("B", "Right", True, 1.0), ("T", "Left", topon, -1.0)):
            h = hand(fr, side)
            if h is None or not on:
                last[tag] = None
                continue
            L = float(np.linalg.norm(h["middle"] - h["wrist"]))
            res = solve_arm(h, top + a * ALONG[tag], a, ff, sg, nf, L, last[tag])
            if last[tag] is not None and ang(res["ept"], last[tag]) > 100:
                flips[tag] += 1
            last[tag] = res["ept"]
            for k in ("bend", "wrist", "elb", "phi", "miss"):
                out[k + tag].append(res[k])
    return out, flips


def fmt(v, mode):
    if not v:
        return "   -   "
    if mode == "max":
        return f"{max(v):5.1f}"
    if mode == "med":
        return f"{st.median(v):5.0f}"
    return f"{min(v):4.0f}..{max(v):3.0f}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--clip", default="")
    ap.add_argument("--frames", action="store_true")
    ap.add_argument("--tracks", default=TRACKS)
    ap.add_argument("--after", action="store_true", help="also re-run the round-14 palm grip on each clip's arms")
    a = ap.parse_args()
    tracks = json.load(open(a.tracks))["clips"]
    paths = sorted(glob.glob(os.path.join(a.dir, "*.pose.json")))
    print(f"{'clip':34s} {'n':>3s} | {'gapB':>5s} {'gapT':>5s} | {'slideB':>9s} {'slideT':>9s} | {'wristB':>8s} {'wristT':>8s} | {'bendB':>5s} {'bendT':>5s} | {'elbB':>8s} {'elbT':>8s} flip | idxUp% B/T | pen fr/cm | {'goalB':>5s} {'goalT':>5s} | rollB rollT")
    agg = {"goal": [], "roll": [], "idx": [0, 0], "pen": 0, "wrist_bad": 0, "bend_bad": 0, "frames": 0}
    for p in paths:
        clip = os.path.basename(p)[:-len(".pose.json")]
        if a.clip and clip != a.clip:
            continue
        rows = tracks.get(clip)
        if rows is None:
            continue
        pose = json.load(open(p))
        out, idx_up, flips, pf, pm, n, lines = analyse(pose, rows, a.frames)
        nb, nt = max(1, len(out["gapB"])), max(1, len(out["gapT"]))
        cm = lambda v: [x * 100 for x in v]
        print(f"{clip[:34]:34s} {n:3d} | {fmt(cm(out['gapB']),'max')} {fmt(cm(out['gapT']),'max')} | "
              f"{fmt(cm(out['slideB']),'rng'):>9s} {fmt(cm(out['slideT']),'rng'):>9s} | {fmt(out['wristB'],'rng'):>8s} {fmt(out['wristT'],'rng'):>8s} | "
              f"{fmt(out['bendB'],'max')} {fmt(out['bendT'],'max')} | {fmt(out['elbB'],'rng'):>8s} {fmt(out['elbT'],'rng'):>8s} {flips[0]:2d}/{flips[1]:<2d}| "
              f"{100*idx_up[0]/nb:4.0f}/{100*idx_up[1]/nt:<4.0f}   | {pf:3d} {pm*100:4.1f}  | {fmt(cm(out['goalB']),'max')} {fmt(cm(out['goalT']),'max')} | {fmt(out['rollB'],'med')} {fmt(out['rollT'],'med')}")
        for l in lines:
            print(l)
        agg.setdefault("bend_all", []).extend(out["bendB"] + out["bendT"])
        if a.after:
            o2, fl = analyse_after(pose, rows)
            print(f"{'   after (palm grip)':34s}     | bend B {fmt(o2['bendB'],'max')} T {fmt(o2['bendT'],'max')} | wrist B {fmt(o2['wristB'],'rng')} T {fmt(o2['wristT'],'rng')} | "
                  f"elb B {fmt(o2['elbB'],'rng')} T {fmt(o2['elbT'],'rng')} flip {fl['B']}/{fl['T']} | swivel max B {fmt(o2['phiB'],'max')} T {fmt(o2['phiT'],'max')} | wrist miss cm B {fmt(cm(o2['missB']),'max')} T {fmt(cm(o2['missT']),'max')}")
            agg.setdefault("after_bend", []).extend(o2["bendB"] + o2["bendT"])
            agg.setdefault("after_wrist", []).extend(o2["wristB"] + o2["wristT"])
            agg.setdefault("after_elb", []).extend(o2["elbB"] + o2["elbT"])
            agg.setdefault("after_miss", []).extend(o2["missB"] + o2["missT"])
            agg.setdefault("after_flip", []).append(fl["B"] + fl["T"])
        agg["frames"] += n
        agg["goal"] += out["goalB"] + out["goalT"]
        agg["roll"] += out["rollB"] + out["rollT"]
        agg["idx"][0] += idx_up[0] + idx_up[1]
        agg["idx"][1] += len(out["gapB"]) + len(out["gapT"])
        agg["pen"] += pf
        agg["wrist_bad"] += sum(1 for w in out["wristB"] + out["wristT"] if w > 110)
        agg["bend_bad"] += sum(1 for w in out["bendB"] + out["bendT"] if w > 75)
    if agg["goal"]:
        print(f"\nALL: {agg['frames']} frames | game wrist goal vs clip wrist: median {st.median(agg['goal'])*100:.1f} cm, max {max(agg['goal'])*100:.1f} cm"
              f" | game hand roll vs clip: median {st.median(agg['roll']):.0f} deg | index-above-little {100*agg['idx'][0]/max(1,agg['idx'][1]):.0f}% of hand-frames"
              f" | wrist>110: {agg['wrist_bad']} | bend>75: {agg['bend_bad']} | blade-in-body frames: {agg['pen']}")
        bb = agg["bend_all"]
        print(f"BEFORE hand bent off the forearm: median {st.median(bb):.0f} deg, >50 deg in {sum(1 for x in bb if x > 50.5)} of {len(bb)} hand-frames")
        if "after_bend" in agg:
            ab, aw, ae, am = agg["after_bend"], agg["after_wrist"], agg["after_elb"], agg["after_miss"]
            print(f"AFTER  hand bent off the forearm: median {st.median(ab):.0f} deg, >50 deg in {sum(1 for x in ab if x > 50.5)} of {len(ab)}"
                  f" | forearm->blade >110: {sum(1 for x in aw if x > 110)} (before {agg['wrist_bad']}) | elbow flex >145.5: {sum(1 for x in ae if x > 145.5)}"
                  f" | elbow flips: {sum(agg['after_flip'])} | wrist short of its goal >2 cm: {sum(1 for x in am if x > 0.02)}")


if __name__ == "__main__":
    main()
