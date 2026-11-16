"""Running between the wickets with the bat (batting world: +X toward the bowler, striker's popping crease at x=0,
other popping crease at x=17.68, RHB chest faces -Y in stance). Bat locked to the bottom hand; top hand free while running."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import batting
from batting import *
from shots import STANCE
from fielding import run_keys, inplace, fwd, left
OUTR = _os.path.join(APL_OUT, "running_between_wickets"); os.makedirs(OUTR, exist_ok=True)
batting.OUT = OUTR
CREASE2 = 17.68
HIP0 = 1.043
def Hw(x, y, z): return V((x, y, z - HIP0))
def lean_dir(h, deg):
    """bend the spine forward along heading h (world-axis spine channels)"""
    r = math.radians(h); return dict(lean=deg*math.cos(r), side=deg*math.sin(r))

def carry_bat(hw, h, swing=0.0):
    """bat carried in the bottom hand at the side, blade forward-down"""
    F = fwd(h); Rt = -left(h)
    bg = V((hw.x, hw.y, 0)) + F*(0.20 + swing) + Rt*0.24 + V((0, 0, 0.88))
    a = (F*0.55 + V((0, 0, -0.83))).normalized()
    return bat_g(bg, a, Rt)
def ground_bat(hw, h, reach=1.02, side=0.26):
    """bat stretched out ahead with the toe sliding along the ground"""
    F = fwd(h); Rt = -left(h)
    toe = V((hw.x, hw.y, 0)) + F*reach + Rt*side + V((0, 0, 0.03))
    a = (F*0.82 + V((0, 0, -0.57))).normalized()
    bg = toe - a*(BAT_LEN - GRIP_R)
    return bat_g(bg, a, V((0, 0, -1)))
def free_arm(h):
    F = fwd(h); L = left(h)
    return dict(topgrip=0.0, lpole=L*0.5 - F*0.6 + V((0, 0, -0.8)), rpole=-L*0.6 - F*0.3 + V((0, 0, -0.8)))
def hips_w(k): return V(k["hips"]) + V((0, 0, HIP0))
def hang_lt(hw, h):
    """free top hand hanging by the left thigh (2026-10 fix: the end poses had no lt, so the hand reached for the world origin)"""
    return V((hw.x, hw.y, 0)) + left(h)*0.22 + fwd(h)*0.04 + V((0, 0, hw.z - 0.12))
def with_carry(keys, swing_amp=0.04):
    out = []
    for i, (t, k) in enumerate(keys):
        h = k["pyaw"]; hw = hips_w(k)
        sw = swing_amp*(1 if i % 2 else -1)
        out.append((t, P(k, **carry_bat(hw, h, sw), **free_arm(h), legw=1.0, lookw=1.0)))
    return out
RUNBASE = dict(STANCE)

CLIPS = {}
def clip(name, n, keys, meta, loop=False): CLIPS[name] = (n, keys, meta, loop)

# ---------------------------------------------------------------- A: sprint loop carrying the bat (in place)
per = 16; nA = 2*per + 1; dist = 3.6
kA, _ = run_keys(RUNBASE, f0=0, f1=nA-1, p0=(0, 0), p1=(dist, 0), heading=90, period=per, contact=0.3, lean=14, hip_h=0.98, arm_amp=0.26)
kA = inplace(kA, 0, nA-1, (0, 0), (dist, 0))
kA = [(t, P(k, look=hips_w(k) + V((20, 0, -0.3)))) for t, k in kA]
clip("run_loop_with_bat", nA, with_carry(kA), dict(type="locomotion loop", loop=True, in_place=True, speed_m_s=round(dist/((nA-1)/30), 2),
     note="sprinting between the wickets, bat carried in the bottom hand, top hand pumping"), loop=True)

# ---------------------------------------------------------------- shared pieces
def start_from_stance(f_turn=12):
    """stance -> pivot toward the bowler's end, bat lifted into the carry"""
    st = dict(STANCE)
    pv = P(STANCE, hips=Hw(0.10, -0.05, 0.96), pyaw=55, lean=10, side=8, twist=0, rball=V((-0.10,-0.10,0)), rtoe=V((1,-0.4,0)),
           lball=V((0.35,-0.20,0)), ltoe=V((1,-0.2,0)), **carry_bat(V((0.10,-0.05,0.96)), 70), **free_arm(70), look=V((18,-0.5,1.2)))
    return [(0, st), (6, st), (f_turn, pv)]
