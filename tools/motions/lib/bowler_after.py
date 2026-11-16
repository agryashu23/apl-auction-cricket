"""Bowler after the ball. Bowling world: bowler runs toward -Y, his popping crease at y=0, his stumps at (0, +1.22),
batter's stumps at (0, -18.9). Right-arm over the wicket runs in at x=+0.32."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import bowling as BW
import fielding
from fielding import *
from field_clips import READY, STAND, WITH_BALL, hands, HOLD_R, HOLD_L, OPEN_R, OPEN_L, place
OUTA = _os.path.join(APL_OUT, "bowler_after_ball"); os.makedirs(OUTA, exist_ok=True)
BATTER_STUMPS = V((0, -18.9, 0.5)); OWN_STUMPS = V((0, 1.22, 0.0)); BAIL_Z = 0.71; UMP = V((0.0, 2.8, 1.75))
def xf(p, x=0, y=0, h=0):
    q = place(p, x, y, h)
    if abs(h) > 1e-9:
        b = R('Z', h) @ V((p["side"], -p["lean"], 0)); q["side"], q["lean"] = b.x, -b.y
    return q
def heading_to(frm, to): d = V((to[0]-frm[0], to[1]-frm[1], 0)); return math.degrees(math.atan2(d.x, -d.y))
BALLH = fing("r", BW.HOLD_BALL)
# the pose every bowling clip starts from (standing at the top of the mark), at the origin
MARK = P(BASE, hips=H(0, 0.05, 1.02), rball=V((-0.15, 0.05, 0)), lball=V((0.15, -0.10, 0)), rtoe=V((-0.1,-1,0)), ltoe=V((0.1,-1,0)),
         rt=V((-0.08, -0.22, 1.12)), lt=V((0.08, -0.22, 1.10)), rp=V((-0.6,0.5,-1)), lp=V((0.6,0.5,-1)), rh=V((0,-0.4,1)), rhw=0.6,
         lh=V((0,-0.4,1)), lhw=0.6, rpal=V((1,0,0)), rpalw=0.6, lpal=V((-1,0,0)), lpalw=0.6, **BALLH, look=V((0, -25, 1.05)))
CLIPS = {}
def clip(name, n, keys, meta): CLIPS[name] = (n, sorted(keys, key=lambda k: k[0]), meta)

# ================================================================= walking back
def walk(f0, f1, p0, h, dist, period=34, polish=False):
    F = fwd(h); p1 = V((p0[0], p0[1], 0)) + F*dist
    rk, pl = run_keys(STAND, f0=f0, f1=f1, p0=p0, p1=(p1.x, p1.y), heading=h, period=period, contact=0.62, lean=3, bob=0.015, arm_amp=0.10, hip_h=1.01)
    out = []
    for i, (t, k) in enumerate(rk):
        k = P(k, **BALLH, look=V(k["hips"]) + V((0, 0, HIP0)) + F*6 + V((0, 0, -0.2)))
        out.append((t, k))
    return out, (p1.x, p1.y)
def polish_ov(f0, f1, h):
    """rub the ball on the front of the right thigh while walking"""
    def fn(f, p):
        if f < f0 or f > f1: return
        w = min(1, (f - f0)/6, (f1 - f)/6)
        hp = V(p["hips"]) + V((0, 0, HIP0)); F = fwd(h); L = left(h)
        rub = hp + F*0.14 - L*0.12 + V((0, 0, -0.18 + 0.06*math.sin((f - f0)*0.9)))
        p["rt"] = V(p["rt"]).lerp(rub, w); p["rh"] = (V((0, 0, -1)) + F*0.3); p["rhw"] = 0.8*w
        p["rpal"] = -F; p["rpalw"] = 0.7*w; p["look"] = V(p["look"]).lerp(hp + F*0.4 + V((0, 0, -0.6)), 0.6*w)
    return fn
def turn_walk_back():
    st = xf(P(STAND, **BALLH), 0, 0, 0)
    turn = P(xf(STAND, 0, 0.05, 110), **BALLH)
    wk, end = walk(18, 120, (0, 0.1), 180, 4.2)
    k = [(0, st), (6, st), (14, turn)] + wk[1:]
    CLIPS["turn_and_walk_back_to_mark"] = (121, k, dict(note="from the end of the follow-through: turns round and walks back toward the mark (root motion ~4.2 m), ball in hand; chain into walk_back_loop"), polish_ov(60, 100, 180))
def walk_loop():
    per = 34; n = 2*per + 1; dist = 1.4
    wk, _ = walk(0, n - 1, (0, 0), 180, dist, period=per)
    wk = inplace(wk, 0, n - 1, (0, 0), tuple(fwd(180)*dist))
    CLIPS["walk_back_loop"] = (n, wk, dict(loop=True, in_place=True, speed_m_s=round(dist/((n-1)/30), 2), note="walking back to the mark, ball in the right hand"), None)
def walk_loop_polish():
    per = 34; n = 2*per + 1; dist = 1.4
    wk, _ = walk(0, n - 1, (0, 0), 180, dist, period=per)
    wk = inplace(wk, 0, n - 1, (0, 0), tuple(fwd(180)*dist))
    CLIPS["walk_back_polishing_ball_loop"] = (n, wk, dict(loop=True, in_place=True, speed_m_s=round(dist/((n-1)/30), 2),
                                             note="walking back, rubbing the ball on the right thigh"), polish_ov(4, n - 5, 180))
def arrive_mark():
    wk, end = walk(0, 34, (0, -1.2), 180, 1.2)
    stop = P(xf(STAND, 0, 0.02, 180), **BALLH)
    turn = P(xf(STAND, 0, 0.02, 90), **BALLH, look=V((-3, 0, 1.2)))
    k = wk + [(42, stop), (54, turn), (70, MARK), (89, MARK)]
    CLIPS["arrive_at_mark_turn_ready"] = (90, k, dict(note="walks the last steps back, stops at the mark, turns to face the batter and settles into the exact pose the bowling clips start from"), None)
turn_walk_back(); walk_loop(); walk_loop_polish(); arrive_mark()

# ================================================================= caught and bowled
def cnb(name, action, kind):
    n0, keys, meta = BW.bowl("tmp", action=action, variant="stock" if action != "off_spin" else "off_break", side="over")
    rel = meta["release_frame"] - 1
    spin = action == "off_spin"
    k = [(t, kk) for t, kk in keys if t <= rel + 4]
    ft = dict(k[-1][1]); xl = 0.32
    back = (40 if not spin else 46)                         # frames from release until the ball comes back
    fc = rel + back
    if kind == "chest":
        C_ = V((xl + 0.15, -1.55, 1.35))
        ready = P(xf(STAND, xl + 0.12, -1.05, 0), hips=H(xl + 0.12, -1.05, 0.95), lean=6, **hands(C_ + V((0, -0.15, 0.05)), "reverse"), **OPEN_R, **OPEN_L, look=BATTER_STUMPS)
        at = P(ready, **hands(C_, "reverse"), look=C_)
        give = P(at, **hands(C_ + V((-0.05, 0.25, -0.15)), "reverse", fd=V((0,-0.3,1)), pd=V((0,-0.9,0.4))), **HOLD_R, **HOLD_L, look=C_ + V((0, 0.3, -0.3)))
        celeb = P(xf(STAND, xl + 0.12, -1.05, 0), rt=V((xl - 0.10, -1.10, 1.94)), lt=V((xl + 0.34, -1.10, 1.94)), rp=V((-1,0.3,-0.3)), lp=V((1,0.3,-0.3)),
                  rh=V((0,0,1)), rhw=0.8, lh=V((0,0,1)), lhw=0.8, **HOLD_R, look=V((0, -8, 4)))
        k += [(rel + 14, P(ready, **hands(C_ + V((0.1, -0.1, -0.3)), "reverse"))), (fc - 6, ready), (fc, at), (fc + 7, give), (fc + 24, celeb), (fc + 44, celeb), (fc + 60, xf(WITH_BALL, xl + 0.12, -1.05, 0))]
        n = fc + 61; note = "takes a firm return catch at chest height in the follow-through, then both arms up"
    elif kind == "dive":
        C_ = V((xl - 0.05, -3.35, 0.12))
        pre = P(xf(READY, xl, -1.5, 0), hips=H(xl, -1.6, 0.80), ptilt=40, **hands(C_ + V((0, 0.5, 0.3)), "custom", fd=V((0,-1,-0.1)), pd=V((0,-0.3,1))), **OPEN_R, **OPEN_L, look=C_,
                rball=V((xl - 0.12, -1.3, 0.05)), rheel=50, lball=V((xl + 0.12, -1.5, 0.25)), lheel=60, footup=0.4)
        air = P(pre, hips=H(xl, -2.55, 0.30), ptilt=84, footup=0.2, rball=V((xl - 0.12, -1.70, 0.28)), lball=V((xl + 0.12, -1.65, 0.38)), rheel=70, lheel=70,
                rkp=V((0,-1,-1)), lkp=V((0,-1,-1)), **hands(C_, "custom", fd=V((0,-1,0.05)), pd=V((0,0,1))))
        land = P(air, hips=H(xl, -2.62, 0.17), ptilt=88, rball=V((xl - 0.12, -1.75, 0.06)), lball=V((xl + 0.12, -1.72, 0.06)), rkp=V((0,0,-1)), lkp=V((0,0,-1)),
                 **hands(C_ + V((0, 0.05, 0.06)), "custom", fd=V((0,-1,0.2)), pd=V((0,0,1))), **HOLD_R, **HOLD_L)
        up = P(READY, hips=H(xl, -2.60, 0.55), ptilt=15, lean=25, rball=V((xl - 0.15, -2.35, 0)), rheel=75, lball=V((xl + 0.18, -3.0, 0)), rkp=V((0,-0.3,-1)),
               rt=V((xl - 0.07, -3.0, 0.85)), lt=V((xl + 0.07, -3.0, 0.84)), **HOLD_R, look=V((0, -20, 1.0)))
        k += [(fc - 8, pre), (fc, air), (fc + 6, land), (fc + 20, land), (fc + 40, up), (fc + 62, xf(WITH_BALL, xl, -2.75, 0)), (fc + 72, xf(WITH_BALL, xl, -2.75, 0))]
        n = fc + 73; note = "the ball dies in front of him: dives forward full length and takes it just off the ground, gets up"
    else:
        C_ = V((xl + 1.05, -1.45, 1.45))
        ready = P(xf(STAND, xl + 0.30, -1.10, 0), hips=H(xl + 0.32, -1.10, 0.92), side=18, lean=6, lball=V((xl + 0.70, -1.15, 0)), ltoe=V((0.5,-1,0)),
                  **hands(C_ + V((-0.2, -0.05, -0.05)), "custom", "l", fd=V((0.6, -0.1, 0.8)), pd=V((0, -1, 0.1))), rt=V((xl - 0.20, -1.0, 1.05)), rhw=0.0,
                  **OPEN_L, look=C_)
        at = P(ready, **hands(C_, "custom", "l", fd=V((0.6, -0.1, 0.8)), pd=V((0, -1, 0.1))), **HOLD_L, look=C_)
        give = P(at, **hands(C_ + V((-0.30, 0.10, -0.12)), "custom", "l", fd=V((0.4, -0.1, 0.9)), pd=V((0, -1, 0.1))), side=8, hips=H(xl + 0.28, -1.08, 0.95))
        k += [(fc - 8, ready), (fc, at), (fc + 7, give), (fc + 30, xf(WITH_BALL, xl + 0.25, -1.08, 0)), (fc + 50, xf(WITH_BALL, xl + 0.25, -1.08, 0))]
        n = fc + 51; note = "a firm return drive to his left: snaps it up one-handed (left hand) at full stretch"
    clip(name, n, k, dict(release_frame=rel + 1, catch_frame=fc + 1, ball_at_catch_m=dict(x=round(C_.x, 3), y=round(C_.y, 3), z=round(C_.z, 3)),
                          action=action, note=note + "; complete clip from the top of the mark"))
for act, tag in (("fast_medium", "pace"), ("off_spin", "spin")):
    cnb(f"caught_and_bowled_{tag}_chest_high", act, "chest")
    cnb(f"caught_and_bowled_{tag}_diving_forward", act, "dive")
    cnb(f"caught_and_bowled_{tag}_one_hand_left", act, "one_hand")

# ================================================================= fielding off his own bowling
def own_field(name, target):
    n0, keys, meta = BW.bowl("tmp", action="fast_medium", variant="stock", side="over")
    rel = meta["release_frame"] - 1
    k = [(t, kk) for t, kk in keys if t <= rel + 4]
    xl = 0.32; fp = rel + 34                                 # pick-up frame
    Cg = V((xl + 0.55, -2.35, 0.05))
    step = P(xf(READY, xl + 0.35, -1.5, 15), hips=H(xl + 0.35, -1.5, 0.92), look=Cg)
    pick = P(READY, hips=H(xl + 0.50, -1.95, 0.70), lean=44, ptilt=8, rball=V((xl + 0.30, -1.75, 0)), rheel=25, lball=V((xl + 0.72, -2.15, 0)), ltoe=V((0.3,-1,0)),
             **hands(Cg, "custom", "r", fd=V((0,-0.4,-1)), pd=V((0,-1,0.2))), lt=V((xl + 0.65, -2.2, 0.45)), **OPEN_R, look=Cg)
    k += [(rel + 14, step), (fp - 4, P(pick, **hands(Cg + V((0, 0.10, 0.15)), "custom", "r", fd=V((0,-0.4,-1)), pd=V((0,-1,0.2))))), (fp, pick)]
    x0, y0 = xl + 0.50, -2.0
    if target == "striker":
        tgt = BATTER_STUMPS; h = heading_to((x0, y0), tgt); F, L = fwd(h), left(h)
        base = xf(READY, x0, y0, h)
        cock = P(base, pyaw=h - 70, hips=H(x0, y0, 0.88), rt=V((x0, y0, 0)) - F*0.30 - L*0.35 + V((0, 0, 1.45)), rp=-L*0.6 + V((0, 0, -0.5)),
                 lt=V((x0, y0, 0)) + F*0.40 + V((0, 0, 1.15)), **HOLD_R, look=tgt)
        relp = P(base, pyaw=h - 15, ptilt=16, hips=H(x0, y0, 0.88), rt=V((x0, y0, 0)) + F*0.60 - L*0.10 + V((0, 0, 1.40)), rh=F + V((0, 0, 0.2)), rhw=0.8,
                 lt=V((x0, y0, 0)) + L*0.35 + V((0, 0, 0.90)), lball=V((x0, y0, 0)) + F*0.45 + L*0.12, **fing("r", CATCHF), look=tgt)
        fol = P(relp, ptilt=30, rt=V((x0, y0, 0)) + F*0.45 + L*0.30 + V((0, 0, 0.70)), rhw=0.2, **fing("r", RELAXF))
        fr = fp + 14
        k += [(fp + 8, P(cock, hips=H(x0, y0, 0.82))), (fr - 3, cock), (fr, relp), (fr + 6, fol), (fr + 26, P(base, **fing("r", RELAXF))), (fr + 36, base)]
        meta_t = dict(throw_target_m=dict(x=tgt.x, y=tgt.y), throw_speed_m_s=26, throw_type="snap throw at the striker's stumps")
        note = "fields a firm push back down the pitch in the follow-through and throws straight at the striker's stumps"
    else:
        tgt = V((OWN_STUMPS.x, OWN_STUMPS.y, 0.45)); h = heading_to((x0, y0), tgt); F = fwd(h); L = left(h)
        spin1 = P(xf(READY, x0, y0, 90), hips=H(x0, y0, 0.78), lean=0, **HOLD_R, look=tgt)
        base = xf(READY, x0, y0 + 0.1, h)
        back = P(base, hips=H(x0, y0 + 0.1, 0.80), rt=V((x0, y0 + 0.1, 0)) - F*0.25 - L*0.25 + V((0, 0, 0.45)), rh=V((0,0,-1)), rhw=0.6, **HOLD_R, look=tgt)
        relp = P(base, hips=H(x0, y0 + 0.25, 0.82), rt=V((x0, y0 + 0.25, 0)) + F*0.55 - L*0.15 + V((0, 0, 0.60)), rh=F + V((0, 0, -0.3)), rhw=0.8,
                 lball=V((x0, y0 + 0.25, 0)) + F*0.45 + L*0.15, **fing("r", CATCHF), look=tgt)
        fol = P(relp, rt=V((x0, y0 + 0.3, 0)) + F*0.60 - L*0.10 + V((0, 0, 1.05)), rh=F + V((0, 0, 0.6)), **fing("r", RELAXF))
        fr = fp + 16
        k += [(fp + 7, spin1), (fr - 4, back), (fr, relp), (fr + 6, fol), (fr + 26, base), (fr + 36, base)]
        meta_t = dict(throw_target_m=dict(x=tgt.x, y=tgt.y), throw_speed_m_s=11, throw_type="turn and underarm flick at his own stumps")
        note = "fields in the follow-through, spins round and flicks the ball underarm at the stumps at his own end"
    clip(name, fr + 37, k, dict(release_frame=rel + 1, pickup_frame=fp + 1, throw_release_frame=fr + 1, **meta_t, note=note + "; complete clip from the top of the mark"))
own_field("field_own_bowling_throw_at_striker_end", "striker")
own_field("field_own_bowling_underarm_at_own_stumps", "own")

# ================================================================= non-striker run-out (bowler stops in his stride and breaks the stumps)
def mankad():
    n0, keys, meta = BW.bowl("tmp", action="fast_medium", variant="stock", side="over")
    bfc = meta["back_foot_contact_frame"] - 1
    k = [(t, kk) for t, kk in keys if t <= bfc]
    xl = 0.32; bails = V((OWN_STUMPS.x + 0.02, OWN_STUMPS.y - 0.02, BAIL_Z - 0.02))
    stop = P(xf(STAND, xl + 0.05, 0.75, -60), hips=H(xl + 0.05, 0.80, 0.90), lean=4, lball=V((xl + 0.15, 0.40, 0)), ltoe=V((-0.2,-1,0)),
             rball=V((xl - 0.05, 1.20, 0)), rtoe=V((-1, -0.3, 0)), rt=V((xl - 0.10, 0.60, 1.30)), lt=V((xl + 0.10, 0.55, 1.25)), **BALLH, look=V((-1.5, 0.4, 1.2)))
    reach = P(stop, pyaw=-110, hips=H(xl - 0.02, 0.95, 0.82), lean=10, **hands(bails + V((0.12, -0.06, 0.10)), "custom", "r", fd=V((-0.7, 0.3, -0.6)), pd=V((-1, 0, -0.2))),
              **BALLH, look=bails)
    hit = P(reach, **hands(bails, "custom", "r", fd=V((-0.7, 0.3, -0.6)), pd=V((-1, 0, -0.2))), **BALLH)
    appeal = P(xf(STAND, xl, 0.85, 180), rt=V((xl + 0.24, 0.92, 1.93)), lt=V((xl - 0.24, 0.92, 1.93)), rp=V((1,-0.3,-0.3)), lp=V((-1,-0.3,-0.3)),
               rh=V((0,0,1)), rhw=0.8, lh=V((0,0,1)), lhw=0.8, **BALLH, look=UMP)
    k += [(bfc + 6, stop), (bfc + 12, reach), (bfc + 15, hit), (bfc + 22, P(hit, hips=H(xl - 0.02, 0.95, 0.88))), (bfc + 34, appeal), (bfc + 60, appeal),
          (bfc + 80, xf(WITH_BALL, xl, 0.85, 180)), (bfc + 90, xf(WITH_BALL, xl, 0.85, 180))]
    clip("non_striker_run_out", bfc + 91, k, dict(back_foot_contact_frame=bfc + 1, bails_broken_frame=bfc + 16,
         bails_m=dict(x=round(bails.x, 3), y=round(bails.y, 3), z=round(bails.z, 3)),
         note="runs in, stops in his delivery stride without letting go, whips the bails off at his own end with the ball in hand (non-striker out of his ground), turns and appeals; complete clip from the top of the mark"))
mankad()

if __name__ == "__main__":
    fielding.OUTF = OUTA
    names = sys.argv[1:] or list(CLIPS)
    data, metas = {}, {}
    for arm in ("ra", "la"):
        for nm in names:
            c = CLIPS[nm]
            n, keys, meta = c[0], c[1], c[2]; ov = c[3] if len(c) > 3 else None
            if arm == "la":
                keys = [(t, BW.mirror(k)) for t, k in keys]
                if ov:
                    base_ov = ov
                    def ov(f, p, base_ov=base_ov):
                        q = BW.mirror(p); base_ov(f, q); r = BW.mirror(q); p.clear(); p.update(r)
            full = f"{arm}_{nm}"
            frames = make_field(full, n, keys, overlay=ov, loop=meta.get("loop", False), lift=True, reach=lambda f: True)
            data[full] = frames; metas[full] = dict(file=full + ".fbx", frames=n, arm="left-arm" if arm == "la" else "right-arm", **meta)
            print(full, "ok max hand err %.3f" % max(make_field.last_errs), flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "ba_frames.pkl"), "wb")); json.dump(metas, open(_os.path.join(APL_WORK, "ba_meta.json"), "w"), indent=1)
