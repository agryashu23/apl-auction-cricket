"""
Batting rework, pilot round: the stance loop and six shots (one per family) rebuilt on the pack
author's library with the body reviewer's spec applied — reachable contact poses so make_bat's
reach assist never folds the spine, the body lowered through the knees, the head's look target
clamped so the face never drops to the ground, the stride landing 0.3 s before contact with soft
knees, the back heel up through contact, finishes over the front foot with the hands clear of the
hips, the pull's chest open at contact, the sweep down on the back knee, the back-foot shots back
and across.

Exposes (importable without side effects; the game exports its bat tracks from these):
  SHOTS_V2   {id: {"name", "angle", "n", "keys", "contact", "lofted", "info", "sweet"}}  (shots74 shape)
  STANCE_V2  (n, keys, contact, meta)                                                   (shots.SHOTS shape)

Roll-out (2026-10-23): every other shot of shots74.SHOTS74 on the same rules, family by family (back-foot
vertical bfx_v2, front-foot drive_v2 / whip_v2 / ffpull_v2, cut_v2, leave_v2, pullx_v2, sweepx_v2, ramp_v2,
switch_v2, heli_v2, adv_v2); the pilot's six shots and the stance are unchanged.  MOVED lists every sweet spot
moved off the pack's; WAVES names the families for the command line.  drivers/batting_v2_fit.json holds the reach
fits (`--fit`: the contact key's hips, the other keys' bat grip, extra keys where an in-between frame fell short).

    motion.ps1 gen drivers\\batting_v2.py [all | stance | pilot | <family> | <ids...>] [rhb|lhb|both] [--check | --fit | --unfit]

--check solves every frame of each clip without exporting and prints where either hand would be
beyond the arm's reach (what make_bat's assist would have to fix) plus the body metrics at the
key moments.  Output: build\\motions\\batting_v2\\ and batting_v2_lh\\ (FBX + sidecar JSON).

Batting frame (right-hander): origin between the feet on the popping crease, Z up, bowler at +X,
chest/off side -Y, leg side +Y.  Left-handers = running_bat.mirror_y of the same keys.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib"))
import batting  # noqa: E402
from batting import BASE, GRIP_R, SWEET, P, V, bat_g, ortho  # noqa: E402
from mixamo_motions import PFX, sample  # noqa: E402
from running_bat import HIP0, mirror_y  # noqa: E402
from shots import BOWLER, blend, far, mid_bat  # noqa: E402
import shots74  # noqa: E402
from shots74 import dirv, slug  # noqa: E402
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bat_clear  # noqa: E402

# ---------------------------------------------------------------- helpers
def Hroot(x, y, z):
    """hips channel for the pelvis root at world height z (the rig stands with its root at HIP0)"""
    return V((x, y, z - HIP0))


def bat_of(pose):
    return {k: V(pose[k]) if k != "batw" else pose[k] for k in ("bg", "ba", "bn", "batw")}


def sweet_of(bat):
    a = V(bat["ba"]).normalized()
    return V(bat["bg"]) + a * (SWEET - GRIP_R)


def bat_shift(bat, d):
    return dict(bat, bg=V(bat["bg"]) + V(d))


def tilt_about_face(bat, deg):
    """rotate the bat about its own face normal (face and sweet spot unchanged): + = toe up"""
    a = V(bat["ba"]).normalized()
    n = ortho(bat["bn"], a)
    S = sweet_of(bat)
    down = n.cross(a).normalized()                        # the third axis; for a horizontal bat ~down
    if down.z > 0:
        down = -down
    r = math.radians(deg)
    a2 = (a * math.cos(r) - down * math.sin(r)).normalized()
    return dict(bg=S - a2 * (SWEET - GRIP_R), ba=a2, bn=n, batw=1.0)


def unitv(*v):
    return V(v).normalized()


def bat_through(S, a0, n):
    """the bat with its sweet spot at S and its face n, the axis as near a0 as is square to the face"""
    n = V(n).normalized()
    a0 = V(a0).normalized()
    a = (a0 - n * a0.dot(n)).normalized()
    return bat_g(V(S) - a * (SWEET - GRIP_R), a, n)


def finish_bat(hands, az, elev, face_tilt=0.0):
    """a checked high finish: bottom-hand grip at `hands`, blade pointing along field azimuth `az` raised
    `elev` degrees above horizontal, face (not rolled) turned up and back toward the batter"""
    d = dirv(az)
    e = math.radians(elev)
    a = d * math.cos(e) + V((0, 0, math.sin(e)))
    n = -d * math.sin(e) + V((0, 0, math.cos(e)))
    if face_tilt:
        n = (n + d.cross(V((0, 0, 1))) * math.tan(math.radians(face_tilt))).normalized()
    return bat_g(V(hands), a, n)


def eyes_of(p):
    """rough world position of the eyes for a pose (root + the tipped trunk)"""
    root = V(p["hips"]) + V((0, 0, HIP0))
    lean = p["lean"] * 0.7 + p.get("ptilt", 0.0)
    side = p["side"] * 0.7
    u = V((math.sin(math.radians(side)), -math.sin(math.radians(lean)), 1.0)).normalized()
    return root + u * 0.63 + V((0, 0.02, 0))


def look_dn(target, eye, max_down=32.0, toward=None, k=0.0, dist=2.5):
    """a look target in the direction of `target` from the eyes, its depression clamped to max_down
    degrees (the eyes do the rest), optionally with the azimuth blended toward another point"""
    d = V(target) - V(eye)
    h = V((d.x, d.y, 0.0))
    hl = h.length
    hn = h.normalized() if hl > 1e-6 else V((1, 0, 0))
    if toward is not None and k > 0:
        d2 = V(toward) - V(eye)
        h2 = V((d2.x, d2.y, 0.0))
        if h2.length > 1e-6:
            hn = (hn * (1 - k) + h2.normalized() * k).normalized()
    dep = math.degrees(math.atan2(-d.z, max(hl, 1e-6)))
    dep = max(-25.0, min(max_down, dep))
    r = math.radians(dep)
    return V(eye) + (hn * math.cos(r) - V((0, 0, math.sin(r)))) * dist


def body(**kw):
    """the trunk/pelvis channels of a key, in one place"""
    return dict(ptilt=kw.get("ptilt", 0.0), lean=kw.get("lean", 0.0), side=kw.get("side", 0.0),
                twist=kw.get("twist", 0.0), pyaw=kw.get("pyaw", 0.0), pside=kw.get("pside", 0.0))


# ---------------------------------------------------------------- the stance (item 10)
# Knees ~25 deg, pelvis hinged ~15 deg with a little upper-back rounding, feet 0.44 m apart on the balls
# of the feet, hands at the top of the back thigh, bat toe hovering a hand's width behind the back foot,
# head locked on the bowler.  Every shot starts and ends here.
ST_ROOT = Hroot(-0.03, 0.05, 1.00)
ST_BAT_G = V((-0.10, -0.17, 0.81))
ST_BAT_A = V((-0.20, -0.06, -0.977)).normalized()
ST_BAT = bat_g(ST_BAT_G, ST_BAT_A, (1, 0, 0))
STANCE = P(BASE, hips=ST_ROOT, **body(ptilt=17, lean=16, side=-2, twist=3, pyaw=0),
           rball=(-0.22, -0.03, 0), rtoe=(0, -1, 0), rheel=4.0, rkp=(0, -1, 0.25),
           lball=(0.22, -0.03, 0), ltoe=(0.35, -1, 0), lheel=4.0, lkp=(0.3, -1, 0.25),
           look=BOWLER, lookw=1.0,
           rpole=(-0.35, -0.5, -1), lpole=(0.75, -0.35, -0.55),
           **ST_BAT)


def st(wx=0.0, dz=0.0, dlean=0.0, tap=0.0):
    """the stance with a weight shift (wx, m toward the bowler), a dip (dz) and a bat tap (0..1)"""
    bg = ST_BAT_G + V((0, -0.02, -0.05)) * tap
    a = (ST_BAT_A * (1 - tap) + V((-0.10, -0.03, -0.995)) * tap).normalized()
    return P(STANCE, hips=ST_ROOT + V((wx, 0, dz - 0.045 * tap)), lean=STANCE["lean"] + dlean + 6.0 * tap,
             **bat_g(bg, a, (1, 0, 0)))


STANCE_N = 120
STANCE_KEYS = [
    (0, STANCE),
    (18, st(wx=0.02)),
    (28, st(wx=0.015, tap=1.0)),
    (34, st(wx=0.01)),
    (42, st(wx=0.0, tap=1.0)),
    (48, st(wx=0.0)),
    (70, st(wx=-0.02, dlean=1.0)),
    (86, st(wx=-0.015, tap=1.0)),
    (92, st(wx=-0.01)),
    (STANCE_N - 1, STANCE),
]
STANCE_V2_GROUNDED = (STANCE_N, STANCE_KEYS, None, dict(footwork="stance", loop=True))


# ---------------------------------------------------------------- the READY stance (round 3, replaces the grounded one)
# The modern T20 ready stance: side-on with the front shoulder ~10-15 deg open to the bowler, feet a little wider than
# the shoulders on the balls of the feet, knees ~25-30 deg, the hinge in the hips with the back flat, the head level
# and still with both eyes on the bowler, the BAT UP: hands at waist height just behind the back hip, the blade
# hanging back toward the keeper with the toe at knee-to-thigh height, the face open toward point/slip.
# The loop (4 s) is a wrists-only waggle, a 1 cm weight shift and a breath.  Every shot starts and ends here too.
RD_ROOT = Hroot(-0.04, 0.04, 1.010)
RD_HANDS = V((-0.08, -0.28, 1.12))          # round 4: just in front of the back hip and out from the body (seen from the bowler)


def rd_bat(elev=30.0, dg=(0, 0, 0)):
    """the raised bat (round 3d): grip at RD_HANDS(+dg), the blade angled UP behind him toward the keeper/slips `elev` deg
    above horizontal (toe higher than the hands), the face slightly open (turned out toward point/slip, not to the sky)"""
    e = math.radians(elev)
    a = V((-math.cos(e) * 0.96, -math.cos(e) * 0.28, math.sin(e))).normalized()
    return bat_g(RD_HANDS + V(dg), a, (0.10, -0.80, -0.55))


def ready(wx=0.0, elev=30.0, breath=0.0, dg=(0, 0, 0)):
    return P(STANCE, hips=RD_ROOT + V((wx, 0, 0.004 * breath)),
             **body(ptilt=20, lean=6 + 1.0 * breath, side=-2, twist=8, pyaw=6),
             rball=V((-0.24, -0.03, 0)), rtoe=V((0.05, -1, 0)), rheel=10.0, rkp=V((0.05, -1, 0.25)),
             lball=V((0.24, -0.03, 0)), ltoe=V((0.40, -1, 0)), lheel=10.0, lkp=V((0.35, -1, 0.25)),
             look=BOWLER, lookw=1.0, rpole=(-0.3, 0.2, -1), lpole=(0.5, -0.4, -1), **rd_bat(elev, dg))


READY = ready()
READY_KEYS = [                                   # wrists-only waggle (toe +-4 deg), 1 cm weight shift, a breath
    (0, READY),
    (14, ready(elev=34.0, dg=(0.0, 0, 0.008))),
    (28, ready(elev=27.0, wx=0.004)),
    (42, ready(elev=33.0, wx=0.008, breath=0.6, dg=(0.0, 0, 0.008))),
    (60, ready(elev=28.0, wx=0.010, breath=1.0)),
    (76, ready(elev=34.0, wx=0.006, breath=0.6, dg=(0.0, 0, 0.008))),
    (92, ready(elev=27.0, wx=0.002, breath=0.2)),
    (106, ready(elev=32.0, dg=(0.0, 0, 0.006))),
    (STANCE_N - 1, READY),
]
STANCE_V2 = (STANCE_N, READY_KEYS, None, dict(footwork="stance", loop=True))

# Round 6: the stance taken from the mocap (drivers/batting_mocap.py writes it).  With APL_STANCE_JSON set, every
# shot here starts and ends in it, and the ceremony clips (ceremony_batter.py imports READY) end in it.
READY_R3D = READY
if os.environ.get("APL_STANCE_JSON"):
    def _vec(d):
        return {k: (V(v) if isinstance(v, list) and len(v) == 3 else v) for k, v in d.items() if k != "gait"}
    _st = json.load(open(os.environ["APL_STANCE_JSON"]))
    READY = P(READY, **_vec(_st["ready"]), gait=0.0)
    STANCE_V2 = (len(_st["loop"]), [(t, P(READY, **_vec(p), gait=0.0)) for t, p in _st["loop"]], None, dict(footwork="stance", loop=True))
    # round 7: the settle's carried bat (relax_key) and the travelling clips' end carry follow the new stance's
    # low hands, not the round-3 raised grip (else the hands bob up to the chest with the toe dangling)
    RD_ROOT = V(READY["hips"])
    RD_HANDS = V(READY["bg"])

SHOTS_V2 = {}


# Reach fits for the roll-out shots (drivers/batting_v2_fit.json, written by `--fit`): per key, the hips and/or the
# bat grip nudged (metres) so both hands reach the handle without make_bat's assist folding the spine.  The pilot
# shots have no entries, so they are exactly as approved.
FIT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "batting_v2_fit.json")
try:
    with open(FIT_PATH) as _fh:
        FIT = {int(k): {int(t): d for t, d in v.items()} for k, v in json.load(_fh).items()}
except (OSError, ValueError):
    FIT = {}


def apply_fit(sid, keys):
    """the key offsets first, then the inserted keys ("ins": sampled from the offset keys, then offset)"""
    fx = FIT.get(sid)
    if not fx:
        return keys

    def nudge(p, d):
        p = dict(p)
        if "hips" in d:
            p["hips"] = V(p["hips"]) + V(d["hips"])
        if "bg" in d:
            p["bg"] = V(p["bg"]) + V(d["bg"])
        return p
    have = {t for t, _ in keys}
    out = [(t, nudge(p, fx[t]) if t in fx else p) for t, p in keys]
    ins = [(t, nudge(sample(out, t), d)) for t, d in fx.items() if d.get("ins") and t not in have]
    return sorted(out + ins, key=lambda k: k[0])


def lift_path(b0, b1, s_):
    """the bat from the resting stance (b0) to a backlift (b1), s_ 0..1 (round 7).  A plain slerp takes the shortest
    turn, which swings the blade out sideways; a real pick-up goes UP AND BACK: the toe swings back toward the keeper
    with the hands rising close to the body.  A backlift on the leg side (reverse shots) goes over the top instead."""
    a0, a1 = V(b0["ba"]).normalized(), V(b1["ba"]).normalized()
    if math.degrees(a0.angle(a1)) < 60:
        return mid_bat(b0, b1, s_)
    g = V(b0["bg"]).lerp(V(b1["bg"]), 0.5)
    if a1.y > 0.3:                                             # reverse backlift: up and over, out of the face
        via = bat_g(g + V((-0.06, -0.34, 0.12)), (-0.20, -0.30, 0.93), (1.0, -0.3, 0.0))
    else:
        ax = (a0 + a1 + V((-0.9, 0.0, 0.0))).normalized()     # toe back toward the keeper, about level
        n = (V(b0["bn"]) + V(b1["bn"])).normalized() if (V(b0["bn"]) + V(b1["bn"])).length > 0.2 else V((0.3, -1, 0))
        via = bat_g(g + V((0.02, 0.0, 0.06)), ax, n)
    return mid_bat(b0, via, s_ * 2) if s_ < 0.5 else mid_bat(via, b1, s_ * 2 - 1)


# round 10 (the owner's "bat handling"): the backlift top a little higher -- hands toward the chest, as in his
# reference video, not at the waist with the wrists flicking the bat up -- and the shape-up clip (the forward defence
# the game holds at 0.43 s while the ball comes) with its bat already at the top by then: the reference batter picks
# the bat up in the trigger and waits there; ours was still grounded at the tap and then whipped up and down.
BACKLIFT_RAISE = V((-0.03, 0.0, 0.08))
LIFT_BY = {1: 13}
# focus batch 1 (BAT-2): a visible trigger -- the weight back and across onto the back foot, a small dip and the
# front heel up -- as he picks the bat up in the shape-up clip, then forward into the stride
TRIGGER = V((-0.07, 0.0, -0.025))


def from_ready(keys, tc, sid=None):
    """round 3: every shot starts (and ends) in the READY stance instead of the grounded one.  The pure-stance keys
    become READY; between frame 6 and the backlift key (tc-8) the bat rises straight from READY's raised bat to the
    shot's backlift and the stance's body/feet differences fade out, so frame 1 and ShapeUp continue the idle loop."""
    kd = dict(keys)
    if kd.get(6) is not STANCE:
        return keys
    te = tc - 8 if (tc - 8) in kd else max((t for t in kd if 12 < t < tc - 2), default=None)   # the leave has no tc-8 key
    if te is None:
        return keys
    # the shape-up clip waits at a FULL backlift (the straight drive's, which every shot can swing down from)
    land_bat = bat_of(dict(shots74.SHOTS74[2]["keys"])[tc - 8] if sid in LIFT_BY else kd[te])
    land_bat["bg"] = V(land_bat["bg"]) + BACKLIFT_RAISE
    tl = LIFT_BY.get(sid, te)
    d_hips = V(READY["hips"]) - V(STANCE["hips"])
    d_feet = {k_: V(READY[k_]) - V(STANCE[k_]) for k_ in ("rball", "lball")}
    out = []
    # a backlift on the other side of him (reverse shots) is reached over the top: via the bat straight up above the
    # hands, never by slerping the blade through the body
    def lift_bat(s_):
        return lift_path(READY, land_bat, s_)
    for t, p in keys:
        if p is STANCE:
            p = READY
        elif t == te:
            p = dict(p, **land_bat)
        elif 6 < t < te:
            s_ = min(1.0, (t - 6) / float(tl - 6))
            s_ = s_ * s_ * (3 - 2 * s_)
            w = 1.0 - s_
            p = dict(p, **lift_bat(s_))
            for ch in ("ptilt", "lean", "side", "twist", "pyaw", "rheel", "lheel"):
                p[ch] = p[ch] + (READY[ch] - STANCE[ch]) * w
            p["hips"] = V(p["hips"]) + d_hips * w
            if sid in LIFT_BY:
                k_ = min(1.0, (t - 6) / float(tl - 6)) * (1.0 - max(0.0, (t - tl) / float(max(1, te - tl))))
                p["hips"] = V(p["hips"]) + TRIGGER * (k_ * k_ * (3 - 2 * k_))
                p["lheel"] = p["lheel"] + 12.0 * k_
            for k_ in ("rball", "lball"):
                p[k_] = V(p[k_]) + d_feet[k_] * w
        out.append((t, p))
    return out


def register(sid, keys, tc=30, n=90):
    old = shots74.SHOTS74[sid]
    raw = tidy(from_ready(sorted(keys, key=lambda k: k[0]), tc, sid), tc, over_top=sid in OVER_TOP)
    keys = apply_fit(sid, raw)
    con = dict(keys)[tc]
    S = sweet_of(con)
    SHOTS_V2[sid] = dict(name=old["name"], angle=old["angle"], n=n, keys=bat_clear.apply(clear_name(sid), keys, n),
                         keys_unclear=keys, contact=tc, lofted=old["lofted"], info=old["info"],
                         sweet=[round(x, 3) for x in S], raw=raw)


def clear_name(sid):
    """the clip's name in the bat-clearance cache (drivers/bat_clear.py; right-handed rows)"""
    return f"{sid:02d}_{slug(shots74.SHOTS74[sid]['name'])}"


def old_bats(sid, tc=30):
    """the pack author's bat geometry for a shot: backlift, contact, the two follow-through keys"""
    kd = dict(shots74.SHOTS74[sid]["keys"])
    return bat_of(kd[tc - 8]), bat_of(kd[tc]), bat_of(kd[tc + 6]), bat_of(kd[tc + 14])