def slide_and_stop(f_slide, x_line, lane_y, h=90, stop_extra=0.8, n_after=30):
    """bat slid over the line at f_slide, batter decelerates and stops just past it"""
    F = fwd(h)
    keys = []
    for df, frac, reach, lo in ((-6, -0.9, 1.10, 0.92), (0, 0.0, 1.30, 0.86), (6, 0.55, 1.25, 0.88)):
        hx = x_line - F.x*1.25 + F.x*frac + 0.0
        hw = V((hx, lane_y, lo))
        keys.append((f_slide+df, dict(_hip=hw, _bat=ground_bat(hw, h, reach), _lean=30 if df <= 0 else 22)))
    return keys

# ---------------------------------------------------------------- B: single (stance -> run -> slide bat -> stop)
def single():
    LANE = -0.95
    k = start_from_stance()
    r1, _ = run_keys(RUNBASE, f0=12, f1=46, p0=(0.25, -0.30), p1=(5.0, LANE), heading=90, period=18, contact=0.34, lean=14, ease="accel", hip_h=0.98, first="r")
    r2, _ = run_keys(RUNBASE, f0=46, f1=86, p0=(5.0, LANE), p1=(14.4, LANE), heading=90, period=16, contact=0.30, lean=14, hip_h=0.98)
    r3, _ = run_keys(RUNBASE, f0=86, f1=110, p0=(14.4, LANE), p1=(18.6, LANE), heading=90, period=17, contact=0.36, lean=10, ease="decel", hip_h=0.97)
    run = with_carry(r1[1:] + r2[1:] + r3[1:])
    # bat goes down and slides over the line (x = 17.68) around frame 102
    def ov(f, p):
        w = max(0.0, min(1.0, (f - 80)/9)) * (1 - max(0.0, min(1.0, (f - 112)/10)))
        if w > 0:
            hw = V(p["hips"]) + V((0, 0, HIP0))
            g = ground_bat(hw, 90, 1.06)
            for c in ("bg", "ba", "bn"): p[c] = V(p[c]).lerp(V(g[c]), w)
            p["ptilt"] += 34*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.20*w))
    end = P(STANCE, hips=Hw(18.6, LANE, 1.01), pyaw=90, lean=0, side=4, twist=0, rball=V((18.45, LANE-0.17, 0)), lball=V((18.75, LANE+0.15, 0)),
            rtoe=V((1,-0.2,0)), ltoe=V((1,0.2,0)), **carry_bat(V((18.6, LANE, 1.01)), 90), **free_arm(90), lt=hang_lt(V((18.6, LANE, 1.01)), 90), look=V((18.6, LANE + 8, 1.4)))     # over the left shoulder (a 180 deg look-back wrenched the head)
    keys = k + run + [(122, end), (134, end)]
    return 135, keys, ov
nB, kB, ovB = single()
CLIPS["run_single_crease_to_crease"] = (nB, kB, dict(type="running between wickets", event="bat grounded over the far popping crease", event_frame=103,
    note="from the batting stance: pivot, accelerate, sprint, slide the bat over the line at the bowler's end and pull up",
    end_position_m=dict(toward_bowler=18.6)), False, ovB)

# ---------------------------------------------------------------- C: run-and-turn for a second run
def turn():
    LANE = -0.95
    r1, _ = run_keys(RUNBASE, f0=0, f1=22, p0=(11.0, LANE), p1=(16.3, LANE), heading=90, period=16, contact=0.32, lean=12, ease="decel", hip_h=0.96, first="l")
    run1 = with_carry(r1)
    t_, k_ = run1[-1]; run1[-1] = (t_, P(k_, **ground_bat(hips_w(k_), 90, 1.0), ptilt=k_['ptilt']+24, hips=V(k_['hips'])+V((0,0,-0.12))))
    # plant at the line, ground the bat over it, pivot to the LEFT (anticlockwise), push back the other way
    plant = P(run1[-1][1], hips=Hw(16.95, LANE, 0.80), pyaw=110, ptilt=26, lball=V((17.35, LANE+0.10, 0)), ltoe=V((0.6,1,0)),
              rball=V((16.55, LANE-0.20, 0)), rheel=40, **ground_bat(V((16.95, LANE, 0.80)), 90, 1.0), **free_arm(110),
              look=V((17.9, LANE, 0)))
    pivot = P(plant, hips=Hw(17.00, LANE, 0.84), pyaw=200, ptilt=20, lball=V((17.30, LANE+0.05, 0)), ltoe=V((-0.3,1,0)),
              rball=V((16.80, LANE+0.35, 0)), rtoe=V((-1,0.5,0)), rheel=20, **carry_bat(V((17.0, LANE, 0.84)), 230), **free_arm(200),
              look=V((10, LANE+2, 1.0)))
    push = P(pivot, hips=Hw(16.70, LANE, 0.92), pyaw=270, ptilt=16, lball=V((16.95, LANE+0.12, 0)), ltoe=V((-1,0,0)),
             rball=V((16.35, LANE-0.10, 0)), rtoe=V((-1,0,0)), **carry_bat(V((16.7, LANE, 0.92)), 270), **free_arm(270),
             look=V((0, LANE, 1.2)))
    r2, _ = run_keys(RUNBASE, f0=44, f1=80, p0=(16.35, LANE), p1=(11.0, LANE), heading=270, period=16, contact=0.30, lean=14, ease="accel", hip_h=0.98, first="r")
    run2 = with_carry(r2[1:])
    keys = run1 + [(28, plant), (34, pivot), (40, push)] + run2
    return 81, keys
