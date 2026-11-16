"""
Ceremony clips for everyone but the batters (fielding conventions: start at the origin facing -Y, x = his left,
all travel in the hips, frames 1-based in the metadata). Bowler clips use the bowling world (crease at the origin,
batter at -Y) and come as ra_ / la_ (bowling.mirror).

  fielder_walk_out_loop / fielder_walk_out_clapping_loop   the fielding side walking out together (in place)
  fielder_walk_back_to_position     turns, walks ~6 m back, turns to face the batter, settles into the ready stance
  captain_toss_flip / captain_toss_watch                   the toss (layout in the JSON); coin path given per frame
  captain_toss_win_choose_bat / captain_toss_win_choose_bowl
  presenter_talk_loop / presenter_hold_mic_out_loop
  ra_/la_intro_polish_ball_loop, ra_/la_mark_run_up        the bowler's intro idle and pacing out his run-up

    motion.ps1 gen drivers\\ceremony_field.py [clip names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ceremony_common import OUT, write_meta
import mixamo_motions as mm
import fielding
from fielding import V, P, BASE, H, HIP0, fing, RELAXF, HOLDF, run_keys, inplace, fwd, left, make_field
from field_clips import STAND, READY, place
import bowling as BW

fielding.OUTF = OUT
CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
def export(name, n, keys, meta, loop=False, overlay=None, mirror_la=False, reach=None):
    for arm in (("ra", "la") if mirror_la else ("",)):
        full = f"{arm}_{name}" if arm else name
        ks = [(t, BW.mirror(k)) for t, k in keys] if arm == "la" else keys
        ov = overlay
        if arm == "la" and overlay:
            def ov(f, p, base=overlay):
                q = BW.mirror(p); base(f, q); r = BW.mirror(q); p.clear(); p.update(r)
        frames = make_field(full, n, ks, overlay=ov, loop=loop, lift=True, reach=reach)
        low = min(min(fr[0][mm.PFX + b][2] for b in ("RightToeBase", "LeftToeBase")) for fr in frames)
        seam = max(abs(a - b) for nm_ in frames[0][0] for a, b in zip(frames[0][0][nm_], frames[-1][0][nm_])) if loop else 0
        print(f"{full}: {n} f  min toe z {low:+.3f}  max hand err {max(make_field.last_errs):.3f}" + (f"  loop seam {seam*1000:.1f} mm" if loop else ""), flush=True)
        write_meta(full, dict(file=full + ".fbx", frames=n, loop=loop, **({"arm": "left-arm" if arm == "la" else "right-arm"} if arm else {}), **meta))

def step_turn(t0, h0, h1, x, y, dt=7, base=None):
    """turn on the spot in two half-turns, one foot stepping at a time (place() alone moves both feet at once: a hop)"""
    base = base or STAND
    out, h = [], h0
    for hn in (h0 + (h1 - h0)/2, h1):
        lead = "l" if hn > h else "r"; tr = "r" if lead == "l" else "l"
        a = place(base, x, y, (h + hn)/2); old = place(base, x, y, h); new = place(base, x, y, hn)
        a[lead + "ball"], a[lead + "toe"] = new[lead + "ball"], new[lead + "toe"]
        a[tr + "ball"], a[tr + "toe"] = old[tr + "ball"], old[tr + "toe"]
        t0 += dt; out.append((t0, a)); t0 += dt; out.append((t0, new)); h = hn
    return out

def walk_loop(period=34, stride=0.74, heading=0.0):
    n = 2*period + 1; d = fwd(heading); dist = stride*4
    kk, _ = run_keys(STAND, f0=0, f1=n - 1, p0=(0, 0), p1=tuple(d*dist), heading=heading, period=period, contact=0.62,
                     lean=3, bob=0.015, arm_amp=0.12, hip_h=1.02)
    return n, inplace(kk, 0, n - 1, (0, 0), tuple(d*dist)), dist/((n - 1)/30.0)

# ---------------------------------------------------------------- fielders
@clip("fielder_walk_out_loop")
def walk_out():
    n, kk, v = walk_loop()
    def ov(f, p):                                           # a glance at the team-mate alongside and back
        p["look"] = V(p["hips"]) + V((0, 0, HIP0)) + V((6*math.sin(2*math.pi*f/(n - 1)), -12, 0.5))
    export("fielder_walk_out_loop", n, kk, dict(type="walk-out", in_place=True, speed_m_s=round(v, 2),
           note="the fielding side walking out together: relaxed walk, arms swinging, a glance to the team-mate either side"), loop=True, overlay=ov)

@clip("fielder_walk_out_clapping_loop")
def walk_out_clap():
    n, kk, v = walk_loop()
    def ov(f, p):
        hw = V(p["hips"]) + V((0, 0, HIP0)); c = 0.5 + 0.5*math.cos(2*math.pi*f/(n - 1)*6)       # 6 claps a loop (2.6 Hz)
        gap = 0.03 + 0.11*c
        p["rt"] = hw + V((-gap, -0.30, 0.20)); p["lt"] = hw + V((gap, -0.30, 0.21))
        p["rp"] = V((-1, 0.3, -0.6)); p["lp"] = V((1, 0.3, -0.6))
        p["rh"] = V((0.15, -0.4, 1)); p["lh"] = V((-0.15, -0.4, 1)); p["rhw"] = p["lhw"] = 0.7
        p["rpal"] = V((1, 0, 0)); p["lpal"] = V((-1, 0, 0)); p["rpalw"] = p["lpalw"] = 0.8
    export("fielder_walk_out_clapping_loop", n, kk, dict(type="walk-out", in_place=True, speed_m_s=round(v, 2), clap_frames=[1 + round(i*(n - 1)/6) for i in range(6)],
           note="walking out clapping (6 claps per loop, hands meeting in front of the chest)"), loop=True, overlay=ov)

@clip("fielder_walk_back_to_position")
def walk_back():
    D = 6.0
    st = P(STAND)
    wk, _ = run_keys(STAND, f0=24, f1=24 + 9*17, p0=(0, 0.10), p1=(0, 0.10 + D), heading=180, period=34, contact=0.62, lean=3,
                     bob=0.015, arm_amp=0.11, hip_h=1.02, first="l")
    e = 24 + 9*17
    back = [(t, P(k, look=V(k["hips"]) + V((0, 6, 1.0)))) for t, k in wk]
    fin = place(READY, 0.0, 0.10 + D + 0.05, 0)
    tb = step_turn(4, 0, 180, 0.0, 0.10, dt=5)
    keys = [(0, st), (4, st)] + tb + back[1:] + step_turn(e + 2, 180, 0, 0.0, 0.10 + D + 0.05, dt=6) + [(e + 36, fin), (e + 50, fin)]
    export("fielder_walk_back_to_position", e + 51, keys, dict(type="walk to position", end_position_m=dict(x=0.0, y=round(0.15 + D, 2)),
           end_facing="-Y (the batter)", ends_in="the first pose of run_00_ready_stance_idle_loop (READY)",
           note="turns round, walks ~6 m back to his position, turns to face the batter and crouches into the ready stance"))

# ---------------------------------------------------------------- the toss
# layout: flipper at the origin facing -Y (the presenter / camera), the other captain 1.0 m to his left (x=+1.0) facing -Y,
# presenter at (0.5, -1.6) facing +Y. Coin: flipped from the flipper's right hand, apex 2.9 m, lands at (0.15, -0.70).
REL_F, LAND_F = 36, 76
C0 = V((-0.14, -0.33, 1.32)); CLAND = V((0.15, -0.70, 0.0)); APEX_Z = 2.9
def coin(f):
    """the coin's position at (0-based) frame f, flipper's frame"""
    if f <= REL_F: return C0
    t_up = math.sqrt(2*(APEX_Z - C0.z)/9.81); t_dn = math.sqrt(2*APEX_Z/9.81); T = t_up + t_dn
    s = min(1.0, (f - REL_F)/30.0/T); tt = s*T
    z = C0.z + 9.81*t_up*tt - 4.905*tt*tt if s < 1 else 0.0
    xy = C0.lerp(CLAND, s)
    return V((xy.x, xy.y, max(0.0, z)))
COIN_META = dict(coin_release_frame=REL_F + 1, coin_land_frame=LAND_F + 1, coin_release_m=dict(x=round(C0.x, 2), y=round(C0.y, 2), up=round(C0.z, 2)),
                 coin_apex_up_m=APEX_Z, coin_land_m=dict(x=CLAND.x, y=CLAND.y),
                 layout="flipper at the origin facing -Y; the other captain at x=+1.0 facing -Y; presenter at (0.5, -1.6) facing +Y; coin positions in the flipper's frame (subtract 1.0 from x for the other captain's)")
CLASP = dict(rt=V((-0.04, -0.17, 0.92)), lt=V((0.04, -0.17, 0.92)), rp=V((-0.8, 0.4, -0.6)), lp=V((0.8, 0.4, -0.6)))

EYE_Z = 1.68                                      # eye height: a level gaze at a standing person (round 7: heads were down)
PRES_EYES = V((0.5, -1.6, EYE_Z))                 # the presenter's eyes, in the flipper's frame
def look_coin(dx=0.0):
    """eyes follow the coin up and down, a short glance at it on the ground, then back up to the presenter's eyes
    (dx: the watcher's offset). Round 7: they used to stare at the coin on the turf for 40 frames."""
    def ov(f, p):
        if REL_F - 4 <= f <= LAND_F + 24:
            c = (coin(f) if f <= LAND_F else CLAND) + V((dx, 0, 0))
            if f > LAND_F + 12:
                c = c.lerp(PRES_EYES + V((dx, 0, 0)), min(1.0, (f - LAND_F - 12)/12.0))
            p["look"] = V(p["look"]).lerp(c, min(1.0, (f - (REL_F - 4))/4.0))
    return ov

@clip("captain_toss_flip")
def toss_flip():
    hold = P(STAND, rt=C0 + V((0.02, 0.04, -0.06)), rp=V((-1, 0.3, -0.5)), rh=V((0.1, -0.6, 0.8)), rhw=0.6, rpal=V((0.3, 0, 1)), rpalw=0.8,
             rfw=1.0, rT=0.75, rI=0.65, rM=0.8, rR=0.85, rP=0.9, lt=CLASP["lt"], lp=CLASP["lp"], look=PRES_EYES)
    cock = P(hold, rt=C0 + V((0.02, 0.05, -0.12)), rT=0.95, look=C0)
    flick = P(hold, rt=C0 + V((0.0, -0.03, 0.08)), rh=V((0.1, -0.4, 1)), rT=0.05, rI=0.4, look=C0 + V((0, 0, 0.3)))
    down = P(STAND, rt=V((-0.20, -0.08, 0.95)), lt=CLASP["lt"], lp=CLASP["lp"])
    peer = P(down, lean=6, hips=H(0, 0.02, 1.02))
    keys = [(0, hold), (24, hold), (32, cock), (36, flick), (44, P(flick, rt=C0 + V((0, -0.02, 0.0)))), (62, down), (78, peer), (88, peer),
            (100, P(down, look=PRES_EYES)), (129, P(down, look=PRES_EYES))]
    export("captain_toss_flip", 130, keys, dict(type="toss", **COIN_META, prop="coin on RightHand until coin_release_frame",
           note="holds the coin on the thumb, cocks it, flicks it up; watches it rise and fall, a short glance at it on the ground, then eyes up (level) to the presenter"),
           overlay=look_coin())

@clip("captain_toss_watch")
def toss_watch():
    st = P(STAND, **CLASP, look=PRES_EYES + V((-1.0, 0, 0)))
    peer = P(st, lean=6, hips=H(0, 0.02, 1.02))
    keys = [(0, st), (24, P(st, look=V((-1.0, -0.4, 1.40)))), (40, st), (78, peer), (88, peer), (100, st), (129, st)]
    export("captain_toss_watch", 130, keys, dict(type="toss", **COIN_META,
           note="the other captain, hands clasped in front: watches the flip, follows the coin up and down, a short glance at it on the ground, then eyes up (level) to the presenter"), overlay=look_coin(-1.0))

@clip("captain_toss_win_choose_bat")
def choose_bat():
    st = P(STAND, **CLASP)
    turn = place(P(STAND, **CLASP), 0, 0, 25)
    grip = place(P(STAND, rt=V((-0.06, -0.30, 1.02)), lt=V((-0.02, -0.30, 1.10)), rp=V((-1, 0.2, -0.5)), lp=V((0.8, 0.3, -0.6)),
                   rh=V((0.3, -0.5, -1)), lh=V((0.3, -0.5, -1)), rhw=0.5, lhw=0.5, **fing("r", HOLDF), **fing("l", HOLDF)), 0, 0, 25)
    lift = P(grip, rt=V((-0.10, -0.15, 1.30)), lt=V((-0.03, -0.17, 1.37)), twist=-12)
    drive = P(grip, rt=V((0.12, -0.42, 0.95)), lt=V((0.15, -0.38, 1.02)), twist=14, lean=8)
    nod = P(turn, look=V((0.6, -1.6, 1.45)))
    keys = [(0, st), (10, P(st, look=V((0.5, -1.6, 1.4)))), (22, turn), (32, grip), (42, lift), (50, drive), (60, grip), (72, nod), (82, turn), (99, turn)]
    export("captain_toss_win_choose_bat", 100, keys, dict(type="toss", gesture_frame=51,
           note="turns to the presenter and mimes a short drive with both hands: 'we will have a bat'"), reach=lambda f: True)

@clip("captain_toss_win_choose_bowl")
def choose_bowl():
    st = P(STAND, **CLASP)
    turn = place(P(STAND, **CLASP), 0, 0, 25)
    point = place(P(STAND, lt=CLASP["lt"], lp=CLASP["lp"], rt=V((-0.55, -0.40, 1.45)), rp=V((-0.3, 0.6, -0.6)), rh=V((-0.7, -0.6, 0.1)), rhw=0.8,
                    **fing("r", mm.POINT)), 0, 0, 25)
    sweep = P(point, rt=V((-0.62, 0.05, 1.42)), rh=V((-1, 0.2, 0.1)))
    circ = lambda phi: P(turn, rdir=BW.arm_dir(phi, 10), rdirw=1.0, rt=V((-0.3, 0, 1.2)), twist=-6 + 0.06*phi)
    keys = [(0, st), (10, P(st, look=V((0.5, -1.6, 1.4)))), (22, turn), (34, point), (46, sweep), (56, turn), (62, circ(10)), (68, circ(100)),
            (74, circ(190)), (80, circ(280)), (86, P(turn, rdirw=0.0)), (99, turn)]
    export("captain_toss_win_choose_bowl", 100, keys, dict(type="toss", gesture_frame=47,
           note="turns to the presenter, points out to the field with a sweep of the arm, rolls the bowling arm over once: 'we will bowl'"), reach=lambda f: True)

# ---------------------------------------------------------------- presenter (microphone on the right hand)
MIC = dict(rp=V((-1, 0.3, -0.6)), rh=V((0.2, -0.3, 1)), rhw=0.7, rpal=V((1, 0, 0)), rpalw=0.6, **fing("r", HOLDF))
@clip("presenter_talk_loop")
def talk():
    base = P(STAND, rt=V((-0.06, -0.15, 1.47)), **MIC, lt=V((0.22, -0.10, 0.98)), lp=V((0.8, 0.4, -0.6)), look=V((0, -6, EYE_Z)))
    g1 = P(base, lt=V((0.24, -0.32, 1.12)), lpal=V((-0.3, 0, 1)), lpalw=0.7, **fing("l", RELAXF), look=V((0.8, -6, EYE_Z)))
    g2 = P(g1, lt=V((0.30, -0.28, 1.08)), look=V((-0.6, -6, EYE_Z)), lean=1)
    keys = [(0, base), (20, g1), (32, g2), (44, g1), (60, base), (80, P(base, look=V((0.6, -1.4, EYE_Z)), twist=8)), (100, P(base, look=V((-0.6, -1.4, EYE_Z)), twist=-8)),
            (118, g2), (132, g1), (150, base), (179, base)]
    export("presenter_talk_loop", 180, keys, dict(type="presenter", prop="microphone on RightHand",
           note="talks into the hand-held microphone to camera (-Y), open-palm beats with the free hand, turns to each captain behind him"), loop=True)

@clip("presenter_hold_mic_out_loop")
def mic_out():
    base = P(STAND, rt=V((-0.32, -0.38, 1.36)), **MIC, lt=V((0.22, -0.06, 0.95)), lp=V((0.8, 0.4, -0.6)), look=V((-1.5, -2.0, EYE_Z)), twist=-12)
    keys = [(0, base), (40, P(base, look=V((-1.5, -2.0, EYE_Z - 0.10)))), (52, base), (90, P(base, rt=V((-0.30, -0.40, 1.38)), look=V((-1.4, -2.0, EYE_Z)))), (119, base)]
    export("presenter_hold_mic_out_loop", 120, keys, dict(type="presenter", prop="microphone on RightHand",
           note="holds the microphone out toward the captain in front of him to his right and listens, small nods"), loop=True)

# ---------------------------------------------------------------- bowler intros (bowling world; la_ = mirror)
BALL = fing("r", BW.HOLD_BALL)
@clip("intro_polish_ball_loop")
def polish():
    st = P(STAND, **BALL, rt=V((-0.10, -0.25, 1.12)), rp=V((-0.8, 0.3, -0.6)), rh=V((0, -0.4, 1)), rhw=0.6, look=V((-0.1, -0.35, 1.05)))
    rub = lambda dz: P(st, rt=V((-0.15, -0.13, 0.80 + dz)), rh=V((0, -0.2, -1)), rhw=0.8, rpal=V((0, 1, 0)), rpalw=0.7, look=V((-0.2, -0.5, 0.7)), lean=8)
    toss = P(st, lt=V((0.05, -0.30, 1.12)), lp=V((0.8, 0.3, -0.6)), lh=V((0, -0.4, 1)), lhw=0.6, **fing("l", BW.HOLD_BALL), look=V((0, -0.35, 1.0)))
    keys = [(0, st), (12, rub(0.06)), (20, rub(-0.04)), (28, rub(0.06)), (36, rub(-0.04)), (44, rub(0.06)), (56, st), (70, P(st, look=V((0, -20, 1.2)))),
            (84, toss), (96, st), (110, P(st, look=V((0, -19, 1.0)))), (125, st)]
    export("intro_polish_ball_loop", 126, keys, dict(type="bowler intro", prop="ball on the bowling hand (in the other hand around frame 85)",
           note="shines the ball on the front of his thigh, checks the shine, looks down at the batter, flips it into the other hand and back"), loop=True, mirror_la=True, reach=lambda f: True)

@clip("mark_run_up")
def mark_run_up():
    PACES, ST = 8, 0.92
    wk, _ = run_keys(STAND, f0=22, f1=22 + PACES*18, p0=(0, 0.15), p1=(0, 0.15 + PACES*ST), heading=180, period=36, contact=0.64, lean=4,
                     bob=0.012, arm_amp=0.08, hip_h=1.02, first="r")
    e = 22 + PACES*18; Y = 0.15 + PACES*ST
    wk = [(t, P(k, look=V(k["hips"]) + V((0, 1.6, -0.55)))) for t, k in wk]          # eyes on his feet, counting the paces
    def scr(d):
        q = place(STAND, 0, Y + 0.05, 180)
        return P(q, rball=V((0.17 - d, Y + 0.03, 0.0)), rtoe=V((-1, 0.1, 0)), rheel=-6, look=V((0, Y + 0.6, 0)), lean=-8)
    keys = ([(0, STAND), (2, STAND)] + step_turn(2, 0, 180, 0, 0.15, dt=5) + wk[1:] + [(e + 10, scr(0.0)), (e + 18, scr(0.25)), (e + 24, scr(0.0)),
            (e + 32, scr(0.25)), (e + 38, place(STAND, 0, Y + 0.05, 180))] + step_turn(e + 38, 180, 0, 0, Y + 0.05, dt=7) + [(e + 70, place(STAND, 0, Y + 0.05, 0))])
    keys = [(t, P(k, **BALL)) for t, k in keys]
    export("mark_run_up", e + 71, keys, dict(type="bowler intro", scrape_frames=[e + 19, e + 33], mark_position_m=dict(behind_crease=round(Y + 0.05, 2), left=0.0),
           note=f"from his crease turns round and paces out {PACES} long strides looking at his feet, scrapes the mark twice with his boot, turns to face the batter"),
           mirror_la=True)

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
    import ceremony_common; ceremony_common.merge_pack()