def relax_key(hips, bat_x, feet, look=BOWLER):
    """the settle after the finish (round 3d): the bat already back up in the ready carry (READY's raised bat moved
    with the pelvis) rather than hanging down past the front pad, so the return to the stance never drags the blade
    through the legs or flips it; bat_x is kept for the callers' signature (the old hanging-bat x)"""
    h = V(hips)
    feet = dict(feet)
    rb = feet.get("rball")
    if rb is not None and RD_HANDS.z < 1.0:
        # round 8: a back foot that went back and across steps home HERE (while the bat is up in the finish/carry),
        # not in the last 0.4 s -- that late step brought the back knee through the lowered blade (35/36/40/41/47-49/61)
        dr = V((READY["rball"][0], READY["rball"][1], 0)) - V((rb[0], rb[1], 0))
        if dr.length > 0.10:
            feet.update(rball=V(READY["rball"]), rtoe=V(READY["rtoe"]), rkp=V(READY["rkp"]))
            h = h + V((dr.x * 0.5, dr.y * 0.3, 0))
            hips = h
    d = h - V(RD_ROOT)
    hands = V((RD_HANDS.x + d.x, RD_HANDS.y + 0.5 * d.y, RD_HANDS.z + 0.8 * d.z))
    return P(STANCE, hips=hips, **body(ptilt=15, lean=10, side=0, twist=7, pyaw=5), **feet,
             **bat_g(hands, READY["ba"], READY["bn"]), rpole=READY["rpole"], lpole=READY["lpole"], look=look)


# round 8: the shots whose leg-side finish came back through the chest (audit r7); the others' finishes are clean
OVER_TOP = {23, 67}


def behind_leg(p):
    """round 8: the bat's toe round on the leg side of him (behind the chest: +y for the right-hander) with the hands
    not out in front -- a slerp from here to a bat in front of him goes through the torso"""
    a = V(p["ba"]).normalized()
    toe = V(p["bg"]) + a * 0.6
    return toe.y > 0.02 and V(p["bg"]).y > -0.30


def tidy(keys, tc, over_top=False):
    """round 3d clean-up of a shot's key list (after from_ready, before the reach fits):
    - a foot that steps more than 5 cm between two grounded keys gets a lifted key half-way (ball 6 cm up, heel
      raised), so it steps instead of skating (step_lift only lifts fast moves);
    - where the bat turns more than 100 deg between two keys (finish -> settle), a slerped key half-way so the
      blade swings round instead of flipping through the body."""
    # the finish is held ~0.45 s (not 0.63 s) and the settle takes ~0.8 s (not 0.47 s): no snap back to the stance
    kd = dict(keys)
    if (tc + 28) in kd and (tc + 42) in kd and not any(tc + 28 < t < tc + 42 for t in kd) and             not any(tc + 22 <= t < tc + 28 for t in kd) and not any(tc + 42 < t <= tc + 46 for t in kd):
        keys = sorted([(tc + 22 if t == tc + 28 else tc + 46 if t == tc + 42 else t, p) for t, p in keys], key=lambda k: k[0])
    out = list(keys)
    add = []
    for (t0, p0), (t1, p1) in zip(keys, keys[1:]):
        if t1 - t0 < 4:
            continue
        tm = (t0 + t1) // 2
        lift = {}
        for k_ in ("r", "l"):
            b0, b1 = V(p0[k_ + "ball"]), V(p1[k_ + "ball"])
            if b0.z < 0.01 and b1.z < 0.01 and min(p0[k_ + "heel"], p1[k_ + "heel"]) < 30 and                     (V((b1.x, b1.y, 0)) - V((b0.x, b0.y, 0))).length > 0.03:
                lift[k_] = True
        a0, a1 = V(p0["ba"]).normalized(), V(p1["ba"]).normalized()
        turn = math.degrees(a0.angle(a1)) > 100 or (t0 == tc + 22 and t1 == tc + 46 and math.degrees(a0.angle(a1)) > 40)
        if t0 == tc + 22 and t1 == tc + 46 and RD_HANDS.z < 1.0 and V(p0["bg"]).y > 0.0:
            turn = True                             # round 7: hands behind on the leg side -> come round in front
        if over_top and t0 == tc + 22 and t1 == tc + 46 and behind_leg(p0):
            # round 8: a finish wrapped round the leg side / behind the back (fine sweep, slog-cow) comes back OVER THE
            # TOP -- the blade up in front of the face -- then down in front of him, then into the stance carry: the
            # plain slerp (and the half-way key) took the blade back through the chest
            u = sample(keys, t0 + 8)
            hp = V(u["hips"]) + V((0, 0, HIP0))
            u.update(bat_g(V((hp.x + 0.12, -0.44, hp.z + 0.36)), (0.15, -0.05, 0.99), (0.2, -1.0, 0.0)))
            f_ = sample(keys, t0 + 16)
            hp = V(f_["hips"]) + V((0, 0, HIP0))
            f_.update(bat_g(V((hp.x + 0.12, -0.38, hp.z + 0.05)), (0.30, -0.25, -0.92), (1.0, -0.3, 0.2)))
            e_ = sample(keys, t0 + 4)                 # first the hands up and out from the chest, the blade upright
            he = V(e_["hips"]) + V((0, 0, HIP0))
            e_.update(bat_g(V((V(p0["bg"]).x + 0.05, he.y - 0.42, V(p0["bg"]).z + 0.10)), (0.0, 0.15, 0.99), (0.2, -1.0, 0.0)))
            add += [(t0 + 4, e_), (t0 + 8, u), (t0 + 16, f_)]
            if lift:
                q = sample(keys, tm)
                for k_ in lift:
                    q[k_ + "ball"] = V(q[k_ + "ball"]) + V((0, 0, 0.06))
                    q[k_ + "heel"] = max(q[k_ + "heel"], 20.0)
                q.update(mid_bat(u, f_, (tm - t0 - 8) / 8.0) if tm < t0 + 16 else mid_bat(f_, p1, (tm - t0 - 16) / float(t1 - t0 - 16)))
                add.append((tm, q))
            continue
        if not lift and not turn:
            continue
        q = sample(keys, tm)
        for k_ in lift:
            q[k_ + "ball"] = V(q[k_ + "ball"]) + V((0, 0, 0.06))
            q[k_ + "heel"] = max(q[k_ + "heel"], 20.0)
        if turn:                                    # the half-way bat held out in front of the chest, clear of the body
            q.update(mid_bat(p0, p1, 0.5))
            q["bg"] = V(q["bg"]) + V((0.10, -0.30, 0.0))
            am = V(q["ba"]).normalized()
            if t0 == tc + 22 and t1 == tc + 46 and am.y > -0.15:
                # round 7: the low carry made this slerp point the toe back across the chest (blade through the
                # torso); keep the toe on the off side of the hands, the bat in front of the body
                q.update(bat_g(V(q["bg"]), V((am.x, -0.15, am.z)).normalized(), q["bn"]))
            if t0 == tc + 22 and a0.z < 0.3:
                # a wrapped finish (blade down behind the front shoulder) first lets the bat down to hang by the front
                # leg, out in front of it, then lifts it across into the ready carry
                tu = t0 + 8
                u = sample(keys, tu)
                hp = V(u["hips"]) + V((0, 0, HIP0))
                u.update(bat_g(V((hp.x + 0.32, hp.y - 0.14, hp.z - 0.02)), (0.25, -0.10, -0.96) if RD_HANDS.z < 1.0 else (0.25, 0.30, -0.92), (1.0, -0.3, 0.2)))   # round 7: toe in front for the low carry
                add.append((tu, u))
                tm = (tu + t1) // 2
                q = sample(keys, tm)
                q.update(mid_bat(u, p1, 0.5))
                q["bg"] = V(q["bg"]) + V((0.06, -0.20, 0.0))
                am = V(q["ba"]).normalized()
                if am.y > -0.15:                    # round 7: the toe stays out in front (see above)
                    q.update(bat_g(V(q["bg"]), V((am.x, -0.15, am.z)).normalized(), q["bn"]))
                for k_ in lift:
                    q[k_ + "ball"] = V(q[k_ + "ball"]) + V((0, 0, 0.06))
                    q[k_ + "heel"] = max(q[k_ + "heel"], 20.0)
        add.append((tm, q))
    have = {t for t, _ in out}
    out += [(t, q) for t, q in add if t not in have]
    return sorted(out, key=lambda k: k[0])


