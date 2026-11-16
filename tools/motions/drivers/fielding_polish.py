"""
Round-2 fielding polish: rebuilds the ground-fielding clips that read badly on the review sheets, keeping their names
and the event frames the game reads (FieldPlay.cs: field_13 event 33 / up-and-throwing by 55, throw_18 release 31).

  field_13_two_hand_pickup   was: run -> crouch in 4 frames, reach-assist crushed the legs. now: two decelerating steps,
                             front foot plants beside the ball, deep flat-backed bend, both hands collect it, scoop to the chest
  throw_18_overarm_throw     was: the back leg swung through 18 cm from the front foot (knees crossed). now: it swings outside
  seq_20 / seq_21 / seq_22   was: one-hand scoop mid-sprint, then ~1 s standing still, then the throw from a standing start.
                             now: decelerate, low right-hand pick beside the planted front foot, rise straight into the crow hop

World: fielding frame (start at the origin facing -Y, x = his left). Output: build/motions/fielding, build/motions/fielding_sequences.

    motion.ps1 gen drivers\\fielding_polish.py [field_13 throw_18 seq_20 seq_21 seq_22]
"""
import json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(os.path.dirname(HERE), "lib")
if LIB not in sys.path: sys.path.insert(0, LIB)
import mixamo_motions as mm
import fielding
from fielding import V, P, H, HIP0, fing, RELAXF, HOLDF, run_keys, fwd, left, make_field
import field_clips as FC
from field_clips import READY, WITH_BALL, hands, place, OPEN_R, OPEN_L, HOLD_R, HOLD_L
import field_seq as FS
from field_seq import xform, seg, merge, heading_to, release_meta, KEEPER

OUT_F = os.path.join(mm.APL_OUT, "fielding"); OUT_S = os.path.join(mm.APL_OUT, "fielding_sequences")
for d in (OUT_F, OUT_S): os.makedirs(d, exist_ok=True)

def export(name, n, keys, meta, outdir, reach=None):
    fielding.OUTF = outdir
    frames = make_field(name, n, keys, lift=True, reach=reach)
    low = min(min(fr[0][mm.PFX + b][2] for b in ("RightToeBase", "LeftToeBase", "RightFoot", "LeftFoot")) for fr in frames)
    hz = [fr[0][mm.PFX + "Hips"][2] for fr in frames]
    print(f"  lowest hips {min(hz):.2f} @f{hz.index(min(hz)) + 1}", flush=True)
    if os.environ.get("FP_DEBUG"): print("  errs>5cm: " + " ".join(f"f{i + 1}:{e:.2f}" for i, e in enumerate(make_field.last_errs) if e > 0.05), flush=True)
    errs = make_field.last_errs
    print(f"{name}: {n} f  min foot z {low:+.3f}  max hand err {max(errs):.3f} @f{errs.index(max(errs)) + 1}", flush=True)
    json.dump(dict(file=name + ".fbx", frames=n, **meta), open(os.path.join(outdir, name + ".json"), "w"), indent=1)

# ---------------------------------------------------------------- throw_18 with a clean recovery step
K18 = [(t, k) for t, k in FC.CLIPS["throw_18_overarm_throw"][1] if t <= 38]
k38 = K18[-1][1]
K18 += [(42, P(k38, hips=H(-0.04, -1.24, 0.90), lean=30, rball=V((-0.32, -1.22, 0.20)), rtoe=V((-0.2, -1, 0)), rheel=35, rkp=V((-0.2, -1, 0.4)))),
        (47, P(place(READY, -0.02, -1.55), hips=H(-0.05, -1.50, 0.94), lean=18, rball=V((-0.26, -1.78, 0)), lball=V((0.16, -1.42, 0)),
               look=V((0, -25, 1.0)))),
        (74, place(READY, -0.04, -1.58))]
REL18 = 30

def throw_18():
    n, _, meta = FC.CLIPS["throw_18_overarm_throw"][:3]
    export("throw_18_overarm_throw", 75, K18, dict(meta, end_position_m=dict(forward=1.58, left=0.04)), OUT_F, reach=lambda f: True)

