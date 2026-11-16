"""
Round-4 fielding brief, part 3: the bowler's run-out chances and the keeper standing up to pace.

  bowler_break_stumps_nonstriker      bowling world (his popping crease y=0, his stumps at (0, +1.22), batter at -Y), right-arm over:
                                      running in, holds on to the ball in the delivery stride, stops beside the stumps, whips the
                                      bails off (f32), turns round and appeals to the umpire behind the stumps; 60 f
  ra_/la_bowler_fingertip_deflection  in his follow-through (clip origin = where it starts, facing the batter), lunges down to his
                                      left (la_: right) at a straight drive; fingertips touch it at f30 (0.3 m up, 0.9 m left, 1.2 m
                                      in front), then he turns to watch it go back toward the stumps; 80 f
  wk_stumping_up_to_pace_offside / _legside   keeper standing up to a medium pacer (keeper.py frame: stumps 0.62 m in front and
                                      0.28 m to his right): takes it at f31, the bails are off by f36; 95 f

    motion.ps1 gen drivers\\round4_fieldbrief3.py [names...]
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from round4_catches import export
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, RELAXF, run_keys
import bowling as BW
import keeper as K
from field_clips import READY, STAND, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
BALL = fing("r", BW.HOLD_BALL)

@clip("bowler_break_stumps_nonstriker")
def mankad():
    BF = 31; BAILS = V((0.0, 1.22, 0.71)); UMP = V((0.0, 3.0, 1.70))
    fielding.run_keys.last = None
    k = run_keys(STAND, f0=0, f1=16, p0=(0.30, 5.6), p1=(0.30, 3.0), heading=0, period=15, contact=0.32, lean=10,
                 extra=dict(**BALL, look=V((0, -18.9, 1.0))))[0]
    chest = lambda x, y, z: dict(rt=V((x - 0.10, y - 0.24, z + 0.36)), rh=V((0.1, -0.3, 1)), rhw=0.6, lt=V((x + 0.08, y - 0.26, z + 0.34)), **BALL)
    plant = P(STAND, hips=H(0.31, 2.05, 0.98), lean=4, rball=V((0.24, 2.05, 0)), rtoe=V((-0.6, -1, 0)), lball=V((0.42, 1.70, 0.12)), lheel=30,
              **chest(0.31, 2.05, 0.98), look=V((-1.0, -18.9, 1.0)))
    stop = P(STAND, hips=H(0.32, 1.62, 0.92), lean=8, pyaw=-20, rball=V((0.24, 2.0, 0)), rtoe=V((-0.6, -1, 0)), rheel=30, lball=V((0.40, 1.28, 0)),
             **chest(0.32, 1.62, 0.92), look=V((-2.0, -16, 1.0)))
    reach = P(stop, pyaw=-55, twist=-20, lean=28, side=-18, hips=H(0.30, 1.50, 0.88), rt=BAILS + V((0.20, 0.0, 0.10)), rh=V((-1, 0, -0.4)), rhw=0.7,
              lt=V((0.55, 1.35, 1.05)), look=BAILS)
    hit = P(reach, rt=BAILS + V((-0.05, 0.0, 0.02)), look=BAILS)
    follow = P(hit, rt=BAILS + V((-0.25, 0.05, -0.05)), side=-12)
    turn = P(STAND, hips=H(0.30, 1.48, 1.0), pyaw=120, rball=V((0.18, 1.52, 0)), lball=V((0.42, 1.38, 0)), rt=V((0.50, 1.62, 1.92)), lt=V((0.18, 1.78, 1.88)),
             **BALL, look=UMP)
    appeal = P(STAND, hips=H(0.30, 1.50, 1.0), pyaw=170, rball=V((0.44, 1.52, 0)), lball=V((0.18, 1.48, 0)), rt=V((0.50, 1.62, 2.0)), lt=V((0.12, 1.64, 1.96)),
               **BALL, look=UMP)
    k += [(20, plant), (25, stop), (29, reach), (BF, hit), (34, follow), (42, turn), (50, appeal), (59, appeal)]
    export("bowler_break_stumps_nonstriker", 60, k, dict(type="run-out", event="bails broken", bails_broken_frame=BF + 1,
           bails_m=dict(forward=-BAILS.y, left=BAILS.x, up=BAILS.z), arm="right-arm", world="bowling world: his popping crease at y=0, his stumps at y=+1.22, batter at -Y",
           note="running in, holds on to the ball in his delivery stride, stops beside the stumps, whips the bails off with the ball in his right hand, turns round and appeals to the umpire"),
           reach=lambda f: 26 <= f <= 36, probes={"hand_at_break": (BF, "RightHandMiddle1")})

def fingertip(name, mirror):
    TF = 29; B = V((0.9, -1.2, 0.3))
    fielding.run_keys.last = None
    k = [(0, P(STAND, hips=H(0.0, 0.0, 0.95), lean=24, pyaw=12, rball=V((-0.10, 0.25, 0.10)), rheel=60, lball=V((0.12, -0.15, 0)),
              rt=V((0.20, -0.20, 0.70)), lt=V((0.35, 0.05, 0.95)), **fing("r", RELAXF), look=V((0, -18, 0.8))))]
    k += run_keys(STAND, f0=4, f1=20, p0=(0.05, -0.15), p1=(0.25, -1.0), heading=10, period=16, contact=0.38, lean=14, ease="decel",
                  extra=dict(look=V((0.5, -10, 0.4))))[0][1:]
    lunge = P(STAND, hips=H(0.38, -0.95, 0.72), lean=28, side=24, pyaw=15, rball=V((0.10, -0.80, 0)), rheel=50, lball=V((0.68, -1.12, 0)), ltoe=V((0.6, -1, 0)),
              rt=V((0.05, -0.85, 0.95)), look=B)
    touch = P(lunge, **hands(B, "custom", "l", fd=V((0.45, -0.35, -0.8)), pd=V((0.2, -1, 0.3))), **fing("l", dict(T=0.1, I=0.0, M=0.0, R=0.05, P=0.1)))
    past = P(lunge, hips=H(0.42, -1.0, 0.76), lt=V((0.95, -0.95, 0.35)), look=V((0.9, 2.0, 0.2)))
    up = P(place(STAND, 0.40, -1.0, 60), look=V((0.3, 1.5, 0.4)))
    watch = P(place(STAND, 0.40, -1.0, 150), rt=V((0.49, -0.95, 1.80)), lt=V((0.31, -1.05, 1.80)), look=V((0.0, 1.22, 0.4)))
    k += [(24, lunge), (TF, touch), (34, past), (44, up), (56, watch), (79, watch)]
    side = "right" if mirror else "left"
    export(name, 80, k, dict(type="deflection", event="fingertips touch the ball", touch_frame=TF + 1,
           ball_at_touch_m=dict(forward=-B.y, left=(-B.x if mirror else B.x), up=B.z), arm="left-arm" if mirror else "right-arm",
           note=f"in his follow-through, lunges down to his {side} at a straight drive, the fingertips of his {side} hand just touch it, then he turns to watch it go back toward the stumps (hands to his head)"),
           catch=TF, hand="l", mirror=mirror, reach=lambda f: 20 <= f <= 38)
clip("ra_bowler_fingertip_deflection")(lambda: fingertip("ra_bowler_fingertip_deflection", False))
clip("la_bowler_fingertip_deflection")(lambda: fingertip("la_bowler_fingertip_deflection", True))

def stump_pace(name, side):
    tc = 30; sx = 1 if side == "off" else -1
    C = V((0.40, -0.48, 0.58)) if side == "off" else V((-0.78, -0.45, 0.58))
    base_x = 0.18*sx if side == "off" else -0.42
    rise = P(K.CROUCH_UP, hips=H(base_x*0.5, 0.06, 0.66), lean=20, rheel=10, lheel=10, **hands(V((base_x*0.5, -0.44, 0.42)), "cup", fd=V((0, -0.3, -1)), pd=V((0, -1, 0.2))))
    st = dict(lball=V((0.55, -0.05, 0))) if side == "off" else dict(rball=V((-0.80, -0.02, 0)), lball=V((-0.12, 0.0, 0)), rtoe=V((-0.5, -1, 0)))
    pre = P(rise, **st, hips=H(base_x, 0.05, 0.66), side=8*sx, **hands(C + V((0, -0.08, 0.03)), "cup", fd=V((0, -0.4, -0.9))), look=C + V((0, -2, 0.1)))
    at = P(pre, **hands(C, "cup", fd=V((0, -0.4, -0.9))), **HOLD_R, **HOLD_L, look=C)
    bails = V((K.STUMPS.x, K.STUMPS.y + 0.08, K.BAIL_Z - 0.02))
    swipe = P(at, **hands(bails + V((0.12*sx, 0, 0.12)), "custom", fd=V((-sx*0.6, -0.4, -0.7)), pd=V((-sx, 0, -0.3))), hips=H(base_x*0.5 - 0.08, 0.02, 0.62), look=bails)
    hitb = P(swipe, **hands(bails, "custom", fd=V((-sx*0.6, -0.4, -0.7)), pd=V((-sx, 0, -0.3))), side=0)
    appeal = P(STAND, hips=H(-0.1, 0.0, 1.0), **hands(V((-0.1, -0.15, 1.90)), "reverse"), **HOLD_R, **HOLD_L, look=V((8, -6, 2.0)))
    k = [(0, K.CROUCH_UP), (8, K.CROUCH_UP), (17, rise), (tc - 4, pre), (tc, at), (tc + 3, swipe), (tc + 5, hitb), (tc + 12, P(hitb, hips=V(hitb["hips"]) + V((0, 0, 0.05)))),
         (tc + 28, appeal), (tc + 44, appeal), (94, place(WITH_BALL, -0.1, 0))]
    export(name, 95, k, dict(type="take (standing up to pace)", event="take", event_frame=tc + 1, ball_at_event_m=dict(forward=-C.y, left=C.x, up=C.z),
           bails_broken_frame=tc + 6, bails_m=dict(forward=round(-bails.y, 3), left=round(bails.x, 3), up=round(bails.z, 3)),
           stumps_m=dict(forward=-K.STUMPS.y, left=K.STUMPS.x, bail_height=K.BAIL_Z),
           note=f"standing up to a medium pacer: rises with the bounce, takes it on the {side} side, whips the gloves onto the bails, appeals"),
           catch=tc, reach=lambda f: tc - 6 <= f <= tc + 10)
clip("wk_stumping_up_to_pace_offside")(lambda: stump_pace("wk_stumping_up_to_pace_offside", "off"))
clip("wk_stumping_up_to_pace_legside")(lambda: stump_pace("wk_stumping_up_to_pace_legside", "leg"))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