# ---------------------------------------------------------------- FRONT FOOT (items 1, 2, 7, 11, 12)
def ff_v2(sid, *, stride, ltoe, hips_land, hips_con, body_land, body_con, defence=False, con_bat=None,
          hips_f1=None, hips_fin=None, body_f1=None, body_fin=None, f1_bat=None, fin_bat=None, f1_df=4, fin_df=9,
          bl_shift=(0.08, 0.0, 0.0), rheel_con=40.0, rheel_fin=50.0, look_down=35.0, look_k=0.0,
          lpole_con=(1, -0.2, 0.4), rpole_con=(-0.3, 0.3, -1), lpole_fin=(0.8, -0.3, 0.25), rpole_fin=(-0.3, -0.6, -0.6),
          relax_hips=None, tc=30, n=90, swing=None, back_feet=None, walk_feet=None, bl_bat=None, look_con=None,
          lheel_fin=0.0, front_extra=None, look_th=None):
    """swing / back_feet / walk_feet / bl_bat / look_con / lheel_fin / front_extra are the roll-out's options (the
    pilot shots leave them unset): swing = [(df, phi, k_bg, d)] downswing keys at tc-df in the bat's own plane
    (replacing the slerped mid key), back_feet = the back foot's trigger position (default back and a little
    across), walk_feet = a first move of the back foot at frame 12 (walk-across / inside-out), bl_bat = the backlift
    bat (default the pack's), look_con = the contact gaze target (default the sweet spot)."""
    bl_b, con_b, f1_b, f2_b = old_bats(sid, tc)
    if bl_bat is not None:
        bl_b = bl_bat
    bl_b = bat_shift(bl_b, bl_shift)                  # the author's backlift, carried forward with the body
    if con_bat is not None:
        con_b = con_bat
    old = shots74.SHOTS74[sid]
    S = sweet_of(con_b)
    ltoe = V(ltoe)
    front = dict(lball=V(stride), ltoe=ltoe, lheel=0.0, lkp=ltoe.normalized() + V((0, 0, 0.3)))
    trigger_back = dict(rball=V((-0.30, -0.04, 0)), rtoe=V((0.05, -1, 0)))     # a small back-and-across trigger
    if back_feet is not None:
        trigger_back = dict(back_feet)
    if front_extra:
        front.update(front_extra)
    # trigger: weight settles back a touch, bat lifts to hip height, eyes on the bowler
    trig = P(STANCE, **trigger_back, hips=ST_ROOT + V((-0.03, 0.0, 0.0)), **mid_bat(STANCE, bl_b, 0.4), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    # the front foot lands 0.3 s before contact: top of the backlift, knees soft, chin on the bowler
    land = P(STANCE, **trigger_back, **front, hips=hips_land, **body_land, **bl_b, look=BOWLER, rheel=6.0, rkp=(0.2, -1, 0.3),
             lpole=(1, 0, 0.5), rpole=(-0.6, 0.3, -0.6))
    con = P(STANCE, **trigger_back, **front, hips=hips_con, **body_con, **con_b, rheel=rheel_con, rkp=(0.35, -1, 0.3),
            lpole=lpole_con, rpole=rpole_con)
    # eyes over the ball; blending the gaze toward the bowler turns the forward trunk lean into a nod, not a head roll
    con["look"] = look_dn(S if look_con is None else V(look_con), eyes_of(con), look_down, toward=BOWLER, k=look_k)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.55))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (tc - 8, land)]
    if swing is None:
        keys.append((tc - 4, mid))
    else:
        for df, phi, kbg, d in swing:
            keys.append((tc - df, P(blend(land, con, 1 - df / 8.0), **downswing(con_b, bl_b, phi, kbg, d))))
    if walk_feet is not None:
        keys[2] = (12, P(trig, **walk_feet))
    keys.append((tc, con))
    if defence:
        f1 = P(con, bg=V(con_b["bg"]) + V((-0.03, 0, -0.01)))
        f1["look"] = look_dn(S + V((-0.15, 0, 0)), eyes_of(f1), look_down + 5, toward=BOWLER, k=look_k)
        fin = P(con, bg=V(con_b["bg"]) + V((-0.04, 0, -0.02)), lean=con["lean"] + 2)
        fin["look"] = look_dn(S + V((-0.3, 0, -0.05)), eyes_of(fin), look_down + 8, toward=BOWLER, k=look_k)
        hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
        keys += [(tc + 6, f1), (tc + 14, fin), (tc + 28, hold)]
    else:
        # the swing goes on up the line of the shot and checks high: by +0.3 s (tc+9) the hands are at chin
        # height in front of the front shoulder, the blade 45-60 deg above horizontal, wrists unrolled
        lf = far(S, dirv(old["angle"] if look_th is None else look_th), old["lofted"])
        f1 = P(con, hips=hips_f1, **body_f1, **f1_bat, rheel=(rheel_con + rheel_fin) / 2,
               lpole=(0.8, 0.0, 0.8), rpole=(-0.3, -0.4, -0.8))
        f1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(f1), 22.0)
        fin = P(con, hips=hips_fin, **body_fin, **fin_bat, rheel=rheel_fin, lpole=lpole_fin, rpole=rpole_fin)
        if lheel_fin:
            fin["lheel"] = lheel_fin
        fin["look"] = look_dn(lf, eyes_of(fin), 15.0)
        hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)), bg=V(fin_bat["bg"]) + V((0.0, 0.0, 0.015)))
        keys += [(tc + f1_df, f1), (tc + fin_df, fin), (tc + 28, hold)]
    rel = relax_key(relax_hips or Hroot(0.16, 0.04, 0.94), 0.22, dict(trigger_back, **dict(front, rheel=0.0, lheel=0.0)))
    keys += [(tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


# 01 forward defence: short backlift, dead bat; the stride lands soft, the body sinks over the front knee,
# head over the ball with the chin ~35 deg down, back heel up, back toe anchored.
ff_v2(1, stride=(0.58, 0.02, 0), ltoe=(0.5, -1, 0),      # round 4: the front pad 10 cm further to leg, clear of the dead bat
      hips_land=Hroot(0.18, 0.03, 0.93), body_land=body(ptilt=14, lean=14, side=6, twist=-4, pyaw=-2),
      hips_con=Hroot(0.45, 0.0, 0.77), body_con=body(ptilt=10, lean=14, side=44, twist=26, pyaw=8),
      bl_shift=(0.10, 0.0, 0.02), defence=True, look_down=35, look_k=0.55,
      lpole_con=(1, -0.3, 0.3), rpole_con=(-0.3, 0.2, -1))

# 02 straight drive: same base, high backlift; the swing carries on up the line to a checked high finish
ff_v2(2, stride=(0.58, -0.08, 0), ltoe=(0.55, -1, 0),
      hips_land=Hroot(0.18, 0.03, 0.93), body_land=body(ptilt=14, lean=14, side=6, twist=-4, pyaw=-2),
      hips_con=Hroot(0.38, 0.0, 0.77), body_con=body(ptilt=10, lean=12, side=44, twist=32, pyaw=12),
      hips_f1=Hroot(0.43, 0.01, 0.79), body_f1=body(ptilt=8, lean=8, side=38, twist=40, pyaw=20),
      hips_fin=Hroot(0.44, 0.02, 0.81), body_fin=body(ptilt=6, lean=6, side=26, twist=44, pyaw=24),
      f1_bat=bat_g((0.86, -0.20, 0.92), unitv(0.97, 0.0, -0.18), unitv(0.18, 0.0, 0.98)),
      fin_bat=finish_bat((0.76, -0.12, 1.30), 0, 52), look_k=0.3, rheel_fin=52)

# 05 cover drive: front toe ~30 deg to the off side, front shoulder and face turned ~25 deg toward cover at
# contact, the bat toe trailing the hands; checked high finish aimed at cover
CD_S = V((0.58, -0.56, 0.20))
ff_v2(5, stride=(0.60, -0.30, 0), ltoe=(0.87, -0.5, 0),
      hips_land=Hroot(0.18, -0.06, 0.92), body_land=body(ptilt=14, lean=16, side=6, twist=-8, pyaw=-4),
      hips_con=Hroot(0.44, -0.16, 0.74), body_con=body(ptilt=12, lean=22, side=45, twist=-20, pyaw=-10),
      con_bat=bat_through(CD_S, (-0.21, -0.05, -1), (math.cos(math.radians(26)), -math.sin(math.radians(26)), -0.14)),
      hips_f1=Hroot(0.45, -0.16, 0.76), body_f1=body(ptilt=10, lean=26, side=40, twist=-6, pyaw=0),
      hips_fin=Hroot(0.44, -0.15, 0.79), body_fin=body(ptilt=8, lean=18, side=30, twist=14, pyaw=10),
      f1_bat=bat_g((0.80, -0.48, 0.90), (dirv(50) * 0.97 + V((0, 0, -0.18))).normalized(), (dirv(50) * 0.18 + V((0, 0, 0.98))).normalized()),
      fin_bat=finish_bat((0.70, -0.42, 1.24), 55, 50), look_k=0.3, rheel_fin=52,
      relax_hips=Hroot(0.16, -0.02, 0.94))


# ---------------------------------------------------------------- BACK-FOOT CROSS BAT: the pull (items 3, 11)
def pull_v2(sid, *, tc=30, n=90):
    bl_b, con_b0, f1_b, f2_b = old_bats(sid, tc)
    old = shots74.SHOTS74[sid]
    # contact at arm's length in front of the front hip: the sweet spot 8 cm further toward the bowler than
    # the author's and the face squarer (322 instead of 314), so the handle sits out in front, not at the navel
    S = V((0.48, -0.42, 1.00))
    con_b = bat_through(S, (0.62, -0.78, -0.26), (math.cos(math.radians(-38)), -math.sin(math.radians(-38)), -0.12))
    bl_b = bat_shift(bl_b, (-0.04, -0.06, -0.12))     # lower than the author's, a hand's width off the grille
    back = dict(rball=V((-0.32, -0.26, 0)), rtoe=V((0.5, -1, 0)), rheel=4.0, rkp=V((0.6, -0.8, 0.3)))
    front = dict(lball=V((0.08, 0.10, 0)), ltoe=V((1, -0.3, 0)), lheel=0.0, lkp=V((1, -0.3, 0.3)))
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_b, 0.45), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.16, -0.02, 0.98), **body(ptilt=10, lean=10, side=0, twist=4, pyaw=4),
             **mid_bat(STANCE, bl_b, 0.8), look=BOWLER, lpole=(0.9, -0.1, 0.4), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(-0.22, -0.03, 0.97), **body(ptilt=8, lean=8, side=-2, twist=10, pyaw=8),
             **bl_b, look=BOWLER, lpole=(0.9, 0.1, 0.4), rpole=(-0.5, 0.3, -0.8))
    # weight over the back foot, chest to midwicket, arms out straight at waist height
    con = P(STANCE, **back, **front, hips=Hroot(-0.28, 0.0, 0.96), **body(ptilt=6, lean=0, side=2, twist=45, pyaw=50),
            **con_b, lpole=(0.4, 0.3, -0.9), rpole=(0.3, -0.4, -0.9))
    con["look"] = look_dn(S, eyes_of(con), 30.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.5), **body(ptilt=6, lean=4, side=4, twist=30, pyaw=32))
    lf = far(S, dirv(old["angle"]), old["lofted"])
    # the follow-through: past mid-on ~30 deg below horizontal at +0.1 s, then the wrists roll it round past
    # the body toward fine leg by +0.3 s, pivoting on the back foot with the front leg opened
    pivot = dict(rtoe=V((1, 0.3, 0)), rheel=22.0, rkp=V((1, 0.3, 0.3)), ltoe=V((1, 0.6, 0)), lkp=V((1, 0.6, 0.3)))
    f1 = P(con, hips=Hroot(-0.25, 0.02, 0.96), **body(ptilt=6, lean=-2, side=4, twist=62, pyaw=62),
           **bat_g((0.20, 0.10, 0.98), unitv(0.30, 0.86, -0.42), unitv(-0.25, 0.35, 0.90)),
           rtoe=V((0.8, -0.3, 0)), rheel=12.0, lpole=(-0.2, 0.8, -0.6), rpole=(0.5, -0.3, -0.8))
    f1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(f1), 24.0)
    fin = P(con, hips=Hroot(-0.17, 0.04, 0.97), **body(ptilt=6, lean=-4, side=6, twist=72, pyaw=72), **pivot,
            **bat_g((0.02, 0.22, 1.50), unitv(-0.72, 0.62, -0.30), unitv(0.3, 0.3, 0.9)),   # round 7: high wrap, bat flat behind the head (reference f80)
            lheel=8.0, lpole=(-0.6, 0.6, -0.5), rpole=(0.6, 0.3, -0.7))
    fin["look"] = look_dn(lf, eyes_of(fin), 15.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(-0.16, 0.02, 0.985), -0.06, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (20, P(land, **mid_bat(STANCE, bl_b, 0.95))),
            (tc - 8, land), (tc - 4, mid), (tc, con), (tc + 3, f1), (tc + 9, fin), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


pull_v2(42)


# ---------------------------------------------------------------- SWEEP (items 6, 11)
def sweep_v2(sid, *, tc=30, n=90):
    bl_b, con_b0, f1_b, f2_b = old_bats(sid, tc)
    # contact: blade ~15 deg toe-down, arms nearly straight, hands well out in front of the front pad at knee
    # height, face to square leg and just starting to roll.  The sweet spot moves up and out from the author's
    # (0.86,-0.25,0.16) to (0.95,-0.30,0.31): with the blade that flat and the hands at knee height it cannot stay at 0.16.
    S = V((0.95, -0.30, 0.31))
    con_b = bat_through(S, (0.92, 0.34, -0.26), (math.cos(math.radians(250)), -math.sin(math.radians(250)), -0.16))
    kneel = dict(rball=V((-0.40, 0.10, 0)), rtoe=V((0.2, -1, 0)), rheel=75.0, rkp=V((0.3, -0.2, -1)))
    bl_low = bat_g((-0.08, -0.16, 0.98), (-0.75, -0.35, 0.45), (0.3, -1, -0.4))      # shots.BL_LOW
    front = dict(lball=V((0.78, -0.10, 0)), ltoe=V((0.45, -1, 0)), lheel=0.0, lkp=V((0.45, -1, 0.35)))
    trig = P(STANCE, hips=ST_ROOT + V((-0.01, 0, 0)), **mid_bat(STANCE, bl_low, 0.5), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    # the back knee starts down as the front foot goes out
    going = P(STANCE, rball=V((-0.36, 0.06, 0)), rtoe=V((0.2, -1, 0)), rheel=50.0, rkp=V((0.3, -0.3, -1)),
              hips=Hroot(0.10, 0.05, 0.82), **body(ptilt=14, lean=16, side=8, twist=4, pyaw=6),
              **bl_low, look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    down = P(STANCE, **kneel, **front, hips=Hroot(0.17, 0.05, 0.60), **body(ptilt=18, lean=18, side=12, twist=6, pyaw=10),
             **mid_bat(bl_low, bl_b, 0.5), look=look_dn(S + V((1.5, 0, 0.3)), V((0.4, -0.1, 1.15)), 25.0),
             lpole=(0.9, -0.3, 0.2), rpole=(-0.5, 0.3, -0.8))
    land = P(STANCE, **kneel, **front, hips=Hroot(0.21, 0.04, 0.52), **body(ptilt=22, lean=24, side=20, twist=8, pyaw=12),
             **bl_b, lpole=(0.9, -0.3, 0.1), rpole=(-0.4, 0.3, -0.9))
    land["look"] = look_dn(S + V((0.8, 0, 0.15)), eyes_of(land), 28.0)
    con = P(STANCE, **kneel, **front, hips=Hroot(0.24, 0.02, 0.50), **body(ptilt=24, lean=36, side=26, twist=10, pyaw=14),
            **con_b, lpole=(0.8, -0.5, -0.2), rpole=(-0.1, -0.6, -0.8))
    con["look"] = look_dn(S, eyes_of(con), 32.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.55))
    # the sweep carries on round past square leg at hip height, then up toward the front shoulder, wrists rolled
    f1 = P(con, hips=Hroot(0.21, 0.06, 0.51), **body(ptilt=20, lean=22, side=16, twist=24, pyaw=20),
           **bat_g((0.58, -0.12, 0.52), unitv(0.10, 0.99, -0.08), unitv(-0.55, 0.0, -0.84)),
           lpole=(0.6, 0.3, -0.6), rpole=(-0.3, -0.5, -0.8))
    f1["look"] = look_dn(S + V((0.3, 1.2, 0.0)), eyes_of(f1), 24.0)
    fin = P(con, hips=Hroot(0.21, 0.07, 0.53), **body(ptilt=18, lean=16, side=12, twist=36, pyaw=26),
            **bat_g((0.44, 0.10, 0.80), unitv(-0.62, 0.55, 0.56), unitv(0.10, -0.75, 0.65)),
            lpole=(0.3, 0.7, 0.3), rpole=(-0.4, -0.4, -0.8))
    fin["look"] = look_dn(S + V((0.0, 2.0, 0.0)), eyes_of(fin), 20.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    up = relax_key(Hroot(0.22, 0.04, 0.95), 0.32,
                   dict(rball=V((-0.22, -0.03, 0)), rtoe=V((0, -1, 0)), rheel=4.0, rkp=V((0, -1, 0.25)),
                        lball=V((0.60, -0.08, 0)), ltoe=V((0.4, -1, 0)), lheel=0.0, lkp=V((0.4, -1, 0.3))))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (15, going), (19, down), (tc - 8, land), (tc - 4, mid), (tc, con),
            (tc + 4, f1), (tc + 9, fin), (tc + 28, hold), (tc + 42, up), (n - 1, STANCE)]
    register(sid, keys, tc, n)


sweep_v2(22)


# ---------------------------------------------------------------- BACK-FOOT VERTICAL BAT: the defence (items 4, 5, 11)
def bf_v2(sid, *, tc=30, n=90):
    bl_b, con_b0, f1_b, f2_b = old_bats(sid, tc)
    bl_b = bat_shift(bl_b, (-0.06, 0.0, 0.03))        # the short backlift stays by the back shoulder
    # dead bat close to the body: hands at sternum height ~25 cm in front of the chest, the blade angled down
    # just in front of the back pad.  The sweet spot moves from the author's (0.04,-0.28,0.72) to
    # (-0.24,-0.29,0.69): 0.28 m nearer the batter (with the handle angle kept, the hands cannot come in otherwise).
    con_b = bat_through(V((-0.24, -0.29, 0.69)), (-0.30, -0.06, -0.95), (1, 0.05, -0.30))
    S = sweet_of(con_b)
    back = dict(rball=V((-0.50, -0.18, 0)), rtoe=V((0.15, -1, 0)), rheel=0.0, rkp=V((0.15, -1, 0.3)))
    front = dict(lball=V((-0.08, -0.12, 0)), ltoe=V((0.5, -1, 0)), lheel=10.0, lkp=V((0.5, -1, 0.3)))
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_b, 0.4), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.14, 0.02, 1.00), **body(ptilt=10, lean=10, side=2, twist=-2, pyaw=0),
             **mid_bat(STANCE, bl_b, 0.75), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    drag = P(STANCE, **back, **front, hips=Hroot(-0.24, 0.0, 0.99), **body(ptilt=8, lean=8, side=4, twist=2, pyaw=2),
             **bl_b, look=BOWLER, lpole=(0.9, -0.1, 0.4), rpole=(-0.5, 0.2, -0.8))
    land = P(drag, hips=Hroot(-0.26, 0.0, 0.99), **body(ptilt=8, lean=8, side=6, twist=6, pyaw=2))
    land["look"] = look_dn(S + V((2.0, 0, 0.5)), eyes_of(land), 15.0)
    con = P(STANCE, **back, **front, hips=Hroot(-0.26, 0.0, 0.99), **body(ptilt=8, lean=8, side=10, twist=18, pyaw=6),
            **con_b, lpole=(0.8, -0.5, 0.1), rpole=(-0.3, -0.4, -0.9))
    # neck and head bent forward ~30 deg: the eyes look down onto the blade
    con["look"] = look_dn(S, eyes_of(con), 32.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.55))
    f1 = P(con, bg=V(con_b["bg"]) + V((-0.02, 0, -0.01)))
    f1["look"] = look_dn(S + V((-0.05, 0, -0.05)), eyes_of(f1), 33.0)
    fin = P(con, bg=V(con_b["bg"]) + V((-0.03, 0, -0.02)))
    fin["look"] = look_dn(S + V((-0.15, 0, -0.1)), eyes_of(fin), 34.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(-0.20, 0.02, 1.0), -0.10, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (20, drag), (tc - 8, land), (tc - 4, mid), (tc, con),
            (tc + 6, f1), (tc + 14, fin), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


bf_v2(32)


# ================================================================= ROLL-OUT: the remaining 68 shots, family by family
# Same rules as the pilot: depth from the knees and pelvis, head level over the ball, front foot down at
# contact - 8, back heel up from contact, hands clear of the hips and face, the bat face toward the shot.
# Every shot keeps the pack's contact frame and angle_deg; a sweet spot moves only where the arms cannot
# reach the pack's (MOVED lists them, old -> new, for the hand-over and the game's contact tables).
from shots74 import face, signed  # noqa: E402

WAVES = {}
MOVED = {}


def moved(sid, new):
    """record a sweet spot moved off the pack's (returns the new one)"""
    new = V(new)
    old = V(shots74.SHOTS74[sid]["sweet"])
    if (new - old).length > 0.005:
        MOVED[sid] = ([round(x, 3) for x in old], [round(x, 3) for x in new])
    return new


def pack_sweet(sid):
    return V(shots74.SHOTS74[sid]["sweet"])


def axis_az(az, elev):
    """a unit vector along field azimuth az (angle_deg convention), elev degrees above horizontal"""
    d = dirv(az)
    e = math.radians(elev)
    return (d * math.cos(e) + V((0, 0, math.sin(e)))).normalized()


def through_bat(hands, az, elev, face_up=60.0):
    """the bat swung on through the line: bottom-hand grip at `hands`, blade along az at `elev` (negative =
    below horizontal), face turned up `face_up` degrees from the shot direction"""
    a = axis_az(az, elev)
    d = dirv(az)
    n = (d * math.cos(math.radians(face_up)) + V((0, 0, math.sin(math.radians(face_up))))).normalized()
    return bat_g(V(hands), a, n)


def wrap_bat(hands, axis, facev):
    return bat_g(V(hands), unitv(*axis), unitv(*facev))


def downswing(con_b, bl_b, phi, k_bg, d=None):
    """the bat phi degrees before contact on a swing in its own plane: the contact bat rotated back about the
    axis square to the blade and the swing direction d (default: the face's horizontal direction), so it comes
    down the line instead of slerping round from the backlift; the grip k_bg of the way back to the backlift's"""
    from mathutils import Matrix
    a = V(con_b["ba"]).normalized()
    n = ortho(con_b["bn"], a)
    d = V(d) if d is not None else V((n.x, n.y, 0.0))
    d = (d - a * d.dot(a)).normalized()
    k = a.cross(d).normalized()
    M = Matrix.Rotation(-math.radians(phi), 3, k)
    return dict(bg=V(con_b["bg"]).lerp(V(bl_b["bg"]), k_bg) + V((0, 0, 0.04 * math.sin(math.radians(phi)))),
                ba=(M @ a).normalized(), bn=(M @ n).normalized(), batw=1.0)


def relax_back(hx, bat_x, feet):
    return relax_key(Hroot(hx, 0.02, 1.0), bat_x, feet)


# ---------------------------------------------------------------- BACK-FOOT VERTICAL BAT: punches, clip, glance, nudge
def bfx_v2(sid, mode, *, S=None, rball=(-0.50, -0.18), lball=(-0.08, -0.12), hips_con=(-0.24, -0.02, 0.97),
           body_con=None, f1_bat=None, fin_bat=None, f1_body=None, fin_body=None, tc=30, n=90):
    """back and across, tall, the hands out in front under the eyes; punches check high up the line,
    the clip/nudge roll the wrists round to the leg side, the glance holds the angled face."""
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    bl_b, con_b0, _, _ = old_bats(sid, tc)
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    if mode == "punch":
        con_b = bat_through(S, (-0.26, -0.05 * math.sin(math.radians(th)), -0.96), face(th, 3, -0.15))
    elif mode in ("clip", "nudge"):
        con_b = bat_through(S, (-0.22, 0.14, -0.96), face(th, 2.5, -0.15))
    else:                                                        # glance: angled, the face turned late
        con_b = bat_through(S, (-0.30, 0.16, -0.94), face(th, 1.0, -0.1))
    bl_b = bat_shift(bl_b, (-0.04, -0.12, 0.12 if mode == "punch" else 0.10))  # hands up by the back shoulder, out in front of the chest
    back = dict(rball=V((rball[0], rball[1], 0)), rtoe=V((0.15, -1, 0)), rheel=0.0, rkp=V((0.15, -1, 0.3)))
    front = dict(lball=V((lball[0], lball[1], 0)), ltoe=V((0.5, -1, 0)), lheel=8.0, lkp=V((0.5, -1, 0.3)))
    body_con = body_con or body(ptilt=8, lean=8, side=10, twist=8 if mode == "punch" else 16, pyaw=4 if mode == "punch" else 10)
    hx, hy, hz = hips_con
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_b, 0.4), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.14, hy * 0.5 + 0.02, 1.00), **body(ptilt=10, lean=10, side=2, twist=-2, pyaw=0),
             **mid_bat(STANCE, bl_b, 0.75), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    drag = P(STANCE, **back, **front, hips=Hroot(hx, hy, hz + 0.02), **body(ptilt=8, lean=8, side=4, twist=2, pyaw=2),
             **bl_b, look=BOWLER, lpole=(0.9, -0.1, 0.4), rpole=(-0.5, 0.2, -0.8))
    land = P(drag, **body(ptilt=8, lean=8, side=6, twist=4, pyaw=2))
    land["look"] = look_dn(S + V((2.0, 0, 0.5)), eyes_of(land), 15.0)
    con = P(STANCE, **back, **front, hips=Hroot(hx, hy, hz), **body_con, **con_b, lpole=(0.8, -0.5, 0.1), rpole=(-0.3, -0.4, -0.9))
    con["look"] = look_dn(S, eyes_of(con), 32.0)
    sw = dirv(signed(th) * 0.5) if mode == "punch" else V((1, 0, 0))     # the bat comes down the line, the wrists turn it late
    mid = P(blend(land, con, 0.5), **downswing(con_b, bl_b, 95, 0.45, sw))
    mid2 = P(blend(land, con, 0.8), **downswing(con_b, bl_b, 45, 0.18, sw))
    lf = far(S, dirv(th), False)
    if mode == "glance":
        g = V(con_b["bg"])
        f1 = P(con, bg=g + V((-0.02, 0.02, 0.02)), **(f1_body or body_con))
        f1["look"] = look_dn(S + V((-0.05, 0.05, -0.02)), eyes_of(f1), 33.0)
        fin = P(con, bg=g + V((-0.03, 0.03, 0.03)), **(fin_body or body_con))
        fin["look"] = look_dn(S + V((-0.10, 0.10, -0.05)), eyes_of(fin), 34.0)
        f1_df, fin_df = 4, 9
    else:
        f1 = P(con, **f1_bat, **f1_body, rheel=14.0, lpole=(0.8, 0.0, 0.6), rpole=(-0.3, -0.4, -0.8))
        f1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(f1), 24.0)
        fin = P(con, **fin_bat, **fin_body, rheel=22.0, lheel=4.0,
                lpole=(0.8, 0.2, 0.2) if mode == "punch" else (-0.3, 0.8, -0.5), rpole=(-0.3, -0.6, -0.6) if mode == "punch" else (0.4, -0.3, -0.8))
        fin["look"] = look_dn(lf, eyes_of(fin), 16.0)
        f1_df, fin_df = 4, 9
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(hx + 0.04, 0.02, 0.97), -0.10, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (20, drag), (tc - 8, land), (tc - 4, mid), (tc - 2, mid2), (tc, con),
            (tc + f1_df, f1), (tc + fin_df, fin), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


def punch_v2(sid, *, S=None, rball, lball, hips_con, body_con, hands_f1, hands_fin, fin_elev=48):
    th = shots74.SHOTS74[sid]["angle"]
    az = signed(th) * 0.85
    bfx_v2(sid, "punch", S=S, rball=rball, lball=lball, hips_con=hips_con, body_con=body_con,
           f1_bat=through_bat(hands_f1, az, -18, 70), f1_body=dict(body_con, side=body_con["side"] - 2, twist=body_con["twist"] + 6),
           fin_bat=finish_bat(hands_fin, az, fin_elev), fin_body=dict(body_con, side=body_con["side"] - 4, lean=body_con["lean"] - 2,
                                                                       twist=body_con["twist"] + 10, pyaw=body_con["pyaw"] + 6))


# 34 straight punch / 35 off punch / 36 cover punch: tall on the back foot, top-hand punch, short checked finish
punch_v2(34, rball=(-0.48, -0.18), lball=(-0.06, -0.12), hips_con=(-0.20, -0.02, 0.97),
         body_con=body(ptilt=8, lean=10, side=12, twist=6, pyaw=4), hands_f1=(0.14, -0.30, 1.06), hands_fin=(0.08, -0.24, 1.30))
punch_v2(35, rball=(-0.48, -0.24), lball=(-0.06, -0.18), hips_con=(-0.20, -0.07, 0.97),
         body_con=body(ptilt=8, lean=12, side=12, twist=2, pyaw=2), hands_f1=(0.14, -0.38, 1.06), hands_fin=(0.08, -0.32, 1.30))
punch_v2(36, rball=(-0.46, -0.32), lball=(-0.04, -0.28), hips_con=(-0.20, -0.14, 0.96),
         body_con=body(ptilt=8, lean=16, side=10, twist=-6, pyaw=-4), hands_f1=(0.10, -0.50, 1.06), hands_fin=(0.06, -0.42, 1.28))

# 47 clip / 49 nudge: the bat comes down straight, the wrists turn the face to the leg side late and roll it round
bfx_v2(47, "clip", S=(0.14, -0.14, 0.86), rball=(-0.48, -0.10), lball=(-0.08, -0.04), hips_con=(-0.24, 0.02, 0.97),
       body_con=body(ptilt=8, lean=8, side=10, twist=16, pyaw=10),
       f1_bat=wrap_bat((0.16, 0.02, 0.98), (0.20, 0.92, -0.34), (0.1, 0.3, -0.95)), f1_body=body(ptilt=8, lean=6, side=8, twist=28, pyaw=20),
       fin_bat=wrap_bat((0.06, 0.20, 1.10), (-0.45, 0.80, -0.40), (-0.2, 0.1, -0.97)), fin_body=body(ptilt=6, lean=4, side=6, twist=36, pyaw=26))
bfx_v2(49, "nudge", S=(0.14, -0.17, 0.86), rball=(-0.48, -0.12), lball=(-0.08, -0.06), hips_con=(-0.24, 0.0, 0.97),
       body_con=body(ptilt=8, lean=8, side=10, twist=14, pyaw=8),
       f1_bat=wrap_bat((0.14, -0.02, 0.96), (0.05, 0.75, -0.66), (0.4, 0.5, -0.75)), f1_body=body(ptilt=8, lean=7, side=9, twist=22, pyaw=14),
       fin_bat=wrap_bat((0.12, 0.06, 1.02), (-0.10, 0.80, -0.59), (0.2, 0.4, -0.9)), fin_body=body(ptilt=8, lean=6, side=8, twist=26, pyaw=18))
# 48 glance: the angled face held, no follow-through
bfx_v2(48, "glance", S=(0.10, -0.12, 0.84), rball=(-0.48, -0.10), lball=(-0.08, -0.04), hips_con=(-0.24, 0.02, 0.97),
       body_con=body(ptilt=8, lean=8, side=10, twist=16, pyaw=10))
