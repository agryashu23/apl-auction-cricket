"""
Bowling actions rebuilt phase by phase from fast-bowling / spin biomechanics (2026-10 rebuild of
lib/bowling.py and lib/bowling2.py, same rig, same channels, same file names and metadata shape).

World (the packs' bowling convention): Z up, the bowler runs toward -Y, his popping crease at y=0, his
stumps at y=+1.22, the batter's stumps at y=-18.9; x = the bowler's left. ra_ right-arm, la_ = mirror.

Phases, each driven by measured targets rather than free key poses:
  mark      standing at the top of the mark (the exact MARK pose of bowler_after.py), a small rock forward
  run-up    v(t) = vmax(1 - exp(-t/tau)): the approach builds over the last 8-10 strides; cadence and
            contact time follow the speed (1.9 + 0.27 v steps/s; 0.13 + 0.25 e^(-v/2.2) s on the ground);
            planted feet never slide; the ball comes up to the chest over the last strides
  bound     take-off from the LEFT foot (right-armer), ballistic hip flight (z = z0 + vz t - g t^2/2),
            right knee drives up, hands gather at the chin, the body turns side-on in the air
  BFC       back foot lands at the action's angle (side-on: parallel to the crease; front-on: down the
            pitch), back knee ~150-158 deg, trunk leaning back, front knee lifted, front arm high
  FFC       front foot lands with the knee at the action's angle (pace 150-166 deg); hip height is
            solved from that knee angle, so the angle is exact
  release   front knee braced to its release angle (express 176, spin ~172), bowling arm a few degrees
            past vertical, trunk flexed forward ~25-35 deg and laterally away from the bowling arm,
            front arm pulled down and in; ball leaves 0.2-0.4 m ahead of the front foot
  follow    arm sweeps down across to the left hip, trunk folds to 40-55 deg, back leg comes through and
            lands, deceleration steps veer off the pitch (to the left over the wicket, right around)
  stop      ends in field_clips.STAND facing the batter (what ra_turn_and_walk_back_to_mark starts from)

    motion.ps1 gen drivers\\bowling_real.py fast                 # the 6 sprint actions (bowling_fast)
    motion.ps1 gen drivers\\bowling_real.py main [names...]      # the 72 bowling clips (bowling)
    motion.ps1 gen drivers\\bowling_real.py one <name>           # one clip from either set
"""
import os, sys, math, json
HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(os.path.dirname(HERE), "lib")
if LIB not in sys.path: sys.path.insert(0, LIB)
import bpy
from mathutils import Vector, Matrix, Quaternion
import mixamo_motions as mm
import fielding
from fielding import V, C, PFX, R, P, BASE, H, HIP0, fing, RELAXF, FieldRig, load_rig, foot_ankle, sample
import bowling as BW
from bowling import mirror, HOLD_BALL, FLICK, KNUCKLE
from field_clips import STAND

TARGET = V((0.0, -18.9, 0.9))
G = 9.81
THIGH, SHIN = 0.443, 0.446                       # rig: RightUpLeg->RightLeg, RightLeg->RightFoot (m)
LMAX = THIGH + SHIN
HIPJ = {"r": V((-0.082, 0.016, -0.068)), "l": V((0.082, 0.016, -0.068))}   # hip joints from the Hips head (rest)
UP = V((0, 0, 1))
def V3(*a): return V(a).normalized()
def fwd(h): r = math.radians(h); return V((math.sin(r), -math.cos(r), 0))
def left(h): r = math.radians(h); return V((math.cos(r), math.sin(r), 0))
def rotz(v, deg): return R('Z', deg) @ V(v)
def clamp(x, a, b): return max(a, min(b, x))
def smooth01(x): x = clamp(x, 0.0, 1.0); return x*x*(3 - 2*x)
def other(f): return "r" if f == "l" else "l"

# ---------------------------------------------------------------- curves
def pchip(pts):
    """monotone cubic (Fritsch-Carlson) through [(t, value)], value float or Vector; clamps at the ends"""
    pts = sorted(pts, key=lambda p: p[0])
    merged = []
    for q in pts:                                  # keys closer than half a frame: the later one wins
        if merged and q[0] - merged[-1][0] < 0.5: merged[-1] = q
        else: merged.append(q)
    pts = merged
    if isinstance(pts[0][1], Vector):
        fs = [pchip([(t, v[i]) for t, v in pts]) for i in range(len(pts[0][1]))]
        return lambda t: V(tuple(f(t) for f in fs))
    ts = [float(p[0]) for p in pts]; vs = [float(p[1]) for p in pts]; n = len(ts)
    if n == 1: return lambda t: vs[0]
    d = [(vs[i+1] - vs[i])/(ts[i+1] - ts[i]) for i in range(n-1)]
    m = [0.0]*n; m[0] = d[0]; m[-1] = d[-1]
    for i in range(1, n-1):
        if d[i-1]*d[i] <= 0: m[i] = 0.0
        else:
            h0, h1 = ts[i] - ts[i-1], ts[i+1] - ts[i]
            w1, w2 = 2*h1 + h0, h1 + 2*h0
            m[i] = (w1 + w2)/(w1/d[i-1] + w2/d[i])
    def f(t):
        if t <= ts[0]: return vs[0]
        if t >= ts[-1]: return vs[-1]
        i = max(j for j in range(n-1) if ts[j] <= t)
        h = ts[i+1] - ts[i]; s = (t - ts[i])/h
        return ((2*s**3 - 3*s**2 + 1)*vs[i] + (s**3 - 2*s**2 + s)*h*m[i] + (-2*s**3 + 3*s**2)*vs[i+1] + (s**3 - s**2)*h*m[i+1])
    return f
def herm(t0, v0, s0, t1, v1, s1):
    """cubic Hermite between (t0, v0, slope s0) and (t1, v1, slope s1); slopes per frame"""
    def f(t):
        h = t1 - t0; s = clamp((t - t0)/h, 0, 1)
        return (2*s**3 - 3*s**2 + 1)*v0 + (s**3 - 2*s**2 + s)*h*s0 + (-2*s**3 + 3*s**2)*v1 + (s**3 - s**2)*h*s1
    return f
def gsmooth(vals, sigma):
    out = []
    w = max(1, int(3*sigma))
    for i in range(len(vals)):
        acc = 0.0; tot = 0.0
        for j in range(max(0, i-w), min(len(vals), i+w+1)):
            k = math.exp(-0.5*((j - i)/sigma)**2); acc += k*vals[j]; tot += k
        out.append(acc/tot)
    return out

# ---------------------------------------------------------------- leg geometry (same maths as FieldRig)
def pelvis(p): return R('Z', p["pyaw"]) @ R('X', p["ptilt"]) @ R('Y', p["pside"])
def hips_w(p): return V(p["hips"]) + V((0, 0, HIP0))
def hip_joint(p, k): return hips_w(p) + pelvis(p) @ HIPJ[k]
def ankle(p, k): return foot_ankle(V(p[k+"ball"]), V(p[k+"toe"]), p[k+"heel"])
def leg_len(knee_deg):
    return math.sqrt(THIGH**2 + SHIN**2 - 2*THIGH*SHIN*math.cos(math.radians(knee_deg)))
def z_for_knee(p, k, knee_deg):
    """hips height (world) that gives leg k exactly knee_deg (included angle, 180 = straight)"""
    A = ankle(p, k); off = pelvis(p) @ HIPJ[k]; J = hips_w(p) + off
    dxy = (V((J.x, J.y, 0)) - V((A.x, A.y, 0))).length; L = leg_len(knee_deg)
    return A.z + math.sqrt(max(0.0, L*L - dxy*dxy)) - off.z

# ---------------------------------------------------------------- gait model
def cad(v): return 1.9 + 0.27*v                    # steps per second
def tcon(v): return 0.11 + 0.26*math.exp(-v/2.0)    # ground contact (s): 0.12 s at 6.5 m/s, 0.17 s at 3 m/s
def zmean(v): return 1.012 - 0.0095*v                # running hips sit lower (knee flexion ~140 at mid-stance)
def zamp(v): return 0.006 + 0.006*v

def foot_state(plants, fo, t, v, heel_up=None):
    """plants: [foot, td, lo, ball(V), toe(V)]; stance = fixed ball, swing = arc (from fielding.run_keys)"""
    mine = sorted([p for p in plants if p[0] == fo], key=lambda p: p[1])
    hu = heel_up if heel_up is not None else 30 + 4*v
    for j, (_, td, lo, pos, toe) in enumerate(q[:5] for q in mine):
        if td - 1e-6 <= t <= lo + 1e-6:
            if td < -1e8:
                return dict(ball=V(pos), toe=V(toe), heel=22*smooth01(1 - (lo - t)/4.0), stance=True, s=0.5)
            s = (t - td)/max(1e-6, lo - td)
            return dict(ball=V(pos), toe=V(toe), heel=hu*max(0.0, (s - 0.5)/0.5)**1.5, stance=True, s=s)
        if j + 1 < len(mine) and lo < t < mine[j+1][1]:
            nx = mine[j+1]; s = (t - lo)/(nx[1] - lo)
            u = s*s*s*(s*(6*s - 15) + 10)
            sh = 0.07 + 0.055*v
            h = sh*math.sin(math.pi*min(1.0, s*1.08))**1.2*(1 - 0.25*s)
            heel = hu*(1 - s)**1.5 + 40*math.sin(math.pi*s)*(1 - s) - 8*s*s
            return dict(ball=V(pos).lerp(V(nx[3]), u) + V((0, 0, max(0.0, h))), toe=V(toe).lerp(V(nx[4]), u).normalized(),
                        heel=heel, stance=False, s=s)
    last = mine[-1]
    return dict(ball=V(last[3]), toe=V(last[4]), heel=0.0, stance=True, s=0.5)

