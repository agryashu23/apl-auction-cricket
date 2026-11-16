"""
Head / gaze pitch of a clip from dump_pose.py's JSON (what motion.ps1 preview writes): per frame, how far the
eyes look below level, and the trunk's forward lean. Gaze 0 = level (the T-pose rest head looks straight ahead;
its Head->HeadTop axis already leans 18.7 deg forward, so that is subtracted), + = looking down.

    python tools/motions/head_pitch.py <pose.json> [<pose.json> ...] [--frames 1,30,60]
"""
import argparse
import json
import math

REST_TIP = 18.7          # Head->HeadTop_End forward tip in the rig's rest pose (degrees)
P = "mixamorig:"


def sub(a, b): return [a[i] - b[i] for i in range(3)]
def dot(a, b): return sum(a[i] * b[i] for i in range(3))
def norm(a):
    n = math.sqrt(dot(a, a)); return [c / n for c in a] if n > 1e-9 else [0.0, 0.0, 0.0]
def cross(a, b): return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def frame_metrics(fr):
    b = fr["bones"]
    H = lambda n: b[P + n][0]
    fwd = norm(cross(sub(H("LeftArm"), H("RightArm")), [0, 0, 1]))       # chest normal, horizontal
    up = norm(sub(H("HeadTop_End"), H("Head")))
    tip = math.degrees(math.atan2(dot(up, fwd), up[2]))                   # forward tip of the head's up axis
    trunk = norm(sub(H("Neck"), H("Hips")))
    lean = math.degrees(math.atan2(dot(trunk, fwd), trunk[2]))
    return tip - REST_TIP, lean


ap = argparse.ArgumentParser()
ap.add_argument("poses", nargs="+")
ap.add_argument("--frames", default="")
a = ap.parse_args()
for path in a.poses:
    d = json.load(open(path))
    rows = [frame_metrics(fr) for fr in d["frames"]]
    g = [r[0] for r in rows]; l = [r[1] for r in rows]
    worst = max(range(len(g)), key=lambda i: g[i])
    print(f"{d['file']:40s} gaze below level: mean {sum(g)/len(g):5.1f}  max {g[worst]:5.1f} @f{worst + 1}  min {min(g):5.1f}"
          f"   trunk lean: mean {sum(l)/len(l):4.1f} max {max(l):4.1f}")
    for f in [int(x) for x in a.frames.split(",") if x.strip()]:
        if 1 <= f <= len(rows):
            print(f"    f{f}: gaze {rows[f-1][0]:5.1f}  trunk {rows[f-1][1]:5.1f}")