WAVES["bf"] = [34, 35, 36, 47, 48, 49]


# ---------------------------------------------------------------- FRONT FOOT: drives, lofts, chips, drops (ff_v2)
# The pilot's straight drive (0 deg) and cover drive (55 deg) are the two anchors: every other vertical-bat drive
# interpolates their stride, pelvis, trunk and finish by o = how far toward the off side it goes (0 straight,
# 1 cover, negative = the on side), and swings the bat down its own plane instead of slerping from the backlift.
def badd(b, **d):
    out = dict(b)
    for k_, v_ in d.items():
        out[k_] = out[k_] + v_
    return out


def drive_v2(sid, mode, *, S=None, k=3.0, wide=False, inside_out=False, dh_con=(0, 0, 0), db_con=None, dh_fin=(0, 0, 0),
             db_fin=None, d_fin_hands=(0, 0, 0), stride_d=(0, 0), a0=None, look_k=0.8):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    sg = signed(th)
    o = max(-0.6, min(1.6, sg / 55.0))
    op = max(0.0, o)
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    elev = dict(ground=-0.12, lofted=0.38, chip=0.18, dead=-0.28)[mode]
    sx = math.sin(math.radians(th))
    if a0 is None and inside_out:
        a0 = (-0.15, 0.18, -1)                               # the blade angled out: hands away from the front pad
    if a0 is None:
        a0 = (-0.36, -0.02 * sx, -0.93) if mode == "dead" else (-0.21 if mode != "lofted" else -0.10, -0.06 * sx, -1)
    con_bat = bat_through(S, a0, face(th, k, elev))
    if wide:
        stride = (S.x + 0.10 + stride_d[0], S.y + 0.18 + stride_d[1], 0)
        ltoe = (0.75, -0.7, 0)
    elif inside_out:
        stride = (0.66 + stride_d[0], 0.14 + stride_d[1], 0)
        ltoe = (0.80, -0.6, 0)
    else:
        stride = (S.x - 0.04 + 0.06 * o + stride_d[0], S.y + 0.20 + 0.06 * op + stride_d[1], 0)
        ltoe = tuple(V((0.55, -1, 0)).lerp(V((0.87, -0.5, 0)), min(op, 1.0))) if o >= 0 else (0.55 + 0.4 * -o, -1, 0)
    hy0 = 0.20 if inside_out else 0.0     # the head stays inside (leg side of) the ball
    hips_land = Hroot(0.18, 0.03 - 0.09 * op + hy0 * 0.6, 0.93 - 0.01 * op)
    body_land = body(ptilt=14, lean=14 + 2 * op, side=6, twist=-4 - 4 * o, pyaw=-2 - 2 * o)
    hc = V((S.x - 0.24 + 0.10 * min(op, 1.0), -0.16 * min(op, 1.0) + hy0, 0.77 - 0.03 * min(op, 1.0))) + V(dh_con)
    if wide:
        hc = V((S.x - 0.08, S.y + 0.30, 0.74)) + V(dh_con)
    bc = dict(ptilt=10 + 2 * op, lean=12 + 10 * min(op, 1.2), side=44 + 12 * min(o, 0.0), twist=32 - 52 * min(o, 1.1), pyaw=12 - 22 * min(o, 1.1))
    if wide:
        bc = dict(ptilt=12, lean=28, side=36, twist=-26, pyaw=-14)
    if inside_out:
        bc.update(lean=bc["lean"] - 6)
    if mode == "dead":
        bc.update(twist=bc["twist"] - 6, pyaw=bc["pyaw"] - 4, lean=bc["lean"] + 2)
    bc.update(db_con or {})
    body_con = body(**bc)
    oc = min(op, 1.0)
    sw = dirv(signed(th) * 0.5)
    if mode == "dead":
        ff_v2(sid, stride=stride, ltoe=ltoe, hips_land=hips_land, body_land=body_land, hips_con=Hroot(*hc), body_con=body_con,
              defence=True, con_bat=con_bat, look_down=35, look_k=0.55, bl_shift=(0.10, 0.0, 0.02),
              bl_bat=BL_LOW_V2, swing=[(4, 70, 0.4, sw), (2, 35, 0.15, sw)],
              lpole_con=(1, -0.3, 0.3), rpole_con=(-0.3, 0.2, -1))
        return
    hf1 = hc + V((0.05 - 0.04 * oc, 0.01, 0.02))
    bf1 = body(**badd(bc, ptilt=-2, lean=-4 + 8 * oc, side=-6 + oc, twist=8 + 6 * oc, pyaw=8 + 2 * oc))
    hfin = hc + V((0.06 - 0.06 * oc, 0.02 - 0.01 * oc, 0.015 + 0.01 * oc)) + V(dh_fin)
    bfn = badd(bc, ptilt=-4, lean=-6 + 2 * oc, side=-18 + 3 * oc, twist=12 + 22 * oc, pyaw=12 + 8 * oc)
    d = dirv(signed(th) * 0.9)
    if mode == "ground":
        f1_bat = bat_g(S + V((0.24 - 0.02 * oc, 0.08, 0.72 - 0.02 * oc)), (d * 0.97 + V((0, 0, -0.18))).normalized(),
                       (d * 0.18 + V((0, 0, 0.98))).normalized())
        # focus batch 1 (BAT-7): a ground drive CHECKS -- hands stop about chest height, the blade held pointing
        # up the shot's line, head still down over it -- so it never reads like the lofted finish
        fin_bat = finish_bat(S + V((0.16 - 0.02 * oc, 0.16 - 0.02 * oc, 0.98 - 0.06 * oc)) + V(d_fin_hands), th, 38 - 2 * oc)
        bfn = badd(bfn, lean=4, side=4)
    elif mode == "lofted":
        # ... and a lofted drive lifts THROUGH it: a full, high finish, the hands above the head, the bat over the
        # shoulder and the chest opened up to the shot
        f1_bat = through_bat(S + V((0.22, 0.08, 0.90)), signed(th) * 0.9, 35, 70)
        fin_bat = finish_bat(S + V((0.12, 0.12, 1.42)) + V(d_fin_hands), th, 115)
        bfn = badd(bfn, twist=14, pyaw=10, lean=-8, side=-4)
    else:                                                        # chip: punchy, the hands stop at chest height
        f1_bat = through_bat(S + V((0.24, 0.08, 0.76)), signed(th) * 0.9, 8, 70)
        fin_bat = finish_bat(S + V((0.18, 0.12, 0.94)) + V(d_fin_hands), th, 40)
    bfn.update(db_fin or {})
    ff_v2(sid, stride=stride, ltoe=ltoe, hips_land=hips_land, body_land=body_land, hips_con=Hroot(*hc), body_con=body_con,
          con_bat=con_bat, hips_f1=Hroot(*hf1), body_f1=bf1, hips_fin=Hroot(*hfin), body_fin=body(**bfn),
          f1_bat=f1_bat, fin_bat=fin_bat, look_k=look_k, rheel_fin=55 if mode == "lofted" else 52,
          swing=[(4, 95, 0.45, sw), (2, 45, 0.18, sw)],
          back_feet=dict(rball=V((-0.22, 0.16, 0)), rtoe=V((0.2, -1, 0))) if inside_out else
          dict(rball=V((-0.22, -0.20, 0)), rtoe=V((0.3, -1, 0))) if wide else None,
          walk_feet=dict(rball=V((-0.22, 0.16, 0)), rtoe=V((0.2, -1, 0))) if inside_out else None,
          relax_hips=Hroot(0.16, hc.y * 0.4 + 0.02, 0.92 if wide else 0.94))


BL_LOW_V2 = bat_g((-0.08, -0.16, 0.98), (-0.75, -0.35, 0.45), (0.3, -1, -0.4))     # shots.BL_LOW

drive_v2(3, "ground")                  # on drive 345
drive_v2(4, "ground")                  # off drive 15
drive_v2(6, "ground")                  # extra cover drive 40
drive_v2(7, "ground", wide=True, a0=(-0.10, -0.55, -0.8), look_k=0.6, d_fin_hands=(0.16, 0.0, 0.0))                # square drive 90
drive_v2(8, "ground", wide=True, a0=(0.15, -0.55, -0.8), look_k=0.6, d_fin_hands=(0.16, 0.0, 0.0))                 # backward square drive 110 (late)
drive_v2(9, "lofted")                  # lofted straight 0
drive_v2(10, "lofted")                 # lofted on 340
drive_v2(11, "lofted")                 # lofted off 20
drive_v2(12, "lofted")                 # lofted cover 60
drive_v2(13, "lofted")                 # lofted extra cover 40
drive_v2(14, "ground", inside_out=True)                                  # inside-out 45
drive_v2(15, "lofted", inside_out=True)                                  # lofted inside-out 40
drive_v2(68, "chip")                   # chip straight 0
drive_v2(69, "chip")                   # chip cover 55
drive_v2(73, "dead", k=2.0)            # soft-hands drop, off side 30
drive_v2(74, "dead", k=2.0, stride_d=(0, 0.08))   # soft-hands drop, leg side 330 (front pad clear of the bat)
WAVES["ffdrive"] = [3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 68, 69, 73, 74]


# ---------------------------------------------------------------- FRONT FOOT LEG SIDE: flicks, whips, glances, chip
# The bat comes down straight past the pad and the wrists turn the face late; ground flicks wrap round low to the
# front shoulder, the lofted flick finishes over the front shoulder, the chip checks at chest height, the glance
# holds its angled face.  Walk-across shots step the back foot across to off first.
def whip_v2(sid, mode, *, S=None, walk=False, stride=None, hips_con=None, body_con=None, look_k=0.8, th=None):
    old = shots74.SHOTS74[sid]
    th = old["angle"] if th is None else th
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    deflect = mode == "deflect"
    elev = dict(ground=-0.1, lofted=0.35, chip=0.18, deflect=-0.1)[mode]
    a0 = (-0.30, 0.15, -0.94) if deflect else (-0.22, 0.10, -1)
    con_bat = bat_through(S, a0, face(th, 1.0 if deflect else 2.5, elev))
    walk_key = None
    if walk:
        feet_back = dict(rball=V((-0.12, -0.40, 0)), rtoe=V((0.3, -1, 0)))
        walk_key = dict(feet_back, hips=Hroot(-0.04, -0.16, 0.97))
        stride = stride or (0.42, -0.28, 0)          # round 3d: the front pad kept clear of the bat (was on the ball's line)
        hc = V(hips_con or (0.28, -0.32, 0.79))
        hland = Hroot(0.12, -0.26, 0.92)
    else:
        feet_back = None
        stride = stride or ((0.66, 0.14, 0) if deflect else (0.66, 0.08, 0))   # the glance's bat clear of the front pad
        hc = V(hips_con or (0.40, 0.07, 0.78))
        hland = Hroot(0.18, 0.05, 0.93)
    bc = body_con or dict(ptilt=10, lean=12, side=40, twist=36 if not deflect else 24, pyaw=18 if not deflect else 12)
    bland = body(ptilt=14, lean=14, side=6, twist=-2, pyaw=0)
    straight = V((1, 0, 0))
    if deflect:
        ff_v2(sid, stride=stride, ltoe=(0.8, -0.7, 0), hips_land=hland, body_land=bland, hips_con=Hroot(*hc), body_con=body(**bc),
              defence=True, con_bat=con_bat, look_down=35, look_k=look_k,
              bl_bat=bat_shift(BL_LOW_V2, (0.0, -0.26, 0.0)) if walk else BL_LOW_V2,     # the backlift goes across with him
              swing=[(4, 70, 0.4, straight), (2, 35, 0.15, straight)], back_feet=feet_back, walk_feet=walk_key,
              bl_shift=(0.10, 0.0, 0.02),
              lpole_con=(1, -0.2, 0.3), rpole_con=(-0.3, 0.2, -1))
        return
    if mode == "ground":
        f1_bat = bat_g(S + V((0.26, 0.06, 0.66)), axis_az(th, -15), (V((0, 0, -1)) + dirv(th) * 0.5).normalized())
        fin_bat = wrap_bat(S + V((-0.02, 0.26, 0.84)), (-0.40, 0.82, -0.30), (0.1, 0.1, -1))
        bfn = badd(bc, ptilt=-4, lean=-6, side=-16, twist=24, pyaw=16)
        lp, rp = (-0.3, 0.8, -0.4), (0.4, -0.3, -0.8)
    elif mode == "lofted":
        f1_bat = through_bat(S + V((0.18, 0.12, 0.74)), th, 25, 70)
        fin_bat = finish_bat(S + V((0.10, 0.20, 1.20)), th, 108)
        bfn = badd(bc, ptilt=-4, lean=-8, side=-18, twist=28, pyaw=18)
        lp, rp = (0.8, -0.3, 0.25), (-0.3, -0.6, -0.6)
    else:
        f1_bat = through_bat(S + V((0.20, 0.12, 0.66)), th, 6, 70)
        fin_bat = finish_bat(S + V((0.14, 0.16, 0.86)), th, 40)
        bfn = badd(bc, ptilt=-4, lean=-6, side=-14, twist=18, pyaw=12)
        lp, rp = (0.8, -0.3, 0.25), (-0.3, -0.6, -0.6)
    ff_v2(sid, stride=stride, ltoe=(0.8, -0.7, 0), hips_land=hland, body_land=bland, hips_con=Hroot(*hc), body_con=body(**bc),
          con_bat=con_bat, hips_f1=Hroot(*(hc + V((0.03, 0.02, 0.02)))), body_f1=body(**badd(bc, ptilt=-2, lean=-3, side=-6, twist=12, pyaw=10)),
          hips_fin=Hroot(*(hc + V((0.02, 0.04, 0.03)))), body_fin=body(**bfn), f1_bat=f1_bat, fin_bat=fin_bat, look_k=look_k,
          rheel_fin=52, swing=[(4, 95, 0.45, straight), (2, 45, 0.18, straight)], back_feet=feet_back, walk_feet=walk_key,
          bl_shift=(0.08, -0.26, 0.0) if walk else (0.08, 0.0, 0.0),        # the backlift goes across with him
          lpole_fin=lp, rpole_fin=rp, relax_hips=Hroot(0.16, hc.y * 0.4 + 0.03, 0.94), look_th=th)


whip_v2(16, "ground")                  # flick 305
whip_v2(17, "ground")                  # square flick 270
whip_v2(18, "lofted")                  # lofted flick 310
whip_v2(19, "deflect", S=(0.66, -0.10, 0.30))   # leg glance 200 (contact out in front of the front pad)
whip_v2(70, "chip")                    # chip midwicket 305
whip_v2(61, "deflect", walk=True, S=(0.60, -0.40, 0.30), stride=(0.40, -0.12, 0))   # walk-across flick 205 (contact out in front of the front pad)
whip_v2(62, "ground", walk=True)       # walk-across whip 270
WAVES["ffwhip"] = [16, 17, 18, 19, 61, 62, 70]


