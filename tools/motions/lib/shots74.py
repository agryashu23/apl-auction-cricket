"""74 batting shots from cricket_batting_shots-2.txt. Right-handed batter.
World: +X toward bowler, -Y off side (chest faces it in stance), +Y leg side, Z up. Origin between the feet at the crease."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, re; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from shots import (STANCE, BOWLER, RELAX, BL_HIGH, BL_LOW, mid_bat, blend, unit, far)
from batting import *

OUT74 = _os.path.join(APL_OUT, "batting74"); os.makedirs(OUT74, exist_ok=True)
import batting; batting.OUT = OUT74

# ---------------------------------------------------------------- helpers
def dirv(th): r = math.radians(th); return V((math.cos(r), -math.sin(r), 0))
def signed(th): return th - 360 if th > 180 else th
def face(th, k, elev):
    """bat-face normal that sends a ball arriving from the bowler toward angle th (k: 1 = pure deflection, 3 = powered)"""
    n = dirv(th)*k + V((1,0,0)); n = V((n.x, n.y, 0)).normalized(); n.z = elev; return n.normalized()
def rotz(v, deg): return R('Z', deg) @ V(v)
def rel_bat(S, off, a, n, rot=0.0):
    """bat placed relative to contact point S, the whole template rotated about Z by rot degrees"""
    return bat_g(V(S) + rotz(off, rot), rotz(a, rot), rotz(n, rot))
def fix_axis(a, n, S=None):
    """rotate the authored bat axis as little as possible so it is square to the required face"""
    a = V(a).normalized(); n = V(n).normalized()
    ap = a - n*a.dot(n)
    return ap.normalized() if ap.length > 0.2 else a
def bat_c(S, a, n): return bat_s(S, fix_axis(a, n, S), n)
LINE = dict(stumps=-0.28, off=-0.39, outside_off=-0.56, wide=-0.72, middle_leg=-0.22, pads=-0.10, leg=-0.02, body=-0.12, hips=-0.08)
def clamp(x, a, b): return max(a, min(b, x))

# ---------------------------------------------------------------- generic timeline builder
SHOTS74 = {}
def shot(sid, name, angle, *, feet_seq, hips_c, body_c, backlift, contact, follows, look_after, lofted, info,
         tc=30, n=90, back_extra=None, end_pose=None, look_override=None, look_hold=None):
    """feet_seq: list of (frame, feet-dict) applied cumulatively before contact
       follows: list of (frame offset after contact, bat dict, body dict)"""
    S = contact["bg"] + contact["ba"]*(SWEET-GRIP_R)
    keys = [(0, STANCE), (6, STANCE)]
    cur = dict(STANCE)
    bl_frame = tc - 8
    # footwork + backlift build-up
    for fr, fd in feet_seq:
        t = clamp((fr - 6) / max(1, bl_frame - 6), 0, 1)
        hip = STANCE["hips"].lerp(V(hips_c), fd.pop("_hip", t*0.8))
        bat = mid_bat(STANCE, backlift, clamp(t, 0, 1))
        body = {k: STANCE[k]*(1-t*0.6) + body_c[k]*t*0.6 for k in body_c}
        cur = P(cur, **fd, hips=hip, **body, **bat, look=(look_override or {}).get("pre", S + V((3,0,0.6))) if fr > 10 else BOWLER)
        keys.append((fr, cur))
    land = P(cur, **backlift, **{k: STANCE[k]*0.35 + body_c[k]*0.65 for k in body_c},
             hips=STANCE["hips"].lerp(V(hips_c), 0.85), look=(look_override or {}).get("pre", S + V((1.5,0,0.3))))
    if keys[-1][0] < bl_frame: keys.append((bl_frame, land))
    else: keys[-1] = (keys[-1][0], P(keys[-1][1], **backlift))
    con = P(land, hips=V(hips_c), **body_c, **contact, look=(look_override or {}).get("contact", S))
    keys.append((tc-4, P(blend(land, con, 0.5), **mid_bat(backlift, contact, 0.55))))
    keys.append((tc, con))
    prev_bat, prev_f, last = contact, tc, con
    for i, (df, bat, body) in enumerate(follows):
        t = (i+1)/len(follows)
        lk = (look_override or {}).get("after", S.lerp(look_after, min(1.0, 0.35+0.65*t)))
        k = P(last, **bat, **body, look=lk)
        # slerp an extra key when the bat turns a lot between keys
        a0 = V(prev_bat["ba"]).normalized(); a1 = V(bat["ba"]).normalized()
        if a0.angle(a1) > math.radians(75) and df > 3:
            keys.append((tc + df - (df - (prev_f - tc))//2, P(blend(last, k, 0.5), **mid_bat(prev_bat, bat, 0.5))))
        keys.append((tc+df, k)); prev_bat, prev_f, last = bat, tc+df, k
    hold_f = max(tc+28, prev_f+8)
    keys.append((hold_f, P(last, hips=last["hips"]+V((0,0,0.01)))))
    if end_pose is None:
        hx = V(hips_c)[0]
        rel = P(last, **bat_g((hx+0.20,-0.26,0.86), (0.15,-0.2,-1), (1,0,0)), lean=12, side=0, twist=6, pyaw=5, rheel=0, lheel=0, look=BOWLER, hips=V(hips_c)+V((-0.04,0,0.06)),
                rball=last["rball"], lball=last["lball"])
        if back_extra: rel = P(rel, **back_extra)
        keys.append((hold_f+14, rel)); keys.append((n-1, STANCE))
    else:
        keys.append((hold_f+16, P(blend(last, end_pose, 0.5), **RELAX and bat_g(V(end_pose["bg"])+V((0.03,0,0.05)), end_pose["ba"], end_pose["bn"]))))
        keys.append((n-1, end_pose))
    keys.sort(key=lambda k: k[0])
    ded = []
    for fr, p in keys:
        if ded and ded[-1][0] == fr: ded[-1] = (fr, p)
        else: ded.append((fr, p))
    SHOTS74[sid] = dict(name=name, angle=angle, n=n, keys=ded, contact=tc, lofted=lofted, info=info,
                        sweet=[round(x,3) for x in S])

# ---------------------------------------------------------------- body presets
def drive_body(th):
    s = math.sin(math.radians(th)); off, leg = max(0, s), max(0, -s)
    return dict(lean=24 + 9*off, side=30 - 10*off, twist=12 - 10*off + 16*leg, pyaw=8 + 10*leg)
FOLLOW_DRIVE = {   # templates for a straight drive (0 deg), relative to contact point; rotated toward the shot
    "ground": [(6, (0.25,0.02,0.62), (0.75,0,-0.35), (0.5,0,0.85)), (14, (-0.15,0.05,1.15), (-0.05,0.35,1.0), (1,0,-0.3))],
    "lofted": [(6, (0.30,0.02,0.85), (0.75,0,0.1), (0.3,0,0.95)), (14, (-0.40,0.15,1.40), (-0.55,0.55,-0.6), (0.3,0.2,0.9))],
    "chip":   [(6, (0.28,0.02,0.70), (0.8,0,-0.1), (0.4,0,0.9)), (14, (0.15,0.02,0.92), (0.75,0.05,0.3), (0.3,0,0.95))],
}
BODY_FIN = {"ground": dict(lean=10, side=10, twist=26, pyaw=24), "lofted": dict(lean=4, side=4, twist=34, pyaw=28),
            "chip": dict(lean=14, side=14, twist=18, pyaw=16)}
def follows_from(tpl, S, rot, body_fin, body_c):
    out = []
    for i, (df, off, a, n) in enumerate(tpl):
        b = body_fin if i == len(tpl)-1 else {k: (body_c[k]+body_fin[k])/2 for k in body_fin}
        out.append((df, rel_bat(S, off, a, n, rot), b))
    return out
def stance_feet(): return dict(rball=STANCE["rball"], lball=STANCE["lball"])

# ================================================================= FAMILY: front-foot drives (vertical bat)
def ff_drive(sid, name, th, line, mode, *, cx=0.62, sz=0.20, wide=False, late=False, inside_out=False, k=3.0, ball=""):
    ln = LINE[line] if isinstance(line, str) else line
    if inside_out:          # backs away to the leg side first, then drives the leg-stump ball through the off side
        feet0 = dict(rball=V((-0.22,0.16,0)), rtoe=V((0.2,-1,0)))
        stride = V((cx+0.06, 0.10, 0))
    else:
        feet0 = {}
        stride = V((cx+0.10, clamp(ln+0.24, -0.46, 0.12), 0))
    if wide: stride = V((cx+0.02, clamp(ln+0.24, -0.56, 0.0), 0))
    S = V((cx - (0.18 if late else 0), ln, sz + (0.02 if mode == "dead" else 0)))
    tilt = dict(ground=0.22, lofted=0.03, chip=0.12, dead=0.36)[mode]
    elev = dict(ground=-0.12, lofted=0.38, chip=0.18, dead=-0.28)[mode]
    a = (-tilt, -0.10*math.sin(math.radians(th)), -1)
    if wide: a = (-0.1 if not late else 0.15, -0.55, -0.8)
    con = bat_c(S, a, face(th, k, elev))
    body_c = drive_body(th)
    if wide: body_c = dict(lean=40, side=16, twist=-4, pyaw=0)
    hips_c = (stride.x*0.57, stride.y*(0.65 if wide else 0.45) + 0.04, -0.29)
    seq = []
    if feet0: seq.append((12, dict(feet0)))
    seq.append((20, dict(lball=stride, ltoe=V((0.5,-1,0)) if th < 180 else V((0.75,-1,0)))))
    if mode == "dead":
        fol = [(6, P(con, bg=V(con["bg"])+V((-0.03,0,-0.01))), {}), (14, P(con, bg=V(con["bg"])+V((-0.04,0,-0.02))), {})]
        fol = [(d, {k_: b[k_] for k_ in ("bg","ba","bn","batw")}, bd) for d, b, bd in fol]
        look_after = S + V((-0.4,0,-0.2))
    else:
        fol = follows_from(FOLLOW_DRIVE[mode], S, -0.6*signed(th), BODY_FIN[mode], body_c)
        look_after = far(S, dirv(th), mode == "lofted")
    eyes = ("Watch the ball from the bowler's hand onto the bat; head over the front knee, still at contact; "
            + ("eyes stay down on the point of contact as the bat dies." if mode == "dead"
               else "then lift to follow the ball over the top." if mode == "lofted" else "then follow the ball along the ground."))
    hands = {"ground": "High backlift; hands lead, bat comes down straight with the face aimed at %d deg; checked high finish, top hand in control." % th,
             "lofted": "High backlift; full swing with the face slightly open-upward at %d deg; hands go through to a full finish over the left shoulder." % th,
             "chip": "Shorter punchy swing with the face lifted slightly; hands stop at chest height (controlled half-loft).",
             "dead": "Soft bottom hand, top hand angles the handle forward; bat 'gives' on impact so the ball drops dead."}[mode]
    body = "Front shoulder leads toward the ball; strong side-bend over the front knee" + \
           (", chest opens through the shot." if mode != "dead" else "; body stays still.")
    feet = ("Back foot steps to the leg side first to make room, then the front foot strides down the pitch." if inside_out else
            "Front foot strides to the pitch of the ball (beside the line), knee bent, back heel lifts on the follow-through." if not wide else
            "Long stride across toward the wide ball, weight fully onto the front foot.")
    shot(sid, name, th, feet_seq=seq, hips_c=hips_c, body_c=body_c, backlift=BL_LOW if mode == "dead" else BL_HIGH,
         contact=con, follows=fol, look_after=look_after, lofted=mode in ("lofted","chip"),
         info=dict(family="front-foot drive", ball=ball, eyes=eyes, hands=hands, upper_body=body, feet=feet))

# ================================================================= FAMILY: front-foot leg-side whips / pulls / slog
FOLLOW_WHIP = {  # relative to S, for a midwicket flick (305 deg)
    "ground": [(6, (0.20,0.15,0.45), (0.2,0.9,-0.35), (0,0.4,-1)), (14, (-0.05,0.25,0.65), (-0.3,0.9,-0.2), (0,0,-1))],
    "lofted": [(6, (0.15,0.15,0.65), (0.2,0.9,0.2), (0.2,0.3,0.9)), (14, (-0.25,0.20,1.15), (-0.5,0.6,-0.6), (0.3,0.2,0.9))],
    "chip":   [(6, (0.18,0.15,0.55), (0.25,0.9,0.0), (0.1,0.3,0.95)), (14, (0.05,0.20,0.80), (0.0,0.9,0.4), (0.2,0.2,0.95))],
    "deflect":[(6, (-0.02,0.02,0.04), (-0.4,0.35,-0.85), (-0.6,1,-0.2)), (14, (-0.04,0.03,0.06), (-0.45,0.4,-0.8), (-0.7,1,-0.2))],
}
FOLLOW_PULL = {  # relative to S for a 300 deg pull
    "ground": [(6, (-0.20,0.57,-0.02), (-0.2,1,-0.3), (0.2,0,-1)), (14, (-0.35,0.67,-0.05), (-0.5,0.8,-0.35), (0,0,-1))],
    "lofted": [(6, (-0.15,0.52,0.15), (-0.1,1,0.25), (0.2,0,1)), (14, (-0.30,0.50,0.37), (-0.55,0.5,-0.65), (0.3,0.3,0.9))],
}
def ff_whip(sid, name, th, line, mode, *, sz=0.30, cx=0.60, walk=False, deflect=False, ball=""):
    ln = LINE[line] if isinstance(line, str) else line
    seq = []
    if walk:
        seq.append((12, dict(rball=V((-0.12,-0.40,0)), rtoe=V((0.3,-1,0)))))
        stride = V((0.48, -0.42, 0)); cx = 0.50
    else:
        stride = V((cx+0.06, clamp(ln+0.14, -0.30, 0.10), 0))
    seq.append((20, dict(lball=stride, ltoe=V((0.7,-1,0)))))
    S = V((cx, ln, sz))
    a = (-0.30, 0.15, -0.94) if deflect else (-0.25, 0.10, -1)
    con = bat_c(S, a, face(th, 1.0 if deflect else 2.5, -0.1 if mode != "lofted" else 0.35))
    body_c = dict(lean=18, side=28, twist=18, pyaw=14)
    fin = dict(lean=10, side=8, twist=32, pyaw=26) if mode != "lofted" else dict(lean=4, side=4, twist=42, pyaw=32)
    if deflect:
        g = V(con["bg"]); fol = [(6, bat_g(g+V((-0.03,0.03,0.03)), con["ba"], con["bn"]), body_c), (14, bat_g(g+V((-0.04,0.04,0.05)), con["ba"], con["bn"]), body_c)]
    else:
        fol = follows_from(FOLLOW_WHIP[mode], S, -0.6*(signed(th) - (-55)), fin, body_c)
    hips_c = (stride.x*0.55, stride.y*0.45+0.05, -0.27)
    shot(sid, name, th, feet_seq=seq, hips_c=hips_c, body_c=body_c, backlift=BL_LOW if deflect else BL_HIGH,
         contact=con, follows=fol, look_after=far(S, dirv(th), mode == "lofted"), lofted=mode in ("lofted","chip"),
         info=dict(family="front-foot leg-side", ball=ball,
                   eyes="Eyes on the ball onto the pads; head stays over the ball and slightly toward the off side so the bat comes past the pad; " +
                        ("eyes follow the ball fine." if deflect else "then turn to follow it to %d deg." % th),
                   hands=("Bat angled, wrists turn the face late toward fine leg — a deflection, no follow-through." if deflect else
                          "Bat comes down straight, then wrists roll the face closed through contact toward %d deg; " % th +
                          {"ground": "bat wraps around low.", "lofted": "bat whips through high over the left shoulder.",
                           "chip": "short follow-through, face lifted (half-loft)."}[mode]),
                   upper_body="Front shoulder leads, then the chest opens to the leg side as the wrists work.",
                   feet=("Back foot walks across to the off side first, front foot follows across — ball is then on the pads." if walk else
                         "Front foot strides toward the line of the ball; front toe opens slightly to let the hips turn.")))

def ff_pull(sid, name, th, mode, *, slog=False, ball=""):
    if slog:
        stride = V((0.58, 0.10, 0)); S = V((0.66, -0.30, 0.40)); a = (0.55,-0.75,-0.35); body_c = dict(lean=26, side=22, twist=30, pyaw=22)
        fol = follows_from(FOLLOW_PULL["lofted"], S, -0.4*(signed(th) - (-60)), dict(lean=2, side=0, twist=72, pyaw=50), body_c)
    else:
        stride = V((0.50, -0.02, 0)); S = V((0.45, -0.40, 0.95)); a = (0.35,-1,-0.05); body_c = dict(lean=10, side=10, twist=50, pyaw=36)
        fol = follows_from(FOLLOW_PULL[mode], S, -0.4*(signed(th) - (-60)), dict(lean=8, side=0, twist=75, pyaw=52), body_c)
    con = bat_c(S, a, face(th, 3, 0.4 if mode == "lofted" or slog else -0.1))
    shot(sid, name, th, feet_seq=[(20, dict(lball=stride, ltoe=V((0.9,-0.5,0))))], hips_c=(stride.x*0.5 if not slog else 0.34, 0.04, -0.20 if not slog else -0.30),
         body_c=body_c, backlift=BL_HIGH, contact=con, follows=fol, look_after=far(S, dirv(th), mode == "lofted" or slog),
         lofted=(mode == "lofted" or slog),
         info=dict(family="front-foot cross-bat", ball=ball,
                   eyes="Pick the length early; eyes on the ball into the hitting zone, then up and across to %d deg." % th,
                   hands=("Big cross-bat swing from high backlift; bottom hand drives through the line toward %d deg, full finish over the left shoulder." % th if slog else
                          "Horizontal bat at waist height, arms extended through contact, wrists roll over the ball toward %d deg." % th),
                   upper_body="Hips and chest rotate hard to face the bowler and beyond; head stays level through the turn.",
                   feet=("Front leg clears to the leg side to free the hips; back foot pivots." if slog else
                         "Short front-foot stride, front toe opened toward midwicket so the hips can rotate.")))

# ================================================================= FAMILY: sweeps / laps / scoops (back knee down)
KNEEL = dict(rball=V((-0.42,0.10,0)), rtoe=V((0.2,-1,0)), rheel=75, rkp=V((0.4,-0.2,-1)))
SWEEP_BODY = dict(lean=34, side=22, twist=8, pyaw=10)
def kneel_shot(sid, name, th, mode, ball=""):
    stride = V((0.82,-0.12,0)); hips_c = (0.18,0.06,-0.47); body_c = dict(SWEEP_BODY); back = dict(KNEEL)
    if mode in ("sweep", "slog_sweep"):
        S = V((0.86,-0.25,0.16 if mode == "sweep" else 0.20)); a = (0.82,0.05,-0.5 if mode == "sweep" else -0.42)
        pre = bat_g((0.20,-0.38,0.78 if mode == "sweep" else 0.95), (0.35,-0.75,0.55) if mode == "sweep" else (0.2,-0.6,0.8), unit(1,0.2,0))
        n = face(th, 1.6, 0.05 if mode == "sweep" else 0.45)
        tpl = [(6, (-0.46,0.21,0.39), (0.15,0.95,-0.15), (-0.2,0.1,-1)), (14, (-0.51,0.36,0.44), (-0.35,0.9,0.05), (0,0,-1))] if mode == "sweep" else \
              [(6, (-0.41,0.24,0.55), (0.15,0.9,0.35), (0,0.3,1)), (14, (-0.66,0.29,0.90), (-0.5,0.4,-0.75), (0.3,0.3,0.9))]
        fol = follows_from(tpl, S, -0.5*(signed(th) - (-90)), dict(lean=28, side=14, twist=22, pyaw=14) if mode == "sweep" else dict(lean=22, side=10, twist=32, pyaw=20), body_c)
        hands = ("Bat comes down from high, swept horizontally in front of the front pad; " +
                 ("wrists roll the face over to keep it down toward %d deg." % th if mode == "sweep" else "face open-upward, big finish over the left shoulder toward %d deg." % th))
    elif mode in ("paddle", "lap"):
        S = V((0.74,-0.08,0.20 if mode == "paddle" else 0.26)); a = (0.6,-0.35,-0.6) if mode == "paddle" else (0.75,-0.4,-0.35)
        pre = bat_g((0.25,-0.28,0.72), (0.6,-0.5,-0.4), unit(1,0.2,0)); n = face(th, 1.0, 0.1 if mode == "paddle" else 0.35)
        fol = [(6, P(bat_g(V(S)+V((-0.40,-0.10,0.40)), (0.6,-0.1,-0.7) if mode == "paddle" else (0.6,0.3,-0.3), (-0.7,1,0.1)), batw=1.0), {}),
               (14, P(bat_g(V(S)+V((-0.42,-0.08,0.42)), (0.6,-0.05,-0.75) if mode == "paddle" else (0.4,0.6,-0.2), (-0.7,1,0.1)), batw=1.0), {})]
        hands = "Short backlift; bat presented with an angled face, " + ("the ball is helped fine off the face — almost no swing." if mode == "paddle" else
                "bat swept across the line under the ball and lifted fine with the wrists.")
    elif mode in ("reverse", "reverse_lofted", "reverse_lap", "reverse_scoop"):
        S = V((0.84,-0.18,{"reverse":0.18,"reverse_lofted":0.22,"reverse_lap":0.26}.get(mode,0.18)))
        a = (0.82,-0.05,-0.5) if mode in ("reverse","reverse_lofted") else ((0.65,0.35,-0.55) if mode == "reverse_lap" else (0.85,0.3,0.05))
        S = V((0.80,-0.20,0.85)) if mode == "reverse_scoop" else S
        pre = bat_g((0.25,0.0,0.82), (0.2,0.75,0.6), unit(1,-0.2,0))
        n = face(th, 1.6 if mode in ("reverse","reverse_lofted") else 1.0,
                 dict(reverse=0.05, reverse_lofted=0.45, reverse_lap=0.25, reverse_scoop=0.85)[mode])
        if mode == "reverse_scoop": hips_c = (0.12,0.04,-0.45); body_c = dict(lean=8, side=-2, twist=30, pyaw=25)
        tpl = [(6, (-0.46,-0.14,0.40), (0.1,-0.95,-0.1), (-0.2,0,-1)), (14, (-0.54,-0.14,0.44), (-0.3,-0.9,0.1), (0,0,-1))] if mode == "reverse" else \
              [(6, (-0.46,-0.17,0.58), (0.1,-0.9,0.4), (0,-0.3,1)), (14, (-0.71,-0.12,0.88), (-0.5,-0.4,0.75), (0.2,-0.3,0.9))] if mode == "reverse_lofted" else \
              [(6, (-0.40,0.05,0.30), (0.5,-0.2,-0.6), (-0.6,-1,0.3)), (14, (-0.42,0.05,0.32), (0.45,-0.3,-0.6), (-0.7,-1,0.3))] if mode == "reverse_lap" else \
              [(6, (-0.48,0.10,0.20), (0.8,0.3,0.35), (-0.6,-0.4,0.7)), (14, (-0.50,0.10,0.24), (0.75,0.3,0.45), (-0.7,-0.4,0.6))]
        fol = follows_from(tpl, S, -0.5*(th - (95 if mode in ("reverse","reverse_lofted") else th)), dict(lean=28, side=16, twist=0, pyaw=6), body_c)
        hands = {"reverse": "Bat taken back to the leg side, then swept across the body with the face turned toward the off side (%d deg); wrists roll the face down." % th,
                 "reverse_lofted": "Reverse swing with the face opened upward, finishing high over the right shoulder toward %d deg." % th,
                 "reverse_lap": "Reverse-paddle: face turned to the off side and angled fine; the ball is guided to %d deg." % th,
                 "reverse_scoop": "Bat held flat, face up and turned to the off side, ball scooped over the slips (%d deg)." % th}[mode]
    elif mode == "dilscoop":
        stride = V((0.60,-0.02,0)); hips_c = (0.12,0.04,-0.45); body_c = dict(lean=6, side=-4, twist=55, pyaw=40)
        back = dict(KNEEL, rball=V((-0.35,0.12,0)), rtoe=V((0.6,-1,0)))
        S = V((0.72,-0.16,0.98)); a = (0.9,-0.25,0.08); pre = bat_g((0.10,-0.18,0.92), (0.6,-0.7,0.3), unit(0,0,1)); n = face(th, 1.0, 1.0)
        fol = [(6, bat_g(V(S)+V((-0.46,0.08,0.12)), (0.85,-0.2,0.35), unit(-0.7,0,0.7)), dict(lean=2, side=-8, twist=55, pyaw=40)),
               (14, bat_g(V(S)+V((-0.50,0.10,0.16)), (0.8,-0.2,0.45), unit(-0.75,0,0.6)), dict(lean=2, side=-8, twist=55, pyaw=40))]
        hands = "Bat held flat in front of the face, face up; the ball is lifted over the batter's own head and the keeper — wrists only."
    elif mode == "falling_scoop":
        stride = V((0.55,-0.20,0)); hips_c = (0.05,0.02,-0.50); body_c = dict(lean=-8, side=-14, twist=30, pyaw=20)
        back = dict(KNEEL, rball=V((-0.40,0.05,0)))
        S = V((0.62,-0.12,0.45)); a = (0.75,0.3,-0.35); pre = bat_g((0.15,-0.15,0.95), (0.5,-0.6,0.5), unit(0.5,0,0.8)); n = face(th, 1.0, 0.6)
        fol = [(6, bat_g(V(S)+V((-0.45,0.05,0.25)), (0.6,0.5,-0.1), (-0.6,0.4,0.7)), dict(lean=-12, side=-18, twist=30, pyaw=20)),
               (14, bat_g(V(S)+V((-0.50,0.05,0.30)), (0.5,0.55,0.0), (-0.6,0.4,0.7)), dict(lean=-14, side=-20, twist=30, pyaw=20))]
        hands = "Bat comes up under the ball as the batter falls away to the off side; face scoops it fine (%d deg)." % th
    con = bat_c(S, a, n)
    look_after = far(S, dirv(th), mode not in ("sweep","paddle","reverse","reverse_lap"))
    look_override = None
    eyes = "Head goes down with the ball; eyes on the ball onto the bat in front of the front pad, " + \
           ("then turn to watch it go %s." % ("over the keeper" if mode == "dilscoop" else "fine" if mode in ("paddle","lap","falling_scoop","reverse_lap") else "to %d deg" % th))
    shot(sid, name, th, feet_seq=[(14, dict(back)), (20, dict(lball=stride, ltoe=V((0.45,-1,0)) if mode != "dilscoop" else V((0.9,-0.4,0))))],
         hips_c=hips_c, body_c=body_c, backlift=pre, contact=con, follows=fol, look_after=look_after,
         lofted=mode in ("slog_sweep","reverse_lofted","dilscoop","reverse_scoop","lap"), back_extra=None,
         info=dict(family="sweep / scoop (kneeling)", ball=ball, eyes=eyes, hands=hands,
                   upper_body=("Chest turned square to the bowler, head under the ball, upright torso." if mode in ("dilscoop","reverse_scoop") else
                               "Falling back and away to the off side as the shot is played." if mode == "falling_scoop" else
                               "Bent low over the front knee, head over the line of the ball, shoulders level."),
                   feet="Long front-foot stride to the pitch of the ball; back knee drops to the ground, back toes tucked."))

# ================================================================= FAMILY: back-foot vertical bat
FOLLOW_PUNCH = [(6, (0.20,0.06,0.35), (0.6,-0.35,-0.5), (0.4,-0.4,0.8)), (14, (0.02,0.16,0.68), (-0.05,0.25,1), (0.8,-1,-0.3))]
def bf_vertical(sid, name, th, line, mode, *, ball=""):
    ln = LINE[line] if isinstance(line, str) else line
    rb = V((-0.42, clamp(ln+0.12, -0.30, 0.0), 0)); lb = V((-0.02, clamp(ln+0.18, -0.24, 0.05), 0))
    S = V((0.04 if mode in ("defence","punch") else -0.02, ln, 0.72 if mode in ("defence","punch") else 0.88))
    if mode == "defence":
        con = bat_c(S, (-0.38,0,-0.92), face(0, 3, -0.25)); fol = [(6, P(con), {}), (14, P(con), {})]
        fol = [(d, {k_: b[k_] for k_ in ("bg","ba","bn","batw")}, bd) for d, b, bd in fol]
    elif mode == "punch":
        con = bat_c(S, (-0.3,-0.06*math.sin(math.radians(th)),-0.95), face(th, 3, -0.15))
        fol = follows_from(FOLLOW_PUNCH, S, -0.6*(signed(th) - 55), dict(lean=8, side=4, twist=12, pyaw=10), dict(lean=12, side=6, twist=-4, pyaw=0))
    elif mode in ("clip", "nudge"):
        con = bat_c(S, (-0.25,0.12,-0.96), face(th, 2.5, -0.15))
        fol = follows_from(FOLLOW_WHIP["ground"], S, -0.6*(signed(th) - (-55)), dict(lean=10, side=4, twist=34, pyaw=26), dict(lean=12, side=4, twist=12, pyaw=8))
        if mode == "nudge": fol = [(d, P(b, bg=V(con["bg"]).lerp(V(b["bg"]), 0.5)), bd) for d, b, bd in fol]
    elif mode == "glance":
        con = bat_c(S, (-0.3,0.15,-0.94), face(th, 1.0, -0.1))
        fol = [(6, bat_g(V(con["bg"])+V((-0.02,0.02,0.03)), con["ba"], con["bn"]), {}), (14, bat_g(V(con["bg"])+V((-0.03,0.03,0.04)), con["ba"], con["bn"]), {})]
    body_c = dict(lean=14, side=4, twist=0 if mode in ("defence","punch") else 10, pyaw=0 if mode in ("defence","punch") else 8)
    shot(sid, name, th, feet_seq=[(14, dict(rball=rb, rtoe=V((0.15,-1,0)))), (20, dict(lball=lb, ltoe=V((0.5,-1,0))))],
         hips_c=(-0.20, lb.y*0.6+0.02, -0.08), body_c=body_c, backlift=BL_LOW if mode in ("defence","glance","nudge") else BL_HIGH,
         contact=con, follows=fol, look_after=far(S, dirv(th), False) if mode != "defence" else S + V((-0.3,0,-0.3)), lofted=False,
         info=dict(family="back-foot vertical bat", ball=ball,
                   eyes="Eyes level, head still and in line with the ball as it rises; " +
                        ("watch it down onto the bat and under the eyes." if mode == "defence" else "watch it onto the bat, then off it toward %d deg." % th),
                   hands={"defence": "High front elbow, top hand in control, soft bottom hand — bat angled down, ball dropped dead.",
                          "punch": "Short backlift, top-hand punch with a firm wrist; the face presented toward %d deg; short checked finish." % th,
                          "clip": "Bat comes down straight, wrists turn the face to the leg side late (%d deg)." % th,
                          "nudge": "Soft hands, short wristy turn into the leg side for a single.",
                          "glance": "Bat angled, face turned late to deflect the ball fine (%d deg); no follow-through." % th}[mode],
                   upper_body="Tall on the back foot, shoulders side-on, head over the ball" + ("; chest opens slightly as the wrists turn." if mode in ("clip","nudge","glance") else "."),
                   feet="Back foot moves back and across toward off stump; front foot follows to stay balanced, both feet planted at contact."))

# ================================================================= FAMILY: cuts (horizontal, off side)
FOLLOW_CUT = {"cut": [(6, (-0.15,0.40,0.06), (0.2,0.15,-1), (0,-1,-0.1)), (14, (-0.10,0.60,0.23), (-0.2,0.6,-0.75), (0,0,-1))],
              "upper": [(6, (-0.10,0.42,0.20), (0.0,-0.6,0.8), (-0.6,0,0.8)), (14, (-0.05,0.67,0.27), (-0.3,0.3,0.9), (-0.7,-0.3,0.5))]}
def cut(sid, name, th, mode, *, front=False, ball=""):
    if front:
        seq = [(20, dict(lball=V((0.44,-0.56,0)), ltoe=V((0.3,-1,0))))]; hips_c = (0.18,-0.30,-0.26)
    else:
        seq = [(14, dict(rball=V((-0.26,-0.50,0)), rtoe=V((0.35,-1,0)))), (20, dict(lball=V((0.08,-0.30,0)), ltoe=V((0.5,-1,0))))]; hips_c = (-0.08,-0.24,-0.14)
    late = th >= 125
    if mode == "upper":
        S = V((0.05 if not late else -0.12, -0.66, 1.32)); a = (0.85,-0.3,0.15); n = face(th, 2.5, 0.75)
        body_c = dict(lean=4, side=-6, twist=-10, pyaw=-8); fin = dict(lean=-2, side=-8, twist=-2, pyaw=-4)
        fol = follows_from(FOLLOW_CUT["upper"], S, -0.4*(th - 100), fin, body_c)
    elif mode == "steer" or (mode == "cut" and late):
        S = V((-0.38 if not front else 0.20, -0.62, 0.62 if not front else 0.45)); a = (-0.25,-0.35,-0.9); n = face(th, 1.0, -0.1)
        body_c = dict(lean=26, side=-2, twist=-16, pyaw=-10)
        fol = [(6, bat_g(V(S)+V((0.13,0.20,0.33))-a_*0 if False else V(S)-V(a)*0.49+V((0.04,0.06,0.04)), a, n), body_c) for a_ in [0]] + \
              [(14, bat_g(V(S)-V(a)*0.49+V((0.06,0.08,0.06)), a, n), body_c)]
        a = V(a)
    else:
        S = V((0.32, -0.62, 0.62)) if front else V((0.22 if th < 100 else 0.04, -0.66, 0.74)); a = (0.9,-0.25,-0.35); n = face(th, 4.0, -0.45)
        body_c = dict(lean=30, side=4, twist=-14, pyaw=-8); fin = dict(lean=30, side=2, twist=-4, pyaw=-4)
        fol = follows_from(FOLLOW_CUT["cut"], S, -0.4*(th - 90), fin, body_c)
    con = bat_c(S, a, n)
    shot(sid, name, th, feet_seq=seq, hips_c=hips_c, body_c=body_c, backlift=bat_g((-0.10,-0.16,1.38), (-0.25,-0.45,0.86), unit(0,-1,-0.2)),
         contact=con, follows=fol, look_after=far(S, dirv(th), mode == "upper"), lofted=(mode == "upper"),
         info=dict(family="cut (off side, horizontal bat)", ball=ball,
                   eyes="Eyes track the ball wide of off stump, head moves toward the line; watch it onto the bat then out to %d deg." % th,
                   hands=("Bat comes up under the bouncing ball, face open and angled upward — guided over the infield to %d deg." % th if mode == "upper" else
                          "Late, soft hands; open face guides the ball fine to %d deg — no real follow-through." % th if (mode == "steer" or late) else
                          "Bat high, then slashed down horizontally on top of the ball; wrists roll to keep it down toward %d deg." % th),
                   upper_body="Front shoulder points toward the ball, arms extend away from the body, weight transfers across to the off side.",
                   feet=("Front foot strides across toward the wide ball." if front else
                         "Back foot steps back and across toward the off side, front foot follows; weight on the back foot.")))

# ================================================================= FAMILY: back-foot pulls / hooks / ramps
def bf_cross(sid, name, th, mode, *, ball=""):
    seq = [(14, dict(rball=V((-0.30,-0.30,0)), rtoe=V((0.6,-1,0)))), (20, dict(lball=V((0.10,0.12,0)), ltoe=V((1,-0.5,0))))]
    hips_c = (-0.10,-0.05,-0.10); bl = bat_g((-0.10,-0.12,1.36), (-0.3,-0.5,0.8), unit(0.3,-1,0))
    look_override = None
    if mode in ("pull", "pull_lofted"):
        S = V((0.40,-0.42,1.0)); a = (0.35,-1,-0.05 if mode == "pull" else 0.05); n = face(th, 3, -0.15 if mode == "pull" else 0.35)
        body_c = dict(lean=10, side=4, twist=55, pyaw=40)
        fol = follows_from(FOLLOW_PULL["ground" if mode == "pull" else "lofted"], S, -0.4*(signed(th) - (-60)),
                           dict(lean=12, side=0, twist=75, pyaw=55) if mode == "pull" else dict(lean=2, side=-2, twist=80, pyaw=58), body_c)
    elif mode == "hook":
        seq = [(14, dict(rball=V((-0.35,-0.20,0)), rtoe=V((0.6,-1,0)))), (20, dict(lball=V((0.05,0.15,0)), ltoe=V((1,-0.4,0))))]
        hips_c = (-0.18,0.0,-0.08); S = V((0.25 if th > 230 else 0.10,-0.40,1.52)); a = (0.3,-1,0.2); n = face(th, 2.5, 0.45)
        body_c = dict(lean=-4, side=-6, twist=60, pyaw=45)
        fol = follows_from(FOLLOW_PULL["lofted"], S, -0.4*(signed(th) - (-110)), dict(lean=-6, side=-10, twist=85, pyaw=60), body_c)
        bl = bat_g((-0.10,-0.12,1.45), (-0.3,-0.5,0.8), unit(0.3,-1,0))
    elif mode in ("ramp", "noloook"):
        seq = [(14, dict(rball=V((-0.36,-0.15,0)), rtoe=V((0.2,-1,0)))), (20, dict(lball=V((0.02,-0.08,0)), ltoe=V((0.6,-1,0))))]
        hips_c = (-0.18,0.02,-0.22); body_c = dict(lean=10, side=-4, twist=20 if th > 170 else 0, pyaw=15 if th > 170 else 0)
        S = V((0.12, -0.30 if th > 170 else -0.55, 1.30)); a = (0.85,-0.35,0.2); n = face(th, 1.0, 0.95)
        bl = bat_g((-0.05,-0.18,1.05), (0.2,-0.7,0.6), unit(0,0,1))
        fol = [(6, bat_g(V(S)-V(a).normalized()*0.49+V((-0.05,0,0.06)), a, rotz((-0.5,0,0.85), 0)), body_c),
               (14, bat_g(V(S)-V(a).normalized()*0.49+V((-0.07,0,0.08)), a, (-0.6,0,0.8)), body_c)]
        if mode == "noloook":
            look_override = dict(contact=V((3,-0.4,0.2)), after=V((4,-0.4,0.0)))   # head turned down the pitch, not following the ball
    con = bat_c(S, a, n)
    lofted = mode in ("pull_lofted","hook","ramp","noloook")
    shot(sid, name, th, feet_seq=seq, hips_c=hips_c, body_c=body_c, backlift=bl, contact=con, follows=fol,
         look_after=far(S, dirv(th), lofted), lofted=lofted, look_override=look_override,
         info=dict(family="back-foot cross-bat", ball=ball,
                   eyes={"pull": "Pick the short ball early; eyes on it at waist/chest height, head inside the line; follow it to %d deg." % th,
                         "pull_lofted": "Eyes on the ball into the hitting arc, head stays still, then up to watch it over the rope at %d deg." % th,
                         "hook": "Eyes on the ball at head height, head moves inside the line (to the off side of the ball); follow it fine-ish to %d deg." % th,
                         "ramp": "Eyes on the ball all the way onto the raised bat face, then turn to watch it go over the %s." % ("keeper" if th > 170 else "slips"),
                         "noloook": "Eyes on the ball until it reaches the bat, then the head deliberately stays looking down the pitch — the batter does not watch it go."}[mode],
                   hands={"pull": "Horizontal bat, arms extended through contact, wrists roll over the ball to keep it down toward %d deg." % th,
                          "pull_lofted": "Horizontal bat swung through and under the ball, face open, full finish over the left shoulder.",
                          "hook": "Bat swung horizontally at head height and through, finishing over the left shoulder.",
                          "ramp": "Bat raised flat, face to the sky, presented under the ball — pace does the work.",
                          "noloook": "Bat raised flat, face up and turned to the off side; the ball runs off the face to third man."}[mode],
                   upper_body="Weight goes back, hips and chest rotate toward the leg side as the bat swings." if mode in ("pull","pull_lofted","hook") else
                              "Knees bend to get under the bounce, torso stays upright and still.",
                   feet="Back foot moves back and across, front foot opens toward the leg side so the body can rotate." if mode in ("pull","pull_lofted","hook") else
                        "Back foot back, then a small crouch under the ball; feet planted."))

# ================================================================= FAMILY: down the track (advance)
def advance(sid, name, th, mode, ball=""):
    from shots import ADV, SHOTS as OLD
    ln = -0.24 if th in (0, 345, 15) else -0.30
    lb = V((1.20, -0.06, 0)) if th != 40 else V((1.12, 0.16, 0)); rb = V((0.66, 0.02, 0)) if th != 40 else V((0.66, 0.22, 0))
    S = V((1.18, ln, 0.28 if th != 40 else 0.42))
    con = bat_c(S, (0.03, -0.1*math.sin(math.radians(th)), -1), face(th, 3, 0.4))
    body_c = dict(DRIVE := drive_body(th)); body_c["lean"] -= 4
    ADV("__tmp", lb_final=lb, ltoe=(0.6,-1,0), rb_cross=rb, hips_c=(0.92 if th != 40 else 0.86, 0.03 if th != 40 else 0.16, -0.28),
        body_c=body_c, contact=con,
        follow=rel_bat(S, (0.30,0.02,0.85), (0.75,0,0.1), (0.3,0,0.95), -0.6*signed(th)),
        finish=rel_bat(S, (-0.40,0.15,1.40), (-0.55,0.55,-0.6), (0.3,0.2,0.9), -0.6*signed(th)),
        body_f=BODY_FIN["lofted"], shot_dir=(dirv(th).x, dirv(th).y), lofted=True)
    n_, keys, c, meta = OLD.pop("__tmp")
    SHOTS74[sid] = dict(name=name, angle=th, n=n_, keys=keys, contact=c, lofted=True, sweet=[round(x,3) for x in S],
        info=dict(family="down the track", ball=ball,
                  eyes="Read the flight early; eyes stay on the ball while charging, head steady at the moment of contact, then up to follow it to %d deg." % th,
                  hands="Bat goes up as the feet move; full swing through the line with the face open toward %d deg; high finish." % th,
                  upper_body="Shoulders stay side-on during the charge, then the front shoulder opens through the shot.",
                  feet="Front foot steps out, back foot crosses behind it (chassé), front foot plants at the pitch of the ball ~1.2 m down the track."))

# ================================================================= FAMILY: switch hit (stance switched, grip kept)
def switch_hit(sid, name, th, ball=""):
    tc = 40
    Y0 = -0.56                                     # left-hander stands on the other side of the stumps line
    sw_feet = dict(rball=V((0.50, Y0, 0)), rtoe=V((0.4,1,0)), lball=V((-0.20, Y0+0.02, 0)), ltoe=V((0.1,1,0)),
                   lkp=V((0,1,0.2)), rkp=V((0.3,1,0.2)))
    hips_c = (0.16, Y0+0.06, -0.24)
    if th < 120:   # slog-whip through the lefty's leg side (original cover / point)
        S = V((0.60, -0.44, 0.42)); a = (0.45, 0.8, -0.35); n = face(th, 3, 0.45)
        body_c = dict(lean=-20, side=22, twist=-30, pyaw=165)
        fin = dict(lean=-6, side=6, twist=-65, pyaw=170)
        fol = [(6, rel_bat(S, (-0.10,-0.45,0.40), (-0.1,-0.95,0.3), (0,-0.3,1)), dict(lean=-12, side=14, twist=-48, pyaw=168)),
               (14, rel_bat(S, (-0.30,-0.40,0.95), (-0.5,-0.5,-0.7), (0.3,-0.3,0.9)), fin)]
    else:          # lefty's leg glance = original third man
        S = V((0.48, -0.40, 0.32)); a = (-0.30, -0.15, -0.94); n = face(th, 1.0, -0.1)
        body_c = dict(lean=-18, side=24, twist=-12, pyaw=165)
        con0 = bat_c(S, a, n); g = V(con0["bg"])
        fol = [(6, bat_g(g+V((-0.03,-0.03,0.03)), con0["ba"], con0["bn"]), body_c), (14, bat_g(g+V((-0.04,-0.04,0.05)), con0["ba"], con0["bn"]), body_c)]
    con = bat_c(S, a, n)
    bl = bat_g((-0.12, -0.46, 1.14), (-0.45, 0.28, 0.85), unit(0.15, 1, -0.4))      # backlift mirrored for the new stance
    seq = [(14, dict(rball=V((0.12,-0.20,0)), lball=V((0.02,-0.30,0)), _hip=0.25)),   # hop: feet come together, moving across
           (24, dict(sw_feet, _hip=0.7))]
    shot(sid, name, th, feet_seq=seq, hips_c=hips_c, body_c=body_c, backlift=bl, contact=con, follows=fol,
         look_after=far(S, dirv(th), th < 120), lofted=th < 120, tc=tc, n=120,
         info=dict(family="switch hit", ball=ball,
                   eyes="Eyes stay on the bowler through the switch; head re-centres over the new front (right) foot, watches the ball onto the bat, then follows it to %d deg." % th,
                   hands="Grip is kept (right hand stays the bottom hand, so the bat stays on the right-hand socket); the bat is swung like a left-hander's " +
                         ("slog through %d deg (the original off side), finishing high." % th if th < 120 else "glance, guiding the ball fine to %d deg (original third man)." % th),
                   upper_body="Hop-turn of ~165 deg into a left-hander's side-on stance during the delivery stride; chest then opens through the shot.",
                   feet="Hop and swap: feet come together while moving ~0.5 m across to the off side of the stumps, then the right foot lands forward and the left foot back (left-hander's stance)."))

# ================================================================= FAMILY: helicopter
def helicopter(sid, name, th, ball=""):
    stride = V((0.62, -0.08, 0)); S = V((0.58, -0.22, 0.21))
    con = bat_c(S, (-0.08, 0.04, -1), face(th, 3, 0.35))
    body_c = dict(lean=18, side=26, twist=22, pyaw=16)
    r = -0.6*(signed(th) - (-15))
    fol = [(4, rel_bat(S, (0.28,0.25,0.50), (0.3,0.8,0.5), (0,-0.3,1), r), dict(lean=10, side=16, twist=34, pyaw=22)),
           (9, rel_bat(S, (0.05,0.20,1.20), (-0.2,0.9,0.4), (0.4,0,0.9), r), dict(lean=4, side=8, twist=40, pyaw=26)),
           (14, rel_bat(S, (-0.20,0.05,1.45), (-0.6,-0.6,0.5), (0.4,-0.3,0.85), r), dict(lean=2, side=4, twist=44, pyaw=30)),
           (20, rel_bat(S, (-0.30,-0.05,1.40), (-0.3,-0.9,-0.3), (0.2,-0.2,0.95), r), dict(lean=2, side=4, twist=44, pyaw=30)),
           (26, rel_bat(S, (-0.30,0.05,1.30), (0.2,0.6,-0.75), (0.5,0.2,0.8), r), dict(lean=4, side=6, twist=40, pyaw=28))]
    shot(sid, name, th, feet_seq=[(20, dict(lball=stride, ltoe=V((0.7,-1,0))))], hips_c=(0.30,0.02,-0.25), body_c=body_c, backlift=BL_HIGH,
         contact=con, follows=fol, look_after=far(S, dirv(th), True), lofted=True, n=100,
         info=dict(family="helicopter", ball=ball,
                   eyes="Eyes locked on the yorker right down to the bat at the base of the stumps, then up to follow it to %d deg." % th,
                   hands="Bat jabbed down onto the yorker with a closed face; bottom-hand wrist whips the bat up and the bat circles over the head (the 'helicopter') toward %d deg." % th,
                   upper_body="Stays tall over the ball at contact, then the chest opens and the head lifts as the bat whirls round.",
                   feet="Short front-foot stride, front leg braced; back foot pivots as the hips rotate."))

# ================================================================= LEAVE
def leave(sid, name, th, ball=""):
    con = bat_g((-0.12,0.04,1.55), (-0.45,0.35,0.82), unit(0,1,-0.2))
    shot(sid, name, th, feet_seq=[(14, dict(rball=V((-0.40,-0.16,0)), rtoe=V((0.15,-1,0)))), (20, dict(lball=V((-0.02,-0.12,0)), ltoe=V((0.5,-1,0))))],
         hips_c=(-0.18,-0.02,-0.08), body_c=dict(lean=8, side=0, twist=-4, pyaw=-2), backlift=BL_HIGH, contact=con,
         follows=[(6, dict(con), {}), (14, dict(con), {})], look_after=V((-3,-0.5,0.8)), lofted=False,
         look_override=dict(pre=V((3,-0.5,0.9)), contact=V((0.0,-0.55,0.9)), after=V((-2.5,-0.5,0.8))),
         info=dict(family="leave", ball=ball, eyes="Eyes track the ball all the way past off stump and into the keeper's gloves.",
                   hands="Bat lifted high and away from the line, hands above the head, face turned away.",
                   upper_body="Tall, shoulders still, body stays inside the line of the ball.",
                   feet="Back foot moves back and across to cover off stump, front foot follows; weight balanced."))

# ================================================================= THE LIST (number, name, angle as in the file)
ff_drive(1,  "Forward defence", 0, "stumps", "dead", ball="Full, on the stumps")
ff_drive(2,  "Straight drive", 0, "stumps", "ground", ball="Full, on the stumps")
ff_drive(3,  "On drive", 345, "middle_leg", "ground", ball="Full, middle/leg")
ff_drive(4,  "Off drive", 15, "off", "ground", ball="Full, off stump")
ff_drive(5,  "Cover drive", 55, "outside_off", "ground", cx=0.58, ball="Full, outside off")
ff_drive(6,  "Extra cover drive", 40, "outside_off", "ground", cx=0.60, ball="Full, outside off")
ff_drive(7,  "Square drive", 90, "wide", "ground", cx=0.50, wide=True, ball="Full and wide")
ff_drive(8,  "Backward square drive", 110, "wide", "ground", cx=0.50, wide=True, late=True, ball="Full and wide, played late")
ff_drive(9,  "Lofted straight drive", 0, "stumps", "lofted", ball="Full, on the stumps")
ff_drive(10, "Lofted on drive", 340, "middle_leg", "lofted", ball="Full, middle/leg")
ff_drive(11, "Lofted off drive", 20, "off", "lofted", ball="Full, off stump")
ff_drive(12, "Lofted cover drive", 60, "outside_off", "lofted", cx=0.58, ball="Full, outside off")
ff_drive(13, "Lofted extra cover drive", 40, "outside_off", "lofted", cx=0.60, ball="Full, outside off")
ff_drive(14, "Inside-out drive", 45, -0.17, "ground", inside_out=True, ball="Full, leg-stump line")
ff_drive(15, "Lofted inside-out drive", 40, -0.17, "lofted", inside_out=True, ball="Full, leg-stump line")
ff_whip(16, "Flick", 305, "pads", "ground", ball="Full, on the pads")
ff_whip(17, "Square flick", 270, "pads", "ground", ball="Full, on the pads")
ff_whip(18, "Lofted flick", 310, "pads", "lofted", ball="Full, on the pads")
ff_whip(19, "Leg glance", 200, "leg", "ground", deflect=True, ball="Full, drifting down leg")
ff_pull(20, "Front-foot pull", 300, "lofted", ball="Short-ish, early pick-up")
ff_pull(21, "Front-foot square pull", 265, "lofted", ball="Short-ish, early pick-up")
kneel_shot(22, "Square sweep", 260, "sweep", ball="Full, spin on leg/middle")
kneel_shot(23, "Fine sweep", 215, "sweep", ball="Full, spin on leg")
kneel_shot(24, "Slog sweep", 305, "slog_sweep", ball="Full, spin")
kneel_shot(25, "Square slog sweep", 270, "slog_sweep", ball="Full, spin")
kneel_shot(26, "Paddle sweep", 195, "paddle", ball="Full, leg side")
kneel_shot(27, "Reverse sweep", 95, "reverse", ball="Full, spin")
kneel_shot(28, "Fine reverse sweep", 140, "reverse", ball="Full, spin")
kneel_shot(29, "Lofted reverse sweep", 90, "reverse_lofted", ball="Full, spin")
cut(30, "Steer / dab", 145, "steer", front=True, ball="Good length, outside off")
cut(31, "Front-foot cut", 90, "cut", front=True, ball="Full-ish and wide")
bf_vertical(32, "Back-foot defence", 0, "stumps", "defence", ball="Short of length, stumps/body")
leave(33, "Leave", 180, ball="Outside off")
bf_vertical(34, "Back-foot straight punch", 0, "stumps", "punch", ball="Short of length, stumps")
bf_vertical(35, "Back-foot off punch", 15, "off", "punch", ball="Short of length, off stump")
bf_vertical(36, "Back-foot cover punch", 55, "outside_off", "punch", ball="Short of length, outside off")
cut(37, "Square cut", 90, "cut", ball="Short and wide")
cut(38, "Backward cut", 110, "cut", ball="Short and wide")
cut(39, "Late cut", 140, "cut", ball="Short and wide, played late")
cut(40, "Upper cut (point)", 100, "upper", ball="Short and wide, high")
cut(41, "Upper cut (third man)", 140, "upper", ball="Short and wide, high")
bf_cross(42, "Pull (midwicket)", 300, "pull", ball="Short, waist to chest")
bf_cross(43, "Pull (square)", 270, "pull", ball="Short, waist to chest")
bf_cross(44, "Lofted pull", 305, "pull_lofted", ball="Short, waist to chest")
bf_cross(45, "Hook (square)", 250, "hook", ball="Short, head height")
bf_cross(46, "Hook (fine)", 210, "hook", ball="Short, head height")
bf_vertical(47, "Back-foot clip", 270, "hips", "clip", ball="Short of length, on the hips")
bf_vertical(48, "Back-foot glance", 205, "hips", "glance", ball="Short of length, on the hips")
bf_vertical(49, "Nudge / nurdle", 300, "body", "nudge", ball="Short of length, on the body")
kneel_shot(50, "Scoop / Dilscoop", 180, "dilscoop", ball="Full, straight")
bf_cross(51, "Ramp (keeper)", 180, "ramp", ball="Short, on the body")
bf_cross(52, "Ramp (slips)", 155, "ramp", ball="Short, outside off")
kneel_shot(53, "Lap", 200, "lap", ball="Full, leg side")
kneel_shot(54, "Reverse lap", 150, "reverse_lap", ball="Full, outside off")
kneel_shot(55, "Reverse scoop", 160, "reverse_scoop", ball="Full, outside off")
switch_hit(56, "Switch hit (cover)", 60, ball="Any, stance switched")
switch_hit(57, "Switch hit (point)", 90, ball="Any, stance switched")
switch_hit(58, "Switch hit (third man)", 140, ball="Any, stance switched")
helicopter(59, "Helicopter shot (long-on)", 345, ball="Full / yorker length")
helicopter(60, "Helicopter shot (midwicket)", 310, ball="Full / yorker length")
ff_whip(61, "Walk-across flick", 205, "off", "ground", walk=True, deflect=True, ball="Full, off stump")
ff_whip(62, "Walk-across whip", 270, "off", "ground", walk=True, ball="Full, off stump")
advance(63, "Down-the-track straight loft", 0, "lofted", ball="Spin, charged to full")
advance(64, "Down-the-track long-on loft", 345, "lofted", ball="Spin, charged to full")
advance(65, "Down-the-track long-off loft", 15, "lofted", ball="Spin, charged to full")
advance(66, "Down-the-track cover loft", 40, "lofted", ball="Spin, charged to full")
ff_pull(67, "Slog / cow shot", 315, "lofted", slog=True, ball="Full, across the line")
ff_drive(68, "Chip (straight)", 0, "stumps", "chip", ball="Full, controlled half-loft")
ff_drive(69, "Chip (cover)", 55, "outside_off", "chip", cx=0.58, ball="Full, controlled half-loft")
ff_whip(70, "Chip (midwicket)", 305, "pads", "chip", ball="Full, controlled half-loft")
kneel_shot(71, "Falling scoop", 195, "falling_scoop", ball="Full / short, falling away")
bf_cross(72, "No-look ramp", 145, "noloook", ball="Short, outside off")
ff_drive(73, "Soft-hands drop (off side)", 30, "off", "dead", k=2.0, ball="Any, dead bat")
ff_drive(74, "Soft-hands drop (leg side)", 330, "middle_leg", "dead", k=2.0, ball="Any, dead bat")

def slug(s): return re.sub(r"[^a-z0-9]+", "_", s.lower()).strip("_")

if __name__ == "__main__":
    ids = [int(x) for x in sys.argv[1:]] or sorted(SHOTS74)
    data, metas = {}, []
    for sid in ids:
        sh = SHOTS74[sid]; fname = f"{sid:02d}_{slug(sh['name'])}"
        frames, worst, md = make_bat(fname, sh["n"], sh["keys"], sh["contact"], dict(loop=False))
        data[sid] = frames
        bad = {k: (round(v,3), worst.get(k+"@")) for k, v in worst.items() if not k.endswith("@") and k.endswith("Grip") and v > 0.012}
        metas.append(dict(id=sid, file=fname+".fbx", name=sh["name"], angle_deg=sh["angle"], frames=sh["n"],
                          contact_frame=sh["contact"]+1, lofted=sh["lofted"], sweet_spot_at_contact=md.get("sweet_spot_at_contact"),
                          bat_face_at_contact=md.get("bat_face_at_contact"), **sh["info"]))
        print(sid, fname, "OK" if not bad else bad, flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "b74_frames_%s.pkl") % ("all" if len(ids) == 74 else "part"), "wb"))
    json.dump(metas, open(_os.path.join(APL_WORK, "b74_meta_%s.json") % ("all" if len(ids) == 74 else "part"), "w"), indent=1)
