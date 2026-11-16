"""Wicketkeeper on the Mixamo rig. Keeper faces the rig's forward (-Y) toward the bowler.
Keeper's LEFT (+X) = off side for a right-handed batter, RIGHT (-X) = leg side. Origin = keeper's feet in the stance.
Standing up: stumps are just in front and to the keeper's right at STUMPS (bails 0.71 m high)."""
import os as _os; _APL_REPO = _os.path.abspath(_os.path.join(_os.path.dirname(_os.path.abspath(__file__)), "..", "..", "..")).replace("\\", "/")  # repo root
APL_RIG = _os.environ.get("APL_RIG", _APL_REPO + "/client-assets/mixamo/sit_talk.fbx")
APL_OUT = _os.environ.get("APL_MOTION_OUT", _APL_REPO + "/build/motions")
APL_WORK = _os.environ.get("APL_MOTION_WORK", _APL_REPO + "/build/motions/work")
_os.makedirs(APL_WORK, exist_ok=True)
import sys, math, json, pickle, os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import fielding
import field_clips as FC
from fielding import *
from field_clips import STAND, WITH_BALL, hands, HOLD_R, HOLD_L, OPEN_R, OPEN_L, place
OUTK = _os.path.join(APL_OUT, "wicketkeeper"); os.makedirs(OUTK, exist_ok=True); fielding.OUTF = OUTK
STUMPS = V((-0.28, -0.62, 0.0)); BAIL_Z = 0.71
GLOVE = dict(T=0.15, I=0.10, M=0.10, R=0.12, P=0.15)          # big open keeping gloves
GLOVES_OPEN = {**fing("r", GLOVE), **fing("l", GLOVE)}

def squat(depth=0.50, x=0.0, y=0.0):
    return P(BASE, hips=H(x, y+0.06, depth), lean=24, ptilt=10,
             rball=V((x-0.27, y-0.05, 0)), lball=V((x+0.27, y-0.05, 0)), rtoe=V((-0.35,-1,0)), ltoe=V((0.35,-1,0)), rheel=25, lheel=25,
             rkp=V((-0.6,-1,0.2)), lkp=V((0.6,-1,0.2)),
             **hands(V((x, y-0.42, 0.18)), "cup", fd=V((0,-0.3,-1)), pd=V((0,-1,0.2)), spread=0.06),
             **GLOVES_OPEN, look=V((x, -20, 1.2)))
CROUCH_BACK = squat(0.52)                                # standing back, ~15-20 m behind the stumps
CROUCH_UP = squat(0.47)                                  # standing up to the stumps for spin
def half(x=0.0, y=0.0, z=0.80, lean=18):                 # rising with the ball
    return P(CROUCH_BACK, hips=H(x, y+0.04, z), lean=lean, rheel=8, lheel=8,
             rball=V((x-0.27, y-0.05, 0)), lball=V((x+0.27, y-0.05, 0)),
             **hands(V((x, y-0.45, 0.62)), "cup", fd=V((0,-0.5,-0.8)), pd=V((0,-1,0.3))), look=V((x, -10, 1.0)))
def ev(kind, frame, C_, extra=None):
    d = dict(type=kind, event_frame=frame+1, ball_at_event_m=dict(forward=round(-C_[1],3), left=round(C_[0],3), up=round(C_[2],3)))
    if extra: d.update(extra)
    return d

CLIPS = {}
def clip(name, n, keys, meta, overlay=None, loop=False, reach=True): CLIPS[name] = (n, keys, meta, overlay, loop, reach)

# ================================================================= stances
def breathe(base, dz=0.012):
    return [(0, base), (20, P(base, hips=V(base["hips"])+V((0,0,dz)))), (40, P(base, hips=V(base["hips"])+V((0.008,0,0)), look=V((0.5,-20,1.2)))),
            (60, P(base, hips=V(base["hips"])+V((0,0,dz*0.5)))), (80, base)]
clip("wk_stance_back_loop", 81, breathe(CROUCH_BACK), dict(type="stance loop", loop=True, note="squat stance standing back to pace, gloves together low"), loop=True)
clip("wk_stance_up_loop", 81, breathe(CROUCH_UP), dict(type="stance loop", loop=True, stumps_m=dict(forward=-STUMPS.y, left=STUMPS.x),
     note="squat stance standing up to the stumps for spin"), loop=True)