def run_arms(p, hw, z, dl, v, heading=0.0, amp_k=1.0):
    F, L = fwd(heading), left(heading)
    amp = (0.08 + 0.022*v)*amp_k
    p["rt"] = hw + L*(-0.20) + F*(0.06 + dl*amp) + V((0, 0, z + 0.12 + 0.10*max(0, dl)*amp_k))
    p["lt"] = hw + L*(0.20) + F*(0.06 - dl*amp) + V((0, 0, z + 0.12 + 0.10*max(0, -dl)*amp_k))
    p["rp"] = L*(-0.4) - F + V((0, 0, -0.4)); p["lp"] = L*0.4 - F + V((0, 0, -0.4))
    p["rh"] = F + V((0, 0, 0.3)); p["lh"] = F + V((0, 0, 0.3)); p["rhw"] = 0.3; p["lhw"] = 0.3
    p["rdirw"] = 0.0; p["ldirw"] = 0.0

# ---------------------------------------------------------------- the actions (right-arm values; la_ = mirror)
# yaw pairs are (pelvis, shoulders) in degrees: 0 = chest square to the batter, -90 = side-on (left side to the batter).
# knees are included angles (180 = straight). lean = spine flexion toward the batter on top of the pelvis tilt.
PACE = dict(kind="pace", t_go=8, ffx=0.0, bfc_back=0.15, d_ffc=0.58, d_rel=0.22, v_rel=2.6, ftl=10, ft_step=1.0, carry=0.65,
            ptilt=dict(bfc=-2, ffc=3, rel=13, ft=16), side=dict(bfc=-6, ffc=0, rel=26, ft=12),
            lean=dict(bfc=-12, ffc=-2, rel=36, ft=54), az=dict(bfc=10, ffc=12), phi_bfc=15,
            ft_steps=[(9, 0.95), (10, 0.75), (11, 0.50), (13, 0.25)], gather_up=0.42)
SPIN = dict(kind="spin", t_go=8, ffx=-0.04, bfc_back=0.10, d_ffc=0.45, d_rel=0.12, v_rel=1.6, ftl=9, ft_step=0.75, carry=0.85,
            ptilt=dict(bfc=0, ffc=3, rel=13, ft=16), side=dict(bfc=-4, ffc=0, rel=14, ft=8),
            lean=dict(bfc=-6, ffc=0, rel=40, ft=54), az=dict(bfc=6, ffc=8), phi_bfc=20,
            ft_steps=[(10, 0.60), (12, 0.30)], gather_up=0.45)
def act(base, **kw):
    a = json.loads(json.dumps(base)); [a.__setitem__(k, v) for k, v in kw.items()]; return a

ACTIONS = {
 # ---- sprint actions (bowling_fast): fixed 4.9 s clip, release at frame 89 as before
 "side_on":  act(PACE, run_frames=60, vmax=7.2, tau=0.75, flight=11, stride=1.52, bfc_ffc=6, ffc_rel=3, back_toe=-85,
                 yaw=dict(bfc=(-85, -95), ffc=(-62, -84), rel=(-12, 8), ft=(22, 48)), knee=dict(bfc=155, ffc=166, rel=176, ft=160),
                 tilt=6, phi_rel=204, front_bfc=(0.04, -0.45, 0.89), front_ffc=(0.06, -0.78, 0.62), ffx=-0.03,
                 desc="Classical side-on: back foot lands parallel to the crease, hips and shoulders side-on, front arm reaching high at the batter, bowling arm close to vertical", kmh=140),
 "front_on": act(PACE, run_frames=62, vmax=7.0, tau=0.75, flight=10, stride=1.40, bfc_ffc=5, ffc_rel=3, back_toe=-15,
                 yaw=dict(bfc=(-22, -28), ffc=(-14, -26), rel=(0, 12), ft=(18, 42)), knee=dict(bfc=153, ffc=162, rel=172, ft=158),
                 tilt=14, phi_rel=203, front_bfc=(0.25, -0.32, 0.91), front_ffc=(0.25, -0.70, 0.66), ffx=0.04,
                 az=dict(bfc=30, ffc=35), side=dict(bfc=-4, ffc=2, rel=16, ft=10),
                 desc="Front-on (chest-on): back foot points down the pitch, chest faces the batter at back-foot contact, shorter stride, arm a little wider of vertical", kmh=136),
 "slinger":  act(PACE, run_frames=61, vmax=7.3, tau=0.75, flight=10, stride=1.45, bfc_ffc=6, ffc_rel=3, back_toe=-40,
                 yaw=dict(bfc=(-42, -96), ffc=(-34, -88), rel=(0, 14), ft=(24, 50)), knee=dict(bfc=152, ffc=160, rel=170, ft=156),
                 tilt=52, phi_rel=212, front_bfc=(0.62, -0.62, 0.48), front_ffc=(0.55, -0.80, 0.20), ffx=0.02,
                 az=dict(bfc=20, ffc=28), side=dict(bfc=-8, ffc=-4, rel=4, ft=8), lean=dict(bfc=-10, ffc=-2, rel=34, ft=52),
                 desc="Slinger: mixed action with big shoulder counter-rotation, front arm swung out wide, low round-arm whip, release about 25 cm lower", kmh=142),
 # ---- the main pace set (bowling): full run-ups from the mark
 "express_fast": act(PACE, run=22.0, vmax=7.3, tau=1.15, flight=12, stride=1.55, bfc_ffc=6, ffc_rel=3, back_toe=-85,
                 yaw=dict(bfc=(-85, -96), ffc=(-62, -85), rel=(-12, 8), ft=(22, 48)), knee=dict(bfc=155, ffc=166, rel=176, ft=160),
                 tilt=6, phi_rel=205, front_bfc=(0.04, -0.45, 0.89), front_ffc=(0.06, -0.78, 0.62), ffx=-0.03),
 "fast_medium": act(PACE, run=16.0, vmax=6.4, tau=1.0, flight=10, stride=1.45, bfc_ffc=5, ffc_rel=3, back_toe=-30,
                 yaw=dict(bfc=(-32, -72), ffc=(-24, -68), rel=(-2, 12), ft=(20, 45)), knee=dict(bfc=153, ffc=158, rel=168, ft=156),
                 tilt=10, phi_rel=204, front_bfc=(0.12, -0.42, 0.90), front_ffc=(0.15, -0.75, 0.64), az=dict(bfc=16, ffc=20)),
 "medium":      act(PACE, run=11.0, vmax=5.4, tau=0.95, flight=9, stride=1.35, bfc_ffc=5, ffc_rel=4, back_toe=-20, v_rel=2.2,
                 yaw=dict(bfc=(-24, -34), ffc=(-14, -28), rel=(0, 10), ft=(16, 40)), knee=dict(bfc=150, ffc=152, rel=160, ft=150),
                 tilt=12, phi_rel=203, front_bfc=(0.22, -0.35, 0.91), front_ffc=(0.22, -0.72, 0.66), az=dict(bfc=26, ffc=30), ffx=0.03,
                 ft_steps=[(10, 0.95), (11, 0.65), (13, 0.30)]),
 "slow_medium": act(PACE, run=7.0, vmax=4.4, tau=0.8, flight=8, stride=1.25, bfc_ffc=6, ffc_rel=4, back_toe=-55, v_rel=2.0, d_ffc=0.46,
                 yaw=dict(bfc=(-58, -74), ffc=(-40, -60), rel=(-4, 8), ft=(16, 38)), knee=dict(bfc=152, ffc=152, rel=160, ft=150),
                 tilt=12, phi_rel=202, front_bfc=(0.10, -0.42, 0.90), front_ffc=(0.12, -0.75, 0.64), az=dict(bfc=14, ffc=18),
                 lean=dict(bfc=-8, ffc=-2, rel=32, ft=46), ft_steps=[(10, 0.80), (12, 0.45), (13, 0.22)]),
 "slinger_main": act(PACE, run=18.0, vmax=6.9, tau=1.05, flight=10, stride=1.45, bfc_ffc=6, ffc_rel=3, back_toe=-40,
                 yaw=dict(bfc=(-42, -96), ffc=(-34, -88), rel=(0, 14), ft=(24, 50)), knee=dict(bfc=152, ffc=160, rel=170, ft=156),
                 tilt=52, phi_rel=212, front_bfc=(0.62, -0.62, 0.48), front_ffc=(0.55, -0.80, 0.20), ffx=0.02,
                 az=dict(bfc=20, ffc=28), side=dict(bfc=-8, ffc=-4, rel=4, ft=8), lean=dict(bfc=-10, ffc=-2, rel=34, ft=52)),
 # ---- spin: short approach ~3 m/s, small hop, high front arm, pivot over a braced front leg
 "off_spin":    act(SPIN, run=6.0, vmax=3.6, tau=0.7, flight=6, stride=1.00, bfc_ffc=7, ffc_rel=5, back_toe=-85,
                 yaw=dict(bfc=(-80, -96), ffc=(-64, -90), rel=(-14, 10), ft=(40, 78)), knee=dict(bfc=158, ffc=162, rel=168, ft=156),
                 tilt=8, phi_rel=206, front_bfc=(0.02, -0.28, 0.96), front_ffc=(0.04, -0.62, 0.78)),
 "leg_spin":    act(SPIN, run=7.0, vmax=3.8, tau=0.7, flight=7, stride=1.05, bfc_ffc=7, ffc_rel=5, back_toe=-70,
                 yaw=dict(bfc=(-66, -92), ffc=(-50, -80), rel=(-6, 14), ft=(40, 72)), knee=dict(bfc=156, ffc=158, rel=165, ft=154),
                 tilt=12, phi_rel=203, front_bfc=(0.12, -0.34, 0.93), front_ffc=(0.14, -0.66, 0.74), az=dict(bfc=10, ffc=12)),
 "side_arm":    act(SPIN, run=6.0, vmax=3.6, tau=0.7, flight=6, stride=1.00, bfc_ffc=7, ffc_rel=5, back_toe=-80,
                 yaw=dict(bfc=(-76, -96), ffc=(-60, -88), rel=(-10, 14), ft=(40, 74)), knee=dict(bfc=158, ffc=160, rel=166, ft=156),
                 tilt=48, phi_rel=200, front_bfc=(0.40, -0.45, 0.80), front_ffc=(0.40, -0.75, 0.50), az=dict(bfc=14, ffc=20),
                 side=dict(bfc=-6, ffc=-2, rel=4, ft=6)),
}

