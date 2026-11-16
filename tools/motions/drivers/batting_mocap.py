"""
Round 6: the real-mocap batting clips (Rokoko Vision on the owner's batting videos), cleaned and turned into our clips.

  body   from the mocap (drivers/mocap_lib.py): aligned to the batting frame, scaled to our rig, feet grounded and
         locked while planted, lightly filtered, forward lean clamped; hips / pelvis / spine / look / feet / knees.
  bat    grip = the mocap bottom (right) wrist every frame; the bat's axis and face come from our approved shot
         (drivers/batting_v2.py, the same shot id) time-warped onto the mocap phases (stance end, backlift top,
         contact, finish).  Rokoko's hands are too noisy to give a usable bat axis on their own (wrist-line jitter
         p90 6-15 deg/frame; the pull's contact axis came out ~60 deg off), so they give the path, not the attitude.
  timing contact at frame tc+1 (31, switch hit 41) as the runtime expects; shots open blended from the mocap stance
         loop and close back into it.

    motion.ps1 gen drivers\\batting_mocap.py [stance | all | <names...>] [rhb|lhb|both] [--check]

Output: build\\motions\\batting_mocap(_lh)\\ FBX + sidecar JSON + <clip>.battrack.json (right-handed rows; the
game mirrors lhb_ clips) + work\\<clip>.v2dump.json for sheets/audit.  The stance pose/loop is also written to
batting_mocap\\stance.json (batting_v2.py and ceremony_batter.py use it when APL_STANCE_JSON points at it).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib"))
import mocap_lib as ml  # noqa: E402
import batting  # noqa: E402
from batting import GRIP_R, SWEET, V, bat_g, ortho  # noqa: E402
from mixamo_motions import P, sample  # noqa: E402
from running_bat import mirror_y  # noqa: E402
from shots import blend, mid_bat  # noqa: E402
import shots74  # noqa: E402
import batting_v2 as v2  # noqa: E402
import bat_clear  # noqa: E402

import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
RAW = _APL_REPO + "/build/motions/work/mocap/raw"
_DIR = os.environ.get("APL_MOCAP_DIR", "batting_mocap")          # round 7: batting_r7
OUTR = os.path.join(batting.APL_OUT, _DIR)
OUTL = os.path.join(batting.APL_OUT, _DIR + "_lh")
STANCE_CLIP = "42_pull"
# name: id, donor (the v2 shot whose bat attitude is used), mocap contact frame (0-based, T-pose dropped, from the
# hand speed and the video), last usable mocap frame, tc, borrowed finish
CLIPS = {
    # round 9: the owner's re-export of the leg glance: contact at the video's f22 (the ball past the front pad by
    # f24); he then whips the bat up over the front shoulder, so the finish attitude goes there rather than the keyed
    # glance's held-low face.  (01_front_foot_defence's re-export was tried and NOT used: a left-hander's take, bent
    # over chest-on with the hands 0.6 m apart at contact -- no side-on defence in it; 01 stays keyed.  The
    # loader options it needed -- raw, align_from, back_at, open_at (see mocap() and shot_keys()) -- are kept.)
    # round 9b: the owner's v2 takes from cleaner source clips (build/mocap/shots/best/*_v2.mp4).  01 v2 was tried
    # (c=47, end=64, stride_k=0.6 -- see squash()) and NOT used: the take lunges ~1.4 m with the pelvis 0.34 m down;
    # even pulled 40 % toward the stance it reads as a deep squat, not clearly better than the keyed defence.  40: side-on from the front-off side,
    # no drift; the upper cut meets the ball at f52 and finishes with the hands above the head -- also used for 41
    # (third man) with that shot's own bat attitude.
    "40_upper_cut":         dict(id=40, donor=40, c=51, end=80, raw="40_upper_cut_v2"),
    "41_upper_cut_tm":      dict(id=41, donor=41, c=51, end=80, raw="40_upper_cut_v2"),
    # round 13: the owner's FINAL batch (build/mocap/ROKOKO_FINAL, exported <take>_mixamo.fbx -> raw/<take>.json; all
    # right-handed, the game mirrors left-handers).  Contact frames are best guesses from the videos (mocap frame =
    # video frame - 1); r13/import_final.ps1 regenerates, audits and draws the compare sheets for review first.
    "01_ffd_v3":            dict(id=1, donor=1, c=40, end=72, raw="01_front_foot_defence_v3"),          # press f32-38, block f41, hold
    "33_leave_v3":          dict(id=33, donor=33, c=46, end=80, raw="06_bat_leave"),                    # bat up over the head f41-50
    "45_hook_v3":           dict(id=45, donor=45, c=44, end=78, raw="07_bat_hook"),                     # swing f41-50, held finish
    "46_hook_fine_v3":      dict(id=46, donor=46, c=44, end=78, raw="07_bat_hook"),                     # same take, the fine hook's face
    "57_switch_point_v3":   dict(id=57, donor=57, c=64, end=86, tc=40, raw="09_bat_switch_hit"),        # switches f20-29, contact ~f65
    "58_switch_tm_v3":      dict(id=58, donor=58, c=64, end=86, tc=40, raw="09_bat_switch_hit"),
    "50_scoop_v3":          dict(id=50, donor=50, c=58, end=80, raw="10_bat_scoop"),                    # doubtful take: review the sheet
    # 08_bat_charge_down_track is NOT mapped: our 82 is a 36-frame footwork move that ends down the pitch, this
    # pipeline's clips end back in the stance, and the take only starts its charge at ~f77 of 90.
    "19_leg_glance":        dict(id=19, donor=19, c=21, end=61, fin_axis=(0.22, 0.30, 0.93), fin_face=(1.0, 0.0, -0.25), ret="front"),
    "02_straight_drive":    dict(id=2, donor=2, c=54, end=85, pull_in=0.30, checked=True, ret="front"),   # focus 1: checked ground finish
    "05_cover_drive":       dict(id=5, donor=5, c=41, end=83, pull_in=0.30, checked=True),
    "05b_cover_drive_spin": dict(id=85, donor=5, c=40, end=83, name="Cover drive (spin)", pull_in=0.30, checked=True),
    "09_lofted_drive":      dict(id=9, donor=9, c=31, end=58, pull_in=0.10, ret="front", fin_axis=(0.24, 0.30, 0.92), fin_face=(1.0, 0.0, -0.25)),   # focus 1: full high finish
    "16_flick":             dict(id=16, donor=16, c=31, end=72, checked=True),
    "22_sweep":             dict(id=22, donor=22, c=47, end=83, kneel=True, ret="over"),
    "24_slog_sweep":        dict(id=24, donor=24, c=73, end=83, align_from=20, kneel=True, ret="over"),   # walks in first
    "27_reverse_sweep":     dict(id=27, donor=27, c=38, end=62, kneel=True),
    "32_back_foot_defence": dict(id=32, donor=32, c=50, end=83, lift_by=13),
    "34_back_foot_punch":   dict(id=34, donor=34, c=57, end=83, pull_in=0.30, checked=True),
    "37_square_cut":        dict(id=37, donor=37, c=51, end=83),
    "39_late_cut":          dict(id=39, donor=39, c=42, end=83),
    "42_pull":              dict(id=42, donor=42, c=47, end=74),
    "45_hook":              dict(id=45, donor=45, c=69, end=71, borrow=("42_pull", 47)),
    "50_scoop":             dict(id=50, donor=50, c=42, end=80, kneel=True),
    "56_switch_hit":        dict(id=56, donor=56, c=60, end=83, tc=40),
    "59_helicopter":        dict(id=59, donor=59, c=67, end=101),
}
B_IN, HOLD, RET = 10, 8, 24
LIFT = 15                                                   # frames from the low stance bat to the backlift top
_CACHE = {}
_RIG = []


def rig():
    if _RIG:
        try:
            _RIG[0].arm.name
        except ReferenceError:                       # the scene was reset (make_bat reloads the rig)
            _RIG.clear()
    if not _RIG:
        arm, _ = batting.load_rig()
        _RIG.append(batting.BatRig(arm))
    return _RIG[0]


def kneel_from(ch, d):
    """Rokoko gets the kneel wrong (back knee 0.4 m up, the foot raised behind, the pelvis too high): while the
    donor shot is down on its back knee, take the pelvis height and the back foot's tuck from it (xy stay mocap)"""
    dz = d["hips"].z + v2.HIP0
    if dz > 0.80:
        return ch
    w = min(1.0, (0.80 - dz) / 0.12)
    ch = dict(ch)
    h = V(ch["hips"])
    ch["hips"] = V((h.x, h.y, min(h.z, h.z * (1 - w) + d["hips"].z * w)))
    k = "r" if ch["rball"].x < ch["lball"].x else "l"
    b = V(ch[k + "ball"])
    ch[k + "ball"] = V((b.x, b.y, b.z * (1 - w)))
    ch[k + "heel"] = ch[k + "heel"] * (1 - w) + d[k + "heel"] * w
    ch[k + "kp"] = (V(ch[k + "kp"]) * (1 - w) + V(d[k + "kp"]).normalized() * w).normalized()
    ch["lean"] = ch["lean"] * (1 - w) + max(ch["lean"], d["lean"] * 0.7) * w
    return ch