# ---------------------------------------------------------------- FRONT-FOOT CROSS BAT: the front-foot pulls, the slog
# pull_v2's shape with the weight forward on a short open front stride: contact at arm's length in front of the front
# hip, chest open to the bowler, the head level; the lofted finish wraps high over the front shoulder.
def ffpull_v2(sid, *, S=None, a0, stride, ltoe, hips_con, body_con, hips_land, fin_hands, f1_hands, back=None, tc=30, n=90):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    bl_b, _, _, _ = old_bats(sid, tc)
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    con_b = bat_through(S, a0, face(th, 3, 0.4))
    back = back or dict(rball=V((-0.26, -0.06, 0)), rtoe=V((0.3, -1, 0)), rheel=6.0, rkp=V((0.3, -1, 0.3)))
    front = dict(lball=V(stride), ltoe=V(ltoe), lheel=0.0, lkp=V(ltoe).normalized() + V((0, 0, 0.3)))
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_b, 0.45), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(*hips_land), **body(ptilt=10, lean=10, side=6, twist=6, pyaw=6),
             **bl_b, look=BOWLER, lpole=(0.9, 0.1, 0.4), rpole=(-0.5, 0.3, -0.8))
    con = P(STANCE, **dict(back, rheel=30.0), **front, hips=Hroot(*hips_con), **body_con, **con_b,
            lpole=(0.4, 0.3, -0.9), rpole=(0.3, -0.4, -0.9))
    con["look"] = look_dn(S, eyes_of(con), 30.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.5), **body(**badd(dict(body_con), twist=-15, pyaw=-16)))
    lf = far(S, dirv(th), True)
    pivot = dict(rtoe=V((1, 0.3, 0)), rheel=40.0, rkp=V((1, 0.3, 0.3)))
    bc = dict(body_con)
    f1 = P(con, hips=Hroot(*(V(hips_con) + V((0.02, 0.03, 0.01)))), **body(**badd(bc, lean=-4, twist=16, pyaw=12)),
           **through_bat(f1_hands, (th + 20) % 360, 30, 70), lpole=(-0.2, 0.8, -0.6), rpole=(0.5, -0.3, -0.8))
    f1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(f1), 20.0)
    fin = P(con, hips=Hroot(*(V(hips_con) + V((0.02, 0.05, 0.02)))), **body(**badd(bc, lean=-8, side=-10, twist=26, pyaw=20)), **pivot,
            **wrap_bat(fin_hands, (-0.55, 0.50, -0.65), (0.3, 0.3, 0.9)), lpole=(-0.6, 0.6, 0.2), rpole=(0.6, 0.3, -0.5))
    fin["look"] = look_dn(lf, eyes_of(fin), 10.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(0.10, 0.04, 0.95), 0.18, dict(dict(back, rheel=0.0), **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (tc - 8, land), (tc - 4, mid), (tc, con), (tc + 3, f1), (tc + 9, fin),
            (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


# 20 front-foot pull 300 / 21 square 265: waist-high ball, short open stride, horizontal bat at arm's length in
# front of the front hip.  The pack's contact (0.45,-0.40,0.95) sits on top of the hips for a front-foot pull,
# so the sweet spot moves 0.15 m toward the bowler (the pilot pull's arm's-length geometry).
ffpull_v2(20, S=(0.60, -0.42, 0.97), a0=(0.62, -0.78, -0.26), stride=(0.46, -0.02, 0), ltoe=(0.9, -0.5, 0),
          hips_land=(0.0, 0.0, 0.95), hips_con=(0.0, 0.02, 0.93), body_con=body(ptilt=6, lean=4, side=6, twist=46, pyaw=44),
          f1_hands=(0.40, 0.08, 1.12), fin_hands=(0.22, 0.20, 1.42))
ffpull_v2(21, S=(0.58, -0.42, 0.97), a0=(0.55, -0.83, -0.26), stride=(0.46, -0.02, 0), ltoe=(0.9, -0.5, 0),
          hips_land=(0.0, 0.0, 0.95), hips_con=(-0.02, 0.03, 0.93), body_con=body(ptilt=6, lean=4, side=6, twist=50, pyaw=48),
          f1_hands=(0.32, 0.14, 1.12), fin_hands=(0.18, 0.24, 1.42))
# 67 slog / cow shot 315: knee-high, front leg cleared to the leg side, knees bent, big swing across the line
ffpull_v2(67, a0=(0.45, -0.45, -0.75), stride=(0.56, 0.16, 0), ltoe=(0.9, -0.3, 0), hips_land=(0.12, 0.10, 0.90),
          hips_con=(0.20, 0.24, 0.76), body_con=body(ptilt=14, lean=22, side=20, twist=30, pyaw=24),
          f1_hands=(0.56, 0.10, 0.98), fin_hands=(0.30, 0.24, 1.40))
WAVES["ffcross"] = [20, 21, 67]
WAVES["ff"] = WAVES["ffdrive"] + WAVES["ffwhip"] + WAVES["ffcross"]


# ---------------------------------------------------------------- CUTS (off side, horizontal bat) - cut_v2
# pull_v2's shape turned to the off side: back and across (or a front-foot stride across), the bat from high over
# the off shoulder, contact at arm's length wide of the body with the front shoulder pointing at the ball, the
# depth in the knees, the head level over the line; the cut wraps across to the front shoulder, the steer/late cut
# holds its open face (soft hands), the upper cut finishes high over the off shoulder.
CUT_BL = bat_g((-0.10, -0.16, 1.38), (-0.25, -0.45, 0.86), (0, -1, -0.2))         # the pack's cut backlift (moved with the body below)


def cut_v2(sid, mode, *, S=None, front_foot=False, hips_con=None, body_con=None, a0=None, f1_bat=None, fin_bat=None, look_k=0.0, look_down=32.0, look_off=None,
           tc=30, n=90):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    if mode == "cut":
        con_b = bat_through(S, a0 or (0.85, -0.49, -0.2), face(th, 4.0, -0.15))    # round 7: blade out toward cover (reference video), face rolling over
    elif mode == "steer":
        con_b = bat_through(S, a0 or (-0.25, -0.35, -0.9), face(th, 1.0, -0.1))
    else:                                                                    # upper
        con_b = bat_through(S, a0 or (0.95, 0.10, 0.0), face(th, 2.5, 0.75))
    if front_foot:
        back = dict(rball=V((-0.24, -0.06, 0)), rtoe=V((0.1, -1, 0)), rheel=6.0, rkp=V((0.1, -1, 0.3)))
        front = dict(lball=V((0.44, -0.56, 0)), ltoe=V((0.35, -1, 0)), lheel=0.0, lkp=V((0.35, -1, 0.3)))
        hc = V(hips_con or (0.14, -0.34, 0.80))
        step_key = None
    else:
        back = dict(rball=V((-0.28, -0.60, 0)), rtoe=V((0.35, -1, 0)), rheel=4.0, rkp=V((0.35, -1, 0.3)))
        front = dict(lball=V((0.06, -0.38, 0)), ltoe=V((0.5, -1, 0)), lheel=6.0, lkp=V((0.5, -1, 0.3)))
        hc = V(hips_con or (-0.10, -0.36, 0.91))
        step_key = P(STANCE, **back, hips=Hroot(-0.08, -0.16, 0.98), **body(ptilt=10, lean=10, side=0, twist=-4, pyaw=-2),
                     **mid_bat(STANCE, bat_shift(CUT_BL, (0.0, hc.y - 0.04, 0.0)), 0.8), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    # the front shoulder points at the ball: the chest closes hard for the cut and the upper cut (the top hand has to
    # reach back to the handle), less for the steer
    bl_c = bat_shift(CUT_BL, (0.0, hc.y - 0.04, 0.0))   # round 4: the backlift goes across with him, out in front of his chest
    bc = body_con or dict(ptilt=12 if mode != "upper" else 10, lean=16 if mode != "upper" else 4, side=4 if mode != "upper" else -6,
                          twist=-14 if mode == "steer" else -38, pyaw=-8 if mode == "steer" else -22)
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_c, 0.45), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(*(hc + V((0.0, 0.06, 0.04)))), **body(ptilt=10, lean=10, side=2, twist=-4, pyaw=-2),
             **bl_c, lpole=(0.9, -0.1, 0.4), rpole=(-0.5, 0.2, -0.8))
    land["look"] = look_dn(S + V((1.5, 0, 0.3)), eyes_of(land), 15.0)
    con = P(STANCE, **back, **front, hips=Hroot(*hc), **body(**bc), **con_b, lpole=(0.6, -0.6, -0.2), rpole=(-0.2, -0.6, -0.8))
    # the gaze a touch behind the contact point (where the ball goes) keeps the eyes level with the chest closed
    look_off = look_off if look_off is not None else ((0, 0, 0) if mode == "steer" else (-0.35, 0, 0))
    con["look"] = look_dn(S + V(look_off), eyes_of(con), look_down, toward=BOWLER, k=look_k)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_c, con_b, 0.55))
    if mode != "steer":          # round 3d: the bat comes down from high OUTSIDE the off shoulder, not through the head
        mid.update(bat_g(hc + V((0.0, -0.54 if mode == "upper" else -0.50, 0.32)), (0.45, -0.55, 0.70), (0.2, -0.9, -0.3)))
    lf = far(S, dirv(th), mode == "upper")
    if f1_bat is None and mode != "steer":
        fol = (cut_follow if mode == "cut" else upper_follow)(S, th, hc)
        f1_bat, fin_bat = fol["f1_bat"], fol["fin_bat"]
    if mode == "steer":
        g = V(con_b["bg"])
        f1 = P(con, bg=g + V((-0.02, 0.02, 0.02)))
        f1["look"] = look_dn(S + V((-0.08, -0.05, -0.02)), eyes_of(f1), 33.0, toward=BOWLER, k=0.3)
        fin = P(con, bg=g + V((-0.03, 0.03, 0.03)))
        fin["look"] = look_dn(S + V((-0.15, -0.10, -0.04)), eyes_of(fin), 34.0, toward=BOWLER, k=0.3)
    else:
        f1 = P(con, **f1_bat, **body(**badd(bc, twist=6, pyaw=4, side=-2)), rheel=10.0, lpole=(0.5, -0.6, 0.2), rpole=(-0.3, -0.6, -0.7))
        f1["look"] = look_dn(S.lerp(lf, 0.25), eyes_of(f1), 26.0)
        fin = P(con, **fin_bat, **body(**badd(bc, twist=12, pyaw=8, side=-4, lean=-4)), rheel=16.0,
                lpole=(0.6, 0.2, 0.4) if mode == "upper" else (0.3, 0.6, -0.4), rpole=(-0.3, -0.6, -0.6))
        fin["look"] = look_dn(lf, eyes_of(fin), 18.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(hc.x + 0.04, hc.y * 0.5, 0.96), hc.x + 0.12, dict(dict(back, rheel=0.0), **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig)] + ([(15, step_key)] if step_key else []) + \
        [(tc - 8, land), (tc - 4, mid), (tc, con), (tc + 3, f1), (tc + 9, fin), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


def cut_follow(S, th, hc):
    """the cut's swing on through toward the shot, then wrapped across to the front shoulder"""
    d = dirv(th)
    f1 = bat_g(V(S) + V((-0.36, 0.20, 0.18)), (d * 0.85 + V((0.25, 0, -0.45))).normalized(), V((0, 0, -1)) + d * 0.3)
    # round 3d: the wrap ends with the blade UP behind the front shoulder (it used to point down through the hips)
    fin = wrap_bat(V(hc) + V((0.36, -0.36, 0.30)), (0.35, 0.35, 0.87), (0.3, 0.7, -0.2))      # up and forward, away from the head
    return dict(f1_bat=f1, fin_bat=fin)


def upper_follow(S, th, hc):
    d = dirv(th)
    f1 = bat_g(V(S) + V((-0.38, 0.22, 0.22)), (d * 0.5 + V((0, 0, 0.85))).normalized(), (-d * 0.6 + V((0, 0, 0.5))).normalized())
    fin = wrap_bat(V(hc) + V((0.30, -0.40, 0.50)), (0.2, -0.3, 0.93), (-0.7, -0.3, 0.5))      # high over the off side
    return dict(f1_bat=f1, fin_bat=fin)


cut_v2(30, "steer", front_foot=True)                                       # steer / dab 145
cut_v2(31, "cut", front_foot=True)                                         # front-foot cut 90
cut_v2(37, "cut")                                                           # square cut 90
cut_v2(38, "cut")                                                           # backward cut 110
cut_v2(39, "steer", look_off=(0.3, 0, 0))                                                       # late cut 140
cut_v2(40, "upper")                                                         # upper cut (point) 100
cut_v2(41, "upper")                                                         # upper cut (third man) 140
WAVES["cut"] = [30, 31, 37, 38, 39, 40, 41]


# ---------------------------------------------------------------- LEAVE (33) - leave_v2
# Back and across to cover off stump, tall, the bat lifted high over the back shoulder (hands above the head and
# clear of it, face turned away), the head still and level, the eyes following the ball past off stump toward the
# keeper without the head rolling; then the bat comes down and he settles back into the stance.
def leave_v2(sid, *, tc=30, n=90):
    back = dict(rball=V((-0.42, -0.18, 0)), rtoe=V((0.15, -1, 0)), rheel=0.0, rkp=V((0.15, -1, 0.3)))
    front = dict(lball=V((-0.04, -0.14, 0)), ltoe=V((0.5, -1, 0)), lheel=6.0, lkp=V((0.5, -1, 0.3)))
    up_bat = bat_g((-0.30, 0.06, 1.66), (-0.40, 0.42, 0.81), (0.1, 1, -0.2))         # hands above the back shoulder
    half_bat = bat_g((-0.22, 0.0, 1.40), (-0.45, 0.30, 0.84), (0.1, 1, -0.3))
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, BL_LOW_V2, 0.4), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.12, -0.04, 1.00), **body(ptilt=10, lean=10, side=2, twist=-2, pyaw=0),
             **mid_bat(STANCE, BL_LOW_V2, 0.8), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    drag = P(STANCE, **back, **front, hips=Hroot(-0.20, -0.06, 0.99), **body(ptilt=8, lean=8, side=2, twist=-2, pyaw=0),
             **half_bat, look=V((3, -0.5, 0.9)), lpole=(0.6, 0.6, 0.2), rpole=(-0.6, 0.4, -0.4))
    tall = body(ptilt=6, lean=6, side=0, twist=-4, pyaw=-2)
    con = P(STANCE, **back, **front, hips=Hroot(-0.20, -0.06, 1.00), **tall, **up_bat,
            lpole=(0.4, 0.8, 0.3), rpole=(-0.5, 0.6, -0.3))
    con["look"] = look_dn(V((0.0, -0.55, 0.85)), eyes_of(con), 30.0, toward=BOWLER, k=0.4)
    f1 = P(con, bg=V(up_bat["bg"]) + V((0, 0, 0.01)))
    f1["look"] = look_dn(V((-0.8, -0.55, 0.80)), eyes_of(f1), 28.0, toward=V((0.0, -0.55, 0.85)), k=0.5)
    fin = P(con, bg=V(up_bat["bg"]) + V((0.01, 0, 0.0)))
    fin["look"] = look_dn(V((-1.4, -0.5, 0.80)), eyes_of(fin), 24.0, toward=V((0.0, -0.55, 0.85)), k=0.5)
    down = P(fin, **half_bat, look=V((-1.0, -0.6, 0.8)))
    rel = relax_key(Hroot(-0.16, 0.02, 0.98), -0.06, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (20, drag), (tc - 6, P(drag, **mid_bat(half_bat, up_bat, 0.6))),
            (tc, con), (tc + 6, f1), (tc + 14, fin), (tc + 26, down), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)
    MOVED[sid] = (shots74.SHOTS74[sid]["sweet"], SHOTS_V2[sid]["sweet"])        # a leave has no contact: the bat's spot


leave_v2(33)
WAVES["leave"] = [33]


