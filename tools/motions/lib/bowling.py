"""Bowling: complete run-up + delivery + follow-through on the Mixamo rig.
World (Blender): Z up, the bowler runs toward -Y (the rig's forward) at the batter.
Origin: the bowler's popping crease, middle-stump line (x = 0). Bowler's stumps at y = +1.22.
Bowler's LEFT is +X. For a right-handed batter the OFF side is the bowler's RIGHT (-X)."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from fielding import *
import fielding
from field_clips import place, STAND, READY
OUTB = _os.path.join(APL_OUT, "bowling"); os.makedirs(OUTB, exist_ok=True)
fielding.OUTF = OUTB

BATTER = V((0, -18.9, 1.05))                     # where the bowler looks (batter's stumps area)
HOLD_BALL = dict(T=0.5, I=0.45, M=0.45, R=0.75, P=0.8)
FLICK = dict(T=0.15, I=0.05, M=0.05, R=0.45, P=0.55)
KNUCKLE = dict(T=0.5, I=0.95, M=0.95, R=0.8, P=0.85)
SPIN_GRIP = dict(T=0.35, I=0.35, M=0.4, R=0.7, P=0.75)

def rotz(v, deg): return R('Z', deg) @ V(v)
def other(f): return "r" if f == "l" else "l"
def nsteps_of(f0, f1, period): return max(1, int(round((f1 - f0) / (period/2.0))))
def arm_dir(phi, tilt=0.0, side=1):
    """straight-arm direction for the bowling arm. phi: 0 = straight down, 90 = back toward the run-up,
    180 = vertical, 270 = toward the batter. tilt: round-arm angle (deg) away from vertical, outward."""
    r = math.radians(phi)
    d = V((0, math.sin(r), -math.cos(r)))
    return (R('Y', -tilt*side) @ d).normalized()

# ---------------------------------------------------------------- mirroring (left-arm bowlers)
PAIRS = ["ball","toe","heel","kp","t","p","h","hw","pal","palw","w","dir","dirw","clav","fw","T","I","M","R","P","htw","thu","thuw","reach"]
NEG_ANG = ("pyaw","twist","side","pside","nyaw","hyaw","nroll","hroll")
def mirror(p):
    q = dict(p)
    for base in PAIRS:
        a, b = "r"+base, "l"+base
        if a in p and b in p: q[a], q[b] = p[b], p[a]
    for k, v in list(q.items()):
        if isinstance(v, Vector) and len(v) == 3: q[k] = V((-v.x, v.y, v.z))
    for k in NEG_ANG:
        if k in q: q[k] = -q[k]
    return q

# ---------------------------------------------------------------- action presets
ACTIONS = {
 # name: run (m), top speed (m/s), stride period (frames), bound (m), BFC yaw (deg; -90 side-on, -20 front-on),
 # counter-rotation twist at BFC (mixed actions), delivery stride (m), arm tilt (round-arm), kind
 "express_fast":  dict(run=22.0, v=7.2, period=15, bound=2.3, yaw=-88, twist=0,   stride=1.45, tilt=8,  kind="pace", lean_rel=32, hip=0.98),
 "fast_medium":   dict(run=16.0, v=6.2, period=16, bound=2.0, yaw=-55, twist=-10, stride=1.35, tilt=10, kind="pace", lean_rel=28, hip=0.98),
 "medium":        dict(run=11.0, v=5.2, period=17, bound=1.7, yaw=-25, twist=-5,  stride=1.25, tilt=12, kind="pace", lean_rel=24, hip=0.99),
 "slow_medium":   dict(run=7.0,  v=4.2, period=19, bound=1.3, yaw=-30, twist=0,   stride=1.15, tilt=12, kind="pace", lean_rel=20, hip=1.00),
 "slinger":       dict(run=18.0, v=6.8, period=15, bound=2.1, yaw=-70, twist=-15, stride=1.40, tilt=55, kind="pace", lean_rel=34, hip=0.97),
 "off_spin":      dict(run=6.0,  v=3.4, period=21, bound=1.0, yaw=-80, twist=-20, stride=1.10, tilt=8,  kind="spin", lean_rel=22, hip=1.00),
 "leg_spin":      dict(run=6.5,  v=3.6, period=21, bound=1.1, yaw=-82, twist=-25, stride=1.15, tilt=10, kind="spin", lean_rel=24, hip=1.00),
 "side_arm":      dict(run=6.0,  v=3.4, period=21, bound=1.0, yaw=-75, twist=-15, stride=1.10, tilt=48, kind="spin", lean_rel=18, hip=1.00),
}

# release variants: arm angle at release, extra trunk flexion, palm direction (bowler's view: -Y toward batter,
# -X = bowler's right / RHB's off side), hand aim (wrist), fingers
def V3(*a): return V(a).normalized()
VARIANTS = {
 "stock":          dict(phi=205, dlean=0,  palm=V3(0,-1,0.05),      fing=FLICK),
 "bouncer":        dict(phi=214, dlean=7,  palm=V3(0,-1,-0.2),      fing=FLICK,   note="released ~5% of height lower, hit-the-deck"),
 "yorker":         dict(phi=197, dlean=-4, palm=V3(0,-1,0.25),      fing=FLICK,   note="higher release, wrist cocked back longer"),
 "outswinger":     dict(phi=205, dlean=0,  palm=V3(0.35,-1,0.05),   fing=FLICK,   note="seam and wrist angled toward the slips"),
 "inswinger":      dict(phi=205, dlean=0,  palm=V3(-0.35,-1,0.05),  fing=FLICK,   note="seam and wrist angled toward fine leg"),
 "off_cutter":     dict(phi=204, dlean=1,  palm=V3(-0.85,-0.5,0),   fing=FLICK,   note="fingers drag down the right side of the ball, wrist rolls toward leg"),
 "leg_cutter":     dict(phi=204, dlean=1,  palm=V3(0.85,-0.5,0),    fing=FLICK,   note="fingers cut across the ball the other way, wrist rolls toward off"),
 "back_of_hand":   dict(phi=203, dlean=0,  palm=V3(0,1,0.1),        fing=FLICK,   note="hand flipped, back of the hand faces the batter"),
 "knuckle":        dict(phi=205, dlean=0,  palm=V3(0,-1,0.05),      fing=KNUCKLE, note="ball pushed out on bent knuckles"),
 # spin
 "off_break":      dict(phi=192, dlean=0,  palm=V3(-0.6,-0.8,0.1),  fing=dict(T=0.2, I=0.6, M=0.3, R=0.6, P=0.7), note="palm toward the batter, fingers rip clockwise"),
 "doosra":         dict(phi=190, dlean=0,  palm=V3(0.3,1,0.2),      fing=dict(T=0.2, I=0.4, M=0.4, R=0.6, P=0.7), note="wrist cocked, back of the hand faces the batter"),
 "carrom":         dict(phi=192, dlean=0,  palm=V3(-0.2,-1,0.1),    fing=dict(T=0.1, I=0.2, M=0.95, R=0.6, P=0.7), note="flicked out between thumb and bent middle finger"),
 "arm_ball":       dict(phi=196, dlean=0,  palm=V3(0,-1,0.0),       fing=FLICK,   note="hand behind the ball, goes on with the arm"),
 "leg_break":      dict(phi=188, dlean=0,  palm=V3(0.75,-0.6,0.1),  fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), note="wrist cocked inward, ring finger rips the ball"),
 "googly":         dict(phi=186, dlean=1,  palm=V3(-0.2,1,0.3),     fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), note="wrist dropped, back of the hand faces the batter"),
 "flipper":        dict(phi=200, dlean=0,  palm=V3(0,-0.3,1),       fing=dict(T=0.1, I=0.3, M=0.8, R=0.7, P=0.7), note="squeezed out from under the hand, palm up, hand toward the target"),
 "top_spinner":    dict(phi=190, dlean=0,  palm=V3(1,-0.2,0.1),     fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), note="side of the hand toward the batter, seam rolls forward"),
}

def bowl(name, action="express_fast", variant="stock", side="over", left_arm=False, meta_extra=None):
    A = dict(ACTIONS[action]); vr = VARIANTS[variant]; spin = A["kind"] == "spin"
    # line: RA over the wicket runs in left of the stumps (+X); around the wicket to the right (-X). Mirrored for LA later.
    xl = 0.32 if side == "over" else -0.48
    y_bfc = 1.32 if not spin else 1.05
    y_ffc = 0.02 + (y_bfc - 1.32)*0.0
    y_to = y_bfc + A["bound"]
    y0 = y_to + A["run"]
    v = A["v"]
    # frames
    f_go = 10
    T_acc = 1.2 if spin else 1.6                                 # seconds to reach top speed
    D_acc = min(A["run"]*0.55, v*T_acc/1.7)
    f_acc = f_go + int(round(D_acc*1.7/v*30))
    f_to = f_acc + int(round((A["run"]-D_acc)/v*30))
    fb = 9 if not spin else 7                                     # bound flight frames
    f_bfc = f_to + fb
    f_ffc = f_bfc + (6 if not spin else 8)
    f_rel = f_ffc + (4 if not spin else 5)
    n = f_rel + (62 if not spin else 52)
    hip = A["hip"]; yaw = A["yaw"]; tw = A["twist"]; tilt = A["tilt"]
    start = P(BASE, hips=H(xl, y0+0.05, 1.02), rball=V((xl-0.15, y0+0.05, 0)), lball=V((xl+0.15, y0-0.10, 0)),
              rtoe=V((-0.1,-1,0)), ltoe=V((0.1,-1,0)), rt=V((xl-0.08, y0-0.22, 1.12)), lt=V((xl+0.08, y0-0.22, 1.10)),
              rp=V((-0.6,0.5,-1)), lp=V((0.6,0.5,-1)), rh=V((0,-0.4,1)), rhw=0.6, lh=V((0,-0.4,1)), lhw=0.6,
              rpal=V((1,0,0)), rpalw=0.6, lpal=V((-1,0,0)), lpalw=0.6, **fing("r", HOLD_BALL), look=BATTER)
    keys = [(0, start), (f_go-2, start)]
    hold = fing("r", HOLD_BALL)
    n2 = nsteps_of(f_acc, f_to, A["period"]); first2 = "l" if n2 % 2 == 0 else "r"      # take off from the LEFT foot
    n1 = nsteps_of(f_go, f_acc, A["period"]+6); first1 = first2 if n1 % 2 == 0 else other(first2)
    k1, pl1 = run_keys(start, f0=f_go, f1=f_acc, p0=(xl, y0), p1=(xl, y0-D_acc), heading=0, period=A["period"]+6, first=first1,
                       contact=0.36, lean=8, ease="accel", hip_h=hip, arm_amp=0.16, extra=dict(look=BATTER, **hold))
    k2, pl2 = run_keys(start, f0=f_acc, f1=f_to, p0=(xl, y0-D_acc), p1=(xl, y_to), heading=0, period=A["period"], first=first2,
                       contact=0.32, lean=12 if not spin else 6, hip_h=hip, arm_amp=0.22 if not spin else 0.14, extra=dict(look=BATTER, **hold))
    # make sure the take-off foot is the LEFT (pace bound off the left, land on the right)
    keys += k1[1:] + k2[1:]
    base_end = keys[-1][1]
    # ---------------- bound / gather
    up = 0.14 if not spin else 0.06
    gather = P(base_end, hips=H(xl, y_to - A["bound"]*0.5, hip+up+0.06), pyaw=yaw*0.5, twist=tw*0.5, ptilt=4, lean=0,
               rball=V((xl-0.12, y_to - A["bound"]*0.45, 0.32)), rheel=30, lball=V((xl+0.10, y_to - A["bound"]*0.25, 0.42)), lheel=30,
               rtoe=V((0,-1,0)), ltoe=V((0,-1,0)), lkp=V((0,-1,0.3)), rkp=V((0,-1,0.3)), footup=0.5,
               rt=V((xl-0.10, y_to - A["bound"]*0.5 - 0.20, 1.45)), lt=V((xl+0.02, y_to - A["bound"]*0.5 - 0.22, 1.42)),
               rdirw=0.0, ldirw=0.0, rp=V((-0.8,0.3,-0.6)), lp=V((0.6,0.3,-0.6)), look=BATTER)
    keys.append((f_to + fb//2 + 1, gather))
    # ---------------- back foot contact: side-on (or per action), front arm high toward the batter, bowling hand by the chin
    side_dir = V((math.cos(math.radians(yaw+90)), math.sin(math.radians(yaw+90)), 0))   # toe direction for the back foot
    bfc = P(BASE, hips=H(xl+0.02, y_bfc-0.05, hip-0.02), pyaw=yaw, twist=tw, lean=-8, side=-6, ptilt=0, footup=0.8,
            rball=V((xl-0.02, y_bfc, 0)), rtoe=V((-math.sin(math.radians(yaw)), -math.cos(math.radians(yaw)) if False else 0.0, 0)) if False else rotz((0,-1,0), yaw),
            rheel=8, rkp=rotz((0,-1,0.2), yaw), lball=V((xl+0.05, y_bfc-0.75, 0.35)), lheel=30, ltoe=V((0,-1,0)), lkp=V((0,-1,0.6)),
            rt=V((xl-0.05, y_bfc-0.20, 1.52)), rdirw=0.0, rp=V((-0.8,0.6,-0.3)),
            ldir=V3(0.05,-0.55,0.85), ldirw=1.0, lp=V((0.5,0.2,-1)),
            rh=V((0,-0.2,1)), rhw=0.5, **hold, look=BATTER)
    keys.append((f_bfc, bfc))
    # bowling arm drops down and back during the delivery stride
    mid = P(bfc, hips=H(xl+0.02, (y_bfc+y_ffc)/2+0.15, hip-0.03), pyaw=yaw*0.92, lean=-4,
            lball=V((xl+0.05, y_ffc+0.25, 0.18)), lheel=15,
            rdir=arm_dir(40, tilt), rdirw=1.0, ldir=V3(0.05,-0.6,0.8), rhw=0.0)
    keys.append(((f_bfc+f_ffc)//2, mid))
    # ---------------- front foot contact: braced front leg, bowling arm still back, front arm still up
    ffc = P(bfc, hips=H(xl+0.04, y_ffc+0.48, hip-0.06), pyaw=yaw*0.75, twist=tw*0.6, lean=2, side=0,
            lball=V((xl+0.06, y_ffc, 0)), ltoe=V3(0.15,-1,0), lheel=0, lkp=V((0,-1,0.15)), rheel=35,
            rdir=arm_dir(100, tilt), rdirw=1.0, ldir=V3(0.1,-0.7,0.6), rh=arm_dir(80, tilt), rhw=0.7, rpal=V3(0,1,0.2), rpalw=0.5)
    keys.append((f_ffc, ffc))
    pre = P(ffc, hips=H(xl+0.05, y_ffc+0.30, hip-0.07), pyaw=yaw*0.5, twist=tw*0.2, lean=A["lean_rel"]*0.4, side=A.get("side_rel", 8)*0.6,
            rdir=arm_dir(160, tilt), ldir=V3(0.25,-0.5,-0.4), rh=arm_dir(140, tilt), rhw=0.8, rpal=vr["palm"].lerp(V((0,-1,0)), 0.5), rpalw=0.7, rheel=55)
    keys.append(((f_ffc+f_rel)//2, pre))
    # ---------------- release
    rel = P(ffc, hips=H(xl+0.06, y_ffc+0.10, hip-0.08), pyaw=yaw*0.22, twist=6, lean=A["lean_rel"]+vr["dlean"], side=12, rheel=70,
            rdir=arm_dir(vr["phi"], tilt), rdirw=1.0, ldir=V3(0.35,0.35,-0.85), ldirw=1.0,
            rh=arm_dir(vr["phi"]+12, tilt), rhw=0.9, rpal=vr["palm"], rpalw=1.0, **fing("r", vr["fing"]), look=BATTER)
    keys.append((f_rel, rel))
    # ---------------- follow-through: arm sweeps down and across, back leg comes through, trunk folds
    ft1 = P(rel, hips=H(xl+0.10, y_ffc-0.18, hip-0.14), pyaw=8, twist=8, lean=A["lean_rel"]+16, side=10, rheel=80,
            rball=V((xl-0.05, y_bfc-0.55, 0.20)),
            rdir=arm_dir(275, tilt*0.5), ldir=V3(0.5,0.6,-0.6), rh=arm_dir(300, tilt*0.5), rhw=0.6, rpalw=0.3, **fing("r", RELAXF))
    keys.append((f_rel+4, ft1))
    ft2 = P(ft1, hips=H(xl+0.22, y_ffc-0.55, hip-0.12), pyaw=22, twist=10, lean=A["lean_rel"]+10, side=6, rheel=20,
            rball=V((xl+0.12, y_ffc-1.20, 0)), rtoe=V3(0.3,-1,0), rkp=V((0.2,-1,0.2)),
            rt=V((xl+0.38, y_ffc-0.50, 0.70)), rdirw=0.0, rp=V((-0.3,0.4,-1)), ldir=V3(0.4,0.8,-0.4), rhw=0.0)
    keys.append((f_rel+9, ft2))
    # deceleration steps veering away from the pitch (to the bowler's left from over the wicket, right from around)
    veer = 1 if side == "over" else -1
    p_from = V((xl+0.12, y_ffc-1.20)); p_to = p_from + V((veer*(1.6 if not spin else 0.9), -(3.6 if not spin else 1.6)))
    k3, _ = run_keys(ft2, f0=f_rel+9, f1=f_rel+(40 if not spin else 30), p0=tuple(p_from), p1=tuple(p_to),
                     heading=-veer*18, period=A["period"]+3, contact=0.4, lean=10, ease="decel", hip_h=hip+0.02, arm_amp=0.12,
                     first="r", extra=dict(look=BATTER, **fing("r", RELAXF)))
    keys += [(f, P(k, rdirw=0.0, ldirw=0.0)) for f, k in k3[1:]]
    endp = place(P(STAND, look=BATTER), p_to.x, p_to.y - 0.15, -veer*10)
    keys.append((n-12, endp)); keys.append((n-1, endp))
    keys.sort(key=lambda kk: kk[0])
    ded = []
    for fr, p in keys:
        if ded and abs(ded[-1][0] - fr) < 1e-6: ded[-1] = (fr, p)
        else: ded.append((fr, p))
    if left_arm: ded = [(fr, mirror(p)) for fr, p in ded]
    meta = dict(action=action, variant=variant, side=side + " the wicket", arm="left" if left_arm else "right",
                frames=n, release_frame=f_rel+1, back_foot_contact_frame=f_bfc+1, front_foot_contact_frame=f_ffc+1,
                run_up_m=round(y0 - y_ffc, 2), start_position_m=dict(behind_crease=round(y0, 2), left=round((-xl if left_arm else xl), 2)),
                note=vr.get("note", ""))
    if meta_extra: meta.update(meta_extra)
    return n, ded, meta

CLIPS = {}
def add(name, **kw):
    CLIPS[name] = kw


# ================================================================= CLIP LIST
def family(prefix, action, arm, stock, variants, around=True):
    la = arm == "la"
    add(f"{prefix}_over_{stock}", action=action, variant=stock, side="over", left_arm=la)
    if around: add(f"{prefix}_around_{stock}", action=action, variant=stock, side="around", left_arm=la)
    for v in variants: add(f"{prefix}_over_{v}", action=action, variant=v, side="over", left_arm=la)
PACE_V = ["bouncer","yorker","outswinger","inswinger","off_cutter","leg_cutter","back_of_hand","knuckle"]
family("ra_express_fast", "express_fast", "ra", "stock", PACE_V)
family("ra_fast_medium",  "fast_medium",  "ra", "stock", PACE_V)
family("ra_medium",       "medium",       "ra", "stock", PACE_V)
family("ra_slow_medium",  "slow_medium",  "ra", "stock", ["back_of_hand"])
family("la_fast",         "express_fast", "la", "stock", PACE_V)
family("la_fast_medium",  "fast_medium",  "la", "stock", [])
family("ra_slinger",      "slinger",      "ra", "stock", ["yorker","inswinger","bouncer","back_of_hand"])
family("ra_off_spin",     "off_spin",     "ra", "off_break", ["doosra","carrom","arm_ball"])
family("la_orthodox",     "off_spin",     "la", "off_break", ["doosra","carrom","arm_ball"])
family("ra_leg_spin",     "leg_spin",     "ra", "leg_break", ["googly","flipper","top_spinner"])
family("la_chinaman",     "leg_spin",     "la", "leg_break", ["googly","flipper","top_spinner"])
add("mystery_side_arm_over", action="side_arm", variant="off_break", side="over")

if __name__ == "__main__":
    import importlib
    names = sys.argv[1:]
    data, metas = {}, {}
    for nm in names:
        n, keys, meta = bowl(nm, **CLIPS[nm])
        frames = make_field(nm, n, keys, lift=True, reach=None)
        data[nm] = frames; metas[nm] = dict(file=nm+".fbx", **meta); print(nm, "ok", n, flush=True)
    tag = "part" if len(names) < len(CLIPS) else "all"
    pickle.dump(data, open(f_os.path.join(APL_WORK, "bw_frames_{tag}.pkl"),"wb")); json.dump(metas, open(f_os.path.join(APL_WORK, "bw_meta_{tag}.json"),"w"), indent=1)