def kneel(ch):
    """Rokoko floats the back foot when he goes down on the knee: low pelvis -> the back foot's toes on the ground,
    heel up (toes tucked), knee pointing down"""
    hz = ch["hips"].z + ml.RIG_HIP
    k = "r" if ch["rball"].x < ch["lball"].x else "l"
    if hz > 0.86 or ch[k + "ball"].z < 0.08:
        return ch
    w = min(1.0, (0.86 - hz) / 0.06)
    ch = dict(ch)
    b = V(ch[k + "ball"])
    ch[k + "ball"] = V((b.x, b.y, b.z * (1 - w)))
    ch[k + "heel"] = ch[k + "heel"] * (1 - w) + 75.0 * w
    ch[k + "kp"] = (V(ch[k + "kp"]) * (1 - w) + V((0.3, -0.2, -1.0)).normalized() * w).normalized()
    return ch


def fit_hips(pose):
    """lower the pelvis until both ankles are within the legs (our legs vs the scaled mocap stride)"""
    for _ in range(4):
        _, _, err = rig().solve(pose, {}, write=False)
        e = max(err.get("RightFoot", 0.0), err.get("LeftFoot", 0.0))
        if e < 0.002:
            break
        pose["hips"] = V(pose["hips"]) - V((0, 0, e + 0.004))
    return pose


