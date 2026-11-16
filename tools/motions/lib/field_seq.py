"""Combined fielding sequences (20-37). Built by placing existing fielding clips in a shared field frame and
adding new pieces. Field frame (Blender): Z up, fielder starts at the origin facing -Y (toward the batter / wickets)."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import fielding
import field_clips as FC
from fielding import *
from field_clips import READY, STAND, WITH_BALL, hands, HOLD_R, HOLD_L, OPEN_R, OPEN_L, place
OUTS = _os.path.join(APL_OUT, "fielding_sequences"); os.makedirs(OUTS, exist_ok=True); fielding.OUTF = OUTS
G = 9.81

# ---------------------------------------------------------------- transforms (rotate about origin by h, then move to x, y)
def xform(p, x=0.0, y=0.0, h=0.0):
    q = place(p, x, y, h)
    if abs(h) > 1e-9:                                  # spine bends are world-axis: turn them with the body
        b = R('Z', h) @ V((p["side"], -p["lean"], 0)); q["side"], q["lean"] = b.x, -b.y
    return q
def inv_xform(p, x, y, h): return xform(xform(p, -x, -y, 0.0), 0, 0, -h)
def seg(keys, f_from, f_to, offset, x=0.0, y=0.0, h=0.0):
    return [(offset + (t - f_from), xform(k, x, y, h)) for t, k in keys if f_from - 1e-6 <= t <= f_to + 1e-6]
def heading_to(frm, to):
    d = V((to[0]-frm[0], to[1]-frm[1], 0)); return math.degrees(math.atan2(d.x, -d.y))
def merge(*lists):
    out = sorted([k for l in lists for k in l], key=lambda k: k[0]); ded = []
    for t, p in out:
        if ded and abs(ded[-1][0]-t) < 0.5: ded[-1] = (t, p)
        else: ded.append((t, p))
    return ded
class Ov:
    """overlays from source clips, re-timed and re-placed"""
    def __init__(s): s.items = []
    def add(s, fn, offset, f_from, g0, g1, x=0.0, y=0.0, h=0.0): s.items.append((fn, offset, f_from, g0, g1, x, y, h))
    def add_world(s, fn, g0, g1): s.items.append((fn, None, None, g0, g1, 0, 0, 0))
    def __call__(s, f, p):
        for fn, off, ffrom, g0, g1, x, y, h in s.items:
            if g0 <= f <= g1:
                if off is None: fn(f, p); continue
                loc = inv_xform(p, x, y, h); fn(f - off + ffrom, loc); p.clear(); p.update(xform(loc, x, y, h))

K18 = FC.CLIPS["throw_18_overarm_throw"][1]; REL18 = 30          # crow-hop overarm throw toward local -Y
K19 = FC.CLIPS["throw_19_underarm_throw"][1]; REL19 = 18
def overarm(offset, x, y, h, upto=60):
    """crow-hop overarm throw starting from WITH_BALL at (x,y) facing heading h; release at offset+30"""
    ks = seg(K18, 0, 48, offset, x, y, h)
    ks.append((offset + upto, xform(place(READY, -0.02, -1.50), x, y, h)))
    return ks, offset + REL18
def release_meta(frame, frm, to, speed, kind="overarm"):
    d = V((to[0]-frm[0], to[1]-frm[1], 0)).normalized()
    return dict(release_frame=frame + 1, throw_type=kind, throw_target_m=dict(x=to[0], y=to[1]), throw_speed_m_s=speed,
                throw_direction=dict(x=round(d.x, 3), y=round(d.y, 3)))

# ---------------------------------------------------------------- generic overlays
def pick_ov(frame, side="r", width=10, depth=0.30, lateral=0.20):
    """bend down and sweep one hand to the ground just ahead of the foot, in whatever direction the fielder faces"""
    def fn(f, p):
        w = pulse(f, frame, width)
        if w <= 0 and f < frame: return
        if w > 0: p["gait"] = 0.0
        h = p["pyaw"]; F = fwd(h); L = left(h)
        hp = V(p["hips"]) + V((0, 0, HIP0))
        if f <= frame + width:
            sg = -1 if side == "r" else 1
            Cg = V((hp.x, hp.y, 0)) + F*depth + L*(sg*lateral) + V((0, 0, 0.05))
            t = hands(Cg, "custom", side, fd=(F*0.4 + V((0, 0, -1))).normalized(), pd=(F + V((0, 0, 0.2))).normalized())
            for k_, v_ in t.items(): p[k_] = (V(p[k_]).lerp(v_, w) if isinstance(v_, Vector) else p[k_]*(1-w) + v_*w)
            p["ptilt"] += 30*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.20*w))
            p["look"] = V(p["look"]).lerp(Cg, w)
        if f >= frame:
            for k_, v_ in fing(side, HOLDF).items(): p[k_] = v_
    return fn
def arm_path_ov(keys_):
    """straight-ish throwing arm along shoulder-relative directions: keys_ = [(frame, dir, weight)]"""
    def fn(f, p):
        if f < keys_[0][0] or f > keys_[-1][0]: return
        for (f0, d0, w0), (f1, d1, w1) in zip(keys_, keys_[1:]):
            if f0 <= f <= f1:
                u = (f - f0)/max(1e-6, f1 - f0); u = u*u*(3-2*u)
                p["rdir"] = V(d0).lerp(V(d1), u).normalized(); p["rdirw"] = w0*(1-u) + w1*u; return
    return fn

SEQ = {}
def sequence(name, n, keys, meta, overlay=None, reach=True):
    SEQ[name] = (n, keys, meta, overlay, reach)

KEEPER = (0.0, -30.0)          # target for most throws: straight ahead of the start position

# ================================================================= 20 chase back, pick up, turn, throw
def s20():
    k = seg(FC.CLIPS["run_09_turn_and_sprint_back"][1], 0, 46, 0)
    rk, _ = run_keys(READY, f0=46, f1=78, p0=(0, 5.5), p1=(0, 11.0), heading=180, period=17, contact=0.36, lean=12, ease="decel")
    ov = Ov(); ov.add_world(pick_ov(66, "r"), 52, 80)
    brace = P(xform(WITH_BALL, 0, 11.1, 180), hips=H(0, 11.15, 0.92), lean=0, side=0, ptilt=8)
    turn1 = P(xform(WITH_BALL, 0, 11.2, 100), hips=H(0, 11.2, 0.95))
    x0, y0 = 0.0, 11.25; h = heading_to((x0, y0), KEEPER)
    th, rel = overarm(98, x0, y0, h)
    keys = merge(k, rk[1:], [(84, brace), (91, turn1)], th)
    sequence("seq_20_chase_back_pickup_turn_throw", 160, keys,
             dict(pickup_frame=67, **release_meta(rel, (x0, y0-1.0), KEEPER, 30),
                  note="sprints back after the ball, picks it up on the move, brakes, turns through 180 deg and crow-hop throws back to the keeper/bowler"), ov)
# ================================================================= 21/22 chase sideways, pick up on the move, plant, throw
def s21(sg, name):
    hdg = 90*sg
    start = [(0, READY), (4, READY), (10, P(READY, pyaw=hdg*0.6, hips=H(sg*0.15, 0.0, 0.95), **{("lball" if sg > 0 else "rball"): V((sg*0.40, -0.05, 0))}))]
    rk, _ = run_keys(READY, f0=10, f1=44, p0=(sg*0.3, -0.05), p1=(sg*7.2, -0.8), heading=hdg - sg*6, period=16, contact=0.30, lean=14, ease="accel", first="l" if sg > 0 else "r")
    rk2, _ = run_keys(READY, f0=44, f1=58, p0=(sg*7.2, -0.8), p1=(sg*8.6, -1.0), heading=hdg - sg*10, period=17, contact=0.40, lean=8, ease="decel")
    ov = Ov(); ov.add_world(pick_ov(40, "r", lateral=0.24 if sg < 0 else 0.10), 30, 56)
    x0, y0 = sg*8.7, -1.05; h = heading_to((x0, y0), KEEPER)
    plant = P(xform(WITH_BALL, x0, y0, (hdg + h)/2), hips=H(x0, y0, 0.93))
    th, rel = overarm(66, x0, y0, h)
    keys = merge(start, rk[1:], rk2[1:], [(62, plant)], th)
    sequence(name, 130, keys, dict(pickup_frame=41, **release_meta(rel, (x0, y0), KEEPER, 30),
             note="runs to the %s, picks up on the move with the right hand, plants and crow-hop throws" % ("left" if sg > 0 else "right")), ov)

# ================================================================= 23 run in, pick up, throw on the run
def s23():
    target = (-14.0, -9.0)
    rk, _ = run_keys(READY, f0=0, f1=40, p0=(0, 3.0), p1=(-1.2, -6.5), heading=-8, period=17, contact=0.32, lean=12)
    rk2, _ = run_keys(READY, f0=40, f1=60, p0=(-1.2, -6.5), p1=(-2.2, -9.0), heading=-20, period=18, contact=0.4, lean=6, ease="decel")
    hT = heading_to((-1.0, -5.0), target)
    tF, tL = fwd(hT), left(hT)
    def throw_body(f, p):
        w = pulse(f, 33, 9)
        p["twist"] += (hT - p["pyaw"])*0.45*w; p["side"] += 0
        p["lt"] = V(p["lt"]).lerp(V(p["hips"]) + V((0, 0, HIP0)) + tF*0.45 + tL*0.2 + V((0, 0, 0.35)), w*0.85)
        if f > 33: p.update(fing("r", RELAXF))
    cock = (-tF*0.7 + V((0, 0, 0.7)) - tL*0.3); rel_d = (tF*0.9 + V((0, 0, 0.35))); fol = (tF*0.6 + tL*0.5 + V((0, 0, -0.6)))
    ov = Ov(); ov.add_world(pick_ov(22, "r", depth=0.35), 12, 32); ov.add_world(throw_body, 24, 44)
    ov.add_world(arm_path_ov([(25, cock, 0.0), (29, cock, 1.0), (33, rel_d, 1.0), (37, fol, 0.9), (43, fol, 0.0)]), 25, 43)
    keys = merge(rk, rk2[1:], [(70, xform(READY, -2.3, -9.3, -20)), (79, xform(READY, -2.3, -9.3, -20))])
    sequence("seq_23_run_in_pickup_throw_on_the_run", 80, keys,
             dict(pickup_frame=23, **release_meta(33, (-1.0, -5.0), target, 24, "on the run, three-quarter arm"),
                  note="attacks the ball, one-hand pick-up, throws on the run toward the stumps at the side without stopping, then slows"), ov)
# ================================================================= 24 run in from close, pick up, underarm at stumps
def s24():
    target = (-2.5, -9.0)
    rk, _ = run_keys(READY, f0=0, f1=34, p0=(0, 1.5), p1=(-0.4, -4.6), heading=-4, period=17, contact=0.34, lean=12)
    rk2, _ = run_keys(READY, f0=34, f1=52, p0=(-0.4, -4.6), p1=(-0.9, -6.3), heading=-8, period=18, contact=0.42, lean=6, ease="decel")
    hT = heading_to((-0.3, -3.6), target); tF = fwd(hT)
    back = (-tF*0.6 + V((0, 0, -0.8))); rel_d = (tF*0.85 + V((0, 0, -0.35))); fol = (tF*0.8 + V((0, 0, 0.45)))
    def lowbody(f, p):
        w = pulse(f, 26, 10); p["ptilt"] += 22*w; p["hips"] = V(p["hips"]) + V((0, 0, -0.18*w))
        if f > 28: p.update(fing("r", RELAXF))
    ov = Ov(); ov.add_world(pick_ov(20, "r", depth=0.32), 10, 30); ov.add_world(lowbody, 16, 40)
    ov.add_world(arm_path_ov([(21, back, 0.0), (24, back, 1.0), (28, rel_d, 1.0), (33, fol, 0.9), (40, fol, 0.0)]), 21, 40)
    keys = merge(rk, rk2[1:], [(62, xform(READY, -0.9, -6.5, -8)), (69, xform(READY, -0.9, -6.5, -8))])
    sequence("seq_24_run_in_pickup_underarm_at_stumps", 70, keys,
             dict(pickup_frame=21, **release_meta(28, (-0.3, -3.6), target, 12, "underarm flick"),
                  note="runs in from close, one-hand pick-up and an underarm flick at the stumps in one movement"), ov)
# ================================================================= 25/26 dive stop, get up, throw
def s25(sg, name):
    src = FC.CLIPS["field_15_dive_stop_left" if sg > 0 else "field_16_dive_stop_right"][1]
    k = seg(src, 0, 84, 0)
    x0, y0 = sg*1.40, 0.0; h = heading_to((x0, y0), KEEPER)
    th, rel = overarm(90, x0, y0, h)
    sequence(name, 152, merge(k, th), dict(stop_frame=31, **release_meta(rel, (x0, y0-1.0), KEEPER, 30),
             note="full-length dive to the %s, stops the ball, gets up and crow-hop throws" % ("left" if sg > 0 else "right")))
# ================================================================= 27 dive stop, throw from the ground (knees)
def s27():
    src = FC.CLIPS["field_15_dive_stop_left"][1]
    k = seg(src, 0, 46, 0)
    x0 = 1.40; tgt = (-6.0, -12.0); h = heading_to((x0, 0), tgt); F, L = fwd(h), left(h)
    knees = P(READY, hips=H(x0, 0.0, 0.58), pyaw=h, lean=0, side=0, ptilt=4,
              rball=V((x0, 0, 0)) - F*0.35 - L*0.14, rheel=75, rkp=-F*0.2 + V((0, 0, -1)), rtoe=F,
              lball=V((x0, 0, 0)) - F*0.30 + L*0.16, lheel=75, lkp=-F*0.2 + V((0, 0, -1)), ltoe=F,
              rt=V((x0, 0, 0)) + F*0.25 - L*0.10 + V((0, 0, 0.95)), lt=V((x0, 0, 0)) + F*0.25 + L*0.08 + V((0, 0, 0.93)),
              rp=-L*0.6 + V((0, 0, -1)), lp=L*0.6 + V((0, 0, -1)), **HOLD_R, **HOLD_L, look=V((tgt[0], tgt[1], 1.0)))
    cock = P(knees, twist=-35*1, rt=V((x0, 0, 0)) - F*0.30 - L*0.30 + V((0, 0, 1.45)), rp=-L*0.6 + V((0, 0, -0.8)),
             lt=V((x0, 0, 0)) + F*0.45 + L*0.10 + V((0, 0, 1.20)), lp=L*0.8 + V((0, 0, -0.6)))
    rel = P(knees, twist=15, ptilt=16, rt=V((x0, 0, 0)) + F*0.60 - L*0.12 + V((0, 0, 1.35)), rp=-L*0.8 + V((0, 0, -0.3)), rh=F + V((0, 0, 0.2)), rhw=0.8,
            lt=V((x0, 0, 0)) + F*0.10 + L*0.30 + V((0, 0, 0.80)), **fing("r", CATCHF))
    fol = P(rel, ptilt=26, twist=25, rt=V((x0, 0, 0)) + F*0.45 + L*0.30 + V((0, 0, 0.65)), rhw=0.3, **fing("r", RELAXF))
    keys = merge(k, [(60, knees), (68, cock), (73, rel), (79, fol), (92, fol), (110, P(knees, **fing("r", RELAXF), rt=V((x0,0,0)) + F*0.2 - L*0.12 + V((0,0,0.85))))])
    sequence("seq_27_dive_stop_throw_from_knees", 111, keys, dict(stop_frame=31, **release_meta(73, (x0, 0), tgt, 20, "from the knees"),
             note="dives, stops the ball, comes up onto both knees and throws without standing up"), None)
# ================================================================= 28 slide stop, get up, throw
def s28():
    src = FC.CLIPS["field_17_slide_stop"][1]
    k = seg(src, 0, 96, 0)
    x0, y0 = 0.0, -6.85; tgt = (0.0, 25.0); h = heading_to((x0, y0), tgt)
    th, rel = overarm(104, x0, y0, h)
    sequence("seq_28_slide_stop_get_up_throw", 166, merge(k, th), dict(stop_frame=54, **release_meta(rel, (x0, y0+1.0), tgt, 30),
             note="chases toward the boundary, slides, collects, gets up turning back to the field and throws back toward the start"))
# ================================================================= 29 boundary chase, slide, flick back
ROPE = -7.6
def s29_keys():
    src = FC.CLIPS["field_17_slide_stop"][1]
    k = seg(src, 0, 53, 0)
    SL = dict(footup=0.3, pside=24, lean=-20, side=8, rkp=V((0,-1,0.6)), lkp=V((0.6,-0.3,-0.2)), rtoe=V((0,-0.5,1)), ltoe=V((0.3,-1,0)))
    flick = P(READY, hips=H(0.10, -6.95, 0.22), **SL, rball=V((-0.12,-7.85,0.05)), lball=V((0.24,-6.85,0.0)), lheel=80,
              **hands(V((-0.30, -6.55, 0.55)), "custom", "r", fd=V((0, 0.6, 0.8)), pd=V((0, 1, 0.3))), lt=V((0.48,-6.75,0.20)), lp=V((1,0.3,-0.5)),
              **fing("r", CATCHF), look=V((0, -2, 1.5)))
    past = P(flick, hips=H(0.10, -7.25, 0.22), rball=V((-0.12,-8.10,0.05)), lball=V((0.24,-7.15,0.0)), rt=V((-0.35, -6.9, 0.70)), **fing("r", RELAXF))
    up = P(READY, hips=H(0.0, -7.9, 0.70), lean=0, rball=V((-0.18,-8.3,0)), rtoe=V((0,-1,0)), lball=V((0.18,-7.6,0)), lheel=75, lkp=V((0,-0.3,-1)),
           rt=V((-0.15,-8.0,0.85)), lt=V((0.15,-8.0,0.85)), look=V((0, 0, 1.0)))
    endp = xform(STAND, 0.0, -8.2, 180)
    return merge(k, [(57, flick), (64, past), (80, up), (96, endp), (109, endp)])
FLICK_F, FLICK_P = 56, V((-0.30, -6.62, 0.50))
def s29():
    vel = V((0.6, 7.5, 3.2))
    sequence("seq_29_boundary_chase_slide_flick_back", 110, s29_keys(),
             dict(collect_frame=54, flick_release_frame=FLICK_F + 1, flick_point_m=dict(x=FLICK_P.x, y=FLICK_P.y, z=FLICK_P.z),
                  flick_velocity_m_s=dict(x=vel.x, y=vel.y, z=vel.z), boundary_rope_y_m=ROPE,
                  note="chases to the rope, slides, scoops the ball and flicks it back inside before his body crosses the rope, ends outside facing the field"))
# ================================================================= 30 boundary flick-back + teammate throws (two clips)
def s30():
    vel = V((0.6, 7.5, 3.2))
    sequence("seq_30a_flick_back_fielder1", 110, s29_keys(),
             dict(role="fielder 1", flick_release_frame=FLICK_F + 1, flick_point_m=dict(x=FLICK_P.x, y=FLICK_P.y, z=FLICK_P.z),
                  flick_velocity_m_s=dict(x=vel.x, y=vel.y, z=vel.z), note="same action as seq_29, flicking the ball back to the teammate"))
    # fielder 2 runs across behind, picks up the flicked ball where it lands, throws to the keeper
    t_air = (vel.z + math.sqrt(vel.z**2 + 2*G*FLICK_P.z))/G
    land = FLICK_P + V((vel.x*t_air, vel.y*t_air, 0)); land.z = 0
    f_land = FLICK_F + round(t_air*30)
    ps = (land.x + 6.0, land.y + 2.0)
    pick_f = f_land + 8
    rk, _ = run_keys(READY, f0=pick_f - 32, f1=pick_f + 6, p0=ps, p1=(land.x + 0.25, land.y - 0.05 + 0.3), heading=heading_to(ps, (land.x, land.y)),
                     period=17, contact=0.34, lean=12, first="r", ease="accel",
                     start_feet={k_: xform(READY, ps[0], ps[1], heading_to(ps, (land.x, land.y)))[k_ + "ball"] for k_ in ("r", "l")})
    x0, y0 = land.x - 0.1, land.y + 0.6; h = heading_to((x0, y0), KEEPER if False else (0.0, 30.0))
    plant = P(xform(WITH_BALL, x0, y0, h), hips=H(x0, y0, 0.93))
    th, rel = overarm(pick_f + 16, x0, y0, h)
    keys = merge([(0, xform(READY, ps[0], ps[1], heading_to(ps, (land.x, land.y)))), (pick_f - 42, xform(READY, ps[0], ps[1], heading_to(ps, (land.x, land.y))))],
                 rk[1:], [(pick_f + 12, plant)], th)
    ov = Ov(); ov.add_world(pick_ov(pick_f, "r", depth=0.32), pick_f - 10, pick_f + 12)
    sequence("seq_30b_teammate_pickup_throw_fielder2", pick_f + 16 + 62, keys,
             dict(role="fielder 2", pickup_frame=pick_f + 1, ball_lands_frame=f_land + 1, ball_lands_at_m=dict(x=round(land.x, 2), y=round(land.y, 2)),
                  **release_meta(rel, (x0, y0), (0.0, 30.0), 30),
                  sync="play at the same time as seq_30a with both characters in the same field frame (fielder 2 starts 6 m to the side)",
                  note="runs in behind fielder 1, picks up the flicked-back ball and throws to the keeper"), ov)
# ================================================================= 31 relay throw (two clips)
def s31():
    # deep fielder at the origin facing the middle fielder 25 m away; middle fielder then throws on to the keeper 30 m beyond
    MID = (0.0, -25.0); KEEP = (0.0, -55.0)
    k = [(0, READY), (6, READY)]
    rk, _ = run_keys(READY, f0=6, f1=26, p0=(0, 0), p1=(0, -2.0), heading=0, period=18, contact=0.38, lean=10, ease="decel")
    pick = P(READY, hips=H(-0.02,-2.12,0.70), lean=46, ptilt=8, rball=V((-0.16,-1.98,0)), rheel=25, lball=V((0.14,-2.42,0)), ltoe=V((0.3,-1,0)),
             **hands(V((-0.02,-2.62,0.06)), "cup", fd=V((0,-0.4,-1)), pd=V((0,-1,0.3))), **OPEN_R, **OPEN_L, look=V((0,-2.6,0)))
    th, rel = overarm(44, 0.0, -2.3, heading_to((0, -2.3), MID))
    keys = merge(k, rk[1:], [(32, pick), (40, xform(WITH_BALL, 0, -2.3, 0))], th)
    d1 = (MID[1] - (-3.3)); speed1 = 28.0; t1 = abs(d1)/speed1
    sequence("seq_31a_relay_deep_fielder", 106, keys, dict(role="deep fielder", pickup_frame=33, **release_meta(rel, (0, -3.3), MID, speed1),
             note="collects in the deep and throws flat to the middle fielder"))
    # middle fielder: faces the deep fielder (+Y), catches at chest height, turns and throws to the keeper
    f_catch = rel + round(t1*30)
    face = 180.0
    mready = xform(READY, MID[0], MID[1], face)
    C = V((MID[0], MID[1] + 0.45, 1.30))
    ready_c = P(xform(P(STAND, hips=H(0, 0.02, 1.0)), MID[0], MID[1], face), **hands(C + V((0, 0.08, 0)), "reverse", fd=V((0, 0.12, 1)), pd=V((0, 1, 0.12))),
                **OPEN_R, **OPEN_L, look=V((0, -2, 1.5)))
    catch = P(ready_c, **hands(C, "reverse", fd=V((0, 0.12, 1)), pd=V((0, 1, 0.12))), look=C)
    give = P(catch, **hands(V((MID[0], MID[1] + 0.25, 1.15)), "reverse", fd=V((0, 0.3, 1)), pd=V((0, 1, 0.3))), **HOLD_R, **HOLD_L)
    x0, y0 = MID[0], MID[1] - 0.15
    turn = P(xform(WITH_BALL, x0, y0, 90), hips=H(x0, y0, 0.97))
    th2, rel2 = overarm(f_catch + 16, x0, y0, heading_to((x0, y0), KEEP))
    keys2 = merge([(0, mready), (f_catch - 12, mready), (f_catch - 5, ready_c), (f_catch, catch), (f_catch + 5, give), (f_catch + 11, turn)], th2)
    sequence("seq_31b_relay_middle_fielder", f_catch + 16 + 62, keys2,
             dict(role="middle fielder", catch_frame=f_catch + 1, catch_point_m=dict(x=C.x, y=C.y, z=C.z),
                  **release_meta(rel2, (x0, y0 - 1.0), KEEP, 30),
                  sync="start both clips together; the middle fielder stands 25 m from the deep fielder, facing him",
                  note="takes the relay at chest height, turns toward the keeper and throws on"))
# ================================================================= 32 long barrier, get up, throw
def s32():
    k = seg(FC.CLIPS["field_12_long_barrier"][1], 0, 62, 0)
    th, rel = overarm(68, 0, 0, 0)
    sequence("seq_32_long_barrier_get_up_throw", 130, merge(k, th), dict(stop_frame=33, **release_meta(rel, (0, -1), KEEPER, 30),
             note="long barrier, collects, stands and crow-hop throws"))
# ================================================================= 33 pick up facing away, spin, throw
def s33():
    target = (0.0, 25.0)                                  # stumps BEHIND the fielder's starting direction
    rk, _ = run_keys(READY, f0=0, f1=26, p0=(0, 4.0), p1=(0, -1.0), heading=0, period=17, contact=0.36, lean=12, ease="decel")
    ov = Ov(); ov.add_world(pick_ov(22, "r", depth=0.30), 12, 32)
    x0, y0 = 0.0, -1.2
    # spin to the left (anticlockwise from above) through 180 deg, releasing as the body comes round
    spin1 = P(xform(WITH_BALL, x0, y0, 70), hips=H(x0, y0, 0.92), rt=V((x0+0.5, y0+0.3, 1.35)), lt=V((x0+0.2, y0-0.4, 1.1)))
    spin2 = P(xform(WITH_BALL, x0, y0, 130), hips=H(x0, y0-0.05, 0.92), lball=V((x0+0.10, y0-0.15, 0)), rball=V((x0-0.30, y0+0.20, 0.10)), rheel=40)
    F = fwd(180); L = left(180)
    rel = P(xform(WITH_BALL, x0, y0+0.2, 175), hips=H(x0, y0+0.25, 0.92), ptilt=15, twist=10,
            rt=V((x0, y0+0.25, 0)) + F*0.55 - L*0.25 + V((0, 0, 1.45)), rp=-L*0.6 + V((0, 0, -0.4)), rh=F + V((0, 0, 0.2)), rhw=0.7,
            lt=V((x0, y0+0.25, 0)) - F*0.1 + L*0.35 + V((0, 0, 0.90)), lball=V((x0+0.15, y0+0.75, 0)), rball=V((x0-0.15, y0+0.05, 0)), rheel=50,
            **fing("r", CATCHF), look=V((0, 25, 1.0)))
    fol = P(rel, ptilt=28, twist=25, rt=V((x0, y0+0.3, 0)) + F*0.45 + L*0.35 + V((0, 0, 0.65)), rhw=0.2, rball=V((x0-0.10, y0+0.95, 0.0)), **fing("r", RELAXF))
    end = xform(READY, x0, y0+1.2, 180)
    keys = merge(rk, [(32, spin1), (37, spin2), (42, rel), (48, fol), (64, end), (74, end)])
    sequence("seq_33_pickup_spin_throw", 75, keys, dict(pickup_frame=23, **release_meta(42, (x0, y0+0.3), target, 26, "spin throw"),
             note="running in with the stumps behind him, picks up, spins round on the left foot (about 180 deg) and releases out of the spin"), ov)
# ================================================================= 34 pick up facing away, backhand flick at the stumps
def s34():
    target = (0.6, 6.0)                                   # stumps a few metres behind
    rk, _ = run_keys(READY, f0=0, f1=24, p0=(0, 3.5), p1=(0, -0.8), heading=0, period=17, contact=0.36, lean=12, ease="decel")
    ov = Ov(); ov.add_world(pick_ov(20, "r", depth=0.32), 10, 28)
    x0, y0 = 0.0, -0.9
    low = P(READY, hips=H(x0, y0, 0.78), lean=30, rball=V((x0-0.20, y0+0.05, 0)), lball=V((x0+0.20, y0-0.25, 0)),
            rt=V((x0-0.25, y0-0.30, 0.35)), rh=V((0,-0.3,-1)), rhw=0.6, lt=V((x0+0.25, y0-0.25, 0.55)), lp=V((0.6,0.4,-1)), **HOLD_R, look=V((x0, y0-1, 0.2)))
    flick = P(low, lean=24, rt=V((x0-0.30, y0+0.40, 0.55)), rp=V((-1, 0, -0.3)), rh=V((0.1, 1, 0.3)), rhw=0.9, rpal=V((0,1,0)), rpalw=0.7,
              **fing("r", CATCHF), look=V((x0-0.2, y0+0.2, 0.4)))
    fol = P(flick, rt=V((x0-0.28, y0+0.55, 0.85)), rh=V((0, 1, 0.8)), **fing("r", RELAXF))
    end = xform(READY, x0, y0, 0)
    keys = merge(rk, [(26, low), (31, flick), (36, fol), (54, end), (64, end)])
    sequence("seq_34_pickup_backhand_flick_at_stumps", 65, keys, dict(pickup_frame=21, **release_meta(31, (x0-0.3, y0+0.4), target, 9, "backhand flick"),
             note="picks up facing away from the stumps and flicks the ball backwards at them with the back of the hand, without turning"), ov)
# ================================================================= 35 catch goes down, pick up, quick throw
def s35():
    C = V((0, -0.48, 0.66)); ground = V((0.05, -0.75, 0.05))
    k = [(0, READY), (8, READY),
         (22, P(READY, hips=H(0,0.02,0.88), lean=20, **hands(C+V((0,-0.08,0.04)), "cup"), **OPEN_R, **OPEN_L, look=V((0,-4,0.9)))),
         (30, P(READY, hips=H(0,0.02,0.86), lean=22, **hands(C, "cup"), **OPEN_R, **OPEN_L, look=C)),
         (34, P(READY, hips=H(0,0.0,0.84), lean=26, **hands(C+V((0,-0.10,-0.12)), "cup"), **OPEN_R, **OPEN_L, look=ground)),       # ball spills out
         (42, P(READY, hips=H(0.02,-0.20,0.72), lean=44, lball=V((0.22,-0.45,0)), **hands(ground, "custom", "r", fd=V((0,-0.4,-1)), pd=V((0,-1,0.2))),
                lt=V((0.2,-0.4,0.6)), **OPEN_R, look=ground)),
         (46, P(READY, hips=H(0.02,-0.20,0.80), lean=30, lball=V((0.22,-0.45,0)), rt=V((-0.10,-0.50,0.60)), **HOLD_R, look=V((-8,-6,1.0))))]
    tgt = (-12.0, -8.0); x0, y0 = 0.02, -0.25; h = heading_to((x0, y0), tgt); F, L = fwd(h), left(h)
    base = xform(READY, x0, y0, h)
    cock = P(base, pyaw=h - 70, hips=H(x0, y0, 0.86), rt=V((x0, y0, 0)) - F*0.30 - L*0.35 + V((0, 0, 1.45)), rp=-L*0.6 + V((0, 0, -0.5)),
             lt=V((x0, y0, 0)) + F*0.40 + V((0, 0, 1.15)), **HOLD_R, look=V((tgt[0], tgt[1], 1.0)))
    rel = P(base, pyaw=h - 15, ptilt=16, hips=H(x0, y0, 0.86), rt=V((x0, y0, 0)) + F*0.60 - L*0.10 + V((0, 0, 1.40)), rh=F + V((0, 0, 0.2)), rhw=0.8,
            lt=V((x0, y0, 0)) + L*0.35 + V((0, 0, 0.90)), lball=V((x0, y0, 0)) + F*0.45 + L*0.12, **fing("r", CATCHF), look=V((tgt[0], tgt[1], 1.0)))
    fol = P(rel, ptilt=30, rt=V((x0, y0, 0)) + F*0.45 + L*0.30 + V((0, 0, 0.70)), rhw=0.2, **fing("r", RELAXF))
    keys = merge(k, [(51, cock), (55, rel), (61, fol), (78, P(base, **fing("r", RELAXF))), (89, base)])
    sequence("seq_35_dropped_catch_pickup_quick_throw", 90, keys,
             dict(catch_attempt_frame=31, ball_spills_frame=34, pickup_frame=43, **release_meta(55, (x0, y0), tgt, 24, "snap throw"),
                  note="goes for the catch, the ball pops out, he picks it up straight away and snap-throws for the run-out (no crow hop)"))
# ================================================================= 36 back running catch, turn, return throw
def s36():
    src = FC.CLIPS["catch_09_back_running_catch"]
    k = seg(src[1], 0, 98, 0)
    ov = Ov(); ov.add(src[3], 0, 0, 0, 98)
    x0, y0 = 0.0, 9.8
    pivot = P(xform(WITH_BALL, x0, y0, 100), hips=H(x0, y0, 0.95))
    th, rel = overarm(112, x0, y0, heading_to((x0, y0), KEEPER))
    sequence("seq_36_back_running_catch_turn_throw", 174, merge(k, [(105, pivot)], th),
             dict(catch_frame=61, **release_meta(rel, (x0, y0-1.0), KEEPER, 30),
                  note="runs back, takes the catch over the shoulder, pulls up, turns and throws the ball back in"), ov)
# ================================================================= 37 diving catch, roll, celebrate, return the ball
def s37():
    src = FC.CLIPS["catch_05_diving_left"][1]
    k = seg(src, 0, 40, 0)
    x = 1.35
    back = P(READY, hips=H(x+0.05, 0.05, 0.22), pside=175, ptilt=0, lean=0, side=0, footup=0.2,
             rball=V((x-0.55, 0.30, 0.30)), lball=V((x-0.45, -0.10, 0.45)), rheel=40, lheel=40, rkp=V((0,0,1)), lkp=V((0,0,1)),
             **hands(V((x+0.40, -0.05, 0.60)), "reverse", fd=V((0.3, 0, 1)), pd=V((0, -0.3, 1))), **HOLD_R, **HOLD_L, look=V((x+0.4, -0.1, 3.0)))
    sit = P(READY, hips=H(x, 0.05, 0.35), pside=0, ptilt=-10, lean=-10, rball=V((x-0.15, -0.70, 0)), lball=V((x+0.18, -0.65, 0)), rkp=V((0,-1,1)), lkp=V((0,-1,1)),
            rheel=0, lheel=0, rt=V((x-0.05, -0.30, 0.95)), lt=V((x+0.08, -0.30, 0.93)), **HOLD_R, look=V((x, -10, 1.2)))
    stand = xform(WITH_BALL, x, 0.0, 0)
    celebrate = P(stand, rt=V((x-0.24, -0.05, 1.96)), rp=V((-1,0.3,-0.3)), rh=V((0,0,1)), rhw=1.0, rpal=V((0,-1,0)), rpalw=0.8, **HOLD_R,
                  lt=V((x+0.30, 0.0, 1.30)), lp=V((1,0.3,-0.6)), lean=-6, look=V((x, -15, 3.0)))
    tgt = (0.0, -22.0); h = heading_to((x, 0), tgt); F, L = fwd(h), left(h)
    lob_back = P(xform(WITH_BALL, x, 0.0, h), rt=V((x, 0, 0)) - F*0.30 - L*0.30 + V((0, 0, 1.40)), rp=-L*0.6 + V((0, 0, -0.5)), **HOLD_R, look=V((tgt[0], tgt[1], 1.5)))
    lob_rel = P(lob_back, ptilt=8, rt=V((x, 0, 0)) + F*0.50 - L*0.15 + V((0, 0, 1.60)), rh=F + V((0, 0, 0.5)), rhw=0.8, **fing("r", CATCHF))
    lob_fol = P(lob_rel, rt=V((x, 0, 0)) + F*0.40 + L*0.10 + V((0, 0, 1.05)), rhw=0.2, **fing("r", RELAXF))
    end = xform(STAND, x, 0.0, h)
    keys = merge(k, [(52, back), (62, back), (76, sit), (92, stand), (102, celebrate), (118, celebrate), (126, xform(WITH_BALL, x, 0, 0)),
                     (134, lob_back), (139, lob_rel), (145, lob_fol), (160, end), (169, end)])
    sequence("seq_37_diving_catch_roll_celebrate_return", 170, keys,
             dict(catch_frame=31, **release_meta(139, (x, 0), tgt, 14, "gentle lob to the keeper"),
                  note="dives left, takes the catch, rolls onto his back holding the ball up, sits up, stands, raises the ball to celebrate, then lobs it to the keeper"))

for fn in (s20, s23, s24, s27, s28, s29, s30, s31, s32, s33, s34, s35, s36, s37): fn()
s21(1, "seq_21_chase_left_pickup_throw"); s21(-1, "seq_22_chase_right_pickup_throw")
s25(1, "seq_25_dive_stop_left_get_up_throw"); s25(-1, "seq_26_dive_stop_right_get_up_throw")

if __name__ == "__main__":
    names = sys.argv[1:] or sorted(SEQ)
    data, metas = {}, {}
    for nm in names:
        n, keys, meta, ov, reach = SEQ[nm]
        frames = make_field(nm, n, keys, overlay=ov, lift=True, reach=(lambda f: True) if reach else None)
        data[nm] = frames; metas[nm] = dict(file=nm+".fbx", frames=n, **meta)
        print(nm, "ok", n, "max hand err %.3f" % max(make_field.last_errs), flush=True)
    tag = "all" if len(names) == len(SEQ) else "part"
    pickle.dump(data, open(f_os.path.join(APL_WORK, "seq_frames_{tag}.pkl"),"wb")); json.dump(metas, open(f_os.path.join(APL_WORK, "seq_meta_{tag}.json"),"w"), indent=1)