nC, kC = turn()
CLIPS["run_turn_for_second"] = (nC, kC, dict(type="running between wickets", event="bat grounded over the line before turning", event_frame=29,
    note="arrives at speed, grounds the bat over the popping crease, plants and turns to the left, sprints back for the next run; start and end match run_loop_with_bat",
    start_position_m=dict(toward_bowler=11.0), end_position_m=dict(toward_bowler=11.0), end_heading="back toward the striker's end"), False, None)

# ---------------------------------------------------------------- D: dragging / sliding the bat into the crease (finish of a run)
def drag():
    LANE = -0.95
    r1, _ = run_keys(RUNBASE, f0=0, f1=30, p0=(10.0, LANE), p1=(16.0, LANE), heading=90, period=16, contact=0.30, lean=14, hip_h=0.98, first="l")
    r2, _ = run_keys(RUNBASE, f0=30, f1=52, p0=(16.0, LANE), p1=(18.9, LANE), heading=90, period=17, contact=0.38, lean=10, ease="decel", hip_h=0.95)
    run = with_carry(r1 + r2[1:])
    def ov(f, p):
        w = max(0.0, min(1.0, (f - 16)/8)) * (1 - max(0.0, min(1.0, (f - 46)/10)))
        if w > 0:
            hw = V(p["hips"]) + V((0, 0, HIP0))
            g = ground_bat(hw, 90, 1.06)
            for c in ("bg", "ba", "bn"): p[c] = V(p[c]).lerp(V(g[c]), w)
            p["ptilt"] += 34*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.20*w))
    end = P(STANCE, hips=Hw(18.9, LANE, 1.01), pyaw=90, lean=0, side=4, twist=0, rball=V((18.75, LANE-0.17, 0)), lball=V((19.05, LANE+0.15, 0)),
            rtoe=V((1,-0.2,0)), ltoe=V((1,0.2,0)), **carry_bat(V((18.9, LANE, 1.01)), 90), **free_arm(90), lt=hang_lt(V((18.9, LANE, 1.01)), 90), look=V((18.9, LANE + 8, 1.4)))     # over the left shoulder (a 180 deg look-back wrenched the head)
    return 75, run + [(62, end), (74, end)], ov
nD, kD, ovD = drag()
CLIPS["drag_bat_into_crease"] = (nD, kD, dict(type="running between wickets", event="bat toe crosses the popping crease", event_frame=27,
    note="at full speed, stretches the bat out ahead and drags the toe along the ground over the line, then pulls up",
    start_position_m=dict(toward_bowler=10.0), end_position_m=dict(toward_bowler=18.9)), False, ovD)