def squash(pose, cfg):
    """round 9b: a take that lunges deeper than a game defence should (01 v2: a ~1.4 m stride, the pelvis 0.34 m
    down, the back nearly flat) is brought toward the stance by cfg["stride_k"] (1: as captured): the pelvis, both
    feet and the forward bend; the wrist follows (it is placed from our solved shoulder)"""
    k = cfg.get("stride_k")
    if k is None:
        return pose
    st = stance()[0][1]
    pose["hips"] = V(st["hips"]) + (V(pose["hips"]) - V(st["hips"])) * k
    for f in ("rball", "lball"):
        a, b = V(st[f]), V(pose[f])
        pose[f] = V((a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, b.z))
    for ch in ("lean", "ptilt"):
        pose[ch] = st[ch] + (pose[ch] - st[ch]) * k
    return pose


def our_wrist(pose, ch, k_arm, shrink=1.0):
    """the mocap bottom-wrist placed relative to OUR solved shoulder (the mocap shoulder->wrist vector, scaled to
    our arm), so a spine/proportion difference never leaves the hand out of reach"""
    Pos, _, _ = rig().solve(pose, {}, write=False)
    sh = batting.C.transposed() @ Pos[batting.PFX + "RightArm"] / 100
    return sh + (ch["rwrist"] - ch["rsh"]) * k_arm * shrink


def mocap(name):
    if name not in _CACHE:
        cfg = CLIPS.get(name, {})
        M = ml.load(os.path.join(RAW, cfg.get("raw", name) + ".json"), k0=cfg.get("align_from", 0))
        if "back_at" in cfg:
            # round 9: a take with no stance at the start (01: he walks in and plays) is aligned on the planted
            # defence instead (align_from), then slid so the back foot sits where the stance's back foot is
            r = M["P"]["RightToeBase"][cfg["c"]]
            d = [cfg["back_at"][0] - r[0], cfg["back_at"][1] - r[1], 0.0]
            for j in M["P"]:
                M["P"][j] = M["P"][j] + d
        M = ml.ground_feet(M)
        chs = ml.filtered([ml.channels(M, f) for f in range(M["n"])])
        _CACHE[name] = (M, chs)
    return _CACHE[name]