# ---------------------------------------------------------------- field_13: two-hand pick-up running in
C13 = V((-0.02, -2.62, 0.06))
def field_13():
    k = [(0, READY), (6, READY)]
    rk, _ = run_keys(READY, f0=6, f1=22, p0=(0, 0), p1=(0, -1.55), heading=0, period=16, contact=0.34, lean=12, first="r")
    k += rk[1:]
    look = dict(look=C13)
    gather = P(READY, hips=H(-0.02, -1.92, 0.84), lean=24, ptilt=6, rball=V((-0.14, -1.95, 0)), rheel=10,
               lball=V((0.10, -2.15, 0.14)), lheel=25, ltoe=V((0.2, -1, 0)), **hands(C13 + V((0, 0.38, 0.62)), "cup"), **OPEN_R, **OPEN_L, **look)
    plant = P(gather, hips=H(-0.02, -2.06, 0.64), lean=32, ptilt=40, rheel=35, lball=V((0.13, -2.40, 0)), lheel=0, ltoe=V((0.25, -1, 0)),
              **hands(C13 + V((0, 0.10, 0.24)), "cup", fd=V((0, -0.4, -1)), pd=V((0, -1, 0.3))))
    pick = P(plant, hips=H(-0.02, -2.08, 0.54), lean=36, ptilt=54, rheel=45, **hands(C13, "cup", fd=V((0, -0.4, -1)), pd=V((0, -1, 0.3))))
    scoop = P(pick, hips=H(-0.02, -2.13, 0.72), lean=30, ptilt=20, **hands(V((-0.04, -2.34, 0.66)), "reverse", fd=V((0, -0.3, 1)), pd=V((0, -0.9, 0.4))),
              **HOLD_R, **HOLD_L, look=V((0, -20, 0.9)))
    rise = P(place(WITH_BALL, 0, -2.2), hips=H(-0.02, -2.18, 0.92), lean=12, rball=V((-0.16, -2.05, 0)), lball=V((0.13, -2.40, 0)), ltoe=V((0.25, -1, 0)))
    k += [(26, gather), (30, plant), (32, pick), (36, P(pick, **HOLD_R, **HOLD_L)), (41, scoop), (48, rise), (55, place(WITH_BALL, 0, -2.2)),
          (89, place(WITH_BALL, 0, -2.2))]
    meta = dict(FC.CLIPS["field_13_two_hand_pickup"][2])
    meta["note"] = ("runs in, two decelerating steps, plants the left foot beside the ball, bends low with a flat back, takes it in both hands "
                    "(fingers down, palms to the ball) and scoops it to the chest; upright with the ball at the chest by frame 55")
    export("field_13_two_hand_pickup", 90, k, meta, OUT_F)

# ---------------------------------------------------------------- chase sequences: decelerate, low pick, rise into the crow hop
CP = V((-0.10, -0.66, 0.06))                      # the ball, local to the arrival point (facing -Y)
def pick_block():
    """local key poses after arrival (dt, pose): gather, plant the left foot beside the ball, pick, rise into the throw"""
    lk = dict(look=CP)
    gather = P(READY, hips=H(-0.02, -0.18, 0.86), lean=22, ptilt=6, rball=V((-0.14, -0.04, 0)), rheel=15,
               lball=V((0.12, -0.40, 0.12)), lheel=20, ltoe=V((0.2, -1, 0)), rt=V((-0.20, -0.42, 0.74)), lt=V((0.28, -0.30, 0.78)), **OPEN_R, **lk)
    hand = hands(CP, "custom", "r", fd=V((0, -0.4, -1)), pd=V((0, -1, 0.2)))
    plant = P(gather, hips=H(-0.03, -0.28, 0.64), lean=32, ptilt=40, rheel=40, lball=V((0.13, -0.54, 0)), lheel=0, ltoe=V((0.25, -1, 0)),
              **hands(CP + V((0, 0.08, 0.20)), "custom", "r", fd=V((0, -0.4, -1)), pd=V((0, -1, 0.2))), lt=V((0.34, -0.42, 0.62)), lp=V((1, 0, -0.5)))
    pick = P(plant, hips=H(-0.03, -0.30, 0.54), lean=36, ptilt=54, rheel=45, **hand)
    held = P(pick, **HOLD_R)
    rise = P(READY, hips=H(-0.03, -0.46, 0.90), lean=16, ptilt=4, rball=V((-0.12, -0.30, 0.06)), rheel=30, lball=V((0.13, -0.54, 0)), ltoe=V((0.25, -1, 0)),
             rt=V((-0.08, -0.72, 1.08)), lt=V((0.07, -0.73, 1.06)), rp=V((-0.6, 0.4, -1)), lp=V((0.6, 0.4, -1)), **HOLD_R, look=V((0, -20, 1.2)))
    return [(3, gather), (6, plant), (7, pick), (9, held), (13, rise)]

