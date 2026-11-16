"""
Shared pieces for the match-ceremony clips (ceremony_batter.py, ceremony_field.py): pose turning for the
batting rig, the batter's walking gait with the bat tucked / carried, and the pack JSON writer.
Output: build/motions/ceremony/<clip>.fbx + <clip>.json sidecars, merged into ceremony.json.
"""
import json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.join(os.path.dirname(HERE), "lib")
for p_ in (LIB, HERE):
    if p_ not in sys.path: sys.path.insert(0, p_)
import mixamo_motions as mm
from mixamo_motions import sample
import batting
from batting import bat_g, BAT_LEN, GRIP_R, P, V
from fielding import run_keys, inplace, fwd, left, R
from running_bat import carry_bat, free_arm, mirror_y, Hw, HIP0
from shots import STANCE

OUT = os.path.join(mm.APL_OUT, os.environ.get("APL_CEREMONY_DIR", "ceremony"))      # round 3 bat clips: APL_CEREMONY_DIR=ceremony_r3
os.makedirs(OUT, exist_ok=True)
UP = V((0, 0, 1))

# ---------------------------------------------------------------- turning a batting pose about the origin
POS_B = ("rball", "lball", "lt", "rt", "bg", "look")
DIR_B = ("rtoe", "ltoe", "rkp", "lkp", "ba", "bn", "rpole", "lpole", "rp", "lp", "rh", "lh", "rpal", "lpal")
def turn_pose(p, h, x=0.0, y=0.0):
    """rotate a batting-rig pose by h degrees about the origin, then move it by (x, y). lean/side are world-axis
    tilts, so their tilt vector turns with the body (as bowler_after.xf does)."""
    Rz = R('Z', h); q = dict(p)
    hp = Rz @ V((p["hips"].x, p["hips"].y, 0)); q["hips"] = V((hp.x + x, hp.y + y, p["hips"].z))
    for c in POS_B:
        if c in p: v = Rz @ V(p[c]); q[c] = V((v.x + x, v.y + y, v.z))
    for c in DIR_B:
        if c in p: q[c] = Rz @ V(p[c])
    q["pyaw"] = p["pyaw"] + h
    t = Rz @ V((p["side"], -p["lean"], 0)); q["side"], q["lean"] = t.x, -t.y
    return q

# ---------------------------------------------------------------- bats
def tuck_bat(hw, h, swing=0.0):
    """bat tucked under the right arm: handle in the bottom hand in front of the right chest, the blade passing back
    between the upper arm and the ribs, toe pointing back behind him (round 3)"""
    F = fwd(h); Rt = -left(h)
    bg = V((hw.x, hw.y, 0)) + F*(0.16 + swing) + Rt*0.19 + V((0, 0, hw.z + 0.12))
    a = (-F*0.95 + Rt*0.06 + V((0, 0, -0.30))).normalized()
    return bat_g(bg, a, Rt)

def carry_down(hw, h, swing=0.0):
    """bat carried by the handle in the bottom hand at his side, blade hanging down along the leg, toe ~0.2 m off the ground"""
    F = fwd(h); Rt = -left(h)
    bg = V((hw.x, hw.y, 0)) + F*(0.13 + swing) + Rt*0.26 + V((0, 0, hw.z - 0.14))
    a = (F*0.12 + Rt*0.08 + V((0, 0, -1))).normalized()          # a touch forward of and away from the thigh
    return bat_g(bg, a, F)

def walk_loop_keys(bat="tucked", period=34, stride=0.74, heading=90.0):
    """an in-place walking loop (two strides) for the batting rig, facing `heading` (90 = toward the bowler, +X)"""
    n = 2*period + 1; dist = stride*4
    d = fwd(heading)
    base = P(STANCE, lean=0, side=0, twist=0)
    kk, _ = run_keys(base, f0=0, f1=n - 1, p0=(0, 0), p1=tuple(d*dist), heading=heading, period=period, contact=0.62,
                     lean=3, bob=0.015, arm_amp=0.12, hip_h=1.02)
    kk = inplace(kk, 0, n - 1, (0, 0), tuple(d*dist))
    out = []
    for i, (t, k) in enumerate(kk):
        hw = V(k["hips"]) + V((0, 0, HIP0))
        ph = math.sin(2*math.pi*t/(n - 1)*2)                    # one bat sway per step pair
        b = tuck_bat(hw, heading, 0.015*ph) if bat == "tucked" else carry_down(hw, heading, 0.03*ph)
        q = P(k, **b, **free_arm(heading), legw=1.0, lookw=1.0, look=hw + d*12 + V((0, 0, 0.45)))
        q["lt"] = V(k["lt"])
        out.append((t, q))
    return n, out, dist/((n - 1)/30.0)