def phases(chs, c, end):
    """stance end (hands start to move), backlift top (highest hands before contact), finish (highest after)"""
    w = [(ch["rwrist"] + ch["lwrist"]) * 0.5 for ch in chs]
    sp = [0.0] + [(w[i] - w[i - 1]).length * 30 for i in range(1, len(w))]
    s = next((i for i in range(1, c) if all(x > 0.7 for x in sp[i:i + 3])), max(1, c - 20))
    s = max(1, min(s - 2, c - 12))
    bl = max(range(s, c - 2), key=lambda i: w[i].z)
    bl = min(max(bl, c - 14), c - 4)
    hi = min(end, c + 20)
    hi = min(hi, len(w) - 1)
    fin = max(range(c + 3, hi + 1), key=lambda i: w[i].z) if hi >= c + 6 else c + 9
    return s, bl, fin


def warp(t, s, bl, c, fin, tc):
    """mocap frame -> donor (v2) frame"""
    pts = [(0, 0), (s, 7), (bl, tc - 8), (c, tc), (fin, tc + 9), (fin + 19, tc + 28)]
    if t <= pts[0][0]:
        return 0.0
    for (m0, v0), (m1, v1) in zip(pts, pts[1:]):
        if t <= m1:
            return v0 + (v1 - v0) * (t - m0) / max(m1 - m0, 1e-6)
    return tc + 28.0


def grip_bat(wrist, a, n):
    """the bat with axis a / face n whose bottom-hand wrist sits at `wrist` (BatRig's grip offsets inverted)"""
    a = V(a).normalized()
    n = ortho(n, a)
    f = (a.cross(n).normalized() + a * 0.35).normalized()
    return bat_g(V(wrist) + f * 0.075 + n * 0.03, a, n)


def body_pose(base, ch):
    keep = {k: ch[k] for k in ("hips", "ptilt", "pside", "pyaw", "lean", "side", "twist", "look", "lookw",
                               "rball", "lball", "rtoe", "ltoe", "rheel", "lheel", "rkp", "lkp")}
    return P(base, **keep, gait=1.0, topgrip=1.0)


def smoothstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


# ---------------------------------------------------------------- the stance loop (from STANCE_CLIP's opening frames)
def low_bat(hip, tap=0.0):
    """the resting stance bat (round 7): grip at the top of the back thigh, just in front of it on the chest side,
    toe back by the back foot about 10 cm up; `tap` (0..1) dips the hands 3 cm and swings the toe to the ground"""
    g = V((hip.x - 0.10, -0.25, 0.80 - 0.03 * tap))
    toe = V((-0.30 - 0.02 * tap, -0.22, 0.10 - 0.07 * tap))
    a = (toe - g).normalized()
    return bat_g(g, a, (1.0, -0.35, 0.15))


def stance_keys(N=120):
    M, chs = mocap(STANCE_CLIP)
    s, _, _ = phases(chs, CLIPS[STANCE_CLIP]["c"], CLIPS[STANCE_CLIP]["end"])
    K = max(6, s - 12)                                    # the still part, before the trigger move
    keys = []
    for i in range(N):
        u = 1 - abs(1 - 2 * i / (N - 1))                     # 0 -> 1 -> 0: closes exactly
        t = u * K
        i0 = int(t)
        i1 = min(i0 + 1, K)
        ch = blend(chs[i0], chs[i1], t - i0)
        pose = body_pose(v2.STANCE, ch)
        # round 7 (the owner's reference video): a relaxed crouch -- pelvis 6 cm lower, more hip hinge, eyes level on
        # the bowler -- and the bat LOW: hands at the top of the back thigh, close in, the bat angled back with its
        # toe resting by the back foot; a light tap (toe to the ground) twice per loop
        pose["hips"] = V(pose["hips"]) + V((0, 0, -0.10))
        pose["ptilt"] = pose["ptilt"] + 14.0
        pose["lean"] = pose["lean"] + 16.0
        pose["look"], pose["lookw"] = V((20.0, 0.0, 1.45)), 1.0
        pose = fit_hips(pose)
        tap = 0.5 - 0.5 * math.cos(2 * math.pi * 2 * i / (N - 1))       # 0 at both ends: the loop still closes
        # focus batch 1 (BAT-2): his weight rocks a little between the feet while he waits (2 cm, once a loop)
        pose["hips"] = V(pose["hips"]) + V((0.02 * math.sin(2 * math.pi * i / (N - 1)), 0, -0.006 * math.sin(math.pi * i / (N - 1))))
        pose.update(low_bat(V(pose["hips"]) + V((0, 0, ml.RIG_HIP)), tap))
        keys.append((i, pose))
    keys[-1] = (N - 1, dict(keys[0][1]))
    return keys


