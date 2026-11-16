"""
Round-2 ceremony clips.
Batters (rhb_/lhb_, batting frame, bat in the bottom hand; every clip starts and ends in the batting stance):
  warmup_shadow_shots      forward defence, cover drive and pull played without a ball (the library's own shot keys, 15% slower)
  warmup_bat_stretch       stands up, bat across the back of the shoulders, trunk twists and side bends, back into the stance
  warmup_jump_and_tap      bounces on the toes, a jump, lands and taps the bat in the crease twice, stance
Toss (fielding frame, start at the origin facing -Y):
  captain_walk_to_centre   walks 7 m out to the centre, stops facing -Y
  captain_speak_into_mic   steps 0.45 m forward to the presenter's microphone, talks with small gestures, steps back
  captain_handshake        steps in and shakes hands (the other captain plays the same clip 1.0 m in front, facing him)
  captain_toss_call_watch  the calling captain: calls with a raised finger while the coin is up, follows it down, leans in

    motion.ps1 gen drivers\\ceremony_round2.py [names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ceremony_common import *
import ceremony_common
from ceremony_field import export, CLASP, COIN_META, look_coin, STAND
import fielding
from fielding import H, fing, HOLDF
import shots

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco

# ---------------------------------------------------------------- batters
@clip("warmup_shadow_shots")
def shadow():
    import batting_v2
    keys, t = [], 0.0
    for sid in (1, 5, 42):                                   # forward defence, cover drive, pull: the round-3 shots (start/end in READY)
        sh = batting_v2.SHOTS_V2[sid]; n, ks = sh["n"], sh["keys"]
        for tt, k in ks:
            if keys and tt == 0: continue
            keys.append((t + tt*1.15, dict(k)))
        t += (n - 1)*1.15
    keys.append((t + 10, batting_v2.READY))
    export_bat("warmup_shadow_shots", int(t + 11), keys, dict(type="warm-up", ends_in="the round-3 ready stance (batting_v2.READY)",
               note="shadow-bats at the crease without a ball: forward defence, cover drive, pull to mid-wicket (the round-3 batting shots 01/05/42, 15% slower), from and back to the ready stance"))

def hang(hz):
    return bat_g((-0.16, -0.27, 0.90 + (hz - 1.02)), (-0.05, -0.10, -1), (1, 0, 0))     # hanging in front of the right thigh
def upright(hz=1.02, **kw):
    from batting_v2 import READY
    feet = {k: V(READY[k]) for k in ("rball", "lball", "rtoe", "ltoe")}
    return P(P(STANCE, hips=Hw(-0.02, 0.04, hz), lean=3, side=0, twist=0, rheel=0, lheel=0, **feet, **hang(hz), **free_arm(0),
               lt=V((0.20, 0.0, hz - 0.12)), look=V((2, -20, 1.6))), **kw)

@clip("warmup_bat_stretch")
def bat_stretch():
    bg0, a0, lt0 = V((-0.42, 0.14, 1.48)), V((1, 0, 0.02)), V((0.24, 0.14, 1.49))
    def across(tw=0.0, sd=0.0):
        Rz = R('Z', tw); Ry = R('Y', sd); piv = V((0, 0, 1.05))
        f = lambda v: Ry @ (Rz @ (v - piv)) + piv
        return P(upright(), **bat_g(f(bg0), Ry @ (Rz @ a0), (0, 0, 1)), lt=f(lt0), lpole=V((1, 0.6, -0.4)), rpole=V((-1, 0.6, -0.4)),
                 twist=tw, side=sd*0.8, look=V((0, -20, 1.6)) if abs(tw) < 1 else V((20*math.sin(math.radians(-tw)), -20, 1.6)))
    from batting_v2 import READY
    keys = [(0, READY), (5, P(READY, topgrip=0.0, lt=top_on_handle(READY))), (12, upright()), (22, P(across(), topgrip=0.0, lt=top_on_handle(across()))), (26, across()), (42, across(35)), (58, across(-35)), (74, across(35)), (90, across()),
            (106, across(0, 18)), (122, across(0, -18)), (136, across()), (140, P(across(), topgrip=0.0, lt=top_on_handle(across()))),
            (143, P(upright(), **bat_g((-0.44, 0.12, 1.42), (0.25, 0.55, -0.80), (1, 0, 0)), lt=V((0.22, -0.02, 1.0)))),   # toe down behind the back ...
            (147, P(upright(), **bat_g((-0.42, -0.04, 1.28), (0.0, 0.15, -1), (1, 0, 0)), lt=V((0.22, -0.05, 1.0)))),     # ... and round the right side
            (154, upright()), (158, P(READY, topgrip=0.0, lt=top_on_handle(READY))), (164, READY), (175, READY)]
    export_bat("warmup_bat_stretch", 176, keys, dict(type="warm-up", ends_in="the round-3 ready stance (batting_v2.READY)",
               note="stands up, lays the bat across the back of his shoulders (bottom hand on the handle, top hand on the toe), twists left and right, bends to each side, back into the stance"))

@clip("warmup_jump_and_tap")
def jump_tap():
    def up(hz, heel=0, feet_z=0.0, **kw):
        return upright(hz, rheel=heel, lheel=heel, rball=V((-0.24, -0.03, feet_z)), lball=V((0.24, -0.03, feet_z)), **kw)
    from batting_v2 import READY, ST_BAT_G, ST_BAT_A
    tap = lambda dz: P(READY, **bat_g(ST_BAT_G + V((0, 0, dz)), ST_BAT_A, (1, 0, 0)))
    keys = [(0, READY), (6, P(READY, topgrip=0.0, lt=top_on_handle(READY))), (12, up(1.0)), (18, up(1.06, 40)), (23, up(1.0)), (28, up(1.06, 40)), (33, up(1.0)), (38, up(1.06, 40)),
            (44, up(0.86, 0, lean=12)), (50, up(1.12, 55)), (56, up(1.36, 30, 0.28)), (62, up(1.04, 25)), (67, up(0.88, 0, lean=10)),
            (74, P(tap(0.08), topgrip=0.0, lt=top_on_handle(tap(0.08)))), (80, tap(0.08)), (85, tap(0.08)), (89, tap(0.0)), (93, tap(0.08)), (97, tap(0.0)), (104, READY), (115, READY)]
    export_bat("warmup_jump_and_tap", 116, keys, dict(type="warm-up", jump_frames=dict(take_off=51, apex=57, landing=63), tap_frames=[90, 98],
               ends_in="the round-3 ready stance (batting_v2.READY)", note="three bounces on the toes, a jump, lands soft, settles into the stance and taps the bat in the crease twice"))

# ---------------------------------------------------------------- toss
@clip("captain_walk_to_centre")
def walk_centre():
    D = 7.0
    fielding.run_keys.last = None
    wk, _ = run_keys(STAND, f0=6, f1=6 + 9*17, p0=(0, 0), p1=(0, -D), heading=0, period=34, contact=0.62, lean=3, bob=0.015,
                     arm_amp=0.12, hip_h=1.02, first="l")
    e = 6 + 9*17
    end = P(STAND, hips=H(0, -D - 0.1, 1.03), rball=V((-0.17, -D - 0.16, 0)), lball=V((0.17, -D - 0.16, 0)),
            rt=V((-0.21, -D - 0.08, 0.90)), lt=V((0.21, -D - 0.08, 0.90)), look=V((0, -D - 20, 1.5)))
    keys = [(0, STAND)] + wk[1:] + [(e + 14, end), (e + 30, end)]
    export("captain_walk_to_centre", e + 31, keys, dict(type="toss", end_position_m=dict(x=0.0, y=round(-D - 0.1, 2)),
           note="walks out 7 m to the centre and stops, facing the way he walked (-Y); place his start 7 m short of the toss spot"))

def stand_at(y, **kw):
    return P(P(STAND, hips=H(0, y, 1.03), rball=V((-0.17, y - 0.06, 0)), lball=V((0.17, y - 0.06, 0)), **CLASP), **kw)

MIC_AT = V((0.02, -0.92, 1.36)); PRES_FACE = V((-0.30, -1.30, 1.68))
@clip("captain_speak_into_mic")
def speak():
    s0 = stand_at(0.0, look=PRES_FACE)
    step1 = P(s0, hips=H(0, -0.20, 1.02), rball=V((-0.17, -0.40, 0)), look=PRES_FACE)          # round 7: eyes on the presenter, not the mic
    y = -0.45
    at = P(stand_at(y), lean=2, look=PRES_FACE, lt=V((0.22, y - 0.02, 1.02)), lp=V((1, 0.4, -0.3)))           # left hand on the hip
    beat = lambda dz, dx: P(at, rt=V((-0.20 + dx, y - 0.30, 1.04 + dz)), rp=V((-1, 0.3, -0.5)), rh=V((0.2, -1, 0.3)), rhw=0.5,
                            rpal=V((0.4, 0, 1)), rpalw=0.7, **fing("r", dict(T=0.1, I=0.05, M=0.05, R=0.1, P=0.15)))
    k = [(0, s0), (8, s0), (16, step1), (26, at)]
    t = 34
    for i in range(10):                                   # talking: small open-palm beats and nods, a glance to the camera
        k.append((t, P(beat(0.05 if i % 2 == 0 else -0.02, 0.03*math.sin(i)), look=PRES_FACE + V((0, 0, -0.08 if i % 3 == 0 else 0)) if i != 6 else V((3, -6, 1.68)))))
        t += 11
    k += [(t + 8, at), (t + 22, step1), (t + 34, s0), (t + 46, s0)]
    export("captain_speak_into_mic", int(t + 47), k, dict(type="toss", speak_frames=[34, int(t)], microphone_at_m=dict(x=MIC_AT.x, y=MIC_AT.y, up=MIC_AT.z),
           layout="presenter_hold_mic_out_loop at (-0.30, -1.30) facing +Y puts the microphone where this clip leans to it",
           note="the toss winner steps 0.45 m forward to the presenter's microphone, talks with small open-palm beats and nods (glances at the camera once), steps back"))

@clip("captain_handshake")
def handshake():
    s0 = stand_at(0.0, look=V((0, -1.0, 1.68)))
    near = P(stand_at(-0.22), rball=V((-0.17, -0.46, 0)), lean=6, look=V((0, -1.0, 1.68)))
    shake = lambda dz: P(near, rt=V((-0.02, -0.52, 1.00 + dz)), rp=V((-1, 0.4, -0.6)), rh=V((0.1, -1, 0)), rhw=0.6, rpal=V((1, 0, 0)), rpalw=0.8,
                         **fing("r", HOLDF), lt=V((0.21, -0.10, 0.92)))
    k = [(0, s0), (8, s0), (20, near), (30, shake(0.0)), (36, shake(0.05)), (42, shake(-0.03)), (48, shake(0.04)), (54, shake(0.0)),
         (64, near), (78, s0), (99, s0)]
    export("captain_handshake", 100, k, dict(type="toss", grip_frames=[31, 55], hand_meet_point_m=dict(x=0.0, y=-0.52, up=1.0),
           layout="the other captain plays the same clip at (0, -1.0) facing +Y: both right hands meet at the midpoint",
           note="steps in, shakes hands with three pumps, steps back"))

@clip("captain_toss_call_watch")
def call_watch():
    st = stand_at(0.0, look=V((-0.5, -1.6, 1.68)))
    call = P(st, rt=V((-0.16, -0.26, 1.32)), rp=V((-1, 0.3, -0.6)), rh=V((0, -0.2, 1)), rhw=0.8, **fing("r", mm.POINT))
    peer = P(st, lean=6, hips=H(0, 0.02, 1.02))
    k = [(0, st), (24, P(st, look=V((-1.0, -0.4, 1.40)))), (34, st), (40, call), (52, call), (62, st), (78, peer), (88, peer), (100, st), (129, st)]
    export("captain_toss_call_watch", 130, k, dict(type="toss", call_frame=41, **COIN_META,
           note="the calling captain (1.0 m to the flipper's left): calls with a raised finger while the coin is up, follows it down, leans in to see it"),
           overlay=look_coin(-1.0))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
    ceremony_common.merge_pack()