# ================================================================= standing back: takes
def take(name, C_, style, *, step=None, z_hips=0.80, note="", rise_frame=14, tc=30, lean=18):
    st = dict(step or {})
    hx = st.pop("_hx", 0.0)
    pre = P(half(hx, 0, z_hips, lean), **st, **hands(C_ + V((0, -0.10, 0.03)), style), **GLOVES_OPEN, look=V(C_)+V((0,-3,0.2)))
    at = P(pre, **hands(C_, style), look=C_)
    give_c = V((hx, -0.25, max(0.75, min(C_.z, 1.3))))
    give = P(at, **hands(give_c, "reverse" if C_.z > 0.9 else "cup", fd=V((0,-0.3,1)) if C_.z > 0.9 else None, pd=V((0,-0.9,0.4)) if C_.z > 0.9 else None),
             **HOLD_R, **HOLD_L, hips=V(at["hips"])+V((0,0.04,0.03)), look=give_c)
    endp = place(WITH_BALL, hx, 0.0)
    keys = [(0, CROUCH_BACK), (rise_frame-6, CROUCH_BACK), (rise_frame+6, P(half(hx*0.6, 0, (z_hips+0.52)/2+0.08, lean), **st)), (tc-5, pre),
            (tc, at), (tc+7, give), (tc+26, endp), (tc+50, endp)]
    clip(name, tc+51, keys, ev("take", tc, C_, dict(note=note)))
take("wk_take_back_waist", V((0, -0.55, 0.85)), "cup", note="ball arriving at waist height, gloves fingers-down")
take("wk_take_back_chest", V((0, -0.50, 1.25)), "reverse", z_hips=0.95, lean=8, note="chest height, fingers up, thumbs together")
take("wk_take_back_head_high", V((0, -0.40, 1.95)), "reverse", z_hips=1.05, lean=0,
     step=dict(rheel=40, lheel=40), note="bouncer carrying through high, stretches up onto the toes")
take("wk_take_back_low", V((0, -0.62, 0.22)), "cup", z_hips=0.60, lean=34, note="low / dipping ball taken just off the ground")
take("wk_take_back_offside", V((0.95, -0.50, 0.95)), "custom" if False else "reverse", z_hips=0.88, lean=12,
     step=dict(_hx=0.55, lball=V((0.85, -0.05, 0)), rball=V((0.30, -0.02, 0)), ltoe=V((0.6,-1,0)), side=10),
     note="shuffles across to the off side (keeper's left) to take a wide ball")
take("wk_take_back_legside", V((-0.95, -0.50, 0.95)), "reverse", z_hips=0.88, lean=12,
     step=dict(_hx=-0.55, rball=V((-0.85, -0.05, 0)), lball=V((-0.30, -0.02, 0)), rtoe=V((-0.6,-1,0)), side=-10),
     note="shuffles across to the leg side (keeper's right) to take a ball down leg")

# ---------------- diving catches standing back (reuse the fielding dive, starting from the keeper's squat)
def wk_dive(name, sg, C_):
    FC.dive("__tmp", sg, C_=C_)
    n, keys, meta = FC.CLIPS.pop("__tmp")[:3]
    keys = [(t, k) for t, k in keys if t >= 14]
    k0 = P(half(sg*0.15, 0, 0.70, 22), look=C_+V((0,-3,0.3)))
    keys = [(0, CROUCH_BACK), (6, CROUCH_BACK), (11, k0)] + keys
    clip(name, n, keys, ev("diving catch", 30, C_, dict(note="dives full length to the %s from the stance; ends standing with the ball 1.4 m across" % ("off side (left)" if sg > 0 else "leg side (right)"))))
wk_dive("wk_dive_catch_back_left", 1, V((1.95, -0.25, 0.65)))
wk_dive("wk_dive_catch_back_right", -1, V((-1.95, -0.25, 0.65)))

# ---------------- fumble -> byes: ball bursts through the gloves, keeper turns and chases
def fumble():
    C_ = V((0.15, -0.55, 0.70))
    pre = P(half(0.05, 0, 0.78), **hands(C_ + V((0,-0.10,0.03)), "cup"), look=C_)
    hit = P(pre, **hands(C_, "cup"), look=C_)
    spill = P(pre, **hands(C_ + V((0.05, 0.12, -0.12)), "cup"), look=V((0.4, 2.0, 0.1)), hips=H(0.05, 0.06, 0.76))
    turn = P(half(0.05, 0.1, 0.88), pyaw=150, rball=V((-0.20, 0.10, 0)), lball=V((0.25, 0.25, 0)), rtoe=V((0.4,1,0)), ltoe=V((0.6,0.8,0)),
             rt=V((-0.2, 0.3, 0.95)), lt=V((0.2, 0.3, 0.95)), look=V((0.6, 4.0, 0.1)))
    rk, _ = run_keys(BASE, f0=46, f1=80, p0=(0.1, 0.4), p1=(0.9, 6.0), heading=172, period=16, contact=0.30, lean=14, ease="accel")
    rk = [(t, P(k, **GLOVES_OPEN, look=V((1.4, 9.0, 0.1)))) for t, k in rk]
    keys = [(0, CROUCH_BACK), (8, CROUCH_BACK), (20, half(0.03, 0, 0.72)), (25, pre), (30, hit), (34, spill), (42, turn)] + rk[1:] + [(90, rk[-1][1])]
    clip("wk_fumble_turn_chase_byes", 91, keys, ev("missed take", 30, C_, dict(ball_spills_frame=35,
         note="ball bursts out of the gloves and runs away behind; keeper turns and chases (root motion ~6 m back)")))