# release variants: change only the wrist / fingers / release angle (and a touch of trunk) around release
VARIANTS = {
 "stock":        dict(dphi=0,  dlean=0,  palm=V3(0, -1, 0.05),     fing=FLICK,   rtw=0),
 "bouncer":      dict(dphi=8,  dlean=6,  palm=V3(0, -1, -0.25),    fing=FLICK,   rtw=0,   note="later release, hit-the-deck: the arm comes further over and the trunk drives down harder"),
 "yorker":       dict(dphi=-7, dlean=-3, palm=V3(0, -1, 0.30),     fing=FLICK,   rtw=0,   note="earlier, higher release, wrist held back behind the ball for a full length"),
 "outswinger":   dict(dphi=0,  dlean=0,  palm=V3(0.35, -1, 0.05),  fing=FLICK,   rtw=-8,  note="seam and wrist angled toward the slips, fingers behind the ball"),
 "inswinger":    dict(dphi=0,  dlean=0,  palm=V3(-0.35, -1, 0.05), fing=FLICK,   rtw=8,   note="seam and wrist angled toward fine leg, the arm a touch more round"),
 "off_cutter":   dict(dphi=-1, dlean=1,  palm=V3(-0.85, -0.5, 0),  fing=FLICK,   rtw=35,  note="fingers drag down the right side of the ball, wrist rolls toward leg"),
 "leg_cutter":   dict(dphi=-1, dlean=1,  palm=V3(0.85, -0.5, 0),   fing=FLICK,   rtw=-35, note="fingers cut across the ball the other way, wrist rolls toward off"),
 "back_of_hand": dict(dphi=-2, dlean=0,  palm=V3(0, 1, 0.1),       fing=FLICK,   rtw=60,  note="hand flipped at the top, back of the hand faces the batter (slower ball)"),
 "knuckle":      dict(dphi=0,  dlean=0,  palm=V3(0, -1, 0.05),     fing=KNUCKLE, rtw=0,   note="ball pushed out on bent knuckles (slower ball)"),
 # spin
 "off_break":    dict(dphi=0,  dlean=0,  palm=V3(-0.6, -0.8, 0.1), fing=dict(T=0.2, I=0.6, M=0.3, R=0.6, P=0.7), rtw=55, note="palm toward the batter, fingers rip clockwise"),
 "doosra":       dict(dphi=-2, dlean=0,  palm=V3(0.3, 1, 0.2),     fing=dict(T=0.2, I=0.4, M=0.4, R=0.6, P=0.7), rtw=-70, note="wrist cocked, back of the hand faces the batter"),
 "carrom":       dict(dphi=0,  dlean=0,  palm=V3(-0.2, -1, 0.1),   fing=dict(T=0.1, I=0.2, M=0.95, R=0.6, P=0.7), rtw=20, note="flicked out between thumb and bent middle finger"),
 "arm_ball":     dict(dphi=3,  dlean=0,  palm=V3(0, -1, 0.0),      fing=FLICK,   rtw=0,   note="hand behind the ball, goes on with the arm"),
 "leg_break":    dict(dphi=0,  dlean=0,  palm=V3(0.75, -0.6, 0.1), fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), rtw=-60, note="wrist cocked inward, ring finger rips the ball"),
 "googly":       dict(dphi=-2, dlean=1,  palm=V3(-0.2, 1, 0.3),    fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), rtw=-110, note="wrist dropped, back of the hand faces the batter"),
 "flipper":      dict(dphi=8,  dlean=0,  palm=V3(0, -0.3, 1),      fing=dict(T=0.1, I=0.3, M=0.8, R=0.7, P=0.7), rtw=10, note="squeezed out from under the hand, palm up, hand toward the target"),
 "top_spinner":  dict(dphi=0,  dlean=0,  palm=V3(1, -0.2, 0.1),    fing=dict(T=0.3, I=0.4, M=0.4, R=0.85, P=0.9), rtw=-80, note="side of the hand toward the batter, seam rolls forward"),
}

def armvec(phi, tilt=0.0, az=0.0):
    """bowling-arm direction: phi 0 = straight down, 90 = back toward the run-up, 180 = vertical, 270 = at the batter;
    az turns the back-swing plane out to the bowler's right (+) ; tilt leans the arc out from vertical (round-arm)"""
    r = math.radians(phi)
    d = V((0, math.sin(r), -math.cos(r)))
    return (R('Y', -tilt) @ (R('Z', az) @ d)).normalized()

MARKP = dict(rt=V((-0.08, -0.22, 1.12)), lt=V((0.08, -0.22, 1.10)), rp=V((-0.6, 0.5, -1)), lp=V((0.6, 0.5, -1)),
             rh=V((0, -0.4, 1)), rhw=0.6, lh=V((0, -0.4, 1)), lhw=0.6, rpal=V((1, 0, 0)), rpalw=0.6, lpal=V((-1, 0, 0)), lpalw=0.6)

