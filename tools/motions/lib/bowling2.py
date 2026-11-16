"""Fast bowling, complete in ~4 s: mark -> accelerating run-up -> gather/bound -> back-foot contact -> front-foot contact
-> release -> follow-through. Bowling world: bowler runs toward -Y, popping crease y=0, own stumps y=+1.22, batter's stumps y=-18.9.
Right-arm over the wicket runs in at x = +0.30 (bowler's left of the stumps)."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import fielding
from fielding import *
import bowling as BW
from bowling import arm_dir, mirror, V3
from field_clips import STAND, place
OUT2 = _os.path.join(APL_OUT, "fast_bowling_actions"); os.makedirs(OUT2, exist_ok=True)
TARGET = V((0, -18.9, 0.9))
BALL = fing("r", BW.HOLD_BALL); FLICK = fing("r", BW.FLICK)

ACTIONS = {
  "side_on":  dict(yaw_bfc=-88, twist_bfc=0,   yaw_ffc=-72, stride=1.50, tilt=6,  phi_rel=206, lean_rel=34, side_rel=14, front_arm=V3(0.05,-0.55,0.85),
                   bfc_toe=-88, release_twist=14, hip_bfc=0.96, gather_up=0.20,
                   desc="Classical side-on: back foot lands parallel to the crease, hips and shoulders side-on, front arm reaching high at the batter, bowling arm close to vertical"),
  "front_on": dict(yaw_bfc=-22, twist_bfc=-6,  yaw_ffc=-14, stride=1.28, tilt=16, phi_rel=204, lean_rel=30, side_rel=10, front_arm=V3(0.25,-0.45,0.86),
                   bfc_toe=-15, release_twist=18, hip_bfc=0.97, gather_up=0.16,
                   desc="Front-on (chest-on): back foot points down the pitch, chest faces the batter at back-foot contact, shorter stride, arm a little wider of vertical"),
  "slinger":  dict(yaw_bfc=-55, twist_bfc=-28, yaw_ffc=-40, stride=1.42, tilt=55, phi_rel=212, lean_rel=36, side_rel=4,  front_arm=V3(0.65,-0.55,0.35),
                   bfc_toe=-50, release_twist=22, hip_bfc=0.95, gather_up=0.14,
                   desc="Slinger: mixed action with big shoulder counter-rotation, front arm swung out wide, low round-arm whip, release about 25 cm lower"),
}

def bowl2(kind):
    A = ACTIONS[kind]; xl = 0.30
    tilt = A["tilt"]
    # ---------------- positions (back from the crease) and timing
    y_ffc = -0.05; y_bfc = y_ffc + A["stride"]; bound = 2.2; y_to = y_bfc + bound
    f_go, f1, f2, f_to = 6, 22, 46, 70
    d1, v2, v3 = 1.6, 5.6, 6.6
    d2 = v2*(f2 - f1)/30; d3 = v3*(f_to - f2)/30
    y0 = y_to + d1 + d2 + d3
    f_bfc = f_to + 10; f_ffc = f_bfc + 5; f_rel = f_ffc + 3; n = f_rel + 32
    # ---------------- start at the mark
    mark = P(BASE, hips=H(xl, y0 + 0.05, 1.02), rball=V((xl - 0.15, y0 + 0.05, 0)), lball=V((xl + 0.15, y0 - 0.12, 0)),
             rtoe=V((-0.1,-1,0)), ltoe=V((0.1,-1,0)), rt=V((xl - 0.08, y0 - 0.22, 1.12)), lt=V((xl + 0.08, y0 - 0.22, 1.10)),
             rp=V((-0.6,0.5,-1)), lp=V((0.6,0.5,-1)), rh=V((0,-0.4,1)), rhw=0.6, lh=V((0,-0.4,1)), lhw=0.6,
             rpal=V((1,0,0)), rpalw=0.6, lpal=V((-1,0,0)), lpalw=0.6, **BALL, look=TARGET)
    keys = [(0, mark), (f_go - 2, P(mark, lean=4))]
    # ---------------- run-up in three stages (jog -> run -> sprint); take off from the LEFT foot
    def nsteps(f0, f1_, per): return max(1, int(round((f1_ - f0)/(per/2.0))))
    pers = (22, 18, 15)
    n3 = nsteps(f2, f_to, pers[2]); first3 = "l" if n3 % 2 == 0 else "r"
    def other(f): return "r" if f == "l" else "l"
    n2 = nsteps(f1, f2, pers[1]); first2 = first3 if n2 % 2 == 0 else other(first3)
    n1 = nsteps(f_go, f1, pers[0]); first1 = first2 if n1 % 2 == 0 else other(first2)
    segs = [(f_go, f1, y0, y0 - d1, pers[0], "accel", first1, 8, 0.14), (f1, f2, y0 - d1, y0 - d1 - d2, pers[1], None, first2, 11, 0.20),
            (f2, f_to, y0 - d1 - d2, y_to, pers[2], None, first3, 14, 0.24)]
    for i, (a, b, ya, yb, per, ease, first, lean, amp) in enumerate(segs):
        rk, _ = run_keys(mark, f0=a, f1=b, p0=(xl, ya), p1=(xl, yb), heading=0, period=per, contact=0.36 if i == 0 else 0.30,
                         lean=lean, ease=ease, hip_h=0.98, arm_amp=amp, first=first, extra=dict(look=TARGET, **BALL))
        keys += rk[1:]
    # ball comes up toward the chin over the last strides (bowling hand), the other arm keeps pumping
    def carry(f, p):
        w = max(0.0, min(1.0, (f - (f2 - 6))/14.0)) * (1 - max(0.0, min(1.0, (f - f_to)/4.0)))
        if w <= 0: return
        hp = V(p["hips"]) + V((0, 0, HIP0))
        chin = V((hp.x - 0.10, hp.y - 0.25, hp.z + 0.35 + 0.04*math.sin(f*0.8)))
        p["rt"] = V(p["rt"]).lerp(chin, 0.75*w); p["rh"] = V((0.1, -0.3, 1)); p["rhw"] = 0.6*w
    # ---------------- gather / bound: leap, right knee drives up, hands meet at the chin, body starts to turn
    yg = y_to - bound*0.55
    gather = P(BASE, hips=H(xl, yg, 0.98 + A["gather_up"]), pyaw=A["yaw_bfc"]*0.45, twist=A["twist_bfc"]*0.5, ptilt=2, lean=0, footup=0.5,
               rball=V((xl - 0.10, yg + 0.05, 0.42)), rheel=40, rkp=V((0,-1,0.6)), lball=V((xl + 0.10, yg + 0.45, 0.28)), lheel=45, lkp=V((0,-1,0.2)),
               rtoe=V((0,-1,0)), ltoe=V((0,-1,0)),
               rt=V((xl - 0.06, yg - 0.22, 1.48 + A["gather_up"] - 0.1)), lt=V((xl + 0.04, yg - 0.24, 1.46 + A["gather_up"] - 0.1)),
               rp=V((-0.8,0.3,-0.6)), lp=V((0.6,0.3,-0.6)), rh=V((0,-0.3,1)), rhw=0.6, lh=V((0,-0.3,1)), lhw=0.6, **BALL, look=TARGET)
    keys += [(f_to + 3, P(gather, hips=H(xl, y_to - bound*0.2, 1.06), rball=V((xl - 0.10, y_to - 0.25, 0.30)), lball=V((xl + 0.10, y_to + 0.05, 0.10)))),
             (f_to + 5, gather)]
    # ---------------- back-foot contact
    toe_b = rotz((0, -1, 0), A["bfc_toe"]) if True else None
    bfc = P(BASE, hips=H(xl + 0.02, y_bfc - 0.02, A["hip_bfc"]), pyaw=A["yaw_bfc"], twist=A["twist_bfc"], lean=-6, side=-6, footup=0.8,
            rball=V((xl - 0.02, y_bfc, 0)), rtoe=toe_b, rheel=6, rkp=rotz((0, -1, 0.2), A["bfc_toe"]),
            lball=V((xl + 0.05, y_bfc - 0.70, 0.40)), lheel=35, ltoe=V((0,-1,0)), lkp=V((0,-1,0.7)),
            rt=V((xl - 0.06, y_bfc - 0.18, 1.50)), rp=V((-0.8,0.6,-0.3)), rh=V((0,-0.2,1)), rhw=0.5,
            ldir=A["front_arm"], ldirw=1.0, lp=V((0.5,0.2,-1)), **BALL, look=TARGET)
    keys.append((f_bfc, bfc))
    # delivery stride: bowling arm drops down and back, front arm stays up, front leg reaches out
    keys.append((f_bfc + 2, P(bfc, hips=H(xl + 0.03, y_bfc - 0.45, A["hip_bfc"] - 0.02), pyaw=A["yaw_bfc"]*0.96, lean=-4,
                             lball=V((xl + 0.05, y_ffc + 0.45, 0.25)), lheel=20, rdir=arm_dir(30, tilt), rdirw=1.0, rhw=0.0)))
    keys.append((f_bfc + 4, P(bfc, hips=H(xl + 0.04, y_bfc - 0.80, A["hip_bfc"] - 0.04), pyaw=(A["yaw_bfc"] + A["yaw_ffc"])/2, lean=-2,
                             lball=V((xl + 0.06, y_ffc + 0.10, 0.08)), lheel=5, rdir=arm_dir(75, tilt), rdirw=1.0, rheel=25, rhw=0.0)))
    # ---------------- front-foot contact: braced front leg, bowling arm still back, front arm still up
    ffc = P(bfc, hips=H(xl + 0.05, y_ffc + 0.50, A["hip_bfc"] - 0.06), pyaw=A["yaw_ffc"], twist=A["twist_bfc"]*0.6, lean=2, side=0,
            lball=V((xl + 0.06, y_ffc, 0)), ltoe=V3(0.15,-1,0), lheel=0, lkp=V((0,-1,0.12)), rheel=40,
            rdir=arm_dir(105, tilt), rdirw=1.0, ldir=(A["front_arm"] + V((0, -0.2, -0.2))).normalized(), rh=arm_dir(85, tilt), rhw=0.7, rpal=V3(0,1,0.2), rpalw=0.5)
    keys.append((f_ffc, ffc))
    # the whip: arm keyed every frame, front arm pulls down hard, shoulders rotate past the hips, trunk flexes
    keys.append((f_ffc + 1, P(ffc, hips=H(xl + 0.05, y_ffc + 0.38, A["hip_bfc"] - 0.07), pyaw=A["yaw_ffc"]*0.75, twist=A["release_twist"]*0.2,
                              lean=A["lean_rel"]*0.30, side=A["side_rel"]*0.4, rdir=arm_dir(145, tilt), ldir=V3(0.30,-0.55,-0.10), rh=arm_dir(120, tilt), rhw=0.8, rheel=50)))
    keys.append((f_ffc + 2, P(ffc, hips=H(xl + 0.06, y_ffc + 0.24, A["hip_bfc"] - 0.08), pyaw=A["yaw_ffc"]*0.45, twist=A["release_twist"]*0.6,
                              lean=A["lean_rel"]*0.65, side=A["side_rel"]*0.8, rdir=arm_dir(178, tilt), ldir=V3(0.35,0.10,-0.85),
                              rh=arm_dir(160, tilt), rhw=0.9, rpal=V3(0,-0.6,0.8), rpalw=0.8, rheel=60)))
    rel = P(ffc, hips=H(xl + 0.06, y_ffc + 0.10, A["hip_bfc"] - 0.09), pyaw=A["yaw_ffc"]*0.25, twist=A["release_twist"], lean=A["lean_rel"],
            side=A["side_rel"], rdir=arm_dir(A["phi_rel"], tilt), rdirw=1.0, ldir=V3(0.35,0.45,-0.80), ldirw=1.0,
            rh=arm_dir(A["phi_rel"] + 14, tilt), rhw=0.95, rpal=V3(0,-1,0.05), rpalw=1.0, **FLICK, rheel=70, look=TARGET)
    keys.append((f_rel, rel))
    # ---------------- follow-through
    keys.append((f_rel + 2, P(rel, hips=H(xl + 0.08, y_ffc - 0.05, A["hip_bfc"] - 0.12), pyaw=A["yaw_ffc"]*0.05, twist=A["release_twist"] + 4,
                              lean=A["lean_rel"] + 10, rdir=arm_dir(250, tilt*0.7), rh=arm_dir(275, tilt*0.7), rhw=0.7, rpalw=0.4,
                              rball=V((xl - 0.04, y_bfc - 0.35, 0.10)), rheel=80, **fing("r", RELAXF))))
    keys.append((f_rel + 5, P(rel, hips=H(xl + 0.12, y_ffc - 0.25, A["hip_bfc"] - 0.14), pyaw=10, twist=A["release_twist"] + 6,
                              lean=A["lean_rel"] + 16, side=8, rdir=arm_dir(300, tilt*0.4), rh=arm_dir(320, tilt*0.4), rhw=0.5,
                              ldir=V3(0.55,0.65,-0.5), rball=V((xl - 0.02, y_ffc - 0.20, 0.30)), rheel=60, **fing("r", RELAXF))))
    ft2 = P(BASE, hips=H(xl + 0.25, y_ffc - 0.65, A["hip_bfc"] - 0.12), pyaw=22, twist=10, lean=A["lean_rel"] + 8, side=6, footup=1.0,
            rball=V((xl + 0.12, y_ffc - 1.25, 0)), rtoe=V3(0.3,-1,0), rkp=V((0.2,-1,0.2)), lball=V((xl + 0.06, y_ffc, 0)), ltoe=V3(0.15,-1,0), lheel=40,
            rt=V((xl + 0.40, y_ffc - 0.55, 0.72)), rp=V((-0.3,0.4,-1)), lt=V((xl + 0.55, y_ffc - 0.20, 1.05)), lp=V((0.6,0.5,-1)), rdirw=0.0, ldirw=0.0,
            **fing("r", RELAXF), look=TARGET)
    keys.append((f_rel + 9, ft2))
    p_from = V((xl + 0.12, y_ffc - 1.25)); p_to = p_from + V((1.5, -3.2))
    rk, _ = run_keys(ft2, f0=f_rel + 9, f1=n - 8, p0=tuple(p_from), p1=tuple(p_to), heading=-25, period=17, contact=0.40, lean=10,
                     ease="decel", hip_h=0.99, arm_amp=0.12, first="r", extra=dict(look=TARGET, **fing("r", RELAXF)))
    keys += [(t, P(k, rdirw=0.0, ldirw=0.0)) for t, k in rk[1:]]
    endp = P(place(P(STAND, look=TARGET), p_to.x, p_to.y - 0.1, -15), **fing("r", RELAXF))
    keys.append((n - 1, endp))
    keys.sort(key=lambda k: k[0])
    ded = []
    for fr, p in keys:
        if ded and abs(ded[-1][0] - fr) < 0.5: ded[-1] = (fr, p)
        else: ded.append((fr, p))
    meta = dict(action=kind, description=A["desc"], frames=n, seconds=round(n/30, 2), release_frame=f_rel + 1, back_foot_contact_frame=f_bfc + 1,
                front_foot_contact_frame=f_ffc + 1, take_off_frame=f_to + 1, run_up_m=round(y0 - y_ffc, 2),
                start_position_m=dict(behind_crease=round(y0, 2), left=xl), release_speed_kmh_typical={"side_on": 140, "front_on": 136, "slinger": 142}[kind])
    return n, ded, meta, carry

def rotz(v, deg): return R('Z', deg) @ V(v)

if __name__ == "__main__":
    fielding.OUTF = OUT2
    data, metas = {}, {}
    for arm in ("ra", "la"):
        for kind in ACTIONS:
            n, keys, meta, carry = bowl2(kind)
            ov = carry
            if arm == "la":
                keys = [(t, mirror(k)) for t, k in keys]
                def ov(f, p, base_ov=carry):
                    q = mirror(p); base_ov(f, q); r = mirror(q); p.clear(); p.update(r)
            name = f"{arm}_fast_{kind}_over"
            frames = make_field(name, n, keys, overlay=ov, lift=True, reach=lambda f: True)
            data[name] = frames; metas[name] = dict(file=name + ".fbx", arm="left-arm" if arm == "la" else "right-arm", **meta)
            print(name, "ok", n, "frames", flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "fb2_frames.pkl"), "wb")); json.dump(metas, open(_os.path.join(APL_WORK, "fb2_meta.json"), "w"), indent=1)