# ---------------------------------------------------------------- E: striker's cancelled run (yes... no!)
def cancel_striker():
    LANE = -0.70
    k = start_from_stance()
    r1, _ = run_keys(RUNBASE, f0=12, f1=34, p0=(0.25, -0.30), p1=(3.0, LANE), heading=90, period=18, contact=0.36, lean=12, ease="accel", hip_h=0.98, first="r")
    run1 = with_carry(r1[1:])
    brake = P(run1[-1][1], hips=Hw(3.35, LANE, 0.86), pyaw=95, ptilt=-6, lball=V((3.75, LANE+0.12, 0)), ltoe=V((1,0.3,0)), lheel=0,
              rball=V((3.05, LANE-0.15, 0)), rheel=35, **carry_bat(V((3.35, LANE, 0.86)), 90), **free_arm(95),
              lt=V((3.5, LANE+0.45, 1.55)), look=V((10, LANE-3, 1.0)))                      # top hand up: "No!"
    pivot = P(brake, hips=Hw(3.40, LANE, 0.88), pyaw=190, ptilt=8, lball=V((3.70, LANE+0.05, 0)), ltoe=V((-0.4,1,0)),
              rball=V((3.20, LANE+0.35, 0)), rtoe=V((-1,0.6,0)), rheel=15, **carry_bat(V((3.4, LANE, 0.88)), 220), **free_arm(190),
              look=V((0, LANE, 1.0)))
    r2, _ = run_keys(RUNBASE, f0=50, f1=72, p0=(3.15, LANE), p1=(0.9, LANE+0.2), heading=270, period=17, contact=0.32, lean=14, ease="accel", hip_h=0.97, first="r")
    run2 = with_carry(r2[1:])
    def ov(f, p):
        w = max(0.0, min(1.0, (f - 56)/8)) * (1 - max(0.0, min(1.0, (f - 86)/10)))
        if w > 0:
            hw = V(p["hips"]) + V((0, 0, HIP0)); g = ground_bat(hw, 270, 1.06)
            for c in ("bg", "ba", "bn"): p[c] = V(p[c]).lerp(V(g[c]), w)
            p["ptilt"] += 34*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.20*w))
    stop = P(run2[-1][1], hips=Hw(0.05, LANE+0.2, 0.92), pyaw=270, ptilt=14, lball=V((-0.30, LANE+0.32, 0)), rball=V((0.30, LANE+0.05, 0)),
             ltoe=V((-1,0,0)), rtoe=V((-1,0,0)), **ground_bat(V((0.05, LANE+0.2, 0.92)), 270, 1.0), **free_arm(270), look=V((-1.2, LANE+0.3, 0)))
    end = P(STANCE, hips=Hw(-0.15, LANE+0.2, 1.01), pyaw=270, lean=0, side=0, rball=V((-0.30, LANE+0.05, 0)), lball=V((0.0, LANE+0.38, 0)),
            rtoe=V((-1,-0.2,0)), ltoe=V((-1,0.2,0)), **carry_bat(V((-0.15, LANE+0.2, 1.01)), 270), **free_arm(270), lt=hang_lt(V((-0.15, LANE+0.2, 1.01)), 270), look=V((10, LANE, 1.3)))
    keys = k + run1 + [(38, brake), (46, pivot)] + run2 + [(82, stop), (98, end), (109, end)]
    return 110, keys, ov
nE, kE, ovE = cancel_striker()
CLIPS["cancel_run_striker"] = (nE, kE, dict(type="running between wickets", event="bat grounded back behind the striker's crease", event_frame=76,
    note="sets off from the stance, brakes hard with the top hand up ('No!'), turns to the left and scrambles back, sliding the bat in"), False, ovE)