STANCE_KEYS = None


def stance():
    global STANCE_KEYS
    if STANCE_KEYS is None:
        STANCE_KEYS = stance_keys()
    return STANCE_KEYS


# ---------------------------------------------------------------- one shot
def return_bat(src, st, u, hip, how=None):
    """round 8: the bat from the finish back to the resting stance.  A straight slerp from a finish round the leg side
    (sweeps, slog sweep) or high over the shoulder (lofted drive) swung the blade through the torso; this goes over the
    top (blade up in front of the face) when the finish is behind him, then down in front of the body, then back to
    the toe by the back foot"""
    if how not in ("over", "front"):
        return mid_bat(src, st, u)
    behind = how == "over"
    pts = [src]
    if behind:
        pts.append(bat_g(V((hip.x + 0.12, -0.44, hip.z + 0.36)), (0.15, -0.05, 0.99), (0.2, -1.0, 0.0)))
    pts.append(bat_g(V((hip.x + 0.12, -0.38, hip.z + 0.05)), V((0.30, -0.25, -0.92)).normalized(), (1.0, -0.3, 0.2)))
    pts.append(st)
    k = len(pts) - 1
    s = u * k
    i = min(int(s), k - 1)
    return mid_bat(pts[i], pts[i + 1], s - i)


def borrowed(name, t):
    """channels for mocap frame t beyond the clip's end: the donor clip's follow-through deltas added on (hook)"""
    cfg = CLIPS[name]
    M, chs = mocap(name)
    end = cfg["end"]
    src, sc = cfg["borrow"]
    _, schs = mocap(src)
    k0 = sc + (end - cfg["c"])
    k = min(len(schs) - 1, k0 + int(t - end))
    base = dict(chs[end])
    for key in ml.FLAT:
        base[key] = chs[end][key] + (schs[k][key] - schs[k0][key])
    for key in ("hips", "look", "rball", "lball", "rwrist", "lwrist"):
        base[key] = chs[end][key] + (schs[k][key] - schs[k0][key])
    for key in ("rtoe", "ltoe", "rkp", "lkp"):
        base[key] = schs[k][key]
    return base


def clear_body(M, f, bat, r=0.17):
    """push the bat out of the torso (mocap depth errors put the hands/bat inside the body)"""
    hip, neck = V(M["P"]["Hips"][f]), V(M["P"]["Head"][f])
    a = V(bat["ba"]).normalized()
    top = V(bat["bg"]) - a * GRIP_R
    push = V((0, 0, 0))
    for i in range(4, 11):
        q = top + a * (0.86 * i / 10.0)
        ab = neck - hip
        t = max(0.0, min(1.0, (q - hip).dot(ab) / ab.dot(ab)))
        d = q - (hip + ab * t)
        d.z = 0.0
        if d.length < r:
            dirn = d.normalized() if d.length > 1e-4 else V((0, -1, 0))
            push = push if push.length > r - d.length else dirn * (r - d.length)
    return push


