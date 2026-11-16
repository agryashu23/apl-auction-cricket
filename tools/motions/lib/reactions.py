"""Batter reactions & dismissals. Batting frame: chest faces -Y (off side), bowler at +X, keeper/stumps behind at -X.
Stumps: 1.22 m behind the popping crease on the middle-stump line (x=-1.22, y=-0.28)."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, json, pickle, os, math; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import shots74 as S74
from shots import STANCE
import batting
from batting import make_bat, bat_g, bat_s, V, P, GRIP_R, SWEET, BAT_LEN, sample
from running_bat import carry_bat, free_arm, mirror_y, Hw, HIP0
from fielding import run_keys, inplace, fwd, left
OUTX = _os.path.join(APL_OUT, "batter_reactions"); os.makedirs(OUTX, exist_ok=True)
batting.OUT = OUTX
STUMPS = V((-1.22, -0.28, 0.0))
KEEPER = V((-16.0, -0.4, 0.8)); UMPIRE = V((21.0, 0.3, 1.75)); WALK_H = 225.0

def base(fn, *a, **k):
    sid = 900 + len(S74.SHOTS74); fn(sid, "tmp", *a, **k); sh = S74.SHOTS74.pop(sid)
    return sh["keys"], sh["contact"], V(sh["sweet"])
def upto(keys, tmax): return [(t, k) for t, k in keys if t <= tmax + 1e-6]
def at(keys, t): return dict(sample(keys, t))
def hold_bat(hx, hy=-0.26, h=0.0):
    """bat held loosely in the bottom hand by the side, toe near the ground"""
    return dict(**bat_g((hx+0.20, hy, 0.86), (0.15,-0.2,-1), (1,0,0)))
def hang_bat(hw, h):
    """walking off: bat hanging from the bottom hand beside the leg"""
    F = fwd(h); Rt = -left(h)
    bg = V((hw.x, hw.y, 0)) + Rt*0.20 + F*0.05 + V((0, 0, 0.89)); a = (F*0.18 + V((0, 0, -1))).normalized()   # where a relaxed hanging arm holds it
    return bat_g(bg, a, Rt)
def shake_look(center, f0, n=3, amp=1.2, period=8):
    return [(f0 + i*period//2, dict(look=center + V((0, amp*(1 if i % 2 else -1), 0)))) for i in range(n*2)] + [(f0 + n*period, dict(look=center))]

CLIPS = {}
def clip(name, n, keys, meta): CLIPS[name] = (n, sorted(keys, key=lambda k: k[0]), meta)

# ---------------------------------------------------------------- walking off pieces
def walk_keys(f0, f1, p0, h, *, dist, period=36, dejected=True):
    F = fwd(h); p1 = V((p0[0], p0[1], 0)) + F*dist
    rk, _ = run_keys(STANCE, f0=f0, f1=f1, p0=p0, p1=(p1.x, p1.y), heading=h, period=period, contact=0.62, lean=6 if dejected else 3,
                     bob=0.015, arm_amp=0.10, hip_h=1.01)
    out = []
    for t, k in rk:
        hw = V(k["hips"]) + V((0, 0, HIP0))
        look = hw + F*3.0 + V((0, 0, -0.9 if dejected else -0.4))
        out.append((t, P(k, **hang_bat(hw, h), **free_arm(h), look=look)))
    return out, (p1.x, p1.y)
def walk_start(f0, hx, hy, dejected=True, n_after=40):
    """from wherever he is: tuck the bat down, turn toward the pavilion and walk off (chains into walk_off loop)"""
    turn = P(STANCE, hips=Hw(hx, hy, 1.0), pyaw=WALK_H*0.6, lean=4, side=0, twist=0, rball=V((hx-0.15, hy-0.05, 0)), lball=V((hx+0.10, hy+0.12, 0)),
             rtoe=fwd(WALK_H*0.6), ltoe=fwd(WALK_H*0.6), **hang_bat(V((hx, hy, 1.0)), WALK_H*0.6), **free_arm(WALK_H*0.6),
             look=V((hx, hy, 0)) + fwd(WALK_H)*3 + V((0, 0, 0.3 if dejected else 0.8)))
    wk, end = walk_keys(f0 + 10, f0 + 10 + n_after, (hx, hy), WALK_H, dist=1.25*n_after/36*(1.0 if dejected else 1.3), dejected=dejected)
    return [(f0, turn)] + wk[1:], end

# ================================================================= misses and edges
def play_and_miss():
    keys, tc, S = base(S74.ff_drive, 55, "outside_off", "ground", cx=0.58)
    k = upto(keys, tc)
    con = at(keys, tc)
    frozen = P(con, look=KEEPER)
    lean_back = P(con, hips=V(con["hips"]) + V((-0.05, 0, 0.04)), lean=con["lean"]-8, look=KEEPER + V((0, -2, 0)))
    k += [(tc+5, P(con, look=S + V((-1.5, -0.3, 0)))), (tc+14, frozen), (tc+22, lean_back)]
    k += [(tc+22+dt, P(lean_back, **kw)) for dt, kw in [(d - (tc+22), kw) for d, kw in shake_look(KEEPER, tc+24, 2, 1.5)]]
    k += [(tc+52, P(STANCE, **hold_bat(0.05), **free_arm(0), lt=V((0.10, -0.15, 1.05)), look=V((8, -0.3, 1.2)))), (tc+75, STANCE), (tc+84, STANCE)]
    pass_pt = S + V((0, -0.12, 0.02))
    clip("play_and_miss_drive", tc+85, k, dict(event="ball beats the outside edge", event_frame=tc+1,
         ball_at_event_m=dict(toward_bowler=round(pass_pt.x,3), toward_off_side=round(-pass_pt.y,3), up=round(pass_pt.z,3)),
         note="goes for the cover drive, ball beats the outside edge; freezes, turns to look at the keeper, shakes head, resets"))
def beaten_defence():
    keys, tc, S = base(S74.ff_drive, 0, "off", "dead")
    k = upto(keys, tc); con = at(keys, tc)
    k += [(tc+8, P(con, look=KEEPER)), (tc+20, P(con, hips=V(con["hips"]) + V((-0.04, 0, 0.03)), look=KEEPER)),
          (tc+40, P(STANCE, **hold_bat(0.05), look=V((-6, -0.5, 0.2)))), (tc+62, STANCE), (tc+70, STANCE)]
    pass_pt = S + V((0, -0.10, 0.03))
    clip("beaten_forward_defence", tc+71, k, dict(event="ball beats the outside edge", event_frame=tc+1,
         ball_at_event_m=dict(toward_bowler=round(pass_pt.x,3), toward_off_side=round(-pass_pt.y,3), up=round(pass_pt.z,3)),
         note="forward defence, beaten outside off; holds the shape, looks back, looks at the pitch, resets"))
def outside_edge():
    keys, tc, S = base(S74.ff_drive, 15, "off", "ground")
    k = upto(keys, tc + 4); con = at(keys, tc)
    slips = V((-12.0, -4.0, 0.6))
    k += [(tc+8, P(con, look=slips)), (tc+20, P(con, hips=V(con["hips"]) + V((-0.03, 0, 0.04)), look=slips + V((0, -1, 0.5)))),
          (tc+44, P(STANCE, **hold_bat(0.05), look=slips)), (tc+66, STANCE), (tc+74, STANCE)]
    edge = S + V((0, -0.05, 0.02))
    clip("outside_edge_reaction", tc+75, k, dict(event="outside edge", event_frame=tc+1,
         ball_at_event_m=dict(toward_bowler=round(edge.x,3), toward_off_side=round(-edge.y,3), up=round(edge.z,3)), ball_goes="behind square on the off side (keeper / slips)",
         note="drives, gets a thick outside edge, whips the head round to follow it toward the slips (use with a catch or a dropped catch)"))
def inside_edge_pad():
    keys, tc, S = base(S74.ff_drive, 0, "stumps", "dead")
    k = upto(keys, tc); con = at(keys, tc)
    jolt = P(con, hips=V(con["hips"]) + V((-0.03, 0.02, 0.0)), look=V((0.5, 0.05, 0.3)))
    k += [(tc+3, jolt), (tc+12, P(con, look=V((0.0, -0.4, 0.05)))), (tc+40, P(STANCE, **hold_bat(0.05))), (tc+60, STANCE), (tc+68, STANCE)]
    clip("inside_edge_onto_pad", tc+69, k, dict(event="inside edge", event_frame=tc+1, pad_hit_frame=tc+3,
         note="forward defence, inside edge deflects onto the front pad; small jolt, looks down for the ball"))

# ================================================================= getting hit
def pad_front():
    keys, tc, S = base(S74.ff_drive, 0, "stumps", "dead")
    k = upto(keys, tc - 4); con = at(keys, tc)
    late = P(con, bg=V(con["bg"]) + V((-0.10, 0.0, 0.12)))                     # bat a fraction late / above the ball
    jolt = P(late, hips=V(con["hips"]) + V((-0.04, 0.01, 0.01)), lheel=10, look=V((0.6, -0.15, 0.25)))
    k += [(tc, late), (tc+3, jolt), (tc+14, P(late, look=UMPIRE)), (tc+40, P(late, look=UMPIRE)), (tc+60, P(STANCE, **hold_bat(0.05))), (tc+78, STANCE), (tc+86, STANCE)]
    clip("hit_on_pad_front_foot", tc+87, k, dict(event="ball hits the front pad", event_frame=tc+1,
         note="forward press, bat a fraction late, ball strikes the front pad; jolt, then looks at the umpire (LBW shout moment), resets"))
def pad_back():
    keys, tc, S = base(S74.bf_vertical, 0, "stumps", "defence")
    k = upto(keys, tc - 4); con = at(keys, tc)
    late = P(con, bg=V(con["bg"]) + V((-0.06, 0.0, 0.14)))
    jolt = P(late, hips=V(con["hips"]) + V((-0.05, 0.0, -0.01)), look=V((-0.2, -0.15, 0.4)))
    k += [(tc, late), (tc+3, jolt), (tc+14, P(late, look=UMPIRE)), (tc+40, P(late, look=UMPIRE)), (tc+60, P(STANCE, **hold_bat(0.05))), (tc+78, STANCE), (tc+86, STANCE)]
    clip("hit_on_pad_back_foot", tc+87, k, dict(event="ball hits the back pad", event_frame=tc+1,
         note="back and across, misses, struck on the back pad in front of the stumps; looks at the umpire, resets"))
def body_blow():
    keys, tc, S = base(S74.bf_cross, 300, "pull")
    k = upto(keys, tc - 6); pre = at(keys, tc - 4)
    hips = V(pre["hips"])
    flinch = P(pre, hips=hips + V((-0.06, 0.04, -0.04)), lean=pre["lean"] + 18, twist=pre["twist"] - 25, topgrip=0.0,
               **hold_bat(hips.x, -0.20), lt=V((hips.x + 0.10, hips.y - 0.12, 1.22)), lpole=V((0.6, 0.0, -1)), look=V((hips.x + 0.5, hips.y - 0.5, 0.2)))
    curl = P(flinch, hips=hips + V((-0.18, 0.06, -0.12)), lean=pre["lean"] + 32, rball=V((pre["rball"].x - 0.30, pre["rball"].y + 0.05, 0)),
             lt=V((hips.x - 0.05, hips.y - 0.12, 1.08)), look=V((hips.x - 0.2, hips.y - 0.4, 0.0)))
    k += [(tc, flinch), (tc+12, curl), (tc+45, curl), (tc+70, P(STANCE, hips=Hw(hips.x - 0.15, hips.y + 0.05, 1.02), lean=4,
                                                                **hold_bat(hips.x - 0.15), **free_arm(0), lt=V((hips.x - 0.05, hips.y - 0.14, 1.15)), look=V((10, -0.3, 1.0)))),
          (tc+96, STANCE), (tc+104, STANCE)]
    clip("hit_on_body_ribs", tc+105, k, dict(event="short ball hits the ribs", event_frame=tc+1,
         note="shapes to pull, the ball is on him too quickly and hits the ribs; flinches, top hand clutches the side, doubles over, recovers"))
def helmet():
    keys, tc, S = base(S74.bf_cross, 250, "hook")
    k = upto(keys, tc - 8); pre = at(keys, tc - 6)
    hips = V(pre["hips"]); head = V((hips.x - 0.05, hips.y - 0.02, 1.62))
    snap = P(pre, hips=hips + V((-0.10, 0.06, 0.0)), lean=-10, twist=pre["twist"] - 30, topgrip=0.0, **hold_bat(hips.x, -0.18),
             lt=head + V((0.10, -0.10, 0.05)), lpole=V((0.8, 0, -0.5)), look=V((hips.x - 3, hips.y + 2, 4.0)))
    stagger = P(snap, hips=hips + V((-0.40, 0.10, -0.10)), rball=V((pre["rball"].x - 0.45, pre["rball"].y + 0.10, 0)), lball=V((pre["lball"].x - 0.35, pre["lball"].y + 0.05, 0)),
                lean=10, look=V((hips.x - 1, hips.y, 0.2)), lt=head + V((0.06, -0.08, -0.12)))
    squat_h = P(stagger, hips=Hw(hips.x - 0.40, hips.y + 0.10, 0.55), lean=30, rball=V((hips.x - 0.65, hips.y - 0.02, 0)), lball=V((hips.x - 0.20, hips.y + 0.18, 0)),
                rheel=20, lheel=20, rkp=V((-0.3,-1,0.3)), lkp=V((0.3,-1,0.3)), **bat_g((hips.x - 0.25, hips.y - 0.35, 0.72), (0.3, -0.3, -1), (1, 0, 0)),
                lt=V((hips.x - 0.40, hips.y - 0.05, 1.08)), look=V((hips.x - 0.3, hips.y - 0.6, 0.0)))
    k += [(tc, snap), (tc+8, stagger), (tc+22, squat_h), (tc+80, squat_h), (tc+110, P(STANCE, hips=Hw(hips.x - 0.40, hips.y + 0.08, 1.02),
              **hold_bat(hips.x - 0.40), **free_arm(0), lt=V((hips.x - 0.35, hips.y - 0.10, 1.68)), look=V((8, -0.3, 1.4)))),
          (tc+130, STANCE), (tc+140, STANCE)]
    clip("hit_on_helmet", tc+141, k, dict(event="bouncer hits the helmet", event_frame=tc+1,
         note="late on the hook, struck on the helmet: head snaps back, staggers, crouches holding the helmet (pause for a concussion check), stands and resets"))
def glove():
    keys, tc, S = base(S74.bf_vertical, 0, "stumps", "defence")
    k = upto(keys, tc); con = at(keys, tc); hips = V(con["hips"])
    off = P(con, topgrip=0.0, **hold_bat(hips.x, -0.22), lt=V((hips.x + 0.15, hips.y - 0.30, 1.05)), lpole=V((0.6, 0, -1)), look=V((hips.x + 0.1, hips.y - 0.3, 1.0)))
    k += [(tc+4, off)]
    for i in range(6):
        k.append((tc+8+i*4, P(off, lt=V((hips.x + 0.15 + (0.08 if i % 2 else -0.08), hips.y - 0.30, 1.02 + (0.06 if i % 2 else 0))))))
    walk = P(off, hips=Hw(hips.x - 0.5, hips.y + 0.2, 1.02), pyaw=150, lean=4, rball=V((hips.x - 0.65, hips.y + 0.1, 0)), lball=V((hips.x - 0.35, hips.y + 0.35, 0)),
             rtoe=fwd(150), ltoe=fwd(150), **hang_bat(V((hips.x - 0.5, hips.y + 0.2, 1.0)), 150), **free_arm(150), lt=V((hips.x - 0.45, hips.y + 0.05, 1.05)),
             look=V((hips.x - 0.6, hips.y + 0.1, 1.0)))
    k += [(tc+40, walk), (tc+70, walk), (tc+95, STANCE), (tc+104, STANCE)]
    clip("hit_on_glove", tc+105, k, dict(event="ball hits the top hand", event_frame=tc+1,
         note="short ball jags into the top glove; the hand comes off the bat and shakes, walks away flexing it, comes back to the crease"))

# ================================================================= getting out
def bowled():
    keys, tc, S = base(S74.ff_drive, 0, "stumps", "ground")
    k = upto(keys, tc + 4); con = at(keys, tc + 4); hips = V(con["hips"])
    look_st = STUMPS + V((0, 0, 0.5))
    k += [(tc+9, P(con, look=look_st)), (tc+22, P(con, look=look_st, hips=hips + V((-0.04, 0, 0.03)))),
          (tc+40, P(STANCE, hips=Hw(hips.x - 0.15, hips.y + 0.03, 0.98), pyaw=-30, lean=14, **hold_bat(hips.x - 0.15),
                    **free_arm(0), lt=V((hips.x - 0.10, hips.y - 0.18, 1.70)), look=look_st))]
    wk, end = walk_start(tc + 62, hips.x - 0.15, hips.y + 0.03)
    clip("bowled_reaction_walk_off", tc + 62 + 50, k + wk, dict(event="bowled (stumps broken)", event_frame=tc+4, ball_passes_bat_frame=tc+1,
         note="drives, misses, the stumps are hit behind him; looks round at them, top hand to the helmet, turns and starts the walk off (chain into walk_off_dejected_loop)",
         end_position_m=dict(toward_bowler=round(end[0],2), toward_off_side=round(-end[1],2))))
def caught():
    keys, tc, S = base(S74.ff_drive, 0, "stumps", "lofted")
    k = upto(keys, tc + 14); fin = at(keys, tc + 14); hips = V(fin["hips"])
    sky = [V((12, -0.5, 9.0)), V((25, -1.0, 12.0)), V((40, -1.5, 6.0)), V((48, -1.5, 1.5))]
    for i, s in enumerate(sky): k.append((tc + 20 + i*12, P(fin, look=s)))
    slump = P(STANCE, hips=Hw(hips.x, hips.y + 0.02, 0.98), pyaw=0, lean=16, **hold_bat(hips.x), **free_arm(0),
              lt=V((hips.x + 0.05, hips.y - 0.15, 1.05)), look=V((hips.x + 1.0, hips.y - 0.6, 0.0)))
    k += [(tc + 64, slump), (tc + 78, slump)]
    wk, end = walk_start(tc + 84, hips.x, hips.y + 0.02)
    clip("caught_reaction_walk_off", tc + 84 + 50, k + wk, dict(event="lofted shot", event_frame=tc+1, catch_taken_frame=tc+57,
         note="lofts it, watches it hang in the air, sees it caught in the deep: head drops, starts the walk off"))
def lbw():
    keys, tc, S = base(S74.ff_drive, 0, "stumps", "dead")
    k = upto(keys, tc - 4); con = at(keys, tc)
    late = P(con, bg=V(con["bg"]) + V((-0.10, 0.0, 0.12)))
    jolt = P(late, hips=V(con["hips"]) + V((-0.04, 0.01, 0.01)), look=V((0.6, -0.15, 0.25)))
    hips = V(con["hips"])
    k += [(tc, late), (tc+3, jolt), (tc+12, P(late, look=UMPIRE)), (tc+44, P(late, look=UMPIRE))]
    shake = shake_look(V((hips.x + 1.5, hips.y - 0.4, 0.1)), tc + 54, 2, 0.8)
    stand = P(STANCE, hips=Hw(hips.x - 0.1, hips.y + 0.02, 1.0), lean=10, **hold_bat(hips.x - 0.1), **free_arm(0),
              lt=V((hips.x - 0.05, hips.y - 0.15, 1.02)), look=V((hips.x + 1.5, hips.y - 0.4, 0.1)))
    k += [(tc+52, stand)] + [(t, P(stand, **kw)) for t, kw in shake]
    wk, end = walk_start(tc + 82, hips.x - 0.1, hips.y + 0.02)
    clip("lbw_given_walk_off", tc + 82 + 50, k + wk, dict(event="ball hits the front pad", event_frame=tc+1, umpire_gives_out_frame=tc+45,
         note="struck on the pad, looks at the umpire, the finger goes up: shakes his head and walks off"))
def run_out_reaction():
    hx, hy = 0.6, -1.0
    st = P(STANCE, hips=Hw(hx, hy, 1.0), pyaw=270, lean=6, side=0, twist=0, rball=V((hx+0.15, hy-0.10, 0)), lball=V((hx-0.15, hy+0.12, 0)),
           rtoe=fwd(270), ltoe=fwd(270), **carry_bat(V((hx, hy, 1.0)), 270), **free_arm(270), look=V((-1.3, -0.3, 0.4)))
    hands_head = P(st, lt=V((hx - 0.05, hy + 0.05, 1.72)), lpole=V((0, 0.5, -0.3)), look=V((-1.3, -0.3, 0.6)))
    back = P(hands_head, lean=-4, look=V((-3, -1, 6.0)))                                                   # looks up at the big screen
    k = [(0, st), (6, st), (16, hands_head), (34, back), (52, back), (60, P(hands_head, look=V((hx + 3, hy - 3, 0.0))))]
    wk, end = walk_start(70, hx, hy)
    clip("run_out_reaction_walk_off", 120, k + wk, dict(event="run out", event_frame=1,
         note="just short of the crease after a run: hand on the helmet, looks back at the broken stumps and up at the screen, walks off"))
def hit_wicket():
    keys, tc, S = base(S74.bf_cross, 300, "pull")
    k = upto(keys, tc + 2); con = at(keys, tc + 2)
    hips = V(con["hips"]); heel = V((STUMPS.x + 0.10, STUMPS.y + 0.12, 0))
    trod = P(con, rball=heel + V((0.15, 0, 0)), rheel=30, hips=hips + V((-0.18, 0.05, -0.03)), look=STUMPS + V((0, 0, 0.3)))
    k += [(tc + 8, trod), (tc + 20, P(trod, look=STUMPS + V((0, 0, 0.3)), lean=20, **hold_bat(hips.x - 0.2), **free_arm(0),
                                     lt=V((hips.x - 0.25, hips.y - 0.1, 1.70))))]
    wk, end = walk_start(tc + 44, hips.x - 0.2, hips.y + 0.05)
    clip("hit_wicket_walk_off", tc + 44 + 50, k + wk, dict(event="back foot breaks the stumps", event_frame=tc + 9,
         note="goes deep in the crease to pull, the back foot treads on the stumps; looks down at them in disbelief, hand on the helmet, walks off"))

# ================================================================= walking off
def walk_loop():
    per = 36; n = 2*per + 1; dist = 1.30
    wk, _ = walk_keys(0, n - 1, (0, 0), WALK_H, dist=dist, period=per)
    wk = inplace(wk, 0, n - 1, (0, 0), tuple(fwd(WALK_H)*dist))
    wk = [(t, P(k, **hang_bat(V(k["hips"]) + V((0, 0, HIP0)), WALK_H))) for t, k in wk]
    clip("walk_off_dejected_loop", n, wk, dict(loop=True, in_place=True, speed_m_s=round(dist/((n-1)/30), 2), heading="behind square on the leg side (turn him toward your pavilion)",
         note="slow walk off, head down, bat hanging from the bottom hand"))
def angry_walk():
    st = P(STANCE, hips=Hw(0.05, 0.02, 1.0), pyaw=0, lean=4)
    swipe1 = P(st, **bat_g((0.10, -0.40, 1.10), (0.6, -0.6, 0.3), (0, 0, -1)), **free_arm(0), lt=V((0.25, -0.10, 1.0)), look=V((0.5, -2, 0.0)))
    swipe2 = P(swipe1, **bat_g((-0.15, -0.30, 0.95), (-0.5, -0.4, -0.6), (0, 0, -1)), look=V((-1.0, -1.5, 0.0)))
    k = [(0, st), (6, st), (14, swipe1), (19, swipe2), (28, P(swipe2, lt=V((0.05, -0.15, 1.70)), look=V((0, -2, 0.0))))]
    wk, end = walk_keys(36, 110, (0.05, 0.02), WALK_H, dist=3.4, period=30, dejected=False)
    clip("walk_off_angry", 111, k + wk[1:], dict(event="dismissed", event_frame=1,
         note="angry: swipes the bat through the air, hand to the helmet, then a brisk walk off (root motion ~3.4 m)",
         end_position_m=dict(toward_bowler=round(end[0],2), toward_off_side=round(-end[1],2))))
def start_walk():
    k = [(0, STANCE), (6, STANCE)]
    wk, end = walk_start(16, 0.0, 0.03)
    clip("turn_and_start_walk_off", 66, k + wk, dict(event="dismissed", event_frame=1,
         note="from the stance: drops the bat into the bottom hand, turns toward the pavilion, first steps (chain into walk_off_dejected_loop)",
         end_position_m=dict(toward_bowler=round(end[0],2), toward_off_side=round(-end[1],2))))

for fn in (play_and_miss, beaten_defence, outside_edge, inside_edge_pad, pad_front, pad_back, body_blow, helmet, glove,
           bowled, caught, lbw, run_out_reaction, hit_wicket, walk_loop, angry_walk, start_walk): fn()

if __name__ == "__main__":
    names = sys.argv[1:] or list(CLIPS)
    data, metas = {}, {}
    for hand in ("rhb", "lhb"):
        for nm in names:
            n, keys, meta = CLIPS[nm]
            if hand == "lhb": keys = [(t, mirror_y(k)) for t, k in keys]
            full = f"{hand}_{nm}"
            frames, worst, md = make_bat(full, n, keys, None, dict(loop=meta.get("loop", False)))
            bad = {k: round(v, 3) for k, v in worst.items() if not k.endswith("@") and k.endswith("Grip") and v > 0.02}
            data[full] = frames; metas[full] = dict(file=full + ".fbx", frames=n, batter="left-handed" if hand == "lhb" else "right-handed", **meta)
            print(full, "OK" if not bad else bad, flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "rx_frames.pkl"), "wb")); json.dump(metas, open(_os.path.join(APL_WORK, "rx_meta.json"), "w"), indent=1)