# ---------------------------------------------------------------- one delivery
def build(aname, side="over", variant="stock", T0_fixed=None):
    A = ACTIONS[aname]; vr = VARIANTS[variant]; spin = A["kind"] == "spin"
    xl = 0.30 if side == "over" else -0.48
    veer = 1 if side == "over" else -1
    y_ffc = -0.04
    y_bfc = y_ffc + A["stride"]
    xb = xl - 0.02; xf = xl + A["ffx"]
    t_go = A["t_go"]; vmax, tau = A["vmax"], A["tau"]
    # ---- timing
    def Dt(t): u = max(0.0, t - t_go)/30.0; return vmax*(u - tau*(1 - math.exp(-u/tau)))
    if "run_frames" in A: T0 = t_go + A["run_frames"]; Drun = Dt(T0)
    else:
        Drun = A["run"]; lo, hi = t_go + 1.0, t_go + 600.0
        for _ in range(60):
            mid = 0.5*(lo + hi); (lo, hi) = (mid, hi) if Dt(mid) < Drun else (lo, mid)
        T0 = int(round(hi))
    k = Drun/Dt(T0)
    D = lambda t: k*Dt(t)
    vel = lambda t: k*vmax*(1 - math.exp(-max(0.0, t - t_go)/30.0/tau))
    acc = lambda t: k*vmax/tau*math.exp(-max(0.0, t - t_go)/30.0/tau) if t >= t_go else 0.0
    Fl, bf, fr = A["flight"], A["bfc_ffc"], A["ffc_rel"]
    BFC = T0 + Fl; FFC = BFC + bf; REL = FFC + fr; FTL = REL + A["ftl"]
    v_to = vel(T0); vb = 0.92*v_to
    yh_bfc = y_bfc + A["bfc_back"]          # hips still behind the back foot as it lands (trunk leaning back)
    yh_T0 = yh_bfc + vb*Fl/30.0
    ymark = yh_T0 + Drun
    hxy = lambda t: V((xl, ymark - D(t), 0))
    # ---- approach footsteps, planned back from the take-off (LEFT foot leaves the ground at T0)
    plants = []
    fo, lo_ = "l", float(T0); td = lo_ - tcon(v_to)*30*1.25
    while True:
        mid = 0.5*(td + lo_)
        plants.append([fo, td, lo_, hxy(td + 0.5*(lo_ - td)) + V((0.075 if fo == "l" else -0.075, -0.03, 0)), V3(0.04 if fo == "l" else -0.04, -1, 0)])
        td2 = td - 30.0/cad(vel(td))
        if td2 < t_go + 5: break
        lo2 = td2 + tcon(vel(td2))*30
        fo = other(fo); td, lo_ = td2, lo2
    plants.reverse()
    nsteps_run = len(plants)
    mark_feet = {"l": V((xl + 0.15, ymark - 0.15, 0)), "r": V((xl - 0.15, ymark + 0.0, 0))}
    init = []
    for f_ in ("l", "r"):
        nx = next(p for p in plants if p[0] == f_)
        if f_ == plants[0][0]: lo0 = t_go
        else:
            vv = vel(nx[1]); lo0 = max(t_go + 2, nx[1] - 30*(2.0/cad(vv) - tcon(vv)))
        init.append([f_, -1e9, lo0, mark_feet[f_], V3(0.1 if f_ == "l" else -0.1, -1, 0)])
    plants = init + plants
    lastR = max((p for p in plants if p[0] == "r"), key=lambda p: p[1])
    lastL = max((p for p in plants if p[0] == "l"), key=lambda p: p[1])
    loR = lastR[2]; tdL = lastL[1]
    # ---- follow-through footsteps: [foot, td, lo, ball, toe, mid-line point]; the back leg lands first at FTL
    x_ftl = xl + veer*0.22; y_ftl = y_ffc - A["ft_step"]
    h0 = veer*14.0
    m = V((x_ftl, y_ftl, 0)) + left(h0)*0.08
    ftp = [["r", FTL, None, V((x_ftl, y_ftl, 0)), V3(veer*0.25, -1, 0), V(m)]]
    t_ = FTL; fo = "r"
    steps = A["ft_steps"]
    for i, (dt, ln) in enumerate(steps):
        last = i == len(steps) - 1
        h = h0 + (veer*34 - h0)*((i + 1)/len(steps))
        fo = other(fo); t_ += dt
        if not last:
            m = m + fwd(h)*ln
            foot = m + left(h)*(0.10 if fo == "l" else -0.10)
            ftp.append([fo, t_, None, V((foot.x, foot.y, 0)), V3(math.sin(math.radians(h)), -math.cos(math.radians(h)), 0), V(m)])
        else:                                   # closing step: feet side by side, square to the batter
            prv = ftp[-1]
            mid_end = V(prv[3]) + V((0.17 if prv[0] == "r" else -0.17, -ln*0.4, 0))
            prv[3] = V(prv[3]); prv[4] = V3(0.1 if prv[0] == "l" else -0.1, -1, 0)
            foot = mid_end + V((0.17 if fo == "l" else -0.17, 0, 0))
            ftp.append([fo, t_, 1e9, V((foot.x, foot.y, 0)), V3(0.1 if fo == "l" else -0.1, -1, 0), V(mid_end)])
    t_end = t_ + 8
    nF = int(t_end) + 12
    for i, p in enumerate(ftp[:-1]):            # lift-off a little after the next foot lands (double support as he slows)
        p[2] = ftp[i+1][1] + (1.0 + 1.5*i)
    ftp[-2][2] = 1e9                            # the foot the closing step joins stays down
    end_hips = (V(ftp[-1][3]) + V(ftp[-2][3]))*0.5

    # ================================================================ posture tracks (delivery)
    Y = A["yaw"]; KN = A["knee"]; LN = dict(A["lean"]); PT = A["ptilt"]; SD = A["side"]
    LN["rel"] = LN["rel"] + vr["dlean"]
    pel = pchip([(T0, 0.0), (T0 + Fl*0.5, Y["bfc"][0]*0.45), (BFC - 2, Y["bfc"][0]*0.9), (BFC, Y["bfc"][0]), (FFC, Y["ffc"][0]),
                 (REL, Y["rel"][0]), (REL + 4, Y["ft"][0]*0.8), (FTL, Y["ft"][0])])
    shy = pchip([(T0, 0.0), (T0 + Fl*0.5, Y["bfc"][1]*0.55), (BFC - 2, Y["bfc"][1]*0.95), (BFC, Y["bfc"][1]), (BFC + bf*0.5, (Y["bfc"][1] + Y["ffc"][1])*0.5 - 2),
                 (FFC, Y["ffc"][1]), (REL, Y["rel"][1]), (REL + 4, Y["ft"][1]*0.85), (FTL, Y["ft"][1])])
    ptl = pchip([(T0, 4.0), (T0 + Fl*0.5, 1.0), (BFC, PT["bfc"]), (FFC, PT["ffc"]), (REL, PT["rel"]), (REL + 4, PT["ft"]), (FTL, PT["ft"])])
    lean = pchip([(T0, 4.0), (T0 + Fl*0.5, -2.0), (BFC, LN["bfc"]), (FFC, LN["ffc"]), (FFC + fr*0.5, (LN["ffc"] + LN["rel"])*0.5),
                  (REL, LN["rel"]), (REL + 3, LN["ft"]*0.85), (REL + 6, LN["ft"]), (FTL, LN["ft"]*0.95)])
    sde = pchip([(T0, 0.0), (BFC, SD["bfc"]), (FFC, SD["ffc"]), (REL, SD["rel"]), (REL + 4, SD["ft"]), (FTL, SD["ft"]*0.6)])
    clv = pchip([(T0, 0.0), (FFC, 4.0), (REL, 14.0), (REL + 3, 6.0), (FTL, 0.0)])
    # ft posture (after FTL): heading-led, lean decays
    ft_end = t_end
    ft_pel = pchip([(FTL, Y["ft"][0]), (FTL + 12, veer*28.0), (ftp[-2][1], veer*18.0), (ft_end, 0.0)])
    ft_tw = pchip([(FTL, Y["ft"][1] - Y["ft"][0]), (FTL + 10, 8.0*veer), (ft_end, 0.0)])
    ft_lean = pchip([(FTL, LN["ft"]*0.95), (FTL + 8, 22.0), (FTL + 20, 10.0), (ft_end, 2.0)])
    ft_ptl = pchip([(FTL, PT["ft"]), (FTL + 14, 4.0), (ft_end, 0.0)])

    # ================================================================ feet tracks (delivery)
    def fkey(t, ball, heel, toe, kp): return (t, dict(ball=V(ball), heel=float(heel), toe=V(toe).normalized(), kp=V(kp)))
    toe_b = rotz((0, -1, 0), A["back_toe"]); toe_b2 = rotz((0, -1, 0), A["back_toe"]*0.7)
    kp_b = toe_b + V((0, 0, 0.25))
    st_R = foot_state(plants, "r", loR, v_to)
    ap = lambda t: hxy(t) if t <= T0 else V((xl, yh_T0 - vb*(t - T0)/30.0, 0))
    # back (right) foot: knee drive through the bound, lands at BFC, pivots, drags, comes through to FTL
    up_mul = A["gather_up"]
    rk = [fkey(loR, st_R["ball"], st_R["heel"], st_R["toe"], V((0, -1, 0.3))),
          fkey(T0, ap(T0) + V((-0.07, -0.12, 0.30)), 45, V3(0, -1, 0), V((0, -1, 0.6))),
          fkey(T0 + Fl*0.45, ap(T0 + Fl*0.45) + V((-0.07, -0.22, up_mul)), 25, V3(-0.2, -1, 0), V((0, -1, 0.6))),
          fkey(BFC - 3, V((xb, y_bfc - 0.08, 0.17)), 10, rotz((0, -1, 0), A["back_toe"]*0.8), kp_b),
          fkey(BFC - 1, V((xb, y_bfc - 0.02, 0.05)), 4, toe_b, kp_b),
          fkey(BFC, V((xb, y_bfc, 0)), 3, toe_b, kp_b)]
    loB = BFC + int(round(bf*(0.55 if not spin else 0.7)))      # back foot leaves before FFC; the toe then drags
    rk += [fkey(loB, V((xb, y_bfc, 0)), 50, toe_b2, toe_b2 + V((0, -0.6, 0.2)))]
    rk += [fkey(FFC, V((xb + 0.02, y_bfc - 0.22, 0.05)), 70, toe_b2, V((0.2, -1, 0.1))),
           fkey(REL, V((xb + 0.05, y_bfc - (0.50 if not spin else 0.36), 0.10)), 82, V3(0.1, -1, 0), V((0.4, -1, 0.2))),
           fkey(REL + 3, V((xl + veer*0.05, y_ffc + 0.70, 0.28)), 70, V3(0.1, -1, 0), V((0.3, -1, 0.3))),
           fkey(REL + 6, V((x_ftl, y_ffc + 0.0, 0.32)), 30, V3(veer*0.2, -1, 0), V((0.2, -1, 0.5))),
           fkey(FTL - 2, V((x_ftl, y_ftl + 0.16, 0.10)), 0, V3(veer*0.25, -1, 0), V((0.2, -1, 0.4))),
           fkey(FTL, V((x_ftl, y_ftl, 0)), 0, V3(veer*0.25, -1, 0), V((0.2, -1, 0.2)))]
    # front (left) foot: leaves at take-off, trails, comes through high (front knee lift at BFC), lands at FFC
    st_L = foot_state(plants, "l", T0, v_to)
    loF = REL + (6 if not spin else 7)            # pushes off the front foot into the follow-through
    lk = [fkey(T0, st_L["ball"], st_L["heel"], st_L["toe"], V((0, -1, 0.3)))]
    if Fl >= 9:      # a long bound: the take-off leg trails high behind, then scissors through
        lk += [fkey(T0 + 3, ap(T0 + 3) + V((0.08, 0.42, 0.26)), 75, V3(0, -1, 0), V((0, -1, 0.5))),
               fkey(T0 + Fl*0.55, ap(T0 + Fl*0.55) + V((0.08, 0.30, 0.42)), 70, V3(0, -1, 0), V((0, -1, 0.5))),
               fkey(BFC - 2, V((xf + 0.04, yh_bfc - 0.18, 0.40)), 45, V3(0.05, -1, 0), V((0, -1, 0.9)))]
    else:            # a short spinner's hop: the leg comes straight through under the hips
        lk += [fkey(T0 + Fl*0.5, ap(T0 + Fl*0.5) + V((0.08, 0.10, 0.32)), 55, V3(0, -1, 0), V((0, -1, 0.6)))]
    lk += [fkey(BFC,V((xf + 0.03, y_bfc - 0.55, 0.38)), 35, V3(0.05, -1, 0), V((0, -1, 0.9))),
          fkey(BFC + bf*0.55, V((xf, (y_bfc - 0.55 + y_ffc)*0.5 - 0.05, 0.20)), 12, V3(0.08, -1, 0), V((0, -1, 0.5))),
          fkey(FFC - 1, V((xf, y_ffc + 0.09, 0.04)), 2, V3(0.12, -1, 0), V((0.05, -1, 0.3))),
          fkey(FFC, V((xf, y_ffc, 0)), 0, V3(0.12, -1, 0), V((0.05, -1, 0.3))),
          fkey(REL + 2, V((xf, y_ffc, 0)), 6, V3(0.12, -1, 0), V((0.1, -1, 0.3))),
          fkey(loF, V((xf, y_ffc, 0)), 62, V3(0.12, -1, 0), V((0.1, -1, 0.3)))]
    # after loF the left foot joins the follow-through footsteps (its first plant is the front-foot position)
    ftp.insert(0, ["l", -1e9, loF, V((xf, y_ffc, 0)), V3(0.12, -1, 0), V((xf, y_ffc, 0))])
    ft_v = lambda t: max(0.6, 3.0*math.exp(-(t - FTL)/22.0))

    # ================================================================ hips
    T = Fl/30.0
    yh_ffc = y_ffc + A["d_ffc"]; v_ffc = 0.94*(yh_bfc - yh_ffc)/(bf/30.0)
    yh_rel = y_ffc + A["d_rel"]; yh_ftl = y_ftl + 0.22
    def hips_at(t, p):
        """world (x, y) and the phase; the delivery heights are solved afterwards"""
        if t <= T0:
            return ap(t), None
        if t <= BFC:
            y = yh_T0 - vb*(t - T0)/30.0; x = xl + (xb + 0.02 - xl)*smooth01((t - T0)/Fl)
            return V((x, y, 0)), "flight"
        if t <= FFC:
            y = herm(BFC, yh_bfc, -vb/30, FFC, yh_ffc, -v_ffc/30)(t)
            x = (xb + 0.02) + ((xf + 0.03) - (xb + 0.02))*smooth01((t - BFC)/bf)
            return V((x, y, 0)), "stride"
        if t <= REL:
            y = herm(FFC, yh_ffc, -v_ffc/30, REL, yh_rel, -A["v_rel"]/30)(t)
            return V((xf + 0.03, y, 0)), "brace"
        if t <= FTL:
            y = herm(REL, yh_rel, -A["v_rel"]/30, FTL, yh_ftl, -2.6/30)(t)
            x = (xf + 0.03) + (x_ftl + veer*0.06 - (xf + 0.03))*smooth01((t - REL)/(FTL - REL))
            return V((x, y, 0)), "follow"
        return None, "ft"
    # follow-through hip path: over each stance foot's mid-line point at mid-stance
    pts = [(FTL - 1, hips_at(FTL - 1, None)[0]), (FTL, hips_at(FTL, None)[0])]
    for i in range(1, len(ftp)):
        td_ = ftp[i][1]
        if td_ < FTL - 0.5: continue
        nxt = ftp[i+1][1] if i + 1 < len(ftp) else td_ + 8
        pts.append((td_ + 0.45*(nxt - td_), V(ftp[i][5]) if i < len(ftp) - 1 else end_hips))
    pts.append((t_end, end_hips)); pts.append((nF, end_hips))
    pts = sorted(pts, key=lambda q: q[0])
    ft_xy = pchip([(t, V((q.x, q.y, 0))) for t, q in pts])

    # ================================================================ per-frame poses
    hold = fing("r", HOLD_BALL); relx = fing("l", RELAXF)
    poses = []; zs = []; mode = []
    rtrack = lambda f: sample(rk, f); ltrack = lambda f: sample(lk, f)
    for f in range(nF):
        t = float(f)
        p = dict(BASE); p.update(hold); p.update(relx); p["look"] = V(TARGET); p["lookw"] = 1.0
        # ---------------- feet
        if t <= loR:
            fr_ = foot_state(plants, "r", t, vel(t))
            p["rball"], p["rtoe"], p["rheel"], p["rkp"] = fr_["ball"], fr_["toe"], fr_["heel"], V3(0, -1, 0.2 if fr_["stance"] else 0.35)
        elif t < FTL:
            s_ = rtrack(t); p["rball"], p["rtoe"], p["rheel"], p["rkp"] = s_["ball"], s_["toe"], s_["heel"], s_["kp"]
        else:
            fr_ = foot_state(ftp, "r", t, ft_v(t), heel_up=40)
            p["rball"], p["rtoe"], p["rheel"], p["rkp"] = fr_["ball"], fr_["toe"], fr_["heel"], fr_["toe"] + V((0, 0, 0.25))
        if t <= T0:
            fl_ = foot_state(plants, "l", t, vel(t))
            p["lball"], p["ltoe"], p["lheel"], p["lkp"] = fl_["ball"], fl_["toe"], fl_["heel"], V3(0, -1, 0.2 if fl_["stance"] else 0.35)
        elif t <= loF:
            s_ = ltrack(t); p["lball"], p["ltoe"], p["lheel"], p["lkp"] = s_["ball"], s_["toe"], s_["heel"], s_["kp"]
        else:
            fl_ = foot_state(ftp, "l", t, ft_v(t), heel_up=62)
            p["lball"], p["ltoe"], p["lheel"], p["lkp"] = fl_["ball"], fl_["toe"], fl_["heel"], fl_["toe"] + V((0, 0, 0.25))
        # ---------------- hips + posture
        xy, ph = hips_at(t, p)
        if ph is None:                                       # run-up
            v = vel(t)
            frs = foot_state(plants, "r", t, v); fls = foot_state(plants, "l", t, v)
            sts = [s_["s"] for s_ in (frs, fls) if s_["stance"]]
            if t < t_go: z = 1.02
            elif len(sts) == 2: z = zmean(v) + 0.008
            elif len(sts) == 1: z = zmean(v) - zamp(v)*math.sin(math.pi*sts[0])
            else: z = zmean(v) + zamp(v)*0.5
            step = max(0.3, v*30.0/cad(max(v, 0.5))/30.0)
            dl = clamp(((V(p["lball"]) - V(p["rball"])).dot(V((0, -1, 0))))/max(0.25, step), -1, 1)
            ww = smooth01((t - t_go)/6.0)
            oscy = 6*dl*min(1.0, v/3.0)*ww
            p["pyaw"] = oscy; p["twist"] = -1.3*oscy
            p["ptilt"] = 3 + min(9.0, 1.4*acc(t))*ww + (0.0 if t >= t_go else 0.0)
            p["lean"] = 4*ww + 2.0*pulse_(t, t_go - 2, 4)
            sway = 0.0
            if frs["stance"] and not fls["stance"]: sway = -0.014
            if fls["stance"] and not frs["stance"]: sway = 0.014
            hw = V((xy.x + sway*ww, xy.y, 0))
            p["hips"] = V((hw.x, hw.y + (0.03 if t < t_go else 0), z - HIP0))
            run_arms(p, hw, z, dl*ww, v)
            # standing at the mark: MARK hands; blend into the running arms
            wm = smooth01((t - t_go)/10.0)
            mk = {kk: (V(vv) + V((xl, ymark, 0)) if kk in ("rt", "lt") else vv) for kk, vv in MARKP.items()}
            for kk, vv in mk.items():
                if isinstance(vv, Vector): p[kk] = V(vv).lerp(V(p[kk]), wm)
                else: p[kk] = vv*(1 - wm) + p[kk]*wm
            # ball comes up to the chest over the last strides (spinners carry it there all the way)
            c = A["carry"]*(smooth01((t - (T0 - 40))/25.0) if not spin else smooth01((t - t_go)/8.0))
            chest = hw + V((-0.10, -0.24, z + 0.36))
            p["rt"] = V(p["rt"]).lerp(chest, c); p["rh"] = V(p["rh"]).lerp(V((0.1, -0.3, 1)), c); p["rhw"] = 0.3 + 0.3*c
            if spin:
                lchest = hw + V((0.02, -0.26, z + 0.34))
                p["lt"] = V(p["lt"]).lerp(lchest, 0.55*c)
            zs.append(z); mode.append("run"); poses.append(p); continue
        if ph == "ft":
            xy = ft_xy(t); tt = t
            v = ft_v(t)
            frs = foot_state(ftp, "r", t, v); fls = foot_state(ftp, "l", t, v)
            sts = [s_["s"] for s_ in (frs, fls) if s_["stance"]]
            if t >= t_end: z = 1.03
            elif len(sts) == 1: z = zmean(v) - 0.03 - zamp(v)*math.sin(math.pi*sts[0])
            else: z = zmean(v) - 0.02
            z = min(z, 1.03)
            p["pyaw"] = ft_pel(t); p["twist"] = ft_tw(t); p["lean"] = ft_lean(t); p["ptilt"] = ft_ptl(t)
            p["side"] = SD["ft"]*0.6*(1 - smooth01((t - FTL)/12.0)); p["pside"] = 0.0
            p["hips"] = V((xy.x, xy.y, z - HIP0))
            dl = clamp(((V(p["lball"]) - V(p["rball"])).dot(fwd(p["pyaw"])))/0.6, -1, 1)
            run_arms(p, V((xy.x, xy.y, 0)), z, dl*(1 - smooth01((t - (t_end - 8))/8.0)), v, heading=p["pyaw"], amp_k=0.8)
            zs.append(z); mode.append("ft"); poses.append(p); continue
        # delivery
        p["pyaw"] = pel(t); p["twist"] = shy(t) - pel(t); p["ptilt"] = ptl(t); p["lean"] = lean(t); p["side"] = sde(t)
        p["rclav"] = clv(t); p["lclav"] = -0.4*clv(t)
        p["hips"] = V((xy.x, xy.y, 0.0))
        zs.append(None); mode.append(ph); poses.append(p)

    # ---------------- hip heights in the delivery
    # take-off: last stance rises on a Hermite into the ballistic flight; flight lands at the BFC height (back knee)
    pb = poses[BFC]; z_bfc = z_for_knee(pb, "r", KN["bfc"])
    z_T0 = zmean(v_to) + 0.012
    vz0 = (z_bfc - z_T0 + G*T*T/2)/T
    midL = 0.5*(tdL + T0)
    zmid = zs[int(math.ceil(midL))] if zs[int(math.ceil(midL))] is not None else zmean(v_to) - zamp(v_to)
    zmid = min(zmid, zmean(v_to) - 0.045)
    hz = herm(midL, zmid, 0.0, T0, z_T0, vz0/30.0)
    for f in range(int(math.ceil(midL)), T0 + 1): zs[f] = hz(f)
    for f in range(T0 + 1, BFC + 1):
        tau_ = (f - T0)/30.0; zs[f] = z_T0 + vz0*tau_ - G*tau_*tau_/2
    vz_land = (vz0 - G*T)/30.0
    pf = poses[FFC]; z_ffc = z_for_knee(pf, "l", KN["ffc"])
    hzs = herm(BFC, z_bfc, vz_land*0.6, FFC, z_ffc, 0.0)
    for f in range(BFC + 1, FFC): zs[f] = hzs(f)
    for f in range(FFC, REL + 1):
        kk = KN["ffc"] + (KN["rel"] - KN["ffc"])*smooth01((f - FFC)/fr)
        zs[f] = z_for_knee(poses[f], "l", kk)
    z_rel = zs[REL]
    for f in range(REL + 1, loF + 1):
        kk = KN["rel"] + (KN["ft"] - KN["rel"])*smooth01((f - REL)/(loF - REL))
        zs[f] = z_for_knee(poses[f], "l", kk)
    # push-off -> back leg lands -> follow-through gait: one smooth bridge
    a_, b_ = loF, FTL + 4
    hb = herm(a_, zs[a_], zs[a_] - zs[a_ - 1], b_, zs[b_], 0.0)
    for f in range(a_ + 1, b_): zs[f] = hb(f)
    for f, p in enumerate(poses): p["hips"] = V((p["hips"].x, p["hips"].y, zs[f] - HIP0))

    # ================================================================ arms (delivery)
    def sh_frame(p):
        yaw = p["pyaw"] + p["twist"]; return fwd(yaw), left(yaw)
    phi_rel = A["phi_rel"] + vr["dphi"]; tilt = A["tilt"]; AZ = A["az"]
    phi = pchip([(BFC - 4, -15.0), (BFC, A["phi_bfc"]), (FFC, 100.0), (REL - 1, phi_rel - 42), (REL, phi_rel), (REL + 1, phi_rel + 42),
                 (REL + 2, phi_rel + 74), (REL + 3, phi_rel + 96), (REL + 6, phi_rel + 122), (REL + 9, phi_rel + 130)])
    azt = pchip([(BFC - 4, AZ["bfc"]*0.5), (BFC, AZ["bfc"]), (FFC, AZ["ffc"]), (REL - 1, AZ["ffc"]*0.4), (REL, 0.0), (REL + 2, 15.0),
                 (REL + 4, 32.0), (REL + 8, 40.0)])
    tlt = pchip([(BFC, tilt*0.6), (FFC, tilt), (REL, tilt), (REL + 3, tilt*0.6), (REL + 8, tilt*0.3)])
    TG = min(T0 + Fl*0.45, BFC - 7.0); TR = min(T0 + Fl*0.75, BFC - 5.0); TS = min(T0, TG - 5)
    TA = int(math.floor(TS))                      # the delivery arms take over here (before take-off for short hops)   # gather / arm rise anchors (short spin hops start them earlier)
    rdw = pchip([(TG, 0.0), (BFC - 3, 0.4), (BFC - 1, 1.0), (REL + 7, 1.0), (FTL + 8, 0.0)])
    ldw = pchip([(TR, 0.0), (BFC - 1, 0.8), (BFC, 1.0), (FFC, 1.0), (REL, 0.35), (REL + 3, 0.0)])
    fa = pchip([(BFC - 2, V(A["front_bfc"])), (BFC, V(A["front_bfc"])), (FFC, V(A["front_ffc"])), (REL, V3(0.35, -0.45, -0.45)), (REL + 3, V3(0.4, 0.1, -0.9))])
    # shoulder-frame offsets (right, forward, up) from the hips: gather at the chin, then the front hand tucks in
    r_off = pchip([(TS, V((-0.16, 0.20, 0.38))), (TG, V((-0.04, 0.26, 0.44))), (TR, V((-0.10, 0.18, 0.30))),
                   (BFC - 2, V((-0.22, 0.05, 0.05))), (REL + 7, V((0.20, 0.30, -0.10))), (FTL + 2, V((0.18, 0.22, 0.05)))])
    l_off = pchip([(TS, V((0.18, 0.22, 0.30))), (TG, V((0.02, 0.28, 0.42))), (TR, V((0.10, 0.30, 0.64))), (BFC - 1, V((0.14, 0.26, 0.86))),
                   (FFC + fr - 1, V((0.18, 0.22, 0.30))), (REL, V((0.21, 0.08, 0.24))), (REL + 4, V((0.24, -0.12, 0.14))),
                   (FTL, V((0.24, -0.06, 0.12)))])
    rpal = pchip([(BFC, V3(1, 0, 0)), (FFC, V3(0, 0.6, -0.8)), (REL - 1, V3(0, -0.6, 0.8)), (REL, vr["palm"]), (REL + 3, V3(0.3, 0, -1)), (FTL, V3(0.6, 0, -1))])
    ARMCH = ("rt", "lt", "rp", "lp", "rh", "lh", "rhw", "lhw", "rdir", "ldir", "rdirw", "ldirw", "rpal", "lpal", "rpalw", "lpalw")
    run0 = {c: (V(poses[TA][c]) if isinstance(poses[TA][c], Vector) else poses[TA][c]) for c in ARMCH}
    for f, p in enumerate(poses):
        t = float(f)
        if mode[f] == "run" and f < TA: continue
        hw = hips_w(p); Fs, Ls = sh_frame(p)
        def at(off): return hw + Ls*(-off.x) + Fs*off.y + V((0, 0, off.z)) if False else hw + Ls*off.x*(-1)*(-1) + Fs*off.y + V((0, 0, off.z))
        # ---- bowling (right) arm
        if t <= FTL + 12:
            ro = r_off(t); rtw = hw + Ls*ro.x + Fs*ro.y + V((0, 0, ro.z))
            rpw = V((-1, 0.2, -0.4)) if t < REL else V((0.2, 0.8, -0.6))
            w_run = smooth01((t - FTL)/10.0) if mode[f] == "ft" else 0.0     # blend into the follow-through running arms
            run_rt, run_rp, run_rh = V(p["rt"]), V(p["rp"]), V(p["rh"])
            p["rt"] = rtw.lerp(run_rt, w_run); p["rp"] = rpw.lerp(run_rp, w_run)
            p["rdir"] = armvec(phi(t), tlt(t), azt(t)); p["rdirw"] = rdw(t)
            rh_d = armvec(phi(t) + (14 if t >= REL - 1 else -25 if t >= FFC - 1 else 0), tlt(t), azt(t))
            p["rh"] = rh_d.lerp(run_rh, w_run)
            p["rhw"] = (0.85 if BFC - 2 <= t <= REL + 3 else 0.4)*(1 - w_run) + 0.3*w_run
            p["rpal"] = rpal(t); p["rpalw"] = (0.9 if BFC <= t <= REL + 3 else 0.4)*(1 - w_run)
            wc = 1 - smooth01((t - (BFC - 6))/4.0)                          # gather: wrist still cocked up under the chin
            if wc > 0:
                p["rh"] = V(p["rh"]).lerp(V((0.1, -0.3, 1)), wc); p["rhw"] = p["rhw"]*(1 - wc) + 0.6*wc; p["rpalw"] *= (1 - wc)
            p["rhtw"] = vr["rtw"]*smooth01((t - (REL - 2))/3.0)*(1 - smooth01((t - (REL + 3))/6.0))
            if t >= REL: p.update(fing("r", vr["fing"] if t <= REL + 2 else RELAXF))
        # ---- front (left) arm
        if t <= FTL + 10:
            w_run = smooth01((t - FTL)/8.0) if mode[f] == "ft" else 0.0
            run_lt, run_lp, run_lh = V(p["lt"]), V(p["lp"]), V(p["lh"])
            lo_ = l_off(t); ltw = hw + Ls*lo_.x + Fs*lo_.y + V((0, 0, lo_.z))
            lpw = Ls*1.0 + Fs*(-0.5) + V((0, 0, -0.4)) if t < FFC else V((0.9, 0.9, -0.5))
            p["lt"] = ltw.lerp(run_lt, w_run); p["lp"] = lpw.lerp(run_lp, w_run)
            p["ldir"] = fa(t).normalized(); p["ldirw"] = ldw(t)
            p["lh"] = fa(t).normalized().lerp(run_lh, w_run); p["lhw"] = (0.5 if t <= FFC else 0.2)*(1 - w_run) + 0.3*w_run
            p["lpal"] = V3(-0.3, -0.2, -1) if t <= FFC else V3(-1, 0, 0); p["lpalw"] = 0.5*(1 - w_run)
            if t <= FFC: p.update(fing("l", dict(T=0.1, I=0.05, M=0.05, R=0.08, P=0.1)))
        w_in = smooth01((t - TA)/5.0)                                    # hand over from the run-up arms
        if w_in < 1:
            for c in ARMCH:
                p[c] = run0[c].lerp(V(p[c]), w_in) if isinstance(run0[c], Vector) else run0[c]*(1 - w_in) + p[c]*w_in
        p["look"] = V(TARGET)
    # ---------------- the end: settle into STAND (field_clips) where he stopped
    stand = dict(STAND); stand.update(fing("r", RELAXF))
    for f in range(int(t_end) - 6, nF):
        w = smooth01((f - (t_end - 6))/10.0)
        p = poses[f]
        for kk in ("rt", "lt"):
            p[kk] = V(p[kk]).lerp(V(stand[kk]) + V((end_hips.x, end_hips.y, 0)), w)
        p["rdirw"] *= (1 - w); p["ldirw"] *= (1 - w); p["lean"] = p["lean"]*(1 - w) + stand["lean"]*w
        p["rp"] = V(p["rp"]).lerp(V(stand["rp"]), w); p["lp"] = V(p["lp"]).lerp(V(stand["lp"]), w)
        p["rhw"] = p["rhw"]*(1 - w); p["lhw"] = p["lhw"]*(1 - w)
    # ---------------- keep the planted feet reachable (lower the hips, never stretch a leg off the ground)
    fix_legs(poses, keep=(T0 - 1, loF))
    if os.environ.get("BOWL_DEBUG"):
        for q in ftp: print("  ftp", q[0], round(q[1], 1), round(q[2], 1), tuple(round(c, 2) for c in q[3]))
        for q in pts: print("  hip wp", round(q[0], 1), tuple(round(c, 2) for c in q[1]))
    ev = dict(T0=T0, BFC=BFC, FFC=FFC, REL=REL, FTL=FTL, t_go=t_go)
    info = dict(xl=xl, ymark=ymark, Drun=Drun, v_to=v_to, vb=vb, y_ffc=y_ffc, y_bfc=y_bfc, nsteps=nsteps_run, vz0=vz0, z_T0=z_T0, z_bfc=z_bfc)
    return nF, poses, ev, info