# ---------------------------------------------------------------- BACK-FOOT CROSS BAT: square pull, lofted pull, hooks
# pull_v2's own geometry (approved on 42): back and across, the weight on the back foot, the chest open to midwicket,
# contact at arm's length in front of the front hip; the ground pull rolls the wrists round past the body, the
# lofted pull and the hooks finish high over the front shoulder.  The hooks meet the ball at head height with the
# head inside the line and level.
def pullx_v2(sid, mode, *, S, a0, hips_con=(-0.28, 0.0, 0.96), body_con=None, f1=None, fin=None, k=None, tc=30, n=90):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    bl_b, _, _, _ = old_bats(sid, tc)
    S = moved(sid, S)
    elev = dict(pull=-0.15, lofted=0.35, hook=0.45)[mode]
    con_b = bat_through(S, a0, face(th, k or (3 if mode != "hook" else 2.5), elev))   # fine: k = 1, the face meets the ball
    bl_b = bat_shift(bl_b, (-0.04, -0.06, -0.12 if mode != "hook" else -0.04))
    back = dict(rball=V((-0.32, -0.26, 0)), rtoe=V((0.5, -1, 0)), rheel=4.0, rkp=V((0.6, -0.8, 0.3)))
    front = dict(lball=V((0.08, 0.10, 0)), ltoe=V((1, -0.3, 0)), lheel=0.0, lkp=V((1, -0.3, 0.3)))
    if mode == "hook":
        back = dict(rball=V((-0.36, -0.22, 0)), rtoe=V((0.5, -1, 0)), rheel=4.0, rkp=V((0.6, -0.8, 0.3)))
        front = dict(lball=V((0.04, 0.14, 0)), ltoe=V((1, -0.3, 0)), lheel=0.0, lkp=V((1, -0.3, 0.3)))
    bc = body_con or dict(ptilt=6, lean=0, side=2, twist=45, pyaw=50)
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl_b, 0.45), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.16, -0.02, 0.98), **body(ptilt=10, lean=10, side=0, twist=4, pyaw=4),
             **mid_bat(STANCE, bl_b, 0.8), look=BOWLER, lpole=(0.9, -0.1, 0.4), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(-0.22, -0.03, 0.97), **body(ptilt=8, lean=8, side=-2, twist=10, pyaw=8),
             **bl_b, look=BOWLER, lpole=(0.9, 0.1, 0.4), rpole=(-0.5, 0.3, -0.8))
    con = P(STANCE, **back, **front, hips=Hroot(*hips_con), **body(**bc), **con_b, lpole=(0.4, 0.3, -0.9), rpole=(0.3, -0.4, -0.9))
    con["look"] = look_dn(S, eyes_of(con), 30.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.5), **body(**badd(bc, lean=4, side=2, twist=-15, pyaw=-18)))
    lf = far(S, dirv(th), mode != "pull")
    pivot = dict(rtoe=V((1, 0.3, 0)), rheel=22.0, rkp=V((1, 0.3, 0.3)), ltoe=V((1, 0.6, 0)), lkp=V((1, 0.6, 0.3)))
    k1 = P(con, hips=Hroot(*(V(hips_con) + V((0.03, 0.02, 0.0)))), **body(**badd(bc, lean=-2, side=2, twist=17, pyaw=12)),
           **f1, rtoe=V((0.8, -0.3, 0)), rheel=12.0, lpole=(-0.2, 0.8, -0.6), rpole=(0.5, -0.3, -0.8))
    k1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(k1), 24.0 if mode == "pull" else 12.0)
    k2 = P(con, hips=Hroot(*(V(hips_con) + V((0.11, 0.04, 0.01)))), **body(**badd(bc, lean=-4, side=4, twist=27, pyaw=22)), **pivot,
           **fin, lheel=8.0, lpole=(-0.6, 0.6, -0.5) if mode == "pull" else (-0.6, 0.6, 0.3), rpole=(0.6, 0.3, -0.7))
    k2["look"] = look_dn(lf, eyes_of(k2), 15.0 if mode == "pull" else 5.0)
    hold = P(k2, hips=k2["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(-0.16, 0.02, 0.985), -0.06, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (20, P(land, **mid_bat(STANCE, bl_b, 0.95))),
            (tc - 8, land), (tc - 4, mid), (tc, con), (tc + 3, k1), (tc + 9, k2), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


HIGH_FIN = dict(**wrap_bat((0.02, 0.22, 1.50), (-0.72, 0.62, -0.30), (0.3, 0.3, 0.9)))      # over the front shoulder

# 43 square pull 270: the pack's contact (reachable), the face to square leg; ground finish wrapped round
pullx_v2(43, "pull", S=(0.40, -0.42, 1.00), a0=(0.62, -0.78, -0.26),
         f1=bat_g((0.18, 0.12, 0.98), unitv(0.10, 0.92, -0.40), unitv(-0.30, 0.30, 0.90)),
         fin=HIGH_FIN)                                    # round 7: high wrap, as in the reference video
# 44 lofted pull 305: under the ball, the face open, the finish high over the front shoulder
pullx_v2(44, "lofted", S=(0.40, -0.42, 1.00), a0=(0.62, -0.78, -0.10),
         f1=through_bat((0.24, 0.10, 1.12), 330, 30, 70), fin=HIGH_FIN)
# 45 hook (square) 250 / 46 hook (fine) 210: head height, swung through and over the front shoulder
pullx_v2(45, "hook", S=(0.25, -0.40, 1.52), a0=(0.60, -0.80, 0.10), hips_con=(-0.30, 0.02, 0.97),
         body_con=dict(ptilt=4, lean=-4, side=-4, twist=50, pyaw=48),
         f1=through_bat((0.06, 0.16, 1.40), 280, 25, 60), fin=HIGH_FIN)
pullx_v2(46, "hook", S=(0.10, -0.40, 1.52), a0=(0.50, -0.86, 0.10), hips_con=(-0.32, 0.02, 0.97), k=1.0,
         body_con=dict(ptilt=4, lean=-4, side=-4, twist=55, pyaw=52),
         f1=through_bat((-0.04, 0.18, 1.40), 250, 25, 60), fin=HIGH_FIN)
WAVES["cross"] = [43, 44, 45, 46]


# ---------------------------------------------------------------- KNEELING: sweeps, slog sweeps, paddle, laps, reverses,
# scoops (sweepx_v2).  sweep_v2's own timeline (approved on 22): the back knee goes down as the front foot goes out,
# the pelvis carries the depth (hip ~0.50 m), the head down over the line and level, contact out in front of the
# front pad with the arms nearly straight.  Sweet spots move up and out from the pack's (0.16-0.20 m high, under
# the front pad) the way the pilot moved 22's, where the arms cannot reach them.  A face that deflects the ball fine
# uses pure deflection (k = 1) so it always faces the incoming ball.
KNEEL_V2 = dict(rball=V((-0.40, 0.10, 0)), rtoe=V((0.2, -1, 0)), rheel=75.0, rkp=V((0.3, -0.2, -1)))
SWEEP_BODY_V2 = dict(ptilt=24, lean=36, side=26, twist=10, pyaw=14)


def sweepx_v2(sid, *, S, a0, k=1.6, elev=0.05, hips_con=(0.24, 0.02, 0.50), body_con=None, front=None, kneel=None,
              f1=None, fin=None, f1_body=None, fin_body=None, bl=None, look_after=None, look_k=0.9, tc=30, n=90):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    bl_b, _, _, _ = old_bats(sid, tc)
    if bl is not None:
        bl_b = bl
    S = moved(sid, S)
    con_b = bat_through(S, a0, face(th, k, elev))
    kneel = dict(KNEEL_V2, **(kneel or {}))
    front = front or dict(lball=V((0.78, -0.10, 0)), ltoe=V((0.45, -1, 0)), lheel=0.0, lkp=V((0.45, -1, 0.35)))
    bc = dict(SWEEP_BODY_V2, **(body_con or {}))
    hx, hy, hz = hips_con
    bl_low = bat_g((-0.08, -0.16, 0.98), (-0.75, -0.35, 0.45), (0.3, -1, -0.4))
    trig = P(STANCE, hips=ST_ROOT + V((-0.01, 0, 0)), **mid_bat(STANCE, bl_low, 0.5), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    going = P(STANCE, rball=V((-0.36, 0.06, 0)), rtoe=V((0.2, -1, 0)), rheel=50.0, rkp=V((0.3, -0.3, -1)),
              hips=Hroot(0.10, 0.05, 0.82), **body(ptilt=14, lean=16, side=8, twist=4, pyaw=6),
              **bl_low, look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    down = P(STANCE, **kneel, **front, hips=Hroot(hx - 0.07, hy + 0.03, hz + 0.10),
             **body(ptilt=18, lean=18, side=12, twist=6, pyaw=10),
             **mid_bat(bl_low, bl_b, 0.5), look=look_dn(S + V((1.5, 0, 0.3)), V((0.4, -0.1, 1.15)), 25.0),
             lpole=(0.9, -0.3, 0.2), rpole=(-0.5, 0.3, -0.8))
    land = P(STANCE, **kneel, **front, hips=Hroot(hx - 0.03, hy + 0.02, hz + 0.02),
             **body(**badd(bc, ptilt=-2, lean=-12, side=-6, twist=-2, pyaw=-2)), **bl_b, lpole=(0.9, -0.3, 0.1), rpole=(-0.4, 0.3, -0.9))
    land["look"] = look_dn(S + V((0.8, 0, 0.15)), eyes_of(land), 28.0)
    con = P(STANCE, **kneel, **front, hips=Hroot(hx, hy, hz), **body(**bc), **con_b, lpole=(0.8, -0.5, -0.2), rpole=(-0.1, -0.6, -0.8))
    con["look"] = look_dn(S, eyes_of(con), 32.0, toward=BOWLER, k=look_k)
    mid = P(blend(land, con, 0.5), **mid_bat(bl_b, con_b, 0.55))
    # after contact the eyes follow the ball only a little way (fine shots go behind him: turning the head round
    # to watch would roll it), staying low over the point of contact
    fine = abs(signed(th)) > 120
    la = V(look_after) if look_after is not None else S + dirv(th) * (0.5 if fine else 1.5)
    if f1 is None:                                   # deflections: the bat held, given a little with soft hands
        g = V(con_b["bg"])
        f1 = dict(con_b, bg=g + V((-0.03, 0.0, 0.02)))
        fin = dict(con_b, bg=g + V((-0.05, 0.0, 0.04)))
    k1 = P(con, hips=Hroot(hx - 0.03, hy + 0.04, hz + 0.01), **body(**(f1_body or bc)), **f1, lpole=(0.6, 0.3, -0.6), rpole=(-0.3, -0.5, -0.8))
    k1["look"] = look_dn(S.lerp(la, 0.5), eyes_of(k1), 30.0 if fine else 26.0, toward=BOWLER, k=look_k * (1.0 if fine else 0.4))
    k2 = P(con, hips=Hroot(hx - 0.03, hy + 0.05, hz + 0.03), **body(**(fin_body or bc)), **fin, lpole=(0.3, 0.7, 0.3), rpole=(-0.4, -0.4, -0.8))
    k2["look"] = look_dn(la, eyes_of(k2), 30.0 if fine else 22.0, toward=BOWLER, k=look_k * (1.0 if fine else 0.3))
    hold = P(k2, hips=k2["hips"] + V((0, 0, 0.01)))
    up = relax_key(Hroot(0.22, 0.04, 0.95), 0.32,
                   dict(rball=V((-0.22, -0.03, 0)), rtoe=V((0, -1, 0)), rheel=4.0, rkp=V((0, -1, 0.25)),
                        lball=V((0.60, -0.08, 0)), ltoe=V((0.4, -1, 0)), lheel=0.0, lkp=V((0.4, -1, 0.3))))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (15, going), (19, down), (tc - 8, land), (tc - 4, mid), (tc, con),
            (tc + 4, k1), (tc + 9, k2), (tc + 28, hold), (tc + 42, up), (n - 1, STANCE)]
    register(sid, keys, tc, n)


def sweep_fin(S, th, lofted):
    """the sweep goes on round past square at hip height then up to the front shoulder, wrists rolled; the slog
    sweep finishes high over the front shoulder"""
    if lofted:
        return dict(f1=through_bat(V(S) + V((-0.30, 0.22, 0.40)), th, 20, 70),
                    fin=wrap_bat(V((0.42, 0.14, 1.12)), (-0.55, 0.40, -0.73), (0.3, 0.3, 0.9)),
                    f1_body=dict(SWEEP_BODY_V2, lean=24, side=16, twist=26, pyaw=20),
                    fin_body=dict(SWEEP_BODY_V2, lean=16, side=10, twist=38, pyaw=26))
    return dict(f1=bat_g((0.58, -0.12, 0.52), unitv(0.10, 0.99, -0.08), unitv(-0.55, 0.0, -0.84)),
                fin=bat_g((0.44, 0.10, 0.80), unitv(-0.62, 0.55, 0.56), unitv(0.10, -0.75, 0.65)),
                f1_body=dict(SWEEP_BODY_V2, lean=22, side=16, twist=24, pyaw=20),
                fin_body=dict(SWEEP_BODY_V2, lean=16, side=12, twist=36, pyaw=26))


def reverse_fin(S, th, lofted):
    """the mirror: swept across to the off side, the wrists rolled over; lofted = high over the back shoulder"""
    if lofted:
        return dict(f1=through_bat(V(S) + V((-0.30, -0.10, 0.40)), th, 20, 70),
                    fin=wrap_bat(V((0.40, -0.42, 1.10)), (-0.50, -0.40, 0.77), (0.2, -0.3, 0.9)),
                    f1_body=dict(SWEEP_BODY_V2, lean=26, side=18, twist=-2, pyaw=4),
                    fin_body=dict(SWEEP_BODY_V2, lean=20, side=14, twist=-8, pyaw=0))
    return dict(f1=bat_g((0.58, -0.46, 0.52), unitv(0.10, -0.99, -0.08), unitv(-0.2, 0.0, -1.0)),
                fin=bat_g((0.44, -0.50, 0.80), unitv(-0.62, -0.55, 0.56), unitv(0.1, 0.75, 0.65)),
                f1_body=dict(SWEEP_BODY_V2, lean=30, side=20, twist=2, pyaw=8),
                fin_body=dict(SWEEP_BODY_V2, lean=26, side=16, twist=-4, pyaw=4))


REV_BL = bat_g((0.46, -0.30, 0.78), (0.15, 0.75, 0.64), (1, -0.2, 0))               # reverse backlift, out in front of the chest (round 3d)
# 23 fine sweep 215: the pilot's sweep, the face deflecting fine
sweepx_v2(23, S=(0.95, -0.28, 0.31), a0=(0.92, 0.34, -0.26), k=1.0, elev=-0.05, **sweep_fin((0.95, -0.28, 0.31), 215, False))
# 24 slog sweep 305 / 25 square slog sweep 270: face open, the finish high over the front shoulder
sweepx_v2(24, S=(0.95, -0.30, 0.34), a0=(0.92, 0.30, -0.24), elev=0.45, **sweep_fin((0.95, -0.30, 0.34), 305, True))
sweepx_v2(25, S=(0.95, -0.30, 0.34), a0=(0.92, 0.34, -0.24), elev=0.45, **sweep_fin((0.95, -0.30, 0.34), 270, True))
# 26 paddle sweep 195 / 53 lap 200: an angled face presented in front of the pad, the ball helped fine
sweepx_v2(26, S=(0.86, -0.16, 0.30), a0=(0.6, -0.35, -0.6), k=1.0, elev=0.1, look_after=V((0.92, -0.12, 0.25)))
LAP_FRONT = dict(lball=V((0.66, 0.10, 0)), ltoe=V((0.45, -1, 0)), lheel=0.0, lkp=V((0.45, -1, 0.35)))   # pad clear of the bat
sweepx_v2(53, S=(0.86, -0.16, 0.34), a0=(0.75, -0.4, -0.35), k=1.0, elev=0.35, look_after=V((0.92, -0.12, 0.30)), front=LAP_FRONT,
          f1=bat_g((0.42, -0.18, 0.70), unitv(0.6, 0.3, -0.3), unitv(-0.7, 1, 0.3)),
          fin=bat_g((0.40, -0.14, 0.76), unitv(0.4, 0.6, -0.2), unitv(-0.7, 1, 0.3)))
# 27 reverse sweep 95 / 28 fine reverse 140 / 29 lofted reverse 90 / 54 reverse lap 150
sweepx_v2(27, S=(0.95, -0.24, 0.31), a0=(0.92, -0.10, -0.30), elev=0.05, bl=REV_BL,
          body_con=dict(twist=8, pyaw=10, side=32), **reverse_fin((0.95, -0.24, 0.31), 95, False))
sweepx_v2(28, S=(0.95, -0.24, 0.31), a0=(0.92, -0.10, -0.30), k=1.0, elev=0.05, bl=REV_BL,
          body_con=dict(twist=8, pyaw=10, side=32), **reverse_fin((0.95, -0.24, 0.31), 140, False))
sweepx_v2(29, S=(0.95, -0.24, 0.34), a0=(0.92, -0.10, -0.26), elev=0.45, bl=REV_BL,
          body_con=dict(twist=8, pyaw=10, side=32), **reverse_fin((0.95, -0.24, 0.34), 90, True))
sweepx_v2(54, S=(0.84, -0.18, 0.26), a0=(0.65, 0.35, -0.55), k=1.0, elev=0.25, bl=REV_BL, body_con=dict(twist=8, pyaw=10, side=32), front=LAP_FRONT,
          look_after=V((0.90, -0.26, 0.22)))
# 55 reverse scoop 160: upright on the knee, the bat flat and face up turned to the off side
sweepx_v2(55, S=(0.80, -0.20, 0.85), a0=(0.85, 0.3, 0.05), k=1.0, elev=0.85, bl=REV_BL, hips_con=(0.12, 0.04, 0.56),
          body_con=dict(ptilt=12, lean=8, side=-2, twist=30, pyaw=25), look_after=V((-0.2, -0.6, 1.2)))
# 50 dilscoop 180: chest square to the bowler, the bat flat in front of the face, face up, lifted over the keeper
sweepx_v2(50, S=(0.72, -0.16, 0.98), a0=(0.9, -0.25, 0.08), k=1.0, elev=1.0, hips_con=(-0.02, 0.04, 0.56),
          kneel=dict(rball=V((-0.35, 0.12, 0)), rtoe=V((0.6, -1, 0))),
          front=dict(lball=V((0.60, -0.02, 0)), ltoe=V((0.9, -0.4, 0)), lheel=0.0, lkp=V((0.9, -0.4, 0.35))),
          bl=bat_g((0.10, -0.18, 0.92), (0.6, -0.7, 0.3), (0, 0, 1)),
          body_con=dict(ptilt=8, lean=-2, side=-8, twist=55, pyaw=40), look_after=V((-0.6, -0.1, 1.6)))
# 71 falling scoop 195: under the ball, falling back and away to the off side
sweepx_v2(71, S=(0.62, -0.12, 0.45), a0=(0.75, 0.3, -0.35), k=1.0, elev=0.6, hips_con=(0.05, 0.0, 0.55),
          kneel=dict(rball=V((-0.40, 0.05, 0))),
          front=dict(lball=V((0.55, -0.20, 0)), ltoe=V((0.45, -1, 0)), lheel=0.0, lkp=V((0.45, -1, 0.35))),
          body_con=dict(ptilt=6, lean=-6, side=-12, twist=30, pyaw=20), look_after=V((0.1, 0.2, 1.0)))
WAVES["sweep"] = [23, 24, 25, 26, 27, 28, 29, 50, 53, 54, 55, 71]


# ---------------------------------------------------------------- RAMPS (51, 52, 72) - ramp_v2
# Back and a little across, then down through the knees under the bounce (the pelvis carries the depth, the trunk
# stays upright), the bat raised flat in front of and above the face with the face to the sky, presented under the
# ball; the pace does the work, the bat is held, then he stands up.  The no-look ramp keeps the head down the
# pitch after contact instead of turning to watch the ball.
def ramp_v2(sid, *, S, a0=(0.85, -0.35, 0.2), hips_con=(-0.18, 0.0, 0.80), body_con=None, no_look=False, tc=30, n=90):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    S = moved(sid, S)
    con_b = bat_through(S, a0, face(th, 1.0, 0.95))
    bl = bat_g((-0.05, -0.18, 1.05), (0.2, -0.7, 0.6), (0, 0, 1))                   # the pack's ramp pick-up
    back = dict(rball=V((-0.38, -0.16, 0)), rtoe=V((0.2, -1, 0)), rheel=0.0, rkp=V((0.2, -1, 0.3)))
    front = dict(lball=V((0.02, -0.10, 0)), ltoe=V((0.6, -1, 0)), lheel=0.0, lkp=V((0.6, -1, 0.3)))
    bc = body_con or dict(ptilt=10, lean=6, side=-2, twist=20 if th > 170 else 6, pyaw=15 if th > 170 else 4)
    hx, hy, hz = hips_con
    trig = P(STANCE, hips=ST_ROOT + V((-0.02, 0, 0)), **mid_bat(STANCE, bl, 0.3), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    step = P(STANCE, **back, hips=Hroot(-0.12, 0.0, 0.98), **body(ptilt=10, lean=10, side=0, twist=2, pyaw=2),
             **mid_bat(STANCE, bl, 0.6), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(hx + 0.02, hy, hz + 0.08), **body(ptilt=10, lean=8, side=0, twist=bc["twist"] * 0.5, pyaw=bc["pyaw"] * 0.5),
             **bl, lpole=(0.9, 0.1, 0.4), rpole=(-0.5, 0.3, -0.8))
    land["look"] = look_dn(S + V((2.0, 0, 0.4)), eyes_of(land), 10.0)
    con = P(STANCE, **back, **front, hips=Hroot(hx, hy, hz), **body(**bc), **con_b, lpole=(0.6, 0.4, -0.6), rpole=(-0.2, -0.6, -0.7))
    con["look"] = look_dn(S + V((0.3, 0, 0)), eyes_of(con), 15.0)
    mid = P(blend(land, con, 0.5), **mid_bat(bl, con_b, 0.5))
    g = V(con_b["bg"])
    f1 = P(con, bg=g + V((-0.03, 0.0, 0.03)))
    fin = P(con, bg=g + V((-0.05, 0.0, 0.05)), hips=Hroot(hx, hy, hz + 0.02))
    if no_look:                                      # the head stays looking down the pitch
        f1["look"] = look_dn(V((3.0, -0.4, 0.4)), eyes_of(f1), 12.0)
        fin["look"] = look_dn(V((4.0, -0.4, 0.2)), eyes_of(fin), 12.0)
    else:                                            # turn to watch it go over the keeper / slips, the head level
        f1["look"] = look_dn(S + V((-0.5, 0.0, 0.6)), eyes_of(f1), 5.0, toward=BOWLER, k=0.2)
        fin["look"] = look_dn(S + V((-1.2, -0.2 if th < 170 else 0.0, 1.2)), eyes_of(fin), 0.0, toward=BOWLER, k=0.2)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.02)))
    rel = relax_key(Hroot(-0.14, 0.02, 0.97), -0.06, dict(back, **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (14, step), (tc - 8, land), (tc - 4, mid), (tc, con),
            (tc + 6, f1), (tc + 14, fin), (tc + 28, hold), (tc + 42, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


ramp_v2(51, S=(0.12, -0.30, 1.30))                    # ramp over the keeper 180
ramp_v2(52, S=(0.12, -0.55, 1.30))                    # ramp over the slips 155
ramp_v2(72, S=(0.12, -0.55, 1.30), no_look=True)      # no-look ramp 145
WAVES["ramp"] = [51, 52, 72]


# ---------------------------------------------------------------- SWITCH HITS (56-58) - switch_v2
# The hop-turn into a left-hander's stance ~0.5 m across to the off side, then a reworked left-hander's leg-side
# shot: the right-hander's whip/flick/glance keys (already reworked above, at the mirrored angle) mirrored
# (running_bat.mirror_y), moved across, and played 10 frames later (contact frame 41 as in the pack).  The grip
# is kept (right hand stays the bottom hand: lefty = 0, so the bat attachment never changes), as the pack states.
# He hops back into his own stance at the end.
SW_Y0 = -0.50


def shift_pose(p, dy):
    q = dict(p)
    for k_ in ("hips", "rball", "lball", "bg", "look"):
        q[k_] = V(p[k_]) + V((0, dy, 0))
    return q


def switch_v2(sid, src_mode, src_th, *, tc=40, n=120):
    # the right-hander's shot at the mirrored angle, built on a donor of the same kind (18 lofted flick / 19 leg
    # glance) as a scratch registration, then the donor's own entry is put back
    donor = 19 if src_mode == "deflect" else 18
    keep = SHOTS_V2.get(donor)
    whip_v2(donor, src_mode, th=src_th)
    src = SHOTS_V2[donor]["raw"]
    if keep is not None:
        SHOTS_V2[donor] = keep
    sw = [(t + 10, shift_pose(dict(mirror_y(p), lefty=0.0), SW_Y0)) for t, p in src if 12 < t < 89]
    sw_st = shift_pose(dict(mirror_y(READY), lefty=0.0), SW_Y0)
    trig = P(READY, hips=RD_ROOT + V((0.0, -0.04, 0.0)), look=BOWLER)
    # the hop: half-way across and a quarter turn round, facing the bowler, the feet side by side in their own lanes
    # (right foot on the off side) so they pass each other without the legs crossing, the bat held up
    hop = P(blend(READY, sw_st, 0.5), rball=V((0.0, -0.40, 0)), lball=V((0.0, -0.12, 0)), rtoe=V((1, -0.1, 0)),
            ltoe=V((1, 0.1, 0)), rkp=V((1, 0, 0.3)), lkp=V((1, 0, 0.3)), rheel=20.0, lheel=20.0, hips=Hroot(0.0, -0.26, 1.02),
            pyaw=90.0, twist=0.0, lean=0.0,
            side=0.0, ptilt=12.0, lefty=0.0, **mid_bat(READY, sw_st, 0.5), look=BOWLER)
    back = P(hop, hips=Hroot(0.0, -0.26, 1.02))
    keys = [(0, READY), (6, READY), (12, trig), (17, hop), (22, sw_st)] + sw + \
        [(99, sw_st), (106, P(back, look=BOWLER)), (112, P(READY, rheel=12.0, lheel=12.0)), (n - 1, READY)]
    register(sid, keys, tc, n)
    MOVED[sid] = (shots74.SHOTS74[sid]["sweet"], SHOTS_V2[sid]["sweet"])


switch_v2(56, "lofted", 300)          # cover 60 = the left-hander's lofted whip to 300
switch_v2(57, "lofted", 270)          # point 90 = his lofted whip square
switch_v2(58, "deflect", 220)         # third man 140 = his leg glance
WAVES["switch"] = [56, 57, 58]


# ---------------------------------------------------------------- HELICOPTER (59, 60) - heli_v2
# The yorker dug out: a short braced front stride, tall over the ball with the knees bent (not the spine), the bat
# jabbed down with a closed face at the base of the stumps, then the bottom-hand whip: the bat circles up over the
# head (the 'helicopter') toward the shot and comes round to finish over the front shoulder; the head stays level
# and lifts with the ball.
def heli_v2(sid, *, S=None, tc=30, n=100):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    con_b = bat_through(S, (-0.08, 0.04, -1), face(th, 3, 0.35))
    bl_b = bat_shift(BL_HIGH_V2, (0.08, 0.0, 0.0))
    back = dict(rball=V((-0.28, -0.04, 0)), rtoe=V((0.1, -1, 0)), rheel=6.0, rkp=V((0.2, -1, 0.3)))
    front = dict(lball=V((0.56, -0.06, 0)), ltoe=V((0.7, -0.8, 0)), lheel=0.0, lkp=V((0.7, -0.8, 0.3)))
    hc = V((0.26, 0.04, 0.80))
    bc = dict(ptilt=12, lean=12, side=30, twist=24, pyaw=16)
    trig = P(STANCE, hips=ST_ROOT + V((-0.03, 0, 0)), **mid_bat(STANCE, bl_b, 0.4), look=BOWLER,
             lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    land = P(STANCE, **back, **front, hips=Hroot(0.14, 0.04, 0.90), **body(ptilt=14, lean=12, side=8, twist=0, pyaw=0),
             **bl_b, look=BOWLER, lpole=(1, 0, 0.5), rpole=(-0.6, 0.3, -0.6))
    con = P(STANCE, **dict(back, rheel=30.0), **front, hips=Hroot(*hc), **body(**bc), **con_b,
            lpole=(1, -0.2, 0.4), rpole=(-0.3, 0.3, -1))
    con["look"] = look_dn(S, eyes_of(con), 35.0, toward=BOWLER, k=0.6)
    sw = V((1, 0, 0))
    m1 = P(blend(land, con, 0.5), **downswing(con_b, bl_b, 95, 0.45, sw))
    m2 = P(blend(land, con, 0.75), **downswing(con_b, bl_b, 45, 0.18, sw))
    r = -0.6 * (signed(th) + 15) if th != 310 else 0.0       # 310: the long-on circle (the rotated one went through him)
    lf = far(S, dirv(th), True)
    tpl = [(4, (0.28, 0.25, 0.50), (0.3, 0.8, 0.5), (0, -0.3, 1), dict(ptilt=10, lean=8, side=22, twist=34, pyaw=22)),
           (9, (0.05, 0.20, 1.10), (-0.2, 0.9, 0.4), (0.4, 0, 0.9), dict(ptilt=8, lean=4, side=12, twist=40, pyaw=26)),
           (14, (-0.16, 0.05, 1.30), (-0.6, -0.6, 0.5), (0.4, -0.3, 0.85), dict(ptilt=8, lean=2, side=6, twist=44, pyaw=30)),
           (20, (-0.24, -0.05, 1.26), (-0.3, -0.9, -0.3), (0.2, -0.2, 0.95), dict(ptilt=8, lean=2, side=6, twist=44, pyaw=30)),
           (26, (-0.24, 0.05, 1.16), (0.2, 0.6, -0.75), (0.5, 0.2, 0.8), dict(ptilt=8, lean=4, side=8, twist=40, pyaw=28))]
    fol = []
    for i, (df, off, a, nn, bd) in enumerate(tpl):
        k_ = P(con, hips=Hroot(*(hc + V((0.03, 0.02, 0.02 + 0.01 * i)))), **body(**bd), **rel_bat(S, off, a, nn, r),
               rheel=40.0 + 3 * i, lpole=(0.6, 0.4, 0.6), rpole=(-0.3, -0.5, -0.8))
        k_["look"] = look_dn(S.lerp(lf, min(1.0, 0.25 + 0.25 * i)), eyes_of(k_), 20.0 if i == 0 else 8.0)
        fol.append((tc + df, k_))
    hold = P(fol[-1][1], hips=fol[-1][1]["hips"] + V((0, 0, 0.01)))
    rel = relax_key(Hroot(0.14, 0.04, 0.94), 0.22, dict(dict(back, rheel=0.0), **dict(front, lheel=0.0)))
    keys = [(0, STANCE), (6, STANCE), (12, trig), (tc - 8, land), (tc - 4, m1), (tc - 2, m2), (tc, con)] + fol + \
        [(tc + 40, hold), (tc + 54, rel), (n - 1, STANCE)]
    register(sid, keys, tc, n)


BL_HIGH_V2 = bat_g((-0.12, -0.10, 1.14), (-0.45, -0.28, 0.85), (0.15, -1, -0.4))     # shots.BL_HIGH
from shots74 import rel_bat  # noqa: E402
heli_v2(59)                     # long-on 345
heli_v2(60)                     # midwicket 310
WAVES["heli"] = [59, 60]


# ---------------------------------------------------------------- DOWN THE TRACK (63-66) - adv_v2
# The charge (front foot out, back foot crossing behind it, the bat going up as the feet move, the head level on
# the flight), the front foot planted at the pitch of the ball 8 frames before contact, then the pilot drive's
# contact and a full lofted finish; he ends standing ~0.9 m down the pitch (contact frame 41 as in the pack).
def adv_v2(sid, *, S=None, lb=(1.20, -0.06), rb=(0.66, 0.02), hips_con=None, body_con=None, tc=40, n=120):
    old = shots74.SHOTS74[sid]
    th = old["angle"]
    S = moved(sid, S) if S is not None else pack_sweet(sid)
    o = max(-0.6, min(1.0, signed(th) / 55.0))
    op = max(0.0, o)
    con_b = bat_through(S, (-0.10, -0.06 * math.sin(math.radians(th)), -1), face(th, 3, 0.38))
    hc = V(hips_con or (S.x - 0.24 + 0.08 * op, lb[1] + 0.06 - 0.10 * op, 0.77))
    bc = body_con or dict(ptilt=10 + 2 * op, lean=12 + 8 * op, side=42, twist=30 - 44 * o, pyaw=12 - 18 * o)
    front_out = dict(lball=V((0.60, -0.03, 0)), ltoe=V((0.6, -1, 0)), lheel=0.0)
    cross = dict(rball=V((rb[0], rb[1], 0)), rtoe=V((0.3, -1, 0)), rheel=10.0)
    plant = dict(lball=V((lb[0], lb[1], 0)), ltoe=V((0.6, -1, 0)), lheel=0.0, lkp=V((0.6, -1, 0.3)))
    up = BL_HIGH_V2
    s1 = P(STANCE, **front_out, hips=Hroot(0.25, 0.04, 0.98), **body(ptilt=12, lean=10, side=4, twist=6, pyaw=2),
           **bat_shift(mid_bat(STANCE, up, 0.45), (0.25, 0, 0)), look=BOWLER, lpole=(0.9, -0.2, 0.3), rpole=(-0.6, 0.2, -0.8))
    s2 = P(s1, **cross, hips=Hroot(0.55, 0.04, 0.97), **bat_shift(mid_bat(STANCE, up, 0.8), (0.55, 0, 0)))
    land = P(s2, **plant, rheel=8.0, hips=Hroot(hc.x - 0.18, hc.y + 0.02, 0.90), **body(ptilt=14, lean=12, side=8, twist=-2, pyaw=0),
             **bat_shift(up, (hc.x - 0.24, 0, 0)), lpole=(1, 0, 0.5), rpole=(-0.6, 0.3, -0.6))
    land["look"] = look_dn(S + V((2.0, 0, 0.5)), eyes_of(land), 15.0)
    con = P(land, hips=Hroot(*hc), **body(**bc), **con_b, rheel=35.0, rkp=V((0.35, -1, 0.3)), lpole=(1, -0.2, 0.4), rpole=(-0.3, 0.3, -1))
    con["look"] = look_dn(S, eyes_of(con), 35.0, toward=BOWLER, k=0.8)
    sw = dirv(signed(th) * 0.5)
    bl_at = bat_shift(up, (hc.x - 0.24, 0, 0))
    m1 = P(blend(land, con, 0.5), **downswing(con_b, bl_at, 95, 0.45, sw))
    m2 = P(blend(land, con, 0.75), **downswing(con_b, bl_at, 45, 0.18, sw))
    lf = far(S, dirv(th), True)
    f1 = P(con, hips=Hroot(*(hc + V((0.03, 0.01, 0.02)))), **body(**badd(bc, ptilt=-2, lean=-4, side=-6, twist=10, pyaw=8)),
           **through_bat(S + V((0.22, 0.08, 0.80)), signed(th) * 0.9, 25, 70), rheel=45.0, lpole=(0.8, 0.0, 0.8), rpole=(-0.3, -0.4, -0.8))
    f1["look"] = look_dn(S.lerp(lf, 0.3), eyes_of(f1), 18.0)
    fin = P(con, hips=Hroot(*(hc + V((0.04, 0.02, 0.03)))), **body(**badd(bc, ptilt=-4, lean=-8, side=-16, twist=20 + 10 * op, pyaw=14)),
            **finish_bat(S + V((0.14, 0.12, 1.28)), th, 100), rheel=55.0, lpole=(0.8, -0.3, 0.25), rpole=(-0.3, -0.6, -0.6))
    fin["look"] = look_dn(lf, eyes_of(fin), 10.0)
    hold = P(fin, hips=fin["hips"] + V((0, 0, 0.01)))
    sx = (lb[0] + rb[0]) / 2
    endp = P(STANCE, lball=V((sx + 0.18, -0.03, 0)), rball=V((sx - 0.18, -0.02, 0)), ltoe=V((0.5, -1, 0)), rtoe=V((0.2, -1, 0)),
             lheel=0.0, rheel=0.0, hips=Hroot(sx - 0.02, 0.04, 0.99), **body(ptilt=8, lean=8, side=0, twist=10, pyaw=8),
             **bat_g(RD_HANDS + V((sx - 0.02 - RD_ROOT.x, 0, -0.01)), READY["ba"], READY["bn"]), look=BOWLER,   # ready carry
             lkp=V((0.5, -1, 0.25)), rkp=V((0.2, -1, 0.25)), rpole=READY["rpole"], lpole=READY["lpole"])
    rel = P(blend(hold, endp, 0.5), **bat_g(RD_HANDS + V((sx + 0.04 - RD_ROOT.x, 0, -0.02)), READY["ba"], READY["bn"]), look=BOWLER)
    keys = [(0, STANCE), (6, STANCE), (12, P(STANCE, **mid_bat(STANCE, up, 0.25), hips=ST_ROOT + V((0.04, 0, 0)))),
            (16, s1), (24, s2), (tc - 8, land), (tc - 4, m1), (tc - 2, m2), (tc, con), (tc + 4, f1), (tc + 9, fin),
            (tc + 28, hold), (tc + 52, rel), (n - 1, endp)]
    register(sid, keys, tc, n)


adv_v2(63)                                                  # straight loft 0
adv_v2(64)                                                  # long-on loft 345
adv_v2(65, hips_con=(0.94, 0.03, 0.77))                                              # long-off loft 15
adv_v2(66, lb=(1.12, 0.16), rb=(0.66, 0.22))                # inside-out over cover 40
WAVES["adv"] = [63, 64, 65, 66]



# ================================================================= ROUND 4: edge / miss clips (78-81) and pre-delivery footwork (82-84)
# Built on the same families (their keys, fits and stance), registered under new ids with pseudo pack entries so the
# file names, sidecars, lhb mirrors and bat tracks come out of the same pipeline.  Each sidecar carries its event
# frame (contact_frame) and the event points the game puts the ball on (batting frame, lhb mirrored).
def pseudo(sid, base, name, angle, family, sweet, extra, eyes, hands, upper, feet, ball="", lofted=False):
    e = dict(shots74.SHOTS74[base])
    e.update(name=name, angle=angle, lofted=lofted, sweet=list(sweet),
             info=dict(family=family, ball=ball, eyes=eyes, hands=hands, upper_body=upper, feet=feet, extra=extra))
    shots74.SHOTS74[sid] = e


# 78 play and miss outside off: a cover drive that goes inside the line; the ball passes 16 cm outside the edge
PM_S = (0.60, -0.44, 0.22)
pseudo(78, 5, "Play and miss (off)", 55, "edge / miss", PM_S,
       dict(event="ball passes the outside edge at contact_frame", ball_point_at_event_m=(0.58, -0.60, 0.26)),
       "Eyes on the ball all the way; head over it as it beats the bat.", "Drives inside the line, the bat goes past the ball.",
       "Front shoulder leads, reaches a little.", "Front foot strides toward the line.", ball="Full, outside off, leaving the bat")
drive_v2(78, "ground", S=PM_S)
# 79 inside edge onto the pad: an off drive, the ball takes the inside (leg-side) edge and goes into the front pad
IE_S = (0.62, -0.30, 0.20)
pseudo(79, 4, "Inside edge onto pad", 15, "edge / miss", IE_S,
       dict(event="inside edge at contact_frame, then the front pad", edge_point_m=(0.62, -0.245, 0.20),
            pad_point_m=(0.58, -0.16, 0.32), pad_frame=33),
       "Eyes on the ball onto the bat.", "Drives through the line; the ball clips the inside edge, the swing is checked.",
       "Front shoulder leads.", "Front foot strides to the pitch of the ball.", ball="Full, nipping back in")
drive_v2(79, "chip", S=IE_S)
# 80 bat-pad pop: a forward prod with bat and pad together; the ball pops up off both
BP_S = (0.62, -0.20, 0.26)
pseudo(80, 1, "Bat-pad pop", 330, "edge / miss", BP_S,
       dict(event="ball hits bat and pad together at contact_frame and pops up", pop_point_m=(0.66, -0.14, 0.34),
            pop_direction_deg=330, pop_elevation_deg=55),
       "Eyes down on the ball onto bat and pad.", "Soft, dead bat pushed out beside the front pad.",
       "Head over the front knee.", "Front foot forward, pad beside the bat.", ball="Full, spinning in")
drive_v2(80, "dead", k=2.0, S=BP_S, stride_d=(0, 0.06))
# 81 chopped on: an attempted square cut, the ball dragged off the bottom edge onto the stumps
CO_S = (0.24, -0.60, 0.60)
pseudo(81, 37, "Chopped on", 90, "edge / miss", CO_S,
       dict(event="bottom edge at contact_frame, then the stumps", edge_point_m=(0.24, -0.60, 0.555),
            stumps_m=(-1.22, -0.28, 0.36)),
       "Eyes on the ball wide of off stump.", "Cuts down on a ball too close to him; it takes the bottom edge.",
       "Weight back and across, arms extended.", "Back foot back and across.", ball="Short of length, too close to cut")
cut_v2(81, "cut", S=CO_S)


def moved_ready(d, **kw):
    q = dict(READY)
    for k_ in ("hips", "rball", "lball", "bg"):
        q[k_] = V(READY[k_]) + V(d)
    q.update({k_: (V(v) if isinstance(v, tuple) else v) for k_, v in kw.items()})
    return q


def foot_v2(sid, name, travel, steps, tc, n, note, feet):
    """a move before the ball: from the ready stance through `steps` [(t, hips offset, feet...)] into the ready stance
    moved by `travel`, held from tc (the frame he is ready to hit from)"""
    pseudo(sid, 2, name, 0, "footwork", tuple(V(READY["bg"]) + V(travel)),
           dict(travel_m=tuple(travel), duration_s=round((tc + 1) / 30.0, 2), ready_frame=tc + 1,
                event="ready to hit from ready_frame; the shot clips blend from here offset by travel_m"),
           "Eyes on the bowler throughout, head level.", "Bat stays up in the ready position.", note, feet)
    keys = [(0, READY), (4, READY)]
    for t, hd, kw in steps:
        keys.append((t, moved_ready(hd, **kw)))
    end = moved_ready(travel)
    keys += [(tc, end), (n - 1, end)]
    register(sid, keys, tc, n)


foot_v2(82, "Charge down the track", (0.95, 0.02, 0.0),
        [(7, (0.22, 0.0, -0.01), dict(lball=(0.62, -0.03, 0.0))),
         (13, (0.50, 0.02, 0.0), dict(lball=(0.62, -0.03, 0.0), rball=(0.58, 0.07, 0.0))),
         (19, (0.82, 0.02, -0.01), dict(lball=(1.19, -0.03, 0.0), rball=(0.58, 0.07, 0.0)))],
        24, 36, "Stays side-on through the charge, then settles balanced.",
        "Three quick steps: front foot out, back foot crosses behind it, front foot plants ~1 m down the pitch.")
foot_v2(83, "Move across off", (-0.04, -0.20, 0.0),
        [(6, (-0.03, -0.10, 0.0), dict(rball=(-0.30, -0.24, 0.0))),
         (11, (-0.04, -0.18, 0.0), dict(rball=(-0.30, -0.24, 0.0), lball=(0.20, -0.23, 0.0)))],
        15, 26, "Stays side-on; head moves across over off stump.", "Back foot back and across, front foot follows: a shuffle toward off stump.")
foot_v2(84, "Move to leg side", (-0.02, 0.28, 0.0),
        [(6, (-0.01, 0.12, 0.0), dict(rball=(-0.26, 0.25, 0.0))),
         (11, (-0.02, 0.24, 0.0), dict(rball=(-0.26, 0.25, 0.0), lball=(0.22, 0.25, 0.0)))],
        15, 26, "Opens up a touch to free the arms toward the off side.", "Back foot steps away to leg, front foot follows: room to hit through the off side.")
WAVES["extra"] = [78, 79, 80, 81, 82, 83, 84]


PILOT = [1, 2, 5, 22, 32, 42]
CHECKPOINTS = {}
SUMMARY = []


# ================================================================= generation, checks, metadata
def stem_of(sid, hand):
    name = "00_stance_idle_loop" if sid == 0 else f"{sid:02d}_{slug(SHOTS_V2[sid]['name'])}"
    return ("lhb_" + name) if hand == "lhb" else name


def clip_of(sid, hand):
    if sid == 0:
        n, keys, contact, meta = STANCE_V2
    else:
        sh = SHOTS_V2[sid]
        n, keys, contact, meta = sh["n"], sh["keys"], sh["contact"], dict(loop=False)
    if hand == "lhb":
        keys = [(t, mirror_y(k)) for t, k in keys]
    return n, keys, contact, meta


def body_metrics(Pos, off_sign):
    """the reviewer's numbers from one solved frame (joint heads in world metres)"""
    def H(n):
        return V(Pos[PFX + n])

    def ang(p, q):
        return math.degrees(p.angle(q)) if p.length > 1e-9 and q.length > 1e-9 else 0.0

    hips, neck, head, top = H("Hips"), H("Neck"), H("Head"), H("HeadTop_End")
    lsh, rsh, lh, rh = H("LeftArm"), H("RightArm"), H("LeftHand"), H("RightHand")
    up = V((0, 0, 1))
    trunk = (neck - hips).normalized()
    shn = (lsh - rsh).normalized()
    chest = shn.cross(up).normalized()
    hu = (top - head).normalized()
    out = dict(hip_z=hips.z,
               trunk_bowler=math.degrees(math.asin(max(-1, min(1, trunk.x)))),
               trunk_off=math.degrees(math.asin(max(-1, min(1, off_sign * trunk.y)))),
               trunk_total=ang(trunk, up),
               chest_open=math.degrees(math.atan2(chest.x, off_sign * chest.y)),
               head_incl=ang(hu, up) - 18.7,                       # the Head bone leans 18.7 deg forward in rest
               head_az=math.degrees(math.atan2(off_sign * hu.y, hu.x)))
    for side, tag in (("Left", "L"), ("Right", "R")):
        out["knee_" + tag] = ang(H(side + "Leg") - H(side + "UpLeg"), H(side + "Foot") - H(side + "Leg"))
        out["toe_z_" + tag] = H(side + "ToeBase").z
        out["knee_z_" + tag] = H(side + "Leg").z
    out["feet_apart"] = (H("LeftToeBase") - H("RightToeBase")).length
    hc = (lh + rh) / 2
    out["hands_z"] = hc.z
    out["hands_dx_hips"] = hc.x - hips.x
    out["hands_to_hip"] = min((h - j).length for h in (lh, rh) for j in (H("LeftUpLeg"), H("RightUpLeg"), hips))
    out["hands_to_head"] = min((h - head).length for h in (lh, rh))
    for tag, j in (("rsh", rsh), ("lsh", lsh), ("rhand", rh), ("lhand", lh)):
        out[tag + "_x"], out[tag + "_y"], out[tag + "_z"] = j.x, j.y, j.z
    out["elbow_L"] = 180 - ang(H("LeftForeArm") - lsh, lh - H("LeftForeArm"))
    out["elbow_R"] = 180 - ang(H("RightForeArm") - rsh, rh - H("RightForeArm"))
    return out


def field_az(v, lhb=False):
    """azimuth in the angle_deg convention: 0 = at the bowler, 90 = off side (point), 270 = leg side"""
    y = v.y if lhb else -v.y
    return math.degrees(math.atan2(y, v.x)) % 360


CP_COLS = ("blade_down", "toe_trail", "face_az", "face_up", "bh_z", "bh_reach", "hip_clr", "head_clr", "head_nod",
           "look_az", "eye_tilt", "topsh_elev", "topelb_dz", "chest_open", "knee_L", "knee_R")


def cp_row(p, J, M, lhb):
    """one checkpoint row: the bat from the authored pose p (what the game's bat track is), the body from the
    solved joints J(name) -> world V, M = the Head bone's pose rotation (rig space)"""
    C = batting.C
    bot, top = ("Left", "Right") if lhb else ("Right", "Left")
    off = 1.0 if lhb else -1.0
    a = V(p["ba"]).normalized()
    n = ortho(p["bn"], a)
    look = (C.transposed() @ (M @ (C @ V((0, -1, 0))))).normalized()
    lat = (C.transposed() @ (M @ (C @ V((1, 0, 0))))).normalized()
    hips = J("Hips")
    bh = J(bot + "Hand")
    hands = (J("LeftHand"), J("RightHand"))
    sh, el = J(top + "Arm"), J(top + "ForeArm")
    down = (hips - J("Neck")).normalized()
    shn = (J("LeftArm") - J("RightArm")).normalized()
    chest = shn.cross(V((0, 0, 1))).normalized()

    def ang(p_, q_):
        return math.degrees(p_.angle(q_)) if p_.length > 1e-9 and q_.length > 1e-9 else 0.0

    knee = {s: ang(J(s + "Leg") - J(s + "UpLeg"), J(s + "Foot") - J(s + "Leg")) for s in ("Left", "Right")}
    return dict(blade_down=math.degrees(math.asin(max(-1, min(1, -a.z)))),
                toe_trail=math.degrees(math.atan2(-a.x, -a.z)) if a.z < -0.3 else float("nan"),
                face_az=field_az(n, lhb), face_up=math.degrees(math.asin(max(-1, min(1, n.z)))),
                bh_z=bh.z, bh_reach=(V((bh.x, bh.y, 0)) - V((hips.x, hips.y, 0))).length,
                hip_clr=min((h - j).length for h in hands for j in (J("LeftUpLeg"), J("RightUpLeg"), hips)),
                head_clr=min((h - J("Head")).length for h in hands),
                head_nod=math.degrees(math.asin(max(-1, min(1, -look.z)))), look_az=field_az(look, lhb),
                eye_tilt=math.degrees(math.asin(max(-1, min(1, lat.z)))),
                topsh_elev=math.degrees((el - sh).angle(down)), topelb_dz=el.z - sh.z,
                chest_open=math.degrees(math.atan2(chest.x, off * chest.y)),
                knee_L=knee["Left"], knee_R=knee["Right"])


def head_matrix(arm):
    pb = arm.pose.bones[PFX + "Head"]
    return pb.matrix.to_3x3() @ pb.bone.matrix_local.to_3x3().inverted()


def checkpoints(keys, frames, fr_list, lhb=False):
    """the in-game checkpoints from the exported clip (fr_list is 1-based)"""
    import bpy
    arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    rows = []
    for f1 in fr_list:
        f = f1 - 1
        Pos = frames[f][0]
        bpy.context.scene.frame_set(f)
        rows.append((f1, cp_row(sample(keys, f), lambda nm: V(Pos[PFX + nm]), head_matrix(arm), lhb)))
    return rows


def checkpoints_solved(rig, arm, poses, fr_list, lhb=False):
    """the same checkpoints in --check mode: each frame solved as authored (no reach assist)"""
    import bpy
    rows = []
    for f1 in fr_list:
        f = f1 - 1
        Pos, _, _ = rig.solve(poses[f], {}, write=True)
        bpy.context.view_layer.update()
        W = {k: batting.C.transposed() @ v / 100 for k, v in Pos.items()}
        rows.append((f1, cp_row(poses[f], lambda nm: W[PFX + nm], head_matrix(arm), lhb)))
    return rows


def print_checkpoints(rows, label):
    print(f"  checkpoints {label}: " + " ".join(f"{c:>10s}" for c in CP_COLS), flush=True)
    for f1, r in rows:
        print(f"  {'':12s}f{f1:<3d}" + " ".join(f"{r[c]:10.2f}" for c in CP_COLS), flush=True)


def print_metrics(rows, label):
    keys = list(rows[0][1].keys())
    print(f"  {label}: {'metric':>14s}" + "".join(f"{'f' + str(f):>8s}" for f, _ in rows), flush=True)
    for k in keys:
        print(f"  {'':{len(label)}s}  {k:>14s}" + "".join(f"{m[k]:8.2f}" for _, m in rows), flush=True)


def check_frames(sid, hand, rig, poses, n, tc):
    """where make_bat's reach assist would have to work: either hand beyond 98% of the arm"""
    bad = []
    pv = {}
    for f in range(n):
        r = rig.reach_vec(poses[f], pv)
        d = max(V(r[0:3]).length, V(r[3:6]).length)
        if d > 0.005:
            bad.append((f + 1, round(d, 3)))
    worst = max(bad, key=lambda b: b[1]) if bad else (0, 0.0)
    if bad:
        print(f"  reach: {len(bad)} frames over by >5 mm, worst {worst[1]} m at f{worst[0]}: "
              + " ".join(f"f{f}:{d}" for f, d in bad[:12]) + (" ..." if len(bad) > 12 else ""), flush=True)
    else:
        print("  reach: every frame within the arms (no assist needed)", flush=True)
    feet = []
    for f in range(n):
        _, _, err = rig.solve(poses[f], {}, write=False)
        e = max(err.get("RightFoot", 0.0), err.get("LeftFoot", 0.0))
        if e > 0.01:
            feet.append((f + 1, round(e, 3)))
    if feet:
        wf = max(feet, key=lambda b: b[1])
        print(f"  legs: {len(feet)} frames with a foot beyond the leg by >1 cm, worst {wf[1]} m at f{wf[0]}", flush=True)
    return worst


def cp_frames(tc):
    return [tc - 8 + 1, tc + 1, tc + 5, tc + 10]


def sidecar(sid, hand, n, keys, tc, stem):
    off_sign = 1.0 if hand == "lhb" else -1.0
    if sid == 0:
        e = dict(id=0, shot="Stance idle (loop)", file=stem + ".fbx")
        if hand == "lhb":
            e["batter"] = "left-handed"
        e.update(frames=n, loop=True)
        return e
    sh = SHOTS_V2[sid]
    S = sweet_of(dict(keys)[tc])
    e = dict(id=sid, shot=sh["name"], file=stem + ".fbx")
    if hand == "lhb":
        e["batter"] = "left-handed"
        e["field_angle_deg"] = (360 - sh["angle"]) % 360
    e.update(ball=sh["info"].get("ball", ""), angle_deg=sh["angle"], family=sh["info"]["family"], frames=n, seconds=round(n / 30, 2),
             contact_frame=tc + 1, lofted=sh["lofted"],
             sweet_spot_at_contact_m=dict(toward_bowler=round(S.x, 3), toward_off_side=round(off_sign * S.y, 3), up=round(S.z, 3)),
             eyes=sh["info"]["eyes"], hands=sh["info"]["hands"], upper_body=sh["info"]["upper_body"], feet=sh["info"]["feet"])
    for k in ("ends_at_x",):
        if k in sh["info"]:
            e[k] = sh["info"][k]
    for k, v in sh["info"].get("extra", {}).items():           # round 4: event points / travel for the new clips
        if isinstance(v, (list, tuple)) and len(v) == 3:          # a right-hander's point/vector: off side = -y for both hands
            v = dict(toward_bowler=round(v[0], 3), toward_off_side=round(-v[1], 3), up=round(v[2], 3))
        e[k] = v
    return e


def dump(stem, frames_xyz, tc=None):
    """frames_xyz: [(joints {name: (head, tail)}, (top, toe, sweet))] -> work/<stem>.v2dump.json for the bat sheet"""
    out = os.path.join(batting.APL_WORK, stem + ".v2dump.json")
    with open(out, "w") as fh:
        json.dump(dict(file=stem, contact=tc, frames=[dict(j={k[len(PFX):]: [list(map(lambda x: round(x, 4), h)), list(map(lambda x: round(x, 4), t))]
                                                  for k, (h, t) in jt.items()},
                                               bat=[list(map(lambda x: round(x, 4), q)) for q in bat]) for jt, bat in frames_xyz]), fh)
    print(f"  dump -> {out}", flush=True)


def run(sid, hand, check_only):
    if sid != 0 and hand == "rhb" and bat_clear.wanted():
        # round 9: (re)compute the bat's clearance from the body on the un-cleared keys, then use it
        sh = SHOTS_V2[sid]
        bat_clear.compute(batting.BatRig(batting.load_rig()[0]), sh["keys_unclear"], sh["n"], sh["contact"], clear_name(sid))
        sh["keys"] = bat_clear.apply(clear_name(sid), sh["keys_unclear"], sh["n"])
    n, keys, tc, meta = clip_of(sid, hand)
    stem = stem_of(sid, hand)
    lhb = hand == "lhb"
    off_sign = 1.0 if lhb else -1.0
    arm, _ = batting.load_rig()
    rig = batting.BatRig(arm)
    poses = []
    for f in range(n):
        p = sample(keys, f)
        batting.step_lift(keys, f, p)
        poses.append(p)
    print(f"{stem}:", flush=True)
    worst = check_frames(sid, hand, rig, poses, n, tc)
    if check_only:
        if tc is not None:
            cps = checkpoints_solved(rig, arm, poses, cp_frames(tc), lhb)
            print_checkpoints(cps, "(authored)")
            CHECKPOINTS[stem] = cps
            summarize(sid, stem, worst, cps, None)
        if os.environ.get("APL_V2_METRICS"):
            rows = []
            for f in moments(n, tc):
                Pos, _, _ = rig.solve(poses[f], {}, write=False)
                rows.append((f + 1, body_metrics({k: tuple(batting.C.transposed() @ v / 100) for k, v in Pos.items()}, off_sign)))
            print_metrics(rows, "authored")
        if os.environ.get("APL_V2_DUMP"):
            fx = []
            for f in range(n):
                Pos, tails, _ = rig.solve(poses[f], {}, write=False)
                a = V(poses[f]["ba"]).normalized()
                fx.append(({k: (tuple(batting.C.transposed() @ v / 100), tuple(batting.C.transposed() @ tails[k] / 100)) for k, v in Pos.items()},
                           tuple(tuple(q) for q in batting.bat_points(V(poses[f]["bg"]), a))))
            dump(stem, fx, tc)
        return
    frames, worst_s, md = batting.make_bat(stem, n, keys, tc, meta)
    bad = {f"{k}@f{worst_s.get(k + '@', -1) + 1}": round(v, 3) for k, v in worst_s.items() if not k.endswith("@") and v > 0.012}
    print(f"  solver: {'grip/reach OK' if not bad else 'worst ' + str(bad)}", flush=True)
    if os.environ.get("APL_V2_METRICS"):
        print_metrics([(f + 1, body_metrics(frames[f][0], off_sign)) for f in moments(n, tc)], "solved")
    if tc is not None:
        cps = checkpoints(keys, frames, cp_frames(tc), lhb)
        print_checkpoints(cps, "")
        CHECKPOINTS[stem] = cps
        summarize(sid, stem, worst, cps, bad)
    toe_z = [fr[2][1][2] for fr in frames]
    print(f"  bat toe z: min {min(toe_z):.3f} (f{toe_z.index(min(toe_z)) + 1}) max {max(toe_z):.3f}", flush=True)
    if meta.get("loop"):
        d = max(abs(a_ - b_) for nm in frames[0][0] for a_, b_ in zip(frames[0][0][nm], frames[-1][0][nm]))
        print(f"  loop: first/last frame max bone difference {d * 1000:.2f} mm", flush=True)
    if tc is not None:
        S = sweet_of(dict(keys)[tc])
        print(f"  sweet spot at contact (keys) ({S.x:+.3f},{S.y:+.3f},{S.z:+.3f}); solver's {md.get('sweet_spot_at_contact')}", flush=True)
    with open(os.path.join(batting.OUT, stem + ".json"), "w") as fh:
        json.dump([sidecar(sid, hand, n, keys, tc, stem)], fh, indent=1)
    if os.environ.get("APL_V2_DUMP"):
        dump(stem, [(dict((k, (fr[0][k], fr[1][k])) for k in fr[0]), fr[2]) for fr in frames], tc)
    print(f"  -> {os.path.join(batting.OUT, stem + '.fbx')}", flush=True)


def moments(n, tc):
    return [0, tc - 8, tc, tc + 6, tc + 14, n - 1] if tc is not None else [0, n // 4, n // 2, 3 * n // 4, n - 1]


def summarize(sid, stem, worst, cps, bad):
    d = dict(cps)
    tc1 = SHOTS_V2[sid]["contact"] + 1
    c, f = d[tc1], d[tc1 + 9]
    SUMMARY.append((stem, SHOTS_V2[sid]["angle"], worst, c, f, bad))


def print_summary():
    if not SUMMARY:
        return
    done = {int(st.split("_")[1]) if st.startswith("lhb_") else int(st.split("_")[0]) for st, *_ in SUMMARY}
    for sid in sorted(done):
        if sid in MOVED:
            print(f"MOVED sweet spot {sid:02d}: pack {MOVED[sid][0]} -> v2 {MOVED[sid][1]}", flush=True)
    print("\nSUMMARY  (c = contact frame, e = contact+9; reach = worst authored shortfall over the clip)", flush=True)
    print(f"  {'clip':36s} {'ang':>4s} {'reach':>12s} | {'c.face':>6s} {'c.bld':>6s} {'c.bhz':>5s} {'c.rch':>5s} {'c.hip':>5s} "
          f"{'c.hd':>5s} {'c.nod':>5s} {'c.tlt':>5s} {'c.chst':>6s} | {'e.bld':>6s} {'e.bhz':>5s} {'e.hip':>5s} {'e.hd':>5s} {'e.tlt':>5s} {'e.chst':>6s}", flush=True)
    for stem, ang, (wf, wd), c, e, bad in SUMMARY:
        print(f"  {stem:36s} {ang:4d} {wd:6.3f}@f{wf:<4d} | {c['face_az']:6.1f} {c['blade_down']:6.1f} {c['bh_z']:5.2f} {c['bh_reach']:5.2f} "
              f"{c['hip_clr']:5.2f} {c['head_clr']:5.2f} {c['head_nod']:5.1f} {c['eye_tilt']:5.1f} {c['chest_open']:6.1f} | "
              f"{e['blade_down']:6.1f} {e['bh_z']:5.2f} {e['hip_clr']:5.2f} {e['head_clr']:5.2f} {e['eye_tilt']:5.1f} {e['chest_open']:6.1f}"
              + (f"  solver {bad}" if bad else ""), flush=True)


def overs(rig, pose, margin):
    """per hand, the vector by which the grip target lies beyond (98% of the arm - margin) from the shoulder"""
    Pos, _, _ = rig.solve(pose, {}, write=False)
    a = V(pose["ba"]).normalized()
    n = ortho(pose["bn"], a)
    e = a.cross(n).normalized()
    lefty = pose.get("lefty", 0.0) > 0.5
    f = ((-e if lefty else e) + a * 0.35).normalized()
    top = V(pose["bg"]) - a * GRIP_R
    BOT, TOP = ("Left", "Right") if lefty else ("Right", "Left")
    tg = {BOT: top + a * GRIP_R - f * 0.075 - n * 0.03, TOP: top + a * batting.GRIP_L - f * 0.075 + n * 0.03}
    out = []
    for side in ("Right", "Left"):
        sh = batting.C.transposed() @ Pos[PFX + side + "Arm"] / 100
        v = tg[side] - sh
        over = v.length - ((rig.L1 + rig.L2) / 100 * 0.98 - margin)
        out.append(v.normalized() * over if over > 0 else V((0, 0, 0)))
    return out


def fit(ids):
    """nudge each roll-out key so the hands reach: the contact key moves the hips (the sweet spot stays), every
    other key moves the bat grip toward the shoulders; offsets accumulate in batting_v2_fit.json"""
    import numpy as np
    arm, _ = batting.load_rig()
    rig = batting.BatRig(arm)
    for sid in ids:
        if sid == 0:
            continue
        sh = SHOTS_V2[sid]
        tc, n = sh["contact"], sh["n"]
        fx = FIT.setdefault(sid, {})
        for t, p in sh["keys"]:
            if t in (0, 6, n - 1):
                continue
            if sid in PILOT and tc - 8 <= t <= tc + 28:          # the approved pilot swings are left exactly as they are
                continue
            d = fx.setdefault(t, {})
            if t == tc:
                x = np.zeros(3)
                for it in range(12):
                    def res(x_):
                        q = dict(p, hips=V(p["hips"]) + V(tuple(x_)))
                        o = overs(rig, q, 0.012)
                        return np.array([*o[0], *o[1]])
                    r = res(x)
                    if np.linalg.norm(r) < 0.002:
                        break
                    J = np.zeros((6, 3))
                    for j in range(3):
                        xd = x.copy()
                        xd[j] += 0.01
                        J[:, j] = (res(xd) - r) / 0.01
                    dx = np.linalg.solve(J.T @ J + np.diag([0.05, 0.05, 0.25]), -J.T @ r)     # depth is the last resort
                    x = np.clip(x + dx, -0.15, 0.15)
                if np.linalg.norm(x) > 0.002:
                    d["hips"] = [round(float(a_ + b_), 4) for a_, b_ in zip(d.get("hips", [0, 0, 0]), x)]
                    print(f"  fit {sid} f{t + 1}: hips {tuple(round(float(v), 3) for v in x)}  left over {np.linalg.norm(res(x)):.3f}", flush=True)
            else:
                sh_ = V((0, 0, 0))
                for it in range(8):
                    o = overs(rig, dict(p, bg=V(p["bg"]) + sh_), 0.015)
                    dv = o[0] + o[1]
                    if dv.length < 0.002:
                        break
                    sh_ -= dv
                if sh_.length > 0.002:
                    d["bg"] = [round(a_ + b_, 4) for a_, b_ in zip(d.get("bg", [0, 0, 0]), sh_)]
                    print(f"  fit {sid} f{t + 1}: bat {tuple(round(v, 3) for v in sh_)}", flush=True)
            if not d:
                del fx[t]
        # then the in-between frames: insert a key at the worst frame still over (up to 6 per shot)
        for k_ in range(6):
            keys = apply_fit(sid, sh["raw"])
            kt = {t for t, _ in keys}
            worst = (0.0, None)
            for f in range(n):
                if f in kt or f < 8 or (sid in PILOT and tc - 8 <= f <= tc + 28):
                    continue
                q = sample(keys, f)
                batting.step_lift(keys, f, q)
                o = overs(rig, q, 0.0)
                w = max(o[0].length, o[1].length)
                if w > max(worst[0], 0.008):
                    worst = (w, f)
            if worst[1] is None:
                break
            f = worst[1]
            q = sample(keys, f)
            sh_ = V((0, 0, 0))
            for it in range(8):
                o = overs(rig, dict(q, bg=V(q["bg"]) + sh_), 0.012)
                dv = o[0] + o[1]
                if dv.length < 0.002:
                    break
                sh_ -= dv
            fx[f] = dict(ins=1, bg=[round(v, 4) for v in sh_])
            print(f"  fit {sid} f{f + 1}: inserted key, bat {tuple(round(v, 3) for v in sh_)} (was over {worst[0]:.3f})", flush=True)
        if not fx:
            del FIT[sid]
    with open(FIT_PATH, "w") as fh:
        json.dump({str(k): {str(t): d for t, d in sorted(v.items())} for k, v in sorted(FIT.items())}, fh, indent=1)
    print(f"fit -> {FIT_PATH}", flush=True)


def select(args):
    ids = []
    for a in args or ["all"]:
        if a == "all":
            ids += [0] + sorted(SHOTS_V2)
        elif a == "stance":
            ids.append(0)
        elif a == "pilot":
            ids += PILOT
        elif a in WAVES:
            ids += WAVES[a]
        else:
            ids.append(int(a))
    return ids


if __name__ == "__main__":
    args = sys.argv[1:]
    check_only = "--check" in args
    do_fit = "--fit" in args
    unfit = "--unfit" in args
    args = [a for a in args if a not in ("--check", "--fit", "--unfit")]
    hands = [a for a in args if a in ("rhb", "lhb", "both")] or ["both"]
    hands = ["rhb", "lhb"] if "both" in hands else hands
    ids = select([a for a in args if a not in ("rhb", "lhb", "both")])
    if unfit:
        for sid in ids:
            FIT.pop(sid, None)
        with open(FIT_PATH, "w") as fh:
            json.dump({str(k): {str(t): d for t, d in sorted(v.items())} for k, v in sorted(FIT.items())}, fh, indent=1)
        print("unfit", ids)
        sys.exit(0)
    if do_fit:
        fit(ids)
        sys.exit(0)
    for hand in hands:
        _d = os.environ.get("APL_V2_DIR", "batting_v2")                    # round 6: batting_mocap for the full set
        batting.OUT = os.path.join(batting.APL_OUT, _d if hand == "rhb" else _d + "_lh")
        os.makedirs(batting.OUT, exist_ok=True)
        for sid in ids:
            run(sid, hand, check_only)
    print_summary()