fumble()

# ================================================================= standing up (spin)
def take_up(name, C_, *, side, stump=False, tc=30):
    sx = 1 if side == "off" else -1
    base_x = 0.18*sx if side == "off" else -0.42
    rise = P(CROUCH_UP, hips=H(base_x*0.5, 0.06, 0.62), lean=22, rheel=10, lheel=10)
    st = dict(lball=V((0.55, -0.05, 0))) if side == "off" else dict(rball=V((-0.80, -0.02, 0)), lball=V((-0.12, 0.0, 0)), rtoe=V((-0.5,-1,0)))
    pre = P(rise, **st, hips=H(base_x, 0.05, 0.62), side=8*sx, **hands(C_ + V((0,-0.08,0.03)), "cup", fd=V((0,-0.4,-0.9))), look=C_+V((0,-2,0.1)))
    at = P(pre, **hands(C_, "cup", fd=V((0,-0.4,-0.9))), **HOLD_R, **HOLD_L, look=C_)
    keys = [(0, CROUCH_UP), (8, CROUCH_UP), (20, rise), (tc-5, pre), (tc, at)]
    meta = ev("take (standing up)", tc, C_, dict(stumps_m=dict(forward=-STUMPS.y, left=STUMPS.x, bail_height=BAIL_Z)))
    if stump:
        bails = V((STUMPS.x, STUMPS.y + 0.08, BAIL_Z - 0.02))
        swipe = P(at, **hands(bails + V((0.12*sx, 0, 0.12)), "custom", fd=V((-sx*0.6, -0.4, -0.7)), pd=V((-sx, 0, -0.3))), hips=H(base_x*0.5 - 0.08, 0.02, 0.60), look=bails)
        hitb = P(swipe, **hands(bails, "custom", fd=V((-sx*0.6, -0.4, -0.7)), pd=V((-sx, 0, -0.3))), side=0)
        appeal = P(STAND, hips=H(-0.1, 0.0, 1.0), **hands(V((-0.1, -0.15, 1.90)), "reverse"), **HOLD_R, **HOLD_L, look=V((8, -6, 2.0)))
        keys += [(tc+4, swipe), (tc+7, hitb), (tc+14, P(hitb, hips=V(hitb["hips"])+V((0,0,0.05)))), (tc+30, appeal), (tc+44, appeal), (tc+64, place(WITH_BALL, -0.1, 0))]
        meta.update(bails_broken_frame=tc+8, bails_m=dict(forward=round(-bails.y,3), left=round(bails.x,3), up=round(bails.z,3)),
                    note="takes the ball on the %s side and whips the gloves onto the bails, then appeals" % side)
        n = tc+65
    else:
        give = P(at, **hands(C_ + V((-0.05*sx, 0.18, 0.15)), "cup", fd=V((0,-0.3,-0.9))), hips=V(at["hips"])+V((0,0.02,0.05)))
        keys += [(tc+7, give), (tc+26, place(WITH_BALL, base_x, 0.0)), (tc+44, place(WITH_BALL, base_x, 0.0))]
        meta.update(note="standing up: rises with the ball and takes it on the %s side" % side)
        n = tc+45
    clip(name, n, keys, meta)
take_up("wk_take_up_offside", V((0.42, -0.50, 0.45)), side="off")
take_up("wk_take_up_legside", V((-0.82, -0.45, 0.45)), side="leg")
take_up("wk_stumping_offside", V((0.42, -0.50, 0.45)), side="off", stump=True)
take_up("wk_stumping_legside", V((-0.82, -0.45, 0.45)), side="leg", stump=True)