def pulse_(t, c, w):
    x = (t - c)/w; return 0.0 if abs(x) >= 1 else 0.5*(1 + math.cos(math.pi*x))

LPLANT = leg_len(178.0)          # a planted leg may brace to 178 deg (fast bowlers reach ~176 at release)
def fix_legs(poses, lim=0.988, keep=None):
    """planted feet: lower the hips until both legs reach; swing feet: pull the foot in. Smoothed over time."""
    n = len(poses); dz = [0.0]*n
    for f, p in enumerate(poses):
        need = 0.0
        for k in ("r", "l"):
            A = ankle(p, k); J = hip_joint(p, k)
            d = (J - A).length
            if d <= LPLANT: continue
            if V(p[k+"ball"]).z < 0.02:
                dxy = (V((J.x, J.y, 0)) - V((A.x, A.y, 0))).length
                zz = math.sqrt(max(0.0, LPLANT**2 - dxy*dxy))
                need = min(need, (A.z + zz) - J.z)
                if os.environ.get("BOWL_DEBUG") and (A.z + zz) - J.z < -0.03:
                    print(f"  fix f{f+1} leg {k} dxy {dxy:.2f} ball {tuple(round(c, 2) for c in p[k+'ball'])} hipJ {tuple(round(c, 2) for c in J)} dz {(A.z + zz) - J.z:+.2f}")
        dz[f] = need
    env = [min(dz[max(0, i-3):i+4]) for i in range(n)]
    sm = gsmooth(env, 1.3)
    for f, p in enumerate(poses):
        d_ = min(sm[f], dz[f])
        if d_ < 0: p["hips"] = V(p["hips"]) + V((0, 0, d_))
    for f, p in enumerate(poses):
        for k in ("r", "l"):
            A = ankle(p, k); J = hip_joint(p, k); d = (J - A).length
            if d > lim*LMAX and V(p[k+"ball"]).z >= 0.02:
                shift = (J - A)*(1 - lim*LMAX/d)
                p[k+"ball"] = V(p[k+"ball"]) + shift
    # light smoothing of the hips height (kills gait corners, keeps the ballistic flight)
    zs = gsmooth([V(p["hips"]).z for p in poses], 0.8)
    for f, p in enumerate(poses):
        if keep and keep[0] <= f <= keep[1]: continue          # the delivery heights are solved exactly (knee angles)
        z = min(zs[f], V(p["hips"]).z + 0.004)
        p["hips"] = V((p["hips"].x, p["hips"].y, z))

