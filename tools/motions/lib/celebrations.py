"""Appeals & celebrations. Fielders/bowler: start at the origin facing the rig's forward (-Y).
Batters: batting frame (chest -Y, bowler +X), bat locked to the bottom hand."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import fielding, batting
import field_clips as FC
from fielding import *
from field_clips import READY, STAND, WITH_BALL, hands, HOLD_R, HOLD_L, place
import mixamo_motions as mm
OUTC = _os.path.join(APL_OUT, "appeals_celebrations"); os.makedirs(OUTC, exist_ok=True)
FIST = dict(T=0.9, I=1.0, M=1.0, R=1.0, P=1.0); POINT = dict(T=0.8, I=0.0, M=1.0, R=1.0, P=1.0)
SPREAD = dict(T=0.0, I=0.0, M=0.0, R=0.0, P=0.0)
def _rot(v, h): return R('Z', h) @ V(v)
def up_arms(cx, cy, z=1.93, spread=0.26, f=SPREAD, h=0.0):
    c = V((cx, cy, 0))
    return dict(rt=c + _rot((-spread, 0, z), h), lt=c + _rot((spread, 0, z), h), rp=_rot((-1,0.3,-0.3), h), lp=_rot((1,0.3,-0.3), h),
                rh=_rot((-0.2,0,1), h), rhw=0.8, lh=_rot((0.2,0,1), h), lhw=0.8, rpal=fwd(h), rpalw=0.6, lpal=fwd(h), lpalw=0.6,
                **fing("r", f), **fing("l", f))
def wide_arms(cx, cy, z=1.42, f=SPREAD, h=0.0):
    c = V((cx, cy, 0))
    return dict(rt=c + _rot((-0.72, 0.05, z), h), lt=c + _rot((0.72, 0.05, z), h), rp=_rot((0,1,-0.5), h), lp=_rot((0,1,-0.5), h),
                rh=_rot((-1,0,0.05), h), rhw=0.8, lh=_rot((1,0,0.05), h), lhw=0.8, **fing("r", f), **fing("l", f))
def xf(p, x=0, y=0, h=0):
    q = place(p, x, y, h)
    if abs(h) > 1e-9:
        b = R('Z', h) @ V((p["side"], -p["lean"], 0)); q["side"], q["lean"] = b.x, -b.y
    return q

FIELD = {}
def fclip(name, n, keys, meta, loop=False): FIELD[name] = (n, sorted(keys, key=lambda k: k[0]), meta, loop)

# ================================================================= BOWLER: appeals (after the follow-through, turning to the umpire behind him)
UMP = V((0, 9.0, 1.75))
def follow_through_turn(f_end=24):
    rk, _ = run_keys(READY, f0=0, f1=18, p0=(0, 0), p1=(0, -2.4), heading=0, period=18, contact=0.38, lean=10, ease="decel")
    turn = P(xf(STAND, 0, -2.55, 120), hips=H(0, -2.55, 0.98), look=UMP)
    return rk + [(f_end, turn)]
def bowler_appeal(name, kind):
    k = follow_through_turn()
    face = xf(STAND, 0, -2.6, 180)
    if kind == "lbw":
        shout = P(face, hips=H(0, -2.62, 1.30), footup=0.6, rball=V((-0.12,-2.55,0.25)), lball=V((0.12,-2.6,0.28)), rheel=50, lheel=50,
                  lean=-8, **up_arms(0, -2.55, 2.18, h=180), look=UMP + V((0, 0, 0.6)))
        hold = P(face, lean=-10, **up_arms(0, -2.55, 1.93, h=180), look=UMP)
        k += [(28, P(face, hips=H(0,-2.6,0.86), **up_arms(0,-2.62,1.55, h=180), look=UMP)), (34, shout), (42, hold), (66, hold)]
        note = "follows through, spins round to the umpire and leaps with both arms up: 'Howzat!'"
    elif kind == "caught_behind":
        point = P(face, lean=-6, rt=V((0.18,-2.70,1.95)), rp=V((1,-0.3,-0.3)), rh=V((0,0,1)), rhw=1.0, **fing("r", POINT),
                  lt=V((-0.45,-2.90,1.40)), lp=V((-1,-0.5,-0.3)), lh=V((-0.6,-1,0.3)), lhw=0.8, **fing("l", SPREAD), look=UMP)
        k += [(30, P(face, **up_arms(0, -2.55, 1.93, h=180), look=UMP)), (40, point), (50, P(point, lt=V((-0.40,-3.00,1.55)))), (66, point)]
        note = "turns to the umpire, both arms up, then the index finger goes up while the other hand points back at the keeper"
    else:
        knees = P(face, hips=H(0, -2.45, 0.58), lean=-14, rball=V((-0.15,-2.95,0)), lball=V((0.15,-2.95,0)), rheel=75, lheel=75,
                  rkp=V((0,0.3,-1)), lkp=V((0,0.3,-1)), rtoe=V((0,1,0)), ltoe=V((0,1,0)), **up_arms(0, -2.40, 1.45, 0.28, h=180), look=UMP + V((0,0,0.8)))
        k += [(30, P(face, **up_arms(0, -2.55, 1.93, h=180), look=UMP)), (40, knees), (70, P(knees, lean=-18))]
        note = "turns to the umpire and drops onto both knees, arms raised: the full-blooded appeal"
    end = P(face, rt=V((0.20,-2.62,1.0)), lt=V((-0.20,-2.62,1.0)), look=UMP)
    k += [(84, end), (99, end)]
    fclip(name, 100, k, dict(appeal_frame=35 if kind == "lbw" else 31, note=note, faces_at_end="toward the umpire (behind the start direction)"))
bowler_appeal("bowler_appeal_lbw", "lbw")
bowler_appeal("bowler_appeal_caught_behind", "caught_behind")
bowler_appeal("bowler_appeal_on_knees", "knees")

# ================================================================= BOWLER: wicket celebrations
def aeroplane():
    rk, _ = run_keys(READY, f0=0, f1=70, p0=(0, 0), p1=(4.5, -7.0), heading=-30, period=18, contact=0.34, lean=8)
    k = [(t, P(kk, **wide_arms(V(kk["hips"]).x, V(kk["hips"]).y, 1.40, h=-30), side=10*math.sin(t/7), look=V(kk["hips"]) + V((5, -8, 1.5)))) for t, kk in rk]
    rk2, _ = run_keys(READY, f0=70, f1=92, p0=(4.5, -7.0), p1=(5.4, -8.4), heading=-30, period=20, contact=0.42, lean=4, ease="decel")
    k += rk2[1:] + [(104, xf(STAND, 5.5, -8.6, -30)), (114, xf(STAND, 5.5, -8.6, -30))]
    fclip("bowler_celebration_aeroplane", 115, k, dict(note="runs off with both arms out like wings, banking side to side (root motion ~10 m)"))
def fist_pump():
    base = STAND
    crouch = P(base, hips=H(0, 0.02, 0.88), lean=12, rt=V((-0.25,-0.25,1.25)), lt=V((0.25,-0.25,1.25)), rp=V((-1,0.3,-1)), lp=V((1,0.3,-1)),
               **fing("r", FIST), **fing("l", FIST), look=V((0, -6, 0.8)))
    pump = P(crouch, hips=H(0, 0.02, 0.84), lean=18, rt=V((-0.28,-0.18,1.00)), lt=V((0.28,-0.18,1.00)))
    roar = P(base, hips=H(0, 0.05, 1.0), lean=-16, rt=V((-0.40,-0.10,1.45)), lt=V((0.40,-0.10,1.45)), rp=V((-1,0.3,-0.6)), lp=V((1,0.3,-0.6)),
             **fing("r", FIST), **fing("l", FIST), look=V((0, -5, 6.0)))
    k = [(0, base), (6, base), (14, crouch), (19, pump), (24, crouch), (29, pump), (36, roar), (64, roar), (80, base), (89, base)]
    fclip("bowler_celebration_fist_pump_roar", 90, k, dict(note="crouches, double fist pump, then leans back and roars at the sky"))
def knee_slide():
    rk, _ = run_keys(READY, f0=0, f1=30, p0=(0, 0), p1=(0, -5.2), heading=0, period=17, contact=0.32, lean=12)
    sl = dict(rball=None)
    def kp(y, z, lean):
        return P(STAND, hips=H(0, y, z), lean=lean, rball=V((-0.15, y+0.55, 0)), lball=V((0.15, y+0.55, 0)), rheel=80, lheel=80,
                 rkp=V((0,-0.3,-1)), lkp=V((0,-0.3,-1)), **wide_arms(0, y, 1.02), look=V((0, y-6, 3.0)))
    k = rk + [(36, kp(-6.1, 0.62, -12)), (44, kp(-7.0, 0.58, -22)), (52, kp(-7.4, 0.58, -26)), (70, kp(-7.45, 0.58, -24)),
              (86, P(STAND, hips=H(0, -7.3, 0.62), lean=20, rball=V((-0.18,-7.8,0)), lball=V((0.18,-7.0,0)), lheel=75, lkp=V((0,-0.3,-1)),
                     rt=V((-0.1,-7.6,0.8)), lt=V((0.1,-7.6,0.8)))), (102, xf(STAND, 0, -7.5)), (109, xf(STAND, 0, -7.5))]
    fclip("bowler_celebration_knee_slide", 110, k, dict(note="sprints, drops onto both knees and slides ~1.3 m leaning back with arms wide, gets up"))
def point_sky():
    pts = P(STAND, lean=-10, rt=V((-0.20,-0.08,1.94)), lt=V((0.20,-0.08,1.94)), rp=V((-1,0.3,-0.3)), lp=V((1,0.3,-0.3)),
            rh=V((0,0,1)), rhw=1.0, lh=V((0,0,1)), lhw=1.0, **fing("r", POINT), **fing("l", POINT), look=V((0, -2, 12)))
    k = [(0, STAND), (8, STAND), (20, pts), (60, pts), (78, STAND), (89, STAND)]
    fclip("bowler_celebration_point_to_sky", 90, k, dict(note="both index fingers pointed to the sky, looking up"))
aeroplane(); fist_pump(); knee_slide(); point_sky()

# ================================================================= FIELDERS
def fielder_appeal():
    k = [(0, READY), (6, READY), (14, P(STAND, **up_arms(0, -0.05, 1.70))),
         (20, P(STAND, hips=H(0, -0.05, 1.25), footup=0.6, rball=V((-0.12,-0.05,0.20)), lball=V((0.12,-0.05,0.22)), rheel=50, lheel=50, **up_arms(0, -0.08, 2.16), look=V((0, -18, 3.0)))),
         (28, P(STAND, **up_arms(0, -0.05, 1.93), look=V((0, -18, 2.2)))), (56, P(STAND, **up_arms(0, -0.05, 1.93), look=V((0, -18, 2.2)))), (72, STAND), (79, STAND)]
    fclip("fielder_appeal_standing", 80, k, dict(appeal_frame=21, note="springs up from the ready position, both arms up, appealing to the umpire in front"))
SLIP = P(READY, hips=H(0, 0.03, 0.78), lean=30, rball=V((-0.25,-0.04,0)), lball=V((0.25,-0.04,0)), rkp=V((-0.4,-1,0.2)), lkp=V((0.4,-1,0.2)),
         **hands(V((0, -0.45, 0.62)), "cup", fd=V((0,-0.5,-0.8)), pd=V((0,-1,0.3))), look=V((0, -18, 1.0)))
def slip_appeal():
    k = [(0, SLIP), (10, SLIP), (18, P(STAND, hips=H(0, -0.02, 1.0), **up_arms(0, -0.05, 1.85))),
         (24, P(STAND, hips=H(0, -0.05, 1.28), footup=0.6, rball=V((-0.12,-0.05,0.22)), lball=V((0.12,-0.05,0.24)), rheel=50, lheel=50, **up_arms(0, -0.08, 2.16), look=V((0,-18,3)))),
         (32, P(STAND, **up_arms(0, -0.05, 1.93), look=V((0, -18, 2.2)))), (58, P(STAND, **up_arms(0, -0.05, 1.93))), (76, STAND), (89, STAND)]
    fclip("slip_appeal_from_crouch", 90, k, dict(appeal_frame=25, note="slip-cordon crouch -> jumps up with both arms raised; the first frames double as a slip stance"))
    fclip("slip_stance_loop", 61, [(0, SLIP), (30, P(SLIP, hips=V(SLIP["hips"]) + V((0, 0, 0.012)))), (60, SLIP)], dict(loop=True, note="slip fielder's crouched ready stance"), loop=True)
SHORTLEG = P(READY, hips=H(0, 0.04, 0.62), lean=34, rball=V((-0.28,-0.02,0)), lball=V((0.28,-0.02,0)), rheel=20, lheel=20, rkp=V((-0.5,-1,0.2)), lkp=V((0.5,-1,0.2)),
             **hands(V((0, -0.42, 0.50)), "cup", fd=V((0,-0.5,-0.8)), pd=V((0,-1,0.3))), look=V((0, -3, 0.6)))
def close_appeal():
    k = [(0, SHORTLEG), (10, SHORTLEG), (16, P(SHORTLEG, hips=H(0, 0.02, 0.85), lean=14, **up_arms(0, -0.15, 1.60), look=V((-6, -12, 2.0)))),
         (24, P(STAND, hips=H(0, -0.02, 1.0), **up_arms(0, -0.10, 1.93), look=V((-6, -14, 2.2)))), (56, P(STAND, **up_arms(0, -0.10, 1.93), look=V((-6, -14, 2.2)))),
         (74, STAND), (89, STAND)]
    fclip("close_fielder_appeal", 90, k, dict(appeal_frame=17, note="short leg / silly point: from a low crouch, rises appealing to the umpire"))
    fclip("close_fielder_crouch_loop", 61, [(0, SHORTLEG), (30, P(SHORTLEG, hips=V(SHORTLEG["hips"]) + V((0, 0, 0.01)))), (60, SHORTLEG)],
          dict(loop=True, note="short leg / silly point crouch"), loop=True)
def turned_down():
    hoh = P(STAND, lean=8, rt=V((-0.10,-0.02,1.72)), lt=V((0.10,-0.02,1.72)), rp=V((-1,0,0)), lp=V((1,0,0)), rh=V((0.6,0.3,0.5)), rhw=0.8, lh=V((-0.6,0.3,0.5)), lhw=0.8,
              look=V((0, -6, 0.3)))
    crouch = P(hoh, hips=H(0, 0.05, 0.60), lean=30, rheel=20, lheel=20, rt=V((-0.10,-0.05,1.30)), lt=V((0.10,-0.05,1.30)), look=V((0, -1.5, 0.0)))
    k = [(0, P(STAND, **up_arms(0, -0.05, 1.93))), (8, P(STAND, **up_arms(0, -0.05, 1.93))), (20, hoh), (36, crouch), (60, crouch),
         (76, P(STAND, pyaw=40, rt=V((-0.15,0.05,1.10)), lt=V((0.15,0.05,1.10)), look=V((4, -3, 1.0)))), (89, P(STAND, pyaw=40))]
    fclip("appeal_turned_down_hands_on_head", 90, k, dict(note="arms up from the appeal, then hands on the head, crouches in disbelief, turns away"))
def run_high_five():
    rk, _ = run_keys(READY, f0=0, f1=40, p0=(0, 0), p1=(3.0, -6.0), heading=-27, period=17, contact=0.34, lean=10)
    F = fwd(-27)
    meet = V((3.2, -6.4, 0))
    hf = P(xf(STAND, meet.x, meet.y, -27), rt=meet + F*0.36 + V((0.05, 0, 1.84)), rp=V((-1,0.3,-0.3)), rh=F + V((0,0,1.5)), rhw=0.9,
           rpal=F, rpalw=0.9, **fing("r", SPREAD), look=meet + F*1.0 + V((0, 0, 1.8)))
    pat = P(xf(STAND, meet.x, meet.y, -27), rt=meet + F*0.40 + V((-0.1, 0, 1.40)), rh=F, rhw=0.6, lt=meet + F*0.40 + V((0.25, 0, 1.40)), look=meet + F*1.0 + V((0, 0, 1.6)))
    k = rk + [(48, hf), (52, hf), (62, pat), (78, pat), (90, xf(STAND, meet.x, meet.y, -27)), (99, xf(STAND, meet.x, meet.y, -27))]
    fclip("fielder_run_to_bowler_high_five", 100, k, dict(high_five_frame=49, high_five_point_m=dict(forward=round(-(meet + F*0.36).y, 2), left=round((meet + F*0.36).x, 2), up=1.84),
          note="runs ~7 m to the bowler, high-fives at full stretch, pats him on the shoulder (place the bowler 0.9 m in front at the end)"))
def high_five():
    hf = P(STAND, rt=V((-0.10,-0.36,1.84)), rp=V((-1,0.3,-0.3)), rh=V((0,-1,1.5)), rhw=0.9, rpal=V((0,-1,0)), rpalw=0.9, **fing("r", SPREAD), look=V((0,-1,1.8)))
    k = [(0, STAND), (6, STAND), (14, P(hf, rt=V((-0.12,-0.25,1.80)))), (18, hf), (24, P(STAND, rt=V((-0.15,-0.25,1.30)))), (40, STAND), (49, STAND)]
    fclip("high_five_standing", 50, k, dict(high_five_frame=19, high_five_point_m=dict(forward=0.36, left=-0.10, up=1.84),
          note="standing high five with the right hand; two players 0.85 m apart facing each other meet in the middle"))
def huddle():
    base = P(STAND, hips=H(0, 0.0, 0.92), lean=30, rt=V((-0.55,-0.25,1.25)), lt=V((0.55,-0.25,1.25)), rp=V((0,1,-0.6)), lp=V((0,1,-0.6)),
             rh=V((-0.6,-0.6,-0.2)), rhw=0.6, lh=V((0.6,-0.6,-0.2)), lhw=0.6, rball=V((-0.22,0.02,0)), lball=V((0.22,0.02,0)), look=V((0,-1.2,0.0)))
    k = []
    for i in range(5):
        t = i*16
        k += [(t, base), (t+8, P(base, hips=V(base["hips"]) + V((0, 0, -0.05)), lean=33))]
    k.append((80, base))
    fclip("team_huddle_loop", 81, k, dict(loop=True, note="arms round the neighbours' shoulders, bent in, bouncing. Place 6-8 copies on a 0.75 m-radius circle, each facing the centre; vary the start frame"), loop=True)
def applause():
    clap_o = P(STAND, rt=V((-0.12,-0.30,1.25)), lt=V((0.12,-0.30,1.25)), rp=V((-1,0.3,-0.6)), lp=V((1,0.3,-0.6)),
               rh=V((0.4,-0.6,0.6)), rhw=0.8, lh=V((-0.4,-0.6,0.6)), lhw=0.8, rpal=V((1,0,0)), rpalw=0.9, lpal=V((-1,0,0)), lpalw=0.9, look=V((0,-15,1.6)))
    clap_c = P(clap_o, rt=V((-0.04,-0.32,1.25)), lt=V((0.04,-0.32,1.25)))
    k = [(i*7, clap_o if i % 2 == 0 else clap_c) for i in range(9)]
    fclip("applause_loop", 57, k + [(56, clap_o)], dict(loop=True, note="clapping at chest height"), loop=True)
def catch_toss():
    toss = P(STAND, rt=V((-0.15,-0.18,1.94)), rp=V((-1,0.3,-0.3)), rh=V((0,0,1)), rhw=0.9, **fing("r", SPREAD), look=V((0, -0.5, 8)))
    k = [(0, WITH_BALL), (6, WITH_BALL), (12, P(WITH_BALL, hips=H(0, 0.02, 0.92), rt=V((-0.15,-0.20,0.95)))), (18, toss),
         (30, P(STAND, **wide_arms(0, 0, 1.45), look=V((0, -0.5, 10)))), (52, P(STAND, **wide_arms(0, 0, 1.45), lean=-10, look=V((0, -10, 3)))),
         (70, P(STAND, **up_arms(0, -0.05, 1.93), look=V((0, -10, 3)))), (88, STAND), (99, STAND)]
    fclip("catch_celebration_ball_in_the_air", 100, k, dict(release_frame=19, release_velocity_m_s=dict(forward=0.3, left=0, up=9.0),
          note="after a catch: flings the ball high in the air, arms out wide, then both arms up; let the ball fall away"))
fielder_appeal(); slip_appeal(); close_appeal(); turned_down(); run_high_five(); high_five(); huddle(); applause(); catch_toss()

# ================================================================= BATTERS (with bat) - right-handed, mirrored for left-handers
from shots import STANCE
from batting import bat_g, make_bat
from running_bat import carry_bat, free_arm, mirror_y, Hw, HIP0 as HIPB
BATS = {}
def bclip(name, n, keys, meta): BATS[name] = (n, sorted(keys, key=lambda k: k[0]), meta)
def bat_up(x, y, z=2.05, lean_back=0.0):
    """bat raised high in the bottom hand, blade to the sky"""
    return bat_g((x - 0.10, y - 0.05, z), (0.05 - lean_back, 0.0, 1), (1, 0, 0))
STAND_B = P(STANCE, hips=Hw(0, 0.0, 1.02), lean=2, side=0, twist=0, rball=V((-0.12, 0.0, 0)), lball=V((0.12, 0.0, 0)), rtoe=V((0,-1,0)), ltoe=V((0,-1,0)),
            **bat_g((0.18, -0.26, 0.86), (0.15, -0.2, -1), (1, 0, 0)), **free_arm(0), lt=V((0.21, 0.02, 0.92)), look=V((0, -20, 1.4)))
PAV = V((-25, 20, 6))                                                    # the dressing room, behind square on the leg side
def fifty():
    up = P(STAND_B, pyaw=-35, **bat_up(0, 0), lt=V((0.25, -0.1, 1.85)), lpole=V((1, 0.3, -0.3)), look=PAV)
    wave = P(up, **bat_up(0.08, 0.0), lt=V((0.30, -0.12, 1.90)))
    turn = P(up, pyaw=40, look=V((20, -20, 6)))
    k = [(0, STANCE), (6, STANCE), (16, STAND_B), (28, up), (36, wave), (44, up), (52, wave), (66, turn), (86, turn), (100, STAND_B), (114, STANCE), (119, STANCE)]
    bclip("bat_fifty_raise_bat", 120, k, dict(raise_frame=29, note="takes the top hand off, raises the bat to the dressing room, waves it, turns to the rest of the ground"))
def hundred():
    leap = P(STAND_B, hips=Hw(0, 0.0, 1.38), footup=0.6, rball=V((-0.12, 0.0, 0.32)), lball=V((0.12, 0.0, 0.34)), rheel=50, lheel=50,
             **bat_up(0, 0, 2.20), lt=V((0.30, -0.05, 2.15)), lpole=V((1, 0.3, -0.3)), look=V((0, -5, 8)))
    land = P(STAND_B, hips=Hw(0, 0.0, 0.92), lean=10, **bat_up(0, 0, 1.95), lt=V((0.30, -0.05, 1.95)), look=V((0, -8, 3)))
    helmet_hold = P(STAND_B, **bat_up(0, 0, 2.0), lt=V((0.05, 0.02, 1.80)), lpole=V((1, 0, -0.3)), look=V((0, -6, 3)))
    helmet_up = P(helmet_hold, lt=V((0.30, -0.05, 2.10)), look=PAV)
    kiss = P(STAND_B, **bat_up(0, 0, 1.95), lt=V((0.10, -0.12, 1.32)), lpole=V((1, 0.4, -1)), look=V((0, -3, 6)))
    k = [(0, STANCE), (6, STANCE), (14, P(STAND_B, hips=Hw(0, 0, 0.88), lean=8)), (22, leap), (30, land), (40, P(land, hips=Hw(0, 0, 1.02), lean=0)),
         (54, helmet_hold), (64, helmet_up), (90, P(helmet_up, pyaw=-30)), (110, P(helmet_up, pyaw=30, look=V((20, -20, 6)))), (124, kiss), (140, kiss),
         (156, STAND_B), (170, STANCE), (179, STANCE)]
    bclip("bat_hundred_leap_helmet_off", 180, k, dict(leap_frame=23, helmet_off_frame=56, helmet_on_frame=154,
          note="leaps with bat and arm raised, takes the helmet off with the top hand (attach the helmet to the top hand between helmet_off_frame and helmet_on_frame), shows bat and helmet to the dressing room and the crowd, hand to the badge"))
def winning_runs():
    rk, _ = run_keys(STANCE, f0=10, f1=60, p0=(0.2, -0.3), p1=(9.0, -1.0), heading=88, period=16, contact=0.30, lean=12, ease="accel", first="r")
    k = [(0, STANCE), (6, STANCE)]
    for t, kk in rk:
        hw = V(kk["hips"]) + V((0, 0, HIPB))
        k.append((t, P(kk, **carry_bat(hw, 88), **free_arm(88), look=hw + V((10, 0, 0.5)))))
    stop = V((10.0, -1.05, 0))
    yell = P(STAND_B, hips=Hw(stop.x, stop.y, 1.0), pyaw=90, lean=-10, rball=V((stop.x - 0.15, stop.y - 0.12, 0)), lball=V((stop.x + 0.15, stop.y + 0.12, 0)),
             rtoe=V((1, 0, 0)), ltoe=V((1, 0, 0)), **bat_up(stop.x, stop.y, 2.05), lt=V((stop.x + 0.1, stop.y + 0.30, 2.05)), lpole=V((0, 1, -0.3)),
             look=V((stop.x + 10, stop.y, 6)))
    jump = P(yell, hips=Hw(stop.x, stop.y, 1.40), footup=0.6, rball=V((stop.x - 0.15, stop.y - 0.12, 0.35)), lball=V((stop.x + 0.15, stop.y + 0.12, 0.38)), rheel=50, lheel=50,
             **bat_up(stop.x, stop.y, 2.25))
    swing = P(yell, **bat_g((stop.x + 0.25, stop.y - 0.35, 1.70), (0.5, -0.5, 0.7), (0, 0, 1)))
    k += [(70, yell), (78, jump), (86, yell), (96, swing), (104, yell), (130, yell), (149, yell)]
    bclip("bat_winning_runs_celebration", 150, k, dict(note="hits the winning runs: sets off running, pulls up, bat and arm raised, leaps, swings the bat round, roars (root motion ~10 m)"))
def glove_punch():
    k = [(0, STANCE), (6, STANCE)]
    wk, _ = run_keys(STANCE, f0=10, f1=40, p0=(0.2, -0.4), p1=(1.8, -0.9), heading=80, period=32, contact=0.6, lean=3, bob=0.015, arm_amp=0.10, hip_h=1.01, first="r")
    for t, kk in wk:
        hw = V(kk["hips"]) + V((0, 0, HIPB))
        k.append((t, P(kk, **carry_bat(hw, 80), **free_arm(80), look=hw + V((3, 0, 0.3)))))
    m = V((2.05, -0.95, 0))
    stand = P(STAND_B, hips=Hw(m.x, m.y, 1.02), pyaw=90, rball=V((m.x - 0.12, m.y - 0.10, 0)), lball=V((m.x + 0.05, m.y + 0.12, 0)), rtoe=V((1,0,0)), ltoe=V((1,0,0)),
              **bat_g((m.x + 0.05, m.y - 0.30, 0.86), (0.25, -0.1, -1), (0, -1, 0)), lt=V((m.x + 0.25, m.y + 0.18, 1.10)), lpole=V((0, 1, -0.6)), look=V((m.x + 1.5, m.y, 1.6)))
    punch = P(stand, lt=V((m.x + 0.45, m.y + 0.10, 1.20)), **fing("l", FIST))
    k += [(48, stand), (54, punch), (58, punch), (66, P(stand, lt=V((m.x + 0.30, m.y + 0.15, 1.15)), look=V((m.x + 1.5, m.y + 0.3, 1.6)))), (90, stand), (99, stand)]
    bclip("bat_mid_pitch_glove_punch", 100, k, dict(glove_punch_frame=55, glove_punch_point_m=dict(toward_bowler=round(m.x + 0.47, 2), toward_off_side=round(-(m.y + 0.10), 2), up=1.2),
          note="walks down the pitch, a few words and a glove punch with the partner (place the partner 0.95 m further down the pitch facing him)"))
def acknowledge():
    ack = P(STAND_B, **bat_up(0, 0, 1.95), lt=V((0.30, -0.10, 1.90)), lpole=V((1, 0.3, -0.3)), look=V((-5, -25, 6)))
    k = [(0, STANCE), (6, STANCE), (16, STAND_B), (28, ack), (48, P(ack, pyaw=-50, look=PAV)), (70, P(ack, pyaw=40, look=V((20, -20, 6)))), (86, STAND_B), (99, STANCE), (104, STANCE)]
    bclip("bat_acknowledge_applause", 105, k, dict(note="raises the bat and the top hand to acknowledge the crowd, turning to each side of the ground"))
fifty(); hundred(); winning_runs(); glove_punch(); acknowledge()

if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    data, metas = {}, {}
    if which in ("all", "field"):
        fielding.OUTF = OUTC
        for nm, (n, keys, meta, loop) in FIELD.items():
            frames = make_field(nm, n, keys, loop=loop, lift=True, reach=None)
            data[nm] = frames; metas[nm] = dict(file=nm + ".fbx", frames=n, **dict(dict(loop=loop), **meta))
            print(nm, "ok max hand err %.3f" % max(make_field.last_errs), flush=True)
    if which in ("all", "bat"):
        batting.OUT = OUTC
        for hand in ("rhb", "lhb"):
            for nm, (n, keys, meta) in BATS.items():
                ks = keys if hand == "rhb" else [(t, mirror_y(k)) for t, k in keys]
                full = f"{hand}_{nm}"
                frames, worst, md = make_bat(full, n, ks, None, dict(loop=False))
                bad = {k: round(v, 3) for k, v in worst.items() if not k.endswith("@") and k == ("RightGrip" if hand == "rhb" else "LeftGrip") and v > 0.015}
                data[full] = frames; metas[full] = dict(file=full + ".fbx", frames=n, batter="left-handed" if hand == "lhb" else "right-handed", **meta)
                print(full, "OK" if not bad else bad, flush=True)
    pickle.dump(data, open(f_os.path.join(APL_WORK, "cel_frames_{which}.pkl"), "wb")); json.dump(metas, open(f_os.path.join(APL_WORK, "cel_meta_{which}.json"), "w"), indent=1)
