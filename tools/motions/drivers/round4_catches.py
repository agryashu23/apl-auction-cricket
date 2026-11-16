"""
Round-4 catches, part 1 (fielding conventions: start at the origin facing -Y = toward the batter, x = his left, travel in
the hips; frames 1-based in the metadata). Every sidecar gives catch_frame, hand_point_m (where the two hands meet the ball,
measured on the solved skeleton: forward / left / up from the start point) and travel_m (where he ends up).

  running_catch_forward               sprints in, gets low, takes it cupped (palms up, fingers down) on the run, slows
  running_catch_over_shoulder_left    turns and sprints back, watches it over the LEFT shoulder, takes it in front of the face
  running_catch_over_shoulder_right   the mirror (over the right shoulder)
  diving_catch_left / _right          full-stretch dive, two-hand take, lands on the side, rolls onto the back showing the ball, up

    motion.ps1 gen drivers\\round4_catches.py [names...]
"""
import json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); LIB = os.path.join(os.path.dirname(HERE), "lib")
if LIB not in sys.path: sys.path.insert(0, LIB)
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, CATCHF, HOLDF, run_keys, make_field, pulse
import field_clips as FC
from field_clips import READY, STAND, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L
import bowling as BW

OUT = os.path.join(mm.APL_OUT, "round4"); os.makedirs(OUT, exist_ok=True)
CLIPS = {}
def clip(name):
    def deco(fn): CLIPS[name] = fn; return fn
    return deco

def export(name, n, keys, meta, catch=None, overlay=None, mirror=False, reach=None, loop=False, hand="both", probes=None):
    """make_field + sidecar; catch = 0-based catch frame (hand point measured there)"""
    fielding.OUTF = OUT
    ks = [(t, BW.mirror(k)) for t, k in keys] if mirror else keys
    ov = overlay
    if mirror and overlay:
        def ov(f, p, base=overlay):
            q = BW.mirror(p); base(f, q); r = BW.mirror(q); p.clear(); p.update(r)
    frames = make_field(name, n, ks, overlay=ov, loop=loop, lift=True, reach=reach)
    J = lambda f, b: V(frames[f][0][mm.PFX + b])
    low = min(min(J(f, b).z for b in ("RightToeBase", "LeftToeBase")) for f in range(n))
    hz = [J(f, "Hips").z for f in range(n)]
    m = dict(meta)
    if catch is not None:
        hm = {"r": "RightHandMiddle1", "l": "LeftHandMiddle1"}
        if mirror and hand in hm: hand = "l" if hand == "r" else "r"
        hp = (J(catch, "RightHandMiddle1") + J(catch, "LeftHandMiddle1"))*0.5 if hand == "both" else J(catch, hm[hand])
        if hand != "both": m["catching_hand"] = "right" if hand == "r" else "left"
        m["catch_frame"] = catch + 1
        m["hand_point_m"] = dict(forward=round(-hp.y, 2), left=round(hp.x, 2), up=round(hp.z, 2))
        if hand == "both": m["hands_apart_at_catch_m"] = round((J(catch, "RightHandMiddle1") - J(catch, "LeftHandMiddle1")).length, 2)
    for pk, (pf, pb) in (probes or {}).items():                  # measured bone positions (field frame, metres) for the sidecar
        q = J(pf, pb); m[pk] = [round(q.x, 3), round(q.y, 3), round(q.z, 3)]
    e = J(n - 1, "Hips"); s = J(0, "Hips")
    m["travel_m"] = dict(forward=round(-(e.y - s.y), 2), left=round(e.x - s.x, 2))
    errs = make_field.last_errs
    print(f"{name}: {n} f  min toe z {low:+.3f}  hips {min(hz):.2f}..{max(hz):.2f}  max hand err {max(errs):.3f}@f{errs.index(max(errs)) + 1}"
          + (f"  catch f{catch + 1} hand {m['hand_point_m']}" if catch is not None else "")
          + f"  travel {m['travel_m']}", flush=True)
    json.dump(dict(file=name + ".fbx", frames=n, loop=loop, **m), open(os.path.join(OUT, name + ".json"), "w"), indent=1)

def chest(p):                                     # both hands with the ball at the chest, relative to the hips
    hp = V(p["hips"]) + V((0, 0, HIP0)); F = fielding.fwd(p["pyaw"]); L = fielding.left(p["pyaw"])
    return dict(rt=hp + F*0.26 - L*0.07 + V((0, 0, 0.05)), lt=hp + F*0.27 + L*0.07 + V((0, 0, 0.03)), **HOLD_R, **HOLD_L)