# ---------------------------------------------------------------- solve + export (make_field without reach-assist)
def make_bowl(name, poses, outdir):
    arm, _ = load_rig(); rig = FieldRig(arm); scn = bpy.context.scene
    n = len(poses); scn.frame_start, scn.frame_end = 0, n - 1
    prev, frames = {}, []
    for f in range(n):
        Pos, tails = rig.solve(poses[f], prev)
        frames.append(({k: (C.transposed() @ v/100) for k, v in Pos.items()}, {k: (C.transposed() @ v/100) for k, v in tails.items()}))
        for pbn in arm.pose.bones:
            pbn.keyframe_insert("rotation_quaternion", frame=f); pbn.keyframe_insert("location", frame=f)
    for pbn in arm.pose.bones:
        pbn.rotation_quaternion = Quaternion(); pbn.location = V((0, 0, 0))
        pbn.keyframe_insert("rotation_quaternion", frame=-1); pbn.keyframe_insert("location", frame=-1)
    scn.frame_set(-1)
    act_ = arm.animation_data.action; act_.name = name
    for fc in act_.fcurves:
        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
    bpy.ops.object.mode_set(mode='OBJECT')
    os.makedirs(outdir, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, name + ".fbx"), object_types={'ARMATURE'},
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
    return frames

# ---------------------------------------------------------------- measurements (what the hand-over reports)
def _ang(a, b):
    a, b = V(a), V(b)
    if a.length < 1e-9 or b.length < 1e-9: return 0.0
    return math.degrees(a.angle(b))
