"""
Round-4 fielding brief, part 2 (same conventions as round4_fieldbrief.py).

  catch_15_skier_judged_reverse_cup / _cup   under a skier, looking up, adjusting steps; takes it at ~2.0 m (fingers up) or
                                              ~1.0 m (cupped); 150 f, catch f120, ends holding it up
  seq_38a_relay_flick_fielder1 / seq_38b_relay_catch_fielder2   boundary relay pair in ONE shared field frame (like seq_30):
          38a leaps at the rope, takes it at 2.4 m (f31), flicks it in (f45) before stepping over the rope; 110 f
          38b runs in from 6 m, takes the flick at chest height (f75), raises the ball; play both at once at their start positions
  catch_16_drop_pop_out     chest catch, it pops up off the hands (f31), a juggle (f37) and a miss, it lands by his feet (f45),
                             hands on head; 100 f
  slip_catch_chest / slip_dive_left / slip_dive_right   from the slip crouch, ball arriving fast; 90 f, catch f12

    motion.ps1 gen drivers\\round4_fieldbrief2.py [names...]
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from round4_catches import export, OUT
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, RELAXF, run_keys
import field_clips as FC
from field_clips import READY, STAND, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L

CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco
G = 9.81
def at(x, y, base=STAND, z=1.02, **kw):
    """a standing pose moved to (x, y) with the hands carried along"""
    return P(P(base, hips=H(x, y, z), rball=V((x - 0.17, y - 0.06, 0)), lball=V((x + 0.17, y - 0.06, 0)),
               rt=V((x - 0.21, y + 0.02, 0.90)), lt=V((x + 0.21, y + 0.02, 0.90))), **kw)
ALOFT = lambda x, y: dict(rt=V((x - 0.16, y - 0.05, 2.02)), rp=V((-1, 0.2, 0)), **HOLD_R, lt=V((x + 0.30, y - 0.10, 1.30)), lp=V((1, 0.3, -0.3)), **fing("l", mm.FIST))

def skier(name, high):
    CF = 119; C = V((0.20, -0.62, 2.00 if high else 1.02))
    ball = lambda f: C + V((0, -0.02*max(0, CF - f), 0.24*max(0, CF - f)))
    style = ("reverse", "fingers up, thumbs together, above the eyes") if high else ("cup", "cupped, fingers down, palms up at waist height")
    look = lambda f: dict(look=ball(f))
    up_h = hands(C + V((0, 0.06, -0.20 if high else 0.12)), style[0]) if high else hands(C + V((0, 0.05, 0.10)), "cup", fd=V((0, -0.3, -1)), pd=V((0, -0.3, 1)))
    take_h = hands(C, "reverse") if high else hands(C, "cup", fd=V((0, -0.3, -1)), pd=V((0, -0.3, 1)))
    k = [(0, P(READY, **look(0))), (10, P(READY, **look(10))), (26, at(0.25, -0.40, **look(26))), (44, at(0.25, -0.40, **look(44))),
         (58, at(0.12, -0.28, **look(58))), (78, at(0.12, -0.28, **look(78))), (90, at(0.20, -0.36, **look(90))),
         (104, at(0.20, -0.36, lean=-6 if high else 6, **up_h, **OPEN_R, **OPEN_L, **look(104))),
         (CF, at(0.20, -0.36, z=1.01 if high else 0.98, lean=-8 if high else 10, **take_h, **OPEN_R, **OPEN_L, **look(CF))),
         (126, at(0.20, -0.34, z=0.96, lean=4, **hands(V((0.20, -0.50, 1.15)), "reverse"), **HOLD_R, **HOLD_L, look=V((0.2, -3, 1.2)))),
         (136, at(0.20, -0.34, **ALOFT(0.20, -0.34), look=V((0.2, -20, 3.0)))), (149, at(0.20, -0.34, **ALOFT(0.20, -0.34), look=V((-1.0, -20, 3.0))))]
    export(name, 150, k, dict(type="catch", event="catch", style="skier, " + style[1],
           ball_falls_from_m=dict(forward=round(-ball(70).y, 2), left=round(ball(70).x, 2), up=round(ball(70).z, 2), frame=71),
           note="waits under a skier with his eyes on it, three small adjusting steps, hands into position, takes it " + style[1] + ", gives with it and holds it up"),
           catch=CF, reach=lambda f: f >= 96)
clip("catch_15_skier_judged_reverse_cup")(lambda: skier("catch_15_skier_judged_reverse_cup", True))
clip("catch_15_skier_judged_cup")(lambda: skier("catch_15_skier_judged_cup", False))

MATE = V((2.0, -5.05, 1.30)); FLICK_F, MATE_F = 44, 74
@clip("seq_38a_relay_flick_fielder1")
def relay_a():
    keys = [(t, k) for t, k in FC.CLIPS["catch_11_boundary_relay_catch"][1] if t <= 38]
    rel = V((0.30, 0.40, 1.10))
    wind = P(STAND, hips=H(0.02, 0.66, 0.92), lean=8, twist=-25, rball=V((-0.18, 0.66, 0)), lball=V((0.16, 0.72, 0)),
             rt=V((-0.30, 0.68, 0.98)), rp=V((-0.6, 0.5, -0.8)), lt=V((0.25, 0.45, 1.05)), **HOLD_R, look=MATE)
    flick = P(STAND, hips=H(0.06, 0.74, 0.94), lean=6, twist=20, rball=V((-0.18, 0.70, 0)), lball=V((0.16, 0.72, 0)),
              rt=rel, rp=V((-0.3, 0.6, -0.8)), rh=(MATE - rel).normalized(), rhw=0.8, lt=V((0.30, 0.55, 1.0)), **fing("r", RELAXF), look=MATE)
    over1 = P(STAND, hips=H(0.0, 1.02, 0.98), lean=-4, rball=V((-0.16, 1.24, 0)), lball=V((0.14, 0.80, 0)), rt=V((-0.3, 0.95, 1.1)), lt=V((0.3, 0.95, 1.1)), look=MATE)
    out = place(STAND, 0.0, 1.32, 0)
    keys += [(40, wind), (FLICK_F, flick), (50, P(flick, rt=rel + V((0.30, -0.25, -0.25)))), (58, over1), (68, out), (109, P(out, look=MATE + V((0, 0, 0.6))))]
    export("seq_38a_relay_flick_fielder1", 110, keys, dict(role="fielder 1", type="catch", event="catch", rope_m_behind_start=0.9,
           note="leaps at the rope and takes it at 2.4 m, lands inside, flicks it in to fielder 2 (seq_38b) before stepping over the rope"),
           catch=30, reach=lambda f: f <= 56, probes={"flick_hand": (FLICK_F, "RightHandMiddle1")})
    # flick velocity from the MEASURED hand at the release frame to fielder 2's chest at his catch frame
    side = json.load(open(os.path.join(OUT, "seq_38a_relay_flick_fielder1.json")))
    tf = (MATE_F - FLICK_F)/30.0
    hand = V(side.pop("flick_hand"))
    vel = V(((MATE.x - hand.x)/tf, (MATE.y - hand.y)/tf, (MATE.z - hand.z + 0.5*G*tf*tf)/tf))
    side.update(flick_release_frame=FLICK_F + 1, flick_release_point_m=dict(x=round(hand.x, 2), y=round(hand.y, 2), z=round(hand.z, 2)),
                flick_velocity_m_s=dict(x=round(vel.x, 2), y=round(vel.y, 2), z=round(vel.z, 2)), gravity=G, flight_s=tf,
                teammate_catch_point_m=dict(x=MATE.x, y=MATE.y, z=MATE.z), teammate_catch_frame=MATE_F + 1,
                shared_frame="x = fielder 1's left, y negative = into the field (toward the batter); fielder 1 starts at the origin facing -Y")
    json.dump(side, open(os.path.join(OUT, "seq_38a_relay_flick_fielder1.json"), "w"), indent=1)
    print("  flick", side["flick_velocity_m_s"], "from", side["flick_release_point_m"], flush=True)

@clip("seq_38b_relay_catch_fielder2")
def relay_b():
    x0, y0 = 2.0, -11.0                                       # 6 m further in than the catch point, facing the rope (+Y)
    fielding.run_keys.last = None
    k = [(0, place(READY, x0, y0, 180)), (20, place(READY, x0, y0, 180))]
    k += run_keys(READY, f0=20, f1=66, p0=(x0, y0), p1=(x0, -5.65), heading=180, period=17, contact=0.36, lean=10)[0][1:]
    set_ = P(place(READY, x0, -5.45, 180), **hands(MATE + V((0, -0.08, -0.15)), "custom", fd=V((0, 0.15, 1)), pd=V((0, 1, 0.1))), **OPEN_R, **OPEN_L, look=MATE + V((0, 3, 0.5)))
    take = P(place(READY, x0, -5.45, 180), hips=H(x0, -5.45, 0.97), **hands(MATE, "custom", fd=V((0, 0.15, 1)), pd=V((0, 1, 0.1))), **OPEN_R, **OPEN_L, look=MATE)
    chest_ = P(place(STAND, x0, -5.45, 180), rt=V((x0 + 0.07, -5.20, 1.08)), lt=V((x0 - 0.07, -5.20, 1.06)), **HOLD_R, **HOLD_L, look=V((x0, 0, 1.2)))
    aloft = P(place(STAND, x0, -5.45, 180), rt=V((x0 + 0.16, -5.40, 2.02)), rp=V((1, -0.2, 0)), **HOLD_R, lt=V((x0 - 0.30, -5.35, 1.30)), lp=V((-1, -0.3, -0.3)),
              **fing("l", mm.FIST), look=V((x0, 10, 3.0)))
    k += [(70, set_), (MATE_F, take), (80, chest_), (92, aloft), (109, aloft)]
    export("seq_38b_relay_catch_fielder2", 110, k, dict(role="fielder 2", type="catch", event="catch", start_position_m=dict(x=x0, y=y0), start_heading="+Y (toward the rope)",
           sync="play at the same time as seq_38a in the same field frame; fielder 2 starts at start_position_m facing the rope",
           note="runs in from 6 m, takes fielder 1's flick at chest height, raises the ball"), catch=MATE_F, reach=lambda f: f >= 66)

@clip("catch_16_drop_pop_out")
def drop_pop():
    HF, JF, LF = 30, 36, 44; C = V((0.0, -0.40, 1.10))
    t1 = (JF - HF)/30.0; J = C + V((0, -0.5*t1, 2.5*t1 - 0.5*G*t1*t1))         # pops up 2.5 m/s, 0.5 m/s forward
    LAND = V((0.05, -0.62, 0.0)); t2 = (LF - JF)/30.0
    v2 = V(((LAND.x - J.x)/t2, (LAND.y - J.y)/t2, (LAND.z - J.z + 0.5*G*t2*t2)/t2))
    readyc = P(READY, **hands(C + V((0, 0.05, 0.05)), "reverse"), **OPEN_R, **OPEN_L, look=C + V((0, -6, 0.6)))
    hit = P(READY, hips=H(0, 0.02, 0.98), **hands(C, "reverse"), **OPEN_R, **OPEN_L, look=C)
    chase = P(READY, hips=H(0, 0.0, 1.0), lean=-4, **hands(J + V((0, 0.02, -0.05)), "reverse"), **OPEN_R, **OPEN_L, look=J)
    miss = P(READY, hips=H(0, -0.05, 0.80), lean=30, rt=V((-0.08, -0.58, 0.55)), lt=V((0.08, -0.55, 0.52)), **OPEN_R, **OPEN_L, look=LAND)
    head = dict(rt=V((-0.09, 0.02, 1.80)), lt=V((0.09, 0.02, 1.80)), rp=V((-1, 0.3, 0.2)), lp=V((1, 0.3, 0.2)), **fing("r", RELAXF), **fing("l", RELAXF))
    k = [(0, READY), (12, READY), (24, readyc), (HF, hit), (33, P(chase, **hands(C + V((0, -0.04, 0.12)), "reverse"))), (JF, chase), (LF - 2, miss),
         (50, miss), (58, P(STAND, hips=H(0, -0.05, 1.01))), (66, P(STAND, lean=-6, **head, look=V((0, -6, 3.5)))),
         (86, P(STAND, lean=10, pyaw=20, **head, look=V((1.5, -4, 0.0)))), (99, place(STAND, 0.0, -0.05, 15))]
    export("catch_16_drop_pop_out", 100, k, dict(type="catch", event="dropped catch", style="drop: pops out, juggle, miss",
           ball_hits_hands_frame=HF + 1, ball_hits_hands_m=dict(forward=-C.y, left=C.x, up=C.z), pop_velocity_m_s=dict(forward=0.5, left=0.0, up=2.5),
           juggle_frame=JF + 1, juggle_point_m=dict(forward=round(-J.y, 3), left=0.0, up=round(J.z, 3)),
           after_juggle_velocity_m_s=dict(forward=round(-v2.y, 2), left=round(v2.x, 2), up=round(v2.z, 2)), gravity=G,
           ball_lands_frame=LF + 1, ball_lands_m=dict(forward=-LAND.y, left=LAND.x),
           note="takes a chest-high ball, it pops up off his hands, he juggles at it, knocks it down and misses, it lands by his feet; hands on his head"),
           catch=HF, reach=lambda f: 22 <= f <= 52)

SLIP = P(READY, hips=H(0, 0.05, 0.78), lean=34, ptilt=12, rball=V((-0.25, -0.02, 0)), lball=V((0.25, -0.02, 0)), rheel=15, lheel=15, rtoe=V((-0.3, -1, 0)),
         ltoe=V((0.3, -1, 0)), rkp=V((-0.5, -1, 0.2)), lkp=V((0.5, -1, 0.2)), **hands(V((0, -0.46, 0.62)), "cup", fd=V((0, -0.3, -1)), pd=V((0, -1, 0.3))),
         **OPEN_R, **OPEN_L, look=V((0, -20, 0.6)))
@clip("slip_catch_chest")
def slip_chest():
    CF = 11; C = V((0.0, -0.40, 1.00))
    k = [(0, SLIP), (4, SLIP), (CF, P(SLIP, hips=H(0, 0.04, 0.88), lean=16, ptilt=6, **hands(C, "reverse"), look=C)),
         (17, P(SLIP, hips=H(0, 0.04, 0.90), lean=10, ptilt=4, **hands(V((0, -0.28, 1.08)), "reverse"), **HOLD_R, **HOLD_L, look=V((0, -5, 1.2)))),
         (30, WITH_BALL), (44, P(STAND, **ALOFT(0, 0), look=V((0, -20, 3.0)))), (89, P(STAND, **ALOFT(0, 0), look=V((-2, -20, 3.0))))]
    export("slip_catch_chest", 90, k, dict(type="catch", event="catch", style="slip, chest high",
           note="from the slip crouch, the ball arrives fast at chest height: hands up with fingers up, takes it and pulls it in, stands and holds it up"),
           catch=CF, reach=lambda f: f <= 20)

def slip_dive(name, sg):
    CF = 11; C = V((sg*1.80, -0.20, 0.40))
    lead, trail = ("l", "r") if sg > 0 else ("r", "l")
    legs = lambda x0, z: {lead + "ball": V((x0, -0.05, z)), trail + "ball": V((x0 - sg*0.10, 0.10, z - 0.06)), lead + "heel": 60, trail + "heel": 60,
                          lead + "kp": V((0, -1, 0)), trail + "kp": V((0, -1, 0))}
    flat = lambda x, z, **kw: P(SLIP, hips=H(x, -0.12, z), pside=sg*(82 if z > 0.3 else 88), lean=4, footup=0.2, **kw)
    k = [(0, SLIP), (3, SLIP), (6, P(SLIP, hips=H(sg*0.20, 0.03, 0.72), side=sg*14, **{lead + "ball": V((sg*0.45, -0.04, 0))})),
         (9, P(SLIP, hips=H(sg*0.80, -0.05, 0.62), pside=sg*50, footup=0.5, **{lead + "ball": V((sg*0.45, -0.04, 0)), lead + "heel": 55,
               trail + "ball": V((sg*0.05, 0.05, 0.25)), trail + "heel": 50},
               **hands(C + V((-sg*0.35, 0, 0.1)), "custom", "both", fd=V((sg, -0.1, 0.1)), pd=V((0, -1, 0.2))), look=C)),
         (CF, flat(sg*1.18, 0.40, **legs(sg*0.42, 0.30), **hands(C, "custom", "both", fd=V((sg, -0.1, 0.05)), pd=V((0, -1, 0.2)), spread=0.06), look=C)),
         (17, flat(sg*1.32, 0.17, **legs(sg*0.50, 0.08), **hands(V((sg*1.75, -0.20, 0.22)), "custom", "both", fd=V((sg, -0.1, 0.3)), pd=V((0, -1, 0.3))),
                   **HOLD_R, **HOLD_L, look=V((sg*1.7, -0.2, 0.2)))),
         (26, flat(sg*1.38, 0.17, **legs(sg*0.56, 0.08), **hands(V((sg*1.80, -0.18, 0.24)), "custom", "both", fd=V((sg, -0.1, 0.4)), pd=V((0, -0.6, 0.8))),
                   **HOLD_R, **HOLD_L, look=V((0, -6, 1.0)))),
         (42, P(READY, hips=H(sg*1.35, 0.05, 0.55), pside=sg*12, lean=20, **{lead + "ball": V((sg*1.62, -0.25, 0)), trail + "ball": V((sg*1.15, 0.32, 0)),
                trail + "heel": 75, trail + "kp": V((0, -0.3, -1))}, rt=V((sg*1.35 - 0.07, -0.32, 0.80)), lt=V((sg*1.35 + 0.07, -0.33, 0.79)), **HOLD_R, **HOLD_L)),
         (58, place(WITH_BALL, sg*1.40, 0.0)), (70, P(place(STAND, sg*1.40, 0.0), **ALOFT(sg*1.40, 0.0), look=V((sg*1.4, -20, 3.0)))),
         (89, P(place(STAND, sg*1.40, 0.0), **ALOFT(sg*1.40, 0.0), look=V((sg*1.4, -20, 3.0))))]
    export(name, 90, k, dict(type="catch", event="catch", style="slip, diving",
           note=f"from the slip crouch, a flat dive to his {'left' if sg > 0 else 'right'}, two hands at full stretch ~1.8 m out and 0.4 m up, lands, gets up and holds it up"),
           catch=CF, reach=lambda f: f <= 30)
clip("slip_dive_left")(lambda: slip_dive("slip_dive_left", 1))
clip("slip_dive_right")(lambda: slip_dive("slip_dive_right", -1))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