# ---------------------------------------------------------------- running catch forward (cupped low, on the run)
@clip("running_catch_forward")
def run_fwd():
    CF = 42
    fielding.run_keys.last = None
    k = [(0, READY), (6, READY)]
    k += run_keys(READY, f0=6, f1=46, p0=(0, 0), p1=(0, -6.6), heading=0, period=16, contact=0.30, lean=14, ease="accel")[0][1:]
    k += run_keys(READY, f0=46, f1=70, p0=(0, -6.6), p1=(0, -9.0), heading=0, period=18, contact=0.40, lean=6, ease="decel")[0][1:]
    k += [(80, place(WITH_BALL, 0, -9.25)), (99, place(WITH_BALL, 0, -9.25))]
    def ov(f, p):
        w = pulse(f, CF, 10)
        hp = V(p["hips"]) + V((0, 0, HIP0))
        if w > 0 and f <= CF:
            ball = V((hp.x, hp.y - 0.50, 0.42))
            t = hands(ball, "cup", fd=V((0, -0.35, -1)), pd=V((0, -0.45, 0.9)))
            for k_, v_ in t.items(): p[k_] = V(p[k_]).lerp(v_, w) if hasattr(v_, "lerp") else p[k_]*(1 - w) + v_*w
            p.update(OPEN_R); p.update(OPEN_L); p["look"] = V(p["look"]).lerp(ball, w)
        if f > CF:
            g = min(1.0, (f - CF)/6.0); c = chest(p)
            for k_ in ("rt", "lt"): p[k_] = V(p[k_]).lerp(c[k_], g)
            p.update(HOLD_R); p.update(HOLD_L)
        if w > 0:
            p["hips"] = V(p["hips"]) + V((0, 0, -0.12*w)); p["ptilt"] += 16*w; p["gait"] = 0.0
    export("running_catch_forward", 100, k, dict(type="catch", event="catch", style="running in, cupped low",
           note="sprints in, bends at the knees and hips, takes it cupped low in front (palms up, fingers down) without stopping, brings it to the chest, slows to a stop"),
           catch=CF, overlay=ov, reach=lambda f: abs(f - CF) <= 8)

# ---------------------------------------------------------------- running back, over the shoulder
def over_shoulder(name, mirror):
    CF = 74
    lead = [(t, k) for t, k in FC.CLIPS["run_09_turn_and_sprint_back"][1] if t <= 46]
    fielding.run_keys.last = None
    k = list(lead)
    k += run_keys(READY, f0=46, f1=80, p0=(0, 5.5), p1=(0, 11.0), heading=180, period=16, contact=0.30, lean=12)[0][1:]
    k += run_keys(READY, f0=80, f1=100, p0=(0, 11.0), p1=(0, 12.8), heading=180, period=18, contact=0.40, lean=6, ease="decel")[0][1:]
    end = place(WITH_BALL, 0, 13.0, 180)
    k += [(108, end), (119, end)]
    def ball_at(f, hp):                                   # the ball drops in from behind his left shoulder
        bc = V((hp.x - 0.14, hp.y + 0.42, 1.38))
        d = max(0.0, CF - f)
        return bc + V((0.05*d, -0.30*d, 0.10*d))
    def ov(f, p):
        hp = V(p["hips"]) + V((0, 0, HIP0))
        if 52 <= f <= CF:
            p["look"] = V(p["look"]).lerp(ball_at(f, hp), min(1.0, (f - 52)/6.0))
        w = pulse(f, CF, 10)
        if w > 0 and f <= CF:
            b = ball_at(CF, hp) + V((0, 0.0, 0.0))
            t = hands(b, "custom", fd=V((0, 0.55, 0.35)), pd=V((0, 0.2, 1)))
            for k_, v_ in t.items(): p[k_] = V(p[k_]).lerp(v_, w) if hasattr(v_, "lerp") else p[k_]*(1 - w) + v_*w
            p.update(OPEN_R); p.update(OPEN_L)
        if f > CF:
            g = min(1.0, (f - CF)/6.0); c = chest(p)
            for k_ in ("rt", "lt"): p[k_] = V(p[k_]).lerp(c[k_], g)
            p.update(HOLD_R); p.update(HOLD_L); p["look"] = V(p["look"]).lerp(hp + fielding.fwd(p["pyaw"])*6, g)
        if w > 0: p["gait"] = 0.0
    side = "right" if mirror else "left"
    export(name, 120, k, dict(type="catch", event="catch", style=f"running back, over the {side} shoulder", end_heading_deg=180,
           note=f"turns and sprints back toward the rope, watches the ball over his {side} shoulder, takes it in front of the face with both hands, slows; ends facing away from the batter"),
           catch=CF, overlay=ov, mirror=mirror, reach=lambda f: abs(f - CF) <= 8)
clip("running_catch_over_shoulder_left")(lambda: over_shoulder("running_catch_over_shoulder_left", False))
clip("running_catch_over_shoulder_right")(lambda: over_shoulder("running_catch_over_shoulder_right", True))

