"""
Round-4 catches, part 2 (same conventions and sidecars as round4_catches.py).

  skier_catch_reverse_cup   two adjusting steps under a high ball, hands above the eyes (fingers up, thumbs together), gives
  boundary_relay_toss       leaps and catches near the rope, tosses it up before crossing, steps over and back, re-catches
                            (the library's catch_11 keys: toss + re-catch physics in the sidecar)
  boundary_relay_flick      leaps and catches near the rope, flicks it to a team-mate in front-left before his momentum
                            carries him over the rope
  dropped_catch             cups a chest-high ball, it pops out, a grab at it, hands on the head

    motion.ps1 gen drivers\\round4_catches2.py [names...]
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from round4_catches import export, chest
from fielding import V, P, H, HIP0, fing, RELAXF, run_keys
import fielding
import field_clips as FC
from field_clips import READY, STAND, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
G = 9.81

@clip("skier_catch_reverse_cup")
def skier():
    CF = 58; C = V((0.30, -0.78, 1.96))
    ball = lambda f: C + V((0, -0.02*max(0, CF - f), 0.32*max(0, CF - f)))
    s1 = P(READY, hips=H(0.15, -0.20, 1.0), rball=V((-0.05, -0.30, 0)), rt=V((-0.05, -0.45, 0.98)), lt=V((0.35, -0.45, 0.98)), look=ball(14))
    s2 = P(STAND, hips=H(0.30, -0.42, 1.0), rball=V((0.13, -0.48, 0)), lball=V((0.47, -0.48, 0)), rt=V((0.12, -0.66, 1.12)), lt=V((0.48, -0.66, 1.12)), look=ball(26))
    ready_up = P(s2, lean=-6, **hands(C + V((0, 0.10, -0.25)), "reverse"), **OPEN_R, **OPEN_L, look=ball(44))
    take = P(s2, lean=-8, hips=H(0.30, -0.42, 1.01), **hands(C, "reverse"), **OPEN_R, **OPEN_L, look=C)
    give = P(s2, lean=4, hips=H(0.30, -0.40, 0.94), **hands(C + V((0, 0.30, -0.62)), "reverse"), **HOLD_R, **HOLD_L, look=V((0.3, -3, 1.2)))
    k = [(0, READY), (6, READY), (14, s1), (26, s2), (44, ready_up), (CF, take), (66, give), (84, place(WITH_BALL, 0.30, -0.45)), (109, place(WITH_BALL, 0.30, -0.45))]
    export("skier_catch_reverse_cup", 110, k, dict(type="catch", event="catch", style="skier, reverse cup above the eyes",
           ball_falls_from_m=dict(forward=round(-ball(20).y, 2), left=round(ball(20).x, 2), up=round(ball(20).z, 2), frame=21),
           note="two small steps to get under a skier, eyes on the ball, hands up above the eyes (fingers up, thumbs together), takes it and gives with it down to the chest"),
           catch=CF, overlay=lambda f, p: p.__setitem__("look", ball(f)) if 8 <= f <= CF else None, reach=lambda f: abs(f - CF) <= 10)

@clip("boundary_relay_toss")
def relay_toss():
    n, keys, meta = FC.CLIPS["catch_11_boundary_relay_catch"][:3]
    m = {k: v for k, v in meta.items() if k not in ("event_frame",)}
    export("boundary_relay_toss", n, keys, dict(m, style="boundary relay: toss, step over and back, re-catch"), catch=meta["event_frame"] - 1,
           reach=lambda f: True)

@clip("boundary_relay_flick")
def relay_flick():
    keys = [(t, k) for t, k in FC.CLIPS["catch_11_boundary_relay_catch"][1] if t <= 38]   # the leap and catch near the rope
    CF = 30; RF = 46
    mate = V((5.0, -3.0, 1.25)); rel = V((0.25, 0.42, 1.12)); tf = 0.70
    vel = V(((mate.x - rel.x)/tf, (mate.y - rel.y)/tf, (mate.z - rel.z + 0.5*G*tf*tf)/tf))
    wind = P(STAND, hips=H(0.02, 0.66, 0.92), lean=8, twist=-25, rball=V((-0.18, 0.66, 0)), lball=V((0.16, 0.70, 0)),
             rt=V((-0.30, 0.66, 1.00)), rp=V((-0.6, 0.5, -0.8)), lt=V((0.25, 0.45, 1.05)), **HOLD_R, look=mate)
    flick = P(STAND, hips=H(0.06, 0.72, 0.94), lean=6, twist=20, rball=V((-0.18, 0.66, 0)), lball=V((0.16, 0.70, 0)),
              rt=rel, rp=V((-0.3, 0.6, -0.8)), rh=vel.normalized(), rhw=0.8, lt=V((0.30, 0.55, 1.0)), **fing("r", RELAXF), look=mate)
    over1 = P(STAND, hips=H(0.0, 1.00, 0.98), lean=-4, rball=V((-0.16, 1.22, 0)), lball=V((0.14, 0.78, 0)), rt=V((-0.3, 0.95, 1.1)), lt=V((0.3, 0.95, 1.1)), look=mate)
    out = place(STAND, 0.0, 1.32, 0)
    keys += [(40, wind), (RF, flick), (52, P(flick, rt=rel + V((0.30, -0.25, -0.25)))), (60, over1), (70, out), (89, out)]
    export("boundary_relay_flick", 90, keys, dict(type="catch", event="catch", style="boundary relay: flick to a team-mate",
           flick_release_frame=RF + 1, flick_release_point_m=dict(forward=round(-rel.y, 2), left=round(rel.x, 2), up=round(rel.z, 2)),
           flick_velocity_m_s=dict(forward=round(-vel.y, 2), left=round(vel.x, 2), up=round(vel.z, 2)), gravity=G,
           teammate_catch_point_m=dict(forward=-mate.y, left=mate.x, up=mate.z), rope_m_behind_start=0.9,
           note="leaps and takes it above his head near the rope, lands, flicks it underarm to a team-mate 5 m to his left and 3 m in front, then his momentum carries him over the rope"),
           catch=CF, reach=lambda f: f <= 56)

@clip("dropped_catch")
def dropped():
    CF = 30; C = V((0.0, -0.40, 1.02))
    readyc = P(READY, **hands(C + V((0, 0.05, 0.05)), "cup"), **OPEN_R, **OPEN_L, look=C + V((0, -6, 0.6)))
    take = P(READY, hips=H(0, 0.02, 0.97), **hands(C, "cup"), **OPEN_R, **OPEN_L, look=C)
    pop = P(READY, hips=H(0, 0.02, 0.96), rt=V((-0.14, -0.38, 1.20)), lt=V((0.14, -0.38, 1.18)), **OPEN_R, **OPEN_L, look=C + V((0, -0.1, 0.25)))
    grab = P(READY, hips=H(0, -0.05, 0.80), lean=30, rt=V((-0.06, -0.55, 0.55)), lt=V((0.08, -0.52, 0.52)), **OPEN_R, **OPEN_L, look=V((0, -0.6, 0.1)))
    head = dict(rt=V((-0.09, 0.02, 1.80)), lt=V((0.09, 0.02, 1.80)), rp=V((-1, 0.3, 0.2)), lp=V((1, 0.3, 0.2)), **fing("r", RELAXF), **fing("l", RELAXF))
    despair = P(STAND, hips=H(0, -0.05, 1.02), lean=-6, **head, look=V((0, -6, 3.5)))
    slump = P(STAND, hips=H(0.05, -0.05, 1.02), lean=10, pyaw=25, **head, look=V((2, -4, 0.0)))
    k = [(0, READY), (10, READY), (22, readyc), (CF, take), (33, pop), (40, grab), (50, grab), (60, P(STAND, hips=H(0, -0.05, 1.01))), (68, despair),
         (90, slump), (104, slump), (118, place(STAND, 0.05, -0.05, 15)), (129, place(STAND, 0.05, -0.05, 15))]
    export("dropped_catch", 130, k, dict(type="catch", event="dropped catch", style="drop", catch_attempt_frame=CF + 1, ball_pops_out_frame=34,
           ball_lands_frame=46, note="gets in line, cups a chest-high ball, it pops out of his hands, a lunging grab at it as it falls, then both hands on his head, head back, a turn away"),
           catch=CF, reach=lambda f: abs(f - CF) <= 8 or 36 <= f <= 56)

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
