"""
Foot slide of an in-place locomotion loop, from motion.ps1's pose dump (build/motions/work/<clip>.pose.json).

The body is carried at a constant ground velocity V; a foot on the ground then travels
at (its in-place velocity + V), which should be zero. V is the best fit (mean of the
planted feet's backward velocity). Reported per clip:
  speed    |V| m/s (on the Mixamo rig the clip was made on)
  slide    mean |planted foot velocity + V| over contact frames, m/s, and as % of |V|
  contact  share of frames with a foot down (lowest point within 3 cm of the clip's lowest)

    python tools/motions/footslide.py build/motions/work/rhb_walk_out_bat_tucked_loop.pose.json [...]
"""
import json
import sys

import numpy as np


def points(frame, side):
    """Heel/ankle and ball/toe points of one foot (bone heads and tails)."""
    b = frame["bones"]
    out = []
    for name in (f"mixamorig:{side}Foot", f"mixamorig:{side}ToeBase"):
        if name in b:
            out += [np.array(b[name][0]), np.array(b[name][1])]
    return np.array(out)


def analyse(path, tol=0.02):
    d = json.load(open(path))
    fps = d.get("fps", 30)
    frames = d["frames"]
    P = {s: np.array([points(f, s) for f in frames]) for s in ("Left", "Right")}  # (n, k, 3)
    ground = min(P[s][:, :, 2].min() for s in P)
    vel = []
    n = len(frames)
    for i in range(n - 1):
        # The planted foot this frame: the one whose lowest point is lowest; its lowest point must be down.
        s = min(P, key=lambda k: P[k][i, :, 2].min())
        j = int(np.argmin(P[s][i, :, 2]))
        if P[s][i, j, 2] > ground + tol or P[s][i + 1, j, 2] > ground + tol:
            continue
        vel.append((P[s][i + 1, j] - P[s][i, j])[:2] * fps)
    vel = np.array(vel)
    V = -np.median(vel, 0)
    resid = np.linalg.norm(vel + V, axis=1)
    return np.linalg.norm(V), float(np.median(resid)), float(resid.mean()), len(vel) / (n - 1), V


if __name__ == "__main__":
    for p in sys.argv[1:]:
        speed, med, mean, contact, V = analyse(p)
        name = p.replace("\\", "/").split("/")[-1].replace(".pose.json", "")
        print(f"{name:34s} speed {speed:4.2f} m/s  dir ({V[0]:+.2f},{V[1]:+.2f})  slide median {med:4.2f} / mean {mean:4.2f} m/s = {100 * med / speed:4.1f}% / {100 * mean / speed:4.1f}%  contact {contact:4.2f}")