def shot_keys(name):
    """the clip's keys (one per frame), its contact frame (0-based) and info for the sidecar"""
    cfg = CLIPS[name]
    M, chs = mocap(name)
    c, end, tc = cfg["c"], cfg["end"], cfg.get("tc", 30)
    end = min(end, len(chs) - 1)                              # round 13: never past the take's last frame
    s, bl, fin = phases(chs, c, end if "borrow" not in cfg else c + 20)
    last = end if "borrow" not in cfg else c + 30
    post = min(last - c, 40)
    n = tc + post + HOLD + RET
    dk = v2.SHOTS_V2[cfg["donor"]]["keys"]
    dtc = v2.SHOTS_V2[cfg["donor"]]["contact"]
    st = stance()[0][1]
    k_arm = (rig().L1 + rig().L2) / 100 / ml.arm_len(M)
    def bat_at(t, shrink=1.0):                               # the bat on our wrist with the donor's attitude at mocap t
        ch = chs[t] if t <= end else borrowed(name, t)
        d = sample(dk, warp(t, s, bl, c, fin, dtc))
        if cfg.get("kneel"):
            ch = kneel_from(ch, d)
        pose = fit_hips(squash(body_pose(v2.STANCE, ch), cfg))
        return grip_bat(our_wrist(pose, ch, k_arm, shrink), d["ba"], d["bn"])
    top = bat_at(bl)                                         # round 7: the lift aims at the backlift top itself
    keys, pushes = [], []
    for o in range(n):
        t = min(max(0, o - tc + c), c + post)
        ch = chs[t] if t <= end else borrowed(name, t)
        wt = warp(t, s, bl, c, fin, dtc)
        if "borrow" in cfg:                                  # round 7: the borrowed arms stay up, so the bat holds
            wt = min(wt, dtc + 22)                           # the donor's finish (its unwind starts at tc+22)
        d = sample(dk, wt)
        if "fin_axis" in cfg and t > c + 2:
            # round 8 (the owner's video): the straight drive finishes with the bat up OVER THE FRONT SHOULDER, toe up
            # and back, not held out pointing at the bowler
            fa = V(cfg["fin_axis"]).normalized()
            fb = mid_bat(dict(bg=V((0, 0, 0)), ba=V(d["ba"]), bn=ortho(d["bn"], V(d["ba"]))),
                         dict(bg=V((0, 0, 0)), ba=fa, bn=ortho(cfg["fin_face"], fa)), smoothstep((t - c - 2) / 10.0))
            d = dict(d, ba=fb["ba"], bn=fb["bn"])
        if cfg.get("kneel"):
            ch = kneel_from(ch, d)
        pose = fit_hips(squash(body_pose(v2.STANCE, ch), cfg))
        # round 7: after contact the hands come back in toward the body (Rokoko leaves the arms locked out in the
        # follow-through; the reference finishes are compact, the hands by the head)
        shrink = 1.0 - cfg.get("pull_in", 0.15) * smoothstep((t - c) / 8.0)
        bat = grip_bat(our_wrist(pose, ch, k_arm, shrink), d["ba"], d["bn"])
        if cfg.get("checked") and t > c + 2:
            # focus batch 1 (BAT-7): a ground stroke checks its finish -- the hands held about chest height (the
            # donor's checked, along-the-line bat attitude), never the lofted shot's full swing over the shoulder
            cap = V(pose["hips"]).z + ml.RIG_HIP + 0.36
            g = V(bat["bg"])
            if g.z > cap:
                k_ = smoothstep((t - c - 2) / 6.0)
                bat["bg"] = V((g.x, g.y, g.z + (cap - g.z) * k_))
        if t < bl:                                           # round 7: the bat rests low and lifts over ~0.5 s
            w = smoothstep((t - (bl - LIFT)) / LIFT)
            if "lift_by" in cfg:                             # round 10: up by this clip frame, then held (shape-up)
                e_ = cfg["lift_by"] - tc + c
                w = smoothstep((t - (e_ - 10)) / 10.0)
            low = low_bat(V(pose["hips"]) + V((0, 0, ml.RIG_HIP)))
            bat = v2.lift_path(low, top, w)
        push = clear_body(M, min(t, end), bat) if abs(o - tc) > 2 else V((0, 0, 0))
        pushes.append(push)
        pose.update(bat)
        keys.append([o, pose])
    for i, (o, pose) in enumerate(keys):                     # smoothed clearance pushes (no pops)
        w = [pushes[j] * math.exp(-((j - i) / 2.0) ** 2) for j in range(max(0, i - 6), min(n, i + 7))]
        ws = sum(math.exp(-((j - i) / 2.0) ** 2) for j in range(max(0, i - 6), min(n, i + 7)))
        pose["bg"] = V(pose["bg"]) + sum(w, V((0, 0, 0))) / ws
    for o in range(n):                                       # open from the stance, close back into it
        pose = keys[o][1]
        o0 = cfg.get("open_at", 0)                           # round 9: hold the stance until here (01 walks in first)
        if o < o0 + B_IN:
            u = smoothstep((o - o0) / B_IN)
            q = P(blend(st, pose, u), **mid_bat(st, pose, u))
            tgt = keys[o0 + B_IN][1]
            for k in ("r", "l"):                             # the feet step from the stance onto the mocap's
                if (V(tgt[k + "ball"]) - V(st[k + "ball"])).length > 0.03:
                    q[k + "ball"] = V(q[k + "ball"]) + V((0, 0, 0.05 * math.sin(math.pi * u)))
            keys[o][1] = q
        r0 = n - RET
        if o >= r0:
            u = smoothstep((o - r0) / (RET - 1))
            src = keys[r0 - 1][1] if o > r0 else pose
            q = blend(src, st, u)
            q = P(q, **return_bat(src, st, u, V(q["hips"]) + V((0, 0, ml.RIG_HIP)), cfg.get("ret")))
            for k in ("r", "l"):                             # a foot that moves steps (lifted arc) instead of sliding
                if (V(src[k + "ball"]) - V(st[k + "ball"])).length > 0.03:
                    q[k + "ball"] = V(q[k + "ball"]) + V((0, 0, 0.06 * math.sin(math.pi * u)))
            keys[o][1] = q
    keys = [(o, p) for o, p in keys]
    keys[-1] = (n - 1, dict(st))
    sh = []                                                  # whatever is still out of reach: the bat comes in
    for o, p in keys:
        d = V((0, 0, 0))
        for _ in range(4):
            r = rig().reach_vec(dict(p, bg=V(p["bg"]) - d), {})
            dv = V(r[0:3]) + V(r[3:6])
            if dv.length < 0.003:
                break
            d = d + dv
        sh.append(d)
    for i, (o, p) in enumerate(keys[:-1]):
        js = range(max(0, i - 4), min(n, i + 5))
        wsum = sum(math.exp(-((j - i) / 2.0) ** 2) for j in js)
        env = sum((sh[j] * math.exp(-((j - i) / 2.0) ** 2) for j in js), V((0, 0, 0))) / wsum
        if env.length < sh[i].length:
            env = sh[i]
        p["bg"] = V(p["bg"]) - env
    S0, S1 = sweet(keys[tc - 1][1]), sweet(keys[tc + 1][1])
    vel = S1 - S0
    ang = math.degrees(math.atan2(-vel.y, vel.x)) % 360
    info = dict(n=n, tc=tc, phases=(s, bl, c, fin), mocap_frames=(c - tc, c + post), measured_angle=round(ang),
                bat_push_max=round(max(p.length for p in pushes), 3), yaw=round(M["yaw_deg"], 1), chest_y=round(M["chest_y"], 2))
    return keys, tc, info