# ---------------------------------------------------------------- export + metadata
META = {}
def export_bat(name, n, keys, meta, loop=False, lhb_lift_from=None):
    """make_bat for rhb_/lhb_ (lhb = running_bat.mirror_y); lhb_lift_from: frame from which a hanging bat is raised
    5.5 cm for the left-hander (the non-striker pack's LHB_BAT_LIFT, so the end matches lhb_75_non_striker_idle_loop)"""
    batting.OUT = OUT
    for hand in ("rhb", "lhb"):
        if hand == "lhb":
            ks = [(t, mirror_y(k)) for t, k in keys]
            if lhb_lift_from is not None:
                ks = [(t, dict(k, bg=V(k["bg"]) + V((0, 0, 0.055)))) if t >= lhb_lift_from else (t, k) for t, k in ks]
        else: ks = keys
        full = f"{hand}_{name}"
        frames, worst, md = batting.make_bat(full, n, ks, None, dict(loop=loop))
        bad = {f"{k}@f{worst.get(k + '@', -1) + 1}": round(v, 3) for k, v in worst.items() if not k.endswith("@") and k.endswith("Grip") and v > 0.015}
        low = min(min(fr[0][mm.PFX + b][2] for b in ("RightToeBase", "LeftToeBase")) for fr in frames)
        if loop:
            dmax = max(abs(a - b) for nm_ in frames[0][0] for a, b in zip(frames[0][0][nm_], frames[-1][0][nm_]))
        else: dmax = None
        rows, clear = bat_rows(frames, ks, n)
        json.dump(dict(clip=full, rows=rows), open(os.path.join(OUT, full + ".battrack.json"), "w"), separators=(",", ":"))
        print(f"{full}: {n} f  grip {'OK' if not bad else bad}  min toe z {low:+.3f}  bat toe min z {min(r[2] + r[5]*(BAT_LEN - GRIP_R) for r in rows):+.2f}"
              f"  bat-body clearance {clear[0]:.2f} m @f{clear[1] + 1}" + (f"  loop seam {dmax*1000:.1f} mm" if loop else ""), flush=True)
        entry = dict(file=full + ".fbx", frames=n, batter="left-handed" if hand == "lhb" else "right-handed", loop=loop, **meta)
        write_meta(full, entry)

def _seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab)/max(1e-9, ab.dot(ab)))); return (p - (a + ab*t)).length
def bat_rows(frames, ks, n):
    """the bat track the game places the bat from: the SOLVED bat (make_bat may pull the grip in), per frame
    [grip xyz, axis xyz, face xyz, topgrip]; plus the closest the blade comes to the torso/thigh centre lines"""
    rows, clear = [], (9.0, 0)
    for f in range(n):
        top, toe = V(frames[f][2][0]), V(frames[f][2][1]); a = (toe - top).normalized()
        g = top + a*GRIP_R; pose = sample(ks, f); nrm = batting.ortho(pose["bn"], a)
        rows.append([round(c, 4) for c in (*g, *a, *nrm, float(pose.get("topgrip", 1.0)))])
        J = {k: V(v) for k, v in frames[f][0].items()}
        segs = [(J[mm.PFX + "Hips"], J[mm.PFX + "Neck"])] + [(J[mm.PFX + s_ + "UpLeg"], J[mm.PFX + s_ + "Leg"]) for s_ in ("Right", "Left")]
        for u in (0.35, 0.55, 0.75, 0.95):                     # blade points (the handle is in the hands by design)
            p = top + a*(BAT_LEN*u)
            d = min(_seg_dist(p, s0, s1) for s0, s1 in segs)
            if d < clear[0]: clear = (d, f)
            if d < 0.11 and os.environ.get("BAT_DEBUG"): print(f"    f{f + 1} u{u} clearance {d:.2f}")
    return rows, clear
def top_on_handle(bat):
    """where the top hand sits on the handle of bat_g(...) -- a free top hand parked here stays on the bat"""
    a = V(bat["ba"]).normalized(); n = batting.ortho(bat["bn"], a); e = a.cross(n).normalized(); f = (e + a*0.35).normalized()
    return V(bat["bg"]) - a*(GRIP_R - batting.GRIP_L) - f*0.075 + n*0.03       # the same wrist offset BatRig.solve uses for the top hand

def write_meta(full, entry):
    META[full] = entry
    json.dump(entry, open(os.path.join(OUT, full + ".json"), "w"), indent=1)

def pose_xy(p): return V((p["hips"].x, p["hips"].y, 0))

CONVENTIONS = {
 "rig": "Mixamo skeleton from Sitting_Talking.fbx (65 bones, mixamorig: names), skeleton only, 30 fps, frames start at 1",
 "batters": "rhb_/lhb_ clips use the batting frame (origin between the feet, +X toward the bowler's end, RHB chest faces -Y in the stance); bat on RightHand (rhb) / LeftHand (lhb); lhb_ = exact mirror across the pitch line (running_bat.mirror_y)",
 "others": "fielder_/captain_/presenter_ clips start at the origin facing the rig's forward direction (-Y); x = his left; ra_/la_ bowler clips use the bowling world (his popping crease at the origin, batter at -Y), la_ = mirror",
 "loops": "*_loop clips start and end in the same pose; walk-out loops are in place (speed_m_s given)",
 "props": "helmet / coin / microphone / ball: attach to the hand named in each clip's 'prop' note between the frames given",
 "chaining": "settle_after_* start from the last frame of the matching running clip and end in frame 1 of 75_non_striker_idle_loop turned 180 deg; take_guard ends in the batting stance; fielder_walk_back_to_position ends in run_00_ready_stance_idle_loop's first pose; mark_run_up ends standing at the mark facing the batter",
 "toss_layout": "flipper at the origin facing -Y; the other captain at x=+1.0 facing -Y; presenter at (0.5, -1.6) facing +Y (see captain_toss_flip's coin_* fields)",
}
def merge_pack():
    """ceremony.json = the conventions + every sidecar that has its FBX"""
    clips = [json.load(open(os.path.join(OUT, f))) for f in sorted(os.listdir(OUT))
             if f.endswith(".json") and f != "ceremony.json" and os.path.exists(os.path.join(OUT, f[:-5] + ".fbx"))]
    json.dump(dict(conventions=CONVENTIONS, clips=clips), open(os.path.join(OUT, "ceremony.json"), "w"), indent=1)
    print(f"ceremony.json: {len(clips)} clips", flush=True)