def _yaw(r, l):
    d = V(l) - V(r); f = V((d.y, -d.x, 0))
    return math.degrees(math.atan2(f.x, -f.y))
def measure(frames, ev, left_arm=False):
    B = lambda f, n: V(frames[f][0][PFX + n])
    s = -1.0 if left_arm else 1.0                         # mirror x for left-armers so the numbers read the same
    def M(v): return V((v.x*s, v.y, v.z))
    bow, fro = ("Left", "Right") if left_arm else ("Right", "Left")
    def at(f):
        g = lambda n: M(B(f, n))
        o = {}
        for tag, sd in (("front", fro), ("back", bow)):
            o[f"knee_{tag}"] = 180 - _ang(g(sd+"Leg") - g(sd+"UpLeg"), g(sd+"Foot") - g(sd+"Leg"))
        o["pelvis_yaw"] = _yaw(g(bow+"UpLeg"), g(fro+"UpLeg"))
        o["shoulder_yaw"] = _yaw(g(bow+"Arm"), g(fro+"Arm"))
        o["separation"] = (o["shoulder_yaw"] - o["pelvis_yaw"] + 180) % 360 - 180
        tr = g("Neck") - g("Hips")
        o["trunk_fwd"] = math.degrees(math.atan2(-tr.y, tr.z))
        o["trunk_lat"] = math.degrees(math.atan2(tr.x, tr.z))
        a = g(bow+"Hand") - g(bow+"Arm")
        o["arm_from_vertical"] = _ang(a, UP)
        o["arm_fwd_of_vertical"] = math.degrees(math.atan2(-a.y, a.z))
        o["arm_out_of_vertical"] = math.degrees(math.atan2(-a.x, a.z))
        o["elbow_flex"] = _ang(g(bow+"ForeArm") - g(bow+"Arm"), g(bow+"Hand") - g(bow+"ForeArm"))
        fa = g(fro+"Hand") - g(fro+"Arm")
        o["front_arm_elev"] = _ang(fa, V((0, 0, -1)))
        o["hip_z"] = g("Hips").z
        hand = g(bow+"HandMiddle1"); o["hand_z"] = hand.z; o["hand_y"] = hand.y; o["hand_x"] = hand.x
        o["front_toe_y"] = g(fro+"ToeBase").y
        return o
    out = {k: at(ev[k]) for k in ("BFC", "FFC", "REL")}
    out["FT"] = at(min(len(frames) - 1, ev["REL"] + 8))
    # speeds and bound
    hz = [B(f, "Hips") for f in range(len(frames))]
    sp = lambda f: (V((hz[f+1].x, hz[f+1].y, 0)) - V((hz[f-1].x, hz[f-1].y, 0))).length*15.0
    out["speed"] = dict(take_off=sp(ev["T0"]), bfc=sp(ev["BFC"]), ffc=sp(ev["FFC"]), rel=sp(ev["REL"]),
                        peak_run=max(sp(f) for f in range(2, ev["T0"])))
    out["bound"] = dict(hip_rise=max(h.z for h in hz[ev["T0"]:ev["BFC"]+1]) - hz[ev["T0"]].z,
                        foot_clear=max(max(B(f, "RightToeBase").z, B(f, "LeftToeBase").z) for f in range(ev["T0"], ev["BFC"])),
                        flight_s=(ev["BFC"] - ev["T0"])/30.0)
    # ground checks
    lows = []
    for f in range(len(frames)):
        z = min(B(f, n).z for n in ("RightToeBase", "LeftToeBase", "RightToe_End", "LeftToe_End", "RightFoot", "LeftFoot"))
        lows.append(z)
    out["ground"] = dict(min_z=min(lows), min_frame=lows.index(min(lows)) + 1)
    # per-frame bone direction jumps (flip detector), ignoring the bowling arm around release
    worst = (0, "", 0)
    for f in range(1, len(frames)):
        for n_ in frames[f][0]:
            if n_.endswith("_End") or "Hand" in n_ and ("Thumb" in n_ or "Index" in n_ or "Middle" in n_ or "Ring" in n_ or "Pinky" in n_): continue
            if abs(f - ev["REL"]) <= 4 and (bow in n_ and ("Arm" in n_ or "Hand" in n_)): continue
            a = V(frames[f][1][n_]) - V(frames[f][0][n_]); b = V(frames[f-1][1][n_]) - V(frames[f-1][0][n_])
            d = _ang(a, b)
            if d > worst[0]: worst = (d, n_[len(PFX):], f + 1)
    out["max_bone_jump"] = dict(deg=worst[0], bone=worst[1], frame=worst[2])
    rel = frames[ev["REL"]][0][PFX + bow + "HandMiddle1"]
    out["release_point"] = dict(left=round(rel[0], 3), forward_of_crease=round(-rel[1], 3), up=round(rel[2], 3))
    return out