# ---------------------------------------------------------------- F: non-striker backing up and sent back
def cancel_nonstriker():
    LANE = -1.10
    X0 = CREASE2 + 0.30
    walk_base = P(STANCE, hips=Hw(X0, LANE, 1.01), pyaw=270, lean=0, side=0, rball=V((X0+0.15, LANE-0.12, 0)), lball=V((X0-0.15, LANE+0.12, 0)),
                  rtoe=V((-1,0,0)), ltoe=V((-1,0,0)), **carry_bat(V((X0, LANE, 1.01)), 270), **free_arm(270), lt=hang_lt(V((X0, LANE, 1.01)), 270), look=V((0, 0, 1.2)))
    w1, _ = run_keys(RUNBASE, f0=6, f1=36, p0=(X0, LANE), p1=(X0-2.4, LANE), heading=270, period=24, contact=0.45, lean=8, ease="accel", hip_h=0.99, first="l")
    walk = with_carry(w1[1:])
    brake = P(walk[-1][1], hips=Hw(X0-2.65, LANE, 0.86), pyaw=265, ptilt=-6, rball=V((X0-3.0, LANE+0.12, 0)), rtoe=V((-1,0.3,0)),
              lball=V((X0-2.35, LANE-0.15, 0)), lheel=35, **carry_bat(V((X0-2.65, LANE, 0.86)), 270), **free_arm(265), look=V((0, 0, 1.2)))
    pivot = P(brake, hips=Hw(X0-2.70, LANE, 0.88), pyaw=375, ptilt=8, rball=V((X0-2.95, LANE+0.05, 0)), rtoe=V((0.4,1,0)),
              lball=V((X0-2.50, LANE-0.35, 0)), ltoe=V((1,0.6,0)), **carry_bat(V((X0-2.7, LANE, 0.88)), 400), **free_arm(375), look=V((X0+3, LANE, 1.0)))
    r2, _ = run_keys(RUNBASE, f0=54, f1=78, p0=(X0-2.5, LANE), p1=(X0+0.9, LANE), heading=450, period=16, contact=0.30, lean=14, ease="accel", hip_h=0.97, first="r")
    run2 = with_carry(r2[1:])
    def ov(f, p):
        w = max(0.0, min(1.0, (f - 56)/8)) * (1 - max(0.0, min(1.0, (f - 90)/10)))
        if w > 0:
            hw = V(p["hips"]) + V((0, 0, HIP0)); g = ground_bat(hw, 90, 1.06)
            for c in ("bg", "ba", "bn"): p[c] = V(p[c]).lerp(V(g[c]), w)
            p["ptilt"] += 34*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.20*w))
    end = P(STANCE, hips=Hw(X0+1.2, LANE, 1.01), pyaw=450, lean=0, side=0, rball=V((X0+1.05, LANE-0.17, 0)), lball=V((X0+1.35, LANE+0.15, 0)),
            rtoe=V((1,-0.2,0)), ltoe=V((1,0.2,0)), **carry_bat(V((X0+1.2, LANE, 1.01)), 450), **free_arm(450), lt=hang_lt(V((X0+1.2, LANE, 1.01)), 450), look=V((0, 0, 1.2)))
    keys = [(0, walk_base)] + walk + [(42, brake), (50, pivot)] + run2 + [(96, end), (109, end)]
    return 110, keys, ov
nF, kF, ovF = cancel_nonstriker()
CLIPS["cancel_run_nonstriker"] = (nF, kF, dict(type="running between wickets", event="bat grounded back behind the non-striker's crease", event_frame=76,
    note="non-striker backing up toward the striker's end is sent back: brakes, turns to the left, sprints back and slides the bat in",
    start_position_m=dict(toward_bowler=round(CREASE2+0.3, 2)), start_heading="toward the striker's end"), False, ovF)

# ---------------------------------------------------------------- G: diving into the crease
def dive():
    LANE = -0.95
    r1, _ = run_keys(RUNBASE, f0=0, f1=22, p0=(9.0, LANE), p1=(14.2, LANE), heading=90, period=16, contact=0.30, lean=16, hip_h=0.98, first="l")
    run = with_carry(r1)
    def B(hx, hz, reach, tilt):
        hw = V((hx, LANE, hz)); F = V((1, 0, 0))
        toe = V((hx + reach, LANE - 0.15, 0.04)); a = V((1, 0, -0.05)).normalized()
        return bat_g(toe - a*(BAT_LEN - GRIP_R), a, V((0, 0, -1)))
    launch = P(run[-1][1], hips=Hw(14.75, LANE, 0.80), pyaw=90, ptilt=45, lball=V((14.95, LANE+0.12, 0)), lheel=50,
               rball=V((14.30, LANE-0.10, 0.25)), rheel=60, footup=0.5, **B(14.75, 0.80, 1.60, 45), topgrip=0.0,
               lt=V((15.6, LANE+0.30, 0.75)), lpole=V((0, 0.5, -1)), look=V((17.7, LANE, 0)))
    air = P(launch, hips=Hw(15.55, LANE, 0.55), ptilt=78, lball=V((14.85, LANE+0.12, 0.45)), rball=V((14.70, LANE-0.10, 0.50)), lheel=70, rheel=70,
            footup=0.2, lkp=V((0, 0, -1)), rkp=V((0, 0, -1)), **B(15.55, 0.55, 1.70, 78), lt=V((16.5, LANE+0.30, 0.40)))
    land = P(air, hips=Hw(16.15, LANE, 0.18), ptilt=88, lball=V((15.25, LANE+0.12, 0.08)), rball=V((15.20, LANE-0.10, 0.08)),
             **B(16.15, 0.18, 1.72, 88), lt=V((16.85, LANE+0.32, 0.12)), look=V((18.2, LANE, 0)))
    slide = P(land, hips=Hw(16.65, LANE, 0.17), lball=V((15.75, LANE+0.12, 0.08)), rball=V((15.70, LANE-0.10, 0.08)),
              **B(16.65, 0.17, 1.70, 88), lt=V((17.30, LANE+0.32, 0.10)))
    kneel = P(STANCE, hips=Hw(16.55, LANE, 0.55), pyaw=90, ptilt=20, lean=0, side=0, twist=0, rball=V((16.10, LANE-0.12, 0)), rheel=75, rkp=V((0.3,0,-1)),
              lball=V((17.05, LANE+0.15, 0)), ltoe=V((1,0,0)), **ground_bat(V((16.55, LANE, 0.55)), 90, 1.0), **free_arm(90),
              lt=V((16.95, LANE+0.25, 0.55)), look=V((19, LANE, 0.5)))
    end = P(STANCE, hips=Hw(16.9, LANE, 1.01), pyaw=90, lean=0, side=4, twist=0, rball=V((16.75, LANE-0.17, 0)), lball=V((17.05, LANE+0.15, 0)),
            rtoe=V((1,-0.2,0)), ltoe=V((1,0.2,0)), **carry_bat(V((16.9, LANE, 1.01)), 90), **free_arm(90), lt=hang_lt(V((16.9, LANE, 1.01)), 90), look=V((16.9, LANE + 8, 1.4)))     # over the left shoulder (a 180 deg look-back wrenched the head)
    keys = run + [(28, launch), (33, air), (38, land), (46, slide), (60, slide), (78, kneel), (94, end), (104, end)]
    return 105, keys, None