def sweet(p):
    a = V(p["ba"]).normalized()
    return V(p["bg"]) + a * (SWEET - GRIP_R)


# ---------------------------------------------------------------- export
def stem(name, hand):
    sid = 0 if name == "stance" else CLIPS[name]["id"]
    nm = "stance_idle_loop" if sid == 0 else shots74.slug(CLIPS[name].get("name") or shots74.SHOTS74[sid]["name"])
    return ("lhb_" if hand == "lhb" else "") + f"{sid:02d}_{nm}"


def ser(p):
    return {k: (list(v) if hasattr(v, "__len__") else v) for k, v in p.items()}


def track(n, keys):
    rows = []
    for f in range(n):
        p = sample(keys, f)
        a = V(p["ba"]).normalized()
        nn = ortho(p["bn"], a)
        rows.append([round(c, 4) for c in (*V(p["bg"]), *a, *nn, float(p.get("topgrip", 1.0)))])
    return rows


def sidecar(name, hand, n, keys, tc, info):
    lhb = hand == "lhb"
    if name == "stance":
        e = dict(id=0, shot="Stance idle (loop)", file=stem(name, hand) + ".fbx", frames=n, loop=True,
                 source=f"rokoko mocap: {STANCE_CLIP} opening stance (ping-pong loop)")
        if lhb:
            e["batter"] = "left-handed"
        return e
    cfg = CLIPS[name]
    sid = cfg["id"]
    base = shots74.SHOTS74[cfg["donor"]]
    ang = cfg.get("angle", base["angle"])
    S = sweet(dict(keys)[tc])
    e = dict(id=sid, shot=cfg.get("name") or shots74.SHOTS74[sid]["name"], file=stem(name, hand) + ".fbx")
    if lhb:
        e["batter"] = "left-handed"
        e["field_angle_deg"] = (360 - ang) % 360
    e.update(ball=base["info"].get("ball", ""), angle_deg=ang, family=base["info"]["family"], frames=n,
             seconds=round(n / 30, 2), contact_frame=tc + 1, lofted=base["lofted"],
             sweet_spot_at_contact_m=dict(toward_bowler=round(S.x, 3), toward_off_side=round((1 if lhb else -1) * S.y, 3), up=round(S.z, 3)),
             eyes=base["info"]["eyes"], hands=base["info"]["hands"], upper_body=base["info"]["upper_body"], feet=base["info"]["feet"],
             source=f"rokoko mocap: {name} (mocap frames {info['mocap_frames'][0]}..{info['mocap_frames'][1]}, contact {cfg['c']})")
    return e


