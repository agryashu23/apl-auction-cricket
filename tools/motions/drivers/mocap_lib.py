"""
Rokoko mocap (tools/motions/mocap_extract.py JSON) -> our batting channel poses (drivers/batting_v2.py convention):
aligned to the batting frame (feet line down the pitch = +X, stance feet midpoint at the origin), scaled to our rig,
grounded with planted feet locked, lightly filtered, then expressed as hips / pelvis / spine / look / feet / knee
channels.  Runs inside Blender (mathutils).  The bat is NOT taken from here (see batting_mocap.py).
"""
import json
import math

import numpy as np
from mathutils import Matrix, Quaternion, Vector as V

RIG_HIP = 1.043            # our rig's rest hip height
SRC_HIP = 0.94             # Rokoko rest hip height (the dropped T-pose frame)
PITCH0 = math.degrees(math.atan2(0.087, 0.107))
J = ["Hips", "Spine2", "Neck", "Head", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftUpLeg", "RightUpLeg",
     "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase"]


def smooth(a, passes=1):
    """[1 2 1]/4 along time, ends held"""
    a = np.array(a, float)
    for _ in range(passes):
        b = a.copy()
        b[1:-1] = 0.25 * a[:-2] + 0.5 * a[1:-1] + 0.25 * a[2:]
        a = b
    return a


def load(path, stance_frames=12, yaw_from="feet", k0=0):
    d = json.load(open(path))
    F = d["frames"]
    n = len(F)
    P = {j: np.array([f["p"][j] for f in F]) for j in J}
    Q = {k: [Quaternion(f["q"][k]) for f in F] for k in ("Hips", "Spine2", "Head")}
    k = min(k0 + stance_frames, n)
    W = slice(k0, k)                                          # the stance frames used for the alignment
    feet = (P["LeftFoot"][W] - P["RightFoot"][W]).mean(0)
    psi = math.atan2(feet[1], feet[0])                        # feet line -> +X
    if yaw_from == "shoulders":                               # a clip whose opening feet are not in the stance
        sh = (P["LeftArm"][W] - P["RightArm"][W]).mean(0)
        psi = math.atan2(sh[1], sh[0]) - math.radians(10)
    c, s = math.cos(-psi), math.sin(-psi)
    Rz = np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
    mid = ((P["LeftToeBase"][W] + P["RightToeBase"][W]) / 2).mean(0)
    sc = RIG_HIP / SRC_HIP
    for j in J:
        P[j] = ((P[j] - [mid[0], mid[1], 0]) @ Rz.T) * sc
    g = np.median(np.minimum(P["LeftToeBase"][:, 2], P["RightToeBase"][:, 2])[W])
    for j in J:
        P[j][:, 2] -= g
    Mz = Matrix(Rz.tolist())
    qz = Mz.to_quaternion()
    for key in Q:
        Q[key] = [qz @ q for q in Q[key]]
    chest = np.cross(P["LeftArm"][W].mean(0) - P["RightArm"][W].mean(0), [0, 0, 1])
    return dict(P=P, Q=Q, n=n, yaw_deg=math.degrees(psi), scale=sc, ground=g, chest_y=float(chest[1]))


def ground_feet(M, z_plant=0.06, v_plant=0.55, min_len=3):
    """lock planted feet: a foot whose ball is near the ground and slow is held at the plant's mean position"""
    P, n = M["P"], M["n"]
    for side in ("Left", "Right"):
        ball, ank = P[side + "ToeBase"], P[side + "Foot"]
        v = np.r_[0, np.linalg.norm(np.diff(ball[:, :2], axis=0), axis=1)] * 30
        planted = (ball[:, 2] < z_plant) & (v < v_plant)
        i = 0
        while i < n:
            if not planted[i]:
                i += 1
                continue
            j = i
            while j + 1 < n and planted[j + 1]:
                j += 1
            if j - i + 1 < min_len:                           # a flicker, not a plant
                i = j + 1
                continue
            m = ball[i:j + 1, :2].mean(0)
            off = ank[i:j + 1] - ball[i:j + 1]
            ball[i:j + 1, :2] = m
            ball[i:j + 1, 2] = 0.0
            ank[i:j + 1] = ball[i:j + 1] + off
            i = j + 1
        lock = np.zeros(n, bool)                              # the frames actually locked above
        i = 0
        while i < n:
            if planted[i]:
                j = i
                while j + 1 < n and planted[j + 1]:
                    j += 1
                if j - i + 1 >= min_len:
                    lock[i:j + 1] = True
                i = j + 1
            else:
                i += 1
        i = 0                                                 # every swing between plants is a real step: lifted
        while i < n:
            if lock[i]:
                i += 1
                continue
            j = i
            while j + 1 < n and not lock[j + 1]:
                j += 1
            a0, a1 = max(i - 1, 0), min(j + 1, n - 1)
            travel = np.linalg.norm(ball[a1, :2] - ball[a0, :2])
            if travel > 0.04 and j > i:
                L = a1 - a0
                for t in range(i, j + 1):
                    lift = min(0.08, 0.5 * travel) * math.sin(math.pi * (t - a0) / L)
                    if ball[t, 2] < lift:
                        dz = lift - ball[t, 2]
                        ball[t, 2] += dz
                        ank[t, 2] += dz
            i = j + 1
        M[side + "_locked"] = lock
        ball[:, 2] = np.maximum(ball[:, 2], 0.0)
        ank[:, 2] = np.maximum(ank[:, 2], 0.06)
        M[side + "_planted"] = planted
    return M