def fmt(m):
    rows = []
    for k in ("BFC", "FFC", "REL", "FT"):
        o = m[k]
        rows.append(f"  {k:4s} knee F/B {o['knee_front']:5.0f}/{o['knee_back']:4.0f}  pelvis {o['pelvis_yaw']:5.0f} sh {o['shoulder_yaw']:5.0f} sep {o['separation']:4.0f}"
                    f"  trunk fwd {o['trunk_fwd']:4.0f} lat {o['trunk_lat']:4.0f}  arm {o['arm_fwd_of_vertical']:5.0f}/{o['arm_out_of_vertical']:4.0f} (elb {o['elbow_flex']:3.0f})"
                    f"  front arm {o['front_arm_elev']:4.0f}  hip {o['hip_z']:.2f} hand {o['hand_z']:.2f}@y{o['hand_y']:+.2f} toeF y{o['front_toe_y']:+.2f}")
    s = m["speed"]; b = m["bound"]
    rows.append(f"  speed peak {s['peak_run']:.1f} take-off {s['take_off']:.1f} BFC {s['bfc']:.1f} FFC {s['ffc']:.1f} REL {s['rel']:.1f} m/s"
                f"  bound: flight {b['flight_s']:.2f}s hip rise {b['hip_rise']:.2f} foot clear {b['foot_clear']:.2f}"
                f"  ground min {m['ground']['min_z']:+.3f}@f{m['ground']['min_frame']}  max jump {m['max_bone_jump']['deg']:.0f} {m['max_bone_jump']['bone']}@f{m['max_bone_jump']['frame']}")
    return "\n".join(rows)

# ---------------------------------------------------------------- clip lists
OUT_FAST = os.path.join(mm.APL_OUT, "bowling_fast")
OUT_MAIN = os.path.join(mm.APL_OUT, "bowling")
FAST = {f"{arm}_fast_{k}_over": dict(action=k, side="over", variant="stock", left=(arm == "la")) for arm in ("ra", "la") for k in ("side_on", "front_on", "slinger")}
MAIN = {}
def fam(prefix, action, la, stock, variants, around=True):
    MAIN[f"{prefix}_over_{stock}"] = dict(action=action, variant=stock, side="over", left=la)
    if around: MAIN[f"{prefix}_around_{stock}"] = dict(action=action, variant=stock, side="around", left=la)
    for v in variants: MAIN[f"{prefix}_over_{v}"] = dict(action=action, variant=v, side="over", left=la)
PACE_V = ["bouncer", "yorker", "outswinger", "inswinger", "off_cutter", "leg_cutter", "back_of_hand", "knuckle"]
fam("ra_express_fast", "express_fast", False, "stock", PACE_V)
fam("ra_fast_medium", "fast_medium", False, "stock", PACE_V)
fam("ra_medium", "medium", False, "stock", PACE_V)
fam("ra_slow_medium", "slow_medium", False, "stock", ["back_of_hand"])
fam("la_fast", "express_fast", True, "stock", PACE_V)
fam("la_fast_medium", "fast_medium", True, "stock", [])
fam("ra_slinger", "slinger_main", False, "stock", ["yorker", "inswinger", "bouncer", "back_of_hand"])
fam("ra_off_spin", "off_spin", False, "off_break", ["doosra", "carrom", "arm_ball"])
fam("la_orthodox", "off_spin", True, "off_break", ["doosra", "carrom", "arm_ball"])
fam("ra_leg_spin", "leg_spin", False, "leg_break", ["googly", "flipper", "top_spinner"])
fam("la_chinaman", "leg_spin", True, "leg_break", ["googly", "flipper", "top_spinner"])
MAIN["mystery_side_arm_over"] = dict(action="side_arm", variant="off_break", side="over", left=False)
JSON_ACTION = {"slinger_main": "slinger"}

def run_clip(name, spec, outdir):
    n, poses, ev, info = build(spec["action"], spec["side"], spec["variant"])
    if os.environ.get("BOWL_DEBUG"):
        for k_ in ("T0", "BFC", "FFC", "REL"):
            q = poses[ev[k_]]
            print(f"  {k_}: " + " ".join(f"{c}={q[c]:.1f}" for c in ("pyaw", "twist", "ptilt", "lean", "side", "pside")) + f" hipz={q['hips'].z + HIP0:.3f}")
    if spec["left"]: poses = [mirror(p) for p in poses]
    frames = make_bowl(name, poses, outdir)
    m = measure(frames, ev, spec["left"])
    print(f"{name}: {n} frames  take-off f{ev['T0']+1} BFC f{ev['BFC']+1} FFC f{ev['FFC']+1} release f{ev['REL']+1}  run-up {info['ymark'] - info['y_ffc']:.2f} m ({info['nsteps']} steps)", flush=True)
    print(fmt(m), flush=True)
    return n, ev, info, m

def bio(m, ev):
    r = lambda x: round(x, 1)
    out = {}
    for k, lab in (("BFC", "back_foot_contact"), ("FFC", "front_foot_contact"), ("REL", "release")):
        o = m[k]
        out[lab] = dict(front_knee_deg=r(o["knee_front"]), back_knee_deg=r(o["knee_back"]), pelvis_yaw_deg=r(o["pelvis_yaw"]),
                        shoulder_yaw_deg=r(o["shoulder_yaw"]), hip_shoulder_separation_deg=r(o["separation"]),
                        trunk_forward_deg=r(o["trunk_fwd"]), trunk_lateral_deg=r(o["trunk_lat"]),
                        bowling_arm_past_vertical_deg=r(o["arm_fwd_of_vertical"]), bowling_arm_out_from_vertical_deg=r(o["arm_out_of_vertical"]),
                        front_arm_elevation_deg=r(o["front_arm_elev"]), hip_height_m=round(o["hip_z"], 2))
    out["follow_through_trunk_forward_deg"] = r(m["FT"]["trunk_fwd"])
    out["approach_speed_m_s"] = dict(peak=r(m["speed"]["peak_run"]), take_off=r(m["speed"]["take_off"]), back_foot_contact=r(m["speed"]["bfc"]))
    out["bound"] = dict(flight_s=round(m["bound"]["flight_s"], 2), hip_rise_m=round(m["bound"]["hip_rise"], 2), foot_clearance_m=round(m["bound"]["foot_clear"], 2))
    return out

if __name__ == "__main__":
    args = sys.argv[1:] or ["fast"]
    which, names = args[0], args[1:]
    if which == "one":
        allc = dict(FAST); allc.update(MAIN); names = names or ["ra_fast_side_on_over"]
        todo = [(nm, allc[nm], OUT_FAST if nm in FAST else OUT_MAIN) for nm in names]
    elif which == "fast": todo = [(nm, FAST[nm], OUT_FAST) for nm in (names or list(FAST))]
    else: todo = [(nm, MAIN[nm], OUT_MAIN) for nm in (names or list(MAIN))]
    for nm, spec, od in todo:
        n, ev, info, m = run_clip(nm, spec, od)
        A = ACTIONS[spec["action"]]; vr = VARIANTS[spec["variant"]]
        xl = info["xl"]; left_x = -xl if spec["left"] else xl
        if od == OUT_FAST:
            meta = dict(file=nm + ".fbx", arm="left-arm" if spec["left"] else "right-arm", action=spec["action"], description=A["desc"],
                        frames=n, seconds=round(n/30, 2), release_frame=ev["REL"] + 1, back_foot_contact_frame=ev["BFC"] + 1,
                        front_foot_contact_frame=ev["FFC"] + 1, take_off_frame=ev["T0"] + 1, run_up_m=round(info["ymark"] - info["y_ffc"], 2),
                        start_position_m=dict(behind_crease=round(info["ymark"], 2), left=round(left_x, 2)),
                        release_speed_kmh_typical=A["kmh"], release_point_m=m["release_point"], biomechanics=bio(m, ev))
        else:
            meta = dict(file=nm + ".fbx", bowler_action=JSON_ACTION.get(spec["action"], spec["action"]), arm="left" if spec["left"] else "right",
                        side=spec["side"] + " the wicket", release_variant=spec["variant"], frames=n, seconds=round(n/30, 2),
                        back_foot_contact_frame=ev["BFC"] + 1, front_foot_contact_frame=ev["FFC"] + 1, release_frame=ev["REL"] + 1,
                        run_up_m=round(info["ymark"] - info["y_ffc"], 2),
                        start_position_m=dict(behind_crease=round(info["ymark"], 2), left=round(left_x, 2)),
                        release_point_m=m["release_point"], elbow_flexion_at_release_deg=round(m["REL"]["elbow_flex"], 1),
                        technique=vr.get("note", ""), take_off_frame=ev["T0"] + 1, biomechanics=bio(m, ev))
        json.dump(meta, open(os.path.join(od, nm + ".json"), "w"), indent=1)
