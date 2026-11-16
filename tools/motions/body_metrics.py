"""
Body posture numbers from dump_pose.py's JSON (what motion.ps1 preview writes
next to the sheet), at chosen frames: the figures a body reviewer judges a
batting clip by — trunk inclination toward the bowler and toward the off side,
chest opening, head nod and lateral tilt, knee flexion, hip height, feet
spacing, hand height and the hands' clearance from the hips and the face.

    python tools/motions/body_metrics.py build/motions/work/<clip>.pose.json --frames 1,22,31,40,90

Batting frame (right-hander): +X toward the bowler, -Y off side, Z up. For an
lhb_ clip pass --lhb (the off side is +Y). Frames are 1-based as the packs count.

  trunk_to_bowler_deg   hips->neck tipped toward +X (the bowler); negative = toward the keeper
  trunk_to_off_deg      hips->neck tipped toward the off side (the chest side in stance); negative = leg side
  chest_open_deg        0 = side-on, 90 = chest square to the bowler, negative = closed toward the keeper
  head_nod_deg          head's up axis tipped toward where the chest points (looking down); negative = chin up
  head_tilt_deg         head's up axis tipped along the shoulder line toward the front shoulder (lateral tilt)
  knee_L/R_deg          knee flexion (0 = locked straight)
"""
import argparse
import json
import math

ap = argparse.ArgumentParser()
ap.add_argument("pose")
ap.add_argument("--frames", default="1,22,31,40,90")
ap.add_argument("--lhb", action="store_true")
a = ap.parse_args()
d = json.load(open(a.pose))
first = d["first"]
by = {fr["frame"]: fr for fr in d["frames"]}
P = "mixamorig:"
OFF = 1.0 if a.lhb else -1.0          # unit vector toward the off side = (0, OFF, 0)
UP = [0.0, 0.0, 1.0]


def sub(p, q):
    return [p[i] - q[i] for i in range(3)]


def dot(p, q):
    return sum(p[i] * q[i] for i in range(3))


def cross(p, q):
    return [p[1] * q[2] - p[2] * q[1], p[2] * q[0] - p[0] * q[2], p[0] * q[1] - p[1] * q[0]]


def norm(p):
    n = math.sqrt(dot(p, p))
    return [c / n for c in p] if n > 1e-9 else [0.0, 0.0, 0.0]


def dist(p, q):
    return math.sqrt(dot(sub(p, q), sub(p, q)))


def deg(x):
    return math.degrees(x)


def angle(p, q):
    return deg(math.acos(max(-1.0, min(1.0, dot(norm(p), norm(q))))))


def metrics(fr):
    b = fr["bones"]

    def H(n):
        return [float(c) for c in b[P + n][0]]

    hips, neck, head, top = H("Hips"), H("Neck"), H("Head"), H("HeadTop_End")
    lsh, rsh = H("LeftArm"), H("RightArm")
    lhand, rhand = H("LeftHand"), H("RightHand")
    out = {}
    out["hip_z"] = hips[2]
    trunk = norm(sub(neck, hips))
    out["trunk_to_bowler_deg"] = deg(math.asin(max(-1, min(1, trunk[0]))))
    out["trunk_to_off_deg"] = deg(math.asin(max(-1, min(1, OFF * trunk[1]))))
    out["trunk_total_deg"] = angle(trunk, UP)
    shn = norm(sub(lsh, rsh))                       # shoulder line, right -> left
    chest = norm(cross(shn, UP))                    # out of the chest
    out["chest_open_deg"] = deg(math.atan2(chest[0], OFF * chest[1]))
    hu = norm(sub(top, head))                       # head up axis
    out["head_nod_deg"] = deg(math.atan2(dot(hu, chest), hu[2]))
    out["head_tilt_deg"] = deg(math.atan2(dot(hu, shn), hu[2]))
    out["head_total_deg"] = angle(hu, UP)
    for side, tag in (("Left", "L"), ("Right", "R")):
        th = sub(H(side + "Leg"), H(side + "UpLeg"))
        sn = sub(H(side + "Foot"), H(side + "Leg"))
        out[f"knee_{tag}_deg"] = angle(th, sn)
        out[f"toe_{tag}_z"] = H(side + "ToeBase")[2]
    out["feet_apart_m"] = dist(H("LeftToeBase"), H("RightToeBase"))
    out["feet_dx_m"] = H("LeftToeBase")[0] - H("RightToeBase")[0]
    out["feet_dy_m"] = H("LeftToeBase")[1] - H("RightToeBase")[1]
    hc = [(lhand[i] + rhand[i]) / 2 for i in range(3)]
    out["hands_z"] = hc[2]
    out["hands_dx_from_hips_m"] = hc[0] - hips[0]
    out["hands_z_minus_shoulder"] = hc[2] - (lsh[2] + rsh[2]) / 2
    out["hands_to_hip_m"] = min(dist(h, j) for h in (lhand, rhand) for j in (H("LeftUpLeg"), H("RightUpLeg"), hips))
    out["hands_to_head_m"] = min(dist(h, head) for h in (lhand, rhand))
    out["elbow_L_deg"] = 180 - angle(sub(H("LeftForeArm"), lsh), sub(lhand, H("LeftForeArm")))
    out["elbow_R_deg"] = 180 - angle(sub(H("RightForeArm"), rsh), sub(rhand, H("RightForeArm")))
    return out


want = [int(x) for x in a.frames.split(",") if x.strip()]
rows = [(f, metrics(by[first + f - 1])) for f in want if (first + f - 1) in by]
keys = list(rows[0][1].keys()) if rows else []
print(f"{d['file']}  ({len(d['frames'])} frames)")
print(f"{'metric':26s}" + "".join(f"{'f' + str(f):>9s}" for f, _ in rows))
for k in keys:
    print(f"{k:26s}" + "".join(f"{m[k]:9.2f}" for _, m in rows))