# ================================================================= run-out: take the throw over the stumps and break them
def runout(name, from_dir, note):
    """keeper behind the stumps, ball thrown in from the field (from_dir = heading the throw comes from)"""
    h = from_dir; F = fwd(h)
    sx, sy = STUMPS.x, STUMPS.y
    pos = V((sx, sy + 0.55, 0))                                         # keeper ~0.5 m behind the stumps
    stand = P(place(P(STAND, hips=H(0, 0.02, 0.92), lean=14, rball=V((-0.25,-0.02,0)), lball=V((0.25,-0.02,0))), pos.x, pos.y, 0),
              pyaw=h*0.35, **GLOVES_OPEN, look=V((pos.x, pos.y, 1.0)) + F*20)
    C_ = V((sx, sy + 0.05, 0.95)) + V((F.x, F.y, 0))*0.25
    ready = P(stand, **hands(C_ + V((F.x, F.y, 0))*0.10, "reverse", fd=V((0,0,1)), pd=(F + V((0,0,0.1))).normalized()))
    catch = P(ready, **hands(C_, "reverse", fd=V((0,0,1)), pd=(F + V((0,0,0.1))).normalized()), **HOLD_R, **HOLD_L, look=C_)
    bails = V((sx, sy, BAIL_Z - 0.02))
    down = P(catch, **hands(bails, "custom", fd=V((0,-0.3,-1)), pd=V((0,-1,0))), hips=V(stand["hips"]) + V((0,-0.05,-0.12)), lean=26, look=bails)
    after = P(down, hips=V(stand["hips"]) + V((0, 0, -0.04)), **hands(bails + V((0, 0.15, 0.25)), "cup"), look=V((pos.x, pos.y - 6, 1.6)))
    keys = [(0, stand), (14, stand), (24, ready), (30, catch), (34, down), (40, after), (60, place(WITH_BALL, pos.x, pos.y)), (74, place(WITH_BALL, pos.x, pos.y))]
    clip(name, 75, keys, ev("run-out take", 30, C_, dict(bails_broken_frame=35, throw_comes_from_heading_deg=h,
         bails_m=dict(forward=round(-bails.y,3), left=round(bails.x,3), up=round(bails.z,3)), note=note)))
runout("wk_runout_take_throw_from_front", 0, "takes a throw coming straight in from in front of the wicket and breaks the stumps")
runout("wk_runout_take_throw_from_offside", 60, "takes a throw coming in from the off side (keeper's left) and breaks the stumps")
runout("wk_runout_take_throw_from_legside", -60, "takes a throw coming in from the leg side (keeper's right) and breaks the stumps")
# diving at the stumps with the ball
def runout_dive():
    pos = V((STUMPS.x + 1.6, STUMPS.y + 0.9, 0))
    st = place(P(WITH_BALL, rball=V((-0.2,-0.02,0)), lball=V((0.2,-0.02,0))), pos.x, pos.y, 50)
    bails = V((STUMPS.x + 0.05, STUMPS.y, BAIL_Z - 0.04))
    air = P(st, hips=H(STUMPS.x + 0.66, STUMPS.y + 0.40, 0.55), pyaw=60, ptilt=55, footup=0.3,
            rball=V((pos.x-0.3, pos.y+0.1, 0.35)), lball=V((pos.x-0.1, pos.y-0.1, 0.30)), rheel=60, lheel=60,
            **hands(bails + V((0.25, 0.10, 0.10)), "custom", fd=V((-0.8,-0.4,-0.3)), pd=V((-1,0,-0.3))), **HOLD_R, look=bails)
    hit = P(air, hips=H(STUMPS.x + 0.46, STUMPS.y + 0.30, 0.32), ptilt=80, **hands(bails, "custom", fd=V((-0.8,-0.4,-0.3)), pd=V((-1,0,-0.3))))
    land = P(hit, hips=H(STUMPS.x + 0.44, STUMPS.y + 0.32, 0.18), ptilt=88, rball=V((pos.x-0.6, pos.y-0.1, 0.08)), lball=V((pos.x-0.5, pos.y-0.3, 0.08)),
             **hands(V((STUMPS.x + 0.12, STUMPS.y + 0.02, 0.22)), "custom", fd=V((-0.8,-0.4,0)), pd=V((0,0,-1))))
    kneel = P(FC.READY, hips=H(STUMPS.x + 0.7, STUMPS.y + 0.55, 0.55), pyaw=60, ptilt=15, rball=V((STUMPS.x+1.1, STUMPS.y+0.9, 0)), rheel=75, rkp=V((0,0.3,-1)),
              lball=V((STUMPS.x+0.45, STUMPS.y+0.30, 0)), rt=V((STUMPS.x+0.5, STUMPS.y+0.3, 0.8)), lt=V((STUMPS.x+0.65, STUMPS.y+0.25, 0.8)),
              look=V((STUMPS.x - 5, STUMPS.y - 5, 2.0)))
    keys = [(0, st), (6, st), (16, air), (20, hit), (26, land), (40, land), (56, kneel), (74, place(WITH_BALL, STUMPS.x + 0.8, STUMPS.y + 0.6, 50)), (84, place(WITH_BALL, STUMPS.x + 0.8, STUMPS.y + 0.6, 50))]
    clip("wk_runout_dive_at_stumps", 85, keys, dict(type="run-out", bails_broken_frame=21, bails_m=dict(forward=round(-bails.y,3), left=round(bails.x,3), up=round(bails.z,3)),
         note="already holding the ball 1.8 m from the stumps: dives and breaks the stumps with the ball in the gloves, gets up"))
