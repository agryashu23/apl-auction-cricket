"""
Round-2 walk-out warm-ups for the fielding side (fielding conventions: start at the origin facing -Y, x = his left;
every clip starts and ends in field_clips.STAND so it chains with fielder_walk_out_loop and back; travel is in the hips).

  fielder_warmup_jog_and_jump        jogs ~3 m, gathers and springs into a tuck jump, lands soft, stands
  fielder_warmup_high_knees          high knees on the spot, arms driving
  fielder_warmup_quad_stretch        heel to the backside, right then left, the free arm out for balance
  fielder_warmup_hamstring_stretch   heel forward toe up, hinge at the hips, hands down the shin; both legs
  fielder_warmup_arm_circles         big arm circles forward then back, then two cross-body shoulder swings
  fielder_warmup_side_lunges         wide stance, lunge right, centre, left, twice
  fielder_warmup_sprint_and_stop     a 10 m burst, decelerates over three steps, stops

    motion.ps1 gen drivers\\ceremony_warmup.py [names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ceremony_field import export, CLIPS as _FC, CLASP
import ceremony_common
import fielding
from fielding import V, P, H, HIP0, run_keys, fwd
from field_clips import STAND
import bowling as BW

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
def run(f0, f1, y0, y1, **kw):
    fielding.run_keys.last = None
    rk, _ = run_keys(STAND, f0=f0, f1=f1, p0=(0, y0), p1=(0, y1), heading=0, **kw)
    return rk
def stand_at(y, **kw): return P(P(STAND, hips=H(0, y, 1.03), rball=V((-0.17, y - 0.06, 0)), lball=V((0.17, y - 0.06, 0)),
                                  rt=V((-0.21, y + 0.02, 0.90)), lt=V((0.21, y + 0.02, 0.90)), look=V((0, y - 20, 0.9))), **kw)

@clip("fielder_warmup_jog_and_jump")
def jog_jump():
    k = [(0, STAND), (6, STAND)] + run(6, 42, 0, -3.0, period=22, contact=0.42, lean=8, ease="accel", arm_amp=0.16)[1:]
    y = -3.35
    gather = stand_at(y, hips=H(0, y, 0.80), lean=18, ptilt=10, rt=V((-0.22, y + 0.25, 0.85)), lt=V((0.22, y + 0.25, 0.85)), rheel=10, lheel=10)
    push = stand_at(y, hips=H(0, y, 1.12), lean=-2, rheel=60, lheel=60, rt=V((-0.25, y - 0.15, 1.75)), lt=V((0.25, y - 0.15, 1.75)))
    tuck = stand_at(y, hips=H(0, y, 1.48), lean=8, rball=V((-0.14, y - 0.10, 0.80)), lball=V((0.14, y - 0.10, 0.80)), rheel=20, lheel=20,
                    rkp=V((0, -1, 0.8)), lkp=V((0, -1, 0.8)), rt=V((-0.22, y - 0.35, 1.25)), lt=V((0.22, y - 0.35, 1.25)), footup=0.5)
    touch = stand_at(y, hips=H(0, y, 1.04), rheel=30, lheel=30, rt=V((-0.25, y - 0.20, 1.10)), lt=V((0.25, y - 0.20, 1.10)))
    land = stand_at(y, hips=H(0, y, 0.84), lean=16, ptilt=8, rt=V((-0.22, y - 0.25, 0.95)), lt=V((0.22, y - 0.25, 0.95)))
    k += [(48, gather), (53, push), (61, tuck), (69, touch), (74, land), (88, stand_at(y)), (104, stand_at(y))]
    export("fielder_warmup_jog_and_jump", 105, k, dict(type="warm-up", jump_frames=dict(take_off=54, apex=62, landing=70),
           end_position_m=dict(x=0.0, y=y), note="jogs about 3 m, gathers, springs into a tuck jump (hips +0.45 m), lands soft and stands"))

@clip("fielder_warmup_high_knees")
def high_knees():
    k = [(0, STAND), (6, STAND)]
    t = 10.0
    for i in range(12):
        up, dn = ("l", "r") if i % 2 == 0 else ("r", "l")
        sx = {"l": 0.12, "r": -0.12}
        q = P(STAND, hips=H(0.03*(1 if dn == "l" else -1), -0.04, 1.05), lean=-2, **{
              up + "ball": V((sx[up], -0.32, 0.50)), up + "heel": 25, up + "kp": V((0, -1, 0.8)),
              dn + "ball": V((sx[dn], -0.02, 0)), dn + "heel": 40,
              ("r" if up == "l" else "l") + "t": V((sx[up]*-1.8, -0.30, 1.22)), (up + "t"): V((sx[up]*1.8, 0.12, 0.98))})
        k.append((t, q)); t += 4.5
    k += [(t + 4, P(STAND, hips=H(0, 0, 1.0))), (t + 16, STAND), (t + 28, STAND)]
    n = int(t + 29)
    export("fielder_warmup_high_knees", n, k, dict(type="warm-up", note="high knees on the spot, thighs to horizontal at about 3.3 steps/s, arms driving; back to standing"))

def quad(side, t0):
    sg = 1 if side == "l" else -1; st = "r" if side == "l" else "l"
    hold = P(STAND, hips=H(-sg*0.07, 0.04, 1.03), lean=3, side=-sg*3, **{
             side + "ball": V((sg*0.11, 0.26, 0.80)), side + "heel": 85, side + "toe": V((0, 1, -0.4)), side + "kp": V((0, 0.1, -1)),
             side + "t": V((sg*0.15, 0.20, 0.90)), side + "p": V((sg*1, 0.4, 0)), st + "t": V((-sg*0.62, -0.10, 1.25)), st + "p": V((-sg*0.3, 0.3, -1)),
             st + "ball": V((-sg*0.10, -0.06, 0))})
    return [(t0 + 10, P(hold, **{side + "ball": V((sg*0.12, 0.18, 0.30))})), (t0 + 16, hold), (t0 + 56, P(hold, lean=5)), (t0 + 66, STAND)]
@clip("fielder_warmup_quad_stretch")
def quad_stretch():
    k = [(0, STAND), (6, STAND)] + quad("r", 6) + quad("l", 76) + [(160, STAND)]
    export("fielder_warmup_quad_stretch", 161, k, dict(type="warm-up", note="stands on one leg, pulls the heel to the backside with the same hand, free arm out for balance; right then left"))

def ham(side, t0):
    sg = 1 if side == "l" else -1; st = "r" if side == "l" else "l"
    fwd_ = P(STAND, hips=H(-sg*0.06, 0.10, 0.88), lean=22, ptilt=24, **{
             side + "ball": V((sg*0.13, -0.50, 0.12)), side + "heel": -40, side + "toe": V((0, -1, 0)), side + "kp": V((0, -1, 0.3)),
             st + "ball": V((-sg*0.14, 0.08, 0)), st + "kp": V((0, -1, 0.4)),
             "rt": V((-0.06 + sg*0.08, -0.42, 0.52)), "lt": V((0.06 + sg*0.08, -0.42, 0.55)), "look": V((sg*0.1, -0.8, 0.3))})
    return [(t0 + 12, P(STAND, hips=H(0, 0.04, 1.0), **{side + "ball": V((sg*0.13, -0.50, 0.12)), side + "heel": -40})), (t0 + 22, fwd_),
            (t0 + 52, P(fwd_, ptilt=28)), (t0 + 62, P(STAND, hips=H(0, 0.04, 1.0), **{side + "ball": V((sg*0.13, -0.50, 0.12)), side + "heel": -40})),
            (t0 + 72, STAND)]
@clip("fielder_warmup_hamstring_stretch")
def ham_stretch():
    k = [(0, STAND), (6, STAND)] + ham("r", 6) + ham("l", 80) + [(170, STAND)]
    export("fielder_warmup_hamstring_stretch", 171, k, dict(type="warm-up", note="heel forward with the toe up, hinges at the hips with a flat back, hands slide down to the shin; right then left"),
           reach=lambda f: True)

@clip("fielder_warmup_arm_circles")
def arm_circles():
    def arms(phi):
        r = BW.arm_dir(phi, 12); l = V((-r.x, r.y, r.z))
        return P(STAND, rdir=r, ldir=l, rdirw=1.0, ldirw=1.0, lean=2)
    k = [(0, STAND), (8, STAND), (14, P(arms(0), rdirw=0.6, ldirw=0.6))]
    t = 14; phi = 0
    for step in range(16):                                   # 2 forward circles (phi down: forward over the top), 2 backward
        phi += -90 if step < 8 else 90; t += 5; k.append((t, arms(phi)))
    sw_r = P(STAND, rt=V((0.35, -0.30, 1.30)), lt=V((-0.35, -0.25, 1.25)), twist=-12)
    sw_o = P(STAND, rt=V((-0.60, 0.05, 1.35)), lt=V((0.60, 0.05, 1.35)), twist=8)
    k += [(t + 8, P(STAND, rdirw=0.0, ldirw=0.0)), (t + 16, sw_o), (t + 24, sw_r), (t + 32, sw_o), (t + 40, sw_r), (t + 52, STAND), (t + 64, STAND)]
    export("fielder_warmup_arm_circles", int(t + 65), k, dict(type="warm-up", note="big straight-arm circles, two forward and two back, then two cross-body shoulder swings"))

@clip("fielder_warmup_side_lunges")
def side_lunges():
    wide = P(STAND, rball=V((-0.42, -0.05, 0)), lball=V((0.42, -0.05, 0)), rtoe=V((-0.3, -1, 0)), ltoe=V((0.3, -1, 0)), **CLASP)
    def lunge(sg):
        return P(wide, hips=H(sg*0.30, 0.0, 0.74), lean=14, ptilt=10, pside=-sg*4, **{("l" if sg > 0 else "r") + "kp": V((sg*0.6, -1, 0.2))},
                 rt=V((-0.04 + sg*0.30, -0.30, 1.0)), lt=V((0.04 + sg*0.30, -0.30, 1.0)))
    k = [(0, STAND), (8, STAND), (18, P(wide, hips=H(0, 0, 0.98)))]
    t = 18
    for sg in (-1, 1, -1, 1):
        k += [(t + 16, lunge(sg)), (t + 26, lunge(sg)), (t + 38, P(wide, hips=H(0, 0, 0.96)))]; t += 38
    k += [(t + 12, STAND), (t + 24, STAND)]
    export("fielder_warmup_side_lunges", int(t + 25), k, dict(type="warm-up", note="wide stance, sinks into a side lunge to the right, back through the centre, to the left; twice"))

@clip("fielder_warmup_sprint_and_stop")
def sprint_stop():
    k = [(0, STAND), (6, P(STAND, lean=8, hips=H(0, 0, 0.98)))]
    k += run(8, 48, 0, -10.0, period=16, contact=0.30, lean=14, ease="accel", arm_amp=0.26)[1:]
    k += run(48, 66, -10.0, -13.0, period=18, contact=0.40, lean=6, ease="decel", arm_amp=0.14)[1:]
    y = -13.25
    k += [(74, stand_at(y, hips=H(0, y, 0.95), lean=8)), (86, stand_at(y)), (99, stand_at(y))]
    export("fielder_warmup_sprint_and_stop", 100, k, dict(type="warm-up", end_position_m=dict(x=0.0, y=y), note="a 10 m burst from standing, decelerates over three steps, stops and stands"))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
    ceremony_common.merge_pack()
