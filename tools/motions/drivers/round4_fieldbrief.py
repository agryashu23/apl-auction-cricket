"""
Round-4 catches to the fielding agent's brief (replaces section A), part 1. Fielding conventions: start at the origin facing
-Y (toward the batter), x = his left, travel in the hips, frames 1-based in the sidecars (catch_frame, hand_point_m, travel_m).

  catch_12_running_side_turn         runs across to his left at ~6 m/s, turns his chest in to a waist/chest-high two-hand take,
                                     brakes; 105 f, catch f55, ~7 m sideways, ends facing the batter
  catch_13_sprint_dive_forward_long  sprints in, full-length forward dive, takes it ~0.15 m off the turf, slides on his chest,
                                     comes up holding it aloft; 120 f, catch f52
  catch_14_dive_side_left / _right   two quick strides, full-stretch sideways dive, one hand ~2.6 m out and 0.5 m up; 120 f, catch f40

    motion.ps1 gen drivers\\round4_fieldbrief.py [names...]
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from round4_catches import export, chest
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, run_keys
from field_clips import READY, STAND, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco

@clip("catch_12_running_side_turn")
def side_turn():
    CF = 54; C = V((6.45, -0.48, 1.05))
    fielding.run_keys.last = None
    k = [(0, READY), (6, READY)]
    k += run_keys(READY, f0=6, f1=46, p0=(0, 0), p1=(5.0, 0), heading=90, period=15, contact=0.30, lean=14, ease="accel")[0][1:]
    turn = P(READY, hips=H(5.85, -0.02, 0.93), pyaw=60, lean=10, rball=V((5.70, 0.06, 0)), rheel=35, lball=V((6.10, -0.12, 0.10)), lheel=20,
             ltoe=V((0.8, -0.6, 0)), rtoe=V((1, -0.2, 0)), **hands(C + V((-0.25, 0.10, 0.05)), "cup", fd=V((0, -0.3, -1)), pd=V((0, -1, 0.4))),
             **OPEN_R, **OPEN_L, look=C)
    take = P(READY, hips=H(6.35, -0.05, 0.90), pyaw=25, lean=12, rball=V((6.05, 0.05, 0)), rheel=30, rtoe=V((0.6, -1, 0)),
             lball=V((6.68, -0.12, 0)), ltoe=V((0.6, -1, 0)), **hands(C, "cup", fd=V((0, -0.3, -1)), pd=V((0, -1, 0.4))), **OPEN_R, **OPEN_L, look=C)
    brake = P(READY, hips=H(6.78, -0.03, 0.86), pyaw=15, lean=18, rball=V((6.98, -0.06, 0)), rtoe=V((0.3, -1, 0)), lball=V((6.68, -0.12, 0)),
              ltoe=V((0.6, -1, 0)), lheel=20, rt=V((6.72, -0.30, 1.0)), lt=V((6.86, -0.30, 1.0)), **HOLD_R, **HOLD_L, look=V((6.8, -20, 1.0)))
    k += [(50, turn), (CF, take), (62, brake), (76, place(WITH_BALL, 6.95, 0.0)), (104, place(WITH_BALL, 6.95, 0.0))]
    export("catch_12_running_side_turn", 105, k, dict(type="catch", event="catch", style="running across, chest turned in",
           note="runs across to his left at ~6 m/s, turns his chest in to the ball and takes it two-handed at waist/chest height, brakes over two steps, ends facing the batter with the ball at the chest"),
           catch=CF, reach=lambda f: 48 <= f <= 66)

@clip("catch_13_sprint_dive_forward_long")
def dive_forward():
    CF = 51; C = V((0.0, -9.75, 0.15))
    fielding.run_keys.last = None
    k = [(0, READY), (6, READY)]
    k += run_keys(READY, f0=6, f1=38, p0=(0, 0), p1=(0, -6.0), heading=0, period=15, contact=0.30, lean=16, ease="accel")[0][1:]
    lay = lambda y, z, fy, fz, **kw: P(READY, hips=H(0, y, z), ptilt=86, lean=4, footup=0.2, rball=V((-0.12, fy, fz)), lball=V((0.12, fy - 0.05, fz)),
                                       rheel=70, lheel=70, rkp=V((0, 0, -1)), lkp=V((0, 0, -1)), **kw)
    launch = P(READY, hips=H(0, -6.75, 0.78), ptilt=45, lean=10, rball=V((-0.12, -6.55, 0)), rheel=55, lball=V((0.12, -6.35, 0.32)), lheel=60,
               **hands(V((0, -7.55, 0.85)), "custom", "both", fd=V((0, -1, 0)), pd=V((0, -0.2, 1))), **OPEN_R, **OPEN_L, look=C)
    fly = P(READY, hips=H(0, -8.30, 0.48), ptilt=80, lean=6, footup=0.2, rball=V((-0.12, -7.45, 0.48)), lball=V((0.12, -7.50, 0.42)), rheel=70, lheel=70,
            rkp=V((0, -1, -1)), lkp=V((0, -1, -1)), **hands(V((0, -9.20, 0.32)), "custom", "both", fd=V((0, -1, -0.1)), pd=V((0, 0, 1))), **OPEN_R, **OPEN_L, look=C)
    take = lay(-9.02, 0.22, -8.15, 0.12, **hands(C, "custom", "both", fd=V((0, -1, 0.05)), pd=V((0, 0, 1))), **OPEN_R, **OPEN_L, look=C)
    slide = lambda y: lay(y, 0.17, y + 0.86, 0.06, **hands(V((0, y - 0.68, 0.24)), "custom", "both", fd=V((0, -1, 0.3)), pd=V((0, 0, 1))),
                          **HOLD_R, **HOLD_L, look=V((0, y - 2, 0.3)))
    kneel = P(READY, hips=H(0, -10.05, 0.55), ptilt=15, lean=22, rball=V((-0.15, -9.65, 0)), rheel=75, rkp=V((0, -0.3, -1)), lball=V((0.18, -10.45, 0)),
              rt=V((-0.07, -10.45, 0.85)), lt=V((0.07, -10.45, 0.84)), **HOLD_R, **HOLD_L, look=V((0, -20, 1.0)))
    aloft = P(STAND, hips=H(0, -10.15, 1.03), rball=V((-0.17, -10.2, 0)), lball=V((0.17, -10.25, 0)), rt=V((-0.16, -10.2, 2.0)), rp=V((-1, 0.2, 0)),
              **HOLD_R, lt=V((0.32, -10.3, 1.35)), lp=V((1, 0.3, -0.3)), **fing("l", mm.FIST), look=V((0, -20, 3.0)))
    k += [(42, launch), (47, fly), (CF, take), (58, slide(-9.55)), (65, slide(-10.10)), (78, kneel), (94, aloft), (119, aloft)]
    export("catch_13_sprint_dive_forward_long", 120, k, dict(type="catch", event="catch", style="sprinting forward dive", dive_launch_frame=43, slide_end_frame=66,
           note="sprints in, launches into a full-length forward dive, takes it two-handed ~0.15 m off the turf, slides on his chest, comes up and holds the ball aloft"),
           catch=CF, reach=lambda f: 40 <= f <= 70)

def dive_side(name, sg):
    CF = 39; C = V((sg*2.60, -0.20, 0.50))
    lead, trail = ("l", "r") if sg > 0 else ("r", "l")
    LH, TH = (OPEN_L, OPEN_R) if sg > 0 else (OPEN_R, OPEN_L)
    rd = lambda x, z=0.95: dict(rt=V((x - 0.19, -0.26, z)), lt=V((x + 0.19, -0.26, z)))       # ready hands carried with the hips
    k = [(0, READY), (6, READY)]
    k.append((13, P(READY, hips=H(sg*0.35, 0.0, 0.92), side=sg*8, **rd(sg*0.35), **{lead + "ball": V((sg*0.62, -0.05, 0)), lead + "toe": V((sg*0.4, -1, 0))}, look=C)))
    k.append((20, P(READY, hips=H(sg*0.72, 0.0, 0.90), side=sg*8, **rd(sg*0.72), **{lead + "ball": V((sg*0.62, -0.05, 0)), trail + "ball": V((sg*0.48, 0.02, 0))}, look=C)))
    k.append((27, P(READY, hips=H(sg*1.00, 0.0, 0.80), lean=14, side=sg*18, **rd(sg*1.0, 0.85), **{lead + "ball": V((sg*1.32, -0.05, 0)), lead + "toe": V((sg*0.5, -1, 0)),
                                                                          trail + "ball": V((sg*0.70, 0.02, 0))}, look=C)))
    k.append((33, P(READY, hips=H(sg*1.38, -0.06, 0.82), pside=sg*42, lean=8, footup=0.6, **{trail + "t": V((sg*1.20, 0.05, 0.95))}, **{lead + "ball": V((sg*1.32, -0.05, 0)), lead + "heel": 55,
                                                                                       trail + "ball": V((sg*0.95, 0.05, 0.30)), trail + "heel": 50},
                    **hands(C + V((-sg*0.5, 0, 0.15)), "custom", lead, fd=V((sg, -0.1, 0.1)), pd=V((0, -1, 0.2))), **LH, look=C)))
    air = dict(pside=sg*82, lean=4, footup=0.2, **{lead + "kp": V((0, -1, 0)), trail + "kp": V((0, -1, 0)), lead + "heel": 60, trail + "heel": 60}, look=C)
    k.append((CF, P(READY, hips=H(sg*1.70, -0.14, 0.46), **air, **{lead + "ball": V((sg*0.92, -0.10, 0.40)), trail + "ball": V((sg*0.82, 0.05, 0.33))},
                    **hands(C, "custom", lead, fd=V((sg, -0.1, 0.0)), pd=V((0, -1, 0.2))), **LH,
                    **{trail + "t": V((sg*1.55, 0.05, 0.75)), trail + "p": V((0, 1, -0.5))})))
    hold = HOLD_L if sg > 0 else HOLD_R
    k.append((46, P(READY, hips=H(sg*1.86, -0.14, 0.18), pside=sg*88, lean=4, footup=0.2, **{lead + "ball": V((sg*1.05, -0.12, 0.10)),
                    trail + "ball": V((sg*0.98, 0.08, 0.08)), lead + "heel": 70, trail + "heel": 70, lead + "kp": V((0, -1, 0)), trail + "kp": V((0, -1, 0))},
                    **hands(V((sg*2.50, -0.20, 0.22)), "custom", lead, fd=V((sg, -0.1, 0.3)), pd=V((0, -1, 0.3))), **hold, look=V((sg*2.5, -0.2, 0.2)),
                    **{trail + "t": V((sg*1.95, -0.30, 0.22)), trail + "p": V((0, 1, -0.3))})))
    k.append((56, P(k[-1][1], hips=H(sg*1.95, -0.12, 0.18))))
    k.append((74, P(READY, hips=H(sg*1.90, 0.05, 0.55), pside=sg*12, lean=20, **{lead + "ball": V((sg*2.15, -0.25, 0)), trail + "ball": V((sg*1.65, 0.32, 0)),
                    trail + "heel": 75, trail + "kp": V((0, -0.3, -1))}, rt=V((sg*1.90 - 0.07, -0.32, 0.80)), lt=V((sg*1.90 + 0.07, -0.33, 0.79)),
                    **HOLD_R, **HOLD_L, look=V((sg*1.9, -20, 1.0)))))
    k += [(94, place(WITH_BALL, sg*1.90, 0.0)), (119, place(WITH_BALL, sg*1.90, 0.0))]
    export(name, 120, k, dict(type="catch", event="catch", style="full-stretch sideways dive, one hand",
           note=f"two quick strides to his {'left' if sg > 0 else 'right'}, a full-stretch dive, takes it one-handed ({'left' if sg > 0 else 'right'} hand) at full stretch, lands on his side, gets up"),
           catch=CF, hand=lead, reach=lambda f: 30 <= f <= 50)
clip("catch_14_dive_side_left")(lambda: dive_side("catch_14_dive_side_left", 1))
clip("catch_14_dive_side_right")(lambda: dive_side("catch_14_dive_side_right", -1))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