def chase(name, lead, run_args, h_run, h_turn, note):
    """lead: keys before the run; run_args: kwargs for run_keys (ends at frame A at the arrival point, heading h_run);
    h_turn: numeric heading for the throw (pick the value that turns the short, natural way)"""
    A = run_args["f1"]; best = None
    for first in ("r", "l"):
        fielding.run_keys.last = None
        rk, plants = run_keys(READY, first=first, **run_args)
        if any(p_[2] == "r" and p_[0] - 0.5 <= A <= p_[1] + 0.5 for p_ in plants): best = rk; break
    rk = best or rk
    ax, ay = run_args["p1"]
    blk = [(A + dt, xform(p, ax, ay, h_run)) for dt, p in pick_block()]
    rh = blk[-1][1]["hips"]
    # K18 frame 16 (side-on, hips at local (0, -0.55)) laid over the rise position, facing the target
    hT = h_turn; off = V((0, -0.55, 0)); Rz = mm.R('Z', hT)
    o = V((rh.x, rh.y, 0)) - Rz @ off
    t16 = A + 17
    th = seg(K18, 16, 47, t16, o.x, o.y, hT)
    rel = t16 + (REL18 - 16)
    end = t16 + (47 - 16)
    n = end + 14
    th.append((n - 1, xform(place(READY, -0.04, -1.58), o.x, o.y, hT)))
    keys = merge(lead, rk[1:], blk, th)
    pick_f = A + 7
    tgt_from = (o.x, o.y)
    meta = dict(pickup_frame=pick_f + 1, **release_meta(rel, tgt_from, KEEPER, 30), note=note)
    lead_end = lead[-1][0] if lead else 0
    export(name, n, keys, meta, OUT_S, reach=lambda f: f >= t16 - 2 or f < lead_end - 2)

def seq_20():
    lead = seg(FC.CLIPS["run_09_turn_and_sprint_back"][1], 0, 46, 0)
    chase("seq_20_chase_back_pickup_turn_throw", lead, dict(f0=46, f1=62, p0=(0, 5.5), p1=(0, 9.0), heading=180, period=17, contact=0.36, lean=12),
          180, 360, "sprints back after the ball, decelerates, plants and picks it up low with the right hand, turns to his left through 180 deg as he rises and crow-hop throws back to the keeper/bowler")

def seq_21(sg, name):
    hdg = 84*sg
    lead = [(0, READY), (4, READY), (10, P(READY, pyaw=hdg*0.6, hips=H(sg*0.15, 0.0, 0.95), **{("lball" if sg > 0 else "rball"): V((sg*0.40, -0.05, 0))}))]
    x1, y1 = sg*5.6, -0.70
    tgt = heading_to((x1, y1), KEEPER)
    chase(name, lead, dict(f0=10, f1=36, p0=(sg*0.3, -0.05), p1=(x1, y1), heading=hdg, period=16, contact=0.30, lean=14, ease="accel"), hdg, tgt,
          "runs to the %s, decelerates into a low right-hand pick-up beside the planted left foot, rises straight into the crow hop and throws" % ("left" if sg > 0 else "right"))

CLIPS = {"field_13": field_13, "throw_18": throw_18, "seq_20": seq_20,
         "seq_21": lambda: seq_21(1, "seq_21_chase_left_pickup_throw"), "seq_22": lambda: seq_21(-1, "seq_22_chase_right_pickup_throw")}
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
SRC_JSON = _APL_REPO + "/client-src/Assets/Resources/Data/cricket"
def merge_packs():
    """full copies of fielding.json / fielding_sequences.json with the rebuilt clips' entries swapped in"""
    for pack, outdir in (("fielding", OUT_F), ("fielding_sequences", OUT_S)):
        d = json.load(open(os.path.join(SRC_JSON, pack + ".json"), encoding="utf-8")); n = 0
        for i, c in enumerate(d["clips"]):
            side = os.path.join(outdir, c["file"][:-4] + ".json")
            if os.path.exists(side) and os.path.exists(side[:-5] + ".fbx") and c["file"][:-4] in REBUILT:
                d["clips"][i] = json.load(open(side)); n += 1
        json.dump(d, open(os.path.join(outdir, pack + ".json"), "w"), indent=1)
        print(f"{pack}.json: {n} entries replaced", flush=True)
REBUILT = {"field_13_two_hand_pickup", "throw_18_overarm_throw", "seq_20_chase_back_pickup_turn_throw",
           "seq_21_chase_left_pickup_throw", "seq_22_chase_right_pickup_throw"}

if __name__ == "__main__":
    for nm in (sys.argv[1:] or list(CLIPS)):
        CLIPS[nm]()
    merge_packs()