nG, kG, _ = dive()
CLIPS["dive_into_crease"] = (nG, kG, dict(type="running between wickets", event="bat toe crosses the popping crease", event_frame=37,
    note="sprints in, launches into a full-length dive with the bat stretched out flat along the ground, slides over the line, gets up",
    start_position_m=dict(toward_bowler=9.0), end_position_m=dict(toward_bowler=16.9)), False, None)

# ---------------------------------------------------------------- left-handed versions: mirror across the pitch line (y -> -y)
PAIRS = ["ball","toe","heel","kp","t","p","h","hw","pal","palw","w","dir","dirw","clav","fw","T","I","M","R","P","htw","thu","thuw","pole"]
def mirror_y(p):
    q = dict(p)
    for base in PAIRS:
        a, b = "r"+base, "l"+base
        if a in p and b in p: q[a], q[b] = p[b], p[a]
    for k, v in list(q.items()):
        if isinstance(v, Vector) and len(v) == 3: q[k] = V((v.x, -v.y, v.z))
    # the rig is symmetric about x, so a y-mirror = x-mirror + 180 deg turn: work out each angle channel
    q["pyaw"] = 180.0 - p["pyaw"]
    for k in ("twist", "lean", "pside", "nyaw", "hyaw", "nroll", "hroll"):
        if k in q: q[k] = -p[k]
    q["lefty"] = 0.0 if p.get("lefty", 0.0) > 0.5 else 1.0
    return q

if __name__ == "__main__":
    names = sys.argv[1:] or list(CLIPS)
    data, metas = {}, {}
    for hand in ("rhb", "lhb"):
        for nm in names:
            n, keys, meta, loop, *ov = CLIPS[nm]
            ov = ov[0] if ov else None
            if hand == "lhb":
                keys = [(t, mirror_y(k)) for t, k in keys]
                if ov:
                    base_ov = ov
                    def ov(f, p, base_ov=base_ov):          # run the overlay in right-handed space, then mirror back
                        q = mirror_y(p); base_ov(f, q); r = mirror_y(q); p.clear(); p.update(r)
            full = f"{hand}_{nm}"
            frames, worst, md = make_bat(full, n, keys, None, dict(loop=loop), ov)
            bad = {k: round(v, 3) for k, v in worst.items() if not k.endswith("@") and k.endswith("Grip") and v > 0.015}
            data[full] = frames; metas[full] = dict(file=full+".fbx", frames=n, batter="left-handed" if hand == "lhb" else "right-handed", **meta)
            print(full, "OK" if not bad else bad, flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "rb_frames.pkl"), "wb")); json.dump(metas, open(_os.path.join(APL_WORK, "rb_meta.json"), "w"), indent=1)