def run(name, hand, check):
    if name == "stance":
        keys, tc, info, n, meta = stance(), None, {}, 120, dict(loop=True)
    else:
        keys, tc, info = shot_keys(name)
        n, meta = info["n"], dict(loop=False)
        # round 9: the bat slid out of the body where it went through (drivers/bat_clear.py; cached on the rhb run)
        cn = stem(name, "rhb")
        if hand == "rhb" and bat_clear.wanted():
            bat_clear.compute(rig(), keys, n, tc, cn)
        keys = bat_clear.apply(cn, keys, n)
    st = stem(name, hand)
    batting.OUT = OUTL if hand == "lhb" else OUTR
    os.makedirs(batting.OUT, exist_ok=True)
    k2 = [(t, mirror_y(p)) for t, p in keys] if hand == "lhb" else keys
    print(f"{st}: frames {n} contact {None if tc is None else tc + 1} info {info}", flush=True)
    rs = []
    for f in range(n):
        r = rig().reach_vec(sample(k2, f), {})
        rs.append((round(max(V(r[0:3]).length, V(r[3:6]).length), 3), f + 1, "R" if V(r[0:3]).length > V(r[3:6]).length else "L"))
    over = [x for x in rs if x[0] > 0.02]
    print(f"  reach shortfall max {max(rs)} ; frames over 2 cm: {len(over)} " + " ".join(f"f{f}{h}:{v}" for v, f, h in over[:14]), flush=True)
    if check:
        return
    frames, ws, md = batting.make_bat(st, n, k2, tc, meta)
    bad = {k: round(v, 3) for k, v in ws.items() if not k.endswith("@") and v > 0.012}
    print(f"  solver: {'grip/reach OK' if not bad else bad}", flush=True)
    json.dump([sidecar(name, hand, n, keys, tc, info)], open(os.path.join(batting.OUT, st + ".json"), "w"), indent=1)
    if hand == "rhb":
        json.dump(dict(clip=st, rows=track(n, keys)), open(os.path.join(OUTR, st + ".battrack.json"), "w"), separators=(",", ":"))
        v2.dump(st, [(dict((k, (fr[0][k], fr[1][k])) for k in fr[0]), fr[2]) for fr in frames], tc)
        if name == "stance":
            json.dump(dict(ready=ser(keys[0][1]), loop=[(t, ser(p)) for t, p in keys]), open(os.path.join(OUTR, "stance.json"), "w"))


if __name__ == "__main__":
    args = sys.argv[1:]
    check = "--check" in args
    hands = [a for a in args if a in ("rhb", "lhb", "both")] or ["both"]
    hands = ["rhb", "lhb"] if "both" in hands else hands
    sel = [a for a in args if a not in ("rhb", "lhb", "both", "--check")] or ["all"]
    names = []
    for a in sel:
        names += (["stance"] + list(CLIPS)) if a == "all" else [a]
    for hand in hands:
        for nm in names:
            run(nm, hand, check)