runout_dive()

# ================================================================= appeal and returning the ball
def appeal():
    C_ = V((0.05, -0.52, 0.80))
    keys = [(0, CROUCH_BACK), (8, CROUCH_BACK), (20, half(0, 0, 0.78)), (25, P(half(0, 0, 0.78), **hands(C_+V((0,-0.1,0.03)), "cup"))),
            (30, P(half(0, 0, 0.78), **hands(C_, "cup"), **HOLD_R, **HOLD_L, look=C_)),
            (38, P(STAND, hips=H(0, -0.05, 1.02), **hands(V((0, -0.25, 1.30)), "reverse"), **HOLD_R, **HOLD_L, look=V((0, -18, 2.0)))),
            (46, P(STAND, hips=H(0, -0.10, 1.30), footup=0.6, rball=V((-0.12,-0.10,0.25)), lball=V((0.12,-0.12,0.28)), rheel=60, lheel=60,
                   **hands(V((0.0, -0.30, 2.10)), "reverse"), **HOLD_R, **HOLD_L, look=V((0, -18, 3.0)))),
            (54, P(STAND, hips=H(0, -0.12, 0.98), **hands(V((-0.05, -0.30, 1.85)), "reverse"), **HOLD_R, **HOLD_L, look=V((0, -18, 2.5)))),
            (72, P(STAND, hips=H(0, -0.12, 1.02), **hands(V((-0.05, -0.30, 1.85)), "reverse"), **HOLD_R, **HOLD_L, look=V((0, -18, 2.2)))),
            (90, place(WITH_BALL, 0, -0.12)), (100, place(WITH_BALL, 0, -0.12))]
    clip("wk_take_and_appeal", 101, keys, ev("take + appeal", 30, C_, dict(appeal_frame=47, note="takes the ball, springs up with both gloves raised ('Howzat!'), holds the appeal")))
appeal()
def return_ball():
    tgt = (0.0, -18.0)
    base = place(WITH_BALL, 0, 0)
    cock = P(base, pyaw=-35, rt=V((-0.40, 0.05, 1.30)), rp=V((-0.6,0.5,-0.6)), lt=V((0.15,-0.40,1.15)), **HOLD_R, look=V((0,-18,1.3)))
    rel = P(base, pyaw=-5, ptilt=8, rt=V((-0.15, -0.60, 1.35)), rh=V((0,-1,0.3)), rhw=0.8, **fing("r", GLOVE), look=V((0,-18,1.3)))
    fol = P(rel, rt=V((0.10, -0.55, 1.00)), rhw=0.2)
    keys = [(0, base), (6, base), (14, cock), (19, rel), (25, fol), (40, P(STAND, look=V((0,-18,1.2)), **GLOVES_OPEN)), (49, P(STAND, look=V((0,-18,1.2)), **GLOVES_OPEN))]
    clip("wk_return_ball_to_bowler", 50, keys, dict(type="throw", release_frame=20, throw_target_m=dict(forward=18, left=0), throw_speed_m_s=14,
         note="short flat flick back to the bowler after a take"))
return_ball()

if __name__ == "__main__":
    names = sys.argv[1:] or list(CLIPS)
    data, metas = {}, {}
    for nm in names:
        n, keys, meta, ov, loop, reach = CLIPS[nm]
        frames = make_field(nm, n, keys, overlay=ov, loop=loop, lift=True, reach=(lambda f: True) if reach else None)
        e = make_field.last_errs; evf = meta.get("event_frame")
        data[nm] = frames; metas[nm] = dict(file=nm+".fbx", frames=n, **meta)
        print(nm, "ok max hand err %.3f" % max(e), ("event err %.3f" % e[evf-1]) if evf else "", flush=True)
    pickle.dump(data, open(_os.path.join(APL_WORK, "wk_frames.pkl"),"wb")); json.dump(metas, open(_os.path.join(APL_WORK, "wk_meta.json"),"w"), indent=1)
