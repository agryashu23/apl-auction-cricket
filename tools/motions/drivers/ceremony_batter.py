"""
Batter ceremony clips (batting frame: origin between the feet, Z up, +X toward the bowler, RHB chest faces -Y in the
stance; lhb_ = running_bat.mirror_y). Bat on RightHand (rhb) / LeftHand (lhb), the bottom hand never leaves it.

  walk_out_bat_tucked_loop      walking out, bat tucked along the right side under the forearm (in place, faces +X)
  walk_out_bat_carried_loop     walking out, bat carried in the bottom hand, blade forward-down (in place, faces +X)
  walk_out_helmet_in_hand_loop  bat tucked, helmet hanging from the top hand by the grille (in place, faces +X)
  put_helmet_on                 standing: lifts the helmet on with the top hand, fastens the strap, sets the peak
  intro_idle_loop               close-up idle: tugs the glove strap, two loose pendulum swings, looks up at the crowd
  take_guard                    bat upright for the umpire's guard, scratches the mark, looks round the field, stance
  settle_after_run_single / _dive / _drag   from the last pose of the running clip to 75_non_striker_idle_loop's first
                                pose, turned round to face back down the pitch

    motion.ps1 gen drivers\\ceremony_batter.py [clip names without the hand prefix...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ceremony_common import *
import mixamo_motions as mm

CLIPS = {}
def clip(name, fn): CLIPS[name] = fn

# ---------------------------------------------------------------- standing base (facing -Y, the camera side)
def stand(hz=1.02, bat="tucked", **kw):
    hw = V((0, 0, hz))
    b = tuck_bat(hw, 0.0) if bat == "tucked" else bat_g((0.18, -0.26, 0.86), (0.15, -0.2, -1), (1, 0, 0))
    base = P(STANCE, hips=Hw(0, 0, hz), lean=2, side=0, twist=0, pyaw=0, ptilt=0, rball=V((-0.12, -0.01, 0)), lball=V((0.12, -0.01, 0)),
             rtoe=V((-0.12, -1, 0)), ltoe=V((0.12, -1, 0)), rheel=0, lheel=0, rkp=V((0, -1, 0.2)), lkp=V((0, -1, 0.2)),
             **b, **free_arm(0), lt=V((0.22, 0.0, 0.84)), look=V((0, -20, 1.55)), **mm.fingers("l", mm.RELAX))
    return P(base, **kw)

def walk(name, bat, helmet=False, note=""):
    def build():
        n, keys, v = walk_loop_keys(bat=bat)
        if helmet:                                               # helmet hanging by the grille from the top hand, small swing
            out = []
            for t, k in keys:
                hw = V(k["hips"]) + V((0, 0, HIP0)); F, L = fwd(90), left(90)
                sw = 0.035*math.sin(2*math.pi*t/(n - 1)*2 + 1.2)
                out.append((t, P(k, lt=V((hw.x, hw.y, 0)) + L*0.25 + F*(0.03 + sw) + V((0, 0, 0.80)),
                                 lpole=L*0.6 - F*0.4 + V((0, 0, -0.8)), lT=0.6, lI=0.75, lM=0.8, lR=0.8, lP=0.8, lfw=1.0)))
            keys = out
        export_bat(name, n, keys, dict(type="walk-out", in_place=True, speed_m_s=round(v, 2), faces="+X (toward the bowler's end, like run_loop_with_bat)",
                                        **({"prop": "helmet on the top hand (LeftHand rhb / RightHand lhb)"} if helmet else {}), note=note), loop=True)
    clip(name, build)
walk("walk_out_bat_tucked_loop", "tucked", note="walking out to bat, bat tucked along the right side under the forearm, free arm swinging, eyes ahead")
walk("walk_out_bat_carried_loop", "carried", note="walking out to bat, bat carried in the bottom hand with the blade forward and down")
walk("walk_out_helmet_in_hand_loop", "tucked", helmet=True, note="walking out with the bat tucked and the helmet hanging from the top hand by the grille")

# ---------------------------------------------------------------- helmet on
def helmet_on():
    side = stand(lt=V((0.24, -0.04, 0.80)), lpole=V((0.6, 0.4, -0.8)), lfw=1.0, lT=0.6, lI=0.75, lM=0.8, lR=0.8, lP=0.8)
    front = stand(lt=V((0.08, -0.33, 1.42)), lpole=V((1, 0.2, -0.6)), look=V((0, -3, 0.9)), lean=6)
    over = stand(lt=V((0.07, -0.18, 1.86)), lpole=V((1, -0.2, -0.3)), look=V((0, -8, 1.2)), lean=5)
    on = stand(lt=V((0.11, -0.04, 1.80)), lpole=V((1, 0.2, -0.3)), look=V((0, -20, 1.5)), lean=3)
    strap = stand(lt=V((0.04, -0.11, 1.47)), lpole=V((1, -0.3, -0.6)), look=V((0, -20, 1.75)), lT=0.3, lI=0.2, lM=0.5, lR=0.7, lP=0.75)
    strap2 = P(strap, lt=V((0.00, -0.11, 1.46)))
    peak = stand(lt=V((0.03, -0.19, 1.76)), lpole=V((1, -0.2, -0.4)), look=V((0, -20, 1.55)), lT=0.4, lI=0.3, lM=0.4, lR=0.5, lP=0.6)
    done = stand(look=V((3, -20, 1.6)))
    keys = [(0, side), (8, side), (22, front), (32, over), (40, on), (48, on), (58, strap), (64, strap2), (70, strap), (80, peak),
            (86, P(peak, look=V((-2, -20, 1.6)))), (92, P(peak, look=V((2, -20, 1.6)))), (104, done), (119, done)]
    export_bat("put_helmet_on", 120, keys, dict(type="ceremony", helmet_on_frame=41,
               prop="helmet on the top hand (LeftHand rhb / RightHand lhb) until helmet_on_frame, then on the Head bone",
               note="bat tucked under the right forearm; lifts the helmet with the top hand, pulls it down over the head, fastens the chin strap, sets the peak, a little shake of the head to settle it; ends in the walk-out stand"))
clip("put_helmet_on", helmet_on)

# ---------------------------------------------------------------- intro close-up idle
def intro():
    B0 = stand(bat="relax", look=V((0, -20, 1.55)), **bat_g((-0.16, -0.27, 0.90), (0.0, -0.10, -1), (1, 0, 0)))   # bat hanging in front of the right thigh
    wrist = V((-0.12, -0.26, 0.99))
    tug_a = P(B0, lt=wrist, lpole=V((1, -0.4, -0.6)), look=V((0.3, -0.6, 0.6)), lean=8, lT=0.5, lI=0.6, lM=0.6, lR=0.6, lP=0.6)
    tug_b = P(tug_a, lt=wrist + V((0.06, -0.02, 0.03)))
    def swing(ax):
        return P(B0, **bat_g((-0.16, -0.27, 0.90), ax, (1, 0, 0)), look=V((0.2, -5, 0.3)), lean=5)    # one-handed loosening swing
    hang, back, thru = swing((-0.05, -0.10, -1)), swing((-0.12, 0.30, -0.95)), swing((-0.05, -0.62, -0.78))
    up = P(B0, look=V((1.5, -12, 7.5)), lean=-2, hips=Hw(0, 0, 1.03))
    up2 = P(up, look=V((-3.5, -12, 7.0)))
    keys = [(0, B0), (14, B0), (24, tug_a), (30, tug_b), (36, tug_a), (42, tug_b), (52, B0), (64, hang), (74, back), (86, thru), (98, back),
            (110, thru), (120, hang), (130, B0), (142, up), (158, up2), (170, B0), (179, B0)]
    export_bat("intro_idle_loop", 180, keys, dict(type="intro idle", faces="-Y (the camera side; chest square)",
               note="player-card close-up: tugs the bottom glove's strap with the top hand, two loose pendulum swings of the bat, looks up at the crowd, back to the start pose"), loop=True)
clip("intro_idle_loop", intro)

# ---------------------------------------------------------------- taking guard (round 3): ends EXACTLY in batting_v2.READY
def take_guard():
    from batting_v2 import READY
    UMP = V((20.5, 0.0, 1.70)); MARK = V((-0.04, -0.34, 0.0))
    feet = dict(rball=V(READY["rball"]), lball=V(READY["lball"]), rtoe=V(READY["rtoe"]), ltoe=V(READY["ltoe"]), rheel=4.0, lheel=4.0)
    def guard(dy=0.0, **kw):                       # bat upright on the crease, hinged at the hips (knees ~30 deg, as in the stance)
        toe = MARK + V((0, dy, 0.01))
        return P(P(READY, hips=Hw(-0.03, 0.05, 0.95), ptilt=34, lean=14, side=-2, twist=10, pyaw=4, **feet,
                   **bat_g(toe + V((0, 0, BAT_LEN - GRIP_R)), (0, 0, -1), (1, 0, 0)), topgrip=1.0, look=UMP), **kw)
    def scratch(x, z=0.0):
        a = V((0.35, -0.05, -1)).normalized(); toe = V((x, MARK.y, z))
        return P(guard(), ptilt=38, **bat_g(toe - a*(BAT_LEN - GRIP_R), a, (0, -1, 0)), look=V((x + 0.05, MARK.y, 0.0)))
    def look(pyaw, twist, tgt):
        hw = V((-0.03, 0.04, 1.02))
        return P(READY, hips=Hw(hw.x, hw.y, hw.z), ptilt=6, lean=3, side=0, twist=twist, pyaw=pyaw, **feet, **carry_down(hw, pyaw),
                 **free_arm(pyaw), lt=hw + left(pyaw)*0.22 + V((0, 0, -0.13)), look=tgt)
    pre = P(READY, hips=Hw(-0.04, 0.04, 1.02), **bat_g(V(READY["bg"]) + V((0.10, -0.06, -0.10)), V((-0.6, -0.10, -0.8)), V(READY["bn"])), topgrip=1.0)
    keys = [(0, guard()), (10, guard()), (24, guard(0.04)), (34, guard(0.04)), (44, guard()), (52, guard(look=V((0.0, -0.4, 0.2)))),
            (58, scratch(-0.16)), (64, scratch(0.08)), (69, scratch(-0.14, 0.05)), (73, scratch(-0.14)), (78, scratch(0.08)),
            (80, P(scratch(0.06), topgrip=0.0, lt=top_on_handle(scratch(0.06)))), (90, look(0, 6, V((8, -14, 1.4)))), (106, look(-24, -10, V((-9, -6, 1.2)))), (124, look(24, 18, V((10, 14, 1.5)))),
            (140, look(0, 8, UMP)), (148, P(pre, topgrip=0.0, lt=top_on_handle(pre))), (152, P(pre, lt=top_on_handle(pre))),
            (164, P(READY, lt=top_on_handle(READY))), (179, P(READY, lt=top_on_handle(READY)))]    # round 4: lt parked on the handle, no re-grip gap
    export_bat("take_guard", 180, keys, dict(type="ceremony", guard_marked_frame=65, mark_point_m=dict(toward_bowler=round(MARK.x, 2), toward_off_side=-MARK.y, up=0.0),
               ends_in="the round-3 ready stance (batting_v2.READY, frame 1 of 00_stance_idle_loop) on frames 165-180: cut to the stance idle",
               note="bat upright on the crease, eyes on the umpire for his guard, nudges it once, two scratches of the mark (hinged at the hips, no crouch), "
                    "stands tall with the bat in the bottom hand and looks round the field (cover and point, the slips, the leg side, the bowler), lifts into the ready stance"))
clip("take_guard", take_guard)

# ---------------------------------------------------------------- settle after a run (round 3): pull-up pose -> the ready stance, facing back down the pitch
def settle(src, name, dx, brush=False):
    import running_bat
    from batting_v2 import READY
    E = running_bat.CLIPS[src][1][-1][1]
    ex, ey = E["hips"].x, E["hips"].y
    Fin = turn_pose(READY, 180.0, ex + dx, ey)                   # the stance with its +X (the bowler) pointing back down the pitch
    def mid(pyaw, u, rb, lb):
        hw = V((ex + (Fin["hips"].x - ex)*u, ey + (Fin["hips"].y - ey)*u, 1.01))
        return P(E, hips=Hw(hw.x, hw.y, 1.01), pyaw=pyaw, lean=0, side=0, twist=6, ptilt=4, rball=rb, lball=lb, rtoe=fwd(pyaw - 8), ltoe=fwd(pyaw + 8),
                 rkp=fwd(pyaw) + V((0, 0, 0.2)), lkp=fwd(pyaw) + V((0, 0, 0.2)), **carry_down(hw, pyaw), **free_arm(pyaw),
                 lt=hw + left(pyaw)*0.22 + V((0, 0, -0.13)), look=V((ex - 6, ey + 1.5, 1.5)))
    t0 = 0
    keys = [(0, E), (8, P(E, side=E["side"] + 7, hips=E["hips"] + V((0, 0, -0.03))))]
    if brush:                                                     # dusts the dive off his shirt and thigh
        chest, thigh = V((ex + 0.20, ey + 0.06, 1.36)), V((ex + 0.22, ey + 0.10, 0.92))
        for t, p in ((12, chest), (17, thigh), (22, chest), (27, thigh)):
            keys.append((t, P(E, lt=p, lpole=V((0, 1, -0.6)), look=V((ex + 0.5, ey, 0.6)), side=E["side"] + 10)))
        t0 = 22
    lb0, rb0 = V(E["lball"]), V(E["rball"])
    lb1 = (lb0 + V(Fin["lball"]))*0.5 + V((0.05, -0.05, 0))
    keys += [(t0 + 20, mid(150, 0.35, rb0, lb1)), (t0 + 32, mid(195, 0.7, V(Fin["rball"]), lb1)), (t0 + 42, mid(180, 0.95, V(Fin["rball"]), V(Fin["lball"]))),
             (t0 + 50, P(Fin, topgrip=0.0, lt=top_on_handle(Fin))), (t0 + 55, P(Fin, lt=top_on_handle(Fin))), (t0 + 69, P(Fin, lt=top_on_handle(Fin)))]
    keys.sort(key=lambda k: k[0])
    export_bat(name, t0 + 70, keys, dict(type="settle after running", start_pose=f"the last frame of {src} (rhb_/lhb_ to match)",
               ends_in="the round-3 ready stance (batting_v2.READY) turned 180 deg about the end position: its bowler direction points back down the pitch",
               end_position_m=dict(toward_bowler=round(ex + dx, 2), toward_off_side=round(-ey, 2)), end_heading_deg=180.0,
               note=("catches his breath, " + ("dusts the dive off his shirt and thigh, " if brush else "") +
                     "turns round to his left with the bat in the bottom hand and lifts into the ready stance facing back down the pitch")))
clip("settle_after_run_single", lambda: settle("run_single_crease_to_crease", "settle_after_run_single", -0.25))
clip("settle_after_dive", lambda: settle("dive_into_crease", "settle_after_dive", 0.20, brush=True))
clip("settle_after_drag", lambda: settle("drag_bat_into_crease", "settle_after_drag", -0.30))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
    import ceremony_common; ceremony_common.merge_pack()