def yxz(M3):
    """Blender 'YXZ' euler of a 3x3 = R('Z',z) @ R('X',x) @ R('Y',y) (our pyaw/ptilt/pside order), degrees"""
    e = M3.to_euler("YXZ")
    return math.degrees(e.x), math.degrees(e.y), math.degrees(e.z)


def channels(M, f, lean_max=30.0):
    """our channels for mocap frame f (hips/pelvis/spine/look/feet/knees), positions in metres, angles in degrees"""
    P = {j: V(M["P"][j][f]) for j in J}
    qh, qc, qhd = M["Q"]["Hips"][f], M["Q"]["Spine2"][f], M["Q"]["Head"][f]
    ptilt, pside, pyaw = yxz(qh.to_matrix())
    rel = (qc @ qh.inverted())
    ax, ang = rel.to_axis_angle()
    A = Quaternion(ax, ang / 3.0).to_matrix()
    lean3, side3, twist3 = yxz(A)
    lean, side, twist = 3 * lean3, 3 * side3, 3 * twist3
    lean = max(-15.0, min(lean_max, lean))                      # mocap depth error: no deep forward fold
    side = max(-35.0, min(35.0, side))
    fwd = qhd @ V((0, -1, 0))
    out = dict(hips=V((P["Hips"].x, P["Hips"].y, P["Hips"].z - RIG_HIP)), ptilt=ptilt, pside=pside, pyaw=pyaw,
               lean=lean, side=side, twist=twist, look=P["Head"] + fwd * 3.0, lookw=1.0)
    for side_, k in (("Right", "r"), ("Left", "l")):
        ball, ank = P[side_ + "ToeBase"], P[side_ + "Foot"]
        h = V((ball.x - ank.x, ball.y - ank.y, 0.0))
        pitch = math.degrees(math.atan2(ank.z - ball.z, max(h.length, 1e-4)))
        knee, hip = P[side_ + "Leg"], P[side_ + "UpLeg"]
        pole = knee - (hip + ank) * 0.5
        out[k + "ball"] = V(ball)
        out[k + "toe"] = h.normalized() if h.length > 1e-4 else V((0, -1, 0))
        out[k + "heel"] = max(-4.0, min(80.0, pitch - PITCH0))
        out[k + "kp"] = pole.normalized() if pole.length > 1e-4 else V((0, -1, 0.2))
    out["rwrist"], out["lwrist"] = V(P["RightHand"]), V(P["LeftHand"])
    out["rsh"], out["lsh"] = V(P["RightArm"]), V(P["LeftArm"])
    return out


FLAT = ["ptilt", "pside", "pyaw", "lean", "side", "twist", "rheel", "lheel"]
VEC = ["hips", "look", "rball", "lball", "rtoe", "ltoe", "rkp", "lkp", "rwrist", "lwrist", "rsh", "lsh"]


def arm_len(M, side="Right"):
    P = M["P"]
    a = np.linalg.norm(P[side + "Arm"] - P[side + "ForeArm"], axis=1) + np.linalg.norm(P[side + "ForeArm"] - P[side + "Hand"], axis=1)
    return float(np.median(a))


def filtered(chs, passes=1, hand_passes=1):
    """light jitter filter over the frame list of channel dicts (angles unwrapped first)"""
    out = [dict(c) for c in chs]
    for k in FLAT:
        a = np.unwrap(np.radians([c[k] for c in chs]))
        a = np.degrees(smooth(a, passes))
        for c, v in zip(out, a):
            c[k] = float(v)
    for k in VEC:
        a = smooth([tuple(c[k]) for c in chs], hand_passes if "wrist" in k else passes)
        for c, v in zip(out, a):
            c[k] = V(tuple(v))
    return out