# ---------------------------------------------------------------- diving catches with a landing roll
def dive_roll(name, sg):
    """sg = +1 dive to his left (+X), -1 to his right"""
    CF = 28
    C = V((sg*1.92, -0.24, 0.72))
    lead, trail = ("l", "r") if sg > 0 else ("r", "l")
    fdir = V((sg*0.95, -0.2, 0.15)); pdir = V((0, -1, 0.25))
    air = hands(C, "custom", "both", fd=fdir, pd=pdir, spread=0.06)
    k = [(0, READY), (6, READY)]
    k.append((13, P(READY, hips=H(sg*0.30, 0.02, 0.80), lean=18, side=sg*20, **{lead + "ball": V((sg*0.62, -0.06, 0)), lead + "toe": V((sg*0.6, -1, 0))},
                    look=C + V((0, -3, 0.3)), **OPEN_R, **OPEN_L)))
    k.append((19, P(READY, hips=H(sg*0.72, 0.0, 0.80), pside=sg*35, lean=10, side=sg*15, footup=0.6,
                    **{lead + "ball": V((sg*0.66, -0.06, 0)), lead + "heel": 45, trail + "ball": V((sg*0.25, 0.0, 0.28)), trail + "heel": 50},
                    **hands(C + V((-sg*0.6, 0, 0.1)), "custom", "both", fd=fdir, pd=pdir), look=C + V((0, -1.5, 0.1)), **OPEN_R, **OPEN_L)))
    flight = dict(pside=sg*82, lean=6, side=sg*6, footup=0.2, look=C, **OPEN_R, **OPEN_L)
    legs = lambda x0, z: {lead + "ball": V((x0 + sg*0.0, -0.05, z)), trail + "ball": V((x0 - sg*0.10, 0.10, z - 0.08)), lead + "heel": 60, trail + "heel": 60,
                          lead + "kp": V((0, -1, 0)), trail + "kp": V((0, -1, 0))}
    k.append((25, P(READY, hips=H(sg*1.12, -0.12, 0.56), **flight, **legs(sg*0.40, 0.48), **air)))
    k.append((CF, P(READY, hips=H(sg*1.24, -0.13, 0.50), **flight, **legs(sg*0.50, 0.42), **air)))
    held = hands(V((sg*1.85, -0.20, 0.30)), "custom", "both", fd=V((sg, -0.15, 0.1)), pd=pdir, spread=0.05)
    land = P(READY, hips=H(sg*1.32, -0.12, 0.18), pside=sg*88, lean=4, footup=0.2, **legs(sg*0.50, 0.10), **held, **HOLD_R, **HOLD_L,
             look=V((sg*1.9, -0.2, 0.3)))
    k.append((35, land))
    show = hands(V((sg*1.62, -0.14, 0.62)), "custom", "both", fd=V((sg*0.3, 0, 1)), pd=V((0, -1, 0.2)), spread=0.05)
    back = P(P(land, hips=H(sg*1.42, -0.10, 0.17), ptilt=-78, **legs(sg*0.62, 0.14), **show, look=V((sg*1.6, -2.0, 2.0))),
             **{lead + "kp": V((0, 0, 1)), trail + "kp": V((0, 0, 1))})
    k += [(44, back), (54, back)]
    k.append((62, P(land, hips=H(sg*1.40, -0.08, 0.22), **legs(sg*0.85, 0.08), **held)))
    k.append((72, P(READY, hips=H(sg*1.35, 0.05, 0.55), pside=sg*12, lean=20, footup=1.0,
                    **{lead + "ball": V((sg*1.62, -0.25, 0)), lead + "toe": V((sg*0.3, -1, 0)), trail + "ball": V((sg*1.15, 0.32, 0)), trail + "heel": 75,
                       trail + "kp": V((0, -0.3, -1))},
                    rt=V((sg*1.35 - 0.07, -0.32, 0.80)), lt=V((sg*1.35 + 0.07, -0.33, 0.79)), **HOLD_R, **HOLD_L, look=V((sg*1.35, -20, 1.0)))))
    k += [(90, place(WITH_BALL, sg*1.40, 0.0)), (119, place(WITH_BALL, sg*1.40, 0.0))]
    export(name, 120, k, dict(type="catch", event="catch", style="full-length dive, two hands",
           note=f"pushes off and dives full length to his {'left' if sg > 0 else 'right'}, takes it two-handed at full stretch, lands on his side, "
                "rolls onto his back holding the ball up, rolls back, kneels and stands with the ball at the chest"),
           catch=CF, reach=lambda f: 22 <= f <= 40)
clip("diving_catch_left")(lambda: dive_roll("diving_catch_left", 1))
clip("diving_catch_right")(lambda: dive_roll("diving_catch_right", -1))

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
